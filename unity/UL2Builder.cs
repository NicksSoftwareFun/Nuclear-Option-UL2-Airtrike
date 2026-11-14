using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Builds UL2Airtrike.prefab from a pristine copy of the CI-22 Cricket plus the UL2 art (UL2_parts.fbx + UL2_layout.json),
// and the mod's own def, parameters, hangar op and weapon mounts. Run from the menu (UL2 > Rebuild) or UL2Builder.Build().
// Every value is a literal on purpose.
public static partial class UL2Builder
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
    static int jointIterations = 12;   // 0.15.7: stiffer chain (mast lean, soft tail)
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
    // a lit material showing a picture (copilot MFD and gauges), slightly self lit like a screen
    static Material PicMat(string name)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ART + name + ".png");
        var m = LitMat(name, Color.white, 0.12f, 0f);   // 0.15.29 (Nick 12:29): was 0.8, the sun behind washed the dials out
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_EmissionMap")) { m.SetTexture("_EmissionMap", tex); m.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.5f)); m.EnableKeyword("_EMISSION"); }
        EditorUtility.SetDirty(m); return m;
    }
    // 0.15.26 (Nick 04:59): rotor blur disc material = a copy of the stock attack helicopter's transparent rotor blur
    // material (same shader settings) with our own disc texture: a faint dark disc, tip ring and segment rings
    static Material BlurMat(string name)
    {
        string tp = ART + name + ".png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
        if (ti != null) { ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport(); }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
        string path = ART + name + ".mat";
        // 0.15.27: base on the stock ducted-fan blur material like the P220 fans (the attack helo one carries a metallic-map keyword)
        var src = AssetDatabase.LoadAssetAtPath<Material>(PH + "Material/UtilityHelo1_fan_blur_PLACEHOLDER.mat");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(src); m.name = name; AssetDatabase.CreateAsset(m, path); }
        else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); m.shaderKeywords = src.shaderKeywords; m.renderQueue = src.renderQueue; }
        foreach (var t in new[] { "_BaseMap", "_MainTex" }) if (m.HasProperty(t)) m.SetTexture(t, tex);
        foreach (var t in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" }) if (m.HasProperty(t)) m.SetTexture(t, null);
        m.SetColor("_BaseColor", Color.white); if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        EditorUtility.SetDirty(m); log.AppendLine(name + " material from " + (src ? src.name : "NONE") + ", texture " + (tex != null) + ", queue " + m.renderQueue);
        return m;
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
    // icon PNGs import as uncompressed single sprites, like the P220 icon
    public static Sprite IconSprite(string ip)
    {
        var ti = AssetImporter.GetAtPath(ip) as TextureImporter;
        if (ti == null) return null;
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.filterMode = FilterMode.Bilinear;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(ip);
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
        // engine internals where a cylinder bank meets the crankcase (Nick 04:29): the stock engine mechanism trim sheet itself,
        // which Blueprinter swaps for the game's own material (stock parts carry it as their mechanical submesh)
        MAT["UL2_enginternal"] = AssetDatabase.LoadAssetAtPath<Material>(PH + "Material/jet_mechanical_PLACEHOLDER.mat");
        MAT["UL2_tyre"] = LitMat("UL2_tyre", new Color32(22, 22, 22, 255), 0.15f, 0f);
        MAT["UL2_seat"] = LitMat("UL2_seat", new Color32(70, 72, 74, 255), 0.25f, 0f);
        MAT["UL2_cockpit"] = LitMat("UL2_cockpit", new Color32(60, 62, 65, 255), 0.3f, 0f);
        MAT["UL2_screen"] = LitMat("UL2_screen", new Color32(12, 14, 16, 255), 0.85f, 0f);
        // seat rifles in the seat 3 gunners' FDE scheme (Nick 2026-11-12 21:23)
        MAT["UL2_fde"] = LitMat("UL2_fde", new Color32(150, 126, 96, 255), 0.32f, 0f);   // lighter FDE, less bronze (Nick 04:04 11-13)
        MAT["UL2_bronze"] = LitMat("UL2_bronze", new Color32(150, 128, 100, 255), 0.45f, 0.10f);   // FDE cerakote, was bronze
        MAT["UL2_black"] = LitMat("UL2_black", new Color32(34, 34, 33, 255), 0.52f, 0.4f);
        MAT["UL2_fabric"] = LitMat("UL2_fabric", new Color32(166, 134, 94, 255), 0.03f, 0f);
        MAT["UL2_red"] = LitMat("UL2_red", new Color32(200, 36, 28, 255), 0.4f, 0f);   // compass lubber pointer (Nick 04:56)
        MAT["UL2_rotorblur"] = BlurMat("UL2_rotorblur"); MAT["UL2_propblur"] = BlurMat("UL2_propblur");
        MAT["UL2_white"] = LitMat("UL2_white", new Color32(232, 234, 232, 255), 0.75f, 0f);   // spinner tip and spiral (Nick 05:12)
        MAT["UL2_yellow"] = LitMat("UL2_yellow", new Color32(236, 190, 30, 255), 0.5f, 0f);   // prop tip and band stripes (Nick 12:29)
        var glass = AssetDatabase.LoadAssetAtPath<Material>(PH + "Material/trainer_glass_PLACEHOLDER.mat");
        MAT["UL2_glass"] = glass != null ? glass : MAT["UL2_screen"];
        try { Mounts(); } catch (System.Exception ex) { log.AppendLine("MOUNTS ERROR " + ex); }
        try { Seat3Mounts(); } catch (System.Exception ex) { log.AppendLine("SEAT3 MOUNTS ERROR " + ex); }
        try { Seat3Ecm(); } catch (System.Exception ex) { log.AppendLine("SEAT3 ECM ERROR " + ex); }
        try { Seat3XBomb(); } catch (System.Exception ex) { log.AppendLine("SEAT3 XBOMB ERROR " + ex); }

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
        // seat 3: the passenger now comes with the Seat 3 hardpoint options (UL2Seat3.cs); empty option = empty seat
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
            // 0.15.4: the controls sit higher in the model itself (fighter grip, throttle quadrant on a raised console)
            var js = Need("joystick"); MoveAlone(js, P("x_stick")); SetMesh(js, "x_stick"); js.GetComponent<Renderer>().enabled = true;
            // 0.15.31 (Nick 16:38): the throttle SLIDES along its slot (stock Cockpit throttle in motion mode: local z =
            // throttle x range). Zero sits 75 mm aft of the slot centre (idle at the aft gate), full is 75 mm forward of it
            var tz = Need("throttleZero"); MoveAlone(tz, P("x_throttle")); tz.rotation = root.rotation; tz.position -= root.forward * 0.075f;
            var th = Need("throttle"); th.localPosition = Vector3.zero; th.localRotation = Quaternion.identity; SetMesh(th, "x_throttle"); th.GetComponent<Renderer>().enabled = true;
            foreach (var ck in root.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "Cockpit"))
            { Set(ck, "throttles.Array.data[0].rotation", false); Set(ck, "throttles.Array.data[0].motion", true); Set(ck, "throttles.Array.data[0].range", 0.15f); }
            // the MFDs show the targeting ball's camera, the stock way (TargetCam draws onto targetScreen's material)
            var ts = Need("targetScreen"); MoveAlone(ts, P("x_mfd1"));
            // 0.15.20 (Nick 03:59: MFD turned 90 deg): the stock targetScreen maps u left->right over the main display
            // (u 0..0.79) and v bottom->top; the exported quad had u running down the screen. Remap to the stock way, a
            // centred square of the main display (u 0.145..0.645)
            var mfd = Object.Instantiate(M["x_mfd1"]); mfd.name = "UL2_mfd1_screen";
            var uvs = mfd.uv; for (int i = 0; i < uvs.Length; i++) { var o0 = uvs[i]; uvs[i] = new Vector2(0.79f * o0.y, 1f - o0.x / 0.5f); } mfd.uv = uvs;   // 0.15.30 (Nick 13:00): the whole main display, u 0..0.79, on a 1.58:1 screen
            string mfdPath = MOD + "UL2_mfd1_screen.asset"; AssetDatabase.DeleteAsset(mfdPath); AssetDatabase.CreateAsset(mfd, mfdPath);
            ts.GetComponent<MeshFilter>().sharedMesh = mfd; var tr = ts.GetComponent<Renderer>(); tr.enabled = true;
            // copilot station: static pictures, not live screens (Nick 20:53)
            var cr = Need("cockpit_R");
            foreach (var pic in new[] { "mfd2", "inst2" })
            {
                var pt = Find(cr, "UL2_" + pic) ?? New("UL2_" + pic, cr); pt.position = P("x_" + pic); pt.rotation = Quaternion.identity;
                SetMesh(pt, "x_" + pic); pt.GetComponent<MeshRenderer>().sharedMaterial = PicMat("UL2_" + pic + "_img");
            }
            log.AppendLine("screen material " + (tr.sharedMaterial ? tr.sharedMaterial.name : "none"));
        }
        // live gauge needles: a pivot facing the pilot (local Z = panel normal) holding the needle mesh; the plugin turns the pivot
        {
            var nrm = new Vector3(0f, Mathf.Sin(20f * Mathf.Deg2Rad), -Mathf.Cos(20f * Mathf.Deg2Rad));   // Blender panel tilt -20 deg about X
            int ng = 0;
            foreach (var id in new[] { "asi", "adi", "alt", "eng", "hdg", "vsi" })   // Trident-style six-pack (Nick 22:04); engine rpm for the turn coordinator (04:56)
            {
                if (L.ContainsKey("x_gf_" + id))
                {   // printed dial face behind the needle
                    var gf = Find(cf, "UL2_gface_" + id) ?? New("UL2_gface_" + id, cf); gf.position = P("x_gf_" + id); gf.rotation = Quaternion.identity;
                    SetMesh(gf, "x_gf_" + id); gf.GetComponent<MeshRenderer>().sharedMaterial = PicMat("UL2_gf_" + id + "_img");
                    if (id == "hdg")
                    {   // 0.15.26 (Nick 04:56): the compass card turns (plugin) under the fixed red pointer
                        var cp = Find(cf, "UL2_gauge_hdgcard") ?? New("UL2_gauge_hdgcard", cf);
                        gf.SetParent(cf, true); cp.position = P("x_gf_" + id); cp.rotation = Quaternion.LookRotation(nrm, Vector3.up);
                        gf.SetParent(cp, true);
                    }
                }
                if (!L.ContainsKey("x_g_" + id)) continue;
                var pv = Find(cf, "UL2_gauge_" + id) ?? New("UL2_gauge_" + id, cf);
                pv.position = P("x_g_" + id); pv.rotation = Quaternion.LookRotation(nrm, Vector3.up);
                var nd = Find(pv, "needle") ?? New("needle", pv);
                nd.position = pv.position; nd.rotation = Quaternion.identity; SetMesh(nd, "x_g_" + id); ng++;
            }
            foreach (var old in new[] { "UL2_gauge_turn", "UL2_gface_turn" }) { var o = Find(cf, old); if (o) Object.DestroyImmediate(o.gameObject); }   // turn coordinator gone (0.15.26)
            log.AppendLine("gauges " + ng);
        }
        // rudder pedals: the plugin slides UL2_pedal_L/R fore and aft on their rails with the yaw input (P220 / PB-22 way)
        foreach (var sd in new[] { "L", "R" })
        {
            var pd = Find(cf, "UL2_pedal_" + sd) ?? New("UL2_pedal_" + sd, cf);
            pd.position = P("x_pedal_" + sd); pd.rotation = Quaternion.identity; SetMesh(pd, "x_pedal_" + sd);
        }
        // 0.15.12 (Nick 00:05): copilot game controller, backup side stick, throttle and small pedals under cockpit_R;
        // the plugin moves the stick, throttle and pedals with the inputs and puts his hands on the controller
        {
            var cr2 = Need("cockpit_R");
            foreach (var nm in new[] { "ctrl2", "stick2", "thr2", "pedal2_L", "pedal2_R" })
            {
                if (!L.ContainsKey("x_" + nm)) { log.AppendLine("missing x_" + nm); continue; }
                var t = Find(cr2, "UL2_" + nm) ?? New("UL2_" + nm, cr2);
                t.position = P("x_" + nm); t.rotation = Quaternion.identity; SetMesh(t, "x_" + nm);
            }
        }
        // 0.15.35 (Nick 22:03/22:04): the handset, stick and throttle are cockpit renderers, which the game switches off outside
        // the cockpit view, so the copilot's handset vanished in third person and the pilot had nothing to hold. Exterior
        // copies on the default layer go on the exterior renderer list (the stock cockpit_F_exterior way); the plugin keeps
        // the stick and throttle copies in step with the real ones and puts the pilots' hands on them.
        {
            var ex = new List<Object>();
            foreach (var sp in new[] { new[] { "UL2_ctrl2", "UL2_ext_ctrl2" }, new[] { "joystick", "UL2_ext_stick" }, new[] { "throttle", "UL2_ext_throttle" } })
            {
                var src = Need(sp[0]); var mf = src.GetComponent<MeshFilter>(); var mr = src.GetComponent<MeshRenderer>();
                if (mf == null || mr == null) { log.AppendLine("exterior copy: no mesh on " + sp[0]); continue; }
                var t = Find(src.parent, sp[1]) ?? New(sp[1], src.parent);
                t.localPosition = src.localPosition; t.localRotation = src.localRotation; t.localScale = src.localScale; t.gameObject.layer = 0;
                var f = t.GetComponent<MeshFilter>(); if (f == null) f = t.gameObject.AddComponent<MeshFilter>();
                var r = t.GetComponent<MeshRenderer>(); if (r == null) r = t.gameObject.AddComponent<MeshRenderer>();
                f.sharedMesh = mf.sharedMesh; r.sharedMaterials = mr.sharedMaterials; r.shadowCastingMode = mr.shadowCastingMode; r.enabled = true;
                ex.Add(r);
            }
            var acx = C(root, "Aircraft"); var so = new SerializedObject(acx); var er = so.FindProperty("exteriorRenderers"); var keep = new List<Object>();
            for (int i = 0; i < er.arraySize; i++) { var o = er.GetArrayElementAtIndex(i).objectReferenceValue; if (o != null && !ex.Contains(o)) keep.Add(o); }
            keep.AddRange(ex); ObjArray(acx, "exteriorRenderers", keep.ToArray());
            log.AppendLine("exterior control copies " + ex.Count + ", exteriorRenderers " + keep.Count);
        }
        // 5-point harnesses: own objects on the exterior renderer list, so the cockpit view hides them with the pilots (stock)
        {
            var hs = new List<Object>();
            foreach (var hp in new[] { new[] { "x_harness1", "cockpit_F" }, new[] { "x_harness2", "cockpit_R" }, new[] { "x_harness3", "intake" } })
            {
                var par = Need(hp[1]); var h = Find(par, "UL2_" + hp[0].Substring(2)) ?? New("UL2_" + hp[0].Substring(2), par);
                h.position = P(hp[0]); h.rotation = Quaternion.identity; SetMesh(h, hp[0]); hs.Add(h.GetComponent<MeshRenderer>());
            }
            var acx = C(root, "Aircraft"); var so = new SerializedObject(acx); var er = so.FindProperty("exteriorRenderers"); var keep = new List<Object>();
            for (int i = 0; i < er.arraySize; i++) { var o = er.GetArrayElementAtIndex(i).objectReferenceValue; if (o != null && !hs.Contains(o)) keep.Add(o); }
            keep.AddRange(hs); ObjArray(acx, "exteriorRenderers", keep.ToArray());
            log.AppendLine("harness on exteriorRenderers, total " + keep.Count);
        }
        // 0.15.7 (Nick 22:41: gauges dead in game): the Trident way. Every cockpit object we add sits on the cockpit camera's
        // layer (the stock targetScreen's) and is listed in Aircraft.cockpitRenderers, or the cockpit view never draws it
        {
            int lay = Need("targetScreen").gameObject.layer; var add = new List<Object>();
            string[] pre = { "UL2_gface_", "UL2_gauge_", "UL2_pedal_", "UL2_mfd2", "UL2_inst2", "UL2_ctrl2", "UL2_stick2", "UL2_thr2", "UL2_pedal2_" };
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => pre.Any(x => t.name.StartsWith(x))).ToList())
                foreach (var tt in t.GetComponentsInChildren<Transform>(true)) { tt.gameObject.layer = lay; var r = tt.GetComponent<MeshRenderer>(); if (r && !add.Contains(r)) add.Add(r); }
            var acx = C(root, "Aircraft"); var so = new SerializedObject(acx); var cr = so.FindProperty("cockpitRenderers"); var keep = new List<Object>();
            for (int i = 0; i < cr.arraySize; i++) { var o = cr.GetArrayElementAtIndex(i).objectReferenceValue; if (o != null && !add.Contains(o)) keep.Add(o); }
            keep.AddRange(add); ObjArray(acx, "cockpitRenderers", keep.ToArray());
            log.AppendLine("cockpit layer " + lay + ": " + add.Count + " renderers added, cockpitRenderers " + keep.Count);
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "EjectionSeat").ToList()) { NoMesh(t); var mc = t.GetComponent<Collider>(); if (mc) mc.enabled = false; }
        foreach (var c in root.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "Pilot")) Set(c, "ejectionSeat", false);
        // crew: the Cricket pilot and WSO (the copilot) sit at the UL-2 seats; pelvis is 0.01 below the Pilot transform
        Need("pilot").position = P("pilot") + new Vector3(0f, 0.01f, 0f);
        // first person eye point at shoulder height (Nick 20:42): the helmet camera points drop 0.20 m
        foreach (var hc in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "helmetCamPoint").ToList()) hc.position -= new Vector3(0f, 0.20f, 0f);
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
            // 0.15.29 (Nick 12:29, lights hovering off the tail): the Cricket light sits 15 cm outboard of its pivot and its
            // lens mesh floats past our end plate; put the glow right on the end plate's outer face and drop the lens
            { var fx = nl.GetComponentsInChildren<Light>(true).Select(l => l.transform).FirstOrDefault();
              var want = new Vector3(sx * 1.015f, -0.10f, -2.80f);
              if (fx != null) nl.position += want - fx.position;
              var nr = nl.GetComponent<MeshRenderer>(); if (nr != null) nr.enabled = false;
              log.AppendLine("nav light " + s + " glow at " + (fx != null ? fx.position.ToString("F3") : "none")); }
            DoomName("wingtip_" + s); DoomName("flap_" + s);
            var hb = Find(root, "HeatBlur_" + s); if (hb) hb.position = P("exhaust") + new Vector3(sx * 0.15f, 0f, 0f);
        }
        // 0.15.15 gyro model: each lifting blade part gets its own lift transform (stock liftNormal field, like the Cricket's
        // cockpit_F_liftNormal). The plugin pitches these with the rotor head (disc tilt = blade incidence), so the rotor makes
        // lift at a level attitude on the takeoff roll, the way a gyro does, instead of needing the nose rotated 10 degrees
        foreach (var s in new[] { "L", "R" })
            for (int k = 1; k <= 3; k++)
            {
                var w = Need("wing" + k + "_" + s);
                var ln = Find(w, w.name + "_liftNormal") ?? New(w.name + "_liftNormal", w);
                ln.localPosition = Vector3.zero; ln.rotation = Quaternion.identity;
                Set(C(w, "AeroPart"), "liftNormal", ln);
            }
        // spinning copy of the blades (turned in game by the plugin); the lifting blade parts keep colliders but hide their mesh
        var spin = Find(Need("rotorHead"), "rotorSpin") ?? New("rotorSpin", Need("rotorHead"));
        spin.position = P("rotorCentre"); spin.rotation = Quaternion.identity;
        // 0.15.24 (Nick 04:39): rotor swoosh = the stock single-rotor helicopter's rotor loop on its own AudioSource (same
        // mixer group, distances and rolloff, copied from the stock source); the plugin sets pitch/volume from the rotor rpm
        {
            var ah = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "GameObject/AttackHelo1_PLACEHOLDER.prefab");
            var rs = ah == null ? null : ah.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(c => c != null && c.GetType().Name == "RotorShaft");
            var srcA = rs == null ? null : new SerializedObject(rs).FindProperty("rotorSource").objectReferenceValue as AudioSource;
            var clip = rs == null ? null : new SerializedObject(rs).FindProperty("exteriorSound").objectReferenceValue as AudioClip;
            var at = Find(Need("rotorHead"), "UL2_rotorAudio") ?? New("UL2_rotorAudio", Need("rotorHead"));
            at.position = P("rotorCentre");
            var au = at.GetComponent<AudioSource>(); if (au == null) au = at.gameObject.AddComponent<AudioSource>();
            if (srcA != null) EditorUtility.CopySerialized(srcA, au);
            au.clip = clip; au.loop = true; au.playOnAwake = false; au.volume = 0f; au.pitch = 0f;
            log.AppendLine("rotor audio: " + (clip != null ? clip.name : "NO CLIP") + ", source copied " + (srcA != null));
        }
        // 0.15.26 (Nick 04:59): blur disc on the spinning frame; the plugin fades it in with rotor rpm and hides the blades
        // 0.15.28 (Nick 05:14/05:29: own blur materials never showed in game; two blurred blades, not a disc): the stock
        // attack helicopter's own blurred blade segments and material (stock SwashRotor fast meshes), two blades' worth
        // scaled 0.6 to our 0.55-4.6 m blades; the plugin swaps them for the blade copies at speed like the stock rotor
        {
            var oldD = Find(spin, "spin_disc"); if (oldD) Object.DestroyImmediate(oldD.gameObject);
            var ah = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "GameObject/AttackHelo1_PLACEHOLDER.prefab");
            var sr = ah.GetComponentsInChildren<MonoBehaviour>(true).First(c => c != null && c.GetType().Name == "SwashRotor");
            var sso = new SerializedObject(sr); var sg = sso.FindProperty("segments");
            var h0 = (sg.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue as Transform);
            const float BS = 0.6f;
            int nb = 0;
            foreach (var sd in new[] { "L", "R" })
            {
                var dv = P("blade_" + sd + "_tip") - P("rotorCentre"); dv.y = 0f; dv.Normalize();
                for (int i = 0; i < sg.arraySize; i++)
                {
                    var e = sg.GetArrayElementAtIndex(i); var st = e.FindPropertyRelative("transform").objectReferenceValue as Transform;
                    var fm = e.FindPropertyRelative("fastMesh").objectReferenceValue as Mesh; var fmat = e.FindPropertyRelative("fastMaterial").objectReferenceValue as Material;
                    float r = 0.55f + BS * Vector3.Distance(st.position, h0.position);
                    var bt = Find(spin, "UL2_rblur_" + sd + i) ?? New("UL2_rblur_" + sd + i, spin);
                    bt.localPosition = dv * r + Vector3.up * (0.05f + Mathf.Max(0f, r - 0.6f) * Mathf.Tan(1.5f * Mathf.Deg2Rad));
                    bt.localRotation = Quaternion.LookRotation(dv, Vector3.up); bt.localScale = Vector3.one * BS;
                    var mf = bt.GetComponent<MeshFilter>(); if (mf == null) mf = bt.gameObject.AddComponent<MeshFilter>(); mf.sharedMesh = fm;
                    var mr = bt.GetComponent<MeshRenderer>(); if (mr == null) mr = bt.gameObject.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = fmat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    bt.gameObject.SetActive(false); nb++;
                }
            }
            log.AppendLine("rotor blur: " + nb + " stock blurred blade segments");
            // 0.15.29 (Nick 12:29): blue disc over the inner rotor. The texture ships on our own material; the plugin moves
            // it onto the live stock blur material at runtime (our transparent copies never draw in game)
            {
                string mp = ART + "UL2_rotorinner_mesh.asset";
                var dm = AssetDatabase.LoadAssetAtPath<Mesh>(mp); bool newM = dm == null; if (newM) dm = new Mesh();
                const int SEG = 48; const float R0 = 0.16f, R1 = 1.70f;
                var vv = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                for (int side = 0; side < 2; side++)
                    for (int i = 0; i <= SEG; i++)
                    {
                        float a2 = 2f * Mathf.PI * i / SEG, c = Mathf.Cos(a2), sn = Mathf.Sin(a2);
                        foreach (var rr in new[] { R0, R1 }) { vv.Add(new Vector3(c * rr, 0f, sn * rr)); uv.Add(new Vector2(0.5f + 0.5f * c * rr / R1, 0.5f + 0.5f * sn * rr / R1)); }
                    }
                int half = (SEG + 1) * 2;
                for (int i = 0; i < SEG; i++)
                {
                    int q = i * 2; tri.AddRange(new[] { q, q + 2, q + 1, q + 1, q + 2, q + 3 });
                    int w = half + q; tri.AddRange(new[] { w, w + 1, w + 2, w + 1, w + 3, w + 2 });
                }
                dm.Clear(); dm.SetVertices(vv); dm.SetUVs(0, uv); dm.SetTriangles(tri, 0); dm.RecalculateNormals(); dm.RecalculateBounds();
                if (newM) AssetDatabase.CreateAsset(dm, mp); else EditorUtility.SetDirty(dm);
                var it = Find(spin, "UL2_rinner") ?? New("UL2_rinner", spin);
                it.localPosition = Vector3.up * 0.07f; it.localRotation = Quaternion.identity; it.localScale = Vector3.one;
                var imf = it.GetComponent<MeshFilter>(); if (imf == null) imf = it.gameObject.AddComponent<MeshFilter>(); imf.sharedMesh = dm;
                var imr = it.GetComponent<MeshRenderer>(); if (imr == null) imr = it.gameObject.AddComponent<MeshRenderer>();
                imr.sharedMaterial = BlurMat("UL2_rotorinner"); imr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                it.gameObject.SetActive(false);
                log.AppendLine("inner rotor disc: radius " + R0 + "-" + R1 + " m, texture " + (imr.sharedMaterial.GetTexture("_BaseMap") != null));
            }
        }
        // 0.15.9 (Nick 23:10): the hub bar / blade grips (rotorHead mesh) spin with the blades; the part keeps its collider
        Visual("spin_hub", "rotorHead", spin);
        { var hr = Need("rotorHead").GetComponent<MeshRenderer>(); if (hr) hr.enabled = false; }
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
        // 0.15.6: the strike-detector disc must not touch engine_core (a separate rigidbody): a touch at <60 rpm (spawn) seizes the
        // prop and sets hubFriction 100000, which then stops it dead whenever the governor cuts power below full throttle (Nick 22:09)
        var pdc = Find(ph, "propDiscCollider"); if (pdc) { pdc.position = P("propCentre") + new Vector3(0f, 0f, -0.08f); pdc.localScale = new Vector3(0.68f, 0.68f, 0.15f); }
        Lod(ph, "propHub_colmesh");
        var csp = C(ph, "ConstantSpeedProp");
        // 0.15.26 (Nick 04:59): our three blades as their own renderers on the spinning hub, wired into the stock PropBlade
        // slots, so the stock prop hides them behind its blur disc when spinning and shows them (broken stubs after a strike)
        {
            var pr = new Transform[3];
            for (int k = 0; k < 3; k++)
            {
                pr[k] = Visual("UL2_propblade_" + k, "x_propblade", hv);
                pr[k].rotation = Quaternion.Euler(0f, 0f, 120f * k);
            }
            var pso = new SerializedObject(csp); var bl = pso.FindProperty("blades");
            for (int i = 0; i < bl.arraySize; i++)
            {
                var e = bl.GetArrayElementAtIndex(i); var v = pr[i % 3];
                e.FindPropertyRelative("renderer").objectReferenceValue = v.GetComponent<MeshRenderer>();
                e.FindPropertyRelative("bladeMeshFilter").objectReferenceValue = v.GetComponent<MeshFilter>();
            }
            var dm = pso.FindProperty("bladeDamageMeshes"); dm.arraySize = 5;
            for (int i = 0; i < 4; i++) dm.GetArrayElementAtIndex(i).objectReferenceValue = M["x_propstub"];
            dm.GetArrayElementAtIndex(4).objectReferenceValue = M["x_propblade"];
            pso.ApplyModifiedPropertiesWithoutUndo();
            // 0.15.28: the stock Cricket blur disc meshes and material (own materials never showed in game), scaled to our prop
            // (stock disc 2.52 m across, ours 2.06 m) on a holder, since the stock code resets the disc's own scale every frame
            var coin = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "GameObject/COIN_PLACEHOLDER.prefab");
            var ccsp = coin.GetComponentsInChildren<MonoBehaviour>(true).First(c => c != null && c.GetType().Name == "ConstantSpeedProp");
            var cpd = coin.GetComponentsInChildren<Transform>(true).First(t => t.name == "propDisc");
            var cdm = new SerializedObject(ccsp).FindProperty("propDiscMeshes");
            var pdm = pso.FindProperty("propDiscMeshes"); pdm.arraySize = cdm.arraySize;
            for (int i = 0; i < cdm.arraySize; i++) pdm.GetArrayElementAtIndex(i).objectReferenceValue = cdm.GetArrayElementAtIndex(i).objectReferenceValue;
            pso.ApplyModifiedPropertiesWithoutUndo();
            if (pd)
            {
                var holder = Find(hv, "UL2_propDiscHolder") ?? New("UL2_propDiscHolder", hv);
                holder.position = P("x_propdisc"); holder.rotation = Quaternion.identity; holder.localScale = Vector3.one * (2.06f / 2.52f);
                pd.SetParent(holder, false); pd.localPosition = Vector3.zero; pd.localRotation = cpd.localRotation; pd.localScale = Vector3.one;
                var pmr = pd.GetComponent<MeshRenderer>(); if (pmr) pmr.sharedMaterial = cpd.GetComponent<MeshRenderer>().sharedMaterial;
                var pmf = pd.GetComponent<MeshFilter>(); if (pmf) pmf.sharedMesh = null;
            }
            var bbm = (SerializedProperty)null;
            // spinner with the white spiral, on the hub part (not the stock spinning hub): the plugin turns it at a capped rate
            var sp = Visual("UL2_spinner", "x_spinner", ph);
            log.AppendLine("prop blades wired: " + bl.arraySize + " slots on 3 renderers, damage meshes stub x4, own blur disc " + (pd != null) + ", base mat slot " + (bbm != null));
        }
        // Rotax 915-class pusher, uprated to 195 kW so it climbs at 5+ deg nose up (Nick 04:08 11-13)
        Set(csp, "bladeLength", 0.74f); Set(csp, "hubRadius", 0.12f); Set(csp, "nominalPower", 195000f); Set(csp, "momentOfInertia", 8f);
        Set(csp, "propTorqueLimit", 1500f); Set(csp, "hubFriction", 60f); Set(csp, "bladeStrength", 8000f);
        var te = C(root, "TurbineEngine");
        Set(te, "maxPower", 195000f); Set(te, "maxFuelConsumption", 0.015f); Set(te, "spoolUpTime", 1.5f); Set(te, "startupTime", 4f);
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
            Set(lg, "extendedDrag", 0.1f);   // 0.15.9: fixed gear drag 0.3 -> 0.1
            // Cricket rough-field values kept (travel, friction, contact area); springs scaled to the lighter aircraft
            if (g == "F") { Set(lg, "wheelRadius", 0.24f); Set(lg, "springRate", 60000f); Set(lg, "dampingRate", 5000f); Set(lg, "mass", 15f); }
            // 0.15.7 (Nick 22:36: tips over while taxiing): stiffer, better damped mains and less nose steering lock
            else { Set(lg, "wheelRadius", 0.27f); Set(lg, "springRate", 80000f); Set(lg, "dampingRate", 9000f); Set(lg, "mass", 20f); }
            if (g == "F") { Set(lg, "steeringLock", 22f); Set(lg, "steeringSpeed", 40f); }   // 0.15.22: plugin also fades the lock with speed
            // 0.15.8: the stock pedal brake (30% of one main wheel up to 108 km/h) yawed the narrow-track trike into a skid,
            // and Cricket tyre grip (0.85) is above the tip ratio (half track 0.81 / CG height 1.29 = 0.63), so a skid rolled
            // it over. Gentle pedal brake, and tyres that slide before they trip.
            else { Set(lg, "differentialBrakeFactor", g == "L" ? -0.25f : 0.25f); Set(lg, "frictionCoef", 0.55f); }
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
        // 0.15.25 (Nick 04:29): extra liveries the stock way, colour map only (normal/metallic/AO/damage maps are shared).
        // Glossiness is the stock LiveryData field the game puts on the skin's _Glossiness; the steel one sets it for a shine.
        ExtraLivery("steel", 0.6f); ExtraLivery("tiger", 0f);
        return mat;
    }
    static string LiveryPath(string k) { return MOD + "Art/UL2_livery_" + k + ".asset"; }
    static void ExtraLivery(string k, float gloss)
    {
        var tex = SkinTex("UL2_skin_" + k + "_b", true, false, false);
        var liv = AssetDatabase.LoadMainAssetAtPath(LiveryPath(k)) as ScriptableObject;
        if (liv == null)
        {
            var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("LiveryData")).FirstOrDefault(x => x != null);
            liv = ScriptableObject.CreateInstance(t); AssetDatabase.CreateAsset(liv, LiveryPath(k));
        }
        var so = new SerializedObject(liv);
        so.FindProperty("Texture").objectReferenceValue = tex; so.FindProperty("Glossiness").floatValue = gloss;
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(liv);
        log.AppendLine("livery " + k + " gloss " + gloss + " " + AssetDatabase.AssetPathToGUID(LiveryPath(k)));
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
            var so = new SerializedObject(owner); var p = so.FindProperty("damageMaterial.renderers");
            if (owner.transform == r.transform && p.arraySize == 0) continue;   // empty list = the part's own renderer
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
        foreach (var n in new[] { "wheel_F", "wheel_L", "wheel_R", "targetCamForward", "gunmount_R_hp", "gunmount_R" })
        { var t = Find(root, n); if (t == null) { log.AppendLine("no " + n); continue; } var mr = t.GetComponent<MeshRenderer>(); if (mr) mr.enabled = false; NoMesh(t); }
        foreach (var s in new[] { "L", "R" }) { var cols = Need("wing2_" + s).GetComponents<MeshCollider>(); for (int i = 1; i < cols.Length; i++) cols[i].enabled = false;
            var l2 = Find(Need("wing2_" + s), "wing2_" + s + "1_LOD1"); if (l2) Doom(l2); }
        foreach (var n in new[] { "gunmount_L", "pylon1_L", "pylon1_R", "pylon2_L", "pylon2_R" }) DoomName(n);
        // visual detection (Nick 20:24): 6 km for the aircraft; the seat 3 spotter raises it to 10 km in the plugin
        foreach (var det in root.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "TargetDetector"))
        { var ds = new SerializedObject(det); var vr = ds.FindProperty("visualRange"); if (vr != null && vr.floatValue >= 1000f) { vr.floatValue = 6000f; ds.ApplyModifiedPropertiesWithoutUndo(); log.AppendLine("visual range 6000 on " + det.name); } }
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
        var g = sets.GetArrayElementAtIndex(0); g.FindPropertyRelative("name").stringValue = "Forward Gun";
        var hps = g.FindPropertyRelative("hardpoints"); hps.arraySize = 1;
        var gunHp = Find(root, "gunmount_R_hp");
        hps.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = gunHp;
        hps.GetArrayElementAtIndex(0).FindPropertyRelative("part").objectReferenceValue = C(Need("gunmount_R"), "AeroPart");
        var opts = g.FindPropertyRelative("weaponOptions"); opts.arraySize = 2; opts.GetArrayElementAtIndex(0).objectReferenceValue = null;
        opts.GetArrayElementAtIndex(1).objectReferenceValue = Mount("UL2_sidegun");
        // stub pylons split left/right like the Ibis side pylons (Nick 2026-11-12 16:20): a TOP station per side where the
        // racks mount flipped onto the stub, and an UNDER station per side for the gunpods and the bomb, which hang normally.
        // Top and under on the same side preclude each other (stock precludingHardpointSets, like the Ibis cargo bays).
        while (sets.arraySize < 3) sets.InsertArrayElementAtIndex(1);
        while (sets.arraySize > 3) sets.DeleteArrayElementAtIndex(sets.arraySize - 1);
        // Nick 2026-11-12 20:42: under stations removed completely, Kingpin (Rocket2_4Pod) removed from the top stations
        string[] topOpts = { "RocketPod1_single", "AGM1_single", "AAM3_single", "ECMPod1", "ATGM1_quad" };
        string[] underOpts = { "gun_12.7mm_pod", "gun_20mm_pod", "bomb_125_single" };
        var defs = new[] {
            new { name = "Left Stub", hp = "pylon_fuselage_L", sym = false, symName = "Stub Pylons (top)", opts = topOpts, pre = 3 },
            new { name = "Right Stub", hp = "pylon_fuselage_R", sym = true, symName = "Stub Pylons (top)", opts = topOpts, pre = 4 },
            new { name = "Left Stub (under)", hp = "pylon_under_L", sym = false, symName = "Stub Pylons (under)", opts = underOpts, pre = 1 },
            new { name = "Right Stub (under)", hp = "pylon_under_R", sym = true, symName = "Stub Pylons (under)", opts = underOpts, pre = 2 } };
        for (int i = 0; i < 2; i++)
        {
            var d = defs[i]; var p = sets.GetArrayElementAtIndex(i + 1);
            p.FindPropertyRelative("name").stringValue = d.name;
            p.FindPropertyRelative("SymmetryWithPrev").boolValue = d.sym;
            p.FindPropertyRelative("SymmetryName").stringValue = d.symName;
            p.FindPropertyRelative("precludingHardpointSets").arraySize = 0;
            var php = p.FindPropertyRelative("hardpoints"); php.arraySize = 1;
            php.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = root.Find(d.hp) ?? Find(root, d.hp);
            php.GetArrayElementAtIndex(0).FindPropertyRelative("part").objectReferenceValue = C(root, "AeroPart");
            var op = p.FindPropertyRelative("weaponOptions"); op.arraySize = d.opts.Length + 1; op.GetArrayElementAtIndex(0).objectReferenceValue = null;
            for (int k = 0; k < d.opts.Length; k++)
            {
                var m = d.opts[k] == "AGM1_single" ? Mount("UL2_AGM1_rail") : d.opts[k] == "AAM3_single" ? Mount("UL2_AAM3_rail") : AssetDatabase.LoadMainAssetAtPath(Placeholder(d.opts[k], "WeaponMount"));
                if (m == null) log.AppendLine("MISSING mount " + d.opts[k]);
                op.GetArrayElementAtIndex(k + 1).objectReferenceValue = m;
            }
        }
        Seat3Hardpoint(sets);
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
            Set(tu, "traverseRange", 5f); Set(tu, "minElevation", -5f); Set(tu, "maxElevation", 5f);   // Nick 20:44: 5 deg any direction
            // the gun housing rides on the barrel so it tilts with the gun (the part keeps only its collider)
            var hs = New("housing", br); hs.localPosition = P("gunmount_R") - P("gunPivot"); hs.localRotation = Quaternion.identity;
            hs.gameObject.AddComponent<MeshFilter>().sharedMesh = M["gunmount_R"]; hs.gameObject.AddComponent<MeshRenderer>().sharedMaterials = Mats("gunmount_R");
            Set(tu, "traverseRate", 40f); Set(tu, "elevationRate", 35f);
            // 0.15.29 (Nick 12:29): no magazine changes, one 300-round belt fired however the pilot wants
            foreach (var gn in go.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null && c.GetType().Name == "Gun"))
            { Set(gn, "magazineCapacity", 300); Set(gn, "magazines", 1); Set(gn, "reloadTime", 0f); log.AppendLine("side gun belt 300 rounds on " + gn.name); }
            var m = SaveMount(go, "UL2_sidegun", "turret_12.7mm_door", null, "12.7mm Gun (300 rounds)");
            var so = new SerializedObject(m);
            var amp = so.FindProperty("ammo"); if (amp != null) amp.intValue = 300;
            so.FindProperty("GearSafety").boolValue = false; so.FindProperty("GroundSafety").boolValue = false;
            so.FindProperty("drag").floatValue = 0.05f; so.FindProperty("emptyDrag").floatValue = 0.05f;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        }
        finally { Object.DestroyImmediate(go); }
        RailMount("AGM1_single", "UL2_AGM1_rail", -0.092f);
        RailMount("AAM3_single", "UL2_AAM3_rail", -0.076f);
    }
    // AGM-48 / IRM-S2 straight on the beefed-up built-in pylon (Nick 2026-11-12 21:35): the stock single mount without
    // its launch pylon, missile raised so its body sits on the pylon's rail. Same info, cost and stats as the stock mount.
    static void RailMount(string src, string key, float y)
    {
        var go = CopyPrefab(src, key);
        try
        {
            // the missile can sit under the stock pylon object: lift every missile out to the mount root first
            // (0.15.5 destroyed the pylon with the missile inside it, so nothing spawned - Nick 22:02)
            var ms = go.GetComponentsInChildren<MonoBehaviour>(true).Where(mm => mm.GetType().Name == "MountedMissile").Select(mm => mm.transform).Distinct().ToList();
            foreach (var mt in ms) { mt.SetParent(go.transform, true); var lp = mt.localPosition; mt.localPosition = new Vector3(lp.x, y, lp.z); }
            var py = go.transform.Find("pylon"); if (py != null) Object.DestroyImmediate(py.gameObject);
            log.AppendLine(key + ": " + ms.Count + " missile(s) kept");
            var srcM = AssetDatabase.LoadMainAssetAtPath(Placeholder(src, "WeaponMount"));
            string nm = new SerializedObject(srcM).FindProperty("mountName").stringValue;
            SaveMount(go, key, src, null, nm);
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
    static void Armor(string n, float pierceArmor, float pierceTol, float blastTol)
    {
        var ap = C(n == "" ? root : Need(n), "AeroPart");
        Set(ap, "armorProperties.pierceArmor", pierceArmor); Set(ap, "armorProperties.pierceTolerance", pierceTol); Set(ap, "armorProperties.blastTolerance", blastTol);
    }
    static void AeroAndDamage()
    {
        // ~680 kg empty with the gun; 60 kg fuel in the frame tank
        Part("", 120f, 0f, 0.05f, -1f);   // 0.15.9: drag areas roughly halved
        Part("cockpit_F", 75f, 0.4f, 0.04f, -1f); Part("nose", 10f, 0f, 0.025f, -1f);
        Part("cockpit_R", 40f, 0f, 0.04f, -1f);
        Part("fuselage", 60f, 0f, 0.03f, 60f);
        Part("intake", 15f, 0f, 0.05f, -1f);
        Part("mast", 40f, 0f, 0.04f, -1f); Part("rotorHead", 20f, 0f, 0.03f, -1f);
        Part("engine_core", 60f, 0f, 0.03f, -1f); Part("engine_L", 15f, 0f, 0.02f, -1f); Part("engine_R", 15f, 0f, 0.02f, -1f);
        Part("propHub", 15f, 0f, 0.02f, -1f);
        // 0.15.8: gun mount 25 -> 10 kg (it sits 0.30 m right of centre), the 15 kg moves to the centred cockpit_F
        Part("gunmount_R", 10f, 0f, 0.05f, -1f);
        Part("nacelle_L", 20f, 0f, 0.04f, -1f); Part("nacelle_R", 20f, 0f, 0.04f, -1f);
        Part("tailboom_L", 15f, 0f, 0.02f, -1f); Part("tailboom_R", 12f, 0f, 0.02f, -1f);
        // rotor: ~31 m2 of lifting blade for the gyro's low stall (blade aero is a fixed span; the head spins only visually)
        foreach (var s in new[] { "L", "R" })
        {
            // 0.15.9 (Nick 23:10: never lifts off; game aircraft need much more lift and less drag than real): blade area x1.6
            Part("wing1_" + s, 8f, 7f, 0.005f, -1f); Part("wing2_" + s, 8f, 10f, 0.005f, -1f); Part("wing3_" + s, 6f, 7f, 0.005f, -1f);
            Part("aileron_" + s, 2f, 3f, 0f, -1f);
            Part("hstab_" + s, 5f, 0.9f, 0.01f, -1f); Part("hstabTip_" + s, 3f, 0.5f, 0.01f, -1f);
            Part("elevator_" + s, 3f, 0.7f, 0f, -1f); Part("elevatorTip_" + s, 2f, 0.4f, 0f, -1f);
            foreach (var w in new[] { "hstab_", "hstabTip_", "elevator_", "elevatorTip_" }) Set(AP(w + s), "centerOfLift", Vector3.zero);
            // 0.15.8: rotor lift acts 0.40 m ahead of the blade parts (z -0.57), just behind the centre of mass (z -0.53),
            // like a gyro's rotor thrust line through the CG; at the part origins it pitched the nose down on the takeoff roll
            foreach (var w in new[] { "wing1_", "wing2_", "wing3_", "aileron_" }) Set(AP(w + s), "centerOfLift", new Vector3(0f, 0f, 0.40f));
        }
        // 0.15.8: bigger fin areas (Nick 22:53: unstable yaw past 60 km/h)
        Part("vstab1_L", 6f, 1.0f, 0.01f, -1f); Part("vstab1_R", 4f, 0.5f, 0.01f, -1f);
        Part("rudder_L", 3f, 0.5f, 0f, -1f); Part("rudder_R", 2f, 0.2f, 0f, -1f);
        // joints: each part to its structural parent.
        // 0.15.22 (Nick 04:05: a crash leaves it in one piece): the old strengths were 10x the Cricket's per kilogram
        // (an 8 kg blade on a 48 kN joint needs 600 g to come off). Now sized like the stock Cricket, about 20-35 g on
        // the mass each joint carries; control surfaces stay strong like stock; gear legs keep landing margin.
        J("cockpit_R", "COIN_PLACEHOLDER", 12000f, "fuselage", 8000f);
        J("cockpit_F", "COIN_PLACEHOLDER", 15000f, "cockpit_R", 8000f, "fuselage", 6000f);   // 0.15.5: every overlapping collider pair is jointed (stock joints switch their collision off), so nothing pushes apart at spawn
        J("nose", "cockpit_F", 3000f);
        J("fuselage", "COIN_PLACEHOLDER", 15000f);
        J("intake", "fuselage", 5000f, "cockpit_R", 3000f, "COIN_PLACEHOLDER", 3000f);
        J("mast", "fuselage", 25000f, "intake", 6000f, "COIN_PLACEHOLDER", 15000f, "cockpit_R", 8000f);   // carries head, blades and part of the engine
        J("rotorHead", "mast", 15000f, "fuselage", 8000f);
        J("engine_core", "mast", 15000f, "fuselage", 8000f, "COIN_PLACEHOLDER", 10000f);
        J("engine_L", "engine_core", 5000f); J("engine_R", "engine_core", 5000f);
        J("propHub", "engine_core", 6000f);
        J("gunmount_R", "COIN_PLACEHOLDER", 4000f, "cockpit_F", 2000f, "cockpit_R", 2000f);
        J("nacelle_L", "fuselage", 20000f, "COIN_PLACEHOLDER", 12000f); J("nacelle_R", "fuselage", 20000f, "COIN_PLACEHOLDER", 12000f);
        J("tailboom_L", "fuselage", 7000f, "mast", 4000f, "engine_core", 3000f, "COIN_PLACEHOLDER", 4000f); J("tailboom_R", "tailboom_L", 8000f, "fuselage", 5000f, "mast", 4000f);
        J("vstab1_L", "tailboom_R", 4000f, "tailboom_L", 2500f); J("vstab1_R", "vstab1_L", 2000f);
        J("rudder_L", "vstab1_L", 5000f); J("rudder_R", "vstab1_R", 5000f);
        foreach (var s in new[] { "L", "R" })
        {
            J("wing1_" + s, "rotorHead", 7000f); J("wing2_" + s, "wing1_" + s, 5000f); J("wing3_" + s, "wing2_" + s, 3000f);
            J("aileron_" + s, "wing3_" + s, 50000f);
            J("hstab_" + s, "tailboom_R", 3500f, "vstab1_L", 2000f); J("hstabTip_" + s, "hstab_" + s, 1500f);
            J("elevator_" + s, "hstab_" + s, 5000f); J("elevatorTip_" + s, "hstabTip_" + s, 5000f);
        }
        // damage effects the stock way: the Cricket's engine fire sat on its engine nacelles, which on the UL-2 are the
        // gear legs. Fire goes on the engine and the fuel tank (fuselage); breakup fragments on the tail too; crash impacts
        // damage the nose, cockpit, mast, rotor head, engine, tail and gear (stock impactDamage, threshold in g)
        var fireSrc = new SerializedObject(AP("nacelle_L")).FindProperty("damageEffects.Array.data[0].prefab").objectReferenceValue;
        var fragSrc = new SerializedObject(AP("nacelle_L")).FindProperty("disintegrationEffects.Array.data[0]").objectReferenceValue;
        foreach (var n in new[] { "nacelle_L", "nacelle_R" }) Size(AP(n), "damageEffects", 0);
        foreach (var n in new[] { "engine_core", "fuselage" })
        { Size(AP(n), "damageEffects", 1); Set(AP(n), "damageEffects.Array.data[0].prefab", fireSrc); Set(AP(n), "damageEffects.Array.data[0].threshold", 0f); }
        foreach (var n in new[] { "tailboom_L", "tailboom_R", "nose", "vstab1_L" }) ObjArray(AP(n), "disintegrationEffects", fragSrc);
        foreach (var n in new[] { "nose", "cockpit_F", "cockpit_R", "mast", "rotorHead", "engine_core", "tailboom_L", "tailboom_R", "nacelle_L", "nacelle_R", "wing1_L", "wing1_R" })
        { Set(AP(n), "impactDamage.threshold", 25f); Set(AP(n), "impactDamage.multiplier", 2f); }
        // 0.15.37 (Nick 23:17: too durable, the cockpit breaks and the rest stays intact): the armor values came over
        // unchanged from the Cricket, whose cockpit tub takes about a fifth of the damage of the rest (pierce tolerance
        // 30-50). The UL-2 is a fibreglass pod on a tube frame, so every part now takes 1.5-2x the Cricket's damage per hit
        // and the cockpit no more than the frame behind it. Damage per hit = (pierce - pierceArmor) / pierceTolerance
        // + blast / blastTolerance; a part comes off once its hit points reach 0.
        Armor("", 10f, 5f, 6f); Armor("cockpit_F", 8f, 6f, 6f); Armor("cockpit_R", 8f, 6f, 6f); Armor("nose", 10f, 4f, 3f);
        Armor("fuselage", 10f, 4f, 4f); Armor("intake", 10f, 4f, 3f); Armor("gunmount_R", 10f, 5f, 3f);
        Armor("mast", 10f, 5f, 3f); Armor("rotorHead", 10f, 5f, 3f);
        Armor("engine_core", 10f, 5f, 3f); Armor("engine_L", 10f, 4f, 3f); Armor("engine_R", 10f, 4f, 3f); Armor("propHub", 10f, 6f, 3f);
        Armor("nacelle_L", 10f, 4f, 4f); Armor("nacelle_R", 10f, 4f, 4f); Armor("tailboom_L", 10f, 3f, 3f); Armor("tailboom_R", 10f, 3f, 3f);
        Armor("vstab1_L", 10f, 3f, 3f); Armor("vstab1_R", 10f, 3f, 3f); Armor("rudder_L", 5f, 3f, 3f); Armor("rudder_R", 5f, 3f, 3f);
        foreach (var s in new[] { "L", "R" })
        {
            Armor("wing1_" + s, 10f, 4f, 5f); Armor("wing2_" + s, 10f, 4f, 3f); Armor("wing3_" + s, 10f, 3f, 2f); Armor("aileron_" + s, 5f, 3f, 3f);
            Armor("hstab_" + s, 10f, 3f, 3f); Armor("hstabTip_" + s, 10f, 3f, 3f); Armor("elevator_" + s, 10f, 3f, 3f); Armor("elevatorTip_" + s, 10f, 3f, 3f);
        }
        log.AppendLine("aero + joints set");
    }

    // 0.15.7 (Nick 22:35), restyled 0.15.20: rotor rpm on the HUD the way the Alkyon shows its wing angle. Own copy of the stock
    // COIN HUD extras plus the Alkyon gauge's label (same font/style), and an idle-to-full bar; the plugin fills it in.
    // ------------------------------------------------------------------ damage paper doll: copy of the Cricket status display with a
    // stylised UL-2 side view. UI/UL2_status.png is the outline, UI/UL2_status_<part>.png one full-frame fill per part (named exactly as
    // the part, which is how the stock StatusDisplay ties an image to a part), drawn in UI/order.txt order (same method as the P220).
    static GameObject StatusDisplayUL2()
    {
        string ui = MOD + "UI/", dst = ui + "UL2_StatusDisplay.prefab";
        string orderPath = System.IO.Path.GetFullPath(ui + "order.txt");
        if (!System.IO.File.Exists(orderPath)) { log.AppendLine("status display: no UI/order.txt"); return null; }
        var order = System.IO.File.ReadAllLines(orderPath).Where(l => l.Trim().Length > 0).Select(l => l.Trim()).ToArray();
        System.Func<string, Sprite> Spr = n =>
        {
            string path = ui + n + ".png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return null;
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false; ti.maxTextureSize = 1024; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        };
        AssetDatabase.DeleteAsset(dst);
        if (!AssetDatabase.CopyAsset(PH + "GameObject/StatusDisplay_COIN_PLACEHOLDER.prefab", dst)) { log.AppendLine("status display copy failed"); return null; }
        var go = PrefabUtility.LoadPrefabContents(dst);
        try
        {
            go.name = "UL2_StatusDisplay";
            const float S = 280f;
            var rt0 = (RectTransform)go.transform; rt0.sizeDelta = new Vector2(S, S);
            var bg = go.GetComponent<UnityEngine.UI.Image>(); bg.sprite = Spr("UL2_status");
            var sd = go.GetComponents<Component>().First(c => c != null && c.GetType().Name == "StatusDisplay");
            var so = new SerializedObject(sd);
            var thr = new Dictionary<string, float>();
            var arr = so.FindProperty("statusDisplays");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i); var im = e.FindPropertyRelative("partImage").objectReferenceValue as Component;
                if (im != null) thr[im.name] = e.FindPropertyRelative("redStatusThreshold").floatValue;
            }
            float dflt = thr.Count > 0 ? thr.Values.Average() : 0.5f;
            foreach (Transform ch in go.transform.Cast<Transform>().ToArray()) if (!ch.name.Contains("FIRE")) Object.DestroyImmediate(ch.gameObject);
            arr.arraySize = order.Length;
            for (int i = 0; i < order.Length; i++)
            {
                var n = order[i];
                var c = new GameObject(n, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                c.layer = go.layer; var rt = (RectTransform)c.transform; rt.SetParent(go.transform, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(S, S);
                rt.SetSiblingIndex(i);
                var im = c.GetComponent<UnityEngine.UI.Image>(); im.sprite = Spr("UL2_status_" + n); im.raycastTarget = false; im.color = new Color(1f, 1f, 0f, 1f);
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("partImage").objectReferenceValue = im;
                e.FindPropertyRelative("redStatusThreshold").floatValue = thr.ContainsKey(n) ? thr[n] : dflt;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            // engine fire bar on the engine (pixel centre in the 512 px frame)
            foreach (var f in go.transform.Cast<Transform>().Where(t => t.name.Contains("FIRE")).ToArray())
            {
                var rt = (RectTransform)f; rt.anchoredPosition = new Vector2((182f / 512f - 0.5f) * S, (0.5f - 200f / 512f) * S);
                rt.localScale = Vector3.one; rt.sizeDelta = new Vector2(30f / 512f * S, 18f / 512f * S); rt.SetAsLastSibling();
            }
            PrefabUtility.SaveAsPrefabAsset(go, dst);
            log.AppendLine("status display: " + order.Length + " parts, default red threshold " + dflt.ToString("F2"));
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(dst);
    }
    static GameObject HudExtrasUL2()
    {
        if (!AssetDatabase.IsValidFolder(MOD + "UI")) AssetDatabase.CreateFolder(MOD.TrimEnd('/'), "UI");
        string dst = MOD + "UI/UL2_HUDExtras.prefab";
        AssetDatabase.DeleteAsset(dst);
        if (!AssetDatabase.CopyAsset(PH + "GameObject/COIN_HUDExtras_PLACEHOLDER.prefab", dst)) { log.AppendLine("HUD extras: copy failed"); return null; }
        var go = PrefabUtility.LoadPrefabContents(dst);
        try
        {
            // 0.15.20 (Nick 03:43): the rotor gauge is a copy of the stock throttle gauge (same arc, bar, carrot and HUD styles),
            // a little right of the old bar. Carrot = rpm set by the axis or the assist, coloured bar + readout = actual rpm.
            var src = go.GetComponentsInChildren<Transform>(true).First(t => t.name == "throttleGauge");
            var g = Object.Instantiate(src.gameObject, go.transform, false); g.name = "UL2_rotorGauge";
            foreach (var c in g.GetComponents<Component>()) if (c != null && c.GetType().Name == "ThrottleGauge") Object.DestroyImmediate(c);
            var rt = (RectTransform)g.transform; rt.anchoredPosition = src.GetComponent<RectTransform>().anchoredPosition + new Vector2(140f, 0f);   // Nick 04:06: back near the old bar spot
            var piv = g.transform.Find("throttleReadingPivot");
            var piv2 = Object.Instantiate(piv.gameObject, g.transform, false); piv2.name = "readPivot";
            piv.name = "carrotPivot";
            foreach (var t in piv.GetComponentsInChildren<Transform>(true).Where(x => x.name == "throttleReading").ToList())
            {
                var bp = t.Find("barPointer"); if (bp != null) bp.SetParent(t.parent, true);   // the carrot sits under the reading in stock
                Object.DestroyImmediate(t.gameObject);
            }
            foreach (var t in piv2.transform.GetComponentsInChildren<Transform>(true).Where(x => x.name == "barPointer").ToList()) if (t != null) Object.DestroyImmediate(t.gameObject);
            var lab = g.transform.Find("throttleLabel"); var tmp = lab.GetComponent<TMPro.TextMeshProUGUI>(); if (tmp) tmp.text = "ROTOR";
            g.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(go, dst);
            log.AppendLine("HUD extras: rotor gauge (throttle style) added");
        }
        catch (System.Exception e) { log.AppendLine("HUD extras ERROR " + e.Message); }
        finally { PrefabUtility.UnloadPrefabContents(go); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(dst);
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
        so.FindProperty("takeoffSpeed").floatValue = 30f;          // 0.15.34: the AI rotates at 0.7x = 76 km/h (was 50)
        so.FindProperty("takeoffDistance").floatValue = 250f;
        so.FindProperty("minimumRadarAlt").floatValue = 40f;       // 0.15.34: AI cruise floor, low-level flyer
        so.FindProperty("verticalLanding").boolValue = false;
        so.FindProperty("turningRadius").floatValue = 300f;
        so.FindProperty("cornerSpeed").floatValue = 35f;
        so.FindProperty("approachSpeed").floatValue = 26f;
        so.FindProperty("landingSpeed").floatValue = 22f;
        so.FindProperty("shortLandingSpeed").floatValue = 20f;
        so.FindProperty("groundTurningRadius").floatValue = 10f;
        so.FindProperty("HUDExtras").objectReferenceValue = HudExtrasUL2();
        // 0.15.29 (Nick 12:29): own damage paper doll (UI/UL2_StatusDisplay.prefab, sprites from ui_doll/doll.py)
        var sdp = StatusDisplayUL2(); if (sdp != null) so.FindProperty("StatusDisplay").objectReferenceValue = sdp;
        so.FindProperty("DefaultFuelLevel").floatValue = 1f;
        so.FindProperty("cruiseThrottle").floatValue = 0.75f;
        var liv = so.FindProperty("liveries"); liv.arraySize = 3;
        string[] livName = { "Reservist Olive", "Stainless Steel", "Black Tiger" };
        string[] livPath = { LIVERY, LiveryPath("steel"), LiveryPath("tiger") };
        for (int i = 0; i < 3; i++)
        {
            var li = liv.GetArrayElementAtIndex(i);
            li.FindPropertyRelative("name").stringValue = livName[i];
            li.FindPropertyRelative("faction").objectReferenceValue = null;
            li.FindPropertyRelative("assetReference.m_AssetGUID").stringValue = AssetDatabase.AssetPathToGUID(livPath[i]);
            li.FindPropertyRelative("assetReference.m_SubObjectName").stringValue = "";
        }
        // six hardpoint sets now: side gun, left/right stub top, left/right stub under, seat 3 passenger
        var lo = so.FindProperty("loadouts");
        for (int i = 0; i < lo.arraySize; i++)
        {
            var w = lo.GetArrayElementAtIndex(i).FindPropertyRelative("weapons"); w.arraySize = 4;
            for (int k = 0; k < 4; k++) w.GetArrayElementAtIndex(k).objectReferenceValue = null;
            if (i == 0) continue;
            w.GetArrayElementAtIndex(0).objectReferenceValue = Mount("UL2_sidegun");
            var at = AssetDatabase.LoadMainAssetAtPath(Placeholder("ATGM1_quad", "WeaponMount"));
            w.GetArrayElementAtIndex(1).objectReferenceValue = at; w.GetArrayElementAtIndex(2).objectReferenceValue = at;
            w.GetArrayElementAtIndex(3).objectReferenceValue = Mount("UL2_s3_mg");
        }
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(par);

        var def = AssetDatabase.LoadMainAssetAtPath(DEF);
        so = new SerializedObject(def);
        so.FindProperty("jsonKey").stringValue = "UL2Airtrike";
        so.FindProperty("unitName").stringValue = "UL-2 Airtrike";
        var wt = so.FindProperty("WorkshopTag"); if (wt != null) wt.stringValue = "UL-2 Airtrike";
        so.FindProperty("code").stringValue = "UL-2";
        so.FindProperty("description").stringValue = "A light three-seat gyrocopter operated by Reserve Aviation units. Border patrol, armed reconnaissance, and light attack are all missions the UL-2 Airtrike can perform, but Reservists are reluctant to take it into enemy airspace where more sophisticated aircraft hunt. AT-145 racks and a forgiving flight envelope allow the gyrocopter to engage enemy light armor in close combat. A third seat allows for a dedicated spotter or shooter with multiple weapons options.";
        // own map and aircraft selection icon (Nick 04:20): white top-down silhouette, nose up, rotor at 35 deg passing under
        // the body, like the stock icons (art/scripts/icons)
        var mapIco = IconSprite(ART + "Icons/UL2_icon_map.png");
        if (mapIco != null) so.FindProperty("mapIcon").objectReferenceValue = mapIco; else log.AppendLine("NO MAP ICON");
        so.FindProperty("length").floatValue = 6.5f;
        so.FindProperty("width").floatValue = 9.2f;
        so.FindProperty("height").floatValue = 3.3f;
        so.FindProperty("value").floatValue = 0f;
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
        // medium hangars only (0.15.8, Nick 22:53: helipad spawns off for now, runway starts give repeatable takeoff tests)
        // 0.15.9 (Nick 23:14: no spawn at small airbases): every building the stock Cricket spawns from, revetments and
        // shelters included (shelter1 also holds a helipad hangar, which stays off)
        // 0.15.20 (Nick 03:48): helicopter spawns (stock helipads) back on
        var hk = new[] { "hangar_med", "revetment1", "shelter1", "Helipad" };
        var hs = so.FindProperty("hangars"); hs.arraySize = hk.Length;
        for (int i = 0; i < hk.Length; i++)
        {
            hs.GetArrayElementAtIndex(i).FindPropertyRelative("hangarUnitJsonKey").stringValue = hk[i];
            var hn = hs.GetArrayElementAtIndex(i).FindPropertyRelative("hangarNames"); hn.arraySize = 1; hn.GetArrayElementAtIndex(0).stringValue = hk[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(so.targetObject);
        string mi = MOD + "modinfo.json";
        System.IO.File.WriteAllText(mi, "{\n    \"displayName\": \"UL-2 Airtrike TEST BUILD\",\n    \"version\": \"0.15.39\"\n}\n");
        log.AppendLine("def, parameters, hangar op, modinfo written");
    }
}
