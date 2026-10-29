# The facade atlas (Uptown B1, 2026-10-04; the BUILDING PASS, 2026-10-28).
#
#   py tools/city/facade_atlas.py            (writes the atlas, its night mask
#                                             and the Enhanced twin)
#   py tools/city/facade_atlas.py --sheet D  (also a check sheet in folder D:
#                                             every column tiled 2 x 2 with its
#                                             night mask over it)
#
# One texture of SIXTEEN 128 x 256 columns (2048 x 256; the Enhanced twin is
# 4096 x 512), so every OSM building's walls in a tile are ONE material
# (CityMeshes.Slot.FacadeGlass, PSX/Lit with PSX_FACADE): the vertex colour
# picks the column (alpha x 32 + the red channel's low bit x 8) and tints it
# (rgb), and the shader wraps U inside the column. Columns, and what each
# repeat is:
#
#   0 silver grid curtain wall   BUILDING PASS: re-laid at 8 floors x 8 bays a
#   1 blue glass curtain wall    repeat (32 px a floor; it was 16, 20 and 24
#   2 teal glass curtain wall    floors - 10 to 16 px a floor, the owner's
#                                "blocky blue glass tower"): each pane is a
#                                pane of the skyscraper_pack sheet the column
#                                always wore (building_03, _04, _08), its
#                                frames and spandrels laid out in code at the
#                                shader's own CW_MULLION / CW_SPANDREL shares
#   3 precast stone, punched     Buildings building_10 (Art/City/city_facade_mid.jpg), 4 floors
#   4 brick, punched windows     Buildings building_01 (Art/City/city_facade_tower.jpg), 4 floors
#   5 granite office, punched    PSX Textures concrete_2 grain + skyscraper_pack glass,
#                                the grid laid out in code; 8 floors x 8 bays of 1.9 m
#   6 precast office, ribbon     the same parts, a continuous window band a floor; 8 floors
#   7 THE STRUCTURE'S PARTS (facade structure, 2026-10-16): stone grain
#     (x 0-63, PSX Textures concrete_2), aluminium panels (x 64-95, PSX
#     Textures II metal_panels_pt_4), louvres (x 96-127, metal_shutters_pt_1),
#     each a neutral grey at a linear mean of 0.5. Its night mask is empty.
#     The building pass draws its CORNICES, belts, canopies, sign bands and
#     roof gear in it too (PSX/Lit wraps column 7 inside its stone half, or
#     its panel quarter on a metal vertex).
#   BUILDING PASS (2026-10-28) - the Buildings pack's photographed facades
#   (tools/city/facade_src), each a whole number of floors and bays, stacked
#   up the column where the photograph is short, so a floor is 43-64 px:
#   8 red-brown brick, wide sash windows   building_03, 3 floors x 3 bays, x2 = 6 floors
#   9 tan glazed brick, small windows      building_07 (two uniform floors, three bays), x2 = 4 floors
#  10 mottled brick, gold frames           building_09, 4 floors x 4 bays
#  11 grey brick tenement                  building_04, 4 floors, irregular windows
#  12 red brick, patterned piers           building_05, 3 floors x 4 bays, x2 = 6 floors
#  13 loft, big mullioned windows          building_02, 2 floors x 4 bays, x2 = 4 floors
#  14 tan brick, paired sash               building_06, 2 floors x 4 bays, x2 = 4 floors
#  15 modern apartment                     PSX Textures II painted_concrete_wall_pt_1 as
#                                          the render, skyscraper_pack glass in tall windows,
#                                          the grid laid out in code; 8 floors x 4 bays
#
# CityMeshes.FacadeLooks holds the same table (floors and metres per repeat)
# and PSX/Lit its FAC_* tables: change one, change all three.
#
# THE NIGHT MASK beside it (city_facade_atlas_night.png) follows the contract of
# tools/night/window_masks.py: R = A = glass, G = a random id per lit unit,
# B = 0 (no shopfronts). The curtain walls light by FLOOR SEGMENTS (four bays
# of one floor share an id); the photographed facades' windows are FOUND
# (colour distance from the wall's median, Otsu, the connected blobs' boxes) and
# each box gets its own id - the check sheet shows every box over its window.
#
# Both are written at their final size, so the importer never resamples them,
# and they are the only city textures with mip maps (PSXTextureCaps.MipsFor).
import os, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
CITY = os.path.join(REPO, "Assets", "PSXRacing", "Art", "City")
SRC = os.path.join(CITY, "Facade", "Source")
PHOTO = os.path.join(HERE, "facade_src")
OUT = os.path.join(CITY, "Facade")
# THE ENHANCED TWIN (EnhancedSrc/TexEnhanced/City/Facade/Core_cityatlas.png,
# 2x) is cut exactly as the classic columns are, at twice the pixels, and
# graded to each classic column's linear mean, so the ONE night mask lands on
# the windows of both tiers and CityMeshes.FacadeLooks' means hold for both
# (main's enhanced-download pass, 2026-10-28). Columns 3 and 4 are the owner's
# Enhanced twins of their photographs, the glass of 5 and 6 at 2x his twin of
# tower04; the re-laid curtain walls and the Buildings-pack photographs are
# cut at 2x from their own sources.
ENHDIR = os.path.join(REPO, "Assets", "PSXRacing", "EnhancedSrc", "TexEnhanced")
ENH = os.path.join(ENHDIR, "City", "Facade", "Core_cityatlas.png")
TWINS = {
    "skyscraper_building_04.png": ("City", "Facade", "Source", "Earlier_Regional_tower04.png"),
    "city_facade_mid.jpg": ("Buildings", "Textures", "Core_citymid.png"),
    "city_facade_tower.jpg": ("Buildings", "Textures", "Core_citytower.png"),
}


