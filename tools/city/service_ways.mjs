// service_ways.mjs - writes Assets/PSXRacing/Resources/charlotte_service.bytes: the
// service ways of the map extract that are not road-graph edges, as pavement
// lines and mouths (tools/city/lib/serviceways.mjs has the rules and the file
// layout; Scripts/City/CityMeshes.ServiceWays.cs reads it at load).
//
//   node tools/city/service_ways.mjs [--cache <dir>] [--out <file>] [--report <file>]
//
// Reads charlotte_city.bytes and charlotte_bld.bytes as they are (the road graph
// is NOT exported again: its hash is printed, and written into the file so the
// game refuses a file made for another graph). --cache: the extract
// (default tools/city/cache; it is not in a worktree: pass the main checkout's,
// read-only). Run it again after any export that changes the graph, the lots
// or the buildings.
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import * as cd from './lib/citydata.mjs';
import * as sw from './lib/serviceways.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..', '..');
const arg = (name, def) => { const i = process.argv.indexOf('--' + name); return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : def; };
const CACHE = arg('cache', process.env.PSX_CITY_CACHE || join(HERE, 'cache'));
const RES = join(ROOT, 'Assets', 'PSXRacing', 'Resources');
const OUT = arg('out', join(RES, 'charlotte_service.bytes'));
const REPORT = arg('report', '');
// the steep places of the game's own ground under the ways (x z along across, by the editor's
// CityAudit.ServiceOnly with PSX_SERVICE_STEEP_OUT): kept in the repo so the step is repeatable
const STEEP = arg('steep', join(HERE, 'service_steep.tsv'));
if (!existsSync(join(CACHE, 'streets_core.json'))) throw new Error(`no extract at ${CACHE} (streets_core.json): pass --cache <the main checkout's tools/city/cache>`);

const t0 = Date.now();
const cityBuf = readFileSync(join(RES, 'charlotte_city.bytes'));
const city = cd.parseCity(cityBuf);
const bld = cd.parseBld(readFileSync(join(RES, 'charlotte_bld.bytes')));
const lots = sw.parseLots(cityBuf, city);
const hash = cd.hashHex(cd.graphHash(city.edges));
console.log(`road graph: ${city.nodes.length} nodes, ${city.edges.length} edges, graph hash ${hash} (read, not written)`);
const ways = sw.loadExtract(CACHE);
const steep = existsSync(STEEP) ? readFileSync(STEEP, 'utf8').split(/\r?\n/).map(l => l.trim().split(/\s+/).map(Number)).filter(r => r.length >= 2 && Number.isFinite(r[0]) && Number.isFinite(r[1])) : [];
console.log(`steep places: ${steep.length} (${existsSync(STEEP) ? STEEP : 'no file'})`);
const res = sw.buildServiceWays({ city, bld, lots, ways, looseAisles: process.env.PSX_SERVICE_LOOSE_AISLES !== '0', steep });
const st = res.stats;
const bytes = sw.serviceBytes(res, city);
writeFileSync(OUT, bytes);
const lines = [];
const P = s => { lines.push(s); console.log(s); };
P(`service ways: ${st.pieces} pieces, ${st.km.toFixed(1)} km, ${st.points} points, ${st.mouths} mouths (${st.mouthsMerged} merged, ${st.mouthCornerMoved} moved off a corner); ${st.inLotKm.toFixed(1)} km on a lot's own asphalt; ${st.pinchedKm.toFixed(1)} km narrowed between buildings`);
sw.KINDS.forEach((k, i) => { const b = st.byKind[i]; if (b.ways) P(`  ${k}: ${b.ways} pieces, ${b.km.toFixed(1)} km, ${b.mouths} mouths`); });
P(`left out: ${st.notSurface} ways not on the surface (${st.notSurfaceKm.toFixed(1)} km), ${st.notTaken} not for cars (${st.notTakenKm.toFixed(1)} km), ${st.cutBuilding} stops at a building (${st.cutBuildingKm.toFixed(1)} km), ${st.cutWater} at water (${st.cutWaterKm.toFixed(1)} km), ${st.cutSteep} at a bank (${st.cutSteepKm.toFixed(1)} km), ` +
  `${st.orphanPieces} pieces that join no street and no lot (${st.orphanKm.toFixed(1)} km), ${st.mouthCorner} mouths inside a junction, ${st.mouthFreeway} on a freeway or ramp; ${st.aislesDrawn} aisles already lot pavement (${st.aislesDrawnKm.toFixed(1)} km)`);
P(`wrote ${OUT}: ${bytes.length} bytes, for graph ${hash}, in ${((Date.now() - t0) / 1000).toFixed(1)} s`);
if (REPORT) {
  writeFileSync(REPORT, lines.join('\n') + '\n\nway\twhy\tm\tlat\tlon\n' + res.left.map(l => `${l.id}\t${l.why}\t${l.m.toFixed(1)}\t${sw.toLat(l.z).toFixed(6)}\t${sw.toLon(l.x).toFixed(6)}`).join('\n') + '\n');
  console.log('report', REPORT);
}
