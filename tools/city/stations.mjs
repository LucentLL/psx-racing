// stations.mjs - Charlotte's filling stations (city fuel r1, 2026-10-30): where
// the owner's gas-station model stands in the city, one for every real station.
//
//   node tools/city/stations.mjs --check          rebuild in memory, compare with Resources/charlotte_fuel.bytes
//   node tools/city/stations.mjs --out <dir>      write <dir>/charlotte_fuel.bytes
//   node tools/city/stations.mjs --json <file>    also write the whole table (the sheet's input: stations_sheet.py)
//   node tools/city/stations.mjs --list           print every station, one line each
//
// The owner, 2026-10-30: "gas station models we have to be used where real gas
// station locations are." Its OWN FILE, like charlotte_signs.bytes: the city
// export (export_osm.mjs) never writes it, so a re-export of the roads does not
// fight it in a binary merge; it is re-made after one (it reads the shipped
// graph and footprints, and --check says when it must be).
//
// INPUTS (nothing is fetched):
//   cache/layers/business.json     every OSM amenity=fuel node and way in the beltway box
//                                  (out center; pinned to the road snapshot)
//   cache/buildings_core.json      the outlines of the fuel ways that are buildings (a
//   cache/buildings_city.json      canopy, a kiosk), and the fuel MULTIPOLYGONS business.json lacks
//   Resources/charlotte_city.bytes the roads (frontage, corridors, lakes, creeks, parking lots)
//   Resources/charlotte_bld.bytes  the footprints the city DRAWS (what a station must not clash with,
//                                  and the station's own kiosk it stands in place of)
//   Resources/charlotte_dem.bytes  the ground (how much a lot falls: the pad the game grades)
//   tools/city/stations_overrides.json  the hand decisions, by OSM id, each with its reason
// No OSM name, brand or operator tag is read: every rule is geometry and the
// fuel tags (hgv, fuel:HGV_diesel), and the overrides are keyed by id.
//
// THE STATIONS. The features are clustered (one station: features within
// MERGE_M of each other with no drawn road between their centres; a truck
// stop's car and truck sides within TRUCK_MERGE_M when either is tagged for
// trucks) and classified:
//   forecourt   a filling station with its shop (the owner's model, whole)
//   truck       a truck stop (hgv=yes or fuel:HGV_diesel=yes on any part)
//   bigbox      a fuel point in a big store's parking field (BIGBOX_SETBACK_M
//               or more from every street, a store of BIGBOX_M2 or more within
//               BIGBOX_R): the model's canopy alone, where OSM drew its canopy
//   skipped     closed (the overrides), or no drawn surface road within
//               FRONT_MAX_M (its street is not in the city data). A marina's, an
//               airport's or a private yard's fuel would be skipped by the
//               overrides; there is none in the data (aviation fuel is OSM's
//               aeroway=fuel, not here; no marina fuel; no access=private)
//
// THE PLACEMENT (US practice, the owner's rules): the station FACES its
// frontage road - the nearest road it could have a driveway on, a bigger road
// preferred at a corner; the lot's front line stands at the back of that
// road's walk (or verge), the pumps under the canopy run PARALLEL to the
// frontage, the canopy's front edge and the price pylon stand out of the
// road's clear zone; the canopy, the shop, the restrooms stand clear of every
// footprint the city draws (but the station's own kiosk, which the model
// replaces), of every other road's pavement, walk and clear zone, of the
// water and of the other stations; driveways meet the curb (curb-cut aprons,
// as the parking lots' entrances), never within DRIVE_CORNER_M of a junction.
// A placement needs at least one driveway. The model is never scaled: every
// layout (shop behind / shop beside, each mirrored by placing its parts; 1-3
// canopy modules; the canopy alone; a truck stop's) slid along the road and set
// back from it competes on ONE cost - how far its canopy and shop stand from
// OSM's own, plus a little for each metre back, a second-choice frontage and
// each step down the list of layouts - so a small or odd lot takes the layout
// that fits it nearest OSM's own. One that must stand over DISPLACED_M away is
// a misfit (flag 32, listed by --list and the sheet); one that fits nowhere is
// stored NOT PLACED (flag 4).
//
// PFUL v1, little-endian (the runtime: Scripts/City/CityStations.cs, PHASE B):
//   u32 'PFUL' | i32 1 | u32 graphHash (charlotte_city.bytes' GHSH) | u32 bldCrc (CRC-32 of charlotte_bld.bytes)
//   u16 nStations, each:
//     f32 x, z          the lot's front-centre (on its front line: the back of the walk), world metres
//     f32 yawDeg        Unity yaw of the lot's +v (away from the road): +v = (sin yaw, cos yaw)
//     u8 cls            0 forecourt, 1 truck, 2 bigbox (3+ never written: skipped stations are not stored)
//     u8 layout         0 shop behind the canopy, 1 shop beside it, 2 canopy alone, 3 truck (shop beside + truck canopy)
//     u8 modules        canopy modules side by side (each 13.06 m along the road x 16.98 m deep)
//     u8 flags          1 mirrored (shop/restrooms on the other hand), 2 corner lot, 4 NOT PLACED (no fit:
//                       the runtime stands nothing; the pole sign stays), 8 steep (the pad grades over
//                       STEEP_M or more), 16 OSM drew no outline (a node or a site centre only),
//                       32 a misfit: it stands over DISPLACED_M from OSM's own (its lot would not fit there)
//     f32 lotW, lotD    the paved lot, metres along the road (centred on x, z) and back from the front line
//     u32 frontEdge | f32 frontS | i8 side    the frontage: edge index, arc position of x, z, side (+1 left of a->b)
//     u8 nDrive, each f32 mx, mz | f32 dx, dz | f32 half | f32 len | u32 edge | f32 s | i8 side
//                       a driveway: its mouth on the road's pavement edge (world), the unit way in, its half
//                       width (the curb cut's), how far it runs to the lot (across the walk or verge, and on
//                       where the lot stands back); then the road (edge index, arc position, side: +1 left of
//                       a->b, the LENT layout) - a hint, valid while the graph hash matches
//     u8 nLink, each f32 ax, az, bx, bz | f32 half    an in-lot station's aisle: a straight paved strip from
//                       its pad (a) to the nearest paving the city draws (b: a parking lot, a road's edge)
//     u8 nHide, each f32 x, z     a footprint the model stands in place of (the station's own kiosk or canopy
//                                 OSM drew), by its polygon's area centroid (shoelace, from the shipped
//                                 float32 points): the runtime draws no box there (matched within 0.5 m)
//     u8 osmType (0 node, 1 way, 2 relation) | u64 osmId    the station's main OSM element (audits only)
// All OSM-derived data (c) OpenStreetMap contributors, ODbL 1.0.

import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { crc32 } from 'node:zlib';
import { parseCity, parseBld, parseDem } from './lib/citydata.mjs';
import { LAT0, LON0, M_LAT, M_LON } from './lib/canopygrid.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const RES = join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources');
// a worktree points at main's cache with PSX_CITY_CACHE, as export_osm.mjs and signs.mjs do
const CACHE = process.env.PSX_CITY_CACHE ? resolve(process.env.PSX_CITY_CACHE) : join(HERE, 'cache');
const args = process.argv.slice(2);
const argOf = k => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : null; };
const outDir = argOf('--out'), jsonOut = argOf('--json');
const check = args.includes('--check'), list = args.includes('--list');
const t0 = Date.now();

const gx = lon => (lon - LON0) * M_LON, gz = lat => (lat - LAT0) * M_LAT;
const need = f => { if (!existsSync(f)) { console.error('missing ' + f); process.exit(2); } return JSON.parse(readFileSync(f, 'utf8')); };

// ---- the rules' numbers
const MERGE_M = 35;           // features this close (gap), no road between: one station
const TRUCK_MERGE_M = 100;    // a truck stop's car and truck sides
const FRONT_MAX_M = 90;       // the frontage road is within this of the station (gap to its pavement)
const BIGBOX_M2 = 4500, BIGBOX_R = 260;   // a big store this size within this of a kiosk-less fuel point
const BIGBOX_SETBACK_M = 75;  // ...standing in its parking field this far from every street
const TRUCK_FRONT_MAX_M = 200;   // a truck stop's lot is big: its frontage road may be this far
const KIOSK_M2 = 650, KIOSK_R = 25;       // an untagged building this small this near the fuel: the station's own shop
const PART_CLEAR_M = 1.0;     // a part keeps this clear of another building
const LOT_BLD_MAX = 0.12;     // at most this share of the lot may lie on other buildings (the runtime cuts them out)
const DRIVE_HALF = 4.6;       // a two-way commercial driveway, 30 ft (NCDOT: 24-36 ft)
const LINK_HALF = 3.66, LINK_MAX_M = 170;  // an in-lot station's aisle to the paving near it (24 ft two-way aisle)
const DRIVE_CORNER_M = 15;    // a driveway's edge this far from a junction (corner clearance)
const DRIVE_CORNER_MIN_M = 7.5;   // ...or, where the frontage is a short block, this far (flagged tight)
const STEEP_M = 3.0;          // a lot whose ground falls this much is flagged (the pad grades it)
const LAYOUT_STEP = 3, CANOPY_ALONE_COST = 12;   // the cost of a layout further down the list, of the canopy alone
const TRUCK_LAYOUT_COST = 20;  // a truck stop without its truck canopy
const DISPLACED_M = 25;       // a station standing this far from OSM's own (canopy or shop) is listed as a misfit
const DISPLACED_TRUCK_M = 45; // (a truck stop's lot is big: its car fuel may be this far from the lot's middle)
const isMisfit = st => st.placed && st.displaced > (st.cls === 'truck' ? DISPLACED_TRUCK_M : DISPLACED_M);
/// How far behind the walk a lot may stand (its driveways then run on to it): OSM puts some
/// stations well back from the only street the city has near them.
const BACKS = [0, 2, 4, 6, 9, 12, 16, 22, 30, 40, 50];
const WALK_W = 2.4;           // CityMeshes.WalkW
const S = 0.7823;             // the pack's scale: its pumps to 2.2 m (PSXRacingBuilder.PumpHeightM)

