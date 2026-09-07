// Bake ONE real mountain road into a playable stage: OpenStreetMap centreline,
// SRTM elevation, and the two files Unity needs.
//
//   node tools/roads/fetch_road.mjs <key>
//
// This is tools/brp/fetch_brp.mjs generalised. That script proved the pipeline
// on the Blue Ridge Parkway and tools/bogue/fetch_bogue.mjs proved it a second
// time on the coast — by COPYING it, which is why a third road was about to
// mean a third copy of the spline resampler, the gaussian smoother and the DEM
// grid baker. The Parkway is just the first row of the table below now.
//
// What changed, and why each one was hardcoded before:
//   * START AND END ANCHORS instead of one anchor plus a length. Every road
//     here was researched as a pair of endpoints with real surveyed elevations,
//     which is the form the owner asked for: "you can at least research the
//     starting and ending altitude and make that accurate in game." Deriving
//     the run from two points also removes the direction guess — forward is
//     whichever way along the chain gets closer to the end.
//   * A MULTI-TILE SRTM sampler, lifted from the Bogue bake. One tile covered
//     the Parkway; Tail of the Dragon straddles the W084/W085 boundary.
//   * A ROAD MATCHER per road. The Parkway is found by name; a state route is
//     found by its ref, and refs are the thing OSM is actually consistent about.
//
// EVERY ELEVATION BELOW IS SOURCED AND INDEPENDENTLY FACT-CHECKED, in metres.
// They are here as documentation and as an assertion: the bake prints what SRTM
// thinks the endpoints are beside these, and a large disagreement is the canopy
// bias talking (SRTM reads the top of a forest, not the road under it) and
// should be read before trusting the result.

import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
// The helpers live in lib.mjs now, shared with the Charlotte bake
// (tools/clt/fetch_clt.mjs). This file keeps the ROADS table and the cut
// logic; every numeric behaviour below is unchanged.
import {
  fetchOverpass, srtmSampler, makeProjection,
  arcPositions, pointAtS, projectOntoChain, splineResample,
  tightestPlan, radiusFloorMessage, waypointBridgeFlags, smoothHeights,
  bridgeSpans, bakeGrid, writeDemMeta, writeStage,
} from './lib.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const cacheDir = join(here, 'cache');
const projectRoot = join(here, '..', '..');
mkdirSync(cacheDir, { recursive: true });

