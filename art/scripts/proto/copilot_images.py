"""Static pictures for the copilot station (Nick 2026-11-12 20:53: the copilot MFD and instruments show an image,
not live controls). Writes UL2_mfd2_img.png (map page, 600x500) and UL2_inst2_img.png (three gauges, 800x200).
Usage: python3 copilot_images.py <outdir>"""
import sys, os, math, random
from PIL import Image, ImageDraw, ImageFont, ImageFilter
out = sys.argv[1]; os.makedirs(out, exist_ok=True); random.seed(7)
def font(sz):
    for f in ('/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'):
        if os.path.exists(f): return ImageFont.truetype(f, sz)
    return ImageFont.load_default()
G, GD, AM, CY, WH = (90, 230, 120), (30, 90, 50), (240, 180, 60), (90, 210, 230), (225, 235, 230)
# ---- MFD: tactical map page
W, H = 600, 500
im = Image.new('RGB', (W, H), (6, 14, 10)); d = ImageDraw.Draw(im)
for k in range(9):                                   # terrain contours
    cx, cy, r = random.randint(60, 540), random.randint(60, 440), random.randint(60, 160)
    for j in range(4):
        rr = r - j * 28
        if rr > 10: d.ellipse((cx - rr, cy - rr * 0.7, cx + rr, cy + rr * 0.7), outline=(20, 60 + 12 * j, 35), width=2)
d.line([(40, 470), (160, 330), (300, 260), (430, 150), (560, 40)], fill=(70, 120, 200), width=5)   # river
route = [(300, 400), (250, 300), (330, 200), (420, 120)]
d.line(route, fill=CY, width=3)
for i, (x, y) in enumerate(route[1:], 1):
    d.ellipse((x - 9, y - 9, x + 9, y + 9), outline=CY, width=3); d.text((x + 12, y - 10), f'WP{i}', fill=CY, font=font(18))
for x, y in ((180, 160), (470, 260)):               # threat rings
    d.ellipse((x - 50, y - 50, x + 50, y + 50), outline=(220, 70, 60), width=3); d.text((x - 10, y - 11), 'S', fill=(220, 70, 60), font=font(22))
d.polygon([(300, 380), (288, 412), (300, 404), (312, 412)], fill=WH)              # own ship
d.arc((150, 230, 450, 530), 200, 340, fill=G, width=2)                            # compass arc
for a in range(200, 341, 10):
    r0, r1 = 150, 140 if a % 30 else 130; t = math.radians(a)
    d.line([(300 + r0 * math.cos(t), 380 + r0 * math.sin(t)), (300 + r1 * math.cos(t), 380 + r1 * math.sin(t))], fill=G, width=2)
d.rectangle((0, 0, W - 1, 34), fill=(10, 30, 18)); d.text((14, 6), 'MAP   HDG 045   GS 088', fill=G, font=font(22))
d.text((W - 120, 6), 'NAV', fill=AM, font=font(22))
for i, t in enumerate(('ZOOM+', 'ZOOM-', 'DCLT', 'TGT')): d.text((6, 70 + i * 100), t, fill=G, font=font(16))
for i, t in enumerate(('WPT', 'THRT', 'CAM', 'MENU')): d.text((W - 62, 70 + i * 100), t, fill=G, font=font(16))
d.text((14, H - 30), 'RNG 5 KM', fill=G, font=font(20)); d.text((W - 170, H - 30), 'FUEL 62%', fill=AM, font=font(20))
im = im.filter(ImageFilter.GaussianBlur(0.6)); im.save(os.path.join(out, 'UL2_mfd2_img.png'))
# ---- instrument strip: three small round gauges
W, H = 800, 200
im = Image.new('RGB', (W, H), (34, 36, 38)); d = ImageDraw.Draw(im)
for i, (lab, val, unit) in enumerate((('ROTOR', 0.62, 'RPM x100'), ('FUEL', 0.62, 'LBS x10'), ('EGT', 0.45, 'C x100'))):
    cx, cy, r = 133 + i * 267, 100, 82
    d.ellipse((cx - r - 6, cy - r - 6, cx + r + 6, cy + r + 6), fill=(70, 72, 74)); d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(12, 12, 14))
    d.arc((cx - r + 8, cy - r + 8, cx + r - 8, cy + r - 8), 135, 135 + 270 * 0.7, fill=(60, 170, 70), width=7)
    d.arc((cx - r + 8, cy - r + 8, cx + r - 8, cy + r - 8), 135 + 270 * 0.85, 405, fill=(200, 50, 40), width=7)
    for k in range(11):
        t = math.radians(135 + 27 * k); r0, r1 = r - 10, r - (28 if k % 5 == 0 else 20)
        d.line([(cx + r0 * math.cos(t), cy + r0 * math.sin(t)), (cx + r1 * math.cos(t), cy + r1 * math.sin(t))], fill=WH, width=3)
    t = math.radians(135 + 270 * val)
    d.line([(cx, cy), (cx + (r - 18) * math.cos(t), cy + (r - 18) * math.sin(t))], fill=(240, 240, 240), width=5)
    d.ellipse((cx - 8, cy - 8, cx + 8, cy + 8), fill=(60, 60, 60))
    d.text((cx - len(lab) * 7, cy + 22), lab, fill=WH, font=font(22)); d.text((cx - len(unit) * 4.5, cy + 48), unit, fill=(170, 170, 170), font=font(14))
im.save(os.path.join(out, 'UL2_inst2_img.png'))
# the quad UVs mirror U in Unity: store the pictures mirrored
for n in ("UL2_mfd2_img.png", "UL2_inst2_img.png"):
    p = os.path.join(out, n); Image.open(p).transpose(Image.FLIP_LEFT_RIGHT).save(p)
print("ok")
