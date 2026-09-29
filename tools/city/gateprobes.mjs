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
//   H   a HEDGED corner (a turn and a turn back of a quarter of it or more
//       within a metre or two) judged by its net turn; a NOTCH (counter-turns
//       either side, the drawing overshooting its net corner) by its signed
//       rounding; S-bends of sampled arcs are not
//   G   a JOG (a sideways step between two straights, the K7 case; review 4:
//       over any transition up to ChordCapM) fails by the part of it that
//       came faster than the plan's ease; a sub-V one, or a slow drift, does
//       not
//   Z   a BUMP (off a straight and back) and a ZIGZAG (review 4: where the net
//       turn is 0 nothing measured the excursion): peak deviation past V fails
//   E   C2's closed list: a line starting along a branch's attach arc is no
//       legitimate end; at a gore NOSE (a collapsed section) it is
//   Q   a SQUEEZED ribbon edge against its I7 envelope: a squeeze that arrives
//       faster than a taper over the class floor, steps, or wanders back out
//       fails A1 - also inside a taller, slow rise (review 4: the envelope
//       took the whole rise's height); one eased over the floor or longer, or
//       several stacked, does not
//   R   the ratchet: growth inside a key's bucket, a second run, a move, a
//       worse peak all FAIL; the same data PASSES; other inputs are STALE,
//       and STALE FAILS; a check gating looser than its baseline FAILS
//
//   node tools/city/gateprobes.mjs [K|A|J|F|D|S|H|G|Z|E|Q|R ...]    exit 1 on any failed probe
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readSmoothRules } from './lib/smoothrules.mjs';
import { loadPaintLayouts } from './lib/paintruns.mjs';
import { city, bent, fillet, sagChord, gateOf, fwd, DEG } from './lib/synthcity.mjs';
import { summarize, ratchet, packKeys, packTiles, SCHEMA, statesOf, beforeAfter } from './lib/gatebase.mjs';

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

// ---- H: hedged corners (review 3): the parts scored centimetres, the net corner was never judged
if (want('H')) {
  console.log('H  B2 KINK: a turn and a turn back of a quarter of it or more is judged by its NET corner');
  const lines7 = res => new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId)).size;
  const worst = (res, c) => Math.max(0, ...gated(res).filter(r => r.check === c).map(r => r.ratio));
  for (const [a, b, g] of [[6, -2.4, 0.8], [6, -2.4, 1.2], [4, -1.1, 1], [3, -1.2, 0.8], [2, -0.52, 1.2]]) {
    const res = gateOf(tw2(bent([a, b], [g])), R, layouts), lone = gateOf(tw2(bent([a + b], [])), R, layouts);
    probe(`H1 [${a}, ${b}] deg ${g} m apart (net ${(a + b).toFixed(2)}): a KINK on all 7 lines, at least as bad as the lone net corner (x${worst(lone, 'B2').toFixed(2)})`,
      lines7(res) === 7 && worst(res, 'B2') >= worst(lone, 'B2') * 0.99 && gated(res).some(r => r.check === 'B2' && /hedged/.test(r.what || '')), fmt(res));
  }
  for (const [label, w] of [['tw4 secondary', { rank: 2, lanes: 4 }], ['mw3 motorway', { rank: 5, lanes: 3, oneway: true }]]) {
    const res = gateOf(city([{ pts: bent([6, -2.4], [1]), ...w }]), R, layouts);
    probe(`H2 [6, -2.4] deg 1 m apart on a ${label}: a KINK`, count(res, 'B2') > 0, fmt(res));
  }
  // S-bends of sampled arcs turn and turn back too: the chords stop at the next vertex, so the net corner never shows
  for (const [R0, D, L, rank] of [[30, 20, 0, 1], [30, 45, 0, 1], [30, 45, 2, 1], [100, 10, 0, 2], [100, 10, 5, 2], [12, 30, 1, 1], [12, 30, 5, 1]]) {
    const hw = city([{ pts: [[0, 0], [0, 1]], rank }]).edges[0].hw, c = sagChord(R0, hw);
    const n = Math.max(1, Math.ceil(R0 * D * DEG / c)), dh = D * DEG / n, ch = 2 * R0 * Math.sin(dh / 2);
    const pts = [[0, 0]]; let h = 10 * DEG, p = fwd(pts[0], h, 64.3); pts.push(p);
    for (let k = 0; k < n; k++) { h += k === 0 ? dh / 2 : dh; p = fwd(p, h, ch); pts.push(p); }
    h += dh / 2; if (L > 0) { p = fwd(p, h, L); pts.push(p); }
    for (let k = 0; k < n; k++) { h -= k === 0 ? dh / 2 : dh; p = fwd(p, h, ch); pts.push(p); }
    h -= dh / 2; pts.push(fwd(p, h, 64.3));
    const res = gateOf(city([{ pts, rank }]), R, layouts);
    probe(`H3 S-bend: R ${R0} m turning ${D} deg and back, ${L} m between, chords at the 2 cm sagitta: no KINK`, count(res, 'B2') === 0, fmt(res));
  }
  // review 4: a NOTCH - a corner with counter-turns either side, its drawing overshooting the net corner - in a window
  // wider than 2.5 m got only the roundest drawing's credit, and passed while its lone net corner failed
  for (const [c, a, g, w] of [[1.8, 6, 1.5], [1.5, 5, 2], [1.2, 4, 1.5], [1.8, 6, 1.5, { rank: 2, lanes: 4 }]]) {
    const res = gateOf(city([{ pts: bent([-c, a, -c], [g, g]), ...(w || {}) }]), R, layouts), lone = gateOf(city([{ pts: bent([a - 2 * c], []), ...(w || {}) }]), R, layouts);
    const lines = new Set(gated(lone).filter(r => r.check === 'B2').map(r => r.lineId));
    const got = new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId));
    probe(`H4 notch [-${c}, ${a}, -${c}] deg ${g} m apart on ${res.S.E[0].profile.key} (net ${(a - 2 * c).toFixed(1)}): a KINK on every line its lone net corner fails (${lines.size}), at least as bad (x${worst(lone, 'B2').toFixed(2)})`,
      lines.size > 0 && [...lines].every(l => got.has(l)) && worst(res, 'B2') >= worst(lone, 'B2') * 0.99, fmt(res));
  }
}

