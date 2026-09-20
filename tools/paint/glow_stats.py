# How much of each car is at the top of the scale?
#
#   py tools/paint/glow_stats.py <Screenshots dir> [out_sheet.png]
#
# PaintGlowCheck writes every frame twice, with the painted renderers and without
# them; the difference is the paint's footprint. Over that mask this prints, per
# livery and sun position: the share of the car at or over 0.97 on the display
# (clipped - "glow"), its 99th percentile and mean luma, and the mean saturation
# of what is NOT glass (so a wash of white over a coloured car shows as a fall).
import sys, os, glob
import numpy as np
from PIL import Image

def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32) / 255.0

def main():
    d = sys.argv[1]
    rows = []
    tiles = []
    for nocar in sorted(glob.glob(os.path.join(d, "psx_glow_*_nocar.png"))):
        car = nocar.replace("_nocar.png", ".png")
        if not os.path.exists(car): continue
        a, b = load(car), load(nocar)
        mask = np.abs(a - b).max(-1) > 0.06
        n = int(mask.sum())
        if n < 200:
            rows.append((os.path.basename(car), "NO CAR FOUND"))
            continue
        px = a[mask]
        Y = px @ np.array([0.299, 0.587, 0.114], np.float32)
        top = px.max(-1)
        mx, mn = px.max(-1), px.min(-1)
        sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)
        paint = Y > 0.22            # not glass, not tyre
        name = os.path.basename(car)[len("psx_glow_"):-4]
        rows.append((name,
                     f"clipped {100.0 * (top >= 0.97).mean():5.1f}%   over .90 {100.0 * (Y >= 0.90).mean():5.1f}%   "
                     f"p99 {np.percentile(Y, 99):.3f}   p90 {np.percentile(Y, 90):.3f}   mean {Y.mean():.3f}   "
                     f"paint sat {sat[paint].mean() if paint.any() else 0:.3f}   ({n} px)"))
        ys, xs = np.where(mask)
        y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
        pad = 12
        crop = Image.fromarray((a[max(0, y0 - pad):y1 + pad, max(0, x0 - pad):x1 + pad] * 255).astype(np.uint8))
        tiles.append((name, crop))
    w = max(len(r[0]) for r in rows) if rows else 10
    for name, line in rows:
        print(f"  {name:<{w}}  {line}")

    # THE LAMPS. "The taillights look like they're hidden under a layer of smoke":
    # a film over a red lens shows as lost SATURATION. The camera sits in the same
    # place relative to the car in all three frames, so the lamp pixels are found
    # once - where they are plainly red, with the sun behind the car - and read in
    # the two frames where the car's tail is in shade. Red liveries are skipped:
    # there the whole car is "red pixels".
    print("  tail lamps (found sun-behind, read in the shade):")
    for behind in sorted(glob.glob(os.path.join(d, "psx_glow_*_sunbehind.png"))):
        name = os.path.basename(behind)[len("psx_glow_"):-len("_sunbehind.png")]
        nocar = behind.replace(".png", "_nocar.png")
        if not os.path.exists(nocar): continue
        a, b = load(behind), load(nocar)
        car = np.abs(a - b).max(-1) > 0.06
        red = car & (a[..., 0] > 1.6 * a[..., 1]) & (a[..., 0] > 1.6 * a[..., 2]) & (a[..., 0] > 0.35)
        if red.sum() < 12 or red.sum() > 0.25 * car.sum(): continue
        parts = []
        for tag in ("sunbehind", "sunabeam", "sunahead"):
            f = os.path.join(d, f"psx_glow_{name}_{tag}.png")
            if not os.path.exists(f): continue
            px = load(f)[red]
            mx, mn = px.max(-1), px.min(-1)
            sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0).mean()
            parts.append(f"{tag[3:]}: sat {sat:.2f} red {px[:, 0].mean():.2f}")
        print(f"    {name:<28} {int(red.sum()):4d} px   " + "   ".join(parts))
    if len(sys.argv) > 2 and tiles:
        tw = max(t[1].width for t in tiles); th = max(t[1].height for t in tiles)
        cols = 3
        sheet = Image.new("RGB", (cols * tw, ((len(tiles) + cols - 1) // cols) * th), (40, 40, 40))
        for i, (name, crop) in enumerate(tiles):
            sheet.paste(crop, ((i % cols) * tw, (i // cols) * th))
        sheet.save(sys.argv[2])
        print("sheet:", sys.argv[2])

if __name__ == "__main__":
    main()
