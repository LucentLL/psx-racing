// Research probe: which closed loops of real road near the Blue Ridge Parkway
// are short enough to race? Fetches every highway way in a handful of
// candidate bboxes (cached under tools/roads/cache, gitignored), lists the
// Parkway's access points (nodes it shares with other roads), and measures
// candidate loops through anchors with the same router the bakes use.
//
//   node tools/roads/probe_loops.mjs roads [key]        the big roads per bbox
//   node tools/roads/probe_loops.mjs junctions <key>    where the Parkway meets other roads
//   node tools/roads/probe_loops.mjs loop <name>        route + measure one candidate loop
//   node tools/roads/probe_loops.mjs loops              every candidate
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fetchOverpass, makeProjection, dist2, splineResample, tightestPlan,
         minSelfClearance, srtmSampler, smoothHeights, profileStats, arcPositions } from './lib.mjs';
import { routeByAnchors, CLASS_COST } from './route.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const cacheDir = join(here, 'cache');
mkdirSync(cacheDir, { recursive: true });
const USER_AGENT = 'psx-racing-loop-probe/1.0 (game map research; contact: mcgeevarnell@gmail.com)';

export const PROBES = {
  blowingrock: { bbox: { s: 36.09, w: -81.76, n: 36.19, e: -81.60 } },
  switzerland: { bbox: { s: 35.79, w: -82.17, n: 35.93, e: -81.98 } },
  asheville:   { bbox: { s: 35.54, w: -82.60, n: 35.70, e: -82.42 } },
  grandfather: { bbox: { s: 36.05, w: -81.90, n: 36.16, e: -81.70 } },
};

// A closed Parkway section is tagged highway=construction in OSM while it
// is being rebuilt; the road is there and the game is set in 1999.
// Service roads are allowed at a price: the Parkway's own access ramps and
// the Cone Manor entrance are tagged service, and without them a junction
// the map shows is a dead end to the router.
const MTN_COST = { ...CLASS_COST, construction: 1.15, service: 3.5, living_street: 4.0 };

// Candidate loops: anchors in route order (the router closes the ring back
// to the first). Filled in from the junction listing.
export const LOOPS = {
  // The Parkway between the US 321 interchange and the Moses Cone access,
  // closed through the village on Shulls Mill Road, US 221 and Main Street.
  blowingrock: {
    probe: 'blowingrock',
    anchors: [{ lat: 36.1478, lon: -81.6650, on: 'blue ridge parkway' },
              { lat: 36.1460, lon: -81.6830, on: 'blue ridge parkway' },
              { lat: 36.14646, lon: -81.70007, on: 'blue ridge parkway' },
              { lat: 36.1360, lon: -81.6820, on: 'yonahlossee' },
              { lat: 36.1365, lon: -81.6740, on: 'main street' }],
    prefer: { names: ['blue ridge parkway', 'main street', 'yonahlossee', 'valley boulevard', 'holshouser'], refs: ['US 221', 'US 321 Business'] },
  },
  // Craven Gap to Bull Gap on the Parkway, down Elk Mountain Scenic Highway
  // and back up Webb Cove Road to Town Mountain Road.
  townmountain: {
    probe: 'asheville',
    anchors: [{ lat: 35.64799, lon: -82.49097, on: 'blue ridge parkway' },
              { lat: 35.66429, lon: -82.47816, on: 'blue ridge parkway' },
              { lat: 35.660, lon: -82.495, on: 'elk mountain scenic' },
              { lat: 35.6453, lon: -82.5182, on: 'webb cove' },
              { lat: 35.6470, lon: -82.5050, on: 'webb cove' }],
    prefer: { names: ['blue ridge parkway', 'elk mountain scenic', 'webb cove', 'town mountain', 'ox creek'], refs: ['NC 694'] },
  },
  // The Parkway past the Folk Art Center between the US 70 and US 74A
  // interchanges, closed on Tunnel Road and Fairview Road.
  oteen: {
    probe: 'asheville',
    anchors: [{ lat: 35.5860, lon: -82.4785 }, { lat: 35.5625, lon: -82.4930 }, { lat: 35.5700, lon: -82.5030 }, { lat: 35.5850, lon: -82.5200 }],
    prefer: { names: ['blue ridge parkway', 'tunnel road', 'fairview road', 'charlotte highway', 'swannanoa river road'], refs: ['US 70', 'US 74A', 'NC 81'] },
  },
  // Gillespie Gap to Little Switzerland on the Parkway, closed on NC 226 and NC 226A.
  switzerland: {
    probe: 'switzerland',
    anchors: [{ lat: 35.8525, lon: -82.0500, on: 'blue ridge parkway' },
              { lat: 35.8520, lon: -82.0700, on: 'blue ridge parkway' },
              { lat: 35.8503, lon: -82.0920, on: 'blue ridge parkway' },
              { lat: 35.8500, lon: -82.0700, on: 'NC 226A' }],
    prefer: { names: ['blue ridge parkway', 'little switzerland tunnel'], refs: ['NC 226A'] },
  },
};

