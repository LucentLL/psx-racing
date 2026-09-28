// fetch_layers.mjs - the OpenStreetMap layers later Charlotte packages consume
// and no fetch made until WP-02 (plan critic C19): pedestrian crossings, turn
// restrictions, traffic calming, railways and level crossings, power poles and
// lines, trees, advertising, barriers and culverts.
//
//   node tools/city/fetch/fetch_layers.mjs [--only name,name] [--force]
//
// EVERY QUERY IS PINNED to the road snapshot's moment,
//   [date:"2026-09-12T02:44:33Z"]  (ways_all.json's timestamp_osm_base)
// so node ids, way splits and tags match the graph the game ships: a crossing
// or a signal is resolved against the SAME raw way the roads were cut from
// (critic C3). Overpass answers a [date:] query from its attic database. The
// answer's timestamp_osm_base is the server's CURRENT base, not the pinned
// date (checked 2026-09-28), so the manifest records both.
//
// Output: tools/city/cache/layers/<name>.json (gitignored, like the rest of the
// Overpass cache; ~tens of MB) and tools/city/fetch/layers_manifest.json
// (committed): per layer the query, the snapshot, element counts, bytes and
// sha256, so a later export can say exactly what it read.
//
// The User-Agent names the repository, not a person (the older fetch scripts'
// e-mail address is owner question Q12). Sequential, one mirror at a time,
// with a pause between queries: Overpass is a shared service.
// All data (c) OpenStreetMap contributors, ODbL 1.0.

import { writeFileSync, readFileSync, existsSync, mkdirSync, statSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, '..', 'cache', 'layers');
const MANIFEST = join(HERE, 'layers_manifest.json');
const UA = 'psx-racing-city-bake/1.0 (game map data; https://github.com/LucentLL/psx-racing)';
// Each server is tried up to three times (the main one answers 504 under
// load). An answer from a server whose database is OLDER than the pinned date
// is refused: a [date:] past the end of a stale mirror's data silently returns
// that mirror's last state (overpass.kumi.systems answered from 2026-05-06
// and 2026-06-01 bases on 2026-09-28).
const MIRRORS = ['https://overpass-api.de/api/interpreter', 'https://overpass-api.de/api/interpreter',
                 'https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter'];
const DATE = '2026-09-12T02:44:33Z';
const BBOX = '35.03,-81.03,35.42,-80.62';          // the beltway bbox of ways_all.json
// timeout 300 and the default maxsize: the public server answered 504 to a
// 900 s / 1 GiB request and 200 to this in seconds.
const HEAD = `[out:json][timeout:300][date:"${DATE}"]`;

/// name -> [query body with {B} for the bbox, what it is for, tiles]. `out
/// geom` for ways (their shape is the point). A layer too big for one answer
/// (the server ran out of its 2 GiB on the trees, and of memory again on 4 x 4) is fetched as tiles x tiles
/// sub-boxes and merged, each element once (by type and id).
const LAYERS = {
  crossings: [`node["highway"="crossing"]({B});out;`,
    'pedestrian crossings (WP-27 stop bars and crosswalks)', 1],
  restrictions: [`relation["type"="restriction"]({B});out;`,
    'turn restrictions (WP-26 lane graph)', 1],
  calming: [`(node["traffic_calming"]({B});way["traffic_calming"]({B}););out geom;`,
    'speed humps and tables (critic C39)', 1],
  railways: [`(way["railway"]({B});node["railway"~"^(level_crossing|crossing)$"]({B}););out geom;`,
    'rail lines, bridges and level crossings (WP-25b)', 1],
  power: [`(node["power"~"^(pole|tower)$"]({B});way["power"~"^(line|minor_line)$"]({B}););out geom;`,
    'utility poles and lines (WP-15)', 1],
  trees: [`(node["natural"="tree"]({B});way["natural"="tree_row"]({B}););out geom;`,
    'street trees and tree rows (WP-22)', 8],
  advertising: [`(node["advertising"]({B});way["advertising"]({B}););out geom;`,
    'billboards (WP-23)', 1],
  barriers: [`(node["barrier"]({B});way["barrier"]({B}););out geom;`,
    'fences, walls, guard rails, gates (WP-24)', 4],
  culverts: [`way["tunnel"="culvert"]({B});out geom;`,
    'culverts (WP-25)', 1],
};

const ARGS = process.argv.slice(2);
const only = ARGS.includes('--only') ? ARGS[ARGS.indexOf('--only') + 1].split(',') : null;
const force = ARGS.includes('--force');
const sha256 = b => createHash('sha256').update(b).digest('hex');
const sleep = ms => new Promise(r => setTimeout(r, ms));

/// The bbox cut into n x n boxes (south,west,north,east).
function tilesOf(n) {
  const [s, w, nn, e] = BBOX.split(',').map(Number);
  const out = [];
  for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
    const a = s + (nn - s) * i / n, b = s + (nn - s) * (i + 1) / n;
    const c = w + (e - w) * j / n, d = w + (e - w) * (j + 1) / n;
    out.push([a, c, b, d].map(v => v.toFixed(5)).join(','));
  }
  return out;
}

