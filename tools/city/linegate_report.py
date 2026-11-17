# linegate_report.py - the line gate's pictures (PIL only; no matplotlib on this machine).
#
#   python -I tools/city/linegate_report.py heat  --csv city_linegate.csv --roads city_linegate_roads.tsv --out heat.png
#                                                 [--spot x,z,label] [--title "..."]
#   python -I tools/city/linegate_report.py plans --plans city_linegate_plans.tsv --csv city_linegate.csv --out plans_dir
#   python -I tools/city/linegate_report.py sheet --shots <Screenshots/City/linegate> --list places.tsv --out sheet_%02d.png
#
# heat : every failure of the CSV on the city's road map, one colour per class.
# plans: a labelled plan drawing of each place in the plans file, from the strands the gate read out of the meshes.
# sheet: contact sheets of the editor frames (plan + driver's eye), every cell labelled.
import sys, os, csv, math, argparse
from PIL import Image, ImageDraw, ImageFont

CLASS_COL = {
    'SJOG': (255, 60, 60), 'SHIFT': (255, 150, 30), 'KINK': (255, 230, 60), 'WIDTH': (60, 220, 255),
    'CONT': (255, 80, 220), 'SURFACE': (90, 230, 110), 'JUNCTION': (90, 130, 255), 'MEDIAN': (180, 110, 255),
    'STEP': (255, 255, 255), 'BARRIER': (230, 140, 120), 'BRIDGE': (60, 200, 170),
}
CLASS_TEXT = {
    'SJOG': 'S-jog: a line swings one way and back',
    'SHIFT': 'lateral shift faster than the taper for the speed',
    'KINK': 'angle point in the alignment',
    'WIDTH': 'lane under 2.7 m or over 4.6 m',
    'CONT': 'line steps sideways or ends for no reason',
    'SURFACE': 'pavement seam / stripe / double surface',
    'JUNCTION': 'junction marking or control',
    'MEDIAN': 'median or island nose',
    'STEP': 'step in the driven surface',
    'BARRIER': 'wall or barrier on or inside an edge line',
    'BRIDGE': 'implausible lanes or width on a bridge',
}
ORDER = ['SJOG', 'SHIFT', 'KINK', 'WIDTH', 'CONT', 'SURFACE', 'JUNCTION', 'MEDIAN', 'STEP', 'BARRIER', 'BRIDGE']


def font(sz, bold=False):
    for name in (('arialbd.ttf' if bold else 'arial.ttf'), 'segoeui.ttf', 'DejaVuSans.ttf'):
        try:
            return ImageFont.truetype(name, sz)
        except Exception:
            pass
    return ImageFont.load_default()


def read_fails(path):
    out = []
    with open(path, newline='', encoding='utf-8') as f:
        for r in csv.DictReader(f):
            if r.get('absorbed') == '1':
                continue
            r['x'] = float(r['x']); r['z'] = float(r['z']); r['visibility'] = float(r['visibility'] or 0)
            r['place'] = int(r['place']); r['tier'] = int(r['tier'])
            out.append(r)
    return out


def read_roads(path):
    edges = {}
    with open(path, encoding='utf-8') as f:
        next(f)
        for line in f:
            e, cls, link, seq, x, z = line.rstrip('\n').split('\t')
            d = edges.setdefault(int(e), [int(cls), int(link), []])
            d[2].append((float(x), float(z)))
    return edges


