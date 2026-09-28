// kink.mjs - B2 KINK's chord, shared by the gate (linegate.mjs) and metrics'
// SMOOTH section (smooth.mjs); Editor/CitySmooth.cs KinkChord is the same.
//
// The facet sagitta f = min(c-, c+, ChordCapM) * |turn| / 8 (gate spec 4.2).
// c- and c+ run from the vertex to the nearest kept vertex on each side that
// turns at least KinkNoiseShare of this vertex's own turn: a neighbour turning
// less is noise - the bisector pinch, a diagonal crossing or a section turning
// a few hundredths of a degree next to a real kink - and must not shorten the
// chord, or a lone 3-6 degree kink with a section 1.8 m away scores under V.
// On a smoothly sampled curve two neighbouring turns share a chord
// (turn = (c- + c+) / 2R), so a real neighbour is never that much smaller and
// the formula stays the facet sagitta the spec calibrated.
//
//   C   arc length at each kept vertex (C[0] = 0)
//   TH  signed turn at each kept vertex (radians; the ends are unused)
//   m   the interior kept vertex
export function kinkChord(C, TH, m, share, cap) {
  const n = C.length, thr = Math.abs(TH[m]) * share;
  let j = m - 1;
  while (j > 0 && Math.abs(TH[j]) < thr && C[m] - C[j] < cap) j--;
  let k = m + 1;
  while (k < n - 1 && Math.abs(TH[k]) < thr && C[k] - C[m] < cap) k++;
  return Math.min(C[m] - C[j], C[k] - C[m], cap);
}
