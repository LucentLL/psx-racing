// water.mjs - Charlotte's creeks and lakes from open data, with bed profiles
// sampled from USGS 3DEP (plan WP-04b, critic C2).
//
// Inputs (tools/city/cache/water/, fetched by fetch/fetch_water.mjs):
//   meck_creeks.geojson       Mecklenburg County "Creeks and Streams" (CC0)
//   meck_lakes.geojson        Mecklenburg County "Lakes and Ponds" (CC0)
//   usgs_flowlines.geojson    USGS 3DHP flowlines (public domain)
//   usgs_waterbodies.geojson  USGS 3DHP waterbodies (public domain)
//   county.geojson            Census TIGER: Mecklenburg's boundary
// and the 3DEP 1/3" box (lib/dem3dep.mjs).
//
// What is kept:
//   * CREEKS. Inside the county, the county's open channels of the class
//     that drains more than 640 acres (a square mile; SWIM buffer 100 ft):
//     the county's own line for every creek RG2 traced and the named creeks
//     it missed. Outside the county, 3DHP channel lines of Strahler order 4
//     or more, the same size of stream. Lines of one creek are chained end to
//     end, clipped to the DEM box and simplified (Douglas-Peucker, 3 m).
//     A creek carries water, and every road crossing it is a water span (the
//     owner's rule: every road crossing water gets a bridge).
//   * RAVINES (kind 2). The smaller open streams: the county's classes that
//     drain 100 to 640 acres (mt100, mt300), and outside the county 3DHP
//     channel lines of order 2 and 3. Their valleys are real and the 60 m
//     grid cannot hold them, so the ground is carved to their bed; but they
//     carry no water sheet and make no span: a road over one keeps its
//     embankment, as a road over a culvert does (plan WP-25 adds the pipe
//     and the headwalls). Simplified at 8 m, the bed every 40 m.
//   * WIDTH is a model, not a measurement: from the 3DHP stream order of the
//     nearest flowline (county lines borrow it), 6 m at order 4, 10 m at 5,
//     14 m at 6, up to 40 m; a creek is cut into pieces where its order
//     changes for 400 m or more.
//   * THE BED. Every 20 m along each line, the lowest 3DEP 1/3" pixel within
//     a few metres of the line (the surveyed line sits in the channel, the
//     10 m pixels straddle it), then made to fall downstream: a running
//     minimum from the higher end, which also takes the culverted road fills
//     the bare-earth DEM keeps out of the channel. Metres ASL.
//   * LAKES. The county's lakes and ponds of 2 ha or more (the Catawba chain:
//     Lake Wylie, Mountain Island Lake and the reach between, Lake Norman's
//     tail), and outside the county 3DHP lakes of 2 ha or more that no county
//     lake covers. Each must be FLAT in the hydro-flattened 3DEP (60% of its
//     pixels within 0.3 m of their median, which is its level), or it is not
//     a lake and is left out. Islands are not kept (one ring per lake).
//
// Output: [{ name, widthM, kind (0 creek, 1 lake, 2 ravine), lake, ravine,
// pts: [[x, z]...] (game frame), bed: Float64Array (m ASL every bedStep m
// along pts from its first point, the last sample at its end; a lake's one
// value is its level, bedStep 0) }].

import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

export const BED_STEP = 20;
const CREEK_CLASSES = new Set(['mt640']);
const RAVINE_CLASSES = new Set(['mt300', 'mt100']);
const USGS_MIN_ORDER = 4, USGS_RAVINE_ORDER = 2;
/// Douglas-Peucker tolerances, metres: the ground lattice is 8 m, so a
/// ravine drawn 8 m off its line still carves the right cell.
const SIMPLIFY_M = 3, RAVINE_SIMPLIFY_M = 8;
/// A ravine's bed is stored every 40 m (a creek's every BED_STEP).
const RAVINE_BED_STEP = 40;
export const KIND_CREEK = 0, KIND_LAKE = 1, KIND_RAVINE = 2;
const LAKE_MIN_M2 = 2e4;
const LAKE_SIMPLIFY_M = 12;
const MAX_PTS = 4000;          // CityMap packs a segment index in 12 bits
const WIDTH_BY_ORDER = { 1: 3, 2: 3, 3: 4, 4: 6, 5: 10, 6: 14, 7: 20, 8: 40, 9: 40 };

