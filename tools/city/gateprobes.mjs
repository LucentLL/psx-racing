// gateprobes.mjs - regression probes for the smoothness gate (linecheck.mjs's
// lib/linegate.mjs and lib/gatebase.mjs): small synthetic cities with a known
// answer, each run through the same replica and gate as the shipped graph.
// Every review finding the gate was fixed for is a probe here, so a change to
// the gate that brings one back fails:
//   K   a lone kink, and a kink SPLIT over close vertices (B2 clusters)
//   A   legitimate curves that must pass: fillets sampled at the 2 cm sagitta,
//       tangent-arc joins, WP-11-style Emax fillets
//   J   a kink at a 2-arm node with a sub-V step there (joined, still judged)
//   F   2-arm nodes drawn as fans (bend fans): judged across, mouths not legit
//   D   D1: a branch's attach arc is report-only, the same pair elsewhere gated
//   S   straight roads: nothing gated
//   R   the ratchet: growth inside a key's bucket, a second run, a move, a
//       worse peak all FAIL; the same data PASSES; other inputs are STALE
//
//   node tools/city/gateprobes.mjs [K|A|J|F|D|S|R ...]      exit 1 on any failed probe
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readSmoothRules } from './lib/smoothrules.mjs';
import { loadPaintLayouts } from './lib/paintruns.mjs';
import { city, bent, fillet, sagChord, gateOf, fwd, DEG } from './lib/synthcity.mjs';
import { summarize, ratchet, packKeys, packTiles, SCHEMA } from './lib/gatebase.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const R = readSmoothRules(join(UNITY, 'Assets/PSXRacing/Editor/SmoothRules.cs'));
const KEYS = ['tw2', 'tw4', 'tw3t', 'tw5t', 'tw6', 'ow1', 'ow2', 'ow3', 'ow4', 'mw2', 'mw3', 'mw4', 'mw5', 'mw6', 'xw2', 'xw3', 'xw4', 'ramp1', 'ramp2', 'ramp3'];
const { layouts } = loadPaintLayouts(join(UNITY, 'Assets/PSXRacing/Art/City'), KEYS);
const only = process.argv.slice(2).map(s => s.toUpperCase());
const want = g => !only.length || only.includes(g);
const REP = 12.192;

let failed = 0, passed = 0;
const gated = res => res.g.runs.filter(r => !r.reportOnly);
const count = (res, check, f = () => true) => gated(res).filter(r => r.check === check && f(r)).length;
const fmt = res => {
  const by = {};
  for (const r of gated(res)) { const b = by[r.check] ||= { n: 0, w: 0 }; b.n++; b.w = Math.max(b.w, r.ratio); }
  return Object.entries(by).map(([k, v]) => `${k} ${v.n} (x${v.w.toFixed(1)})`).join(', ') || 'nothing gated';
};
function probe(name, ok, detail) {
  if (ok) passed++; else failed++;
  console.log(`${ok ? '  ok  ' : '  FAIL'} ${name}${detail ? ` - ${detail}` : ''}`);
}
const tw2 = pts => city([{ pts }]);
const hw2 = 3.9624;

