// Bake ONE Charlotte street venue into a playable stage: OpenStreetMap
// centreline, SRTM elevation, and the files Unity needs.
//
//   node tools/clt/fetch_clt.mjs <uptown|tryon|independence> [--route] [--dry]
//
// This is the fourth road bake and the first that is NOT a copy: every helper
// it shares with tools/roads/fetch_road.mjs lives in tools/roads/lib.mjs. What
// is new here is what a city needs and a mountain does not:
//
//   * A WAY-ID LIST per venue, verified against live Overpass on 2026-09-07.
//     fetch_road.mjs chains ways by matching endpoints, which on a divided
//     freeway (both carriageways of the Belk are named "John Belk Freeway")
//     or where Tryon splits into a one-way pair picks whichever branch matches
//     first — the probe's first attempt left the freeway and wandered 19 km up
//     Beatties Ford Road. A list of way ids in route order is the exact
//     answer, and it is data, not a rule that can be wrong on the next road.
//   * A ONEWAY-RESPECTING ROUTER as the fallback for a spec that has only
//     anchors (or `--route` to cross-check a list). tools/bogue/route.mjs
//     ignores oneway on purpose; on a freeway loop that hops carriageways.
//     For a loop it tries every candidate node within 400 m of each anchor
//     and keeps the shortest CLOSED route — the nearest node alone lands on
//     the wrong carriageway and the router leaves the freeway to get back.
//   * A CYCLIC stage. UPTOWN LOOP is the I-277 belt: the spline is periodic,
//     the smoother wraps, the corner test includes the seam, and the JSON
//     says `loop` so TrackCatalog races it by laps instead of to a finish.
//   * The CITY FRAME. Each bake writes where its origin sits in
//     charlotte_city.json's coordinates, so the stage builder can find uptown
//     (Trade & Tryon) and put the towers where the towers are.
//   * A CARRIAGEWAY-JOG TAPER. Where a two-way street divides into a one-way
//     pair, OSM's centreline steps sideways onto the carriageway in a few
//     metres; the spline reads that as a 6 m corner on a road that has no
//     corner there. Recognised by its tags, replaced by the lane-shift taper
//     a real road uses (see taperCarriagewayJogs).
//
// Speed limits are informational here (mph, as posted); the catalog carries
// them in km/h on TrackDef.speedLimitKmh, which is what the game reads.

import { mkdirSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  fetchOverpass, srtmSampler, makeProjection, dist2,
  arcPositions, pointAtS, projectOntoChain, splineResample,
  tightestPlan, radiusFloorMessage, minSelfClearance, waypointBridgeFlags,
  smoothHeights, profileStats, bridgeSpans, bakeGrid, writeDemMeta, writeStage,
} from '../roads/lib.mjs';
import { chainByIds, routeByAnchors, onewayOf, bridgeOf, lanesOf } from '../roads/route.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const cacheDir = join(here, 'cache');                       // gitignored
// The SRTM tile is 8 MB and already sits in the roads cache (also
// gitignored); one copy per tool would be one download per tool.
const srtmCache = join(here, '..', 'roads', 'cache');
const projectRoot = join(here, '..', '..');
mkdirSync(cacheDir, { recursive: true });

// ------------------------------------------------------------ constants
const SPACING = 4;                 // TrackCatalog.Spacing
const RADIUS_FLOOR_M = 12;         // the self-test's stage floor (the CAR's)
// A span has to be longer than the builder's two approach ramps
// (2 x TrackCatalog.BridgeRampM = 52 m) or the gorge under it never reaches
// full depth and the deck spans a saucer; the self-test refuses shorter
// ones. Blowing Rock's 40 m and 48 m culvert bridges were the first to fall
// between the old 40 m floor and that rule (2026-09-11).
const MIN_BRIDGE_M = 56, BRIDGE_MERGE_M = 30;
const NEAR_CELL = 12, NEAR_MARGIN = 1200;
const FAR_CELL = 60, FAR_MARGIN = 9000;
/// The fuel ceiling: the thirstiest stage-4 car in the catalog (the Escudo)
/// reaches the self-test's 85% tank at this distance at race load. A venue
/// whose RaceMeters passes it fails a 40-minute build; printed here instead.
const FUEL_CEILING_M = 14200;
const USER_AGENT = 'psx-racing-clt-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)';

