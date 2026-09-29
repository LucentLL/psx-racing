// kink.mjs - B2 KINK, shared by the gate (linegate.mjs) and metrics' SMOOTH
// section (smooth.mjs); Editor/CitySmooth.cs KinkChord / KinkScores are the
// same code in C#.
//
// A LONE VERTEX (gate spec 4.2): the facet sagitta f = min(c-, c+, ChordCapM)
// * |turn| / 8. c- and c+ run from the vertex to the nearest kept vertex on
// each side where the line has turned again by KinkNoiseShare of this
// vertex's own turn - one vertex turning that much, or several whose turns
// ADD UP to it: a neighbour turning less is noise (the bisector pinch, a
// diagonal crossing, a section turning a few hundredths of a degree next to a
// real kink) and must not shorten the chord, or a lone 3-6 degree kink with a
// section 1.8 m away scores under V; but a curve that keeps turning, a little
// at every vertex, ends the chord where it has turned that much. On a
// smoothly sampled curve two neighbouring turns share a chord (turn = (c- +
// c+) / 2R), so the chord stops at the neighbour and the formula stays the
// facet sagitta the spec calibrated.
//
// A ZIGZAG PEAK: a vertex whose chords stop at a turn BACK on both sides (the
// line turns the other way before and after it). The smooth line the eye
// expects is then the zigzag's mean line, not an arc through the neighbours,
// and the peak stands off it by c |turn| / 4 - twice the facet sagitta (the
// spec's section 2: "symmetric zigzag: peak deviation <= V"). A sampled
// curve's vertex, an S-bend's inflection or a hedged corner has a same-way
// turn or a straight on one side and stays lone.
//
// A CLUSTER: a corner split over close vertices - 3 degrees as 1.5 + 1.5 a
// metre apart, 15.4 degrees as 7.7 + 7.7 0.91 m apart (Lower Rocky River
// Road) - is the same corner to the eye as a lone one, but every vertex of it
// has a short chord, so the lone formula scores each under V. So every run of
// consecutive kept vertices j..k turning the same way (a vertex turning the
// other way by less than KinkNoiseShare of the run's turn is noise inside it)
// and spanning at most ChordCapM is judged as one vertex too:
//   Vc   its virtual corner, where the line into j meets the line out of k;
//   T    its summed turn;
//   dr   its own rounding: the distance from Vc to the drawn polygon j..k;
//   f    min(c-, c+, ChordCapM) * |T| / 8, the chords running from Vc to
//        where the line turns again by KinkNoiseShare of T (the lone rule:
//        for one vertex Vc is the vertex and dr is 0);
//   gap  f - dr, how far the drawn corner stands inside the smooth curve the
//        eye expects.
// At a viewing distance D a pixel is p = V * D / PixelAtM. The corner reads as
// SHARP there while its rounding is under a pixel (dr < p) and its gap shows
// while it is over one (gap > p). The gate judges the chase view out to
// KinkViewM (40 m: the spec's "at R 7.5 a 90 degree corner stays rounded out
// to about 40 m"), where p is pMax = V * KinkViewM / PixelAtM (16.7 cm), so
// some judged distance shows a kink exactly when
//        gap > max(V, dr)  and  dr < pMax.
// The score is that test in V units: gap * V / max(V, dr) while dr < pMax, a
// KINK past V; a bend rounded by pMax or more reads as a curve at every
// judged distance, and B3's radius is its test. For a lone vertex the score is
// f, the spec's formula. A uniformly sampled arc scores at most its own facet
// sagitta whatever run of it is taken (its rounding grows as fast as its
// chords); a bend spread over half the chord cap or more has gap <= dr; a
// WP-11 fillet is rounded by tens of cm on every drawn line. The score goes to
// the cluster's vertices that turn at least KinkNoiseShare of its largest
// turn (a noise vertex at one end of a run is not the corner).
//
// A HEDGED WINDOW: a turn followed within ChordCapM by a turn back of a
// quarter of it or more - [6, -2.4] degrees 0.8-1.2 m apart, a net 3.6 degree
// corner - stops every chord at the counter-turn and breaks the cluster, so
// its parts score centimetres while the eye sees the NET turn (a lone 3.6
// degree vertex scores 3.1x). The painted lines do this at nearly every
// section kink: the diagonal crossing lands a few cm from the section and
// turns back. So every window j..k of at most ChordCapM that holds turns of
// BOTH signs (a counter-turn of at least KinkNoiseShare of the running turn:
// smaller ones are the cluster's noise) is also judged as a whole:
//   NET CORNER, anywhere: T rounded at most as the ROUNDEST drawing of T inside
//        the window's span w (T / 2 at each end), dr = (w / 2) tan(|T| / 2),
//        chords from the window's middle - a lower bound on how sharp it
//        reads; (f - dr) * V / max(V, dr) while dr < pMax, as a cluster is.
//        On a sampled curve the chords stop at the next vertex, so a window's
//        f never outgrows its dr (an S-bend's inflection scores nothing).
// BETWEEN TWO STRAIGHTS - the line does not turn again by KinkNoiseShare of
// the window's largest heading excursion for min(w / KinkNoiseShare,
// ChordCapM) on either side (the eye's whole kink chord once the window is a
// quarter of it, and longer than any chord of a sampled curve the window
// could sit in, whose next vertex stops it), or runs on straight to its end
// (a fan mouth) at least w away - the eye's reference is exact, the approach
// line (into j) and the exit line (out of k). Then also:
//   NET CORNER at the drawn Vc, where they meet: its rounding is the drawn
//        polygon's, SIGNED - a drawing that passes OUTSIDE Vc (Vc inside the
//        polygon j..k: a notch, a corner with counter-turns either side)
//        rounds it by minus its overshoot, so it scores f plus the overshoot,
//        never a rounded corner's credit;
//   OUTSIDE: how far any drawn vertex between j and k stands outside the
//        region every smooth convex transition between the two straights
//        occupies - the triangle P[j], Vc, P[k] where they meet ahead of j and
//        behind k (a corner), else the hull of P[j], P[k] and each one's foot
//        on the other line (a step; a segment where they coincide: a BUMP,
//        [2, -4, 2] degrees 2 m apart standing 7 cm off a straight). A lone
//        kink, a cluster, a fillet or a hedged corner stands inside (0); a
//        bump, a zigzag between straights or a notch does not. Scored as is;
//   JOG: d, the least sideways gap between the two lines anywhere along the
//        window (0 when they cross inside it: a corner, not a step) - a
//        sideways step, which B4 JUMP fails past V at a node. Drawn over w,
//        the part of it that came faster than the plan's fastest ease (plan
//        I7: a smoothstep over the shortest class taper floor, Lf, the
//        street's 15 m) is d (1 - g(w / Lf)), g(x) = 1.5x - 0.5x^3 (the
//        squeeze envelope's), scored as is: the K7 3.1 cm step over 0.6 m
//        and an 18.2 cm step over 3.2 m fail, a 3 cm drift over 10 m does not.
//        A taper eased over its floor never fits a ChordCapM window.
// A curve that keeps turning (an S-bend of sampled arcs, a taper eased over
// its floor) stops the straights at its next vertex and gets the net-corner
// lower bound only; B1, B3 and A1 judge it.
//
//   C   arc length at each kept vertex (C[0] = 0)
//   TH  signed turn at each kept vertex (radians; the ends are unused)
//   m   the interior kept vertex
export function kinkChord(C, TH, m, share, cap) {
  const thr = Math.abs(TH[m]) * share;
  return Math.min(C[m] - C[reach(C, TH, m, -1, thr, cap, 0)], C[reach(C, TH, m, 1, thr, cap, 0)] - C[m], cap);
}
/// From kept vertex m, step by dir (-1 / +1) to the first vertex where the
/// line has turned again by thr (one turn, or the running sum of the turns
/// passed), or the strand's end, or `cap` metres (plus `pad`, the distance
/// already travelled from a cluster's virtual corner) away.
export function reach(C, TH, m, dir, thr, cap, pad) {
  const n = C.length;
  let i = m + dir, sum = 0;
  while (i > 0 && i < n - 1 && Math.abs(C[i] - C[m]) + pad < cap) {
    sum += TH[i];
    if (Math.abs(TH[i]) >= thr || Math.abs(sum) >= thr) break;
    i += dir;
  }
  return i;
}
/// The sign of the turn that stops reach(C, TH, m, dir, thr, cap, 0): the one
/// vertex turning thr or more, else the running sum; 0 when the strand's end
/// or `cap` stops it instead.
export function stopSign(C, TH, m, dir, thr, cap) {
  const n = C.length;
  let i = m + dir, sum = 0;
  while (i > 0 && i < n - 1 && Math.abs(C[i] - C[m]) < cap) {
    sum += TH[i];
    if (Math.abs(TH[i]) >= thr) return Math.sign(TH[i]);
    if (Math.abs(sum) >= thr) return Math.sign(sum);
    i += dir;
  }
  return 0;
}

