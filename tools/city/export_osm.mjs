// export_osm.mjs — bake Charlotte for Unity straight from OpenStreetMap.
//
// Replaces export_charlotte.mjs (2026-08-25), which read Racing-Game-2's
// WHOLE-ROAD rows: dual carriageways merged into one painted ribbon, real lane
// counts collapsed into a class index, junctions guessed from geometry, bridge
// extents inferred from "higher than the terrain". This reads the raw ways:
//
//   * REAL TOPOLOGY. A junction is a node two ways share, by id. Nothing is
//     inferred from crossings any more; a crossing with no shared node IS a
//     grade separation, and OSM says which road is on top (layer / bridge /
//     tunnel).
//   * REAL CARRIAGEWAYS. A divided road is two one-way edges 10-30 m apart
//     with the ground showing between them, each with its own `lanes` and its
//     own asymmetric shoulders (a freeway's is 3 m on the right, 1.2 m on the
//     left, in the direction of travel).
//   * REAL BRIDGES. `bridge=yes` marks the exact extent of every deck; the
//     Unity solver holds those stations on structure and lifts them clear.
//   * REAL RAMPS. Every *_link way, joined to its mainline at the node OSM
//     joins it at, with the merge gore drawn by the tile builder.
//   * REAL GROUND. A 60 m SRTM height grid over the whole beltway (min-filtered
//     so uptown's roofs do not read as hills), instead of value noise.
//   * REAL BUILDINGS in the core: 35k footprints with heights, so the skyline
//     is Charlotte's and the neighbourhoods are the neighbourhoods.
//   * THE THREE RACE ROUTES (Uptown Loop, Tryon Sprint, Independence Sprint)
//     as edge sequences through this same graph, so a race and free roam are
//     the same streets — the CLT stage bakes are retired.
//
// Sources (read-only; all © OpenStreetMap contributors, ODbL — the
// attribution string below is shown in-game):
//   tools/city/cache/ways_all.json      arterials + ramps, beltway bbox
//   tools/city/cache/nodes_all.json     signal / stop / yield nodes
//   tools/city/cache/streets_core.json  residential/unclassified in the core
//   tools/city/cache/buildings_core.json footprints in the core
//   tools/roads/cache/N35W081.hgt.gz    SRTM 1-arcsecond (via lib.mjs)
//   RG2/src/config/world/baselineWater.ts  the traced creeks + Lake Wylie
//   RG2/src/config/world/baselineRoads.ts  legacy I-485, ONLY to register water
//
// Outputs:
//   Assets/PSXRacing/Resources/charlotte_city.bytes    graph + water + routes
//   Assets/PSXRacing/Resources/charlotte_dem.bytes     height grid
//   Assets/PSXRacing/Resources/charlotte_bld.bytes     footprints
//   Assets/PSXRacing/Resources/charlotte_routes.json   the menu's copy of the routes
//   tools/city/charlotte_*.png                          debug plots
//
// Run:  node tools/city/export_osm.mjs
//
// Frame: plain equirectangular about RG2's fixture centre (the I-485 centroid),
// x east, z north, metres — the same frame every previous bake registered
// into, so nothing that stored a city coordinate moves.

import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { srtmSampler } from '../roads/lib.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const CACHE = join(HERE, 'cache');
const RG2 = 'C:/Users/mcgee/code/Racing-Game-2';
const RES = join(UNITY, 'Assets', 'PSXRacing', 'Resources');
const SRTM_CACHE = join(HERE, '..', 'roads', 'cache');

// --------------------------------------------------------------- constants
const LANE_M = 3.6576;                 // 12 ft, the section currency (never scaled)
const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;   // RG2 fixture centre
const M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const MPT = 17.212235294117647;        // RG2 tile size, for the water fit only
const CENTER = 1250;
const XDEDUP_M = 8;                    // two crossings of one pair closer than this are one
const BANK_M = 6;                      // dry bank either side of water under a deck
const DEM_CELL = 60;                   // metres per height sample
const DEM_MARGIN = 1500;
const ATTRIBUTION = 'Road network data (c) OpenStreetMap contributors, ODbL 1.0';

const toX = lon => (lon - LON0) * M_LON;
const toZ = lat => (lat - LAT0) * M_LAT;
const toLon = x => LON0 + x / M_LON;
const toLat = z => LAT0 + z / M_LAT;

const CLS_RANK = { motorway: 5, trunk: 4, primary: 3, secondary: 2, tertiary: 1,
                   residential: 0, unclassified: 0, living_street: 0, service: 0 };

/// Paved shoulder either side, metres, [left, right] IN THE DIRECTION OF
/// TRAVEL for a one-way edge; a two-way road gets the same both sides. A
/// freeway's outside shoulder is a full 10 ft and its inside one a 4 ft
/// strip — those are the numbers a driver reads the road width from.
function shoulders(base, link, oneway) {
  if (base === 'motorway') return link ? [0.6, 1.8] : [1.2, 3.0];
  if (base === 'trunk') return link ? [0.6, 1.2] : oneway ? [0.9, 2.4] : [1.5, 1.5];
  if (base === 'primary') return [0.5, 0.5];
  if (base === 'secondary' || base === 'tertiary') return [0.3, 0.3];
  return [0.25, 0.25];
}

/// Lane count when the mapper left the tag off. Charlotte's arterials are
/// 82% tagged; these cover the rest without inventing a six-lane street.
function defaultLanes(base, link, oneway) {
  if (link) return base === 'motorway' && oneway ? 1 : 1;
  switch (base) {
    case 'motorway': return 3;
    case 'trunk': return oneway ? 2 : 4;
    case 'primary': return oneway ? 2 : 4;
    case 'secondary': return oneway ? 2 : 2;
    case 'tertiary': return oneway ? 1 : 2;
    default: return 2;
  }
}

function parseSpeedKmh(t) {
  const m = /([\d.]+)\s*(mph)?/.exec(t.maxspeed || '');
  if (!m) return 0;
  const v = parseFloat(m[1]);
  return Math.round(m[2] || !t.maxspeed.includes('km') ? v * 1.609344 : v);
}

function onewayOf(t) {
  if (t.oneway === '-1' || t.oneway === 'reverse') return -1;
  if (t.oneway === 'yes' || t.oneway === '1' || t.oneway === 'true') return 1;
  if (t.oneway === 'no') return 0;
  if (t.junction === 'roundabout' || t.junction === 'circular' || t.highway === 'motorway') return 1;
  return 0;
}

// ------------------------------------------------------------------ load
function loadJson(p) { return JSON.parse(readFileSync(p, 'utf8')); }
const wayFile = existsSync(join(CACHE, 'ways_all.json')) ? join(CACHE, 'ways_all.json')
  : `${RG2}/fixtures/osm/raw/charlotte_ways.json`;
const nodeFile = existsSync(join(CACHE, 'nodes_all.json')) ? join(CACHE, 'nodes_all.json')
  : `${RG2}/fixtures/osm/raw/charlotte_nodes.json`;
console.log('ways from', wayFile);
const rawWays = loadJson(wayFile).elements.filter(e => e.type === 'way' && e.tags && e.geometry);
const rawNodes = loadJson(nodeFile).elements.filter(e => e.type === 'node');
const minorFile = join(CACHE, 'streets_core.json');
const rawMinor = existsSync(minorFile)
  ? loadJson(minorFile).elements.filter(e => e.type === 'way' && e.tags && e.geometry) : [];
const bldFile = join(CACHE, 'buildings_core.json');
const rawBld = existsSync(bldFile) ? loadJson(bldFile).elements : [];
console.log(`raw: ${rawWays.length} arterial ways, ${rawMinor.length} minor ways, ${rawNodes.length} control nodes, ${rawBld.length} building elements`);

