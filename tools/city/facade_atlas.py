# The uptown facade atlas (Uptown B1, 2026-10-04).
#
#   py tools/city/facade_atlas.py
#
# One 1024 x 256 texture of eight 128 x 256 columns, so every OSM building's
# walls in a tile are ONE material (CityMeshes.Slot.FacadeGlass, PSX/Lit with
# PSX_FACADE): the vertex colour picks the column (alpha) and tints it (rgb),
# and the shader wraps U inside the column. Columns, and what each repeat is:
#
#   0 silver grid curtain wall   skyscraper_pack building_03, 16 floors x 14 bays
#   1 blue glass curtain wall    skyscraper_pack building_04, 20 floors x 16 bays
#   2 teal glass curtain wall    skyscraper_pack building_08 (its lower half,
#                                stacked twice), 24 floors x 16 bays
#   3 precast stone, punched     Buildings building_10 (Art/City/city_facade_mid.jpg),
#                                4 floors
#   4 brick, punched windows     Buildings building_01 (Art/City/city_facade_tower.jpg),
#                                4 floors
#   5-7 spare (copies of 0, 3, 4; nothing points at them yet)
#
# CityMeshes.FacadeLooks holds the same table (floors and metres per repeat):
# change one, change both.
#
# THE NIGHT MASK beside it (city_facade_atlas_night.png) follows the contract of
# tools/night/window_masks.py: R = A = glass, G = a random id per lit unit,
# B = 0 (no shopfronts). The curtain walls light by FLOOR SEGMENTS (four bays
# of one floor share an id: an office floor's lights are on or off together);
# the two photographed facades take their existing masks, resampled nearest.
#
# Both are written at their final size, so the importer never resamples them,
# and they are the only city textures with mip maps (PSXTextureCaps.MipsFor):
# a 64-floor tower seen from a kilometre away must average its windows, not
# alias them into bands.
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
CITY = os.path.join(REPO, "Assets", "PSXRacing", "Art", "City")
SRC = os.path.join(CITY, "Facade", "Source")
OUT = os.path.join(CITY, "Facade")
W, H, COLS = 128, 256, 8


def rgb(p):
    return Image.open(p).convert("RGB")


def ids(seed, n):
    r = np.random.default_rng(seed)
    return r.integers(1, 256, size=n)


def curtain(img, rows, bay_px, grid_cover, seed, block=4):
    """A curtain-wall column and its mask: ids per (floor, block of bays)."""
    a = np.asarray(img).astype(np.float32)
    y = np.arange(H)[:, None]
    x = np.arange(W)[None, :]
    row = np.minimum((y * rows) // H, rows - 1)
    bay = np.floor(x / bay_px).astype(np.int64)
    blk = bay // block
    table = ids(seed, rows * 64).reshape(rows, 64)
    g = table[row, blk].astype(np.uint8)
    if grid_cover:
        cover = np.ones((H, W), np.uint8) * 255
    else:
        lum = a.mean(2)
        thr = (np.percentile(lum, 20) + np.percentile(lum, 90)) * 0.5
        cover = np.where(lum < thr, 255, 0).astype(np.uint8)
    m = np.zeros((H, W, 4), np.uint8)
    m[..., 0] = cover; m[..., 1] = g; m[..., 3] = cover
    return a.astype(np.uint8), m


def photo(img_path, mask_path):
    c = rgb(img_path).resize((W, H), Image.LANCZOS)
    m = Image.open(mask_path).convert("RGBA").resize((W, H), Image.NEAREST)
    return np.asarray(c), np.asarray(m)


def main():
    cols, masks = [], []
    # 0: silver grid - 126 px of whole 9 px bays, stretched nearest to 128
    s3 = rgb(os.path.join(SRC, "skyscraper_building_03.png")).crop((0, 0, 126, 256)).resize((W, H), Image.NEAREST)
    c, m = curtain(s3, 16, 9 * W / 126.0, False, 3)
    cols.append(c); masks.append(m)
    # 1: blue glass
    s4 = rgb(os.path.join(SRC, "skyscraper_building_04.png")).crop((0, 0, 128, 256))
    c, m = curtain(s4, 20, 8, True, 4)
    cols.append(c); masks.append(m)
    # 2: teal glass - the lower half of its sheet, stacked twice
    s8 = rgb(os.path.join(SRC, "skyscraper_building_08.png")).crop((0, 128, 128, 256))
    st = Image.new("RGB", (W, H)); st.paste(s8, (0, 0)); st.paste(s8, (0, 128))
    c, m = curtain(st, 24, 8, True, 8)
    cols.append(c); masks.append(m)
    # 3, 4: the photographed facades the city already wears, with their masks
    c, m = photo(os.path.join(CITY, "city_facade_mid.jpg"), os.path.join(CITY, "Night", "city_facade_mid_night.png"))
    cols.append(c); masks.append(m)
    c, m = photo(os.path.join(CITY, "city_facade_tower.jpg"), os.path.join(CITY, "Night", "city_facade_tower_night.png"))
    cols.append(c); masks.append(m)
    for k in (0, 3, 4):
        cols.append(cols[k]); masks.append(masks[k])
    atlas = np.concatenate(cols, axis=1)
    mask = np.concatenate(masks, axis=1)
    assert atlas.shape == (H, W * COLS, 3) and mask.shape == (H, W * COLS, 4)
    Image.fromarray(atlas, "RGB").save(os.path.join(OUT, "city_facade_atlas.png"), optimize=True)
    Image.fromarray(mask, "RGBA").save(os.path.join(OUT, "city_facade_atlas_night.png"), optimize=True)
    lit = [(masks[k][..., 3] > 127).mean() for k in range(5)]
    print("facade atlas", atlas.shape, "glass share per column", [round(v, 2) for v in lit])


if __name__ == "__main__":
    main()
