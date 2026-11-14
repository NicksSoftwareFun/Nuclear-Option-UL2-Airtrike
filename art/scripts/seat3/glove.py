"""Organic gloved hands for the seat 3 kits (Nick 2026-11-14: "hands need a massive improvement in detail").
A proper hand skeleton (palm, 4 three-phalanx fingers, opposable thumb with thenar pad) is built from capsules, every
finger joint curled until it touches the held object (BVH contact solve, so fingers wrap whatever shape they hold),
then the capsules are fused with a voxel remesh, smoothed and decimated into one glove shell. Glove details on top:
knuckle armour strip, padded backs on the first finger segments, a cuff with a hook-and-loop strap.

Same call as mg_model._glove_wrap: glove(tag, top, bot, side, fwd, rg, M, arm_dir, thumb) where top->bot is the grip
axis, rg the grip radius, side the hand (+1 right, -1 left), fwd the direction the fingers wrap toward. The index
finger and thumb sit at the 'top' end. Returns the cuff point (gun frame) for the arm IK."""
import bpy, bmesh, math
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
import ul2lib
from ul2lib import tube, ball, box, SEAT, DARK

# gloved hand, medium-large (metres)
PALM_W, PALM_L, PALM_T = 0.086, 0.096, 0.030
FING = {   # x offset from the knuckle-line centre toward the thumb side, knuckle y, segment lengths, root radius
    'index': (0.0305, 0.000, (0.044, 0.027, 0.023), 0.0110),
    'middle': (0.0100, 0.004, (0.048, 0.030, 0.024), 0.0112),
    'ring': (-0.0105, 0.000, (0.045, 0.028, 0.023), 0.0107),
    'little': (-0.0300, -0.009, (0.035, 0.022, 0.020), 0.0096),
}
SPLIT = (0.42, 0.36, 0.22)         # how a finger's total curl spreads over its three joints
VOXEL = 0.0021
LOG = []                           # build report (curl angles, triangle counts)


def _rot(v, axis, ang): return Quaternion(axis, ang) @ v


def _chain(root, d0, axis, lens, ang, split=SPLIT):
    pts = [root.copy()]; d = d0.copy(); acc = 0.0
    for L, s in zip(lens, split):
        acc += ang * s; dd = _rot(d0, axis, acc); pts.append(pts[-1] + dd * L)
    return pts


def _hits(bvh, pts, rads, margin):
    for j, ((a, b), r) in enumerate(zip(zip(pts, pts[1:]), rads)):
        for k in range(2 if j == 0 else 0, 6):           # the knuckle itself sits on the palm, which may touch the grip
            p = a + (b - a) * (k / 5.0)
            hit = bvh.find_nearest(p)
            if hit[0] is not None and hit[3] < r * margin: return True
    return False


def _curl(bvh, root, d0, axis, lens, rads, amax, margin=0.92):
    """largest curl (radians) that keeps every segment clear of the held object; fingers that never touch stop at amax.
    0.15.53: when the straight finger already touches (a fat foregrip), keep curling until it comes clear and then wrap
    on from there, instead of giving up at the minimum curl"""
    if bvh is None: return amax
    step = math.radians(2.0); a = 0.0; best = None; clear0 = None
    while a <= amax:
        hit = _hits(bvh, _chain(root, d0, axis, lens, a), rads, margin)
        if not hit:
            if clear0 is None: clear0 = a
            best = a
        elif clear0 is not None: break
        a += step
    if best is None: return math.radians(90)
    return best


def _target_bvh(M):
    """everything already built in the current part (the weapon / binoculars), in gun space"""
    Mi = M.inverted(); V, F = [], []
    for o in ul2lib.PARTS[ul2lib.CUR[0]]['objs']:
        if o.type != 'MESH' or o.name.startswith('glove'): continue
        W = Mi @ o.matrix_world; base = len(V)
        V += [W @ v.co for v in o.data.vertices]; F += [[base + i for i in p.vertices] for p in o.data.polygons]
    return BVHTree.FromPolygons(V, F) if F else None


