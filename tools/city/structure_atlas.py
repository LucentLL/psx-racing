# THE STRUCTURE ATLAS (bridges pass, 2026-10-29): one 512 x 512 sheet for
# everything a Charlotte bridge is built of below its deck, plus the
# guardrail object markers and the high masts - CityMeshes.Bridges.cs maps
# its UVs into these BANDS (each a horizontal strip that tiles in U, so a
# 300 m girder repeats along its length and never stretches):
#
#   band  rows      v (Unity)      what
#   0     0-63      0.875-1.000    concrete girder web / deck edge (shadowed top)
#   1     64-127    0.750-0.875    weathering-steel plate girder (stiffeners)
#   2     128-255   0.500-0.750    pier column (streaks, splash-stained foot)
#   3     256-319   0.375-0.500    pier cap / bent beam (stained under-edge)
#   4     320-383   0.250-0.375    undersides: girder flanges, cap soffits
#   5     384-447   0.125-0.250    abutment / backwall (form-liner ribs, stained foot)
#   6     448-511   0.000-0.125    misc cells, 128 px each across:
#                                  u 0-.25 OM-3 object marker (yellow/black)
#                                  u .25-.5 bearing pad (elastomer + plate)
#                                  u .5-.75 galvanized steel (high masts)
#                                  u .75-1  luminaire housing (dark)
#
# All of it is the owner's PSX Textures II 512 pack (concrete_wall_pt_1/_2/_4,
# concrete_pt_5, rusty_metal_pt_7, metal_roofing_pt_1, concrete_pt_6),
# cropped, squashed into its band and brought to the brightness of the city's
# structural concrete (Art/City/Pack/concrete_pt_1_city.png) so a new girder
# reads as the same pour as the parapet above it. Only the object marker's
# yellow and black are a painted pattern (MUTCD OM-3, over the pack's grit).
#
#   py -3 tools/city/structure_atlas.py            writes the atlas
#   py -3 tools/city/structure_atlas.py --preview  and a 2x preview beside it
import os, sys
from PIL import Image, ImageStat, ImageFilter

PACK = r"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Textures II v1.6\PSX Textures II\512\Color Maps"
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "Art", "City", "Pack", "city_structure.png")
CITY_CONCRETE = os.path.join(ROOT, "Assets", "PSXRacing", "Art", "City", "Pack", "concrete_pt_1_city.png")
W = 512


def pack(name):
    return Image.open(os.path.join(PACK, name + ".png")).convert("RGB")


def to_mean(im, target, desat=0.0):
    """Scale each channel so the image's mean is target; desat pulls the
    variation toward grey first (0..1)."""
    if desat > 0:
        g = im.convert("L").convert("RGB")
        im = Image.blend(im, g, desat)
    m = ImageStat.Stat(im).mean
    r, gg, b = im.split()
    f = [target[i] / max(1.0, m[i]) for i in range(3)]
    r = r.point(lambda v: min(255, int(v * f[0] + 0.5)))
    gg = gg.point(lambda v: min(255, int(v * f[1] + 0.5)))
    b = b.point(lambda v: min(255, int(v * f[2] + 0.5)))
    return Image.merge("RGB", (r, gg, b))


def shade_rows(im, fn):
    """Multiply each row y (0 = top) by fn(y / h)."""
    px = im.load()
    w, h = im.size
    for y in range(h):
        k = fn((y + 0.5) / h)
        for x in range(w):
            p = px[x, y]
            px[x, y] = (min(255, int(p[0] * k)), min(255, int(p[1] * k)), min(255, int(p[2] * k)))
    return im


def shade_cols(im, fn):
    px = im.load()
    w, h = im.size
    for x in range(w):
        k = fn(x)
        if k == 1.0:
            continue
        for y in range(h):
            p = px[x, y]
            px[x, y] = (min(255, int(p[0] * k)), min(255, int(p[1] * k)), min(255, int(p[2] * k)))
    return im


def band(src, rows, h, target, desat=0.0, crop=None):
    im = src.crop(crop) if crop else src
    im = im.resize((W, h), Image.BILINEAR)
    return to_mean(im, target, desat)