/// charlotte_city.json's frame, from RG2 tools/osm/build.mjs and
/// tools/city/export_charlotte.mjs: a plain equirectangular about the
/// fixture's centre with 111132 m per degree of latitude and 111320*cos(lat0)
/// per degree of longitude. Repeated here rather than imported because the
/// exporter reads them out of the RG2 fixture at run time.
const CITY_LAT0 = 35.18456015184093, CITY_LON0 = -80.81770185962013;
const CITY_M_LAT = 111132, CITY_M_LON = 111320 * Math.cos(CITY_LAT0 * Math.PI / 180);
const cityXZ = (lat, lon) => ({ x: (lon - CITY_LON0) * CITY_M_LON, z: (lat - CITY_LAT0) * CITY_M_LAT });

// ---------------------------------------------------------------- venues
// Way-id sequences verified live on 2026-09-07 (route order). `anchors` are
// the router's via points in route order; `start`/`end` are where the lines
// go (a loop has no end — waypoint 0 is the line).
const VENUES = {
  // The 277 belt, clockwise: I-77 NB -> Brookshire Fwy EB -> John Belk Fwy WB
  // -> ramps -> I-77. One carriageway; 1.6 km of it on structure.
  uptown: {
    name: 'Uptown Loop — I-277',
    artDir: 'CLT', prefix: 'clt_uptown', resKey: 'clt_uptown',
    bbox: { s: 35.205, w: -80.880, n: 35.250, e: -80.815 },
    classes: ['motorway', 'motorway_link'],
    prefer: { names: [], refs: ['I 277', 'I 77'] },
    // Waypoint 0 on the long Brookshire straight so the grid has 60 m of
    // straight road behind it.
    start: { lat: 35.2367939, lon: -80.8401828 },
    anchors: [{ lat: 35.2367939, lon: -80.8401828 },   // Brookshire EB
              { lat: 35.2200, lon: -80.8300 },         // the Belk
              { lat: 35.2270, lon: -80.8630 }],        // I-77 NB
    wayIds: [101537866,101537836,101537860,122753230,159022507,159022518,999013937,
      159022506,159022515,159022502,159022498,159022496,159022509,159022503,648310354,
      159022510,166576479,166576475,449112229,122753228,159022511,159022517,51062795,
      159022548,1193243346,159022547,51062772,159022552,159022545,55249555,55249553,
      55204694,999046226,55204686,94405125,992734649,94405118,159072248,159072239,
      120108305,449112238,159072322,94750534,122082583,94750518,1343703528,94750516,
      94750540,94745382,648861197,94745335,40153335,648861047,836960240,1340236493,
      836960239,836960241,16661475,836960242,449113974,122753223,836960235,836960234,
      101537874],
    loop: true, laps: 1, leadM: 0, shutdownM: 0,
    smoothSigma: 85, maxGrade: 0.085,
    roadWidthM: 14.6, lanes: 3, speedLimitMph: 50, oneWay: true,
  },

  // South End to NoDa straight up Tryon. Two-way to ~12th Street, then the
  // northbound carriageway of the US-29/NC-49 one-way pair.
  tryon: {
    name: 'Tryon Street Sprint',
    artDir: 'CLT', prefix: 'clt_tryon', resKey: 'clt_tryon',
    bbox: { s: 35.200, w: -80.875, n: 35.258, e: -80.795 },
    classes: null,                                     // every drivable class
    prefer: { names: ['tryon street'], refs: [] },
    start: { lat: 35.2161102, lon: -80.8562172, elevM: 227 },  // S Tryon @ Camden Rd
    end:   { lat: 35.2527695, lon: -80.8081048, elevM: 214 },  // N Tryon @ E 36th St
    anchors: [{ lat: 35.2161102, lon: -80.8562172 }, { lat: 35.2527695, lon: -80.8081048 }],
    wayIds: [1343934894,1343934893,1128432446,129834072,1128437577,1128432445,1128432444,
      129834070,51063159,34764092,34764115,1183425772,130144233,130144237,1183425773,
      1123907747,1183425774,16714405,1183425775,1051632137,648281647,16674240,645263197,
      1367659007,1255272355,502495542,1051039408,502495543,1255950645,654682186,713030468,
      1343934255,648297750,732748582,1430838847,1031979931,1031979929,1031979930,
      1365969253,1345043673,1345043674,1345043672,886386517,1345038519,1345038520,
      1345038521,732748585,1345155870,1345155871,1345155872,1345099188,952112777,
      // 1546936566 is the 147 m of the NB carriageway between 35th and 36th
      // that the verified list skipped — chainByIds refused the jump, a live
      // query of the gap found exactly one N Tryon way filling it.
      1055083832,736548240,1546936566,1055289169,1055016922,648281611],
    loop: false, laps: 1, leadM: 60, shutdownM: 250,
    smoothSigma: 85, maxGrade: 0.085,
    roadWidthM: 15.5, lanes: 4, speedLimitMph: 35, oneWay: false,
  },

  // Off the Belk and east down the Independence Expressway to the Sharon
  // Amity crossing. The Belk EB carriageway before the split is the lead-in.
  independence: {
    name: 'Independence Sprint — US 74',
    artDir: 'CLT', prefix: 'clt_independence', resKey: 'clt_independence',
    bbox: { s: 35.175, w: -80.845, n: 35.225, e: -80.755 },
    classes: ['motorway', 'motorway_link', 'trunk', 'trunk_link'],
    prefer: { names: ['independence', 'john belk'], refs: ['US 74'] },
    start: { lat: 35.2194592, lon: -80.8249734 },      // the I-277 / US 74 split
    end:   { lat: 35.194184,  lon: -80.7660976 },      // Sharon Amity overpass
    anchors: [{ lat: 35.2196, lon: -80.8285 },          // Belk EB, 300 m back (lead)
              { lat: 35.2194592, lon: -80.8249734 },
              { lat: 35.194184, lon: -80.7660976 }],
    wayIds: [34922866,648310357,51062775,159022530,159022529,159022531,159022535,
      1042942297,1042942296,1042934015,1042934016,51062789,1042945066,854801717,
      854801720,1122238767,1122238766,1012799821,1012799822,1123280092,1123280093,
      1123280096,1123280097,51062788,1123280099,1123280098,1123280095,1123280094,
      257836865,158903886,1023317415,648297613,1049913071,1076368826,158903888,
      648297612,574785444],
    loop: false, laps: 1, leadM: 60, shutdownM: 300,
    smoothSigma: 85, maxGrade: 0.085,
    roadWidthM: 16.0, lanes: 4, speedLimitMph: 55, oneWay: true,
  },
};