export const WATER_FILES = ['meck_creeks', 'meck_lakes', 'usgs_flowlines', 'usgs_waterbodies', 'county'];

export function waterInputPaths(cacheDir) {
  return WATER_FILES.map(n => ({ label: `tools/city/cache/water/${n}.geojson`, path: join(cacheDir, 'water', `${n}.geojson`) }));
}

// --------------------------------------------------------------- geometry
function dp(pts, tol) {
  if (pts.length < 3) return pts.slice();
  const keep = new Uint8Array(pts.length); keep[0] = keep[pts.length - 1] = 1;
  const stack = [[0, pts.length - 1]];
  while (stack.length) {
    const [a, b] = stack.pop();
    const [ax, az] = pts[a], [bx, bz] = pts[b];
    const dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
    let best = -1, bi = -1;
    for (let i = a + 1; i < b; i++) {
      const [px, pz] = pts[i];
      let t = L2 > 0 ? ((px - ax) * dx + (pz - az) * dz) / L2 : 0;
      t = Math.max(0, Math.min(1, t));
      const d = Math.hypot(px - ax - dx * t, pz - az - dz * t);
      if (d > best) { best = d; bi = i; }
    }
    if (best > tol) { keep[bi] = 1; stack.push([a, bi], [bi, b]); }
  }
  return pts.filter((_, i) => keep[i]);
}
function pip(ring, x, y) {
  let ins = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const [xi, yi] = ring[i], [xj, yj] = ring[j];
    if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) ins = !ins;
  }
  return ins;
}
function ringArea(r) { let a = 0; for (let i = 0, j = r.length - 1; i < r.length; j = i++) a += (r[j][0] + r[i][0]) * (r[j][1] - r[i][1]); return Math.abs(a) / 2; }
/// Sutherland-Hodgman against an axis-aligned rectangle [x0, z0, x1, z1].
function clipRing(ring, box) {
  let poly = ring;
  const edges = [[p => p[0] >= box[0], (a, b) => lerpAt(a, b, 0, box[0])], [p => p[0] <= box[2], (a, b) => lerpAt(a, b, 0, box[2])],
                 [p => p[1] >= box[1], (a, b) => lerpAt(a, b, 1, box[1])], [p => p[1] <= box[3], (a, b) => lerpAt(a, b, 1, box[3])]];
  for (const [inside, cross] of edges) {
    const out = [];
    for (let i = 0; i < poly.length; i++) {
      const cur = poly[i], prev = poly[(i + poly.length - 1) % poly.length];
      const ci = inside(cur), pi = inside(prev);
      if (ci) { if (!pi) out.push(cross(prev, cur)); out.push(cur); } else if (pi) out.push(cross(prev, cur));
    }
    poly = out;
    if (!poly.length) break;
  }
  return poly;
}
function lerpAt(a, b, k, v) { const t = (v - a[k]) / (b[k] - a[k]); return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]; }
/// Split a polyline into the runs inside the rectangle (cut at the crossings).
function clipLine(pts, box) {
  const inside = p => p[0] >= box[0] && p[0] <= box[2] && p[1] >= box[1] && p[1] <= box[3];
  const runs = []; let cur = [];
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i];
    if (inside(p)) { if (!cur.length && i > 0) cur.push(edgeCross(pts[i - 1], p, box)); cur.push(p); }
    else if (cur.length) { cur.push(edgeCross(p, pts[i - 1], box)); runs.push(cur); cur = []; }
  }
  if (cur.length) runs.push(cur);
  return runs.filter(r => r.length >= 2);
}
function edgeCross(out, inn, box) {
  let t = 0;
  for (const [k, v] of [[0, box[0]], [0, box[2]], [1, box[1]], [1, box[3]]]) {
    const d = inn[k] - out[k];
    if (Math.abs(d) < 1e-12) continue;
    const tt = (v - out[k]) / d;
    if (tt >= 0 && tt <= 1) {
      const q = [out[0] + (inn[0] - out[0]) * tt, out[1] + (inn[1] - out[1]) * tt];
      if (q[0] >= box[0] - 1e-6 && q[0] <= box[2] + 1e-6 && q[1] >= box[1] - 1e-6 && q[1] <= box[3] + 1e-6) t = Math.max(t, tt);
    }
  }
  return [out[0] + (inn[0] - out[0]) * t, out[1] + (inn[1] - out[1]) * t];
}
function lineLen(p) { let l = 0; for (let i = 1; i < p.length; i++) l += Math.hypot(p[i][0] - p[i - 1][0], p[i][1] - p[i - 1][1]); return l; }
function lines(g) { return !g ? [] : g.type === 'LineString' ? [g.coordinates] : g.type === 'MultiLineString' ? g.coordinates : []; }
function polys(g) { return !g ? [] : g.type === 'Polygon' ? [g.coordinates] : g.type === 'MultiPolygon' ? g.coordinates : []; }

