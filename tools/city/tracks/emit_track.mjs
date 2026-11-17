// emit_track.mjs - ONE Charlotte track from the candidate sheet into ITS OWN FILE (merge-safe: adding a
// track adds Assets/PSXRacing/Resources/CityTracks/<route id>.json and edits nothing shared; the game
// finds it at load, Scripts/CityTracks.cs).
//
//   node tools/city/tracks/emit_track.mjs <CAND> [options]
//     <CAND>             a course of candidates_2026-11-16.json: C1-C10, H1-H7, S1-S6, X1-X4, D1-D4
//     --route id         the route id (default: the candidate id, lower case: "d1")
//     --venue Id         the venue id, TrackCatalog's (default: "Clt" + the candidate id: "CltD1")
//     --name "NAME"      the menu name, 30 characters at most (default: the sheet's name)
//     --label "LABEL"    the HUD's name for the run (default: the name after its ':' or the district)
//     --blurb "..."      the menu's line (default: made from the district, the roads and the length)
//     --laps N           a circuit's laps (default: as many as fit under the 14.2 km tank line, 1-4)
//     --limit KMH        the posted limit in km/h (default: the start line road's)
//     --oneway 0|1       every lane one way in the race (default 1 when every road is one-way, or a freeway lap)
//     --reverse 0|1      a reverse twin "<Id>Rev" (default 1 when no road of the course is one-way and it
//                        is not a drag; a one-way road backwards is the wrong way up it)
//     --rname/--rlabel/--rblurb   the twin's name, label and blurb (default: name + " II", the label, ...)
//     --host Scene       the scene it races in (default: UptownLoop for a loop, TryonSprint for one with ends)
//     --force            overwrite an existing file      --dry   print, write nothing
//
// The chain is the sheet's own (edge, dir) list on graph b2aa3453, checked again here: every edge exists,
// each one ends where the next begins, a loop closes, the length matches the sheet. A loop starts on the
// sheet's grid line (gridLineM); a course with ends starts leadM in and finishes runM later (a drag) or
// shutM before its end (a sprint). The game refuses a file routed on another graph (CityTracks.Register).
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { city, chainPoly, routeVia, WT, toX, toZ } from './lib.mjs';
import { CANDS } from './cands.mjs';
import { tracksDir, readTracks, venueIdsOf } from './trackfiles.mjs';

const args = process.argv.slice(2), BOOL = new Set(['force', 'dry']), vals = {}, flags = new Set();
let cid = null;
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (!a.startsWith('--')) { cid = cid ?? a; continue; }
  const k = a.slice(2);
  if (BOOL.has(k)) flags.add(k); else vals[k] = args[++i];
}
const opt = (k, d) => (k in vals ? vals[k] : d);
const flag = k => flags.has(k);
if (!cid) { console.error('usage: node tools/city/tracks/emit_track.mjs <CAND> [--route id] [--venue Id] [--name ..] ...'); process.exit(2); }

const C = city();
const hex = h => (h >>> 0).toString(16).padStart(8, '0');
const sheet = JSON.parse(readFileSync(new URL('./candidates_2026-11-16.json', import.meta.url), 'utf8'));
if (sheet.graphHash !== hex(C.graphHash)) throw new Error(`the sheet was routed on graph ${sheet.graphHash}, this city is ${hex(C.graphHash)}: re-route it first`);
const cand = sheet.courses.find(c => c.id === cid);
if (!cand) throw new Error('no candidate ' + cid + ' (the sheet has ' + sheet.courses.map(c => c.id).join(' ') + ')');
const kind = cand.kind;                       // circuit | sprint | drag
const loop = kind === 'circuit';
const chain = cand.chain.map(([ei, dir]) => ({ ei, dir: dir < 0 ? -1 : 1 }));

