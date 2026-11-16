// CENSUS (a): every place a two-way road opens into / closes from its own
// one-way carriageways - each SPLT record, every other two-way/one-way meeting
// of one name, and the short islands they bound - measured on the lanes the
// game draws (OSM line of the .bytes + the replicated line model offsets).
import fs from 'node:fs';
import { buildModel, outD, laneCentre, ext, offsetPoint, shiftAt, speedMph, floorOf, LANE } from './lm.mjs';
import { pointAt, tangentAt, toLL, OUT } from './load.mjs';
import { loadRaw, rawDirs, nearestVertex } from './osmraw.mjs';

const M = buildModel(); const E = M.E, map = M.map, NODES = map.nodes;
const raw = loadRaw();
const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
const DEG = 180 / Math.PI;
const nodeP = n => [NODES[n].x, NODES[n].z];
const mphR = e => Math.round(speedMph(e));
const rateFull = S => S < 45 ? S * S / 60 : S;      // MUTCD L/W: W S^2/60 below 45 mph, W S from 45
const CLASS = ['local', 'tertiary', 'secondary', 'primary', 'trunk', 'motorway'];

function twoWaySame(n, name, except = -1) {
  return map.nodeEdges[n].map(i => E[i]).filter(e => e.index !== except && e.a !== e.b && !e.oneway && !e.link && e.name === name);
}
function isSplitLike(n, name) {
  if (M.splitAt.has(n)) return true;
  const arms = map.nodeEdges[n].map(i => E[i]);
  return arms.some(e => !e.oneway && !e.link && e.name === name) && arms.some(e => e.oneway && !e.link && e.name === name);
}
// ChordInto (LineModel.cs:1286): the direction U runs INTO node, a chord from 10 m out
function chordInto(U, node) {
  const d = Math.min(10, U.length * 0.49); const p = pointAt(U, U.a === node ? d : U.length - d); const q = nodeP(node);
  const v = [q[0] - p[0], q[1] - p[1]], L = Math.hypot(v[0], v[1]);
  if (L > 1e-2) return [v[0] / L, v[1] / L];
  const t = U.a === node ? tangentAt(U, 0).map(x => -x) : tangentAt(U, U.length); return t;
}
// a carriageway from its node, away from it, through one-way continuations of its name
function walkC(C, node, maxLen) {
  const chain = []; let e = C, from = node, len = 0, far = -1, stop = 'len';
  for (let g = 0; g < 40; g++) {
    chain.push({ e, fwd: e.a === from, d0: len }); len += e.length; far = e.a === from ? e.b : e.a;
    if (isSplitLike(far, C.name)) { stop = 'split'; break; }
    if (len >= maxLen) { stop = 'len'; break; }
    const arrives = e.b === far; const cand = [];
    for (const i of map.nodeEdges[far]) { if (i === e.index) continue; const o = E[i]; if (o.name !== e.name || !o.oneway || o.link || o.a === o.b || (o.a === far) !== arrives) continue; cand.push(o); }
    if (cand.length !== 1) { stop = cand.length ? 'branch' : 'end'; break; }
    e = cand[0]; from = far;
  }
  return { chain, len, far, stop };
}
function chainAt(ch, d) {
  let k = 0; while (k < ch.chain.length - 1 && d > ch.chain[k].d0 + ch.chain[k].e.length) k++;
  const c = ch.chain[k]; const x = Math.max(0, Math.min(c.e.length, d - c.d0));
  return { e: c.e, s: c.fwd ? x : c.e.length - x, fwd: c.fwd };
}
// U behind the node: walk back along U through mitred partners / same-name two-way 2-arm continuations
function walkBack(U, node, maxLen) {
  const chain = []; let e = U, from = node, len = 0;
  for (let g = 0; g < 20; g++) {
    chain.push({ e, fwd: e.a === from, d0: len }); len += e.length; if (len >= maxLen) break;
    const far = e.a === from ? e.b : e.a; let p = M.partner(e, far);
    if (p < 0 || E[p].oneway) break; e = E[p]; from = far;
  }
  return { chain, len };
}
// the direction lanes' centre on two-way U for the side outLeftOfT1 (+1: left of travel into the node)
function uDirLane(U, s, node, outSign) {
  const [eM, eP] = ext(U, s); const plusIsLeftT1 = U.b === node;
  const nF = U.hasLset ? U.nF : 1, nB = U.hasLset ? U.nB : 1;
  const plusSide = (outSign > 0) === plusIsLeftT1;
  return plusSide ? eP - U.shl - nB * LANE / 2 : -(eM - U.shr - nF * LANE / 2);
}
function hermiteAxis(P1, t1, P2, t2) {
  const L = Math.hypot(P2[0] - P1[0], P2[1] - P1[1]); const ax = [], axS = [0];
  for (let i = 0; i <= 64; i++) {
    const t = i / 64, tt = t * t, t3 = tt * t, h1 = 2 * t3 - 3 * tt + 1, h2 = t3 - 2 * tt + t, h3 = -2 * t3 + 3 * tt, h4 = t3 - tt;
    ax.push([h1 * P1[0] + h2 * L * t1[0] + h3 * P2[0] + h4 * L * t2[0], h1 * P1[1] + h2 * L * t1[1] + h3 * P2[1] + h4 * L * t2[1]]);
    if (i) axS.push(axS[i - 1] + Math.hypot(ax[i][0] - ax[i - 1][0], ax[i][1] - ax[i - 1][1]));
  }
  return { ax, axS, len: axS[64] };
}
function axisLocal(A, p) {
  let best = Infinity, bk = -1, bt = 0;
  for (let k = 1; k < A.ax.length; k++) {
    const a = A.ax[k - 1], ab = [A.ax[k][0] - a[0], A.ax[k][1] - a[1]], l2 = ab[0] * ab[0] + ab[1] * ab[1]; if (l2 < 1e-8) continue;
    let t = ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2; t = Math.max(0, Math.min(1, t));
    const d = (p[0] - a[0] - ab[0] * t) ** 2 + (p[1] - a[1] - ab[1] * t) ** 2; if (d < best) { best = d; bk = k; bt = t; }
  }
  const a = A.ax[bk - 1], ab = [A.ax[bk][0] - a[0], A.ax[bk][1] - a[1]], L = Math.hypot(ab[0], ab[1]);
  const tn = [ab[0] / L, ab[1] / L], left = [-tn[1], tn[0]];
  // past either end: the end piece's line carried on (u < 0 before the start, > len beyond the end)
  if ((bk === 1 && bt === 0) || (bk === A.ax.length - 1 && bt === 1)) { const o = bt === 0 ? a : A.ax[bk]; const v = [p[0] - o[0], p[1] - o[1]]; return { u: (bt === 0 ? 0 : A.len) + v[0] * tn[0] + v[1] * tn[1], lat: v[0] * left[0] + v[1] * left[1] }; }
  const at = [a[0] + ab[0] * bt, a[1] + ab[1] * bt];
  return { u: A.axS[bk - 1] + bt * (A.axS[bk] - A.axS[bk - 1]), lat: (p[0] - at[0]) * left[0] + (p[1] - at[1]) * left[1] };
}
// raw OSM deflection at the node between U (into it) and C (out of it)
function rawDeflection(U, C, node) {
  const wu = raw.get(U.wayId), wc = raw.get(C.wayId); if (!wu || !wc) return null;
  const P = nodeP(node);
  // the raw node: a node id both ways share, nearest the node; else the nearest vertex of C's way
  const setU = new Map(); wu.nodes.forEach((id, i) => setU.set(id, i));
  let best = null;
  wc.nodes.forEach((id, i) => { if (setU.has(id)) { const d = Math.hypot(wc.pts[i][0] - P[0], wc.pts[i][1] - P[1]); if (!best || d < best.d) best = { ic: i, iu: setU.get(id), d }; } });
  if (!best) { const [ic, dc] = nearestVertex(wc, P[0], P[1]); const [iu, du] = nearestVertex(wu, P[0], P[1]); best = { ic, iu, d: Math.max(dc, du), approx: true }; }
  if (best.d > 25) return null;
  // C leaves the node along its way toward the carriageway: the side its chain goes (compare with the .bytes direction)
  const cOut = outD(C, node);
  let dc = null; for (const dir of [1, -1]) { const r = rawDirs(wc, best.ic, dir); if (r && (!dc || dot(r.chord, cOut) > dot(dc.chord, cOut))) dc = r; }
  // U's direction INTO the node: from the far side of U's way
  const uIn = outD(U, node).map(v => -v);
  let du = null; for (const dir of [1, -1]) { const r = rawDirs(wu, best.iu, dir); if (r && (!du || dot(r.chord, uIn) < dot(du.chord, uIn))) du = r; }
  if (!dc || !du) return null;
  const into = du.first.map(v => -v), intoC = du.chord.map(v => -v);
  const ang = (a, b) => Math.acos(Math.max(-1, Math.min(1, dot(a, b)))) * DEG;
  return { first: ang(into, dc.first), chord15: ang(intoC, dc.chord), moved: best.d, approx: !!best.approx };
}

