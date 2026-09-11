// Bake a MOUNTAIN LOOP into a playable stage: a closed ring of real road
// that uses a section of the Blue Ridge Parkway and comes back on the roads
// that meet it, with SRTM elevation under all of it.
//
//   node tools/roads/fetch_loop.mjs <blowingrock|switzerland> [--dry]
//
// This is the fifth road bake and, like the Charlotte one, it is not a copy:
// the projection, the centripetal spline, the SRTM sampler, the circular
// smoother and the grid baker are tools/roads/lib.mjs's, and the router is
// tools/roads/route.mjs's. What a loop through a Parkway interchange needs
// that neither the mountains nor the city did:
//
//   * ROUTING THROUGH JUNCTIONS. A mountain stage is one road; a loop turns
//     off it. Each anchor is snapped ONTO a node of the road it names and
//     the router is held to exactly that node, so the leg it exists to
//     force is forced — twelve preferred nodes within 400 m of a spot where
//     two roads run parallel includes the other road, and the router will
//     happily take it.
//   * JUNCTION FILLETS. OSM draws a T-junction turn as a spike: two long
//     legs meeting at a vertex. Through the spline that is a 3 m radius on
//     a corner a car takes at 14. Any vertex turning more than a set angle
//     is replaced by a circular arc tangent to both legs, at the radius a
//     car turns a junction at — which is what the paint on a real junction
//     is laid out for. It is NOT the smoothing pass the roads memory warns
//     against: a hairpin on the open road turns through its vertices a few
//     degrees at a time and never trips the threshold.
//   * A SELF-CROSSING. Both loops here pass UNDER their own Parkway bridge:
//     the Parkway crosses the closing road on an overpass, and a ring that
//     uses both roads crosses itself once, at different heights. SRTM
//     cannot see a 15 m cut, so both roads read the same height there; the
//     bake finds the crossing in plan, decides which road is the bridge
//     (OSM's bridge tag, else the Parkway), lifts that road's profile over
//     the other by a clearance on a long cosine ramp, forces a bridge span
//     around it, and writes the crossing into the JSON so the builder can
//     keep the lower road's corridor and hold the piers off it.
//   * A TUNNEL. The Little Switzerland loop runs through the Parkway's
//     tunnel. Inside a tunnel-tagged span the road's height is the line
//     between the two portals rather than the ridge SRTM reads over it, and
//     the span is written out so the builder can leave the mountain standing
//     over the road and put a tube around it.
//
// Every number the self-test asserts is printed here first: tightest plan
// corner (the 12 m floor), self-clearance with grade separations exempted,
// elevation range, peak grade, vertical radius, RaceMeters against the fuel
// ceiling. A refusal here costs a second; the same refusal from the
// self-test costs a forty-minute build.

import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  fetchOverpass, srtmSampler, makeProjection, dist2,
  arcPositions, pointAtS, projectOntoChain, splineResample,
  tightestPlan, radiusFloorMessage, planRadius, waypointBridgeFlags,
  smoothHeights, profileStats, bridgeSpans, bakeGrid, writeDemMeta, writeStage,
} from './lib.mjs';
import { routeByAnchors, CLASS_COST, matchesPref } from './route.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const cacheDir = join(here, 'cache');
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
/// The fuel ceiling: the thirstiest stage-4 car reaches the self-test's 85%
/// tank at this distance at race load.
const FUEL_CEILING_M = 14200;
/// Daylight the lifted road keeps over the road it crosses: a deck 1.3 m
/// thick and a car under it with room to spare, which is also what the
/// terrain audit demands under a full-blend span (3 m) with margin.
const CROSSING_CLEAR_M = 6.5;
/// Gentlest ramp the lift is spread over, per metre of lift. 1/0.055 m per
/// metre keeps the hump under 5.5% on its own, on top of whatever grade the
/// road already has there.
const LIFT_RAMP_PER_M = 1 / 0.055, LIFT_RAMP_MIN_M = 90;
/// Half the bridge span forced around a crossing.
const CROSSING_SPAN_HALF_M = 46;
/// Junction fillet: turns sharper than this get an arc of this radius.
const FILLET_MIN_DEG = 22, FILLET_R_M = 14, FILLET_R_MIN_M = 9;
/// Two waypoints closer than this along the route are the same piece of
/// road for the self-clearance test (the self-test's own 22 stations).
const SELF_SEP = 22;
/// Height difference at which two parts of the route close in plan are a
/// grade separation rather than a collision.
const GRADE_SEP_M = 4.5;
const USER_AGENT = 'psx-racing-loop-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)';

// A closed Parkway section is tagged highway=construction in OSM while it is
// being rebuilt (Helene); service roads are allowed at a price so the Cone
// Manor access and the Parkway's own ramps are routable.
const LOOP_COST = { ...CLASS_COST, construction: 1.15, tertiary_link: 1.5, service: 3.5, living_street: 4.0 };

