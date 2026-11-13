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
    // 0.15.38 (Nick 23:37): event content (a mount flagged isEventContent, hidden unless the mission allows event content).
    // The stock GPO-N 1.5kt bomb strapped nose-down in seat 3. Firing it spawns the bomb as usual (it costs a warhead like
    // the stock bomb, its info is nuclear), but the plugin keeps it on the seat and plays a timer tick; the copy's own fuse
    // does the rest: arm delay 6 s and an altitude fuse so high that it goes off the moment it arms. It stays intangible
    // (tangible delay 30 s) so it never strikes its own aircraft, and its parachute never opens (open delay 999 s).
    // AI never picks it (effectiveness 0).
    const string XB_NAME = "Expedient 1.5kt Bomb";
    const string XB_DESC = "Volunteers needed. Single men only. Over 40 preferred.";
    // pose in seat3_hp space: nose point and lean (deg, tail toward +x); set from the fit Nick picks
    static readonly Vector3 XB_NOSE = new Vector3(-0.05f, -0.30f, 0.05f);   // Nick 23:42: nose on the seat 3 floor, leaned far right
    const float XB_LEAN = 62f;   // tail fins stay clear under the rotor disc
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
            var n = new Vector3(-Mathf.Sin(XB_LEAN * Mathf.Deg2Rad), -Mathf.Cos(XB_LEAN * Mathf.Deg2Rad), 0f);
            b.localRotation = Quaternion.LookRotation(n, Vector3.forward);
            b.localPosition = XB_NOSE - n * 1.58f;   // bomb origin sits 1.58 m behind the nose tip (mesh z -2.02..1.58)
            var mm = C(b, "MountedMissile");
            var mso = new SerializedObject(mm); mso.FindProperty("info").objectReferenceValue = info; mso.ApplyModifiedPropertiesWithoutUndo();
            var m = SaveMount(go, "UL2_s3_xbomb", "nuclearBomb1_external", info, XB_NAME);
            var wso = new SerializedObject(m);
            wso.FindProperty("isEventContent").boolValue = true;
            wso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
            log.AppendLine("seat3 xbomb: def " + def.name + ", tick " + (tickClip != null) + ", nose " + XB_NOSE + " lean " + XB_LEAN);
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
