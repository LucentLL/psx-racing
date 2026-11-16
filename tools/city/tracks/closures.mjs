// closures.mjs - a sanctioned event's closures for a track, offline, by CityRaceClosures' own rule, and
// THE ROUTE CHECK the race play check runs: no closure may stand on the race line.
//
// The rule (Scripts/CityRaceClosures.cs, owner 2026-11-16 15:10: closures only in legal events, like a
// parade, never on the race line): every road that meets the route is shut by a row of barricades
// up it, at the first spot from 14 m (2 m steps) where the row's centre is (its width / 2 + 1.5 m)
// and both its ends 1.5 m off the course's pavement (each route edge's polyline +- its half-width,
// at least 3 m); panels that still reach within 1.5 m are dropped; a road that runs beside the course
// to its end is left open. The check walks every point of the race line (the chain's polyline) and
// fails any row closer than the driven lanes there (the course's own half-width, at least 3.7 m)
// + 0.5 m. Exit code 1 when anything stands on the line.
//   node tools/city/tracks/closures.mjs i485west
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { city, WT, toLat, toLon, chainPoly } from './lib.mjs';
const C = city();
const id = process.argv[2] || 'i485west';
const file = JSON.parse(readFileSync(WT.replace(/[\\/]$/, '') + '/Assets/PSXRacing/Resources/charlotte_tracks.json', 'utf8'));
const r = file.routes.find(x => x.id === id);
if (!r) throw new Error('no route ' + id);
const SETBACK = 14, CLEAR = 1.5, PANEL = 2.4;
const onRoute = new Set(r.edges);
// the course's pavement
const course = r.edges.map(ei => { const e = C.edges[ei]; return { pts: e.pts, half: Math.max(3, e.profile.width / 2) }; });
const distSeg = (p, a, b) => { const dx = b[0] - a[0], dz = b[1] - a[1], L = dx * dx + dz * dz; let t = L > 0 ? ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L : 0; t = Math.max(0, Math.min(1, t)); return Math.hypot(p[0] - a[0] - dx * t, p[1] - a[1] - dz * t); };
const outside = p => { let best = 30; for (const c of course) for (let i = 1; i < c.pts.length; i++) { const d = distSeg(p, c.pts[i - 1], c.pts[i]) - c.half; if (d < best) best = d; } return best; };
const at = (e, s) => { let k = 1; while (k < e.pts.length - 1 && e.s[k] < s) k++; const t = (s - e.s[k - 1]) / Math.max(1e-6, e.s[k] - e.s[k - 1]); const a = e.pts[k - 1], b = e.pts[k]; const tx = b[0] - a[0], tz = b[1] - a[1], L = Math.hypot(tx, tz) || 1; return { p: [a[0] + tx * t, a[1] + tz * t], t: [tx / L, tz / L] }; };
const nodes = [];
for (let i = 0; i < r.edges.length; i++) { const e = C.edges[r.edges[i]]; if (!r.loop && i === r.edges.length - 1) break; nodes.push(r.dirs[i] < 0 ? e.a : e.b); }
if (!r.loop) { const e0 = C.edges[r.edges[0]]; nodes.unshift(r.dirs[0] < 0 ? e0.b : e0.a); }
const shut = new Set(), rows = [], open = [];
for (const n of nodes) for (const ei of C.nodeEdges[n]) {
  if (onRoute.has(ei) || shut.has(ei)) continue;
  shut.add(ei);
  const e = C.edges[ei];
  if (e.length < 6) continue;
  const w = Math.max(3, e.profile.width);
  let placed = null;
  for (let d = Math.min(SETBACK, e.length / 2); d <= e.length - 2 && !placed; d += 2) {
    const s = e.a === n ? d : e.length - d;
    const { p, t } = at(e, s), ac = [t[1], -t[0]];
    if (outside(p) < w / 2 + CLEAR) continue;
    if (outside([p[0] + ac[0] * w / 2, p[1] + ac[1] * w / 2]) < CLEAR || outside([p[0] - ac[0] * w / 2, p[1] - ac[1] * w / 2]) < CLEAR) continue;
    const panels = Math.max(1, Math.ceil(w / PANEL)), pw = w / panels;
    let lo = Infinity, hi = -Infinity;
    for (let i = 0; i < panels; i++) {
      const off = (i + 0.5) * pw - w / 2, c = [p[0] + ac[0] * off, p[1] + ac[1] * off];
      const ea = [c[0] + ac[0] * pw / 2, c[1] + ac[1] * pw / 2], eb = [c[0] - ac[0] * pw / 2, c[1] - ac[1] * pw / 2];
      if (outside(ea) < CLEAR || outside(eb) < CLEAR) continue;
      lo = Math.min(lo, off - pw / 2); hi = Math.max(hi, off + pw / 2);
    }
    if (lo <= hi) placed = { edge: ei, name: e.name || (e.link ? '(ramp)' : '(unnamed)'), a: [p[0] + ac[0] * lo, p[1] + ac[1] * lo], b: [p[0] + ac[0] * hi, p[1] + ac[1] * hi], up: +d.toFixed(0) };
  }
  if (placed) rows.push(placed); else open.push({ edge: ei, name: e.name || (e.link ? '(ramp)' : '(unnamed)') });
}
// THE ROUTE CHECK: every leg of the race line against every row
const line = chainPoly(r.edges.map((ei, k) => ({ ei, dir: r.dirs[k] })));
const segSeg = (a, b, c, d) => {
  const cr = (u, v) => u[0] * v[1] - u[1] * v[0], sub = (u, v) => [u[0] - v[0], u[1] - v[1]];
  const d1 = cr(sub(b, a), sub(c, a)), d2 = cr(sub(b, a), sub(d, a)), d3 = cr(sub(d, c), sub(a, c)), d4 = cr(sub(d, c), sub(b, c));
  if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return 0;
  return Math.min(distSeg(a, c, d), distSeg(b, c, d), distSeg(c, a, b), distSeg(d, a, b));
};
let bad = 0, worst = null;
for (const row of rows) {
  let best = Infinity, bi = 0;
  for (let i = 1; i < line.length; i++) { const d = segSeg(row.a, row.b, line[i - 1], line[i]); if (d < best) { best = d; bi = i; } }
  const halfLane = Math.max(3.7, -outside(line[bi]));
  const margin = best - halfLane;
  row.margin = +margin.toFixed(2);
  if (margin < 0.5) bad++;
  if (!worst || margin < worst.margin) worst = row;
}
mkdirSync(new URL('./out/', import.meta.url), { recursive: true });
const out = rows.map(x => ({ ...x, x: +((x.a[0] + x.b[0]) / 2).toFixed(1), z: +((x.a[1] + x.b[1]) / 2).toFixed(1), lat: +toLat((x.a[1] + x.b[1]) / 2).toFixed(5), lon: +toLon((x.a[0] + x.b[0]) / 2).toFixed(5) }));
writeFileSync(new URL(`./out/closures_${id}.json`, import.meta.url), JSON.stringify(out));
console.log(`${id}: ${rows.length} roads closed, ${open.length} left open beside the course (${open.map(o => o.name + ' e' + o.edge).join(', ') || 'none'})`);
console.log(`ROUTE CHECK: ${bad} closures on the race line; nearest ${worst ? worst.name + ' e' + worst.edge + ' ' + worst.margin + ' m clear of the lanes, ' + worst.up + ' m up its road' : '-'}`);
process.exit(bad ? 1 : 0);
