# render.py - draw the Charlotte road network + candidate courses (quick contact sheet or the final sheet)
import json, math, sys, os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
FONT = 'C:/Windows/Fonts/arialbd.ttf'
FONTR = 'C:/Windows/Fonts/arial.ttf'
MONO = 'C:/Windows/Fonts/consola.ttf'

def font(sz, bold=True):
    return ImageFont.truetype(FONT if bold else FONTR, sz)

BG = (14, 17, 22)
WATER = (28, 52, 82)
LAKE = (30, 58, 92)
BLD = (36, 40, 48)
ROAD = {5: (120, 126, 138), 4: (108, 114, 126), 3: (92, 97, 108), 2: (80, 85, 95), 1: (66, 70, 79), 0: (50, 54, 62)}
PALETTE = [(255, 82, 82), (255, 171, 64), (255, 235, 59), (118, 255, 3), (0, 230, 118), (29, 233, 182), (0, 229, 255),
           (64, 196, 255), (68, 138, 255), (124, 77, 255), (224, 64, 251), (255, 64, 129), (255, 138, 101), (174, 234, 0),
           (100, 255, 218), (255, 213, 79), (179, 136, 255), (255, 110, 64), (105, 240, 174), (0, 176, 255), (234, 128, 252),
           (255, 241, 118), (128, 216, 255), (255, 145, 128), (185, 246, 202), (255, 204, 128)]

def load():
    net = json.load(open(os.path.join(OUT, 'net.json')))
    cands = json.load(open(os.path.join(OUT, 'cands.json')))
    bld = json.load(open(os.path.join(OUT, 'bld.json'))) if os.path.exists(os.path.join(OUT, 'bld.json')) else []
    # pre-split flat point lists
    edges = []
    for rank, flags, width, flat, name in net['edges']:
        pts = list(zip(flat[0::2], flat[1::2]))
        xs = flat[0::2]; zs = flat[1::2]
        edges.append((rank, flags, width, pts, (min(xs), min(zs), max(xs), max(zs)), name))
    waters = []
    for kind, width, flat, name in net['waters']:
        pts = list(zip(flat[0::2], flat[1::2]))
        if not pts: continue
        xs = flat[0::2]; zs = flat[1::2]
        waters.append((kind, width, pts, (min(xs), min(zs), max(xs), max(zs))))
    blds = []
    for h, flat in bld:
        pts = list(zip(flat[0::2], flat[1::2]))
        if len(pts) < 3: continue
        xs = flat[0::2]; zs = flat[1::2]
        blds.append((h, pts, (min(xs), min(zs), max(xs), max(zs))))
    return edges, waters, blds, cands

class View:
    def __init__(self, x0, z0, x1, z1, W, H):
        # fit the box into W x H keeping aspect
        s = min(W / (x1 - x0), H / (z1 - z0))
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        self.s = s; self.W = W; self.H = H
        self.x0 = cx - W / 2 / s; self.x1 = cx + W / 2 / s
        self.z0 = cz - H / 2 / s; self.z1 = cz + H / 2 / s
    def p(self, x, z):
        return ((x - self.x0) * self.s, (self.z1 - z) * self.s)
    def inside(self, bb, pad=0):
        return not (bb[2] < self.x0 - pad or bb[0] > self.x1 + pad or bb[3] < self.z0 - pad or bb[1] > self.z1 + pad)

