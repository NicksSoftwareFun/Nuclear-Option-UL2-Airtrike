"""Render the UL-2 with the painted skin. Usage: python3 -I ul2_preview.py -- <ul2_uv.blend> <skin_b.png> <outdir> <scale> [views]"""
import sys, bpy; sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from render_lib import *
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); img = bpy.data.images.load(a[1]); out = a[2]; scale = float(a[3]); only = set(a[4:])
m = bpy.data.materials['UL2_skin']; m.use_nodes = True; nt = m.node_tree
bsdf = [n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'][0]
tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = img; nt.links.new(tx.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 0.7
fr = bpy.data.materials.get('UL2_frame')
sc, cam, g = setup(int(20 + 40 * scale))
c = bpy.data.collections.get('CREWREF')
if c: c.hide_render = True
def S(name, *args, **kw):
    if only and name not in only: return
    shot(sc, cam, f'{out}/{name}.png', *args, scale=scale, **kw)
S('L1_front_3q_right', (5.4, 7.4, 2.7), (0, -0.2, 1.35), 42)
S('L2_like_306', (-1.5, 7.5, 2.6), (0, -0.4, 1.3), 40)
S('L3_tail_left', (-3.2, -4.6, 1.6), (0, -3.0, 1.25), 35, res=(1200, 900))
S('L4_rear_3q_right', (5.0, -6.0, 3.0), (0, -1.0, 1.3), 40)
