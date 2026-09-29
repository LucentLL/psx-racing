// linegate.mjs - THE SMOOTHNESS GATE, offline (gate spec sections 3-6; plan
// amendment A2/A4 WP-G). The owner's rule of 2026-09-28: painted lines and
// road edges never wobble or kink.
//
// What it measures is what the renderer draws: for every ribbon quad of the
// replica (linesim.mjs), every painted line of the slot's texture (runs READ
// FROM THE PNGs, paintruns.mjs) is found as the iso-line U = u inside each of
// the quad's two triangles (paintiso.quadIso) - so a diagonal bulge is sampled
// at its peak, never between samples (plan A7). Ribbon edges and the ribbon
// midline come from the same sections. Those lines are then checked against
//   * a PLAN built from data only (the graph, RoadProfiles' layout rules and
//     the Trims taper TABLE, a design decision) - never the builder's
//     sections, U or quads, so a bug in the line model cannot hide itself;
//   * their own shape (jitter, facet sagitta - lone, split and hedged corners,
//     zigzag peaks, jogs, bumps and notches, waves, lib/kink.mjs - radius); a
//     squeezed ribbon edge (the squeeze fired: sqL / sqR, the cause, as the
//     mesh tap flags it) also against its plan I7 envelope (squeezeDev);
//   * their continuity (jumps, gaps, ends - a gore NOSE, not any clip, is a
//     legitimate end - dash lengths), chained through
//     mitred nodes (two ends within V are one line, the step taken out for
//     the shape checks) and bend fans (2-arm nodes drawn as slabs: judged
//     across, their mouths no legitimate end);
//   * other pavement (paint inside another ribbon at the same level; only a
//     branch's attach arc is report-only).
// Every lateral limit is V (SmoothRules.cs, read by smoothrules.mjs).
//
// Editor/CitySmooth.cs is the same gate on the BUILT meshes. The two must
// agree on every check both measure; a disagreement is a gate bug. What only
// the mesh gate can see: tile SEAMs (B4s), fan perimeters (B2/B3 FAN, D1 into
// fans), heights (D1's same-level test is the census's plan approximation
// here), E1 (strip paint does not exist yet).
import { quadIso, frameOf } from './paintiso.mjs';
import { dot, sub } from './linesim.mjs';
import { kinkScores, kindText, turningPoints, easeInto } from './kink.mjs';

const DEG = 180 / Math.PI;
const LANE = 3.6576;

// ------------------------------------------------------------ CityMeshes.IsFresh (the surface a span is drawn with)
export function isFresh(x, z) {
  const xi = Math.trunc(Math.fround(Math.fround(x) * 100)) | 0, zi = Math.trunc(Math.fround(Math.fround(z) * 100)) | 0;
  let h = (Math.imul(xi, 0x9e3779b1 | 0) ^ Math.imul(zi, 0x6a09e667)) >>> 0;
  h = (h ^ (h >>> 16)) >>> 0; h = Math.imul(h, 0x85ebca6b) >>> 0;
  h = (h ^ (h >>> 13)) >>> 0; h = Math.imul(h, 0xc2b2ae35) >>> 0;
  h = (h ^ (h >>> 16)) >>> 0;
  return h % 100 < 40;
}
const surfaceKey = (e, elev) => { const f = isFresh(e.pts[0][0], e.pts[0][1]); return elev ? (f ? 'concrete_new' : 'concrete_old') : (f ? 'asphalt_new' : 'asphalt_old'); };

// ------------------------------------------------------------ THE PLAN (gate spec 3.4)
/// The lines a profile row should carry, from RoadProfiles' layout rules
/// (lanes of 3.6576 m, shoulders; edge lines at shoulder + 0.06; a double
/// yellow or TWLTL pair on an undivided road). Offsets are lateral from the
/// design centreline, + = left of travel. Anchors: C (constant offset from the
/// centreline), EL / ER (a constant inset from the design edge).
export function planLinesOf(prof, R) {
  const W = prof.width, hw = W / 2, oneway = !prof.key.startsWith('tw'), turn = prof.key.endsWith('t');
  const PH = R.EdgeLineInsetM;
  const out = [];
  out.push({ id: 'EL', anchor: 'EL', col: oneway ? 'Y' : 'W', dashed: false, inset: prof.shl + PH, off: hw - (prof.shl + PH), kind: 'edge' });
  out.push({ id: 'ER', anchor: 'ER', col: 'W', dashed: false, inset: prof.shr + PH, off: -(hw - (prof.shr + PH)), kind: 'edge' });
  // a line within half a centimetre of the centreline is +0.00: its sign is float noise in the profile's width
  // (C# adds the profile's float numbers, this file doubles), and a key must not flip on it (review of the first Unity run)
  const C = (m, col, dashed, kind) => { const off = hw - m; out.push({ id: `C${col}${dashed ? 'd' : 's'}${off >= 0 || Math.abs(off) < 0.005 ? '+' : '-'}${Math.abs(off).toFixed(2)}`, anchor: 'C', col, dashed, off, kind }); };
  if (oneway) for (let i = 1; i < prof.lanes; i++) C(prof.shl + LANE * i, 'W', true, 'lane');
  else {
    const perSide = (prof.lanes - (turn ? 1 : 0)) / 2, medStart = prof.shl + perSide * LANE;
    for (let i = 1; i < perSide; i++) C(prof.shl + LANE * i, 'W', true, 'lane');
    if (turn) {
      C(medStart - 2 * PH, 'Y', false, 'centre'); C(medStart + 2 * PH, 'Y', true, 'centre');
      C(medStart + LANE - 2 * PH, 'Y', true, 'centre'); C(medStart + LANE + 2 * PH, 'Y', false, 'centre');
      for (let i = 1; i < perSide; i++) C(medStart + LANE + LANE * i, 'W', true, 'lane');
    } else {
      C(medStart - 2 * PH, 'Y', false, 'centre'); C(medStart + 2 * PH, 'Y', false, 'centre');
      for (let i = 1; i < perSide; i++) C(medStart + LANE * i, 'W', true, 'lane');
    }
  }
  return out;
}

/// Match a texture's runs to the plan's lines at nominal scale (colour,
/// pattern, nearest within StrayM). q = the run's lateral minus the plan's:
/// the texture's own quantisation. Only up to HALF A TEXEL of it (cap = W /
/// texW / 2 + TexelPadM) is the texture's rounding, which the position checks
/// subtract (runQc) - V is half a texel precisely because below it geometry
/// cannot be told from the texture (gate spec 2). A run further off is a
/// painter fault: A0 TEXTURE fails, and the checks still see the rest of it.
export function matchLayout(runs, plan, W, R) {
  const cand = [];
  runs.forEach((r, k) => plan.forEach((p, j) => {
    if (r.col !== p.col || r.dashed !== p.dashed) return;
    const d = (0.5 - r.u) * W - p.off;
    if (Math.abs(d) <= R.StrayM) cand.push({ k, j, d });
  }));
  cand.sort((a, b) => Math.abs(a.d) - Math.abs(b.d));
  const runPlan = new Array(runs.length).fill(-1), runQ = new Array(runs.length).fill(0), planRun = new Array(plan.length).fill(-1);
  for (const c of cand) if (runPlan[c.k] < 0 && planRun[c.j] < 0) { runPlan[c.k] = c.j; runQ[c.k] = c.d; planRun[c.j] = c.k; }
  const texW = runs.length ? runs[0].texW : 0;
  const cap = (texW ? W / texW / 2 : 0) + R.TexelPadM;
  const runQc = runQ.map(q => Math.max(-cap, Math.min(cap, q)));
  return { runPlan, runQ, runQc, planRun, cap };
}

