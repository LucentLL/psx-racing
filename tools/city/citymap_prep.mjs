// citymap_prep.mjs - gather the layers of the whole-city map (the track preview's ground) into one
// JSON for tools/city/citymap_tiles.py, which draws the tile pyramid under
// Assets/StreamingAssets/CityMap.
//
// The owner, 2026-11-16: "not a fan of each track getting its own saved images. It should be a
// picture of the entire city, the track drawn over it, then zoomed in to fit the track on screen
// for track preview before race". So one map of the whole city, drawn once from the city's own
// data, in the game's frame (x east, z north, metres; export_osm.mjs's equirectangular):
//   roads    the shipped graph (Resources/charlotte_city.bytes), class, flags, painted width
//   water    the same file's WATR (creeks with their width, lakes as rings; ravines left out)
//   land     OSM commercial / retail / industrial landuse (cache/layers/landuse.json)
//   rail     OSM rail, light rail and tram lines (cache/layers/railways.json), no abandoned or disused
//   blds     OSM building footprints, the whole city (cache/buildings_city.json) and the core
//            (cache/buildings_core.json)
//   aisles   OSM parking aisles and service roads in lots (cache/lots_city.json)
//   venues   the squares the race venues are framed in (RouteGeo.Frame's rule: the route's box,
//            1.14x its longer side + 60 m, never under 420 m) for every route in
//            Resources/charlotte_routes.json, the parking decks' squares from
//            Resources/TrackMapFrames.txt, and --venues <json> extra squares ([{key, cx, cz, span}]):
//            the deep levels of the pyramid are drawn only round these, so a new route gets its sharp
//            tiles from the next run without a list to remember
// The tree canopy is read by the tile script straight from Resources/charlotte_canopy.bytes.
// Every source is already in tools/city/SOURCES.md (OSM, 3DEP, USFS canopy); nothing is fetched.
//
// Run: node tools/city/citymap_prep.mjs --out <file.json> [--project <unity project>] [--venues <extra.json>]
//      --project reads that project's Resources (default: this checkout); PSX_CITY_CACHE points a
//      worktree or a sandbox at the main checkout's tools/city/cache. tools/citymap-bake.ps1 runs both steps.
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity } from './lib/citydata.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const arg = n => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : null; };
const UNITY = arg('--project') ? resolve(arg('--project')) : join(HERE, '..', '..');
const CACHE = process.env.PSX_CITY_CACHE ? resolve(process.env.PSX_CITY_CACHE) : join(HERE, 'cache');
const RES = join(UNITY, 'Assets', 'PSXRacing', 'Resources');
const OUT = arg('--out');
if (!OUT) throw new Error('--out <file.json> is required');

const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;
const M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const X = lon => (lon - LON0) * M_LON, Z = lat => (lat - LAT0) * M_LAT;
const r1 = v => Math.round(v * 2) / 2;   // half a metre is a fifth of a pixel at the deepest level
const ring = geom => geom.flatMap(g => [r1(X(g.lon)), r1(Z(g.lat))]);
const readJson = p => JSON.parse(readFileSync(p, 'utf8'));

const city = parseCity(readFileSync(join(RES, 'charlotte_city.bytes')));
const roads = city.edges.map(e => [e.rank, (e.link ? 1 : 0) | (e.oneway ? 2 : 0) | (e.bridge ? 4 : 0) | (e.tunnel ? 8 : 0),
  +e.profile.width.toFixed(1), e.pts.flatMap(p => [r1(p[0]), r1(p[1])])]);
const water = city.waters.filter(w => w.kind !== 2).map(w => [w.kind, +w.width.toFixed(1), w.pts.flatMap(p => [r1(p[0]), r1(p[1])])]);

// landuse: ways, and relations' outer rings
const LU = { commercial: 0, retail: 1, industrial: 2 };
const land = [];
for (const e of readJson(join(CACHE, 'layers', 'landuse.json')).elements) {
  const k = LU[e.tags && e.tags.landuse];
  if (k == null) continue;
  if (e.type === 'way' && e.geometry && e.geometry.length >= 3) land.push([k, ring(e.geometry)]);
  else if (e.type === 'relation' && e.members) for (const m of e.members) if (m.role === 'outer' && m.geometry && m.geometry.length >= 3) land.push([k, ring(m.geometry)]);
}