/// One query, tried on each server in turn; the parsed answer or null (and
/// why, in lastError).
let lastError = '';
async function ask(query, label) {
  for (const m of MIRRORS) {
    try {
      const t0 = Date.now();
      console.log(`[fetch] ${label} from ${new URL(m).host}`);
      const res = await fetch(m, { method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'User-Agent': UA },
        body: 'data=' + encodeURIComponent(query) });
      if (!res.ok) { console.warn(`  HTTP ${res.status}`); lastError = `HTTP ${res.status}`; await sleep(30000); continue; }
      const text = Buffer.from(await res.arrayBuffer()).toString('utf8');
      const p = JSON.parse(text);
      if (p.remark && /error|timed out|runtime/i.test(p.remark)) { console.warn('  remark: ' + p.remark); lastError = p.remark; await sleep(30000); continue; }
      const snap = p.osm3s && p.osm3s.timestamp_osm_base;
      if (!snap || snap < DATE) { console.warn(`  refused: the server's data ends ${snap}, before the pinned ${DATE}`); lastError = `stale server (${snap})`; continue; }
      console.log(`  ok ${(p.elements || []).length} elements  ${(text.length / 1e6).toFixed(1)} MB  ${((Date.now() - t0) / 1000).toFixed(0)} s`);
      return p;
    } catch (e) { console.warn('  failed: ' + e.message); lastError = e.message; await sleep(30000); }
  }
  return null;
}

mkdirSync(OUT, { recursive: true });
const manifest = existsSync(MANIFEST) ? JSON.parse(readFileSync(MANIFEST, 'utf8'))
  : { schema: 1, note: '', date: DATE, bbox: BBOX, layers: {} };
manifest.note = 'OpenStreetMap layers fetched by tools/city/fetch/fetch_layers.mjs, pinned to the road snapshot (ways_all.json). The bodies are in tools/city/cache/layers/ (gitignored). (c) OpenStreetMap contributors, ODbL 1.0.';
manifest.date = DATE; manifest.bbox = BBOX;

let failed = 0;
for (const [name, [body, why, tiles]] of Object.entries(LAYERS)) {
  if (only && !only.includes(name)) continue;
  const file = join(OUT, name + '.json');
  if (!force && existsSync(file) && statSync(file).size > 200 && manifest.layers[name]) { console.log(`[cache] ${name}`); continue; }
  const boxes = tiles > 1 ? tilesOf(tiles) : [BBOX];
  const seen = new Set(), elements = [];
  let head = null, ok = true;
  for (let k = 0; k < boxes.length; k++) {
    const p = await ask(`${HEAD};${body.replaceAll('{B}', boxes[k])}`, boxes.length > 1 ? `${name} tile ${k + 1}/${boxes.length}` : name);
    if (!p) { ok = false; break; }
    if (!head) head = { version: p.version, generator: p.generator, osm3s: p.osm3s };
    for (const e of p.elements || []) {
      const key = e.type + '/' + e.id;
      if (seen.has(key)) continue;
      seen.add(key); elements.push(e);
    }
    if (k + 1 < boxes.length) await sleep(10000);   // the server answers 429 to a tighter loop
  }
  if (!ok) {
    console.warn(`FAILED ${name}`); failed++;
    // A layer that has never been fetched says so, and why, in the manifest.
    if (!manifest.layers[name] || !manifest.layers[name].sha256)
      manifest.layers[name] = { for: why, pinned: DATE, not_fetched: `${new Date().toISOString().slice(0, 10)}: ${lastError}`,
                                retry: `node tools/city/fetch/fetch_layers.mjs --only ${name}` };
    writeFileSync(MANIFEST, JSON.stringify(manifest, null, 2) + '\n');
    await sleep(5000); continue;
  }
  // Stable order whatever the tiling: by type, then id.
  const rank = { node: 0, way: 1, relation: 2 };
  elements.sort((a, b) => (rank[a.type] - rank[b.type]) || (a.id - b.id));
  const buf = Buffer.from(JSON.stringify({ ...head, pinned_date: DATE, bbox: BBOX, elements }));
  const counts = {};
  for (const e of elements) counts[e.type] = (counts[e.type] || 0) + 1;
  writeFileSync(file, buf);
  manifest.layers[name] = { for: why, query: `${HEAD};${body.replaceAll('{B}', tiles > 1 ? '<tile>' : BBOX)}`, tiles: boxes.length,
                            pinned: DATE, server_base: head.osm3s && head.osm3s.timestamp_osm_base || null, elements: counts,
                            bytes: buf.length, sha256: sha256(buf), fetched: new Date().toISOString().slice(0, 10) };
  console.log(`  ${name}: ${JSON.stringify(counts)}  ${(buf.length / 1e6).toFixed(1)} MB  (as of ${DATE})`);
  writeFileSync(MANIFEST, JSON.stringify(manifest, null, 2) + '\n');
  await sleep(5000);
}
console.log(failed ? `LAYERS: ${failed} failed (re-run to retry just those)` : 'LAYERS OK');
process.exitCode = failed ? 1 : 0;
