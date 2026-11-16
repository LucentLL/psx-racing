# Top-down map of a stretch: the road with station ticks, buildings (inn magenta), side roads, lots, lake; optional
# camera cones.  python topmap.py <m0> <m1> <out.png> [m=..,yaw=..,fov=.. ; ...]
import json, math, os, sys
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import overlay as O
m0, m1, out = float(sys.argv[1]), float(sys.argv[2]), sys.argv[3]
cams = [c.strip() for c in ' '.join(sys.argv[4:]).split(';') if c.strip()]
pts = [p for i, p in enumerate(O.P) if m0 <= i * O.SP <= m1]
x0 = min(p[0] for p in pts) - 150; x1 = max(p[0] for p in pts) + 150
z0 = min(p[2] for p in pts) - 150; z1 = max(p[2] for p in pts) + 150
S = 700 / max(x1 - x0, z1 - z0)
Wd, Hd = int((x1 - x0) * S), int((z1 - z0) * S)
im = Image.new('RGB', (Wd, Hd), (235, 235, 225)); d = ImageDraw.Draw(im)
T = lambda x, z: ((x - x0) * S, (z1 - z) * S)
for ring in O.W['lake']:
    xz = ring['xz']; q = [T(xz[2 * k], xz[2 * k + 1]) for k in range(len(xz) // 2)]
    if len(q) > 2: d.polygon(q, fill=(170, 200, 240))
for l in O.W['lots']:
    xz = l['xz']; d.polygon([T(xz[2 * k], xz[2 * k + 1]) for k in range(len(xz) // 2)], outline=(200, 0, 160))
for o in O.W.get('open', []):
    xz = o['xz']; d.polygon([T(xz[2 * k], xz[2 * k + 1]) for k in range(len(xz) // 2)], outline=(0, 150, 0))
for r in O.W['roads']:
    xz = r['xz']; q = [T(xz[2 * k], xz[2 * k + 1]) for k in range(len(xz) // 2)]
    d.line(q, fill=(230, 120, 0), width=2)
for b in O.W['buildings']:
    xz = b['xz']; q = [T(xz[2 * k], xz[2 * k + 1]) for k in range(len(xz) // 2)]
    d.polygon(q, fill=(255, 0, 255) if b.get('role') == 'inn' else (90, 90, 90))
d.line([T(p[0], p[2]) for p in pts], fill=(0, 0, 0), width=3)
for i, p in enumerate(O.P):
    m = i * O.SP
    if m0 <= m <= m1 and int(m) % 50 == 0:
        x, y = T(p[0], p[2]); d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(255, 0, 0)); d.text((x + 3, y - 6), str(int(m)), fill=(200, 0, 0))
for c in cams:
    ps = O.pose(c.replace(',', ' '))
    cx, _, cz = ps['c']; f = ps['f']; hf = math.radians(ps['fov'] / 2)
    for a in (-hf, hf):
        dx = f[0] * math.cos(a) + f[1] * math.sin(a); dz = -f[0] * math.sin(a) + f[1] * math.cos(a)
        d.line([T(cx, cz), T(cx + dx * 250, cz + dz * 250)], fill=(0, 0, 255), width=1)
    x, y = T(cx, cz); d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(0, 0, 255))
im.save(out); print(out, im.size)