/// RoadsideOccupancy.ClearZoneOf (rank = the shipped class: 0 local .. 5 motorway)
const clearZoneOf = e => e.link ? 4.5 : e.rank >= 5 ? 9 : e.rank === 4 ? 6 : e.rank >= 2 ? 3.5 : e.rank === 1 ? 2.5 : 2;

// ------------------------------------------------------------------ inputs
const BIZ = need(join(CACHE, 'layers', 'business.json'));
const [la0, lo0, la1, lo1] = BIZ.bbox.split(',').map(Number);
const inBox = (lat, lon) => lat >= la0 && lat <= la1 && lon >= lo0 && lon <= lo1;
const geo = new Map();
for (const f of ['buildings_core.json', 'buildings_city.json']) {
  for (const e of need(join(CACHE, f)).elements) {
    if (!((e.type === 'way' && e.geometry) || (e.type === 'relation' && e.members))) continue;
    const k = e.type + e.id;
    if (!geo.has(k)) geo.set(k, e);
  }
}
const OV = need(join(HERE, 'stations_overrides.json'));
const cityBytes = readFileSync(join(RES, 'charlotte_city.bytes'));
const bldBytes = readFileSync(join(RES, 'charlotte_bld.bytes'));
const city = parseCity(cityBytes);
const bld = parseBld(bldBytes);
const dem = parseDem(readFileSync(join(RES, 'charlotte_dem.bytes')));

// ------------------------------------------------------------------ geometry
const polyArea = r => { let a = 0; for (let i = 0, n = r.length; i < n; i++) { const p = r[i], q = r[(i + 1) % n]; a += p[0] * q[1] - q[0] * p[1]; } return a / 2; };
const centroid = r => {
  let a = 0, cx = 0, cz = 0;
  for (let i = 0, n = r.length; i < n; i++) { const p = r[i], q = r[(i + 1) % n], w = p[0] * q[1] - q[0] * p[1]; a += w; cx += (p[0] + q[0]) * w; cz += (p[1] + q[1]) * w; }
  if (Math.abs(a) < 1e-9) return [r.reduce((s, p) => s + p[0], 0) / r.length, r.reduce((s, p) => s + p[1], 0) / r.length];
  return [cx / (3 * a), cz / (3 * a)];
};
const inRing = (r, x, z) => { let c = false; for (let i = 0, j = r.length - 1; i < r.length; j = i++) if ((r[i][1] > z) !== (r[j][1] > z) && x < (r[j][0] - r[i][0]) * (z - r[i][1]) / (r[j][1] - r[i][1]) + r[i][0]) c = !c; return c; };
const segDist = (px, pz, ax, az, bx, bz) => { const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz; const t = L2 > 1e-12 ? Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / L2)) : 0; return Math.hypot(px - ax - dx * t, pz - az - dz * t); };
const crosses = (ax, az, bx, bz, cx, cz, dx, dz) => {
  const d1 = (bx - ax) * (cz - az) - (bz - az) * (cx - ax), d2 = (bx - ax) * (dz - az) - (bz - az) * (dx - ax);
  const d3 = (dx - cx) * (az - cz) - (dz - cz) * (ax - cx), d4 = (dx - cx) * (bz - cz) - (dz - cz) * (bx - cx);
  return (d1 > 0) !== (d2 > 0) && (d3 > 0) !== (d4 > 0);
};
const segSeg = (a, b, c, d) => crosses(a[0], a[1], b[0], b[1], c[0], c[1], d[0], d[1]) ? 0
  : Math.min(segDist(a[0], a[1], c[0], c[1], d[0], d[1]), segDist(b[0], b[1], c[0], c[1], d[0], d[1]),
             segDist(c[0], c[1], a[0], a[1], b[0], b[1]), segDist(d[0], d[1], a[0], a[1], b[0], b[1]));
/// Gap between two rings (0 when they touch or overlap).
function ringGap(A, B) {
  if (inRing(B, A[0][0], A[0][1]) || inRing(A, B[0][0], B[0][1])) return 0;
  let best = Infinity;
  for (let i = 0; i < A.length; i++) for (let j = 0; j < B.length; j++) {
    best = Math.min(best, segSeg(A[i], A[(i + 1) % A.length], B[j], B[(j + 1) % B.length]));
    if (best === 0) return 0;
  }
  return best;
}
const ringPointGap = (r, x, z) => { if (inRing(r, x, z)) return 0; let b = Infinity; for (let i = 0; i < r.length; i++) b = Math.min(b, segDist(x, z, r[i][0], r[i][1], r[(i + 1) % r.length][0], r[(i + 1) % r.length][1])); return b; };

/// A spatial hash of items by the cells their boxes touch.
class Grid {
  constructor(cell) { this.c = cell; this.m = new Map(); }
  add(x0, z0, x1, z1, v) {
    for (let cx = Math.floor(x0 / this.c); cx <= Math.floor(x1 / this.c); cx++)
      for (let cz = Math.floor(z0 / this.c); cz <= Math.floor(z1 / this.c); cz++) {
        const k = cx * 100003 + cz; let l = this.m.get(k); if (!l) this.m.set(k, l = []); l.push(v);
      }
  }
  near(x0, z0, x1, z1) {
    const out = new Set();
    for (let cx = Math.floor(x0 / this.c); cx <= Math.floor(x1 / this.c); cx++)
      for (let cz = Math.floor(z0 / this.c); cz <= Math.floor(z1 / this.c); cz++)
        for (const v of this.m.get(cx * 100003 + cz) || []) out.add(v);
    return [...out].sort((a, b) => a - b);
  }
}

// ------------------------------------------------------------------ roads
// half the drawn ribbon, with the line model's one-sided offset (CityMap.HalfMax, roughly)
const halfOf = e => e.hw + (city.taprOff ? Math.abs(city.taprOff[e.index]) : 0);
const segs = [];   // [ax, az, bx, bz, edgeIndex, k]
const segGrid = new Grid(64);
for (const e of city.edges) {
  for (let k = 0; k + 1 < e.pts.length; k++) {
    const [ax, az] = e.pts[k], [bx, bz] = e.pts[k + 1];
    const i = segs.length;
    segs.push([ax, az, bx, bz, e.index, k]);
    segGrid.add(Math.min(ax, bx), Math.min(az, bz), Math.max(ax, bx), Math.max(az, bz), i);
  }
}
const nodeDeg = new Uint16Array(city.nodes.length);
for (const e of city.edges) { nodeDeg[e.a]++; nodeDeg[e.b]++; }
// the game's WalkEdge, roughly: a surface street (local..primary, no ramp, tunnel or
// bridge), two lanes wide or more, near the Square or with a non-house building within 40 m
const UPTOWN = city.uptown;
function pointAt(e, s) {
  s = Math.max(0, Math.min(e.length, s));
  let i = 1; while (i < e.pts.length - 1 && e.s[i] < s) i++;
  const L = Math.max(1e-9, e.s[i] - e.s[i - 1]), t = (s - e.s[i - 1]) / L;
  const a = e.pts[i - 1], b = e.pts[i];
  return { x: a[0] + (b[0] - a[0]) * t, z: a[1] + (b[1] - a[1]) * t, tx: (b[0] - a[0]) / L, tz: (b[1] - a[1]) / L, k: i - 1 };
}
/// The nearest point of edge e to (x, z): arc position, distance, side (+1 left of a->b).
function project(e, x, z) {
  let best = Infinity, bs = 0, side = 1;
  for (let k = 0; k + 1 < e.pts.length; k++) {
    const [ax, az] = e.pts[k], [bx, bz] = e.pts[k + 1];
    const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
    const t = L2 > 1e-12 ? Math.max(0, Math.min(1, ((x - ax) * dx + (z - az) * dz) / L2)) : 0;
    const d = Math.hypot(x - ax - dx * t, z - az - dz * t);
    if (d < best) { best = d; bs = e.s[k] + Math.sqrt(L2) * t; side = dx * (z - az) - dz * (x - ax) > 0 ? 1 : -1; }
  }
  return { s: bs, d: best, side };
}

// ------------------------------------------------------------------ footprints
const fps = bld.footprints.map((f, i) => {
  let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity;
  for (const [x, z] of f.pts) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); z0 = Math.min(z0, z); z1 = Math.max(z1, z); }
  const area = Math.abs(polyArea(f.pts));
  return { i, pts: f.pts, x0, x1, z0, z1, area, c: centroid(f.pts), house: f.style === 3 || f.gable, part: !!f.part, hidden: !!f.hidden };
});
const fpGrid = new Grid(64);
for (const f of fps) if (!f.part) fpGrid.add(f.x0, f.z0, f.x1, f.z1, f.i);   // parts stand inside their outline
const bigStores = fps.filter(f => !f.house && !f.part && f.area >= BIGBOX_M2);
const bigGrid = new Grid(256);
for (const f of bigStores) bigGrid.add(f.x0, f.z0, f.x1, f.z1, f.i);

