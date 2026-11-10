// cog.mjs - read tiles of a Cloud Optimized GeoTIFF over HTTP range requests.
//
// Lifted out of fetch/fetch_3dep.mjs (the Charlotte 1/3" box) so the mountain
// stages can read the same USGS files, and the 1 m DEM tiles, without a second
// copy of the TIFF directory reader, the LZW decoder and the predictor. No npm.
//
// What it reads: classic or BigTIFF, little-endian, tiled, one float32 sample,
// LZW (5) or Deflate (8 / 32946), predictor 1 (none), 2 (horizontal, on the
// 32-bit words) or 3 (floating point). Every image directory in the file is
// returned, so a caller can take an overview level instead of the full image
// (the 60 m far grid does not need 10 m pixels).
//
// Public domain data; credit "U.S. Geological Survey, 3D Elevation Program".

import { inflateSync } from 'node:zlib';

const sleep = ms => new Promise(r => setTimeout(r, ms));

/// A range reader bound to one User-Agent, counting what it reads.
export function makeFetcher(userAgent) {
  const stats = { requests: 0, bytes: 0 };
  async function fetchRange(url, start, end) {
    let last;
    for (let attempt = 0; attempt < 6; attempt++) {
      try {
        const r = await fetch(url, { headers: { 'User-Agent': userAgent, Range: `bytes=${start}-${end}` } });
        if (r.status !== 206 && r.status !== 200) throw new Error(`HTTP ${r.status}`);
        const b = Buffer.from(await r.arrayBuffer());
        if (b.length !== end - start + 1) throw new Error(`short read ${b.length} of ${end - start + 1}`);
        stats.requests++; stats.bytes += b.length;
        return b;
      } catch (e) { last = e; await sleep(1500 * (attempt + 1)); }
    }
    throw new Error(`${url} [${start}-${end}]: ${last.message}`);
  }
  async function head(url) {
    let last;
    for (let attempt = 0; attempt < 6; attempt++) {
      try {
        const r = await fetch(url, { method: 'HEAD', headers: { 'User-Agent': userAgent } });
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return { bytes: +r.headers.get('content-length'), lastModified: r.headers.get('last-modified'), etag: r.headers.get('etag') };
      } catch (e) { last = e; await sleep(1500 * (attempt + 1)); }
    }
    throw new Error(`HEAD ${url}: ${last.message}`);
  }
  return { fetchRange, head, stats };
}

// ---------------------------------------------------------------- TIFF
const SIZE = { 1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 6: 1, 7: 1, 8: 2, 9: 4, 10: 8, 11: 4, 12: 8, 16: 8, 17: 8, 18: 8 };

/// Every image directory of a little-endian TIFF (classic or BigTIFF), with
/// the tags a tiled float32 raster needs. `headerBytes` is how much of the
/// file's head was read to get them (for the download ledger).
export async function readIfds(fetchRange, url, { firstOnly = false } = {}) {
  let buf = await fetchRange(url, 0, 65535);
  let headerBytes = buf.length;
  const ensure = async n => {
    if (n > buf.length) {
      const more = await fetchRange(url, buf.length, n + 65535);
      headerBytes += more.length;
      buf = Buffer.concat([buf, more]);
    }
  };
  if (buf.toString('latin1', 0, 2) !== 'II') throw new Error(`${url}: not a little-endian TIFF`);
  const magic = buf.readUInt16LE(2);
  if (magic !== 42 && magic !== 43) throw new Error(`${url}: not a TIFF (magic ${magic})`);
  const big = magic === 43;
  let off = big ? Number(buf.readBigUInt64LE(8)) : buf.readUInt32LE(4);
  const ifds = [];
  while (off && ifds.length < 16) {
    await ensure(off + 8);
    const n = big ? Number(buf.readBigUInt64LE(off)) : buf.readUInt16LE(off);
    const entry = big ? 20 : 12, first = off + (big ? 8 : 2);
    await ensure(first + n * entry + 8);
    const tags = {};
    for (let k = 0; k < n; k++) {
      const p = first + k * entry;
      const tag = buf.readUInt16LE(p), type = buf.readUInt16LE(p + 2);
      const count = big ? Number(buf.readBigUInt64LE(p + 4)) : buf.readUInt32LE(p + 4);
      const bytes = (SIZE[type] || 1) * count;
      const inline = big ? 8 : 4;
      let at = p + (big ? 12 : 8);
      if (bytes > inline) { at = big ? Number(buf.readBigUInt64LE(at)) : buf.readUInt32LE(at); await ensure(at + bytes); }
      const vals = [];
      for (let i = 0; i < count && i < 4e6; i++) {
        if (type === 3) vals.push(buf.readUInt16LE(at + 2 * i));
        else if (type === 4) vals.push(buf.readUInt32LE(at + 4 * i));
        else if (type === 12) vals.push(buf.readDoubleLE(at + 8 * i));
        else if (type === 16) vals.push(Number(buf.readBigUInt64LE(at + 8 * i)));
        else if (type === 2) { vals.push(buf.toString('latin1', at, at + count).replace(/\0+$/, '')); break; }
        else vals.push(buf[at + i]);
      }
      tags[tag] = vals;
    }
    const one = t => tags[t]?.[0];
    ifds.push({
      width: one(256), height: one(257), bits: one(258), compression: one(259), predictor: one(317) || 1,
      tileW: one(322), tileH: one(323), offsets: tags[324], counts: tags[325], format: one(339) || 1,
      scale: tags[33550], tie: tags[33922], geoKeys: tags[34735], geoAscii: one(34737),
      nodata: tags[42113] ? parseFloat(tags[42113][0]) : null, subfile: one(254) || 0,
    });
    if (firstOnly) break;
    const nextAt = first + n * entry;
    off = big ? Number(buf.readBigUInt64LE(nextAt)) : buf.readUInt32LE(nextAt);
  }
  return { ifds, headerBytes, big };
}

