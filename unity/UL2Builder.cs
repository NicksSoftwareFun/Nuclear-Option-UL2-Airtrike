using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Builds UL2Airtrike.prefab from a pristine copy of the CI-22 Cricket plus the UL2 art (UL2_parts.fbx + UL2_layout.json),
// and the mod's own def, parameters, hangar op and weapon mounts. Run from the menu (UL2 > Rebuild) or UL2Builder.Build().
// Every value is a literal on purpose.
public static class UL2Builder
{
    const string MOD = "Assets/Blueprinter/Mods/UL2Airtrike/";
    const string ART = MOD + "Art/";
    const string MNT = MOD + "Mounts/";
    const string TOOLS = "Assets/UL2Tools/";
    const string SRC = TOOLS + "UL2_coinsrc.prefab";
    const string DST = MOD + "UL2Airtrike.prefab";
    const string DEF = MOD + "UL2Airtrike_def.asset";
    const string PAR = MOD + "UL2Airtrike_Parameters.asset";
    const string PH = "Assets/Blueprinter/_donotship/";
    const string DONOR = PH + "GameObject/COIN_PLACEHOLDER.prefab";

    [System.Serializable] class E { public string name, coll, kind; public float[] pos, rot, bmin, bmax; public string[] mats; }
    [System.Serializable] class W { public E[] items; }

    static Dictionary<string, E> L;
    static Dictionary<string, Mesh> M;
    static Dictionary<string, Material> MAT;
    static Transform root;
    static StringBuilder log;
    static readonly HashSet<Object> doomed = new HashSet<Object>();
    static readonly List<GameObject> doomedRoots = new List<GameObject>();
    static readonly List<Component> doomedComps = new List<Component>();

    [MenuItem("UL2/Rebuild")]
    public static void Menu() { Debug.Log(Build()); }

