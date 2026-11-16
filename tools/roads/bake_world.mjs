// bake_world.mjs - the place around a stage's road, in the stage's own frame.
//
//   node tools/roads/bake_world.mjs lure [--offline]
//   node tools/roads/bake_world.mjs lure --frame chimney     (the same place for the Park Road stage)
//
// Written for US 64/74A at Lake Lure as it stood BEFORE HURRICANE HELENE
// (2024-09-27). Every input is from before the storm:
//   * OpenStreetMap as of 2024-09-20 (Overpass attic queries, cached in
//     tools/roads/cache as attic_lakelure_<kind>_2024-09-20.json and
//     overpass_lakelure_2024-09-20.json);
//   * the USGS 3DEP 1 m lidar DEM NC_Phase5_2018 (flown 2018).
// It reads the stage the road bake made (Resources/<pre>_stage.json) and
// writes Art/<stage>/<pre>_world.json, which the stage builder's town and
// river passes read (PSXRacingBuilder.StageTown.cs / .StageRiver.cs):
//
//   lake       Lake Lure's outline (rings, an island as a hole) and its level.
//              The 2018 lidar was flown in a winter drawdown: its flattened
//              water stands at 298.26 m, ~3.6 m under full pond (990.5 ft,
//              Town of Lake Lure) - the mapped shore stands at a median
//              300.9 m in it. The lake is built at FULL POOL, 301.8 m. The
//              lidar has no lake bed: depth is estimated from the shore slope
//              (the drawdown band the lidar did see), capped at 20 m.
//   river      the Rocky Broad (OSM "Broad River") from above Bat Cave to the
//              lake: centreline, the water surface from the lidar (held
//              falling downstream, never under full pool), width from the OSM
//              river area, white water where it steepens, flow speed.
//   carve      near-grid cells lowered under the river (12 m cells cannot hold
//              a 20 m channel the 10 m DEM smooths over).
//   buildings  footprints within 150 m of the road with the lidar ground
//              under them and the OSM building class (no names are written).
//   roads      every road meeting the route: where, which side, its class and
//              width, and its first 70 m with lidar heights.
//   lots, paths, crossings  parking areas, footways / boardwalks / the
//              Flowering Bridge, and marked crossings, near the road.
//   parkRoad   the Chimney Rock Park Road's start, from the ChimneyRock
//              stage's own bake (same line, same heights), in this frame.
//
// --frame chimney writes Art/ChimneyRock/chimney_world.json: the same place in
// the ChimneyRock stage's frame (its projection and baseM) for the Park Road
// stage's start - Main Street as a street, the village's buildings within
// 450 m of the park road's mouth, the river and the lake, the park road from
// Main Street over its bridge to that stage's waypoint 0, and that stage's own
// near-grid carve under the river.

import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { makeProjection } from './lib.mjs';
import { open3dep, dem1m, toUtm } from './lib3dep.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..', '..');
const args = process.argv.slice(2);
const pre = args[0];
if (pre !== 'lure') { console.error('usage: node bake_world.mjs lure [--offline] [--frame chimney]'); process.exit(2); }
const FRAME = args.includes('--frame') ? args[args.indexOf('--frame') + 1] : 'lure';
const CACHE = process.env.PSX_ROADS_CACHE || join(HERE, 'cache');
const ART = join(ROOT, 'Assets', 'PSXRacing', 'Art', 'LakeLure');
const RES = join(ROOT, 'Assets', 'PSXRacing', 'Resources');
const attic = kind => {
  const p = join(CACHE, kind === 'roads' ? 'overpass_lakelure_2024-09-20.json' : `attic_lakelure_${kind}_2024-09-20.json`);
  if (!existsSync(p)) throw new Error(`missing ${p} (an Overpass attic extract dated 2024-09-20)`);
  return JSON.parse(readFileSync(p, 'utf8')).elements;
};

// ------------------------------------------------------------- the stage
const st = JSON.parse(readFileSync(join(RES, pre + '_stage.json'), 'utf8'));
const meta = JSON.parse(readFileSync(join(ART, pre + '_dem_meta.json'), 'utf8'));
const proj = makeProjection(st.lat0, st.lon0);
const baseM = st.baseM, SP = st.spacing || 4;
const N = st.xyz.length / 3;
const W = []; for (let i = 0; i < N; i++) W.push({ x: st.xyz[3 * i], y: st.xyz[3 * i + 1], z: st.xyz[3 * i + 2] });
const XZ = p => proj.toXZ(p.lat, p.lon);
const r2 = v => Math.round(v * 100) / 100;
const r1 = v => Math.round(v * 10) / 10;

// nearest station: a 25 m hash of the waypoints
const HC = 25, hash = new Map();
W.forEach((p, i) => { const k = Math.floor(p.x / HC) + ',' + Math.floor(p.z / HC); if (!hash.has(k)) hash.set(k, []); hash.get(k).push(i); });
function foot(x, z, reach = 400) {
  let best = { d: Infinity, i: -1, side: 0, t: 0 };
  const R = Math.ceil(reach / HC), cx = Math.floor(x / HC), cz = Math.floor(z / HC);
  for (let a = -R; a <= R; a++) for (let b = -R; b <= R; b++) {
    const l = hash.get((cx + a) + ',' + (cz + b)); if (!l) continue;
    for (const i of l) {
      const j = Math.min(N - 1, i + 1); if (j === i) continue;
      const ex = W[j].x - W[i].x, ez = W[j].z - W[i].z, L2 = ex * ex + ez * ez;
      const t = Math.max(0, Math.min(1, ((x - W[i].x) * ex + (z - W[i].z) * ez) / L2));
      const px = W[i].x + ex * t, pz = W[i].z + ez * t, d = Math.hypot(x - px, z - pz);
      if (d < best.d) best = { d, i, t, side: Math.sign(ex * (z - W[i].z) - ez * (x - W[i].x)) || 1 };
    }
  }
  // side: +1 = left of the direction of travel (x right-handed: ex*dz - ez*dx > 0 is LEFT when z is north and x east)
  return best;
}
const stationM = f => (f.i + f.t) * SP;

