"""signs_atlas.py - compose Charlotte's sign-face atlas (plan WP-23).

    py tools/city/signs_atlas.py            write the two PNGs (only if their bytes change)
    py tools/city/signs_atlas.py --check    compose in memory, exit 1 if the shipped PNGs differ
    py tools/city/signs_atlas.py --sheet X  also write a 2x preview sheet to X

Writes Assets/PSXRacing/Art/City/Signs/CitySigns.png (512 x 512, opaque: it
ships as RGB565) and CitySigns_night.png (128 x 128, the night mask PSX/Lit's
_NightMask reads: R = A = how brightly the texel glows after dark).

EVERY FACE IS MADE OF THE OWNER'S PACK PICTURES (owner rule; plan Q6's default,
"pack faces only"), and every brand is FICTIONAL (the owner: no real brands or
trademarks). Nothing is lettered in code: the only words on the faces are the
ones the pack pictures carry.
  * food: BurgerPiz `menu_burger.png` (burgers, fries), the All pack's
    `Foods_04.jpg` (a pepperoni pizza) and `Foods.jpg` (bananas, a watermelon);
    the game's own STACK BURGER and SLICE HOUSE are its burger on red and its
    pizza on green;
  * the Gas_station pack's own fictional brand, `6twelve` (logo and price
    board), and its striped `Sign.jpg`; the Pizzeria pack's `sign.png` (PIZZA);
  * tin signs from the Pizzeria pack's `Decorative_Sign.png`, chosen after
    LOOKING at every one of its 36: "Burgers - Best in Town", the two "Eat Good
    Food", the dancing couple, and the NORTH CAROLINA licence plate (a made-up
    number). NOT used: the two CAMEL signs (a tobacco trademark), the
    Harley-Davidson bar-and-shield signs, the ones with a contour cola bottle,
    HOLLYWOOD GASOLINE (it may name a real brand of the past) and the
    fries-and-burger sign that
    reads like a burger chain's name;
  * for the trades with no shop sign in the packs, a picture of what they sell:
    a palm beach (the All pack's `Paintings.jpg`) for a motel, a gold bar (the
    PSX Mega Pack's `gold_bar_mp_1.png`) for a bank, the NEUROZAM-9 pill
    bottle's prescription label (its own pack, a made-up drug) for a pharmacy,
    a car wheel (the boggle vehicles pack, `2_door_coupe`) for tyres and car
    lots and a car wash, the PSX Textures pack's `water_1.png` for a car wash, a
    shirt (the All pack's `Clothing.png`; the blue one with a red pocket tab
    left out) and an old television (`Electronics.jpg`; the flat sets with a
    maker's name left out) for a strip mall's tenants; the coast and the
    desert mountains of `Paintings.jpg` for a tourism board;
  * the grounds are pack metal (the lamp posts' LifeSim House `Metal.jpg`,
    WP-07) tinted to the sign's colour, as the guardrails tint theirs; the same
    metal, dark and weathered, for poles and billboard backs, lighter and
    galvanised for gantry trusses.
NOT used although the plan listed them: the Buildings pack's `Shops_01-31`,
which are photographs of real storefronts with real business names and phone
numbers.

Drawn in code (plan Q7, "the pattern is the design"): only the gantry panels
(green, white border, abstract white bars and arrows - no text).

THE LAYOUT IS SHARED WITH Scripts/City/CitySigns.cs (the Atlas table there):
pixel rectangles from the TOP-LEFT, every edge a multiple of 4 so the 128 px
night mask is in register.
"""
import sys, io, os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
ART = os.environ.get('PSX_ART_DIR', r'C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing')
ART_ROOT = os.path.dirname(ART)     # the owner's whole art folder (the packs beside PSX Racing)
OUT_DIR = os.path.join(REPO, 'Assets', 'PSXRacing', 'Art', 'City', 'Signs')
ATLAS = os.path.join(OUT_DIR, 'CitySigns.png')
MASK = os.path.join(OUT_DIR, 'CitySigns_night.png')
METAL = os.path.join(REPO, 'Assets', 'PSXRacing', 'Art', 'LifeSim', 'House', 'Textures', 'Metal.jpg')

