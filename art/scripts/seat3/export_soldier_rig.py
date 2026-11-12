"""Export the rigged seat 3 soldier for the Unity builder as JSON (Unity coordinates: x right, y up, z forward,
pelvis at the origin). Usage: python3 -I export_soldier_rig.py -- <rig.blend> <out.json> [--rifle]
Bones are written with their head positions only (the builder makes them identity-rotated, so bind poses are pure
translations); each vertex keeps its 4 strongest weights. Without --rifle the troop rifle between the knees is dropped."""
import bpy, bmesh, sys, json
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
so = bpy.data.objects['UL2_soldier']; ao = bpy.data.objects['UL2_soldier_rig']; me = so.data
U = lambda v: [round(v[0], 5), round(v[2], 5), round(v[1], 5)]      # Blender (x, y, z) -> Unity (x, z, y)
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table(); drop = set()
if '--rifle' not in sys.argv:
    seen = set()
    for v in bm.verts:
        if v.index in seen: continue
        st = [v]; comp = []; seen.add(v.index)
        while st:
            q = st.pop(); comp.append(q)
            for e in q.link_edges:
                w = e.other_vert(q)
                if w.index not in seen: seen.add(w.index); st.append(w)
        lo = [min(q.co[i] for q in comp) for i in range(3)]; hi = [max(q.co[i] for q in comp) for i in range(3)]
        if lo[0] > -0.02 and hi[0] < 0.07 and lo[1] > 0.15 and hi[2] < 0.25 and lo[2] < -0.1: drop.update(q.index for q in comp)
bm.free()
names = [b.name for b in ao.data.bones]
gidx = {g.index: g.name for g in so.vertex_groups}
uvl = me.uv_layers.active.data
# split per loop UV like Unity does: one vertex per (vertex, uv) pair
key = {}; verts = []; uvs = []; nrm = []; bw = []; tris = []
me.calc_loop_triangles() if hasattr(me, 'calc_loop_triangles') else None
for lt in me.loop_triangles:
    if any(me.loops[li].vertex_index in drop for li in lt.loops): continue
    out = []
    for li in lt.loops:
        vi = me.loops[li].vertex_index; uv = tuple(round(c, 5) for c in uvl[li].uv)
        k = (vi, uv)
        if k not in key:
            key[k] = len(verts); v = me.vertices[vi]
            verts.append(U(v.co)); nrm.append(U(v.normal)); uvs.append(list(uv))
            ws = sorted(((g.weight, gidx[g.group]) for g in v.groups if gidx[g.group] in names and g.weight > 0.001), reverse=True)[:4]
            t = sum(w for w, _ in ws) or 1.0
            bw.append([[names.index(n), round(w / t, 4)] for w, n in ws])
        out.append(key[k])
    tris += [out[0], out[2], out[1]]          # handedness flip
bones = [{'name': b.name, 'parent': b.parent.name if b.parent else '', 'head': U(b.head_local), 'tail': U(b.tail_local)} for b in ao.data.bones]
json.dump({'verts': verts, 'normals': nrm, 'uvs': uvs, 'tris': tris, 'weights': bw, 'bones': bones}, open(a[1], 'w'))
print('soldier json', len(verts), 'verts', len(tris) // 3, 'tris', len(bones), 'bones', 'dropped', len(drop))