/// The least sideways gap, anywhere along the window j..k, between the
/// approach line (P[j-1] -> P[j]) and the exit line (P[k] -> P[k+1]),
/// measured across their mean direction; 0 when they cross inside the
/// window (the header's jog d).
export function jogGap(X, Z, j, k) {
  let ux = X[j] - X[j - 1], uz = Z[j] - Z[j - 1], wx = X[k + 1] - X[k], wz = Z[k + 1] - Z[k];
  const ul = Math.hypot(ux, uz), wl = Math.hypot(wx, wz);
  if (ul < 1e-9 || wl < 1e-9) return 0;
  ux /= ul; uz /= ul; wx /= wl; wz /= wl;
  let mx = ux + wx, mz = uz + wz;
  const ml = Math.hypot(mx, mz);
  if (ml < 1e-9) return 0;
  mx /= ml; mz /= ml;
  const nx = -mz, nz = mx, cu = ux * mx + uz * mz, cw = wx * mx + wz * mz;
  if (cu < 1e-6 || cw < 1e-6) return 0;
  // each line's sideways position where it passes along-distance x; the gap is linear in x
  const x0 = X[j] * mx + Z[j] * mz, x1 = X[k] * mx + Z[k] * mz;
  const latIn = x => (X[j] * nx + Z[j] * nz) + (x - x0) * (ux * nx + uz * nz) / cu;
  const latOut = x => (X[k] * nx + Z[k] * nz) + (x - x1) * (wx * nx + wz * nz) / cw;
  const g0 = latOut(x0) - latIn(x0), g1 = latOut(x1) - latIn(x1);
  return g0 * g1 <= 0 ? 0 : Math.min(Math.abs(g0), Math.abs(g1));
}