async function fetchProbe(key) {
  const b = PROBES[key].bbox;
  const query = `[out:json][timeout:180];
way["highway"](${b.s},${b.w},${b.n},${b.e});
out tags geom;`;
  return fetchOverpass({ cacheDir, name: 'loopprobe_' + key + '.json', query, userAgent: USER_AGENT });
}

function wayLenM(w) {
  let len = 0;
  for (let i = 1; i < w.geometry.length; i++) {
    const a = w.geometry[i - 1], c = w.geometry[i];
    const dx = (c.lon - a.lon) * 111320 * Math.cos(a.lat * Math.PI / 180), dz = (c.lat - a.lat) * 111132;
    len += Math.hypot(dx, dz);
  }
  return len;
}

const label = w => {
  const t = w.tags || {};
  return `${t.ref || ''} | ${t.name || ''} | ${t.highway || ''}`;
};

async function roads(key) {
  const res = await fetchProbe(key);
  const ways = res.elements.filter(e => e.type === 'way');
  console.log(`${key}: ${ways.length} ways`);
  const names = new Map();
  for (const w of ways) {
    const k = label(w);
    const e = names.get(k) || { n: 0, len: 0 };
    e.n++; e.len += wayLenM(w); names.set(k, e);
  }
  const rows = [...names.entries()].filter(([k, v]) => v.len > 800 && !/residential|service|track|path|footway|living_street|unclassified/.test(k))
    .sort((a, b) => b[1].len - a[1].len).slice(0, 40);
  for (const [k, v] of rows) console.log(`  ${(v.len / 1000).toFixed(1).padStart(6)} km  ${v.n.toString().padStart(3)} ways  ${k}`);
}

/// Every node the Parkway shares with a road that is not the Parkway: the
/// access points, printed with the other road's name so a loop can be read
/// off the list.
async function junctions(key) {
  const res = await fetchProbe(key);
  const ways = res.elements.filter(e => e.type === 'way' && e.geometry);
  const isBrp = w => /blue ridge parkway/i.test((w.tags || {}).name || '');
  const nodeKey = p => p.lat.toFixed(6) + ',' + p.lon.toFixed(6);
  const brpNodes = new Map();
  for (const w of ways) if (isBrp(w)) for (const p of w.geometry) brpNodes.set(nodeKey(p), p);
  const hits = [];
  for (const w of ways) {
    if (isBrp(w)) continue;
    const t = w.tags || {};
    if (/footway|path|cycleway|bridleway|steps|track/.test(t.highway || '')) continue;
    for (const p of w.geometry) {
      const k = nodeKey(p);
      if (!brpNodes.has(k)) continue;
      hits.push({ lat: p.lat, lon: p.lon, way: w });
    }
  }
  // Group hits within 60 m so one interchange prints as one line.
  const groups = [];
  for (const h of hits) {
    let g = groups.find(g => Math.hypot((g.lat - h.lat) * 111132, (g.lon - h.lon) * 111320 * Math.cos(h.lat * Math.PI / 180)) < 60);
    if (!g) { g = { lat: h.lat, lon: h.lon, roads: new Set() }; groups.push(g); }
    g.roads.add(label(h.way) + ` (${wayLenM(h.way).toFixed(0)} m)`);
  }
  groups.sort((a, b) => b.lat - a.lat);
  console.log(`${key}: ${groups.length} Parkway junction groups`);
  for (const g of groups)
    console.log(`  ${g.lat.toFixed(5)}, ${g.lon.toFixed(5)}  ->  ${[...g.roads].join(' ; ')}`);
}