const KEY = process.argv[2];
const FLAGS = new Set(process.argv.slice(3));
const FORCE_ROUTE = FLAGS.has('--route');
const DRY = FLAGS.has('--dry');
if (!KEY || !VENUES[KEY]) {
  console.error('usage: node fetch_clt.mjs <' + Object.keys(VENUES).join('|') + '> [--route] [--dry]');
  process.exit(2);
}
const CFG = VENUES[KEY];
const BBOX = CFG.bbox;
console.log('=== ' + CFG.name + ' (' + KEY + (CFG.loop ? ', loop' : '') + ') ===');

// ------------------------------------------------- tags, chain, router
// All of it lives in tools/roads/route.mjs now (2026-09-11), shared with the
// Parkway loop bake. The tag readers, the way-id chainer and the oneway-
// respecting router below were this file's; the loop bake would have been a
// third copy of them. Every behaviour is unchanged: the three Charlotte bakes
// re-run byte-identical through the shared module.

// ---------------------------------------------------- carriageway jogs
// WHERE A TWO-WAY STREET DIVIDES, OSM STEPS SIDEWAYS. North Tryon is one
// centreline for five lanes up to 12th Street and then the NORTHBOUND
// CARRIAGEWAY of a one-way pair; the mapped line jumps from the middle of
// the road to the middle of that carriageway — 4.3 m across in 8 m along,
// +50/-25/-24 degrees on three consecutive vertices (and again at 33rd
// Street, 4.8 m in 9 m). Through the spline that is a 6 m plan radius, and
// the road has no corner there at all: the northbound lanes run straight on
// and the southbound ones leave. The refusal under the 12 m floor is right
// to fire — the roads memory's rule stands: measure, refuse, never smooth a
// REAL corner open — but this is not a corner, it is a change of datum, and
// a real road makes that change with a LANE-SHIFT TAPER.
//
// So a jog is recognised by its DATA signature, not by being tight: the
// oneway tag changes across it (a divided-road transition), the turns are
// sharp (>= 15 deg) and cancel (net < 10 deg) inside 15 m, so the road
// carries on the way it came. A hairpin nets 180 degrees, a junction turn
// 90, a chicane on a road that stays two-way has no oneway change: none of
// them qualify. The jog's vertices go, and the near-straight vertices for
// half a taper either side go with them, so the spline — which still passes
// through every vertex left, the true centreline on both sides — spreads the
// sidestep over the length MUTCD gives a lane shift: L = W*S at 45 mph and
// up, W*S^2/60 below (W and L in feet, S in mph; 2009 MUTCD section 6C.08),
// from the MEASURED offset and the venue's posted limit. Nothing moves
// further than the sidestep itself, and a bake with no divided transition
// (both freeways) is untouched — the pass prints what it did either way.
const JOG_TURN_MIN_DEG = 15, JOG_NET_MAX_DEG = 10, JOG_SPAN_MAX_M = 15;
/// A vertex turning less than this is "straight" and may be lifted out of
/// the taper's run so the shift can spread; a real curve's vertices stay.
const STRAIGHT_DEG = 3;

