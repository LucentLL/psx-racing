// census loader: the shipped city (PC45 = mountain-nm 7badf362), read-only.
import fs from 'node:fs';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
// the repo this tool sits in (tools/city/splitcensus/..)
export const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..').replace(/\\/g, '/');
// the output folder: PSX_SPLITCENSUS_OUT, else the current directory
export const OUT = (process.env.PSX_SPLITCENSUS_OUT || process.cwd()).replace(/\\/g, '/');
const CD = await import(pathToFileURL(ROOT + '/tools/city/lib/citydata.mjs').href);
export const { parseCity, profileFor, lineSetOf, hashHex } = CD;
export const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;
export const KX = 111320 * Math.cos(35.18456 * Math.PI / 180), KZ = 111132;
export const toLL = (x, z) => [z / KZ + LAT0, x / KX + LON0];
export const fromLL = (lat, lon) => [(lon - LON0) * KX, (lat - LAT0) * KZ];
export const llStr = (x, z) => { const [a, b] = toLL(x, z); return a.toFixed(6) + ',' + b.toFixed(6); };

export function fnv64(buf) {
  let h = 0xcbf29ce484222325n; const p = 0x100000001b3n, m = (1n << 64n) - 1n;
  for (let i = 0; i < buf.length; i++) { h ^= BigInt(buf[i]); h = (h * p) & m; }
  return h.toString(16).padStart(16, '0');
}

export function load() {
  const buf = fs.readFileSync(ROOT + '/Assets/PSXRacing/Resources/charlotte_city.bytes');
  const city = parseCity(buf);
  const S = city.sections;
  // SPLT
  let splt = [];
  if (S.SPLT) {
    let p = S.SPLT[0]; const n = buf.readUInt32LE(p); p += 4;
    for (let i = 0; i < n; i++) {
      splt.push({ node: buf.readInt32LE(p), u: buf.readInt32LE(p + 4), a: buf.readInt32LE(p + 8), b: buf.readInt32LE(p + 12),
                  offA: buf.readFloatLE(p + 16), offB: buf.readFloatLE(p + 20), rate: buf.readFloatLE(p + 24) });
      p += 28;
    }
  }
  city.splt = splt;
  city.buf = buf;
  // edge helpers
  for (const e of city.edges) {
    e.cls = e.rank;
    e.speedKmh = e.speed;
    e.shl = e.profile.shl; e.shr = e.profile.shr;
  }
  return city;
}

// polyline helpers on an edge (pts [[x,z]], s cumulative)
export function pointAt(e, s) {
  const P = e.pts, S = e.s, n = P.length;
  if (s <= 0) return [P[0][0], P[0][1]];
  if (s >= e.length) return [P[n - 1][0], P[n - 1][1]];
  let lo = 0, hi = n - 1;
  while (hi - lo > 1) { const m = (lo + hi) >> 1; if (S[m] <= s) lo = m; else hi = m; }
  const t = (s - S[lo]) / Math.max(1e-9, S[hi] - S[lo]);
  return [P[lo][0] + (P[hi][0] - P[lo][0]) * t, P[lo][1] + (P[hi][1] - P[lo][1]) * t];
}
export function tangentAt(e, s) {
  const P = e.pts, S = e.s, n = P.length;
  let k = 1;
  if (s <= 0) k = 1; else if (s >= e.length) k = n - 1;
  else { let lo = 0, hi = n - 1; while (hi - lo > 1) { const m = (lo + hi) >> 1; if (S[m] <= s) lo = m; else hi = m; } k = hi; }
  let dx = P[k][0] - P[k - 1][0], dz = P[k][1] - P[k - 1][1];
  const d = Math.hypot(dx, dz) || 1; return [dx / d, dz / d];
}
// out direction of edge e at node n (first segment)
export function outDir(e, n) {
  const P = e.pts; let p, q;
  if (e.a === n) { p = P[0]; q = P[1]; } else { p = P[P.length - 1]; q = P[P.length - 2]; }
  const d = Math.hypot(q[0] - p[0], q[1] - p[1]) || 1; return [(q[0] - p[0]) / d, (q[1] - p[1]) / d];
}