// polygon helpers
const inRing = (x, z, ring) => { let c = false; for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) { const a = ring[i], b = ring[j]; if ((a.z > z) !== (b.z > z) && x < (b.x - a.x) * (z - a.z) / (b.z - a.z) + a.x) c = !c; } return c; };
function assembleRings(members) {
  const segs = members.filter(m => m.type === 'way' && m.geometry).map(m => ({ role: m.role, pts: m.geometry.map(XZ) }));
  const key = p => p.x.toFixed(2) + ',' + p.z.toFixed(2);
  const rings = [];
  for (const role of ['outer', 'inner']) {
    const list = segs.filter(s => (s.role || 'outer') === role).map(s => s.pts);
    while (list.length) {
      let ring = list.shift(); let grew = true;
      while (grew && key(ring[0]) !== key(ring.at(-1))) {
        grew = false;
        for (let i = 0; i < list.length; i++) {
          const s = list[i];
          if (key(s[0]) === key(ring.at(-1))) ring = ring.concat(s.slice(1));
          else if (key(s.at(-1)) === key(ring.at(-1))) ring = ring.concat(s.slice(0, -1).reverse());
          else continue;
          list.splice(i, 1); grew = true; break;
        }
      }
      if (key(ring[0]) === key(ring.at(-1))) ring = ring.slice(0, -1);
      rings.push({ hole: role === 'inner' ? 1 : 0, pts: ring });
    }
  }
  return rings;
}
const flat = pts => { const o = []; for (const p of pts) o.push(r2(p.x), r2(p.z)); return o; };
function densify(pts, step) {
  const o = [pts[0]];
  for (let i = 0; i + 1 < pts.length; i++) {
    const a = pts[i], b = pts[i + 1], L = Math.hypot(b.x - a.x, b.z - a.z), m = Math.max(1, Math.round(L / step));
    for (let j = 1; j <= m; j++) o.push({ x: a.x + (b.x - a.x) * j / m, z: a.z + (b.z - a.z) * j / m });
  }
  return o;
}
const lenOf = pts => { let s = 0; for (let i = 1; i < pts.length; i++) s += Math.hypot(pts[i].x - pts[i - 1].x, pts[i].z - pts[i - 1].z); return s; };
function clipLength(pts, maxM) {
  const o = [pts[0]]; let s = 0;
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1], b = pts[i], L = Math.hypot(b.x - a.x, b.z - a.z);
    if (s + L >= maxM) { const t = (maxM - s) / L; o.push({ x: a.x + (b.x - a.x) * t, z: a.z + (b.z - a.z) * t }); return o; }
    s += L; o.push(b);
  }
  return o;
}

// ---------------------------------------------------------------- lidar
const store = open3dep({ offline: args.includes('--offline') });
const L0 = dem1m(store, 0), L1 = dem1m(store, 1), L2 = dem1m(store, 2);
const ll = p => proj.toLL(p.x, p.z);
async function heights(dem, pts) { const q = pts.map(ll); await dem.prepare(q); return q.map(p => dem.sample(p.lat, p.lon)); }
const zone = s => s < 5000 ? 'lake' : s < 6300 ? 'downtown' : s < 6850 ? 'inlet' : s < 8100 ? 'village' : s < 10950 ? 'gorge' : 'batcave';

// ------------------------------------------------------------------ lake
const LAKE_LEVEL_M = 301.8, LIDAR_WATER_M = 298.26, MAX_DEPTH_M = 20;
const waterrel = attic('waterrel');
const lakeRel = waterrel.find(e => e.id === 195723);
const lakeRings = assembleRings(lakeRel.members);
const lakeOuter = lakeRings.filter(r => !r.hole).sort((a, b) => b.pts.length - a.pts.length)[0].pts;
// the shore slope: from each mapped shore point walk 40 m into the lake to where the lidar reaches its flattened water
let slopeS = [];
{
  const ring = densify(lakeOuter, 25);
  const probes = [];
  for (let i = 0; i < ring.length; i += 2) {
    const a = ring[i], b = ring[(i + 1) % ring.length];
    const tx = b.x - a.x, tz = b.z - a.z, L = Math.hypot(tx, tz) || 1;
    let nx = -tz / L, nz = tx / L;
    if (!inRing(a.x + nx * 6, a.z + nz * 6, lakeOuter)) { nx = -nx; nz = -nz; }
    probes.push({ a, nx, nz });
  }
  const pts = []; for (const p of probes) for (let m = 0; m <= 40; m += 2) pts.push({ x: p.a.x + p.nx * m, z: p.a.z + p.nz * m });
  const h = await heights(L1, pts);
  let k = 0;
  for (const p of probes) {
    const prof = h.slice(k, k + 21); k += 21;
    const top = prof[0], at = prof.findIndex(v => v <= LIDAR_WATER_M + 0.05);
    if (at > 0 && top > LIDAR_WATER_M + 1.0 && top < LAKE_LEVEL_M + 2) slopeS.push((top - LIDAR_WATER_M) / (at * 2));
  }
}
slopeS.sort((a, b) => a - b);
const shoreSlope = slopeS.length ? slopeS[slopeS.length >> 1] : 0.2;
console.log(`lake: ${lakeRings.length} ring(s), outer ${lakeOuter.length} points; full pool ${LAKE_LEVEL_M} m (lidar water ${LIDAR_WATER_M} m); shore slope median ${shoreSlope.toFixed(3)} from ${slopeS.length} profiles`);

