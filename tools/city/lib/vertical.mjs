// vertical.mjs - the VERTICAL section of metrics.mjs (plan B2, 2026-10-02):
// an offline emulation of CityElevation.Solve's height passes and the PROFILE
// numbers on it - trench decisions, double separations, mixed decisions,
// carriageway pairs at crossings, the AASHTO vertical-curve census - so every
// export can be judged without Unity. Ported from the vertical diagnosis's
// scratch scripts (emulate.mjs, profile_audit.mjs, hump_causes.mjs,
// xing_truth.mjs); Editor/CityAudit.Profile.cs measures the same things on the
// real solve, and the two are compared in every package that moves heights.
//
// EMULATED: step 1 (terrain profile on the roads' 60 m grid, Smooth 2.5,
// ClampGrade, BlendEndsToNodes), SinkTrenches (the B2 group rule, or the rule
// before it with { rule: 'old' }), RaiseAllCrossings/RaiseHump (ApproachDrop),
// PruneMutualCrossings, HoldBridges, ReconcileNodes, the relax clamp,
// SnapNodesToEnds, RaiseConesFromNodes. NOT emulated: PairedRoadBaseY (the
// midline of divided roads), water spans and culvert holds, ramp seats,
// twin-deck holds, VerticalCurves. So the counts here are the emulation's;
// the audit's are the game's (both are reported, neither replaces the other).

const STEP = 10;
const APPROACH = 0.045, CLEAR = 5.0, DECK = 0.55;
export const SEPARATION_MAX = CLEAR + DECK + 1.5;
export const PAIR_DY_MAX = 0.5;
const TRENCH_END_M = 20, GROUP_REACH = 60, RAMP_REACH = 60, RAMP_COS = 0.9, RAMP_GRADE = 0.08, WATER_PAD = 10;
const PAIR_REACH = 45;

// ---------------------------------------------------------------- geometry
function segIndex(arr, s) {
  let lo = 0, hi = arr.length - 2;
  if (hi < 0) return 0;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (arr[mid] <= s) lo = mid; else hi = mid - 1; }
  return lo;
}
export function pointAt(e, s) {
  s = Math.min(e.length, Math.max(0, s));
  const lo = segIndex(e.s, s), seg = e.s[lo + 1] - e.s[lo], t = seg > 1e-6 ? (s - e.s[lo]) / seg : 0;
  const a = e.pts[lo], b = e.pts[lo + 1];
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
}
export function tangentAt(e, s) {
  s = Math.min(e.length, Math.max(0, s));
  const lo = segIndex(e.s, s), a = e.pts[lo], b = e.pts[lo + 1], dx = b[0] - a[0], dz = b[1] - a[1], m = Math.hypot(dx, dz);
  return m > 1e-5 ? [dx / m, dz / m] : [0, 1];
}
export function projectOn(e, p) {
  let best = Infinity, arc = 0;
  for (let i = 0; i + 1 < e.pts.length; i++) {
    const a = e.pts[i], dx = e.pts[i + 1][0] - a[0], dz = e.pts[i + 1][1] - a[1], L2 = dx * dx + dz * dz;
    const t = L2 > 1e-8 ? Math.min(1, Math.max(0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L2)) : 0;
    const qx = a[0] + dx * t, qz = a[1] + dz * t, dd = (p[0] - qx) ** 2 + (p[1] - qz) ** 2;
    if (dd < best) { best = dd; arc = e.s[i] + Math.sqrt(L2) * t; }
  }
  return arc;
}
export function yAt(e, s) {
  s = Math.min(e.length, Math.max(0, s));
  const S = e.stS, Y = e.stY;
  if (S.length < 2) return Y[0];
  const lo = segIndex(S, s), seg = S[lo + 1] - S[lo], t = seg > 1e-6 ? (s - S[lo]) / seg : 0;
  return Y[lo] + (Y[lo + 1] - Y[lo]) * t;
}

// ------------------------------------------------- the solver's own numbers
const maxGrade = e => e.rank >= 5 && !e.link ? 0.04 : e.link ? 0.08 : e.rank === 4 ? 0.05 : e.rank === 0 ? 0.08 : 0.065;
const judgedKmh = e => e.link ? (e.rank >= 5 ? 90 : e.rank >= 3 ? 80 : 70)
  : e.rank >= 5 ? 150 : e.rank === 4 ? 130 : e.rank === 3 ? 110 : e.rank === 2 ? 100 : e.rank === 1 ? 90 : 70;
const crestR = (e, route) => { const v = Math.max(judgedKmh(e), route ? 140 : 0) / 3.6; return Math.max(1.5 * v * v / 9.81, 10 * v / 1.0); };
function approachDrop(d, r) {
  if (d <= 0) return 0;
  const d1 = APPROACH * r;
  return d <= d1 ? d * d / (2 * r) : APPROACH * d - 0.5 * APPROACH * d1;
}
const tierOfRank = r => r >= 3 ? 1 : r >= 1 ? 2 : 3;

// ------------------------------------------------- AASHTO (Green Book 2018)
const K_MPH = [20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70];
const K_SAG_HEAD = [17, 26, 37, 49, 64, 79, 96, 115, 136, 157, 181];
const K_CREST = [7, 12, 19, 29, 44, 61, 84, 114, 151, 193, 247];
const kAt = (tab, mph) => {
  if (mph <= K_MPH[0]) return tab[0];
  for (let i = 1; i < K_MPH.length; i++) if (mph <= K_MPH[i]) return tab[i - 1] + (tab[i] - tab[i - 1]) * (mph - K_MPH[i - 1]) / (K_MPH[i] - K_MPH[i - 1]);
  return tab[tab.length - 1];
};
const kSagComfort = mph => mph * mph / 46.5;
const needL = (K, A, mph) => Math.max(K * Math.abs(A) * 100, 3 * mph) * 0.3048;
/// A loop ramp (CityAudit.IsLoopRamp): a link turning 120 degrees or more at a
/// mean radius under 120 m.
export function isLoopRamp(e) {
  if (!e.link || e.pts.length < 3) return false;
  let turn = 0;
  for (let i = 1; i + 1 < e.pts.length; i++) {
    const ax = e.pts[i][0] - e.pts[i - 1][0], az = e.pts[i][1] - e.pts[i - 1][1], bx = e.pts[i + 1][0] - e.pts[i][0], bz = e.pts[i + 1][1] - e.pts[i][1];
    if (ax * ax + az * az < 1e-6 || bx * bx + bz * bz < 1e-6) continue;
    turn += Math.abs(Math.atan2(ax * bz - az * bx, ax * bx + az * bz)) * 180 / Math.PI;
  }
  return turn >= 120 && e.length / (turn * Math.PI / 180) < 120;
}
/// The owner's vertical design speeds (owner_decisions): motorway 65, trunk
/// and primary 50, secondary and tertiary 40, local 30, ramps 40, loops 30.
export const verticalMph = e => e.link ? (isLoopRamp(e) ? 30 : 40) : e.rank >= 5 ? 65 : e.rank >= 3 ? 50 : e.rank >= 1 ? 40 : 30;
const classMaxGrade = e => e.link ? 0.08 : e.rank >= 5 ? 0.05 : e.rank >= 3 ? 0.07 : e.rank >= 1 ? 0.09 : 0.12;
const classKey = e => ['local', 'tertiary', 'secondary', 'primary', 'trunk', 'motorway'][e.rank] + (e.link ? '_link' : '');

