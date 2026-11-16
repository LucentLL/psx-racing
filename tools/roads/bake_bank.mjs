// bake_bank.mjs - the road's cross-section, crown and superelevation, from the 1 m lidar.
//
//   node tools/roads/bake_bank.mjs <prefix> [--check] [--plot <dir>] [--dump <file.csv>]
//
// Every stage road was built level side to side (PSXRacingBuilder.BuildRoad: "The ring is flat across").
// A real two-lane road is not. On a straight it is CROWNED: each lane falls about 2% from the centre line
// to its edge, so rain runs off. In a bend it is SUPERELEVATED: one plane, banked down toward the inside of
// the bend, so the car leans into it. Between the two is a RUNOFF, where the outside lane is rotated up
// through level to the bank over a few tens of metres. This tool measures all of it from the same 1 m
// bare-earth lidar the bakes read (bake_3dep.mjs), checks it against the AASHTO / NCDOT design value for
// the bend's radius and speed, and writes ONE NEW FILE per stage. It changes nothing else: the stage JSON,
// the DEM grids and every other bake stay exactly as they are.
//
//   Art/<stage>/<pre>_bank.json
//     left[i], right[i]   per waypoint, the left and right tarmac EDGE's height over the centre line, as a
//                         cross slope in per mille of the half width:  (y_edge - y_centre) / half * 1000.
//                         Positive = the edge stands above the centre line. A 2% crown is -20 / -20; a
//                         right-hand bend banked 6% is +60 / -60. "Left" and "right" are as the waypoints
//                         run (Unity's RightAt = Cross(up, forward)), so a reversed twin swaps them.
//     source              one letter per waypoint: m = measured, d = design (the lidar could not say),
//                         s = on a structure (bridge, tunnel, a deck the bare earth drops away under:
//                         design), e = the route's first or last three stations (held at their neighbour's)
//     stats, standards    what was measured and the design rules used, for the record
//
// HOW IT IS MEASURED
//   Per waypoint, five cross-section lines 0.75 m apart along the road (the station's own and two either
//   side, so one fit sees about 7 m of road), sampled every 0.25 m across the paved width less 0.5 m at
//   each edge (the edge itself is where the shoulder, a ditch or a kerb begins). The window is centred on
//   the road bench's middle (bench.mjs findBench) where the bench is confidently found within 1.5 m of the
//   line, else on the line. Two models are fitted, each robustly (residuals over 2.5 sigma dropped, three
//   rounds), each with a term along the road for the grade:
//     A  one plane                 h = c + s t + g a                     (superelevated, or a tilted road)
//     B  a crown, two half planes  h = c + pL (t - e0)+ + pR (e0 - t)+ + g a,  e0 within 0.75 m of the middle
//   B is taken only when it is clearly better (its RMS at least 3 mm and 15% under A's) AND both halves
//   fall away from the crown; otherwise A. A station counts as MEASURED when the fit kept 60% of its
//   samples, its RMS is under RMS_OK, no edge slope is over 14%, it is not on a structure, and it is SANE:
//   in a bend under 150 m radius the outside edge may not fall more than 2.5% below the centre (an
//   adverse bank there is the ditch or the canopy, not the road). The kept values are then filtered along
//   the road: the median of the confident stations within two either side (three needed), a station more
//   than 2% off that median is dropped, and the median of what is left is the measurement.
//
// THE DESIGN VALUE
//   AASHTO, A Policy on Geometric Design of Highways and Streets (2018, "Green Book") 3.3: the minimum
//   radius Rmin = V^2 / (15 (emax + fmax)) (feet, mph) with fmax from its Table 3-7; superelevation by
//   Method 5, in the closed form e = emax (2 Rmin/R - (Rmin/R)^2) (exact at Rmin, falling parabolically to
//   normal crown); under Rmin(posted) the bend's own design speed is lower and it sits at its minimum
//   radius, so e = emax. Normal crown (2%) where e < 1.5%, a 2% plane (reverse crown) up to 2%. emax = 8%
//   (NCDOT rural; see STANDARDS below), the Linn Cove Viaduct its own 10%. The speed is the stage's posted
//   limit (TrackCatalog speedLimitKmh).
//
// CHOSEN AND SMOOTHED
//   Measured where it is confident and sane, design where it is not and on every structure. Then along the
//   road, split as tilt T = (L - R)/2 and crown C = (L + R)/2: a median of three, then every rise and fall
//   is given a real runoff: the edge's height relative to the centre line may change by at most the
//   AASHTO maximum relative gradient for the speed (Green Book Table 3-15: 0.78% at 15 mph to 0.54% at 45
//   mph; a two-lane road rotated about its centre line rotates one lane width each side), with two thirds
//   of each runoff on the tangent and one third in the bend (NCDOT: "applied 1/3 in, 2/3 outside of
//   curve"). Then |slope| <= 12% everywhere.
//
// --check re-derives everything from the lidar cache and compares it with the file on disk (exit 1 if it
// differs). --plot <dir> writes per-stage cross-section CSVs (the sharpest bend and a typical straight:
// lidar ground, the fitted surface, the game road) for plotting. --dump writes every station's numbers.
//
// THE LIDAR CACHE. Read only, never fetched: %PSX_ROADS_CACHE%\3dep when PSX_ROADS_CACHE is set, else this
// checkout's tools/roads/cache/3dep, else (a git worktree has no cache of its own: it is gitignored) the
// main checkout's. A station whose lidar tile is not in the cache gets the design value and is listed.
// The ledger is not written: this tool only reads what the bakes stored.

import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { makeProjection } from './lib.mjs';
import { open3dep, dem1m, STAGES, loadStage, PROJECT_1M } from './lib3dep.mjs';
import { ribbon, findBench, tangents } from './bench.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..', '..');
const args = process.argv.slice(2);
const flag = n => args.includes(n);
const opt = (n, d) => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : d; };
const pre = args[0];
if (!pre || !STAGES[pre]) {
  console.error('usage: node bake_bank.mjs <' + Object.keys(STAGES).join('|') + '> [--check] [--plot dir] [--dump file.csv]');
  process.exit(2);
}
const CHECK = flag('--check');
const PLOT = opt('--plot', null);
const DUMP = opt('--dump', null);
const st = STAGES[pre];

