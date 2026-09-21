# Night-window masks for the Charlotte facades (2026-09-21, the NFS-2015 night pass).
#
#   py tools/night/window_masks.py            masks + metas (if missing) + contact sheet
#   py tools/night/window_masks.py --sheet    contact sheet only (reads the masks on disk)
#
# WHAT IT IS FOR. The owner asked for NFS (2015)'s night: "how dark the night is",
# a city you read by its lights. A dark downtown whose every window is the same
# daytime photograph at 8% brightness reads as a car park, not a city. So each
# facade texture gets a MASK that says where its glass is, and PSX/Lit (package
# P2: `_NightMask`, `_NightWin`, gated on `_PSXNight`) lights a random share of
# those windows after dark - warm tungsten, cool fluorescent, shopfronts always.
#
# MASK FORMAT (the contract P2's fragment reads; sRGBTexture OFF, so these are raw):
#   R = A = window coverage, 0 or 255 at the SOURCE resolution, hard-edged.
#           Frames, mullions, meeting rails, sills, AC units and brick are 0.
#           A is the authoritative channel: every term of the emission is
#           multiplied by it. READ COVERAGE FROM A, NEVER R: ConfigureTextureImporters
#           forces alphaIsTransparency on every PNG under Art/, and that
#           importer option dilates colour into A=0 texels (it is there to stop
#           bilinear fringes) - after import R can read 255 in the brick around
#           the glass. G and B are already filled that way on purpose (TRAP 1).
#           (The metas below carry alphaIsTransparency 1 on purpose: it is what
#           the builder would set anyway, so the importer is never "dirty" and
#           no machine imports these differently from another.)
#   G     = per-WINDOW random id (not per pane: a room light lights every pane
#           of its window, so a three-light window with one pane lit reads as a
#           bug). Deterministic: seeded from the texture name, windows ordered
#           top-to-bottom, left-to-right.
#   B     = 255 for shopfront glass (always lit at night), 0 for an office or
#           flat window (lit at random, WIN_LIT_FRAC x night in P2).
#
# TRAP 1 - THE IMPORTER RESAMPLES. ConfigureTextureImporters clamps everything under
# Art/ to 256 px (Mitchell resize, nPOT "ToNearest" first): the 950x1024 mid
# facade and this mask both land at 256x256. Because the mask is written at the
# facade's own source size, Unity resamples BOTH through the same filter and
# their windows stay registered texel for texel. But a resample AVERAGES: a G
# value next to another window's G would come out as a third, random id and
# light one stray texel column. So G and B are not left at 0 outside the glass -
# every texel carries the id/kind of its NEAREST window (distance to the
# window's box), and any blend the resample makes near a window is a blend of
# that window with itself. Walls between windows are 35+ source px wide in the
# three resampled textures (the two drawn 64 px ones are never resampled), so
# the boundary where one window's id meets the next is always in the brick.
# Checked: resized channel by channel with bicubic, lanczos, bilinear and box,
# no glass texel of any mask lands more than 2 off its window's id.
#
# TRAP 2 - GUIDS. The .png.meta files are written here with FIXED GUIDs (design
# R15), never left for Unity to mint: a sandbox-minted GUID dies on the next
# /MIR of the sandbox (the pink-pizza lesson). A meta is written only when it is
# MISSING - Unity owns it after that. The Night/ folder gets its own fixed meta
# for the same reason.
#
# TRAP 3 - TWO OF THE FIVE TEXTURES ARE DRAWN, NOT PHOTOGRAPHED. city_facade_glass
# and city_facade_house come out of PSXRacingBuilder.City.cs GenerateCityTextures
# on every build. Their masks are found from the PNGs by colour, so if that
# drawing code changes, re-run this script (the sheet will show a mismatch).
#
# city_facade_brick is plain brick (no windows): no mask, `_NightWin` 0. The two
# photographed facades with windows (tower, mid) and the shop atlas carry their
# window tables below: the tower is regular enough to find by colour; mid's
# glass is the same muddy grey-brown as its frames (sampled: frame 81,77,72,
# dark glass 83,81,76), so its panes were MEASURED off 3x crops and are written
# down as rectangles; the shops are a composite of five storefronts, also
# measured. The contact sheet is how every one of these was checked: texture |
# mask | overlay | a night preview at the in-game size.
import os
import sys
import zlib
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "Assets", "PSXRacing", "Art", "City")
OUT = os.path.join(ART, "Night")
SHEET = os.path.join(HERE, "window_masks_sheet.png")

