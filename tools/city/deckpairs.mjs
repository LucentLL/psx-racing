// deckpairs.mjs - THE TWIN-DECK CENSUS AND DECISION TABLE, offline (plan B1,
// 2026-10-02; promoted from the bridges diagnosis's census.mjs + outline.mjs
// + final.mjs). The owner's example: West 5th Street over I-77 is ONE bridge
// with a median, drawn as two decks with four parapets.
//
// Every parallel-deck pair in the shipped graph, and whether it is one
// structure. The same rule, step for step, as Scripts/City/DeckPairs.cs (the
// game's table, built at load into map.deckPairs; its CSV from the TWIN
// report is checked against this with --compare):
//
//   DECKS (critic C1): every span the solver puts on structure from the
//     facts - a bridge=yes edge end to end, an over-edge within
//     CityElevation.DeckReach of its crossing, a water span (section SPAN).
//     (The solver's last-resort margin decks need the solve: the TWIN report
//     in the Editor sees them.)
//   PAIRS: every 2 m of deck, the nearest deck on each side running within
//     15 degrees either way, its foot inside that edge's deck, the gap
//     between the two ribbons' facing edges (the line model's lmPlus /
//     lmMinus: profile + TAPR offset) under 20 m; summed per edge pair, kept
//     when one edge sees the other for >= 10 m. The gap is the median sample.
//   ONE STRUCTURE (owner_decisions.md "Twin freeway decks"):
//     gap <= 0.3 m          no: the squeeze (one rail) or the clip already joins them
//     another OSM layer     no
//     override (BRST)       FORCE / NEVER by way pair (tools/city/deckpairs_overrides.json; Q7)
//     a culvert's creek     no (owner Q8 / critic C2: a water span within 15 m
//                           of an OSM tunnel=culvert line is a pipe, not a
//                           bridge; B2 converts it; until then it stays as it is)
//     outlines (BRST)       the same man_made=bridge outline: yes up to 20 m;
//                           two different outlines: never
//     otherwise G           opposite carriageways of one road: 6.1 m for EVERY
//                           class (owner L3: no 9.1 m arterial band); a ramp beside its road (or
//                           two ramps): 3.05 m; any other pair 1.2 m
//
//   node tools/city/deckpairs.mjs                      the report (Resources' charlotte_city.bytes)
//   --data <dir>                                       measure an --out export instead
//   --csv <file>                                       every pair, one row each
//   --review <file>                                    the owner's Street View list (CSV)
//   --write-baseline                                   record tools/city/baseline/deckpairs_baseline.json
//   --check                                            compare with the baseline: exit 1 when the union
//                                                      set moved or any pair is unexplained
//   --compare <twin_pairs.csv>                         the game's table (TWIN report, DeckPairs.cs) against
//                                                      this one: exit 1 on any decision that differs
// A file without BRST (before B1) is measured with the outlines and culverts
// read from the cache (lib/bridges.mjs), the same rules the exporter applies.

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, classOf, tierOf, hashHex } from './lib/citydata.mjs';
import { loadOutlines, outlineIndex, structIdOf, loadCulverts, culvertSpans, outlineName } from './lib/bridges.mjs';
import { LAT0, LON0, M_LAT, M_LON, toLat, toLon } from './lib/canopygrid.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const ARGS = process.argv.slice(2);
const argVal = n => { const i = ARGS.indexOf(n); if (i < 0) return null; const v = ARGS[i + 1]; return v && !v.startsWith('--') ? v : null; };
const DATA = resolve(UNITY, argVal('--data') || 'Assets/PSXRacing/Resources');
const BASELINE = join(HERE, 'baseline', 'deckpairs_baseline.json');
const toX = lon => (lon - LON0) * M_LON, toZ = lat => (lat - LAT0) * M_LAT;
const ll = (x, z) => `${toLat(z).toFixed(5)},${toLon(x).toFixed(5)}`;

// ---- the rule's numbers (DeckPairs.cs carries the same) -------------------
export const STEP = 2, GAP_MAX = 20, OVERLAP_MIN = 10, COS_MIN = Math.cos(15 * Math.PI / 180), CELL = 32;
export const SQUEEZE_M = 0.3, G_OUTLINE = 20, G_DUAL_FWY = 6.1, G_DUAL_ART = 6.1 /* owner 2026-10-02 (L3): was 9.1 */, G_RAMP = 3.05, G_OTHER = 1.2;
const CORRIDOR_BLEND = 26;   // CityElevation.CorridorBlend

