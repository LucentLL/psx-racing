# Zero the R/G (rear running/brake) texels of a lamp mask whose body face is in the
# front half of the car: PSX/CarPaint lights R and G red, so a front parking lamp
# drawn into R would glow red. B/A (headlamp core/scatter) are untouched.
import sys
from PIL import Image
import numpy as np
obj, mask, out = sys.argv[1:4]
V, T, faces = [], [], []
for line in open(obj, encoding='utf-8'):
    if line.startswith('v '): V.append([float(x) for x in line.split()[1:4]])
    elif line.startswith('vt '): T.append([float(x) for x in line.split()[1:3]])
    elif line.startswith('f '):
        idx = [p.split('/') for p in line.split()[1:]]
        for i in range(1, len(idx) - 1):
            faces.append([idx[0], idx[i], idx[i + 1]])
V = np.array(V); T = np.array(T)
im = np.array(Image.open(mask).convert('RGBA')).astype(np.int32)
H, W = im.shape[:2]
zmap = np.full((H, W), np.nan)
for f in faces:
    vi = [int(p[0]) - 1 for p in f]; ti = [int(p[1]) - 1 for p in f]
    z = V[vi, 2].mean()
    uv = T[ti] * [W, H]; uv[:, 1] = H - uv[:, 1]
    x0, y0 = np.floor(uv.min(0)).astype(int); x1, y1 = np.ceil(uv.max(0)).astype(int)
    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W), min(y1, H)
    if x1 <= x0 or y1 <= y0: continue
    ys, xs = np.mgrid[y0:y1, x0:x1] + 0.5
    (ax, ay), (bx, by), (cx, cy) = uv
    d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(d) < 1e-12: continue
    l1 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
    l2 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
    inside = (l1 >= -1e-3) & (l2 >= -1e-3) & (1 - l1 - l2 >= -1e-3)
    sub = zmap[y0:y1, x0:x1]
    sub[inside & np.isnan(sub)] = z
zmin, zmax = np.nanmin(V[:, 2]), np.nanmax(V[:, 2])
mid = (zmin + zmax) / 2
head = (im[..., 2] > 0) | (im[..., 3] > 0)
hz = zmap[head & ~np.isnan(zmap)]
front_sign = np.sign(np.median(hz) - mid)
rg = (im[..., 0] > 0) | (im[..., 1] > 0)
front = ~np.isnan(zmap) & (np.sign(zmap - mid) == front_sign)
kill = rg & front
print('%s: headlamps at %s end; R/G texels %d, cleared at the front %d' % (mask.split('/')[-1], '+z' if front_sign > 0 else '-z', rg.sum(), kill.sum()))
im[kill, 0] = 0; im[kill, 1] = 0
Image.fromarray(im.astype(np.uint8), 'RGBA').save(out)