// ------------------------------------------------------------- emulation
function smooth(y, sigma) {
  const n = y.length; if (n < 4) return;
  const r = Math.ceil(sigma * 2.5), o = new Float64Array(n);
  for (let i = 0; i < n; i++) { let s = 0, w = 0; for (let k = -r; k <= r; k++) { const j = Math.min(n - 1, Math.max(0, i + k)); const q = Math.exp(-k * k / (2 * sigma * sigma)); s += y[j] * q; w += q; } o[i] = s / w; }
  for (let i = 0; i < n; i++) y[i] = o[i];
}
function clampGrade(e, g) {
  const S = e.stS, Y = e.stY;
  for (let i = 1; i < Y.length; i++) { const ds = S[i] - S[i - 1]; Y[i] = Math.min(Y[i - 1] + g * ds, Math.max(Y[i - 1] - g * ds, Y[i])); }
  for (let i = Y.length - 2; i >= 0; i--) { const ds = S[i + 1] - S[i]; Y[i] = Math.min(Y[i + 1] + g * ds, Math.max(Y[i + 1] - g * ds, Y[i])); }
}
function blendEnds(nodeY, e) {
  const S = e.stS, Y = e.stY, n = Y.length, L = Math.min(e.length * 0.5, 90);
  if (L < 1) {
    const ya = nodeY[e.a], yb = nodeY[e.b];
    for (let i = 0; i < n; i++) Y[i] = ya + (yb - ya) * (e.length > 0 ? S[i] / e.length : 0);
    return;
  }
  const dA = nodeY[e.a] - Y[0], dB = nodeY[e.b] - Y[n - 1];
  for (let i = 0; i < n; i++) {
    const fa = S[i], fb = e.length - S[i];
    if (fa < L) Y[i] += dA * (1 - fa / L);
    if (fb < L) Y[i] += dB * (1 - fb / L);
  }
}
const roadKey = e => e.name ? e.name : '#' + e.index;

