"""Bake a seat 3 kit (soldier + weapon + harness) for the game: one skinned mesh bound at a reference pose plus a pose
table sampled from the Blender rig (gunner_rig.Gunner) on a heading x elevation grid. The plugin interpolates the
table from the stock turret's traverse and elevation, so the in-game poses are the same IK solutions as the renders.

Usage: python3 -I seat3_bake.py -- <kind mg|gl|spot> <out.json> [--step 10]
Coordinates: Unity (x right, y up, z forward), origin = the soldier pelvis (the seat 3 hardpoint)."""
import bpy, sys, math, json, re
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]; KIND = a[0]; OUT = a[1]
STEP = float(sys.argv[sys.argv.index('--step') + 1]) if '--step' in sys.argv else 10.0
bpy.ops.wm.read_factory_settings(use_empty=True)
import mg_model as mg
ARMREC = {}
_orig_build_arm = mg.build_arm
def _rec_arm(hip, elbow, post_bot, post_top=None):
    ARMREC.update(hip=Vector(hip), elbow=Vector(elbow), bot=Vector(post_bot), top=Vector(post_top) if post_top is not None else Vector(post_bot) + Vector((0, 0, 0.08)))
    return _orig_build_arm(hip, elbow, post_bot, post_top)
mg.build_arm = _rec_arm
from gunner_rig import Gunner
import look

# arcs per kind (same as the validated sweeps): forward limit right, forward limit left, rear limit; swap path over the top
AFR, AFL, AR = {'gl': (25, 45, 100), 'mg': (30, 38, 120), 'spot': (20, 20, 130)}[KIND]
OVER = {'gl': [(25, 20), (15, 45), (0, 55), (-20, 60), (-40, 52), (-45, 20)],
        'mg': [(30, 20), (12, 68), (0, 74), (-12, 69), (-38, 20)],
        'spot': [(20, 20), (0, 30), (-20, 20)]}[KIND]
ELEV = [-40.0, -25.0, -10.0, 5.0, 20.0]
REF = (75.0, -10.0)


def e_over(A):
    pts = sorted(OVER)
    if A <= pts[0][0]: return pts[0][1]
    for (a0, e0), (a1, e1) in zip(pts, pts[1:]):
        if a0 <= A <= a1: f = (A - a0) / (a1 - a0); return e0 + (e1 - e0) * f
    return pts[-1][1]


def vis(A, E):
    """turret heading/elevation -> the pose actually shown: inside the forward zone the weapon rides the swap path over
    the seat 2 crewman, blended in over the first 12 deg so a turret swinging through the front never snaps"""
    if -AFL < A < AFR:
        w = min(1.0, (AFR - A) / 12.0, (A + AFL) / 12.0); w = w * w * (3 - 2 * w)
        return A, E + (e_over(A) - E) * w
    return A, E


g = Gunner(Matrix.Identity(4), kind=KIND)
look.crew(g); look.gun(g.gun_objs); look.gear(g)
ao, so, pb = g.ao, g.so, g.ao.pose.bones
DEF = [b.name for b in ao.data.bones if b.name in so.vertex_groups]
BONES = DEF + ['gun'] + (['armU', 'armL'] if KIND == 'mg' else [])
S = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))
def U(v): return [round(v[0], 5), round(v[2], 5), round(v[1], 5)]


def frame(o, y_to, ref_up):
    y = (y_to - o).normalized(); x = y.cross(ref_up).normalized(); z = x.cross(y)
    M = Matrix((x, y, z)).transposed().to_4x4(); M.translation = o; return M


def bone_mats():
    W = ao.matrix_world; out = {}
    for n in DEF: out[n] = W @ pb[n].matrix
    out['gun'] = g.gun.matrix_world.copy()
    if KIND == 'mg' and ARMREC:
        h, e, b = ARMREC['hip'], ARMREC['elbow'], ARMREC['bot']
        nrm = (e - h).cross(b - e)
        if nrm.length < 1e-6: nrm = Vector((0, 0, 1))
        out['armU'] = frame(h, e, nrm.normalized()); out['armL'] = frame(e, b, nrm.normalized())
    return out


def to_u(M):
    R = S @ M.to_3x3() @ S; q = R.to_quaternion()
    return U(M.translation) + [round(q.x, 5), round(q.y, 5), round(q.z, 5), round(q.w, 5)]