// ------------------------------------------------------------- ways -> edges
const ways = [];
const seenWay = new Set();
let tollDropped = 0;
const tollNodes = new Set();   // every node a dropped toll way touched
function keepWay(w, minor) {
  if (seenWay.has(w.id)) return;
  const t = w.tags;
  const hw = t.highway || '';
  if (t.area === 'yes') return;
  const link = hw.endsWith('_link');
  const base = link ? hw.slice(0, -5) : hw;
  if (!(base in CLS_RANK)) return;
  // NO TOLL ROADS. Every toll=yes way in this snapshot is post-2015: the
  // I-77 Express Lanes (2019), the I-485 Express Lanes (2019) and the Monroe
  // Expressway (2018), plus their slip ramps. The game is set in 1999, and
  // the express lanes were also the "zigzag" on I-77: OSM maps them as a
  // second carriageway 5.5 m from the general lanes, so both ribbons were
  // squeezed against each other and the paint slalomed wherever the two
  // mapped lines drifted apart or together.
  if (t.toll === 'yes') { tollDropped++; for (const id of w.nodes) tollNodes.add(id); return; }
  if (minor) {
    // Named service roads are streets (a shopping-centre ring road); the
    // unnamed ones are car-park aisles and loading bays and would clutter
    // every block with stubs.
    if (base === 'service' && !t.name) return;
    if (t.access === 'private' || t.access === 'no') return;
  }
  seenWay.add(w.id);
  let ow = onewayOf(t);
  let nodes = w.nodes.slice();
  let geom = w.geometry.map(g => [toX(g.lon), toZ(g.lat)]);
  if (ow === -1) { nodes.reverse(); geom.reverse(); ow = 1; }
  const oneway = ow === 1;
  let lanes = parseInt(t.lanes, 10);
  const lf = parseInt(t['lanes:forward'], 10), lb = parseInt(t['lanes:backward'], 10);
  let turn = false;
  if (!oneway && Number.isFinite(lf) && Number.isFinite(lb)) {
    const both = parseInt(t['lanes:both_ways'], 10);
    turn = Number.isFinite(both) && both > 0;
    if (!Number.isFinite(lanes)) lanes = lf + lb + (turn ? 1 : 0);
  }
  if (!Number.isFinite(lanes) || lanes < 1) lanes = defaultLanes(base, link, oneway);
  lanes = Math.min(lanes, oneway ? 6 : 7);
  // A two-way road with an odd lane count is two lanes a side and a centre
  // turn lane — the TWLTL on every Charlotte arterial that was never divided.
  if (!oneway && lanes >= 3 && lanes % 2 === 1) turn = true;
  const [shl, shr] = shoulders(base, link, oneway);
  const layer = parseInt(t.layer, 10);
  const bridge = !!t.bridge && t.bridge !== 'no';
  const tunnel = !!t.tunnel && t.tunnel !== 'no' && t.tunnel !== 'building_passage' || t.covered === 'yes';
  const level = tunnel ? (Number.isFinite(layer) ? Math.min(layer, -1) : -1)
              : Number.isFinite(layer) ? layer : bridge ? 1 : 0;
  // A freeway is called by its NUMBER. OSM names I-485 "Governor James G
  // Martin Freeway" for one stretch and "Craig Lawing Freeway" for the next;
  // no Charlotte driver has ever said either, and the HUD, the audit and
  // the preview all look for "I-485". Ramps carry no name at all: the HUD
  // says RAMP.
  let name = t.name || '';
  const refFirst = t.ref ? t.ref.split(';')[0].trim().replace(/^I (\d)/, 'I-$1') : '';
  if (!name || (base === 'motorway' && !link && /^I-\d/.test(refFirst))) name = refFirst || name;
  if (link) name = '';
  ways.push({
    id: w.id, base, link, rank: CLS_RANK[base], oneway, lanes, turn, shl, shr,
    bridge, tunnel, level, name, speed: parseSpeedKmh(t),
    roundabout: t.junction === 'roundabout' || t.junction === 'circular',
    nodes, geom,
  });
}
for (const w of rawWays) keepWay(w, false);
for (const w of rawMinor) keepWay(w, true);
console.log(`kept ${ways.length} ways (dropped ${tollDropped} toll ways: the 2018-19 express lanes)`);

// A slip ramp that only ever led to a dropped toll lane now ends in mid-air:
// an untolled link whose free end is a node only the toll lanes shared. Drop
// those too, and keep dropping until no ramp leads nowhere (a two-piece ramp
// loses its far piece first, then its near one).
{
  let stubs = 0;
  for (;;) {
    const use = new Map();
    for (const w of ways) for (const id of w.nodes) use.set(id, (use.get(id) || 0) + 1);
    let dropped = false;
    for (let i = ways.length - 1; i >= 0; i--) {
      const w = ways[i];
      if (!w.link) continue;
      const first = w.nodes[0], last = w.nodes[w.nodes.length - 1];
      const stubAt = id => use.get(id) === 1 && tollNodes.has(id);
      if (!stubAt(first) && !stubAt(last)) continue;
      for (const id of w.nodes) tollNodes.add(id);
      ways.splice(i, 1);
      stubs++; dropped = true;
    }
    if (!dropped) break;
  }
  console.log(`dropped ${stubs} ramp pieces that led only to the toll lanes`);
}

// junction nodes: any OSM node two kept ways share, plus every way's ends
const nodeUse = new Map();
for (const w of ways) for (const id of w.nodes) nodeUse.set(id, (nodeUse.get(id) || 0) + 1);
// a way that visits the same node twice (a loop ramp closing on itself)
// counts as two uses, which is what we want: the loop closes at a junction.

const nodeIndex = new Map();   // osm id -> graph node index
const nodes = [];              // [x, z]
function nodeFor(id, xz) {
  let n = nodeIndex.get(id);
  if (n === undefined) { n = nodes.length; nodes.push([xz[0], xz[1]]); nodeIndex.set(id, n); }
  return n;
}

const edges = [];
const edgeDedup = new Set();
for (const w of ways) {
  let start = 0;
  for (let i = 1; i < w.nodes.length; i++) {
    const last = i === w.nodes.length - 1;
    if (!last && nodeUse.get(w.nodes[i]) < 2) continue;
    // cut [start..i]
    const pts = [];
    for (let k = start; k <= i; k++) {
      const p = w.geom[k];
      if (pts.length && Math.hypot(p[0] - pts[pts.length - 1][0], p[1] - pts[pts.length - 1][1]) < 0.05) continue;
      pts.push([p[0], p[1]]);
    }
    if (pts.length >= 2) {
      const a = nodeFor(w.nodes[start], w.geom[start]);
      const b = nodeFor(w.nodes[i], w.geom[i]);
      let L = 0;
      for (let k = 1; k < pts.length; k++) L += Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]);
      const mid = pts[pts.length >> 1];
      const key = `${Math.min(a, b)}:${Math.max(a, b)}:${pts.length}:${mid[0].toFixed(0)}:${mid[1].toFixed(0)}`;
      if (!edgeDedup.has(key) && L > 0.2) {
        edgeDedup.add(key);
        edges.push({ id: edges.length, way: w, a, b, pts, len: L, seq: edges.length });
      }
    }
    start = i;
  }
}
console.log(`graph: ${edges.length} edges, ${nodes.length} nodes`);

// The minor streets came from a different query than the arterials and
// could in principle meet them at nodes the arterial snapshot no longer
// has. Count dead ends that sit ON another edge and weld them.
const nodeDeg = new Array(nodes.length).fill(0);
for (const e of edges) { nodeDeg[e.a]++; nodeDeg[e.b]++; }

// ------------------------------------------------------------ spatial hash
const SEGCELL = 48;
const segHash = new Map();
const cellKey = (cx, cz) => cx * 100003 + cz;
function hashSegs() {
  segHash.clear();
  for (const e of edges) {
    for (let i = 1; i < e.pts.length; i++) {
      const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
      const x0 = Math.floor(Math.min(ax, bx) / SEGCELL), x1 = Math.floor(Math.max(ax, bx) / SEGCELL);
      const z0 = Math.floor(Math.min(az, bz) / SEGCELL), z1 = Math.floor(Math.max(az, bz) / SEGCELL);
      for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) {
        const k = cellKey(cx, cz);
        let l = segHash.get(k);
        if (!l) segHash.set(k, l = []);
        l.push(e.id * 4096 + i);
      }
    }
  }
}
hashSegs();

function nearestOnEdge(e, x, z) {
  let best = Infinity, bs = 0, bx = 0, bz = 0, bi = 0, bt = 0;
  let acc = 0;
  for (let i = 1; i < e.pts.length; i++) {
    const [ax, az] = e.pts[i - 1], [cx, cz] = e.pts[i];
    const dx = cx - ax, dz = cz - az, L2 = dx * dx + dz * dz;
    let t = L2 > 0 ? ((x - ax) * dx + (z - az) * dz) / L2 : 0;
    t = Math.max(0, Math.min(1, t));
    const px = ax + dx * t, pz = az + dz * t;
    const d = (x - px) ** 2 + (z - pz) ** 2;
    if (d < best) { best = d; bs = acc + Math.sqrt(L2) * t; bx = px; bz = pz; bi = i; bt = t; }
    acc += Math.sqrt(L2);
  }
  return { d: Math.sqrt(best), s: bs, x: bx, z: bz, i: bi, t: bt };
}

