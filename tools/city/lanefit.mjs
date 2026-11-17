// lanefit.mjs - THE LANE FIT LIST (the road-lines round, 2026-11-16).
//
// The owner on North Tryon Street (PC48): the double yellow S-jogs, a lane 8 m wide. Cause: OSM tags SEVEN
// lanes there (3 + a centre two-way left-turn lane + 3; then a left bay in the centre lane), the widest
// two-way profile had six, and the exporter's line set (lib/lineset.mjs twoWaySplit, "fitted") squeezed the
// bay in by taking an opposing through lane, piece by piece - the line model then relayed the centre line a
// lane sideways at every piece's end.
//
// This tool reads the raw OSM ways the exporter read (the gitignored cache; PSX_CITY_CACHE overrides) and
// writes Assets/PSXRacing/Scripts/City/LineModel.LaneFitList.cs: every two-way way whose tags carry seven
// lanes or more, with its TRUE lanes (forward, backward, both_ways) and turn-only lanes, plus the short
// six-lane pieces lying between two of them (the mapper left the centre lane out: lent 3 + TWLTL + 3). The
// game gives those ways the seven-lane profile (RoadProfiles.Tw7) and their own line set at load
// (LineModel.ApplyLaneFit, PSX_CITY_LINEFIX). Keyed by OSM way id: no export, the graph is untouched.
//
//   node tools/city/lanefit.mjs            write the list (and print it)
//   node tools/city/lanefit.mjs --check    print only
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadRaw } from './splitcensus/osmraw.mjs';
import { load } from './splitcensus/load.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const OUT = path.join(ROOT, 'Assets', 'PSXRacing', 'Scripts', 'City', 'LineModel.LaneFitList.cs');
const LENT_MAX_M = 200;

const int = v => { const n = parseInt(v, 10); return Number.isFinite(n) ? n : NaN; };
// bit i = lane i (OSM order: left to right in travel) turns and never goes through (lineset.mjs turnOnlyBits)
function turnOnlyBits(tl) {
  if (!tl) return 0;
  let bits = 0;
  tl.split('|').forEach((lane, i) => {
    if (i > 7) return;
    const v = lane.split(';').map(x => x.trim());
    if (v.some(x => /^(slight_|sharp_)?(left|right)$/.test(x)) && !v.includes('through')) bits |= 1 << i;
  });
  return bits;
}
const lenOf = w => { let L = 0; for (let i = 1; i < w.pts.length; i++) L += Math.hypot(w.pts[i][0] - w.pts[i - 1][0], w.pts[i][1] - w.pts[i - 1][1]); return L; };
const twoWay = t => t.highway && !/_link$/.test(t.highway) && t.oneway !== 'yes' && t.oneway !== '-1' && t.oneway !== '1' && t.junction !== 'roundabout' && /^(trunk|primary|secondary|tertiary|unclassified|residential)$/.test(t.highway);

