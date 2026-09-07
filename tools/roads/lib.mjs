// The road-bake toolkit: everything tools/roads/fetch_road.mjs and
// tools/clt/fetch_clt.mjs share.
//
// fetch_brp.mjs was copied into fetch_bogue.mjs, then generalised into
// fetch_road.mjs, and the Charlotte venues were about to be a FOURTH copy of
// the projection, the centripetal spline, the SRTM sampler, the height
// smoother and the DEM grid baker. Each copy had already drifted from the
// last (the Bogue spline is uniform, the roads one centripetal; the Bogue SRTM
// sampler is multi-tile, the BRP one was not). So the helpers live here once,
// with the two behaviours a CLOSED route needs (a periodic spline, a circular
// smoother, a wrap-aware corner test) as opt-in flags whose defaults are
// exactly what fetch_road.mjs did before — that script's bakes are shipped and
// must not move by a centimetre.
//
// Every function is pure over its arguments except the three that touch disk
// or the network (fetchCached, fetchOverpass, srtmSampler, bakeGrid,
// writeStage), which take their paths explicitly.

import { gunzipSync } from 'node:zlib';
import { mkdirSync, readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { dirname, join } from 'node:path';

// ------------------------------------------------------------- fetching
export async function fetchCached(cacheDir, name, url, opts) {
  const path = join(cacheDir, name);
  if (existsSync(path)) return readFileSync(path);
  console.log('fetching ' + url);
  const res = await fetch(url, opts);
  if (!res.ok) throw new Error(url + ' -> HTTP ' + res.status);
  const buf = Buffer.from(await res.arrayBuffer());
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, buf);
  return buf;
}

/// Global-coverage mirrors only — overpass.osm.ch is Switzerland-only and
/// happily returns an empty result for a North Carolina bbox, and a cached
/// empty body would poison every later run. overpass-api.de rate-limits
/// (429 during the Charlotte probe); the rotation is what makes that a retry
/// rather than a failure.
export const OVERPASS_MIRRORS = [
  'https://overpass-api.de/api/interpreter',
  'https://overpass.kumi.systems/api/interpreter',
  'https://overpass.private.coffee/api/interpreter',
  'https://maps.mail.ru/osm/tools/overpass/api/interpreter',
];

export async function fetchOverpass({ cacheDir, name, query, userAgent }) {
  let lastErr = null;
  for (let round = 0; round < 3; round++) {
    for (const url of OVERPASS_MIRRORS) {
      try {
        const buf = await fetchCached(cacheDir, name, url,
          { method: 'POST', body: 'data=' + encodeURIComponent(query),
            headers: {
              'Content-Type': 'application/x-www-form-urlencoded',
              'User-Agent': userAgent,
              'Accept': 'application/json',
            } });
        const parsed = JSON.parse(buf.toString('utf8'));
        if (!parsed.elements || !parsed.elements.length)
          throw new Error('empty result (regional mirror or bad query)');
        return parsed;
      } catch (e) {
        lastErr = e; console.log('  mirror failed: ' + e.message);
        // A cached empty/bad body must not poison the next attempt.
        try { const p = join(cacheDir, name); if (existsSync(p)) unlinkSync(p); } catch {}
      }
    }
    if (round < 2) { console.log('  retrying in 15 s...'); await new Promise(r => setTimeout(r, 15000)); }
  }
  throw lastErr;
}

// ----------------------------------------------------------------- SRTM
// MULTI-TILE, lifted from the Bogue bake. One .hgt covered the Parkway; Tail
// of the Dragon straddles the W084/W085 boundary at lon -84, so a single-tile
// sampler would read zero for a third of it and bake a cliff.
const tileKey = (latN, lonW) =>
  `N${String(latN).padStart(2, '0')}W${String(-lonW).padStart(3, '0')}`;