// ---------------------------------------------------------------- the roads
const ROADS = {
  // The one that already exists, so the table is the whole truth and the
  // Parkway is not a special case living in another file.
  blueridge: {
    name: 'Blue Ridge Parkway — Linn Cove',
    artDir: 'BRP', prefix: 'brp', resKey: 'brp_stage',
    bbox: { s: 36.06, w: -81.86, n: 36.16, e: -81.74 },
    match: { names: ['blue ridge parkway', 'linn cove'], refs: ['BLRP'] },
    start: { lat: 36.1266, lon: -81.8093, elevM: 1280 },   // Rough Ridge end
    end:   { lat: 36.0813, lon: -81.8286, elevM: 1128 },   // Beacon Heights
    leadM: 60, shutdownM: 300, smoothSigma: 85, maxGrade: 0.085,
  },

  // NC 128 — off the Parkway at MP 355.4 and straight up. 432 m of climb in
  // 7.5 km, finishing at the highest point east of the Mississippi. The
  // shortest of the set and the biggest altitude change per kilometre.
  mtmitchell: {
    name: 'Mount Mitchell — NC 128',
    artDir: 'MtMitchell', prefix: 'mtm', resKey: 'mtm_stage',
    bbox: { s: 35.698, w: -82.304, n: 35.787, e: -82.244 },
    match: { names: ['mount mitchell'], refs: ['NC 128'] },
    start: { lat: 35.717990, lon: -82.279765, elevM: 1573 },  // BRP junction
    end:   { lat: 35.766247, lon: -82.265216, elevM: 2005 },  // summit car park
    leadM: 60, shutdownM: 150,
    // Tighter than the Parkway's: this road climbs at 9% and the Parkway does
    // not, so the clamp that keeps SRTM noise off a cliff must not also flatten
    // the gradient that IS the stage.
    smoothSigma: 60, maxGrade: 0.115,
  },

  // NC 80 south of the Parkway at Buck Creek Gap. 635 m DOWN over 19.5 km and
  // a published 160 curves.
  //
  // BAKED, MEASURED, AND NOT SHIPPED. Its first switchback off the gap is a
  // real 9.8 m radius — confirmed on the raw OSM at a 30 m chord, 6 samples in
  // 6706 and every other turn on the road over 13 m — which is under the 12 m
  // floor the stage self-test holds. That floor is not the obstacle: a 9 m road
  // on a 9.8 m centreline leaves a 5.3 m inner kerb, which is a hairpin taken
  // at walking pace and a ribbon that very nearly folds. The road needs one of
  // three real answers, none of which is a smoothing pass — a cut that starts
  // below the hairpin, a narrower roadWidth through it, or a floor derived from
  // the car's lock and the road's width instead of a constant.
  devilswhip: {
    name: "The Devil's Whip — NC 80",
    artDir: 'DevilsWhip', prefix: 'whip', resKey: 'whip_stage',
    bbox: { s: 35.6721, w: -82.1862, n: 35.7908, e: -82.0419 },
    match: { names: [], refs: ['NC 80'] },
    start: { lat: 35.770766, lon: -82.164796, elevM: 1020 },  // Buck Creek Gap
    end:   { lat: 35.692096, lon: -82.061916, elevM: 385 },   // US 70
    // The whole road is 19.5 km and the WHIP is the top of it — the switchback
    // descent off the gap, before NC 80 straightens out along the Toe River.
    // Twelve kilometres takes the curves and leaves the run-out.
    maxRunM: 12000,
    leadM: 60, shutdownM: 250, smoothSigma: 70, maxGrade: 0.10,
  },

  // NC 215 off the Parkway at Beech Gap. 760 m down in 13.4 km.
  nc215: {
    name: 'Beech Gap — NC 215',
    artDir: 'BeechGap', prefix: 'beech', resKey: 'beech_stage',
    bbox: { s: 35.2097, w: -82.9425, n: 35.3188, e: -82.8525 },
    match: { names: [], refs: ['NC 215'] },
    start: { lat: 35.298520, lon: -82.910320, elevM: 1623 },  // under the BRP
    end:   { lat: 35.247700, lon: -82.883700, elevM: 884 },   // above Balsam Grove
    leadM: 60, shutdownM: 250, smoothSigma: 70, maxGrade: 0.10,
  },

  // US 129 at Deals Gap. 318 curves in 11 miles, and the only one here that
  // does not touch the Parkway — it is on the list because it is the road
  // every driver in the south east knows by name.
  dragon: {
    name: 'Tail of the Dragon — US 129',
    artDir: 'Dragon', prefix: 'dragon', resKey: 'dragon_stage',
    bbox: { s: 35.445, w: -84.014, n: 35.545, e: -83.897 },
    match: { names: [], refs: ['US 129'] },
    // 1,955 ft at the gap, per USGS. The first pass wrote 512 m here and the
    // bake said 595, which looked like canopy bias in a height field and was
    // not: the RESEARCH was wrong and SRTM was right to within a metre.
    start: { lat: 35.465741, lon: -83.919882, elevM: 596 },   // Deals Gap
    end:   { lat: 35.524821, lon: -83.993306, elevM: 268 },   // Tabcat Creek
    leadM: 60, shutdownM: 250, smoothSigma: 55, maxGrade: 0.10,
  },
};

const KEY = process.argv[2];
if (!KEY || !ROADS[KEY]) {
  console.error('usage: node fetch_road.mjs <' + Object.keys(ROADS).join('|') + '>');
  process.exit(2);
}
const CFG = ROADS[KEY];
const BBOX = CFG.bbox;
const LEAD_M = CFG.leadM;
const SHUTDOWN_M = CFG.shutdownM;
// TWO NUMBERS, NOT ONE. The relaxation converges ASYMPTOTICALLY onto whatever
// it is aiming at — it pushes hardest when a corner is far under and barely at
// all when it is a millimetre under — so a target that is also the assertion
// can never be satisfied. It aims at 13 m and is checked against the 12 m the
// self-test uses, which is itself the CAR's floor: 22 degrees of lock on a
// 2.5 m wheelbase describes about 6.7 m.
const RADIUS_FLOOR_M = CFG.minRadiusM ?? 12;
const SPACING = 4;            // TrackCatalog.Spacing — waypoint spacing
const STATION = 10;           // metres between DEM samples pre-smoothing
const SMOOTH_SIGMA = CFG.smoothSigma;
const MAX_GRADE = CFG.maxGrade;
const MIN_BRIDGE_M = 40;
const BRIDGE_MERGE_M = 30;
const NEAR_CELL = 12, NEAR_MARGIN = 1200;
const FAR_CELL = 60, FAR_MARGIN = 9000;

