// roadprofile.mjs - MEASURED ROAD PROFILES (section RPRF; plan B7 / FD-E,
// leftover item 1 "the dips under bridges", 2026-10-03).
//
// Until now every road read its height off the SMOOTHED LAND (the 60 m grid
// through a 48 m Gaussian, CityElevation.BuildRoadDem), which cannot see a
// 40-80 m freeway cut or an approach fill. So every grade separation was
// improvised: a 4.5% V dug under each street bridge (SinkTrenches; 213 V
// bottoms) or a tent raised over the road below. The real freeway runs in a
// CONTINUOUS cut (3DEP: 4.8 m under the ring land, local notch at a bridge
// +0.08 m) and real streets really dip under some bridges (W Trade St under
// I-77, 2.6 m). This samples the USGS 3DEP 1/3" bare earth along every tier-1
// and tier-2 edge (motorway .. tertiary and their links) at the solver's own
// stations and designs the profile offline, so the game only TOPS UP:
//
//   1. SAMPLE  each station: the median of 5 points across +/-min(3, hw-1) m.
//   2. MASK    what bare earth does not show: tagged bridges and tunnels (the
//              ground under a deck is the road below), the over road within
//              its deck's reach of a crossing, water spans (+6 m), the 15 m of
//              an approach next to a bridge's end node (the deck's start is
//              rarely where OSM ends it), and the under road where the road
//              over it is NOT a bridge (bare earth there is the over road's
//              fill). Masked runs are filled by a straight line between the
//              ground either side - a deck runs from abutment to abutment.
//   3. PAIRS   a divided road's two carriageways (one-way, same name, running
//              opposite, within 45 m) are pulled to within 0.3 m of each
//              other where both stand on the ground.
//   4. NODES   each junction's height from its arms' ground within 40 m,
//              extrapolated on their own grade to the node; nodes with no
//              ground near (a node between two decks) from their neighbours.
//   5. CHAINS  through each node's straightest pair (same name first), the
//              highest class first; a chain ends pinned at junction nodes a
//              higher chain already fixed. Each chain is smoothed (Gaussian,
//              sigma 1.5 stations) and limited to AASHTO curvature at the
//              owner's design speeds (owner_decisions: motorway 65, trunk and
//              primary 50, secondary/tertiary 40, ramps 40, loops 30 mph):
//              crests to the stopping-sight radius, sags to the HEADLIGHT
//              radius (the target; the comfort radius is the fail line).
//
// The output is per edge: the station heights (world y = metres above the
// 97.0 m datum) and the masked runs (the game treats every other station as
// measured GROUND: never a deck by the 3.5 m margin). Layout in
// tools/city/lib/citydata.mjs (RPRF). Public domain data; credit "U.S.
// Geological Survey, 3D Elevation Program".

import { pointAt, tangentAt, projectOn, verticalMph } from './vertical.mjs';

export const RPRF_STEP = 10;                 // CityElevation.StationStep
const ACROSS_M = 3;                          // half the cross-section sampled
const BANK_M = 6;                            // dry bank masked past a water span
const ABUT_M = 15;                           // an approach masked next to a bridge node
const ABUT_FAR_M = 100;                      // ... and on while the ground falls too steeply
/// The class's steepest grade (CityAudit.ClassMaxGrade): links 8%, motorway
/// 5%, trunk/primary 7%, secondary/tertiary 9%, local 12%.
const classMaxGrade = e => e.link ? 0.08 : e.rank >= 5 ? 0.05 : e.rank >= 3 ? 0.07 : e.rank >= 1 ? 0.09 : 0.12;
const NODE_REACH_M = 40;                     // a node's height from ground this close
const PAIR_REACH_M = 45, PAIR_DY_M = 0.3;    // carriageway pairs
const SIGMA_ST = 1.5;                        // Gaussian, in stations
const GRADE_CAP = 1.6;                       // x the class's steepest grade
const FT = 0.3048;

