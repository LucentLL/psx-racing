# sheet.py - the candidate sheet: whole city with every candidate numbered, a zoomed panel each, the table
import json, math, os, sys, textwrap
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from render import load, View, draw_base, draw_route, label_disc, font, BG, PALETTE, ll2xz, OUT

W = 3600
COLS = 6
PANEL = 590
GAP = 10
CAP = 150
TXT = (232, 232, 236)
DIM = (160, 164, 172)
ACC = (255, 196, 64)

def kind_word(c):
    return {'circuit': 'CIRCUIT', 'sprint': 'SPRINT', 'drag': 'DRAG'}[c['kind']]

def race_m(c):
    if c['kind'] == 'circuit': return c['length']
    if c['kind'] == 'drag': return c['runM']
    return c['length'] - (c.get('leadM') or 60) - (c.get('shutM') or 250)

def laps(c):
    L = c['length'] / 1000.0
    n = max(1, min(5, int(13.0 / L)))
    while n > 1 and n * L > 14.2: n -= 1
    return n

def status(c):
    bad = [f['label'] for f in c.get('faults', []) if f.get('hard')]
    surf = sorted(set(h['kind'] for h in c.get('auditHits', []) if h['kind'] in ('DECK-HOLE', 'STEP', 'STEP-JOINT', 'KINK', 'KINK-JOINT', 'UNDER-STEP', 'UNDER-KINK', 'WALL-IN-LANE', 'RAIL-ON-LANE')))
    if bad:
        return 'NEEDS THE DECK FIX: ' + bad[0].split(' (')[0]
    if surf:
        return 'NEEDS A JOINT FIX: ' + ', '.join(surf)
    return 'DRIVABLE NOW'

def colour(i):
    return PALETTE[i % len(PALETTE)]

def wrap(d, text, f, width):
    words = text.split(); lines = []; cur = ''
    for w in words:
        t = (cur + ' ' + w).strip()
        if d.textlength(t, font=f) <= width: cur = t
        else:
            if cur: lines.append(cur)
            cur = w
    if cur: lines.append(cur)
    return lines

def route_label_points(c, v, ox, oy):
    pts = c['poly']
    n = len(pts)
    cands = [pts[int(n * k / 10) % n] for k in (3, 5, 7, 1, 9, 2, 8, 4, 6, 0)]
    return [(ox + v.p(x, z)[0], oy + v.p(x, z)[1]) for x, z in cands]

def place_labels(d, cands, v, ox, oy, placed, r=17, fs=17, box=None):
    for i, c in cands:
        opts = route_label_points(c, v, ox, oy)
        pick = None
        for (px, py) in opts:
            if box and not (box[0] + r < px < box[2] - r and box[1] + r < py < box[3] - r): continue
            if all(math.hypot(px - qx, py - qy) > 2.3 * r for qx, qy in placed):
                pick = (px, py); break
        if pick is None:
            # push outward from the first option until free
            px, py = opts[0]
            for k in range(1, 30):
                ang = k * 2.4
                qx, qy = px + math.cos(ang) * r * 1.2 * k ** 0.5 * 1.6, py + math.sin(ang) * r * 1.2 * k ** 0.5 * 1.6
                if all(math.hypot(qx - a, qy - b) > 2.3 * r for a, b in placed):
                    d.line([(px, py), (qx, qy)], fill=colour(i), width=2)
                    pick = (qx, qy); break
            if pick is None: pick = opts[0]
        placed.append(pick)
        label_disc(d, pick, c['id'], colour(i), r=r, fs=fs)

