// linecheck.mjs - THE SMOOTHNESS GATE, offline (plan amendment A4, WP-G;
// gate spec scratchpad linewobble/gate_spec.md). The owner's rule of
// 2026-09-28: "I circled some concerning squiggly road lines. This should
// never happen. Nor should any sharp angles of road or road lines."
//
// It replays the tile builder's plan-view geometry from the shipped graph
// (lib/linesim.mjs), extracts every painted line per TRIANGLE (iso-U, so a
// diagonal bulge is sampled at its peak - plan A7), every ribbon edge and the
// ribbon midline, and checks them against a plan built from data only
// (lib/linegate.mjs), with V = 2.5 cm and every threshold from
// Assets/PSXRacing/Editor/SmoothRules.cs. Editor/CitySmooth.cs is the same
// gate on the BUILT meshes: the two must agree, and a disagreement is a gate
// bug (Docs/CHARLOTTE.md, "Smoothness gate").
//
// It also keeps the 2026-09-28 CENSUS (lib/linecensus.mjs): the same replica
// measured the census's way (20 m p2p wobble, displaced paint, 2 degree
// kinks), so its headline numbers are reproduced on any graph and model.
//
//   node tools/city/linecheck.mjs                         census + gate, as built, report to stdout
//   node tools/city/linecheck.mjs --model m0              the M0 stopgap (U against the nominal half
//                                                         width + smoothstep taper, 2 m sections)
//   --model asbuilt|nominalU|m0|dense2                    nominalU / dense2: the census's two variants
//   --data <dir>                                          measure an --out export instead of Resources
//   --out <dir>                                           write linecheck.txt + linecheck.json there
//   --csv <file>                                          every violation run, one row each
//   --write-baseline [file]                               record this model's numbers, keys and inputs
//   --ratchet [file]                                      compare with them (lib/gatebase.mjs): exit 1 on
//                                                         any new key, a key worse or longer than its
//                                                         baseline, more runs or metres, a worse worst
//                                                         case; exit 3 (STALE, not FAIL) when the baseline
//                                                         was measured on other inputs (graph, container
//                                                         sections, rules, road PNGs, model): re-record
//   --pin                                                 enforce the creek pin now (SmoothRules.PinActive)
//   --plan-taper linear|smooth                            the plan's design taper (default: SmoothRules
//                                                         PlanTaperShape; --model m0 implies smooth)
//   --no-census / --no-gate                               skip a half
//   --edges 12887,549                                     only the chains through these edges (debugging)
// Baseline file default: tools/city/baseline/linecheck_baseline.json.
// Regression probes (synthetic cities with known answers): node tools/city/gateprobes.mjs
// About 1.5-3 minutes for the whole city (the census alone ~15 s of it).

import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseCity, hashHex } from './lib/citydata.mjs';
import { createSim, MODELS } from './lib/linesim.mjs';
import { runCensus } from './lib/linecensus.mjs';
import { runGate } from './lib/linegate.mjs';
import { readSmoothRules } from './lib/smoothrules.mjs';
import { loadPaintLayouts, SURFACES } from './lib/paintruns.mjs';
import { summarize, readBaseline, writeBaseline, ratchet, inputsOf } from './lib/gatebase.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const ARGS = process.argv.slice(2);
const has = n => ARGS.includes(n);
const argVal = (n, dflt = null) => { const i = ARGS.indexOf(n); if (i < 0) return dflt; const v = ARGS[i + 1]; return v && !v.startsWith('--') ? v : dflt; };
const MODEL = argVal('--model', 'asbuilt');
if (!MODELS[MODEL]) { console.error(`--model: one of ${Object.keys(MODELS).join(', ')}`); process.exit(2); }
const DATA = resolve(UNITY, argVal('--data') || 'Assets/PSXRacing/Resources');
const ART = join(UNITY, 'Assets/PSXRacing/Art/City');
const RULES_CS = join(UNITY, 'Assets/PSXRacing/Editor/SmoothRules.cs');
const REFSPOTS_CS = join(UNITY, 'Assets/PSXRacing/Editor/CityRefSpots.cs');
const BASELINE = resolve(UNITY, argVal('--write-baseline') || argVal('--ratchet') || 'tools/city/baseline/linecheck_baseline.json');