/// The trench decisions (CityElevation.SinkTrenches) on the emulated
/// profiles: 'b2' the plan B2 group rule, 'old' the rule before it. Returns
/// { trenched, why, pending, pinned } and lowers the profiles.
function sinkTrenches(city, nodeY, rule) {
  const { edges, nodes, nodeEdges, crossings } = city;
  const NC = crossings.length;
  const trenched = new Uint8Array(NC), why = new Array(NC).fill(null), oldWhy = new Array(NC).fill(null);
  const pinned = new Float64Array(nodes.length).fill(NaN);
  const wet = new Uint8Array(edges.length), spansOf = new Array(edges.length).fill(null);
  for (const w of city.wspans) { wet[w.edge] = 1; (spansOf[w.edge] ??= []).push([w.s0, w.s1]); }
  const pending = [];
  const sU = new Float64Array(NC), sO = new Float64Array(NC);
  const dig = (ci, over, s, under) => {
    const half = under.width * 0.5 + 7;
    for (let i = 0; i < over.stS.length; i++) if (Math.abs(over.stS[i] - s) <= half) over.stElev[i] = 1;
    trenched[ci] = 1;
  };
  const cand = [];
  for (let ci = 0; ci < NC; ci++) {
    const c = crossings[ci], over = edges[c.over], under = edges[c.under];
    if (!(under.rank >= 5 && !under.link)) continue;
    sU[ci] = projectOn(under, [c.x, c.z]); sO[ci] = projectOn(over, [c.x, c.z]);
    if (!c.forced) { why[ci] = oldWhy[ci] = 'not forced (layers guessed)'; continue; }
    if (under.tunnel) { why[ci] = oldWhy[ci] = 'under road a tunnel'; continue; }
    if (over.rank >= 5 || over.link) { why[ci] = oldWhy[ci] = 'over road a freeway or ramp'; continue; }
    if (wet[c.under]) oldWhy[ci] = 'a water span somewhere on the under edge (whole edge)';
    else if (sU[ci] < TRENCH_END_M || under.length - sU[ci] < TRENCH_END_M) oldWhy[ci] = `within ${TRENCH_END_M} m of the under edge's end`;
    cand.push(ci);
  }
  const spanIn = (ei, lo, hi) => { for (const [a, b] of spansOf[ei] || []) if (b + WATER_PAD >= lo && a - WATER_PAD <= hi) return `the water span on e${ei} at s ${a.toFixed(0)}-${b.toFixed(0)}`; return null; };
  const waterBeyond = (from, node, left, hops) => {
    if (left <= 0 || hops > 8) return null;
    for (const oi of nodeEdges[node]) {
      if (oi === from) continue;
      const o = edges[oi];
      if (o.rank < 5 || o.link || o.tunnel || o.a === o.b) continue;
      const fromA = o.a === node;
      const hit = fromA ? spanIn(oi, 0, left) : spanIn(oi, o.length - left, o.length);
      if (hit) return hit;
      const h2 = waterBeyond(oi, fromA ? o.b : o.a, left - o.length, hops + 1);
      if (h2) return h2;
    }
    return null;
  };
  const cutReachesWater = (ei, sAt, depth, grade, through) => {
    const reach = Math.max(0, depth) / grade + WATER_PAD, e = edges[ei];
    const hit = spanIn(ei, sAt - reach, sAt + reach);
    if (hit || !through) return hit;
    return waterBeyond(ei, e.b, reach - (e.length - sAt), 0) ?? waterBeyond(ei, e.a, reach - sAt, 0);
  };
  const junctionAlong = (from, node, d, hops) => {
    if (d >= TRENCH_END_M || hops > 8) return d;
    const arms = nodeEdges[node];
    if (arms.length !== 2) return d;
    const oi = arms[0] === from ? arms[1] : arms[0], o = edges[oi];
    if (o.a === o.b) return d;
    return junctionAlong(oi, o.a === node ? o.b : o.a, d + o.length, hops + 1);
  };
  const junctionDistance = (ei, s) => { const e = edges[ei]; return Math.min(junctionAlong(ei, e.b, e.length - s, 0), junctionAlong(ei, e.a, s, 0)); };
  const groupsDug = [], units = [], pre = new Map(), wants = new Map();
  const stats = { candidates: cand.length, groups: 0, dugGroups: 0, dug: 0, humpWater: 0, humpJunction: 0, ramps: 0, nowDug: [], nowHumped: [] };

  if (rule === 'old') {
    for (const ci of cand) {
      if (oldWhy[ci]) { why[ci] = oldWhy[ci]; continue; }
      const c = crossings[ci], over = edges[c.over];
      pending.push([c.under, sU[ci], sU[ci], yAt(over, sO[ci]) - CLEAR - DECK, APPROACH, 1, -1]);
      dig(ci, over, sO[ci], edges[c.under]); stats.dug++;
    }
  } else {
    const parent = new Map(cand.map(ci => [ci, ci]));
    const find = x => { while (parent.get(x) !== x) { parent.set(x, parent.get(parent.get(x))); x = parent.get(x); } return x; };
    for (let p = 0; p < cand.length; p++) for (let q = p + 1; q < cand.length; q++) {
      const cp = crossings[cand[p]], cq = crossings[cand[q]];
      if ((cp.x - cq.x) ** 2 + (cp.z - cq.z) ** 2 > GROUP_REACH * GROUP_REACH) continue;
      if (roadKey(edges[cp.over]) !== roadKey(edges[cq.over]) || roadKey(edges[cp.under]) !== roadKey(edges[cq.under])) continue;
      parent.set(find(cand[p]), find(cand[q]));
    }
    const groups = new Map();
    for (const ci of cand) { const r = find(ci); if (!groups.has(r)) groups.set(r, []); groups.get(r).push(ci); }
    const sorted = [...groups.entries()].sort((a, b) => a[0] - b[0]);
    stats.groups = sorted.length;
    const dugGroups = [];
    for (const [, members] of sorted) {
      let hard = null, soft = 0, target = Infinity;
      for (const ci of members) {
        const c = crossings[ci], over = edges[c.over], under = edges[c.under];
        const t = yAt(over, sO[ci]) - CLEAR - DECK;
        target = Math.min(target, t);
        const w = cutReachesWater(c.under, sU[ci], yAt(under, sU[ci]) - t, APPROACH, true);
        if (w && !hard) hard = `x${ci}'s cut would reach ${w}`;
        if (junctionDistance(c.under, sU[ci]) < TRENCH_END_M) soft++;
      }
      if (hard || soft === members.length) {
        const w = hard ? `a water span within the cut's reach (${hard})` : `every crossing of the group within ${TRENCH_END_M} m of a junction`;
        if (hard) stats.humpWater++; else stats.humpJunction++;
        for (const ci of members) { why[ci] = (members.length > 1 ? 'group: ' : '') + w; if (!oldWhy[ci]) stats.nowHumped.push(ci); }
        continue;
      }
      stats.dugGroups++;
      const gid = groupsDug.length;
      groupsDug.push([...members]);
      const unitOf = new Map();
      dugGroups.push([members, target]);
      const span = new Map();
      for (const ci of members) {
        const c = crossings[ci];
        const v = span.get(c.under);
        span.set(c.under, v ? [Math.min(v[0], sU[ci]), Math.max(v[1], sU[ci])] : [sU[ci], sU[ci]]);
        if (!unitOf.has(c.under)) { unitOf.set(c.under, units.length); units.push([gid, []]); }
        units[unitOf.get(c.under)][1].push(ci);
        dig(ci, edges[c.over], sO[ci], edges[c.under]); stats.dug++;
        if (oldWhy[ci]) stats.nowDug.push(ci);
      }
      for (const ue of [...span.keys()]) {
        let [lo, hi] = span.get(ue);
        for (const ci of members) { const sp = projectOn(edges[ue], [crossings[ci].x, crossings[ci].z]); lo = Math.min(lo, sp); hi = Math.max(hi, sp); }
        span.set(ue, [lo, hi]);
      }
      for (const [ei, [lo, hi]] of span) pending.push([ei, lo, hi, target, APPROACH, 1, unitOf.get(ei)]);
    }
    for (let ri = 0; ri < NC; ri++) {
      const rc = crossings[ri], ramp = edges[rc.under];
      if (!rc.forced || !ramp.link || ramp.tunnel || trenched[ri]) continue;
      const rover = edges[rc.over];
      if (rover.rank >= 5 || rover.link) continue;
      const rkey = roadKey(rover), sR = projectOn(ramp, [rc.x, rc.z]), sRo = projectOn(rover, [rc.x, rc.z]);
      for (let gi = 0; gi < dugGroups.length; gi++) {
        const [members, target] = dugGroups[gi];
        let inside = false;
        for (const ci of members) {
          const mc = crossings[ci];
          if (roadKey(edges[mc.over]) !== rkey) break;
          if ((mc.x - rc.x) ** 2 + (mc.z - rc.z) ** 2 > RAMP_REACH * RAMP_REACH) continue;
          const tr = tangentAt(ramp, sR), tm = tangentAt(edges[mc.under], sU[ci]);
          if (Math.abs(tr[0] * tm[0] + tr[1] * tm[1]) < RAMP_COS) continue;
          inside = true; break;
        }
        if (!inside) continue;
        // the ramp's own street's height: it needs no deeper than that
        const rTarget = yAt(rover, sRo) - CLEAR - DECK;
        const w = cutReachesWater(rc.under, sR, yAt(ramp, sR) - rTarget, RAMP_GRADE, false);
        if (w) { why[ri] = 'ramp beside a dug mainline, but its cut would reach ' + w; break; }
        pending.push([rc.under, sR, sR, rTarget, RAMP_GRADE, 2, units.length]);
        units.push([gi, [ri]]);
        groupsDug[gi].push(ri);
        dig(ri, rover, sRo, ramp); stats.ramps++;
        break;
      }
    }
  }
  // sink, carried through the mainline's nodes (and a ramp's through nodes
  // where only ramps meet)
  const q = [...pending]; let guard = 0;
  while (q.length && guard++ < 200000) {
    const [ei, lo, hi, target, grade, carry, group] = q.shift(); const e = edges[ei]; let moved = false;
    for (let i = 0; i < e.stS.length; i++) {
      const want = target + Math.max(0, Math.max(lo - e.stS[i], e.stS[i] - hi)) * grade;
      if (group >= 0) {
        const key = ei * 1048576 + i, p0 = pre.has(key) ? pre.get(key) : e.stY[i];
        if (want < p0 - 0.01) { pre.set(key, p0); if (!wants.has(key)) wants.set(key, []); wants.get(key).push([group, want]); }
      }
      if (e.stY[i] > want + 0.01) { e.stY[i] = want; moved = true; }
    }
    if (!moved || !carry) continue;
    for (const [node, endS] of [[e.a, 0], [e.b, e.length]]) {
      const wantEnd = target + Math.max(0, Math.max(lo - endS, endS - hi)) * grade;
      const endY = e.a === node ? e.stY[0] : e.stY[e.stY.length - 1];
      if (endY > wantEnd + 0.01) continue;
      if (carry === 2 && nodeEdges[node].some(oi => !edges[oi].link || edges[oi].tunnel)) continue;
      if (!Number.isNaN(pinned[node]) && pinned[node] <= endY + 0.01) continue;
      pinned[node] = endY;
      for (const oi of nodeEdges[node]) {
        if (oi === ei) continue; const o = edges[oi];
        if (carry === 1 ? (o.rank < 5 || o.link || o.tunnel) : (!o.link || o.tunnel || o.a === o.b)) continue;
        const oAt = o.a === node ? 0 : o.length, g = carry === 1 ? APPROACH : RAMP_GRADE;
        if (rule === 'old') { if (wet[oi]) continue; }
        else if (spansOf[oi]) {
          const reach = Math.max(0, yAt(o, oAt) - endY) / g + WATER_PAD;
          if (spansOf[oi].some(([a, b]) => (oAt === 0 ? a : o.length - b) - WATER_PAD < reach)) continue;
        }
        q.push([oi, oAt, oAt, endY, g, carry, group]);
      }
    }
  }
  for (const e of edges) {
    const pa = !Number.isNaN(pinned[e.a]), pb = !Number.isNaN(pinned[e.b]);
    if (!pa && !pb) continue;
    if (pa) nodeY[e.a] = pinned[e.a]; if (pb) nodeY[e.b] = pinned[e.b];
    if (e.link) {
      for (const [node, fromA] of [[e.a, true], [e.b, false]]) {
        if (Number.isNaN(pinned[node])) continue;
        for (let i = 0; i < e.stS.length; i++) { const d = fromA ? e.stS[i] : e.length - e.stS[i]; const want = pinned[node] + d * 0.08; if (e.stY[i] > want) e.stY[i] = want; }
      }
    } else blendEnds(nodeY, e);
  }
  const post = new Map();
  for (const key of wants.keys()) post.set(key, edges[Math.floor(key / 1048576)].stY[key % 1048576]);
  return { trenched, why, oldWhy, pinned, stats, groupsDug, units, pre, wants, post, given: new Float64Array(units.length) };
}