// weld dead ends that sit within 2.5 m of another edge but share no node
{
  let welded = 0, deadEnds = 0;
  const splitsByEdge = new Map();
  for (let n = 0; n < nodes.length; n++) {
    if (nodeDeg[n] !== 1) continue;
    deadEnds++;
    const [x, z] = nodes[n];
    const own = edges.find(e => e.a === n || e.b === n);
    const cx = Math.floor(x / SEGCELL), cz = Math.floor(z / SEGCELL);
    let best = null;
    for (let ix = cx - 1; ix <= cx + 1; ix++) for (let iz = cz - 1; iz <= cz + 1; iz++) {
      const l = segHash.get(cellKey(ix, iz));
      if (!l) continue;
      for (const packed of l) {
        const e = edges[packed >> 12];
        if (e === own || e.way === own.way) continue;
        const nb = nearestOnEdge(e, x, z);
        if (nb.d < 2.5 && (!best || nb.d < best.nb.d)) best = { e, nb };
      }
    }
    if (!best) continue;
    // move the node onto the host and record a split there
    nodes[n] = [best.nb.x, best.nb.z];
    let l = splitsByEdge.get(best.e.id);
    if (!l) splitsByEdge.set(best.e.id, l = []);
    l.push({ s: best.nb.s, node: n });
    welded++;
  }
  // apply splits: cut host edges at the recorded arc positions
  for (const [eid, splits] of splitsByEdge) {
    const e = edges[eid];
    splits.sort((p, q) => p.s - q.s);
    // rebuild as pieces
    const pieces = [];
    let curPts = [e.pts[0].slice()], curA = e.a, acc = 0, si = 0;
    for (let i = 1; i < e.pts.length; i++) {
      const segL = Math.hypot(e.pts[i][0] - e.pts[i - 1][0], e.pts[i][1] - e.pts[i - 1][1]);
      while (si < splits.length && splits[si].s <= acc + segL + 1e-6) {
        const t = segL > 0 ? Math.max(0, Math.min(1, (splits[si].s - acc) / segL)) : 0;
        const p = [e.pts[i - 1][0] + (e.pts[i][0] - e.pts[i - 1][0]) * t,
                   e.pts[i - 1][1] + (e.pts[i][1] - e.pts[i - 1][1]) * t];
        curPts.push(p);
        pieces.push({ a: curA, b: splits[si].node, pts: curPts });
        curPts = [p.slice()];
        curA = splits[si].node;
        si++;
      }
      curPts.push(e.pts[i].slice());
      acc += segL;
    }
    pieces.push({ a: curA, b: e.b, pts: curPts });
    // first piece replaces the edge in place; the rest append
    let first = true;
    for (const pc of pieces) {
      const clean = [pc.pts[0]];
      for (let k = 1; k < pc.pts.length; k++)
        if (Math.hypot(pc.pts[k][0] - clean[clean.length - 1][0], pc.pts[k][1] - clean[clean.length - 1][1]) > 0.05 || k === pc.pts.length - 1)
          clean.push(pc.pts[k]);
      if (clean.length < 2) continue;
      let L = 0;
      for (let k = 1; k < clean.length; k++) L += Math.hypot(clean[k][0] - clean[k - 1][0], clean[k][1] - clean[k - 1][1]);
      if (first) { e.a = pc.a; e.b = pc.b; e.pts = clean; e.len = L; first = false; }
      else edges.push({ id: edges.length, way: e.way, a: pc.a, b: pc.b, pts: clean, len: L, seq: e.seq + 0.5 });
    }
  }
  console.log(`dead ends ${deadEnds}, welded ${welded} onto host edges (${splitsByEdge.size} hosts split)`);
  hashSegs();
}

// -------------------------------------------------------------- crossings
function segX(ax, ay, bx, by, cx, cy, dx, dy) {
  const r1x = bx - ax, r1y = by - ay, r2x = dx - cx, r2y = dy - cy;
  const den = r1x * r2y - r1y * r2x;
  if (Math.abs(den) < 1e-9) return null;
  const t = ((cx - ax) * r2y - (cy - ay) * r2x) / den;
  const u = ((cx - ax) * r1y - (cy - ay) * r1x) / den;
  if (t < 0 || t > 1 || u < 0 || u > 1) return null;
  return [ax + r1x * t, ay + r1y * t];
}
const crossings = [];   // { over, under, x, z, forced }
{
  const pairSeen = new Map();
  let same = 0, guessed = 0;
  for (const l of segHash.values()) {
    for (let m = 0; m < l.length; m++) for (let n = m + 1; n < l.length; n++) {
      const ea = edges[l[m] >> 12], ia = l[m] & 4095;
      const eb = edges[l[n] >> 12], ib = l[n] & 4095;
      if (ea === eb || ea.way === eb.way) continue;
      const hit = segX(ea.pts[ia - 1][0], ea.pts[ia - 1][1], ea.pts[ia][0], ea.pts[ia][1],
                       eb.pts[ib - 1][0], eb.pts[ib - 1][1], eb.pts[ib][0], eb.pts[ib][1]);
      if (!hit) continue;
      // a crossing at a node both edges share is the junction itself
      const shared = [ea.a, ea.b].filter(x => x === eb.a || x === eb.b);
      if (shared.some(nn => Math.hypot(nodes[nn][0] - hit[0], nodes[nn][1] - hit[1]) < 3)) continue;
      const key = ea.id < eb.id ? `${ea.id}:${eb.id}` : `${eb.id}:${ea.id}`;
      const prior = pairSeen.get(key) || [];
      if (prior.some(q => Math.hypot(q[0] - hit[0], q[1] - hit[1]) < XDEDUP_M)) continue;
      prior.push(hit); pairSeen.set(key, prior);
      const la = ea.way.level, lb = eb.way.level;
      let over, under, forced = true;
      if (la !== lb) [over, under] = la > lb ? [ea, eb] : [eb, ea];
      else {
        forced = false; guessed++;
        if (ea.way.rank !== eb.way.rank) [over, under] = ea.way.rank > eb.way.rank ? [ea, eb] : [eb, ea];
        else [over, under] = ea.len >= eb.len ? [ea, eb] : [eb, ea];
        if (la === lb) same++;
      }
      crossings.push({ over: over.id, under: under.id, x: hit[0], z: hit[1], forced });
    }
  }
  console.log(`grade separations: ${crossings.length} (${guessed} with no layer difference — over picked by class)`);
}

// --------------------------------------------------------------- water
function tsArray(file, exportName) {
  const src = readFileSync(file, 'utf8');
  const at = src.indexOf(exportName);
  if (at < 0) throw new Error(`${exportName} not in ${file}`);
  const eq = src.indexOf('=', at);
  const open = src.indexOf('[', eq);
  let depth = 0, i = open;
  for (; i < src.length; i++) {
    if (src[i] === '[') depth++;
    else if (src[i] === ']') { depth--; if (depth === 0) break; }
  }
  return new Function(`return ${src.slice(open, i + 1)};`)();
}
const RIVERS = tsArray(`${RG2}/src/config/world/baselineWater.ts`, 'BASELINE_RIVERS');
const LAKES = tsArray(`${RG2}/src/config/world/baselineWater.ts`, 'BASELINE_LAKES');
const LEGACY = tsArray(`${RG2}/src/config/world/baselineRoads.ts`, 'BASELINE_ROADS');

function rowPts(r, header) { const p = []; for (let i = header; i < r.length; i += 2) p.push([r[i], r[i + 1]]); return p; }
function polyResample(pts, step) {
  const out = [pts[0].slice()];
  let carry = 0;
  for (let i = 1; i < pts.length; i++) {
    const [ax, ay] = pts[i - 1], [bx, by] = pts[i];
    const seg = Math.hypot(bx - ax, by - ay);
    let t = step - carry;
    while (t < seg) { out.push([ax + (bx - ax) * t / seg, ay + (by - ay) * t / seg]); t += step; }
    carry = (seg - (t - step)) % step;
  }
  return out;
}
function nearestOnPoly(pts, x, y) {
  let best = Infinity, bx = 0, by = 0;
  for (let i = 1; i < pts.length; i++) {
    const [ax, ay] = pts[i - 1], [cx, cy] = pts[i];
    const dx = cx - ax, dy = cy - ay, L2 = dx * dx + dy * dy;
    let t = L2 > 0 ? ((x - ax) * dx + (y - ay) * dy) / L2 : 0;
    t = Math.max(0, Math.min(1, t));
    const px = ax + dx * t, py = ay + dy * t;
    const d = (x - px) ** 2 + (y - py) ** 2;
    if (d < best) { best = d; bx = px; by = py; }
  }
  return { d: Math.sqrt(best), x: bx, y: by };
}
function umeyama(src, dst) {
  const n = src.length;
  let mx = 0, my = 0, ux = 0, uy = 0;
  for (let i = 0; i < n; i++) { mx += src[i][0]; my += src[i][1]; ux += dst[i][0]; uy += dst[i][1]; }
  mx /= n; my /= n; ux /= n; uy /= n;
  let sxx = 0, sxy = 0, syx = 0, syy = 0, varS = 0;
  for (let i = 0; i < n; i++) {
    const ax = src[i][0] - mx, ay = src[i][1] - my, bx = dst[i][0] - ux, by = dst[i][1] - uy;
    sxx += ax * bx; sxy += ax * by; syx += ay * bx; syy += ay * by;
    varS += ax * ax + ay * ay;
  }
  const dot = sxx + syy, cross = sxy - syx;
  const th = Math.atan2(cross, dot);
  const c = Math.cos(th), s = Math.sin(th);
  const scale = (dot * c + cross * s) / varS;
  return { s: scale, c, sn: s, tx: ux - scale * (c * mx - s * my), ty: uy - scale * (s * mx + c * my) };
}
const applySim = (T, x, y) => [T.s * (T.c * x - T.sn * y) + T.tx, T.s * (T.sn * x + T.c * y) + T.ty];

