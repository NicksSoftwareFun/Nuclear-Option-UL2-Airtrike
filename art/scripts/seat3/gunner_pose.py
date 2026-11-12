"""Seat 3 gunner holding the LMG on the harness arm. Usage:
python3 -I gunner_pose.py -- <rig.blend> <outdir> [twist_deg] [gx gy gz]   (gun origin in rig space)"""
import bpy, sys, math
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); out = a[1]
import ul2lib; from ul2lib import part
import mg_model as mg
from render_lib import setup, shot
TW = float(a[2]) if len(a) > 2 else 40.0
G = Vector(map(float, a[3:6])) if len(a) > 5 else Vector((0.21, 0.0, 0.25))
HR = (float(a[6]), float(a[7])) if len(a) > 7 else (-90.0, 30.0)   # hand roll R, L (deg about the bone)
HP = (float(a[8]), float(a[9])) if len(a) > 9 else (50.0, -30.0)   # hand pitch R, L
ONLY = '--gun' in sys.argv

ao = bpy.data.objects['UL2_soldier_rig']; ao.location = (0, 0, 0.55)
# the MG gunner has no rifle standing between his knees (Ibis troop rifle pieces, all within x -0.02..0.07 below the belt)
import bmesh
so = bpy.data.objects['UL2_soldier']; bm = bmesh.new(); bm.from_mesh(so.data); bm.verts.ensure_lookup_table(); seen = set(); kill = []
for v in bm.verts:
    if v.index in seen: continue
    st = [v]; comp = []; seen.add(v.index)
    while st:
        q = st.pop(); comp.append(q)
        for e in q.link_edges:
            w = e.other_vert(q)
            if w.index not in seen: seen.add(w.index); st.append(w)
    lo = [min(q.co[i] for q in comp) for i in range(3)]; hi = [max(q.co[i] for q in comp) for i in range(3)]
    if lo[0] > -0.02 and hi[0] < 0.07 and lo[1] > 0.15 and hi[2] < 0.25 and lo[2] < -0.1: kill += comp
bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(so.data); bm.free(); print('rifle verts removed', len(kill))
pb = ao.pose.bones
for n, f in (('spine', 1.0), ('chest', 0.0)):
    pb[n].rotation_mode = 'XYZ'; pb[n].rotation_euler = (0, -math.radians(TW * f), 0)
bpy.context.view_layer.update()

RIG = ao.matrix_world.copy()
GM = RIG @ Matrix.Translation(G)                       # gun frame in world (points along rig +Y)
gun = lambda p: GM @ Vector(p)
part('seat3_mg'); mg.build_gun(GM)
tR = gun(mg.GRIP + Vector((0.006, -0.045, 0.005)))
tL = gun(mg.FORE + Vector((-0.012, -0.070, 0.0)))
for s, t, pole in (('R', tR, (0.7, -0.4, -0.3)), ('L', tL, (-0.6, 0.1, -0.4))):
    e = bpy.data.objects.new(f'ik_{s}', None); bpy.context.scene.collection.objects.link(e); e.location = t
    c = pb[f'forearm_{s}'].constraints.new('IK'); c.target = e; c.chain_count = 2
    p = bpy.data.objects.new(f'pole_{s}', None); bpy.context.scene.collection.objects.link(p); p.parent = ao; p.location = pole
    c.pole_target = p; c.pole_angle = 0
for s, r in (('R', HR[0]), ('L', HR[1])):
    pb[f'hand_{s}'].rotation_mode = 'XYZ'; pb[f'hand_{s}'].rotation_euler = (math.radians(HP[0 if s == 'R' else 1]), math.radians(r), 0)
bpy.context.view_layer.update()
for s, t in (('R', tR), ('L', tL)):
    w = ao.matrix_world @ pb[f'hand_{s}'].head
    print(f'wrist {s} miss {(w - t).length:.3f} m')
hip = RIG @ Vector((0.22, -0.04, 0.14)); post = gun(mg.MOUNT + Vector((0, 0, -0.028)))
elbow = (hip + post) / 2 + (RIG.to_3x3() @ Vector((0.15, 0.02, -0.06)))
mg.build_arm(hip, elbow, post)
sc, cam, gg = setup(24)
c = RIG @ Vector((0.05, 0.10, 0.30))
if not ONLY: shot(sc, cam, f'{out}/mg_3q.png', c + Vector((1.5, 1.9, 0.55)), c, 45, res=(900, 900), scale=0.8)
if not ONLY: shot(sc, cam, f'{out}/mg_side.png', c + Vector((2.6, 0.05, 0.10)), c, 45, res=(900, 900), scale=0.8)
if not ONLY: shot(sc, cam, f'{out}/mg_front.png', c + Vector((-0.6, 2.6, 0.5)), c, 45, res=(900, 900), scale=0.8)
shot(sc, cam, f'{out}/mg_gun.png', gun((0.35, 0.15, 0.10)) + Vector((0.55, 0.25, 0.25)), gun((0, 0.15, 0.03)), 45, res=(900, 600), scale=0.8)
if not ONLY: shot(sc, cam, f'{out}/mg_back.png', c + Vector((0.4, -2.2, 0.9)), c + Vector((0, 0, 0.25)), 45, res=(900, 900), scale=0.8)
bpy.ops.wm.save_as_mainfile(filepath=f'{out}/gunner_pose.blend')
