// THE STOP SIGNS' OWN TAGS (charlotte_stops.bytes), beside the graph.
//
// WP-10 resolved every OSM control node onto the raw ways and the export
// carries them in charlotte_city.bytes, section TAGN: node id, way, kind
// (4 signal, 2 stop, 1 give way) and where it lies now (edge, s). What TAGN
// does not carry is what a STOP means at a junction node: `stop=all` (an
// all-way stop), `stop=minor`, and `direction=forward|backward` (which way
// along its way the traffic that stops is going). This reads them from the
// same Overpass cache the export read (tools/city/cache/nodes_all.json) for
// every stop node TAGN holds, and writes them keyed by OSM node id, so the
// graph file is not re-exported for them (CitySignals reads both).
//
//   node tools/city/stop_tags.mjs
//
// Layout (little-endian): "PSTP", i32 version 1, i32 count, then count x
// (u32 id lo, u32 id hi, u8 flags): 1 stop=all, 2 direction=forward,
// 4 direction=backward, 8 stop=minor.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..', '..');
const RES = path.join(ROOT, 'Assets', 'PSXRacing', 'Resources');
const CACHE = path.join(HERE, 'cache', 'nodes_all.json');

// the TAGN section of the graph the game ships
const city = fs.readFileSync(path.join(RES, 'charlotte_city.bytes'));
if (city.readUInt32LE(0) !== 0x43585350) throw new Error('charlotte_city.bytes: bad magic');
const nsec = city.readUInt32LE(8);
let tagn = null;
for (let i = 0; i < nsec; i++) {
  const tag = city.toString('latin1', 12 + 12 * i, 16 + 12 * i);
  if (tag === 'TAGN') tagn = { off: city.readUInt32LE(16 + 12 * i), len: city.readUInt32LE(20 + 12 * i) };
}
if (!tagn) throw new Error('charlotte_city.bytes has no TAGN section');
const want = new Set();
{
  const n = city.readUInt32LE(tagn.off);
  let p = tagn.off + 4;
  for (let i = 0; i < n; i++, p += 25) {
    const lo = city.readUInt32LE(p), hi = city.readUInt32LE(p + 4), kind = city.readUInt8(p + 12);
    if (kind === 2) want.add(hi * 4294967296 + lo);
  }
}

const raw = JSON.parse(fs.readFileSync(CACHE, 'utf8'));
const els = Array.isArray(raw) ? raw : raw.elements;
const rows = [];
const stats = { all: 0, minor: 0, forward: 0, backward: 0, plain: 0 };
for (const n of els) {
  const t = n.tags || {};
  if (t.highway !== 'stop' || !want.has(n.id)) continue;
  let f = 0;
  if (t.stop === 'all') { f |= 1; stats.all++; }
  if (t.stop === 'minor') { f |= 8; stats.minor++; }
  if (t.direction === 'forward') { f |= 2; stats.forward++; }
  else if (t.direction === 'backward') { f |= 4; stats.backward++; }
  if (!f) stats.plain++;
  rows.push([n.id, f]);
}
rows.sort((a, b) => a[0] - b[0]);
const buf = Buffer.alloc(12 + rows.length * 9);
buf.write('PSTP', 0, 'latin1');
buf.writeInt32LE(1, 4);
buf.writeInt32LE(rows.length, 8);
rows.forEach(([id, f], i) => {
  const p = 12 + i * 9;
  buf.writeUInt32LE(id % 4294967296, p);
  buf.writeUInt32LE(Math.floor(id / 4294967296), p + 4);
  buf.writeUInt8(f, p + 8);
});
fs.writeFileSync(path.join(RES, 'charlotte_stops.bytes'), buf);
console.log(`charlotte_stops.bytes: ${rows.length} of ${want.size} TAGN stop nodes tagged (${JSON.stringify(stats)}), ${buf.length} bytes`);
