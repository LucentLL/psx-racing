// fetch_brp.mjs — bake the Blue Ridge Parkway stage data for PSX Racing.
//
// Pulls the parkway centreline from OpenStreetMap (Overpass) and elevation
// from the SRTM 1-arc-second tile AWS mirrors (skadi .hgt.gz — raw int16,
// no PNG/TIFF decoding), then bakes:
//
//   Assets/PSXRacing/Resources/brp_stage.json   route waypoints at 4 m + bridges
//   Assets/PSXRacing/Art/BRP/brp_dem_near.bytes 12 m ground grid, route bbox+1.2 km
//   Assets/PSXRacing/Art/BRP/brp_dem_far.bytes  60 m vista grid, route bbox+9 km
//   Assets/PSXRacing/Art/BRP/brp_dem_meta.json  grid origins/cells/dims
//
// The stage: SOUTHBOUND from below Rough Ridge, across the Linn Cove
// Viaduct, finishing at Beacon Heights — the Grandfather Mountain mile.
// Southbound puts the viaduct's outside lane against the Wilson Creek
// valley and the most famous corner right before the finish.
//
// Heights are DEM-sampled then smoothed along the path: SRTM posts are
// ~30 m and carry a metre or two of noise, which at 4 m waypoints would
// read as corrugations nobody paved. The smoothing keeps the real grades
// (the parkway is engineered to ~8%) and drops the noise.
//
// Deliberately dependency-free. Downloads cache in tools/brp/cache/.
//
// Data: (c) OpenStreetMap contributors (ODbL) — credited in the stage
// blurb like Charlotte. Elevation: USGS/NASA SRTM (public domain).

import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gunzipSync } from 'node:zlib';

const here = dirname(fileURLToPath(import.meta.url));
const cacheDir = join(here, 'cache');
const projectRoot = join(here, '..', '..');
mkdirSync(cacheDir, { recursive: true });

// ---------------------------------------------------------------- config
const BBOX = { s: 36.06, w: -81.86, n: 36.16, e: -81.74 };

// The cut is anchored at Beacon Heights (just north of the US-221
// crossing) and measured 6.9 km back up the parkway toward Rough Ridge.
// LEAD is road before the start line (the grid stands on it), SHUTDOWN is
// road past the finish (a car crosses at speed and needs to stop).
const ANCHOR_BEACON = { lat: 36.0813, lon: -81.8286 };
const RUN_M = 6900;
const LEAD_M = 60;
const SHUTDOWN_M = 300;
const SPACING = 4;            // TrackCatalog.Spacing — waypoint spacing
const STATION = 10;           // metres between DEM samples pre-smoothing
// SRTM posts are ~30 m and the road is a ledge cut into slopes the DEM
// smears — at sigma 30 a third of the route pinned against the grade
// clamp, which is the clamp doing the smoothing's job. The parkway's own
// vertical curves are long (45 mph design speed), so sigma 70 is still
// honest about every crest the road actually has.
const SMOOTH_SIGMA = 85;      // metres, gaussian along-path height smoothing
const MAX_GRADE = 0.085;      // clamp anything steeper (SRTM noise on cliffs)
const MIN_BRIDGE_M = 40;      // spans shorter than this are culverts
const BRIDGE_MERGE_M = 30;    // gaps smaller than this merge two spans

const NEAR_CELL = 12, NEAR_MARGIN = 1200;
const FAR_CELL = 60, FAR_MARGIN = 9000;

// ---------------------------------------------------------------- fetch
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
        const buf = await fetchCached('overpass_brp.json', url,
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
        try { const p = join(cacheDir, 'overpass_brp.json');
              if (existsSync(p)) (await import('node:fs')).unlinkSync(p); } catch {}
      }
    }
    if (round < 2) { console.log('  retrying in 15 s...'); await new Promise(r => setTimeout(r, 15000)); }
  }
  throw lastErr;
}

async function fetchHgt() {
  // N36W082 covers lat 36..37, lon -82..-81 — the whole bbox.
  const gz = await fetchCached('N36W082.hgt.gz',
    'https://s3.amazonaws.com/elevation-tiles-prod/skadi/N36/N36W082.hgt.gz');
  const raw = gunzipSync(gz);
  const posts = 3601;
  if (raw.length !== posts * posts * 2)
    throw new Error('unexpected HGT size ' + raw.length);
  return { raw, posts, latN: 37, lonW: -82 };
}