/// The emulated solve. Mutates each edge's stS/stY/stElev/terrainY.
export function emulateSolve(city, roadAt, { rule = 'b2', routeEdges = new Set() } = {}) {
  const { edges, nodes, nodeEdges, crossings } = city;
  const nodeY = new Float64Array(nodes.length);
  for (let i = 0; i < nodes.length; i++) nodeY[i] = roadAt(nodes[i].x, nodes[i].z);
  const R = e => crestR(e, routeEdges.has(e.index));
  for (const e of edges) {
    const n = Math.max(2, Math.ceil(e.length / STEP) + 1);
    e.stS = new Float64Array(n); e.stY = new Float64Array(n); e.stElev = new Uint8Array(n);
    for (let i = 0; i < n; i++) { const at = i === n - 1 ? e.length : i * e.length / (n - 1); e.stS[i] = at; const p = pointAt(e, at); e.stY[i] = roadAt(p[0], p[1]); }
    smooth(e.stY, 2.5);
    clampGrade(e, maxGrade(e));
    blendEnds(nodeY, e);
    if (e.bridge) e.stElev.fill(1);
    e.terrainY = Float64Array.from(e.stY);
  }
  const tr = sinkTrenches(city, nodeY, rule);
  const on = new Uint8Array(crossings.length).fill(1);
  { const by = new Map();
    crossings.forEach((c, i) => { const k = Math.min(c.over, c.under) + ':' + Math.max(c.over, c.under); if (!by.has(k)) by.set(k, []); by.get(k).push(i); });
    for (const l of by.values()) for (let m = 0; m < l.length; m++) for (let n = m + 1; n < l.length; n++) {
      const cm = crossings[l[m]], cn = crossings[l[n]]; if (cm.over === cn.over || !on[l[m]] || !on[l[n]]) continue;
      if (Math.hypot(cm.x - cn.x, cm.z - cn.z) > 500) continue;
      const em = edges[cm.over], en = edges[cn.over];
      const mW = em.level !== en.level ? em.level > en.level : em.rank !== en.rank ? em.rank > en.rank : em.length >= en.length;
      on[mW ? l[n] : l[m]] = 0;
    } }
  const order = []; for (let i = 0; i < crossings.length; i++) if (on[i]) order.push(i);
  order.sort((p, q2) => edges[crossings[p].over].level - edges[crossings[q2].over].level);
  const tgt = new Float64Array(crossings.length).fill(NaN);
  const raiseHump = (e, sAt, targetY) => { const r = R(e); for (let i = 0; i < e.stS.length; i++) { const want = targetY - approachDrop(Math.abs(e.stS[i] - sAt), r); if (e.stY[i] < want) e.stY[i] = want; } };
  const raiseAll = fresh => {
    for (const ci of order) {
      const c = crossings[ci], over = edges[c.over], under = edges[c.under];
      const sO = projectOn(over, [c.x, c.z]);
      if (fresh || Number.isNaN(tgt[ci])) { const sU = projectOn(under, [c.x, c.z]); const t = yAt(under, sU) + CLEAR + DECK; tgt[ci] = fresh && !Number.isNaN(tgt[ci]) ? Math.max(tgt[ci], t) : t; }
      raiseHump(over, sO, tgt[ci]);
    }
  };
  const holdBridges = () => { for (const e of edges) { if (!e.bridge || e.stY.length < 3) continue; const y0 = e.stY[0], y1 = e.stY[e.stY.length - 1]; for (let i = 1; i < e.stY.length - 1; i++) { const h = y0 + (y1 - y0) * e.stS[i] / e.length; if (e.stY[i] < h) e.stY[i] = h; } } };
  const endY = (e, n) => e.a === n ? e.stY[0] : e.stY[e.stY.length - 1];
  const pinned = tr.pinned;
  const reconcile = () => {
    let moved = 0;
    for (let n = 0; n < nodes.length; n++) { let best = -Infinity; for (const ei of nodeEdges[n]) best = Math.max(best, endY(edges[ei], n)); if (best > -Infinity) { moved = Math.max(moved, Math.abs(best - nodeY[n])); nodeY[n] = best; } }
    for (const e of edges) {
      if (e.a === e.b) continue;
      if (!Number.isNaN(pinned[e.a]) || !Number.isNaN(pinned[e.b])) continue;
      if (e.length < 25) { const d = Math.abs(nodeY[e.a] - nodeY[e.b]); if (d > 0.01 && d < 2.5) { const m = Math.max(nodeY[e.a], nodeY[e.b]); nodeY[e.a] = m; nodeY[e.b] = m; moved = Math.max(moved, d); } }
      else if (e.length < 90) { const f = e.length * maxGrade(e) * 1.6, hi = Math.max(nodeY[e.a], nodeY[e.b]), lo = Math.min(nodeY[e.a], nodeY[e.b]); if (hi - lo > f) { const lift = hi - f - lo; if (nodeY[e.a] < nodeY[e.b]) nodeY[e.a] += lift; else nodeY[e.b] += lift; moved = Math.max(moved, lift); } }
    }
    for (const e of edges) blendEnds(nodeY, e);
    return moved;
  };
  const snap = () => { for (let n = 0; n < nodes.length; n++) { let best = -Infinity; for (const ei of nodeEdges[n]) best = Math.max(best, endY(edges[ei], n)); if (best > -Infinity) nodeY[n] = best; } };
  const raiseCone = (node, y) => {
    const cq = [[node, y, 0, y]]; let head = 0, any = false;
    while (head < cq.length && head < 4000) {
      const [n, apex, d0, ny] = cq[head++];
      if (nodeY[n] < ny) nodeY[n] = ny;
      for (const ei of nodeEdges[n]) {
        const e = edges[ei], fromA = e.a === n, cnt = e.stS.length, r = R(e); let moved = false;
        for (let k = 0; k < cnt; k++) { const i = fromA ? k : cnt - 1 - k; const dist = fromA ? e.stS[i] : e.length - e.stS[i]; const want = apex - approachDrop(d0 + dist, r); if (e.stY[i] < want - 0.02) { e.stY[i] = want; moved = true; } }
        if (moved) any = true; if (!moved) continue;
        const far = fromA ? e.b : e.a, farWant = apex - approachDrop(d0 + e.length, r);
        if (farWant > nodeY[far] + 0.02) cq.push([far, apex, d0 + e.length, farWant]);
      }
    }
    return any;
  };
  const cones = () => {
    let seeded = 0;
    for (let n = 0; n < nodes.length; n++) {
      const y = nodeY[n]; let seed = y > roadAt(nodes[n].x, nodes[n].z) + 0.5;
      if (!seed) for (const ei of nodeEdges[n]) if (endY(edges[ei], n) < y - 0.02) { seed = true; break; }
      if (seed && raiseCone(n, y)) seeded++;
    }
    return seeded;
  };
  for (let it = 0; it < 4; it++) { raiseAll(false); holdBridges(); const m = reconcile(); if (m < 0.05) break; }
  for (const e of edges) {
    const last = e.stY.length - 1, ends = Math.abs(e.stY[last] - e.stY[0]) / Math.max(e.length, 1), g = Math.max(maxGrade(e) * 1.6, ends * 1.05);
    for (let pass = 0; pass < 2; pass++) {
      for (let i = 1; i < last; i++) { const ds = e.stS[i] - e.stS[i - 1]; e.stY[i] = Math.min(e.stY[i - 1] + g * ds, Math.max(e.stY[i - 1] - g * ds, e.stY[i])); }
      for (let i = last - 1; i >= 1; i--) { const ds = e.stS[i + 1] - e.stS[i]; e.stY[i] = Math.min(e.stY[i + 1] + g * ds, Math.max(e.stY[i + 1] - g * ds, e.stY[i])); }
    }
  }
  holdBridges(); raiseAll(true); raiseAll(true); raiseAll(true);
  // RelaxTrenches: a dug group whose street still rose gives the excess back
  const relax = () => {
    if (rule === 'old' || !tr.units.length) return 0;
    const spare = tr.units.map(([, xs]) => {
      let E = Infinity;
      for (const ci of xs) { const c = crossings[ci], o = edges[c.over], u = edges[c.under]; E = Math.min(E, yAt(o, projectOn(o, [c.x, c.z])) - DECK - CLEAR - yAt(u, projectOn(u, [c.x, c.z]))); }
      return E;
    });
    const isRamp = u => edges[crossings[tr.units[u][1][0]].under].link;
    // totals given after this round (CityElevation.RelaxTrenches)
    const total = tr.units.map((_, u) => tr.given[u] + (Number.isFinite(spare[u]) ? Math.max(0, spare[u]) : 0));
    const cwMin = new Map();
    tr.units.forEach(([g], u) => { if (!isRamp(u)) cwMin.set(g, Math.min(cwMin.get(g) ?? Infinity, total[u])); });
    let any = false;
    tr.units.forEach(([g], u) => {
      let cap = cwMin.get(g); if (cap === undefined || cap === Infinity) cap = 0;
      const want = isRamp(u) ? Math.min(total[u], cap) : Math.min(total[u], cap + 0.4);
      if (want > tr.given[u] + 0.1) { tr.given[u] = want; any = true; }
    });
    if (!any) return 0;
    let raised = 0;
    for (const [key, list] of tr.wants) {
      const ei = Math.floor(key / 1048576), i = key % 1048576, e = edges[ei];
      let G = Infinity; for (const [u] of list) G = Math.min(G, tr.given[u]);
      const floor = Math.min(tr.pre.get(key), tr.post.get(key) + G);
      if (e.stY[i] < floor - 0.02) { e.stY[i] = floor; raised++; }
    }
    relaxed += raised;
    return raised;
  };
  let relaxed = 0;
  for (let k = 0; k < 12; k++) { raiseAll(true); snap(); const rm = relax(); if (cones() === 0 && rm === 0) break; }
  snap();
  for (const e of edges) if (e.a !== e.b) { e.stY[0] = Math.max(e.stY[0], nodeY[e.a]); e.stY[e.stY.length - 1] = Math.max(e.stY[e.stY.length - 1], nodeY[e.b]); }
  return { nodeY, on, ...tr, relaxed };
}