# Design R15: fixed GUIDs, one per mask. The folder GUID was minted for this pass.
GUIDS = {
    "city_facade_tower": "aad215abb9184a449b7120045a9c4780",
    "city_facade_mid": "0a93143a3daf41b2b0f6f04762864cdb",
    "city_facade_glass": "325d9709e7e64b49baaf712885e8cf01",
    "city_shops": "91ec4354231c428196dc8ac00f1f2124",
    "city_facade_house": "cd8e0e01e5fe4b6f9b06826bbfb4b038",
}
FOLDER_GUID = "70f2ab25f49d4c14b0fc58d0e35f1a0b"

SOURCES = {
    "city_facade_tower": "city_facade_tower.jpg",
    "city_facade_mid": "city_facade_mid.jpg",
    "city_facade_glass": "city_facade_glass.png",
    "city_shops": "city_shops.png",
    "city_facade_house": "city_facade_house.png",
}

OFFICE, SHOP = 0, 1

# ---------------------------------------------------------------------------
#  Small numpy image helpers (numpy + PIL only, like the other tools here)
# ---------------------------------------------------------------------------
def luma(rgb):
    return 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]


def grow4(m):
    """One 4-neighbour dilation step of a bool image."""
    g = m.copy()
    g[1:, :] |= m[:-1, :]
    g[:-1, :] |= m[1:, :]
    g[:, 1:] |= m[:, :-1]
    g[:, :-1] |= m[:, 1:]
    return g


def flood(seed, passable):
    """Every passable pixel 4-connected to the seed (seed must be passable)."""
    cur = seed & passable
    while True:
        nxt = grow4(cur) & passable
        if (nxt == cur).all():
            return cur
        cur = nxt


def label(mask):
    """4-connected components by min-label propagation. Returns (labels, n);
    labels are 1..n in raster order of each component's first pixel, 0 = off."""
    h, w = mask.shape
    big = h * w + 1
    lab = np.where(mask, np.arange(h * w).reshape(h, w), big)
    while True:
        m = lab.copy()
        m[1:, :] = np.minimum(m[1:, :], lab[:-1, :])
        m[:-1, :] = np.minimum(m[:-1, :], lab[1:, :])
        m[:, 1:] = np.minimum(m[:, 1:], lab[:, :-1])
        m[:, :-1] = np.minimum(m[:, :-1], lab[:, 1:])
        m = np.where(mask, m, big)
        if (m == lab).all():
            break
        lab = m
    ids = np.unique(lab[mask])
    out = np.zeros((h, w), np.int32)
    for k, v in enumerate(ids):
        out[lab == v] = k + 1
    return out, len(ids)


def runs_below(profile, thr, min_len):
    """[start, end) runs where profile < thr, at least min_len long."""
    out, s = [], None
    for i, v in enumerate(profile):
        if v < thr and s is None:
            s = i
        if v >= thr and s is not None:
            if i - s >= min_len:
                out.append((s, i))
            s = None
    if s is not None and len(profile) - s >= min_len:
        out.append((s, len(profile)))
    return out


class Win:
    """One window: the pane pixels it owns and what kind of glass it is."""

    def __init__(self, kind):
        self.kind = kind
        self.rects = []      # [x0, x1) x [y0, y1) pane rectangles, source px

    def box(self):
        xs0 = min(r[0] for r in self.rects)
        xs1 = max(r[1] for r in self.rects)
        ys0 = min(r[2] for r in self.rects)
        ys1 = max(r[3] for r in self.rects)
        return xs0, xs1, ys0, ys1


