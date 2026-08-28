// probe3_bogue.mjs — route the candidate venues and report what they are,
// before any of it gets baked into a stage.

import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadGraph, nearestNode, shortestPath, pathGeometry, pathLength } from './route.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const ORIGIN = { lat0: 34.69, lon0: -76.90 };
const g = loadGraph(join(here, 'cache', 'overpass_bogue.json'), ORIGIN);
console.log(`graph: ${g.ways.length} routable ways, ${g.nodeLL.length} nodes`);

// Anchors. The bridge endpoints are EXACT, straight off the OSM way tags
// found by probe2 — those are the only coordinates here that are not eyeballed.
const A = {
  langstonN:  { lat: 34.67979, lon: -77.06710, note: 'Langston Bridge, Cape Carteret end' },
  langstonS:  { lat: 34.66750, lon: -77.06333, note: 'Langston Bridge, Emerald Isle end' },
  abBridgeS:  { lat: 34.71106, lon: -76.73684, note: 'Atlantic Beach Bridge, island end' },
  abBridgeN:  { lat: 34.72111, lon: -76.73435, note: 'Atlantic Beach Bridge, Morehead end' },
  fortMacon:  { lat: 34.69750, lon: -76.68000, note: 'Fort Macon, east tip' },
  emeraldMid: { lat: 34.67350, lon: -77.04500, note: 'Emerald Isle, mid-island' },
  capeCarteret:{lat: 34.68900, lon: -77.06400, note: 'Cape Carteret, NC-24/NC-58' },
  moreheadW:  { lat: 34.72700, lon: -76.78500, note: 'Morehead City, west side' },
  newport:    { lat: 34.78600, lon: -76.85900, note: 'Newport' },
};

const snapped = {};
for (const [k, v] of Object.entries(A)) {
  const s = nearestNode(g, v.lat, v.lon);
  snapped[k] = s.node;
  console.log(`  ${k.padEnd(13)} snapped ${s.offM.toFixed(0).padStart(4)} m  — ${v.note}`);
}

function report(label, keys) {
  let total = 0;
  const parts = [];
  const allGeom = [];
  for (let i = 0; i + 1 < keys.length; i++) {
    const p = shortestPath(g, snapped[keys[i]], snapped[keys[i + 1]]);
    if (!p) { console.log(`\n${label}: NO PATH ${keys[i]} -> ${keys[i + 1]}`); return null; }
    const geom = pathGeometry(g, p);
    const L = pathLength(g, geom);
    total += L; parts.push(`${keys[i]}->${keys[i + 1]} ${(L / 1000).toFixed(2)} km`);
    allGeom.push(...(i ? geom.slice(1) : geom));
  }
  // which named roads did it actually use, by metres
  const byName = new Map();
  for (let i = 1; i < allGeom.length; i++) {
    const w = g.ways[allGeom[i].way];
    const nm = w ? (w.tags.name || w.tags.ref || w.tags.highway) : '?';
    const a = g.toXZ(allGeom[i - 1].lat, allGeom[i - 1].lon);
    const b = g.toXZ(allGeom[i].lat, allGeom[i].lon);
    byName.set(nm, (byName.get(nm) || 0) + Math.hypot(b.x - a.x, b.z - a.z));
  }
  const top = [...byName.entries()].sort((x, y) => y[1] - x[1]).slice(0, 8)
    .map(([n, m]) => `${n} ${(m / 1000).toFixed(1)}`).join(', ');
  console.log(`\n${label}: ${(total / 1000).toFixed(2)} km  [${parts.join(' + ')}]`);
  console.log(`   ${allGeom.length} vertices; roads (km): ${top}`);
  return allGeom;
}

report('LANGSTON BRIDGE run', ['langstonN', 'langstonS']);
report('ATLANTIC BEACH BRIDGE run', ['abBridgeN', 'abBridgeS']);
report('ISLAND, west foot to Fort Macon', ['langstonS', 'emeraldMid', 'abBridgeS', 'fortMacon']);
report('ISLAND + both bridges', ['langstonN', 'emeraldMid', 'abBridgeS', 'abBridgeN']);
const loop = report('FULL CIRCUIT (island out, mainland back)',
  ['langstonN', 'langstonS', 'emeraldMid', 'abBridgeS', 'abBridgeN', 'moreheadW', 'capeCarteret', 'langstonN']);

// Longest straight on the island, done properly this time.
const island = report('  (straight hunt) island only', ['langstonS', 'emeraldMid', 'abBridgeS']);
if (island) {
  const TOL = 6;
  const P = i => g.toXZ(island[i].lat, island[i].lon);
  let best = { len: 0, i0: 0, i1: 0 };
  for (let i = 0; i < island.length; i++) {
    const a = P(i);
    for (let j = i + 2; j < island.length; j++) {
      const b = P(j);
      const ex = b.x - a.x, ez = b.z - a.z, len = Math.hypot(ex, ez);
      let ok = true;
      for (let k = i + 1; k < j; k++) {
        const q = P(k);
        if (Math.abs(((q.x - a.x) * ez - (q.z - a.z) * ex) / len) > TOL) { ok = false; break; }
      }
      if (!ok) break;
      if (len > best.len) best = { len, i0: i, i1: j };
    }
  }
  console.log(`\nlongest straight on the island (tol ${TOL} m): ${best.len.toFixed(0)} m`);
  console.log(`  from ${island[best.i0].lat.toFixed(5)},${island[best.i0].lon.toFixed(5)}`);
  console.log(`  to   ${island[best.i1].lat.toFixed(5)},${island[best.i1].lon.toFixed(5)}`);
  const w = g.ways[island[best.i0 + 1].way];
  console.log(`  on: ${w ? (w.tags.name || w.tags.ref) : '?'}`);
}