// ------------------------------------------------------------------ parking lots (LOTS) and water
function parseLots() {
  const sec = city.sections.LOTS;
  if (!sec) return [];
  const b = cityBytes; let p = sec[0];
  const u16 = () => { const v = b.readUInt16LE(p); p += 2; return v; }, i16 = () => { const v = b.readInt16LE(p); p += 2; return v; };
  const u8 = () => b.readUInt8(p++), f32 = () => { const v = b.readFloatLE(p); p += 4; return v; }, u32 = () => { const v = b.readUInt32LE(p); p += 4; return v; };
  const head = u32(), n = head & 0x7fffffff, v2 = (head & 0x80000000) !== 0;
  const lots = [];
  for (let i = 0; i < n; i++) {
    const ox = f32(), oz = f32(), m = u16(), ring = [];
    for (let k = 0; k < m; k++) ring.push([ox + i16() * 0.05, oz + i16() * 0.05]);
    i16(); i16();
    const runs = u16();
    for (let k = 0; k < runs; k++) { i16(); i16(); u8(); if (v2) { i16(); i16(); } }
    if (v2) { u8(); const nh = u16(); for (let h = 0; h < nh; h++) { u8(); const hm = u16(); for (let k = 0; k < hm; k++) { i16(); i16(); } } }
    let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity;
    for (const [x, z] of ring) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); z0 = Math.min(z0, z); z1 = Math.max(z1, z); }
    lots.push({ ring, x0, x1, z0, z1, area: Math.abs(polyArea(ring)) });
  }
  if (p !== sec[1]) throw new Error(`LOTS read ${p - sec[0]} of ${sec[1] - sec[0]} bytes`);
  return lots;
}
const lots = parseLots();
const lotGrid = new Grid(128);
lots.forEach((L, i) => lotGrid.add(L.x0, L.z0, L.x1, L.z1, i));
const waterSegs = [], waterGrid = new Grid(64), lakes = [];
for (const w of city.waters) {
  if (w.lake) { lakes.push(w.pts); }
  for (let k = 0; k + 1 < w.pts.length; k++) {
    const [ax, az] = w.pts[k], [bx, bz] = w.pts[k + 1];
    waterGrid.add(Math.min(ax, bx), Math.min(az, bz), Math.max(ax, bx), Math.max(az, bz), waterSegs.length);
    waterSegs.push([ax, az, bx, bz, w.lake ? 0 : w.width * 0.5, w.ravine]);
  }
}

// ------------------------------------------------------------------ the fuel features
const ringsOf = e => e.type === 'way' ? [e.geometry.map(p => [gx(p.lon), gz(p.lat)])]
  : e.members.filter(m => (m.role === 'outer' || m.role === '') && m.geometry).map(m => m.geometry.map(p => [gx(p.lon), gz(p.lat)]));
const clean = r => { const o = r.slice(); if (o.length > 1 && Math.hypot(o[0][0] - o[o.length - 1][0], o[0][1] - o[o.length - 1][1]) < 0.05) o.pop(); return o; };
const feats = [];
const typeCode = { node: 0, way: 1, relation: 2 };
const seenF = new Set();
function addFeat(e, rings, x, z) {
  const id = e.type[0] + e.id;
  if (seenF.has(id)) return;
  seenF.add(id);
  const t = e.tags || {};
  const truck = t.hgv === 'yes' || t['fuel:HGV_diesel'] === 'yes';
  const isBuilding = !!t.building;
  const roof = t.building === 'roof';
  feats.push({ id, type: typeCode[e.type], osm: BigInt(e.id), x, z, rings: rings ? rings.map(clean).filter(r => r.length >= 3) : null,
               truck, building: isBuilding, roof, area: rings ? rings.reduce((s, r) => s + Math.abs(polyArea(clean(r))), 0) : 0 });
}
for (const e of BIZ.elements) {
  if (!e.tags || e.tags.amenity !== 'fuel') continue;
  const g = geo.get(e.type + e.id);
  const p = e.type === 'node' ? e : e.center;
  if (!p || !inBox(p.lat, p.lon)) continue;
  if (g) { const R = ringsOf(g), c = centroid(clean(R[0])); addFeat(e, R, c[0], c[1]); }
  else addFeat(e, null, gx(p.lon), gz(p.lat));
}
// the fuel multipolygons business.json's out center left out (it lists nodes and ways)
for (const e of [...geo.values()].sort((a, b) => (a.type + a.id < b.type + b.id ? -1 : 1))) {
  if (!e.tags || e.tags.amenity !== 'fuel' || e.type !== 'relation') continue;
  const R = ringsOf(e); if (!R.length) continue;
  const c = centroid(clean(R[0]));
  if (!inBox(LAT0 + c[1] / M_LAT, LON0 + c[0] / M_LON)) continue;
  addFeat(e, R, c[0], c[1]);
}
feats.sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
const fIndex = new Map(feats.map((f, i) => [f.id, i]));

// ---- clusters
const parent = feats.map((_, i) => i);
const find = i => { while (parent[i] !== i) i = parent[i] = parent[parent[i]]; return i; };
const unite = (a, b) => { a = find(a); b = find(b); if (a !== b) parent[Math.max(a, b)] = Math.min(a, b); };
const featGap = (a, b) => {
  if (!a.rings && !b.rings) return Math.hypot(a.x - b.x, a.z - b.z);
  if (!a.rings) return Math.min(...b.rings.map(r => ringPointGap(r, a.x, a.z)));
  if (!b.rings) return Math.min(...a.rings.map(r => ringPointGap(r, b.x, b.z)));
  let g = Infinity; for (const r of a.rings) for (const q of b.rings) g = Math.min(g, ringGap(r, q)); return g;
};
function roadBetween(ax, az, bx, bz) {
  for (const si of segGrid.near(Math.min(ax, bx), Math.min(az, bz), Math.max(ax, bx), Math.max(az, bz))) {
    const s = segs[si];
    const e = city.edges[s[4]];
    if (e.tunnel) continue;
    if (crosses(ax, az, bx, bz, s[0], s[1], s[2], s[3])) return e;
  }
  return null;
}
const apart = new Set((OV.apart || []).map(([a, b]) => [a, b].sort().join('|')));
const mergeWhy = [];
for (let i = 0; i < feats.length; i++) for (let j = i + 1; j < feats.length; j++) {
  const a = feats[i], b = feats[j];
  if (Math.hypot(a.x - b.x, a.z - b.z) > TRUCK_MERGE_M + 120) continue;
  if (apart.has([a.id, b.id].sort().join('|'))) continue;
  const gap = featGap(a, b);
  const lim = a.truck || b.truck ? TRUCK_MERGE_M : MERGE_M;
  if (gap > lim) continue;
  if (roadBetween(a.x, a.z, b.x, b.z)) continue;
  unite(i, j);
  mergeWhy.push([a.id, b.id, gap]);
}
for (const [a, b] of OV.merge || []) {
  if (!fIndex.has(a) || !fIndex.has(b)) { console.error('stations_overrides.json: merge names a feature not in the box: ' + a + ' ' + b); process.exit(2); }
  unite(fIndex.get(a), fIndex.get(b));
}
const clusters = new Map();
feats.forEach((f, i) => { const r = find(i); if (!clusters.has(r)) clusters.set(r, []); clusters.get(r).push(f); });

// ------------------------------------------------------------------ the station's parts (the model, measured)
// Measured off Art/GasStation/Gas_station.fbx in Blender, scaled x0.7823 (the pumps to 2.2 m), in the
// station's frame: u along the road, v back from the lot's front line. The pack's own lot is 28.0 x 41.4 m
// (AsphaltDamaged), its canopy 16.98 x 13.06 m with two pump islands 10.96 m apart, its shop 11.19 x 12.67 m,
// its restrooms 3.68 x 6.10 m. Its islands run AWAY from the road; the owner's rule wants them along it, so
// the canopy module stands turned a quarter (13.06 along the road, 16.98 deep), its islands at +-5.48 m.
// PHASE B's prefab baker measures the same numbers in Unity and asserts them (PART_CHECK in CityStations).
const CAN_W = 13.06, CAN_D = 16.98, CAN_V0 = 3.43;
const SHOP_W = 11.19, SHOP_D = 12.67, WC_W = 3.68, WC_D = 6.10;
/// The layout's parts as rectangles [u0, u1, v0, v1] about a canopy group of n modules centred on u = 0.
function layout(kind, n, mirror) {
  const cw = CAN_W * n, P = [];
  const can = [-cw / 2, cw / 2, CAN_V0, CAN_V0 + CAN_D];
  P.push(['canopy', ...can]);
  let lot;
  if (kind === 0) {            // B: the shop behind the canopy, facing the road (the pack's own arrangement)
    const su = -0.94;          // the pack's shop centre, off the canopy's
    P.push(['shop', su - SHOP_W / 2, su + SHOP_W / 2, 25.58, 25.58 + SHOP_D]);
    P.push(['wc', cw / 2 + 6.92 - WC_W / 2, cw / 2 + 6.92 + WC_W / 2, 25.82, 25.82 + WC_D]);
    P.push(['dumpster', cw / 2 + 7.75 - 0.8, cw / 2 + 7.75 + 0.8, 31.9, 32.9]);
    lot = [Math.min(-cw / 2, su - SHOP_W / 2) - 5.3, Math.max(cw / 2 + 7.5, cw / 2 + 6.92 + WC_W / 2 + 0.9), 0, 41.4];
  } else if (kind === 1 || kind === 3) {   // A: the shop beside the canopy, its door toward the pumps
    const s0 = cw / 2 + 7.12;
    P.push(['shop', s0, s0 + SHOP_D, 5.32, 5.32 + SHOP_W]);
    P.push(['wc', s0 + 0.23, s0 + 0.23 + WC_D, 23.47, 23.47 + WC_W]);
    P.push(['dumpster', s0 + 6.8 - 0.8, s0 + 6.8 + 0.8, 25.6, 26.6]);
    lot = [-cw / 2 - 5.4, s0 + SHOP_D + 3.2, 0, 28.0];
    if (kind === 3) {          // a truck stop: a second canopy group for the trucks, behind
      const tw = CAN_W * 2;
      const tu = (lot[0] + lot[1]) / 2;
      P.push(['truck', tu - tw / 2, tu + tw / 2, 34, 34 + CAN_D]);
      lot = [Math.min(lot[0], tu - tw / 2 - 7), Math.max(lot[1], tu + tw / 2 + 7), 0, 34 + CAN_D + 8];
    }
  } else {                     // the canopy alone (a fuel point in a big store's lot)
    lot = [-cw / 2 - 6, cw / 2 + 6, 0, CAN_V0 + CAN_D + 4];
  }
  // the pylon: at the lot's front corner on the canopy's hand
  P.push(['pylon', lot[0] + 1.0, lot[0] + 3.4, 1.2, 1.6]);
  if (mirror) {
    for (const p of P) { const a = -p[2], b = -p[1]; p[1] = a; p[2] = b; }
    const a = -lot[1], b = -lot[0]; lot = [a, b, lot[2], lot[3]];
  }
  // centre the lot on u = 0 (the station's x, z is the lot's front-centre)
  const mid = (lot[0] + lot[1]) / 2;
  for (const p of P) { p[1] -= mid; p[2] -= mid; }
  lot = [lot[0] - mid, lot[1] - mid, lot[2], lot[3]];
  return { parts: P, lot };
}
const LAYOUT_NAME = ['shop behind', 'shop beside', 'canopy alone', 'truck stop'];