// The fit runs in RG2's TILE frame (both legacy sets live there). The OSM
// side of it is RG2's own baked I-485 row — ONE merged centreline of the
// loop — rather than this snapshot's two carriageways: a ring assembled from
// both carriageways plus the ramps that share the ref is not a curve
// nearestOnPoly can walk, and the ICP collapsed its scale to 0.22 against it.
// The frames agree (RG2's tile = this metre frame / MPT about the same
// centre), so the fit carries over exactly.
const rg2Rows = loadJson(`${RG2}/fixtures/osm/charlotte_rows.json`).rows;
const osm485 = rowPts(rg2Rows.find(r => r[2] === 'I-485'), 4);
const leg485row = LEGACY.find(r => r[2] === 'I-485');
const leg485 = polyResample(rowPts(leg485row, 4), 8);
let T;
{
  const cen = pts => pts.reduce((a, p) => [a[0] + p[0] / pts.length, a[1] + p[1] / pts.length], [0, 0]);
  const rms = (pts, c) => Math.sqrt(pts.reduce((a, p) => a + (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2, 0) / pts.length);
  const cl = cen(leg485), co = cen(osm485);
  const s0 = rms(osm485, co) / rms(leg485, cl);
  T = { s: s0, c: 1, sn: 0, tx: co[0] - s0 * cl[0], ty: co[1] - s0 * cl[1] };
}
let fitResid = Infinity;
for (let it = 0; it < 14; it++) {
  const src = [], dst = [];
  let sum = 0;
  for (const p of leg485) {
    const [x, y] = applySim(T, p[0], p[1]);
    const nb = nearestOnPoly(osm485, x, y);
    src.push(p); dst.push([nb.x, nb.y]); sum += nb.d;
  }
  fitResid = sum / leg485.length;
  T = umeyama(src, dst);
}
console.log(`water fit: scale ${T.s.toFixed(4)} rot ${(Math.atan2(T.sn, T.c) * 180 / Math.PI).toFixed(3)}deg resid ${(fitResid * MPT).toFixed(0)} m`);
if (fitResid * MPT > 120) throw new Error('water co-registration failed');
const tileToM = ([tx, ty]) => [(tx - CENTER) * MPT, (CENTER - ty) * MPT];

const waters = [];
for (const rv of RIVERS) {
  const w = rv[0], name = rv[1];
  const pts = [];
  for (let i = 2; i < rv.length; i += 2) pts.push(tileToM(applySim(T, rv[i], rv[i + 1])));
  waters.push({ name, widthM: Math.max(5, w * LANE_M / 1.275), lake: false, pts });
}
for (const lk of LAKES) {
  const pts = [];
  for (let i = 1; i < lk.length; i += 2) pts.push(tileToM(applySim(T, lk[i], lk[i + 1])));
  waters.push({ name: lk[0], widthM: 0, lake: true, pts });
}
function pointInPoly(pts, x, y) {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const [xi, yi] = pts[i], [xj, yj] = pts[j];
    if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}
const wspans = [];
{
  // water segments hashed so 40k edges do not each walk 30 creeks
  const wHash = new Map();
  waters.forEach((w, wi) => {
    if (w.lake) return;
    for (let i = 1; i < w.pts.length; i++) {
      const [ax, az] = w.pts[i - 1], [bx, bz] = w.pts[i];
      const x0 = Math.floor(Math.min(ax, bx) / SEGCELL), x1 = Math.floor(Math.max(ax, bx) / SEGCELL);
      const z0 = Math.floor(Math.min(az, bz) / SEGCELL), z1 = Math.floor(Math.max(az, bz) / SEGCELL);
      for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) {
        const k = cellKey(cx, cz);
        let l = wHash.get(k);
        if (!l) wHash.set(k, l = []);
        l.push([wi, i]);
      }
    }
  });
  const lakeBB = waters.map(w => {
    if (!w.lake) return null;
    let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
    for (const p of w.pts) { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); }
    return [x0, z0, x1, z1];
  });
  for (const e of edges) {
    const acc = [0];
    for (let i = 1; i < e.pts.length; i++) acc.push(acc[i - 1] + Math.hypot(e.pts[i][0] - e.pts[i - 1][0], e.pts[i][1] - e.pts[i - 1][1]));
    const spans = [];
    for (let i = 1; i < e.pts.length; i++) {
      const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
      const x0 = Math.floor(Math.min(ax, bx) / SEGCELL), x1 = Math.floor(Math.max(ax, bx) / SEGCELL);
      const z0 = Math.floor(Math.min(az, bz) / SEGCELL), z1 = Math.floor(Math.max(az, bz) / SEGCELL);
      const seen = new Set();
      for (let cx = x0; cx <= x1; cx++) for (let cz = z0; cz <= z1; cz++) {
        const l = wHash.get(cellKey(cx, cz));
        if (!l) continue;
        for (const [wi, j] of l) {
          const k = wi * 100000 + j;
          if (seen.has(k)) continue;
          seen.add(k);
          const w = waters[wi];
          const p = segX(ax, az, bx, bz, w.pts[j - 1][0], w.pts[j - 1][1], w.pts[j][0], w.pts[j][1]);
          if (!p) continue;
          const segL = acc[i] - acc[i - 1];
          const s = acc[i - 1] + Math.hypot(p[0] - ax, p[1] - az);
          const half = w.widthM / 2 + BANK_M;
          spans.push([s - half, s + half]);
        }
      }
    }
    // lakes: inside-intervals
    waters.forEach((w, wi) => {
      if (!w.lake) return;
      const bb = lakeBB[wi];
      let touches = false;
      for (const p of e.pts) if (p[0] >= bb[0] - 10 && p[0] <= bb[2] + 10 && p[1] >= bb[1] - 10 && p[1] <= bb[3] + 10) { touches = true; break; }
      if (!touches) return;
      let prevIn = pointInPoly(w.pts, e.pts[0][0], e.pts[0][1]);
      let openAt = prevIn ? 0 : -1;
      for (let i = 1; i < e.pts.length; i++) {
        const nowIn = pointInPoly(w.pts, e.pts[i][0], e.pts[i][1]);
        if (nowIn !== prevIn) {
          const sMid = (acc[i - 1] + acc[i]) / 2;
          if (nowIn) openAt = sMid;
          else if (openAt >= 0) { spans.push([openAt - BANK_M, sMid + BANK_M]); openAt = -1; }
          else spans.push([0, sMid + BANK_M]);
          prevIn = nowIn;
        }
      }
      if (openAt >= 0) spans.push([openAt - BANK_M, acc[acc.length - 1]]);
    });
    if (!spans.length) continue;
    spans.sort((p, q) => p[0] - q[0]);
    const merged = [spans[0]];
    for (const s of spans.slice(1)) {
      const last = merged[merged.length - 1];
      if (s[0] <= last[1] + 2) last[1] = Math.max(last[1], s[1]); else merged.push(s);
    }
    const total = acc[acc.length - 1];
    for (const [s0, s1] of merged) wspans.push({ e: e.id, s0: Math.max(0, s0), s1: Math.min(total, s1) });
  }
  console.log(`water bridge spans: ${wspans.length}`);
}

// ---------------------------------------------------------------- controls
const nodeCtl = new Uint8Array(nodes.length);
{
  let matched = 0;
  for (const n of rawNodes) {
    const idx = nodeIndex.get(n.id);
    if (idx === undefined) continue;
    const h = (n.tags || {}).highway;
    nodeCtl[idx] = h === 'traffic_signals' ? 4 : h === 'stop' ? 2 : h === 'give_way' ? 1 : 0;
    if (nodeCtl[idx]) matched++;
  }
  console.log(`controls matched to graph nodes: ${matched}`);
}

// ------------------------------------------------------------------- DEM
let bbox = { x0: 1e18, x1: -1e18, z0: 1e18, z1: -1e18 };
for (const e of edges) for (const p of e.pts) {
  bbox.x0 = Math.min(bbox.x0, p[0]); bbox.x1 = Math.max(bbox.x1, p[0]);
  bbox.z0 = Math.min(bbox.z0, p[1]); bbox.z1 = Math.max(bbox.z1, p[1]);
}
bbox = { x0: bbox.x0 - DEM_MARGIN, x1: bbox.x1 + DEM_MARGIN, z0: bbox.z0 - DEM_MARGIN, z1: bbox.z1 + DEM_MARGIN };
const demNX = Math.ceil((bbox.x1 - bbox.x0) / DEM_CELL) + 1;
const demNZ = Math.ceil((bbox.z1 - bbox.z0) / DEM_CELL) + 1;
console.log(`DEM grid ${demNX} x ${demNZ} at ${DEM_CELL} m over ${((bbox.x1 - bbox.x0) / 1000).toFixed(1)} x ${((bbox.z1 - bbox.z0) / 1000).toFixed(1)} km`);
const srtm = await srtmSampler({ s: toLat(bbox.z0), n: toLat(bbox.z1), w: toLon(bbox.x0), e: toLon(bbox.x1) }, SRTM_CACHE);
let dem = new Float32Array(demNX * demNZ);
for (let iz = 0; iz < demNZ; iz++)
  for (let ix = 0; ix < demNX; ix++)
    dem[iz * demNX + ix] = srtm(toLat(bbox.z0 + iz * DEM_CELL), toLon(bbox.x0 + ix * DEM_CELL));
