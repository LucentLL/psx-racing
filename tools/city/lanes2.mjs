// lanes2.mjs - LANES2 (2026-11-15, the owner: "roads should be the newest 2026 version"):
// splice what an export WITH the lane table (PSX_EXPORT_LANES2=1, tools/city/lanes2_overrides.tsv)
// does differently into the shipped charlotte_city.bytes as section LAN2, and nothing else.
//
// Why a section and not a re-export: the shipped file's own sections stay byte for byte what
// they were (the graph, its hash, every (edge, s) key, PSX_CITY_LANES2=0 = the picture as it
// was), and the game applies LAN2 at load: the changed edges' lane count, centre-lane flag and
// line set, and the whole taper / ribbon-offset list as the exporter designed it for those
// counts. The points are NOT taken from the override export: the graph never moves.
//
//   LAN2  u32 version (1) | u32 n | n x { u32 edge; u8 lanes; u8 turn; u8 nF; u8 nB; u8 centre;
//         u8 lsFlags; u16 turnOnly; u8 sub; u8 bike; u16 parking }   (16 bytes a row; parking: 1 left of a->b, 2 right; 4 / 8 a bike lane left / right; 16 / 32 a paved shoulder)
//         | the override export's TAPR section, verbatim
//         | the override export's SPLT section, verbatim (a split's carriageway offsets follow the lane count)
//
//   node tools/city/lanes2.mjs --with <dir of the override export> [--in <charlotte_city.bytes>] [--out <path>] [--dry]
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, graphHash, hashHex } from './lib/citydata.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const val = n => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : null; };
const inPath = val('--in') || join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources', 'charlotte_city.bytes');
const withDir = val('--with');
const outPath = val('--out') || inPath;
const dry = args.includes('--dry');
// ON-STREET PARKING (CityMeshes.Parking): a parking lane on a way's left / right, from the map's own parking tags
// (--cache <the Overpass cache>: ways_all.json, streets_core.json) and from tools/city/parking2.tsv (way, both|left|right,
// the evidence; 'none' drops a tagged way). The bits go into a row's last u16: 1 the left of a->b, 2 the right.
const cacheDir = val('--cache');
const parkOfWay = new Map();
{
  const bits = v => v === 'both' ? 3 : v === 'left' ? 1 : v === 'right' ? 2 : 0;
  let tagged = 0, listed = 0, dropped = 0;
  if (cacheDir) {
    const yes = v => !!v && /^(lane|street_side|parallel|diagonal|perpendicular|marked|yes|on_street)$/.test(v);
    for (const f of ['ways_all.json', 'streets_core.json'])
      for (const w of JSON.parse(readFileSync(join(cacheDir, f), 'utf8')).elements) {
        if (w.type !== 'way' || !w.tags) continue; const t = w.tags; let b = 0;
        if (yes(t['parking:lane:both']) || yes(t['parking:both'])) b |= 3;
        if (yes(t['parking:lane:left']) || yes(t['parking:left'])) b |= 1;
        if (yes(t['parking:lane:right']) || yes(t['parking:right'])) b |= 2;
        if (b && !parkOfWay.has(w.id)) { parkOfWay.set(w.id, b); tagged++; }
      }
  }
  const pf = join(HERE, 'parking2.tsv');
  if (existsSync(pf)) for (const line of readFileSync(pf, 'utf8').split(/\r?\n/)) {
    if (!line.trim() || line.startsWith('#')) continue;
    const c = line.split('\t'); const id = parseInt(c[0], 10), b = bits((c[1] || '').trim());
    if (Number.isFinite(id) && (c[1] || '').trim() === 'none') { if (parkOfWay.delete(id)) dropped++; continue; }   // a tagged way that cannot take the lane (a building in its width)
    if (Number.isFinite(id) && b) { parkOfWay.set(id, (parkOfWay.get(id) || 0) | b); listed++; }
  }
  console.log(`parking: ${parkOfWay.size} ways (${tagged} by the map's parking tags${cacheDir ? '' : ' - no --cache given'}, ${listed} rows of parking2.tsv; ${dropped} tagged ways dropped by its 'none' rows)`);
}
// ROADSIDE STRIPS (CityMeshes.Parking): a bike lane or a paved shoulder beside the travel lanes. tools/city/strips2.tsv:
//   name:<road name> <tab> tags <tab> evidence     every piece of this road takes the map's own cycleway tags (LSET's bike bits)
//   <way id> <tab> bike|shoulder <tab> evidence    both sides of this way: seen in a view, not tagged
// Rows starting "#" are only listed. The bits go above the parking bits: 4 / 8 a bike lane left / right of a->b, 16 / 32 a shoulder.
const stripNames = new Set(), stripWays = new Map();
{
  const sf = join(HERE, 'strips2.tsv');
  if (existsSync(sf)) for (const line of readFileSync(sf, 'utf8').split(/\r?\n/)) {
    if (!line.trim() || line.startsWith('#')) continue;
    const c = line.split('\t'); const k = (c[0] || '').trim(), v = (c[1] || '').trim();
    if (k.startsWith('name:')) stripNames.add(k.slice(5).trim());
    else { const id = parseInt(k, 10); if (Number.isFinite(id)) stripWays.set(id, v === 'shoulder' ? 48 : 12); }
  }
  console.log(`strips: ${stripNames.size} roads take their bike tags, ${stripWays.size} ways by strips2.tsv's own rows`);
}
if (!withDir) { console.error('usage: node tools/city/lanes2.mjs --with <dir of the override export> [--in file] [--out file] [--dry]'); process.exit(2); }

