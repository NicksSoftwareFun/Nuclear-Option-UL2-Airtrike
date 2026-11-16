using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Passenger module (Nick 2026-11-16 21:36): one loadout pick that takes the Third Seat and both wing stubs. Two outward seats
// back to back in row 3 (machine gunners) and one outward seat on each stub (grenadiers), each seat with a footrest and a
// rifle butt cup. Four stock turrets in one mount; the plugin attaches the three the stock SpawnMount skips, and the game
// groups them by weapon info, so in flight the two MGs fire as one weapon and the two GLs as another (Ibis door gun style).
// Seats and soldiers: art/scripts/pm (pm_model.py, pm_bake.py outward pose tables, pm_kitdump.py, pm_arcs.py firing arcs).
public static partial class UL2Builder
{
    class PmStation { public string name, src, kind; public Vector3 pos; public float sx, centre; public float[][] cones; }

    // firing arcs from pm_arcs.py (rays against the airframe, crew, the other soldiers and the prop, not the rotor disc).
    // Headings in the seat frame: 0 = straight out, + = clockwise from above. Traverse is +-94 deg about 'centre'; the
    // exclusion cones (heading, elevation, half angle; greedy fit, pm/cones3.py) cover what is still blocked inside that: the
    // stub seat and grenadier ahead of and below the gunners (level fire straight ahead stays open, only the grenadier's head is
    // coned out), the prop edge and frame behind them, the gunner behind and above the grenadiers, the stub under them.
    static readonly PmStation[] PM = {
        new PmStation { name = "pm_mg_L", src = "turret_12.7mm_door", kind = "pmmg", pos = new Vector3(-0.45f, -0.02f, 0f), sx = -1f, centre = 22f,
                        cones = new[] { new[] { 90f, -34f, 34f }, new[] { 114f, -6f, 16f }, new[] { 70f, -2f, 8f }, new[] { -82f, -2f, 14f }, new[] { 58f, -18f, 6f } } },
        new PmStation { name = "pm_mg_R", src = "turret_12.7mm_door", kind = "pmmg", pos = new Vector3(0.45f, -0.02f, 0f), sx = 1f, centre = -22f,
                        cones = new[] { new[] { -98f, -42f, 40f }, new[] { -66f, -2f, 8f }, new[] { -126f, -30f, 38f }, new[] { -50f, -22f, 6f }, new[] { 82f, -2f, 14f }, new[] { -70f, -6f, 6f } } },
        new PmStation { name = "pm_gl_L", src = "turret_40mm_grenade", kind = "pmgl", pos = new Vector3(-0.82f, -0.34f, 0.95f), sx = -1f, centre = 8f,
                        cones = new[] { new[] { -96f, -50f, 18f }, new[] { -72f, 14f, 6f }, new[] { -88f, 14f, 6f }, new[] { -48f, 6f, 4f } } },
        new PmStation { name = "pm_gl_R", src = "turret_40mm_grenade", kind = "pmgl", pos = new Vector3(0.82f, -0.34f, 0.95f), sx = 1f, centre = -8f,
                        cones = new[] { new[] { 76f, 14f, 6f }, new[] { 96f, -50f, 18f }, new[] { 68f, 10f, 4f }, new[] { 92f, 14f, 8f } } } };
    const float PM_TRAVERSE = 94f;

