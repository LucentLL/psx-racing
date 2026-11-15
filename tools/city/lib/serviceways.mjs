// serviceways.mjs - SERVICE WAYS AS PAVEMENT (2026-11-15, the owner: "continue
// correcting roads around the city (including minor roads, driveways,
// neighborhood roads, etc)").
//
// The export keeps a service road only when it has a name (export_osm.mjs:405:
// "the unnamed ones are car-park aisles and loading bays and would clutter
// every block with stubs") and drops private ones (export_osm.mjs:406): 240 km
// of access drives, alleys, drive-throughs and loading yards in the map
// extract are not in the game, so the lots and buildings they serve have no
// way in. They cannot join the ROAD GRAPH (race routes, saved spots and the
// refuge lists are keyed to its nodes and edges), so this step brings them in
// as PAVEMENT ONLY: each way's centre line and width, cut where the data's line
// meets a road's pavement (a MOUTH there: the street edge, the arc position,
// the side, the opening's half width), a building, water or the end of the
// surface, into Resources/charlotte_service.bytes. The game reads it at load
// (Scripts/City/CityMeshes.ServiceWays.cs): the pavement is cut into the
// ground like a parking lot's aisle (no second surface), the mouth is a curb
// cut with the walk carried across. No node or edge is added.
//
//   node tools/city/service_ways.mjs --cache <the extract> [--out <bytes>]
//
// File (little-endian): u32 'SVCW' | i32 version 1 | u32 graph hash | u32 edges
//   | u32 ways  | per way: u32 OSM id; u8 kind; u8 flags; u16 n; f32 x0, z0;
//                 n x { i16 dx, dz (5 cm from x0, z0); u8 half width (5 cm, 7 bits;
//                       the top bit: the whole width lies on a parking lot's asphalt) }
//   | u32 mouths | per mouth: u32 way (index); u8 end (0 first point, 1 last);
//                 u32 edge; f32 s; i8 side (+1 left of a->b); f32 half
// kind: 0 service, 1 alley, 2 driveway, 3 drive-through, 4 parking aisle,
//       5 private street, 6 bus / emergency. flags: 1 one-way, 2 private or
//       customers, 4 unpaved in the data, 8 named, 32 a loose parking aisle of
//       the lots' fetch (outside a kept lot: PSX_CITY_SERVICE_AISLES=0 leaves them out).
import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

export const LON0 = -80.81770185962013, LAT0 = 35.18456015184093;
export const M_LON = 111320 * Math.cos(35.18456 * Math.PI / 180), M_LAT = 111132;
export const toX = lon => (lon - LON0) * M_LON, toZ = lat => (lat - LAT0) * M_LAT;
export const toLat = z => LAT0 + z / M_LAT, toLon = x => LON0 + x / M_LON;
export const KINDS = ['service', 'alley', 'driveway', 'drive-through', 'parking aisle', 'private street', 'bus / emergency'];
export const MAGIC = 0x57435653;   // 'SVCW'
export const VERSION = 1;

// ---- widths (metres), by kind: two-way, one-way. A two-way drive aisle is
// 24 ft, an alley 16 ft (12 ft one way), a driveway and a drive-through lane
// 12 ft, a private street the local street's 24 ft of asphalt.
const WIDTH = [[7.32, 4.57], [4.88, 3.66], [3.66, 3.66], [3.66, 3.66], [7.32, 4.9], [7.32, 4.57], [7.32, 4.57]];
const MIN_HALF = 1.5;          // a 3 m lane is the least a car is given
const BLD_CLEAR = 0.35;        // pavement stays this far off a wall
const MIN_PIECE = 6;           // metres of pavement worth keeping
const CORNER_M = 14, CORNER_MIN = 6;   // a mouth this far from a junction's node (and the relaxed limit)

// ---------------------------------------------------------------- geometry
export function segDist(px, pz, ax, az, bx, bz) {
  const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
  const t = L2 > 1e-12 ? Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / L2)) : 0;
  const qx = ax + t * dx, qz = az + t * dz;
  return { d: Math.hypot(px - qx, pz - qz), t, qx, qz };
}
export function inPoly(ring, x, z) {
  let c = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const [xi, zi] = ring[i], [xj, zj] = ring[j];
    if ((zi > z) !== (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) c = !c;
  }
  return c;
}
function polyEdgeDist(ring, x, z) {
  let best = 1e9;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const d = segDist(x, z, ring[j][0], ring[j][1], ring[i][0], ring[i][1]).d;
    if (d < best) best = d;
  }
  return best;
}
const boxOf = pts => { let x0 = 1e18, z0 = 1e18, x1 = -1e18, z1 = -1e18; for (const p of pts) { if (p[0] < x0) x0 = p[0]; if (p[0] > x1) x1 = p[0]; if (p[1] < z0) z0 = p[1]; if (p[1] > z1) z1 = p[1]; } return [x0, z0, x1, z1]; };

