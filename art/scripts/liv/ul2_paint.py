"""UL-2 Airtrike skin painter, livery 1 "Army Olive" (matte olive drab like the reference photo, white tail number,
small stencils). Evaluates on the texel maps from tex_raster_tb.py in 3D, so markings run across UV seams.
Usage: python3 ul2_paint.py <maps.npz> <outdir>
Writes UL2_skin_b.png (sRGB), _n (flat OpenGL normal), _m (R metallic, A smoothness), _ao, _dmg_b (colour + alpha =
hull level at which each shard opens, stock style), _dmg_n."""
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy.spatial import cKDTree
sys.path.insert(0, '/mnt/project-files/tu22/art/scripts/tex')
from tex_paint import vnoise

OLIVE = np.array([82, 88, 56], np.float32) / 255
WHITE = np.array([226, 226, 220], np.float32) / 255
BLACK = np.array([28, 29, 27], np.float32) / 255
FONT = '/usr/share/fonts/opentype/inter/Inter-Black.otf'
FONT_S = '/usr/share/fonts/opentype/inter/Inter-Bold.otf'
m = np.load(sys.argv[1]); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
S = m['pos'].shape[0]; pos = m['pos'].reshape(-1, 3); nrm = m['nrm'].reshape(-1, 3); pid = m['pid'].ravel()
cov = m['cov'].ravel(); idx = m['idx'].ravel(); names = list(m['names'])
src = idx                                       # gutter texels copy their nearest covered texel
P = pos[src]; N = nrm[src]; PID = pid[src]
def ids(*n): return np.isin(PID, [names.index(x) for x in n if x in names])

# livery: env UL2_LIVERY = olive (default, writes every map) | steel | tiger (colour map only, like stock extra liveries:
# LiveryData swaps just the colour texture; normal/metallic/AO/damage maps are shared)
LIV = os.environ.get('UL2_LIVERY', 'olive')
ORANGE = np.array([236, 118, 24], np.float32) / 255
if LIV == 'steel':
    # polished stainless (Nick 04:29): bright steel, sheets of slightly different tone between the frame seams, fine
    # polishing streaks along the length, and a painted-in sky/ground reflection so it reads as mirror-bright metal
    base = np.array([178, 182, 187], np.float32) / 255
    col = np.tile(base, (len(P), 1))
    sheet = np.floor((P[:, 1] + 0.10) / 0.55) * 7.3 + np.floor(P[:, 0] * 1.7) * 3.1
    col *= (1 + 0.04 * np.sin(sheet))[:, None]
    streak = vnoise(P * np.array([6.0, 0.25, 6.0], np.float32), 0.05, 13)
    col *= (1 + 0.07 * streak)[:, None]
    up = N[:, 2]
    # mirror cues baked in: bright sky on upward faces, a sharp dark horizon line just below the side equator,
    # warm dark ground reflection on the belly, and a soft sun glint band on the upper flanks
    # 0.15.28 (Nick 05:29: artifact on the tail): no horizon line or glint bands any more (they wandered across the flat fin
    # with tiny normal changes); a smooth sky-to-ground gradient only
    col *= (1 + 0.22 * np.clip(up, -1, 1))[:, None]
    tw = np.clip(0.5 + up * 1.5, 0, 1)[:, None]                                    # cool above, warm below, smoothly
    col = col * (tw * np.array([0.97, 1.0, 1.04], np.float32) + (1 - tw) * np.array([1.04, 1.0, 0.93], np.float32))
elif LIV == 'tiger':
    # black with orange tiger stripes (Nick 04:29): wavy, tapering, forked bands running round the aircraft, slanted back
    col = np.tile(np.array([20, 20, 21], np.float32) / 255, (len(P), 1))
    sc = P[:, 1] * 0.94 + P[:, 2] * 0.34 + 0.22 * vnoise(P, 0.9, 51) + 0.06 * vnoise(P, 0.25, 52)
    ph = (sc / 0.42) % 1.0
    width = 0.20 + 0.10 * vnoise(P, 0.7, 53) - 0.16 * np.clip(np.abs(P[:, 0]) / 0.9, 0, 1) * (np.abs(N[:, 0]) < 0.5)
    band = np.abs(ph - 0.5) < width / 2
    fork = (np.abs(ph - 0.5) < width * 0.12) & (vnoise(P, 0.35, 54) > 0.15)
    stripe = (band & ~fork & (vnoise(P, 0.5, 55) > -0.45)).astype(np.float32)
    col = col * (1 - stripe[:, None]) + ORANGE * stripe[:, None] * (1 + 0.04 * vnoise(P, 0.3, 56))[:, None]