// AASHTO Green Book (2018) K values by design speed (ft per % of A)
const K_MPH = [20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70];
const K_SAG_HEAD = [17, 26, 37, 49, 64, 79, 96, 115, 136, 157, 181];
const K_CREST = [7, 12, 19, 29, 44, 61, 84, 114, 151, 193, 247];
const kAt = (tab, mph) => {
  if (mph <= K_MPH[0]) return tab[0];
  for (let i = 1; i < K_MPH.length; i++) if (mph <= K_MPH[i]) return tab[i - 1] + (tab[i] - tab[i - 1]) * (mph - K_MPH[i - 1]) / (K_MPH[i] - K_MPH[i - 1]);
  return tab[tab.length - 1];
};
/// Radius (m) of a K (ft/%): R = 100 K ft.
const radiusOfK = K => K * 100 * FT;
export const crestRadius = mph => radiusOfK(kAt(K_CREST, mph));
export const sagHeadlightRadius = mph => radiusOfK(kAt(K_SAG_HEAD, mph));
export const sagComfortRadius = mph => radiusOfK(mph * mph / 46.5);

/// Which edges carry a measured profile: tier 1 and tier 2 (rank >= 1).
export const measuredRank = e => e.rank >= 1;

/// The solver's station layout (CityElevation.Solve step 1).
export function stationsOf(e) {
  const n = Math.max(2, Math.ceil(e.length / RPRF_STEP) + 1);
  const s = new Float64Array(n);
  for (let i = 0; i < n; i++) s[i] = i === n - 1 ? e.length : i * e.length / (n - 1);
  return s;
}

/// The cross-section median at arc s (world y), and the 5 samples' spread.
export function sampleSection(e, s, groundY) {
  const p = pointAt(e, s), t = tangentAt(e, s);
  const half = Math.max(0, Math.min(ACROSS_M, e.hw - 1));
  const v = [];
  for (const o of [-1, -0.5, 0, 0.5, 1]) v.push(groundY(p[0] + t[1] * o * half, p[1] - t[0] * o * half));
  v.sort((a, b) => a - b);
  return v[2];
}

/// THE GRADE CAP: no station-to-station grade past the class's steepest
/// (CityAudit.ClassMaxGrade) x GRADE_CAP - or, between two pinned stations
/// that need more, their own average x 1.05 (CityElevation's interior easing
/// rule). What the lidar reads steeper is not the road (an approach's side
/// slope where OSM's line runs beside it). Forward and back sweeps, pins held.
/// Returns how many stations moved.
function clampGrades(d, y, pinned, gmax) {
  const n = y.length; let moved = 0;
  let a = 0;
  while (a < n - 1) {
    let b = a + 1; while (b < n - 1 && !pinned[b]) b++;
    const need = Math.abs(y[b] - y[a]) / Math.max(1, d[b] - d[a]) * 1.05;
    for (let pass = 0; pass < 2; pass++) {
      for (let i = a + 1; i < b; i++) { const g = Math.max(gmax[i], need), h = d[i] - d[i - 1]; const v = Math.min(y[i - 1] + g * h, Math.max(y[i - 1] - g * h, y[i])); if (v !== y[i]) { y[i] = v; moved++; } }
      for (let i = b - 1; i > a; i--) { const g = Math.max(gmax[i], need), h = d[i + 1] - d[i]; const v = Math.min(y[i + 1] + g * h, Math.max(y[i + 1] - g * h, y[i])); if (v !== y[i]) { y[i] = v; moved++; } }
    }
    a = b;
  }
  return moved;
}

function smoothGauss(y, sigma, pinned) {
  const n = y.length; if (n < 4) return;
  const r = Math.ceil(sigma * 2.5), o = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    if (pinned[i]) { o[i] = y[i]; continue; }
    let s = 0, w = 0;
    for (let k = -r; k <= r; k++) {
      const j = i + k; if (j < 0 || j >= n) continue;
      const q = Math.exp(-k * k / (2 * sigma * sigma)); s += y[j] * q; w += q;
    }
    o[i] = s / w;
  }
  for (let i = 0; i < n; i++) y[i] = o[i];
}

