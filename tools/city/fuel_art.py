"""fuel_art.py - the gas-station pack's art made brand-free (city fuel r1, 2026-10-30).

    py tools/city/fuel_art.py            write the textures (only those whose bytes change)
    py tools/city/fuel_art.py --check    compose in memory, exit 1 if a shipped texture differs
    py tools/city/fuel_art.py --sheet X  also write a before/after sheet to X (keep it out of the
                                         repository: its left half is the pack's branded art)
    py tools/city/fuel_art.py --sheet-after X   the shipped textures alone (Docs/shots/city_fuel)

The owner will sell the game (no brands, no trademarks). The Gas_station pack's
own store is "6twelve" - a parody of a real chain's name and logo - and its
price board names a real oil company's grades ("Magna", "Premium") at peso
prices. Its shelves, fridges and stock room are photographs of real products:
legible soft-drink, beer, candy, cereal, snack and noodle brands. This script
rebuilds every one of those textures from the pack's ORIGINALS (read from the
owner's art folder, PSX_ART_DIR, never written) into Assets/PSXRacing/Art/
GasStation/Textures, under the same file names, so every material, the
LifeSim's town station, the circuits' fuel stops and the city's stations all
change with it.

THE STORE: "PUMPWREN" - a made-up name in the owner's own pattern (Curbkite
Shoes, Pawpixel Pet Salon, Brickmoon Insurance) - and plain words: FOOD MART,
SELF SERVE, REGULAR 87. No logo, no phone number. The faces are the pack's own
material: its striped fascia band (Sign.jpg), its dark sign metal, a lightbox
white; the lettering is the Overpass face (SIL OFL 1.1, tools/city/fonts, the
highway signs' face; rasterised here, the font file never ships).

  * 6twelve.jpg            the shop's fascia badge: PUMP / WREN, FOOD MART, the stripes
  * 6twelve_Sign.png       the price pylon: its two cabinet faces (both now carry the
                           face; the pack left the back blank metal), a 1990s
                           internally lit cabinet - the name, FOOD MART, the price box
                           (REGULAR 87, and the price in FLIP NUMERALS, .99: the
                           game's one grade at FuelModel.PricePerGallon 0.99),
                           SELF SERVE. No LEDs (the owner's 90s rule): the pack's
                           seven-segment price displays are gone. Posts, sides and top
                           keep the pack's metal.
  * 6twelve_Sign_Emissor.png  what of the pylon glows after dark: the lightbox panels,
                           a fluorescent white-green, and the numerals.
  * the shelves, fridges, stock room and litter (Foods_01-04, Foods_07,
    Sweets_others, Refrigerador, _01, _02, Deposit, Others, Trash): every region
    that shows a product's label is re-printed SOFT - box-filtered to 1/k and
    eased back up - so the colours, the shapes of the packs and bottles and the
    light stay, and no letter or logo survives. The rest of each picture (the
    fruit, the pizzas, the fridge doors' frames) is untouched. The game ships
    these at 256 px (WebGL), where the labels were already smudged; the
    originals in the project are now clean at any size.

The file names keep "6twelve" because the pack's FBX binds its materials to
them; nothing a player sees carries the name.
"""
import io
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
ART = os.environ.get('PSX_ART_DIR', r'C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing')
PACK = os.path.join(ART, 'Gas_station', 'Gas_station', 'Textures')
OUT = os.path.join(REPO, 'Assets', 'PSXRacing', 'Art', 'GasStation', 'Textures')
FONT = os.path.join(HERE, 'fonts', 'Overpass-Variable.ttf')

NAME_LINES = ('PUMP', 'WREN')
PRICE = '.99'          # FuelModel.PricePerGallon (0.99 a gallon, 87 REG)

NAVY = (24, 42, 96)
WHITE = (238, 238, 232)
INK = (26, 26, 30)
TILE = (34, 34, 38)


def pack(name):
    p = os.path.join(PACK, name)
    if not os.path.exists(p):
        sys.exit('missing pack file (set PSX_ART_DIR): ' + p)
    return Image.open(p)