// ------------------------------------------------------------------ station frame helpers
/// World corners of a local rectangle [u0, u1, v0, v1] for a frame (origin o, along u, back v).
const rectWorld = (F, r) => [[r[0], r[2]], [r[1], r[2]], [r[1], r[3]], [r[0], r[3]]].map(([u, v]) => [F.ox + F.ux * u + F.vx * v, F.oz + F.uz * u + F.vz * v]);
const boxOf = R => { let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity; for (const [x, z] of R) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); z0 = Math.min(z0, z); z1 = Math.max(z1, z); } return [x0, z0, x1, z1]; };
/// Share of rectangle R's area inside ring Q (1 m sampling).
function overlapArea(F, r, Q) {
  let n = 0, inside = 0;
  for (let u = r[0] + 0.5; u < r[1]; u += 1) for (let v = r[2] + 0.5; v < r[3]; v += 1) {
    n++;
    const x = F.ox + F.ux * u + F.vx * v, z = F.oz + F.uz * u + F.vz * v;
    if (inRing(Q, x, z)) inside++;
  }
  return n ? inside : 0;
}

// ------------------------------------------------------------------ the stations
const stations = [];
const skipped = [];
const placedLots = [];   // world rings of the lots placed so far (no two overlap)
const clusterList = [...clusters.values()].map(fs => fs.sort((a, b) => (a.id < b.id ? -1 : 1)));
clusterList.sort((A, B) => (A[0].id < B[0].id ? -1 : 1));

/// The measures the class is read from (kept on the record: the sheet and --list print them).
function measures(fs, core) {
  const M = {};
  // the nearest drawn surface road's pavement
  M.roadGap = Infinity;
  for (const si of segGrid.near(core.x - 300, core.z - 300, core.x + 300, core.z + 300)) {
    const s = segs[si], e = city.edges[s[4]];
    if (e.link || e.tunnel || e.bridge || e.rank >= 5) continue;
    M.roadGap = Math.min(M.roadGap, Math.max(0, segDist(core.x, core.z, s[0], s[1], s[2], s[3]) - halfOf(e)));
  }
  // on (inside, or within a stall's depth of) an OSM parking lot
  M.inLot = false;
  for (const li of lotGrid.near(core.x - 10, core.z - 10, core.x + 10, core.z + 10)) {
    const L = lots[li];
    if (inRing(L.ring, core.x, core.z) || ringPointGap(L.ring, core.x, core.z) <= 6) { M.inLot = true; M.lotM2 = Math.round(L.area); break; }
  }
  // the biggest store near
  M.store = 0; M.storeGap = Infinity;
  for (const fi of bigGrid.near(core.x - BIGBOX_R, core.z - BIGBOX_R, core.x + BIGBOX_R, core.z + BIGBOX_R)) {
    const f = fps[fi], g = ringPointGap(f.pts, core.x, core.z);
    if (g <= BIGBOX_R && f.area > M.store) { M.store = Math.round(f.area); M.storeGap = g; }
  }
  M.kioskTagged = fs.some(f => f.rings && f.building && !f.roof);
  return M;
}

function classify(fs, core, M) {
  for (const f of fs) if (OV.skip && OV.skip[f.id]) return { cls: 'skipped', why: OV.skip[f.id] };
  for (const f of fs) if (OV.class && OV.class[f.id]) return { cls: OV.class[f.id][0], why: OV.class[f.id][1] };
  if (fs.some(f => f.truck)) return { cls: 'truck', why: 'tagged for trucks (hgv / HGV diesel)' };
  // a fuel point standing IN a big store's parking field: further from every street than a
  // filling station's lot reaches, a big store near (a warehouse club's or a supermarket's)
  if (M.store >= BIGBOX_M2 && M.storeGap <= BIGBOX_R && M.roadGap >= BIGBOX_SETBACK_M)
    return { cls: 'bigbox', why: `in a parking field ${Math.round(M.roadGap)} m off the street, a ${M.store} m2 store ${Math.round(M.storeGap)} m away` };
  return { cls: 'forecourt', why: '' };
}

/// Frontage candidates: surface roads (not a motorway, ramp, tunnel or bridge) whose pavement is within reach.
function frontages(core, reach) {
  const out = new Map();
  const R = reach + 20;
  for (const si of segGrid.near(core.x - R, core.z - R, core.x + R, core.z + R)) {
    const s = segs[si], e = city.edges[s[4]];
    if (e.link || e.tunnel || e.bridge || e.rank >= 5 || e.roundabout) continue;
    if (e.level && e.level !== 0) continue;
    const d = segDist(core.x, core.z, s[0], s[1], s[2], s[3]) - halfOf(e);
    if (d > reach) continue;
    const prev = out.get(e.index);
    if (!prev || d < prev) out.set(e.index, d);
  }
  // a bigger road wins a corner: 5 m of the gap per class (rank 0 local .. 4 trunk)
  return [...out.entries()].map(([ei, d]) => ({ e: city.edges[ei], gap: Math.max(0, d), score: Math.max(0, d) - 5 * Math.min(4, city.edges[ei].rank) }))
    .sort((a, b) => a.score - b.score || a.e.index - b.e.index);
}

/// Is a street a sidewalk street (CityMeshes.WalkEdge, roughly)?
function walkEdge(e) {
  if (e.link || e.rank > 3 || e.tunnel || e.bridge || e.a === e.b) return false;
  if (e.width < (e.oneway ? 4 : 6)) return false;
  for (let k = 1; k <= 3; k++) {
    const p = pointAt(e, e.length * k * 0.25);
    if (Math.hypot(p.x - UPTOWN[0], p.z - UPTOWN[1]) < 2200) return true;
    for (const fi of fpGrid.near(p.x - 40, p.z - 40, p.x + 40, p.z + 40)) { const f = fps[fi]; if (!f.house && ringPointGap(f.pts, p.x, p.z) <= 40) return true; }
  }
  return false;
}
const walkCache = new Map();
const isWalk = e => { if (!walkCache.has(e.index)) walkCache.set(e.index, walkEdge(e)); return walkCache.get(e.index); };
/// From the centreline to the lot's front line: the pavement, then the walk (or the verge).
const frontLine = e => halfOf(e) + (isWalk(e) ? WALK_W + 0.3 : 0.6);

/// Every check of one candidate placement; null when it fits, else the reason.
function test(F, L, ownFp, frontE) {
  const lotW = rectWorld(F, L.lot), lb = boxOf(lotW);
  // other stations
  for (const R of placedLots) if (ringGap(R, lotW) < 1) return 'another station';
  // roads: no part within any road's pavement + walk/clear zone, no lot within its pavement + walk/verge
  for (const si of segGrid.near(lb[0] - 40, lb[1] - 40, lb[2] + 40, lb[3] + 40)) {
    const s = segs[si], e = city.edges[s[4]];
    if (e.tunnel) continue;
    const a = [s[0], s[1]], b = [s[2], s[3]];
    const lotNeed = frontE && e.index === frontE.index ? frontLine(e) - 0.35 : halfOf(e) + (isWalk(e) ? WALK_W : 0.6);
    for (let k = 0; k < 4; k++) if (segSeg(lotW[k], lotW[(k + 1) % 4], a, b) < lotNeed) return 'road ' + (e.name || '#' + e.index);
    if (inRing(lotW, a[0], a[1])) return 'road ' + (e.name || '#' + e.index);
    for (const p of L.parts) {
      if (p[0] === 'dumpster') continue;
      const pw = rectWorld(F, p.slice(1));
      const need = halfOf(e) + Math.max(clearZoneOf(e), isWalk(e) ? WALK_W + 0.3 : 0);
      for (let k = 0; k < 4; k++) if (segSeg(pw[k], pw[(k + 1) % 4], a, b) < need) return p[0] + ' in the clear zone of ' + (e.name || '#' + e.index);
    }
  }
  // buildings: parts clear of every footprint but the station's own; the lot mostly
  let lotOnBld = 0;
  const lotCells = (L.lot[1] - L.lot[0]) * (L.lot[3] - L.lot[2]);
  for (const fi of fpGrid.near(lb[0] - 5, lb[1] - 5, lb[2] + 5, lb[3] + 5)) {
    if (ownFp.has(fi)) continue;
    const f = fps[fi];
    for (const p of L.parts) {
      if (p[0] === 'dumpster') continue;
      if (ringGap(rectWorld(F, p.slice(1)), f.pts) < PART_CLEAR_M) return p[0] + ' on a building';
    }
    if (ringGap(lotW, f.pts) === 0) lotOnBld += overlapArea(F, L.lot, f.pts);
  }
  if (lotOnBld > LOT_BLD_MAX * lotCells) return 'lot on buildings';
  // water: no part within a creek's banks + 6 m, nor the lot within + 2 m; nothing in a lake
  for (const wi of waterGrid.near(lb[0] - 30, lb[1] - 30, lb[2] + 30, lb[3] + 30)) {
    const w = waterSegs[wi];
    const a = [w[0], w[1]], b = [w[2], w[3]];
    for (let k = 0; k < 4; k++) if (segSeg(lotW[k], lotW[(k + 1) % 4], a, b) < w[4] + 2) return 'water';
  }
  for (const r of lakes) if (inRing(r, F.ox, F.oz)) return 'lake';
  return null;
}