const LAT0 = 35.18456015184093, LON0 = -80.81770185962013;
const M_LAT = 111132, M_LON = 111320 * Math.cos(LAT0 * Math.PI / 180);
const ll = (x, z) => `${(LAT0 + z / M_LAT).toFixed(6)},${(LON0 + x / M_LON).toFixed(6)}`;
const r1 = v => Math.round(v * 10) / 10, r2 = v => Math.round(v * 100) / 100, r3 = v => Math.round(v * 1000) / 1000;

const t0 = Date.now();
const secs = () => ((Date.now() - t0) / 1000).toFixed(1);
const out = [];
const P = s => out.push(s);
const log = m => { if (has('--verbose')) console.error(`[${secs()}s] ${m}`); };

// ------------------------------------------------------------------ data
const cityBuf = readFileSync(join(DATA, 'charlotte_city.bytes'));
const city = parseCity(cityBuf);
const graph = hashHex(city.graphHash);
// TAPR / PARA (plan A4 WP-10): the exporter's lane-count transitions and
// parallel-carriageway fixes. Not written yet; when a container carries them
// the plan must read them - refuse to run silently on the old taper table.
const extraSections = city.version === 2 ? readSectionTags(cityBuf) : [];
function readSectionTags(buf) {
  const n = buf.readUInt32LE(8), tags = [];
  for (let i = 0; i < n; i++) tags.push(buf.toString('latin1', 12 + i * 12, 16 + i * 12));
  return tags;
}
const R = readSmoothRules(RULES_CS);
const planTaper = argVal('--plan-taper') || (MODEL === 'm0' ? 'smooth' : R.PlanTaperShape === 1 ? 'smooth' : 'linear');
const pinActive = R.PinActive || has('--pin');
const refSpots = [];
if (existsSync(REFSPOTS_CS))
  for (const m of readFileSync(REFSPOTS_CS, 'utf8').matchAll(/new Spot\("([^"]+)", Kind\.\w+, (-?[0-9.]+), (-?[0-9.]+),/g))
    refSpots.push([(parseFloat(m[3]) - LON0) * M_LON, (parseFloat(m[2]) - LAT0) * M_LAT]);

P(`LINECHECK  the smoothness gate, offline (linecheck.mjs; the mesh gate is Editor/CitySmooth.cs)`);
P(`  V ${(R.V * 100).toFixed(1)} cm (1 px at ${R.PixelAtM} m; ${R.FramebufferLines} lines, fov ${R.FovDeg})  model ${MODEL}  plan taper ${planTaper}  graph ${graph}  PSXC v${city.version}`);
P(`  rules ${RULES_CS.replace(/\\/g, '/').replace(UNITY.replace(/\\/g, '/') + '/', '')}; ${R.ReportOnly ? 'REPORT-ONLY cycle' : 'gating'}; creek pin ${pinActive ? 'ACTIVE' : 'not yet active (flips with the creek fix)'}`);
const unknown = extraSections.filter(t => !['META', 'NODE', 'NAME', 'EDGE', 'PNTS', 'WATR', 'XING', 'SPAN', 'ROUT', 'GHSH'].includes(t));
if (unknown.includes('TAPR') || unknown.includes('PARA'))
  P(`  NOTE the container carries ${unknown.filter(t => t === 'TAPR' || t === 'PARA').join(' + ')}: the plan must read them (WP-10); until lib/linegate.mjs does, the taper TABLE is the builder's ComputeTrims replica`);

const S = createSim(city, MODEL);
for (const e of S.E) e.secs = S.sectionsOf(e);
log('sections');

// ------------------------------------------------------------------ the census
const CENSUS_2026_09_28 = {   // scratchpad linewobble/census_asis.json and census_nominalU.json (graph 27bccd93)
  asbuilt: { sections: 575216, wobble_km_ge5cm: 264.88, wobble_km_ge15cm: 242.2, wobble_km_ge30cm: 216.49, displaced_km_gt30cm: 356.18,
             zigzag_km_ge15cm: 106.18, kinks_gt2deg: 75073, kinks_ge5deg: 33192, kinks_ge10deg: 11330, kinks_ge25deg: 2149,
             taper_edges: 5774, taper_km: 215.66, diamonds: 1224, turn_lane_tapers: 4656, jump20: 6360, overlap_km: 13.21, folds_10cm: 192,
             flicker: 3397, squeeze_wander15_km: 16.17, edge_line_km: 7304.2, creek_zig_m: 0.34, creek_p2p_m: 0.94, creek_dash_m: 1.93 },
  nominalU: { sections: 575216, wobble_km_ge5cm: 17.47, wobble_km_ge15cm: 2.4, wobble_km_ge30cm: 1.18, displaced_km_gt30cm: 0.38,
              zigzag_km_ge15cm: 0.37, kinks_gt2deg: 75073, jump20: 7586, edge_line_km: 6972.6, creek_zig_m: 0, creek_p2p_m: 0, creek_dash_m: 0 },
};
let census = null;
if (!has('--no-census')) {
  census = runCensus(S, { cacheDir: join(HERE, 'cache'), log });
  const t = census.totals, ref = CENSUS_2026_09_28[MODEL === 'm0' ? 'nominalU' : MODEL];
  const got = {
    sections: census.sections, wobble_km_ge5cm: t.wobble_km_ge5cm, wobble_km_ge15cm: t.wobble_km_ge15cm, wobble_km_ge30cm: t.wobble_km_ge30cm,
    displaced_km_gt30cm: t.displaced_km_gt30cm, zigzag_km_ge15cm: t.zigzag_km_ge15cm, kinks_gt2deg: census.kinkCount,
    kinks_ge5deg: census.kinkGe5, kinks_ge10deg: census.kinkGe10, kinks_ge25deg: census.kinkGe25,
    taper_edges: census.taperStats.edges, taper_km: census.taperStats.km, diamonds: census.taperStats.diamonds, turn_lane_tapers: census.taperStats.withTurnLanes,
    jump20: census.nodeStats.jump20, overlap_km: census.overlapKm, folds_10cm: census.folds.gt10cm, flicker: t.edge_line_flicker,
    squeeze_wander15_km: t.squeeze_edge_wobble_km_ge15, edge_line_km: census.paintKm.edge,
    creek_zig_m: r2(census.creek.e12887?.zig ?? 0), creek_p2p_m: r2(census.creek.e12887?.wMax ?? 0), creek_dash_m: r2(census.creek.e12887?.disp ?? 0),
  };
  census.headline = got;
  P('');
  P(`CENSUS (the 2026-09-28 line-wobble census, measured its own way: nominal paint positions, 20 m p2p, 2 degree kinks)`);
  if (MODEL === 'm0') P(`  compared with the census's "U against nominal" run - the census measured M0's U change only, without the smoothstep taper and 2 m sections`);
  const row = (k, label, unit = '') => {
    const v = got[k], c = ref ? ref[k] : undefined;
    const tag = c === undefined ? '' : Math.abs(v - c) <= Math.max(0.011, Math.abs(c) * 1e-4) ? `  = census ${c}` : `  census ${c} (${v - c >= 0 ? '+' : ''}${r2(v - c)})`;
    P(`  ${label.padEnd(58)} ${String(v).padStart(9)}${unit}${tag}`);
  };
  row('sections', 'cross-sections');
  row('wobble_km_ge5cm', 'paint moving sideways >= 5 cm within 20 m (km)');
  row('wobble_km_ge15cm', '  >= 15 cm (km)');
  row('wobble_km_ge30cm', '  >= 30 cm (km)');
  row('displaced_km_gt30cm', 'lane/centre paint > 30 cm off its place (km of line)');
  row('zigzag_km_ge15cm', 'zigzag at a quad diagonal >= 15 cm (km)');
  row('kinks_gt2deg', 'drawn-edge kinks > 2 degrees');
  row('kinks_ge5deg', '  >= 5 degrees'); row('kinks_ge10deg', '  >= 10 degrees'); row('kinks_ge25deg', '  >= 25 degrees');
  row('taper_edges', 'edges carrying a symmetric taper'); row('taper_km', '  km of ribbon inside a taper');
  row('diamonds', '  tapering from both ends, never full width'); row('turn_lane_tapers', '  with turn:lanes tags (turn bays)');
  row('jump20', 'mitred nodes whose lines jump > 20 cm');
  row('overlap_km', 'paint inside another ribbon at the same level (km)');
  row('folds_10cm', 'quads folding back > 10 cm'); row('flicker', 'edge line cropped on/off (transitions)');
  row('squeeze_wander15_km', 'squeezed edge wandering >= 15 cm (km)'); row('edge_line_km', 'edge line drawn (km)');
  row('creek_zig_m', 'creek e12887: zigzag at the diagonals (m)'); row('creek_p2p_m', 'creek e12887: worst 20 m p2p (m)'); row('creek_dash_m', 'creek e12887: lane dash off its line (m)');
  P(`  wobble >= 5 cm by cause (km): ${Object.entries(census.wobbleCauseKm).map(([k, v]) => `${k} ${v}`).join(', ')}; kink causes: ${Object.entries(census.kinkCauses).map(([k, v]) => `${k} ${v}`).join(', ')}`);
  log('census');
}

// ------------------------------------------------------------------ the gate
let gate = null, summary = null, verdict = null;
if (!has('--no-gate')) {
  const keys = [...new Set(S.E.map(e => e.profile.key))];
  const { layouts, notes } = loadPaintLayouts(ART, keys);
  const only = argVal('--edges') ? new Set(argVal('--edges').split(',').map(Number)) : null;
  gate = runGate(S, R, layouts, { planTaperShape: planTaper, refSpots, onlyEdges: only, log });
  const runs = gate.runs;
  // ---- what this run measured: the baseline's fingerprint
  const paintFiles = [];
  for (const k of keys) for (const surf of SURFACES) { const f = join(ART, `city_road_${k}_${surf}.png`); if (existsSync(f)) paintFiles.push(f); }
  const inputs = inputsOf({ cityBuf, city, graph, R, paintFiles, model: MODEL, planTaper });
  // ---- the per-check table: runs, metres, worst; every key with its worst ratio; runs per tile (lib/gatebase.mjs)
  summary = summarize(gate, R);
  const unit = (id, v) => id === 'A4' ? `${(v * 100).toFixed(0)}%` : id === 'B3' ? `R ${r1(v)} m` : id === 'C2' ? `${r1(v)}x` : id === 'C3' ? `${(v * 100).toFixed(0)}%`
    : Math.abs(v) >= 1 ? `${r2(Math.abs(v))} m` : `${(Math.abs(v) * 100).toFixed(1)} cm`;
  // ---- baseline / ratchet
  const base = readBaseline(BASELINE)?.entries?.[MODEL];
  if (has('--ratchet')) verdict = ratchet(summary, base, R, { pin: pinActive, runs, inputs });
  P('');
  P(`SMOOTHNESS GATE  ${gate.stats.edges} ribbons, ${r1(gate.stats.ribbonKm)} km of ribbon, ${r1(gate.stats.lineKm)} km of painted line, ${gate.chains} chains, ${gate.stats.strands} strands`);
  P(`  bend fans (2-arm nodes drawn as junction slabs; plan A2: never legitimate): ${gate.stats.bendFans} (${gate.stats.bendFans60} turning 60 degrees or more) - each is judged across (B2/B3) and its mouths are no legitimate end (C2)`);
  P(`  check          state    runs     metres   worst        x V/limit  data/builder   baseline`);
  for (const c of R.Checks) {
    const s = summary[c.id];
    const b = base?.checks?.[c.id];
    const bl = b ? `${b.runs} runs, worst ${unit(c.id, b.worst)}` : '-';
    P(`  ${(c.id + ' ' + c.name).padEnd(14)} ${c.state.padEnd(8)} ${String(s.runs).padStart(6)} ${String(Math.round(s.metres)).padStart(10)}   ${unit(c.id, s.worst).padEnd(12)} ${r1(s.worstRatio).toString().padStart(6)}   ${`${s.data}/${s.builder}`.padEnd(14)} ${bl}`);
    if (s.reportOnlyRuns) P(`  ${''.padEnd(14)} ${'REPORT'.padEnd(8)} ${String(s.reportOnlyRuns).padStart(6)} ${String(Math.round(s.reportOnlyMetres)).padStart(10)}   ${c.id === 'D1' ? 'inside a branch attach arc (its clip range + MergeMarginM; report-only until WP-18b)' : 'truncated dash stubs at mouths and gores (report-only until WP-17)'}`);
  }
  P(`  keys: one per ${R.KeyStepM} m bucket a run's bad samples touch, each with its worst ratio and bad length (${[...Object.values(summary)].reduce((a, s) => a + s.keys.size, 0)} in all)`);
  P(`  not measurable offline: B4s SEAM (tile seams: one city-wide replica cuts every span once), B2/B3 on FAN perimeters and D1 into fans (the replica builds no fans; across a bend fan it bridges the ribbon edges straight, the mesh gate along the slab's perimeter), heights (D1's same-level test is the census's plan approximation), E1 (no strip paint yet)`);
  P(`  texel quantisation per profile (texture run centre vs RoadProfiles; subtracted from A1-A3/A5/C2 up to half a texel, A0 fails past it): ${Object.entries(gate.texQ).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k} ${(v * 100).toFixed(1)}/${(gate.texCap[k] * 100).toFixed(1)}`).join(', ')} cm`);
  if (gate.texNotes.length) for (const n of gate.texNotes.slice(0, 10)) P(`  TEXTURE ${n}`);
  if (notes.length) for (const n of notes.slice(0, 10)) P(`  PNG ${n}`);

  // ---- the worst N, de-duplicated per (way, check, line) within DedupM
  const ranked = runs.filter(r => !r.reportOnly && R.check(r.check)?.state !== 'REPORT').sort((a, b) => (b.score - a.score) || (b.ratio - a.ratio));
  const worst = [];
  const keptBy = new Map();
  for (const r of ranked) {
    const k = `${r.way}:${r.check}:${r.lineId}`;
    const l = keptBy.get(k) || [];
    if (l.some(q => Math.hypot(q.x - r.x, q.z - r.z) < R.DedupM)) continue;
    l.push(r); keptBy.set(k, l);
    worst.push(r);
    if (worst.length >= R.WorstN) break;
  }
  const describe = id => {
    if (id === 'EL') return 'edge line, left of travel';
    if (id === 'ER') return 'edge line, right of travel';
    if (id === 'RL') return 'ribbon edge, right of travel';
    if (id === 'RR') return 'ribbon edge, left of travel';
    if (id === 'MID') return 'ribbon midline';
    if (id === 'CPAIR') return 'centre pair (double yellow / TWLTL)';
    const m = id.match(/^C([YW])([sd])([+-][0-9.]+)$/);
    if (m) return `${m[1] === 'Y' ? 'yellow centre' : 'white lane'} line (${m[2] === 'd' ? 'dash' : 'solid'}, ${m[3]} m)`;
    return id;
  };
  P('');
  P(`WORST ${worst.length} (ratio capped at ${R.RankRatioCap} x class weight x exposure; gated checks only; one per way, check and line within ${R.DedupM} m)`);
  worst.forEach((r, i) => {
    const e = S.E[r.e];
    const elev = S.elevatedAt(e, r.s);
    const nb = r.s < 1 ? e.a : e.length - r.s < 1 ? e.b : -1;
    const nbTxt = nb >= 0 ? `  node ${nb} -> ${S.nodeEdges[nb].filter(o => o !== e.index).map(o => `e${o} ${S.E[o].profile.key}`).join(', ') || 'dead end'}` : '';
    P(` #${String(i + 1).padEnd(2)} ${r.check} ${(R.check(r.check)?.name || '').padEnd(9)} ${unit(r.check, r.val).padEnd(9)} (${r1(r.ratio)}x)  ${describe(r.lineId)}  e${r.e} '${e.name || '(unnamed)'}' way ${e.wayId}`);
    P(`      ${e.klass} ${e.profile.key} ${elev ? 'deck' : 'ground'}  s ${r1(r.s)}/${r1(e.length)}  run ${r1(r.len)} m  game (${r1(r.x)}, ${r1(r.z)})  ${ll(r.x, r.z)}  tile (${Math.floor(r.x / 256)},${Math.floor(r.z / 256)})${nbTxt}`);
    P(`      cause ${r.cause}; ${r.data}${r.what ? '; ' + r.what : ''}${r.exposure > 1 ? '; exposure ' + r.exposure : ''}`);
    P(`      spot ${r1(r.x)},${r1(r.z)},${r.e},${r.lineId}`);
  });
  // ---- the creek pin
  const pinRuns = runs.filter(r => r.pinned && !r.reportOnly);
  const pinBy = {};
  for (const r of pinRuns) { const b = pinBy[r.check] ||= { n: 0, worst: 0, val: 0 }; b.n++; if (r.ratio > b.worst) { b.worst = r.ratio; b.val = r.val; } }
  P('');
  P(`PINNED  the creek (ways ${R.PinnedWays.join(', ')}; the owner's circled creek_high frame) - ${pinActive ? 'ACTIVE: any run fails' : 'recorded, not yet enforced'}`);
  P(`  ${pinRuns.length ? Object.entries(pinBy).map(([k, v]) => `${k} ${v.n} (worst ${unit(k, v.val)})`).join(', ') : 'no violations: 0.000 on every check'}`);
  if (verdict) {
    P('');
    P(`RATCHET against ${BASELINE.replace(/\\/g, '/')} [${MODEL}]: ${verdict.ok ? 'PASS' : verdict.stale && !verdict.zeroFail ? 'STALE (re-record; not a verdict)' : 'FAIL'}${R.ReportOnly ? ' (SmoothRules.ReportOnly: the city audit would only report this)' : ''}`);
    for (const l of verdict.lines) P('  ' + l);
  }
  gate.worst = worst;
  gate.pin = { active: pinActive, runs: pinRuns.length, byCheck: pinBy };
  if (has('--write-baseline')) {
    writeBaseline(BASELINE, MODEL, summary, R, inputs);
    P(`\nwrote the baseline for ${MODEL} (graph ${graph}, inputs ${JSON.stringify(inputs)}) to ${BASELINE.replace(/\\/g, '/')}`);
  }
}
P(`\n(${secs()} s, data ${DATA.replace(/\\/g, '/')})`);
console.log(out.join('\n'));