    static void PassengerModule()
    {
        var go = new GameObject("UL2_pm");
        try
        {
            var rt = go.transform;
            // seats, footrests and saddles: one static mesh in plain kit colours
            var seats = New("UL2_pm_seats", rt); seats.localPosition = Vector3.zero; seats.localRotation = Quaternion.identity;
            var kv = PmSeatsMesh();
            seats.gameObject.AddComponent<MeshFilter>().sharedMesh = kv.Key;
            seats.gameObject.AddComponent<MeshRenderer>().sharedMaterials = kv.Value;
            foreach (var S in PM)
            {
                var st = New(S.name, rt); st.localPosition = S.pos; st.localRotation = Quaternion.Euler(0f, 90f * S.sx, 0f);   // facing out
                var tg = CopyPrefab(S.src, S.name + "_turret"); var tt = tg.transform;
                tt.SetParent(st, false); tt.localPosition = Vector3.zero; tt.localRotation = Quaternion.Euler(0f, S.centre, 0f);
                var tr = tt.Find("traverse"); var br = tr.Find("barrel");
                tr.localPosition = new Vector3(0f, 0.45f, 0f); tr.localRotation = Quaternion.identity; tr.GetComponent<MeshFilter>().sharedMesh = null;
                br.localPosition = new Vector3(0f, 0f, 0.55f); br.localRotation = Quaternion.identity; br.GetComponent<MeshFilter>().sharedMesh = null;
                var mz = br.Find("muzzle"); mz.localPosition = new Vector3(0f, 0f, 0.5f); mz.localRotation = Quaternion.identity;
                foreach (var n in new[] { "firingConeNegative_up", "firingConeNegative_down" }) { var c0 = tt.Find(n); if (c0 != null) Object.DestroyImmediate(c0.gameObject); }
                var tu = C(tr, "Turret"); var gun = C(br, "Gun");
                Set(tu, "traverseRange", PM_TRAVERSE); Set(tu, "minElevation", -40f); Set(tu, "maxElevation", 20f);
                Set(tu, "traverseRate", 100f); Set(tu, "elevationRate", 70f);
                var so = new SerializedObject(tu); var fc = so.FindProperty("firingCones"); fc.arraySize = S.cones.Length;
                for (int i = 0; i < S.cones.Length; i++)
                {
                    var ct = New(S.name + "_cone" + i, st); ct.localPosition = new Vector3(0f, 0.45f, 0f); ct.localRotation = Quaternion.Euler(-S.cones[i][1], S.cones[i][0], 0f);
                    var e = fc.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("transform").objectReferenceValue = ct;
                    e.FindPropertyRelative("coneAngle").floatValue = S.cones[i][2];
                    e.FindPropertyRelative("exclusion").boolValue = true;
                }
                so.FindProperty("aimSafetyWeapon").objectReferenceValue = gun;   // no firing while the barrel slews (0.15.81)
                so.ApplyModifiedPropertiesWithoutUndo();
                // seat 3 gun settings + 3 spare magazines (Nick 21:59), own infos with +50% range (Nick 21:56)
                if (S.kind == "pmmg") { Set(gun, "fireRate", 650f); Set(gun, "magazineCapacity", 75); Set(gun, "magazines", 9); Set(gun, "reloadTime", 5f); Set(gun, "bulletSpread", 2.5f); Set(gun, "recoilImpulse", 25f); Set(gun, "info", PmMgInfo()); Set(gun, "tracerRatio", 3); Set(gun, "pitch", 0.72f); }
                else { Set(gun, "fireRate", 113f); Set(gun, "magazineCapacity", 6); Set(gun, "magazines", 9); Set(gun, "reloadTime", 7f); Set(gun, "info", PmGlInfo()); }
                S3Kit(st.gameObject, S.kind);
                log.AppendLine("pm station " + S.name + " at " + S.pos + " centre " + S.centre + " cones " + S.cones.Length);
            }
            var m = SaveMount(go, "UL2_pm", "turret_12.7mm_door", PmInfo(), "Special Operations Team");   // Nick 22:09: station "Passenger Module", weapon "Special Operations Team"
            var ms = new SerializedObject(m);
            ms.FindProperty("GearSafety").boolValue = false; ms.FindProperty("GroundSafety").boolValue = false;
            ms.FindProperty("emptyMass").floatValue = 470f;   // four soldiers with kit (seat 3: 100-115 each) + seats and footrests
            ms.FindProperty("drag").floatValue = 0.08f; ms.FindProperty("emptyDrag").floatValue = 0.08f;
            ms.FindProperty("emptyCost").floatValue = 0.08f;
            ms.FindProperty("ammo").intValue = 675;            // MG: 75 x 9 per gun as the loadout figure
            ms.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        }
        catch (System.Exception ex) { log.AppendLine("PM ERROR " + ex); }
        finally { Object.DestroyImmediate(go); }
    }

