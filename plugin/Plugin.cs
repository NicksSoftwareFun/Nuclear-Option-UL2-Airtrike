using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace UL2Airtrike
{
    // Carries the UL-2 Airtrike bundle (embedded .nobp, found and loaded by Blueprinter) and spins the rotor.
    // Why a plugin: the rotor blades are fixed lifting parts (the game has no autorotating rotor for fixed-wing aircraft),
    // so the spinning blades are a visual copy turned here with airspeed. The only stock spinner, ConstantSpeedProp,
    // is engine driven and would make thrust. When a blade section breaks off, its spinning copy is hidden and the
    // broken part shows its own mesh.
    [BepInPlugin("nick.nuclearoption.ul2airtrike", "UL-2 Airtrike", "1.0.0")]
    [BepInDependency("com.nikkorap.blueprinter", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        private void Awake()
        {
            Log = Logger;
            try
            {
                var ac = AccessTools.TypeByName("Aircraft");
                var start = ac?.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         ?? ac?.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (start == null) { Logger.LogWarning("UL-2: Aircraft.Start not found, rotor spin off"); return; }
                var h = new Harmony("nick.nuclearoption.ul2airtrike");
                h.Patch(start, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostStart)));
                // open crew seats: a bail-out (no ejection seat) parks the generic pilot's ejection seat in the cockpit,
                // where it shows over the UL-2's own seats (Nick 2026-11-12 21:46), so hide it on UL-2 cockpits
                var bail = AccessTools.Method(AccessTools.TypeByName("EjectionSeat"), "BailOut");
                if (bail != null) h.Patch(bail, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostBailOut)));
                // prop RPM loop below full throttle (Nick 22:09): log what the strike detector touches
                var trig = AccessTools.Method(AccessTools.TypeByName("PropStrikeDetector"), "OnTriggerStay");
                if (trig != null) h.Patch(trig, prefix: new HarmonyMethod(typeof(Plugin), nameof(PreStrike)));
                // 0.15.34 AI pitch limiter: applied just before the game filters the AI's inputs, so the tail surfaces and
                // our rotor head both see the limited pitch
                var filt = AccessTools.Method(ac, "FilterInputs");
                if (filt != null) h.Patch(filt, prefix: new HarmonyMethod(typeof(Plugin), nameof(PreFilter)));
                // 0.15.43 (Nick 01:05): the nose gun is a fixed gun. The stock turret in manual mode with no target locked
                // points along the cockpit camera (head tracked); for our nose gun only, that vector is replaced by the gun's
                // own dead-ahead line. With a target locked the stock aim solver still leads it inside the +-5 deg travel.
                // 0.15.44: SetVector is a one-line setter the JIT inlines into FixedUpdate, so its patch never ran; patch the
                // manual aim step itself (AimTurret(Vector3), only used in manual mode with no target)
                var aimM = AccessTools.Method(AccessTools.TypeByName("Turret"), "AimTurret", new[] { typeof(Vector3) });
                if (aimM != null) h.Patch(aimM, prefix: new HarmonyMethod(typeof(Plugin), nameof(PreAimManual)));
                Logger.LogInfo("UL-2 nose gun fixed-aim patch " + (aimM != null));
                // 0.15.84 (Nick 22:51): passenger module machine gunners swing the gun OVER the stub grenadier's head
                var tTur = AccessTools.TypeByName("Turret");
                var aimS = AccessTools.Method(tTur, "AimTurret", new[] { AccessTools.TypeByName("WeaponStation") });
                if (aimM != null) h.Patch(aimM, prefix: new HarmonyMethod(typeof(PmArc), nameof(PmArc.Pre)), postfix: new HarmonyMethod(typeof(PmArc), nameof(PmArc.Post)));
                if (aimS != null) h.Patch(aimS, prefix: new HarmonyMethod(typeof(PmArc), nameof(PmArc.Pre)), postfix: new HarmonyMethod(typeof(PmArc), nameof(PmArc.Post)));
                Logger.LogInfo("UL-2 passenger MG over-head patch " + (aimM != null) + "/" + (aimS != null));
                // 0.15.54 (Nick 20:59): the stock combat AI climbs its target height 5 m a second up to 200 m over the ground.
                // For the UL-2 the target height is held low so it masks behind terrain; with the spotter aboard it may go higher.
                var alt = AccessTools.Method(AccessTools.TypeByName("AIPilotCombatModes"), "ManageAltitude");
                if (alt != null) h.Patch(alt, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostAltitude)));
                // 0.15.54b (Nick 23:03): the low cap only runs for 5 minutes after a missile alert or while attacking a target
                var alert = AccessTools.Method(AccessTools.TypeByName("AIPilotCombatModes"), "AICombat_OnMissileAlert");
                if (alert != null) h.Patch(alert, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostMissileAlert)));
                Logger.LogInfo("UL-2 combat altitude patch " + (alt != null) + ", missile alert " + (alert != null));
                // 0.15.56 (Nick 23:14): tactics on top of the stock combat AI, applied after it picks its steering each tick:
                // pop-up missile attack, beaming a radar that locks it, and the spotter's standoff loiter
                var tCm = AccessTools.TypeByName("AIPilotCombatModes");
                var run = AccessTools.Method(tCm, "RunAttackMode");
                if (run != null) h.Patch(run, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostRunAttack)));
                var rw = AccessTools.Method(tCm, "AICombat_OnRadarWarning");
                if (rw != null) h.Patch(rw, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostRadarWarning)));
                Logger.LogInfo("UL-2 tactics patches: attack " + (run != null) + ", radar warning " + (rw != null));
                // 0.15.63 (Nick 03:01, AI aborting landings like the PB-22 and P220 did): same fix as theirs
                LandingFix.Patch(h);
                // 0.15.86 (Nick 23:16): slow climb-out sat on the runway centreline in the way of other traffic; side-step off it
                TakeoffFix.Patch(h);
                // 0.15.64 (Nick 03:07, Lynchpins all missing): only fire laser-guided rockets once the nose laser is on the target
                LaserFix.Patch(h);
                // passenger module (Nick 2026-11-16 21:39): one mount with four turrets. Stock SpawnMount attaches only the first
                // turret of a turret mount to the weapon manager; attach the rest. The weapon manager groups them by weapon info,
                // so the two machine guns fire as one weapon and the two grenade launchers as another.
                var spawnM = AccessTools.Method(AccessTools.TypeByName("Hardpoint"), "SpawnMount");
                if (spawnM != null) h.Patch(spawnM, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostSpawnMount)));
                var remM = AccessTools.Method(AccessTools.TypeByName("Hardpoint"), "RemoveMount");
                if (remM != null) h.Patch(remM, prefix: new HarmonyMethod(typeof(Plugin), nameof(PreRemoveMount)));
                Logger.LogInfo("UL-2 passenger module turret patch " + (spawnM != null));
                // 0.15.84 (Nick 22:47): the stock WeaponMount.Initialize names a gun mount "<first gun's weapon> (N rounds)" and swaps
                // the mount's info for the first gun's. Keep the team's own name, description and icon in the loadout menu.
                var wmInit = AccessTools.Method(AccessTools.TypeByName("WeaponMount"), "Initialize");
                if (wmInit != null) h.Patch(wmInit, prefix: new HarmonyMethod(typeof(Plugin), nameof(PreMountInit)), postfix: new HarmonyMethod(typeof(Plugin), nameof(PostMountInit)));
            }
            catch (Exception e) { Logger.LogError("UL-2 patch failed: " + e); }
        }
        static FieldInfo fMountInfo, fMountName;
        static void PreMountInit(object __instance, out object __state)
        {
            __state = null;
            try
            {
                if (!(__instance is UnityEngine.Object o) || o.name != "UL2_pm") return;
                fMountInfo = fMountInfo ?? AccessTools.Field(__instance.GetType(), "info");
                __state = fMountInfo.GetValue(__instance);
            }
            catch (Exception e) { Log.LogError("UL-2 passenger module name: " + e); }
        }
        static void PostMountInit(object __instance, object __state)
        {
            try
            {
                if (__state == null) return;
                fMountName = fMountName ?? AccessTools.Field(__instance.GetType(), "mountName");
                fMountInfo.SetValue(__instance, __state);
                fMountName.SetValue(__instance, "Special Operations Team");
            }
            catch (Exception e) { Log.LogError("UL-2 passenger module name: " + e); }
        }
        static MethodInfo mAttachTurret;
        // 0.15.84b (Nick 23:01: backflip on the takeoff pitch-up): the stock hardpoint puts the whole mount mass on its part (the
        // seat 3 part, z -0.55, almost over the main wheels), but two of the four soldiers sit on the stubs at z +0.40. 60 % of the
        // team's empty mass moves to the rear cockpit part (z +0.23), so the module's mass centre sits near its real one (z -0.08);
        // the ammo stays on the seat part. Put back when the mount is removed.
        const float PM_FWD = 0.6f;
        static readonly Dictionary<object, KeyValuePair<Component, float>> pmShift = new Dictionary<object, KeyValuePair<Component, float>>();
        static FieldInfo fHpPart, fHpMount, fMountEmpty; static MethodInfo mPartMass, mAllParts;
        static void PmMass(object hp, object aircraft, bool on)
        {
            try
            {
                var tHp = hp.GetType(); fHpPart = fHpPart ?? AccessTools.Field(tHp, "part"); fHpMount = fHpMount ?? AccessTools.Field(tHp, "mount");
                var part = fHpPart.GetValue(hp) as Component; if (part == null) return;
                mPartMass = mPartMass ?? AccessTools.Method(part.GetType(), "ModifyMass");
                object id = hp; KeyValuePair<Component, float> kv;
                if (!on)
                {
                    if (!pmShift.TryGetValue(id, out kv)) return; pmShift.Remove(id);
                    if (kv.Key != null) mPartMass.Invoke(kv.Key, new object[] { -kv.Value });
                    mPartMass.Invoke(part, new object[] { kv.Value });
                    return;
                }
                if (pmShift.ContainsKey(id)) return;
                var mount = fHpMount.GetValue(hp); if (mount == null) return;
                fMountEmpty = fMountEmpty ?? AccessTools.Field(mount.GetType(), "emptyMass");
                float m = (float)fMountEmpty.GetValue(mount) * PM_FWD;
                mAllParts = mAllParts ?? AccessTools.Method(aircraft.GetType(), "GetAllParts");
                Component fwd = null;
                if (mAllParts.Invoke(aircraft, null) is System.Collections.IEnumerable all) foreach (var o in all) if (o is Component c && c != null && c.name == "cockpit_R") { fwd = c; break; }
                if (fwd == null) { Log.LogWarning("UL-2 passenger module: no cockpit_R part, mass left on " + part.name); return; }
                mPartMass.Invoke(part, new object[] { -m }); mPartMass.Invoke(fwd, new object[] { m });
                pmShift[id] = new KeyValuePair<Component, float>(fwd, m);
                Log.LogInfo("UL-2 passenger module: " + m.ToString("0") + " kg moved from " + part.name + " to " + fwd.name);
            }
            catch (Exception e) { Log.LogError("UL-2 passenger module mass: " + e); }
        }
        static void PreRemoveMount(object __instance) { if (__instance != null && pmShift.ContainsKey(__instance)) PmMass(__instance, null, false); }
        static void PostSpawnMount(object aircraft, GameObject __result, object __instance)
        {
            try
            {
                if (__result == null || !__result.name.StartsWith("UL2_pm")) return;
                PmMass(__instance, aircraft, true);
                var tT = AccessTools.TypeByName("Turret");
                mAttachTurret = mAttachTurret ?? AccessTools.Method(tT, "AttachToWeaponManager");
                var ts = __result.GetComponentsInChildren(tT, true);
                for (int i = 1; i < ts.Length; i++) mAttachTurret.Invoke(ts[i], new[] { aircraft });
                Log.LogInfo("UL-2 passenger module: " + ts.Length + " turrets, attached " + Math.Max(0, ts.Length - 1) + " more");
            }
            catch (Exception e) { Log.LogError("UL-2 passenger module attach: " + e); }
        }
        static readonly System.Collections.Generic.Dictionary<int, Transform> noseGunBase = new System.Collections.Generic.Dictionary<int, Transform>();
        static void PreAimManual(Component __instance, ref Vector3 aimVector)
        {
            try
            {
                if (__instance == null) return;
                int id = __instance.GetInstanceID();
                if (!noseGunBase.TryGetValue(id, out var bt))
                {
                    bt = null;   // the mount root of our nose gun (UL2_sidegun), never the seat 3 gunner turret
                    var path = "";
                    for (var t = __instance.transform.parent; t != null; t = t.parent)
                    {
                        path += t.name + "/";
                        if (t.name.StartsWith("UL2_sidegun") || t.name == "gunmount_R_hp") { bt = t; break; }
                    }
                    noseGunBase[id] = bt;
                    if (bt != null || path.Contains("UL2Airtrike")) Log.LogInfo("UL-2 manual turret " + __instance.name + " under " + path + " -> fixed " + (bt != null));
                }
                if (bt != null) aimVector = bt.forward * Mathf.Max(aimVector.magnitude, 1000f);
            }
            catch (Exception e) { Log.LogError("UL-2 nose gun: " + e); }
        }
        static FieldInfo fParentUnit;
        static void PostBailOut(Component __instance, Rigidbody cockpit)
        {
            try
            {
                if (__instance == null || cockpit == null) return;
                var up = cockpit.GetComponent(AccessTools.TypeByName("UnitPart"));
                if (up == null) return;
                fParentUnit = fParentUnit ?? AccessTools.Field(up.GetType(), "parentUnit");
                var unit = fParentUnit?.GetValue(up) as Component;
                if (unit == null || !unit.name.StartsWith("UL2Airtrike")) return;
                foreach (var r in __instance.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (var c in __instance.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                Log.LogInfo("UL-2 bail-out: parked ejection seat hidden");
            }
            catch (Exception e) { Log.LogError("UL-2 bail-out: " + e); }
        }
        internal static readonly System.Collections.Generic.HashSet<int> OurDetectors = new System.Collections.Generic.HashSet<int>();
        static float strikeLogT;
        static void PreStrike(Component __instance, Collider other)
        {
            try
            {
                if (__instance == null || other == null || !OurDetectors.Contains(__instance.GetInstanceID())) return;
                if (Time.time - strikeLogT < 0.5f) return; strikeLogT = Time.time;
                Log.LogInfo("UL-2 prop strike detector touched " + other.name + " (" + other.transform.root.name + ", layer " + other.gameObject.layer + ", trigger " + other.isTrigger + ")");
            }
            catch { }
        }
        static void PreFilter(Component __instance)
        {
            try
            {
                if (__instance == null || !__instance.name.StartsWith("UL2Airtrike")) return;
                var rs = __instance.GetComponent<RotorSpinner>(); if (rs != null) rs.AiFilter();
            }
            catch { }
        }
        internal static FieldInfo fTh, fStAc, fCurT;
        static RotorSpinner UL2Of(object state)
        {
            fStAc = fStAc ?? AccessTools.Field(state.GetType(), "aircraft");
            var ac = fStAc?.GetValue(state) as Component;
            if (ac == null || !ac.name.StartsWith("UL2Airtrike")) return null;
            return ac.GetComponent<RotorSpinner>();
        }
        static void PostMissileAlert(object __instance)
        {
            try { var rs = UL2Of(__instance); if (rs != null) rs.GoLow("missile alert"); } catch { }
        }
        // ---------------------------------------------------------------- 0.15.56 tactics (Nick 23:14 / 23:15)
        internal static FieldInfo fMode, fEvade, fDest, fTkp, fWi, fObsc, fFollow, fLastFired, fName, fReq, fMaxR, fMissile, fGx, fGy, fGz;
        internal static MethodInfo mGlobal; internal static Type tGP; static bool tacFailed;
        // stock GlobalPosition is an extension method (GlobalPositionExtensions.GlobalPosition(Transform)), not a Unit method
        internal static Vector3 GlobalOf(Component c) { return GP(mGlobal.Invoke(null, new object[] { c.transform })); }
        internal static Vector3 GP(object g) { return new Vector3((float)fGx.GetValue(g), (float)fGy.GetValue(g), (float)fGz.GetValue(g)); }
        internal static object MakeGP(Vector3 v) { var g = Activator.CreateInstance(tGP); fGx.SetValue(g, v.x); fGy.SetValue(g, v.y); fGz.SetValue(g, v.z); return g; }
        static bool TacInit(object st)
        {
            if (fMode != null) return true;
            if (tacFailed) return false;   // 0.15.62: look up once; a failed lookup retried every frame dropped the game to single-digit fps
            var t = st.GetType();
            fMode = AccessTools.Field(t, "attackMode"); fEvade = AccessTools.Field(t, "evadeMode"); fDest = AccessTools.Field(t, "destination");
            fTkp = AccessTools.Field(t, "targetKnownPosition"); fWi = AccessTools.Field(t, "currentWeaponInfo"); fObsc = AccessTools.Field(t, "targetObscured");
            fFollow = AccessTools.Field(t, "followTerrain"); fLastFired = AccessTools.Field(t, "lastFiredTime"); fName = AccessTools.Field(t, "stateDisplayName");
            fCurT = fCurT ?? AccessTools.Field(t, "currentTarget"); fTh = fTh ?? AccessTools.Field(t, "targetHeight");
            tGP = AccessTools.TypeByName("GlobalPosition"); fGx = AccessTools.Field(tGP, "x"); fGy = AccessTools.Field(tGP, "y"); fGz = AccessTools.Field(tGP, "z");
            mGlobal = AccessTools.Method(AccessTools.TypeByName("GlobalPositionExtensions"), "GlobalPosition", new[] { typeof(Transform) });
            var tWi = AccessTools.TypeByName("WeaponInfo"); fReq = AccessTools.Field(tWi, "targetRequirements"); fMissile = AccessTools.Field(tWi, "missile");
            fMaxR = AccessTools.Field(AccessTools.TypeByName("TargetRequirements"), "maxRange");
            bool ok = fMode != null && fEvade != null && fDest != null && fTkp != null && fName != null && mGlobal != null && fGx != null && fMaxR != null;
            Log.LogInfo("UL-2 tactics fields " + ok + (fDest == null ? " (no destination field)" : ""));
            if (!ok) { fMode = null; tacFailed = true; }
            return ok;
        }
        static void PostRadarWarning(object __instance, object source)
        {
            try
            {
                var rs = UL2Of(__instance); if (rs == null || source == null) return;
                var t = source.GetType();
                if (!(bool)t.GetField("detected").GetValue(source) || !(bool)t.GetField("isTarget").GetValue(source)) return;
                var em = t.GetField("emitter").GetValue(source) as Component; if (em == null) return;
                if (!TacInit(__instance)) return;
                rs.RadarLock(GlobalOf(em));
            }
            catch (Exception e) { Log.LogWarning("UL-2 radar warning: " + e.Message); }
        }
        static void PostRunAttack(object __instance)
        {
            try
            {
                var rs = UL2Of(__instance); if (rs == null || !TacInit(__instance)) return;
                string mode = fMode.GetValue(__instance).ToString(), ev = fEvade.GetValue(__instance).ToString();
                string name = fName.GetValue(__instance) as string;
                if (name != null && !name.StartsWith("UL-2: ")) rs.stockName = name;      // what the stock AI last showed
                string tac = ev == "None" ? rs.Tactic(__instance, mode) : null;
                fName.SetValue(__instance, tac != null ? "UL-2: " + tac : rs.stockName);  // spectator status box
            }
            catch (Exception e) { Log.LogWarning("UL-2 tactics: " + e.Message); fMode = null; tacFailed = true; }
        }
        const float LASE_ALT = 150f; static FieldInfo fWiA, fLg;
        static void PostAltitude(object __instance)
        {
            try
            {
                var rs = UL2Of(__instance); if (rs == null) return;
                // attacking: a live target keeps the low window open; with nothing going on it flies the stock way
                fCurT = fCurT ?? AccessTools.Field(__instance.GetType(), "currentTarget");
                var tgt = fCurT?.GetValue(__instance) as UnityEngine.Object;
                if (tgt != null) rs.GoLow("attacking a target");
                if (!rs.LowMode || rs.PopUp) return;
                fTh = fTh ?? AccessTools.Field(__instance.GetType(), "targetHeight");
                // 0.15.66 (Nick 03:38, Lynchpins still missing with the laser on at launch): a laser rocket needs a clear line
                // from itself to the spot the whole way in (LaserSeeker.TrackLaser). Fired from the 35 m cap it skims the ground,
                // loses the line behind every rise and flies into the dirt. With a laser-guided weapon selected the UL-2 runs in
                // and lases from 150 m like the stock gunships, instead of the low cap.
                fWiA = fWiA ?? AccessTools.Field(__instance.GetType(), "currentWeaponInfo");
                var wiA = fWiA?.GetValue(__instance);
                if (wiA != null) { fLg = fLg ?? AccessTools.Field(wiA.GetType(), "laserGuided"); if (fLg != null && (bool)fLg.GetValue(wiA)) { if ((float)fTh.GetValue(__instance) < LASE_ALT) fTh.SetValue(__instance, LASE_ALT); return; } }
                float th = (float)fTh.GetValue(__instance), cap = rs.SpotterAboard ? RotorSpinner.CAP_SPOT : RotorSpinner.CAP_LOW;
                if (th > cap) fTh.SetValue(__instance, cap);
            }
            catch { }
        }
        static void PostStart(Component __instance)
        {
            try
            {
                if (__instance == null || !__instance.name.StartsWith("UL2Airtrike")) return;
                if (__instance.GetComponent<RotorSpinner>() == null) __instance.gameObject.AddComponent<RotorSpinner>();
                if (__instance.GetComponent<XBombHold>() == null) __instance.gameObject.AddComponent<XBombHold>();
            }
            catch (Exception e) { Log.LogError("UL-2 rotor: " + e); }
        }
    }

    // 0.15.38 (Nick 23:37/23:38): event content "Expedient 1.5kt Bomb" in seat 3. Why a plugin: no stock mount keeps a
    // fired weapon on the aircraft. When the posed bomb on the seat 3 mount is fired (the stock MountedMissile hides it and
    // spawns the bomb at its exact pose), the spawned bomb is held kinematic on that pose until its own fuse (bundle copy:
    // arm 6 s, altitude fuse 100 km = goes off as it arms) detonates it, with a timer tick that speeds up toward the end.
    // If the airframe loses the seat the bomb is let go and still goes off on time.
    public class XBombHold : MonoBehaviour
    {
        Transform mounted; bool wasActive; float scanT, clock, searchUntil = -1f, nextTick;
        Rigidbody held; AudioSource tick; float heldT0;
        static readonly HashSet<int> claimed = new HashSet<int>();
        void Update()
        {
            try
            {
                float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f; clock += dt;
                if (mounted == null)
                {
                    if (clock < scanT) return; scanT = clock + 2f;
                    if (clock > 30f) { enabled = false; return; }   // 0.15.84: the bomb is mounted at spawn; stop searching every part's transforms
                    // 0.15.45: the parts detach from the aircraft root at spawn (seat 3 rides on its own part), so search the
                    // unit's part list (RotorSpinner keeps it), not the root's children
                    var rs = GetComponent<RotorSpinner>();
                    var roots = new List<Transform> { transform };
                    if (rs != null) foreach (var c in rs.partList) if (c != null) roots.Add(c.transform);
                    foreach (var r in roots) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == "UL2_xbomb_mounted") { mounted = t; break; } if (mounted != null) break; }
                    if (mounted != null) { wasActive = mounted.gameObject.activeInHierarchy; Plugin.Log.LogInfo("UL-2 expedient bomb on seat 3, watching for the fire"); }
                    return;
                }
                bool act = mounted.gameObject.activeInHierarchy;
                if (wasActive && !act && held == null) searchUntil = clock + 2f;
                if (!wasActive && act) { held = null; tick = null; }
                wasActive = act;
                if (searchUntil > clock && held == null)
                {
                    foreach (var rb in FindObjectsOfType<Rigidbody>())
                    {
                        if (rb == null || !rb.name.StartsWith("UL2_xbomb") || claimed.Contains(rb.GetInstanceID())) continue;
                        if ((rb.position - mounted.position).sqrMagnitude > 400f) continue;
                        held = rb; claimed.Add(rb.GetInstanceID()); heldT0 = clock; nextTick = clock; searchUntil = -1f;
                        var tt = rb.transform.Find("UL2_tick"); tick = tt != null ? tt.GetComponent<AudioSource>() : null;
                        Plugin.Log.LogInfo("UL-2 expedient bomb fired: held on seat 3, tick " + (tick != null));
                        break;
                    }
                }
                if (held != null && tick != null && tick.clip != null && clock >= nextTick)
                {
                    float e = clock - heldT0;
                    tick.PlayOneShot(tick.clip);
                    nextTick += e < 3f ? 1f : e < 5f ? 0.5f : 0.2f;
                }
            }
            catch (Exception e) { Plugin.Log.LogError("UL-2 xbomb: " + e); enabled = false; }
        }
        // 0.15.46 (Nick 02:29: it slid back a foot into the engine when fired): the bomb rigidbody interpolates, so a pose set
        // in the physics step drew a step behind the moving seat. Now it is parented to the seat 3 part with interpolation
        // off and its local pose re-set every step and frame, so it rides the part exactly.
        void Hold()
        {
            if (held == null) return;
            if (mounted == null || !mounted.gameObject.scene.IsValid()) { held.transform.SetParent(null, true); held.isKinematic = false; held = null; return; }
            if (!held.isKinematic) held.isKinematic = true;
            if (held.interpolation != RigidbodyInterpolation.None) held.interpolation = RigidbodyInterpolation.None;
            var par = mounted.parent;
            if (held.transform.parent != par) held.transform.SetParent(par, false);
            held.transform.localPosition = mounted.localPosition; held.transform.localRotation = mounted.localRotation;
            held.velocity = Vector3.zero; held.angularVelocity = Vector3.zero;
        }
        void FixedUpdate() { try { Hold(); } catch { held = null; } }
        void LateUpdate() { try { Hold(); } catch { held = null; } }
    }

    public class RotorSpinner : MonoBehaviour
    {
        static FieldInfo fSpeed, fParts; static MethodInfo mDetached;
        Component unit; Transform spin; Component headPart;
        readonly List<KeyValuePair<Component, Transform>> blades = new List<KeyValuePair<Component, Transform>>();
        float rpm; bool ready; int tries;
        // 0.15.53 (Nick 19:28): spawned on the ground, both blades start folded forward over the nose like a shipped
        // airframe, wait 2 s, then swing out to the flying position over 3 s; the rotor is held still until they lock out.
        // Mirrors the stock RotorShaft rotor hinges (Ibis): hinge euler = foldAngle * SmoothStep(foldAmount), unfold after a
        // delay, rotor locked until unfolded. RotorShaft itself is the helicopter engine, so the gyro rotor can't use it.
        readonly Transform[] fold = new Transform[2]; readonly float[] foldAng = new float[2]; bool foldOn; float foldT;
        // 0.15.26 (Nick 04:56/04:59): rotor blur disc and blade strikes (stock SwashRotor way: a line along each blade)
        readonly List<GameObject> rblur = new List<GameObject>(); bool rblurOn; bool struck; readonly Vector3[] bladeDir = new Vector3[2];
        readonly HashSet<Collider> ownCols = new HashSet<Collider>(); float colScanT;
        const float ROTOR_R = 4.6f; readonly Component[,] wingParts = new Component[2, 3];
        // cockpit gauges (all values the game already has; oil and volts have no game signal and follow engine rpm)
        readonly Dictionary<string, Transform> gauges = new Dictionary<string, Transform>();
        readonly Dictionary<string, float> shown = new Dictionary<string, float>();
        readonly Dictionary<string, Quaternion> rest = new Dictionary<string, Quaternion>();
        Transform sSpine, sHead; Quaternion rSpine, rHead; float t0 = UnityEngine.Random.value * 100f;
        Component engine; static MethodInfo mRpm, mFuel; static FieldInfo fAlt; float lastY, vs; bool haveY;
        // pre-rotator, jump launch and rotor tilt (Nick 2026-11-12): inputs from the stock ControlInputs
        static MethodInfo mInputs; static FieldInfo fThr, fPitch, fRoll, fAxis, fYaw, fBrake; static PropertyInfo pRb;
        // rudder pedals (P220 / PB-22 way): UL2_pedal_L/R slide fore and aft on their floor rails with the yaw input
        readonly Dictionary<string, Transform> pedals = new Dictionary<string, Transform>();
        readonly Dictionary<string, Vector3> pedalRest = new Dictionary<string, Vector3>(); float yawIn, pedalPos, brakeIn;
        Transform tilt; float tiltX, tiltZ, thr, pitchIn, rollIn, axis, prevAxis, alt, er, jumpT = -1f, jumpE, massSum, massT;
        internal readonly List<Component> partList = new List<Component>(); float scanT, offGround, dbgT;

        static FieldInfo fFa; bool autoArmed = true, autoSpin;
        // 0.15.15 gyro model (Nick 01:28: takeoff deep dive). The game's wing lift is Cl(alpha) * q * area off each part's lift
        // transform; our blade surfaces sat at 0 deg incidence with a symmetric airfoil, so on a level ground roll the rotor made
        // no lift at all (the old roll-out push only gave 45 % of the weight). Now: blade incidence follows the rotor head
        // (disc tilt): 3 deg neutral, 12 deg stick fully back, -1 deg pushed, held below the stall like a real rotor; and the
        // blades only lift in proportion to rotor rpm, which builds with airspeed (autorotation) or the pre-rotator.
        class Blade { public Component part; public Transform ln; public float area; }
        readonly List<Blade> rotorBlades = new List<Blade>(); static MethodInfo mSetArea, mGetArea;
        float inc = 3f, rotorK, bodyAoA, logThrust; static MethodInfo mThrust;
        const float INC0 = 3f, INC_PULL = 9f, INC_PUSH = 4f, ROTOR_AOA_MAX = 13f;
        // 0.15.12 (Nick 00:05): copilot backup controls follow the inputs; his hands rest on the game controller (two-bone IK)
        Transform ctrl2, stick2, thr2, cop; Quaternion stick2Rest, thr2Rest; float stick2X, stick2Z, thr2A;
        readonly Dictionary<string, Transform> pedals2 = new Dictionary<string, Transform>();
        readonly Dictionary<string, Quaternion> pedal2Rest = new Dictionary<string, Quaternion>();
        readonly Transform[][] arms = new Transform[2][]; bool armsLogged;
        // 0.15.35 (Nick 22:04): front pilot's right hand on the centre stick, left hand on the throttle (third person)
        readonly Transform[][] parms = new Transform[2][]; Transform pil, jsT, thT, exStick, exThr; Vector3 jsWrist, thWrist; Quaternion jsHand, thHand; bool parmsLogged;
        readonly Transform[] slP = new Transform[2], slC = new Transform[2];   // 0.15.63 sleeve bridges (L, R)
        void Update()
        {
            try
            {
                if (!ready) { if (tries++ < 120) Setup(); return; }
                // game time reads 0 inside plugin components (seen on the PB-22 gauges): use unscaled time, frozen while paused
                float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
                float spd = fSpeed != null ? (float)fSpeed.GetValue(unit) : 0f;
                bool headOn = headPart == null || !Detached(headPart);
                Inputs();
                alt = fAlt != null ? (float)fAlt.GetValue(unit) : 99f;
                er = 0f; try { if (engine != null && mRpm != null) er = Mathf.Clamp01((float)mRpm.Invoke(engine, null)); } catch { }
                bool ground = alt < 0.8f, engOn = er > 0.05f;
                if (ground) offGround = 0f; else offGround += dt;
                bool clutch = offGround < 3f;          // Nick 20:07: custom axis 1 = pre-rotator, on the ground and for 3 s after takeoff
                // autorotation: about 9 rpm per m/s of airspeed, 380 rpm cap (~42 m/s); spins down over a few seconds
                // 0.15.15: a gyro rotor settles near its flying rpm once air flows up through it; about 18 rpm per m/s from 40,
                // 380 rpm by 19 m/s. Below 2 m/s it winds down unless the pre-rotator drives it
                float target = headOn && !struck && spd > 2f ? Mathf.Min(380f, 40f + 18f * spd) : 0f;
                float upRate = 60f;
                // pre-rotator (0.15.19, Nick 03:39: no jumps at all). On the ground (and 3 s after liftoff) with the engine running:
                // - custom axis = manual rotor rpm, 110 at the bottom of its travel to 450 at the top (overrides the assist)
                // - flight assist on, axis untouched = best rpm for the moment: full 450 at full throttle on the takeoff roll,
                //   otherwise it follows the throttle (110 idle to 300)
                // - flight assist off, axis untouched = no pre-spin. In the air the rotor autorotates from the airflow.
                bool fa = false; try { if (fFa == null) fFa = AccessTools.Field(unit.GetType(), "flightAssist"); if (fFa != null) fa = (bool)fFa.GetValue(unit); } catch { }
                // 0.15.75 (Nick 18:16): flight assist is "Auto RPM Management". Moving the custom axis switches it off (the game's
                // own toggle, with its popup) and the axis keeps the rotor until the player turns the assist back on; turning it
                // back on hands the rotor to the assist even with the axis left part way up, until the axis is moved again.
                if (!axisInit || (fa && !prevFa)) { axisHeld = axis; axisInit = true; }
                if (fa && Mathf.Abs(axis - axisHeld) > 0.02f)
                {
                    try { mSetFa = mSetFa ?? AccessTools.Method(unit.GetType(), "SetFlightAssist"); mSetFa?.Invoke(unit, new object[] { false }); } catch { }
                    try { fa = (bool)fFa.GetValue(unit); } catch { fa = false; }
                }
                if (!fa) axisHeld = axis;
                prevFa = fa;
                bool manual = !fa && axis > 0.05f;
                manualOn = manual;
                autoSpin = !manual && fa && ground && engOn && thr > 0.05f;
                // 0.15.47 (Nick 02:41): with flight assist OFF the custom axis is the rotor rpm in flight too (raise or lower it);
                // in the air it sets the rpm outright instead of only adding to the autorotation. Axis at the bottom = released,
                // back to autorotation. Flight assist on keeps the old behaviour (pre-rotator only on the ground and 3 s after).
                bool manualAir = manual && !fa && !clutch;
                // 0.15.51 (Nick 02:49): no automatic rotor braking. The drive (pre-rotator / manual axis in the air with flight
                // assist off) can only spin the rotor UP to its setpoint; above it the rotor runs free on the airflow (settles
                // toward the autorotation rpm, or winds down slowly with no airflow). Only the BRAKE button pulls it down: a
                // modest rotor brake toward the axis setpoint (axis at minimum = FREE: brake toward a stop), ground or air.
                float autoT = target, drive = 0f;
                if (headOn && engOn && !struck && (clutch || manualAir))
                {
                    if (manual) drive = 110f + 340f * axis;
                    else if (fa && ground) drive = thr > 0.95f ? 450f : Mathf.Lerp(110f, 300f, thr);
                    if (rpm > 250f) upRate = 30f;
                }
                target = Mathf.Max(autoT, drive);
                // 0.15.70 (Nick 17:03): the pre-rotator loads the engine; driving the rotor past 380 rpm takes it into the yellow
                preLoad = drive > 0f ? Mathf.Clamp01((rpm - 380f) / 70f) : 0f;
                float sp = manual ? 110f + 340f * axis : 0f;
                bool braking = headOn && brakeIn > 0.1f && rpm > sp + 0.5f;
                if (manual && (dbgT -= dt) <= 0f) { dbgT = 2f; Plugin.Log.LogInfo($"UL-2 pre-rotator axis {axis:0.00} rpm {rpm:0} alt {alt:0.00} engine {er:0.00} thr {thr:0.00} clutch {clutch} brake {braking}"); }
                prevAxis = axis;
                setRpm = manual ? sp : target;
                if (braking) rpm = Mathf.Max(sp, rpm - 35f * brakeIn * dt);                       // rotor brake ~35 rpm/s
                else rpm = Mathf.MoveTowards(rpm, target, (target > rpm ? upRate : autoT > 0f ? 40f : 6f) * dt);   // free: aero settling / slow wind-down
                if (foldOn)
                {
                    foldT += dt; float k = Mathf.SmoothStep(0f, 1f, (foldT - 2f) / 3f);
                    for (int i = 0; i < 2; i++) if (fold[i] != null) fold[i].localRotation = Quaternion.AngleAxis(foldAng[i] * (1f - k), Vector3.up);
                    rpm = 0f; if (k >= 1f) { foldOn = false; Plugin.Log.LogInfo("UL-2 rotor blades unfolded"); }
                }
                spin.localRotation *= Quaternion.Euler(0f, rpm * 6f * dt, 0f);
                RotorAudio(headOn);
                RotorBlur(headOn);
                if (headOn && !struck && rpm > 60f) RotorStrike();
                // rotor disc tilt with the stick (gyros steer by tilting the head); held back on the ground roll
                if (tilt != null)
                {
                    float tx = -(inc - INC0), tz = -6f * Mathf.Clamp(rollIn, -1f, 1f);     // the visible disc tilts with the blade incidence
                    tiltX = Mathf.Lerp(tiltX, tx, 1f - Mathf.Exp(-dt * 4f)); tiltZ = Mathf.Lerp(tiltZ, tz, 1f - Mathf.Exp(-dt * 4f));
                    tilt.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);
                }
                if ((scanT -= dt) <= 0f) { scanT = 1f; ScanKits(); }
                PropWatch(dt);
                Spinner(dt);
                LiverySync(dt);
                HudGauge(dt, engOn);
                if (alt < 40f) LeanLog(dt);
                Harnesses();
                Gauges(spd, dt);
                Soldier();
                for (int i = blades.Count - 1; i >= 0; i--)
                {
                    var b = blades[i];
                    if (b.Key == null || b.Value == null) { blades.RemoveAt(i); continue; }
                    if (!Detached(b.Key)) continue;
                    b.Value.gameObject.SetActive(false);                 // spinning copy goes
                    foreach (var r in b.Key.GetComponents<MeshRenderer>()) r.enabled = true;   // the broken piece shows
                    blades.RemoveAt(i);
                }
            }
            catch (Exception e) { Plugin.Log.LogError("UL-2 rotor: " + e); enabled = false; }
        }

        void Inputs()
        {
            try
            {
                if (mInputs == null) return;
                var ci = mInputs.Invoke(unit, null); if (ci == null) return;
                if (fThr == null) { var t = ci.GetType(); fThr = AccessTools.Field(t, "throttle"); fPitch = AccessTools.Field(t, "pitch"); fRoll = AccessTools.Field(t, "roll"); fAxis = AccessTools.Field(t, "customAxis1"); fYaw = AccessTools.Field(t, "yaw"); fBrake = AccessTools.Field(t, "brake"); Plugin.Log.LogInfo("UL-2 inputs " + t.Name + " throttle " + (fThr != null) + " customAxis1 " + (fAxis != null)); }
                thr = Mathf.Clamp01(fThr != null ? (float)fThr.GetValue(ci) : 0f);
                pitchIn = fPitch != null ? (float)fPitch.GetValue(ci) : 0f; rollIn = fRoll != null ? (float)fRoll.GetValue(ci) : 0f;
                // the axis reads 0..~0.45 on Nick's binding (log 21:53): 0.4 and up counts as full
                axis = Mathf.Clamp01((fAxis != null ? (float)fAxis.GetValue(ci) : 0f) / 0.4f);
                brakeIn = fBrake != null ? Mathf.Clamp01((float)fBrake.GetValue(ci)) : 0f;
                yawIn = fYaw != null ? Mathf.Clamp((float)fYaw.GetValue(ci), -1f, 1f) : 0f; if (float.IsNaN(yawIn)) yawIn = 0f;
            }
            catch (Exception e) { if (!inErr) { inErr = true; Plugin.Log.LogWarning("UL-2 inputs: " + e.Message); } }
        }
        bool inErr, manualOn, axisInit, prevFa = true; float engDisp, preLoad, axisHeld; static MethodInfo mSetFa;
        // HUD rotor gauge (Nick 22:31/22:35/22:36): the Alkyon-style label from our HUD extras prefab (UL2_rotorGauge),
        // rpm readout and an idle (110) to full (450 rpm) bar, red until the rotor is fast enough to jump (400), then green
        // 0.15.20 (Nick 03:43): styled like the stock throttle gauge: carrot = set rpm, coloured arc bar + readout = actual rpm
        Transform hud, carrotPiv, readPiv; UnityEngine.UI.Image barImg; TMPro.TMP_Text hudRead, hudLabel, hudDash; float hudFind, hudShow, setRpm;
        // 0.15.64 (Nick 03:32: a row of red/green dashes with the readout pushed far right): every UL-2 in the mission, AI
        // ones too, found the player's HUD gauge, added its own rpm dash and moved the readout 22 px further right, then wrote
        // its own rpm into it. Only the aircraft the player's combat HUD is showing drives the gauge now.
        static Type tHud; static FieldInfo fHudAc; static Component combatHud; static float hudLook;
        bool HudOwner()
        {
            try
            {
                if (tHud == null) { tHud = AccessTools.TypeByName("CombatHUD"); fHudAc = AccessTools.Field(tHud, "aircraft"); }
                if (combatHud == null) { if (Time.unscaledTime < hudLook) return false; hudLook = Time.unscaledTime + 1f; combatHud = FindObjectOfType(tHud) as Component; if (combatHud == null) return false; }
                return ReferenceEquals(fHudAc.GetValue(combatHud), unit);
            }
            catch { return false; }
        }
        void HudGauge(float dt, bool engOn)
        {
            try
            {
                if (!HudOwner()) { hud = null; hudRead = null; hudDash = null; barImg = null; hudLabel = null; carrotPiv = null; readPiv = null; return; }
                if (hud == null)
                {
                    if ((hudFind -= dt) > 0f) return; hudFind = 1f;
                    foreach (var t in Resources.FindObjectsOfTypeAll<RectTransform>()) if (t.name == "UL2_rotorGauge" && t.gameObject.scene.IsValid()) { hud = t; break; }
                    if (hud == null) return;
                    foreach (var t in hud.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "throttlebar") barImg = t.GetComponent<UnityEngine.UI.Image>();
                        else if (t.name == "throttleLabel") hudLabel = t.GetComponent<TMPro.TMP_Text>();
                        else if (t.name == "carrotPivot") carrotPiv = t;
                        else if (t.name == "readPivot") readPiv = t;
                    }
                    if (readPiv != null) foreach (var t in readPiv.GetComponentsInChildren<Transform>(true)) if (t.name == "throttleReading") hudRead = t.GetComponent<TMPro.TMP_Text>();
                    // 0.15.49 (Nick 02:44): two markers, set rpm "<" (the stock bar pointer, on the carrot pivot) and actual rpm "-"
                    // (on the readout pivot, same radius just outboard of the pointer), so they sit in a line when the rotor is at
                    // the set rpm and split apart while it lags. The readout number moves out past the dash.
                    if (hudRead != null) { var old = hudRead.transform.parent.Find("UL2_rpmDash"); if (old != null) hudDash = old.GetComponent<TMPro.TMP_Text>(); }   // already set up once
                    if (hudRead != null && hudDash == null)
                    {
                        var d = Instantiate(hudRead.gameObject, hudRead.transform.parent, false); d.name = "UL2_rpmDash";
                        hudDash = d.GetComponent<TMPro.TMP_Text>(); hudDash.text = "-"; hudDash.alignment = TMPro.TextAlignmentOptions.Center;
                        var dr = (RectTransform)d.transform; var rr = (RectTransform)hudRead.transform;
                        dr.sizeDelta = new Vector2(30f, rr.sizeDelta.y); dr.anchoredPosition = rr.anchoredPosition + new Vector2(-14f, 0f);
                        rr.anchoredPosition += new Vector2(22f, 0f);
                    }
                    Plugin.Log.LogInfo("UL-2 HUD rotor gauge found, bar " + (barImg != null) + " carrot " + (carrotPiv != null) + " readout " + (hudRead != null));
                }
                bool want = manualOn || autoSpin || (alt < 0.8f && engOn) || rpm > 1f;   // 0.15.50: stays up in flight so FREE / the markers can be read
                if (want) hudShow = 3f; else if (hudShow > 0f) hudShow -= dt;
                bool show = hudShow > 0f;
                if (hud.gameObject.activeSelf != show) hud.gameObject.SetActive(show);
                if (!show) return;
                float f = Mathf.Clamp01(rpm / 450f), fs = Mathf.Clamp01(setRpm / 450f);
                // 0.15.48 (Nick 02:43): while custom axis 1 is in use the marker shows where the axis is asking the rpm to go
                if (manualOn) fs = Mathf.Clamp01((110f + 340f * axis) / 450f);
                var col = rpm >= 300f ? new Color(0.3f, 1f, 0.3f, 1f) : Color.Lerp(new Color(1f, 0.25f, 0.2f, 1f), new Color(1f, 0.6f, 0.1f, 1f), Mathf.Clamp01(rpm / 300f));
                if (barImg != null) { barImg.fillAmount = f; barImg.color = col; }
                if (carrotPiv != null) carrotPiv.localEulerAngles = new Vector3(0f, 0f, fs * 26f - 13f);
                if (readPiv != null) { readPiv.localEulerAngles = new Vector3(0f, 0f, f * 26f - 13f); foreach (Transform ch in readPiv) ch.localEulerAngles = new Vector3(0f, 0f, -(f * 26f - 13f)); }   // 0.15.29 (Nick 12:29): readout line stays flat like the throttle one
                if (hudDash != null) hudDash.color = col;
                if (hudRead != null) { hudRead.text = (!manualOn && !autoSpin) ? "FREE" : Mathf.RoundToInt(rpm) + " RPM"; /* 0.15.50 (Nick 02:46): axis at minimum = free airspeed rotation */ hudRead.color = col; if (hudRead.transform.parent != readPiv) hudRead.transform.localEulerAngles = new Vector3(0f, 0f, -(f * 26f - 13f)); }
                if (hudLabel != null) hudLabel.text = manualOn ? "ROTOR MANUAL" : autoSpin ? "ROTOR AUTO" : "ROTOR";
            }
            catch (Exception e) { if (!hudErr) { hudErr = true; Plugin.Log.LogWarning("UL-2 HUD gauge: " + e.Message); } }
        }
        bool hudErr;
        float leanT = 2f;
        float logSpd, logVy, logYaw, logF;
        void LeanLog(float dt)
        {
            if ((leanT -= dt) > 0f) return; leanT = logSpd > 3f ? 1f : 5f;
            try
            {
                string o = "";
                foreach (var c in partList) { if (c == null) continue; if (c.name == "mast" || c.name == "rotorHead" || c.name == "engine_core" || c.name == "tailboom_R" || c.name.StartsWith("nacelle")) { var q = Quaternion.Inverse(transform.rotation) * c.transform.rotation; var e = q.eulerAngles; o += $" {c.name} p{Wrap(e.x):0.0} r{Wrap(e.z):0.0}"; } }
                var re = transform.rotation.eulerAngles;
                Plugin.Log.LogInfo($"UL-2 flight: {logSpd * 3.6f:0} km/h climb {logVy:0.0} m/s alt {alt:0.0} | rotor {rpm:0} rpm lift {rotorK:0.00} incidence {inc:0.0} body aoa {bodyAoA:0.0} stick {pitchIn:0.00} thr {thr:0.00} prop thrust {logThrust:0} N jump {logF:0.00} W rotor thrust {logT:0.00} W brake {brakeIn:0.0} roll in {rollIn:0.00}{(aiPilot ? $" AI raw {aiRawLog:0.00} path {aiGamMin:0.0}..{aiGamMax:0.0} obst {aiObst:0}" : "")} | pitch {Wrap(re.x):0.0} roll {Wrap(re.z):0.0} yaw rate {logYaw:0} deg/s |{o}");
            }
            catch { }
        }
        // diagnostics for the prop RPM loop (Nick 22:09): log the prop state when its RPM falls away
        Component prop; float propLook; static Type tProp; static FieldInfo fpRpm, fpPitch, fpAoA, fpPow, fpAng; float propPeak, propLogT;
        void PropWatch(float dt)
        {
            try
            {
                if (prop == null) { if ((propLook -= dt) > 0f) return; propLook = 3f; var tp = tProp = tProp ?? AccessTools.TypeByName("ConstantSpeedProp"); if (tp == null) return; foreach (var c in partList) { if (c == null) continue; var pc = c.GetComponentInChildren(tp, true); if (pc != null) { prop = pc; break; } } if (prop == null) return;
                    var td = AccessTools.TypeByName("PropStrikeDetector"); if (td != null) foreach (var d in prop.transform.root.GetComponentsInChildren(td, true)) Plugin.OurDetectors.Add(d.GetInstanceID());
                    foreach (var c in partList) if (c != null && td != null) foreach (var d in c.GetComponentsInChildren(td, true)) Plugin.OurDetectors.Add(d.GetInstanceID());
                    Plugin.Log.LogInfo("UL-2 prop watch on " + prop.name + ", " + Plugin.OurDetectors.Count + " strike detector(s)");
                    fpRpm = AccessTools.Field(tp, "RPM"); fpPitch = AccessTools.Field(tp, "PropPitch"); fpAoA = AccessTools.Field(tp, "averageAoA"); fpPow = AccessTools.Field(tp, "powerAvailable"); fpAng = AccessTools.Field(tp, "angularVelocity"); }
                float r = (float)fpRpm.GetValue(prop);
                propPeak = Mathf.Max(r, propPeak - 400f * dt);
                propLogT -= dt;
                if (propPeak > 600f && r < propPeak * 0.6f && propLogT <= 0f)
                {
                    propLogT = 0.3f;
                    Plugin.Log.LogInfo($"UL-2 prop rpm drop {r:0} (peak {propPeak:0}) pitch {(float)fpPitch.GetValue(prop):0.0} aoa {(float)fpAoA.GetValue(prop):0.0} power {(float)fpPow.GetValue(prop):0} thr {thr:0.00} engine {er:0.00} speed {(fSpeed != null ? (float)fSpeed.GetValue(unit) : 0f):0}");
                }
            }
            catch (Exception e) { if (!propErr) { propErr = true; Plugin.Log.LogWarning("UL-2 prop watch: " + e.Message); } }
        }
        bool propErr;
        // 0.15.27 (Nick 05:12): spinner with a white spiral like the P220 fans; it follows the prop but never turns faster on
        // screen than 4 rev/s, so the spiral reads as turning instead of strobing
        Transform spinner; Quaternion spinnerRest; float spinnerA, spinnerFind; static FieldInfo fpTurn;
        void Spinner(float dt)
        {
            try
            {
                if (spinner == null)
                {
                    if ((spinnerFind -= dt) > 0f) return; spinnerFind = 2f;
                    foreach (var c in partList) { if (c == null) continue; var t = Deep(c.transform, "UL2_spinner"); if (t != null) { spinner = t; spinnerRest = t.localRotation; break; } }
                    if (spinner == null) return;
                }
                if (prop == null || fpRpm == null) return;
                if (fpTurn == null) fpTurn = AccessTools.Field(prop.GetType(), "turnDirection");
                float dir = 1f; try { if (fpTurn != null) dir = Convert.ToSingle(fpTurn.GetValue(prop)); } catch { }
                float revs = Mathf.Min((float)fpRpm.GetValue(prop) / 60f, 4f);
                spinnerA = Mathf.Repeat(spinnerA - revs * 360f * dir * dt, 360f);
                spinner.localRotation = spinnerRest * Quaternion.Euler(0f, 0f, spinnerA);
            }
            catch { }
        }
        // extra rotor lift the fixed lifting surfaces can't give: a spun-up rotor on the takeoff roll, and the jump
        Component noseGear; bool noseSearched; static FieldInfo fLock;
        // 0.15.24 (Nick 04:39): rotor swoosh. Stock RotorShaft plays its loop at pitch = rpm/nominal * pitch and volume = rpm/nominal.
        // The clip is the attack helicopter's (4 blades at 280 rpm, pitch 0.7); matched to our 2-blade beat that is 0.7 * rpm / 560.
        AudioSource rotorAudio; bool audioSearched;
        // 0.15.70 (Nick 17:03): the engine note rises (up to +14 % pitch, a little louder) while the pre-rotator drives the
        // rotor past 380 rpm. The engine's own audio sources keep whatever the game sets; we scale on top each frame.
        readonly List<AudioSource> engAudio = new List<AudioSource>(); readonly Dictionary<AudioSource, (float p, float v, float sp, float sv)> engBase = new Dictionary<AudioSource, (float, float, float, float)>();
        bool engAudioSearched; float loadAud;
        void EngineAudio(float dt)
        {
            if (!engAudioSearched && engine != null) { engAudioSearched = true; foreach (var a in ((Component)engine).GetComponentsInChildren<AudioSource>(true)) if (a != null && a.name != "UL2_rotorAudio") engAudio.Add(a); Plugin.Log.LogInfo("UL-2 engine audio sources " + engAudio.Count); }
            loadAud = Mathf.MoveTowards(loadAud, preLoad, dt * 1.5f);
            foreach (var a in engAudio)
            {
                if (a == null) continue;
                // the base is whatever the game last wrote: if the value is not the one we set, the game changed it
                if (!engBase.TryGetValue(a, out var b) || Mathf.Abs(a.pitch - b.sp) > 1e-4f || Mathf.Abs(a.volume - b.sv) > 1e-4f) b = (a.pitch, a.volume, 0f, 0f);
                float np = b.p * (1f + 0.14f * loadAud), nv = Mathf.Min(1f, b.v * (1f + 0.15f * loadAud));
                a.pitch = np; a.volume = nv; engBase[a] = (b.p, b.v, np, nv);
            }
        }
        void RotorAudio(bool headOn)
        {
            if (!audioSearched) { audioSearched = true; foreach (var a in unit.GetComponentsInChildren<AudioSource>(true)) if (a.name == "UL2_rotorAudio") { rotorAudio = a; break; }
                if (rotorAudio == null) foreach (var c in partList) { if (c == null) continue; var t = c.transform.Find("UL2_rotorAudio"); if (t != null) { rotorAudio = t.GetComponent<AudioSource>(); break; } }
                Plugin.Log.LogInfo("UL-2 rotor audio " + (rotorAudio != null && rotorAudio.clip != null)); }
            if (rotorAudio == null || rotorAudio.clip == null) return;
            float r = headOn ? rpm : 0f;
            if (r < 5f) { if (rotorAudio.isPlaying) rotorAudio.Stop(); return; }
            if (!rotorAudio.isPlaying) { rotorAudio.time = UnityEngine.Random.Range(0f, rotorAudio.clip.length * 0.9f); rotorAudio.Play(); }
            rotorAudio.pitch = 0.7f * r / 560f;
            rotorAudio.volume = Mathf.Clamp01(r / 450f) * 0.8f;
        }
        void FixedUpdate()
        {
            try
            {
                if (!ready || pRb == null) return;
                var rb = pRb.GetValue(unit) as Rigidbody; if (rb == null || rb.isKinematic) return;
                if (headPart != null && Detached(headPart)) return;
                if ((massT -= Time.fixedDeltaTime) <= 0f)
                {
                    massT = 2f; massSum = 0f;
                    foreach (var c in partList) { if (c == null || Detached(c)) continue; var r = c.GetComponent<Rigidbody>(); if (r != null) massSum += r.mass; }
                    if (massSum < rb.mass) massSum = rb.mass;
                }
                float W = massSum * 9.81f, spd = fSpeed != null ? (float)fSpeed.GetValue(unit) : 0f, F = 0f;
                // 0.15.16 (Nick 03:14 log): the game's own forces on the high blade parts pitched the nose down 10 deg with the
                // stick back, and the jump push on the nose-heavy root body flipped it backwards. Rotor thrust and the jump now
                // act at the whole aircraft's centre of mass; the pitch and roll moments are ours, so they can be tuned.
                Vector3 com = rb.worldCenterOfMass; float cm = 0f; Vector3 cs = Vector3.zero;
                foreach (var c in partList) { if (c == null || Detached(c)) continue; var r = c.GetComponent<Rigidbody>(); if (r == null || r.isKinematic) continue; cs += r.worldCenterOfMass * r.mass; cm += r.mass; }
                if (cm > 0f) com = cs / cm;
                // 0.15.22 (Nick 04:10: nose steering flips it): the stock nose wheel steers a fixed lock at any speed. Scale the lock
                // so a full pedal turn stays under 0.3 g sideways (tip-over is ~0.63 g on this narrow, tall trike): 22 deg at walking
                // pace, ~4 deg at 30 km/h, 2 deg floor.
                if (noseGear == null && !noseSearched)
                {
                    noseSearched = true; var tLg = AccessTools.TypeByName("LandingGear");
                    if (tLg != null) { fLock = AccessTools.Field(tLg, "steeringLock"); var fSt = AccessTools.Field(tLg, "steering");
                        foreach (var g in unit.GetComponentsInChildren(tLg, true)) if (fSt != null && (bool)fSt.GetValue(g)) { noseGear = g; break; } }
                    Plugin.Log.LogInfo("UL-2 nose steering " + (noseGear != null));
                }
                if (noseGear != null && fLock != null)
                {
                    float v2 = Mathf.Max(spd * spd, 1f), lockDeg = Mathf.Clamp(Mathf.Atan(2.6f * 0.3f * 9.81f / v2) * Mathf.Rad2Deg, 2f, 22f);
                    fLock.SetValue(noseGear, lockDeg);
                }
                // 0.15.34 (Nick 21:51-21:52): AI pilots rotate early and pull too hard for a gyro, bleed speed and fall back in.
                // The stock autopilot has no speed protection in pitch, so for AI pilots only: no pull before 80 km/h, level
                // (or a gentle dive with height to spare) to build speed under 80 km/h, shallow climbs above, stay low.
                if (IsAI()) pitchIn = AiPitch(rb, spd, pitchIn);
                var thrustV = RotorAero(rb, spd, W);
                thrustV += rotorN * F;
                if (thrustV.sqrMagnitude > 0f) rb.AddForceAtPosition(thrustV, com, ForceMode.Force);
                logSpd = spd; logVy = rb.velocity.y; logYaw = Vector3.Dot(rb.angularVelocity, transform.up) * Mathf.Rad2Deg; logF = W > 0f ? F / W : 0f;
                logT = W > 0f ? Vector3.Dot(thrustV, transform.up) / W : 0f;
                // rotor head control moments (a gyro pitches and rolls by tilting the head) plus the rotor's own damping,
                // which is what keeps a real gyro from tumbling. Game pitch input: positive = nose down; roll: positive = right.
                float rs = Mathf.Clamp01(rpm / 300f);
                float K = alt < 2f ? 1800f : Mathf.Lerp(2500f, 1200f, Mathf.InverseLerp(15f, 35f, spd));
                var wl = transform.InverseTransformDirection(rb.angularVelocity);
                var tq = Vector3.zero;
                if (rs > 0.05f) tq += transform.right * (Mathf.Clamp(pitchIn, -1f, 1f) * K * rs) - transform.forward * (Mathf.Clamp(rollIn, -1f, 1f) * 2400f * rs);
                // 0.15.18 (Nick 03:35: drifts left, little right roll): the body hangs under the rotor, so it rolls back level
                // on its own; the stick now has the authority to hold about 50 deg of bank either way
                if (alt >= 1.5f) tq += transform.forward * (-Mathf.Sin(Wrap(transform.rotation.eulerAngles.z) * Mathf.Deg2Rad) * 2600f * rotorK);
                float damp = 2500f * Mathf.Max(rotorK, 0.3f);
                tq -= transform.right * (wl.x * damp) + transform.forward * (wl.z * 1500f * Mathf.Max(rotorK, 0.3f));
                // on the wheels: the wide gear keeps it upright against prop torque and the side load as the rotor unloads them
                if (alt < 1.5f)
                {
                    float rollA = Wrap(transform.rotation.eulerAngles.z) * Mathf.Deg2Rad;
                    tq += transform.forward * (-rollA * 9000f);
                }
                // 0.15.20 (Nick 03:51: more rudder): extra rudder moment from the airflow plus the prop wash over the fin
                float qe = 0.6125f * rb.velocity.sqrMagnitude + 0.3f * Mathf.Max(logThrust, 0f);
                tq += transform.up * (Mathf.Clamp(yawIn, -1f, 1f) * qe * 1.4f);
                rb.AddTorque(tq, ForceMode.Force);
            }
            catch (Exception e) { Plugin.Log.LogError("UL-2 rotor lift: " + e); pRb = null; }
        }
        static FieldInfo fState; float aiChk, aiRawLog, aiGamMax, aiGamMin, aiObst = -1f; bool aiPilot, aiLanding, aiRoll;
        bool IsAI()
        {
            if ((aiChk -= Time.fixedDeltaTime) > 0f) return aiPilot; aiChk = 0.5f;
            try
            {
                if (fPilots == null) fPilots = AccessTools.Field(AccessTools.TypeByName("Aircraft"), "pilots");
                var pl = fPilots?.GetValue(unit) as Array; aiPilot = false;
                if (pl != null && pl.Length > 0 && pl.GetValue(0) is object p && p != null)
                {
                    if (fState == null) fState = AccessTools.Field(p.GetType(), "currentState");
                    var st = fState?.GetValue(p);
                    aiPilot = st != null && st.GetType().Name.StartsWith("AI");
                    aiLanding = aiPilot && st.GetType().Name.Contains("Landing");
                }
            }
            catch { aiPilot = false; aiLanding = false; }
            return aiPilot;
        }
        // flight path limiter: the AI's pitch is kept at or nose-down of what holds the flight path at or below gammaMax
        internal void AiFilter()
        {
            if (!ready || mInputs == null || pRb == null || !IsAI()) return;
            var rb = pRb.GetValue(unit) as Rigidbody; if (rb == null || rb.isKinematic) return;
            var ci = mInputs.Invoke(unit, null); if (ci == null || fPitch == null) return;
            aiRawLog = (float)fPitch.GetValue(ci);
            float spd = fSpeed != null ? (float)fSpeed.GetValue(unit) : 0f;
            fPitch.SetValue(ci, AiPitch(rb, spd, (float)fPitch.GetValue(ci)));
            if (aiLevel && fRoll != null)
            {
                float bank = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, transform.forward), transform.up, transform.forward);
                fRoll.SetValue(ci, Mathf.Clamp(bank * 0.03f, -1f, 1f));            // roll back toward wings level
            }
        }
        bool aiLevel; float aiNeedT, aiNeed = -90f;
        static System.Type tUnitT;
        static bool IsVehicle(Collider c)
        {
            if (c == null) return false; tUnitT = tUnitT ?? AccessTools.TypeByName("Unit");
            var u = c.GetComponentInParent(tUnitT); if (u == null) return false;
            var n = u.GetType().Name; return n != "Building" && n != "Scenery";
        }
        float TerrainNeed(Rigidbody rb, Vector3 v, float vm, float gamma)
        {
            if ((aiNeedT -= Time.fixedDeltaTime) > 0f) return aiNeed; aiNeedT = 0.15f;
            var vh = new Vector3(v.x, 0f, v.z); float sh = vh.magnitude; if (sh < 3f) return aiNeed = -90f;
            float w = rb.angularVelocity.y;                                         // turn rate about world up, rad/s
            Vector3 p = rb.worldCenterOfMass, dir = vh / sh; float best = -90f, s = 0f;
            for (int i = 1; i <= 14; i++)
            {
                const float dt = 0.5f; float t = i * dt;
                dir = Quaternion.AngleAxis(w * dt * Mathf.Rad2Deg, Vector3.up) * dir; p += dir * sh * dt; s += sh * dt;
                var top = new Vector3(p.x, rb.worldCenterOfMass.y + 600f, p.z);
                if (!Physics.Raycast(top, Vector3.down, out var gh, 1500f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (gh.rigidbody == rb || gh.transform.IsChildOf(transform.root) || IsVehicle(gh.collider)) continue;
                float yOnPath = rb.worldCenterOfMass.y + v.y * t;                   // height if the path angle is held
                float margin = Mathf.Lerp(8f, 22f, Mathf.InverseLerp(1f, 5f, t));
                float shortBy = gh.point.y + margin - yOnPath;
                if (shortBy > 0f) best = Mathf.Max(best, gamma + Mathf.Atan2(shortBy, s) * Mathf.Rad2Deg);
            }
            return aiNeed = best;
        }
        float AiPitch(Rigidbody rb, float spd, float raw)
        {
            float p = Mathf.Clamp(raw, -1f, 1f);
            const float V80 = 22.2f;                                   // 80 km/h
            var v = rb.velocity; float vm = Mathf.Max(v.magnitude, 1f);
            float gamma = Mathf.Asin(Mathf.Clamp(v.y / vm, -1f, 1f)) * Mathf.Rad2Deg;
            float upRate = -Vector3.Dot(rb.angularVelocity, transform.right) * Mathf.Rad2Deg;   // nose-up rate, deg/s
            float gMax;
            if (alt < 1.5f) { aiGamMax = 0f; return spd < V80 ? Mathf.Max(p, 0f) : Mathf.Max(p, -0.3f); }   // no rotation before 80 km/h
            if (spd < V80) gMax = -4f * Mathf.InverseLerp(8f, 25f, alt);               // level near the ground, gentle dive with height
            else gMax = Mathf.Lerp(0f, 6f, Mathf.InverseLerp(V80, 30f, spd));          // shallow climb once speed builds
            // stay low (0.15.54, Nick 20:59: lower in combat, masking behind terrain): no climbing above ~45 m over the ground,
            // or ~190 m with the spotter aboard so he can see over the terrain
            // 0.15.54b (Nick 23:03): only while the low window is open (missile alert or attack in the last 5 min); otherwise
            // the 0.15.34 cap (no climbing above ~150 m)
            if (!LowMode || PopUp) gMax = Mathf.Min(gMax, Mathf.Lerp(6f, -2f, Mathf.InverseLerp(120f, 220f, alt)));   // 0.15.56: a pop-up may climb
            else if (SpotterAboard) gMax = Mathf.Min(gMax, Mathf.Lerp(6f, -2f, Mathf.InverseLerp(160f, 240f, alt)));
            else gMax = Mathf.Min(gMax, Mathf.Lerp(6f, -2f, Mathf.InverseLerp(35f, 70f, alt)));
            // 0.15.54 (Nick 20:21: AI held 20 m on the approach until it stalled): on a landing the floors below all read the
            // runway ahead as ground to climb over, so the stock landing autopilot keeps the path and only the speed
            // protection above stays on
            if (aiLanding)
            {
                aiLevel = false; aiObst = -1f; aiGamMax = gMax; aiGamMin = -90f;
                return Mathf.Max(p, Mathf.Clamp(0.08f * (gamma - gMax) + 0.02f * upRate, -0.35f, 0.7f));
            }
            // 0.15.35 (Nick 22:05: lining up a seat 3 gun run it flies into the ground or obstacles): a floor on the flight
            // path from height above ground, plus a look-ahead along the flight path for terrain, trees and buildings
            float gMin = Mathf.Clamp(-(alt - 10f) * 0.5f, -15f, 0f);
            if (alt < 10f) gMin = Mathf.Lerp(4f, 0f, alt / 10f);
            float look = Mathf.Max(spd * 5f, 40f); Vector3 vd = v / vm, org = rb.worldCenterOfMass + vd * 6f;
            // 0.15.53 (Nick 19:14: overshooting the target with the nose gun): vehicles are not obstacles. On a gun run the
            // target itself sat on the flight path, so the floor pulled the nose up over it and the rounds went long
            if (Physics.Raycast(org, vd, out var hit, look, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody != rb && !hit.transform.IsChildOf(transform.root) && !IsVehicle(hit.collider))
            {
                float ttc = hit.distance / vm;                                         // seconds to impact on the current path
                gMin = Mathf.Max(gMin, Mathf.Lerp(12f, 2f, Mathf.InverseLerp(1.5f, 5f, ttc)));
                aiObst = hit.distance;
            }
            else aiObst = -1f;
            // 0.15.53 (Nick 19:14: still flies into mountains while circling a target for the seat 3 guns): the straight
            // look-ahead misses rising ground on the inside of a turn. Now the curved path is predicted from the turn rate
            // for 7 s, the ground height under each point is measured, and the path floor climbs early enough to clear it
            // by 25 m. When the needed climb is more than a few degrees beyond the current path, the wings are rolled level
            // so the pull goes into climbing instead of tightening the turn into the slope.
            float need = TerrainNeed(rb, v, vm, gamma);
            aiLevel = need > gamma + 4f;
            gMin = Mathf.Max(gMin, Mathf.Min(need, 18f));
            aiGamMax = gMax; aiGamMin = gMin;
            if (gMin > gMax) gMax = gMin;                                              // not hitting the ground beats speed
            float pMin = 0.08f * (gamma - gMax) + 0.02f * upRate;
            float pMax = 0.10f * (gamma - gMin) + 0.03f * upRate;                      // negative = must pull
            p = Mathf.Max(p, Mathf.Clamp(pMin, -0.35f, 0.7f));
            return Mathf.Min(p, Mathf.Clamp(pMax, -0.8f, 1f));
        }
        Vector3 rotorN = Vector3.up; float logT, rotorArea = 48f;
        static float Atanh(float x) { return 0.5f * Mathf.Log((1f + x) / (1f - x)); }
        // 0.15.16 rotor thrust from the plugin (the blade surfaces keep a token area so damage still works): thrust is normal to
        // the tilted disc, Cl rises with disc angle of attack and flattens instead of stalling, and scales with rotor rpm.
        Vector3 RotorAero(Rigidbody rb, float spd, float W)
        {
            var v = transform.InverseTransformDirection(rb.velocity);
            bodyAoA = v.sqrMagnitude > 1f ? Mathf.Atan2(-v.y, Mathf.Max(v.z, 0.1f)) * Mathf.Rad2Deg : 0f;
            float pull = Mathf.Clamp01(-pitchIn), push = Mathf.Clamp01(pitchIn);
            float cap = Mathf.Max(rotorK * 0.5f * 1.225f * rb.velocity.sqrMagnitude * rotorArea * 1.6f, 1f);
            float perDeg = cap * 1.15f / 12f / Mathf.Max(W, 1f);           // weight fraction per degree of disc angle near 0
            float aLevel = 12f * Atanh(Mathf.Min(W / (cap * 1.15f), 0.95f));
            float neutral = Mathf.Min(INC0, aLevel + 1.3f);                 // trims itself level at speed (body sits ~-1.3 deg)
            float cmd = neutral + Mathf.Min(INC_PULL, 1.5f / perDeg) * pull - Mathf.Min(INC_PUSH, 1.0f / perDeg) * push;
            inc = Mathf.MoveTowards(inc, Mathf.Clamp(cmd, -6f, 14f), 20f * Time.fixedDeltaTime);
            float k = Mathf.Clamp01((rpm - 100f) / 200f); rotorK = k * k;
            float area = 0f;
            foreach (var b in rotorBlades)
            {
                if (b.part == null) continue;
                if (Detached(b.part)) { if (b.ln != null) b.ln.localRotation = Quaternion.identity; mSetArea?.Invoke(b.part, new object[] { b.area }); b.part = null; continue; }
                if (b.ln != null) b.ln.localRotation = Quaternion.identity;
                mSetArea?.Invoke(b.part, new object[] { b.area * 0.02f });
                area += b.area;
            }
            try { if (prop != null) { mThrust = mThrust ?? AccessTools.Method(prop.GetType(), "GetThrust"); logThrust = (float)mThrust.Invoke(prop, null); } } catch { }
            float lat = 4f * Mathf.Clamp(rollIn, -1f, 1f);
            rotorN = (Quaternion.AngleAxis(-inc + INC0, transform.right) * Quaternion.AngleAxis(-lat, transform.forward)) * transform.up;
            rotorArea = Mathf.Max(area, 1f);
            if (area <= 0f || rb.velocity.sqrMagnitude < 1f) return Vector3.zero;
            float a = bodyAoA + inc;                                      // disc angle of attack, deg
            // 0.15.17 (Nick 03:27 log: lifted off but bled speed and sank, disc at 10-20 deg): effective lifting area 1.6x the
            // blade surfaces (a full 8.4 m disc), so cruise needs a few degrees of disc angle, not 10+, and induced drag drops
            float cl = a >= 0f ? 1.15f * (float)Math.Tanh(a / 12f) : (spd > 8f ? Mathf.Max(-0.08f, a / 15f) : 0f);
            float q = 0.5f * 1.225f * rb.velocity.sqrMagnitude;
            float T = Mathf.Min(rotorK * cl * q * area * 1.6f, 2.5f * W);
            // 0.15.74 (Nick 17:54: AI landings bounced off the runway and glided back up a few metres, then stalled in): once an
            // AI landing has its wheels on the ground the rotor can no longer lift more than half the weight, like a pilot
            // holding the disc flat on the rollout, so it stays down and slows on the brakes
            if (aiLanding && aiPilot) { if (alt < 0.8f) aiRoll = true; } else aiRoll = false;
            if (aiRoll) T = Mathf.Min(T, 0.5f * W);
            var dir = -rb.velocity.normalized;
            float Dp = rotorK * 0.009f * q * area;                        // blade profile drag; induced drag comes from the disc tilt
            return rotorN * T + dir * Dp;
        }
        float spawnT;
        void OnDetached<T>(T part) where T : class
        {
            var c = part as Component;
            Plugin.Log.LogInfo($"UL-2 part detached: {(c != null ? c.name : "?")} at {Time.time - spawnT:0.00} s after spawn");
        }
        void ScanKits()
        {
            bool kit = false, spot = false;
            foreach (var c in partList)
            {
                if (c == null) continue;
                foreach (var t in c.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("UL2_s3kit_")) { if (t.GetComponent<Seat3Pose>() == null) t.gameObject.AddComponent<Seat3Pose>(); if (t.gameObject.activeInHierarchy && !t.name.StartsWith("UL2_s3kit_pm")) { kit = true; if (t.name == "UL2_s3kit_spot") spot = true; } }   // passenger module kits (pm*) are not seat 3: no seat 3 lap belt
                    else if (t.name.StartsWith("UL2_harness") && t.name.Length == 12) { int k = t.name[11] - '1'; if (k >= 0 && k < 3) harness[k] = t.gameObject; }
                }
            }
            seat3Kit = kit; spotKit = spot;
        }
        bool spotKit; internal bool SpotterAboard => spotKit;
        internal const float CAP_LOW = 35f, CAP_SPOT = 160f;   // combat target height over the ground, m
        // 0.15.54b (Nick 23:03): the low cap holds for 5 minutes after the last missile alert or attack; otherwise normal flight
        internal const float LOW_TIME = 300f;
        float lowUntil = -1f;
        internal bool LowMode => Time.time < lowUntil;
        // ---- 0.15.56 tactics: each returns the label shown to spectators, or null to leave the stock AI alone
        internal string stockName;
        float beamStart = -99f, beamLast = -99f, popUntil = -1f, popFired = -99f; Vector3 beamFrom; int orbitDir;
        internal bool PopUp => Time.time < popUntil;
        Vector3 homeDir = Vector3.forward; static FieldInfo fThreat, fTv, fBase, fCenter;
        // flat direction from the target toward the nearest friendly airbase the stock AI already tracks (local space)
        Vector3? HomeBearing(object st, Vector3 targetLocal)
        {
            try
            {
                fBase = fBase ?? AccessTools.Field(st.GetType(), "nearestAirbase");
                var ab = fBase?.GetValue(st) as Component; if (ab == null) return null;
                fCenter = fCenter ?? AccessTools.Field(ab.GetType(), "center");
                var c = fCenter?.GetValue(ab) as Transform; if (c == null) return null;
                var d = Flat(c.position - targetLocal); return d.sqrMagnitude > 1f ? d.normalized : (Vector3?)null;
            }
            catch { return null; }
        }
        internal void RadarLock(Vector3 emitterGlobal)
        {
            if (Time.time - beamStart > 40f) beamStart = Time.time;            // up to 20 s of beaming, then 20 s of ignoring it
            beamLast = Time.time; beamFrom = emitterGlobal; GoLow("radar lock");
        }
        bool Beaming => Time.time - beamLast < 4f && Time.time - beamStart < 20f;
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        void SetDest(object st, Vector3 g, float height)
        {
            Plugin.fDest.SetValue(st, Plugin.MakeGP(g)); Plugin.fTh.SetValue(st, height);
            if (Plugin.fFollow != null) Plugin.fFollow.SetValue(st, true);
        }
        internal string Tactic(object st, string mode)
        {
            Vector3 own = Plugin.GlobalOf(unit), fwd = Flat(transform.forward).normalized;
            var tgt = Plugin.fCurT.GetValue(st) as Component;
            Vector3 tpos = tgt != null ? Plugin.GP(Plugin.fTkp.GetValue(st)) : own;
            // RADAR SHY: a radar locked on, so turn side-on to it (beam it, so it loses the closing speed) and get down low.
            // Not while a missile shot is already lined up.
            if (Beaming && mode != "UsingMissiles")
            {
                var d = Flat(own - beamFrom).normalized; var perp = new Vector3(-d.z, 0f, d.x);
                if (Vector3.Dot(perp, fwd) < 0f) perp = -perp;
                SetDest(st, own + perp * 3000f, 12f);
                return "Beaming Radar";
            }
            // SPOTTER LOITER: with the spotter aboard, hold a wide slow circle 4 km from the target and watch it for the team
            // instead of running in; missile shots that come in range are still taken from the circle
            // 0.15.57 (Nick 23:20: the orbit must not carry it deep into enemy ground): it is no longer a full circle but an arc
            // on the friendly side, at most 60 deg either side of the line from the target to the nearest friendly airbase
            // (or the side it came in from), pushed away from the threats the stock AI knows about; at an end of the arc it
            // turns back the other way. Starting on the far side, it first works round to the friendly side.
            if (SpotterAboard && tgt != null && mode != "UsingMissiles" && mode != "NoTarget")
            {
                var r = Flat(own - tpos); float dist = r.magnitude; var rad = dist > 1f ? r / dist : -fwd;
                if (orbitDir == 0) { orbitDir = Vector3.Dot(Vector3.Cross(Vector3.up, rad), fwd) >= 0f ? 1 : -1; homeDir = rad; }
                var h = HomeBearing(st, tgt.transform.position) ?? homeDir;
                try
                {
                    fThreat = fThreat ?? AccessTools.Field(st.GetType(), "threatVector");
                    var tvo = fThreat?.GetValue(st); fTv = fTv ?? (tvo != null ? AccessTools.Field(tvo.GetType(), "threatVector") : null);
                    if (tvo != null && fTv != null) { var tv = Flat((Vector3)fTv.GetValue(tvo)); if (tv.sqrMagnitude > 1e-4f) h = (h - tv.normalized * 0.5f).normalized; }
                }
                catch { }
                float a = Vector3.SignedAngle(h, rad, Vector3.up);             // where it sits on the circle, from the friendly line
                Vector3 aim;
                if (Mathf.Abs(a) > 75f)                                         // outside the arc: head round to its near end
                    aim = tpos + (Quaternion.AngleAxis(Mathf.Sign(a) * 60f, Vector3.up) * h) * 4000f;
                else
                {
                    if (Mathf.Abs(a) > 60f && Mathf.Sign(a) == -orbitDir) orbitDir = -orbitDir;   // end of the arc: turn back
                    aim = dist < 3000f ? own + rad * 2000f : tpos + (Quaternion.AngleAxis(-orbitDir * 25f, Vector3.up) * rad) * 4000f;
                }
                aim.y = own.y; SetDest(st, aim, 150f);
                return "Spotting Target";
            }
            orbitDir = 0;
            // POP-UP ATTACK: with a missile selected, run in very low and out of sight; inside range + 2 km climb only until
            // the target is in view, fire, then dive away to the side
            var wi = Plugin.fWi?.GetValue(st);
            bool missile = wi != null && Plugin.fMissile != null && (bool)Plugin.fMissile.GetValue(wi);
            if (tgt != null && missile && (mode == "FlyingToTarget" || mode == "UsingMissiles"))
            {
                float maxR = (float)Plugin.fMaxR.GetValue(Plugin.fReq.GetValue(wi)), dist = Flat(own - tpos).magnitude;
                float lf = Plugin.fLastFired != null ? (float)Plugin.fLastFired.GetValue(st) : -99f;
                if (Time.timeSinceLevelLoad - lf < 1f) popFired = Time.time;
                if (dist > maxR + 2000f) { Plugin.fTh.SetValue(st, 25f); if (Plugin.fFollow != null) Plugin.fFollow.SetValue(st, true); return "Low Run-In"; }
                bool hidden = Plugin.fObsc != null && (bool)Plugin.fObsc.GetValue(st);
                popUntil = Time.time + 3f;
                Plugin.fTh.SetValue(st, hidden ? 110f : Mathf.Max(alt, 25f));
                return "Pop-Up Attack";
            }
            if (Time.time - popFired < 12f && (mode == "BreakOffAttack" || mode == "RetreatStandoff" || mode == "FlyingToTarget"))
            {
                var away = Flat(own - tpos).normalized; var side = new Vector3(-away.z, 0f, away.x);
                if (Vector3.Dot(side, fwd) < 0f) side = -side;
                SetDest(st, own + (away + side).normalized * 3000f, 15f);
                return "Diving Away";
            }
            return null;
        }
        internal void GoLow(string why)
        {
            if (!LowMode) Plugin.Log.LogInfo("UL-2 AI low-level mode on (" + why + "), 5 min" + (SpotterAboard ? ", spotter cap 160 m" : ", cap 35 m"));
            lowUntil = Time.time + LOW_TIME;
        }
        // harnesses go with their occupant (Nick 21:58): pilot / copilot harness hides once that crewman has ejected,
        // the seat 3 lap belt shows only while a seat 3 kit (with its soldier) is loaded
        readonly GameObject[] harness = new GameObject[3]; bool seat3Kit; static FieldInfo fPilots, fEjected;
        void Harnesses()
        {
            if (fPilots == null) fPilots = AccessTools.Field(AccessTools.TypeByName("Aircraft"), "pilots");
            var pl = fPilots?.GetValue(unit) as Array;
            for (int k = 0; k < 3; k++)
            {
                var h = harness[k]; if (h == null) continue;
                bool show;
                if (k == 2) show = seat3Kit;
                else
                {
                    show = true;
                    if (pl != null && k < pl.Length && pl.GetValue(k) is object p && p != null)
                    {
                        if (fEjected == null) fEjected = AccessTools.Field(p.GetType(), "ejected");
                        if (fEjected != null && (bool)fEjected.GetValue(p)) show = false;
                    }
                }
                if (h.activeSelf != show) h.SetActive(show);
            }
        }
        static Transform Deep(Transform r, string n) { foreach (var x in r.GetComponentsInChildren<Transform>(true)) if (x.name == n) return x; return null; }
        // copilot backup stick (pitch / roll), throttle lever (25 deg aft at idle, 25 forward at full) and pedals (rock on the bar)
        void CopilotControls(float dt)
        {
            float k = 1f - Mathf.Exp(-dt * 12f);
            stick2X = Mathf.Lerp(stick2X, 12f * Mathf.Clamp(pitchIn, -1f, 1f), k); stick2Z = Mathf.Lerp(stick2Z, -12f * Mathf.Clamp(rollIn, -1f, 1f), k);
            thr2A = Mathf.Lerp(thr2A, -25f + 50f * thr, k);
            if (stick2 != null) stick2.localRotation = stick2Rest * Quaternion.Euler(stick2X, 0f, stick2Z);
            if (thr2 != null) thr2.localRotation = thr2Rest * Quaternion.Euler(thr2A, 0f, 0f);
            foreach (var kv in pedals2)
                if (kv.Value != null) kv.Value.localRotation = pedal2Rest[kv.Key] * Quaternion.Euler((kv.Key == "R" ? 1f : -1f) * 250f * pedalPos, 0f, 0f);
        }
        // after the animator: wrists to the controller handles, elbows down and out, hands along the grips
        void LateUpdate()
        {
            if (!ready) return;
            try { EngineAudio(Time.deltaTime); } catch (Exception ex) { Plugin.Log.LogError("UL-2 engine audio: " + ex.Message); engAudio.Clear(); }
            try
            {
                if (exStick != null && jsT != null) exStick.localRotation = jsT.localRotation;
                if (exThr != null && thT != null) exThr.localPosition = thT.localPosition;
            }
            catch { }
            if (pil != null && pil.gameObject.activeInHierarchy && jsT != null && thT != null)
            {
                try
                {
                    // 0.15.72 (Nick 16:51): the stock skinned hand holds each grip, like the stock pilot does; the wrist goes to the
                    // fitted point and the hand takes the fitted turn in the stick / throttle frame so its curled fist closes round
                    // the grip, and the stock skin stretches the sleeve to it (no modelled gloves or sleeve bridges)
                    if (parms[1] != null) { Reach(parms[1], jsT.TransformPoint(jsWrist), transform.TransformDirection(new Vector3(0.7f, -1f, -0.3f)), jsT.up); parms[1][2].rotation = jsT.rotation * jsHand; }
                    if (parms[0] != null) { Reach(parms[0], thT.TransformPoint(thWrist), transform.TransformDirection(new Vector3(-0.7f, -1f, -0.3f)), thT.up); parms[0][2].rotation = thT.rotation * thHand; }
                    Sleeves(slP, parms, false);
                    if (!parmsLogged) { parmsLogged = true; Plugin.Log.LogInfo("UL-2 pilot hands on stick and throttle, wrist miss R " + (parms[1] != null ? (parms[1][2].position - jsT.TransformPoint(jsWrist)).magnitude.ToString("0.000") : "-") + " L " + (parms[0] != null ? (parms[0][2].position - thT.TransformPoint(thWrist)).magnitude.ToString("0.000") : "-") + " m"); }
                }
                catch (Exception ex) { Plugin.Log.LogError("UL-2 pilot hands: " + ex); pil = null; }
            }
            if (!(pil != null && pil.gameObject.activeInHierarchy && jsT != null && thT != null)) Sleeves(slP, parms, false);
            if (ctrl2 == null || cop == null || !cop.gameObject.activeInHierarchy) { Sleeves(slC, arms, false); return; }
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var b = arms[i]; if (b == null || b[0] == null || b[1] == null || b[2] == null) continue;
                    float sx = i == 0 ? -1f : 1f;
                    // 0.15.31 (Nick 15:19): bigger handset with long handles; wrists sit behind and below the handles and the
                    // hands bend up along them, so the grip reads as holding a controller instead of clasped hands
                    // 0.15.72: the stock hand holds each handle (fitted wrist point and turn in the handset frame)
                    Reach(b, ctrl2.TransformPoint(i == 0 ? copWristL : copWristR), ctrl2.TransformDirection(new Vector3(sx * 0.7f, -1f, -0.3f)), ctrl2.up);
                    b[2].rotation = ctrl2.rotation * (i == 0 ? copHandL : copHandR);
                }
                Sleeves(slC, arms, false);
                if (!armsLogged) { armsLogged = true; Plugin.Log.LogInfo("UL-2 copilot hands on the controller, wrist miss L " + (arms[0] != null ? (arms[0][2].position - ctrl2.TransformPoint(copWristL)).magnitude.ToString("0.000") : "-") + " m"); }
            }
            catch (Exception ex) { Plugin.Log.LogError("UL-2 copilot hands: " + ex); ctrl2 = null; }
        }
        // 0.15.63 (Nick 02:49): each sleeve bridge sits on its wrist and points up the forearm to the elbow, covering the
        // pinched end of the stock sleeve where the folded-away hand used to be
        static readonly Vector3 copWristL = new Vector3(-0.12078f, -0.00792f, -0.13621f), copWristR = new Vector3(0.12057f, -0.00137f, -0.15199f);
        static readonly Quaternion copHandL = new Quaternion(0.16110f, 0.53279f, 0.82484f, -0.09911f), copHandR = new Quaternion(-0.16110f, 0.53279f, 0.82484f, 0.09911f);
        static void Sleeves(Transform[] sl, Transform[][] b, bool on)
        {
            for (int i = 0; i < 2; i++)
            {
                var s = sl[i]; if (s == null) continue;
                bool ok = on && b[i] != null && b[i][1] != null && b[i][2] != null && b[i][1].gameObject.activeInHierarchy;
                if (s.gameObject.activeSelf != ok) s.gameObject.SetActive(ok);
                if (!ok) continue;
                Vector3 w = b[i][2].position, up = b[i][1].position - w;
                if (up.sqrMagnitude < 1e-6f) continue;
                s.SetPositionAndRotation(w, Quaternion.LookRotation(up.normalized, Vector3.up));
            }
        }
        // two-bone arm IK: shoulder, elbow, wrist; the elbow bends toward the pole and the hand turns to point along hdir
        static void Reach(Transform[] b, Vector3 tgt, Vector3 pole, Vector3 hdir)
        {
            if (b == null || b[0] == null || b[1] == null || b[2] == null) return;
            Vector3 a = b[0].position, e = b[1].position, w = b[2].position;
            float l1 = (e - a).magnitude, l2 = (w - e).magnitude, d = Mathf.Clamp((tgt - a).magnitude, 0.05f, (l1 + l2) * 0.999f);
            Vector3 dir = (tgt - a).normalized, bend = Vector3.ProjectOnPlane(pole, dir).normalized;
            float ca = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f), sa = Mathf.Sqrt(1f - ca * ca);
            Vector3 elbow = a + dir * (l1 * ca) + bend * (l1 * sa);
            b[0].rotation = Quaternion.FromToRotation(e - a, elbow - a) * b[0].rotation;
            e = b[1].position; w = b[2].position;
            b[1].rotation = Quaternion.FromToRotation(w - e, a + dir * d - e) * b[1].rotation;
            var fin = b[2].childCount > 0 ? b[2].GetChild(0) : null;
            Vector3 cur = fin != null ? fin.position - b[2].position : b[2].position - b[1].position;
            if (cur.sqrMagnitude > 1e-8f) b[2].rotation = Quaternion.FromToRotation(cur, hdir) * b[2].rotation;
        }
        // right rudder pushes the right pedal forward and the left one back, 6 cm each way (P220 numbers)
        void Pedals(float dt)
        {
            if (pedals.Count == 0 || dt <= 0f) return;
            pedalPos = Mathf.Lerp(pedalPos, 0.06f * yawIn, 1f - Mathf.Exp(-dt * 10f));
            foreach (var kv in pedals)
            {
                if (kv.Value == null) continue;
                kv.Value.localPosition = pedalRest[kv.Key] + Vector3.forward * (kv.Key == "R" ? pedalPos : -pedalPos);
            }
        }
        void Gauges(float spd, float dt)
        {
            Pedals(dt); CopilotControls(dt);
            if (gauges.Count == 0 || dt <= 0f) return;
            float y = transform.position.y;
            if (haveY) vs = Mathf.Lerp(vs, (y - lastY) / dt, 1f - Mathf.Exp(-dt * 2f));
            lastY = y; haveY = true;
            float alt = fAlt != null ? Mathf.Max((float)fAlt.GetValue(unit), 0f) * 3.28084f : 0f;
            float fuel = 0f; try { if (mFuel != null) fuel = Mathf.Clamp01((float)mFuel.Invoke(unit, null)); } catch { }
            Needle("asi", -135f + 270f * Mathf.Clamp01(spd * 3.6f / 320f), dt);              // 0-320 km/h (0.15.69, yellow 240-280, red 280)
            Needle("alt", alt % 1000f / 1000f * 360f, dt);                                  // one turn per 1000 ft
            Needle("vsi", -90f + 135f * Mathf.Clamp(vs / 10f, -1f, 1f), dt);                // +-10 m/s, zero at 9 o'clock
            // six-pack (Nick 22:04, Trident style): attitude horizon bar, turn coordinator, heading
            var e = transform.eulerAngles;
            Needle("adi", -Wrap(e.z), dt);                                                  // horizon rolls against the bank (sign fixed 0.15.26, Nick 04:56)
            // 0.15.68 (Nick 04:07): globe attitude indicator. The ball turns for pitch (nose up shows more sky, the horizon
            // drops below the symbol) and rolls against the bank like the old bar
            if (gauges.TryGetValue("adiball", out var ab) && ab != null)
            {
                var want = Quaternion.Euler(0f, 0f, Wrap(e.z)) * Quaternion.Euler(Mathf.Clamp(-Wrap(e.x), -85f, 85f), 0f, 0f);
                ab.localRotation = Quaternion.Slerp(ab.localRotation, rest["adiball"] * want, 1f - Mathf.Exp(-dt * 8f));
            }
            // 0.15.29 (Nick 12:29, pinned at max): the game holds this engine at 100 % rpm whenever it runs (the prop governs),
            // so the dial shows 1400 rpm idle to 5800 at full throttle, with a little lag like a real engine
            // 0.15.68 (Nick 04:07: full throttle sat on the red line): full throttle reads 5500, the top of the green arc
            // (max continuous); yellow 5500-5800 and the red line stay on the dial
            // 0.15.70 (Nick 17:03): pre-rotator driving the rotor past 380 rpm pushes it up through the yellow (5500-5780)
            float engWant = er > 0.05f ? Mathf.Lerp(1400f, 5500f, thr) * Mathf.Clamp01(er) : 0f;
            if (er > 0.05f && preLoad > 0f) engWant = Mathf.Max(engWant, Mathf.Lerp(5500f, 5780f, preLoad));
            engDisp = Mathf.MoveTowards(engDisp, engWant, 3000f * dt);
            Needle("eng", -135f + 270f * Mathf.Clamp01(engDisp / 7000f), dt);
            Needle("hdgcard", -e.y, dt);                                                     // compass card turns under the fixed red pointer
            Needle("rotor", -135f + 270f * Mathf.Clamp01(rpm / 450f), dt);                   // rotor rpm, 0-450
            Needle("rpm", -135f + 270f * er, dt);
            Needle("fuel", -120f + 240f * fuel, dt);
            Needle("oil", er > 0.05f ? -135f + 270f * (0.45f + 0.25f * er) : -135f, dt);
            Needle("volt", -135f + 270f * (er > 0.05f ? 0.70f : 0.60f), dt);
        }
        // seat 3 soldier, from the hips up: a slow look-out scan to the sides and down, the head leading the body
        void Soldier()
        {
            if (sSpine == null || sHead == null) return;
            float t = Time.unscaledTime * (Time.timeScale > 0f ? 1f : 0f) + t0;
            // the torso stays still (a spine twist shears the plate carrier); only the head turns, inside the collar
            float hyaw = 32f * Mathf.Sin(t * 0.33f + 0.6f) + 10f * Mathf.Sin(t * 1.1f);
            float hpitch = 10f + 7f * Mathf.Sin(t * 0.47f + 2.1f);                       // positive X = looking down
            sSpine.localRotation = rSpine;
            sHead.localRotation = rHead * Quaternion.Euler(hpitch, hyaw, 0f);
        }
        // 0.15.28: the stock attack helicopter's blurred blade segments replace the blade copies above 230 rpm (stock
        // SwashRotor swaps at 60 % of its rotor speed); unlike stock the swap ignores game speed, so paused shots keep the blur
        void RotorBlur(bool headOn)
        {
            bool fast = headOn && !struck && rpm > 230f;
            if (fast == rblurOn) return; rblurOn = fast;
            foreach (var g in rblur) if (g != null) g.SetActive(fast);
            if (rinner != null) rinner.gameObject.SetActive(fast);
            foreach (var b in blades) if (b.Value != null) b.Value.gameObject.SetActive(!fast);
        }
        // 0.15.29 (Nick 12:29): blue inner rotor disc. Our own transparent materials never drew in game: they are copies of
        // the editor stand-ins on the URP Lit shader, which the game does not ship in its transparent form, while the stock
        // blur pieces get the game's real material. So the disc borrows the live stock blur material at runtime and only
        // puts our texture on it.
        Renderer rinner;
        void InnerDisc(Transform spin)
        {
            try
            {
                var t = spin.Find("UL2_rinner"); if (t == null) { Plugin.Log.LogInfo("UL-2 inner rotor disc: none"); return; }
                rinner = t.GetComponent<Renderer>(); var own = rinner != null ? rinner.sharedMaterial : null;
                Renderer stock = null; foreach (var g in rblur) if (g != null) { stock = g.GetComponent<Renderer>(); if (stock != null) break; }
                var sm = stock != null ? stock.sharedMaterial : null;
                Plugin.Log.LogInfo("UL-2 inner rotor disc: own shader " + (own != null && own.shader != null ? own.shader.name + " supported " + own.shader.isSupported : "none")
                    + " | stock blur shader " + (sm != null && sm.shader != null ? sm.shader.name + " supported " + sm.shader.isSupported + " queue " + sm.renderQueue : "none"));
                if (own == null || sm == null) return;
                Texture tex = own.HasProperty("_BaseMap") ? own.GetTexture("_BaseMap") : own.mainTexture;
                var m = new Material(sm) { name = "UL2_rinner_live" };
                foreach (var p in new[] { "_BaseMap", "_MainTex" }) if (m.HasProperty(p)) m.SetTexture(p, tex);
                foreach (var p in new[] { "_BaseColor", "_Color", "_TintColor" }) if (m.HasProperty(p)) m.SetColor(p, Color.white);
                rinner.sharedMaterial = m; rinner.gameObject.SetActive(false);
                Plugin.Log.LogInfo("UL-2 inner rotor disc on the live stock blur material, texture " + (tex != null ? tex.name : "none"));
            }
            catch (Exception e) { Plugin.Log.LogWarning("UL-2 inner rotor disc: " + e.Message); }
        }
        // the game paints liveries only into the airframe parts; the side gun housing rides on the gun mount, so it copies
        // the airframe skin's livery texture and gloss (one property block, checked twice a second)
        Renderer liverySrc; readonly List<Renderer> liveryDst = new List<Renderer>(); float liveryT; Texture liveryTex; MaterialPropertyBlock liveryMpb;
        void LiverySync(float dt)
        {
            if ((liveryT -= dt) > 0f) return; liveryT = 0.5f;
            try
            {
                if (liverySrc == null) foreach (var c in partList) { if (c == null) continue; if (c.name == "cockpit_F") { liverySrc = c.GetComponent<Renderer>(); break; } }
                if (liverySrc == null) return;
                liveryDst.RemoveAll(r => r == null);
                var roots = new List<Transform> { transform.root }; foreach (var c in partList) if (c != null && !roots.Contains(c.transform.root)) roots.Add(c.transform.root);
                foreach (var rt in roots) foreach (var r in rt.GetComponentsInChildren<Renderer>(true))
                    if (r.name == "housing" && !liveryDst.Contains(r)) { liveryDst.Add(r); liveryTex = null; }
                var m = liverySrc.sharedMaterial; if (m == null || !m.HasProperty("_Livery")) return;
                var tex = m.GetTexture("_Livery"); if (tex == liveryTex || liveryDst.Count == 0) return;
                liveryTex = tex; if (liveryMpb == null) liveryMpb = new MaterialPropertyBlock();
                liveryMpb.SetTexture("_Livery", tex); if (m.HasProperty("_Glossiness")) liveryMpb.SetFloat("_Glossiness", m.GetFloat("_Glossiness"));
                foreach (var r in liveryDst) r.SetPropertyBlock(liveryMpb, 0);
                Plugin.Log.LogInfo("UL-2 gun housing livery " + (tex != null ? tex.name : "none") + " on " + liveryDst.Count);
            }
            catch (Exception e) { if (!livErr) { livErr = true; Plugin.Log.LogWarning("UL-2 livery sync: " + e.Message); } }
        }
        bool livErr;
        // a blade that meets the ground or anything solid while turning breaks at that point, both blades lose that segment
        // and everything outboard (stock SwashRotor.BreakSegments), the rotor stops and the stock rotor strike dust plays
        void RotorStrike()
        {
            if ((colScanT -= Time.unscaledDeltaTime) <= 0f)
            {
                colScanT = 2f; ownCols.Clear();
                foreach (var c in partList) if (c != null) foreach (var col in c.GetComponentsInChildren<Collider>(true)) ownCols.Add(col);
                if (unit != null) foreach (var col in unit.GetComponentsInChildren<Collider>(true)) ownCols.Add(col);
            }
            for (int s = 0; s < 2; s++)
            {
                if (bladeDir[s] == Vector3.zero) continue;
                Vector3 c0 = spin.position, d = spin.TransformDirection(bladeDir[s]);
                float best = 99f; RaycastHit bh = default(RaycastHit);
                foreach (var h in Physics.RaycastAll(c0 + d * 0.6f, d, ROTOR_R - 0.6f, ~((1 << 2) | (1 << 3) | (1 << 5) | (1 << 9) | (1 << 10) | (1 << 13) | (1 << 15)), QueryTriggerInteraction.Ignore))
                {
                    if (h.collider == null || ownCols.Contains(h.collider)) continue;
                    if (h.collider.attachedRigidbody != null && ownCols.Contains(h.collider)) continue;
                    if (h.distance < best) { best = h.distance; bh = h; }
                }
                if (best > 50f) continue;
                float r = best + 0.6f; int seg = r < 1.9f ? 1 : r < 3.4f ? 2 : 3;
                struck = true; rpm = 0f;
                Plugin.Log.LogInfo("UL-2 rotor strike on " + bh.collider.name + " at " + r.ToString("0.00") + " m, breaking segment " + seg);
                var mBreak = AccessTools.Method(AccessTools.TypeByName("AeroPart"), "BreakJointsToPart");
                for (int side = 0; side < 2; side++)
                {
                    var child = wingParts[side, seg - 1]; var parent = seg == 1 ? headPart : wingParts[side, seg - 2];
                    if (child != null && parent != null && mBreak != null) try { mBreak.Invoke(child, new object[] { parent }); } catch (Exception e) { Plugin.Log.LogWarning("UL-2 rotor strike break: " + e.Message); }
                }
                try
                {
                    var tGA = AccessTools.TypeByName("GameAssets"); var gi = tGA == null ? null : AccessTools.Field(tGA, "i")?.GetValue(null) ?? AccessTools.Property(tGA, "i")?.GetValue(null, null);
                    var pf = gi == null ? null : AccessTools.Field(tGA, "rotorStrike_dirt")?.GetValue(gi) as GameObject;
                    if (pf != null) Destroy(Instantiate(pf, bh.point, Quaternion.LookRotation(Vector3.up + d * 0.3f)), 20f);
                }
                catch { }
                return;
            }
        }
        void Needle(string id, float deg, float dt)
        {
            Transform t; if (!gauges.TryGetValue(id, out t) || t == null) return;
            float a; shown.TryGetValue(id, out a);
            a = Mathf.LerpAngle(a, deg, 1f - Mathf.Exp(-dt * 6f)); shown[id] = a;
            t.localRotation = rest[id] * Quaternion.Euler(0f, 0f, a);      // pivot Z faces the pilot: positive turns clockwise as seen from the seat
        }

        static float Wrap(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }
        bool Detached(Component part) { try { return mDetached != null && (bool)mDetached.Invoke(part, null); } catch { return false; } }

        void StowBlades()
        {
            float a0 = 99f; try { if (fAlt != null) a0 = (float)fAlt.GetValue(unit); } catch { }
            // same test as the stock RotorShaft (Ibis): folded only when spawned at its ground spawn height (spawnOffset 1.15 + 1 m)
            if (a0 > 2.15f) { Plugin.Log.LogInfo($"UL-2 spawned in the air ({a0:0.0} m): blades start unfolded"); return; }
            var fw = spin.InverseTransformDirection(transform.forward); fw.y = 0f;
            if (fw.sqrMagnitude < 1e-4f) return; fw.Normalize();
            string[] sides = { "L", "R" }; string[] seg = { "root", "mid", "tip" };
            for (int i = 0; i < 2; i++)
            {
                var root = spin.Find("spin_" + sides[i] + "_root"); if (root == null || bladeDir[i] == Vector3.zero) continue;
                var pv = new GameObject("UL2_fold_" + sides[i]).transform; pv.SetParent(spin, false);
                pv.position = root.position; pv.localRotation = Quaternion.identity;                 // fold hinge at the blade grip
                foreach (var g in seg) { var v = spin.Find("spin_" + sides[i] + "_" + g); if (v != null) v.SetParent(pv, true); }
                var d = bladeDir[i]; d.y = 0f;
                foldAng[i] = Vector3.SignedAngle(d.normalized, fw, Vector3.up);
                pv.localRotation = Quaternion.AngleAxis(foldAng[i], Vector3.up); fold[i] = pv;
            }
            foldOn = fold[0] != null || fold[1] != null; foldT = 0f;
            Plugin.Log.LogInfo($"UL-2 blades stowed forward ({foldAng[0]:0} / {foldAng[1]:0} deg), unfolding 2 s after spawn");
        }

        void Setup()
        {
            var tUnit = AccessTools.TypeByName("Unit"); var tPart = AccessTools.TypeByName("UnitPart");
            fSpeed = fSpeed ?? AccessTools.Field(tUnit, "speed"); fParts = fParts ?? AccessTools.Field(tUnit, "partLookup");
            mDetached = mDetached ?? AccessTools.Method(tPart, "IsDetached");
            unit = GetComponent(tUnit);
            var parts = new Dictionary<string, Component>();
            foreach (var c in GetComponentsInChildren(tPart, true)) parts[c.name] = c;
            if (fParts?.GetValue(unit) is System.Collections.IEnumerable pl)
                foreach (var o in pl) if (o is Component c && c != null) parts[c.name] = c;
            if (!parts.TryGetValue("rotorHead", out headPart)) return;
            mRpm = mRpm ?? AccessTools.Method(AccessTools.TypeByName("TurbineEngine"), "GetRPMRatio");
            mFuel = mFuel ?? AccessTools.Method(AccessTools.TypeByName("Aircraft"), "GetFuelLevel"); fAlt = fAlt ?? AccessTools.Field(tUnit, "radarAlt");
            mInputs = mInputs ?? AccessTools.Method(AccessTools.TypeByName("Aircraft"), "GetInputs"); pRb = pRb ?? AccessTools.Property(tUnit, "rb");
            var tEng = AccessTools.TypeByName("TurbineEngine");
            foreach (var c in parts.Values)
            {
                if (c == null) continue;
                foreach (var t in c.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "UL2_sold_spine") { sSpine = t; rSpine = t.localRotation; }
                    else if (t.name == "UL2_sold_head") { sHead = t; rHead = t.localRotation; }
                    else if (t.name.StartsWith("UL2_gauge_")) { gauges[t.name.Substring(10)] = t; rest[t.name.Substring(10)] = t.localRotation; }
                    else if (t.name.StartsWith("UL2_pedal_")) { pedals[t.name.Substring(10)] = t; pedalRest[t.name.Substring(10)] = t.localPosition; }
                    else if (t.name.StartsWith("UL2_pedal2_")) { pedals2[t.name.Substring(11)] = t; pedal2Rest[t.name.Substring(11)] = t.localRotation; }
                    else if (t.name == "UL2_ctrl2") ctrl2 = t;
                    else if (t.name == "UL2_stick2") { stick2 = t; stick2Rest = t.localRotation; }
                    else if (t.name == "UL2_thr2") { thr2 = t; thr2Rest = t.localRotation; }
                    else if (t.name == "copilot" && cop == null) cop = t;
                }
                if (engine == null && tEng != null) { var e = c.GetComponentInChildren(tEng, true); if (e != null) engine = e; }
            }
            if (cop == null) foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "copilot") { cop = t; break; }
            if (cop != null)
                for (int i = 0; i < 2; i++)
                {
                    string sd = i == 0 ? "L" : "R";
                    arms[i] = new[] { Deep(cop, "upperarm_" + sd), Deep(cop, "forearm_" + sd), Deep(cop, "hand_" + sd) };
                    if (arms[i].Any(b => b == null)) arms[i] = null;
                }
            try
            {
                // 0.15.54 (Nick 20:25: pilot hands not on the controls): the parts are detached from the root at spawn, so
                // searching the root alone found no pilot, stick or throttle; search every part like the copilot does
                var all = new List<Transform>(GetComponentsInChildren<Transform>(true));
                foreach (var c in parts.Values) if (c != null) all.AddRange(c.GetComponentsInChildren<Transform>(true));
                foreach (var t in all)
                {
                    if (t.name == "pilot" && pil == null) pil = t;
                    else if (t.name == "joystick" && jsT == null) jsT = t;
                    else if (t.name == "throttle" && thT == null && t.parent != null && t.parent.name == "throttleZero") thT = t;
                    else if (t.name == "UL2_ext_stick") exStick = t;
                    else if (t.name == "UL2_ext_throttle") exThr = t;
                    else if (t.name.StartsWith("UL2_sleeve_") && t.name.Length == 13) { var arr = t.name[11] == 'P' ? slP : slC; arr[t.name[12] == 'L' ? 0 : 1] = t; }
                }
                if (pil != null)
                    for (int i = 0; i < 2; i++)
                    {
                        string sd = i == 0 ? "L" : "R";
                        parms[i] = new[] { Deep(pil, "upperarm_" + sd), Deep(pil, "forearm_" + sd), Deep(pil, "hand_" + sd) };
                        if (parms[i].Any(b => b == null)) parms[i] = null;
                    }
                // wrist points in the stick / throttle frames, from the aircraft-frame grip centres (stick grip 0, 0.193, 1.661;
                // throttle knob 0.013, 0.163 above the lever root) with the wrist just behind and outboard of each grip
                // 0.15.54: gloved hands are modelled on the stick and throttle; the wrists go to the glove cuffs (mesh-local points
                // in the stick / throttle frames, from the model) and the stock hands fold away inside them
                // 0.15.72: stock hands on the grips; the hand's grip hole (index side up) sits on each grip axis
                jsWrist = new Vector3(0.03875f, 0.80026f, -0.29020f); jsHand = new Quaternion(-0.12153f, 0.71428f, 0.67946f, 0.11561f);
                thWrist = new Vector3(-0.01908f, 0.14400f, -0.11770f); thHand = new Quaternion(0.11847f, 0.69711f, 0.69711f, -0.11847f);
                Plugin.Log.LogInfo("UL-2 pilot hands: pilot " + (pil != null) + " stick " + (jsT != null) + " throttle " + (thT != null) + " arms " + (parms[0] != null) + "/" + (parms[1] != null) + " exterior copies " + (exStick != null) + "/" + (exThr != null));
            }
            catch (Exception ex) { Plugin.Log.LogWarning("UL-2 pilot hands setup: " + ex.Message); }
            var tAero = AccessTools.TypeByName("AeroPart"); mSetArea = mSetArea ?? AccessTools.Method(tAero, "SetWingArea"); mGetArea = mGetArea ?? AccessTools.Method(tAero, "GetWingArea");
            rotorBlades.Clear();
            foreach (var s in new[] { "L", "R" })
                for (int k = 1; k <= 3; k++)
                    if (parts.TryGetValue("wing" + k + "_" + s, out var wp) && wp != null && tAero.IsInstanceOfType(wp))
                        rotorBlades.Add(new Blade { part = wp, ln = wp.transform.Find(wp.name + "_liftNormal"), area = (float)mGetArea.Invoke(wp, null) });
            Plugin.Log.LogInfo("UL-2 gyro rotor: " + rotorBlades.Count + " blade surfaces, " + rotorBlades.Count(b => b.ln != null) + " lift transforms, area " + rotorBlades.Sum(b => b.area) + " m2");
            Plugin.Log.LogInfo("UL-2 copilot controls: controller " + (ctrl2 != null) + " stick " + (stick2 != null) + " throttle " + (thr2 != null) + " pedals " + pedals2.Count + " arms " + (arms[0] != null) + "/" + (arms[1] != null));
            spin = headPart.transform.Find("rotorSpin");
            if (spin == null) return;
            partList.Clear(); partList.AddRange(parts.Values);
            var tg = new GameObject("rotorTilt").transform; tg.SetParent(spin.parent, false);
            tg.localPosition = spin.localPosition; tg.localRotation = Quaternion.identity; spin.SetParent(tg, true); tilt = tg;
            foreach (var s in new[] { "L", "R" })
            {
                string[] seg = { "root", "mid", "tip" }; string[] wing = { "wing1_", "wing2_", "wing3_" };
                for (int k = 0; k < 3; k++)
                {
                    var vis = spin.Find("spin_" + s + "_" + seg[k]);
                    if (vis != null && parts.TryGetValue(wing[k] + s, out var wp)) blades.Add(new KeyValuePair<Component, Transform>(wp, vis));
                    Component wpk; parts.TryGetValue(wing[k] + s, out wpk); wingParts[s == "L" ? 0 : 1, k] = wpk;
                }
                var tipv = spin.Find("spin_" + s + "_tip"); var tmf = tipv != null ? tipv.GetComponent<MeshFilter>() : null;
                if (tmf != null && tmf.sharedMesh != null)
                {
                    var dw = tipv.TransformPoint(tmf.sharedMesh.bounds.center) - spin.position; dw -= Vector3.Project(dw, spin.up);
                    bladeDir[s == "L" ? 0 : 1] = spin.InverseTransformDirection(dw.normalized);
                }
            }
            try { StowBlades(); } catch (System.Exception e) { foldOn = false; Plugin.Log.LogInfo("UL-2 blade stow skipped: " + e.Message); }
            rblur.Clear(); foreach (Transform ch in spin) if (ch.name.StartsWith("UL2_rblur_")) rblur.Add(ch.gameObject);
            InnerDisc(spin);
            Plugin.Log.LogInfo("UL-2 rotor blur segments " + rblur.Count + ", blade strike lines " + (bladeDir[0] != Vector3.zero) + "/" + (bladeDir[1] != Vector3.zero));
            ready = true; spawnT = Time.time;
            // 0.15.5 diagnostics: log every part that breaks off, and when (tail / rotor blade reports)
            var ev = tPart.GetEvent("onPartDetached");
            if (ev != null)
            {
                var h = Delegate.CreateDelegate(ev.EventHandlerType, this, AccessTools.Method(GetType(), nameof(OnDetached)).MakeGenericMethod(tPart));
                foreach (var c in parts.Values) if (c != null && tPart.IsInstanceOfType(c)) ev.AddEventHandler(c, h);
            }
            Plugin.Log.LogInfo("UL-2 rotor ready, " + blades.Count + " blade sections, " + gauges.Count + " gauges, " + pedals.Count + " pedals, engine " + (engine != null) + ", soldier " + (sSpine != null && sHead != null));
        }
    }

    // Seat 3 passenger: poses the kit's bones from the baked table (heading x elevation, same IK solutions as the
    // Blender rig), following the stock turret it rides on. The visual heading moves linearly inside the side arcs, so a
    // swap from one side to the other always goes forward over the seat 2 crewman, never round the back.
    // Passenger module machine gunners (Nick 22:51): the stock turret slews traverse and elevation independently, so swinging
    // forward at a low aim put the barrel through the stub grenadier's head. Inside the grenadier's heading band the gun must be
    // at least CLEAR deg up: the elevation is lifted first and the traverse is held at the band edge until it is up, so the
    // gun passes over his head. Headings are in the gunner's outward seat frame (0 = straight out, + = clockwise from above),
    // from the head exclusion cones in UL2PassengerModule.cs (right: -66 +-8, left: 70 +-8, both 2 deg down).
    internal static class PmArc
    {
        class Cfg { public float centre, lo, hi; }
        const float CLEAR = 12f, MARGIN = 4f;
        static readonly Dictionary<int, Cfg> cfg = new Dictionary<int, Cfg>();
        static FieldInfo fT, fE, fTR, fER, fElev;
        static Cfg Get(Component t)
        {
            int id = t.GetInstanceID(); Cfg c;
            if (cfg.TryGetValue(id, out c)) return c;
            c = null;
            for (var p = t.transform.parent; p != null; p = p.parent)
            {
                if (p.name == "pm_mg_R") { c = new Cfg { centre = -11.5f, lo = -76f, hi = -46f }; break; }   // 0.15.85: head + neck + shoulders
                if (p.name == "pm_mg_L") { c = new Cfg { centre = 11.5f, lo = 50f, hi = 80f }; break; }
                if (p.name.StartsWith("UL2_pm") || p.parent == null) break;
            }
            if (c != null && fT == null)
            {
                var tt = t.GetType();
                fT = AccessTools.Field(tt, "traverseAngle"); fE = AccessTools.Field(tt, "elevationAngle");
                fTR = AccessTools.Field(tt, "traverseRate"); fER = AccessTools.Field(tt, "elevationRate"); fElev = AccessTools.Field(tt, "elevationTransform");
            }
            cfg[id] = c; return c;
        }
        public static void Pre(Component __instance, out float __state)
        {
            __state = float.NaN;
            try { if (__instance != null && Get(__instance) != null && fT != null) __state = (float)fT.GetValue(__instance); } catch { }
        }
        public static void Post(Component __instance, float __state)
        {
            try
            {
                if (float.IsNaN(__state)) return;
                var c = cfg[__instance.GetInstanceID()];
                float tr = (float)fT.GetValue(__instance), el = (float)fE.GetValue(__instance);   // elevationAngle: negative = up
                float h0 = c.centre + __state, h1 = c.centre + tr, up = -el;
                bool in1 = h1 > c.lo - MARGIN && h1 < c.hi + MARGIN;
                if (!in1 || up >= CLEAR) return;
                bool in0 = h0 > c.lo - MARGIN && h0 < c.hi + MARGIN;
                float dt = Time.fixedDeltaTime, er = (float)fER.GetValue(__instance);
                if (!in0) { tr = __state; fT.SetValue(__instance, tr); __instance.transform.localEulerAngles = new Vector3(0f, tr, 0f); }   // hold at the band edge
                el = Mathf.MoveTowards(el, -CLEAR, Mathf.Max(er, 60f) * dt);                                                    // lift over the head
                fE.SetValue(__instance, el);
                var et = fElev.GetValue(__instance) as Transform; if (et != null) et.localEulerAngles = new Vector3(el, 0f, 0f);
            }
            catch { }
        }
    }

    public class Seat3Pose : MonoBehaviour
    {
        class Table { public float afr, afl, ar; public string[] bones; public float[] A, E, d; }
        static readonly Dictionary<string, Table> tables = new Dictionary<string, Table>();
        static Type tTurret, tAircraft, tDetector; static FieldInfo fElev, fWm, fMag, fVis, fManual; static MethodInfo mGetTarget; static MethodInfo mTargets;
        float lastA = 999f, lastE = 999f; bool posedOnce; SkinnedMeshRenderer smr;
        Table T; Transform[] bones; Behaviour turret; Transform elev; Component aircraft; string kind;
        float vA = 52f, vE = -40f, side = 1f, t0, logT;
        readonly Dictionary<Component, float> magOrig = new Dictionary<Component, float>(), visOrig = new Dictionary<Component, float>();

        static string RS(System.IO.BinaryReader r) { int n = r.ReadInt32(); return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n)); }
        static Table Load(string kind)
        {
            Table t; if (tables.TryGetValue(kind, out t)) return t;
            using (var s = typeof(Plugin).Assembly.GetManifestResourceStream("UL2Airtrike.UL2_s3_" + kind + "_pose.bytes"))
            {
                if (s == null) return null;
                var r = new System.IO.BinaryReader(s); t = new Table();
                RS(r); t.afr = r.ReadSingle(); t.afl = r.ReadSingle(); t.ar = r.ReadSingle();
                int nb = r.ReadInt32(); t.bones = new string[nb]; for (int i = 0; i < nb; i++) t.bones[i] = RS(r);
                int na = r.ReadInt32(); t.A = new float[na]; for (int i = 0; i < na; i++) t.A[i] = r.ReadSingle();
                int ne = r.ReadInt32(); t.E = new float[ne]; for (int i = 0; i < ne; i++) t.E[i] = r.ReadSingle();
                t.d = new float[ne * na * nb * 7]; for (int i = 0; i < t.d.Length; i++) t.d[i] = r.ReadSingle();
            }
            tables[kind] = t; return t;
        }

        void Start()
        {
            try
            {
                kind = name.Substring("UL2_s3kit_".Length); T = Load(kind); t0 = UnityEngine.Random.value * 50f;
                if (T == null) { Plugin.Log.LogWarning("UL-2 seat 3: no pose table for " + kind); enabled = false; return; }
                bones = new Transform[T.bones.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = transform.Find("b_" + T.bones[i]);
                tTurret = tTurret ?? AccessTools.TypeByName("Turret"); tAircraft = tAircraft ?? AccessTools.TypeByName("Aircraft");
                tDetector = tDetector ?? AccessTools.TypeByName("TargetDetector");
                fElev = fElev ?? AccessTools.Field(tTurret, "elevationTransform"); fManual = fManual ?? AccessTools.Field(tTurret, "manual"); mGetTarget = mGetTarget ?? AccessTools.Method(tTurret, "GetTarget"); fWm = fWm ?? AccessTools.Field(tAircraft, "weaponManager");
                mTargets = mTargets ?? AccessTools.Method(AccessTools.TypeByName("WeaponManager"), "GetTargetList");
                fMag = fMag ?? AccessTools.Field(tDetector, "magnification"); fVis = fVis ?? AccessTools.Field(tDetector, "visualRange");
                if (transform.parent != null) turret = transform.parent.GetComponentInChildren(tTurret, true) as Behaviour;
                if (turret != null) elev = fElev.GetValue(turret) as Transform;
                aircraft = GetComponentInParent(tAircraft);
                if (aircraft == null) foreach (var a in FindObjectsOfType(tAircraft)) { var c = (Component)a; if (c.name.StartsWith("UL2Airtrike") && (c.transform.position - transform.position).sqrMagnitude < 100f) { aircraft = c; break; } }
                // spotter (Nick 20:24): visual detection 6 km -> 10 km for the whole aircraft while he is aboard
                if (kind == "spot" && aircraft != null && fVis != null)
                    foreach (var det in aircraft.GetComponentsInChildren(tDetector, true))
                    {
                        float v = (float)fVis.GetValue(det); if (v < 1000f) continue;
                        visOrig[det] = v; fVis.SetValue(det, 10000f);
                        if (fMag != null) { float m = (float)fMag.GetValue(det); magOrig[det] = m; fMag.SetValue(det, m * 10000f / v); }
                    }
                Plugin.Log.LogInfo("UL-2 seat 3 " + kind + " ready, turret " + (turret != null) + ", aircraft " + (aircraft != null));
            }
            catch (Exception e) { Plugin.Log.LogError("UL-2 seat 3: " + e); enabled = false; }
        }
        // 0.15.23 (Nick 04:21, 04:24): any seat 3 passenger gives the aircraft 1/8 of a full Ibis troop load's capture power (16 troops,
        // 8 per bay, 8 capture each = 16), i.e. 2. Same rule as the stock MountedTroops: only while landed and stopped.
        static FieldInfo fSpd, fRalt; static MethodInfo mCap, mPartDet; static Type tUnitS; bool seatSearched; Component seatPart; bool capOn; float capT;
        const float CAPTURE = 2f;   // passenger module: four kits x 2 = 8, one full Ibis infantry bay (Nick 22:09)
        void Capture()
        {
            if ((capT -= Time.deltaTime) > 0f || aircraft == null) return; capT = 2f;
            // 0.15.84 (Nick 22:51: frame drops): AccessTools.TypeByName scans every loaded assembly; it ran every 2 s per kit
            var tU = tUnitS = tUnitS ?? AccessTools.TypeByName("Unit");
            fSpd = fSpd ?? AccessTools.Field(tU, "speed"); fRalt = fRalt ?? AccessTools.Field(tU, "radarAlt"); mCap = mCap ?? AccessTools.Method(tU, "ModifyCaptureStrength");
            if (seatPart == null && !seatSearched) { seatSearched = true; var tP = AccessTools.TypeByName("UnitPart"); seatPart = GetComponentInParent(tP); mPartDet = mPartDet ?? AccessTools.Method(tP, "IsDetached"); }
            bool gone = seatPart != null && mPartDet != null && (bool)mPartDet.Invoke(seatPart, null);
            bool on = !gone && fSpd != null && fRalt != null && (float)fSpd.GetValue(aircraft) < 2f && (float)fRalt.GetValue(aircraft) < 1f;
            if (on != capOn && mCap != null) { capOn = on; mCap.Invoke(aircraft, new object[] { on ? CAPTURE : -CAPTURE }); Plugin.Log.LogInfo("UL-2 seat 3 capture " + (on ? "on" : "off")); }
        }
        void OnDestroy() { try { if (capOn && aircraft != null && mCap != null) mCap.Invoke(aircraft, new object[] { -CAPTURE }); } catch { } foreach (var kv in magOrig) if (kv.Key != null) fMag.SetValue(kv.Key, kv.Value); foreach (var kv in visOrig) if (kv.Key != null) fVis.SetValue(kv.Key, kv.Value); }

        static float Wrap(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }

        void LateUpdate()
        {
            try
            {
                try { Capture(); } catch (Exception ce) { if (capT > -1f) Plugin.Log.LogWarning("UL-2 seat 3 capture: " + ce.Message); capT = 30f; }
                float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
                float tA = 0f, tE = 0f; bool active = false;
                if (turret != null && elev != null)
                {
                    bool man = false; object tg = null;
                    try { if (fManual != null) man = (bool)fManual.GetValue(turret); if (mGetTarget != null) tg = mGetTarget.Invoke(turret, null); } catch { }
                    active = turret.enabled && (man || (tg is Component tc && tc != null));
                    // 0.15.20 (Nick 03:55: shooter looks left of his aim): heading and elevation from the barrel's actual world
                    // direction in this kit's own frame, not the turret's local euler angles (which may sit on a rotated base)
                    var bd = transform.InverseTransformDirection(elev.forward);
                    float bA = Mathf.Atan2(bd.x, bd.z) * Mathf.Rad2Deg, bE = Mathf.Asin(Mathf.Clamp(bd.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float oA = Wrap(turret.transform.localEulerAngles.y), oE = -Wrap(elev.localEulerAngles.x);
                    if ((logT -= dt) <= 0f) { logT = 5f; Plugin.Log.LogInfo($"UL-2 seat 3 {kind} aim: barrel {bA:0.0}/{bE:0.0} turret euler {oA:0.0}/{oE:0.0} shown {vA:0.0}/{vE:0.0}"); }
                    tA = bA; tE = bE;
                }
                else if (kind == "spot" && aircraft != null && fWm != null && mTargets != null)
                {
                    var wm = fWm.GetValue(aircraft);
                    if (wm != null && mTargets.Invoke(wm, null) is System.Collections.IList l)
                        foreach (var o in l)
                        {
                            var u = o as Component; if (u == null) continue;
                            var d = transform.InverseTransformDirection(u.transform.position - (transform.position + transform.up * 0.6f));
                            tA = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; tE = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                            active = true; break;
                        }
                }
                float t = Time.unscaledTime + t0;
                if (!active && kind.StartsWith("pm")) { tA = 0f; tE = -12f; }   // passenger module: faces out, idle weapon low and straight out
                else if (!active) { tA = side * (kind == "spot" ? 75f + 20f * Mathf.Sin(t * 0.21f) : 52f); tE = kind == "spot" ? -12f + 6f * Mathf.Sin(t * 0.17f + 1.3f) : -12f; }   // idle: sitting up, weapon slightly low on his side (Nick 21:53: -40 read as slouched); spotter scans
                tA = Mathf.Clamp(tA, -T.ar, T.ar);
                if (tA > T.afr) side = 1f; else if (tA < -T.afl) side = -1f;
                vA = Mathf.MoveTowards(vA, tA, 150f * dt);
                vE = Mathf.MoveTowards(vE, Mathf.Clamp(tE, T.E[0], T.E[T.E.Length - 1]), 90f * dt);
                // 0.15.84 (Nick 22:51: frame drops): writing the bones every frame re-skins the whole soldier (20-30k verts) even
                // when he holds still or is off screen; only pose him when the pose changed and a renderer can be seen
                if (Mathf.Abs(vA - lastA) > 0.05f || Mathf.Abs(vE - lastE) > 0.05f)
                {
                    if (smr == null) smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (smr == null || smr.isVisible || !posedOnce) { Apply(vA, vE); lastA = vA; lastE = vE; posedOnce = true; }
                }
            }
            catch (Exception e) { Plugin.Log.LogError("UL-2 seat 3 pose: " + e); enabled = false; }
        }

        static void Cell(float[] g, float v, out int i, out float f)
        {
            i = 0; while (i < g.Length - 2 && v > g[i + 1]) i++;
            f = Mathf.Clamp01((v - g[i]) / (g[i + 1] - g[i]));
        }
        void Apply(float a, float e)
        {
            int ia, ie; float fa, fe; Cell(T.A, a, out ia, out fa); Cell(T.E, e, out ie, out fe);
            int nb = T.bones.Length, na = T.A.Length;
            for (int b = 0; b < nb; b++)
            {
                if (bones[b] == null) continue;
                Vector3 p = Vector3.zero; Vector4 q = Vector4.zero; Vector4 q0 = Vector4.zero;
                for (int k = 0; k < 4; k++)
                {
                    int ja = ia + (k & 1), je = ie + (k >> 1);
                    float w = ((k & 1) == 1 ? fa : 1f - fa) * ((k >> 1) == 1 ? fe : 1f - fe);
                    int o = ((je * na + ja) * nb + b) * 7;
                    p += new Vector3(T.d[o], T.d[o + 1], T.d[o + 2]) * w;
                    var qq = new Vector4(T.d[o + 3], T.d[o + 4], T.d[o + 5], T.d[o + 6]);
                    if (k == 0) q0 = qq; else if (Vector4.Dot(qq, q0) < 0f) qq = -qq;
                    q += qq * w;
                }
                q.Normalize();
                bones[b].localPosition = p; bones[b].localRotation = new Quaternion(q.x, q.y, q.z, q.w);
            }
        }
    }

    // 0.15.63 (Nick 03:01): the stock AI landing aborts in Touched_Down when radarAlt > 1 m. radarAlt is measured from the
    // aircraft origin, which sits 1.15 m above the wheels on the UL-2 (spawnOffset), so every touchdown read as a bounce and
    // it went around. Same fix as the PB-22 and P220: while on the runway the landing state sees the height of the wheels
    // minus a margin (abort only above ~2 m of real bounce), a gentle nose-down push keeps it on the ground, and on leaving
    // the state it deregisters from the runway (stock bug: followers otherwise abort for "traffic ahead").
    internal static class LandingFix
    {
        const float TouchedDown = 4f;   // AIPilotLandingState.LandingMode.Touched_Down
        const float WHEELS = 1.15f, MARGIN = 1.0f;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo radarAlt, fAc, fMode, fCi, fUsage, fSpeed, fRb; static FieldInfo fPitch; static FieldInfo fRunway; static MethodInfo mDereg;
        public static void Patch(Harmony h)
        {
            try
            {
                var t = AccessTools.TypeByName("AIPilotLandingState"); var m = t == null ? null : AccessTools.Method(t, "RunMode");
                radarAlt = AccessTools.Field(AccessTools.TypeByName("Unit"), "radarAlt");
                if (m == null || radarAlt == null) { Plugin.Log.LogWarning("UL-2: AI landing state not found, landing fix off"); return; }
                fAc = AccessTools.Field(t, "aircraft"); fMode = AccessTools.Field(t, "landingMode"); fCi = AccessTools.Field(t, "controlInputs"); fUsage = AccessTools.Field(t, "runwayUsage");
                h.Patch(m, prefix: new HarmonyMethod(typeof(LandingFix), nameof(Pre)), postfix: new HarmonyMethod(typeof(LandingFix), nameof(Post)));
                var sw = AccessTools.Method(t, "SwitchMode"); if (sw != null) h.Patch(sw, prefix: new HarmonyMethod(typeof(LandingFix), nameof(Switch)));
                var leave = AccessTools.Method(t, "LeaveState"); if (leave != null) h.Patch(leave, prefix: new HarmonyMethod(typeof(LandingFix), nameof(Leave)));
                Plugin.Log.LogInfo("UL-2 landing fix: run " + (m != null) + ", switch " + (sw != null) + ", leave " + (leave != null) + ", fields " + (fAc != null && fMode != null && fCi != null));
            }
            catch (Exception e) { Plugin.Log.LogWarning("UL-2 landing fix not applied: " + e.Message); }
        }
        static Component Ours(object st)
        {
            var ac = fAc?.GetValue(st) as Component;
            return ac != null && ac.GetComponent<RotorSpinner>() != null ? ac : null;
        }
        static bool OnRunway(object st) { var v = fMode?.GetValue(st); return v != null && Convert.ToSingle(Convert.ToInt32(v)) == TouchedDown; }
        static void Pre(object __instance, out float __state)
        {
            __state = float.NaN;
            try
            {
                var ac = Ours(__instance); if (ac == null || !OnRunway(__instance)) return;
                float alt = (float)radarAlt.GetValue(ac); __state = alt;
                radarAlt.SetValue(ac, Mathf.Max(alt - WHEELS - MARGIN, 0f));
            }
            catch { }
        }
        static void Post(object __instance, float __state)
        {
            try
            {
                var ac = Ours(__instance); if (ac == null) return;
                if (!float.IsNaN(__state)) radarAlt.SetValue(ac, __state);
                if (!OnRunway(__instance)) return;
                var ci = fCi?.GetValue(__instance); if (ci == null) return;
                fPitch = fPitch ?? ci.GetType().GetField("pitch"); if (fPitch == null) return;
                // in this game pitch > 0 is stick forward (nose down)
                float pitchDeg = Mathf.Asin(Mathf.Clamp(ac.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                float push = Mathf.Clamp(pitchDeg * 0.15f, 0f, 0.6f);
                if ((float)fPitch.GetValue(ci) < push) fPitch.SetValue(ci, push);
            }
            catch { }
        }
        static void Switch(object __instance, object[] __args)
        {
            try
            {
                var ac = Ours(__instance); if (ac == null) return;
                var from = fMode?.GetValue(__instance); var to = __args != null && __args.Length > 0 ? __args[0] : null;
                if (from == null || to == null || from.Equals(to)) return;
                Plugin.Log.LogInfo("UL-2 AI landing #" + ac.GetInstanceID() + ": " + from + " -> " + to + ", height " + ((float)radarAlt.GetValue(ac)).ToString("F2")
                    + " m, pitch " + (Mathf.Asin(Mathf.Clamp(ac.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg).ToString("F1"));
            }
            catch { }
        }
        static void Leave(object __instance)
        {
            try
            {
                var ac = Ours(__instance); if (ac == null || !OnRunway(__instance)) return;
                var usage = fUsage?.GetValue(__instance); if (usage == null) return;
                fRunway = fRunway ?? AccessTools.Field(usage.GetType(), "Runway");
                var runway = fRunway?.GetValue(usage); if (runway == null) return;
                mDereg = mDereg ?? AccessTools.Method(runway.GetType(), "DeregisterLanding");
                if (mDereg != null) mDereg.Invoke(runway, new object[] { ac });
            }
            catch { }
        }
    }

    // 0.15.86 (Nick 23:16): the stock takeoff state aims 300 m straight down the runway until 75 m radar altitude. The UL-2
    // climbs slowly, so it stays on the centreline for a long time in the way of traffic behind it. Once it is clear of the
    // ground the aim point is swung 25 degrees to one side (eased in from 3 to 12 m) so it banks gently off the centreline.
    // Each aircraft keeps one side; odd and even instances go opposite ways so a stream of takeoffs fans out.
    internal static class TakeoffFix
    {
        const float SIDE_DEG = 25f, START = 3f, FULL = 12f, DIST = 300f;
        static FieldInfo fAc, fTaken, fRv, radarAlt, fX, fZ;
        [ThreadStatic] static bool active; [ThreadStatic] static Vector3 shift;
        public static void Patch(Harmony h)
        {
            try
            {
                var t = AccessTools.TypeByName("AIPilotTakeoffState"); var m = t == null ? null : AccessTools.Method(t, "FixedUpdateState");
                var tAp = AccessTools.TypeByName("AutopilotPlane"); var tGp = AccessTools.TypeByName("GlobalPosition");
                var aim = tAp == null || tGp == null ? null : AccessTools.Method(tAp, "AutoAim", new[] { tGp, typeof(bool), typeof(bool), typeof(bool), typeof(float), typeof(float), typeof(bool), typeof(float), typeof(Vector3) });
                fAc = AccessTools.Field(t, "aircraft"); fTaken = AccessTools.Field(t, "takenOff"); fRv = AccessTools.Field(t, "runwayVector");
                radarAlt = AccessTools.Field(AccessTools.TypeByName("Unit"), "radarAlt");
                fX = tGp == null ? null : AccessTools.Field(tGp, "x"); fZ = tGp == null ? null : AccessTools.Field(tGp, "z");
                if (m == null || aim == null || fAc == null || fTaken == null || fRv == null || radarAlt == null || fX == null || fZ == null)
                { Plugin.Log.LogWarning("UL-2: AI takeoff state not found, takeoff side-step off"); return; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(TakeoffFix), nameof(Pre)), postfix: new HarmonyMethod(typeof(TakeoffFix), nameof(Post)));
                h.Patch(aim, prefix: new HarmonyMethod(typeof(TakeoffFix), nameof(PreAim)));
                Plugin.Log.LogInfo("UL-2 takeoff side-step patched");
            }
            catch (Exception e) { Plugin.Log.LogWarning("UL-2 takeoff side-step not applied: " + e.Message); }
        }
        static void Pre(object __instance)
        {
            active = false;
            try
            {
                var ac = fAc.GetValue(__instance) as Component;
                if (ac == null || !ac.name.StartsWith("UL2Airtrike") || !(bool)fTaken.GetValue(__instance)) return;
                float alt = (float)radarAlt.GetValue(ac); if (alt <= START) return;
                Vector3 rv = (Vector3)fRv.GetValue(__instance); rv.y = 0f; if (rv.sqrMagnitude < 0.01f) return;
                rv.Normalize();
                float side = (ac.GetInstanceID() & 1) == 0 ? 1f : -1f;
                float ang = SIDE_DEG * side * Mathf.Clamp01((alt - START) / (FULL - START));
                shift = (Quaternion.Euler(0f, ang, 0f) * rv - rv) * DIST;
                active = true;
            }
            catch { }
        }
        static void Post() { active = false; }
        static void PreAim(object[] __args)
        {
            if (!active) return;
            active = false;
            try
            {
                object gp = __args[0];
                fX.SetValue(gp, (float)fX.GetValue(gp) + shift.x);
                fZ.SetValue(gp, (float)fZ.GetValue(gp) + shift.z);
                __args[0] = gp;
            }
            catch { }
        }
    }

    // 0.15.64 (Nick 03:07): AGR-18 Lynchpins fired by the AI all missed. Stock flow: the AI fills the weapon target list and
    // fires on the same tick, but the nose LaserDesignator only lases what is in that list every 0.25 s. A rocket that
    // leaves before its target is lased starts with no target and never picks one up again (LaserSeeker.Initialize), so it
    // flies straight. For the UL-2 AI only, the target is put in the list first and the shot is held until the laser is on
    // it (at most 3 s, then the stock shot goes anyway). Every UL-2 laser rocket logs whether it had the laser at launch.
    internal static class LaserFix
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo fAc, fCur, fActive, fWm, fSeekTarget, fSeekMissile;
        static MethodInfo mDes, mIsLased, mList, mChanged, mOwner;
        static bool holdFire; static int waitId; static float waitStart, logT;
        public static void Patch(Harmony h)
        {
            try
            {
                var t = AccessTools.TypeByName("AIPilotCombatModes");
                var use = AccessTools.Method(t, "UseLaserGuided");
                fAc = AccessTools.Field(t, "aircraft"); fCur = AccessTools.Field(t, "currentTarget"); fActive = AccessTools.Field(t, "activeLaserGuidedMissiles");
                var tAc = AccessTools.TypeByName("Aircraft"); fWm = AccessTools.Field(tAc, "weaponManager"); mDes = AccessTools.Method(tAc, "GetLaserDesignator");
                var tLd = AccessTools.TypeByName("LaserDesignator"); mIsLased = AccessTools.Method(tLd, "IsLased");
                var tWm = AccessTools.TypeByName("WeaponManager"); mList = AccessTools.Method(tWm, "GetTargetList"); mChanged = AccessTools.Method(tWm, "TargetListChanged");
                var fire = AccessTools.Method(tWm, "Fire", Type.EmptyTypes);
                bool ok = use != null && fire != null && fAc != null && fCur != null && fActive != null && fWm != null && mDes != null && mIsLased != null && mList != null;
                if (ok)
                {
                    h.Patch(use, prefix: new HarmonyMethod(typeof(LaserFix), nameof(PreUse)), postfix: new HarmonyMethod(typeof(LaserFix), nameof(PostUse)));
                    h.Patch(fire, prefix: new HarmonyMethod(typeof(LaserFix), nameof(PreFire)));
                }
                var tSeek = AccessTools.TypeByName("LaserSeeker"); var init = AccessTools.Method(tSeek, "Initialize");
                var tMs = AccessTools.TypeByName("MissileSeeker"); fSeekTarget = AccessTools.Field(tMs, "targetUnit"); fSeekMissile = AccessTools.Field(tMs, "missile");
                mOwner = AccessTools.PropertyGetter(AccessTools.TypeByName("Missile"), "owner");
                if (init != null && fSeekTarget != null && fSeekMissile != null && mOwner != null) h.Patch(init, postfix: new HarmonyMethod(typeof(LaserFix), nameof(PostInit)));
                var seek = AccessTools.Method(tSeek, "Seek"); fHasLock = AccessTools.Field(tSeek, "hasLock");
                if (seek != null && fSeekMissile != null && mOwner != null) h.Patch(seek, postfix: new HarmonyMethod(typeof(LaserFix), nameof(PostSeek)));
                Plugin.Log.LogInfo("UL-2 laser fix " + ok + ", seeker log " + (init != null));
            }
            catch (Exception e) { Plugin.Log.LogWarning("UL-2 laser fix not applied: " + e.Message); }
        }
        static Component Ours(object st)
        {
            var ac = fAc.GetValue(st) as Component;
            return ac != null && ac.GetComponent<RotorSpinner>() != null ? ac : null;
        }
        static void PreUse(object __instance)
        {
            holdFire = false;
            try
            {
                var ac = Ours(__instance); if (ac == null) return;
                var tgt = fCur.GetValue(__instance) as Component; if (tgt == null) return;
                var act = fActive.GetValue(__instance) as System.Collections.ICollection; if (act != null && act.Count > 0) return;
                var des = mDes.Invoke(ac, null); if (des == null) return;
                if ((bool)mIsLased.Invoke(des, new object[] { tgt })) { waitId = 0; return; }
                // not lased yet: make sure the designator has it in its list, and hold this tick's shot
                var wm = fWm.GetValue(ac); var list = mList.Invoke(wm, null) as System.Collections.IList;
                if (list != null && !list.Contains(tgt)) { list.Clear(); list.Add(tgt); mChanged?.Invoke(wm, null); }
                int id = tgt.GetInstanceID();
                if (waitId != id) { waitId = id; waitStart = Time.time; Plugin.Log.LogInfo("UL-2 laser: lasing " + tgt.name + " before firing"); }
                if (Time.time - waitStart < 3f) holdFire = true;
                else if (Time.time - logT > 5f) { logT = Time.time; Plugin.Log.LogInfo("UL-2 laser: " + tgt.name + " still not lased after 3 s (no line of sight from the nose?), firing anyway"); }
            }
            catch (Exception e) { Plugin.Log.LogWarning("UL-2 laser hold: " + e.Message); }
        }
        static void PostUse() { holdFire = false; }
        // 0.15.66: each UL-2 laser rocket logs its lock, height and range every second until it goes
        static FieldInfo fHasLock; static readonly Dictionary<int, float> seekLog = new Dictionary<int, float>();
        static void PostSeek(object __instance)
        {
            try
            {
                var m = fSeekMissile.GetValue(__instance) as Component; if (m == null) return;
                int id = m.GetInstanceID(); float now = Time.time;
                if (!seekLog.TryGetValue(id, out var nx)) { var own = mOwner.Invoke(m, null) as Component; if (own == null || own.GetComponent<RotorSpinner>() == null) { seekLog[id] = float.MaxValue; return; } nx = now; }
                if (now < nx) return; seekLog[id] = now + 1f;
                if (seekLog.Count > 400) seekLog.Clear();
                var tu = fSeekTarget.GetValue(__instance) as Component;
                bool lk = fHasLock != null && (bool)fHasLock.GetValue(__instance);
                float h = -1f; if (Physics.Raycast(m.transform.position - Vector3.up * 2f, Vector3.down, out var hit, 2000f, -1, QueryTriggerInteraction.Ignore)) h = hit.distance + 2f;
                Plugin.Log.LogInfo("UL-2 laser rocket #" + id + ": lock " + lk + ", target " + (tu != null ? tu.name : "none") + (tu != null ? ", range " + Vector3.Distance(tu.transform.position, m.transform.position).ToString("F0") + " m, off-bore " + Vector3.Angle(tu.transform.position - m.transform.position, m.transform.forward).ToString("F0") : "") + ", height " + h.ToString("F0") + " m");
            }
            catch { }
        }
        static bool PreFire() { return !holdFire; }
        static void PostInit(object __instance)
        {
            try
            {
                var m = fSeekMissile.GetValue(__instance) as Component; if (m == null) return;
                var own = mOwner.Invoke(m, null) as Component; if (own == null || own.GetComponent<RotorSpinner>() == null) return;
                var tu = fSeekTarget.GetValue(__instance) as Component;
                string ang = tu != null ? Vector3.Angle(tu.transform.position - m.transform.position, m.transform.forward).ToString("F1") : "-";
                Plugin.Log.LogInfo("UL-2 laser rocket launched: target " + (tu != null ? tu.name : "NONE (not lased)") + ", off-bore " + ang + " deg");
            }
            catch { }
        }
    }

}