// ---- G: jogs (K7, review 2's open list): a sideways step between two straights
if (want('G')) {
  console.log('G  B2 KINK: a jog - a sideways step of V or more between two straights - fails (B4 fails the same step at a node)');
  const lines7 = res => new Set(gated(res).filter(r => r.check === 'B2' && /jog/.test(r.what || '')).map(r => r.lineId)).size;
  for (const [t, g] of [[3, 0.6], [2.8, 0.6], [1, 2], [3, 2]]) {
    const res = gateOf(tw2(bent([t, -t], [g])), R, layouts);
    probe(`G1 [+${t}, -${t}] deg ${g} m apart: a ${(g * Math.sin(t * DEG) * 100).toFixed(1)} cm jog on all 7 lines`, lines7(res) === 7, fmt(res));
  }
  for (const [t, g] of [[1.2, 0.3], [2, 0.6], [2.3, 0.6]]) {
    const res = gateOf(tw2(bent([t, -t], [g])), R, layouts);
    probe(`G2 [+${t}, -${t}] deg ${g} m apart: a ${(g * Math.sin(t * DEG) * 100).toFixed(1)} cm step, under V: nothing`, gated(res).length === 0, fmt(res));
  }
  // the part of a step that came faster than the plan's fastest ease (a smoothstep over the street's 15 m floor) is judged
  for (const [t, g, why] of [[2.5, 0.6, 'of which 2.45 cm came faster than the ease'], [0.17, 10, 'a slow drift: 0.44 cm faster than the ease']]) {
    const res = gateOf(tw2(bent([t, -t], [g])), R, layouts);
    probe(`G2 [+${t}, -${t}] deg ${g} m apart: a ${(g * Math.sin(t * DEG) * 100).toFixed(1)} cm step, ${why}: nothing`, gated(res).length === 0, fmt(res));
  }
  const res = gateOf(tw2(bent([1, 1, -1, -1], [0.5, 0.5, 0.5])), R, layouts);
  probe('G3 a 3.5 cm jog eased over four vertices 0.5 m apart: a jog', count(res, 'B2', r => /jog/.test(r.what || '')) > 0, fmt(res));
  // review 4: the rule stopped at a 2.5 m transition (its straights had to be 4x the window inside the 10 m chord cap)
  for (const [t1, t2, g, w] of [[1.2, 1.2, 3], [1.7, 1.7, 5], [3.3, 3.4, 3.2], [1.5, 1.5, 6], [2, 2, 9], [1.7, 1.7, 5, { rank: 2, lanes: 4 }], [3.3, 3.4, 3.2, { rank: 3, oneway: true, lanes: 3 }]]) {
    const pts = bent([t1, -t2], [g]), res = gateOf(city([{ pts, ...(w || {}) }]), R, layouts);
    const jl = new Set(gated(res).filter(r => r.check === 'B2' && /jog/.test(r.what || '')).map(r => r.lineId)), bl = new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId));
    probe(`G4 [+${t1}, -${t2}] deg ${g} m apart on ${res.S.E[0].profile.key}: a ${(g * Math.sin(t1 * DEG) * 100).toFixed(1)} cm step: a jog on every line`, jl.size >= 5 && jl.size === bl.size, `${jl.size} lines; ${fmt(res)}`);
  }
  // a short S-bend of sampled arcs between straights is a step too: R 30 m arcs turning 3 degrees each way shift the line
  // 8.2 cm in 3.1 m. The long S-bends (H3) are not: they never fit a ChordCapM window
  {
    const R0 = 30, D = 3, c = sagChord(R0, hw2), n = Math.max(1, Math.ceil(R0 * D * DEG / c)), dh = D * DEG / n, ch = 2 * R0 * Math.sin(dh / 2);
    const pts = [[0, 0]]; let h = 10 * DEG, p = fwd(pts[0], h, 64.3); pts.push(p);
    for (let k = 0; k < n; k++) { h += k === 0 ? dh / 2 : dh; p = fwd(p, h, ch); pts.push(p); }
    h += dh / 2;
    for (let k = 0; k < n; k++) { h -= k === 0 ? dh / 2 : dh; p = fwd(p, h, ch); pts.push(p); }
    h -= dh / 2; pts.push(fwd(p, h, 64.3));
    const res = gateOf(tw2(pts), R, layouts);
    probe('G5 a 3.1 m S-bend of R 30 m arcs (8.2 cm across) between straights: a jog on all 7 lines', new Set(gated(res).filter(r => r.check === 'B2' && /jog/.test(r.what || '')).map(r => r.lineId)).size === 7, fmt(res));
  }
}

