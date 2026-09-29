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
//       came faster than the plan's ease - review 5: over a quarter of the cap
//       or less by the whole step, as round 3 scored it (the ease there had
//       dropped 763 of the city's keys); a sub-V one, or a slow drift, does not
//   Z   a BUMP (off a straight and back) and a ZIGZAG (review 4: where the net
//       turn is 0 nothing measured the excursion): peak deviation past V fails;
//       a zigzag of under V about the straight does not
//   W   a WAVE (review 5): a smooth sampled wave, a zigzag with split or flat
//       peaks, a data zigzag WP-11 filleted into tangent arcs - lobes turning
//       back on both sides, off their mean line past V - fails; a sub-V one, a
//       long gentle meander, a winding road's lobes (SimplifyEpsM or more) do not
//   M   a jog that runs into a FAN MOUTH closer than its own width (review 5,
//       Baxter Street): the exit straight is the strand's last segment
//   B   one LEGAL vertex (under the lone limit) within 10 m of a jog or bump
//       (review 5, North Irwin Avenue): the line still runs straight there
//   N   bumps and notches WIDER than the 10 m chord cap (review 5, East 12th
//       Street): judged between straights up to KinkViewM, by the part faster
//       than the plan's ease - an eased bump, the plan's own, passes
//   P   a ribbon edge pulled in WITHOUT a squeeze (review 5): judged against the
//       design edge (A1) and by A2/A3, never against a squeeze envelope; the
//       mesh gate's tap flags the squeeze from its cause (source contract)
//   E   C2's closed list: a line starting along a branch's attach arc is no
//       legitimate end; at a gore NOSE (a collapsed section) it is
//   Q   a SQUEEZED ribbon edge against its I7 envelope: a squeeze that arrives
//       faster than a taper over the class floor, steps, or wanders back out
//       fails A1 - also inside a taller, slow rise (review 4: the envelope
//       took the whole rise's height); one eased over the floor or longer, or
//       several stacked, does not
//   C   ON A CURVE (review 6: every rule measured against a straight, so on
//       an arc bumps, jogs, kinks, waves and squeeze zigzags passed at 4-10
//       times the straight-road limit): judged in the ARC'S FRAME (lib/kink.mjs
//       arcFrame) they fail as on a straight; clean arcs, WP-11 fillets, sub-V
//       features, eased tapers and long S-bends on a curve do not; review 7:
//       the offline rules parser reads a const float as C# holds it (the arc
//       frame's discrete tests flipped between the gates on V as a double)
//   L   NAMES (the first Unity run of the mesh gate): a run is named after the
//       line its own samples lie on, a jump or a dash at the end on the lower
//       way, and a centre line has no sign - so one run has one key whichever
//       way a chain runs (the two gates orient chains differently)
//   T   a HOOK (review 6: a corner in a line's last metres passed until its
//       end stood 8V off): the corner extrapolated from the approach, at most
//       the end's own offset, fails; a turn whose end stands under V off, or a
//       legal vertex a few metres before the end, does not
//   R   the ratchet: growth inside a key's bucket, a second run, a move, a
//       worse peak all FAIL; the same data PASSES; other inputs are STALE,
//       and STALE FAILS; a check gating looser than its baseline FAILS -
//       review 5: the gate's own code is an input, a way dropped from the pin
//       fails, a re-record that loosens (keys vanished or lower, the data
//       unmoved) is refused, a ribbon edge's key takes its bucket's side -
//       review 6: the gate moving TOGETHER with the data is refused (two
//       steps), the builder replica is data (a builder fix records freely),
//       and the mesh gate's Loosenings keeps the same contract
//
//   node tools/city/gateprobes.mjs [K|A|J|F|D|S|H|G|Z|W|M|B|N|E|Q|P|C|T|R ...]    exit 1 on any failed probe
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readSmoothRules } from './lib/smoothrules.mjs';
import { loadPaintLayouts } from './lib/paintruns.mjs';
import { city, bent, fillet, sagChord, gateOf, fwd, DEG } from './lib/synthcity.mjs';
import { arcFrame, kinkScores } from './lib/kink.mjs';
import { summarize, ratchet, packKeys, packTiles, SCHEMA, statesOf, beforeAfter } from './lib/gatebase.mjs';
import * as GB from './lib/gatebase.mjs';
import { readFileSync } from 'node:fs';

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
  for (const [t, g, why] of [[0.17, 10, 'a slow drift: 0.44 cm faster than the ease']]) {
    const res = gateOf(tw2(bent([t, -t], [g])), R, layouts);
    probe(`G2 [+${t}, -${t}] deg ${g} m apart: a ${(g * Math.sin(t * DEG) * 100).toFixed(1)} cm step, ${why}: nothing`, gated(res).length === 0, fmt(res));
  }
  // review 5: over a quarter of the cap or less a step is scored whole, as round 3 did (the ease there - 6-25% of the step -
  // dropped 763 of the city's keys when the baseline was re-recorded)
  for (const [t, g] of [[2.5, 0.6], [1.2, 1.3], [0.7, 2.5]]) {
    const res = gateOf(tw2(bent([t, -t], [g])), R, layouts);
    const jl = new Set(gated(res).filter(r => r.check === 'B2' && /jog/.test(r.what || '')).map(r => r.lineId));
    probe(`G6 [+${t}, -${t}] deg ${g} m apart: a ${(g * Math.sin(t * DEG) * 100).toFixed(1)} cm step over ${g} m (a quarter of the cap or less): the whole step, a jog on all 7 lines`, jl.size === 7, fmt(res));
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
  // centred on the straight (half legs at its ends): +-1.7 cm about the line the eye expects
  const czig = (t, g, legs) => { const pts = zig(t, g, legs), turns = [t / 2]; for (let i = 1; i < legs; i++) turns.push(i % 2 ? -t : t); turns.push(legs % 2 ? -t / 2 : t / 2);
    const gaps = turns.slice(1).map(() => g); gaps[0] = g / 2; gaps[gaps.length - 1] = g / 2; return pts && bent(turns, gaps); };
  for (const [label, pts] of [['Z3 zigzag +-2 deg every 2 m, centred on the straight (+-1.7 cm)', czig(2, 2, 8)], ['Z3 zigzag +-1 deg every 4 m, centred (+-1.7 cm)', czig(1, 4, 6)],
    ['Z3 zigzag +-1 deg every 4 m touching the straight at every trough (3.5 cm peaks, over 4 m legs: slower than the ease)', zig(1, 4, 6)],
    ['Z3 bump [0.5, -1, 0.5] deg 2 m apart (1.7 cm)', bent([0.5, -1, 0.5], [2, 2])], ['Z3 bump [0.3, -0.6, 0.3] deg 4 m apart (2.1 cm over 8 m)', bent([0.3, -0.6, 0.3], [4, 4])]]) {
    const res = gateOf(tw2(pts), R, layouts);
    probe(`${label}, under V: nothing`, gated(res).length === 0, fmt(res));
  }
  // one-sided (touching the straight at every trough), a row of 3.5 cm bumps each faster than the ease: as its single bump
  // (Z1 [1, -2, 1] 2 m apart) fails
  {
    const res = gateOf(tw2(zig(2, 2, 8)), R, layouts);
    probe('Z4 zigzag +-2 deg every 2 m touching the straight at every trough (a row of 3.5 cm bumps over 2 m legs): a KINK on all 7 lines', b2lines(res) === 7, fmt(res));
  }
}

