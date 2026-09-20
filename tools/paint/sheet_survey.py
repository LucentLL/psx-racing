# What is on a livery sheet, by colour? The car paint shader has no submeshes to
# go by - one 128 px sheet paints the body, the glass, the tyres, the lamps and
# the plate - so anything it treats differently it has to FIND in the texels.
# This surveys every livery: how much of each sheet is dark-and-grey (glass,
# tyres, trim), dark-and-RED/AMBER (lamp lenses), and what the body colour is,
# so a lens mask can be checked against the liveries it would mistake for one
# (a maroon body is dark, red and saturated too).
#
#   py tools/paint/sheet_survey.py <Art/Car/Models dir> [out_sheet.png]
import sys, os, glob
import numpy as np
from PIL import Image

def srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

def masks(a):
    """a: HxWx3 sRGB 0..1. Returns (glass, lens) float masks as the shader computes them (on LINEAR texels)."""
    lin = srgb_to_lin(a)
    lum = lin @ np.array([0.30, 0.59, 0.11])
    mx = lin.max(-1); mn = lin.min(-1)
    sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)
    dark = np.clip((0.22 - lum) / 0.22, 0, 1)
    grey = 1 - np.clip(sat * 2.5 - 0.75, 0, 1)
    glass = dark * grey
    warm = np.clip((lin[..., 0] - np.maximum(lin[..., 1], lin[..., 2]) * 1.35) * 6.0, 0, 1)   # red / amber dominant
    lens = np.clip(sat * 2.5 - 1.25, 0, 1) * warm * np.clip((0.45 - lum) / 0.25, 0, 1)
    return glass, lens, lum

def main():
    root = sys.argv[1]
    rows = []
    tiles = []
    for p in sorted(glob.glob(os.path.join(root, "*", "textures", "*.png"))):
        im = Image.open(p).convert("RGBA")
        a = np.asarray(im).astype(np.float64) / 255.0
        op = a[..., 3] > 0.5
        rgb = a[..., :3]
        glass, lens, lum = masks(rgb)
        n = max(op.sum(), 1)
        # the body colour: the most common bright-ish quantised colour
        q = (rgb[op] * 15).round().astype(int)
        keys, counts = np.unique(q, axis=0, return_counts=True)
        body = keys[counts.argmax()] / 15.0
        name = os.path.basename(os.path.dirname(os.path.dirname(p))) + "/" + os.path.basename(p)[:-4]
        rows.append((name, glass[op].mean(), (lens[op] > 0.5).mean(), body, lens[op].sum() / n))
        tiles.append((name, im, lens, glass))
    rows.sort(key=lambda r: -r[2])
    print(f"{len(rows)} liveries. Share of the sheet the LENS mask takes (over 0.5), highest first:")
    for name, g, l, body, _ in rows[:24]:
        print(f"  {name:44s} lens {100*l:5.1f}%   glass {100*g:5.1f}%   body rgb {body[0]:.2f} {body[1]:.2f} {body[2]:.2f}")
    print("  ...")
    med = np.median([r[2] for r in rows])
    print(f"median lens share {100*med:.1f}%; liveries over 8%: {sum(1 for r in rows if r[2] > 0.08)}")
    if len(sys.argv) > 2:
        pick = [t for t in tiles if any(k in t[0] for k in ("audi_saloon/sky_blue", "charger_69/redline", "charger_69/midnight", "audi_saloon/glacier"))]
        pick += [t for t in tiles if t[0] in [r[0] for r in rows[:4]]]
        w = 128 * 3 + 8
        sheet = Image.new("RGB", (w, 132 * len(pick)), (30, 30, 30))
        for i, (name, im, lens, glass) in enumerate(pick):
            base = im.convert("RGB").resize((128, 128), Image.NEAREST)
            sheet.paste(base, (0, i * 132))
            lm = Image.fromarray((np.clip(lens, 0, 1) * 255).astype(np.uint8)).resize((128, 128), Image.NEAREST)
            gm = Image.fromarray((np.clip(glass, 0, 1) * 255).astype(np.uint8)).resize((128, 128), Image.NEAREST)
            sheet.paste(Image.merge("RGB", (lm, Image.new("L", (128, 128)), Image.new("L", (128, 128)))), (132, i * 132))
            sheet.paste(Image.merge("RGB", (Image.new("L", (128, 128)), gm, gm)), (264, i * 132))
        sheet.save(sys.argv[2])
        print("sheet (texture | lens mask red | glass mask cyan):", sys.argv[2], [p[0] for p in pick])

if __name__ == "__main__":
    main()
