// THE SIGNALS' OWN TAGS (charlotte_sigtags.bytes), beside the graph (2026-11-15).
//
// TAGN (charlotte_city.bytes) carries every OSM control node and where it lies now
// (edge, s), but not what KIND of signal it is nor which way along its way the
// traffic it faces is going. Both decide what the game stands there:
//   * traffic_signals:direction (or direction) = forward | backward: a signal mapped
//     at a STOP LINE, on the approach, for the traffic going that way along the way.
//     It belongs to the junction that traffic drives into - not to the nearer end of
//     the piece of road it happens to lie on (CitySignals, PSX_CITY_SIGNALS2).
//   * traffic_signals = emergency (a fire station's signal: dark or flashing yellow
//     until the engines roll), pedestrian_crossing (a crossing's own signal: rests
//     green for the road), blinker (a flashing beacon: an all-way STOP's red
//     flasher). None of them is a junction's red-yellow-green signal.
// Read from the same Overpass cache the export read (tools/city/cache/nodes_all.json;
// pass another cache folder as the first argument), for every signal and give-way
// node TAGN holds, keyed by OSM node id: the graph file is not re-exported.
//
//   node tools/city/signal_tags.mjs [cacheDir]
//
// Layout (little-endian): "PSGT", i32 version 1, i32 count, then count x
// (u32 id lo, u32 id hi, u8 flags): bits 0-1 the direction (1 forward, 2 backward,
// 3 both), bits 4-6 the kind (0 a junction's signal, 1 emergency, 2 pedestrian
// crossing, 3 blinker). Only nodes with a flag are written.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..', '..');
const RES = path.join(ROOT, 'Assets', 'PSXRacing', 'Resources');
const CACHE = path.join(process.argv[2] || path.join(HERE, 'cache'), 'nodes_all.json');

const city = fs.readFileSync(path.join(RES, 'charlotte_city.bytes'));
if (city.readUInt32LE(0) !== 0x43585350) throw new Error('charlotte_city.bytes: bad magic');
const nsec = city.readUInt32LE(8);
let tagn = null;
for (let i = 0; i < nsec; i++) {
  const tag = city.toString('latin1', 12 + 12 * i, 16 + 12 * i);
  if (tag === 'TAGN') tagn = { off: city.readUInt32LE(16 + 12 * i), len: city.readUInt32LE(20 + 12 * i) };
}
if (!tagn) throw new Error('charlotte_city.bytes has no TAGN section');
const want = new Map();
{
  const n = city.readUInt32LE(tagn.off);
  let p = tagn.off + 4;
  for (let i = 0; i < n; i++, p += 25) {
    const lo = city.readUInt32LE(p), hi = city.readUInt32LE(p + 4), kind = city.readUInt8(p + 12);
    if (kind === 4 || kind === 1) want.set(hi * 4294967296 + lo, kind);
  }
}

const raw = JSON.parse(fs.readFileSync(CACHE, 'utf8'));
const els = Array.isArray(raw) ? raw : raw.elements;
const rows = [];
const stats = { signals: 0, giveWays: 0, forward: 0, backward: 0, both: 0, emergency: 0, pedestrian_crossing: 0, blinker: 0 };
for (const n of els) {
  const t = n.tags || {};
  const kind = want.get(n.id);
  if (!kind) continue;
  if (kind === 4 && t.highway !== 'traffic_signals') continue;
  if (kind === 1 && t.highway !== 'give_way') continue;
  if (kind === 4) stats.signals++; else stats.giveWays++;
  let f = 0;
  const d = (kind === 4 ? t['traffic_signals:direction'] : null) || t.direction || '';
  if (d === 'forward') { f |= 1; stats.forward++; }
  else if (d === 'backward') { f |= 2; stats.backward++; }
  else if (d === 'both' || d === 'all') { f |= 3; stats.both++; }
  if (kind === 4) {
    const k = t.traffic_signals || '';
    if (k === 'emergency') { f |= 1 << 4; stats.emergency++; }
    else if (k === 'pedestrian_crossing') { f |= 2 << 4; stats.pedestrian_crossing++; }
    else if (k === 'blinker') { f |= 3 << 4; stats.blinker++; }
  }
  if (f) rows.push([n.id, f]);
}
rows.sort((a, b) => a[0] - b[0]);
const buf = Buffer.alloc(12 + rows.length * 9);
buf.write('PSGT', 0, 'latin1');
buf.writeInt32LE(1, 4);
buf.writeInt32LE(rows.length, 8);
rows.forEach(([id, f], i) => {
  const p = 12 + i * 9;
  buf.writeUInt32LE(id % 4294967296, p);
  buf.writeUInt32LE(Math.floor(id / 4294967296), p + 4);
  buf.writeUInt8(f, p + 8);
});
fs.writeFileSync(path.join(RES, 'charlotte_sigtags.bytes'), buf);
console.log(`charlotte_sigtags.bytes: ${rows.length} of ${want.size} TAGN signal and give-way nodes carry a flag (${JSON.stringify(stats)}), ${buf.length} bytes`);
