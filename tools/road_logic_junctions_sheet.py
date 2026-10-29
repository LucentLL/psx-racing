# The road-logic JUNCTIONS before/after sheet: the spot census's counts of the
# control / signal / crossing-flow / box / STOP classes (before and after, with
# the city-wide static audit), and the driver's view of junctions' heads and
# signs (Editor/CityRefSpots.Junctions.cs: <label>_<before|after>_<A|B>.png and
# junctions.txt).
#   py -3 tools/road_logic_junctions_sheet.py --before Docs/traffic_census_junctions_before
#       --after Docs/traffic_census_junctions_after --shots <dir> --out Docs/shots/road_logic_junctions_before_after.png
import argparse, json, os, re
from PIL import Image, ImageDraw, ImageFont

ap = argparse.ArgumentParser()
ap.add_argument('--before', required=True)
ap.add_argument('--after', required=True)
ap.add_argument('--shots', required=True)
ap.add_argument('--out', required=True)
a = ap.parse_args()

def summary(d):
    p = os.path.join(d, 'summary.json')
    if not os.path.exists(p):
        p = os.path.join(d, 'raw', 'summary.json')
    return json.load(open(p, encoding='utf-8'))

B, A = summary(a.before), summary(a.after)
KINDS = [
    ('control/no-control-major-crossing', 'major roads crossing, no control (static, city-wide)'),
    ('signal/conflicting-green', 'crossing approaches green together (static, city-wide)'),
    ('conflict/signal-crossing-flows', 'crossing flows in one box: signal'),
    ('conflict/stop-crossing-flows', 'crossing flows in one box: STOP'),
    ('conflict/yield-crossing-flows', 'crossing flows in one box: yield'),
    ('conflict/uncontrolled-crossing', 'crossing flows in one box: no control'),
    ('stuck/gridlock-box', 'queue standing in the box 15 s+'),
    ('stuck/crossing-blocked', 'held by a crossing car 15 s+'),
    ('stuck/stop-deadlock', 'waiting 30 s+ at a STOP'),
    ('stuck/yield-starved', 'waiting 40 s+ to yield'),
    ('stuck/left-turn-starved', 'left turn waiting 45 s+'),
    ('stuck/keep-clear-wait', 'held 40 s+ short of a full box (new rule)'),
    ('wreck/car-in-junction', 'car-on-car wreck in a junction'),
    ('contact/car-bump', 'car-on-car bump'),
]
def cnt(s, k): return s.get('counts', {}).get(k, 0)

try:
    font = ImageFont.truetype('arial.ttf', 17); bold = ImageFont.truetype('arialbd.ttf', 19); big = ImageFont.truetype('arialbd.ttf', 24); small = ImageFont.truetype('arial.ttf', 14)
except Exception:
    font = bold = big = small = ImageFont.load_default()

# the shots: labels in order of junctions.txt
txt = os.path.join(a.shots, 'junctions.txt')
notes, aspects = {}, {}
if os.path.exists(txt):
    mode = label = None
    for line in open(txt, encoding='utf-8'):
        m = re.match(r'^(before|after) (\S+): (.*)$', line.rstrip())
        if m:
            mode, label = m.group(1), m.group(2)
            notes.setdefault(label, m.group(3))
            aspects[(mode, label)] = []
        elif line.startswith('    ') and mode:
            aspects[(mode, label)].append(line.strip())
labels = list(notes.keys())

TW, CW, CH = 1920, 470, 264
rows_h = 40 + 26 * (len(KINDS) + 3)
shot_h = sum(30 + 2 * (CH + 140) for _ in labels)
img = Image.new('RGB', (TW, 90 + rows_h + shot_h + 20), (22, 24, 28))
d = ImageDraw.Draw(img)
d.text((20, 16), 'ROAD LOGIC - JUNCTION CONTROL AND SIGNALS: before / after', font=big, fill=(240, 240, 240))
d.text((20, 50), f"spot census, {B.get('spots', '?')} spots: before {B.get('car_hours', 0):.1f} car-hours / {B.get('km_driven', 0):.0f} km, after {A.get('car_hours', 0):.1f} car-hours / {A.get('km_driven', 0):.0f} km (static rows: the whole map)", font=font, fill=(200, 200, 200))
y = 90
d.text((20, y), 'kind', font=bold, fill=(255, 220, 120)); d.text((900, y), 'before', font=bold, fill=(255, 220, 120)); d.text((1020, y), 'after', font=bold, fill=(255, 220, 120))
d.text((1140, y), 'per car-hour before / after', font=bold, fill=(255, 220, 120))
y += 30
for k, desc in KINDS:
    b, af = cnt(B, k), cnt(A, k)
    static = 'static' in desc
    d.text((20, y), f'{k}  -  {desc}', font=font, fill=(230, 230, 230))
    d.text((900, y), str(b), font=font, fill=(230, 160, 160) if b else (160, 160, 160))
    d.text((1020, y), str(af), font=font, fill=(160, 230, 160) if af < b else (230, 230, 160) if af == b else (240, 140, 140))
    if not static:
        pb = b / max(0.1, B.get('car_hours', 1)); pa = af / max(0.1, A.get('car_hours', 1))
        d.text((1140, y), f'{pb:.2f} / {pa:.2f}', font=font, fill=(200, 200, 200))
    y += 26
y += 20
for label in labels:
    d.text((20, y), f'{label}: {notes.get(label, "")}', font=bold, fill=(255, 220, 120))
    y += 30
    for mode in ('before', 'after'):
        x = 20
        for side in ('A', 'B'):
            p = os.path.join(a.shots, f'{label}_{mode}_{side}.png')
            if os.path.exists(p):
                im = Image.open(p).convert("RGB").resize((CW + 230, CH + 130))
                img.paste(im, (x, y))
            d.text((x + 8, y + 6), f'{mode.upper()} - approach {side}', font=bold, fill=(255, 255, 255))
            x += CW + 250
        asp = aspects.get((mode, label), [])
        d.text((x, y + 4), mode.upper(), font=bold, fill=(255, 220, 120))
        for i, l in enumerate(asp[:9]):
            d.text((x, y + 30 + i * 20), l[:44], font=small, fill=(220, 220, 220))
        y += CH + 140
img = img.crop((0, 0, TW, y + 10))
os.makedirs(os.path.dirname(a.out), exist_ok=True)
img.save(a.out, optimize=True)
print('sheet', a.out, img.size)
