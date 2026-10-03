// culverts.mjs - creeks OSM says are PIPED under a road (owner Q8, 2026-10-02;
// plan B2; Docs/CHARLOTTE.md "B2").
//
// The rule before: every road crossing a county/USGS creek line got a railed
// deck (a water span, section SPAN). 317 of the 564 spans lie within 15 m of
// an OSM waterway tagged tunnel=culvert - creeks the map says run through a
// pipe under the road, with no bridge to see (critic C2: West 5th Street's
// two carriageways over Irwin Creek past the east signal, I-77 southbound's
// deck 240 m from the owner's crossing). The owner, 2026-10-02: FOLLOW OSM,
// BUILD CULVERTS.
//
// A road's crossing of a creek line is a CULVERT when, by geometry:
//   * an OSM culvert line (tunnel=culvert or culvert=yes) passes within
//     CULVERT_NEAR_M of the crossing point,
//   * that line crosses the road's ribbon or passes within CULVERT_RIBBON_M
//     of it (its paved half width from the OSM line, plus a metre for the
//     line model's offsets) within CULVERT_ALONG_M along the road of the
//     crossing,
//   * the road is not tagged bridge=yes (OSM's word beats the creek line's),
//     nor a tunnel,
//   * its deck would hold no other water (a lake, another creek).
// Every candidate (a creek crossing with a culvert line within 15 m) is listed
// with its decision and reason (export_osm.mjs writes tools/city/baseline/culverts_q8.csv).
//
// What a culvert becomes (export_osm.mjs, then the game):
//   * no SPAN: no deck, no parapets - the road keeps its embankment;
//   * the creek line runs on under the road, as a ravine's does (WP-25): the
//     road's fill covers it (CityElevation.HoldCulverts holds the road at the
//     pipe's cover, CityCulverts.MinFillM over the creek's carved floor, so
//     the fill always stands over the water and the sheet under it is hidden)
//     and the water shows again where the fill meets the channel;
//   * section CULV (lib/citydata.mjs has the layout) lists the crossings:
//     CityCulverts walks the creek from each one to the fill's toe both ways
//     and stands a headwall (or a projecting pipe) there - the end the water
//     runs out of - exactly as it does for a ravine.

import { readFileSync, existsSync } from 'node:fs';

export const CULVERT_NEAR_M = 15;
export const CULVERT_RIBBON_M = 3;
export const CULVERT_ALONG_M = 15;
/// A creek's flat floor each side of its line (CityElevation.CreekFlatHalf).
export const creekFlatHalf = widthM => Math.max(6, Math.max(4, widthM) / 2 + 3);


const segD = (px, pz, ax, az, bx, bz) => {
  const dx = bx - ax, dz = bz - az, L = dx * dx + dz * dz;
  let t = L > 0 ? ((px - ax) * dx + (pz - az) * dz) / L : 0;
  t = Math.max(0, Math.min(1, t));
  return { d: Math.hypot(px - (ax + t * dx), pz - (az + t * dz)), t };
};
function segX(ax, az, bx, bz, cx, cz, dx, dz) {
  const rx = bx - ax, rz = bz - az, qx = dx - cx, qz = dz - cz, den = rx * qz - rz * qx;
  if (Math.abs(den) < 1e-9) return null;
  const wx = cx - ax, wz = cz - az, t = (wx * qz - wz * qx) / den, u = (wx * rz - wz * rx) / den;
  return t >= 0 && t <= 1 && u >= 0 && u <= 1 ? { t, u } : null;
}

