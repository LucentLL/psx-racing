# The lens test's A/B pairs side by side (tools\r32lights-shots.ps1 -Mode lens):
# every <name>_A.png / <name>_B.png in RAW becomes <name>_AB.png in OUT, A on the
# left, B on the right, each frame scaled up 2x NEAREST (the game's pixels, not
# a resample) under a one-line label.
#   py tools\r32lenses-pairs.py <raw dir> <out dir>
import os
import sys
from PIL import Image, ImageDraw

raw, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
names = sorted(f[:-6] for f in os.listdir(raw) if f.endswith('_A.png'))
for n in names:
    a_path, b_path = os.path.join(raw, n + '_A.png'), os.path.join(raw, n + '_B.png')
    if not os.path.exists(b_path):
        continue
    a, b = Image.open(a_path).convert('RGB'), Image.open(b_path).convert('RGB')
    k = 2 if a.width < 1000 else 1
    a = a.resize((a.width * k, a.height * k), Image.NEAREST)
    b = b.resize((b.width * k, b.height * k), Image.NEAREST)
    bar, gap = 26, 8
    sheet = Image.new('RGB', (a.width + gap + b.width, a.height + bar), (24, 24, 24))
    sheet.paste(a, (0, bar))
    sheet.paste(b, (a.width + gap, bar))
    d = ImageDraw.Draw(sheet)
    d.text((8, 7), 'A  OPAQUE LAMPS (v03)   ' + n, fill=(235, 190, 90))
    d.text((a.width + gap + 8, 7), 'B  RECESSED + CLEAR COVERS (v04)   ' + n, fill=(235, 190, 90))
    path = os.path.join(out, n + '_AB.png')
    sheet.save(path)
    print('pair', path, sheet.size)