// ---- K: kinks, lone and split over close vertices (every line of a tw2 local street)
if (want('K')) {
  console.log('K  B2 KINK: a lone kink, and the same corner split over close vertices');
  const lines7 = res => new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId)).size;
  let res = gateOf(tw2(bent([1.1], [])), R, layouts);
  probe('K1 lone 1.1 deg passes (the spec allows a lone 1.15 deg)', count(res, 'B2') === 0, fmt(res));
  res = gateOf(tw2(bent([1.3], [])), R, layouts);
  probe('K1 lone 1.3 deg is a KINK on all 7 lines', lines7(res) === 7, fmt(res));
  for (const [name, turns, gaps, extra] of [
    ['3 deg as 1.5 + 1.5, 0.6 m apart', [1.5, 1.5], [0.6]],
    ['3 deg as 1 + 1 + 1, 0.6 m apart', [1, 1, 1], [0.6, 0.6]],
    ['4 deg as 2 + 2, 1 m apart', [2, 2], [1]],
    ['6 deg as 3 + 3, 1 m apart', [3, 3], [1]],
    ['6 deg as 2 + 2 + 2, 0.7 m apart', [2, 2, 2], [0.7, 0.7]],
    ['8 deg as 4 x 2, 0.6 m apart', [2, 2, 2, 2], [0.6, 0.6, 0.6]],
    ['10 deg as 5 x 2, 0.6 m apart', [2, 2, 2, 2, 2], [0.6, 0.6, 0.6, 0.6]],
    ['12 deg as 4 x 3, 0.8 m apart', [3, 3, 3, 3], [0.8, 0.8, 0.8]],
    ['5 deg as 4 + 1, 0.6 m apart', [4, 1], [0.6]],
    ['15.4 deg as 7.7 + 7.7, 0.91 m apart, tertiary (Lower Rocky River Road, e55)', [7.7, 7.7], [0.91], { rank: 1 }],
    ['10 deg as 5 + 5, 1.3 m apart, secondary tw4', [5, 5], [1.3], { rank: 2, lanes: 4 }],
    ['3 deg as 1.5 + 1.5, 0.6 m apart, motorway mw3', [1.5, 1.5], [0.6], { rank: 5, lanes: 3, oneway: true }],
  ]) {
    res = gateOf(city([{ pts: bent(turns, gaps), ...(extra || {}) }]), R, layouts);
    const nLines = res.S.E[0].profile.key === 'tw2' ? 7 : 0;
    probe(`K2 ${name} is a KINK${nLines ? ' on all 7 lines' : ''}`, nLines ? lines7(res) === 7 : count(res, 'B2') > 0, fmt(res));
  }
}

// ---- A: legitimate curves that must pass the shape checks
if (want('A')) {
  console.log('A  legitimate curves: no B2 KINK, no B3 CURVE (fillets 5% over R_min: B3 reads a sampled arc about 2% under its radius)');
  // B1 at a tangent-arc join on the INNER ribbon edge of an R_min street fillet (inner radius ~3.5 m) is past V: an open question for
  // the spec (its B1 calibration is for the centreline down to R 7.5), printed, not a probe
  const shapeFree = res => ['B2', 'B3'].every(c => count(res, c) === 0);
  const info = res => count(res, 'B1') ? `; B1 ${count(res, 'B1')} on ${[...new Set(gated(res).filter(r => r.check === 'B1').map(r => r.lineId))].join(' ')}` : '';
  for (const [R0, D, rank, lanes, oneway] of [[7.9, 90, 0, 2], [12, 60, 1, 2], [30, 40, 1, 2], [100, 20, 2, 2], [500, 10, 5, 3, true]]) {
    const hw = city([{ pts: [[0, 0], [0, 1]], rank, lanes, oneway }]).edges[0].hw;
    const res = gateOf(city([{ pts: fillet(R0, D, sagChord(R0, hw)), rank, lanes, oneway }]), R, layouts);
    probe(`A1 fillet R ${R0} m turning ${D} deg, chords at the 2 cm sagitta, tangent-arc joins (${res.S.E[0].profile.key})`, shapeFree(res), fmt(res) + info(res));
  }
  // WP-11's fillets: R = Emax / (sec(D/2) - 1), never under R_min (street Emax 0.75 m)
  for (const D of [10, 20, 30, 51, 90]) {
    const R0 = Math.max(7.9, hw2 + 3, 0.75 / (1 / Math.cos(D * DEG / 2) - 1));
    const res = gateOf(tw2(fillet(R0, D, sagChord(R0, hw2))), R, layouts);
    probe(`A2 WP-11 street fillet turning ${D} deg (R ${R0.toFixed(1)} m, arc ${(R0 * D * DEG).toFixed(1)} m)`, shapeFree(res), fmt(res) + info(res));
  }
  for (const [R0, c] of [[30, 0.5], [100, 1], [400, 4]]) {
    const res = gateOf(tw2(fillet(R0, 30, c)), R, layouts);
    probe(`A3 finely sampled arc R ${R0} m, chords ${c} m, 30 deg`, shapeFree(res) && count(res, 'B1') === 0, fmt(res));
  }
}

