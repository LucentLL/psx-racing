// linncove_rocks.mjs - THE LINN COVE VIADUCT'S GRANITE, found in the 2018 lidar.
//
// Owner 2026-11-16, with two photographs of the viaduct in peak fall colour:
// "would also be nice to add some of the iconic rock face to the track and the
// title shot angle". The rocks are real: grey granite boulders and slabs on the
// ground under the deck between the piers, and rows of outcrops and blocks on
// Grandfather Mountain's slope above the road. In a 1 m bare-earth DEM a
// boulder or a ledge is abrupt relief: the ground minus a smoothed copy of
// itself stands up a metre or more over a few metres.
//
//   node tools/roads/linncove_rocks.mjs [--from 4380] [--to 4990] [--half 260]
//        [--dump <dir>]        (raw grids for a picture: h, residual, slope)
//
// Reads the 1 m tiles OFFLINE from the road bake's cache (PSX_ROADS_CACHE =
// the main checkout's tools/roads/cache); a tile that was never fetched is a
// hole, never a download. Writes Assets/PSXRacing/Art/BRP/brp_rocks.json:
// rock features in stage metres (x east, z north, y = the lidar ground less
// the stage's baseM), each with its size, height over the smoothed slope,
// the slope's fall line, and whether it stands under the deck.
//
// Credit: U.S. Geological Survey, 3D Elevation Program (public domain).
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { open3dep, dem1m, loadStage, toUtm } from './lib3dep.mjs';
import { makeProjection } from './lib.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..', '..');
const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d; };
const FROM = +arg('--from', 4380), TO = +arg('--to', 4990), HALF = +arg('--half', 260);
const DUMP = arg('--dump', null);

const st = loadStage(ROOT, 'brp');
const proj = makeProjection(st.json.lat0, st.json.lon0);
const baseM = st.json.baseM, spacing = st.json.spacing;
const bridges = st.json.bridges;
let via = [bridges[0], bridges[1]];
for (let k = 0; k < bridges.length; k += 2) if (bridges[k + 1] - bridges[k] > via[1] - via[0]) via = [bridges[k], bridges[k + 1]];
console.log(`viaduct ${via[0]}-${via[1]} m (${via[1] - via[0]} m), stations ${FROM}-${TO}, +-${HALF} m`);

// the road's points in the window, and the area round them
const i0 = Math.max(0, Math.floor(FROM / spacing)), i1 = Math.min(st.n - 1, Math.ceil(TO / spacing));
let x0 = 1e9, x1 = -1e9, z0 = 1e9, z1 = -1e9;
for (let i = i0; i <= i1; i++) { const p = st.wp[i]; x0 = Math.min(x0, p.x); x1 = Math.max(x1, p.x); z0 = Math.min(z0, p.z); z1 = Math.max(z1, p.z); }
x0 = Math.floor(x0 - HALF); x1 = Math.ceil(x1 + HALF); z0 = Math.floor(z0 - HALF); z1 = Math.ceil(z1 + HALF);
const W = x1 - x0 + 1, H = z1 - z0 + 1;
console.log(`grid ${W} x ${H} m at 1 m, x ${x0}..${x1}, z ${z0}..${z1}`);

// nearest road point, station and offset (signed: + = left of travel)
function roadFoot(x, z) {
  let best = 1e18, bi = i0;
  for (let i = Math.max(0, i0 - 60); i <= Math.min(st.n - 1, i1 + 60); i++) {
    const p = st.wp[i], d = (p.x - x) ** 2 + (p.z - z) ** 2;
    if (d < best) { best = d; bi = i; }
  }
  const a = st.wp[Math.max(0, bi - 1)], b = st.wp[Math.min(st.n - 1, bi + 1)];
  const tx = b.x - a.x, tz = b.z - a.z, L = Math.hypot(tx, tz) || 1;
  const off = (-(tz / L) * (x - st.wp[bi].x) + (tx / L) * (z - st.wp[bi].z));
  return { i: bi, s: bi * spacing, d: Math.sqrt(best), off, roadY: st.h[bi] };
}