const ways = loadRaw();
const rows = new Map();          // way id -> { lf, lb, both, bf, bb, why, name, len }
const byNode = new Map();        // end node -> ways
for (const w of ways.values()) {
  if (!w.tags.highway) continue;
  for (const n of [w.nodes[0], w.nodes[w.nodes.length - 1]]) { if (!byNode.has(n)) byNode.set(n, []); byNode.get(n).push(w); }
}
// (A) seven lanes or more by their own tags
for (const w of ways.values()) {
  const t = w.tags;
  if (!twoWay(t)) continue;
  const lf = int(t['lanes:forward']), lb = int(t['lanes:backward']);
  const both = int(t['lanes:both_ways']) || (t.centre_turn_lane === 'yes' || t['turn:lanes:both_ways'] ? 1 : 0);
  if (!(lf >= 1 && lb >= 1) || lf + lb + both < 7) continue;
  rows.set(w.id, { lf, lb, both: both ? 1 : 0, bf: turnOnlyBits(t['turn:lanes:forward']), bb: turnOnlyBits(t['turn:lanes:backward']), why: 'tags', name: t.name || t.ref || '', len: lenOf(w) });
}
// (B) a six-lane piece between two seven-lane neighbours of the same road (a listed way, or the pair of
// one-way carriageways the road splits into when their lanes add to seven or more): lent 3 + TWLTL + 3
const sevenAt = (w, node) => {
  const others = (byNode.get(node) || []).filter(o => o !== w && (o.tags.name || o.tags.ref) === (w.tags.name || w.tags.ref));
  if (others.some(o => rows.has(o.id))) return true;
  const ow = others.filter(o => o.tags.oneway === 'yes' || o.tags.oneway === '-1');
  if (ow.length >= 2) { let s = 0; for (const o of ow) s += int(o.tags.lanes) || 0; if (s >= 7) return true; }
  return false;
};
// (B2, 11-17) ...or between a seven-lane neighbour and a six-lane piece whose centre lane is ONE direction's left
// bay (2 + 4 with the four's inner lane left-only): the centre lane runs on through the six-lane piece (painted
// hatching or a raised nose where nobody turns: North Tryon Street west of Dorton Street, viewed on the aerial -
// seven lanes wide, the through lanes straight), and the mapper counted only the lanes that carry traffic. Drawn
// on six lanes, the piece's centre line had to relay a whole lane sideways between its two neighbours (11:1 at
// 45 mph, right behind the owner's pose).
const centreBayAt = (w, node) => {
  const others = (byNode.get(node) || []).filter(o => o !== w && (o.tags.name || o.tags.ref) === (w.tags.name || w.tags.ref) && twoWay(o.tags));
  return others.some(o => {
    const t = o.tags, f = int(t['lanes:forward']), b = int(t['lanes:backward']);
    if (!(f >= 1 && b >= 1) || f + b !== 6 || Math.abs(f - b) !== 2) return false;
    const big = f > b ? t['turn:lanes:forward'] : t['turn:lanes:backward'];
    return (turnOnlyBits(big) & 1) !== 0 && /^(slight_)?left/.test((big || '').split('|')[0].trim());
  });
};
for (let pass = 0; pass < 4; pass++) {
  let added = 0;
  for (const w of ways.values()) {
    const t = w.tags;
    if (rows.has(w.id) || !twoWay(t) || int(t.lanes) !== 6) continue;
    const lf = int(t['lanes:forward']), lb = int(t['lanes:backward']);
    if ((Number.isFinite(lf) || Number.isFinite(lb)) && !(lf === 3 && lb === 3)) continue;
    const L = lenOf(w);
    if (L > LENT_MAX_M) continue;
    const n0 = w.nodes[0], n1 = w.nodes[w.nodes.length - 1];
    const s0 = sevenAt(w, n0), s1 = sevenAt(w, n1);
    let why = 'lent';
    if (!s0 || !s1) {
      if (!((s0 && centreBayAt(w, n1)) || (s1 && centreBayAt(w, n0)))) continue;
      why = 'lent-bay';
    }
    rows.set(w.id, { lf: 3, lb: 3, both: 1, bf: turnOnlyBits(t['turn:lanes:forward']), bb: turnOnlyBits(t['turn:lanes:backward']), why, name: t.name || t.ref || '', len: L });
    added++;
  }
  if (!added) break;
}

// (C) a tagged centre two-way left-turn lane (lanes:both_ways, centre_turn_lane, turn:lanes:both_ways) that
// the exported row draws as a double yellow, where the lanes add to three or five - the game has those
// profiles (1 + TWLTL + 1, 2 + TWLTL + 2) and the exporter did not take them (Parkwood Avenue: lanes=2 +
// centre_turn_lane=yes drawn as a plain two-lane street). Four lanes with a centre lane (1 + TWLTL + 2)
// have no profile: listed in the print, not in the table.
const noProfile = [];
{
  const city = load();
  const seen = new Set();
  for (const e of city.edges) {
    if (e.oneway || e.link || e.a === e.b || !e.lset || !(e.lset.flags & 1) || e.lset.centre === 2) continue;
    const w = ways.get(e.wayId); if (!w || rows.has(w.id) || seen.has(w.id)) continue;
    const t = w.tags;
    if (!twoWay(t)) continue;
    const both = int(t['lanes:both_ways']) > 0 || t.centre_turn_lane === 'yes' || !!t['turn:lanes:both_ways'];
    if (!both) continue;
    seen.add(w.id);
    let lf = int(t['lanes:forward']), lb = int(t['lanes:backward']);
    const L = int(t.lanes);
    if (!(lf >= 1 && lb >= 1)) {
      // no split tagged: the lanes shared evenly either side of the centre lane (lanes counts it, or not)
      if (L === 2 || L === 3) lf = lb = 1; else if (L === 4 || L === 5) lf = lb = 2; else continue;
    }
    const total = lf + lb + 1;
    if (total !== 3 && total !== 5) { noProfile.push(`way ${w.id} '${t.name || t.ref || ''}' ${lenOf(w).toFixed(0)} m: ${lf} + TWLTL + ${lb}`); continue; }
    rows.set(w.id, { lf, lb, both: 1, bf: turnOnlyBits(t['turn:lanes:forward']), bb: turnOnlyBits(t['turn:lanes:backward']), why: 'twltl', name: t.name || t.ref || '', len: lenOf(w) });
  }
}

