// fetch_canopy.mjs - present-day tree canopy under Charlotte (plan WP-08)
//
//   node tools/city/fetch/fetch_canopy.mjs [--force] [--year 2024]
//
// The source is the USDA Forest Service's NLCD Tree Canopy Cover (TCC),
// v2025.6: percent tree canopy per 30 m pixel, one layer a year 1985-2025,
// public domain (a US Government work; credit "USDA Forest Service"). The
// owner chose PRESENT DAY for everything but the cars (2026-09-28, Q3), so
// this takes the latest full year, 2024 (2025's layer is the year the
// release came out in). It is served by the USFS image service on the
// Interagency Imagery Portal (the old apps.fs.usda.gov endpoint now answers
// "migrated to IIPP"), which clips and returns just the box asked for:
//
//   exportImage on USFS_EDW_NLCD_TCC_CONUS, the one catalog raster of that
//   year locked (mosaicRule lockRaster), in the raster's own projection
//   (ESRI:102008, North America Albers Equal Area, 30 m), the box snapped to
//   its pixel lattice, nearest neighbour, uncompressed U8 GeoTIFF. Nothing
//   is resampled on the server.
//
// Output (gitignored; about 3.6 MB, cached once): tools/city/cache/canopy/
// tcc<year>.u8, the pixels rows north to south, and tcc<year>.json with the
// lattice (xmin, ymax, pixel size, cols, rows), the request, the catalog
// item's name, the fetch time and the sha256. tools/city/canopy.mjs builds
// the game's 60 m canopy grid from it. Values: 0-100 percent; 254 and 255
// are the product's non-processing / background codes (read as 0).

