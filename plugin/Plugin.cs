using System;
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
    [BepInPlugin("nick.nuclearoption.ul2airtrike", "UL-2 Airtrike TEST BUILD", "0.15.11")]
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
        static MethodInfo mInputs; static FieldInfo fThr, fPitch, fRoll, fAxis, fYaw; static PropertyInfo pRb;
        // rudder pedals (P220 / PB-22 way): UL2_pedal_L/R slide fore and aft on their floor rails with the yaw input
        readonly Dictionary<string, Transform> pedals = new Dictionary<string, Transform>();
        readonly Dictionary<string, Vector3> pedalRest = new Dictionary<string, Vector3>(); float yawIn, pedalPos;
        Transform tilt; float tiltX, tiltZ, thr, pitchIn, rollIn, axis, prevAxis, alt, er, jumpT = -1f, jumpE, massSum, massT;
        readonly List<Component> partList = new List<Component>(); float scanT, offGround, dbgT;

        static FieldInfo fFa; bool autoArmed = true, autoSpin;
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
                float target = headOn ? Mathf.Clamp(spd * 9f, 0f, 380f) : 0f;
                float upRate = 60f;
                // pre-rotator: on the ground with the engine running the clutch drives the rotor, 110 rpm at idle throttle
                // up to 250 rpm at full; the custom axis over-speeds it (blades flat) up to 450 rpm for a jump launch
                // auto jump (Nick 23:12): with flight assist on, full throttle on the ground spins the rotor up as if the
                // axis were held and launches the jump on its own at 430 rpm; armed again once the throttle comes back
                bool fa = false; try { if (fFa == null) fFa = AccessTools.Field(unit.GetType(), "flightAssist"); if (fFa != null) fa = (bool)fFa.GetValue(unit); } catch { }
                if (thr < 0.9f || !ground) autoArmed = ground && thr < 0.9f;
                bool autoJ = fa && ground && engOn && thr > 0.98f && autoArmed && axis < 0.05f;
                autoSpin = autoJ;
                if (headOn && clutch && engOn && jumpT < 0f)
                {
                    float pre = ground ? Mathf.Lerp(110f, 250f, thr) : 0f;
                    if (autoJ) { pre = Mathf.Max(pre, 450f); if (rpm > 250f) upRate = 20f; }
                    if (autoJ && rpm >= 430f) { autoArmed = false; jumpT = 0f; jumpE = Mathf.Clamp01((rpm - 300f) / 150f); Plugin.Log.LogInfo("UL-2 auto jump launch (flight assist), rotor " + Mathf.RoundToInt(rpm) + " rpm"); }
                    if (axis > 0.05f) { pre = Mathf.Max(pre, 250f + 200f * axis); if (rpm > 250f) upRate = 20f; }
                    target = Mathf.Max(target, pre);
                    // jump: axis released after a spin-up past 400 rpm -> blades to flying pitch, stored energy becomes lift
                    if (prevAxis > 0.5f && axis < 0.2f && rpm > 400f) { jumpT = 0f; jumpE = Mathf.Clamp01((rpm - 300f) / 150f); Plugin.Log.LogInfo("UL-2 jump launch, rotor " + Mathf.RoundToInt(rpm) + " rpm"); }
                }
                if (axis > 0.05f && (dbgT -= dt) <= 0f) { dbgT = 2f; Plugin.Log.LogInfo($"UL-2 pre-rotator axis {axis:0.00} rpm {rpm:0} alt {alt:0.00} engine {er:0.00} thr {thr:0.00} clutch {clutch}"); }
                prevAxis = axis;
                if (jumpT >= 0f) { jumpT += dt; target = Mathf.Max(target, 300f); if (jumpT > 2.0f) jumpT = -1f; }
                rpm = Mathf.MoveTowards(rpm, target, (target > rpm ? upRate : (jumpT >= 0f ? 80f : 40f)) * dt);
                spin.localRotation *= Quaternion.Euler(0f, rpm * 6f * dt, 0f);
                // rotor disc tilt with the stick (gyros steer by tilting the head); held back on the ground roll
                if (tilt != null)
                {
                    float tx = ground ? -8f : Mathf.Clamp(7f * pitchIn, -8f, 6f), tz = -6f * Mathf.Clamp(rollIn, -1f, 1f);
                    tiltX = Mathf.Lerp(tiltX, tx, 1f - Mathf.Exp(-dt * 4f)); tiltZ = Mathf.Lerp(tiltZ, tz, 1f - Mathf.Exp(-dt * 4f));
                    tilt.localRotation = Quaternion.Euler(tiltX, 0f, tiltZ);
                }
                if ((scanT -= dt) <= 0f) { scanT = 1f; ScanKits(); }
                PropWatch(dt);
                HudGauge(dt, engOn);
                if (alt < 2f) LeanLog(dt);
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
                if (fThr == null) { var t = ci.GetType(); fThr = AccessTools.Field(t, "throttle"); fPitch = AccessTools.Field(t, "pitch"); fRoll = AccessTools.Field(t, "roll"); fAxis = AccessTools.Field(t, "customAxis1"); fYaw = AccessTools.Field(t, "yaw"); Plugin.Log.LogInfo("UL-2 inputs " + t.Name + " throttle " + (fThr != null) + " customAxis1 " + (fAxis != null)); }
                thr = Mathf.Clamp01(fThr != null ? (float)fThr.GetValue(ci) : 0f);
                pitchIn = fPitch != null ? (float)fPitch.GetValue(ci) : 0f; rollIn = fRoll != null ? (float)fRoll.GetValue(ci) : 0f;
                // the axis reads 0..~0.45 on Nick's binding (log 21:53): 0.4 and up counts as full
                axis = Mathf.Clamp01((fAxis != null ? (float)fAxis.GetValue(ci) : 0f) / 0.4f);
                yawIn = fYaw != null ? Mathf.Clamp((float)fYaw.GetValue(ci), -1f, 1f) : 0f; if (float.IsNaN(yawIn)) yawIn = 0f;
            }
            catch (Exception e) { if (!inErr) { inErr = true; Plugin.Log.LogWarning("UL-2 inputs: " + e.Message); } }
        }
        bool inErr;
        // HUD rotor gauge (Nick 22:31/22:35/22:36): the Alkyon-style label from our HUD extras prefab (UL2_rotorGauge),
        // rpm readout and an idle (110) to full (450 rpm) bar, red until the rotor is fast enough to jump (400), then green
        Transform hud; RectTransform barFill; UnityEngine.UI.Image fillImg; TMPro.TMP_Text hudRpm, hudHint; float hudFind, hudShow;
        void HudGauge(float dt, bool engOn)
        {
            try
            {
                if (hud == null)
                {
                    if ((hudFind -= dt) > 0f) return; hudFind = 1f;
                    foreach (var t in Resources.FindObjectsOfTypeAll<RectTransform>()) if (t.name == "UL2_rotorGauge" && t.gameObject.scene.IsValid()) { hud = t; break; }
                    if (hud == null) return;
                    barFill = hud.Find("barBg/barFill") as RectTransform; if (barFill != null) fillImg = barFill.GetComponent<UnityEngine.UI.Image>();
                    var l = hud.Find("label"); if (l != null) hudRpm = l.GetComponent<TMPro.TMP_Text>();
                    var ti = hud.Find("hint"); if (ti != null) hudHint = ti.GetComponent<TMPro.TMP_Text>();
                    Plugin.Log.LogInfo("UL-2 HUD rotor gauge found, bar " + (barFill != null) + " text " + (hudRpm != null));
                }
                bool want = axis > 0.05f || jumpT >= 0f || autoSpin;
                if (want) hudShow = 3f; else if (hudShow > 0f) hudShow -= dt;
                bool show = hudShow > 0f;
                if (hud.gameObject.activeSelf != show) hud.gameObject.SetActive(show);
                if (!show) return;
                bool ready = rpm >= 400f;
                var col = jumpT >= 0f || ready ? new Color(0.3f, 1f, 0.3f, 1f) : new Color(1f, 0.25f, 0.2f, 1f);
                // vertical bar (0.15.9): only the fill changes colour; the texts keep the HUD colour from their style applier
                if (barFill != null) barFill.sizeDelta = new Vector2(barFill.sizeDelta.x, 198f * Mathf.Clamp01((rpm - 110f) / 340f));
                if (fillImg != null) fillImg.color = col;
                if (hudRpm != null) hudRpm.text = "ROTOR " + Mathf.RoundToInt(rpm) + " RPM";
                if (hudHint != null) hudHint.text = jumpT >= 0f ? "JUMP" : autoSpin ? "AUTO JUMP" : ready ? "RELEASE TO JUMP" : "";
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
                Plugin.Log.LogInfo($"UL-2 roll-out: {logSpd * 3.6f:0} km/h climb {logVy:0.0} m/s yaw rate {logYaw:0} deg/s rotor assist {logF:0.00} W | pitch {Wrap(re.x):0.0} roll {Wrap(re.z):0.0} alt {alt:0.00} |{o}");
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
                // 0.15.9 (Nick 23:10: never lifts off): rotor lift on the roll held to 25 m/s (was fading out from 18 m/s,
                // right across the 20 m/s takeoff speed), then handed over to the blade surfaces by 40 m/s
                if (alt < 15f && spd < 40f)
                {
                    float s = Mathf.Clamp01(rpm / 250f);
                    F += W * 0.45f * s * s * Mathf.Clamp01(spd / 10f) * (1f - Mathf.InverseLerp(25f, 40f, spd));
                }
                if (jumpT >= 0f) F += W * 2.2f * jumpE * Mathf.Clamp01(1f - jumpT / 2.0f);
                if (F > 0f) rb.AddForce(transform.up * F, ForceMode.Force);
                logSpd = spd; logVy = rb.velocity.y; logYaw = Vector3.Dot(rb.angularVelocity, transform.up) * Mathf.Rad2Deg; logF = W > 0f ? F / W : 0f;
                // rotor head pitch authority (a gyro pitches by tilting the head, Nick 22:31: nose would not come up on the
                // takeoff roll). Scales with rotor rpm; strongest on the ground, eased off in flight where the elevator works.
                // Game pitch input: positive = nose down.
                float rs = Mathf.Clamp01(rpm / 300f);
                float K = alt < 2f ? 4500f : Mathf.Lerp(2500f, 900f, Mathf.InverseLerp(15f, 35f, spd));
                if (rs > 0.05f && Mathf.Abs(pitchIn) > 0.02f) rb.AddTorque(transform.right * (pitchIn * K * rs), ForceMode.Force);
            }
            catch (Exception e) { Plugin.Log.LogError("UL-2 rotor lift: " + e); pRb = null; }
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
            Pedals(dt);
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
                }
                if (engine == null && tEng != null) { var e = c.GetComponentInChildren(tEng, true); if (e != null) engine = e; }
            }
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
