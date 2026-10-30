"""stations_sheet.py - the map sheet of Charlotte's filling stations (city fuel r1).

    node tools/city/stations.mjs --json <stations.json>
    py tools/city/stations_sheet.py <stations.json> <out.png>

One overview of the whole beltway box - every station over the road network,
coloured by class - and eight close-ups, chosen from the table by kind (uptown,
an arterial strip, a highway exit, a suburb, a corner lot, a truck stop, a fuel
point in a big store's lot, the tightest fit), each drawn at 0.5 m a pixel: the
roads at their drawn width, the footprints the city draws (grey), the ones the
model stands in place of (red), OSM's own fuel outlines and points (orange),
and the placed station - its paved lot, canopy (pumps along the frontage),
shop, restrooms, pylon - with its driveways' curb cuts (yellow).

Reads Assets/PSXRacing/Resources/charlotte_city.bytes and charlotte_bld.bytes
(the shipped data), the way tools/city/lib/citydata.mjs does. No street names
other than the map's own road names; no business names or brands anywhere.
"""
import json
import math
import os
import struct
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources'))
FONT = os.path.join(HERE, 'fonts', 'Overpass-Variable.ttf')
LANE_M = 3.6576


def font(size, bold=False):
    f = ImageFont.truetype(FONT, size)
    try:
        f.set_variation_by_name('Bold' if bold else 'Regular')
    except Exception:
        pass
    return f


def read_str(b, p):
    n, shift = 0, 0
    while True:
        c = b[p]; p += 1
        n |= (c & 0x7F) << shift; shift += 7
        if not c & 0x80:
            break
    return b[p:p + n].decode('utf-8'), p + n


def profile_width(rank, link, oneway, lanes, turn):
    """citydata.mjs profileFor: the painted width the game draws."""
    clamp = lambda v, a, b: max(a, min(b, v))
    if not oneway:
        n = clamp(lanes, 2, 6)
        if turn and n % 2 == 0 and n < 6:
            n += 1
        return n * LANE_M + 0.6
    if link:
        if rank >= 4:
            return clamp(lanes, 1, 3) * LANE_M + 2.4
        return clamp(lanes, 1, 4) * LANE_M + 0.6
    if rank >= 4 and lanes <= 1:
        return LANE_M + 2.4
    if rank >= 5:
        return clamp(lanes, 2, 6) * LANE_M + 4.2
    if rank == 4:
        return clamp(lanes, 2, 4) * LANE_M + 3.3
    return clamp(lanes, 1, 4) * LANE_M + 0.6


def parse_city(path):
    b = open(path, 'rb').read()
    magic, ver, nsec = struct.unpack_from('<IiI', b, 0)
    assert magic == 0x43585350 and ver == 2, 'charlotte_city.bytes: PSXC v2 expected'
    tab, p = {}, 12
    for _ in range(nsec):
        tag = b[p:p + 4].decode('latin1'); off, ln = struct.unpack_from('<II', b, p + 4); p += 12
        tab[tag] = (off, ln)
    p = tab['NAME'][0]
    (nn,) = struct.unpack_from('<I', b, p); p += 4
    names = []
    for _ in range(nn):
        s, p = read_str(b, p); names.append(s)
    p = tab['EDGE'][0]
    (ne,) = struct.unpack_from('<I', b, p); p += 4
    edges = []
    for _ in range(ne):
        a, bb, name, rank, flags, lanes, level, w, shl, shr, speed, way, n = struct.unpack_from('<IIIBBBbfffBIH', b, p)
        p += struct.calcsize('<IIIBBBbfffBIH')
        edges.append({'name': names[name], 'rank': rank, 'link': bool(flags & 1), 'oneway': bool(flags & 2), 'bridge': bool(flags & 4),
                      'tunnel': bool(flags & 8), 'turn': bool(flags & 16), 'lanes': max(1, lanes), 'n': n})
    p = tab['PNTS'][0] + 4
    for e in edges:
        pts = struct.unpack_from('<%df' % (2 * e['n']), b, p); p += 8 * e['n']
        e['pts'] = [(pts[2 * k], pts[2 * k + 1]) for k in range(e['n'])]
        e['w'] = profile_width(e['rank'], e['link'], e['oneway'], e['lanes'], e['turn'])
    # the parking lots (section LOTS; export_osm.mjs writes it): their rings only
    lots = []
    if 'LOTS' in tab:
        p = tab['LOTS'][0]
        (head,) = struct.unpack_from('<I', b, p); p += 4
        n, v2 = head & 0x7FFFFFFF, bool(head & 0x80000000)
        for _ in range(n):
            ox, oz, m = struct.unpack_from('<ffH', b, p); p += 10
            q = struct.unpack_from('<%dh' % (2 * m), b, p); p += 4 * m
            lots.append([(ox + q[2 * k] * 0.05, oz + q[2 * k + 1] * 0.05) for k in range(m)])
            p += 4
            (runs,) = struct.unpack_from('<H', b, p); p += 2
            p += runs * (9 if v2 else 5)
            if v2:
                p += 1
                (nh,) = struct.unpack_from('<H', b, p); p += 2
                for _ in range(nh):
                    p += 1
                    (hm,) = struct.unpack_from('<H', b, p); p += 2 + 4 * hm
    edges_lots[:] = lots
    return edges


