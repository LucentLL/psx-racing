# tools/night/photo_families.py IMAGE ["name:x0,y0,x1,y1;..."] [--min-y 95]   (2026-10-29, the 1990s city)
# The owner's 1990s photograph of uptown at dusk (not in the repo) and our night frames, measured
# the same way: in each box the LIT pixels (Ycode >= --min-y and 45 over the box's 30th percentile)
# split into the two office families by green against red -
#   INCANDESCENT  g < r - 12  (yellow: tungsten)
#   FLUORESCENT   g >= r - 4  (white-green: tubes; mercury light on daylight film)
# (the band between is left out). Per family: share of the lit pixels, hue (circular mean), HSV sat,
# value, median rgb and Ycode. The photo, 2026-10-29: incandescent hue 36 sat 0.35 value 0.75
# Ycode 164; fluorescent hue 75 sat 0.19 value 0.77 Ycode 192; fluorescent 2:1 by lit pixel.
import sys, colorsys
import numpy as np
from PIL import Image

def ycode(rgb):
    s = rgb / 255.0
    lin = np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4)
    Y = lin @ np.array([0.2126, 0.7152, 0.0722])
    return np.where(Y <= 0.0031308, Y * 12.92, 1.055 * np.power(np.maximum(Y, 0), 1 / 2.4) - 0.055) * 255.0

def hsv(px):
    mx = px.max(1); mn = px.min(1)
    s = (mx - mn) / np.maximum(mx, 1)
    h = np.array([colorsys.rgb_to_hsv(*(c / 255.0))[0] for c in px]) * 360.0
    return h, s, mx / 255.0

def circ(h):
    r = np.radians(h)
    return (np.degrees(np.arctan2(np.sin(r).mean(), np.cos(r).mean())) + 360) % 360

def family(tag, q):
    if len(q) < 3: return f"{tag} n=0"
    h, s, v = hsv(q)
    return (f"{tag} n={len(q)} hue={circ(h):.0f} sat={np.median(s):.2f} val={np.median(v):.2f} "
            f"rgb={tuple(int(c) for c in np.median(q, 0))} Ycode={np.median(ycode(q)):.0f}")

def measure(img, box, min_y):
    x0, y0, x1, y1 = box
    px = img[y0:y1, x0:x1].reshape(-1, 3)
    yc = ycode(px)
    lit = px[yc >= max(min_y, np.percentile(yc, 30) + 45)]
    if len(lit) < 5: return "few lit", 0, 0
    d = lit[:, 1] - lit[:, 0]
    inc, flu = lit[d < -12], lit[d >= -4]
    n = max(len(inc) + len(flu), 1)
    return (f"lit {len(lit) / len(px):.3f} | tube share {len(flu) / n:.2f} | " + family("INCANDESCENT", inc) + " | " + family("FLUORESCENT", flu),
            len(inc), len(flu))

if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    min_y = 95.0
    if "--min-y" in sys.argv: min_y = float(sys.argv[sys.argv.index("--min-y") + 1]); args = [a for a in args if a != str(sys.argv[sys.argv.index("--min-y") + 1])]
    img = np.asarray(Image.open(args[0]).convert("RGB")).astype(np.float64)
    H, W, _ = img.shape
    boxes = [("all", (0, 0, W, H))]
    if len(args) > 1:
        boxes = []
        for item in args[1].split(";"):
            name, b = item.split(":")
            boxes.append((name, tuple(int(v) for v in b.split(","))))
    ti = tf = 0
    for name, b in boxes:
        line, ni, nf = measure(img, b, min_y)
        ti += ni; tf += nf
        print(f"{name:10s} {line}")
    if len(boxes) > 1: print(f"ALL boxes: tube share {tf / max(ti + tf, 1):.2f} ({tf} fluorescent / {ti} incandescent px)")