// ---- Z: bumps and zigzags (review 4): where the approach and exit lines coincide (net turn 0) no rule measured the excursion
if (want('Z')) {
  console.log('Z  B2 KINK: a bump off a straight and back, and a zigzag - peak deviation past V (gate spec section 2) fails');
  const b2lines = res => new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId)).size;
  const peak = pts => { const [a, b] = [pts[0], pts[1]], ux = b[0] - a[0], uz = b[1] - a[1], L = Math.hypot(ux, uz); const lat = pts.slice(1, -1).map(p => ((p[0] - a[0]) * uz - (p[1] - a[1]) * ux) / L); return Math.max(...lat) - Math.min(...lat); };
  for (const [t, g, w] of [[2, 2], [3, 1.5], [1, 4], [1, 2], [1.5, 3], [2, 2, { rank: 2, lanes: 4 }], [2, 2, { rank: 5, lanes: 3, oneway: true }]]) {
    const pts = bent([t, -2 * t, t], [g, g]), res = gateOf(city([{ pts, ...(w || {}) }]), R, layouts);
    const n = b2lines(res), lines = res.S.E[0].profile.key === 'tw2' ? 7 : 5;
    probe(`Z1 bump [${t}, ${-2 * t}, ${t}] deg ${g} m apart on ${res.S.E[0].profile.key}: ${(peak(pts) * 100).toFixed(1)} cm off the straight over ${2 * g} m: a KINK on ${lines === 7 ? 'all 7' : 'at least 5'} lines`,
      n >= lines && gated(res).some(r => r.check === 'B2' && /bump|zigzag/.test(r.what || '')), fmt(res));
  }
  const zig = (t, g, legs) => { const turns = [t / 2]; for (let i = 1; i < legs; i++) turns.push(i % 2 ? -t : t); turns.push(legs % 2 ? -t / 2 : t / 2); return bent(turns, turns.slice(1).map(() => g)); };
  for (const [t, g, legs] of [[3, 3, 8], [2, 5, 6], [3, 2, 8], [1.5, 5, 6]]) {
    const pts = zig(t, g, legs), res = gateOf(tw2(pts), R, layouts);
    probe(`Z2 zigzag +-${t} deg every ${g} m (+-${(peak(pts) * 50).toFixed(1)} cm about its mean line): a KINK on all 7 lines`, b2lines(res) === 7, fmt(res));
  }
  for (const [label, pts] of [['Z3 zigzag +-2 deg every 2 m (+-1.7 cm)', zig(2, 2, 8)], ['Z3 zigzag +-1 deg every 4 m (+-1.7 cm)', zig(1, 4, 6)],
    ['Z3 bump [0.5, -1, 0.5] deg 2 m apart (1.7 cm)', bent([0.5, -1, 0.5], [2, 2])], ['Z3 bump [0.3, -0.6, 0.3] deg 4 m apart (2.1 cm over 8 m)', bent([0.3, -0.6, 0.3], [4, 4])]]) {
    const res = gateOf(tw2(pts), R, layouts);
    probe(`${label}, under V: nothing`, gated(res).length === 0, fmt(res));
  }
}

