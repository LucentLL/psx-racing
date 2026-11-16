// legdebug.mjs - print each waypoint's edge and each routed leg of one candidate
import { city, toLat, toLon, routeVia, edgePts } from './lib.mjs';
import { CANDS } from './cands.mjs';
const C = city();
const id = process.argv[2];
const cand = CANDS.find(c => c.id === id);
const wps = cand.via.map(f => f());
const hdgOf = w => { const p = edgePts(C.edges[w.ei], w.dir); const a = p[0], b = p[p.length - 1]; return Math.round((Math.atan2(b[0] - a[0], b[1] - a[1]) * 180 / Math.PI + 360) % 360); };
for (const w of wps) {
  if (typeof w === 'number') { console.log('node', w); continue; }
  const e = C.edges[w.ei]; const p = edgePts(e, w.dir)[0];
  console.log(`wp e${w.ei} ${e.name || '(ramp)'} link=${e.link} ow=${e.oneway} dir=${w.dir} hdg=${hdgOf(w)} len=${e.length.toFixed(0)} at ${toLat(p[1]).toFixed(5)},${toLon(p[0]).toFixed(5)}`);
}
for (let i = 1; i <= wps.length; i++) {
  const sub = [wps[i - 1], wps[i % wps.length]];
  try {
    const ch = routeVia(sub, { fwy: !!cand.fwy });
    const L = ch.reduce((a, c) => a + C.edges[c.ei].length, 0);
    const names = []; for (const c of ch) { const n = C.edges[c.ei].name || '(ramp)'; if (names[names.length - 1] !== n) names.push(n); }
    console.log(`leg ${i - 1}->${i % wps.length}: ${L.toFixed(0)} m  ${names.join(' > ').slice(0, 220)}`);
  } catch (e) { console.log(`leg ${i - 1}->${i % wps.length}: FAIL ${e.message}`); }
}
