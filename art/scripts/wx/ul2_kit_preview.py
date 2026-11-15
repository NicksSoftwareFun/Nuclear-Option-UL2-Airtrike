"""Preview the kit weathering with a livery. Usage: python3 -I ul2_kit_preview.py -- <ul2_uv_kit.blend> <skin dir> <kit dir|none>
<livery olive|steel|tiger> <out.png> <cam x,y,z> <look x,y,z> <lens> [scale]"""
import sys, bpy, os; sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from render_lib import *
from ul2_kit_unwrap_mats import KIT
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); skin, kit, liv, outp = a[1], a[2], a[3], a[4]
cl = tuple(map(float, a[5].split(','))); lk = tuple(map(float, a[6].split(','))); lens = float(a[7]); scale = float(a[8]) if len(a) > 8 else 1.0
def tex(nt, path, nc=False):
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(path, check_existing=True)
    if nc: t.image.colorspace_settings.name = 'Non-Color'
    return t
def wire(m, b, mm, nn=None):
    m.use_nodes = True; nt = m.node_tree; bsdf = [x for x in nt.nodes if x.type == 'BSDF_PRINCIPLED'][0]
    nt.links.new(tex(nt, b).outputs['Color'], bsdf.inputs['Base Color'])
    tm = tex(nt, mm, True); sp = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(tm.outputs['Color'], sp.inputs['Color'])
    nt.links.new(sp.outputs['Red'], bsdf.inputs['Metallic'])
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
    nt.links.new(tm.outputs['Alpha'], inv.inputs[1]); nt.links.new(inv.outputs['Value'], bsdf.inputs['Roughness'])
    if nn:
        tn = tex(nt, nn, True); nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
bname = {'olive': 'UL2_skin_b.png', 'steel': 'UL2_skin_steel_b.png', 'tiger': 'UL2_skin_tiger_b.png'}[liv]
wire(bpy.data.materials['UL2_skin'], f'{skin}/{bname}', f'{skin}/UL2_skin_m.png', f'{skin}/UL2_skin_n.png')
if liv == 'steel':   # steel livery glossiness 0.6 (LiveryData) scales the smoothness
    pass
FLAT = {'UL2_frame': ((175, 177, 180), 0.40, 0.20), 'UL2_mech': ((110, 112, 115), 0.35, 0.40), 'UL2_dark': ((42, 44, 47), 0.40, 0.0),
        'UL2_tyre': ((22, 22, 22), 0.15, 0.0), 'UL2_seat': ((70, 72, 74), 0.25, 0.0), 'UL2_cockpit': ((60, 62, 65), 0.30, 0.0),
        'UL2_fde': ((150, 126, 96), 0.32, 0.0), 'UL2_black': ((34, 34, 33), 0.52, 0.40)}
for k in KIT:
    m = bpy.data.materials.get(k)
    if not m: continue
    if kit != 'none': wire(m, f'{kit}/UL2_kit_b.png', f'{kit}/UL2_kit_m.png')
    else:
        m.use_nodes = True; bsdf = [x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED'][0]
        c, s, mt = FLAT[k]; bsdf.inputs['Base Color'].default_value = (*[(v / 255) ** 2.2 for v in c], 1)
        bsdf.inputs['Roughness'].default_value = 1 - s; bsdf.inputs['Metallic'].default_value = mt
sc, cam, g = setup(48)
c = bpy.data.collections.get('CREWREF')
if c: c.hide_render = True
for o in bpy.data.objects:
    if o.name.startswith(('x_rotordisc', 'x_propdisc')): o.hide_render = True
shot(sc, cam, outp, cl, lk, lens, scale=scale)
