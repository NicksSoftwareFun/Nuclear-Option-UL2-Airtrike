# UL-2 status paper doll (0.15.29, Nick 12:29: "custom paper doll ... stylize it a bit for readability").
# Side view, nose right, from the Unity prefab meshes (ul2_doll_proj.json: world verts per renderer, grouped by part).
# Stylised: the rotor blades are spread flat either side of the hub (left blade left, right blade right), the tailplane
# is a small top view under the tail, left/right pairs that overlap in profile are split (gear legs front/rear, engine
# cylinder banks upper/lower). One full-frame sprite per part, named exactly as the part (how StatusDisplay finds it).
import json, os, sys, numpy as np
from PIL import Image, ImageDraw, ImageFilter
SRC, OUT = sys.argv[1], sys.argv[2]; os.makedirs(OUT, exist_ok=True)
P = json.load(open(SRC)); S = 512; SS = 4
G = {}
for k, d in P.items():
    part, name, act = k.split('|')
    if name in ('chocks',) or act != 'True': continue
    G.setdefault(part, []).append((name, np.array(d['V'], np.float32), np.array(d['T'], np.int32).reshape(-1, 3)))
SC = 64.0; ZC = -0.30; R0 = 92; YTOP = 2.30
def side(V): return np.stack([(256 + (V[:, 2] - ZC) * SC), R0 + (YTOP - V[:, 1]) * SC], 1)
HUBX, HUBR = 256 + (-0.97 - ZC) * SC, R0 + (YTOP - 2.04) * SC
BS = 44.0
def blade(V): return np.stack([HUBX + V[:, 0] * BS, HUBR + (V[:, 2] + 0.97) * BS * 1.6], 1)
TAILX, TAILR = 256 + (-2.95 - ZC) * SC, R0 + (YTOP + 1.55) * SC
TS = 52.0
def tailtop(V): return np.stack([TAILX + (V[:, 2] + 2.92) * TS, TAILR + V[:, 0] * TS], 1)   # top view turned nose right: forward = right, left half up
def rast(polys):
    im = Image.new('L', (S * SS, S * SS), 0); d = ImageDraw.Draw(im)
    for XY, T in polys:
        for t in T: d.polygon([tuple(XY[i] * SS) for i in t], fill=255)
    return np.array(im.resize((S, S), Image.BOX)).astype(np.float32) / 255 > 0.45
def part(n, f=side, names=None, skin=False):
    out = []
    for nm, V, T in G.get(n, []):
        if names is not None and nm not in names: continue
        if skin: T = T[(np.abs(V[T][:, :, 0]) > 0.26).all(1)]     # outer skin only: seats, frames and guns inside the pod are left out
        out.append((f(V), T))
    return rast(out)
def fill(m):   # close and fill each column between its top and bottom
    out = np.zeros_like(m)
    for c in range(S):
        r = np.nonzero(m[:, c])[0]
        if len(r): out[r.min():r.max() + 1, c] = True
    return out
def rect(x0, y0, x1, y1):
    m = np.zeros((S, S), bool); m[int(y0):int(y1), int(x0):int(x1)] = True; return m
names = []; lab = np.full((S, S), -1, np.int32)
def paint(n, m):
    if n not in names: names.append(n)
    lab[m] = names.index(n)