// int16 big-endian posts, row 0 = north edge. Bilinear, void-guarded.
function makeElevSampler(hgt) {
  const { raw, posts, latN, lonW } = hgt;
  const at = (r, c) => {
    r = Math.min(posts - 1, Math.max(0, r));
    c = Math.min(posts - 1, Math.max(0, c));
    return raw.readInt16BE((r * posts + c) * 2);
  };
  return (lat, lon) => {
    const fr = (latN - lat) * 3600;
    const fc = (lon - lonW) * 3600;
    const r0 = Math.floor(fr), c0 = Math.floor(fc);
    const tr = fr - r0, tc = fc - c0;
    let h00 = at(r0, c0), h01 = at(r0, c0 + 1),
        h10 = at(r0 + 1, c0), h11 = at(r0 + 1, c0 + 1);
    // SRTM v3 is void-filled here, but guard anyway.
    const ok = v => v > -32000;
    const fallback = [h00, h01, h10, h11].find(ok) ?? 0;
    if (!ok(h00)) h00 = fallback; if (!ok(h01)) h01 = fallback;
    if (!ok(h10)) h10 = fallback; if (!ok(h11)) h11 = fallback;
    return (h00 * (1 - tc) + h01 * tc) * (1 - tr)
         + (h10 * (1 - tc) + h11 * tc) * tr;
  };
}

// ------------------------------------------------------- assemble chain
function isParkway(tags) {
  if (!tags) return false;
  const name = (tags.name || '').toLowerCase();
  if (name.includes('blue ridge parkway')) return true;
  if (name.includes('linn cove')) return true;
  return (tags.ref || '').toUpperCase() === 'BLRP';
}

function assembleChain(overpass) {
  const ways = overpass.elements.filter(e => e.type === 'way' && isParkway(e.tags));
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

// Open Catmull-Rom through the cut vertices, densely sampled then
// arc-resampled — the same recipe TrackCatalog uses for circuit control
// points, so the ribbon gets real curvature instead of chorded kinks.
function splineResample(pts, spacing) {
  const dense = [];
  const P = i => pts[Math.max(0, Math.min(pts.length - 1, i))];
  for (let i = 0; i + 1 < pts.length; i++) {
    const p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
    // Enough sub-samples that dense spacing ~1 m
    const seg = Math.sqrt(dist2(p1, p2));
    const steps = Math.max(2, Math.ceil(seg));
    for (let sIdx = 0; sIdx < steps; sIdx++) {
      const t = sIdx / steps, t2 = t * t, t3 = t2 * t;
      dense.push({
        x: 0.5 * ((2 * p1.x) + (-p0.x + p2.x) * t
          + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2
          + (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3),
        z: 0.5 * ((2 * p1.z) + (-p0.z + p2.z) * t
          + (2 * p0.z - 5 * p1.z + 4 * p2.z - p3.z) * t2
          + (-p0.z + 3 * p1.z - 3 * p2.z + p3.z) * t3),
        srcS: null, srcI: i,
      });
    }
  }
  dense.push({ ...pts[pts.length - 1], srcI: pts.length - 1 });

  const out = [dense[0]];
  let acc = 0;
  for (let i = 1; i < dense.length; i++) {
    const d = Math.sqrt(dist2(dense[i - 1], dense[i]));
    acc += d;
    while (acc >= spacing) {
      const over = acc - spacing;
      const t = d > 1e-9 ? over / d : 0;
      out.push({
        x: dense[i].x + (dense[i - 1].x - dense[i].x) * t,
        z: dense[i].z + (dense[i - 1].z - dense[i].z) * t,
        srcI: dense[i].srcI,
      });
      acc = over;
    }
  }
  return out;
}

// ------------------------------------------------------------------ main
const overpass = await fetchOverpass();
const hgt = await fetchHgt();
const elevAt = makeElevSampler(hgt);

const { chain, perVertexBridge } = assembleChain(overpass);

// Project about the rough middle of the eventual cut so numbers stay small.
const mid = chain[Math.floor(chain.length / 2)];
let proj = makeProjection(mid.lat, mid.lon);
let chainXZ = chain.map(p => ({ ...proj.toXZ(p.lat, p.lon) }));
let chainS = arcPositions(chainXZ);

// Anchor at Beacon Heights, walk RUN_M back north up the chain.
const beacon = projectOntoChain(chainXZ, chainS, proj.toXZ(ANCHOR_BEACON.lat, ANCHOR_BEACON.lon));
console.log(`Beacon Heights anchor: ${Math.sqrt(beacon.d2).toFixed(1)} m off the chain, ` +
  `s=${beacon.s.toFixed(0)} of ${chainS.at(-1).toFixed(0)} m`);

// Which direction along the chain is NORTH from the anchor? Compare
// latitudes a little either side.
const sAtFrac = f => {
  // point at arc position f
  let i = 0;
  while (i + 1 < chainS.length && chainS[i + 1] < f) i++;
  const t = (f - chainS[i]) / Math.max(1e-9, chainS[i + 1] - chainS[i]);
  return {
    x: chainXZ[i].x + (chainXZ[i + 1].x - chainXZ[i].x) * t,
    z: chainXZ[i].z + (chainXZ[i + 1].z - chainXZ[i].z) * t,
    i,
  };
};
const probeN = sAtFrac(Math.min(beacon.s + 500, chainS.at(-1)));
const probeP = sAtFrac(Math.max(beacon.s - 500, 0));
const northIsForward = probeN.z > probeP.z;
console.log('north along chain = ' + (northIsForward ? 'increasing s' : 'decreasing s'));

// Slice [beacon - SHUTDOWN ... beacon + RUN + LEAD] measured northward,
// then order the result north->south so waypoint 0 is the START (the run
// is southbound: down past the viaduct to Beacon Heights).
const sLo = northIsForward ? beacon.s - SHUTDOWN_M : beacon.s - (RUN_M + LEAD_M);
const sHi = northIsForward ? beacon.s + RUN_M + LEAD_M : beacon.s + SHUTDOWN_M;
if (sLo < 0 || sHi > chainS.at(-1))
  throw new Error(`cut [${sLo.toFixed(0)}, ${sHi.toFixed(0)}] leaves the chain ` +
    `(0..${chainS.at(-1).toFixed(0)}) — widen BBOX`);

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
const northFirst = northIsForward ? false : true;
if (!northFirst) { cut.reverse(); cutBridge.reverse(); }

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
const wp = splineResample(cut, SPACING);
const cutS = arcPositions(cut);
console.log(`waypoints: ${wp.length} (${(wp.length * SPACING / 1000).toFixed(2)} km)`);

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
  const outPath = join(projectRoot, 'Assets', 'PSXRacing', 'Art', 'BRP', name + '.bytes');
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, buf);
  console.log(`${name}: ${cols}x${rows} @ ${cell} m (${(buf.length / 1024).toFixed(0)} KB)`);
  return { originX: minX, originZ: minZ, cell, cols, rows };
}

