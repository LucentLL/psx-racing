# For each staged HD colour sheet: a SEAM MASK (R = shut-line texel, G = paint
# texel near a seam, B = any paint texel) for the measuring renders, and the
# NORMAL MAP CONVENTION check: beside a seam, does the map's (x, y) point toward
# the seam centre in UV space with +V up (a groove in OpenGL +Y, what Unity's
# UnpackNormal and a MikkTSpace bitangent along +V expect)?
# Usage: py -I seams.py <stage> <outdir>
import sys, os, glob, json
import numpy as np
from PIL import Image, ImageFilter

stage, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)

def blur(a, r):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(r))).astype(np.float64) / 255

def dilate(m, r):
    o = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            o |= np.roll(np.roll(m, dy, 0), dx, 1)
    return o

rep = {}
for col_path in sorted(glob.glob(os.path.join(stage, '*__*_hd.png'))):
    key = os.path.basename(col_path)[:-len('_hd.png')]
    c = np.asarray(Image.open(col_path).convert('RGBA')).astype(np.float64) / 255
    rgb, a = c[..., :3], c[..., 3]
    L = rgb @ np.array([0.30, 0.59, 0.11])
    Lim = Image.fromarray((L * 255).astype(np.uint8))
    med = np.asarray(Lim.filter(ImageFilter.MedianFilter(15))).astype(np.float64) / 255
    medc = np.stack([np.asarray(Image.fromarray((rgb[..., k] * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(15))) for k in range(3)], -1).astype(np.float64) / 255
    hi, lo = medc.max(-1), medc.min(-1)
    paintish = ((hi - lo) / np.maximum(hi, 0.08) > 0.35) & (hi > 0.2)
    seam = paintish & (L < 0.80 * med) & (a > 0.3)
    paint = paintish & ~seam & (L > 0.85 * med)
    near = paint & dilate(seam, 6)
    m = np.zeros(L.shape + (3,), np.uint8)
    m[..., 0] = seam * 255
    m[..., 1] = near * 255
    m[..., 2] = paint * 255
    Image.fromarray(m, 'RGB').save(os.path.join(out, 'mask_' + key + '.png'))
    # convention: direction toward the seam = grad of the blurred seam mask (image x right, y down)
    g = blur(seam.astype(np.float64), 3)
    gy, gx = np.gradient(g)
    wall = (g > 0.05) & (g < 0.6) & ~seam & paintish
    n = np.asarray(Image.open(os.path.join(stage, key + '_hd_normal.png')).convert('RGB')).astype(np.float64) / 255 * 2 - 1
    nx, ny = n[..., 0], n[..., 1]
    gu, gv = gx, -gy     # UV space: u right, v UP
    mag = np.hypot(gu, gv) + 1e-9
    du, dv = gu / mag, gv / mag
    w = wall & (mag > 1e-3)
    tilt = np.hypot(nx, ny)
    toward_gl = float(((nx * du + ny * dv) * w).sum() / max(1, (tilt * w).sum()))
    toward_dx = float(((nx * du - ny * dv) * w).sum() / max(1, (tilt * w).sum()))
    xcorr = float(((nx * du) * w).sum() / max(1, w.sum())); ycorr = float(((ny * dv) * w).sum() / max(1, w.sum()))
    rep[key] = {'seam_texels': int(seam.sum()), 'wall_texels': int(w.sum()), 'mean_tilt_on_walls': float((tilt * w).sum() / max(1, w.sum())),
                'toward_seam_GL': toward_gl, 'toward_seam_DX': toward_dx, 'x_part': xcorr, 'y_part_GL': ycorr}
json.dump(rep, open(os.path.join(out, 'seams.json'), 'w'), indent=1)
for k, v in rep.items():
    print(f"{k:50s} seams {v['seam_texels']:7d} walls {v['wall_texels']:7d} tilt {v['mean_tilt_on_walls']:.3f}  toward(GL) {v['toward_seam_GL']:+.3f}  toward(DX) {v['toward_seam_DX']:+.3f}  x {v['x_part']:+.4f} y {v['y_part_GL']:+.4f}")