export class Grid {
  constructor(cell = 32) { this.c = cell; this.m = new Map(); }
  k(ix, iz) { return (ix + 20000) * 65536 + (iz + 20000); }
  add(x0, z0, x1, z1, item) {
    const c = this.c;
    for (let ix = Math.floor(x0 / c); ix <= Math.floor(x1 / c); ix++)
      for (let iz = Math.floor(z0 / c); iz <= Math.floor(z1 / c); iz++) {
        const k = this.k(ix, iz); let l = this.m.get(k);
        if (!l) this.m.set(k, l = []);
        l.push(item);
      }
  }
  /// every item whose cells meet the box (an item may come up more than once)
  each(x0, z0, x1, z1, fn) {
    const c = this.c;
    for (let ix = Math.floor(x0 / c); ix <= Math.floor(x1 / c); ix++)
      for (let iz = Math.floor(z0 / c); iz <= Math.floor(z1 / c); iz++) {
        const l = this.m.get(this.k(ix, iz));
        if (l) for (const it of l) fn(it);
      }
  }
}

// ---------------------------------------------------------------- the extract
const loadEls = p => existsSync(p) ? JSON.parse(readFileSync(p, 'utf8')).elements : [];
/// Every highway way of the extract (first file wins an id), with its plan
/// line and length. src: arterial, core, uncc, connector, lots (the parking
/// aisles the lots' fetch took, city-wide).
export function loadExtract(cache, { aisles = true } = {}) {
  const files = [['arterial', 'ways_all.json'], ['core', 'streets_core.json'], ['uncc', 'uncc_ways.json'], ['connector', join('layers', 'connectors.json')]];
  if (aisles) files.push(['lots', 'lots_core.json'], ['lots', 'lots_city.json']);
  const ways = new Map();
  for (const [src, f] of files)
    for (const w of loadEls(join(cache, f))) {
      if (w.type !== 'way' || !w.tags || !w.geometry || !w.tags.highway || ways.has(w.id)) continue;
      const g = w.geometry.map(p => [toX(p.lon), toZ(p.lat)]);
      let len = 0; for (let i = 1; i < g.length; i++) len += Math.hypot(g[i][0] - g[i - 1][0], g[i][1] - g[i - 1][1]);
      ways.set(w.id, { id: w.id, t: w.tags, nodes: w.nodes || [], g, len, src });
    }
  return ways;
}

/// LOTS and LENT of charlotte_city.bytes (lib/citydata.mjs skips them).
export function parseLots(buf, city) {
  const lots = [], entrances = [];
  const sec = city.sections;
  if (sec.LOTS) {
    let p = sec.LOTS[0];
    const u32 = () => { const v = buf.readUInt32LE(p); p += 4; return v; }, f32 = () => { const v = buf.readFloatLE(p); p += 4; return v; };
    const u16 = () => { const v = buf.readUInt16LE(p); p += 2; return v; }, i16 = () => { const v = buf.readInt16LE(p); p += 2; return v; }, u8 = () => buf.readUInt8(p++);
    const raw = u32(); const v2 = (raw & 0x80000000) !== 0; const n = raw & 0x7fffffff;
    for (let i = 0; i < n; i++) {
      const ox = f32(), oz = f32(); const np = u16(); const ring = new Array(np);
      for (let k = 0; k < np; k++) ring[k] = [ox + i16() * 0.05, oz + i16() * 0.05];
      p += 4;
      const nr = u16(); let stalls = 0;
      for (let k = 0; k < nr; k++) { p += 4; stalls += u8(); if (v2) p += 4; }
      let aisleOnly = false; const holes = [];
      if (v2) {
        aisleOnly = u8() !== 0;
        const nh = u16();
        for (let h = 0; h < nh; h++) { const kind = u8(), hp = u16(); const hr = new Array(hp); for (let k = 0; k < hp; k++) hr[k] = [ox + i16() * 0.05, oz + i16() * 0.05]; holes.push({ kind, ring: hr, box: boxOf(hr) }); }
      }
      lots.push({ index: i, ring, box: boxOf(ring), holes, aisleOnly, stalls });
    }
    if (p !== sec.LOTS[1]) throw new Error(`LOTS read ${p - sec.LOTS[0]} of ${sec.LOTS[1] - sec.LOTS[0]} bytes`);
  }
  if (sec.LENT) {
    let p = sec.LENT[0];
    const n = buf.readUInt32LE(p); p += 4;
    for (let i = 0; i < n; i++) { entrances.push({ edge: buf.readUInt32LE(p), s: buf.readFloatLE(p + 4), side: buf.readInt8(p + 8), half: buf.readFloatLE(p + 9) }); p += 13; }
  }
  return { lots, entrances };
}