// ---- E: C2's closed list (review 3: any clipped section within 2 m excused an end as a "gore")
if (want('E')) {
  console.log('E  C2 END: only a gore NOSE (a collapsed section) excuses an end, not a branch\'s attach arc');
  for (const ang of [15, 25, 35]) {
    const h0 = [0, 0], h1 = fwd(h0, 0, 100), h2 = fwd(h1, 0, 100), b1 = fwd(h1, ang * DEG, 100);
    const ways = [{ pts: [h0, h1] }, { pts: [h1, h2] }, { pts: [h1, b1] }];
    const res = gateOf(city(ways), R, layouts);
    const ends = gated(res).filter(r => r.check === 'C2' && r.e === 2 && /^start/.test(r.what));
    probe(`E1 a side street leaving its host at ${ang} deg: its double yellow starts along the attach arc - C2 on both lines`,
      ['CYs+0.12', 'CYs-0.12'].every(id => ends.some(r => r.lineId === id)), ends.map(r => `${r.lineId} s ${r.s.toFixed(1)}`).join(', '));
    if (ang !== 25) continue;
    // the same, with the sections by the yellows' starts made a gore nose
    const at = ends.filter(r => r.lineId.startsWith('CY')).map(r => r.s);
    const nose = gateOf(city(ways), R, layouts, { tweakSecs: S => S.E[2].secs.forEach(x => { if (at.some(s => Math.abs(x.s - s) <= 1)) x.collapsed = true; }) });
    const e2 = gated(nose).filter(r => r.check === 'C2' && r.e === 2 && r.lineId.startsWith('CY') && /^start/.test(r.what));
    probe('E2 the same starts within GoreNoseM of a collapsed section (a gore nose): legitimate', at.length > 0 && e2.length === 0, e2.map(r => r.what).join('; ') || `starts at s ${at.map(s => s.toFixed(1)).join(', ')}`);
  }
}