function table(buf) {
  if (buf.readUInt32LE(0) !== 0x43585350 || buf.readInt32LE(4) !== 2) throw new Error('not a PSXC v2 file');
  const n = buf.readUInt32LE(8); const secs = [];
  for (let k = 0; k < n; k++) secs.push({ tag: buf.toString('latin1', 12 + 12 * k, 16 + 12 * k), off: buf.readUInt32LE(16 + 12 * k), len: buf.readUInt32LE(20 + 12 * k) });
  return secs;
}
const bytesOf = (buf, secs, tag) => { const s = secs.find(x => x.tag === tag); return s ? buf.subarray(s.off, s.off + s.len) : null; };

let base = readFileSync(inPath);
let bs = table(base);
// a file that already carries LAN2: take it out first (the splice is repeatable)
if (bs.some(s => s.tag === 'LAN2')) { base = rebuild(base, bs.filter(s => s.tag !== 'LAN2'), null); bs = table(base); }
const ovr = readFileSync(join(withDir, 'charlotte_city.bytes'));
const os = table(ovr);

// ---- what differs, section by section
const same = [], differ = [];
for (const s of bs) { const a = bytesOf(base, bs, s.tag), b = bytesOf(ovr, os, s.tag); (b && a.equals(b) ? same : differ).push(s.tag); }
console.log(`sections identical: ${same.join(' ')}`);
console.log(`sections that differ: ${differ.join(' ') || '(none)'}`);

const cb = parseCity(base), co = parseCity(ovr);
if (cb.edges.length !== co.edges.length || cb.nodes.length !== co.nodes.length) throw new Error(`the override export has another graph: ${co.nodes.length} nodes / ${co.edges.length} edges against ${cb.nodes.length} / ${cb.edges.length}`);
for (let i = 0; i < cb.edges.length; i++) if (cb.edges[i].a !== co.edges[i].a || cb.edges[i].b !== co.edges[i].b || cb.edges[i].wayId !== co.edges[i].wayId) throw new Error(`edge ${i} is another edge in the override export`);
const hb = hashHex(graphHash(cb.edges)), ho = hashHex(graphHash(co.edges));
let moved = 0, movedMax = 0;
for (let i = 0; i < cb.edges.length; i++) {
  const P = cb.edges[i].pts, Q = co.edges[i].pts; let d = P.length !== Q.length ? 1e9 : 0;
  if (d === 0) for (let k = 0; k < P.length; k++) d = Math.max(d, Math.hypot(P[k][0] - Q[k][0], P[k][1] - Q[k][1]));
  if (d > 1e-4) { moved++; if (d < 1e8) movedMax = Math.max(movedMax, d); }
}
console.log(`graph hash: shipped ${hb}, the override export ${ho}; edges whose points the override export would move: ${moved}${moved ? ` (largest ${movedMax.toFixed(2)} m; NOT taken: the shipped points stay)` : ''}`);