// ---- J: a kink at a 2-arm node with a sub-V step across it
if (want('J')) {
  console.log('J  a 4 deg kink at a mitred 2-arm node, the second edge stepped sideways by under V');
  const p0 = [0, 0], p1 = fwd(p0, 0, 8 * REP), q2 = fwd(p1, 4 * DEG, 8 * REP);
  for (const d of [0, 0.004, 0.015, 0.024]) {
    const res = gateOf(city([{ pts: [p0, p1] }, { pts: [p1, q2] }]), R, layouts, { tweakSecs: S => S.E[1].secs.forEach(x => {
      x.L = [x.L[0] + x.right[0] * d, x.L[1] + x.right[1] * d]; x.R = [x.R[0] + x.right[0] * d, x.R[1] + x.right[1] * d]; }) });
    const n = new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId)).size;
    probe(`J5 step ${(d * 100).toFixed(1)} cm: the kink is still a KINK on all 7 lines, no JUMP`, n === 7 && count(res, 'B4') === 0, fmt(res));
  }
}

// ---- F: 2-arm nodes drawn as fans
if (want('F')) {
  console.log('F  bend fans: a 2-arm node past ContinueCos is drawn as a junction slab (plan A2: never legitimate)');
  for (const t of [45, 90]) {
    const p0 = [0, 0], p1 = fwd(p0, 10 * DEG, 64.3), p2 = fwd(p1, (10 + t) * DEG, 64.3);
    const res = gateOf(city([{ pts: [p0, p1] }, { pts: [p1, p2] }]), R, layouts);
    probe(`F1 ${t} deg bend fan (patch ${res.S.T.patch[1]}): judged across (B2) and its mouths are no legitimate end (C2)`,
      res.S.T.patch[1] === 1 && count(res, 'B2') > 0 && count(res, 'C2') >= 8, fmt(res));
  }
}

// ---- D: D1 merge zones are the attach arc only
if (want('D')) {
  console.log('D  D1 CROSS: report-only only on a branch attach arc');
  const h0 = [0, 0], h1 = fwd(h0, 0, 100), h2 = fwd(h1, 0, 100);
  const bA = fwd(h1, 12 * DEG, 60), bB = fwd(bA, -40 * DEG, 60);
  const res = gateOf(city([{ pts: [h0, h1], lanes: 4, rank: 1 }, { pts: [h1, h2], lanes: 4, rank: 1 }, { pts: [h1, bA, bB], lanes: 2 }]), R, layouts);
  const clipEnd = Math.max(...res.S.clipsOf(2).map(c => c.sTo));
  const ro = res.g.runs.filter(r => r.check === 'D1' && r.reportOnly && r.e === 2), gt = res.g.runs.filter(r => r.check === 'D1' && !r.reportOnly && r.e === 2);
  probe(`D3 a branch bending back across its host 60 m on (clip ends at s ${clipEnd.toFixed(1)}): gated past the arc, report-only only on it`,
    gt.some(r => r.s > clipEnd + R.MergeMarginM) && ro.every(r => Math.max(r.s0, r.s1) <= clipEnd + R.MergeMarginM + 0.5),
    `${gt.length} gated, ${ro.length} report-only on the branch`);
  const a0 = [0, 0], a1 = fwd(a0, 0, 120), b0 = [-60, 60], b1 = fwd(b0, 90 * DEG, 120);
  const x = gateOf(city([{ pts: [a0, a1] }, { pts: [b0, b1] }]), R, layouts);
  probe('D1 two roads crossing at grade with no node: gated', count(x, 'D1') > 0, fmt(x));
}

