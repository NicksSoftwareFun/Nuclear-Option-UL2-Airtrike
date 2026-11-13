import bpy, sys, math, bmesh
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from render_lib import *
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); out = a[1]; only = set(a[2:])
for o in bpy.data.objects:
    if o.name.startswith(('REF_',)) or o.name in ('x_harness3', 'x_rotordisc', 'x_propdisc'): o.hide_render = True
sc, cam, g = setup(48)
pbl = bpy.data.objects.get('x_propblade')
if pbl:
    for k in (1, 2):
        c = pbl.copy(); sc.collection.objects.link(c)
        piv = Vector((0, -2.22, 1.74))
        c.matrix_world = Matrix.Translation(piv) @ Matrix.Rotation(math.radians(120 * k), 4, 'Y') @ Matrix.Translation(-piv) @ pbl.matrix_world

def mk(name, col, rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal; return m
POD = mk('ecm', (0.32, 0.34, 0.33), 0.5, 0.2); STRAP = mk('strap', (0.85, 0.55, 0.05), 0.8); BUCK = mk('buckle', (0.6, 0.6, 0.62), 0.3, 1.0)
# stock ECM pod mesh (Unity x right, y up, z forward) -> Blender (x, y fwd, z up)
V, F = [], []
for l in open('/mnt/project-files/ul2/ecm_fit/ecmpod1.obj'):
    p = l.split()
    if not p: continue
    if p[0] == 'v': x, y, z = map(float, p[1:4]); V.append((-x, z, y))
    elif p[0] == 'f': F.append([int(t.split('/')[0]) - 1 for t in p[1:]])
me = bpy.data.meshes.new('ecm'); me.from_pydata(V, [], F); me.materials.append(POD)
pod = bpy.data.objects.new('ECMPod1', me); sc.collection.objects.link(pod)
for f in me.polygons: f.use_smooth = False
ymin = min(v[1] for v in V)
def place(tilt_side, tilt_back, base):
    # stand it nose up, mounting face against the seat back (aft), aft end down; then lean
    R = Matrix.Rotation(math.radians(tilt_side), 4, 'Y') @ Matrix.Rotation(math.radians(90 + tilt_back), 4, 'X')
    pod.matrix_world = Matrix.Translation(base) @ R @ Matrix.Translation((0, -ymin, 0))
    bpy.context.view_layer.update()
    W = [pod.matrix_world @ Vector(v) for v in V]
    print('pod top z %.2f x %.2f..%.2f y %.2f..%.2f' % (max(w.z for w in W), min(w.x for w in W), max(w.x for w in W), min(w.y for w in W), max(w.y for w in W)))
    return R
def strap(name, c, R, w, d, h=0.05):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1); bmesh.ops.transform(bm, matrix=Matrix.Translation(c) @ R @ Matrix.Diagonal((w, d, h, 1)), verts=bm.verts)
    m = bpy.data.meshes.new(name); bm.to_mesh(m); m.materials.append(STRAP); o = bpy.data.objects.new(name, m); sc.collection.objects.link(o); return o
def S(name, loc, look, lens=40, res=(1400, 1000)):
    if only and name not in only: return
    shot(sc, cam, f'{out}/{name}.png', loc, look, lens=lens, res=res)
mode = a[2] if len(a) > 2 else 'A'
if mode == 'A':
    R = place(0, 0, Vector((0, -0.59, 1.21)))
    tag = 'A_upright'
