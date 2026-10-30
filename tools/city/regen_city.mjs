// regen_city.mjs - after the roads or the footprints change, remake EVERY file
// made from the city export, in order, and check them all (city fuel r1,
// 2026-10-30: the coordinator's merge order, as one command).
//
//   node tools/city/regen_city.mjs                 export, stations, signs, then the three --checks
//   node tools/city/regen_city.mjs --no-export     the export is already written: stations, signs, checks
//   node tools/city/regen_city.mjs --print         say the commands, run nothing
//   node tools/city/regen_city.mjs -- <args>       pass <args> on to export_osm.mjs (both its --out and --check)
//
// THE ORDER, and why:
//   1. export_osm.mjs --out Resources --fingerprint tools/city/fingerprint.json
//        charlotte_city.bytes (the graph), charlotte_bld.bytes (the footprints),
//        charlotte_dem.bytes, charlotte_decks.bytes; the fingerprint and
//        baseline/culverts_q8.csv beside them (commit both with the export)
//   2. stations.mjs --out Resources      charlotte_fuel.bytes: stamped with the graph
//        hash and the footprints' CRC; its frontage edges, curb cuts and replaced
//        boxes index the files step 1 wrote
//   3. signs.mjs --out Resources         charlotte_signs.bytes: the roadside signs
//        (it keeps fuel POIs off the frontage, the stations stand there)
//   4. export_osm.mjs --check, stations.mjs --check, signs.mjs --check
// It stops at the first step that fails and says which. A worktree points at
// main's cache with PSX_CITY_CACHE (and PSX_GIS_DIR), as every tool here does;
// this passes the environment on unchanged. Nothing is fetched.

import { spawnSync } from 'node:child_process';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO = join(HERE, '..', '..');
const RES = join(REPO, 'Assets', 'PSXRacing', 'Resources');
const argv = process.argv.slice(2);
const dash = argv.indexOf('--');
const own = dash >= 0 ? argv.slice(0, dash) : argv;
const exportExtra = dash >= 0 ? argv.slice(dash + 1) : [];
const noExport = own.includes('--no-export'), printOnly = own.includes('--print');
const unknown = own.filter(a => a !== '--no-export' && a !== '--print');
if (unknown.length) {
  console.error('regen_city.mjs: unknown ' + unknown.join(' ') + ' (export_osm arguments go after --)');
  process.exit(2);
}

const tool = f => join(HERE, f);
const steps = [];
if (!noExport) steps.push(['export', [tool('export_osm.mjs'), '--out', RES, '--fingerprint', join(HERE, 'fingerprint.json'), ...exportExtra]]);
steps.push(['stations', [tool('stations.mjs'), '--out', RES]]);
steps.push(['signs', [tool('signs.mjs'), '--out', RES]]);
steps.push(['export --check', [tool('export_osm.mjs'), '--check', ...exportExtra]]);
steps.push(['stations --check', [tool('stations.mjs'), '--check']]);
steps.push(['signs --check', [tool('signs.mjs'), '--check']]);

const show = a => a.map(x => (x.startsWith(REPO) ? relative(REPO, x) : x)).map(x => (/\s/.test(x) ? '"' + x + '"' : x)).join(' ');
const t0 = Date.now();
for (const [name, args] of steps) {
  console.log('\n==== regen_city: ' + name + '\n     node ' + show(args));
  if (printOnly) continue;
  const r = spawnSync(process.execPath, args, { cwd: REPO, stdio: 'inherit', env: process.env });
  if (r.status !== 0) {
    console.error('\nREGEN FAILED at "' + name + '" (exit ' + (r.status ?? r.signal) + '): fix it, then run regen_city.mjs again' +
                  (name === 'export' ? '' : ' (--no-export: the export is written)'));
    process.exit(1);
  }
}
if (printOnly) process.exit(0);
const changed = spawnSync('git', ['status', '--short', '--', 'Assets/PSXRacing/Resources', 'tools/city/fingerprint.json', 'tools/city/baseline'],
                          { cwd: REPO, encoding: 'utf8' });
console.log('\nREGEN OK in ' + ((Date.now() - t0) / 1e3).toFixed(0) + ' s' +
            (changed.status === 0 ? (changed.stdout.trim() ? '; changed (commit them together):\n' + changed.stdout.trimEnd()
                                                         : '; nothing changed') : ''));