# ---------------------------------------------------------------------------
#  Per-texture window finders. Each returns (panes bool HxW, window index
#  int HxW with -1 off the glass, [Win]).
# ---------------------------------------------------------------------------
def windows_from_rects(shape, wins, holes=()):
    h, w = shape
    panes = np.zeros(shape, bool)
    widx = np.full(shape, -1, np.int32)
    for i, wn in enumerate(wins):
        for (x0, x1, y0, y1) in wn.rects:
            panes[y0:y1, x0:x1] = True
            widx[y0:y1, x0:x1] = i
    for (x0, x1, y0, y1) in holes:
        panes[y0:y1, x0:x1] = False
        widx[y0:y1, x0:x1] = -1
    return panes, widx, wins


def find_tower(rgb):
    """Red brick with dark-grey three-light windows, a transom bar low in each.
    Regular enough to find by colour: the window BANDS are the rows and
    columns where brick (R well above G) drops below half; every band crossing
    is one window. Inside a window, glass is the light-grey (luma above
    TOWER_GLASS_LUMA) and frame is everything darker that is CONNECTED to the
    window's border - so a dark chair behind the glass is filled back in as
    glass, while a mullion (which runs frame to frame) stays out."""
    TOWER_BRICK_RG = 25       # R-G above this is brick
    TOWER_GLASS_LUMA = 125    # frames sample ~95, glass 150-190, header shadow ~20
    L = luma(rgb)
    brick = (rgb[..., 0] - rgb[..., 1]) > TOWER_BRICK_RG
    rows = runs_below(brick.mean(1), 0.5, 30)
    cols = runs_below(brick.mean(0), 0.5, 30)
    h, w = L.shape
    panes = np.zeros((h, w), bool)
    widx = np.full((h, w), -1, np.int32)
    wins = []
    for (y0, y1) in rows:
        for (x0, x1) in cols:
            sub = L[y0:y1, x0:x1]
            dark = sub < TOWER_GLASS_LUMA
            border = np.zeros_like(dark)
            border[0, :] = border[-1, :] = True
            border[:, 0] = border[:, -1] = True
            frame = flood(border & dark, dark)
            glass = ~frame
            # Each surviving component is one pane, and a pane is a RECTANGLE:
            # a dark chair standing on the sill touches the frame, so the flood
            # carves it out of the glass as a notch (the first sheet showed
            # three). The pane's rows and columns that are at least half as
            # full as its fullest are its rectangle; a lone highlight speck on
            # the sash (under 60 px, a quarter of a game texel's worth x4) is
            # not a pane at all.
            lab, n = label(glass)
            wn = Win(OFFICE)
            for k in range(1, n + 1):
                comp = lab == k
                if comp.sum() < 60:
                    continue
                colf = comp.sum(0)
                rowf = comp.sum(1)
                cx = np.where(colf >= 0.5 * colf.max())[0]
                cy = np.where(rowf >= 0.5 * rowf.max())[0]
                wn.rects.append((x0 + cx.min(), x0 + cx.max() + 1, y0 + cy.min(), y0 + cy.max() + 1))
            if wn.rects:
                wins.append(wn)
    return windows_from_rects((h, w), wins)


