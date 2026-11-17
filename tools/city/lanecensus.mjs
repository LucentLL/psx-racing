// lanecensus.mjs - THE LANE-DATA CENSUS (the road-lines round, 2026-11-16): where OSM's lane tags and the
// exported line set (LSET) disagree, and where the line model has to relay lines sideways between two
// pieces of one road. Read-only: the shipped charlotte_city.bytes and the raw OSM ways the exporter read
// (the gitignored cache; PSX_CITY_CACHE overrides).
//
//   node tools/city/lanecensus.mjs [--tsv out.tsv]
//
// Classes, per two-way marked edge (its OSM way's tags against its LSET row):
//   ABOVE     the tags carry more lanes than the widest two-way profile (seven or more)
//   SQUEEZED  lanes:forward / lanes:backward are tagged and the row's split is another one ("fitted")
//   TWLTL-    a centre turn lane is tagged (lanes:both_ways, centre_turn_lane, turn:lanes:both_ways) and
//             the row draws a double yellow instead
//   TWLTL+    the row draws a centre turn lane the tags do not name (odd count, implied)
// and per join of two two-way pieces of one road (a 2-arm node, or the straight pair through a junction)
// whose rows differ on one profile - a RELAY in the line model:
//   THROUGH   a direction's through lanes (an inner left bay not counted) change: a lane ends or starts
//             inside the relay - North Tryon Street's fault
//   CENTRE    only the centre lane changes hands (bay <-> bay, bay <-> TWLTL): the through lanes stay
// and corridors where the centre line moves a lane one way and back within 300 m (an S).
import fs from 'node:fs';
import { load, toLL } from './splitcensus/load.mjs';
import { loadRaw } from './splitcensus/osmraw.mjs';

const LANE = 3.6576;
const city = load();
const ways = loadRaw();
const int = v => { const n = parseInt(v, 10); return Number.isFinite(n) ? n : NaN; };
const E = city.edges;
const nodeEdges = new Map();
for (let i = 0; i < E.length; i++) { const e = E[i]; e.index = i; for (const n of [e.a, e.b]) { if (!nodeEdges.has(n)) nodeEdges.set(n, []); nodeEdges.get(n).push(i); } }
const twoWay = e => !e.oneway && !e.link && e.a !== e.b && e.lset && (e.lset.flags & 1);
const innerBay = (n, bits) => (n >= 2 && (bits & 1)) ? 1 : 0;

const cls = { ABOVE: [], SQUEEZED: [], 'TWLTL-': [], 'TWLTL+': [] };
for (const e of E) {
  if (!twoWay(e)) continue;
  const w = ways.get(e.wayId); if (!w) continue;
  const t = w.tags;
  const lf = int(t['lanes:forward']), lb = int(t['lanes:backward']);
  const both = int(t['lanes:both_ways']) > 0 || t.centre_turn_lane === 'yes' || !!t['turn:lanes:both_ways'];
  const r = e.lset;
  e.tag = { lf, lb, both, L: int(t.lanes) };
  if (lf >= 1 && lb >= 1 && lf + lb + (both ? 1 : 0) >= 7) cls.ABOVE.push(e);
  if (lf >= 1 && lb >= 1 && !((r.nF === lf && r.nB === lb) || (r.nF === lb && r.nB === lf))) cls.SQUEEZED.push(e);
  if (both && r.centre !== 2) cls['TWLTL-'].push(e);
  if (!both && r.centre === 2) cls['TWLTL+'].push(e);
}
function byRoad(list) {
  const m = new Map();
  for (const e of list) { const k = e.name || '(unnamed)'; const q = m.get(k) || { n: 0, len: 0 }; q.n++; q.len += e.length; m.set(k, q); }
  return [...m.entries()].sort((a, b) => b[1].len - a[1].len);
}
const km = l => (l.reduce((s, e) => s + e.length, 0) / 1000).toFixed(2);
console.log(`LANE-DATA CENSUS: ${E.filter(twoWay).length} two-way marked edges`);
for (const k of Object.keys(cls)) {
  console.log(`${k}: ${cls[k].length} edges, ${km(cls[k])} km` + (k === 'TWLTL+' ? ' (informational)' : ''));
  for (const [name, q] of byRoad(cls[k]).slice(0, k === 'TWLTL+' ? 6 : 25)) console.log(`    ${name}: ${q.n} edges, ${(q.len / 1000).toFixed(2)} km`);
}

