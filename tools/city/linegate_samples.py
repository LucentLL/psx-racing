# linegate_samples.py - the false-positive check: N random places of each class of a line gate run.
#
#   python -I tools/city/linegate_samples.py pick --csv city_linegate.csv --n 30 --seed 1116 --out samples.tsv [--shots shots.tsv]
#       samples.tsv : id class kind road edge s x z lat lon mph what          (one row per sampled place)
#       shots.tsv   : the list for CityAudit.RunLineGateShots (plan only, 64 x 36 m)
#   python -I tools/city/linegate_samples.py grid --samples samples.tsv --shots <dir> --out grid_%s.png
#       one sheet per class: the editor's plan frame of each sample, labelled with what the gate said
import sys, os, csv, math, random, argparse
from PIL import Image, ImageDraw, ImageFont

ORDER = ['SJOG', 'SHIFT', 'KINK', 'WIDTH', 'CONT', 'SURFACE', 'JUNCTION', 'MEDIAN', 'STEP', 'BARRIER', 'BRIDGE']


def font(sz, bold=False):
    try:
        return ImageFont.truetype('arialbd.ttf' if bold else 'arial.ttf', sz)
    except Exception:
        return ImageFont.load_default()


def pick(a):
    rows = [r for r in csv.DictReader(open(a.csv, newline='', encoding='utf-8')) if r['absorbed'] != '1']
    rnd = random.Random(a.seed)
    out = []; shots = ['id\tx\tz\tedge\ts\tdir\thalf\tviews']
    for c in ORDER:
        # one row per place: its most visible failure
        best = {}
        for r in rows:
            if r['class'] != c:
                continue
            p = r['place']
            if p not in best or float(r['visibility']) > float(best[p]['visibility']):
                best[p] = r
        places = sorted(best.values(), key=lambda r: int(r['place']))
        rnd.shuffle(places)
        for i, r in enumerate(places[:a.n]):
            sid = f'smp_{c}_{i + 1:02d}'
            s = 0.5 * (float(r['s0']) + float(r['s1']))
            out.append('\t'.join([sid, c, r['kind'], r['road'], r['edge'], f'{s:.1f}', r['x'], r['z'], r['lat'], r['lon'], r['mph'], r['what']]))
            shots.append(f"{sid}\t{float(r['x']):.1f}\t{float(r['z']):.1f}\t{r['edge']}\t{s:.1f}\t0\t{a.half}\tplan")
    open(a.out, 'w', encoding='utf-8').write('id\tclass\tkind\troad\tedge\ts\tx\tz\tlat\tlon\tmph\twhat\n' + '\n'.join(out) + '\n')
    if a.shots:
        open(a.shots, 'w', encoding='ascii').write('\n'.join(shots) + '\n')
    print('picked', len(out), 'samples')


def grid(a):
    by = {}
    with open(a.samples, encoding='utf-8') as f:
        h = f.readline().rstrip('\n').split('\t')
        for line in f:
            d = dict(zip(h, line.rstrip('\n').split('\t')))
            by.setdefault(d['class'], []).append(d)
    CW, CH, cap = 640, 360, 62
    for c, lst in by.items():
        cols = 5
        rows = math.ceil(len(lst) / cols)
        im = Image.new('RGB', (cols * (CW + 8) + 8, rows * (CH + cap + 8) + 60), (18, 20, 24))
        d = ImageDraw.Draw(im)
        d.text((8, 12), f'{c}: {len(lst)} random failing places, editor plan frames ({2 * a.half * 16 / 9:.0f} x {2 * a.half:.0f} m, travel up; the place is at the centre)', fill=(255, 255, 255), font=font(30, True))
        for i, s in enumerate(lst):
            x = 8 + (i % cols) * (CW + 8); y = 60 + (i // cols) * (CH + cap + 8)
            p = os.path.join(a.shots, s['id'] + '_plan.png')
            if os.path.exists(p):
                im.paste(Image.open(p).convert('RGB').resize((CW, CH), Image.LANCZOS), (x, y + cap))
                # the centre mark
                d.ellipse((x + CW / 2 - 14, y + cap + CH / 2 - 14, x + CW / 2 + 14, y + cap + CH / 2 + 14), outline=(255, 0, 255), width=2)
            else:
                d.rectangle((x, y + cap, x + CW, y + cap + CH), outline=(90, 90, 90))
            d.text((x, y + 2), f"{s['id'][4:]}  {s['road'][:34]}  e{s['edge']}  {s['mph']} mph", fill=(255, 255, 0), font=font(17, True))
            w = s['what']
            d.text((x, y + 22), w[:86], fill=(220, 224, 232), font=font(14))
            d.text((x, y + 40), w[86:172], fill=(220, 224, 232), font=font(14))
        outp = a.out % c if '%' in a.out else a.out
        im.save(outp)
        print('grid', outp, im.size)


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('mode'); ap.add_argument('--csv'); ap.add_argument('--n', type=int, default=30); ap.add_argument('--seed', type=int, default=1116)
    ap.add_argument('--out'); ap.add_argument('--shots'); ap.add_argument('--samples'); ap.add_argument('--half', type=float, default=18.0)
    a = ap.parse_args()
    {'pick': pick, 'grid': grid}[a.mode](a)
