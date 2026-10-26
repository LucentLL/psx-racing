# tools/night/nightcity_stats.py IMAGE "name:x0,y0,x1,y1;..." [label]  (2026-10-26, the night-city pass;
# Docs/shots/nightcity/nightcity_numbers.txt was measured with it on NightCityShots frames)
# Night-city stats v2. Per box: lit windows = pixels with Ycode >= 60 and r > b + 12 (warm, lit);
# walls = the rest. wall: median Ycode (p75) and median rgb; win: median Ycode, HSV sat,
# hue (circular mean), median rgb, share of box; contrast = win / wall median Ycode.
import sys, colorsys
import numpy as np
from PIL import Image
def ycode(rgb):
    s = rgb / 255.0
    lin = np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4)
    Y = lin @ np.array([0.2126, 0.7152, 0.0722])
    return np.where(Y <= 0.0031308, Y * 12.92, 1.055 * np.power(np.maximum(Y, 0), 1 / 2.4) - 0.055) * 255.0
def stats(px):
    yc = ycode(px)
    wm = (yc >= 60) & (px[:, 0] > px[:, 2] + 12)
    wl = px[~wm]; wy = yc[~wm]
    o = dict(wall=float(np.median(wy)), wall75=float(np.percentile(wy, 75)), wrgb=tuple(int(v) for v in np.median(wl, 0)))
    if wm.sum() < 3: o.update(win=0, sat=0, hue=0, share=0, contrast=0, rgb=None); return o
    w = px[wm]; mx = w.max(1); mn = w.min(1); sat = (mx - mn) / np.maximum(mx, 1)
    hs = np.array([colorsys.rgb_to_hsv(*(c / 255.0))[0] for c in w]) * 2 * np.pi
    hue = (np.degrees(np.arctan2(np.sin(hs).mean(), np.cos(hs).mean())) + 360) % 360
    o.update(win=float(np.median(yc[wm])), sat=float(np.median(sat)), hue=float(hue), share=float(wm.mean()),
             contrast=float(np.median(yc[wm]) / max(o['wall'], 1)), rgb=tuple(int(v) for v in np.median(w, 0)))
    return o
img = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(np.float64)
lab = sys.argv[3] if len(sys.argv) > 3 else ""
for item in sys.argv[2].split(";"):
    name, box = item.split(":")
    x0, y0, x1, y1 = [int(v) for v in box.split(",")]
    s = stats(img[y0:y1, x0:x1].reshape(-1, 3))
    print(f"{lab:7s}{name:12s} wall {s['wall']:5.1f} p75 {s['wall75']:5.1f} rgb {s['wrgb']} | win {s['win']:5.1f} sat {s['sat']:.2f} hue {s['hue']:4.1f} rgb {s['rgb']} share {s['share']*100:4.1f}% | contrast {s['contrast']:4.1f}x")
