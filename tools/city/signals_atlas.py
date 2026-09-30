"""Charlotte's JUNCTION FURNITURE ATLAS (stop signs and traffic signals): one
256 px sheet of four 128 px cells that the STOP signs, their posts, the
signal poles, mast arms, span wires, signal heads and backplates, and the
painted stop bars all draw from, so a tile's junction furniture is one mesh
and one draw (CitySignals, PSX/Lit's PSX_FURNITURE variant: each vertex names
its cell in its colour and the pixel wraps its UVs inside it, exactly as the
utility poles do with CityFurniture.png).

Every cell is the owner's pack art (the owner's rule: never drawn in code):

  metal  Art/LifeSim/House/Textures/Metal.jpg (the house pack), tinted, in
         linear light, like the lamp posts (0.42, 0.43, 0.46): galvanised
         poles and mast arms, the sign's back and post
  black  the same metal tinted to black paint: signal heads, backplates, span
         wire and hangers
  stop   the STOP sign of the owner's Roads pack
         (PSX Assets/Roads/Roads/Textures/T (11).jpg), cropped to the sign's
         own octagon (white border included) so the octagon the mesh cuts
         spans the cell edge to edge
  paint  Art/City/Pack/concrete_pt_1_city.png (PSX Textures II), lifted to
         the white of fresh thermoplastic: the stop bars

Cells are texel rectangles with the origin at the BOTTOM left, as the shader
reads them: metal (0,0), black (128,0), stop (0,128), paint (128,128).
CitySignals.Cell* carries the same numbers.

    py tools/city/signals_atlas.py

Writes Assets/PSXRacing/Art/City/Signals/CitySignals.png (opaque RGB,
point-filtered, in the 16-bit set like CityFurniture.png: its .meta carries
the psx16 label and a linear import, and PSX/Lit decodes it).
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
ART = os.path.join(ROOT, "Assets", "PSXRacing", "Art")
OUT = os.path.join(ART, "City", "Signals", "CitySignals.png")
# the owner's art folder (the Roads pack is not otherwise in the project)
PACKS = os.path.join(os.path.expanduser("~"), "OneDrive", "Documents", "Game Development", "PSX Assets")
STOP_SRC = os.path.join(PACKS, "Roads", "Roads", "Textures", "T (11).jpg")
# the sign's octagon, white border included, in that photo (measured)
STOP_BOX = (10, 7, 246, 245)

CELL = 128
SIDE = 256

LAMP_TINT = (0.42, 0.43, 0.46)   # CityLampPostMat's, as the furniture atlas
BLACK_TINT = (0.16, 0.16, 0.17)  # black paint over steel, as the furniture atlas
PAINT_TINT = (2.6, 2.6, 2.5)     # concrete lifted to a white road paint


def to_lin(c):
    c = c / 255.0 if c > 1.0 else c
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def to_srgb(l):
    l = max(0.0, min(1.0, l))
    s = l * 12.92 if l <= 0.0031308 else 1.055 * (l ** (1.0 / 2.4)) - 0.055
    return int(round(s * 255.0))


def tinted(im, tint):
    tl = list(tint)
    lut = [[to_srgb(to_lin(v) * tl[ch]) for v in range(256)] for ch in range(3)]
    r, g, b = im.split()
    return Image.merge("RGB", (r.point(lut[0]), g.point(lut[1]), b.point(lut[2])))


def cell_of(path, tint=None, box=None):
    im = Image.open(path).convert("RGB")
    if box is not None:
        im = im.crop(box)
    im = im.resize((CELL, CELL), Image.BOX)
    return im if tint is None else tinted(im, tint)


def main():
    if not os.path.exists(STOP_SRC):
        raise SystemExit("the Roads pack's STOP sign is not at " + STOP_SRC)
    metal_src = os.path.join(ART, "LifeSim", "House", "Textures", "Metal.jpg")
    metal = cell_of(metal_src, LAMP_TINT)
    black = cell_of(metal_src, BLACK_TINT)
    stop = cell_of(STOP_SRC, box=STOP_BOX)
    paint = cell_of(os.path.join(ART, "City", "Pack", "concrete_pt_1_city.png"), PAINT_TINT)
    sheet = Image.new("RGB", (SIDE, SIDE))
    # PNG rows run top down; the shader's cells bottom up
    sheet.paste(metal, (0, SIDE - CELL))        # UV (0, 0)
    sheet.paste(black, (CELL, SIDE - CELL))     # UV (128, 0)
    sheet.paste(stop, (0, 0))                   # UV (0, 128)
    sheet.paste(paint, (CELL, 0))               # UV (128, 128)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.save(OUT, optimize=True)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
