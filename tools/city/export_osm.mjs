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
//   * REAL GROUND. A 60 m height grid over the whole beltway: every node the
//     mean of the USGS 3DEP 1/3 arc-second bare earth over its 60 m cell
//     (WP-04), with no filter after it. Until WP-04 it was the AWS "skadi"
//     1" tiles run through an opening, a closing and a blur written to take
//     out radar "roofs" that were never there; that took out 66% of the
//     core's relief instead.
//   * REAL WATER. The creeks are the county's surveyed centrelines
//     (Mecklenburg Creeks_Streams) inside Mecklenburg and USGS 3DHP flowlines
//     outside it, the lakes the county's and 3DHP's water bodies, each creek
//     with its BED sampled from 3DEP every 20 m (lib/water.mjs, WP-04b). They
//     replace RG2's hand-traced water.
//   * REAL BUILDINGS in the core: 35k footprints with heights, so the skyline
//     is Charlotte's and the neighbourhoods are the neighbourhoods.
//   * THE THREE RACE ROUTES (Uptown Loop, Tryon Sprint, Independence Sprint)
//     as edge sequences through this same graph, so a race and free roam are
//     the same streets — the CLT stage bakes are retired.
//
// Sources (read-only). Every one, with its licence, is in the registry
// tools/city/SOURCES.md, whose Credits table this reads for the attribution
// it writes into the data (and refuses to run without):
//   tools/city/cache/ways_all.json      arterials + ramps, beltway bbox (OSM)
//   tools/city/cache/nodes_all.json     signal / stop / yield nodes (OSM)
//   tools/city/cache/streets_core.json  residential/unclassified in the core (OSM)
//   tools/city/cache/buildings_core.json footprints in the core (OSM)
//   tools/city/cache/3dep/box13.f32     USGS 3DEP 1/3" over the DEM box (public
//                                       domain; fetch/fetch_3dep.mjs; or
//                                       %PSX_GIS_DIR%\3dep)
//   tools/city/cache/water/*.geojson    creeks, lakes and the county line
//                                       (fetch/fetch_water.mjs; CC0 and
//                                       public domain, see SOURCES.md)
//   tools/city/cache/bridges_mm.json    man_made=bridge outlines (OSM; B1)
//   tools/city/cache/layers/culverts.json  tunnel=culvert waterways (OSM;
//                                       fetch/fetch_layers.mjs; B1, owner Q8)
//   tools/city/deckpairs_overrides.json twin-deck FORCE / NEVER by way pair (B1)
// Every input's size, sha256 and Overpass snapshot time is recorded in
// tools/city/cache_manifest.json; --check verifies them. (RG2's traced water,
// vendored in tools/city/vendor/rg2/ by WP-02, is no longer read: WP-04b.)
//
// Outputs (the four files the game loads from Resources; the layouts are in
// tools/city/lib/citydata.mjs, which reads them the way the game does):
//   charlotte_city.bytes    PSXC v2: a section table, then META NODE NAME EDGE
//                           PNTS WATR WBED XING SPAN ROUT and GHSH, the graph
//                           hash (WBED, the creek beds, since WP-04b), then
//                           LANW TAPR PARA TAGN (WP-10), SPLT (WP-11) and BRST
//                           (the roads pass's B1: bridge outlines, culvert creeks)
//   charlotte_dem.bytes     PDEM v3: the 30 m height grid in delta-coded
//                           blocks (lib/pdem3.mjs), datum pinned at 97.0 m
//   charlotte_bld.bytes     PBLD v1: footprints
//   charlotte_routes.json   the menu's copy of the routes
//   tools/city/charlotte_*.png   debug plots (with --out only; gitignored)
//
// Run (it never writes into Resources unless told to):
//   node tools/city/export_osm.mjs --check
//       export in memory and compare byte for byte with the shipped files
//       in Assets/PSXRacing/Resources (or --against <dir>); also checks the
//       inputs against cache_manifest.json and the result against
//       tools/city/fingerprint.json. Exit 0 only when all of it matches.
//   node tools/city/export_osm.mjs --out Assets/PSXRacing/Resources --fingerprint tools/city/fingerprint.json
//       write the four files (to any directory), and the fingerprint.
//       --no-plots skips the debug PNGs (tools/city/determinism.mjs uses it).
//   node tools/city/export_osm.mjs --manifest
//       record the current inputs in cache_manifest.json (after a re-fetch).
//   node tools/city/determinism.mjs
//       export twice, in two processes, and compare every file's sha256.
// Node is pinned in tools/city/package.json (engines): V8's trig is not
// bit-stable across releases, so after a Node change compare the
// fingerprint, not the bytes.
//
// Frame: plain equirectangular about RG2's fixture centre (the I-485 centroid),
// x east, z north, metres — the same frame every previous bake registered
// into, so nothing that stored a city coordinate moves.