// ---- W: waves (review 5): a line swinging to and fro with no sharp peak, no straights and no cluster passed everything
if (want('W')) {
  console.log('W  B2 KINK: a WAVE - lobes turning back on both sides - off its mean line past V (spec section 2: symmetric zigzag, peak deviation <= V)');
  const waveLines = res => new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId)).size;
  const hasWave = res => gated(res).some(r => r.check === 'B2' && /wave/.test(r.what || ''));
  const h0 = 20 * DEG, ux = Math.sin(h0), uz = Math.cos(h0), nx = uz, nz = -ux;
  const wavy = (A, lam, cyc, step) => { const pts = [[0, 0]], L = lam * cyc; for (let x = 0; x <= L + 1e-9; x += step) { const y = A * Math.sin(2 * Math.PI * x / lam); pts.push([ux * (60 + x) + nx * y, uz * (60 + x) + nz * y]); } pts.push([ux * (120 + L), uz * (120 + L)]); return pts; };
  for (const [A, lam, step, w] of [[0.08, 10, 0.5], [0.15, 16, 1], [0.2, 24, 1], [0.08, 10, 0.5, { rank: 3, lanes: 4 }], [0.12, 16, 1, { rank: 3, lanes: 4 }]]) {
    const res = gateOf(city([{ pts: wavy(A, lam, 3, step), ...(w || {}) }]), R, layouts), lines = res.S.E[0].profile.key === 'tw2' ? 7 : 9;
    probe(`W1 a sampled wave +-${A * 100} cm every ${lam} m (samples ${step} m) on ${res.S.E[0].profile.key}: a wave on all ${lines} lines`, waveLines(res) === lines && hasWave(res), fmt(res));
  }
  const split = (a, g, sp) => { const turns = [a / 2], gaps = []; for (let i = 0; i < 8; i++) { const sg = i % 2 ? 1 : -1; turns.push(sg * a / 2, sg * a / 2); gaps.push(g - sp, sp); } turns.push(a / 2); gaps.push(g); return bent(turns, gaps); };
  for (const [a, g, sp] of [[3, 4, 1], [2, 5, 1]]) {
    const res = gateOf(tw2(split(a, g, sp)), R, layouts);
    probe(`W2 a zigzag of +-${a} deg turns every ${g} m, each peak split in two ${sp} m apart: a KINK on all 7 lines`, waveLines(res) === 7, fmt(res));
  }
  {
    const turns = []; for (let i = 0; i < 8; i++) turns.push(...(i % 2 === 0 ? [2, -2] : [-2, 2]));
    const res = gateOf(tw2(bent(turns, turns.map(() => 4).slice(1))), R, layouts);
    probe('W3 a flat-topped wave [2, -2, -2, 2, ...] deg every 4 m (14 cm peak to peak): a wave on all 7 lines', waveLines(res) === 7 && hasWave(res), fmt(res));
  }
  // what WP-11 makes of a data zigzag: tangent arcs meeting at the legs' midpoints, densified at the 2 cm sagitta
  const arcChain = arcs => { const pts = [[0, 0]]; let h = 10 * DEG, p = fwd([0, 0], h, 60); pts.push(p);
    for (const { R: R0, turn } of arcs) { const D = Math.abs(turn) * DEG, sg = Math.sign(turn), c = sagChord(R0, hw2), n = Math.max(1, Math.ceil(R0 * D / c)), dh = D / n, ch = 2 * R0 * Math.sin(dh / 2);
      for (let k = 0; k < n; k++) { h += sg * (k === 0 ? dh / 2 : dh); p = fwd(p, h, ch); pts.push(p); } h += sg * dh / 2; }
    pts.push(fwd(p, h, 60)); return pts; };
  const filleted = (a, g) => { const R0 = (g / 2) / Math.tan(a / 2 * DEG), arcs = [{ R: R0, turn: a / 2 }]; for (let i = 0; i < 8; i++) arcs.push({ R: R0, turn: i % 2 === 0 ? -a : a }); arcs.push({ R: R0, turn: -a / 2 }); return arcChain(arcs); };
  for (const [a, g] of [[10, 5], [12, 6]]) {
    const res = gateOf(tw2(filleted(a, g)), R, layouts);
    probe(`W4 a data zigzag of +-${a} deg every ${g} m WP-11 filleted into R ${((g / 2) / Math.tan(a / 2 * DEG)).toFixed(1)} m tangent arcs: a wave on all 7 lines`, waveLines(res) === 7 && hasWave(res), fmt(res));
  }
  for (const [label, pts] of [['W5 a wave of +-2 cm every 10 m (under V)', wavy(0.02, 10, 3, 0.5)], ['W5 a meander of +-10 cm every 80 m (1 m samples: its 10 m chords sag under a cm)', wavy(0.1, 80, 2, 1)],
    ['W5 a winding road: R 60 m arcs turning +-30 deg (lobes of 1.2 m: geometry, SimplifyEpsM or more)', (() => { const arcs = [{ R: 60, turn: 15 }]; for (let i = 0; i < 6; i++) arcs.push({ R: 60, turn: i % 2 === 0 ? -30 : 30 }); arcs.push({ R: 60, turn: -15 }); return arcChain(arcs); })()],
    ['W5 the same zigzag filleted at +-30 deg every 10 m (R 18.7 m: lobes of 0.63 m, geometry)', filleted(30, 10)]]) {
    const res = gateOf(tw2(pts), R, layouts);
    probe(`${label}: no wave`, !hasWave(res) && count(res, 'B2') === 0, fmt(res));
  }
}