def to_lin(c):
    v = np.asarray(c).astype(np.float32) / 255
    return np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4)


def match_mean(e, c):
    """Enhanced column `e` graded per channel, in linear light, to the
    classic column `c`'s mean (the tint is an OSM colour over that mean)."""
    el, cl = to_lin(e), to_lin(c)
    gain = cl.reshape(-1, 3).mean(0) / np.maximum(el.reshape(-1, 3).mean(0), 1e-6)
    v = np.clip(el * gain, 0, 1)
    v = np.where(v <= 0.0031308, v * 12.92, 1.055 * v ** (1 / 2.4) - 0.055)
    return np.clip(v * 255 + 0.5, 0, 255).astype(np.uint8)
W, H, COLS = 128, 256, 16
# the shader's curtain-wall shares (PSXLit.shader CW_MULLION, CW_SPANDREL)
CW_MULLION, CW_SPANDREL = 0.12, 0.22


def rgb(p):
    return Image.open(p).convert("RGB")


def ids(seed, n):
    r = np.random.default_rng(seed)
    return r.integers(1, 256, size=n)


def photo(img_path, mask_path):
    c = rgb(img_path).resize((W, H), Image.LANCZOS)
    m = Image.open(mask_path).convert("RGBA").resize((W, H), Image.NEAREST)
    return np.asarray(c), np.asarray(m)