// ---------------------------------------------------------------- loops
// Anchors in ROUTE order; the first is waypoint 0 and the start line. `on`
// names the road the anchor snaps to.
const LOOPS = {
  // BLOWING ROCK. The Parkway west from the US 321 interchange to the Moses
  // Cone access (MP 291.9 -> 294.6), Cone Road down to US 221, Yonahlossee
  // Road and Main Street through the village, Valley Boulevard and the
  // Holshouser Highway back to the interchange, the ramp up, and under the
  // Parkway's own bridge over US 321 to close.
  blowingrock: {
    name: 'Blowing Rock — Moses Cone Loop',
    probe: 'blowingrock', artDir: 'BlowingRock', prefix: 'brock', resKey: 'brock_stage',
    bbox: { s: 36.09, w: -81.76, n: 36.19, e: -81.60 },
    anchors: [
      { lat: 36.14856, lon: -81.66380, wayId: 344342930 },           // the Flat Top junction, west of the US 321 bridge
      { lat: 36.15216, lon: -81.68753, wayId: 712276765 },           // mid-Parkway, Moses Cone estate
      { lat: 36.14727, lon: -81.69956, wayId: 453068871 },           // where the Cone access leaves the Parkway
      { lat: 36.14600, lon: -81.70014, on: 'US 221' },               // US 221 at the foot of Cone Road
      { lat: 36.13632, lon: -81.68337, on: 'yonahlossee' },          // Yonahlossee Road
      { lat: 36.13548, lon: -81.67238, on: 'main street' },          // Main Street, Blowing Rock
      { lat: 36.14494, lon: -81.66504, on: 'holshouser' },           // Holshouser Hwy northbound
    ],
    prefer: { names: ['blue ridge parkway', 'cone road', 'main street', 'yonahlossee', 'valley boulevard', 'holshouser'],
              refs: ['US 221', 'US 321 Business'] },
    roadWidthM: 10, lanes: 2, speedLimitMph: 35, laps: 1,
    smoothSigma: 60, maxGrade: 0.10,
    surveyed: { start: 1060, end: 1060 },   // Blowing Rock village sits at 3,500 ft; the Parkway at the interchange a little under
  },
  // LITTLE SWITZERLAND. The Parkway west from Gillespie Gap through the
  // Little Switzerland Tunnel to the village access (MP 330.9 -> 334), the
  // village links onto NC 226A, NC 226A back east along the ridge to NC 226,
  // NC 226 under the Parkway's bridge, and the ramp up to close.
  switzerland: {
    name: 'Little Switzerland — Gillespie Gap Loop',
    probe: 'switzerland', artDir: 'Switzerland', prefix: 'swiss', resKey: 'swiss_stage',
    bbox: { s: 35.79, w: -82.17, n: 35.93, e: -81.98 },
    anchors: [
      { lat: 35.85263, lon: -82.05035, wayId: 1370351211 },          // Gillespie Gap, just west of the ramp merge
      { lat: 35.85004, lon: -82.07038, on: 'blue ridge parkway' },   // mid-Parkway
      { lat: 35.85008, lon: -82.09170, wayId: 124554599 },          // the exit link at Little Switzerland
      { lat: 35.84937, lon: -82.06949, on: 'NC 226A' },              // NC 226A along the ridge
    ],
    prefer: { names: ['blue ridge parkway', 'little switzerland tunnel', 'high ridge', 'chestnut grove church'],
              refs: ['NC 226A', 'NC 226'] },
    roadWidthM: 9.5, lanes: 2, speedLimitMph: 35, laps: 1,
    smoothSigma: 60, maxGrade: 0.10,
    surveyed: { start: 859, end: 859 },
  },
};

const KEY = process.argv[2];
const DRY = process.argv.includes('--dry');
if (!KEY || !LOOPS[KEY]) {
  console.error('usage: node fetch_loop.mjs <' + Object.keys(LOOPS).join('|') + '> [--dry]');
  process.exit(2);
}
const CFG = LOOPS[KEY];
const BBOX = CFG.bbox;
console.log('=== ' + CFG.name + ' (' + KEY + ', loop) ===');

// ---------------------------------------------------------------- tags
const bridgeOf = w => !!(w && w.tags && (w.tags.bridge === 'yes' || w.tags.bridge === 'viaduct'));
const tunnelOf = w => !!(w && w.tags && w.tags.tunnel === 'yes');
const nameOf = w => (w && w.tags && (w.tags.name || w.tags.ref)) || '?';

