# cars-1116 (2026-11-16): two import-side corrections to the 512 lamp maps of the owner's new shells, after
# the self-test's lamp-map checks. The HD lamp maps take them from the 512 maps (stage_carhd.py).
#   lux_coupe_97   the parcel-shelf stop lamp (two mirrored blobs at x +-0.05, y 0.90, inside the cabin) sits
#                  behind the backlight, which the game draws opaque: never seen, and "two centre lamps" fails
#                  the pairs check. Its R/G texels are zeroed (texels on faces near the centreline above 0.8 m).
#   pony_coupe_67  the delivery's map has no lens-scatter channel (A = 0, "reserved"): A = 0.35 x B, the rule
#                  the 11-11 batch used for the CTR, so the lenses glow like every other car's.
# usage: py -I cars1116_fix.py <repo>
import os, sys
import numpy as np
from PIL import Image

repo = sys.argv[1]
MODELS = os.path.join(repo, 'Assets', 'PSXRacing', 'Art', 'Car', 'Models')
LAMPS = os.path.join(repo, 'Assets', 'PSXRacing', 'Resources', 'CarLights')


def faces_of(obj):
    V, T, F = [], [], []
    for line in open(obj, encoding='utf-8'):
        if line.startswith('v '): V.append([float(x) for x in line.split()[1:4]])
        elif line.startswith('vt '): T.append([float(x) for x in line.split()[1:3]])
        elif line.startswith('f '):
            idx = [p.split('/') for p in line.split()[1:]]
            for i in range(1, len(idx) - 1): F.append([idx[0], idx[i], idx[i + 1]])
    return np.array(V), np.array(T), F


def footprint(uv, W, H):
    uv = uv * [W, H]; uv[:, 1] = H - uv[:, 1]
    x0, y0 = np.floor(uv.min(0)).astype(int); x1, y1 = np.ceil(uv.max(0)).astype(int)
    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W), min(y1, H)
    if x1 <= x0 or y1 <= y0: return None
    ys, xs = np.mgrid[y0:y1, x0:x1] + 0.5
    (ax, ay), (bx, by), (cx, cy) = uv
    d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(d) < 1e-12: return None
    l1 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
    l2 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
    return x0, y0, x1, y1, (l1 >= -1e-3) & (l2 >= -1e-3) & (1 - l1 - l2 >= -1e-3)


# ---- lux_coupe_97: the hidden parcel-shelf stop lamp
p = os.path.join(LAMPS, 'lux_coupe_97_lightmask.png')
im = np.array(Image.open(p).convert('RGBA'))
H, W = im.shape[:2]
V, T, F = faces_of(os.path.join(MODELS, 'lux_coupe_97', 'lux_coupe_97.obj'))
kill = np.zeros((H, W), bool); keep = np.zeros((H, W), bool)
for f in F:
    vi = [int(q[0]) - 1 for q in f]; ti = [int(q[1]) - 1 for q in f]
    fp = footprint(T[ti].copy(), W, H)
    if fp is None: continue
    x0, y0, x1, y1, inside = fp
    c = V[vi].mean(0)
    shelf = abs(c[0]) < 0.25 and c[1] > 0.80          # near the centreline, above the tail lamps (y 0.64-0.77)
    (kill if shelf else keep)[y0:y1, x0:x1] |= inside
lit = (im[..., 0] > 0) | (im[..., 1] > 0)
zero = lit & kill & ~keep
before = int(lit.sum())
im[zero, 0] = 0; im[zero, 1] = 0
Image.fromarray(im, 'RGBA').save(p)
print('lux_coupe_97: R/G texels %d -> %d (%d on the parcel shelf zeroed; %d shelf texels shared with other faces kept)' % (before, int(((im[..., 0] > 0) | (im[..., 1] > 0)).sum()), int(zero.sum()), int((lit & kill & keep).sum())))

# ---- pony_coupe_67: the scatter channel
p = os.path.join(LAMPS, 'pony_coupe_67_lightmask.png')
im = np.array(Image.open(p).convert('RGBA'))
a0 = int((im[..., 3] > 0).sum())
im[..., 3] = np.clip(np.round(im[..., 2].astype(np.float64) * 0.35), 0, 255).astype(np.uint8)
Image.fromarray(im, 'RGBA').save(p)
print('pony_coupe_67: scatter (A) texels %d -> %d (A = 0.35 x B; core texels %d)' % (a0, int((im[..., 3] > 0).sum()), int((im[..., 2] > 0).sum())))