// ------------------------------------------------------------------ files
const outDir = argVal('--out');
if (outDir) {
  const dir = resolve(UNITY, outDir);
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, 'linecheck.txt'), out.join('\n') + '\n');
  const J = { schema: 1, graph, model: MODEL, planTaper, V: R.V, census: census && { headline: census.headline, totals: census.totals, taperStats: census.taperStats, kinkCauses: census.kinkCauses, nodeStats: census.nodeStats, overlapM: census.overlapM, folds: census.folds, creek: census.creek, routes: census.routes },
    gate: gate && { stats: gate.stats, texQ: gate.texQ, texCap: gate.texCap, texFails: gate.texFails, checks: Object.fromEntries(Object.entries(summary).map(([k, s]) => [k, { ...s, keys: s.keys.size, tiles: s.tiles.size, metres: r1(s.metres) }])),
      worst: gate.worst.map(r => pick(r)), pin: gate.pin }, ratchet: verdict };
  writeFileSync(join(dir, 'linecheck.json'), JSON.stringify(J, null, 1) + '\n');
  console.log(`wrote ${join(dir, 'linecheck.txt')} and linecheck.json`);
}
const csvPath = argVal('--csv');
if (csvPath && gate) {
  const cols = ['check', 'lineId', 'key', 'keys', 'way', 'e', 'name', 'cls', 'profile', 's', 's0', 's1', 'len', 'val', 'ratio', 'x', 'z', 'cause', 'data', 'pinned', 'reportOnly', 'what'];
  const lines = [cols.join(',')];
  for (const r of gate.runs) lines.push(cols.map(c => { const v = c === 'keys' ? r.kk.length : r[c]; if (v === undefined || v === null) return ''; if (typeof v === 'number') return String(r3(v)); const s = String(v); return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; }).join(','));
  writeFileSync(resolve(UNITY, csvPath), lines.join('\n') + '\n');
  console.log(`wrote ${gate.runs.length} runs to ${csvPath}`);
}
process.exit(verdict && !verdict.ok ? (verdict.stale && !verdict.zeroFail ? 3 : 1) : 0);

// ------------------------------------------------------------------ baseline helpers
function pick(r) {
  const o = {};
  for (const k of ['check', 'lineId', 'key', 'way', 'e', 'name', 'cls', 'profile', 's', 's0', 's1', 'len', 'val', 'ratio', 'score', 'x', 'z', 'cause', 'data', 'pinned', 'what'])
    if (r[k] !== undefined) o[k] = typeof r[k] === 'number' ? r3(r[k]) : r[k];
  o.latlon = ll(r.x, r.z);
  return o;
}
