"""Six-pack dial faces for the pilot panel, drawn in the PB-22 Trident style (near-black dials, white ticks and
numbers, grey labels). Nick 2026-11-12 22:04: standard six-pack copied from the Trident.
Writes UL2_gf_<id>_img.png (256x256) for asi, adi, alt, eng, hdg, vsi; mirrored left-right like the copilot
pictures, because the Blender face UVs come out mirrored in Unity.
Usage: python3 gauge_faces.py <outdir>"""
import sys, os, math
from PIL import Image, ImageDraw, ImageFont
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
S = 256; C = S / 2; RR = S / 2 - 4
DIAL, WHITE, GREY, AMBER, RED, GREEN = (18, 18, 20), (235, 235, 230), (150, 150, 150), (235, 170, 50), (210, 50, 40), (60, 190, 80)
SKY, GND = (96, 136, 176), (74, 52, 34)


def font(sz):
    for f in ('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf'):
        if os.path.exists(f): return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def base():
    im = Image.new('RGB', (S, S), (40, 42, 45)); d = ImageDraw.Draw(im)
    d.ellipse((C - RR, C - RR, C + RR, C + RR), fill=DIAL); return im, d


def text(d, x, y, s, sz, col=WHITE):
    f = font(sz); b = d.textbbox((0, 0), s, font=f); d.text((x - (b[2] - b[0]) / 2, y - (b[3] - b[1]) / 2 - b[1]), s, fill=col, font=f)


def ticks(d, a0, a1, major, minor=4, r=RR):
    n = major * minor
    for k in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * k / n); big = k % minor == 0; r0 = r * (0.78 if big else 0.87)
        d.line((C + r0 * math.sin(a), C - r0 * math.cos(a), C + r * 0.96 * math.sin(a), C - r * 0.96 * math.cos(a)), fill=WHITE, width=4 if big else 2)


def numbers(d, a0, a1, vals, sz, r=RR):
    for k, v in enumerate(vals):
        a = math.radians(a0 + (a1 - a0) * k / (len(vals) - 1)); text(d, C + r * 0.58 * math.sin(a), C - r * 0.58 * math.cos(a), str(v), sz)


def arc(d, a0, a1, col, r=RR * 0.9, w=7):   # clock angles (0 = 12 o'clock)
    d.arc((C - r, C - r, C + r, C + r), a0 - 90, a1 - 90, fill=col, width=w)


faces = {}
# airspeed 0-320 km/h over -135..+135 (plugin); 0.15.69 (Nick 16:30): green 50-240, yellow 240-280, red line at 280
def kmh(v): return -135 + 270 * v / 320
im, d = base(); arc(d, kmh(50), kmh(240), GREEN); arc(d, kmh(240), kmh(280), AMBER); arc(d, kmh(280), kmh(320), RED)   # 0.15.70 (Nick 16:43): red band to the end of the scale
a = math.radians(kmh(280)); d.line((C + RR * 0.74 * math.sin(a), C - RR * 0.74 * math.cos(a), C + RR * 0.97 * math.sin(a), C - RR * 0.97 * math.cos(a)), fill=RED, width=6)
ticks(d, -135, 135, 8, 4); numbers(d, -135, 135, [0, 40, 80, 120, 160, 200, 240, 280, 320], 18); text(d, C, C + 66, 'KM/H', 16, GREY); faces['asi'] = im
# attitude (0.15.68, Nick 04:07): the globe (UL2_adi_ball, built in Unity) shows pitch and roll through the middle;
# the face keeps the bank scale round the rim and a fixed amber bank index at the top
im, d = base()
for ang in (-60, -45, -30, -20, -10, 10, 20, 30, 45, 60):
    a = math.radians(ang); big = ang % 30 == 0; r0 = RR * (0.78 if big else 0.86)
    d.line((C + r0 * math.sin(a), C - r0 * math.cos(a), C + RR * 0.97 * math.sin(a), C - RR * 0.97 * math.cos(a)), fill=WHITE, width=5 if big else 3)