def main():
    edges, waters, blds, cands = load()
    order = sorted(range(len(cands)), key=lambda i: ('CHSXD'.index(cands[i]['id'][0]), int(cands[i]['id'][1:])))
    cands = [cands[i] for i in order]
    lang = json.load(open(os.path.join(OUT, 'langston.json'))) if os.path.exists(os.path.join(OUT, 'langston.json')) else None
    npan = len(cands) + (1 if lang else 0)
    rows = (npan + COLS - 1) // COLS
    MAPH = 2400
    TABLE_ROW = 44
    H = 150 + MAPH + 40 + rows * (PANEL + CAP + GAP) + 120 + (len(cands) + 2) * TABLE_ROW + 260
    img = Image.new('RGB', (W, H), BG)
    d = ImageDraw.Draw(img)
    # ---------------------------------------------------------------- header
    d.text((40, 28), 'CHARLOTTE RACE TRACK CANDIDATES  -  2026-11-16', fill=TXT, font=font(54))
    d.text((40, 96), 'Real Charlotte streets, routed on the game\'s own road graph (charlotte_city.bytes, graph b2aa3453 = the PC47 code). '
           'Pick by number.  C = circuit   H = freeway-and-ramp circuit   S = sprint   X = cut of today\'s sprint   D = drag.  '
           'Checkered square = start line, ring = finish, arrows = direction.', fill=DIM, font=font(24, False))
    # ---------------------------------------------------------------- whole city
    y0 = 150
    # the whole graph's square, cropped to the 485 belt
    x0, z0 = ll2xz(35.010, -81.040); x1, z1 = ll2xz(35.430, -80.610)
    v = View(x0, z0, x1, z1, MAPH, MAPH)
    sub = Image.new('RGB', (MAPH, MAPH), BG)
    draw_base(sub, 0, 0, v, edges, waters, blds, detail=False)
    sd = ImageDraw.Draw(sub)
    for i, c in enumerate(cands):
        draw_route(sub, 0, 0, v, c, colour(i), 5 if c['kind'] != 'drag' else 7, arrows=False, start=False)
    # inset frame on the big map
    ix0, iz0 = ll2xz(35.180, -80.880); ix1, iz1 = ll2xz(35.262, -80.780)
    a = v.p(ix0, iz1); b = v.p(ix1, iz0)
    sd.rectangle([a[0], a[1], b[0], b[1]], outline=(220, 220, 220), width=2)
    sd.text((a[0] + 6, a[1] - 26), 'CENTRAL (right)', fill=(220, 220, 220), font=font(18))
    placed = []
    outside = [(i, c) for i, c in enumerate(cands) if not all(ix0 < p[0] < ix1 and iz0 < p[1] < iz1 for p in c['poly'][::5])]
    place_labels(sd, outside, v, 0, 0, placed, r=19, fs=17)
    # scale bar 5 km
    L = 5000 * v.s
    sd.line([(30, MAPH - 30), (30 + L, MAPH - 30)], fill=TXT, width=4)
    sd.text((40 + L, MAPH - 44), '5 km', fill=TXT, font=font(22))
    sd.text((30, 24), 'THE WHOLE CITY (I-485 belt)', fill=TXT, font=font(30))
    for name, lat, lon in [('UPTOWN', 35.2271, -80.8431), ('UNIVERSITY', 35.3070, -80.7330), ('BALLANTYNE', 35.0480, -80.8480),
                           ('SOUTHPARK', 35.1500, -80.8200), ('AIRPORT', 35.2140, -80.9450), ('MATTHEWS', 35.1170, -80.7230)]:
        px, py = v.p(*ll2xz(lat, lon))
        sd.text((px + 14, py + 10), name, fill=(200, 200, 205), font=font(20))
    img.paste(sub, (0, y0))
    # inset: central Charlotte
    IN = W - MAPH - 20
    vi = View(ix0, iz0, ix1, iz1, IN, IN)
    ins = Image.new('RGB', (IN, IN), BG)
    draw_base(ins, 0, 0, vi, edges, waters, blds, detail=True)
    for i, c in enumerate(cands):
        draw_route(ins, 0, 0, vi, c, colour(i), 6 if c['kind'] != 'drag' else 8, arrows=False, start=False)
    idr = ImageDraw.Draw(ins)
    inside = [(i, c) for i, c in enumerate(cands) if any(ix0 < p[0] < ix1 and iz0 < p[1] < iz1 for p in c['poly'][::5])]
    place_labels(idr, inside, vi, 0, 0, [], r=19, fs=17, box=(0, 0, IN, IN))
    L = 1000 * vi.s
    idr.line([(20, IN - 24), (20 + L, IN - 24)], fill=TXT, width=4)
    idr.text((28 + L, IN - 38), '1 km', fill=TXT, font=font(20))
    idr.text((16, 12), 'CENTRAL CHARLOTTE', fill=TXT, font=font(26))
    img.paste(ins, (MAPH + 20, y0))
    # legend under the inset
    ly = y0 + IN + 20
    d.text((MAPH + 30, ly), 'WHAT WAS CHECKED (every candidate)', fill=ACC, font=font(26)); ly += 40
    notes = [
        'Routed edge by edge on the shipped city graph, one-way streets driven the legal way, no U-turns (no turn over 120 degrees at a junction).',
        'Clear of every place HANDOFF.md says to keep race routes off: the four single-point-interchange decks (I-85 x Freedom Dr, I-77 x Tyvola, I-85 x Brookshire, the airport ramps), the 8 single joints, and the owner\'s 4 Beatties Ford spots: 0 hits, except H1 (your loop), which turns round on the airport ramps at Little Rock Road.',
        'Freeway laps (H): carriageways driven in their own direction, every turnaround on the interchange\'s own ramps, and every deck on the loop checked against the bridges audit\'s worst lists (steps, kinks, holes, walls in lanes).',
        'Start: a straight of at least 90 m with two lanes in the race direction for the two-abreast grid (8 cars = 28.5 m); a drag needs 11.6 m or more for four abreast.',
        'Heights are the game\'s measured road profiles (RPRF). Lanes and widths are the game\'s own road profiles (3.66 m lanes).',
        'Corners: t = tight (radius under 35 m: a junction turn), m = 35-120 m, f = fast (120-800 m).',
        'Side streets = junction mouths to close for a sanctioned (no traffic) race.',
    ]
    f = font(21, False)
    for n in notes:
        for line in wrap(d, n, f, IN - 40):
            d.text((MAPH + 34, ly), line, fill=TXT, font=f); ly += 28
        ly += 8
    # ---------------------------------------------------------------- panels
    py0 = y0 + MAPH + 40
    allp = list(enumerate(cands))
    for k, (i, c) in enumerate(allp):
        cx = (k % COLS) * (PANEL + GAP) + 6
        cy = py0 + (k // COLS) * (PANEL + CAP + GAP)
        xs = [p[0] for p in c['poly']]; zs = [p[1] for p in c['poly']]
        span = max(max(xs) - min(xs), max(zs) - min(zs), 900 if c['kind'] != 'drag' else 1100) * 1.22
        mx, mz = (max(xs) + min(xs)) / 2, (max(zs) + min(zs)) / 2
        vp = View(mx - span / 2, mz - span / 2, mx + span / 2, mz + span / 2, PANEL, PANEL)
        pim = Image.new('RGB', (PANEL, PANEL), BG)
        draw_base(pim, 0, 0, vp, edges, waters, blds, detail=True)
        draw_route(pim, 0, 0, vp, c, colour(i), 6)
        pd = ImageDraw.Draw(pim)
        label_disc(pd, (30, 30), c['id'], colour(i), r=24, fs=22)
        Ls = 500 if span < 4000 else 1000
        L = Ls * vp.s
        pd.line([(14, PANEL - 18), (14 + L, PANEL - 18)], fill=TXT, width=3)
        pd.text((20 + L, PANEL - 30), f'{Ls} m', fill=TXT, font=font(16, False))
        img.paste(pim, (cx, cy))
        d.rectangle([cx - 1, cy - 1, cx + PANEL, cy + PANEL], outline=(60, 64, 72))
        ty = cy + PANEL + 6
        d.text((cx, ty), f"{c['id']}  {c['name']}", fill=colour(i), font=font(21)); ty += 28
        rk = race_m(c) / 1000.0
        if c['id'].startswith('H'):
            line2 = f"{rk:.2f} km lap x {laps(c)}  |  {status(c)}"
        elif c['kind'] == 'circuit':
            line2 = f"{c['district']}  |  {rk:.2f} km lap x {laps(c)}  |  corners {c['corners']}"
        elif c['kind'] == 'drag':
            line2 = f"{c['district']}  |  {int(c['runM'])} m run  |  {c['dragDev']['minLanes']} lanes, {c['width']['max']} m"
        else:
            line2 = f"{c['district']}  |  {rk:.2f} km  |  corners {c['corners']}"
        d.text((cx, ty), line2, fill=TXT, font=font(17, False)); ty += 24
        for line in wrap(d, c['fun'], font(16, False), PANEL - 4)[:3]:
            d.text((cx, ty), line, fill=DIM, font=font(16, False)); ty += 21
    if lang:
        k = len(allp)
        cx = (k % COLS) * (PANEL + GAP) + 6
        cy = py0 + (k // COLS) * (PANEL + CAP + GAP)
        draw_langston(img, cx, cy, lang)
    # ---------------------------------------------------------------- table
    ty = py0 + rows * (PANEL + CAP + GAP) + 30
    d.text((40, ty), 'THE TABLE', fill=ACC, font=font(34)); ty += 54
    cols = [('#', 70), ('TYPE', 120), ('NAME', 640), ('RACE', 250), ('CORNERS t/m/f', 220), ('LANES / DIR', 200), ('WIDTH m', 150),
            ('HEIGHT m (climb, max grade)', 380), ('SIGNALS', 120), ('SIDE STREETS', 170), ('START GRID', 400), ('STATUS', 470), ('ROADS', 360)]
    x = 40
    for name, w in cols:
        d.text((x, ty), name, fill=ACC, font=font(19)); x += w
    ty += TABLE_ROW - 10
    d.line([(40, ty - 6), (W - 40, ty - 6)], fill=(80, 84, 92), width=2)
    fr = font(19, False)
    for i, c in enumerate(cands):
        rk = race_m(c) / 1000.0
        if c['kind'] == 'circuit': race = f"{rk:.2f} km x {laps(c)} = {rk * laps(c):.1f}"
        elif c['kind'] == 'drag': race = f"{int(c['runM'])} m ({'1/4' if c['runM'] < 500 else '1/2'} mi)"
        else: race = f"{rk:.2f} km"
        g = c['grid']
        if c['kind'] == 'circuit': grid = f"{g['len']} m straight on {(g['road'] or 'the freeway').replace('Street', 'St').replace('Boulevard', 'Blvd').replace('Avenue', 'Ave').replace('Road', 'Rd')[:22]}"
        elif g.get('today'): grid = "today's start (in the game)"
        elif g.get('closedOnly'): grid = f"{g['len']} m straight, 2-lane road: closed road only"
        else: grid = f"{g['len']} m straight, {g['lanes']} lanes"
        e = c['elev']
        vals = [c['id'], kind_word(c), c['name'], race, f"{c['corners']}  ({c['tight']}/{c['medium']}/{c['fast']})",
                f"{c['lanes']['min']}-{c['lanes']['max']}  ({c['lanes']['twoPlusPct']}% 2+)", f"{c['width']['min']}-{c['width']['max']}",
                f"{e['min']}-{e['max']}  (+{e['climb']}, {e['maxGrade']}%)", str(c['signals']), str(c['mouths']), grid, status(c),
                ' > '.join(dict.fromkeys(n.replace('Street', 'St').replace('Boulevard', 'Blvd').replace('Avenue', 'Ave').replace('Road', 'Rd').replace('Parkway', 'Pkwy').replace('Expressway', 'Expy') for n in c['names']))]
        x = 40
        for (name, w), val in zip(cols, vals):
            col = colour(i) if name == '#' else TXT
            t = val
            while d.textlength(t, font=fr) > w - 12 and len(t) > 3: t = t[:-2]
            if t != val: t = t[:-1] + '…'
            d.text((x, ty), t, fill=col, font=fr); x += w
        ty += TABLE_ROW
    if lang:
        x = 40
        vals = ['D5', 'DRAG', 'LANGSTON BRIDGE 1/2 MILE (cut of today\'s 1,408 m drag)', '804 m (1/2 mi)', '0', 'stage road', f"{lang['width']}",
                f"{lang['zmin']:.0f}-{lang['zmax']:.0f} (bridge, +{lang['climb']:.0f})", '-', '-', "today's start line (152 m)", "DRIVABLE NOW (today's road)", 'Langston Bridge (coast stage)']
        for (name, w), val in zip(cols, vals):
            t = val
            while d.textlength(t, font=fr) > w - 12 and len(t) > 3: t = t[:-2]
            if t != val: t = t[:-1] + '…'
            d.text((x, ty), t, fill=TXT if name != '#' else (255, 255, 255), font=fr); x += w
        ty += TABLE_ROW
    ty += 20
    for line in ['Known faults within 220 m of a route: none for C, S, X and D (SPUI decks, airport ramps, the 8 single joints, Beatties Ford). H1 (your loop) turns round at Little Rock Road: the airport ramps, so it needs that deck fix; H2-H7 are clear.',
                 'Not checked yet (Phase 3, once picked): the drawn surface in Unity (race-play-check both ways), AI racing lines, closures at side streets, traffic for street events.']:
        d.text((40, ty), line, fill=DIM, font=font(20, False)); ty += 30
    img = img.crop((0, 0, W, ty + 30))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(OUT, 'sheet.png')
    img.save(out, optimize=True)
    print('wrote', out, img.size)

def draw_langston(img, cx, cy, lang):
    d = ImageDraw.Draw(img)
    d.rectangle([cx, cy, cx + PANEL, cy + PANEL], fill=(18, 22, 28), outline=(60, 64, 72))
    label_disc(d, (cx + 30, cy + 30), 'D5', (255, 255, 255), r=24, fs=20)
    d.text((cx + 64, cy + 18), 'LANGSTON BRIDGE - side view', fill=TXT, font=font(20))
    prof = lang['prof']  # [[m, y], ...]
    m0, m1 = 0, prof[-1][0]
    ymin, ymax = min(p[1] for p in prof) - 2, max(p[1] for p in prof) + 4
    gx0, gy0, gx1, gy1 = cx + 40, cy + 90, cx + PANEL - 20, cy + PANEL - 60
    X = lambda m: gx0 + (m - m0) / (m1 - m0) * (gx1 - gx0)
    Y = lambda y: gy1 - (y - ymin) / (ymax - ymin) * (gy1 - gy0)
    d.line([(X(p[0]), Y(p[1])) for p in prof], fill=(150, 150, 160), width=3)
    seg = [p for p in prof if lang['start'] <= p[0] <= lang['finishCut']]
    d.line([(X(p[0]), Y(p[1])) for p in seg], fill=(255, 255, 255), width=6)
    for m, lab, col in [(lang['start'], 'START', (255, 255, 255)), (lang['finishCut'], '1/2 MILE', (255, 196, 64)), (lang['finishToday'], 'TODAY', (150, 150, 160))]:
        d.line([(X(m), gy0), (X(m), gy1)], fill=col, width=2)
        d.text((X(m) + 4, gy0 - 22), lab, fill=col, font=font(15))
    d.text((gx0, gy1 + 10), '0 m', fill=DIM, font=font(15, False))
    d.text((gx1 - 60, gy1 + 10), f'{int(m1)} m', fill=DIM, font=font(15, False))
    ty = cy + PANEL + 6
    d.text((cx, ty), 'D5  LANGSTON 1/2 MILE (cut of today\'s drag)', fill=(255, 255, 255), font=font(21)); ty += 28
    d.text((cx, ty), 'Bogue Banks stage  |  804 m instead of 1,408 m', fill=TXT, font=font(17, False)); ty += 24
    d.text((cx, ty), 'Same start line; finish just over the crest of the span, so the', fill=DIM, font=font(16, False)); ty += 21
    d.text((cx, ty), 'line is out of sight until the top. Run-out down the far side.', fill=DIM, font=font(16, False))

if __name__ == '__main__':
    main()
