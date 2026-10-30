"""guide_atlas.py - Charlotte's freeway guide-sign atlas (the highway signs pass, 2026-10-29).

    py tools/city/guide_atlas.py              write the atlas and its font table (only if their bytes change)
    py tools/city/guide_atlas.py --check      compose in memory; exit 1 if either shipped file differs
    py tools/city/guide_atlas.py --sheet X    also write a 2x preview of the atlas to X

WHAT IT MAKES
  Assets/PSXRacing/Art/City/Signs/CityGuide.png (1024 x 512): every letter,
  figure, arrow, border piece and plain colour the freeway signs are tiled
  from, and the pack metal their structures wear; and
  Assets/PSXRacing/Scripts/City/CityGuideFont.cs: where each piece is and how
  wide each letter sets. CitySigns sets every legend at load from the map's own
  words (charlotte_signs.bytes) as a MOSAIC of quads on the panel's face - a
  letter's cell, the green between the letters, the border's corners and edges
  - none overlapping another, so nothing z-fights, any legend the map holds
  can be set, and the atlas never grows with the city. Every cell sits in a
  field of its own background colour (green round the green set, yellow round
  the yellow), so a mip level that reaches past a cell's edge still reads the
  sign's own colour.

THE FACE: Overpass (Delve Withrington and the Overpass Project Authors, Red Hat;
  SIL Open Font License 1.1, tools/city/fonts/OFL.txt) - the open face drawn
  from the FHWA Standard Alphabets for Highway Signs ("Highway Gothic", the
  alphabets US guide signs were lettered in through the 1990s; NOT Clearview,
  which came after 2004). The OFL lets a sold game embed or rasterise the face;
  only pictures of its letters ship, never the font file. Set at weight 720
  (near Series E(Modified), the heavy series of freeway guide signs) with the
  loose letter spacing of a sign, and supersampled 4x.

COLOURS: MUTCD guide-sign green with white legend and border; warning yellow
  with black (the exit-speed advisory, the LEFT plate).

METAL: the owner's PSX Textures II pack (512 px Color Maps): metal_roofing_pt_1
  (weathered galvanised sheet: the posts and the trusses, and lighter, the
  panels' aluminium backs) and concrete_pt_1 (the footings).
"""
import hashlib
import io
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
FONT = os.path.join(HERE, 'fonts', 'Overpass-Variable.ttf')
PACK = r'C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Textures II v1.6\PSX Textures II\512\Color Maps'
OUT_PNG = os.path.join(REPO, 'Assets', 'PSXRacing', 'Art', 'City', 'Signs', 'CityGuide.png')
OUT_CS = os.path.join(REPO, 'Assets', 'PSXRacing', 'Scripts', 'City', 'CityGuideFont.cs')

W, H = 1024, 512
SS = 4                      # supersampling
CAP = 40                    # a capital's height, atlas px
WEIGHT = 720
TRACK = 0.07                # extra letter spacing, in capitals (a sign sets loose)
GUT = 6                     # gutter round every cell

GREEN = (8, 122, 86)
WHITE = (238, 242, 238)
YELLOW = (244, 196, 18)
BLACK = (22, 22, 24)

GREEN_CHARS = ("ABCDEFGHIJKLMNOPQRSTUVWXYZ" "abcdefghijklmnopqrstuvwxyz" "0123456789" ".,-&'/()"
               "\u00bd\u00bc\u00be")
YELLOW_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-/"


def font_at(px):
    f = ImageFont.truetype(FONT, px)
    try:
        f.set_variation_by_axes([WEIGHT])
    except Exception:
        pass
    return f


def cap_font():
    """The font size (supersampled) whose capital H is CAP*SS tall."""
    ref = font_at(400)
    l, t, r, b = ref.getbbox('H', anchor='ls')
    size = round(400 * CAP * SS / (b - t))
    return font_at(size)


FT = cap_font()