# city_facade_mid (950x1024): twelve windows, each a wide picture light between
# two double-hung side lights. Rows 2-4 are dark-brown steel whose frame is
# the same colour as the glass behind it, so these are MEASURED (3x crops with
# a 10 px grid, 2026-09-21), inclusive source px:
#   (top, bottom, (rail top, rail bottom), (L x0, x1), (C x0, x1), (R x0, x1))
# The centre light has no rail; each side light is split by its meeting rail
# (row 1: the white sash's transom).
MID_WINDOWS = [
    (59, 188, (121, 127), (32, 86), (101, 211), (227, 280)),
    (59, 188, (121, 127), (347, 401), (414, 526), (540, 595)),
    (59, 188, (121, 127), (664, 718), (732, 843), (857, 912)),
    (318, 460, (388, 393), (33, 91), (102, 210), (220, 277)),
    (320, 460, (388, 393), (350, 407), (418, 525), (535, 591)),
    (320, 460, (388, 393), (667, 723), (735, 841), (853, 908)),
    (575, 716, (645, 651), (37, 90), (100, 216), (227, 278)),
    (575, 716, (646, 651), (350, 405), (415, 533), (540, 593)),
    (575, 716, (645, 651), (664, 718), (729, 846), (857, 908)),
    (835, 975, (902, 909), (35, 82), (93, 219), (229, 276)),
    (835, 975, (902, 909), (349, 396), (407, 534), (544, 591)),
    (835, 975, (902, 910), (665, 713), (723, 852), (860, 908)),
]
# The two window air-conditioners that sit IN a window (the others hang on the
# brick below a sill and are outside every pane already). Metal boxes glow no
# more than brick does. Inclusive source px: (x0, x1, y0, y1).
MID_AC_UNITS = [
    (30, 94, 417, 464),     # row 2, left window, lower-left sash
    (532, 598, 405, 470),   # row 2, middle window, over the right mullion
]


def incl(x0, x1, y0, y1):
    return (x0, x1 + 1, y0, y1 + 1)


def find_mid(rgb):
    wins = []
    for (top, bot, (r0, r1), (lx0, lx1), (cx0, cx1), (rx0, rx1)) in MID_WINDOWS:
        wn = Win(OFFICE)
        for (x0, x1) in ((lx0, lx1), (rx0, rx1)):
            wn.rects.append(incl(x0, x1, top, r0 - 1))
            wn.rects.append(incl(x0, x1, r1 + 1, bot))
        wn.rects.append(incl(cx0, cx1, top, bot))
        wins.append(wn)
    holes = [incl(*r) for r in MID_AC_UNITS]
    return windows_from_rects(rgb.shape[:2], wins, holes)


# city_shops (1024x256): five storefronts composed side by side by
# ComposeShops. MEASURED like mid, inclusive source px (x0, x1, y0, y1):
#   J&M SNKRS   - one big display window: SHOP (always lit, it is a shop at night)
#   grille shop - glass behind a see-through security grille, the door mullion
#                 and a window AC unit left out: OFFICE (a closed shop that may
#                 or may not have left its lights on - variety along a street
#                 that is otherwise wall-to-wall shop glow)
#   Pampurr'd   - a solid roller shutter: nothing
#   Farmers     - an insurance office's glass front and doors: OFFICE (after hours)
#   last two    - glass doors + windows under the graffiti shutters (the shutters
#                 roll over TRANSOM glass and are not windows): SHOP each
SHOP_FRONTS = [
    (SHOP, [(10, 238, 95, 243)], []),
    (OFFICE, [(281, 360, 67, 243), (374, 487, 67, 243)], [(369, 400, 63, 107)]),
    (OFFICE, [(643, 760, 124, 219)], []),
    (SHOP, [(800, 818, 141, 244), (832, 879, 126, 229)], []),
    (SHOP, [(915, 941, 141, 244), (948, 998, 135, 229)], []),
]


def find_shops(rgb):
    wins, holes = [], []
    for kind, rects, hs in SHOP_FRONTS:
        wn = Win(kind)
        wn.rects = [incl(*r) for r in rects]
        wins.append(wn)
        holes += [incl(*r) for r in hs]
    return windows_from_rects(rgb.shape[:2], wins, holes)


