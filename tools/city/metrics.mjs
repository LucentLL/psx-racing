// metrics.mjs - the Charlotte numbers every refinement package is judged by,
// measured offline (node, no Unity, under 30 s) on the data files the game
// ships. Plan section 2.4 (WP-01); it folds in the survey scripts
// kinks/census.mjs, roads/geom_stats.py and flat/crests2.py.
//
//   node tools/city/metrics.mjs                      print the report
//   node tools/city/metrics.mjs --json out.json      ...and write every number
//   node tools/city/metrics.mjs --data <dir>         measure an --out export instead of Resources
//   node tools/city/metrics.mjs --compare tools/city/baseline/metrics_baseline.json
//                                                    print what moved against a baseline
//   --fast                                           Brotli at quality 5 (SIZE is then not comparable)
//
// Sections:
//   GRAPH     counts, km by class, node degrees
//   KINKS     free-vertex turns (>=5/10/25 deg per km) by class, the largest,
//             inner-edge folds over 10 cm, split nodes of divided roads (C11)
//   LANES     painted centre turn lanes vs the tags, uneven lane splits,
//             lane tags present (needs the Overpass cache for the tags)
//   HEIGHT    the ground and the road line against USGS 3DEP (tools/city/truth):
//             city-wide DEM, the core's relief, 8 transects (RMSE, crests and
//             dips kept, climb, crest radius), the land beside the road
//   VERTICAL  the elevation solve emulated offline (lib/vertical.mjs; plan
//             B2): trench decisions, double separations, mixed decisions,
//             carriageway pairs at crossings, the AASHTO vertical-curve census
//             (the PROFILE audit's numbers on the emulation), and 3DEP's real
//             separation at the tall ones when the 1/3" cache is there.
//             --vertical-old adds the trench rule before B2, for a BEFORE
//   SMOOTH    the smoothness gate's B2 KINK / B3 CURVE on the exported
//             centreline and its offset curves (smooth.mjs; plan A4 WP-G)
//   CONTROL   signal / stop / give-way nodes by junction class
//   SIZE      raw and Brotli bytes per file and per charlotte_city section
//   ACCURACY  road position and width against county pavement data (C12) -
//             not measurable yet: the county layers are not fetched
//
// The height comparisons emulate only what can be emulated offline: "game
// DEM" is the shipped grid sampled on the line; "game road~" runs
// CityElevation.Solve's step 1 on it (10 m stations, Gaussian sigma 2.5
// stations, the class grade clamp) with no crossing facts, cones or seats.

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { gunzipSync, brotliCompressSync, constants as Z } from 'node:zlib';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, parseDem, parseBld, roadGridSteps, fingerprint, freeVertices, kmByClass, classOf, CLASSES, DEG, STATION_STEP, signedTurn } from './lib/citydata.mjs';
import { smoothSection } from './smooth.mjs';
import { emulateSolve, profileNumbers, realSeparations, SEPARATION_MAX } from './lib/vertical.mjs';
import { load3dep } from './lib/dem3dep.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const ARGS = process.argv.slice(2);
const argVal = n => { const i = ARGS.indexOf(n); return i >= 0 ? ARGS[i + 1] : null; };
const DATA = resolve(UNITY, argVal('--data') || 'Assets/PSXRacing/Resources');
const TRUTH = join(HERE, 'truth');
const CACHE = join(HERE, 'cache');
const FAST = ARGS.includes('--fast');

const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;
const M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const toX = lon => (lon - LON0) * M_LON, toZ = lat => (lat - LAT0) * M_LAT;
const toLat = z => LAT0 + z / M_LAT, toLon = x => LON0 + x / M_LON;
const ll = (x, z) => `${toLat(z).toFixed(5)},${toLon(x).toFixed(5)}`;

