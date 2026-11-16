// shelf_edges.mjs - both edges of the road shelf in the cached 1 m 3DEP lidar ground, at every waypoint of a stage line.
//
//   node tools/roads/shelf_edges.mjs <stage.json> <out.json>
//
// Offline and read-only on the cache (%PSX_ROADS_CACHE%\3dep, or tools/roads/cache/3dep): no fetch, no ledger write.
// The same idea as bench.mjs (ground flatter than 12 % over +-1 m, in runs at least 4.5 m wide, the run nearest the
// line), but it returns BOTH edges of the run, as offsets along the waypoint's normal (+ = left of the direction of
// travel), so tools/roads/waves.py can tell an S in the real road (both edges carry it) from a jog in the map, and can
// list where the road's edge leaves the shelf. Written by the J3 'stage lines' round (2026-11-16) from the align
// investigation's lidarprobe.mjs.
//   out: { n, a: [right edge or null], z: [left edge or null], w: [width or null], nx: [], nz: [] }
import { readFileSync, writeFileSync } from 'node:fs';
import { open3dep, dem1m } from './lib3dep.mjs';
import { makeProjection } from './lib.mjs';

const [inPath, outPath] = process.argv.slice(2);
if (!inPath || !outPath) { console.error('usage: node shelf_edges.mjs <stage.json> <out.json>'); process.exit(2); }
const j = JSON.parse(readFileSync(inPath, 'utf8'));
const n = j.xyz.length / 3, loop = !!j.loop;
const wp = []; for (let i = 0; i < n; i++) wp.push({ x: j.xyz[3 * i], z: j.xyz[3 * i + 2] });
const proj = makeProjection(j.lat0, j.lon0);
const store = open3dep({ offline: true });
store.saveLedger = () => {};
const lidar = dem1m(store, 0);
const pts = []; for (const q of wp) for (const dx of [-40, 0, 40]) for (const dz of [-40, 0, 40]) pts.push(proj.toLL(q.x + dx, q.z + dz));
await lidar.prepare(pts);
const H0 = 30, ST = 0.5, K0 = Math.round(H0 / ST), K = 2 * K0 + 1, b = 2;
const out = { n, a: [], z: [], w: [], nx: [], nz: [] };
for (let i = 0; i < n; i++) {
  const pa = wp[loop ? (i - 1 + n) % n : Math.max(0, i - 1)], pb = wp[loop ? (i + 1) % n : Math.min(n - 1, i + 1)];
  const L = Math.hypot(pb.x - pa.x, pb.z - pa.z) || 1, tx = (pb.x - pa.x) / L, tz = (pb.z - pa.z) / L;
  const nx = -tz, nz = tx;
  const h = new Float64Array(K);
  for (let k = 0; k < K; k++) { const t = (k - K0) * ST; const ll = proj.toLL(wp[i].x + nx * t, wp[i].z + nz * t); h[k] = lidar.sample(ll.lat, ll.lon); }
  let best = null, k0 = -1;
  for (let k = b; k <= K - b; k++) {
    const low = k < K - b && Number.isFinite(h[k + b]) && Number.isFinite(h[k - b]) && Math.abs(h[k + b] - h[k - b]) / (2 * b * ST) < 0.12;
    if (low && k0 < 0) k0 = k;
    if (!low && k0 >= 0) {
      const a = (k0 - K0) * ST, z = (k - 1 - K0) * ST, w = z - a;
      if (w >= 4.5) { const d = a <= 0 && z >= 0 ? 0 : Math.min(Math.abs(a), Math.abs(z)); if (!best || d < best.d) best = { a, z, w, d }; }
      k0 = -1;
    }
  }
  const ok = best && best.d <= 3 && Math.abs(best.a + best.z) / 2 <= 10;
  out.a.push(ok ? best.a : null); out.z.push(ok ? best.z : null); out.w.push(ok ? best.w : null);
  out.nx.push(+nx.toFixed(6)); out.nz.push(+nz.toFixed(6));
}
writeFileSync(outPath, JSON.stringify(out));
console.log(`${inPath}: ${n} stations, shelf found at ${out.a.filter(v => v !== null).length}`);
