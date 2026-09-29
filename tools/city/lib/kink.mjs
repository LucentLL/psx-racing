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
// BETWEEN TWO STRAIGHTS - on each side, for Ls = min(w / KinkNoiseShare,
// ChordCapM) (the eye's whole kink chord once the window is a quarter of it),
// the line either stays within V of ONE straight line (straightRun: every
// vertex within V either side of a line parallel to its chord - a single bend
// of up to the lone limit, 1.15 degrees, at the middle of a ChordCapM stretch,
// is still straight: review 5, a legal 0.57 degree vertex 9.4 m before North
// Irwin Avenue's jog switched every rule below off), or does not turn again
// by KinkNoiseShare of the window's largest heading excursion (round 3's
// rule, kept: a big feature's small neighbours are its noise); and either one
// may instead run on to the strand's END, whatever its length (review 5: a
// jog 0.8 m before the fan mouth that trims its ribbon was judged by neither
// gate) - the eye's reference is then exact, the approach line (into j) and
// the exit line (out of k). Then also:
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
//        sideways step, which B4 JUMP fails past V at a node. Drawn over w up
//        to a quarter of ChordCapM (straights of 4w: round 3's rule) it scores
//        d, the whole step (review 5: the ease below, applied there, dropped
//        763 of the city's keys); drawn over more, the part of it that came
//        faster than the plan's fastest ease (plan I7: a smoothstep over the
//        shortest class taper floor, Lf, the street's 15 m), d (1 - g(w /
//        Lf)), g(x) = 1.5x - 0.5x^3 (the squeeze envelope's): an 18.2 cm
//        step over 3.2 m fails, a 3 cm drift over 10 m does not. A taper
//        eased over its floor never fits a ChordCapM window.
// WIDER WINDOWS, up to KinkViewM (review 5: a bump, 14 cm on 4 m rises, or a
// notch like East 12th Street's, a 28.7 cm dip before a 1.9 degree corner,
// passed once it spanned more than the 10 m cap): where the line runs within V
// of one straight line for a whole ChordCapM (or to its end) on both sides of
// a window from the feature's first real turn to its last (each at least
// KinkNoiseShare of its largest), the signed NET CORNER as above, and OUTSIDE
// by the part of the excursion that came faster than the plan's ease (the
// squeeze envelope's EASE on the outside distance: an eased bump, the plan's
// own out-and-back over two floors, reads 0). No jog: a wide S-bend between
// straights is a road's reverse curve, B3's to judge.
// A curve that keeps turning (an S-bend of sampled arcs, a taper eased over
// its floor) stops the straights at its next vertex and gets the net-corner
// lower bound only; B1, B3 and A1 judge it.
//
// A WAVE (review 5: a smooth sideways wave - a sampled sinusoid of +-8 cm
// every 10 m, a zigzag whose peaks are split or flat, a data zigzag WP-11
// filleted into tangent arcs - has no sharp peak, no straights and no
// cluster, and passed every check): the line's LOBES are its runs of turns of
// one sign over its SIGNIFICANT vertices - those turning at least
// KinkNoiseShare of the largest turn within ChordCapM / 2 (a diagonal
// crossing's hundredths of a degree beside a section's turn, or a sampled
// wave's inflection, is no lobe of its own) - consecutive ones at most
// ChordCapM apart. A lobe that the line leaves and enters by turning back
// within ChordCapM - neighbours turning the other way by at least
// KinkNoiseShare of its own turn, as the zigzag peak's rule has it (legs
// longer than the cap are lone corners) - stands off the chord of its two
// INFLECTIONS (midway to each neighbour), and where two or more such lobes
// follow each other that chord is the wave's MEAN
// LINE: the spec's section 2 rule, "symmetric zigzag: peak deviation <= V",
// judges each lobe's deviation from it, over at most ChordCapM (a lobe longer
// than the cap by its worst ChordCapM sub-chord, as the lone rule caps its
// chords). One lobe alone is a bump (OUTSIDE's), two a single S-bend. A lobe
// standing SimplifyEpsM (0.5 m) or more off its mean line is geometry, not a
// wobble: the plan's WP-10 Douglas-Peucker removes every sideways excursion
// under it before WP-11 fillets what is left, so a winding road's lobes pass
// and B3 judges their radius. That test is the DATA line's: a line drawn hw
// off it (an edge, a paint line) has a lobe turning T hw (1 - cos(T / 2))
// shallower on the inside of it, so a lobe that close to the limit is left to
// the centreline's own (halfWidth: the most any line of the strand stands off
// it; 0 for the midline and the data line).
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