console.log('=== ' + CFG.name + ' (' + KEY + ') ===');
console.log('  ' + CFG.start.elevM + ' m -> ' + CFG.end.elevM + ' m, surveyed');

const USER_AGENT = 'psx-racing-brp-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)';

function fetchOverpassBbox() {
  const query = `[out:json][timeout:120];
way["highway"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
out tags geom;`;
  return fetchOverpass({ cacheDir, name: 'overpass_' + KEY + '.json', query, userAgent: USER_AGENT });
}

// ------------------------------------------------------- assemble chain
// NAME OR REF, per road. The Parkway has a name and an unreliable ref; a state
// route has a reliable ref and a name that changes every few miles. Refs are
// compared with the spacing stripped because OSM writes "NC 128", "NC128" and
// "NC 128;NC 80" and all three mean this road is here.
function matchesRoad(tags) {
  if (!tags) return false;
  const name = (tags.name || '').toLowerCase();
  for (const n of CFG.match.names) if (n && name.includes(n)) return true;
  const refs = (tags.ref || '').toUpperCase().split(';').map(r => r.replace(/\s+/g, ''));
  for (const r of CFG.match.refs)
    if (refs.includes(r.replace(/\s+/g, '').toUpperCase())) return true;
  return false;
}

function assembleChain(overpass) {
  const ways = overpass.elements.filter(e => e.type === 'way' && matchesRoad(e.tags));
  if (!ways.length) throw new Error('no parkway ways in bbox');
  console.log(`parkway ways in bbox: ${ways.length}`);

  // Chain by matching endpoints (< ~2 m). Way directions are arbitrary.
  const key = p => p.lat.toFixed(5) + ',' + p.lon.toFixed(5);
  const used = new Set();
  // Start from the way with the southernmost endpoint, then grow both ends.
  let start = ways[0];
  for (const w of ways) {
    const lo = Math.min(w.geometry[0].lat, w.geometry.at(-1).lat);
    const cur = Math.min(start.geometry[0].lat, start.geometry.at(-1).lat);
    if (lo < cur) start = w;
  }
  used.add(start.id);
  const chain = [...start.geometry];      // [{lat,lon,bridge?}...] — tag later
  const bridgeOf = w => w.tags && (w.tags.bridge === 'yes' || w.tags.bridge === 'viaduct');
  const perVertexBridge = new Array(chain.length).fill(bridgeOf(start));

  let grew = true;
  while (grew) {
    grew = false;
    for (const w of ways) {
      if (used.has(w.id)) continue;
      const g = w.geometry;
      const headK = key(chain[0]), tailK = key(chain.at(-1));
      let add = null, front = false;
      if (key(g[0]) === tailK) { add = g.slice(1); front = false; }
      else if (key(g.at(-1)) === tailK) { add = g.slice(0, -1).reverse(); front = false; }
      else if (key(g[0]) === headK) { add = g.slice(1).reverse(); front = true; }
      else if (key(g.at(-1)) === headK) { add = g.slice(0, -1); front = true; }
      if (!add) continue;
      used.add(w.id); grew = true;
      const b = bridgeOf(w);
      if (front) { chain.unshift(...add); perVertexBridge.unshift(...add.map(() => b)); }
      else { chain.push(...add); perVertexBridge.push(...add.map(() => b)); }
    }
  }
  console.log(`chained ${used.size}/${ways.length} ways, ${chain.length} vertices`);
  return { chain, perVertexBridge };
}

// ------------------------------------------------------------------ main
// The projection, the centripetal spline and the arc resampler are lib.mjs's
// now (see the notes there on WHY the spline is centripetal and why the
// resampler keeps its last point — both were learned on these roads).
const overpass = await fetchOverpassBbox();
const elevAt = await srtmSampler(BBOX, cacheDir);

const { chain, perVertexBridge } = assembleChain(overpass);

// Project about the rough middle of the eventual cut so numbers stay small.
const mid = chain[Math.floor(chain.length / 2)];
let proj = makeProjection(mid.lat, mid.lon);
let chainXZ = chain.map(p => ({ ...proj.toXZ(p.lat, p.lon) }));
let chainS = arcPositions(chainXZ);