/// Join the lines of one group end to end where their ends meet (within
/// 0.5 m), into as few chains as the branching allows.
function chain(parts) {
  const key = p => `${Math.round(p[0] * 2)},${Math.round(p[1] * 2)}`;
  const ends = new Map();
  parts.forEach((p, i) => { for (const [e, at] of [[0, p[0]], [1, p[p.length - 1]]]) { const k = key(at); if (!ends.has(k)) ends.set(k, []); ends.get(k).push([i, e]); } });
  const used = new Uint8Array(parts.length), out = [];
  const nextFrom = (k, not) => { const l = (ends.get(k) || []).filter(([i]) => !used[i] && i !== not); return l.length === 1 && (ends.get(k) || []).length === 2 ? l[0] : null; };
  for (let s = 0; s < parts.length; s++) {
    if (used[s]) continue;
    used[s] = 1;
    let line = parts[s].slice();
    for (let dir = 0; dir < 2; dir++) {
      for (;;) {
        const tail = line[line.length - 1];
        const nx = nextFrom(key(tail), -1);
        if (!nx) break;
        const [i, e] = nx; used[i] = 1;
        const p = e === 0 ? parts[i] : parts[i].slice().reverse();
        line = line.concat(p.slice(1));
      }
      line.reverse();
    }
    out.push(line);
  }
  return out;
}