def main():
    city = Image.open(CITY_CONCRETE).convert("RGB")
    cm = [round(v) for v in ImageStat.Stat(city).mean]          # the parapets' concrete
    light = (cm[0] + 4, cm[1] + 3, cm[2] + 2)
    atlas = Image.new("RGB", (W, W), (128, 128, 128))

    # 0: the girder web / deck edge - concrete_wall_pt_1's light pour with
    # its form lines, the top fifth in the overhang's shadow
    g = band(pack("concrete_wall_pt_1"), None, 64, light, 0.35, crop=(0, 0, 512, 300))
    shade_rows(g, lambda t: 0.62 + 0.38 * min(1.0, t / 0.22) if t < 0.22 else (0.97 if t > 0.9 else 1.0))
    atlas.paste(g, (0, 0))

    # 1: weathering-steel plate girder: the pack's rust, a stiffener every
    # 128 px (2 m at the 8 m repeat), the bottom flange's lit edge
    s = band(pack("rusty_metal_pt_7"), None, 64, (118, 78, 52), 0.1)
    shade_cols(s, lambda x: 0.72 if x % 128 in (60, 61) else 1.18 if x % 128 in (62, 63, 64) else 0.86 if x % 128 in (65, 66) else 1.0)
    shade_rows(s, lambda t: 0.7 if t < 0.12 else 1.15 if t > 0.88 else 1.0)
    atlas.paste(s, (0, 64))

    # 2: the pier column - concrete_wall_pt_2: vertical weathering, its dark
    # stained lower band kept as the splash line at the foot
    c = pack("concrete_wall_pt_2").transpose(Image.ROTATE_90).transpose(Image.ROTATE_270)   # (as drawn: foot down)
    c = band(c, None, 128, light, 0.3, crop=(0, 40, 512, 512))
    atlas.paste(c, (0, 128))

    # 3: the cap - concrete_pt_5's even pour, its under-edge darkened (drip)
    k = band(pack("concrete_pt_5"), None, 64, light, 0.35)
    shade_rows(k, lambda t: 1.0 if t < 0.8 else 0.86)
    atlas.paste(k, (0, 256))

    # 4: undersides - the same pour in shadow
    u = band(pack("concrete_wall_pt_1"), None, 64, (int(cm[0] * 0.80), int(cm[1] * 0.80), int(cm[2] * 0.82)), 0.4, crop=(0, 300, 512, 512))
    atlas.paste(u, (0, 320))

    # 5: abutment / backwall - concrete_wall_pt_4's form-liner ribs, warmed
    # to the pour and stained at the foot
    a = band(pack("concrete_wall_pt_4"), None, 64, light, 0.75)
    shade_rows(a, lambda t: 1.0 if t < 0.75 else 0.8)
    atlas.paste(a, (0, 384))

    # 6: the misc cells (128 x 64 each)
    grit = pack("painted_concrete_wall_pt_1").convert("L").resize((128, 64), Image.BILINEAR)
    om = Image.new("RGB", (128, 64))
    opx = om.load(); gpx = grit.load()
    for y in range(64):
        for x in range(128):
            n = 0.85 + 0.15 * (gpx[x, y] / 255.0)
            # OM-3: stripes sloping down toward the traffic side, 4 across
            yellow = ((x + y) // 16) % 2 == 0
            base = (250, 196, 18) if yellow else (24, 22, 20)
            opx[x, y] = tuple(min(255, int(v * n)) for v in base)
    atlas.paste(om, (0, 448))
    bear = band(pack("concrete_pt_6"), None, 64, (58, 56, 54), 0.6).crop((0, 0, 128, 64))
    shade_rows(bear, lambda t: 1.35 if t < 0.25 or t > 0.75 else 1.0)   # the steel plates above and below the pad
    atlas.paste(bear, (128, 448))
    galv = band(pack("metal_roofing_pt_1"), None, 64, (150, 154, 158), 0.5).crop((0, 0, 128, 64))
    atlas.paste(galv, (256, 448))
    hous = band(pack("metal_panels_pt_4"), None, 64, (52, 52, 50), 0.7).crop((0, 0, 128, 64))
    atlas.paste(hous, (384, 448))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    atlas.save(OUT)
    print("wrote", OUT, atlas.size, "city concrete mean", cm)
    if "--preview" in sys.argv:
        prev = os.path.join(os.environ.get("TEMP", HERE), "city_structure_preview.png")
        atlas.resize((W * 2, W * 2), Image.NEAREST).save(prev)
        print("preview", prev)


if __name__ == "__main__":
    main()