// ---- the chain, checked
const fail = m => { console.error('FAIL ' + cid + ': ' + m); process.exit(1); };
const from = c => c.dir === 1 ? C.edges[c.ei].a : C.edges[c.ei].b, to = c => c.dir === 1 ? C.edges[c.ei].b : C.edges[c.ei].a;
for (const c of chain) if (!C.edges[c.ei]) fail('edge ' + c.ei + ' is not in this city');
// a piece shorter than the sheet's 5 m sampling can be missing from its chain (S1: 2.6 m of Providence Road):
// put back the one or two edges (25 m at most) that join the gap, the legal way round; any other gap fails below
{
  const at = new Map();
  for (const e of C.edges) for (const n of [e.a, e.b]) { if (!at.has(n)) at.set(n, []); at.get(n).push(e); }
  const steps = n => (at.get(n) || []).flatMap(e => [
    ...(e.a === n ? [{ ei: e.index, dir: 1, to: e.b }] : []),
    ...(e.b === n && !e.oneway ? [{ ei: e.index, dir: -1, to: e.a }] : [])]);
  const join = (a, b) => {
    for (const s1 of steps(a)) if (s1.to === b) return [s1];
    for (const s1 of steps(a)) for (const s2 of steps(s1.to)) if (s2.to === b) return [s1, s2];
    return null;
  };
  const end = c => c.dir === 1 ? C.edges[c.ei].b : C.edges[c.ei].a, beg = c => c.dir === 1 ? C.edges[c.ei].a : C.edges[c.ei].b;
  for (let i = 1; i <= chain.length; i++) {
    if (i === chain.length && !loop) break;
    const prev = chain[i - 1], next = chain[i % chain.length];
    if (end(prev) === beg(next)) continue;
    const fix = join(end(prev), beg(next));
    if (!fix || fix.reduce((a, q) => a + C.edges[q.ei].length, 0) > 25) continue;
    chain.splice(i, 0, ...fix.map(q => ({ ei: q.ei, dir: q.dir })));
    console.log(`${cid}: the chain's gap after edge ${prev.ei} joined by ${fix.map(q => q.ei + ' (' + C.edges[q.ei].length.toFixed(1) + ' m)').join(', ')}`);
    i += fix.length;
  }
}
for (let i = 1; i < chain.length; i++) if (to(chain[i - 1]) !== from(chain[i])) fail('the chain breaks at ' + i);
if (loop && to(chain[chain.length - 1]) !== from(chain[0])) fail('the loop does not close');
for (const c of chain) { const e = C.edges[c.ei]; if (e.oneway && c.dir !== 1) fail('edge ' + c.ei + ' (' + (e.name || 'ramp') + ') is driven against its one-way direction'); }
const lengthM = chain.reduce((a, c) => a + C.edges[c.ei].length, 0);
const poly = chainPoly(chain);
// where the course starts on its chain: 0, or (a cut or a drag: the sheet's chain is every edge the course
// touches, whole) the sheet's cut point projected onto the chain, as analyze.mjs measured it
let s0 = 0;
if (Math.abs(lengthM - cand.lengthM) > Math.max(5, cand.lengthM * 0.01)) {
  const cut = (CANDS.find(c => c.id === cid) || {}).cut;
  if (loop || !cut || cut.from == null) fail(`the chain is ${lengthM.toFixed(0)} m, the sheet says ${cand.lengthM} m, and it has no cut point`);
  if (typeof cut.from === 'number') {
    // metres along the course's whole route (the export's own for a cut of one, else routed again from the
    // sheet's waypoints, as analyze.mjs did): less the route's length before the chain's first edge
    const def = CANDS.find(c => c.id === cid);
    let full;
    if (def.route) { const rt = (C.routes || []).find(x => x.id === def.route); full = rt.edges.map((ei, k) => ({ ei, dir: rt.dirs[k] < 0 ? -1 : 1 })); }
    else full = routeVia(def.via.map(f => f()), { loop, fwy: !!def.fwy, ...(def.opts || {}) });
    const k = full.findIndex(c => c.ei === chain[0].ei && c.dir === chain[0].dir);
    if (k < 0) fail('the chain of the sheet does not start on its route');
    let before = 0; for (let i = 0; i < k; i++) before += C.edges[full[i].ei].length;
    s0 = Math.max(0, cut.from - before);
  } else {
    const px = toX(cut.from.lon), pz = toZ(cut.from.lat);
    let best = 1e9, acc = 0;
    for (let i = 1; i < poly.length; i++) {
      const [ax, az] = poly[i - 1], [bx, bz] = poly[i], dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz, L = Math.sqrt(L2);
      const t = L2 > 0 ? Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / L2)) : 0;
      const d = Math.hypot(ax + dx * t - px, az + dz * t - pz);
      if (d < best) { best = d; s0 = acc + L * t; }
      acc += L;
    }
    // the sheet's point is the ROUTE's nearest to it (analyze.mjs), not necessarily on it
    if (best > 150) fail(`the cut point is ${best.toFixed(0)} m off the chain`);
    s0 = Math.max(0, s0 + (cut.from.d || 0));
  }
  if (s0 + cand.lengthM > lengthM + 5) fail(`the course (${cand.lengthM} m from ${s0.toFixed(0)} m) runs past its chain (${lengthM.toFixed(0)} m)`);
}

