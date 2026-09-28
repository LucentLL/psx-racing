"""refsheet.py <dir> [--before <dir>] [--title T] - contact sheets of the
Charlotte reference spots (Editor/CityRefSpots.cs, tools/city-refspots.ps1).

Without --before: every shot in <dir>, labelled, 4 across, one sheet per
group (sv = Street View spots, kink, crest/dip). With --before: each spot as
a BEFORE | AFTER pair, two pairs across. Writes <dir>/sheet_<group>.jpg.
Labels come from ref_spots.txt beside the shots (what each spot is, and
whether it snapped to the named road).
"""
import argparse, os, sys
from PIL import Image, ImageDraw, ImageFont

GROUPS = [('sv', 'Street View spots (survey A1-A15)'), ('kink', 'Kinks: verdict spots + census worst 20'),
          ('crest', 'Transect crests and dips')]


def font(size):
    for f in ('arial.ttf', 'DejaVuSans.ttf'):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def labels(d):
    out = {}
    p = os.path.join(d, 'ref_spots.txt')
    if os.path.isfile(p):
        for l in open(p, encoding='utf-8').read().splitlines()[1:]:
            c = l.split('\t')
            if len(c) >= 11:
                out[c[0]] = (c[10], c[3] == 'yes', c[2])
    return out


def group_of(name):
    if name.startswith('sv_'):
        return 'sv'
    if name.startswith('kink'):
        return 'kink'
    return 'crest'


def fit(img, w, h):
    return img.resize((w, h), Image.BILINEAR) if img.size != (w, h) else img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir')
    ap.add_argument('--before', default=None)
    ap.add_argument('--title', default='Charlotte reference spots')
    a = ap.parse_args()
    names = sorted(n[:-4] for n in os.listdir(a.dir) if n.endswith('.png'))
    if not names:
        print('no shots in', a.dir); sys.exit(1)
    lab = labels(a.dir)
    f_t, f_l = font(26), font(15)
    TW, TH, PAD, LH = 480, 270, 8, 40
    for g, gtitle in GROUPS:
        items = [n for n in names if group_of(n) == g]
        if not items:
            continue
        pairs = a.before is not None
        cols = 2 if pairs else 4
        cellw = (TW * 2 + PAD) if pairs else TW
        rows = (len(items) + cols - 1) // cols
        W = cols * cellw + (cols + 1) * PAD
        H = 50 + rows * (TH + LH + PAD) + PAD
        sheet = Image.new('RGB', (W, H), (24, 24, 28))
        dr = ImageDraw.Draw(sheet)
        dr.text((PAD, 12), f"{a.title} - {gtitle}" + ('   (left BEFORE, right AFTER)' if pairs else ''), fill=(240, 200, 90), font=f_t)
        for i, n in enumerate(items):
            r, c = divmod(i, cols)
            x = PAD + c * (cellw + PAD)
            y = 50 + r * (TH + LH + PAD)
            after = fit(Image.open(os.path.join(a.dir, n + '.png')).convert('RGB'), TW, TH)
            if pairs:
                bp = os.path.join(a.before, n + '.png')
                before = fit(Image.open(bp).convert('RGB'), TW, TH) if os.path.isfile(bp) else Image.new('RGB', (TW, TH), (60, 20, 20))
                sheet.paste(before, (x, y))
                sheet.paste(after, (x + TW + PAD, y))
            else:
                sheet.paste(after, (x, y))
            what, named, edge = lab.get(n.replace('_top', ''), ('', True, ''))
            text = n + ('  [top]' if n.endswith('_top') else '')
            sub = (what if what else '') + ('' if named else '  (NOT on the named road)')
            # labels stay inside their own cell
            def cut(t):
                while t and dr.textlength(t, font=f_l) > cellw - 4:
                    t = t[:-2]
                return t
            dr.text((x, y + TH + 3), cut(text), fill=(230, 230, 230), font=f_l)
            dr.text((x, y + TH + 21), cut(sub), fill=(170, 170, 180) if named else (255, 120, 100), font=f_l)
        out = os.path.join(a.dir, f'sheet_{g}.jpg')
        sheet.save(out, quality=85)
        print('wrote', out, f'{len(items)} shots')


if __name__ == '__main__':
    main()
