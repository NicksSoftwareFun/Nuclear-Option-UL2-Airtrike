"""Blockout render sheet. Usage: python3 -I ul2_render.py -- <blend> <outdir> [shot names...]"""
import bpy, sys, math
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0]); out = args[1]; only = set(args[2:])
sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 24; sc.cycles.use_denoising = True
sc.world = sc.world or bpy.data.worlds.new('w'); sc.world.use_nodes = True
bg = sc.world.node_tree.nodes['Background']; bg.inputs[0].default_value = (0.78, 0.83, 0.90, 1); bg.inputs[1].default_value = 1.0
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.2; sun.data.angle = 0.05
sun.rotation_euler = (math.radians(52), math.radians(8), math.radians(-40)); sc.collection.objects.link(sun)
sc.view_settings.view_transform = 'Standard'
# ground
import bmesh
me = bpy.data.meshes.new('ground'); bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60); bm.to_mesh(me)
gm = bpy.data.materials.new('ground'); gm.use_nodes = True
gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.42, 0.43, 0.42, 1)
gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.95
me.materials.append(gm); ground = bpy.data.objects.new('ground', me); sc.collection.objects.link(ground)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam

VAR = ['ENG_A', 'ENG_B', 'PAX_SPOTTER', 'PAX_MG', 'PAX_GL', 'STORES']
def show(*on):
    for v in VAR:
        c = bpy.data.collections.get(v)
        if c: c.hide_render = v not in on

def shot(name, loc, look, on, ortho=None, lens=50, res=(1400, 880)):
    if only and name not in only: return
    show(*on)
    sc.render.resolution_x, sc.render.resolution_y = res
    cam.location = loc
    cam.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    if ortho: cam.data.type = 'ORTHO'; cam.data.ortho_scale = ortho
    else: cam.data.type = 'PERSP'; cam.data.lens = lens
    ground.hide_render = bool(ortho) and abs(loc[2]) > 20
    sc.render.filepath = f'{out}/{name}.png'; bpy.ops.render.render(write_still=True)

C = (0, 0.1, 1.3)
shot('01_front_3q_right', (6.2, 8.6, 3.0), C, ('ENG_A', 'PAX_MG', 'STORES'), lens=42)
shot('02_front_3q_left', (-6.2, 8.6, 3.0), C, ('ENG_A', 'PAX_GL', 'STORES'), lens=42)
shot('03_side_right', (40, 0.1, 1.55), (0, 0.1, 1.55), ('ENG_A', 'PAX_MG', 'STORES'), ortho=10.0, res=(1600, 620))
shot('04_engineA_rear', (-4.6, -6.4, 2.6), (0, -0.9, 1.25), ('ENG_A', 'PAX_SPOTTER'), lens=45)
shot('05_engineB_rear', (-4.6, -6.4, 2.6), (0, -0.9, 1.25), ('ENG_B', 'PAX_SPOTTER'), lens=45)
shot('06_side_right_engineB', (40, 0.1, 1.55), (0, 0.1, 1.55), ('ENG_B', 'PAX_SPOTTER', 'STORES'), ortho=10.0, res=(1600, 620))
shot('07_pax_spotter', (-2.3, 1.9, 2.2), (0, 0.1, 1.25), ('ENG_A', 'PAX_SPOTTER'), lens=48, res=(1100, 880))
shot('08_pax_mg', (2.4, 1.5, 2.3), (0.2, 0.05, 1.3), ('ENG_A', 'PAX_MG'), lens=48, res=(1100, 880))
shot('09_pax_gl', (-2.3, 1.9, 2.2), (0, 0.1, 1.25), ('ENG_A', 'PAX_GL'), lens=48, res=(1100, 880))
shot('10_side_gun', (2.9, 4.4, 1.25), (0.5, 1.7, 0.8), ('ENG_A', 'PAX_MG', 'STORES'), lens=50, res=(1100, 880))
shot('11_top', (0, -0.3, 40), (0, -0.3, 0), ('ENG_A', 'PAX_MG', 'STORES'), ortho=10.0, res=(1100, 1100))
shot('12_front', (0, 40, 1.55), (0, 0, 1.55), ('ENG_A', 'PAX_MG', 'STORES'), ortho=10.0, res=(1600, 620))