const store = open3dep({ offline: true });
const dem = dem1m(store, 0);
// prepare tile by tile: a tile the cache does not hold is a hole
const utm = new Array(W * H);
for (let r = 0; r < H; r++) for (let c = 0; c < W; c++) {
  const ll = proj.toLL(x0 + c, z0 + r);
  utm[r * W + c] = toUtm(ll.lat, ll.lon);
}
const plan = dem.plan(utm);
console.log(`1 m tiles: ${plan.tiles} needed, ${plan.missing} not in the cache`);
const h = new Float32Array(W * H).fill(NaN);
{
  // group points by the 1 m file's own 512 px tile (dem1m's locate: a
  // 6 m border, rows from the north edge), a pixel in from each side so a
  // point's bilinear pair stays in its tile: a missing tile loses only its own
  const byTile = new Map();
  for (let k = 0; k < utm.length; k++) {
    const e = utm[k].e, n = utm[k].n;
    const kx = Math.floor(e / 10000), ky = Math.floor(n / 10000) + 1;
    const fx = e - (kx * 10000 - 6) - 0.5, fy = (ky * 10000 + 6) - n - 0.5;
    if ((fx % 512) < 1 || (fx % 512) > 510 || (fy % 512) < 1 || (fy % 512) > 510) continue;   // a tile's rim: left a hole
    const key = `x${kx}y${ky}/${Math.floor(fx / 512)}_${Math.floor(fy / 512)}`;
    if (!byTile.has(key)) byTile.set(key, []);
    byTile.get(key).push(k);
  }
  let holes = 0;
  for (const ks of byTile.values()) {
    const pts = ks.map(k => utm[k]);
    try {
      await dem.prepare(pts);
      for (const k of ks) h[k] = dem.sampleUtm(utm[k].e, utm[k].n) - baseM;
    } catch (e) { holes += ks.length; }
  }
  console.log(`read ${W * H - holes} points, ${holes} in holes`);
}

// smoothed ground: separable gaussian, NaN-aware
function blur(src, sigma) {
  const R = Math.ceil(sigma * 3), k = new Float32Array(2 * R + 1);
  for (let i = -R; i <= R; i++) k[i + R] = Math.exp(-0.5 * (i / sigma) ** 2);
  const tmp = new Float32Array(W * H), out = new Float32Array(W * H);
  for (let r = 0; r < H; r++) for (let c = 0; c < W; c++) {
    let s = 0, w = 0;
    for (let i = -R; i <= R; i++) { const cc = c + i; if (cc < 0 || cc >= W) continue; const v = src[r * W + cc]; if (v !== v) continue; s += v * k[i + R]; w += k[i + R]; }
    tmp[r * W + c] = w > 0 ? s / w : NaN;
  }
  for (let r = 0; r < H; r++) for (let c = 0; c < W; c++) {
    let s = 0, w = 0;
    for (let i = -R; i <= R; i++) { const rr = r + i; if (rr < 0 || rr >= H) continue; const v = tmp[rr * W + c]; if (v !== v) continue; s += v * k[i + R]; w += k[i + R]; }
    out[r * W + c] = w > 0 ? s / w : NaN;
  }
  return out;
}
const hs = blur(h, 7);
const res = new Float32Array(W * H), slope = new Float32Array(W * H);
for (let r = 1; r < H - 1; r++) for (let c = 1; c < W - 1; c++) {
  const k = r * W + c;
  res[k] = h[k] - hs[k];
  const dx = (h[k + 1] - h[k - 1]) / 2, dz = (h[k + W] - h[k - W]) / 2;
  slope[k] = Math.hypot(dx, dz);
}

// the road's own bench, cut and fill are not rock: mask the corridor
const foot = new Array(W * H);
const corridor = new Uint8Array(W * H);
for (let r = 0; r < H; r += 1) for (let c = 0; c < W; c += 1) {
  const f = roadFoot(x0 + c, z0 + r);
  foot[r * W + c] = f;
  const onDeck = f.s >= via[0] - 4 && f.s <= via[1] + 4;
  // off the deck the bench and its cut and fill reach ~14 m; on the deck
  // only the piers' own footing (a 4 m band under the line)
  if (f.d < (onDeck ? 4 : 14)) corridor[r * W + c] = 1;
}

