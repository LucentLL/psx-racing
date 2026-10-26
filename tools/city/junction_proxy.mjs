// junction_proxy.mjs - REAL HEIGHTS junction surfaces (2026-10-25): the
// launch audit's junction spots, estimated offline on a profile set without
// Unity. At every node, every movement between two arms meeting nearly
// straight on (through-ish, cos < -0.5) is flown over the grade break the
// node puts between them (each arm's grade over its first metres): a crest
// break A at the judged speed v separates a free car by A^2 v^2 / 2g (no
// vertical curve drawn across a node: the fan and the mitre are straight
// chords), LAUNCH past 0.15 m, UNLOAD past 0.05 m - the city launch audit's
// thresholds and judged speeds (CityLaunchAudit). Also the fan's PLANARITY:
// each arm's first metres against the plane the node's through pair sets.
//   node tools/city/junction_proxy.mjs [--city <bytes>] [--dump <real_on.tsv>] [--top N]
// Without --dump it reads the city's own RPRF section (the design); with it,
// the dump's drawn column (the game's solve).
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, tierOf } from './lib/citydata.mjs';
import { tangentAt } from './lib/vertical.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const val = n => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : null; };
const city = parseCity(readFileSync(val('--city') || join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources', 'charlotte_city.bytes')));
const { edges, nodes, nodeEdges } = city;
const TOP = +(val('--top') || 15);
// profile per edge: { s: [], y: [] }
const P = new Array(edges.length).fill(null);
if (val('--dump')) {
  const col = +(val('--col') || 4);
  for (const line of readFileSync(val('--dump'), 'utf8').split('\n').slice(1)) {
    const f = line.split('\t'); if (f.length <= col) continue;
    const ei = +f[0]; (P[ei] ??= { s: [], y: [] }); P[ei].s.push(+f[2]); P[ei].y.push(+f[col]);
  }
} else {
  for (const e of edges) if (e.rprf) { const n = e.rprf.y.length, s = []; for (let i = 0; i < n; i++) s.push(n > 1 ? e.length * i / (n - 1) : 0); P[e.index] = { s, y: Array.from(e.rprf.y) }; }
}
// the audit's judged speeds (km/h): motorway 150, trunk 130, primary 110, secondary 100, tertiary 90, local 70, ramps 80
const kmh = e => e.link ? 80 : [70, 90, 100, 110, 130, 150][e.rank];
const yAt = (ei, d, atA) => { const p = P[ei], e = edges[ei]; const s = atA ? Math.min(d, e.length) : Math.max(0, e.length - d); let k = 0; while (k + 2 < p.s.length && p.s[k + 1] < s) k++; const t = (s - p.s[k]) / Math.max(1e-3, p.s[k + 1] - p.s[k]); return p.y[k] + (p.y[k + 1] - p.y[k]) * Math.max(0, Math.min(1, t)); };
const G = 9.81, REACH = 8;
let launch = [0, 0, 0, 0], unload = [0, 0, 0, 0];
const spots = [];
for (let nd = 0; nd < nodes.length; nd++) {
  const arms = nodeEdges[nd].filter(i => P[i] && P[i].s.length >= 2 && edges[i].a !== edges[i].b);
  if (arms.length < 2) continue;
  const g = arms.map(i => { const e = edges[i], atA = e.a === nd, d = Math.min(REACH, e.length); return (yAt(i, d, atA) - yAt(i, 0, atA)) / Math.max(0.5, d); });
  const u = arms.map(i => { const e = edges[i]; return e.a === nd ? tangentAt(e, 0) : tangentAt(e, e.length).map(v => -v); });
  let worst = 0, wp = -1, wq = -1;
  for (let p = 0; p < arms.length; p++) for (let q = p + 1; q < arms.length; q++) {
    if (u[p][0] * u[q][0] + u[p][1] * u[q][1] > -0.5) continue;
    const A = -(g[p] + g[q]);                 // crest break through the node (either way: symmetric)
    if (A <= 0) continue;
    const v = Math.min(kmh(edges[arms[p]]), kmh(edges[arms[q]])) / 3.6;
    const sep = A * A * v * v / (2 * G);
    if (sep > worst) { worst = sep; wp = p; wq = q; }
  }
  if (worst <= 0.05) continue;
  const tier = Math.min(tierOf(edges[arms[wp]].rank), tierOf(edges[arms[wq]].rank));
  unload[tier]++; if (worst > 0.15) launch[tier]++;
  spots.push({ nd, worst, a: arms[wp], b: arms[wq], gp: g[wp], gq: g[wq], deg: nodeEdges[nd].length });
}
// THE FAN'S FOLDS: at each node of three arms or more, the plane is the
// node's height and its major through pair's grade (CityElevation's
// junction landings pick the pair the same way: the higher class, then the
// wider); every other arm's height where the fan's corners stand on it
// (the major road's half width over the sine of the meeting angle + 2 m,
// the arm's own reach at most) against that plane. A corner off the plane
// tilts the fan's triangles under the through lanes.
const fold = [0, 0, 0, 0], foldBig = [0, 0, 0, 0], folds = [];
for (let nd = 0; nd < nodes.length; nd++) {
  const arms = nodeEdges[nd].filter(i => P[i] && P[i].s.length >= 2 && edges[i].a !== edges[i].b);
  if (arms.length < 3) continue;
  const u = arms.map(i => { const e = edges[i]; return e.a === nd ? tangentAt(e, 0) : tangentAt(e, e.length).map(v => -v); });
  let bp = -1, bq = -1, bk = -1e9;
  for (let p = 0; p < arms.length; p++) for (let q = p + 1; q < arms.length; q++) {
    if (u[p][0] * u[q][0] + u[p][1] * u[q][1] > -0.7) continue;
    const E = edges[arms[p]], F = edges[arms[q]];
    const key = Math.min(E.rank * 2 + (E.link ? 0 : 1), F.rank * 2 + (F.link ? 0 : 1)) * 100 + E.hw + F.hw;
    if (key > bk) { bk = key; bp = p; bq = q; }
  }
  if (bp < 0) continue;
  const atA = i => edges[i].a === nd;
  const y0 = yAt(arms[bp], 0, atA(arms[bp]));
  const hP = Math.min(10, edges[arms[bp]].length), hQ = Math.min(10, edges[arms[bq]].length);
  const gP = (yAt(arms[bp], hP, atA(arms[bp])) - y0) / hP, gQ = (yAt(arms[bq], hQ, atA(arms[bq])) - y0) / hQ;
  let ax = u[bq][0] - u[bp][0], az = u[bq][1] - u[bp][1]; const al = Math.hypot(ax, az) || 1; ax /= al; az /= al;
  const gAlong = (gQ - gP) * 0.5;
  const hwMaj = Math.max(edges[arms[bp]].hw, edges[arms[bq]].hw);
  let worst = 0, wa = -1;
  for (let a = 0; a < arms.length; a++) {
    if (a === bp || a === bq) continue;
    const e = edges[arms[a]];
    const sin = Math.abs(ax * u[a][1] - az * u[a][0]);
    const d = Math.min(e.length, hwMaj / Math.max(0.4, sin) + 2);
    const plane = y0 + gAlong * (ax * u[a][0] + az * u[a][1]) * d;
    const off = yAt(arms[a], d, atA(arms[a])) - plane;
    if (Math.abs(off) > Math.abs(worst)) { worst = off; wa = a; }
  }
  if (wa < 0 || Math.abs(worst) <= 0.1) continue;
  const tier = tierOf(edges[arms[bp]].rank);
  fold[tier]++; if (Math.abs(worst) > 0.3) foldBig[tier]++;
  folds.push({ nd, off: worst, arm: arms[wa], maj: arms[bp] });
}
folds.sort((a, b) => Math.abs(b.off) - Math.abs(a.off));
console.log(`FAN FOLDS: a minor arm's corner off its junction's plane > 0.1 m: T1 ${fold[1]} / T2 ${fold[2]} / T3 ${fold[3]} = ${fold[1] + fold[2] + fold[3]} (> 0.3 m: ${foldBig[1] + foldBig[2] + foldBig[3]})`);
for (const f of folds.slice(0, Math.min(TOP, 8))) console.log(`  node ${f.nd} major e${f.maj} '${edges[f.maj].name}': e${f.arm} '${edges[f.arm].name}' len ${edges[f.arm].length.toFixed(0)} ${f.off >= 0 ? '+' : ''}${f.off.toFixed(2)} m`);
if (val('--nodes')) { const set = new Set(val('--nodes').split(',').map(Number)); let h = 0; for (const f of folds) if (set.has(f.nd)) h++; console.log(`  audit nodes with a fold: ${h}`); }
// INTERIOR CRESTS: a break between two station grades inside an edge whose
// radius (the stations' spacing over the break) is under 1.5 v^2/g at the
// judged speed - a crest the drawn profile must round hard (it lowers a
// crest 0.15 m at most) or the car leaves the road
const ic = [0, 0, 0, 0], icAt = [];
for (const e of edges) {
  const p = P[e.index]; if (!p || p.s.length < 3) continue;
  const v = kmh(e) / 3.6, need = 1.5 * v * v / G;
  for (let i = 1; i + 1 < p.s.length; i++) {
    const h1 = p.s[i] - p.s[i - 1], h2 = p.s[i + 1] - p.s[i]; if (h1 < 1 || h2 < 1) continue;
    const A = (p.y[i] - p.y[i - 1]) / h1 - (p.y[i + 1] - p.y[i]) / h2;
    if (A <= 0.005) continue;
    const R = 0.5 * (h1 + h2) / A;
    if (R >= need) continue;
    ic[tierOf(e.rank)]++; icAt.push([A, e.index, p.s[i]]);
  }
}
icAt.sort((a, b) => b[0] - a[0]);
console.log(`INTERIOR CRESTS under 1.5 v^2/g: T1 ${ic[1]} / T2 ${ic[2]} / T3 ${ic[3]}; worst ${icAt.slice(0, 5).map(([A, ei, s]) => `e${ei} '${edges[ei].name}' s ${s.toFixed(0)} ${(A * 100).toFixed(1)}%`).join(', ')}`);
spots.sort((a, b) => b.worst - a.worst);
console.log(`JUNCTION PROXY (${val('--dump') ? 'dump ' + val('--dump') : 'RPRF design'}): crest breaks through nodes, LAUNCH (>0.15 m) T1 ${launch[1]} / T2 ${launch[2]} / T3 ${launch[3]} = ${launch[1] + launch[2] + launch[3]}; UNLOAD (>0.05 m) T1 ${unload[1]} / T2 ${unload[2]} / T3 ${unload[3]} = ${unload[1] + unload[2] + unload[3]}`);
for (const s of spots.slice(0, TOP)) {
  const n = nodes[s.nd];
  console.log(`  node ${s.nd} deg${s.deg} (${n.x.toFixed(0)},${n.z.toFixed(0)}) sep ${s.worst.toFixed(2)}: e${s.a} '${edges[s.a].name}' r${edges[s.a].rank} ${(s.gp * 100).toFixed(1)}% | e${s.b} '${edges[s.b].name}' r${edges[s.b].rank} ${(s.gq * 100).toFixed(1)}%`);
}
if (val('--nodes')) { const set = new Set(val('--nodes').split(',').map(Number)); for (const s of spots) if (set.has(s.nd)) console.log(`  hit node ${s.nd} sep ${s.worst.toFixed(2)}`); }