// ------------------------------------------------------------ standards
/// AASHTO Green Book (2018) Table 3-7: the side friction factor the minimum radius is computed with.
const FMAX = [[10, 0.38], [15, 0.32], [20, 0.27], [25, 0.23], [30, 0.20], [35, 0.18], [40, 0.16], [45, 0.15], [50, 0.14], [55, 0.13], [60, 0.12]];
/// AASHTO Green Book (2018) Table 3-15: maximum relative gradient of the edge against the profile line.
const DREL = [[15, 0.0078], [20, 0.0074], [25, 0.0070], [30, 0.0066], [35, 0.0062], [40, 0.0058], [45, 0.0054], [50, 0.0050], [55, 0.0047], [60, 0.0045]];
const EMAX = 0.08;              // NCDOT rural (see STANDARDS)
const NC = -20;                 // normal crown, per mille
const CLAMP = 120;              // 12%: no road here is banked steeper, even a hairpin
const RMS_OK = 0.035;           // m: a fit noisier than this is canopy or rubble, not pavement (see the stats)
const MAX_SLOPE = 140;          // per mille: a fitted edge slope over 14% is not a road surface
const ADVERSE_R = 150, ADVERSE = -25;   // m, per mille: the outside edge of a bend under 150 m may fall no more than this
/// Real structures with design values of their own, where the bare earth has no road to measure.
const STRUCTURES = {
  brp: [{
    name: 'Linn Cove Viaduct', minSpanM: 300, emax: 0.10, relGrad: 0.10 * 2 * 3.2 / 55,
    cite: 'J. Muller and J. M. Barker, "Design and Construction of Linn Cove Viaduct", PCI Journal 30(5), Sept-Oct 1985: ' +
      'the roadway goes from a full 10% superelevation one way to 10% the other in 180 ft (55 m) transitions, ' +
      'on 250 ft (76 m) curves (https://doi.org/10.15554/pcij.09011985.38.53; TRB News 102)',
  }],
};
const STANDARDS = {
  emax: EMAX, normalCrownPermille: NC, clampPermille: CLAMP,
  method: 'AASHTO Green Book 2018 3.3: Rmin = V^2/(15(emax+fmax)) (Table 3-7 fmax); Method 5 as e = emax(2Rmin/R - (Rmin/R)^2); ' +
    'R <= Rmin(posted): e = emax at the bend\'s own lower design speed; NC 2% where e < 1.5%, 2% plane (RC) to 2%',
  runoff: 'edge relative gradient <= AASHTO Table 3-15 for the design speed (0.78% at 15 mph .. 0.54% at 45 mph), one lane ' +
    'rotated each side of the centre line; 2/3 of the runoff on the tangent, 1/3 in the bend',
  citations: [
    'AASHTO, A Policy on Geometric Design of Highways and Streets, 7th ed. (2018), 3.3.3-3.3.8 (Tables 3-7, 3-8, 3-15)',
    'NCDOT Roadway Design Manual (May 2025 update), superelevation guidance as taught in NCDOT "Superelevation Training" ' +
      '(https://connect.ncdot.gov/projects/Value-Management/CLEAR-Program/Video/Superelevation%20Lunch%20and%20Learn.pdf): ' +
      'runoff by the 2018 Green Book 3.3.8.2.1 formula, worked at e = 8% on a rural two-lane road; runoff applied 1/3 in, 2/3 outside the curve; ' +
      '0.06 maximum where a bridge is proposed on the curve (new work only). The Manual itself (connect.ncdot.gov/projects/Roadway/RDM) ' +
      'was not read: its PDF is over the fetch limit. emax 8% is the rural value the training works at and the Green Book\'s common rural choice.',
    ...(STRUCTURES[pre] || []).map(s => s.cite),
  ],
};

const interp = (tab, v) => {
  if (v <= tab[0][0]) return tab[0][1];
  for (let k = 1; k < tab.length; k++) if (v <= tab[k][0]) {
    const [a, fa] = tab[k - 1], [b, fb] = tab[k];
    return fa + (fb - fa) * (v - a) / (b - a);
  }
  return tab[tab.length - 1][1];
};
const FT = 0.3048;
const rminM = (Vmph, emax) => Vmph * Vmph / (15 * (emax + interp(FMAX, Vmph))) * FT;
/// The speed whose minimum radius at emax is R (bisection; under 10 mph answers 10).
function speedForR(R, emax, Vtop) {
  let lo = 5, hi = Vtop;
  if (rminM(hi, emax) <= R) return hi;
  for (let k = 0; k < 40; k++) { const m = (lo + hi) / 2; if (rminM(m, emax) <= R) lo = m; else hi = m; }
  return Math.max(10, lo);
}
/// {e, V}: design superelevation (fraction) and design speed (mph) for a bend of radius R metres.
function designE(R, Vpost, emax) {
  const Rmin = rminM(Vpost, emax);
  if (!(R > Rmin)) return { e: emax, V: speedForR(R, emax, Vpost) };
  const q = Rmin / R;
  return { e: emax * (2 * q - q * q), V: Vpost };
}

// --------------------------------------------------------------- speed
/// The stage's posted limit, km/h, from TrackCatalog (the block with this id).
function postedKmh() {
  const src = readFileSync(join(ROOT, 'Assets', 'PSXRacing', 'Scripts', 'TrackCatalog.cs'), 'utf8');
  const at = src.indexOf(`id = "${st.id}"`);
  if (at < 0) throw new Error(`TrackCatalog.cs has no venue id "${st.id}"`);
  const m = /speedLimitKmh\s*=\s*([0-9.]+)f/.exec(src.slice(at, at + 4000));
  if (!m) throw new Error(`TrackCatalog.cs: no speedLimitKmh after id "${st.id}"`);
  return +m[1];
}

// ---------------------------------------------------------------- cache
function roadsCacheDir() {
  if (process.env.PSX_ROADS_CACHE) return process.env.PSX_ROADS_CACHE;
  const own = join(HERE, 'cache');
  if (existsSync(join(own, '3dep', 'sources.json'))) return own;
  try {
    const common = execFileSync('git', ['rev-parse', '--git-common-dir'], { cwd: ROOT, encoding: 'utf8' }).trim();
    const main = join(dirname(resolve(ROOT, common)), 'tools', 'roads', 'cache');
    if (existsSync(join(main, '3dep', 'sources.json'))) return main;
  } catch { /* not a git checkout */ }
  return own;
}