/// Ground relief under the lot (the DEM, metres).
function relief(F, L) {
  let lo = Infinity, hi = -Infinity;
  for (let u = L.lot[0]; u <= L.lot[1] + 1e-6; u += (L.lot[1] - L.lot[0]) / 4)
    for (let v = L.lot[2]; v <= L.lot[3] + 1e-6; v += (L.lot[3] - L.lot[2]) / 4) {
      const h = dem.at(F.ox + F.ux * u + F.vx * v, F.oz + F.uz * u + F.vz * v);
      lo = Math.min(lo, h); hi = Math.max(hi, h);
    }
  return hi - lo;
}

/// The station's own buildings the model stands in place of: the fuel outlines the city
/// draws (a canopy or kiosk OSM tagged amenity=fuel), and - only where OSM tagged no kiosk
/// - the ONE small building nearest the fuel within KIOSK_R (its shop, drawn untagged).
/// Every other building stays, and blocks.
function ownBuildings(fs, polys, core) {
  const own = new Map();
  for (const f of polys) for (const r of f.rings) {
    const c = centroid(r), a = Math.abs(polyArea(r));
    for (const fi of fpGrid.near(c[0] - 3, c[1] - 3, c[0] + 3, c[1] + 3)) {
      const fp = fps[fi];
      if (Math.hypot(fp.c[0] - c[0], fp.c[1] - c[1]) < 2.0 && fp.area > a * 0.6 && fp.area < a * 1.6) own.set(fi, 'fuel outline');
    }
  }
  if (!fs.some(f => f.rings && f.building && !f.roof)) {
    let best = -1, bestGap = KIOSK_R;
    for (const fi of fpGrid.near(core.x - 60, core.z - 60, core.x + 60, core.z + 60)) {
      const f = fps[fi];
      if (own.has(fi) || f.house || f.hidden || f.area >= KIOSK_M2) continue;
      const gap = Math.min(...fs.map(ff => ff.rings ? Math.min(...ff.rings.map(r => ringGap(r, f.pts))) : ringPointGap(f.pts, ff.x, ff.z)));
      if (gap < bestGap || (gap === bestGap && fi < best)) { bestGap = gap; best = fi; }
    }
    if (best >= 0) own.set(best, 'shop beside the fuel');
  }
  return own;
}

/// The cost of a candidate: how far its canopy (and shop) stand from OSM's own.
/// (kioskAt: the centre of the station's shop as OSM drew it - a tagged kiosk, else the
/// untagged shop beside the fuel - or null.)
function costOf(F, L, canopies, kioskAt, core) {
  const cw = r => [F.ox + F.ux * (r[1] + r[2]) / 2 + F.vx * (r[3] + r[4]) / 2, F.oz + F.uz * (r[1] + r[2]) / 2 + F.vz * (r[3] + r[4]) / 2];
  const can = L.parts.find(q => q[0] === 'canopy'), shop = L.parts.find(q => q[0] === 'shop');
  const cc = cw(can);
  if (canopies.length) {
    const oc = centroid(canopies[0].rings[0]);
    let c = Math.hypot(cc[0] - oc[0], cc[1] - oc[1]);
    if (shop && kioskAt) { const sc = cw(shop); c += 0.5 * Math.hypot(sc[0] - kioskAt[0], sc[1] - kioskAt[1]); }
    return c;
  }
  if (kioskAt && shop) { const sc = cw(shop); return Math.hypot(sc[0] - kioskAt[0], sc[1] - kioskAt[1]) + 0.3 * Math.hypot(cc[0] - core.x, cc[1] - core.z); }
  const q = [cc]; if (shop) q.push(cw(shop));
  return Math.min(...q.map(p => Math.hypot(p[0] - core.x, p[1] - core.z)));
}

/// The OSM canopy's long axis (unit) and length, or null.
function canopyAxis(canopies) {
  if (!canopies.length) return null;
  const r = canopies.slice().sort((a, b) => b.area - a.area)[0].rings[0];
  let best = null;
  for (let i = 0; i < r.length; i++) {
    const a = r[i], b = r[(i + 1) % r.length], L = Math.hypot(b[0] - a[0], b[1] - a[1]);
    if (L < 1) continue;
    const ux = (b[0] - a[0]) / L, uz = (b[1] - a[1]) / L;
    let u0 = Infinity, u1 = -Infinity, v0 = Infinity, v1 = -Infinity;
    for (const p of r) { const u = p[0] * ux + p[1] * uz, v = -p[0] * uz + p[1] * ux; u0 = Math.min(u0, u); u1 = Math.max(u1, u); v0 = Math.min(v0, v); v1 = Math.max(v1, v); }
    const A = (u1 - u0) * (v1 - v0);
    if (!best || A < best.A - 1e-9) best = u1 - u0 >= v1 - v0 ? { A, ux, uz, len: u1 - u0, wid: v1 - v0 } : { A, ux: -uz, uz: ux, len: v1 - v0, wid: u1 - u0 };
  }
  // a canonical sign, so the answer does not depend on the ring's winding
  if (best && (best.ux < -1e-9 || (Math.abs(best.ux) <= 1e-9 && best.uz < 0))) { best.ux = -best.ux; best.uz = -best.uz; }
  return best;
}

/// THE FRONTAGE PLACEMENT: the layouts in order, each mirrored or not, slid along the road
/// and set back from it; the first layout that fits anywhere, at its cheapest place.
function placeOnFrontage(fs, fr, own, core, canopies, kiosks, truckWant) {
  // where OSM drew the station's shop: its tagged kiosk, else the untagged shop beside the fuel
  let kioskAt = kiosks.length ? centroid(kiosks[0].rings[0]) : null;
  if (!kioskAt) for (const [fi, why] of own) if (why === 'shop beside the fuel') kioskAt = fps[fi].c;
  const front0 = fr[0].e, pj0 = project(front0, core.x, core.z), t0p = pointAt(front0, pj0.s);
  // how many canopy modules OSM's canopy asks for: its length along the road
  let nMod = 1;
  if (canopies.length) {
    let lo = Infinity, hi = -Infinity;
    for (const f of canopies) for (const r of f.rings) for (const [x, z] of r) { const u = (x - t0p.x) * t0p.tx + (z - t0p.z) * t0p.tz; lo = Math.min(lo, u); hi = Math.max(hi, u); }
    nMod = Math.max(1, Math.min(3, Math.round((hi - lo) / CAN_W)));
  }
  // where OSM drew the shop against the canopy (both outlined): behind it, or beside it
  let first = 0;
  if (canopies.length && kiosks.length) {
    const cc = centroid(canopies[0].rings[0]), kc = centroid(kiosks[0].rings[0]);
    const vx = pj0.side > 0 ? -t0p.tz : t0p.tz, vz = pj0.side > 0 ? t0p.tx : -t0p.tx;
    const du = (kc[0] - cc[0]) * t0p.tx + (kc[1] - cc[1]) * t0p.tz, dv = (kc[0] - cc[0]) * vx + (kc[1] - cc[1]) * vz;
    first = Math.abs(dv) >= Math.abs(du) ? 0 : 1;
  }
  const order = [];
  if (truckWant) order.push([3, 2]);
  for (const k of [first, 1 - first]) for (const n of nMod > 1 ? [nMod, nMod - 1, 1] : [1]) order.push([k, n]);
  if (truckWant) order.push([1, 2], [0, 2]);
  order.push([2, Math.max(1, nMod)], [2, 1]);
  const tries = [];
  for (const [k, n] of order) if (!tries.some(t => t[0] === k && t[1] === n)) tries.push([k, n]);
  // every layout competes on one cost: how far its canopy and shop stand from OSM's own, how
  // far back from the road, a second-choice frontage, and how far down the list of layouts
  // (the canopy alone pays most: it is the model without its shop)
  let best = null, lastWhy = '';
  const frs = fr.slice(0, 2);
  for (let ti = 0; ti < tries.length; ti++) {
    const [kind, n] = tries[ti];
    const layoutCost = ti * LAYOUT_STEP + (kind === 2 ? CANOPY_ALONE_COST : 0) + (truckWant && kind !== 3 ? TRUCK_LAYOUT_COST : 0);
    for (const fcand of frs) {
      const e = fcand.e, pj = project(e, core.x, core.z);
      for (const mirror of [false, true]) {
        if (kind === 2 && mirror) continue;   // the canopy alone is symmetric
        const L = layout(kind, n, mirror);
        for (const back of BACKS) {
          for (const slide of [0, -3, 3, -6, 6, -9, 9, -13, 13, -18, 18, -24, 24]) {
            const s = pj.s + slide;
            if (s < 0 || s > e.length) continue;
            const p = pointAt(e, s);
            const vx = pj.side > 0 ? -p.tz : p.tz, vz = pj.side > 0 ? p.tx : -p.tx;   // away from the road
            const ux = vz, uz = -vx;                                                   // along it (right of +v)
            const off = frontLine(e) + back;
            const F = { ox: p.x + vx * off, oz: p.z + vz * off, ux, uz, vx, vz };
            // the cost first (cheap), the checks (dear) only for a candidate that could win
            const cost = costOf(F, L, canopies, kioskAt, core) + 0.25 * back + (fcand === frs[0] ? 0 : 6) + layoutCost;
            if (best && cost >= best.cost) continue;
            const why = test(F, L, own, e);
            if (why) { lastWhy = why; continue; }
            // a way in: at least one driveway the rules allow
            const D = driveways({ F, L, e, back }, fr, own);
            if (!D.drives.length) { lastWhy = 'no driveway (junctions, buildings, other roads in the way)'; continue; }
            best = { cost, kind, n, mirror, back, slide, F, L, e, s, side: pj.side, D };
          }
        }
      }
    }
  }
  return { best, lastWhy };
}

