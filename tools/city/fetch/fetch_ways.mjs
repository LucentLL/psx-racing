// COPY (2026-09-28, WP-01) of the one-off fetch that made tools/city/cache/ (gitignored):
// run from inside tools/city/cache on 2026-09-11; the log beside this file is that run's.
// The snapshot it returned is pinned by sha256 + timestamp_osm_base in ../cache_manifest.json.
// Re-running it fetches TODAY's OSM, not that snapshot: add [date:"2026-09-12T02:44:33Z"]
// to the query settings to ask Overpass for the same moment (plan critic C19).

// one-off: every arterial way (+ ramps) and every signal/stop node in the beltway bbox,
// the same query RG2's tools/osm/fetch.mjs ran on 2026-08-12, refetched so the roads,
// the buildings and the minor streets come from ONE snapshot.
import { writeFileSync, existsSync, statSync } from 'node:fs';
const UA = 'psx-racing-city-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)';
const MIRRORS = ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter'];
const BBOX = '35.03,-81.03,35.42,-80.62';
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
await q('ways_all.json', `[out:json][timeout:600];(way["highway"~"^(motorway|trunk|primary|secondary|tertiary)(_link)?$"](${BBOX}););out geom;`);
await q('nodes_all.json', `[out:json][timeout:300];(node["highway"~"^(traffic_signals|stop|give_way)$"](${BBOX}););out;`);
// Plan B1 (2026-10-02): OSM's bridge OUTLINES - one man_made=bridge area round
// two carriageways is one structure (West 5th Street over I-77 is
// w984482059). Read by export_osm.mjs into section BRST (lib/bridges.mjs).
// The shipped bridges_mm.json is the bridges diagnosis's read-only fetch of
// 2026-10-02 (timestamp_osm_base 2026-10-02T21:13:19Z: TODAY's outlines, not
// the 2026-09-12 road snapshot; 336 ways + 1 multipolygon); a re-fetch should
// pin [date:"2026-09-12T02:44:33Z"] like the layers do, and record it with
// export_osm.mjs --manifest.
await q('bridges_mm.json', `[out:json][timeout:300];(way["man_made"="bridge"](${BBOX});relation["man_made"="bridge"](${BBOX}););out geom;`);
console.log('done');
