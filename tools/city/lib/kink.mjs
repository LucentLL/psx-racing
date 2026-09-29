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
// A HEDGED CORNER: a turn followed within a metre or two by a turn back of a
// quarter of it or more - [6, -2.4] degrees 0.8-1.2 m apart, a net 3.6 degree
// corner - stops every chord at the counter-turn and breaks the cluster, so
// its parts score centimetres while the eye sees the NET turn (a lone 3.6
// degree vertex scores 3.1x). The painted lines do this at nearly every
// section kink: the diagonal crossing lands a few cm from the section and
// turns back. So every window j..k of at most ChordCapM that holds turns of
// BOTH signs (a counter-turn of at least KinkNoiseShare of the running turn:
// smaller ones are the cluster's noise) is also judged by its net turn T:
//   dr   the rounding of the ROUNDEST drawing of T inside the window's span w,
//        T / 2 at each end: (w / 2) tan(|T| / 2). The drawn polygon, with its
//        counter-turns, is less round, so the score is a lower bound on how
//        sharp the corner reads;
//   f    min(c-, c+, ChordCapM) * |T| / 8, the chords from the window's middle
//        (w / 2, plus the chord from each end to where the line turns again
//        by KinkNoiseShare of T);
// scored as a cluster is: (f - dr) * V / max(V, dr) while dr < pMax. On a
// sampled curve the chords stop at the next vertex, so a window's f never
// outgrows its dr (an S-bend's inflection scores nothing).
//
// A JOG: the same window, when its approach line (into j) and exit line (out
// of k) are parallel or meet outside it. [+3, -3] degrees 0.6 m apart steps
// the line 3.1 cm sideways: no net corner, each part scores 0.4 cm, and B1's
// circle over +-2 m halves a step. B4 JUMP fails a sideways step past V where
// a line continues across a node; drawn over a short transition it is the
// same fault (at 6 m, 0.6 m of road is 7 of the 240 rows: a 1.2 px jog in a
// straight line, and the eye's vernier acuity reads an offset between two
// aligned lines far below a pixel). So the window's
//   d    least sideways gap between the approach and exit lines anywhere
//        along the window (0 when they cross inside it: a corner, not a step)
// scores d when the line runs straight on both sides for at least w /
// KinkNoiseShare (the chord from each end to where the line turns again by
// KinkNoiseShare of the window's largest heading excursion): a step between
// two straights. A curve that keeps turning (an S-bend of sampled arcs, a
// taper eased over its floor) stops those chords at its next vertex and is
// never a jog; B1, B3 and A1 judge it.
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

/// What gave a vertex its B2 score (score.kind[m]), for the run's text.
export const KIND_LONE = 0, KIND_CLUSTER = 1, KIND_HEDGE = 2, KIND_JOG = 3;
export const KIND_TEXT = ['a lone corner', 'a corner split over close vertices', 'a hedged corner (turn and turn back; the net corner)', 'a jog (a sideways step between two straights)'];