// ---- the line and the finish
let startM, finishM;
if (loop) {
  if (cand.gridLineM == null) fail('a circuit with no grid line on the sheet');
  startM = cand.gridLineM; finishM = 0;
} else {
  startM = s0 + (cand.leadM ?? 60);
  finishM = kind === 'drag' && cand.runM ? startM + cand.runM : s0 + cand.lengthM - (cand.shutM ?? 250);
  if (finishM <= startM + 100) fail('the finish is not past the line');
}
const edgeAt = m => { let acc = 0; for (const c of chain) { acc += C.edges[c.ei].length; if (acc >= m) return C.edges[c.ei]; } return C.edges[chain[chain.length - 1].ei]; };
const lineEdge = edgeAt(startM);

// ---- the menu's coarse line: every 20 m from the chain's start (charlotte_routes.json's own form)
const pts = [+poly[0][0].toFixed(1), +poly[0][1].toFixed(1)];
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

// ---- the venue
const roads = []; for (const c of chain) { const n = C.edges[c.ei].name; if (n && !roads.includes(n)) roads.push(n); }
const anyOneWay = chain.some(c => C.edges[c.ei].oneway), allOneWay = chain.every(c => C.edges[c.ei].oneway);
// a freeway-and-ramp lap (H1-H7, cands.mjs fwy) races one way on every lane, the short crossing street
// at a diamond turnaround included (TrafficSystem: all lanes one way on a one-way venue)
const fwy = !!(CANDS.find(c => c.id === cid) || {}).fwy;
const oneWay = +(opt('oneway', allOneWay || fwy ? 1 : 0));
const routeId = opt('route', cid.toLowerCase());
const venueId = opt('venue', 'Clt' + cid);
if (!/^[a-z0-9]+$/.test(routeId)) fail('a route id is lower-case letters and digits: ' + routeId);
if (!/^[A-Za-z][A-Za-z0-9]*$/.test(venueId)) fail('a venue id is letters and digits: ' + venueId);
if ((C.routes || []).some(r => r.id === routeId) || routeId === 'deck') fail('route id ' + routeId + ' is one of the export\'s own');
const catalog = readFileSync(join(WT, 'Assets/PSXRacing/Scripts/TrackCatalog.cs'), 'utf8');
if (new RegExp('id = "' + venueId + '(Rev)?"').test(catalog)) fail('venue id ' + venueId + ' is TrackCatalog\'s already');
const file = join(tracksDir(WT), routeId + '.json');
for (const t of readTracks(WT)) {
  if (t.file === routeId + '.json') continue;
  if (venueIdsOf(t).some(v => v === venueId || v === venueId + 'Rev')) fail('venue id ' + venueId + ' is ' + t.file + '\'s already');
  if (t.candidate === cid) fail('candidate ' + cid + ' is ' + t.file + ' already');
}
if (existsSync(file) && !flag('force') && !flag('dry')) fail(file + ' exists (--force to write it again)');

