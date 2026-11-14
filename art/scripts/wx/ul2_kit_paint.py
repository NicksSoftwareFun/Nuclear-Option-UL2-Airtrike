"""UL-2 weathering for everything that is NOT the livery skin: frame, mast, engine, gear, seats, cockpit tub, grips,
rotor and prop. One shared atlas for all three liveries (the skin keeps its own light weathering in the livery maps).
Evaluates in 3D on the texel maps from tex_raster_tb.py (UL2_kit family), so grime runs across UV seams.
Usage: python3 -I ul2_kit_paint.py <kit_maps.npz> <kit_ao_raw.png> <outdir>
Writes UL2_kit_b.png (sRGB colour, the material colours are baked in), UL2_kit_m.png (R metallic, A smoothness),
UL2_kit_ao.png (occlusion, mean ~0.85 like the stock skins)."""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, '/mnt/project-files/tu22/art/scripts/tex')
from tex_paint import vnoise

m = np.load(sys.argv[1]); out = sys.argv[3]; os.makedirs(out, exist_ok=True)
S = m['pos'].shape[0]; idx = m['idx'].ravel()
P = m['pos'].reshape(-1, 3)[idx]; N = m['nrm'].reshape(-1, 3)[idx]; PID = m['pid'].ravel()[idx]
MAT = m['mat'].ravel()[idx]; names = list(m['names']); mats = [str(x) for x in m['mats']]
def ids(*n): return np.isin(PID, [names.index(x) for x in n if x in names])
def mat(*n): return np.isin(MAT, [mats.index(x) for x in n])
def C(*rgb): return np.array(rgb, np.float32) / 255

# the builder's flat colours, smoothness, metallic (UL2Builder.cs LitMat calls)
BASE = {'UL2_frame': (C(175, 177, 180), 0.40, 0.20), 'UL2_mech': (C(110, 112, 115), 0.35, 0.40), 'UL2_dark': (C(42, 44, 47), 0.40, 0.0),
        'UL2_tyre': (C(22, 22, 22), 0.15, 0.0), 'UL2_seat': (C(70, 72, 74), 0.25, 0.0), 'UL2_cockpit': (C(60, 62, 65), 0.30, 0.0),
        'UL2_fde': (C(150, 126, 96), 0.32, 0.0), 'UL2_black': (C(34, 34, 33), 0.52, 0.40)}
n = len(P); col = np.zeros((n, 3), np.float32); sm = np.zeros(n, np.float32); me = np.zeros(n, np.float32)
for i, k in enumerate(mats): s = MAT == i; col[s], sm[s], me[s] = BASE[k]

# ambient occlusion: buried faces (inside joints, never seen) bake black; keep them neutral so nothing goes inky
ao = np.asarray(Image.open(sys.argv[2]).convert('L'), np.float32).ravel()[idx] / 255
ao = ndimage.median_filter(ao.reshape(S, S), 3).ravel()
ao = np.where(ao < 0.06, 0.7, ao)
cav = np.clip((0.80 - ao) / 0.55, 0, 1)                                         # 0 open .. 1 deep corner

up = N[:, 2]; z = P[:, 2]
CLEAN = ids('x_g_asi', 'x_g_adi', 'x_g_alt', 'x_g_eng', 'x_g_vsi')              # gauge needles behind glass
BLADES = ids('blade_R_root', 'blade_R_mid', 'blade_R_tip', 'blade_L_root', 'blade_L_mid', 'blade_L_tip')
INSIDE = mat('UL2_cockpit', 'UL2_seat', 'UL2_fde', 'UL2_black') | ids('x_stick', 'x_throttle', 'x_pedal_L', 'x_pedal_R', 'x_harness1',
         'x_harness2', 'x_harness3', 'x_ctrl2', 'x_stick2', 'x_thr2', 'x_pedal2_L', 'x_pedal2_R')
OUT = ~INSIDE & ~CLEAN
GRIME = C(52, 46, 38); DUST = C(150, 136, 112); MUD = C(108, 96, 80); OIL = C(34, 27, 20); SOOT = C(28, 28, 29)
def mix(a, c, k): return a * (1 - k[:, None]) + c * k[:, None]