def heat(a):
    fails = read_fails(a.csv)
    roads = read_roads(a.roads)
    xs = [p[0] for e in roads.values() for p in e[2]]; zs = [p[1] for e in roads.values() for p in e[2]]
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    MW, MH = 3000, 3000
    sc = min(MW / (x1 - x0), MH / (z1 - z0))
    MW = int((x1 - x0) * sc) + 1; MH = int((z1 - z0) * sc) + 1
    PANEL = 900; PAD = 30; TOP = 150
    W = MW + PANEL + 3 * PAD; H = max(MH, 2300) + TOP + PAD
    im = Image.new('RGB', (W, H), (16, 18, 22))
    d = ImageDraw.Draw(im, 'RGBA')

    def P(x, z):
        return (PAD + (x - x0) * sc, TOP + (z1 - z) * sc)

    for cls_want in (0, 1, 2, 3, 4, 5):
        col = [(46, 48, 54), (58, 60, 68), (72, 74, 84), (92, 94, 104), (118, 120, 132), (140, 142, 156)][cls_want]
        wd = [1, 1, 1, 2, 2, 3][cls_want]
        for e in roads.values():
            if e[0] != cls_want:
                continue
            pts = [P(*p) for p in e[2]]
            if len(pts) >= 2:
                d.line(pts, fill=col, width=wd)
    # one dot per place and class (the most visible failure of the place)
    best = {}
    for r in fails:
        k = (r['class'], r['place'])
        if k not in best or r['visibility'] > best[k]['visibility']:
            best[k] = r
    places = sorted(best.values(), key=lambda r: (ORDER.index(r['class']) if r['class'] in ORDER else 99, r['visibility']), reverse=False)
    places.sort(key=lambda r: r['visibility'])
    for r in places:
        c = CLASS_COL.get(r['class'], (200, 200, 200))
        rad = 2.2 + min(6.0, 1.3 * math.sqrt(max(0.0, r['visibility'])))
        px, pz = P(r['x'], r['z'])
        d.ellipse((px - rad, pz - rad, px + rad, pz + rad), fill=c + (150,))
    if a.spot:
        for sp in a.spot:
            q = sp.split(',')
            px, pz = P(float(q[0]), float(q[1]))
            d.ellipse((px - 26, pz - 26, px + 26, pz + 26), outline=(255, 255, 255, 255), width=4)
            d.text((px + 32, pz - 16), q[2] if len(q) > 2 else 'spot', fill=(255, 255, 255, 255), font=font(30, True))
    # title, legend, totals
    d.text((PAD, 28), a.title or 'Charlotte: every road and road-line failure of the line gate', fill=(255, 255, 255), font=font(54, True))
    d.text((PAD, 96), a.sub or '', fill=(190, 195, 205), font=font(30))
    lx = MW + 2 * PAD; y = TOP + 10
    d.text((lx, y), 'FAILING PLACES BY CLASS', fill=(255, 255, 255), font=font(36, True)); y += 60
    tot = {}
    for r in places:
        t = tot.setdefault(r['class'], [0, 0, 0, 0])
        t[0] += 1; t[min(3, max(1, r['tier']))] += 1
    d.text((lx + 470, y), 'all', fill=(170, 175, 185), font=font(26)); d.text((lx + 570, y), 'T1', fill=(170, 175, 185), font=font(26))
    d.text((lx + 670, y), 'T2', fill=(170, 175, 185), font=font(26)); d.text((lx + 770, y), 'T3', fill=(170, 175, 185), font=font(26)); y += 40
    allp = 0
    for c in ORDER:
        t = tot.get(c, [0, 0, 0, 0])
        allp += t[0]
        d.ellipse((lx, y + 6, lx + 28, y + 34), fill=CLASS_COL[c] + (255,))
        d.text((lx + 44, y), c, fill=(255, 255, 255), font=font(32, True))
        d.text((lx + 470, y), str(t[0]), fill=(255, 255, 255), font=font(32, True))
        d.text((lx + 570, y), str(t[1]), fill=(220, 220, 225), font=font(30))
        d.text((lx + 670, y), str(t[2]), fill=(220, 220, 225), font=font(30))
        d.text((lx + 770, y), str(t[3]), fill=(220, 220, 225), font=font(30))
        y += 40
        d.text((lx + 44, y), CLASS_TEXT[c], fill=(160, 166, 178), font=font(22)); y += 44
    y += 10
    d.text((lx, y), f'ALL CLASSES: {allp} places', fill=(255, 255, 255), font=font(36, True)); y += 60
    for ln in ('T1 = freeways, expressways, primary roads and their ramps',
               'T2 = secondary and tertiary roads',
               'T3 = neighbourhood streets',
               'A place = the failures of one class within 25 m.',
               'Dot size = how visible it is from the driver\'s seat',
               '(speed x how far past the limit x size).'):
        d.text((lx, y), ln, fill=(170, 175, 185), font=font(24)); y += 34
    if a.note:
        y += 20
        for ln in a.note.split('|'):
            d.text((lx, y), ln, fill=(235, 220, 160), font=font(24)); y += 34
    im.save(a.out)
    print('heat map', a.out, im.size, 'places', allp)


