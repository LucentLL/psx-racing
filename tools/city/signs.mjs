// signs.mjs - build Charlotte's sign data, charlotte_signs.bytes (plan WP-23)
//
//   node tools/city/signs.mjs --check            rebuild in memory, compare with the shipped file
//   node tools/city/signs.mjs --out <dir>        write <dir>/charlotte_signs.bytes
//   (either way it prints the numbers: route km, the zoning share, the
//   billboards each route asks for, the pole-sign businesses, the gantries)
//
// WHAT IT HOLDS (the runtime, Scripts/City/CitySigns.cs, places everything
// from the live graph, so the lines release moves the signs with the roads):
//
//   * a 60 m grid on the DEM's own lattice (as the canopy grid) of two bits:
//       1 BILLBOARD ZONING. North Carolina lets a billboard stand along an
//         interstate or a federal-aid primary only in a commercial or
//         industrial area within 660 ft of the right of way (19A NCAC 02E
//         .0203; an "unzoned commercial area" needs a business near it). The
//         game reads OSM landuse=commercial|retail|industrial within 100 m of
//         the cell, or a business within 150 m, as that area.
//       2 COMMERCIAL FRONTAGE: retail or commercial land at the cell, where
//         the business pole signs line the road even where OSM maps no shop;
//   * THE ROUTES and how dense their billboards are. The density is a
//     STATISTIC off NCDOT's outdoor-advertising permits (plan Q8's default:
//     NCDOT data for statistics and validation only, never positions),
//     measured per route in Mecklenburg by the plan's critic
//     (scratchpad critic/billboard_density.mjs, 2026-09-28): boards per km of
//     route, divided roads counted once. The routes it did not measure take
//     their class's figure (interstate 0.87, US/NC 0.60 per km). Each route's
//     OSM billboards count toward its figure; the rest are placed at random
//     on its ZONED stretches, so the rate stored is per km of zoned
//     carriageway;
//   * the 46 OSM billboards (advertising=billboard; ODbL), where they are;
//   * the businesses that would put up a pole sign (OSM shop, fast food,
//     restaurants, fuel, banks, motels ... within 70 m of a collector or
//     bigger), each with the kind of sign it would have;
//   * per motorway way that ends at an exit (a motorway_link diverge), the
//     lanes of each destination group from its OSM destination:lanes, which
//     is how many panels the exit gantry carries and over which lanes.
//
// PSGN v1, little-endian:
//   u32 'PSGN' | i32 1 | i32 nx, nz | f32 x0, z0, cell | nx*nz u8 grid (row z0 first)
//   u16 nRoutes { u8 len, name | u8 cls (1 interstate, 2 US/NC) | f32 perRouteKm | f32 perZonedCarriagewayKm }
//   u32 nWays { u32 wayId | u8 route }            sorted by wayId
//   u32 nBill { f32 x, z | f32 yawDeg (NaN: a node) | u8 flags (1 lit) }
//   u32 nPoi { f32 x, z | u8 kind }               sorted by x, z
//   u32 nGant { u32 wayId | u8 n | n x u8 lanes } sorted by wayId
// All OSM-derived data (c) OpenStreetMap contributors, ODbL 1.0.

import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliCompressSync, constants as Z } from 'node:zlib';
import { demLattice, LAT0, LON0, M_LAT, M_LON } from './lib/canopygrid.mjs';
import { parseCity } from './lib/citydata.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const RES = join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources');
const CACHE = join(HERE, 'cache');
const args = process.argv.slice(2);
const oi = args.indexOf('--out');
const outDir = oi >= 0 ? args[oi + 1] : null;
const check = args.includes('--check');
const t0 = Date.now();

const gx = lon => (lon - LON0) * M_LON, gz = lat => (lat - LAT0) * M_LAT;
const need = f => { if (!existsSync(f)) { console.error('missing ' + f + ' (node tools/city/fetch/fetch_layers.mjs; fetch_ways.mjs)'); process.exit(2); } return JSON.parse(readFileSync(f, 'utf8')); };

