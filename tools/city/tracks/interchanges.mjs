// interchanges.mjs - ramp clusters along each freeway (where a lap can turn round)
import { city, toLat, toLon } from './lib.mjs';
const C = city();
const FWY = process.argv.slice(2).length ? process.argv.slice(2) : ['I-77', 'I-277', 'I-485', 'I-85', 'East Independence Expressway', 'Brookshire Freeway', 'John Belk Freeway'];
for (const F of FWY) {
  const pts = [];
  for (const e of C.edges) {
    if (!e.link) continue;
    for (const n of [e.a, e.b]) {
      if (!C.nodeEdges[n].some(i => C.edges[i].name === F && !C.edges[i].link)) continue;
      // what the ramp leads to at its other end(s): names of non-freeway roads within 2 hops
      const other = new Set();
      const far = n === e.a ? e.b : e.a;
      for (const i of C.nodeEdges[far]) { const f = C.edges[i]; if (!f.link && f.name && f.name !== F) other.add(f.name); }
      for (const i of C.nodeEdges[far]) { const f = C.edges[i]; if (!f.link) continue; const f2 = f.a === far ? f.b : f.a; for (const j of C.nodeEdges[f2]) { const g = C.edges[j]; if (!g.link && g.name && g.name !== F) other.add(g.name); } }
      pts.push({ x: C.nodes[n].x, z: C.nodes[n].z, other });
    }
  }
  const cl = [];
  for (const p of pts) {
    let c = cl.find(c => Math.hypot(c.x - p.x, c.z - p.z) < 900);
    if (!c) cl.push({ x: p.x, z: p.z, n: 1, other: new Set(p.other) });
    else { c.n++; for (const o of p.other) c.other.add(o); }
  }
  console.log('== ' + F + ': ' + cl.length + ' interchanges');
  for (const c of cl.sort((a, b) => b.z - a.z)) console.log(`  ${toLat(c.z).toFixed(4)},${toLon(c.x).toFixed(4)} ramps ${c.n}  ${[...c.other].slice(0, 5).join(' / ')}`);
}