/// How far from kept vertex m, going dir (-1: back along the line INTO m; +1:
/// on along the line OUT of m), the line stays within V of ONE straight line -
/// every vertex passed within V either side of a line parallel to the chord
/// from P[m] to the far end (the farthest vertex, or the point exactly cap
/// away; half their spread across it) - and turns, over the vertices passed,
/// by no more than maxTurn (the lone limit, 8V / ChordCapM: a stretch that
/// turns more is a curve, however flat, not the eye's reference). A single
/// bend of 1.15 degrees at the middle of a ChordCapM stretch is just within.
/// Interpolated between the last vertex inside and the first outside;
/// Infinity when it runs so to the strand's end; at most cap. line (optional,
/// 4 numbers): that line, the last one accepted - the unit chord from P[m]
/// towards the far end, the offset of the strip's centre from it along (cz,
/// -cx), and the strip's half-width.
export function straightRun(X, Z, C, TH, m, dir, V, cap, maxTurn, line = null) {
  const n = C.length;
  let prevH = 0, pcx = 0, pcz = 0, pc = 0, turn = 0;
  const done = len => { if (line) { line[0] = pcx; line[1] = pcz; line[2] = pc; line[3] = prevH; } return len; };
  for (let i = m + dir; i >= 0 && i < n; i += dir) {
    const L = Math.abs(C[i] - C[m]), Lp = Math.abs(C[i - dir] - C[m]), cut = L > cap;
    if (i !== m + dir) { turn += TH[i - dir]; if (Math.abs(turn) > maxTurn) return done(Math.min(cap, Lp)); }
    // the far end: vertex i, or the point exactly cap away on the segment into it
    let ex = X[i], ez = Z[i];
    if (cut) { const t = (cap - Lp) / (L - Lp); ex = X[i - dir] + (X[i] - X[i - dir]) * t; ez = Z[i - dir] + (Z[i] - Z[i - dir]) * t; }
    let cx = ex - X[m], cz = ez - Z[m];
    const cl = Math.sqrt(cx * cx + cz * cz);
    let lo = 0, hi = 0;
    if (cl > 1e-9) {
      cx /= cl; cz /= cl;
      for (let q = m + dir; q !== i; q += dir) {
        const d = (X[q] - X[m]) * cz - (Z[q] - Z[m]) * cx;
        if (d < lo) lo = d; else if (d > hi) hi = d;
      }
    } else { cx = 0; cz = 0; }
    const h = (hi - lo) / 2;
    if (h > V) return done(Math.min(cap, Lp + ((cut ? cap : L) - Lp) * (V - prevH) / (h - prevH)));
    pcx = cx; pcz = cz; pc = (lo + hi) / 2; prevH = h;
    if (L >= cap) return done(cap);
  }
  return done(Infinity);
}

/// The least sideways gap, anywhere along the window between the points A
/// and B, between the line through A with unit direction u and the line
/// through B with unit direction w, measured across their mean direction; 0
/// when they cross inside it (the header's jog d).
export function jogLines(ax, az, ux, uz, bx, bz, wx, wz) {
  let mx = ux + wx, mz = uz + wz;
  const ml = Math.hypot(mx, mz);
  if (ml < 1e-9) return 0;
  mx /= ml; mz /= ml;
  const nx = -mz, nz = mx, cu = ux * mx + uz * mz, cw = wx * mx + wz * mz;
  if (cu < 1e-6 || cw < 1e-6) return 0;
  // each line's sideways position where it passes along-distance x; the gap is linear in x
  const x0 = ax * mx + az * mz, x1 = bx * mx + bz * mz;
  const latIn = x => (ax * nx + az * nz) + (x - x0) * (ux * nx + uz * nz) / cu;
  const latOut = x => (bx * nx + bz * nz) + (x - x1) * (wx * nx + wz * nz) / cw;
  const g0 = latOut(x0) - latIn(x0), g1 = latOut(x1) - latIn(x1);
  return g0 * g1 <= 0 ? 0 : Math.min(Math.abs(g0), Math.abs(g1));
}
/// jogLines between the approach line (P[j-1] -> P[j]) and the exit line (P[k] -> P[k+1]).
export function jogGap(X, Z, j, k) {
  const ux = X[j] - X[j - 1], uz = Z[j] - Z[j - 1], wx = X[k + 1] - X[k], wz = Z[k + 1] - Z[k];
  const ul = Math.hypot(ux, uz), wl = Math.hypot(wx, wz);
  if (ul < 1e-9 || wl < 1e-9) return 0;
  return jogLines(X[j], Z[j], ux / ul, uz / ul, X[k], Z[k], wx / wl, wz / wl);
}

