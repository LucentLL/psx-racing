// profile.mjs — ASCII elevation profile of a baked stage, so a synthesised
// bridge deck can be LOOKED AT rather than trusted. Prints grade and plan
// curvature alongside, because "it is a smooth hump" and "it is a smooth hump
// that also turns" are different bakes.
//
//   node tools/bogue/profile.mjs bogue_langston

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const id = process.argv[2] || 'bogue_langston';
const j = JSON.parse(readFileSync(
  join(here, '..', '..', 'Assets', 'PSXRacing', 'Resources', id + '.json'), 'utf8'));

const n = j.xyz.length / 3;
const P = i => ({ x: j.xyz[i * 3], y: j.xyz[i * 3 + 1], z: j.xyz[i * 3 + 2] });
const sp = j.spacing;

console.log(`${j.name}`);
console.log(`  ${n} waypoints at ${sp} m = ${(n * sp / 1000).toFixed(2)} km`);
console.log(`  start line ${j.startLineM} m, finish ${j.finishM} m ` +
  `(race ${(j.finishM - j.startLineM).toFixed(1)} m)`);
console.log(`  water plane y=${j.waterY}, bridges ${JSON.stringify(j.bridges)}`);

const ys = Array.from({ length: n }, (_, i) => P(i).y);
const lo = Math.min(...ys), hi = Math.max(...ys);
const ROWS = 18, COLS = 96;
const grid = Array.from({ length: ROWS }, () => new Array(COLS).fill(' '));
for (let c = 0; c < COLS; c++) {
  const i = Math.round(c * (n - 1) / (COLS - 1));
  const r = ROWS - 1 - Math.round((ys[i] - lo) / Math.max(hi - lo, 0.001) * (ROWS - 1));
  grid[r][c] = '#';
  // waterline
  const wr = ROWS - 1 - Math.round((j.waterY - lo) / Math.max(hi - lo, 0.001) * (ROWS - 1));
  if (wr >= 0 && wr < ROWS && grid[wr][c] === ' ') grid[wr][c] = '~';
}
const mark = (m, ch) => {
  const c = Math.round((m / sp) * (COLS - 1) / (n - 1));
  if (c >= 0 && c < COLS) for (let r = 0; r < ROWS; r++) if (grid[r][c] === ' ') grid[r][c] = ch;
};
mark(j.startLineM, '|');
mark(j.finishM, '!');
console.log(`\n  ${hi.toFixed(1).padStart(5)} m`);
for (const row of grid) console.log('        ' + row.join(''));
console.log(`  ${lo.toFixed(1).padStart(5)} m   ` +
  `(| start, ! finish, ~ sea level, # road)`);

// grade + plan curvature
let worstG = 0, worstGAt = 0, worstK = 0, worstKAt = 0;
for (let i = 1; i < n; i++) {
  const g = Math.abs(P(i).y - P(i - 1).y) / sp;
  if (g > worstG) { worstG = g; worstGAt = i * sp; }
}
for (let i = 2; i < n - 2; i++) {
  // circumradius of a plan triple two apart — same measure the self-test uses
  const a = P(i - 2), b = P(i), c = P(i + 2);
  const ab = Math.hypot(b.x - a.x, b.z - a.z), bc = Math.hypot(c.x - b.x, c.z - b.z);
  const ca = Math.hypot(a.x - c.x, a.z - c.z);
  const area = Math.abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) / 2;
  if (area < 1e-6) continue;
  const R = ab * bc * ca / (4 * area);
  const k = 1 / R;
  if (k > worstK) { worstK = k; worstKAt = i * sp; }
}
console.log(`\n  max grade ${(worstG * 100).toFixed(2)}% at ${worstGAt.toFixed(0)} m`);
console.log(`  tightest plan radius ${(1 / worstK).toFixed(0)} m at ${worstKAt.toFixed(0)} m`);

// straightness and grade ACROSS THE RACE ONLY — the lead-in and the shutdown
// are allowed to turn off onto a causeway, and judging the venue on them
// would condemn every bridge here.
const i0 = Math.round(j.startLineM / sp), i1 = Math.round(j.finishM / sp);
let raceMinR = Infinity, riseM = 0;
for (let i = Math.max(2, i0); i <= Math.min(n - 3, i1); i++) {
  const a = P(i - 2), b = P(i), c = P(i + 2);
  const ab = Math.hypot(b.x - a.x, b.z - a.z), bc = Math.hypot(c.x - b.x, c.z - b.z);
  const ca = Math.hypot(a.x - c.x, a.z - c.z);
  const area = Math.abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) / 2;
  if (area > 1e-6) raceMinR = Math.min(raceMinR, ab * bc * ca / (4 * area));
}
for (let i = i0 + 1; i <= i1; i++) riseM += Math.max(0, P(i).y - P(i - 1).y);
console.log(`  ON THE RACE: tightest plan radius ` +
  `${raceMinR > 1e5 ? 'straight' : raceMinR.toFixed(0) + ' m'}, total climb ${riseM.toFixed(1)} m`);
