# audit_3dep_plots.py - draw the lidar audit (tools/roads/audit_3dep.mjs output).
#
#   py -I tools/roads/audit_3dep_plots.py <audit dir> <png out dir> [--sheet <png>] [--suffix _after]
#
# Per stage: <pre>_profile.png (height real vs game, the difference, grade real
# vs game: three panels on one distance axis) and <pre>_plan.png (the route in
# plan coloured by how far the mapped line sits from the lidar road bench, with
# the offset along the route under it). --sheet: one page, a row per stage.
# PIL only (no matplotlib on this machine).

import json, math, os, sys
from PIL import Image, ImageDraw, ImageFont

SURFACE, INK, INK2, MUTED, GRID, AXIS = '#fcfcfb', '#0b0b0b', '#52514e', '#898781', '#e1e0d9', '#c3c2b7'
REAL, GAME = '#2a78d6', '#eb6834'            # categorical slots 1 and 2, fixed: real = blue, game = orange
SEQ = ['#b7d3f6', '#6da7ec', '#256abf', '#0d366b']   # one hue, light to dark: |offset| < 1, 1-2, 2-3.5, > 3.5 m
NOBENCH = '#c3c2b7'
ORDER = ['swa', 'gap', 'mtm', 'beech', 'chimney', 'brp', 'swiss', 'brock']

def font(size, bold=False):
    for name in (['segoeuib.ttf', 'arialbd.ttf'] if bold else ['segoeui.ttf', 'arial.ttf']):
        try: return ImageFont.truetype(os.path.join(r'C:\Windows\Fonts', name), size)
        except OSError: pass
    return ImageFont.load_default()

def nice_step(span, target):
    raw = span / max(1, target)
    mag = 10 ** math.floor(math.log10(raw)) if raw > 0 else 1
    for m in (1, 2, 2.5, 5, 10):
        if raw <= m * mag: return m * mag
    return 10 * mag

class Panel:
    """One axis pair inside an image: data -> pixel, grid, ticks, lines."""
    def __init__(self, dr, box, xr, yr, S=1):
        self.dr, self.box, self.xr, self.yr, self.S = dr, box, xr, yr, S
    def px(self, x, y):
        x0, y0, x1, y1 = self.box
        return (x0 + (x - self.xr[0]) / (self.xr[1] - self.xr[0]) * (x1 - x0),
                y1 - (y - self.yr[0]) / (self.yr[1] - self.yr[0]) * (y1 - y0))
    def frame(self, xlabel=None, ylabel=None, xticks=True, yfmt='{:g}', xdiv=1000.0, xfmt='{:g}', ny=4, f=None, zero=False):
        dr, S = self.dr, self.S
        x0, y0, x1, y1 = self.box
        f = f or font(11 * S)
        ys = nice_step(self.yr[1] - self.yr[0], ny)
        v = math.ceil(self.yr[0] / ys) * ys
        while v <= self.yr[1] + 1e-9:
            _, y = self.px(self.xr[0], v)
            dr.line([(x0, y), (x1, y)], fill=AXIS if (zero and abs(v) < 1e-9) else GRID, width=S)
            t = yfmt.format(v if abs(v) > 1e-9 else 0)
            dr.text((x0 - 6 * S, y), t, fill=MUTED, font=f, anchor='rm')
            v += ys
        xs = nice_step((self.xr[1] - self.xr[0]) / xdiv, 8) * xdiv
        v = math.ceil(self.xr[0] / xs) * xs
        while v <= self.xr[1] + 1e-9:
            x, _ = self.px(v, self.yr[0])
            dr.line([(x, y1), (x, y1 + 4 * S)], fill=AXIS, width=S)
            if xticks: dr.text((x, y1 + 6 * S), xfmt.format(v / xdiv), fill=MUTED, font=f, anchor='ma')
            v += xs
        dr.line([(x0, y1), (x1, y1)], fill=AXIS, width=S)
        if xlabel: dr.text(((x0 + x1) / 2, y1 + 22 * S), xlabel, fill=INK2, font=f, anchor='ma')
        if ylabel: dr.text((x0, y0 - 6 * S), ylabel, fill=INK2, font=f, anchor='ls')
    def line(self, xs, ys, color, width=2):
        pts, out = [], []
        for x, y in zip(xs, ys):
            if y is None or (isinstance(y, float) and math.isnan(y)):
                if len(pts) > 1: out.append(pts)
                pts = []
            else: pts.append(self.px(x, min(max(y, self.yr[0]), self.yr[1])))
        if len(pts) > 1: out.append(pts)
        for p in out: self.dr.line(p, fill=color, width=width * self.S, joint='curve')
    def band(self, a, b, color):
        x0, y0, x1, y1 = self.box
        xa, _ = self.px(a, 0); xb, _ = self.px(b, 0)
        self.dr.rectangle([xa, y0, max(xb, xa + 1), y1], fill=color)