else:
    col = np.tile(OLIVE, (len(P), 1))
    col *= (1 + 0.035 * vnoise(P, 1.2, 3) + 0.02 * vnoise(P, 0.25, 7))[:, None]      # faint mottling of matte paint
col *= (1 - 0.06 * np.clip((0.75 - P[:, 2]) / 0.3, 0, 1) * ids('cockpit_F', 'nose', 'gunmount_R'))[:, None]   # slightly dusty belly
MARK = {'olive': WHITE, 'steel': BLACK, 'tiger': WHITE}[LIV]   # 0.15.29 (Nick 12:29): tiger text white
STEP = {'olive': BLACK, 'steel': BLACK, 'tiger': WHITE}[LIV]
smooth = np.full(len(P), 0.28, np.float32)

def text_mask(txt, font, px=400):
    f = ImageFont.truetype(font, px); l, t, r, b = f.getbbox(txt)
    im = Image.new('L', (r - l + 20, b - t + 20), 0); ImageDraw.Draw(im).text((10 - l, 10 - t), txt, 255, font=f)
    return np.asarray(im, np.float32) / 255

def stamp(sel, txt, font, centre, h, side, colour, axis='x'):
    """Text on a surface facing +-axis: centre (y, z) in metres, cap height h; reads nose-to-the-right on the right side."""
    tm = text_mask(txt, font); th, tw = tm.shape; w = h * tw / th
    face = sel & (np.sign(N[:, 0]) == side) & (np.abs(N[:, 0]) > 0.5)
    u = (P[:, 1] - centre[0]) * side / w + 0.5; v = 0.5 - (P[:, 2] - centre[1]) / h
    ok = face & (u >= 0) & (u < 1) & (v >= 0) & (v < 1)
    a = np.zeros(len(P), np.float32)
    a[ok] = tm[(v[ok] * th).astype(int).clip(0, th - 1), (u[ok] * tw).astype(int).clip(0, tw - 1)]
    col[:] = col * (1 - a[:, None]) + colour * a[:, None]
    return ok.sum()

def stamp_top(sel, txt, font, centre, h, colour, up=1):
    """Text on an upward face, read from behind (nose up the page): centre (x, y)."""
    tm = text_mask(txt, font); th, tw = tm.shape; w = h * tw / th
    face = sel & (N[:, 2] * up > 0.6)
    u = (P[:, 0] - centre[0]) / w + 0.5; v = 0.5 - (P[:, 1] - centre[1]) / h
    ok = face & (u >= 0) & (u < 1) & (v >= 0) & (v < 1)
    a = np.zeros(len(P), np.float32)
    a[ok] = tm[(v[ok] * th).astype(int).clip(0, th - 1), (u[ok] * tw).astype(int).clip(0, tw - 1)]
    col[:] = col * (1 - a[:, None]) + colour * a[:, None]
    return ok.sum()

n = 0
fin = ids('vstab', 'rudder')
if LIV == 'tiger':
    for sd in (-1, 1):
        n += stamp(ids('vstab'), '\u2588\u2588\u2588', FONT, (-2.91, 1.397), 0.24, sd, np.array([20, 20, 21], np.float32) / 255)
        n += stamp(ids('cockpit_F', 'nose'), '\u2588\u2588', FONT, (2.30, 0.95), 0.22, sd, np.array([20, 20, 21], np.float32) / 255)