// ------------------------------------------------------------- the census
/// Chains through the nodes' through pairs (CityAudit.ThroughChains).
function throughChains(city) {
  const { edges, nodes, nodeEdges } = city;
  const partner = new Map();
  const dirAt = (e, n) => e.a === n ? tangentAt(e, 0) : tangentAt(e, e.length).map(v => -v);
  for (let n = 0; n < nodes.length; n++) {
    const arms = nodeEdges[n].map(i => edges[i]).filter(e => e.a !== e.b && e.length >= 0.5);
    const cand = [];
    for (let p = 0; p < arms.length; p++) for (let q = p + 1; q < arms.length; q++) {
      const dp = dirAt(arms[p], n), dq = dirAt(arms[q], n), dot = dp[0] * dq[0] + dp[1] * dq[1];
      if (!(arms.length === 2 ? dot < 0.3 : dot < -0.7)) continue;
      cand.push([dot - (arms[p].name && arms[p].name === arms[q].name ? 0.5 : 0) - (arms[p].rank === arms[q].rank ? 0.2 : 0), arms[p].index, arms[q].index]);
    }
    cand.sort((a, b) => a[0] - b[0]);
    const used = new Set();
    for (const [, P, Q] of cand) { if (used.has(P) || used.has(Q)) continue; used.add(P); used.add(Q); partner.set(P + ':' + n, Q); partner.set(Q + ':' + n, P); }
  }
  const seen = new Uint8Array(edges.length), chains = [];
  for (const e0 of edges) {
    if (seen[e0.index] || e0.a === e0.b) continue;
    let e = e0, node = e0.a, guard = 0;
    while (guard++ < 10000) { const p = partner.get(e.index + ':' + node); if (p === undefined || p === e0.index) break; const o = edges[p]; node = o.a === node ? o.b : o.a; e = o; if (e === e0) break; }
    const list = []; let fwdNode = node; guard = 0;
    while (e && !seen[e.index] && guard++ < 10000) {
      seen[e.index] = 1;
      const fromA = e.a === fwdNode; list.push([e, fromA]);
      const far = fromA ? e.b : e.a, p = partner.get(e.index + ':' + far);
      fwdNode = far; e = p === undefined || seen[p] ? null : edges[p];
    }
    chains.push(list);
  }
  return chains;
}

