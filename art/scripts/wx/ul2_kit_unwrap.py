"""Weathering atlas for every non-skin part of the UL-2 (frame, mast, engine, gear, seats, cockpit tub, grips).
Unwraps the faces of the KIT materials into their own packed atlas in UV0 (the skin faces keep the livery atlas; the
two overlap in UV space, which is harmless because they are different materials). Runs after ul2_unwrap.py.
Usage: python3 -I ul2_kit_unwrap.py -- <ul2_uv.blend in> <out.blend>"""
import bpy, bmesh, sys, math, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ul2_kit_unwrap_mats import KIT
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
def kidx(o): return [i for i, m in enumerate(o.data.materials) if m and m.name in KIT]
objs = [o for o in bpy.data.collections['PARTS'].objects if o.type == 'MESH' and kidx(o)]
for o in objs:
    if not o.data.uv_layers: o.data.uv_layers.new(name='UVMap')
bpy.context.view_layer.objects.active = objs[0]
for o in bpy.data.objects: o.select_set(False)
for o in objs: o.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.context.scene.tool_settings.use_uv_select_sync = False
bpy.ops.mesh.select_mode(type='FACE')
n = 0
for o in objs:
    bm = bmesh.from_edit_mesh(o.data); si = kidx(o)
    for v in bm.verts: v.select = False
    for e in bm.edges: e.select = False
    for f in bm.faces: f.select = f.material_index in si; n += f.select
    bm.select_flush_mode(); bmesh.update_edit_mesh(o.data)
print('kit faces', n, 'objects', len(objs))
bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.0, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.average_islands_scale()
bpy.ops.uv.pack_islands(rotate=True, margin_method='FRACTION', margin=0.0015, shape_method='CONCAVE')
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.wm.save_as_mainfile(filepath=a[1])
print('saved', a[1])