function taperLengthM(offsetM, mph) {
  const W = offsetM / 0.3048;
  const Lft = mph >= 45 ? W * mph : W * mph * mph / 60;
  return Lft * 0.3048;
}

function taperCarriagewayJogs(cut, flag, wayOf, mph) {
  const n = cut.length;
  const s = arcPositions(cut);
  const heading = k => Math.atan2(cut[k + 1].x - cut[k].x, cut[k + 1].z - cut[k].z);
  const turnDeg = k => {
    let d = heading(k) - heading(k - 1);
    while (d > Math.PI) d -= 2 * Math.PI;
    while (d < -Math.PI) d += 2 * Math.PI;
    return d * 180 / Math.PI;
  };
  const ow = k => wayOf[k] ? onewayOf(wayOf[k]) : 0;
  const drop = new Uint8Array(n);
  let jogs = 0;
  for (let i = 1; i + 1 < n; i++) {
    if (drop[i]) continue;
    const t0 = turnDeg(i);
    if (Math.abs(t0) < JOG_TURN_MIN_DEG) continue;
    let sum = t0, j = -1;
    for (let k = i + 1; k + 1 < n && s[k] - s[i] <= JOG_SPAN_MAX_M; k++) {
      const tk = turnDeg(k);
      sum += tk;
      if (Math.sign(tk) !== Math.sign(t0) && Math.abs(tk) >= JOG_TURN_MIN_DEG * 0.5 &&
          Math.abs(sum) < JOG_NET_MAX_DEG) { j = k; break; }
    }
    if (j < 0) continue;
    // The divided-road gate: the oneway status changes somewhere across it.
    let divided = false;
    for (let k = i - 1; k <= j; k++) if (ow(k) !== ow(k + 1)) divided = true;
    if (!divided) continue;
    // The sidestep: how far the road after the jog sits off the line the
    // road before it was running along.
    const ux = cut[i].x - cut[i - 1].x, uz = cut[i].z - cut[i - 1].z, ul = Math.hypot(ux, uz) || 1;
    const dx = cut[j].x - cut[i].x, dz = cut[j].z - cut[i].z;
    const offset = Math.abs((ux * dz - uz * dx) / ul);
    const L = taperLengthM(offset, mph);
    for (let k = i; k <= j; k++) drop[k] = 1;
    // Straight vertices within half a taper either side go too, stopping at
    // the first real curve vertex or bridge-flag change, and never the ends.
    const straight = k => k >= 1 && k + 1 < n && Math.abs(turnDeg(k)) < STRAIGHT_DEG &&
                          flag[k - 1] === flag[k] && flag[k] === flag[k + 1];
    let a = i - 1, b = j + 1;
    while (a >= 1 && s[i] - s[a] <= L / 2 && straight(a)) drop[a--] = 1;
    while (b + 1 < n && s[b] - s[j] <= L / 2 && straight(b)) drop[b++] = 1;
    jogs++;
    console.log(`  carriageway jog at ${s[i].toFixed(0)} m (way ${wayOf[i] ? wayOf[i].id : '?'} -> ` +
      `${wayOf[j + 1] ? wayOf[j + 1].id : '?'}): ${offset.toFixed(1)} m sidestep over ` +
      `${(s[j] - s[i]).toFixed(0)} m, tapered over ${(s[b] - s[a]).toFixed(0)} m ` +
      `(MUTCD L = ${L.toFixed(0)} m at ${mph} mph)`);
  }
  if (!jogs) { console.log('  no carriageway jogs'); return { cut, flag, wayOf }; }
  const keep = (_, k) => !drop[k];
  return { cut: cut.filter(keep), flag: flag.filter(keep), wayOf: wayOf.filter(keep) };
}

