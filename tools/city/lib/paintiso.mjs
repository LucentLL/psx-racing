// paintiso.mjs - where a painted line of one ribbon quad actually lands, the
// way the GPU draws it (plan amendment A7: per TRIANGLE, never only at the
// sections).
//
// BuildRoadsAndDecks (CityMeshes.cs:1919) emits each span between sections A
// and B as Bucket.Quad(A.L, B.L, B.R, A.R, uv...), and Bucket.Quad (:347-355)
// splits it into the triangles (a,c,b) = (A.L, B.R, B.L) and (a,d,c) =
// (A.L, A.R, B.R): the diagonal is A.L-B.R. Texture U (and V) are affine
// inside each triangle (PSX/Lit's _Affine is 0: perspective-correct, so linear
// in world space), so a painted line - a constant U - is an exact straight
// segment inside each triangle and bends where it crosses the diagonal.
//
// Two forms:
//   lineInSpan / slat / frameOf  the census's own (kept for the reproduction)
//   quadIso                      the gate's: every crossing computed once per
//                                quad EDGE in a canonical direction, so the
//                                two triangles and the two quads sharing a
//                                section agree to the bit
import { dot, sub } from './linesim.mjs';

export const cross = (u, v) => u[0] * v[1] - u[1] * v[0];

/// The ribbon frame of span (A, B): centre c(t) = lerp(pA, pB, t) and cross
/// direction r(t) = lerp(rA, rB, t). Returns {t, lat} with P = c(t) + lat * r^(t):
/// a line painted at a constant offset has a constant lat in this frame.
export function frameOf(A, B, P) {
  const D = sub(B.p, A.p), d = sub(P, A.p);
  const R0 = A.right, R1 = sub(B.right, A.right);
  const a2 = -cross(D, R1), a1 = cross(d, R1) - cross(D, R0), a0 = cross(d, R0);
  const DD = dot(D, D);
  const tl = DD > 1e-9 ? dot(d, D) / DD : 0;
  let t = tl;
  if (Math.abs(a2) < 1e-9) { if (Math.abs(a1) > 1e-12) t = -a0 / a1; }
  else {
    const disc = a1 * a1 - 4 * a2 * a0;
    if (disc >= 0) {
      const sq = Math.sqrt(disc), r1 = (-a1 + sq) / (2 * a2), r2 = (-a1 - sq) / (2 * a2);
      t = Math.abs(r1 - tl) < Math.abs(r2 - tl) ? r1 : r2;
    }
  }
  const c = [A.p[0] + D[0] * t, A.p[1] + D[1] * t];
  const r = [R0[0] + R1[0] * t, R0[1] + R1[1] * t];
  const rl = Math.hypot(r[0], r[1]) || 1;
  return { t, lat: ((P[0] - c[0]) * r[0] + (P[1] - c[1]) * r[1]) / rl };
}

function iso(V, labels, u) {
  const out = [];
  for (let k = 0; k < 3; k++) {
    const a = V[k], b = V[(k + 1) % 3];
    const da = a.u - u, db = b.u - u;
    if ((da >= 0) === (db >= 0)) continue;
    const t = da / (da - db);
    out.push({ P: [a.P[0] + (b.P[0] - a.P[0]) * t, a.P[1] + (b.P[1] - a.P[1]) * t], lab: labels[k] });
  }
  return out.length === 2 ? out : [];
}

/// The census's form. For one span and one line U: up to two segments, each [pt0, pt1].
export function lineInSpan(A, B, u) {
  const AL = { P: A.L, u: A.uL }, AR = { P: A.R, u: A.uR }, BL = { P: B.L, u: B.uL }, BR = { P: B.R, u: B.uR };
  const segs = [];
  const t2 = iso([AL, AR, BR], ['A', 'R', 'D'], u);   // (A.L, A.R, B.R): edges A.L-A.R, A.R-B.R, B.R-A.L
  const t1 = iso([AL, BR, BL], ['D', 'B', 'Lg'], u);  // (A.L, B.R, B.L): edges A.L-B.R, B.R-B.L, B.L-A.L
  if (t2.length) segs.push(t2);
  if (t1.length) segs.push(t1);
  return segs;
}

/// (s, lat) of a line point: on a section exactly, elsewhere from the frame.
export function slat(A, B, pt) {
  if (pt.lab === 'A') return { s: A.s, lat: dot(sub(pt.P, A.p), A.right), t: 0 };
  if (pt.lab === 'B') return { s: B.s, lat: dot(sub(pt.P, B.p), B.right), t: 1 };
  let f = frameOf(A, B, pt.P);
  if (!(f.t > -0.1 && f.t < 1.1)) {
    // the frame is ambiguous this far out (short span, rotating cross-lines): fall back to the chord
    const D = sub(B.p, A.p), DD = dot(D, D);
    const t = DD > 1e-9 ? Math.max(0, Math.min(1, dot(sub(pt.P, A.p), D) / DD)) : 0;
    const r = [A.right[0] + (B.right[0] - A.right[0]) * t, A.right[1] + (B.right[1] - A.right[1]) * t], rl = Math.hypot(r[0], r[1]) || 1;
    const c = [A.p[0] + D[0] * t, A.p[1] + D[1] * t];
    f = { t, lat: ((pt.P[0] - c[0]) * r[0] + (pt.P[1] - c[1]) * r[1]) / rl };
  }
  return { s: A.s + (B.s - A.s) * f.t, lat: f.lat, t: f.t };
}