def font(px, weight='Black'):
    f = ImageFont.truetype(FONT, px)
    try:
        f.set_variation_by_name(weight)
    except Exception:
        pass
    return f


def text_fit(d, box, text, fill, weight='Black', max_px=200):
    """Letter `text` centred in box (x0, y0, x1, y1), as big as fits (never clipped)."""
    x0, y0, x1, y1 = box
    px = max_px
    while px > 6:
        f = font(px, weight)
        l, t, r, b = d.textbbox((0, 0), text, font=f)
        if r - l <= x1 - x0 and b - t <= y1 - y0:
            break
        px -= 1
    l, t, r, b = d.textbbox((0, 0), text, font=f)
    d.text((x0 + (x1 - x0 - (r - l)) / 2 - l, y0 + (y1 - y0 - (b - t)) / 2 - t), text, fill=fill, font=f)


def stripes(w, h):
    """The pack's own fascia stripes (Sign.jpg: blue, magenta, teal bands), along the band."""
    return pack('Sign.jpg').convert('RGB').resize((w, h), Image.LANCZOS)


def metal_tint(im, rgb):
    """The pack's dark sign metal (its own pylon's), tinted: a ground with grain."""
    g = im.convert('L')
    vals = g.tobytes()
    mean = max(1.0, sum(vals) / len(vals))
    lut = [tuple(min(255, round(c * (0.86 + 0.14 * v / mean))) for c in rgb) for v in range(256)]
    return Image.frombytes('RGB', im.size, b''.join(bytes(lut[v]) for v in vals))


SC = 4   # the faces are drawn at 4x and box-filtered down: crisp at the texture's own size


def pylon_face(w=70, h=168):
    """One cabinet face, 1990s internally lit: name, FOOD MART, the price, SELF SERVE."""
    W, H = w * SC, h * SC
    base = pack('6twelve_Sign.png').convert('RGB')
    grain = base.crop((9, 88, 79, 256)).resize((W, H), Image.NEAREST)   # its own blank back face: the metal
    im = metal_tint(grain, (44, 46, 52))
    d = ImageDraw.Draw(im)
    y = lambda v: v * SC
    im.paste(stripes(W, y(9)), (0, 0))
    # the name panel: lightbox white, navy letters
    d.rectangle([y(2), y(11), W - y(2), y(59)], fill=WHITE)
    text_fit(d, (y(5), y(13), W - y(5), y(34)), NAME_LINES[0], NAVY)
    text_fit(d, (y(5), y(36), W - y(5), y(57)), NAME_LINES[1], NAVY)
    # FOOD MART, white on navy
    d.rectangle([y(2), y(62), W - y(2), y(77)], fill=NAVY)
    text_fit(d, (y(5), y(64), W - y(5), y(75)), 'FOOD MART', WHITE, 'Bold')
    # the price box: REGULAR 87 over flip numerals
    d.rectangle([y(2), y(80), W - y(2), y(149)], fill=WHITE)
    text_fit(d, (y(5), y(82), W - y(5), y(94)), 'REGULAR 87', NAVY, 'Bold')
    tiles = [(c, 9 if c == '.' else 23) for c in PRICE]
    tw = sum(t[1] for t in tiles) + 2 * (len(tiles) - 1)
    x = (w - tw) / 2
    for c, tw1 in tiles:
        x0, x1 = y(x), y(x + tw1)
        d.rectangle([x0, y(97), x1, y(146)], fill=TILE)
        text_fit(d, (x0 + y(1), y(99), x1 - y(1), y(144)), c, WHITE, 'Bold')
        d.line([x0, y(121.5), x1, y(121.5)], fill=(10, 10, 12), width=SC)   # the flap's hinge line
        x += tw1 + 2
    # SELF SERVE, white on navy
    d.rectangle([y(2), y(152), W - y(2), y(165)], fill=NAVY)
    text_fit(d, (y(5), y(154), W - y(5), y(163)), 'SELF SERVE', WHITE, 'Bold')
    return im.resize((w, h), Image.BOX)


