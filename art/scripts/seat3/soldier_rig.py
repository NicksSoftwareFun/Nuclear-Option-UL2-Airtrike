"""Rig the seat 3 soldier (one soldier from the Ibis troop mesh, exported from Unity with UVs, pelvis at the origin,
facing forward) with a small armature, weights computed from bone distance plus piece rules, then pose tests.
Usage: python3 -I soldier_rig.py -- <UL2_soldier_unity.obj> <out.blend>
Blender frame: +Y forward, +Z up (Unity (x, y, z) -> Blender (x, z, y))."""
import bpy, bmesh, sys, math
import numpy as np
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
V, UV, F = [], [], []
for ln in open(a[0]):
    p = ln.split()
    if not p: continue
    if p[0] == 'v': x, y, z = map(float, p[1:4]); V.append((x, z, y))
    elif p[0] == 'vt': UV.append((float(p[1]), float(p[2])))
    elif p[0] == 'f':
        ids = [int(t.split('/')[0]) - 1 for t in p[1:4]]; F.append((ids[0], ids[2], ids[1]))
me = bpy.data.meshes.new('UL2_soldier'); me.from_pydata(V, [], F); me.update()
uvl = me.uv_layers.new(name='UVMap')
for poly in me.polygons:
    for li in poly.loop_indices: uvl.data[li].uv = UV[me.loops[li].vertex_index]
for poly in me.polygons: poly.use_smooth = True
so = bpy.data.objects.new('UL2_soldier', me); bpy.context.scene.collection.objects.link(so)

# ---- armature (rest = the seated troop pose)
B = {  # name: (head, tail, parent)
    'hips': ((0, -0.05, -0.02), (0, -0.07, 0.15), None),
    'spine': ((0, -0.07, 0.15), (0, -0.08, 0.40), 'hips'),
    'chest': ((0, -0.08, 0.40), (0, -0.07, 0.53), 'spine'),
    'head': ((0, -0.04, 0.55), (0, -0.03, 0.86), 'chest'),
}
for s, sx in (('L', -1), ('R', 1)):
    B[f'upperarm_{s}'] = ((sx * 0.20, -0.10, 0.46), (sx * 0.25, -0.03, 0.25), 'chest')
    B[f'forearm_{s}'] = ((sx * 0.25, -0.03, 0.25), (sx * 0.17, 0.22, 0.19), f'upperarm_{s}')
    B[f'hand_{s}'] = ((sx * 0.17, 0.22, 0.19), (sx * 0.14, 0.31, 0.15), f'forearm_{s}')
    B[f'thigh_{s}'] = ((sx * 0.10, 0.00, 0.00), (sx * 0.13, 0.33, 0.08), 'hips')
    B[f'shin_{s}'] = ((sx * 0.13, 0.33, 0.08), (sx * 0.15, 0.34, -0.42), f'thigh_{s}')
arm = bpy.data.armatures.new('UL2_soldier_rig'); ao = bpy.data.objects.new('UL2_soldier_rig', arm); bpy.context.scene.collection.objects.link(ao)
bpy.context.view_layer.objects.active = ao; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, par) in B.items():
    eb = arm.edit_bones.new(n); eb.head = h; eb.tail = t
