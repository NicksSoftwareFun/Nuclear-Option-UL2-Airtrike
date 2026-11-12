"""Cockpit review renders. Usage: python3 -I render_cockpit.py -- <blend> <outdir> [names...]
First person shots use the game's eye point (helmetCamPoint, Blender final (0, 1.315, 1.665)) and the default 50 deg
vertical field of view, with the crew and the pilot harness hidden like the game does in cockpit view."""
import sys, bpy, math; sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from render_lib import *
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); out = a[1]; only = set(a[2:])
sc, cam, g = setup(24)
EYE = Vector((0, 1.315, 1.665))
def S(name, loc, look, fov=None, lens=40, res=(1600, 900), fp=False):
    if only and name not in only: return
    for o in bpy.data.objects:
        if o.name.startswith(('x_harness1', 'REF_pilot')): o.hide_render = fp
    if fov: cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(fov)
    else: cam.data.sensor_fit = 'AUTO'; cam.data.lens = lens
    sc.render.resolution_x, sc.render.resolution_y = res
    cam.location = loc; cam.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'PERSP'; sc.render.filepath = f'{out}/{name}.png'; bpy.ops.render.render(write_still=True)
def dirn(pitch_down): return EYE + Vector((0, math.cos(math.radians(pitch_down)), -math.sin(math.radians(pitch_down))))
S('fp_level', EYE, dirn(0), fov=50, fp=True)
S('fp_down10', EYE, dirn(10), fov=50, fp=True)
S('stick_grip', (0.20, 1.50, 1.62), (0, 1.69, 1.39), lens=60, res=(1200, 900), fp=True)
S('throttle', (-0.10, 1.45, 1.55), (-0.31, 1.62, 1.40), lens=60, res=(1200, 900), fp=True)
S('pedals', (0.30, 1.75, 1.05), (0, 2.28, 0.62), lens=30, res=(1200, 900), fp=True)
S('copilot_panel', (0, 0.30, 1.86), (0, 0.87, 1.42), lens=35, res=(1200, 900), fp=True)
S('nose_3q', (2.6, 4.6, 2.2), (0, 1.9, 1.25), lens=40, res=(1400, 900))
S('side', (-6.0, 1.2, 1.45), (0, 1.2, 1.25), lens=50, res=(1400, 900))
S('rifle1', (-0.95, 1.05, 1.45), (-0.29, 1.25, 1.35), lens=45, res=(1000, 1200))
S('rifle1_back', (-0.75, 0.55, 1.55), (-0.29, 1.15, 1.35), lens=45, res=(1000, 1200), fp=True)
S('belly_pod', (0.9, 2.2, 0.15), (0, 1.0, 0.40), lens=45, res=(1200, 900))
S('pod_front', (0.25, 2.0, 0.05), (0, 1.05, 0.25), lens=70, res=(1200, 900))
S('pylon_R', (1.9, 1.6, 1.4), (0.86, 0.50, 0.80), lens=45, res=(1200, 900))
S('windscreen_front', (0.0, 4.2, 1.9), (0, 1.9, 1.35), lens=45, res=(1400, 900))