def find_glass(rgb):
    """The drawn curtain wall: 4x4 panes of 14 px in a 2 px mullion grid of one
    exact colour (0x2c3036). Every pane is its own window - an office tower's
    lights go bay by bay."""
    pane = luma(rgb) > 75           # mullion luma 48, the darkest pane ~100
    lab, n = label(pane)
    h, w = pane.shape
    widx = np.full((h, w), -1, np.int32)
    wins = []
    for k in range(1, n + 1):
        ys, xs = np.where(lab == k)
        wn = Win(OFFICE)
        wn.rects.append((xs.min(), xs.max() + 1, ys.min(), ys.max() + 1))
        widx[lab == k] = len(wins)
        wins.append(wn)
    return pane, widx, wins


def find_house(rgb):
    """The drawn house: one sash window of four blue panes inside a white frame
    and cross mullion. The panes are the only blue in it; the four are ONE
    window (one room behind it)."""
    pane = (rgb[..., 2] - rgb[..., 0]) > 25
    wn = Win(OFFICE)
    ys, xs = np.where(pane)
    wn.rects.append((xs.min(), xs.max() + 1, ys.min(), ys.max() + 1))
    widx = np.where(pane, 0, -1).astype(np.int32)
    return pane, widx, [wn]


FINDERS = {
    "city_facade_tower": find_tower,
    "city_facade_mid": find_mid,
    "city_facade_glass": find_glass,
    "city_shops": find_shops,
    "city_facade_house": find_house,
}

# ---------------------------------------------------------------------------
#  Mask assembly
# ---------------------------------------------------------------------------
def window_ids(name, n):
    """One G byte per window, deterministic per texture (crc32 of its name) so a
    re-run writes byte-identical masks. 1..255: 0 is left meaning 'no window'
    for anyone reading the PNG by eye. Drawn WITHOUT replacement: the first
    sheet gave two of the curtain wall's sixteen panes the same byte, and two
    windows that share an id switch on and off together on every building."""
    rng = np.random.default_rng(zlib.crc32(name.encode()))
    return rng.choice(np.arange(1, 256), n, replace=False)


def nearest_window(shape, wins):
    """Index of the nearest window for every pixel, by distance to each
    window's pane box (TRAP 1 in the header)."""
    h, w = shape
    yy, xx = np.mgrid[0:h, 0:w]
    best = np.full(shape, np.inf)
    idx = np.zeros(shape, np.int32)
    for i, wn in enumerate(wins):
        x0, x1, y0, y1 = wn.box()
        dx = np.maximum(np.maximum(x0 - xx, xx - (x1 - 1)), 0)
        dy = np.maximum(np.maximum(y0 - yy, yy - (y1 - 1)), 0)
        d = dx * dx + dy * dy
        closer = d < best
        best[closer] = d[closer]
        idx[closer] = i
    return idx


def build_mask(name, rgb):
    panes, widx, wins = FINDERS[name](rgb)
    ids = window_ids(name, len(wins))
    near = nearest_window(panes.shape, wins)
    own = np.where(widx >= 0, widx, near)
    a = np.where(panes, 255, 0).astype(np.uint8)
    g = ids[own].astype(np.uint8)
    b = np.array([255 if wn.kind == SHOP else 0 for wn in wins], np.uint8)[own]
    return np.dstack([a, g, b, a]), wins


# ---------------------------------------------------------------------------
#  Meta files (TRAP 2). The TextureImporter block of city_facade_tower.jpg.meta
#  with: its own GUID, sRGBTexture 0 (the channels are data), alphaIsTransparency
#  1 (what ConfigureTextureImporters sets on every Art/ PNG, so it never finds
#  this importer dirty), point filter, no mips, 256 px, uncompressed.
# ---------------------------------------------------------------------------
TEMPLATE_META = "city_facade_tower.jpg.meta"


