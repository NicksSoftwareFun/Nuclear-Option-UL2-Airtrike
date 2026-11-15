"""PALA Camel Corps tail badge for the UL-2 (Nick 2026-11-15, from his sketch): shield, black chief, camel standing
over a star, scimitar beneath. Flat PALA palette plus gold (Nick 00:48: black, red, white and gold): red field, black chief with a gold keyline, gold star, white camel and blade, gold guard and pommel.
Usage: python3 camel_badge.py <out.png> [height_px]"""
import sys, numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt
SS = 4; W, H = 1000, 1150
RED = (146, 4, 0); WHITE = (230, 230, 230); BLACK = (0, 0, 0); GOLD = (206, 160, 52)
def cr(pts, closed=True, n=12):
    p = np.array(pts, float); out = []
    m = len(p) if closed else len(p) - 1
    for i in range(m):
        p0, p1, p2, p3 = p[i - 1] if (closed or i) else p[0], p[i], p[(i + 1) % len(p)], p[(i + 2) % len(p)] if (closed or i + 2 < len(p)) else p[-1]
        for t in np.linspace(0, 1, n, endpoint=False):
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    return out
def mask(polys):
    im = Image.new('L', (W * SS, H * SS), 0); d = ImageDraw.Draw(im)
    for pl in polys: d.polygon([(x * SS, y * SS) for x, y in pl], fill=255)
    return np.asarray(im) > 0
def grow(m, r): return distance_transform_edt(~m) <= r * SS
canvas = np.zeros((H * SS, W * SS, 4), np.uint8)
def put(m, col, line=0):
    if line: o = grow(m, line); canvas[o] = BLACK + (255,)
    canvas[m] = col + (255,)
# shield: gently arched top, straight slightly tapering sides, angled lower flanks to a point
top = [(110 + 780 * t, 150 - 70 * np.sin(np.pi * t)) for t in np.linspace(0, 1, 40)]
shield = top + [(872, 770), (500, 1090), (128, 770)]
inner = lambda pts, k: pts
put(mask([shield]), RED, 16)
# chief: black band across the top, its lower edge arched like the top
chief = top + [(900, 150), (900, 250)] + [(890 - 780 * t, 250 - 70 * np.sin(np.pi * (1 - t))) for t in np.linspace(0, 1, 40)][1:] + [(110, 250)]
cm = mask([chief]) & mask([shield]); canvas[cm] = BLACK + (255,)
# off-white keyline under the chief
kl = [(110 + 780 * t, 250 - 70 * np.sin(np.pi * t)) for t in np.linspace(0, 1, 60)]
klp = kl + [(x, y + 14) for x, y in kl[::-1]]
km = mask([klp]) & mask([shield]); canvas[km] = GOLD + (255,)
# star, point up
cx, cy, R, r = 500, 650, 235, 94
star = [(cx + (R if i % 2 == 0 else r) * np.sin(i * np.pi / 5), cy - (R if i % 2 == 0 else r) * np.cos(i * np.pi / 5)) for i in range(10)]
# scimitar under the star: blade centre line a quadratic curve from the guard to the tip
P0, P1, P2 = np.array([350., 878.]), np.array([560., 896.]), np.array([724., 862.])   # level: hilt and tip at one height, centred
t = np.linspace(0, 1, 80)[:, None]; c = (1 - t) ** 2 * P0 + 2 * (1 - t) * t * P1 + t * t * P2
dc = 2 * (1 - t) * (P1 - P0) + 2 * t * (P2 - P1); nrm = np.stack([dc[:, 1], -dc[:, 0]], 1); nrm /= np.linalg.norm(nrm, axis=1, keepdims=True)
wb = (15 * (1 - t ** 3) + 9 * np.sin(np.pi * t) ** 2).ravel()[:, None]          # widest towards the tip third, sharp tip
wt = (8 * (1 - t)).ravel()[:, None]
blade = [tuple(p) for p in (c + nrm * wb)] + [tuple(p) for p in (c - nrm * wt)[::-1]]
grip = [(292, 868), (340, 868), (340, 890), (292, 890)]
guard = [(340, 846), (356, 846), (356, 910), (340, 910)]
pommel = [(276, 864), (294, 864), (294, 894), (276, 894)]
put(mask([blade]), WHITE, 9)
put(mask([grip]), (60, 24, 14), 9); put(mask([guard, pommel]), GOLD, 9)
put(mask([star]), GOLD, 13)
# PALA triangle in the star: inverted red triangle with three white triangles (1-2 pattern of the roundel)
tr = [(cx - 62, cy - 30), (cx + 62, cy - 30), (cx, cy + 78)]
put(mask([tr]), RED, 9)
s = 34; wt3 = [[(x - s / 2, y + s * 0.87), (x + s / 2, y + s * 0.87), (x, y)] for x, y in ((cx - 25, cy - 22), (cx + 25, cy - 22), (cx, cy + 22))]
canvas[mask(wt3)] = WHITE + (255,)
# camel (dromedary) facing left, standing astride the star's top point
body = [(279, 304), (285, 316), (307, 320), (325, 318), (337, 330), (351, 352), (369, 374), (389, 388), (411, 394), (423, 408),
        (423, 450), (420, 488), (418, 500), (435, 500), (435, 488), (437, 450), (441, 412), (461, 398), (511, 400), (543, 394),
        (557, 404), (565, 448), (561, 488), (559, 500), (576, 500), (577, 488), (581, 446), (589, 416), (603, 398), (611, 370),
        (621, 384), (626, 412), (633, 410), (627, 370), (619, 342), (599, 300), (571, 262), (536, 248), (501, 258), (476, 288),
        (455, 318), (433, 332), (411, 325), (391, 305), (371, 284), (355, 272), (347, 260), (339, 261), (333, 272), (316, 276), (293, 286)]
far = [cr([(447, 416), (461, 414), (459, 452), (457, 488), (457, 500), (443, 500), (441, 488), (443, 452)]),
       cr([(575, 412), (591, 414), (597, 446), (597, 488), (597, 500), (583, 500), (581, 488), (583, 446)])]
fm = mask(far); put(fm, (200, 200, 200), 9)
put(mask([cr(body, n=10)]), WHITE, 11)
eye = mask([[(321 + 5 * np.cos(a), 284 + 4 * np.sin(a)) for a in np.linspace(0, 2 * np.pi, 16)]]); canvas[eye] = BLACK + (255,)
# keep the art inside the shield outline
outer = grow(mask([shield]), 16); canvas[~outer] = 0
hpx = int(sys.argv[2]) if len(sys.argv) > 2 else 1024
im = Image.fromarray(canvas, 'RGBA')
# premultiplied downscale so edges do not fringe
a = canvas[..., 3:4].astype(np.float32) / 255; pm = np.concatenate([canvas[..., :3] * a, a * 255], 2).astype(np.uint8)
im = Image.fromarray(pm, 'RGBA').resize((int(W * hpx / H), hpx), Image.LANCZOS)
arr = np.asarray(im).astype(np.float32); al = np.maximum(arr[..., 3:4] / 255, 1e-4)
arr[..., :3] = np.clip(arr[..., :3] / al, 0, 255); Image.fromarray(arr.astype(np.uint8), 'RGBA').save(sys.argv[1])
print('wrote', sys.argv[1])
