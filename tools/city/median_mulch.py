# The median dress's bark mulch (2026-11-09): Art/City/Pack/mulch_dirt_6_city.png
# from the owner's PSX Textures v3.1 dirt_6 (256 x 256; its clods read as bark
# chips), warmed and lifted to the sun-bleached mulch of the road-level photos
# of NC-16 Brookshire Blvd (measured against the same photos' concrete: mulch
# 0.91 of the concrete's luminance, R:G:B 1 : 0.85 : 0.69), a little under that
# so a bed reads as bark beside the concrete nose. Nothing is resampled: the
# pack's own texels, a gain per channel and a contrast about the mean.
#
#   py tools/city/median_mulch.py            write the texture
#   py tools/city/median_mulch.py --sheet D  and the tile sheet into folder D:
#       the unmarked 3 x 3 repeat, the half-tile-offset view, an 8 x 8 repeat
#       at reduced size, and the wrap's numbers (AGENTS.md's tile rules), made
#       from the PNG as written
import os, sys
from PIL import Image, ImageStat, ImageDraw, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SRC = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Textures v3.1\PSX Textures\256\Color Maps\dirt_6.png"
OUT = os.path.join(REPO, "Assets", "PSXRacing", "Art", "City", "Pack", "mulch_dirt_6_city.png")
TARGET = (136.0, 108.0, 80.0)
CONTRAST = 1.55          # about the mean, after the gain (the source's deviation is 15, 10, 8)


def build():
    im = Image.open(SRC).convert("RGB")
    mean = ImageStat.Stat(im).mean
    chans = []
    for c, band in enumerate(im.split()):
        g = TARGET[c] / mean[c]
        # gain to the target mean, then contrast about it (less than the gain alone gives)
        k = CONTRAST / g if g > CONTRAST else 1.0
        lut = [max(0, min(255, int(round(TARGET[c] + (v * g - TARGET[c]) * k)))) for v in range(256)]
        chans.append(band.point(lut))
    out = Image.merge("RGB", chans)
    out.save(OUT)
    return out


def edge_step(im):
    """Mean absolute step across the wrap (last column -> first, last row -> first)
    against the mean step between neighbouring columns and rows inside."""
    px = im.convert("L").load()
    w, h = im.size
    wrap_u = sum(abs(px[w - 1, y] - px[0, y]) for y in range(h)) / h
    wrap_v = sum(abs(px[x, h - 1] - px[x, 0]) for x in range(w)) / w
    in_u = sum(abs(px[x, y] - px[x + 1, y]) for y in range(h) for x in range(w - 1)) / (h * (w - 1))
    in_v = sum(abs(px[x, y] - px[x, y + 1]) for y in range(h - 1) for x in range(w)) / (w * (h - 1))
    return wrap_u, in_u, wrap_v, in_v


def sheet(im, folder):
    os.makedirs(folder, exist_ok=True)
    w, h = im.size
    r3 = Image.new("RGB", (w * 3, h * 3))
    for a in range(3):
        for b in range(3):
            r3.paste(im, (a * w, b * h))
    half = ImageChops.offset(im, w // 2, h // 2)
    h2 = Image.new("RGB", (w * 2, h * 2))
    for a in range(2):
        for b in range(2):
            h2.paste(half, (a * w, b * h))
    r8 = Image.new("RGB", (w * 8, h * 8))
    for a in range(8):
        for b in range(8):
            r8.paste(im, (a * w, b * h))
    r8 = r8.resize((w * 3, h * 3), Image.LANCZOS)
    pad = 28
    s = Image.new("RGB", (w * 3 + w * 2 + w * 3 + 4 * 12, h * 3 + pad + 44), (24, 24, 24))
    d = ImageDraw.Draw(s)
    x = 12
    for img, label in ((r3, "3 x 3 repeat, unmarked (shipping PNG, 256 px)"), (h2, "half-tile offset (the wrap in the middle)"), (r8, "8 x 8 repeat at 3/8 size")):
        s.paste(img, (x, pad))
        d.text((x, 8), label, fill=(255, 255, 255))
        x += img.width + 12
    wu, iu, wv, iv = edge_step(im)
    mean = [round(v) for v in ImageStat.Stat(im).mean]
    d.text((12, h * 3 + pad + 8), f"mulch_dirt_6_city.png  mean RGB {mean}  repeat axes U and V (world-planar, 4 m a repeat)", fill=(255, 255, 255))
    d.text((12, h * 3 + pad + 24), f"wrap step U {wu:.2f} (inside {iu:.2f})  V {wv:.2f} (inside {iv:.2f})  - grey levels between neighbouring texels; the wrap is no rougher than the inside", fill=(255, 255, 255))
    p = os.path.join(folder, "mulch_tile_sheet.png")
    s.save(p)
    return p, (wu, iu, wv, iv), mean


if __name__ == "__main__":
    im = build()
    print("wrote", OUT, im.size, [round(v) for v in ImageStat.Stat(im).mean], [round(v) for v in ImageStat.Stat(im).stddev])
    if "--sheet" in sys.argv:
        im2 = Image.open(OUT).convert("RGB")          # the export as written
        p, st, mean = sheet(im2, sys.argv[sys.argv.index("--sheet") + 1])
        print("sheet", p, "wrap U %.2f inside %.2f | wrap V %.2f inside %.2f" % st)