/// The nearest node on a way whose name or ref contains `a.on`, held exactly.
function snapTo(res, a) {
  // By way id: the node of THAT way nearest the coordinate (an unnamed
  // ramp has nothing else to be named by).
  if (a.wayId) {
    const w = res.elements.find(e => e.type === 'way' && e.id === a.wayId);
    if (!w) throw new Error('way ' + a.wayId + ' is not in the fetched area');
    let best = null, bestD = Infinity;
    for (const p of w.geometry) {
      const d = Math.hypot((p.lat - a.lat) * 111132, (p.lon - a.lon) * 111320 * Math.cos(a.lat * Math.PI / 180));
      if (d < bestD) { bestD = d; best = p; }
    }
    return { lat: best.lat, lon: best.lon, reachM: 3 };
  }
  const want = a.on.toLowerCase().replace(/\s+/g, '');
  let best = null, bestD = Infinity;
  for (const w of res.elements) {
    if (w.type !== 'way' || !w.geometry || !w.tags) continue;
    const tag = ((w.tags.name || '') + ' ' + (w.tags.ref || '')).toLowerCase().replace(/\s+/g, '');
    if (!tag.includes(want)) continue;
    for (const p of w.geometry) {
      const d = Math.hypot((p.lat - a.lat) * 111132, (p.lon - a.lon) * 111320 * Math.cos(a.lat * Math.PI / 180));
      if (d < bestD) { bestD = d; best = p; }
    }
  }
  if (!best) throw new Error('no way matches anchor.on = ' + a.on);
  if (bestD > 60) console.log(`  WARNING: anchor on ${a.on} snapped ${bestD.toFixed(0)} m — check the coordinate`);
  return { lat: best.lat, lon: best.lon, reachM: 3 };
}

// -------------------------------------------------------- junction fillets
/// Replace every vertex turning more than FILLET_MIN_DEG with a circular arc
/// tangent to both legs, at FILLET_R_M. The tangent points are found by
/// WALKING THE POLYLINE either side of the corner: OSM draws a ramp
/// junction as a spike followed by several short, slightly turning
/// vertices, and a fillet that could only use the one segment touching the
/// corner is a fillet of 6 m. The vertices the arc swallows are dropped;
/// where the outgoing side bends before the tangent point, the arc's end is
/// joined to the first surviving vertex by a straight and the spline rounds
/// the join. The radius shrinks to fit a leg too short to hold the tangent
/// length, down to FILLET_R_MIN_M; below that the corner is left for the
/// floor check to report.
function filletCorners(cut, flags, ways) {
  const n = cut.length;
  const at = k => cut[((k % n) + n) % n];
  const seg = (a, b) => Math.sqrt(dist2(at(a), at(b)));
  const turnAt = i => {
    const a = at(i - 1), b = at(i), c = at(i + 1);
    const u = { x: b.x - a.x, z: b.z - a.z }, v = { x: c.x - b.x, z: c.z - b.z };
    const lu = Math.hypot(u.x, u.z), lv = Math.hypot(v.x, v.z);
    if (lu < 1e-6 || lv < 1e-6) return 0;
    const cosA = Math.max(-1, Math.min(1, (u.x * v.x + u.z * v.z) / (lu * lv)));
    return Math.acos(cosA) * 180 / Math.PI;
  };
  const defl = new Array(n).fill(0).map((_, i) => turnAt(i));
  // Room along the polyline either side, stopping at the next real corner.
  const RUN_MAX = 80, STRAIGHT_DEG = 12;
  const run = (i, dir) => {
    let acc = 0, k = i;
    for (let step = 0; step < 40; step++) {
      const j = k + dir;
      acc += seg(k, j);
      if (acc >= RUN_MAX) return RUN_MAX;
      if (defl[((j % n) + n) % n] >= STRAIGHT_DEG) return acc;
      k = j;
    }
    return acc;
  };
  // A point `t` metres from vertex i along direction dir, and the FAR
  // vertex of the segment it landed on — the first vertex the arc does
  // not swallow on that side.
  const walk = (i, dir, t) => {
    let rem = t, k = i;
    for (let step = 0; step < 60; step++) {
      const j = k + dir;
      const l = seg(k, j);
      if (rem <= l || l < 1e-6) {
        const f = l > 1e-6 ? rem / l : 0;
        return { pt: { x: at(k).x + (at(j).x - at(k).x) * f, z: at(k).z + (at(j).z - at(k).z) * f }, far: j };
      }
      rem -= l; k = j;
    }
    return { pt: at(k), far: k + dir };
  };
  const consumed = new Uint8Array(n);
  const arcAt = new Map();   // vertex index -> arc points that replace it (and its consumed run)
  let filleted = 0;
  for (let i = 0; i < n; i++) {
    if (defl[i] < FILLET_MIN_DEG || defl[i] > 172 || consumed[i]) continue;
    const alpha = defl[i] * Math.PI / 180;
    const tan = Math.tan(alpha / 2);
    let r = FILLET_R_M, t = r * tan;
    const room = Math.min(run(i, -1), run(i, 1)) - 0.5;
    if (t > room) { t = room; r = t / tan; }
    if (r < FILLET_R_MIN_M) {
      console.log(`  NO fillet at ${nameOf(ways[i])} (${defl[i].toFixed(0)} deg): only ${room.toFixed(1)} m of room`);
      continue;
    }
    const back = walk(i, -1, t), fwd = walk(i, 1, t);
    const p0 = back.pt, p1 = fwd.pt;
    const u = norm({ x: at(i).x - p0.x, z: at(i).z - p0.z });
    const v = norm({ x: p1.x - at(i).x, z: p1.z - at(i).z });
    const cross = u.x * v.z - u.z * v.x;
    const side = cross > 0 ? 1 : -1;
    const nIn = { x: -u.z * side, z: u.x * side };
    const centre = { x: p0.x + nIn.x * r, z: p0.z + nIn.z * r };
    const a0 = Math.atan2(p0.z - centre.z, p0.x - centre.x);
    const a1 = Math.atan2(p1.z - centre.z, p1.x - centre.x);
    let sweep = a1 - a0;
    while (sweep > Math.PI) sweep -= 2 * Math.PI;
    while (sweep < -Math.PI) sweep += 2 * Math.PI;
    const steps = Math.max(3, Math.ceil(Math.abs(sweep) * r / 2));
    const pts = [];
    for (let k = 0; k <= steps; k++) {
      const ang = a0 + sweep * (k / steps);
      pts.push({ x: centre.x + Math.cos(ang) * r, z: centre.z + Math.sin(ang) * r });
    }
    // Everything strictly between the two tangent points' far vertices —
    // the corner and whatever the walk passed — is the arc's now.
    for (let k = back.far + 1; k < fwd.far; k++) consumed[((k % n) + n) % n] = 1;
    arcAt.set(((back.far + 1) % n + n) % n, pts);
    filleted++;
    console.log(`  fillet ${defl[i].toFixed(0)} deg at ${nameOf(ways[i])}: R ${r.toFixed(1)} m, t ${t.toFixed(1)} m, ` +
                `${fwd.far - back.far - 1} vertex(es) swallowed`);
  }
  const out = [], outF = [], outW = [];
  for (let k = 0; k < n; k++) {
    if (arcAt.has(k)) { for (const p of arcAt.get(k)) { out.push(p); outF.push(flags[k]); outW.push(ways[k]); } continue; }
    if (consumed[k]) continue;
    out.push(cut[k]); outF.push(flags[k]); outW.push(ways[k]);
  }
  console.log(`  ${filleted} junction corner(s) filleted`);
  return { cut: out, flags: outF, ways: outW };
}
const norm = v => { const l = Math.hypot(v.x, v.z) || 1; return { x: v.x / l, z: v.z / l }; };

