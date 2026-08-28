// probe_bogue.mjs — survey what OSM actually has for Bogue Banks before
// committing to a bake. Throwaway diagnostic; fetch_bogue.mjs is the real
// pipeline.
//
// Reports, for NC-58 / NC-24 / US-70 in the Crystal Coast bbox:
//   - which ways chain into a continuous route and how long it is
//   - every bridge=yes span, its length, and whether it is STRAIGHT
//     (max lateral deviation from its own end-to-end chord)
//   - the tags that might carry a deck height (layer, maxheight, seamark)
//
// The straightness number is the one that decides whether a bridge can be
// a drag strip: a graded straight-line race is fine, a graded CURVE is a
// different event.

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

async function fetchOverpass() {
  const q = `[out:json][timeout:180];
way["highway"~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|motorway_link|trunk_link|primary_link|secondary_link)$"](${BBOX.s},${BBOX.w},${BBOX.n},${BBOX.e});
out tags geom;`;
  const mirrors = [
    'https://overpass-api.de/api/interpreter',
    'https://overpass.kumi.systems/api/interpreter',
    'https://overpass.private.coffee/api/interpreter',
    'https://maps.mail.ru/osm/tools/overpass/api/interpreter',
  ];
  let lastErr = null;
  for (let round = 0; round < 3; round++) {
    for (const url of mirrors) {
      try {
        const buf = await fetchCached('overpass_bogue.json', url,
          { method: 'POST', body: 'data=' + encodeURIComponent(q),
            headers: {
              'Content-Type': 'application/x-www-form-urlencoded',
              'User-Agent': 'psx-racing-bogue-bake/1.0 (game map bake; contact: mcgeevarnell@gmail.com)',
              'Accept': 'application/json',
            } });
        const parsed = JSON.parse(buf.toString('utf8'));
        if (!parsed.elements || !parsed.elements.length)
          throw new Error('empty result');
        return parsed;
      } catch (e) {
        lastErr = e; console.log('  mirror failed: ' + e.message);
        try { const p = join(cacheDir, 'overpass_bogue.json');
              if (existsSync(p)) unlinkSync(p); } catch {}
      }
    }
    if (round < 2) { console.log('  retrying in 15 s...'); await new Promise(r => setTimeout(r, 15000)); }
  }
  throw lastErr;
}

// -------------------------------------------------------------- geometry
const R = 6371000;
function metresBetween(a, b) {
  const p = (a.lat + b.lat) / 2 * Math.PI / 180;
  const dx = (b.lon - a.lon) * 111412.84 * Math.cos(p);
  const dz = (b.lat - a.lat) * 111132.92;
  return Math.hypot(dx, dz);
}
function wayLength(g) {
  let s = 0;
  for (let i = 1; i < g.length; i++) s += metresBetween(g[i - 1], g[i]);
  return s;
}
// Max perpendicular distance from the end-to-end chord, in metres.
function chordDeviation(g) {
  if (g.length < 3) return 0;
  const a = g[0], b = g.at(-1);
  const p = (a.lat + b.lat) / 2 * Math.PI / 180;
  const X = ll => (ll.lon - a.lon) * 111412.84 * Math.cos(p);
  const Z = ll => (ll.lat - a.lat) * 111132.92;
  const ex = X(b), ez = Z(b);
  const len = Math.hypot(ex, ez);
  if (len < 1) return 0;
  let worst = 0;
  for (const q of g) {
    const d = Math.abs((X(q) * ez - Z(q) * ex) / len);
    if (d > worst) worst = d;
  }
  return worst;
}

// ------------------------------------------------------------------ main
const op = await fetchOverpass();
const ways = op.elements.filter(e => e.type === 'way' && e.geometry);
console.log(`total ways in bbox: ${ways.length}`);

const refOf = t => (t?.ref || '').toUpperCase();
const nameOf = t => (t?.name || '');
const routeMatch = (t, want) => {
  const r = refOf(t);
  if (!r) return false;
  return r.split(';').map(s => s.trim()).includes(want);
};

for (const want of ['NC 58', 'NC 24', 'US 70', 'US 70 BUS']) {
  const hit = ways.filter(w => routeMatch(w.tags, want));
  const total = hit.reduce((a, w) => a + wayLength(w.geometry), 0);
  const names = [...new Set(hit.map(w => nameOf(w.tags)).filter(Boolean))];
  console.log(`\n=== ${want}: ${hit.length} ways, ${(total / 1000).toFixed(1)} km of segments`);
  console.log(`    names: ${names.join(' | ') || '(none)'}`);
  const br = hit.filter(w => w.tags?.bridge);
  for (const w of br) {
    const L = wayLength(w.geometry);
    const dev = chordDeviation(w.geometry);
    const extra = ['layer', 'maxheight', 'maxspeed', 'lanes', 'name']
      .filter(k => w.tags[k]).map(k => `${k}=${w.tags[k]}`).join(' ');
    console.log(`    BRIDGE ${w.id}  ${L.toFixed(0)} m  chord-dev ${dev.toFixed(1)} m  ${extra}`);
  }
  if (!br.length) console.log('    (no bridge-tagged ways)');
}

// Any bridge anywhere in the bbox over 200 m — catches spans whose ref tag
// is missing or spelled differently.
console.log('\n=== all bridges > 200 m in bbox');
for (const w of ways) {
  if (!w.tags?.bridge) continue;
  const L = wayLength(w.geometry);
  if (L < 200) continue;
  const dev = chordDeviation(w.geometry);
  console.log(`  ${w.id}  ${L.toFixed(0)} m  dev ${dev.toFixed(1)} m  ` +
    `ref=${refOf(w.tags) || '-'} name=${nameOf(w.tags) || '-'} ` +
    `layer=${w.tags.layer || '-'} highway=${w.tags.highway}`);
}
console.log('\nOK');
