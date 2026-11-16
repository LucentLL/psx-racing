// the raw OSM ways the exporter read (ways_all.json + streets_core.json), read-only
import fs from 'node:fs';
import { fromLL } from './load.mjs';
import { ROOT } from './load.mjs';
// the raw OSM the exporter read (gitignored; PSX_CITY_CACHE overrides, e.g. the main checkout's)
const CACHE = (process.env.PSX_CITY_CACHE || (ROOT + '/tools/city/cache')).replace(/\\/g, '/').replace(/\/?$/, '/');
export function loadRaw() {
  const ways = new Map();
  for (const f of ['ways_all.json', 'streets_core.json']) {
    const j = JSON.parse(fs.readFileSync(CACHE + f, 'utf8'));
    for (const w of j.elements) if (w.type === 'way' && w.geometry && w.nodes) {
      if (ways.has(w.id)) continue;
      ways.set(w.id, { id: w.id, nodes: w.nodes, pts: w.geometry.map(g => fromLL(g.lat, g.lon)), tags: w.tags || {} });
    }
  }
  return ways;
}
const unit = (x, z) => { const d = Math.hypot(x, z) || 1; return [x / d, z / d]; };
/// direction along way w from vertex i, toward increasing (dir +1) or decreasing (-1) index:
/// first segment and the chord to the point `chord` metres along
export function rawDirs(w, i, dir, chord = 15) {
  const P = w.pts; const j = i + dir; if (j < 0 || j >= P.length) return null;
  const first = unit(P[j][0] - P[i][0], P[j][1] - P[i][1]);
  let acc = 0, k = i, q = P[i];
  while (true) {
    const kn = k + dir; if (kn < 0 || kn >= P.length) { q = P[k]; break; }
    const L = Math.hypot(P[kn][0] - P[k][0], P[kn][1] - P[k][1]);
    if (acc + L >= chord) { const t = (chord - acc) / L; q = [P[k][0] + (P[kn][0] - P[k][0]) * t, P[k][1] + (P[kn][1] - P[k][1]) * t]; acc = chord; break; }
    acc += L; k = kn;
  }
  return { first, chord: unit(q[0] - P[i][0], q[1] - P[i][1]), chordLen: acc };
}
/// the vertex of way w nearest (x, z): [index, distance]
export function nearestVertex(w, x, z) {
  let bi = -1, bd = Infinity;
  for (let i = 0; i < w.pts.length; i++) { const d = Math.hypot(w.pts[i][0] - x, w.pts[i][1] - z); if (d < bd) { bd = d; bi = i; } }
  return [bi, bd];
}