NUM = {'olive': '214', 'steel': '237', 'tiger': '251'}[LIV]     # 0.15.35 (Nick 22:15): 2 = the unit, then the airframe's own two digits
for sd in (-1, 1): n += stamp(ids('vstab'), NUM, FONT, (-2.91, 1.397), 0.19, sd, MARK)          # big tail number, both sides (0.15.29: centred on the fin chord, clear of the leading edge)
for sd in (-1, 1): n += stamp(ids('vstabTip'), 'UL-2', FONT_S, (-2.95, 1.967), 0.066, sd, MARK)
for sd in (-1, 1): n += stamp(ids('cockpit_F', 'nose'), NUM[1:], FONT, (2.30, 0.95), 0.18, sd, MARK)  # tactical number on the pod
for sx in (-1, 1): n += stamp_top(ids('hstab_L' if sx < 0 else 'hstab_R'), 'NO STEP', FONT_S, (sx * 0.28, -2.80), 0.055, STEP)
for sd in (-1, 1): n += stamp(ids('cockpit_F'), 'RESCUE', FONT_S, (1.28, 0.72), 0.045, sd, np.array([210, 170, 40], np.float32) / 255)
print('stamped texels', n)
# ---- 0.15.35 weathering (Nick 22:16): a worked engine. Oil mist and drips settle on the skin around and under the engine
# (the rear pod, the keel frame, the stub), thrown back and down by the prop wash; exhaust soot fans aft from the stack
# onto the fin root and inner tailplanes. Plus faint rain runs on the pod sides and boot wear on the cockpit sills.
# Kept light, like a well used but looked after machine. Applied after the markings so it runs over them.
ENG = np.array([0.0, -1.48, 1.78], np.float32); EXH = np.array([0.0, -1.75, 1.41], np.float32)
up = N[:, 2]
dE = np.linalg.norm((P - ENG) * np.array([1.0, 0.6, 0.7], np.float32), axis=1)
streak_y = vnoise(P * np.array([5.0, 0.35, 5.0], np.float32), 0.05, 61)                 # runs stretched fore and aft
patchy = np.clip(0.55 + 0.6 * vnoise(P, 0.18, 62) + 0.35 * streak_y, 0, 1)
oil = np.exp(-(dE / 0.95) ** 2) * patchy * (0.65 + 0.35 * np.clip(up, 0, 1))
drip = np.clip(-N[:, 1], 0, 1) * np.exp(-(dE / 1.2) ** 2) * np.clip(vnoise(P * np.array([7, 7, 0.5], np.float32), 0.05, 63) * 1.6, 0, 1)   # aft faces, vertical drips
oil = np.clip(oil + 0.6 * drip, 0, 1) * 0.42
dx = EXH[1] - P[:, 1]; behind = dx > 0
rp = np.hypot(P[:, 0], P[:, 2] - (EXH[2] - 0.06 * dx))
soot = behind * np.exp(-(rp / (0.10 + 0.10 * dx)) ** 2) * np.exp(-dx / 2.2) * np.clip(0.6 + 0.5 * vnoise(P * np.array([4, 0.4, 4], np.float32), 0.05, 64), 0, 1) * 0.40
OIL = np.array([38, 30, 22], np.float32) / 255; SOOT = np.array([30, 30, 31], np.float32) / 255
col = col * (1 - oil[:, None]) + OIL * oil[:, None]
col = col * (1 - soot[:, None]) + SOOT * soot[:, None]
PODS = ids('cockpit_F', 'nose', 'gunmount_R')
side = PODS * (np.abs(N[:, 0]) > 0.5)
rain = side * np.clip(vnoise(P * np.array([9, 9, 0.7], np.float32), 0.05, 65) * 2.0 - 0.6, 0, 1) * 0.06
col *= (1 - rain)[:, None]
sill = PODS * (P[:, 2] > 1.30) * (np.abs(P[:, 0]) > 0.24) * (P[:, 1] > -0.10) * (P[:, 1] < 2.10) * (up > 0.3)
wear = sill * np.clip((vnoise(P, 0.025, 66) + 0.25 * vnoise(P, 0.12, 67) - 0.15) / 0.12, 0, 1) * 0.35
WEAR = {'olive': np.array([104, 106, 84], np.float32) / 255, 'steel': np.array([150, 152, 154], np.float32) / 255, 'tiger': np.array([58, 56, 52], np.float32) / 255}[LIV]
col = col * (1 - wear[:, None]) + WEAR * wear[:, None]
if LIV == 'olive': col = col * (1 + 0.05 * np.clip(up, 0, 1))[:, None]                 # a touch of sun fade on top
print('weathering: oil', int((oil > 0.1).sum()), 'soot', int((soot > 0.08).sum()), 'rain', int((rain > 0.02).sum()), 'sill wear', int((wear > 0.1).sum()))

