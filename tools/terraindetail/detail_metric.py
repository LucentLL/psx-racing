# The GT4 study's fine-detail number on before/after pairs (tools/terraindetail-shots.ps1):
# the frame scaled to 960x540, luma (Rec. 601), the mean absolute HORIZONTAL
# luma gradient over
#   centre  rows 60-440, cols 180-780 (the study's crop: GT4 8.9-11.7, ours 5.8)
#   ground  the ground beside the car in that frame (ROI below, 960x540 px)
#
#   py tools\terraindetail\detail_metric.py <dir> [<name> ...]
#      pairs <dir>\before_<name>.png with <dir>\after_<name>.png
import os
import sys
import numpy as np
from PIL import Image

CENTRE = (180, 60, 780, 440)          # x0, y0, x1, y1 at 960x540
# The ground near the car per frame (x0, y0, x1, y1 at 960x540): grass, verge,
# bank or snow only - no road, no car, no sky, no trees.
GROUND = {
    "summer_start": (760, 400, 960, 540),
    "summer_wp1358": (700, 330, 940, 470),
    "summer_wp543": (720, 380, 960, 520),
    "snow_start": (700, 300, 900, 420),
    "snow_wp815": (720, 330, 960, 470),
}


def luma(path):
    im = Image.open(path).convert("RGB").resize((960, 540), Image.BILINEAR)
    a = np.asarray(im, dtype=np.float64)
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


def grad(L, box):
    x0, y0, x1, y1 = box
    c = L[y0:y1, x0:x1]
    return float(np.mean(np.abs(np.diff(c, axis=1))))


def main():
    d = sys.argv[1]
    names = sys.argv[2:] or sorted(f[len("before_"):-4] for f in os.listdir(d)
                                   if f.startswith("before_") and f.endswith(".png"))
    print(f"{'frame':16s} {'centre b/a':>16s} {'x':>6s}   {'ground b/a':>16s} {'x':>6s}")
    for n in names:
        b, a = os.path.join(d, "before_" + n + ".png"), os.path.join(d, "after_" + n + ".png")
        if not (os.path.exists(b) and os.path.exists(a)):
            continue
        Lb, La = luma(b), luma(a)
        cb, ca = grad(Lb, CENTRE), grad(La, CENTRE)
        line = f"{n:16s} {cb:7.2f} {ca:7.2f}  {ca / cb:5.2f}x"
        if n in GROUND:
            gb, ga = grad(Lb, GROUND[n]), grad(La, GROUND[n])
            line += f"   {gb:7.2f} {ga:7.2f}  {ga / gb:5.2f}x"
        print(line)


if __name__ == "__main__":
    main()