/// g(x) = 1.5x - 0.5x^3 (1 from x = 1): the most a smoothstep of height 1
/// over a length L changes over any stretch x L (the squeeze envelope's EASE,
/// linegate.mjs squeezeDev, the jog's and wide OUTSIDE's).
export const easeG = x => x >= 1 ? 1 : 1.5 * x - 0.5 * x * x * x;
/// The plan's fastest ease: the shortest class taper floor (SmoothRules.TaperFloor).
export const minTaperFloor = R => Math.min(...Object.values(R.TaperFloor));

/// The turning points of a signal c (V hysteresis; a plateau's first sample):
/// its indices, the first and the last included (squeezeDev's and wide
/// OUTSIDE's rises and falls).
export function turningPoints(c, V) {
  const m = c.length, tp = [0];
  let mode = 0, cand = 0;
  for (let k = 1; k < m; k++) {
    if (mode === 0) { if (c[k] - c[0] > V) { mode = 1; cand = k; } else if (c[0] - c[k] > V) { mode = -1; cand = k; } continue; }
    if (mode > 0) { if (c[k] > c[cand]) cand = k; else if (c[cand] - c[k] > V) { tp.push(cand); mode = -1; cand = k; } }
    else { if (c[k] < c[cand]) cand = k; else if (c[k] - c[cand] > V) { tp.push(cand); mode = 1; cand = k; } }
  }
  if (mode !== 0 && cand !== 0) tp.push(cand);
  if (tp[tp.length - 1] !== m - 1) tp.push(m - 1);
  return tp;
}
/// EASE: inside each rise or fall of height H > V between turning points tp
/// of the signal c (at arc s), any two samples w < L apart may differ by at
/// most H_L g(w / L) - the most a smoothstep of height H_L over L changes over
/// any w, H_L the LOCAL height (the change across the L-long window centred
/// on the pair, from the last sample at or before its start to the first at
/// or after its end, inside the rise; never more than H). bump(k, e) gets
/// every excess e > 0 at both samples of the pair (squeezeDev's EASE term).
export function easeInto(s, c, tp, L, V, bump) {
  for (let t = 0; t + 1 < tp.length; t++) {
    const a = tp[t], b = tp[t + 1], H = Math.abs(c[b] - c[a]);
    if (H <= V) continue;
    for (let i = a; i < b; i++) {
      // the floor window centred on the pair (i, j): kl the last sample at or before its start, kh the first at or
      // after its end (inside the rise); both only move forward as j does
      let kl = i, kh = i + 1;
      for (let j = i + 1; j <= b && s[j] - s[i] < L; j++) {
        const mid = (s[i] + s[j]) / 2, lo = mid - L / 2, hi = mid + L / 2;
        if (j === i + 1) { while (kl > a && s[kl] > lo) kl--; }
        else while (kl < i && s[kl + 1] <= lo) kl++;
        if (kh < j) kh = j;
        while (kh < b && s[kh] < hi) kh++;
        const HL = Math.min(H, Math.abs(c[kh] - c[kl]));
        const e = Math.abs(c[j] - c[i]) - HL * easeG((s[j] - s[i]) / L);
        if (e > 0) { bump(i, e); bump(j, e); }
      }
    }
  }
}

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
export const KIND_LONE = 0, KIND_CLUSTER = 1, KIND_HEDGE = 2, KIND_JOG = 3, KIND_ZIG = 4, KIND_OUT = 5, KIND_WAVE = 6;
export const KIND_TEXT = ['a lone corner', 'a corner split over close vertices', 'a hedged corner (turn and turn back; the net corner)',
  'a jog (a sideways step between two straights)', 'a zigzag peak (the line turns back on both sides)',
  'a bump or notch (drawn outside every smooth transition between its straights)',
  'a wave (lobes turning back on both sides, off their mean line)'];