// ----------------------------------------------------------------- river
const rivers = attic('rivers');
const riverArea = assembleRings(waterrel.find(e => e.id === 195618).members);
const riverOuter = riverArea.filter(r => !r.hole).map(r => r.pts), riverHoles = riverArea.filter(r => r.hole).map(r => r.pts);
const inRiver = (x, z) => riverOuter.some(r => inRing(x, z, r)) && !riverHoles.some(r => inRing(x, z, r));
const rway = rivers.find(e => e.id === 728892126);
let rline = rway.geometry.map(XZ);
{ // from the top down to 60 m inside the lake
  const o = []; let inside = 0;
  for (const p of densify(rline, 6)) { o.push(p); if (inRing(p.x, p.z, lakeOuter)) { inside += 6; if (inside > 60) break; } }
  rline = o;
}
// the reach the road sees: from 400 m above the route's end down
{
  let first = 0;
  for (let i = 0; i < rline.length; i++) { const f = foot(rline[i].x, rline[i].z, 600); if (f.d < 350) { first = Math.max(0, i - 40); break; } }
  rline = rline.slice(first);
}
const RN = rline.length;
const rtan = rline.map((p, i) => { const a = rline[Math.max(0, i - 1)], b = rline[Math.min(RN - 1, i + 1)], L = Math.hypot(b.x - a.x, b.z - a.z) || 1; return { x: (b.x - a.x) / L, z: (b.z - a.z) / L }; });
// surface: lowest lidar ground within 2 m of the line (the water or the bed), held falling, never under full pool
const rpts = []; for (let i = 0; i < RN; i++) for (const o of [-2, -1, 0, 1, 2]) rpts.push({ x: rline[i].x - rtan[i].z * o, z: rline[i].z + rtan[i].x * o });
const rh = await heights(L0, rpts);
let ry = []; for (let i = 0; i < RN; i++) { let m = Infinity; for (let k = 0; k < 5; k++) { const v = rh[i * 5 + k]; if (Number.isFinite(v)) m = Math.min(m, v); } ry.push(m); }
for (let i = 0; i < RN; i++) if (!Number.isFinite(ry[i])) ry[i] = i ? ry[i - 1] : LAKE_LEVEL_M + 50;
for (let i = 1; i < RN; i++) ry[i] = Math.min(ry[i], ry[i - 1]);
{ // light smoothing that keeps it falling
  const s = ry.slice(); for (let i = 2; i < RN - 2; i++) s[i] = (ry[i - 2] + ry[i - 1] + ry[i] + ry[i + 1] + ry[i + 2]) / 5; ry = s;
  for (let i = 1; i < RN; i++) ry[i] = Math.min(ry[i], ry[i - 1]);
}
ry = ry.map(v => Math.max(v + 0.25, LAKE_LEVEL_M + 0.02));      // the water a hand over the lidar's lowest, and the lake backs the river up to full pool
for (let i = 1; i < RN; i++) ry[i] = Math.min(ry[i], ry[i - 1]);
// width from the river area: walk out each side to its edge
const rw = [];
for (let i = 0; i < RN; i++) {
  const p = rline[i], nx = -rtan[i].z, nz = rtan[i].x;
  let wl = 0, wr = 0;
  if (inRiver(p.x, p.z)) {
    for (let m = 0.5; m < 45; m += 0.5) { if (!inRiver(p.x + nx * m, p.z + nz * m)) break; wl = m; }
    for (let m = 0.5; m < 45; m += 0.5) { if (!inRiver(p.x - nx * m, p.z - nz * m)) break; wr = m; }
  }
  rw.push(wl + wr > 4 ? r1(wl + wr) : 0);
}
{ // fill gaps (line outside the mapped area) with neighbours, smooth, clamp
  let last = 14; for (let i = 0; i < RN; i++) { if (rw[i]) last = rw[i]; else rw[i] = last; }
  const s = rw.slice(); for (let i = 3; i < RN - 3; i++) { let a = 0; for (let k = -3; k <= 3; k++) a += rw[i + k]; s[i] = a / 7; }
  for (let i = 0; i < RN; i++) rw[i] = r1(Math.max(7, Math.min(40, s[i])));
}
const grade = i => { const a = Math.max(0, i - 4), b = Math.min(RN - 1, i + 4); let L = 0; for (let k = a; k < b; k++) L += Math.hypot(rline[k + 1].x - rline[k].x, rline[k + 1].z - rline[k].z); return L > 0 ? (ry[a] - ry[b]) / L : 0; };
const rwhite = [], rspeed = [];
for (let i = 0; i < RN; i++) { const g = grade(i); rwhite.push(r2(Math.max(0, Math.min(1, (g - 0.012) / 0.03)))); rspeed.push(r2(ry[i] <= LAKE_LEVEL_M + 0.05 ? 0.15 : Math.max(0.4, Math.min(4, 0.5 + 25 * g)))); }
const riverFall = ry[0] - ry[RN - 1];
console.log(`river: ${(RN * 6 / 1000).toFixed(2)} km of the Rocky Broad, ${ry[0].toFixed(1)} -> ${ry[RN - 1].toFixed(1)} m (falls ${riverFall.toFixed(1)} m), width median ${[...rw].sort((a, b) => a - b)[RN >> 1]} m, white water on ${rwhite.filter(v => v > 0.3).length * 6} m`);

