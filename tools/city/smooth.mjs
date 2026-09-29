// smooth.mjs - metrics.mjs' SMOOTH section (plan amendment A4, G-offline): the
// smoothness gate's two SHAPE checks run on the exported DATA, in seconds - an
// early warning before any tile is built. It never replaces the mesh gate
// (Editor/CitySmooth.cs) or its offline twin (linecheck.mjs): it sees the
// design line only, not the sections, U or paint the builder makes of it.
//
//   B2 KINK   facet sagitta f = min(c-, c+, ChordCapM) * |turn| / 8 > V at every
//             vertex, and the same for every corner split over close vertices
//             (a same-way cluster judged as one vertex, less its own rounding;
//             collinear vertices under CollinearDeg dropped first; the chords
//             reach past neighbours turning under KinkNoiseShare of the turn;
//             a zigzag peak scores twice its sagitta; a turn and a turn back
//             is judged by its net turn and, between two straights, by how
//             far it is drawn outside every smooth transition - a bump, a
//             notch - and by a sideways step (whole over 2.5 m or less, else
//             the part faster than the plan's ease); a wave's lobes by their
//             deviation from its mean line - lib/kink.mjs kinkScores, the
//             gate's own rule)
//   B3 CURVE  R = CurveHalfM / (heading change of the chords either side) under
//             the class's R_min (the centreline), or under InnerEdgeMinRM (the
//             inner offset curve)
// on three lines per road: the exported centreline and its two offset curves
// at the profile's half width (mitre-joined, as the ribbon's sections are cut
// on bisectors). Lines run through 2-arm nodes (the mesh mitres or fans
// them), not through junctions of three or more; roundabouts are left out
// (drawn polygonal on purpose, as in KINKS). Every threshold comes from
// Assets/PSXRacing/Editor/SmoothRules.cs.
import { classOf, CLASSES } from './lib/citydata.mjs';
import { readSmoothRules } from './lib/smoothrules.mjs';
import { kinkScores } from './lib/kink.mjs';

const DEG = 180 / Math.PI;
const r1 = v => Math.round(v * 10) / 10, r3 = v => Math.round(v * 1000) / 1000;
const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;
const ll = (x, z) => `${(LAT0 + z / 111132).toFixed(5)},${(LON0 + x / (111320 * Math.cos(LAT0 * Math.PI / 180))).toFixed(5)}`;

/// Chains of edges through 2-arm nodes: [{ pts: [[x,z]...], cls: per point, hw: per point }].
function designLines(city) {
  const E = city.edges, used = new Uint8Array(E.length);
  const through = (ei, node) => {
    const inc = city.nodeEdges[node];
    if (inc.length !== 2 || inc[0] === inc[1]) return -1;
    const o = inc[0] === ei ? inc[1] : inc[0];
    return E[o].roundabout || E[o].a === E[o].b ? -1 : o;
  };
  const out = [];
  for (const e0 of E) {
    if (used[e0.index] || e0.roundabout || e0.a === e0.b) continue;
    // back to the start of the chain (or once round a ring)
    let cur = e0, enter = e0.a;
    for (let g = 0; g < E.length; g++) {
      const o = through(cur.index, enter);
      if (o < 0 || o === e0.index) break;
      const oe = E[o]; enter = oe.a === enter ? oe.b : oe.a; cur = oe;
    }
    const pts = [], cls = [], hw = [];
    let at = cur, from = enter;
    while (at && !used[at.index]) {
      used[at.index] = 1;
      const fwd = at.a === from, P = fwd ? at.pts : [...at.pts].reverse();
      const k = classOf(at);
      for (let i = pts.length ? 1 : 0; i < P.length; i++) { pts.push(P[i]); cls.push(k); hw.push(at.hw); }
      const exit = fwd ? at.b : at.a;
      const o = through(at.index, exit);
      if (o < 0) break;
      at = E[o]; from = exit;
    }
    if (pts.length >= 3) out.push({ pts, cls, hw });
  }
  return out;
}

const turnAt = (a, b, c) => { const ux = b[0] - a[0], uz = b[1] - a[1], vx = c[0] - b[0], vz = c[1] - b[1]; return Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz); };

