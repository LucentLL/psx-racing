// Roads pass L8 (2026-10-03): the ONE Overpass fetch for the parking lots and the
// turning circles, in the core box the minor streets come from (fetch_bld.mjs's B),
// fetched 2026-10-03 at today's OSM: the attic query at the cache's moment
// (LOTS_DATE=1 adds [date:...], plan critic C19) runs Overpass out of memory.
//   node tools/city/fetch/fetch_lots.mjs count   -> 'out count' only (run first)
//   node tools/city/fetch/fetch_lots.mjs          -> tools/city/cache/lots_core.json
// amenity=parking ways and multipolygons (the lots), service=parking_aisle ways
// (the aisles: leftover item 4 paves them and lays the stall rows along them) and
// highway=turning_circle / turning_loop nodes (the cul-de-sac bulbs).
import { writeFileSync, existsSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const UA = 'psx-racing-city-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)';
const MIRRORS = ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter'];
const B = '35.190,-80.880,35.262,-80.790';
const DATE = '2026-09-12T02:44:33Z';
const OUT = join(dirname(fileURLToPath(import.meta.url)), '..', 'cache', 'lots_core.json');
const body = `(way["amenity"="parking"](${B});relation["amenity"="parking"]["type"="multipolygon"](${B});` +
             `way["highway"="service"]["service"="parking_aisle"](${B});node["highway"~"^(turning_circle|turning_loop)$"](${B}););`;
const count = process.argv[2] === 'count';
const query = `[out:json][timeout:300]${process.env.LOTS_DATE ? `[date:"${DATE}"]` : ""};${body}out ${count ? 'count' : 'geom'};`;
async function main() {
if (!count && existsSync(OUT) && statSync(OUT).size > 1000) { console.log('[cache]', OUT); return; }
for (const m of MIRRORS) {
  try {
    const t0 = Date.now();
    console.log('[fetch]', count ? 'count' : OUT, 'from', new URL(m).host);
    const res = await fetch(m, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'User-Agent': UA }, body: 'data=' + encodeURIComponent(query) });
    if (!res.ok) { console.warn('  HTTP', res.status); continue; }
    const text = await res.text();
    const p = JSON.parse(text);
    if (count) { console.log(JSON.stringify(p.elements, null, 0), p.remark || ""); return; }
    if (!p.elements || !p.elements.length) { console.warn('  0 elements', p.remark); continue; }
    writeFileSync(OUT, text);
    console.log('  ok', p.elements.length, 'elements', (text.length / 1e6).toFixed(1), 'MB', ((Date.now() - t0) / 1000).toFixed(0), 's');
    return;
  } catch (e) { console.warn('  failed', e.message); }
}
throw new Error('all mirrors failed');
}
await main();
