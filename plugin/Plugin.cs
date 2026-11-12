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
    [BepInPlugin("nick.nuclearoption.ul2airtrike", "UL-2 Airtrike TEST BUILD", "0.6.0")]
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
                new Harmony("nick.nuclearoption.ul2airtrike").Patch(start, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostStart)));
            }
            catch (Exception e) { Logger.LogError("UL-2 patch failed: " + e); }
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
        Component engine; static MethodInfo mRpm, mFuel; static FieldInfo fAlt; float lastY, vs; bool haveY;

        void Update()
        {
            try
            {
                if (!ready) { if (tries++ < 120) Setup(); return; }
                // game time reads 0 inside plugin components (seen on the PB-22 gauges): use unscaled time, frozen while paused
                float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
                float spd = fSpeed != null ? (float)fSpeed.GetValue(unit) : 0f;
                bool headOn = headPart == null || !Detached(headPart);
                // autorotation: about 9 rpm per m/s of airspeed, 380 rpm cap (~42 m/s); spins down over a few seconds
                float target = headOn ? Mathf.Clamp(spd * 9f, 0f, 380f) : 0f;
                rpm = Mathf.MoveTowards(rpm, target, (target > rpm ? 60f : 40f) * dt);
                spin.localRotation *= Quaternion.Euler(0f, rpm * 6f * dt, 0f);
                Gauges(spd, dt);
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

        void Gauges(float spd, float dt)
        {
            if (gauges.Count == 0 || dt <= 0f) return;
            float y = transform.position.y;
            if (haveY) vs = Mathf.Lerp(vs, (y - lastY) / dt, 1f - Mathf.Exp(-dt * 2f));
            lastY = y; haveY = true;
            float alt = fAlt != null ? Mathf.Max((float)fAlt.GetValue(unit), 0f) * 3.28084f : 0f;
            float er = 0f; try { if (engine != null && mRpm != null) er = Mathf.Clamp01((float)mRpm.Invoke(engine, null)); } catch { }
            float fuel = 0f; try { if (mFuel != null) fuel = Mathf.Clamp01((float)mFuel.Invoke(unit, null)); } catch { }
            Needle("asi", -135f + 270f * Mathf.Clamp01(spd * 1.94384f / 120f), dt);          // 0-120 kt
            Needle("alt", alt % 1000f / 1000f * 360f, dt);                                  // one turn per 1000 ft
            Needle("vsi", -90f + 135f * Mathf.Clamp(vs / 10f, -1f, 1f), dt);                // +-10 m/s, zero at 9 o'clock
            Needle("rotor", -135f + 270f * Mathf.Clamp01(rpm / 450f), dt);                   // rotor rpm, 0-450
            Needle("rpm", -135f + 270f * er, dt);
            Needle("fuel", -120f + 240f * fuel, dt);
            Needle("oil", er > 0.05f ? -135f + 270f * (0.45f + 0.25f * er) : -135f, dt);
            Needle("volt", -135f + 270f * (er > 0.05f ? 0.70f : 0.60f), dt);
        }
        void Needle(string id, float deg, float dt)
        {
            Transform t; if (!gauges.TryGetValue(id, out t) || t == null) return;
            float a; shown.TryGetValue(id, out a);
            a = Mathf.Lerp(a, deg, 1f - Mathf.Exp(-dt * 6f)); shown[id] = a;
            t.localRotation = rest[id] * Quaternion.Euler(0f, 0f, a);      // pivot Z faces the pilot: positive turns clockwise as seen from the seat
        }

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
            var tEng = AccessTools.TypeByName("TurbineEngine");
            foreach (var c in parts.Values)
            {
                if (c == null) continue;
                foreach (var t in c.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("UL2_gauge_")) { gauges[t.name.Substring(10)] = t; rest[t.name.Substring(10)] = t.localRotation; }
                if (engine == null && tEng != null) { var e = c.GetComponentInChildren(tEng, true); if (e != null) engine = e; }
            }
            spin = headPart.transform.Find("rotorSpin");
            if (spin == null) return;
            foreach (var s in new[] { "L", "R" })
            {
                string[] seg = { "root", "mid", "tip" }; string[] wing = { "wing1_", "wing2_", "wing3_" };
                for (int k = 0; k < 3; k++)
                {
                    var vis = spin.Find("spin_" + s + "_" + seg[k]);
                    if (vis != null && parts.TryGetValue(wing[k] + s, out var wp)) blades.Add(new KeyValuePair<Component, Transform>(wp, vis));
                }
            }
            ready = true;
            Plugin.Log.LogInfo("UL-2 rotor ready, " + blades.Count + " blade sections, " + gauges.Count + " gauges, engine " + (engine != null));
        }
    }
}