/// The check fetch_3dep.mjs has always made on the 1/3" files.
export function assertFloatLzw(url, d) {
  if (d.compression !== 5 || d.predictor !== 3 || d.bits !== 32 || d.format !== 3 || !d.tileW)
    throw new Error(`${url}: expected tiled float32 LZW with the floating-point predictor, got ${JSON.stringify({ c: d.compression, p: d.predictor, b: d.bits, f: d.format, t: d.tileW })}`);
}

/// TIFF LZW (MSB-first codes, 9-12 bits, early change: the width grows when
/// the next free code reaches 2^width - 1, as libtiff decodes it).
export function lzwDecode(src, expected) {
  const out = new Uint8Array(expected);
  const prefix = new Int32Array(4096), suffix = new Uint8Array(4096), first = new Uint8Array(4096), len = new Int32Array(4096);
  for (let i = 0; i < 256; i++) { prefix[i] = -1; suffix[i] = i; first[i] = i; len[i] = 1; }
  let next = 258, width = 9, old = -1, op = 0, bit = 0;
  const nbits = src.length * 8;
  const emit = code => {
    const L = len[code];
    if (op + L > out.length) throw new Error('LZW: output overrun');
    let c = code;
    for (let k = op + L - 1; k >= op; k--) { out[k] = suffix[c]; c = prefix[c]; }
    op += L;
  };
  for (;;) {
    if (bit + width > nbits) break;
    let v = 0, need = width, p = bit;
    while (need > 0) {
      const off = p & 7, avail = 8 - off, take = avail < need ? avail : need;
      v = (v << take) | ((src[p >> 3] >> (avail - take)) & ((1 << take) - 1));
      need -= take; p += take;
    }
    bit += width;
    const code = v;
    if (code === 257) break;
    if (code === 256) { next = 258; width = 9; old = -1; continue; }
    if (old < 0) {
      if (code > 255) throw new Error('LZW: bad first code');
      emit(code); old = code; continue;
    }
    if (code < next) {
      emit(code);
      if (next < 4096) { prefix[next] = old; suffix[next] = first[code]; first[next] = first[old]; len[next] = len[old] + 1; next++; }
    } else if (code === next) {
      if (next < 4096) { prefix[next] = old; suffix[next] = first[old]; first[next] = first[old]; len[next] = len[old] + 1; next++; }
      emit(code);
    } else throw new Error(`LZW: code ${code} past the table (${next})`);
    old = code;
    if (next + 1 >= (1 << width) && width < 12) width++;
  }
  if (op !== expected) throw new Error(`LZW: ${op} bytes, expected ${expected}`);
  return out;
}

/// Undo TIFF predictor 3 (floating point, TIFF Technical Note 3) on one tile:
/// per row, a byte-wise running sum, then the byte planes (most significant
/// first) re-interleaved into little-endian float32.
export function undoFloatPredictor(bytes, w, h) {
  const out = new Float32Array(w * h);
  const le = new Uint8Array(out.buffer);
  const rowB = w * 4;
  for (let y = 0; y < h; y++) {
    const r = y * rowB;
    for (let i = 1; i < rowB; i++) bytes[r + i] = (bytes[r + i] + bytes[r + i - 1]) & 255;
    for (let i = 0; i < w; i++)
      for (let b = 0; b < 4; b++) le[r + 4 * i + b] = bytes[r + (3 - b) * w + i];
  }
  return out;
}

/// One stored tile (its compressed bytes) of directory `d` as float32,
/// tileW x tileH, whatever of the three predictors and two codecs it uses.
export function decodeTile(d, raw) {
  const n = d.tileW * d.tileH * 4;
  if (d.bits !== 32 || d.format !== 3) throw new Error(`cog: only float32 rasters (bits ${d.bits}, format ${d.format})`);
  let bytes;
  if (d.compression === 5) bytes = lzwDecode(raw, n);
  else if (d.compression === 8 || d.compression === 32946) {
    const z = inflateSync(raw);
    if (z.length !== n) throw new Error(`cog: inflate gave ${z.length} bytes, expected ${n}`);
    bytes = new Uint8Array(z.buffer, z.byteOffset, n).slice();
  } else if (d.compression === 1) bytes = new Uint8Array(raw.buffer, raw.byteOffset, n).slice();
  else throw new Error(`cog: compression ${d.compression} is not read here`);
  if (d.predictor === 3) return undoFloatPredictor(bytes, d.tileW, d.tileH);
  if (d.predictor === 2) {
    const w32 = new Uint32Array(bytes.buffer, bytes.byteOffset, d.tileW * d.tileH);
    for (let y = 0; y < d.tileH; y++)
      for (let x = 1; x < d.tileW; x++) w32[y * d.tileW + x] = (w32[y * d.tileW + x] + w32[y * d.tileW + x - 1]) >>> 0;
  } else if (d.predictor !== 1) throw new Error(`cog: predictor ${d.predictor} is not read here`);
  return new Float32Array(bytes.buffer, bytes.byteOffset, d.tileW * d.tileH);
}