def plans(a):
    # id -> strands
    by = {}
    with open(a.plans, encoding='utf-8') as f:
        next(f)
        for line in f:
            c = line.rstrip('\n').split('\t')
            pid = c[0]
            st = by.setdefault(pid, {}).setdefault((int(c[1]), int(c[2])), [int(c[3]), int(c[4]), int(c[5]), int(c[6]), []])
            st[4].append((float(c[8]), float(c[9])))
    fails = read_fails(a.csv) if a.csv else []
    os.makedirs(a.out, exist_ok=True)
    S = 900
    for pid, strands in by.items():
        pts = [p for st in strands.values() for p in st[4]]
        cx = sum(p[0] for p in pts) / len(pts); cz = sum(p[1] for p in pts) / len(pts)
        if a.centre and pid in a.centre:
            cx, cz = a.centre[pid]
        R = a.radius
        sc = S / (2 * R)
        im = Image.new('RGB', (S, S), (38, 40, 44))
        d = ImageDraw.Draw(im, 'RGBA')

        def P(x, z):
            return (S / 2 + (x - cx) * sc, S / 2 - (z - cz) * sc)
        # pavement: fill between nothing (strands only); fans as outlines
        for (sid, edge), st in strands.items():
            role, tk, yellow, broken, p = st
            if len(p) < 2:
                continue
            q = [P(*t) for t in p]
            if role == 9:
                d.polygon(q, outline=(120, 120, 130, 255))
            elif role in (0, 1):
                d.line(q, fill=(170, 170, 175, 255), width=2)
            else:
                col = (255, 205, 40, 255) if yellow else (245, 245, 245, 255)
                if broken:
                    for i in range(0, len(q) - 1):
                        if i % 2 == 0:
                            d.line([q[i], q[i + 1]], fill=col, width=2)
                else:
                    d.line(q, fill=col, width=2)
        for r in fails:
            if abs(r['x'] - cx) < R and abs(r['z'] - cz) < R:
                c = CLASS_COL.get(r['class'], (200, 200, 200))
                px, pz = P(r['x'], r['z'])
                d.ellipse((px - 9, pz - 9, px + 9, pz + 9), outline=c + (255,), width=3)
                d.text((px + 11, pz - 9), r['class'], fill=c + (255,), font=font(15, True))
        # 20 m scale bar, north arrow
        d.line([(20, S - 24), (20 + 20 * sc, S - 24)], fill=(255, 255, 255, 255), width=3)
        d.text((20, S - 48), '20 m', fill=(255, 255, 255, 255), font=font(16))
        d.text((S - 40, 14), 'N', fill=(255, 255, 255, 255), font=font(20, True))
        d.line([(S - 32, 60), (S - 32, 38)], fill=(255, 255, 255, 255), width=3)
        im.save(os.path.join(a.out, f'plan_{pid}.png'))
    print('plans', len(by), 'to', a.out)


def sheet(a):
    rows = []
    with open(a.list, encoding='utf-8') as f:
        for line in f:
            c = line.rstrip('\n').split('\t')
            if len(c) >= 2 and c[0] != 'id':
                rows.append(c)
    per = a.per
    CW, CH = 800, 450
    cap = 96
    n = 0
    for s0 in range(0, len(rows), per):
        part = rows[s0:s0 + per]
        cols = 2
        W = 2 * (2 * CW + 30) + 30; Hh = math.ceil(len(part) / cols) * (CH + cap + 20) + 110
        im = Image.new('RGB', (W, Hh), (18, 20, 24))
        d = ImageDraw.Draw(im)
        d.text((30, 20), a.title + (f' - sheet {n + 1}' if len(rows) > per else ''), fill=(255, 255, 255), font=font(40, True))
        d.text((30, 70), a.sub, fill=(180, 186, 198), font=font(22))
        for i, c in enumerate(part):
            pid, label = c[0], c[1]
            sub2 = c[2] if len(c) > 2 else ''
            x = 30 + (i % cols) * (2 * CW + 30); y = 110 + (i // cols) * (CH + cap + 20)
            for k, suf in enumerate(('_plan', '_drive')):
                p = os.path.join(a.shots, pid + suf + '.png')
                if os.path.exists(p):
                    t = Image.open(p).convert('RGB').resize((CW, CH), Image.LANCZOS)
                    im.paste(t, (x + k * CW, y + cap))
                else:
                    d.rectangle((x + k * CW, y + cap, x + (k + 1) * CW - 2, y + cap + CH), outline=(90, 90, 90))
                    d.text((x + k * CW + 20, y + cap + 20), 'no frame', fill=(200, 80, 80), font=font(22))
                d.text((x + k * CW + 8, y + cap + 6), 'PLAN (straight down, 128 x 72 m, travel up)' if k == 0 else "DRIVER'S EYE (1.15 m, 45 m before)", fill=(255, 255, 0), font=font(18, True))
            d.text((x, y + 4), label[:118], fill=(255, 255, 255), font=font(24, True))
            d.text((x, y + 36), sub2[:170], fill=(200, 206, 216), font=font(19))
            d.text((x, y + 62), (c[3] if len(c) > 3 else '')[:170], fill=(160, 166, 178), font=font(19))
        outp = a.out % (n + 1) if '%' in a.out else a.out
        im.save(outp)
        print('sheet', outp, im.size)
        n += 1


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('mode')
    ap.add_argument('--csv'); ap.add_argument('--roads'); ap.add_argument('--out'); ap.add_argument('--plans')
    ap.add_argument('--spot', action='append'); ap.add_argument('--title', default=''); ap.add_argument('--sub', default=''); ap.add_argument('--note', default='')
    ap.add_argument('--radius', type=float, default=70.0); ap.add_argument('--shots'); ap.add_argument('--list'); ap.add_argument('--per', type=int, default=10)
    a = ap.parse_args()
    a.centre = None
    {'heat': heat, 'plans': plans, 'sheet': sheet}[a.mode](a)