const buf = readFileSync(join(DATA, 'charlotte_city.bytes'));
const city = parseCity(buf);
const E = city.edges;
console.log(`DECK PAIRS - ${join(DATA, 'charlotte_city.bytes')} graph ${hashHex(city.graphHash)}, ${E.length} edges`);

// ---- the line model's extents (LineModel.Init: lmPlus left of a->b) ------
for (const e of E) {
  const pr = e.profile;
  const lanesHalf = (pr.width - pr.shl - pr.shr) * 0.5;
  const off = city.taprOff ? city.taprOff[e.index] : 0;
  e.plus = lanesHalf + pr.shl + off;
  e.minus = lanesHalf + pr.shr - off;
  e.off = off;
  e.cls = classOf(e);
  e.tier = tierOf(e.rank);
}
// LineModel.FixMirrored (WP-11b): a lane-count change whose narrow run carries
// its offset mirrored is re-offset to line up with the wide run's fixed edge,
// along its 2-arm continuation (14 such in WP-10's data, 9 moved) - the same
// steps, so the extents here are the game's
let mirroredFixed = 0;
{
  const tangentEnd = (e, node) => {   // OutD: out of the node along e
    if (e.a === node) { const [dx, dz] = [e.pts[1][0] - e.pts[0][0], e.pts[1][1] - e.pts[0][1]]; const m = Math.hypot(dx, dz) || 1; return [dx / m, dz / m]; }
    const n = e.pts.length; const [dx, dz] = [e.pts[n - 2][0] - e.pts[n - 1][0], e.pts[n - 2][1] - e.pts[n - 1][1]]; const m = Math.hypot(dx, dz) || 1; return [dx / m, dz / m];
  };
  for (const t of city.tapr || []) {
    if (t.edge < 0 || t.edge >= E.length || (t.flags & 16)) continue;   // a SHIFT record (L4) is no lane change
    const W = E[t.edge], n = t.end === 0 ? W.a : W.b, dW = tangentEnd(W, n);
    let N = null, bd = -0.85;
    for (const oi of city.nodeEdges[n]) {
      const o = E[oi];
      if (o === W || o.a === o.b) continue;
      const od = tangentEnd(o, n), d = dW[0] * od[0] + dW[1] * od[1];
      if (d < bd) { bd = d; N = o; }
    }
    if (!N) continue;
    const same = (N.b === n) === (W.a === n);
    const nPlus = same ? N.plus : N.minus, nMinus = same ? N.minus : N.plus;
    const dPlus = W.plus - nPlus, dMinus = W.minus - nMinus;
    const moving = t.side === 0 ? dPlus : dMinus, fixd = t.side === 0 ? dMinus : dPlus;
    if (!(Math.abs(moving) <= 0.05 && Math.abs(fixd - t.dw) <= 0.3)) continue;
    const deltaW = t.side === 0 ? -fixd : fixd;
    const run = [];
    let cur = N, at = n, sign = same ? 1 : -1, left = false;
    for (let guard = 0; guard < 64; guard++) {
      run.push([cur, sign]);
      const far = cur.a === at ? cur.b : cur.a;
      if (city.nodeEdges[far].length !== 2) break;
      const nxI = city.nodeEdges[far][0] === cur.index ? city.nodeEdges[far][1] : city.nodeEdges[far][0];
      const nx = E[nxI];
      if (nx === cur || nx.a === nx.b) break;
      const through = (nx.a === far) === (cur.b === far);
      if (nx.profile.key !== cur.profile.key || Math.abs(nx.off * (through ? 1 : -1) - cur.off) > 0.05) { left = true; break; }
      sign *= through ? 1 : -1;
      cur = nx; at = far;
    }
    if (left) continue;
    for (const [e, sg] of run) { const d = deltaW * sg; e.off += d; e.plus += d; e.minus -= d; }
    mirroredFixed++;
  }
}
for (const e of E) e.halfMax = Math.max(e.plus, e.minus);