/// Mitre-joined offset of a polyline by d (+ = left of travel), the mitre
/// capped at 5x (a hairpin's inner mitre runs to infinity).
function offsetLine(pts, d) {
  const n = pts.length, out = new Array(n);
  const nrm = (a, b) => { const dx = b[0] - a[0], dz = b[1] - a[1], m = Math.hypot(dx, dz) || 1; return [-dz / m, dx / m]; };
  for (let i = 0; i < n; i++) {
    const n0 = i > 0 ? nrm(pts[i - 1], pts[i]) : null, n1 = i + 1 < n ? nrm(pts[i], pts[i + 1]) : null;
    let m = n0 && n1 ? [n0[0] + n1[0], n0[1] + n1[1]] : (n0 || n1);
    const ml = Math.hypot(m[0], m[1]) || 1; m = [m[0] / ml, m[1] / ml];
    const c = n0 && n1 ? Math.max(0.2, m[0] * n1[0] + m[1] * n1[1]) : 1;
    const di = typeof d === 'number' ? d : d[i];
    out[i] = [pts[i][0] + m[0] * di / c, pts[i][1] + m[1] * di / c];
  }
  return out;
}

/// B2 and B3 on one polyline (hw: how far it stands off the data line, for the
/// WAVE rule's geometry test; 0 on the centreline). Returns the failing vertices.
function shape(pts, R, rLimitAt, hw = 0) {
  // drop collinear vertices and duplicates
  const lim = R.CollinearDeg / DEG, keep = [0];
  for (let i = 1; i + 1 < pts.length; i++) {
    const a = pts[keep[keep.length - 1]], b = pts[i], c = pts[i + 1];
    if (Math.hypot(b[0] - a[0], b[1] - a[1]) < 1e-3 || Math.hypot(c[0] - b[0], c[1] - b[1]) < 1e-3) continue;
    if (Math.abs(turnAt(a, b, c)) < lim) continue;
    keep.push(i);
  }
  keep.push(pts.length - 1);
  const C = [0];
  for (let m = 1; m < keep.length; m++) C.push(C[m - 1] + Math.hypot(pts[keep[m]][0] - pts[keep[m - 1]][0], pts[keep[m]][1] - pts[keep[m - 1]][1]));
  const L = C[C.length - 1];
  const at = (a, hint) => {
    let i = Math.max(0, Math.min(hint, keep.length - 2));
    while (i > 0 && C[i] > a) i--;
    while (i + 2 < keep.length && C[i + 1] < a) i++;
    const p = pts[keep[i]], q = pts[keep[i + 1]], s = C[i + 1] - C[i], t = s > 1e-9 ? Math.max(0, Math.min(1, (a - C[i]) / s)) : 0;
    return [p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t];
  };
  const b2 = [], b3 = [];
  const TH = new Float64Array(keep.length);
  for (let m = 1; m + 1 < keep.length; m++) TH[m] = turnAt(pts[keep[m - 1]], pts[keep[m]], pts[keep[m + 1]]);
  const K2 = kinkScores(keep.map(i => pts[i][0]), keep.map(i => pts[i][1]), C, TH, R, null, hw);
  for (let m = 1; m + 1 < keep.length; m++) {
    const i = keep[m], b = pts[i];
    const th = TH[m];
    const f = K2[m];
    if (f > R.V) b2.push({ i, f, deg: Math.abs(th) * DEG, x: b[0], z: b[1] });
    if (rLimitAt && C[m] >= R.CurveHalfM && L - C[m] >= R.CurveHalfM) {
      const p0 = at(C[m] - R.CurveHalfM, m), p1 = at(C[m] + R.CurveHalfM, m);
      let dpsi = Math.abs(Math.atan2(p1[1] - b[1], p1[0] - b[0]) - Math.atan2(b[1] - p0[1], b[0] - p0[0]));
      if (dpsi > Math.PI) dpsi = 2 * Math.PI - dpsi;
      const Rr = dpsi > 1e-9 ? R.CurveHalfM / dpsi : Infinity, lim2 = rLimitAt(i, th);
      if (Rr < lim2) b3.push({ i, R: Rr, limit: lim2, x: b[0], z: b[1] });
    }
  }
  return { b2, b3, kept: keep.slice(1, -1) };
}