// ---- NCDOT permits per km of route, Mecklenburg (critic, 2026-09-28): boards, km
const NCDOT = {
  'I 77': [72, 60.2], 'I 85': [62, 41.6], 'I 485': [26, 90.1], 'I 277': [14, 7.1],
  'US 74': [48, 38.3], 'NC 16': [27, 48.1], 'US 29': [27, 38.8], 'NC 24': [17, 31.6],
  'NC 49': [12, 42.1], 'NC 27': [6, 38.5], 'US 21': [0, 41.6],
};
const CLASS_DENSITY = { 1: 174 / 199, 2: 167 / 279 };

const city = parseCity(readFileSync(join(RES, 'charlotte_city.bytes')));
const lat = demLattice(readFileSync(join(RES, 'charlotte_dem.bytes')));
const W = need(join(CACHE, 'ways_all.json'));
const tags = new Map();
for (const e of W.elements) if (e.type === 'way') tags.set(e.id, e.tags || {});
const ADV = need(join(CACHE, 'layers', 'advertising.json'));
const LAND = need(join(CACHE, 'layers', 'landuse.json'));
const BIZ = need(join(CACHE, 'layers', 'business.json'));

// ---- the route of every way: the FIRST ref it carries (a concurrency's
// boards were permitted under one route name), normalised "I-77" -> "I 77"
const normRef = r => r.trim().replace(/-/g, ' ').replace(/\s+/g, ' ');
const clsOfRef = r => /^I \d+$/.test(r) ? 1 : /^(US|NC) \d+$/.test(r) ? 2 : 0;
const routes = [], routeIdx = new Map(), wayRoute = new Map();
for (const e of city.edges) {
  if (e.link || wayRoute.has(e.wayId)) continue;
  const t = tags.get(e.wayId);
  if (!t || !t.ref) continue;
  const r = normRef(t.ref.split(';')[0]);
  const c = clsOfRef(r);
  if (!c) continue;
  if (!routeIdx.has(r)) {
    const m = NCDOT[r];
    routeIdx.set(r, routes.length);
    routes.push({ name: r, cls: c, density: m ? m[0] / m[1] : CLASS_DENSITY[c], measured: !!m, km: 0, zonedKm: 0, osm: 0 });
  }
  wayRoute.set(e.wayId, routeIdx.get(r));
}

// ---- the fine grid (20 m) the landuse and the businesses are painted on
const F = 3, fcell = lat.cell / F;
const fx0 = lat.x0 - lat.cell / 2, fz0 = lat.z0 - lat.cell / 2;
const fnx = lat.nx * F, fnz = lat.nz * F;
const land = new Uint8Array(fnx * fnz);   // 1 commercial/retail, 2 industrial, 4 a business
function fillPoly(ring, bit) {
  let minZ = Infinity, maxZ = -Infinity;
  for (const [, z] of ring) { minZ = Math.min(minZ, z); maxZ = Math.max(maxZ, z); }
  const i0 = Math.max(0, Math.floor((minZ - fz0) / fcell)), i1 = Math.min(fnz - 1, Math.floor((maxZ - fz0) / fcell));
  for (let iz = i0; iz <= i1; iz++) {
    const cz = fz0 + (iz + 0.5) * fcell, xs = [];
    for (let k = 0, n = ring.length; k < n; k++) {
      const [ax, az] = ring[k], [bx, bz] = ring[(k + 1) % n];
      if ((az > cz) !== (bz > cz)) xs.push(ax + (cz - az) / (bz - az) * (bx - ax));
    }
    xs.sort((a, b) => a - b);
    for (let k = 0; k + 1 < xs.length; k += 2) {
      const a = Math.max(0, Math.ceil((xs[k] - fx0) / fcell - 0.5)), b = Math.min(fnx - 1, Math.floor((xs[k + 1] - fx0) / fcell - 0.5));
      for (let ix = a; ix <= b; ix++) land[iz * fnx + ix] |= bit;
    }
  }
}
/// Join a relation's outer member ways into closed rings by their end points.
function rings(members) {
  const segs = members.filter(m => m.type === 'way' && (m.role === 'outer' || m.role === '') && m.geometry)
    .map(m => m.geometry.map(p => [gx(p.lon), gz(p.lat)]));
  const out = [];
  const key = p => p[0].toFixed(2) + ',' + p[1].toFixed(2);
  while (segs.length) {
    let ring = segs.shift();
    for (let guard = 0; guard < 500 && key(ring[0]) !== key(ring[ring.length - 1]); guard++) {
      const end = key(ring[ring.length - 1]);
      const j = segs.findIndex(s => key(s[0]) === end || key(s[s.length - 1]) === end);
      if (j < 0) break;
      const s = segs.splice(j, 1)[0];
      ring = ring.concat(key(s[0]) === end ? s.slice(1) : s.reverse().slice(1));
    }
    if (ring.length >= 4) out.push(ring);
  }
  return out;
}
let landPolys = 0;
for (const el of LAND.elements) {
  const lu = el.tags && el.tags.landuse;
  const bit = lu === 'industrial' ? 2 : 1;
  if (el.type === 'way' && el.geometry && el.geometry.length >= 4) { fillPoly(el.geometry.map(p => [gx(p.lon), gz(p.lat)]), bit); landPolys++; }
  else if (el.type === 'relation') for (const r of rings(el.members || [])) { fillPoly(r, bit); landPolys++; }
}