/// g(x) = 1.5x - 0.5x^3 (1 from x = 1): the most a smoothstep of height 1
/// over a length L changes over any stretch x L (the squeeze envelope's EASE,
/// linegate.mjs squeezeDev, and the jog's).
export const easeG = x => x >= 1 ? 1 : 1.5 * x - 0.5 * x * x * x;
/// The plan's fastest ease: the shortest class taper floor (SmoothRules.TaperFloor).
export const minTaperFloor = R => Math.min(...Object.values(R.TaperFloor));

/// The convex hull of a few points [x, z] (Andrew's monotone chain, counter-
/// clockwise, collinear points dropped; an insertion sort, so the C# port
/// orders them identically).
export function hullOf(pts) {
  const p = pts.slice();
  for (let i = 1; i < p.length; i++) {
    const q = p[i]; let j = i - 1;
    while (j >= 0 && (p[j][0] > q[0] || (p[j][0] === q[0] && p[j][1] > q[1]))) { p[j + 1] = p[j]; j--; }
    p[j + 1] = q;
  }
  const cr = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
  const lo = [], up = [];
  for (let i = 0; i < p.length; i++) { const q = p[i]; while (lo.length >= 2 && cr(lo[lo.length - 2], lo[lo.length - 1], q) <= 0) lo.pop(); lo.push(q); }
  for (let i = p.length - 1; i >= 0; i--) { const q = p[i]; while (up.length >= 2 && cr(up[up.length - 2], up[up.length - 1], q) <= 0) up.pop(); up.push(q); }
  lo.pop(); up.pop();
  return lo.concat(up);
}
/// How far (x, z) stands outside the convex polygon `poly` (hullOf's order);
/// 0 inside. A polygon of one or two points is a point or a segment.
export function outsideOf(poly, x, z) {
  const n = poly.length;
  if (n === 0) return 0;
  if (n === 1) return Math.hypot(poly[0][0] - x, poly[0][1] - z);
  let inside = n >= 3, d = Infinity;
  for (let i = 0; i < n; i++) {
    const a = poly[i], b = poly[(i + 1) % n], sx = b[0] - a[0], sz = b[1] - a[1], L2 = sx * sx + sz * sz;
    if (n >= 3 && sx * (z - a[1]) - sz * (x - a[0]) < -1e-9) inside = false;
    const t = L2 > 1e-12 ? Math.max(0, Math.min(1, ((x - a[0]) * sx + (z - a[1]) * sz) / L2)) : 0;
    const e = Math.hypot(a[0] + sx * t - x, a[1] + sz * t - z);
    if (e < d) d = e;
  }
  return inside ? 0 : d;
}

