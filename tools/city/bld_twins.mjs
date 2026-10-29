// TWIN FOOTPRINTS on the shipped data (lib/twins.mjs says what a twin is).
// export_osm.mjs flags them as it writes; this flags them in a
// charlotte_bld.bytes already written, in place: only bit 0 (hidden) of each
// twin's flag byte changes - same size, same records, same indices.
//
//   node tools/city/bld_twins.mjs            report (no write)
//   node tools/city/bld_twins.mjs --write    flag the twins in the shipped file
//   node tools/city/bld_twins.mjs --file <charlotte_bld.bytes> [--dem <charlotte_dem.bytes>] [--write]
import { readFileSync, writeFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';
import { parseDem } from './lib/citydata.mjs';
import { findTwins } from './lib/twins.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const RES = join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources');
const arg = k => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : null; };
const file = arg('--file') || join(RES, 'charlotte_bld.bytes');
const demFile = arg('--dem') || join(RES, 'charlotte_dem.bytes');
const write = process.argv.includes('--write');

// PBLD v3, read with each record's flag byte offset
const buf = readFileSync(file);
let p = 0;
const u8 = () => buf.readUInt8(p++), u16 = () => { const v = buf.readUInt16LE(p); p += 2; return v; };
const i32 = () => { const v = buf.readInt32LE(p); p += 4; return v; }, u32 = () => { const v = buf.readUInt32LE(p); p += 4; return v; };
const f32 = () => { const v = buf.readFloatLE(p); p += 4; return v; };
if (u32() !== 0x444C4250) throw new Error(file + ': bad magic');
if (i32() !== 3) throw new Error(file + ': not PBLD v3');
p += 16;
const n = u32();
const fps = new Array(n);
for (let i = 0; i < n; i++) {
  const f = { style: u8() & 0x7f, h: f32() };
  const lb = u8(); if (lb & 0x80) p += 3;
  f.flagAt = p; const xb = u8();
  f.hidden = (xb & 1) !== 0; f.part = (xb & 2) !== 0;
  if (xb & 0x80) { f.minH = u16() / 10; p += 2 + 2 + 1; f.outline = i32(); }
  const np = u8(); f.pts = new Array(np);
  for (let k = 0; k < np; k++) f.pts[k] = [f32(), f32()];
  fps[i] = f;
}
if (p !== buf.length) throw new Error(file + ': trailing bytes');

const dem = parseDem(readFileSync(demFile));
const { twins, left, pairs } = findTwins(fps, (x, z) => dem.at(x, z));
const fresh = twins.filter(t => !fps[t.i].hidden);
const m2 = k => Math.round(twins.filter(t => t.kind === k).reduce((s, t) => s + t.m2, 0));
console.log(`${n} footprints, ${pairs} pairs with a shared face; twins: ${twins.length} ` +
            `(same outline ${twins.filter(t => t.kind === 'same').length}, ${m2('same')} m2 of doubled wall; ` +
            `inside another ${twins.filter(t => t.kind === 'inside').length}, ${m2('inside')} m2), ${fresh.length} not yet flagged; ` +
            `closest inside call ${Math.min(...twins.filter(t => t.kind === 'inside').map(t => t.margin)).toFixed(2)} m under the slack`);
for (const t of twins.slice(0, 60)) {
  const f = fps[t.i], k = fps[t.keeper];
  console.log(`  ${t.kind.padEnd(6)} ${t.i} (h ${f.h.toFixed(1)}) under ${t.keeper} (h ${k.h.toFixed(1)}) ${Math.round(t.m2)} m2 at ${f.pts[0].map(v => v.toFixed(0)).join(',')}`);
}
console.log(`left drawn (their shared walls need cutting): ${left.length} - ` +
            left.map(l => `${l.kind} ${l.a}/${l.b} ${Math.round(l.m2)} m2 at ${fps[l.a].pts[0].map(v => v.toFixed(0)).join(',')}`).join('; '));
if (write && fresh.length) {
  for (const t of fresh) buf[fps[t.i].flagAt] |= 1;
  writeFileSync(file, buf);
  console.log(`flagged ${fresh.length} twins hidden in ${file}`);
}