// ---- the businesses: kind of sign, and only those a collector or bigger
// passes within 70 m of (a pole sign stands at a road's frontage)
const KINDS = ['fuel', 'burger', 'pizza', 'restaurant', 'motel', 'bank', 'pharmacy', 'carwash', 'cars', 'tires', 'shop', 'grocery', 'bar'];
function kindOf(t) {
  const a = t.amenity, s = t.shop, c = (t.cuisine || '').toLowerCase();
  if (a === 'fuel') return 0;
  if (a === 'fast_food') return c.includes('pizza') ? 2 : 1;
  if (a === 'restaurant' || a === 'cafe' || a === 'ice_cream') return c.includes('pizza') ? 2 : c.includes('burger') ? 1 : 3;
  if (t.tourism === 'motel' || t.tourism === 'hotel') return 4;
  if (a === 'bank') return 5;
  if (a === 'pharmacy' || s === 'chemist') return 6;
  if (a === 'car_wash') return 7;
  if (s === 'car' || a === 'car_rental' || s === 'motorcycle') return 8;
  if (s === 'tyres' || s === 'car_repair' || s === 'car_parts') return 9;
  if (s === 'supermarket' || s === 'convenience' || s === 'alcohol' || s === 'greengrocer' || s === 'butcher') return 11;
  if (a === 'bar' || a === 'pub') return 12;
  if (s) return 10;
  return -1;
}
// a segment hash of the collector-and-bigger edges (not motorways: no business fronts a freeway)
const SH = 100, segHash = new Map();
for (const e of city.edges) {
  if (e.link || e.rank < 1 || e.rank >= 5 || e.tunnel) continue;
  for (let k = 0; k + 1 < e.pts.length; k++) {
    const [ax, az] = e.pts[k], [bx, bz] = e.pts[k + 1];
    const x0 = Math.floor(Math.min(ax, bx) / SH), x1 = Math.floor(Math.max(ax, bx) / SH);
    const z0 = Math.floor(Math.min(az, bz) / SH), z1 = Math.floor(Math.max(az, bz) / SH);
    for (let z = z0; z <= z1; z++) for (let x = x0; x <= x1; x++) {
      const kk = x + ',' + z; if (!segHash.has(kk)) segHash.set(kk, []); segHash.get(kk).push([ax, az, bx, bz, e.hw]);
    }
  }
}
function nearArterial(x, z, r) {
  let best = Infinity;
  for (let cz = Math.floor((z - r) / SH); cz <= Math.floor((z + r) / SH); cz++)
    for (let cx = Math.floor((x - r) / SH); cx <= Math.floor((x + r) / SH); cx++)
      for (const [ax, az, bx, bz, hw] of segHash.get(cx + ',' + cz) || []) {
        const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
        const t = L2 > 1e-9 ? Math.max(0, Math.min(1, ((x - ax) * dx + (z - az) * dz) / L2)) : 0;
        best = Math.min(best, Math.hypot(x - ax - dx * t, z - az - dz * t) - hw);
      }
  return best;
}
const pois = [];
let bizAll = 0;
const kindCount = new Array(KINDS.length).fill(0);
for (const el of BIZ.elements) {
  const t = el.tags || {};
  const k = kindOf(t);
  const p = el.type === 'node' ? el : el.center;
  if (k < 0 || !p) continue;
  bizAll++;
  const x = gx(p.lon), z = gz(p.lat);
  const ix = Math.floor((x - fx0) / fcell), iz = Math.floor((z - fz0) / fcell);
  if (ix >= 0 && iz >= 0 && ix < fnx && iz < fnz) land[iz * fnx + ix] |= 4;
  if (nearArterial(x, z, 70) > 70) continue;
  pois.push([x, z, k]); kindCount[k]++;
}
pois.sort((a, b) => a[0] - b[0] || a[1] - b[1]);

