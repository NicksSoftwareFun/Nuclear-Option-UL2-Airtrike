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
def S(name, loc, look, fov=None, lens=40, res=(1600, 900), fp=False, clip=0.05):
    if only and name not in only: return
    cam.data.clip_start = clip
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
S('fp_right', EYE, (0.40, 2.05, 1.40), lens=28, fp=True)   # 0.15.29 glass to hull edge check
S('fp_right_zoom', EYE, (0.37, 2.05, 1.13), lens=90, fp=True)
S('ws_corner', (0.75, 1.80, 1.35), (0.38, 2.03, 1.13), lens=110, res=(1200, 900))
S('ws_corner_in', (0.05, 1.85, 1.40), (0.37, 2.03, 1.13), lens=90, res=(1200, 900))
S('ws_side_out', (0.95, 1.75, 1.45), (0.37, 2.10, 1.18), lens=55, res=(1200, 900))
S('stick_grip', (0.20, 1.50, 1.62), (0, 1.69, 1.39), lens=60, res=(1200, 900), fp=True)
S('throttle', (-0.10, 1.45, 1.55), (-0.31, 1.62, 1.40), lens=60, res=(1200, 900), fp=True)
S('pedals', (0.30, 1.75, 1.05), (0, 2.28, 0.62), lens=30, res=(1200, 900), fp=True)
S('copilot_panel', (0, 0.30, 1.86), (0, 0.87, 1.42), lens=35, res=(1200, 900), fp=True)
S('nose_3q', (2.6, 4.6, 2.2), (0, 1.9, 1.25), lens=40, res=(1400, 900))
S('side', (-6.0, 1.2, 1.45), (0, 1.2, 1.25), lens=50, res=(1400, 900))
S('rifle1', (-0.95, 1.05, 1.45), (-0.29, 1.25, 1.35), lens=45, res=(1000, 1200))
S('rifle1_back', (-0.75, 0.55, 1.55), (-0.29, 1.15, 1.35), lens=45, res=(1000, 1200), fp=True)
S('sub_side', (-1.0, 1.15, 1.55), (-0.30, 1.15, 1.55), lens=50, res=(1200, 1200), clip=0.66)
S('sub_3q', (-0.95, 1.55, 1.85), (-0.30, 1.15, 1.55), lens=50, res=(1200, 1200), clip=0.62)
S('belly_pod', (0.9, 2.2, 0.15), (0, 1.0, 0.40), lens=45, res=(1200, 900))
S('pod_front', (0.25, 2.0, 0.05), (0, 1.05, 0.25), lens=70, res=(1200, 900))
S('pylon_R', (1.9, 1.6, 1.4), (0.86, 0.50, 0.80), lens=45, res=(1200, 900))
S('windscreen_front', (0.0, 4.2, 1.9), (0, 1.9, 1.35), lens=45, res=(1400, 900))
# 0.15.12 hull / glass joint checks (Nick 00:02)
S('ws_base_L', (-0.80, 2.55, 1.62), (-0.30, 2.10, 1.32), lens=45, res=(1200, 900))
S('ws_base_top', (-0.15, 2.75, 2.05), (-0.15, 2.12, 1.38), lens=40, res=(1200, 900))
S('rim_side', (-1.10, 1.30, 1.45), (-0.33, 1.50, 1.02), lens=35, res=(1200, 900))
S('ws_inside', (0.15, 1.75, 1.62), (-0.30, 2.15, 1.30), lens=30, res=(1200, 900), fp=True)
S('co_station', (0.60, 0.25, 2.05), (0, 0.50, 1.40), lens=32, res=(1200, 900))
S('co_ctrl', (0.30, 0.78, 1.85), (0, 0.445, 1.545), lens=55, res=(1200, 900))
S('co_side', (-1.1, 0.55, 1.45), (0, 0.65, 1.15), lens=28, res=(1200, 900), clip=0.72)
S('co_pedals', (0.05, 0.55, 1.25), (0, 1.03, 0.92), lens=30, res=(1200, 900))
S('sub_grip', (-0.80, 1.20, 1.45), (-0.30, 1.22, 1.40), lens=60, res=(1200, 1200), clip=0.40)