N = 512
# ---- the layout (x, y, w, h), top-left origin; CitySigns.cs mirrors it ----
BULLETIN = [(256 * (i % 2), 76 * (i // 2), 256, 76) for i in range(6)]      # 14 x 48 ft faces
POSTER = [(128 * i, 228, 128, 64) for i in range(4)]                         # 12 x 24 ft faces
POLE = [(64 * (i % 8), 292 + 64 * (i // 8), 64, 64) for i in range(16)]      # business cabinets
GANTRY = [(0, 420, 128, 64), (128, 420, 128, 64)]                            # through, exit
METAL_DARK = (256, 420, 128, 92)
METAL_LIGHT = (384, 420, 128, 92)
LENS = (0, 484, 16, 16)       # a floodlight's lens
BLACK = (16, 484, 16, 16)     # cabinet sides
WHITE = (32, 484, 16, 16)

# what each business cabinet shows (CitySigns.KindCells picks them by trade)
POLE_CELLS = ['6twelve', '6twelve prices', 'STACK BURGER', 'BURGERS tin', 'SLICE HOUSE', 'PIZZA banner', 'EAT GOOD FOOD tin',
              'motel (palm beach)', 'bank (gold bar)', 'pharmacy (Rx label)', 'car wash (wheel, water)', 'car lot (NC plate, wheels)',
              'tyres', 'strip mall tenants', 'grocer (produce)', 'lounge (dancers tin)']


def pack(rel, root=None):
    p = os.path.join(root or ART, rel)
    if not os.path.exists(p):
        sys.exit('missing pack file (set PSX_ART_DIR): ' + p)
    return Image.open(p).convert('RGBA')


def paste_fit(dst, img, box, mode='contain'):
    """Scale img into box (contain, cover or stretch), centred; alpha composited."""
    x, y, w, h = box
    sw, sh = img.size
    if mode == 'stretch':
        nw, nh = w, h
    else:
        s = min(w / sw, h / sh) if mode == 'contain' else max(w / sw, h / sh)
        nw, nh = max(1, round(sw * s)), max(1, round(sh * s))
    im = img.resize((nw, nh), Image.LANCZOS)
    if mode == 'cover':
        l, t = (nw - w) // 2, (nh - h) // 2
        im = im.crop((l, t, l + w, t + h)); nw, nh = w, h
    layer = Image.new('RGBA', dst.size, (0, 0, 0, 0))
    layer.paste(im, (x + (w - nw) // 2, y + (h - nh) // 2))
    dst.alpha_composite(layer)


def key_to_alpha(img, test):
    """Clear the pixels a pack picture's backdrop is made of (white, black, grey)."""
    px = img.load()
    for yy in range(img.size[1]):
        for xx in range(img.size[0]):
            r, g, b, a = px[xx, yy]
            if test(r, g, b):
                px[xx, yy] = (r, g, b, 0)
    return img


def white_to_alpha(img, thr=236):
    return key_to_alpha(img, lambda r, g, b: r > thr and g > thr and b > thr)


def black_to_alpha(img, thr=18):
    return key_to_alpha(img, lambda r, g, b: r < thr and g < thr and b < thr)


def grey_to_alpha(img, key=(150, 150, 150), tol=26):
    return key_to_alpha(img, lambda r, g, b: abs(r - key[0]) < tol and abs(g - key[1]) < tol and abs(b - key[2]) < tol)


_metal = None


def ground(dst, box, rgb):
    """A sign's ground: the pack metal, tinted to the sign's colour (its grain kept)."""
    global _metal
    if _metal is None:
        _metal = Image.open(METAL).convert('L')
    x, y, w, h = box
    m = _metal.resize((max(w, 64), max(h, 64)), Image.LANCZOS).crop((0, 0, w, h))
    mean = max(1.0, sum(m.tobytes()) / (w * h))
    px = m.load()
    out = Image.new('RGBA', (w, h))
    op = out.load()
    for yy in range(h):
        for xx in range(w):
            k = 0.82 + 0.18 * px[xx, yy] / mean
            op[xx, yy] = tuple(min(255, round(c * k)) for c in rgb) + (255,)
    dst.alpha_composite(out, (x, y))


def frame(dst, box, rgb, bw=2):
    """A cabinet's rim: the pack metal, dark."""
    x, y, w, h = box
    for (fx, fy, fw, fh) in ((x, y, w, bw), (x, y + h - bw, w, bw), (x, y, bw, h), (x + w - bw, y, bw, h)):
        ground(dst, (fx, fy, fw, fh), rgb)


def compose():
    atlas = Image.new('RGBA', (N, N), (40, 40, 40, 255))
    d = ImageDraw.Draw(atlas)

    # ---- pack pictures
    menu = pack(r'BurgerPiz\BurgerPiz\Textures\menu_burger.png')
    burger = white_to_alpha(menu.crop((403, 22, 503, 100)).copy())
    burger2 = white_to_alpha(menu.crop((388, 132, 462, 194)).copy())
    fries = white_to_alpha(menu.crop((18, 150, 122, 196)).copy())
    foods4 = pack(r'All\All\Textures\Foods_04.jpg')
    pizza = grey_to_alpha(foods4.crop((702, 519, 952, 769)).copy())
    foods = pack(r'All\All\Textures\Foods.jpg')
    bananas = black_to_alpha(foods.crop((770, 0, 1024, 256)).copy())
    melon = black_to_alpha(foods.crop((256, 815, 385, 985)).copy())
    logo6 = pack(r'Gas_station\Gas_station\Textures\6twelve.jpg').crop((30, 30, 226, 226))
    board6 = pack(r'Gas_station\Gas_station\Textures\6twelve_Sign.png')
    prices = board6.crop((96, 176, 176, 252))
    stripes = pack(r'Gas_station\Gas_station\Textures\Sign.jpg')
    pizza_banner = pack(r'Pizzeria\Pizzeria\Textures\sign.png').crop((0, 0, 360, 165))
    deco = pack(r'Pizzeria\Pizzeria\Textures\Decorative_Sign.png')
    tin_burgers = deco.crop((255, 111, 350, 176))      # "Burgers - Best in Town"
    tin_eat1 = deco.crop((340, 199, 430, 256))          # "Eat Good Food" (two burgers)
    tin_eat2 = deco.crop((102, 220, 196, 272))          # "EAT GOOD FOOD"
    tin_dance = deco.crop((193, 102, 252, 180))         # a couple dancing
    plate_nc = deco.crop((103, 62, 172, 96))            # NORTH CAROLINA, a made-up number
    paintings = pack(r'All\All\Textures\Paintings.jpg')
    palms = paintings.crop((258, 0, 514, 190))
    desert = paintings.crop((514, 268, 774, 460))
    shirt = black_to_alpha(pack(r'All\All\Textures\Clothing.png').crop((8, 165, 160, 370)).copy())
    tv = pack(r'All\All\Textures\Electronics.jpg').crop((660, 0, 900, 155))
    gold = pack(r'PSX Mega Pack v3.2.1\PSX Mega Pack\Textures\gold_bar_mp_1.png', ART_ROOT).crop((0, 0, 64, 64))
    rx = pack(r'NEUROZAM-9_PSX_PillBottle\NEUROZAM-9_PSX_PillBottle\T_PillBottle_PSX.png', ART_ROOT).crop((2, 15, 97, 59))
    wheel = pack(r'psx_vehicles_by_boggle_V28062025\psx_vehicles_by_boggle\American\2_door_coupe\textures\Candy Apple Red.png', ART_ROOT).crop((0, 0, 33, 33))
    wheel = key_to_alpha(wheel.copy(), lambda r, g, b: r > 200 and g < 80 and b < 80)   # the body's red round it
    water = pack(r'PSX Textures v3.1\PSX Textures\256\Color Maps\water_1.png', ART_ROOT)

    RED, YEL, WHT, GRN, CRM = (186, 32, 26), (240, 200, 40), (236, 234, 226), (22, 96, 48), (232, 214, 176)
    BLU, NAVY, DGRN, INK, PLUM, BRN = (30, 110, 190), (22, 50, 120), (18, 70, 50), (26, 26, 28), (60, 26, 80), (84, 70, 56)

    # ---- bulletins (256 x 76)
    b = BULLETIN
    ground(atlas, b[0], RED)                                             # STACK BURGER
    paste_fit(atlas, fries, (b[0][0] + 4, b[0][1] + 10, 80, 56))
    paste_fit(atlas, tin_burgers, (b[0][0] + 86, b[0][1] + 6, 84, 64))
    paste_fit(atlas, burger, (b[0][0] + 172, b[0][1] + 2, 82, 72))

    ground(atlas, b[1], GRN)                                             # SLICE HOUSE
    paste_fit(atlas, pizza, (b[1][0] + 4, b[1][1] + 4, 68, 68))
    ground(atlas, (b[1][0] + 78, b[1][1] + 8, 172, 60), WHT)
    paste_fit(atlas, pizza_banner, (b[1][0] + 80, b[1][1] + 10, 168, 56))

    ground(atlas, b[2], WHT)                                             # 6twelve
    paste_fit(atlas, stripes, (b[2][0], b[2][1], 256, 12), mode='stretch')
    paste_fit(atlas, logo6, (b[2][0] + 8, b[2][1] + 14, 60, 60))
    paste_fit(atlas, prices, (b[2][0] + 78, b[2][1] + 14, 66, 60))
    paste_fit(atlas, logo6, (b[2][0] + 180, b[2][1] + 14, 60, 60))

    ground(atlas, b[3], CRM)                                             # Eat Good Food
    paste_fit(atlas, tin_eat1, (b[3][0] + 4, b[3][1] + 4, 120, 68))
    paste_fit(atlas, tin_eat2, (b[3][0] + 130, b[3][1] + 6, 122, 64))

    ground(atlas, b[4], YEL)                                             # Burgers, best in town
    paste_fit(atlas, tin_burgers, (b[4][0] + 4, b[4][1] + 4, 110, 68))
    paste_fit(atlas, fries, (b[4][0] + 116, b[4][1] + 8, 60, 60))
    paste_fit(atlas, burger2, (b[4][0] + 180, b[4][1] + 4, 72, 68))

    ground(atlas, b[5], WHT)                                             # a tourism board: the coast, the mountains
    paste_fit(atlas, palms, (b[5][0] + 4, b[5][1] + 4, 124, 68), mode='cover')
    paste_fit(atlas, desert, (b[5][0] + 132, b[5][1] + 4, 120, 68), mode='cover')

    # ---- posters (128 x 64)
    p = POSTER
    ground(atlas, p[0], CRM); paste_fit(atlas, tin_eat2, (p[0][0] + 2, p[0][1] + 2, 124, 60))
    ground(atlas, p[1], RED); paste_fit(atlas, tin_burgers, (p[1][0] + 2, p[1][1] + 2, 124, 60))
    ground(atlas, p[2], WHT)
    paste_fit(atlas, logo6, (p[2][0] + 2, p[2][1] + 4, 56, 56))
    paste_fit(atlas, prices, (p[2][0] + 62, p[2][1] + 2, 64, 60))
    ground(atlas, p[3], GRN)
    paste_fit(atlas, pizza, (p[3][0] + 2, p[3][1] + 2, 60, 60))
    ground(atlas, (p[3][0] + 64, p[3][1] + 10, 62, 44), WHT)
    paste_fit(atlas, pizza_banner, (p[3][0] + 65, p[3][1] + 12, 60, 40))

    # ---- business cabinets (64 x 64)
    f = POLE

    def band(i, top=True, h=12):
        x, y, w, hh = f[i]
        paste_fit(atlas, stripes, (x, y if top else y + hh - h, w, h), mode='stretch')

    ground(atlas, f[0], WHT); paste_fit(atlas, logo6, f[0])              # 6twelve
    ground(atlas, f[1], WHT); band(1, h=14)                               # its price board
    paste_fit(atlas, prices, (f[1][0] + 4, f[1][1] + 16, 56, 46))
    ground(atlas, f[2], RED); paste_fit(atlas, burger, (f[2][0] + 4, f[2][1] + 6, 56, 52))       # STACK BURGER
    ground(atlas, f[3], YEL); paste_fit(atlas, tin_burgers, f[3])                                 # BURGERS
    ground(atlas, f[4], GRN); paste_fit(atlas, pizza, (f[4][0] + 5, f[4][1] + 5, 54, 54))        # SLICE HOUSE
    ground(atlas, f[5], WHT); paste_fit(atlas, pizza_banner, f[5])                                # PIZZA
    ground(atlas, f[6], CRM); paste_fit(atlas, tin_eat1, f[6])                                    # EAT GOOD FOOD
    x, y = f[7][0], f[7][1]                                                                       # motel: the palm beach
    ground(atlas, f[7], BLU); band(7)
    paste_fit(atlas, palms, (x + 4, y + 16, 56, 44), mode='cover')
    x, y = f[8][0], f[8][1]                                                                       # bank: a gold bar
    ground(atlas, f[8], DGRN)
    paste_fit(atlas, gold, (x + 10, y + 10, 44, 44))
    x, y = f[9][0], f[9][1]                                                                       # pharmacy: the Rx label
    ground(atlas, f[9], WHT); band(9)
    paste_fit(atlas, rx, (x + 2, y + 18, 60, 40))
    x, y = f[10][0], f[10][1]                                                                     # car wash: a wheel over water
    ground(atlas, f[10], BLU)
    paste_fit(atlas, water, (x + 2, y + 40, 60, 22), mode='cover')
    paste_fit(atlas, wheel, (x + 14, y + 4, 36, 36))
    x, y = f[11][0], f[11][1]                                                                     # car lot: a plate, two wheels
    ground(atlas, f[11], WHT); band(11)
    paste_fit(atlas, plate_nc, (x + 4, y + 14, 56, 28))
    paste_fit(atlas, wheel, (x + 8, y + 42, 20, 20)); paste_fit(atlas, wheel, (x + 36, y + 42, 20, 20))
    x, y = f[12][0], f[12][1]                                                                     # tyres
    ground(atlas, f[12], INK)
    ground(atlas, (x, y + 50, 64, 14), YEL)
    paste_fit(atlas, wheel, (x + 2, y + 8, 30, 30)); paste_fit(atlas, wheel, (x + 32, y + 8, 30, 30))
    x, y = f[13][0], f[13][1]                                                                     # a strip mall's tenant pylon
    ground(atlas, f[13], BRN); band(13, h=10)
    for k, (bg, pic) in enumerate([(WHT, shirt), (INK, tv), (GRN, pizza), (RED, burger2)]):
        tx, ty = x + 4 + 29 * (k % 2), y + 13 + 25 * (k // 2)
        ground(atlas, (tx, ty, 27, 23), bg)
        paste_fit(atlas, pic, (tx + 1, ty + 1, 25, 21))
    x, y = f[14][0], f[14][1]                                                                     # grocer: produce
    ground(atlas, f[14], RED)
    paste_fit(atlas, bananas, (x + 2, y + 4, 34, 56))
    paste_fit(atlas, melon, (x + 34, y + 16, 28, 40))
    ground(atlas, f[15], PLUM); paste_fit(atlas, tin_dance, (f[15][0] + 6, f[15][1] + 2, 52, 60))  # lounge: the dancers
    for i in range(16):
        frame(atlas, f[i], (20, 20, 22), 1)

    # ---- gantry panels (code-drawn: plan Q7)
    for gi, g in enumerate(GANTRY):
        x, y, w, h = g
        d.rectangle((x, y, x + w - 1, y + h - 1), fill=(0, 102, 64, 255))
        d.rectangle((x + 3, y + 3, x + w - 4, y + h - 4), outline=(236, 240, 236, 255), width=2)
        d.rectangle((x + 16, y + 14, x + 16 + (70 if gi == 0 else 58), y + 21), fill=(236, 240, 236, 255))
        d.rectangle((x + 16, y + 28, x + 16 + (52 if gi == 0 else 76), y + 34), fill=(236, 240, 236, 255))
        if gi == 0:   # a down arrow per lane
            for ax in (x + 34, x + 94):
                d.rectangle((ax - 2, y + 42, ax + 2, y + 52), fill=(236, 240, 236, 255))
                d.polygon([(ax - 7, y + 51), (ax + 7, y + 51), (ax, y + 58)], fill=(236, 240, 236, 255))
        else:         # the exit arrow, up and right
            d.line((x + 96, y + 54, x + 112, y + 38), fill=(236, 240, 236, 255), width=5)
            d.polygon([(x + 104, y + 34), (x + 117, y + 33), (x + 116, y + 46)], fill=(236, 240, 236, 255))

    # ---- metal (pack), lens, cabinet sides
    metal = Image.open(METAL).convert('RGB')
    for box, tint in ((METAL_DARK, (0.42, 0.43, 0.46)), (METAL_LIGHT, (0.80, 0.81, 0.82))):
        x, y, w, h = box
        m = metal.resize((w, h), Image.LANCZOS)
        r, g, bl = m.split()
        m = Image.merge('RGB', [c.point(lambda v, k=k: min(255, round(v * k))) for c, k in zip((r, g, bl), tint)])
        atlas.paste(m, (x, y))
    ground(atlas, LENS, (255, 244, 214))
    ground(atlas, BLACK, (24, 24, 26))
    ground(atlas, WHITE, (236, 236, 232))

    atlas = atlas.convert('RGB')

    # ---- the night mask: R = A = glow
    mask = Image.new('RGBA', (N // 4, N // 4), (0, 0, 0, 0))
    md = ImageDraw.Draw(mask)
    for box in BULLETIN + POSTER:     # floodlit from the lamps under the face: brightest at the foot
        x, y, w, h = [v // 4 for v in box]
        for k in range(h):
            v = round(255 * (0.62 + 0.38 * (k + 0.5) / h))
            md.line((x, y + k, x + w - 1, y + k), fill=(v, 0, 255, v))
    for box in POLE:                  # lit from inside
        x, y, w, h = [v // 4 for v in box]
        md.rectangle((x, y, x + w - 1, y + h - 1), fill=(235, 0, 255, 235))
    x, y, w, h = [v // 4 for v in LENS]
    md.rectangle((x, y, x + w - 1, y + h - 1), fill=(255, 0, 255, 255))
    return atlas, mask


def png_bytes(im):
    bio = io.BytesIO()
    im.save(bio, 'PNG', optimize=False)
    return bio.getvalue()


def main():
    check = '--check' in sys.argv
    atlas, mask = compose()
    out = [(ATLAS, png_bytes(atlas)), (MASK, png_bytes(mask))]
    if '--sheet' in sys.argv:
        sheet = sys.argv[sys.argv.index('--sheet') + 1]
        atlas.resize((1024, 1024), Image.NEAREST).save(sheet)
    bad = 0
    for path, data in out:
        have = open(path, 'rb').read() if os.path.exists(path) else None
        if check:
            if have != data:
                print('DIFFERS', path); bad += 1
            continue
        if have == data:
            print('same', path); continue
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, 'wb').write(data)
        print('wrote', path, len(data), 'bytes')
    if check:
        print('SIGNS ATLAS ' + ('OK' if bad == 0 else 'DIFFERS'))
        sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
