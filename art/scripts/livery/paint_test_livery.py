"""Test livery painted ONLY from the Livery Bundle FBX (proves the bundle's UVs line up with the game skin).
Usage: python3 -I paint_test_livery.py -- "<UL2 Exterior.fbx>" <out.png>
Racing scheme from 3D position: gloss navy top, white belly, a red and gold cheat line along the pod and booms, a
checkerboard fin, red tailplane tips. Each skin triangle is rasterised in UV space at 2048 px."""
import bpy, sys, numpy as np
from PIL import Image
from scipy import ndimage
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=a[0])
ob = bpy.data.objects['UL2 Skin']; me = ob.data; mw = ob.matrix_world
me.calc_loop_triangles()
uvl = me.uv_layers.active.data
S = 2048
img = np.zeros((S, S, 3), np.float32); cov = np.zeros((S, S), bool)
NAVY = np.array([18, 32, 68]) / 255; WHITE = np.array([236, 236, 232]) / 255; RED = np.array([196, 28, 36]) / 255
GOLD = np.array([222, 170, 40]) / 255; BLACK = np.array([16, 16, 18]) / 255
def colour(P, N):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    c = np.where((z > 0.95 + 0.06 * np.sin(y * 2.0))[:, None], NAVY, WHITE)
    stripe = np.abs(z - (0.92 + 0.06 * np.sin(y * 2.0)))
    c = np.where((stripe < 0.045)[:, None], RED, c); c = np.where(((stripe >= 0.045) & (stripe < 0.065))[:, None], GOLD, c)
    fin = (y < -2.6) & (z > 1.15) & (np.abs(x) < 0.15)
    chk = ((np.floor(y / 0.12) + np.floor(z / 0.12)) % 2 == 0)
    c = np.where((fin & chk)[:, None], BLACK, np.where(fin[:, None], WHITE, c))
    c = np.where((np.abs(x) > 0.75)[:, None] & (y < -2.5)[:, None], RED, c)       # tailplane tips
    return c
for t in me.loop_triangles:
    uv = np.array([uvl[li].uv[:] for li in t.loops]) * S; uv[:, 1] = S - uv[:, 1]
    P3 = np.array([(mw @ me.vertices[v].co)[:] for v in t.vertices])
    x0, y0 = np.floor(uv.min(0)).astype(int) - 1; x1, y1 = np.ceil(uv.max(0)).astype(int) + 1
    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, S - 1), min(y1, S - 1)
    if x1 < x0 or y1 < y0: continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    q = np.stack([gx.ravel(), gy.ravel()], 1)
    A, B, C = uv; v0 = B - A; v1 = C - A; v2 = q - A
    d = v0[0] * v1[1] - v1[0] * v0[1]
    if abs(d) < 1e-9: continue
    l1 = (v2[:, 0] * v1[1] - v1[0] * v2[:, 1]) / d; l2 = (v0[0] * v2[:, 1] - v2[:, 0] * v0[1]) / d; l0 = 1 - l1 - l2
    e = -0.6 / max(1.0, np.sqrt(abs(d)))
    ins = (l0 >= e) & (l1 >= e) & (l2 >= e)
    if not ins.any(): continue
    P = l0[ins, None] * P3[0] + l1[ins, None] * P3[1] + l2[ins, None] * P3[2]
    px = (q[ins, 0] - 0.5).astype(int); py = (q[ins, 1] - 0.5).astype(int)
    img[py, px] = colour(P, None); cov[py, px] = True
_, (iy, ix) = ndimage.distance_transform_edt(~cov, return_indices=True)
img = img[iy, ix]
Image.fromarray((img * 255 + 0.5).astype(np.uint8)).save(a[1])
print('covered texels', int(cov.sum()))