# 1. fine grunge everywhere: tone and gloss wander a little, like handled paint and metal
g1 = vnoise(P, 0.06, 101); g2 = vnoise(P, 0.35, 102)
col *= (1 + 0.035 * g1 + 0.03 * g2)[:, None] * ~CLEAN[:, None] + CLEAN[:, None]
sm += (0.05 * g1 - 0.04 * np.clip(g2, 0, 1)) * ~CLEAN

# 2. grime packed into corners and joints
kc = cav * (0.55 * OUT + 0.30 * INSIDE) * ~CLEAN
col = mix(col, GRIME, kc); sm -= 0.12 * kc

# 3. dust on everything that faces up, more of it low down outside; a dusty tub floor and boot scuffs inside
dust_out = OUT * np.clip(up, 0, 1) ** 1.5 * (0.10 + 0.10 * np.clip((1.2 - z) / 0.9, 0, 1)) * np.clip(0.7 + 0.6 * vnoise(P, 0.25, 103), 0, 1.3)
floor = INSIDE & mat('UL2_cockpit') & (up > 0.6) & (z < 1.05)
dust_in = floor * np.clip(0.30 + 0.25 * vnoise(P, 0.15, 104), 0, 1) * 0.55
boots = floor * np.clip((vnoise(P * np.array([1, 0.4, 1], np.float32), 0.03, 105) - 0.1) / 0.25, 0, 1) * 0.35
dust = np.clip(dust_out + dust_in, 0, 0.6) * ~BLADES
col = mix(col, DUST, dust); sm = sm * (1 - dust) + 0.08 * dust
col = mix(col, C(92, 92, 90), boots); sm += 0.10 * boots

# 4. rain streaks on the outside verticals
vert = OUT * (np.abs(up) < 0.45)
rain = vert * np.clip(vnoise(P * np.array([3, 3, 0.4], np.float32), 0.05, 106) * 2.2 - 0.7, 0, 1) * 0.06
col *= (1 - rain)[:, None]

# 5. oil: mist round the engine thrown aft by the prop wash, drips down the crankcase, mast foot and keel, a pool
# stain on the frame under the sump
ENG = np.array([0.0, -1.42, 1.72], np.float32); EXH = np.array([0.0, -1.75, 1.41], np.float32)
dE = np.linalg.norm((P - ENG) * np.array([1.0, 0.7, 0.8], np.float32), axis=1)
patchy = np.clip(0.5 + 0.7 * vnoise(P, 0.12, 107) + 0.4 * vnoise(P * np.array([5, 0.4, 5], np.float32), 0.05, 108), 0, 1)
mist = np.exp(-(dE / 0.55) ** 2) * patchy * 0.55
drip = (np.abs(up) < 0.7) * (z < ENG[2] + 0.1) * np.exp(-(dE / 0.75) ** 2) \
       * np.clip(vnoise(P * np.array([3.5, 3.5, 0.4], np.float32), 0.05, 109) * 2.0 - 0.2, 0, 1) \
       * (1 - 0.65 * mat('UL2_frame'))   # runs show on the engine metal, only faintly on the painted frame
sump = (up > 0.3) * np.exp(-(np.hypot(P[:, 0] / 0.35, (P[:, 1] + 1.40) / 0.45)) ** 2) * (z < 1.2) * np.clip(0.4 + vnoise(P, 0.08, 110), 0, 1) * 0.7
oil = np.clip(mist + 0.5 * drip + sump, 0, 0.85) * OUT * ~BLADES
col = mix(col, OIL, oil); sm = sm * (1 - oil) + 0.62 * oil                     # oil is wet and glossy

# 6. exhaust soot: black round the stack outlet, fanning aft and down the tail boom
dx = EXH[1] - P[:, 1]
rp = np.hypot(P[:, 0], P[:, 2] - (EXH[2] - 0.05 * np.maximum(dx, 0)))
fan = (dx > -0.05) * np.exp(-(rp / (0.09 + 0.12 * np.maximum(dx, 0))) ** 2) * np.exp(-np.maximum(dx, 0) / 1.6)
lip = np.exp(-(np.linalg.norm(P - EXH, axis=1) / 0.10) ** 2)
soot = np.clip((0.75 * fan + 0.9 * lip) * np.clip(0.65 + 0.5 * vnoise(P * np.array([4, 0.5, 4], np.float32), 0.05, 111), 0, 1), 0, 0.9) * OUT
col = mix(col, SOOT, soot); sm = sm * (1 - soot) + 0.12 * soot; me *= 1 - soot