const near = bakeGrid('brp_dem_near', NEAR_CELL, NEAR_MARGIN);
const far = bakeGrid('brp_dem_far', FAR_CELL, FAR_MARGIN);

const metaPath = join(projectRoot, 'Assets', 'PSXRacing', 'Art', 'BRP', 'brp_dem_meta.json');
writeFileSync(metaPath, JSON.stringify({ baseM, near, far }, null, 2));

// ------------------------------------------------------------ stage json
// Flat arrays, deliberately: Unity's JsonUtility parses [1,2,3] but not
// [[1,2],[3,4]], and the consumer is TrackCatalog at runtime.
const xyz = [];
for (let i = 0; i < wp.length; i++)
  xyz.push(+wp[i].x.toFixed(2), +(smoothH[i] - baseM).toFixed(2), +wp[i].z.toFixed(2));
const stage = {
  name: 'Blue Ridge Parkway — Grandfather Mountain',
  attribution: 'Route data (c) OpenStreetMap contributors. Elevation: USGS/NASA SRTM.',
  lat0: proj.toLL(0, 0).lat, lon0: proj.toLL(0, 0).lon, baseM,
  spacing: SPACING,
  startLineM: LEAD_M,
  finishM: LEAD_M + RUN_M,
  bridges: bridges.flat(),
  xyz,
};
const stagePath = join(projectRoot, 'Assets', 'PSXRacing', 'Resources', 'brp_stage.json');
writeFileSync(stagePath, JSON.stringify(stage));
console.log(`brp_stage.json: ${stage.xyz.length / 3} pts, finish at ${stage.finishM} m ` +
  `(${(readFileSync(stagePath).length / 1024).toFixed(0)} KB)`);
console.log('OK');
