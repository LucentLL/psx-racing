// probe4_water.mjs — what does OSM give us for water, sand and the
// navigation channel?
//
// Three things the bake needs and the highway extract does not carry:
//   1. a land/water mask, because on a barrier island the ground builder
//      has to know which chunks are ocean and which are sand
//   2. natural=beach polygons, ditto for the strand
//   3. the Intracoastal Waterway channel, which is WHERE THE CROWN OF EACH
//      BRIDGE GOES — the 65 ft clearance is over the fairway, not over the
//      midpoint of the span, and putting the summit in the wrong place is
//      the difference between a real profile and a speed bump.

import { mkdirSync, readFileSync, writeFileSync, existsSync, unlinkSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const cacheDir = join(here, 'cache');
mkdirSync(cacheDir, { recursive: true });
const BBOX = { s: 34.64, w: -77.14, n: 34.80, e: -76.65 };

async function fetchCached(name, url, opts) {
  const path = join(cacheDir, name);
  if (existsSync(path)) return readFileSync(path);
  console.log('fetching ' + url);
  const res = await fetch(url, opts);
  if (!res.ok) throw new Error(url + ' -> HTTP ' + res.status);
  const buf = Buffer.from(await res.arrayBuffer());
  writeFileSync(path, buf);
  return buf;
}

async function overpass(name, q) {
  const mirrors = [
    'https://overpass-api.de/api/interpreter',
    'https://overpass.kumi.systems/api/interpreter',
    'https://overpass.private.coffee/api/interpreter',
  ];
  let lastErr = null;
  for (let round = 0; round < 3; round++) {
    for (const url of mirrors) {
      try {
        const buf = await fetchCached(name, url, {
          method: 'POST', body: 'data=' + encodeURIComponent(q),
          headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
            'User-Agent': 'psx-racing-bogue-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)',
            'Accept': 'application/json',
          },
        });
        const parsed = JSON.parse(buf.toString('utf8'));
        if (!parsed.elements) throw new Error('no elements');
        return parsed;
      } catch (e) {
        lastErr = e; console.log('  mirror failed: ' + e.message);
        try { const p = join(cacheDir, name); if (existsSync(p)) unlinkSync(p); } catch {}
      }
    }
    if (round < 2) { console.log('  retrying in 15 s...'); await new Promise(r => setTimeout(r, 15000)); }
  }
  throw lastErr;
}

const q = `[out:json][timeout:180];
(
  way["natural"="coastline"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["natural"="water"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["natural"="beach"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["natural"="sand"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["waterway"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  way["seamark:type"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
  relation["natural"="water"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
);
out tags geom;`;

const op = await overpass('overpass_bogue_water.json', q);
console.log(`elements: ${op.elements.length}`);

const LATM = 111132.92, LONM = 111412.84 * Math.cos(34.7 * Math.PI / 180);
const len = g => { let s = 0; for (let i = 1; i < g.length; i++)
  s += Math.hypot((g[i].lon - g[i-1].lon) * LONM, (g[i].lat - g[i-1].lat) * LATM); return s; };
const areaKm2 = g => { let a = 0; for (let i = 0; i < g.length; i++) {
  const p = g[i], q2 = g[(i + 1) % g.length];
  a += (p.lon * LONM) * (q2.lat * LATM) - (q2.lon * LONM) * (p.lat * LATM); }
  return Math.abs(a) / 2 / 1e6; };

const tally = new Map();
for (const e of op.elements) {
  const t = e.tags || {};
  const k = t.natural ? `natural=${t.natural}`
    : t['seamark:type'] ? `seamark=${t['seamark:type']}`
    : t.waterway ? `waterway=${t.waterway}` : 'other';
  const r = tally.get(k) || { n: 0, m: 0 };
  r.n++; if (e.geometry) r.m += len(e.geometry);
  tally.set(k, r);
}
console.log('\n=== by tag');
for (const [k, v] of [...tally.entries()].sort((a, b) => b[1].n - a[1].n))
  console.log(`  ${k.padEnd(28)} ${String(v.n).padStart(4)} elements, ${(v.m / 1000).toFixed(1)} km of line`);

console.log('\n=== biggest water bodies');
const waters = op.elements.filter(e => e.tags?.natural === 'water' && e.geometry?.length > 3)
  .map(e => ({ name: e.tags.name || '(unnamed)', a: areaKm2(e.geometry), n: e.geometry.length, type: e.type }))
  .sort((a, b) => b.a - a.a).slice(0, 12);
for (const w of waters) console.log(`  ${w.a.toFixed(2).padStart(8)} km2  ${w.n.toString().padStart(5)} pts  ${w.type}  ${w.name}`);

console.log('\n=== coastline ways');
const cl = op.elements.filter(e => e.tags?.natural === 'coastline');
console.log(`  ${cl.length} ways, ${(cl.reduce((a, e) => a + len(e.geometry), 0) / 1000).toFixed(1)} km total`);
for (const e of cl.slice(0, 8))
  console.log(`    ${e.id} ${len(e.geometry).toFixed(0).padStart(6)} m  ${e.geometry.length} pts  ` +
    `${e.geometry[0].lat.toFixed(4)},${e.geometry[0].lon.toFixed(4)} -> ` +
    `${e.geometry.at(-1).lat.toFixed(4)},${e.geometry.at(-1).lon.toFixed(4)}`);

console.log('\n=== beach / sand');
for (const e of op.elements.filter(e => e.tags?.natural === 'beach' || e.tags?.natural === 'sand'))
  console.log(`  ${e.tags.natural} ${e.id} ${areaKm2(e.geometry).toFixed(3)} km2 ${e.geometry.length} pts ${e.tags.name || ''}`);

console.log('\n=== navigable channel candidates (near the two bridges)');
const near = (lat, lon, e) => e.geometry?.some(p =>
  Math.abs(p.lat - lat) < 0.02 && Math.abs(p.lon - lon) < 0.02);
for (const [nm, lat, lon] of [['Langston', 34.6736, -77.0652], ['AtlanticBeach', 34.7161, -76.7356]]) {
  console.log(`  -- ${nm}`);
  for (const e of op.elements) {
    const t = e.tags || {};
    if (!(t.waterway || t['seamark:type'])) continue;
    if (!near(lat, lon, e)) continue;
    console.log(`     ${e.id} ${JSON.stringify(t).slice(0, 150)}`);
  }
}