/// THE IN-LOT PLACEMENT (a fuel point in a big store's parking field): the canopy where
/// OSM drew it, its islands along OSM's canopy (else along the lot's stall rows), its front
/// toward the nearest street; no lot of its own (the parking field is paved) and no
/// driveway (the field's own entrances serve it).
function placeInLot(fs, own, core, canopies, kiosks, fr) {
  const ax = canopyAxis(canopies);
  const n = ax ? Math.max(1, Math.min(3, Math.round(ax.len / CAN_W))) : 2;
  const at = ax ? centroid(canopies.slice().sort((a, b) => b.area - a.area)[0].rings[0]) : [core.x, core.z];
  let ux = 1, uz = 0;
  if (ax) { ux = ax.ux; uz = ax.uz; }
  else if (fr.length) { const pj = project(fr[0].e, core.x, core.z), p = pointAt(fr[0].e, pj.s); ux = p.tx; uz = p.tz; }
  // +v away from the nearest street
  let vx = -uz, vz = ux;
  if (fr.length) {
    const pj = project(fr[0].e, at[0], at[1]), p = pointAt(fr[0].e, pj.s);
    if ((at[0] - p.x) * vx + (at[1] - p.z) * vz < 0) { vx = -vx; vz = -vz; ux = -ux; uz = -uz; }
  }
  let best = null, lastWhy = '';
  for (const nn of n > 1 ? [n, n - 1, 1] : [1]) {
    const L = layout(2, nn, false);
    // the canopy's centre on OSM's: the station's origin (the lot's front-centre) in front of it
    const can = L.parts.find(q => q[0] === 'canopy');
    const cu = (can[1] + can[2]) / 2, cv = (can[3] + can[4]) / 2;
    for (const [du, dv] of [[0, 0], [3, 0], [-3, 0], [0, 3], [0, -3], [6, 0], [-6, 0], [0, 6], [0, -6]]) {
      const F = { ux, uz, vx, vz, ox: at[0] - ux * (cu - du) - vx * (cv - dv), oz: at[1] - uz * (cu - du) - vz * (cv - dv) };
      const cost = Math.hypot(du, dv);
      if (best && cost >= best.cost) continue;
      const why = test(F, L, own, null);
      if (why) { lastWhy = why; continue; }
      best = { cost, kind: 2, n: nn, mirror: false, back: 0, slide: 0, F, L, e: fr.length ? fr[0].e : null };
    }
    if (best) break;
  }
  if (best && best.e) { const pj = project(best.e, best.F.ox, best.F.oz); best.s = pj.s; best.side = pj.side; }
  return { best, lastWhy };
}

/// Driveways: near each end of the lot's front (one in the middle of a narrow lot), each on
/// the surface road nearest that point of the front, its mouth DRIVE_CORNER_M clear of every
/// junction (else DRIVE_CORNER_MIN_M, flagged tight), never on a roundabout, its run to the lot
/// over no building and across no other road; and, on a corner lot, one on the side street.
const junctionGrid = new Grid(64);
city.nodes.forEach((n, i) => { if (nodeDeg[i] >= 3) junctionGrid.add(n.x, n.z, n.x, n.z, i); });
function junctionGap(x, z) {
  let g = Infinity;
  for (const ni of junctionGrid.near(x - 40, z - 40, x + 40, z + 40)) g = Math.min(g, Math.hypot(city.nodes[ni].x - x, city.nodes[ni].z - z));
  return g;
}
function driveTo(P, reach, need, own, onlyEdge) {
  // the nearest surface road to the lot-front point P
  let best = null;
  for (const si of segGrid.near(P[0] - reach, P[1] - reach, P[0] + reach, P[1] + reach)) {
    const sg = segs[si], e = city.edges[sg[4]];
    if (e.link || e.tunnel || e.bridge || e.rank >= 5 || e.roundabout) continue;
    if (onlyEdge != null && e.index !== onlyEdge) continue;
    const d = segDist(P[0], P[1], sg[0], sg[1], sg[2], sg[3]);
    if (d <= reach && (!best || d < best.d)) best = { e, d };
  }
  const dbg = why => { if (DRIVE_DEBUG) console.log('  drive at (' + P[0].toFixed(0) + ',' + P[1].toFixed(0) + '): ' + why); return null; };
  if (!best) return dbg('no road within ' + reach.toFixed(0) + ' m');
  const e = best.e, pj = project(e, P[0], P[1]), q = pointAt(e, pj.s);
  if (pj.s <= 0.5 || pj.s >= e.length - 0.5) return dbg('off the end of edge ' + e.index + ' (' + (e.name || '') + ') s ' + pj.s.toFixed(1) + '/' + e.length.toFixed(1));
  if (junctionGap(q.x, q.z) - DRIVE_HALF < need) return dbg('junction ' + junctionGap(q.x, q.z).toFixed(1) + ' m on ' + (e.name || e.index));
  const len = pj.d - halfOf(e);
  if (len < 0.3) return dbg('lot on the road');
  // its run: no other road across it, no building on it
  const nx = (P[0] - q.x) / pj.d, nz = (P[1] - q.z) / pj.d, h = DRIVE_HALF * 0.9;
  const a = [q.x + nx * halfOf(e), q.z + nz * halfOf(e)];
  const strip = [[a[0] - nz * h, a[1] + nx * h], [P[0] - nz * h, P[1] + nx * h], [P[0] + nz * h, P[1] - nx * h], [a[0] + nz * h, a[1] - nx * h]];
  const sb = boxOf(strip);
  for (const si of segGrid.near(sb[0], sb[1], sb[2], sb[3])) {
    const sg = segs[si];
    if (sg[4] === e.index || city.edges[sg[4]].tunnel) continue;
    if (crosses(a[0], a[1], P[0], P[1], sg[0], sg[1], sg[2], sg[3])) return dbg('crosses ' + (city.edges[sg[4]].name || 'edge ' + sg[4]) + ' r' + city.edges[sg[4]].rank + (city.edges[sg[4]].link ? 'L' : ''));
  }
  for (const fi of fpGrid.near(sb[0], sb[1], sb[2], sb[3])) if (!own.has(fi) && ringGap(strip, fps[fi].pts) === 0) return dbg('building ' + fi + ' ' + Math.round(fps[fi].area) + ' m2');
  // the mouth on the road's pavement edge, and the way in (world: the runtime needs no graph index)
  return { edge: e.index, s: pj.s, side: pj.side, half: DRIVE_HALF, len, mx: a[0], mz: a[1], dx: nx, dz: nz };
}
function driveways(rec, fr, own) {
  const B = rec, lotU0 = B.L.lot[0], lotU1 = B.L.lot[1];
  const out = [];
  let tight = 0;
  const reach = frontLine(B.e) + (B.back || 0) + 12;
  const wantU = lotU1 - lotU0 >= 26 ? [lotU0 + DRIVE_HALF + 1.2, lotU1 - DRIVE_HALF - 1.2] : [(lotU0 + lotU1) / 2];
  for (const need of [DRIVE_CORNER_M, DRIVE_CORNER_MIN_M]) {
    for (const u0 of wantU) {
      let got = null;
      for (let u = u0, k = 0; k < 10 && !got; k++, u += (u0 <= (lotU0 + lotU1) / 2 ? 1 : -1) * 2.0) {
        if (u - DRIVE_HALF < lotU0 - 0.01 || u + DRIVE_HALF > lotU1 + 0.01) break;
        got = driveTo([B.F.ox + B.F.ux * u, B.F.oz + B.F.uz * u], reach, need, own, null);
      }
      if (got && !out.some(d => d.edge === got.edge && Math.abs(d.s - got.s) < 2 * DRIVE_HALF + 3)) { out.push(got); if (need < DRIVE_CORNER_M) tight++; }
    }
    if (out.length) break;
  }
  // a corner lot: a side street across the frontage, along one side of the lot
  let corner = false;
  for (const fc of fr) {
    const e2 = fc.e;
    if (e2.index === B.e.index || e2.roundabout) continue;
    const mid = [B.F.ox + B.F.vx * (B.L.lot[3] / 2), B.F.oz + B.F.vz * (B.L.lot[3] / 2)];
    const pj2 = project(e2, mid[0], mid[1]);
    const p2 = pointAt(e2, pj2.s);
    if (Math.abs(p2.tx * B.F.ux + p2.tz * B.F.uz) > 0.5) continue;   // runs along the frontage: not a side street
    if (e2.name && e2.name === B.e.name) continue;                    // the frontage road itself, round a bend
    const gap = pj2.d - halfOf(e2);
    if (gap > Math.max(Math.abs(lotU0), Math.abs(lotU1)) + 14) continue;
    corner = true;
    // the side-street drive's lot end: on the lot's side line facing it, half way back
    const sideU = ((mid[0] - p2.x) * B.F.ux + (mid[1] - p2.z) * B.F.uz) > 0 ? lotU0 : lotU1;
    for (const v of [B.L.lot[3] * 0.55, B.L.lot[3] * 0.7, B.L.lot[3] * 0.4]) {
      const P = [B.F.ox + B.F.ux * sideU + B.F.vx * v, B.F.oz + B.F.uz * sideU + B.F.vz * v];
      const got = driveTo(P, gap + Math.abs(sideU) + 20, DRIVE_CORNER_M, own, e2.index);
      if (got) { got.side2 = true; out.push(got); break; }
    }
    break;
  }
  return { drives: out, corner, tight };
}

