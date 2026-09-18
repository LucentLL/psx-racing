# The texture half of export_sodas.py: cut each bottle's window out of the
# pack's food atlas and lay the four side by side on one 256 x 256 sheet.
#
#   py tools/sodas/build_atlas.py
#
# 256 because that is the renderer's ceiling for anything that is not the sky,
# and it costs these bottles nothing: each one's picture in the pack's atlas is
# about 65 x 230 pixels, so a 60 x 252 column is its native size.
#
# The gutter is filled with the window's OWN edge pixels rather than left
# black. The sheet is point filtered with no mips, so nothing should ever
# sample it - and a bottle with a black seam up its side is what happens the
# day a UV lands half a texel out.
import json, os, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(os.path.dirname(HERE))
TEX_DIR = os.environ.get("SODA_TEXTURES",
    r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\All\All\Textures")
OUT = os.path.join(PROJ, "Assets", "PSXRacing", "Art", "LifeSim", "Groceries", "Sodas2L.png")

spec = json.load(open(os.path.join(HERE, "soda_uv.json")))
sheet, col, gut = spec["sheet"], spec["col"], spec["gutter"]
atlas = Image.open(os.path.join(TEX_DIR, spec["atlas"])).convert("RGB")
W, H = atlas.size

out = Image.new("RGB", (sheet, sheet), (0, 0, 0))
for i in range(len(spec["bottles"])):
    w = spec["bottles"]["soda_2l_%d" % i]
    # v runs UP and image rows run DOWN.
    box = (w["u0"] * W, (1.0 - w["v1"]) * H, w["u1"] * W, (1.0 - w["v0"]) * H)
    inner = atlas.crop(tuple(int(round(b)) for b in box)).resize((col - 2 * gut, sheet - 2 * gut), Image.LANCZOS)
    # Edge-extended to fill the whole column, gutter included.
    full = inner.resize((col, sheet), Image.NEAREST)
    full.paste(inner, (gut, gut))
    # top/bottom/left/right strips: replicate the inner image's border rows.
    for x in range(col):
        sx = min(max(x - gut, 0), inner.width - 1)
        for y in range(gut):
            full.putpixel((x, y), inner.getpixel((sx, 0)))
            full.putpixel((x, sheet - 1 - y), inner.getpixel((sx, inner.height - 1)))
    for y in range(sheet):
        sy = min(max(y - gut, 0), inner.height - 1)
        for x in range(gut):
            full.putpixel((x, y), inner.getpixel((0, sy)))
            full.putpixel((col - 1 - x, y), inner.getpixel((inner.width - 1, sy)))
    out.paste(full, (i * col, 0))

out.save(OUT)
print("wrote", OUT, out.size)
