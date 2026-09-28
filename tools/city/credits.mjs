// credits.mjs - the game's data credits, generated from tools/city/SOURCES.md
// (plan critic C17: a CREDITS page in the pause menu and a LICENSES.txt next
// to index.html on Pages, both from the one registry).
//
//   node tools/city/credits.mjs            check both files are up to date (exit 1 if not)
//   node tools/city/credits.mjs --write    write them
//
// Writes:
//   Assets/PSXRacing/Resources/psx_credits.txt      the pause menu's CREDITS page
//                                                   (CreditsPanel reads it)
//   Assets/WebGLTemplates/PSXMobile/LICENSES.txt    copied into every WebGL build
//                                                   beside index.html
// Edit SOURCES.md's Credits table, never these files; then run --write, and
// export_osm.mjs --out ... so the attribution inside the data matches too.

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readCredits, creditsPage, licensesFile } from './lib/sources.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const rows = readCredits(join(HERE, 'SOURCES.md'));
const files = [
  [join(UNITY, 'Assets', 'PSXRacing', 'Resources', 'psx_credits.txt'), creditsPage(rows)],
  [join(UNITY, 'Assets', 'WebGLTemplates', 'PSXMobile', 'LICENSES.txt'), licensesFile(rows)],
];
const write = process.argv.includes('--write');
let stale = 0;
for (const [path, text] of files) {
  const rel = relative(UNITY, path).replace(/\\/g, '/');
  if (write) { writeFileSync(path, text); console.log(`wrote ${rel} (${text.length} B)`); continue; }
  // compare ignoring line endings: git may check a .txt out with CRLF
  const have = existsSync(path) ? readFileSync(path, 'utf8').replace(/\r\n/g, '\n') : null;
  if (have === text) console.log(`  up to date  ${rel}`);
  else { console.log(`  ${have == null ? 'MISSING' : 'STALE'}  ${rel}`); stale++; }
}
if (!write) {
  console.log(stale ? `CREDITS: ${stale} file(s) out of date - run node tools/city/credits.mjs --write` : `CREDITS OK (${rows.map(r => r.id).join(', ')})`);
  process.exitCode = stale ? 1 : 0;
}