// ---- S: straight roads
if (want('S')) {
  console.log('S  straight roads: nothing gated');
  for (const [lanes, turn, oneway, rank, link] of [[2, false, false, 0], [4, false, false, 1], [3, true, false, 2], [6, false, false, 3], [2, false, true, 0], [3, false, true, 5], [2, false, true, 5, true]])
    for (const hdg of [0, 37 * DEG]) {
      const L = 8 * REP, p0 = [100, 100], p1 = fwd(p0, hdg, L), p2 = fwd(p1, hdg, L), p3 = fwd(p2, hdg, L);
      const w = { lanes, turn, oneway, rank, link };
      const res = gateOf(city([{ ...w, pts: [p0, p1] }, { ...w, pts: [p1, p2] }, { ...w, pts: [p2, p3] }]), R, layouts);
      probe(`S1 ${res.S.E[0].profile.key} heading ${(hdg / DEG).toFixed(0)}, split in three at whole dash cycles`, gated(res).length === 0, fmt(res));
    }
  // split at other lengths the dash phase restarts at every split (V restarts per edge; WP-11's chain continuity): C3, and only C3
  const p0 = [0, 0], p1 = fwd(p0, 20 * DEG, 100), p2 = fwd(p1, 20 * DEG, 77.3), p3 = fwd(p2, 20 * DEG, 130);
  const res = gateOf(city([{ pts: [p0, p1], lanes: 4, rank: 1 }, { pts: [p1, p2], lanes: 4, rank: 1 }, { pts: [p2, p3], lanes: 4, rank: 1 }]), R, layouts);
  probe('S2 tw4 split 100 / 77.3 / 130 m: the dash phase restart is C3 DASH, and nothing else', count(res, 'C3') > 0 && gated(res).every(r => r.check === 'C3'), fmt(res));
}

// ---- R: the ratchet
if (want('R')) {
  console.log('R  the ratchet (lib/gatebase.mjs, what linecheck.mjs runs)');
  const pts = []; for (let i = 0; i <= 120; i++) pts.push(fwd([0, 0], 30 * DEG, i));
  const mk = () => city([{ pts, wayId: 7001 }]);
  const jog = (set, d) => ({ tweakSecs: S => S.E[0].secs.forEach(x => { if (set.some(s => Math.abs(x.s - s) < 0.01)) {
    x.L = [x.L[0] + x.right[0] * d, x.L[1] + x.right[1] * d]; x.R = [x.R[0] + x.right[0] * d, x.R[1] + x.right[1] * d]; x.p = [x.p[0] + x.right[0] * d, x.p[1] + x.right[1] * d]; } }) });
  const summ = opts => summarize(gateOf(mk(), R, layouts, opts).g, R);
  const inputs = { graph: 'synthetic', sections: { EDGE: 'a' }, rules: 'r', paint: 'p', model: 'asbuilt', planTaper: 'linear' };
  const entryOf = (s, inp) => { const checks = {}; for (const [id, c] of Object.entries(s)) { const k = packKeys(c.keys, R); checks[id] = { runs: c.runs, metres: Math.round(c.metres * 10) / 10, worst: c.worst, worstRatio: Math.round(c.worstRatio * 1000) / 1000, keys: k.n, keys_b64: k.b64, tiles_b64: packTiles(c.tiles) }; } return { schema: SCHEMA, date: 'probe', inputs: inp, checks }; };
  const B = entryOf(summ(jog([51], 0.05)), inputs);
  const verdict = opts => ratchet(summ(opts), B, R, { inputs });
  probe('R1 the same data passes', verdict(jog([51], 0.05)).ok);
  probe('R2 a worse peak (6 cm) fails', !verdict(jog([51], 0.06)).ok);
  probe('R3 the same jog held over s 49-52 (longer inside its buckets) fails', !verdict(jog([49, 50, 51, 52], 0.05)).ok);
  probe('R4 held over s 48-52 fails', !verdict(jog([48, 49, 50, 51, 52], 0.05)).ok);
  probe('R5 a second run in the same bucket fails', !verdict(jog([51, 49], 0.05)).ok);
  probe('R6 moved to s 56 fails', !verdict(jog([56], 0.05)).ok);
  const st = ratchet(summ(jog([51], 0.05)), B, R, { inputs: { ...inputs, sections: { EDGE: 'a', SPAN: 'b' } } });
  probe('R7 other inputs (a new SPAN section) are STALE, not FAIL', !st.ok && !!st.stale && st.stale.some(s => s.includes('SPAN')), st.stale && st.stale.join('; '));
}

console.log(`\n${passed} probes passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