def legend(dr, x, y, items, S=1, f=None):
    f = f or font(11 * S)
    for label, color in items:
        dr.line([(x, y), (x + 18 * S, y)], fill=color, width=2 * S)
        dr.text((x + 24 * S, y), label, fill=INK2, font=f, anchor='lm')
        x += 24 * S + dr.textlength(label, font=f) + 18 * S
    return x

def pad_range(lo, hi, frac=0.06, minspan=1.0):
    span = max(hi - lo, minspan)
    return (lo - span * frac, hi + span * frac)

def structures(p, d):
    for g in d['structures']['gameBridges'] + d['structures']['gameTunnels']:
        p.band(g['fromM'], g['toM'], '#efeee9')

def profile_png(d, path, S=2):
    W, H = 1280 * S, 760 * S
    im = Image.new('RGB', (W, H), SURFACE); dr = ImageDraw.Draw(im)
    s = d['series']; x = s['sM']; pr = d['profile']
    dr.text((56 * S, 22 * S), f"{d['road']}  ({d['id']}, {d['stage']}_stage)", fill=INK, font=font(17 * S, True), anchor='ls')
    dr.text((56 * S, 42 * S), f"Height profile against the USGS 1 m lidar ground on the road bench.  Game - real: bias {pr['biasM']:+.2f} m, RMS {pr['rmsM']:.2f} m, worst {pr['worstM']:+.2f} m at {pr['worstAtM']} m.  "
            f"Descent {pr['climbReal']['down']:.0f} m real / {pr['climbGame']['down']:.0f} game, climb {pr['climbReal']['up']:.0f} / {pr['climbGame']['up']:.0f}.", fill=INK2, font=font(11 * S), anchor='ls')
    xr = (0, d['lengthM'])
    L, R = 70 * S, W - 30 * S
    # A: height
    lo = min(v for v in s['real'] + s['game'] if v is not None); hi = max(v for v in s['real'] + s['game'] if v is not None)
    a = Panel(dr, (L, 84 * S, R, 360 * S), xr, pad_range(lo, hi), S); structures(a, d)
    a.frame(ylabel='Height above sea level (m)', xticks=False, ny=6)
    a.line(x, s['real'], REAL); a.line(x, s['game'], GAME)
    legend(dr, R - 330 * S, 72 * S, [('Real road (1 m lidar)', REAL), ('Game (shipped bake)', GAME)], S)
    # B: difference
    dh = [g - r for g, r in zip(s['game'], s['real'])]
    m = max(2.0, max(abs(v) for v in dh))
    b = Panel(dr, (L, 400 * S, R, 520 * S), xr, (-m * 1.1, m * 1.1), S); structures(b, d)
    b.frame(ylabel='Game - real height (m): above zero the game road is too high', xticks=False, ny=4, zero=True, yfmt='{:+g}')
    b.line(x, dh, GAME)
    # C: grade
    gr = [v * 100 for v in s['gradeReal']]; gg = [v * 100 for v in s['gradeGame']]
    m = max(10.0, min(16.0, max(abs(v) for v in gr + gg)))
    c = Panel(dr, (L, 560 * S, R, 700 * S), xr, (-m * 1.05, m * 1.05), S); structures(c, d)
    c.frame(xlabel='Distance from the start of the route (km)', ylabel=f"Grade (%), over a {d['profile']['smoothingSigmaM']} m gaussian: real p95 {pr['gradeReal']['p95Pct']:.1f}, max {pr['gradeReal']['maxPct']:.1f};  game p95 {pr['gradeGame']['p95Pct']:.1f}, max {pr['gradeGame']['maxPct']:.1f}", ny=4, zero=True, yfmt='{:+g}')
    c.line(x, gr, REAL); c.line(x, gg, GAME)
    if d['structures']['gameBridges'] or d['structures']['gameTunnels']:
        dr.text((R, 742 * S), 'Shaded: the stage\'s bridge and tunnel spans (no ground to read; real height is a straight line across)', fill=MUTED, font=font(10 * S), anchor='rs')
    im = im.resize((W // S, H // S), Image.LANCZOS); im.save(path)

def off_color(v, conf):
    if not conf or v is None: return NOBENCH
    a = abs(v)
    return SEQ[0] if a < 1 else SEQ[1] if a < 2 else SEQ[2] if a < 3.5 else SEQ[3]

def plan_png(d, path, S=2):
    W, H = 1280 * S, 900 * S
    im = Image.new('RGB', (W, H), SURFACE); dr = ImageDraw.Draw(im)
    s = d['series']; ln = d['line']
    dr.text((40 * S, 22 * S), f"{d['road']}  ({d['id']}, {d['stage']}_stage)", fill=INK, font=font(17 * S, True), anchor='ls')
    dr.text((40 * S, 42 * S), f"Mapped centreline against the road bench in the 1 m lidar ground.  Bench found at {ln['confidentPct']:.0f}% of stations: offset median {ln['medianAbsM']:.2f} m, "
            f"p95 {ln['p95AbsM']:.1f} m, worst {ln['worstAbsM']:.1f} m; {ln['over2mRuns']} stretches over 2 m ({ln['over2mRunsM']} m of road).", fill=INK2, font=font(11 * S), anchor='ls')
    # plan, equal scale, north up
    box = (40 * S, 70 * S, W - 40 * S, 640 * S)
    xs, zs = s['x'], s['z']
    cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    sc = min((box[2] - box[0] - 60 * S) / max(1, max(xs) - min(xs)), (box[3] - box[1] - 60 * S) / max(1, max(zs) - min(zs)))
    P = lambda x, z: ((box[0] + box[2]) / 2 + (x - cx) * sc, (box[1] + box[3]) / 2 - (z - cz) * sc)
    n = len(xs)
    for i in range(n - 1):
        v = s['benchOffFiltered'][i]; c = off_color(s['benchOff'][i] if s['benchConf'][i] else None, s['benchConf'][i])
        if s['structure'][i]: c = '#efeee9'
        wdt = 3 * S if c in (SEQ[2], SEQ[3]) else 2 * S
        dr.line([P(xs[i], zs[i]), P(xs[i + 1], zs[i + 1])], fill=c, width=wdt)
    f = font(11 * S)
    # km marks and the worst stretches, labelled
    for km in range(0, int(d['lengthM'] // 1000) + 1):
        i = min(n - 1, km * 1000 // d['spacingM']); x, y = P(xs[i], zs[i])
        dr.ellipse([x - 3 * S, y - 3 * S, x + 3 * S, y + 3 * S], fill=SURFACE, outline=INK2, width=S)
        dr.text((x + 6 * S, y - 6 * S), f'{km} km', fill=MUTED, font=font(10 * S), anchor='ls')
    for k, r in enumerate(ln['worstRuns'][:5]):
        i = min(n - 1, int((r['fromM'] + r['toM']) / 2 / d['spacingM'])); x, y = P(xs[i], zs[i])
        dr.ellipse([x - 7 * S, y - 7 * S, x + 7 * S, y + 7 * S], outline=INK, width=S)
        dr.text((x + 10 * S, y + 8 * S), f"{abs(r['peakM']):.1f} m off at {r['fromM']}-{r['toM']} m", fill=INK, font=f, anchor='ls')
    x0, y0 = P(xs[0], zs[0]); dr.text((x0 - 8 * S, y0 + 4 * S), 'START', fill=INK, font=font(10 * S, True), anchor='rs')
    # scale bar and north
    bar = 500 if sc * 500 < 260 * S else 200
    bx, by = box[0] + 10 * S, box[3] - 8 * S
    dr.line([(bx, by), (bx + bar * sc, by)], fill=INK2, width=2 * S); dr.text((bx + bar * sc / 2, by - 4 * S), f'{bar} m', fill=INK2, font=f, anchor='ms')
    dr.text((box[2] - 14 * S, box[1] + 18 * S), 'N', fill=INK2, font=font(13 * S, True), anchor='ms'); dr.line([(box[2] - 14 * S, box[1] + 46 * S), (box[2] - 14 * S, box[1] + 22 * S)], fill=INK2, width=2 * S)
    # legend (sequential: one hue, light to dark)
    lx, ly = W - 560 * S, 62 * S
    for lab, c in [('< 1 m', SEQ[0]), ('1-2 m', SEQ[1]), ('2-3.5 m', SEQ[2]), ('> 3.5 m', SEQ[3]), ('no bench to read', NOBENCH)]:
        dr.line([(lx, ly), (lx + 18 * S, ly)], fill=c, width=4 * S); dr.text((lx + 24 * S, ly), lab, fill=INK2, font=f, anchor='lm')
        lx += 24 * S + dr.textlength(lab, font=f) + 16 * S
    # the offset along the route
    off = [v if c else None for v, c in zip(s['benchOff'], s['benchConf'])]
    m = max(4.0, min(8.0, ln['worstAbsM']))
    p = Panel(dr, (70 * S, 700 * S, W - 30 * S, 840 * S), (0, d['lengthM']), (-m * 1.1, m * 1.1), S); structures(p, d)
    p.frame(xlabel='Distance from the start of the route (km)', ylabel='Bench centre from the mapped line (m): + left of travel, - right.  Dots: each station; line: filtered along the road', zero=True, yfmt='{:+g}', ny=4)
    for xv, v in zip(s['sM'], off):
        if v is None: continue
        x, y = p.px(xv, max(-m, min(m, v))); dr.ellipse([x - S, y - S, x + S, y + S], fill=SEQ[1])
    p.line(s['sM'], s['benchOffFiltered'], SEQ[3], 2)
    im = im.resize((W // S, H // S), Image.LANCZOS); im.save(path)

def sheet_png(ds, path, S=2, title='Mountain stages against USGS 3DEP lidar: real road vs game'):
    rowH = 190
    W, H = 1700 * S, (110 + rowH * len(ds) + 30) * S
    im = Image.new('RGB', (W, H), SURFACE); dr = ImageDraw.Draw(im)
    dr.text((30 * S, 34 * S), title, fill=INK, font=font(22 * S, True), anchor='ls')
    dr.text((30 * S, 58 * S), 'Left: height along the route, real road (USGS 1 m lidar on the road bench) and the game. Right: game minus real height, metres (above zero the game road is too high).',
            fill=INK2, font=font(12 * S), anchor='ls')
    legend(dr, 30 * S, 80 * S, [('Real road (1 m lidar)', REAL), ('Game', GAME)], S, font(12 * S))
    f, fb, fs = font(11 * S), font(14 * S, True), font(10 * S)
    for k, d in enumerate(ds):
        y0 = (110 + k * rowH) * S
        s = d['series']; pr = d['profile']; ln = d['line']; tn = d['terrain']
        dr.line([(30 * S, y0 - 8 * S), (W - 30 * S, y0 - 8 * S)], fill=GRID, width=S)
        dr.text((30 * S, y0 + 14 * S), d['road'], fill=INK, font=fb, anchor='ls')
        lines = [
            f"{d['id']} ({d['stage']}), {d['lengthM'] / 1000:.1f} km{', loop' if d['loop'] else ''}",
            f"Height: bias {pr['biasM']:+.1f} m, RMS {pr['rmsM']:.1f}, worst {pr['worstM']:+.1f} m",
            f"Descent {pr['climbReal']['down']:.0f} real / {pr['climbGame']['down']:.0f} game; climb {pr['climbReal']['up']:.0f} / {pr['climbGame']['up']:.0f} m",
            f"Grade p95 {pr['gradeReal']['p95Pct']:.1f}% real / {pr['gradeGame']['p95Pct']:.1f}% game; max {pr['gradeReal']['maxPct']:.1f} / {pr['gradeGame']['maxPct']:.1f}%",
            f"Grade off by > 1% on {pr['gradeOff1pctShare']:.0f}% of the route",
            f"Line vs bench: median {ln['medianAbsM']:.1f} m, p95 {ln['p95AbsM']:.1f}, worst {ln['worstAbsM']:.1f} m",
            f"Terrain RMS: {tn['near']['bands'][1]['rmsM']:.1f} m at 30-100 m, {tn['near']['bands'][2]['rmsM']:.1f} m to 500 m, far grid "
            + (f"{tn['far']['all']['rmsM']:.1f} m" if tn['far']['all']['rmsM'] < 20 else f"{tn['far']['bands'][2]['rmsM']:.1f} m to 2.3 km, then a HOLE (worst {tn['far']['all']['worstM']:.0f} m)"),
        ]
        for j, t in enumerate(lines): dr.text((30 * S, y0 + (34 + j * 17) * S), t, fill=INK2, font=f, anchor='ls')
        xr = (0, d['lengthM'])
        vals = [v for v in s['real'] + s['game'] if v is not None]
        a = Panel(dr, (470 * S, y0 + 8 * S, 1090 * S, y0 + (rowH - 44) * S), xr, pad_range(min(vals), max(vals)), S); structures(a, d)
        a.frame(ny=3, f=fs, xlabel='km' if k == len(ds) - 1 else None)
        a.line(s['sM'], s['real'], REAL); a.line(s['sM'], s['game'], GAME)
        dh = [g - r for g, r in zip(s['game'], s['real'])]
        m = max(4.0, max(abs(v) for v in dh))
        b = Panel(dr, (1170 * S, y0 + 8 * S, 1670 * S, y0 + (rowH - 44) * S), xr, (-m * 1.1, m * 1.1), S); structures(b, d)
        b.frame(ny=4, f=fs, zero=True, yfmt='{:+g}', xlabel='km' if k == len(ds) - 1 else None)
        b.line(s['sM'], dh, GAME)
    im = im.resize((W // S, H // S), Image.LANCZOS); im.save(path)

def main():
    a = sys.argv[1:]
    src, out = a[0], a[1]
    suffix = a[a.index('--suffix') + 1] if '--suffix' in a else ''
    only = a[a.index('--only') + 1].split(',') if '--only' in a else ORDER
    os.makedirs(out, exist_ok=True)
    ds = []
    for pre in only:
        p = os.path.join(src, pre + '_audit.json')
        if not os.path.exists(p): continue
        d = json.load(open(p, encoding='utf8')); ds.append(d)
        profile_png(d, os.path.join(out, f'{pre}_profile{suffix}.png'))
        plan_png(d, os.path.join(out, f'{pre}_plan{suffix}.png'))
        print('drew', pre)
    if '--sheet' in a:
        sheet_png(ds, a[a.index('--sheet') + 1]); print('sheet', a[a.index('--sheet') + 1])

if __name__ == '__main__':
    main()
