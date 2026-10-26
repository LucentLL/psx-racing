// realheights_check.mjs - REAL HEIGHTS (2026-10-25): the game's solved road
// heights (Editor/CityRealHeights.cs dumps: real_on.tsv, real_off.tsv - every
// station: edge, i, s, station y, drawn y, structure) against USGS 3DEP 1/3"
// bare earth sampled the way the exporter samples it (median of five across
// the road, lib/roadprofile.mjs sampleSection).
//
// Per dump: per tier, |drawn - 3DEP| on stations OFF structure (p50 / p95, km
// more than 1.4 m above / below); structure on roads OSM does not tag as a
// bridge (km, the worst edges); and the owner's spots - every road within
// 150 m of each spot: the most it stands over its 3DEP ground off a tagged
// bridge, and whether any of it is structure.
//   node tools/city/realheights_check.mjs <real_off.tsv> <real_on.tsv> [--city <charlotte_city.bytes>]
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, tierOf } from './lib/citydata.mjs';
import { load3dep } from './lib/dem3dep.mjs';
import { sampleSection } from './lib/roadprofile.mjs';
import { pointAt, projectOn } from './lib/vertical.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
let cityPath = join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources', 'charlotte_city.bytes');
const dumps = [];
for (let i = 0; i < args.length; i++) { if (args[i] === '--city') cityPath = args[++i]; else dumps.push(args[i]); }
const city = parseCity(readFileSync(cityPath));
const { edges, crossings } = city;
const dem = load3dep();
const LAT0 = 35.18456015184093, LON0 = -80.81770185962013, M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const groundY = (x, z) => dem.sample(LAT0 + z / M_LAT, LON0 + x / M_LON) - 97.0;
const truth = new Map();
const truthAt = (ei, s) => { const k = ei + ':' + s; let v = truth.get(k); if (v === undefined) { v = sampleSection(edges[ei], +s, groundY); truth.set(k, v); } return v; };
const pct = (a, q) => a.length ? a[Math.min(a.length - 1, Math.floor(q * a.length))] : NaN;

// the spots: a crossing of two named roads (its point), or a way's middle
const findX = (overRe, underRe) => crossings.find(c => overRe.test(edges[c.over].name || '') && underRe.test(edges[c.under].name || ''));
const spots = [];
{
  const e372 = edges.find(e => e.wayId === 16662608);
  if (e372) { const p = pointAt(e372, e372.length * 0.6); spots.push({ label: 'Exit 3A ramp / N Davidson / E 12th', x: p[0], z: p[1], r: 260 }); }
  const w5 = findX(/^West 5th Street$/, /^I-77$/); if (w5) spots.push({ label: 'W 5th St over I-77', x: w5.x, z: w5.z, r: 150 });
  const br = findX(/^I-277$/, /^North Brevard Street$/); if (br) spots.push({ label: 'I-277 at N Brevard', x: br.x, z: br.z, r: 120 });
  const ca = findX(/^North Caldwell Street$/, /^I-277$/); if (ca) spots.push({ label: 'I-277 at N Caldwell', x: ca.x, z: ca.z, r: 120 });
  // one Independence Boulevard crossing: the nearest uptown
  let best = null, bd = Infinity;
  for (const c of crossings) {
    const o = edges[c.over].name || '', u = edges[c.under].name || '';
    if (!/Independence Boulevard/.test(o + '|' + u)) continue;
    const d = Math.hypot(c.x, c.z - 4000); if (d < bd) { bd = d; best = c; }
  }
  if (best) spots.push({ label: `Independence Blvd x ${edges[best.over].name === edges[best.under].name ? '' : (/Independence/.test(edges[best.over].name || '') ? edges[best.under].name : edges[best.over].name)}`, x: best.x, z: best.z, r: 150 });
}

