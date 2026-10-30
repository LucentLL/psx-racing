// waymoves.mjs - WHICH ROADS MOVED between two exports of charlotte_city.bytes
// (the gap audit, Docs/gap_audit.md). Per OSM way id in both exports:
//   old->new  the largest distance from a vertex of the old line to the new
//             line: the measure behind "74 of 20,566 ways moved > 20 cm" (the
//             connector fetch, d83a3d8b) - it misses a new line that leaves
//             the old one between old vertices
//   both      the symmetric (Hausdorff) distance, both lines sampled every 2 m
//
//   node tools/city/waymoves.mjs <before> <after> [--top N] [--json out.json]
//     <before> / <after>: a charlotte_city.bytes file, or a git revision (its
//     Assets/PSXRacing/Resources/charlotte_city.bytes, read with git show)
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity } from './lib/citydata.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ARGS = process.argv.slice(2);
const opt = k => { const i = ARGS.indexOf(k); return i >= 0 ? ARGS[i + 1] : null; };
const pos = ARGS.filter((a, i) => !a.startsWith('--') && !(i > 0 && ARGS[i - 1].startsWith('--')));
if (pos.length !== 2) { console.error('usage: node tools/city/waymoves.mjs <before> <after> [--top N] [--json out.json]'); process.exit(2); }
const load = src => existsSync(src) ? readFileSync(src)
  : execFileSync('git', ['-C', join(HERE, '..', '..'), 'show', `${src}:Assets/PSXRacing/Resources/charlotte_city.bytes`], { maxBuffer: 1 << 28 });
const B = parseCity(load(pos[0])), A = parseCity(load(pos[1]));
const byWay = c => { const m = new Map(); for (const e of c.edges) { let l = m.get(e.wayId); if (!l) m.set(e.wayId, l = []); l.push(e.pts); } return m; };
const WB = byWay(B), WA = byWay(A);
function dseg(p, a, b) { const dx = b[0] - a[0], dz = b[1] - a[1], L = dx * dx + dz * dz; let t = L > 0 ? ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / L : 0; t = Math.max(0, Math.min(1, t)); return Math.hypot(p[0] - a[0] - t * dx, p[1] - a[1] - t * dz); }
const dset = (p, lines) => { let m = Infinity; for (const P of lines) for (let i = 1; i < P.length; i++) { const d = dseg(p, P[i - 1], P[i]); if (d < m) m = d; } return m; };
const samples = (lines, step) => { const out = []; for (const P of lines) for (let i = 1; i < P.length; i++) { const n = Math.max(1, Math.ceil(Math.hypot(P[i][0] - P[i - 1][0], P[i][1] - P[i - 1][1]) / step)); for (let k = i === 1 ? 0 : 1; k <= n; k++) out.push([P[i - 1][0] + (P[i][0] - P[i - 1][0]) * k / n, P[i - 1][1] + (P[i][1] - P[i - 1][1]) * k / n]); } return out; };
const same = (a, b) => a.length === b.length && a.every((P, k) => P.length === b[k].length && P.every((p, j) => p[0] === b[k][j][0] && p[1] === b[k][j][1]));
const bins = () => ({ lt1cm: 0, to20cm: 0, gt20cm: 0 });
const cnt = { oldNew: bins(), both: bins() };
const bin = (o, d) => o[d < 0.01 ? 'lt1cm' : d <= 0.2 ? 'to20cm' : 'gt20cm']++;
const rows = [];
let common = 0;
for (const [id, lb] of WB) {
  const la = WA.get(id);
  if (!la) continue;
  common++;
  if (same(lb, la)) { bin(cnt.oldNew, 0); bin(cnt.both, 0); continue; }
  let oldNew = 0; for (const P of lb) for (const p of P) oldNew = Math.max(oldNew, dset(p, la));
  let both = oldNew; for (const p of samples(lb, 2)) both = Math.max(both, dset(p, la)); for (const p of samples(la, 2)) both = Math.max(both, dset(p, lb));
  bin(cnt.oldNew, oldNew); bin(cnt.both, both);
  if (both >= 0.01) { const e = B.edges.find(x => x.wayId === id); rows.push({ id, name: e.name || '(ramp)', oldNew: +oldNew.toFixed(3), both: +both.toFixed(3) }); }
}
rows.sort((p, q) => q.both - p.both);
const fmt = o => `< 1 cm ${o.lt1cm}, 1-20 cm ${o.to20cm}, > 20 cm ${o.gt20cm}`;
console.log(`ways: ${WB.size} before, ${WA.size} after; ${common} in both, ${[...WA.keys()].filter(id => !WB.has(id)).length} new, ${[...WB.keys()].filter(id => !WA.has(id)).length} gone`);
console.log(`  old->new: ${fmt(cnt.oldNew)}`);
console.log(`  both:     ${fmt(cnt.both)}`);
for (const r of rows.slice(0, +(opt('--top') || 12))) console.log(`  way ${r.id} ${r.name}: old->new ${r.oldNew.toFixed(2)} m, both ${r.both.toFixed(2)} m`);
if (opt('--json')) writeFileSync(opt('--json'), JSON.stringify({ counts: cnt, rows }, null, 1) + '\n');