const t0 = Date.now();
const files = {
  city: readFileSync(join(DATA, 'charlotte_city.bytes')),
  dem: readFileSync(join(DATA, 'charlotte_dem.bytes')),
  bld: readFileSync(join(DATA, 'charlotte_bld.bytes')),
  routes: readFileSync(join(DATA, 'charlotte_routes.json')),
};
const city = parseCity(files.city), dem = parseDem(files.dem), bld = parseBld(files.bld);
/// THE ROADS' GROUND (WP-04): the solve reads the grid through a Gaussian of
/// CityElevation.RoadDemSigmaDefault cells (the land reads it raw). Read out of
/// the C# so the emulation of solver step 1 below can never drift from it.
const ROAD_SIGMA = (() => {
  // the same measuring override the game reads (PSX_CITY_ROADSIGMA)
  if (process.env.PSX_CITY_ROADSIGMA !== undefined) return +process.env.PSX_CITY_ROADSIGMA;
  try {
    const cs = readFileSync(join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Scripts', 'City', 'CityElevation.cs'), 'utf8');
    const m = /RoadDemSigmaDefault\s*=\s*([\d.]+)f/.exec(cs);
    return m ? +m[1] : 0;
  } catch { return 0; }
})();
const roadDem = (() => {
  if (!(ROAD_SIGMA > 0.05)) return dem;
  // the 60 m grid the game rebuilds from the 30 m nodes (WP-13), in metres
  const g = roadGridSteps(dem), mul = dem.scale === Math.fround(0.1) ? 0.1 : dem.scale;
  const { nx, nz } = g, r = Math.ceil(ROAD_SIGMA * 3), k = [];
  const src = Float64Array.from(g.steps, v => v * mul);
  let ks = 0; for (let i = -r; i <= r; i++) { const v = Math.exp(-0.5 * i * i / (ROAD_SIGMA * ROAD_SIGMA)); k.push(v); ks += v; }
  const tmp = new Float64Array(nx * nz), h = new Float32Array(nx * nz);
  for (let z = 0; z < nz; z++) for (let x = 0; x < nx; x++) { let a = 0; for (let i = -r; i <= r; i++) a += k[i + r] * src[z * nx + Math.min(nx - 1, Math.max(0, x + i))]; tmp[z * nx + x] = a / ks; }
  for (let z = 0; z < nz; z++) for (let x = 0; x < nx; x++) { let a = 0; for (let i = -r; i <= r; i++) a += k[i + r] * tmp[Math.min(nz - 1, Math.max(0, z + i)) * nx + x]; h[z * nx + x] = a / ks; }
  const at = (x, z) => {
    const fx = (x - dem.x0) / g.cell, fz = (z - dem.z0) / g.cell;
    const ix = Math.min(nx - 2, Math.max(0, Math.floor(fx))), iz = Math.min(nz - 2, Math.max(0, Math.floor(fz)));
    const tx = Math.min(1, Math.max(0, fx - ix)), tz = Math.min(1, Math.max(0, fz - iz));
    const a = h[iz * nx + ix], b = h[iz * nx + ix + 1], c = h[(iz + 1) * nx + ix], d = h[(iz + 1) * nx + ix + 1];
    return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
  };
  return { ...dem, nx, nz, cell: g.cell, h, at, asl: (x, z) => at(x, z) + dem.base };
})();
const R = { schema: 1, data: DATA.replace(/\\/g, '/').replace(UNITY.replace(/\\/g, '/') + '/', ''), node: process.version };
const out = [];
const P = s => out.push(s);
const r1 = v => Math.round(v * 10) / 10, r2 = v => Math.round(v * 100) / 100, r3 = v => Math.round(v * 1000) / 1000;

// ------------------------------------------------------------ numerics
function percentile(arr, p) {                       // numpy's default (linear)
  const a = Float64Array.from(arr).sort();
  if (!a.length) return NaN;
  const i = (p / 100) * (a.length - 1), lo = Math.floor(i), hi = Math.ceil(i);
  return a[lo] + (a[hi] - a[lo]) * (i - lo);
}
const mean = a => { let s = 0; for (const v of a) s += v; return s / a.length; };
const sd = a => { const m = mean(a); let s = 0; for (const v of a) s += (v - m) * (v - m); return Math.sqrt(s / a.length); };
/// scipy.ndimage.gaussian_filter1d, mode 'nearest', truncate 4.
function gauss1d(y, sigma) {
  const n = y.length, r = Math.floor(4 * sigma + 0.5);
  const k = new Float64Array(2 * r + 1); let ks = 0;
  for (let i = -r; i <= r; i++) { k[i + r] = Math.exp(-0.5 * (i / sigma) ** 2); ks += k[i + r]; }
  for (let i = 0; i < k.length; i++) k[i] /= ks;
  const o = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    let s = 0;
    for (let j = -r; j <= r; j++) s += k[j + r] * y[Math.min(n - 1, Math.max(0, i + j))];
    o[i] = s;
  }
  return o;
}
/// np.interp over the good samples (constant beyond the ends).
function fill(y, bad) {
  const o = Float64Array.from(y), ok = [];
  for (let i = 0; i < y.length; i++) if (!bad[i] && Number.isFinite(y[i])) ok.push(i);
  if (!ok.length) return o;
  let j = 0;
  for (let i = 0; i < y.length; i++) {
    if (!bad[i] && Number.isFinite(y[i])) continue;
    while (j + 1 < ok.length && ok[j + 1] < i) j++;
    if (i < ok[0]) o[i] = y[ok[0]];
    else if (i > ok[ok.length - 1]) o[i] = y[ok[ok.length - 1]];
    else { const a = ok[j], b = ok[j + 1]; o[i] = y[a] + (y[b] - y[a]) * (i - a) / (b - a); }
  }
  return o;
}
/// scipy.signal.find_peaks(x, prominence=prom, distance=dist): local maxima
/// (plateau midpoints), thinned by distance keeping the higher, then kept
/// when their prominence reaches prom. Returns [{i, prom}].
function findPeaks(x, prom, dist) {
  const n = x.length, pk = [];
  let i = 1;
  while (i < n - 1) {
    if (x[i - 1] < x[i]) {
      let ahead = i + 1;
      while (ahead < n - 1 && x[ahead] === x[i]) ahead++;
      if (x[ahead] < x[i]) { pk.push((i + ahead - 1) >> 1); i = ahead; }
    }
    i++;
  }
  // distance: highest first, drop neighbours within dist
  const keep = new Uint8Array(pk.length).fill(1);
  const order = pk.map((p, k) => k).sort((a, b) => x[pk[a]] - x[pk[b]]);
  for (let q = order.length - 1; q >= 0; q--) {
    const j = order[q];
    if (!keep[j]) continue;
    let k = j - 1;
    while (k >= 0 && pk[j] - pk[k] < dist) { keep[k] = 0; k--; }
    k = j + 1;
    while (k < pk.length && pk[k] - pk[j] < dist) { keep[k] = 0; k++; }
  }
  const res = [];
  for (let k = 0; k < pk.length; k++) {
    if (!keep[k]) continue;
    const p = pk[k];
    let lmin = x[p], j = p;
    while (j >= 0 && x[j] <= x[p]) { if (x[j] < lmin) lmin = x[j]; j--; }
    let rmin = x[p]; j = p;
    while (j < n && x[j] <= x[p]) { if (x[j] < rmin) rmin = x[j]; j++; }
    const pr = x[p] - Math.max(lmin, rmin);
    if (pr >= prom) res.push({ i: p, prom: pr });
  }
  return res;
}
const STEP = 2.0;
const g = (y, sigM) => gauss1d(y, sigM / STEP);
/// flat/metrics.py compare(): truth vs estimate on 2 m stations.
function compare(truth, est, bad) {
  const n = truth.length, ok = new Uint8Array(n);
  for (let i = 0; i < n; i++) ok[i] = !bad[i] && Number.isFinite(truth[i]) && Number.isFinite(est[i]) ? 1 : 0;
  const tf = fill(truth, bad), ef = fill(est, bad);
  const t5 = g(tf, 5), e5 = g(ef, 5);
  const d = []; for (let i = 0; i < n; i++) if (ok[i]) d.push(est[i] - truth[i]);
  const r = { bias: mean(d), rmse: Math.sqrt(mean(d.map(v => v * v))) };
  // grade over 20 m where the whole baseline is measured
  const k = 5, gt = new Float64Array(n).fill(NaN), ge = new Float64Array(n).fill(NaN);
  for (let i = k; i < n - k; i++) { gt[i] = (t5[i + k] - t5[i - k]) / 20; ge[i] = (e5[i + k] - e5[i - k]) / 20; }
  const okg = new Uint8Array(n);
  for (let i = 0; i < n; i++) { let c = 0; for (let j = i - 5; j <= i + 5; j++) if (j >= 0 && j < n && ok[j]) c++; okg[i] = c > 10.5 ? 1 : 0; }
  const gta = [], gea = [], gd = [];
  for (let i = 0; i < n; i++) if (okg[i] && Number.isFinite(gt[i]) && Number.isFinite(ge[i])) { gta.push(Math.abs(gt[i])); gea.push(Math.abs(ge[i])); gd.push(ge[i] - gt[i]); }
  r.grade_rmse_pp = Math.sqrt(mean(gd.map(v => v * v))) * 100;
  r.grade_p95_real = percentile(gta, 95) * 100; r.grade_p95 = percentile(gea, 95) * 100;
  // band-pass relief: 5 m .. 100 m sigma
  const gt100 = g(tf, 100), ge100 = g(ef, 100);
  const ht = new Float64Array(n), he = new Float64Array(n);
  for (let i = 0; i < n; i++) { ht[i] = t5[i] - gt100[i]; he[i] = e5[i] - ge100[i]; }
  const hto = [], heo = [], hd = [];
  for (let i = 0; i < n; i++) if (ok[i]) { hto.push(ht[i]); heo.push(he[i]); hd.push(he[i] - ht[i]); }
  r.relief_kept = sd(heo) / sd(hto);
  r.relief_fidelity = 1 - Math.sqrt(mean(hd.map(v => v * v))) / sd(hto);
  // climb: total |dh| on 10 m steps of the 5 m-smoothed profile
  let ct = 0, ce = 0, L = 0;
  for (let i = 0; i < n; i++) if (ok[i]) L++;
  for (let i = 5; i < n; i += 5) if (ok[i] && ok[i - 5]) { ct += Math.abs(t5[i] - t5[i - 5]); ce += Math.abs(e5[i] - e5[i - 5]); }
  r.km = L * STEP / 1000;
  r.climb_real_m_per_km = ct / r.km; r.climb_kept = ce / ct;
  // crests and dips >= 1 m in the band-pass truth, kept if the estimate has
  // one of at least half the prominence within 40 m
  let kept = 0, tot = 0;
  for (const sign of [1, -1]) {
    const T = findPeaks(ht.map(v => sign * v), 1.0, 20);
    const E = findPeaks(he.map(v => sign * v), 0.5, 20);
    for (const p of T) {
      if (!ok[p.i]) continue;
      tot++;
      let best = -Infinity;
      for (const q of E) if (Math.abs(q.i - p.i) <= 20 && q.prom > best) best = q.prom;
      if (best >= 0.5 * p.prom) kept++;
    }
  }
  r.features_real = tot; r.features_kept = kept;
  return r;
}
/// flat/crests2.py: crest curvature on an 8 m-smoothed profile over +-14 m.
function crestR(y, bad) {
  const n = y.length, ys = g(fill(y, bad), 8), k = 7, H = 14;
  const okc = new Uint8Array(n);
  for (let i = 0; i < n; i++) { let c = 0; for (let j = i - 15; j <= i + 15; j++) if (j >= 0 && j < n && !bad[j]) c++; okc[i] = c > 30.5 ? 1 : 0; }
  const kk = [];
  for (let i = k; i < n - k; i++) { const c = (ys[i + k] - 2 * ys[i] + ys[i - k]) / (H * H); if (okc[i] && c < 0) kk.push(-c); }
  if (kk.length < 10) return null;
  return { r_min: 1 / Math.max(...kk), r_p99: 1 / percentile(kk, 99) };
}
/// flat/profiles.py game_road(): CityElevation.Solve step 1 on one long line.
function gameRoad(y, s, grade) {
  const st = []; for (let v = 0; v < s[s.length - 1]; v += 10) st.push(v);
  const interp = (xs, X, Y) => { const o = new Float64Array(xs.length); let j = 0;
    for (let i = 0; i < xs.length; i++) { const x = xs[i];
      if (x <= X[0]) { o[i] = Y[0]; continue; } if (x >= X[X.length - 1]) { o[i] = Y[Y.length - 1]; continue; }
      while (X[j + 1] < x) j++; o[i] = Y[j] + (Y[j + 1] - Y[j]) * (x - X[j]) / (X[j + 1] - X[j]); } return o; };
  let v = gauss1d(interp(st, s, y), 2.5);
  for (let i = 1; i < v.length; i++) v[i] = Math.min(v[i - 1] + grade * 10, Math.max(v[i - 1] - grade * 10, v[i]));
  for (let i = v.length - 2; i >= 0; i--) v[i] = Math.min(v[i + 1] + grade * 10, Math.max(v[i + 1] - grade * 10, v[i]));
  return interp(s, st, v);
}
/// PTRU grid (tools/city/truth/*.ptru.gz, see make_truth notes in transects_meta.json)
function readPtru(path) {
  const b = gunzipSync(readFileSync(path));
  if (b.toString('latin1', 0, 4) !== 'PTRU') throw new Error(path + ': not PTRU');
  const nx = b.readUInt32LE(8), nz = b.readUInt32LE(12);
  const x0 = b.readFloatLE(16), z0 = b.readFloatLE(20), cell = b.readFloatLE(24), datum = b.readFloatLE(28), scale = b.readFloatLE(32);
  const h = new Float64Array(nx * nz);
  let p = 36;
  for (let iz = 0; iz < nz; iz++) { let v = 0; for (let ix = 0; ix < nx; ix++) { v = (v + b.readUInt16LE(p)) & 0xffff; p += 2; h[iz * nx + ix] = datum + v * scale; } }
  return { nx, nz, x0, z0, cell, h };
}
/// scipy gaussian_filter, mode 'reflect', truncate 4, on an nz x nx grid.
function gauss2d(h, nx, nz, sigma) {
  const r = Math.floor(4 * sigma + 0.5), k = new Float64Array(2 * r + 1); let ks = 0;
  for (let i = -r; i <= r; i++) { k[i + r] = Math.exp(-0.5 * (i / sigma) ** 2); ks += k[i + r]; }
  for (let i = 0; i < k.length; i++) k[i] /= ks;
  const refl = (i, n) => { while (i < 0 || i >= n) i = i < 0 ? -i - 1 : 2 * n - i - 1; return i; };
  const tmp = new Float64Array(h.length), o = new Float64Array(h.length);
  for (let z = 0; z < nz; z++) for (let x = 0; x < nx; x++) { let s = 0; for (let j = -r; j <= r; j++) s += k[j + r] * h[z * nx + refl(x + j, nx)]; tmp[z * nx + x] = s; }
  for (let z = 0; z < nz; z++) for (let x = 0; x < nx; x++) { let s = 0; for (let j = -r; j <= r; j++) s += k[j + r] * tmp[refl(z + j, nz) * nx + x]; o[z * nx + x] = s; }
  return o;
}