def meta_text(guid):
    with open(os.path.join(ART, TEMPLATE_META), "r", encoding="utf-8", newline="") as f:
        t = f.read()
    t = t.replace("guid: e16a52eac1a06fb48bb2ee33c131ff33", "guid: " + guid, 1)
    t = t.replace("    sRGBTexture: 1\n", "    sRGBTexture: 0\n", 1)
    t = t.replace("  alphaIsTransparency: 0\n", "  alphaIsTransparency: 1\n", 1)
    for must in ("guid: " + guid, "    sRGBTexture: 0\n", "  alphaIsTransparency: 1\n",
                 "    filterMode: 0\n", "    enableMipMap: 0\n",
                 "    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 256\n"):
        if must not in t:
            raise SystemExit("meta template changed shape; missing: " + must.strip())
    return t


FOLDER_META = (
    "fileFormatVersion: 2\n"
    "guid: " + FOLDER_GUID + "\n"
    "folderAsset: yes\n"
    "DefaultImporter:\n"
    "  externalObjects: {}\n"
    "  userData: \n"
    "  assetBundleName: \n"
    "  assetBundleVariant: \n"
)


def write_if_missing(path, text):
    if os.path.exists(path):
        return False
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return True


# ---------------------------------------------------------------------------
#  Contact sheet: texture | mask | overlay | night preview at the in-game size
# ---------------------------------------------------------------------------
def import_size(w, h, cap=256):
    """What Unity makes of a source this size: nPOT ToNearest, then the 256 cap
    (aspect kept)."""
    def pot(n):
        lo = 1 << (n.bit_length() - 1)
        hi = lo << 1
        return lo if n - lo <= hi - n else hi
    pw, ph = pot(w), pot(h)
    s = max(pw, ph) / cap
    if s > 1:
        pw, ph = max(1, int(pw / s)), max(1, int(ph / s))
    return pw, ph


def s2l(x):
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def l2s(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055)


def night_preview(rgb8, mask8, cell=0.37):
    """P2's window emission as specified (PACKAGES.md P2.5) on the texture as
    the game will hold it: both resampled to the import size, a dim bluish
    night light on the wall, then warm / cool / shop windows by the id."""
    h, w = rgb8.shape[:2]
    size = import_size(w, h)
    tex = np.asarray(Image.fromarray(rgb8).resize(size, Image.BICUBIC)).astype(np.float32) / 255
    # Channel by channel, NOT as one RGBA image: PIL premultiplies RGBA by its
    # alpha to resize it, and bicubic ringing on A then drags G off its id by
    # up to 20 at the glass edge. The importer's Mitchell resize is modelled
    # channel by channel (no premultiply); resized that way, G lands on its
    # window's exact id at every glass texel of all five masks (checked
    # 2026-09-21: zero texels off by more than 2).
    m = np.dstack([np.asarray(Image.fromarray(np.ascontiguousarray(mask8[..., c])).resize(size, Image.BICUBIC))
                   for c in range(4)]).astype(np.float32) / 255
    lin = s2l(tex) * np.array([0.10, 0.10, 0.14], np.float32)
    hh = np.mod(m[..., 1] * 7.13 + cell, 1.0)
    lit = np.where(m[..., 2] > 0.5, 1.0, (hh < 0.42).astype(np.float32))
    warm = np.array([1.0, 0.74, 0.42], np.float32)
    cool = np.array([0.70, 0.82, 1.0], np.float32)
    shop = np.array([1.0, 0.86, 0.62], np.float32)
    col = np.where((np.mod(hh * 3.7, 1.0) < 0.62)[..., None], warm, cool)
    col = np.where((m[..., 2] > 0.5)[..., None], shop, col)
    bright = (0.55 + 0.45 * np.mod(hh * 11.3, 1.0)) * 1.1
    emis = col * (bright * lit * np.clip(m[..., 3], 0, 1))[..., None]
    return (l2s(lin + emis) * 255).astype(np.uint8)