# ------------------------------------------------------------------ reference pose: geometry + bind
g.pose(*REF); look.gear(g)
bpy.context.view_layer.update()
REFM = bone_mats()
dg = bpy.context.evaluated_depsgraph_get()
mats = {}            # material key -> {name, color, smooth, metal}
sub = {}             # material key -> list of tris
V, N, UV, BW = [], [], [], []
VKEY = {}


def mat_key(m):
    if m is None: return 'grey'
    if m.name.startswith('crew_soldier'): return 'soldier'
    k = re.sub(r'[^A-Za-z0-9_]', '_', m.name)
    if k not in mats:
        col, rough, metal = (0.5, 0.5, 0.5), 0.6, 0.0
        if m.use_nodes:
            nt = m.node_tree; b = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            ramp = next((n for n in nt.nodes if n.type == 'VALTORGB'), None)
            if ramp is not None:
                c0, c1 = ramp.color_ramp.elements[0].color, ramp.color_ramp.elements[-1].color
                col = tuple((c0[i] + c1[i]) / 2 for i in range(3))
            elif b is not None and not b.inputs['Base Color'].is_linked:
                col = tuple(b.inputs['Base Color'].default_value[:3])
            if b is not None:
                rough = b.inputs['Roughness'].default_value; metal = b.inputs['Metallic'].default_value
        mats[k] = dict(color=[round(x, 4) for x in col], smooth=round(1.0 - rough, 3), metal=round(metal, 3))
    return k


def add_mesh(obj, weights_fn, decimate=None):
    ev = obj.evaluated_get(dg); me = ev.to_mesh(); W = obj.matrix_world
    me.calc_loop_triangles()
    uvl = me.uv_layers.active.data if me.uv_layers.active else None
    nm = W.to_3x3().inverted().transposed()
    for lt in me.loop_triangles:
        m = obj.material_slots[lt.material_index].material if obj.material_slots else None
        k = mat_key(m); tri = []
        for li in lt.loops:
            vi = me.loops[li].vertex_index; p = W @ me.vertices[vi].co
            n = (nm @ (me.corner_normals[li].vector if hasattr(me, 'corner_normals') else me.loops[li].normal)).normalized()
            rec = (tuple(U(p)), tuple(U(n)), tuple(round(c, 4) for c in uvl[li].uv) if uvl else (0, 0))
            w = weights_fn(vi, p); key = rec + (str(w),)
            idx = VKEY.get(key)
            if idx is None:
                idx = len(V); VKEY[key] = idx
                V.append(list(rec[0])); N.append([round(x, 3) for x in rec[1]]); UV.append(list(rec[2])); BW.append(w)
            tri.append(idx)
        sub.setdefault(k, []).append([tri[0], tri[2], tri[1]])        # mirrored axes: flip winding
    ev.to_mesh_clear()


# body: armature-deformed at the reference pose, its own weights (top 4)
gi = {vg.index: vg.name for vg in so.vertex_groups}
src = so.data.vertices
def body_w(vi, p):
    ws = sorted(((gg.weight, gi[gg.group]) for gg in src[vi].groups if gi.get(gg.group) in DEF and gg.weight > 0.001), reverse=True)[:4]
    t = sum(w for w, _ in ws) or 1.0
    return [[BONES.index(n), round(w / t, 4)] for w, n in ws] or [[BONES.index('hips'), 1.0]]
add_mesh(so, body_w)

# triangle diet (Nick 04:36, no visible change): merge coplanar triangles on the rigid pieces (planar dissolve 0.5 deg, never
# across materials, seams or sharp edges). Their weights depend only on position, so the vertex renumbering is harmless.
_n = 0
for o in list(g.gun_objs) + list(g.harness_objs) + list(getattr(g, 'arm_objs', [])):
    if o.type == 'MESH' and len(o.data.polygons) > 3:
        d = o.modifiers.new('diet', 'DECIMATE'); d.decimate_type = 'DISSOLVE'; d.angle_limit = math.radians(0.5)
        d.delimit = {'MATERIAL', 'SEAM', 'SHARP'}; _n += 1
dg = bpy.context.evaluated_depsgraph_get(); dg.update(); print('planar diet on', _n, 'objects')
# rigid pieces: gun + gloves -> gun; harness / chest kit -> their parent bone; support arm pieces -> nearest link
for o in g.gun_objs:
    if o.type == 'MESH': add_mesh(o, lambda vi, p: [[BONES.index('gun'), 1.0]])