const km = x => (x / 1000).toFixed(1);
// the posted limit: the start line road's, or (unknown there) the one most of the course is posted at, or 56 km/h
const speedOf = () => {
  if (lineEdge.speed) return Math.round(lineEdge.speed);
  const w = new Map();
  for (const c of chain) { const e = C.edges[c.ei]; if (e.speed) w.set(e.speed, (w.get(e.speed) || 0) + e.length); }
  let best = 0, bw = 0;
  for (const [sp, l] of w) if (l > bw) { bw = l; best = sp; }
  return Math.round(best || 56);
};
const laps = +(opt('laps', loop ? Math.max(1, Math.min(4, Math.floor(14200 / lengthM))) : 1));
const name = opt('name', cand.name);
if (name.length > 30) fail(`the name "${name}" is ${name.length} characters, the menu fits 30: --name "SHORTER"`);
const label = opt('label', (cand.name.includes(':') ? cand.name.split(':').pop().trim() : (cand.district || '').toUpperCase()).slice(0, 16));
const roadsText = roads.slice(0, 3).join(', ') + (roads.length > 3 ? ' and on' : '');
const runM = finishM - startM;
const blurb = opt('blurb', loop
  ? `${cand.district}: ${roadsText}. ${km(lengthM)} km a lap, ${laps} lap${laps > 1 ? 's' : ''}. Map (c) OpenStreetMap contributors.`
  : kind === 'drag'
    ? `${cand.district}: ${runM < 500 ? 'a quarter mile' : 'a half mile'} (${runM.toFixed(0)} m) from a standing start on ${roads[0] || 'the street'}. Map (c) OpenStreetMap contributors.`
    : `${cand.district}: ${roadsText}. ${km(runM)} km, line to flag. Map (c) OpenStreetMap contributors.`);
const reverse = +(opt('reverse', !anyOneWay && kind !== 'drag' ? 1 : 0));
if (reverse && anyOneWay) console.warn(`WARN ${cid}: --reverse 1 on a course with one-way roads: the twin drives them backwards (a closed course only)`);
const venue = {
  id: venueId, name, blurb, label,
  host: opt('host', loop ? 'UptownLoop' : 'TryonSprint'),
  roadWidth: +lineEdge.profile.width.toFixed(1),
  speedLimitKmh: +(opt('limit', speedOf())),
  laps, oneWay, reverse, noDelivery: 1,
  reverseName: reverse ? opt('rname', (name.length <= 27 ? name + ' II' : name)) : '',
  reverseLabel: reverse ? opt('rlabel', label) : '',
  reverseBlurb: reverse ? opt('rblurb', 'The same road, the other way round. ' + blurb) : '',
};
const route = {
  id: routeId, name: cand.name, loop: loop ? 1 : 0, oneway: oneWay,
  roadWidth: venue.roadWidth, speed: lineEdge.speed || venue.speedLimitKmh,
  lengthM: +lengthM.toFixed(1), startM: +startM.toFixed(1), finishM: +finishM.toFixed(1), pts,
  edges: chain.map(c => c.ei), dirs: chain.map(c => c.dir),
};
const J = x => JSON.stringify(x);
const text = '{\n' + [
  `"version": 1`, `"graph": ${J(hex(C.graphHash))}`,
  `"attribution": ${J('Road network data (c) OpenStreetMap contributors, ODbL 1.0')}`,
  `"candidate": ${J(cid)}`,
  `"venue": ${JSON.stringify(venue, null, 2).replace(/\n/g, '\n  ')}`,
  `"route": ${J(route)}`,
].map(l => '  ' + l).join(',\n') + '\n}\n';
console.log(`${cid} -> ${venueId} (route ${routeId}, ${kind}): ${chain.length} edges, ${km(lengthM)} km, ` +
  (loop ? `line at ${startM.toFixed(0)} m, ${laps} lap(s) = ${km(lengthM * laps)} km` : `line at ${startM.toFixed(0)} m, flag at ${finishM.toFixed(0)} m (${runM.toFixed(0)} m)`) +
  ` on ${lineEdge.name || '(ramp)'}, ${venue.roadWidth} m wide, ${venue.speedLimitKmh} km/h, one-way roads ${allOneWay ? 'all' : anyOneWay ? 'some' : 'none'}${fwy ? ' (a freeway lap)' : ''}, oneWay ${oneWay}, reverse ${reverse}, host ${venue.host}`);
if (flag('dry')) { console.log('(dry: nothing written)'); process.exit(0); }
mkdirSync(tracksDir(WT), { recursive: true });
writeFileSync(file, text);
console.log('wrote ' + file);
