// canopygrid.mjs - the Charlotte canopy grid (plan WP-08): projections, the
// cached USFS raster, and the charlotte_canopy.bytes layout.
//
// PCAN v1 (little-endian): u32 'PCAN' | i32 1 | u32 nx, nz | f32 x0, z0, cell
// | u16 year | u16 steps | nx*nz u8, row z0 first. A cell's value v is
// canopy v / steps: steps 25, so 4-point steps (0.39 MB Brotli against 0.54
// for whole percent; +-2 points is under a tree per 60 m cell). The lattice is the DEM's
// (charlotte_dem.bytes' nx, nz, x0, z0, cell), so a canopy cell and a DEM
// cell are the same 60 m of ground; the value is the MEAN canopy over that
// cell's area (4 x 4 samples of the 30 m raster, nearest pixel).

import { readFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));

/// export_osm.mjs's frame: equirectangular about RG2's fixture centre.
export const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;
export const M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
export const toLon = x => LON0 + x / M_LON;
export const toLat = z => LAT0 + z / M_LAT;

/// ESRI:102008, North America Albers Equal Area Conic on NAD83 (GRS80):
/// standard parallels 20 and 60, origin 40 N 96 W, no false origin. Snyder,
/// "Map Projections: A Working Manual" (1987), eqs. 3-12, 14-3 to 14-6.
/// WGS84 and NAD83 differ by about a metre here, a thirtieth of a pixel.
const A = 6378137, E2 = 0.00669438002290, E = Math.sqrt(E2), D = Math.PI / 180;
const qf = s => (1 - E2) * (s / (1 - E2 * s * s) - (1 / (2 * E)) * Math.log((1 - E * s) / (1 + E * s)));
const mf = p => Math.cos(p) / Math.sqrt(1 - E2 * Math.sin(p) ** 2);
const m1 = mf(20 * D), m2 = mf(60 * D), q1 = qf(Math.sin(20 * D)), q2 = qf(Math.sin(60 * D)), q0 = qf(Math.sin(40 * D));
const N = (m1 * m1 - m2 * m2) / (q2 - q1), C = m1 * m1 + N * q1, RHO0 = A * Math.sqrt(C - N * q0) / N;
export function albers102008(lat, lon) {
  const rho = A * Math.sqrt(C - N * qf(Math.sin(lat * D))) / N, th = N * (lon + 96) * D;
  return [rho * Math.sin(th), RHO0 - rho * Math.cos(th)];
}

export function canopyCacheDir() {
  return process.env.PSX_GIS_DIR ? join(process.env.PSX_GIS_DIR, 'canopy') : join(HERE, '..', 'cache', 'canopy');
}

/// The raster fetch_canopy.mjs cached: { meta, at(x, y) -> percent at an
/// Albers point (nearest pixel; background and outside read 0) }.
export function loadCanopyRaster(year = 2024) {
  const dir = canopyCacheDir();
  const u8 = join(dir, `tcc${year}.u8`), js = join(dir, `tcc${year}.json`);
  if (!existsSync(u8) || !existsSync(js))
    throw new Error(`${u8} is missing: fetch it with node tools/city/fetch/fetch_canopy.mjs (USDA Forest Service TCC, ~3.6 MB)`);
  const meta = JSON.parse(readFileSync(js, 'utf8'));
  const px = readFileSync(u8);
  if (px.length !== meta.cols * meta.rows) throw new Error(`${u8}: ${px.length} bytes, header says ${meta.cols} x ${meta.rows}`);
  const at = (x, y) => {
    const c = Math.floor((x - meta.xmin) / meta.pixel), r = Math.floor((meta.ymax - y) / meta.pixel);
    if (c < 0 || r < 0 || c >= meta.cols || r >= meta.rows) return 0;
    const v = px[r * meta.cols + c];
    return v <= 100 ? v : 0;
  };
  return { meta, px, at, u8Path: u8 };
}

/// The DEM's lattice, off charlotte_dem.bytes' PDEM header.
export function demLattice(demBytes) {
  const b = demBytes;
  if (b.toString('latin1', 0, 4) !== 'PDEM') throw new Error('charlotte_dem.bytes: bad magic');
  return { nx: b.readInt32LE(8), nz: b.readInt32LE(12), x0: b.readFloatLE(16), z0: b.readFloatLE(20), cell: b.readFloatLE(24) };
}

export const MagicCanopy = 'PCAN';
export const SUB = 4;   // samples per cell side
export const STEPS = 25;

/// Build the grid: per DEM cell (centred on its lattice node, as BaseY reads
/// the DEM), the mean of SUB x SUB nearest-pixel samples.
export function buildCanopyGrid(raster, lat) {
  const { nx, nz, x0, z0, cell } = lat;
  const grid = new Uint8Array(nx * nz);
  for (let iz = 0; iz < nz; iz++)
    for (let ix = 0; ix < nx; ix++) {
      let sum = 0;
      for (let sz = 0; sz < SUB; sz++)
        for (let sx = 0; sx < SUB; sx++) {
          const x = x0 + (ix - 0.5 + (sx + 0.5) / SUB) * cell, z = z0 + (iz - 0.5 + (sz + 0.5) / SUB) * cell;
          const [ax, ay] = albers102008(toLat(z), toLon(x));
          sum += raster.at(ax, ay);
        }
      grid[iz * nx + ix] = Math.round(sum / (SUB * SUB));
    }
  return grid;
}

export function encodeCanopy(lat, grid, year) {
  const head = Buffer.alloc(32);
  head.write(MagicCanopy, 0, 'latin1');
  head.writeInt32LE(1, 4);
  head.writeInt32LE(lat.nx, 8); head.writeInt32LE(lat.nz, 12);
  head.writeFloatLE(lat.x0, 16); head.writeFloatLE(lat.z0, 20); head.writeFloatLE(lat.cell, 24);
  head.writeUInt16LE(year, 28); head.writeUInt16LE(STEPS, 30);
  return Buffer.concat([head, Buffer.from(grid.map(v => Math.round(v * STEPS / 100)))]);
}

export function decodeCanopy(b) {
  if (b.toString('latin1', 0, 4) !== MagicCanopy) throw new Error('charlotte_canopy.bytes: bad magic');
  const nx = b.readInt32LE(8), nz = b.readInt32LE(12);
  return { nx, nz, x0: b.readFloatLE(16), z0: b.readFloatLE(20), cell: b.readFloatLE(24), year: b.readUInt16LE(28),
           steps: b.readUInt16LE(30), grid: b.subarray(32, 32 + nx * nz) };
}