    // ------------------------------------------------------------------ helpers
    static readonly Quaternion LaserRest = Quaternion.LookRotation(Vector3.down, Vector3.back);
    static Vector3 BtoU(float[] p) { return new Vector3(p[0], p[2], p[1]); }
    static Vector3 P(string n) { return BtoU(Lget(n).pos); }
    static E Lget(string n) { E e; if (!L.TryGetValue(n, out e)) throw new System.Exception("layout missing " + n); return e; }
    static Transform Find(Transform under, string name)
    {
        if (under.name == name) return under;
        foreach (Transform c in under) { var r = Find(c, name); if (r != null) return r; }
        return null;
    }
    static Transform Need(string name)
    {
        var t = Find(root, name);
        if (t == null) throw new System.Exception("missing " + name);
        return t;
    }
    static Transform New(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
    }
    static Component C(Transform t, string type) { return t.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == type); }
    static Material[] Mats(string mesh) { return Lget(mesh).mats.Select(m => MAT[m]).ToArray(); }
    static void SetMesh(Transform t, string mesh)
    {
        var mf = t.GetComponent<MeshFilter>(); if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = M[mesh];
        var mr = t.GetComponent<MeshRenderer>(); if (mr == null) mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterials = Mats(mesh);
    }
    static void NoMesh(Transform t) { var mf = t.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = null; }
    static void SetCol(Transform t, string col)
    {
        var mc = t.GetComponent<MeshCollider>(); if (mc == null) mc = t.gameObject.AddComponent<MeshCollider>();
        mc.convex = true; mc.sharedMesh = M[col];
    }
    static void Lod(Transform t, string col)
    {
        foreach (Transform l in t)
        {
            if (!l.name.ToLower().EndsWith("_lod1")) continue;
            l.localPosition = Vector3.zero; l.localRotation = Quaternion.identity; l.localScale = Vector3.one;
            var mf = l.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = M[col];
        }
    }
    // a whole part: pivot from the layout (world-aligned), visible mesh, convex hull collider, LOD shell
    static Transform PartAt(string name, string mesh = null)
    {
        mesh = mesh ?? name;
        var t = Need(name);
        MoveAlone(t, P(mesh));
        SetMesh(t, mesh); SetCol(t, mesh + "_colmesh"); Lod(t, mesh + "_colmesh");
        return t;
    }
    // move a transform without carrying its children along
    static void MoveAlone(Transform t, Vector3 pos)
    {
        var kids = new List<Transform>(); foreach (Transform c in t) kids.Add(c);
        var wp = kids.Select(c => c.position).ToList(); var wr = kids.Select(c => c.rotation).ToList();
        t.position = pos; t.rotation = Quaternion.identity;
        for (int i = 0; i < kids.Count; i++) { kids[i].position = wp[i]; kids[i].rotation = wr[i]; }
    }
    static void Doom(Transform t)
    {
        if (t == null) return;
        doomedRoots.Add(t.gameObject);
        foreach (var tr in t.GetComponentsInChildren<Transform>(true))
        {
            doomed.Add(tr.gameObject);
            foreach (var c in tr.GetComponents<Component>()) if (c != null) doomed.Add(c);
        }
    }
    static void DoomName(string n) { Doom(Find(root, n)); }
    static void DoomComp(Component c) { if (c == null) return; doomed.Add(c); doomedComps.Add(c); }
    // remove every reference to a doomed object: the innermost array element holding it goes, a plain field is nulled
    static void Purge()
    {
        foreach (var c in root.GetComponentsInChildren<Component>(true))
        {
            if (c == null || doomed.Contains(c) || c is Transform) continue;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            var hits = new List<string>();
            while (it.Next(true))
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null && doomed.Contains(it.objectReferenceValue))
                    hits.Add(it.propertyPath);
            if (hits.Count == 0) continue;
            var dels = new List<KeyValuePair<string, int>>();
            foreach (var h in hits)
            {
                int k = h.LastIndexOf(".Array.data[");
                if (k < 0) { so.FindProperty(h).objectReferenceValue = null; log.AppendLine("null " + c.name + "." + c.GetType().Name + "." + h); continue; }
                int e = h.IndexOf(']', k);
                dels.Add(new KeyValuePair<string, int>(h.Substring(0, k), int.Parse(h.Substring(k + 12, e - k - 12))));
            }
            foreach (var d in dels.Distinct().OrderByDescending(x => x.Value))
            {
                var arr = so.FindProperty(d.Key);
                if (arr == null || !arr.isArray || d.Value >= arr.arraySize) continue;   // its parent element already went
                int before = arr.arraySize;
                arr.DeleteArrayElementAtIndex(d.Value);
                if (arr.arraySize == before) arr.DeleteArrayElementAtIndex(d.Value);
                log.AppendLine("drop " + c.name + "." + c.GetType().Name + "." + d.Key + "[" + d.Value + "]");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var c in doomedComps) if (c != null) Object.DestroyImmediate(c);
        foreach (var g in doomedRoots) if (g != null) Object.DestroyImmediate(g);
        doomed.Clear(); doomedRoots.Clear(); doomedComps.Clear();
    }
    static SerializedProperty Prop(SerializedObject so, string path)
    {
        var p = so.FindProperty(path);
        if (p == null) log.AppendLine("MISSING PROP " + so.targetObject.GetType().Name + "." + path);
        return p;
    }
    static void Set(Object c, string path, object v)
    {
        if (c == null) { log.AppendLine("NULL TARGET for " + path); return; }
        var so = new SerializedObject(c);
        var p = Prop(so, path);
        if (p == null) return;
        if (v is float) { if (p.propertyType == SerializedPropertyType.Integer) p.intValue = (int)(float)v; else p.floatValue = (float)v; }
        else if (v is int) { if (p.propertyType == SerializedPropertyType.Float) p.floatValue = (int)v; else p.intValue = (int)v; }
        else if (v is bool) p.boolValue = (bool)v;
        else if (v is Vector3) p.vector3Value = (Vector3)v;
        else if (v is string) p.stringValue = (string)v;
        else if (v is AnimationCurve) p.animationCurveValue = (AnimationCurve)v;
        else if (v == null || v is Object) p.objectReferenceValue = (Object)v;
        else log.AppendLine("UNSUPPORTED " + path);
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Size(Object c, string path, int n)
    {
        var so = new SerializedObject(c); var p = Prop(so, path); if (p == null) return;
        p.arraySize = n; so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void ObjArray(Object c, string path, params Object[] items)
    {
        var so = new SerializedObject(c); var p = Prop(so, path); if (p == null) return;
        p.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static Component AP(string n) { return C(Need(n), "AeroPart"); }
    static int jointIterations = 6;
    static void Joints(string part, params object[] pairs)
    {
        var ap = AP(part);
        int n = pairs.Length / 2;
        Size(ap, "joints", n);
        for (int i = 0; i < n; i++)
        {
            string j = "joints.Array.data[" + i + "].";
            Set(ap, j + "connectedPart", AP((string)pairs[2 * i]));
            Set(ap, j + "tensor", null);
            Set(ap, j + "anchor", null);
            Set(ap, j + "breakForce", (float)pairs[2 * i + 1]);
            Set(ap, j + "breakTorque", (float)pairs[2 * i + 1]);
            Set(ap, j + "solverIterations", jointIterations);
        }
    }
    static Material LitMat(string name, Color col, float smooth, float metal)
    {
        string path = ART + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(AssetDatabase.LoadAssetAtPath<Material>(PH + "Material/jet_mechanical_PLACEHOLDER.mat"));
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
        }
        foreach (var t in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
            if (m.HasProperty(t)) m.SetTexture(t, null);
        m.SetColor("_BaseColor", col); if (m.HasProperty("_Color")) m.SetColor("_Color", col);
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        EditorUtility.SetDirty(m);
        return m;
    }
    static string Placeholder(string name, string type)
    {
        foreach (var g in AssetDatabase.FindAssets(name + "_PLACEHOLDER t:" + type))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == name + "_PLACEHOLDER" && !p.Contains("__dedupe")) return p;
        }
        foreach (var g in AssetDatabase.FindAssets(name + "_PLACEHOLDER t:" + type))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == name + "_PLACEHOLDER") return p;
        }
        throw new System.Exception("no placeholder " + name + " (" + type + ")");
    }
    static Object Mount(string name)
    {
        var custom = AssetDatabase.LoadMainAssetAtPath(MNT + name + ".asset");
        if (custom != null) return custom;
        return AssetDatabase.LoadMainAssetAtPath(Placeholder(name, "WeaponMount"));
    }


    // ------------------------------------------------------------------ setup: pristine donor, def, parameters, importer
    const string DEFSRC = PH + "MonoBehaviour/CI-22_PLACEHOLDER.asset";
    const string PARSRC = PH + "MonoBehaviour/COINParameters_PLACEHOLDER.asset";
    static void Setup()
    {
        if (!AssetDatabase.IsValidFolder("Assets/UL2Tools")) AssetDatabase.CreateFolder("Assets", "UL2Tools");
        if (!AssetDatabase.IsValidFolder(MOD + "Art")) AssetDatabase.CreateFolder(MOD.TrimEnd('/'), "Art");
        if (!AssetDatabase.IsValidFolder(MOD + "Mounts")) AssetDatabase.CreateFolder(MOD.TrimEnd('/'), "Mounts");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SRC) == null)
        {
            if (!AssetDatabase.CopyAsset(DONOR, SRC)) throw new System.Exception("copy of the Cricket prefab failed");
            log.AppendLine("saved pristine Cricket copy to " + SRC);
        }
        if (AssetDatabase.LoadMainAssetAtPath(DEF) == null && !AssetDatabase.CopyAsset(DEFSRC, DEF)) throw new System.Exception("def copy failed");
        if (AssetDatabase.LoadMainAssetAtPath(PAR) == null && !AssetDatabase.CopyAsset(PARSRC, PAR)) throw new System.Exception("parameters copy failed");
        var imp = (ModelImporter)AssetImporter.GetAtPath(ART + "UL2_parts.fbx");
        if (imp == null) throw new System.Exception("no " + ART + "UL2_parts.fbx");
        if (imp.materialImportMode != ModelImporterMaterialImportMode.None || !imp.isReadable || imp.animationType != ModelImporterAnimationType.None)
        {
            imp.materialImportMode = ModelImporterMaterialImportMode.None; imp.isReadable = true; imp.importAnimation = false;
            imp.animationType = ModelImporterAnimationType.None; imp.importCameras = false; imp.importLights = false;
            imp.meshCompression = ModelImporterMeshCompression.Off; imp.weldVertices = true;
            imp.SaveAndReimport();
            log.AppendLine("fbx importer set");
        }
    }

    // ------------------------------------------------------------------ build
    public static string Build()
    {
        log = new StringBuilder(); doomed.Clear(); doomedRoots.Clear(); doomedComps.Clear();
        try { Setup(); } catch (System.Exception ex) { return log + "SETUP ERROR " + ex; }
        L = JsonUtility.FromJson<W>("{\"items\":" + System.IO.File.ReadAllText(ART + "UL2_layout.json") + "}").items.ToDictionary(e => e.name);
        M = new Dictionary<string, Mesh>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ART + "UL2_parts.fbx")) if (o is Mesh) M[o.name] = (Mesh)o;
        MAT = new Dictionary<string, Material>();
        // untextured grey until the texture passes (Nick)
        MAT["UL2_skin"] = SkinMat();
        MAT["UL2_frame"] = LitMat("UL2_frame", new Color32(175, 177, 180, 255), 0.4f, 0.2f);
        MAT["UL2_mech"] = LitMat("UL2_mech", new Color32(110, 112, 115, 255), 0.35f, 0.4f);
        MAT["UL2_dark"] = LitMat("UL2_dark", new Color32(42, 44, 47, 255), 0.4f, 0f);
        MAT["UL2_tyre"] = LitMat("UL2_tyre", new Color32(22, 22, 22, 255), 0.15f, 0f);
        MAT["UL2_seat"] = LitMat("UL2_seat", new Color32(70, 72, 74, 255), 0.25f, 0f);
        MAT["UL2_cockpit"] = LitMat("UL2_cockpit", new Color32(60, 62, 65, 255), 0.3f, 0f);
        MAT["UL2_screen"] = LitMat("UL2_screen", new Color32(12, 14, 16, 255), 0.85f, 0f);
        var glass = AssetDatabase.LoadAssetAtPath<Material>(PH + "Material/trainer_glass_PLACEHOLDER.mat");
        MAT["UL2_glass"] = glass != null ? glass : MAT["UL2_screen"];
        try { Mounts(); } catch (System.Exception ex) { log.AppendLine("MOUNTS ERROR " + ex); }

        root = PrefabUtility.LoadPrefabContents(SRC).transform;
        root.name = "COIN_PLACEHOLDER";
        try
        {
            Body();
            Rotor();
            EnginePart();
            Tail();
            Gear();
            Misc();
            Weapons();
            Purge();
            AeroAndDamage();
            Lods();
            SkinRegister();
            root.name = "UL2Airtrike";
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, DST);
            log.AppendLine("saved " + DST);
        }
        catch (System.Exception ex) { log.AppendLine("ERROR " + ex); }
        finally { PrefabUtility.UnloadPrefabContents(root.gameObject); }
        try { Assets(); } catch (System.Exception ex) { log.AppendLine("ASSETS ERROR " + ex); }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // a new airframe part cloned from a plain Cricket part (AeroPart + convex collider + LOD shell), children stripped
    static Transform NewPart(string like, string name, string parent)
    {
        var src = Need(like);
        var go = (GameObject)Object.Instantiate(src.gameObject, Need(parent));
        go.name = name;
        var kids = new List<Transform>(); foreach (Transform c in go.transform) kids.Add(c);
        foreach (var c in kids) if (!c.name.ToLower().EndsWith("_lod1")) Object.DestroyImmediate(c.gameObject);
        var lods = new List<Transform>(); foreach (Transform c in go.transform) lods.Add(c);
        for (int i = 1; i < lods.Count; i++) Object.DestroyImmediate(lods[i].gameObject);
        if (go.transform.childCount > 0) go.transform.GetChild(0).name = name + "_LOD1";
        var ap = C(go.transform, "AeroPart"); Size(ap, "joints", 0);
        Set(ap, "liftNormal", null);
        return PartAt(name);
    }
    static Transform Visual(string name, string mesh, Transform parent)
    {
        var t = Find(parent, name) ?? New(name, parent);
        t.position = P(mesh); t.rotation = Quaternion.identity; SetMesh(t, mesh); return t;
    }
    static void Hide(string n)
    {
        var t = Find(root, n); if (t == null) { log.AppendLine("hide: no " + n); return; }
        NoMesh(t);
        foreach (var r in t.GetComponents<Renderer>()) r.enabled = false;
        foreach (var c in t.GetComponents<Collider>()) c.enabled = false;
    }

    // ------------------------------------------------------------------ frame, pod, crew
    static void Body()
    {
        SetMesh(root, "UL2Airtrike"); SetCol(root, "UL2Airtrike_colmesh"); Lod(root, "UL2Airtrike_colmesh");
        var rl = root.Find("engine_LOD1"); if (rl != null) { var mf = rl.GetComponent<MeshFilter>(); if (mf) mf.sharedMesh = M["UL2Airtrike_colmesh"]; }
        PartAt("cockpit_R"); PartAt("cockpit_F"); PartAt("nose");
        PartAt("fuselage", "frame_R");
        PartAt("intake", "seat3");
        try { Soldier(); } catch (System.Exception ex) { log.AppendLine("SOLDIER ERROR " + ex); }
        var cf = Need("cockpit_F");
        Visual("gear_F_visible", "gear_F", cf);
        // the Cricket cockpit, canopies and ejection seats go invisible; their components stay for the stock logic
        foreach (var n in new[] { "cockpit_F_interior", "cockpit_F_exterior",
                                  "canopyFrame_F", "canopy_F_LOD1", "canopyGlass_F_interior", "canopyFrame_F_exterior", "canopyFrame_F_interior",
                                  "canopyGlass_F_exterior", "canopyFrame_R", "canopy_R_LOD1", "canopy_glass_R_exterior", "canopy_glass_R_interior",
                                  "geardoor_FL", "geardoor_FR", "geardoor_LL", "geardoor_LR", "geardoor_RL", "geardoor_RR", "ejectionSeat2" })
            Hide(n);
        // pilot controls on the Cricket's Cockpit links: stick and throttle move with the inputs (stock Cockpit component)
        {
            var js = Need("joystick"); MoveAlone(js, P("x_stick")); SetMesh(js, "x_stick"); js.GetComponent<Renderer>().enabled = true;
            var tz = Need("throttleZero"); MoveAlone(tz, P("x_throttle"));
            var th = Need("throttle"); MoveAlone(th, P("x_throttle")); SetMesh(th, "x_throttle"); th.GetComponent<Renderer>().enabled = true;
            // the MFDs show the targeting ball's camera, the stock way (TargetCam draws onto targetScreen's material)
            var ts = Need("targetScreen"); MoveAlone(ts, P("x_mfd1"));
            ts.GetComponent<MeshFilter>().sharedMesh = M["x_mfd1"]; var tr = ts.GetComponent<Renderer>(); tr.enabled = true;
            var ts2 = Find(root, "targetScreen2");
            if (ts2 == null) { ts2 = ((GameObject)Object.Instantiate(ts.gameObject, Need("cockpit_R"))).transform; ts2.name = "targetScreen2"; }
            MoveAlone(ts2, P("x_mfd2")); ts2.GetComponent<MeshFilter>().sharedMesh = M["x_mfd2"];
            log.AppendLine("screen material " + (tr.sharedMaterial ? tr.sharedMaterial.name : "none"));
        }
        // live gauge needles: a pivot facing the pilot (local Z = panel normal) holding the needle mesh; the plugin turns the pivot
        {
            var nrm = new Vector3(0f, Mathf.Sin(20f * Mathf.Deg2Rad), -Mathf.Cos(20f * Mathf.Deg2Rad));   // Blender panel tilt -20 deg about X
            int ng = 0;
            foreach (var id in new[] { "asi", "alt", "vsi", "rotor", "rpm", "fuel", "oil", "volt" })
            {
                if (!L.ContainsKey("x_g_" + id)) continue;
                var pv = Find(cf, "UL2_gauge_" + id) ?? New("UL2_gauge_" + id, cf);
                pv.position = P("x_g_" + id); pv.rotation = Quaternion.LookRotation(nrm, Vector3.up);
                var nd = Find(pv, "needle") ?? New("needle", pv);
                nd.position = pv.position; nd.rotation = Quaternion.identity; SetMesh(nd, "x_g_" + id); ng++;
            }
            log.AppendLine("gauges " + ng);
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "EjectionSeat").ToList()) { NoMesh(t); var mc = t.GetComponent<Collider>(); if (mc) mc.enabled = false; }
        foreach (var c in root.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "Pilot")) Set(c, "ejectionSeat", false);
        // crew: the Cricket pilot and WSO (the copilot) sit at the UL-2 seats; pelvis is 0.01 below the Pilot transform
        Need("pilot").position = P("pilot") + new Vector3(0f, 0.01f, 0f);
        var wso = Need("WSO"); wso.position = P("copilot") + new Vector3(0f, 0.01f, 0f); wso.name = "copilot";
        // targeting ball behind the nose gear: the camera and the laser look from it; one laser spot, not the Cricket's two
        Need("targetCamForward").position = P("targetCam");
        var nose = Need("nose"); var ld = C(nose, "LaserDesignator"); Set(ld, "maxTargets", 1);
        Need("radarAlt").position = P("wheel_F") + new Vector3(0f, 0.3f, -0.3f);
        Need("gearlight_F").position = P("landingLight");
        Need("intlight1").position = P("pilot") + new Vector3(0f, 0.7f, 0.3f);
        // stub pylons on the root frame
        foreach (var s in new[] { "L", "R" })
        {
            var hp = Need("pylon_fuselage_" + s); hp.SetParent(root, true); hp.position = P("hardpoint_" + s); hp.rotation = Quaternion.Euler(0f, 0f, 180f);   // racks mount on TOP of the stub, flipped (Nick 2026-11-12)
        }
        DoomName("pylon_L"); DoomName("pylon_R"); DoomName("ventralHardpoint");
        // side gun: a mount part on the frame, the gun part in it; the Cricket's right gun hardpoint moves into it
        var ghp = Need("gunmount_R"); ghp.name = "gunmount_R_hp";
        NewPart("intake", "gunmount_R", "COIN_PLACEHOLDER");
        ghp.SetParent(Need("gunmount_R"), true); ghp.position = P("gunPivot"); ghp.rotation = Quaternion.identity;
    }

    // ------------------------------------------------------------------ rotor: mast, head, blades (the blades carry the lift)
    static void Rotor()
    {
        NewPart("intake", "mast", "fuselage");
        NewPart("intake", "rotorHead", "mast");
        foreach (var s in new[] { "L", "R" })
        {
            // Cricket wing chain root -> mid -> tip is the blade chain; nacelles (the main gear) move to the frame
            var nac = Need("nacelle_" + s); nac.SetParent(Need("fuselage"), true);
            var w1 = Need("wing1_" + s); w1.SetParent(Need("rotorHead"), true);
            PartAt("wing1_" + s, "blade_" + s + "_root");
            PartAt("wing2_" + s, "blade_" + s + "_mid");
            PartAt("wing3_" + s, "blade_" + s + "_tip");
            var tip = P("blade_" + s + "_tip"); var tipb = Lget("blade_" + s + "_tip");
            float sx = s == "L" ? -1f : 1f;
            var tipEnd = tip + new Vector3(sx * 1.1f, 0f, 0f);
            // aileron: no mesh of its own; acts at the outer blade, its collider is the tip hull
            var ail = Need("aileron_" + s); MoveAlone(ail, tipEnd); NoMesh(ail);
            var amc = ail.GetComponent<MeshCollider>(); if (amc) { amc.sharedMesh = M["blade_" + s + "_tip_colmesh"]; amc.enabled = false; }
            Lod(ail, "blade_" + s + "_tip_colmesh");
            var av = Find(ail, "aileron_" + s + "_visible"); if (av) { av.position = tipEnd; NoMesh(av); }
            // nav light rides the tail endplate; the Cricket wingtip and flaps go
            var nl = Need("navLight_" + s); nl.SetParent(Need("tailboom_" + (s == "L" ? "L" : "R")), true); nl.position = P("navLight_" + s);
            DoomName("wingtip_" + s); DoomName("flap_" + s);
            var hb = Find(root, "HeatBlur_" + s); if (hb) hb.position = P("exhaust") + new Vector3(sx * 0.15f, 0f, 0f);
        }
        // spinning copy of the blades (turned in game by the plugin); the lifting blade parts keep colliders but hide their mesh
        var spin = Find(Need("rotorHead"), "rotorSpin") ?? New("rotorSpin", Need("rotorHead"));
        spin.position = P("rotorCentre"); spin.rotation = Quaternion.identity;
        foreach (var s in new[] { "L", "R" })
        {
            string[] seg = { "root", "mid", "tip" };
            for (int k = 0; k < 3; k++)
            {
                Visual("spin_" + s + "_" + seg[k], "blade_" + s + "_" + seg[k], spin);
                var mr = Need("wing" + (k + 1) + "_" + s).GetComponent<MeshRenderer>(); if (mr) mr.enabled = false;
            }
        }
    }

    // ------------------------------------------------------------------ engine core + left/right, prop
    static void EnginePart()
    {
        NewPart("intake", "engine_core", "mast");
        NewPart("intake", "engine_L", "engine_core");
        NewPart("intake", "engine_R", "engine_core");
        var ph = Need("propHub"); MoveAlone(ph, P("propCentre"));
        SetCol(Need("propHub"), "propHub_colmesh");
        var hv = Need("hub_visible"); hv.position = P("propCentre"); hv.rotation = Quaternion.identity; SetMesh(hv, "propHub");
        foreach (Transform b in hv) { if (!b.name.StartsWith("blade")) continue; b.position = P("propCentre"); NoMesh(b);
            var lt = b.childCount > 0 ? b.GetChild(0) : null; if (lt) lt.position = P("propCentre") + (lt.position - P("propCentre")).normalized * 0.6f; }
        var pd = Find(ph, "propDisc"); if (pd) { pd.position = P("propCentre"); pd.localScale = Vector3.one * 0.68f; }
        var pdc = Find(ph, "propDiscCollider"); if (pdc) { pdc.position = P("propCentre"); pdc.localScale = Vector3.one * 0.68f; }
        Lod(ph, "propHub_colmesh");
        var csp = C(ph, "ConstantSpeedProp");
        // Rotax 915-class pusher: literal power and blade size
        Set(csp, "bladeLength", 0.74f); Set(csp, "hubRadius", 0.12f); Set(csp, "nominalPower", 150000f); Set(csp, "momentOfInertia", 8f);
        Set(csp, "propTorqueLimit", 1500f); Set(csp, "hubFriction", 60f); Set(csp, "bladeStrength", 8000f);
        var te = C(root, "TurbineEngine");
        Set(te, "maxPower", 150000f); Set(te, "maxFuelConsumption", 0.012f); Set(te, "spoolUpTime", 1.5f); Set(te, "startupTime", 4f);
        Set(te, "IRMin", 0.6f); Set(te, "IRMax", 0.8f);
    }

    // ------------------------------------------------------------------ tail: two boom pieces, split stabiliser, fin with tip
    static void Tail()
    {
        var tbL = Need("tailboom_L"); tbL.SetParent(Need("fuselage"), true);
        var tbR = Need("tailboom_R"); tbR.SetParent(tbL, true);
        PartAt("tailboom_L", "tailboom_F"); PartAt("tailboom_R", "tailboom_R");
        var v1 = Need("vstab1_L"); v1.SetParent(tbR, true); PartAt("vstab1_L", "vstab");
        var v2 = Need("vstab1_R"); v2.SetParent(v1, true); PartAt("vstab1_R", "vstabTip");
        PartAt("rudder_L", "rudder"); var rv = Find(Need("rudder_L"), "rudder_L_visible"); if (rv) { rv.position = P("rudder"); NoMesh(rv); }
        var r2 = Need("rudder_R"); r2.SetParent(v2, true); PartAt("rudder_R", "rudderTip"); rv = Find(r2, "rudder_R_visible"); if (rv) { rv.position = P("rudderTip"); NoMesh(rv); }
        foreach (var n in new[] { "vstab1_L_liftnormal", "vstab1_R_liftnormal" }) { var t = Find(root, n); if (t) { t.localPosition = Vector3.zero; t.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.right); } }
        foreach (var n in new[] { "rudder_L_liftnormal", "rudder_R_liftnormal" }) { var t = Find(root, n); if (t) { t.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.right); } }
        var hs = Need("hstab"); hs.SetParent(tbR, true); hs.name = "hstab_L"; PartAt("hstab_L");
        var el = Need("elevator"); el.name = "elevator_L"; PartAt("elevator_L");
        var ev = Find(el, "elevator_visible"); if (ev) { ev.position = P("elevator_L"); NoMesh(ev); }
        NewPart("hstab_L", "hstab_R", "tailboom_R");
        NewPart("hstab_L", "hstabTip_L", "hstab_L");
        NewPart("hstab_L", "hstabTip_R", "hstab_R");
        foreach (var n in new[] { "elevator_R", "elevatorTip_L", "elevatorTip_R" })
        {
            var par = n == "elevator_R" ? "hstab_R" : n.EndsWith("L") ? "hstabTip_L" : "hstabTip_R";
            var go = (GameObject)Object.Instantiate(el.gameObject, Need(par)); go.name = n;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t != go.transform && t.name.StartsWith("elevator")) t.name = t.name.Replace("elevator", n);
            PartAt(n); Size(C(go.transform, "AeroPart"), "joints", 0);
            var vis = Find(go.transform, n + "_visible"); if (vis) { vis.position = P(n); NoMesh(vis); }
        }
        DoomName("vstab2_L"); DoomName("vstab2_R");
    }

    // ------------------------------------------------------------------ gear: fixed legs (own meshes); the stock rough-field gear keeps its physics
    static void Gear()
    {
        // whole Cricket gear rigs shift so their wheels sit at the UL-2 wheels; the skinned Cricket legs are hidden
        System.Action<string, string, string> shiftGear = (hinge, wheel, pt) =>
        {
            var h = Need(hinge); var w = Find(h, wheel); var d = P(pt) - w.position; h.position += d;
        };
        shiftGear("gearHinge_F", "wheel_F", "wheel_F");
        PartAt("nacelle_L", "gear_L"); PartAt("nacelle_R", "gear_R");
        shiftGear("gearhinge_L", "wheel_L", "wheel_L");
        shiftGear("gearhinge_R", "wheel_R", "wheel_R");
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name.StartsWith("COIN_gear"))) smr.enabled = false;
        foreach (var g in new[] { "F", "L", "R" })
        {
            var gt = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "COIN_gear_" + g && C(t, "LandingGear") != null);
            var lg = C(gt, "LandingGear");
            // never retracts: the gear key only switches the aircraft's gear state (lights, weapon safeties, AI)
            Set(lg, "foldDegrees", 0f); Set(lg, "foldSpeed", 1000f); Set(lg, "hingeFoldMotion", Vector3.zero);
            Size(lg, "gearDoors", 0);
            Set(lg, "extendedDrag", 0.3f);
            // Cricket rough-field values kept (travel, friction, contact area); springs scaled to the lighter aircraft
            if (g == "F") { Set(lg, "wheelRadius", 0.24f); Set(lg, "springRate", 60000f); Set(lg, "dampingRate", 5000f); Set(lg, "mass", 15f); }
            else { Set(lg, "wheelRadius", 0.27f); Set(lg, "springRate", 50000f); Set(lg, "dampingRate", 5000f); Set(lg, "mass", 20f); }
            var bc = gt.GetComponent<BoxCollider>(); if (bc) bc.size = bc.size * 0.4f;
        }
    }

    // one LOD group for the whole airframe: every visible mesh in LOD0, every collider shell (_LOD1) in LOD1
    static void Lods()
    {
        var lg = root.GetComponent<LODGroup>(); var old = lg.GetLODs();
        var r0 = new List<Renderer>(); var r1 = new List<Renderer>();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            if (r.name.ToLower().EndsWith("_lod1")) r1.Add(r); else r0.Add(r);
        }
        foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.enabled) r0.Add(r);
        lg.SetLODs(new[] { new LOD(old[0].screenRelativeTransitionHeight, r0.ToArray()), new LOD(old[old.Length - 1].screenRelativeTransitionHeight, r1.ToArray()) });
        log.AppendLine("LOD0 " + r0.Count + " renderers, LOD1 " + r1.Count);
    }
    // ------------------------------------------------------------------ livery (stock way: one skin texture on the AircraftSkin shader, LiveryData swaps its colour map)
    const string SKIN = MOD + "Art/Skin/"; const string LIVERY = MOD + "Art/UL2_livery_olive.asset";
    static Texture2D SkinTex(string name, bool srgb, bool normal, bool alpha)
    {
        string path = SKIN + name + ".png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) throw new System.Exception("missing " + path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = srgb; ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = false; ti.mipmapEnabled = true; ti.maxTextureSize = 2048;
        ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.wrapMode = TextureWrapMode.Clamp; ti.anisoLevel = 4;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Material SkinMat()
    {
        var b = SkinTex("UL2_skin_b", true, false, false); var n = SkinTex("UL2_skin_n", false, true, false);
        var m = SkinTex("UL2_skin_m", false, false, true); var ao = SkinTex("UL2_skin_ao", false, false, false);
        var db = SkinTex("UL2_skin_dmg_b", true, false, true); var dn = SkinTex("UL2_skin_dmg_n", false, true, false);
        string path = MOD + "Art/UL2_skin.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = AssetDatabase.LoadAssetAtPath<Shader>(PH + "Shader/Shader Graphs_AircraftSkin_PLACEHOLDER.shader");
        if (sh == null) throw new System.Exception("AircraftSkin shader placeholder not found");
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = sh; mat.shaderKeywords = new string[0];
        mat.SetTexture("_Basecolor", b); mat.SetTexture("_Normal", n); mat.SetTexture("_Metallic", m); mat.SetTexture("_AO", ao);
        mat.SetTexture("_BasecolorDmg", db); mat.SetTexture("_NormalDmg", dn); mat.SetTexture("_Livery", b);
        mat.SetFloat("_HitPoints", 100f); mat.SetFloat("_Glossiness", 0f);
        EditorUtility.SetDirty(mat);
        var liv = AssetDatabase.LoadMainAssetAtPath(LIVERY) as ScriptableObject;
        if (liv == null)
        {
            var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("LiveryData")).FirstOrDefault(x => x != null);
            if (t == null) throw new System.Exception("LiveryData type not found");
            liv = ScriptableObject.CreateInstance(t); AssetDatabase.CreateAsset(liv, LIVERY);
        }
        var so = new SerializedObject(liv);
        so.FindProperty("Texture").objectReferenceValue = b; so.FindProperty("Glossiness").floatValue = 0f;
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(liv);
        log.AppendLine("skin material + livery " + AssetDatabase.AssetPathToGUID(LIVERY));
        return mat;
    }
    // the game writes the livery texture only into renderers listed in a part's damageMaterial.renderers (an empty list = the
    // part's own renderer), so every skin renderer that is not a part itself is added to its owning part (as on the P220)
    static void SkinRegister()
    {
        var listed = new HashSet<Renderer>(); var parts = new List<Component>();
        foreach (var c in root.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue; bool up = false;
            for (var bt = c.GetType(); bt != null; bt = bt.BaseType) if (bt.Name == "UnitPart") { up = true; break; }
            if (!up) continue; parts.Add(c);
            var so = new SerializedObject(c); var p = so.FindProperty("damageMaterial.renderers"); if (p == null) continue;
            for (int i = 0; i < p.arraySize; i++) { var r = p.GetArrayElementAtIndex(i).objectReferenceValue as Renderer; if (r != null) listed.Add(r); }
        }
        int added = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var sm = r.sharedMaterials; if (sm.Length == 0 || sm[0] == null || sm[0].name != "UL2_skin" || listed.Contains(r)) continue;
            Component owner = null;
            for (var t = r.transform; t != null && owner == null; t = t.parent)
                foreach (var c in parts) if (c.transform == t) { owner = c; break; }
            if (owner == null) { log.AppendLine("skin renderer without part: " + r.name); continue; }
            if (owner.transform == r.transform) continue;
            var so = new SerializedObject(owner); var p = so.FindProperty("damageMaterial.renderers");
            if (p.arraySize == 0) { var own = owner.GetComponent<Renderer>(); if (own != null) { p.arraySize = 1; p.GetArrayElementAtIndex(0).objectReferenceValue = own; listed.Add(own); } }
            p.arraySize++; p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = r; so.ApplyModifiedPropertiesWithoutUndo();
            listed.Add(r); added++;
        }
        log.AppendLine("livery renderers added to parts: " + added);
    }
    // seat 3 gunner: one soldier taken out of the Ibis 8-man troop mesh (stock soldier1 material), turned to face forward, pelvis on the seat
    static void Soldier()
    {
        var src = AssetDatabase.LoadAssetAtPath<Mesh>(PH + "Mesh/utilityHelo1_troops_F_PLACEHOLDER.asset");
        var v = src.vertices; var nr = src.normals; var uv = src.uv; var col = src.colors; var tr = src.GetTriangles(2); int n = v.Length;
        var par = new int[n]; for (int i = 0; i < n; i++) par[i] = i;
        System.Func<int, int> F = null; F = x => { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; };
        var key = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < n; i++) { var k = Vector3Int.RoundToInt(v[i] * 2000f); int o; if (key.TryGetValue(k, out o)) par[F(i)] = F(o); else key[k] = i; }
        for (int t = 0; t < tr.Length; t += 3) { par[F(tr[t])] = F(tr[t + 1]); par[F(tr[t + 1])] = F(tr[t + 2]); }
        var sum = new Dictionary<int, Vector4>();
        for (int t = 0; t < tr.Length; t += 3) { int r = F(tr[t]); var c = (v[tr[t]] + v[tr[t + 1]] + v[tr[t + 2]]) / 3f; Vector4 a; sum.TryGetValue(r, out a); sum[r] = a + new Vector4(c.x, c.y, c.z, 1f); }
        var map = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nc = new List<Color>(); var nt = new List<int>();
        var rot = Quaternion.Euler(0f, 180f, 0f);                     // the troop row faces -Z; the gunner faces forward
        for (int t = 0; t < tr.Length; t += 3)
        {
            var c = sum[F(tr[t])]; c /= c.w; if (!(c.x >= 0.5f && c.z < 0.05f)) continue;
            for (int j = 0; j < 3; j++) { int i = tr[t + j]; int o; if (!map.TryGetValue(i, out o)) { o = nv.Count; map[i] = o; nv.Add(rot * v[i]); nn.Add(rot * nr[i]); nu.Add(uv[i]); if (col.Length == n) nc.Add(col[i]); } nt.Add(o); }
        }
        var b = new Bounds(nv[0], Vector3.zero); foreach (var q in nv) b.Encapsulate(q);
        var pel = new Vector3(b.center.x, b.min.y + 0.52f, b.min.z + 0.30f);   // same pelvis estimate used for the stock pilot fit
        for (int i = 0; i < nv.Count; i++) nv[i] -= pel;
        string path = MOD + "Art/UL2_soldier.asset";
        var me = AssetDatabase.LoadAssetAtPath<Mesh>(path); bool fresh = me == null; if (fresh) me = new Mesh();
        me.Clear(); me.name = "UL2_soldier"; me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        me.SetVertices(nv); me.SetNormals(nn); me.SetUVs(0, nu); if (nc.Count == nv.Count) me.SetColors(nc); me.SetTriangles(nt, 0); me.RecalculateBounds(); me.RecalculateTangents();
        if (fresh) AssetDatabase.CreateAsset(me, path); else EditorUtility.SetDirty(me);
        var seat = Need("intake"); var so = Find(seat, "gunner_soldier") ?? New("gunner_soldier", seat);
        so.position = P("gunner"); so.rotation = Quaternion.identity;
        // three-bone rig for the seated soldier (hips, spine, head), animated from the hips up by the plugin.
        // Weights: the helmet/head pieces follow the head, the rifle and vest pieces the spine; the main body
        // island blends hips -> spine over the waist and spine -> head over the neck; the legs stay on the hips.
        var hips = New("UL2_sold_hips", so); hips.localPosition = Vector3.zero;
        var spine = New("UL2_sold_spine", hips); spine.localPosition = new Vector3(0f, 0.12f, -0.06f);
        var head = New("UL2_sold_head", spine); head.localPosition = new Vector3(0f, 0.56f, -0.04f) - spine.localPosition;
        int vn = nv.Count; var vp = new int[vn]; for (int i = 0; i < vn; i++) vp[i] = i;
        System.Func<int, int> G = null; G = x => { while (vp[x] != x) { vp[x] = vp[vp[x]]; x = vp[x]; } return x; };
        var vk = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < vn; i++) { var k = Vector3Int.RoundToInt(nv[i] * 2000f); int o; if (vk.TryGetValue(k, out o)) vp[G(i)] = G(o); else vk[k] = i; }
        for (int t = 0; t < nt.Count; t += 3) { vp[G(nt[t])] = G(nt[t + 1]); vp[G(nt[t + 1])] = G(nt[t + 2]); }
        var isum = new Dictionary<int, Vector4>(); var ilo = new Dictionary<int, Vector3>(); var ihi = new Dictionary<int, Vector3>();
        for (int i = 0; i < vn; i++)
        {
            int r = G(i); Vector4 a4; isum.TryGetValue(r, out a4); isum[r] = a4 + new Vector4(nv[i].x, nv[i].y, nv[i].z, 1f);
            ilo[r] = ilo.ContainsKey(r) ? Vector3.Min(ilo[r], nv[i]) : nv[i]; ihi[r] = ihi.ContainsKey(r) ? Vector3.Max(ihi[r], nv[i]) : nv[i];
        }
        int mainIsl = isum.OrderByDescending(x => x.Value.w).First().Key;
        var bw = new BoneWeight[vn];
        for (int i = 0; i < vn; i++)
        {
            int r = G(i); var c = isum[r] / isum[r].w; var q = nv[i]; float wh = 0f, ws = 0f, wd = 0f;
            if (r != mainIsl)
            {
                if (c.y > 0.50f && c.z > -0.15f) wd = 1f;                                     // helmet, face, goggles, headset (the radio and its antenna on the back plate stay on the spine)
                else if (c.y < 0.05f) wh = 1f;                                                // boots and the rifle standing between the knees
                else ws = 1f;                                                                // pouches, vest buckles
            }
            else
            {
                float s1 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.26f, q.y));
                if (q.z > 0.12f && q.y < 0.22f) s1 *= Mathf.InverseLerp(0.30f, 0.12f, q.z);   // thighs and knees stay with the hips
                // only the neck itself (a 7 cm radius column under the helmet) blends to the head; the plate carrier collar and the
                // shoulders stay rigid on the spine so the vest never warps when the head turns (Nick 2026-11-12)
                float neckR = new Vector2(q.x, q.z + 0.02f).magnitude;
                float s2 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.58f, q.y)) * Mathf.InverseLerp(0.085f, 0.06f, neckR);
                wd = s2; ws = s1 * (1f - s2); wh = 1f - wd - ws;
            }
            bw[i] = new BoneWeight { boneIndex0 = 0, weight0 = wh, boneIndex1 = 1, weight1 = ws, boneIndex2 = 2, weight2 = wd };
        }
        me.boneWeights = bw;
        var bones = new[] { hips, spine, head };
        me.bindposes = bones.Select(bt => bt.worldToLocalMatrix * so.localToWorldMatrix).ToArray();
        EditorUtility.SetDirty(me);
        var mr = so.gameObject.AddComponent<SkinnedMeshRenderer>();
        mr.sharedMesh = me; mr.bones = bones; mr.rootBone = hips; mr.updateWhenOffscreen = false;
        mr.localBounds = new Bounds(new Vector3(0f, 0.2f, 0f), new Vector3(1.0f, 1.6f, 1.2f));
        mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PH + "Material/soldier1_PLACEHOLDER.mat");
        log.AppendLine("soldier " + nv.Count + " verts, " + nt.Count / 3 + " tris, mat " + (mr.sharedMaterial ? mr.sharedMaterial.name : "MISSING"));
    }
    static void Misc()
    {
        DoomName("ladder");
        // the Cricket wheels, camera ball and gun stub meshes: the UL-2 meshes carry their own
        foreach (var n in new[] { "wheel_F", "wheel_L", "wheel_R", "targetCamForward", "gunmount_R_hp" })
        { var t = Find(root, n); if (t == null) { log.AppendLine("no " + n); continue; } var mr = t.GetComponent<MeshRenderer>(); if (mr) mr.enabled = false; NoMesh(t); }
        foreach (var s in new[] { "L", "R" }) { var cols = Need("wing2_" + s).GetComponents<MeshCollider>(); for (int i = 1; i < cols.Length; i++) cols[i].enabled = false;
            var l2 = Find(Need("wing2_" + s), "wing2_" + s + "1_LOD1"); if (l2) Doom(l2); }
        foreach (var n in new[] { "gunmount_L", "pylon1_L", "pylon1_R", "pylon2_L", "pylon2_R" }) DoomName(n);
        var fe = C(root, "FlareEjector");
        foreach (var s in new[] { "L", "R" })
        {
            var f = Need("flareEjector_" + s); f.SetParent(Need("tailboom_L"), true);
            f.position = new Vector3(s == "L" ? -0.15f : 0.15f, -0.55f, -1.4f);
            f.rotation = Quaternion.LookRotation(new Vector3(s == "L" ? -0.4f : 0.4f, -1f, -0.3f).normalized, Vector3.forward);
        }
        var fso = new SerializedObject(fe); var eps = fso.FindProperty("ejectionPoints");
        for (int i = 0; i < eps.arraySize; i++) eps.GetArrayElementAtIndex(i).FindPropertyRelative("part").objectReferenceValue = C(Need("tailboom_L"), "AeroPart");
        fso.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ weapons: side gun (fixed forward for this build), two stub pylons
    static void Weapons()
    {
        var wm = C(Need("cockpit_F"), "WeaponManager");
        var so = new SerializedObject(wm); var sets = so.FindProperty("hardpointSets");
        // keep set 0 (internal gun) and set 2 (fuselage pylons); drop the rest
        var keep = new[] { 0, 2 };
        for (int i = sets.arraySize - 1; i >= 0; i--) if (!keep.Contains(i)) sets.DeleteArrayElementAtIndex(i);
        so.ApplyModifiedPropertiesWithoutUndo();
        so = new SerializedObject(wm); sets = so.FindProperty("hardpointSets");
        var g = sets.GetArrayElementAtIndex(0); g.FindPropertyRelative("name").stringValue = "Side Gun";
        var hps = g.FindPropertyRelative("hardpoints"); hps.arraySize = 1;
        var gunHp = Find(root, "gunmount_R_hp");
        hps.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = gunHp;
        hps.GetArrayElementAtIndex(0).FindPropertyRelative("part").objectReferenceValue = C(Need("gunmount_R"), "AeroPart");
        var opts = g.FindPropertyRelative("weaponOptions"); opts.arraySize = 2; opts.GetArrayElementAtIndex(0).objectReferenceValue = null;
        opts.GetArrayElementAtIndex(1).objectReferenceValue = Mount("UL2_sidegun");
        // stub pylons split left/right like the Ibis side pylons (Nick 2026-11-12 16:20): a TOP station per side where the
        // racks mount flipped onto the stub, and an UNDER station per side for the gunpods and the bomb, which hang normally.
        // Top and under on the same side preclude each other (stock precludingHardpointSets, like the Ibis cargo bays).
        foreach (var s in new[] { "L", "R" })
        {
            var u = root.Find("pylon_under_" + s); if (u == null) { u = new GameObject("pylon_under_" + s).transform; u.SetParent(root, false); }
            u.position = P("hardpoint_" + s) + new Vector3(0f, -0.125f, 0f); u.rotation = Quaternion.identity;
        }
        while (sets.arraySize < 5) sets.InsertArrayElementAtIndex(1);
        string[] topOpts = { "RocketPod1_single", "AGM1_single", "AAM3_single", "Rocket2_4Pod", "ECMPod1", "ATGM1_quad" };
        string[] underOpts = { "gun_12.7mm_pod", "gun_20mm_pod", "bomb_125_single" };
        var defs = new[] {
            new { name = "Left Stub (top)", hp = "pylon_fuselage_L", sym = false, symName = "Stub Pylons (top)", opts = topOpts, pre = 3 },
            new { name = "Right Stub (top)", hp = "pylon_fuselage_R", sym = true, symName = "Stub Pylons (top)", opts = topOpts, pre = 4 },
            new { name = "Left Stub (under)", hp = "pylon_under_L", sym = false, symName = "Stub Pylons (under)", opts = underOpts, pre = 1 },
            new { name = "Right Stub (under)", hp = "pylon_under_R", sym = true, symName = "Stub Pylons (under)", opts = underOpts, pre = 2 } };
        for (int i = 0; i < 4; i++)
        {
            var d = defs[i]; var p = sets.GetArrayElementAtIndex(i + 1);
            p.FindPropertyRelative("name").stringValue = d.name;
            p.FindPropertyRelative("SymmetryWithPrev").boolValue = d.sym;
            p.FindPropertyRelative("SymmetryName").stringValue = d.symName;
            var pr = p.FindPropertyRelative("precludingHardpointSets"); pr.arraySize = 1; pr.GetArrayElementAtIndex(0).intValue = d.pre;
            var php = p.FindPropertyRelative("hardpoints"); php.arraySize = 1;
            php.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = root.Find(d.hp) ?? Find(root, d.hp);
            php.GetArrayElementAtIndex(0).FindPropertyRelative("part").objectReferenceValue = C(root, "AeroPart");
            var op = p.FindPropertyRelative("weaponOptions"); op.arraySize = d.opts.Length + 1; op.GetArrayElementAtIndex(0).objectReferenceValue = null;
            for (int k = 0; k < d.opts.Length; k++)
            {
                var m = AssetDatabase.LoadMainAssetAtPath(Placeholder(d.opts[k], "WeaponMount"));
                if (m == null) log.AppendLine("MISSING mount " + d.opts[k]);
                op.GetArrayElementAtIndex(k + 1).objectReferenceValue = m;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("hardpoint sets: " + sets.arraySize);
    }

    static GameObject CopyPrefab(string src, string dst)
    {
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Placeholder(src, "GameObject")));
        PrefabUtility.UnpackPrefabInstance(tmp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        tmp.name = dst; return tmp;
    }
    static Object SaveMount(GameObject go, string key, string copyFrom, Object info, string mountName)
    {
        var pf = PrefabUtility.SaveAsPrefabAsset(go, MNT + key + ".prefab");
        string mpath = MNT + key + ".asset";
        var m = AssetDatabase.LoadMainAssetAtPath(mpath);
        if (m == null)
        {
            AssetDatabase.CopyAsset(Placeholder(copyFrom, "WeaponMount"), mpath); AssetDatabase.ImportAsset(mpath);
            m = AssetDatabase.LoadMainAssetAtPath(mpath);
        }
        var so = new SerializedObject(m);
        so.FindProperty("prefab").objectReferenceValue = pf;
        so.FindProperty("jsonKey").stringValue = key;
        if (info != null) so.FindProperty("info").objectReferenceValue = info;
        so.FindProperty("mountName").stringValue = mountName;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(m);
        log.AppendLine("mount " + key + " (" + mountName + ") from " + copyFrom);
        return m;
    }
    // side gun: the stock door gun turret (Ibis), re-skinned with the UL-2 gun in a Ka-50 style limited arc: a little
    // traverse, mostly depression. Stock flags already let it fire with the gear down and on the ground.
    static void Mounts()
    {
        var go = CopyPrefab("turret_12.7mm_door", "UL2_sidegun");
        try
        {
            var tr = go.transform.Find("traverse"); tr.localPosition = Vector3.zero; tr.localRotation = Quaternion.identity;
            tr.GetComponent<MeshFilter>().sharedMesh = null;
            var br = tr.Find("barrel"); br.localPosition = Vector3.zero; br.localRotation = Quaternion.identity;
            br.GetComponent<MeshFilter>().sharedMesh = M["gun_R"]; br.GetComponent<MeshRenderer>().sharedMaterials = Mats("gun_R");
            var mz = br.Find("muzzle"); mz.localPosition = new Vector3(0f, 0f, 1.58f); mz.localRotation = Quaternion.identity;
            var tu = C(tr, "Turret");
            Set(tu, "traverseRange", 10f); Set(tu, "minElevation", -40f); Set(tu, "maxElevation", 5f);
            Set(tu, "traverseRate", 40f); Set(tu, "elevationRate", 35f);
            var m = SaveMount(go, "UL2_sidegun", "turret_12.7mm_door", null, "12.7mm Side Gun (600 rounds)");
            var so = new SerializedObject(m);
            so.FindProperty("GearSafety").boolValue = false; so.FindProperty("GroundSafety").boolValue = false;
            so.FindProperty("drag").floatValue = 0.05f; so.FindProperty("emptyDrag").floatValue = 0.05f;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // ------------------------------------------------------------------ masses, areas, joints (literal values)
    static void Part(string n, float mass, float area, float drag, float fuel)
    {
        var t = n == "" ? root : Need(n);
        var ap = C(t, "AeroPart");
        Set(ap, "mass", mass); Set(ap, "wingArea", area); Set(ap, "dragArea", drag);
        var ft = C(t, "FuelTank");
        if (ft != null) Set(ft, "fuelCapacity", fuel < 0f ? 0f : fuel);
        else if (fuel > 0f) log.AppendLine("no FuelTank on " + n);
    }
    static void J(string part, params object[] pairs) { Joints(part, pairs); }
    static void AeroAndDamage()
    {
        // ~680 kg empty with the gun; 60 kg fuel in the frame tank
        Part("", 120f, 0f, 0.10f, -1f);
        Part("cockpit_F", 60f, 0.4f, 0.08f, -1f); Part("nose", 10f, 0f, 0.05f, -1f);
        Part("cockpit_R", 40f, 0f, 0.08f, -1f);
        Part("fuselage", 60f, 0f, 0.06f, 60f);
        Part("intake", 15f, 0f, 0.05f, -1f);
        Part("mast", 40f, 0f, 0.04f, -1f); Part("rotorHead", 20f, 0f, 0.03f, -1f);
        Part("engine_core", 60f, 0f, 0.06f, -1f); Part("engine_L", 15f, 0f, 0.02f, -1f); Part("engine_R", 15f, 0f, 0.02f, -1f);
        Part("propHub", 15f, 0f, 0.02f, -1f);
        Part("gunmount_R", 25f, 0f, 0.05f, -1f);
        Part("nacelle_L", 20f, 0f, 0.04f, -1f); Part("nacelle_R", 20f, 0f, 0.04f, -1f);
        Part("tailboom_L", 15f, 0f, 0.02f, -1f); Part("tailboom_R", 12f, 0f, 0.02f, -1f);
        // rotor: ~31 m2 of lifting blade for the gyro's low stall (blade aero is a fixed span; the head spins only visually)
        foreach (var s in new[] { "L", "R" })
        {
            Part("wing1_" + s, 8f, 4.5f, 0.01f, -1f); Part("wing2_" + s, 8f, 6f, 0.01f, -1f); Part("wing3_" + s, 6f, 4.5f, 0.01f, -1f);
            Part("aileron_" + s, 2f, 2f, 0f, -1f);
            Part("hstab_" + s, 5f, 0.9f, 0.01f, -1f); Part("hstabTip_" + s, 3f, 0.5f, 0.01f, -1f);
            Part("elevator_" + s, 3f, 0.7f, 0f, -1f); Part("elevatorTip_" + s, 2f, 0.4f, 0f, -1f);
            foreach (var w in new[] { "wing1_", "wing2_", "wing3_", "hstab_", "hstabTip_", "elevator_", "elevatorTip_" }) Set(AP(w + s), "centerOfLift", Vector3.zero);
            Set(AP("aileron_" + s), "centerOfLift", Vector3.zero);
        }
        Part("vstab1_L", 6f, 0.6f, 0.01f, -1f); Part("vstab1_R", 4f, 0.3f, 0.01f, -1f);
        Part("rudder_L", 3f, 0.5f, 0f, -1f); Part("rudder_R", 2f, 0.2f, 0f, -1f);
        // joints: each part to its structural parent (break forces like the Cricket's light parts)
        J("cockpit_R", "COIN_PLACEHOLDER", 15000f, "fuselage", 10000f);
        J("cockpit_F", "COIN_PLACEHOLDER", 20000f, "cockpit_R", 10000f);
        J("nose", "cockpit_F", 5000f);
        J("fuselage", "COIN_PLACEHOLDER", 15000f);
        J("intake", "fuselage", 6000f, "cockpit_R", 4000f);
        J("mast", "fuselage", 15000f, "intake", 5000f);
        J("rotorHead", "mast", 12000f);
        J("engine_core", "mast", 12000f, "fuselage", 6000f);
        J("engine_L", "engine_core", 6000f); J("engine_R", "engine_core", 6000f);
        J("propHub", "engine_core", 10000f);
        J("gunmount_R", "COIN_PLACEHOLDER", 10000f, "cockpit_F", 5000f);
        J("nacelle_L", "fuselage", 10000f, "COIN_PLACEHOLDER", 6000f); J("nacelle_R", "fuselage", 10000f, "COIN_PLACEHOLDER", 6000f);
        J("tailboom_L", "fuselage", 10000f); J("tailboom_R", "tailboom_L", 8000f);
        J("vstab1_L", "tailboom_R", 6000f); J("vstab1_R", "vstab1_L", 3000f);
        J("rudder_L", "vstab1_L", 20000f); J("rudder_R", "vstab1_R", 20000f);
        foreach (var s in new[] { "L", "R" })
        {
            J("wing1_" + s, "rotorHead", 12000f); J("wing2_" + s, "wing1_" + s, 8000f); J("wing3_" + s, "wing2_" + s, 6000f);
            J("aileron_" + s, "wing3_" + s, 50000f);
            J("hstab_" + s, "tailboom_R", 6000f, "vstab1_L", 4000f); J("hstabTip_" + s, "hstab_" + s, 3000f);
            J("elevator_" + s, "hstab_" + s, 20000f); J("elevatorTip_" + s, "hstabTip_" + s, 20000f);
        }
        log.AppendLine("aero + joints set");
    }

    // ------------------------------------------------------------------ def, parameters, hangar op (literal values)
    static void Assets()
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(DST);
        var par = AssetDatabase.LoadMainAssetAtPath(PAR);
        var so = new SerializedObject(par);
        so.FindProperty("aircraftName").stringValue = "UL-2 Airtrike";
        so.FindProperty("aircraftGLimit").floatValue = 3.5f;
        so.FindProperty("PIDReferenceAirspeed").floatValue = 40f;
        so.FindProperty("maxSpeed").floatValue = 50f;
        so.FindProperty("takeoffSpeed").floatValue = 20f;
        so.FindProperty("takeoffDistance").floatValue = 150f;
        so.FindProperty("verticalLanding").boolValue = false;
        so.FindProperty("turningRadius").floatValue = 300f;
        so.FindProperty("cornerSpeed").floatValue = 35f;
        so.FindProperty("approachSpeed").floatValue = 26f;
        so.FindProperty("landingSpeed").floatValue = 22f;
        so.FindProperty("shortLandingSpeed").floatValue = 20f;
        so.FindProperty("groundTurningRadius").floatValue = 6f;
        so.FindProperty("DefaultFuelLevel").floatValue = 1f;
        so.FindProperty("cruiseThrottle").floatValue = 0.75f;
        var liv = so.FindProperty("liveries"); liv.arraySize = 1;
        var li = liv.GetArrayElementAtIndex(0);
        li.FindPropertyRelative("name").stringValue = "Army Olive";
        li.FindPropertyRelative("faction").objectReferenceValue = null;
        li.FindPropertyRelative("assetReference.m_AssetGUID").stringValue = AssetDatabase.AssetPathToGUID(LIVERY);
        li.FindPropertyRelative("assetReference.m_SubObjectName").stringValue = "";
        // five hardpoint sets now: side gun, left/right stub top, left/right stub under
        var lo = so.FindProperty("loadouts");
        for (int i = 0; i < lo.arraySize; i++)
        {
            var w = lo.GetArrayElementAtIndex(i).FindPropertyRelative("weapons"); w.arraySize = 5;
            for (int k = 0; k < 5; k++) w.GetArrayElementAtIndex(k).objectReferenceValue = null;
            if (i == 0) continue;
            w.GetArrayElementAtIndex(0).objectReferenceValue = Mount("UL2_sidegun");
            var at = AssetDatabase.LoadMainAssetAtPath(Placeholder("ATGM1_quad", "WeaponMount"));
            w.GetArrayElementAtIndex(1).objectReferenceValue = at; w.GetArrayElementAtIndex(2).objectReferenceValue = at;
        }
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(par);

        var def = AssetDatabase.LoadMainAssetAtPath(DEF);
        so = new SerializedObject(def);
        so.FindProperty("jsonKey").stringValue = "UL2Airtrike";
        so.FindProperty("unitName").stringValue = "UL-2 Airtrike";
        var wt = so.FindProperty("WorkshopTag"); if (wt != null) wt.stringValue = "UL-2 Airtrike";
        so.FindProperty("code").stringValue = "UL-2";
        so.FindProperty("description").stringValue = "A light three-seat gyroplane for scouting and harassment from rough strips. The pilot lases targets from a small optical ball under the nose, the copilot runs the sensors, and a soldier in the third seat works a 12.7 mm gun in a side mount.";
        so.FindProperty("length").floatValue = 6.5f;
        so.FindProperty("width").floatValue = 9.2f;
        so.FindProperty("height").floatValue = 3.3f;
        so.FindProperty("value").floatValue = 4f;
        so.FindProperty("mass").floatValue = 680f;
        so.FindProperty("radarSize").floatValue = 0.3f;
        so.FindProperty("spawnOffset").vector3Value = new Vector3(0f, 1.15f, 0f);
        so.FindProperty("unitPrefab").objectReferenceValue = pf;
        so.FindProperty("aircraftParameters").objectReferenceValue = par;
        if (so.FindProperty("aircraftInfo") != null)
        {
            so.FindProperty("aircraftInfo.emptyWeight").floatValue = 680f;
            so.FindProperty("aircraftInfo.maxWeight").floatValue = 1100f;
            so.FindProperty("aircraftInfo.maxSpeed").floatValue = 180f;
            so.FindProperty("aircraftInfo.stallSpeed").floatValue = 50f;
            so.FindProperty("aircraftInfo.maneuverability").floatValue = 3f;
        }
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(def);
        var pc = PrefabUtility.LoadPrefabContents(DST);
        try { Set(C(pc.transform, "Aircraft"), "definition", def); PrefabUtility.SaveAsPrefabAsset(pc, DST); }
        finally { PrefabUtility.UnloadPrefabContents(pc); }

        string op = MOD + "OpAddAircraftToHangars.asset";
        if (AssetDatabase.LoadMainAssetAtPath(op) == null)
        {
            var o = ScriptableObject.CreateInstance("OpAddAircraftToHangars");
            if (o == null) { log.AppendLine("no OpAddAircraftToHangars type"); return; }
            AssetDatabase.CreateAsset(o, op);
        }
        so = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(op));
        so.FindProperty("aircraftJsonKey").stringValue = "UL2Airtrike";
        var hs = so.FindProperty("hangars"); hs.arraySize = 1;
        hs.GetArrayElementAtIndex(0).FindPropertyRelative("hangarUnitJsonKey").stringValue = "hangar_med";
        var hn = hs.GetArrayElementAtIndex(0).FindPropertyRelative("hangarNames"); hn.arraySize = 1; hn.GetArrayElementAtIndex(0).stringValue = "hangar_med";
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(so.targetObject);
        string mi = MOD + "modinfo.json";
        System.IO.File.WriteAllText(mi, "{\n    \"displayName\": \"UL-2 Airtrike TEST BUILD\",\n    \"version\": \"0.14.0\"\n}\n");
        log.AppendLine("def, parameters, hangar op, modinfo written");
    }
}