def extents():
    """The row cell: how far above and below the baseline any glyph of the set reaches, px."""
    asc = desc = 0
    for ch in GREEN_CHARS:
        if ch in '\u00bd\u00bc\u00be':
            continue
        l, t, r, b = FT.getbbox(ch, anchor='ls')
        asc = max(asc, -t)
        desc = max(desc, b)
    return int(-(-asc // SS)) + 2, int(-(-desc // SS)) + 2


ASC_PX, DESC_PX = extents()
ROW = ASC_PX + DESC_PX


def advance(ch):
    """A glyph's set width in atlas px (its advance plus the sign's tracking), whole pixels."""
    if ch in '\u00bd\u00bc\u00be':
        return round(CAP * 1.02)
    return max(1, round(FT.getlength(ch) / SS + TRACK * CAP))


def glyph_alpha(ch, w):
    """A glyph's coverage in a w x ROW cell, the pen at the cell's left plus half the tracking."""
    big = Image.new('L', (w * SS, ROW * SS), 0)
    d = ImageDraw.Draw(big)
    base = ASC_PX * SS
    if ch in '\u00bd\u00bc\u00be':
        num, den = {'\u00bd': ('1', '2'), '\u00bc': ('1', '4'), '\u00be': ('3', '4')}[ch]
        small = font_at(round(FT.size * 0.62))
        # numerator high left, denominator low right, a slash between (a sign's stacked fraction)
        d.text((w * SS * 0.06, base - CAP * SS * 0.40), num, font=small, fill=255, anchor='ls')
        d.text((w * SS * 0.52, base), den, font=small, fill=255, anchor='ls')
        sw = CAP * SS * 0.085
        x0, y0, x1, y1 = w * SS * 0.30, base, w * SS * 0.72, base - CAP * SS
        d.polygon([(x0 - sw, y0), (x0 + sw, y0), (x1 + sw, y1), (x1 - sw, y1)], fill=255)
    else:
        d.text((TRACK * CAP * SS * 0.5, base), ch, font=FT, fill=255, anchor='ls')
    return big.resize((w, ROW), Image.Resampling.BOX)


def paint(img, x, y, alpha, fg, bg):
    w, h = alpha.size
    tile = Image.new('RGB', (w, h), bg)
    tile.paste(Image.new('RGB', (w, h), fg), (0, 0), alpha)
    img.paste(tile, (x, y))


def arrow_diag(D):
    """MUTCD's up-diagonal exit arrow, up and to the right, in a D x D cell (white on green)."""
    big = Image.new('L', (D * SS, D * SS), 0)
    d = ImageDraw.Draw(big)
    # an upward arrow in unit coordinates, turned 45 degrees clockwise about the centre
    up = [(0.43, 0.97), (0.43, 0.42), (0.20, 0.45), (0.50, 0.04), (0.80, 0.45), (0.57, 0.42), (0.57, 0.97)]
    import math
    c, s = math.cos(math.radians(45)), math.sin(math.radians(45))
    pts = []
    for (u, v) in up:
        x, y = u - 0.5, v - 0.5
        xr, yr = x * c - y * s, x * s + y * c
        pts.append(((0.5 + xr * 0.92) * D * SS, (0.5 + yr * 0.92) * D * SS))
    d.polygon(pts, fill=255)
    return big.resize((D, D), Image.Resampling.BOX)


def arrow_down(w, h):
    """A down arrow over a lane (white on green)."""
    big = Image.new('L', (w * SS, h * SS), 0)
    d = ImageDraw.Draw(big)
    W2, H2 = w * SS, h * SS
    shaft = 0.30 * W2
    head = 0.45 * H2
    d.rectangle(((W2 - shaft) / 2, 0.03 * H2, (W2 + shaft) / 2, H2 - head + 2), fill=255)
    d.polygon([(0.04 * W2, H2 - head), (0.96 * W2, H2 - head), (W2 / 2, 0.97 * H2)], fill=255)
    return big.resize((w, h), Image.Resampling.BOX)


def ring_corner(T, margin, stroke):
    """The top-left corner of a border: a quarter ring centred on the cell's inner corner."""
    big = Image.new('L', (T * SS, T * SS), 0)
    d = ImageDraw.Draw(big)
    R = (T - margin) * SS
    r = (T - margin - stroke) * SS
    C = T * SS
    d.ellipse((C - R, C - R, C + R, C + R), fill=255)
    d.ellipse((C - r, C - r, C + r, C + r), fill=0)
    return big.resize((T, T), Image.Resampling.BOX)


def ring_edge(T, margin, stroke, L=8):
    """A border's edge, across x (the panel's edge at x = 0), constant along y."""
    big = Image.new('L', (T * SS, L * SS), 0)
    d = ImageDraw.Draw(big)
    d.rectangle((margin * SS, 0, (margin + stroke) * SS - 1, L * SS), fill=255)
    return big.resize((T, L), Image.Resampling.BOX)


def _thick(d, pts, w):
    """A thick polyline with round joints and caps (supersampled coordinates)."""
    d.line(pts, fill=255, width=int(round(w)), joint='curve')
    r = w / 2
    for (px, py) in (pts[0], pts[-1]):
        d.ellipse((px - r, py - r, px + r, py + r), fill=255)


def _head(d, tip, direction, length, width):
    """A triangular arrowhead at tip pointing along direction (unit, supersampled px)."""
    import math
    dx, dy = direction
    n = math.hypot(dx, dy)
    dx, dy = dx / n, dy / n
    bx, by = tip[0] - dx * length, tip[1] - dy * length
    px, py = -dy, dx
    d.polygon([tip, (bx + px * width / 2, by + py * width / 2), (bx - px * width / 2, by - py * width / 2)], fill=255)


def warning_symbol(sym, S):
    """A W1 symbol (black on the diamond), upright, curving to the RIGHT, in an S x S mask."""
    import math
    big = Image.new('L', (S, S), 0)
    d = ImageDraw.Draw(big)
    u = S / 100.0
    w = 11 * u            # the shaft
    hl, hw = 17 * u, 30 * u   # the head
    if sym == 'turn':
        pts = [(44 * u, 80 * u), (44 * u, 42 * u), (60 * u, 42 * u)]
        _thick(d, pts, w)
        _head(d, (79 * u, 42 * u), (1, 0), hl, hw)
    elif sym == 'curve':
        # up, then bending right through 50 degrees on an arc
        pts = []
        cx, cy, r = 78 * u, 64 * u, 34 * u
        pts.append((44 * u, 82 * u))
        for k in range(0, 13):
            a = math.radians(180 - k * 4.2)
            pts.append((cx + r * math.cos(a), cy - r * math.sin(a)))
        _thick(d, pts, w)
        a = math.radians(180 - 12 * 4.2)
        tdir = (math.sin(a), math.cos(a))           # the arc's tangent, going round to the right
        tip = (pts[-1][0] + tdir[0] * hl * 0.85, pts[-1][1] + tdir[1] * hl * 0.85)
        _head(d, tip, tdir, hl, hw)
    elif sym == 'revturn':
        pts = [(36 * u, 84 * u), (36 * u, 58 * u), (62 * u, 58 * u), (62 * u, 36 * u)]
        _thick(d, pts, w)
        _head(d, (62 * u, 16 * u), (0, -1), hl, hw)
    elif sym == 'revcurve':
        pts = []
        for k in range(0, 25):
            t = k / 24.0
            y = 84 - 56 * t
            s = 0.5 - 0.5 * math.cos(math.pi * min(1.0, t * 1.25))
            x = 38 + 24 * s
            pts.append((x * u, y * u))
        _thick(d, pts, w)
        _head(d, (62 * u, 15 * u), (0, -1), hl, hw)
    return big


def warning_diamond(D, sym):
    """A W1 sign whole in a D x D cell: the yellow diamond inscribed (its points at the
    cell's edge midpoints), a black border, the black symbol; black outside the diamond."""
    S = D * SS
    img = Image.new('RGB', (S, S), BLACK)
    d = ImageDraw.Draw(img)
    c = S / 2
    d.polygon([(c, 0), (S, c), (c, S), (0, c)], fill=BLACK)
    t = 0.040 * S                     # the border (1.25 in on a 48 in sign), inset
    k = t * 1.4142
    d.polygon([(c, k), (S - k, c), (c, S - k), (k, c)], fill=YELLOW)
    m = warning_symbol(sym, int(S * 0.58))
    o = int((S - m.size[0]) / 2)
    img.paste(Image.new('RGB', m.size, BLACK), (o, o), m)
    return img.resize((D, D), Image.Resampling.BOX)


def chevron_sign(w, h):
    """MUTCD W1-8, a chevron pointing right: black chevron and border on yellow."""
    W2, H2 = w * SS, h * SS
    img = Image.new('RGB', (W2, H2), YELLOW)
    d = ImageDraw.Draw(img)
    b = 0.06 * W2
    d.rectangle((0, 0, W2 - 1, H2 - 1), fill=BLACK)
    d.rectangle((b, b, W2 - 1 - b, H2 - 1 - b), fill=YELLOW)
    pts = [(0.20, 0.14), (0.47, 0.14), (0.80, 0.50), (0.47, 0.86), (0.20, 0.86), (0.53, 0.50)]
    d.polygon([(x * W2, y * H2) for (x, y) in pts], fill=BLACK)
    return img.resize((w, h), Image.Resampling.BOX)


def pack_metal(name, size, tint, lift=0.0, desat=0.0):
    src = Image.open(os.path.join(PACK, name)).convert('RGB').resize((size, size), Image.Resampling.LANCZOS)
    px = src.load()
    out = Image.new('RGB', (size, size))
    op = out.load()
    for y in range(size):
        for x in range(size):
            r, g, b = px[x, y]
            l = 0.299 * r + 0.587 * g + 0.114 * b
            r, g, b = (c * (1 - desat) + l * desat for c in (r, g, b))
            op[x, y] = tuple(min(255, max(0, round(c * k + lift))) for c, k in zip((r, g, b), tint))
    return out


class Shelf:
    """Cells packed left to right in rows, in a region of the atlas."""
    def __init__(self, x0, y0, x1, y1):
        self.x0, self.y0, self.x1, self.y1 = x0, y0, x1, y1
        self.x, self.y, self.h = x0 + GUT, y0 + GUT, 0

    def place(self, w, h):
        if self.x + w + GUT > self.x1:
            self.x, self.y, self.h = self.x0 + GUT, self.y + self.h + GUT, 0
        if self.y + h + GUT > self.y1:
            raise SystemExit(f'atlas region full placing {w}x{h}')
        at = (self.x, self.y)
        self.x += w + GUT
        self.h = max(self.h, h)
        return at


def compose():
    img = Image.new('RGB', (W, H), GREEN)
    d = ImageDraw.Draw(img)
    cells = {}
    adv = {ch: advance(ch) for ch in GREEN_CHARS}

    # the green set: white letters (the top of the sheet, on green)
    green = Shelf(0, 0, W, 448)
    for ch in GREEN_CHARS:
        x, y = green.place(adv[ch], ROW)
        paint(img, x, y, glyph_alpha(ch, adv[ch]), WHITE, GREEN)
        cells['g' + ch] = (x, y, adv[ch], ROW)
    # the green pieces: arrows, the border, plain green and white
    pieces = green
    D = 96
    x, y = pieces.place(D, D); paint(img, x, y, arrow_diag(D), WHITE, GREEN); cells['ArrowDiag'] = (x, y, D, D)
    aw, ah = 56, 96
    x, y = pieces.place(aw, ah); paint(img, x, y, arrow_down(aw, ah), WHITE, GREEN); cells['ArrowDown'] = (x, y, aw, ah)
    T, m, s = 48, 4, 8          # 0.30 m at 160 px/m: a 0.025 m margin, a 0.05 m white border
    x, y = pieces.place(T, T); paint(img, x, y, ring_corner(T, m, s), WHITE, GREEN); cells['RingGCorner'] = (x, y, T, T)
    x, y = pieces.place(T, 8); paint(img, x, y, ring_edge(T, m, s), WHITE, GREEN); cells['RingGEdge'] = (x, y, T, 8)
    x, y = pieces.place(24, 24); cells['SolidGreen'] = (x, y, 24, 24)
    x, y = pieces.place(24, 24); d.rectangle((x - 2, y - 2, x + 25, y + 25), fill=WHITE); cells['SolidWhite'] = (x, y, 24, 24)
    # (signs round 2) the curve warnings' faces, whole: MUTCD W1-1 Turn, W1-2
    # Curve, W1-3 Reverse Turn, W1-4 Reverse Curve, each a yellow diamond with
    # its black border and symbol inscribed in the cell (the mesh's quad takes
    # the diamond's four points; flipped for a curve to the left). The cell's
    # corners and a frame in its gutter are black, the border's colour, so a
    # far mip of a diamond's tip reads black and not the green round it.
    for key, sym in (('W1Turn', 'turn'), ('W1Curve', 'curve'), ('W1RevTurn', 'revturn'), ('W1RevCurve', 'revcurve')):
        x, y = pieces.place(D, D)
        d.rectangle((x - 3, y - 3, x + D + 2, y + D + 2), fill=BLACK)
        img.paste(warning_diamond(D, sym), (x, y))
        cells[key] = (x, y, D, D)

    # the yellow field: black letters, the yellow border, plain yellow and black
    y0 = green.y + green.h + GUT
    yf = (0, y0, W, 448)
    d.rectangle((yf[0], yf[1], yf[2] - 1, yf[3] - 1), fill=YELLOW)
    ys = Shelf(*yf)
    for ch in YELLOW_CHARS:
        x, y = ys.place(adv[ch], ROW)
        paint(img, x, y, glyph_alpha(ch, adv[ch]), BLACK, YELLOW)
        cells['y' + ch] = (x, y, adv[ch], ROW)
    T2, m2, s2 = 24, 2, 4
    x, y = ys.place(T2, T2); paint(img, x, y, ring_corner(T2, m2, s2), BLACK, YELLOW); cells['RingYCorner'] = (x, y, T2, T2)
    x, y = ys.place(T2, 8); paint(img, x, y, ring_edge(T2, m2, s2), BLACK, YELLOW); cells['RingYEdge'] = (x, y, T2, 8)
    x, y = ys.place(16, 16); cells['SolidYellow'] = (x, y, 16, 16)
    x, y = ys.place(16, 16); d.rectangle((x - 2, y - 2, x + 17, y + 17), fill=BLACK); cells['SolidBlack'] = (x, y, 16, 16)
    # (signs round 2) the EXIT ONLY panel's down arrow (black on yellow, one
    # over each dropped lane: MUTCD E11-1), and the W1-8 Chevron Alignment
    # sign whole (24 x 30 in: a black chevron and border on yellow), on the
    # yellow field so their mips stay yellow
    x, y = ys.place(34, ROW); paint(img, x, y, arrow_down(34, ROW), BLACK, YELLOW); cells['ArrowDownBlack'] = (x, y, 34, ROW)
    x, y = ys.place(48, ROW); img.paste(chevron_sign(48, ROW), (x, y)); cells['Chevron'] = (x, y, 48, ROW)

    # the metal strip (the bottom): the owner's pack
    galv = pack_metal('metal_roofing_pt_1.png', 64, (1.02, 1.02, 1.0), lift=38, desat=0.55)
    alum = pack_metal('metal_roofing_pt_1.png', 64, (0.92, 0.93, 0.94), lift=78, desat=0.8)
    conc = pack_metal('concrete_pt_1.png', 64, (1.0, 1.0, 1.0), lift=24, desat=0.3)
    mx = 0
    for key, tile in (('Galv', galv), ('Alum', alum), ('Concrete', conc)):
        img.paste(tile, (mx, 448)); cells[key] = (mx, 448, 64, 64)
        mx += 64
    # the rest of the strip: galvanised, so a far mip of a truss reads steel
    for x in range(mx, W, 64):
        img.paste(galv, (x, 448))
    return img, cells, adv


def cs_table(cells, adv):
    def r(key):
        x, y, w, h = cells[key]
        return f'new RectInt({x}, {y}, {w}, {h})'
    gx = ', '.join(f'{cells["g" + c][0]}, {cells["g" + c][1]}' for c in GREEN_CHARS)
    yx = ', '.join(f'{cells["y" + c][0]}, {cells["y" + c][1]}' for c in YELLOW_CHARS)
    aw = ', '.join(str(adv[c]) for c in GREEN_CHARS)
    esc = lambda s: ''.join(c if c.isascii() and c not in '\\"' else ('\\\\' if c == '\\' else '\\"' if c == '"' else f'\\u{ord(c):04x}') for c in s)
    space = round(FT.getlength(' ') / SS + TRACK * CAP)
    pieces = '\n'.join(f'        public static readonly RectInt {k} = {r(k)};' for k in (
        'ArrowDiag', 'ArrowDown', 'RingGCorner', 'RingGEdge', 'SolidGreen', 'SolidWhite',
        'RingYCorner', 'RingYEdge', 'SolidYellow', 'SolidBlack', 'Galv', 'Alum', 'Concrete',
        'W1Turn', 'W1Curve', 'W1RevTurn', 'W1RevCurve', 'ArrowDownBlack', 'Chevron'))
    return f'''// GENERATED by tools/city/guide_atlas.py - do not edit by hand (run it again).
using UnityEngine;

namespace PSXRacing.City
{{
    /// <summary>
    /// Where the freeway guide signs' pieces are on their atlas
    /// (Art/City/Signs/CityGuide.png) and how wide each letter sets: the
    /// face is Overpass (SIL Open Font License 1.1), the open Highway Gothic,
    /// at weight {WEIGHT}, a capital {CAP} px tall. A glyph's cell is its set
    /// width by <see cref="RowPx"/>, the baseline <see cref="AscPx"/> below
    /// its top; rectangles in pixels from the atlas's top left.
    /// </summary>
    public static class CityGuideFont
    {{
        public const int AtlasW = {W}, AtlasH = {H};
        public const int CapPx = {CAP}, AscPx = {ASC_PX}, DescPx = {DESC_PX}, RowPx = {ROW};
        /// <summary>A word space's set width, px.</summary>
        public const int SpacePx = {space};
        /// <summary>The border pieces: a green panel's ring is {48} px across
        /// (a {4} px green margin, an {8} px white border), a yellow sign's {24}
        /// ({2}, {4} black).</summary>
        public const int RingGPx = 48, RingYPx = 24;
        public const string Green = "{esc(GREEN_CHARS)}";
        public const string Yellow = "{esc(YELLOW_CHARS)}";
        /// <summary>Each green-set glyph's set width, px.</summary>
        public static readonly byte[] Adv = {{ {aw} }};
        /// <summary>Each glyph's cell, x and y (the width is its <see cref="Adv"/>).</summary>
        public static readonly short[] GreenAt = {{ {gx} }};
        public static readonly short[] YellowAt = {{ {yx} }};
{pieces}
    }}
}}
'''


def main():
    args = sys.argv[1:]
    img, cells, adv = compose()
    buf = io.BytesIO()
    img.save(buf, 'PNG', optimize=True)
    png = buf.getvalue()
    cs = cs_table(cells, adv).replace('\n', '\r\n').encode('utf-8')
    if '--check' in args:
        same = os.path.exists(OUT_PNG) and open(OUT_PNG, 'rb').read() == png and os.path.exists(OUT_CS) and open(OUT_CS, 'rb').read() == cs
        print('GUIDE ATLAS CHECK OK' if same else 'GUIDE ATLAS CHECK: DIFFERS')
        sys.exit(0 if same else 1)
    for path, data in ((OUT_PNG, png), (OUT_CS, cs)):
        if os.path.exists(path) and open(path, 'rb').read() == data:
            print('unchanged ' + path)
            continue
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, 'wb').write(data)
        print('wrote ' + path)
    print(f'cap {CAP} px, row {ROW} px (asc {ASC_PX}, desc {DESC_PX}); png {len(png) / 1e3:.0f} KB, sha {hashlib.sha256(png).hexdigest()[:12]}')
    if '--sheet' in args:
        out = args[args.index('--sheet') + 1]
        img.resize((W * 2, H * 2), Image.Resampling.NEAREST).save(out)
        print('sheet ' + out)


if __name__ == '__main__':
    main()
