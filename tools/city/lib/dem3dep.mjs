// dem3dep.mjs - read the USGS 3DEP 1/3" box fetched by fetch/fetch_3dep.mjs.
//
// The box is float32 metres (NAVD88) on the global 1/10800-degree lattice,
// rows north to south; pixel (c, r) covers lon west + [c, c+1] * res and lat
// north - [r, r+1] * res. Three reads are offered:
//   sample(lat, lon)            bilinear between pixel centres
//   blockMean(s, n, w, e)       the mean of every pixel whose CENTRE is in
//                               the box (the 60 m area mean of the game DEM)
//   minNear(lat, lon, rM)       the lowest pixel whose centre is within rM
//                               metres (a creek bed near a surveyed line)
// Public domain; credit "U.S. Geological Survey, 3D Elevation Program".

import { readFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));

/// Where the box lives: %PSX_GIS_DIR%\3dep, else tools/city/cache/3dep.
export function dem3depDir() {
  return process.env.PSX_GIS_DIR ? join(process.env.PSX_GIS_DIR, '3dep') : join(HERE, '..', 'cache', '3dep');
}

export function load3dep() {
  const dir = dem3depDir();
  const f32 = join(dir, 'box13.f32'), js = join(dir, 'box13.json');
  if (!existsSync(f32) || !existsSync(js))
    throw new Error(`${f32} is missing: fetch it with node tools/city/fetch/fetch_3dep.mjs (USGS 3DEP 1/3", ~0.1 GB of range reads)`);
  const meta = JSON.parse(readFileSync(js, 'utf8'));
  const buf = readFileSync(f32);
  if (buf.length !== meta.cols * meta.rows * 4) throw new Error(`${f32}: ${buf.length} bytes, header says ${meta.cols} x ${meta.rows}`);
  const grid = new Float32Array(buf.buffer, buf.byteOffset, meta.cols * meta.rows);
  const { cols, rows, res_deg: res, west, north } = meta;
  const M_LAT = 111132;   // metres per degree of latitude (the export frame's)
  const inside = (c, r) => c >= 0 && r >= 0 && c < cols && r < rows;
  const at = (c, r) => {
    if (!inside(c, r)) throw new Error(`3DEP box: pixel (${c}, ${r}) outside ${cols} x ${rows}`);
    return grid[r * cols + c];
  };
  function sample(lat, lon) {
    const fc = (lon - west) / res - 0.5, fr = (north - lat) / res - 0.5;
    const c = Math.floor(fc), r = Math.floor(fr), tx = fc - c, tz = fr - r;
    return (at(c, r) * (1 - tx) + at(c + 1, r) * tx) * (1 - tz) + (at(c, r + 1) * (1 - tx) + at(c + 1, r + 1) * tx) * tz;
  }
  function blockMean(s, n, w, e) {
    const c0 = Math.ceil((w - west) / res - 0.5), c1 = Math.floor((e - west) / res - 0.5);
    const r0 = Math.ceil((north - n) / res - 0.5), r1 = Math.floor((north - s) / res - 0.5);
    let sum = 0, k = 0;
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) { sum += at(c, r); k++; }
    if (!k) return sample((s + n) / 2, (w + e) / 2);
    return sum / k;
  }
  function minNear(lat, lon, rM) {
    const mLon = M_LAT * Math.cos(lat * Math.PI / 180);
    const dc = Math.ceil(rM / (mLon * res)) + 1, dr = Math.ceil(rM / (M_LAT * res)) + 1;
    const cc = Math.floor((lon - west) / res), rc = Math.floor((north - lat) / res);
    let best = Infinity;
    for (let r = rc - dr; r <= rc + dr; r++) for (let c = cc - dc; c <= cc + dc; c++) {
      if (!inside(c, r)) continue;
      const plat = north - (r + 0.5) * res, plon = west + (c + 0.5) * res;
      if (Math.hypot((plat - lat) * M_LAT, (plon - lon) * mLon) > rM) continue;
      const v = grid[r * cols + c];
      if (v < best) best = v;
    }
    return best === Infinity ? sample(lat, lon) : best;
  }
  return { meta, sample, blockMean, minNear, pixel: at, f32Path: f32, jsonPath: js };
}