    static KeyValuePair<Mesh, Material[]> PmSeatsMesh()
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var subs = new List<KeyValuePair<string, int[]>>();
        using (var r = new BinaryReader(File.OpenRead(S3 + "UL2_pm_seats_kit.bytes")))
        {
            int nv = r.ReadInt32();
            for (int i = 0; i < nv; i++) v.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            for (int i = 0; i < nv; i++) n.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            int ns = r.ReadInt32();
            for (int s = 0; s < ns; s++) { var key = RS(r); int c = r.ReadInt32(); var t = new int[c]; for (int i = 0; i < c; i++) t[i] = r.ReadInt32(); subs.Add(new KeyValuePair<string, int[]>(key, t)); }
        }
        string path = ART + "UL2_pm_seats.asset";
        var me = AssetDatabase.LoadAssetAtPath<Mesh>(path); bool fresh = me == null; if (fresh) me = new Mesh();
        me.Clear(); me.name = "UL2_pm_seats"; me.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        me.SetVertices(v); me.SetNormals(n); me.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) me.SetTriangles(subs[s].Value, s);
        me.RecalculateBounds();
        if (fresh) AssetDatabase.CreateAsset(me, path); else EditorUtility.SetDirty(me);
        // the plain kit base colours the hand controls use (CTL), so the seats match the airframe's frame, cushions and fittings
        var mm = subs.Select(s => CTL.ContainsKey(s.Key) ? CtlMat(s.Key) : CtlMat("UL2_dark")).ToArray();
        log.AppendLine("pm seats: " + v.Count + " verts, " + string.Join(",", subs.Select(s => s.Key + " " + s.Value.Length / 3)));
        return new KeyValuePair<Mesh, Material[]>(me, mm);
    }

    static Object PmInfoCopy(string path, Object src)
    {
        var m = AssetDatabase.LoadMainAssetAtPath(path);
        if (m == null) { AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), path); AssetDatabase.ImportAsset(path); m = AssetDatabase.LoadMainAssetAtPath(path); }
        return m;
    }
    static Object PmMgInfo()
    {
        var m = PmInfoCopy(MNT + "UL2_pm_mg_info.asset", MgInfo());
        var so = new SerializedObject(m);
        so.FindProperty("targetRequirements.maxRange").floatValue = 3150f;   // seat 3 MG 2100 m +50%
        so.FindProperty("weaponName").stringValue = ".338 LM Machine Gunners";
        so.FindProperty("shortName").stringValue = "MG-338 x2";
        so.FindProperty("description").stringValue = "Two outward facing machine gunners in the third row with belt-fed .338 Lapua Magnum machine guns. They fire together.";
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        return m;
    }
    static Object PmGlInfo()
    {
        var src = GlInfo(null);
        var m = PmInfoCopy(MNT + "UL2_pm_gl_info.asset", src);
        var ss = new SerializedObject(src); var so = new SerializedObject(m);
        float r0 = ss.FindProperty("targetRequirements.maxRange").floatValue, v0 = ss.FindProperty("muzzleVelocity").floatValue;
        // +50% range (Nick 21:56): the AI engagement range and, for the lobbed grenade, the muzzle velocity by sqrt(1.5) so it carries 50% further
        so.FindProperty("targetRequirements.maxRange").floatValue = r0 * 1.5f;
        so.FindProperty("muzzleVelocity").floatValue = v0 * 1.2247f;
        var ms = so.FindProperty("maxSpeed"); if (ms != null && ms.floatValue > 0f) ms.floatValue = Mathf.Max(ms.floatValue, v0 * 1.2247f);
        so.FindProperty("weaponName").stringValue = "40 mm Grenadiers";
        so.FindProperty("shortName").stringValue = "40mm GL x2";
        so.FindProperty("description").stringValue = "Two outward facing grenadiers on the wing stubs with six-shot 40 mm grenade launchers. They fire together.";
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        log.AppendLine("pm gl info: range " + r0 + " -> " + r0 * 1.5f + ", muzzle " + v0 + " -> " + v0 * 1.2247f);
        return m;
    }
    static Object PmInfo()
    {
        var m = PmInfoCopy(MNT + "UL2_pm_info.asset", MgInfo());
        var so = new SerializedObject(m);
        so.FindProperty("weaponName").stringValue = "Special Operations Team";
        so.FindProperty("shortName").stringValue = "SOT";
        so.FindProperty("description").stringValue = "Four soldiers on outward facing seats: two .338 machine gunners back to back in the third row and two 40 mm grenadiers on the wing stubs. Takes the third seat and both stubs. The gunners and the grenadiers each fire as a pair. On the ground they capture like a full Ibis infantry bay.";
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(m);
        return m;
    }

    // set 4: one hardpoint at the seat 3 pelvis point on the seat part. Its Plug is the seat 3 mesh (on 'intake'), which the
    // stock Hardpoint.ShowPylon hides while the module is loaded. It precludes the stubs and the Third Seat and they preclude it.
    static void PmHardpoint(SerializedProperty sets)
    {
        var seat = Need("intake");
        var hp = Find(root, "pm_hp") ?? New("pm_hp", seat);
        hp.position = P("gunner"); hp.rotation = Quaternion.identity;
        sets.arraySize = 5;
        var p = sets.GetArrayElementAtIndex(4);
        p.FindPropertyRelative("name").stringValue = "Passenger Module";
        p.FindPropertyRelative("SymmetryWithPrev").boolValue = false;
        p.FindPropertyRelative("SymmetryName").stringValue = "";
        var pre = p.FindPropertyRelative("precludingHardpointSets"); pre.arraySize = 3;
        for (int i = 0; i < 3; i++) pre.GetArrayElementAtIndex(i).intValue = i + 1;
        var php = p.FindPropertyRelative("hardpoints"); php.arraySize = 1;
        var h = php.GetArrayElementAtIndex(0);
        h.FindPropertyRelative("transform").objectReferenceValue = hp;
        h.FindPropertyRelative("part").objectReferenceValue = C(seat, "AeroPart");
        h.FindPropertyRelative("Plug").objectReferenceValue = seat.GetComponent<MeshRenderer>();
        var op = p.FindPropertyRelative("weaponOptions"); op.arraySize = 2;
        op.GetArrayElementAtIndex(0).objectReferenceValue = null;
        op.GetArrayElementAtIndex(1).objectReferenceValue = Mount("UL2_pm");
        for (int s = 1; s <= 3; s++)
        {
            var q = sets.GetArrayElementAtIndex(s).FindPropertyRelative("precludingHardpointSets"); q.arraySize = 1; q.GetArrayElementAtIndex(0).intValue = 4;
        }
        log.AppendLine("pm hardpoint: plug " + (seat.GetComponent<MeshRenderer>() != null));
    }
}