import { writeFileSync, existsSync, mkdirSync, readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { albers102008 } from '../lib/canopygrid.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = process.env.PSX_GIS_DIR ? join(process.env.PSX_GIS_DIR, 'canopy') : join(HERE, '..', 'cache', 'canopy');
const UA = 'psx-racing-city-bake/1.0 (game map data; https://github.com/LucentLL/psx-racing)';
const SERVICE = 'https://imagery.geoplatform.gov/iipp/rest/services/Vegetation/USFS_EDW_NLCD_TCC_CONUS/ImageServer';
/// The DEM box, padded (as fetch_water.mjs): [west, south, east, north].
const BOX = [-81.12, 34.975, -80.555, 35.495];
const force = process.argv.includes('--force');
const yi = process.argv.indexOf('--year');
const YEAR = yi > 0 ? +process.argv[yi + 1] : 2024;

async function getJson(url) {
  const r = await fetch(url, { headers: { 'User-Agent': UA } });
  if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
  const j = await r.json();
  if (j.error) throw new Error(`${url}: ${JSON.stringify(j.error)}`);
  return j;
}

mkdirSync(OUT, { recursive: true });
const u8Path = join(OUT, `tcc${YEAR}.u8`), jsonPath = join(OUT, `tcc${YEAR}.json`);
if (existsSync(u8Path) && existsSync(jsonPath) && !force) {
  console.log(`${u8Path} is cached (--force to fetch again)`);
  process.exit(0);
}

// ---- the catalog raster of that year, and its pixel lattice
const q = await getJson(`${SERVICE}/query?where=${encodeURIComponent(`beginyear=${YEAR}`)}&outFields=objectid,name,beginyear,endyear&returnGeometry=false&f=json`);
if (q.features?.length !== 1) throw new Error(`catalog: ${q.features?.length} rasters for ${YEAR}`);
const item = q.features[0].attributes;
const info = await getJson(`${SERVICE}/${item.objectid}/info?f=json`);
if (info.extent?.spatialReference?.wkid !== 102008) throw new Error(`raster ${item.objectid} is not in ESRI:102008: ${JSON.stringify(info.extent?.spatialReference)}`);
const PX = info.pixelSizeX, ox = info.origin.x, oy = info.origin.y;
if (PX !== 30 || info.pixelSizeY !== 30) throw new Error(`pixel ${PX} x ${info.pixelSizeY}, expected 30 m`);

// ---- the box in the raster's projection, snapped outward to its lattice
let xmin = Infinity, xmax = -Infinity, ymin = Infinity, ymax = -Infinity;
for (let i = 0; i <= 20; i++)
  for (let j = 0; j <= 20; j++) {
    const [x, y] = albers102008(BOX[1] + (BOX[3] - BOX[1]) * j / 20, BOX[0] + (BOX[2] - BOX[0]) * i / 20);
    xmin = Math.min(xmin, x); xmax = Math.max(xmax, x); ymin = Math.min(ymin, y); ymax = Math.max(ymax, y);
  }
xmin = ox + Math.floor((xmin - ox) / PX) * PX; xmax = ox + Math.ceil((xmax - ox) / PX) * PX;
ymax = oy - Math.floor((oy - ymax) / PX) * PX; ymin = oy - Math.ceil((oy - ymin) / PX) * PX;
const cols = Math.round((xmax - xmin) / PX), rows = Math.round((ymax - ymin) / PX);

const params = new URLSearchParams({
  bbox: [xmin, ymin, xmax, ymax].join(','), bboxSR: '102008', imageSR: '102008', size: `${cols},${rows}`,
  format: 'tiff', pixelType: 'U8', compression: 'None', interpolation: 'RSP_NearestNeighbor',
  mosaicRule: JSON.stringify({ mosaicMethod: 'esriMosaicLockRaster', lockRasterIds: [item.objectid] }),
  renderingRule: JSON.stringify({ rasterFunction: 'None' }), f: 'json',
});
const request = `${SERVICE}/exportImage?${params}`;
const ex = await getJson(request);
if (ex.width !== cols || ex.height !== rows) throw new Error(`exportImage answered ${ex.width} x ${ex.height}, asked ${cols} x ${rows}`);
const r = await fetch(ex.href, { headers: { 'User-Agent': UA } });
if (!r.ok) throw new Error(`${ex.href}: HTTP ${r.status}`);
const tif = Buffer.from(await r.arrayBuffer());

// ---- the TIFF: classic little-endian, one U8 band, uncompressed (strips or tiles)
if (tif.toString('latin1', 0, 2) !== 'II' || tif.readUInt16LE(2) !== 42) throw new Error('not a classic little-endian TIFF');
const ifd = tif.readUInt32LE(4), n = tif.readUInt16LE(ifd), tags = {};
for (let k = 0; k < n; k++) {
  const p = ifd + 2 + k * 12, tag = tif.readUInt16LE(p), type = tif.readUInt16LE(p + 2), count = tif.readUInt32LE(p + 4);
  const size = { 1: 1, 2: 1, 3: 2, 4: 4, 12: 8 }[type] || 1;
  const at = size * count > 4 ? tif.readUInt32LE(p + 8) : p + 8;
  const vals = [];
  for (let i = 0; i < count && i < 1e6; i++)
    vals.push(type === 3 ? tif.readUInt16LE(at + 2 * i) : type === 4 ? tif.readUInt32LE(at + 4 * i) : type === 12 ? tif.readDoubleLE(at + 8 * i) : tif[at + i]);
  tags[tag] = vals;
}
const one = t => tags[t]?.[0];
if (one(256) !== cols || one(257) !== rows || one(258) !== 8 || one(259) !== 1 || (one(277) || 1) !== 1)
  throw new Error(`TIFF: expected ${cols} x ${rows} uncompressed U8, got ${JSON.stringify({ w: one(256), h: one(257), b: one(258), c: one(259), spp: one(277) })}`);
const px = Buffer.alloc(cols * rows);
if (tags[322]) {
  // tiles, row-major across the image; the right and bottom ones padded
  const tw = one(322), th = one(323), across = Math.ceil(cols / tw);
  tags[324].forEach((off, i) => {
    if (tags[325][i] !== tw * th) throw new Error(`TIFF tile ${i}: ${tags[325][i]} bytes, expected ${tw * th}`);
    const c0 = (i % across) * tw, r0 = Math.floor(i / across) * th;
    for (let y = 0; y < th && r0 + y < rows; y++)
      tif.copy(px, (r0 + y) * cols + c0, off + y * tw, off + y * tw + Math.min(tw, cols - c0));
  });
} else {
  let w = 0;
  tags[273].forEach((off, i) => { tif.copy(px, w, off, off + tags[279][i]); w += tags[279][i]; });
  if (w !== px.length) throw new Error(`TIFF strips hold ${w} bytes, expected ${px.length}`);
}

const hist = new Array(256).fill(0);
for (const v of px) hist[v]++;
const bad = hist.slice(101, 254).reduce((a, b) => a + b, 0);
if (bad) throw new Error(`${bad} pixels between 101 and 253: not a percent layer`);
writeFileSync(u8Path, px);
const meta = {
  schema: 1,
  what: `USDA Forest Service NLCD Tree Canopy Cover, ${item.name} (percent canopy, 30 m), the Charlotte DEM box padded`,
  credit: 'USDA Forest Service, NLCD Tree Canopy Cover', licence: 'public domain (US Government work)',
  year: YEAR, catalog: item, crs: 'ESRI:102008 (North America Albers Equal Area Conic, NAD83)',
  xmin, ymax, pixel: PX, cols, rows, layout: 'u8, rows north to south; pixel (c, r) covers x xmin + [c, c+1] * pixel, y ymax - [r, r+1] * pixel',
  codes: '0-100 percent; 254/255 non-processing or background (read as 0)',
  background: hist[254] + hist[255], mean_pct: +(px.reduce((a, v) => a + (v <= 100 ? v : 0), 0) / px.length).toFixed(2),
  request, fetched: new Date().toISOString(), sha256: createHash('sha256').update(px).digest('hex'),
};
writeFileSync(jsonPath, JSON.stringify(meta, null, 2) + '\n');
console.log(`wrote ${u8Path} (${cols} x ${rows}, ${(px.length / 1e6).toFixed(1)} MB; mean ${meta.mean_pct}% canopy; ${meta.background} background px) from ${item.name}`);