// --------------------------------------------------------------- spikes
/// A REVERSAL SPIKE: a vertex where the route turns back on itself by more
/// than SPIKE_DEG. OSM draws a ramp merge as a way that overshoots the ramp
/// it joins by a few metres, and a Y-junction as two roads meeting at their
/// shared end — the router walks to the shared node and back out the other
/// road, which is a hairpin no car makes. The spike is cut off: the vertices
/// within SPIKE_CUT_M either side along the polyline are replaced by the two
/// points that far out, joined by a straight, and the corner that leaves is
/// an ordinary junction turn for the fillet pass to round.
const SPIKE_DEG = 150, SPIKE_CUT_M = 18;
function removeSpikes(cut, flags, ways) {
  let round = 0;
  for (;;) {
    const n = cut.length;
    const at = k => cut[((k % n) + n) % n];
    const seg = (a, b) => Math.sqrt(dist2(at(a), at(b)));
    let spike = -1, worst = SPIKE_DEG;
    for (let i = 0; i < n; i++) {
      const a = at(i - 1), b = at(i), c = at(i + 1);
      const u = { x: b.x - a.x, z: b.z - a.z }, v = { x: c.x - b.x, z: c.z - b.z };
      const lu = Math.hypot(u.x, u.z), lv = Math.hypot(v.x, v.z);
      if (lu < 1e-6 || lv < 1e-6) continue;
      const d = Math.acos(Math.max(-1, Math.min(1, (u.x * v.x + u.z * v.z) / (lu * lv)))) * 180 / Math.PI;
      if (d > worst) { worst = d; spike = i; }
    }
    if (spike < 0 || ++round > 20) return { cut, flags, ways };
    const walk = (i, dir) => {
      let rem = SPIKE_CUT_M, k = i;
      for (let step = 0; step < 40; step++) {
        const j = k + dir, l = seg(k, j);
        if (rem <= l || l < 1e-6) {
          const f = l > 1e-6 ? rem / l : 0;
          return { pt: { x: at(k).x + (at(j).x - at(k).x) * f, z: at(k).z + (at(j).z - at(k).z) * f }, far: j };
        }
        rem -= l; k = j;
      }
      return { pt: at(k), far: k + dir };
    };
    const back = walk(spike, -1), fwd = walk(spike, 1);
    const drop = new Uint8Array(n);
    for (let k = back.far + 1; k < fwd.far; k++) drop[((k % n) + n) % n] = 1;
    const out = [], outF = [], outW = [];
    const insertAt = ((back.far + 1) % n + n) % n;
    for (let k = 0; k < n; k++) {
      if (k === insertAt) { out.push(back.pt, fwd.pt); outF.push(flags[spike], flags[spike]); outW.push(ways[spike], ways[spike]); }
      if (drop[k]) continue;
      out.push(cut[k]); outF.push(flags[k]); outW.push(ways[k]);
    }
    console.log(`  spike ${worst.toFixed(0)} deg at ${nameOf(ways[spike])} cut (${fwd.far - back.far - 1} vertex(es))`);
    cut = out; flags = outF; ways = outW;
  }
}