/// B2's score at every kept vertex: the worst of its lone facet sagitta, of
/// every cluster it is a corner vertex of, and of every hedged window's net
/// corner and jog (the header). X, Z: the kept vertices; C, TH as above;
/// exempt(m): an exempt vertex (X2 gore nose, X3 clipped inner edge) scores 0
/// and no window runs through it. R: SmoothRules (V, ChordCapM,
/// KinkNoiseShare, KinkViewM, PixelAtM). Returns Float64Array scores, with
/// .kind (Uint8Array) naming the rule that gave each.
export function kinkScores(X, Z, C, TH, R, exempt = null) {
  const n = C.length, V = R.V, cap = R.ChordCapM, share = R.KinkNoiseShare, pMax = V * R.KinkViewM / R.PixelAtM;
  const score = new Float64Array(n);
  /// which rule gave each score: KIND_LONE, KIND_CLUSTER, KIND_HEDGE (a hedged window's net corner) or KIND_JOG
  const kind = new Uint8Array(n);
  score.kind = kind;
  const ex = m => exempt !== null && exempt(m);
  for (let m = 1; m + 1 < n; m++) if (!ex(m)) score[m] = kinkChord(C, TH, m, share, cap) * Math.abs(TH[m]) / 8;
  const minTurn = 8 * V / cap;   // a corner turning less cannot score V over the whole cap
  /// the score goes to the window's vertices turning at least KinkNoiseShare of its largest turn
  const give = (j, k, s, big, why) => { const corner = share * big; for (let m = j; m <= k; m++) if (Math.abs(TH[m]) >= corner && s > score[m]) { score[m] = s; kind[m] = why; } };
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
    const fMax = cap * aT / 8;
    let dr = Infinity;
    for (let i = j; i < k && dr > 0; i++) {
      const sx = X[i + 1] - X[i], sz = Z[i + 1] - Z[i], L2 = sx * sx + sz * sz;
      const tt = L2 > 1e-12 ? Math.max(0, Math.min(1, ((vx - X[i]) * sx + (vz - Z[i]) * sz) / L2)) : 0;
      const d = Math.hypot(X[i] + sx * tt - vx, Z[i] + sz * tt - vz);
      if (d < dr) dr = d;
    }
    if (dr >= pMax || fMax - dr <= Math.max(V, dr)) return;
    // the chords, from Vc past noise to where the line turns again
    const thr = share * aT;
    const i0 = reach(C, TH, j, -1, thr, cap, a), i1 = reach(C, TH, k, 1, thr, cap, b);
    const f = Math.min(C[j] - C[i0] + a, C[i1] - C[k] + b, cap) * aT / 8;
    const s = (f - dr) * V / Math.max(V, dr);
    if (s > V) give(j, k, s, big, KIND_CLUSTER);
  }
  /// A hedged window j..k (turns of both signs): its net corner, and its jog.
  /// exc: the largest heading the line reached, against the approach line, before k.
  function hedged(j, k, T, big, exc) {
    const w = C[k] - C[j], aT = Math.abs(T);
    if (aT > minTurn && aT < Math.PI * 0.95) {
      const h = w / 2, dre = h * Math.tan(aT / 2), thr = share * aT;
      // the net corner, rounded at most as T / 2 at each end of the window
      if (dre < pMax && cap * aT / 8 - dre > Math.max(V, dre)) {
        const i0 = reach(C, TH, j, -1, thr, cap, h), i1 = reach(C, TH, k, 1, thr, cap, h);
        const f = Math.min(C[j] - C[i0] + h, C[i1] - C[k] + h, cap) * aT / 8;
        const s = (f - dre) * V / Math.max(V, dre);
        if (s > V) give(j, k, s, big, KIND_HEDGE);
      }
      // between two straights (each at least w / KinkNoiseShare long: never an S-bend, whose chords stop at its next
      // vertex) the corner is where the approach and exit lines meet, Vc, and its rounding is the drawn polygon's
      // (never more than the roundest drawing's): a counter-turn that overshoots Vc leaves the corner sharp
      const o0 = reach(C, TH, j, -1, thr, cap, 0), o1 = reach(C, TH, k, 1, thr, cap, 0);
      if (Math.min(C[j] - C[o0], C[o1] - C[k], cap) * share >= w) {
        let ux = X[j] - X[j - 1], uz = Z[j] - Z[j - 1], wx = X[k + 1] - X[k], wz = Z[k + 1] - Z[k];
        const ul = Math.hypot(ux, uz), wl = Math.hypot(wx, wz);
        const den = ul > 1e-9 && wl > 1e-9 ? (ux * wz - uz * wx) / (ul * wl) : 0;
        if (Math.abs(den) > 1e-9) {
          ux /= ul; uz /= ul; wx /= wl; wz /= wl;
          const dx = X[k] - X[j], dz = Z[k] - Z[j];
          const a = (dx * wz - dz * wx) / den, b = (ux * dz - uz * dx) / den;
          if (-a <= ul && -b <= wl) {   // Vc on the drawn approach, the drawn exit, or between them
            const vx = X[j] + ux * a, vz = Z[j] + uz * a;
            let dr = dre;
            for (let i = j - 1; i <= k && dr > 0; i++) {
              const sx = X[i + 1] - X[i], sz = Z[i + 1] - Z[i], L2 = sx * sx + sz * sz;
              const tt = L2 > 1e-12 ? Math.max(0, Math.min(1, ((vx - X[i]) * sx + (vz - Z[i]) * sz) / L2)) : 0;
              const d = Math.hypot(X[i] + sx * tt - vx, Z[i] + sz * tt - vz);
              if (d < dr) dr = d;
            }
            if (dr < pMax) {
              const i0 = reach(C, TH, j, -1, thr, cap, a), i1 = reach(C, TH, k, 1, thr, cap, b);
              const f = Math.min(C[j] - C[i0] + a, C[i1] - C[k] + b, cap) * aT / 8;
              const s = (f - dr) * V / Math.max(V, dr);
              if (s > V) give(j, k, s, big, KIND_HEDGE);
            }
          }
        }
      }
    }
    // the jog: a step between two straights
    const d = jogGap(X, Z, j, k);
    if (d > V) {
      const thr = share * Math.max(exc, aT);
      const i0 = reach(C, TH, j, -1, thr, cap, 0), i1 = reach(C, TH, k, 1, thr, cap, 0);
      if (Math.min(C[j] - C[i0], C[i1] - C[k], cap) * share >= w) give(j, k, d, big, KIND_JOG);
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
