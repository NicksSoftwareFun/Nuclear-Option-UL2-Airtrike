"""Pose test for the rigged seat 3 soldier: hands to a gun's grips by IK. Usage: python3 -I pose_test.py -- <rig.blend> <outdir> [grip x y z] [fore x y z]"""
import bpy, sys, math; sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from render_lib import setup, shot
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); out = a[1]
g = Vector(map(float, a[2:5])) if len(a) > 4 else Vector((0.12, 0.36, 0.38))
f = Vector(map(float, a[5:8])) if len(a) > 7 else Vector((-0.06, 0.62, 0.42))
ao = bpy.data.objects['UL2_soldier_rig']; ao.location = (0, 0, 0.55)
for s, t in (('R', g), ('L', f)):
    e = bpy.data.objects.new(f'ik_{s}', None); bpy.context.scene.collection.objects.link(e); e.parent = ao; e.location = t
    c = ao.pose.bones[f'forearm_{s}'].constraints.new('IK'); c.target = e; c.chain_count = 2
    pole = bpy.data.objects.new(f'pole_{s}', None); bpy.context.scene.collection.objects.link(pole); pole.parent = ao
    pole.location = ((0.5 if s == 'R' else -0.5), -0.1, 0.0); c.pole_target = pole; c.pole_angle = 0
bpy.context.view_layer.update()
sc, cam, gg = setup(24)
shot(sc, cam, f'{out}/pose_3q.png', (1.6, 2.2, 1.4), (0, 0.15, 0.85), 40, res=(900, 900), scale=0.7)
shot(sc, cam, f'{out}/pose_side.png', (-2.6, 0.2, 1.0), (0, 0.15, 0.85), 40, res=(900, 900), scale=0.7)
shot(sc, cam, f'{out}/pose_hands.png', (0.9, 1.3, 1.1), (0.05, 0.25, 0.85), 50, res=(900, 900), scale=0.7)
