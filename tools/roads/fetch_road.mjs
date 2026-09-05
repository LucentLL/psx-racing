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

import { createRequire } from 'node:module';
import { gunzipSync } from 'node:zlib';
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

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

async function fetchCached(name, url, opts) {
  const path = join(cacheDir, name);
  if (existsSync(path)) return readFileSync(path);
  console.log('fetching ' + url);
  const res = await fetch(url, opts);
  if (!res.ok) throw new Error(url + ' -> HTTP ' + res.status);
  const buf = Buffer.from(await res.arrayBuffer());
  writeFileSync(path, buf);
  return buf;
}

async function fetchOverpass() {
  const q = `[out:json][timeout:120];
way["highway"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
out tags geom;`;
  // Global-coverage mirrors only — overpass.osm.ch is Switzerland-only and
  // happily returns an empty result for a North Carolina bbox.
  const mirrors = [
    'https://overpass-api.de/api/interpreter',
    'https://overpass.kumi.systems/api/interpreter',
    'https://overpass.private.coffee/api/interpreter',
    'https://maps.mail.ru/osm/tools/overpass/api/interpreter',
  ];
  let lastErr = null;
  for (let round = 0; round < 3; round++) {
    for (const url of mirrors) {
      try {
        const buf = await fetchCached('overpass_' + KEY + '.json', url,
          { method: 'POST', body: 'data=' + encodeURIComponent(q),
            headers: {
              'Content-Type': 'application/x-www-form-urlencoded',
              'User-Agent': 'psx-racing-brp-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)',
              'Accept': 'application/json',
            } });
        const parsed = JSON.parse(buf.toString('utf8'));
        if (!parsed.elements || !parsed.elements.length)
          throw new Error('empty result (regional mirror or bad query)');
        return parsed;
      } catch (e) {
        lastErr = e; console.log('  mirror failed: ' + e.message);
        // A cached empty/bad body must not poison the next attempt.
        try { const p = join(cacheDir, 'overpass_' + KEY + '.json');
              if (existsSync(p)) (await import('node:fs')).unlinkSync(p); } catch {}
      }
    }
    if (round < 2) { console.log('  retrying in 15 s...'); await new Promise(r => setTimeout(r, 15000)); }
  }
  throw lastErr;
}

// MULTI-TILE, lifted from the Bogue bake. One .hgt covered the Parkway; Tail
// of the Dragon straddles the W084/W085 boundary at lon -84, so a single-tile
// sampler would read zero for a third of it and bake a cliff.
const tiles = new Map();
async function loadTile(latN, lonW) {
  const key = `N${String(latN).padStart(2, '0')}W${String(-lonW).padStart(3, '0')}`;
  if (tiles.has(key)) return tiles.get(key);
  const gz = await fetchCached(key + '.hgt.gz',
    `https://s3.amazonaws.com/elevation-tiles-prod/skadi/N${String(latN).padStart(2, '0')}/${key}.hgt.gz`);
  const raw = gunzipSync(gz);
  const posts = 3601;
  if (raw.length !== posts * posts * 2) throw new Error(`${key}: unexpected size ${raw.length}`);
  const t = { raw, posts, latN: latN + 1, lonW };
  tiles.set(key, t);
  console.log('  SRTM ' + key + ' loaded');
  return t;
}

function sampleTile(t, lat, lon) {
  const { raw, posts, latN, lonW } = t;
  const at = (r, c) => {
    r = Math.min(posts - 1, Math.max(0, r)); c = Math.min(posts - 1, Math.max(0, c));
    return raw.readInt16BE((r * posts + c) * 2);
  };
  const fr = (latN - lat) * 3600, fc = (lon - lonW) * 3600;
  const r0 = Math.floor(fr), c0 = Math.floor(fc);
  const tr = fr - r0, tc = fc - c0;
  let h00 = at(r0, c0), h01 = at(r0, c0 + 1), h10 = at(r0 + 1, c0), h11 = at(r0 + 1, c0 + 1);
  const ok = v => v > -32000;
  const fb = [h00, h01, h10, h11].find(ok) ?? 0;
  if (!ok(h00)) h00 = fb; if (!ok(h01)) h01 = fb;
  if (!ok(h10)) h10 = fb; if (!ok(h11)) h11 = fb;
  return (h00 * (1 - tc) + h01 * tc) * (1 - tr) + (h10 * (1 - tc) + h11 * tc) * tr;
}

