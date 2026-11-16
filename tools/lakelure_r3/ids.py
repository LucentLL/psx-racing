# Number the buildings in view of a locked pose on its video frame (their world-file index), to match each to the video.
import json, math, os, sys
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import overlay as O
HERE = os.path.dirname(os.path.abspath(__file__))
poses = {p['name']: p for p in json.load(open(os.path.join(HERE, 'poses_r3.json')))}
names = sys.argv[1].split(',')
ims = []
for n in names:
    p = poses[n]
    ps = O.pose(f"m={p['m']} yaw={p['yaw']}")
    im = Image.open(os.path.join(O.FR, p['frame'] + '.png')).convert('RGB'); d = ImageDraw.Draw(im)
    w, h = im.size
    rows = []
    for i, b in enumerate(O.W['buildings']):
        xz = b['xz']; ring = [(xz[2 * k], xz[2 * k + 1]) for k in range(len(xz) // 2)]
        g = b['g0']; top = g + 6.5
        qs = [O.proj(ps, (x, y, z), w, h) for x, z in ring for y in (g, top)]
        qs = [q for q in qs if q]
        if len(qs) < 4: continue
        u0, u1 = min(q[0] for q in qs), max(q[0] for q in qs); v0, v1 = min(q[1] for q in qs), max(q[1] for q in qs)
        dist = min(q[2] for q in qs)
        if u1 < 0 or u0 > w or dist > 400: continue
        d.rectangle([u0, v0, u1, v1], outline=(255, 0, 255) if b.get('role') else (0, 255, 255))
        d.text((max(0, u0) + 2, max(0, v0) + 1), str(i), fill=(255, 255, 0))
        rows.append(f"{n} #{i} {b.get('zone')}/{b.get('kind')}/{b.get('role') or '-'} st {b['st']:.0f} dist {b['dist']:.0f} area {b['area']:.0f} lv {b.get('levels')} near {dist:.0f} m")
    ims.append(im)
    print('\n'.join(rows))
sheet = Image.new('RGB', (640 * min(2, len(ims)), 360 * ((len(ims) + 1) // 2)))
for k, im in enumerate(ims): sheet.paste(im, ((k % 2) * 640, (k // 2) * 360))
sheet.save(os.path.join(HERE, 'ids_' + '_'.join(names) + '.png'))
