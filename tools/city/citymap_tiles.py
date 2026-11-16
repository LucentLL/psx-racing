# citymap_tiles.py - draw the whole-city map as a tile pyramid (the track preview's ground).
#
# The owner, 2026-11-16: "It should be a picture of the entire city, the track drawn over it, then
# zoomed in to fit the track on screen for track preview before race". The picture is drawn ONCE,
# here, from the city's own data (tools/city/citymap_prep.mjs gathers it), as 512 px tiles:
#
#   level L covers the city square (SPAN m, centred on CX, CZ) with 2^L x 2^L tiles, so a tile is
#   SPAN / 2^L metres and a pixel SPAN / 2^L / 512: levels 0-4 cover the whole city (level 4 = 6.5 m/px);
#   level 5 = 3.25 m/px, 6 = 1.63, 7 = 0.81 and 8 = 0.41 m/px are drawn only round the venues (their squares
#   from TrackMapFrames.txt, the new courses' from --venues), which is what keeps a 400 m parking
#   deck and a 1 km sprint sharp without baking 20,000 tiles of woods.
#
# Out: Assets/StreamingAssets/CityMap/<L>/<tx>_<ty>.png (ty counts down from the north edge) and
# Assets/PSXRacing/Resources/CityMapIndex.txt ("CITYMAP 1 SPAN CX CZ TILE", then one line per level:
# "L <level> <tx>_<ty> ..."), which RouteMap reads to know what exists. A tile that is only ground
# colour is not written (the runtime fills the frame with the ground colour first).
#
# Style: the race-select card's own dark ground and grid; roads in greys by class at their painted
# width once the zoom allows it; water deep blue; tree canopy (Resources/charlotte_canopy.bytes, USFS
# NLCD 2024) as a dark green wash; commercial / industrial land a shade lighter; buildings and parking
# lots at the deep levels; rail lines. No labels (a menu label is drawn by the UI, sharp at any size).
#
# Run (Python 3 with PIL + numpy):
#   node tools/city/citymap_prep.mjs --out <layers.json> [--venues <extra.json>]
#   python -I tools/city/citymap_tiles.py <layers.json> [--levels 0-8] [--jobs 8]
import json, math, os, struct, sys, time
from multiprocessing import Pool
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
UNITY = os.path.normpath(os.path.join(HERE, '..', '..'))
OUTDIR = os.path.join(UNITY, 'Assets', 'StreamingAssets', 'CityMap')
INDEX = os.path.join(UNITY, 'Assets', 'PSXRacing', 'Resources', 'CityMapIndex.txt')
CANOPY = os.path.join(UNITY, 'Assets', 'PSXRacing', 'Resources', 'charlotte_canopy.bytes')

TILE = 512
SS = 2                       # supersampling: drawn at 2x, boxed down (antialiased edges)
SPAN = 53248.0               # the city square: level 6 = 1.625 m/px
CX, CZ = -1085.0, 5573.0     # the graph's box centre (x -24958..22789, z -20409..31555)
X0, Z1 = CX - SPAN / 2, CZ + SPAN / 2
FULL_LEVELS = 4              # 0..4 everywhere (6.5 m/px); deeper only round the venues
DEEP = {5: 1500.0, 6: 500.0, 7: 250.0, 8: 120.0}   # margin (m) round a venue's square at each deep level
DEEP_MAX_SPAN = {5: 1e9, 6: 1e9, 7: 1800.0, 8: 700.0}  # a level is baked only for venues framed this small or smaller

BG = (14, 16, 21)
GREEN = (21, 40, 29)
LAND = [(30, 30, 41), (31, 30, 43), (34, 32, 29)]          # commercial, retail, industrial
WATER = (24, 48, 82)
BLD = (40, 44, 55)
LOT = (24, 26, 33)
AISLE = (36, 39, 47)
RAIL = [(98, 90, 78), (78, 72, 64), (112, 98, 142), (112, 98, 142)]
ROAD = {0: (58, 62, 72), 1: (70, 75, 86), 2: (82, 88, 100), 3: (96, 102, 116), 4: (110, 116, 130), 5: (126, 132, 148)}
CASING = (10, 11, 15)