/// The nearest node on a way whose name or ref contains `a.on`, so an
/// anchor lands ON the road it names rather than on whatever is nearest.
function snapTo(res, a) {
  const want = a.on.toLowerCase().replace(/\s+/g, '');
  let best = null, bestD = Infinity;
  for (const w of res.elements) {
    if (w.type !== 'way' || !w.geometry || !w.tags) continue;
    const tag = ((w.tags.name || '') + ' ' + (w.tags.ref || '')).toLowerCase().replace(/\s+/g, '');
    if (!tag.includes(want)) continue;
    for (const p of w.geometry) {
      const d = Math.hypot((p.lat - a.lat) * 111132, (p.lon - a.lon) * 111320 * Math.cos(a.lat * Math.PI / 180));
      if (d < bestD) { bestD = d; best = p; }
    }
  }
  if (!best) throw new Error('no way matches anchor.on = ' + a.on);
  console.log(`  anchor on ${a.on}: snapped ${bestD.toFixed(0)} m to ${best.lat.toFixed(5)}, ${best.lon.toFixed(5)}`);
  return { lat: best.lat, lon: best.lon, reachM: 3 };
}

/// Every way with a node within `r` metres of a point — what a road end
/// actually meets.
function near(res, lat, lon, r = 40) {
  const out = new Map();
  for (const w of res.elements) {
    if (w.type !== 'way' || !w.geometry) continue;
    for (const p of w.geometry) {
      const d = Math.hypot((p.lat - lat) * 111132, (p.lon - lon) * 111320 * Math.cos(lat * Math.PI / 180));
      if (d <= r) { out.set(w.id, `${label(w)} [${w.id}] ${d.toFixed(0)} m`); break; }
    }
  }
  return [...out.values()];
}

function planRadiusLoop(w, i) {
  const n = w.length;
  const a0 = w[(i - 1 + n) % n], a2 = w[(i + 1) % n];
  const a = Math.sqrt(dist2(a0, w[i])), b = Math.sqrt(dist2(w[i], a2)), c = Math.sqrt(dist2(a0, a2));
  const area = Math.abs((w[i].x - a0.x) * (a2.z - a0.z) - (a2.x - a0.x) * (w[i].z - a0.z)) * 0.5;
  return area < 1e-6 ? Infinity : (a * b * c) / (4 * area);
}

