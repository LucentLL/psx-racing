// gatebase.mjs - the smoothness gate's tally, baseline and RATCHET for the
// offline tool (linecheck.mjs; probes import it too, so what they test is the
// ratchet that runs). Editor/CitySmooth.cs reads and writes the same baseline
// format (schema 2) for its own graph:mesh entries.
//
// Keys are (wayId, round(s on way / KeyStepM), check, line): a run carries one
// for EVERY 5 m bucket its bad samples touch, each with its own worst ratio.
// The ratchet fails on a new key, on a key worse than its baseline ratio, on
// more runs, or on a worse city-wide worst; a ZERO check fails on any run, a
// REPORT check never fails.
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { gzipSync, gunzipSync } from 'node:zlib';
import { dirname } from 'node:path';

const r1 = v => Math.round(v * 10) / 10, r3 = v => Math.round(v * 1000) / 1000;

/// Per check: runs, metres, worst, DATA/BUILDER, report-only runs, every key
/// with its worst ratio, and runs per tile (the tile of the run's worst sample,
/// "tx,tz"). A0 TEXTURE counts the texture's faults.
export function summarize(gate, R) {
  const summary = {};
  for (const c of R.Checks) summary[c.id] = { id: c.id, name: c.name, state: c.state, runs: 0, metres: 0, worst: 0, worstRatio: 0, data: 0, builder: 0, reportOnlyRuns: 0, reportOnlyMetres: 0, keys: new Map(), tiles: new Map() };
  for (const r of gate.runs) {
    const s = summary[r.check]; if (!s) continue;
    if (r.reportOnly) { s.reportOnlyRuns++; s.reportOnlyMetres += r.len; continue; }
    s.runs++; s.metres += r.len;
    r.kk.forEach((k, i) => { if (!(s.keys.get(k) >= r.kq[i])) s.keys.set(k, r.kq[i]); });
    s.tiles.set(r.tile, (s.tiles.get(r.tile) || 0) + 1);
    if (r.ratio > s.worstRatio) { s.worstRatio = r.ratio; s.worst = r.val; }
    if (r.data === 'DATA') s.data++; else s.builder++;
  }
  for (const f of gate.texFails || []) {
    const s = summary.A0; if (!s) break;
    s.runs++; s.builder++;
    s.keys.set(`tex:${f.key}_${f.surf}:${f.what}`, f.ratio);
    if (f.ratio > s.worstRatio) { s.worstRatio = f.ratio; s.worst = f.val; }
  }
  return summary;
}

/// FNV-1a over the key's UTF-8: the baseline stores 32-bit key hashes.
export function fnv1a(str) {
  let h = 0x811c9dc5;
  for (const b of Buffer.from(str, 'utf8')) { h ^= b; h = Math.imul(h, 0x01000193) >>> 0; }
  return h >>> 0;
}
/// A ratio stored as the power of RatioQuantum at or above it, so the same data
/// always passes its own baseline (C# uses the same float: Math.fround).
export function quantRatio(r, R) {
  const Q = Math.fround(R.RatioQuantum);
  if (!(r > 1)) return 0;
  let q = Math.ceil(Math.log(r) / Math.log(Q));
  while (Q ** q < r) q++;
  return q;
}
export const unquant = (q, R) => Math.fround(R.RatioQuantum) ** q;
function leb(bytes, v) { do { let b = v & 0x7f; v = Math.floor(v / 128); if (v) b |= 0x80; bytes.push(b); } while (v); }
function* unleb(raw) { let v = 0, mul = 1; for (const b of raw) { v += (b & 0x7f) * mul; if (b & 0x80) mul *= 128; else { yield v; v = 0; mul = 1; } } }

/// Schema 2: every key as (FNV-1a hash delta, quantised worst ratio) LEB128
/// pairs sorted by hash, gzipped, base64.
export function packKeys(keys, R) {
  const byHash = new Map();
  for (const [k, ratio] of keys) { const h = fnv1a(k), q = quantRatio(ratio, R); if (!(byHash.get(h) >= q)) byHash.set(h, q); }
  const hs = [...byHash.keys()].sort((a, b) => a - b);
  const bytes = [];
  let prev = 0;
  for (const h of hs) { leb(bytes, h - prev); prev = h; leb(bytes, byHash.get(h)); }
  return { n: hs.length, b64: gzipSync(Buffer.from(bytes), { level: 9 }).toString('base64') };
}
export function unpackKeys(b64) {
  const it = unleb(gunzipSync(Buffer.from(b64, 'base64'))), map = new Map();
  let prev = 0;
  for (;;) { const d = it.next(); if (d.done) break; prev += d.value; map.set(prev, it.next().value); }
  return map;
}
/// Runs per tile: tile id ((tx + 32768) << 16 | (tz + 32768)) deltas and counts.
export const tileId = (tx, tz) => (((tx + 32768) << 16) | (tz + 32768)) >>> 0;
export function packTiles(tiles) {
  const ids = [...tiles].map(([t, n]) => { const [tx, tz] = t.split(',').map(Number); return [tileId(tx, tz), n]; }).sort((a, b) => a[0] - b[0]);
  const bytes = [];
  let prev = 0;
  for (const [id, n] of ids) { leb(bytes, id - prev); prev = id; leb(bytes, n); }
  return gzipSync(Buffer.from(bytes), { level: 9 }).toString('base64');
}
export function unpackTiles(b64) {
  const it = unleb(gunzipSync(Buffer.from(b64, 'base64'))), map = new Map();
  let prev = 0;
  for (;;) { const d = it.next(); if (d.done) break; prev += d.value; map.set(prev, it.next().value); }
  return map;
}

