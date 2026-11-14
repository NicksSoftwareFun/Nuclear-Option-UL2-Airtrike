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
    [BepInPlugin("nick.nuclearoption.ul2airtrike", "UL-2 Airtrike TEST BUILD", "0.15.47")]
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
            }
            catch (Exception e) { Logger.LogError("UL-2 patch failed: " + e); }
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
        readonly Transform[][] parms = new Transform[2][]; Transform pil, jsT, thT, exStick, exThr; Vector3 jsWrist, thWrist; bool parmsLogged;
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
                bool manual = axis > 0.05f;
                autoSpin = !manual && fa && ground && engOn && thr > 0.05f;
                // 0.15.47 (Nick 02:41): with flight assist OFF the custom axis is the rotor rpm in flight too (raise or lower it);
                // in the air it sets the rpm outright instead of only adding to the autorotation. Axis at the bottom = released,
                // back to autorotation. Flight assist on keeps the old behaviour (pre-rotator only on the ground and 3 s after).
                bool manualAir = manual && !fa && !clutch;
                if (headOn && engOn && !struck && manualAir) target = 110f + 340f * axis;
                else if (headOn && clutch && engOn && !struck)
                {
                    float pre = 0f;
                    if (manual) pre = 110f + 340f * axis;
                    else if (fa && ground) pre = thr > 0.95f ? 450f : Mathf.Lerp(110f, 300f, thr);
                    if (rpm > 250f) upRate = 30f;
                    target = Mathf.Max(target, pre);
                }
                if (manual && (dbgT -= dt) <= 0f) { dbgT = 2f; Plugin.Log.LogInfo($"UL-2 pre-rotator axis {axis:0.00} rpm {rpm:0} alt {alt:0.00} engine {er:0.00} thr {thr:0.00} clutch {clutch}"); }
                prevAxis = axis;
                setRpm = target;
                rpm = Mathf.MoveTowards(rpm, target, (target > rpm ? upRate : 40f) * dt);
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
        bool inErr; float engDisp;
        // HUD rotor gauge (Nick 22:31/22:35/22:36): the Alkyon-style label from our HUD extras prefab (UL2_rotorGauge),
        // rpm readout and an idle (110) to full (450 rpm) bar, red until the rotor is fast enough to jump (400), then green
        // 0.15.20 (Nick 03:43): styled like the stock throttle gauge: carrot = set rpm, coloured arc bar + readout = actual rpm
        Transform hud, carrotPiv, readPiv; UnityEngine.UI.Image barImg; TMPro.TMP_Text hudRead, hudLabel; float hudFind, hudShow, setRpm;
        void HudGauge(float dt, bool engOn)
        {
            try
            {
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
                    Plugin.Log.LogInfo("UL-2 HUD rotor gauge found, bar " + (barImg != null) + " carrot " + (carrotPiv != null) + " readout " + (hudRead != null));
                }
                bool want = axis > 0.05f || autoSpin || (alt < 0.8f && engOn);
                if (want) hudShow = 3f; else if (hudShow > 0f) hudShow -= dt;
                bool show = hudShow > 0f;
                if (hud.gameObject.activeSelf != show) hud.gameObject.SetActive(show);
                if (!show) return;
                float f = Mathf.Clamp01(rpm / 450f), fs = Mathf.Clamp01(setRpm / 450f);
                var col = rpm >= 300f ? new Color(0.3f, 1f, 0.3f, 1f) : Color.Lerp(new Color(1f, 0.25f, 0.2f, 1f), new Color(1f, 0.6f, 0.1f, 1f), Mathf.Clamp01(rpm / 300f));
                if (barImg != null) { barImg.fillAmount = f; barImg.color = col; }
                if (carrotPiv != null) carrotPiv.localEulerAngles = new Vector3(0f, 0f, fs * 26f - 13f);
                if (readPiv != null) { readPiv.localEulerAngles = new Vector3(0f, 0f, f * 26f - 13f); foreach (Transform ch in readPiv) ch.localEulerAngles = new Vector3(0f, 0f, -(f * 26f - 13f)); }   // 0.15.29 (Nick 12:29): readout line stays flat like the throttle one
                if (hudRead != null) { hudRead.text = Mathf.RoundToInt(rpm) + " RPM"; hudRead.color = col; if (hudRead.transform.parent != readPiv) hudRead.transform.localEulerAngles = new Vector3(0f, 0f, -(f * 26f - 13f)); }
                if (hudLabel != null) hudLabel.text = axis > 0.05f ? "ROTOR MANUAL" : autoSpin ? "ROTOR AUTO" : "ROTOR";
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
        Component prop; static FieldInfo fpRpm, fpPitch, fpAoA, fpPow, fpAng; float propPeak, propLogT;
        void PropWatch(float dt)
        {
            try
            {
                if (prop == null) { var tp = AccessTools.TypeByName("ConstantSpeedProp"); if (tp == null) return; foreach (var c in partList) { if (c == null) continue; var pc = c.GetComponentInChildren(tp, true); if (pc != null) { prop = pc; break; } } if (prop == null) return;
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
        static FieldInfo fState; float aiChk, aiRawLog, aiGamMax, aiGamMin, aiObst = -1f; bool aiPilot;
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
                }
            }
            catch { aiPilot = false; }
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
            gMax = Mathf.Min(gMax, Mathf.Lerp(6f, -2f, Mathf.InverseLerp(120f, 220f, alt)));   // stay low: no climbing above ~150 m
            // 0.15.35 (Nick 22:05: lining up a seat 3 gun run it flies into the ground or obstacles): a floor on the flight
            // path from height above ground, plus a look-ahead along the flight path for terrain, trees and buildings
            float gMin = Mathf.Clamp(-(alt - 10f) * 0.5f, -15f, 0f);
            if (alt < 10f) gMin = Mathf.Lerp(4f, 0f, alt / 10f);
            float look = Mathf.Max(spd * 5f, 40f); Vector3 vd = v / vm, org = rb.worldCenterOfMass + vd * 6f;
            if (Physics.Raycast(org, vd, out var hit, look, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody != rb && !hit.transform.IsChildOf(transform.root))
            {
                float ttc = hit.distance / vm;                                         // seconds to impact on the current path
                gMin = Mathf.Max(gMin, Mathf.Lerp(12f, 2f, Mathf.InverseLerp(1.5f, 5f, ttc)));
                aiObst = hit.distance;
            }
            else aiObst = -1f;
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
            bool kit = false;
            foreach (var c in partList)
            {
                if (c == null) continue;
                foreach (var t in c.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("UL2_s3kit_")) { if (t.GetComponent<Seat3Pose>() == null) t.gameObject.AddComponent<Seat3Pose>(); if (t.gameObject.activeInHierarchy) kit = true; }
                    else if (t.name.StartsWith("UL2_harness") && t.name.Length == 12) { int k = t.name[11] - '1'; if (k >= 0 && k < 3) harness[k] = t.gameObject; }
                }
            }
            seat3Kit = kit;
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
                    // right hand: wrist behind the grip, hand along it; left hand on the throttle knob
                    if (parms[1] != null) Reach(parms[1], jsT.TransformPoint(jsWrist), transform.TransformDirection(new Vector3(0.7f, -1f, -0.3f)), jsT.TransformDirection(new Vector3(-0.25f, 0.15f, 1f)).normalized);
                    if (parms[0] != null) Reach(parms[0], thT.TransformPoint(thWrist), transform.TransformDirection(new Vector3(-0.7f, -1f, -0.3f)), thT.TransformDirection(new Vector3(0.25f, 0.15f, 1f)).normalized);
                    if (!parmsLogged) { parmsLogged = true; Plugin.Log.LogInfo("UL-2 pilot hands on stick and throttle, wrist miss R " + (parms[1] != null ? (parms[1][2].position - jsT.TransformPoint(jsWrist)).magnitude.ToString("0.000") : "-") + " L " + (parms[0] != null ? (parms[0][2].position - thT.TransformPoint(thWrist)).magnitude.ToString("0.000") : "-") + " m"); }
                }
                catch (Exception ex) { Plugin.Log.LogError("UL-2 pilot hands: " + ex); pil = null; }
            }
            if (ctrl2 == null || cop == null || !cop.gameObject.activeInHierarchy) return;
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var b = arms[i]; if (b == null || b[0] == null || b[1] == null || b[2] == null) continue;
                    float sx = i == 0 ? -1f : 1f;
                    // 0.15.31 (Nick 15:19): bigger handset with long handles; wrists sit behind and below the handles and the
                    // hands bend up along them, so the grip reads as holding a controller instead of clasped hands
                    Reach(b, ctrl2.TransformPoint(new Vector3(sx * 0.103f, -0.109f, -0.072f)), ctrl2.TransformDirection(new Vector3(sx * 0.7f, -1f, -0.3f)),
                        ctrl2.TransformDirection(new Vector3(-sx * 0.31f, 0.86f, 0.42f)).normalized);
                }
                if (!armsLogged) { armsLogged = true; Plugin.Log.LogInfo("UL-2 copilot hands on the controller, wrist miss L " + (arms[0] != null ? (arms[0][2].position - ctrl2.TransformPoint(new Vector3(-0.103f, -0.109f, -0.072f))).magnitude.ToString("0.000") : "-") + " m"); }
            }
            catch (Exception ex) { Plugin.Log.LogError("UL-2 copilot hands: " + ex); ctrl2 = null; }
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
            Needle("asi", -135f + 270f * Mathf.Clamp01(spd * 3.6f / 280f), dt);              // 0-280 km/h (0.15.26, yellow 200-240)
            Needle("alt", alt % 1000f / 1000f * 360f, dt);                                  // one turn per 1000 ft
            Needle("vsi", -90f + 135f * Mathf.Clamp(vs / 10f, -1f, 1f), dt);                // +-10 m/s, zero at 9 o'clock
            // six-pack (Nick 22:04, Trident style): attitude horizon bar, turn coordinator, heading
            var e = transform.eulerAngles;
            Needle("adi", -Wrap(e.z), dt);                                                  // horizon rolls against the bank (sign fixed 0.15.26, Nick 04:56)
            // 0.15.29 (Nick 12:29, pinned at max): the game holds this engine at 100 % rpm whenever it runs (the prop governs),
            // so the dial shows 1400 rpm idle to 5800 at full throttle, with a little lag like a real engine
            engDisp = Mathf.MoveTowards(engDisp, er > 0.05f ? Mathf.Lerp(1400f, 5800f, thr) * Mathf.Clamp01(er) : 0f, 3000f * dt);
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
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "pilot" && pil == null) pil = t;
                    else if (t.name == "joystick" && jsT == null) jsT = t;
                    else if (t.name == "throttle" && thT == null && t.parent != null && t.parent.name == "throttleZero") thT = t;
                    else if (t.name == "UL2_ext_stick") exStick = t;
                    else if (t.name == "UL2_ext_throttle") exThr = t;
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
                if (jsT != null) jsWrist = jsT.InverseTransformPoint(transform.TransformPoint(new Vector3(0.025f, 0.180f, 1.586f)));
                if (thT != null) thWrist = thT.InverseTransformPoint(thT.position + transform.TransformDirection(new Vector3(-0.007f, 0.153f, -0.070f)));
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
    public class Seat3Pose : MonoBehaviour
    {
        class Table { public float afr, afl, ar; public string[] bones; public float[] A, E, d; }
        static readonly Dictionary<string, Table> tables = new Dictionary<string, Table>();
        static Type tTurret, tAircraft, tDetector; static FieldInfo fElev, fWm, fMag, fVis, fManual; static MethodInfo mGetTarget; static MethodInfo mTargets;
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
        static FieldInfo fSpd, fRalt; static MethodInfo mCap, mPartDet; Component seatPart; bool capOn; float capT;
        const float CAPTURE = 2f;
        void Capture()
        {
            if ((capT -= Time.deltaTime) > 0f || aircraft == null) return; capT = 2f;
            var tU = AccessTools.TypeByName("Unit");
            fSpd = fSpd ?? AccessTools.Field(tU, "speed"); fRalt = fRalt ?? AccessTools.Field(tU, "radarAlt"); mCap = mCap ?? AccessTools.Method(tU, "ModifyCaptureStrength");
            if (seatPart == null) { var tP = AccessTools.TypeByName("UnitPart"); seatPart = GetComponentInParent(tP); mPartDet = mPartDet ?? AccessTools.Method(tP, "IsDetached"); }
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
                if (!active) { tA = side * (kind == "spot" ? 75f + 20f * Mathf.Sin(t * 0.21f) : 52f); tE = kind == "spot" ? -12f + 6f * Mathf.Sin(t * 0.17f + 1.3f) : -12f; }   // idle: sitting up, weapon slightly low on his side (Nick 21:53: -40 read as slouched); spotter scans
                tA = Mathf.Clamp(tA, -T.ar, T.ar);
                if (tA > T.afr) side = 1f; else if (tA < -T.afl) side = -1f;
                vA = Mathf.MoveTowards(vA, tA, 150f * dt);
                vE = Mathf.MoveTowards(vE, Mathf.Clamp(tE, T.E[0], T.E[T.E.Length - 1]), 90f * dt);
                Apply(vA, vE);
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
}