/// The SMOOTH section: { json, lines } for metrics.mjs to print and store.
export function smoothSection(city, rulesPath) {
  const R = readSmoothRules(rulesPath);
  const t0 = Date.now();
  const lines = designLines(city);
  const by = {};
  for (const c of CLASSES) by[c] = { vertices: 0, b2_centre: 0, b2_offset: 0, b3_centre: 0, b3_inner: 0 };
  let worstB2 = null, worstB3 = null, worstB3i = null, total = { vertices: 0, b2_centre: 0, b2_offset: 0, b3_centre: 0, b3_inner: 0 };
  for (const L of lines) {
    const c = shape(L.pts, R, i => R.rMinFor(L.cls[i]));
    for (const i of c.kept) { by[L.cls[i]].vertices++; total.vertices++; }
    for (const v of c.b2) { by[L.cls[v.i]].b2_centre++; total.b2_centre++; if (!worstB2 || v.f > worstB2.f) worstB2 = { ...v, cls: L.cls[v.i] }; }
    for (const v of c.b3) { by[L.cls[v.i]].b3_centre++; total.b3_centre++; if (!worstB3 || v.limit / v.R > worstB3.limit / worstB3.R) worstB3 = { ...v, cls: L.cls[v.i] }; }
    for (const sgn of [1, -1]) {
      const off = offsetLine(L.pts, L.hw.map(h => sgn * h));
      // the INNER offset curve of a turn is the one on its side: + (left) for a left turn
      const o = shape(off, R, (i, th) => Math.sign(th) === sgn ? R.InnerEdgeMinRM : 0, Math.max(...L.hw));
      for (const v of o.b2) { by[L.cls[v.i]].b2_offset++; total.b2_offset++; }
      for (const v of o.b3) { by[L.cls[v.i]].b3_inner++; total.b3_inner++; if (!worstB3i || v.R < worstB3i.R) worstB3i = { ...v, cls: L.cls[v.i] }; }
    }
  }
  const json = {
    V: R.V, chord_cap_m: R.ChordCapM, lines: lines.length, ...total,
    worst_b2_centre: worstB2 && { f_cm: r1(worstB2.f * 100), deg: r1(worstB2.deg), cls: worstB2.cls, at: ll(worstB2.x, worstB2.z) },
    worst_b3_centre: worstB3 && { r_m: r1(worstB3.R), limit_m: worstB3.limit, cls: worstB3.cls, at: ll(worstB3.x, worstB3.z) },
    worst_b3_inner: worstB3i && { r_m: r3(worstB3i.R), cls: worstB3i.cls, at: ll(worstB3i.x, worstB3i.z) },
    by_class: Object.fromEntries(Object.entries(by).filter(([, v]) => v.vertices > 0)),
    elapsed_s: r1((Date.now() - t0) / 1000),
    note: 'the design line only (exported centreline and its half-width offsets, mitre-joined, through 2-arm nodes); the mesh gate (CitySmooth / linecheck.mjs) measures what is drawn',
  };
  const out = [];
  out.push(`\nSMOOTH (the smoothness gate's B2 KINK and B3 CURVE on the design line; V ${R.V * 100} cm, chords capped at ${R.ChordCapM} m)`);
  out.push(`  ${lines.length} lines through 2-arm nodes, ${total.vertices} turning vertices: B2 fails at ${total.b2_centre} centreline and ${total.b2_offset} offset-curve vertices; B3 under the class R_min at ${total.b3_centre}, inner offset under ${R.InnerEdgeMinRM} m at ${total.b3_inner}`);
  if (worstB2) out.push(`  worst B2 ${json.worst_b2_centre.f_cm} cm (${json.worst_b2_centre.deg} deg, ${worstB2.cls}) at ${json.worst_b2_centre.at}; worst B3 R ${json.worst_b3_centre?.r_m} m against ${json.worst_b3_centre?.limit_m} m (${worstB3?.cls}) at ${json.worst_b3_centre?.at}`);
  out.push('  class            vertices  B2 centre  B2 offsets  B3 centre  B3 inner');
  for (const [k, v] of Object.entries(json.by_class))
    out.push(`  ${k.padEnd(15)} ${String(v.vertices).padStart(9)}  ${String(v.b2_centre).padStart(9)}  ${String(v.b2_offset).padStart(10)}  ${String(v.b3_centre).padStart(9)}  ${String(v.b3_inner).padStart(8)}`);
  return { json, lines: out };
}
