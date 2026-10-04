// Uptown B2 (2026-10-04): the building:part layer for the same 8 x 8 km core
// box as fetch_bld.mjs, at the SAME snapshot moment (so the parts' outlines
// are the ways buildings_core.json holds). One fetch, cached in
// tools/city/cache/parts_core.json (gitignored; pinned by sha256 in
// ../cache_manifest.json). Run from tools/city/cache:
//   node ../fetch/fetch_parts.mjs count    # how big it is first
//   node ../fetch/fetch_parts.mjs          # the fetch
import { writeFileSync, existsSync, statSync } from 'node:fs';
const UA = 'psx-racing-city-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)';
const MIRRORS = ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter'];
const B = '35.190,-80.880,35.262,-80.790';
// the snapshot of ways_all.json (buildings_core.json's is 02:39:51Z, five
// minutes earlier). Overpass still prints TODAY's timestamp_osm_base in the
// body of a [date:] query, so the manifest's snapshot line for this file is
// the fetch day (2026-10-04), not the data's moment.
const DATE = '[date:"2026-09-12T02:44:33Z"]';
const SEL = `(way["building:part"](${B});relation["building:part"](${B});relation["type"="building"](${B}););`;
async function post(query) {
  for (const m of MIRRORS) {
    try {
      const res = await fetch(m, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'User-Agent': UA }, body: 'data=' + encodeURIComponent(query) });
      if (!res.ok) { console.warn('  HTTP', res.status, new URL(m).host); continue; }
      return await res.text();
    } catch (e) { console.warn('  failed', new URL(m).host, e.message); }
  }
  throw new Error('all mirrors failed');
}
if (process.argv[2] === 'count') {
  console.log(await post(`[out:json][timeout:120]${DATE};${SEL}out count;`));
} else {
  const name = 'parts_core.json';
  if (existsSync(name) && statSync(name).size > 1000) { console.log('[cache]', name); process.exit(0); }
  const t0 = Date.now();
  const text = await post(`[out:json][timeout:300]${DATE};${SEL}out geom;`);
  const p = JSON.parse(text);
  if (!p.elements || !p.elements.length) throw new Error('0 elements ' + (p.remark || ''));
  writeFileSync(name, text);
  console.log('ok', p.elements.length, 'elements', (text.length / 1e6).toFixed(1), 'MB', ((Date.now() - t0) / 1000).toFixed(0), 's');
}
