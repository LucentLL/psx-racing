"""lure_art.py - the Lake Lure stage's few textures, from art already in the repo.

    python tools/roads/lure_art.py

Writes Assets/PSXRacing/Art/LakeLure/Tex/:
  * wall and roof tiles cropped out of the city's own house atlas
    (Art/City/Props/city_house_atlas.png, 64 px tiles) so they can repeat
    across a wall: white lap siding, weathered planks (the village's
    log-cabin and board fronts), cedar, dark stained wood, asphalt shingle,
    stucco; a window; ridged metal roofing and a storefront drawn plainly;
  * lure_signs.png + lure_signs.json: every sign face the stage stands up,
    lettered in Overpass (tools/city/fonts, SIL OFL 1.1) like the city's guide
    signs. Routes are WORDS, never shields (no trademarked marks); shop boards
    are generic words, never a real business's name.
"""
import json, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
ART = os.path.join(ROOT, 'Assets', 'PSXRacing', 'Art')
OUT = os.path.join(ART, 'LakeLure', 'Tex')
os.makedirs(OUT, exist_ok=True)
FONT = os.path.join(ROOT, 'tools', 'city', 'fonts', 'Overpass-Variable.ttf')

atlas = Image.open(os.path.join(ART, 'City', 'Props', 'city_house_atlas.png')).convert('RGB')
T = atlas.size[0] // 4
def tile(c, r, w=1, h=1): return atlas.crop((c * T, r * T, (c + w) * T, (r + h) * T))
def save(im, name, size=None):
    if size: im = im.resize(size, Image.BICUBIC)
    im.save(os.path.join(OUT, name + '.png'))
def tint(im, rgb, k=1.0):
    g = im.convert('L')
    return Image.merge('RGB', [g.point(lambda v, c=c: max(0, min(255, int(v * c / 255 * k)))) for c in rgb])

save(tile(0, 2, 2, 1), 'siding_white', (256, 128))       # white lap siding
save(tile(2, 1), 'wood_planks', (128, 128))              # weathered horizontal planks: log-cabin / board fronts
save(tile(1, 1), 'wood_cedar', (128, 128))               # cedar boards
save(tile(2, 0), 'wood_dark', (128, 128))                # dark stained wood
save(tile(3, 2), 'shingle_dark', (128, 128))             # asphalt shingle roof
save(tile(3, 3), 'window', (128, 128))                   # a window, white frame
st = tile(1, 0).convert('L').point(lambda v: int(170 + v * 0.33))
save(Image.merge('RGB', [st, st, st.point(lambda v: int(v * 0.97))]), 'stucco_white', (128, 128))

# ridged metal roofing: standing seams every 16 px, grey (tinted in the material)
m = Image.new('RGB', (128, 128), (150, 152, 152)); d = ImageDraw.Draw(m)
for x in range(0, 128, 16):
    d.line([(x, 0), (x, 127)], fill=(205, 207, 207), width=2); d.line([(x + 3, 0), (x + 3, 127)], fill=(112, 114, 114), width=1)
save(m.filter(ImageFilter.GaussianBlur(0.6)), 'metal_roof')
# clay tile roof (the Lake Lure Inn): the repo's roof tiles, warmed to terracotta
rt = Image.open(os.path.join(ART, 'Buildings', 'Textures', 'Roof_tiles.jpg')).convert('RGB')
save(tint(rt, (214, 104, 72), 1.25), 'tile_roof_red', (128, 128))
# a storefront: dark glass in white mullions, a door, a kick plate
s = Image.new('RGB', (256, 128), (238, 236, 228)); d = ImageDraw.Draw(s)
for x0 in (8, 136):
    d.rectangle([x0, 18, x0 + 112, 112], fill=(44, 54, 60)); d.rectangle([x0, 100, x0 + 112, 112], fill=(120, 110, 96))
    for k in (37, 75): d.line([(x0 + k, 18), (x0 + k, 100)], fill=(238, 236, 228), width=3)
    d.line([(x0, 60), (x0 + 112, 60)], fill=(238, 236, 228), width=2)
    for y in range(20, 98, 6): d.line([(x0 + 4, y), (x0 + 30, y + 10)], fill=(70, 84, 92), width=1)
d.rectangle([118, 30, 132, 120], fill=(96, 66, 40))
save(s, 'storefront')