// ---- the 60 m grid: bit 1 zoned (land within 100 m or a business within
// 150 m of the cell's middle), bit 2 frontage (commercial/retail at the cell)
const grid = new Uint8Array(lat.nx * lat.nz);
const R_LAND = Math.round(100 / fcell), R_BIZ = Math.round(150 / fcell);
// separable box dilation of each bit
function dilate(mask, r) {
  const tmp = new Uint8Array(fnx * fnz), out = new Uint8Array(fnx * fnz);
  for (let z = 0; z < fnz; z++) { let run = -1e9; for (let x = 0; x < fnx; x++) { if (mask[z * fnx + x]) run = x; if (x - run <= r) tmp[z * fnx + x] = 1; }
    run = 1e9; for (let x = fnx - 1; x >= 0; x--) { if (mask[z * fnx + x]) run = x; if (run - x <= r) tmp[z * fnx + x] = 1; } }
  for (let x = 0; x < fnx; x++) { let run = -1e9; for (let z = 0; z < fnz; z++) { if (tmp[z * fnx + x]) run = z; if (z - run <= r) out[z * fnx + x] = 1; }
    run = 1e9; for (let z = fnz - 1; z >= 0; z--) { if (tmp[z * fnx + x]) run = z; if (run - z <= r) out[z * fnx + x] = 1; } }
  return out;
}
const dl = dilate(land.map(v => v & 3 ? 1 : 0), R_LAND), db = dilate(land.map(v => v & 4 ? 1 : 0), R_BIZ);
for (let iz = 0; iz < lat.nz; iz++)
  for (let ix = 0; ix < lat.nx; ix++) {
    // the fine cell at this lattice node (a coarse cell is centred on its node)
    const fi = (iz * F + 1) * fnx + ix * F + 1;
    let g = (dl[fi] || db[fi]) ? 1 : 0;
    for (let sz = 0; sz < F && !(g & 2); sz++) for (let sx = 0; sx < F; sx++) if (land[(iz * F + sz) * fnx + ix * F + sx] & 1) { g |= 2; break; }
    grid[iz * lat.nx + ix] = g;
  }
const cellOf = (x, z) => { const ix = Math.round((x - lat.x0) / lat.cell), iz = Math.round((z - lat.z0) / lat.cell);
  return ix < 0 || iz < 0 || ix >= lat.nx || iz >= lat.nz ? 0 : grid[iz * lat.nx + ix]; };