// ------------------------------------------------------------------ main
const useRouter = FORCE_ROUTE || !CFG.wayIds || !CFG.wayIds.length;
const overpass = useRouter
  ? await fetchOverpass({ cacheDir, name: 'overpass_' + KEY + '.json', userAgent: USER_AGENT,
      query: `[out:json][timeout:180];
way["highway"~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link)$"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
out tags geom;` })
  // Cached under the LIST, not the venue: correcting an id must fetch again,
  // or the corrected list reads yesterday's body and chains the old gap.
  : await fetchOverpass({ cacheDir, userAgent: USER_AGENT,
      name: `overpass_${KEY}_ways_${CFG.wayIds.length}_${CFG.wayIds.reduce((a, id) => (a * 31 + id) % 1000003, 7)}.json`,
      query: `[out:json][timeout:60];way(id:${CFG.wayIds.join(',')});out tags geom;` });
const elevAt = await srtmSampler(BBOX, srtmCache);

// A rough projection about the bbox centre for the chaining and routing;
// re-projected about the route's own centroid once it is known.
const proj0 = makeProjection((BBOX.s + BBOX.n) / 2, (BBOX.w + BBOX.e) / 2);
const { chain, flag, wayOf } = useRouter
  ? routeByAnchors(overpass, proj0, { anchors: CFG.anchors, loop: CFG.loop, classes: CFG.classes, prefer: CFG.prefer })
  : chainByIds(overpass, CFG.wayIds, proj0);

// Project about the route centroid so numbers stay small.
const cLat = chain.reduce((a, p) => a + p.lat, 0) / chain.length;
const cLon = chain.reduce((a, p) => a + p.lon, 0) / chain.length;
const proj = makeProjection(cLat, cLon);
let cut = chain.map(p => proj.toXZ(p.lat, p.lon));
let cutBridge = flag.slice();
let cutWay = wayOf.slice();

// Length-weighted lane count off the ways the route actually uses.
{
  let lm = 0, ln = 0, lo = Infinity, hi = 0;
  for (let i = 1; i < cut.length; i++) {
    const w = cutWay[i]; const n = w ? lanesOf(w) : 0;
    if (!n) continue;
    const m = Math.sqrt(dist2(cut[i - 1], cut[i]));
    lm += m; ln += m * n; lo = Math.min(lo, n); hi = Math.max(hi, n);
  }
  console.log(`lanes: ${lm > 0 ? (ln / lm).toFixed(2) : '?'} length-weighted (${lo}-${hi}), catalog roadWidthM ${CFG.roadWidthM}`);
}