// ================================================================ GRAPH
const fp = fingerprint(city, dem, bld);
const km = kmByClass(city);
R.graph = { ...fp.city, km_by_class: Object.fromEntries(Object.entries(km).map(([k, v]) => [k, r1(v)])) };
delete R.graph.kinks; delete R.graph.kinks_per_km;
R.dem = fp.dem; R.bld = fp.bld;
P('GRAPH');
P(`  ${fp.city.nodes} nodes, ${fp.city.edges} edges, ${fp.city.points} points, ${fp.city.km} km; ${fp.city.crossings} grade separations (${fp.city.crossings_forced} by tag), ${fp.city.water_spans} water spans, ${fp.city.bridges} bridge edges, ${fp.city.waters} waters`);
P(`  node degrees ${fp.city.node_degrees.map((n, i) => `${i}:${n}`).join(' ')}`);
P('  km by class: ' + CLASSES.filter(c => km[c] > 0).map(c => `${c} ${km[c].toFixed(1)}`).join(', '));
P(`  routes: ${fp.city.routes.map(r => `${r.id} ${(r.length_m / 1000).toFixed(2)} km / ${r.edges} edges`).join(', ')}`);
P(`  DEM ${fp.dem.nx} x ${fp.dem.nz} at ${fp.dem.cell} m, datum ${fp.dem.datum_m} m, ${fp.dem.min_asl}..${fp.dem.max_asl} m ASL; ${fp.bld.footprints} footprints`);
P(`  PSXC v${fp.city.version}, graph hash ${fp.city.graph_hash}; PDEM v${fp.dem.version}, step ${fp.dem.scale_m} m`);