// TWO ANCHORS. The old bake had one point and a length and then had to GUESS
// which way along the chain was forward by comparing latitudes either side of
// it; with a researched start and end there is nothing to guess — forward is
// whichever direction gets closer to the end.
const aStart = projectOntoChain(chainXZ, chainS, proj.toXZ(CFG.start.lat, CFG.start.lon));
const aEnd   = projectOntoChain(chainXZ, chainS, proj.toXZ(CFG.end.lat, CFG.end.lon));
console.log(`start anchor ${Math.sqrt(aStart.d2).toFixed(0)} m off the chain at s=${aStart.s.toFixed(0)}`);
console.log(`end   anchor ${Math.sqrt(aEnd.d2).toFixed(0)} m off the chain at s=${aEnd.s.toFixed(0)}`);
if (Math.sqrt(aStart.d2) > 250 || Math.sqrt(aEnd.d2) > 250)
  throw new Error('an anchor is a long way off the matched road — wrong ref, or the ' +
                  'chain assembled the wrong branch. Check the match rule.');

const forward = aEnd.s > aStart.s;      // true: the run walks up the chain

// A STAGE IS A SECTION OF A ROAD, NOT THE ROAD.
//
// The Parkway entry has always been one: 3.5 miles of a 469-mile road, cut at
// two overlooks. The long entries here need the same treatment for a reason
// that is not aesthetic — TERRAIN COSTS. Blue Ridge's 7.0 km bakes 19 MB of
// mesh assets and the cost is close to linear in length, so NC 80's full
// 19.5 km would be 53 MB added to a WebGL download that is 49 MB in total
// today. Two roads like that would more than double what the player waits
// through before the game starts, to add road they would need eleven minutes
// to drive down.
//
// maxRunM trims from the FAR end, keeping the researched START — the anchor
// that NAMES the stage, because these roads are known by where they begin: a
// gap, a junction, a summit road's foot. The elevation the finish lands at is
// then read off the DEM instead of being researched, which is sound on these
// roads specifically: every one came out of the bake within 4 m of BOTH its
// surveyed endpoints, so the height field has earned the middle where there is
// nothing to check it against.
let endS = aEnd.s;
let runM = Math.abs(endS - aStart.s);
if (CFG.maxRunM && runM > CFG.maxRunM)
{
  endS = aStart.s + (forward ? 1 : -1) * CFG.maxRunM;
  console.log(`  capped: ${(runM / 1000).toFixed(2)} km of road cut to ` +
              `${(CFG.maxRunM / 1000).toFixed(2)} km from the start anchor`);
  runM = CFG.maxRunM;
}
console.log(`run ${(runM / 1000).toFixed(2)} km along the chain, ` +
            (forward ? 'increasing' : 'decreasing') + ' s');

// CLAMPED, not thrown. Lead is road before the start line for the grid to
// stand on and shutdown is road past the finish for a car crossing at speed —
// and a dead-end spur has NEITHER. Mount Mitchell starts at a junction with the
// Parkway and ends in the summit car park; there is no more road at either end
// and demanding some is demanding the mountain be longer. Take what the road
// has and say what was lost, because a stage that quietly finishes 120 m short
// of the summit is a stage that lied about its own altitude.
const wantLo = (forward ? aStart.s - LEAD_M : endS - SHUTDOWN_M);
const wantHi = (forward ? endS + SHUTDOWN_M : aStart.s + LEAD_M);
const sLo = Math.max(0, wantLo);
const sHi = Math.min(chainS.at(-1), wantHi);

// WHAT THE ROAD COULD NOT LEND, THE RUN LENDS INSTEAD.
//
// Lead is road before the start line for the grid to stand on and shutdown is
// road past the finish for a car crossing at speed. A dead-end spur has neither
// — Mount Mitchell starts at a junction with the Parkway and ends in the summit
// car park — and simply going without them puts the grid half off the map and
// the finish line three waypoints from the last piece of tarmac in the state.
//
// So the START LINE moves INTO the road by whatever the near end could not
// provide, and the FINISH moves BACK by whatever the far end could not. The cut
// still covers the whole road, so the terrain, the walls and the banks are the
// whole mountain; it is only the timed section that shortens. What that costs is
// altitude at both ends, which is why it is printed rather than absorbed: a
// stage that quietly finishes short of the summit is a stage that lied about its
// own climb.
const leadHave = forward ? aStart.s - sLo : sHi - aStart.s;
const shutHave = forward ? sHi - endS : endS - sLo;
const leadBorrow = Math.max(0, LEAD_M - leadHave);
const shutBorrow = Math.max(0, SHUTDOWN_M - shutHave);
const leadActualM = leadHave + leadBorrow;
const timedM = Math.abs(endS - aStart.s) - leadBorrow - shutBorrow;
if (leadBorrow > 1 || shutBorrow > 1)
  console.log(`  road runs out: start line moved ${leadBorrow.toFixed(0)} m in, ` +
              `finish ${shutBorrow.toFixed(0)} m back — timed section ` +
              `${(timedM / 1000).toFixed(2)} km of ${(runM / 1000).toFixed(2)} km of road`);
