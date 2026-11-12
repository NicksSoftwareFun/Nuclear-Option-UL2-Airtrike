"""Standalone gunner pose renders. Usage: python3 -I gunner_test.py -- <outdir> [A E]"""
import bpy, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]; out = a[0]
A = float(a[1]) if len(a) > 1 else 0.0; E = float(a[2]) if len(a) > 2 else 0.0
bpy.ops.wm.read_factory_settings(use_empty=True)
from gunner_rig import Gunner
from render_lib import setup, shot
g = Gunner(Matrix.Translation((0, 0, 0.55)))
m = g.pose(A, E); print('wrist miss', {k: round(v, 3) for k, v in m.items()}, 'T', g.T, 'arm', round(g.arm_len, 3))
sc, cam, gg = setup(20)
c = Vector((0.05, 0.10, 0.90)); tag = f'{int(A)}_{int(E)}'
shot(sc, cam, f'{out}/gp_{tag}_3q.png', c + Vector((1.5, 1.9, 0.55)), c, 45, res=(800, 800), scale=0.8)
shot(sc, cam, f'{out}/gp_{tag}_side.png', c + Vector((2.6, 0.05, 0.10)), c, 45, res=(800, 800), scale=0.8)
shot(sc, cam, f'{out}/gp_{tag}_back.png', c + Vector((0.6, -2.2, 0.9)), c + Vector((0, 0, 0.0)), 45, res=(800, 800), scale=0.8)
shot(sc, cam, f'{out}/gp_{tag}_front.png', c + Vector((-0.6, 2.6, 0.5)), c, 45, res=(800, 800), scale=0.8)