for (const path of dumps) {
  const rows = readFileSync(path, 'utf8').split(/\r?\n/);
  const dev = { 1: [], 2: [], 3: [] }, above = { 1: 0, 2: 0, 3: 0 }, below = { 1: 0, 2: 0, 3: 0 };
  const untag = new Map(); let untagM = 0, structM = 0, prevEl = false, prevEi = -1;
  const spotRoads = spots.map(() => new Map());
  // station spacing per edge (for lengths)
  const cnt = new Map();
  for (let r = 1; r < rows.length; r++) { if (!rows[r]) continue; const ei = +rows[r].slice(0, rows[r].indexOf('\t')); cnt.set(ei, (cnt.get(ei) || 0) + 1); }
  for (let r = 1; r < rows.length; r++) {
    if (!rows[r]) continue;
    const [eis, , ss, , drawns, elev] = rows[r].split('\t');
    const ei = +eis, s = +ss, e = edges[ei]; if (!e) continue;
    const h = e.length / Math.max(1, cnt.get(ei) - 1);
    const t = tierOf(e.rank);
    const isEl = elev === '1';
    // a segment is structure when both its stations are
    if (isEl && prevEl && prevEi === ei) { structM += h; if (!e.bridge) { untagM += h; untag.set(ei, (untag.get(ei) || 0) + h); } }
    prevEl = isEl; prevEi = ei;
    const p = pointAt(e, s);
    const inSpot = spots.map(q => Math.hypot(p[0] - q.x, p[1] - q.z) <= q.r);
    if (isEl && !inSpot.some(Boolean)) continue;
    const d = +drawns - truthAt(ei, s);
    if (!isEl) {
      dev[t].push(Math.abs(d));
      if (d > 1.4) above[t] += h; else if (d < -1.4) below[t] += h;
    }
    for (let k = 0; k < spots.length; k++) {
      if (!inSpot[k]) continue;
      const key = e.name || (e.link ? `ramp e${ei}` : `e${ei}`);
      let v = spotRoads[k].get(key); if (!v) spotRoads[k].set(key, v = { up: -99, dn: 99, el: 0, elUntag: 0, bridge: false, n: 0 });
      v.n++;
      if (e.bridge) v.bridge = true;
      if (isEl) { v.el += h; if (!e.bridge) v.elUntag += h; }
      if (!e.bridge) { v.up = Math.max(v.up, d); v.dn = Math.min(v.dn, d); }
    }
  }
  console.log(`== ${path}`);
  for (const t of [1, 2, 3]) {
    const a = dev[t].sort((p, q) => p - q);
    console.log(`  T${t}: |drawn - 3DEP| off structure p50 ${pct(a, 0.5).toFixed(2)} / p95 ${pct(a, 0.95).toFixed(2)} m over ${a.length} stations; more than 1.4 m above ${(above[t] / 1000).toFixed(2)} km, below ${(below[t] / 1000).toFixed(2)} km`);
  }
  const top = [...untag].sort((a, b) => b[1] - a[1]).slice(0, 8).map(([ei, m]) => `e${ei} ${edges[ei].name || (edges[ei].link ? 'ramp' : '-')} ${m.toFixed(0)} m`);
  console.log(`  STRUCTURE ${(structM / 1000).toFixed(1)} km; on roads OSM does not tag as a bridge ${(untagM / 1000).toFixed(2)} km on ${untag.size} edges (${top.join(', ')})`);
  for (let k = 0; k < spots.length; k++) {
    const q = spots[k];
    console.log(`  SPOT ${q.label} (${q.x.toFixed(0)}, ${q.z.toFixed(0)}, ${q.r} m):`);
    for (const [name, v] of [...spotRoads[k]].sort((a, b) => b[1].up - a[1].up))
      console.log(`    ${name.padEnd(28)} over 3DEP max ${v.up > -98 ? v.up.toFixed(2) : '  - '} / min ${v.dn < 98 ? v.dn.toFixed(2) : '  - '} m off its tagged bridge; structure ${v.el > 0 ? `YES ${v.el.toFixed(0)} m` : 'no'}${v.elUntag > 0 ? ` (${v.elUntag.toFixed(0)} m untagged)` : ''}${v.bridge ? '; OSM bridge' : ''}`);
  }
}