/// An in-lot station's way in: where its own pad does not touch a parking lot the city paves,
/// the shortest straight aisle (LINK_HALF each side) from the pad to the nearest paved lot or
/// surface road within LINK_MAX_M that crosses no building.
function accessLinks(rec, own) {
  const lotW = rectWorld(rec.F, rec.L.lot), lb = boxOf(lotW);
  for (const li of lotGrid.near(lb[0] - 1, lb[1] - 1, lb[2] + 1, lb[3] + 1)) if (ringGap(lotW, lots[li].ring) <= 1) return [];
  // points round the pad's edge, every metre
  const rim = [];
  for (let k = 0; k < 4; k++) {
    const a = lotW[k], b = lotW[(k + 1) % 4], L = Math.hypot(b[0] - a[0], b[1] - a[1]);
    for (let t = 0; t <= L; t += 1) rim.push([a[0] + (b[0] - a[0]) * t / L, a[1] + (b[1] - a[1]) * t / L]);
  }
  const cands = [];
  const nearestOn = (px, pz, ax, az, bx, bz) => { const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz; const t = L2 > 1e-12 ? Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / L2)) : 0; return [ax + dx * t, az + dz * t]; };
  for (const li of lotGrid.near(lb[0] - LINK_MAX_M, lb[1] - LINK_MAX_M, lb[2] + LINK_MAX_M, lb[3] + LINK_MAX_M)) {
    const r = lots[li].ring;
    for (const p of rim) for (let i = 0; i < r.length; i++) {
      const q = nearestOn(p[0], p[1], r[i][0], r[i][1], r[(i + 1) % r.length][0], r[(i + 1) % r.length][1]);
      const d = Math.hypot(q[0] - p[0], q[1] - p[1]);
      if (d <= LINK_MAX_M) cands.push([d, p, q, 'lot']);
    }
  }
  for (const si of segGrid.near(lb[0] - LINK_MAX_M - 30, lb[1] - LINK_MAX_M - 30, lb[2] + LINK_MAX_M + 30, lb[3] + LINK_MAX_M + 30)) {
    const sg = segs[si], e = city.edges[sg[4]];
    if (e.link || e.tunnel || e.bridge || e.rank >= 5) continue;
    for (const p of rim) {
      const q = nearestOn(p[0], p[1], sg[0], sg[1], sg[2], sg[3]);
      const d = Math.hypot(q[0] - p[0], q[1] - p[1]) - halfOf(e);
      if (d <= LINK_MAX_M && d > 0) { const t = d / (d + halfOf(e)); cands.push([d, p, [p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t], 'road']); }
    }
  }
  cands.sort((a, b) => a[0] - b[0] || a[1][0] - b[1][0] || a[1][1] - b[1][1]);
  for (const [d, p, q] of cands) {
    if (d < 0.5) return [];
    const dx = q[0] - p[0], dz = q[1] - p[1], L = Math.hypot(dx, dz), nx = -dz / L * LINK_HALF, nz = dx / L * LINK_HALF;
    const strip = [[p[0] + nx, p[1] + nz], [q[0] + nx, q[1] + nz], [q[0] - nx, q[1] - nz], [p[0] - nx, p[1] - nz]];
    const sb = boxOf(strip);
    let blocked = false;
    for (const fi of fpGrid.near(sb[0], sb[1], sb[2], sb[3])) if (!own.has(fi) && ringGap(strip, fps[fi].pts) === 0) { blocked = true; break; }
    if (!blocked) return [{ ax: p[0], az: p[1], bx: q[0], bz: q[1], half: LINK_HALF }];
  }
  return [];
}

let mergedFeatures = 0;
let DRIVE_DEBUG = false;
for (const fs of clusterList) {
  DRIVE_DEBUG = !!process.env.PSX_FUEL_DEBUG && fs.some(f => process.env.PSX_FUEL_DEBUG.split(',').includes(f.id));
  if (fs.length > 1) mergedFeatures += fs.length - 1;
  const polys = fs.filter(f => f.rings);
  const canopies = polys.filter(f => f.roof || !f.building);
  const kiosks = polys.filter(f => f.building && !f.roof);
  // the core: the outlines' area-weighted centre, else the points'
  let core;
  if (polys.length) {
    let A = 0, cx = 0, cz = 0;
    for (const f of polys) for (const r of f.rings) { const a = Math.abs(polyArea(r)), c = centroid(r); A += a; cx += c[0] * a; cz += c[1] * a; }
    core = { x: cx / A, z: cz / A };
  } else core = { x: fs.reduce((s, f) => s + f.x, 0) / fs.length, z: fs.reduce((s, f) => s + f.z, 0) / fs.length };
  const main = polys.slice().sort((a, b) => b.area - a.area || (a.id < b.id ? -1 : 1))[0] || fs[0];
  const M = measures(fs, core);
  const C = classify(fs, core, M);
  const rec = { features: fs.map(f => f.id), osm: main, core, cls: C.cls, why: C.why, node: !polys.length, M };
  if (C.cls === 'skipped' || C.cls === 'private' || C.cls === 'marina' || C.cls === 'airport') { skipped.push(rec); continue; }
  if (process.env.PSX_FUEL_DEBUG && fs.some(f => process.env.PSX_FUEL_DEBUG.split(',').includes(f.id))) console.log('DEBUG', fs.map(f => f.id).join(' '), JSON.stringify(M), C.cls);
  const own = ownBuildings(fs, polys, core);
  const fr = frontages(core, C.cls === 'truck' ? TRUCK_FRONT_MAX_M : C.cls === 'bigbox' ? 400 : FRONT_MAX_M);
  if (!fr.length) { rec.cls = 'skipped'; rec.why = 'its street is not in the city data (no drawn surface road within ' + FRONT_MAX_M + ' m)'; skipped.push(rec); continue; }
  const { best, lastWhy } = C.cls === 'bigbox' ? placeInLot(fs, own, core, canopies, kiosks, fr)
    : placeOnFrontage(fs, fr, own, core, canopies, kiosks, C.cls === 'truck');
  if (!best) {
    rec.placed = false; rec.whyNot = lastWhy;
    // stored, NOT PLACED: the runtime keeps the pole sign and stands nothing
    const e = fr[0].e, pj = project(e, core.x, core.z), p = pointAt(e, pj.s);
    const vx = pj.side > 0 ? -p.tz : p.tz, vz = pj.side > 0 ? p.tx : -p.tx;
    const off = frontLine(e);
    rec.F = { ox: p.x + vx * off, oz: p.z + vz * off, ux: vz, uz: -vx, vx, vz };
    rec.L = layout(0, 1, false); rec.kind = 0; rec.n = 1; rec.mirror = false; rec.e = e; rec.s = pj.s; rec.side = pj.side;
    rec.drives = []; rec.hide = []; rec.links = []; rec.corner = false; rec.tight = 0; rec.relief = relief(rec.F, rec.L);
    stations.push(rec);
    continue;
  }
  Object.assign(rec, { placed: true, kind: best.kind, n: best.n, mirror: best.mirror, back: best.back, slide: best.slide, F: best.F, L: best.L, e: best.e, s: best.s, side: best.side, cost: best.cost });
  placedLots.push(rectWorld(best.F, best.L.lot));
  // the buildings the model stands in place of: its own, where the lot covers them
  // (a part of the model stands on it, or the station's lot covers half of it; else it stays)
  rec.hide = [];
  const lotW = rectWorld(best.F, best.L.lot);
  for (const [fi, why] of [...own.entries()].sort((a, b) => a[0] - b[0])) {
    const f = fps[fi];
    const onPart = best.L.parts.some(q => q[0] !== 'pylon' && ringGap(rectWorld(best.F, q.slice(1)), f.pts) === 0);
    let inLot = 0, n = 0;
    const [bx0, bz0, bx1, bz1] = [f.x0, f.z0, f.x1, f.z1];
    for (let x = bx0 + 0.5; x < bx1; x += 1) for (let z = bz0 + 0.5; z < bz1; z += 1) if (inRing(f.pts, x, z)) { n++; if (inRing(lotW, x, z)) inLot++; }
    if (!onPart && !(n > 0 && inLot >= 0.5 * n)) continue;
    rec.hide.push({ fi, x: f.c[0], z: f.c[1], area: f.area, why });
  }
  if (C.cls === 'bigbox') { rec.drives = []; rec.corner = false; rec.tight = 0; rec.links = accessLinks(rec, own); }
  else { Object.assign(rec, best.D); rec.links = []; }
  rec.relief = relief(best.F, best.L);
  // how far it stands from OSM's own (its canopy or shop from OSM's nearest outline or point)
  {
    const cw = r => [best.F.ox + best.F.ux * (r[1] + r[2]) / 2 + best.F.vx * (r[3] + r[4]) / 2, best.F.oz + best.F.uz * (r[1] + r[2]) / 2 + best.F.vz * (r[3] + r[4]) / 2];
    const mine = best.L.parts.filter(q => q[0] === 'canopy' || q[0] === 'shop').map(cw);
    const theirs = fs.map(f => f.rings ? centroid(f.rings[0]) : [f.x, f.z]);
    rec.displaced = Math.min(...mine.flatMap(a => theirs.map(b => Math.hypot(a[0] - b[0], a[1] - b[1]))));
  }
  // how much of the lot lies on OSM parking (the runtime does not pave it twice)
  let onLots = 0;
  const lb2 = boxOf(lotW);
  for (const li of lotGrid.near(lb2[0], lb2[1], lb2[2], lb2[3])) onLots += overlapArea(best.F, best.L.lot, lots[li].ring);
  rec.onLotsM2 = onLots;
  stations.push(rec);
}
stations.sort((a, b) => a.F.ox - b.F.ox || a.F.oz - b.F.oz);

// ------------------------------------------------------------------ names of places (streets), for the lists
const nameOf = e => e ? (e.name || ('edge ' + e.index)) : '?';
function crossStreet(st) {
  // the nearest other named road to the lot's front-centre
  let best = null, bd = 160;
  for (const si of segGrid.near(st.F.ox - 160, st.F.oz - 160, st.F.ox + 160, st.F.oz + 160)) {
    const s = segs[si], e = city.edges[s[4]];
    if (!e.name || e.name === st.e.name || e.link) continue;
    const d = segDist(st.F.ox, st.F.oz, s[0], s[1], s[2], s[3]);
    if (d < bd) { bd = d; best = e; }
  }
  return best ? best.name : null;
}
for (const st of stations) { st.place = nameOf(st.e) + (crossStreet(st) ? ' near ' + crossStreet(st) : ''); }
for (const r of skipped) {
  // the nearest named road (any class), for the lists
  let best = null, bd = 400;
  for (const si of segGrid.near(r.core.x - 400, r.core.z - 400, r.core.x + 400, r.core.z + 400)) {
    const sg = segs[si], e = city.edges[sg[4]];
    if (!e.name) continue;
    const d = segDist(r.core.x, r.core.z, sg[0], sg[1], sg[2], sg[3]);
    if (d < bd) { bd = d; best = e; }
  }
  r.place = best ? Math.round(bd) + ' m from ' + best.name : 'no named road within 400 m';
}