def save_rgb(a, name):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8).reshape(S, S, -1)).save(os.path.join(out, name))
srgb = np.where(col <= 0.0031308, col * 12.92, 1.055 * np.power(np.clip(col, 0, 1), 1 / 2.4) - 0.055)
# ---- 0.15.22 surface detail (Nick 04:04: better metallic and normal maps). A 3D height field (panel seams, flush
# screw heads, rib rivet rows, faint orange peel) is sampled along each texel's UV tangent frame, so the normal map
# follows the real surface and runs across UV seams. Metallic/smoothness: matte paint, slicker leading edges,
# sparse bare-metal chips on edges and fastener heads, duller dusty belly. Kept light, like stock.
T = m['tan'].reshape(-1, 3)[src].astype(np.float32); BS = m['bsg'].ravel()[src].astype(np.float32)
B = np.cross(N, T) * np.where(BS == 0, 1, BS)[:, None]
POD = ids('cockpit_F', 'nose', 'gunmount_R'); HS = ids('hstab_L', 'hstab_R', 'hstabTip_L', 'hstabTip_R', 'elevator_L', 'elevator_R', 'elevatorTip_L', 'elevatorTip_R')
VS = ids('vstab', 'rudder', 'vstabTip', 'rudderTip'); STUB = ids('UL2Airtrike'); GEAR = ids('gear_L', 'gear_R', 'gear_F')

def groove(d, w=0.0022): return -np.exp(-(d / w) ** 2)                 # panel line (negative = recessed)
def dots(a, b, pitch, r=0.0028):                                          # flush screw heads on a line: a along, b across
    da = a - np.round(a / pitch) * pitch; rr = (da ** 2 + b ** 2) / r ** 2
    return np.exp(-rr) * 0.55 - np.exp(-((np.sqrt(rr) - 1.15) / 0.35) ** 2) * 0.25   # slight dome with a ring
POD_Y = (0.30, 0.85, 1.40, 1.95)                                          # pod frame stations (seams around the pod)
PANEL = (0.45, 0.80, 0.76, 0.92)                                          # side access panel y0, y1, z0, z1
def height(Q):
    x, y, z = Q[:, 0], Q[:, 1], Q[:, 2]; h = np.zeros(len(Q), np.float32); mk = np.zeros(len(Q), np.float32)
    ang = np.arctan2(z - 0.95, x); rad = np.hypot(x, z - 0.95); arc = ang * np.maximum(rad, 0.2)
    for y0 in POD_Y:
        g = groove(y - y0); h += POD * g; mk += POD * (dots(arc, y - y0 - 0.011, 0.045) + dots(arc, y - y0 + 0.011, 0.045))
    side = POD * (np.abs(x) > 0.18)
    h += side * (y < 2.45) * groove(z - 0.70); mk += side * (y < 2.45) * dots(y, z - 0.712, 0.05)
    y0, y1, z0, z1 = PANEL; inb = (y > y0 - 0.01) & (y < y1 + 0.01) & (z > z0 - 0.01) & (z < z1 + 0.01)
    pe = np.minimum(np.minimum(np.abs(y - y0), np.abs(y - y1)) + np.where((z < z0) | (z > z1), 9, 0),
                    np.minimum(np.abs(z - z0), np.abs(z - z1)) + np.where((y < y0) | (y > y1), 9, 0))
    h += side * inb * groove(pe); mk += side * inb * ((np.abs(y - y0 - 0.018) < 0.004) | (np.abs(y - y1 + 0.018) < 0.004)) * dots(z, 0 * z, 0.05) * ((z > z0 + 0.01) & (z < z1 - 0.01))
    # tail surfaces: rib rivet rows (spanwise pitch 0.12) and a spar row
    for sel, sp, ch in ((HS, x, y), (VS, z, y), (STUB, x, y)):
        rib = sp - np.round(sp / 0.12) * 0.12; mk += sel * dots(ch, rib, 0.028, 0.0022) * 0.8
    mk += HS * dots(x, y + 2.80, 0.03, 0.0022) * 0.8 + VS * dots(z, y + 2.86, 0.03, 0.0022) * 0.8 + STUB * dots(x, y - 0.45, 0.03, 0.0022) * 0.8
    h += GEAR * groove(z - 0.50)
    return h * 0.0006 + mk * 0.00035 + vnoise(Q, 0.012, 21) * 0.00004, mk
