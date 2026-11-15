"""UL-2 tyre textures (Nick 2026-11-15 16:42: wheels too bland, tread and tyre detail through the UV map).
UL2_tread_b/_n.png: one repeating tile of the tread band, u = round the tyre (two lug pitches per tile),
v = across the tread from one shoulder to the other. Staggered block tread for rough fields.
UL2_tyreside_b/_n.png: the sidewall as a flat disc (centre of the image = axle, edge = tyre radius) with fine
rim-protector ribs, raised moulded lettering and dust that gets heavier toward the tread.
Matte dark rubber like the stock vehicle tyres; colour + tangent-space normal (OpenGL, green up).
Usage: python3 tyre_tex.py <outdir>"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

out = sys.argv[1]; os.makedirs(out, exist_ok=True)
RUB, GROOVE, DUST = np.array([36, 36, 38.]), np.array([20, 20, 21.]), np.array([112, 100, 82.])
rng = np.random.default_rng(7)


def font(sz):
    for f in ('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf'):
        if os.path.exists(f): return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def normal_from_height(h, strength, wrap):
    if wrap:
        dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5; dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    else:
        dx = np.gradient(h, axis=1); dy = np.gradient(h, axis=0)
    # image rows run down, texture v runs up: flip the y slope so green = up in UV space
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return (np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5) * 255


def noise(shape, blur):
    n = Image.fromarray((rng.random(shape) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(blur))
    a = np.asarray(n, float) / 255.; return (a - a.min()) / (a.max() - a.min() + 1e-6)


def blur_wrap(a, r):
    """gaussian blur that wraps left-right (the tile repeats round the tyre)"""
    pad = int(r * 4) + 2; big = np.concatenate([a[:, -pad:], a, a[:, :pad]], 1)
    im = Image.fromarray((np.clip(big, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r))
    return np.asarray(im, float)[:, pad:-pad] / 255.


# ---------------------------------------------------------------- tread tile
TW, TH = 512, 512
H = Image.new('L', (TW, TH), 0); d = ImageDraw.Draw(H)
def block(u0, u1, v0, v1):
    # u, v in tile fractions; v measured across the tread (0 = one shoulder); wraps in u
    for off in (-TW, 0, TW):
        d.rounded_rectangle((u0 * TW + off, v0 * TH, u1 * TW + off, v1 * TH), radius=10, fill=255)
for k in (0, 1):   # two pitches per tile
    u = k * 0.5
    block(u + 0.02, u + 0.36, -0.05, 0.30)        # shoulder blocks run off both edges onto the shoulder
    block(u + 0.02, u + 0.36, 0.70, 1.05)
    block(u + 0.27, u + 0.49, 0.37, 0.63)          # centre blocks, staggered half a pitch
    block(u + 0.26, u + 0.40, 0.30, 0.37)          # tie bars between centre and shoulder blocks
    block(u + 0.26, u + 0.40, 0.63, 0.70)
h = np.asarray(H, float) / 255.
# sipes: thin cuts across each shoulder block
S = Image.new('L', (TW, TH), 0); sd = ImageDraw.Draw(S)
for k in (0, 1):
    u = (k * 0.5 + 0.19) * TW
    sd.line((u - 6, 0.04 * TH, u + 6, 0.24 * TH), fill=255, width=5); sd.line((u + 6, 0.76 * TH, u - 6, 0.96 * TH), fill=255, width=5)
h = np.clip(h - np.asarray(S, float) / 255. * 0.7, 0, 1)
hb = blur_wrap(h, 2.2)
grain = blur_wrap(rng.random((TH, TW)), 0.8)
n = normal_from_height(hb * 7.0 + grain * 0.25, 1.0, True)
# colour: rubber on the blocks (top edges a touch lighter where they wear), dark grooves with a little packed dust
edge = np.clip(hb - blur_wrap(h, 6.0), 0, 1)
dust = blur_wrap(rng.random((TH, TW)), 6.0); dust = (dust - dust.min()) / (dust.max() - dust.min())
col = RUB[None, None] * hb[..., None] + GROOVE[None, None] * (1 - hb[..., None])
col += edge[..., None] * 14
col = col * (1 - (1 - hb[..., None]) * dust[..., None] * 0.35) + DUST[None, None] * (1 - hb[..., None]) * dust[..., None] * 0.35
col += (grain[..., None] - 0.5) * 6
Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).save(os.path.join(out, 'UL2_tread_b.png'))
Image.fromarray(np.clip(n, 0, 255).astype(np.uint8)).save(os.path.join(out, 'UL2_tread_n.png'))

# ---------------------------------------------------------------- sidewall disc
SW = 1024; C = SW / 2
yy, xx = np.mgrid[0:SW, 0:SW]; rr = np.hypot(xx - C + 0.5, yy - C + 0.5) / C   # 1 = tyre radius
hs = np.zeros((SW, SW))
# fine concentric ribs on the rim protector band and a smooth bead line
hs += np.where((rr > 0.80) & (rr < 0.88), 0.5 + 0.5 * np.sin(rr * 2 * math.pi * 160), 0) * 0.6
hs += np.clip(1 - np.abs(rr - 0.585) / 0.006, 0, 1) * 0.8
# raised lettering round the wall (generic sizes, no maker name), twice, reading clockwise from outside
L = Image.new('L', (SW, SW), 0)
labels = ['6.00-6   6 PLY RATING', 'TUBE TYPE   UL-2']
fnt = font(30); r_txt = 0.70 * C
for half, text in enumerate(labels):
    widths = [fnt.getbbox(ch)[2] - fnt.getbbox(ch)[0] if ch != ' ' else 12 for ch in text]
    total = sum(widths) + 3 * (len(text) - 1); ang = -math.pi / 2 + half * math.pi - (total / r_txt) / 2
    for ch, wch in zip(text, widths):
        a = ang + (wch / 2) / r_txt
        if ch != ' ':
            t = Image.new('L', (60, 60), 0); ImageDraw.Draw(t).text((30, 30), ch, fill=255, font=fnt, anchor='mm')
            t = t.rotate(-math.degrees(a) - 90, resample=Image.BICUBIC)
            x, y = C + r_txt * math.cos(a), C + r_txt * math.sin(a); L.paste(255, (int(x - 30), int(y - 30)), t)
        ang += (wch + 3) / r_txt
lt = np.asarray(L.filter(ImageFilter.GaussianBlur(0.8)), float) / 255.
hs += lt * 1.0
grain = noise((SW, SW), 0.8)
ns = normal_from_height(np.asarray(Image.fromarray((np.clip(hs, 0, 2) * 120).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.0)), float) / 120. * 5.0 + grain * 0.2, 1.0, False)
# colour: rubber, lettering a little lighter (scuffed), dust that builds toward the tread and settles low on the wall
dust = np.clip((rr - 0.6) / 0.4, 0, 1) ** 1.5 * (0.55 + 0.45 * noise((SW, SW), 10))
cs = np.ones((SW, SW, 3)) * RUB
cs += lt[..., None] * 18
cs = cs * (1 - dust[..., None] * 0.28) + DUST * dust[..., None] * 0.28
cs += (grain[..., None] - 0.5) * 6
# yellow balance dot near the bead, once
by, bx = C - 0.63 * C, C
cs[np.hypot(xx - bx, yy - by) < 7] = (190, 150, 40)
Image.fromarray(np.clip(cs, 0, 255).astype(np.uint8)).save(os.path.join(out, 'UL2_tyreside_b.png'))
Image.fromarray(np.clip(ns, 0, 255).astype(np.uint8)).save(os.path.join(out, 'UL2_tyreside_n.png'))
print('wrote tyre textures to', out)
