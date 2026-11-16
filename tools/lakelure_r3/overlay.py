# Round 3 pose finder: project the map (stage road, buildings, side roads, lots, lake shore) through a candidate
# dash-cam pose onto the owner's video frame. Reference only (frames stay in scratch).
#   python overlay.py <frame> <out.png> m=<metres> [lat=1.75] [h=1.4] [yaw=0] [pitch=0] [fov=80] [m2=...] ...
# Several poses: separate them with '|' -> one panel each, side by side.
import json, math, os, sys
from PIL import Image, ImageDraw, ImageFont
WT = r'C:/Users/mcgee/PSX Racing-lakelure/Assets/PSXRacing'
HERE = os.path.dirname(os.path.abspath(__file__))
FR = os.path.join(os.path.dirname(HERE), 'frames')
W = json.load(open(WT + '/Art/LakeLure/lure_world.json'))
ST = json.load(open(WT + '/Resources/lure_stage.json'))
SP = ST['spacing']
P = [(ST['xyz'][i], ST['xyz'][i + 1], ST['xyz'][i + 2]) for i in range(0, len(ST['xyz']), 3)]
HALF = 3.5

def at(m):
    f = m / SP; i = max(0, min(len(P) - 2, int(f))); t = f - i
    a, b = P[i], P[i + 1]
    p = tuple(a[k] + (b[k] - a[k]) * t for k in range(3))
    j0, j1 = max(0, i - 2), min(len(P) - 1, i + 3)
    hx, hz = P[j1][0] - P[j0][0], P[j1][2] - P[j0][2]; L = math.hypot(hx, hz)
    return p, (hx / L, hz / L)

def pose(spec):
    kv = dict(x.split('=') for x in spec.split())
    if 'x' in kv:
        cx, cy, cz = float(kv['x']), float(kv['y']), float(kv['z']); th = math.radians(float(kv['hdg']))
        f = (math.sin(th), math.cos(th))
    else:
        p, f = at(float(kv['m']))
        lat = float(kv.get('lat', 1.3)); h = float(kv.get('h', 1.4))
        r = (f[1], -f[0])
        cx, cy, cz = p[0] + r[0] * lat, p[1] + h, p[2] + r[1] * lat
        yaw = math.radians(float(kv.get('yaw', 0)))
        f = (f[0] * math.cos(yaw) + f[1] * math.sin(yaw), -f[0] * math.sin(yaw) + f[1] * math.cos(yaw))
    pitch = math.radians(float(kv.get('pitch', 0))); fov = float(kv.get('fov', 68))
    if 'pitch' not in kv and 'm' in kv:
        # the dash camera's mount (+10.7 deg, solved at 8:15 on flat ground) plus the road's own grade there
        a, _ = at(float(kv['m']) - 10); b, _ = at(float(kv['m']) + 10)
        pitch = math.radians(float(kv.get('mount', 10.7))) + math.atan2(b[1] - a[1], 20.0)
    kv.setdefault('lat', '1.3')
    hdg = (math.degrees(math.atan2(f[0], f[1])) + 360) % 360
    return dict(c=(cx, cy, cz), f=f, pitch=pitch, fov=fov, hdg=hdg, spec=spec)

def proj(ps, q, w, h):
    cx, cy, cz = ps['c']; fx, fz = ps['f']; rx, rz = fz, -fx
    dx, dy, dz = q[0] - cx, q[1] - cy, q[2] - cz
    xc = dx * rx + dz * rz; zc0 = dx * fx + dz * fz
    cp, sp = math.cos(ps['pitch']), math.sin(ps['pitch'])
    zc = zc0 * cp + dy * sp; yc = dy * cp - zc0 * sp
    if zc < 0.5: return None
    k = (w / 2) / math.tan(math.radians(ps['fov']) / 2)
    return (w / 2 + xc / zc * k, h / 2 - yc / zc * k, zc)

def line(d, ps, pts, w, h, col, width=1, maxd=1500):
    prev = None
    for q in pts:
        p = proj(ps, q, w, h)
        if p and p[2] < maxd and prev and abs(p[0]) < 5 * w and abs(prev[0]) < 5 * w: d.line([prev[:2], p[:2]], fill=col, width=width)
        prev = p