def draw_base(img, ox, oy, v, edges, waters, blds, detail=True):
    d = ImageDraw.Draw(img)
    # water
    for kind, width, pts, bb in waters:
        if not v.inside(bb, 50): continue
        P = [(ox + a, oy + b) for a, b in (v.p(x, z) for x, z in pts)]
        if kind == 1 and len(P) >= 3:
            d.polygon(P, fill=LAKE)
        elif kind == 0 and len(P) >= 2:
            w = max(1, int(round(max(width, 4) * v.s)))
            if width * v.s >= 0.6 or width >= 15:
                d.line(P, fill=WATER, width=w)
    # buildings
    if detail and v.s > 0.08:
        for h, pts, bb in blds:
            if not v.inside(bb, 20): continue
            P = [(ox + a, oy + b) for a, b in (v.p(x, z) for x, z in pts)]
            shade = min(70, 36 + int(h / 8))
            d.polygon(P, fill=(shade, shade + 4, shade + 12))
    # roads, low ranks first
    order = sorted(range(len(edges)), key=lambda i: (edges[i][0] + (0.5 if not edges[i][1] & 1 else 0)))
    for i in order:
        rank, flags, width, pts, bb, name = edges[i]
        if not v.inside(bb, 30): continue
        if not detail and rank < 2 and not flags & 1: continue
        P = [(ox + a, oy + b) for a, b in (v.p(x, z) for x, z in pts)]
        w = max(1, int(round(width * v.s * (1.0 if v.s > 0.3 else 1.6))))
        if v.s < 0.12: w = 2 if rank >= 5 else 1
        col = ROAD.get(rank, ROAD[0])
        if flags & 1: col = ROAD[min(5, rank)]
        d.line(P, fill=col, width=w)

def arrow(d, a, b, col, size):
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    tip = b
    l = (tip[0] - size * math.cos(ang - 0.45), tip[1] - size * math.sin(ang - 0.45))
    r = (tip[0] - size * math.cos(ang + 0.45), tip[1] - size * math.sin(ang + 0.45))
    d.polygon([tip, l, r], fill=col, outline=(0, 0, 0))

def label_disc(d, xy, text, col, r=16, fs=18):
    x, y = xy
    d.ellipse([x - r, y - r, x + r, y + r], fill=col, outline=(0, 0, 0), width=2)
    f = font(fs)
    tb = d.textbbox((0, 0), text, font=f)
    d.text((x - (tb[2] - tb[0]) / 2 - tb[0], y - (tb[3] - tb[1]) / 2 - tb[1]), text, fill=(0, 0, 0), font=f)

def draw_route(img, ox, oy, v, c, col, width, arrows=True, start=True, outline=True):
    d = ImageDraw.Draw(img)
    P = [(ox + a, oy + b) for a, b in (v.p(x, z) for x, z in c['poly'])]
    if c['kind'] == 'circuit': P = P + [P[0]]
    if outline: d.line(P, fill=(0, 0, 0), width=width + 4, joint='curve')
    d.line(P, fill=col, width=width, joint='curve')
    if arrows and len(P) > 4:
        # arrows every ~ 1/6 of the length
        n = len(P)
        for k in range(1, 7):
            q = int(n * k / 7)
            if q + 2 < n: arrow(d, P[q], P[q + 2], col, max(10, width * 3))
    if start:
        if c['kind'] == 'circuit' and c.get('grid') and c['grid'].get('at'):
            sx, sz = c['grid']['at']
        else:
            sx, sz = c['poly'][min(len(c['poly']) - 1, int((c.get('leadM') or 60) / 10))]
        sp = v.p(sx, sz); sp = (ox + sp[0], oy + sp[1])
        # chequered start marker
        r = max(7, width + 3)
        d.rectangle([sp[0] - r, sp[1] - r, sp[0] + r, sp[1] + r], fill=(255, 255, 255), outline=(0, 0, 0), width=2)
        d.rectangle([sp[0] - r, sp[1] - r, sp[0], sp[1]], fill=(0, 0, 0))
        d.rectangle([sp[0], sp[1], sp[0] + r, sp[1] + r], fill=(0, 0, 0))
        if c['kind'] != 'circuit':
            fx, fz = c['poly'][-1]
            if c.get('runM'):
                k = min(len(c['poly']) - 1, int(((c.get('leadM') or 60) + c['runM']) / 10))
                fx, fz = c['poly'][k]
            fp = v.p(fx, fz); fp = (ox + fp[0], oy + fp[1])
            d.ellipse([fp[0] - r, fp[1] - r, fp[0] + r, fp[1] + r], fill=(255, 255, 255), outline=(0, 0, 0), width=2)
            d.ellipse([fp[0] - r / 2, fp[1] - r / 2, fp[0] + r / 2, fp[1] + r / 2], fill=(0, 0, 0))