const list = [...rows.entries()].sort((a, b) => (a[1].name < b[1].name ? -1 : a[1].name > b[1].name ? 1 : a[0] - b[0]));
let km = 0; for (const [, r] of list) km += r.len / 1000;
const byRoad = new Map();
for (const [, r] of list) { const q = byRoad.get(r.name) || { n: 0, m: 0 }; q.n++; q.m += r.len; byRoad.set(r.name, q); }
console.log(`lane fit: ${list.length} two-way ways (${km.toFixed(2)} km): ${list.filter(x => x[1].why === 'tags').length} tagged seven lanes or more, ${list.filter(x => x[1].why === 'lent').length} lent, ${list.filter(x => x[1].why === 'lent-bay').length} lent beside a centre bay, ${list.filter(x => x[1].why === 'twltl').length} with a tagged centre turn lane drawn as a double yellow (three or five lanes)`);
if (noProfile.length) { console.log(`  no profile (four lanes with a centre turn lane, left as exported): ${noProfile.length}`); for (const s of noProfile) console.log('    ' + s); }
for (const [k, v] of [...byRoad.entries()].sort((a, b) => b[1].m - a[1].m)) console.log(`  ${k}: ${v.n} ways, ${(v.m / 1000).toFixed(2)} km`);
for (const [id, r] of list) console.log(`  way ${id} '${r.name}' ${r.len.toFixed(0)} m: ${r.lf} + ${r.both ? 'TWLTL + ' : ''}${r.lb} (turn-only f ${r.bf.toString(2)} b ${r.bb.toString(2)}) [${r.why}]`);

if (!process.argv.includes('--check')) {
  let s = '';
  s += '// GENERATED by tools/city/lanefit.mjs (the road-lines round, 2026-11-16) from the raw OSM ways the\r\n';
  s += '// exporter read - do not edit by hand. Two-way ways whose tags carry seven lanes or more (the widest\r\n';
  s += '// exported profile holds six), and the short six-lane pieces between two of them (lent). Rows: OSM way\r\n';
  s += '// id, lanes forward, lanes backward, both_ways (0 / 1), turn-only bits forward, backward (bit i = lane i\r\n';
  s += '// from the left of its travel), in the WAY\'s direction.\r\n';
  s += 'namespace PSXRacing.City\r\n{\r\n    public static partial class LineModel\r\n    {\r\n';
  s += `        /// <summary>${list.length} ways, ${km.toFixed(2)} km.</summary>\r\n`;
  s += '        static readonly uint[] LaneFitData = {\r\n';
  for (const [id, r] of list) s += `            ${id}u, ${r.lf}, ${r.lb}, ${r.both}, ${r.bf}, ${r.bb},   // ${r.name.replace(/[\\\r\n]/g, ' ')} ${r.len.toFixed(0)} m${r.why === 'lent' ? ' [lent]' : r.why === 'lent-bay' ? ' [lent: the centre lane runs on into a bay]' : r.why === 'twltl' ? ' [centre turn lane]' : ''}\r\n`;
  s += '        };\r\n    }\r\n}\r\n';
  fs.writeFileSync(OUT, s);
  console.log('wrote ' + OUT);
}
