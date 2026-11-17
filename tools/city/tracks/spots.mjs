// spots.mjs - driver-eye cameras on a track, for track-job.ps1 -Mode eye (CityRefSpots.RunPoles: the eye in
// the lane, looking down the road, day and night). Prints the -Spots value.
//
//   node tools/city/tracks/spots.mjs <route id> [m1 m2 ...]
//     default: 25 m behind the start line looking down the course, and the middle of the race
//     m1 m2 ...: metres along the route's chain instead (the file's startM / finishM are on the same scale)
import { city, chainPoly, WT, toLat, toLon } from './lib.mjs';
import { readTracks } from './trackfiles.mjs';

const C = city();
const id = process.argv[2];
const t = readTracks(WT).find(x => x.route && x.route.id === id);
if (!t) { console.error('no track file with route id ' + id); process.exit(1); }
const r = t.route;
const chain = r.edges.map((ei, k) => ({ ei, dir: r.dirs[k] < 0 ? -1 : 1 }));
const poly = chainPoly(chain);
const cum = [0];
for (let i = 1; i < poly.length; i++) cum.push(cum[i - 1] + Math.hypot(poly[i][0] - poly[i - 1][0], poly[i][1] - poly[i - 1][1]));
const total = cum[cum.length - 1];
const wrap = m => r.loop ? ((m % total) + total) % total : Math.max(0, Math.min(total - 1, m));
const at = m => {
  m = wrap(m);
  let i = 1; while (i < poly.length - 1 && cum[i] < m) i++;
  const [ax, az] = poly[i - 1], [bx, bz] = poly[i], L = cum[i] - cum[i - 1] || 1, f = (m - cum[i - 1]) / L;
  return { x: ax + (bx - ax) * f, z: az + (bz - az) * f, hdg: (Math.atan2(bx - ax, bz - az) * 180 / Math.PI + 360) % 360 };
};
const edgeAt = m => { m = wrap(m); let acc = 0; for (const c of chain) { acc += C.edges[c.ei].length; if (acc >= m) return C.edges[c.ei]; } return C.edges[chain[chain.length - 1].ei]; };
const race = r.loop ? total : r.finishM - r.startM;
const marks = process.argv.length > 3 ? process.argv.slice(3).map(Number) : [r.startM - 25, r.startM + race / 2];
const names = process.argv.length > 3 ? marks.map((m, k) => 'm' + Math.round(m)) : ['grid', 'middle'];
const parts = marks.map((m, k) => {
  const p = at(m), e = edgeAt(m);
  return `${r.id}_${names[k]},${toLat(p.z).toFixed(6)},${toLon(p.x).toFixed(6)},${(e.name || '').replace(/[,;]/g, ' ')},${p.hdg.toFixed(0)}`;
});
console.log(parts.join(';'));
