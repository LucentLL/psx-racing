// plan_smooth.mjs - a mountain stage's plan line without waves (owner, 2026-11-16: "an otherwise straight road will
// wave back and forth abruptly in a way that DOT would never approve").
//
// Used by bake_3dep.mjs --plan-smooth <m> [--refits] on the mapped line (--keep-line), and on its own for trying a
// line without re-baking heights:
//
//   node tools/roads/plan_smooth.mjs <pre> <out_stage.json> [--sigma 8] [--refits]
//     reads tools/roads/srtm_bakes/<pre>_srtm/<pre>_stage.json (the mapped line), writes the smoothed plan with the
//     input's heights carried by index. tools/roads/waves.py and plan_wiggle.py measure what is left.
//
// 1. planSmooth - the Lake Lure gentle-stretch gaussian (lakelure 7a9f964d, fetch_road.mjs planSmoothM), as a tool of
//    its own so fetch_road.mjs and fetch_loop.mjs are not touched. A mapped line carries a metre of digitising jog - two
//    bends of R 100 m the opposite ways inside 20 m - which the builder's spline faithfully draws. A gaussian of sigma
//    planSmoothM along the line is applied in full where the road bends gently (R over 120 m) and not at all in a real
//    bend (R under 50 m); the blend follows the TIGHTEST curvature within 3 sigma, so a bend's approach keeps its line
//    too, and the blend is itself smoothed so nothing switches abruptly. On a gentle curve it moves the line by
//    sigma^2 / 2R, a few centimetres; a 20 m jog loses nearly all of itself. An open stage keeps its ends (nothing
//    within 3 sigma of the first or last point moves); a loop wraps.
//    Unlike fetch_road.mjs it does NOT re-space the line: every waypoint keeps its index. The game reads a place given
//    in metres along a stage as waypoint m / spacing (TrackCatalog sprint cuts, rails, start and finish, bridges), so
//    each of them stays where it was, to the size of the move itself.
//
// 2. localRefits - at an S the gaussian is too short to take out (a jog spread over 40-80 m), the line inside the S
//    is replaced by the quadratic fitted (in its chord frame) to the stretch around it, about 200 m, blended back with
//    a quintic (C2) taper over 72 m on each side: the investigation's variant L. A move over 1 m is refused (that is a
//    real bend inside the window, not a jog in a straight). The places come from tools/roads/plan_refits.json, which
//    tools/roads/waves.py --refits writes after reading the 1 m lidar shelf's two edges: an S that both edges carry is
//    the real road, and an S on a mapped bridge is the bridge; neither is listed.
//    A move of W metres over a taper L meets L >= max(45 W, 61 m) and L >= 1.5 W S at S = 45 mph (G1 rural floor and
//    G4) for W up to 1.07 m, and the whole refit is longer than 2 L.

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ss = (e0, e1, x) => { const t = Math.min(1, Math.max(0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t); };
const q5 = t => { t = Math.min(1, Math.max(0, t)); return t * t * t * (10 - 15 * t + 6 * t * t); };

/// The gentle-stretch gaussian. wp: [{x, z}] at `spacing` m. Returns { wp, maxMove, weight }.
export function planSmooth(wp, { sigmaM = 8, spacing = 4, loop = false, rFull = 120, rNone = 50 } = {}) {
  const n = wp.length, sig = sigmaM / spacing, R = Math.ceil(3 * sig);
  const at = k => (loop ? ((k % n) + n) % n : Math.min(n - 1, Math.max(0, k)));
  const kern = []; for (let j = -R; j <= R; j++) kern.push(Math.exp(-0.5 * (j / sig) ** 2));
  const blur = get => {
    const o = new Float64Array(n);
    for (let i = 0; i < n; i++) { let a = 0, w = 0; for (let j = -R; j <= R; j++) { a += get(at(i + j)) * kern[j + R]; w += kern[j + R]; } o[i] = a / w; }
    return o;
  };
  const sx = blur(i => wp[i].x), sz = blur(i => wp[i].z);
  // curvature from the circle through the waypoints 12 m either side (3 stations at 4 m)
  const curv = i => {
    if (!loop && (i < 3 || i >= n - 3)) return 0;
    const a = wp[at(i - 3)], b = wp[i], c = wp[at(i + 3)];
    const cr = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
    const l = Math.hypot(b.x - c.x, b.z - c.z) * Math.hypot(a.x - c.x, a.z - c.z) * Math.hypot(a.x - b.x, a.z - b.z);
    return l > 0 ? Math.abs(2 * cr / l) : 0;
  };
  const w0 = new Float64Array(n);
  for (let i = 0; i < n; i++) w0[i] = (!loop && (i < R || i >= n - R)) ? 0 : 1 - ss(1 / rFull, 1 / rNone, curv(i));
  const w1 = new Float64Array(n);
  for (let i = 0; i < n; i++) { let m = 1; for (let j = -R; j <= R; j++) m = Math.min(m, w0[at(i + j)]); w1[i] = m; }
  const weight = blur(i => w1[i]);
  let maxMove = 0, maxAt = 0;
  const out = wp.map((p, i) => {
    const q = { x: p.x + (sx[i] - p.x) * weight[i], z: p.z + (sz[i] - p.z) * weight[i] };
    const d = Math.hypot(q.x - p.x, q.z - p.z); if (d > maxMove) { maxMove = d; maxAt = i; }
    return q;
  });
  return { wp: out, maxMove, maxAt: maxAt * spacing, weight };
}

/// Symmetric positive definite solve (dense; a refit window is under 150 waypoints).
function solveSPD(A, b) {
  const m = b.length, L = A.map(r => Float64Array.from(r)), y = Float64Array.from(b);
  for (let c = 0; c < m; c++) {
    for (let r = c + 1; r < m; r++) {
      if (L[r][c] === 0) continue;
      const f = L[r][c] / L[c][c];
      for (let k = c; k < Math.min(m, c + 7); k++) L[r][k] -= f * L[c][k];
      y[r] -= f * y[c];
    }
  }
  const x = new Float64Array(m);
  for (let r = m - 1; r >= 0; r--) { let s = y[r]; for (let k = r + 1; k < Math.min(m, r + 7); k++) s -= L[r][k] * x[k]; x[r] = s / L[r][r]; }
  return x;
}

/// The posted limits the refit tapers are sized for (TrackCatalog speedLimitKmh, in mph).
export const STAGE_MPH = { swa: 35, gap: 45, mtm: 35, beech: 45, chimney: 25, brp: 45, swiss: 35, brock: 35 };

/// The local refits. places: [{ fromM, toM }] (the S's first and last lobe peak, metres along the line = index *
/// spacing). The line is redrawn as the curve that stays closest to it while changing its curvature least (weighted
/// squared distance + lambda x the squared third differences of the waypoints): an S has a great deal of curvature
/// change, an arc and a spiral almost none, so the S goes and the bends either side keep their line. Between the
/// peaks the line is held only loosely (weight wCore), over a quintic taper on each side ever more firmly, and the
/// first and last three waypoints of the window not at all loosely: position, heading and curvature carry on where
/// the refit meets the line. Each taper is max(72 m, 45 W, 1.5 W S) for the move W it makes at the posted speed S
/// (mph): the G1 rural floor and G4 for a move off the mapped line; the whole refit is longer than 2 L.
/// This is the investigation's variant L with that curve in place of its single 200 m quadratic, which cannot follow
/// the bends such a window reaches on these roads: on the smoothed lines it would have moved the line 1.2-7.7 m at all
/// seven places left, and was refused at each.
/// Returns { wp, done: [{fromM, toM, fromFadeM, toFadeM, taperM, moveM}], refused: [...] }.
export function localRefits(wp, places, { spacing = 4, loop = false, mph = 45, padM = 0, maxMoveM = 2.0, lambda = 100, wCore = 0.05 } = {}) {
  const n = wp.length, pad = Math.round(padM / spacing);
  let P = wp.map(p => ({ x: p.x, z: p.z }));
  const done = [], refused = [];
  const solve = (core0, core1, T) => {
    const iA = core0 - T, iB = core1 + T;
    if (!loop && (iA < 2 || iB > n - 3)) return null;
    const idx = []; for (let k = iA; k <= iB; k++) idx.push(loop ? ((k % n) + n) % n : k);
    const m = idx.length, o = P[idx[0]];
    const A = Array.from({ length: m }, () => new Float64Array(m));
    const w = new Float64Array(m);
    for (let j = 0; j < m; j++) {
      const k = iA + j;
      const B = k < core0 ? q5((k - iA) / Math.max(1, core0 - iA)) : k > core1 ? q5((iB - k) / Math.max(1, iB - core1)) : 1;
      w[j] = (j < 3 || j >= m - 3) ? 1e9 : wCore + (1 - wCore) * (1 - B);
      A[j][j] += w[j];
    }
    const c = [-1, 3, -3, 1];
    for (let j = 0; j + 3 < m; j++) for (let a = 0; a < 4; a++) for (let b = 0; b < 4; b++) A[j + a][j + b] += lambda * c[a] * c[b];
    const xs = solveSPD(A, idx.map((k, j) => w[j] * (P[k].x - o.x))), zs = solveSPD(A, idx.map((k, j) => w[j] * (P[k].z - o.z)));
    let mv = 0; for (let j = 0; j < m; j++) mv = Math.max(mv, Math.hypot(xs[j] + o.x - P[idx[j]].x, zs[j] + o.z - P[idx[j]].z));
    return { idx, xs, zs, o, iA, iB, mv, T };
  };
  for (const pl of places) {
    const core0 = Math.floor(pl.fromM / spacing) - pad, core1 = Math.ceil(pl.toM / spacing) + pad;
    let T = Math.ceil(72 / spacing), sol = solve(core0, core1, T);
    for (let tries = 0; sol && tries < 4; tries++) {          // a taper long enough for the move it makes
      const need = Math.ceil(Math.max(72, 45 * sol.mv, 1.5 * sol.mv * mph) / spacing);
      if (need <= T) break;
      T = need; sol = solve(core0, core1, T);
    }
    if (!sol) { refused.push({ ...pl, why: 'its tapers would reach the end of the stage' }); continue; }
    const rec = { fromM: pl.fromM, toM: pl.toM, fromFadeM: sol.iA * spacing, toFadeM: sol.iB * spacing, taperM: sol.T * spacing, moveM: +sol.mv.toFixed(3) };
    if (sol.mv > maxMoveM) { refused.push({ ...rec, why: `it would move the line ${sol.mv.toFixed(2)} m (over ${maxMoveM} m)` }); continue; }
    sol.idx.forEach((k, j) => { P[k] = { x: sol.xs[j] + sol.o.x, z: sol.zs[j] + sol.o.z }; });
    done.push(rec);
  }
  return { wp: P, done, refused };
}

/// tools/roads/plan_refits.json: { "<pre>": [{ "fromM", "toM", "why" }] }
export function refitsFor(pre) {
  const p = process.env.PSX_REFITS_JSON || join(dirname(fileURLToPath(import.meta.url)), 'plan_refits.json');
  if (!existsSync(p)) return [];
  const j = JSON.parse(readFileSync(p, 'utf8'));
  return (j.stages && j.stages[pre]) || [];
}

// ------------------------------------------------------------------ CLI: plan only, heights carried by index
if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const args = process.argv.slice(2);
  const opt = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };
  const [pre, outPath] = args;
  if (!pre || !outPath) { console.error('usage: node plan_smooth.mjs <pre> <out_stage.json> [--sigma 8] [--refits] [--in <stage.json>]'); process.exit(2); }
  const inPath = opt('--in', join(dirname(fileURLToPath(import.meta.url)), 'srtm_bakes', `${pre}_srtm`, `${pre}_stage.json`));
  const j = JSON.parse(readFileSync(inPath, 'utf8'));
  const n = j.xyz.length / 3, sp = j.spacing || 4, loop = !!j.loop;
  let wp = []; for (let i = 0; i < n; i++) wp.push({ x: j.xyz[3 * i], z: j.xyz[3 * i + 2] });
  const sm = planSmooth(wp, { sigmaM: +opt('--sigma', 8), spacing: sp, loop });
  wp = sm.wp;
  let msg = `${pre}: gaussian ${opt('--sigma', 8)} m on the gentle stretches, largest move ${sm.maxMove.toFixed(2)} m at ${sm.maxAt} m`;
  if (args.includes('--refits')) {
    const env = (k, d) => (process.env[k] !== undefined ? +process.env[k] : d);   // experiments only; the bake uses the defaults
    const rf = localRefits(wp, refitsFor(pre), { spacing: sp, loop, mph: STAGE_MPH[pre] ?? 45,
      lambda: env("PSX_REFIT_LAMBDA", 100), padM: env("PSX_REFIT_PAD", 0), wCore: env("PSX_REFIT_WCORE", 0.05) });
    wp = rf.wp;
    msg += `; ${rf.done.length} refit(s)` + rf.done.map(d => ` ${d.fromM}-${d.toM} m (moved ${d.moveM} m, tapers ${d.taperM} m, ${d.fromFadeM}-${d.toFadeM} m)`).join(',');
    if (rf.refused.length) msg += `; refused ${rf.refused.map(d => `${d.fromM}-${d.toM} m: ${d.why}`).join('; ')}`;
  }
  const xyz = [];
  for (let i = 0; i < n; i++) xyz.push(+wp[i].x.toFixed(2), j.xyz[3 * i + 1], +wp[i].z.toFixed(2));
  writeFileSync(outPath, JSON.stringify({ ...j, xyz }));
  console.log(msg);
}
