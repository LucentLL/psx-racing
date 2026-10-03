// heights_check.mjs - HEIGHT: the game's solved road heights against USGS 3DEP
// lidar (leftover item 1, 2026-10-03; plan B7's metric).
//
// Reads one or more station dumps written by the heights probe
// (Editor/CityAudit.Profile.cs HeightsOnly: heights_on.tsv / heights_off.tsv,
// every tier-1/2 station: edge, i, s, y, elev, ground) and samples the 3DEP
// 1/3" bare earth at the same points the exporter does (the median of five
// across the road, lib/roadprofile.mjs sampleSection). Counted: the stations
// the lidar sees the road at (RPRF's ground flag) that the game also keeps on
// the ground (not structure). Reported per tier: |game - 3DEP| p50 / p95 / max
// and the share past 1.5 m; the gate (plan B7) is T1 p50 <= 0.5 m, p95 <= 1.5 m.
//
//   node tools/city/heights_check.mjs <heights_off.tsv> [<heights_on.tsv> ...]
//        [--city Assets/PSXRacing/Resources/charlotte_city.bytes]

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, tierOf } from './lib/citydata.mjs';
import { load3dep } from './lib/dem3dep.mjs';
import { sampleSection } from './lib/roadprofile.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
let cityPath = join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources', 'charlotte_city.bytes');
const dumps = [];
for (let i = 0; i < args.length; i++) { if (args[i] === '--city') cityPath = args[++i]; else dumps.push(args[i]); }
if (!dumps.length) { console.error('usage: node tools/city/heights_check.mjs <heights_*.tsv> ... [--city <charlotte_city.bytes>]'); process.exit(2); }

const city = parseCity(readFileSync(cityPath));
if (!city.rprfEdges) { console.error(`${cityPath} has no RPRF section: nothing says where the lidar sees the road`); process.exit(2); }
const dem = load3dep();
// the export frame (export_osm.mjs) and the pinned datum
const LAT0 = 35.18456015184093, LON0 = -80.81770185962013, M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const DATUM = 97.0;
const groundY = (x, z) => dem.sample(LAT0 + z / M_LAT, LON0 + x / M_LON) - DATUM;
const truth = new Map();
const truthAt = (ei, s) => { const k = ei + ':' + s; let v = truth.get(k); if (v === undefined) { v = sampleSection(city.edges[ei], +s, groundY); truth.set(k, v); } return v; };
const nearestGround = (e, s) => { const g = e.rprf.ground, n = g.length; const k = Math.max(0, Math.min(n - 1, Math.round((e.length > 1e-4 ? Math.min(1, Math.max(0, s / e.length)) : 0) * (n - 1)))); return g[k] === 1; };

const pct = (a, q) => a.length ? a[Math.min(a.length - 1, Math.floor(q * a.length))] : NaN;
let gateOk = true;
for (const path of dumps) {
  const rows = readFileSync(path, 'utf8').split(/\r?\n/);
  const dev = { 1: [], 2: [] }, signed = { 1: [], 2: [] };
  let skipped = 0;
  for (let r = 1; r < rows.length; r++) {
    if (!rows[r]) continue;
    const [ei, , s, y, elev] = rows[r].split('\t');
    const e = city.edges[+ei];
    if (!e || !e.rprf) { skipped++; continue; }
    if (elev === '1' || !nearestGround(e, +s)) continue;
    const t = tierOf(e.rank); if (t > 2) continue;
    const d = +y - truthAt(+ei, s);
    dev[t].push(Math.abs(d)); signed[t].push(d);
  }
  console.log(`HEIGHT ${path}: game minus 3DEP on stations the lidar sees and the game keeps on the ground${skipped ? ` (${skipped} rows on edges without RPRF skipped)` : ''}`);
  for (const t of [1, 2]) {
    const a = dev[t].sort((p, q) => p - q), sg = signed[t].sort((p, q) => p - q);
    const over = a.filter(v => v > 1.5).length;
    console.log(`  T${t}: ${a.length} stations; |dy| p50 ${pct(a, 0.5).toFixed(3)} / p95 ${pct(a, 0.95).toFixed(3)} / max ${a.length ? a[a.length - 1].toFixed(2) : '-'} m; past 1.5 m ${over} (${(100 * over / Math.max(1, a.length)).toFixed(1)}%); signed p05 ${pct(sg, 0.05).toFixed(2)} p50 ${pct(sg, 0.5).toFixed(2)} p95 ${pct(sg, 0.95).toFixed(2)} m`);
    if (t === 1) gateOk = pct(a, 0.5) <= 0.5 && pct(a, 0.95) <= 1.5;
  }
  console.log(`  gate (T1 p50 <= 0.5 m, p95 <= 1.5 m): ${gateOk ? 'PASS' : 'FAIL'}`);
}
process.exit(gateOk ? 0 : 1);
