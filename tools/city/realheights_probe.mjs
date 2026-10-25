// realheights_probe.mjs - REAL HEIGHTS (2026-10-25): every Charlotte road's
// designed profile off 3DEP (lib/roadprofile.mjs, opts.all), measured before
// it goes into the game: the clearance under every tagged bridge (deck top
// minus the road under it; the game's rule is ClearanceM 5.0 + DeckThick
// 0.55 = 5.55 m), and the named spots. Read-only.
//   node tools/city/realheights_probe.mjs [--city <charlotte_city.bytes>]
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, tierOf } from './lib/citydata.mjs';
import { load3dep } from './lib/dem3dep.mjs';
import { buildRoadProfiles, sampleSection } from './lib/roadprofile.mjs';
import { projectOn } from './lib/vertical.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
let cityPath = join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources', 'charlotte_city.bytes');
for (let i = 0; i < args.length; i++) if (args[i] === '--city') cityPath = args[++i];
const city = parseCity(readFileSync(cityPath));
const { edges, crossings } = city;
const dem = load3dep();
const LAT0 = 35.18456015184093, LON0 = -80.81770185962013, M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const groundY = (x, z) => dem.sample(LAT0 + z / M_LAT, LON0 + x / M_LON) - 97.0;
const t0 = Date.now();
const rp = buildRoadProfiles(city, groundY, { all: true });
for (const l of rp.lines) console.log(l);
console.log(`designed in ${((Date.now() - t0) / 1000).toFixed(1)} s`);
const P = rp.prof;
const yAt = (ei, s) => { const p = P[ei]; if (!p) return NaN; const n = p.y.length, e = edges[ei]; const f = Math.max(0, Math.min(1, s / Math.max(1e-3, e.length))) * (n - 1); const k = Math.min(n - 2, Math.floor(f)); return p.y[k] + (p.y[k + 1] - p.y[k]) * (f - k); };
const rawAt = (ei, s) => sampleSection(edges[ei], s, groundY);
// clearance under every tagged bridge
const NEED = 5.55;
const short = [];
let n = 0;
for (const c of crossings) {
  const sO = projectOn(edges[c.over], [c.x, c.z]), sU = projectOn(edges[c.under], [c.x, c.z]);
  const yO = yAt(c.over, sO), yU = yAt(c.under, sU);
  if (Number.isNaN(yO) || Number.isNaN(yU)) continue;
  n++;
  const cl = yO - yU;
  if (cl < NEED) short.push([NEED - cl, c, sO, sU, yO, yU]);
}
short.sort((a, b) => b[0] - a[0]);
const hist = [0.25, 0.5, 1, 2, 3, 5, 99].map(t => short.filter(s => s[0] <= t).length);
console.log(`CLEARANCE: ${n} crossings, ${short.length} short of ${NEED} m deck-top over the road under (deficit <=0.25 ${hist[0]}, <=0.5 ${hist[1]}, <=1 ${hist[2]}, <=2 ${hist[3]}, <=3 ${hist[4]}, <=5 ${hist[5]}, more ${short.length - hist[5]})`);
for (const [d, c, sO, sU, yO, yU] of short.slice(0, 15)) {
  const O = edges[c.over], U = edges[c.under];
  console.log(`  -${d.toFixed(2)} m: over e${O.index} ${O.name || (O.link ? 'ramp' : '-')} r${O.rank} (deck ${yO.toFixed(2)}, raw ${rawAt(O.index, sO).toFixed(2)}) under e${U.index} ${U.name || (U.link ? 'ramp' : '-')} r${U.rank} (${yU.toFixed(2)}, raw ${rawAt(U.index, sU).toFixed(2)}) at (${c.x.toFixed(0)}, ${c.z.toFixed(0)})`);
}
// the spots: designed vs raw along each edge
const spot = (label, ei) => {
  const e = edges[ei], p = P[ei]; if (!p) { console.log(`${label} e${ei}: not designed`); return; }
  const k = [0, Math.floor(p.y.length / 4), Math.floor(p.y.length / 2), Math.floor(3 * p.y.length / 4), p.y.length - 1];
  console.log(`${label} e${ei} ${e.name || '-'}${e.bridge ? ' BRIDGE' : ''} ${e.length.toFixed(0)} m: ` + k.map(i => `s${p.s[i].toFixed(0)} ${p.y[i].toFixed(2)}/${p.raw[i].toFixed(2)}${p.mask[i] ? 'm' : ''}`).join('  '));
};
for (const [l, w] of [['exit3A', 16662608], ['Davidson deck', 1181521353], ['Davidson link deck', 159022559], ['I-277 over Brevard', 159022503]])
  for (const e of edges.filter(e => e.wayId === w)) spot(l, e.index);