// ================================================================ KINKS
{
  const fv = freeVertices(city);
  const K = {};
  let totKm = 0;
  for (const c of CLASSES) {
    const V = fv.filter(v => v.cls === c);
    const kmc = city.edges.filter(e => classOf(e) === c && !e.roundabout).reduce((a, e) => a + e.length / 1000, 0);
    totKm += kmc;
    if (!V.length && kmc === 0) continue;
    const cnt = t => V.filter(v => v.deg >= t).length;
    let mx = null; for (const v of V) if (!mx || v.deg > mx.deg) mx = v;
    K[c] = {
      km: r1(kmc), shape_vertices: V.filter(v => v.kind === 'shape').length, joins: V.filter(v => v.kind === 'join').length,
      ge5: cnt(5), ge10: cnt(10), ge25: cnt(25),
      ge5_per_km: r2(cnt(5) / kmc), ge10_per_km: r2(cnt(10) / kmc), ge25_per_km: r2(cnt(25) / kmc),
      ge10_short_leg: V.filter(v => v.deg >= 10 && Math.min(v.legIn, v.legOut) < 10).length,
      folds_over_10cm: V.filter(v => v.fold > 0.1).length, folds_over_50cm: V.filter(v => v.fold > 0.5).length,
      max_deg: mx ? r1(mx.deg) : 0, max_at: mx ? ll(mx.x, mx.z) : null, max_edge: mx ? mx.edge : null,
    };
  }
  const all = fv;
  const cntA = t => all.filter(v => v.deg >= t).length;
  let mx = null; for (const v of all) if (!mx || v.deg > mx.deg) mx = v;
  // the largest free-vertex kink on the roads a car drives fast (arterials
  // and up, not ramps): what the lines phase exists to remove
  let mxMajor = null; for (const v of all) if (/^(motorway|trunk|primary|secondary)$/.test(v.cls) && (!mxMajor || v.deg > mxMajor.deg)) mxMajor = v;
  let mxShape = null; for (const v of all) if (v.kind === 'shape' && /^(motorway|trunk|primary|secondary)$/.test(v.cls) && (!mxShape || v.deg > mxShape.deg)) mxShape = v;
  // split nodes (plan critic C11): a two-way road that divides into two
  // one-way carriageways of the same name, one in, one out
  let splits = 0, splitFold = 0, splitWorst = null;
  for (let ni = 0; ni < city.nodes.length; ni++) {
    const inc = city.nodeEdges[ni];
    if (inc.length !== 3) continue;
    const E = inc.map(i => city.edges[i]);
    const tw = E.filter(e => !e.oneway), ow = E.filter(e => e.oneway);
    if (tw.length !== 1 || ow.length !== 2 || !tw[0].name || E.some(e => e.name !== tw[0].name || e.link)) continue;
    const into = ow.filter(e => e.b === ni).length, outOf = ow.filter(e => e.a === ni).length;
    if (into !== 1 || outOf !== 1) continue;
    splits++;
    const t = tw[0], tp = t.a === ni ? t.pts[1] : t.pts[t.pts.length - 2];
    const node = [city.nodes[ni].x, city.nodes[ni].z];
    let worst = 0, fold = 0;
    for (const o of ow) {
      const op = o.a === ni ? o.pts[1] : o.pts[o.pts.length - 2];
      // the turn a car makes from the two-way road into this carriageway
      // (0 = straight on); tp -> node -> op whichever way the carriageway runs
      const turn = Math.abs(signedTurn(tp, node, op)) * DEG;
      const leg = Math.min(Math.hypot(op[0] - node[0], op[1] - node[1]), Math.hypot(tp[0] - node[0], tp[1] - node[1]));
      const f = Math.max(t.hw, o.hw) * Math.sin(turn / DEG / 2) - leg;
      if (f > fold) { fold = f; worst = turn; }
    }
    if (fold > 0.1) { splitFold++; if (!splitWorst || fold > splitWorst.fold) splitWorst = { fold: r2(fold), turn_deg: r1(worst), at: ll(node[0], node[1]), name: t.name }; }
  }
  R.kinks = {
    km: r1(totKm), free_vertices: all.length,
    ge5_per_km: r3(cntA(5) / totKm), ge10_per_km: r3(cntA(10) / totKm), ge25_per_km: r3(cntA(25) / totKm),
    ge5: cntA(5), ge10: cntA(10), ge25: cntA(25),
    folds_over_10cm: all.filter(v => v.fold > 0.1).length, folds_over_50cm: all.filter(v => v.fold > 0.5).length,
    max_free_kink: mx ? { deg: r1(mx.deg), cls: mx.cls, at: ll(mx.x, mx.z), edge: mx.edge, kind: mx.kind } : null,
    max_free_kink_major: mxMajor ? { deg: r1(mxMajor.deg), cls: mxMajor.cls, at: ll(mxMajor.x, mxMajor.z), edge: mxMajor.edge, kind: mxMajor.kind } : null,
    max_shape_kink_major: mxShape ? { deg: r1(mxShape.deg), cls: mxShape.cls, at: ll(mxShape.x, mxShape.z), edge: mxShape.edge } : null,
    split_nodes: splits, split_nodes_fold_over_10cm: splitFold, split_worst: splitWorst,
    by_class: K,
  };
  P('\nKINKS (free vertices: interior polyline points + 2-arm joins; roundabouts out)');
  P(`  all: ${R.kinks.ge5_per_km} >=5 deg/km, ${R.kinks.ge10_per_km} >=10, ${R.kinks.ge25_per_km} >=25 over ${R.kinks.km} km; folds >10 cm ${R.kinks.folds_over_10cm}, >50 cm ${R.kinks.folds_over_50cm}`);
  P(`  largest free kink ${R.kinks.max_free_kink.deg} deg (${R.kinks.max_free_kink.cls}, ${R.kinks.max_free_kink.kind}) at ${R.kinks.max_free_kink.at}; on motorway..secondary ${R.kinks.max_free_kink_major.deg} deg (${R.kinks.max_free_kink_major.cls}) at ${R.kinks.max_free_kink_major.at}`);
  P(`  largest interior (shape) vertex kink on motorway..secondary ${R.kinks.max_shape_kink_major.deg} deg (${R.kinks.max_shape_kink_major.cls}) at ${R.kinks.max_shape_kink_major.at}`);
  P(`  split nodes (two-way -> two carriageways, same name): ${splits}, ${splitFold} fold > 10 cm${splitWorst ? `; worst ${splitWorst.fold} m, ${splitWorst.turn_deg} deg, ${splitWorst.name} at ${splitWorst.at}` : ''}`);
  P('  class            km     >=5/km >=10/km >=25/km  >=10 short-leg  folds>10cm  max deg');
  for (const [c, k] of Object.entries(K))
    P(`  ${c.padEnd(15)} ${String(k.km).padStart(6)}  ${String(k.ge5_per_km).padStart(6)}  ${String(k.ge10_per_km).padStart(6)}  ${String(k.ge25_per_km).padStart(6)}  ${String(k.ge10_short_leg).padStart(14)}  ${String(k.folds_over_10cm).padStart(10)}  ${String(k.max_deg).padStart(7)}`);
}

// ================================================================ SMOOTH
{ const sm = smoothSection(city, join(UNITY, 'Assets/PSXRacing/Editor/SmoothRules.cs')); R.smooth = sm.json; for (const l of sm.lines) P(l); }

// ================================================================ LANES
{
  const tw = city.edges.filter(e => !e.oneway);
  const painted = tw.filter(e => e.profile.key.endsWith('t'));
  const kmOf = l => r1(l.reduce((a, e) => a + e.length / 1000, 0));
  R.lanes = { painted_twltl_km: kmOf(painted), painted_twltl_edges: painted.length, width_basis: '12 ft (3.6576 m) every lane, RoadProfiles' };
  const byProf = {}; for (const e of city.edges) byProf[e.profile.key] = (byProf[e.profile.key] || 0) + e.length / 1000;
  R.lanes.km_by_profile = Object.fromEntries(Object.entries(byProf).sort().map(([k, v]) => [k, r1(v)]));
  const cacheFiles = ['ways_all.json', 'streets_core.json'].map(f => join(CACHE, f));
  if (cacheFiles.every(existsSync)) {
    const tags = new Map();
    for (const f of cacheFiles) for (const el of JSON.parse(readFileSync(f, 'utf8')).elements) if (el.type === 'way' && el.tags) tags.set(el.id, el.tags);
    const has = (t, k) => t[k] !== undefined && t[k] !== '';
    const tg = e => tags.get(e.wayId) || {};
    const evidence = e => { const t = tg(e); return (parseInt(t['lanes:both_ways'], 10) || 0) > 0 || has(t, 'turn:lanes:both_ways') || has(t, 'centre_turn_lane'); };
    const fb = e => { const t = tg(e); const lf = parseInt(t['lanes:forward'], 10), lb = parseInt(t['lanes:backward'], 10); return [lf, lb]; };
    const withEv = painted.filter(evidence);
    const contradicted = painted.filter(e => { if (evidence(e)) return false; const [lf, lb] = fb(e); const L = parseInt(tg(e).lanes, 10); return Number.isFinite(lf) && Number.isFinite(lb) && lf + lb === L; });
    const uneven = tw.filter(e => { const [lf, lb] = fb(e); return Number.isFinite(lf) && Number.isFinite(lb) && lf !== lb; });
    const tagged = city.edges.filter(e => has(tg(e), 'lanes'));
    const turnLanes = city.edges.filter(e => { const t = tg(e); return has(t, 'turn:lanes') || has(t, 'turn:lanes:forward') || has(t, 'turn:lanes:backward'); });
    Object.assign(R.lanes, {
      twltl_tagged_km: kmOf(tw.filter(evidence)), painted_twltl_with_tag_km: kmOf(withEv),
      painted_twltl_contradicted_km: kmOf(contradicted), painted_twltl_contradicted_edges: contradicted.length,
      uneven_split_km: kmOf(uneven), uneven_split_edges: uneven.length,
      lanes_tagged_share_km: r3(kmOf(tagged) / kmOf(city.edges)), turn_lanes_tagged_km: kmOf(turnLanes),
    });
  } else R.lanes.note = 'tools/city/cache is absent: tag-based lane numbers not measured';
  P('\nLANES');
  P(`  centre turn lane painted on ${R.lanes.painted_twltl_km} km (${R.lanes.painted_twltl_edges} edges); tagged as one (lanes:both_ways / turn:lanes:both_ways) on ${R.lanes.twltl_tagged_km} km; painted with no tag and forward+backward = lanes (a turn pocket painted as a TWLTL) ${R.lanes.painted_twltl_contradicted_km} km`);
  P(`  uneven splits (lanes:forward != lanes:backward) ${R.lanes.uneven_split_km} km on ${R.lanes.uneven_split_edges} edges, drawn symmetric; lanes tagged on ${(R.lanes.lanes_tagged_share_km * 100).toFixed(0)}% of km; turn:lanes on ${R.lanes.turn_lanes_tagged_km} km (ignored)`);
  P(`  width basis: ${R.lanes.width_basis}`);
}