let leadBorrow = 0, shutBorrow = 0, timedM = 0;
if (CFG.loop) {
  // A RING. It must close on itself, and waypoint 0 must be the start
  // anchor: the projected anchor is inserted as a vertex and the ring is
  // rotated to begin there, so the spline (which passes through every
  // vertex) puts the start line exactly where it was researched, and the
  // one odd-length closing segment stays at the seam where every circuit
  // already has one.
  const closeGap = Math.sqrt(dist2(cut[cut.length - 1], cut[0]));
  console.log(`way chain closes with a gap of ${closeGap.toFixed(2)} m`);
  if (closeGap > 30) throw new Error('the way list does not close into a loop');
  if (closeGap < 2) { cut.pop(); cutBridge.pop(); cutWay.pop(); }
  const ring = cut.concat([cut[0]]);
  const ringS = arcPositions(ring);
  const a = projectOntoChain(ring, ringS, proj.toXZ(CFG.start.lat, CFG.start.lon));
  console.log(`start anchor ${Math.sqrt(a.d2).toFixed(1)} m off the ring at s=${a.s.toFixed(0)}`);
  if (Math.sqrt(a.d2) > 100) throw new Error('the start anchor is not on this ring');
  // Which segment, and split it there.
  let seg = 0;
  while (seg + 1 < ringS.length && ringS[seg + 1] < a.s) seg++;
  const at = pointAtS(ring, ringS, a.s);
  const n = cut.length;
  const rot = [], rotB = [], rotW = [];
  const startsOnVertex = Math.sqrt(dist2(at, cut[seg % n])) < 0.5;
  if (!startsOnVertex) { rot.push(at); rotB.push(cutBridge[seg % n]); rotW.push(cutWay[seg % n]); }
  for (let k = 1; k <= n; k++) {
    const i = (seg + k) % n;
    if (startsOnVertex && k === n) { rot.unshift(cut[seg % n]); rotB.unshift(cutBridge[seg % n]); rotW.unshift(cutWay[seg % n]); break; }
    rot.push(cut[i]); rotB.push(cutBridge[i]); rotW.push(cutWay[i]);
  }
  cut = rot; cutBridge = rotB; cutWay = rotW;
} else {
  // TWO ANCHORS on an open road: the timed section between them, lead
  // before the start for the grid and shutdown after the finish — borrowed
  // from the run when the listed ways do not reach that far, exactly as
  // fetch_road.mjs does for a dead-end spur.
  const chainS = arcPositions(cut);
  const aS = projectOntoChain(cut, chainS, proj.toXZ(CFG.start.lat, CFG.start.lon));
  const aE = projectOntoChain(cut, chainS, proj.toXZ(CFG.end.lat, CFG.end.lon));
  console.log(`start anchor ${Math.sqrt(aS.d2).toFixed(0)} m off the chain at s=${aS.s.toFixed(0)}`);
  console.log(`end   anchor ${Math.sqrt(aE.d2).toFixed(0)} m off the chain at s=${aE.s.toFixed(0)}`);
  if (Math.sqrt(aS.d2) > 250 || Math.sqrt(aE.d2) > 250)
    throw new Error('an anchor is a long way off the route — wrong way list or the route took a wrong branch');
  if (aE.s <= aS.s) throw new Error('the route runs from the end anchor to the start anchor — reverse the way list');
  const sLo = Math.max(0, aS.s - CFG.leadM);
  const sHi = Math.min(chainS.at(-1), aE.s + CFG.shutdownM);
  leadBorrow = Math.max(0, CFG.leadM - (aS.s - sLo));
  shutBorrow = Math.max(0, CFG.shutdownM - (sHi - aE.s));
  timedM = (aE.s - aS.s) - leadBorrow - shutBorrow;
  if (leadBorrow > 1 || shutBorrow > 1)
    console.log(`  road runs out: start line moved ${leadBorrow.toFixed(0)} m in, ` +
                `finish ${shutBorrow.toFixed(0)} m back — timed section ${(timedM / 1000).toFixed(2)} km`);
  const win = [], winB = [], winW = [];
  const lo = pointAtS(cut, chainS, sLo), hi = pointAtS(cut, chainS, sHi);
  win.push(lo); winB.push(cutBridge[0]); winW.push(cutWay[0]);
  for (let i = 0; i < cut.length; i++)
    if (chainS[i] > sLo + 0.01 && chainS[i] < sHi - 0.01) { win.push(cut[i]); winB.push(cutBridge[i]); winW.push(cutWay[i]); }
  win.push(hi); winB.push(cutBridge[cut.length - 1]); winW.push(cutWay[cut.length - 1]);
  cut = win; cutBridge = winB; cutWay = winW;
}
console.log(`cut vertices: ${cut.length}`);

// Where the street divides, taper the centreline's sidestep (see above).
{
  const t = taperCarriagewayJogs(cut, cutBridge, cutWay, CFG.speedLimitMph);
  cut = t.cut; cutBridge = t.flag; cutWay = t.wayOf;
}

