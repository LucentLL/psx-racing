// audit_check.mjs - cross the candidates' edges with the bridges audit (Docs/CITY_BRIDGES_AUDIT_1115_after.txt)
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const AUD = fileURLToPath(new URL('../../../Docs/CITY_BRIDGES_AUDIT_1115_after.txt', import.meta.url));
const KINDS = new Set(['DECK-HOLE', 'STEP-JOINT', 'STEP', 'KINK-JOINT', 'KINK', 'WALL-IN-LANE', 'RAIL-ON-LANE', 'UNDER-STEP', 'UNDER-KINK', 'RAIL-DROP', 'LOW-CLEAR']);
const hits = new Map();   // edge -> [{kind, line}]
let kind = null;
for (const line of readFileSync(AUD, 'utf8').split(/\r?\n/)) {
  const m = line.match(/^== ([A-Z-]+):/);
  if (m) { kind = m[1]; continue; }
  if (!kind || !KINDS.has(kind)) continue;
  const e = line.match(/^\s+([\d.]+)\s+e(\d+)\s+'([^']*)'/);
  if (!e) continue;
  const ei = +e[2];
  if (!hits.has(ei)) hits.set(ei, []);
  hits.get(ei).push({ kind, mag: +e[1], name: e[3] });
}
const f = new URL('./out/cands.json', import.meta.url);
const cands = JSON.parse(readFileSync(f, 'utf8'));
for (const c of cands) {
  c.auditHits = [];
  for (const [ei] of c.chain) for (const h of hits.get(ei) || []) c.auditHits.push({ ei, ...h });
  if (c.auditHits.length) console.log(c.id, c.auditHits.map(h => `${h.kind} ${h.mag} e${h.ei} ${h.name}`).join('; '));
}
writeFileSync(f, JSON.stringify(cands));
console.log('audit edges listed', hits.size);