// ---- the changed edges
const rows = []; const byName = new Map(); let km = 0; let parkEdges = 0, parkM = 0; const parkNames = new Map(); let stripEdges = 0, stripM = 0; const stripRoads = new Map();
for (let i = 0; i < cb.edges.length; i++) {
  const a = cb.edges[i], b = co.edges[i];
  const la = a.lset || {}, lb = b.lset || {};
  const lsSame = la.nF === lb.nF && la.nB === lb.nB && la.centre === lb.centre && la.flags === lb.flags && la.turnOnly === lb.turnOnly && la.sub === lb.sub && la.bike === lb.bike;
  const side = !a.link && !a.bridge && !a.tunnel && a.rank <= 3;
  const park = side ? (parkOfWay.get(a.wayId) || 0) : 0;
  let strip = 0;
  if (side) { if (stripNames.has(a.name)) strip |= ((lb.bike || 0) & 15) << 2; strip |= stripWays.get(a.wayId) || 0; }
  if (a.lanes === b.lanes && a.turn === b.turn && lsSame && !park && !strip) continue;
  rows.push({ i, b, lb, park: park | strip });
  if (strip) { stripEdges++; const m = a.length * ([4, 8, 16, 32].filter(q => strip & q).length > 1 && (strip & 20) && (strip & 40) ? 2 : 1); stripM += m; stripRoads.set(a.name || '(unnamed)', (stripRoads.get(a.name || '(unnamed)') || 0) + m); }
  if (park) { parkEdges++; parkM += a.length * (park === 3 ? 2 : 1); parkNames.set(a.name || '(unnamed)', (parkNames.get(a.name || '(unnamed)') || 0) + a.length); }
  if (a.profile.key !== b.profile.key) {
    km += a.length / 1000;
    const k = (a.name || '(unnamed)') + ': ' + a.profile.key + ' -> ' + b.profile.key;
    byName.set(k, (byName.get(k) || 0) + a.length);
  }
}
console.log(`edges with another lane count, centre lane or line set: ${rows.length}; with another drawn section: ${km.toFixed(1)} km`);
// --edges <file>: the edges whose drawn section changes, for the audits (CityAudit.RunRoadsSample's PSX_ROADS_DRIVE_EDGES)
if (val('--edges')) writeFileSync(val('--edges'), rows.filter(r => cb.edges[r.i].profile.key !== r.b.profile.key).map(r => r.i).join(','));
for (const [k, m] of [...byName].sort((x, y) => y[1] - x[1]).slice(0, 60)) console.log(`   ${k}  ${(m / 1000).toFixed(2)} km`);
console.log(`edges with a parking lane: ${parkEdges} (${(parkM / 1000).toFixed(1)} km of curb side): ` + [...parkNames].sort((x, y) => y[1] - x[1]).slice(0, 24).map(([k, m]) => `${k} ${(m / 1000).toFixed(2)}`).join(', '));
if (val('--park-edges')) writeFileSync(val('--park-edges'), rows.filter(r => r.park & 3).map(r => r.i).join(','));
console.log(`edges with a bike lane or paved shoulder: ${stripEdges} (${(stripM / 1000).toFixed(1)} km of road side): ` + [...stripRoads].sort((x, y) => y[1] - x[1]).slice(0, 24).map(([k, m]) => `${k} ${(m / 1000).toFixed(2)}`).join(', '));
if (val('--strip-edges')) writeFileSync(val('--strip-edges'), rows.filter(r => r.park & 60).map(r => r.i).join(','));

const tapr = bytesOf(ovr, os, 'TAPR');
if (!tapr) throw new Error('the override export has no TAPR section');
const head = Buffer.alloc(8 + rows.length * 16);
head.writeUInt32LE(1, 0); head.writeUInt32LE(rows.length, 4);
rows.forEach((r, k) => {
  const o = 8 + k * 16, ls = r.lb;
  head.writeUInt32LE(r.i, o); head.writeUInt8(r.b.lanes, o + 4); head.writeUInt8(r.b.turn ? 1 : 0, o + 5);
  head.writeUInt8(ls.nF || 0, o + 6); head.writeUInt8(ls.nB || 0, o + 7); head.writeUInt8(ls.centre || 0, o + 8); head.writeUInt8(ls.flags || 0, o + 9);
  head.writeUInt16LE(ls.turnOnly || 0, o + 10); head.writeUInt8(ls.sub || 0, o + 12); head.writeUInt8(ls.bike || 0, o + 13); head.writeUInt16LE(r.park || 0, o + 14);
});
const splt = bytesOf(ovr, os, 'SPLT');
if (!splt) throw new Error('the override export has no SPLT section');
if (splt.readUInt32LE(0) !== bytesOf(base, bs, 'SPLT').readUInt32LE(0)) throw new Error('the override export has another number of splits');
const lan2 = Buffer.concat([head, tapr, splt]);
console.log(`LAN2: ${rows.length} edge rows + the taper list (${tapr.length} bytes; shipped TAPR ${bytesOf(base, bs, 'TAPR').length}) + the split table (${splt.length} bytes, ${splt.readUInt32LE(0)} splits) = ${lan2.length} bytes`);

function rebuild(buf, secs, extra) {
  const all = secs.map(s => ({ tag: s.tag, data: buf.subarray(s.off, s.off + s.len) }));
  if (extra) all.push(extra);
  const headLen = 12 + 12 * all.length;
  let at = headLen; const parts = [];
  const tab = Buffer.alloc(headLen);
  buf.copy(tab, 0, 0, 8); tab.writeUInt32LE(all.length, 8);
  all.forEach((s, k) => {
    const pad = (4 - (at % 4)) % 4; if (pad) { parts.push(Buffer.alloc(pad)); at += pad; }
    tab.write(s.tag, 12 + 12 * k, 4, 'latin1'); tab.writeUInt32LE(at, 16 + 12 * k); tab.writeUInt32LE(s.data.length, 20 + 12 * k);
    parts.push(s.data); at += s.data.length;
  });
  return Buffer.concat([tab, ...parts]);
}
const outBuf = rebuild(base, bs, { tag: 'LAN2', data: lan2 });
// the result must read back as the same graph with the same sections
const back = parseCity(outBuf);
if (hashHex(graphHash(back.edges)) !== hb) throw new Error('the spliced file hashes differently');
const ns = table(outBuf);
for (const s of bs) if (!bytesOf(outBuf, ns, s.tag).equals(bytesOf(base, bs, s.tag))) throw new Error(`section ${s.tag} changed in the splice`);
console.log(`spliced file: ${outBuf.length} bytes (was ${base.length}); every shipped section byte for byte; graph hash ${hb}`);
if (!dry) { writeFileSync(outPath, outBuf); console.log('wrote ' + outPath); } else console.log('(dry run: nothing written)');
