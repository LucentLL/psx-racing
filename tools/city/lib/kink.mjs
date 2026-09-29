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

/// B2's score at every kept vertex: the worst of its lone facet sagitta and
/// of every cluster it is a corner vertex of (the header). X, Z: the kept
/// vertices; C, TH as above; exempt(m): an exempt vertex (X2 gore nose, X3
/// clipped inner edge) scores 0 and no cluster runs through it. R:
/// SmoothRules (V, ChordCapM, KinkNoiseShare, KinkViewM, PixelAtM). Returns
/// Float64Array scores.
export function kinkScores(X, Z, C, TH, R, exempt = null) {
  const n = C.length, V = R.V, cap = R.ChordCapM, share = R.KinkNoiseShare, pMax = V * R.KinkViewM / R.PixelAtM;
  const score = new Float64Array(n);
  const ex = m => exempt !== null && exempt(m);
  for (let m = 1; m + 1 < n; m++) if (!ex(m)) score[m] = kinkChord(C, TH, m, share, cap) * Math.abs(TH[m]) / 8;
  const minTurn = 8 * V / cap;   // a corner turning less cannot score V over the whole cap
  for (let j = 1; j + 2 < n; j++) {
    if (TH[j] === 0 || ex(j)) continue;
    const sg = Math.sign(TH[j]);
    let T = TH[j], big = Math.abs(TH[j]);
    for (let k = j + 1; k + 1 < n && C[k] - C[j] <= cap; k++) {
      if (ex(k)) break;
      const t = TH[k];
      if (Math.sign(t) !== sg) { if (Math.abs(t) < share * Math.abs(T)) { T += t; continue; } break; }
      T += t; big = Math.max(big, Math.abs(t));
      const aT = Math.abs(T);
      if (aT <= minTurn || aT >= Math.PI * 0.95) continue;
      // the virtual corner: P[j] + a u = P[k] - b w
      let ux = X[j] - X[j - 1], uz = Z[j] - Z[j - 1], wx = X[k + 1] - X[k], wz = Z[k + 1] - Z[k];
      const ul = Math.hypot(ux, uz), wl = Math.hypot(wx, wz);
      if (ul < 1e-9 || wl < 1e-9) continue;
      ux /= ul; uz /= ul; wx /= wl; wz /= wl;
      const den = ux * wz - uz * wx;
      if (Math.abs(den) < 1e-9) continue;
      const dx = X[k] - X[j], dz = Z[k] - Z[j];
      const a = (dx * wz - dz * wx) / den, b = (ux * dz - uz * dx) / den;
      if (!(a >= 0 && b >= 0)) continue;
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
      if (dr >= pMax || fMax - dr <= Math.max(V, dr)) continue;
      // the chords, from Vc past noise to where the line turns again
      const thr = share * aT;
      const i0 = reach(C, TH, j, -1, thr, cap, a), i1 = reach(C, TH, k, 1, thr, cap, b);
      const f = Math.min(C[j] - C[i0] + a, C[i1] - C[k] + b, cap) * aT / 8;
      const s = (f - dr) * V / Math.max(V, dr);
      if (s <= V) continue;
      const corner = share * big;
      for (let m = j; m <= k; m++) if (Math.abs(TH[m]) >= corner && s > score[m]) score[m] = s;
    }
  }
  return score;
}