// ================================================================ HEIGHT
{
  R.height = {};
  P('\nHEIGHT (against USGS 3DEP, tools/city/truth; the shipped DEM, not the solved roads)');
  // --- the spot check
  const meta = JSON.parse(readFileSync(join(TRUTH, 'transects_meta.json'), 'utf8'));
  const tt = meta.spot_checks[0];
  const tx = toX(tt.lon), tz = toZ(tt.lat), gy = dem.asl(tx, tz);
  R.height.trade_tryon = { game_dem_asl: r2(gy), truth_asl: tt.truth_m, error: r2(gy - tt.truth_m) };
  P(`  Trade & Tryon: game DEM ${gy.toFixed(2)} m ASL, 3DEP/EPQS ${tt.truth_m} m (${(gy - tt.truth_m).toFixed(2)} m)`);
  // --- city-wide, at 120 m nodes of the 3DEP 60 m block means
  {
    const C = readPtru(join(TRUTH, 'city120.ptru.gz'));
    const d = [], hv = [], tv = [];
    const margin = 13;                                        // the exporter's 1.5 km margin
    const game = new Float64Array(C.h.length);
    for (let iz = 0; iz < C.nz; iz++) for (let ix = 0; ix < C.nx; ix++) game[iz * C.nx + ix] = dem.asl(C.x0 + ix * C.cell, C.z0 + iz * C.cell);
    for (let iz = margin; iz < C.nz - margin; iz++) for (let ix = margin; ix < C.nx - margin; ix++) { const i = iz * C.nx + ix; d.push(game[i] - C.h[i]); }
    // topographic position on the truth (height above the ~1 km mean)
    const box = (h, r) => { const o = new Float64Array(h.length);
      for (let iz = 0; iz < C.nz; iz++) for (let ix = 0; ix < C.nx; ix++) { let s = 0, n = 0;
        for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) { const x = Math.min(C.nx - 1, Math.max(0, ix + dx)), z = Math.min(C.nz - 1, Math.max(0, iz + dz)); s += h[z * C.nx + x]; n++; }
        o[iz * C.nx + ix] = s / n; } return o; };
    const m1k = box(C.h, 4);
    const cls = { deep_valley: [], valley: [], mid: [], ridge: [], high_ridge: [] };
    const relief = (h) => { const o = []; for (let iz = margin; iz < C.nz - margin; iz++) for (let ix = margin; ix < C.nx - margin; ix++) {
      let lo = Infinity, hi = -Infinity; for (let dz = -2; dz <= 2; dz++) for (let dx = -2; dx <= 2; dx++) { const v = h[(iz + dz) * C.nx + ix + dx]; if (v < lo) lo = v; if (v > hi) hi = v; } o.push(hi - lo); } return o; };
    const slope = (h) => { const o = []; for (let iz = margin; iz < C.nz - margin; iz++) for (let ix = margin; ix < C.nx - margin; ix++) {
      const i = iz * C.nx + ix; o.push(Math.hypot(h[i + 1] - h[i], h[i + C.nx] - h[i]) / C.cell * 100); } return o; };
    for (let iz = margin; iz < C.nz - margin; iz++) for (let ix = margin; ix < C.nx - margin; ix++) {
      const i = iz * C.nx + ix, tpi = C.h[i] - m1k[i], e = game[i] - C.h[i];
      (tpi < -8 ? cls.deep_valley : tpi < -4 ? cls.valley : tpi < 4 ? cls.mid : tpi < 8 ? cls.ridge : cls.high_ridge).push(e);
    }
    const rt = relief(C.h), rg = relief(game), st = slope(C.h), sg = slope(game);
    R.height.city = {
      grid: '3DEP 60 m block means at every 2nd game DEM node (120 m); the 1.5 km margin dropped',
      bias: r2(mean(d)), rmse: r2(Math.sqrt(mean(d.map(v => v * v)))), p1: r1(percentile(d, 1)), p99: r1(percentile(d, 99)),
      bias_by_position: Object.fromEntries(Object.entries(cls).map(([k, v]) => [k, { share: r3(v.length / d.length), bias: r2(mean(v)) }])),
      relief600_p50: { truth: r1(percentile(rt, 50)), game: r1(percentile(rg, 50)) }, relief600_p90: { truth: r1(percentile(rt, 90)), game: r1(percentile(rg, 90)) },
      slope120_p90_pct: { truth: r2(percentile(st, 90)), game: r2(percentile(sg, 90)) },
    };
    const c = R.height.city;
    P(`  city-wide DEM vs 3DEP: bias ${c.bias} m, RMSE ${c.rmse} m (p1 ${c.p1}, p99 ${c.p99}); high ridges ${c.bias_by_position.high_ridge.bias} m, deep valleys ${c.bias_by_position.deep_valley.bias >= 0 ? '+' : ''}${c.bias_by_position.deep_valley.bias} m`);
    P(`    relief in 600 m p50 ${c.relief600_p50.game} m (real ${c.relief600_p50.truth}), p90 ${c.relief600_p90.game} (real ${c.relief600_p90.truth}); slope p90 at 120 m ${c.slope120_p90_pct.game}% (real ${c.slope120_p90_pct.truth}%)`);
  }
  // --- the game grid's own slope at 60 m, the plan's WP-04 targets (slope p90
  // >= 8%, cells over 6% >= 15%), measured as survey_flatness did (flat/city.py):
  // forward differences over one cell, the 1.5 km margin (25 cells) dropped.
  // 3DEP block means at 60 m give p90 9.02% and 28.6% over 6% (the survey).
  {
    const m = Math.round(1500 / dem.cell), sl = [];
    for (let iz = m; iz < dem.nz - m - 1; iz++) for (let ix = m; ix < dem.nx - m - 1; ix++) {
      const i = iz * dem.nx + ix;
      sl.push(Math.hypot(dem.h[i + 1] - dem.h[i], dem.h[i + dem.nx] - dem.h[i]) / dem.cell * 100);
    }
    R.height.grid60 = { method: `survey_flatness: forward differences over one ${dem.cell} m cell (the shipped grid's), the 1.5 km margin dropped`, cell_m: dem.cell,
                        slope_p50_pct: r2(percentile(sl, 50)), slope_p90_pct: r2(percentile(sl, 90)), slope_p99_pct: r2(percentile(sl, 99)),
                        share_over_6pct: r3(sl.filter(v => v > 6).length / sl.length), real_3dep_60m: { slope_p90_pct: 9.02, share_over_6pct: 0.286 } };
    const g = R.height.grid60;
    P(`  game grid slope at ${dem.cell} m: p50 ${g.slope_p50_pct}%, p90 ${g.slope_p90_pct}%, p99 ${g.slope_p99_pct}%; cells over 6%: ${(g.share_over_6pct * 100).toFixed(1)}% (3DEP at 60 m: p90 9.02%, 28.6%)`);
  }
  // --- the core's relief, against 3DEP 10 m
  {
    const C = readPtru(join(TRUTH, 'core10.ptru.gz'));
    const game = new Float64Array(C.h.length);
    for (let iz = 0; iz < C.nz; iz++) for (let ix = 0; ix < C.nx; ix++) game[iz * C.nx + ix] = dem.asl(C.x0 + ix * C.cell, C.z0 + iz * C.cell);
    const sig = 100 / C.cell, inset = 200 / C.cell;
    const gt = gauss2d(C.h, C.nx, C.nz, sig), ge = gauss2d(game, C.nx, C.nz, sig);
    const ht = [], he = [], dd = [], hd = [];
    for (let iz = inset; iz < C.nz - inset; iz++) for (let ix = inset; ix < C.nx - inset; ix++) {
      const i = iz * C.nx + ix, a = C.h[i] - gt[i], b = game[i] - ge[i];
      ht.push(a); he.push(b); hd.push(b - a); dd.push(game[i] - C.h[i]);
    }
    const slope = h => { const o = []; for (let iz = inset; iz < C.nz - inset; iz++) for (let ix = inset; ix < C.nx - inset; ix++) { const i = iz * C.nx + ix; o.push(Math.hypot(h[i + 1] - h[i], h[i + C.nx] - h[i]) / C.cell * 100); } return o; };
    const st = slope(C.h), sg = slope(game);
    R.height.core = {
      grid: '3DEP 10 m over the 8 x 8 km core, 200 m inset; relief = height minus its 100 m Gaussian',
      rmse: r2(Math.sqrt(mean(dd.map(v => v * v)))), bias: r2(mean(dd)),
      relief_kept: r2(sd(he) / sd(ht)), relief_fidelity: r2(1 - Math.sqrt(mean(hd.map(v => v * v))) / sd(ht)), relief_sd_truth: r2(sd(ht)),
      slope10_p90_pct: { truth: r1(percentile(st, 90)), game: r1(percentile(sg, 90)) },
      share_over_8pct: { truth: r3(st.filter(v => v > 8).length / st.length), game: r3(sg.filter(v => v > 8).length / sg.length) },
    };
    const c = R.height.core;
    P(`  core relief kept ${c.relief_kept} (fidelity ${c.relief_fidelity}), RMSE ${c.rmse} m, bias ${c.bias} m; slope p90 ${c.slope10_p90_pct.game}% (real ${c.slope10_p90_pct.truth}%), over 8%: ${c.share_over_8pct.game} (real ${c.share_over_8pct.truth})`);
  }
  // --- the 8 transects
  {
    // CRLF-safe: git's autocrlf checks these out with \r\n on Windows
    const csv = f => readFileSync(join(TRUTH, f), 'utf8').trim().split(/\r?\n/).slice(1).map(l => l.split(','));
    const rows = csv('transects.csv'), side = csv('roadside.csv');
    const T = new Map();
    for (const m of meta.transects) T.set(m.id, { ...m, s: [], x: [], z: [], truth: [], bad: [], side: [] });
    for (const r of rows) { const t = T.get(r[0]); t.s.push(+r[1]); t.x.push(+r[2]); t.z.push(+r[3]); t.truth.push(r[4] === '' ? NaN : +r[4]); t.bad.push(r[5] !== '0'); }
    for (const r of side) T.get(r[0]).side.push({ i: +r[1], nx: +r[2], nz: +r[3], off: { '-60': +r[4] || NaN, '-30': +r[5] || NaN, '30': +r[6] || NaN, '60': +r[7] || NaN } });
    const per = {}, agg = { dem: [], road: [] };
    const land = { 30: { t: [], g: [] }, 60: { t: [], g: [] } };
    for (const t of T.values()) {
      const game = t.x.map((x, i) => dem.asl(x, t.z[i]));
      const road = gameRoad(t.x.map((x, i) => roadDem.asl(x, t.z[i])), t.s, t.grade);
      const a = compare(t.truth, game, t.bad), b = compare(t.truth, road, t.bad);
      agg.dem.push(a); agg.road.push(b);
      const cr = crestR(road, t.bad), crt = crestR(t.truth, t.bad);
      // land beside the road: |land - road| at 30 and 60 m, both sides
      const roadT = g(fill(t.truth, t.bad), 5);
      for (const sd_ of t.side) {
        if (t.bad[sd_.i]) continue;
        for (const o of [30, 60]) for (const sgn of [-1, 1]) {
          const tv = sd_.off[String(sgn * o)];
          const gx = t.x[sd_.i] + sd_.nx * sgn * o, gz = t.z[sd_.i] + sd_.nz * sgn * o;
          if (Number.isFinite(tv)) { land[o].t.push(Math.abs(tv - roadT[sd_.i])); land[o].g.push(Math.abs(dem.asl(gx, gz) - road[sd_.i])); }
        }
      }
      per[t.id] = { km_measured: r2(a.km), dem: { rmse: r2(a.rmse), bias: r2(a.bias), fidelity: r2(a.relief_fidelity), climb_kept: r2(a.climb_kept), features: `${a.features_kept}/${a.features_real}` },
                    road: { rmse: r2(b.rmse), bias: r2(b.bias), grade_p95: r1(b.grade_p95), grade_p95_real: r1(b.grade_p95_real), fidelity: r2(b.relief_fidelity), climb_kept: r2(b.climb_kept), features: `${b.features_kept}/${b.features_real}`,
                            crest_r_min: cr ? Math.round(cr.r_min) : null, crest_r_min_real: crt ? Math.round(crt.r_min) : null } };
    }
    const all = arr => { const w = arr.map(r => r.km), W = w.reduce((x, y) => x + y, 0);
      const wm = k => arr.reduce((s, r, i) => s + r[k] * w[i], 0) / W;
      const ft = arr.reduce((s, r) => s + r.features_real, 0), fk = arr.reduce((s, r) => s + r.features_kept, 0);
      return { km: r1(W), rmse: r2(Math.sqrt(arr.reduce((s, r, i) => s + r.rmse * r.rmse * w[i], 0) / W)), grade_rmse_pp: r2(wm('grade_rmse_pp')),
               grade_p95: r1(wm('grade_p95')), grade_p95_real: r1(wm('grade_p95_real')), relief_kept: r2(wm('relief_kept')), fidelity: r2(wm('relief_fidelity')),
               climb_kept: r2(wm('climb_kept')), features_kept: fk, features_real: ft, features_kept_share: r3(fk / ft) }; };
    const lp = o => ({ p50_real: r2(percentile(land[o].t, 50)), p90_real: r2(percentile(land[o].t, 90)), share_over_2m_real: r3(land[o].t.filter(v => v > 2).length / land[o].t.length),
                       p50_game: r2(percentile(land[o].g, 50)), p90_game: r2(percentile(land[o].g, 90)), share_over_2m_game: r3(land[o].g.filter(v => v > 2).length / land[o].g.length) });
    R.height.transects = { all_dem: all(agg.dem), all_road: all(agg.road), per_transect: per,
                           roadside: { note: '|land - road| both sides at every 5th station; game land = the shipped DEM (the tiles pin it nearer the road), game road = solver step 1 emulated', at_30m: lp(30), at_60m: lp(60) } };
    const A = R.height.transects.all_dem, B = R.height.transects.all_road;
    P(`  transects (${A.km} km measured of 51.8): game DEM on the road line RMSE ${A.rmse} m, fidelity ${A.fidelity}, climb kept ${A.climb_kept}, crests+dips kept ${A.features_kept}/${A.features_real} (${(A.features_kept_share * 100).toFixed(0)}%)`);
    P(`    game road~ (solver step 1, the grid through a ${ROAD_SIGMA}-cell Gaussian): RMSE ${B.rmse} m, grade p95 ${B.grade_p95}% (real ${B.grade_p95_real}%), crests+dips kept ${B.features_kept}/${B.features_real} (${(B.features_kept_share * 100).toFixed(0)}%)`);
    for (const [id, p] of Object.entries(per))
      P(`    ${id.padEnd(13)} ${String(p.km_measured).padStart(5)} km  RMSE ${String(p.road.rmse).padStart(5)}  bias ${String(p.road.bias).padStart(6)}  kept ${p.road.features.padStart(5)}  crest R min ${p.road.crest_r_min} m (real ${p.road.crest_r_min_real})`);
    const L30 = R.height.transects.roadside.at_30m, L60 = R.height.transects.roadside.at_60m;
    P(`    land beside the road, |land-road| p90: at 30 m ${L30.p90_game} m (real ${L30.p90_real}), at 60 m ${L60.p90_game} m (real ${L60.p90_real}); share over 2 m at 60 m ${L60.share_over_2m_game} (real ${L60.share_over_2m_real})`);
  }
  // --- the creek beds (WP-04b, critic C2): the seven creek transects of
  // truth/creek_transects.json (3DEP 1 m; the bed is each line's lowest
  // point). Per transect, within 60 m of that bed along the line: the lowest
  // 60 m grid, and the lowest CARVED TERRAIN - the grid cut down to every
  // creek's and ravine's stored bed the way CityElevation.Ground cuts it
  // before any road grades the land (its constants are read out of the C#,
  // so this cannot drift from the game). Data without WBED carves nothing
  // here (the old fixed 3.6 m carve along RG2's lines is not emulated).
  {
    const CT = JSON.parse(readFileSync(join(TRUTH, 'creek_transects.json'), 'utf8')).transects;
    const K = (() => {
      const cs = readFileSync(join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Scripts', 'City', 'CityElevation.cs'), 'utf8');
      const num = name => { const m = new RegExp(`\\b${name}\\s*=\\s*([\\d.]+)f`).exec(cs); if (!m) throw new Error(`CityElevation.cs: no ${name}`); return +m[1]; };
      return { below: num('WaterBelowBed'), carve: num('CarveBelowWater'), flatMin: num('CreekFlatMin'), flatPad: num('CreekFlatPad'),
               bank: num('BankSlope'), ravBelow: num('RavineBelowBed'), ravFlat: num('RavineFlat'), reach: num('CarveReachM') };
    })();
    const lines = city.waters.filter(w => !w.lake && w.bed && w.bed.length).map(w => {
      const acc = [0]; for (let i = 1; i < w.pts.length; i++) acc.push(acc[i - 1] + Math.hypot(w.pts[i][0] - w.pts[i - 1][0], w.pts[i][1] - w.pts[i - 1][1]));
      let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity; for (const [x, z] of w.pts) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); z0 = Math.min(z0, z); z1 = Math.max(z1, z); }
      return { w, acc, box: [x0 - K.reach, z0 - K.reach, x1 + K.reach, z1 + K.reach], width: w.ravine ? Math.max(1, w.width) : Math.max(4, w.width) };
    });
    // CityElevation.BedYAt: samples every bedStep from the first point, the last at the line's end
    const bedAt = (w, len, s) => { const b = w.bed; if (b.length === 1 || !(w.bedStep > 0)) return b[0];
      const last = b.length - 1, sLast = (last - 1) * w.bedStep;
      if (s >= sLast) return b[last - 1] + (b[last] - b[last - 1]) * Math.min(1, Math.max(0, (s - sLast) / Math.max(1e-3, len - sLast)));
      const f = Math.max(0, s) / w.bedStep, k = Math.min(last - 1, Math.floor(f)); return b[k] + (b[k + 1] - b[k]) * (f - k); };
    const carved = (x, z) => {
      let y = dem.asl(x, z);
      for (const L of lines) {
        if (x < L.box[0] || x > L.box[2] || z < L.box[1] || z > L.box[3]) continue;
        const p = L.w.pts;
        let best = Infinity, sBest = 0;
        for (let i = 1; i < p.length; i++) {
          const dx = p[i][0] - p[i - 1][0], dz = p[i][1] - p[i - 1][1], l2 = dx * dx + dz * dz;
          const t = l2 > 1e-8 ? Math.max(0, Math.min(1, ((x - p[i - 1][0]) * dx + (z - p[i - 1][1]) * dz) / l2)) : 0;
          const d = Math.hypot(x - p[i - 1][0] - dx * t, z - p[i - 1][1] - dz * t);
          if (d < best) { best = d; sBest = L.acc[i - 1] + Math.sqrt(l2) * t; }
        }
        if (best >= K.reach) continue;
        const bed = bedAt(L.w, L.acc[L.acc.length - 1], sBest);
        const floor = L.w.ravine ? bed - K.ravBelow : bed - K.below - K.carve;
        const flat = L.w.ravine ? K.ravFlat : Math.max(K.flatMin, L.width * 0.5 + K.flatPad);
        y = Math.min(y, floor + Math.max(0, best - flat) * K.bank);
      }
      return y;
    };
    const per = {};
    for (const t of CT) {
      const br = t.bearing_deg * Math.PI / 180, cx = toX(t.centre[1]), cz = toZ(t.centre[0]);
      let gMin = Infinity, cMin = Infinity;
      for (let a = t.bed.along_m - 60; a <= t.bed.along_m + 60; a += 1) {
        const x = cx + Math.sin(br) * a, z = cz + Math.cos(br) * a;
        gMin = Math.min(gMin, dem.asl(x, z)); cMin = Math.min(cMin, carved(x, z));
      }
      per[t.id] = { bed_3dep_1m: t.bed.asl, grid60_error: r2(gMin - t.bed.asl), carved_error: r2(cMin - t.bed.asl) };
    }
    const errs = Object.values(per).map(v => v.carved_error);
    R.height.creeks = { note: 'lowest within 60 m of the 3DEP 1 m bed along each creek transect: the 60 m grid, and the grid carved to the stored beds (CityElevation.Ground before the road corridors)',
                        within_1m: errs.filter(v => Math.abs(v) <= 1).length, transects: errs.length, worst: r2(errs.reduce((m, v) => Math.abs(v) > Math.abs(m) ? v : m, 0)), per_transect: per };
    const c = R.height.creeks;
    P(`  creek beds (${c.transects} transects, 3DEP 1 m): carved terrain within 1 m at ${c.within_1m} of ${c.transects}, worst ${c.worst >= 0 ? '+' : ''}${c.worst} m` +
      (lines.length ? '' : ' (NO WBED in this data: nothing carved)'));
    P('    ' + Object.entries(per).map(([id, v]) => `${id} ${v.carved_error >= 0 ? '+' : ''}${v.carved_error} (grid ${v.grid60_error >= 0 ? '+' : ''}${v.grid60_error})`).join(', '));
  }
}