// ------------------------------------------------------------ polyline helpers
function turnOf(ax, az, bx, bz, cx, cz) {
  const ux = bx - ax, uz = bz - az, vx = cx - bx, vz = cz - bz;
  return Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz);
}
/// Collinear vertices (|turn| < CollinearDeg) and near-duplicates dropped:
/// the indices kept.
/// The vertices the shape checks keep (Editor/CitySmooth.cs KeepIdx is the same code), the SAME whichever way a
/// strand is walked (review 8: dropped relative to the last kept vertex, a walk from each end kept different vertices,
/// and the two gates walk strands from different ends): a run of vertices each within 1 mm of the next is one vertex,
/// the one with the smallest (x, z); a vertex is kept when the collinear walk from EITHER end keeps it (its turn from
/// that walk's last kept vertex to the next is at least CollinearDeg); both ends are kept.
export function keepIdx(P, R) {
  const n = P.length;
  if (!n) return [];
  const rep = [];
  for (let i = 0; i < n;) {
    let j = i, best = i;
    while (j + 1 < n && Math.hypot(P[j + 1].x - P[j].x, P[j + 1].z - P[j].z) < 1e-3) {
      j++;
      if (P[j].x < P[best].x || (P[j].x === P[best].x && P[j].z < P[best].z)) best = j;
    }
    rep.push(best); i = j + 1;
  }
  const m = rep.length;
  if (m <= 2) return rep;
  const lim = R.CollinearDeg / DEG, mark = new Uint8Array(m);
  mark[0] = 1; mark[m - 1] = 1;
  for (let a = 0, k = 1; k + 1 < m; k++) {
    const A = P[rep[a]], B = P[rep[k]], Cn = P[rep[k + 1]];
    if (Math.hypot(B.x - A.x, B.z - A.z) < 1e-3) continue;
    if (Math.abs(turnOf(A.x, A.z, B.x, B.z, Cn.x, Cn.z)) < lim) continue;
    mark[k] = 1; a = k;
  }
  for (let a = m - 1, k = m - 2; k > 0; k--) {
    const A = P[rep[a]], B = P[rep[k]], Cn = P[rep[k - 1]];
    if (Math.hypot(B.x - A.x, B.z - A.z) < 1e-3) continue;
    if (Math.abs(turnOf(A.x, A.z, B.x, B.z, Cn.x, Cn.z)) < lim) continue;
    mark[k] = 1; a = k;
  }
  const keep = [];
  for (let k = 0; k < m; k++) if (mark[k]) keep.push(rep[k]);
  return keep;
}
/// B1's sample stations on one strand, as sorted strand arcs with NaN between two windows that do not meet
/// (Editor/CitySmooth.cs B1Stations is the same code). A window is +-H about each kept interior vertex, clipped to
/// [H, Ltot - H]; windows closer than a step merge. Inside a window a station stands where the line's own way arc
/// (wayArc: wayOff + s, the arc the ratchet keys are rounded on) is a whole number of steps and a half (never on a 5 m
/// key boundary, where a sample's float s put it in one bucket walked one way and the next walked the other), interpolated along each
/// kept segment - so a place is sampled the same whichever way a strand is walked and wherever it was cut (anchored
/// to the strand's middle, as before, the two gates read B1 at a different phase wherever they cut a strand
/// differently). A kept segment that crosses to another way, or along which the way's arc does not advance about
/// as fast as the line (a fan's perimeter, a branching way's jump), is stepped from its own midpoint instead.
export function b1Stations(P, idx, C, Ltot, step, H, wayArc, wayOf) {
  const n = idx.length, W = new Float64Array(n), Wy = new Array(n);
  for (let m = 0; m < n; m++) { const p = P[idx[m]]; W[m] = wayArc(p); Wy[m] = wayOf(p); }
  const out = [];
  let lo = 0, hi = -1, seg = 0;
  const flush = () => {
    if (hi < lo) return;
    if (out.length) out.push(NaN);
    while (seg > 0 && C[seg] > lo) seg--;
    for (let m = seg; m + 1 < n && C[m] <= hi; m++) {
      const a0 = C[m], a1 = C[m + 1], L = a1 - a0;
      if (a1 < lo || L < 1e-9) continue;
      seg = m;
      const x0 = Math.max(lo, a0), x1 = Math.min(hi, a1), dw = W[m + 1] - W[m], adw = Math.abs(dw);
      if (Wy[m] === Wy[m + 1] && adw >= 0.2 * L && adw <= 5 * L + 0.5 && adw > 1e-6) {
        const w0 = W[m] + (x0 - a0) / L * dw, w1 = W[m] + (x1 - a0) / L * dw;
        const k0 = Math.ceil(Math.min(w0, w1) / step - 0.5 - 1e-9), k1 = Math.floor(Math.max(w0, w1) / step - 0.5 + 1e-9);
        if (dw > 0) for (let k = k0; k <= k1; k++) out.push(a0 + ((k + 0.5) * step - W[m]) / dw * L);
        else for (let k = k1; k >= k0; k--) out.push(a0 + ((k + 0.5) * step - W[m]) / dw * L);
      } else {
        const mid = (a0 + a1) / 2, k0 = Math.ceil((x0 - mid) / step - 1e-9), k1 = Math.floor((x1 - mid) / step + 1e-9);
        for (let k = k0; k <= k1; k++) out.push(mid + k * step);
      }
    }
  };
  for (let m = 1; m + 1 < n; m++) {
    const a = Math.max(C[m] - H, H), b = Math.min(C[m] + H, Ltot - H);
    if (b < a) continue;
    if (hi >= lo && a <= hi + step) { if (b > hi) hi = b; continue; }
    flush(); lo = a; hi = b;
  }
  flush();
  return out;
}
function arcOf(P, idx) {
  const C = new Float64Array(idx.length);
  for (let i = 1; i < idx.length; i++) C[i] = C[i - 1] + Math.hypot(P[idx[i]].x - P[idx[i - 1]].x, P[idx[i]].z - P[idx[i - 1]].z);
  return C;
}
function pointAtArc(P, idx, C, a, hint) {
  let i = Math.max(0, Math.min(hint | 0, idx.length - 2));
  while (i > 0 && C[i] > a) i--;
  while (i + 2 < idx.length && C[i + 1] < a) i++;
  const p = P[idx[i]], q = P[idx[i + 1]], L = C[i + 1] - C[i];
  const t = L > 1e-9 ? Math.max(0, Math.min(1, (a - C[i]) / L)) : 0;
  return { x: p.x + (q.x - p.x) * t, z: p.z + (q.z - p.z) * t, i, t };
}
/// B1's local model of a window: the Kasa least-squares circle, or the
/// least-squares line when the line fits the window at least as well (Kasa's
/// algebraic fit is biased towards small circles on short, nearly straight
/// windows, which is exactly what most windows are). Returns the residual of
/// sample c from the chosen model.
function kasaResidual(xs, zs, c) {
  const n = xs.length;
  const ox = xs[c], oz = zs[c];
  let Sx = 0, Sz = 0, Sxx = 0, Szz = 0, Sxz = 0, Sxr = 0, Szr = 0, Sr = 0;
  for (let i = 0; i < n; i++) {
    const x = xs[i] - ox, z = zs[i] - oz, r = x * x + z * z;
    Sx += x; Sz += z; Sxx += x * x; Szz += z * z; Sxz += x * z; Sxr += x * r; Szr += z * r; Sr += r;
  }
  // the line: principal axis through the centroid
  const mx = Sx / n, mz = Sz / n;
  const cxx = Sxx / n - mx * mx, czz = Szz / n - mz * mz, cxz = Sxz / n - mx * mz;
  const th = 0.5 * Math.atan2(2 * cxz, cxx - czz), nx = -Math.sin(th), nz = Math.cos(th);
  let lineSS = 0;
  for (let i = 0; i < n; i++) { const d = (xs[i] - ox - mx) * nx + (zs[i] - oz - mz) * nz; lineSS += d * d; }
  const lineRes = Math.abs((0 - mx) * nx + (0 - mz) * nz);
  // the circle: [Sxx Sxz Sx; Sxz Szz Sz; Sx Sz n] [D E F]' = -[Sxr Szr Sr]' (Cramer)
  const det3 = (a0, a1, a2, b0, b1, b2, c0, c1, c2) => a0 * (b1 * c2 - b2 * c1) - a1 * (b0 * c2 - b2 * c0) + a2 * (b0 * c1 - b1 * c0);
  const d = det3(Sxx, Sxz, Sx, Sxz, Szz, Sz, Sx, Sz, n);
  if (Math.abs(d) < 1e-30) return lineRes;
  const r0 = -Sxr, r1 = -Szr, r2c = -Sr;
  const D = det3(r0, Sxz, Sx, r1, Szz, Sz, r2c, Sz, n) / d;
  const E = det3(Sxx, r0, Sx, Sxz, r1, Sz, Sx, r2c, n) / d;
  const F = det3(Sxx, Sxz, r0, Sxz, Szz, r1, Sx, Sz, r2c) / d;
  const cx = -D / 2, cz = -E / 2, r2 = cx * cx + cz * cz - F;
  if (!(r2 > 0) || !Number.isFinite(r2)) return lineRes;
  const r = Math.sqrt(r2);
  let circSS = 0;
  for (let i = 0; i < n; i++) { const g = Math.hypot(xs[i] - ox - cx, zs[i] - oz - cz) - r; circSS += g * g; }
  if (circSS >= lineSS) return lineRes;
  return Math.abs(Math.hypot(cx, cz) - r);   // the centre sample is the origin
}

/// A1 on a SQUEEZED ribbon edge, against where plan I7 puts it: "the cut per
/// chain is the cut needed, max-filtered over the taper floor and eased like
/// a taper". smp: the edge's A1 samples in order of s ({s, err, x3}); err is
/// the signed offset from the design edge, so the cut (inward) is -sgn * err;
/// L: the class's taper floor. The cut is split at its turning points (V
/// hysteresis) into rises and falls; two terms, per sample, in metres:
///   EASE  inside each rise or fall of height H, any two samples w < L apart
///         may differ by at most H_L g(w / L), g(x) = 1.5x - 0.5x^3 - the most
///         a smoothstep of height H_L over L changes over any w (the plan's
///         taper shape). H_L is the LOCAL height: the change across the floor-
///         length window centred on the pair (from the last sample at or
///         before its start to the first at or after its end, inside the
///         rise), never more than H - so a fast step inside a tall, slow rise
///         no longer borrows the whole rise's height (review 4: 1 m in 8.9 m
///         after a slow 1 m ramp over 80 m read 0). The excess is how much of
///         the change came faster than a taper over the floor: a squeeze
///         arriving or leaving in 8 m instead of 15, or in steps;
///   HOLD  at a dip between two cuts (a turning-point minimum between two
///         maxima) narrower than L where it drops below the lower of them:
///         that lower cut less the cut - an edge coming back out between cuts
///         less than a floor apart, where I7 holds it in.
/// A cut eased as a smoothstep over L or longer (or several stacked), and held
/// between cuts, reads 0 on both at every sample (a pointwise bound: no
/// sampling slack; the sampled window only widens H_L). A
/// clipped or collapsed sample (X3/X2) breaks the signal and reads 0.
export function squeezeDev(smp, sgn, L, V) {
  const n = smp.length, dev = new Float64Array(n);
  for (let i0 = 0; i0 < n;) {
    if (smp[i0].x3) { i0++; continue; }
    let i1 = i0; while (i1 + 1 < n && !smp[i1 + 1].x3) i1++;
    const m = i1 - i0 + 1, s = new Float64Array(m), c = new Float64Array(m);
    for (let k = 0; k < m; k++) { s[k] = smp[i0 + k].s; c[k] = -sgn * smp[i0 + k].err; }
    // turning points (V hysteresis; a plateau's first sample), then EASE per rise or fall (lib/kink.mjs)
    const tp = turningPoints(c, V);
    const bump = (k, e) => { if (e > dev[i0 + k]) dev[i0 + k] = e; };
    easeInto(s, c, tp, L, V, bump);
    // HOLD, per dip between two cuts
    for (let t = 1; t + 1 < tp.length; t++) {
      const k0 = tp[t];
      if (!(c[k0] < c[tp[t - 1]] && c[k0] < c[tp[t + 1]])) continue;
      const H = Math.min(c[tp[t - 1]], c[tp[t + 1]]);
      let kL = k0, kR = k0;
      while (kL > 0 && c[kL] < H) kL--;
      while (kR < m - 1 && c[kR] < H) kR++;
      if (s[kR] - s[kL] >= L) continue;
      for (let k = kL + 1; k < kR; k++) bump(k, H - c[k]);
    }
    i0 = i1 + 1;
  }
  return dev;
}
export const SQUEEZE_WHAT = 'squeezed edge outside its I7 envelope (the cut eased like a taper over the class floor)';

// ------------------------------------------------------------ the gate
/// S: a linesim with e.secs built. R: smoothrules. layouts: paintruns. opts:
/// { planTaperShape: 'linear'|'smooth', refSpots: [[x,z]], log }.
/// A line id without its offset ('CYs+0.12' -> 'CYs'): the agreement instrument's line class.
export const lineClass = id => (id || '').replace(/[-+][0-9.]+$/, '');

