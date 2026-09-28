// determinism.mjs - the Charlotte export's determinism test (plan WP-02):
// run tools/city/export_osm.mjs twice, in two separate Node processes, into
// two scratch directories, and compare every output file's sha256. Exit 0
// only when both runs wrote the same set of files with the same bytes.
//
//   node tools/city/determinism.mjs [--keep]
//
// Two processes, not two calls in one: what this is for is anything that
// varies between runs - Map/Set order fed by timing, Date, Math.random, a
// parallel fetch finishing in a different order - and a second call inside
// one process would share the first one's state. The scratch directories are
// made under the OS temp dir and removed afterwards (--keep leaves them).
// About 25 s. --check (byte compare with the shipped files) is the other
// half: this says the export is a function of its inputs, --check says the
// shipped files are that function's output.

import { spawnSync } from 'node:child_process';
import { mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const EXPORT = join(HERE, 'export_osm.mjs');
const keep = process.argv.includes('--keep');
const sha256 = b => createHash('sha256').update(b).digest('hex');

const root = mkdtempSync(join(tmpdir(), 'psx-city-determinism-'));
const runs = [];
let failed = false;
try {
  for (const tag of ['a', 'b']) {
    const out = join(root, tag);
    const t0 = Date.now();
    const r = spawnSync(process.execPath, [EXPORT, '--out', out, '--no-plots'], { encoding: 'utf8', maxBuffer: 64 << 20 });
    if (r.status !== 0) {
      console.log(`run ${tag}: export_osm.mjs exited ${r.status}\n${(r.stderr || '').slice(-2000)}`);
      failed = true;
      break;
    }
    const files = new Map();
    for (const f of readdirSync(out).sort()) files.set(f, sha256(readFileSync(join(out, f))));
    runs.push(files);
    console.log(`run ${tag}: ${files.size} files in ${((Date.now() - t0) / 1000).toFixed(1)} s`);
  }
  if (!failed) {
    const [a, b] = runs;
    for (const name of new Set([...a.keys(), ...b.keys()])) {
      const x = a.get(name), y = b.get(name);
      if (x && x === y) console.log(`  SAME       ${name}  sha256 ${x}`);
      else { console.log(`  DIFFERENT  ${name}  ${x || '(missing)'}  vs  ${y || '(missing)'}`); failed = true; }
    }
  }
} finally {
  if (keep) console.log(`kept ${root}`);
  else rmSync(root, { recursive: true, force: true });
}
console.log(failed ? 'DETERMINISM: FAILED' : 'DETERMINISM OK: two exports in two processes wrote identical bytes');
process.exitCode = failed ? 1 : 0;