// Roofs: SRTM is a radar return and uptown's towers stand 200 m proud of
// the street. A 5x5 MIN over 60 m cells (a 120 m reach) finds street level
// between blocks; then a Gaussian settles the erosion's flat tops.
function minFilter(src, r) {
  const out = new Float32Array(src.length);
  for (let iz = 0; iz < demNZ; iz++) for (let ix = 0; ix < demNX; ix++) {
    let m = Infinity;
    for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) {
      const x = Math.min(demNX - 1, Math.max(0, ix + dx)), z = Math.min(demNZ - 1, Math.max(0, iz + dz));
      m = Math.min(m, src[z * demNX + x]);
    }
    out[iz * demNX + ix] = m;
  }
  return out;
}
function gauss(src, sigma) {
  const r = Math.ceil(sigma * 2.5);
  const k = [];
  let ks = 0;
  for (let i = -r; i <= r; i++) { const v = Math.exp(-i * i / (2 * sigma * sigma)); k.push(v); ks += v; }
  const tmp = new Float32Array(src.length), out = new Float32Array(src.length);
  for (let iz = 0; iz < demNZ; iz++) for (let ix = 0; ix < demNX; ix++) {
    let s = 0;
    for (let i = -r; i <= r; i++) s += k[i + r] * src[iz * demNX + Math.min(demNX - 1, Math.max(0, ix + i))];
    tmp[iz * demNX + ix] = s / ks;
  }
  for (let iz = 0; iz < demNZ; iz++) for (let ix = 0; ix < demNX; ix++) {
    let s = 0;
    for (let i = -r; i <= r; i++) s += k[i + r] * tmp[Math.min(demNZ - 1, Math.max(0, iz + i)) * demNX + ix];
    out[iz * demNX + ix] = s / ks;
  }
  return out;
}
function maxFilter(src, r) {
  const out = new Float32Array(src.length);
  for (let iz = 0; iz < demNZ; iz++) for (let ix = 0; ix < demNX; ix++) {
    let m = -Infinity;
    for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) {
      const x = Math.min(demNX - 1, Math.max(0, ix + dx)), z = Math.min(demNZ - 1, Math.max(0, iz + dz));
      m = Math.max(m, src[z * demNX + x]);
    }
    out[iz * demNX + ix] = m;
  }
  return out;
}
// A bare MIN filter (the first cut) took the roofs out and the hills with
// them: 6.6 m low on average, 20 m low beside every valley, so every road
// on a hillside stood proud of the "ground" and was built as a deck. A
// morphological OPENING (erode, then dilate back) removes only what is
// narrower than the kernel — the towers — and returns the surface
// everywhere else (2 m mean, and Trade & Tryon lands at 227 m ASL, which
// is right); the CLOSING after it fills the one-pixel pits the radar left.
{
  const opened = maxFilter(minFilter(dem, 2), 2);
  const closed = minFilter(maxFilter(opened, 1), 1);
  dem = gauss(closed, 1.1);
}
let demMin = Infinity, demMax = -Infinity;
for (const v of dem) { demMin = Math.min(demMin, v); demMax = Math.max(demMax, v); }
const demBase = Math.floor(demMin) - 2;
console.log(`DEM range ${demMin.toFixed(0)}..${demMax.toFixed(0)} m ASL; base ${demBase}`);
function demAt(x, z) {
  const fx = (x - bbox.x0) / DEM_CELL, fz = (z - bbox.z0) / DEM_CELL;
  const ix = Math.min(demNX - 2, Math.max(0, Math.floor(fx))), iz = Math.min(demNZ - 2, Math.max(0, Math.floor(fz)));
  const tx = Math.min(1, Math.max(0, fx - ix)), tz = Math.min(1, Math.max(0, fz - iz));
  const a = dem[iz * demNX + ix], b = dem[iz * demNX + ix + 1], c = dem[(iz + 1) * demNX + ix], d = dem[(iz + 1) * demNX + ix + 1];
  return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz - demBase;
}

// ----------------------------------------------------------------- routes
// The three Charlotte venues, as verified way-id lists (tools/clt/fetch_clt.mjs,
// 2026-09-07, checked again against this snapshot at export time). A route
// is an ordered chain of graph edges with a direction each.
const ROUTES = [
  {
    id: 'uptown', name: 'UPTOWN LOOP', loop: true, roadWidth: 14.6, oneway: true, speed: 80,
    startAt: [35.2367939, -80.8401828], leadM: 0, shutdownM: 0,
    wayIds: [101537866,101537836,101537860,122753230,159022507,159022518,999013937,
      159022506,159022515,159022502,159022498,159022496,159022509,159022503,648310354,
      159022510,166576479,166576475,449112229,122753228,159022511,159022517,51062795,
      159022548,1193243346,159022547,51062772,159022552,159022545,55249555,55249553,
      55204694,999046226,55204686,94405125,992734649,94405118,159072248,159072239,
      120108305,449112238,159072322,94750534,122082583,94750518,1343703528,94750516,
      94750540,94745382,648861197,94745335,40153335,648861047,836960240,1340236493,
      836960239,836960241,16661475,836960242,449113974,122753223,836960235,836960234,
      101537874],
  },
  {
    id: 'tryon', name: 'TRYON STREET SPRINT', loop: false, roadWidth: 15.5, oneway: false, speed: 56,
    leadM: 60, shutdownM: 250,
    wayIds: [1343934894,1343934893,1128432446,129834072,1128437577,1128432445,1128432444,
      129834070,51063159,34764092,34764115,1183425772,130144233,130144237,1183425773,
      1123907747,1183425774,16714405,1183425775,1051632137,648281647,16674240,645263197,
      1367659007,1255272355,502495542,1051039408,502495543,1255950645,654682186,713030468,
      1343934255,648297750,732748582,1430838847,1031979931,1031979929,1031979930,
      1365969253,1345043673,1345043674,1345043672,886386517,1345038519,1345038520,
      1345038521,732748585,1345155870,1345155871,1345155872,1345099188,952112777,
      1055083832,736548240,1546936566,1055289169,1055016922,648281611],
  },
  {
    id: 'independence', name: 'INDEPENDENCE SPRINT', loop: false, roadWidth: 16.0, oneway: true, speed: 89,
    leadM: 60, shutdownM: 300,
    wayIds: [34922866,648310357,51062775,159022530,159022529,159022531,159022535,
      1042942297,1042942296,1042934015,1042934016,51062789,1042945066,854801717,
      854801720,1122238767,1122238766,1012799821,1012799822,1123280092,1123280093,
      1123280096,1123280097,51062788,1123280099,1123280098,1123280095,1123280094,
      257836865,158903886,1023317415,648297613,1049913071,1076368826,158903888,
      648297612,574785444],
  },
];
const edgesByWay = new Map();
for (const e of edges) {
  let l = edgesByWay.get(e.way.id);
  if (!l) edgesByWay.set(e.way.id, l = []);
  l.push(e);
}
for (const l of edgesByWay.values()) l.sort((p, q) => p.seq - q.seq);

// oneway-respecting shortest path, for a gap between two listed ways
const adj = new Array(nodes.length).fill(null).map(() => []);
for (const e of edges) {
  adj[e.a].push({ e, dir: 1, to: e.b });
  if (!e.way.oneway) adj[e.b].push({ e, dir: -1, to: e.a });
}
function shortestPath(from, to, maxM) {
  const dist = new Map([[from, 0]]);
  const prev = new Map();
  const open = [[0, from]];
  while (open.length) {
    open.sort((p, q) => p[0] - q[0]);
    const [d, n] = open.shift();
    if (n === to) break;
    if (d > maxM || d > dist.get(n)) continue;
    for (const s of adj[n]) {
      const nd = d + s.e.len;
      if (nd < (dist.get(s.to) ?? Infinity)) { dist.set(s.to, nd); prev.set(s.to, s); open.push([nd, s.to]); }
    }
  }
  if (!prev.has(to)) return null;
  const out = [];
  for (let n = to; n !== from; n = prev.get(n).dir === 1 ? prev.get(n).e.a : prev.get(n).e.b) out.push(prev.get(n));
  return out.reverse();
}