export function runGate(S, R, layouts, opts = {}) {
  const { E, T, nodeEdges } = S;
  const log = opts.log || (() => {});
  const V = R.V;
  const shape = opts.planTaperShape === 'smooth' ? (t => { t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t); }) : (t => Math.max(0, Math.min(1, t)));
  const hwD = (e, s) => {   // the plan's design half width: the Trims taper TABLE with the plan's shape
    const hw = e.width * 0.5, i = e.index; let ha = hw, hb = hw;
    if (T.taperA[i] > 0 && s < T.taperA[i]) ha = T.hwA[i] + (hw - T.hwA[i]) * shape(s / T.taperA[i]);
    if (T.taperB[i] > 0 && e.length - s < T.taperB[i]) hb = T.hwB[i] + (hw - T.hwB[i]) * shape((e.length - s) / T.taperB[i]);
    return Math.min(ha, hb);
  };
  const planOff = (L, e, s) => L.anchor === 'C' ? L.off : L.anchor === 'EL' ? hwD(e, s) - L.inset : -(hwD(e, s) - L.inset);
  const planExists = (L, e, s) => L.anchor === 'C' ? Math.abs(L.off) < hwD(e, s) - R.ExistInsetM : hwD(e, s) - L.inset > 0;

  // ---- the plan and the texture per profile key
  const profOf = new Map();
  for (const e of E) profOf.set(e.profile.key, e.profile);
  const plans = new Map(), texNotes = [], texQ = {}, texCap = {}, texFails = [];
  for (const [key, prof] of profOf) {
    const plan = planLinesOf(prof, R);
    const bySurf = {};
    for (const surf of ['asphalt_new', 'asphalt_old', 'concrete_new', 'concrete_old']) {
      const runs = layouts.get(`${key}_${surf}`);
      if (!runs) continue;
      const m = matchLayout(runs, plan, prof.width, R);
      bySurf[surf] = { runs, ...m };
      // A0 TEXTURE: every run planned and within half a texel of its line, every plan line painted
      const fail = (what, val, ratio) => { texNotes.push(`${key}_${surf}: ${what}`); texFails.push({ key, surf, what, val, ratio }); };
      runs.forEach((r, k) => {
        if (m.runPlan[k] < 0) fail(`texture run at u ${r.u.toFixed(4)} (${r.col}${r.dashed ? ' dashed' : ''}) has no plan line`, 0, 99);
        else if (Math.abs(m.runQ[k]) > m.cap) fail(`texture run at u ${r.u.toFixed(4)} sits ${(m.runQ[k] * 100).toFixed(1)} cm from plan line ${plan[m.runPlan[k]].id}, over half a texel (${(m.cap * 100).toFixed(1)} cm)`, Math.abs(m.runQ[k]), Math.abs(m.runQ[k]) / m.cap);
      });
      plan.forEach((p, j) => { if (m.planRun[j] < 0) fail(`plan line ${p.id} has no texture run`, 0, 99); });
      let q = 0; for (const v of m.runQ) q = Math.max(q, Math.abs(v)); texQ[key] = Math.max(texQ[key] || 0, q);
      texCap[key] = m.cap;
    }
    plans.set(key, { plan, bySurf });
  }

  // ---- ways: the arc of each edge along its OSM way (the stable key)
  const wayOff = new Float64Array(E.length);
  {
    const byWay = new Map();
    for (const e of E) { let l = byWay.get(e.wayId); if (!l) byWay.set(e.wayId, l = []); l.push(e); }
    for (const list of byWay.values()) {
      if (list.length === 1) continue;
      const startAt = new Map(); for (const e of list) startAt.set(e.a, e);
      const ends = new Set(list.map(e => e.b));
      let first = list.find(e => !ends.has(e.a)) || list[0];
      const seen = new Set(); let off = 0, cur = first;
      while (cur && !seen.has(cur.index)) { seen.add(cur.index); wayOff[cur.index] = off; off += cur.length; cur = startAt.get(cur.b); }
      for (const e of list) if (!seen.has(e.index)) wayOff[e.index] = 0;   // a branching way: keyed on its own arc
    }
  }
  const pinned = new Set(R.PinnedWays);

  // ---- routes and reference spots (exposure)
  const onRoute = new Set();
  for (const r of S.city.routes) for (const ei of r.edges) onRoute.add(ei);
  const refSpots = opts.refSpots || [];
  const exposureAt = (ei, x, z) => {
    let ex = 1;
    if (onRoute.has(ei)) ex = Math.max(ex, R.ExposureRoute);
    for (const [sx, sz] of refSpots) if (Math.hypot(sx - x, sz - z) <= R.RefSpotReachM) { ex = Math.max(ex, R.ExposureRefSpot); break; }
    return ex;
  };

  // ---- chains through mitred nodes and BEND FANS
  const hasRibbon = e => e.secs && e.secs.length >= 2;
  const endsAt = (e, n) => hasRibbon(e) && (e.a === n ? e.secs[0].s < 1e-6 : Math.abs(e.secs[e.secs.length - 1].s - e.length) < 1e-6);
  const jointAt = (e, n) => {
    if (!T.mitre[n] || e.a === e.b) return -1;
    const o = T.throughA[n] === e.index ? T.throughB[n] : T.throughB[n] === e.index ? T.throughA[n] : -1;
    if (o < 0 || o === e.index || E[o].a === E[o].b) return -1;
    return endsAt(e, n) && endsAt(E[o], n) ? o : -1;
  };
  /// A 2-arm node the builder draws as a junction slab (past ContinueCos,
  /// ComputeTrims patches it): plan A2 - a 2-arm node is never a junction
  /// corner however sharp - and A7. The gate chains the two arms across it
  /// (so B2/B3 judge the corner the slab draws), and a line ending at its
  /// mouths is no legitimate end (C2).
  const armsOf = n => nodeEdges[n].filter(i => E[i].a !== E[i].b);
  const isBendFan = n => !!T.patch[n] && armsOf(n).length === 2;
  const bendAt = (e, n) => {
    if (e.a === e.b || !isBendFan(n) || !hasRibbon(e)) return -1;
    const arms = armsOf(n), o = arms[0] === e.index ? arms[1] : arms[1] === e.index ? arms[0] : -1;
    return o >= 0 && o !== e.index && hasRibbon(E[o]) ? o : -1;
  };
  const linkAt = (e, n) => { const o = jointAt(e, n); if (o >= 0) return { o, kind: 'mitre' }; const b = bendAt(e, n); return b >= 0 ? { o: b, kind: 'bend' } : null; };
  let bendFans = 0, bendFans60 = 0;
  for (let n = 0; n < nodeEdges.length; n++) if (isBendFan(n)) {
    bendFans++;
    const [i, j] = armsOf(n), a = S.outDir(E[i], n), b = S.outDir(E[j], n);
    if (180 - Math.acos(Math.max(-1, Math.min(1, a[0] * b[0] + a[1] * b[1]))) * DEG >= 60) bendFans60++;
  }
  const chainOf = new Int32Array(E.length).fill(-1);
  const chains = [];
  for (const e0 of E) {
    if (!hasRibbon(e0) || chainOf[e0.index] >= 0) continue;
    // walk back to the start (or round a ring)
    let cur = e0, enter = e0.a, guard = 0;
    for (;;) {
      const l = linkAt(cur, enter);
      if (!l || l.o === e0.index || chainOf[l.o] >= 0 || ++guard > 100000) break;
      const oe = E[l.o];
      enter = oe.a === enter ? oe.b : oe.a;
      cur = oe;
    }
    const list = [];
    let at = cur, from = enter, linkIn = null;
    const id = chains.length;
    for (;;) {
      if (chainOf[at.index] >= 0) break;
      chainOf[at.index] = id;
      const fwd = at.a === from;
      list.push({ e: at, fwd, linkIn });
      const exit = fwd ? at.b : at.a;
      const l = linkAt(at, exit);
      if (!l) break;
      at = E[l.o]; from = exit; linkIn = l.kind;
    }
    chains.push(list);
  }
  log(`gate: ${chains.length} chains, ${bendFans} bend fans`);
  /// Two strand ends within V are one strand: a sub-V step is invisible, and
  /// a kink right at the node must still be judged by B1/B2. The shape checks
  /// then run on the concatenated polyline with the step taken out: the new
  /// piece (copied) is moved onto the strand's end. sh is the move already
  /// applied to the strand's tail ({x, z}); the new one is returned. A
  /// strand's shape is unchanged by a move, and its positions are off by the
  /// sum of the sub-V steps it crossed.
  /// Of the two ends of a jump or a dash, the one a run is reported at: the lower OSM way, then the lower edge, then
  /// the lower s. One run gets one key whichever way the chain runs (this gate starts a chain at its lowest edge,
  /// CitySmooth at the first edge of the tile's ring: they named a jump after either end).
  const firstEnd = (p, q) => { const wp = E[p.e].wayId, wq = E[q.e].wayId; return wp !== wq ? wp < wq : p.e !== q.e ? p.e < q.e : p.s <= q.s; };
  /// Two candidate joint pairs at the same distance: ordered by their end points (the firstEnd one first, then the
  /// other), so the pairing does not depend on the order the chain's walk met the pieces.
  const ptOrder = (p, q) => E[p.e].wayId - E[q.e].wayId || p.e - q.e || p.s - q.s || p.x - q.x || p.z - q.z;
  function pairOrder(P1, P2) {
    const ends = pr => { const u = pr.a.pts.at(-1), w = pr.b.pts[0]; return ptOrder(u, w) <= 0 ? [u, w] : [w, u]; };
    const [a1, b1] = ends(P1), [a2, b2] = ends(P2);
    return ptOrder(a1, a2) || ptOrder(b1, b2);
  }
  function joinEnds(strand, pc, sh, bridge = false) {
    const a = strand[strand.length - 1], b = pc[0];
    const tx = bridge ? sh.x : a.x - b.x, tz = bridge ? sh.z : a.z - b.z;
    const moved = Math.abs(tx) > 1e-12 || Math.abs(tz) > 1e-12;
    // the joint's one vertex is named after the end firstEnd picks (its place stays: either way the strand is the same
    // shape, the next piece translated onto it), so its runs have one key whichever way the chain runs; exempt only if
    // both ends are (never looser than either way)
    if (!bridge && (b.e !== a.e || b.s !== a.s) && firstEnd(b, a))
      strand[strand.length - 1] = { ...a, e: b.e, s: b.s, tag: b.tag, span: b.span, sec: b.sec, side: b.side, lid: b.lid, x3: !!(a.x3 && b.x3), gore: !!(a.gore && b.gore) };
    else if (!bridge && ((a.x3 && !b.x3) || (a.gore && !b.gore))) strand[strand.length - 1] = { ...a, x3: !!(a.x3 && b.x3), gore: !!(a.gore && b.gore) };
    for (let i = bridge ? 0 : 1; i < pc.length; i++) strand.push(moved ? { ...pc[i], x: pc[i].x + tx, z: pc[i].z + tz } : pc[i]);
    return { x: tx, z: tz };
  }

  // ---- the design centreline of each edge, extended through its joints (A-family reference)
  function refLine(e) {
    const X = [], Z = [];
    const pre = [];
    const oa = jointAt(e, e.a);
    if (oa >= 0) {   // the neighbour's points leading INTO e.a, up to 40 m
      const o = E[oa], P = o.b === e.a ? o.pts : [...o.pts].reverse();
      let acc = 0;
      for (let i = P.length - 2; i >= 0 && acc < 40; i--) { acc += Math.hypot(P[i + 1][0] - P[i][0], P[i + 1][1] - P[i][1]); pre.unshift(P[i]); }
    }
    for (const p of pre) { X.push(p[0]); Z.push(p[1]); }
    for (const p of e.pts) { X.push(p[0]); Z.push(p[1]); }
    const ob = jointAt(e, e.b);
    if (ob >= 0) {
      const o = E[ob], P = o.a === e.b ? o.pts : [...o.pts].reverse();
      let acc = 0;
      for (let i = 1; i < P.length && acc < 40; i++) { acc += Math.hypot(P[i][0] - P[i - 1][0], P[i][1] - P[i - 1][1]); X.push(P[i][0]); Z.push(P[i][1]); }
    }
    const Sa = new Float64Array(X.length);
    for (let i = 1; i < X.length; i++) Sa[i] = Sa[i - 1] + Math.hypot(X[i] - X[i - 1], Z[i] - Z[i - 1]);
    let off = 0; for (let i = 1; i <= pre.length; i++) off = Sa[i]; if (!pre.length) off = 0;
    return { X, Z, S: Sa, off: pre.length ? Sa[pre.length] : 0 };
  }
  /// Signed distance from the true offset curve's centreline (nearest point;
  /// + = left of travel): an offset line's error is sd - offset.
  function sdRef(ref, x, z, sHint) {
    const { X, Z, S: Sa } = ref, n = X.length;
    const target = sHint + ref.off;
    let lo = 0, hi = n - 2;
    while (lo < hi) { const m = (lo + hi + 1) >> 1; if (Sa[m] <= target) lo = m; else hi = m - 1; }
    let best = Infinity, bi = lo, bt = 0;
    const scan = i => {
      const dx = X[i + 1] - X[i], dz = Z[i + 1] - Z[i], L2 = dx * dx + dz * dz;
      const t = L2 > 1e-12 ? Math.max(0, Math.min(1, ((x - X[i]) * dx + (z - Z[i]) * dz) / L2)) : 0;
      const qx = X[i] + dx * t - x, qz = Z[i] + dz * t - z, d = qx * qx + qz * qz;
      if (d < best) { best = d; bi = i; bt = t; }
    };
    for (let i = lo; i >= 0 && Sa[lo] - Sa[i + 1] < 30; i--) scan(i);
    for (let i = lo + 1; i + 1 < n && Sa[i] - Sa[lo + 1] < 30; i++) scan(i);
    // tangent at the foot: the segment's, or at a vertex the mean of its two
    let tx = X[bi + 1] - X[bi], tz = Z[bi + 1] - Z[bi];
    const nrm = (a, b) => { const m = Math.hypot(a, b) || 1; return [a / m, b / m]; };
    [tx, tz] = nrm(tx, tz);
    if (bt <= 1e-9 && bi > 0) { const [px, pz] = nrm(X[bi] - X[bi - 1], Z[bi] - Z[bi - 1]); [tx, tz] = nrm(tx + px, tz + pz); }
    else if (bt >= 1 - 1e-9 && bi + 2 < n) { const [nx, nz] = nrm(X[bi + 2] - X[bi + 1], Z[bi + 2] - Z[bi + 1]); [tx, tz] = nrm(tx + nx, tz + nz); }
    const fx = X[bi] + (X[bi + 1] - X[bi]) * bt, fz = Z[bi] + (Z[bi + 1] - Z[bi]) * bt;
    const side = tx * (z - fz) - tz * (x - fx);
    return (side >= 0 ? 1 : -1) * Math.sqrt(best);
  }
  /// B2 (lone and clustered, lib/kink.mjs) on the design polyline within 1 m
  /// of s: the DATA / BUILDER cause hint.
  const refCache = new Map();
  function dataKinkNear(e, s) {
    let ref = refCache.get(e.index);
    if (!ref) {
      ref = refLine(e);
      const P = ref.X.map((x, i) => ({ x, z: ref.Z[i] }));
      const keep = keepIdx(P, R), C = arcOf(P, keep);
      const KX = keep.map(i => P[i].x), KZ = keep.map(i => P[i].z), TH = new Float64Array(keep.length);
      for (let m = 1; m + 1 < keep.length; m++) TH[m] = turnOf(KX[m - 1], KZ[m - 1], KX[m], KZ[m], KX[m + 1], KZ[m + 1]);
      const sc = kinkScores(KX, KZ, C, TH, R);
      ref.bad = [];
      for (let m = 1; m + 1 < keep.length; m++) if (sc[m] > V) ref.bad.push(ref.S[keep[m]]);
      refCache.set(e.index, ref);
    }
    const t = s + ref.off;
    return ref.bad.some(sv => Math.abs(sv - t) <= 1);
  }

  // ---- D1: every ribbon triangle in a 16 m grid (the census's overlap machinery)
  const TC = 16, triCells = new Map();
  const triList = [];
  const tkey = (cx, cz) => (cx + 8192) * 16384 + (cz + 8192);
  for (const e of E) {
    const secs = e.secs; if (!secs) continue;
    for (let i = 1; i < secs.length; i++) {
      const A = secs[i - 1], B = secs[i];
      if (A.collapsed && B.collapsed) continue;
      for (const [p, q, r] of [[A.L, B.R, B.L], [A.L, A.R, B.R]]) {
        const area = (q[0] - p[0]) * (r[1] - p[1]) - (r[0] - p[0]) * (q[1] - p[1]);
        if (Math.abs(area) < 1e-4) continue;
        triList.push(p[0], p[1], q[0], q[1], r[0], r[1], e.index);
      }
    }
  }
  const nTri = triList.length / 7;
  const triXZ = new Float64Array(nTri * 6), triBox = new Float64Array(nTri * 4), triOwner = new Int32Array(nTri);
  for (let id = 0; id < nTri; id++) {
    for (let k = 0; k < 6; k++) triXZ[id * 6 + k] = triList[id * 7 + k];
    triOwner[id] = triList[id * 7 + 6];
    const x0 = Math.min(triXZ[id * 6], triXZ[id * 6 + 2], triXZ[id * 6 + 4]), x1 = Math.max(triXZ[id * 6], triXZ[id * 6 + 2], triXZ[id * 6 + 4]);
    const z0 = Math.min(triXZ[id * 6 + 1], triXZ[id * 6 + 3], triXZ[id * 6 + 5]), z1 = Math.max(triXZ[id * 6 + 1], triXZ[id * 6 + 3], triXZ[id * 6 + 5]);
    triBox[id * 4] = x0; triBox[id * 4 + 1] = z0; triBox[id * 4 + 2] = x1; triBox[id * 4 + 3] = z1;
    for (let cx = Math.floor(x0 / TC); cx <= Math.floor(x1 / TC); cx++)
      for (let cz = Math.floor(z0 / TC); cz <= Math.floor(z1 / TC); cz++) { const k = tkey(cx, cz); let l = triCells.get(k); if (!l) triCells.set(k, l = []); l.push(id); }
  }
  triList.length = 0;
  /// Minimum signed distance inside the triangle at T6[o..o+5] (positive inside).
  const insideBy = (T6, o, x, z) => {
    const ax = T6[o], az = T6[o + 1], bx = T6[o + 2], bz = T6[o + 3], cx = T6[o + 4], cz = T6[o + 5];
    const sg = ((bx - ax) * (cz - az) - (cx - ax) * (bz - az)) > 0 ? 1 : -1;
    const ed = (px, pz, qx, qz) => { const ex = qx - px, ez = qz - pz; return sg * (ex * (z - pz) - ez * (x - px)) / Math.hypot(ex, ez); };
    return Math.min(ed(ax, az, bx, bz), ed(bx, bz, cx, cz), ed(cx, cz, ax, az));
  };
  const XC = 128, xCells = new Map();
  S.city.crossings.forEach((c, i) => { const k = tkey(Math.floor(c.x / XC), Math.floor(c.z / XC)); let l = xCells.get(k); if (!l) xCells.set(k, l = []); l.push(i); });
  const nbrSet = e => e._nb || (e._nb = new Set([e.index, ...nodeEdges[e.a], ...nodeEdges[e.b]]));
  const sameRoad = (e, i) => nbrSet(e).has(i) || (e.name && E[i].name === e.name);
  function sepNear(e, o, x, z) {   // APPROX (no heights): the census's same-level test
    const cx = Math.floor(x / XC), cz = Math.floor(z / XC);
    for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) {
      const l = xCells.get(tkey(cx + dx, cz + dz)); if (!l) continue;
      for (const i of l) {
        const c = S.city.crossings[i];
        if (Math.hypot(c.x - x, c.z - z) > 80) continue;
        if ((sameRoad(e, c.over) && sameRoad(o, c.under)) || (sameRoad(e, c.under) && sameRoad(o, c.over))) return true;
      }
    }
    return S.elevatedAt(e, S.projectOn(e, [x, z])) !== S.elevatedAt(o, S.projectOn(o, [x, z]));
  }
  function depthInEdge(o, x, z) {
    const secs = o.secs; if (!secs) return 0;
    const so = S.projectOn(o, [x, z]);
    let k = 1; while (k < secs.length - 1 && secs[k].s < so) k++;
    const A = secs[k - 1], B = secs[k];
    const t = B.s - A.s > 1e-6 ? Math.max(0, Math.min(1, (so - A.s) / (B.s - A.s))) : 0;
    const p = S.pointAt(o, so), tan = S.tangentAt(o, so);
    const lat = (x - p[0]) * -tan[1] + (z - p[1]) * tan[0];
    const latL = A.latL + (B.latL - A.latL) * t, latR = A.latR + (B.latR - A.latR) * t;
    return Math.max(0, Math.min(lat - latL, latR - lat));
  }
  /// A plan merge zone (gate spec 4.4: report-only until WP-18b) is a branch
  /// ATTACH ARC only: where a branch runs beside its host, the branch's clip
  /// range (EmitBranch's attached samples, a metre either side, out to the
  /// node - BranchSeats' pieces on the meshes) plus MergeMarginM. The paint of
  /// either road inside the other there is report-only; the same pair
  /// anywhere else - a branch bending back across its host 60 m on - is
  /// gated, and so is paint sharing a fan or a mitred node with another road
  /// ("paint crossing the junction").
  function mergeZoneAt(e, s, o, x, z) {
    if (!S.isClipPair(e.index, o.index)) return false;
    const M = R.MergeMarginM;
    for (const c of S.clipsOf(e.index)) if (c.host.edges.includes(o) && s >= c.sFrom - M && s <= c.sTo + M) return true;
    const oc = S.clipsOf(o.index).filter(c => c.host.edges.includes(e));
    if (!oc.length) return false;
    const so = S.projectOn(o, [x, z]);
    return oc.some(c => so >= c.sFrom - M && so <= c.sTo + M);
  }

  // ---- runs
  const runs = [];
  const stats = { edges: 0, ribbonKm: 0, lineKm: 0, chains: chains.length, strands: 0, samples: 0 };
  const lineKey = (e, s) => Math.round((wayOff[e.index] + s) / R.KeyStepM);
  const BUCKETS = 1048576;   // a bucket id is wayId * BUCKETS + round(s on way / KeyStepM)
  const bucketOf = (e, s) => e.wayId * BUCKETS + Math.max(0, lineKey(e, s));
  const minRunLen = { A5: R.StrayRunM, A5b: R.StrayRunM };
  /// Consecutive bad samples along one line, merged into a run and streamed
  /// (no sample objects: the city is thirty million of them). A run breaks
  /// where two bad samples lie more than RunBreakM apart, and keeps for every
  /// KeyStepM bucket it touches the worst value and the bad length there (the
  /// arc from the previous bad sample goes to the bucket of this one), and the
  /// side of the ribbon its samples there lie on: its ratchet keys.
  // ---- the agreement instrument (opts.trace, linecheck --trace): this gate's highest reading, as a ratio to the
  // check's limit, at each of the OTHER gate's runs - on the same check, edge and line (maxE) or line class (maxC,
  // the offset dropped), within the given s range - so a run one gate has and the other lacks says what the other
  // read there. Editor/CitySmooth.cs PSX_SMOOTH_TRACE is the same instrument on the meshes.
  const TR = opts.trace ? new Map() : null;
  if (TR) for (const t of opts.trace) {
    const k = t.check + '|' + t.e; let l = TR.get(k); if (!l) TR.set(k, l = []);
    l.push(Object.assign(t, { cls: lineClass(t.line), maxE: -1, nE: 0, maxC: -1, nC: 0 }));
  }
  const limitOf = check => check === 'A4' ? R.LineWidthTol : check === 'A5' || check === 'A5b' ? R.StrayM : check === 'C1' ? R.GapM
    : check === 'C2' ? 1 : check === 'C3' ? R.DashTol : check === 'D1' ? R.CrossM : V;
  const traceSample = (check, extra, e, s, val, rl, side, lid) => {
    const l = TR.get(check + '|' + e); if (!l) return;
    const line = extra && extra.kind === 'edge' && side ? 'R' + side : lid, cls = lineClass(line);
    const r = check === 'B3' ? (rl > 0 && val > 0 ? rl / val : 0) : Math.abs(val) / limitOf(check);
    for (const t of l) {
      if (s < t.s0 || s > t.s1) continue;
      if (line === t.line) { t.nE++; if (r > t.maxE) t.maxE = r; }
      if (cls === t.cls) { t.nC++; if (r > t.maxC) t.maxC = r; }
    }
  };
  class RunBuilder {
    constructor(check, lineId, extra = null, minIsWorse = false) { this.check = check; this.lineId = lineId; this.extra = extra; this.minIsWorse = minIsWorse; this.cur = null; this.arc = 0; this.px = NaN; this.pz = NaN; }
    /// A break in the samples (between B1 windows): closes, no arc across it.
    gap() { this.close(); this.px = NaN; }
    push(x, z, e, s, val, bad, tag, span, side, what, lid, rl) {
      if (TR) traceSample(this.check, this.extra, e, s, val, rl, side, lid ?? this.lineId);
      if (!Number.isNaN(this.px)) this.arc += Math.hypot(x - this.px, z - this.pz);
      this.px = x; this.pz = z;
      if (!bad) { this.close(); return; }
      let c = this.cur;
      if (c && this.arc - c.lastArc > R.RunBreakM) { this.close(); c = null; }
      const inc = c ? this.arc - c.lastArc : 0;
      if (!c) {
        c = this.cur = { check: this.check, lineId: lid ?? this.lineId, e, s, x, z, val, len: 0, a0: this.arc, lastArc: this.arc, e0: e, s0: s, e1: e, s1: s, tag, span, side, bk: [], bv: [], bl: [], bs: [], bn: [], br: [] };
        if (what !== undefined) c.what = what;
        if (this.extra) Object.assign(c, this.extra);
        if (rl !== undefined) c.rLimit = rl;
      }
      c.len = this.arc - c.a0; c.lastArc = this.arc; c.e1 = e; c.s1 = s;
      // a sample's own limit (B3: its own edge's class): the worst is the tightest against its own limit
      const worseThan = (v, r, cv, cr) => this.minIsWorse ? (r !== undefined && cr !== undefined ? v / r < cv / cr : v < cv) : Math.abs(v) > Math.abs(cv);
      const worse = worseThan(val, rl, c.val, c.rLimit);
      const bid = bucketOf(E[e], s), nb = c.bk.length;
      // the arc from the previous bad sample: half to its bucket, half to this one's (all of it to this one's made a
      // key's bad length depend on which way the line was walked - review 8)
      if (nb && c.bk[nb - 1] === bid) {
        if (worseThan(val, rl, c.bv[nb - 1], c.br[nb - 1])) { c.bv[nb - 1] = val; c.br[nb - 1] = rl; }
        c.bl[nb - 1] += inc;
        if (lid !== undefined && (c.bn[nb - 1] === undefined || lid < c.bn[nb - 1])) c.bn[nb - 1] = lid;
      }
      else { if (nb) c.bl[nb - 1] += inc / 2; c.bk.push(bid); c.bv.push(val); c.bl.push(nb ? inc / 2 : inc); c.bs.push(side); c.bn.push(lid); c.br.push(rl); }
      if (worse) { c.val = val; c.e = e; c.s = s; c.x = x; c.z = z; c.tag = tag; c.span = span; c.side = side; if (what !== undefined) c.what = what; if (lid !== undefined) c.lineId = lid; if (rl !== undefined) c.rLimit = rl; }
    }
    close() { if (this.cur) { runs.push(this.cur); this.cur = null; } }
  }
  /// Emit runs from an ordered sample list: [{x, z, e, s, val, bad}] or {gap: true}.
  function emitRuns(check, lineId, samples, limit, extra = {}, minIsWorse = false) {
    const b = new RunBuilder(check, lineId, extra, minIsWorse);
    for (const p of samples) { if (p.gap) b.gap(); else b.push(p.x, p.z, p.e, p.s, p.val, p.bad, p.tag, p.span, p.side, p.what, p.lid, p.rl); }
    b.close();
  }

  // ---- extraction of one edge (in its own direction)
  function extract(e) {
    const secs = e.secs, prof = e.profile, W = prof.width;
    const P = plans.get(prof.key);
    const nSpan = secs.length - 1;
    const spans = [];
    for (let i = 1; i < secs.length; i++) {
      const A = secs[i - 1], B = secs[i];
      const skip = A.collapsed && B.collapsed;
      const elev = A.elev || B.elev;
      const surf = surfaceKey(e, elev);
      const q = { AL: { x: A.L[0], z: A.L[1], u: A.uL, v: A.v }, AR: { x: A.R[0], z: A.R[1], u: A.uR, v: A.v },
                  BL: { x: B.L[0], z: B.L[1], u: B.uL, v: B.v }, BR: { x: B.R[0], z: B.R[1], u: B.uR, v: B.v } };
      spans.push({ i, A, B, skip, surf, q, taper: Math.abs(A.hw - B.hw) > 0.005,
        cropL: A.sqL || B.sqL || (A.clippedIn && A.innerSide < 0) || (B.clippedIn && B.innerSide < 0) || A.collapsed || B.collapsed,
        cropR: A.sqR || B.sqR || (A.clippedIn && A.innerSide > 0) || (B.clippedIn && B.innerSide > 0) || A.collapsed || B.collapsed });
    }
    // the texture's runs (every span of one profile paints the same layout; the surface only changes grain)
    const lay = P.bySurf[spans[0].surf] || Object.values(P.bySurf)[0];
    const slat = (sp, x, z, tag) => {
      const { A, B } = sp;
      if (tag === 'A') return { s: A.s, lat: (x - A.p[0]) * A.right[0] + (z - A.p[1]) * A.right[1] };
      if (tag === 'B') return { s: B.s, lat: (x - B.p[0]) * B.right[0] + (z - B.p[1]) * B.right[1] };
      let f = frameOf(A, B, [x, z]);
      if (!(f.t > -0.1 && f.t < 1.1)) {
        const D = sub(B.p, A.p), DD = dot(D, D);
        const t = DD > 1e-9 ? Math.max(0, Math.min(1, dot([x - A.p[0], z - A.p[1]], D) / DD)) : 0;
        const r = [A.right[0] + (B.right[0] - A.right[0]) * t, A.right[1] + (B.right[1] - A.right[1]) * t], rl = Math.hypot(r[0], r[1]) || 1;
        f = { t, lat: ((x - A.p[0] - D[0] * t) * r[0] + (z - A.p[1] - D[1] * t) * r[1]) / rl };
      }
      return { s: A.s + (B.s - A.s) * f.t, lat: f.lat };
    };
    // painted lines, per run
    const lines = lay.runs.map((run, k) => {
      const j = lay.runPlan[k];
      const L = { k, run, plan: j >= 0 ? P.plan[j] : null, q: lay.runQc[k], pieces: [] };
      let piece = null;
      for (const sp of spans) {
        if (sp.skip) { piece = null; continue; }
        for (const sg of quadIso(sp.q, run.u)) {
          const ratio = 1 / (sg.grad * W);
          const mk = (c, tag) => { const f = slat(sp, c.x, c.z, tag); return { x: c.x, z: c.z, v: c.v, tag, span: sp.i, s: f.s, lat: f.lat, e: e.index }; };
          const last = piece && piece.pts[piece.pts.length - 1];
          if (last && Math.abs(last.x - sg.a.x) < 1e-9 && Math.abs(last.z - sg.a.z) < 1e-9) {
            piece.pts.push(mk(sg.b, sg.eb)); piece.ratio.push(ratio);
          } else {
            piece = { pts: [mk(sg.a, sg.ea), mk(sg.b, sg.eb)], ratio: [ratio] };
            L.pieces.push(piece);
          }
        }
      }
      return L;
    });
    // ribbon edges and midline, per section
    const rib = { L: [], R: [] }, mid = [];
    for (const side of ['L', 'R']) {
      let piece = null;
      for (const sp of spans) {
        if (sp.skip) { piece = null; continue; }
        const mk = (sec, si) => {
          const P2 = side === 'L' ? sec.L : sec.R;
          const crop = side === 'L' ? (sec.sqL || (sec.clippedIn && sec.innerSide < 0) || sec.collapsed) : (sec.sqR || (sec.clippedIn && sec.innerSide > 0) || sec.collapsed);
          const x3 = (sec.clippedIn && sec.innerSide === (side === 'L' ? -1 : 1)) || sec.collapsed;
          return { x: P2[0], z: P2[1], s: sec.s, lat: side === 'L' ? sec.latL : sec.latR, sec: si, crop, x3, sq: side === 'L' ? sec.sqL : sec.sqR, e: e.index, side };
        };
        if (!piece) { piece = [mk(sp.A, sp.i - 1)]; rib[side].push(piece); }
        piece.push(mk(sp.B, sp.i));
      }
    }
    {
      let piece = null;
      for (const sp of spans) {
        if (sp.skip) { piece = null; continue; }
        const mk = (sec, si) => ({ x: (sec.L[0] + sec.R[0]) / 2, z: (sec.L[1] + sec.R[1]) / 2, s: sec.s, sec: si, e: e.index,
          x3: sec.clippedIn || sec.collapsed, crop: sec.sqL || sec.sqR || sec.clippedIn || sec.collapsed });
        if (!piece) { piece = [mk(sp.A, sp.i - 1)]; mid.push(piece); }
        piece.push(mk(sp.B, sp.i));
      }
    }
    return { e, secs, spans, nSpan, lines, rib, mid, plan: P.plan, lay, ref: refLine(e) };
  }

  // ---- A-family + D1 + grids on one edge
  function checkEdge(ed, chainSet) {
    const { e, spans, lines, rib, ref } = ed;
    const sMin = ed.secs[0].s, sMax = ed.secs[ed.secs.length - 1].s;
    const BIN = R.SampleStepM;
    const nb = Math.max(1, Math.ceil((sMax - sMin) / BIN));
    const binS = k => sMin + (k + 0.5) * BIN;
    const spanAt = s => { let lo = 0, hi = spans.length - 1; while (lo < hi) { const m = (lo + hi) >> 1; if (spans[m].B.s < s) lo = m + 1; else hi = m; } return spans[lo]; };
    stats.edges++; stats.ribbonKm += (sMax - sMin) / 1000;
    const sdOf = (x, z, s) => sdRef(ref, x, z, s);
    // grid of each line's signed distance (first hit per bin, like the census)
    const grid = lines.map(() => new Float64Array(nb).fill(NaN));
    const gridRib = { L: new Float64Array(nb).fill(NaN), R: new Float64Array(nb).fill(NaN) };
    const fillGrid = (G, a, b, sda, sdb) => {
      const lo = Math.min(a.s, b.s), hi = Math.max(a.s, b.s);
      const k0 = Math.max(0, Math.ceil((lo - sMin) / BIN - 0.5)), k1 = Math.min(nb - 1, Math.floor((hi - sMin) / BIN - 0.5));
      for (let k = k0; k <= k1; k++) { const t = hi - lo > 1e-9 ? (binS(k) - a.s) / (b.s - a.s) : 0; if (Number.isNaN(G[k])) G[k] = sda + (sdb - sda) * t; }
    };
    // ---- painted lines: A1, A4, A5 (+ D1) on samples; the grid
    for (const L of lines) {
      const plan = L.plan, col = L.run.col;
      const lineId = plan ? plan.id : `T${col}${L.run.dashed ? 'd' : 's'}u${L.run.u.toFixed(3)}`;
      for (const pc of L.pieces) {
        const bA1 = new RunBuilder('A1', lineId), bA4 = new RunBuilder('A4', lineId), bA5 = new RunBuilder('A5', lineId);
        const bD1 = new RunBuilder('D1', lineId), bD1m = new RunBuilder('D1', lineId, { reportOnly: 'merge zone (a branch attach arc: the clip range + MergeMarginM): report-only until WP-18b' });
        for (let i = 1; i < pc.pts.length; i++) {
          const a = pc.pts[i - 1], b = pc.pts[i];
          const segL = Math.hypot(b.x - a.x, b.z - a.z);
          stats.lineKm += segL / 1000;
          const n = Math.max(1, Math.ceil(segL / BIN));
          const sda = sdOf(a.x, a.z, a.s), sdb = sdOf(b.x, b.z, b.s);
          fillGrid(grid[L.k], a, b, sda, sdb);
          const ratio = pc.ratio[i - 1];
          const wv = Number.isFinite(ratio) ? ratio - 1 : 0, wbad = Math.abs(wv) > R.LineWidthTol;
          for (let m = i === 1 ? 0 : 1; m <= n; m++) {
            const t = m / n, x = a.x + (b.x - a.x) * t, z = a.z + (b.z - a.z) * t, s = a.s + (b.s - a.s) * t;
            const sd = m === 0 ? sda : m === n ? sdb : sdOf(x, z, s);
            const tag = m === 0 ? a.tag : m === n ? b.tag : 'I', span = b.span;
            const hw = hwD(e, s);
            stats.samples++;
            // A1: against its own plan line, where the plan has it
            if (plan && (plan.anchor === 'C' ? Math.abs(plan.off) < hw - R.ExistInsetM : hw - plan.inset > 0)) {
              const err = sd - ((plan.anchor === 'C' ? plan.off : plan.anchor === 'EL' ? hw - plan.inset : -(hw - plan.inset)) + L.q);
              bA1.push(x, z, e.index, s, err, Math.abs(err) > V, tag, span);
            } else bA1.push(x, z, e.index, s, 0, false);
            // A4: rendered width against the run's at nominal scale
            bA4.push(x, z, e.index, s, wv, wbad, tag, span);
            // A5: nearest plan line of this colour
            let dmin = Infinity;
            for (let j = 0; j < ed.plan.length; j++) {
              const pj = ed.plan[j];
              if (pj.col !== col) continue;
              if (pj.anchor === 'C' ? !(Math.abs(pj.off) < hw - R.ExistInsetM) : !(hw - pj.inset > 0)) continue;
              const kk = ed.lay.planRun[j];
              const off = pj.anchor === 'C' ? pj.off : pj.anchor === 'EL' ? hw - pj.inset : -(hw - pj.inset);
              const d = Math.abs(sd - off - (kk >= 0 ? ed.lay.runQc[kk] : 0));
              if (d < dmin) dmin = d;
            }
            bA5.push(x, z, e.index, s, Math.min(dmin, 10), dmin > R.StrayM, tag, span);
            // D1: inside another ribbon at the same level
            let depth = 0, other = -1;
            const l = triCells.get(tkey(Math.floor(x / TC), Math.floor(z / TC)));
            if (l) for (let q = 0; q < l.length; q++) {
              const id = l[q], oi = triOwner[id];
              if (oi === e.index || chainSet.has(oi)) continue;
              if (x < triBox[id * 4] || x > triBox[id * 4 + 2] || z < triBox[id * 4 + 1] || z > triBox[id * 4 + 3]) continue;
              if (insideBy(triXZ, id * 6, x, z) < 0.01) continue;
              if (S.isSeparatedPair(e.index, oi) || sepNear(e, E[oi], x, z)) continue;
              const d = depthInEdge(E[oi], x, z);
              if (d > depth) { depth = d; other = oi; }
            }
            const cross = depth > R.CrossM, mz = cross && mergeZoneAt(e, s, E[other], x, z);
            const into = cross ? `inside e${other}` : undefined;
            bD1.push(x, z, e.index, s, depth, cross && !mz, tag, span, undefined, into);
            bD1m.push(x, z, e.index, s, depth, cross && mz, tag, span, undefined, into);
          }
        }
        bA1.close(); bA4.close(); bA5.close(); bD1.close(); bD1m.close();
      }
    }
    // ---- ribbon edges: A1-edge. An unsqueezed section against the design edge; a SQUEEZED one (X6 no longer exempts
    // it: a smooth squeeze step wandered through every shape check) against its I7 envelope (squeezeDev); a clipped
    // inner edge or a gore nose (X3/X2) not at all - the host's edge carries it
    for (const side of ['L', 'R']) {
      const sgn = side === 'L' ? -1 : 1;
      for (const pc of rib[side]) {
        const smp = [];
        for (let i = 1; i < pc.length; i++) {
          const a = pc[i - 1], b = pc[i];
          const segL = Math.hypot(b.x - a.x, b.z - a.z), n = Math.max(1, Math.ceil(segL / BIN));
          const sda = sdOf(a.x, a.z, a.s), sdb = sdOf(b.x, b.z, b.s);
          fillGrid(gridRib[side], a, b, sda, sdb);
          const x3 = a.x3 || b.x3, sq = !x3 && (a.sq || b.sq);
          for (let m = i === 1 ? 0 : 1; m <= n; m++) {
            const t = m / n, x = a.x + (b.x - a.x) * t, z = a.z + (b.z - a.z) * t, s = a.s + (b.s - a.s) * t;
            const sd = x3 ? 0 : m === 0 ? sda : m === n ? sdb : sdOf(x, z, s);
            smp.push({ x, z, s, x3, sq, err: x3 ? 0 : sd - sgn * hwD(e, s), tag: m === 0 || m === n ? 'S' : 'I', sec: b.sec });
          }
        }
        const dev = smp.some(q => q.sq) ? squeezeDev(smp, sgn, R.taperFloorFor(e.klass), V) : null;
        const bA1 = new RunBuilder('A1', 'R' + side);
        smp.forEach((q, i) => {
          if (q.x3) { bA1.push(q.x, q.z, e.index, q.s, 0, false); return; }
          const d = dev ? dev[i] : 0, env = d > V && (q.sq || d > Math.abs(q.err));
          const val = q.sq || d > Math.abs(q.err) ? d : q.err;
          bA1.push(q.x, q.z, e.index, q.s, val, env || (!q.sq && Math.abs(q.err) > V), q.tag, q.sec, side, env ? SQUEEZE_WHAT : null);
        });
        bA1.close();
      }
    }
    // ---- A2 SKEW (undivided two-way rows): the centre pair against the drawn midline
    const plan = ed.plan;
    if (e.profile.key.startsWith('tw')) {
      const ys = plan.map((p, j) => ({ p, j })).filter(o => o.p.anchor === 'C' && o.p.col === 'Y');
      if (ys.length >= 2) {
        ys.sort((a, b) => a.p.off - b.p.off);
        const k1 = ed.lay.planRun[ys[0].j], k2 = ed.lay.planRun[ys[ys.length - 1].j];
        if (k1 >= 0 && k2 >= 0) {
          const smp = [];
          for (let k = 0; k < nb; k++) {
            const s = binS(k), sp = spanAt(s);
            const a = grid[k1][k], b = grid[k2][k], l = gridRib.L[k], r = gridRib.R[k];
            const p = S.pointAt(e, s);
            if (sp.cropL || sp.cropR || [a, b, l, r].some(Number.isNaN)) { smp.push({ x: p[0], z: p[1], e: e.index, s, val: 0, bad: false }); continue; }
            const skew = (a + b) / 2 - (ed.lay.runQc[k1] + ed.lay.runQc[k2]) / 2 - (l + r) / 2;
            smp.push({ x: p[0], z: p[1], e: e.index, s, val: skew, bad: Math.abs(skew) > V, span: sp.i });
          }
          emitRuns('A2', 'CPAIR', smp, V);
        }
      }
    }
    // ---- A3 INSET: each edge line against its own drawn edge
    for (const [pid, side, sgn] of [['EL', 'R', 1], ['ER', 'L', -1]]) {
      const j = plan.findIndex(p => p.id === pid), k = j >= 0 ? ed.lay.planRun[j] : -1;
      if (k < 0) continue;
      // the PLAN's inset (shoulder + EdgeLineInsetM), less only the texture's half-texel rounding
      const planInset = plan[j].inset - sgn * ed.lay.runQc[k];
      const smp = [];
      for (let b = 0; b < nb; b++) {
        const s = binS(b), sp = spanAt(s), p = S.pointAt(e, s);
        const ln = grid[k][b], rb = gridRib[side][b];
        if ((side === 'L' ? sp.cropL : sp.cropR) || Number.isNaN(ln) || Number.isNaN(rb)) { smp.push({ x: p[0], z: p[1], e: e.index, s, val: 0, bad: false }); continue; }
        const d = Math.abs(rb - ln) - planInset;
        smp.push({ x: p[0], z: p[1], e: e.index, s, val: d, bad: Math.abs(d) > V, span: sp.i });
      }
      emitRuns('A3', pid, smp, V);
    }
    // ---- A5b MISSING (report-only): a plan line with no rendered line of its colour within StrayM
    for (let j = 0; j < plan.length; j++) {
      const pj = plan[j], smp = [];
      for (let b = 0; b < nb; b++) {
        const s = binS(b), p = S.pointAt(e, s);
        if (!planExists(pj, e, s)) { smp.push({ x: p[0], z: p[1], e: e.index, s, val: 0, bad: false }); continue; }
        let near = Infinity;
        for (const L of lines) {
          if (L.run.col !== pj.col) continue;
          const g = grid[L.k][b]; if (Number.isNaN(g)) continue;
          near = Math.min(near, Math.abs(g - (planOff(pj, e, s) + L.q)));
        }
        smp.push({ x: p[0], z: p[1], e: e.index, s, val: Math.min(near, 10), bad: near > R.StrayM });
      }
      emitRuns('A5b', pj.id, smp, R.StrayM);
    }
    // ---- C1 GAP: a solid line interrupted inside the edge where the plan has it
    for (const L of lines) {
      if (L.run.dashed || !L.plan) continue;
      const ps = [...L.pieces].sort((a, b) => Math.min(a.pts[0].s, a.pts.at(-1).s) - Math.min(b.pts[0].s, b.pts.at(-1).s));
      for (let i = 1; i < ps.length; i++) {
        const a = ps[i - 1].pts.reduce((m, p) => p.s > m.s ? p : m), b = ps[i].pts.reduce((m, p) => p.s < m.s ? p : m);
        const gap = Math.hypot(b.x - a.x, b.z - a.z);
        const sm = (a.s + b.s) / 2;
        if (gap > R.GapM && planExists(L.plan, e, sm))
          runs.push({ check: 'C1', lineId: L.plan.id, e: e.index, s: sm, x: (a.x + b.x) / 2, z: (a.z + b.z) / 2, val: gap, len: gap, e0: e.index, s0: a.s, e1: e.index, s1: b.s, span: a.span });
      }
    }
    return { grid };
  }

  // ---- B-family on one strand (an ordered, joined polyline)
  /// rLimit: B3's limit, a function of an edge index (ribbon edges: InnerEdgeMinRM; the midline: its OWN edge's class
  /// R_min - a strand that crosses from a link onto a motorway was judged at the link's 25 m all along, because the
  /// limit came from the strand's first edge, and the two gates start strands at different edges), or 0: no B3.
  function shapeChecks(kind, lineId, pts, rLimit) {
    if (pts.length < 3) return;
    if (opts.reverseStrands) pts = [...pts].reverse();   // a debugging switch (linecheck --reverse): the answer must not depend on it
    const keep = keepIdx(pts, R);
    if (keep.length < 3) return;
    const C = arcOf(pts, keep);
    const Ltot = C[C.length - 1];
    // B2 KINK at every kept interior vertex: lone and clustered corners (lib/kink.mjs)
    const TH = new Float64Array(keep.length), KX = new Float64Array(keep.length), KZ = new Float64Array(keep.length);
    for (let m = 0; m < keep.length; m++) { KX[m] = pts[keep[m]].x; KZ[m] = pts[keep[m]].z; }
    for (let m = 1; m + 1 < keep.length; m++) TH[m] = turnOf(KX[m - 1], KZ[m - 1], KX[m], KZ[m], KX[m + 1], KZ[m + 1]);
    // the most any line of the strand stands off its data line (the WAVE rule's geometry test): 0 on the midline
    let hw = 0;
    if (kind !== 'midline') for (const i of keep) if (E[pts[i].e].hw > hw) hw = E[pts[i].e].hw;
    const K2 = kinkScores(KX, KZ, C, TH, R, m => { const b = pts[keep[m]]; return !!(b.x3 || b.gore); }, hw);
    // the strand dump (opts.strands, linecheck --strands; CitySmooth PSX_SMOOTH_STRANDS is the same)
    if (opts.strands && keep.some(i => opts.strands.edges.has(pts[i].e))) {
      opts.strands.out.push(`S ${kind} ${lineId} n ${keep.length}`);
      keep.forEach((i, m) => { const p = pts[i]; opts.strands.out.push(`${p.e} ${+p.s.toFixed(4)} ${+p.x.toFixed(5)} ${+p.z.toFixed(5)} ${p.x3 || p.gore ? 1 : 0} ${+K2[m].toFixed(5)}`); });
    }
    const b2 = [];
    for (let m = 1; m + 1 < keep.length; m++) {
      const b = pts[keep[m]];
      if (b.x3 || b.gore) { b2.push({ ...b, val: 0, bad: false }); continue; }
      // collinear in the frame of its curve: no sample (lib/kink.mjs arcFrame)
      if (K2.silent && K2.silent[m]) continue;
      const f = K2[m];
      b2.push({ x: b.x, z: b.z, e: b.e, s: b.s, val: f, bad: f > V, tag: b.tag, span: b.span ?? b.sec, side: b.side, what: f > V ? kindText(K2.kind[m]) : null, lid: b.lid });
    }
    emitRuns('B2', lineId, b2, V, { kind });
    // B3 CURVE (ribbon edges and midline only)
    if (typeof rLimit === 'function') {
      const b3 = [];
      let hint = 0;
      for (let m = 1; m + 1 < keep.length; m++) {
        const b = pts[keep[m]];
        if (C[m] < R.CurveHalfM || Ltot - C[m] < R.CurveHalfM || b.x3 || b.gore) { b3.push({ ...b, val: 0, bad: false }); continue; }
        const p0 = pointAtArc(pts, keep, C, C[m] - R.CurveHalfM, hint), p1 = pointAtArc(pts, keep, C, C[m] + R.CurveHalfM, m);
        hint = p0.i;
        const h1 = Math.atan2(b.z - p0.z, b.x - p0.x), h2 = Math.atan2(p1.z - b.z, p1.x - b.x);
        let dpsi = Math.abs(h2 - h1); if (dpsi > Math.PI) dpsi = 2 * Math.PI - dpsi;
        const Rr = dpsi > 1e-9 ? R.CurveHalfM / dpsi : Infinity;
        const lim = rLimit(b.e);
        b3.push({ x: b.x, z: b.z, e: b.e, s: b.s, val: Rr, bad: Rr < lim, span: b.span ?? b.sec, side: b.side, rl: lim });
      }
      emitRuns('B3', lineId, b3, 0, { kind }, true);
    }
    // B1 JITTER: 0.25 m samples within JitterHalfM of a turning vertex, Kasa fit over +-JitterHalfM
    const step = R.JitterStepM, H = R.JitterHalfM, nH = Math.round(H / step);
    // stations anchored to the WAY's arc (b1Stations): the same samples - and readings - whichever way the strand is
    // walked and wherever a gate cuts it (the mesh gate cuts strands at its 3x3 ring, this one at a chain's ends)
    const cand = b1Stations(pts, keep, C, Ltot, step, H, p => wayOff[p.e] + p.s, p => E[p.e].wayId);
    if (!cand.length) return;
    const b1 = [];
    const xs = new Float64Array(2 * nH + 1), zs = new Float64Array(2 * nH + 1);
    let last = -1, hint = 0, wlo = 0;
    for (const a of cand) {
      if (Number.isNaN(a)) { if (last >= 0) b1.push({ gap: true }); last = -1; continue; }   // between two windows
      if (last >= 0 && Math.abs(a - last) < 1e-6) continue;
      last = a;
      let exempt = false;
      for (let j = -nH; j <= nH; j++) {
        const p = pointAtArc(pts, keep, C, a + j * step, hint);
        if (j === -nH) hint = p.i;
        xs[j + nH] = p.x; zs[j + nH] = p.z;
        // exempt where the line it samples is: on a vertex, that vertex; inside a segment, both its ends (review 8:
        // the segment's START alone made a window exempt walked one way and judged walked the other)
        const q0 = pts[keep[p.i]], q1 = pts[keep[Math.min(p.i + 1, keep.length - 1)]];
        const x0 = !!(q0.x3 || q0.gore), x1 = !!(q1.x3 || q1.gore);
        if (p.t <= 0 ? x0 : p.t >= 1 ? x1 : x0 && x1) exempt = true;
      }
      const centre = pointAtArc(pts, keep, C, a, hint), src = pts[keep[centre.i]], nxt = pts[keep[Math.min(centre.i + 1, keep.length - 1)]];
      // the sample's own (edge, s): interpolated along its segment (a kept segment can be tens of metres long); its
      // side and name from the nearer end (a ribbon edge's side flips at a joint the chain runs backwards)
      // (a station at the midpoint of a segment between two ways - the fallback - is a tie: the firstEnd end)
      const near = centre.t < 0.5 - 1e-9 ? src : centre.t > 0.5 + 1e-9 ? nxt : firstEnd(src, nxt) ? src : nxt;
      const sE = src.e === nxt.e ? src.e : near.e;
      const sS = src.e === nxt.e ? src.s + (nxt.s - src.s) * centre.t : near.s;
      if (exempt) { b1.push({ x: centre.x, z: centre.z, e: sE, s: sS, val: 0, bad: false }); continue; }
      // prefilter: every vertex of the window within V/2 of the window's chord -> the fit leaves < V
      const cx = xs[2 * nH] - xs[0], cz = zs[2 * nH] - zs[0], cl = Math.hypot(cx, cz) || 1;
      let dev = 0;
      while (wlo < keep.length && C[wlo] <= a - H) wlo++;
      for (let m = wlo; m < keep.length && C[m] < a + H; m++) {
        const p = pts[keep[m]];
        dev = Math.max(dev, Math.abs(((p.x - xs[0]) * cz - (p.z - zs[0]) * cx) / cl));
      }
      const res = dev < V / 2 ? 0 : kasaResidual(xs, zs, nH);
      b1.push({ x: centre.x, z: centre.z, e: sE, s: sS, val: res, bad: res > V, span: near.span ?? near.sec, side: near.side, lid: near.lid });
    }
    emitRuns('B1', lineId, b1, V, { kind });
  }

  // ---- C3 DASH along one identity chain of a dashed line
  function dashChecks(lineId, pieces, run) {
    // pieces: ordered [{pts}], linked end to start (joined or jumped at a node)
    const lo = R.DashM * (1 - R.DashTol), hi = R.DashM * (1 + R.DashTol), glo = R.DashGapM * (1 - R.DashTol), ghi = R.DashGapM * (1 + R.DashTol);
    const onAt = v => { const f = v - Math.floor(v); return f >= run.vOn0 && f < run.vOn1; };
    let state = null, len = 0, start = null, truncatedStart = true;
    const out = [];
    const close = (endPt, truncatedEnd) => {
      if (state === null) return;
      const lim = state ? [lo, hi] : [glo, ghi];
      // reported at firstEnd, named after that end's line: one run whichever way the chain runs
      const q = firstEnd(start, endPt) ? start : endPt, qid = q.lid ?? lineId;
      if (!truncatedStart && !truncatedEnd && (len < lim[0] || len > lim[1]))
        out.push({ check: 'C3', lineId: qid, e: q.e, s: q.s, x: q.x, z: q.z, val: len / (state ? R.DashM : R.DashGapM) - 1, len,
                   e0: start.e, s0: start.s, e1: endPt.e, s1: endPt.s, what: state ? 'dash' : 'gap', span: q.span });
      else if (state && (truncatedStart || truncatedEnd) && len < R.StubM)
        out.push({ check: 'C3', lineId: qid, e: q.e, s: q.s, x: q.x, z: q.z, val: len / R.DashM - 1, len, e0: start.e, s0: start.s, e1: endPt.e, s1: endPt.s,
                   what: 'stub', reportOnly: 'a truncated dash under 1 m at a mouth or gore: report-only until WP-17', span: q.span });
    };
    for (let pi = 0; pi < pieces.length; pi++) {
      const pts = pieces[pi].pts, prevLast = pi > 0 ? pieces[pi - 1].pts.at(-1) : null;
      for (let i = 1; i < pts.length; i++) {
        const a = pts[i - 1], b = pts[i];
        const segL = Math.hypot(b.x - a.x, b.z - a.z);
        if (segL < 1e-9) continue;
        // the V boundaries (n + vOn0, n + vOn1) inside this segment, by t
        const v0 = a.v, v1 = b.v;
        const ts = [0];
        const vmin = Math.min(v0, v1), vmax = Math.max(v0, v1);
        for (let n = Math.floor(vmin) - 1; n <= Math.ceil(vmax) + 1; n++)
          for (const edge of [n + run.vOn0, n + run.vOn1]) if (edge > vmin && edge < vmax) ts.push((edge - v0) / (v1 - v0));
        ts.push(1); ts.sort((p, q) => p - q);
        for (let k = 1; k < ts.length; k++) {
          const tm = (ts[k - 1] + ts[k]) / 2, on = onAt(v0 + (v1 - v0) * tm), dl = segL * (ts[k] - ts[k - 1]);
          if (dl < 1e-9) continue;
          if (state !== on) {
            let at = { x: a.x + (b.x - a.x) * ts[k - 1], z: a.z + (b.z - a.z) * ts[k - 1], e: a.e, s: a.s + (b.s - a.s) * ts[k - 1], span: b.span, lid: a.lid };
            // at the joint between two pieces: the joint's firstEnd point (review 8: a dash or gap ending at a joint was
            // named after the piece the walk reached it on - e11484 one way, e1904 the other)
            if (prevLast && i === 1 && k === 1 && firstEnd(prevLast, a)) at = { x: prevLast.x, z: prevLast.z, e: prevLast.e, s: prevLast.s, span: prevLast.span, lid: prevLast.lid };
            close(at, false);
            truncatedStart = state === null; state = on; len = 0; start = at;
          }
          len += dl;
        }
      }
    }
    const lastPts = pieces[pieces.length - 1].pts, endPt = lastPts[lastPts.length - 1];
    close(endPt, true);
    for (const r of out) runs.push(r);
  }

  // ---- one chain at a time
  let chainNo = 0;
  for (const chain of chains) {
    chainNo++;
    if (opts.onlyEdges && !chain.some(c => opts.onlyEdges.has(c.e.index))) continue;
    // D1 leaves out the line's own road: the edges mitred to it (a bend fan's other arm is NOT its own road -
    // paint of one arm inside the other is paint crossing the slab's corner)
    const eds = chain.map(c => ({ ...extract(c.e), fwd: c.fwd, linkIn: c.linkIn }));
    const groupOf = [];
    eds.forEach((ed, c) => { groupOf.push(c > 0 && ed.linkIn === 'mitre' ? groupOf[c - 1] : c); });
    const groups = new Map();
    eds.forEach((ed, c) => { let g = groups.get(groupOf[c]); if (!g) groups.set(groupOf[c], g = new Set()); g.add(ed.e.index); });
    eds.forEach((ed, c) => checkEdge(ed, groups.get(groupOf[c])));

    // orient every strand to the chain's direction
    const orient = (ed, pts) => ed.fwd ? pts : [...pts].reverse();
    const nodeOf = (ed, end) => (end === 'start') === ed.fwd ? ed.e.a : ed.e.b;
    // gore noses: within GoreNoseM of a collapsed section (X2, edge shape checks)
    for (const ed of eds) {
      const coll = ed.secs.filter(c => c.collapsed).map(c => c.s);
      if (!coll.length) continue;
      for (const side of ['L', 'R']) for (const pc of ed.rib[side]) for (const p of pc) if (coll.some(s => Math.abs(s - p.s) <= R.GoreNoseM)) p.gore = true;
    }
    // ---- ribbon edges and midline: across a mitred joint the strand runs on when the two ends are within V
    // (a sub-V step is invisible, and a kink right at the node must still be judged: joinEnds moves the next
    // piece onto the strand's end), else a JUMP (B4) and a new strand. Across a BEND FAN (a 2-arm node drawn as a junction
    // slab; plan A2/A7: never legitimate) it runs on straight across the slab, from mouth to mouth, so B2/B3
    // judge the corner the slab draws; no JUMP there - the slab's corner is the fault, and it is reported.
    const joinStrands = (getPieces, kindName, rLimitOf) => {
      let open = null;   // the strand still open at the chain's current end
      const finish = () => { if (open) { stats.strands++; shapeChecks(kindName, open.id, open.pts, open.rLimit); } open = null; };
      for (let c = 0; c < eds.length; c++) {
        const ed = eds[c];
        const pcs = getPieces(ed).map(p => orient(ed, p));
        if (!ed.fwd) pcs.reverse();
        const startSec = ed.fwd ? 0 : ed.secs.length - 1, endSec = ed.fwd ? ed.secs.length - 1 : 0;
        for (let i = 0; i < pcs.length; i++) {
          const pc = pcs[i];
          if (i === 0 && open && pc[0].sec === startSec) {
            const a = open.pts[open.pts.length - 1], b = pc[0];
            if (ed.linkIn === 'bend') { open.sh = joinEnds(open.pts, pc, open.sh, true); continue; }
            const d = Math.hypot(a.x - open.sh.x - b.x, a.z - open.sh.z - b.z);   // a's own position
            if (d <= V) { open.sh = joinEnds(open.pts, pc, open.sh); continue; }
            // reported at firstEnd: the same run whichever way the chain runs
            const useA = firstEnd(a, b), q = useA ? a : b;
            runs.push({ check: 'B4', lineId: kindName === 'midline' ? 'MID' : 'R' + q.side, e: q.e, s: q.s, x: useA ? a.x - open.sh.x : b.x, z: useA ? a.z - open.sh.z : b.z, val: d, len: 0,
                        e0: a.e, s0: a.s, e1: b.e, s1: b.s, kind: kindName, node: nodeOf(ed, 'start'), side: kindName === 'midline' ? undefined : q.side });
          }
          finish();
          open = { pts: [...pc], id: kindName === 'midline' ? 'MID' : 'R' + pc[0].side, rLimit: rLimitOf, sh: { x: 0, z: 0 } };
        }
        // a strand that does not reach this edge's chain-end section cannot continue into the next edge
        if (open && open.pts[open.pts.length - 1].sec !== endSec) finish();
        if (!pcs.length) finish();
      }
      finish();
    };
    // chain-right is the edge's L going forward, its R going backward
    joinStrands(ed => ed.fwd ? ed.rib.L : ed.rib.R, 'edge', () => R.InnerEdgeMinRM);
    joinStrands(ed => ed.fwd ? ed.rib.R : ed.rib.L, 'edge', () => R.InnerEdgeMinRM);
    joinStrands(ed => ed.mid, 'midline', ei => R.rMinFor(E[ei].klass));

    // ---- painted lines: link pieces across joints (join <= JoinM, else JUMP within MatchM, else END)
    // every piece, oriented, tagged with whether it touches the chain-start / chain-end section of its edge
    const P = [];
    eds.forEach((ed, c) => {
      for (const L of ed.lines) for (const pc of L.pieces) {
        const pts = orient(ed, pc.pts);
        const lid = L.plan ? L.plan.id : `T${L.run.col}u${L.run.u.toFixed(3)}`;
        for (const q of pts) q.lid = lid;
        const first = pts[0], last = pts[pts.length - 1];
        const onSec = (p, which) => {
          const atStart = ed.fwd ? (p.tag === 'A' && p.span === 1) : (p.tag === 'B' && p.span === ed.nSpan);
          const atEnd = ed.fwd ? (p.tag === 'B' && p.span === ed.nSpan) : (p.tag === 'A' && p.span === 1);
          return which === 'start' ? atStart : atEnd;
        };
        P.push({ c, ed, L, pts, col: L.run.col, dashed: L.run.dashed, id: lid,
                 startsAtJoint: c > 0 && ed.linkIn === 'mitre' && onSec(first, 'start'), endsAtJoint: c + 1 < eds.length && eds[c + 1].linkIn === 'mitre' && onSec(last, 'end'),
                 next: null, prev: null, joined: false });
      }
    });
    for (let c = 0; c + 1 < eds.length; c++) {
      const ends = P.filter(p => p.c === c && p.endsAtJoint), starts = P.filter(p => p.c === c + 1 && p.startsAtJoint);
      const pairs = [];
      for (const a of ends) for (const b of starts) {
        if (a.col !== b.col || a.dashed !== b.dashed) continue;
        const pa = a.pts[a.pts.length - 1], pb = b.pts[0], d = Math.hypot(pa.x - pb.x, pa.z - pb.z);
        if (d <= R.MatchM) pairs.push({ a, b, d });
      }
      pairs.sort((p, q) => p.d - q.d || pairOrder(p, q));
      for (const { a, b, d } of pairs) {
        if (a.next || b.prev) continue;
        a.next = b; b.prev = a;
        if (d <= V) b.joined = true;   // within V: one strand, so B1/B2 judge the node (joinEnds)
        else {
          const pb = b.pts[0], pa = a.pts.at(-1);
          // at firstEnd, named after that end's line: one run, whichever way the chain runs
          const useA = firstEnd(pa, pb), q = useA ? pa : pb;
          runs.push({ check: 'B4', lineId: useA ? a.id : b.id, e: q.e, s: q.s, x: q.x, z: q.z, val: d, len: 0, e0: pa.e, s0: pa.s, e1: pb.e, s1: pb.s, kind: 'paint', node: nodeOf(b.ed, 'start') });
        }
      }
    }
    // identity chains: follow next from every piece with no prev
    for (const head of P) {
      if (head.prev) continue;
      const seq = [];
      for (let p = head; p; p = p.next) seq.push(p);
      // B-family on joined runs of the sequence (a pair within V is one strand: joinEnds)
      let strand = null, sh = null;
      for (const p of seq) {
        if (strand && p.joined) sh = joinEnds(strand, p.pts, sh);
        else { if (strand) { stats.strands++; shapeChecks('paint', head.id, strand, 0); } strand = [...p.pts]; sh = { x: 0, z: 0 }; }
      }
      if (strand) { stats.strands++; shapeChecks('paint', head.id, strand, 0); }
      // C2 END at both ends of the identity chain; C1 across a joint
      for (const [p, which] of [[seq[0], 'start'], [seq[seq.length - 1], 'end']]) {
        const pt = which === 'start' ? p.pts[0] : p.pts[p.pts.length - 1];
        const ed = p.ed, e = ed.e;
        const sMin = ed.secs[0].s, sMax = ed.secs[ed.secs.length - 1].s;
        const atEdgeEnd = Math.abs(pt.s - sMin) <= R.FanMouthM ? 'a' : Math.abs(pt.s - sMax) <= R.FanMouthM ? 'b' : null;
        const node = atEdgeEnd === 'a' ? e.a : atEdgeEnd === 'b' ? e.b : -1;
        let legit = null;
        if (node >= 0 && T.patch[node] && !isBendFan(node)) legit = 'fan mouth';   // a bend fan's mouths are no legitimate end (plan A2)
        else if (node >= 0 && nodeEdges[node].length === 1) legit = 'dead end';
        else if (node >= 0 && (T.branchA[e.index] >= 0 && node === e.a || T.branchB[e.index] >= 0 && node === e.b)) legit = 'branch mouth';
        // a gore NOSE only (gate spec 4.3's closed list): a collapsed section - the branch wholly inside its host - within
        // GoreNoseM. A line that starts or stops along a branch's attach arc, where the branch is only clipped, ends
        // mid-road: the crop drew it there, and the markings layer (WP-17/18b) ends it at the nose
        else if (ed.secs.some(c => c.collapsed && Math.abs(c.s - pt.s) <= R.GoreNoseM)) legit = 'gore nose';
        const plan = p.L.plan;
        if (!legit && plan) {
          // the plan drops or adds the line here (its existence changes within FanMouthM); or at a joint the plan across
          // carries fewer lines of this colour and pattern (a lane drop: one of them must end), or none within a lane
          // (MatchM). A line that continues across the node was paired above (a JUMP) and never gets here.
          const s0 = Math.max(sMin, pt.s - R.FanMouthM), s1 = Math.min(sMax, pt.s + R.FanMouthM);
          if (planExists(plan, e, s0) !== planExists(plan, e, s1)) legit = 'plan lane drop';
          else if (node >= 0 && jointAt(e, node) >= 0) {
            const o = E[jointAt(e, node)], op = plans.get(o.profile.key).plan;
            const so = o.a === node ? 0 : o.length, sn = node === e.a ? 0 : e.length;
            const lat = sdRef(ed.ref, pt.x, pt.z, pt.s);
            // the other edge's lateral frame: flip when the two run opposite ways through the node
            const same = (e.b === node) === (o.a === node);
            const like = q => q.col === p.col && q.dashed === p.dashed;
            const nOther = op.filter(q => like(q) && planExists(q, o, so)).length, nThis = ed.plan.filter(q => like(q) && planExists(q, e, sn)).length;
            const has = op.some(q => like(q) && planExists(q, o, so) && Math.abs((same ? 1 : -1) * planOff(q, o, so) - lat) <= R.MatchM);
            if (nOther < nThis) legit = 'plan lane drop at a node';
            else if (!has) legit = 'plan line ends at a node';
          }
        }
        // a legitimate place is still a stray end when the line is off its plan there
        let err = 0;
        if (plan) err = sdRef(ed.ref, pt.x, pt.z, pt.s) - (planOff(plan, e, pt.s) + p.L.q);
        const inArc = !legit && ed.secs.some(c => c.clipped && Math.abs(c.s - pt.s) <= R.GoreNoseM);
        if (!legit || Math.abs(err) > R.StrayM)
          runs.push({ check: 'C2', lineId: p.id, e: e.index, s: pt.s, x: pt.x, z: pt.z, val: Math.max(1, Math.abs(err) / R.StrayM), len: 0, e0: e.index, s0: pt.s, e1: e.index, s1: pt.s,
                      what: `${which} ${legit ? `at a ${legit} but ${Math.abs(err).toFixed(2)} m off its plan` : node >= 0 && isBendFan(node) ? "at a bend fan's mouth (a 2-arm node drawn as a junction slab)"
                        : inArc ? 'mid-road, along a branch attach arc (clipped; no gore nose within GoreNoseM)' : 'mid-road'}`, span: pt.span });
      }
      // C3 DASH along the identity chain
      if (head.dashed) {
        dashChecks(head.id, seq, head.L.run);
      }
    }
    if (chainNo % 2000 === 0) log(`gate: ${chainNo}/${chains.length} chains, ${runs.length} runs`);
  }

  // ---- key, class, cause, score for every run
  const secNear = (e, s) => {
    const secs = e.secs; if (!secs) return [];
    return secs.filter(c => Math.abs(c.s - s) <= 1.0);
  };
  for (const r of runs) {
    const e = E[r.e];
    if (r.kind === 'edge' && r.side) r.lineId = 'R' + r.side;
    r.way = e.wayId; r.name = e.name; r.cls = e.klass; r.profile = e.profile.key;
    r.key = `${e.wayId}:${lineKey(e, r.s)}:${r.check}:${r.lineId}`;
    const limit = r.check === 'A4' ? R.LineWidthTol : r.check === 'A5' || r.check === 'A5b' ? R.StrayM : r.check === 'B3' ? r.rLimit
      : r.check === 'C1' ? R.GapM : r.check === 'C2' ? 1 : r.check === 'C3' ? R.DashTol : r.check === 'D1' ? R.CrossM : V;
    r.limit = limit;
    const ratioOf = v => r.check === 'B3' ? limit / Math.max(1e-6, v) : r.check === 'C2' ? v : Math.abs(v) / limit;
    r.ratio = ratioOf(r.val);
    // the ratchet keys: (way, round(s on way / KeyStepM), check, line) for EVERY bucket the run's bad samples touch,
    // each with its own worst ratio and bad length; a point run (B4, C2, C3) has its one (length 0), a gap (C1)
    // every bucket it spans (each with the gap's length)
    // (a ribbon edge's line is the side of the edge each bucket lies on: a chain runs some of its edges backwards, so
    // one strand is RR on one edge and RL on the next, and a key labelled by the run's worst sample moved between
    // RL and RR - vanishing from the ratchet - whenever that worst moved to an edge the chain runs the other way)
    r.kk = []; r.kq = []; r.kl = [];
    // (and a painted line's bucket is named after the line its own samples lie on - the plan line of their edge: a
    // strand crosses joints into other profiles, and named after its identity chain's first piece it took whichever
    // line the chain happened to start from, so the two gates, which orient chains differently, named one run twice)
    // (a B3 bucket against its own samples' limit)
    const addKey = (bid, v, l, side, name, lim) => { r.kk.push(`${Math.floor(bid / BUCKETS)}:${bid % BUCKETS}:${r.check}:${r.kind === 'edge' && side ? 'R' + side : name ?? r.lineId}`); r.kq.push(r.check === 'B3' && lim !== undefined ? lim / Math.max(1e-6, v) : ratioOf(v)); r.kl.push(l); };
    if (r.bk) r.bk.forEach((bid, i) => addKey(bid, r.bv[i], r.bl[i], r.bs[i], r.bn ? r.bn[i] : undefined, r.br ? r.br[i] : undefined));
    else if (r.check === 'C1') for (let b = bucketOf(e, Math.min(r.s0, r.s1)); b <= bucketOf(e, Math.max(r.s0, r.s1)); b++) addKey(b, r.val, r.len);
    else addKey(bucketOf(e, r.s), r.val, 0);
    delete r.bk; delete r.bv; delete r.bl; delete r.bs; delete r.bn; delete r.br; delete r.lastArc;
    r.tile = `${Math.floor(r.x / 256)},${Math.floor(r.z / 256)}`;
    if ((r.check === 'A5' || r.check === 'A5b') && r.len < minRunLen[r.check]) r.drop = true;
    // cause hint
    const near = r.check === 'C1' ? (e.secs || []).filter(c => c.s >= Math.min(r.s0, r.s1) - 1 && c.s <= Math.max(r.s0, r.s1) + 1) : secNear(e, r.s);
    const causes = [];
    const sp = r.span && e.secs && e.secs[r.span] && e.secs[r.span - 1] ? [e.secs[r.span - 1], e.secs[r.span]] : null;
    if (near.some(c => c.taper) || (sp && Math.abs(sp[0].hw - sp[1].hw) > 0.005)) causes.push('TAPER');
    if (r.tag === 'D' || r.tag === 'I' && sp && Math.abs(sp[0].hw - sp[1].hw) > 0.005) causes.push('DIAGONAL');
    if (near.some(c => c.kind === 'vertex')) causes.push('VERTEX');
    if (near.some(c => c.kind === 'mitre') || r.node !== undefined) causes.push('MITRE');
    if (near.some(c => c.sqL || c.sqR)) causes.push('SQUEEZE');
    if (near.some(c => c.clipped || c.collapsed)) causes.push('CLIP');
    if (near.some(c => c.structEnd)) causes.push('STRUCTURE-END');
    // at a bend fan's mouth: the slab's corner, a DATA fault (WP-11 fillets every 2-arm node)
    const atBend = e.secs && ((isBendFan(e.a) && r.s - e.secs[0].s <= R.GoreNoseM) || (isBendFan(e.b) && e.secs[e.secs.length - 1].s - r.s <= R.GoreNoseM));
    if (atBend) causes.push('BEND-FAN');
    r.cause = causes.join('+') || '-';
    r.data = atBend || dataKinkNear(e, r.s) ? 'DATA' : 'BUILDER';
    r.pinned = pinned.has(e.wayId);
    r.exposure = exposureAt(e.index, r.x, r.z);
    r.score = Math.min(r.ratio, R.RankRatioCap) * R.weightFor(e.klass) * r.exposure;
  }
  const kept = runs.filter(r => !r.drop);
  stats.bendFans = bendFans; stats.bendFans60 = bendFans60;
  return { runs: kept, stats, texQ, texCap, texNotes, texFails, chains: chains.length };
}