/// The PROFILE numbers on the emulated solve (CityAudit.Profile's
/// definitions). Returns { json, lines }.
export function profileNumbers(city, em) {
  const { edges, crossings, nodes } = city;
  const out = [], P = s => out.push(s);
  const J = {};
  // curves + grades
  const curves = [];
  const gradeOver = [0, 0, 0, 0];
  for (const ch of throughChains(city)) {
    const S = []; let base = 0;
    for (const [e, fwd] of ch) {
      const n = e.stS.length, endNode = fwd ? e.b : e.a, ctl = (nodes[endNode].ctl & 6) !== 0, mph = verticalMph(e);
      for (let k = 0; k < n; k++) {
        const i = fwd ? k : n - 1 - k, along = fwd ? e.stS[i] : e.length - e.stS[i], d = base + along;
        if (S.length && Math.abs(S[S.length - 1].d - d) < 0.05) continue;
        S.push({ d, y: e.stY[i], e, i, mph: ctl && e.length - along <= 30 ? 25 : mph });
      }
      base += e.length;
    }
    for (let k = 1; k < S.length; k++) { const h = S[k].d - S[k - 1].d; if (h < 2) continue; if (Math.abs(S[k].y - S[k - 1].y) / h > classMaxGrade(S[k].e) + 1e-4) gradeOver[tierOfRank(S[k].e.rank)]++; }
    let cur = null;
    const flush = () => { if (cur && Math.abs(cur.A) >= 0.005) curves.push(cur); cur = null; };
    for (let k = 1; k + 1 < S.length; k++) {
      const h1 = S[k].d - S[k - 1].d, h2 = S[k + 1].d - S[k].d;
      if (h1 < 0.5 || h2 < 0.5) continue;
      const dg = (S[k + 1].y - S[k].y) / h2 - (S[k].y - S[k - 1].y) / h1;
      if (Math.abs(dg) < 0.001) { flush(); continue; }
      const sag = dg > 0;
      if (!cur || cur.sag !== sag) { flush(); cur = { sag, A: 0, L: 0, maxDg: 0, mph: S[k].mph, tier: tierOfRank(S[k].e.rank), cls: classKey(S[k].e) }; }
      cur.A += dg; cur.L += (h1 + h2) / 2;
      if (Math.abs(dg) > Math.abs(cur.maxDg)) { cur.maxDg = dg; cur.mph = S[k].mph; cur.tier = tierOfRank(S[k].e.rank); cur.cls = classKey(S[k].e); }
    }
    flush();
  }
  const C = { sags: [0, 0, 0, 0], sagShortComfort: [0, 0, 0, 0], sagShortHeadlight: [0, 0, 0, 0], corners: [0, 0, 0, 0], crests: [0, 0, 0, 0], crestShort: [0, 0, 0, 0] };
  for (const c of curves) {
    const t = c.tier, A = Math.abs(c.A);
    if (c.sag) {
      C.sags[t]++;
      if (c.L < needL(kSagComfort(c.mph), A, c.mph) * 0.999) C.sagShortComfort[t]++;
      if (c.L < needL(kAt(K_SAG_HEAD, c.mph), A, c.mph) * 0.999) C.sagShortHeadlight[t]++;
      if (A >= 0.02 && c.L <= 10.5) C.corners[t]++;
    } else { C.crests[t]++; if (c.L < needL(kAt(K_CREST, c.mph), A, c.mph) * 0.999) C.crestShort[t]++; }
  }
  const T = a => ({ T1: a[1], T2: a[2], T3: a[3] });
  J.curves = { total: curves.length, sags: T(C.sags), sags_short_of_comfort: T(C.sagShortComfort), sags_short_of_headlight: T(C.sagShortHeadlight), sag_corners: T(C.corners), crests: T(C.crests), crests_short: T(C.crestShort) };
  J.grades_over_class_max = T(gradeOver);
  // crossings
  const NC = crossings.length, sO = new Float64Array(NC), sU = new Float64Array(NC), sep = new Float64Array(NC);
  for (let ci = 0; ci < NC; ci++) { const c = crossings[ci]; sO[ci] = projectOn(edges[c.over], [c.x, c.z]); sU[ci] = projectOn(edges[c.under], [c.x, c.z]); sep[ci] = yAt(edges[c.over], sO[ci]) - yAt(edges[c.under], sU[ci]); }
  const tierX = ci => Math.min(tierOfRank(edges[crossings[ci].over].rank), tierOfRank(edges[crossings[ci].under].rank));
  const tall = [0, 0, 0, 0], doubles = [0, 0, 0, 0], doubleList = [], tallList = [];
  for (let ci = 0; ci < NC; ci++) {
    if (!em.on[ci] || !(sep[ci] > SEPARATION_MAX)) continue;
    tall[tierX(ci)]++; tallList.push(ci);
    if (em.trenched[ci]) { doubles[tierX(ci)]++; doubleList.push(ci); }
  }
  // decisions: groups + ramps beside the cut
  const cand = [];
  for (let ci = 0; ci < NC; ci++) { if (!em.on[ci]) continue; const c = crossings[ci], O = edges[c.over], U = edges[c.under]; if (U.rank < 5 || U.link || U.tunnel || O.rank >= 5 || O.link) continue; cand.push(ci); }
  const parent = new Map(cand.map(ci => [ci, ci]));
  const find = x => { while (parent.get(x) !== x) { parent.set(x, parent.get(parent.get(x))); x = parent.get(x); } return x; };
  for (let a = 0; a < cand.length; a++) for (let b = a + 1; b < cand.length; b++) {
    const ca = crossings[cand[a]], cb = crossings[cand[b]];
    if (Math.hypot(ca.x - cb.x, ca.z - cb.z) > GROUP_REACH) continue;
    if (roadKey(edges[ca.over]) !== roadKey(edges[cb.over]) || roadKey(edges[ca.under]) !== roadKey(edges[cb.under])) continue;
    parent.set(find(cand[a]), find(cand[b]));
  }
  const groups = new Map(); for (const ci of cand) { const r = find(ci); if (!groups.has(r)) groups.set(r, []); groups.get(r).push(ci); }
  let mixedGroups = 0, mixedX = 0, rampsOut = 0;
  for (const members of groups.values()) {
    const dug = members.filter(ci => em.trenched[ci]).length;
    let ramps = 0;
    if (dug > 0) for (let ri = 0; ri < NC; ri++) {
      if (!em.on[ri]) continue; const rc = crossings[ri], RU = edges[rc.under];
      if (!RU.link || RU.tunnel || roadKey(edges[rc.over]) !== roadKey(edges[crossings[members[0]].over])) continue;
      for (const ci of members) {
        if (!em.trenched[ci] || em.trenched[ri]) continue; const mc = crossings[ci];
        if (Math.hypot(rc.x - mc.x, rc.z - mc.z) > GROUP_REACH) continue;
        const tr = tangentAt(RU, sU[ri]), tm = tangentAt(edges[mc.under], sU[ci]);
        if (Math.abs(tr[0] * tm[0] + tr[1] * tm[1]) < 0.9) continue;
        if (yAt(RU, sU[ri]) > yAt(edges[mc.under], sU[ci]) + 1) ramps++;
        break;
      }
    }
    if ((dug > 0 && dug < members.length) || ramps > 0) { mixedGroups++; mixedX += members.length + ramps; rampsOut += ramps; }
  }
  // pairs across the median
  const carriageway = e => e.oneway && !e.link && e.name && e.rank >= 2;
  const pairN = [0, 0, 0, 0], pairFail = [0, 0, 0, 0], pairRaw = [0, 0, 0, 0]; let pairWorst = 0;
  for (let i = 0; i < NC; i++) {
    if (!em.on[i]) continue; const ci = crossings[i], Ui = edges[ci.under]; if (!carriageway(Ui)) continue;
    for (let j = i + 1; j < NC; j++) {
      if (!em.on[j]) continue; const cj = crossings[j], Uj = edges[cj.under];
      if (cj.under === ci.under || !carriageway(Uj) || Uj.name !== Ui.name || roadKey(edges[cj.over]) !== roadKey(edges[ci.over])) continue;
      if (Math.hypot(ci.x - cj.x, ci.z - cj.z) > PAIR_REACH + 15) continue;
      const ti = tangentAt(Ui, sU[i]), tj = tangentAt(Uj, sU[j]);
      if (ti[0] * tj[0] + ti[1] * tj[1] > -0.85) continue;
      const t = Math.min(tierOfRank(Ui.rank), tierOfRank(Uj.rank));
      if (Math.abs(yAt(Ui, sU[i]) - yAt(Uj, sU[j])) > PAIR_DY_MAX) pairRaw[t]++;
      const fj = projectOn(Uj, [ci.x, ci.z]), fi = projectOn(Ui, [cj.x, cj.z]);
      const dy = Math.max(Math.abs(yAt(Ui, sU[i]) - yAt(Uj, fj)), Math.abs(yAt(Uj, sU[j]) - yAt(Ui, fi)));
      pairN[t]++;
      if (dy > PAIR_DY_MAX) { pairFail[t]++; if (t === 1) pairWorst = Math.max(pairWorst, dy); }
    }
  }
  // W 5th
  const w5 = [];
  for (let ci = 0; ci < NC; ci++) { const c = crossings[ci], O = edges[c.over], U = edges[c.under]; if (!(O.name || '').includes('West 5th') || U.name !== 'I-77' || U.link) continue; w5.push({ ci, over: O.index, under: U.index, way: U.wayId, yU: yAt(U, sU[ci]), sep: sep[ci], dug: !!em.trenched[ci] }); }
  const nb = w5.filter(r => r.way === 122082585), sb = w5.filter(r => r.way === 648861047);
  const w5dy = nb.length && sb.length ? Math.abs(Math.min(...nb.map(r => r.yU)) - Math.min(...sb.map(r => r.yU))) : NaN;
  const w5sep = nb.length ? Math.max(...nb.map(r => r.sep)) : NaN;
  J.crossings = { enforced: em.on.reduce((a, v) => a + v, 0), trenched: em.trenched.reduce((a, v) => a + v, 0), separations_over_max: T(tall), double_separations: T(doubles) };
  J.decisions = { candidates: em.stats.candidates, groups: em.stats.groups, dug_groups: em.stats.dugGroups, humped_for_water: em.stats.humpWater, humped_at_junction: em.stats.humpJunction, ramps_in_cut: em.stats.ramps,
                  mixed_groups: mixedGroups, mixed_crossings: mixedX, ramps_outside_cut: rampsOut, redecided_dug: em.stats.nowDug.length, redecided_humped: em.stats.nowHumped.length };
  J.pairs = { T1: pairN[1], T1_over: pairFail[1], T1_worst: Math.round(pairWorst * 100) / 100, T2: pairN[2], T2_over: pairFail[2], T1_points_over: pairRaw[1] };
  J.w5th = { carriageway_dy: Math.round(w5dy * 100) / 100, separation_over_northbound: Math.round(w5sep * 100) / 100, crossings: w5.map(r => ({ ci: r.ci, over: r.over, under: r.under, sep: Math.round(r.sep * 100) / 100, dug: r.dug })) };
  // why the rest are at grade
  const whyCount = {};
  for (let ci = 0; ci < NC; ci++) if (em.why[ci] && em.on[ci]) { const k = em.why[ci].replace(/\(x\d+.*$/, '(...)'); whyCount[k] = (whyCount[k] || 0) + 1; }
  J.at_grade_why = whyCount;
  P(`  trench decisions: ${J.crossings.trenched} crossings dug of ${J.crossings.enforced} enforced; ${em.stats.candidates} street-over-freeway candidates` +
    (em.stats.groups ? ` in ${em.stats.groups} groups (${em.stats.dugGroups} dug, ${em.stats.humpWater} at grade for water, ${em.stats.humpJunction} at a junction), ${em.stats.ramps} ramps in the cut; redecided ${em.stats.nowDug.length} dug / ${em.stats.nowHumped.length} at grade` : ''));
  P(`  at grade: ${Object.entries(whyCount).map(([k, v]) => `${k} ${v}`).join('; ')}`);
  P(`  DOUBLE separations (dug and still over ${SEPARATION_MAX.toFixed(2)} m): T1 ${doubles[1]}, T2 ${doubles[2]}; separations over ${SEPARATION_MAX.toFixed(2)} m: T1 ${tall[1]}, T2 ${tall[2]}, T3 ${tall[3]}`);
  P(`  MIXED decisions: ${mixedGroups} groups, ${mixedX} crossings (${rampsOut} ramps outside the cut)`);
  P(`  PAIRS across the median: T1 ${pairN[1]}, ${pairFail[1]} over ${PAIR_DY_MAX} m (worst ${pairWorst.toFixed(2)} m; crossing points compared: ${pairRaw[1]}); T2 ${pairN[2]}, ${pairFail[2]} over`);
  P(`  W 5th over I-77: carriageways ${w5dy.toFixed(2)} m apart, separation over the northbound ${w5sep.toFixed(2)} m (${w5.map(r => `x${r.ci} ${r.sep.toFixed(1)}${r.dug ? ' dug' : ''}`).join(', ')})`);
  P(`  curves (A >= 0.5%, AASHTO at the owner's design speeds): T1 sags ${C.sags[1]} (short of comfort ${C.sagShortComfort[1]}, corners ${C.corners[1]}), crests ${C.crests[1]} (short ${C.crestShort[1]}); T2 sags ${C.sags[2]} (short ${C.sagShortComfort[2]}); T3 sags ${C.sags[3]} (short ${C.sagShortComfort[3]}); grades over the class max T1 ${gradeOver[1]} / T2 ${gradeOver[2]} / T3 ${gradeOver[3]} stations`);
  return { json: J, lines: out, doubleList, tallList, sep, tierX, w5 };
}

/// 3DEP at the crossings (when the 1/3" cache is there): the real separation
/// (the over road's abutments against the under road at the crossing), so a
/// game separation over the maximum can be told from a real one.
export function realSeparations(city, sample3dep) {
  const { edges, crossings } = city;
  const out = new Float64Array(crossings.length).fill(NaN);
  // bare earth has no decks: an over road's samples on a bridge, or within
  // 18 m of anything it crosses, read the ground under it
  const overAt = new Map();
  crossings.forEach(c => { if (!overAt.has(c.over)) overAt.set(c.over, []); overAt.get(c.over).push([c.x, c.z]); });
  const masked = (e, p) => e.bridge || (overAt.get(e.index) || []).some(q => Math.hypot(q[0] - p[0], q[1] - p[1]) < 18);
  const med = (e, s) => { const p = pointAt(e, s), t = tangentAt(e, s); const v = [-3, -1.5, 0, 1.5, 3].map(o => sample3dep(p[0] - t[1] * o, p[1] + t[0] * o)).sort((a, b) => a - b); return v[2]; };
  for (let ci = 0; ci < crossings.length; ci++) {
    const c = crossings[ci], O = edges[c.over], U = edges[c.under];
    const sO = projectOn(O, [c.x, c.z]), sU = projectOn(U, [c.x, c.z]);
    const tO = tangentAt(O, sO), tU = tangentAt(U, sU), sin = Math.max(0.4, Math.abs(tO[0] * tU[1] - tO[1] * tU[0]));
    const a0 = (U.width / 2 + 4) / sin;
    const ab = [];
    for (const side of [-1, 1]) {
      // along the over road, on through its nodes (the best-aligned arm)
      let e = O, s = sO, fwd = side > 0, d = 0, hops = 0;
      while (d <= 120) {
        const next = fwd ? s + 5 : s - 5;
        if (next >= 0 && next <= e.length) { s = next; d += 5; }
        else {
          const node = fwd ? e.b : e.a;
          const dirIn = fwd ? tangentAt(e, e.length) : tangentAt(e, 0).map(v => -v);
          let best = null, bs = -Infinity;
          for (const oi of city.nodeEdges[node]) {
            if (oi === e.index) continue; const o = edges[oi]; if (o.a === o.b) continue;
            const dd = o.a === node ? tangentAt(o, 0) : tangentAt(o, o.length).map(v => -v);
            const dot = dd[0] * dirIn[0] + dd[1] * dirIn[1];
            if (dot < 0.6) continue;
            const sc = dot + (o.name && o.name === e.name ? 1 : 0) + (o.link === e.link ? 0.5 : 0);
            if (sc > bs) { bs = sc; best = o; }
          }
          if (!best || ++hops > 20) break;
          const over = fwd ? next - e.length : -next;
          e = best; fwd = best.a === node; s = fwd ? Math.min(best.length, over) : Math.max(0, best.length - over); d += 5;
        }
        if (d < a0) continue;
        if (masked(e, pointAt(e, s))) continue;
        ab.push(med(e, s)); break;
      }
    }
    if (!ab.length) continue;
    out[ci] = ab.reduce((a, b) => a + b, 0) / ab.length - med(U, sU);
  }
  return out;
}