elif mode == 'C':
    B = float(a[3]); base = Vector((0, float(a[4]), float(a[5])))
    R = place(0, B, base)
    ax = Vector((R @ Vector((0, 1, 0, 0)))[:3])     # pod long axis (up/aft)
    nz = Vector((R @ Vector((0, 0, 1, 0)))[:3])     # pod depth axis
    W = [pod.matrix_world @ Vector(v) for v in V]
    lo = min(w.dot(nz) for w in W); hi = max(w.dot(nz) for w in W); print('depth range', lo, hi, 'axis', ax)
    import importlib.util; sp = importlib.util.spec_from_file_location('straps', '/mnt/project-files/ul2/ecm_fit/straps.py'); ST = importlib.util.module_from_spec(sp); sp.loader.exec_module(ST)
    fwd = Vector((0, 1, 0))
    EDG = [(W[e.vertices[0]], W[e.vertices[1]]) for e in me.edges]
    so = bpy.data.objects['seat3']; SW = [so.matrix_world @ v.co for v in so.data.vertices]
    SEAT = [(SW[e.vertices[0]], SW[e.vertices[1]]) for e in so.data.edges if SW[e.vertices[0]].y < -0.7 or SW[e.vertices[1]].y < -0.7]
    mz = min(W, key=lambda w: w.z); print('pod low point', mz)
    # rubber chock wedge on the seat 3 foot plate under the tilted foot
    PAD = mk('pad', (0.05, 0.05, 0.05), 0.9)
    foot = [w for w in W if (w - base).dot(ax) < 0.02]
    fy0, fy1 = min(w.y for w in foot), max(w.y for w in foot)
    bmw = bmesh.new(); zt = lambda y: min(w.z for w in foot if abs(w.y - y) < 0.06) if any(abs(w.y - y) < 0.06 for w in foot) else mz.z
    for x in (-0.14, 0.14):
        pass
    vv = [bmw.verts.new((x, y, z)) for x in (-0.14, 0.14) for (y, z) in ((fy0 - 0.03, 0.905), (fy1 + 0.03, 0.905), (fy1 + 0.03, mz.z), (fy0 - 0.03, mz.z + (fy1 - fy0 + 0.06) * math.tan(math.radians(B))))]
    for f in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)): bmw.faces.new([vv[i] for i in f])
    TRAY = mk('tray', (0.42, 0.43, 0.42), 0.45, 0.6)
    bmt = bmesh.new(); bmesh.ops.create_cube(bmt, size=1); bmesh.ops.transform(bmt, matrix=Matrix.Translation((0, (fy0 + fy1) / 2, 0.8985)) @ Matrix.Diagonal((0.66, fy1 - fy0 + 0.12, 0.013, 1)), verts=bmt.verts)
    for sx in (-1, 1):   # U clamps over the side rails
        for yy in (fy0 - 0.03, fy1 + 0.03):
            bmesh.ops.create_cube(bmt, size=1, matrix=Matrix.Translation((sx * 0.30, yy, 0.885)) @ Matrix.Diagonal((0.05, 0.03, 0.045, 1)))
    met = bpy.data.meshes.new('tray'); bmt.to_mesh(met); met.materials.append(TRAY); sc.collection.objects.link(bpy.data.objects.new('tray', met))
    mew = bpy.data.meshes.new('chock'); bmw.to_mesh(mew); mew.materials.append(PAD); sc.collection.objects.link(bpy.data.objects.new('chock', mew))
    # lower strap: hooks on the seat 3 back frame edges; upper strap: hooks on D-rings clamped to the mast
    for k, (d, ancs) in enumerate(((0.62, None), (1.42, None))):
        c = base + ax * d
        if k == 0: A = [Vector((-0.085, -1.05, c.z)), Vector((0.085, -1.05, c.z))]
        else: A = [Vector((-0.11, -1.00, c.z)), Vector((0.16, -1.00, c.z))]
        # put anchors in the strap plane
        A = [p - ax * (p - c).dot(ax) for p in A]
        ST.strap(sc, f's{k}', EDG, c, ax, fwd, A, ratchet_side=1, extra=SEAT if k == 0 else ())
        for q, p in enumerate(A):   # anchor bar / D-ring the hook bites on
            if False: ST.tube(sc, f'abar{k}{q}', [p - ax * 0.09 + Vector((0, 0.03, 0)), p - ax * 0.09, p + ax * 0.09, p + ax * 0.09 + Vector((0, 0.03, 0))], 0.010, ST.STEEL)
            else:
                ST.tube(sc, f'dring{k}{q}', [p - ax * 0.05, p - ax * 0.05 - Vector((0, 0.04, 0)), p + ax * 0.05 - Vector((0, 0.04, 0)), p + ax * 0.05, p - ax * 0.05], 0.007, ST.STEEL)
    mx = Vector((0, -1.03, 0)); 
    for q, zz in enumerate((-0.06, 0.06)):
        cc = base + ax * 1.42; ST.tube(sc, f'mastclamp{q}', [Vector((0.15 * math.cos(math.radians(a2)) * 0.6 + 0.03, mx.y + 0.07 * math.sin(math.radians(a2)), cc.z + zz)) for a2 in range(0, 380, 30)], 0.008, ST.STEEL)
    from mathutils.bvhtree import BVHTree
    def wb(o):
        bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world); t = BVHTree.FromBMesh(bm); bm.free(); return t
    pb = wb(pod)
    for o in bpy.data.objects:
        if o.type != 'MESH' or o is pod or o.name.startswith(('strap', 'tie', 'web', 'hook', 'ratchet', 'abar', 'dring', 'mastclamp')) or o.hide_render: continue
        h = pb.overlap(wb(o))
        if h: print('HIT', o.name, len(h))
    for w in [o for o in bpy.data.objects if o.name.startswith('web')]:
        wt = wb(w)
        for o in bpy.data.objects:
            if o.type == 'MESH' and not o.name.startswith(('web', 'hook', 'ratchet', 'abar', 'dring', 'strap', 'tie')) and o.name != 'ground' and not o.hide_render and wt.overlap(wb(o)): print('WEBHIT', w.name, o.name)
    HIDE = a[6:] if len(a) > 6 else []
    for n in HIDE:
        if n in bpy.data.objects: bpy.data.objects[n].hide_render = True
    tag = 'C_back%d' % B