def curtain_synth(kind, scale=1, seed=3):
    """Columns 0-2 at 8 floors x 8 bays a repeat. Each new pane is one pane
    of the old sheet (its own tone and reflection, resampled into the new
    pane), the frames and spandrels laid out at the shader's shares: a
    mullion on each bay's LEFT edge (frac(bays) < CW_MULLION), the slab at
    each floor's FOOT (frac(storeys) < CW_SPANDREL), as PSX/Lit's night
    panes cut them."""
    if kind == "silver":
        sheet = rgb(os.path.join(SRC, "skyscraper_building_03.png")).crop((0, 0, 126, 256))
        sb, sf = 14, 16
    elif kind == "blue":
        sheet = rgb(os.path.join(SRC, "skyscraper_building_04.png")).crop((0, 0, 128, 256))
        sb, sf = 16, 20
    else:
        half = rgb(os.path.join(SRC, "skyscraper_building_08.png")).crop((0, 128, 128, 256))
        sheet = Image.new("RGB", (128, 256)); sheet.paste(half, (0, 0)); sheet.paste(half, (0, 128))
        sb, sf = 16, 24
    s = np.asarray(sheet).astype(np.float32)
    sw, sh = sheet.size
    pw, ph = sw / sb, sh / sf                       # a source pane, px
    w, h = W * scale, H * scale
    cw, chh = w // 8, h // 8                        # a new pane cell, px
    mull = max(1, int(round(CW_MULLION * cw)))
    span = max(1, int(round(CW_SPANDREL * chh)))
    lum = s.mean(2)
    # the frame's colour: the sheet's bright texels (silver), its darkest (glass)
    if kind == "silver":
        frame = s[lum > np.percentile(lum, 80)].mean(0)
        slab = frame * 0.92
    else:
        frame = s[lum < np.percentile(lum, 8)].mean(0) * 0.85
        slab = s.reshape(-1, 3).mean(0) * 0.62
    grain = np.asarray(rgb(os.path.join(SRC, "psx2_metal_panels_4.png")).resize((w, h), Image.BICUBIC)).astype(np.float32).mean(2)
    grain = (grain - grain.mean()) / (grain.std() + 1e-6)
    out = np.zeros((h, w, 3), np.float32)
    glass_mean = s[lum <= np.percentile(lum, 80)].mean(0) if kind == "silver" else s.reshape(-1, 3).mean(0)
    rng = np.random.default_rng(seed)
    ox, oy = int(rng.integers(0, sb)), int(rng.integers(0, sf))
    for fb in range(8):                              # floor from the BOTTOM
        for bb in range(8):
            sx, sy = (bb + ox) % sb, (fb + oy) % sf
            # the source pane's interior (its own frame left out), image rows from the top
            x0 = int(round(sx * pw)) + (1 if kind == "silver" else 0)
            x1 = max(x0 + 1, int(round((sx + 1) * pw)) - (1 if kind == "silver" else 0))
            ty = sf - 1 - sy
            y0 = int(round(ty * ph)) + (1 if kind == "silver" else 0)
            y1 = max(y0 + 1, int(round((ty + 1) * ph)) - (1 if kind == "silver" else 0))
            pane = Image.fromarray(np.clip(s[y0:y1, x0:x1], 0, 255).astype(np.uint8))
            gw, gh = cw - mull, chh - span
            pane = np.asarray(pane.resize((gw, gh), Image.BICUBIC)).astype(np.float32)
            # each pane keeps its own shading but only half its difference
            # from the sheet's mean pane: at a pane four times the old size
            # the full spread read as a blotchy checkerboard (and PSX/Lit
            # adds its own per-pane tint and tilt on top)
            pane = glass_mean + 0.5 * (pane.mean((0, 1)) - glass_mean) + (pane - pane.mean((0, 1)))
            top = (7 - fb) * chh                     # the cell's image rows
            left = bb * cw
            out[top:top + chh, left:left + cw] = slab
            out[top:top + gh, left + mull:left + cw] = pane
            out[top:top + chh, left:left + mull] = frame
            if kind != "silver":                     # a lighter transom cap on the slab
                out[top + gh:top + gh + max(1, scale), left:left + cw] = frame * 1.35
    metal = np.zeros((h, w), bool)
    for fb in range(8):
        top = (7 - fb) * chh
        metal[top + chh - span:top + chh, :] = True
    for bb in range(8):
        metal[:, bb * cw:bb * cw + mull] = True
    out = np.where(metal[..., None], out * (1.0 + 0.04 * grain[..., None]), out)
    c = np.clip(out, 0, 255).astype(np.uint8)
    # mask: the glass; ids per floor and four-bay block
    m = np.zeros((h, w, 4), np.uint8)
    glass = (~metal).astype(np.uint8) * 255
    yy = np.arange(h)[:, None]; xx = np.arange(w)[None, :]
    row = 7 - (yy // chh); blk = (xx // cw) // 4
    table = ids(seed + 100, 8 * 4).reshape(8, 4)
    m[..., 0] = glass; m[..., 1] = table[row, blk].astype(np.uint8); m[..., 3] = glass
    return c, m


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
    y = np.arange(h)[:, None]; x = np.arange(w)[None, :]
    fy = (y % (32 * scale)) / (32.0 * scale); bx = (x % (16 * scale)) / (16.0 * scale)
    col = np.stack([wall * 1.00, wall * 0.985, wall * 0.96], 2) if kind == "granite" else np.stack([wall] * 3, 2)
    # the glass: the pack's blue curtain wall, or at 2x the owner's Enhanced twin of it
    gsrc, gcrop = os.path.join(SRC, "skyscraper_building_04.png"), (0, 0, 128, 256)
    if scale == 2 and os.path.exists(os.path.join(ENHDIR, *TWINS["skyscraper_building_04.png"])):
        gsrc, gcrop = os.path.join(ENHDIR, *TWINS["skyscraper_building_04.png"]), (0, 0, 256, 512)
    glass = np.asarray(rgb(gsrc).crop(gcrop).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255
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
    head = np.roll(win, scale * 2, axis=0) & ~win
    out[head] *= 0.80
    c = np.clip(out * 255, 0, 255).astype(np.uint8)
    rows = (y * 8) // h; blk = (x // (16 * scale)) // 2
    table = ids(seed + (0 if kind == "granite" else 1), 8 * 64).reshape(8, 64)
    m = np.zeros((h, w, 4), np.uint8)
    cover = np.where(win, 255, 0).astype(np.uint8)
    m[..., 0] = cover; m[..., 1] = table[rows, blk].astype(np.uint8); m[..., 3] = cover
    return c, m


def apartment(scale=1, seed=77):
    """Column 15: a modern mid-rise apartment block (South End): a smooth
    render (PSX Textures II painted_concrete_wall_pt_1) with tall windows in
    a dark frame, a sill under each, 8 floors x 4 bays of 3 m."""
    w, h = W * scale, H * scale
    g = np.asarray(rgb(os.path.join(PHOTO, "psx2_painted_concrete_wall_1.png")).resize((w, h), Image.BICUBIC)).astype(np.float32).mean(2)
    g = (g - g.mean()) / (g.std() + 1e-6)
    wall = np.clip(0.78 + 0.03 * g, 0, 1)
    col = np.stack([wall * 1.0, wall * 0.985, wall * 0.955], 2)
    y = np.arange(h)[:, None]; x = np.arange(w)[None, :]
    fy = (y % (32 * scale)) / (32.0 * scale); bx = (x % (32 * scale)) / (32.0 * scale)
    glass = np.asarray(rgb(os.path.join(SRC, "skyscraper_building_04.png")).crop((0, 0, 128, 256)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255 * 0.55
    frame = (bx > 0.14) & (bx < 0.86) & (fy > 0.14) & (fy < 0.88)
    win = (bx > 0.17) & (bx < 0.83) & (fy > 0.17) & (fy < 0.86)
    mid = (np.abs(bx - 0.5) < 0.025) & win                    # the casement's mullion
    out = np.where(frame[..., None], np.array([0.20, 0.20, 0.21]), col)
    out = np.where(win[..., None], glass, out)
    out = np.where(mid[..., None], np.array([0.20, 0.20, 0.21]), out)
    sill = (bx > 0.12) & (bx < 0.88) & (fy >= 0.88) & (fy < 0.92)
    out[sill] = out[sill] * 1.12
    head = np.roll(frame, scale * 2, axis=0) & ~frame
    out[head] *= 0.82
    c = np.clip(out * 255, 0, 255).astype(np.uint8)
    win = win & ~mid
    rows = (y * 8) // h; bay = x // (32 * scale)
    table = ids(seed, 8 * 8).reshape(8, 8)
    m = np.zeros((h, w, 4), np.uint8)
    cover = np.where(win, 255, 0).astype(np.uint8)
    m[..., 0] = cover; m[..., 1] = table[rows, bay].astype(np.uint8); m[..., 3] = cover
    return c, m


# The photographed facades: source, crop (x0, y0, x1, y1) of whole floors and
# bays, the floors in the crop, how many times it is stacked up the column.
PHOTOS = {
    8: ("building_03.jpg", None, 3, 2),
    9: ("building_07.jpg", (179, 95, 803, 565), 2, 2),
    10: ("building_09.jpg", (0, 18, 916, 902), 4, 1),   # its right edge holds a sliver of a fifth bay
    11: ("building_04.jpg", None, 4, 1),
    12: ("building_05.jpg", None, 3, 2),
    13: ("building_02.jpg", None, 2, 2),
    14: ("building_06.jpg", None, 2, 2),
}


def find_windows(a):
    """The window boxes of a photographed facade (Classic size): pixels far
    in colour from the wall's median, cleaned, their blobs' bounding boxes."""
    med = np.median(a.reshape(-1, 3), axis=0)
    d = np.sqrt(((a - med) ** 2).sum(2))
    # Otsu on the distance
    hist, edges = np.histogram(d, bins=64)
    p = hist / hist.sum(); c = np.cumsum(p); mu = np.cumsum(p * edges[:-1]); mt = mu[-1]
    sb = (mt * c - mu) ** 2 / (c * (1 - c) + 1e-9)
    thr = edges[int(np.argmax(sb))]
    b = d > thr
    b = ndimage.binary_opening(b, np.ones((2, 2)))
    b = ndimage.binary_closing(b, np.ones((3, 3)))
    lab, n = ndimage.label(b)
    boxes = []
    for sl in ndimage.find_objects(lab):
        if sl is None: continue
        hh, ww = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        if hh * ww < 14 or hh < 4 or ww < 3: continue
        boxes.append([sl[1].start, sl[0].start, sl[1].stop, sl[0].stop])
    # a double-hung window's two sashes (a light lower pane read as wall)
    # are ONE opening: boxes over each other with most of their width in
    # common and a few pixels between them are merged
    merged = True
    while merged:
        merged = False
        for i in range(len(boxes)):
            for j in range(i + 1, len(boxes)):
                a, c = boxes[i], boxes[j]
                ov = min(a[2], c[2]) - max(a[0], c[0])
                gap = max(a[1], c[1]) - min(a[3], c[3])
                if ov >= 0.6 * min(a[2] - a[0], c[2] - c[0]) and gap <= 4:
                    boxes[i] = [min(a[0], c[0]), min(a[1], c[1]), max(a[2], c[2]), max(a[3], c[3])]
                    del boxes[j]; merged = True; break
            if merged: break
    return [tuple(b) for b in boxes]


def regularize(boxes, floor_px):
    """The facade's own grid from what was found: specks (a sill, an air
    conditioner, a transom line) under 0.18 of a storey tall are dropped;
    the rest sort into bays (by centre, a gap of half a window apart) and
    storeys (by centre, the photograph's own floor height). Every bay x
    storey is then a window: across, the bay's median edges; up, the union
    of what the storey's windows showed (a double-hung window's dark upper
    sash in one bay, its light lower one in the next = the whole opening)."""
    boxes = [b for b in boxes if (b[3] - b[1]) >= 0.18 * floor_px]
    if len(boxes) < 3: return boxes
    xc = np.array([(b[0] + b[2]) / 2 for b in boxes])
    mw = float(np.median([b[2] - b[0] for b in boxes]))
    order = np.argsort(xc)
    bays, cur = [], [order[0]]
    for a, b in zip(order[:-1], order[1:]):
        if xc[b] - xc[a] > 0.5 * mw: bays.append(cur); cur = [b]
        else: cur.append(b)
    bays.append(cur)
    rows = {}
    for i, b in enumerate(boxes):
        rows.setdefault(int(((b[1] + b[3]) / 2) // floor_px), []).append(i)
    out = []
    for bg in bays:
        x0 = int(np.median([boxes[i][0] for i in bg])); x1 = int(np.median([boxes[i][2] for i in bg]))
        for r, rg in rows.items():
            y0 = min(boxes[i][1] for i in rg); y1 = max(boxes[i][3] for i in rg)
            out.append((x0, y0, x1, y1))
    return out


def photo_col(k, scale=1, seed=0):
    name, crop, floors, stack = PHOTOS[k]
    im = rgb(os.path.join(PHOTO, name))
    if crop: im = im.crop(crop)
    w, h = W * scale, H * scale
    one = im.resize((w, h // stack), Image.LANCZOS)
    c = np.concatenate([np.asarray(one)] * stack, axis=0)
    if c.shape[0] != h: c = np.asarray(Image.fromarray(c).resize((w, h), Image.LANCZOS))
    # the mask at Classic size, from the Classic-size copy
    one1 = np.asarray(im.resize((W, H // stack), Image.LANCZOS)).astype(np.float32)
    boxes = regularize(find_windows(one1), (H // stack) / float(floors))
    m = np.zeros((H, W, 4), np.uint8)
    rng = np.random.default_rng(1000 + k)
    for s in range(stack):
        oy = s * (H // stack)
        for (x0, y0, x1, y1) in boxes:
            g = int(rng.integers(1, 256))
            m[oy + y0:oy + y1, x0:x1, 0] = 255
            m[oy + y0:oy + y1, x0:x1, 1] = g
            m[oy + y0:oy + y1, x0:x1, 3] = 255
    return c, m, floors * stack, boxes


def parts(scale=1):
    """Column 7: stone grain, aluminium panels, louvres - grey, linear mean
    0.5 each, at `scale` x the atlas size."""
    w, h = W * scale, H * scale
    out = np.zeros((h, w, 3), np.float32)
    def strip(name, x0, x1, src_m, strip_m, contrast):
        src = rgb(os.path.join(SRC, name))
        sw = src.size[0]
        px_per_m = sw / src_m
        need_w = int(round(px_per_m * strip_m[0])); need_h = int(round(px_per_m * strip_m[1]))
        tile = Image.new("RGB", (max(need_w, 1), max(need_h, 1)))
        for ty in range(0, need_h, src.size[1]):
            for tx in range(0, need_w, sw):
                tile.paste(src, (tx, ty))
        g = np.asarray(tile.resize((x1 - x0, h), Image.LANCZOS)).astype(np.float32) / 255
        g = np.where(g <= 0.04045, g / 12.92, ((g + 0.055) / 1.055) ** 2.4).mean(2)
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


def wall_lum(c, m):
    """The wall's own mean linear luminance (the windows left out)."""
    v = c.astype(np.float32) / 255
    v = np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4)
    l = v @ np.array([0.2126, 0.7152, 0.0722])
    keep = m[..., 3] < 128 if m is not None else np.ones(l.shape, bool)
    return float(l[keep].mean()) if keep.any() else float(l.mean())


def main():
    sheet_dir = sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None
    cols, masks, info = [None] * COLS, [None] * COLS, {}
    for k, kind in ((0, "silver"), (1, "blue"), (2, "teal")):
        cols[k], masks[k] = curtain_synth(kind, 1, seed=3 + k)
        info[k] = (kind + " curtain", 8)
    cols[3], masks[3] = photo(os.path.join(CITY, "city_facade_mid.jpg"), os.path.join(CITY, "Night", "city_facade_mid_night.png")); info[3] = ("precast stone", 4)
    cols[4], masks[4] = photo(os.path.join(CITY, "city_facade_tower.jpg"), os.path.join(CITY, "Night", "city_facade_tower_night.png")); info[4] = ("brick", 4)
    for k, kind in ((5, "granite"), (6, "precast")):
        cols[k], masks[k] = office(kind); info[k] = (kind, 8)
    cols[7] = parts(); masks[7] = np.zeros((H, W, 4), np.uint8); info[7] = ("parts", 0)
    boxes = {}
    for k in PHOTOS:
        cols[k], masks[k], fl, boxes[k] = photo_col(k)
        info[k] = (PHOTOS[k][0], fl)
    cols[15], masks[15] = apartment(); info[15] = ("apartment", 8)
    for k in range(COLS):
        print("%2d %-18s floors %d  meanLin %s  wallLum %.3f  glass %.2f" % (
            k, info[k][0], info[k][1], [round(float(v), 3) for v in lin(cols[k])],
            wall_lum(cols[k], masks[k]), (masks[k][..., 3] > 127).mean()))
    atlas = np.concatenate(cols, axis=1)
    mask = np.concatenate(masks, axis=1)
    assert atlas.shape == (H, W * COLS, 3) and mask.shape == (H, W * COLS, 4)
    Image.fromarray(atlas, "RGB").save(os.path.join(OUT, "city_facade_atlas.png"), optimize=True)
    Image.fromarray(mask, "RGBA").save(os.path.join(OUT, "city_facade_atlas_night.png"), optimize=True)
    # THE ENHANCED TWIN (4096 x 512): every column at 2x, cut as the classic
    # is, graded to the classic column's mean (match_mean)
    if all(os.path.exists(os.path.join(ENHDIR, *t)) for t in TWINS.values()):
        e = np.zeros((H * 2, W * 2 * COLS, 3), np.uint8)
        for k in range(COLS):
            x0 = k * W * 2
            if k in (3, 4):
                tw = TWINS["city_facade_mid.jpg" if k == 3 else "city_facade_tower.jpg"]
                e[:, x0:x0 + W * 2] = np.asarray(rgb(os.path.join(ENHDIR, *tw)).resize((W * 2, H * 2), Image.LANCZOS))
            elif k <= 2:
                e[:, x0:x0 + W * 2] = curtain_synth(("silver", "blue", "teal")[k], 2, seed=3 + k)[0]
            elif k in (5, 6):
                e[:, x0:x0 + W * 2] = office(("granite", "precast")[k - 5], 2)[0]
            elif k == 7:
                e[:, x0:x0 + W * 2] = parts(2)
            elif k == 15:
                e[:, x0:x0 + W * 2] = apartment(2)[0]
            else:
                e[:, x0:x0 + W * 2] = photo_col(k, 2)[0]
            if k != 7:
                e[:, x0:x0 + W * 2] = match_mean(e[:, x0:x0 + W * 2], cols[k])
        Image.fromarray(e, "RGB").save(ENH, optimize=True)
        print("enhanced twin", e.shape)
    else:
        print("enhanced twin NOT written - a twin is missing")
    if sheet_dir:
        S = Image.new("RGB", (COLS * W, H * 2 + 16), (20, 20, 20))
        for k in range(COLS):
            t = np.concatenate([np.concatenate([cols[k]] * 2, axis=0)], axis=1)
            im = Image.fromarray(t).convert("RGB")
            ov = Image.new("RGBA", im.size, (0, 0, 0, 0))
            mm = np.concatenate([masks[k]] * 2, axis=0)
            o = np.zeros((H * 2, W, 4), np.uint8)
            o[..., 1] = 255; o[..., 3] = (mm[..., 3] > 127) * 90
            ov = Image.fromarray(o, "RGBA")
            im = Image.alpha_composite(im.convert("RGBA"), ov).convert("RGB")
            S.paste(im, (k * W, 0))
            ImageDraw.Draw(S).text((k * W + 2, H * 2 + 2), "%d %s" % (k, info[k][0][:14]), fill=(255, 255, 0))
        S.save(os.path.join(sheet_dir, "atlas_check.png"))
        Image.fromarray(atlas).resize((W * COLS // 2, H // 2)).save(os.path.join(sheet_dir, "atlas_small.png"))


if __name__ == "__main__":
    main()