rows = np.arange(S)[:, None].repeat(S, 1); cols = np.arange(S)[None, :].repeat(S, 0)
# back to front
paint('fuselage', part('fuselage'))
paint('tailboom_R', part('tailboom_R'))
paint('tailboom_L', part('tailboom_L'))
paint('engine_core', part('engine_core'))
eng = part('engine_L') | part('engine_R'); emid = R0 + (YTOP - 0.55) * SC
paint('engine_L', eng & (rows < emid)); paint('engine_R', eng & (rows >= emid))
paint('intake', part('intake'))
paint('mast', part('mast'))
paint('propHub', part('propHub') | rect(256 + (-2.10 - ZC) * SC, R0 + (YTOP - 1.62) * SC, 256 + (-1.98 - ZC) * SC, R0 + (YTOP - 0.03) * SC))
# the pod as a clean profile from the model's own section table (bottom to the cockpit sill, coaming and nose to the top)
import re
src = open('/mnt/project-files/ul2/art/scripts/proto/ul2_proto.py').read()
ns = {}; exec(src[src.index('POD = ['):src.index('NS = 40')], {'math': __import__('math')}, ns)
POD, sill = ns['POD'], ns['sill']
skinV = np.concatenate([V for n in ('cockpit_F', 'nose') for nm, V, T in G[n] if nm == n])
DZ = skinV[:, 2].max() - POD[0][0]
noseV = np.concatenate([V for nm, V, T in G['nose']]); nz0 = noseV[:, 2].min() - DZ
DY = noseV[:, 1].max() - max(p[3] for p in POD if p[0] >= nz0)
poly_top = [(y, min(zt, sill(y)) if 0.10 <= y <= 2.22 else zt) for y, hw, zb, zt in POD]
poly = [(y + DZ, z + DY) for y, z in poly_top] + [(y + DZ, zb + DY) for y, hw, zb, zt in reversed(POD)]
im = Image.new('L', (S * SS, S * SS), 0)
ImageDraw.Draw(im).polygon([((256 + (z - ZC) * SC) * SS, (R0 + (YTOP - y) * SC) * SS) for z, y in poly], fill=255)
podm = np.array(im.resize((S, S), Image.BOX)) > 115
zr = lambda z: 256 + (z - ZC) * SC
nose_z = min(V[:, 2].min() for nm, V, T in G['nose']); cf_z = max(V[:, 2].max() for nm, V, T in G['cockpit_R'] if nm == 'cockpit_R')
paint('cockpit_R', podm & (cols < zr(cf_z))); paint('cockpit_F', podm & (cols >= zr(cf_z)) & (cols < zr(nose_z))); paint('nose', podm & (cols >= zr(nose_z)))
print('pod offsets', DZ, DY)
paint('UL2Airtrike', part('UL2Airtrike'))
gear = part('nacelle_L') | part('nacelle_R'); gx = 256 + (-0.67 - ZC) * SC
paint('nacelle_L', gear & (cols >= gx)); paint('nacelle_R', gear & (cols < gx))
gy = R0 + (YTOP + 0.38) * SC
paint('gunmount_R', rect(256 + (1.20 - ZC) * SC, gy - 2.5, 256 + (2.86 - ZC) * SC, gy + 2.5))
for n in ('vstab1_L', 'rudder_L', 'vstab1_R', 'rudder_R'): paint(n, part(n))
# rotor spread flat, hub in the middle
paint('rotorHead', rast([(blade(V), T) for nm, V, T in G['rotorHead'] if nm == 'spin_hub']) | (np.hypot(cols - HUBX, rows - HUBR) < 9))
for s in 'LR':
    for k in (1, 2, 3):
        paint(f'wing{k}_{s}', rast([(blade(V), T) for nm, V, T in G['rotorHead'] if nm == f'spin_{s}_' + ('root', 'mid', 'tip')[k - 1]]))
# tailplane top view inset under the tail
for n in ('hstab_L', 'hstabTip_L', 'elevator_L', 'elevatorTip_L', 'hstab_R', 'hstabTip_R', 'elevator_R', 'elevatorTip_R'): paint(n, part(n, tailtop))
def ero(m, r):
    im = Image.fromarray((m * 255).astype(np.uint8))
    for _ in range(r): im = im.filter(ImageFilter.MinFilter(3))
    return np.array(im) > 127
line = np.zeros((S, S), np.float32)
for i, n in enumerate(names):
    m = lab == i
    f = ero(m, 1); line = np.maximum(line, (m & ~f).astype(np.float32))
    fa = np.array(Image.fromarray((f * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.4)))
    im = np.zeros((S, S, 4), np.uint8); im[..., :3] = 170; im[..., 3] = fa
    Image.fromarray(im).save(OUT + '/UL2_status_' + n + '.png'); print(n, int(f.sum()))
line = np.clip(np.array(Image.fromarray((line * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.5))).astype(np.float32) / 255 * 1.6, 0, 1)
bg = np.zeros((S, S, 4), np.uint8); bg[..., :3] = 235; bg[..., 3] = (line * 255).astype(np.uint8)
Image.fromarray(bg).save(OUT + '/UL2_status.png')
open(OUT + '/order.txt', 'w').write('\n'.join(names)); print(len(names), 'parts')
# preview: in-game look (yellow parts on the HUD's dark backdrop), a few damaged
pv = np.zeros((S, S, 3), np.float32); pv[...] = (30, 34, 40)
dmg = {'wing3_L': (1, 0.5, 0), 'rudder_R': (1, 0, 0), 'elevatorTip_R': (0.7, 0, 0.25), 'engine_R': (1, 0.6, 0)}
for i, n in enumerate(names):
    a = np.array(Image.open(OUT + '/UL2_status_' + n + '.png'))[..., 3:4].astype(np.float32) / 255
    c = np.array(dmg.get(n, (1, 1, 0)), np.float32) * 170 / 255 * 255
    pv = pv * (1 - a * 0.85) + c * a * 0.85
pv = pv * (1 - line[..., None]) + 235 * line[..., None]
Image.fromarray(pv.clip(0, 255).astype(np.uint8)).resize((1024, 1024), Image.LANCZOS).save(OUT + '/preview.png')