def fluorescent(rgb):
    """A lit panel's colour under fluorescent tubes: white-green (the 90s rule, hue ~78, sat ~0.2)."""
    r, g, b = rgb
    return (round(r * 0.90), round(g * 1.0), round(b * 0.86))


def compose_sign():
    base = pack('6twelve_Sign.png').convert('RGBA')
    face = pylon_face()
    out = base.copy()
    # the front face (u runs to the viewer's right) and the back face (u runs to the
    # viewer's left: the face is laid mirrored there, so it reads from behind too)
    out.paste(face.convert('RGBA'), (95, 88))
    out.paste(face.transpose(Image.FLIP_LEFT_RIGHT).convert('RGBA'), (9, 88))
    # the glow: the lightbox panels and the numerals; the metal, the frames and the tiles dark
    em = Image.new('RGBA', base.size, (0, 0, 0, 255))
    lit = face.copy()
    px = lit.load()
    for yy in range(lit.size[1]):
        for xx in range(lit.size[0]):
            r, g, b = px[xx, yy]
            lum = (r * 3 + g * 6 + b) / 10
            px[xx, yy] = fluorescent((r, g, b)) if lum > 120 or b > 80 else (0, 0, 0)
    em.paste(lit.convert('RGBA'), (95, 88))
    em.paste(lit.transpose(Image.FLIP_LEFT_RIGHT).convert('RGBA'), (9, 88))
    return out, em


def compose_badge():
    """The shop's fascia badge (6twelve.jpg; the face shows its 31..225 square)."""
    S = 256 * SC
    im = metal_tint(pack('Metal.jpg').convert('RGB').resize((S, S)), NAVY)   # the pack's metal, painted navy
    d = ImageDraw.Draw(im)
    y = lambda v: v * SC
    # everything inside the 31..225 square the shop's face shows (and signs_atlas.py crops)
    text_fit(d, (y(44), y(42), y(212), y(92)), NAME_LINES[0], WHITE)
    text_fit(d, (y(44), y(98), y(212), y(148)), NAME_LINES[1], WHITE)
    d.rectangle([0, y(156), S, y(163)], fill=WHITE)
    im.paste(stripes(S, y(24)), (0, y(163)))
    text_fit(d, (y(48), y(192), y(208), y(216)), 'FOOD MART', WHITE, 'Bold')
    return im.resize((256, 256), Image.BOX)


# the regions of each photograph that show a product's label: (x0, y0, x1, y1, k)
SCRUB = {
    'Foods_01.jpg': [(0, 0, 1024, 512, 10), (0, 512, 512, 770, 10)],             # soft drinks, cereal (the fruit stays)
    'Foods_02.jpg': [(0, 0, 1024, 1024, 10)],                                     # snack bags, soft drinks, a cookie box
    'Foods_03.jpg': [(0, 0, 1024, 1024, 10)],                                     # cookies, cakes, candy bars
    'Foods_04.jpg': [(0, 0, 1024, 528, 10), (0, 540, 208, 735, 10), (0, 700, 745, 1004, 10)],   # cakes, noodles, a cup, milk (the pizzas stay)
    'Foods_07.jpg': [(0, 505, 1024, 590, 8), (630, 505, 1024, 800, 8), (0, 795, 1024, 1024, 8)],  # cans, rice, bread, water, sauces, oils
    'Sweets_others.jpg': [(0, 0, 994, 642, 9)],                                   # candy, gum, cigarettes, accessories
    'Refrigerador.jpg': [(35, 165, 428, 990, 10)],                                # the fridges' shelves: soft drinks,
    'Refrigerador_01.jpg': [(35, 165, 428, 990, 10)],                             # beer, sports and energy drinks
    'Refrigerador_02.jpg': [(35, 165, 428, 990, 10)],                             # (the doors' frames stay)
    'Deposit.jpg': [(0, 0, 784, 824, 9)],                                         # the stock room's printed cartons and crates
    'Others.png': [(0, 38, 92, 142, 6), (118, 44, 186, 134, 6), (118, 128, 212, 256, 6)],   # sachets, packets, a printed bag
    'Trash.png': [(0, 28, 340, 150, 5)],                                          # printed wrappers and cartons in the litter
    # the time clock's maker on its dial and its label (its "Checker" object is stripped by
    # SpawnStation and never ships; cleaned all the same)
    'Checker.png': [(86, 82, 136, 97, 4), (84, 184, 150, 212, 5), (152, 116, 202, 128, 3)],
}