// rail: 0 main line, 1 spur / siding / yard, 2 light rail, 3 tram (streetcar)
const rail = [];
for (const e of readJson(join(CACHE, 'layers', 'railways.json')).elements) {
  if (e.type !== 'way' || !e.geometry || e.geometry.length < 2) continue;
  const t = e.tags || {};
  let k = null;
  if (t.railway === 'rail') k = t.service ? 1 : 0;
  else if (t.railway === 'light_rail') k = t.service ? 1 : 2;
  else if (t.railway === 'tram') k = 3;
  if (k == null) continue;
  rail.push([k, t.tunnel === 'yes' ? 1 : 0, ring(e.geometry)]);
}

// buildings: the whole city's and the core's (the core file is denser where they overlap; the same
// footprint drawn twice is the same pixels)
const blds = [];
const seen = new Set();
for (const f of ['buildings_city.json', 'buildings_core.json']) {
  const p = join(CACHE, f);
  if (!existsSync(p)) { console.log('  no ' + f + ': skipped'); continue; }
  for (const e of readJson(p).elements) {
    if (e.type === 'way' && e.geometry && e.geometry.length >= 3) {
      if (seen.has(e.id)) continue;
      seen.add(e.id);
      blds.push(ring(e.geometry));
    } else if (e.type === 'relation' && e.members) {
      if (seen.has('r' + e.id)) continue;
      seen.add('r' + e.id);
      for (const m of e.members) if (m.role === 'outer' && m.geometry && m.geometry.length >= 3) blds.push(ring(m.geometry));
    }
  }
}

// parking aisles and drive-throughs in lots (thin lines at the deep levels)
const aisles = [];
const lp = join(CACHE, 'lots_city.json');
if (existsSync(lp)) for (const e of readJson(lp).elements) {
  if (e.type !== 'way' || !e.geometry || e.geometry.length < 2) continue;
  const t = e.tags || {};
  if (t.amenity === 'parking' && e.geometry.length >= 3) aisles.push([1, ring(e.geometry)]);       // a lot's outline
  else if (t.highway === 'service') aisles.push([0, ring(e.geometry)]);                             // an aisle
}

// the venues' squares: the photographed frames of the city venues + any extra (new courses)
const venues = [];
// every city route, framed the way RouteGeo.Frame frames its card
const routesFile = join(RES, 'charlotte_routes.json');
if (existsSync(routesFile)) for (const r of readJson(routesFile).routes || []) {
  const p = r.pts || [];
  if (p.length < 4) continue;
  let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity;
  for (let i = 0; i + 1 < p.length; i += 2) { x0 = Math.min(x0, p[i]); x1 = Math.max(x1, p[i]); z0 = Math.min(z0, p[i + 1]); z1 = Math.max(z1, p[i + 1]); }
  venues.push({ key: r.id, cx: (x0 + x1) / 2, cz: (z0 + z1) / 2, span: Math.max(Math.max(x1 - x0, z1 - z0) * 1.14 + 60, 420) });
}
// the parking decks (their runs are not routes: their squares are in the photo frames list)
const frames = join(RES, 'TrackMapFrames.txt');
if (existsSync(frames)) for (const line of readFileSync(frames, 'utf8').split(/\r?\n/)) {
  const f = line.trim().split(/\s+/);
  if (f.length < 4 || !f[0].startsWith('deck_')) continue;
  venues.push({ key: f[0], cx: +f[1], cz: +f[2], span: +f[3] });
}
const extra = arg('--venues');
if (extra) for (const v of readJson(extra)) venues.push(v);

writeFileSync(OUT, JSON.stringify({ frame: 'export_osm equirectangular, x east z north, metres', roads, water, land, rail, blds, aisles, venues }));
console.log(`citymap_prep: roads ${roads.length}, water ${water.length}, land ${land.length}, rail ${rail.length}, buildings ${blds.length}, lot lines ${aisles.length}, venues ${venues.length} -> ${OUT}`);