// carve: near-grid cells inside the river area, lowered under its water
const near = meta.near, nearBuf = readFileSync(join(ART, pre + '_dem_near.bytes'));
const carveIdx = [], carveH = [];
{
  let lo = Infinity, hi = -Infinity, lz = Infinity, hz = -Infinity;
  for (const p of rline) { lo = Math.min(lo, p.x); hi = Math.max(hi, p.x); lz = Math.min(lz, p.z); hz = Math.max(hz, p.z); }
  const c0 = Math.max(0, Math.floor((lo - 60 - near.originX) / near.cell)), c1 = Math.min(near.cols - 1, Math.ceil((hi + 60 - near.originX) / near.cell));
  const r0 = Math.max(0, Math.floor((lz - 60 - near.originZ) / near.cell)), r1_ = Math.min(near.rows - 1, Math.ceil((hz + 60 - near.originZ) / near.cell));
  for (let r = r0; r <= r1_; r++) for (let c = c0; c <= c1; c++) {
    const x = near.originX + c * near.cell, z = near.originZ + r * near.cell;
    if (!inRiver(x, z) || inRing(x, z, lakeOuter)) continue;
    let bi = 0, bd = Infinity; for (let i = 0; i < RN; i++) { const d = (rline[i].x - x) ** 2 + (rline[i].z - z) ** 2; if (d < bd) { bd = d; bi = i; } }
    const want = Math.round((ry[bi] - 1.4 - baseM) * 10), k = r * near.cols + c, have = nearBuf.readInt16LE(k * 2);
    // never under the road's own corridor: the road bake owns the ground within 9 m of the centreline
    const f = foot(x, z, 60); if (f.d < 9) continue;
    if (want < have) { carveIdx.push(k); carveH.push(want); }
  }
}
console.log(`carve: ${carveIdx.length} near cells lowered under the river`);

let chCarve = { idx: [], dm: [] };
if (FRAME === 'chimney') {
  const cs = JSON.parse(readFileSync(join(RES, 'chimney_stage.json'), 'utf8'));
  const cm = JSON.parse(readFileSync(join(ROOT, 'Assets', 'PSXRacing', 'Art', 'ChimneyRock', 'chimney_dem_meta.json'), 'utf8')).near;
  const cbuf = readFileSync(join(ROOT, 'Assets', 'PSXRacing', 'Art', 'ChimneyRock', 'chimney_dem_near.bytes'));
  const cp = makeProjection(cs.lat0, cs.lon0);
  const cw = []; for (let i = 0; i < cs.xyz.length / 3; i++) cw.push({ x: cs.xyz[3 * i], z: cs.xyz[3 * i + 2] });
  for (let r = 0; r < cm.rows; r++) for (let c = 0; c < cm.cols; c++) {
    const x = cm.originX + c * cm.cell, z = cm.originZ + r * cm.cell;
    const q = cp.toLL(x, z), p = proj.toXZ(q.lat, q.lon);
    if (!inRiver(p.x, p.z) || inRing(p.x, p.z, lakeOuter)) continue;
    let bd = Infinity; for (const w of cw) bd = Math.min(bd, (w.x - x) ** 2 + (w.z - z) ** 2); if (bd < 81) continue;   // that stage's own road corridor
    let bi = 0, bd2 = Infinity; for (let i = 0; i < RN; i++) { const d = (rline[i].x - p.x) ** 2 + (rline[i].z - p.z) ** 2; if (d < bd2) { bd2 = d; bi = i; } }
    const want = Math.round((ry[bi] - 1.4 - cs.baseM) * 10), k = r * cm.cols + c;
    if (want < cbuf.readInt16LE(k * 2)) { chCarve.idx.push(k); chCarve.dm.push(want); }
  }
}

// -------------------------------------------------------------- buildings
const bld = attic('buildings').filter(e => e.type === 'way' && e.geometry && e.geometry.length >= 4);
const B = [];
for (const e of bld) {
  let ring = e.geometry.map(XZ); if (Math.hypot(ring[0].x - ring.at(-1).x, ring[0].z - ring.at(-1).z) < 0.05) ring = ring.slice(0, -1);
  const cx = ring.reduce((a, p) => a + p.x, 0) / ring.length, cz = ring.reduce((a, p) => a + p.z, 0) / ring.length;
  const f = foot(cx, cz, 200); if (f.d > 150) continue;
  let area = 0; for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) area += (ring[j].x + ring[i].x) * (ring[j].z - ring[i].z); area = Math.abs(area) / 2;
  const t = e.tags || {};
  B.push({ e, ring, cx, cz, f, area, t });
}
{
  const pts = []; for (const b of B) { pts.push({ x: b.cx, z: b.cz }); for (const p of b.ring) pts.push(p); }
  const h = await heights(L1, pts); let k = 0;
  for (const b of B) { const v = h.slice(k, k + 1 + b.ring.length).filter(Number.isFinite); k += 1 + b.ring.length; b.g0 = Math.min(...v); b.g1 = Math.max(...v); }
}
const kindOf = b => b.t.building || 'yes';
const faceOf = b => { const i = b.f.i, j = Math.min(N - 1, i + 1); const px = W[i].x + (W[j].x - W[i].x) * b.f.t, pz = W[i].z + (W[j].z - W[i].z) * b.f.t;
  const dx = px - b.cx, dz = pz - b.cz, L = Math.hypot(dx, dz) || 1; return { fx: r2(dx / L), fz: r2(dz / L) }; };
// A ROLE, never the name: the footprints carry real business names, and only what the place IS reaches the game.
const ROLES = [[/lake lure inn/i, 'inn'], [/town hall|municipal/i, 'townhall'], [/welcome|visitor|chamber/i, 'welcome'], [/arcade/i, 'arcade'],
  [/post office/i, 'postoffice'], [/church|chapel/i, 'church'], [/boat ?house|marina|boat/i, 'boathouse'], [/beach/i, 'beach'], [/fire|rescue/i, 'firehall']];