async function loadTile(tiles, cacheDir, latN, lonW) {
  const key = tileKey(latN, lonW);
  if (tiles.has(key)) return tiles.get(key);
  const gz = await fetchCached(cacheDir, key + '.hgt.gz',
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

/// An (lat, lon) => metres ASL sampler over every SRTM tile the bbox touches.
export async function srtmSampler(bbox, cacheDir) {
  const tiles = new Map();
  for (let lat = Math.floor(bbox.s); lat <= Math.floor(bbox.n); lat++)
    for (let lon = Math.floor(bbox.w); lon <= Math.floor(bbox.e); lon++)
      await loadTile(tiles, cacheDir, lat, lon);
  return (lat, lon) => {
    const t = tiles.get(tileKey(Math.floor(lat), Math.floor(lon)));
    return t ? sampleTile(t, lat, lon) : 0;
  };
}

// ------------------------------------------------------------ projection
// Local equirectangular about the route centroid: x = east, z = north,
// exactly what Unity's ground plane wants. Sub-0.1% over a 20 km window.
export function makeProjection(lat0, lon0) {
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
export const dist2 = (a, b) => { const dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; };

export function arcPositions(pts) {
  const s = [0];
  for (let i = 1; i < pts.length; i++)
    s.push(s[i - 1] + Math.sqrt(dist2(pts[i - 1], pts[i])));
  return s;
}

/// The world point at arc position <at> along a chain. The inverse of
/// projectOntoChain, and needed for the same reason: a capped run's finish is
/// known as a DISTANCE and has to become a PLACE before it can be re-measured
/// on the waypoint list.
export function pointAtS(pts, s, at) {
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

export function projectOntoChain(pts, s, p) {
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
//
// `loop`: the chain is a RING. The knot indices wrap instead of clamping, so
// the curve through the last vertex back to the first is as smooth as any
// other — an open spline treats P(-1) = P(0) and leaves a corner exactly at
// the start line, which is where the grid stands. The dense polyline closes
// back onto vertex 0 and arcResample keeps that closing point; the caller
// decides whether the near-duplicate survives (TrackCatalog's own rule drops
// it under half a spacing).
export function splineResample(pts, spacing, { loop = false } = {}) {
  // Coincident vertices give a zero knot interval, and OSM has them wherever
  // two ways were joined at a shared node. Dropped, keeping each survivor's
  // ORIGINAL index — the bridge flags are looked up by it downstream, and a
  // renumbered chain would move every span.
  const src = [];
  for (let i = 0; i < pts.length; i++)
    if (!src.length || dist2(src[src.length - 1], pts[i]) > 0.25)
      src.push({ x: pts[i].x, z: pts[i].z, oi: i });
  // A ring whose last vertex is its first (the way list closed on the same
  // node) would otherwise get a zero-length closing segment.
  if (loop && src.length > 2 && dist2(src[src.length - 1], src[0]) <= 0.25) src.pop();
  if (src.length < 2) throw new Error('spline needs at least two distinct points');

  // Centripetal knots: dt = |dP| ^ 0.5, floored so the duplicated end points
  // (P(-1) = P(0) on an open spline) cannot collapse an interval.
  const knot = (t, a, b) => t + Math.max(1e-3, Math.pow(dist2(a, b), 0.25));

  const dense = [];
  const n = src.length;
  const P = loop ? i => src[((i % n) + n) % n]
                 : i => src[Math.max(0, Math.min(n - 1, i))];
  const segs = loop ? n : n - 1;
  for (let i = 0; i < segs; i++) {
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
  const tail = loop ? src[0] : src[n - 1];
  dense.push({ x: tail.x, z: tail.z, srcS: null, srcI: loop ? src[n - 1].oi : tail.oi });

  return arcResample(dense, spacing);
}

/// Walk a polyline and drop a point every <spacing> metres. Lifted out of
/// splineResample because the plan relaxation below needs it too: nudging a
/// waypoint sideways shortens the chords either side of it, and a radius
/// measured on shortened chords is not the radius the road has.
export function arcResample(poly, spacing) {
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

// ------------------------------------------------------------ plan checks
// THE TIGHTEST CORNER, IN PLAN, MEASURED AND THEN ENFORCED.
//
// Measured, because the self-test asserts it downstream and a failure there
// costs a forty-minute build to hear about, while here it costs nothing — and
// because it is the ONLY thing that would have caught the uniform-spline
// overshoot above. A 1.5 m radius is invisible in an elevation profile, in a
// length, and in a screenshot of a road nobody has driven into yet.
export function planRadius(w, i, loop = false) {
  const n = w.length;
  const a0 = loop ? w[(i - 1 + n) % n] : w[i - 1];
  const a2 = loop ? w[(i + 1) % n] : w[i + 1];
  const a = Math.sqrt(dist2(a0, w[i]));
  const b = Math.sqrt(dist2(w[i], a2));
  const c = Math.sqrt(dist2(a0, a2));
  const area = Math.abs((w[i].x - a0.x) * (a2.z - a0.z)
                      - (a2.x - a0.x) * (w[i].z - a0.z)) * 0.5;
  return area < 1e-6 ? Infinity : (a * b * c) / (4 * area);
}

/// Tightest plan radius over a waypoint list and how many stations sit under
/// <floor>. On a ring the first and last waypoints are corners too.
export function tightestPlan(w, floor, { loop = false } = {}) {
  let minR = Infinity, at = 0, under = 0;
  const lo = loop ? 0 : 1, hi = loop ? w.length : w.length - 1;
  for (let i = lo; i < hi; i++) {
    const R = planRadius(w, i, loop);
    if (R < minR) { minR = R; at = i; }
    if (R < floor) under++;
  }
  return { minR, at, under };
}

/// The refusal, in one place, so both bakes say the same thing about it.
export function radiusFloorMessage(t, floor) {
  return `${t.under} waypoint(s) under the ${floor} m floor, tightest ` +
    `${t.minR.toFixed(1)} m. This road cannot be baked at ` +
    'this width as it stands. Do NOT reach for a smoothing pass: one was ' +
    'written and it is in the history for a reason — opening a hairpin by ' +
    'pulling its apex toward the chord CUTS THE CORNER OFF, which took 300 m ' +
    'out of NC 80 and left the finish line past the end of the route. Check ' +
    'the raw OSM first (a 30 m chord over the densified way tells you whether ' +
    'the turn is real); if it is real, the answer is a different cut, a ' +
    'narrower roadWidth, or a floor derived from the CAR and the road width ' +
    'rather than a constant.';
}

/// Nearest approach IN PLAN between two stretches of the route that are not
/// neighbours along it — the self-test's MinSelfClearance, so the bake can
/// print the number the 40-minute build would otherwise be the first to
/// report. Stations closer than <minSep> along the route are the same piece
/// of road; on a ring the separation is measured the short way round.
export function minSelfClearance(w, { loop = false, minSep = 22 } = {}) {
  const n = w.length;
  let min = Infinity, at = [0, 0];
  for (let i = 0; i < n; i++)
    for (let j = i + minSep; j < n; j++) {
      const sep = loop ? Math.min(j - i, n - (j - i)) : j - i;
      if (sep < minSep) continue;
      const d = dist2(w[i], w[j]);
      if (d < min) { min = d; at = [i, j]; }
    }
  return { minM: Math.sqrt(min), at };
}

// --------------------------------------------------------------- heights
// DEM at the waypoints, gaussian smooth, grade clamp, then a LIGHT re-smooth
// to round the kink a hard clamp leaves at its boundary (a grade discontinuity
// is a vertical hairpin, and the self-test's crest-radius floor exists to
// catch exactly that shape). The re-smooth can nudge a few segments a hair
// past the clamp, which is why the limit sits under the 9.5% the game tests.
//
// `circular`: the profile is a RING and the window wraps. Clamping the window
// at the ends is right for a road with ends; on a loop it leaves a seam at the
// start line where two independently smoothed halves meet, and the grade
// clamp — which propagates along the list — could not cross it. Wrapped, the
// smoother sees the same neighbourhood at station 0 as at any other, and the
// clamp passes run round twice so a violation at the seam is carried through.
export function smoothHeights(rawH, { spacing, sigma, maxGrade, circular = false }) {
  const n = rawH.length;
  const idx = circular ? j => ((j % n) + n) % n : j => Math.max(0, Math.min(n - 1, j));
  const gauss = (src, s) => {
    const win = Math.ceil((s * 3) / spacing);
    return src.map((_, i) => {
      let sw = 0, sh = 0;
      for (let o = -win; o <= win; o++) {
        const w = Math.exp(-((o * spacing) ** 2) / (2 * s * s));
        sw += w; sh += w * src[idx(i + o)];
      }
      return sh / sw;
    });
  };
  const smoothH = gauss(rawH, sigma);
  const lim = maxGrade * spacing;
  const laps = circular ? 2 : 1;
  for (let pass = 0; pass < 3; pass++) {
    for (let k = 1; k < n * laps; k++) {
      const i = idx(k), p = idx(k - 1);
      const d = smoothH[i] - smoothH[p];
      if (d > lim) smoothH[i] = smoothH[p] + lim;
    }
    for (let k = n * laps - 2; k >= 0; k--) {
      const i = idx(k), q = idx(k + 1);
      const d = smoothH[i] - smoothH[q];
      if (d > lim) smoothH[i] = smoothH[q] + lim;
    }
    const rounded = gauss(smoothH, 22);
    for (let i = 0; i < n; i++) smoothH[i] = rounded[i];
  }
  return smoothH;
}

/// Range, steepest station-to-station grade, and the tightest vertical radius
/// (the self-test's 3-station triple), for the printout.
export function profileStats(h, spacing, { circular = false } = {}) {
  const n = h.length;
  const idx = circular ? j => ((j % n) + n) % n : j => Math.max(0, Math.min(n - 1, j));
  let maxGrade = 0, minH = Infinity, maxH = -Infinity, minVertR = Infinity;
  for (let i = 0; i < n; i++) {
    minH = Math.min(minH, h[i]); maxH = Math.max(maxH, h[i]);
    const j = idx(i + 1);
    if (j !== i) maxGrade = Math.max(maxGrade, Math.abs(h[j] - h[i]) / spacing);
    const a = h[idx(i - 3)], c = h[idx(i + 3)];
    const g1 = (h[i] - a) / (3 * spacing), g2 = (c - h[i]) / (3 * spacing);
    const dg = Math.abs(g2 - g1) / (3 * spacing);
    if (dg > 1e-6) minVertR = Math.min(minVertR, 1 / dg);
  }
  return { minH, maxH, maxGrade, minVertR };
}

// --------------------------------------------------------------- bridges
/// A waypoint is on a bridge when its source cut segment is (either end of
/// the segment, so a span never loses its last station to rounding).
export function waypointBridgeFlags(wp, cutBridge, { loop = false } = {}) {
  const m = cutBridge.length;
  return wp.map(p => {
    const i = Math.max(0, Math.min(m - 1, p.srcI));
    const j = loop ? (i + 1) % m : Math.min(m - 1, i + 1);
    return cutBridge[i] || cutBridge[j];
  });
}

/// Bridge spans in metres-along from per-waypoint flags: runs, then spans
/// closer than <mergeM> merged, then anything under <minM> dropped. On a
/// ring a span that reaches the last station and one that starts at the
/// first are one deck across the seam, written past the lap length the way
/// TrackCatalog.BridgeBlend's modulo branch reads it.
export function bridgeSpans(wpBridge, spacing, { minM, mergeM, loop = false }) {
  const spans = [];
  let open = -1;
  for (let i = 0; i < wpBridge.length; i++) {
    if (wpBridge[i] && open < 0) open = i * spacing;
    if ((!wpBridge[i] || i === wpBridge.length - 1) && open >= 0) {
      spans.push([open, i * spacing]); open = -1;
    }
  }
  for (let i = spans.length - 2; i >= 0; i--)
    if (spans[i + 1][0] - spans[i][1] < mergeM) {
      spans[i][1] = spans[i + 1][1]; spans.splice(i + 1, 1);
    }
  if (loop && spans.length >= 2) {
    const lap = wpBridge.length * spacing;
    const first = spans[0], last = spans[spans.length - 1];
    if (first[0] === 0 && last[1] >= (wpBridge.length - 1) * spacing - 1e-6 &&
        lap - last[1] + first[0] < mergeM + spacing) {
      last[1] = lap + first[1];
      spans.shift();
    }
  }
  return spans.filter(s => s[1] - s[0] >= minM);
}

// ------------------------------------------------------------- DEM grids
/// Int16 decimetres above baseM, row-major from the south-west corner, over
/// the waypoints' bounding box grown by <margin>. Returns the meta the Unity
/// side reads (PSXRacingBuilder.Stage DemGridMeta).
export function bakeGrid({ wp, proj, elevAt, baseM, cell, margin, outDir, name }) {
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
  const outPath = join(outDir, name + '.bytes');
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, buf);
  console.log(`${name}: ${cols}x${rows} @ ${cell} m (${(buf.length / 1024).toFixed(0)} KB)`);
  return { originX: minX, originZ: minZ, cell, cols, rows };
}

export function writeDemMeta(outDir, prefix, baseM, near, far) {
  const metaPath = join(outDir, prefix + '_dem_meta.json');
  writeFileSync(metaPath, JSON.stringify({ baseM, near, far }, null, 2));
  return metaPath;
}

// ------------------------------------------------------------ stage json
/// Flat arrays, deliberately: Unity's JsonUtility parses [1,2,3] but not
/// [[1,2],[3,4]], and the consumer is TrackCatalog at runtime. `extra` is
/// spread in before xyz so a venue can carry more (the Charlotte bakes add
/// loop, lanes and the city-frame origin) without the shipped bakes changing
/// by a byte.
export function writeStage(stagePath, { name, attribution, proj, baseM, spacing,
                                        startLineM, finishM, bridges, wp, heights,
                                        extra = {} }) {
  const xyz = [];
  for (let i = 0; i < wp.length; i++)
    xyz.push(+wp[i].x.toFixed(2), +(heights[i] - baseM).toFixed(2), +wp[i].z.toFixed(2));
  const stage = {
    name,
    attribution,
    lat0: proj.toLL(0, 0).lat, lon0: proj.toLL(0, 0).lon, baseM,
    spacing,
    startLineM,
    finishM,
    bridges: bridges.flat(),
    ...extra,
    xyz,
  };
  mkdirSync(dirname(stagePath), { recursive: true });
  writeFileSync(stagePath, JSON.stringify(stage));
  console.log(`${stagePath.split(/[\\/]/).pop()}: ${stage.xyz.length / 3} pts, finish at ${stage.finishM} m ` +
    `(${(readFileSync(stagePath).length / 1024).toFixed(0)} KB)`);
  return stage;
}