/// What gave a vertex its B2 score (score.kind[m]), for the run's text.
export const KIND_LONE = 0, KIND_CLUSTER = 1, KIND_HEDGE = 2, KIND_JOG = 3, KIND_ZIG = 4, KIND_OUT = 5;
export const KIND_TEXT = ['a lone corner', 'a corner split over close vertices', 'a hedged corner (turn and turn back; the net corner)',
  'a jog (a sideways step between two straights, faster than the plan\'s ease)', 'a zigzag peak (the line turns back on both sides)',
  'a bump or notch (drawn outside every smooth transition between its straights)'];

/// B2's score at every kept vertex: the worst of its lone facet sagitta (at
/// a zigzag peak twice it), of every cluster it is a corner vertex of, and of
/// every hedged window's net corner, outside distance and jog (the header).
/// X, Z: the kept vertices; C, TH as above; exempt(m): an exempt vertex (X2
/// gore nose, X3 clipped inner edge) scores 0 and no window runs through it.
/// R: SmoothRules (V, ChordCapM, KinkNoiseShare, KinkViewM, PixelAtM,
/// TaperFloor). Returns Float64Array scores, with .kind (Uint8Array) naming
/// the rule that gave each.
export function kinkScores(X, Z, C, TH, R, exempt = null) {
  const n = C.length, V = R.V, cap = R.ChordCapM, share = R.KinkNoiseShare, pMax = V * R.KinkViewM / R.PixelAtM;
  const Lf = minTaperFloor(R);
  const score = new Float64Array(n);
  /// which rule gave each score: KIND_*
  const kind = new Uint8Array(n);
  score.kind = kind;
  const ex = m => exempt !== null && exempt(m);
  for (let m = 1; m + 1 < n; m++) {
    if (ex(m)) continue;
    score[m] = kinkChord(C, TH, m, share, cap) * Math.abs(TH[m]) / 8;
    // a zigzag peak: the chord stops at a turn back on both sides
    const sg = Math.sign(TH[m]), thr = Math.abs(TH[m]) * share;
    if (sg !== 0 && stopSign(C, TH, m, -1, thr, cap) === -sg && stopSign(C, TH, m, 1, thr, cap) === -sg) { score[m] *= 2; kind[m] = KIND_ZIG; }
  }
  const minTurn = 8 * V / cap;   // a corner turning less cannot score V over the whole cap
  /// the score goes to the window's vertices turning at least KinkNoiseShare of its largest turn
  const give = (j, k, s, big, why) => { const corner = share * big; for (let m = j; m <= k; m++) if (Math.abs(TH[m]) >= corner && s > score[m]) { score[m] = s; kind[m] = why; } };
  /// the least distance from (vx, vz) to the drawn segments P[i0]P[i0+1] .. P[i1]P[i1+1], no more than d0
  const pathDist = (i0, i1, vx, vz, d0) => {
    let dr = d0;
    for (let i = i0; i <= i1 && dr > 0; i++) {
      const sx = X[i + 1] - X[i], sz = Z[i + 1] - Z[i], L2 = sx * sx + sz * sz;
      const tt = L2 > 1e-12 ? Math.max(0, Math.min(1, ((vx - X[i]) * sx + (vz - Z[i]) * sz) / L2)) : 0;
      const d = Math.hypot(X[i] + sx * tt - vx, Z[i] + sz * tt - vz);
      if (d < dr) dr = d;
    }
    return dr;
  };
  /// (vx, vz) inside the polygon P[j] .. P[k], closed by its chord (crossing number)
  const insidePoly = (j, k, vx, vz) => {
    let c = false;
    for (let i = j; i <= k; i++) {
      const i2 = i < k ? i + 1 : j, xa = X[i], za = Z[i], xb = X[i2], zb = Z[i2];
      if ((za > vz) !== (zb > vz) && vx < (xb - xa) * (vz - za) / (zb - za) + xa) c = !c;
    }
    return c;
  };
  /// A cluster j..k (same-sign turns, noise the other way inside): its virtual corner and own rounding.
  function cluster(j, k, T, big) {
    const aT = Math.abs(T);
    if (aT <= minTurn || aT >= Math.PI * 0.95) return;
    // the virtual corner: P[j] + a u = P[k] - b w
    let ux = X[j] - X[j - 1], uz = Z[j] - Z[j - 1], wx = X[k + 1] - X[k], wz = Z[k + 1] - Z[k];
    const ul = Math.hypot(ux, uz), wl = Math.hypot(wx, wz);
    if (ul < 1e-9 || wl < 1e-9) return;
    ux /= ul; uz /= ul; wx /= wl; wz /= wl;
    const den = ux * wz - uz * wx;
    if (Math.abs(den) < 1e-9) return;
    const dx = X[k] - X[j], dz = Z[k] - Z[j];
    const a = (dx * wz - dz * wx) / den, b = (ux * dz - uz * dx) / den;
    if (!(a >= 0 && b >= 0)) return;
    const vx = X[j] + ux * a, vz = Z[j] + uz * a;
    // the bound first: f <= cap |T| / 8, and only a gap past max(V, dr) scores past V
    const fMax = cap * aT / 8, dr = pathDist(j, k - 1, vx, vz, Infinity);
    if (dr >= pMax || fMax - dr <= Math.max(V, dr)) return;
    // the chords, from Vc past noise to where the line turns again
    const thr = share * aT;
    const i0 = reach(C, TH, j, -1, thr, cap, a), i1 = reach(C, TH, k, 1, thr, cap, b);
    const f = Math.min(C[j] - C[i0] + a, C[i1] - C[k] + b, cap) * aT / 8;
    const s = (f - dr) * V / Math.max(V, dr);
    if (s > V) give(j, k, s, big, KIND_CLUSTER);
  }
  /// A hedged window j..k (turns of both signs): its net corner; between two straights its drawn corner, the
  /// drawing's outside distance and its jog. exc: the largest heading the line reached, against the approach
  /// line, before k.
  function hedged(j, k, T, big, exc) {
    const w = C[k] - C[j], aT = Math.abs(T);
    const corner = aT > minTurn && aT < Math.PI * 0.95;
    // the net corner, rounded at most as T / 2 at each end of the window (anywhere: a lower bound)
    if (corner) {
      const h = w / 2, dre = h * Math.tan(aT / 2), thr = share * aT;
      if (dre < pMax && cap * aT / 8 - dre > Math.max(V, dre)) {
        const i0 = reach(C, TH, j, -1, thr, cap, h), i1 = reach(C, TH, k, 1, thr, cap, h);
        const f = Math.min(C[j] - C[i0] + h, C[i1] - C[k] + h, cap) * aT / 8;
        const s = (f - dre) * V / Math.max(V, dre);
        if (s > V) give(j, k, s, big, KIND_HEDGE);
      }
    }
    // between two straights: the line does not turn again by KinkNoiseShare of the window's largest heading
    // excursion for min(w / KinkNoiseShare, ChordCapM) on either side, or runs on straight to its end at least w
    // away (never an S-bend or a sampled curve, whose next vertex stops it)
    const thrS = share * Math.max(exc, aT), Ls = Math.min(w / share, cap);
    const o0 = reach(C, TH, j, -1, thrS, cap, 0), o1 = reach(C, TH, k, 1, thrS, cap, 0);
    const l0 = C[j] - C[o0], l1 = C[o1] - C[k];
    if (!((l0 >= Ls || (o0 === 0 && l0 >= w)) && (l1 >= Ls || (o1 === n - 1 && l1 >= w)))) return;
    let ux = X[j] - X[j - 1], uz = Z[j] - Z[j - 1], wx = X[k + 1] - X[k], wz = Z[k + 1] - Z[k];
    const ul = Math.hypot(ux, uz), wl = Math.hypot(wx, wz);
    if (ul < 1e-9 || wl < 1e-9) return;
    ux /= ul; uz /= ul; wx /= wl; wz /= wl;
    const den = ux * wz - uz * wx, dx = X[k] - X[j], dz = Z[k] - Z[j];
    const meet = Math.abs(den) > 1e-9;
    const a = meet ? (dx * wz - dz * wx) / den : 0, b = meet ? (ux * dz - uz * dx) / den : 0;
    const vx = X[j] + ux * a, vz = Z[j] + uz * a;
    // the net corner at the drawn Vc (on the drawn approach, the drawn exit, or between them), its rounding the
    // drawn polygon's, SIGNED: minus the overshoot where the drawing passes outside Vc
    if (corner && meet && -a <= ul && -b <= wl) {
      const dr = k > j + 1 && insidePoly(j, k, vx, vz) ? -pathDist(j, k - 1, vx, vz, Infinity) : pathDist(j - 1, k, vx, vz, (w / 2) * Math.tan(aT / 2));
      if (dr < pMax) {
        const thr = share * aT;
        const i0 = reach(C, TH, j, -1, thr, cap, a), i1 = reach(C, TH, k, 1, thr, cap, b);
        const f = Math.min(C[j] - C[i0] + a, C[i1] - C[k] + b, cap) * aT / 8;
        const s = (f - dr) * V / Math.max(V, dr);
        if (s > V) give(j, k, s, big, KIND_HEDGE);
      }
    }
    // outside: the drawn vertices between j and k against the region every smooth convex transition occupies
    if (k > j + 1) {
      let poly;
      if (meet && a >= 0 && b >= 0) poly = hullOf([[X[j], Z[j]], [vx, vz], [X[k], Z[k]]]);
      else {
        const tk = (X[k] - X[j]) * ux + (Z[k] - Z[j]) * uz, tj = (X[j] - X[k]) * wx + (Z[j] - Z[k]) * wz;
        poly = hullOf([[X[j], Z[j]], [X[k], Z[k]], [X[j] + ux * tk, Z[j] + uz * tk], [X[k] + wx * tj, Z[k] + wz * tj]]);
      }
      let od = 0;
      for (let i = j + 1; i < k; i++) { const o = outsideOf(poly, X[i], Z[i]); if (o > od) od = o; }
      if (od > V) give(j, k, od, big, KIND_OUT);
    }
    // the jog: the part of a step between the two straights that came faster than the plan's fastest ease
    const d = jogGap(X, Z, j, k);
    if (d > V) {
      const e = d * (1 - easeG(w / Lf));
      if (e > V) give(j, k, e, big, KIND_JOG);
    }
  }
  for (let j = 1; j + 2 < n; j++) {
    if (TH[j] === 0 || ex(j)) continue;
    const sg = Math.sign(TH[j]);
    let T = TH[j], big = Math.abs(TH[j]), bigAll = big, exc = big, hedge = false;
    for (let k = j + 1; k + 1 < n && C[k] - C[j] <= cap; k++) {
      if (ex(k)) break;
      const t = TH[k];
      // a turn back of KinkNoiseShare of the running turn or more: no longer one cluster, a hedged window from here on
      if (!hedge && Math.sign(t) !== sg && Math.abs(t) >= share * Math.abs(T)) hedge = true;
      T += t; bigAll = Math.max(bigAll, Math.abs(t));
      if (hedge) hedged(j, k, T, bigAll, exc);
      else if (Math.sign(t) === sg) { big = Math.max(big, Math.abs(t)); cluster(j, k, T, big); }
      exc = Math.max(exc, Math.abs(T));
    }
  }
  return score;
}