// ------------------------------------------------------------ crossings
/// Plan intersections between waypoint segments that are not neighbours
/// along the ring: [{i, j, x, z}] with i the earlier station.
function findCrossings(wp, minSep) {
  const n = wp.length;
  const out = [];
  for (let i = 0; i < n; i++) {
    const a = wp[i], b = wp[(i + 1) % n];
    for (let j = i + minSep; j < n; j++) {
      const sep = Math.min(j - i, n - (j - i));
      if (sep < minSep) continue;
      const c = wp[j], d = wp[(j + 1) % n];
      const p = segIntersect(a, b, c, d);
      if (p) out.push({ i, j, x: p.x, z: p.z });
    }
  }
  return out;
}
function segIntersect(a, b, c, d) {
  const r = { x: b.x - a.x, z: b.z - a.z }, s = { x: d.x - c.x, z: d.z - c.z };
  const den = r.x * s.z - r.z * s.x;
  if (Math.abs(den) < 1e-9) return null;
  const qp = { x: c.x - a.x, z: c.z - a.z };
  const t = (qp.x * s.z - qp.z * s.x) / den, u = (qp.x * r.z - qp.z * r.x) / den;
  if (t < 0 || t > 1 || u < 0 || u > 1) return null;
  return { x: a.x + r.x * t, z: a.z + r.z * t };
}

/// Self-clearance with grade separations exempted: the nearest approach in
/// plan between two stretches of the ring that are NOT at least GRADE_SEP_M
/// apart in height.
function selfClearanceGraded(wp, h, minSep) {
  const n = wp.length;
  let min = Infinity, at = [0, 0];
  for (let i = 0; i < n; i++)
    for (let j = i + minSep; j < n; j++) {
      const sep = Math.min(j - i, n - (j - i));
      if (sep < minSep) continue;
      const d = dist2(wp[i], wp[j]);
      if (d < min * min && Math.abs(h[i] - h[j]) < GRADE_SEP_M) { min = Math.sqrt(d); at = [i, j]; }
    }
  return { minM: min, at };
}

// ------------------------------------------------------------------ main
const overpass = await fetchOverpass({
  cacheDir, name: 'loopprobe_' + CFG.probe + '.json', userAgent: USER_AGENT,
  query: `[out:json][timeout:180];\nway["highway"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});\nout tags geom;`,
});
const elevAt = await srtmSampler(BBOX, cacheDir);

const proj0 = makeProjection((BBOX.s + BBOX.n) / 2, (BBOX.w + BBOX.e) / 2);
const anchors = CFG.anchors.map(a => snapTo(overpass, a));
const { chain, wayOf, cost } = routeByAnchors(overpass, proj0, {
  anchors, loop: true, classes: null, prefer: CFG.prefer, classCost: LOOP_COST, offPreference: 8,
  stitches: CFG.stitches || [],
});

// Project about the route centroid so numbers stay small.
const cLat = chain.reduce((a, p) => a + p.lat, 0) / chain.length;
const cLon = chain.reduce((a, p) => a + p.lon, 0) / chain.length;
const proj = makeProjection(cLat, cLon);
let cut = chain.map(p => proj.toXZ(p.lat, p.lon));
let cutFlag = wayOf.map(w => (bridgeOf(w) ? 1 : 0) | (tunnelOf(w) ? 2 : 0));
let cutWay = wayOf.slice();

// The legs, for the record.
{
  const legs = [];
  for (let i = 1; i < chain.length; i++) {
    const l = nameOf(wayOf[i]);
    const m = Math.sqrt(dist2(cut[i - 1], cut[i]));
    if (legs.length && legs[legs.length - 1].l === l) legs[legs.length - 1].m += m;
    else legs.push({ l, m });
  }
  console.log('route:');
  for (const l of legs) console.log(`  ${(l.m / 1000).toFixed(2).padStart(6)} km  ${l.l}`);
  const used = [...new Set(wayOf.filter(Boolean).map(w => w.id))];
  console.log(`  way ids (${used.length}): ${used.join(',')}`);
}