e = 0.0012
h0, mk = height(P)
dT = (height(P + T * e)[0] - height(P - T * e)[0]) / (2 * e); dB = (height(P + B * e)[0] - height(P - B * e)[0]) / (2 * e)
nt = np.stack([-dT, -dB, np.ones(len(P), np.float32)], 1); nt /= np.linalg.norm(nt, axis=1, keepdims=True)
if LIV == 'olive': save_rgb(nt * 0.5 + 0.5, 'UL2_skin_n.png')
seam = np.clip(-h0 / 0.0006, 0, 1); fast = np.clip(mk, 0, 1)
col *= (1 - 0.18 * seam)[:, None]                                         # a little grime in the lines
lead = np.clip((N[:, 1] - 0.75) / 0.2, 0, 1) * (HS | VS | STUB | POD)    # forward-facing edges
chip = np.clip((vnoise(P, 0.03, 31) - 0.42) / 0.10, 0, 1) * np.clip(lead + fast * 0.8, 0, 1)
BARE = np.array([150, 150, 146], np.float32) / 255
col = col * (1 - 0.7 * chip[:, None]) + BARE * 0.7 * chip[:, None]
metal = np.clip(0.04 + 0.12 * fast + 0.75 * chip, 0, 1)
smooth = smooth + 0.03 * vnoise(P, 0.4, 41) + 0.10 * lead + 0.12 * fast + 0.25 * chip - 0.08 * seam
smooth -= 0.06 * np.clip((0.75 - P[:, 2]) / 0.3, 0, 1) * POD             # dusty belly
smooth = np.clip(smooth, 0.12, 0.6)
print('surface detail: seam texels', int((seam > 0.3).sum()), 'fastener', int((fast > 0.3).sum()), 'chips', int((chip > 0.3).sum()))
if LIV != 'olive':
    if LIV == 'steel': col = col * (1 - 0.7 * chip[:, None]) + col * 0.7 * chip[:, None] / np.maximum(BARE, 1e-3) * BARE   # chips = same steel
    save_rgb(col, f'UL2_skin_{LIV}_b.png'); print('wrote livery', LIV); sys.exit(0)
save_rgb(np.stack([metal, np.zeros(len(P)), np.zeros(len(P)), smooth], 1), 'UL2_skin_m.png')
save_rgb(col, 'UL2_skin_b.png')     # colours are authored as sRGB values
ao = 0.86 + 0.06 * vnoise(P, 0.6, 11)
save_rgb(np.repeat(ao[:, None], 3, 1), 'UL2_skin_ao.png')
# damage: 3D shards (Voronoi cells ~0.3 m), each opening at one of the stock levels; scorched colour, dark rims
rng = np.random.default_rng(5); lo, hi = P.min(0), P.max(0)
seeds = rng.uniform(lo, hi, (int(np.prod((hi - lo) / 0.3)) + 50, 3))
tree = cKDTree(seeds); d, k = tree.query(P, k=2)
LEV = np.array([0.40, 0.52, 0.66, 1.0], np.float32); SH = np.array([0.06, 0.30, 0.44, 0.20])
lev = LEV[rng.choice(4, len(seeds), p=SH)][k[:, 0]]
rim = np.clip((d[:, 1] - d[:, 0]) / 0.03, 0, 1)
dcol = np.array([48, 46, 43], np.float32) / 255 * (0.55 + 0.45 * rim)[:, None]
save_rgb(np.concatenate([dcol, lev[:, None]], 1), 'UL2_skin_dmg_b.png')
save_rgb(np.tile(np.array([0.5, 0.5, 1.0], np.float32), (len(P), 1)), 'UL2_skin_dmg_n.png')
print('wrote', out)