/// The culvert lines (tunnel=culvert or culvert=yes waterways) as polylines
/// in the game frame, on a 50 m grid: { ways: [{ id, name, pts }], near(x, z, r) }.
export function loadCulvertLines(path, toX, toZ) {
  if (!existsSync(path)) return null;
  const j = JSON.parse(readFileSync(path, 'utf8'));
  const ways = [];
  for (const w of j.elements || j) {
    if (w.type !== 'way' || !w.geometry || !w.tags || w.geometry.length < 2) continue;
    if (!(w.tags.tunnel === 'culvert' || w.tags.culvert === 'yes')) continue;
    ways.push({ id: w.id, name: w.tags.name || '', pts: w.geometry.map(g => [toX(g.lon), toZ(g.lat)]) });
  }
  const C = 50, grid = new Map(), key = (a, b) => a * 100003 + b;
  ways.forEach((w, k) => {
    const seen = new Set();
    for (let i = 1; i < w.pts.length; i++) {
      const [ax, az] = w.pts[i - 1], [bx, bz] = w.pts[i];
      for (let cx = Math.floor(Math.min(ax, bx) / C); cx <= Math.floor(Math.max(ax, bx) / C); cx++)
        for (let cz = Math.floor(Math.min(az, bz) / C); cz <= Math.floor(Math.max(az, bz) / C); cz++) {
          const kk = key(cx, cz); if (seen.has(kk)) continue; seen.add(kk);
          let l = grid.get(kk); if (!l) grid.set(kk, l = []); l.push(k);
        }
    }
  });
  return {
    ways,
    /// the culvert ways with a segment within r of (x, z), nearest first:
    /// [{ w, d }]
    near(x, z, r) {
      const out = new Map();
      for (let cx = Math.floor((x - r) / C); cx <= Math.floor((x + r) / C); cx++)
        for (let cz = Math.floor((z - r) / C); cz <= Math.floor((z + r) / C); cz++)
          for (const k of grid.get(key(cx, cz)) || []) {
            if (out.has(k)) continue;
            const w = ways[k]; let d = Infinity;
            for (let i = 1; i < w.pts.length; i++) d = Math.min(d, segD(x, z, w.pts[i - 1][0], w.pts[i - 1][1], w.pts[i][0], w.pts[i][1]).d);
            out.set(k, d);
          }
      return [...out].filter(([, d]) => d <= r).sort((a, b) => a[1] - b[1] || ways[a[0]].id - ways[b[0]].id).map(([k, d]) => ({ w: ways[k], d }));
    },
  };
}

/// How a culvert line meets a road: the least distance from the line to the
/// road's ribbon (negative inside it) among the road's segments within
/// `along` of arc s, and the road arc where it does. pts/acc: the road's line
/// and its cumulative arc; hw its half width.
export function ribbonMeet(cul, pts, acc, s, hw, along) {
  let best = { dRib: Infinity, at: s };
  for (let i = 1; i < pts.length; i++) {
    if (acc[i] < s - along || acc[i - 1] > s + along) continue;
    const [ax, az] = pts[i - 1], [bx, bz] = pts[i];
    for (let k = 1; k < cul.length; k++) {
      const [cx, cz] = cul[k - 1], [dx, dz] = cul[k];
      const x = segX(ax, az, bx, bz, cx, cz, dx, dz);
      if (x) {
        const at = acc[i - 1] + (acc[i] - acc[i - 1]) * x.t;
        if (Math.abs(at - s) <= along && -hw < best.dRib) best = { dRib: -hw, at };
        continue;
      }
      // the closest approach: the four end-to-segment distances
      for (const [px, pz, onRoad] of [[cx, cz, false], [dx, dz, false], [ax, az, true], [bx, bz, true]]) {
        let d, at;
        if (onRoad) { const q = segD(px, pz, cx, cz, dx, dz); d = q.d; at = onRoad && px === ax && pz === az ? acc[i - 1] : acc[i]; }
        else { const q = segD(px, pz, ax, az, bx, bz); d = q.d; at = acc[i - 1] + (acc[i] - acc[i - 1]) * q.t; }
        if (Math.abs(at - s) > along) continue;
        if (d - hw < best.dRib) best = { dRib: d - hw, at };
      }
    }
  }
  return best;
}

export const arcOf = pts => { const a = [0]; for (let k = 1; k < pts.length; k++) a.push(a[k - 1] + Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1])); return a; };


/// A water's bed at arc s, as the game reads it (CityElevation.BedYAt):
/// samples every bedStep from the first point, the last one at the line's end.
export function bedAt(w, s) {
  const b = w.bed, n = b ? b.length : 0;
  if (!n) return NaN;
  if (n === 1 || !(w.bedStep > 0)) return b[0];
  const last = n - 1, len = w.bedLen ?? (last * w.bedStep), sLast = (last - 1) * w.bedStep;
  if (s >= sLast) return b[last - 1] + (b[last] - b[last - 1]) * Math.max(0, Math.min(1, (s - sLast) / Math.max(1e-3, len - sLast)));
  const f = Math.max(0, s) / w.bedStep, k = Math.min(last - 1, Math.floor(f));
  return b[k] + (b[k + 1] - b[k]) * (f - k);
}

