// armsearch.mjs - pick the freeway waypoints along each arm that give the shortest legal lap
// usage: node armsearch.mjs '<json: [{name, A:[lat,lon], B:[lat,lon], hdgs:[h1,h2]|null}, ...]>'
import { city, routeVia, edgeNear } from './lib.mjs';
const C = city();
const arms = JSON.parse(process.argv[2]);
const hdgAB = (A, B) => (Math.atan2((B[1] - A[1]) * 111320 * Math.cos(35.18456 * Math.PI / 180), (B[0] - A[0]) * 111132) * 180 / Math.PI + 360) % 360;
// each arm contributes an ordered list of waypoint options (out then back)
const opts = arms.map(a => {
  const h = a.hdg != null ? a.hdg : hdgAB(a.A, a.B);
  const list = [];
  for (const t of [0.3, 0.4, 0.5, 0.6, 0.7]) {
    const p = [a.A[0] + (a.B[0] - a.A[0]) * t, a.A[1] + (a.B[1] - a.A[1]) * t];
    try { list.push({ t, w: edgeNear(p[0], p[1], a.name, a.dir === -1 ? (h + 180) % 360 : h, 300) }); } catch (e) {}
  }
  return list;
});
let best = null;
function rec(k, chosen) {
  if (k === opts.length) {
    try {
      const ch = routeVia(chosen.map(c => c.w), { loop: true, fwy: true });
      const L = ch.reduce((a, c) => a + C.edges[c.ei].length, 0);
      if (!best || L < best.L) best = { L, ts: chosen.map(c => c.t), wps: chosen.map(c => c.w) };
    } catch (e) {}
    return;
  }
  const seen = new Set();
  for (const o of opts[k]) { if (seen.has(o.w.ei)) continue; seen.add(o.w.ei); rec(k + 1, [...chosen, o]); }
}
rec(0, []);
console.log(best ? `best lap ${best.L.toFixed(0)} m at t=${best.ts.join(',')} edges ${best.wps.map(w => 'e' + w.ei).join(',')}` : 'no lap');