def glove(tag, top, bot, side, fwd, rg, M, arm_dir, thumb='top', palm_ang=-40.0, report=None):
    if report is None: report = LOG
    M = Matrix(M) if M is not None else Matrix.Identity(4)
    top, bot = Vector(top), Vector(bot); ax = (bot - top).normalized()
    eo = Vector((side, 0, 0)); eo = (eo - ax * eo.dot(ax)).normalized()
    ef = Vector(fwd); ef = (ef - ax * ef.dot(ax) - eo * ef.dot(eo)).normalized()
    pa = math.radians(palm_ang)
    n = (math.cos(pa) * eo + math.sin(pa) * ef).normalized()                    # outward from the grip at the palm
    Y = (-math.sin(pa) * eo + math.cos(pa) * ef).normalized()                   # fingers start along the wrap direction
    Z = n; X = Y.cross(Z)
    hs = 1 if (-X).dot(-ax) > 0 else -1                                         # thumb side points at the 'top' end
    T = -X * hs
    if report is not None: report.append(f'{tag}: hand {"R" if side > 0 else "L"} built as {"R" if hs > 0 else "L"}')
    cen = top + ax * 0.050
    O = cen + n * (rg + PALM_T * 0.5)                                            # palm centre, mid thickness
    bvh = _target_bvh(M)
    tmp = []                                                                     # capsules fused below
    def cap(p, q, r0, r1=None):
        r1 = r0 if r1 is None else r1
        tmp.append(tube(f'_g_{len(tmp)}', p, q, r0, SEAT, segs=12, r1=r1, M=M))
        tmp.append(ball(f'_g_{len(tmp)}', tuple(q), (r1, r1, r1), SEAT, M=M, seg=12))
    # palm: a thick rounded slab, slightly cupped toward the grip at the heel
    K0 = O + Y * (PALM_L * 0.48)
    for fx in (-0.028, -0.009, 0.010, 0.028):
        a = O - Y * (PALM_L * 0.45) + T * fx * 0.85 - Z * 0.002; b = K0 + T * fx - Z * 0.001
        cap(a, b, PALM_T * 0.50, PALM_T * 0.46)
    tmp.append(ball('_g_heel', tuple(O - Y * 0.030 - T * 0.020 - Z * 0.004), (0.026, 0.030, 0.016), SEAT, M=M, seg=14))
    # fingers
    curls = {}
    for nm, (fx, fy, lens, r) in FING.items():
        root = K0 + T * fx + Y * fy
        rads = (r, r * 0.93, r * 0.86)
        amax = math.radians(250 if nm != 'little' else 260)
        ang = _curl(bvh, root, Y, -X, lens, rads, amax)
        ang = max(ang, math.radians(25))                                          # never dead straight
        curls[nm] = round(math.degrees(ang))
        P = _chain(root, Y, -X, lens, ang)
        tmp.append(ball(f'_g_k{nm}', tuple(root), (r * 1.08, r * 1.08, r * 1.08), SEAT, M=M, seg=12))
        for (p, q), r0, r1 in zip(zip(P, P[1:]), rads, rads[1:] + (r * 0.80,)): cap(p, q, r0, r1)
        tmp.append(ball(f'_g_t{nm}', tuple(P[-1] + (P[-1] - P[-2]).normalized() * 0.002), (r * 0.82,) * 3, SEAT, M=M, seg=12))
    # thumb: from the heel of the palm on the thumb side, opposing the fingers
    if thumb == 'top':        # pistol grip / binoculars: wraps the opposite way round the grip from the fingers
        tb = O + T * 0.036 - Y * 0.018 + Z * 0.002; d0 = (-Y * 0.30 + T * 0.80 - Z * 0.40).normalized()   # 0.15.53: starts clear of the grip
    else:                     # foregrip: across the front face, above the index finger
        tb = O + T * 0.040 - Y * 0.010 + Z * 0.002; d0 = (Y * 0.55 + T * 0.65 - Z * 0.35).normalized()
    t_ax = d0.cross(-Z); t_ax = t_ax.normalized() if t_ax.length > 1e-4 else -X
    tl = (0.038, 0.031, 0.026); tr = (0.0135, 0.0125, 0.0115)
    ta = max(_curl(bvh, tb, d0, t_ax, tl, tr, math.radians(150), margin=0.95), math.radians(15))
    curls['thumb'] = round(math.degrees(ta))
    TP = _chain(tb, d0, t_ax, tl, ta, (0.25, 0.40, 0.35))
    for (p, q), r0, r1 in zip(zip(TP, TP[1:]), tr, tr[1:] + (0.0100,)): cap(p, q, r0, r1)
    tmp.append(ball('_g_tt', tuple(TP[-1]), (0.0100,) * 3, SEAT, M=M, seg=12))
    tmp.append(ball('_g_thenar', tuple((tb + TP[1]) / 2 - Z * 0.002), (0.020, 0.024, 0.016), SEAT, M=M, seg=12))
    # wrist toward the forearm
    ad = Vector(arm_dir).normalized(); W0 = O - Y * (PALM_L * 0.50) + Z * 0.002
    W1 = W0 + (ad * 0.7 - Y * 0.3).normalized() * 0.030
    cap(W0, W1, 0.029, 0.0295)                                                   # full wrist, no pinched stalk
    # fuse: join the capsules into one mesh, voxel remesh, smooth, decimate
    bm = bmesh.new()
    for o in tmp:
        me = o.data.copy(); me.transform(o.matrix_world); bm.from_mesh(me); bpy.data.meshes.remove(me)
    objs = ul2lib.PARTS[ul2lib.CUR[0]]['objs']
    for o in tmp:
        if o in objs: objs.remove(o)
        bpy.data.objects.remove(o)
    me = bpy.data.meshes.new(tag + '_raw'); bm.to_mesh(me); bm.free()
    raw = bpy.data.objects.new(tag + '_raw', me); ul2lib.coll('WORK').objects.link(raw)
    rm = raw.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = VOXEL; rm.adaptivity = 0.0
    sm = raw.modifiers.new('sm', 'SMOOTH'); sm.factor = 0.7; sm.iterations = 6
    dc = raw.modifiers.new('dc', 'DECIMATE'); dc.ratio = 0.10
    dg = bpy.context.evaluated_depsgraph_get(); ev = raw.evaluated_get(dg)
    bm = bmesh.new(); bm.from_mesh(ev.to_mesh()); raw.evaluated_get(dg).to_mesh_clear()
    bpy.data.objects.remove(raw); bpy.data.meshes.remove(me)
    g = ul2lib.finish(tag, bm, SEAT, True, auto=False)
    # the wear: knuckle armour strip across the back of the knuckles, hook-and-loop wrist strap, cuff
    kc = K0 + Z * (PALM_T * 0.5 + 0.004) - Y * 0.004
    Rk = Matrix((T, Y, Z)).transposed()
    if Rk.determinant() < 0: Rk = Matrix((-T, Y, Z)).transposed()
    # (0.15.53 review: the flat knuckle slab did not follow the hand, removed)
    c1 = W1 + (W1 - W0).normalized() * 0.040
    tube(f'{tag}_cuff', tuple(W1 - (W1 - W0).normalized() * 0.004), tuple(c1), 0.0305, DARK, segs=16, r1=0.0315, M=M)
    ball(f'{tag}_cuff_cap', tuple(c1), (0.0315, 0.0315, 0.0315), DARK, M=M, seg=16)        # closed cuff end (no hollow tube from the front)
    sd = (W1 - W0).normalized(); sz = (Z - sd * Z.dot(sd)).normalized()
    Rs = Matrix((sd.cross(sz).normalized(), sd, sz)).transposed()
    box(f'{tag}_cuff_strap', tuple(W1 + sd * 0.012 + sz * 0.030), (0.040, 0.020, 0.005), DARK, rot=tuple(Rs.to_euler()), bevel=0.0015, M=M)
    if report is not None: report.append(f'{tag}: curls {curls}, tris {sum(len(p.vertices) - 2 for p in g.data.polygons)}')
    return c1