/// THE CURVATURE LIMITER: every break between two stations' grades within
/// -(h1+h2)/(2 Rc) .. +(h1+h2)/(2 Rs). Projections onto each violated
/// constraint (moving the three stations along its normal, pinned ones held),
/// swept both ways until nothing is out by more than 0.02% (or the sweep cap).
/// Returns { sweeps, worst } (worst: the largest violation left, as a grade).
function limitCurvature(d, y, pinned, Rc, Rs, maxSweeps = 4000) {
  const n = y.length;
  if (n < 3) return { sweeps: 0, worst: 0 };
  let worst = 0, sweep = 0;
  const tol = 0.0002;
  const relax = (k) => {
    const h1 = d[k] - d[k - 1], h2 = d[k + 1] - d[k];
    if (h1 < 0.3 || h2 < 0.3) return 0;
    const a0 = 1 / h1, a1 = -(1 / h1 + 1 / h2), a2 = 1 / h2;
    const brk = a0 * y[k - 1] + a1 * y[k] + a2 * y[k + 1];
    const lo = -(h1 + h2) / (2 * Rc[k]), hi = (h1 + h2) / (2 * Rs[k]);
    let ex = 0;
    if (brk > hi) ex = brk - hi; else if (brk < lo) ex = brk - lo; else return 0;
    const w0 = pinned[k - 1] ? 0 : 1, w1 = pinned[k] ? 0 : 1, w2 = pinned[k + 1] ? 0 : 1;
    const nn = w0 * a0 * a0 + w1 * a1 * a1 + w2 * a2 * a2;
    if (nn <= 0) return Math.abs(ex);
    const f = ex / nn;
    y[k - 1] -= w0 * f * a0; y[k] -= w1 * f * a1; y[k + 1] -= w2 * f * a2;
    return Math.abs(ex);
  };
  for (sweep = 0; sweep < maxSweeps; sweep++) {
    worst = 0;
    for (let k = 1; k < n - 1; k++) worst = Math.max(worst, relax(k));
    for (let k = n - 2; k >= 1; k--) worst = Math.max(worst, relax(k));
    if (worst < tol) break;
  }
  return { sweeps: sweep, worst };
}

