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
#   5 granite office, punched    built here (2026-10-12): Pizza Doggy PSX
#                                Textures concrete_2 as the stone's grain, the
#                                skyscraper_pack blue glass in the windows, the
#                                grid laid out in code; 8 floors x 8 bays of 1.9 m
#   6 precast office, ribbon     the same parts: white precast spandrels and a
#                                continuous window band per floor; 8 floors
#   7 THE STRUCTURE'S PARTS (facade structure, 2026-10-16): not a facade but
#     the surfaces PSX/Lit's tower-scale structure draws its solid parts in,
#     each a neutral grey at a linear mean of 0.5 (the shader divides it out,
#     so the part keeps its own colour and gains the grain):
#       x   0- 63  stone: PSX Textures concrete_2 (2 m x 8 m)
#       x  64- 95  aluminium panels: PSX Textures II metal_panels_pt_4 (1 m x 8 m)
#       x  96-127  louvres: PSX Textures II metal_shutters_pt_1 (1 m x 8 m)
#     Its night mask is empty: no part is ever a lit window.
#
# 5 and 6 exist because the city's only "stone" (3) is a weathered tenement
# with window units: on a granite office tower it read as a dirty grid (owner,
# 2026-10-12, "this building looks worse").
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


def office(kind, scale=1, seed=50):
    """Columns 5 (granite, punched) and 6 (precast, ribbon), at `scale` x the
    atlas size (2 for the Enhanced twin). 8 floors of 32 px, 8 bays of 16 px."""
    w, h = W * scale, H * scale
    grain = rgb(os.path.join(SRC, "psx_concrete_2.png")).resize((w, h), Image.BICUBIC)
    g = np.asarray(grain).astype(np.float32).mean(2)
    g = (g - g.mean()) / (g.std() + 1e-6)                  # the grain, neutral
    base = 0.66 if kind == "granite" else 0.80              # sRGB wall level
    amp = 0.035 if kind == "granite" else 0.025
    wall = np.clip(base + amp * g, 0, 1)
    # a quiet vertical pier / horizontal spandrel rhythm, so a wall reads as
    # built, not as a sheet
    y = np.arange(h)[:, None]; x = np.arange(w)[None, :]
    fy = (y % (32 * scale)) / (32.0 * scale); bx = (x % (16 * scale)) / (16.0 * scale)
    col = np.stack([wall * 1.00, wall * 0.985, wall * 0.96], 2) if kind == "granite" else np.stack([wall] * 3, 2)
    glass = np.asarray(rgb(os.path.join(SRC, "skyscraper_building_04.png")).crop((0, 0, 128, 256)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255
    glass = glass * 0.55                                     # tinted, darker than the wall
    if kind == "granite":
        win = (bx > 0.22) & (bx < 0.78) & (fy > 0.22) & (fy < 0.84)
        sill = (bx > 0.18) & (bx < 0.82) & (fy >= 0.84) & (fy < 0.89)
        col[sill] *= 1.06
    else:
        win = (fy > 0.30) & (fy < 0.78)
        mull = (np.abs(bx - 0.5) > 0.47)
        col[win & mull] = col[win & mull] * 0.55
        win = win & ~mull
    out = np.where(win[..., None], glass, col)
    # reveal shadow under each window head
    head = np.roll(win, scale * 2, axis=0) & ~win
    out[head] *= 0.80
    c = np.clip(out * 255, 0, 255).astype(np.uint8)
    rows = (y * 8) // h; blk = (x // (16 * scale)) // 2
    table = ids(seed + (0 if kind == "granite" else 1), 8 * 64).reshape(8, 64)
    m = np.zeros((h, w, 4), np.uint8)
    cover = np.where(win, 255, 0).astype(np.uint8)
    m[..., 0] = cover; m[..., 1] = table[rows, blk].astype(np.uint8); m[..., 3] = cover
    return c, m


def parts(scale=1):
    """Column 7: stone grain, aluminium panels, louvres - grey, linear mean
    0.5 each, at `scale` x the atlas size. Real-world sizes above: each
    source is tiled to the strip's metres and resampled isotropically."""
    w, h = W * scale, H * scale
    out = np.zeros((h, w, 3), np.float32)
    def strip(name, x0, x1, src_m, strip_m, contrast):
        src = rgb(os.path.join(SRC, name))
        sw = src.size[0]
        px_per_m = sw / src_m                       # source pixels a metre
        need_w = int(round(px_per_m * strip_m[0])); need_h = int(round(px_per_m * strip_m[1]))
        tile = Image.new("RGB", (max(need_w, 1), max(need_h, 1)))
        for ty in range(0, need_h, src.size[1]):
            for tx in range(0, need_w, sw):
                tile.paste(src, (tx, ty))
        g = np.asarray(tile.resize((x1 - x0, h), Image.LANCZOS)).astype(np.float32) / 255
        g = np.where(g <= 0.04045, g / 12.92, ((g + 0.055) / 1.055) ** 2.4).mean(2)   # linear luminance
        g = 0.5 + contrast * (g / max(g.mean(), 1e-4) - 1.0) * 0.5
        out[:, x0:x1, :] = np.clip(g, 0.05, 0.95)[..., None]
    strip("psx_concrete_2.png", 0, w // 2, 2.0, (2.0, 8.0), 1.0)
    strip("psx2_metal_panels_4.png", w // 2, w * 3 // 4, 2.0, (1.0, 8.0), 1.0)
    strip("psx2_metal_shutters_1.png", w * 3 // 4, w, 1.5, (1.0, 8.0), 1.0)
    srgb = np.where(out <= 0.0031308, out * 12.92, 1.055 * out ** (1 / 2.4) - 0.055)
    return np.clip(srgb * 255 + 0.5, 0, 255).astype(np.uint8)


def lin(c):
    v = c.astype(np.float32) / 255
    return np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4).reshape(-1, 3).mean(0)


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
    for kind in ("granite", "precast"):
        c, m = office(kind)
        cols.append(c); masks.append(m)
        print(kind, "mean linear", [round(float(v), 3) for v in lin(c)])
    cols.append(parts()); masks.append(np.zeros((H, W, 4), np.uint8))   # 7: the structure's parts
    # the Enhanced twin (2x) gets the same two columns at its own size
    enh = os.path.join(REPO, "Assets", "PSXRacing", "EnhancedSrc", "TexEnhanced", "City", "Facade", "Core_cityatlas.png")
    if os.path.exists(enh):
        e = np.asarray(rgb(enh)).copy()
        if e.shape == (H * 2, W * 2 * COLS, 3):
            for k, kind in ((5, "granite"), (6, "precast")):
                e[:, k * W * 2:(k + 1) * W * 2] = office(kind, 2)[0]
            e[:, 7 * W * 2:8 * W * 2] = parts(2)
            Image.fromarray(e, "RGB").save(enh, optimize=True)
            print("enhanced twin: columns 5, 6, 7 written")
    atlas = np.concatenate(cols, axis=1)
    mask = np.concatenate(masks, axis=1)
    assert atlas.shape == (H, W * COLS, 3) and mask.shape == (H, W * COLS, 4)
    Image.fromarray(atlas, "RGB").save(os.path.join(OUT, "city_facade_atlas.png"), optimize=True)
    Image.fromarray(mask, "RGBA").save(os.path.join(OUT, "city_facade_atlas_night.png"), optimize=True)
    lit = [(masks[k][..., 3] > 127).mean() for k in range(7)]
    print("facade atlas", atlas.shape, "glass share per column", [round(v, 2) for v in lit])


if __name__ == "__main__":
    main()