/// B2's score at every kept vertex: the worst of its lone facet sagitta (at
/// a zigzag peak twice it), of every cluster it is a corner vertex of, of
/// every hedged window's net corner, outside distance and jog, and of every
/// wave lobe's deviation from its mean line (the header). X, Z: the kept
/// vertices; C, TH as above; exempt(m): an exempt vertex (X2 gore nose, X3
/// clipped inner edge) scores 0 and no window or lobe runs through it. R:
/// SmoothRules (V, ChordCapM, KinkNoiseShare, KinkViewM, PixelAtM,
/// TaperFloor, SimplifyEpsM). halfWidth: how far the line may stand off the
/// data line (the WAVE rule's geometry test). Returns Float64Array scores,
/// with .kind (Uint8Array) naming the rule that gave each.
export function kinkScores(X, Z, C, TH, R, exempt = null, halfWidth = 0) {
  const n = C.length, V = R.V, cap = R.ChordCapM, share = R.KinkNoiseShare, view = R.KinkViewM, pMax = V * view / R.PixelAtM;
  const Lf = minTaperFloor(R), eps = R.SimplifyEpsM;
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
  /// how far the line runs within V of one straight line into m (back) and out of m (on), and that line:
  /// straightRun, once each
  const runB = new Float64Array(n).fill(-1), runF = new Float64Array(n).fill(-1), lineB = new Float64Array(4 * n), lineF = new Float64Array(4 * n);
  const tmp = [0, 0, 0, 0];
  const back = m => { if (runB[m] < 0) { runB[m] = straightRun(X, Z, C, TH, m, -1, V, cap, minTurn, tmp); for (let q = 0; q < 4; q++) lineB[4 * m + q] = tmp[q]; } return runB[m]; };
  const on = m => { if (runF[m] < 0) { runF[m] = straightRun(X, Z, C, TH, m, 1, V, cap, minTurn, tmp); for (let q = 0; q < 4; q++) lineF[4 * m + q] = tmp[q]; } return runF[m]; };
  /// no vertex within the cap from m (going dir) turns thr or more
  const quiet = (m, dir, thr) => { for (let i = m + dir; i > 0 && i < n - 1 && Math.abs(C[i] - C[m]) < cap; i += dir) if (Math.abs(TH[i]) >= thr) return false; return true; };
  /// A reference line: a point on it, its unit direction of travel, how far back (or on) it holds, and how far
  /// the drawn line strays either side of it there (h). The drawn segment into j (dir -1) or out of k (dir +1);
  /// null when degenerate.
  const segLine = (m, dir) => {
    const ux = dir < 0 ? X[m] - X[m - 1] : X[m + 1] - X[m], uz = dir < 0 ? Z[m] - Z[m - 1] : Z[m + 1] - Z[m], l = Math.hypot(ux, uz);
    return l < 1e-9 ? null : { px: X[m], pz: Z[m], ux: ux / l, uz: uz / l, len: l, h: 0 };
  };
  /// the straight line the line runs within V of into m (dir -1) or out of m (+1): straightRun's strip centre
  const fitLine = (m, dir) => {
    const L = dir < 0 ? lineB : lineF, cx = L[4 * m], cz = L[4 * m + 1], c = L[4 * m + 2];
    if (cx === 0 && cz === 0) return null;
    return { px: X[m] + cz * c, pz: Z[m] - cx * c, ux: dir < 0 ? -cx : cx, uz: dir < 0 ? -cz : cz, len: Math.min(dir < 0 ? runB[m] : runF[m], cap), h: L[4 * m + 3] };
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
  /// drawing's outside distance and (up to ChordCapM) its jog. exc: the largest heading the line reached, against
  /// the approach line, before k. A window wider than ChordCapM (up to KinkViewM) only from the feature's first real
  /// turn to its last, between straights of a whole ChordCapM (the caller checked the one into j). A side that is
  /// straight by round 3's rule is judged from the drawn segment (as it always was); one that runs within V of one
  /// straight line from that line (straightRun's strip centre: a zigzag of under V either side of its mean line is
  /// judged against the mean, not against one of its legs); the worst of the two.
  function hedged(j, k, T, big, exc) {
    const w = C[k] - C[j], aT = Math.abs(T), wide = w > cap;
    const corner = aT > minTurn && aT < Math.PI * 0.95;
    // the net corner, rounded at most as T / 2 at each end of the window (anywhere: a lower bound)
    if (corner && !wide) {
      const h = w / 2, dre = h * Math.tan(aT / 2), thr = share * aT;
      if (dre < pMax && cap * aT / 8 - dre > Math.max(V, dre)) {
        const i0 = reach(C, TH, j, -1, thr, cap, h), i1 = reach(C, TH, k, 1, thr, cap, h);
        const f = Math.min(C[j] - C[i0] + h, C[i1] - C[k] + h, cap) * aT / 8;
        const s = (f - dre) * V / Math.max(V, dre);
        if (s > V) give(j, k, s, big, KIND_HEDGE);
      }
    }
    // between two straights
    let oldA = false, oldB = false, newA, newB;
    if (wide) {
      // the whole feature: no vertex within the cap either side turns KinkNoiseShare of its largest turn (a window
      // ending inside an eased transition's tail - still turning - is not the feature; the one that takes the tail in is)
      const thrF = share * big;
      if (Math.abs(TH[j]) < thrF || Math.abs(TH[k]) < thrF || !quiet(j, -1, thrF) || !quiet(k, 1, thrF)) return;
      newA = true; newB = on(k) >= cap;
      if (!newB) return;
    } else {
      const Ls = Math.min(w / share, cap), thrS = share * Math.max(exc, aT);
      const o0 = reach(C, TH, j, -1, thrS, cap, 0), o1 = reach(C, TH, k, 1, thrS, cap, 0);
      oldA = C[j] - C[o0] >= Ls || o0 === 0; oldB = C[o1] - C[k] >= Ls || o1 === n - 1;
      newA = back(j) >= Ls; newB = on(k) >= Ls;
      if (!(oldA || newA) || !(oldB || newB)) return;
    }
    const sa = segLine(j, -1), sb = segLine(k, 1);
    if (oldA && oldB && sa && sb) between(j, k, Math.abs(T), big, wide, sa, sb);
    if (newA || newB) {
      const la = newA ? fitLine(j, -1) : sa, lb = newB ? fitLine(k, 1) : sb;
      if (la && lb) between(j, k, Math.abs(Math.atan2(la.ux * lb.uz - la.uz * lb.ux, la.ux * lb.ux + la.uz * lb.uz)), big, wide, la, lb);
    }
  }
  /// The window j..k between the approach line A (into j) and the exit line B (out of k), which turn aT between
  /// them: the net corner where they meet, the drawing's outside distance, and (up to ChordCapM) its jog - each
  /// less the lines' own spread (A.h + B.h: a line only known to within its strip is no sharper reference).
  function between(j, k, aT, big, wide, A, B) {
    const w = C[k] - C[j], slop = A.h + B.h;
    const corner = aT > minTurn && aT < Math.PI * 0.95;
    const ux = A.ux, uz = A.uz, wx = B.ux, wz = B.uz;
    const den = ux * wz - uz * wx, dx = B.px - A.px, dz = B.pz - A.pz;
    const meet = Math.abs(den) > 1e-9;
    const a = meet ? (dx * wz - dz * wx) / den : 0, b = meet ? (ux * dz - uz * dx) / den : 0;
    const vx = A.px + ux * a, vz = A.pz + uz * a;
    // the net corner at Vc (on the approach, the exit, or between them), its rounding the drawn polygon's, SIGNED:
    // minus the overshoot where the drawing passes outside Vc
    if (corner && meet && -a <= A.len && -b <= B.len) {
      const dr = k > j + 1 && insidePoly(j, k, vx, vz) ? -pathDist(j, k - 1, vx, vz, Infinity) : pathDist(j - 1, k, vx, vz, (w / 2) * Math.tan(aT / 2));
      if (dr < pMax) {
        const thr = share * aT;
        const i0 = reach(C, TH, j, -1, thr, cap, a), i1 = reach(C, TH, k, 1, thr, cap, b);
        const f = Math.min(C[j] - C[i0] + a, C[i1] - C[k] + b, cap) * aT / 8;
        const s = (f - dr) * V / Math.max(V, dr) - slop;
        if (s > V) give(j, k, s, big, KIND_HEDGE);
      }
    }
    // outside: the drawn vertices between j and k against the region every smooth convex transition occupies;
    // wider than the cap, the part of it that came faster than the plan's ease
    if (k > j + 1) {
      let poly;
      if (meet && a >= 0 && b >= 0) poly = hullOf([[A.px, A.pz], [vx, vz], [B.px, B.pz]]);
      else {
        const tk = (B.px - A.px) * ux + (B.pz - A.pz) * uz, tj = (A.px - B.px) * wx + (A.pz - B.pz) * wz;
        poly = hullOf([[A.px, A.pz], [B.px, B.pz], [A.px + ux * tk, A.pz + uz * tk], [B.px + wx * tj, B.pz + wz * tj]]);
      }
      const m = k - j + 1, od = new Float64Array(m);
      let odMax = 0;
      for (let i = 1; i + 1 < m; i++) { od[i] = outsideOf(poly, X[j + i], Z[j + i]); if (od[i] > odMax) odMax = od[i]; }
      if (odMax - slop > V) {
        if (!wide) give(j, k, odMax - slop, big, KIND_OUT);
        else {
          let e = 0;
          easeInto(C.subarray ? C.subarray(j, k + 1) : C.slice(j, k + 1), od, turningPoints(od, V), Lf, V, (q, x) => { if (x > e) e = x; });
          if (e - slop > V) give(j, k, e - slop, big, KIND_OUT);
        }
      }
    }
    // the jog: a step between the two straights - the whole of it over a quarter of the cap or less, else the part
    // that came faster than the plan's fastest ease
    if (!wide) {
      const d = jogLines(A.px, A.pz, ux, uz, B.px, B.pz, wx, wz) - slop;
      if (d > V) {
        const e = w <= cap * share ? d : d * (1 - easeG(w / Lf));
        if (e > V) give(j, k, e, big, KIND_JOG);
      }
    }
  }
  for (let j = 1; j + 2 < n; j++) {
    if (TH[j] === 0 || ex(j)) continue;
    const sg = Math.sign(TH[j]);
    let T = TH[j], big = Math.abs(TH[j]), bigAll = big, exc = big, hedge = false, wideOk = -1;
    for (let k = j + 1; k + 1 < n; k++) {
      const w = C[k] - C[j];
      if (w > cap) {
        // wider windows only where the line runs within V of one straight line for the whole cap into j
        if (w > view) break;
        if (wideOk < 0) wideOk = back(j) >= cap ? 1 : 0;
        if (!wideOk) break;
      }
      if (ex(k)) break;
      const t = TH[k];
      // a turn back of KinkNoiseShare of the running turn or more: no longer one cluster, a hedged window from here on
      if (!hedge && Math.sign(t) !== sg && Math.abs(t) >= share * Math.abs(T)) hedge = true;
      T += t; bigAll = Math.max(bigAll, Math.abs(t));
      if (hedge) hedged(j, k, T, bigAll, exc);
      else if (w <= cap && Math.sign(t) === sg) { big = Math.max(big, Math.abs(t)); cluster(j, k, T, big); }
      exc = Math.max(exc, Math.abs(T));
    }
  }
  waves(X, Z, C, TH, share, cap, V, eps, halfWidth, ex, score, kind);
  return score;
}

/// The header's WAVE rule, into score / kind.
function waves(X, Z, C, TH, share, cap, V, eps, hw, ex, score, kind) {
  const n = C.length;
  // the significant vertices: turning at least KinkNoiseShare of the largest turn within ChordCapM / 2 - a diagonal
  // crossing's hundredths of a degree beside a section's turn, or a sampled wave's inflection, is not a lobe of its own
  const sig = new Uint8Array(n);
  for (let m = 1; m + 1 < n; m++) {
    if (ex(m) || TH[m] === 0) continue;
    let big = 0;
    for (let i = m; i > 0 && C[m] - C[i] <= cap / 2; i--) if (!ex(i) && Math.abs(TH[i]) > big) big = Math.abs(TH[i]);
    for (let i = m + 1; i + 1 < n && C[i] - C[m] <= cap / 2; i++) if (!ex(i) && Math.abs(TH[i]) > big) big = Math.abs(TH[i]);
    if (Math.abs(TH[m]) >= share * big) sig[m] = 1;
  }
  // lobes: runs of significant vertices turning one way, consecutive ones at most ChordCapM apart; LJ: the line
  // enters the lobe from the one before it by turning back within ChordCapM (only insignificant vertices, none
  // exempt, between: a zigzag peak's turn back, as its rule has it - legs longer than the cap are lone corners)
  const LA = [], LB = [], LT = [], LG = [], LJ = [];
  let broken = true;
  for (let m = 1; m + 1 < n; m++) {
    if (ex(m)) { broken = true; continue; }
    if (!sig[m]) continue;
    const q = LA.length - 1;
    if (q >= 0 && !broken && Math.sign(TH[m]) === Math.sign(LT[q]) && C[m] - C[LB[q]] <= cap) {
      LB[q] = m; LT[q] += TH[m]; if (Math.abs(TH[m]) > LG[q]) LG[q] = Math.abs(TH[m]);
    } else { LJ.push(q >= 0 && !broken && Math.sign(TH[m]) !== Math.sign(LT[q]) && C[m] - C[LB[q]] <= cap); LA.push(m); LB.push(m); LT.push(TH[m]); LG.push(Math.abs(TH[m])); }
    broken = false;
  }
  const L = LA.length;
  if (L < 4) return;   // a wave needs two lobes, each with a neighbour on both sides
  /// lobe t, which the line leaves and enters by turning back: both neighbours turn the other way, by KinkNoiseShare of its turn
  const turnsBack = t => t > 0 && t + 1 < L && LJ[t] && LJ[t + 1] && Math.abs(LT[t - 1]) >= share * Math.abs(LT[t]) && Math.abs(LT[t + 1]) >= share * Math.abs(LT[t]);
  const at = s => {   // the point at arc s
    let lo = 0, hi = n - 1;
    while (hi - lo > 1) { const md = (lo + hi) >> 1; if (C[md] <= s) lo = md; else hi = md; }
    const l = C[hi] - C[lo], t = l > 1e-12 ? (s - C[lo]) / l : 0;
    return [X[lo] + (X[hi] - X[lo]) * t, Z[lo] + (Z[hi] - Z[lo]) * t];
  };
  /// the most the vertices lo..hi stand off the chord p -> q, on the lobe's outer side (sgn: -1 for a lobe turning +)
  const dev = (p, q, lo, hi, sgn) => {
    let cx = q[0] - p[0], cz = q[1] - p[1];
    const cl = Math.sqrt(cx * cx + cz * cz);
    if (cl < 1e-9) return 0;
    cx /= cl; cz /= cl;
    let d = 0;
    for (let v = lo; v <= hi; v++) { const e = sgn * (cx * (Z[v] - p[1]) - cz * (X[v] - p[0])); if (e > d) d = e; }
    return d;
  };
  for (let t = 1; t + 1 < L; t++) {
    if (!turnsBack(t) || !(turnsBack(t - 1) || turnsBack(t + 1))) continue;
    const a = LA[t], b = LB[t], sgn = LT[t] > 0 ? -1 : 1;
    // the mean line: the chord of the lobe's inflections, midway between it and each neighbour
    const s0 = (C[LB[t - 1]] + C[a]) / 2, s1 = (C[b] + C[LA[t + 1]]) / 2;
    const lo0 = LB[t - 1] + 1, hi0 = LA[t + 1] - 1;
    const D = dev(at(s0), at(s1), lo0, hi0, sgn);
    // geometry, not a wobble: the data line's lobe stands SimplifyEpsM off (this line's, less its offset's shrink)
    if (D <= V || D + hw * (1 - Math.cos(Math.abs(LT[t]) / 2)) >= eps) continue;
    let sc = D;
    if (s1 - s0 > cap) {
      // over at most ChordCapM: the worst sub-chord of the cap's length, one centred on each vertex (kept inside)
      sc = 0;
      for (let v = lo0; v <= hi0; v++) {
        const t0 = Math.max(s0, Math.min(s1 - cap, C[v] - cap / 2)), t1 = t0 + cap;
        let lo = lo0; while (lo <= hi0 && C[lo] <= t0) lo++;
        let hi = hi0; while (hi >= lo0 && C[hi] >= t1) hi--;
        if (lo > hi) continue;
        const e = dev(at(t0), at(t1), lo, hi, sgn);
        if (e > sc) sc = e;
      }
    }
    if (!(sc > V)) continue;
    const corner = share * LG[t];
    for (let v = a; v <= b; v++) if (Math.abs(TH[v]) >= corner && sc > score[v]) { score[v] = sc; kind[v] = KIND_WAVE; }
  }
}