// ---- BRST: structIds, culvert spans, overrides ----------------------------
let brst = city.brst, brstFrom = 'BRST section';
if (!brst) {
  brstFrom = 'computed from tools/city/cache (no BRST in this file)';
  const outl = loadOutlines(join(HERE, 'cache', 'bridges_mm.json'), toX, toZ);
  const cul = loadCulverts(join(HERE, 'cache', 'layers', 'culverts.json'), toX, toZ);
  const structId = new Uint32Array(E.length);
  if (outl) { const ix = outlineIndex(outl); for (const e of E) if (e.bridge) structId[e.index] = structIdOf(e.pts, ix).id; }
  const spans = city.wspans.map(w => ({ e: w.edge, s0: w.s0, s1: w.s1 }));
  const cs = culvertSpans(spans, i => E[i].pts, cul);
  const ov = existsSync(join(HERE, 'deckpairs_overrides.json')) ? JSON.parse(readFileSync(join(HERE, 'deckpairs_overrides.json'), 'utf8')).pairs || [] : [];
  brst = { structId, culvert: cs.map(c => ({ span: c.span, way: c.way, d: c.d })), overrides: ov.map(o => ({ wayA: o.wayA, wayB: o.wayB, force: o.decision === 'FORCE' })) };
}
const culvertSpan = new Map(brst.culvert.map(c => [c.span, c]));
const override = new Map();
for (const o of brst.overrides) { override.set(o.wayA + ':' + o.wayB, o.force); override.set(o.wayB + ':' + o.wayA, o.force); }
console.log(`  line model: ${mirroredFixed} mirrored TAPR offsets put right (LineModel.FixMirrored); outlines + culverts from ${brstFrom}: ${E.filter(e => brst.structId[e.index]).length} bridge edges in an outline, ${brst.culvert.length} water spans a culvert claims, ${brst.overrides.length} overrides`);

// ---- geometry --------------------------------------------------------------
function segAt(e, s) {   // CityMap.Edge.SegmentAt: the last i in 0..n-2 with s[i] <= s
  let lo = 0, hi = e.s.length - 2;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (e.s[mid] <= s) lo = mid; else hi = mid - 1; }
  return lo;
}
function pointAt(e, s) {
  s = Math.max(0, Math.min(e.length, s));
  const i = segAt(e, s), seg = e.s[i + 1] - e.s[i], t = seg > 1e-6 ? (s - e.s[i]) / seg : 0;
  return [e.pts[i][0] + (e.pts[i + 1][0] - e.pts[i][0]) * t, e.pts[i][1] + (e.pts[i + 1][1] - e.pts[i][1]) * t];
}
function tangentAt(e, s) {
  const i = segAt(e, Math.max(0, Math.min(e.length, s)));
  const dx = e.pts[i + 1][0] - e.pts[i][0], dz = e.pts[i + 1][1] - e.pts[i][1], m = Math.hypot(dx, dz);
  return m > 1e-5 ? [dx / m, dz / m] : [0, 1];
}
function projectOn(e, p) {   // CityElevation.ProjectOn
  let best = Infinity, arc = 0;
  for (let i = 0; i + 1 < e.pts.length; i++) {
    const a = e.pts[i], dx = e.pts[i + 1][0] - a[0], dz = e.pts[i + 1][1] - a[1], L2 = dx * dx + dz * dz;
    const t = L2 > 1e-8 ? Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L2)) : 0;
    const qx = a[0] + dx * t, qz = a[1] + dz * t, d = (p[0] - qx) ** 2 + (p[1] - qz) ** 2;
    if (d < best) { best = d; arc = e.s[i] + Math.sqrt(L2) * t; }
  }
  return arc;
}
const corridorHalf = e => e.halfMax + 6.5;
function deckReach(over, under, sO, sU) {   // CityElevation.DeckReach
  const tO = tangentAt(over, sO), tU = tangentAt(under, sU);
  const cos = tO[0] * tU[0] + tO[1] * tU[1];
  const sin = Math.max(0.4, Math.sqrt(Math.max(0, 1 - cos * cos)));
  return (corridorHalf(under) + CORRIDOR_BLEND * 0.85 + corridorHalf(over)) / sin;
}