// A RING: close it, and rotate so the first anchor is waypoint 0.
{
  const closeGap = Math.sqrt(dist2(cut[cut.length - 1], cut[0]));
  console.log(`way chain closes with a gap of ${closeGap.toFixed(2)} m`);
  if (closeGap > 30) throw new Error('the route does not close into a loop');
  if (closeGap < 2) { cut.pop(); cutFlag.pop(); cutWay.pop(); }
  const ring = cut.concat([cut[0]]);
  const ringS = arcPositions(ring);
  const a = projectOntoChain(ring, ringS, proj.toXZ(anchors[0].lat, anchors[0].lon));
  if (Math.sqrt(a.d2) > 100) throw new Error('the start anchor is not on this ring');
  let seg = 0;
  while (seg + 1 < ringS.length && ringS[seg + 1] < a.s) seg++;
  const at = pointAtS(ring, ringS, a.s);
  const n = cut.length;
  const rot = [], rotF = [], rotW = [];
  const startsOnVertex = Math.sqrt(dist2(at, cut[seg % n])) < 0.5;
  if (!startsOnVertex) { rot.push(at); rotF.push(cutFlag[seg % n]); rotW.push(cutWay[seg % n]); }
  for (let k = 1; k <= n; k++) {
    const i = (seg + k) % n;
    if (startsOnVertex && k === n) { rot.unshift(cut[seg % n]); rotF.unshift(cutFlag[seg % n]); rotW.unshift(cutWay[seg % n]); break; }
    rot.push(cut[i]); rotF.push(cutFlag[i]); rotW.push(cutWay[i]);
  }
  cut = rot; cutFlag = rotF; cutWay = rotW;
}
console.log(`cut vertices: ${cut.length}`);

// Spikes go, then junction corners become arcs.
{
  const sp = removeSpikes(cut, cutFlag, cutWay);
  cut = sp.cut; cutFlag = sp.flags; cutWay = sp.ways;
  const f = filletCorners(cut, cutFlag, cutWay);
  cut = f.cut; cutFlag = f.flags; cutWay = f.ways;
}

// Spline-resample to 4 m waypoints, periodic on the ring.
let wp = splineResample(cut, SPACING, { loop: true });
while (wp.length > 2 && Math.sqrt(dist2(wp[wp.length - 1], wp[0])) < SPACING * 0.5) wp.pop();
const closureGap = Math.sqrt(dist2(wp[wp.length - 1], wp[0]));
const lengthM = wp.length * SPACING;
console.log(`loop closes: ${wp.length} waypoints, closing segment ${closureGap.toFixed(2)} m`);
console.log(`waypoints: ${wp.length} (${(lengthM / 1000).toFixed(2)} km)`);

// Per-waypoint bridge and tunnel flags, from the source vertices.
// The router tags each vertex with the way that ARRIVED at it, so the
// segment from vertex i to i+1 belongs to vertex i+1's way — and a
// waypoint on that segment takes that way's flags. Bridges are generous
// by one segment either side (a span never loses its last station to
// rounding); a tunnel's portals are where the way says they are.
const srcFlagAt = i => cutFlag[((i % cutFlag.length) + cutFlag.length) % cutFlag.length];
const wpBridge = wp.map(p => !!((srcFlagAt(p.srcI) & 1) || (srcFlagAt(p.srcI + 1) & 1)));
const wpTunnel = wp.map(p => !!(srcFlagAt(p.srcI + 1) & 2));
const wpWay = wp.map(p => cutWay[((p.srcI + 1) % cutWay.length + cutWay.length) % cutWay.length]);

// The tightest corner: measured, then refused under the floor.
{
  const t = tightestPlan(wp, RADIUS_FLOOR_M, { loop: true });
  console.log(`tightest corner ${t.minR.toFixed(1)} m at wp ${t.at} (${(t.at * SPACING / 1000).toFixed(2)} km, ${nameOf(wpWay[t.at])})` +
              (t.under ? `  — ${t.under} waypoint(s) under the ${RADIUS_FLOOR_M} m floor` : ''));
  if (t.under) {
    const worst = [];
    for (let i = 0; i < wp.length; i++) { const R = planRadius(wp, i, true); if (R < RADIUS_FLOOR_M) worst.push([R, i]); }
    worst.sort((a, b) => a[0] - b[0]);
    for (const [R, i] of worst.slice(0, 8)) {
      const ll = proj.toLL(wp[i].x, wp[i].z);
      console.log(`    ${R.toFixed(1)} m at wp ${i} ${ll.lat.toFixed(5)},${ll.lon.toFixed(5)} on ${nameOf(wpWay[i])}`);
    }
    // The cut vertices around the three worst: what the spline was given.
    const shown = new Set();
    for (const [, i] of worst.slice(0, 3)) {
      const si = wp[i].srcI;
      if (shown.has(si)) continue; shown.add(si);
      console.log(`    -- cut vertices around src ${si}:`);
      const m = cut.length;
      for (let k = si - 4; k <= si + 4; k++) {
        const a = cut[((k - 1) % m + m) % m], b = cut[((k % m) + m) % m], c = cut[((k + 1) % m + m) % m];
        const u = { x: b.x - a.x, z: b.z - a.z }, v = { x: c.x - b.x, z: c.z - b.z };
        const lu = Math.hypot(u.x, u.z), lv = Math.hypot(v.x, v.z);
        const d = Math.acos(Math.max(-1, Math.min(1, (u.x * v.x + u.z * v.z) / (lu * lv || 1)))) * 180 / Math.PI;
        const ll = proj.toLL(b.x, b.z);
        console.log(`       v${((k % m) + m) % m}: leg ${lu.toFixed(1)} m, turn ${d.toFixed(0)} deg, ${ll.lat.toFixed(5)},${ll.lon.toFixed(5)} ${nameOf(cutWay[((k % m) + m) % m])}`);
      }
    }
    throw new Error(radiusFloorMessage(t, RADIUS_FLOOR_M));
  }
}