const roleOf = b => { const n = b.t.name || ''; for (const [re, r] of ROLES) if (re.test(n)) return r; return b.t.building === 'church' ? 'church' : b.t.amenity === 'place_of_worship' ? 'church' : ''; };
console.log(`buildings: ${B.length} within 150 m (` + Object.entries(B.reduce((a, b) => { const k = zone(stationM(b.f)) + ':' + kindOf(b); a[k] = (a[k] || 0) + 1; return a; }, {})).map(([k, v]) => k + ' ' + v).join(', ') + ')');

// ------------------------------------------------------------- side roads
const roads = attic('roads');
const ROUTE = new Set([16764570, 1300422906, 16764567, 270316593, 270316592, 719032438, 730188243, 270149863, 270149866, 719032437, 738602468, 276156791, 276156790, 16763555, 718998123, 718998122, 16629582, 16618601]);
const routeNodes = new Set(); for (const w of roads) if (ROUTE.has(w.id)) for (const nd of w.nodes) routeNodes.add(nd);
const service = attic('service');
const WIDTH = { primary: 7.0, secondary: 6.7, tertiary: 6.1, unclassified: 5.5, residential: 5.5, service: 4.0, track: 3.5 };
const SR = [];
for (const w of roads.concat(service.filter(e => e.type === 'way' && e.tags && e.tags.highway))) {
  if (ROUTE.has(w.id) || !w.nodes) continue;
  const t = w.tags || {};
  for (let k = 0; k < w.nodes.length; k++) {
    if (!routeNodes.has(w.nodes[k])) continue;
    // outward: the longer way from the shared node
    const fwd = w.geometry.slice(k), back = w.geometry.slice(0, k + 1).reverse();
    for (const g of [fwd, back]) {
      if (g.length < 2) continue;
      let pts = g.map(XZ); const L = lenOf(pts);
      if (t.highway === 'service' && (L < 15 || t.service === 'parking_aisle')) continue;
      pts = densify(clipLength(pts, 70), 4);
      const f = foot(pts[0].x, pts[0].z, 50); if (f.d > 6) continue;
      SR.push({ w, t, pts, f, L });
    }
  }
}
{
  const all = []; for (const s of SR) for (const p of s.pts) all.push(p);
  const h = await heights(L0, all); let k = 0;
  for (const s of SR) { s.h = h.slice(k, k + s.pts.length); k += s.pts.length; for (let i = 0; i < s.h.length; i++) if (!Number.isFinite(s.h[i])) s.h[i] = i ? s.h[i - 1] : W[s.f.i].y + baseM; }
}
SR.sort((a, b) => stationM(a.f) - stationM(b.f));
console.log(`side roads: ${SR.length} mouths (` + Object.entries(SR.reduce((a, s) => { a[s.t.highway] = (a[s.t.highway] || 0) + 1; return a; }, {})).map(([k, v]) => k + ' ' + v).join(', ') + ')');

// ----------------------------------------------------- lots, paths, crossings
const lots = [];
for (const e of service) {
  if (e.type !== 'way' || !e.tags || e.tags.amenity !== 'parking' || !e.geometry) continue;
  let ring = e.geometry.map(XZ); if (ring.length > 3 && Math.hypot(ring[0].x - ring.at(-1).x, ring[0].z - ring.at(-1).z) < 0.05) ring = ring.slice(0, -1);
  const cx = ring.reduce((a, p) => a + p.x, 0) / ring.length, cz = ring.reduce((a, p) => a + p.z, 0) / ring.length;
  const f = foot(cx, cz, 120); if (f.d > 70) continue;
  lots.push({ ring, f, cx, cz, surface: e.tags.surface || '' });
}
{ const h = await heights(L1, lots.map(l => ({ x: l.cx, z: l.cz }))); lots.forEach((l, i) => l.y = h[i]); }
const paths = [];
for (const e of attic('paths')) {
  if (e.type !== 'way' || !e.geometry) continue;
  const pts = densify(e.geometry.map(XZ), 3);
  const fs = pts.map(p => foot(p.x, p.z, 80)); if (Math.min(...fs.map(f => f.d)) > 40) continue;
  const t = e.tags || {};
  paths.push({ pts, deck: (t.bridge && t.bridge !== 'no') ? 1 : 0, kind: t.footway === 'sidewalk' ? 'sidewalk' : t.bridge === 'boardwalk' ? 'boardwalk' : (t.highway || 'footway'), f: fs[fs.length >> 1] });
}
{ const all = []; for (const p of paths) all.push(...p.pts); const h = await heights(L0, all); let k = 0; for (const p of paths) { p.h = h.slice(k, k + p.pts.length); k += p.pts.length; } }
const crossings = [];
for (const e of attic('pois')) if (e.tags && e.tags.highway === 'crossing') { const f = foot(...Object.values(XZ(e)), 30); if (f.d < 12) crossings.push(r1(stationM(f))); }
console.log(`lots ${lots.length}, paths ${paths.length} (${paths.filter(p => p.deck).length} on decks), crossings ${crossings.length}`);

