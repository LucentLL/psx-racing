// credits.mjs - the game's data credits, generated from tools/city/SOURCES.md
// (plan critic C17: a CREDITS page in the pause menu and a LICENSES.txt next
// to index.html on Pages, both from the one registry), PER EDITION: MAIN (the
// site root) credits the stages' data, CITY (/city/) Charlotte's, ALL both
// (SOURCES.md, "THE EDITIONS").
//
//   node tools/city/credits.mjs            check every file is up to date (exit 1 if not)
//   node tools/city/credits.mjs --write    write them (and a .meta for a new one)
//   node tools/city/credits.mjs --build <Build/WebGL> [--edition MAIN|CITY|ALL]
//       check a FINISHED build carries its own edition's LICENSES.txt and no
//       other edition's (the edition defaults to the build's psx-edition.txt;
//       tools/build-and-publish.ps1 runs this before a deploy)
//
// Writes:
//   Assets/PSXRacing/Resources/psx_credits.txt       ALL's CREDITS page     } the pause menu's
//   Assets/PSXRacing/Resources/psx_credits_main.txt  MAIN's CREDITS page    } CREDITS (CreditsPanel.Text
//   Assets/PSXRacing/Resources/psx_credits_city.txt  CITY's CREDITS page    } loads the build's own)
//   Assets/PSXRacing/Resources/psx_credits_line.txt  the one-line credit per edition
//                                                    (CreditsPanel.Line; the CITY front page)
//   Assets/WebGLTemplates/PSXMobile/LICENSES.txt       ALL's  } copied into every WebGL build;
//   Assets/WebGLTemplates/PSXMobile/LICENSES-MAIN.txt  MAIN's } PSXBuildWebGL.PickLicenses keeps
//   Assets/WebGLTemplates/PSXMobile/LICENSES-CITY.txt  CITY's } the edition's as LICENSES.txt
// Edit SOURCES.md's Credits table, never these files; then run --write, and
// export_osm.mjs --out ... so the attribution inside the data matches too.

import { readFileSync, writeFileSync, existsSync, readdirSync } from 'node:fs';
import { randomBytes } from 'node:crypto';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readCredits, creditsPage, linesFile, licensesFile, rowsFor, EDITIONS } from './lib/sources.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const UNITY = join(HERE, '..', '..');
const RES = join(UNITY, 'Assets', 'PSXRacing', 'Resources');
const TPL = join(UNITY, 'Assets', 'WebGLTemplates', 'PSXMobile');
const ALL_EDITIONS = ['ALL', ...EDITIONS];
const rows = readCredits(join(HERE, 'SOURCES.md'));
const args = process.argv.slice(2);
const argOf = k => (args.indexOf(k) >= 0 ? args[args.indexOf(k) + 1] || '' : null);
const norm = s => s.replace(/\r\n/g, '\n');   // git may check a .txt out with CRLF

// ---- --build: what a finished build carries ----
const buildDir = argOf('--build');
if (buildDir !== null) {
  if (!buildDir || !existsSync(buildDir)) { console.log(`CREDITS BUILD CHECK FAILED - no build at "${buildDir}"`); process.exit(1); }
  const edFile = join(buildDir, 'psx-edition.txt');
  let ed = argOf('--edition');
  if (!ed) ed = existsSync(edFile) ? readFileSync(edFile, 'utf8').trim() : 'ALL';   // before the editions: the whole game
  ed = ed.toUpperCase();
  if (!ALL_EDITIONS.includes(ed)) { console.log(`CREDITS BUILD CHECK FAILED - edition "${ed}" is not ${ALL_EDITIONS.join(', ')}`); process.exit(1); }
  const problems = [];
  const lic = join(buildDir, 'LICENSES.txt');
  if (!existsSync(lic)) problems.push('the build has no LICENSES.txt');
  else {
    const have = norm(readFileSync(lic, 'utf8'));
    if (have !== licensesFile(rows, ed)) {
      const is = ALL_EDITIONS.find(e => have === licensesFile(rows, e));
      problems.push(is ? `its LICENSES.txt is ${is}'s, not ${ed}'s`
                       : `its LICENSES.txt is not ${ed}'s as tools/city/SOURCES.md has it now (an older registry, or a template that was not regenerated: credits.mjs --write, then rebuild)`);
    }
  }
  for (const f of readdirSync(buildDir))
    if (/^LICENSES-.*\.txt$/i.test(f)) problems.push(`${f} is still beside index.html (PickLicenses keeps only the edition's, as LICENSES.txt)`);
  if (problems.length) {
    for (const p of problems) console.log('  ' + p);
    console.log(`CREDITS BUILD CHECK FAILED - ${buildDir} (${ed})`);
    process.exit(1);
  }
  console.log(`CREDITS BUILD CHECK OK - ${buildDir} carries ${ed}'s LICENSES.txt (${rowsFor(rows, ed).map(r => r.id).join(', ')})`);
  process.exit(0);
}