// ---- the decks: [{ s0, s1, src }] per edge, src 1 bridge, 2 crossing, 4 water, 8 culvert water
const iv = E.map(() => []);
for (const e of E) if (e.bridge) iv[e.index].push({ s0: 0, s1: e.length, src: 1 });
for (const c of city.crossings) {
  const over = E[c.over], under = E[c.under];
  const sO = projectOn(over, [c.x, c.z]), sU = projectOn(under, [c.x, c.z]);
  const r = deckReach(over, under, sO, sU);
  iv[c.over].push({ s0: Math.max(0, sO - r), s1: Math.min(over.length, sO + r), src: 2 });
}
city.wspans.forEach((w, k) => {
  const e = E[w.edge];
  iv[w.edge].push({ s0: Math.max(0, Math.min(e.length, w.s0)), s1: Math.max(0, Math.min(e.length, w.s1)), src: culvertSpan.has(k) ? 8 : 4 });
});
/// Which deck sources hold arc s of edge e (0: none).
function deckAt(e, s) { let m = 0; for (const r of iv[e.index]) if (s >= r.s0 - 1e-6 && s <= r.s1 + 1e-6) m |= r.src; return m; }

// ---- the census --------------------------------------------------------------
const cand = E.filter(e => iv[e.index].length);
const grid = new Map(), key = (i, j) => i * 100003 + j;
for (const e of cand) for (let k = 0; k + 1 < e.pts.length; k++) {
  const a = e.pts[k], b = e.pts[k + 1];
  for (let i = Math.floor(Math.min(a[0], b[0]) / CELL); i <= Math.floor(Math.max(a[0], b[0]) / CELL); i++)
    for (let j = Math.floor(Math.min(a[1], b[1]) / CELL); j <= Math.floor(Math.max(a[1], b[1]) / CELL); j++) {
      const kk = key(i, j); let l = grid.get(kk); if (!l) grid.set(kk, l = []); l.push(e.index * 4096 + k);
    }
}
const facing = (e, side) => side > 0 ? e.plus : e.minus;   // +1 = left of a->b
const SIDE = (p, q, t) => ((q[0] - p[0]) * -t[1] + (q[1] - p[1]) * t[0]) >= 0 ? 1 : -1;
const pairs = new Map();
let samples = 0;
for (const e of cand) {
  for (let s = STEP / 2; s < e.length; s += STEP) {
    const src = deckAt(e, s);
    if (!src) continue;
    samples++;
    const [x, z] = pointAt(e, s), t = tangentAt(e, s);
    const R = GAP_MAX + 30;
    const seen = new Map();
    for (let i = Math.floor((x - R) / CELL); i <= Math.floor((x + R) / CELL); i++)
      for (let j = Math.floor((z - R) / CELL); j <= Math.floor((z + R) / CELL); j++) {
        const l = grid.get(key(i, j)); if (!l) continue;
        for (const packed of l) {
          const oi = Math.floor(packed / 4096), k = packed % 4096;
          if (oi === e.index) continue;
          const o = E[oi], a = o.pts[k], b = o.pts[k + 1];
          const dx = b[0] - a[0], dz = b[1] - a[1], L2 = dx * dx + dz * dz;
          if (L2 < 1e-6) continue;
          const L = Math.sqrt(L2), to = [dx / L, dz / L];
          if (Math.abs(to[0] * t[0] + to[1] * t[1]) < COS_MIN) continue;
          const u = ((x - a[0]) * dx + (z - a[1]) * dz) / L2;
          if (u < 0 || u > 1) continue;
          const q = [a[0] + dx * u, a[1] + dz * u], d = Math.hypot(x - q[0], z - q[1]);
          const prev = seen.get(oi);
          if (!prev || d < prev.d) seen.set(oi, { d, at: o.s[k] + L * u, q, t: to });
        }
      }
    const best = { '-1': null, '1': null };
    for (const [oi, h] of seen) {
      const o = E[oi];
      const srcO = deckAt(o, h.at);
      if (!srcO) continue;
      const side = SIDE([x, z], h.q, t), sideO = SIDE(h.q, [x, z], h.t);
      const gap = h.d - facing(e, side) - facing(o, sideO);
      if (gap >= GAP_MAX) continue;
      const cur = best[side];
      if (!cur || gap < cur.gap) best[side] = { oi, gap, at: h.at, cos: h.t[0] * t[0] + h.t[1] * t[1], srcO, side };
    }
    for (const side of [-1, 1]) {
      const b = best[side]; if (!b) continue;
      const i = Math.min(e.index, b.oi), j = Math.max(e.index, b.oi), k = i + '|' + j;
      let P = pairs.get(k);
      if (!P) pairs.set(k, P = { i, j, gaps: [], from: {}, cos: b.cos, range: {}, culOnly: {}, total: 0 });
      P.gaps.push(b.gap);
      P.from[e.index] = (P.from[e.index] || 0) + STEP;
      P.total++;
      // the deck there is only a culvert's creek (owner Q8): on this edge, and on the partner
      if (src === 8) P.culOnly[e.index] = (P.culOnly[e.index] || 0) + 1;
      if (b.srcO === 8) P.culOnly[b.oi] = (P.culOnly[b.oi] || 0) + 1;
      const grow = (ei, v) => { const r = P.range[ei]; if (!r) P.range[ei] = [v, v]; else { if (v < r[0]) r[0] = v; if (v > r[1]) r[1] = v; } };
      grow(e.index, s); grow(b.oi, b.at);
    }
  }
}