// ---- the interchanges. .0203(2)(b)(ii) keeps a freeway's billboards 500 ft
// from an interchange only OUTSIDE a town's corporate limits, and the belt is
// nearly all Charlotte and its incorporated towns, so that rule is not
// applied; what is kept is .0203(2)(a), not obscuring an official sign: no
// board within INTERCHANGE_M of a node where a ramp meets a motorway (the gore
// and its exit gantry). CitySigns.InterchangeNear reads the same nodes.
const nodeLink = new Uint8Array(city.nodes.length), nodeMw = new Uint8Array(city.nodes.length);
for (const e of city.edges) for (const n of [e.a, e.b]) { if (e.link) nodeLink[n] = 1; else if (e.rank >= 5) nodeMw[n] = 1; }
const ixNodes = [];
for (let n = 0; n < city.nodes.length; n++) if (nodeLink[n] && nodeMw[n]) ixNodes.push([city.nodes[n].x, city.nodes[n].z]);
const IX = 200, ixHash = new Map();
for (const p of ixNodes) { const k = Math.floor(p[0] / IX) + ',' + Math.floor(p[1] / IX); if (!ixHash.has(k)) ixHash.set(k, []); ixHash.get(k).push(p); }
const INTERCHANGE_M = 80;
function nearInterchange(x, z) {
  for (let cz = Math.floor((z - INTERCHANGE_M) / IX); cz <= Math.floor((z + INTERCHANGE_M) / IX); cz++)
    for (let cx = Math.floor((x - INTERCHANGE_M) / IX); cx <= Math.floor((x + INTERCHANGE_M) / IX); cx++)
      for (const p of ixHash.get(cx + ',' + cz) || []) if (Math.hypot(p[0] - x, p[1] - z) < INTERCHANGE_M) return true;
  return false;
}

// ---- route km, and zoned carriageway km: stations every 25 m (as
// CitySigns), zoned when the grid says so where the board would stand,
// ZONE_OFF_M past the centreline on the outer side of a one-way carriageway,
// on either side of a two-way road
const STEP = 25, ZONE_OFF_M = 25;
for (const e of city.edges) {
  const ri = wayRoute.get(e.wayId);
  if (ri === undefined || e.link || e.tunnel) continue;
  const r = routes[ri];
  r.km += e.length / 1000 * (e.oneway ? 0.5 : 1);
  for (let k = 0; (k + 0.5) * STEP < e.length; k++) {
    const s = (k + 0.5) * STEP;
    let i = 1; while (i < e.pts.length - 1 && e.s[i] < s) i++;
    const L = Math.max(1e-6, e.s[i] - e.s[i - 1]), t = (s - e.s[i - 1]) / L;
    const dx = (e.pts[i][0] - e.pts[i - 1][0]) / L, dz = (e.pts[i][1] - e.pts[i - 1][1]) / L;
    const x = e.pts[i - 1][0] + dx * L * t, z = e.pts[i - 1][1] + dz * L * t;
    if (r.cls === 1 && nearInterchange(x, z)) continue;
    const off = e.hw + ZONE_OFF_M;
    // right of travel is (dz, -dx): x is east and z north, so facing +z the right hand is +x
    const zr = cellOf(x + dz * off, z - dx * off) & 1, zl = cellOf(x - dz * off, z + dx * off) & 1;
    if (e.oneway ? zr : (zr || zl)) r.zonedKm += STEP / 1000;
  }
}

// ---- the OSM billboards (advertising=billboard; nodes and the faces drawn as ways)
const bills = [];
for (const el of ADV.elements) {
  const t = el.tags || {};
  if (t.advertising !== 'billboard') continue;
  let x, z, yaw = NaN;
  if (el.type === 'node') { x = gx(el.lon); z = gz(el.lat); }
  else if (el.geometry && el.geometry.length >= 2) {
    const a = el.geometry[0], b = el.geometry[el.geometry.length - 1];
    const ax = gx(a.lon), az = gz(a.lat), bx = gx(b.lon), bz = gz(b.lat);
    x = (ax + bx) / 2; z = (az + bz) / 2;
    yaw = Math.atan2(bz - az, bx - ax) * 180 / Math.PI;
  } else continue;
  bills.push([x, z, yaw, t.lit === 'yes' ? 1 : 0]);
  // which route it counts toward: the nearest route edge within 250 m
  let best = 250, bestR = -1;
  for (const e of city.edges) {
    const ri = wayRoute.get(e.wayId);
    if (ri === undefined) continue;
    for (let k = 0; k + 1 < e.pts.length; k++) {
      const [ax, az] = e.pts[k], [bx, bz] = e.pts[k + 1];
      if (Math.min(ax, bx) > x + best || Math.max(ax, bx) < x - best || Math.min(az, bz) > z + best || Math.max(az, bz) < z - best) continue;
      const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
      const tt = L2 > 1e-9 ? Math.max(0, Math.min(1, ((x - ax) * dx + (z - az) * dz) / L2)) : 0;
      const d = Math.hypot(x - ax - dx * tt, z - az - dz * tt);
      if (d < best) { best = d; bestR = ri; }
    }
  }
  if (bestR >= 0) routes[bestR].osm++;
}
bills.sort((a, b) => a[0] - b[0] || a[1] - b[1]);

