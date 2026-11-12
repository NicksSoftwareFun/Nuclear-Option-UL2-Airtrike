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
        MAT["UL2_skin"] = LitMat("UL2_skin", new Color32(150, 152, 155, 255), 0.45f, 0f);
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
        var cf = Need("cockpit_F");
        Visual("gear_F_visible", "gear_F", cf);
        // the Cricket cockpit, canopies and ejection seats go invisible; their components stay for the stock logic
        foreach (var n in new[] { "cockpit_F_interior", "joystick", "throttle", "throttleZero", "targetScreen", "cockpit_F_exterior",
                                  "canopyFrame_F", "canopy_F_LOD1", "canopyGlass_F_interior", "canopyFrame_F_exterior", "canopyFrame_F_interior",
                                  "canopyGlass_F_exterior", "canopyFrame_R", "canopy_R_LOD1", "canopy_glass_R_exterior", "canopy_glass_R_interior",
                                  "geardoor_FL", "geardoor_FR", "geardoor_LL", "geardoor_LR", "geardoor_RL", "geardoor_RR", "ejectionSeat2" })
            Hide(n);
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
            var hp = Need("pylon_fuselage_" + s); hp.SetParent(root, true); hp.position = P("hardpoint_" + s); hp.rotation = Quaternion.identity;
        }
        DoomName("pylon_L"); DoomName("pylon_R"); DoomName("ventralHardpoint");
        // side gun: a mount part on the frame, the gun part in it; the Cricket's right gun hardpoint moves into it
        var ghp = Need("gunmount_R"); ghp.name = "gunmount_R_hp";
        NewPart("intake", "gunmount_R", "COIN_PLACEHOLDER");
        var gun = NewPart("intake", "gun_R", "gunmount_R");
        ghp.SetParent(gun, true); ghp.position = P("gunPivot"); ghp.rotation = Quaternion.identity;
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
        hps.GetArrayElementAtIndex(0).FindPropertyRelative("part").objectReferenceValue = C(Need("gun_R"), "AeroPart");
        var p = sets.GetArrayElementAtIndex(1); p.FindPropertyRelative("name").stringValue = "Stub Pylons";
        var php = p.FindPropertyRelative("hardpoints");
        for (int i = 0; i < php.arraySize; i++) php.GetArrayElementAtIndex(i).FindPropertyRelative("part").objectReferenceValue = C(root, "AeroPart");
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("hardpoint sets: " + sets.arraySize);
    }

    static void Mounts() { }

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
        Part("gunmount_R", 25f, 0f, 0.05f, -1f); Part("gun_R", 30f, 0f, 0.02f, -1f);
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
        J("gun_R", "gunmount_R", 6000f);
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
        li.FindPropertyRelative("name").stringValue = "Default";
        li.FindPropertyRelative("faction").objectReferenceValue = null;
        li.FindPropertyRelative("assetReference.m_AssetGUID").stringValue = "";
        li.FindPropertyRelative("assetReference.m_SubObjectName").stringValue = "";
        // two hardpoint sets now: side gun, stub pylons
        var lo = so.FindProperty("loadouts");
        for (int i = 0; i < lo.arraySize; i++)
        {
            var w = lo.GetArrayElementAtIndex(i).FindPropertyRelative("weapons"); w.arraySize = 2;
            w.GetArrayElementAtIndex(0).objectReferenceValue = i == 0 ? null : AssetDatabase.LoadMainAssetAtPath(Placeholder("gun_12.7mm_internal", "WeaponMount"));
            w.GetArrayElementAtIndex(1).objectReferenceValue = i == 0 ? null : AssetDatabase.LoadMainAssetAtPath(Placeholder("RocketPod1_single", "WeaponMount"));
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
        System.IO.File.WriteAllText(mi, "{\n    \"displayName\": \"UL-2 Airtrike TEST BUILD\",\n    \"version\": \"0.1.0\"\n}\n");
        log.AppendLine("def, parameters, hangar op, modinfo written");
    }
}
