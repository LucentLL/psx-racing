// mdtable.mjs - the candidate table as markdown rows (for the art-workspace note)
import { readFileSync } from 'node:fs';
import { toLat, toLon } from './lib.mjs';
const cands = JSON.parse(readFileSync(new URL('./out/cands.json', import.meta.url), 'utf8'));
const ord = c => 'CHSXD'.indexOf(c.id[0]) * 100 + +c.id.slice(1);
const status = c => { const bad = c.faults.filter(f => f.hard).map(f => f.label.split(' (')[0]); const surf = [...new Set((c.auditHits || []).filter(h => ['DECK-HOLE', 'STEP', 'STEP-JOINT', 'KINK', 'KINK-JOINT', 'UNDER-STEP', 'UNDER-KINK', 'WALL-IN-LANE', 'RAIL-ON-LANE'].includes(h.kind)).map(h => h.kind))];
  return bad.length ? 'NEEDS THE DECK FIX: ' + bad[0] : surf.length ? 'NEEDS A JOINT FIX: ' + surf.join(', ') : 'DRIVABLE NOW'; };
cands.sort((a, b) => ord(a) - ord(b));
const sh = s => s.replace(/Street/g, 'St').replace(/Boulevard/g, 'Blvd').replace(/Avenue/g, 'Ave').replace(/Road/g, 'Rd').replace(/Parkway/g, 'Pkwy').replace(/Expressway/g, 'Expy').replace(/North |South |East |West /g, (m) => m[0] + ' ');
const race = c => c.kind === 'circuit' ? c.length : c.kind === 'drag' ? c.runM : c.length - (c.leadM || 60) - (c.shutM || 250);
const laps = c => { const L = c.length / 1000; let n = Math.max(1, Math.min(5, Math.floor(13 / L))); while (n > 1 && n * L > 14.2) n--; return n; };
console.log('| # | Type | Name | District | Race | Corners (tight/medium/fast) | Lanes per direction (share with 2+) | Width m | Height m (climb, steepest) | Signals | Side streets | Start | Status | Roads |');
console.log('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|');
for (const c of cands) {
  const r = race(c) / 1000;
  const rs = c.kind === 'circuit' ? `${r.toFixed(2)} km lap, ${laps(c)} lap${laps(c) > 1 ? 's' : ''} = ${(r * laps(c)).toFixed(1)} km` : c.kind === 'drag' ? `${c.runM} m (${c.runM < 500 ? '1/4' : '1/2'} mile)` : `${r.toFixed(2)} km`;
  const g = c.grid;
  const gs = c.kind === 'circuit' ? `${g.len} m straight on ${sh(g.road || 'the freeway')}` : g.today ? "today's start" : g.closedOnly ? `${g.len} m straight; 2-lane road, closed road only` : `${g.len} m straight, ${g.lanes} lanes`;
  const at = g.at ? ` (${toLat(g.at[1]).toFixed(4)}, ${toLon(g.at[0]).toFixed(4)})` : '';
  const names = [...new Set(c.names.map(sh))].join(' > ');
  console.log(`| ${c.id} | ${c.kind} | ${c.name} | ${c.district} | ${rs} | ${c.corners} (${c.tight}/${c.medium}/${c.fast}) | ${c.lanes.min}-${c.lanes.max} (${c.lanes.twoPlusPct}%) | ${c.width.min}-${c.width.max} | ${c.elev.min}-${c.elev.max} (+${c.elev.climb}, ${c.elev.maxGrade}%) | ${c.signals} | ${c.mouths} | ${gs}${at} | ${status(c)} | ${names} |`);
}