else:
    TH = float(a[3]) if len(a) > 3 else 38
    R = place(TH, 0, Vector((-0.10, -0.59, 1.21)))
    ax = R @ Vector((0, 0, 1, 0)); ax = Vector(ax[:3])
    for k, d in enumerate((0.35, 0.95)):
        c = Vector((-0.10, -0.59, 1.21)) + ax * d + Vector(((R @ Vector((0, 1, 0, 0)))[:3])) * 0.0
        o = strap(f'strap{k}', c + Vector((0, 0.17, 0)), R, 0.30, 0.56, 0.05)
    # tie-down straps from the pod down to the seat frame rails on the right
    for k, (p0, p1) in enumerate(((Vector((-0.10, -0.59, 1.21)) + ax * 0.95 + Vector((0.12, 0.2, 0)), Vector((0.29, -0.30, 0.76))), (Vector((-0.10, -0.59, 1.21)) + ax * 0.95 + Vector((0.12, -0.05, 0)), Vector((0.29, -0.90, 0.76))))):
        d = p1 - p0; L = d.length; Rq = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        strap(f'tie{k}', (p0 + p1) / 2, Rq, 0.035, 0.006, L)
    tag = 'B_leaned%d' % TH
for o in bpy.data.objects:
    if o.name.startswith('REF_'): o.hide_render = True
import os
if os.environ.get('NR'): sys.exit(0)
if not os.environ.get('FO'): shot(sc, cam, f'{out}/{tag}_3q.png', (2.3, -2.9, 3.0), (0, -0.55, 1.8), lens=32, res=(1400, 1000))
shot(sc, cam, f'{out}/{tag}_foot.png', (0.75, 0.25, 1.45), (0, -0.45, 1.0), lens=30, res=(1400, 1000))
if os.environ.get('FO'): sys.exit(0)
shot(sc, cam, f'{out}/{tag}_close.png', (1.5, 0.2, 2.2), (0, -0.7, 1.8), lens=35, res=(1400, 1000))
shot(sc, cam, f'{out}/{tag}_side.png', (4.2, -0.6, 2.0), (0, -0.6, 1.9), lens=30, res=(1400, 1000))
