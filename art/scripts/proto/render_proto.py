import sys, bpy; sys.path.insert(0, '/mnt/project-files/ul2/art/scripts/proto')
from render_lib import *
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); out = a[1]; scale = float(a[2]); only = set(a[3:])
sc, cam, g = setup(int(20 + 40 * scale))
def S(name, *args, crew=True, **kw):
    if only and name not in only: return
    c = bpy.data.collections.get('CREWREF')
    if c: c.hide_render = not crew
    shot(sc, cam, f'{out}/{name}.png', *args, scale=scale, **kw)
S('01_front_3q_right', (5.4, 7.4, 2.7), (0, -0.2, 1.35), 42)
S('02_rear_3q_left', (-5.6, -6.8, 2.6), (0, -0.9, 1.35), 42)
S('03_side_left', (-40, -0.2, 1.55), (0, -0.2, 1.55), ortho=8.4, res=(1600, 900))
S('04_front', (0, 40, 1.4), (0, 0, 1.4), ortho=4.2, res=(1200, 1000))
S('05_seat_fit', (-3.2, 1.0, 1.6), (0, 0.3, 1.25), 40, res=(1400, 1000))
S('06_gun_mount', (2.2, 2.8, 1.1), (0.45, 1.15, 0.80), 45, res=(1200, 900))
S('07_bare_3q', (5.4, 7.4, 2.7), (0, -0.2, 1.35), 42, crew=False)
S('08_low_3q_left_like_303', (-6.5, 1.0, 0.5), (0, -0.4, 1.4), 38)
S('R303_right_low', (7.2, -0.4, 0.9), (0, -0.4, 1.35), 42)
S('R306_front', (-1.3, 9.0, 1.45), (0, 0, 1.2), 50)
S('R306_flight_right_high', (7.0, -2.2, 3.2), (0, -0.3, 1.3), 42)
S('Rrear_left', (-3.8, -5.6, 1.7), (0, -1.3, 1.4), 40)
S('09_rotor_head', (1.3, 0.2, 3.35), (0, -0.97, 2.95), 50, res=(1200, 900), crew=False)
