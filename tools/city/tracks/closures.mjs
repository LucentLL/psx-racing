// closures.mjs - where CityRaceClosures shuts the roads that meet a track (the same rule, offline):
// every edge that touches a node of the route and is not on it, barricaded 14 m (or half its length)
// up from the junction. Prints them and writes out/closures_<id>.json for render.py's closure view.
//   node tools/city/tracks/closures.mjs i485west
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { city, WT, toLat, toLon } from './lib.mjs';
const C = city();
const id = process.argv[2] || 'i485west';
const file = JSON.parse(readFileSync(WT.replace(/[\\/]$/, '') + '/Assets/PSXRacing/Resources/charlotte_tracks.json', 'utf8'));
const r = file.routes.find(x => x.id === id);
if (!r) throw new Error('no route ' + id);
const onRoute = new Set(r.edges);
const nodes = [];
for (let i = 0; i < r.edges.length; i++) {
  const e = C.edges[r.edges[i]];
  if (!r.loop && i === r.edges.length - 1) break;
  nodes.push(r.dirs[i] < 0 ? e.a : e.b);
}
if (!r.loop) { const e0 = C.edges[r.edges[0]]; nodes.unshift(r.dirs[0] < 0 ? e0.b : e0.a); }
const shut = new Set(), out = [];
for (const n of nodes) for (const ei of C.nodeEdges[n]) {
  if (onRoute.has(ei) || shut.has(ei)) continue;
  shut.add(ei);
  const e = C.edges[ei];
  if (e.length < 6) continue;
  const d = Math.min(14, e.length / 2), s = e.a === n ? d : e.length - d;
  // point at arc s
  let k = 1; while (k < e.pts.length - 1 && e.s[k] < s) k++;
  const t = (s - e.s[k - 1]) / Math.max(1e-6, e.s[k] - e.s[k - 1]);
  const p = [e.pts[k - 1][0] + (e.pts[k][0] - e.pts[k - 1][0]) * t, e.pts[k - 1][1] + (e.pts[k][1] - e.pts[k - 1][1]) * t];
  out.push({ edge: ei, name: e.name || (e.link ? '(ramp)' : '(unnamed)'), width: +e.profile.width.toFixed(1), x: +p[0].toFixed(1), z: +p[1].toFixed(1), lat: +toLat(p[1]).toFixed(5), lon: +toLon(p[0]).toFixed(5) });
}
mkdirSync(new URL('./out/', import.meta.url), { recursive: true });
writeFileSync(new URL(`./out/closures_${id}.json`, import.meta.url), JSON.stringify(out));
console.log(`${id}: ${out.length} roads closed`);
const byName = {};
for (const c of out) byName[c.name] = (byName[c.name] || 0) + 1;
console.log(Object.entries(byName).map(([k, v]) => `${k} x${v}`).join(', '));
