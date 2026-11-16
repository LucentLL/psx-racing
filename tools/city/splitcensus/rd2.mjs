// My own reader for charlotte_city.bytes (sections NODE NAME EDGE PNTS GHSH TAPR SPLT), from CityMap.cs:712-990.
import fs from 'node:fs';
import { ROOT } from './load.mjs';
export const BYTES = ROOT + '/Assets/PSXRacing/Resources/charlotte_city.bytes';
const LaneM = 3.6576;
const prof = [];
const TW = (l, t) => ({ lanes: l, turn: t, shl: 0.3, shr: 0.3, oneway: false });
const OW = l => ({ lanes: l, shl: 0.3, shr: 0.3, oneway: true });
const RP = l => ({ lanes: l, shl: 0.6, shr: 1.8, oneway: true });
const XW = l => ({ lanes: l, shl: 0.9, shr: 2.4, oneway: true });
const MW = l => ({ lanes: l, shl: 1.2, shr: 3.0, oneway: true });
prof.push(TW(2, false), TW(3, true), TW(4, false), TW(5, true), TW(6, false), OW(1), OW(2), OW(3), OW(4), RP(1), RP(2), RP(3), XW(2), XW(3), XW(4), MW(2), MW(3), MW(4), MW(5), MW(6));
for (const p of prof) p.width = p.lanes * LaneM + p.shl + p.shr;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
export function indexFor(cls, link, oneway, lanes, turn) {
  if (!oneway) { let n = clamp(lanes, 2, 6); if (turn && n % 2 === 0 && n < 6) n++; if (n === 3 || n === 5) return n === 3 ? 1 : 3; return n <= 2 ? 0 : n === 4 ? 2 : 4; }
  if (link) { if (cls >= 4) return 9 + clamp(lanes, 1, 3) - 1; return 5 + clamp(lanes, 1, 4) - 1; }
  if (cls >= 4 && lanes <= 1) return 9;
  if (cls >= 5) return 15 + clamp(lanes, 2, 6) - 2;
  if (cls === 4) return 12 + clamp(lanes, 2, 4) - 2;
  return 5 + clamp(lanes, 1, 4) - 1;
}
export function fnv64(buf) {
  let h = 0xcbf29ce484222325n; const P = 0x100000001b3n, M = (1n << 64n) - 1n;
  for (const b of buf) { h ^= BigInt(b); h = (h * P) & M; }
  return h.toString(16).padStart(16, '0');
}
export function load(path = BYTES) {
  const buf = fs.readFileSync(path);
  const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  let pos = 0;
  const u32 = () => { const v = dv.getUint32(pos, true); pos += 4; return v; };
  const i32 = () => { const v = dv.getInt32(pos, true); pos += 4; return v; };
  const f32 = () => { const v = dv.getFloat32(pos, true); pos += 4; return v; };
  const u8 = () => buf[pos++];
  const i8 = () => { const v = dv.getInt8(pos); pos += 1; return v; };
  const u16 = () => { const v = dv.getUint16(pos, true); pos += 2; return v; };
  const str = () => { let len = 0, shift = 0, b; do { b = buf[pos++]; len |= (b & 127) << shift; shift += 7; } while (b & 128); const s = buf.toString('utf8', pos, pos + len); pos += len; return s; };
  const magic = u32(); const version = i32(); const nsec = i32();
  const tbl = {};
  for (let i = 0; i < nsec; i++) { const t = u32(); const off = i32(), len = i32(); const tag = String.fromCharCode(t & 255, (t >> 8) & 255, (t >> 16) & 255, (t >> 24) & 255); tbl[tag] = { off, len }; }
  const open = t => { pos = tbl[t].off; };
  const close = t => { if (pos !== tbl[t].off + tbl[t].len) throw new Error('section ' + t + ' read ' + (pos - tbl[t].off) + ' of ' + tbl[t].len); };
  const map = { magic, version, tags: Object.keys(tbl) };
  open('NODE'); const nn = i32(); map.nodes = new Array(nn); map.ctl = new Uint8Array(nn);
  for (let i = 0; i < nn; i++) { map.nodes[i] = [f32(), f32()]; map.ctl[i] = u8(); } close('NODE');
  open('NAME'); const ns = i32(); const names = []; for (let i = 0; i < ns; i++) names.push(str()); close('NAME');
  open('EDGE'); const ne = i32(); map.edges = new Array(ne); map.nodeEdges = Array.from({ length: nn }, () => []);
  const pc = new Int32Array(ne);
  for (let i = 0; i < ne; i++) {
    const e = { index: i }; e.a = i32(); e.b = i32(); e.name = names[i32()]; e.cls = u8(); const fl = u8();
    e.link = !!(fl & 1); e.oneway = !!(fl & 2); e.bridge = !!(fl & 4); e.tunnel = !!(fl & 8); e.turnLane = !!(fl & 16); e.roundabout = !!(fl & 32);
    e.lanes = Math.max(1, u8()); e.layer = i8(); e.expW = f32(); e.shl0 = f32(); e.shr0 = f32(); e.speedKmh = u8(); e.wayId = u32();
    e.profile = indexFor(e.cls, e.link, e.oneway, e.lanes, e.turnLane); const p = prof[e.profile];
    e.width = p.width; e.shl = p.shl; e.shr = p.shr; pc[i] = u16(); map.edges[i] = e;
    if (e.a >= 0 && e.a < nn) map.nodeEdges[e.a].push(i); if (e.b >= 0 && e.b < nn) map.nodeEdges[e.b].push(i);
  }
  close('EDGE');
  open('PNTS'); const tot = i32();
  for (let i = 0; i < ne; i++) { const e = map.edges[i]; const n = pc[i]; e.pts = new Array(n); e.s = new Float64Array(n);
    for (let k = 0; k < n; k++) e.pts[k] = [f32(), f32()];
    let acc = 0; for (let k = 1; k < n; k++) { acc += Math.hypot(e.pts[k][0] - e.pts[k - 1][0], e.pts[k][1] - e.pts[k - 1][1]); e.s[k] = acc; } e.length = acc; }
  close('PNTS');
  open('GHSH'); map.graphHash = u32().toString(16).padStart(8, '0'); close('GHSH');
  map.tapr = []; const taprOff = new Float64Array(ne);
  if (tbl.TAPR) { open('TAPR'); const nt = i32(); for (let i = 0; i < nt; i++) map.tapr.push({ edge: i32(), end: u8(), side: u8(), src: u8(), flags: u8(), dw: f32(), len: f32(), room: f32(), off: f32() });
    const no = i32(); for (let i = 0; i < no; i++) { const ei = i32(); const o0 = f32(), o1 = f32(); f32(); f32(); if (ei >= 0 && ei < ne) taprOff[ei] = 0.5 * (o0 + o1); } close('TAPR'); }
  map.taprOff = taprOff;
  map.splt = [];
  if (tbl.SPLT) { open('SPLT'); const n = i32(); for (let i = 0; i < n; i++) map.splt.push({ node: i32(), u: i32(), a: i32(), b: i32(), offA: f32(), offB: f32(), rate: f32() }); close('SPLT'); }
  if (tbl.LSET) { open('LSET'); const nls = i32(); for (let i = 0; i < nls; i++) { const nF = u8(), nB = u8(), centre = u8(), fl = u8(); u16(); u8(); u8(); if (i >= ne) continue; const e = map.edges[i]; const p = prof[e.profile]; const fits = p.oneway ? (nB === 0 && centre === 0 && nF === p.lanes) : (nF >= 1 && nB >= 1 && centre <= 2 && nF + nB + (centre === 2 ? 1 : 0) === p.lanes); e.lsFlags = fl; if (fits) { e.lsNF = nF; e.lsNB = nB; e.lsC = centre; } else { const t = p.turn ? 1 : 0; e.lsNF = e.lsNB = (p.lanes - t) / 2; e.lsC = p.oneway ? 0 : (p.turn ? 2 : 1); if (p.oneway) { e.lsNF = p.lanes; e.lsNB = 0; } } e.hasLset = true; } close('LSET'); }
  // LineModel.Init (no FixMirrored)
  for (const e of map.edges) { const off = taprOff[e.index] || 0; const lh = (e.width - e.shl - e.shr) * 0.5; e.lmOff = off; e.lmPlus = lh + e.shl + off; e.lmMinus = lh + e.shr - off; }
  return { map, buf };
}
export function pointAt(e, s) {
  const P = e.pts, S = e.s; if (s <= 0) return P[0].slice(); if (s >= e.length) return P[P.length - 1].slice();
  let k = 1; while (k < P.length - 1 && S[k] < s) k++;
  const t = (s - S[k - 1]) / Math.max(1e-9, S[k] - S[k - 1]); return [P[k - 1][0] + (P[k][0] - P[k - 1][0]) * t, P[k - 1][1] + (P[k][1] - P[k - 1][1]) * t];
}
export function tangentAt(e, s) {
  const P = e.pts, S = e.s; let k = 1; while (k < P.length - 1 && S[k] < s) k++;
  const dx = P[k][0] - P[k - 1][0], dy = P[k][1] - P[k - 1][1], d = Math.hypot(dx, dy) || 1; return [dx / d, dy / d];
}
export const toLL = (x, z) => [35.18456015184093 + z / 111132, -80.81770185962013 + x / (111320 * Math.cos(35.18456 * Math.PI / 180))];
