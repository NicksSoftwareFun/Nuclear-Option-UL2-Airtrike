"""Bake ambient occlusion into the UL2_kit weathering atlas with Cycles. Rotor and prop blur discs are hidden; the
skin faces bake into a throwaway image. Usage: python3 -I ul2_kit_ao.py -- <ul2_uv_kit.blend> <out_ao.png> [size] [samples]"""
import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ul2_kit_unwrap_mats import KIT
a = sys.argv[sys.argv.index('--') + 1:]
S = int(a[2]) if len(a) > 2 else 2048; SAMP = int(a[3]) if len(a) > 3 else 32
bpy.ops.wm.open_mainfile(filepath=a[0])
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = SAMP
if sc.world is None: sc.world = bpy.data.worlds.new('w')
sc.world.light_settings.distance = 0.45
for c in bpy.data.collections:
    if c.name != 'PARTS': c.hide_render = True
for o in bpy.data.objects:
    if o.name.startswith(('x_rotordisc', 'x_propdisc')): o.hide_render = True
img = bpy.data.images.new('kit_ao', S, S, float_buffer=False); img.generated_color = (1, 1, 1, 1)
junk = bpy.data.images.new('junk', 64, 64)
for m in bpy.data.materials:
    m.use_nodes = True; nt = m.node_tree
    for n in nt.nodes: n.select = False
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = img if m.name in KIT else junk; t.select = True; nt.nodes.active = t
objs = [o for o in bpy.data.collections['PARTS'].objects if o.type == 'MESH' and not o.hide_render
        and any(m and m.name in KIT for m in o.data.materials)]
for o in bpy.data.objects: o.select_set(False)
for o in objs: o.select_set(True)
bpy.context.view_layer.objects.active = objs[0]
sc.render.bake.margin = 6; sc.render.bake.margin_type = 'EXTEND'
bpy.ops.object.bake(type='AO', use_clear=False)
img.filepath_raw = a[1]; img.file_format = 'PNG'; img.save()
print('baked', len(objs), 'objects ->', a[1])
