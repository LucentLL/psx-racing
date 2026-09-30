"""The city's roadside FURNITURE ATLAS (Charlotte WP-15): one 256 px sheet of
four 128 px cells the utility poles, their wires and cobra-heads, the street
lamps and uptown's acorn posts all draw from, so a tile's furniture is one
mesh and one draw (CityPoles, PSX/Lit's PSX_FURNITURE variant: each vertex
names its cell in its colour and the pixel wraps its metre UVs inside it).

Every cell is the owner's pack art (the owner's rule: metal, wood, rock from
the PSX packs, never drawn in code), copies the project already ships:

  wood   Art/Guardrail/wood_pt_4.png   (PSX Textures II; the guardrail posts)
         - the creosoted pole, its grain running up the pole
  metal  Art/LifeSim/House/Textures/Metal.jpg  (the house pack; the lamp posts)
         tinted, in linear light, by the lamp posts' own material tint
         (PSXRacingBuilder.CityLampPostMat, (0.42, 0.43, 0.46)), so a lamp
         post drawn from the atlas is the lamp post it was
  black  the same metal tinted to black paint: uptown's acorn posts
  wire   the same metal tinted near black: the conductors and the cable

Cells are in texel rectangles of the sheet with the origin at the BOTTOM
left, as the shader reads them (UV space): wood (0,0), metal (128,0),
black (0,128), wire (128,128). CityPoles.Cell* carries the same numbers.

    py tools/city/furniture_atlas.py

Writes Assets/PSXRacing/Art/City/Furniture/CityFurniture.png (opaque RGB,
point-filtered in the game, in the 16-bit set: its .meta carries the psx16
label and a linear import - ReleaseBudget - and PSX/Lit decodes it).
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
ART = os.path.join(ROOT, "Assets", "PSXRacing", "Art")
OUT = os.path.join(ART, "City", "Furniture", "CityFurniture.png")

CELL = 128
SIDE = 256

# the tints, authored as the colours a material would carry (sRGB)
LAMP_TINT = (0.42, 0.43, 0.46)   # CityLampPostMat's
BLACK_TINT = (0.16, 0.16, 0.17)  # black paint over galvanised steel
WIRE_TINT = (0.10, 0.10, 0.10)   # weathered conductor and cable


def to_lin(c):
    c = c / 255.0 if c > 1.0 else c
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def to_srgb(l):
    l = max(0.0, min(1.0, l))
    s = l * 12.92 if l <= 0.0031308 else 1.055 * (l ** (1.0 / 2.4)) - 0.055
    return int(round(s * 255.0))


def cell_of(path, tint=None):
    im = Image.open(path).convert("RGB")
    # the whole repeat shrunk into the cell, so it still tiles
    im = im.resize((CELL, CELL), Image.BOX)
    if tint is None:
        return im
    tl = [to_lin(t) for t in tint]
    lut = [[to_srgb(to_lin(v) * tl[ch]) for v in range(256)] for ch in range(3)]
    r, g, b = im.split()
    return Image.merge("RGB", (r.point(lut[0]), g.point(lut[1]), b.point(lut[2])))


def main():
    wood = cell_of(os.path.join(ART, "Guardrail", "wood_pt_4.png"))
    metal_src = os.path.join(ART, "LifeSim", "House", "Textures", "Metal.jpg")
    metal = cell_of(metal_src, LAMP_TINT)
    black = cell_of(metal_src, BLACK_TINT)
    wire = cell_of(metal_src, WIRE_TINT)
    sheet = Image.new("RGB", (SIDE, SIDE))
    # PNG rows run top down; the shader's cells bottom up
    sheet.paste(wood, (0, SIDE - CELL))         # UV (0, 0)
    sheet.paste(metal, (CELL, SIDE - CELL))     # UV (128, 0)
    sheet.paste(black, (0, 0))                  # UV (0, 128)
    sheet.paste(wire, (CELL, 0))                # UV (128, 128)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.save(OUT, optimize=True)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
