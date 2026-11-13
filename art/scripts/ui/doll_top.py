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
SC = 58.0; ZTOP = 3.05; R0 = 50
def top(V): return np.stack([256 + V[:, 0] * SC, R0 + (ZTOP - V[:, 2]) * SC], 1)
zr = lambda z: R0 + (ZTOP - z) * SC; xc = lambda x: 256 + x * SC
def rast(polys):
    im = Image.new('L', (S * SS, S * SS), 0); d = ImageDraw.Draw(im)
    for XY, T in polys:
        for t in T: d.polygon([tuple(XY[i] * SS) for i in t], fill=255)
    return np.array(im.resize((S, S), Image.BOX)).astype(np.float32) / 255 > 0.45
def part(n, f=top, names=None):
    return rast([(f(V), T) for nm, V, T in G.get(n, []) if names is None or nm in names])
def rect(x0, y0, x1, y1):
    m = np.zeros((S, S), bool); m[int(round(y0)):int(round(y1)), int(round(x0)):int(round(x1))] = True; return m
def poly(pts):
    im = Image.new('L', (S * SS, S * SS), 0); ImageDraw.Draw(im).polygon([(x * SS, y * SS) for x, y in pts], fill=255)
    return np.array(im.resize((S, S), Image.BOX)) > 115
names = []; lab = np.full((S, S), -1, np.int32)
def paint(n, m):
    if n not in names: names.append(n)
    lab[m] = names.index(n)
rows = np.arange(S)[:, None].repeat(S, 1); cols = np.arange(S)[None, :].repeat(S, 0)
# Top view, nose up (Nick 15:13). Stylised for readability: rotor blades laid out sideways just behind the main gear,
# the fin and rudder folded flat to the right of the tail, the mast as a ring round the rotor hub.
paint('fuselage', part('fuselage'))
paint('UL2Airtrike', part('UL2Airtrike'))
gear = part('nacelle_L') | part('nacelle_R')
paint('nacelle_L', gear & (cols < 256)); paint('nacelle_R', gear & (cols >= 256))
# pod outline from the model's section table (half widths), split into its three parts by station
src = open('/mnt/project-files/ul2/art/scripts/proto/ul2_proto.py').read()
ns = {}; exec(src[src.index('POD = ['):src.index('NS = 40')], {'math': __import__('math')}, ns); POD = ns['POD']
noseV = np.concatenate([V for nm, V, T in G['nose']]); DZ = noseV[:, 2].max() - POD[0][0]
pod = poly([(xc(hw), zr(y + DZ)) for y, hw, zb, zt in POD] + [(xc(-hw), zr(y + DZ)) for y, hw, zb, zt in reversed(POD)])
nose_z = noseV[:, 2].min(); cr_z = max(V[:, 2].max() for nm, V, T in G['cockpit_R'] if nm == 'cockpit_R')
paint('cockpit_R', pod & (rows > zr(cr_z))); paint('cockpit_F', pod & (rows <= zr(cr_z)) & (rows > zr(nose_z))); paint('nose', pod & (rows <= zr(nose_z)))
paint('gunmount_R', rect(xc(0.40), zr(2.60), xc(0.50), zr(1.20)))
seat3 = part('intake') & ~(np.abs(cols - 256) > 0.24 * SC); paint('intake', seat3)
# booms drawn as one widened spar behind the prop (the front boom sits under the engine in a true top view)
paint('tailboom_L', rect(xc(-0.07), zr(-2.13), xc(0.07), zr(-2.50)))
paint('tailboom_R', rect(xc(-0.06), zr(-2.50), xc(0.06), zr(-3.20)))
paint('engine_core', part('engine_core'))
paint('engine_L', part('engine_L')); paint('engine_R', part('engine_R'))
paint('propHub', rect(xc(-0.86), zr(-2.00), xc(0.86), zr(-2.13)) | (np.hypot(cols - 256, rows - zr(-2.0)) < 7))
for n in ('hstab_L', 'hstabTip_L', 'elevator_L', 'elevatorTip_L', 'hstab_R', 'hstabTip_R', 'elevator_R', 'elevatorTip_R'): paint(n, part(n))
# fin and rudder folded flat to the right (side profile, height to the right)
FX = xc(1.18)
def fin(V): return np.stack([FX + (V[:, 1] + 0.30) * SC, R0 + (ZTOP - V[:, 2]) * SC], 1)
for n in ('vstab1_L', 'rudder_L', 'vstab1_R', 'rudder_R'): paint(n, part(n, fin))
# mast as a ring round the hub, then the hub
HX, HR = 256, zr(-0.97)
ring = (np.hypot(cols - HX, rows - HR) < 15) & (np.hypot(cols - HX, rows - HR) >= 10)
paint('mast', part('mast') & ~(np.hypot(cols - HX, rows - HR) < 15) | ring)
BR = zr(-1.32); RIN = 0.55 * SC; K = (246 - RIN) / 4.11
paint('rotorHead', (np.hypot(cols - HX, rows - HR) < 9) | rect(HX - RIN, BR - 4, HX + RIN, BR + 4) | rect(HX - 4, HR, HX + 4, BR))
for s, sg in (('L', -1), ('R', 1)):
    for k in (1, 2, 3):
        V = np.concatenate([V for nm, V, T in G['rotorHead'] if nm == f'spin_{s}_' + ('root', 'mid', 'tip')[k - 1]])
        x0, x1 = np.abs(V[:, 0]).min(), np.abs(V[:, 0]).max()
        a, b = HX + sg * (RIN + (x0 - 0.5) * K), HX + sg * (RIN + (x1 - 0.5) * K)
        w = 10 if k < 3 else 8
        paint(f'wing{k}_{s}', rect(min(a, b), BR - w, max(a, b), BR + w))
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
