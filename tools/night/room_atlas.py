# tools/night/room_atlas.py  (NIGHT-LOOK PASS 5, 2026-10-28)
# THE ROOMS BEHIND THE GLASS: the owner's PSX Textures II pack interiors packed as LUMINANCE into
# one RGBA sheet, Assets/PSXRacing/Resources/CityRooms.png, for PSX/Lit's FacRoom (interior
# mapping on the lit facade windows). One channel a surface, one fetch a pixel:
#   R  wallpaper_wall_pt_1    (the walls: wallpaper over a wainscot - the panels at the image's
#                              BOTTOM, which Unity samples at v = 0, the floor)
#   G  building_ceiling_pt_1  (the ceiling: an acoustic tile in its grid)
#   B  carpet_pt_1_1          (an office floor)
#   A  wooden_floor_pt_1      (an apartment floor)
# Luminance only: a room is lit by its window's ONE yellow (owner rule: brightness varies, the hue
# never), so a surface brings its pattern, never its colour. Each channel is the texture's linear
# luminance scaled to a mean of 0.25 and stored as its square root (mean ~0.5): the shader's
# s * s * 4 is then the surface at a mean of 1.
# Usage: py -3 tools/night/room_atlas.py [packColorMapsDir]
import os, sys
import numpy as np
from PIL import Image

PACK = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.expanduser("~"), "OneDrive", "Documents", "Game Development", "PSX Assets",
    "PSX Textures II v1.6", "PSX Textures II", "512", "Color Maps")
SIZE = 256
CHANNELS = ["wallpaper_wall_pt_1", "building_ceiling_pt_1", "carpet_pt_1_1", "wooden_floor_pt_1"]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "PSXRacing", "Resources", "CityRooms.png")


def luminance(name):
    im = Image.open(os.path.join(PACK, name + ".png")).convert("RGB").resize((SIZE, SIZE), Image.BOX)
    s = np.asarray(im).astype(np.float64) / 255.0
    lin = np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4)
    L = lin @ np.array([0.2126, 0.7152, 0.0722])
    L = np.clip(L * (0.25 / max(L.mean(), 1e-6)), 0.0, 1.0)
    return np.sqrt(L)


chans = [luminance(n) for n in CHANNELS]
rgba = np.stack(chans, axis=-1)
Image.fromarray(np.round(rgba * 255.0).astype(np.uint8), "RGBA").save(os.path.normpath(OUT))
for n, c in zip(CHANNELS, chans):
    print(f"{n:24s} stored mean {c.mean():.3f}  p5 {np.percentile(c, 5):.3f}  p95 {np.percentile(c, 95):.3f}")
print("wrote", os.path.normpath(OUT))