// ---- the generated files ----
const files = [
  [join(RES, 'psx_credits.txt'), creditsPage(rows, 'ALL')],
  [join(RES, 'psx_credits_main.txt'), creditsPage(rows, 'MAIN')],
  [join(RES, 'psx_credits_city.txt'), creditsPage(rows, 'CITY')],
  [join(RES, 'psx_credits_line.txt'), linesFile(rows)],
  [join(TPL, 'LICENSES.txt'), licensesFile(rows, 'ALL')],
  [join(TPL, 'LICENSES-MAIN.txt'), licensesFile(rows, 'MAIN')],
  [join(TPL, 'LICENSES-CITY.txt'), licensesFile(rows, 'CITY')],
];
// A .meta for a new file, as Unity writes one for a text asset. It is
// committed with the file: a GUID minted only in a sandbox dies on the next
// mirror (Docs, the GUID churn notes).
const meta = () => ['fileFormatVersion: 2', `guid: ${randomBytes(16).toString('hex')}`, 'TextScriptImporter:',
                    '  externalObjects: {}', '  userData: ', '  assetBundleName: ', '  assetBundleVariant: ', ''].join('\n');
const write = args.includes('--write');
let problems = 0;
for (const [path, text] of files) {
  const rel = relative(UNITY, path).replace(/\\/g, '/');
  if (write) {
    writeFileSync(path, text); console.log(`wrote ${rel} (${text.length} B)`);
    if (!existsSync(path + '.meta')) { writeFileSync(path + '.meta', meta()); console.log(`wrote ${rel}.meta (new)`); }
    continue;
  }
  const have = existsSync(path) ? norm(readFileSync(path, 'utf8')) : null;
  if (have === text) console.log(`  up to date  ${rel}`);
  else { console.log(`  ${have == null ? 'MISSING' : 'STALE'}  ${rel}`); problems++; }
  if (have != null && !existsSync(path + '.meta')) { console.log(`  NO .meta    ${rel}`); problems++; }
}
// Anything else wearing these names is a leftover that would ship (or, as a
// LICENSES-<X>.txt, be picked) as if it were current.
const known = new Set(files.map(f => f[0].toLowerCase()));
for (const [dir, re] of [[RES, /^psx_credits.*\.txt$/i], [TPL, /^LICENSES.*\.txt$/i]])
  for (const f of existsSync(dir) ? readdirSync(dir) : [])
    if (re.test(f) && !known.has(join(dir, f).toLowerCase())) {
      console.log(`  UNEXPECTED  ${relative(UNITY, join(dir, f)).replace(/\\/g, '/')} (not written by this script: delete it and its .meta)`);
      problems++;
    }
if (!write || problems) {
  console.log(problems
    ? `CREDITS: ${problems} problem(s) - run node tools/city/credits.mjs --write (and remove anything UNEXPECTED)`
    : `CREDITS OK (${ALL_EDITIONS.map(e => `${e}: ` + rowsFor(rows, e).map(r => r.id).join(' ')).join('; ')})`);
  process.exitCode = problems ? 1 : 0;
}
