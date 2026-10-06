// Coverage pass (2026-10-06): the buildings and parking lots OUTSIDE the
// uptown core box, over the drivable roads' whole extent (ways_all.json's
// lat 35.00-35.47 / lon -81.09..-80.59), so a campus or a suburban shopping
// centre has its buildings and its lot. The core's own fetches
// (fetch_bld.mjs, fetch_lots.mjs, fetch_parts.mjs) are untouched; the
// exporter drops any element those already hold, so the core stays as it was.
//
// Only what the procedural passes cannot make: non-residential buildings
// (by building=*, or any building carrying amenity/shop/office), building=yes
// with a perimeter over 70 m (~300 m2: the exporter applies the real area
// test), building:part ways, amenity=parking lots (surface and multi-storey)
// and their parking aisles. Detached houses stay with CityHouses' fill.
//   node tools/city/fetch/fetch_coverage.mjs   -> cache/cov/*.json (one per tile)
//                                                -> cache/buildings_city.json, lots_city.json
// Today's OSM (no attic date): a tile that fails is retried on the next run.
import { writeFileSync, readFileSync, existsSync, statSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const UA = 'psx-racing-city-bake/1.0 (game map data; https://github.com/LucentLL/psx-racing)';
const MIRRORS = ['https://maps.mail.ru/osm/tools/overpass/api/interpreter', 'https://overpass-api.de/api/interpreter', 'https://overpass.private.coffee/api/interpreter',
  'https://overpass.kumi.systems/api/interpreter'];
const CACHE = join(dirname(fileURLToPath(import.meta.url)), '..', 'cache');
const DIR = join(CACHE, 'cov');
mkdirSync(DIR, { recursive: true });
const S = 35.00, N = 35.47, W = -81.09, E = -80.59, NY = 4, NX = 4;
const USES = 'commercial|retail|supermarket|office|industrial|warehouse|university|college|school|hospital|' +
  'hotel|apartments|dormitory|church|cathedral|chapel|mosque|synagogue|temple|religious|civic|public|government|' +
  'parking|sports_centre|sports_hall|stadium|grandstand|kindergarten|train_station|transportation|fire_station|' +
  'manufacture|service|hangar|data_center|museum|library|kiosk';
const bldQ = B => `[out:json][timeout:900];(way["building"~"^(${USES})$"](${B});way["building"]["amenity"](${B});` +
  `way["building"]["shop"](${B});way["building"]["office"](${B});way["building"="yes"](if:length()>70)(${B});` +
  `relation["building"]["type"="multipolygon"](${B});way["building:part"](${B}););out geom;`;
const lotQ = B => `[out:json][timeout:600];(way["amenity"="parking"](${B});relation["amenity"="parking"]["type"="multipolygon"](${B});` +
  `way["highway"="service"]["service"="parking_aisle"](${B}););out geom;`;
// One query, cached; null when every mirror failed `rounds` times over.
async function q(file, query, rounds) {
  if (existsSync(file) && statSync(file).size > 100) { return JSON.parse(readFileSync(file, 'utf8')).elements; }
  for (let attempt = 0; attempt < rounds; attempt++)
    for (const m of MIRRORS) {
      try {
        const t0 = Date.now();
        const res = await fetch(m, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'User-Agent': UA }, body: 'data=' + encodeURIComponent(query), signal: AbortSignal.timeout(240000) });
        if (!res.ok) { console.warn('  HTTP', res.status, new URL(m).host); await new Promise(r => setTimeout(r, res.status === 429 ? 30000 : 5000)); continue; }
        const text = await res.text();
        const p = JSON.parse(text);
        if (!p.elements) { console.warn('  no elements', p.remark); continue; }
        if (p.remark && /error|timed out|out of memory/i.test(p.remark)) { console.warn('  remark', p.remark); continue; }
        writeFileSync(file, text);
        console.log('  ok', file.split(/[\/]/).pop(), p.elements.length, 'elements', (text.length / 1e6).toFixed(1), 'MB', ((Date.now() - t0) / 1000).toFixed(0), 's');
        return p.elements;
      } catch (e) { console.warn('  failed', e.message); }
    }
  return null;
}
// A tile the mirrors cannot answer whole (a 504 on a dense one) is asked as
// four quarters (cov/<k>_<iy>_<ix>_<q>.json), and a quarter as four again.
async function tile(kind, name, s, w, n, e, depth) {
  const B = `${s.toFixed(4)},${w.toFixed(4)},${n.toFixed(4)},${e.toFixed(4)}`;
  const quartered = existsSync(join(DIR, `${kind}_${name}_0.json`));
  const els = quartered ? null : await q(join(DIR, `${kind}_${name}.json`), (kind === 'b' ? bldQ : lotQ)(B), depth < 2 ? 1 : 3);
  if (els) return els;
  if (depth >= 2) throw new Error('all mirrors failed for ' + kind + '_' + name);
  console.log('  quartering', kind, name);
  const my = (s + n) / 2, mx = (w + e) / 2, out = [];
  const qs = [[s, w, my, mx], [s, mx, my, e], [my, w, n, mx], [my, mx, n, e]];
  for (let k = 0; k < 4; k++) for (const el of await tile(kind, `${name}_${k}`, ...qs[k], depth + 1)) out.push(el);
  return out;
}
const bld = new Map(), lots = new Map();
// COV_FIRST="2_2,3_3" asks those tiles first (a second process can work the
// list from the other end: COV_REV=1); every tile is still asked, cached ones free.
const order = [];
for (let iy = 0; iy < NY; iy++) for (let ix = 0; ix < NX; ix++) order.push([iy, ix]);
if (process.env.COV_REV) order.reverse();
for (const f of (process.env.COV_FIRST || '').split(',').filter(Boolean).reverse()) {
  const k = order.findIndex(([y, x]) => `${y}_${x}` === f); if (k >= 0) order.unshift(...order.splice(k, 1));
}
for (const [iy, ix] of order) {
    const s = S + (N - S) * iy / NY, n = S + (N - S) * (iy + 1) / NY;
    const w = W + (E - W) * ix / NX, e = W + (E - W) * (ix + 1) / NX;
    console.log('[tile]', iy, ix);
    for (const el of await tile('b', `${iy}_${ix}`, s, w, n, e, 0)) bld.set(el.type + el.id, el);
    for (const el of await tile('l', `${iy}_${ix}`, s, w, n, e, 0)) lots.set(el.type + el.id, el);
  }
const out = (name, m) => { const t = JSON.stringify({ version: 0.6, generator: 'fetch_coverage.mjs', elements: [...m.values()] }); writeFileSync(join(CACHE, name), t); console.log(name, m.size, 'elements', (t.length / 1e6).toFixed(1), 'MB'); };
out('buildings_city.json', bld);
out('lots_city.json', lots);
