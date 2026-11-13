"""Render the UL-2 with the painted skin. Usage: python3 -I ul2_preview.py -- <ul2_uv.blend> <skin_b.png> <outdir> <scale> [views]"""
import sys, bpy; sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from render_lib import *
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); img = bpy.data.images.load(a[1]); out = a[2]; scale = float(a[3]); only = set(a[4:])
m = bpy.data.materials['UL2_skin']; m.use_nodes = True; nt = m.node_tree
bsdf = [n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'][0]
tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = img; nt.links.new(tx.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 0.7
import os
pn, pm = a[1].replace('_b.png', '_n.png'), a[1].replace('_b.png', '_m.png')
if os.path.exists(pn):     # 0.15.22: normal + metallic/smoothness maps as the game reads them
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = bpy.data.images.load(pn); tn.image.colorspace_settings.name = 'Non-Color'
    nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
if os.path.exists(pm):
    tm = nt.nodes.new('ShaderNodeTexImage'); tm.image = bpy.data.images.load(pm); tm.image.colorspace_settings.name = 'Non-Color'
    sp = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(tm.outputs['Color'], sp.inputs['Color']); nt.links.new(sp.outputs['Red'], bsdf.inputs['Metallic'])
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
    nt.links.new(tm.outputs['Alpha'], inv.inputs[1]); nt.links.new(inv.outputs['Value'], bsdf.inputs['Roughness'])
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
S('L5_pod_close', (1.9, 1.9, 1.15), (0.2, 0.9, 0.85), 40, res=(1400, 900))
S('L6_tail_close', (1.6, -2.0, 1.9), (0.3, -2.85, 1.2), 40, res=(1400, 900))
S('L4_rear_3q_right', (5.0, -6.0, 3.0), (0, -1.0, 1.3), 40)