// ---- rates: what each route asks for, less its OSM boards, over its zoned
// km; then raised by what the spacing rule will take back. CitySigns draws a
// board at each 25 m station with a probability (a Poisson process along the
// road, rate r per km) and a board loses to any better-ranked one on the same
// side within the rule's distance s, so the boards that stand are
// r (1 - e^-L) / L per km with L = 2 s r (one side: a freeway carriageway;
// a two-way road's boards split between its two sides). Solved for r.
const SPACING = { 1: 0.1524, 2: 0.0305 };   // km: 500 ft on a freeway, 100 ft in a town
for (const r of routes) {
  const want = r.density * r.km;
  r.stand = r.zonedKm > 0.05 ? Math.max(0, want - r.osm) / r.zonedKm : 0;
  const sides = r.cls === 1 ? 1 : 2, s = SPACING[r.cls];
  const x = 2 * s * r.stand / sides;            // 1 - e^-L
  r.rate = r.stand <= 0 ? 0 : x < 0.95 ? sides * -Math.log(1 - x) / (2 * s) : r.stand * 3;
}

// ---- exit gantries: destination:lanes on the motorway way ending at a diverge
const outOf = new Map(), into = new Map();
for (const e of city.edges) { (outOf.get(e.a) || outOf.set(e.a, []).get(e.a)).push(e); (into.get(e.b) || into.set(e.b, []).get(e.b)).push(e); }
const gant = new Map();
let diverges = 0, tagged = 0;
for (const [n, outs] of outOf) {
  if (!outs.some(e => e.link && e.oneway) || !outs.some(e => e.rank >= 5 && !e.link && e.oneway)) continue;
  const ins = (into.get(n) || []).filter(e => e.rank >= 5 && !e.link && e.oneway);
  if (!ins.length) continue;
  diverges++;
  const t = tags.get(ins[0].wayId) || {};
  const dl2 = t['destination:lanes'];
  if (!dl2) continue;
  const lanes = dl2.split('|').map(s => s.trim());
  const groups = [];
  for (let i = 0; i < lanes.length; i++) { if (i > 0 && lanes[i] === lanes[i - 1]) groups[groups.length - 1]++; else groups.push(1); }
  if (groups.length > 4) continue;
  gant.set(ins[0].wayId, groups); tagged++;
}
const gantList = [...gant.entries()].sort((a, b) => a[0] - b[0]);