// ---- M: a jog into a fan mouth (review 5, Baxter Street: 12.2 cm over 2.7 m, 0.79 m before the mouth, passed both gates)
if (want('M')) {
  console.log('M  B2 KINK: a jog that ends closer to a fan mouth than its own width - the exit straight is the last segment');
  const T = (step, w, d) => {
    const probeCity = gateOf(city([{ pts: [[-80, 0], [0, 0]] }, { pts: [[0, 0], [80, 0]] }, { pts: [[0, 80], [0, 0]] }]), R, layouts);
    const trim = probeCity.S.E[2].length - probeCity.S.E[2].secs.at(-1).s, zEnd = trim + d, zStart = zEnd + w;
    return gateOf(city([{ pts: [[step - 80, 0], [step, 0]] }, { pts: [[step, 0], [step + 80, 0]] }, { pts: [[0, 80], [0, zStart], [step, zEnd], [step, 0]] }]), R, layouts);
  };
  const side = res => gated(res).filter(r => r.e === 2);
  for (const [step, w, d] of [[0.105, 3, 2], [0.105, 3, 1], [0.14, 4, 2.5], [0.14, 4, 1]]) {
    const res = T(step, w, d), jl = new Set(side(res).filter(r => r.check === 'B2' && /jog/.test(r.what || '')).map(r => r.lineId));
    probe(`M1 a ${(step * 100).toFixed(1)} cm jog over ${w} m ending ${d} m before a T junction's fan mouth: a jog on all 7 lines of the side street`, jl.size === 7, `${jl.size} lines; ${side(res).map(r => r.check).join(' ') || 'nothing on the side street'}`);
  }
  const res = T(0, 3, 1);
  probe('M2 the same side street with no jog: nothing on it', side(res).length === 0, side(res).map(r => `${r.check} ${r.lineId}`).join(', ') || 'nothing');
}

// ---- B: one legal vertex beside a jog or bump (review 5: 'no turn of 25%' made a 0.5 deg vertex switch the rules off)
if (want('B')) {
  console.log('B  B2 KINK: a jog or bump with one legal vertex (under the lone limit) within 10 m - the line still runs straight there');
  const b2l = res => new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId)).size;
  for (const [label, t, g] of [['jog [1.5, -1.5] over 4 m (10.5 cm)', [1.5, -1.5], [4]], ['jog [2, -2] over 3 m (10.5 cm)', [2, -2], [3]], ['bump [2, -2, -2, 2] 2/1/2 m (7 cm)', [2, -2, -2, 2], [2, 1, 2]]])
    for (const [b, d, where] of [[0.5, 9.5, 'before'], [0.7, 6, 'before'], [1, 8, 'after'], [1, 5, 'before']]) {
      const pts = where === 'before' ? bent([b, ...t], [d, ...g]) : bent([...t, b], [...g, d]);
      const res = gateOf(tw2(pts), R, layouts);
      probe(`B1 ${label} with a ${b} deg vertex ${d} m ${where} it: a KINK on all 7 lines`, b2l(res) === 7, fmt(res));
    }
  const res = gateOf(tw2(bent([1], [])), R, layouts);
  probe('B2 the 1 deg vertex alone: nothing', gated(res).length === 0, fmt(res));
}

// ---- N: wider than the chord cap (review 5: every window rule stopped at 10 m, so a bump just wider passed)
if (want('N')) {
  console.log('N  B2 KINK: bumps and notches wider than ChordCapM, between straights, up to KinkViewM (the part faster than the ease)');
  const b2l = res => new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId)).size;
  const h0 = 20 * DEG, ux = Math.sin(h0), uz = Math.cos(h0), nx = uz, nz = -ux;
  const shape = (f, W, step = 1) => { const pts = [[0, 0]]; for (let x = 0; x <= W + 1e-9; x += step) { const y = f(x); pts.push([ux * (60 + x) + nx * y, uz * (60 + x) + nz * y]); } pts.push([ux * (120 + W), uz * (120 + W)]); return pts; };
  for (const [t, g1, g2] of [[2, 4, 3], [2, 4, 6], [2, 4, 9], [2, 5, 4], [2.2, 5, 5]]) {
    const res = gateOf(tw2(bent([t, -t, -t, t], [g1, g2, g1])), R, layouts);
    probe(`N1 a trapezoid bump [${t}, -${t}, -${t}, ${t}] deg, rises of ${g1} m, a ${g2} m top (${2 * g1 + g2} m): a KINK on all 7 lines`, b2l(res) === 7, fmt(res));
  }
  for (const [A, W] of [[0.1, 12], [0.2, 16], [0.15, 20]]) {
    const res = gateOf(tw2(shape(x => A * 0.5 * (1 - Math.cos(2 * Math.PI * x / W)), W)), R, layouts);
    probe(`N2 a raised-cosine bump of ${A * 100} cm over ${W} m (1 m samples): a KINK on all 7 lines`, b2l(res) === 7, fmt(res));
  }
  for (const [g1, g2] of [[6.5, 4.1], [8, 4.1]]) {
    const res = gateOf(tw2(bent([-1.2, 2.35, 1.1], [g1, g2])), R, layouts);
    probe(`N3 a notch [-1.2, 2.35, 1.1] deg ${g1} / ${g2} m apart (East 12th Street's shape, ${g1 + g2} m): a KINK on all 7 lines`, b2l(res) === 7, fmt(res));
  }
  const ss = u => { u = Math.max(0, Math.min(1, u)); return u * u * (3 - 2 * u); };
  for (const [H, L] of [[0.3, 15], [0.3, 20], [0.1, 15]]) {
    const res = gateOf(tw2(shape(x => H * (x <= L ? ss(x / L) : 1 - ss((x - L) / L)), 2 * L)), R, layouts);
    probe(`N4 an eased bump, ${H * 100} cm out over ${L} m and back over ${L} m (the plan's ease): no KINK`, count(res, 'B2') === 0, fmt(res));
  }
}