# ------------------------------------------------------------------ signs
W, H = 1024, 1280
A = Image.new('RGBA', (W, H), (0, 0, 0, 0)); D = ImageDraw.Draw(A)
rects = {}
cur = [4, 4, 0]   # x, y, row height
def font(px, bold=True):
    f = ImageFont.truetype(FONT, px)
    try: f.set_variation_by_name('Bold' if bold else 'Regular')
    except Exception: pass
    return f
def place(name, w, h, draw):
    if cur[0] + w > W - 4: cur[0] = 4; cur[1] += cur[2] + 4; cur[2] = 0
    x, y = cur[0], cur[1]; cur[0] += w + 4; cur[2] = max(cur[2], h)
    draw(x, y, w, h); rects[name] = [x / W, 1 - (y + h) / H, w / W, h / H]
def centred(x, y, w, text, f, fill, yy):
    tw = D.textlength(text, font=f); D.text((x + (w - tw) / 2, y + yy), text, font=f, fill=fill)
def board(x, y, w, h, bg, edge, rad=6):
    D.rounded_rectangle([x, y, x + w - 1, y + h - 1], radius=rad, fill=edge)
    D.rounded_rectangle([x + 4, y + 4, x + w - 5, y + h - 5], radius=max(1, rad - 3), fill=bg)
WHITE, BLACK = (246, 246, 240, 255), (16, 16, 16, 255)
for mph in (35, 25, 20):          # R2-1, 24 x 30 in
    def dr(x, y, w, h, mph=mph):
        board(x, y, w, h, WHITE, BLACK, 8)
        centred(x, y, w, 'SPEED', font(30), BLACK, 14); centred(x, y, w, 'LIMIT', font(30), BLACK, 48)
        centred(x, y, w, str(mph), font(70), BLACK, 84)
    place('speed%d' % mph, 128, 160, dr)
GREEN, BROWN, CREAM = (0, 104, 68, 255), (92, 58, 36, 255), (244, 236, 214, 255)
def village(x, y, w, h):
    board(x, y, w, h, GREEN, WHITE, 10)
    centred(x, y, w, 'CHIMNEY ROCK', font(44), WHITE, 16); centred(x, y, w, 'VILLAGE LIMITS', font(44), WHITE, 66)
place('village', 384, 128, village)
def park(x, y, w, h):
    board(x, y, w, h, BROWN, CREAM, 10)
    centred(x, y, w, 'CHIMNEY ROCK', font(42), CREAM, 12); centred(x, y, w, 'STATE PARK', font(42), CREAM, 58)
    ax, ay = x + w / 2, y + 128; D.polygon([(ax - 70, ay), (ax - 30, ay - 22), (ax - 30, ay + 22)], fill=CREAM); D.rectangle([ax - 32, ay - 8, ax + 70, ay + 8], fill=CREAM)
place('park', 384, 160, park)
for route, way in (('US 64', 'WEST'), ('US 74A', 'WEST'), ('NC 9', 'NORTH'), ('US 64', 'EAST'), ('US 74A', 'EAST'), ('NC 9', 'SOUTH')):
    def dr(x, y, w, h, route=route, way=way):
        board(x, y, w, h, WHITE, BLACK, 6)
        centred(x, y, w, way, font(30), BLACK, 10); centred(x, y, w, route, font(46 if len(route) < 6 else 40), BLACK, 54)
    place('route_%s_%s' % (route.replace(' ', ''), way), 160, 112, dr)
def byway(x, y, w, h):
    board(x, y, w, h, (26, 92, 120, 255), WHITE, 8)
    centred(x, y, w, 'NC SCENIC', font(30), WHITE, 12); centred(x, y, w, 'BYWAY', font(42), WHITE, 46)
place('byway', 192, 112, byway)
YEL = (250, 204, 21, 255)
def ped(x, y, w, h):          # W11-2 as a pictogram: a walker, no words
    c = (x + w / 2, y + h / 2)
    D.polygon([(c[0], y + 2), (x + w - 2, c[1]), (c[0], y + h - 2), (x + 2, c[1])], fill=BLACK)
    D.polygon([(c[0], y + 10), (x + w - 10, c[1]), (c[0], y + h - 10), (x + 10, c[1])], fill=YEL)
    D.ellipse([c[0] - 9, c[1] - 52, c[0] + 9, c[1] - 34], fill=BLACK)
    D.line([(c[0], c[1] - 32), (c[0] - 4, c[1] + 6)], fill=BLACK, width=12)
    D.line([(c[0] - 4, c[1] + 4), (c[0] - 22, c[1] + 42)], fill=BLACK, width=10); D.line([(c[0] - 4, c[1] + 4), (c[0] + 18, c[1] + 42)], fill=BLACK, width=10)
    D.line([(c[0] - 2, c[1] - 22), (c[0] - 26, c[1] - 2)], fill=BLACK, width=8); D.line([(c[0] - 2, c[1] - 22), (c[0] + 22, c[1] - 6)], fill=BLACK, width=8)
