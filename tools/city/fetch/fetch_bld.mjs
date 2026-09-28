// COPY (2026-09-28, WP-01) of the one-off fetch that made tools/city/cache/ (gitignored):
// run from inside tools/city/cache on 2026-09-11; the log beside this file is that run's.
// The snapshot it returned is pinned by sha256 + timestamp_osm_base in ../cache_manifest.json.
// Re-running it fetches TODAY's OSM, not that snapshot: add [date:"2026-09-12T02:44:33Z"]
// to the query settings to ask Overpass for the same moment (plan critic C19).

// one-off: buildings + minor streets around uptown, cached here
import { writeFileSync, existsSync, statSync } from 'node:fs';
const UA = 'psx-racing-city-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)';
const MIRRORS = ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter'];
async function q(name, query) {
  if (existsSync(name) && statSync(name).size > 1000) { console.log('[cache]', name); return; }
  for (const m of MIRRORS) {
    try {
      const t0 = Date.now();
      console.log('[fetch]', name, 'from', new URL(m).host);
      const res = await fetch(m, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'User-Agent': UA }, body: 'data=' + encodeURIComponent(query) });
      if (!res.ok) { console.warn('  HTTP', res.status); continue; }
      const text = await res.text();
      const p = JSON.parse(text);
      if (!p.elements || !p.elements.length) { console.warn('  0 elements', p.remark); continue; }
      writeFileSync(name, text);
      console.log('  ok', p.elements.length, 'elements', (text.length / 1e6).toFixed(1), 'MB', ((Date.now() - t0) / 1000).toFixed(0), 's');
      return;
    } catch (e) { console.warn('  failed', e.message); }
  }
  throw new Error('all mirrors failed for ' + name);
}
const B = process.argv[2] || '35.190,-80.880,35.262,-80.790';
await q('buildings_core.json', `[out:json][timeout:600];(way["building"](${B});relation["building"]["type"="multipolygon"](${B}););out geom;`);
await q('streets_core.json', `[out:json][timeout:300];(way["highway"~"^(residential|unclassified|living_street|service)$"]["service"!~"^(parking_aisle|driveway|alley)$"](${B}););out geom;`);
console.log('done');