/// Build every measured profile. city: citydata.parseCity's result;
/// groundY(x, z): 3DEP bare earth in world y (metres above the datum).
/// Returns { prof: Array(edge) of null | { y: Float64Array, mask: Uint8Array },
///           stats, lines }.
export function buildRoadProfiles(city, groundY) {
  const { edges, nodes, nodeEdges, crossings, wspans } = city;
  const NE = edges.length, NN = nodes.length;
  const sel = edges.map(e => measuredRank(e) && e.a !== e.b && e.length >= 0.5);
  const st = new Array(NE).fill(null);        // { s, raw, y, mask }
  const lines = [];
  let nSt = 0;
  // ---- 1. sample
  for (const e of edges) {
    if (!sel[e.index]) continue;
    const s = stationsOf(e), n = s.length;
    const raw = new Float64Array(n), mask = new Uint8Array(n);
    for (let i = 0; i < n; i++) raw[i] = sampleSection(e, s[i], groundY);
    st[e.index] = { s, raw, y: Float64Array.from(raw), mask };
    nSt += n;
  }
  // ---- 2. masks
  const why = { bridge: 0, tunnel: 0, water: 0, over: 0, abut: 0, underFill: 0, steep: 0 };
  const maskRange = (ei, lo, hi, k) => {
    const t = st[ei]; if (!t) return;
    for (let i = 0; i < t.s.length; i++) if (t.s[i] >= lo && t.s[i] <= hi && !t.mask[i]) { t.mask[i] = k; why[['', 'bridge', 'tunnel', 'water', 'over', 'abut', 'underFill', 'steep'][k]]++; }
  };
  for (const e of edges) {
    if (!st[e.index]) continue;
    if (e.bridge) maskRange(e.index, -1, e.length + 1, 1);
    else if (e.tunnel) maskRange(e.index, -1, e.length + 1, 2);
  }
  for (const w of wspans) maskRange(w.edge, w.s0 - BANK_M, w.s1 + BANK_M, 3);
  const bridgeNode = new Uint8Array(NN);
  for (const e of edges) if (e.bridge) { bridgeNode[e.a] = 1; bridgeNode[e.b] = 1; }
  for (const e of edges) {
    if (!st[e.index] || e.bridge) continue;
    if (bridgeNode[e.a]) maskRange(e.index, -1, ABUT_M, 5);
    if (bridgeNode[e.b]) maskRange(e.index, e.length - ABUT_M, e.length + 1, 5);
    // OSM often ends a bridge short of the real deck: past the 15 m, the
    // ground under the deck still falls away steeper than the class could
    // be built (Tuckaseegee Road's approach to its bridge: 18-25% "grades"
    // that were the valley under the deck). Masked on out to 100 m while it does.
    const t = st[e.index], n = t.s.length, gmax = classMaxGrade(e) + 0.02;
    for (const atA of [true, false]) {
      if (!bridgeNode[atA ? e.a : e.b]) continue;
      for (let q = 0; q + 1 < n; q++) {
        const i = atA ? q : n - 1 - q, j = atA ? q + 1 : n - 2 - q;
        const dist = atA ? t.s[i] : e.length - t.s[i];
        if (dist > ABUT_FAR_M) break;
        const g = Math.abs(t.raw[j] - t.raw[i]) / Math.max(0.5, Math.abs(t.s[j] - t.s[i]));
        if (g > gmax) { if (!t.mask[i]) { t.mask[i] = 5; why.abut++; } if (!t.mask[j]) { t.mask[j] = 5; why.abut++; } }
        else if (dist >= ABUT_M) break;
      }
    }
  }
  // anything steeper than the class could be built by 5% is not the road:
  // a deck OSM left untagged, a retaining wall, a culvert's headwall
  for (const e of edges) {
    const t = st[e.index]; if (!t || e.bridge || e.tunnel) continue;
    const gcap = classMaxGrade(e) + 0.05;
    for (let i = 0; i + 1 < t.s.length; i++) {
      const g = Math.abs(t.raw[i + 1] - t.raw[i]) / Math.max(0.5, t.s[i + 1] - t.s[i]);
      if (g <= gcap) continue;
      for (const k of [i, i + 1]) if (!t.mask[k]) { t.mask[k] = 7; why.steep++; }
    }
  }
  for (const c of crossings) {
    const O = edges[c.over], U = edges[c.under];
    const at = [c.x, c.z];
    const sO = projectOn(O, at), sU = projectOn(U, at);
    const tO = tangentAt(O, sO), tU = tangentAt(U, sU);
    const cos = tO[0] * tU[0] + tO[1] * tU[1];
    const sin = Math.max(0.4, Math.sqrt(Math.max(0, 1 - cos * cos)));
    if (st[O.index] && !O.bridge) maskRange(O.index, sO - (U.hw + O.hw + 8) / sin, sO + (U.hw + O.hw + 8) / sin, 4);
    if (st[U.index] && !O.bridge) maskRange(U.index, sU - (O.hw + 4) / sin, sU + (O.hw + 4) / sin, 6);
  }
  // ---- 3. pairs: the two carriageways of a divided road within 0.3 m
  let pairMoved = 0, pairStations = 0;
  {
    const cell = 50, grid = new Map();
    const key = (x, z) => Math.floor(x / cell) * 100003 + Math.floor(z / cell);
    const pts = [];   // [ei, i, x, z, tx, tz]
    for (const e of edges) {
      const t = st[e.index]; if (!t || !e.oneway || e.link || !e.name) continue;
      for (let i = 0; i < t.s.length; i++) {
        if (t.mask[i]) continue;
        const p = pointAt(e, t.s[i]), g = tangentAt(e, t.s[i]);
        const k = key(p[0], p[1]); let l = grid.get(k); if (!l) grid.set(k, l = []);
        l.push(pts.length); pts.push([e.index, i, p[0], p[1], g[0], g[1]]);
      }
    }
    const shift = new Float64Array(pts.length), cnt = new Uint16Array(pts.length);
    for (let a = 0; a < pts.length; a++) {
      const [ei, i, x, z, tx, tz] = pts[a];
      const name = edges[ei].name;
      let best = -1, bd = Infinity;
      for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) {
        const l = grid.get((Math.floor(x / cell) + dx) * 100003 + Math.floor(z / cell) + dz); if (!l) continue;
        for (const b of l) {
          const q = pts[b]; if (q[0] === ei || edges[q[0]].name !== name) continue;
          if (tx * q[4] + tz * q[5] > -0.7) continue;
          const along = Math.abs((q[2] - x) * tx + (q[3] - z) * tz);
          if (along > 6) continue;
          const dd = Math.hypot(q[2] - x, q[3] - z);
          if (dd <= PAIR_REACH_M && dd < bd) { bd = dd; best = b; }
        }
      }
      if (best < 0) continue;
      const q = pts[best];
      const ya = st[ei].y[i], yb = st[q[0]].y[q[1]], dy = ya - yb;
      pairStations++;
      if (Math.abs(dy) <= PAIR_DY_M) continue;
      shift[a] += -Math.sign(dy) * (Math.abs(dy) - PAIR_DY_M) / 2; cnt[a]++;
    }
    for (let a = 0; a < pts.length; a++) if (cnt[a]) { const [ei, i] = pts[a]; st[ei].y[i] += shift[a] / cnt[a]; pairMoved++; }
  }
  // ---- fill masked interior runs (straight between the ground either side)
  for (const e of edges) {
    const t = st[e.index]; if (!t) continue;
    const n = t.s.length;
    let i = 0;
    while (i < n) {
      if (!t.mask[i]) { i++; continue; }
      let j = i; while (j < n && t.mask[j]) j++;
      if (i > 0 && j < n) for (let k = i; k < j; k++) t.y[k] = t.y[i - 1] + (t.y[j] - t.y[i - 1]) * (t.s[k] - t.s[i - 1]) / (t.s[j] - t.s[i - 1]);
      else for (let k = i; k < j; k++) t.y[k] = NaN;   // runs to an end: from the node
      i = j;
    }
  }
  // ---- 4. node heights from the ground within 40 m of each node
  const nodeY = new Float64Array(NN).fill(NaN);
  let nodesMeasured = 0, nodesFilled = 0;
  for (let nd = 0; nd < NN; nd++) {
    let sum = 0, w = 0;
    for (const ei of nodeEdges[nd]) {
      const e = edges[ei], t = st[ei]; if (!t) continue;
      const atA = e.a === nd;
      // ground stations within reach, distance from the node
      const xs = [], ys = [];
      for (let i = 0; i < t.s.length; i++) {
        if (t.mask[i]) continue;
        const dist = atA ? t.s[i] : e.length - t.s[i];
        if (dist <= NODE_REACH_M) { xs.push(dist); ys.push(t.y[i]); }
      }
      if (!xs.length) continue;
      let v;
      const near = xs.reduce((m, x, k) => x < xs[m] ? k : m, 0);
      if (xs[near] <= 3 || xs.length < 2) v = ys[near];
      else {
        // straight line through the ground, read at the node (never past +/-1.5 m of the nearest)
        let mx = 0, my = 0; for (let k = 0; k < xs.length; k++) { mx += xs[k]; my += ys[k]; } mx /= xs.length; my /= xs.length;
        let sxx = 0, sxy = 0; for (let k = 0; k < xs.length; k++) { sxx += (xs[k] - mx) ** 2; sxy += (xs[k] - mx) * (ys[k] - my); }
        const g = sxx > 1e-6 ? sxy / sxx : 0;
        v = my - g * mx;
        v = Math.max(ys[near] - 1.5, Math.min(ys[near] + 1.5, v));
      }
      const wt = (1 + e.rank) * (e.link ? 0.5 : 1);
      sum += v * wt; w += wt;
    }
    if (w > 0) { nodeY[nd] = sum / w; nodesMeasured++; }
  }
  // nodes with no ground near: from their measured neighbours, length-weighted
  for (let it = 0; it < 200; it++) {
    let changed = 0;
    for (let nd = 0; nd < NN; nd++) {
      if (!Number.isNaN(nodeY[nd])) continue;
      let sum = 0, w = 0;
      for (const ei of nodeEdges[nd]) {
        const e = edges[ei]; if (!st[ei]) continue;
        const o = e.a === nd ? e.b : e.a;
        if (Number.isNaN(nodeY[o])) continue;
        const wt = 1 / Math.max(1, e.length);
        sum += nodeY[o] * wt; w += wt;
      }
      if (w > 0) { nodeY[nd] = sum / w; changed++; nodesFilled++; }
    }
    if (!changed) break;
  }
  // ---- 4b. node heights within the grade cap of each other: two junctions
  // a short measured edge apart cannot stand further apart than the class
  // could climb between them (N Sharon Amity Rd's 10 m piece between two
  // Holbrook Dr junctions read 17.6% from two arms' extrapolations). Both
  // move halfway, a few sweeps.
  let nodeGradeMoves = 0;
  for (let it = 0; it < 30; it++) {
    let moved = 0;
    for (const e of edges) {
      if (!st[e.index] || e.a === e.b) continue;
      const ya = nodeY[e.a], yb = nodeY[e.b];
      if (Number.isNaN(ya) || Number.isNaN(yb)) continue;
      const cap = classMaxGrade(e) * GRADE_CAP * Math.max(1, e.length) * 0.98, dy = yb - ya;
      if (Math.abs(dy) <= cap) continue;
      const fix = (Math.abs(dy) - cap) / 2 * Math.sign(dy);
      nodeY[e.a] += fix; nodeY[e.b] -= fix; moved++;
    }
    nodeGradeMoves += moved;
    if (!moved) break;
  }
  // ---- 5. chains through the straightest pairs, highest class first
  const partner = new Map();
  for (let nd = 0; nd < NN; nd++) {
    const arms = nodeEdges[nd].filter(i => st[i]).map(i => edges[i]);
    if (arms.length < 2) continue;
    const dirAt = e => e.a === nd ? tangentAt(e, 0) : tangentAt(e, e.length).map(v => -v);
    const cand = [];
    for (let p = 0; p < arms.length; p++) for (let q = p + 1; q < arms.length; q++) {
      const dp = dirAt(arms[p]), dq = dirAt(arms[q]), dot = dp[0] * dq[0] + dp[1] * dq[1];
      if (!(arms.length === 2 ? dot < 0.3 : dot < -0.7)) continue;
      cand.push([dot - (arms[p].name && arms[p].name === arms[q].name ? 0.5 : 0) - (arms[p].rank === arms[q].rank ? 0.2 : 0) - (arms[p].link === arms[q].link ? 0.2 : 0), arms[p].index, arms[q].index]);
    }
    cand.sort((a, b) => a[0] - b[0]);
    const used = new Set();
    for (const [, P, Q] of cand) { if (used.has(P) || used.has(Q)) continue; used.add(P); used.add(Q); partner.set(P + ':' + nd, Q); partner.set(Q + ':' + nd, P); }
  }
  const seen = new Uint8Array(NE), chains = [];
  for (const e0 of edges) {
    if (!st[e0.index] || seen[e0.index]) continue;
    let e = e0, node = e0.a, guard = 0;
    while (guard++ < 20000) { const p = partner.get(e.index + ':' + node); if (p === undefined || p === e0.index || seen[p]) break; const o = edges[p]; node = o.a === node ? o.b : o.a; e = o; if (e === e0) break; }
    const list = []; let at = node; guard = 0;
    while (e && !seen[e.index] && guard++ < 20000) {
      seen[e.index] = 1;
      const fwd = e.a === at; list.push([e.index, fwd]);
      const far = fwd ? e.b : e.a, p = partner.get(e.index + ':' + far);
      at = far; e = p === undefined || seen[p] ? null : edges[p];
    }
    let rank = 0, len = 0; for (const [ei] of list) { rank = Math.max(rank, edges[ei].rank * 2 + (edges[ei].link ? 0 : 1)); len += edges[ei].length; }
    chains.push({ list, rank, len });
  }
  chains.sort((a, b) => b.rank - a.rank || b.len - a.len);
  const fixedNode = new Uint8Array(NN), done = new Uint8Array(NE);
  let sweepsMax = 0, sweepsSum = 0, leftOver = 0, pinsTotal = 0, gradesClamped = 0, ghosts = 0;
  // THE ROAD ON THROUGH A JUNCTION: a chain ending at a node a higher chain
  // fixed is curved INTO that road's grade - the next station of the arm it
  // runs straight on into (already designed) is a pinned ghost beyond the
  // node, so the limiter bends this approach, not the junction (N Davidson
  // St met its node at +9.3% where the road on was +1.6%: a launch at speed).
  const ghostAt = (nd, ei0, fromStart) => {
    const e0 = edges[ei0];
    const d0 = e0.a === nd ? tangentAt(e0, 0) : tangentAt(e0, e0.length).map(v => -v);
    let best = null, bdot = -0.7;
    for (const oi of nodeEdges[nd]) {
      if (oi === ei0 || !done[oi]) continue;
      const o = edges[oi], t = st[oi]; if (!t || o.a === o.b || t.s.length < 2) continue;
      const dO = o.a === nd ? tangentAt(o, 0) : tangentAt(o, o.length).map(v => -v);
      const dot = d0[0] * dO[0] + d0[1] * dO[1];
      if (dot < bdot) { bdot = dot; const k = o.a === nd ? 1 : t.s.length - 2; best = { h: o.a === nd ? t.s[k] : o.length - t.s[k], y: t.y[k] }; }
    }
    return best;
  };
  for (const ch of chains) {
    // the chain's stations in travel order
    const D = [], Y = [], P = [], Rc = [], Rs = [], G = [], ref = [];
    let base = 0;
    for (let k = 0; k < ch.list.length; k++) {
      const [ei, fwd] = ch.list[k], e = edges[ei], t = st[ei], n = t.s.length;
      const startNode = fwd ? e.a : e.b, endNode = fwd ? e.b : e.a;
      const mph = verticalMph(e), rc = crestRadius(mph), rs = sagHeadlightRadius(mph);
      for (let q = 0; q < n; q++) {
        const i = fwd ? q : n - 1 - q;
        if (q === 0 && k > 0) { ref[ref.length - 1].push([ei, i]); continue; }   // the shared node station
        const along = fwd ? t.s[i] : e.length - t.s[i];
        const isNode = q === 0 || q === n - 1;
        const nd = q === 0 ? startNode : q === n - 1 ? endNode : -1;
        let y = t.y[i];
        if (isNode && !Number.isNaN(nodeY[nd])) y = nodeY[nd];
        D.push(base + along); Y.push(y); Rc.push(rc); Rs.push(rs); G.push(classMaxGrade(e) * GRADE_CAP); ref.push([[ei, i]]);
        // pinned: the chain's two ends, and nodes a higher chain fixed
        P.push(isNode && ((k === 0 && q === 0) || (k === ch.list.length - 1 && q === n - 1) || fixedNode[nd]) ? 1 : 0);
      }
      base += e.length;
    }
    // the ghosts beyond fixed end nodes (pinned, written nowhere)
    {
      const [e0i, f0] = ch.list[0], [eLi, fL] = ch.list[ch.list.length - 1];
      const n0 = f0 ? edges[e0i].a : edges[e0i].b, nL = fL ? edges[eLi].b : edges[eLi].a;
      if (fixedNode[n0] && D.length >= 2) {
        const g = ghostAt(n0, e0i, true);
        if (g && g.h > 0.5) { D.unshift(-g.h); Y.unshift(g.y); P.unshift(1); Rc.unshift(Rc[0]); Rs.unshift(Rs[0]); G.unshift(G[0]); ref.unshift([]); ghosts++; }
      }
      if (fixedNode[nL] && D.length >= 2 && !(n0 === nL)) {
        const g = ghostAt(nL, eLi, false);
        if (g && g.h > 0.5) { D.push(D[D.length - 1] + g.h); Y.push(g.y); P.push(1); Rc.push(Rc[Rc.length - 1]); Rs.push(Rs[Rs.length - 1]); G.push(G[G.length - 1]); ref.push([]); ghosts++; }
      }
    }
    const n = Y.length;
    // ends with no height: the nearest known along the chain
    for (let i = 0; i < n; i++) if (Number.isNaN(Y[i])) {
      let j = i; while (j < n && Number.isNaN(Y[j])) j++;
      const a = i > 0 ? Y[i - 1] : NaN, b = j < n ? Y[j] : NaN;
      for (let k = i; k < j; k++) Y[k] = Number.isNaN(a) ? b : Number.isNaN(b) ? a : a + (b - a) * (D[k] - D[i - 1]) / (D[j] - D[i - 1]);
      i = j;
    }
    if (Y.some(v => Number.isNaN(v))) continue;
    for (const p of P) pinsTotal += p;
    smoothGauss(Y, SIGMA_ST, P);
    gradesClamped += clampGrades(D, Y, P, G);
    const r = limitCurvature(D, Y, P, Rc, Rs);
    gradesClamped += clampGrades(D, Y, P, G);   // and again: the curves must not steepen past the cap
    sweepsMax = Math.max(sweepsMax, r.sweeps); sweepsSum += r.sweeps;
    if (r.worst > 0.001) leftOver++;
    for (let i = 0; i < n; i++) for (const [ei, si] of ref[i]) st[ei].y[si] = Y[i];
    for (const [ei] of ch.list) done[ei] = 1;
    // fix this chain's nodes for every chain after it
    for (let k = 0; k < ch.list.length; k++) {
      const [ei] = ch.list[k], e = edges[ei], t = st[ei];
      for (const [nd, si] of [[e.a, 0], [e.b, t.s.length - 1]]) if (!fixedNode[nd]) { fixedNode[nd] = 1; nodeY[nd] = t.y[si]; }
    }
  }
  // every edge's end stations ARE its nodes' heights
  for (const e of edges) {
    const t = st[e.index]; if (!t) continue;
    if (!Number.isNaN(nodeY[e.a])) t.y[0] = nodeY[e.a];
    if (!Number.isNaN(nodeY[e.b])) t.y[t.y.length - 1] = nodeY[e.b];
  }
  // ---- stats: deviation from the raw ground on measured stations
  const dev = [];
  for (const e of edges) { const t = st[e.index]; if (!t) continue; for (let i = 0; i < t.s.length; i++) if (!t.mask[i]) dev.push(Math.abs(t.y[i] - t.raw[i])); }
  dev.sort((a, b) => a - b);
  const pct = q => dev.length ? dev[Math.min(dev.length - 1, Math.floor(q * dev.length))] : 0;
  const nEdges = st.filter(Boolean).length;
  let masked = 0; for (const t of st) if (t) for (const m of t.mask) if (m) masked++;
  const stats = { edges: nEdges, stations: nSt, masked, why, nodesMeasured, nodesFilled, chains: chains.length, sweepsMax, sweepsMean: chains.length ? sweepsSum / chains.length : 0, leftOver, pinsTotal,
                  pairStations, pairMoved, devP50: pct(0.5), devP95: pct(0.95), devMax: dev.length ? dev[dev.length - 1] : 0 };
  lines.push(`RPRF: ${nEdges} edges (tier 1 + 2), ${nSt} stations, ${masked} masked (bridge ${why.bridge}, tunnel ${why.tunnel}, water ${why.water}, over a crossing ${why.over}, abutment ${why.abut}, under a fill ${why.underFill}, steeper than the class + 5% ${why.steep}); ` +
             `nodes ${nodesMeasured} from ground + ${nodesFilled} from neighbours; pairs ${pairStations} stations, ${pairMoved} pulled to ${PAIR_DY_M} m; ${chains.length} chains, limiter sweeps mean ${stats.sweepsMean.toFixed(1)} max ${sweepsMax}, ${leftOver} chains not fully limited (held by pins); ${gradesClamped} station moves by the grade cap (class max x ${GRADE_CAP}), ${nodeGradeMoves} node moves; ${ghosts} junction ghosts; ` +
             `designed vs measured ground p50 ${stats.devP50.toFixed(3)} / p95 ${stats.devP95.toFixed(3)} / max ${stats.devMax.toFixed(2)} m`);
  return { prof: st.map(t => t ? { y: t.y, mask: t.mask, raw: t.raw, s: t.s } : null), stats, lines };
}

