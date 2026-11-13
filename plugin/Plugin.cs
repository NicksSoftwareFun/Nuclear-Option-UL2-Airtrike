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
    [BepInPlugin("nick.nuclearoption.ul2airtrike", "UL-2 Airtrike TEST BUILD", "0.15.20")]
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
            }
            catch (Exception e) { Logger.LogError("UL-2 patch failed: " + e); }
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
        static void PostStart(Component __instance)
        {
            try
            {
                if (__instance == null || !__instance.name.StartsWith("UL2Airtrike")) return;
                if (__instance.GetComponent<RotorSpinner>() == null) __instance.gameObject.AddComponent<RotorSpinner>();
            }
            catch (Exception e) { Log.LogError("UL-2 rotor: " + e); }
        }
    }

    public class RotorSpinner : MonoBehaviour
    {
        static FieldInfo fSpeed, fParts; static MethodInfo mDetached;
        Component unit; Transform spin; Component headPart;
        readonly List<KeyValuePair<Component, Transform>> blades = new List<KeyValuePair<Component, Transform>>();
        float rpm; bool ready; int tries;
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
        readonly List<Component> partList = new List<Component>(); float scanT, offGround, dbgT;

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
                float target = headOn && spd > 2f ? Mathf.Min(380f, 40f + 18f * spd) : 0f;
                float upRate = 60f;
                // pre-rotator (0.15.19, Nick 03:39: no jumps at all). On the ground (and 3 s after liftoff) with the engine running:
                // - custom axis = manual rotor rpm, 110 at the bottom of its travel to 450 at the top (overrides the assist)
                // - flight assist on, axis untouched = best rpm for the moment: full 450 at full throttle on the takeoff roll,
                //   otherwise it follows the throttle (110 idle to 300)
                // - flight assist off, axis untouched = no pre-spin. In the air the rotor autorotates from the airflow.
                bool fa = false; try { if (fFa == null) fFa = AccessTools.Field(unit.GetType(), "flightAssist"); if (fFa != null) fa = (bool)fFa.GetValue(unit); } catch { }
                bool manual = axis > 0.05f;
                autoSpin = !manual && fa && ground && engOn && thr > 0.05f;
                if (headOn && clutch && engOn)
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
                // rotor disc tilt with the stick (gyros steer by tilting the head); held back on the ground roll
                if (tilt != null)
                {
                    float tx = -(inc - INC0), tz = -6f * Mathf.Clamp(rollIn, -1f, 1f);     // the visible disc tilts with the blade incidence
                    tiltX = Mathf.Lerp(tiltX, tx, 1f - Mathf.Exp(-dt * 4f)); tiltZ = Mathf.Lerp(tiltZ, tz, 1f - Mathf.Exp(-dt * 4f));
                    tilt.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);
                }
                if ((scanT -= dt) <= 0f) { scanT = 1f; ScanKits(); }
                PropWatch(dt);
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
        bool inErr;
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
                if (readPiv != null) readPiv.localEulerAngles = new Vector3(0f, 0f, f * 26f - 13f);
                if (hudRead != null) { hudRead.text = Mathf.RoundToInt(rpm) + " RPM"; hudRead.color = col; hudRead.transform.localEulerAngles = new Vector3(0f, 0f, -(f * 26f - 13f)); }
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
                Plugin.Log.LogInfo($"UL-2 flight: {logSpd * 3.6f:0} km/h climb {logVy:0.0} m/s alt {alt:0.0} | rotor {rpm:0} rpm lift {rotorK:0.00} incidence {inc:0.0} body aoa {bodyAoA:0.0} stick {pitchIn:0.00} thr {thr:0.00} prop thrust {logThrust:0} N jump {logF:0.00} W rotor thrust {logT:0.00} W brake {brakeIn:0.0} roll in {rollIn:0.00} | pitch {Wrap(re.x):0.0} roll {Wrap(re.z):0.0} yaw rate {logYaw:0} deg/s |{o}");
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
        // extra rotor lift the fixed lifting surfaces can't give: a spun-up rotor on the takeoff roll, and the jump
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
            if (!ready || ctrl2 == null || cop == null || !cop.gameObject.activeInHierarchy) return;
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var b = arms[i]; if (b == null || b[0] == null || b[1] == null || b[2] == null) continue;
                    float sx = i == 0 ? -1f : 1f;
                    Vector3 tgt = ctrl2.TransformPoint(new Vector3(sx * 0.055f, -0.036f, -0.066f));
                    Vector3 pole = ctrl2.TransformDirection(new Vector3(sx * 0.7f, -1f, -0.3f));
                    Vector3 hdir = ctrl2.TransformDirection(new Vector3(-sx * 0.3f, 0.615f, 0.795f)).normalized;
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
                if (!armsLogged) { armsLogged = true; Plugin.Log.LogInfo("UL-2 copilot hands on the controller, wrist miss L " + (arms[0] != null ? (arms[0][2].position - ctrl2.TransformPoint(new Vector3(-0.055f, -0.036f, -0.066f))).magnitude.ToString("0.000") : "-") + " m"); }
            }
            catch (Exception ex) { Plugin.Log.LogError("UL-2 copilot hands: " + ex); ctrl2 = null; }
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
            Needle("asi", -135f + 270f * Mathf.Clamp01(spd * 1.94384f / 120f), dt);          // 0-120 kt
            Needle("alt", alt % 1000f / 1000f * 360f, dt);                                  // one turn per 1000 ft
            Needle("vsi", -90f + 135f * Mathf.Clamp(vs / 10f, -1f, 1f), dt);                // +-10 m/s, zero at 9 o'clock
            // six-pack (Nick 22:04, Trident style): attitude horizon bar, turn coordinator, heading
            var e = transform.eulerAngles;
            Needle("adi", Wrap(e.z), dt);                                                   // horizon rolls against the bank
            float yr = 0f; try { var rb0 = pRb?.GetValue(unit) as Rigidbody; if (rb0 != null) yr = rb0.angularVelocity.y * Mathf.Rad2Deg; } catch { }
            Needle("turn", Mathf.Clamp(yr / 3f * 20f, -35f, 35f), dt);                        // standard rate (3 deg/s) on the marks
            Needle("hdg", e.y, dt);
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
                }
            }
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
        float vA = 52f, vE = -40f, side = 1f, t0;
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
        void OnDestroy() { foreach (var kv in magOrig) if (kv.Key != null) fMag.SetValue(kv.Key, kv.Value); foreach (var kv in visOrig) if (kv.Key != null) fVis.SetValue(kv.Key, kv.Value); }

        static float Wrap(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }

        void LateUpdate()
        {
            try
            {
                float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
                float tA = 0f, tE = 0f; bool active = false;
                if (turret != null && elev != null)
                {
                    bool man = false; object tg = null;
                    try { if (fManual != null) man = (bool)fManual.GetValue(turret); if (mGetTarget != null) tg = mGetTarget.Invoke(turret, null); } catch { }
                    active = turret.enabled && (man || (tg is Component tc && tc != null));
                    tA = Wrap(turret.transform.localEulerAngles.y); tE = -Wrap(elev.localEulerAngles.x);
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