// ---------------------------------------------------------------- the sites
const sites = [];
for (const sp of M.splitAt.values()) sites.push({ type: 'SPLT', node: sp.node, U: E[sp.u], cs: [[E[sp.a], 'a', sp.offA], [E[sp.b], 'b', sp.offB]] });
for (let n = 0; n < NODES.length; n++) {
  if (M.splitAt.has(n)) continue;
  const arms = map.nodeEdges[n].map(i => E[i]).filter(e => e.a !== e.b);
  for (const U of arms.filter(e => !e.oneway && !e.link && e.name)) {
    const du = outD(U, n);
    const opp = arms.filter(e => e !== U && e.oneway && !e.link && e.name === U.name && dot(du, outD(e, n)) <= -0.5);
    if (!opp.length) continue;
    // one U per node: the two-way arm most opposite the carriageways
    const prev = sites.find(s => s.node === n && s.type === 'meet'); if (prev) continue;
    sites.push({ type: 'meet', node: n, U, cs: opp.map(C => [C, C.a === n ? 'leave' : 'arrive', C.a === n ? -1 : 1]) });
  }
}


// ---------------------------------------------------------------- measure
// THE LINE MEASURED: each carriageway's OUTER pavement edge (the side away
// from the other carriageway) - continuous with the undivided road's edge on
// that side at a flush split, so a lane-count change at the node (3 lanes into
// 2) is not read as a move - joined to the undivided road's edge by the mitre
// (the two offset lines cut where they cross). Laterals are OUTWARD (+ away
// from the median), along a REFERENCE for the road's own line:
//   island  both carriageways reach the same split node <= 150 m on: the
//           game's own island axis (LineModel.BuildRefugePair: a Hermite
//           curve between the nodes on the undivided roads' 10 m chords)
//   midline both carriageways run >= 60 m: a Hermite from the node (the
//           undivided road's chord) to the midpoint of the two carriageways'
//           OSM lines 100 m on (their mean direction there)
//   arc     otherwise: the undivided road's approach continued as a circle
//           (its curvature over its last 60 m)
function outerEdgeLat(e, s, outerPlus) { const [eM, eP] = ext(e, s); return outerPlus ? eP : -eM; }
function approachCurv(back) {
  const pts = [];
  for (const dd of [60, 30, 0]) {
    let k = 0; while (k < back.chain.length - 1 && dd > back.chain[k].d0 + back.chain[k].e.length) k++;
    const c = back.chain[k]; const xx = Math.min(c.e.length, Math.max(0, dd - c.d0)); pts.push(pointAt(c.e, c.fwd ? xx : c.e.length - xx));
  }
  const [a, b, c] = pts; const ab = [b[0] - a[0], b[1] - a[1]], bc = [c[0] - b[0], c[1] - b[1]];
  const cr = ab[0] * bc[1] - ab[1] * bc[0], la = Math.hypot(...ab), lb = Math.hypot(...bc), lc = Math.hypot(c[0] - a[0], c[1] - a[1]);
  if (la < 5 || lb < 5) return 0;
  return 2 * cr / (la * lb * lc);
}
const segX = (a, b, c, d) => {
  const r = [b[0] - a[0], b[1] - a[1]], s = [d[0] - c[0], d[1] - c[1]], den = r[0] * s[1] - r[1] * s[0];
  if (Math.abs(den) < 1e-12) return null;
  const t = ((c[0] - a[0]) * s[1] - (c[1] - a[1]) * s[0]) / den, w = ((c[0] - a[0]) * r[1] - (c[1] - a[1]) * r[0]) / den;
  return t >= 0 && t <= 1 && w >= 0 && w <= 1 ? [a[0] + r[0] * t, a[1] + r[1] * t] : null;
};
const rows = [];
for (const site of sites) {
  const { node, U } = site; const P1 = nodeP(node); const t1 = chordInto(U, node); const left1 = [-t1[1], t1[0]];
  const back = walkBack(U, node, 60);
  // the splits round: measured in the undivided road's STRAIGHT frame (PSX_SPLITCENSUS_ARC=1: the approach arc)
  const kap = process.env.PSX_SPLITCENSUS_ARC === '1' && back.len >= 55 ? approachCurv(back) : 0;
  const cw = site.cs.map(([C, which, offC]) => ({ C, which, outSign: Math.sign(offC) || (C.a === node ? -1 : 1), ch: walkC(C, node, 220) }));
  // ---- the reference
  let refKind = 'arc', A = null, axLen = 0, U2 = null, refEnd = 80;
  if (cw.length === 2 && cw.every(c => c.ch.stop === 'split' && c.ch.len <= 150) && cw[0].ch.far === cw[1].ch.far) {
    const far = cw[0].ch.far, last = cw[0].ch.chain[cw[0].ch.chain.length - 1].e;
    const arr = outD(last, far).map(v => -v);
    let bd = 2; for (const u2 of twoWaySame(far, U.name)) { const d = dot(arr, outD(u2, far)); if (d < bd) { bd = d; U2 = u2; } }
    if (U2 && dot(outD(U2, far), arr) > 0.5) {
      const P2 = nodeP(far), t2 = chordInto(U2, far).map(v => -v), L = Math.hypot(P2[0] - P1[0], P2[1] - P1[1]);
      const dir = [(P2[0] - P1[0]) / L, (P2[1] - P1[1]) / L];
      if (L >= 8 && dot(t1, dir) >= 0.5 && dot(t2, dir) >= 0.5) { A = hermiteAxis(P1, t1, P2, t2); axLen = A.len; refKind = 'island'; refEnd = axLen * 0.5; }
    }
  }
  // (a midline reference changes the bulge sizes - census C5; PSX_SPLITCENSUS_MIDLINE=1 restores it)
  if (!A && process.env.PSX_SPLITCENSUS_MIDLINE === '1' && cw.length === 2 && cw.every(c => c.ch.len >= 60)) {
    const D = Math.min(100, cw[0].ch.len, cw[1].ch.len);
    const pt = c => { const at = chainAt(c.ch, D); const t = tangentAt(at.e, at.s); return [pointAt(at.e, at.s), at.fwd ? t : t.map(v => -v)]; };
    const [pa, ta] = pt(cw[0]), [pb, tb] = pt(cw[1]);
    const Mp = [(pa[0] + pb[0]) / 2, (pa[1] + pb[1]) / 2]; let tM = [ta[0] + tb[0], ta[1] + tb[1]]; const lt = Math.hypot(...tM);
    if (lt > 0.5) { tM = [tM[0] / lt, tM[1] / lt]; const L = Math.hypot(Mp[0] - P1[0], Mp[1] - P1[1]); const dir = [(Mp[0] - P1[0]) / L, (Mp[1] - P1[1]) / L];
      if (L > 30 && dot(t1, dir) > 0.5 && dot(tM, dir) > 0.5) { A = hermiteAxis(P1, t1, Mp, tM); axLen = A.len; refKind = 'midline'; refEnd = Math.min(80, axLen); } }
  }
  const R = kap !== 0 ? 1 / kap : Infinity;
  function local(p) {
    if (A) { const q = axisLocal(A, p); return [q.u, q.lat]; }
    const v = [p[0] - P1[0], p[1] - P1[1]], al = dot(v, t1), la = dot(v, left1);
    if (!isFinite(R) || Math.abs(R) > 1e5) return [al, la];
    const c = [left1[0] * R, left1[1] * R]; const w = [v[0] - c[0], v[1] - c[1]]; const dist = Math.hypot(w[0], w[1]);
    const lat = Math.abs(R) - dist; const ang = Math.atan2(dot(w, t1), -dot(w, left1) * Math.sign(R));
    return [Math.abs(R) * ang, R > 0 ? lat : -lat];
  }
  for (const { C, which, outSign, ch } of cw) {
    // the carriageway's own heading 60 m on (or at its end) against the undivided road's: a TURNING roadway (a channelized junction approach) when over 35 deg
    const dT = Math.min(60, ch.len - 0.5); const atT = chainAt(ch, dT); const tT = tangentAt(atT.e, atT.s); const tAway = atT.fwd ? tT : tT.map(v => -v);
    const netTurn = Math.acos(Math.max(-1, Math.min(1, dot(tAway, t1)))) * DEG;
    const Wd = refKind === 'island' ? Math.min(ch.len, axLen) * 0.5 : Math.min(ch.len, refKind === 'midline' ? refEnd : 80);
    const Upts = [], Cpts = [];
    for (let d = -40; d <= Wd + 1e-6; d += 0.5) {
      let p, q;
      if (d < 0) {
        const dd = -d; let k = 0; while (k < back.chain.length - 1 && dd > back.chain[k].d0 + back.chain[k].e.length) k++;
        const c = back.chain[k]; if (dd > c.d0 + c.e.length + 1e-6) continue;
        const xx = dd - c.d0, s = c.fwd ? xx : c.e.length - xx;
        const near = c.fwd ? c.e.a : c.e.b, plusIsLeftT1 = c.e.b === near;
        p = offsetPoint(c.e, s, outerEdgeLat(c.e, s, (outSign > 0) === plusIsLeftT1)); q = pointAt(c.e, s);
      } else {
        const at = chainAt(ch, d);
        p = offsetPoint(at.e, at.s, outerEdgeLat(at.e, at.s, (outSign > 0) === at.fwd)); q = pointAt(at.e, at.s);
      }
      const lp = local(p);
      (d < 0 ? Upts : Cpts).push([lp[0], outSign * lp[1], outSign * local(q)[1], d]);
    }
    if (Upts.length < 4 || Cpts.length < 6) continue;
    Upts.sort((a, b) => a[3] - b[3]);
    let cut = null;
    for (let i = Upts.length - 2; i >= Math.max(0, Upts.length - 40) && !cut; i--)
      for (let j = 0; j < Math.min(Cpts.length - 1, 40) && !cut; j++) { const X = segX(Upts[i], Upts[i + 1], Cpts[j], Cpts[j + 1]); if (X) cut = { i, j, X }; }
    const line = cut ? [...Upts.slice(0, cut.i + 1), [cut.X[0], cut.X[1], NaN, 0], ...Cpts.slice(cut.j + 1)] : [...Upts, ...Cpts];
    const jump0 = cut ? 0 : Cpts[0][1] - Upts[Upts.length - 1][1];
    const gapAt = cut ? null : Upts[Upts.length - 1][0];
    const ys = [];
    { let k = 0; const u0 = Math.ceil(line[0][0] * 2) / 2, u1 = line[line.length - 1][0];
      for (let u = u0; u <= u1; u += 0.5) {
        while (k < line.length - 2 && line[k + 1][0] < u) k++;
        const a = line[k], b = line[k + 1]; const t = b[0] - a[0] > 1e-9 ? Math.max(0, Math.min(1, (u - a[0]) / (b[0] - a[0]))) : 0;
        ys.push([u, a[1] + (b[1] - a[1]) * t, gapAt !== null && a[0] <= gapAt + 1e-9 && b[0] > gapAt ? 1 : 0]);
      } }
    if (ys.length < 10) continue;
    let trend = 0;
    const near = (lo, hi) => { const v = ys.filter(r => r[0] >= lo && r[0] <= hi); return v.length ? v.reduce((a, r) => a + r[1], 0) / v.length : NaN; };
    let yApp = near(-25, -15); if (isNaN(yApp)) yApp = near(-10, -1); if (isNaN(yApp)) continue;
    // the approach's own trend (a reference a little off the road's line): fitted over u -30..-3 and taken out
    { const v = ys.filter(r => r[0] >= -30 && r[0] <= -3); if (v.length >= 10) { const n = v.length, mu = v.reduce((a, r) => a + r[0], 0) / n, my = v.reduce((a, r) => a + r[1], 0) / n;
      let sxy = 0, sxx = 0; for (const r of v) { sxy += (r[0] - mu) * (r[1] - my); sxx += (r[0] - mu) ** 2; } const bb = sxx > 0 ? sxy / sxx : 0;
      if (Math.abs(bb) < 0.05) { const aa = my - bb * mu; for (const r of ys) r[1] -= bb * r[0]; yApp = aa; trend = bb; } } }
    const win = Math.min(Wd, 80);
    let yMax = -Infinity, dMax = 0, yMin = Infinity, dMin = 0, slope = 0, dSlope = 0, iMax = 0, iMin = 0;
    ys.forEach(([d, y], i) => { if (d < 0 || d > win) return; if (y > yMax) { yMax = y; dMax = d; iMax = i; } if (y < yMin) { yMin = y; dMin = d; iMin = i; } });
    for (let i = 2; i < ys.length - 2; i++) {
      const d = ys[i][0]; if (d < -20 || d > win) continue;
      if (ys[i - 2][2] || ys[i - 1][2] || ys[i][2] || ys[i + 1][2]) continue;
      const sl = Math.abs(ys[i + 2][1] - ys[i - 2][1]) / (ys[i + 2][0] - ys[i - 2][0]); if (sl > slope) { slope = sl; dSlope = d; }
    }
    let move20 = 0; for (const [d, y] of ys) if (d >= 0 && d <= 20) move20 = Math.max(move20, Math.abs(y - yApp));
    const outMove = yMax - yApp, inMove = yApp - yMin, mainOut = outMove >= inMove;
    const Amain = Math.max(0, mainOut ? outMove : inMove), iA = mainOut ? iMax : iMin, dA = mainOut ? dMax : dMin;
    // the move's start: back from the extreme to where the edge was within 5 % of the move of the approach
    let dStart = Math.max(-20, ys[0][0]); for (let i = iA; i >= 0; i--) { if (ys[i][0] < -20) break; if (Math.abs(ys[i][1] - yApp) <= 0.05 * Amain) { dStart = ys[i][0]; break; } }
    const Lmove = Math.max(0.5, dA - dStart);
    const Tavg = Amain > 0.05 ? Lmove / Amain : Infinity, Tpeak = slope > 1e-4 ? 1 / slope : Infinity;
    const yEnd = ys[ys.length - 1][1];
    const bulge = Math.max(0, yMax - Math.max(yApp, refKind === 'island' ? yApp : yEnd));
    const S = Math.max(mphR(U), mphR(C)); const rate = rateFull(S);
    const st = M.splitTapers.find(r => r.node === node && r.c === C.index);
    const defl = rawDeflection(U, C, node);
    const dB = Math.acos(Math.max(-1, Math.min(1, dot(outD(U, node).map(v => -v), outD(C, node))))) * DEG;
    const [lat, lon] = toLL(P1[0], P1[1]);
    const listed = M.refugeEdges.has(C.index);
    const cand = M.refugeCands.find(c => [c.sp.node, c.s2.node].includes(node));
    const row = {
      type: site.type, node, x: +P1[0].toFixed(1), z: +P1[1].toFixed(1), lat: +lat.toFixed(6), lon: +lon.toFixed(6), name: U.name, cls: CLASS[U.cls], U: U.index, C: C.index, which,
      uLanes: U.lanes, uW: +U.W.toFixed(2), cLanes: C.lanes, cW: +C.W.toFixed(2), mph: S, posted: (U.speedKmh > 0 || C.speedKmh > 0) ? 1 : 0, ctl: NODES[node].ctl,
      junction: ['none', 'mitre2', 'mitre-through', 'fan', 'bendfan'][M.kindOf[node]], arms: map.nodeEdges[node].length,
      netTurn: +netTurn.toFixed(1), ref: refKind, paired: refKind === 'island' ? 1 : 0, far: ch.far, chainLen: +ch.len.toFixed(1), stop: ch.stop, island: refKind === 'island' ? +axLen.toFixed(1) : '', approachR: isFinite(R) && Math.abs(R) < 1e5 ? Math.round(R) : '',
      refuge: listed ? 'listed' : cand ? 'candidate-not-listed' : '',
      shift0: st ? +st.shift0.toFixed(2) : '', lenRule: st ? +st.lenRule.toFixed(1) : '', lenDrawn: st ? +st.lenDrawn.toFixed(1) : '', room: st ? +st.room.toFixed(1) : '',
      lmTaper: st && st.lenDrawn > 0 ? +(st.lenDrawn / Math.abs(st.shift0)).toFixed(1) : '',
      deflBytes: +dB.toFixed(1), deflRaw: defl ? +defl.first.toFixed(1) : '', deflRaw15: defl ? +defl.chord15.toFixed(1) : '',
      yApp: +yApp.toFixed(2), jump0: +jump0.toFixed(2), yMax: +yMax.toFixed(2), dMax, yMin: +yMin.toFixed(2), dMin, yEnd: +yEnd.toFixed(2),
      move: +Amain.toFixed(2), moveDir: mainOut ? 'out' : 'in', dStart, Lmove: +Lmove.toFixed(1), Tavg: +Tavg.toFixed(1), slopeMax: +slope.toFixed(3), slopeDeg: +(Math.atan(slope) * DEG).toFixed(1), dSlope, Tpeak: +Tpeak.toFixed(1),
      move20: +move20.toFixed(2), bulge: +bulge.toFixed(2), trend: +trend.toFixed(4), rateFull: +rate.toFixed(1), rateShift: +(rate / 2).toFixed(1),
    };
    if (listed) {
      row.note = 'refuge profile drawn (not replicated): design lateral slope <= 0.045-0.05 (taper >= 20:1)';
      for (const k of ['yMax', 'dMax', 'yMin', 'dMin', 'yEnd', 'move', 'moveDir', 'dStart', 'Lmove', 'Tavg', 'bulge', 'dSlope']) row[k] = '';
      row.slopeMax = 0.05; row.slopeDeg = 2.9; row.Tpeak = 20;
      row.failShift = rate / 2 > 20 ? 1 : 0; row.failFull = rate > 20 ? 1 : 0; row.peakFailShift = row.failShift; row.shortfallM = 0; row.ratio = +((rate / 2) / 20).toFixed(2);
    } else {
      row.note = '';
      row.failNear = move20 >= 0.3 && 20 / move20 < rate / 2 ? 1 : 0;
      row.failShift = Amain >= 0.3 && Tavg < rate / 2 ? 1 : 0;
      row.failFull = Amain >= 0.3 && Tavg < rate ? 1 : 0;
      row.peakFailShift = Amain >= 0.3 && Tpeak < rate / 2 ? 1 : 0;
      row.shortfallM = Amain >= 0.3 ? +Math.max(0, Amain * rate / 2 - Lmove).toFixed(1) : 0;
      row.ratio = Amain >= 0.3 && isFinite(Tavg) ? +((rate / 2) / Tavg).toFixed(2) : 0;
    }
    rows.push(row);
  }
}
fs.writeFileSync(OUT + '/census_a_rows.json', JSON.stringify(rows));
const cols = Object.keys(rows[0]);
const esc = v => typeof v === 'string' && /[",]/.test(v) ? '"' + v.replace(/"/g, '""') + '"' : v;
fs.writeFileSync(OUT + '/census_a_all.csv', cols.join(',') + '\n' + rows.map(r => cols.map(c => esc(r[c])).join(',')).join('\n') + '\n');
console.log('rows', rows.length, 'sites', sites.length, 'SPLT', sites.filter(s => s.type === 'SPLT').length, 'meet', sites.filter(s => s.type === 'meet').length,
  'refs', JSON.stringify(rows.reduce((a, r) => (a[r.ref] = (a[r.ref] || 0) + 1, a), {})));