d.polygon([(C, C - RR * 0.76), (C - 10, C - RR * 0.95), (C + 10, C - RR * 0.95)], fill=AMBER)
faces['adi'] = im
# altimeter: one turn per 1000 ft
im, d = base(); ticks(d, 0, 324, 9, 5); numbers(d, 0, 324, list(range(10)), 24); text(d, C, C - 40, 'ALT', 16, GREY); text(d, C, C + 44, 'x100 FT', 14, GREY)
faces['alt'] = im
# engine rpm (0.15.26, Nick 04:56, replaces the turn coordinator): 0-7000 rpm over -135..+135, green 1400-5500,
# yellow 5500-5800 (5 min take-off power), red line 5800
def erpm(v): return -135 + 270 * v / 7000
im, d = base(); arc(d, erpm(1400), erpm(5500), GREEN); arc(d, erpm(5500), erpm(5800), AMBER); arc(d, erpm(5800), erpm(7000), RED)   # 0.15.70 (Nick 16:59): red band to the end
a = math.radians(erpm(5800)); d.line((C + RR * 0.74 * math.sin(a), C - RR * 0.74 * math.cos(a), C + RR * 0.97 * math.sin(a), C - RR * 0.97 * math.cos(a)), fill=RED, width=6)
ticks(d, -135, 135, 7, 2); numbers(d, -135, 135, list(range(8)), 22); text(d, C, C + 46, 'RPM', 16, GREY); text(d, C, C + 64, 'x1000', 13, GREY)
faces['eng'] = im
# heading: the card turns under a fixed red lubber pointer (0.15.26, Nick 04:56)
# 0.15.69 (Nick 16:30): drawn at 4x (1024 px) so the numbers stay crisp, numbers a little smaller to sit inside the red window
K = 4; SK, CK, RK = S * K, C * K, RR * K
im = Image.new('RGB', (SK, SK), (40, 42, 45)); d = ImageDraw.Draw(im); d.ellipse((CK - RK, CK - RK, CK + RK, CK + RK), fill=DIAL)
for k in range(72):   # 5 deg ticks pushed to the rim, long every 30
    a = math.radians(5 * k); big = k % 6 == 0; mid = k % 2 == 0; r0 = RK * (0.84 if big else 0.89 if mid else 0.92)
    d.line((CK + r0 * math.sin(a), CK - r0 * math.cos(a), CK + RK * 0.98 * math.sin(a), CK - RK * 0.98 * math.cos(a)), fill=WHITE, width=(4 if big else 2) * K)
for k, s_ in enumerate(['N', '3', '6', 'E', '12', '15', 'S', '21', '24', 'W', '30', '33']):
    # every number upright toward the rim (top outward, like a real card), just inside the ticks
    sz = (26 if len(s_) == 1 else 22) * K; f = font(sz); tb = d.textbbox((0, 0), s_, font=f)
    tile = Image.new('RGBA', (tb[2] - tb[0] + 8 * K, tb[3] - tb[1] + 8 * K), (0, 0, 0, 0)); td = ImageDraw.Draw(tile)
    td.text((4 * K - tb[0], 4 * K - tb[1]), s_, fill=AMBER if s_ == 'N' else WHITE, font=f)
    tile = tile.rotate(-30 * k, expand=True, resample=Image.BICUBIC)
    a = math.radians(30 * k); r = RK * 0.69
    im.paste(tile, (int(CK + r * math.sin(a) - tile.width / 2), int(CK - r * math.cos(a) - tile.height / 2)), tile)
faces['hdg'] = im
# vertical speed: +-10 m/s, zero at 9 o'clock, -90 +- 135 (plugin)
im, d = base(); ticks(d, -225, 45, 4, 5)
for k, v in enumerate(['10', '5', '0', '5', '10']):
    a = math.radians(-225 + 270 * k / 4); text(d, C + RR * 0.58 * math.sin(a), C - RR * 0.58 * math.cos(a), v, 22)
text(d, C + 18, C - 30, 'UP', 14, GREY); text(d, C + 18, C + 30, 'DN', 14, GREY); text(d, C + 30, C, 'M/S', 14, GREY)
faces['vsi'] = im
# attitude globe texture (0.15.68): equirectangular, u = longitude (-180..180, display right positive), v = latitude
BW, BH = 1024, 512
ball = Image.new('RGB', (BW, BH), SKY); bd = ImageDraw.Draw(ball)
def bxy(lon, lat): return (BW / 2 + lon * BW / 360, BH / 2 - lat * BH / 180)
bd.rectangle((0, BH / 2, BW, BH), fill=GND)
for lat in range(-80, 90, 5):
    if lat == 0: continue
    big = lat % 10 == 0; w = 14 if big else 6
    for c0 in (0, 120, -120):   # the ladder repeats three times round, the front one is what shows
        x0, y = bxy(c0 - w, lat); x1, _ = bxy(c0 + w, lat); bd.line((x0, y, x1, y), fill=WHITE, width=3 if big else 2)
        if big and abs(lat) <= 60:
            for sg in (-1, 1):
                x, _ = bxy(c0 + sg * (w + 8), lat); ft = font(20); t = str(abs(lat)); tb = bd.textbbox((0, 0), t, font=ft)
                bd.text((x - (tb[2] - tb[0]) / 2, y - (tb[3] - tb[1]) / 2 - tb[1]), t, fill=WHITE, font=ft)
bd.rectangle((0, BH / 2 - 2, BW, BH / 2 + 2), fill=WHITE)
for c0 in (0, 120, -120):   # sky pointer at the top of the front, for bank at a glance
    x, y = bxy(c0, 72); bd.polygon([(x, y - 10), (x - 9, y + 8), (x + 9, y + 8)], fill=WHITE)
ball.save(os.path.join(out, 'UL2_adi_ball_img.png'))
for k, im in faces.items():
    im.transpose(Image.FLIP_LEFT_RIGHT).save(os.path.join(out, f'UL2_gf_{k}_img.png'))
print('wrote', len(faces), 'faces to', out)
