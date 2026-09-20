# What the atlas composer will do to each billboard: slide (and, where the crown
# would otherwise be cut off, shrink) it so the painted trunk stands on the
# cell's middle - then measure the trunk a car hits and the low foliage it
# ploughs through. The C# is TreeKit.CentreOnTrunk; this is its prototype and
# the contact sheet that checked it.
#
#   py tools/trees/centre_preview.py <Trees dir> <out_sheet.png>
import sys, os
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(__file__))
from trunk_finder import find_trunk, FALL, WINTER, SPRING, SUMMER, SNOW

CELL = 128
HEIGHTS = [11, 10.5, 12, 10, 11, 11, 9.5, 9, 10.5, 10, 10.5, 11, 12, 12.5, 11, 9]
CONIFER = [0] * 12 + [1, 1, 1, 0]

def centre(im):
    """im: RGBA 128x128 (row 0 = top). Returns (new image, scale, trunk_px_after)."""
    a = np.asarray(im)[..., 3] > 127
    t, w, base = find_trunk(a)
    cols = np.where(a.any(0))[0]
    lo, hi = cols.min(), cols.max()
    mid = (CELL - 1) / 2.0
    s = min(1.0, (mid + 0.5) / max(t - lo + 0.5, 1), (mid + 0.5) / max(hi - t + 0.5, 1))
    src = np.asarray(im)
    out = np.zeros_like(src)
    for y in range(CELL):
        sy = base - (base - y) / s
        if sy < 0: continue
        for x in range(CELL):
            sx = t + (x - mid) / s
            ix, iy = int(round(sx)), int(round(sy))
            if 0 <= ix < CELL and 0 <= iy < CELL: out[y, x] = src[iy, ix]
    return Image.fromarray(out, "RGBA"), s, w * s, base

def metrics(im, base, card_h, card_w):
    a = np.asarray(im)[..., 3] > 127
    mid = (CELL - 1) / 2.0
    ppm_v = CELL / card_h
    y1 = int(round(base - 0.3 * ppm_v)); y0 = int(round(base - 1.4 * ppm_v))
    band = a[max(0, y0):max(1, y1)]
    # trunk: the run of columns through the middle that is opaque in most of the band
    colfill = band.mean(0)
    half = 0
    while half < 40 and colfill[int(mid - half)] > 0.5 and colfill[int(mid + 1 + half)] > 0.5: half += 1
    trunk_half_px = max(half, 1)
    # brush: the widest symmetric span about the middle that is at least 45% leaf
    brush = 0
    for B in range(62, trunk_half_px + 4, -1):
        seg = band[:, int(mid - B):int(mid + B) + 1]
        if seg.mean() >= 0.45: brush = B; break
    return trunk_half_px / CELL, brush / CELL

def main():
    src, out = sys.argv[1], sys.argv[2]
    dresses = [("FALL", FALL), ("WINTER", WINTER), ("SPRING", SPRING), ("SUMMER", SUMMER), ("SNOW", SNOW)]
    scale = 2
    sheet = Image.new("RGB", (16 * CELL * scale, 5 * (CELL * scale + 14)), (90, 120, 160))
    dr = ImageDraw.Draw(sheet)
    for d, (dn, files) in enumerate(dresses):
        print(dn)
        for i, n in enumerate(files):
            im = Image.open(os.path.join(src, n + ".png")).convert("RGBA").resize((CELL, CELL), Image.NEAREST)
            new, s, tw, base = centre(im)
            h = HEIGHTS[i]; w = h * (0.62 if CONIFER[i] else 1.0)
            tf, bf = metrics(new, base, h, w)
            r = min(0.60, max(0.22, tf * w)); B = bf * w
            print(f"  {i:2d} {n}  scale {s:4.2f}  trunk r {r:4.2f} m  brush {B:4.1f} m")
            ox, oy = i * CELL * scale, d * (CELL * scale + 14)
            bg = Image.new("RGBA", (CELL, CELL), (90, 120, 160, 255)); bg.alpha_composite(new)
            sheet.paste(bg.convert("RGB").resize((CELL * scale, CELL * scale), Image.NEAREST), (ox, oy))
            cx = ox + CELL * scale // 2
            dr.line([(cx, oy + CELL * scale - 50), (cx, oy + CELL * scale)], fill=(255, 0, 0))
            if bf > 0:
                yb = oy + CELL * scale - int(0.8 * CELL / h * scale)
                dr.line([(cx - bf * CELL * scale, yb), (cx + bf * CELL * scale, yb)], fill=(255, 255, 0), width=2)
            dr.text((ox + 3, oy + CELL * scale), f"{n} s{s:.2f} r{r:.2f} B{B:.1f}", fill=(255, 255, 255))
    sheet.save(out)

if __name__ == "__main__":
    main()
