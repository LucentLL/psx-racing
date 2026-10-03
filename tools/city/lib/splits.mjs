// splits.mjs - WP-11 C11 (plan amendment A4 "WP-11 (amended)", A8 rule 4):
// the places where one road's undivided two-way piece splits into its two
// one-way carriageways. They are NOT junctions: each carriageway starts as
// its half of the undivided road's section and eases out to its own line
// along a median taper. Written as section SPLT for the line model (WP-11b)
// to draw; until then the builder keeps drawing them as it does (the
// through arm mitred, the other carriageway clipped beside it).
//
// A split node: three arms, one two-way non-link edge U and two
// one-way non-link edges of U's name, one leaving the node and one arriving,
// both pointing away from U (a continuation, not a side street).
//   node, U, the carriageway leaving (A), the one arriving (B), then in U's
//   frame at the node (+ = left of U's direction INTO the node): A's and B's
//   centre offsets when the two tile U's section in proportion to their
//   widths (A on the right of that direction, B on the left: right-hand
//   traffic), and the MUTCD shifting-taper rate (metres along per metre
//   across) at the road's speed.
import { profileFor } from './citydata.mjs';

const mphOf = w => w.speed > 0 ? w.speed / 1.609344 : w.link ? 35 : [25, 35, 40, 45, 55, 65][w.rank];
const shiftRate = mph => Math.max(10, mph <= 40 ? mph * mph / 60 : mph);

export function findSplits(edges, nodes) {
  const arms = Array.from({ length: nodes.length }, () => []);
  for (const e of edges) if (e.a !== e.b) { arms[e.a].push(e); arms[e.b].push(e); }
  const out = [];
  const outDir = (e, n) => {
    const P = e.pts, [p, q] = e.a === n ? [P[0], P[1]] : [P[P.length - 1], P[P.length - 2]];
    const d = Math.hypot(q[0] - p[0], q[1] - p[1]) || 1;
    return [(q[0] - p[0]) / d, (q[1] - p[1]) / d];
  };
  for (let n = 0; n < nodes.length; n++) {
    const l = arms[n];
    if (l.length < 3) continue;
    // roads pass L4 (plan B6 FX2a): also where the split IS a junction - a
    // cross street at the node (4+ arms) - the one two-way arm whose two
    // carriageways of its name, one leaving and one arriving, run on
    // opposite it (dot < -0.85); the cross street is left alone
    let U = null, A = null, B = null, found = 0;
    for (const u of l) {
      if (u.way.oneway || u.way.link || !u.way.name) continue;
      const du = outDir(u, n), lim = l.length === 3 ? -0.5 : -0.85;
      const opp = l.filter(e => e !== u && e.way.oneway && !e.way.link && e.way.name === u.way.name
        && du[0] * outDir(e, n)[0] + du[1] * outDir(e, n)[1] <= lim);
      const a = opp.filter(e => e.a === n), b = opp.filter(e => e.b === n);   // leaving, arriving
      if (a.length !== 1 || b.length !== 1) continue;
      if (l.length === 3 && (l.filter(e => !e.way.oneway && !e.way.link).length !== 1 || opp.length !== 2)) continue;
      U = u; A = a[0]; B = b[0]; found++;
    }
    if (found !== 1) continue;
    const du = outDir(U, n), da = outDir(A, n), db = outDir(B, n);
    const pu = profileFor(U.way.rank, false, false, U.lanes, U.turn);
    const pa = profileFor(A.way.rank, false, true, A.lanes, A.turn), pb = profileFor(B.way.rank, false, true, B.lanes, B.turn);
    const wA = pu.width * pa.width / (pa.width + pb.width), wB = pu.width - wA;
    out.push({ node: n, u: U.id, a: A.id, b: B.id, offA: -pu.width / 2 + wA / 2, offB: pu.width / 2 - wB / 2,
               rate: shiftRate(mphOf(U.way)), turnDeg: Math.acos(Math.max(-1, Math.min(1, da[0] * db[0] + da[1] * db[1]))) * 180 / Math.PI });
  }
  return out;
}
