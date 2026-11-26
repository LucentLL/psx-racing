// The converter refuses a 5A car over either triangle cap (carparts stage 2).
// Runs export_models.mjs on the two-door test car with each cap lowered below
// the car's own count and expects a failure naming the cap; then once at the
// real caps and expects success (which re-writes the test car's files).
//
//   node tools/carmodels/test/test_caps.mjs
import { spawnSync } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const exporter = path.resolve(here, '..', 'export_models.mjs');
const run = env => spawnSync(process.execPath, [exporter, 'test5a_2door'], { env: { ...process.env, ...env }, encoding: 'utf8' });

let bad = 0;
const cases = [
  ['closed set over its cap', { CM_CAP_CLOSED: '400' }, /closed set \d+ tris over the cap 400/],
  ['open-only set over its cap', { CM_CAP_OPEN: '100' }, /open-only set \d+ tris over the 2-door cap 100/],
];
for (const [what, env, want] of cases) {
  const r = run(env);
  const ok = r.status !== 0 && want.test(r.stderr + r.stdout);
  if (!ok) bad++;
  console.log(`${ok ? 'ok  ' : 'FAIL'} the converter refuses a car with its ${what} (exit ${r.status}: ${(r.stderr.match(/Error: (.*)/) || [, '-'])[1]})`);
}
const r = run({});
const ok = r.status === 0 && /5A 2-door: closed set \d+ tris \(cap 10000\), open-only set \d+ tris \(cap 3000\)/.test(r.stdout);
if (!ok) bad++;
console.log(`${ok ? 'ok  ' : 'FAIL'} at the real caps the same car converts (exit ${r.status})`);
console.log(bad ? `CAPS TEST FAILED (${bad})` : 'CAPS TEST OK');
process.exit(bad ? 1 : 0);