// ================================================================ VERTICAL
{
  const t1 = Date.now();
  const routeEdges = new Set(); for (const rt of city.routes) for (const ei of rt.edges) routeEdges.add(ei);
  let dep = null;
  try { dep = load3dep(); } catch { dep = null; }
  P(`
VERTICAL (plan B2: CityElevation.Solve emulated offline - no water spans, seats, twin holds or vertical curves; the PROFILE audit measures the game's own)`);
  const rules = ARGS.includes('--vertical-old') ? ['old', 'b2'] : ['b2'];
  for (const rule of rules) {
    const em = emulateSolve(city, roadDem.at, { rule, routeEdges });
    const pn = profileNumbers(city, em);
    P(`  [${rule === 'b2' ? "the B2 trench rule (the game's)" : 'the trench rule before B2'}]`);
    for (const l of pn.lines) P(l);
    if (dep) {
      const real = realSeparations(city, (x, z) => dep.sample(toLat(z), toLon(x)));
      const t1Tall = pn.tallList.filter(ci => pn.tierX(ci) === 1);
      const realTall = t1Tall.filter(ci => real[ci] > SEPARATION_MAX).length;
      const med = a => { const v = a.filter(Number.isFinite).sort((p, q) => p - q); return v.length ? v[v.length >> 1] : NaN; };
      P(`  3DEP at the T1 separations over ${SEPARATION_MAX.toFixed(2)} m: ${realTall} of ${t1Tall.length} are that tall in reality too; real separation median ${med([...real]).toFixed(2)} m over all crossings`);
      pn.json.crossings.t1_over_max_real_too = realTall;
    }
    if (rule === 'b2') R.vertical = pn.json; else R.vertical_old_rule = pn.json;
  }
  P(`  (${((Date.now() - t1) / 1000).toFixed(1)} s)`);
}