for o in g.harness_objs:
    if o.type == 'MESH':
        bn = o.parent_bone if o.parent_type == 'BONE' and o.parent_bone in BONES else 'chest'
        add_mesh(o, lambda vi, p, bn=bn: [[BONES.index(bn), 1.0]])
if KIND == 'mg':
    h, e, b = ARMREC['hip'], ARMREC['elbow'], ARMREC['bot']
    def seg_d(p, a0, a1):
        d = a1 - a0; t = max(0.0, min(1.0, (p - a0).dot(d) / d.length_squared)); return (a0 + d * t - p).length
    def arm_w(vi, p):
        du, dl = seg_d(p, h, e), seg_d(p, e, b)
        if (p - b).length < 0.03 or p.z > b.z + 0.005 and (p - b).length < 0.12: return [[BONES.index('gun'), 1.0]]
        return [[BONES.index('armU' if du <= dl else 'armL'), 1.0]]
    for o in g.arm_objs:
        if o.type == 'MESH': add_mesh(o, arm_w)
# neck gaiter: collar ring on the chest -> head ring on the head
if hasattr(g, 'neck_obj'):
    hz = (ao.matrix_world @ pb['head'].head).z
    cz = hz - 0.06
    def neck_w(vi, p):
        f = max(0.0, min(1.0, (p.z - cz) / 0.13))
        return [[BONES.index('chest'), round(1 - f, 3)], [BONES.index('head'), round(f, 3)]] if f < 0.999 else [[BONES.index('head'), 1.0]]
    add_mesh(g.neck_obj, neck_w)
# wrist gaiters: sleeve end on the forearm, cuff end on the weapon
for o, s in zip(getattr(g, 'wrist_objs', []), 'RL'):
    cuff = g.GM @ (g.wR if s == 'R' else g.wL)
    el = ao.matrix_world @ pb[f'forearm_{s}'].head
    L = max(1e-4, (cuff - el).length)
    def wr_w(vi, p, cuff=cuff, el=el, s=s):
        f = max(0.0, min(1.0, 1.0 - (p - cuff).length / 0.06))
        return [[BONES.index(f'forearm_{s}'), round(1 - f, 3)], [BONES.index('gun'), round(f, 3)]]
    add_mesh(o, wr_w)
print('verts', len(V), 'tris', sum(len(t) for t in sub.values()), 'submeshes', {k: len(t) for k, t in sub.items()})

# ------------------------------------------------------------------ pose table
alist = []
n = max(2, int(round(2 * AR / STEP)))
for i in range(n + 1): alist.append(round(-AR + 2 * AR * i / n, 3))
table = []
worst = 0.0
for E in ELEV:
    row = []
    # warm start each row from the reference side, then walk left to right (continuity penalty in the rig)
    g._prev_el = {}; g._prev_hand = {}
    g.pose(*vis(alist[0], E))
    prev = None
    for A in alist:
        av, ev = vis(A, E)
        miss = g.pose(av, ev)
        bm = bone_mats()
        row.append([to_u(bm[nn]) for nn in BONES])
        els = {s_: g._prev_el[s_].copy() for s_ in 'RL'}
        if prev: worst = max(worst, max((els[s_] - prev[s_]).length for s_ in 'RL'))
        prev = els
        print(f'E {E:5.1f} A {A:7.2f} -> ({av:6.1f},{ev:5.1f}) miss R {miss["R"]:.3f} L {miss["L"]:.3f}', flush=True)
    table.append(row)
print('max elbow step between neighbours', round(worst, 3))

out = dict(kind=KIND, bones=BONES, parents=[], arcs=[AFR, AFL, AR], A=alist, E=ELEV, ref=[to_u(REFM[nn]) for nn in BONES],
           verts=V, normals=N, uvs=UV, weights=BW, submeshes=[dict(mat=k, tris=[i for t in tris for i in t]) for k, tris in sub.items()],
           mats=mats, table=table)
json.dump(out, open(OUT, 'w'), separators=(',', ':'))
print('wrote', OUT, len(V), 'verts', len(BONES), 'bones', len(alist), 'x', len(ELEV), 'samples')
