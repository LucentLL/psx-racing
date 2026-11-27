// ROAD SURVEY stage 1: the game's road graph as plain JSON for survey.py (no geometry work here).
//   node tools/city/survey/graph_dump.mjs OUT.json
// Reads Resources/charlotte_city.bytes with the shared parser (lib/citydata.mjs). Per edge: a, b, name, class,
// one-way / link / bridge / tunnel / turn / roundabout, the map's lane count, the RULE's cross-section (the class
// profile: width, shoulders; LineModel.Extents is width / 2 each side where no ease acts) and the polyline (x east,
// z north, metres). Per node: x, z. Also the game's lat / lon mapping (export_osm.mjs: LAT0, LON0, M_LAT, M_LON).
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, classOf } from '../lib/citydata.mjs';
import { LAT0, LON0, M_LAT, M_LON } from '../lib/canopygrid.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const RES = join(HERE, '..', '..', '..', 'Assets', 'PSXRacing', 'Resources');
const out = process.argv[2];
if (!out) { console.error('usage: node graph_dump.mjs OUT.json'); process.exit(2); }
const city = parseCity(readFileSync(join(RES, 'charlotte_city.bytes')));
const r2 = v => Math.round(v * 100) / 100;
const J = {
  map: { LAT0, LON0, M_LAT, M_LON }, uptown: city.uptown,
  nodes: city.nodes.map(n => [r2(n.x), r2(n.z), n.ctl]),
  edges: city.edges.map(e => ({
    i: e.index, a: e.a, b: e.b, name: e.name, cls: classOf(e), rank: e.rank, ow: e.oneway ? 1 : 0, link: e.link ? 1 : 0,
    br: e.bridge ? 1 : 0, tu: e.tunnel ? 1 : 0, turn: e.turn ? 1 : 0, rb: e.roundabout ? 1 : 0, lanes: e.lanes, lvl: e.level,
    w: r2(e.width), shl: e.profile.shl, shr: e.profile.shr, key: e.profile.key, way: e.wayId,
    pts: e.pts.map(p => [r2(p[0]), r2(p[1])]),
  })),
};
writeFileSync(out, JSON.stringify(J));
console.log(`graph: ${J.nodes.length} nodes, ${J.edges.length} edges -> ${out}`);