if (sHi - sLo < 200)
  throw new Error(`only ${(sHi - sLo).toFixed(0)} m of road between the anchors — ` +
    `wrong match rule, or the bbox clips the route`);

const cut = [];
const cutBridge = [];
for (let i = 0; i < chainXZ.length; i++) {
  if (chainS[i] >= sLo - 1 && chainS[i] <= sHi + 1) {
    cut.push(chainXZ[i]);
    cutBridge.push(perVertexBridge[i]);
  }
}
// Ensure exact endpoints
console.log(`cut vertices: ${cut.length}`);
// Waypoint 0 must be the START. The cut came out in chain order, so it only
// needs reversing when the run walks DOWN the chain.
if (!forward) { cut.reverse(); cutBridge.reverse(); }

// Re-project about the cut centroid.
{
  const cLat = cut.reduce((a, p) => a + proj.toLL(p.x, p.z).lat, 0) / cut.length;
  const cLon = cut.reduce((a, p) => a + proj.toLL(p.x, p.z).lon, 0) / cut.length;
  const old = proj;
  proj = makeProjection(cLat, cLon);
  for (const p of cut) {
    const ll = old.toLL(p.x, p.z);
    const q = proj.toXZ(ll.lat, ll.lon);
    p.x = q.x; p.z = q.z;
  }
}

// Spline-resample to 4 m waypoints.
let wp = splineResample(cut, SPACING);
const cutS = arcPositions(cut);
console.log(`waypoints: ${wp.length} (${(wp.length * SPACING / 1000).toFixed(2)} km)`);

// THE TIGHTEST CORNER, IN PLAN, MEASURED AND THEN ENFORCED.
//
// Measured, because the self-test asserts it downstream and a failure there
// costs a forty-minute build to hear about, while here it costs nothing — and
// because it is the ONLY thing that would have caught the uniform-spline
// overshoot above. A 1.5 m radius is invisible in an elevation profile, in a
// length, and in a screenshot of a road nobody has driven into yet.
//
// Enforced, because centripetal fixed the SPLINE and NC 80 still had three
// waypoints at 7.7 m: those are its real switchbacks off Buck Creek Gap,
// digitised tight. A 7.7 m centreline inside a 9 m road puts the inner kerb at
// 3.2 m, which very nearly closes on itself. So the plan gets the same
// treatment the heights already get — measure, clamp the violation, then let
// the clamp settle — by nudging only the offending waypoints toward the
// midpoint of their neighbours, in small steps, until every corner clears.
// Nothing that already passes is touched, which is the point: this must open a
// hairpin the road cannot hold, not soften a road that climbs on hairpins.
{
  const t = tightestPlan(wp, RADIUS_FLOOR_M);
  console.log(`tightest corner ${t.minR.toFixed(1)} m at wp ${t.at}` +
              (t.under ? `  — ${t.under} waypoint(s) under the ${RADIUS_FLOOR_M} m floor` : ''));
  if (t.under) throw new Error(radiusFloorMessage(t, RADIUS_FLOOR_M));
}