import { readFileSync, writeFileSync, mkdirSync, existsSync, statSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { dirname, join, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { load3dep } from './lib/dem3dep.mjs';
import { buildWaters, waterInputPaths } from './lib/water.mjs';
import { parseCity, parseDem, parseBld, fingerprint, graphHash, hashHex, CITY_SECTIONS, profileFor, tierOf } from './lib/citydata.mjs';
import { loadOutlines, outlineIndex, structIdOf, outlineName } from './lib/bridges.mjs';
import { loadCulvertLines, ribbonMeet, arcOf, bedAt, creekFlatHalf, CULVERT_NEAR_M, CULVERT_RIBBON_M, CULVERT_ALONG_M } from './lib/culverts.mjs';
import { readCredits } from './lib/sources.mjs';
import { lineClean, LANE_W, LANE_W_LINK } from './lib/lineclean.mjs';
import { buildLineSets, lineTagsOf, CENTRE_TWLTL } from './lib/lineset.mjs';
import { readSmoothRules } from './lib/smoothrules.mjs';
import { findSplits } from './lib/splits.mjs';
import { encodePdem3 } from './lib/pdem3.mjs';
import { buildLots } from './lib/lots.mjs';
import { buildRoadProfiles, writeRprf } from './lib/roadprofile.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const CACHE = join(HERE, 'cache');
const SOURCES = join(HERE, 'SOURCES.md');
const RES = join(UNITY, 'Assets', 'PSXRacing', 'Resources');
const MANIFEST = join(HERE, 'cache_manifest.json');
const FINGERPRINT = join(HERE, 'fingerprint.json');

// ------------------------------------------------------------------ mode
const ARGS = process.argv.slice(2);
function argVal(name) {
  const i = ARGS.indexOf(name);
  if (i < 0) return null;
  const v = ARGS[i + 1];
  if (!v || v.startsWith('--')) throw new Error(`${name} needs a value`);
  return v;
}
const MODE = {
  check: ARGS.includes('--check'),
  out: argVal('--out'),
  against: argVal('--against') || RES,
  fingerprint: argVal('--fingerprint'),
  manifest: ARGS.includes('--manifest'),
  plots: !ARGS.includes('--no-plots'),
  // --rprf (leftover item 1, 2026-10-03): write section RPRF, the measured
  // road profiles. OFF by default: the item failed its launch gate (Docs/
  // CHARLOTTE.md "HEIGHTS"), so the shipped data carries no RPRF and the game
  // solves from the smoothed land as before.
  rprf: ARGS.includes('--rprf'),
};
if (!MODE.check && !MODE.out && !MODE.manifest) {
  console.error('export_osm.mjs writes nothing unless told where. Use one of:\n' +
    '  --check                     export in memory, compare with the shipped files (exit 1 on any difference)\n' +
    '  --out <dir> [--fingerprint <file>]   write the four data files into <dir>\n' +
    '  --manifest                  record the current inputs in tools/city/cache_manifest.json\n' +
    'e.g. node tools/city/export_osm.mjs --out Assets/PSXRacing/Resources --fingerprint tools/city/fingerprint.json');
  process.exit(2);
}
{
  // Node is pinned for byte-identical output (see the header).
  try {
    const want = JSON.parse(readFileSync(join(HERE, 'package.json'), 'utf8')).engines?.node;
    if (want && process.version.replace(/^v/, '') !== want.replace(/^v/, ''))
      console.log(`NOTE: Node ${process.version}, but tools/city/package.json pins ${want}: bytes may differ; compare the fingerprint.`);
  } catch { /* no package.json: nothing pinned */ }
}
/// The finished files, by name, written or compared at the end.
const OUTPUTS = new Map();
const emit = (name, bytes) => OUTPUTS.set(name, Buffer.isBuffer(bytes) ? bytes : Buffer.from(bytes));

// --------------------------------------------------------------- constants
const LANE_M = 3.6576;                 // 12 ft, the section currency (never scaled)
const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;   // RG2 fixture centre
const M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const XDEDUP_M = 8;                    // two crossings of one pair closer than this are one
const BANK_M = 6;                      // dry bank either side of water under a deck
/// The creek beds (WBED) are stored as u16 centimetres above the datum.
const BED_UNITS = 100;
/// Metres per height sample: 30 since WP-13 (60 from WP-04). The roads still
/// read a 60 m grid (CityElevation.BuildRoadDem rebuilds it from these nodes),
/// so the 30 m nodes are the 60 m lattice's nodes and the midpoints between.
const DEM_CELL = 30;
const ROAD_GRID_CELL = 60;
const DEM_MARGIN = 1500;
/// THE DATUM, metres above sea level: world y = 0. PINNED (WP-02). It was
/// floor(lowest grid height) - 2, which is 97 on today's grid (lowest 99.2 m,
/// the Pineville Quarry) but would move with any new ground - and every world
/// y, every solved road height and anything stored against one with it. A
/// new ground source must fit ABOVE it: see DEM_CLAMP.
const DEM_BASE = 97.0;
/// The grid stores u16 steps of DEM_SCALE metres above the datum: decimetres,
/// 0..6553.5 m. Written by multiplying by DEM_UNITS (never dividing by the
/// scale: x / 0.1 and x * 10 round differently at the half-steps).
const DEM_UNITS = 10;
const DEM_SCALE = 1 / DEM_UNITS;
/// THE PIT CLAMP (plan critic C9). The lowest the ground may go is
/// DEM_FLOOR; a cell below it is clamped up to it only inside one of these
/// named boxes, and the export FAILS anywhere else - a u16 below the datum
/// would otherwise wrap to a 6.5 km spike, or be silently flattened. The two
/// boxes are the quarry pits USGS 3DEP puts below 97.5 m: the Pineville
/// Quarry floor (69.1 m in the 1/3" DEM) and the Arrowood Quarry (89.5 m on
/// the 30 m grid), so a pit loses up to ~28 m of depth on the 30 m grid
/// (WP-04; the skadi grid before it never went below 99.2 m).
/// Keyed by the OpenStreetMap way that outlines each pit (ODbL); boxes are
/// [south, west, north, east] in degrees, ~100 m beyond the cells.
const DEM_FLOOR = DEM_BASE + 0.5;
const DEM_CLAMP = [
  { name: 'Pineville Quarry', osmWay: 246229422, box: [35.1178, -80.9004, 35.1250, -80.8943] },
  { name: 'Arrowood Quarry', osmWay: 246229420, box: [35.1030, -80.9248, 35.1106, -80.9190] },
];

/// The credit lines of tools/city/SOURCES.md's Credits table, in order: the
/// attribution this export writes into the data (lib/sources.mjs reads it; a
/// missing table or row is an error, not a silent fallback).
const CREDITS = readCredits(SOURCES);
const ATTRIBUTION = CREDITS.map(r => r.credit).join('\n');
console.log(`credits (tools/city/SOURCES.md): ${CREDITS.map(r => r.id).join(', ')}`);

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
// The Overpass cache is on the machine that fetched it only (gitignored).
// There is no fallback: until WP-02 a missing cache silently read RG2's
// older snapshot instead, which is a different graph.
const wayFile = join(CACHE, 'ways_all.json');
const nodeFile = join(CACHE, 'nodes_all.json');
for (const f of [wayFile, nodeFile])
  if (!existsSync(f)) throw new Error(`${relative(UNITY, f)} is missing: fetch it with tools/city/fetch/fetch_ways.mjs (its snapshot is recorded in cache_manifest.json)`);
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
let tollKept = 0, tollDropped = 0;
/// The toll roads (the I-77 and I-485 Express Lanes, the Monroe Expressway,
/// their ramps: 177 ways). The owner wants them (Q3, present day) and WP-10's
/// PARA pass moves the express lanes clear of the general lanes (93 -> 1 km
/// of pair short of its gap). HELD BACK for now: kept, the first city audit
/// failed on them alone - their layer=1 connector flyovers over I-485 clear
/// the general lanes by -0.55 m, an I-77 connector climbs 39% into a 26 m
/// bridge (35.3759,-80.8470) and a Monroe Expressway ramp crosses
/// Independence Boulevard at grade - elevation-solver work, not lines. Flip
/// this with that fix (tools/city/lib/lineclean.mjs already handles them):
/// the plan's WP-10t, right after WP-11b ships.
const KEEP_TOLL = false;
const tollNodes = new Set();   // every node a dropped toll way touched
function keepWay(w, minor) {
  if (seenWay.has(w.id)) return;
  const t = w.tags;
  const hw = t.highway || '';
  if (t.area === 'yes') return;
  const link = hw.endsWith('_link');
  const base = link ? hw.slice(0, -5) : hw;
  if (!(base in CLS_RANK)) return;
  // TOLL ROADS (owner Q3, 2026-09-28: present day; KEEP_TOLL above). OSM
  // maps the express lanes about 5.5 m from the general lanes, so the two
  // ribbons overlapped and were squeezed into one slalom; WP-10's PARA pass
  // (lib/lineclean.mjs) moves them apart to a real gap when they are kept.
  if (t.toll === 'yes') {
    if (!KEEP_TOLL) { tollDropped++; for (const id of w.nodes) tollNodes.add(id); return; }
    tollKept++;
  }
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
  // What WP-10's lane rules read (lib/lineclean.mjs): whether the count was
  // TAGGED, the per-direction counts, and turn:lanes (which side a lane
  // opens on). A reversed one-way (oneway=-1) reads its backward tags.
  const tagged = Number.isFinite(parseInt(t.lanes, 10)) || (Number.isFinite(lf) && Number.isFinite(lb));
  const rev = onewayOf(t) === -1;
  ways.push({
    id: w.id, base, link, rank: CLS_RANK[base], oneway, lanes, turn, shl, shr,
    bridge, tunnel, level, name, speed: parseSpeedKmh(t),
    roundabout: t.junction === 'roundabout' || t.junction === 'circular',
    toll: t.toll === 'yes', tagged,
    lf: Number.isFinite(lf) ? lf : NaN, lb: Number.isFinite(lb) ? lb : NaN,
    tl: rev ? (t['turn:lanes:backward'] || t['turn:lanes']) : t['turn:lanes'],
    tlf: t['turn:lanes:forward'], tlb: t['turn:lanes:backward'],
    // the line set's tag facts (roads pass L2, lib/lineset.mjs)
    lt: lineTagsOf(t, base, rev),
    nodes, geom,
  });
}
for (const w of rawWays) keepWay(w, false);
for (const w of rawMinor) keepWay(w, true);
console.log(KEEP_TOLL ? `kept ${ways.length} ways (${tollKept} of them toll: the express lanes and the Monroe Expressway, kept - owner Q3)`
                      : `kept ${ways.length} ways (dropped ${tollDropped} toll ways: held back, see KEEP_TOLL)`);
// A slip ramp that only ever led to a dropped toll lane now ends in mid-air:
// an untolled link whose free end is a node only the toll lanes shared. Drop
// those too, and keep dropping until no ramp leads nowhere (a two-piece ramp
// loses its far piece first, then its near one).
if (!KEEP_TOLL) {
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

// ------------------------------------------------- WP-10: the line clean-up
// Tagged nodes, lane-count data, simplify, doglegs, TAPR, PARA: the rules and
// their reasons are in lib/lineclean.mjs. Everything after this (crossings,
// water spans, routes, the plots) reads the cleaned lines.
const uptownXY = [toX(-80.8431), toZ(35.2271)];
// WP-11's fillet numbers are the smoothness gate's (Editor/SmoothRules.cs):
// R_min by class (B3), the inner-edge floor, the 2 cm sagitta and the chord cap
const SR = readSmoothRules(join(UNITY, 'Assets', 'PSXRacing', 'Editor', 'SmoothRules.cs'));
const LC = lineClean({ ways, edges, nodes, rawWays: [...rawWays, ...rawMinor],
                       rawNodes, toX, toZ, uptown: uptownXY,
                       fillet: { rMinFor: SR.rMinFor, innerEdgeMinR: SR.InnerEdgeMinRM, eps: SR.DensifyEpsM,
                                 chordCap: SR.ChordCapM, collinearDeg: SR.CollinearDeg } });
hashSegs();
const SPLITS = findSplits(edges, nodes);
// THE LINE SET (roads pass L2): what MUTCD paints on every edge
const LSET = buildLineSets(edges, profileFor, LC.tapr);
{
  const km = l => (l.reduce((a, x) => a + x.len, 0) / 1000).toFixed(1);
  const tw = LSET.rows.filter((r, i) => !edges[i].way.oneway);
  const unevenE = edges.filter((e, i) => !e.way.oneway && LSET.rows[i].nF !== LSET.rows[i].nB);
  const twltlE = edges.filter((e, i) => LSET.rows[i].centre === CENTRE_TWLTL);
  const unmarkedE = edges.filter((e, i) => !(LSET.rows[i].flags & 1));
  console.log(`L2 line set: ${tw.length} two-way edges; TWLTL on ${km(twltlE)} km (${km(LSET.noEvidence)} km without tag evidence: ${LSET.noEvidence.length} edges, listed with PSX_LSET_DIAG); uneven splits ${km(unevenE)} km on ${unevenE.length} edges; unmarked ${km(unmarkedE)} km (${unmarkedE.length} edges)`);
  if (process.env.PSX_LSET_DIAG) for (const r of LSET.noEvidence) console.log(`  TWLTL no evidence: e${r.edge} way ${r.way} ${r.why} ${r.len.toFixed(0)} m`);
}
{
  const st = LC.stats;
  console.log(`WP-10 lines: ${st.points.before} -> ${st.points.after_simplify} points after collapse + Douglas-Peucker 0.5 m (${st.pinned_vertices} shared raw vertices pinned; first piece held at ${st.simplify_held.taper_ends} lane-change edges, ${st.simplify_held.structure} decks/tunnels left alone); ` +
              `doglegs ${st.doglegs_raw.all} (${st.doglegs_raw.jog_ge_1m} jog >= 1 m) -> ${st.doglegs_after.all} (fixed ${st.doglegs_fixed.all}, worst jog ${st.doglegs_fixed.worst_jog_m} m)`);
  console.log(`WP-10 lanes: flickers ${st.lane_fixes.flicker} (${st.lane_fix_km.flicker} km), short pieces ${st.lane_fixes.short} (${st.lane_fix_km.short} km), inferred ${st.lane_fixes.inferred} (${st.lane_fix_km.inferred} km)`);
  console.log(`WP-10 lanes: ${st.lane_kept_tagged} short tagged pieces kept (turn bays, drawn one-sided); edges drawn below their own lanes= tag: ${st.lane_below_tag}`);
  console.log(`WP-10 TAPR: ${st.tapr.transitions} lane-count changes, ${st.tapr.oneSided} one-sided, ${st.tapr.twoSided} two-sided, ${st.tapr.tagged} side from tags (L4: ${st.tapr.perDirection} two-way changes per direction - each on its own outside, Q4 - from the line set's split; ${st.tapr.mergeSide} one-way sides from a merging/diverging branch, ${st.tapr.mergeSideLeft} of them left), ${st.tapr.bays} turn bays, ${st.tapr.atJunction} full width at a junction mouth, ${st.tapr.shortened} fitted to the chain, ${st.tapr.absorbed} absorbed (no room), ${st.tapr.reanchored} re-anchored at a junction (> 1 lane off; ${st.tapr.shifts} SHIFT records ease them back past it, L4), ${st.tapr.recentred} untagged junction-mouth lanes opened on the re-centring side`);
  console.log(`WP-10 PARA: before ${JSON.stringify(st.para_before)}; after ${JSON.stringify(st.para_after)}; moved ${st.para_moved.chains} chains (max ${st.para_moved.max_offset_m} m)`);
  {
    const rv = LC.review.filter(r => r.kind === 'para');
    const core = rv.filter(r => Math.abs(r.x - uptownXY[0]) <= 4000 && Math.abs(r.z - uptownXY[1]) <= 4000);
    console.log(`WP-10 PARA review list: ${rv.length} pairs city-wide, ${core.length} in the 8 x 8 km core (${core.reduce((a, r) => a + r.metres, 0).toFixed(0)} m); untagged bridges kept at the default: ${LC.review.filter(r => r.kind === 'untagged-bridge').length}`);
  }
  if (process.env.PSX_LC_DIAG) {
    const kindOf = (E, F) => E.way.link || F.way.link ? 'ramp' : !!E.way.toll !== !!F.way.toll ? 'express' : E.way.oneway && F.way.oneway && E.way.name && E.way.name === F.way.name ? 'divided' : 'other';
    const rows = (defs, tag) => {
      const agg = {};
      for (const d of defs) { const k = (d.exempt ? 'exempt-' : '') + kindOf(d.E, d.F); agg[k] = (agg[k] || 0) + 2.5; }
      console.log(tag, JSON.stringify(Object.fromEntries(Object.entries(agg).map(([k, v]) => [k, +(v / 1000).toFixed(1)]))));
    };
    rows(LC.para.defs0, 'PARA km before by kind');
    rows(LC.para.defs, 'PARA km after by kind');
    const top = [...LC.para.moved].sort((a, b) => b.umax - a.umax).slice(0, 12);
    for (const m of top) { const e = edges[m.edges[0]]; const P = e.pts[e.pts.length >> 1]; console.log('moved', m.umax.toFixed(1), 'm', e.way.name || 'ramp', e.way.id, toLat(P[1]).toFixed(5), toLon(P[0]).toFixed(5), m.edges.length, 'edges'); }
    writeFileSync(process.env.PSX_LC_DIAG, JSON.stringify(LC.para.pairs.map(p => ({ ...p, kind: kindOf(edges[p.e1], edges[p.e2]), n1: edges[p.e1].way.name, n2: edges[p.e2].way.name, w1: edges[p.e1].way.id, w2: edges[p.e2].way.id, lat: toLat(p.z), lon: toLon(p.x) })).sort((a, b) => b.n - a.n)));
  }
  if (st.fillet) {
    const f = st.fillet;
    console.log(`WP-11 fillets: ${f.strands} strands (${f.closed} closed loops held at one node), ${f.vertices} vertices, ${f.filleted} filleted, ${f.merged} short-tangent pairs fitted as one curve; ${f.junctionsFilleted} mitred junctions filleted across (${f.armsFollowed} arms followed their node); ` +
                `${f.tight} under their class floor (listed), ${f.overEmax} past their class Emax (listed); 2-arm nodes moved ${f.nodesMoved} (median ${f.nodeMoveMedian} m, max ${f.nodeMoveMax} m); ` +
                `points ${f.pointsBefore} -> ${f.pointsAfter}; edge ends off their node before: ${st.fillet_end_gap_m} m`);
    console.log(`WP-11 fillets (review 2026-09-30): ${f.lone} lone vertices left as vertices (net turn within 10 m under 8 eps / 10 m), ${f.held} near-U-turn nodes held (bend slab, not a fold), ${f.oneWayClash} one-way pairs meeting nose to nose not run across; free vertices still under half width + ${SR.InnerEdgeMinRM} m: ${f.foldFree} (listed)`);
  }
  console.log(`WP-11 C11 split nodes (an undivided road opening into its two carriageways; section SPLT for the line model): ${SPLITS.length}`);
  console.log(`WP-10 tagged nodes: ${st.tagged_nodes} recorded on the raw ways, ${st.tagged_projected.nodes} projected (max move ${st.tagged_projected.moved_max_m} m)`);
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
/// Two nodes joined by one edge under 50 m.
const shortLinks = new Set();
for (const e of edges) if (e.len < 50) { shortLinks.add(e.a + ':' + e.b); shortLinks.add(e.b + ':' + e.a); }
const shortLink = (a, b) => shortLinks.has(a + ':' + b);
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
      // Two edges at the SAME level that share a node and cross within 150 m
      // of it (150 m: a long ramp taper) are one merging into the other:
      // their ribbons meet in a gore or a clip, never on a bridge. The mapped
      // lines touch there, and WP-10's simplify and PARA can leave them
      // crossing a few metres off the node - a "grade separation" the solver
      // then lifted (a 70 m I-485 node, 22 of them on the first run).
      // (or a node one short edge away: a ramp leaving just before the
      // carriageway it runs beside was split by a junction)
      if (ea.way.level === eb.way.level) {
        const near = [...shared];
        for (const na of [ea.a, ea.b]) for (const nb of [eb.a, eb.b])
          if (na !== nb && shortLink(na, nb)) near.push(na, nb);
        if (near.some(nn => Math.hypot(nodes[nn][0] - hit[0], nodes[nn][1] - hit[1]) < 150)) continue;
      }
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
// The box the ground grid covers (every road point plus DEM_MARGIN): the
// DEM below and the water both use it.
let bbox = { x0: 1e18, x1: -1e18, z0: 1e18, z1: -1e18 };
for (const e of edges) for (const p of e.pts) {
  bbox.x0 = Math.min(bbox.x0, p[0]); bbox.x1 = Math.max(bbox.x1, p[0]);
  bbox.z0 = Math.min(bbox.z0, p[1]); bbox.z1 = Math.max(bbox.z1, p[1]);
}
bbox = { x0: bbox.x0 - DEM_MARGIN, x1: bbox.x1 + DEM_MARGIN, z0: bbox.z0 - DEM_MARGIN, z1: bbox.z1 + DEM_MARGIN };
/// USGS 3DEP 1/3" over the box (lib/dem3dep.mjs): the ground and the beds.
const dem3 = load3dep();
console.log(`3DEP: ${relative(UNITY, dem3.f32Path)} (${dem3.meta.cols} x ${dem3.meta.rows} px, sha256 ${dem3.meta.sha256.slice(0, 12)})`);

// Creeks and lakes from open data with their beds from 3DEP (WP-04b; the
// rules are in lib/water.mjs). Each water is { name, widthM, lake, pts, bed }.
const waters = buildWaters({ cacheDir: CACHE, dem3, toX, toZ, toLat, toLon, box: bbox }).waters;
// WP-25: a POND never touches a road, so no pond makes a water span (the
// owner's rule gives every road over water a bridge; a retention pond is
// not what a road crosses): one within POND_ROAD_CLEAR_M of any road's
// pavement, or with a road's point inside it, is left out. So is one PERCHED
// over a road: a road within POND_PERCH_M of its shore whose ground (3DEP at
// the road's line) is under the pond's level plus POND_PERCH_DY - the road's
// cut would drain it, and the water would hang over the slope beside the road.
// The solve can cut a road below 3DEP's ground by a hand or two, so the
// margin is a metre (the runtime audit judges the solved road at +0.3 m).
{
  const POND_ROAD_CLEAR_M = 4, POND_PERCH_M = 16, POND_PERCH_DY = 1.0, C = 64;
  const cells = new Map();
  let maxHw = 0;
  for (const e of edges) {
    // the piece's own lane count after WP-10's clean-up (e.lanes), at the
    // 3.6576 m lanes the game still draws
    const hw = (e.lanes * LANE_M + e.way.shl + e.way.shr) / 2;
    maxHw = Math.max(maxHw, hw);
    for (let i = 1; i < e.pts.length; i++) {
      const [ax, az] = e.pts[i - 1], [bx, bz] = e.pts[i];
      for (let cx = Math.floor(Math.min(ax, bx) / C); cx <= Math.floor(Math.max(ax, bx) / C); cx++)
        for (let cz = Math.floor(Math.min(az, bz) / C); cz <= Math.floor(Math.max(az, bz) / C); cz++) {
          const k = cx * 100003 + cz;
          let l = cells.get(k); if (!l) cells.set(k, l = []);
          l.push([ax, az, bx, bz, hw]);
        }
    }
  }
  const ptSeg = (px, pz, ax, az, bx, bz) => {
    const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
    const t = L2 > 0 ? Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / L2)) : 0;
    return Math.hypot(px - ax - dx * t, pz - az - dz * t);
  };
  const segSeg = (a, b, c, d) => segX(a[0], a[1], b[0], b[1], c[0], c[1], d[0], d[1]) ? 0 :
    Math.min(ptSeg(a[0], a[1], c[0], c[1], d[0], d[1]), ptSeg(b[0], b[1], c[0], c[1], d[0], d[1]),
             ptSeg(c[0], c[1], a[0], a[1], b[0], b[1]), ptSeg(d[0], d[1], a[0], a[1], b[0], b[1]));
  const groundASL = (x, z) => dem3.sample(toLat(z), toLon(x));
  // 'near' (within the clearance), 'perched' (over a road nearby), or null
  const nearRoad = w => {
    const pts = w.pts, reach = maxHw + POND_PERCH_M;
    let perched = false;
    for (let i = 0; i < pts.length; i++) {
      const a = pts[i], b = pts[(i + 1) % pts.length];
      for (let cx = Math.floor((Math.min(a[0], b[0]) - reach) / C); cx <= Math.floor((Math.max(a[0], b[0]) + reach) / C); cx++)
        for (let cz = Math.floor((Math.min(a[1], b[1]) - reach) / C); cz <= Math.floor((Math.max(a[1], b[1]) + reach) / C); cz++)
          for (const [ax, az, bx, bz, hw] of cells.get(cx * 100003 + cz) || []) {
            const d = segSeg(a, b, [ax, az], [bx, bz]);
            if (d < hw + POND_ROAD_CLEAR_M) return 'near';
            if (pointInPoly(pts, ax, az) || pointInPoly(pts, bx, bz)) return 'near';
            if (!perched && d < hw + POND_PERCH_M) {
              // the road's line at the foot of this shore point
              const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
              const t = L2 > 0 ? Math.max(0, Math.min(1, ((a[0] - ax) * dx + (a[1] - az) * dz) / L2)) : 0;
              const x = ax + dx * t, z = az + dz * t;
              if (Math.hypot(a[0] - x, a[1] - z) < hw + POND_PERCH_M && groundASL(x, z) < w.level + POND_PERCH_DY) perched = true;
            }
          }
    }
    return perched ? 'perched' : null;
  };
  let kept = 0, near = 0, perched = 0;
  for (let i = waters.length - 1; i >= 0; i--) {
    if (!waters[i].pond) continue;
    const why = nearRoad(waters[i]);
    if (why) { waters.splice(i, 1); if (why === 'near') near++; else perched++; } else kept++;
  }
  console.log(`ponds: ${kept} kept; left out ${near} within ${POND_ROAD_CLEAR_M} m of a road's pavement, ${perched} perched over a road within ${POND_PERCH_M} m`);
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
/// Every road crossing of a creek line (plan B2): { e, s, half, wi, ws, x, z, sin }
/// - the edge and arc, the deck's half length, the water and its arc, the
/// point, the sine of the crossing angle. Section CULV and the culvert
/// decisions below read it.
const creekX = [];
/// The culverts (owner Q8; lib/culverts.mjs): the pipes for section CULV, and
/// every candidate's decision for tools/city/baseline/culverts_q8.csv.
let CULV = { xs: [], rows: [] };
{
  // water segments hashed so 40k edges do not each walk 30 creeks
  const wHash = new Map();
  waters.forEach((w, wi) => {
    // a ravine carries no water sheet and a road over it keeps its
    // embankment (a culvert, WP-25): only creeks and lakes make spans
    if (w.lake || w.ravine) return;
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
  const waterAcc = waters.map(w => w.lake ? null : arcOf(w.pts));
  const perEdge = [];
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
          const s = acc[i - 1] + Math.hypot(p[0] - ax, p[1] - az);
          const half = w.widthM / 2 + BANK_M;
          // the crossing, for the culvert decisions (plan B2)
          const cdx = w.pts[j][0] - w.pts[j - 1][0], cdz = w.pts[j][1] - w.pts[j - 1][1], rdx = bx - ax, rdz = bz - az;
          const sin = Math.abs(rdx * cdz - rdz * cdx) / Math.max(1e-9, Math.hypot(rdx, rdz) * Math.hypot(cdx, cdz));
          const ws = waterAcc[wi][j - 1] + Math.hypot(p[0] - w.pts[j - 1][0], p[1] - w.pts[j - 1][1]);
          creekX.push({ e: e.id, s, half, wi, ws, x: p[0], z: p[1], sin, acc });
          spans.push([s - half, s + half, creekX.length - 1]);
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
    perEdge.push({ e, acc, spans });
  }

  // ---- THE CULVERTS (owner Q8, plan B2; the rules: lib/culverts.mjs) ----
  const culLines = loadCulvertLines(join(CACHE, 'layers', 'culverts.json'), toX, toZ);
  if (!culLines) throw new Error('tools/city/cache/layers/culverts.json is missing: fetch it with tools/city/fetch/fetch_layers.mjs');
  const hwOf = e => (e.lanes * LANE_M + e.way.shl + e.way.shr) / 2 + 1;
  const conv = new Map();          // creekX index -> { way, dRib, dP, at }
  const rowOf = new Map();         // creekX index -> its candidate row
  const why = (xi, w) => { conv.delete(xi); rowOf.get(xi).why = w; };
  creekX.forEach((X, xi) => {
    const near = culLines.near(X.x, X.z, CULVERT_NEAR_M);
    if (!near.length) return;
    const e = edges[X.e], wy = e.way;
    const row = { xi, edge: X.e, way: wy.id, rank: wy.rank, link: wy.link, name: wy.name, water: waters[X.wi].name, culvertWay: near[0].w.id, culvertName: near[0].w.name,
                  dP: near[0].d, dRib: NaN, alongOff: NaN, x: X.x, z: X.z, why: '' };
    rowOf.set(xi, row); CULV.rows.push(row);
    if (wy.bridge) { row.why = 'the road is tagged bridge=yes (OSM says a bridge)'; return; }
    if (wy.tunnel) { row.why = 'the road is a tunnel'; return; }
    let best = null, closest = null;
    for (const { w, d } of near) {
      const m = ribbonMeet(w.pts, e.pts, X.acc, X.s, hwOf(e), CULVERT_ALONG_M);
      if (!closest || m.dRib < closest.m.dRib) closest = { w, d, m };
      if (m.dRib <= CULVERT_RIBBON_M && (!best || m.dRib < best.m.dRib - 1e-6)) best = { w, d, m };
    }
    if (!best) {
      row.dRib = closest.m.dRib;
      row.why = Number.isFinite(closest.m.dRib)
        ? `the culvert line (way ${closest.w.id}) passes ${closest.m.dRib.toFixed(1)} m outside the road's ribbon within ${CULVERT_ALONG_M} m of the creek (needs ${CULVERT_RIBBON_M} m)`
        : `the culvert line (way ${closest.w.id}) does not reach the road within ${CULVERT_ALONG_M} m of the creek`;
      return;
    }
    Object.assign(row, { culvertWay: best.w.id, culvertName: best.w.name, dP: best.d, dRib: best.m.dRib, alongOff: best.m.at - X.s });
    conv.set(xi, true);
  });
  // THE SAME FILL: a crossing of the same creek within CULVERT_SHARED_M of a
  // piped one is on that pipe's embankment too (a ramp seated on its
  // mainline, a divided road's other carriageway: I-85's ramp e9018 kept a
  // deck beside the piped mainline it rides on, and the deck stood on the
  // ground) - unless OSM tags it a bridge, or it is a tunnel
  const CULVERT_SHARED_M = 25;
  for (let pass = 0; pass < 10; pass++) {
    let added = 0;
    creekX.forEach((Y, yi) => {
      if (conv.has(yi)) return;
      const e = edges[Y.e], wy = e.way;
      if (wy.bridge || wy.tunnel) return;
      for (const xi of conv.keys()) {
        const X = creekX[xi];
        if (X.wi !== Y.wi || Math.hypot(X.x - Y.x, X.z - Y.z) > CULVERT_SHARED_M) continue;
        let row = rowOf.get(yi);
        if (!row) {
          row = { xi: yi, edge: Y.e, way: wy.id, rank: wy.rank, link: wy.link, name: wy.name, water: waters[Y.wi].name, culvertWay: 0, culvertName: '',
                  dP: NaN, dRib: NaN, alongOff: NaN, x: Y.x, z: Y.z, why: '' };
          rowOf.set(yi, row); CULV.rows.push(row);
        }
        Object.assign(row, { culvertWay: rowOf.get(xi).culvertWay, culvertName: rowOf.get(xi).culvertName, why: '', shared: X.e });
        conv.set(yi, true); added++;
        return;
      }
    });
    if (!added) break;
  }
  // (a) the deck would hold other water too: it stays a deck
  for (let pass = 0; pass < 20; pass++) {
    let changed = false;
    for (const pe of perEdge)
      for (const sp of pe.spans) {
        if (sp[2] === undefined || !conv.has(sp[2])) continue;
        const other = pe.spans.find(q => q !== sp && !(q[2] !== undefined && conv.has(q[2])) && q[0] <= sp[1] + 2 && sp[0] <= q[1] + 2);
        if (other) { why(sp[2], other[2] === undefined ? 'its deck also spans a lake' : `its deck also spans another creek crossing (${waters[creekX[other[2]].wi].name || 'unnamed'}) that is not piped`); changed = true; }
      }
    if (!changed) break;
  }
  // every piped crossing: no span; the rest as they were
  for (const pe of perEdge) {
    const spans = pe.spans.filter(sp => !(sp[2] !== undefined && conv.has(sp[2])));
    if (!spans.length) continue;
    spans.sort((p, q) => p[0] - q[0]);
    const merged = [spans[0].slice(0, 2)];
    for (const s of spans.slice(1)) {
      const last = merged[merged.length - 1];
      if (s[0] <= last[1] + 2) last[1] = Math.max(last[1], s[1]); else merged.push(s.slice(0, 2));
    }
    const total = pe.acc[pe.acc.length - 1];
    for (const [s0, s1] of merged) wspans.push({ e: pe.e.id, s0: Math.max(0, s0), s1: Math.min(total, s1) });
  }
  // the creek runs on under the road, as a ravine does: the road's fill
  // covers it (CityElevation.HoldCulverts keeps the pipe's cover) and the
  // game finds each end where the fill meets the channel (CityCulverts)
  const xs = [...conv.keys()].sort((a, b) => a - b);
  const byTier = [0, 0, 0, 0], km = [0, 0, 0, 0];
  for (const xi of xs) {
    rowOf.get(xi).why = 'CULVERT';
    const e = edges[creekX[xi].e]; byTier[tierOf(e.way.rank)]++; km[tierOf(e.way.rank)] += 2 * creekX[xi].half;
  }
  CULV.xs = xs.map(xi => {
    const X = creekX[xi], w = waters[X.wi];
    // the road's hold over the pipe: across the creek's flat floor and two
    // metres, along the road
    const halfAlong = Math.min(30, (creekFlatHalf(w.widthM) + 2) / Math.max(0.35, X.sin));
    return { way: rowOf.get(xi).culvertWay, water: X.wi, ws: X.ws, e: X.e, s: X.s, half: halfAlong, bed: bedAt(w, X.ws) };
  });
  const rejected = CULV.rows.filter(r => r.why !== 'CULVERT').length;
  console.log(`water bridge spans: ${wspans.length}; B2 culverts (owner Q8): ${CULV.rows.length} creek crossings with an OSM culvert line within ${CULVERT_NEAR_M} m: ` +
              `${xs.length} piped (T1 ${byTier[1]}, T2 ${byTier[2]}, T3 ${byTier[3]}; ${(km[1] / 1000).toFixed(2)}/${(km[2] / 1000).toFixed(2)}/${(km[3] / 1000).toFixed(2)} km of deck gone), ${rejected} left as bridges`);
}

// ------------------------------------------ B1: bridge outlines, culvert creeks
// (plan B1, owner Q8, 2026-10-02; the rules are in lib/bridges.mjs). An OSM
// man_made=bridge outline round two carriageways makes them ONE structure:
// each bridge=yes edge takes the outline holding >= 60% of its 4 m samples
// (its structId; section BRST), and the game's twin-deck table
// (Scripts/City/DeckPairs.cs) joins two decks in one outline, keeps two in
// different outlines apart, and falls back to the class rule without one.
// B1 listed here the water spans within 15 m of an OSM culvert line, so no
// twin-deck union joined one before B2 decided them (critic C2/D3). B2
// (owner Q8) has: a creek OSM pipes under the road has no span any more (the
// culverts above, section CULV), and a span left is a bridge by the rule, so
// BRST's culvert table is empty from B2 on (the layout keeps it). Twin-deck
// overrides by way pair (FORCE one structure / NEVER) come from
// tools/city/deckpairs_overrides.json.
const OUTLINES_FILE = join(CACHE, 'bridges_mm.json');
const CULVERTS_FILE = join(CACHE, 'layers', 'culverts.json');
const OVERRIDES_FILE = join(HERE, 'deckpairs_overrides.json');
const BRST = (() => {
  if (!existsSync(OUTLINES_FILE)) throw new Error(`${relative(UNITY, OUTLINES_FILE)} is missing: fetch it with tools/city/fetch/fetch_ways.mjs (its snapshot is recorded in cache_manifest.json)`);
  if (!existsSync(CULVERTS_FILE)) throw new Error(`${relative(UNITY, CULVERTS_FILE)} is missing: fetch it with tools/city/fetch/fetch_layers.mjs`);
  const outl = loadOutlines(OUTLINES_FILE, toX, toZ), ix = outlineIndex(outl);
  const struct = [];
  let touched = 0;
  for (const e of edges) {
    if (!e.way.bridge) continue;
    const r = structIdOf(e.pts, ix);
    if (r.id) struct.push([e.id, r.id]); else if (r.touched) touched++;
  }
  const overrides = existsSync(OVERRIDES_FILE) ? JSON.parse(readFileSync(OVERRIDES_FILE, 'utf8')).pairs || [] : [];
  for (const o of overrides)
    if (!(o.decision === 'FORCE' || o.decision === 'NEVER') || !(o.wayA > 0) || !(o.wayB > 0))
      throw new Error(`deckpairs_overrides.json: ${JSON.stringify(o)} is not { wayA, wayB, decision: FORCE | NEVER, why }`);
  const bridgeEdges = edges.filter(e => e.way.bridge).length;
  console.log(`B1 outlines: ${outl.length} man_made=bridge outlines; ${struct.length} of ${bridgeEdges} bridge edges in one (${new Set(struct.map(q => q[1])).size} outlines used), ${touched} more only partly inside one (structId 0: the class rule)`);
  return { struct, culvert: [], overrides };
})();

// ------------------------------- C9: dead ends short of a road (plan critic C9)
// A road that stops a few metres short of another at the same level, sharing
// no node, is a GAP in the data that no mesh audit can see (their outline is
// built from the same edges). The exporter gate: every interior dead end
// whose nearest same-level road's pavement is within DEAD_END_REACH_M -
// short of it, or inside it - is welded or explained, T1 gating (T2 and T3
// reported for B8 / B9). Weld only same-name ends or a link onto a road; a
// true non-connection (West 11th Street beside a motorway_link) never.
// EXPLAINED: each with the OSM evidence that the end is a REAL connection to
// a road this snapshot does not carry - welding it onto the road beside
// would invent a connection that is not there.
const DEAD_END_REACH_M = 6;
const DEAD_END_EXPLAINED = new Map([
  // I-85 east of the Catawba (35.258,-81.006): both carriageways leave by a
  // motorway_link that ends in mid-air (ways 754768127 east, 754768136
  // west) and come back 320 m on by another that starts in mid-air beside
  // the freeway (these two). The four free ends are a roadside facility's
  // service roads, which the arterial query does not fetch (and unnamed
  // service ways are dropped); a weld would make a slip road from I-85
  // back onto I-85 that does not exist.
  [7050788926, "I-85 eastbound entrance (way 754768134) from a roadside facility west of it, not from I-85: the facility's service road is not in the snapshot"],
  [7050791386, "I-85 westbound entrance (way 785500728) from a roadside facility east of it, not from I-85: the facility's service road is not in the snapshot"],
  // Johnston Road at Ballantyne (35.0400,-80.8462): OSM's turn restriction
  // 16858537 (only_right_turn, from this way via its end node to way
  // 1233246204) says this slip lane turns RIGHT onto way 1233246204 - a
  // street outside the residential coverage (Q6: core + 1 km ring, B9) - and
  // not back onto Johnston Road beside it.
  [11443025075, 'Johnston Road right-turn slip (way 1233246203) onto way 1233246204 (restriction 16858537 only_right_turn), a street outside the residential coverage (Q6, B9)'],
]);
{
  const osmIdOf = new Map();
  for (const [id, n] of nodeIndex) osmIdOf.set(n, id);
  const deg = new Array(nodes.length).fill(0), own = new Array(nodes.length).fill(null);
  for (const e of edges) { deg[e.a]++; deg[e.b]++; own[e.a] = e; own[e.b] = e; }
  const hwOf = e => profileFor(e.way.rank, e.way.link, e.way.oneway, e.lanes, e.turn).width / 2;
  const WORLD = [35.03, -81.03, 35.42, -80.62], MARG = 0.004;
  const ptSeg = (px, pz, a, b) => { const dx = b[0] - a[0], dz = b[1] - a[1], L = dx * dx + dz * dz; const t = L > 0 ? Math.max(0, Math.min(1, ((px - a[0]) * dx + (pz - a[1]) * dz) / L)) : 0; return Math.hypot(px - a[0] - dx * t, pz - a[1] - dz * t); };
  const rows = { 1: [], 2: [], 3: [] }, ends = [0, 0, 0, 0];
  for (let n = 0; n < nodes.length; n++) {
    if (deg[n] !== 1) continue;
    const e = own[n], [x, z] = nodes[n], lat = toLat(z), lon = toLon(x);
    if (lat < WORLD[0] + MARG || lat > WORLD[2] - MARG || lon < WORLD[1] + MARG || lon > WORLD[3] - MARG) continue;   // the world's edge
    const t = tierOf(e.way.rank);
    ends[t]++;
    let best = Infinity, bo = null;
    const cx = Math.floor(x / SEGCELL), cz = Math.floor(z / SEGCELL);
    for (let ix = cx - 1; ix <= cx + 1; ix++) for (let iz = cz - 1; iz <= cz + 1; iz++)
      for (const packed of segHash.get(cellKey(ix, iz)) || []) {
        const o = edges[packed >> 12], k = packed & 4095;
        if (o === e || o.a === n || o.b === n || o.way.level !== e.way.level) continue;
        const d = ptSeg(x, z, o.pts[k - 1], o.pts[k]) - hwOf(o);
        if (d < best) { best = d; bo = o; }
      }
    if (!bo || best > DEAD_END_REACH_M) continue;
    const osm = osmIdOf.get(n);
    rows[t].push({ n, osm, e, o: bo, d: best, why: DEAD_END_EXPLAINED.get(osm) || null, at: `${lat.toFixed(5)},${lon.toFixed(5)}` });
  }
  const line = r => `${r.d <= 0.3 ? 'inside' : 'short'} ${r.d.toFixed(2)} m: e${r.e.id} way ${r.e.way.id} ${r.e.way.base}${r.e.way.link ? '_link' : ''} '${r.e.way.name}' end node ${r.osm} -> e${r.o.id} way ${r.o.way.id} '${r.o.way.name}' @ ${r.at}`;
  const unexplained = rows[1].filter(r => !r.why);
  console.log(`C9 dead ends within ${DEAD_END_REACH_M} m of a same-level road (interior ends T1 ${ends[1]} / T2 ${ends[2]} / T3 ${ends[3]}): T1 ${rows[1].length} (${rows[1].length - unexplained.length} explained, 0 welded), T2 ${rows[2].length}, T3 ${rows[3].length} (reported: B8 / B9)`);
  for (const r of rows[1]) console.log(`  T1 ${line(r)}${r.why ? '\n       EXPLAINED: ' + r.why : '  UNEXPLAINED'}`);
  if (unexplained.length)
    throw new Error(`C9 gate: ${unexplained.length} T1 dead end(s) within ${DEAD_END_REACH_M} m of a same-level road with no weld and no explanation (weld a same-name end or a link onto its road; explain a real connection in DEAD_END_EXPLAINED):\n  ` + unexplained.map(line).join('\n  '));
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
const demK = ROAD_GRID_CELL / DEM_CELL;
const demNX = demK * Math.ceil((bbox.x1 - bbox.x0) / ROAD_GRID_CELL) + 1;
const demNZ = demK * Math.ceil((bbox.z1 - bbox.z0) / ROAD_GRID_CELL) + 1;
console.log(`DEM grid ${demNX} x ${demNZ} at ${DEM_CELL} m over ${((bbox.x1 - bbox.x0) / 1000).toFixed(1)} x ${((bbox.z1 - bbox.z0) / 1000).toFixed(1)} km`);
// Every node is the MEAN of the 3DEP 1/3" pixels (about 8.4 x 10.3 m) whose
// centres lie in its own 30 m cell: an area average, so the grid neither
// aliases the 10 m detail nor loses the ridges and valleys a 30 m grid can
// hold. NOTHING FILTERS IT AFTERWARDS. The opening (erode, dilate), closing
// and Gaussian that stood here until WP-04 were written against radar
// "roofs": the source was taken to be SRTM with uptown's towers 200 m proud.
// It never was (the skadi tiles were bare earth, and so is 3DEP), so all they
// removed was the real terrain - 66% of the core's relief, every creek valley
// and ridge line (survey_flatness, 2026-09-27).
let dem = new Float32Array(demNX * demNZ);
for (let iz = 0; iz < demNZ; iz++)
  for (let ix = 0; ix < demNX; ix++) {
    const x = bbox.x0 + ix * DEM_CELL, z = bbox.z0 + iz * DEM_CELL, h = DEM_CELL / 2;
    dem[iz * demNX + ix] = dem3.blockMean(toLat(z - h), toLat(z + h), toLon(x - h), toLon(x + h));
  }
// The pit clamp (DEM_CLAMP): below DEM_FLOOR only inside a named box, and
// there the ground is lifted to it; anywhere else the export stops.
{
  const clamped = new Map(), outside = [];
  for (let iz = 0; iz < demNZ; iz++) for (let ix = 0; ix < demNX; ix++) {
    const i = iz * demNX + ix;
    if (!(dem[i] < DEM_FLOOR)) continue;
    const lat = toLat(bbox.z0 + iz * DEM_CELL), lon = toLon(bbox.x0 + ix * DEM_CELL);
    const pit = DEM_CLAMP.find(c => lat >= c.box[0] && lat <= c.box[2] && lon >= c.box[1] && lon <= c.box[3]);
    if (!pit) { outside.push(`${lat.toFixed(5)},${lon.toFixed(5)} ${dem[i].toFixed(1)} m`); continue; }
    const c = clamped.get(pit.name) || { n: 0, deepest: Infinity };
    c.n++; c.deepest = Math.min(c.deepest, dem[i]);
    clamped.set(pit.name, c);
    dem[i] = DEM_FLOOR;
  }
  if (outside.length)
    throw new Error(`${outside.length} DEM cells below ${DEM_FLOOR} m outside every DEM_CLAMP box (the datum is pinned at ${DEM_BASE} m): ` +
                    outside.slice(0, 8).join('; ') + (outside.length > 8 ? '; ...' : ''));
  console.log(clamped.size ? 'DEM clamped to ' + DEM_FLOOR + ' m: ' + [...clamped].map(([k, v]) => `${k} ${v.n} cells (deepest ${v.deepest.toFixed(1)} m)`).join(', ')
                           : `DEM clamp: nothing below ${DEM_FLOOR} m`);
}
let demMin = Infinity, demMax = -Infinity;
for (const v of dem) { demMin = Math.min(demMin, v); demMax = Math.max(demMax, v); }
const demBase = DEM_BASE;
if ((demMax - demBase) * DEM_UNITS > 65535) throw new Error(`DEM max ${demMax.toFixed(1)} m does not fit u16 ${DEM_SCALE} m steps above ${demBase} m`);
console.log(`DEM range ${demMin.toFixed(0)}..${demMax.toFixed(0)} m ASL; base ${demBase} (pinned)`);
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

// ---- roads pass L8 (lib/lots.mjs): the parking lots, their stall lines and
// entrances, and the turning circles, from fetch/fetch_lots.mjs's one fetch
const lotsFile = join(CACHE, 'lots_core.json');
const LOTS = existsSync(lotsFile)
  ? buildLots({ raw: loadJson(lotsFile).elements, edges, buildings, nodeIndex, nodeCount: nodes.length, toX, toZ, laneM: LANE_M })
  : { lots: [], entrances: [], turns: [], stats: { missing: lotsFile } };
console.log('lots (L8):', JSON.stringify(LOTS.stats));

// ------------------------------------------------------------------ emit
class Writer {
  constructor() { this.chunks = []; this.buf = Buffer.alloc(1 << 20); this.pos = 0; }
  ensure(n) { if (this.pos + n > this.buf.length) { this.chunks.push(this.buf.subarray(0, this.pos)); this.buf = Buffer.alloc(Math.max(1 << 20, n)); this.pos = 0; } }
  u8(v) { this.ensure(1); this.buf.writeUInt8(v & 255, this.pos); this.pos += 1; }
  i8(v) { this.ensure(1); this.buf.writeInt8(Math.max(-128, Math.min(127, v)), this.pos); this.pos += 1; }
  u16(v) { this.ensure(2); this.buf.writeUInt16LE(v & 65535, this.pos); this.pos += 2; }
  i16(v) { this.ensure(2); this.buf.writeInt16LE(v, this.pos); this.pos += 2; }
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
// uptown (Trade & Tryon)
const uptownX = toX(-80.8431), uptownZ = toZ(35.2271);

// ---- graph: PSXC v2, a section table and then the sections (the layout is
// documented in tools/city/lib/citydata.mjs). The content of every section
// is what version 1 wrote in one run, with the edges' points moved out of
// EDGE into PNTS; GHSH is new.
{
  const sec = new Map();
  const section = (tag, fill) => { const w = new Writer(); fill(w); sec.set(tag, w.bytes()); };
  const f32r = v => Math.fround(v);
  section('META', w => { w.str(ATTRIBUTION); w.f32(uptownX); w.f32(uptownZ); });
  section('NODE', w => {
    w.u32(nodes.length);
    for (let i = 0; i < nodes.length; i++) { w.f32(nodes[i][0]); w.f32(nodes[i][1]); w.u8(nodeCtl[i]); }
  });
  const names = new Map([['', 0]]);
  const nameList = [''];
  const nameIdx = s => { let i = names.get(s); if (i === undefined) { i = nameList.length; nameList.push(s); names.set(s, i); } return i; };
  for (const e of edges) nameIdx(e.way.name);
  for (const wt of waters) nameIdx(wt.name);
  section('NAME', w => { w.u32(nameList.length); for (const s of nameList) w.str(s); });
  let pointCount = 0;
  section('EDGE', w => {
    w.u32(edges.length);
    for (const e of edges) {
      const wy = e.way;
      w.u32(e.a); w.u32(e.b); w.u32(nameIdx(wy.name));
      w.u8(wy.rank);
      w.u8((wy.link ? 1 : 0) | (wy.oneway ? 2 : 0) | (wy.bridge ? 4 : 0) | (wy.tunnel ? 8 : 0) | (e.turn ? 16 : 0) | (wy.roundabout ? 32 : 0));
      w.u8(e.lanes); w.i8(wy.level);
      // the exporter's width at the REAL lane width of its class (LANW; the
      // game still draws the profile's 3.6576 m lanes until WP-11b)
      w.f32(e.lanes * (wy.link ? LANE_W_LINK : LANE_W)[wy.rank] + wy.shl + wy.shr); w.f32(wy.shl); w.f32(wy.shr);
      w.u8(Math.min(255, wy.speed)); w.u32(wy.id);
      if (e.pts.length > 65535) throw new Error(`edge ${e.id} has ${e.pts.length} points (u16)`);
      w.u16(e.pts.length);
      pointCount += e.pts.length;
    }
  });
  section('PNTS', w => {
    w.u32(pointCount);
    for (const e of edges) for (const p of e.pts) { w.f32(p[0]); w.f32(p[1]); }
  });
  section('WATR', w => {
    w.u32(waters.length);
    for (const wt of waters) {
      w.u32(nameIdx(wt.name)); w.f32(wt.widthM); w.u8(wt.kind);   // 0 creek, 1 lake, 2 ravine
      w.u32(wt.pts.length);
      for (const p of wt.pts) { w.f32(p[0]); w.f32(p[1]); }
    }
  });
  // WBED (WP-04b): each water's bed in WATR's order (lib/citydata.mjs has
  // the layout): the first sample as u16 cm above the datum, then i16 cm
  // steps. A bed below the datum would wrap: the export stops instead, as the
  // DEM clamp does.
  section('WBED', w => {
    w.u32(waters.length); w.f32(DEM_BASE); w.f32(1 / BED_UNITS);
    for (const wt of waters) {
      const bed = wt.bed || [];
      w.u32(bed.length); w.f32(wt.bedStep || 0);
      let prev = 0;
      bed.forEach((v, k) => {
        const u = Math.round((v - DEM_BASE) * BED_UNITS);
        if (!(u >= 0 && u <= 65535)) throw new Error(`water "${wt.name}": bed ${v.toFixed(2)} m does not fit u16 cm above the ${DEM_BASE} m datum`);
        if (k === 0) w.u16(u);
        else { const d = u - prev; if (d < -32768 || d > 32767) throw new Error(`water "${wt.name}": a bed step of ${d} cm`); w.u16(d & 0xffff); }
        prev = u;
      });
    }
  });
  section('XING', w => {
    w.u32(crossings.length);
    for (const c of crossings) { w.u32(c.over); w.u32(c.under); w.f32(c.x); w.f32(c.z); w.u8(c.forced ? 1 : 0); }
  });
  section('SPAN', w => { w.u32(wspans.length); for (const s of wspans) { w.u32(s.e); w.f32(s.s0); w.f32(s.s1); } });
  section('ROUT', w => {
    w.u32(routes.length);
    for (const r of routes) {
      w.str(r.id); w.str(r.name); w.u8(r.loop ? 1 : 0); w.u8(r.oneway ? 1 : 0);
      w.f32(r.roadWidth); w.u8(r.speed);
      w.f32(r.lengthM); w.f32(r.startM); w.f32(r.finishM);
      w.u32(r.chain.length);
      for (const c of r.chain) { w.u32(c.e.id); w.i8(c.dir); }
    }
  });
  // the graph hash, over the points as the file stores them (float32)
  const ghash = graphHash(edges.map(e => ({ a: e.a, b: e.b, pts: e.pts.map(p => [f32r(p[0]), f32r(p[1])]) })));
  section('GHSH', w => w.u32(ghash));
  // ---- WP-10 (plan A4 + A8; lib/lineclean.mjs has the rules)
  // LANW: real lane widths by class, rank 0..5, then the links'
  section('LANW', w => { w.u8(LANE_W.length); for (const v of LANE_W) w.f32(v); for (const v of LANE_W_LINK) w.f32(v); });
  // TAPR: every lane-count change along a chain - the wide run's edge and
  // end at the node, the ribbon side that moves (0 left, 1 right of a->b),
  // where the side came from (0 rule, 1 bay rule, 2 turn:lanes, 3 lanes:
  // forward/backward), flags (1 turn bay, 2 full width at the node - a
  // junction mouth, or no room for a taper - with len 0 at a junction, 4 a
  // drop in chain order, 8 untagged), the width change, the MUTCD length,
  // the room the chain has, and the wide run's offset at the node; then
  // every edge's ribbon offset off its OSM line (+ = left of a->b), where
  // not zero, as o0 + (o1 - o0) * smoothstep((s - t0) / (t1 - t0)): u32
  // edge, f32 o0, o1, t0, t1. Since the WP-10 review the offset HOLDS along
  // an edge (o0 = o1, t0 0, t1 1): no mid-block shift; the fields stay for
  // WP-11b's eases.
  section('TAPR', w => {
    w.u32(LC.tapr.length);
    for (const t of LC.tapr) { w.u32(t.edge); w.u8(t.end); w.u8(t.side); w.u8(t.src); w.u8(t.flags); w.f32(t.dw); w.f32(t.len); w.f32(t.room); w.f32(t.off); }
    const offs = [];
    for (let i = 0; i < edges.length; i++) { const r = LC.edgeOffset[i]; if (r && (Math.abs(r.o0) > 1e-4 || Math.abs(r.o1) > 1e-4)) offs.push(i); }
    w.u32(offs.length);
    for (const i of offs) { const r = LC.edgeOffset[i]; w.u32(i); w.f32(r.o0); w.f32(r.o1); w.f32(r.t0); w.f32(r.t1); }
  });
  // PARA: the carriageway pairs still short of their gap outside an attach
  // zone (the review list: e1, e2, where, the deficit, metres, in the core,
  // on a race route), then every edge PARA moved with its largest shift.
  section('PARA', w => {
    const onRoute = new Set(routes.flatMap(r => r.chain.map(c => c.e.id)));
    const items = LC.review.filter(r => r.kind === 'para');
    {
      const inC = r => Math.abs(r.x - uptownX) <= 4000 && Math.abs(r.z - uptownZ) <= 4000;
      const onR = r => onRoute.has(r.edge) || onRoute.has(r.edge2);
      console.log(`WP-10 PARA review: ${items.length} pairs; core ${items.filter(inC).length}, routes ${items.filter(onR).length}`);
      if (process.env.PSX_LC_DIAG) for (const r of items.filter(r => inC(r) || onR(r))) console.log(`  PARA ${inC(r) ? 'core' : ''}${onR(r) ? ' route' : ''} e${r.edge}/e${r.edge2} ${r.note} short ${r.deficit.toFixed(2)} m for ${r.metres.toFixed(0)} m at (${r.x.toFixed(0)}, ${r.z.toFixed(0)}) ${toLat(r.z).toFixed(5)},${toLon(r.x).toFixed(5)}`);
    }
    w.u32(items.length);
    for (const r of items) {
      w.u32(r.edge); w.u32(r.edge2); w.f32(r.x); w.f32(r.z); w.f32(r.deficit); w.f32(r.metres);
      w.u8(Math.abs(r.x - uptownX) <= 4000 && Math.abs(r.z - uptownZ) <= 4000 ? 1 : 0);
      w.u8(onRoute.has(r.edge) || onRoute.has(r.edge2) ? 1 : 0);
    }
    const movedE = new Map();
    for (const m of LC.para.moved) for (const id of m.edges) movedE.set(id, Math.max(movedE.get(id) || 0, m.umax));
    w.u32(movedE.size);
    for (const [id, u] of movedE) { w.u32(id); w.f32(u); }
  });
  // TAGN: control nodes resolved on the RAW ways before any vertex moved
  // (critic C3): OSM node id (lo, hi), way id, kind (4 signal, 2 stop, 1 give
  // way), raw distance along the way, and where they are now (edge, s).
  section('TAGN', w => {
    w.u32(LC.tagged.length);
    for (const t of LC.tagged) { w.u32(t.nodeId % 4294967296); w.u32(Math.floor(t.nodeId / 4294967296)); w.u32(t.wayId); w.u8(t.kind); w.f32(t.rawS); w.u32(t.edge); w.f32(t.s); }
  });
  // SPLT (WP-11, critic C11; lib/splits.mjs): where an undivided road splits
  // into its two carriageways - not a junction but a median taper, drawn by
  // the line model (WP-11b): u32 node, u32 undivided edge, u32 carriageway
  // leaving, u32 carriageway arriving, f32 their centre offsets at the node
  // in the undivided edge's frame (+ = left of its direction into the node),
  // f32 the MUTCD shifting-taper rate (m along per m across).
  section('SPLT', w => {
    w.u32(SPLITS.length);
    for (const t of SPLITS) { w.u32(t.node); w.u32(t.u); w.u32(t.a); w.u32(t.b); w.f32(t.offA); w.f32(t.offB); w.f32(t.rate); }
  });
  // BRST (plan B1; lib/citydata.mjs has the layout, lib/bridges.mjs the
  // rules): each bridge edge's OSM outline, the water spans a culvert
  // claims (owner Q8), the twin-deck overrides by way pair.
  section('BRST', w => {
    w.u32(BRST.struct.length);
    for (const [ei, id] of BRST.struct) { w.u32(ei); w.u32(id); }
    w.u32(BRST.culvert.length);
    for (const c of BRST.culvert) { w.u32(c.span); w.u32(c.way); w.f32(c.d); }
    w.u32(BRST.overrides.length);
    for (const o of BRST.overrides) { w.u32(o.wayA); w.u32(o.wayB); w.u8(o.decision === 'FORCE' ? 1 : 0); }
  });
  // CULV (plan B2, owner Q8): the road crossings of a creek OSM pipes under
  // the road - no span; the game's culvert. Layout: lib/citydata.mjs.
  section('CULV', w => {
    w.u32(CULV.xs.length);
    for (const x of CULV.xs) { w.u32(x.way); w.u32(x.water); w.f32(x.ws); w.u32(x.e); w.f32(x.s); w.f32(x.half); w.f32(x.bed); }
  });
  // LSET (roads pass L2, lib/lineset.mjs has the layout and the rules): per
  // edge, the lanes each way, the centre (none / double yellow / TWLTL),
  // marked or not (owner Q1), bays, turn-only lanes, sub-class, bike lanes.
  section('LSET', w => {
    w.u32(edges.length);
    for (const r of LSET.rows) { w.u8(r.nF); w.u8(r.nB); w.u8(r.centre); w.u8(r.flags); w.u16(r.turnOnly); w.u8(r.sub); w.u8(r.bike); }
  });
  // TURN (roads pass L8, lib/lots.mjs): the turning circles at dead ends -
  // u32 node, u8 kind (0 circle, 1 loop), f32 radius.
  section('TURN', w => {
    w.u32(LOTS.turns.length);
    for (const t of LOTS.turns) { w.u32(t.node); w.u8(t.kind); w.f32(t.r); }
  });
  // LOTS (roads pass L8): the surface parking lots - per lot f32 origin x, z
  // (its first point), u16 points, then i16 dx, dz per point in 5 cm from the
  // origin (anticlockwise); the stall rows' direction u (i16 x, z / 32767;
  // the stalls stand along v, its left); u16 runs, each i16 x, z of its first
  // separator's foot in 5 cm from the origin and u8 stalls (separators
  // 2.74 m apart along u, 5.49 m long along v, one more than the stalls).
  section('LOTS', w => {
    w.u32(LOTS.lots.length);
    const q = v => { const k = Math.round(v / 0.05); if (k < -32768 || k > 32767) throw new Error('LOTS offset out of i16 range'); return k; };
    for (const L of LOTS.lots) {
      const [ox, oz] = L.ring[0];
      w.f32(ox); w.f32(oz);
      w.u16(L.ring.length);
      for (const p of L.ring) { w.i16(q(p[0] - ox)); w.i16(q(p[1] - oz)); }
      w.i16(Math.round(L.ux * 32767)); w.i16(Math.round(L.uz * 32767));
      w.u16(L.runs.length);
      for (const r of L.runs) { w.i16(q(r.x - ox)); w.i16(q(r.z - oz)); w.u8(r.n); }
    }
  });
  // LENT (roads pass L8): the lots' entrances - u32 edge, f32 arc position,
  // i8 side (+1 left of a->b), f32 half width. The game pours a concrete apron.
  section('LENT', w => {
    w.u32(LOTS.entrances.length);
    for (const t of LOTS.entrances) { w.u32(t.edge); w.f32(t.s); w.i8(t.side); w.f32(t.half); }
  });
  const align = n => (n + 3) & ~3;
  const assemble = () => {
    const headLen = 12 + 12 * sec.size;
    const head = Buffer.alloc(headLen);
    head.writeUInt32LE(0x43585350, 0); // "PSXC"
    head.writeInt32LE(2, 4);
    head.writeUInt32LE(sec.size, 8);
    const parts = [head];
    let at = headLen, k = 0;
    for (const [tag, body] of sec) {
      const off = align(at);
      if (off > at) parts.push(Buffer.alloc(off - at));
      head.write(tag, 12 + 12 * k, 4, 'latin1');
      head.writeUInt32LE(off, 16 + 12 * k);
      head.writeUInt32LE(body.length, 20 + 12 * k);
      parts.push(body);
      at = off + body.length; k++;
    }
    return Buffer.concat(parts);
  };
  // RPRF (leftover item 1, plan B7): the measured road profiles, designed on
  // the graph exactly as the game reads it (the file so far, parsed back), in
  // world y (3DEP bare earth minus the pinned datum). lib/roadprofile.mjs.
  // Only with --rprf (OFF: see MODE.rprf).
  if (MODE.rprf) {
    const graph = parseCity(assemble());
    const rp = buildRoadProfiles(graph, (x, z) => dem3.sample(toLat(z), toLon(x)) - DEM_BASE);
    for (const l of rp.lines) console.log(l);
    section('RPRF', w => writeRprf(w, rp.prof));
  }
  if ([...sec.keys()].join() !== CITY_SECTIONS.filter(t => t !== 'RPRF' || MODE.rprf).join()) throw new Error('PSXC sections out of step with citydata.mjs CITY_SECTIONS');
  const bytes = assemble();
  emit('charlotte_city.bytes', bytes);
  console.log(`charlotte_city.bytes ${(bytes.length / 1024).toFixed(0)} KB (PSXC v2: ${[...sec].map(([t, b]) => `${t} ${(b.length / 1024).toFixed(0)}`).join(', ')} KB); graph hash ${hashHex(ghash)}`);
}

// ---- DEM: PDEM v3 (WP-13): the v2 header, then the grid in delta-coded
// blocks the game decodes one at a time (lib/pdem3.mjs has the layout)
{
  const q = new Uint16Array(dem.length);
  for (let i = 0; i < dem.length; i++) q[i] = Math.round(Math.max(0, dem[i] - demBase) * DEM_UNITS);
  const bytes = encodePdem3({ nx: demNX, nz: demNZ, x0: bbox.x0, z0: bbox.z0, cell: DEM_CELL, base: demBase, scale: DEM_SCALE, q });
  emit('charlotte_dem.bytes', bytes);
  console.log(`charlotte_dem.bytes ${(bytes.length / 1024).toFixed(0)} KB (PDEM v3, ${demNX} x ${demNZ} at ${DEM_CELL} m in ${Math.ceil((demNX - 1) / 32) * Math.ceil((demNZ - 1) / 32)} blocks)`);
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
  emit('charlotte_bld.bytes', bytes);
  console.log(`charlotte_bld.bytes ${(bytes.length / 1024).toFixed(0)} KB`);
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
  emit('charlotte_routes.json', JSON.stringify(out));
  console.log('charlotte_routes.json');
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
    const wpx = Math.max(1, (e.lanes * LANE_M + wy.shl + wy.shr) * scale);
    for (let i = 1; i < e.pts.length; i++) line(px(e.pts[i - 1][0]), py(e.pts[i - 1][1]), px(e.pts[i][0]), py(e.pts[i][1]), wpx, ...col);
  }
  for (const c of crossings) if (inView([c.x, c.z])) line(px(c.x) - 2, py(c.z), px(c.x) + 2, py(c.z), 3, 200, 0, 200);
  for (const r of routes) for (let i = 1; i < r.coarse.length; i++) if (inView(r.coarse[i])) line(px(r.coarse[i - 1][0]), py(r.coarse[i - 1][1]), px(r.coarse[i][0]), py(r.coarse[i][1]), 2, 0, 200, 255);
  png(S, S, rgb, join(HERE, `charlotte_${name}.png`));
}
if (MODE.out && MODE.plots) {
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
}

// ------------------------------------------------- write, or check, the result
// The inputs this export read: the Overpass cache (with its snapshot time),
// the 3DEP box the ground and the beds come from, the water layers, and
// SOURCES.md (the credits are part of the output). --manifest records them;
// --check verifies them, so a failed check says whether the INPUTS moved or
// the CODE did.
function inputFiles() {
  const files = [];
  const add = (label, path, kind) => { if (existsSync(path)) files.push({ label, path, kind }); };
  add('tools/city/cache/ways_all.json', join(CACHE, 'ways_all.json'), 'overpass');
  add('tools/city/cache/nodes_all.json', join(CACHE, 'nodes_all.json'), 'overpass');
  add('tools/city/cache/streets_core.json', join(CACHE, 'streets_core.json'), 'overpass');
  if (existsSync(join(CACHE, 'lots_core.json'))) add('tools/city/cache/lots_core.json', join(CACHE, 'lots_core.json'), 'overpass');
  add('tools/city/cache/buildings_core.json', join(CACHE, 'buildings_core.json'), 'overpass');
  // the 3DEP box (%PSX_GIS_DIR%\3dep when that is set) and its georeference
  add('tools/city/cache/3dep/box13.f32', dem3.f32Path, '3dep');
  add('tools/city/cache/3dep/box13.json', dem3.jsonPath, '3dep');
  for (const f of waterInputPaths(CACHE)) add(f.label, f.path, 'water');
  // B1: the bridge outlines, the culvert lines, the twin-deck overrides
  add('tools/city/cache/bridges_mm.json', OUTLINES_FILE, 'overpass');
  add('tools/city/cache/layers/culverts.json', CULVERTS_FILE, 'overpass');
  // by its pairs only (the note is prose; a checkout's line endings are not data)
  files.push({ label: 'tools/city/deckpairs_overrides.json#pairs', content: Buffer.from(JSON.stringify(BRST.overrides), 'utf8'), kind: 'registry' });
  // SOURCES.md by its Credits only: the rest of the registry is prose that
  // later packages edit without changing a byte of the output
  files.push({ label: 'tools/city/SOURCES.md#credits', content: Buffer.from(ATTRIBUTION, 'utf8'), kind: 'registry' });
  return files;
}
const sha256 = buf => createHash('sha256').update(buf).digest('hex');
function describeInputs() {
  return inputFiles().map(f => {
    const buf = f.content || readFileSync(f.path);
    const d = { file: f.label, kind: f.kind, bytes: buf.length, sha256: sha256(buf) };
    if (f.kind === 'overpass') {
      // the snapshot line sits in the first few hundred bytes of an Overpass body
      const m = /"timestamp_osm_base"\s*:\s*"([^"]+)"/.exec(buf.subarray(0, 2048).toString('utf8'));
      d.timestamp_osm_base = m ? m[1] : null;
    }
    return d;
  });
}
const withoutVolatile = fp => { const c = JSON.parse(JSON.stringify(fp)); delete c.outputs; delete c.generated; return c; };
function diffJson(a, b, path = '', out = []) {
  if (typeof a !== 'object' || a === null || typeof b !== 'object' || b === null) {
    if (JSON.stringify(a) !== JSON.stringify(b)) out.push(`${path || '.'}: ${JSON.stringify(b)} -> ${JSON.stringify(a)}`);
    return out;
  }
  for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) diffJson(a[k], b[k], path ? `${path}.${k}` : k, out);
  return out;
}
function makeFingerprint() {
  const fp = fingerprint(parseCity(OUTPUTS.get('charlotte_city.bytes')),
                         parseDem(OUTPUTS.get('charlotte_dem.bytes')),
                         parseBld(OUTPUTS.get('charlotte_bld.bytes')));
  const outputs = {};
  for (const [name, buf] of OUTPUTS) outputs[name] = { bytes: buf.length, sha256: sha256(buf) };
  return { schema: 1, ...fp, outputs, generated: { by: 'tools/city/export_osm.mjs', node: process.version } };
}

let failed = false;
if (MODE.manifest) {
  const m = { schema: 1, note: 'Inputs of tools/city/export_osm.mjs. The Overpass cache is gitignored and exists only on the machine that fetched it (tools/city/fetch/ holds the queries); this records exactly what the shipped data was made from.',
              recorded_with: process.version, inputs: describeInputs() };
  writeFileSync(MANIFEST, JSON.stringify(m, null, 2) + '\n');
  console.log(`wrote ${relative(UNITY, MANIFEST)} (${m.inputs.length} inputs)`);
}
if (MODE.out) {
  const dir = resolve(UNITY, MODE.out);
  mkdirSync(dir, { recursive: true });
  for (const [name, buf] of OUTPUTS) {
    writeFileSync(join(dir, name), buf);
    console.log(`wrote ${relative(UNITY, join(dir, name))}  ${buf.length} B  sha256 ${sha256(buf).slice(0, 16)}`);
  }
  // owner Q8: every candidate creek crossing, piped or left a bridge, and why
  // (into the baseline beside the shipped files; beside the export otherwise)
  const csvDir = resolve(dir) === resolve(RES) ? join(HERE, 'baseline') : dir;
  const llOf = (x, z) => `${(LAT0 + z / M_LAT).toFixed(5)} ${(LON0 + x / M_LON).toFixed(5)}`;
  const clsName = (rank, link) => ['local', 'tertiary', 'secondary', 'primary', 'trunk', 'motorway'][rank] + (link ? '_link' : '');
  const q = v => `"${String(v ?? '').replace(/"/g, "'")}"`;
  const rows = [...CULV.rows].sort((a, b) => (a.why === 'CULVERT' ? 0 : 1) - (b.why === 'CULVERT' ? 0 : 1) || tierOf(a.rank) - tierOf(b.rank) || a.edge - b.edge || a.xi - b.xi);
  writeFileSync(join(csvDir, 'culverts_q8.csv'), 'decision,tier,edge,way,class,name,creek,culvertWay,culvertName,culvertFromCrossingM,culvertFromRibbonM,culvertAlongRoadM,latlon,reason\n' +
    rows.map(r => [r.why === 'CULVERT' ? 'CULVERT' : 'BRIDGE', 'T' + tierOf(r.rank), r.edge, r.way, clsName(r.rank, r.link), q(r.name), q(r.water), r.culvertWay, q(r.culvertName),
                   Number.isFinite(r.dP) ? r.dP.toFixed(1) : '', Number.isFinite(r.dRib) ? r.dRib.toFixed(1) : '', Number.isFinite(r.alongOff) ? r.alongOff.toFixed(1) : '', llOf(r.x, r.z), q(r.why === 'CULVERT' ? (r.shared !== undefined ? `on the same fill as the piped crossing of e${r.shared} (within 25 m on the creek)` : '') : r.why)].join(',')).join('\n') + '\n');
  console.log(`wrote ${relative(UNITY, join(csvDir, 'culverts_q8.csv'))} (${rows.length} candidate creek crossings)`);
}
if (MODE.fingerprint) {
  const path = resolve(UNITY, MODE.fingerprint);
  writeFileSync(path, JSON.stringify(makeFingerprint(), null, 2) + '\n');
  console.log(`wrote ${relative(UNITY, path)}`);
}
if (MODE.check) {
  console.log('\n==== CHECK ====');
  // 1. inputs against the manifest
  if (!existsSync(MANIFEST)) { console.log('inputs: no cache_manifest.json to check against'); failed = true; }
  else {
    const want = new Map(JSON.parse(readFileSync(MANIFEST, 'utf8')).inputs.map(d => [d.file, d]));
    const have = describeInputs();
    let bad = 0;
    for (const d of have) {
      const w = want.get(d.file);
      if (!w) { console.log(`  input NEW       ${d.file}`); bad++; continue; }
      want.delete(d.file);
      if (w.sha256 !== d.sha256) {
        console.log(`  input CHANGED   ${d.file}  ${w.sha256.slice(0, 12)} -> ${d.sha256.slice(0, 12)}` +
                    (d.timestamp_osm_base ? `  snapshot ${w.timestamp_osm_base} -> ${d.timestamp_osm_base}` : ''));
        bad++;
      }
    }
    for (const w of want.values()) { console.log(`  input MISSING   ${w.file}`); bad++; }
    console.log(bad ? `inputs: ${bad} differ from cache_manifest.json` : `inputs: all ${have.length} match cache_manifest.json`);
    if (bad) failed = true;
  }
  // 2. the four files, byte for byte
  const dir = resolve(UNITY, MODE.against);
  for (const [name, buf] of OUTPUTS) {
    const p = join(dir, name);
    if (!existsSync(p)) { console.log(`  MISSING  ${name} (not in ${relative(UNITY, dir)})`); failed = true; continue; }
    const old = readFileSync(p);
    if (old.equals(buf)) { console.log(`  IDENTICAL  ${name}  ${buf.length} B  sha256 ${sha256(buf).slice(0, 16)}`); continue; }
    let at = 0;
    while (at < Math.min(old.length, buf.length) && old[at] === buf[at]) at++;
    console.log(`  DIFFERENT  ${name}  shipped ${old.length} B, export ${buf.length} B, first difference at byte ${at}`);
    failed = true;
  }
  // 3. the fingerprint (what survives a Node change that moves low bits)
  if (existsSync(FINGERPRINT)) {
    const d = diffJson(withoutVolatile(makeFingerprint()), withoutVolatile(JSON.parse(readFileSync(FINGERPRINT, 'utf8'))));
    if (d.length) {
      console.log(`fingerprint: ${d.length} differences from tools/city/fingerprint.json (was -> now)`);
      for (const l of d.slice(0, 40)) console.log('  ' + l);
      failed = true;
    } else console.log('fingerprint: identical to tools/city/fingerprint.json');
  } else { console.log('fingerprint: no tools/city/fingerprint.json yet'); failed = true; }
  console.log(failed ? 'EXPORT CHECK: FAILED' : 'EXPORT CHECK OK: the cache reproduces the shipped data byte for byte');
}
if (failed) process.exitCode = 1;