const routes = [];
for (const R of ROUTES) {
  const chain = [];   // { e, dir }
  const missing = R.wayIds.filter(id => !edgesByWay.has(id));
  if (missing.length) console.log(`  ${R.id}: ${missing.length} way ids not in this snapshot (${missing.slice(0, 5).join(',')}) — routing across them`);
  let tail = -1;
  let routed = 0;
  for (let k = 0; k < R.wayIds.length; k++) {
    const l = edgesByWay.get(R.wayIds[k]);
    if (!l) continue;
    const headF = l[0].a, tailF = l[l.length - 1].b;
    let fwd;
    if (tail < 0) {
      // orient by the next listed way that exists
      let nx = null;
      for (let j = k + 1; j < R.wayIds.length && !nx; j++) nx = edgesByWay.get(R.wayIds[j]);
      if (nx) {
        const ends = [nx[0].a, nx[nx.length - 1].b];
        const near = (n, m) => Math.hypot(nodes[n][0] - nodes[m][0], nodes[n][1] - nodes[m][1]);
        const dF = Math.min(near(tailF, ends[0]), near(tailF, ends[1]));
        const dB = Math.min(near(headF, ends[0]), near(headF, ends[1]));
        fwd = dF <= dB;
      } else fwd = true;
    } else if (tail === headF) fwd = true;
    else if (tail === tailF && !l[0].way.oneway) fwd = false;
    else {
      // not adjacent: route to whichever end is reachable
      const pF = shortestPath(tail, headF, 1500);
      const pB = l[0].way.oneway ? null : shortestPath(tail, tailF, 1500);
      const use = pF && (!pB || pF.length <= pB.length) ? pF : pB;
      if (!use) throw new Error(`${R.id}: way ${R.wayIds[k]} (#${k}) is not reachable from the chain`);
      for (const s of use) chain.push({ e: s.e, dir: s.dir });
      routed += use.length;
      fwd = use === pF;
      tail = fwd ? headF : tailF;
    }
    if (fwd) { for (const e of l) chain.push({ e, dir: 1 }); tail = tailF; }
    else { for (let i = l.length - 1; i >= 0; i--) chain.push({ e: l[i], dir: -1 }); tail = headF; }
  }
  // continuity + length
  let L = 0;
  for (let i = 0; i < chain.length; i++) {
    const c = chain[i];
    const from = c.dir === 1 ? c.e.a : c.e.b, to = c.dir === 1 ? c.e.b : c.e.a;
    if (i > 0) {
      const p = chain[i - 1];
      const pTo = p.dir === 1 ? p.e.b : p.e.a;
      if (pTo !== from) throw new Error(`${R.id}: chain breaks at edge ${i} (${p.e.way.name} -> ${c.e.way.name})`);
    }
    L += c.e.len;
  }
  if (R.loop) {
    const first = chain[0].dir === 1 ? chain[0].e.a : chain[0].e.b;
    if (tail !== first) {
      const close = shortestPath(tail, first, 800);
      if (!close) throw new Error(`${R.id}: loop does not close`);
      for (const s of close) { chain.push({ e: s.e, dir: s.dir }); L += s.e.len; }
      routed += close.length;
    }
  }
  // polyline (dense) for the thumbnail + the start line position
  const poly = [];
  for (const c of chain) {
    const pts = c.dir === 1 ? c.e.pts : c.e.pts.slice().reverse();
    for (let i = poly.length ? 1 : 0; i < pts.length; i++) poly.push(pts[i]);
  }
  let startM = R.leadM;
  if (R.startAt) {
    const sx = toX(R.startAt[1]), sz = toZ(R.startAt[0]);
    let best = Infinity, acc = 0;
    for (let i = 1; i < poly.length; i++) {
      const [ax, az] = poly[i - 1], [bx, bz] = poly[i];
      const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
      let t = L2 > 0 ? ((sx - ax) * dx + (sz - az) * dz) / L2 : 0;
      t = Math.max(0, Math.min(1, t));
      const d = Math.hypot(sx - (ax + dx * t), sz - (az + dz * t));
      if (d < best) { best = d; startM = acc + Math.sqrt(L2) * t; }
      acc += Math.sqrt(L2);
    }
    console.log(`  ${R.id}: start line ${best.toFixed(0)} m from the anchor at ${startM.toFixed(0)} m along`);
  }
  const finishM = R.loop ? 0 : L - R.shutdownM;
  const bridgeM = chain.reduce((a, c) => a + (c.e.way.bridge ? c.e.len : 0), 0);
  console.log(`  route ${R.id}: ${chain.length} edges (${routed} routed), ${(L / 1000).toFixed(2)} km, ${(bridgeM / 1000).toFixed(2)} km on structure, ${chain.filter(c => c.e.way.link).length} ramp edges`);
  // coarse polyline every ~20 m for the menu chip
  const coarse = [poly[0]];
  let carry = 0;
  for (let i = 1; i < poly.length; i++) {
    const [ax, az] = poly[i - 1], [bx, bz] = poly[i];
    const seg = Math.hypot(bx - ax, bz - az);
    let t = 20 - carry;
    while (t < seg) { coarse.push([ax + (bx - ax) * t / seg, az + (bz - az) * t / seg]); t += 20; }
    carry = (seg - (t - 20)) % 20;
  }
  coarse.push(poly[poly.length - 1]);
  routes.push({ ...R, chain, lengthM: L, startM, finishM, coarse });
}

// -------------------------------------------------------------- buildings
function rdp(pts, eps) {
  if (pts.length < 3) return pts;
  const keep = new Uint8Array(pts.length);
  keep[0] = keep[pts.length - 1] = 1;
  const stack = [[0, pts.length - 1]];
  while (stack.length) {
    const [a, b] = stack.pop();
    let maxD = -1, maxI = -1;
    const [ax, ay] = pts[a], [bx, by] = pts[b];
    const dx = bx - ax, dy = by - ay, L2 = dx * dx + dy * dy;
    for (let i = a + 1; i < b; i++) {
      let t = L2 > 0 ? ((pts[i][0] - ax) * dx + (pts[i][1] - ay) * dy) / L2 : 0;
      t = Math.max(0, Math.min(1, t));
      const d = Math.hypot(pts[i][0] - (ax + dx * t), pts[i][1] - (ay + dy * t));
      if (d > maxD) { maxD = d; maxI = i; }
    }
    if (maxD > eps) { keep[maxI] = 1; stack.push([a, maxI], [maxI, b]); }
  }
  return pts.filter((_, i) => keep[i]);
}
function polyArea(p) { let a = 0; for (let i = 0; i < p.length; i++) { const q = p[(i + 1) % p.length]; a += p[i][0] * q[1] - q[0] * p[i][1]; } return a / 2; }
const HOUSE_TYPES = new Set(['house', 'detached', 'semidetached_house', 'terrace', 'residential', 'bungalow', 'cabin', 'garage', 'garages', 'hut']);
const SKIP_TYPES = new Set(['roof', 'carport', 'shed', 'greenhouse', 'no', 'ruins', 'tent', 'container']);
function parseHeight(h) {
  if (!h) return NaN;
  const m = /([\d.]+)\s*(m|ft|')?/.exec(h);
  if (!m) return NaN;
  const v = parseFloat(m[1]);
  return m[2] === 'ft' || m[2] === "'" ? v * 0.3048 : v;
}
const buildings = [];
{
  let dropped = 0;
  const addPoly = (geom, t) => {
    if (SKIP_TYPES.has(t.building)) { dropped++; return; }
    // An ELEVATED structure — a skywalk over the street (uptown's
    // Overstreet Mall), a building on stilts, anything with a floor above
    // the ground — extruded from the pavement is a wall across the road.
    if (t.building === 'bridge' || parseInt(t['building:min_level'], 10) > 0 ||
        parseHeight(t.min_height) > 1.5 || parseInt(t.layer, 10) > 0) { dropped++; return; }
    let pts = geom.map(g => [toX(g.lon), toZ(g.lat)]);
    if (pts.length > 1 && Math.hypot(pts[0][0] - pts[pts.length - 1][0], pts[0][1] - pts[pts.length - 1][1]) < 0.05) pts.pop();
    pts = rdp(pts, 0.35);
    if (pts.length < 3) { dropped++; return; }
    let area = polyArea(pts);
    if (area < 0) { pts.reverse(); area = -area; }   // counter-clockwise, always
    if (area < 25) { dropped++; return; }
    if (pts.length > 40) pts = rdp(pts, 1.2);
    const house = HOUSE_TYPES.has(t.building) || (t.building === 'yes' && area < 260);
    let h = parseHeight(t.height);
    const levels = parseInt(t['building:levels'], 10);
    if (!Number.isFinite(h) || h <= 0) {
      if (Number.isFinite(levels) && levels > 0) h = levels * (house ? 2.9 : 3.4) + (house ? 0.6 : 1.0);
      else if (house) h = t.building === 'garage' || t.building === 'garages' ? 3.2 : area < 90 ? 3.6 : area < 220 ? 5.6 : 7.2;
      else if (t.building === 'parking') h = 12;
      else if (t.building === 'church') h = 14;
      else if (t.building === 'apartments' || t.building === 'dormitory' || t.building === 'hotel') h = 13;
      else h = area < 300 ? 5.5 : area < 1200 ? 8 : area < 4000 ? 12 : 16;
    }
    h = Math.min(h, 300);
    // style: 0 tower glass, 1 midrise, 2 brick, 3 house, 4 shops (retail
    // ground floor; the tile builder decides which wall faces the street)
    let style;
    if (house) style = 3;
    else if (h >= 55) style = 0;
    else if (h >= 20) style = 1;
    else if (t.building === 'retail' || t.building === 'commercial' || t.shop || t.amenity) style = 4;
    else style = area > 900 ? 1 : (t.building === 'yes' ? 4 : 2);
    const gable = house && area < 480 && pts.length <= 10;
    buildings.push({ pts, h, style, gable, area });
  };
  for (const el of rawBld) {
    if (el.type === 'way' && el.tags && el.geometry) addPoly(el.geometry, el.tags);
    else if (el.type === 'relation' && el.tags && el.members) {
      for (const m of el.members) if (m.role === 'outer' && m.geometry) addPoly(m.geometry, el.tags);
    }
  }
  // A footprint a road runs THROUGH (in one side and out the other) is a
  // structure over the road or a mapping error; either way a wall across
  // the lane. A road that merely ENDS inside one is a garage entrance.
  {
    let through = 0;
    const keep = [];
    for (const b of buildings) {
      let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
      for (const p of b.pts) { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); }
      let straddled = false;
      const seenSeg = new Set();
      outer:
      for (let cx = Math.floor(x0 / SEGCELL); cx <= Math.floor(x1 / SEGCELL); cx++)
        for (let cz = Math.floor(z0 / SEGCELL); cz <= Math.floor(z1 / SEGCELL); cz++) {
          const l = segHash.get(cellKey(cx, cz));
          if (!l) continue;
          for (const packed of l) {
            if (seenSeg.has(packed)) continue;
            seenSeg.add(packed);
            const e = edges[packed >> 12], i = packed & 4095;
            const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
            let hits = 0;
            for (let k = 0; k < b.pts.length; k++) {
              const p = b.pts[k], q = b.pts[(k + 1) % b.pts.length];
              if (segX(ax, az, bx, bz, p[0], p[1], q[0], q[1])) hits++;
            }
            if (hits >= 2) { straddled = true; break outer; }
          }
        }
      if (straddled) through++; else keep.push(b);
    }
    buildings.length = 0;
    for (const b of keep) buildings.push(b);
    dropped += through;
    console.log(`  ${through} footprints straddling a road dropped`);
  }
  const styles = [0, 0, 0, 0, 0];
  for (const b of buildings) styles[b.style]++;
  console.log(`buildings: ${buildings.length} kept, ${dropped} dropped; styles glass ${styles[0]} mid ${styles[1]} brick ${styles[2]} house ${styles[3]} shops ${styles[4]}; gabled ${buildings.filter(b => b.gable).length}`);
  // the building bbox, so the tile builder knows where data covers the ground
  let x0 = 1e18, x1 = -1e18, z0 = 1e18, z1 = -1e18;
  for (const b of buildings) for (const p of b.pts) { x0 = Math.min(x0, p[0]); x1 = Math.max(x1, p[0]); z0 = Math.min(z0, p[1]); z1 = Math.max(z1, p[1]); }
  buildings.bbox = [x0, z0, x1, z1];
  console.log(`  footprint coverage x ${x0.toFixed(0)}..${x1.toFixed(0)} z ${z0.toFixed(0)}..${z1.toFixed(0)}`);
}

