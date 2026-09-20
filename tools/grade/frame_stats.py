# The tone signature of a reference frame, so a grade can be tuned to NUMBERS.
#
#   py tools/grade/frame_stats.py frame1.jpg [frame2.jpg ...]
#
# Letterbox and pillarbox are cropped off automatically (a phone screenshot of a
# video is mostly black bars). Per frame: luma percentiles (where the floor and
# the ceiling sit), mean saturation, and the mean colour of the darkest 5%, the
# mids and the top 3% - which is where a grade's tint shows.
#
# What the owner's four frames (2026-09-19) measured, and PSX/Blit's grade was
# tuned to: floor 0.09-0.18, ceiling 0.91-0.96 (r .895 g .927 b .914: cream with
# a breath of green), saturation 0.12-0.16 with the warm hues left loud.
import sys
import numpy as np
from PIL import Image

def frame(path):
    im = Image.open(path).convert("RGB")
    a = np.asarray(im).astype(np.float32) / 255.0
    Y = a.mean(-1)
    rows = np.where((Y > 0.06).mean(1) > 0.5)[0]
    cols = np.where((Y > 0.06).mean(0) > 0.5)[0]
    if len(rows) < 8 or len(cols) < 8: return a, im.size
    y0, y1, x0, x1 = rows[0] + 6, rows[-1] - 6, cols[0] + 6, cols[-1] - 6
    return a[y0:y1, x0:x1], im.size

def stats(name, a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    Y = 0.299 * r + 0.587 * g + 0.114 * b
    mx, mn = a.max(-1), a.min(-1)
    sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)
    p = np.percentile(Y, [0.1, 1, 5, 25, 50, 75, 95, 99, 99.9])
    print(f"{name}")
    print("    Y pct .1/1/5/25/50/75/95/99/99.9 = " + " ".join(f"{v:.3f}" for v in p) + f"   sat mean {sat.mean():.3f}")
    for nm, lo, hi in (("darkest 5%", 0, 5), ("45-55%", 45, 55), ("top 3%", 97, 100)):
        m = (Y >= np.percentile(Y, lo)) & (Y <= np.percentile(Y, hi))
        c = a[m].mean(0)
        print(f"    {nm:10s} rgb = {c[0]:.3f} {c[1]:.3f} {c[2]:.3f}   r-b {c[0] - c[2]:+.3f}  g-mid {c[1] - (c[0] + c[2]) / 2:+.3f}")

if __name__ == "__main__":
    for f in sys.argv[1:]:
        a, size = frame(f)
        stats(f"{f}  {size}", a)