// ---- P: a ribbon edge pulled in without a squeeze (review 5: the mesh tap's flag was the symptom, so any edge standing
// inside its half width was judged only against its own envelope, and A2 / A3 skipped it)
if (want('P')) {
  console.log('P  a ribbon edge pulled in WITHOUT a squeeze: A1 against the design edge, A2, A3 - never the squeeze envelope');
  const ss = t => { t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t); };
  const pts = []; for (let i = 0; i <= 120; i++) pts.push(fwd([0, 0], 20 * DEG, i));
  const pulled = (cut, sq) => gateOf(city([{ pts }]), R, layouts, { tweakSecs: S => S.E[0].secs.forEach(x => {
    const d = cut(x.s); if (!(d > 0)) return;
    x.L = [x.L[0] + x.right[0] * d, x.L[1] + x.right[1] * d]; x.latL += d; x.uL = 0.5 - x.latL / S.E[0].width; if (sq) x.sqL = true; }) });
  for (const [label, cut] of [['0.3 m in over 16 m and held', s => 0.3 * ss((s - 30) / 16)], ['0.5 m in over 20 m, held 30 m, back out', s => 0.5 * ss((s - 20) / 20) * (1 - ss((s - 70) / 20))]]) {
    const res = pulled(cut, false);
    const a1 = gated(res).filter(r => r.check === 'A1' && r.lineId === 'RL'), a2 = count(res, 'A2'), a3 = count(res, 'A3');
    probe(`P1 the L edge ${label}, no squeeze (sqL unset, as the tap now flags it): A1 against the design edge, A2 and A3`, a1.length > 0 && !a1.some(r => /I7/.test(r.what || '')) && a2 > 0 && a3 > 0,
      `A1 ${a1.length} (x${Math.max(0, ...a1.map(r => r.ratio)).toFixed(1)}), A2 ${a2}, A3 ${a3}`);
  }
  // the mesh gate: CityMeshes.TapFlags must flag the squeeze from its CAUSE (the section's sqL / sqR, set by SqueezeSection),
  // never from where the drawn edge stands; CitySmooth reads that flag as the squeeze, the clip flags as X3
  const meshes = readFileSync(join(UNITY, 'Assets/PSXRacing/Scripts/City/CityMeshes.cs'), 'utf8'), smooth = readFileSync(join(UNITY, 'Assets/PSXRacing/Editor/CitySmooth.cs'), 'utf8');
  const tf = (meshes.match(/static ushort TapFlags\([^)]*\)\s*\{[\s\S]*?\n        \}/) || [''])[0];
  const sqSet = /SqueezeSection[\s\S]*?sec\.sqR = true[\s\S]*?sec\.sqL = true/.test(meshes);
  const rib = (smooth.match(/static Pt RibPt\([^)]*\)\s*\{[\s\S]*?\n        \}/) || [''])[0];
  probe('P2 the mesh tap flags the squeeze from its cause (TapFlags reads the section\'s sqL / sqR, which SqueezeSection sets; no drawn position), and CitySmooth reads it as the squeeze',
    /c\.sqL/.test(tf) && /c\.sqR/.test(tf) && !/latL|PointAt/.test(tf) && sqSet && /FSqueezedL/.test(rib), tf ? 'TapFlags: ' + tf.split('\n').slice(1, 3).join(' ').replace(/\s+/g, ' ').slice(0, 160) : 'no TapFlags found');
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