D = None   # the layers, loaded once per worker


def bbox(flat):
    xs = flat[0::2]; zs = flat[1::2]
    return (min(xs), min(zs), max(xs), max(zs))


class Grid:
    """A uniform 1 km bucket index over feature bounding boxes."""
    def __init__(self, items, cell=1000.0):
        self.cell = cell
        self.b = {}
        for i, bb in enumerate(items):
            for gx in range(int(math.floor(bb[0] / cell)), int(math.floor(bb[2] / cell)) + 1):
                for gz in range(int(math.floor(bb[1] / cell)), int(math.floor(bb[3] / cell)) + 1):
                    self.b.setdefault((gx, gz), []).append(i)

    def query(self, x0, z0, x1, z1):
        out = set()
        for gx in range(int(math.floor(x0 / self.cell)), int(math.floor(x1 / self.cell)) + 1):
            for gz in range(int(math.floor(z0 / self.cell)), int(math.floor(z1 / self.cell)) + 1):
                l = self.b.get((gx, gz))
                if l: out.update(l)
        return out


def load(path):
    j = json.load(open(path))
    L = {}
    for name in ('roads', 'water', 'land', 'rail', 'blds', 'aisles'):
        L[name] = j[name]
    L['venues'] = j['venues']
    pts_of = {'roads': 3, 'water': 2, 'land': 1, 'rail': 2, 'aisles': 1}
    for name, k in pts_of.items():
        L[name + '_bb'] = [bbox(f[k]) for f in L[name]]
        L[name + '_ix'] = Grid(L[name + '_bb'])
    L['blds_bb'] = [bbox(f) for f in L['blds']]
    L['blds_ix'] = Grid(L['blds_bb'], 500.0)
    # canopy (PCAN v1): u32 magic | i32 1 | u32 nx, nz | f32 x0, z0, cell | u16 year | u16 steps | nx*nz u8
    with open(CANOPY, 'rb') as f:
        buf = f.read()
    magic, ver, nx, nz = struct.unpack_from('<IiII', buf, 0)
    cx0, cz0, cell = struct.unpack_from('<fff', buf, 16)
    year, steps = struct.unpack_from('<HH', buf, 28)
    grid = np.frombuffer(buf, dtype=np.uint8, count=nx * nz, offset=32).reshape(nz, nx).astype(np.float32) / steps
    L['canopy'] = (grid, cx0, cz0, cell)
    return L


def init(path):
    global D
    D = load(path)


def tile_box(level, tx, ty):
    t = SPAN / (1 << level)
    x0 = X0 + tx * t
    z1 = Z1 - ty * t
    return x0, z1 - t, x0 + t, z1