// ------------------------------------------------------------------ encode
const CLS = { forecourt: 0, truck: 1, bigbox: 2 };
const parts = [];
const head = Buffer.alloc(16);
head.write('PFUL', 0, 'latin1'); head.writeInt32LE(1, 4); head.writeUInt32LE(city.storedHash >>> 0, 8); head.writeUInt32LE(crc32(bldBytes) >>> 0, 12);
parts.push(head);
{ const b = Buffer.alloc(2); b.writeUInt16LE(stations.length); parts.push(b); }
const yawOf = st => Math.atan2(st.F.vx, st.F.vz) * 180 / Math.PI;
for (const st of stations) {
  const flags = (st.mirror ? 1 : 0) | (st.corner ? 2 : 0) | (st.placed ? 0 : 4) | (st.relief >= STEEP_M ? 8 : 0) | (st.node ? 16 : 0) | (isMisfit(st) ? 32 : 0);
  const b = Buffer.alloc(12 + 4 + 8 + 9);
  let o = 0;
  b.writeFloatLE(st.F.ox, o); o += 4; b.writeFloatLE(st.F.oz, o); o += 4; b.writeFloatLE(yawOf(st), o); o += 4;
  b.writeUInt8(CLS[st.cls], o++); b.writeUInt8(st.kind, o++); b.writeUInt8(st.n, o++); b.writeUInt8(flags, o++);
  b.writeFloatLE(st.L.lot[1] - st.L.lot[0], o); o += 4; b.writeFloatLE(st.L.lot[3] - st.L.lot[2], o); o += 4;
  b.writeUInt32LE(st.e.index, o); o += 4; b.writeFloatLE(st.s, o); o += 4; b.writeInt8(st.side, o++);
  parts.push(b);
  { const d = Buffer.alloc(1 + st.drives.length * 33); d.writeUInt8(st.drives.length, 0);
    st.drives.forEach((v, i) => { const q = 1 + i * 33; d.writeFloatLE(v.mx, q); d.writeFloatLE(v.mz, q + 4); d.writeFloatLE(v.dx, q + 8); d.writeFloatLE(v.dz, q + 12);
      d.writeFloatLE(v.half, q + 16); d.writeFloatLE(v.len, q + 20); d.writeUInt32LE(v.edge, q + 24); d.writeFloatLE(v.s, q + 28); d.writeInt8(v.side, q + 32); });
    parts.push(d); }
  { const k = Buffer.alloc(1 + st.links.length * 20); k.writeUInt8(st.links.length, 0);
    st.links.forEach((v, i) => { const q = 1 + i * 20; k.writeFloatLE(v.ax, q); k.writeFloatLE(v.az, q + 4); k.writeFloatLE(v.bx, q + 8); k.writeFloatLE(v.bz, q + 12); k.writeFloatLE(v.half, q + 16); });
    parts.push(k); }
  { const h = Buffer.alloc(1 + st.hide.length * 8); h.writeUInt8(st.hide.length, 0);
    st.hide.forEach((v, i) => { h.writeFloatLE(v.x, 1 + i * 8); h.writeFloatLE(v.z, 5 + i * 8); });
    parts.push(h); }
  { const m = Buffer.alloc(9); m.writeUInt8(st.osm.type, 0); m.writeBigUInt64LE(st.osm.osm, 1); parts.push(m); }
}
const bytes = Buffer.concat(parts);

// ------------------------------------------------------------------ the numbers
const byCls = {}; for (const st of stations) { const k = st.cls + (st.placed ? '' : ' (not placed)'); byCls[k] = (byCls[k] || 0) + 1; }
const byLayout = {}; for (const st of stations) if (st.placed) { const k = LAYOUT_NAME[st.kind] + (st.n > 1 ? ' x' + st.n : '') + (st.mirror ? ' mirrored' : ''); byLayout[k] = (byLayout[k] || 0) + 1; }
const skipWhy = {}; for (const r of skipped) skipWhy[r.why] = (skipWhy[r.why] || 0) + 1;
const hides = stations.reduce((s, st) => s + st.hide.length, 0), hideFuel = stations.reduce((s, st) => s + st.hide.filter(h => h.why === 'fuel outline').length, 0);
console.log(`fuel features in the box: ${feats.length} (${feats.filter(f => f.type === 0).length} nodes, ${feats.filter(f => f.type === 1 && f.rings).length} outlined ways, ${feats.filter(f => f.type === 1 && !f.rings).length} site ways (centre only), ${feats.filter(f => f.type === 2).length} relations); ${mergedFeatures} merged into a station they share -> ${clusterList.length} stations`);
console.log(`stations by class: ${Object.entries(byCls).map(([k, v]) => k + ' ' + v).join(', ')}; skipped ${skipped.length} (${Object.entries(skipWhy).map(([k, v]) => k + ': ' + v).join('; ')})`);
console.log(`layouts placed: ${Object.entries(byLayout).sort((a, b) => b[1] - a[1]).map(([k, v]) => k + ' ' + v).join(', ')}`);
const misfits = stations.filter(isMisfit);
console.log(`misfits (standing over ${DISPLACED_M} m from OSM's own): ${misfits.length}` + misfits.map(s => '\n    ' + s.place + ' (' + s.displaced.toFixed(0) + ' m)').join(''));
console.log(`driveways tight to a junction (${DRIVE_CORNER_MIN_M}-${DRIVE_CORNER_M} m) at ${stations.filter(s => s.tight).length} stations`);
console.log(`corner lots ${stations.filter(s => s.placed && s.corner).length}; steep (>= ${STEEP_M} m) ${stations.filter(s => s.placed && s.relief >= STEEP_M).length}; driveways ${stations.reduce((s, st) => s + st.drives.length, 0)} (stations with none: ${stations.filter(s => s.placed && !s.drives.length).length}); footprints replaced ${hides} (${hideFuel} fuel outlines, ${hides - hideFuel} untagged shops beside the fuel)`);
console.log(`size ${bytes.length} bytes; built in ${((Date.now() - t0) / 1e3).toFixed(1)} s`);
if (list) {
  for (const st of stations) console.log(`  ${st.placed ? 'placed' : 'NO FIT'} ${st.cls.padEnd(9)} ${(st.placed ? LAYOUT_NAME[st.kind] + ' x' + st.n + (st.mirror ? ' m' : '') : st.whyNot).padEnd(28)} (${st.F.ox.toFixed(0)},${st.F.oz.toFixed(0)}) ${st.place}  [${st.features.join(' ')}] road ${st.M.roadGap.toFixed(0)} m${st.M.inLot ? ' in-lot' : ''}${st.M.store ? ' store ' + st.M.store + '@' + st.M.storeGap.toFixed(0) : ''} hide ${st.hide.length} drives ${st.drives.length}${st.tight ? ' tight' : ''}${st.corner ? ' corner' : ''}`);
  for (const r of skipped) console.log(`  SKIP   ${r.why}  (${r.core.x.toFixed(0)},${r.core.z.toFixed(0)}) ${r.place} [${r.features.join(' ')}]`);
}
if (jsonOut) {
  const J = {
    note: 'stations.mjs --json: the station table (world metres, x east, z north). OSM data (c) OpenStreetMap contributors, ODbL 1.0.',
    bbox: BIZ.bbox, uptown: UPTOWN,
    stations: stations.map(st => ({
      x: st.F.ox, z: st.F.oz, yaw: yawOf(st), cls: st.cls, placed: st.placed, whyNot: st.whyNot || null, layout: LAYOUT_NAME[st.kind], modules: st.n, mirror: st.mirror,
      corner: st.corner, relief: +st.relief.toFixed(2), place: st.place, displaced: +(st.displaced || 0).toFixed(1), frontage: nameOf(st.e), frontRank: st.e.rank, osm: st.features,
      lot: rectWorld(st.F, st.L.lot), parts: st.L.parts.map(p => ({ kind: p[0], ring: rectWorld(st.F, p.slice(1)) })),
      drives: st.drives.map(d => ({ mx: d.mx, mz: d.mz, dx: d.dx, dz: d.dz, half: d.half, len: d.len, edge: d.edge, s: d.s, side: d.side })),
      hide: st.hide.map(h => ({ x: h.x, z: h.z, area: Math.round(h.area), why: h.why, ring: fps[h.fi].pts })),
      links: st.links || [],
      core: st.core, onLotsM2: st.onLotsM2 || 0, why: st.why || '', roadGap: +st.M.roadGap.toFixed(1), inLot: st.M.inLot, store: st.M.store, storeGap: +(st.M.storeGap === Infinity ? -1 : st.M.storeGap).toFixed(1), tight: st.tight || 0,
      osmRings: st.features.map(id => feats[fIndex.get(id)]).filter(f => f.rings).flatMap(f => f.rings),
      osmPoints: st.features.map(id => feats[fIndex.get(id)]).filter(f => !f.rings).map(f => [f.x, f.z]),
    })),
    skipped: skipped.map(r => ({ x: r.core.x, z: r.core.z, why: r.why, osm: r.features, roadGap: +r.M.roadGap.toFixed(1), place: r.place })),
  };
  writeFileSync(jsonOut, JSON.stringify(J));
  console.log('wrote ' + jsonOut);
}
const shipped = join(RES, 'charlotte_fuel.bytes');
if (check) {
  const same = existsSync(shipped) && Buffer.compare(readFileSync(shipped), bytes) === 0;
  console.log(same ? 'STATIONS CHECK OK (byte for byte)' : 'STATIONS CHECK: DIFFERS from ' + shipped);
  process.exitCode = same ? 0 : 1;
}
if (outDir) { mkdirSync(outDir, { recursive: true }); writeFileSync(join(outDir, 'charlotte_fuel.bytes'), bytes); console.log('wrote ' + join(outDir, 'charlotte_fuel.bytes')); }