async function buildElevSampler() {
  for (let lat = Math.floor(BBOX.s); lat <= Math.floor(BBOX.n); lat++)
    for (let lon = Math.floor(BBOX.w); lon <= Math.floor(BBOX.e); lon++)
      await loadTile(lat, lon);
  return (lat, lon) => {
    const t = tiles.get(`N${String(Math.floor(lat)).padStart(2, '0')}W${String(-Math.floor(lon)).padStart(3, '0')}`);
    return t ? sampleTile(t, lat, lon) : 0;
  };
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

// ------------------------------------------------------------ projection
// Local equirectangular about the route centroid: x = east, z = north,
// exactly what Unity's ground plane wants. Sub-0.1% over a 20 km window.
function makeProjection(lat0, lon0) {
  const phi = lat0 * Math.PI / 180;
  const mLat = 111132.92 - 559.82 * Math.cos(2 * phi) + 1.175 * Math.cos(4 * phi);
  const mLon = 111412.84 * Math.cos(phi) - 93.5 * Math.cos(3 * phi);
  return {
    mLat, mLon,
    toXZ: (lat, lon) => ({ x: (lon - lon0) * mLon, z: (lat - lat0) * mLat }),
    toLL: (x, z) => ({ lat: lat0 + z / mLat, lon: lon0 + x / mLon }),
  };
}

// --------------------------------------------------------------- helpers
const dist2 = (a, b) => { const dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; };

function arcPositions(pts) {
  const s = [0];
  for (let i = 1; i < pts.length; i++)
    s.push(s[i - 1] + Math.sqrt(dist2(pts[i - 1], pts[i])));
  return s;
}

/// The world point at arc position <at> along a chain. The inverse of
/// projectOntoChain, and needed for the same reason: a capped run's finish is
/// known as a DISTANCE and has to become a PLACE before it can be re-measured
/// on the waypoint list.
function pointAtS(pts, s, at) {
  if (at <= 0) return { x: pts[0].x, z: pts[0].z };
  for (let i = 1; i < pts.length; i++) {
    if (s[i] < at) continue;
    const seg = s[i] - s[i - 1];
    const f = seg > 1e-9 ? (at - s[i - 1]) / seg : 0;
    return { x: pts[i - 1].x + (pts[i].x - pts[i - 1].x) * f,
             z: pts[i - 1].z + (pts[i].z - pts[i - 1].z) * f };
  }
  return { x: pts[pts.length - 1].x, z: pts[pts.length - 1].z };
}

function projectOntoChain(pts, s, p) {
  let best = { d2: Infinity, s: 0 };
  for (let i = 0; i + 1 < pts.length; i++) {
    const ax = pts[i].x, az = pts[i].z;
    const ex = pts[i + 1].x - ax, ez = pts[i + 1].z - az;
    const len2 = ex * ex + ez * ez;
    if (len2 < 1e-9) continue;
    let t = ((p.x - ax) * ex + (p.z - az) * ez) / len2;
    t = Math.max(0, Math.min(1, t));
    const qx = ax + ex * t, qz = az + ez * t;
    const d2 = (qx - p.x) ** 2 + (qz - p.z) ** 2;
    if (d2 < best.d2) best = { d2, s: s[i] + Math.sqrt(len2) * t };
  }
  return best;
}

// CENTRIPETAL Catmull-Rom through the cut vertices, densely sampled then
// arc-resampled.
//
// UNIFORM parameterisation was the first version, copied from TrackCatalog —
// where it is right, because circuit control points are AUTHORED at comparable
// spacing. OSM vertices are not. A mountain road is digitised with clusters two
// metres apart through a switchback and fifty-metre chords down the straight
// between them, and uniform Catmull-Rom on a spacing ratio like that overshoots
// hard: NC 215 baked a 1.5 m plan radius and NC 80 a 3.9 m, on roads whose real
// switchbacks are twelve to fifteen. Those are not corners, they are the spline
// doubling back on itself, and the ribbon builder would have splayed the road
// sixty metres wide around them.
//
// It is the same trap the Bogue bake hit — one OSM way modelling a bridge as
// two vertices, 40 m / 40 m / 1288 m, giving an 11 m hairpin — where it was
// patched by DENSIFYING long segments. That treats one half of it: a very SHORT
// segment beside a normal one breaks it just as badly, which is the mountain
// case. Centripetal (alpha = 0.5) is the actual fix and it is what the
// parameterisation exists for — it provably cannot cusp or self-intersect at
// any spacing ratio, while still passing through every vertex. Evaluated with
// the Barry-Goldman pyramid, which is the numerically stable form.
function splineResample(pts, spacing) {
  // Coincident vertices give a zero knot interval, and OSM has them wherever
  // two ways were joined at a shared node. Dropped, keeping each survivor's
  // ORIGINAL index — the bridge flags are looked up by it downstream, and a
  // renumbered chain would move every span.
  const src = [];
  for (let i = 0; i < pts.length; i++)
    if (!src.length || dist2(src[src.length - 1], pts[i]) > 0.25)
      src.push({ x: pts[i].x, z: pts[i].z, oi: i });
  if (src.length < 2) throw new Error('spline needs at least two distinct points');

  // Centripetal knots: dt = |dP| ^ 0.5, floored so the duplicated end points
  // (P(-1) = P(0) on an open spline) cannot collapse an interval.
  const knot = (t, a, b) => t + Math.max(1e-3, Math.pow(dist2(a, b), 0.25));

  const dense = [];
  const P = i => src[Math.max(0, Math.min(src.length - 1, i))];
  for (let i = 0; i + 1 < src.length; i++) {
    const p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
    const t0 = 0, t1 = knot(t0, p0, p1), t2 = knot(t1, p1, p2), t3 = knot(t2, p2, p3);
    const seg = Math.sqrt(dist2(p1, p2));
    const steps = Math.max(2, Math.ceil(seg));   // dense spacing ~1 m
    for (let sIdx = 0; sIdx < steps; sIdx++) {
      const t = t1 + (t2 - t1) * (sIdx / steps);
      const mix = (a, b, ta, tb) => {
        const d = tb - ta;
        const w = (tb - t) / d, v = (t - ta) / d;
        return { x: a.x * w + b.x * v, z: a.z * w + b.z * v };
      };
      const A1 = mix(p0, p1, t0, t1);
      const A2 = mix(p1, p2, t1, t2);
      const A3 = mix(p2, p3, t2, t3);
      const B1 = mix(A1, A2, t0, t2);
      const B2 = mix(A2, A3, t1, t3);
      const C  = mix(B1, B2, t1, t2);
      dense.push({ x: C.x, z: C.z, srcS: null, srcI: src[i].oi });
    }
  }
  dense.push({ x: src[src.length - 1].x, z: src[src.length - 1].z,
               srcS: null, srcI: src[src.length - 1].oi });

  return arcResample(dense, spacing);
}

/// Walk a polyline and drop a point every <spacing> metres. Lifted out of
/// splineResample because the plan relaxation below needs it too: nudging a
/// waypoint sideways shortens the chords either side of it, and a radius
/// measured on shortened chords is not the radius the road has.
function arcResample(poly, spacing) {
  const out = [poly[0]];
  let acc = 0;
  for (let i = 1; i < poly.length; i++) {
    const d = Math.sqrt(dist2(poly[i - 1], poly[i]));
    acc += d;
    while (acc >= spacing) {
      const over = acc - spacing;
      const t = d > 1e-9 ? over / d : 0;
      out.push({
        x: poly[i].x + (poly[i - 1].x - poly[i].x) * t,
        z: poly[i].z + (poly[i - 1].z - poly[i].z) * t,
        srcI: poly[i].srcI,
      });
      acc = over;
    }
  }
  // KEEP THE LAST POINT. Walking in fixed steps leaves a remainder shorter than
  // one step, and dropping it costs a waypoint per call — which is nothing when
  // this ran once at the end of splineResample and was 1204 waypoints when the
  // relaxation below started calling it twelve hundred times. Forty per cent of
  // NC 80 disappeared off the bottom of the mountain, and the only sign of it
  // was the printed elevation range starting 85 m higher than the survey.
  const last = poly[poly.length - 1];
  if (dist2(out[out.length - 1], last) > 1e-6)
    out.push({ x: last.x, z: last.z, srcI: last.srcI });
  return out;
}

// ------------------------------------------------------------------ main
const overpass = await fetchOverpass();
const elevAt = await buildElevSampler();

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
function planRadius(w, i) {
  const a = Math.sqrt(dist2(w[i - 1], w[i]));
  const b = Math.sqrt(dist2(w[i], w[i + 1]));
  const c = Math.sqrt(dist2(w[i - 1], w[i + 1]));
  const area = Math.abs((w[i].x - w[i - 1].x) * (w[i + 1].z - w[i - 1].z)
                      - (w[i + 1].x - w[i - 1].x) * (w[i].z - w[i - 1].z)) * 0.5;
  return area < 1e-6 ? Infinity : (a * b * c) / (4 * area);
}
function tightestPlan(w) {
  let minR = Infinity, at = 0, under = 0;
  for (let i = 1; i + 1 < w.length; i++) {
    const R = planRadius(w, i);
    if (R < minR) { minR = R; at = i; }
    if (R < RADIUS_FLOOR_M) under++;
  }
  return { minR, at, under };
}
{
  const t = tightestPlan(wp);
  console.log(`tightest corner ${t.minR.toFixed(1)} m at wp ${t.at}` +
              (t.under ? `  — ${t.under} waypoint(s) under the ${RADIUS_FLOOR_M} m floor` : ''));
  if (t.under)
    throw new Error(
      `${t.under} waypoint(s) under the ${RADIUS_FLOOR_M} m floor, tightest ` +
      `${t.minR.toFixed(1)} m. This road cannot be baked at ` +
      'this width as it stands. Do NOT reach for a smoothing pass: one was ' +
      'written and it is in the history for a reason — opening a hairpin by ' +
      'pulling its apex toward the chord CUTS THE CORNER OFF, which took 300 m ' +
      'out of NC 80 and left the finish line past the end of the route. Check ' +
      'the raw OSM first (a 30 m chord over the densified way tells you whether ' +
      'the turn is real); if it is real, the answer is a different cut, a ' +
      'narrower roadWidth, or a floor derived from the CAR and the road width ' +
      'rather than a constant.');
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
const wpBridge = wp.map(p => {
  const i = Math.max(0, Math.min(cutBridge.length - 1, p.srcI));
  const j = Math.min(cutBridge.length - 1, i + 1);
  return cutBridge[i] || cutBridge[j];
});

// Heights: DEM at stations, gaussian smooth, grade clamp, then per waypoint.
const rawH = wp.map(p => {
  const ll = proj.toLL(p.x, p.z);
  return elevAt(ll.lat, ll.lon);
});
const win = Math.ceil((SMOOTH_SIGMA * 3) / SPACING);
const smoothH = rawH.map((_, i) => {
  let sw = 0, sh = 0;
  for (let o = -win; o <= win; o++) {
    const j = Math.max(0, Math.min(rawH.length - 1, i + o));
    const w = Math.exp(-((o * SPACING) ** 2) / (2 * SMOOTH_SIGMA * SMOOTH_SIGMA));
    sw += w; sh += w * rawH[j];
  }
  return sh / sw;
});
// Grade clamp, two directions — then a LIGHT re-smooth to round the kink a
// hard clamp leaves at its boundary (a grade discontinuity is a vertical
// hairpin, and the self-test's crest-radius floor exists to catch exactly
// that shape). The re-smooth can nudge a few segments a hair past the
// clamp, which is why the limit here sits under the 9.5% the game tests.
for (let pass = 0; pass < 3; pass++) {
  for (let i = 1; i < smoothH.length; i++) {
    const d = smoothH[i] - smoothH[i - 1];
    const lim = MAX_GRADE * SPACING;
    if (d > lim) smoothH[i] = smoothH[i - 1] + lim;
  }
  for (let i = smoothH.length - 2; i >= 0; i--) {
    const d = smoothH[i] - smoothH[i + 1];
    const lim = MAX_GRADE * SPACING;
    if (d > lim) smoothH[i] = smoothH[i + 1] + lim;
  }
  const kinkSigma = 22, kinkWin = Math.ceil((kinkSigma * 3) / SPACING);
  const rounded = smoothH.map((_, i) => {
    let sw = 0, sh = 0;
    for (let o = -kinkWin; o <= kinkWin; o++) {
      const j = Math.max(0, Math.min(smoothH.length - 1, i + o));
      const w = Math.exp(-((o * SPACING) ** 2) / (2 * kinkSigma * kinkSigma));
      sw += w; sh += w * smoothH[j];
    }
    return sh / sw;
  });
  for (let i = 0; i < smoothH.length; i++) smoothH[i] = rounded[i];
}

let maxGrade = 0, minH = Infinity, maxH = -Infinity;
for (let i = 1; i < smoothH.length; i++) {
  maxGrade = Math.max(maxGrade, Math.abs(smoothH[i] - smoothH[i - 1]) / SPACING);
  minH = Math.min(minH, smoothH[i]); maxH = Math.max(maxH, smoothH[i]);
}
const baseM = Math.floor(minH - 40);
console.log(`route elevation ${minH.toFixed(0)}..${maxH.toFixed(0)} m ASL, ` +
  `max grade ${(maxGrade * 100).toFixed(1)}%, baseM ${baseM}`);

// Bridge spans in metres-along.
const spans = [];
let open = -1;
for (let i = 0; i < wpBridge.length; i++) {
  if (wpBridge[i] && open < 0) open = i * SPACING;
  if ((!wpBridge[i] || i === wpBridge.length - 1) && open >= 0) {
    spans.push([open, i * SPACING]); open = -1;
  }
}
// merge close, drop short
for (let i = spans.length - 2; i >= 0; i--)
  if (spans[i + 1][0] - spans[i][1] < BRIDGE_MERGE_M) {
    spans[i][1] = spans[i + 1][1]; spans.splice(i + 1, 1);
  }
const bridges = spans.filter(s => s[1] - s[0] >= MIN_BRIDGE_M);
console.log(`bridge spans: ${bridges.map(s =>
  `${s[0].toFixed(0)}-${s[1].toFixed(0)} (${(s[1] - s[0]).toFixed(0)} m)`).join(', ') || 'none'}`);

// ------------------------------------------------------------- DEM grids
function bakeGrid(name, cell, margin) {
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (const p of wp) {
    minX = Math.min(minX, p.x); maxX = Math.max(maxX, p.x);
    minZ = Math.min(minZ, p.z); maxZ = Math.max(maxZ, p.z);
  }
  minX -= margin; maxX += margin; minZ -= margin; maxZ += margin;
  const cols = Math.ceil((maxX - minX) / cell) + 1;
  const rows = Math.ceil((maxZ - minZ) / cell) + 1;
  const buf = Buffer.alloc(cols * rows * 2);
  for (let r = 0; r < rows; r++)
    for (let c = 0; c < cols; c++) {
      const x = minX + c * cell, z = minZ + r * cell;
      const ll = proj.toLL(x, z);
      const h = elevAt(ll.lat, ll.lon) - baseM;
      buf.writeInt16LE(Math.round(h * 10), (r * cols + c) * 2);
    }
  const outPath = join(projectRoot, 'Assets', 'PSXRacing', 'Art', CFG.artDir, name + '.bytes');
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, buf);
  console.log(`${name}: ${cols}x${rows} @ ${cell} m (${(buf.length / 1024).toFixed(0)} KB)`);
  return { originX: minX, originZ: minZ, cell, cols, rows };
}

const near = bakeGrid(CFG.prefix + '_dem_near', NEAR_CELL, NEAR_MARGIN);
const far = bakeGrid(CFG.prefix + '_dem_far', FAR_CELL, FAR_MARGIN);

const metaPath = join(projectRoot, 'Assets', 'PSXRacing', 'Art', CFG.artDir, CFG.prefix + '_dem_meta.json');
writeFileSync(metaPath, JSON.stringify({ baseM, near, far }, null, 2));

// ------------------------------------------------------------ stage json
// Flat arrays, deliberately: Unity's JsonUtility parses [1,2,3] but not
// [[1,2],[3,4]], and the consumer is TrackCatalog at runtime.
const xyz = [];
for (let i = 0; i < wp.length; i++)
  xyz.push(+wp[i].x.toFixed(2), +(smoothH[i] - baseM).toFixed(2), +wp[i].z.toFixed(2));
const stage = {
  // CFG.name, not a literal. This was left hardcoded when fetch_brp was
  // generalised, so every road baked itself a JSON claiming to be the Parkway.
  // Nothing reads it at runtime — TrackCatalog takes only attribution — which
  // is exactly why it went unnoticed and exactly why it had to be fixed: an
  // unread field that lies is a trap for whoever reads it next.
  name: CFG.name,
  attribution: 'Route data (c) OpenStreetMap contributors. Elevation: USGS/NASA SRTM.',
  lat0: proj.toLL(0, 0).lat, lon0: proj.toLL(0, 0).lon, baseM,
  spacing: SPACING,
  // MEASURED, not assumed. Both used to be written from the constants the cut
  // was asked for; the cut is now clamped to the road that actually exists, so
  // a spur with no lead-in would have put its start line 50 m before its own
  // first metre and its finish line 120 m past the summit.
  startLineM: wpStart,
  finishM: wpFinish,
  bridges: bridges.flat(),
  xyz,
};
const stagePath = join(projectRoot, 'Assets', 'PSXRacing', 'Resources', CFG.resKey + '.json');
writeFileSync(stagePath, JSON.stringify(stage));
console.log(`${CFG.resKey}.json: ${stage.xyz.length / 3} pts, finish at ${stage.finishM} m ` +
  `(${(readFileSync(stagePath).length / 1024).toFixed(0)} KB)`);
console.log('OK');