// Tunnels: spans of consecutive tunnel waypoints, in metres.
const tunnels = [];
{
  let open = -1;
  for (let i = 0; i <= wp.length; i++) {
    const inT = i < wp.length && wpTunnel[i];
    if (inT && open < 0) open = i;
    if (!inT && open >= 0) { tunnels.push([open * SPACING, i * SPACING]); open = -1; }
  }
  console.log(`tunnel spans: ${tunnels.map(s => `${s[0]}-${s[1]} (${s[1] - s[0]} m)`).join(', ') || 'none'}`);
}

// Heights: SRTM at every waypoint. Inside a tunnel the road is the LINE
// between its portals, not the ridge over it — written in before the
// smoother sees it, so the approaches blend into the line.
const rawH = wp.map(p => { const ll = proj.toLL(p.x, p.z); return elevAt(ll.lat, ll.lon); });
for (const [fromM, toM] of tunnels) {
  const i0 = Math.round(fromM / SPACING), i1 = Math.round(toM / SPACING);
  const h0 = rawH[Math.max(0, i0 - 2)], h1 = rawH[Math.min(wp.length - 1, i1 + 1)];
  for (let i = i0; i < i1; i++) rawH[i] = h0 + (h1 - h0) * ((i - i0) / Math.max(1, i1 - i0));
}
const smoothH = smoothHeights(rawH, { spacing: SPACING, sigma: CFG.smoothSigma, maxGrade: CFG.maxGrade, circular: true });

// THE CROSSING. Find where the ring crosses itself in plan, decide which is
// the bridge, and lift it clear of the other.
const crossings = findCrossings(wp, 40);
const crossingsOut = [];
for (const c of crossings) {
  const bi = wpBridge[c.i] || wpBridge[(c.i + 1) % wp.length];
  const bj = wpBridge[c.j] || wpBridge[(c.j + 1) % wp.length];
  let upper, lower;
  if (bi && !bj) { upper = c.i; lower = c.j; }
  else if (bj && !bi) { upper = c.j; lower = c.i; }
  else {
    // Neither (or both) tagged: the Parkway is the bridge in both loops here.
    const pi = /parkway/i.test(nameOf(wpWay[c.i])), pj = /parkway/i.test(nameOf(wpWay[c.j]));
    if (pi && !pj) { upper = c.i; lower = c.j; } else if (pj && !pi) { upper = c.j; lower = c.i; } else { upper = c.i; lower = c.j; }
    console.log(`  crossing at wp ${c.i}/${c.j}: no single bridge tag (${bi}/${bj}) — taking ${nameOf(wpWay[upper])} as the bridge`);
  }
  const need = smoothH[lower] + CROSSING_CLEAR_M - smoothH[upper];
  const ll = proj.toLL(c.x, c.z);
  console.log(`crossing: ${nameOf(wpWay[upper])} (wp ${upper}, ${(upper * SPACING / 1000).toFixed(2)} km) over ` +
              `${nameOf(wpWay[lower])} (wp ${lower}, ${(lower * SPACING / 1000).toFixed(2)} km) at ${ll.lat.toFixed(5)},${ll.lon.toFixed(5)}: ` +
              `SRTM gap ${(smoothH[upper] - smoothH[lower]).toFixed(1)} m, lift ${Math.max(0, need).toFixed(1)} m`);
  if (need > 0) {
    const W = Math.max(LIFT_RAMP_MIN_M, need * LIFT_RAMP_PER_M);
    const n = wp.length;
    for (let k = 0; k < n; k++) {
      let d = Math.abs(k - upper); d = Math.min(d, n - d) * SPACING;
      if (d > W) continue;
      smoothH[k] += need * (0.5 + 0.5 * Math.cos(Math.PI * d / W));
    }
    console.log(`  lifted over ${(2 * W).toFixed(0)} m`);
  }
  // A span around it on the upper road, whatever OSM says.
  const half = Math.round(CROSSING_SPAN_HALF_M / SPACING);
  for (let k = upper - half; k <= upper + half; k++) wpBridge[((k % wp.length) + wp.length) % wp.length] = true;
  crossingsOut.push(upper * SPACING, lower * SPACING);
}
if (!crossings.length) console.log('crossings: none');