# 7. mud and dust kicked up by the wheels: gear legs, wheel hubs, the keel and stubs low down, splatter aft of the wheels
low = np.clip((0.75 - z) / 0.55, 0, 1)
WHEELS = [np.array(w, np.float32) for w in ((0, 1.98, 0.24), (-1.0, -0.67, 0.27), (1.0, -0.67, 0.27))]
near = np.zeros(n, np.float32)
for w in WHEELS: near = np.maximum(near, np.exp(-(np.linalg.norm(P - w, axis=1) / 0.55) ** 2))
spl = np.clip((vnoise(P, 0.018, 112) + 0.35 * vnoise(P, 0.06, 113) - 0.05) / 0.18, 0, 1)
mud = OUT * ~mat('UL2_tyre') * np.clip(low * (0.25 + 0.35 * near) * (0.75 + 0.25 * spl) + near * spl * 0.15 * (up < 0.6), 0, 0.6)
col = mix(col, MUD, mud); sm = sm * (1 - mud) + 0.06 * mud; me *= 1 - mud
# tyres: dry dust on the sidewalls, worn darker tread
tyre = mat('UL2_tyre')
side = tyre * (np.abs(N[:, 0]) > 0.6)
tdust = side * np.clip(0.30 + 0.25 * vnoise(P, 0.04, 114), 0, 1) * 0.40 + tyre * ~side * 0.12
col = mix(col, C(118, 106, 88), tdust); sm = sm * (1 - tdust) + 0.05 * tdust

# 8. wear: paint chips on the frame tubes low down and where boots and hands go (seat sills, the step tubes), grip
# polish on the black handles, seat cushions rubbed lighter and a little smoother where people sit
FR = mat('UL2_frame')
reach = np.clip((1.1 - z) / 0.6, 0, 1) + ((z > 1.0) & (z < 1.45) & (np.abs(P[:, 0]) > 0.2)).astype(np.float32) * 0.6
chip = FR * (np.clip((vnoise(P, 0.012, 115) - 0.45) / 0.12, 0, 1) * np.clip(reach, 0, 1)) * (vnoise(P, 0.2, 116) > -0.2)
col = mix(col, C(96, 98, 100), chip); me = me * (1 - chip) + 0.75 * chip; sm = sm * (1 - chip) + 0.45 * chip
SE = mat('UL2_seat')
rub = SE * (up > 0.35) * np.clip(0.55 + 0.6 * vnoise(P, 0.10, 117), 0, 1) * 0.30
col = col * (1 + 0.22 * rub)[:, None]; sm += 0.12 * rub
BK = mat('UL2_black') | (INSIDE & mat('UL2_dark'))
pol = BK * np.clip(0.5 + 0.7 * vnoise(P, 0.03, 118), 0, 1) * 0.15
sm += pol
# rotor blades: faint leading-edge erosion (lighter, matt) along the front edge, both blades spin the same way
BL = BLADES * np.clip((vnoise(P, 0.02, 119) + 0.2) / 0.5, 0, 1) * np.clip(np.abs(N[:, 1]) - 0.5, 0, 1) * 0.25
col = mix(col, C(150, 152, 154), BL); sm -= 0.10 * BL

col *= (0.80 + 0.20 * ao)[:, None] * ~CLEAN[:, None] + CLEAN[:, None]         # a touch of the AO in the colour, like stock
col = np.clip(col, 0, 1); sm = np.clip(sm, 0.03, 0.85); me = np.clip(me, 0, 1)
aoo = 1 - 0.65 * (1 - ao); aoo = np.clip(aoo + (0.85 - aoo[m['cov'].ravel()].mean()), 0.35, 1.0)
print('oil %d soot %d mud %d dust %d chips %d texels; ao mean %.3f' % ((oil > .1).sum(), (soot > .1).sum(), (mud > .1).sum(),
      (dust > .1).sum(), (chip > .2).sum(), aoo[m['cov'].ravel()].mean()))
def save(a, name, mode):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8).reshape(S, S, -1).squeeze(), mode).save(os.path.join(out, name))
save(col, 'UL2_kit_b.png', 'RGB')
save(np.stack([me, np.zeros(n), np.zeros(n), sm], 1), 'UL2_kit_m.png', 'RGBA')
save(np.repeat(aoo[:, None], 3, 1), 'UL2_kit_ao.png', 'RGB')
print('wrote', out)