// ---- Q: a squeezed ribbon edge against its I7 envelope (review 3: X6 exempted it, a smooth squeeze step passed)
if (want('Q')) {
  console.log('Q  A1 on a SQUEEZED ribbon edge: eased like a taper over the class floor (local 15 m, arterial 30 m), held between cuts');
  const ss = t => { t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t); };
  const arcStep = (x, x0, Rj, D) => {   // two tangent arcs of radius Rj stepping D (review 3's edgejog_crop.mjs)
    const th = Math.acos(1 - D / 2 / Rj), L = Rj * Math.sin(th), t = x - x0;
    if (t <= 0) return 0; if (t >= 2 * L) return D;
    if (t <= L) return Rj - Math.sqrt(Rj * Rj - t * t);
    const u = 2 * L - t; return D - (Rj - Math.sqrt(Rj * Rj - u * u));
  };
  const sq = (cutAt, { step = 0.5, lanes = 2, rank = 0, len = 120 } = {}) => {
    const pts = []; for (let i = 0; i <= len / step; i++) pts.push(fwd([0, 0], 20 * DEG, i * step));
    return gateOf(city([{ pts, lanes, rank }]), R, layouts, { tweakSecs: S => S.E[0].secs.forEach(x => {
      const d = cutAt(x.s); if (!(d > 0)) return;
      x.L = [x.L[0] + x.right[0] * d, x.L[1] + x.right[1] * d]; x.latL += d; x.uL = 0.5 - x.latL / S.E[0].width; x.sqL = true; }) });
  };
  const env = res => gated(res).filter(r => r.check === 'A1' && r.lineId === 'RL' && /I7/.test(r.what || ''));
  const w = res => env(res).length ? `A1 x${Math.max(...env(res).map(r => r.ratio)).toFixed(1)}; ${fmt(res)}` : fmt(res);
  for (const [label, cut] of [
    ['Q1 1 m in over ~8 m through two 15 deg arcs of R 15 m (review 3), U cropped', s => arcStep(s, 50, 15, 1)],
    ['Q1 1 m in as a smoothstep over 8 m (a 15 m floor)', s => ss((s - 40) / 8)],
    ['Q1 0.1 m in over 5 m', s => 0.1 * ss((s - 40) / 5)],
    ['Q1 in steps: 0.5 m over 1 m, 10 m flat, 0.5 m over 1 m', s => s < 40 ? 0 : s < 41 ? 0.5 * (s - 40) : s < 51 ? 0.5 : s < 52 ? 0.5 + 0.5 * (s - 51) : 1],
    ['Q1 held 0.5 m in, wandering +-5 cm every 2 m', s => s < 40 ? 0.5 * ss((s - 25) / 15) : 0.5 + (Math.round(s / 2) % 2 ? 0.05 : -0.05)],
  ]) { const res = sq(cut); probe(`${label}: A1 on the edge (outside its I7 envelope)`, env(res).length > 0, w(res)); }
  const t4 = sq(s => ss((s - 40) / 15), { lanes: 4, rank: 2 });
  probe('Q1 a tw4 secondary squeezed 1 m over 15 m, under its 30 m floor: A1 on the edge', env(t4).length > 0, w(t4));
  for (const [label, cut, o] of [
    ['Q2 1 m in as a smoothstep over the 15 m floor', s => ss((s - 40) / 15)],
    ['Q2 1 m in over 15 m, out over 15 m after a 15 m hold', s => ss((s - 30) / 15) * (1 - ss((s - 60) / 15))],
    ['Q2 1 m in over 20 m', s => ss((s - 40) / 20)],
    ['Q2 0.3 m in over 15 m, sections every 2 m', s => 0.3 * ss((s - 40) / 15), { step: 2 }],
    ['Q2 held 0.5 m in, 2 cm (sub-V) dips every other section', s => s < 40 ? 0.5 * ss((s - 25) / 15) : 0.5 - (Math.round(s / 2) % 2 ? 0.02 : 0)],
    ['Q2 a tw4 secondary 1 m in over its 30 m floor', s => ss((s - 40) / 30), { lanes: 4, rank: 2 }],
  ]) { const res = sq(cut, o); probe(`${label}: no A1 on the edge`, env(res).length === 0, w(res)); }
  // review 4: EASE took the WHOLE rise's height, so a fast step hidden inside a taller, slow rise passed
  for (const [label, cut, o] of [
    ['Q3 1 m in by two R 20 m arcs (8.9 m), after a slow 1 m ramp over 80 m', s => ss((s - 20) / 80) + arcStep(s, 100, 20, 1), { len: 200 }],
    ['Q3 0.6 m in over 4 m inside a slow 2 m ramp over 150 m', s => 2 * ss((s - 20) / 150) + 0.6 * ss((s - 100) / 4), { len: 200 }],
    ['Q3 a tw4 secondary 1 m in by two R 40 m arcs (12.6 m), after a slow 1.5 m ramp over 150 m', s => 1.5 * ss((s - 50) / 150) + arcStep(s, 200, 40, 1), { len: 400, lanes: 4, rank: 2 }],
  ]) { const res = sq(cut, o); probe(`${label}: A1 on the edge`, env(res).length > 0 && Math.max(...env(res).map(r => r.ratio)) > 5, w(res)); }
  for (const [label, cut, o] of [
    ['Q4 2 m in over 45 m (three floors)', s => 2 * ss((s - 40) / 45), { len: 160 }],
    ['Q4 1 m in over 15 m, then 1 m more over 15 m', s => ss((s - 40) / 15) + ss((s - 55) / 15)],
    ['Q4 1 m in over 15 m, then 1 m more over 15 m, 10 m later', s => ss((s - 40) / 15) + ss((s - 65) / 15)],
    ['Q4 a slow 1 m ramp over 80 m, then 1 m more over the 15 m floor', s => ss((s - 20) / 80) + ss((s - 110) / 15), { len: 200 }],
    ['Q4 1 m in over 15 m, sections every 2 m', s => ss((s - 40) / 15), { step: 2 }],
  ]) { const res = sq(cut, o); probe(`${label}: no A1 on the edge`, env(res).length === 0, w(res)); }
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
  const entryOf = (s, inp, states = statesOf(R)) => { const checks = {}; for (const [id, c] of Object.entries(s)) { const k = packKeys(c.keys, R); checks[id] = { runs: c.runs, metres: Math.round(c.metres * 10) / 10, worst: c.worst, worstRatio: Math.round(c.worstRatio * 1000) / 1000, keys: k.n, keys_b64: k.b64, tiles_b64: packTiles(c.tiles) }; } return { schema: SCHEMA, date: 'probe', inputs: inp, ...(states ? { states } : {}), checks }; };
  const S0 = summ(jog([51], 0.05)), B = entryOf(S0, inputs);
  const verdict = opts => ratchet(summ(opts), B, R, { inputs });
  probe('R1 the same data passes', verdict(jog([51], 0.05)).ok);
  probe('R2 a worse peak (6 cm) fails', !verdict(jog([51], 0.06)).ok);
  probe('R3 the same jog held over s 49-52 (longer inside its buckets) fails', !verdict(jog([49, 50, 51, 52], 0.05)).ok);
  probe('R4 held over s 48-52 fails', !verdict(jog([48, 49, 50, 51, 52], 0.05)).ok);
  probe('R5 a second run in the same bucket fails', !verdict(jog([51, 49], 0.05)).ok);
  probe('R6 moved to s 56 fails', !verdict(jog([56], 0.05)).ok);
  // STALE (review 3: it used to be information only, so the city audit went quiet exactly when a re-export landed)
  const st = ratchet(S0, B, R, { inputs: { ...inputs, sections: { EDGE: 'a', SPAN: 'b' } } });
  probe('R7 other inputs (a new SPAN section) are STALE, and STALE FAILS - even on the very same runs', !st.ok && !!st.stale && st.stale.some(s => s.includes('SPAN')) && st.lines.some(l => l.includes('FAIL baseline STALE')), st.stale && st.stale.join('; '));
  const dm = ratchet(S0, entryOf(S0, inputs, { ...statesOf(R), B2: 'ZERO' }), R, { inputs });
  probe('R8 a check gating looser than when its baseline was recorded (B2 ZERO -> RATCHET) FAILS on the same data', !dm.ok && !!dm.demoted && dm.demoted.some(d => d.startsWith('B2 ZERO -> RATCHET')), dm.demoted && dm.demoted.join('; '));
  const ns = ratchet(S0, entryOf(S0, inputs, null), R, { inputs });
  probe('R9 a baseline that records no check states FAILS (re-record it)', !ns.ok && !!ns.demoted, ns.demoted && ns.demoted.join('; '));
  const ba = beforeAfter(summ(jog([51, 56], 0.06)), B, R, { ...inputs, sections: { EDGE: 'a', SPAN: 'b' } });
  probe('R10 the explicit re-record prints BEFORE -> AFTER for every check and what moved', ba.length === R.Checks.length + 2 && ba[0].includes('container section SPAN') && ba.some(l => /^  B1 JITTER .*->/.test(l)), ba[0]);
}

console.log(`\n${passed} probes passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
