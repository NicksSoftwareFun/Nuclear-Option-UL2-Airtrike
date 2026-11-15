"""UL-2 skin damage maps in the P220 style (Nick 2026-11-15): light bare-metal colour with no darkening (no soot, no dark
rims) and a shredded hull: shard edges are warped 3D Voronoi cuts (broad bend + fine ragged warp), big shards with
smaller ones inside, each opening at one of the stock levels (alpha = hull level at which it opens).
Usage: python3 ul2_dmg.py <maps.npz> <outdir>   writes UL2_skin_dmg_b.png (RGBA) and UL2_skin_dmg_n.png"""
import sys, os
import numpy as np
from PIL import Image
sys.path.insert(0, '/mnt/project-files/tu22/art/scripts/tex')
sys.path.insert(0, '/mnt/project-files/a220/art/scripts/liv')
from tex_paint import vnoise
from tex_feat import hash01
from p220_dmg_alpha import cells, LEVELS, SHARE

BIG = np.array([0.30, 0.40, 0.30], np.float32)      # UL-2 is small: shards scaled down from the P220 (0.5/0.75/0.5)
SMALL = np.array([0.12, 0.15, 0.12], np.float32)
m = np.load(sys.argv[1]); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
S = m['pos'].shape[0]; idx = m['idx'].ravel()
P = m['pos'].reshape(-1, 3)[idx].astype(np.float32)
W = np.stack([0.05 * vnoise(P, 0.6, seed=41 + k, octaves=2) + 0.012 * vnoise(P, 0.035, seed=51 + k, octaves=2)
              for k in range(3)], 1)
Q = P + W
big = cells(Q, BIG, 61); r = hash01(big[:, 0], big[:, 1], big[:, 2] + 7)
lvl = np.searchsorted(np.cumsum(SHARE), r).clip(0, len(LEVELS) - 1)
small = cells(Q, SMALL, 62); r2 = hash01(small[:, 0], small[:, 1], small[:, 2] + 11)
lvl = np.where(r2 < 0.25, np.maximum(lvl - 1, 0), lvl)
lvl = np.where(r2 > 0.95, len(LEVELS) - 1, lvl)
alpha = LEVELS[lvl]
# colour: light metal with a faint heat tint, never darker than the heat tone
n1 = vnoise(P, 1.0, seed=21, octaves=3); n2 = vnoise(P, 0.4, seed=22, octaves=2)
burn = np.clip(0.35 + 0.6 * n1 + 0.2 * n2, 0, 0.6)
metal = np.array([168, 168, 165], np.float32) / 255; heat = np.array([140, 134, 126], np.float32) / 255
col = metal * (1 - burn[:, None]) + heat * burn[:, None]
col *= (1 + 0.03 * n2)[:, None]
# no floating skin (Nick 21:43): a ragged solid band runs round every UV island edge (the part edges, where the skin is
# held), and at each level every piece of skin still standing must connect to that band; pieces that would be left
# hanging in the air drop to the next lower level, so they go no later than the skin around them.
from scipy import ndimage
cov = m['cov'].astype(bool); ppm = float(m['ppm'])
A = alpha.reshape(S, S).copy()
edge = cov & ~ndimage.binary_erosion(cov)
dist = ndimage.distance_transform_edt(~edge)
wid = (0.025 + 0.025 * np.clip(0.5 + 0.9 * vnoise(P, 0.08, seed=73, octaves=2), 0, 1)).reshape(S, S) * ppm
A = np.where(cov & (dist < wid), 1.0, A)
rim = ndimage.binary_dilation(edge, iterations=1)
st = np.ones((3, 3), bool)
for i in range(len(LEVELS) - 1, 0, -1):
    t = LEVELS[i] - 1e-3
    lab, n = ndimage.label(cov & (A >= t), structure=st)
    held = np.unique(lab[rim & (lab > 0)])
    ok = np.zeros(n + 1, bool); ok[held] = True
    drop = (lab > 0) & ~ok[lab]
    A = np.where(drop, np.minimum(A, LEVELS[i - 1]), A)
    print('level %.2f: %d pieces, %d floating -> %.2f (%d texels)' % (LEVELS[i], n, n + 1 - len(held) - 1, LEVELS[i - 1], int(drop.sum())))
alpha = A.ravel()[idx]          # gutter texels copy their island texel again
img = np.concatenate([np.clip(col, 0, 1), alpha[:, None]], 1)
Image.fromarray((img * 255 + 0.5).astype(np.uint8).reshape(S, S, 4), 'RGBA').save(os.path.join(out, 'UL2_skin_dmg_b.png'))
# normal: light crumple
e = 0.004
h = lambda X: 0.004 * vnoise(X, 0.2, seed=31, octaves=3)
T = m['tan'].reshape(-1, 3)[idx].astype(np.float32); N = m['nrm'].reshape(-1, 3)[idx].astype(np.float32)
BS = m['bsg'].ravel()[idx].astype(np.float32); B = np.cross(N, T) * np.where(BS == 0, 1, BS)[:, None]
h0 = h(P); dT = (h(P + T * e) - h0) / e; dB = (h(P + B * e) - h0) / e
n = np.stack([-dT, -dB, np.ones(len(P), np.float32)], 1); n /= np.linalg.norm(n, axis=1, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8).reshape(S, S, 3), 'RGB').save(os.path.join(out, 'UL2_skin_dmg_n.png'))
cv = m['cov'].ravel().astype(bool)
print('alpha share', [round(float((alpha[cv] == L).mean()), 3) for L in LEVELS], 'col mean', col[cv].mean(0) * 255)