def bbox_of(c, pad_frac=0.12, min_span=900):
    xs = [p[0] for p in c['poly']]; zs = [p[1] for p in c['poly']]
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    span = max(x1 - x0, z1 - z0, min_span) * (1 + 2 * pad_frac)
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    return cx - span / 2, cz - span / 2, cx + span / 2, cz + span / 2

def quick(edges, waters, blds, cands, path, cols=4, cell=640):
    rows = (len(cands) + cols - 1) // cols
    img = Image.new('RGB', (cols * cell, rows * (cell + 60)), BG)
    d = ImageDraw.Draw(img)
    for i, c in enumerate(cands):
        ox, oy = (i % cols) * cell, (i // cols) * (cell + 60)
        v = View(*bbox_of(c), cell, cell)
        sub = Image.new('RGB', (cell, cell), BG)
        draw_base(sub, 0, 0, v, edges, waters, blds)
        draw_route(sub, 0, 0, v, c, PALETTE[i % len(PALETTE)], 5)
        img.paste(sub, (ox, oy))
        d.text((ox + 8, oy + cell + 4), f"{c['id']} {c['name']}  {c['length']/1000:.2f} km  corners {c['corners']}", fill=(230, 230, 230), font=font(18))
        d.text((ox + 8, oy + cell + 30), (' > '.join(c['names']))[:90], fill=(170, 170, 170), font=font(13, False))
        # scale bar 500 m
        L = 500 * v.s
        d.line([(ox + 10, oy + cell - 14), (ox + 10 + L, oy + cell - 14)], fill=(230, 230, 230), width=3)
        d.text((ox + 14 + L, oy + cell - 24), '500 m', fill=(230, 230, 230), font=font(14, False))
    img.save(path)
    print('wrote', path, img.size)

LAT0, LON0 = 35.18456015184093, -80.81770185962013
M_LAT = 111132.0
M_LON = 111320.0 * math.cos(LAT0 * math.pi / 180)
def ll2xz(lat, lon):
    return ((lon - LON0) * M_LON, (lat - LAT0) * M_LAT)

def district(edges, waters, blds, lat, lon, span, path, size=1400, minrank=1):
    x, z = ll2xz(lat, lon)
    v = View(x - span / 2, z - span / 2, x + span / 2, z + span / 2, size, size)
    img = Image.new('RGB', (size, size), BG)
    draw_base(img, 0, 0, v, edges, waters, blds)
    d = ImageDraw.Draw(img)
    placed = []
    f = font(12, False)
    for rank, flags, width, pts, bb, name in sorted(edges, key=lambda e: -e[0]):
        if not name or rank < minrank or not v.inside(bb): continue
        L = sum(math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]) for k in range(1, len(pts)))
        if L < 120: continue
        mx, mz = pts[len(pts) // 2]
        px, py = v.p(mx, mz)
        if not (0 <= px < size and 0 <= py < size): continue
        tb = d.textbbox((px, py), name, font=f)
        box = (tb[0] - 2, tb[1] - 1, tb[2] + 2, tb[3] + 1)
        if any(not (box[2] < b[0] or box[0] > b[2] or box[3] < b[1] or box[1] > b[3]) for b in placed): continue
        placed.append(box)
        d.rectangle(box, fill=(0, 0, 0))
        d.text((px, py), name, fill=(255, 220, 120) if rank >= 2 else (200, 200, 200), font=f)
    L = 500 * v.s
    d.line([(10, size - 14), (10 + L, size - 14)], fill=(230, 230, 230), width=3)
    img.save(path)
    print('wrote', path)

if __name__ == '__main__':
    edges, waters, blds, cands = load()
    mode = sys.argv[1] if len(sys.argv) > 1 else 'quick'
    if mode == 'district':
        district(edges, waters, blds, float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]), sys.argv[5], minrank=int(sys.argv[6]) if len(sys.argv) > 6 else 1)
    if mode == 'quick':
        sel = sys.argv[3].split(',') if len(sys.argv) > 3 else None
        if sel: cands = [c for c in cands if c['id'] in sel]
        quick(edges, waters, blds, cands, sys.argv[2] if len(sys.argv) > 2 else os.path.join(OUT, 'quick.png'))
