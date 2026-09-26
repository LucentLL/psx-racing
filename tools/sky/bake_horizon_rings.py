# The horizon ring (Shaders/PSXFogRing.cginc): eight colours per sky panorama,
# measured just above the photograph's horizon (1-6 degrees of elevation), in
# LINEAR light, written as ratios to their own mean so the hour's fog colour
# stays the fog's average. Re-run when a panorama in Resources/Sky changes:
#
#   py tools/sky/bake_horizon_rings.py
#
# Writes Assets/PSXRacing/Resources/Sky/horizon_rings.txt, one line per sky:
#   name r0 g0 b0 r1 g1 b1 ... r7 g7 b7   (bearing k centred on image u = (k+0.5)/8)
import glob, math, os
from PIL import Image

root = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'PSXRacing', 'Resources', 'Sky')
LO, HI = 0.70, 1.35       # a bearing is never darker or brighter than this against the mean
# The fog already glows toward the sun (PSXFogTowardSun), and its brightness is
# the owner's: the ring carries mostly COLOUR - each bearing's ratio has its
# own luminance divided out and only this power of it put back.
LUM_KEEP = 0.3

def lin(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

lines = []
for path in sorted(glob.glob(os.path.join(root, 'sky_*.png'))):
    im = Image.open(path).convert('RGB')
    w, h = im.size
    px = im.load()
    r0 = int(h * (0.5 - math.radians(6) / math.pi))
    r1 = int(h * (0.5 - math.radians(1) / math.pi))
    ring = []
    for k in range(8):
        acc = [0.0, 0.0, 0.0]; n = 0
        for x in range(k * w // 8, (k + 1) * w // 8):
            for y in range(r0, r1 + 1):
                p = px[x, y]
                for ch in range(3): acc[ch] += lin(p[ch])
                n += 1
        ring.append([a / n for a in acc])
    mean = [sum(c[ch] for c in ring) / 8 for ch in range(3)]
    vals = []
    for c in ring:
        rat = [c[ch] / max(mean[ch], 1e-6) for ch in range(3)]
        lum = max(0.2126 * rat[0] + 0.7152 * rat[1] + 0.0722 * rat[2], 1e-6)
        for ch in range(3):
            vals.append(min(HI, max(LO, rat[ch] / lum * lum ** LUM_KEEP)))
    name = os.path.splitext(os.path.basename(path))[0]
    lines.append(name + ' ' + ' '.join('%.4f' % v for v in vals))
    print(name, ' | '.join('%.2f %.2f %.2f' % tuple(vals[k*3:k*3+3]) for k in range(8)))
with open(os.path.join(root, 'horizon_rings.txt'), 'w', newline='\n') as f:
    f.write('\n'.join(lines) + '\n')