// ---------------------------------------------------------------- the kinds
const off = v => v !== undefined && v !== 'no' && v !== '0';
export const onewayOf = t => (t.oneway === 'yes' || t.oneway === '1' || t.oneway === 'true' || t.junction === 'roundabout') ? 1 : t.oneway === '-1' ? -1 : 0;
/// A minor way's audit kind (the table's rows).
export function auditKind(t) {
  const hw = t.highway;
  if (hw === 'service') return t.service ? (['parking_aisle', 'driveway', 'alley', 'drive-through'].includes(t.service) ? 'service=' + t.service : 'service=other (' + t.service + ')') : (t.name ? 'service, named' : 'service, plain');
  return hw;
}
/// Not pavement on the ground: null, or why.
export function notSurface(t) {
  if (t.area === 'yes') return 'an area, not a line';
  if (off(t.tunnel)) return t.tunnel === 'building_passage' ? 'through a building (building_passage)' : 'a tunnel';
  if (t.covered === 'yes' || t.indoor === 'yes' || t.location === 'underground') return 'covered or underground';
  if (off(t.bridge)) return 'on a bridge';
  const layer = parseInt(t.layer, 10);
  if (Number.isFinite(layer) && layer !== 0) return 'on another layer (' + t.layer + ')';
  return null;
}
/// The pavement kind of a way this step takes (-1: not taken), and why not.
export function serviceKind(t) {
  const hw = t.highway;
  const priv = t.access === 'private' || t.motor_vehicle === 'private';
  if (t.access === 'no' || t.motor_vehicle === 'no' || t.vehicle === 'no') return { kind: -1, why: 'access=no (not for cars)' };
  if (t.golf || t.golf_cart === 'designated' || t.golf_cart === 'yes' && t.motor_vehicle === 'no') return { kind: -1, why: 'a golf cart path' };
  if (hw === 'service') {
    const s = t.service;
    if (s === 'slipway') return { kind: -1, why: 'a boat ramp' };
    return { kind: s === 'alley' ? 1 : s === 'driveway' ? 2 : s === 'drive-through' ? 3 : s === 'parking_aisle' ? 4 : s === 'bus' || s === 'emergency_access' ? 6 : 0, why: null };
  }
  if ((hw === 'residential' || hw === 'unclassified' || hw === 'living_street') && priv) return { kind: 5, why: null };
  return { kind: -1, why: 'not a service way' };
}
export function widthOf(t, kind) {
  const ow = onewayOf(t) !== 0;
  let w = parseFloat(String(t.width || '').replace(',', '.'));
  if (/ft|'/.test(String(t.width || ''))) w *= 0.3048;
  if (!(w >= 2.4 && w <= 14)) {
    const lanes = parseInt(t.lanes, 10);
    w = Number.isFinite(lanes) && lanes >= 1 && lanes <= 4 ? Math.max(3.66, lanes * 3.35 + 0.6) : WIDTH[kind][ow ? 1 : 0];
  }
  return Math.max(3.0, Math.min(11, w));
}

// ---------------------------------------------------------------- the step
/// city: lib/citydata.mjs parseCity; bld: parseBld; lots: parseLots; ways: loadExtract.
export function buildServiceWays({ city, bld, lots, ways, looseAisles = true, steep = [] }) {
  const edges = city.edges;
  const inGraph = new Set(edges.map(e => e.wayId));
  const stats = { candidates: 0, candidatesKm: 0, inGraph: 0, notSurface: 0, notSurfaceKm: 0, notTaken: 0, notTakenKm: 0, pieces: 0, km: 0, inLotKm: 0, cutBuilding: 0, cutBuildingKm: 0, cutWater: 0, cutWaterKm: 0, cutSteep: 0, cutSteepKm: 0,
                  onRoadKm: 0, shortKm: 0, orphanPieces: 0, orphanKm: 0, mouths: 0, mouthsMerged: 0, mouthFreeway: 0, mouthCorner: 0, mouthCornerMoved: 0, pinchedKm: 0, aislesDrawn: 0, aislesDrawnKm: 0, aislesLoose: 0, aislesLooseKm: 0, points: 0, byKind: KINDS.map(() => ({ ways: 0, km: 0, mouths: 0 })) };
  const left = [];   // { id, why, m, x, z }

  // ---- the roads
  const roadGrid = new Grid(32);
  const clipEdge = e => e.level === 0 && !e.bridge && !e.tunnel;
  edges.forEach((e, ei) => {
    const P = e.pts;
    for (let i = 1; i < P.length; i++) {
      const pad = e.hw + 1;
      roadGrid.add(Math.min(P[i - 1][0], P[i][0]) - pad, Math.min(P[i - 1][1], P[i][1]) - pad, Math.max(P[i - 1][0], P[i][0]) + pad, Math.max(P[i - 1][1], P[i][1]) + pad, ei * 4096 + i);
    }
  });
  /// The pavement a point is on (deepest inside its band): null, or the edge, arc position and side.
  const roadAt = (x, z, pad = 0) => {
    let best = null;
    roadGrid.each(x, z, x, z, packed => {
      const ei = Math.floor(packed / 4096), i = packed % 4096, e = edges[ei];
      if (!clipEdge(e)) return;
      const a = e.pts[i - 1], b = e.pts[i];
      const r = segDist(x, z, a[0], a[1], b[0], b[1]);
      const depth = e.hw + pad - r.d;
      if (depth <= 0 || (best && depth <= best.depth)) return;
      const side = ((b[0] - a[0]) * (z - a[1]) - (b[1] - a[1]) * (x - a[0])) > 0 ? 1 : -1;
      best = { depth, ei, i, t: r.t, d: r.d, side, s: e.s[i - 1] + r.t * (e.s[i] - e.s[i - 1]) };
    });
    return best;
  };
  const edgePoint = (e, s) => {
    s = Math.max(0, Math.min(e.length, s));
    let i = 1; while (i < e.pts.length - 1 && e.s[i] < s) i++;
    const a = e.pts[i - 1], b = e.pts[i], L = e.s[i] - e.s[i - 1] || 1e-9, t = (s - e.s[i - 1]) / L;
    const tx = (b[0] - a[0]) / L, tz = (b[1] - a[1]) / L;
    return { x: a[0] + (b[0] - a[0]) * t, z: a[1] + (b[1] - a[1]) * t, tx, tz };
  };
  const degree = n => city.nodeEdges[n].length;

  // ---- the buildings
  const bldGrid = new Grid(32);
  const fps = bld.footprints;
  fps.forEach((f, fi) => { if (f.hidden || f.pts.length < 3) return; f.box = boxOf(f.pts); bldGrid.add(f.box[0] - 8, f.box[1] - 8, f.box[2] + 8, f.box[3] + 8, fi); });
  /// How far the nearest wall is (0 inside a footprint), up to 8 m.
  const wallAt = (x, z) => {
    let best = 8;
    bldGrid.each(x, z, x, z, fi => {
      const f = fps[fi], b = f.box;
      if (x < b[0] - best || x > b[2] + best || z < b[1] - best || z > b[3] + best) return;
      if (inPoly(f.pts, x, z)) { best = 0; return; }
      const d = polyEdgeDist(f.pts, x, z);
      if (d < best) best = d;
    });
    return best;
  };

  // ---- the water
  const waterGrid = new Grid(64);
  city.waters.forEach((w, wi) => {
    if (w.lake) { const b = boxOf(w.pts); waterGrid.add(b[0] - 3, b[1] - 3, b[2] + 3, b[3] + 3, wi * 65536); return; }
    for (let i = 1; i < w.pts.length; i++) {
      const pad = w.width / 2 + 3;
      waterGrid.add(Math.min(w.pts[i - 1][0], w.pts[i][0]) - pad, Math.min(w.pts[i - 1][1], w.pts[i][1]) - pad, Math.max(w.pts[i - 1][0], w.pts[i][0]) + pad, Math.max(w.pts[i - 1][1], w.pts[i][1]) + pad, wi * 65536 + i);
    }
  });
  const waterAt = (x, z) => {
    let hit = false;
    waterGrid.each(x, z, x, z, packed => {
      if (hit) return;
      const w = city.waters[Math.floor(packed / 65536)], i = packed % 65536;
      if (i === 0) { if (inPoly(w.pts, x, z) || polyEdgeDist(w.pts, x, z) < 2) hit = true; return; }
      if (segDist(x, z, w.pts[i - 1][0], w.pts[i - 1][1], w.pts[i][0], w.pts[i][1]).d < w.width / 2 + 2.5) hit = true;
    });
    return hit;
  };

  // ---- the lots
  const lotGrid = new Grid(64);
  lots.lots.forEach((L, li) => lotGrid.add(L.box[0], L.box[1], L.box[2], L.box[3], li));
  /// The lot whose pavement (or island: the game takes it out) the point is on, -1 none.
  const lotAt = (x, z) => {
    let hit = -1;
    lotGrid.each(x, z, x, z, li => {
      if (hit >= 0) return;
      const L = lots.lots[li], b = L.box;
      if (x < b[0] || x > b[2] || z < b[1] || z > b[3] || !inPoly(L.ring, x, z)) return;
      for (const h of L.holes) if (h.kind === 0 && x >= h.box[0] && x <= h.box[2] && z >= h.box[1] && z <= h.box[3] && inPoly(h.ring, x, z)) return;
      hit = li;
    });
    return hit;
  };

  // ---- the steep places (the game's own ground under the ways, measured by the editor's
  // CityAudit.ServiceOnly into tools/city/service_steep.tsv: steeper than 20% along or 25% across -
  // a road's bank, a cut): a way stops short of them
  const steepGrid = new Grid(16);
  for (const q of steep) steepGrid.add(q[0] - 2.5, q[1] - 2.5, q[0] + 2.5, q[1] + 2.5, q);
  const steepAt = (x, z) => {
    let hit = false;
    steepGrid.each(x, z, x, z, q => { if (!hit && Math.hypot(q[0] - x, q[1] - z) < 2.5) hit = true; });
    return hit;
  };

  // ---- every candidate way, in id order
  const pieces = [];
  // (the parking aisles of the lots' fetch too: outside the core the export keeps an aisle only inside a
  // lot of 2000 m2 or more - export_osm.mjs:1722 - so the drives of every smaller lot and of every
  // apartment complex mapped as aisles are on disk and not in the game)
  const list = [...ways.values()].filter(w => w.src !== 'arterial' && (looseAisles || w.src !== 'lots')).sort((a, b) => a.id - b.id);
  for (const w of list) {
    const sk = serviceKind(w.t);
    if (sk.kind < 0) { if (w.t.highway === 'service') { stats.notTaken++; stats.notTakenKm += w.len / 1000; left.push({ id: w.id, why: sk.why, m: w.len, x: w.g[0][0], z: w.g[0][1] }); } continue; }
    if (inGraph.has(w.id)) { stats.inGraph++; continue; }
    stats.candidates++; stats.candidatesKm += w.len / 1000;
    const ns = notSurface(w.t);
    if (ns) { stats.notSurface++; stats.notSurfaceKm += w.len / 1000; left.push({ id: w.id, why: ns, m: w.len, x: w.g[0][0], z: w.g[0][1] }); continue; }
    const kind = sk.kind;
    const ow = onewayOf(w.t);
    const W = widthOf(w.t, kind);
    const flags = (ow !== 0 ? 1 : 0) | (w.t.access === 'private' || w.t.access === 'customers' || w.t.motor_vehicle === 'private' ? 2 : 0) |
                  (/^(gravel|unpaved|fine_gravel|dirt|ground|compacted|grass)$/.test(w.t.surface || '') ? 4 : 0) | (w.t.name ? 8 : 0) | (w.src === 'lots' ? 32 : 0);
    // its line: points under 1.2 m apart merged (the ends kept), in travel order
    let g = w.g, nodes = w.nodes;
    if (ow === -1) { g = g.slice().reverse(); nodes = nodes.slice().reverse(); }
    const P = [], N = [];
    for (let i = 0; i < g.length; i++) {
      if (P.length && i < g.length - 1 && Math.hypot(g[i][0] - P[P.length - 1][0], g[i][1] - P[P.length - 1][1]) < 1.2) continue;
      if (P.length && i === g.length - 1 && Math.hypot(g[i][0] - P[P.length - 1][0], g[i][1] - P[P.length - 1][1]) < 1.2 && P.length > 1) { P.pop(); N.pop(); }
      P.push(g[i]); N.push(nodes[i]);
    }
    if (P.length < 2) { left.push({ id: w.id, why: 'under 1.2 m long', m: w.len, x: g[0][0], z: g[0][1] }); continue; }
    // stations every metre: 0 clear, 1 a road's pavement, 2 a building, 3 water, 4 a bank
    const S = [];   // { x, z, at (arc), seg, state, half, road, lot }
    let arc = 0;
    for (let i = 1; i < P.length; i++) {
      const L = Math.hypot(P[i][0] - P[i - 1][0], P[i][1] - P[i - 1][1]), k = Math.max(1, Math.round(L));
      for (let j = (i === 1 ? 0 : 1); j <= k; j++) {
        const x = P[i - 1][0] + (P[i][0] - P[i - 1][0]) * j / k, z = P[i - 1][1] + (P[i][1] - P[i - 1][1]) * j / k;
        const road = roadAt(x, z);
        const wall = road ? 8 : wallAt(x, z);
        const half = Math.min(W / 2, wall - BLD_CLEAR);
        const state = road ? 1 : half < MIN_HALF ? 2 : waterAt(x, z) ? 3 : steep.length && steepAt(x, z) ? 4 : 0;
        const lot = state === 0 ? lotAt(x, z) : -1;
        let full = false;
        if (lot >= 0) {
            const h = Math.max(MIN_HALF, half) + 0.25, px = -(P[i][1] - P[i - 1][1]) / (L || 1), pz = (P[i][0] - P[i - 1][0]) / (L || 1);
            full = lotAt(x + px * h, z + pz * h) >= 0 && lotAt(x - px * h, z - pz * h) >= 0;
        }
        S.push({ x, z, at: arc + L * j / k, seg: i, vtx: j === k ? i : j === 0 ? i - 1 : -1, state, half: Math.max(MIN_HALF, half), road, lot, full });
      }
      arc += L;
    }
    // an aisle of the lots' fetch that the lots already pave (80 % of it off the roads) is theirs
    if (w.src === 'lots') {
      let clear = 0, on = 0;
      for (const st of S) if (st.state !== 1) { clear++; if (st.lot >= 0) on++; }
      if (clear === 0 || on >= 0.8 * clear) { stats.aislesDrawn++; stats.aislesDrawnKm += w.len / 1000; stats.candidates--; stats.candidatesKm -= w.len / 1000; continue; }
      stats.aislesLoose++; stats.aislesLooseKm += w.len / 1000;
    }
    // runs of clear stations
    for (let a = 0; a < S.length;) {
      if (S[a].state !== 0) {
        let b = a; while (b < S.length && S[b].state === S[a].state) b++;
        const m = S[Math.min(b, S.length - 1)].at - S[a].at;
        if (S[a].state === 1) stats.onRoadKm += m / 1000;
        else if (S[a].state === 2) { stats.cutBuilding++; stats.cutBuildingKm += m / 1000; left.push({ id: w.id, why: 'the line meets a building: stopped short', m, x: S[a].x, z: S[a].z }); }
        else if (S[a].state === 3) { stats.cutWater++; stats.cutWaterKm += m / 1000; left.push({ id: w.id, why: 'the line crosses water (no culvert in the data): stopped short', m, x: S[a].x, z: S[a].z }); }
        else { stats.cutSteep++; stats.cutSteepKm += m / 1000; left.push({ id: w.id, why: 'the land is a bank there (over 20% along or 25% across the way): stopped short', m, x: S[a].x, z: S[a].z }); }
        a = b; continue;
      }
      let b = a; while (b + 1 < S.length && S[b + 1].state === 0) b++;
      const m = S[b].at - S[a].at;
      const before = a > 0 ? S[a - 1] : null, after = b + 1 < S.length ? S[b + 1] : null;
      if (m < MIN_PIECE && !(before && before.state === 1 && after && after.state === 1)) {
        if (m > 0.5) { stats.shortKm += m / 1000; left.push({ id: w.id, why: `a piece under ${MIN_PIECE} m between cuts`, m, x: S[a].x, z: S[a].z }); }
        a = b + 1; continue;
      }
      // the piece's points: the way's own vertices and where the width steps
      const pts = [];
      let lastHalf = -1, lastOn = null;
      const halfAt = i => { let h = 1e9; for (let k = Math.max(a, i - 2); k <= Math.min(b, i + 2); k++) h = Math.min(h, S[k].half); return h; };
      let inLot = 0, pinched = 0;
      const nodeIds = [];
      for (let i = a; i <= b; i++) {
        const h = halfAt(i);
        if (i > a) { if (S[i].lot >= 0 && S[i - 1].lot >= 0) inLot += S[i].at - S[i - 1].at; if (h < W / 2 - 0.05) pinched += S[i].at - S[i - 1].at; }
        if (S[i].vtx >= 0 && N[S[i].vtx] !== undefined) nodeIds.push(N[S[i].vtx]);
        // (on a lot: this station and its neighbours are, so a segment between two such points is all on the lot)
        const onLot = S[i].full && (i === a || S[i - 1].full) && (i === b || S[i + 1].full);
        if (i === a || i === b || S[i].vtx >= 0 || Math.abs(h - lastHalf) > 0.3 || onLot !== lastOn) { pts.push([S[i].x, S[i].z, h, onLot]); lastHalf = h; lastOn = onLot; }
      }
      const piece = { id: w.id, kind, flags, W, pts, len: m, inLot, pinched, ends: [null, null], nodeIds, lotsOn: new Set(), name: w.t.name || '' };
      for (let i = a; i <= b; i++) if (S[i].lot >= 0) piece.lotsOn.add(S[i].lot);
      if (before && before.state === 1) piece.ends[0] = { road: before.road, at: S[a] };
      if (after && after.state === 1) piece.ends[1] = { road: after.road, at: S[b] };
      // an end at a node a graph way owns but outside its band (a narrow road's edge): still a mouth
      for (const [end, st] of [[0, S[a]], [1, S[b]]]) {
        if (piece.ends[end] || (end === 0 ? a > 0 : b < S.length - 1)) continue;
        const r = roadAt(st.x, st.z, 2.5);
        if (r) piece.ends[end] = { road: r, at: st };
      }
      pieces.push(piece);
      a = b + 1;
    }
  }

  // ---- the mouths
  const mouths = [];
  const mouthOk = e => !(e.rank >= 5 || (e.link && e.rank >= 4));
  for (const pc of pieces) {
    for (let end = 0; end < 2; end++) {
      const E = pc.ends[end];
      if (!E) continue;
      pc.ends[end] = null;
      const e = edges[E.road.ei];
      const pts = pc.pts;
      const tip = end === 0 ? pts[0] : pts[pts.length - 1];
      if (!mouthOk(e)) {
        stats.mouthFreeway++; left.push({ id: pc.id, why: 'meets a freeway or a ramp: no mouth', m: 0, x: tip[0], z: tip[1] });
        continue;
      }
      // the arc position, a junction's corner kept clear
      let s = E.road.s; const side = E.road.side;
      const dA = degree(e.a) >= 3 ? s : 1e9, dB = degree(e.b) >= 3 ? e.length - s : 1e9;
      let moved = 0;
      if (Math.min(dA, dB) < CORNER_M) {
        const lim = e.length >= 2 * CORNER_M + 4 ? CORNER_M : CORNER_MIN;
        const s2 = dA < dB ? Math.min(lim, e.length / 2) : Math.max(e.length - lim, e.length / 2);
        if (e.length < 2 * CORNER_MIN + 2 || Math.abs(s2 - s) > 16) {
          stats.mouthCorner++; left.push({ id: pc.id, why: 'meets the street inside a junction (a fourth arm): no mouth without a graph node', m: 0, x: tip[0], z: tip[1] });
          continue;
        }
        if (Math.abs(s2 - s) > 0.5) { moved = Math.abs(s2 - s); s = s2; stats.mouthCornerMoved++; }
      }
      const q = edgePoint(e, s);
      const nx = -q.tz * side, nz = q.tx * side;   // out of the road, towards the way
      const edgeX = q.x + nx * e.hw, edgeZ = q.z + nz * e.hw;
      // the piece's end: drop its points within reach of the old tip when the mouth moved, then the
      // pavement's edge and a point inside the band (the tile cuts the paving along the pavement as drawn)
      const drop = moved > 0 ? moved + 6 : 0.8;
      const keep = p => Math.hypot(p[0] - edgeX, p[1] - edgeZ) > drop && ((p[0] - edgeX) * nx + (p[1] - edgeZ) * nz) > 0.5;
      let body = end === 0 ? pts.slice() : pts.slice().reverse();
      const h0 = body[0][2];
      while (body.length > 1 && !keep(body[0])) body.shift();
      if (body.length === 1 && !keep(body[0])) body = [[edgeX + nx * 6, edgeZ + nz * 6, h0, false]];
      // (2.5 m inside the profile's edge: a road drawn narrower than its profile is still met)
      const inD = Math.max(0.5, e.hw - 2.5);
      const inX = q.x + nx * inD, inZ = q.z + nz * inD;
      body.unshift([inX, inZ, h0, false], [edgeX, edgeZ, h0, false]);
      pc.pts = end === 0 ? body : body.reverse();
      // the opening along the street: the way's half width over the skew's cosine
      const nb = body[2] || body[1];
      const ox = nb[0] - edgeX, oz = nb[1] - edgeZ, ol = Math.hypot(ox, oz) || 1;
      const cos = Math.max(0.45, (ox * nx + oz * nz) / ol);
      const half = Math.min(6, h0 / cos + 0.4);
      mouths.push({ piece: pc, end, edge: E.road.ei, s, side, half, x: edgeX, z: edgeZ });
      pc.ends[end] = mouths[mouths.length - 1];
    }
  }
  // two of this step's mouths on one side of one edge within reach of each other: one opening
  mouths.sort((a, b) => a.edge - b.edge || a.side - b.side || a.s - b.s);
  for (let i = 1; i < mouths.length; i++) {
    const a = mouths[i - 1], b = mouths[i];
    if (a.dead || a.edge !== b.edge || a.side !== b.side || b.s - a.s > a.half + b.half) continue;
    const lo = Math.min(a.s - a.half, b.s - b.half), hi = Math.max(a.s + a.half, b.s + b.half);
    if (hi - lo > 13) continue;
    b.s = (lo + hi) / 2; b.half = (hi - lo) / 2; b.also = (a.also || []).concat([a]); a.dead = true; stats.mouthsMerged++;
  }

  // ---- what joins what: pieces that share an OSM node, and the lots they run on
  const parent = pieces.map((_, i) => i);
  const find = i => { while (parent[i] !== i) i = parent[i] = parent[parent[i]]; return i; };
  const byNode = new Map(), byLot = new Map();
  pieces.forEach((pc, i) => {
    for (const n of pc.nodeIds) { if (byNode.has(n)) parent[find(i)] = find(byNode.get(n)); else byNode.set(n, i); }
    for (const li of pc.lotsOn) { if (byLot.has(li)) parent[find(i)] = find(byLot.get(li)); else byLot.set(li, i); }
  });
  const comp = new Map();
  pieces.forEach((pc, i) => {
    const r = find(i); let c = comp.get(r);
    if (!c) comp.set(r, c = { pieces: [], mouths: 0, lots: new Set(), km: 0 });
    c.pieces.push(pc); c.km += pc.len / 1000; pc.comp = c;
    if (pc.ends[0]) c.mouths++; if (pc.ends[1]) c.mouths++;
    for (const li of pc.lotsOn) c.lots.add(li);
  });
  // a group with no mouth and on no lot is pavement nothing reaches: left out
  const kept = [];
  for (const pc of pieces) {
    if (pc.comp.mouths === 0 && pc.comp.lots.size === 0) { stats.orphanPieces++; stats.orphanKm += pc.len / 1000; left.push({ id: pc.id, why: 'joins no street and no lot in the data (its street is a way the extract lacks)', m: pc.len, x: pc.pts[0][0], z: pc.pts[0][1] }); continue; }
    kept.push(pc);
  }
  kept.forEach((pc, i) => { pc.index = i; });
  const outMouths = mouths.filter(m => !m.dead && m.piece.index !== undefined);
  for (const pc of kept) {
    stats.pieces++; stats.km += pc.len / 1000; stats.inLotKm += pc.inLot / 1000; stats.pinchedKm += pc.pinched / 1000;
    const k = stats.byKind[pc.kind]; k.ways++; k.km += pc.len / 1000; stats.points += pc.pts.length;
  }
  for (const m of outMouths) { stats.mouths++; stats.byKind[m.piece.kind].mouths++; }
  return { pieces: kept, mouths: outMouths, comps: [...comp.values()], left, stats, roadAt, lotAt, wallAt, edgePoint };
}

// ---------------------------------------------------------------- the file
export function serviceBytes(result, city) {
  const { pieces, mouths } = result;
  let size = 16 + 4 + 4;
  for (const pc of pieces) size += 4 + 1 + 1 + 2 + 8 + pc.pts.length * 5;
  size += mouths.length * 18;
  const b = Buffer.alloc(size);
  let p = 0;
  const u32 = v => { b.writeUInt32LE(v >>> 0, p); p += 4; }, f32 = v => { b.writeFloatLE(v, p); p += 4; };
  u32(MAGIC); b.writeInt32LE(VERSION, p); p += 4; u32(city.graphHash); u32(city.edges.length);
  u32(pieces.length);
  for (const pc of pieces) {
    u32(pc.id); b.writeUInt8(pc.kind, p++); b.writeUInt8(pc.flags, p++);
    if (pc.pts.length > 65535) throw new Error('a service way with over 65535 points');
    b.writeUInt16LE(pc.pts.length, p); p += 2;
    const ox = Math.fround(pc.pts[0][0]), oz = Math.fround(pc.pts[0][1]);
    f32(ox); f32(oz);
    for (const q of pc.pts) {
      const dx = Math.round((q[0] - ox) / 0.05), dz = Math.round((q[1] - oz) / 0.05);
      if (Math.abs(dx) > 32767 || Math.abs(dz) > 32767) throw new Error(`service way ${pc.id}: a point over 1.6 km from its first`);
      b.writeInt16LE(dx, p); b.writeInt16LE(dz, p + 2); b.writeUInt8(Math.max(1, Math.min(127, Math.round(q[2] / 0.05))) | (q[3] ? 0x80 : 0), p + 4); p += 5;
    }
  }
  u32(mouths.length);
  for (const m of mouths) { u32(m.piece.index); b.writeUInt8(m.end, p++); u32(m.edge); f32(m.s); b.writeInt8(m.side, p++); f32(m.half); }
  if (p !== size) throw new Error(`service bytes: wrote ${p} of ${size}`);
  return b;
}
