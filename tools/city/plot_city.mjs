// plot_city.mjs — rasterize charlotte_city.json to a PNG for eyeballing.
// No dependencies: minimal PNG writer over node's zlib.
//   node tools/city/plot_city.mjs [outPath] [sizePx]

import { readFileSync, writeFileSync } from 'fs';
import { deflateSync } from 'zlib';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const HERE = dirname(fileURLToPath(import.meta.url));
const city = JSON.parse(readFileSync(join(HERE, '..', '..', 'Assets', 'PSXRacing', 'Resources', 'charlotte_city.json'), 'utf8'));
const OUT = process.argv[2] || join(HERE, 'charlotte_map.png');
const S = Number(process.argv[3] || 1400);

// ------- canvas
const px = new Uint8Array(S * S * 3).fill(255);
function put(x, y, r, g, b) {
  if (x < 0 || y < 0 || x >= S || y >= S) return;
  const i = (y * S + x) * 3;
  px[i] = r; px[i + 1] = g; px[i + 2] = b;
}
function disc(x, y, rad, r, g, b) {
  for (let dy = -rad; dy <= rad; dy++) for (let dx = -rad; dx <= rad; dx++)
    if (dx * dx + dy * dy <= rad * rad) put(Math.round(x + dx), Math.round(y + dy), r, g, b);
}
function line(x0, y0, x1, y1, wpx, r, g, b) {
  const L = Math.hypot(x1 - x0, y1 - y0);
  const steps = Math.max(1, Math.ceil(L));
  const rad = Math.max(0, Math.round(wpx / 2));
  for (let i = 0; i <= steps; i++) {
    const x = x0 + (x1 - x0) * i / steps, y = y0 + (y1 - y0) * i / steps;
    if (rad === 0) put(Math.round(x), Math.round(y), r, g, b);
    else disc(x, y, rad, r, g, b);
  }
}

// ------- world -> pixel  (x east, z north; png y down)
let mnx = 1e18, mxx = -1e18, mnz = 1e18, mxz = -1e18;
for (const e of city.edges) for (let i = 0; i < e.pts.length; i += 2) {
  mnx = Math.min(mnx, e.pts[i]); mxx = Math.max(mxx, e.pts[i]);
  mnz = Math.min(mnz, e.pts[i + 1]); mxz = Math.max(mxz, e.pts[i + 1]);
}
const span = Math.max(mxx - mnx, mxz - mnz) * 1.04;
const cx = (mnx + mxx) / 2, cz = (mnz + mxz) / 2;
const X = wx => (wx - cx) / span * S + S / 2;
const Y = wz => S / 2 - (wz - cz) / span * S;
const mPerPx = span / S;

// ------- water first
for (const w of city.waters) {
  const pts = [];
  for (let i = 0; i < w.pts.length; i += 2) pts.push([X(w.pts[i]), Y(w.pts[i + 1])]);
  if (w.lake) {
    // scanline fill
    let y0 = 1e9, y1 = -1e9;
    for (const p of pts) { y0 = Math.min(y0, p[1]); y1 = Math.max(y1, p[1]); }
    for (let y = Math.max(0, Math.floor(y0)); y <= Math.min(S - 1, Math.ceil(y1)); y++) {
      const xs = [];
      for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
        const [xi, yi] = pts[i], [xj, yj] = pts[j];
        if ((yi > y) !== (yj > y)) xs.push(xi + (xj - xi) * (y - yi) / (yj - yi));
      }
      xs.sort((a, b) => a - b);
      for (let k = 0; k + 1 < xs.length; k += 2)
        for (let x = Math.max(0, Math.round(xs[k])); x <= Math.min(S - 1, Math.round(xs[k + 1])); x++)
          put(x, y, 168, 214, 238);
    }
  } else {
    const wpx = Math.max(1, w.w / mPerPx);
    for (let i = 1; i < pts.length; i++) line(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], wpx, 138, 194, 228);
  }
}

// ------- edges by class (minor first so freeways draw over)
const CLS_STYLE = {
  1: [200, 200, 200], 2: [150, 150, 150], 3: [232, 180, 0], 4: [240, 140, 0], 5: [232, 68, 42],
};
const order = [...city.edges].sort((a, b) => (a.cls - b.cls) || (a.link - b.link));
for (const e of order) {
  const col = e.link ? [120, 200, 140] : CLS_STYLE[e.cls] || [180, 180, 180];
  const wpx = Math.max(1, e.w / mPerPx);
  for (let i = 2; i < e.pts.length; i += 2)
    line(X(e.pts[i - 2]), Y(e.pts[i - 1]), X(e.pts[i]), Y(e.pts[i + 1]), wpx, col[0], col[1], col[2]);
}

// ------- separations, water spans, uptown
for (const c of city.crossings) disc(X(c.x), Y(c.z), 2, 122, 42, 160);
for (const s of city.wspans) {
  const e = city.edges[s.e];
  disc(X(e.pts[0]), Y(e.pts[1]), 2, 10, 84, 200);
}
disc(X(city.uptownX), Y(city.uptownZ), 5, 0, 0, 0);
disc(X(city.uptownX), Y(city.uptownZ), 3, 255, 255, 255);

// ------- minimal PNG writer
function crc32(buf) {
  let c, table = crc32.table;
  if (!table) {
    table = crc32.table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
      c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
      table[n] = c;
    }
  }
  c = ~0;
  for (let i = 0; i < buf.length; i++) c = table[(c ^ buf[i]) & 0xFF] ^ (c >>> 8);
  return ~c >>> 0;
}
function chunk(type, data) {
  const out = Buffer.alloc(8 + data.length + 4);
  out.writeUInt32BE(data.length, 0);
  out.write(type, 4, 'ascii');
  data.copy(out, 8);
  out.writeUInt32BE(crc32(out.subarray(4, 8 + data.length)), 8 + data.length);
  return out;
}
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(S, 0); ihdr.writeUInt32BE(S, 4);
ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
const raw = Buffer.alloc(S * (S * 3 + 1));
for (let y = 0; y < S; y++) {
  raw[y * (S * 3 + 1)] = 0;
  Buffer.from(px.subarray(y * S * 3, (y + 1) * S * 3)).copy(raw, y * (S * 3 + 1) + 1);
}
const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
  chunk('IHDR', ihdr),
  chunk('IDAT', deflateSync(raw, { level: 6 })),
  chunk('IEND', Buffer.alloc(0)),
]);
writeFileSync(OUT, png);
console.log(`wrote ${OUT} (${(png.length / 1024).toFixed(0)} KB, ${S}px, ${mPerPx.toFixed(1)} m/px)`);