def parse_bld(path):
    b = open(path, 'rb').read()
    magic, ver = struct.unpack_from('<Ii', b, 0)
    assert magic == 0x444C4250
    p = 8 + 16
    (n,) = struct.unpack_from('<I', b, p); p += 4
    out = []
    for _ in range(n):
        sb = b[p]; p += 1
        p += 4   # h
        if ver >= 2:
            lb = b[p]; p += 1
            if lb & 0x80:
                p += 3
        part = False
        if ver >= 3:
            xb = b[p]; p += 1
            part = bool(xb & 2)
            if xb & 0x80:
                p += 2 + 2 + 2 + 1 + 4
        np_ = b[p]; p += 1
        pts = struct.unpack_from('<%df' % (2 * np_), b, p); p += 8 * np_
        out.append({'pts': [(pts[2 * k], pts[2 * k + 1]) for k in range(np_)], 'house': (sb & 0x7F) == 3 or bool(sb & 0x80), 'part': part})
    return out


edges_lots = []
CLS_COL = {'forecourt': (40, 110, 220), 'truck': (232, 120, 20), 'bigbox': (30, 160, 70)}
PART_COL = {'canopy': (70, 140, 230), 'truck': (232, 140, 40), 'shop': (150, 95, 60), 'wc': (120, 120, 160), 'dumpster': (60, 90, 60), 'pylon': (230, 40, 160)}


def overview(J, edges, W=2000):
    xs = [s['x'] for s in J['stations']] + [s['x'] for s in J['skipped']]
    zs = [s['z'] for s in J['stations']] + [s['z'] for s in J['skipped']]
    x0, x1, z0, z1 = min(xs) - 1500, max(xs) + 1500, min(zs) - 1500, max(zs) + 1500
    sc = W / (x1 - x0)
    H = int((z1 - z0) * sc)
    im = Image.new('RGB', (W, H), (246, 244, 238))
    d = ImageDraw.Draw(im)
    P = lambda x, z: ((x - x0) * sc, (z1 - z) * sc)
    for e in sorted(edges, key=lambda e: e['rank']):
        r = e['rank']
        col = (205, 205, 205) if r == 0 else (170, 170, 170) if r <= 2 else (120, 120, 120) if r <= 4 else (70, 70, 80)
        wd = 1 if r <= 2 else 2 if r <= 4 else 3
        if e['link']:
            col, wd = (110, 110, 120), 1
        d.line([P(x, z) for x, z in e['pts']], fill=col, width=wd)
    ux, uz = J['uptown']
    d.ellipse([P(ux, uz)[0] - 5, P(ux, uz)[1] - 5, P(ux, uz)[0] + 5, P(ux, uz)[1] + 5], outline=(0, 0, 0), width=2)
    for s in J['skipped']:
        x, y = P(s['x'], s['z'])
        d.line([x - 6, y - 6, x + 6, y + 6], fill=(200, 0, 0), width=3); d.line([x - 6, y + 6, x + 6, y - 6], fill=(200, 0, 0), width=3)
    for s in J['stations']:
        x, y = P(s['x'], s['z'])
        col = CLS_COL[s['cls']] if s['placed'] else (220, 0, 220)
        r = 6 if s['cls'] != 'forecourt' else 4
        d.ellipse([x - r, y - r, x + r, y + r], fill=col, outline=(20, 20, 20))
    return im, (x0, x1, z0, z1, sc)


