"""Turn the raw renders into game icons. Usage: python3 icon_lines.py <icondir>
Weapon icons: white outlines (object boundaries + silhouette) on black, 512x256, like weaponIcon_50cal.
Map icon: white top-down silhouette with the two-blade rotor turned 45 deg, 256x256 RGBA, like mapIcon_trainer."""
import sys, numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage
D = sys.argv[1]
def lines(name, thick=7, minblob=900):
    im = np.asarray(Image.open(f'{D}/raw_{name}.png').convert('RGBA')).astype(np.int32)
    a = im[..., 3] > 128; rgb = im[..., :3] * a[..., None]
    # label flat-colour regions; drop tiny regions (screws, slots) so the drawing stays clean at icon size
    key = (rgb[..., 0] // 8) * 1024 + (rgb[..., 1] // 8) * 32 + rgb[..., 2] // 8; key[~a] = -1
    key = ndimage.median_filter(key, size=9)          # knurling, ribs and screw heads merge into their part
    e = np.zeros(a.shape, bool)
    for dy, dx in ((0, 1), (1, 0)):
        k2 = np.roll(key, (-dy, -dx), (0, 1)); e |= (key != k2)
    lab, n = ndimage.label(~e)
    sizes = ndimage.sum(np.ones_like(lab), lab, range(n + 1)); small = sizes < minblob
    smallmask = small[lab] & a
    e &= ~ndimage.binary_dilation(smallmask, iterations=2) | ~a | np.roll(~a, 1, 0) | np.roll(~a, 1, 1)
    sil = a ^ ndimage.binary_erosion(a, iterations=1)
    e = (e & (a | np.roll(a, 1, 0) | np.roll(a, 1, 1))) | sil
    el, ne = ndimage.label(e, structure=np.ones((3, 3))); es = ndimage.sum(e, el, range(ne + 1)); e &= (es >= 120)[el]   # drop speckle
    e = ndimage.binary_dilation(e, iterations=thick // 2)
    ys, xs = np.nonzero(a); y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    crop = Image.fromarray((e[y0:y1 + 1, x0:x1 + 1] * 255).astype(np.uint8))
    W, H = 512, 256; s = min(0.86 * W / crop.width, 0.78 * H / crop.height)
    crop = crop.resize((max(1, int(crop.width * s)), max(1, int(crop.height * s))), Image.LANCZOS)
    out = Image.new('L', (W, H), 0); out.paste(crop, ((W - crop.width) // 2, (H - crop.height) // 2))
    out = out.point(lambda v: min(255, int(v * 1.6)))
    Image.merge('RGBA', (out, out, out, Image.new('L', (W, H), 255))).save(f'{D}/UL2_icon_s3_{name}.png')
for n in ('mg', 'gl', 'spot'): lines(n)
body = np.asarray(Image.open(f'{D}/raw_map.png').convert('RGBA'))[..., 3] > 100
rot = Image.open(f'{D}/raw_map_rotor.png').convert('RGBA').split()[3]
ra = np.asarray(rot) > 100; ys, xs = np.nonzero(ra); cy, cx = ys.mean(), xs.mean()
rot = rot.rotate(35, resample=Image.BILINEAR, center=(cx, cy))
ra = ndimage.binary_dilation(np.asarray(rot) > 100, iterations=3)       # blades a touch thicker so they read at map size
body = ndimage.binary_dilation(body, iterations=10)                    # thin tubes and booms still read at 32 px
m = body | (ra & ~ndimage.binary_dilation(body, iterations=22))          # blades pass UNDER the body with a small gap
img = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.5)).resize((256, 256), Image.LANCZOS)
rgba = Image.new('RGBA', (256, 256), (255, 255, 255, 0)); rgba.putalpha(img); rgba.save(f'{D}/UL2_icon_map.png')
# review sheet: stock-like dark blue background, plus map icon at in-game size
sheet = Image.new('RGBA', (1100, 600), (40, 60, 90, 255))
for i, n in enumerate(('mg', 'gl', 'spot')):
    sheet.alpha_composite(Image.open(f'{D}/UL2_icon_s3_{n}.png'), ((i % 2) * 540 + 20, (i // 2) * 290 + 20))
sheet.alpha_composite(rgba, (560 + 10, 290 + 15)); sheet.alpha_composite(rgba.resize((32, 32), Image.LANCZOS), (560 + 300, 290 + 120))
sheet.save(f'{D}/icons_sheet.png'); print('ok')