// THE TWO LINES ARE MEASURED ON THE WAYPOINT LIST, WHICH IS THE ROAD THE GAME
// DRIVES. They used to be metres along the original OSM chain, which is a
// different curve: the spline through it is longer, the arc resample is a
// little shorter, and opening a hairpin shortens it again. NC 80 came out
// 11.97 km with its finish line written at 12.06 km — a race that cannot be
// completed, on a road that looked perfect in every other number the bake
// prints. Projecting the two ANCHORS onto the final list instead is immune to
// every one of those steps, because it asks where the places ARE rather than
// how far along something else they were.
const wpS = arcPositions(wp);
const startPt = proj.toXZ(CFG.start.lat, CFG.start.lon);
const endPt = pointAtS(chainXZ, chainS, endS);
const wpStart = projectOntoChain(wp, wpS, startPt).s + leadBorrow;
const wpFinish = projectOntoChain(wp, wpS, endPt).s - shutBorrow;
{
  // Two per cent, floored at 100 m. This is not a precision check — the route
  // and the chain are different curves and always will be — it is a check that
  // the route is the SAME ROAD. A wrong branch is kilometres out, not metres.
  const drift = (wpFinish - wpStart) - timedM;
  if (Math.abs(drift) > Math.max(100, timedM * 0.02))
    throw new Error(`timed section measures ${((wpFinish - wpStart) / 1000).toFixed(2)} km ` +
                    `on the route but ${(timedM / 1000).toFixed(2)} km on the chain — ` +
                    'that is too much to be resampling; the route took a wrong branch');
  if (wpFinish > wpS[wpS.length - 1] - 10)
    throw new Error(`the finish sits at ${wpFinish.toFixed(0)} m on a ` +
                    `${wpS[wpS.length - 1].toFixed(0)} m route — no shutdown left`);
  console.log(`start line ${wpStart.toFixed(0)} m, finish ${wpFinish.toFixed(0)} m ` +
              `of ${wpS[wpS.length - 1].toFixed(0)} m of route` +
              (Math.abs(drift) > 1 ? `  (${drift > 0 ? '+' : ''}${drift.toFixed(0)} m ` +
                                     'against the chain measurement)' : ''));
}

// Bridge flags carry from source vertices: a waypoint is on a bridge when
// its source cut segment is.
const wpBridge = waypointBridgeFlags(wp, cutBridge);

// Heights: DEM at stations, gaussian smooth, grade clamp (then the light
// re-smooth that rounds the clamp's kinks — see smoothHeights).
const rawH = wp.map(p => {
  const ll = proj.toLL(p.x, p.z);
  return elevAt(ll.lat, ll.lon);
});
const smoothH = smoothHeights(rawH, { spacing: SPACING, sigma: SMOOTH_SIGMA, maxGrade: MAX_GRADE });

let maxGrade = 0, minH = Infinity, maxH = -Infinity;
for (let i = 1; i < smoothH.length; i++) {
  maxGrade = Math.max(maxGrade, Math.abs(smoothH[i] - smoothH[i - 1]) / SPACING);
  minH = Math.min(minH, smoothH[i]); maxH = Math.max(maxH, smoothH[i]);
}
const baseM = Math.floor(minH - 40);
console.log(`route elevation ${minH.toFixed(0)}..${maxH.toFixed(0)} m ASL, ` +
  `max grade ${(maxGrade * 100).toFixed(1)}%, baseM ${baseM}`);

// Bridge spans in metres-along: runs, merge close, drop short.
const bridges = bridgeSpans(wpBridge, SPACING, { minM: MIN_BRIDGE_M, mergeM: BRIDGE_MERGE_M });
console.log(`bridge spans: ${bridges.map(s =>
  `${s[0].toFixed(0)}-${s[1].toFixed(0)} (${(s[1] - s[0]).toFixed(0)} m)`).join(', ') || 'none'}`);

// ------------------------------------------------------------- DEM grids
const artDir = join(projectRoot, 'Assets', 'PSXRacing', 'Art', CFG.artDir);
const grid = (name, cell, margin) =>
  bakeGrid({ wp, proj, elevAt, baseM, cell, margin, outDir: artDir, name });
const near = grid(CFG.prefix + '_dem_near', NEAR_CELL, NEAR_MARGIN);
const far = grid(CFG.prefix + '_dem_far', FAR_CELL, FAR_MARGIN);
writeDemMeta(artDir, CFG.prefix, baseM, near, far);

// ------------------------------------------------------------ stage json
writeStage(join(projectRoot, 'Assets', 'PSXRacing', 'Resources', CFG.resKey + '.json'), {
  // CFG.name, not a literal. This was left hardcoded when fetch_brp was
  // generalised, so every road baked itself a JSON claiming to be the Parkway.
  // Nothing reads it at runtime — TrackCatalog takes only attribution — which
  // is exactly why it went unnoticed and exactly why it had to be fixed: an
  // unread field that lies is a trap for whoever reads it next.
  name: CFG.name,
  attribution: 'Route data (c) OpenStreetMap contributors. Elevation: USGS/NASA SRTM.',
  proj, baseM, spacing: SPACING,
  // MEASURED, not assumed. Both used to be written from the constants the cut
  // was asked for; the cut is now clamped to the road that actually exists, so
  // a spur with no lead-in would have put its start line 50 m before its own
  // first metre and its finish line 120 m past the summit.
  startLineM: wpStart,
  finishM: wpFinish,
  bridges, wp, heights: smoothH,
});
console.log('OK');
