// synthcity.mjs - small synthetic cities for the smoothness gate's probes
// (tools/city/gateprobes.mjs): ways in game metres, run through the same
// replica (linesim.mjs) and gate (linegate.mjs) linecheck.mjs runs on the
// shipped graph.
import { createSim } from './linesim.mjs';
import { runGate } from './linegate.mjs';
import { profileFor } from './citydata.mjs';

export const DEG = Math.PI / 180;
/// A point d metres from p on heading h (0 = +z, clockwise from above).
export const fwd = (p, h, d) => [p[0] + Math.sin(h) * d, p[1] + Math.cos(h) * d];

/// ways: [{ pts: [[x, z]...], lanes, rank, link, oneway, turn, name, wayId }].
/// Nodes are shared where two ways' end points coincide (to the millimetre).
export function city(ways) {
  const nodes = [], nodeAt = new Map(), key = p => `${p[0].toFixed(3)},${p[1].toFixed(3)}`;
  const node = p => { const k = key(p); if (!nodeAt.has(k)) { nodeAt.set(k, nodes.length); nodes.push({ x: p[0], z: p[1], ctl: 0 }); } return nodeAt.get(k); };
  const edges = ways.map((w, i) => {
    const e = { index: i, a: node(w.pts[0]), b: node(w.pts.at(-1)), name: w.name || `Way ${i}`, rank: w.rank ?? 0, link: !!w.link, oneway: !!w.oneway,
      bridge: false, tunnel: false, turn: !!w.turn, roundabout: false, lanes: w.lanes ?? 2, level: 0, speed: 25, wayId: w.wayId ?? (5000 + i),
      pts: w.pts.map(p => [Math.fround(p[0]), Math.fround(p[1])]) };
    const s = new Float64Array(e.pts.length);
    for (let k = 1; k < e.pts.length; k++) s[k] = s[k - 1] + Math.hypot(e.pts[k][0] - e.pts[k - 1][0], e.pts[k][1] - e.pts[k - 1][1]);
    e.s = s; e.length = s.at(-1);
    e.profile = profileFor(e.rank, e.link, e.oneway, e.lanes, e.turn); e.width = e.profile.width; e.hw = e.width / 2;
    return e;
  });
  const nodeEdges = nodes.map(() => []);
  for (const e of edges) { nodeEdges[e.a].push(e.index); nodeEdges[e.b].push(e.index); }
  return { version: 2, nodes, edges, nodeEdges, crossings: [], wspans: [], routes: [], waters: [], sections: {} };
}

/// A polyline: a straight lead, then turns[i] degrees at vertices gaps[i - 1]
/// metres apart, then a straight tail.
export function bent(turns, gaps, h0 = 10 * DEG, lead = 64.3) {
  const pts = [[0, 0]]; let h = h0, p = fwd(pts[0], h, lead);
  pts.push(p);
  turns.forEach((t, i) => { h += t * DEG; p = fwd(p, h, i + 1 < turns.length ? gaps[i] : lead); pts.push(p); });
  return pts;
}

/// A tangent-arc fillet: a lead, an arc of radius R turning D degrees whose
/// vertices sit on the circle with chords of at most `chord`, and a tail.
export function fillet(R, D, chord, h0 = 10 * DEG, lead = 64.3) {
  const n = Math.max(1, Math.ceil(R * D * DEG / chord)), dh = D * DEG / n;
  const pts = [[0, 0]]; let h = h0, p = fwd(pts[0], h, lead);
  pts.push(p);
  // chord k leaves on the heading of the arc at its midpoint: the tangent point turns dh/2, every vertex after dh
  const c = 2 * R * Math.sin(dh / 2);
  for (let k = 0; k < n; k++) { h += k === 0 ? dh / 2 : dh; p = fwd(p, h, c); pts.push(p); }
  h += dh / 2; pts.push(fwd(p, h, lead));
  return pts;
}

/// The sagitta-densified chord of a fillet (plan A2 I5 / WP-11): at most
/// sqrt(8 eps R^2 / (R + hw)), so the OUTER drawn edge's facets stay within eps.
export const sagChord = (R, hw, eps = 0.02) => Math.sqrt(8 * eps * R * R / (R + hw));

/// Run the replica and the gate. tweakT(S) edits the trims before sections are
/// cut; tweakSecs(S) edits the sections before the gate reads them.
export function gateOf(c, R, layouts, { model = 'asbuilt', tweakT, tweakSecs } = {}) {
  const S = createSim(c, model);
  if (tweakT) tweakT(S);
  for (const e of S.E) e.secs = S.sectionsOf(e);
  if (tweakSecs) tweakSecs(S);
  return { S, g: runGate(S, R, layouts, { planTaperShape: model === 'm0' ? 'smooth' : 'linear', refSpots: [] }) };
}