// ---- decisions ---------------------------------------------------------------
const RANK = { motorway: 6, trunk: 5, primary: 4, secondary: 3, tertiary: 2, local: 1 };
const base = c => c.replace('_link', '');
function kindOf(a, b, cos) {
  if (a.link || b.link) return a.link && b.link ? 'ramp+ramp' : 'ramp+mainline';
  if (cos < 0) return a.name && a.name === b.name ? 'dual-carriageway' : 'opposed-other';
  return 'same-direction';
}
export function decide(r, a, b) {
  if (r.med <= SQUEEZE_M) return { u: false, G: 0, why: 'squeeze/clip (<= 0.3 m: one rail or one surface already)' };
  if (a.level !== b.level) return { u: false, G: 0, why: 'another OSM layer' };
  const ov = override.get(a.wayId + ':' + b.wayId);
  if (ov !== undefined) return { u: ov, G: ov ? G_OUTLINE : 0, why: ov ? 'override FORCE' : 'override NEVER' };
  if (r.culvert) return { u: false, G: 0, why: 'culvert creek (owner Q8: B2 builds a culvert; no union)' };
  const sa = brst.structId[a.index], sb = brst.structId[b.index];
  if (sa && sb) {
    if (sa === sb) return { u: r.med <= G_OUTLINE, G: G_OUTLINE, why: 'outline: ONE' + (r.med <= G_OUTLINE ? '' : ' (gap past 20 m)') };
    return { u: false, G: 0, why: 'outline: TWO' };
  }
  const fwy = ['motorway', 'trunk'].includes(r.cls);
  const G = r.kind === 'dual-carriageway' ? (fwy ? G_DUAL_FWY : G_DUAL_ART) : r.kind.startsWith('ramp') ? G_RAMP : G_OTHER;
  return { u: r.med <= G, G, why: `rule G=${G}` };
}
const rows = [];
for (const P of pairs.values()) {
  const a = E[P.i], b = E[P.j];
  const overlap = Math.max(...Object.values(P.from));
  if (overlap < OVERLAP_MIN) continue;
  const g = P.gaps.slice().sort((p, q) => p - q);
  const ra = RANK[base(a.cls)], rb = RANK[base(b.cls)];
  const hi = ra >= rb ? a : b;
  // the pair is a culvert's when, on either edge, most of what it sees of the other is only a culvert's creek
  const culvert = [P.i, P.j].some(ei => (P.culOnly[ei] || 0) * 2 > P.total);
  const r = {
    i: P.i, j: P.j, wayA: a.wayId, wayB: b.wayId, nameA: a.name, nameB: b.name, clsA: a.cls, clsB: b.cls,
    cls: base(hi.cls), tier: hi.tier, kind: kindOf(a, b, P.cos), med: g[g.length >> 1], min: g[0], overlap,
    structA: brst.structId[a.index], structB: brst.structId[b.index], culvert,
    rangeA: P.range[P.i], rangeB: P.range[P.j],
  };
  const d = decide(r, a, b);
  r.union = d.u; r.G = d.G; r.why = d.why;
  const pa = pointAt(a, (r.rangeA[0] + r.rangeA[1]) / 2);
  r.x = pa[0]; r.z = pa[1];
  rows.push(r);
}
rows.sort((p, q) => p.i - q.i || p.j - q.j);