def panel(frame, spec, w=640, h=360):
    ps = pose(spec)
    im = Image.open(os.path.join(FR, frame)).convert('RGB').resize((w, h))
    d = ImageDraw.Draw(im)
    cx, _, cz = ps['c']
    near = lambda x, z, r=900: (x - cx) ** 2 + (z - cz) ** 2 < r * r
    # the road: edges white, centre yellow
    i0 = max(0, int(((0) if False else 0)))
    road = [p for p in P if near(p[0], p[2])]
    for off, col in ((-HALF, (255, 255, 255)), (HALF, (255, 255, 255)), (0, (255, 220, 0))):
        pts = []
        for i, p in enumerate(P):
            if not near(p[0], p[2]): continue
            j0, j1 = max(0, i - 1), min(len(P) - 1, i + 1)
            hx, hz = P[j1][0] - P[j0][0], P[j1][2] - P[j0][2]; L = math.hypot(hx, hz) or 1
            rx, rz = hz / L, -hx / L
            pts.append((p[0] + rx * off, p[1], p[2] + rz * off))
        line(d, ps, pts, w, h, col, 1)
    # side roads
    for r in W['roads']:
        xz = r['xz']; ys = r['y']
        pts = [(xz[2 * k], ys[k] if k < len(ys) else 0, xz[2 * k + 1]) for k in range(len(xz) // 2)]
        if not any(near(p[0], p[2], 600) for p in pts): continue
        line(d, ps, pts, w, h, (255, 140, 0), 1, 400)
    # buildings: ground ring and eave ring with corner posts
    for b in W['buildings']:
        xz = b['xz']; ring = [(xz[2 * k], xz[2 * k + 1]) for k in range(len(xz) // 2)]
        if not any(near(x, z, 700) for x, z in ring): continue
        g = b['g0']; lv = b.get('levels') or (3 if b.get('role') == 'inn' else 2 if b.get('zone') in ('village', 'downtown') else 1)
        top = g + lv * 3.2
        base = [(x, g, z) for x, z in ring] + [(ring[0][0], g, ring[0][1])]
        roof = [(x, top, z) for x, z in ring] + [(ring[0][0], top, ring[0][1])]
        col = (0, 255, 255) if b.get('role') != 'inn' else (255, 0, 255)
        line(d, ps, base, w, h, col, 1, 900); line(d, ps, roof, w, h, col, 1, 900)
        for x, z in ring: line(d, ps, [(x, g, z), (x, top, z)], w, h, col, 1, 900)
    # lots, the lake shore
    for l in W['lots']:
        xz = l['xz']; pts = [(xz[2 * k], l['y'], xz[2 * k + 1]) for k in range(len(xz) // 2)]
        line(d, ps, pts + pts[:1], w, h, (255, 0, 160), 1, 600)
    for ring in W['lake']:
        xz = ring['xz']; pts = [(xz[2 * k], W['lakeLevelY'], xz[2 * k + 1]) for k in range(len(xz) // 2)]
        if not any(near(p[0], p[2], 1500) for p in pts): continue
        line(d, ps, pts, w, h, (60, 120, 255), 1, 1500)
    if 'cliffs' in os.environ.get('OV', ''):
        C = W['cliffs']
        for k in range(0, len(C), 6):
            q = proj(ps, (C[k], C[k + 2], C[k + 1]), w, h)
            if q and q[2] < 4000: d.rectangle([q[0] - 1, q[1] - 1, q[0] + 1, q[1] + 1], fill=(255, 0, 0))
    d.text((4, h - 14), f"{spec}  hdg {ps['hdg']:.0f} pitch {math.degrees(ps['pitch']):.1f}", fill=(255, 255, 0))
    return im

if __name__ == '__main__':
    frame, out = sys.argv[1], sys.argv[2]
    specs = ' '.join(sys.argv[3:]).split('|')
    ims = [panel(frame, s.strip()) for s in specs]
    cols = 2 if len(ims) > 1 else 1
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new('RGB', (640 * cols, 360 * rows))
    for k, im in enumerate(ims): sheet.paste(im, ((k % cols) * 640, (k // cols) * 360))
    if len(ims) > 1: sheet = sheet.resize((sheet.size[0] * 3 // 4, sheet.size[1] * 3 // 4))
    sheet.save(out); print(out, sheet.size)
