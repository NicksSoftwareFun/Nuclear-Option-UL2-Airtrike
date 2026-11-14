using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Seat 3 passenger kits (machine gunner, grenadier, spotter) as weapon mounts on a "Seat 3" hardpoint at the seat 3
// pelvis. Each kit is one skinned mesh (soldier + weapon + harness, bound at a reference pose baked in Blender) with
// flat bone transforms; the plugin poses the bones from a pose table, following the stock turret's traverse and
// elevation. The machine gun and launcher are stock door-gun turrets (copies) with exclusion cones forward and aft;
// the spotter is a turret-less mount like the stock ECM pod. Every value is a literal on purpose.
public static partial class UL2Builder
{
    const string S3 = TOOLS + "Seat3/";

    class S3Data
    {
        public Vector3[] v, n; public Vector2[] uv; public BoneWeight[] bw; public string[] bones;
        public Vector3[] rp; public Quaternion[] rr;
        public List<KeyValuePair<string, int[]>> subs = new List<KeyValuePair<string, int[]>>();
        public Dictionary<string, float[]> mats = new Dictionary<string, float[]>();
    }
    static string RS(BinaryReader r) { int n = r.ReadInt32(); return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n)); }

    static S3Data S3Load(string kind)
    {
        var d = new S3Data();
        using (var r = new BinaryReader(File.OpenRead(S3 + "UL2_s3_" + kind + "_mesh.bytes")))
        {
            int nv = r.ReadInt32();
            d.v = new Vector3[nv]; d.n = new Vector3[nv]; d.uv = new Vector2[nv]; d.bw = new BoneWeight[nv];
            for (int i = 0; i < nv; i++) d.v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            for (int i = 0; i < nv; i++) d.n[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            for (int i = 0; i < nv; i++) d.uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            for (int i = 0; i < nv; i++)
            {
                var b = new BoneWeight();
                b.boneIndex0 = r.ReadInt32(); b.weight0 = r.ReadSingle(); b.boneIndex1 = r.ReadInt32(); b.weight1 = r.ReadSingle();
                b.boneIndex2 = r.ReadInt32(); b.weight2 = r.ReadSingle(); b.boneIndex3 = r.ReadInt32(); b.weight3 = r.ReadSingle();
                d.bw[i] = b;
            }
            int nb = r.ReadInt32(); d.bones = new string[nb]; d.rp = new Vector3[nb]; d.rr = new Quaternion[nb];
            for (int i = 0; i < nb; i++) d.bones[i] = RS(r);
            for (int i = 0; i < nb; i++)
            {
                d.rp[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                d.rr[i] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            }
            int ns = r.ReadInt32();
            for (int s = 0; s < ns; s++)
            {
                var key = RS(r); int c = r.ReadInt32(); var t = new int[c];
                for (int i = 0; i < c; i++) t[i] = r.ReadInt32();
                d.subs.Add(new KeyValuePair<string, int[]>(key, t));
            }
            int nm = r.ReadInt32();
            for (int m = 0; m < nm; m++) { var key = RS(r); var f = new float[5]; for (int i = 0; i < 5; i++) f[i] = r.ReadSingle(); d.mats[key] = f; }
        }
        return d;
    }

    static Material S3Mat(string key, S3Data d)
    {
        if (key == "soldier") return AssetDatabase.LoadAssetAtPath<Material>(PH + "Material/soldier1_PLACEHOLDER.mat");
        float[] f;
        if (!d.mats.TryGetValue(key, out f)) f = new[] { 0.05f, 0.05f, 0.05f, 0.2f, 0f };
        // Blender colours are linear; the material colour is authored in gamma space
        var col = new Color(Mathf.LinearToGammaSpace(f[0]), Mathf.LinearToGammaSpace(f[1]), Mathf.LinearToGammaSpace(f[2]), 1f);
        return LitMat("UL2_s3_" + key, col, Mathf.Clamp01(f[3]), Mathf.Clamp01(f[4]));
    }

    static void S3Kit(GameObject go, string kind)
    {
        var d = S3Load(kind);
        var kit = New("UL2_s3kit_" + kind, go.transform); kit.localPosition = Vector3.zero; kit.localRotation = Quaternion.identity;
        var bones = new Transform[d.bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            bones[i] = New("b_" + d.bones[i], kit);
            bones[i].localPosition = d.rp[i]; bones[i].localRotation = d.rr[i];
        }
        string path = ART + "UL2_s3_" + kind + ".asset";
        var me = AssetDatabase.LoadAssetAtPath<Mesh>(path); bool fresh = me == null; if (fresh) me = new Mesh();
        me.Clear(); me.name = "UL2_s3_" + kind; me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        me.vertices = d.v; me.normals = d.n; me.uv = d.uv; me.boneWeights = d.bw;
        me.subMeshCount = d.subs.Count;
        for (int s = 0; s < d.subs.Count; s++) me.SetTriangles(d.subs[s].Value, s);
        me.bindposes = bones.Select(b => b.worldToLocalMatrix * kit.localToWorldMatrix).ToArray();
        me.RecalculateBounds(); me.RecalculateTangents();
        if (fresh) AssetDatabase.CreateAsset(me, path); else EditorUtility.SetDirty(me);
        var smr = kit.gameObject.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = me; smr.bones = bones; smr.rootBone = bones[System.Array.IndexOf(d.bones, "hips")];
        smr.updateWhenOffscreen = false; smr.localBounds = new Bounds(new Vector3(0f, 0.3f, 0f), new Vector3(2.4f, 2.0f, 2.4f));
        smr.sharedMaterials = d.subs.Select(s => S3Mat(s.Key, d)).ToArray();
        log.AppendLine("seat3 kit " + kind + ": " + d.v.Length + " verts, " + d.subs.Sum(s => s.Value.Length) / 3 + " tris, " + bones.Length + " bones, " + d.subs.Count + " materials");
    }

    // forward / aft exclusion cones (half angles) from the arcs the rig was validated on
    static void Seat3Mounts()
    {
        var kinds = new[] {
            new { k = "mg", src = "turret_12.7mm_door", name = "Machine Gunner", fwd = 34f, aft = 60f, mass = 115f },
            new { k = "gl", src = "turret_40mm_grenade", name = "Grenadier", fwd = 35f, aft = 80f, mass = 105f },
            new { k = "spot", src = "turret_12.7mm_door", name = "Spotter", fwd = 20f, aft = 50f, mass = 100f } };
        foreach (var K in kinds)
        {
            var go = CopyPrefab(K.src, "UL2_s3_" + K.k);
            try
            {
                var tr = go.transform.Find("traverse"); var br = tr.Find("barrel");
                tr.localPosition = new Vector3(0f, 0.45f, 0f); tr.localRotation = Quaternion.identity; tr.GetComponent<MeshFilter>().sharedMesh = null;
                br.localPosition = new Vector3(0f, 0f, 0.55f); br.localRotation = Quaternion.identity; br.GetComponent<MeshFilter>().sharedMesh = null;
                var mz = br.Find("muzzle"); mz.localPosition = new Vector3(0f, 0f, 0.5f); mz.localRotation = Quaternion.identity;
                var cf = go.transform.Find("firingConeNegative_up"); cf.name = "cone_forward"; cf.localPosition = new Vector3(0f, 0.45f, 0f); cf.localRotation = Quaternion.identity;
                var ca = go.transform.Find("firingConeNegative_down"); ca.name = "cone_aft"; ca.localPosition = new Vector3(0f, 0.45f, 0f); ca.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var tu = C(tr, "Turret"); var gun = C(br, "Gun");
                if (K.k == "spot")
                {
                    // binoculars only: no turret, no gun (the plugin turns him toward the aircraft's selected target)
                    Object.DestroyImmediate(tu); Object.DestroyImmediate(gun);
                    foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
                    foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true).Select(x => x.gameObject).ToList()) if (p != null) Object.DestroyImmediate(p);
                }
                else
                {
                    Set(tu, "traverseRange", 360f); Set(tu, "minElevation", -40f); Set(tu, "maxElevation", 20f);
                    Set(tu, "traverseRate", 100f); Set(tu, "elevationRate", 70f);
                    var so = new SerializedObject(tu); var fc = so.FindProperty("firingCones"); fc.arraySize = 2;
                    fc.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = cf;
                    fc.GetArrayElementAtIndex(0).FindPropertyRelative("coneAngle").floatValue = K.fwd;
                    fc.GetArrayElementAtIndex(0).FindPropertyRelative("exclusion").boolValue = true;
                    fc.GetArrayElementAtIndex(1).FindPropertyRelative("transform").objectReferenceValue = ca;
                    fc.GetArrayElementAtIndex(1).FindPropertyRelative("coneAngle").floatValue = K.aft;
                    fc.GetArrayElementAtIndex(1).FindPropertyRelative("exclusion").boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    // Nick 20:20: MG 75-round belts, 5 s reload, 2.1 km range (own weapon info so it is its own station); GL 6-round
                    // magazine, 7 s reload, a third of the Ibis 40 mm turret's 340 rpm
                    if (K.k == "mg") { Set(gun, "fireRate", 650f); Set(gun, "magazineCapacity", 75); Set(gun, "magazines", 6); Set(gun, "reloadTime", 5f); Set(gun, "bulletSpread", 2.5f); Set(gun, "recoilImpulse", 25f); Set(gun, "info", MgInfo()); Set(gun, "tracerRatio", 3); /* every 3rd round a tracer (Nick 04:51; stock 12.7 door gun is 5) */ }
                    else { Set(gun, "fireRate", 113f); Set(gun, "magazineCapacity", 6); Set(gun, "magazines", 6); Set(gun, "reloadTime", 7f); Set(gun, "info", GlInfo(new SerializedObject(gun).FindProperty("info").objectReferenceValue)); }
                }
                S3Kit(go, K.k);
                var m = SaveMount(go, "UL2_s3_" + K.k, K.k == "spot" ? "ECMPod1" : K.src, null, K.name);
                var ms = new SerializedObject(m);
                ms.FindProperty("GearSafety").boolValue = false; ms.FindProperty("GroundSafety").boolValue = false;
                ms.FindProperty("emptyMass").floatValue = K.mass; ms.FindProperty("drag").floatValue = 0.03f; ms.FindProperty("emptyDrag").floatValue = 0.03f;
                ms.FindProperty("emptyCost").floatValue = 0.02f;
                if (K.k == "mg") { ms.FindProperty("ammo").intValue = 450; ms.FindProperty("info").objectReferenceValue = MgInfo(); }
                if (K.k == "gl") { ms.FindProperty("ammo").intValue = 36; ms.FindProperty("info").objectReferenceValue = GlInfo(null); }
                if (K.k == "spot")
                {
                    ms.FindProperty("info").objectReferenceValue = SpotInfo(); ms.FindProperty("ammo").intValue = 0;   // info only for the loadout screen icon/text; no weapon, so no station
                    foreach (var f in new[] { "turret", "countermeasure", "radar", "missileBay" }) { var p = ms.FindProperty(f); if (p != null) p.boolValue = false; }
                }
                ms.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
            }
            catch (System.Exception ex) { log.AppendLine("SEAT3 " + K.k + " ERROR " + ex); }
            finally { Object.DestroyImmediate(go); }
        }
    }

    // own copies of the stock 40 mm grenade info and a loadout-screen-only spotter info, so the seat 3 options get their own
    // weapon icons (Nick 04:20) without touching the stock Ibis assets
    static Object GlInfo(Object src)
    {
        string path = MNT + "UL2_gl40_info.asset";
        var m = AssetDatabase.LoadMainAssetAtPath(path);
        if (m == null) { AssetDatabase.CopyAsset(src != null ? AssetDatabase.GetAssetPath(src) : PH + "MonoBehaviour/Grenade_40mm_PLACEHOLDER.asset", path); AssetDatabase.ImportAsset(path); m = AssetDatabase.LoadMainAssetAtPath(path); }
        var so = new SerializedObject(m);
        so.FindProperty("weaponName").stringValue = "40 mm Grenadier";
        so.FindProperty("shortName").stringValue = "40mm GL";
        so.FindProperty("description").stringValue = "A third seat grenadier with a six-shot 40 mm grenade launcher with HEDP ammo.";
        so.FindProperty("weaponIcon").objectReferenceValue = UL2Builder.IconSprite(ART + "Icons/UL2_icon_s3_gl.png");
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        return m;
    }
    static Object SpotInfo()
    {
        string path = MNT + "UL2_spot_info.asset";
        var m = AssetDatabase.LoadMainAssetAtPath(path);
        if (m == null) { AssetDatabase.CopyAsset(PH + "MonoBehaviour/Gun12.7mm_PLACEHOLDER.asset", path); AssetDatabase.ImportAsset(path); m = AssetDatabase.LoadMainAssetAtPath(path); }
        var so = new SerializedObject(m);
        so.FindProperty("weaponName").stringValue = "Spotter";
        so.FindProperty("shortName").stringValue = "SPOT";
        so.FindProperty("description").stringValue = "A spotter with long range binoculars. Raises the gyrocopter's visual detection range to 10 km.";
        so.FindProperty("targetRequirements.maxRange").floatValue = 10000f;
        so.FindProperty("pierceDamage").floatValue = 0f; so.FindProperty("blastDamage").floatValue = 0f;
        so.FindProperty("effectiveness.antiAir").floatValue = 0f; so.FindProperty("effectiveness.antiSurface").floatValue = 0f;
        so.FindProperty("costPerRound").floatValue = 0f; so.FindProperty("massPerRound").floatValue = 0f;
        so.FindProperty("weaponIcon").objectReferenceValue = UL2Builder.IconSprite(ART + "Icons/UL2_icon_s3_spot.png");
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        return m;
    }

    // 0.15.37 (Nick's names page): loadout-screen text for the seat 3 ECM pod. The stock pod mount carries no info; the
    // pod has no Weapon, so this info only feeds the loadout screen. No icon, so the screen shows just the text.
    static Object EcmInfo()
    {
        string path = MNT + "UL2_ecm_info.asset";
        var m = AssetDatabase.LoadMainAssetAtPath(path);
        if (m == null) { AssetDatabase.CopyAsset(PH + "MonoBehaviour/JammingPod1_PLACEHOLDER.asset", path); AssetDatabase.ImportAsset(path); m = AssetDatabase.LoadMainAssetAtPath(path); }
        var so = new SerializedObject(m);
        so.FindProperty("weaponName").stringValue = "Expedient ECM Pod";
        so.FindProperty("shortName").stringValue = "ECM POD";
        so.FindProperty("description").stringValue = "An enterprising copilot didn't like the idea of giving up half his armament for a defensive ECM pod, so he tricked the aircraft into thinking a stolen pod strapped to the third seat was a \"center pylon\" and connected it to the engine's alternator. Don't get cocky out there.";
        so.FindProperty("weaponIcon").objectReferenceValue = null;
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        return m;
    }

    static Object MgInfo()
    {
        string path = MNT + "UL2_mg338_info.asset";
        var m = AssetDatabase.LoadMainAssetAtPath(path);
        if (m == null) { AssetDatabase.CopyAsset(PH + "MonoBehaviour/Gun12.7mm_PLACEHOLDER.asset", path); AssetDatabase.ImportAsset(path); m = AssetDatabase.LoadMainAssetAtPath(path); }
        var so = new SerializedObject(m);
        so.FindProperty("targetRequirements.maxRange").floatValue = 2100f;
        // .338 Lapua Magnum round (Nick 20:24): faster and slightly more penetrating than 12.7, less damage per hit
        so.FindProperty("muzzleVelocity").floatValue = 950f; so.FindProperty("maxSpeed").floatValue = 950f;
        so.FindProperty("dragCoef").floatValue = 0.16f; so.FindProperty("pierceDamage").floatValue = 150f;   // 0.15.36 (Nick 22:44: kills too slowly): 120 -> 150, near the 12.7 (160); armour tier stays 3.3 (12.7 is 3)
        so.FindProperty("armorTierEffectiveness").floatValue = 3.3f; so.FindProperty("massPerRound").floatValue = 0.045f;
        so.FindProperty("costPerRound").floatValue = 0.0000025f;
        so.FindProperty("effectiveness.antiAir").floatValue = 0.10f; so.FindProperty("effectiveness.antiSurface").floatValue = 0.10f;
        so.FindProperty("weaponName").stringValue = ".338 LM Machine Gun";
        so.FindProperty("shortName").stringValue = "MG-338";
        so.FindProperty("weaponIcon").objectReferenceValue = UL2Builder.IconSprite(ART + "Icons/UL2_icon_s3_mg.png");
        so.FindProperty("description").stringValue = "A third seat machine gunner with a belt-fed .338 Lapua Magnum machine gun with lightweight polymer case ammunition.";
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        return m;
    }
    // the "Seat 3" hardpoint set at the seat 3 pelvis, on the seat part so the passenger leaves with it if it breaks off
    static void Seat3Hardpoint(SerializedProperty sets)
    {
        var seat = Need("intake");
        var hp = Find(root, "seat3_hp") ?? New("seat3_hp", seat);
        hp.position = P("gunner"); hp.rotation = Quaternion.identity;
        sets.arraySize = 4;
        var p = sets.GetArrayElementAtIndex(3);
        p.FindPropertyRelative("name").stringValue = "Third Seat";
        p.FindPropertyRelative("SymmetryWithPrev").boolValue = false;
        p.FindPropertyRelative("SymmetryName").stringValue = "";
        p.FindPropertyRelative("precludingHardpointSets").arraySize = 0;
        var php = p.FindPropertyRelative("hardpoints"); php.arraySize = 1;
        php.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = hp;
        php.GetArrayElementAtIndex(0).FindPropertyRelative("part").objectReferenceValue = C(seat, "AeroPart");
        var op = p.FindPropertyRelative("weaponOptions"); op.arraySize = 6;
        op.GetArrayElementAtIndex(0).objectReferenceValue = null;
        op.GetArrayElementAtIndex(1).objectReferenceValue = Mount("UL2_s3_mg");
        op.GetArrayElementAtIndex(2).objectReferenceValue = Mount("UL2_s3_gl");
        op.GetArrayElementAtIndex(3).objectReferenceValue = Mount("UL2_s3_spot");
        op.GetArrayElementAtIndex(4).objectReferenceValue = Mount("UL2_s3_ecm");
        op.GetArrayElementAtIndex(5).objectReferenceValue = Mount("UL2_s3_xbomb");   // 0.15.38 event content
    }

    // 0.15.33 (Nick 21:30): the stock ECM jamming pod carried in seat 3. The pod stands nose up, leaned 12 deg back
    // against the mast with its mounting face aft, foot on a rubber chock on a tray across the frame rails, held by two
    // 6 inch ratchet straps (lower one round the seat back, upper one to D-rings on mast clamps). Two multi-pin cables
    // run from the pod's pylon face along the left cockpit edge (two zip ties) into receptacles behind the gunner MFD.
    // Kit mesh: ecm_fit/kitdump.py (seat3_hp local, Unity axes). Pod pose from the same fit (RotX -102, origin below).
    static void Seat3Ecm()
    {
        var go = CopyPrefab("ECMPod1", "UL2_s3_ecm");
        try
        {
            var rt = go.transform;
            var mf0 = rt.GetComponent<MeshFilter>(); var mr0 = rt.GetComponent<MeshRenderer>();
            var pod = New("UL2_ecmpod", rt); pod.localPosition = new Vector3(0f, 0.508f, -0.094f); pod.localRotation = Quaternion.Euler(-102f, 0f, 0f);
            var pmf = pod.gameObject.AddComponent<MeshFilter>(); pmf.sharedMesh = mf0.sharedMesh;
            var pmr = pod.gameObject.AddComponent<MeshRenderer>(); pmr.sharedMaterials = mr0.sharedMaterials;
            // anything on the mount that pointed at the root renderer / filter now points at the posed pod
            int moved = 0;
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c == mf0 || c == mr0) continue;
                var so = new SerializedObject(c); var it = so.GetIterator(); bool ch = false;
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        if (it.objectReferenceValue == mr0) { it.objectReferenceValue = pmr; ch = true; moved++; }
                        else if (it.objectReferenceValue == mf0) { it.objectReferenceValue = pmf; ch = true; moved++; }
                    }
                if (ch) so.ApplyModifiedPropertiesWithoutUndo();
            }
            Object.DestroyImmediate(mr0); Object.DestroyImmediate(mf0);
            var bc = rt.GetComponent<BoxCollider>();
            if (bc != null) { bc.center = new Vector3(0f, 0.497f, 0.143f); bc.size = new Vector3(0.24f, 1.99f, 0.85f); }
            // straps, tray, chock, D-rings, mast clamps, cables, connectors and zip ties: one static mesh
            var kit = New("UL2_ecmkit", rt); kit.localPosition = Vector3.zero; kit.localRotation = Quaternion.identity;
            var kv = EcmKitMesh(); 
            kit.gameObject.AddComponent<MeshFilter>().sharedMesh = kv.Key;
            kit.gameObject.AddComponent<MeshRenderer>().sharedMaterials = kv.Value;
            log.AppendLine("seat3 ecm: pod posed, " + moved + " references moved, kit " + kv.Key.vertexCount + " verts, " + kv.Value.Length + " materials");
            var m = SaveMount(go, "UL2_s3_ecm", "ECMPod1", null, "Expedient ECM Pod");
            var ms = new SerializedObject(m);
            ms.FindProperty("emptyMass").floatValue = 162f;   // stock pod 150 kg + straps, tray and cables
            ms.FindProperty("info").objectReferenceValue = EcmInfo();   // 0.15.37: own loadout text (the stock pod has none, so the screen showed the aircraft text)
            ms.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        }
        catch (System.Exception ex) { log.AppendLine("SEAT3 ecm ERROR " + ex); }
        finally { Object.DestroyImmediate(go); }
    }
    // 0.15.38 (Nick 23:37), 0.15.39 (Nick 00:03: just the warhead can, wedged and strapped): event content (a mount flagged isEventContent, hidden unless the mission allows event content).
    // The stock GPO-N 1.5kt bomb strapped nose-down in seat 3. Firing it spawns the bomb as usual (it costs a warhead like
    // the stock bomb, its info is nuclear), but the plugin keeps it on the seat and plays a timer tick; the copy's own fuse
    // does the rest: arm delay 6 s and an altitude fuse so high that it goes off the moment it arms. It stays intangible
    // (tangible delay 30 s) so it never strikes its own aircraft, and its parachute never opens (open delay 999 s).
    // AI never picks it (effectiveness 0).
    const string XB_NAME = "Expedient 1.5kt Bomb";
    const string XB_DESC = "Volunteers needed. Single men only. Over 40 preferred.";
    // can pose in seat3_hp space: base centre on the blocks, axis leaned 12 deg back (toward the mast)
    // 0.15.42 (Nick 00:43: it clipped the seat 3 cushion): slid 0.16 m forward and leaned 19 deg back (was 12). Clearance
    // scan against every aircraft mesh: 4.9 cm to the seat back top edge, more everywhere else (cushion, cockpit_F, mast)
    static readonly Vector3 XB_BASE = new Vector3(0f, -0.44f, 0.29f);
    static readonly Vector3 XB_AX = new Vector3(0f, 0.94552f, -0.32557f);   // 19 deg back from vertical
    static readonly Vector3 XB_FW = new Vector3(0f, 0.32557f, 0.94552f);
    class MB
    {
        public List<Vector3> v = new List<Vector3>(); public List<Vector3> n = new List<Vector3>(); public List<List<int>> t = new List<List<int>>();
        public MB(int subs) { for (int i = 0; i < subs; i++) t.Add(new List<int>()); }
        public void Quad(int sm, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var nn = Vector3.Cross(b - a, d - a).normalized; int k = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d); n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn);
            t[sm].AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
        }
        // ring band (smooth normals) from z0 to z1 at radius r, in a frame (o, x, y, z)
        public void Tube(int sm, Vector3 o, Vector3 X, Vector3 Y, Vector3 Z, float r, float z0, float z1, int seg)
        {
            int k = v.Count;
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; var d = X * Mathf.Cos(a) + Y * Mathf.Sin(a); v.Add(o + d * r + Z * z0); n.Add(d); v.Add(o + d * r + Z * z1); n.Add(d); }
            for (int i = 0; i < seg; i++) { int q = k + i * 2; t[sm].AddRange(new[] { q, q + 3, q + 1, q, q + 2, q + 3 }); }
        }
        public void Disc(int sm, Vector3 o, Vector3 X, Vector3 Y, Vector3 Z, float r, float z, bool up, int seg)
        {
            int c = v.Count; var nz = up ? Z : -Z; v.Add(o + Z * z); n.Add(nz);
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; v.Add(o + (X * Mathf.Cos(a) + Y * Mathf.Sin(a)) * r + Z * z); n.Add(nz); }
            for (int i = 0; i < seg; i++) { if (up) t[sm].AddRange(new[] { c, c + 2 + i, c + 1 + i }); else t[sm].AddRange(new[] { c, c + 1 + i, c + 2 + i }); }
        }
        // a box from p0 to p1 (length axis), width w along wdir, thickness h
        public void Strip(int sm, Vector3 p0, Vector3 p1, Vector3 wdir, float w, float h)
        {
            var L = p1 - p0; var W = Vector3.ProjectOnPlane(wdir, L.normalized).normalized * (w * 0.5f); var H = Vector3.Cross(L.normalized, W.normalized) * (h * 0.5f);
            Box(sm, p0, L, W, H);
        }
        public void Box(int sm, Vector3 p0, Vector3 L, Vector3 W, Vector3 H)
        {
            Vector3 a = p0 - W - H, b = p0 + W - H, c = p0 + W + H, d = p0 - W + H;
            Quad(sm, a, b, b + L, a + L); Quad(sm, b, c, c + L, b + L); Quad(sm, c, d, d + L, c + L); Quad(sm, d, a, a + L, d + L);
            Quad(sm, a, d, c, b); Quad(sm, a + L, b + L, c + L, d + L);
        }
        public Mesh Make(string name)
        {
            var m = new Mesh(); m.name = name; m.SetVertices(v); m.SetNormals(n); m.subMeshCount = t.Count;
            for (int i = 0; i < t.Count; i++) m.SetTriangles(t[i], i);
            m.RecalculateBounds(); return m;
        }
    }
    static Mesh SaveMesh(Mesh m, string path)
    {
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old == null) { AssetDatabase.CreateAsset(m, path); return m; }
        old.Clear(); old.SetVertices(new List<Vector3>(m.vertices)); old.SetNormals(new List<Vector3>(m.normals)); old.subMeshCount = m.subMeshCount;
        for (int i = 0; i < m.subMeshCount; i++) old.SetTriangles(m.GetTriangles(i), i);
        old.RecalculateBounds(); EditorUtility.SetDirty(old); return old;
    }
    // the warhead can: the GPO-N centre body only (0.37 m across), 1.30 m long, with a slim lip and a small flat end
    // cap at each end and a yellow band; matte mid-grey like the stock weapon bodies. Own mesh (local z = can axis).
    static KeyValuePair<Mesh, Material[]> XbCan()
    {
        var mb = new MB(4); Vector3 o = Vector3.zero, X = Vector3.right, Y = Vector3.up, Z = Vector3.forward; int sg = 32;
        mb.Tube(0, o, X, Y, Z, 0.185f, -0.63f, 0.63f, sg);
        foreach (var sgn in new[] { -1f, 1f })
        {
            float e = 0.63f * sgn, f = 0.65f * sgn, g = 0.68f * sgn;
            mb.Tube(2, o, X, Y, Z, 0.190f, Mathf.Min(e, f), Mathf.Max(e, f), sg);              // lip ring
            mb.Disc(2, o, X, Y, Z, 0.190f, f, sgn > 0, sg);
            mb.Tube(2, o, X, Y, Z, 0.165f, Mathf.Min(f, g), Mathf.Max(f, g), sg);              // end cap
            mb.Disc(2, o, X, Y, Z, 0.165f, g, sgn > 0, sg);
            for (int k = 0; k < 6; k++)                                                        // cap bolts
            { float a = k * Mathf.PI / 3f; var p = (X * Mathf.Cos(a) + Y * Mathf.Sin(a)) * 0.13f + Z * g; mb.Box(2, p, Z * 0.012f * sgn, X * 0.012f, Y * 0.012f); }
        }
        mb.Tube(1, o, X, Y, Z, 0.1865f, 0.53f, 0.59f, sg);   // yellow band (above the upper strap)
        // 0.15.41 (Nick 00:30): radiation trefoil on the left and right sides, between the straps: black on a yellow disc,
        // laid onto the curved skin (centre disc r, blades 60 deg wide from 1.5r to 5r, one blade pointing up the can)
        foreach (var th in new[] { 0f, Mathf.PI })
        {
            float zc = 0.205f, R1 = 0.1875f, R2 = 0.1890f, r = 0.018f;
            System.Func<float, float, float, Vector3> P = (a, b, R) => { float q = th + a / R; return (X * Mathf.Cos(q) + Y * Mathf.Sin(q)) * R + Z * (zc + b); };
            System.Func<float, float, Vector3> N = (a, R) => { float q = th + a / R; return X * Mathf.Cos(q) + Y * Mathf.Sin(q); };
            // flip the 2D 'a' axis on the far side so both trefoils read the same way up
            float fl = th == 0f ? 1f : 1f;
            // a ring sector as a fine (angle x radius) grid so it lies on the curved skin instead of cutting chords under it
            System.Action<int, float, float, float, float, float> sector = (sm, R, r0, r1, f0, f1) =>
            {
                int na = 24, nr = 8, k = mb.v.Count;
                for (int i = 0; i <= na; i++) for (int j = 0; j <= nr; j++)
                {
                    float a = Mathf.Lerp(f0, f1, i / (float)na), rr = Mathf.Lerp(r0, r1, j / (float)nr);
                    var q = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr; mb.v.Add(P(q.x * fl, q.y, R)); mb.n.Add(N(q.x * fl, R));
                }
                for (int i = 0; i < na; i++) for (int j = 0; j < nr; j++)
                {
                    int a0 = k + i * (nr + 1) + j, a1 = a0 + nr + 1;   // (i,j) (i,j+1) (i+1,j+1) (i+1,j): CCW in the decal plane
                    mb.t[sm].AddRange(new[] { a0, a0 + 1, a1 + 1, a0, a1 + 1, a1 });
                }
            };
            sector(1, R1, 0f, 5.9f * r, 0f, Mathf.PI * 2f);                         // yellow disc
            sector(3, R2, 0f, r, 0f, Mathf.PI * 2f);                                // centre dot
            foreach (var bl in new[] { 90f, 210f, 330f })                           // blades
                sector(3, R2, 1.5f * r, 5f * r, (bl - 30f) * Mathf.Deg2Rad, (bl + 30f) * Mathf.Deg2Rad);
        }
        var mesh = SaveMesh(mb.Make("UL2_xb_can"), ART + "UL2_xb_can.asset");
        return new KeyValuePair<Mesh, Material[]>(mesh, new[] {
            LitMat("UL2_xb_body", new Color32(84, 86, 84, 255), 0.45f, 0f),
            LitMat("UL2_xb_yellow", new Color32(196, 160, 40, 255), 0.4f, 0f),
            LitMat("UL2_xb_cap", new Color32(66, 68, 67, 255), 0.4f, 0.3f),
            LitMat("UL2_xb_black", new Color32(22, 22, 22, 255), 0.35f, 0f) });
    }
    // 0.15.40 (Nick 00:13): the ECM pod's tie-down, sized for the can: tray clamped across the frame side rails with a
    // rubber chock, two 6 in olive webbing ratchet straps pulled taut round the can front (straps.py ported), the lower
    // one also round the seat back to hooks on the seat back frame edges, the upper one to D-rings on a mast clamp
    const float XW = 0.152f, XT = 0.005f;
    static List<Vector2> Hull2(List<Vector2> pts)
    {
        var P = pts.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList(); if (P.Count < 3) return P;
        System.Func<Vector2, Vector2, Vector2, float> cr = (o, a, b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var lo = new List<Vector2>(); foreach (var p in P) { while (lo.Count >= 2 && cr(lo[lo.Count - 2], lo[lo.Count - 1], p) <= 0) lo.RemoveAt(lo.Count - 1); lo.Add(p); }
        var up = new List<Vector2>(); for (int i = P.Count - 1; i >= 0; i--) { var p = P[i]; while (up.Count >= 2 && cr(up[up.Count - 2], up[up.Count - 1], p) <= 0) up.RemoveAt(up.Count - 1); up.Add(p); }
        lo.RemoveAt(lo.Count - 1); up.RemoveAt(up.Count - 1); lo.AddRange(up); return lo;
    }
    static void BoxC(MB mb, int sm, Vector3 c, Vector3 a, Vector3 b, Vector3 d)   // centre and full edge vectors
    { if (Vector3.Dot(Vector3.Cross(a, b), d) < 0f) d = -d; mb.Box(sm, c - a * 0.5f, a, b * 0.5f, d * 0.5f); }   // keep the box right-handed (faces out)
    static Vector3 OnPlane(Vector3 p, Vector3 c, Vector3 ax) { p.y = c.y - ((p.x - c.x) * ax.x + (p.z - c.z) * ax.z) / ax.y; return p; }
    static void XbStrap(MB mb, Vector3 c, Vector3 ax, Vector3 fwd, Vector3[] anchors, Vector3[] extra)
    {
        var f = (fwd - ax * Vector3.Dot(fwd, ax)).normalized; var u = Vector3.right; u = (u - ax * Vector3.Dot(u, ax) - f * Vector3.Dot(u, f)).normalized;
        for (int k = 0; k < anchors.Length; k++) anchors[k] = OnPlane(anchors[k], c, ax);
        for (int k = 0; k < extra.Length; k++) extra[k] = OnPlane(extra[k], c, ax);
        System.Func<Vector3, Vector2> to2 = p => new Vector2((float)System.Math.Round(Vector3.Dot(p - c, u), 4), (float)System.Math.Round(Vector3.Dot(p - c, f), 4));
        var sec = new List<Vector2>();
        for (int i = 0; i < 48; i++) { float a = i * Mathf.PI * 2f / 48f; sec.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.190f); }
        sec.AddRange(extra.Select(to2));
        Vector2 A = to2(anchors[0]), B = to2(anchors[1]); sec.Add(A); sec.Add(B);
        var H = Hull2(sec); int ia = H.IndexOf(A), ib = H.IndexOf(B);
        var ch1 = new List<Vector2>(); for (int k = 0; k <= ((ib - ia) % H.Count + H.Count) % H.Count; k++) ch1.Add(H[(ia + k) % H.Count]);
        var ch2 = new List<Vector2>(); for (int k = 0; k <= ((ia - ib) % H.Count + H.Count) % H.Count; k++) ch2.Add(H[((ia - k) % H.Count + H.Count) % H.Count]);
        var ch = ch1.Max(p => p.y) >= ch2.Max(p => p.y) ? ch1 : ch2;   // the run round the can front
        var P = ch.Select(q => c + u * q.x + f * q.y).ToList();
        Vector3[] prev = null;
        for (int i = 0; i < P.Count; i++)
        {
            var t = (P[Mathf.Min(i + 1, P.Count - 1)] - P[Mathf.Max(i - 1, 0)]).normalized; var n = Vector3.Cross(ax, t).normalized;
            if (Vector3.Dot(P[i] - c, n) < 0 && i > 0 && i < P.Count - 1) n = -n;
            var vs = new[] { P[i] - ax * XW * 0.5f, P[i] - ax * XW * 0.5f + n * XT, P[i] + ax * XW * 0.5f, P[i] + ax * XW * 0.5f + n * XT };
            if (prev != null) { mb.Quad(0, prev[0], vs[0], vs[2], prev[2]); mb.Quad(0, prev[1], prev[3], vs[3], vs[1]); mb.Quad(0, prev[0], prev[1], vs[1], vs[0]); mb.Quad(0, prev[2], vs[2], vs[3], prev[3]); }
            prev = vs;
        }
        // hooks: a flat plate at each end biting on its anchor bar / D-ring
        for (int k = 0; k < 2; k++)
        {
            var end = k == 0 ? P[0] : P[P.Count - 1]; var nxt = k == 0 ? P[1] : P[P.Count - 2];
            var t = (end - nxt).normalized; var n = Vector3.Cross(ax, t).normalized;
            BoxC(mb, 1, end - t * 0.03f, t * 0.06f, ax * (XW * 0.75f), n * 0.008f);
            BoxC(mb, 1, end + t * 0.012f + n * 0.012f, t * 0.012f, ax * (XW * 0.6f), n * 0.03f);
            BoxC(mb, 1, anchors[k] + t * 0.012f, ax * 0.12f, t * 0.014f, n * 0.014f);   // anchor bar / D-ring bar
        }
        // red ratchet block on the right tail, part way along
        {
            int bi = P.Count - 2; float bl = -1f;   // longest run on the right side of the can
            for (int i = 0; i < P.Count - 1; i++) { if (Vector3.Dot(P[i] - c, u) < 0.15f || Vector3.Dot(P[i + 1] - c, u) < 0.15f) continue; float L = (P[i + 1] - P[i]).magnitude; if (L > bl) { bl = L; bi = i; } }
            var a0 = P[bi + 1]; var b0 = P[bi]; var p = Vector3.Lerp(a0, b0, 0.5f); var t = (b0 - a0).normalized; var n = Vector3.Cross(ax, t).normalized;
            if (Vector3.Dot(p - c, n) < 0) n = -n;
            BoxC(mb, 2, p + n * 0.025f, t * 0.17f, ax * (XW + 0.03f), n * 0.035f);
            foreach (var sg in new[] { -1f, 1f }) BoxC(mb, 1, p + n * 0.035f + ax * sg * (XW * 0.5f + 0.018f), t * 0.15f, ax * 0.008f, n * 0.06f);
            BoxC(mb, 2, p + n * 0.06f + t * 0.05f, t * 0.13f, ax * 0.14f, n * 0.015f);   // handle
        }
    }
    static KeyValuePair<Mesh, Material[]> XbKit()
    {
        var mb = new MB(5); var A = XB_AX; var F = XB_FW; var X = Vector3.right; var B = XB_BASE; var Y = Vector3.up; var Zf = Vector3.forward;
        // tray across the frame side rails (U clamps at both rails, front and back) and the rubber chock on it
        BoxC(mb, 3, new Vector3(0f, -0.5165f, B.z + 0.02f), X * 0.66f, Y * 0.013f, Zf * 0.40f);
        foreach (var sx in new[] { -1f, 1f }) foreach (var zz in new[] { -0.16f, 0.20f }) BoxC(mb, 3, new Vector3(sx * 0.30f, -0.535f, B.z + zz), X * 0.05f, Y * 0.045f, Zf * 0.03f);
        {   // chock: flat on the tray, top sloped to meet the leaned can foot
            float z0 = B.z - 0.12f, z1 = B.z + 0.20f, yb = -0.51f; System.Func<float, float> yt = z => B.y - (z - B.z) * XB_AX.z / XB_AX.y;   // plane square to the can axis through the foot
            var v = new List<Vector3>(); foreach (var x in new[] { -0.15f, 0.15f }) { v.Add(new Vector3(x, yb, z0)); v.Add(new Vector3(x, yb, z1)); v.Add(new Vector3(x, yt(z1) - 0.004f, z1)); v.Add(new Vector3(x, yt(z0) - 0.004f, z0)); }
            int[][] fc = { new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 }, new[] { 0, 4, 5, 1 }, new[] { 1, 5, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 3, 7, 4, 0 } };
            foreach (var q in fc) mb.Quad(4, v[q[0]], v[q[1]], v[q[2]], v[q[3]]);
        }
        // lower strap: round the can and the seat 3 back, hooks on the seat back frame edges
        var cL = B + A * 0.70f;
        // (0.15.41, Nick 00:30: it clipped the seat) seat 3 back is x +-0.27, z -0.27..-0.37 with the centre spine (x +-0.08)
        // behind it, so the strap runs outside the side wings, round the back corners and hooks onto the spine
        XbStrap(mb, cL, A, F, new[] { new Vector3(-0.09f, cL.y, -0.40f), new Vector3(0.09f, cL.y, -0.40f) },
            new[] { new Vector3(-0.279f, cL.y, -0.262f), new Vector3(0.279f, cL.y, -0.262f), new Vector3(-0.279f, cL.y, -0.379f), new Vector3(0.279f, cL.y, -0.379f) });
        // upper strap: round the can to D-rings on a clamp round the mast
        var cU = B + A * 1.12f;
        XbStrap(mb, cU, A, F, new[] { new Vector3(-0.11f, cU.y, -0.43f), new Vector3(0.16f, cU.y, -0.43f) }, new Vector3[0]);
        float yD = OnPlane(new Vector3(0f, 0f, -0.43f), cU, A).y;   // D-ring height where the strap ends
        foreach (var dy in new[] { -0.06f, 0.06f }) mb.Tube(1, new Vector3(0.0f, yD + dy, -0.53f), X, Zf, Y, 0.065f, -0.008f, 0.008f, 16);   // mast clamp bands
        foreach (var dx in new[] { -0.11f, 0.16f }) BoxC(mb, 1, new Vector3(dx * 0.5f, yD, -0.47f), X * (Mathf.Abs(dx) + 0.02f), Y * 0.03f, Zf * 0.03f);   // clamp ears to the D-rings
        var mesh = SaveMesh(mb.Make("UL2_xb_kit"), ART + "UL2_xb_kit.asset");
        return new KeyValuePair<Mesh, Material[]>(mesh, new[] {
            LitMat("UL2_xb_strap", new Color32(86, 92, 52, 255), 0.15f, 0f),
            LitMat("UL2_xb_steel", new Color32(150, 152, 155, 255), 0.5f, 0.8f),
            LitMat("UL2_xb_ratchet", new Color32(150, 30, 26, 255), 0.4f, 0.1f),
            LitMat("UL2_xb_tray", new Color32(107, 110, 107, 255), 0.45f, 0.6f),
            LitMat("UL2_xb_pad", new Color32(30, 30, 30, 255), 0.1f, 0f) });
    }
    static void Seat3XBomb()
    {
        // 1. the bomb itself: own prefab + definition + info, copied from the stock GPO-N
        string bp = MNT + "UL2_xbomb.prefab", dp = MNT + "UL2_xbomb_def.asset", ip = MNT + "UL2_xbomb_info.asset";
        if (AssetDatabase.LoadMainAssetAtPath(bp) == null) { AssetDatabase.CopyAsset(PH + "GameObject/nuclearBomb1_PLACEHOLDER.prefab", bp); AssetDatabase.ImportAsset(bp); }
        var stockMissile = C(AssetDatabase.LoadAssetAtPath<GameObject>(PH + "GameObject/nuclearBomb1_PLACEHOLDER.prefab").transform, "Missile");
        var stockDef = new SerializedObject(stockMissile).FindProperty("definition").objectReferenceValue;
        if (AssetDatabase.LoadMainAssetAtPath(dp) == null) { AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(stockDef), dp); AssetDatabase.ImportAsset(dp); }
        if (AssetDatabase.LoadMainAssetAtPath(ip) == null) { AssetDatabase.CopyAsset(PH + "MonoBehaviour/info_nuclearBomb1_PLACEHOLDER.asset", ip); AssetDatabase.ImportAsset(ip); }
        var def = AssetDatabase.LoadMainAssetAtPath(dp); var info = AssetDatabase.LoadMainAssetAtPath(ip);
        var tickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ART + "UL2_tick.wav");
        var bgo = PrefabUtility.LoadPrefabContents(bp);
        try
        {
            bgo.name = "UL2_xbomb";
            var ms = new SerializedObject(C(bgo.transform, "Missile"));
            ms.FindProperty("definition").objectReferenceValue = def; ms.FindProperty("info").objectReferenceValue = info;
            ms.ApplyModifiedPropertiesWithoutUndo();
            var sk = new SerializedObject(C(bgo.transform, "OpticalSeekerBomb"));
            sk.FindProperty("armDelay").floatValue = 6f; sk.FindProperty("altitudeFuseHeight").floatValue = 100000f; sk.FindProperty("tangibleDelay").floatValue = 30f;
            sk.ApplyModifiedPropertiesWithoutUndo();
            foreach (var c in bgo.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Parachute") { var ps = new SerializedObject(c); var od = ps.FindProperty("openDelayMin"); if (od != null) { od.floatValue = 999f; ps.ApplyModifiedPropertiesWithoutUndo(); } else log.AppendLine("xbomb: no openDelayMin"); }
            // 0.15.39 (Nick 00:03): only the warhead can (centre section, small end caps), our own mesh
            var can = XbCan();
            var bmf = bgo.GetComponent<MeshFilter>(); bmf.sharedMesh = can.Key; bgo.GetComponent<MeshRenderer>().sharedMaterials = can.Value;
            var cc = bgo.GetComponent<CapsuleCollider>(); if (cc != null) { cc.direction = 2; cc.center = Vector3.zero; cc.radius = 0.19f; cc.height = 1.36f; }
            var blod = bgo.GetComponent<LODGroup>(); if (blod != null) Object.DestroyImmediate(blod);
            var tk = Find(bgo.transform, "UL2_tick"); if (tk == null) tk = New("UL2_tick", bgo.transform);
            tk.localPosition = Vector3.zero; tk.localRotation = Quaternion.identity;
            var src = tk.GetComponent<AudioSource>(); if (src == null) src = tk.gameObject.AddComponent<AudioSource>();
            src.clip = tickClip; src.playOnAwake = false; src.loop = false; src.spatialBlend = 1f; src.volume = 1f; src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 6f; src.maxDistance = 400f;
            PrefabUtility.SaveAsPrefabAsset(bgo, bp);
        }
        finally { PrefabUtility.UnloadPrefabContents(bgo); }
        var bomb = AssetDatabase.LoadAssetAtPath<GameObject>(bp);
        var dso = new SerializedObject(def);
        dso.FindProperty("jsonKey").stringValue = "UL2_xbomb"; dso.FindProperty("unitName").stringValue = XB_NAME;
        dso.FindProperty("unitPrefab").objectReferenceValue = bomb;
        dso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(def);
        var iso = new SerializedObject(info);
        iso.FindProperty("weaponName").stringValue = XB_NAME; iso.FindProperty("shortName").stringValue = "X-BOMB";
        iso.FindProperty("description").stringValue = XB_DESC;
        iso.FindProperty("weaponPrefab").objectReferenceValue = bomb;
        iso.FindProperty("effectiveness.antiSurface").floatValue = 0f; iso.FindProperty("effectiveness.antiAir").floatValue = 0f;
        iso.FindProperty("useWeaponDoors").boolValue = false;
        iso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(info);

        // 2. the seat 3 mount: stock external bomb mount, pylon removed, bomb posed nose-down in the seat
        var go = CopyPrefab("nuclearBomb1_external", "UL2_s3_xbomb");
        try
        {
            var rt = go.transform;
            var py = rt.Find("pylon");   // the stock bomb hangs under its pylon: lift it out, then drop the pylon
            var b = py.Find("bomb"); b.SetParent(rt, false); b.localScale = Vector3.one; b.name = "UL2_xbomb_mounted";   // the stock bomb carries an inverse of the pylon scale
            Object.DestroyImmediate(py.gameObject);
            // the can stands on the ECM tray chock on the seat 3 floor, leaned 12 deg back against the mast (like the
            // ECM pod), held by two olive ratchet straps: the lower one round the seat back to the rear rails, the upper
            // one to a D-ring clamped on the mast
            var lod = b.GetComponent<LODGroup>(); if (lod != null) Object.DestroyImmediate(lod);
            foreach (Transform ch in b) Object.DestroyImmediate(ch.gameObject);
            var can2 = XbCan();
            b.GetComponent<MeshFilter>().sharedMesh = can2.Key; b.GetComponent<MeshRenderer>().sharedMaterials = can2.Value;
            b.localRotation = Quaternion.LookRotation(XB_AX, XB_FW); b.localPosition = XB_BASE + XB_AX * 0.68f;
            var kit = New("UL2_xbkit", rt); kit.localPosition = Vector3.zero; kit.localRotation = Quaternion.identity;
            var kv = XbKit();
            kit.gameObject.AddComponent<MeshFilter>().sharedMesh = kv.Key; kit.gameObject.AddComponent<MeshRenderer>().sharedMaterials = kv.Value;
            var mm = C(b, "MountedMissile");
            var mso = new SerializedObject(mm); mso.FindProperty("info").objectReferenceValue = info; mso.ApplyModifiedPropertiesWithoutUndo();
            var m = SaveMount(go, "UL2_s3_xbomb", "nuclearBomb1_external", info, XB_NAME);
            var wso = new SerializedObject(m);
            wso.FindProperty("isEventContent").boolValue = true;
            wso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
            log.AppendLine("seat3 xbomb: def " + def.name + ", tick " + (tickClip != null) + ", can on the seat, kit " + kv.Key.vertexCount + " verts");
        }
        catch (System.Exception ex) { log.AppendLine("SEAT3 xbomb ERROR " + ex); }
        finally { Object.DestroyImmediate(go); }
    }
    static KeyValuePair<Mesh, Material[]> EcmKitMesh()
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var subs = new List<KeyValuePair<string, int[]>>(); var mats = new Dictionary<string, float[]>();
        using (var r = new BinaryReader(File.OpenRead(S3 + "UL2_s3_ecm_kit.bytes")))
        {
            int nv = r.ReadInt32();
            for (int i = 0; i < nv; i++) v.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            for (int i = 0; i < nv; i++) n.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            int ns = r.ReadInt32();
            for (int s = 0; s < ns; s++) { var key = RS(r); int c = r.ReadInt32(); var t = new int[c]; for (int i = 0; i < c; i++) t[i] = r.ReadInt32(); subs.Add(new KeyValuePair<string, int[]>(key, t)); }
            int nm = r.ReadInt32();
            for (int k = 0; k < nm; k++) { var key = RS(r); var f = new float[5]; for (int i = 0; i < 5; i++) f[i] = r.ReadSingle(); mats[key] = f; }
        }
        string path = ART + "UL2_s3_ecm_kit.asset";
        var me = AssetDatabase.LoadAssetAtPath<Mesh>(path); bool fresh = me == null; if (fresh) me = new Mesh();
        me.Clear(); me.name = "UL2_s3_ecm_kit"; me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        me.SetVertices(v); me.SetNormals(n); me.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) me.SetTriangles(subs[s].Value, s);
        me.RecalculateBounds();
        if (fresh) AssetDatabase.CreateAsset(me, path); else EditorUtility.SetDirty(me);
        var mm = subs.Select(s =>
        {
            float[] f; if (!mats.TryGetValue(s.Key, out f)) f = new[] { 0.05f, 0.05f, 0.05f, 0.2f, 0f };
            var col = new Color(Mathf.LinearToGammaSpace(f[0]), Mathf.LinearToGammaSpace(f[1]), Mathf.LinearToGammaSpace(f[2]), 1f);
            return LitMat("UL2_ecm_" + s.Key, col, Mathf.Clamp01(f[3]), Mathf.Clamp01(f[4]));
        }).ToArray();
        return new KeyValuePair<Mesh, Material[]>(me, mm);
    }
}