// ---- the relays: joins of two two-way pieces of one road whose rows differ on one profile ----
function nextAlong(e, node) {
  const arms = nodeEdges.get(node) || [];
  const P = e.pts, n = P.length;
  const tin = e.b === node ? [P[n - 1][0] - P[n - 2][0], P[n - 1][1] - P[n - 2][1]] : [P[0][0] - P[1][0], P[0][1] - P[1][1]];
  const li = Math.hypot(tin[0], tin[1]) || 1;
  let best = null, bc = 0.8, cnt = 0;
  for (const i of arms) {
    const o = E[i];
    if (o === e || !twoWay(o) || o.name !== e.name) continue;
    const Q = o.pts, m = Q.length;
    const tout = o.a === node ? [Q[1][0] - Q[0][0], Q[1][1] - Q[0][1]] : [Q[m - 2][0] - Q[m - 1][0], Q[m - 2][1] - Q[m - 1][1]];
    const lo = Math.hypot(tout[0], tout[1]) || 1;
    const c = (tin[0] * tout[0] + tin[1] * tout[1]) / (li * lo);
    if (c > 0.8) cnt++;
    if (c > bc) { bc = c; best = o; }
  }
  return cnt === 1 ? best : null;
}
// a row in a frame: same sense (e's a->b) or flipped
const rowIn = (o, same) => {
  const r = o.lset, f = r.turnOnly & 0xFF, b = (r.turnOnly >> 8) & 0xFF;
  return same ? { nF: r.nF, nB: r.nB, c: r.centre, tf: f, tb: b } : { nF: r.nB, nB: r.nF, c: r.centre, tf: b, tb: f };
};
// the centre line's place across the ribbon (from the plus edge, m): between the directions
const centreAt = r => (r.nB + (r.c === 2 ? 0.5 : 0)) * LANE;
const relays = [];
const seen = new Set();
for (const e of E) {
  if (!twoWay(e)) continue;
  for (const node of [e.a, e.b]) {
    const o = nextAlong(e, node);
    if (!o || o.profile.lanes !== e.profile.lanes) continue;
    const key = Math.min(e.index, o.index) + ':' + Math.max(e.index, o.index) + ':' + node;
    if (seen.has(key)) continue; seen.add(key);
    const same = (e.b === node) === (o.a === node);
    const A = rowIn(e, true), B = rowIn(o, same);
    if (A.nF === B.nF && A.nB === B.nB && A.c === B.c) continue;
    const thA = [A.nF - innerBay(A.nF, A.tf), A.nB - innerBay(A.nB, A.tb)], thB = [B.nF - innerBay(B.nF, B.tf), B.nB - innerBay(B.nB, B.tb)];
    const through = thA[0] !== thB[0] || thA[1] !== thB[1];
    const move = Math.abs(centreAt(A) - centreAt(B));
    const mph = e.speed > 0 ? e.speed / 1.609344 : [25, 35, 40, 45, 55, 65][Math.max(0, Math.min(5, e.rank))];
    const need = move * (mph <= 40 ? mph * mph / 60 : mph);
    relays.push({ e, o, node, A, B, through, move, mph, need, room: e.length + o.length, arms: (nodeEdges.get(node) || []).length });
  }
}
const thr = relays.filter(r => r.through), cen = relays.filter(r => !r.through);
const shortThr = thr.filter(r => r.room < r.need), shortCen = cen.filter(r => r.move > 0.5 && r.room < 0.5 * r.need);
console.log(`RELAYS (two pieces of one road, one profile, two line sets): ${relays.length}; THROUGH lane changes ${thr.length} (${shortThr.length} with the two pieces together shorter than the merging taper W S); CENTRE only ${cen.length} (${shortCen.length} shorter than half the taper)`);
function relayRoads(list) {
  const m = new Map();
  for (const r of list) { const k = r.e.name || '(unnamed)'; m.set(k, (m.get(k) || 0) + 1); }
  return [...m.entries()].sort((a, b) => b[1] - a[1]);
}
console.log('  THROUGH lane changes by road:');
for (const [name, n] of relayRoads(thr).slice(0, 30)) console.log(`    ${name}: ${n}`);