// -------------------------------------------------------------- park road
let parkRoad = null, parkLead = null, parkDeckFrom = 0;
{
  const cs = JSON.parse(readFileSync(join(RES, 'chimney_stage.json'), 'utf8'));
  const cp = makeProjection(cs.lat0, cs.lon0);
  const pts = [], ys = [];
  const upto = (cs.startLineM || 40) + 160;
  for (let i = 0; i * (cs.spacing || 4) <= upto && i < cs.xyz.length / 3; i++) {
    const q = cp.toLL(cs.xyz[3 * i], cs.xyz[3 * i + 2]); const p = proj.toXZ(q.lat, q.lon);
    pts.push(p); ys.push(cs.xyz[3 * i + 1] + cs.baseM);
  }
  const f0 = foot(pts[0].x, pts[0].z, 80), fE = foot(pts.at(-1).x, pts.at(-1).z, 300);
  // the lead from Main Street: the park road's own short ways and its river bridge (OSM 2024-09-20)
  const svc = attic('service'); const byId = new Map(svc.filter(e => e.type === 'way').map(e => [e.id, e]));
  let lead = [];
  for (const id of [267913865, 267913867, 267913868]) {
    const w = byId.get(id); if (!w) continue;
    let g = w.geometry.map(XZ);
    if (lead.length) { const e = lead.at(-1); if (Math.hypot(g[0].x - e.x, g[0].z - e.z) > Math.hypot(g.at(-1).x - e.x, g.at(-1).z - e.z)) g = g.reverse(); g = g.slice(1); }
    else { const fr = foot(g[0].x, g[0].z, 50), fl = foot(g.at(-1).x, g.at(-1).z, 50); if (fl.d < fr.d) g = g.reverse(); }
    if (id === 267913868) parkDeckFrom = lenOf(lead.length ? lead : [g[0]]);
    lead = lead.concat(g);
  }
  if (lead.length) {
    const e = lead.at(-1); if (Math.hypot(e.x - pts[0].x, e.z - pts[0].z) > 0.5) lead.push(pts[0]);
    const leadD = densify(lead, 2);
    const lh = await heights(L0, leadD);
    const deckTo = lenOf(leadD);
    // the bridge: straight between its abutments (the lidar under it is the river)
    const sAt = []; let acc = 0; leadD.forEach((p, i) => { if (i) acc += Math.hypot(p.x - leadD[i - 1].x, p.z - leadD[i - 1].z); sAt.push(acc); });
    const iA = sAt.findIndex(v => v >= parkDeckFrom); const yA = lh[Math.max(0, iA)], yB = ys[0];
    const ly = leadD.map((p, i) => sAt[i] < parkDeckFrom ? lh[i] : yA + (yB - yA) * (sAt[i] - parkDeckFrom) / Math.max(1, deckTo - parkDeckFrom));
    parkLead = { xz: flat(leadD), y: ly.map(v => r2(v - baseM)), deckFrom: r1(parkDeckFrom), deckTo: r1(deckTo) };
    console.log(`park road lead from Main Street: ${deckTo.toFixed(0)} m, its river bridge ${parkDeckFrom.toFixed(0)}-${deckTo.toFixed(0)} m (${(deckTo - parkDeckFrom).toFixed(0)} m) from ${yA.toFixed(1)} to ${yB.toFixed(1)} m`);
  }
  parkRoad = { xz: flat(pts), y: ys.map(v => r2(v - baseM)), st: r1(stationM(f0)), side: f0.side, d0: r2(f0.d), startLineM: cs.startLineM, spacing: cs.spacing || 4,
               leadXZ: parkLead ? parkLead.xz : [], leadY: parkLead ? parkLead.y : [], deckFrom: parkLead ? parkLead.deckFrom : 0, deckTo: parkLead ? parkLead.deckTo : 0,
               mouthSt: parkLead ? r1(stationM(foot(parkLead.xz[0], parkLead.xz[1], 50))) : 0 };
  console.log(`park road: ${pts.length} waypoints (${upto.toFixed(0)} m) from the ChimneyRock bake; its waypoint 0 stands ${f0.d.toFixed(1)} m off this road at ${stationM(f0).toFixed(0)} m, its end ${fE.d.toFixed(0)} m off`);
}

// ------------------------------------------------- round 2: open ground, piers
// Open ground the forest must not fill (owner's video, 8:15: the inn stood across an open lawn): parks, pitches,
// playgrounds, the beach, the marina and slipway, grass, retail and commercial land; and the marina's piers.
// Overpass attic 2024-09-20 in four small boxes (attic_lakelure_open_2024-09-20.json).
const OPEN = [], PIERS = [];
{
  const p = join(CACHE, 'attic_lakelure_open_2024-09-20.json');
  for (const e of existsSync(p) ? JSON.parse(readFileSync(p, 'utf8')).elements : []) {
    if (e.type !== 'way' || !e.geometry) continue;
    const t = e.tags || {}; const pts = e.geometry.map(XZ);
    const closed = Math.hypot(pts[0].x - pts.at(-1).x, pts[0].z - pts.at(-1).z) < 0.5;
    if (t.man_made === 'pier') { PIERS.push({ xz: flat(closed ? pts.slice(0, -1) : pts), area: closed ? 1 : 0 }); continue; }
    if (!closed || pts.length < 4) continue;
    OPEN.push({ kind: t.leisure || t.natural || t.landuse || '', xz: flat(pts.slice(0, -1)) });
  }
  console.log(`open ground: ${OPEN.length} areas (${[...new Set(OPEN.map(o => o.kind))].join(', ')}), piers: ${PIERS.length}`);
}

