// fetch_water.mjs - Charlotte's creeks and water bodies (plan WP-04b, critic C2)
//
//   node tools/city/fetch/fetch_water.mjs [--only name,name] [--force]
//
// Replaces RG2's hand-traced creeks (vendor/rg2/baselineWater.ts, registered
// onto the OSM frame with a 23 m ICP residual) with open, surveyed lines:
//
//   meck_creeks    Mecklenburg County GIS "Creeks and Streams" (3,991 lines,
//                  county-maintained, lidar-aligned). The centrelines INSIDE
//                  the county.
//   meck_lakes     Mecklenburg County GIS "Lakes and Ponds": the Catawba
//                  chain (Mountain Island Lake, Lake Wylie) and the county's
//                  ponds.
//   usgs_flowlines USGS 3D Hydrography Program (3DHP) flowlines, layer 50, in
//                  the DEM box: the centrelines OUTSIDE the county.
//   usgs_waterbodies  3DHP waterbodies, layer 60, in the DEM box.
//   county         US Census TIGERweb: Mecklenburg County's boundary (GEOID
//                  37119), which decides inside and outside.
//
// Licences (tools/city/SOURCES.md has the credits):
//   Mecklenburg County Open Mapping: "Unless otherwise specified, our data is
//   released under a CC0 ('No Rights Reserved') license" (the Open Mapping
//   site's disclaimer, maps.mecklenburgcountync.gov/openmapping, checked
//   2026-09-28; the layers' metadata specifies nothing else). The MIT licence
//   the surveys quoted is the site's SOFTWARE licence, not the data's.
//   USGS 3DHP and Census TIGER: US Government works, public domain.
//
// Everything is requested in WGS84 (outSR=4326) as GeoJSON, paged by object
// id, and written with the features SORTED by id, so a re-fetch of unchanged
// data is byte-identical. Output: tools/city/cache/water/<name>.geojson
// (gitignored) and tools/city/fetch/water_manifest.json (committed): the
// query, the fetch time, the feature count, bytes and sha256 of each file.
// ArcGIS servers are shared: sequential requests with a pause between pages.

import { writeFileSync, readFileSync, existsSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, '..', 'cache', 'water');
const MANIFEST = join(HERE, 'water_manifest.json');
const UA = 'psx-racing-city-bake/1.0 (game map data; https://github.com/LucentLL/psx-racing)';
/// The DEM box (charlotte_dem.bytes covers lat 34.9875..35.4815, lon
/// -81.1052..-80.5710), padded a little: [west, south, east, north].
const BOX = [-81.12, 34.975, -80.555, 35.495];

const MECK = 'https://meckgis.mecklenburgcountync.gov/server/rest/services';
const USGS = 'https://3dhp.nationalmap.gov/arcgis/rest/services/usgs_3dhp_all/FeatureServer';
const TIGER = 'https://tigerweb.geo.census.gov/arcgis/rest/services/TIGERweb/State_County/MapServer/1';

/// name -> {url (a layer), where, fields, idField, box (spatial filter), what}
const LAYERS = {
  meck_creeks: { url: `${MECK}/CreeksAndStreams/FeatureServer/0`, where: '1=1', idField: 'objectid_12',
                 fields: 'objectid_12,class,name,stream,us_station,ds_station,buff_width,status',
                 what: 'county creek centrelines (inside Mecklenburg)', licence: 'CC0 (Mecklenburg County Open Mapping)' },
  meck_lakes: { url: `${MECK}/LakesAndPonds/FeatureServer/0`, where: '1=1', idField: 'objectid', box: true,
                fields: 'objectid,feat_type,sq_feet', what: 'county lakes and ponds (the Catawba chain and ponds)',
                licence: 'CC0 (Mecklenburg County Open Mapping)' },
  usgs_flowlines: { url: `${USGS}/50`, where: '1=1', idField: 'OBJECTID', box: true,
                    fields: 'OBJECTID,id3dhp,gnisid,gnisidlabel,featuretype,featuretypelabel,lengthkm,flowdirection,streamorder,levelpath,hydrosequence,dnhydrosequence,featuredate',
                    what: '3DHP flowlines in the DEM box (outside the county)', licence: 'public domain (USGS)' },
  usgs_waterbodies: { url: `${USGS}/60`, where: '1=1', idField: 'OBJECTID', box: true,
                      fields: '*', what: '3DHP waterbodies in the DEM box', licence: 'public domain (USGS)' },
  county: { url: TIGER, where: "GEOID='37119'", idField: 'OBJECTID', fields: 'OBJECTID,GEOID,NAME',
            what: 'Mecklenburg County boundary (inside/outside)', licence: 'public domain (US Census TIGER)' },
};