// ------------------------------------------------------------------ emit
class Writer {
  constructor() { this.chunks = []; this.buf = Buffer.alloc(1 << 20); this.pos = 0; }
  ensure(n) { if (this.pos + n > this.buf.length) { this.chunks.push(this.buf.subarray(0, this.pos)); this.buf = Buffer.alloc(Math.max(1 << 20, n)); this.pos = 0; } }
  u8(v) { this.ensure(1); this.buf.writeUInt8(v & 255, this.pos); this.pos += 1; }
  i8(v) { this.ensure(1); this.buf.writeInt8(Math.max(-128, Math.min(127, v)), this.pos); this.pos += 1; }
  u16(v) { this.ensure(2); this.buf.writeUInt16LE(v & 65535, this.pos); this.pos += 2; }
  i32(v) { this.ensure(4); this.buf.writeInt32LE(v | 0, this.pos); this.pos += 4; }
  u32(v) { this.ensure(4); this.buf.writeUInt32LE(v >>> 0, this.pos); this.pos += 4; }
  f32(v) { this.ensure(4); this.buf.writeFloatLE(v, this.pos); this.pos += 4; }
  /// C#'s BinaryReader.ReadString: 7-bit-encoded byte length then UTF-8.
  str(s) {
    const b = Buffer.from(s || '', 'utf8');
    let n = b.length;
    do { let byte = n & 0x7f; n >>>= 7; if (n) byte |= 0x80; this.u8(byte); } while (n);
    this.ensure(b.length); b.copy(this.buf, this.pos); this.pos += b.length;
  }
  bytes() { this.chunks.push(this.buf.subarray(0, this.pos)); return Buffer.concat(this.chunks); }
}
mkdirSync(RES, { recursive: true });

// uptown (Trade & Tryon)
const uptownX = toX(-80.8431), uptownZ = toZ(35.2271);

// ---- graph
{
  const w = new Writer();
  w.u32(0x43585350); // "PSXC"
  w.u32(1);
  w.str(ATTRIBUTION);
  w.f32(uptownX); w.f32(uptownZ);
  w.u32(nodes.length);
  for (let i = 0; i < nodes.length; i++) { w.f32(nodes[i][0]); w.f32(nodes[i][1]); w.u8(nodeCtl[i]); }
  const names = new Map([['', 0]]);
  const nameList = [''];
  const nameIdx = s => { let i = names.get(s); if (i === undefined) { i = nameList.length; nameList.push(s); names.set(s, i); } return i; };
  for (const e of edges) nameIdx(e.way.name);
  for (const wt of waters) nameIdx(wt.name);
  w.u32(nameList.length);
  for (const s of nameList) w.str(s);
  w.u32(edges.length);
  for (const e of edges) {
    const wy = e.way;
    w.u32(e.a); w.u32(e.b); w.u32(nameIdx(wy.name));
    w.u8(wy.rank);
    w.u8((wy.link ? 1 : 0) | (wy.oneway ? 2 : 0) | (wy.bridge ? 4 : 0) | (wy.tunnel ? 8 : 0) | (wy.turn ? 16 : 0) | (wy.roundabout ? 32 : 0));
    w.u8(wy.lanes); w.i8(wy.level);
    w.f32(wy.lanes * LANE_M + wy.shl + wy.shr); w.f32(wy.shl); w.f32(wy.shr);
    w.u8(Math.min(255, wy.speed)); w.u32(wy.id);
    w.u16(e.pts.length);
    for (const p of e.pts) { w.f32(p[0]); w.f32(p[1]); }
  }
  w.u32(waters.length);
  for (const wt of waters) {
    w.u32(nameIdx(wt.name)); w.f32(wt.widthM); w.u8(wt.lake ? 1 : 0);
    w.u32(wt.pts.length);
    for (const p of wt.pts) { w.f32(p[0]); w.f32(p[1]); }
  }
  w.u32(crossings.length);
  for (const c of crossings) { w.u32(c.over); w.u32(c.under); w.f32(c.x); w.f32(c.z); w.u8(c.forced ? 1 : 0); }
  w.u32(wspans.length);
  for (const s of wspans) { w.u32(s.e); w.f32(s.s0); w.f32(s.s1); }
  w.u32(routes.length);
  for (const r of routes) {
    w.str(r.id); w.str(r.name); w.u8(r.loop ? 1 : 0); w.u8(r.oneway ? 1 : 0);
    w.f32(r.roadWidth); w.u8(r.speed);
    w.f32(r.lengthM); w.f32(r.startM); w.f32(r.finishM);
    w.u32(r.chain.length);
    for (const c of r.chain) { w.u32(c.e.id); w.i8(c.dir); }
  }
  const bytes = w.bytes();
  writeFileSync(join(RES, 'charlotte_city.bytes'), bytes);
  console.log(`wrote charlotte_city.bytes ${(bytes.length / 1024).toFixed(0)} KB`);
}

// ---- DEM
{
  const w = new Writer();
  w.u32(0x4D454450); // "PDEM"
  w.u32(1);
  w.u32(demNX); w.u32(demNZ);
  w.f32(bbox.x0); w.f32(bbox.z0); w.f32(DEM_CELL); w.f32(demBase);
  for (let i = 0; i < dem.length; i++) w.u16(Math.round(Math.max(0, dem[i] - demBase) * 10));
  const bytes = w.bytes();
  writeFileSync(join(RES, 'charlotte_dem.bytes'), bytes);
  console.log(`wrote charlotte_dem.bytes ${(bytes.length / 1024).toFixed(0)} KB`);
}