/// Route a candidate loop and print what the bake would refuse on.
async function measureLoop(name, cfg) {
  const res = await fetchProbe(cfg.probe);
  const b = PROBES[cfg.probe].bbox;
  const proj0 = makeProjection((b.s + b.n) / 2, (b.w + b.e) / 2);
  const anchors = cfg.anchors.map(a => a.on ? snapTo(res, a) : a);
  const { chain, wayOf, cost } = routeByAnchors(res, proj0, {
    anchors, loop: true, classes: null, prefer: cfg.prefer,
    classCost: MTN_COST, offPreference: cfg.offPreference ?? 8, quiet: true,
  });
  const cLat = chain.reduce((a, p) => a + p.lat, 0) / chain.length;
  const cLon = chain.reduce((a, p) => a + p.lon, 0) / chain.length;
  const proj = makeProjection(cLat, cLon);
  let cut = chain.map(p => proj.toXZ(p.lat, p.lon));
  const closeGap = Math.sqrt(dist2(cut[cut.length - 1], cut[0]));
  if (closeGap < 2) cut.pop();
  let wp = splineResample(cut, 4, { loop: true });
  while (wp.length > 2 && Math.sqrt(dist2(wp[wp.length - 1], wp[0])) < 2) wp.pop();
  const lengthM = wp.length * 4;
  const t = tightestPlan(wp, 12, { loop: true });
  const c = minSelfClearance(wp, { loop: true });
  // Which roads, in order, with metres on each.
  const legs = [];
  for (let i = 1; i < chain.length; i++) {
    const w = wayOf[i]; const l = w ? label(w) : '?';
    const m = Math.sqrt(dist2(proj.toXZ(chain[i - 1].lat, chain[i - 1].lon), proj.toXZ(chain[i].lat, chain[i].lon)));
    if (legs.length && legs[legs.length - 1].l === l) legs[legs.length - 1].m += m;
    else legs.push({ l, m });
  }
  const elevAt = await srtmSampler(b, cacheDir);
  const rawH = wp.map(p => { const ll = proj.toLL(p.x, p.z); return elevAt(ll.lat, ll.lon); });
  const sm = smoothHeights(rawH, { spacing: 4, sigma: cfg.sigma ?? 70, maxGrade: cfg.maxGrade ?? 0.10, circular: true });
  const st = profileStats(sm, 4, { circular: true });
  console.log(`=== ${name}: ${(lengthM / 1000).toFixed(2)} km, ${wp.length} wp, closes ${closeGap.toFixed(1)} m, cost ${(cost / 1000).toFixed(1)}`);
  console.log(`    tightest ${t.minR.toFixed(1)} m at wp ${t.at} (${t.under} under 12 m); self-clearance ${c.minM.toFixed(1)} m`);
  console.log(`    elevation ${st.minH.toFixed(0)}..${st.maxH.toFixed(0)} m, max grade ${(st.maxGrade * 100).toFixed(1)}%, vert R ${st.minVertR.toFixed(0)} m`);
  for (const l of legs) console.log(`    ${(l.m / 1000).toFixed(2).padStart(6)} km  ${l.l}`);
  // WHERE the tight corners and the near-miss are, with the road each is on.
  const wpS = arcPositions(wp);
  const legAt = m => { let acc = 0; for (const l of legs) { acc += l.m; if (m <= acc) return l.l; } return legs[legs.length - 1].l; };
  const corners = [];
  for (let i = 0; i < wp.length; i++) { const R = planRadiusLoop(wp, i); if (R < 12) corners.push([R, i]); }
  corners.sort((a, b) => a[0] - b[0]);
  for (const [R, i] of corners.slice(0, 6)) {
    const ll = proj.toLL(wp[i].x, wp[i].z);
    console.log(`    corner ${R.toFixed(1)} m at wp ${i} (${(i * 4 / 1000).toFixed(2)} km) ${ll.lat.toFixed(5)},${ll.lon.toFixed(5)} on ${legAt(i * 4)}`);
  }
  if (c.minM < 25) {
    const [i, j] = c.at;
    const a = proj.toLL(wp[i].x, wp[i].z);
    console.log(`    near-miss ${c.minM.toFixed(1)} m: wp ${i} (${(i * 4 / 1000).toFixed(2)} km, ${legAt(i * 4)}) vs wp ${j} (${(j * 4 / 1000).toFixed(2)} km, ${legAt(j * 4)}) at ${a.lat.toFixed(5)},${a.lon.toFixed(5)}`);
  }
  return { lengthM, t, c, st, legs };
}

const mode = process.argv[2] || 'roads';
const arg = process.argv[3];
if (mode === 'roads') { for (const k of Object.keys(PROBES)) if (!arg || arg === k) await roads(k); }
else if (mode === 'junctions') await junctions(arg);
else if (mode === 'loop') await measureLoop(arg, LOOPS[arg]);
else if (mode === 'near') {
  const [key, lat, lon, r] = process.argv.slice(3);
  const res = await fetchProbe(key);
  for (const l of near(res, +lat, +lon, r ? +r : 40)) console.log('  ' + l);
}
else if (mode === 'loops') { for (const [n, cfg] of Object.entries(LOOPS)) { try { await measureLoop(n, cfg); } catch (e) { console.log(`=== ${n}: ${e.message}`); } } }
