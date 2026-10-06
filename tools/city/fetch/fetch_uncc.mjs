// one-off (2026-10-06): the streets around UNC Charlotte's two parking decks
// by the EPIC building, outside the core streets' box. Overpass answered 504
// on every mirror that day, so this reads the OSM API's own map call (a small
// bbox) and writes the highway ways in Overpass's "out tags geom" shape:
//   node tools/city/fetch/fetch_uncc.mjs   ->  tools/city/cache/uncc_ways.json
// The exporter takes only the ways it names from it (export_osm.mjs UNCC_ACCESS).
import { writeFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const OUT = join(dirname(fileURLToPath(import.meta.url)), '..', 'cache', 'uncc_ways.json');
const UA = 'psx-racing-city-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)';
const BBOX = '-80.7475,35.3055,-80.7375,35.3130';   // left,bottom,right,top
if (existsSync(OUT) && !process.argv.includes('--force')) { console.log('cached:', OUT); process.exit(0); }
const r = await fetch('https://api.openstreetmap.org/api/0.6/map?bbox=' + BBOX, { headers: { 'User-Agent': UA } });
if (!r.ok) throw new Error('OSM API ' + r.status);
const x = await r.text();
const nodes = new Map();
for (const m of x.matchAll(/<node id="(\d+)"[^>]*?lat="([-\d.]+)" lon="([-\d.]+)"/g)) nodes.set(m[1], { lat: +m[2], lon: +m[3] });
const unq = s => s.replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
const elements = [];
for (const m of x.matchAll(/<way id="(\d+)"[^>]*>([\s\S]*?)<\/way>/g)) {
  const nd = [...m[2].matchAll(/<nd ref="(\d+)"/g)].map(a => a[1]);
  const tags = {};
  for (const t of m[2].matchAll(/<tag k="([^"]*)" v="([^"]*)"/g)) tags[unq(t[1])] = unq(t[2]);
  if (!tags.highway || nd.some(id => !nodes.has(id))) continue;
  elements.push({ type: 'way', id: +m[1], nodes: nd.map(Number), geometry: nd.map(id => nodes.get(id)), tags });
}
writeFileSync(OUT, JSON.stringify({ version: 0.6, generator: 'api.openstreetmap.org/api/0.6/map bbox=' + BBOX,
  osm3s: { timestamp_osm_base: new Date().toISOString() }, elements }));
console.log(`wrote ${OUT}: ${elements.length} highway ways`);