// ---- buildings
{
  const w = new Writer();
  w.u32(0x444C4250); // "PBLD"
  w.u32(1);
  const bb = buildings.bbox || [0, 0, 0, 0];
  w.f32(bb[0]); w.f32(bb[1]); w.f32(bb[2]); w.f32(bb[3]);
  w.u32(buildings.length);
  for (const b of buildings) {
    w.u8(b.style | (b.gable ? 0x80 : 0)); w.f32(b.h);
    w.u8(b.pts.length);
    for (const p of b.pts) { w.f32(p[0]); w.f32(p[1]); }
  }
  const bytes = w.bytes();
  writeFileSync(join(RES, 'charlotte_bld.bytes'), bytes);
  console.log(`wrote charlotte_bld.bytes ${(bytes.length / 1024).toFixed(0)} KB`);
}

// ---- the menu's routes (small JSON: lengths and a coarse line per venue)
{
  const r1 = v => Math.round(v * 10) / 10;
  const out = {
    attribution: ATTRIBUTION,
    routes: routes.map(r => ({
      id: r.id, name: r.name, loop: r.loop ? 1 : 0, oneway: r.oneway ? 1 : 0,
      roadWidth: r.roadWidth, speed: r.speed,
      lengthM: r1(r.lengthM), startM: r1(r.startM), finishM: r1(r.finishM),
      pts: r.coarse.flatMap(p => [r1(p[0]), r1(p[1])]),
    })),
  };
  writeFileSync(join(RES, 'charlotte_routes.json'), JSON.stringify(out));
  console.log('wrote charlotte_routes.json');
}

// ------------------------------------------------------------- stats
{
  let km = 0, bridgeKm = 0, linkKm = 0, byRank = [0, 0, 0, 0, 0, 0], ow = 0;
  for (const e of edges) { km += e.len; if (e.way.bridge) bridgeKm += e.len; if (e.way.link) linkKm += e.len; byRank[e.way.rank] += e.len; if (e.way.oneway) ow += e.len; }
  console.log(`network ${(km / 1000).toFixed(0)} km: motorway ${(byRank[5] / 1000).toFixed(0)}, trunk ${(byRank[4] / 1000).toFixed(0)}, primary ${(byRank[3] / 1000).toFixed(0)}, secondary ${(byRank[2] / 1000).toFixed(0)}, tertiary ${(byRank[1] / 1000).toFixed(0)}, local ${(byRank[0] / 1000).toFixed(0)}; ${(bridgeKm / 1000).toFixed(1)} km on bridges, ${(linkKm / 1000).toFixed(0)} km of ramps, ${(100 * ow / km).toFixed(0)}% one-way`);
  const deg = new Array(8).fill(0);
  const d2 = new Array(nodes.length).fill(0);
  for (const e of edges) { d2[e.a]++; d2[e.b]++; }
  for (const d of d2) deg[Math.min(7, d)]++;
  console.log(`node degrees: ${deg.map((n, i) => `${i}:${n}`).join(' ')}`);
  // connectivity from uptown
  const seen = new Uint8Array(nodes.length);
  let start = -1, bd = Infinity;
  for (const e of edges) { if (e.way.link || e.way.rank === 5) continue; const d = Math.hypot(nodes[e.a][0] - uptownX, nodes[e.a][1] - uptownZ); if (d < bd) { bd = d; start = e.a; } }
  const stack = [start]; seen[start] = 1;
  while (stack.length) { const n = stack.pop(); for (const s of adj[n]) if (!seen[s.to]) { seen[s.to] = 1; stack.push(s.to); } }
  let inKm = 0;
  for (const e of edges) if (seen[e.a]) inKm += e.len;
  console.log(`reachable from uptown (respecting oneway): ${(100 * inKm / km).toFixed(1)}%`);
}

// ------------------------------------------------------------- debug PNGs
function png(width, height, rgb, path) {
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) { raw[y * (width * 3 + 1)] = 0; rgb.copy(raw, y * (width * 3 + 1) + 1, y * width * 3, (y + 1) * width * 3); }
  const crcTable = new Int32Array(256);
  for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crcTable[n] = c; }
  const crc = b => { let c = -1; for (const v of b) c = crcTable[(c ^ v) & 255] ^ (c >>> 8); return (c ^ -1) >>> 0; };
  const chunk = (type, data) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const td = Buffer.concat([Buffer.from(type), data]);
    const cc = Buffer.alloc(4); cc.writeUInt32BE(crc(td));
    return Buffer.concat([len, td, cc]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  writeFileSync(path, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]));
}
function plot(name, cx, cz, halfM, S) {
  const rgb = Buffer.alloc(S * S * 3, 255);
  const scale = S / (2 * halfM);
  const px = x => (x - cx + halfM) * scale, py = z => (halfM - (z - cz)) * scale;
  const put = (x, y, r, g, b) => { if (x < 0 || y < 0 || x >= S || y >= S) return; const i = (y * S + x) * 3; rgb[i] = r; rgb[i + 1] = g; rgb[i + 2] = b; };
  const line = (x0, y0, x1, y1, wpx, r, g, b) => {
    const L = Math.hypot(x1 - x0, y1 - y0), steps = Math.max(1, Math.ceil(L)), rad = Math.max(0, Math.round(wpx / 2));
    for (let i = 0; i <= steps; i++) {
      const x = x0 + (x1 - x0) * i / steps, y = y0 + (y1 - y0) * i / steps;
      for (let dy = -rad; dy <= rad; dy++) for (let dx = -rad; dx <= rad; dx++) if (dx * dx + dy * dy <= rad * rad) put(Math.round(x + dx), Math.round(y + dy), r, g, b);
    }
  };
  const inView = p => Math.abs(p[0] - cx) < halfM * 1.1 && Math.abs(p[1] - cz) < halfM * 1.1;
  // ground shade from the DEM
  if (halfM > 3000) {
    for (let y = 0; y < S; y += 2) for (let x = 0; x < S; x += 2) {
      const h = demAt(cx - halfM + x / scale, cz + halfM - y / scale);
      const v = 205 + Math.round(Math.min(50, Math.max(-30, (h - 60) * 0.6)));
      for (let dy = 0; dy < 2; dy++) for (let dx = 0; dx < 2; dx++) put(x + dx, y + dy, v, v + 4, v - 8);
    }
  }
  for (const b of buildings) {
    if (!inView(b.pts[0])) continue;
    const col = b.style === 0 ? [90, 110, 170] : b.style === 3 ? [180, 170, 150] : [150, 150, 160];
    for (let i = 0; i < b.pts.length; i++) { const p = b.pts[i], q = b.pts[(i + 1) % b.pts.length]; line(px(p[0]), py(p[1]), px(q[0]), py(q[1]), 1, ...col); }
  }
  for (const w of waters) {
    const pts = w.pts;
    for (let i = 1; i < pts.length; i++) if (inView(pts[i])) line(px(pts[i - 1][0]), py(pts[i - 1][1]), px(pts[i][0]), py(pts[i][1]), Math.max(1, w.widthM * scale), 120, 170, 220);
    if (w.lake) for (let i = 0; i < pts.length; i++) { const q = pts[(i + 1) % pts.length]; if (inView(pts[i])) line(px(pts[i][0]), py(pts[i][1]), px(q[0]), py(q[1]), 1, 120, 170, 220); }
  }
  const CLS_COLOR = { 5: [230, 70, 40], 4: [240, 140, 0], 3: [230, 180, 0], 2: [110, 110, 110], 1: [160, 160, 160], 0: [200, 200, 200] };
  for (const e of edges) {
    if (!inView(e.pts[0]) && !inView(e.pts[e.pts.length - 1])) continue;
    const wy = e.way;
    const col = wy.bridge ? [120, 40, 160] : wy.link ? [80, 170, 90] : CLS_COLOR[wy.rank];
    const wpx = Math.max(1, (wy.lanes * LANE_M + wy.shl + wy.shr) * scale);
    for (let i = 1; i < e.pts.length; i++) line(px(e.pts[i - 1][0]), py(e.pts[i - 1][1]), px(e.pts[i][0]), py(e.pts[i][1]), wpx, ...col);
  }
  for (const c of crossings) if (inView([c.x, c.z])) line(px(c.x) - 2, py(c.z), px(c.x) + 2, py(c.z), 3, 200, 0, 200);
  for (const r of routes) for (let i = 1; i < r.coarse.length; i++) if (inView(r.coarse[i])) line(px(r.coarse[i - 1][0]), py(r.coarse[i - 1][1]), px(r.coarse[i][0]), py(r.coarse[i][1]), 2, 0, 200, 255);
  png(S, S, rgb, join(HERE, `charlotte_${name}.png`));
}
plot('city', 0, 0, 18000, 1800);
plot('uptown', uptownX, uptownZ, 1500, 1500);
plot('core', uptownX, uptownZ, 4500, 1800);
{
  // the busiest interchange: the crossing cluster with the most crossings in 300 m
  let best = null, bn = 0;
  for (let i = 0; i < crossings.length; i += 7) {
    const c = crossings[i];
    let n = 0;
    for (const d of crossings) if (Math.abs(d.x - c.x) < 300 && Math.abs(d.z - c.z) < 300) n++;
    if (n > bn) { bn = n; best = c; }
  }
  if (best) plot('interchange', best.x, best.z, 500, 1200);
}
console.log('wrote debug PNGs');