// ================================================================ CONTROL
{
  const ctlName = { 4: 'signal', 2: 'stop', 1: 'give_way' };
  const classOfNode = ni => {
    const E = city.nodeEdges[ni].map(i => city.edges[i]);
    if (E.some(e => e.rank === 5 && !e.link)) return 'freeway';
    if (E.some(e => e.link) && E.some(e => !e.link)) return 'ramp_terminal';
    const ranks = [...new Set(E.filter(e => !e.link).map(e => e.wayId))].map(w => Math.max(...E.filter(e => e.wayId === w).map(e => e.rank))).sort((a, b) => b - a);
    const hi = ranks[0] ?? 0, lo = ranks[1] ?? 0;
    return hi >= 2 && lo >= 2 ? 'arterial_x_arterial' : hi >= 2 ? 'arterial_x_minor' : 'minor_x_minor';
  };
  const byClass = {};
  let atJunction = 0, midWay = 0, atEnd = 0;
  const tot = { signal: 0, stop: 0, give_way: 0 };
  for (let ni = 0; ni < city.nodes.length; ni++) {
    const deg = city.nodeEdges[ni].length, c = city.nodes[ni].ctl;
    if (c) { tot[ctlName[c]]++; if (deg >= 3) atJunction++; else if (deg === 2) midWay++; else atEnd++; }
    if (deg < 3) continue;
    const k = classOfNode(ni);
    byClass[k] ??= { junctions: 0, signal: 0, stop: 0, give_way: 0 };
    byClass[k].junctions++;
    if (c) byClass[k][ctlName[c]]++;
  }
  for (const v of Object.values(byClass)) v.controlled_share = r3((v.signal + v.stop + v.give_way) / v.junctions);
  R.control = { ...tot, at_junction_nodes: atJunction, at_mid_way_nodes: midWay, at_dead_ends: atEnd, by_junction_class: byClass,
                note: 'node tags only, matched by OSM id; a signal mapped on the approach (mid-way) does not control the junction yet' };
  P('\nCONTROL (OSM highway=traffic_signals/stop/give_way matched to graph nodes by id)');
  P(`  ${tot.signal} signals, ${tot.stop} stops, ${tot.give_way} give-ways; ${atJunction} on junction nodes (3+ arms), ${midWay} mid-way (2 arms), ${atEnd} at dead ends`);
  for (const [k, v] of Object.entries(byClass)) P(`    ${k.padEnd(20)} ${String(v.junctions).padStart(5)} junctions  ${String(v.signal).padStart(4)} signal  ${String(v.stop).padStart(3)} stop  controlled ${(v.controlled_share * 100).toFixed(1)}%`);
}

