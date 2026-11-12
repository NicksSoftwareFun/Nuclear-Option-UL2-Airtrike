import bpy, math, bmesh
from mathutils import Vector
def setup(samples=32):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = samples; sc.cycles.use_denoising = True
    sc.world = sc.world or bpy.data.worlds.new('w'); sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes['Background']; bg.inputs[0].default_value = (0.80, 0.84, 0.90, 1); bg.inputs[1].default_value = 0.9
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.2; sun.data.angle = 0.06
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35)); sc.collection.objects.link(sun)
    sc.view_settings.view_transform = 'Standard'
    me = bpy.data.meshes.new('ground'); bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60); bm.to_mesh(me)
    gm = bpy.data.materials.new('ground'); gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.33, 0.34, 0.35, 1)
    me.materials.append(gm); g = bpy.data.objects.new('ground', me); sc.collection.objects.link(g)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    return sc, cam, g
def shot(sc, cam, path, loc, look, lens=45, ortho=None, res=(1400, 900), scale=1.0):
    sc.render.resolution_x, sc.render.resolution_y = int(res[0] * scale), int(res[1] * scale)
    cam.location = loc; cam.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    if ortho: cam.data.type = 'ORTHO'; cam.data.ortho_scale = ortho
    else: cam.data.type = 'PERSP'; cam.data.lens = lens
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
