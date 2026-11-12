"""Close renders of a seat 3 kit on its own, with the preview finish. Usage: python3 -I kit_shots.py -- <gl|bino> <outdir> <tag>"""
import bpy, sys
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]; kind, out, tag = a[:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
import ul2lib; from ul2lib import part
import look
from render_lib import setup, shot
part('k'); M = Matrix.Translation((0, 0, 1.0))
if kind == 'gl':
    import gl_model as km; km.build_gl(M); km.build_hands(M) if '--hands' in sys.argv else None
    c = Vector((0, 0.10, 1.04)); d = 1.0
else:
    import bino_model as km; km.build_bino(M); km.build_hands(M) if '--hands' in sys.argv else None
    c = Vector((0, 0.08, 1.0)); d = 0.45
look.gun([o for o in bpy.data.objects if o.type == 'MESH'])
sc, cam, gg = setup(24)
shot(sc, cam, f'{out}/{kind}_{tag}_R.png', c + Vector((1.5 * d, 0.0, 0.05 * d)), c, 40, res=(1200, 700), scale=1.0)
shot(sc, cam, f'{out}/{kind}_{tag}_fl.png', c + Vector((-0.9 * d, 1.0 * d, 0.45 * d)), c, 40, res=(1200, 800), scale=1.0)
shot(sc, cam, f'{out}/{kind}_{tag}_rl.png', c + Vector((-0.8 * d, -1.0 * d, 0.55 * d)), c, 40, res=(1200, 800), scale=1.0)
shot(sc, cam, f'{out}/{kind}_{tag}_top.png', c + Vector((0.3 * d, 0.0, 1.5 * d)), c, 40, res=(1200, 800), scale=1.0)