for n, (h, t, par) in B.items():
    if par: arm.edit_bones[n].parent = arm.edit_bones[par]; arm.edit_bones[n].use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---- weights: Blender bone heat first (follows the surface, so hands on the knees don't bind to the legs)
names = list(B)
HEAT = '--dist' not in sys.argv
if HEAT:
    # bone heat needs a welded mesh: weld a copy (UV seams split the export), heat it, map the weights back by position
    wm = me.copy(); wo = bpy.data.objects.new('weld', wm); bpy.context.scene.collection.objects.link(wo)
    wb = bmesh.new(); wb.from_mesh(wm); bmesh.ops.remove_doubles(wb, verts=wb.verts, dist=0.0006); wb.to_mesh(wm); wb.free()
    for o in bpy.context.scene.objects: o.select_set(False)
    wo.select_set(True); ao.select_set(True); bpy.context.view_layer.objects.active = ao
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    from mathutils.kdtree import KDTree
    kd = KDTree(len(wm.vertices))
    for v in wm.vertices: kd.insert(v.co, v.index)
    kd.balance()
    wn = {g.index: g.name for g in wo.vertex_groups}
    for n in names: so.vertex_groups.new(name=n)
    hit = 0
    for v in me.vertices:
        co, wi, d = kd.find(v.co)
        for gr in wm.vertices[wi].groups:
            if gr.weight > 0: so.vertex_groups[wn[gr.group]].add([v.index], gr.weight, 'REPLACE'); hit += 1
    print('heat weld verts', len(wm.vertices), 'weighted links', hit)
    bpy.data.objects.remove(wo)
# ---- fallback weights: nearest bone segments (inverse distance^4 over the two nearest), with piece rules
P = np.array(V)
names = list(B)
def segdist(p, h, t):
    h = np.array(h); t = np.array(t); d = t - h; u = np.clip(((p - h) @ d) / (d @ d), 0, 1)
    return np.linalg.norm(p - (h + u[:, None] * d), axis=1)
D = np.stack([segdist(P, *B[n][:2]) for n in names], 1)
# pieces (islands)
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
isl = np.full(len(V), -1); k = 0
key = {}
for v in bm.verts: key.setdefault(tuple(np.round(np.array(v.co) * 2000).astype(int)), []).append(v.index)
for v in bm.verts:
    if isl[v.index] >= 0: continue
    st = [v]; isl[v.index] = k
    while st:
        q = st.pop(); nb = [e.other_vert(q) for e in q.link_edges] + [bm.verts[i] for i in key[tuple(np.round(np.array(q.co) * 2000).astype(int))]]
        for w in nb:
            if isl[w.index] < 0: isl[w.index] = k; st.append(w)
    k += 1
sizes = np.bincount(isl); main = sizes.argmax()
cen = np.array([P[isl == i].mean(0) for i in range(k)])
force = {}
for i in range(k):
    if i == main: continue
    c = cen[i]
    if c[2] > 0.50 and c[1] > -0.15: force[i] = 'head'          # helmet pieces (Blender y = forward)
    elif c[2] < 0.05: force[i] = 'hips'                         # rifle standing between the knees, boots
    else: force[i] = 'chest'                                    # vest pouches, radio, antenna
W = np.zeros_like(D)
o = np.argsort(D, 1)[:, :2]
for j in range(2):
    d = D[np.arange(len(P)), o[:, j]]; W[np.arange(len(P)), o[:, j]] = 1 / np.maximum(d, 0.01) ** 4
# the plate carrier and neck: nothing above the shoulders except the neck column goes to the head
nr = np.hypot(P[:, 0], P[:, 1] + 0.02)
W[:, names.index('head')] *= np.where((P[:, 2] > 0.50) & (nr < 0.085), 1.0, 0.0)
W /= W.sum(1, keepdims=True)
for i, bn in force.items(): W[isl == i] = 0; W[isl == i, names.index(bn)] = 1
if HEAT:
    # read heat weights back, apply the neck rule and the piece rules, renormalise
    W = np.zeros((len(P), len(names)))
    gi = {g.index: names.index(g.name) for g in so.vertex_groups if g.name in names}
    for v in me.vertices:
        for gr in v.groups:
            if gr.group in gi: W[v.index, gi[gr.group]] = gr.weight
    none = W.sum(1) == 0
    W[none] = (1 / np.maximum(D[none], 0.01) ** 4)
    W[:, names.index('head')] *= np.where((P[:, 2] > 0.50) & (nr < 0.085), 1.0, 0.0)
    # hands rest on the knees: a vertex belongs either to an arm or to a leg, never both (no stretching when the arms lift)
    for s_ in ('L', 'R'):
        ai = [names.index(f'{b}_{s_}') for b in ('upperarm', 'forearm', 'hand')]
        li = [names.index(f'{b}_{s_}') for b in ('thigh', 'shin')] + [names.index(f'{b}_{"R" if s_ == "L" else "L"}') for b in ('thigh', 'shin')]
        arm_w = W[:, ai].sum(1); leg_w = W[:, li].sum(1)
        # the hand and its fingers draped over the knee: flood along the mesh from the palm, up to 16 cm from the hand bone
        hd = D[:, names.index(f'hand_{s_}')]
        flood = np.zeros(len(P), bool); st = list(np.nonzero(hd < 0.035)[0]); flood[st] = True
        while st:
            q = bm.verts[st.pop()]
            for w in [e.other_vert(q) for e in q.link_edges] + [bm.verts[i] for i in key[tuple(np.round(np.array(q.co) * 2000).astype(int))]]:
                if not flood[w.index] and hd[w.index] < 0.20 and P[w.index, 2] > -0.03: flood[w.index] = True; st.append(w.index)
        armv = (arm_w > leg_w) | flood
        W[np.ix_(armv, li)] = 0; W[np.ix_(~armv, ai)] = 0
        empty = W.sum(1) == 0
        for vi in np.nonzero(empty & armv)[0]: W[vi, ai[int(np.argmin(D[vi, ai]))]] = 1
        for vi in np.nonzero(empty & ~armv)[0]:
            cand = [j for j in range(len(names)) if j not in ai]; W[vi, cand[int(np.argmin(D[vi, cand]))]] = 1
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    for i, bn in force.items(): W[isl == i] = 0; W[isl == i, names.index(bn)] = 1
    print('verts without heat weights', int(none.sum()))
    for g in list(so.vertex_groups): so.vertex_groups.remove(g)
for n in names: so.vertex_groups.new(name=n)
for vi in range(len(P)):
    for j in np.nonzero(W[vi] > 0.01)[0]: so.vertex_groups[names[j]].add([vi], float(W[vi, j]), 'REPLACE')
if not so.modifiers: mod = so.modifiers.new('rig', 'ARMATURE'); mod.object = ao
so.parent = ao
print('islands', k, 'forced', {b: sum(1 for v in force.values() if v == b) for b in set(force.values())})
bpy.ops.wm.save_as_mainfile(filepath=a[1])
print('saved', a[1])
