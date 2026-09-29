"""signs_atlas.py - compose Charlotte's sign-face atlas (plan WP-23).

    py tools/city/signs_atlas.py            write the two PNGs (only if their bytes change)
    py tools/city/signs_atlas.py --check    compose in memory, exit 1 if the shipped PNGs differ
    py tools/city/signs_atlas.py --sheet X  also write a 2x preview sheet to X

Writes Assets/PSXRacing/Art/City/Signs/CitySigns.png (512 x 512, opaque: it
ships as RGB565) and CitySigns_night.png (128 x 128, the night mask PSX/Lit's
_NightMask reads: R = A = how brightly the texel glows after dark).

EVERY PICTURE COMES FROM THE OWNER'S PACKS (owner rule), and every brand is
FICTIONAL (the owner: no real brands or trademarks):
  * food: BurgerPiz `menu_burger.png` (burgers, fries), the All pack's
    `Foods_04.jpg` (a pepperoni pizza); the game's own restaurants STACK BURGER
    and SLICE HOUSE;
  * the Gas_station pack's own fictional brand, `6twelve` (logo and price
    board), and its striped `Sign.jpg`; the Pizzeria pack's `sign.png` (PIZZA);
  * three tin signs from the Pizzeria pack's `Decorative_Sign.png`, chosen
    after LOOKING at every one of its 36: "Burgers - Best in Town", and the two
    "Eat Good Food". NOT used: the two CAMEL signs (a tobacco trademark), the
    Harley-Davidson bar-and-shield signs, the ones with a contour cola bottle,
    and the fries-and-burger sign that reads like a burger chain's name;
  * the structure metal is the pack metal the city's lamp posts wear
    (LifeSim House `Metal.jpg`, WP-07), dark weathered steel for poles and
    billboard backs, a lighter galvanised grey for gantry trusses.
NOT used although the plan listed them: the Buildings pack's `Shops_01-31`,
which are photographs of real storefronts with real business names and phone
numbers.

Drawn in code (plan Q7, "the pattern is the design"): the gantry panels (green,
white border, abstract white bars - no text), plain colour grounds, and the
lettering of the fictional names, set in Aileron (CC0, the face Pillow embeds).

THE LAYOUT IS SHARED WITH Scripts/City/CitySigns.cs (the Atlas table there):
pixel rectangles from the TOP-LEFT, every edge a multiple of 4 so the 128 px
night mask is in register.
"""
import sys, io, os
from PIL import Image, ImageDraw, ImageFont, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
ART = os.environ.get('PSX_ART_DIR', r'C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing')
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

POLE_NAMES = ['6twelve', 'GAS', 'STACK BURGER', 'BURGERS', 'SLICE HOUSE', 'PIZZA', 'EAT GOOD FOOD', 'MOTEL',
              'BANK', 'DRUGS', 'CAR WASH', 'AUTO SALES', 'TIRES', 'PLAZA', 'FOOD MART', 'LOUNGE']


def pack(rel):
    p = os.path.join(ART, rel)
    if not os.path.exists(p):
        sys.exit('missing pack file (set PSX_ART_DIR): ' + p)
    return Image.open(p).convert('RGBA')


def font(size):
    return ImageFont.load_default(size)