// TWO ROADS SIDE BY SIDE. Where the Parkway and the road that closes the
// loop run parallel (NC 226A rides the same ridge as the Parkway for a
// kilometre), their centrelines can come closer than two barrier lines
// need. The LATER road is nudged away from the earlier one, perpendicular
// to it, by however much the clearance is short — a metre or two, on a
// nine-metre road, tapered in and out over a hundred metres. Nothing about
// the road's shape changes; it moves sideways by less than a lane.
{
  const need = CFG.roadWidthM + 2 * (CFG.roadWidthM * 0.5 + 1.15) + 0.4;
  const n = wp.length;
  const push = new Array(n).fill(0), pushDir = new Array(n).fill(null);
  for (let j = 0; j < n; j++)
    for (let i = 0; i < j - SELF_SEP; i++) {
      if (n - (j - i) < SELF_SEP) continue;
      const d = Math.sqrt(dist2(wp[i], wp[j]));
      if (d >= need) continue;
      // A grade separation is not a near miss: the lifted road passes over.
      if (Math.abs(smoothH[i] - smoothH[j]) >= GRADE_SEP_M) continue;
      const dx = wp[j].x - wp[i].x, dz = wp[j].z - wp[i].z;
      const l = Math.hypot(dx, dz) || 1;
      if (need - d > push[j]) { push[j] = need - d; pushDir[j] = { x: dx / l, z: dz / l }; }
    }
  // Taper: every station within TAPER stations of a pushed one takes a
  // smoothed share, so the sidestep eases in rather than kinks.
  const TAPER = 25;
  const smooth = new Array(n).fill(0), dir = new Array(n).fill(null);
  let pushed = 0;
  for (let j = 0; j < n; j++) {
    let best = 0, bd = null;
    for (let o = -TAPER; o <= TAPER; o++) {
      const k = ((j + o) % n + n) % n;
      if (!push[k]) continue;
      const w = push[k] * (0.5 + 0.5 * Math.cos(Math.PI * o / TAPER));
      if (w > best) { best = w; bd = pushDir[k]; }
    }
    smooth[j] = best; dir[j] = bd;
    if (push[j]) pushed++;
  }
  if (pushed) {
    let maxPush = 0;
    for (let j = 0; j < n; j++) if (smooth[j] && dir[j]) {
      wp[j] = { ...wp[j], x: wp[j].x + dir[j].x * smooth[j], z: wp[j].z + dir[j].z * smooth[j] };
      maxPush = Math.max(maxPush, smooth[j]);
    }
    console.log(`  ${pushed} station(s) ran within ${need.toFixed(1)} m of the other road and were nudged ` +
                `sideways (up to ${maxPush.toFixed(2)} m)`);
    const t = tightestPlan(wp, RADIUS_FLOOR_M, { loop: true });
    console.log(`  tightest corner after the nudge ${t.minR.toFixed(1)} m`);
    if (t.under) throw new Error(radiusFloorMessage(t, RADIUS_FLOOR_M));
  }
}


const stats = profileStats(smoothH, SPACING, { circular: true });
const baseM = Math.floor(stats.minH - 40);
console.log(`route elevation ${stats.minH.toFixed(0)}..${stats.maxH.toFixed(0)} m ASL, ` +
  `max grade ${(stats.maxGrade * 100).toFixed(1)}%, min vertical radius ${stats.minVertR.toFixed(0)} m, baseM ${baseM}`);
if (CFG.surveyed) console.log(`  surveyed start ${CFG.surveyed.start} m, SRTM says ${smoothH[0].toFixed(0)}`);

// Self-clearance, grade separations exempted — what the self-test asserts.
{
  const need = CFG.roadWidthM + 2 * (CFG.roadWidthM * 0.5 + 1.15);
  const c = selfClearanceGraded(wp, smoothH, SELF_SEP);
  console.log(`min self-clearance ${c.minM.toFixed(1)} m between wp ${c.at[0]} and ${c.at[1]} (need ${need.toFixed(1)} m; grade separations exempt)`);
  if (c.minM < need) {
    const a = proj.toLL(wp[c.at[0]].x, wp[c.at[0]].z);
    throw new Error(`the route runs into its own barriers at ${a.lat.toFixed(5)},${a.lon.toFixed(5)} ` +
                    `(${nameOf(wpWay[c.at[0]])} vs ${nameOf(wpWay[c.at[1]])})`);
  }
}

// Bridge spans in metres-along (seam-merged on the ring).
const bridges = bridgeSpans(wpBridge, SPACING, { minM: MIN_BRIDGE_M, mergeM: BRIDGE_MERGE_M, loop: true });
const bridgeM = bridges.reduce((a, s) => a + (s[1] - s[0]), 0);
console.log(`bridge spans: ${bridges.map(s =>
  `${s[0].toFixed(0)}-${s[1].toFixed(0)} (${(s[1] - s[0]).toFixed(0)} m)`).join(', ') || 'none'} — ${bridgeM.toFixed(0)} m on structure`);

const raceM = lengthM * CFG.laps;
console.log(`RaceMeters ${(raceM / 1000).toFixed(2)} km (${CFG.laps} lap) = ${(raceM / FUEL_CEILING_M * 100).toFixed(0)}% of the fuel ceiling` +
  (raceM > FUEL_CEILING_M ? '  — OVER: the self-test will fail this venue' : ''));

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
  startLineM: 0, finishM: 0,
  bridges, wp, heights: smoothH,
  extra: {
    loop: true,
    oneWay: false,
    speedLimitMph: CFG.speedLimitMph,
    lanes: CFG.lanes,
    roadWidthM: CFG.roadWidthM,
    tunnels: tunnels.flat(),
    crossings: crossingsOut,
  },
});
console.log('OK');