// ------------------------------------------------- round 2: the granite
// Bare rock where the 2018 lidar shows it: Chimney Rock's cliffs, Rumbling Bald, the gorge walls. A 6 m grid of the
// 1 m lidar at 4 m (its 2x2 overview) within 2.2 km of the road; a cell is CLIFF where the ground stands steeper than
// 52 degrees AND the land within 21 m spans more than 18 m (a cliff, not a steep bank). Written as 12 m cells: centre,
// ground, the downhill direction, steepness. Never on a gentle forested slope: the rule is the rock's own steepness.
const CLIFFS = [];
{
  let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity;
  for (const p of W) { x0 = Math.min(x0, p.x); x1 = Math.max(x1, p.x); z0 = Math.min(z0, p.z); z1 = Math.max(z1, p.z); }
  const M = 2200, G = 6;
  x0 -= M; x1 += M; z0 -= M; z1 += M;
  const nx = Math.floor((x1 - x0) / G) + 1, nz = Math.floor((z1 - z0) / G) + 1;
  // only the cells within 2.2 km of the road
  const coarse = W.filter((_, i) => i % 10 === 0); const nearRoad = (x, z) => coarse.some(q => (q.x - x) * (q.x - x) + (q.z - z) * (q.z - z) < M * M);
  const keep = new Uint8Array(nx * nz);
  const pts = [];
  for (let j = 0; j < nz; j += 4) for (let i = 0; i < nx; i += 4) { const x = x0 + i * G, z = z0 + j * G; if (nearRoad(x, z)) for (let b = 0; b < 4 && j + b < nz; b++) for (let a = 0; a < 4 && i + a < nx; a++) keep[(j + b) * nx + i + a] = 1; }
  for (let j = 0; j < nz; j++) for (let i = 0; i < nx; i++) if (keep[j * nx + i]) pts.push(proj.toLL(x0 + i * G, z0 + j * G));
  await L2.prepare(pts);
  const H = new Float32Array(nx * nz).fill(NaN);
  for (let j = 0, k = 0; j < nz; j++) for (let i = 0; i < nx; i++) if (keep[j * nx + i]) { const q = proj.toLL(x0 + i * G, z0 + j * G); H[j * nx + i] = L2.sample(q.lat, q.lon); }
  const h = (i, j) => (i < 0 || j < 0 || i >= nx || j >= nz) ? NaN : H[j * nx + i];
  const TAN = Math.tan(52 * Math.PI / 180);
  const steep = new Uint8Array(nx * nz), gx = new Float32Array(nx * nz), gz = new Float32Array(nx * nz);
  let nSteep = 0;
  for (let j = 1; j < nz - 1; j++) for (let i = 1; i < nx - 1; i++) {
    const dx = (h(i + 1, j) - h(i - 1, j)) / (2 * G), dz = (h(i, j + 1) - h(i, j - 1)) / (2 * G);
    if (!Number.isFinite(dx) || !Number.isFinite(dz)) continue;
    const g = Math.hypot(dx, dz); gx[j * nx + i] = dx; gz[j * nx + i] = dz;
    if (g < TAN) continue;
    let lo = Infinity, hi = -Infinity;
    for (let b = -3; b <= 3; b++) for (let a = -3; a <= 3; a++) { const v = h(i + a, j + b); if (Number.isFinite(v)) { lo = Math.min(lo, v); hi = Math.max(hi, v); } }
    if (hi - lo > 18) { steep[j * nx + i] = 1; nSteep++; }
  }
  // 12 m cells (2x2 samples) with at least two steep samples
  for (let j = 1; j + 1 < nz; j += 2) for (let i = 1; i + 1 < nx; i += 2) {
    let n = 0, sx = 0, sz = 0, y = Infinity, g = 0;
    for (const [a, b] of [[0, 0], [1, 0], [0, 1], [1, 1]]) { const k = (j + b) * nx + i + a; if (steep[k]) { n++; sx += gx[k]; sz += gz[k]; g += Math.hypot(gx[k], gz[k]); } if (Number.isFinite(H[k])) y = Math.min(y, H[k]); }
    if (n < 2) continue;
    const L = Math.hypot(sx, sz) || 1, x = x0 + (i + 0.5) * G, z = z0 + (j + 0.5) * G;
    // the road's own cut is the stage builder's: nothing within 25 m of the centreline
    if (foot(x, z, 40).d < 25) continue;
    CLIFFS.push(r1(x), r1(z), r2(y - baseM), r2(-sx / L), r2(-sz / L), r2(g / n));
  }
  console.log(`granite: ${nSteep} steep 6 m samples, ${CLIFFS.length / 6} cliff cells of 12 m within ${M} m of the road`);
}

// ---------------------------------------------------------- speed zones
const speeds = [];
{
  const mph = t => { const m = /(\d+)/.exec(t.maxspeed || ''); return m ? +m[1] : 0; };
  const marks = [];
  for (const w of roads) if (ROUTE.has(w.id) && w.geometry) {
    const v = mph(w.tags || {}); if (!v) continue;
    const g = w.geometry.map(XZ); const a = foot(g[0].x, g[0].z, 30), b = foot(g.at(-1).x, g.at(-1).z, 30);
    marks.push([Math.min(stationM(a), stationM(b)), Math.max(stationM(a), stationM(b)), v]);
  }
  marks.sort((p, q) => p[0] - q[0]);
  let cur = 0;
  for (const [s0, , v] of marks) if (v !== cur) { speeds.push({ st: r1(s0), mph: v }); cur = v; }
  console.log('speed zones: ' + speeds.map(z => `${z.mph} mph from ${z.st.toFixed(0)} m`).join(', '));
}

