# Before/after pairs from two HazeProbe folders: py -I tools/roads/audit_3dep_pairs.py <before dir> <after dir> <out dir> <prefix> "8400:8416,9360:9376,..." [hour]
import os, sys
from PIL import Image, ImageDraw, ImageFont
b, a, out, pre, pairs = sys.argv[1:6]
hour = sys.argv[6] if len(sys.argv) > 6 else 'noon'
os.makedirs(out, exist_ok=True)
f = ImageFont.truetype(r'C:\Windows\Fonts\segoeui.ttf', 22)
for pr in pairs.split(','):
    m0, m1 = pr.split(':')
    for view in ('chase', 'vista'):
        p0 = os.path.join(b, f'hz_{hour}_{m0}_{view}_asis.png'); p1 = os.path.join(a, f'hz_{hour}_{m1}_{view}_asis.png')
        if not (os.path.exists(p0) and os.path.exists(p1)): print('missing', p0 if not os.path.exists(p0) else p1); continue
        i0, i1 = Image.open(p0).convert('RGB'), Image.open(p1).convert('RGB')
        w, h = i0.size
        im = Image.new('RGB', (w * 2 + 12, h + 40), '#fcfcfb')
        im.paste(i0, (0, 40)); im.paste(i1, (w + 12, 40))
        d = ImageDraw.Draw(im)
        d.text((8, 6), f'BEFORE (SRTM bake): {view} at {m0} m', fill='#0b0b0b', font=f)
        d.text((w + 20, 6), f'AFTER (3DEP lidar bake): {view} at {m1} m (the same place)', fill='#0b0b0b', font=f)
        s = 1600 / im.width
        im = im.resize((1600, int(im.height * s)), Image.LANCZOS)
        o = os.path.join(out, f'{pre}_ba_{m0}_{view}.jpg'); im.save(o, quality=86); print(o)