// ---- encode
const parts = [];
const head = Buffer.alloc(28);
head.write('PSGN', 0, 'latin1'); head.writeInt32LE(1, 4);
head.writeInt32LE(lat.nx, 8); head.writeInt32LE(lat.nz, 12);
head.writeFloatLE(lat.x0, 16); head.writeFloatLE(lat.z0, 20); head.writeFloatLE(lat.cell, 24);
parts.push(head, Buffer.from(grid));
{ const b = Buffer.alloc(2); b.writeUInt16LE(routes.length); parts.push(b); }
for (const r of routes) {
  const nm = Buffer.from(r.name, 'latin1');
  const b = Buffer.alloc(1 + nm.length + 1 + 8);
  b.writeUInt8(nm.length, 0); nm.copy(b, 1); b.writeUInt8(r.cls, 1 + nm.length);
  b.writeFloatLE(r.density, 2 + nm.length); b.writeFloatLE(r.rate, 6 + nm.length);
  parts.push(b);
}
const ways = [...wayRoute.entries()].sort((a, b) => a[0] - b[0]);
{ const b = Buffer.alloc(4 + ways.length * 5); b.writeUInt32LE(ways.length, 0); ways.forEach(([w, r], i) => { b.writeUInt32LE(w, 4 + i * 5); b.writeUInt8(r, 8 + i * 5); }); parts.push(b); }
{ const b = Buffer.alloc(4 + bills.length * 13); b.writeUInt32LE(bills.length, 0);
  bills.forEach(([x, z, y, f], i) => { const o = 4 + i * 13; b.writeFloatLE(x, o); b.writeFloatLE(z, o + 4); b.writeFloatLE(y, o + 8); b.writeUInt8(f, o + 12); }); parts.push(b); }
{ const b = Buffer.alloc(4 + pois.length * 9); b.writeUInt32LE(pois.length, 0);
  pois.forEach(([x, z, k], i) => { const o = 4 + i * 9; b.writeFloatLE(x, o); b.writeFloatLE(z, o + 4); b.writeUInt8(k, o + 8); }); parts.push(b); }
{ let n = 4; for (const [, g] of gantList) n += 5 + g.length;
  const b = Buffer.alloc(n); b.writeUInt32LE(gantList.length, 0); let o = 4;
  for (const [w, g] of gantList) { b.writeUInt32LE(w, o); b.writeUInt8(g.length, o + 4); g.forEach((v, i) => b.writeUInt8(v, o + 5 + i)); o += 5 + g.length; }
  parts.push(b); }
const bytes = Buffer.concat(parts);
const br = brotliCompressSync(bytes, { params: { [Z.BROTLI_PARAM_QUALITY]: 11, [Z.BROTLI_PARAM_LGWIN]: 24, [Z.BROTLI_PARAM_SIZE_HINT]: bytes.length } }).length;

// ---- the numbers
let zoned = 0, front = 0; for (const g of grid) { if (g & 1) zoned++; if (g & 2) front++; }
console.log(`grid ${lat.nx} x ${lat.nz} at ${lat.cell} m: ${landPolys} landuse polygons; zoned for billboards ${(100 * zoned / grid.length).toFixed(1)}% of the box, commercial frontage ${(100 * front / grid.length).toFixed(1)}%`);
console.log(`routes (boards per km of route; OSM boards on it; zoned carriageway km; the rate placed per zoned carriageway km):`);
for (const r of [...routes].sort((a, b) => b.km - a.km)) if (r.km >= 3)
  console.log(`  ${r.name.padEnd(8)} ${r.cls === 1 ? 'interstate' : 'US/NC     '} ${r.km.toFixed(1).padStart(6)} km  ${r.density.toFixed(2)}/km${r.measured ? ' (NCDOT)' : ' (class) '}  want ${(r.density * r.km).toFixed(0).padStart(3)}  osm ${String(r.osm).padStart(2)}  zoned ${r.zonedKm.toFixed(1).padStart(6)} km  to stand ${r.stand.toFixed(3)}  drawn at ${r.rate.toFixed(3)}`);
console.log(`OSM billboards ${bills.length}; businesses ${bizAll}, ${pois.length} within 70 m of a collector or bigger: ${KINDS.map((k, i) => k + ' ' + kindCount[i]).join(', ')}`);
console.log(`motorway diverges ${diverges}, ${tagged} with destination:lanes (gantry panels)`);
console.log(`size ${(bytes.length / 1e3).toFixed(0)} KB raw, ${(br / 1e3).toFixed(0)} KB Brotli; built in ${((Date.now() - t0) / 1e3).toFixed(1)} s`);

const shipped = join(RES, 'charlotte_signs.bytes');
if (check) {
  const same = existsSync(shipped) && Buffer.compare(readFileSync(shipped), bytes) === 0;
  console.log(same ? 'SIGNS CHECK OK (byte for byte)' : 'SIGNS CHECK: DIFFERS from ' + shipped);
  process.exitCode = same ? 0 : 1;
}
if (outDir) { mkdirSync(outDir, { recursive: true }); writeFileSync(join(outDir, 'charlotte_signs.bytes'), bytes); console.log('wrote ' + join(outDir, 'charlotte_signs.bytes')); }