def closeup(J, edges, bld, st, title, half=110.0, px=0.5):
    S = int(2 * half / px)
    im = Image.new('RGB', (S, S), (226, 232, 214))
    d = ImageDraw.Draw(im, 'RGBA')
    cx, cz = sum(p[0] for p in st['lot']) / 4.0, sum(p[1] for p in st['lot']) / 4.0
    P = lambda x, z: ((x - cx + half) / px, (cz + half - z) / px)
    x0, x1, z0, z1 = cx - half - 60, cx + half + 60, cz - half - 60, cz + half + 60

    def hits(pts):
        """Does a polyline's box meet the window (a long segment's ends may both lie outside)?"""
        return not (max(p[0] for p in pts) < x0 or min(p[0] for p in pts) > x1 or max(p[1] for p in pts) < z0 or min(p[1] for p in pts) > z1)
    # the parking lots the city paves
    for r in edges_lots:
        if not hits(r):
            continue
        d.polygon([P(x, z) for x, z in r], fill=(112, 114, 122), outline=(80, 80, 88))
    # roads at their drawn width (pavement), centreline
    for e in sorted(edges, key=lambda e: e['rank']):
        if not hits(e['pts']):
            continue
        w = max(1, int(e['w'] / px))
        d.line([P(x, z) for x, z in e['pts']], fill=(92, 92, 96) if not e['bridge'] else (120, 110, 100), width=w, joint='curve')
    for e in edges:
        if not hits(e['pts']):
            continue
        d.line([P(x, z) for x, z in e['pts']], fill=(230, 200, 60), width=1)
    # every station near (theirs too)
    near = [s for s in J['stations'] if abs(s['x'] - cx) < half + 80 and abs(s['z'] - cz) < half + 80]
    for s in near:
        if s['placed']:
            d.polygon([P(x, z) for x, z in s['lot']], fill=(70, 70, 74, 110), outline=(255, 255, 255))
    # footprints the city draws
    hid = set()
    for s in near:
        for h in s['hide']:
            hid.add((round(h['x'], 1), round(h['z'], 1)))
    for f in bld:
        if f['part'] or not f['pts']:
            continue
        fx = sum(p[0] for p in f['pts']) / len(f['pts']); fz = sum(p[1] for p in f['pts']) / len(f['pts'])
        if not (x0 <= fx <= x1 and z0 <= fz <= z1):
            continue
        d.polygon([P(x, z) for x, z in f['pts']], fill=(196, 194, 188, 230) if not f['house'] else (214, 196, 170, 230), outline=(110, 110, 110))
    for s in near:
        for h in s['hide']:
            d.polygon([P(x, z) for x, z in h['ring']], fill=(220, 50, 50, 90), outline=(220, 30, 30))
            a, b = P(*h['ring'][0]), P(*h['ring'][len(h['ring']) // 2])
            d.line([a, b], fill=(220, 30, 30), width=2)
    # OSM's own fuel outlines and points
    for s in near:
        for r in s['osmRings']:
            d.line([P(x, z) for x, z in r] + [P(*r[0])], fill=(255, 140, 0), width=3)
        for x, z in s['osmPoints']:
            X, Y = P(x, z); d.ellipse([X - 6, Y - 6, X + 6, Y + 6], outline=(255, 140, 0), width=3)
    # the stations' parts and driveways
    for s in near:
        if not s['placed']:
            continue
        for q in s['parts']:
            d.polygon([P(x, z) for x, z in q['ring']], fill=PART_COL[q['kind']] + (210,), outline=(10, 10, 10))
        for k in s.get('links', []):
            dx, dz = k['bx'] - k['ax'], k['bz'] - k['az']; L = math.hypot(dx, dz) or 1
            nx, nz = -dz / L * k['half'], dx / L * k['half']
            d.polygon([P(k['ax'] + nx, k['az'] + nz), P(k['bx'] + nx, k['bz'] + nz), P(k['bx'] - nx, k['bz'] - nz), P(k['ax'] - nx, k['az'] - nz)], fill=(250, 220, 40, 170), outline=(120, 100, 0))
        for dv in s['drives']:
            nx, nz = dv['dx'], dv['dz']           # the way in
            tx, tz = nz, -nx                     # across the mouth
            pts = []
            for a, b in ((-dv['half'], 0), (dv['half'], 0), (dv['half'], dv['len']), (-dv['half'], dv['len'])):
                pts.append(P(dv['mx'] + tx * a + nx * b, dv['mz'] + tz * a + nz * b))
            d.polygon(pts, fill=(250, 220, 40, 170), outline=(120, 100, 0))
    # scale bar and caption (two lines: what, where - fitted, never clipped)
    lines = title.split('\n')
    d.rectangle([0, 0, S, 24 * len(lines) + 6], fill=(20, 20, 20, 210))
    for k, ln in enumerate(lines):
        size = 20 if k == 0 else 18
        f = font(size, k == 0)
        while size > 10 and d.textlength(ln, font=f) > S - 12:
            size -= 1; f = font(size, k == 0)
        d.text((6, 3 + 24 * k), ln, fill=(255, 255, 255), font=f)
    d.rectangle([10, S - 22, 10 + 20 / px, S - 16], fill=(255, 255, 255), outline=(0, 0, 0))
    d.text((14 + 20 / px, S - 28), '20 m', fill=(0, 0, 0), font=font(16))
    return im


def pick(J, edges=None):
    """The close-ups: one station of each kind the brief asks to see."""
    st = [s for s in J['stations'] if s['placed']]
    ramp_ends = [p for e in (edges or []) if e['link'] and e['rank'] >= 4 for p in (e['pts'][0], e['pts'][-1])]

    def ramp_gap(s):
        return min((math.hypot(p[0] - s['x'], p[1] - s['z']) for p in ramp_ends), default=1e9)
    ux, uz = J['uptown']
    dist = lambda s: math.hypot(s['x'] - ux, s['z'] - uz)
    out, used = [], set()

    def take(label, cands, key):
        cands = [s for s in cands if id(s) not in used]
        if not cands:
            return
        s = min(cands, key=key)
        used.add(id(s)); out.append((label, s))
    take('UPTOWN', st, dist)
    # an arterial strip: the station with the most other stations within 600 m on an arterial
    def dens(s):
        return -sum(1 for o in st if o is not s and math.hypot(o['x'] - s['x'], o['z'] - s['z']) < 600)
    take('ARTERIAL STRIP', [s for s in st if s['frontRank'] >= 3 and s['cls'] == 'forecourt'], dens)
    take('HIGHWAY EXIT', [s for s in st if s['cls'] == 'forecourt' and s['modules'] >= 2], ramp_gap)
    take('SUBURB', [s for s in st if s['cls'] == 'forecourt' and s['frontRank'] <= 2], lambda s: -dist(s))
    take('CORNER LOT', [s for s in st if s['corner'] and s['cls'] == 'forecourt' and s['modules'] >= 2], lambda s: dist(s))
    take('TRUCK STOP', [s for s in st if s['cls'] == 'truck'], lambda s: dist(s))
    take('BIG STORE FUEL', [s for s in st if s['cls'] == 'bigbox'], lambda s: dist(s))
    take('SHOP BESIDE', [s for s in st if s['layout'] == 'shop beside'], lambda s: dist(s))
    return out


def qa_grid(J, edges, bld, out, n, cols=6, which=None):
    """A QA sheet: n stations (every k-th of the table, or those named) as close-ups."""
    st = J['stations'] if which is None else which
    k = max(1, len(st) // n) if which is None else 1
    pickd = st[::k][:n]
    cw = 400
    rows = (len(pickd) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * cw, rows * cw), (255, 255, 255))
    for i, s in enumerate(pickd):
        lab = '%d %s %s%s - %s' % (J['stations'].index(s), s['cls'], s['layout'] if s['placed'] else 'NOT PLACED', ' x%d' % s['modules'], s['place'])
        im = closeup(J, edges, bld, s, lab[:70])
        sheet.paste(im.resize((cw, cw), Image.LANCZOS), ((i % cols) * cw, (i // cols) * cw))
    sheet.save(out)
    print('wrote', out, sheet.size)


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    J = json.load(open(sys.argv[1]))
    edges = parse_city(os.path.join(RES, 'charlotte_city.bytes'))
    bld = parse_bld(os.path.join(RES, 'charlotte_bld.bytes'))
    if '--grid' in sys.argv:
        i = sys.argv.index('--grid')
        n, off = int(sys.argv[i + 1]), int(sys.argv[i + 2]) if len(sys.argv) > i + 2 and sys.argv[i + 2].isdigit() else 0
        st = J['stations'][off:] if off else None
        qa_grid(J, edges, bld, sys.argv[2], n, which=st[::max(1, len(st) // n)][:n] if st else None)
        return 0
    if '--idx' in sys.argv:
        ids = json.load(open(sys.argv[sys.argv.index('--idx') + 1]))
        qa_grid(J, edges, bld, sys.argv[2], len(ids), which=[J['stations'][i] for i in ids])
        return 0
    if '--cls' in sys.argv:
        c = sys.argv[sys.argv.index('--cls') + 1]
        qa_grid(J, edges, bld, sys.argv[2], 48, which=[s for s in J['stations'] if s['cls'] == c or (c == 'tight' and s.get('tight')) or (c == 'nodrive' and s['placed'] and not s['drives'])][:48])
        return 0
    ov, _ = overview(J, edges)
    cl = [closeup(J, edges, bld, s, '%s: %s%s\n%s' % (label, s['layout'], ' x%d' % s['modules'] if s['modules'] > 1 else '', s['place'])) for label, s in pick(J, edges)]
    cw = 440
    rows = (len(cl) + 1) // 2
    H = max(ov.size[1], rows * cw) + 120
    sheet = Image.new('RGB', (ov.size[0] + 2 * cw + 30, H), (255, 255, 255))
    sheet.paste(ov, (0, 120))
    for i, im in enumerate(cl):
        sheet.paste(im.resize((cw, cw), Image.LANCZOS), (ov.size[0] + 20 + (i % 2) * (cw + 5), 120 + (i // 2) * (cw + 5)))
    d = ImageDraw.Draw(sheet)
    cnt = {}
    for s in J['stations']:
        k = s['cls'] if s['placed'] else 'not placed'
        cnt[k] = cnt.get(k, 0) + 1
    d.text((16, 10), 'CHARLOTTE FILLING STATIONS - city fuel r1 (OSM amenity=fuel, beltway box; the owner\'s gas-station model)', fill=(0, 0, 0), font=font(30, True))
    line = '   '.join('%s %d' % (k, v) for k, v in sorted(cnt.items())) + '   skipped %d' % len(J['skipped'])
    d.text((16, 50), line, fill=(0, 0, 0), font=font(22))
    lx = 16
    for label, col in (('forecourt', CLS_COL['forecourt']), ('truck stop', CLS_COL['truck']), ('big-store fuel', CLS_COL['bigbox']), ('not placed', (220, 0, 220)), ('skipped', (200, 0, 0))):
        d.ellipse([lx, 86, lx + 16, 102], fill=col, outline=(0, 0, 0)); d.text((lx + 22, 82), label, fill=(0, 0, 0), font=font(20)); lx += 200
    d.text((ov.size[0] + 20, 74), 'close-ups, 220 m across: the lot (grey), canopy (blue: pumps along the frontage), shop (brown),', fill=(0, 0, 0), font=font(16))
    d.text((ov.size[0] + 20, 96), 'restrooms, pylon (pink), driveways (yellow); OSM fuel (orange); footprints replaced (red)', fill=(0, 0, 0), font=font(16))
    # the lists: misfits, skipped, the layouts
    y = 120 + ((len(cl) + 1) // 2) * (cw + 5) + 10
    tx = ov.size[0] + 20
    def put(t, size=17, bold=False):
        nonlocal y
        d.text((tx, y), t, fill=(0, 0, 0), font=font(size, bold)); y += size + 7
    lay = {}
    for s in J['stations']:
        if s['placed']:
            k = s['layout'] + (' x%d' % s['modules'] if s['modules'] > 1 else '')
            lay[k] = lay.get(k, 0) + 1
    put('LAYOUTS (each also mirrored):', 18, True)
    items = sorted(lay.items(), key=lambda kv: -kv[1])
    for i in range(0, len(items), 3):
        put('   '.join('%s %d' % kv for kv in items[i:i + 3]))
    mis = [s for s in J['stations'] if s['placed'] and s.get('displaced', 0) > (45 if s['cls'] == 'truck' else 25)]
    put("MISFITS - placed, but over 25 m from OSM's own (%d):" % len(mis), 18, True)
    for s in mis:
        put('  %s (%d m)' % (s['place'], round(s['displaced'])))
    put('SKIPPED (%d):' % len(J['skipped']), 18, True)
    for s in J['skipped']:
        put('  %s: %s' % (s.get('place', '(%d, %d)' % (round(s['x']), round(s['z']))), s['why'].split(' (')[0].split(':')[0]))
    put('replaced footprints %d; driveways %d; corner lots %d' % (sum(len(s['hide']) for s in J['stations']), sum(len(s['drives']) for s in J['stations']), sum(1 for s in J['stations'] if s['corner'])))
    sheet.save(sys.argv[2])
    print('wrote', sys.argv[2], sheet.size)


if __name__ == '__main__':
    sys.exit(main())