def fit_text(d, box, text, fill, max_size, stroke=1, stroke_fill=None):
    """Largest size that fits the box, centred in it."""
    x, y, w, h = box
    size = max_size
    while size > 6:
        f = font(size)
        l, t, r, b = d.textbbox((0, 0), text, font=f, stroke_width=stroke)
        if r - l <= w and b - t <= h:
            break
        size -= 1
    f = font(size)
    l, t, r, b = d.textbbox((0, 0), text, font=f, stroke_width=stroke)
    d.text((x + (w - (r - l)) // 2 - l, y + (h - (b - t)) // 2 - t), text, font=f, fill=fill,
           stroke_width=stroke, stroke_fill=stroke_fill or fill)


def paste_fit(dst, img, box, bg=None, mode='contain'):
    """Scale img into box (contain or cover), centred; alpha composited."""
    x, y, w, h = box
    sw, sh = img.size
    s = min(w / sw, h / sh) if mode == 'contain' else max(w / sw, h / sh)
    nw, nh = max(1, round(sw * s)), max(1, round(sh * s))
    im = img.resize((nw, nh), Image.LANCZOS)
    if mode == 'cover':
        l, t = (nw - w) // 2, (nh - h) // 2
        im = im.crop((l, t, l + w, t + h)); nw, nh = w, h
    layer = Image.new('RGBA', dst.size, (0, 0, 0, 0))
    layer.paste(im, (x + (w - nw) // 2, y + (h - nh) // 2))
    dst.alpha_composite(layer)


def white_to_alpha(img, thr=236):
    """The menu photos sit on white: make the white clear so they sit on a colour."""
    px = img.load()
    for yy in range(img.size[1]):
        for xx in range(img.size[0]):
            r, g, b, a = px[xx, yy]
            if r > thr and g > thr and b > thr:
                px[xx, yy] = (r, g, b, 0)
    return img


def grey_to_alpha(img, key=(150, 150, 150), tol=26):
    px = img.load()
    for yy in range(img.size[1]):
        for xx in range(img.size[0]):
            r, g, b, a = px[xx, yy]
            if abs(r - key[0]) < tol and abs(g - key[1]) < tol and abs(b - key[2]) < tol:
                px[xx, yy] = (r, g, b, 0)
    return img


def rect(d, box, fill, border=None, bw=2):
    x, y, w, h = box
    d.rectangle((x, y, x + w - 1, y + h - 1), fill=fill)
    if border:
        for k in range(bw):
            d.rectangle((x + k, y + k, x + w - 1 - k, y + h - 1 - k), outline=border)


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
    logo6 = pack(r'Gas_station\Gas_station\Textures\6twelve.jpg')
    board6 = pack(r'Gas_station\Gas_station\Textures\6twelve_Sign.png')
    prices = board6.crop((96, 176, 176, 252))
    stripes = pack(r'Gas_station\Gas_station\Textures\Sign.jpg')
    pizza_banner = pack(r'Pizzeria\Pizzeria\Textures\sign.png').crop((0, 0, 360, 165))
    deco = pack(r'Pizzeria\Pizzeria\Textures\Decorative_Sign.png')
    tin_burgers = deco.crop((255, 111, 350, 176))      # "Burgers - Best in Town"
    tin_eat1 = deco.crop((340, 199, 430, 256))          # "Eat Good Food" (two burgers)
    tin_eat2 = deco.crop((102, 220, 196, 272))          # "EAT GOOD FOOD"

    RED, YEL, WHT, GRN, CRM = (186, 32, 26, 255), (255, 214, 40, 255), (250, 250, 245, 255), (22, 96, 48, 255), (238, 222, 184, 255)

    # ---- bulletins (256 x 76)
    b = BULLETIN
    rect(d, b[0], RED)                                             # STACK BURGER
    paste_fit(atlas, burger, (b[0][0] + 170, b[0][1] + 2, 84, 72))
    fit_text(d, (b[0][0] + 8, b[0][1] + 8, 160, 34), 'STACK BURGER', YEL, 30, 1)
    fit_text(d, (b[0][0] + 8, b[0][1] + 46, 160, 20), 'NEXT EXIT  -  OPEN LATE', WHT, 16, 0)

    rect(d, b[1], GRN)                                             # SLICE HOUSE
    paste_fit(atlas, pizza, (b[1][0] + 4, b[1][1] + 4, 68, 68))
    fit_text(d, (b[1][0] + 80, b[1][1] + 8, 170, 34), 'SLICE HOUSE', WHT, 30, 1)
    fit_text(d, (b[1][0] + 80, b[1][1] + 46, 170, 20), 'HOT PIZZA  -  DRIVE THRU', YEL, 16, 0)

    rect(d, b[2], WHT)                                             # 6twelve
    paste_fit(atlas, logo6.crop((30, 30, 226, 226)), (b[2][0] + 6, b[2][1] + 4, 68, 68))
    fit_text(d, (b[2][0] + 82, b[2][1] + 8, 166, 30), 'OPEN 24 HOURS', (20, 150, 160, 255), 26, 1)
    fit_text(d, (b[2][0] + 82, b[2][1] + 44, 166, 22), 'COLD DRINKS  -  HOT COFFEE', (40, 170, 70, 255), 16, 0)

    rect(d, b[3], CRM)                                             # Eat Good Food
    paste_fit(atlas, tin_eat1, (b[3][0] + 4, b[3][1] + 4, 120, 68))
    fit_text(d, (b[3][0] + 130, b[3][1] + 10, 120, 30), 'NEXT EXIT', RED, 26, 1)
    fit_text(d, (b[3][0] + 130, b[3][1] + 46, 120, 20), 'DINER  -  BREAKFAST', (60, 40, 20, 255), 14, 0)

    rect(d, b[4], YEL)                                             # Burgers, best in town
    paste_fit(atlas, tin_burgers, (b[4][0] + 4, b[4][1] + 4, 110, 68))
    paste_fit(atlas, fries, (b[4][0] + 176, b[4][1] + 8, 76, 60))
    fit_text(d, (b[4][0] + 116, b[4][1] + 24, 60, 28), 'EXIT 9', RED, 22, 1)

    rect(d, b[5], (30, 30, 34, 255))                               # STACK BURGER, the fries board
    paste_fit(atlas, fries, (b[5][0] + 4, b[5][1] + 10, 90, 56))
    paste_fit(atlas, burger2, (b[5][0] + 190, b[5][1] + 6, 62, 64))
    fit_text(d, (b[5][0] + 96, b[5][1] + 8, 92, 30), 'STACK', YEL, 28, 1)
    fit_text(d, (b[5][0] + 96, b[5][1] + 40, 92, 26), 'BURGER', YEL, 24, 1)

    # ---- posters (128 x 64)
    p = POSTER
    rect(d, p[0], CRM); paste_fit(atlas, tin_eat2, (p[0][0] + 2, p[0][1] + 2, 124, 60))
    rect(d, p[1], RED); paste_fit(atlas, tin_burgers, (p[1][0] + 2, p[1][1] + 2, 124, 60))
    rect(d, p[2], WHT)
    paste_fit(atlas, logo6.crop((30, 30, 226, 226)), (p[2][0] + 2, p[2][1] + 4, 56, 56))
    paste_fit(atlas, prices, (p[2][0] + 62, p[2][1] + 2, 64, 60))
    rect(d, p[3], GRN)
    paste_fit(atlas, pizza, (p[3][0] + 2, p[3][1] + 2, 60, 60))
    fit_text(d, (p[3][0] + 64, p[3][1] + 8, 62, 22), 'SLICE', WHT, 20, 1)
    fit_text(d, (p[3][0] + 64, p[3][1] + 34, 62, 22), 'HOUSE', WHT, 20, 1)

    # ---- business cabinets (64 x 64)
    f = POLE
    stripe_bg = stripes.resize((64, 64), Image.LANCZOS)

    def cab(i, bg, text, fg, sub=None, subfg=None, stripe=False):
        x, y, w, h = f[i]
        rect(d, f[i], bg)
        if stripe:
            atlas.alpha_composite(stripe_bg, (x, y))
            rect(d, (x + 4, y + 14, 56, 36), bg)
        if sub:
            fit_text(d, (x + 5, y + 16, 54, 22), text, fg, 20, 1)
            fit_text(d, (x + 5, y + 38, 54, 10), sub, subfg or fg, 9, 0)
        else:
            fit_text(d, (x + 5, y + 16, 54, 32), text, fg, 22, 1)
        rect(d, f[i], None, (20, 20, 20, 255), 1)

    rect(d, f[0], WHT); paste_fit(atlas, logo6.crop((30, 30, 226, 226)), f[0])
    rect(d, f[1], WHT); paste_fit(atlas, stripes, (f[1][0], f[1][1], 64, 26), mode='cover')
    fit_text(d, (f[1][0] + 4, f[1][1] + 4, 56, 18), 'GAS', WHT, 18, 1, (20, 20, 20, 255))
    paste_fit(atlas, prices, (f[1][0] + 4, f[1][1] + 26, 56, 36))
    rect(d, f[2], RED); paste_fit(atlas, burger, (f[2][0] + 8, f[2][1] + 2, 48, 36))
    fit_text(d, (f[2][0] + 3, f[2][1] + 40, 58, 10), 'STACK', YEL, 12, 1)
    fit_text(d, (f[2][0] + 3, f[2][1] + 51, 58, 10), 'BURGER', YEL, 12, 1)
    rect(d, f[3], YEL); paste_fit(atlas, tin_burgers, f[3])
    rect(d, f[4], GRN); paste_fit(atlas, pizza, (f[4][0] + 12, f[4][1] + 2, 40, 40))
    fit_text(d, (f[4][0] + 3, f[4][1] + 43, 58, 18), 'SLICE HOUSE', WHT, 12, 1)
    rect(d, f[5], WHT); paste_fit(atlas, pizza_banner, f[5])
    rect(d, f[6], CRM); paste_fit(atlas, tin_eat1, f[6])
    cab(7, (20, 60, 140, 255), 'MOTEL', (255, 90, 60, 255), 'VACANCY', WHT, stripe=True)
    cab(8, (18, 70, 50, 255), 'BANK', WHT, 'DRIVE-UP', YEL)
    cab(9, WHT, 'DRUGS', RED, 'PHARMACY', (20, 60, 140, 255), stripe=True)
    cab(10, (30, 120, 200, 255), 'CAR', WHT, 'WASH', YEL)
    cab(11, WHT, 'AUTO', (20, 60, 140, 255), 'SALES', RED, stripe=True)
    cab(12, (20, 20, 22, 255), 'TIRES', YEL, 'BRAKES - OIL', WHT)
    x, y = f[13][0], f[13][1]                                       # PLAZA: a strip mall's tenant pylon
    rect(d, f[13], (70, 60, 50, 255))
    fit_text(d, (x + 4, y + 2, 56, 12), 'PLAZA', WHT, 12, 1)
    for k, (bg, fg, t) in enumerate([(WHT, RED, 'NAILS'), (YEL, (20, 20, 20, 255), 'CLEANERS'),
                                     ((20, 60, 140, 255), WHT, 'VIDEO'), (RED, WHT, 'DELI')]):
        rect(d, (x + 4, y + 16 + 12 * k, 56, 11), bg)
        fit_text(d, (x + 6, y + 17 + 12 * k, 52, 9), t, fg, 9, 0)
    cab(14, (200, 40, 30, 255), 'FOOD', WHT, 'MART', YEL, stripe=True)
    cab(15, (40, 20, 60, 255), 'LOUNGE', (255, 120, 200, 255))

    # ---- gantry panels (code-drawn: plan Q7)
    for gi, g in enumerate(GANTRY):
        x, y, w, h = g
        rect(d, g, (0, 102, 64, 255))
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
    rect(d, LENS, (255, 244, 214, 255))
    rect(d, BLACK, (24, 24, 26, 255))
    rect(d, WHITE, (236, 236, 232, 255))

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
