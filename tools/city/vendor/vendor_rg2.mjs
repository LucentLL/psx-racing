// vendor_rg2.mjs - copy the three Racing-Game-2 inputs of the Charlotte export
// into tools/city/vendor/rg2/, so export_osm.mjs no longer needs an RG2
// checkout (plan WP-02). Run once; the output is committed. See
// tools/city/vendor/README.md for what each file is and where it came from.
//
//   node tools/city/vendor/vendor_rg2.mjs [path/to/Racing-Game-2]
//
// baselineWater.ts is copied byte for byte (its sha256 stays the RG2 file's).
// Of the other two only the I-485 row is ever read (the water fit's ICP pairs
// RG2's legacy I-485 trace with RG2's OSM I-485 row), so i485_fit.json keeps
// just those two rows - numbers through JSON round-trip exactly - and records
// the sha256 and commit of the files they were cut from.

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'rg2');
const RG2 = process.argv[2] || 'C:/Users/mcgee/code/Racing-Game-2';
const sha256 = b => createHash('sha256').update(b).digest('hex');
const commitOf = rel => {
  try { return execFileSync('git', ['-C', RG2, 'log', '-1', '--format=%H %cs', '--', rel], { encoding: 'utf8' }).trim(); }
  catch { return null; }
};

/// The array literal exported as `exportName` from a .ts file (the same
/// reader export_osm.mjs uses).
function tsArray(src, exportName) {
  const at = src.indexOf(exportName);
  if (at < 0) throw new Error(`${exportName} not found`);
  const eq = src.indexOf('=', at);
  const open = src.indexOf('[', eq);
  let depth = 0, i = open;
  for (; i < src.length; i++) {
    if (src[i] === '[') depth++;
    else if (src[i] === ']') { depth--; if (depth === 0) break; }
  }
  return new Function(`return ${src.slice(open, i + 1)};`)();
}

mkdirSync(OUT, { recursive: true });

const waterRel = 'src/config/world/baselineWater.ts';
const water = readFileSync(join(RG2, waterRel));
writeFileSync(join(OUT, 'baselineWater.ts'), water);

const roadsRel = 'src/config/world/baselineRoads.ts';
const roads = readFileSync(join(RG2, roadsRel));
const legacy = tsArray(roads.toString('utf8'), 'BASELINE_ROADS').find(r => r[2] === 'I-485');
const rowsRel = 'fixtures/osm/charlotte_rows.json';
const rowsBuf = readFileSync(join(RG2, rowsRel));
const osm = JSON.parse(rowsBuf.toString('utf8')).rows.find(r => r[2] === 'I-485');
if (!legacy || !osm) throw new Error('an I-485 row is missing');

const fit = {
  note: 'The two I-485 rows the Charlotte water fit (export_osm.mjs, "water") registers RG2\'s traced water with: ' +
        'legacy_i485 is RG2\'s hand trace (its tile frame, the row format [width, isMajor, name, z, x1, y1, ...]); ' +
        'osm_i485 is RG2\'s own OSM bake of the same loop ([.., .., name, .., x1, y1, ...] from index 4). ' +
        'Cut from the files below by tools/city/vendor/vendor_rg2.mjs; numbers are exact (JSON round-trip).',
  sources: {
    legacy_i485: { repo: 'Racing-Game-2', file: roadsRel, sha256: sha256(roads), commit: commitOf(roadsRel) },
    osm_i485: { repo: 'Racing-Game-2', file: rowsRel, sha256: sha256(rowsBuf), commit: commitOf(rowsRel),
                licence: 'ODbL 1.0 (derived from OpenStreetMap by RG2 tools/osm/build.mjs)' },
  },
  legacy_i485: legacy,
  osm_i485: osm,
};
writeFileSync(join(OUT, 'i485_fit.json'), JSON.stringify(fit, null, 1) + '\n');
console.log(`vendored from ${RG2}:`);
console.log(`  rg2/baselineWater.ts  ${water.length} B  sha256 ${sha256(water)}  (${commitOf(waterRel)})`);
console.log(`  rg2/i485_fit.json     legacy row ${legacy.length} values, OSM row ${osm.length} values`);