// ROCK: residual over 1.0 m on a steep or abrupt patch; grow regions
const isRock = new Uint8Array(W * H);
for (let k = 0; k < W * H; k++) {
  if (corridor[k] || res[k] !== res[k]) continue;
  if (res[k] > 1.0 || (slope[k] > 1.4 && res[k] > 0.4)) isRock[k] = 1;
}
const label = new Int32Array(W * H).fill(-1);
const rocks = [];
for (let k0 = 0; k0 < W * H; k0++) {
  if (!isRock[k0] || label[k0] >= 0) continue;
  const stack = [k0]; label[k0] = rocks.length;
  const cells = [];
  while (stack.length) {
    const k = stack.pop(); cells.push(k);
    const r = (k / W) | 0, c = k % W;
    for (const [dr, dc] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const rr = r + dr, cc = c + dc; if (rr < 0 || rr >= H || cc < 0 || cc >= W) continue;
      const kk = rr * W + cc; if (!isRock[kk] || label[kk] >= 0) continue;
      label[kk] = rocks.length; stack.push(kk);
    }
  }
  if (cells.length < 6) { rocks.push(null); continue; }
  let sx = 0, sz = 0, peak = 0, pk = cells[0], minX = 1e9, maxX = -1e9, minZ = 1e9, maxZ = -1e9, steep = 0;
  for (const k of cells) {
    const r = (k / W) | 0, c = k % W;
    sx += c; sz += r;
    if (res[k] > peak) { peak = res[k]; pk = k; }
    minX = Math.min(minX, c); maxX = Math.max(maxX, c); minZ = Math.min(minZ, r); maxZ = Math.max(maxZ, r);
    if (slope[k] > 1.4) steep++;
  }
  // its extent's principal axis (second moments)
  const mx = sx / cells.length, mz = sz / cells.length;
  let cxx = 0, czz = 0, cxz = 0;
  for (const k of cells) { const r = (k / W) | 0, c = k % W; cxx += (c - mx) ** 2; czz += (r - mz) ** 2; cxz += (c - mx) * (r - mz); }
  cxx /= cells.length; czz /= cells.length; cxz /= cells.length;
  const tr = cxx + czz, det = cxx * czz - cxz * cxz, l1 = tr / 2 + Math.sqrt(Math.max(0, tr * tr / 4 - det)), l2 = tr - l1;
  const axis = Math.atan2(l1 - cxx, cxz || 1e-9) * 180 / Math.PI;
  // the fall line of the smoothed slope under it
  const kc = Math.round(mz) * W + Math.round(mx);
  const gx = (hs[kc + 1] - hs[kc - 1]) / 2, gz = (hs[kc + W] - hs[kc - W]) / 2;
  const fall = Math.atan2(-gx, -gz) * 180 / Math.PI;   // degrees from +z (north), clockwise to +x (east)
  const x = x0 + mx, z = z0 + mz;
  const f = roadFoot(x, z);
  rocks.push({
    x: +x.toFixed(1), z: +z.toFixed(1), y: +h[kc].toFixed(2),
    area: cells.length, long: +(4 * Math.sqrt(l1)).toFixed(1), wide: +(4 * Math.sqrt(Math.max(l2, 0))).toFixed(1), axis: +axis.toFixed(0),
    peak: +peak.toFixed(2), steepShare: +(steep / cells.length).toFixed(2), slope: +Math.hypot(gx, gz).toFixed(2), fall: +fall.toFixed(0),
    station: f.s, offset: +f.off.toFixed(1), road: +f.d.toFixed(1), roadY: +(f.roadY - baseM).toFixed(2),
    underDeck: f.s >= via[0] && f.s <= via[1] && f.d < 30,
  });
}
const found = rocks.filter(r => r);
found.sort((a, b) => b.peak * Math.sqrt(b.area) - a.peak * Math.sqrt(a.area));
console.log(`${found.length} rock features (>= 6 m2): ${found.filter(r => r.underDeck).length} under the deck, ` +
            `${found.filter(r => r.offset > 0).length} left of travel, ${found.filter(r => r.offset < 0).length} right`);
for (const r of found.slice(0, 25))
  console.log(`  s ${r.station} off ${r.offset} m  ${r.long}x${r.wide} m area ${r.area}  peak +${r.peak} m  steep ${r.steepShare}  slope ${r.slope}  ${r.underDeck ? 'UNDER DECK' : ''}`);

const out = {
  what: 'Linn Cove Viaduct rock features from the 1 m lidar bare earth (residual over a 7 m gaussian > 1 m, or steep and abrupt); tools/roads/linncove_rocks.mjs',
  credit: 'U.S. Geological Survey, 3D Elevation Program (public domain)',
  viaduct: via, stations: [FROM, TO], halfM: HALF, baseM,
  rocks: found,
};
const outPath = join(ROOT, 'Assets', 'PSXRacing', 'Art', 'BRP', 'brp_rocks.json');
writeFileSync(outPath, JSON.stringify(out, null, 1));
console.log('wrote ' + outPath);
if (DUMP) {
  mkdirSync(DUMP, { recursive: true });
  writeFileSync(join(DUMP, 'h.f32'), Buffer.from(h.buffer));
  writeFileSync(join(DUMP, 'res.f32'), Buffer.from(res.buffer));
  writeFileSync(join(DUMP, 'rock.u8'), Buffer.from(isRock.buffer));
  writeFileSync(join(DUMP, 'corr.u8'), Buffer.from(corridor.buffer));
  writeFileSync(join(DUMP, 'meta.json'), JSON.stringify({ W, H, x0, z0, road: st.wp.slice(i0, i1 + 1).map(p => [p.x - x0, p.z - z0]) }));
  console.log('dumped grids to ' + DUMP);
}
