"""Close renders of the gun's laser module. Usage: python3 -I ngal_shots.py -- <outdir> <tag>"""
import bpy, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]; out, tag = a[0], a[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
import ul2lib; from ul2lib import part
import mg_model as mg
from render_lib import setup, shot
part('g'); mg.build_gun(Matrix.Translation((0, 0, 1.0)))
sc, cam, gg = setup(24)
c = Vector((0, mg.NGAL_Y, 1.0 + 0.145))
shot(sc, cam, f'{out}/ngal_{tag}_front3q.png', c + Vector((0.20, 0.30, 0.12)), c, 40, res=(900, 700), scale=1.0)
shot(sc, cam, f'{out}/ngal_{tag}_rear3q.png', c + Vector((-0.22, -0.28, 0.16)), c, 40, res=(900, 700), scale=1.0)
shot(sc, cam, f'{out}/ngal_{tag}_side.png', c + Vector((0.40, 0.0, 0.02)), c, 40, res=(900, 700), scale=1.0)
shot(sc, cam, f'{out}/ngal_{tag}_gun.png', Vector((0.9, 0.9, 1.35)), Vector((0, 0.30, 1.05)), 45, res=(1200, 700), scale=1.0)
