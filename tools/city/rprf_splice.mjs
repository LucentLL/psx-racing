// rprf_splice.mjs - REAL HEIGHTS iteration aid (2026-10-25): design the
// measured profiles (lib/roadprofile.mjs, every road) on an existing
// charlotte_city.bytes and write it back with section RPRF replaced -
// exactly what `export_osm.mjs` writes (it designs on its own assembled
// file the same way), in ~30 s instead of the full ~5 min export. The
// export stays the source of truth: run `export_osm.mjs --check` before a
// commit.
//   node tools/city/rprf_splice.mjs [--in <charlotte_city.bytes>] [--out <path>] [--freeways]
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity } from './lib/citydata.mjs';
import { load3dep } from './lib/dem3dep.mjs';
import { buildRoadProfiles, writeRprf } from './lib/roadprofile.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const val = n => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : null; };
const inPath = val('--in') || join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources', 'charlotte_city.bytes');
const outPath = val('--out') || inPath;
const freeways = args.includes('--freeways');

class Writer {
  constructor() { this.b = []; }
  push(n, f) { const x = Buffer.alloc(n); f(x); this.b.push(x); }
  u8(v) { this.push(1, x => x.writeUInt8(v & 255)); }
  i8(v) { this.push(1, x => x.writeInt8(Math.max(-128, Math.min(127, v)))); }
  u16(v) { this.push(2, x => x.writeUInt16LE(v & 65535)); }
  i16(v) { this.push(2, x => x.writeInt16LE(v)); }
  u32(v) { this.push(4, x => x.writeUInt32LE(v >>> 0)); }
  bytes() { return Buffer.concat(this.b); }
}

const buf = readFileSync(inPath);
if (buf.readUInt32LE(0) !== 0x43585350) throw new Error('not PSXC');
const n = buf.readUInt32LE(8);
const sec = new Map();
for (let k = 0; k < n; k++) {
  const tag = buf.toString('latin1', 12 + 12 * k, 16 + 12 * k), off = buf.readUInt32LE(16 + 12 * k), len = buf.readUInt32LE(20 + 12 * k);
  if (tag !== 'RPRF') sec.set(tag, buf.subarray(off, off + len));
}
const assemble = () => {
  const headLen = 12 + 12 * sec.size, head = Buffer.alloc(headLen);
  head.writeUInt32LE(0x43585350, 0); head.writeInt32LE(2, 4); head.writeUInt32LE(sec.size, 8);
  const parts = [head]; let at = headLen, k = 0;
  for (const [tag, body] of sec) {
    const off = (at + 3) & ~3;
    if (off > at) parts.push(Buffer.alloc(off - at));
    head.write(tag, 12 + 12 * k, 4, 'latin1'); head.writeUInt32LE(off, 16 + 12 * k); head.writeUInt32LE(body.length, 20 + 12 * k);
    parts.push(body); at = off + body.length; k++;
  }
  return Buffer.concat(parts);
};
const graph = parseCity(assemble());
const dem = load3dep();
const LAT0 = 35.18456015184093, LON0 = -80.81770185962013, M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const t0 = Date.now();
const rp = buildRoadProfiles(graph, (x, z) => dem.sample(LAT0 + z / M_LAT, LON0 + x / M_LON) - 97.0, { all: !freeways });
for (const l of rp.lines) console.log(l);
const prof = freeways ? rp.prof.map((p, ei) => p && graph.edges[ei].rank >= 4 && !graph.edges[ei].link ? p : null) : rp.prof;
const w = new Writer(); writeRprf(w, prof);
sec.set('RPRF', w.bytes());
const out = assemble();
writeFileSync(outPath, out);
console.log(`RPRF ${(sec.get('RPRF').length / 1024).toFixed(0)} KB for ${prof.filter(Boolean).length} edges (${((Date.now() - t0) / 1000).toFixed(1)} s); ${outPath} ${(buf.length / 1024).toFixed(0)} -> ${(out.length / 1024).toFixed(0)} KB`);