def scrub(name):
    im = pack(name)
    mode = im.mode
    im = im.convert('RGBA')
    for (x0, y0, x1, y1, k) in SCRUB[name]:
        x1, y1 = min(x1, im.size[0]), min(y1, im.size[1])
        reg = im.crop((x0, y0, x1, y1))
        w, h = reg.size
        small = reg.resize((max(1, w // k), max(1, h // k)), Image.BOX)
        soft = small.resize((w, h), Image.BICUBIC)
        # keep the photograph's alpha (cut-out stickers on a transparent sheet stay cut out)
        soft.putalpha(reg.getchannel('A'))
        im.paste(soft, (x0, y0))
    return im if mode == 'RGBA' else im.convert('RGB')


def encode(im, name):
    bio = io.BytesIO()
    if name.lower().endswith('.jpg'):
        im.convert('RGB').save(bio, 'JPEG', quality=92, optimize=False, subsampling=0)
    else:
        im.save(bio, 'PNG', optimize=False)
    return bio.getvalue()


def compose():
    sign, em = compose_sign()
    out = {'6twelve_Sign.png': sign, '6twelve_Sign_Emissor.png': em, '6twelve.jpg': compose_badge()}
    for name in SCRUB:
        out[name] = scrub(name)
    return out


def sheet(images, path, before_too=True):
    """Each texture scaled to 256 px tall: the pack's original (left) beside the shipped one,
    or (before_too=False: for the repository, which must not carry the brands) the shipped only."""
    rows = []
    for name, im in images.items():
        before = pack(name).convert('RGB')
        s = 256 / before.size[1]
        b = before.resize((max(1, round(before.size[0] * s)), 256), Image.LANCZOS)
        a = im.convert('RGB').resize(b.size, Image.LANCZOS)
        row = Image.new('RGB', (b.size[0] * (2 if before_too else 1) + 12, 276), (255, 255, 255))
        if before_too:
            row.paste(b, (0, 20)); row.paste(a, (b.size[0] + 12, 20))
        else:
            row.paste(a, (0, 20))
        ImageDraw.Draw(row).text((4, 2), name, fill=(0, 0, 0), font=font(14, 'Bold'))
        rows.append(row)
    W = max(r.size[0] for r in rows)
    cols = 2
    widths = [max(rows[i].size[0] for i in range(c, len(rows), cols)) for c in range(cols)]
    H = 276 * ((len(rows) + cols - 1) // cols)
    out = Image.new('RGB', (sum(widths) + 20, H), (255, 255, 255))
    for i, r in enumerate(rows):
        out.paste(r, (sum(widths[:i % cols]) + 20 * (i % cols), 276 * (i // cols)))
    out.save(path)
    print('wrote', path, out.size)


def main():
    check = '--check' in sys.argv
    images = compose()
    bad = 0
    for name, im in images.items():
        data = encode(im, name)
        path = os.path.join(OUT, name)
        have = open(path, 'rb').read() if os.path.exists(path) else None
        if check:
            if have != data:
                print('DIFFERS', path); bad += 1
            continue
        if have == data:
            print('same', path); continue
        open(path, 'wb').write(data)
        print('wrote', path, len(data), 'bytes')
    if '--sheet' in sys.argv:
        sheet(images, sys.argv[sys.argv.index('--sheet') + 1])
    if '--sheet-after' in sys.argv:
        sheet(images, sys.argv[sys.argv.index('--sheet-after') + 1], before_too=False)
    if check:
        print('FUEL ART ' + ('OK' if bad == 0 else 'DIFFERS'))
        sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