// ----------------------------------------------------------------- math
const median = a => { if (!a.length) return NaN; const s = a.slice().sort((p, q) => p - q); const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
/// Least squares by the normal equations (k <= 4 unknowns), or null when singular.
function lsq(rows, ys) {
  const k = rows[0].length, A = Array.from({ length: k }, () => new Float64Array(k + 1));
  for (let r = 0; r < rows.length; r++) {
    const x = rows[r];
    for (let i = 0; i < k; i++) { for (let j = 0; j < k; j++) A[i][j] += x[i] * x[j]; A[i][k] += x[i] * ys[r]; }
  }
  for (let c = 0; c < k; c++) {
    let p = c; for (let r = c + 1; r < k; r++) if (Math.abs(A[r][c]) > Math.abs(A[p][c])) p = r;
    if (Math.abs(A[p][c]) < 1e-9) return null;
    [A[c], A[p]] = [A[p], A[c]];
    for (let r = 0; r < k; r++) if (r !== c) { const f = A[r][c] / A[c][c]; for (let j = c; j <= k; j++) A[r][j] -= f * A[c][j]; }
  }
  return A.map((row, i) => row[k] / row[i]);
}
/// Fit with residuals over 2.5 sigma dropped (sigma at least 1 cm), three rounds.
function robustFit(samples, basis) {
  let use = samples.map(() => true), coef = null, rms = Infinity, kept = 0;
  for (let it = 0; it < 4; it++) {
    const rows = [], ys = [];
    samples.forEach((p, k) => { if (use[k]) { rows.push(basis(p)); ys.push(p.h); } });
    if (rows.length < 8) return null;
    coef = lsq(rows, ys); if (!coef) return null;
    const res = samples.map(p => p.h - basis(p).reduce((s, x, j) => s + x * coef[j], 0));
    let s2 = 0; kept = 0;
    res.forEach((r, k) => { if (use[k]) { s2 += r * r; kept++; } });
    rms = Math.sqrt(s2 / kept);
    if (it === 3) break;
    const lim = 2.5 * Math.max(0.01, rms);
    const next = res.map(r => Math.abs(r) <= lim);
    if (next.every((u, k) => u === use[k])) break;
    use = next;
  }
  return { coef, rms, kept };
}

// ---------------------------------------------------------------- stage
const S = loadStage(ROOT, pre);
const { json, n, loop } = S;
const SP = json.spacing || 4;
const proj = makeProjection(json.lat0, json.lon0);
const half = st.paved / 2;
const wp = S.wp;
const lap = n * SP;
const kmh = postedKmh(), Vpost = kmh / 1.609344;
console.log(`=== ${pre}: ${st.road} (${st.id}), ${(lap / 1000).toFixed(2)} km${loop ? ', loop' : ''}, ${st.paved} m paved, posted ${kmh} km/h (${Vpost.toFixed(0)} mph) ===`);

const ROADS_CACHE = roadsCacheDir();
const store = open3dep({ dir: join(ROADS_CACHE, '3dep'), offline: true });
store.saveLedger = () => {};   // read only: never rewrite the bakes' ledger
const lidar = dem1m(store, 0);
const { tx, tz } = tangents(wp, loop);
/// World point at `t` metres LEFT of station i's line (bench.mjs convention) and `a` metres along it.
const at = (i, t, a) => ({ x: wp[i].x - tz[i] * t + tx[i] * a, z: wp[i].z + tx[i] * t + tz[i] * a });
const sampleXZ = (x, z) => {
  try { const ll = proj.toLL(x, z), h = lidar.sample(ll.lat, ll.lon); return Number.isFinite(h) ? h : NaN; }
  catch { return NaN; }
};
const noTile = new Uint8Array(n);
{
  const pts = i => { const p = []; for (const t of [-32, -12, 0, 12, 32]) for (const a of [-3, 3]) { const q = at(i, t, a); p.push(proj.toLL(q.x, q.z)); } return p; };
  try {
    const all = []; for (let i = 0; i < n; i++) all.push(...pts(i));
    await lidar.prepare(all);
  } catch (e) {
    console.log(`  lidar: not every tile is in the cache (${e.message.split('\n')[0]}); reading station by station`);
    for (let i = 0; i < n; i++) { try { await lidar.prepare(pts(i)); } catch { noTile[i] = 1; } }
  }
}
console.log(`  lidar: ${PROJECT_1M} 1 m DEM from ${join(ROADS_CACHE, '3dep')} (offline)${noTile.some(Boolean) ? `, ${noTile.reduce((s, v) => s + v, 0)} station(s) without a tile` : ''}`);

// ----------------------------------------------------------- structures
const spansOf = a => { const o = []; for (let k = 0; k + 1 < (a || []).length; k += 2) o.push([a[k], a[k + 1]]); return o; };
const bridgeSpans = spansOf(json.bridges), tunnelSpans = spansOf(json.tunnels);
/// OSM bridge / tunnel ways lying on the route (as bake_3dep.mjs reads them).
function osmSpans() {
  const p = join(ROADS_CACHE, st.osm);
  if (!existsSync(p)) return [];
  const ways = JSON.parse(readFileSync(p, 'utf8')).elements.filter(e => e.type === 'way' && e.geometry && e.tags && e.tags.highway &&
    ((e.tags.bridge && e.tags.bridge !== 'no') || (e.tags.tunnel && e.tags.tunnel !== 'no')));
  const out = [];
  for (const w of ways) {
    const idx = []; let tot = 0;
    const g = w.geometry.map(q => proj.toXZ(q.lat, q.lon));
    for (let k = 0; k + 1 < g.length; k++) {
      const L = Math.hypot(g[k + 1].x - g[k].x, g[k + 1].z - g[k].z), m = Math.max(1, Math.round(L / 4));
      for (let j = 0; j <= m; j++) {
        const x = g[k].x + (g[k + 1].x - g[k].x) * j / m, z = g[k].z + (g[k + 1].z - g[k].z) * j / m; tot++;
        let bi = -1, bd = 9;
        for (let i = 0; i < wp.length; i++) { const d = (wp[i].x - x) ** 2 + (wp[i].z - z) ** 2; if (d < bd) { bd = d; bi = i; } }
        if (bi >= 0) idx.push(bi);
      }
    }
    if (idx.length >= 2 && idx.length / tot >= 0.6) out.push([Math.min(...idx) * SP, Math.max(...idx) * SP]);
  }
  return out;
}
const osm = osmSpans();
const inSpans = (spans, s, pad) => spans.some(([a, b]) => (s >= a - pad && s <= b + pad) || (loop && s + lap >= a - pad && s + lap <= b + pad));
const onStructure = i => inSpans(bridgeSpans, i * SP, 12) || inSpans(tunnelSpans, i * SP, 12) || inSpans(osm, i * SP, 10);
const rib = ribbon(wp, loop, sampleXZ);
const bench = findBench(rib, { skip: onStructure });
const structure = new Uint8Array(n);
for (let i = 0; i < n; i++) if (onStructure(i)) structure[i] = 1;
for (const [a, b] of bench.dips) for (let i = a; i <= b; i++) structure[i] = 1;
/// A structure with design values of its own (the Linn Cove Viaduct), per station, or null.
const special = new Array(n).fill(null);
for (const sd of STRUCTURES[pre] || [])
  for (const [a, b] of bridgeSpans) if (b - a >= sd.minSpanM)
    for (let i = 0; i < n; i++) if (inSpans([[a, b]], i * SP, 12)) special[i] = sd;

// ------------------------------------------------------------ curvature
/// Signed plan curvature (+ = turning left), Menger over +-2 stations, then a light gaussian.
const kap = new Float64Array(n);
const W = i => (loop ? wp[((i % n) + n) % n] : wp[Math.max(0, Math.min(n - 1, i))]);
for (let i = 0; i < n; i++) {
  const a = W(i - 2), b = W(i), c = W(i + 2);
  const ux = b.x - a.x, uz = b.z - a.z, vx = c.x - b.x, vz = c.z - b.z;
  const cr = ux * vz - uz * vx, la = Math.hypot(ux, uz), lb = Math.hypot(vx, vz), lc = Math.hypot(c.x - a.x, c.z - a.z);
  kap[i] = la * lb * lc > 1e-9 ? 2 * cr / (la * lb * lc) : 0;
}
{
  const s = new Float64Array(n), w = [0.25, 0.5, 0.25];
  for (let i = 0; i < n; i++) { let v = 0; for (let o = -1; o <= 1; o++) { const j = loop ? (i + o + n) % n : Math.max(0, Math.min(n - 1, i + o)); v += w[o + 1] * kap[j]; } s[i] = v; }
  kap.set(s);
}
const radius = i => (Math.abs(kap[i]) > 1e-6 ? 1 / Math.abs(kap[i]) : Infinity);

// ------------------------------------------------------------- measure
const STEP = 0.25, ALONG = [-1.5, -0.75, 0, 0.75, 1.5], WIN = half - 0.5;
const E0S = [-0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75];
const raw = Array.from({ length: n }, () => null);
let rmsAll = [];
function fitStation(i) {
  const centred = bench.conf[i] && Number.isFinite(bench.offF[i]) && Math.abs(bench.offF[i]) <= 1.5;
  const c = centred ? bench.offF[i] : 0;
  const samples = []; let tried = 0;
  for (const a of ALONG) for (let t = -WIN; t <= WIN + 1e-9; t += STEP) {
    tried++;
    const p = at(i, c + t, a), h = sampleXZ(p.x, p.z);
    if (Number.isFinite(h)) samples.push({ t, a, h });
  }
  if (samples.length < 0.6 * tried) return { c, why: 'no lidar' };
  const A = robustFit(samples, p => [1, p.t, p.a]);
  if (!A) return { c, why: 'fit' };
  let B = null, e0B = 0;
  for (const e0 of E0S) {
    const f = robustFit(samples, p => [1, Math.max(p.t - e0, 0), Math.max(e0 - p.t, 0), p.a]);
    if (f && (!B || f.rms < B.rms)) { B = f; e0B = e0; }
  }
  // THE CLASS: crowned when B is clearly the better fit and both halves fall away from the crown.
  const crownedFit = B && A.rms - B.rms >= Math.max(0.003, 0.15 * A.rms) && B.coef[1] < 0 && B.coef[2] < 0;
  // THE VALUES: B's two half planes are exactly what the game's three-vertex ring can draw (left edge,
  // centre, right edge), and a plane is just the case pL = -pR, so B's slopes are taken whatever the
  // class (with ~100 samples each half is good to about 0.3%), which keeps a weak crown that did not
  // pass the class test instead of reading it as level. Not where B makes a valley (a centre below both
  // edges is no road here: a ditch in the window), and not where B is steeper than a road and A is not.
  const valley = B && (B.coef[1] + B.coef[2]) / 2 > 0.005;
  const steepB = B && Math.max(Math.abs(B.coef[1]), Math.abs(B.coef[2])) * 1000 > MAX_SLOPE && Math.abs(A.coef[1]) * 1000 <= MAX_SLOPE;
  const useB = B && !valley && !steepB;
  const fit = useB ? B : A;
  // left / right edge slopes, per mille, about the game's centre line (the crown, where B)
  const L = useB ? B.coef[1] * 1000 : A.coef[1] * 1000;
  const R = useB ? B.coef[2] * 1000 : -A.coef[1] * 1000;
  return { c, model: crownedFit ? 'B' : 'A', L, R, rms: fit.rms, kept: fit.kept / samples.length, e0: useB ? e0B : 0, coef: fit.coef, fitB: useB, samples: samples.length };
}
for (let i = 0; i < n; i++) {
  if (structure[i] || noTile[i]) { raw[i] = { why: structure[i] ? 'structure' : 'no lidar' }; continue; }
  raw[i] = fitStation(i);
  if (Number.isFinite(raw[i].rms)) rmsAll.push(raw[i].rms);
}
const rmsSorted = rmsAll.slice().sort((a, b) => a - b), pct = p => rmsSorted[Math.min(rmsSorted.length - 1, Math.floor(p * rmsSorted.length))];

// confident and sane
const confident = new Uint8Array(n), why = new Array(n).fill('');
for (let i = 0; i < n; i++) {
  const r = raw[i];
  if (!r || r.why) { why[i] = r ? r.why : 'none'; continue; }
  if (r.kept < 0.6) { why[i] = 'kept'; continue; }
  if (!(r.rms <= RMS_OK)) { why[i] = 'rms'; continue; }
  if (Math.abs(r.L) > MAX_SLOPE || Math.abs(r.R) > MAX_SLOPE) { why[i] = 'steep'; continue; }
  if (radius(i) < ADVERSE_R) {
    const outside = kap[i] > 0 ? r.R : r.L;            // a left turn's outside is the right edge
    if (outside < ADVERSE) { why[i] = 'adverse'; continue; }
  }
  confident[i] = 1;
}
// along the road: T = tilt (+ = higher on the left), C = crown (+ = centre below the edges)
const Tm = new Float64Array(n).fill(NaN), Cm = new Float64Array(n).fill(NaN);
{
  const T0 = i => (raw[i].L - raw[i].R) / 2, C0 = i => (raw[i].L + raw[i].R) / 2;
  const nb = (i, f) => { const v = []; for (let o = -2; o <= 2; o++) { let j = i + o; if (loop) j = (j + n) % n; else if (j < 0 || j >= n) continue; if (f(j)) v.push(j); } return v; };
  const keep = Uint8Array.from(confident);
  for (let i = 0; i < n; i++) {
    if (!confident[i]) continue;
    const js = nb(i, j => confident[j]);
    if (js.length < 3) { keep[i] = 0; why[i] = 'alone'; continue; }
    const mt = median(js.map(T0)), mc = median(js.map(C0));
    if (Math.abs(T0(i) - mt) > 20 || Math.abs(C0(i) - mc) > 20) { keep[i] = 0; why[i] = 'outlier'; }
  }
  for (let i = 0; i < n; i++) {
    if (structure[i] || noTile[i]) continue;
    const js = nb(i, j => keep[j]);
    if (js.length < 3) continue;
    Tm[i] = median(js.map(T0)); Cm[i] = median(js.map(C0));
  }
}

// --------------------------------------------------------------- design
const Td = new Float64Array(n), Cd = new Float64Array(n), Vd = new Float64Array(n), Ed = new Float64Array(n);
for (let i = 0; i < n; i++) {
  const emax = special[i] ? special[i].emax : EMAX;
  const { e, V } = designE(radius(i), Vpost, emax);
  Vd[i] = V; Ed[i] = e;
  if (e < 0.015) { Td[i] = 0; Cd[i] = NC; }
  else { Td[i] = -Math.sign(kap[i]) * Math.max(e, 0.02) * 1000; Cd[i] = 0; }
}
// --------------------------------------------------------------- choose
const T = new Float64Array(n), C = new Float64Array(n), src = new Array(n);
for (let i = 0; i < n; i++) {
  if (structure[i]) { T[i] = Td[i]; C[i] = Cd[i]; src[i] = 's'; }
  else if (Number.isFinite(Tm[i])) { T[i] = Tm[i]; C[i] = Cm[i]; src[i] = 'm'; }
  else { T[i] = Td[i]; C[i] = Cd[i]; src[i] = 'd'; }
}
/// Per station: the most the edge's slope (per mille of the half width) may change to the next station.
/// A designed station takes the AASHTO relative gradient for its speed; a MEASURED one keeps the road as
/// the lidar found it, up to MEAS_RATE times that (old mountain roads, and every hairpin, turn faster than
/// a modern runoff: Chimney Rock's hairpins reach their 8-10% within two stations), which only stops a
/// spike the along-road median let through.
const MEAS_RATE = 4;
const rate = new Float64Array(n);
for (let i = 0; i < n; i++) rate[i] = (special[i] ? special[i].relGrad : interp(DREL, Vd[i])) * SP / half * 1000 * (src[i] === 'm' ? MEAS_RATE : 1);

// ---------------------------------------------------------------- smooth
// The measured stations are the road: nothing designed may move them. So every pass below lets a
// measured station take a constraint only from another measured one, while a designed station takes
// it from both sides; the runoffs are laid on the designed side of every join.
const J = (i, o) => (loop ? (i + o + n) % n : Math.max(0, Math.min(n - 1, i + o)));
const feeds = (from, to) => src[to] !== 'm' || src[from] === 'm';
function median3(a) { const o = Float64Array.from(a); for (let i = 0; i < n; i++) if (src[i] !== 'm') o[i] = median([a[J(i, -1)], a[i], a[J(i, 1)]]); return o; }
/// The upper envelope max_j (x_j - path rate) and the lower min_j (x_j + path rate), by forward and
/// backward passes (twice round on a loop).
function envelopes(x) {
  const U = Float64Array.from(x), D = Float64Array.from(x);
  for (let round = 0; round < (loop ? 2 : 1); round++) {
    for (let i = 1; i < n + (loop ? 1 : 0); i++) { const k = i % n, p = (i - 1 + n) % n, r = Math.min(rate[k], rate[p]); if (!feeds(p, k)) continue; U[k] = Math.max(U[k], U[p] - r); D[k] = Math.min(D[k], D[p] + r); }
    for (let i = n - 2 + (loop ? 1 : 0); i >= 0; i--) { const k = i % n, q = (i + 1) % n, r = Math.min(rate[k], rate[q]); if (!feeds(q, k)) continue; U[k] = Math.max(U[k], U[q] - r); D[k] = Math.min(D[k], D[q] + r); }
  }
  return { U, D };
}
/// A non-negative peak sequence given its runoffs: two thirds of each rise or fall before the change point.
function runoff(x) { const { U, D } = envelopes(x); return Float64Array.from(x, (_, i) => (2 * U[i] + D[i]) / 3); }
const Ts = median3(T), Cs = median3(C);
const Tp = runoff(Ts.map(v => Math.max(v, 0))), Tn = runoff(Ts.map(v => Math.max(-v, 0)));
// the crown channel as a non-negative sequence for the runoff (offset CROWN_BASE: a crown down to 10%, a centre up to 4% over the edges; a measured crown steeper than 2% is kept)
const CROWN_BASE = 100;
const Cr = runoff(Cs.map(v => Math.min(CROWN_BASE + 40, Math.max(0, v + CROWN_BASE))));
let Lf = new Float64Array(n), Rf = new Float64Array(n);
for (let i = 0; i < n; i++) { const t = Tp[i] - Tn[i], c = Cr[i] - CROWN_BASE; Lf[i] = c + t; Rf[i] = c - t; }
/// Last guard on each edge's relative gradient (the tilt and crown runoffs can overlap at a reversal).
function limitRate(a) {
  for (let round = 0; round < (loop ? 2 : 1); round++) {
    for (let i = 1; i < n + (loop ? 1 : 0); i++) { const k = i % n, p = (i - 1 + n) % n, r = Math.min(rate[k], rate[p]); if (feeds(p, k)) a[k] = Math.max(a[p] - r, Math.min(a[p] + r, a[k])); }
    for (let i = n - 2 + (loop ? 1 : 0); i >= 0; i--) { const k = i % n, q = (i + 1) % n, r = Math.min(rate[k], rate[q]); if (feeds(q, k)) a[k] = Math.max(a[q] - r, Math.min(a[q] + r, a[k])); }
  }
}
limitRate(Lf); limitRate(Rf);
/// A designed run held between two measured stations further apart in slope than its rate can join
/// (a short gap between a bend banked one way and a hairpin banked the other): one pass of limitRate
/// leaves the whole misfit in one step at the run's end, so lay the run straight between the two
/// measured values instead, which is the gentlest join there is.
function joinRuns(a) {
  let s = -1;
  for (let i = 0; i <= n; i++) {
    const free = i < n && src[i] !== 'm';
    if (free && s < 0) s = i;
    if (!free && s >= 0) {
      const e = i - 1;
      if (s > 0 && e < n - 1) {
        let worst = 0;
        for (let k = s - 1; k <= e; k++) worst = Math.max(worst, Math.abs(a[k + 1] - a[k]) - Math.min(rate[k], rate[k + 1]));
        if (worst > 0.5) for (let k = s; k <= e; k++) a[k] = a[s - 1] + (a[e + 1] - a[s - 1]) * (k - s + 1) / (e - s + 2);
      }
      s = -1;
    }
  }
}
joinRuns(Lf); joinRuns(Rf);
const left = new Int16Array(n), right = new Int16Array(n);
for (let i = 0; i < n; i++) {
  left[i] = Math.round(Math.max(-CLAMP, Math.min(CLAMP, Lf[i])));
  right[i] = Math.round(Math.max(-CLAMP, Math.min(CLAMP, Rf[i])));
}
// a route's ends: the first and last three stations hold their neighbour's section
if (!loop && n > 8) for (let k = 0; k < 3; k++) {
  left[k] = left[3]; right[k] = right[3]; src[k] = 'e';
  left[n - 1 - k] = left[n - 4]; right[n - 1 - k] = right[n - 4]; src[n - 1 - k] = 'e';
}

// ----------------------------------------------------------------- stats
const pc = v => +(v / 10).toFixed(1);                  // per mille -> percent, one decimal
const isBend = i => radius(i) < 150, isStraight = i => radius(i) > 800;
const sel = f => { const o = []; for (let i = 0; i < n; i++) if (f(i)) o.push(i); return o; };
const meas = sel(i => Number.isFinite(Tm[i]));
const tiltIn = (i, t) => -Math.sign(kap[i]) * t;       // + = banked toward the inside of the bend
const bendsM = meas.filter(isBend), straightM = meas.filter(isStraight);
const chosenBank = i => tiltIn(i, (left[i] - right[i]) / 2);
const pick = (arr, f) => arr.map(f);
const p98 = a => { if (!a.length) return NaN; const s = a.slice().sort((p, q) => p - q); return s[Math.min(s.length - 1, Math.floor(0.98 * s.length))]; };
const runs = mask => { const o = []; let a = -1; for (let i = 0; i <= n; i++) { const on = i < n && mask(i); if (on && a < 0) a = i; if (!on && a >= 0) { o.push([a, i - 1]); a = -1; } } return o; };
const unmeasured = runs(i => src[i] !== 'm' && src[i] !== 'e').filter(([a, b]) => b - a + 1 >= 3);
// the runoffs the measurement itself shows: per bend peak of the measured inward bank over 4%, the
// length from 20% to 80% of the peak on its way in
const runoffsM = [];
{
  const tin = Float64Array.from({ length: n }, (_, i) => (Number.isFinite(Tm[i]) ? tiltIn(i, Tm[i]) : NaN));
  for (let i = 2; i < n - 2; i++) {
    const v = tin[i];
    if (!(v >= 40) || !(v >= tin[i - 1] && v >= tin[i + 1] && v >= tin[i - 2] && v >= tin[i + 2])) continue;
    let k = i, k80 = -1;
    while (k > 0 && Number.isFinite(tin[k - 1]) && tin[k - 1] <= tin[k] + 2 && tin[k - 1] > 0.2 * v) { if (k80 < 0 && tin[k - 1] < 0.8 * v) k80 = k - 1; k--; }
    if (k > 0 && Number.isFinite(tin[k - 1]) && k80 >= 0) runoffsM.push((k80 - k + 1) * SP + SP);
  }
}
// the sharpest bend a race drives: on a route with ends, between the start line and the flag (past the
// flag Gillespie Gap turns onto the ramp at the junction and Mount Mitchell into the summit lot)
let sharp = -1;
const raced = i => loop || !(json.finishM > 0) || (i * SP >= (json.startLineM || 0) && i * SP <= json.finishM);
for (let i = loop ? 0 : 4; i < (loop ? n : n - 4); i++) if (raced(i) && (sharp < 0 || radius(i) < radius(sharp))) sharp = i;
const describe = i => ({
  wp: i, m: i * SP, radiusM: +radius(i).toFixed(1), turn: kap[i] > 0 ? 'left' : 'right', source: src[i],
  measured: Number.isFinite(Tm[i]) ? { bankInPct: pc(tiltIn(i, Tm[i])), crownPct: pc(-Cm[i]), leftPct: pc(Cm[i] + Tm[i]), rightPct: pc(Cm[i] - Tm[i]) } : null,
  raw: raw[i] && !raw[i].why ? { model: raw[i].model, leftPct: pc(raw[i].L), rightPct: pc(raw[i].R), rmsCm: +(raw[i].rms * 100).toFixed(1), why: why[i] || 'ok' } : { why: raw[i] ? raw[i].why : '?' },
  design: { bankInPct: pc(tiltIn(i, Td[i])), crownPct: pc(-Cd[i]), speedMph: +Vd[i].toFixed(0) },
  chosen: { leftPct: pc(left[i]), rightPct: pc(right[i]), bankInPct: pc(chosenBank(i)) },
});
const whyCount = {}; for (let i = 0; i < n; i++) if (!confident[i]) whyCount[why[i] || 'none'] = (whyCount[why[i] || 'none'] || 0) + 1;
// On a straight a station is either CROWNED (model B) or one TILTED plane (model A: a one-way cross
// slope, common on a sidehill road, or the runoff into the next bend). Report the two apart: a median
// over both would read a 2% crown on half the stations and a tilt on the rest as "no crown".
const straightC = sel(i => isStraight(i) && confident[i]);
const crowned = straightC.filter(i => raw[i].model === 'B'), tilted = straightC.filter(i => raw[i].model === 'A');
const crownStraight = median(crowned.map(i => -(raw[i].L + raw[i].R) / 2));
const stats = {
  stations: n, measured: meas.length, measuredPct: +(100 * meas.length / n).toFixed(1),
  sources: { m: src.filter(c => c === 'm').length, d: src.filter(c => c === 'd').length, s: src.filter(c => c === 's').length, e: src.filter(c => c === 'e').length },
  notConfident: whyCount,
  fitRmsCm: { p10: +(pct(0.1) * 100).toFixed(1), p50: +(pct(0.5) * 100).toFixed(1), p90: +(pct(0.9) * 100).toFixed(1), threshold: RMS_OK * 100 },
  straightsOver800m: {
    stations: straightC.length,
    crownedSharePct: +(100 * crowned.length / Math.max(1, straightC.length)).toFixed(0),
    crownMedianPct: pc(crownStraight),
    crownQuartilesPct: [pc(median(crowned.map(i => -(raw[i].L + raw[i].R) / 2).filter(v => v < crownStraight))),
                        pc(median(crowned.map(i => -(raw[i].L + raw[i].R) / 2).filter(v => v > crownStraight)))],
    tiltedMedianPct: pc(median(tilted.map(i => Math.abs(raw[i].L - raw[i].R) / 2))),
    chosenCrownMedianPct: pc(median(sel(isStraight).map(i => -(left[i] + right[i]) / 2))),
  },
  bendsUnder150m: {
    stations: sel(isBend).length, measuredStations: bendsM.length,
    measuredBankInPct: { median: pc(median(pick(bendsM, i => tiltIn(i, Tm[i])))), p98: pc(p98(pick(bendsM, i => tiltIn(i, Tm[i])))) },
    designBankInPct: { median: pc(median(sel(isBend).map(i => tiltIn(i, Td[i])))), max: pc(Math.max(...sel(isBend).map(i => tiltIn(i, Td[i])))) },
    chosenBankInPct: { median: pc(median(sel(isBend).map(chosenBank))), max: pc(Math.max(...sel(isBend).map(chosenBank))) },
    adverseRejected: whyCount.adverse || 0,
  },
  chosenMaxSlopePct: pc(Math.max(...Array.from(left, Math.abs), ...Array.from(right, Math.abs))),
  // how faithfully the written file keeps the measurement: measured stations whose written edge is more
  // than 0.5% off the measured one (the 12% clamp, a spike the measured rate cap cut, the route's ends)
  measuredMovedOver5Permille: sel(i => src[i] === 'm' && Math.max(Math.abs(left[i] - (Cm[i] + Tm[i])), Math.abs(right[i] - (Cm[i] - Tm[i]))) > 5).length,
  // the fastest twist: the largest change of an edge's height relative to the centre from one station to the next
  maxEdgeTwist: (() => {
    let w = 0, wi = 0;
    for (let i = 0; i < (loop ? n : n - 1); i++) { const j = (i + 1) % n, d = Math.max(Math.abs(left[j] - left[i]), Math.abs(right[j] - right[i])); if (d > w) { w = d; wi = i; } }
    return { permillePerStation: w, cmPer4m: +(w * half / 10).toFixed(1), wp: wi };
  })(),
  measuredRunoffM: { bends: runoffsM.length, median: median(runoffsM), p10: runoffsM.length ? runoffsM.slice().sort((a, b) => a - b)[Math.floor(runoffsM.length * 0.1)] : NaN },
  lidarCouldNotSay: unmeasured.map(([a, b]) => `${a}-${b}${src[a] === 's' ? ' structure' : ''}`),
  sharpest: describe(sharp),
};

// ----------------------------------------------------------------- write
const art = join(ROOT, 'Assets', 'PSXRacing', 'Art', st.art);
const outPath = join(art, `${pre}_bank.json`);
const doc = {
  schema: 1,
  what: 'Road cross-section per waypoint: the left and right tarmac edge height over the centre line as a cross slope, ' +
    'per mille of the half width ((y_edge - y_centre) / halfWidthM * 1000; + = above the centre). Left / right as the waypoints run.',
  baked: `tools/roads/bake_bank.mjs ${pre}`,
  credit: 'Measured from U.S. Geological Survey 3D Elevation Program 1 m lidar DEM ' + PROJECT_1M + ' (public domain)',
  spacing: SP, count: n, halfWidthM: half, unit: 'permille',
  left: Array.from(left), right: Array.from(right), source: src.join(''),
  stats, standards: STANDARDS,
};
const text = '{\n' + Object.entries(doc).map(([k, v]) => ` ${JSON.stringify(k)}: ${JSON.stringify(v)}`).join(',\n') + '\n}\n';
if (JSON.parse(readFileSync(join(S.resDir, `${pre}_stage.json`), 'utf8')).xyz.length / 3 !== n) throw new Error('count drifted');

console.log(`  measured ${stats.measuredPct}% of ${n} stations (m ${stats.sources.m}, design ${stats.sources.d}, structure ${stats.sources.s}, ends ${stats.sources.e}); ` +
  `fit RMS p50 ${stats.fitRmsCm.p50} cm, p90 ${stats.fitRmsCm.p90} cm; not confident: ${JSON.stringify(whyCount)}`);
{
  const s8 = stats.straightsOver800m;
  console.log(`  straights (R > 800 m, ${s8.stations} confident stations): ${s8.crownedSharePct}% crowned, crown median ${s8.crownMedianPct}% ` +
    `(quartiles ${s8.crownQuartilesPct.join(' / ')}%); the rest one tilted plane, median ${s8.tiltedMedianPct}%; chosen crown median ${s8.chosenCrownMedianPct}%`);
}
const bb = stats.bendsUnder150m;
console.log(`  bends under 150 m (${bb.stations} stations, ${bb.measuredStations} measured): bank toward the inside measured median ${bb.measuredBankInPct.median}% (p98 ${bb.measuredBankInPct.p98}%), ` +
  `design median ${bb.designBankInPct.median}% (max ${bb.designBankInPct.max}%), chosen median ${bb.chosenBankInPct.median}% (max ${bb.chosenBankInPct.max}%)`);
console.log(`  runoffs the lidar shows (20-80% of a measured bank over 4%): ${runoffsM.length} bends, median ${stats.measuredRunoffM.median} m`);
console.log(`  sharpest: ${JSON.stringify(stats.sharpest)}`);
console.log(`  lidar could not say (3+ stations): ${unmeasured.length ? unmeasured.map(([a, b]) => `wp ${a}-${b}${src[a] === 's' ? ' (structure)' : ''}`).join(', ') : 'nowhere'}`);

if (DUMP) {
  const rows = ['wp,m,R,kap,src,why,model,rawL,rawR,rms,Tm,Cm,Td,Cd,Vd,left,right'];
  for (let i = 0; i < n; i++) { const r = raw[i] || {}; rows.push([i, i * SP, radius(i).toFixed(1), kap[i].toFixed(5), src[i], why[i], r.model || '', r.L?.toFixed(1) ?? '', r.R?.toFixed(1) ?? '', r.rms?.toFixed(4) ?? '', Tm[i].toFixed(1), Cm[i].toFixed(1), Td[i].toFixed(1), Cd[i].toFixed(1), Vd[i].toFixed(1), left[i], right[i]].join(',')); }
  writeFileSync(DUMP, rows.join('\n') + '\n');
}

if (PLOT) {
  mkdirSync(PLOT, { recursive: true });
  // the sharpest bend, and a typical straight: a measured crowned station in the longest straight run
  let straight = -1, bestRun = 0;
  for (const [a, b] of runs(i => isStraight(i) && src[i] === 'm')) if (b - a > bestRun) { bestRun = b - a; straight = (a + b) >> 1; }
  // where the sharpest bend is not measured (a structure, a rejected station), also its sharpest measured one
  let sharpM = -1;
  if (src[sharp] !== 'm') for (let i = 0; i < n; i++) if (raced(i) && src[i] === 'm' && (sharpM < 0 || radius(i) < radius(sharpM))) sharpM = i;
  const cells = [['bend', sharp], ...(sharpM >= 0 ? [['bendm', sharpM]] : []), ...(straight >= 0 ? [['straight', straight]] : [])];
  for (const [kind, i] of cells) {
    const r = raw[i] && !raw[i].why ? raw[i] : null, Hc = S.h[i];
    const rows = ['u,ground,fit,game'];
    for (let u = -10; u <= 10 + 1e-9; u += 0.25) {
      const t = -u;                                         // u + = right of travel, t + = left
      const p = at(i, t, 0), g = sampleXZ(p.x, p.z);
      let fit = '';
      if (r) {
        const tw = t - r.c;
        if (Math.abs(tw) <= WIN + 1e-9) {
          const k = r.coef;
          fit = r.fitB ? k[0] + k[1] * Math.max(tw - r.e0, 0) + k[2] * Math.max(r.e0 - tw, 0) : k[0] + k[1] * tw;
          fit = fit.toFixed(3);
        }
      }
      let game = '';
      if (Math.abs(u) <= half + 1e-9) game = (Hc + (u < 0 ? -u * left[i] : u * right[i]) / 1000).toFixed(3);
      rows.push([u.toFixed(2), Number.isFinite(g) ? g.toFixed(3) : '', fit, game].join(','));
    }
    const info = { stage: pre, id: st.id, road: st.road, kind, wp: i, m: i * SP, radiusM: +radius(i).toFixed(1), turn: kap[i] > 0 ? 'left' : 'right',
      halfWidthM: half, source: src[i], model: r ? r.model : null, rmsCm: r ? +(r.rms * 100).toFixed(1) : null,
      measured: describe(i).measured, design: describe(i).design, chosen: describe(i).chosen, rawFit: describe(i).raw, centreOffsetM: r ? +r.c.toFixed(2) : null };
    writeFileSync(join(PLOT, `${pre}_${kind}.csv`), rows.join('\n') + '\n');
    writeFileSync(join(PLOT, `${pre}_${kind}.json`), JSON.stringify(info));
  }
  writeFileSync(join(PLOT, `${pre}_stats.json`), JSON.stringify(stats));
  console.log(`  plot data: ${cells.map(c => `${c[0]} wp ${c[1]}`).join(', ')} -> ${PLOT}`);
}

const norm = s => s.replace(/\r\n/g, '\n');
if (CHECK) {
  const have = existsSync(outPath) ? norm(readFileSync(outPath, 'utf8')) : null;
  const same = have === text;
  console.log(`  check ${pre}_bank.json: ${same ? 'same' : have === null ? 'MISSING' : 'DIFFERENT'}`);
  console.log(same ? 'CHECK OK: the file on disk is this bake' : 'CHECK FAILED: the file differs from a fresh bake');
  process.exit(same ? 0 : 1);
}
mkdirSync(art, { recursive: true });
writeFileSync(outPath, text);
const metaPath = outPath + '.meta';
if (!existsSync(metaPath))
  writeFileSync(metaPath, ['fileFormatVersion: 2', `guid: ${randomBytes(16).toString('hex')}`, 'TextScriptImporter:', '  externalObjects: {}',
    '  userData: ', '  assetBundleName: ', '  assetBundleVariant: ', ''].join('\r\n'));
console.log(`  wrote ${outPath}${existsSync(metaPath) ? '' : ''}`);
console.log('OK');