def canopy_wash(x0, z0, x1, z1, n):
    grid, cx0, cz0, cell = D['canopy']
    nz, nx = grid.shape
    xs = x0 + (np.arange(n) + 0.5) * (x1 - x0) / n
    zs = z1 - (np.arange(n) + 0.5) * (z1 - z0) / n
    fx = np.clip((xs - cx0) / cell, 0, nx - 1.001)
    fz = np.clip((zs - cz0) / cell, 0, nz - 1.001)
    ix = fx.astype(np.int32); iz = fz.astype(np.int32)
    tx = (fx - ix)[None, :]; tz = (fz - iz)[:, None]
    a = grid[iz[:, None], ix[None, :]]; b = grid[iz[:, None], ix[None, :] + 1]
    c = grid[iz[:, None] + 1, ix[None, :]]; d = grid[iz[:, None] + 1, ix[None, :] + 1]
    v = (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz
    inside = ((xs >= cx0) & (xs <= cx0 + cell * (nx - 1)))[None, :] & ((zs >= cz0) & (zs <= cz0 + cell * (nz - 1)))[:, None]
    v = np.where(inside, v, 0.0)
    w = np.floor(np.clip(v, 0, 0.999) * 4.0) / 3.0 * 0.9   # four flat tones: 0, 30, 60, 90 % of the way to GREEN
    bg = np.array(BG, np.float32); gr = np.array(GREEN, np.float32)
    img = bg[None, None, :] * (1 - w[..., None]) + gr[None, None, :] * w[..., None]
    return Image.fromarray(img.astype(np.uint8), 'RGB')


def render(job):
    level, tx, ty = job
    x0, z0, x1, z1 = tile_box(level, tx, ty)
    n = TILE * SS
    mpp = (x1 - x0) / n                 # metres per supersampled pixel
    img = canopy_wash(x0, z0, x1, z1, n)
    dr = ImageDraw.Draw(img)
    P = lambda flat: [((flat[i] - x0) / mpp, (z1 - flat[i + 1]) / mpp) for i in range(0, len(flat), 2)]
    pad = 60.0
    q = (x0 - pad, z0 - pad, x1 + pad, z1 + pad)
    px_m = mpp * SS                     # metres per output pixel

    # land use
    if level >= 1:
        for i in sorted(D['land_ix'].query(*q)):
            k, flat = D['land'][i]
            if len(flat) >= 6: dr.polygon(P(flat), fill=LAND[k])
    # parking lots (outlines filled) at the deep levels
    if level >= 6:
        for i in sorted(D['aisles_ix'].query(*q)):
            k, flat = D['aisles'][i]
            if k == 1 and len(flat) >= 6: dr.polygon(P(flat), fill=LOT)
    # water: lakes, then creeks at their width
    for i in sorted(D['water_ix'].query(*q)):
        kind, width, flat = D['water'][i]
        if kind == 1 and len(flat) >= 6:
            dr.polygon(P(flat), fill=WATER)
    for i in sorted(D['water_ix'].query(*q)):
        kind, width, flat = D['water'][i]
        if kind == 0 and len(flat) >= 4:
            if level < 3 and width < 25: continue
            w = max(1.0 * SS, width / mpp) if level >= 4 else max(1.0 * SS * (0.6 if width < 10 else 1.0), width / mpp)
            dr.line(P(flat), fill=WATER, width=int(round(w)), joint='curve')
    # buildings
    if level >= 5:
        for i in sorted(D['blds_ix'].query(*q)):
            flat = D['blds'][i]
            if len(flat) >= 6: dr.polygon(P(flat), fill=BLD)
    # parking aisles
    if level >= 6:
        for i in sorted(D['aisles_ix'].query(*q)):
            k, flat = D['aisles'][i]
            if k == 0 and len(flat) >= 4: dr.line(P(flat), fill=AISLE, width=max(SS, int(round(5.0 / mpp))), joint='curve')
    # rail
    if level >= 2:
        for i in sorted(D['rail_ix'].query(*q)):
            k, tun, flat = D['rail'][i]
            if tun: continue
            if level < 4 and k == 1: continue
            w = max(SS * (1.0 if k != 1 else 0.7), 3.0 / mpp)
            dr.line(P(flat), fill=RAIL[k], width=int(round(w)), joint='curve')
    # roads: lowest class first; at the deep levels a dark casing under each class so crossings read
    minrank = 3 if level <= 1 else 2 if level == 2 else 1 if level == 3 else 0
    ids = [i for i in D['roads_ix'].query(*q) if D['roads'][i][0] >= minrank]
    ids.sort(key=lambda i: (1 if D['roads'][i][1] & 4 else 0, D['roads'][i][0], D['roads'][i][1] & 1 == 0))

    def road_w(rank, flags, width):
        if level >= 5:
            base = width / mpp
            floor = {0: 1.4, 1: 1.6, 2: 1.8, 3: 2.0, 4: 2.2, 5: 2.6}[rank] * SS
            return max(base, floor)
        return {0: 0.8, 1: 1.0, 2: 1.2, 3: 1.5, 4: 1.9, 5: 2.4}[rank] * SS * (1.0 + 0.25 * level)

    # one layer at a time (a bridge is its own layer, over everything at grade): every casing of the
    # layer, then every fill, so two pieces of one road meeting at a node show no dark seam
    def layer_of(i):
        rank, flags = D['roads'][i][0], D['roads'][i][1]
        return (1 if flags & 4 else 0, rank)
    layers = {}
    for i in ids:
        if D['roads'][i][1] & 8: continue   # tunnels are not drawn
        layers.setdefault(layer_of(i), []).append(i)
    for key in sorted(layers):
        group = layers[key]
        if level >= 5:
            for i in group:
                rank, flags, width, flat = D['roads'][i]
                if rank >= 2 or flags & 4:
                    dr.line(P(flat), fill=CASING, width=int(round(road_w(rank, flags, width) + 2.0 * SS)), joint='curve')
        for i in group:
            rank, flags, width, flat = D['roads'][i]
            dr.line(P(flat), fill=ROAD[min(5, rank)], width=max(1, int(round(road_w(rank, flags, width)))), joint='curve')

    out = img.resize((TILE, TILE), Image.BOX)
    arr = np.asarray(out)
    if (np.abs(arr.astype(np.int16) - np.array(BG, np.int16)).max() <= 2):
        return (level, tx, ty, None)
    q8 = out.quantize(colors=48, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    d = os.path.join(OUTDIR, str(level))
    os.makedirs(d, exist_ok=True)
    p = os.path.join(d, f'{tx}_{ty}.png')
    q8.save(p, optimize=True)
    return (level, tx, ty, os.path.getsize(p))


def jobs_for(level, venues):
    n = 1 << level
    if level <= FULL_LEVELS:
        return [(level, tx, ty) for ty in range(n) for tx in range(n)]
    t = SPAN / n
    want = set()
    for v in venues:
        if v['span'] > DEEP_MAX_SPAN[level]: continue
        m = DEEP[level]
        x0 = v['cx'] - v['span'] / 2 - m; x1 = v['cx'] + v['span'] / 2 + m
        z0 = v['cz'] - v['span'] / 2 - m; z1 = v['cz'] + v['span'] / 2 + m
        for tx in range(max(0, int((x0 - X0) // t)), min(n - 1, int((x1 - X0) // t)) + 1):
            for ty in range(max(0, int((Z1 - z1) // t)), min(n - 1, int((Z1 - z0) // t)) + 1):
                want.add((level, tx, ty))
    return sorted(want)


def main():
    path = sys.argv[1]
    lv = '0-8'
    jobsn = 8
    if '--levels' in sys.argv: lv = sys.argv[sys.argv.index('--levels') + 1]
    if '--jobs' in sys.argv: jobsn = int(sys.argv[sys.argv.index('--jobs') + 1])
    a, b = (int(v) for v in lv.split('-')) if '-' in lv else (int(lv), int(lv))
    venues = json.load(open(path))['venues']
    t0 = time.time()
    found = {}
    with Pool(jobsn, initializer=init, initargs=(path,)) as pool:
        for level in range(a, b + 1):
            js = jobs_for(level, venues)
            # a level re-drawn replaces its folder's tiles
            d = os.path.join(OUTDIR, str(level))
            if os.path.isdir(d):
                for f in os.listdir(d):
                    if f.endswith('.png'): os.remove(os.path.join(d, f))
            res = pool.map(render, js, chunksize=4)
            kept = [(r[1], r[2]) for r in res if r[3] is not None]
            size = sum(r[3] for r in res if r[3] is not None)
            found[level] = kept
            print(f'level {level}: {len(js)} tiles drawn, {len(kept)} kept, {size / 1e6:.1f} MB, {time.time() - t0:.0f} s', flush=True)
    # the index: keep the levels this run did not draw as they were
    old = {}
    if os.path.exists(INDEX):
        for line in open(INDEX):
            f = line.split()
            if len(f) >= 2 and f[0] == 'L': old[int(f[1])] = f[2:]
    for k, v in found.items():
        old[k] = [f'{x}_{y}' for x, y in v]
    with open(INDEX, 'w', newline='\n') as f:
        f.write(f'CITYMAP 1 {SPAN:.0f} {CX:.1f} {CZ:.1f} {TILE}\n')
        for k in sorted(old):
            f.write('L ' + str(k) + ' ' + ' '.join(old[k]) + '\n')
    print('index', INDEX)


if __name__ == '__main__':
    main()
