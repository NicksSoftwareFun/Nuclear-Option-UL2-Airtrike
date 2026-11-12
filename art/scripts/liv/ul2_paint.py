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

col = np.tile(OLIVE, (len(P), 1))
col *= (1 + 0.035 * vnoise(P, 1.2, 3) + 0.02 * vnoise(P, 0.25, 7))[:, None]      # faint mottling of matte paint
col *= (1 - 0.06 * np.clip((0.75 - P[:, 2]) / 0.3, 0, 1) * ids('cockpit_F', 'nose', 'gunmount_R'))[:, None]   # slightly dusty belly
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
for sd in (-1, 1): n += stamp(ids('vstab'), '214', FONT, (-2.875, 1.397), 0.21, sd, WHITE)         # big tail number, both sides
for sd in (-1, 1): n += stamp(ids('vstabTip'), 'UL-2', FONT_S, (-2.95, 1.967), 0.066, sd, WHITE)
for sd in (-1, 1): n += stamp(ids('cockpit_F', 'nose'), '14', FONT, (2.30, 0.95), 0.18, sd, WHITE)  # tactical number on the pod
for sx in (-1, 1): n += stamp_top(ids('hstab_L' if sx < 0 else 'hstab_R'), 'NO STEP', FONT_S, (sx * 0.28, -2.80), 0.055, BLACK)
for sd in (-1, 1): n += stamp(ids('cockpit_F'), 'RESCUE', FONT_S, (1.28, 0.72), 0.045, sd, np.array([210, 170, 40], np.float32) / 255)
print('stamped texels', n)

def save_rgb(a, name):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8).reshape(S, S, -1)).save(os.path.join(out, name))
srgb = np.where(col <= 0.0031308, col * 12.92, 1.055 * np.power(np.clip(col, 0, 1), 1 / 2.4) - 0.055)
srgb = col      # colours above are authored as sRGB values already
save_rgb(srgb, 'UL2_skin_b.png')
save_rgb(np.tile(np.array([0.5, 0.5, 1.0], np.float32), (len(P), 1)), 'UL2_skin_n.png')
save_rgb(np.stack([np.zeros(len(P)), np.zeros(len(P)), np.zeros(len(P)), smooth], 1), 'UL2_skin_m.png')
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