// ---- S: the centre line a lane or more one way and back within 300 m along one road ----
let sCount = 0; const sRoads = new Map(); const sList = [];
const usedS = new Set();
for (const r of relays) {
  if (r.move < 1.8) continue;
  // from the far piece o, walk on: the next join whose centre line comes back
  let cur = r.o, node = r.o.a === r.node ? r.o.b : r.o.a, dist = r.o.length, same = (r.e.b === r.node) === (r.o.a === r.node);
  const c0 = centreAt(r.A), c1 = centreAt(r.B);
  for (let g = 0; g < 8 && dist < 300; g++) {
    const nx = nextAlong(cur, node);
    if (!nx || nx.profile.lanes !== r.e.profile.lanes) break;
    const sm = same === ((cur.b === node) === (nx.a === node));
    const C = rowIn(nx, sm);
    const c2 = centreAt(C);
    if (Math.abs(c2 - c1) >= 1.8 && Math.sign(c2 - c1) === -Math.sign(c1 - c0)) {
      const key = [r.e.index, nx.index].sort((a, b) => a - b).join(':');
      if (!usedS.has(key)) { usedS.add(key); sCount++; const k = r.e.name || '(unnamed)'; sRoads.set(k, (sRoads.get(k) || 0) + 1); sList.push({ r, dist, nx }); }
      break;
    }
    dist += nx.length; node = nx.a === node ? nx.b : nx.a; cur = nx; same = sm;
  }
}
console.log(`S (the centre line a lane or more one way and back within 300 m): ${sCount}`);
for (const [name, n] of [...sRoads.entries()].sort((a, b) => b[1] - a[1]).slice(0, 30)) console.log(`    ${name}: ${n}`);

const nodeAt = (e, n) => e.a === n ? e.pts[0] : e.pts[e.pts.length - 1];
const ti = process.argv.indexOf('--tsv');
if (ti > 0) {
  let s = 'class\tedge\tedge2\tnode\troad\tx\tz\tlat\tlon\trowA\trowB\tmove\tmph\tneed\troom\n';
  for (const k of ['ABOVE', 'SQUEEZED', 'TWLTL-'])
    for (const e of cls[k]) { const p = e.pts[Math.floor(e.pts.length / 2)]; const [la, lo] = toLL(p[0], p[1]); s += `${k}\t${e.index}\t\t\t${e.name}\t${p[0].toFixed(0)}\t${p[1].toFixed(0)}\t${la.toFixed(6)}\t${lo.toFixed(6)}\t${e.lset.nF}+${e.lset.nB}c${e.lset.centre}\ttags ${e.tag.lf}+${e.tag.lb}${e.tag.both ? '+T' : ''} lanes=${e.tag.L}\t\t\t\t${e.length.toFixed(0)}\n`; }
  for (const r of relays) { const p = nodeAt(r.e, r.node); const [la, lo] = toLL(p[0], p[1]); s += `${r.through ? 'RELAY-THROUGH' : 'RELAY-CENTRE'}\t${r.e.index}\t${r.o.index}\t${r.node}\t${r.e.name}\t${p[0].toFixed(0)}\t${p[1].toFixed(0)}\t${la.toFixed(6)}\t${lo.toFixed(6)}\t${r.A.nF}+${r.A.nB}c${r.A.c}\t${r.B.nF}+${r.B.nB}c${r.B.c}\t${r.move.toFixed(2)}\t${r.mph.toFixed(0)}\t${r.need.toFixed(0)}\t${r.room.toFixed(0)}\n`; }
  for (const x of sList) { const p = nodeAt(x.r.e, x.r.node); const [la, lo] = toLL(p[0], p[1]); s += `S\t${x.r.e.index}\t${x.nx.index}\t${x.r.node}\t${x.r.e.name}\t${p[0].toFixed(0)}\t${p[1].toFixed(0)}\t${la.toFixed(6)}\t${lo.toFixed(6)}\t\t\t${x.r.move.toFixed(2)}\t${x.r.mph.toFixed(0)}\t\t${x.dist.toFixed(0)}\n`; }
  fs.writeFileSync(process.argv[ti + 1], s);
  console.log('wrote ' + process.argv[ti + 1]);
}