export function readBaseline(path) {
  if (!existsSync(path)) return null;
  return JSON.parse(readFileSync(path, 'utf8'));
}
export function writeBaseline(path, id, summary, R) {
  const B = readBaseline(path) || {};
  B.schema = 2; B.tool = 'tools/city/linecheck.mjs';
  B.note = 'per graph hash and builder model: per-check runs, metres and worst; every violation key (way, round(s on way / 5 m), check, line) with its worst ratio, as FNV-1a hash deltas + ratio quanta (RatioQuantum) in LEB128, gzipped; runs per tile';
  B.entries = B.entries || {};
  for (const k of Object.keys(B.entries)) if (B.entries[k].schema !== 2) delete B.entries[k];   // schema 1 keys carry no ratios: re-record
  const checks = {};
  for (const [id2, s] of Object.entries(summary)) {
    const k = packKeys(s.keys, R);
    checks[id2] = { runs: s.runs, metres: r1(s.metres), worst: r3(s.worst), worstRatio: r3(s.worstRatio), keys: k.n, keys_b64: k.b64, tiles_b64: packTiles(s.tiles) };
  }
  B.entries[id] = { schema: 2, date: new Date().toISOString().slice(0, 10), V: R.V, checks };
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(B, null, 1) + '\n');
}

/// The ratchet (gate spec 1/8). opts.tiles (a Set of "tx,tz"): compare run
/// counts only against the baseline's runs in those tiles (a subset run, as
/// FAST is on the meshes). opts.pin: fail on any run on a pinned way.
export function ratchet(summary, base, R, opts = {}) {
  const lines = [];
  let ok = true;
  if (!base || base.schema !== 2) { lines.push(`no ${base ? 'schema-2 ' : ''}baseline for this graph and model: record one with --write-baseline (a re-export re-records, with before/after numbers in the commit)`); return { ok: false, lines }; }
  const tileSet = opts.tiles ? new Set([...opts.tiles].map(t => { const [tx, tz] = t.split(',').map(Number); return tileId(tx, tz); })) : null;
  for (const c of R.Checks) {
    const s = summary[c.id], b = base.checks[c.id];
    if (c.state === 'REPORT') { lines.push(`  ok   ${c.id} ${c.name}: report-only (${s.runs} runs)`); continue; }
    if (c.state === 'ZERO') {
      const pass = s.runs === 0; ok = ok && pass;
      lines.push(`${pass ? '  ok  ' : '  FAIL'} ${c.id} ${c.name}: ${s.runs} runs (ZERO)`); continue;
    }
    if (!b) { ok = false; lines.push(`  FAIL ${c.id} ${c.name}: not in the baseline`); continue; }
    const old = unpackKeys(b.keys_b64);
    let fresh = 0, worseKeys = 0, ex = null;
    for (const [k, ratio] of s.keys) {
      const q = old.get(fnv1a(k));
      if (q === undefined) { fresh++; ex = ex || `new ${k} x${r1(ratio)}`; }
      else if (ratio > unquant(q, R) * (1 + 1e-9)) { worseKeys++; ex = ex || `worse ${k} x${r1(ratio)} (baseline x${r1(unquant(q, R))})`; }
    }
    let baseRuns = b.runs;
    if (tileSet) { baseRuns = 0; for (const [t, n] of unpackTiles(b.tiles_b64)) if (tileSet.has(t)) baseRuns += n; }
    const worse = s.worstRatio > b.worstRatio + 1e-3, more = s.runs > baseRuns;
    const pass = !fresh && !worseKeys && !worse && !more; ok = ok && pass;
    lines.push(`${pass ? '  ok  ' : '  FAIL'} ${c.id} ${c.name}: ${s.runs} runs (baseline ${baseRuns}${tileSet ? ' in these tiles' : ''})${more ? ' MORE' : ''}, worst x${r1(s.worstRatio)} (baseline x${r1(b.worstRatio)})${worse ? ' WORSE' : ''}, ${fresh} new keys, ${worseKeys} worse keys of ${s.keys.size}${ex ? `; e.g. ${ex}` : ''}`);
  }
  if (opts.pin) {
    const n = (opts.runs || []).filter(r => r.pinned && !r.reportOnly).length;
    ok = ok && n === 0;
    lines.push(`${n === 0 ? '  ok  ' : '  FAIL'} creek pin: ${n} runs on ways ${R.PinnedWays.join(', ')}`);
  }
  return { ok, lines };
}