const ARGS = process.argv.slice(2);
const only = (() => { const i = ARGS.indexOf('--only'); return i >= 0 ? ARGS[i + 1].split(',') : null; })();
const force = ARGS.includes('--force');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const sha256 = b => createHash('sha256').update(b).digest('hex');

async function getJson(url) {
  let last;
  for (let attempt = 0; attempt < 5; attempt++) {
    try {
      const r = await fetch(url, { headers: { 'User-Agent': UA } });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      const j = await r.json();
      if (j.error) throw new Error(`service error ${JSON.stringify(j.error).slice(0, 200)}`);
      return j;
    } catch (e) { last = e; console.log(`  retry ${attempt + 1}: ${e.message}`); await sleep(3000 * (attempt + 1)); }
  }
  throw last;
}

async function fetchLayer(name, L) {
  const pageSize = 1000;
  const q = new URLSearchParams({
    where: L.where, outFields: L.fields, returnGeometry: 'true', outSR: '4326', f: 'geojson',
    orderByFields: L.idField, resultRecordCount: String(pageSize),
  });
  if (L.box) {
    q.set('geometry', BOX.join(',')); q.set('geometryType', 'esriGeometryEnvelope');
    q.set('inSR', '4326'); q.set('spatialRel', 'esriSpatialRelIntersects');
  }
  const feats = new Map();
  for (let offset = 0; ; offset += pageSize) {
    q.set('resultOffset', String(offset));
    const url = `${L.url}/query?${q}`;
    const j = await getJson(url);
    const fs = j.features || [];
    for (const f of fs) {
      const id = f.properties?.[L.idField] ?? f.id;
      if (id === undefined) throw new Error(`${name}: a feature without ${L.idField}`);
      feats.set(id, f);
    }
    console.log(`  ${name}: offset ${offset} -> ${fs.length} (total ${feats.size})`);
    if (fs.length < pageSize) break;
    await sleep(800);
  }
  const sorted = [...feats.entries()].sort((a, b) => a[0] - b[0]).map(([, f]) => ({
    type: 'Feature', id: f.properties?.[L.idField] ?? f.id, properties: f.properties, geometry: f.geometry,
  }));
  const query = `${L.url}/query?` + new URLSearchParams(Object.fromEntries([...q].filter(([k]) => k !== 'resultOffset'))).toString();
  return { type: 'FeatureCollection', name, source: L.url, query, licence: L.licence, what: L.what, features: sorted };
}

mkdirSync(OUT, { recursive: true });
const manifest = existsSync(MANIFEST) ? JSON.parse(readFileSync(MANIFEST, 'utf8')) : { schema: 1, layers: {} };
manifest.note = 'Creek centrelines and water bodies for the Charlotte export (WP-04b), fetched by tools/city/fetch/fetch_water.mjs into tools/city/cache/water/ (gitignored). Features are sorted by object id; coordinates WGS84.';
manifest.box = BOX;
for (const [name, L] of Object.entries(LAYERS)) {
  if (only && !only.includes(name)) continue;
  const path = join(OUT, `${name}.geojson`);
  if (existsSync(path) && !force) { console.log(`${name}: cached (use --force to re-fetch)`); continue; }
  console.log(`${name}: ${L.what}`);
  const fc = await fetchLayer(name, L);
  const body = Buffer.from(JSON.stringify(fc));
  writeFileSync(path, body);
  manifest.layers[name] = { file: `tools/city/cache/water/${name}.geojson`, what: L.what, licence: L.licence,
                            query: fc.query, fetched: new Date().toISOString(), features: fc.features.length,
                            bytes: body.length, sha256: sha256(body) };
  console.log(`  wrote ${path} (${fc.features.length} features, ${(body.length / 1e6).toFixed(1)} MB)`);
  writeFileSync(MANIFEST, JSON.stringify(manifest, null, 2) + '\n');
  await sleep(1000);
}
writeFileSync(MANIFEST, JSON.stringify(manifest, null, 2) + '\n');
console.log(`wrote ${MANIFEST}`);
