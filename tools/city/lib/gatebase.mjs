// gatebase.mjs - the smoothness gate's tally, baseline and RATCHET for the
// offline tool (linecheck.mjs; probes import it too, so what they test is the
// ratchet that runs). Editor/CitySmooth.cs reads and writes the same baseline
// format (schema 3) for its own 'mesh' entry.
//
// Keys are (wayId, round(s on way / KeyStepM), check, line): a run carries one
// for EVERY 5 m bucket its bad samples touch, each with its own worst ratio and
// its BAD LENGTH there (the arc between consecutive bad samples, summed over
// the runs that share the key). The ratchet fails on a new key, on a key worse
// than its baseline ratio, on a key longer than its baseline length, on more
// runs, on more metres or on a worse city-wide worst; a ZERO check fails on
// any run, a REPORT check never fails.
//
// Each entry carries the INPUTS it was measured on (inputsOf): the graph hash,
// a digest of each container section the replica reads, of the geometry rules
// in SmoothRules.cs (the creek pin's ways included), of the road PNGs, of the
// GATE'S OWN CODE (GATE_CODE: the replica, the checks, this file - review 5: a
// loosening edit to the gate left the inputs alone, so it passed its own
// ratchet and was never STALE), and the model. A ratchet against an entry
// whose inputs differ is STALE, and STALE FAILS (linecheck exits 1, the city
// audit counts it): the data or the gate moved (a re-export, a merge that
// brings new SPAN/XING rows, a threshold or a rule changed), so the keys
// cannot tell a regression from the move, and a gate that went quiet there
// would let the regression that arrives with the move straight through. The
// way out is the explicit re-record (--write-baseline), which prints the
// before and after numbers (beforeAfter) for the commit that moves the inputs
// - and REFUSES (loosenings) when the data did not move but keys vanished or
// scored lower, or a check state or the pin loosened: only a change to the
// gate can do that, and it is recorded only with --allow-loosen, its list in
// the commit. Each entry also records every check's STATE and the PINNED ways:
// a check that is looser now than when it was recorded (ZERO -> RATCHET ->
// REPORT), or a way dropped from the pin, fails, stale or not - nothing ever
// loosens.
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { gzipSync, gunzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { dirname, basename } from 'node:path';

export const SCHEMA = 3;
const r1 = v => Math.round(v * 10) / 10, r3 = v => Math.round(v * 1000) / 1000;

/// Per check: runs, metres, worst, DATA/BUILDER, report-only runs, every key
/// with its worst ratio and bad length, and runs per tile (the tile of the
/// run's worst sample, "tx,tz"). A0 TEXTURE counts the texture's faults.
export function summarize(gate, R) {
  const summary = {};
  for (const c of R.Checks) summary[c.id] = { id: c.id, name: c.name, state: c.state, runs: 0, metres: 0, worst: 0, worstRatio: 0, data: 0, builder: 0, reportOnlyRuns: 0, reportOnlyMetres: 0, keys: new Map(), tiles: new Map() };
  for (const r of gate.runs) {
    const s = summary[r.check]; if (!s) continue;
    if (r.reportOnly) { s.reportOnlyRuns++; s.reportOnlyMetres += r.len; continue; }
    s.runs++; s.metres += r.len;
    r.kk.forEach((k, i) => {
      const o = s.keys.get(k);
      if (!o) s.keys.set(k, { q: r.kq[i], l: r.kl[i] });
      else { if (r.kq[i] > o.q) o.q = r.kq[i]; o.l += r.kl[i]; }
    });
    s.tiles.set(r.tile, (s.tiles.get(r.tile) || 0) + 1);
    if (r.ratio > s.worstRatio) { s.worstRatio = r.ratio; s.worst = r.val; }
    if (r.data === 'DATA') s.data++; else s.builder++;
  }
  for (const f of gate.texFails || []) {
    const s = summary.A0; if (!s) break;
    s.runs++; s.builder++;
    s.keys.set(`tex:${f.key}_${f.surf}:${f.what}`, { q: f.ratio, l: 0 });
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
/// A bad length stored in LengthQuantumM steps, rounded up.
export const quantLen = (l, R) => l > 1e-9 ? Math.ceil(l / Math.fround(R.LengthQuantumM) - 1e-6) : 0;
function leb(bytes, v) { do { let b = v & 0x7f; v = Math.floor(v / 128); if (v) b |= 0x80; bytes.push(b); } while (v); }
function* unleb(raw) { let v = 0, mul = 1; for (const b of raw) { v += (b & 0x7f) * mul; if (b & 0x80) mul *= 128; else { yield v; v = 0; mul = 1; } } }

/// Schema 3: every key as (FNV-1a hash delta, quantised worst ratio,
/// quantised bad length) LEB128 triples sorted by hash, gzipped, base64.
export function packKeys(keys, R) {
  const byHash = new Map();
  for (const [k, o] of keys) {
    const h = fnv1a(k), q = quantRatio(o.q, R), l = quantLen(o.l, R), p = byHash.get(h);
    if (!p) byHash.set(h, [q, l]); else { p[0] = Math.max(p[0], q); p[1] += l; }
  }
  const hs = [...byHash.keys()].sort((a, b) => a - b);
  const bytes = [];
  let prev = 0;
  for (const h of hs) { const [q, l] = byHash.get(h); leb(bytes, h - prev); prev = h; leb(bytes, q); leb(bytes, l); }
  return { n: hs.length, b64: gzipSync(Buffer.from(bytes), { level: 9 }).toString('base64') };
}
export function unpackKeys(b64) {
  const it = unleb(gunzipSync(Buffer.from(b64, 'base64'))), map = new Map();
  let prev = 0;
  for (;;) { const d = it.next(); if (d.done) break; prev += d.value; map.set(prev, [it.next().value, it.next().value]); }
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

// ------------------------------------------------------------ the inputs a baseline was measured on
export const sha12 = data => createHash('sha256').update(data).digest('hex').slice(0, 12);
/// The container sections the replica reads (NAME: D1's same-road rule).
export const GEOMETRY_SECTIONS = ['NODE', 'NAME', 'EDGE', 'PNTS', 'SPAN', 'XING'];
/// SmoothRules members that decide no violation (the rollout switches and the
/// ranking): changing them leaves a baseline valid. The pin's ways are in the
/// digest: dropping one loosens the gate.
const NOT_MEASURED = new Set(['source', 'ReportOnly', 'PinActive', 'FastInAudit', 'WorstN', 'DedupM', 'ShotsN', 'ShotsDedupM', 'RefSpotReachM',
  'ExposureRoute', 'ExposureRefSpot', 'RankRatioCap', 'BandCount', 'ClassWeight', 'Checks']);
export function rulesDigest(R) {
  const o = {};
  for (const k of Object.keys(R).sort()) if (!NOT_MEASURED.has(k) && typeof R[k] !== 'function') o[k] = R[k];
  return sha12(JSON.stringify(o));
}
/// The gate's own code (tools/city/lib): the replica, the checks, the plan, the
/// texture scan, the rules parser and this ratchet. Any edit to them can move
/// a key, so it is an input like the data (a report-only edit to linecheck.mjs
/// is not).
export const GATE_CODE = ['citydata.mjs', 'gatebase.mjs', 'kink.mjs', 'linegate.mjs', 'linesim.mjs', 'paintiso.mjs', 'paintruns.mjs', 'png.mjs', 'smoothrules.mjs'];
/// Their digest (line ends normalised, so a checkout's CRLF does not move it).
export function codeDigest(libDir) {
  const h = createHash('sha256');
  for (const f of GATE_CODE) { h.update(f); h.update(readFileSync(`${libDir}/${f}`, 'utf8').replace(/\r\n/g, '\n')); }
  return h.digest('hex').slice(0, 12);
}
/// The inputs that are DATA (not the gate): what may legitimately make a key vanish.
const DATA_INPUTS = ['graph', 'sections', 'paint', 'dem', 'model', 'planTaper'];
/// The fingerprint: { graph, sections: {TAG: digest}, rules, paint, code, model, planTaper }.
export function inputsOf({ cityBuf, city, graph, R, paintFiles, model, planTaper, libDir }) {
  const sections = {};
  for (const tag of GEOMETRY_SECTIONS) { const s = city.sections && city.sections[tag]; if (s) sections[tag] = sha12(cityBuf.subarray(s[0], s[1])); }
  const paint = createHash('sha256');
  for (const f of [...paintFiles].sort()) { paint.update(basename(f)); paint.update(readFileSync(f)); }
  return { graph, sections, rules: rulesDigest(R), paint: paint.digest('hex').slice(0, 12), code: libDir ? codeDigest(libDir) : null, model, planTaper };
}
/// What differs between two fingerprints (empty: the same inputs).
export function inputsDiff(was, now) {
  if (!was) return ['the baseline records no inputs (an older schema)'];
  const d = [];
  if (was.graph !== now.graph) d.push(`graph ${was.graph} -> ${now.graph}`);
  for (const t of new Set([...Object.keys(was.sections || {}), ...Object.keys(now.sections || {})]))
    if ((was.sections || {})[t] !== (now.sections || {})[t]) d.push(`container section ${t}`);
  for (const k of ['rules', 'paint', 'code', 'dem', 'model', 'planTaper'])
    if ((was[k] ?? null) !== (now[k] ?? null)) d.push(k === 'rules' ? 'SmoothRules.cs geometry rules (or the pinned ways)' : k === 'paint' ? 'road PNGs' : k === 'code' ? "the gate's code (tools/city/lib)" : `${k} ${was[k]} -> ${now[k]}`);
  return d;
}
/// Did the DATA move between two fingerprints (not just the rules or the gate's code)?
export function dataMoved(was, now) {
  if (!was) return true;
  for (const k of DATA_INPUTS) if (JSON.stringify(was[k] ?? null) !== JSON.stringify(now[k] ?? null)) return true;
  return false;
}

export function readBaseline(path) {
  if (!existsSync(path)) return null;
  return JSON.parse(readFileSync(path, 'utf8'));
}
/// Record the entry `id` (the builder model: asbuilt, m0, ...) with its inputs.
export function writeBaseline(path, id, summary, R, inputs) {
  const B = readBaseline(path) || {};
  B.schema = SCHEMA; B.tool = 'tools/city/linecheck.mjs';
  B.note = 'per builder model: the inputs it was measured on (graph hash, container section, rules and paint digests); per-check runs, metres and worst; every violation key (way, round(s on way / 5 m), check, line) with its worst ratio and bad length, as FNV-1a hash deltas + ratio quanta (RatioQuantum) + length quanta (LengthQuantumM) in LEB128, gzipped; runs per tile';
  B.entries = B.entries || {};
  for (const k of Object.keys(B.entries)) if (B.entries[k].schema !== SCHEMA) delete B.entries[k];   // older schemas: re-record
  const checks = {};
  for (const [id2, s] of Object.entries(summary)) {
    const k = packKeys(s.keys, R);
    checks[id2] = { runs: s.runs, metres: r1(s.metres), worst: r3(s.worst), worstRatio: r3(s.worstRatio), keys: k.n, keys_b64: k.b64, tiles_b64: packTiles(s.tiles) };
  }
  B.entries[id] = { schema: SCHEMA, date: new Date().toISOString().slice(0, 10), V: R.V, inputs, states: statesOf(R), pinned: [...R.PinnedWays], keySides: 'bucket', checks };
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(B, null, 1) + '\n');
}

/// Every check's gating state, as an entry records it.
export const statesOf = R => Object.fromEntries(R.Checks.map(c => [c.id, c.state]));
const STRICT = { REPORT: 0, RATCHET: 1, ZERO: 2 };
/// The checks gating looser now than when the entry was recorded (ZERO ->
/// RATCHET -> REPORT, or a check gone), and ways dropped from the creek pin:
/// ['B2 RATCHET -> REPORT', 'pin: way 16671358 dropped', ...].
export function demotions(base, R) {
  if (!base || !base.states) return ['the baseline records no check states (it predates the demotion check): re-record it'];
  const now = statesOf(R), d = [];
  for (const [id, was] of Object.entries(base.states)) {
    if (!(id in now)) d.push(`${id} ${was} -> (removed)`);
    else if (STRICT[now[id]] < STRICT[was]) d.push(`${id} ${was} -> ${now[id]}`);
  }
  for (const w of base.pinned || []) if (!R.PinnedWays.includes(w)) d.push(`pin: way ${w} dropped`);
  return d;
}

/// What a re-record would LOOSEN (review 5: re-recording after a gate change
/// dropped 763 of the city's B2 keys, hidden inside a total that grew): check
/// states or pinned ways loosened (demotions); and, when the DATA did not move
/// - only the rules or the gate's code did - every key that vanished and every
/// key that now scores lower, per check. Only a change to the gate can do that.
/// Returns { dataMoved, lines } (lines empty: nothing loosens).
export function loosenings(summary, base, R, inputs) {
  const lines = [];
  if (!base || base.schema !== SCHEMA) return { dataMoved: true, lines };
  for (const d of demotions(base, R)) lines.push(`  ${d}`);
  const moved = dataMoved(base.inputs, inputs);
  if (!moved)
    for (const c of R.Checks) {
      const s = summary[c.id], b = base.checks[c.id];
      if (!s || !b) continue;
      const now = new Map(), put = (h, q) => { const p = now.get(h); now.set(h, p === undefined ? q : Math.max(p, q)); };
      for (const [k, o] of s.keys) {
        const q = quantRatio(o.q, R);
        put(fnv1a(k), q);
        // an entry recorded before ribbon-edge keys took the side of their bucket (keySides) labelled a whole run by its
        // worst sample's side: its key may be this bucket's with the other side, the same run - not a vanished key
        if (!base.keySides && /:R[LR]$/.test(k)) put(fnv1a(k.slice(0, -1) + (k.endsWith('L') ? 'R' : 'L')), q);
      }
      let gone = 0, lower = 0;
      for (const [h, [q]] of unpackKeys(b.keys_b64)) { const x = now.get(h); if (x === undefined) gone++; else if (x < q) lower++; }
      if (gone || lower) lines.push(`  ${c.id} ${c.name}: ${gone} keys vanished, ${lower} keys score lower (the data did not move: the gate loosened)`);
    }
  return { dataMoved: moved, lines };
}

/// The before and after numbers of a re-record (--write-baseline): per check
/// the replaced entry's runs, metres, worst and keys against this run's, and
/// what moved in the inputs. They go into the commit that re-records.
export function beforeAfter(summary, base, R, inputs) {
  const lines = [];
  if (!base || base.schema !== SCHEMA) { lines.push(`BEFORE: no schema-${SCHEMA} entry for this model (a first record)`); return lines; }
  const moved = inputsDiff(base.inputs, inputs), dem = base.states ? demotions(base, R) : [];
  lines.push(`BEFORE (${base.date}) -> AFTER (this run); inputs moved: ${moved.length ? moved.join('; ') : 'none'}${dem.length ? `; check states loosened: ${dem.join(', ')}` : ''}`);
  lines.push(`  check          runs before -> after        metres before -> after     worst x before -> after     keys before -> after`);
  const d = (x, y) => `${String(x).padStart(9)} -> ${String(y).padEnd(9)}`;
  for (const c of R.Checks) {
    const s = summary[c.id], b = base.checks[c.id] || { runs: 0, metres: 0, worstRatio: 0, keys: 0 };
    lines.push(`  ${(c.id + ' ' + c.name).padEnd(14)} ${d(b.runs, s.runs)}  ${d(Math.round(b.metres), Math.round(r1(s.metres)))}  ${d(r1(b.worstRatio), r1(s.worstRatio))}  ${d(b.keys, new Set([...s.keys.keys()].map(fnv1a)).size)}`);
  }
  return lines;
}

/// The ratchet (gate spec 1/8). Returns { ok, stale, zeroFail, demoted, lines }.
/// stale (a list of what moved) when the entry was measured on other inputs:
/// then ok is FALSE - a stale ratchet cannot tell a regression from the move,
/// so it FAILS until the explicit re-record (--write-baseline, with its before
/// and after numbers); the RATCHET checks are still compared, as 'moved', for
/// that commit. A ZERO check or the pin fails on its own (zeroFail: they need
/// no baseline), and so does a check gating looser than when the entry was
/// recorded (demoted). opts.inputs: this run's fingerprint.
/// opts.tiles (a Set of "tx,tz"): compare run counts only against the
/// baseline's runs in those tiles (a subset run, as FAST is on the meshes;
/// metres are then left to the keys). opts.pin: fail on any run on a pinned way.
export function ratchet(summary, base, R, opts = {}) {
  const lines = [];
  let ok = true, zeroOk = true;
  if (!base || base.schema !== SCHEMA) { lines.push(`  FAIL no ${base ? `schema-${SCHEMA} ` : ''}baseline for this model: record one with --write-baseline (a re-export re-records, with before/after numbers in the commit)`); return { ok: false, stale: null, zeroFail: false, demoted: null, lines }; }
  const stale = opts.inputs ? inputsDiff(base.inputs, opts.inputs) : [];
  const FAIL = stale.length ? '  moved' : '  FAIL';
  if (stale.length) lines.push(`  FAIL baseline STALE: it (${base.date}) was measured on other inputs - ${stale.join('; ')}. A stale ratchet cannot tell a regression from the move, so this FAILS until the explicit re-record (--write-baseline prints the before and after numbers for the commit); the comparison below is for that commit.`);
  const demoted = demotions(base, R);
  if (demoted.length) { ok = false; lines.push(`  FAIL check states never loosen: ${demoted.join(', ')}`); }
  const tileSet = opts.tiles ? new Set([...opts.tiles].map(t => { const [tx, tz] = t.split(',').map(Number); return tileId(tx, tz); })) : null;
  for (const c of R.Checks) {
    const s = summary[c.id], b = base.checks[c.id];
    if (c.state === 'REPORT') { lines.push(`  ok   ${c.id} ${c.name}: report-only (${s.runs} runs)`); continue; }
    if (c.state === 'ZERO') {
      const pass = s.runs === 0; ok = ok && pass; zeroOk = zeroOk && pass;
      lines.push(`${pass ? '  ok  ' : '  FAIL'} ${c.id} ${c.name}: ${s.runs} runs (ZERO)`); continue;
    }
    if (!b) { ok = false; lines.push(`${FAIL} ${c.id} ${c.name}: not in the baseline`); continue; }
    const old = unpackKeys(b.keys_b64);
    let fresh = 0, worseKeys = 0, longerKeys = 0, ex = null;
    for (const [k, o] of s.keys) {
      const bq = old.get(fnv1a(k));
      if (bq === undefined) { fresh++; ex = ex || `new ${k} x${r1(o.q)}`; continue; }
      if (o.q > unquant(bq[0], R) * (1 + 1e-9)) { worseKeys++; ex = ex || `worse ${k} x${r1(o.q)} (baseline x${r1(unquant(bq[0], R))})`; }
      if (quantLen(o.l, R) > bq[1]) { longerKeys++; ex = ex || `longer ${k} ${r1(o.l)} m (baseline ${r1(bq[1] * R.LengthQuantumM)} m)`; }
    }
    let baseRuns = b.runs;
    if (tileSet) { baseRuns = 0; for (const [t, n] of unpackTiles(b.tiles_b64)) if (tileSet.has(t)) baseRuns += n; }
    const worse = s.worstRatio > b.worstRatio + 1e-3, more = s.runs > baseRuns, moreM = !tileSet && s.metres > b.metres + 0.05 + 1e-6;
    const pass = !fresh && !worseKeys && !longerKeys && !worse && !more && !moreM; ok = ok && pass;
    lines.push(`${pass ? '  ok  ' : FAIL} ${c.id} ${c.name}: ${s.runs} runs (baseline ${baseRuns}${tileSet ? ' in these tiles' : ''})${more ? ' MORE' : ''}, ${r1(s.metres)} m (baseline ${b.metres})${moreM ? ' LONGER' : ''}, worst x${r1(s.worstRatio)} (baseline x${r1(b.worstRatio)})${worse ? ' WORSE' : ''}, ${fresh} new / ${worseKeys} worse / ${longerKeys} longer keys of ${s.keys.size}${ex ? `; e.g. ${ex}` : ''}`);
  }
  if (opts.pin) {
    const n = (opts.runs || []).filter(r => r.pinned && !r.reportOnly).length;
    ok = ok && n === 0; zeroOk = zeroOk && n === 0;
    lines.push(`${n === 0 ? '  ok  ' : '  FAIL'} creek pin: ${n} runs on ways ${R.PinnedWays.join(', ')}`);
  }
  if (stale.length) ok = false;
  return { ok, stale: stale.length ? stale : null, zeroFail: !zeroOk, demoted: demoted.length ? demoted : null, lines };
}
