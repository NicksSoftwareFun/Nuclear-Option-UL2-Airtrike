"""Six-pack dial faces for the pilot panel, drawn in the PB-22 Trident style (near-black dials, white ticks and
numbers, grey labels). Nick 2026-11-12 22:04: standard six-pack copied from the Trident.
Writes UL2_gf_<id>_img.png (256x256) for asi, adi, alt, turn, hdg, vsi; mirrored left-right like the copilot
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
# airspeed 0-120 kt over -135..+135 (plugin)
im, d = base(); arc(d, -135 + 270 * 25 / 120, -135 + 270 * 85 / 120, GREEN); arc(d, -135 + 270 * 85 / 120, -135 + 270 * 110 / 120, AMBER)
ticks(d, -135, 135, 6); numbers(d, -135, 135, [0, 20, 40, 60, 80, 100, 120], 22); text(d, C, C + 52, 'KNOTS', 16, GREY); faces['asi'] = im
# attitude: sky over ground with a bank scale; the plugin rolls the horizon bar over it
im, d = base(); d.pieslice((C - RR, C - RR, C + RR, C + RR), 180, 360, fill=SKY); d.pieslice((C - RR, C - RR, C + RR, C + RR), 0, 180, fill=GND)
d.line((C - RR, C, C + RR, C), fill=WHITE, width=3)
for ang in (-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60):
    a = math.radians(ang); big = ang % 30 == 0; r0 = RR * (0.74 if big else 0.82)
    d.line((C + r0 * math.sin(a), C - r0 * math.cos(a), C + RR * 0.95 * math.sin(a), C - RR * 0.95 * math.cos(a)), fill=WHITE, width=4 if big else 2)
for p in (-20, -10, 10, 20): w = 26 if abs(p) == 20 else 16; y = C - p * 2.4; d.line((C - w, y, C + w, y), fill=WHITE, width=2)
d.polygon([(C, C - RR * 0.72), (C - 9, C - RR * 0.60), (C + 9, C - RR * 0.60)], fill=AMBER)
d.line((C - 46, C, C - 16, C), fill=AMBER, width=5); d.line((C + 16, C, C + 46, C), fill=AMBER, width=5); d.ellipse((C - 5, C - 5, C + 5, C + 5), fill=AMBER)
faces['adi'] = im
# altimeter: one turn per 1000 ft
im, d = base(); ticks(d, 0, 324, 9, 5); numbers(d, 0, 324, list(range(10)), 24); text(d, C, C - 40, 'ALT', 16, GREY); text(d, C, C + 44, 'x100 FT', 14, GREY)
faces['alt'] = im
# turn coordinator: standard-rate marks at +-20 deg, slip ball
im, d = base()
for ang in (-20, 0, 20):
    a = math.radians(ang - 90 if ang else -90)
for side in (-1, 1):
    for ang in (0, 20):
        a = math.radians(90 + side * ang) if side > 0 else math.radians(-90 - ang)
        r0 = RR * 0.70; d.line((C + r0 * math.sin(a), C - r0 * math.cos(a), C + RR * 0.95 * math.sin(a), C - RR * 0.95 * math.cos(a)), fill=WHITE, width=5)
text(d, C - 70, C + 34, 'L', 20); text(d, C + 70, C + 34, 'R', 20); text(d, C, C - 60, 'TURN COORD', 13, GREY); text(d, C, C - 42, '2 MIN', 12, GREY)
d.rounded_rectangle((C - 60, C + 60, C + 60, C + 84), 12, outline=WHITE, width=2); d.ellipse((C - 10, C + 62, C + 10, C + 82), fill=(10, 10, 10), outline=WHITE)
faces['turn'] = im
# heading: fixed card, the needle points at the heading
im, d = base(); ticks(d, 0, 360, 12, 3)
for k, s in enumerate(['N', '3', '6', 'E', '12', '15', 'S', '21', '24', 'W', '30', '33']):
    a = math.radians(30 * k); text(d, C + RR * 0.58 * math.sin(a), C - RR * 0.58 * math.cos(a), s, 22 if len(s) == 1 else 18, AMBER if s == 'N' else WHITE)
faces['hdg'] = im
# vertical speed: +-10 m/s, zero at 9 o'clock, -90 +- 135 (plugin)
im, d = base(); ticks(d, -225, 45, 4, 5)
for k, v in enumerate(['10', '5', '0', '5', '10']):
    a = math.radians(-225 + 270 * k / 4); text(d, C + RR * 0.58 * math.sin(a), C - RR * 0.58 * math.cos(a), v, 22)
text(d, C + 18, C - 30, 'UP', 14, GREY); text(d, C + 18, C + 30, 'DN', 14, GREY); text(d, C + 30, C, 'M/S', 14, GREY)
faces['vsi'] = im
for k, im in faces.items():
    im.transpose(Image.FLIP_LEFT_RIGHT).save(os.path.join(out, f'UL2_gf_{k}_img.png'))
print('wrote', len(faces), 'faces to', out)