// Spline-resample to 4 m waypoints — periodic on the ring.
let wp = splineResample(cut, SPACING, { loop: CFG.loop });
let closureGap = 0;
if (CFG.loop) {
  // TrackCatalog.Sample's own closure rule, applied until it holds: drop the
  // last point while it sits within half a spacing of the first, so the
  // closing segment is between 2 and 6 m and never a sliver.
  while (wp.length > 2 && Math.sqrt(dist2(wp[wp.length - 1], wp[0])) < SPACING * 0.5) wp.pop();
  closureGap = Math.sqrt(dist2(wp[wp.length - 1], wp[0]));
  console.log(`loop closes: ${wp.length} waypoints, closing segment ${closureGap.toFixed(2)} m`);
}
const lengthM = CFG.loop ? wp.length * SPACING : (wp.length - 1) * SPACING;
console.log(`waypoints: ${wp.length} (${(lengthM / 1000).toFixed(2)} km)`);

// The tightest corner and the self-clearance, measured here because the
// self-test asserts both and a failure there costs a forty-minute build.
{
  const t = tightestPlan(wp, RADIUS_FLOOR_M, { loop: CFG.loop });
  console.log(`tightest corner ${t.minR.toFixed(1)} m at wp ${t.at} (${(t.at * SPACING / 1000).toFixed(2)} km)` +
              (t.under ? `  — ${t.under} waypoint(s) under the ${RADIUS_FLOOR_M} m floor` : ''));
  if (t.under) throw new Error(radiusFloorMessage(t, RADIUS_FLOOR_M));
  const c = minSelfClearance(wp, { loop: CFG.loop });
  // What the stage self-test demands: roadWidth + 2 * (roadWidth/2 + 1.15 m verge).
  const need = CFG.roadWidthM + 2 * (CFG.roadWidthM * 0.5 + 1.15);
  console.log(`min self-clearance ${c.minM.toFixed(1)} m between wp ${c.at[0]} and ${c.at[1]} (need ${need.toFixed(1)} m)`);
  if (c.minM < need) throw new Error('the route runs into its own barriers');
}

// Start and finish lines, measured on the WAYPOINT LIST — the road the game
// drives — by projecting the anchors onto it.
let wpStart = 0, wpFinish = 0;
if (!CFG.loop) {
  const wpS = arcPositions(wp);
  wpStart = projectOntoChain(wp, wpS, proj.toXZ(CFG.start.lat, CFG.start.lon)).s + leadBorrow;
  wpFinish = projectOntoChain(wp, wpS, proj.toXZ(CFG.end.lat, CFG.end.lon)).s - shutBorrow;
  const drift = (wpFinish - wpStart) - timedM;
  if (Math.abs(drift) > Math.max(100, timedM * 0.02))
    throw new Error(`timed section measures ${((wpFinish - wpStart) / 1000).toFixed(2)} km on the route ` +
                    `but ${(timedM / 1000).toFixed(2)} km on the chain`);
  if (wpFinish > wpS[wpS.length - 1] - 10) throw new Error('no shutdown left past the finish');
  console.log(`start line ${wpStart.toFixed(0)} m, finish ${wpFinish.toFixed(0)} m of ${wpS[wpS.length - 1].toFixed(0)} m of route`);
}

// Heights: SRTM at every waypoint, smoothed and grade-clamped — circularly
// on the ring so the profile has no seam at the start line.
const wpBridge = waypointBridgeFlags(wp, cutBridge, { loop: CFG.loop });
const rawH = wp.map(p => { const ll = proj.toLL(p.x, p.z); return elevAt(ll.lat, ll.lon); });
const smoothH = smoothHeights(rawH, { spacing: SPACING, sigma: CFG.smoothSigma,
                                      maxGrade: CFG.maxGrade, circular: CFG.loop });
const stats = profileStats(smoothH, SPACING, { circular: CFG.loop });
const baseM = Math.floor(stats.minH - 40);
console.log(`route elevation ${stats.minH.toFixed(0)}..${stats.maxH.toFixed(0)} m ASL, ` +
  `max grade ${(stats.maxGrade * 100).toFixed(1)}%, min vertical radius ${stats.minVertR.toFixed(0)} m, baseM ${baseM}`);
if (CFG.start.elevM) console.log(`  surveyed start ${CFG.start.elevM} m, SRTM says ${smoothH[Math.round(wpStart / SPACING)].toFixed(0)}`);
if (CFG.end && CFG.end.elevM) console.log(`  surveyed end ${CFG.end.elevM} m, SRTM says ${smoothH[Math.round(wpFinish / SPACING)].toFixed(0)}`);