// ---- C: on a curve (review 6): every excursion rule needed a straight reference, so on an arc they had none
if (want('C')) {
  console.log("C  B2 KINK on a curve: bumps, jogs, kinks, waves and squeeze zigzags judged in the ARC'S FRAME, as on a straight");
  const gt = pts => gateOf(tw2(pts), R, layouts);
  const b2l = res => new Set(gated(res).filter(r => r.check === 'B2').map(r => r.lineId)).size;
  const arcHit = res => gated(res).some(r => r.check === 'B2' && /on a curve/.test(r.what || ''));
  // a road along an arc of radius Rc (Infinity: straight) from heading 20 deg, its centreline displaced sideways by y(s),
  // sampled at the arc lengths ss (sorted), with 60 m of the arc sampled every arcStep m before and after them
  const h0 = 20 * DEG;
  const arcPts = (ss, y, Rc, arcStep = 2) => {
    const k = Number.isFinite(Rc) ? 1 / Rc : 0, all = [...new Set([...ss, ...Array.from({ length: Math.round((ss.at(-1) - ss[0] + 120) / arcStep) + 1 }, (_, i) => ss[0] - 60 + i * arcStep)].map(v => +v.toFixed(6)))].sort((a, b) => a - b);
    return all.map(s => { const th = h0 + k * s, bx = k ? (Math.cos(h0) - Math.cos(th)) / k : Math.sin(h0) * s, bz = k ? (Math.sin(th) - Math.sin(h0)) / k : Math.cos(h0) * s, d = y(s); return [bx + Math.cos(th) * d, bz - Math.sin(th) * d]; });
  };
  const grid = (a, b, st) => { const o = []; for (let x = a; x <= b + 1e-9; x += st) o.push(x); return o; };
  const rc = (h, W) => s => (s >= 0 && s <= W ? h * (1 - Math.cos(2 * Math.PI * s / W)) / 2 : 0);
  const step = (h, W) => s => (s <= 0 ? 0 : s >= W ? h : h * (1 - Math.cos(Math.PI * s / W)) / 2);
  const wave = (A, lam) => s => (s >= 0 && s <= 3 * lam ? A * Math.sin(2 * Math.PI * s / lam) : 0);
  for (const [label, y, W, Rc] of [['a raised-cosine bump of 20 cm over 8 m', rc(0.2, 8), 8, 300], ['the same bump', rc(0.2, 8), 8, 100], ['a bump of 12 cm over 12 m', rc(0.12, 12), 12, 300],
    ['a smooth jog of 20 cm over 4 m', step(0.2, 4), 4, 300], ['a wave of +-8 cm every 16 m (3 cycles)', wave(0.08, 16), 48, 100], ['a wave of +-15 cm every 24 m', wave(0.15, 24), 72, 50]]) {
    const res = gt(arcPts(grid(0, W, 1), y, Rc));
    probe(`C1 ${label} (1 m samples) on an arc of R ${Rc} m: a KINK on all 7 lines, in the frame of the arc`, b2l(res) === 7 && arcHit(res), fmt(res));
  }
  // a lone kink AGAINST the curve: the arc's own turns stopped its chord within a few metres
  {
    const arcKink = (Rc, st, t) => { const pts = [[0, 0]]; let h = 20 * DEG, p = fwd([0, 0], h, 60); pts.push(p); const n = Math.round(80 / st), dh = st / Rc;
      for (let k = 0; k < n; k++) { h += (k === 0 ? dh / 2 : dh) + (k === Math.floor(n / 2) ? t * DEG : 0); p = fwd(p, h, st); pts.push(p); } h += dh / 2; pts.push(fwd(p, h, 60)); return pts; };
    const res = gt(arcKink(100, 1, -3));
    probe('C2 a lone 3 deg kink against an arc of R 100 m (vertices every 1 m): a KINK on all 7 lines', b2l(res) === 7, fmt(res));
    const ok = gt(arcKink(100, 1, -1));
    probe('C2 a lone 1 deg kink against the same arc (under the lone limit): nothing', gated(ok).length === 0, fmt(ok));
  }
  // sharp two- and three-vertex data features on an arc drawn every 2 m (the drawn line is the data line)
  {
    const arcData = (Rc, st, feat) => { const ss = new Set(); for (let s = 0; s <= 80 + 1e-9; s += st) ss.add(+s.toFixed(6)); for (const [o] of feat) ss.add(40 + o);
      const lat = s => { const o = s - 40; if (o <= feat[0][0]) return 0; if (o >= feat.at(-1)[0]) return feat.at(-1)[1]; for (let i = 1; i < feat.length; i++) if (o <= feat[i][0]) return feat[i - 1][1] + (feat[i][1] - feat[i - 1][1]) * (o - feat[i - 1][0]) / (feat[i][0] - feat[i - 1][0]); };
      return arcPts([...ss].sort((a, b) => a - b), lat, Rc, st); };
    for (const [label, Rc, feat] of [['a two-vertex data jog of 8 cm over 2 m', 300, [[0, 0], [2, 0.08]]], ['a tent bump of 10 cm over 8 m', 100, [[0, 0], [4, 0.1], [8, 0]]]]) {
      const res = gt(arcData(Rc, 2, feat));
      probe(`C3 ${label} on an arc of R ${Rc} m drawn every 2 m: a KINK on all 7 lines`, b2l(res) === 7, fmt(res));
    }
  }
  // the squeeze zigzag (review 4's S7, rises longer than the floor: only the wave rule sees it) on a curving road
  {
    const sq = (Rc, cut) => { const pts = [], k = 1 / Rc; for (let i = 0; i <= 400; i++) { const s = i * 0.5, th = h0 + k * s; pts.push([(Math.cos(h0) - Math.cos(th)) / k, (Math.sin(th) - Math.sin(h0)) / k]); }
      return gateOf(city([{ pts }]), R, layouts, { tweakSecs: S => S.E[0].secs.forEach(x => { const d = cut(x.s); if (!(d > 0)) return; x.L = [x.L[0] + x.right[0] * d, x.L[1] + x.right[1] * d]; x.latL += d; x.uL = 0.5 - x.latL / S.E[0].width; x.sqL = true; }) }); };
    const S7 = s => (s < 20 ? 0 : 0.1 * (1 - Math.cos(2 * Math.PI * (s - 20) / 32)));
    for (const Rc of [300, 100]) { const res = sq(Rc, S7); probe(`C4 the L ribbon edge squeezed 0.2 m in and out every 32 m on a road curving at R ${Rc} m: a wave on the edge`, gated(res).some(r => r.check === 'B2' && r.lineId === 'RL' && /wave/.test(r.what || '')), fmt(res)); }
    // eased over 20 m (over the 15 m floor itself the smoothstep's curvature step at each end reads as a split corner, on a
    // straight as on a curve: a calibration question, not the frame's)
    const eased = sq(300, s => { const u = Math.max(0, Math.min(1, (s - 40) / 20)); return u * u * (3 - 2 * u); });
    probe('C4 the same edge squeezed 1 m in as a smoothstep over 20 m on R 300 m (an eased taper): no B2, no A1 against its envelope', count(eased, 'B2') === 0 && !gated(eased).some(r => r.check === 'A1' && /I7/.test(r.what || '')), fmt(eased));
    const ss = u => { u = Math.max(0, Math.min(1, u)); return u * u * (3 - 2 * u); };
    const bumpE = gt(arcPts(grid(0, 30, 1), s => 0.3 * (s <= 15 ? ss(s / 15) : 1 - ss((s - 15) / 15)), 100));
    probe("C4 an eased bump, 30 cm out over 15 m and back over 15 m (the plan's ease), on R 100 m: nothing", gated(bumpE).length === 0, fmt(bumpE));
  }
  // WP-11: a data curve drawn every 8 m, filleted with tangent arcs at the 2 cm sagitta - with a 25 cm spike, and without
  {
    const polyArc = (Rc, L, n, at, d) => { const pts = []; for (let i = 0; i <= n; i++) { const s = i * L, th = h0 + s / Rc; let x = (Math.cos(h0) - Math.cos(th)) * Rc, z = (Math.sin(th) - Math.sin(h0)) * Rc; if (i === at) { x += Math.cos(th) * d; z -= Math.sin(th) * d; } pts.push([x, z]); } return pts; };
    const filletAll = P => { const out = [P[0]]; for (let i = 1; i + 1 < P.length; i++) { const a = P[i - 1], b = P[i], c = P[i + 1], u = [b[0] - a[0], b[1] - a[1]], v = [c[0] - b[0], c[1] - b[1]], lu = Math.hypot(...u), lv = Math.hypot(...v);
      const th = Math.atan2(u[0] * v[1] - u[1] * v[0], u[0] * v[0] + u[1] * v[1]); if (Math.abs(th) < 1e-6) { out.push(b); continue; }
      const t = Math.min(lu, lv) / 2, Rr = t / Math.tan(Math.abs(th) / 2), nS = Math.max(1, Math.ceil(Rr * Math.abs(th) / sagChord(Rr, hw2))), dh = th / nS, c0 = 2 * Rr * Math.sin(Math.abs(dh) / 2);
      let h = Math.atan2(u[1], u[0]), q = [b[0] - u[0] / lu * t, b[1] - u[1] / lu * t]; out.push(q); for (let k = 0; k < nS; k++) { h += k === 0 ? dh / 2 : dh; q = [q[0] + Math.cos(h) * c0, q[1] + Math.sin(h) * c0]; out.push(q); } }
      out.push(P.at(-1)); return out; };
    for (const [Rc, L] of [[300, 8], [100, 6], [60, 5]]) { const n = Math.round(120 / L) + 10, res = gt(filletAll(polyArc(Rc, L, n, -1, 0))); probe(`C5 an R ${Rc} m data curve drawn every ${L} m, WP-11 filleted (tangent arcs, 2 cm sagitta): nothing`, gated(res).length === 0, fmt(res)); }
    const n = Math.round(120 / 8) + 10, spiked = gt(filletAll(polyArc(300, 8, n, Math.round(n / 2), 0.25)));
    probe('C5 the R 300 m curve with one data vertex 25 cm out, WP-11 filleted (19 cm off the clean fillet): a KINK on all 7 lines', b2l(spiked) === 7, fmt(spiked));
  }
  // the first Unity run: the two gates named one run two ways. A strand's runs took the line id of the identity
  // chain's FIRST piece (a tw4's yellow +0.12 named a tw5t's run +1.95), a jump or a dash across a joint was reported
  // at whichever end the chain reached second, and a lane line at the centre was -0.00 in C# and +0.00 here. The two
  // gates start chains at different edges, so every such run had two keys. Here: one city, its two ways given in both
  // orders (the chain then runs one way and then the other), must give the same keys.
  {
    const A = []; { let p = [0, 0], h = 10 * DEG; A.push(p); p = fwd(p, h, 60); A.push(p); h += 3 * DEG; p = fwd(p, h, 40); A.push(p); }
    const B = []; { let p = A.at(-1), h = 13 * DEG; B.push(p); p = fwd(p, h, 40); B.push(p); h -= 3 * DEG; p = fwd(p, h, 60); B.push(p); }
    const wA = { pts: A, lanes: 4, rank: 2, wayId: 7001, name: 'L1 A' }, wB = { pts: [...B].reverse(), lanes: 5, turn: true, rank: 2, wayId: 7002, name: 'L1 B' };
    const keysOf = res => res.g.runs.filter(r => !r.drop).flatMap(r => (r.kk || []).map((k, i) => `${k}@${(r.kq[i] || 0).toFixed(3)}`)).sort();
    const k1 = keysOf(gateOf(city([wA, wB]), R, layouts)), k2 = keysOf(gateOf(city([wB, wA]), R, layouts));
    const only1 = k1.filter(k => !k2.includes(k)), only2 = k2.filter(k => !k1.includes(k));
    const foreign = k1.filter(k => (/^7001:.*:(CYs|CYd)[-+]1\.(95|71)/.test(k)) || (/^7002:.*:CYs[-+]0\.12/.test(k)));
    const b4 = k1.filter(k => /:B4:/.test(k)).length, b2 = k1.filter(k => /:B2:/.test(k)).length;
    probe('L1 one city, its two ways (tw4 then tw5t, the second drawn against the first) given in both orders: the same keys - every run named after its own line, a jump and a dash at the end on the lower way',
      k1.length > 0 && b4 > 0 && b2 > 0 && only1.length === 0 && only2.length === 0 && foreign.length === 0,
      `${k1.length} keys (${b2} B2, ${b4} B4); only in the first order ${only1.length}${only1.length ? ' e.g. ' + only1[0] : ''}; only in the second ${only2.length}${only2.length ? ' e.g. ' + only2[0] : ''}; a tw5t line named on the tw4 or the reverse ${foreign.length}`);
    const ow2 = gateOf(city([{ pts: bent([3], [30]), lanes: 2, oneway: true, rank: 2, wayId: 7003 }]), R, layouts).g.runs;
    const minus0 = ow2.filter(r => /[-]0\.00$/.test(r.lineId) || (r.kk || []).some(k => /[-]0\.00$/.test(k)));
    probe('L1 a one-way two-lane street: its centre lane line is CWd+0.00, never -0.00 (the sign of a float\'s noise)',
      ow2.some(r => /CWd\+0\.00$/.test(r.lineId)) && minus0.length === 0, `${ow2.filter(r => /CWd/.test(r.lineId)).length} runs on its lane line; -0.00 on ${minus0.length}`);
  }
  // B2 read both ways: the rules walk a line from one end and decide greedily (a cluster's first member, a straight's
  // start, a lobe's inflections); before, 2.6% of the vertices past V scored differently read backwards (0.27 cm one
  // way, 10.9 cm the other). 400 random lines of jogs, bumps, corners and short legs, and each backwards: the same
  {
    let seed = 5; const rnd = () => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed / 0x7fffffff; };
    const arrs = P => { const n = P.length, X = P.map(q => q[0]), Z = P.map(q => q[1]), C = [0], TH = new Array(n).fill(0);
      for (let i = 1; i < n; i++) C.push(C[i - 1] + Math.hypot(X[i] - X[i - 1], Z[i] - Z[i - 1]));
      for (let i = 1; i + 1 < n; i++) { const ux = X[i] - X[i - 1], uz = Z[i] - Z[i - 1], vx = X[i + 1] - X[i], vz = Z[i + 1] - Z[i]; TH[i] = Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz); }
      return [X, Z, C, TH]; };
    let past = 0, differ = 0, worst = 0;
    for (let c = 0; c < 400; c++) {
      const P = [[0, 0]]; let h = rnd() * 6, q = [0, 0];
      const n = 5 + Math.floor(rnd() * 25);
      for (let i = 0; i < n; i++) { const L = rnd() < 0.3 ? 0.3 + rnd() : 1 + rnd() * 15; if (rnd() < 0.5) h += (rnd() - 0.5) * 8 * DEG; q = [q[0] + Math.sin(h) * L, q[1] + Math.cos(h) * L]; P.push(q); }
      const a = kinkScores(...arrs(P), R), b = kinkScores(...arrs([...P].reverse()), R);
      for (let m = 1; m + 1 < P.length; m++) { const x = a[m], y = b[P.length - 1 - m]; if (x > R.V || y > R.V) past++; const d = Math.abs(x - y) / Math.max(1e-9, x, y); if ((x > R.V || y > R.V) && d > 1e-9) { differ++; worst = Math.max(worst, d); } }
    }
    probe('L2 400 random lines of corners, jogs, bumps and short legs, each read backwards too: every vertex scores the same B2 either way (the worse of the two walks)',
      past > 100 && differ === 0, `${past} vertices past V, ${differ} scoring differently backwards (worst ${(worst * 100).toFixed(1)}%)`);
  }
  // review 7: SmoothRules' numbers are C# FLOATS. Read as doubles offline, V was 1.5e-8 off the mesh gate's and the arc
  // frame's discrete tests (|k| <= tolK at exactly R 2000, the V/4 fit, the V/50 chord points, a ceil at an exact ratio)
  // flipped between the two gates: an R 2000 bump failed offline at 1.20x and read 0.04x in CitySmooth
  {
    const fl = [['V', 0.025], ['CollinearDeg', 0.02], ['ExistInsetM', 0.3], ['MatchM', 3.6576], ['DashTol', 0.2]].filter(([k, v]) => !(R[k] === Math.fround(v) && R[k] !== v));
    const tb = R.RMin.motorway === Math.fround(150) && R.TaperFloor.local === Math.fround(15) && R.RMin.local === Math.fround(7.5);
    const frameOf = Rc => {
      const k = 1 / Rc, X = [], Z = [];
      for (let s = 0; s <= 100 + 1e-9; s += 1) { const th = k * s, o = s - 50, d = o >= 0 && o <= 8 ? 0.03 * (1 - Math.cos(2 * Math.PI * o / 8)) / 2 : 0; X.push(Math.sin(th) / k - Math.sin(th) * d); Z.push((1 - Math.cos(th)) / k + Math.cos(th) * d); }
      const n = X.length, C = [0], TH = new Array(n).fill(0);
      for (let i = 1; i < n; i++) C.push(C[i - 1] + Math.hypot(X[i] - X[i - 1], Z[i] - Z[i - 1]));
      for (let i = 1; i + 1 < n; i++) { const ux = X[i] - X[i - 1], uz = Z[i] - Z[i - 1], vx = X[i + 1] - X[i], vz = Z[i + 1] - Z[i]; TH[i] = Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz); }
      return !!arcFrame(X, Z, C, TH, R);
    };
    const f2000 = frameOf(2000), f1999 = frameOf(1999);
    probe('C7 the rules parser reads a const float as C# holds it (Math.fround: V 0.02500000037) and the RClass tables as floats; an exact R 2000 arc with a 3 cm bump then has no frame, as in CitySmooth (the float tolK holds it), and R 1999 has one',
      fl.length === 0 && tb && !f2000 && f1999, `V ${R.V}; not float: ${fl.map(x => x[0]).join(', ') || 'none'}; tables ${tb ? 'float' : 'NOT float'}; R 2000 frame ${f2000 ? 'yes' : 'no'}, R 1999 ${f1999 ? 'yes' : 'no'}`);
  }
  // legitimate curves and sub-V features on them
  for (const [label, pts] of [['a clean arc of R 1000 m sampled every 1 m', arcPts(grid(0, 60, 1), () => 0, 1000)], ['a clean arc of R 30 m sampled every 1 m', arcPts(grid(0, 60, 1), () => 0, 30)],
    ['a bump of 2 cm over 8 m on R 100 m (under V)', arcPts(grid(0, 8, 1), rc(0.02, 8), 100)], ['a wave of +-1.5 cm every 16 m on R 100 m (under V)', arcPts(grid(0, 48, 1), wave(0.015, 16), 100)],
    ['a long S-bend of R 300 m arcs turning 20 deg each way', (() => { const pts = [[0, 0]]; let h = 10 * DEG, p = fwd([0, 0], h, 60); pts.push(p); const c = sagChord(300, hw2), nA = Math.ceil(300 * 20 * DEG / c), dh = 20 * DEG / nA, ch = 2 * 300 * Math.sin(dh / 2);
      for (const sg of [1, -1]) { for (let k = 0; k < nA; k++) { h += sg * (k === 0 ? dh / 2 : dh); p = fwd(p, h, ch); pts.push(p); } h += sg * dh / 2; } pts.push(fwd(p, h, 60)); return pts; })()]]) {
    const res = gt(pts);
    probe(`C6 ${label}: nothing`, gated(res).length === 0, fmt(res));
  }
}