place('ped', 160, 160, ped)
def inlane(x, y, w, h):       # R1-6, in the lane at a crossing: words only
    board(x, y, w, h, WHITE, BLACK, 4)
    centred(x, y, w, 'STATE LAW', font(20), BLACK, 10); D.rectangle([x + 10, y + 36, x + w - 10, y + 70], fill=(200, 30, 30, 255))
    centred(x, y, w, 'YIELD', font(24), WHITE, 38); centred(x, y, w, 'TO', font(20), BLACK, 76); centred(x, y, w, 'PEDESTRIANS', font(18), BLACK, 102)
place('inlane', 128, 136, inlane)
for town in ('LAKE LURE', 'BAT CAVE'):
    def dr(x, y, w, h, town=town):
        board(x, y, w, h, GREEN, WHITE, 10); centred(x, y, w, town, font(52), WHITE, 22)
    place('town_' + town.replace(' ', ''), 320, 96, dr)
# Type III barricade rail: orange and white stripes slanting down toward the side the traffic passes
def stripes(x, y, w, h):
    D.rectangle([x, y, x + w - 1, y + h - 1], fill=(250, 250, 244, 255))
    for k in range(-h, w + h, 40):
        D.polygon([(x + k, y + h), (x + k + 20, y + h), (x + k + 20 + h, y), (x + k + h, y)], fill=(244, 110, 20, 255))
    D.rectangle([x, y, x + w - 1, y + h - 1], outline=(30, 30, 30, 255), width=2)
place('barricade', 512, 48, stripes)
def closed(x, y, w, h):
    board(x, y, w, h, WHITE, BLACK, 6); centred(x, y, w, 'ROAD', font(40), BLACK, 6); centred(x, y, w, 'CLOSED', font(40), BLACK, 50)
place('closed', 224, 104, closed)
# shop boards: generic words only
SHOPS = ['GEMS', 'CAFE', 'GIFTS', 'LODGE', 'ICE CREAM', 'FUDGE', 'OUTFITTERS', 'GALLERY', 'ANTIQUES', 'BBQ', 'GENERAL STORE',
         'CABINS', 'RIVER LODGE', 'TACOS', 'COFFEE', 'TAVERN', 'MOTEL', 'BAKERY', 'TUBING', 'CRAFTS', 'PIZZA', 'MARINA', 'BOAT RENTALS', 'INN']
STYLES = [((70, 44, 26, 255), (240, 226, 190, 255)), ((240, 232, 210, 255), (40, 70, 52, 255)), ((32, 58, 44, 255), (236, 222, 180, 255)),
          ((128, 34, 30, 255), (246, 236, 214, 255)), ((236, 230, 214, 255), (120, 40, 30, 255))]
for k, name in enumerate(SHOPS):
    def dr(x, y, w, h, name=name, k=k):
        bg, fg = STYLES[k % len(STYLES)]
        board(x, y, w, h, bg, (40, 28, 20, 255), 6)
        f = font(40);
        while D.textlength(name, font=f) > w - 24: f = font(f.size - 2)
        centred(x, y, w, name, f, fg, (h - f.size) / 2 - 6)
    place('shop%02d' % k, 288, 64, dr)
A.save(os.path.join(ART, 'LakeLure', 'lure_signs.png'))
names = list(rects.keys())
json.dump({'note': 'uv rect (u, v, w, h; v from the bottom) of each named face in lure_signs.png, four floats per name', 'shops': SHOPS,
           'names': names, 'uv': [round(v, 6) for n in names for v in rects[n]]},
          open(os.path.join(ART, 'LakeLure', 'lure_signs.json'), 'w'))
print('wrote', len(os.listdir(OUT)), 'textures and', len(rects), 'sign faces; atlas rows used to y', cur[1] + cur[2])