// ================================================================ SIZE
{
  const br = b => brotliCompressSync(b, { params: { [Z.BROTLI_PARAM_QUALITY]: FAST ? 5 : 11, [Z.BROTLI_PARAM_LGWIN]: 24, [Z.BROTLI_PARAM_SIZE_HINT]: b.length } }).length;
  const S = { brotli_quality: FAST ? 5 : 11, files: {}, city_sections: {} };
  let rawT = 0, brT = 0;
  for (const [k, name] of [['city', 'charlotte_city.bytes'], ['dem', 'charlotte_dem.bytes'], ['bld', 'charlotte_bld.bytes'], ['routes', 'charlotte_routes.json']]) {
    const b = files[k], c = br(b); S.files[name] = { raw: b.length, brotli: c }; rawT += b.length; brT += c;
  }
  S.total = { raw: rawT, brotli: brT };
  for (const [name, [a, b]] of Object.entries(city.sections)) { const sub = files.city.subarray(a, b); S.city_sections[name] = { raw: sub.length, brotli: br(sub) }; }
  S.city_sections.EDGE.points_raw = city.pointBytes;
  R.size = S;
  P(`\nSIZE (Brotli q${S.brotli_quality}, lgwin 24, each file alone: a proxy for WebGL.data.unityweb)`);
  for (const [n, v] of Object.entries(S.files)) P(`  ${n.padEnd(22)} ${(v.raw / 1e6).toFixed(3).padStart(7)} MB raw  ${(v.brotli / 1e6).toFixed(3).padStart(7)} MB brotli`);
  P(`  ${'total'.padEnd(22)} ${(rawT / 1e6).toFixed(3).padStart(7)} MB raw  ${(brT / 1e6).toFixed(3).padStart(7)} MB brotli`);
  P('  charlotte_city.bytes by section: ' + Object.entries(S.city_sections).map(([k, v]) => `${k} ${(v.raw / 1e3).toFixed(0)}/${(v.brotli / 1e3).toFixed(0)} KB`).join(', '));
}

// ================================================================ ACCURACY
R.accuracy = { measured: false, note: 'Plan critic C12: centreline offset and drawn-minus-real width against Mecklenburg EdgeOfPavementImpervious (CC0/MIT) and County Streets PAVEMENTWIDTH (CC BY 4.0). Neither layer is fetched yet (they belong on D:\\gis\\charlotte, PSX_GIS_DIR); this section reports nothing until they are.' };
P('\nACCURACY: not measured - the county pavement layers (plan C12) are not fetched yet');

R.elapsed_s = r1((Date.now() - t0) / 1000);
P(`\n(${R.elapsed_s} s, data ${R.data})`);

// ------------------------------------------------------------ compare
const cmpPath = argVal('--compare');
if (cmpPath) {
  const base = JSON.parse(readFileSync(resolve(UNITY, cmpPath), 'utf8'));
  const diffs = [];
  const walk = (a, b, path) => {
    if (typeof a === 'number' && typeof b === 'number') { if (a !== b) diffs.push(`${path}: ${b} -> ${a} (${a - b >= 0 ? '+' : ''}${r3(a - b)})`); return; }
    if (typeof a !== 'object' || a === null || typeof b !== 'object' || b === null) { if (JSON.stringify(a) !== JSON.stringify(b)) diffs.push(`${path}: ${JSON.stringify(b)} -> ${JSON.stringify(a)}`); return; }
    for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) if (!['elapsed_s', 'node', 'data'].includes(k)) walk(a[k], b[k], path ? `${path}.${k}` : k);
  };
  walk(R, base, '');
  P(`\nCOMPARE with ${cmpPath}: ${diffs.length} values moved`);
  for (const d of diffs) P('  ' + d);
}
console.log(out.join('\n'));
const jsonPath = argVal('--json');
if (jsonPath) { writeFileSync(resolve(UNITY, jsonPath), JSON.stringify(R, null, 1) + '\n'); console.log(`wrote ${jsonPath}`); }
