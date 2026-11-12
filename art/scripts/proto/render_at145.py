"""UL-2 with crew in all three seats and the Ibis 4x AT145 launcher (stock ATGM1_quad, exported OBJ) on both stub pylons.
Usage: python3 -I render_at145.py -- <blend> <atgm.obj> <outdir> <scale> [skin_b.png]"""
import sys, bpy, bmesh; sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from render_lib import *
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); out = a[2]; scale = float(a[3])
if len(a) > 4:
    m = bpy.data.materials['UL2_skin']; m.use_nodes = True; nt = m.node_tree
    bsdf = [n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'][0]
    tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = bpy.data.images.load(a[4]); nt.links.new(tx.outputs['Color'], bsdf.inputs['Base Color'])
vs, fs = [], []
for ln in open(a[1]):
    p = ln.split()
    if not p: continue
    if p[0] == 'v': vs.append(tuple(map(float, p[1:4])))
    elif p[0] == 'f': fs.append(tuple(int(x.split('/')[0]) - 1 for x in p[1:4]))
me = bpy.data.meshes.new('atgm'); me.from_pydata(vs, [], fs); me.update()
for pg in me.polygons: pg.use_smooth = True
mt = bpy.data.materials.new('atgm_mat'); mt.use_nodes = True
mt.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.16, 0.17, 0.15, 1); me.materials.append(mt)
HP_Z = 0.59      # underside of the stub pylon plate
for sx in (-1, 1):
    o = bpy.data.objects.new(f'atgm{sx}', me); bpy.context.scene.collection.objects.link(o)
    o.matrix_world = Matrix.Translation((sx * 0.70, 0.20, HP_Z))
lo = min(v[2] for v in vs) + HP_Z; print('launcher lowest point z', round(lo, 3), 'length', round(max(v[1] for v in vs) - min(v[1] for v in vs), 3))
sc, cam, g = setup(int(20 + 40 * scale))
c = bpy.data.collections.get('CREWREF')
if c: c.hide_render = False
def S(name, *args, **kw): shot(sc, cam, f'{out}/{name}.png', *args, scale=scale, **kw)
S('A1_front_3q_right', (5.4, 7.4, 2.7), (0, -0.2, 1.35), 42)
S('A2_side_left', (-40, -0.2, 1.55), (0, -0.2, 1.55), ortho=8.4, res=(1600, 900))
S('A3_front', (0, 40, 1.4), (0, 0, 1.4), ortho=4.2, res=(1200, 1000))
S('A4_rear_3q_left', (-5.6, -6.8, 2.6), (0, -0.9, 1.35), 42)
