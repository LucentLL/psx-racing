# THE UPTOWN PAVERS (charlotte-uptown-street-dress, 2026-11-09): the brick
# sidewalk of Tryon and Trade Streets (CityMeshes.Slot.WalkPaver,
# CityMeshes.StreetDress.cs). The owner's PSX Textures II basket-weave paver
# (256/Color Maps/cobblestone_floor_pt_1.png - a pack texture, never a
# photograph of the place), graded from its grey-brown to the red-brown of a
# clay paver and brought to the brightness the city's walks have under the
# game's light (Art/City/Pack/concrete_pt_4_walk.png). It repeats on BOTH
# axes (a walk span maps U along the street and V across it; a corner's fan
# maps world x,z), one repeat = CityMeshes.PaverTexM metres.
#
#   py -3 tools/city/paver_walk.py                 writes Art/City/Pack/brick_paver_walk.png
#   py -3 tools/city/paver_walk.py --check <dir>   and the tile checks (art AGENTS.md): numbers on
#                                                  stdout, <dir>/paver_3x3.png, paver_halfoffset.png,
#                                                  paver_8x8.png made FROM THE WRITTEN PNG
import os, sys
from PIL import Image, ImageStat

PACK = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Textures II v1.6\PSX Textures II\256\Color Maps"
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
PACKDIR = os.path.join(ROOT, "Assets", "PSXRacing", "Art", "City", "Pack")
OUT = os.path.join(PACKDIR, "brick_paver_walk.png")
WALK = os.path.join(PACKDIR, "concrete_pt_4_walk.png")
# a clay paver's hue (red-brown), at two thirds of the walk's luminance (brick is darker than a broomed slab)
# (2026-11-10, the ground floors round: the first grade (0.58, 0.31, 0.24) at 0.66 of the walk read brighter and redder
# than the streets own pavers - a weathered red-brown, greyed by its mortar and dust)
BRICK = (0.50, 0.365, 0.315)
KEEP = 0.42       # how much of the pack texel's own colour variation survives the grade


def lum(c):
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def build():
    src = Image.open(os.path.join(PACK, "cobblestone_floor_pt_1.png")).convert("RGB")
    assert src.size == (256, 256), src.size
    target = lum(ImageStat.Stat(Image.open(WALK).convert("RGB")).mean) * 0.60
    mean = lum(ImageStat.Stat(src).mean)
    bl = lum([c * 255 for c in BRICK])
    px = src.load()
    out = Image.new("RGB", src.size)
    op = out.load()
    for y in range(256):
        for x in range(256):
            r, g, b = px[x, y]
            l = lum((r, g, b)) / mean                 # the texel's relief (joints dark, faces light)
            # per-channel: the brick hue at this relief, plus a share of the texel's own tint
            base = [BRICK[i] * 255 * target / bl * l for i in range(3)]
            own = [c * target / mean for c in (r, g, b)]
            op[x, y] = tuple(max(0, min(255, int(round(base[i] * (1 - KEEP) + (own[i] - lum(own) + lum(base)) * KEEP)))) for i in range(3))
    out.save(OUT, optimize=True)
    return out


def seam_scores(im):
    """Mean absolute step across the wrap against the mean step between interior neighbours, per axis."""
    g = im.convert("L"); w, h = g.size; p = g.load()
    def col_step(x0, x1): return sum(abs(p[x0, y] - p[x1, y]) for y in range(h)) / h
    def row_step(y0, y1): return sum(abs(p[x, y0] - p[x, y1]) for x in range(w)) / w
    u_wrap = col_step(w - 1, 0); u_in = sum(col_step(x, x + 1) for x in range(w - 1)) / (w - 1)
    v_wrap = row_step(h - 1, 0); v_in = sum(row_step(y, y + 1) for y in range(h - 1)) / (h - 1)
    return u_wrap, u_in, v_wrap, v_in


def check(outdir):
    os.makedirs(outdir, exist_ok=True)
    im = Image.open(OUT).convert("RGB")       # the finished export, as shipped
    w, h = im.size
    u_wrap, u_in, v_wrap, v_in = seam_scores(im)
    print(f"export {w}x{h} {os.path.getsize(OUT)} bytes mean {[round(c) for c in ImageStat.Stat(im).mean]}")
    print(f"U wrap step {u_wrap:.2f} vs interior {u_in:.2f}; V wrap step {v_wrap:.2f} vs interior {v_in:.2f} (a wrap no rougher than the interior)")
    t3 = Image.new("RGB", (w * 3, h * 3))
    for i in range(3):
        for j in range(3): t3.paste(im, (i * w, j * h))
    t3.save(os.path.join(outdir, "paver_3x3.png"))
    half = Image.new("RGB", (w, h))
    half.paste(im, (-w // 2, -h // 2)); half.paste(im, (w // 2, -h // 2)); half.paste(im, (-w // 2, h // 2)); half.paste(im, (w // 2, h // 2))
    half.resize((w * 2, h * 2), Image.NEAREST).save(os.path.join(outdir, "paver_halfoffset.png"))
    t8 = Image.new("RGB", (w * 8, h * 8))
    for i in range(8):
        for j in range(8): t8.paste(im, (i * w, j * h))
    t8.resize((768, 768), Image.LANCZOS).save(os.path.join(outdir, "paver_8x8.png"))


if __name__ == "__main__":
    build()
    if "--check" in sys.argv:
        check(sys.argv[sys.argv.index("--check") + 1])
    print("wrote", OUT)