// ---- T: a HOOK at a line's end (review 6: the lone rule's chord toward the end is the stub d, so f = d |t| / 8 ~ the
// end's offset / 8 - a hook whose end stood up to 8V off passed)
if (want('T')) {
  console.log("T  B2 KINK: a HOOK - a corner whose chord runs on to the strand's end - by the approach extrapolated, at most the end's offset");
  const gt = pts => gateOf(tw2(pts), R, layouts);
  const b2l = (res, e) => new Set(gated(res).filter(r => r.check === 'B2' && (e === undefined || r.e === e)).map(r => r.lineId)).size;
  const deadEnd = (t, d) => { const p0 = [0, 0], p1 = fwd(p0, 10 * DEG, 80), p2 = fwd(p1, (10 + t) * DEG, d); return gt([p0, p1, p2]); };
  for (const [t, d] of [[6, 1], [4, 2.7]]) {
    const res = deadEnd(t, d);
    probe(`T1 a ${t} deg hook ${d} m before a dead end (the end ${(d * Math.sin(t * DEG) * 100).toFixed(1)} cm off the approach): a KINK on all 7 lines`, b2l(res) === 7 && gated(res).some(r => r.check === 'B2' && /hook/.test(r.what || '')), fmt(res));
  }
  {
    // 0.84 m before the end the paint lines have stopped short of the corner: the ribbon edges and the midline carry it
    const res = deadEnd(13, 0.84), on = new Set(gated(res).filter(r => r.check === 'B2' && /hook/.test(r.what || '')).map(r => r.lineId));
    probe('T1 a 13 deg hook 0.84 m before a dead end (the end 18.9 cm off; review 6: 0.95x): a hook on both ribbon edges and the midline', ['RL', 'RR', 'MID'].every(l => on.has(l)), fmt(res));
  }
  // before a T junction's fan mouth: the side street bends t degrees d metres before its mouth
  const tee = (t, d) => {
    const probeCity = gateOf(city([{ pts: [[-80, 0], [0, 0]] }, { pts: [[0, 0], [80, 0]] }, { pts: [[0, 80], [0, 0]] }]), R, layouts);
    const trim = probeCity.S.E[2].length - probeCity.S.E[2].secs.at(-1).s, zv = trim + d, sx = Math.tan(t * DEG) * zv;
    return gateOf(city([{ pts: [[sx - 80, 0], [sx, 0]] }, { pts: [[sx, 0], [sx + 80, 0]] }, { pts: [[0, 80], [0, zv], [sx, 0]] }]), R, layouts);
  };
  // (the corner d metres from the data node; at 6 deg and 1 m the fan's mouth trims the corner itself away)
  for (const [t, d] of [[6, 1.2], [5, 1]]) {
    const res = tee(t, d);
    probe(`T1 a ${t} deg hook ${d} m before a T junction's fan mouth: a hook on all 7 lines of the side street`, b2l(res, 2) === 7 && gated(res).some(r => r.e === 2 && /hook/.test(r.what || '')), gated(res).filter(r => r.e === 2).map(r => `${r.check} ${r.lineId} x${r.ratio.toFixed(2)}`).join(', ') || 'nothing on the side street');
  }
  for (const [t, d, why] of [[2, 0.1, 'the end 0.35 cm off'], [1, 3, 'a legal vertex']]) {
    const res = deadEnd(t, d);
    probe(`T2 a ${t} deg turn ${d} m before a dead end (${why}): nothing`, gated(res).length === 0, fmt(res));
  }
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
  // review 5: the gate's own code is an input - a loosening edit to it used to pass its own ratchet, never STALE
  const code = typeof GB.codeDigest === 'function' ? GB.codeDigest(join(HERE, 'lib')) : null;
  const sc = ratchet(S0, B, R, { inputs: { ...inputs, code: 'another' } });
  probe('R11 the gate\'s code (tools/city/lib) is in the fingerprint, and a baseline measured with other code is STALE, and FAILS', /^[0-9a-f]{12}$/.test(code || '') && !sc.ok && !!sc.stale && sc.stale.some(x => /code/.test(x)),
    `digest ${code}; ${sc.stale ? sc.stale.join('; ') : 'not stale'}`);
  // review 5: dropping a way from the creek pin changed neither fingerprint, and the pin then passed
  const pinned = ratchet(S0, { ...B, pinned: [...R.PinnedWays, 424242] }, R, { inputs });
  probe('R12 a way dropped from the creek pin since the baseline was recorded FAILS (and the pin\'s ways are in the rules digest)',
    !pinned.ok && !!pinned.demoted && pinned.demoted.some(d => /424242/.test(d)) && typeof GB.rulesDigest === 'function' && GB.rulesDigest(R) !== GB.rulesDigest({ ...R, PinnedWays: R.PinnedWays.slice(1) }), pinned.demoted && pinned.demoted.join('; '));
  // review 5: a re-record after a gate change dropped 763 of the city's keys inside a total that grew
  const loose = typeof GB.loosenings === 'function' ? GB.loosenings : () => ({ dataMoved: true, lines: [] });
  const Bk = entryOf(summ(jog([51, 76], 0.05)), inputs);
  const gone = loose(summ(jog([51], 0.05)), Bk, R, { ...inputs, code: 'changed' }), moved = loose(summ(jog([51], 0.05)), Bk, R, { ...inputs, graph: 'moved' });
  const lower = loose(summ(jog([51], 0.04)), entryOf(summ(jog([51], 0.05)), inputs), R, { ...inputs, rules: 'changed' });
  probe('R13 a re-record that loosens - keys vanished or scoring lower while the data did not move (only the gate did) - is REFUSED; the same after a data move alone is not',
    gone.lines.some(l => /B2 KINK: \d+ keys vanished/.test(l)) && lower.lines.some(l => /B2 KINK: 0 keys vanished, [1-9]\d* keys score lower/.test(l)) && moved.lines.length === 0,
    [...gone.lines, ...lower.lines].map(l => l.trim()).join(' | ') || 'nothing refused');
  // review 5: a ribbon edge's key took the side of the run's worst sample, so a key moved between RL and RR - vanishing from
  // the ratchet - when a new rule scored an edge the chain runs the other way higher; now it is the side of its own bucket
  {
    // the chain runs edge 0 (way 7101) forward and edge 1 (way 7102, drawn from its far end) backwards: a 3 degree kink at
    // the node and a 6 degree one 2 m on make one run on each ribbon edge, its worst on edge 1; that strand is RL on edge 0
    // and RR on edge 1 (the other strand the other way round)
    const a0 = [0, 0], a1 = fwd(a0, 0, 60), b1 = fwd(a1, 3 * DEG, 2), a2 = fwd(b1, 9 * DEG, 60);
    const res = gateOf(city([{ pts: [a0, a1], wayId: 7101 }, { pts: [a2, b1, a1], wayId: 7102 }]), R, layouts);
    const runs = gated(res).filter(r => r.check === 'B2' && r.kind === 'edge' && r.kk.some(k => k.startsWith('7101:')) && r.kk.some(k => k.startsWith('7102:')));
    const sideOf = (r, w) => new Set(r.kk.filter(k => k.startsWith(w + ':')).map(k => k.split(':')[3]));
    probe("R14 a kink run across a node into an edge the chain runs backwards keys each bucket by its own edge's side (RL on one edge is RR on the other), not by the side of the run's worst",
      runs.length === 2 && runs.every(r => { const x = sideOf(r, 7101), y = sideOf(r, 7102); return x.size === 1 && y.size === 1 && [...x][0] !== [...y][0]; }),
      runs.map(r => r.kk.join(' ')).join(' | ') || gated(res).filter(r => r.check === 'B2').map(r => r.kk.join(' ')).join(' | '));
  }
  // review 6: a loosening rode along whenever any data input moved - the charlotte merge's NAME and SPAN, a re-export
  {
    const inp6 = { ...inputs, replica: 'q' }, B6 = entryOf(summ(jog([51, 76], 0.05)), inp6), B6one = entryOf(summ(jog([51], 0.05)), inp6);
    const merge = loose(summ(jog([51, 76], 0.05)), B6, R, { ...inp6, code: 'changed', sections: { EDGE: 'a', NAME: 'n2', SPAN: 's2' } });
    const png = loose(summ(jog([51], 0.05)), B6, R, { ...inp6, rules: 'changed', paint: 'p2' });
    probe('R15 the gate moving TOGETHER with the data (its code with the charlotte merge\'s NAME and SPAN; its rules with a road PNG) is REFUSED - two steps, the gate change on the old data first - even when no key is lost',
      merge.lines.some(l => /TOGETHER/.test(l) && /NAME/.test(l)) && png.lines.some(l => /TOGETHER/.test(l)) && merge.gateMoved && merge.dataMoved,
      [...merge.lines, ...png.lines].map(l => l.trim().slice(0, 120)).join(' | ') || 'nothing refused');
    // the builder replica is data: a builder fix mirrored in it drops keys and records freely; a check-code loosening does not
    const fix = loose(summ(jog([51], 0.05)), B6, R, { ...inp6, replica: 'q2' });
    const chk = loose(summ(jog([51], 0.05)), B6, R, { ...inp6, code: 'changed' });
    const lib = join(HERE, 'lib'), split = typeof GB.codeDigest === 'function' && Array.isArray(GB.REPLICA_CODE) && Array.isArray(GB.CHECK_CODE);
    probe('R16 the builder replica (linesim.mjs, citydata.mjs) has a digest of its own, out of the gate\'s: a replica move that drops keys (a builder fix) records freely; the same keys dropped by the check code are REFUSED',
      split && GB.REPLICA_CODE.every(f => !GB.CHECK_CODE.includes(f)) && GB.codeDigest(lib, GB.REPLICA_CODE) !== GB.codeDigest(lib) && fix.lines.length === 0 && chk.lines.some(l => /keys vanished/.test(l)) && B6one,
      `replica move: ${fix.lines.length ? fix.lines.join(' | ') : 'records'}; check code: ${chk.lines.map(l => l.trim()).join(' | ') || 'records'}`);
  }
  // the mesh gate: CitySmooth.Loosenings compares keys only when the gate ('rules', 'code') moved, refuses the gate moving
  // with the data, and a builder change (CityMeshes beyond the tap: no input) compares nothing (source contract; the
  // harness in the review notes runs it on the same cases)
  {
    const smooth = readFileSync(join(UNITY, 'Assets/PSXRacing/Editor/CitySmooth.cs'), 'utf8').replace(/\r\n/g, '\n');
    const lo = (smooth.match(/static List<string> Loosenings\([^)]*\)\s*\{[\s\S]*?\n        \}/) || [''])[0];
    probe('R17 the mesh gate\'s Loosenings keeps the contract: gate inputs are rules and code, the gate moving with the data is refused as two steps, keys are compared only when the gate alone moved',
      /k == "rules" \|\| k == "code"/.test(lo) && /TOGETHER/.test(lo) && /if \(gate\.Count == 0 \|\| data\.Count > 0\) return o;/.test(lo),
      lo ? lo.split('\n').filter(l => /TOGETHER|gate\.Count/.test(l)).map(l => l.trim().slice(0, 100)).join(' | ') : 'no Loosenings found');
  }
}

console.log(`\n${passed} probes passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