// ------------------------------------------------------------------ build
export function buildWaters({ cacheDir, dem3, toX, toZ, toLat, toLon, box, log = console.log }) {
  const W = n => {
    const p = join(cacheDir, 'water', `${n}.geojson`);
    if (!existsSync(p)) throw new Error(`${p} is missing: fetch it with node tools/city/fetch/fetch_water.mjs`);
    return JSON.parse(readFileSync(p, 'utf8'));
  };
  const toG = ([lon, lat]) => [toX(lon), toZ(lat)];
  const countyRings = W('county').features.flatMap(f => polys(f.geometry).map(p => p[0]));
  const inCounty = (lon, lat) => countyRings.some(r => pip(r, lon, lat));

  // ---- 3DHP flowlines: an order lookup for everything, and the lines outside
  const flow = W('usgs_flowlines').features;
  const ORD = 200;                                        // hash cell, metres
  const ordHash = new Map();
  for (const f of flow) {
    const so = f.properties.streamorder;
    if (!(so >= 1)) continue;
    for (const ln of lines(f.geometry)) for (const c of ln) {
      const [x, z] = toG(c), k = `${Math.floor(x / ORD)},${Math.floor(z / ORD)}`;
      if (!ordHash.has(k)) ordHash.set(k, []);
      ordHash.get(k).push([x, z, so]);
    }
  }
  const orderNear = (x, z, r = 60) => {
    let best = Infinity, so = 0;
    for (let cx = Math.floor((x - r) / ORD); cx <= Math.floor((x + r) / ORD); cx++)
      for (let cz = Math.floor((z - r) / ORD); cz <= Math.floor((z + r) / ORD); cz++)
        for (const [px, pz, o] of ordHash.get(`${cx},${cz}`) || []) { const d = Math.hypot(px - x, pz - z); if (d < r && d < best) { best = d; so = o; } }
    return so;
  };

  const groups = new Map();   // key -> { name, src, kind, parts: [game pts] }
  const addPart = (key, name, src, kind, pts) => { if (!groups.has(key)) groups.set(key, { name, src, kind, parts: [] }); groups.get(key).parts.push(pts); };

  let meckKept = 0;
  for (const f of W('meck_creeks').features) {
    const pr = f.properties;
    const kind = CREEK_CLASSES.has(pr.class) ? KIND_CREEK : RAVINE_CLASSES.has(pr.class) ? KIND_RAVINE : -1;
    if (kind < 0) continue;
    const name = ((pr.stream || '').trim() || (pr.name || '').trim());
    for (const ln of lines(f.geometry)) { addPart(`meck|${kind}|${name}`, name, 'meck', kind, ln.map(toG)); meckKept++; }
  }
  let usgsKept = 0;
  for (const f of flow) {
    const pr = f.properties;
    if (pr.featuretypelabel !== 'Channel Line' || !(pr.streamorder >= USGS_RAVINE_ORDER)) continue;
    const kind = pr.streamorder >= USGS_MIN_ORDER ? KIND_CREEK : KIND_RAVINE;
    for (const ln of lines(f.geometry)) {
      // the runs of the line OUTSIDE the county (the county's lines own the inside)
      let run = [];
      const flush = () => { if (run.length >= 2) { addPart(`usgs|${kind}|${pr.levelpath}`, (pr.gnisidlabel || '').trim(), 'usgs', kind, run.map(toG)); usgsKept++; } run = []; };
      for (const c of ln) { if (inCounty(c[0], c[1])) flush(); else run.push(c); }
      flush();
    }
  }

  const waters = [];
  const stats = { creeks: 0, creekKm: 0, creekPts: 0, ravines: 0, ravineKm: 0, ravinePts: 0, lakes: 0, lakePts: 0, lakesNotFlat: [], beds: 0, meckParts: meckKept, usgsParts: usgsKept };
  const gbox = [box.x0, box.z0, box.x1, box.z1];
  const widthOf = so => WIDTH_BY_ORDER[Math.max(1, Math.min(9, so || USGS_MIN_ORDER))];
  for (const [, g] of [...groups].sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0))) {
    for (const whole of chain(g.parts)) {
      for (const run of clipLine(whole, gbox)) {
        const pts = dp(run, g.kind === KIND_RAVINE ? RAVINE_SIMPLIFY_M : SIMPLIFY_M);
        const L = lineLen(pts);
        if (L < 60) continue;
        const acc = [0];
        for (let i = 1; i < pts.length; i++) acc.push(acc[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
        // the stream order at every vertex (3DHP's, county lines borrow it),
        // gaps filled from the neighbours
        const ord = pts.map(p => orderNear(p[0], p[1]));
        for (let i = 1; i < ord.length; i++) if (!ord[i]) ord[i] = ord[i - 1];
        for (let i = ord.length - 2; i >= 0; i--) if (!ord[i]) ord[i] = ord[i + 1];
        const pointAt = s => {
          let i = 1;
          while (i < pts.length - 1 && acc[i] < s) i++;
          const t = acc[i] > acc[i - 1] ? Math.min(1, Math.max(0, (s - acc[i - 1]) / (acc[i] - acc[i - 1]))) : 0;
          return [pts[i - 1][0] + (pts[i][0] - pts[i - 1][0]) * t, pts[i - 1][1] + (pts[i][1] - pts[i - 1][1]) * t, i - 1];
        };
        // THE BED along the whole chain, every BED_STEP m: the lowest 3DEP
        // pixel near the line, then falling downstream (a running minimum
        // from the higher end)
        const n = Math.max(2, Math.ceil(L / BED_STEP) + 1);
        const bed = new Float64Array(n);
        for (let k = 0; k < n; k++) {
          const s = Math.min(L, k * BED_STEP);
          const [x, z, i] = pointAt(s);
          bed[k] = dem3.minNear(toLat(z), toLon(x), Math.max(10, widthOf(ord[i]) / 2 + 4));
        }
        // one sample far under its neighbours (a pond's deep end, a pit
        // beside the line) would carry down the whole creek through the
        // running minimum below: none may sit more than 2.5 m under the
        // median of the samples within 100 m of it
        {
          const raw = Float64Array.from(bed);
          for (let k = 0; k < n; k++) {
            const win = Array.from(raw.subarray(Math.max(0, k - 5), Math.min(n, k + 6))).sort((a, b) => a - b);
            bed[k] = Math.max(raw[k], win[Math.floor(win.length / 2)] - 2.5);
          }
        }
        const q = Math.max(1, Math.floor(n / 10));
        let up = 0, dn = 0;
        for (let k = 0; k < q; k++) { up += bed[k]; dn += bed[n - 1 - k]; }
        if (up >= dn) { for (let k = 1; k < n; k++) bed[k] = Math.min(bed[k], bed[k - 1]); }
        else { for (let k = n - 2; k >= 0; k--) bed[k] = Math.min(bed[k], bed[k + 1]); }
        const bedAt = s => { const f = Math.min(n - 1, Math.max(0, s / BED_STEP)), k = Math.min(n - 2, Math.floor(f)); return bed[k] + (bed[k + 1] - bed[k]) * (f - k); };
        // PIECES: one width each, cut where the stream order changes for
        // 400 m or more, and at MAX_PTS points; neighbours share a vertex
        const cuts = [0];
        let cur = ord[0];
        for (let i = 1; i < pts.length - 1; i++) {
          if (ord[i] === cur) continue;
          let j = i; while (j < pts.length - 1 && ord[j] === ord[i]) j++;
          if (acc[j] - acc[i] >= 400 && acc[i] - acc[cuts[cuts.length - 1]] >= 400) { cuts.push(i); cur = ord[i]; }
          i = j - 1;
        }
        cuts.push(pts.length - 1);
        for (let c = 0; c + 1 < cuts.length; c++) {
          for (let a = cuts[c]; a < cuts[c + 1]; a += MAX_PTS - 1) {
            const b = Math.min(cuts[c + 1], a + MAX_PTS - 1);
            const piece = pts.slice(a, b + 1);
            if (piece.length < 2) continue;
            const s0 = acc[a], len = acc[b] - acc[a];
            const so = ord[Math.floor((a + b) / 2)];
            const rav = g.kind === KIND_RAVINE;
            const step = rav ? RAVINE_BED_STEP : BED_STEP;
            const m = Math.max(2, Math.ceil(len / step) + 1);
            const pb = new Float64Array(m);
            for (let k = 0; k < m; k++) pb[k] = bedAt(s0 + Math.min(len, k * step));
            waters.push({ name: g.name, widthM: rav ? 2 : widthOf(so), kind: g.kind, lake: false, ravine: rav, pts: piece, src: g.src, order: so, bed: pb, bedStep: step, bedLen: len });
            if (rav) { stats.ravines++; stats.ravineKm += len / 1000; stats.ravinePts += piece.length; }
            else { stats.creeks++; stats.creekKm += len / 1000; stats.creekPts += piece.length; }
            stats.beds += m;
          }
        }
      }
    }
  }

  // ---- lakes
  const countyLakes = [];
  const lakeCands = [];
  for (const f of W('meck_lakes').features) {
    const t = f.properties.feat_type || '';
    if (/wetland|quarry/i.test(t) || !t) continue;
    for (const p of polys(f.geometry)) countyLakes.push(p[0]);
    for (const p of polys(f.geometry)) lakeCands.push({ name: t === 'Pond' ? '' : t.replace(/^Catawba River \/ /, ''), ring: p[0], src: 'meck' });
  }
  for (const f of W('usgs_waterbodies').features) {
    if (f.properties.featuretypelabel !== 'Lake') continue;
    for (const p of polys(f.geometry)) {
      // judged by the part inside the DEM box: a big lake's centroid can lie
      // outside the county while the part the game shows is a county lake's
      const r = p[0];
      const inBox = clipRing(r.map(toG), gbox);
      if (inBox.length < 3) continue;
      let cx = 0, cz = 0; for (const c of inBox) { cx += c[0]; cz += c[1]; } cx /= inBox.length; cz /= inBox.length;
      const lon = toLon(cx), lat = toLat(cz);
      if (inCounty(lon, lat) || countyLakes.some(cr => pip(cr, lon, lat))) continue;
      lakeCands.push({ name: (f.properties.gnisidlabel || '').trim(), ring: r, src: 'usgs' });
    }
  }
  for (const c of lakeCands) {
    let ring = c.ring.map(toG);
    if (ring.length > 1 && ring[0][0] === ring[ring.length - 1][0] && ring[0][1] === ring[ring.length - 1][1]) ring = ring.slice(0, -1);
    ring = clipRing(ring, gbox);
    if (ring.length < 3 || ringArea(ring) < LAKE_MIN_M2) continue;
    // flat in the hydro-flattened DEM, or not a lake
    const vals = pixelsIn(dem3, c.ring).sort((a, b) => a - b);
    if (vals.length < 4) continue;
    const med = vals[Math.floor(vals.length / 2)];
    const flat = vals.filter(v => Math.abs(v - med) <= 0.3).length / vals.length;
    if (flat < 0.6) { stats.lakesNotFlat.push(`${c.src} ${c.name || '(pond)'} ${(ringArea(ring) / 1e4).toFixed(1)} ha flat ${flat.toFixed(2)}`); continue; }
    let tol = LAKE_SIMPLIFY_M, simp = dpRing(ring, tol);
    while (simp.length > MAX_PTS) { tol *= 1.5; simp = dpRing(ring, tol); }
    if (simp.length < 3) continue;
    waters.push({ name: c.name, widthM: 0, kind: KIND_LAKE, lake: true, ravine: false, pts: simp, src: c.src, level: med, bed: Float64Array.of(med), bedStep: 0, bedLen: 0 });
    stats.lakes++; stats.lakePts += simp.length;
  }
  log(`water: ${stats.creeks} creek lines (${stats.creekKm.toFixed(0)} km, ${stats.creekPts} points), ${stats.ravines} ravines (${stats.ravineKm.toFixed(0)} km, ${stats.ravinePts} points), ${stats.beds} bed samples (county ${stats.meckParts} parts, 3DHP ${stats.usgsParts}); ${stats.lakes} lakes (${stats.lakePts} points); ${stats.lakesNotFlat.length} water bodies left out as not flat`);
  return { waters, stats };
}

/// Douglas-Peucker on a closed ring (split at its two farthest-apart points).
function dpRing(ring, tol) {
  let far = 0, fi = 0;
  for (let i = 1; i < ring.length; i++) { const d = Math.hypot(ring[i][0] - ring[0][0], ring[i][1] - ring[0][1]); if (d > far) { far = d; fi = i; } }
  const a = dp(ring.slice(0, fi + 1), tol), b = dp(ring.slice(fi).concat([ring[0]]), tol);
  return a.concat(b.slice(1, -1));
}

/// Every 3DEP pixel whose centre is inside the (lon, lat) ring (scanline).
function pixelsIn(dem3, ring) {
  const { cols, rows, west, north, res_deg: res } = dem3.meta;
  let s = 90, n = -90; for (const p of ring) { s = Math.min(s, p[1]); n = Math.max(n, p[1]); }
  const r0 = Math.max(0, Math.ceil((north - n) / res - 0.5)), r1 = Math.min(rows - 1, Math.floor((north - s) / res - 0.5));
  const vals = [];
  const stride = Math.max(1, Math.floor((r1 - r0) / 400));
  for (let r = r0; r <= r1; r += stride) {
    const lat = north - (r + 0.5) * res, xs = [];
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const [xi, yi] = ring[i], [xj, yj] = ring[j];
      if ((yi > lat) !== (yj > lat)) xs.push(xi + (lat - yi) * (xj - xi) / (yj - yi));
    }
    xs.sort((a, b) => a - b);
    for (let k = 0; k + 1 < xs.length; k += 2) {
      const c0 = Math.max(0, Math.ceil((xs[k] - west) / res - 0.5)), c1 = Math.min(cols - 1, Math.floor((xs[k + 1] - west) / res - 0.5));
      for (let c = c0; c <= c1; c += stride) vals.push(dem3.pixel(c, r));
    }
  }
  return vals;
}