def mask_vis(mask8):
    """Windows coloured by id (hue from G), shop glass drawn orange, off = black."""
    a = mask8[..., 3] > 127
    g = mask8[..., 1].astype(np.float32) / 255
    hue = np.dstack([0.5 + 0.5 * np.cos(6.283 * (g + k / 3)) for k in range(3)])
    rgb = 0.35 + 0.65 * hue
    rgb = np.where((mask8[..., 2] > 127)[..., None], np.array([1.0, 0.55, 0.1]), rgb)
    return (np.where(a[..., None], rgb, 0) * 255).astype(np.uint8)


def overlay(rgb8, mask8):
    """The texture at half brightness, glass tinted cyan: anything cyan that is
    not glass, or glass that is not cyan, is a mask error."""
    t = rgb8.astype(np.float32) / 255
    a = (mask8[..., 3] > 127)[..., None]
    o = np.where(a, t * 0.45 + np.array([0.0, 0.55, 0.55]), t * 0.55)
    return (np.clip(o, 0, 1) * 255).astype(np.uint8)


def panel(arr, w, h):
    return Image.fromarray(arr).resize((w, h), Image.NEAREST)


def contact_sheet(items):
    W = 1024
    blocks = []
    for name, rgb8, mask8, wins in items:
        h, w = rgb8.shape[:2]
        imgs =[rgb8, mask_vis(mask8), overlay(rgb8, mask8), night_preview(rgb8, mask8)]
        if w >= 2 * h:          # the shop strip: stacked, full width
            pw, ph = W, int(W * h / w)
            blk = Image.new("RGB", (W, 18 + 4 * (ph + 4)), (24, 24, 28))
            for k, im in enumerate(imgs):
                blk.paste(panel(im, pw, ph), (0, 18 + k * (ph + 4)))
        else:                   # square-ish: four across
            ph = 252
            pw = int(ph * w / h)
            blk = Image.new("RGB", (W, 18 + ph + 4), (24, 24, 28))
            for k, im in enumerate(imgs):
                blk.paste(panel(im, pw, ph), (k * (pw + 4), 18))
        n_shop = sum(1 for wn in wins if wn.kind == SHOP)
        cover = (mask8[..., 3] > 127).mean()
        ImageDraw.Draw(blk).text(
            (4, 3), f"{name}  {w}x{h} -> {'x'.join(map(str, import_size(w, h)))} in game   "
                    f"windows {len(wins)} (shop {n_shop})   glass {cover * 100:.1f}%   "
                    f"| texture | mask (hue = G id, orange = shop) | overlay (cyan = glass) | night",
            fill=(230, 230, 120))
        blocks.append(blk)
    H = sum(b.size[1] for b in blocks) + 4 * len(blocks)
    sheet = Image.new("RGB", (W, H), (0, 0, 0))
    y = 0
    for b in blocks:
        sheet.paste(b, (0, y))
        y += b.size[1] + 4
    sheet.save(SHEET)
    print("sheet", SHEET, sheet.size)


def load_rgb(name):
    return np.asarray(Image.open(os.path.join(ART, SOURCES[name])).convert("RGB"))


def main():
    sheet_only = "--sheet" in sys.argv[1:]
    os.makedirs(OUT, exist_ok=True)
    if write_if_missing(OUT + ".meta", FOLDER_META):
        print("wrote", OUT + ".meta")
    items = []
    for name in SOURCES:
        rgb8 = load_rgb(name)
        out_png = os.path.join(OUT, name + "_night.png")
        mask8, wins = build_mask(name, rgb8.astype(np.int32))
        if sheet_only and os.path.exists(out_png):
            mask8 = np.asarray(Image.open(out_png).convert("RGBA"))
        else:
            Image.fromarray(mask8, "RGBA").save(out_png, optimize=True)
            print(f"{name}: {len(wins)} windows -> {out_png}")
        if write_if_missing(out_png + ".meta", meta_text(GUIDS[name])):
            print("wrote", out_png + ".meta")
        items.append((name, rgb8, mask8, wins))
    contact_sheet(items)


if __name__ == "__main__":
    main()