// ---- report ------------------------------------------------------------------
const over03 = rows.filter(r => r.med > SQUEEZE_M);
const U = rows.filter(r => r.union);
const T = {};
for (const r of rows) {
  const k = `T${r.tier} ${r.cls}`;
  const t = T[k] = T[k] || { up: 0, um: 0, sp: 0, sm: 0, why: {} };
  if (r.union) { t.up++; t.um += r.overlap; } else { t.sp++; t.sm += r.overlap; }
  const w = (r.union ? 'UNION ' : 'separate ') + r.why; t.why[w] = (t.why[w] || 0) + 1;
}
console.log(`  ${cand.length} deck edges, ${samples} deck samples (${(samples * STEP / 1000).toFixed(1)} km), ${rows.length} parallel pairs >= ${OVERLAP_MIN} m (${over03.length} more than ${SQUEEZE_M} m apart)`);
for (const k of Object.keys(T).sort()) {
  const t = T[k];
  console.log(`  ${k.padEnd(14)} UNION ${t.up} pairs / ${t.um} m (inner parapets ${2 * t.um} m); separate ${t.sp} / ${t.sm} m`);
  for (const [w, n] of Object.entries(t.why).sort()) console.log(`      ${w}: ${n}`);
}
const um = U.reduce((a, r) => a + r.overlap, 0);
const byTier = t => U.filter(r => r.tier === t).length;
const culvertPairs = rows.filter(r => r.why.startsWith('culvert'));
const culvertWould = culvertPairs.filter(r => { const a = E[r.i], b = E[r.j]; const s = { ...r, culvert: false }; return decide(s, a, b).u; });
console.log(`DECK PAIRS: ${U.length} UNION (T1 ${byTier(1)}, T2 ${byTier(2)}, T3 ${byTier(3)}), ${um} m of carriageway pair, ${2 * um} m of inner parapet to go; ` +
            `${culvertWould.length} pairs (${culvertWould.reduce((a, r) => a + r.overlap, 0)} m) left apart as culvert creeks (Q8); unexplained 0`);
const show = r => `e${r.i}/e${r.j} way ${r.wayA}/${r.wayB} '${r.nameA}'/'${r.nameB}' ${r.clsA}/${r.clsB} ${r.kind} gap ${r.med.toFixed(2)} (min ${r.min.toFixed(2)}) x ${r.overlap} m` +
                  ` ${r.structA || r.structB ? `outline ${r.structA ? outlineName(r.structA) : '-'}/${r.structB ? outlineName(r.structB) : '-'}` : 'no outline'} -> ${r.union ? 'UNION' : 'separate'} (${r.why}) @ ${ll(r.x, r.z)}`;
for (const [label, a, b] of [['W 5th St over I-77 (the owner\'s example)', 1253, 5445], ['I-277 e2437/e2438 (negative test: two structures)', 2437, 2438],
                              ['I-277 viaduct e1910/e1921', 1910, 1921], ['I-277 e2026/e2029 (height test)', 2026, 2029]]) {
  const r = rows.find(q => q.i === Math.min(a, b) && q.j === Math.max(a, b));
  console.log(`  ${label}: ${r ? show(r) : 'not a pair'}`);
}
console.log(`  culvert creeks kept apart (owner Q8; ${brst.culvert.length} water spans within 15 m of an OSM culvert line):`);
for (const r of culvertWould) console.log('    ' + show(r));

// ---- files -------------------------------------------------------------------
const csvRow = r => [r.i, r.j, r.wayA, r.wayB, r.union ? 1 : 0, r.G, r.med.toFixed(3), r.min.toFixed(3), r.overlap, r.tier, r.cls, r.kind,
                     r.structA ? outlineName(r.structA) : '', r.structB ? outlineName(r.structB) : '', r.culvert ? 1 : 0,
                     r.rangeA[0].toFixed(1), r.rangeA[1].toFixed(1), r.rangeB[0].toFixed(1), r.rangeB[1].toFixed(1),
                     JSON.stringify(r.why), ll(r.x, r.z).replace(',', ' ')].join(',');
