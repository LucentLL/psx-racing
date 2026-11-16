// emit_tracks.mjs - write the picked courses into Assets/PSXRacing/Resources/charlotte_tracks.json:
// the Charlotte races added after the export's own three, as chains of the shipped graph's edges.
//
//   node tools/city/tracks/emit_tracks.mjs H2[=routeId] [C5=queensroad ...]
//
// Each course is routed again from cands.mjs (the same waypoints the candidate sheet used), so the
// chain is the whole routed chain, not the measured samples. A loop's start line is the grid line
// analyze.mjs chose (out/cands.json gridLineM: the longest straight with two lanes in the race
// direction); a course with ends starts at its lead-in (leadM) and finishes shutM before its end.
// The file carries the graph hash it was routed on: the game refuses a route on any other graph
// (Scripts/CityTracks.cs), so a re-export that moves the edges cannot send a race down a wrong road.
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { city, routeVia, chainPoly, WT } from './lib.mjs';
import { CANDS } from './cands.mjs';

const C = city();
const hex = h => (h >>> 0).toString(16).padStart(8, '0');
const measured = JSON.parse(readFileSync(new URL('./out/cands.json', import.meta.url), 'utf8'));
const OUT = WT.replace(/[\\/]$/, '') + '/Assets/PSXRacing/Resources/charlotte_tracks.json';
const prev = existsSync(OUT) ? JSON.parse(readFileSync(OUT, 'utf8')) : null;
const routes = prev && prev.graph === hex(C.graphHash) ? prev.routes.slice() : [];
for (const arg of process.argv.slice(2)) {
  const [cid, rid] = arg.split('=');
  const cand = CANDS.find(c => c.id === cid);
  const m = measured.find(c => c.id === cid);
  if (!cand || !m) throw new Error('no candidate ' + cid + ' (run analyze.mjs first)');
  if (cand.route || cand.cut) throw new Error(cid + ': a cut of an existing route is a sprint section, not a new route');
  const loop = cand.kind === 'circuit';
  const chain = routeVia(cand.via.map(f => f()), { loop, fwy: !!cand.fwy, ...(cand.opts || {}) });
  // continuity, and a loop closes
  const from = c => c.dir === 1 ? C.edges[c.ei].a : C.edges[c.ei].b, to = c => c.dir === 1 ? C.edges[c.ei].b : C.edges[c.ei].a;
  for (let i = 1; i < chain.length; i++) if (to(chain[i - 1]) !== from(chain[i])) throw new Error(cid + ': chain breaks at ' + i);
  if (loop && to(chain[chain.length - 1]) !== from(chain[0])) throw new Error(cid + ': the loop does not close');
  const lengthM = chain.reduce((a, c) => a + C.edges[c.ei].length, 0);
  const startM = loop ? m.grid.lineS : (cand.leadM ?? 60);
  const finishM = loop ? 0 : lengthM - (cand.shutM ?? 250);
  // the menu's coarse line, every 20 m from the chain's start (charlotte_routes.json's own form)
  const poly = chainPoly(chain);
  const pts = [poly[0][0], poly[0][1]];
  let carry = 0;
  for (let i = 1; i < poly.length; i++) {
    const [ax, az] = poly[i - 1], [bx, bz] = poly[i];
    const seg = Math.hypot(bx - ax, bz - az);
    let t = 20 - carry;
    while (t < seg) { pts.push(+(ax + (bx - ax) * t / seg).toFixed(1), +(az + (bz - az) * t / seg).toFixed(1)); t += 20; }
    carry = (seg - (t - 20)) % 20;
  }
  const last = poly[poly.length - 1];
  pts.push(+last[0].toFixed(1), +last[1].toFixed(1));
  // the race width: the carriageway at the grid line
  const lineEdge = (() => { let acc = 0; for (const c of chain) { acc += C.edges[c.ei].length; if (acc >= startM) return C.edges[c.ei]; } return C.edges[chain[0].ei]; })();
  const r = {
    id: rid || cid.toLowerCase(), name: cand.name, loop: loop ? 1 : 0, oneway: cand.fwy ? 1 : 0,
    roadWidth: +lineEdge.profile.width.toFixed(1), speed: lineEdge.speed || 0,
    lengthM: +lengthM.toFixed(1), startM: +startM.toFixed(1), finishM: +finishM.toFixed(1), pts,
    edges: chain.map(c => c.ei), dirs: chain.map(c => c.dir), candidate: cid,
  };
  const at = routes.findIndex(x => x.id === r.id);
  if (at >= 0) routes[at] = r; else routes.push(r);
  console.log(`${cid} -> ${r.id}: ${chain.length} edges, ${(lengthM / 1000).toFixed(2)} km, start line ${startM.toFixed(0)} m on ${lineEdge.name || '(ramp)'}, width ${r.roadWidth} m, speed ${r.speed}`);
}
writeFileSync(OUT, JSON.stringify({
  note: 'Charlotte races added after the export\'s own (tools/city/tracks/emit_tracks.mjs). Edges and dirs are the shipped graph\'s; graph is its hash.',
  attribution: 'Road network data (c) OpenStreetMap contributors, ODbL 1.0',
  graph: hex(C.graphHash), routes,
}));
console.log('wrote', OUT);