/// RPRF bytes (lib/citydata.mjs documents the layout): u32 count, then per
/// edge u32 edge, u16 n, i16 y0 (cm above the datum), n-1 deltas as i8 cm (an
/// i8 of -128 escapes to an i16), u8 masked runs, each u16 first, u16 last.
export function writeRprf(w, prof) {
  const rows = [];
  for (let ei = 0; ei < prof.length; ei++) if (prof[ei]) rows.push(ei);
  w.u32(rows.length);
  for (const ei of rows) {
    const { y, mask } = prof[ei], n = y.length;
    if (n > 65535) throw new Error(`RPRF: e${ei} has ${n} stations`);
    w.u32(ei); w.u16(n);
    let prev = Math.round(y[0] * 100);
    if (prev < -32767 || prev > 32767) throw new Error(`RPRF: e${ei} starts at ${y[0]} m`);
    w.i16(prev);
    for (let i = 1; i < n; i++) {
      const q = Math.round(y[i] * 100), d = q - prev;
      if (d >= -127 && d <= 127) w.i8(d);
      else { w.i8(-128); if (d < -32767 || d > 32767) throw new Error(`RPRF: e${ei} step ${d} cm`); w.i16(d); }
      prev = q;
    }
    const runs = [];
    for (let i = 0; i < n; i++) { if (!mask[i]) continue; let j = i; while (j + 1 < n && mask[j + 1]) j++; runs.push([i, j]); i = j; }
    if (runs.length > 255) throw new Error(`RPRF: e${ei} has ${runs.length} masked runs`);
    w.u8(runs.length);
    for (const [a, b] of runs) { w.u16(a); w.u16(b); }
  }
}