// ------------------------------------------------------------------ write
const out = {
  version: 1, stage: pre, baseM,
  source: 'tools/roads/bake_world.mjs lure: OpenStreetMap as of 2024-09-20 (Overpass attic; (c) OpenStreetMap contributors, ODbL) and the USGS 3DEP 1 m lidar DEM NC_Phase5_2018. Pre-Helene.',
  lakeLevelM: LAKE_LEVEL_M, lakeLevelY: r2(LAKE_LEVEL_M - baseM), lidarWaterM: LIDAR_WATER_M, shoreSlope: r2(shoreSlope), maxDepthM: MAX_DEPTH_M,
  lake: lakeRings.map(r => ({ hole: r.hole, xz: flat(r.pts) })),
  riverAreas: riverArea.map(r => ({ hole: r.hole, xz: flat(r.pts) })),
  river: { xz: flat(rline), y: ry.map(v => r2(v - baseM)), w: rw, white: rwhite, speed: rspeed },
  carve: { idx: carveIdx, dm: carveH },
  buildings: B.map(b => ({ xz: flat(b.ring), g0: r2(b.g0 - baseM), g1: r2(b.g1 - baseM), kind: kindOf(b), levels: +(b.t['building:levels'] || 0), roof: b.t['roof:shape'] || '', height: +(b.t.height || 0), st: r1(stationM(b.f)), side: b.f.side, dist: r1(b.f.d), area: r1(b.area), zone: zone(stationM(b.f)), role: roleOf(b), ...faceOf(b) })),
  roads: SR.map(s => ({ st: r1(stationM(s.f)), side: s.f.side, cls: s.t.highway, w: +(s.t.width || 0) || WIDTH[s.t.highway] || 5, lenM: r1(s.L), xz: flat(s.pts), y: s.h.map(v => r2(v - baseM)) })),
  lots: lots.map(l => ({ xz: flat(l.ring), y: r2(l.y - baseM), st: r1(stationM(l.f)), gravel: /gravel|dirt|unpaved/.test(l.surface) ? 1 : 0 })),
  paths: paths.map(p => ({ xz: flat(p.pts), y: p.h.map(v => r2((Number.isFinite(v) ? v : baseM) - baseM)), deck: p.deck, kind: p.kind, st: r1(stationM(p.f)) })),
  crossings,
  speeds,
  open: OPEN,
  piers: PIERS,
  cliffs: CLIFFS,
  parkRoad,
};
let text, outPath;
if (FRAME === 'lure') { text = JSON.stringify(out); outPath = join(ART, pre + '_world.json'); }
else if (FRAME === 'chimney') {
  const cs = JSON.parse(readFileSync(join(RES, 'chimney_stage.json'), 'utf8'));
  const cp = makeProjection(cs.lat0, cs.lon0), dy = baseM - cs.baseM;
  const xzTo = a => { const o = []; for (let k = 0; k + 1 < a.length; k += 2) { const q = proj.toLL(a[k], a[k + 1]), p2 = cp.toXZ(q.lat, q.lon); o.push(r2(p2.x), r2(p2.z)); } return o; };
  const yTo = a => a.map(v => r2(v + dy));
  const mouth = parkLead ? { x: parkLead.xz[0], z: parkLead.xz[1] } : { x: 0, z: 0 };
  const near = (a, m) => { for (let k = 0; k + 1 < a.length; k += 2) if (Math.hypot(a[k] - mouth.x, a[k + 1] - mouth.z) < m) return true; return false; };
  // Main Street through the village, 400 m either side of the park road's mouth, on this road's own line and heights
  const ms = parkRoad.mouthSt, i0 = Math.max(0, Math.floor((ms - 400) / SP)), i1 = Math.min(N - 1, Math.ceil((ms + 400) / SP));
  const sx = [], sy = []; for (let i = i0; i <= i1; i++) { sx.push(r2(W[i].x), r2(W[i].z)); sy.push(r2(W[i].y)); }
  const ch = {
    version: 1, stage: 'lure', frame: 'chimney', baseM: cs.baseM, source: out.source + ' Re-projected into the ChimneyRock stage frame.',
    lakeLevelM: out.lakeLevelM, lakeLevelY: r2(out.lakeLevelM - cs.baseM), lidarWaterM: out.lidarWaterM, shoreSlope: out.shoreSlope, maxDepthM: out.maxDepthM,
    lake: out.lake.map(r => ({ hole: r.hole, xz: xzTo(r.xz) })),
    riverAreas: out.riverAreas.map(r => ({ hole: r.hole, xz: xzTo(r.xz) })),
    river: { ...out.river, xz: xzTo(out.river.xz), y: yTo(out.river.y) },
    carve: chCarve,
    buildings: out.buildings.filter(b => near(b.xz, 450)).map(b => ({ ...b, xz: xzTo(b.xz), g0: r2(b.g0 + dy), g1: r2(b.g1 + dy) })),
    roads: [], lots: out.lots.filter(l => near(l.xz, 450)).map(l => ({ ...l, xz: xzTo(l.xz), y: r2(l.y + dy) })),
    paths: [], crossings: [], speeds: [],
    open: OPEN.filter(o => near(o.xz, 900)).map(o => ({ ...o, xz: xzTo(o.xz) })),
    piers: [],
    cliffs: (() => { const o = []; for (let k = 0; k + 5 < CLIFFS.length; k += 6) { const q = proj.toLL(CLIFFS[k], CLIFFS[k + 1]), p2 = cp.toXZ(q.lat, q.lon); o.push(r1(p2.x), r1(p2.z), r2(CLIFFS[k + 2] + dy), CLIFFS[k + 3], CLIFFS[k + 4], CLIFFS[k + 5]); } return o; })(),
    streets: [{ st: 0, side: 0, cls: 'primary', w: 7.0, lenM: (i1 - i0) * SP, xz: xzTo(sx), y: yTo(sy) }],
    parkRoad: { ...parkRoad, xz: xzTo(parkRoad.xz.slice(0, 2)), y: yTo(parkRoad.y.slice(0, 1)), leadXZ: xzTo(parkRoad.leadXZ), leadY: yTo(parkRoad.leadY) },
  };
  text = JSON.stringify(ch); outPath = join(ROOT, 'Assets', 'PSXRacing', 'Art', 'ChimneyRock', 'chimney_world.json');
  console.log(`chimney frame: ${ch.buildings.length} buildings, ${ch.lots.length} lots, Main Street ${(i1 - i0) * SP} m, carve ${chCarve.idx.length} cells`);
}
writeFileSync(outPath, text);
store.saveLedger();
console.log(`wrote ${outPath.split(/[\\/]/).slice(-3).join('/')} (${(text.length / 1024).toFixed(0)} KB); lidar read this run ${(store.sessionBytes() / 1e6).toFixed(1)} MB`);
