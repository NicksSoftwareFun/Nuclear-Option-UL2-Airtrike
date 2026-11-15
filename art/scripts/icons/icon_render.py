"""UL-2 icons, raw renders. Usage: python3 -I icon_render.py -- <outdir>
Weapon icons (stock style: white line drawing of the weapon from the side on black, 512x256): each kit is rendered
side-on, orthographic, with flat random colour per object; icon_lines.py turns colour boundaries into lines.
Map icon: top-down silhouette of the whole aircraft (alpha)."""
import bpy, sys, math
sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto'); sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/seat3')
from mathutils import Vector, Matrix
out = sys.argv[sys.argv.index('--') + 1]

def flat_mat(rand=True):
    m = bpy.data.materials.new('flat'); m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    o = nt.nodes.new('ShaderNodeOutputMaterial'); e = nt.nodes.new('ShaderNodeEmission')
    if rand:   # flat random colour per object, plus a little of the face normal so creases inside one object show too
        oi = nt.nodes.new('ShaderNodeObjectInfo'); h = nt.nodes.new('ShaderNodeCombineColor'); h.mode = 'HSV'
        nt.links.new(oi.outputs['Random'], h.inputs[0]); h.inputs[1].default_value = 0.9; h.inputs[2].default_value = 1.0
        nt.links.new(h.outputs[0], e.inputs['Color'])
    nt.links.new(e.outputs[0], o.inputs['Surface']); return m
def wb(sc, rand=True):
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 1; sc.cycles.use_denoising = False
    sc.cycles.pixel_filter_type = 'BOX'; sc.cycles.filter_width = 0.01; sc.cycles.max_bounces = 0
    sc.render.film_transparent = True; sc.view_settings.view_transform = 'Standard'
    sc.render.resolution_x, sc.render.resolution_y = 2048, 1024
    m = flat_mat(rand)
    for ob in bpy.data.objects:
        if ob.type == 'MESH':
            ob.data.materials.clear(); ob.data.materials.append(m)
def cam_side(sc, objs, pad=1.10):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (lo + hi) / 2; L = max(hi.y - lo.y, 2 * (hi.z - lo.z)) * pad
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = L; cam.location = c + Vector((-5, 0, 0))   # left side: muzzle (+y) points left like the stock icons
    cam.rotation_euler = (Vector(c) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    # muzzle to the LEFT like the stock icons: camera on +x looking -x has +y (muzzle) on the right, so mirror later if needed
    return cam

import ul2lib; from ul2lib import part
for kind in ('mg', 'gl', 'spot'):
    bpy.ops.wm.read_factory_settings(use_empty=True); ul2lib.MATS.clear() if hasattr(ul2lib, 'MATS') else None
    part('k'); M = Matrix.Translation((0, 0, 1.0))
    if kind == 'mg': import mg_model as km; km.build_gun(M)
    elif kind == 'gl': import gl_model as km; km.build_gl(M)
    else: import bino_model as km; km.build_bino(M)
    objs = [o for o in bpy.data.objects if o.type == 'MESH' and not o.name.startswith(('REF', 'hand', 'glove'))]
    for o in bpy.data.objects:
        if o.type == 'MESH' and o not in objs: o.hide_render = True
    sc = bpy.context.scene; wb(sc); cam = cam_side(sc, objs)
    if kind == 'spot':
        c0 = cam.location + Vector((5, 0, 0)); d = Vector((-1.0, 0.75, 0.45)).normalized(); cam.location = c0 + d * 5
        cam.rotation_euler = (c0 - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam.data.ortho_scale *= 1.25
    sc.render.filepath = f'{out}/raw_{kind}.png'; bpy.ops.render.render(write_still=True)

# map icon: whole aircraft from above
bpy.ops.wm.open_mainfile(filepath='/mnt/project-files/ul2/art/blend/ul2_proto.blend'); sc = bpy.context.scene
for cn in ('COLLISION', 'POINTS', 'CREWREF', 'REF'):
    c = bpy.data.collections.get(cn)
    if c: c.hide_render = True
for o in bpy.data.objects:
    if o.type != 'MESH' or 'colmesh' in o.name or o.name.startswith(('x_', 'REF', 'spin_')): o.hide_render = True
wb(sc, rand=False); sc.render.resolution_x = sc.render.resolution_y = 1024
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 8.6; cam.location = (0, -0.24, 30); cam.rotation_euler = (0, 0, 0)
blades = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('blade_') and not o.hide_render]
for o in blades: o.hide_render = True
sc.render.filepath = f'{out}/raw_map.png'; bpy.ops.render.render(write_still=True)
keep = {o: o.hide_render for o in bpy.data.objects}
for o in bpy.data.objects: o.hide_render = o not in blades
sc.render.filepath = f'{out}/raw_map_rotor.png'; bpy.ops.render.render(write_still=True)