// SRTM IN A CITY READS ROOFS. The smoothed profile hides that under the
// road; the ground BESIDE it does not. Report how far the raw DEM stands
// above the road within 100 m, so the roof lumps are a number before they
// are a screenshot.
{
  let worst = 0, at = -1, out = 0;
  for (let i = 0; i < wp.length; i += 4) {
    const n = CFG.loop ? wp[(i + 1) % wp.length] : wp[Math.min(i + 1, wp.length - 1)];
    const dx = n.x - wp[i].x, dz = n.z - wp[i].z, l = Math.hypot(dx, dz) || 1;
    const rx = dz / l, rz = -dx / l;
    for (const d of [-100, -60, -30, 30, 60, 100]) {
      const ll = proj.toLL(wp[i].x + rx * d, wp[i].z + rz * d);
      const rise = elevAt(ll.lat, ll.lon) - smoothH[i];
      if (rise > worst) { worst = rise; at = i; out = d; }
    }
  }
  console.log(`raw DEM above the road within 100 m: max ${worst.toFixed(1)} m at wp ${at} (${out} m out)`);
}

// Bridge spans in metres-along (seam-merged on the ring).
const bridges = bridgeSpans(wpBridge, SPACING, { minM: MIN_BRIDGE_M, mergeM: BRIDGE_MERGE_M, loop: CFG.loop });
const bridgeM = bridges.reduce((a, s) => a + (s[1] - s[0]), 0);
console.log(`bridge spans: ${bridges.map(s =>
  `${s[0].toFixed(0)}-${s[1].toFixed(0)} (${(s[1] - s[0]).toFixed(0)} m)`).join(', ') || 'none'}` +
  ` — ${bridgeM.toFixed(0)} m on structure`);

// The race, against the fuel ceiling.
const raceM = CFG.loop ? lengthM * CFG.laps : wpFinish - wpStart;
console.log(`RaceMeters ${(raceM / 1000).toFixed(2)} km` + (CFG.loop ? ` (${CFG.laps} lap)` : '') +
  ` = ${(raceM / FUEL_CEILING_M * 100).toFixed(0)}% of the ${FUEL_CEILING_M / 1000} km fuel ceiling` +
  (raceM > FUEL_CEILING_M ? '  — OVER: the self-test will fail this venue' : ''));

// The city frame: where this stage's origin sits in charlotte_city.json.
const origin = proj.toLL(0, 0);
const city0 = cityXZ(origin.lat, origin.lon);
console.log(`city frame origin: cityX0 ${city0.x.toFixed(1)}, cityZ0 ${city0.z.toFixed(1)}`);

if (DRY) { console.log('DRY RUN — nothing written'); process.exit(0); }

// ------------------------------------------------------------- outputs
const artDir = join(projectRoot, 'Assets', 'PSXRacing', 'Art', CFG.artDir);
const grid = (name, cell, margin) =>
  bakeGrid({ wp, proj, elevAt, baseM, cell, margin, outDir: artDir, name });
const near = grid(CFG.prefix + '_dem_near', NEAR_CELL, NEAR_MARGIN);
const far = grid(CFG.prefix + '_dem_far', FAR_CELL, FAR_MARGIN);
writeDemMeta(artDir, CFG.prefix, baseM, near, far);

writeStage(join(projectRoot, 'Assets', 'PSXRacing', 'Resources', CFG.resKey + '.json'), {
  name: CFG.name,
  attribution: 'Route data (c) OpenStreetMap contributors. Elevation: USGS/NASA SRTM.',
  proj, baseM, spacing: SPACING,
  // On the ring waypoint 0 IS the line and there is no finish: both zero,
  // and TrackCatalog races it by laps.
  startLineM: CFG.loop ? 0 : wpStart,
  finishM: CFG.loop ? 0 : wpFinish,
  bridges, wp, heights: smoothH,
  extra: {
    loop: CFG.loop,
    oneWay: CFG.oneWay,
    speedLimitMph: CFG.speedLimitMph,   // informational — the catalog carries km/h
    lanes: CFG.lanes,
    roadWidthM: CFG.roadWidthM,
    inCity: true, cityX0: +city0.x.toFixed(2), cityZ0: +city0.z.toFixed(2),
  },
});
console.log('OK');