const HEAD = 'a,b,wayA,wayB,union,G,gap,gapMin,overlap,tier,class,kind,outlineA,outlineB,culvert,a0,a1,b0,b1,why,latlon';
if (argVal('--csv')) { writeFileSync(argVal('--csv'), HEAD + '\n' + rows.map(csvRow).join('\n') + '\n'); console.log(`wrote ${argVal('--csv')}`); }
if (argVal('--review')) {
  // the owner's Street View list: unions that rest on the class rule alone in
  // the band where outlines disagree most (3.05 m to G), and every pair an
  // outline did not settle within 1 m of its G
  const rv = rows.filter(r => r.med > SQUEEZE_M && !(r.structA && r.structB) && !r.why.startsWith('culvert') &&
                              ((r.union && r.med > G_RAMP) || (!r.union && r.G > 0 && r.med <= r.G + 1)));
  writeFileSync(argVal('--review'), 'why,' + HEAD + '\n' + rv.map(r => (r.union ? 'UNION by rule, gap over 3.05 m' : 'separate, within 1 m of G') + ',' + csvRow(r)).join('\n') + '\n');
  console.log(`wrote ${argVal('--review')} (${rv.length} pairs for the owner's Street View check)`);
}
const unionKeys = U.map(r => `${r.wayA}:${r.wayB}`).sort();
const summary = {
  graph: hashHex(city.graphHash), pairs: rows.length, over_0_3m: over03.length,
  union: { pairs: U.length, metres: um, t1: byTier(1), t2: byTier(2), t3: byTier(3) },
  culvert_kept_apart: { pairs: culvertWould.length, metres: culvertWould.reduce((a, r) => a + r.overlap, 0) },
  unexplained: 0,
};
if (ARGS.includes('--write-baseline')) {
  writeFileSync(BASELINE, JSON.stringify({ schema: 1, note: 'tools/city/deckpairs.mjs --write-baseline (plan B1): the twin-deck pairs the graph holds and which are one structure. --check fails when the union set moves; re-record with BEFORE -> AFTER in the commit.', ...summary, unionWays: unionKeys }, null, 1) + '\n');
  console.log(`wrote ${BASELINE}`);
}
let failed = false;
if (ARGS.includes('--check')) {
  if (!existsSync(BASELINE)) { console.log('CHECK: no baseline'); failed = true; }
  else {
    const b = JSON.parse(readFileSync(BASELINE, 'utf8'));
    const was = new Set(b.unionWays), now = new Set(unionKeys);
    const gone = [...was].filter(k => !now.has(k)), added = [...now].filter(k => !was.has(k));
    console.log(`CHECK against ${BASELINE}: union ${b.union.pairs} -> ${U.length}; ${added.length} pairs joined, ${gone.length} parted`);
    for (const k of added) console.log(`  + ${k}`);
    for (const k of gone) console.log(`  - ${k}`);
    if (added.length || gone.length) failed = true;
  }
}
if (argVal('--compare')) {
  // the game's table (CityAudit TWIN report: twin_pairs.csv, DeckPairs.cs)
  const lines = readFileSync(argVal('--compare'), 'utf8').trim().split(/\r?\n/);
  const head = lines.shift().split(',');
  const col = n => head.indexOf(n);
  const game = new Map();
  let solvedRows = 0;
  for (const l of lines) {
    const c = l.split(',');
    // a pair only the solve's structure holds (DeckPairs.Complete: margin decks, seats, carried
    // deck ends) has no offline twin: this census sees the facts' decks only
    if (col('solved') >= 0 && c[col('solved')] === '1') { solvedRows++; continue; }
    game.set(c[col('a')] + '|' + c[col('b')], { union: c[col('union')] === '1', gap: +c[col('gap')], overlap: +c[col('overlap')] });
  }
  if (solvedRows) console.log(`  (${solvedRows} pairs on decks only the solve made: no offline twin, not compared)`);
  let same = 0, diff = 0, onlyHere = 0, onlyGame = 0;
  const here = new Map(rows.map(r => [r.i + '|' + r.j, r]));
  for (const [k, r] of here) {
    const g = game.get(k);
    if (!g) { if (r.union) { onlyHere++; console.log(`  only offline (UNION): ${show(r)}`); } continue; }
    if (g.union === r.union) same++;
    else { diff++; console.log(`  DIFFERS game ${g.union ? 'UNION' : 'separate'} (gap ${g.gap}, ${g.overlap} m) vs offline ${show(r)}`); }
  }
  for (const [k, g] of game) if (!here.has(k) && g.union) { onlyGame++; console.log(`  only in the game (UNION): e${k.replace('|', '/e')} gap ${g.gap} x ${g.overlap} m`); }
  console.log(`COMPARE with ${argVal('--compare')}: ${same} decisions agree, ${diff} differ, ${onlyHere} unions only offline, ${onlyGame} only in the game`);
  if (diff || onlyHere || onlyGame) failed = true;
}
console.log(JSON.stringify(summary));
if (failed) process.exitCode = 1;
