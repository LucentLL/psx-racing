// bench.mjs - find a road's bench in the 1 m lidar ground.
//
// A mountain road is a shelf cut across a slope: seven to nine metres of
// nearly level ground (pavement and shoulders) between a cut face going up and
// a fill slope going down. In a 1 m bare-earth DEM that shelf is plain to see
// in a cross-section, and its middle is where the road really is.
//
//   ribbon(wp, loop, sampleXZ)   cross-sections at every waypoint: heights at
//                                -30..+30 m across the line, every 0.5 m
//                                (positive = left of the direction of travel)
//   findBench(rib, {skip})       per station: the shelf nearest the line, its
//                                centre offset and width, a confidence flag,
//                                the offsets filtered along the road, and the
//                                road height on the shelf
//
// Where the ground either side is as level as the road (a valley floor, a
// summit car park) there is no shelf to find and the station is marked not
// confident: the line is left where it was and only the height is read.

export const RIBBON = { halfM: 30, stepM: 0.5 };

export function tangents(wp, loop) {
  const n = wp.length, tx = new Float64Array(n), tz = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    const a = wp[loop ? (i - 1 + n) % n : Math.max(0, i - 1)], b = wp[loop ? (i + 1) % n : Math.min(n - 1, i + 1)];
    const L = Math.hypot(b.x - a.x, b.z - a.z) || 1;
    tx[i] = (b.x - a.x) / L; tz[i] = (b.z - a.z) / L;
  }
  return { tx, tz };
}

export function ribbon(wp, loop, sampleXZ, { halfM = RIBBON.halfM, stepM = RIBBON.stepM } = {}) {
  const n = wp.length, K0 = Math.round(halfM / stepM), K = 2 * K0 + 1;
  const { tx, tz } = tangents(wp, loop);
  const nx = new Float64Array(n), nz = new Float64Array(n);
  const H = new Float32Array(n * K);
  for (let i = 0; i < n; i++) {
    nx[i] = -tz[i]; nz[i] = tx[i];                       // left normal
    for (let k = 0; k < K; k++) {
      const t = (k - K0) * stepM;
      H[i * K + k] = sampleXZ(wp[i].x + nx[i] * t, wp[i].z + nz[i] * t);
    }
  }
  return { n, K, K0, stepM, halfM, H, nx, nz, tx, tz, loop };
}

export function gaussSmooth(a, sigma, loop = false) {
  const n = a.length, out = new Float64Array(n);
  if (sigma <= 0) { out.set(a); return out; }
  const win = Math.ceil(sigma * 3);
  const w = []; for (let o = -win; o <= win; o++) w.push(Math.exp(-(o * o) / (2 * sigma * sigma)));
  for (let i = 0; i < n; i++) {
    let sw = 0, sh = 0;
    for (let o = -win; o <= win; o++) {
      let j = i + o;
      if (loop) j = ((j % n) + n) % n; else if (j < 0 || j >= n) continue;
      const v = a[j]; if (!Number.isFinite(v)) continue;
      sw += w[o + win]; sh += w[o + win] * v;
    }
    out[i] = sw > 0 ? sh / sw : NaN;
  }
  return out;
}

const median = a => { const s = a.slice().sort((p, q) => p - q); return s[s.length >> 1]; };

/// Fill non-finite values of `a` by straight lines between finite neighbours.
/// On a loop the run that crosses the seam (a bridge over the start line) is
/// filled from the last finite value round to the first.
export function fillLinear(a, loop = false) {
  const n = a.length; let last = -1, first = -1;
  for (let i = 0; i < n; i++) {
    if (!Number.isFinite(a[i])) continue;
    if (first < 0) first = i;
    else if (i - last > 1) for (let j = last + 1; j < i; j++) a[j] = a[last] + (a[i] - a[last]) * (j - last) / (i - last);
    last = i;
  }
  if (first < 0) return a;
  const gap = n - last + first;                          // steps from the last finite value round to the first
  for (let j = last + 1; j < n; j++) a[j] = loop && gap > 1 ? a[last] + (a[first] - a[last]) * (j - last) / gap : a[last];
  for (let j = 0; j < first; j++) a[j] = loop && gap > 1 ? a[last] + (a[first] - a[last]) * (n - last + j) / gap : a[first];
  return a;
}