// ------------------------------------------------------------ the gate's form
// A quad is {AL, BL, BR, AR} vertices, each {x, z, u, v}. Its five edges, in
// the canonical direction every caller uses:
//   0 'A'  section A   AL -> AR
//   1 'B'  section B   BL -> BR
//   2 'D'  diagonal    AL -> BR
//   3 'L'  L chord     AL -> BL      (the ribbon's L side, right of travel)
//   4 'R'  R chord     AR -> BR      (the ribbon's R side, left of travel)
// Triangle T2 = (AL, AR, BR) owns edges A, R, D; triangle T1 = (AL, BR, BL)
// owns D, B, L. A line enters the quad through A (in T2), crosses D, leaves
// through B (in T1) - or meets a chord where the ribbon edge crops it.
export const QE = ['A', 'B', 'D', 'L', 'R'];
const QE_ENDS = [['AL', 'AR'], ['BL', 'BR'], ['AL', 'BR'], ['AL', 'BL'], ['AR', 'BR']];
const TRI_T2 = [0, 4, 2], TRI_T1 = [2, 1, 3];

function crossAt(a, b, u) {
  const da = a.u - u, db = b.u - u;
  if ((da >= 0) === (db >= 0)) return null;
  const t = da / (da - db);
  return { x: a.x + (b.x - a.x) * t, z: a.z + (b.z - a.z) * t, v: a.v + (b.v - a.v) * t, t };
}

/// U's plan gradient magnitude over one triangle (1/m), for LINEWIDTH.
export function gradU(p, q, r) {
  const x1 = q.x - p.x, z1 = q.z - p.z, x2 = r.x - p.x, z2 = r.z - p.z;
  const det = x1 * z2 - x2 * z1;
  if (Math.abs(det) < 1e-9) return NaN;
  const du1 = q.u - p.u, du2 = r.u - p.u;
  const gx = (du1 * z2 - du2 * z1) / det, gz = (x1 * du2 - x2 * du1) / det;
  return Math.hypot(gx, gz);
}

/// Every segment of the iso-line U = u inside one quad: an array of up to two
/// {a, b, ea, eb, grad} with a/b = {x, z, v} and ea/eb the quad-edge letters,
/// in TRAVEL order: each segment runs forward and the two are emitted in the
/// order the line visits them, so a line that crosses the diagonal and is
/// cropped out through the L chord (A -> D in T2, then D -> L in T1) comes back
/// as A->D, D->L - one polyline, never a reversed tail.
///
/// Travel position of a crossing: A = 0, B = 1, and on the L, R and D edges
/// (all of which run from section A to section B) the edge's own parameter.
/// Two segments sharing the D crossing are one path X - D - Y, walked from
/// whichever outer end lies earlier; two segments not sharing D (A->R in T2
/// and L->B in T1) are separate pieces, T2's first.
export function quadIso(q, u) {
  const X = new Array(5);
  for (let k = 0; k < 5; k++) X[k] = crossAt(q[QE_ENDS[k][0]], q[QE_ENDS[k][1]], u);
  const segs = [];
  for (const [tri, verts] of [[TRI_T2, ['AL', 'AR', 'BR']], [TRI_T1, ['AL', 'BR', 'BL']]]) {
    const hit = tri.filter(k => X[k]);
    if (hit.length !== 2) continue;
    segs.push({ hit, grad: gradU(q[verts[0]], q[verts[1]], q[verts[2]]) });
  }
  const tOf = k => k === 0 ? 0 : k === 1 ? 1 : X[k].t;
  const mk = (h0, h1, grad) => ({ a: X[h0], b: X[h1], ea: QE[h0], eb: QE[h1], grad });
  if (segs.length === 2 && segs[0].hit.includes(2) && segs[1].hit.includes(2)) {
    // one path through the diagonal: X (T2's outer end) - D - Y (T1's outer end)
    const x = segs[0].hit.find(k => k !== 2), y = segs[1].hit.find(k => k !== 2);
    const xFirst = tOf(x) < tOf(y) || (tOf(x) === tOf(y) && x <= y);
    return xFirst ? [mk(x, 2, segs[0].grad), mk(2, y, segs[1].grad)] : [mk(y, 2, segs[1].grad), mk(2, x, segs[0].grad)];
  }
  const out = [];
  for (const sg of segs) {
    let [h0, h1] = sg.hit;
    if (tOf(h1) < tOf(h0) || (tOf(h1) === tOf(h0) && h1 < h0)) [h0, h1] = [h1, h0];
    out.push(mk(h0, h1, sg.grad));
  }
  return out;
}
