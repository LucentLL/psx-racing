"""understory_atlas.py - Charlotte's roadside understory atlases from the owner's Codex pack
(Environment_Overhaul2_Uptown/Roadside_LowVegetation).

    py tools/city/understory/understory_atlas.py <handoff dir>

One sheet a season look, 4 x 2 square cells (CityUnderstory.Cells keeps the same table):

    row 0 (top):    0 fescue clump   1 switchgrass clump   2 inkberry   3 mountain laurel
    row 1:          4 azalea         5-6 the verge strip (2:1, one repeat)    7 empty

CLASSIC cells are 128 px (512 x 256, Art/City/Understory), the ENHANCED twins 256 px
(1024 x 512, EnhancedSrc/TexEnhanced/City/Understory). Looks: Green (the pack's default,
Mixed_Green: spring and summer), Fall (Drying_Verge) and Winter (Dormant_Brown: winter and
snow). The shrubs are the same in all three. Resampled with premultiplied alpha, then each
cell's colour bled into its transparent texels (no dark fringe at the 0.5 cutoff).
"""
import os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
CLASSIC = os.path.join(REPO, "Assets", "PSXRacing", "Art", "City", "Understory")
ENH = os.path.join(REPO, "Assets", "PSXRacing", "EnhancedSrc", "TexEnhanced", "City", "Understory")
LOOKS = {"Green": None, "Fall": "Drying_Verge", "Winter": "Dormant_Brown"}
GRASS = ["TallGrass_Fescue", "TallGrass_Switchgrass", "TallGrass_VergeStrip"]
CELLS = [("TallGrass_Fescue", 0, 0, 1), ("TallGrass_Switchgrass", 1, 0, 1), ("Shrub_Inkberry", 2, 0, 1),
         ("Bush_MountainLaurel", 3, 0, 1), ("Shrub_Azalea", 0, 1, 1), ("TallGrass_VergeStrip", 1, 1, 2)]


def src(handoff, name, look):
    v = LOOKS[look]
    if v is not None and name in GRASS:
        return os.path.join(handoff, "Variants", v, "Enhanced_2x", name + "_overhaul1.png")
    return os.path.join(handoff, "Enhanced_2x", name + "_overhaul1.png")


def resize_premul(im, size):
    a = np.asarray(im.convert("RGBA")).astype(np.float32) / 255
    a[..., :3] *= a[..., 3:4]
    r = np.stack([np.asarray(Image.fromarray(a[..., k]).resize(size, Image.LANCZOS)) for k in range(4)], -1)
    r = np.clip(r, 0, 1)
    rgb = np.where(r[..., 3:4] > 1e-4, r[..., :3] / np.maximum(r[..., 3:4], 1e-4), 0)
    return np.concatenate([np.clip(rgb, 0, 1), r[..., 3:4]], -1)


def bleed(cell, steps=8):
    """Spread each opaque texel's colour into the transparent ones beside it."""
    rgb, a = cell[..., :3].copy(), cell[..., 3]
    known = a >= 0.5
    for _ in range(steps):
        acc = np.zeros_like(rgb); cnt = np.zeros(a.shape)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            k = np.roll(known, (dy, dx), (0, 1)); c = np.roll(rgb, (dy, dx), (0, 1))
            acc += c * k[..., None]; cnt += k
        grow = (~known) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]; known = known | grow
    return np.concatenate([rgb, a[..., None]], -1)


def build(handoff, look, cell):
    W, H = 4 * cell, 2 * cell
    sheet = np.zeros((H, W, 4), np.float32)
    for name, cx, cy, span in CELLS:
        im = Image.open(src(handoff, name, look))
        c = bleed(resize_premul(im, (cell * span, cell)))
        sheet[cy * cell:(cy + 1) * cell, cx * cell:(cx + span) * cell] = c
    return Image.fromarray((sheet * 255 + 0.5).astype(np.uint8), "RGBA")


def main():
    handoff = sys.argv[1]
    os.makedirs(CLASSIC, exist_ok=True); os.makedirs(ENH, exist_ok=True)
    for look in LOOKS:
        build(handoff, look, 128).save(os.path.join(CLASSIC, "Understory_%s.png" % look), optimize=True)
        build(handoff, look, 256).save(os.path.join(ENH, "Understory_%s_ov1.png" % look), optimize=True)
        print("wrote", look)


if __name__ == "__main__":
    main()