/// opts: maxCross (cross slope a road can have, 12%), minWidthM / maxWidthM
/// (a shelf narrower is a ditch or a path, wider is open flat ground),
/// reachM (how far from the line a shelf may start: the mapped line is nearly
/// always ON the shelf, so a shelf that begins further off is another road, a
/// driveway or the next leg of a hairpin), maxOffM (how far its middle may
/// be), skip(i) -> true on a bridge or in a tunnel (no ground to read).
export function findBench(rib, { maxCross = 0.12, minWidthM = 4.5, maxWidthM = 18, reachM = 3, maxOffM = 10, skip = () => false } = {}) {
  const { n, K, K0, stepM, H, loop } = rib;
  const off = new Float64Array(n).fill(NaN), width = new Float64Array(n).fill(NaN), conf = new Uint8Array(n);
  const b = Math.round(1 / stepM);                       // slope over +-1 m
  for (let i = 0; i < n; i++) {
    if (skip(i)) continue;
    const row = i * K;
    let best = null, k0 = -1;
    for (let k = b; k <= K - b; k++) {
      const low = k < K - b && Math.abs(H[row + k + b] - H[row + k - b]) / (2 * b * stepM) < maxCross;
      if (low && k0 < 0) k0 = k;
      if (!low && k0 >= 0) {
        const a = (k0 - K0) * stepM, z = (k - 1 - K0) * stepM, w = z - a;
        if (w >= minWidthM) {
          const d = a <= 0 && z >= 0 ? 0 : Math.min(Math.abs(a), Math.abs(z));
          if (!best || d < best.d) best = { a, z, w, d };
        }
        k0 = -1;
      }
    }
    if (!best || best.d > reachM || Math.abs(best.a + best.z) / 2 > maxOffM) continue;
    off[i] = (best.a + best.z) / 2; width[i] = best.w + 2 * b * stepM * 0.5;   // the slope window eats ~0.5 m off each edge
    conf[i] = best.w <= maxWidthM ? 1 : 0;
    if (!conf[i]) off[i] = NaN;
  }
  // along the road: a running median over +-5 stations, then outliers dropped
  const med = new Float64Array(n).fill(NaN);
  for (let i = 0; i < n; i++) {
    const v = [];
    for (let o = -5; o <= 5; o++) {
      let j = i + o; if (loop) j = ((j % n) + n) % n; else if (j < 0 || j >= n) continue;
      if (conf[j]) v.push(off[j]);
    }
    if (v.length >= 5) med[i] = median(v);
  }
  for (let i = 0; i < n; i++) if (conf[i] && !(Math.abs(off[i] - med[i]) <= 2)) conf[i] = 0;
  const offF0 = Float64Array.from(med);
  fillLinear(offF0);
  for (let i = 0; i < n; i++) if (!Number.isFinite(offF0[i])) offF0[i] = 0;
  const offF = gaussSmooth(offF0, 2, loop);
  // the road height: the median of the ground within 1.5 m of the shelf's middle
  const hRoad = new Float64Array(n).fill(NaN);
  for (let i = 0; i < n; i++) {
    if (skip(i)) continue;
    const kc = Math.round(offF[i] / stepM) + K0, r = Math.round(1.5 / stepM), v = [];
    for (let k = Math.max(0, kc - r); k <= Math.min(K - 1, kc + r); k++) if (Number.isFinite(H[i * K + k])) v.push(H[i * K + k]);
    if (v.length) hRoad[i] = median(v);
  }
  fillLinear(hRoad, loop);
  // WHERE THE GROUND FALLS AWAY UNDER THE ROAD. Bare earth has no bridge
  // decks: under a creek bridge or a big culvert the DEM drops to the stream
  // and climbs back within a few tens of metres. A road cannot do that (a sag
  // curve of 170 m radius is the gentlest thing this would catch), so a
  // station more than 1.2 m under the median of the 80 m around it is a deck:
  // its height is the straight line between the ground either side.
  const dips = [];
  for (let pass = 0; pass < 2; pass++) {
    const mark = new Uint8Array(n);
    for (let i = 0; i < n; i++) {
      const v = [];
      for (let o = -10; o <= 10; o++) { let j = i + o; if (loop) j = ((j % n) + n) % n; else if (j < 0 || j >= n) continue; v.push(hRoad[j]); }
      if (hRoad[i] < median(v) - 1.2) for (let o = -3; o <= 3; o++) { const j = i + o; if (j >= 0 && j < n) mark[j] = 1; }
    }
    let a = -1, any = false;
    for (let i = 0; i <= n; i++) {
      if (i < n && mark[i] && !skip(i)) { if (a < 0) a = i; hRoad[i] = NaN; any = true; }
      else if (a >= 0) { dips.push([a, i - 1]); a = -1; }
    }
    if (!any) break;
    fillLinear(hRoad, loop);
  }
  return { off, offF, width, conf, hRoad, dips };
}
