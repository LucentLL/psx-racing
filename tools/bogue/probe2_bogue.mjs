// probe2_bogue.mjs — second pass. probe_bogue reported chord-dev 0.0 m for
// the two big bridges, which is also what a 2-vertex way reports, so this
// checks VERTEX COUNTS before anyone believes it. Also chains NC-58 across
// the island and hunts the longest dead-straight run on it.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const op = JSON.parse(readFileSync(join(here, 'cache', 'overpass_bogue.json'), 'utf8'));
const ways = op.elements.filter(e => e.type === 'way' && e.geometry);

const LATM = 111132.92, LONM = 111412.84 * Math.cos(34.7 * Math.PI / 180);
const X = ll => ll.lon * LONM, Z = ll => ll.lat * LATM;
const seg = (a, b) => Math.hypot(X(b) - X(a), Z(b) - Z(a));
const wayLength = g => g.reduce((s, p, i) => i ? s + seg(g[i - 1], p) : 0, 0);

function devDetail(g) {
  if (g.length < 3) return { dev: null, note: `${g.length}-vertex way — chord test meaningless` };
  const a = g[0], b = g.at(-1);
  const ex = X(b) - X(a), ez = Z(b) - Z(a), len = Math.hypot(ex, ez);
  let worst = 0;
  for (const q of g) worst = Math.max(worst, Math.abs(((X(q) - X(a)) * ez - (Z(q) - Z(a)) * ex) / len));
  return { dev: worst, note: `${g.length} vertices` };
}

console.log('=== the two candidate strips, in detail');
for (const id of [42820377, 16461727]) {
  const w = ways.find(v => v.id === id);
  const d = devDetail(w.geometry);
  console.log(`\n  way ${id}  ${wayLength(w.geometry).toFixed(0)} m  ${d.note}` +
    (d.dev === null ? '' : `  max chord deviation ${d.dev.toFixed(2)} m`));
  console.log(`    tags: ${JSON.stringify(w.tags)}`);
  console.log(`    from ${w.geometry[0].lat.toFixed(5)},${w.geometry[0].lon.toFixed(5)}` +
    `  to ${w.geometry.at(-1).lat.toFixed(5)},${w.geometry.at(-1).lon.toFixed(5)}`);
  // bearing drift between consecutive vertices tells you about kinks the
  // chord test can average away
  if (w.geometry.length >= 3) {
    let maxTurn = 0;
    for (let i = 1; i + 1 < w.geometry.length; i++) {
      const a = w.geometry[i - 1], b = w.geometry[i], c = w.geometry[i + 1];
      const h1 = Math.atan2(X(b) - X(a), Z(b) - Z(a));
      const h2 = Math.atan2(X(c) - X(b), Z(c) - Z(b));
      let t = Math.abs(h2 - h1); if (t > Math.PI) t = 2 * Math.PI - t;
      maxTurn = Math.max(maxTurn, t * 180 / Math.PI);
    }
    console.log(`    sharpest vertex-to-vertex turn: ${maxTurn.toFixed(2)} deg`);
  }
}

// ------------------------------------------------- chain NC-58 end to end
const isIsland = t => {
  if (!t) return false;
  const r = (t.ref || '').toUpperCase().split(';').map(s => s.trim());
  if (r.includes('NC 58')) return true;
  const n = t.name || '';
  return /Emerald Drive|Salter Path Road|Fort Macon Road|Atlantic Beach Bridge/i.test(n);
};
const strip = ways.filter(w => isIsland(w.tags));
console.log(`\n=== island route candidates: ${strip.length} ways`);

const key = p => p.lat.toFixed(6) + ',' + p.lon.toFixed(6);
// grow the longest chain we can from the westernmost way
let start = strip[0];
for (const w of strip)
  if (Math.min(w.geometry[0].lon, w.geometry.at(-1).lon) <
      Math.min(start.geometry[0].lon, start.geometry.at(-1).lon)) start = w;
const used = new Set([start.id]);
let chain = [...start.geometry];
let grew = true;
while (grew) {
  grew = false;
  for (const w of strip) {
    if (used.has(w.id)) continue;
    const g = w.geometry, hK = key(chain[0]), tK = key(chain.at(-1));
    let add = null, front = false;
    if (key(g[0]) === tK) add = g.slice(1);
    else if (key(g.at(-1)) === tK) add = g.slice(0, -1).reverse();
    else if (key(g[0]) === hK) { add = g.slice(1).reverse(); front = true; }
    else if (key(g.at(-1)) === hK) { add = g.slice(0, -1); front = true; }
    if (!add) continue;
    used.add(w.id); grew = true;
    if (front) chain.unshift(...add); else chain.push(...add);
  }
}
console.log(`  chained ${used.size}/${strip.length} ways -> ${chain.length} vertices, ` +
  `${(wayLength(chain) / 1000).toFixed(2)} km`);
console.log(`  west end ${chain[0].lat.toFixed(5)},${chain[0].lon.toFixed(5)}`);
console.log(`  east end ${chain.at(-1).lat.toFixed(5)},${chain.at(-1).lon.toFixed(5)}`);

// ------------------------------- longest straight run along the island
// Walk the chain; a run continues while every point stays within TOL of
// the run's own start->current chord.
const TOL = 6;   // metres — half a lane; wider than this is a visible bend
let best = { len: 0, i0: 0, i1: 0 };
for (let i = 0; i < chain.length; i++) {
  for (let j = i + 2; j < chain.length; j++) {
    const a = chain[i], b = chain[j];
    const ex = X(b) - X(a), ez = Z(b) - Z(a), len = Math.hypot(ex, ez);
    if (len < 1) continue;
    let ok = true;
    for (let k = i + 1; k < j; k++) {
      const q = chain[k];
      if (Math.abs(((X(q) - X(a)) * ez - (Z(q) - Z(a)) * ex) / len) > TOL) { ok = false; break; }
    }
    if (!ok) break;
    if (len > best.len) best = { len, i0: i, i1: j };
  }
}
console.log(`\n=== longest straight on the island chain (tol ${TOL} m): ${best.len.toFixed(0)} m`);
console.log(`  from ${chain[best.i0].lat.toFixed(5)},${chain[best.i0].lon.toFixed(5)}`);
console.log(`  to   ${chain[best.i1].lat.toFixed(5)},${chain[best.i1].lon.toFixed(5)}`);
