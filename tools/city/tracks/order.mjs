// order.mjs - THE INTEGRATOR'S STEP: the Charlotte tracks' place in TrackCatalog.All.
//
// A saved career stores its venue by INDEX, so a venue may only ever be appended. The game lays the
// track files' venues (Resources/CityTracks/*.json: the forward venue, then its "Rev" twin) after every
// authored venue, in the order of Resources/CityTracks/_order.txt, then every venue that file does not
// list yet, by file name. Lanes never edit _order.txt (two branches appending to one file conflict);
// the integrator, after merging them, appends the new ones here, in that same by-file-name order, so
// nothing a lane raced moves and nothing shipped ever moves:
//
//   node tools/city/tracks/order.mjs           what is listed, what is not (the game appends it after)
//   node tools/city/tracks/order.mjs --write   append the unlisted venues to _order.txt (then commit it)
//
// A listed venue with no file is an ERROR: every venue after it would move down one index.
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { WT } from './lib.mjs';
import { tracksDir, readTracks, readOrder, venueIdsOf, ORDER_FILE } from './trackfiles.mjs';

const tracks = readTracks(WT);
const listed = readOrder(WT);
const made = [];
for (const t of tracks) for (const v of venueIdsOf(t)) made.push({ v, file: t.file });
const have = new Set(made.map(m => m.v));
let bad = 0;
for (const v of listed) if (!have.has(v)) { console.log(`ERROR ${v} is in ${ORDER_FILE} but no track file makes it: every venue after it moves down one index (put its file back)`); bad++; }
const seen = new Set();
for (const v of listed) { if (seen.has(v)) { console.log(`ERROR ${v} is listed twice`); bad++; } seen.add(v); }
const unlisted = made.filter(m => !seen.has(m.v));
console.log(`${tracks.length} track files, ${made.length} venues; ${listed.length} listed in ${ORDER_FILE}, ${unlisted.length} not yet:`);
for (const m of unlisted) console.log(`  + ${m.v}   (${m.file})`);
if (process.argv.includes('--write') && unlisted.length) {
  if (bad) { console.log('nothing written: fix the errors first'); process.exit(1); }
  const p = join(tracksDir(WT), ORDER_FILE);
  let text = existsSync(p) ? readFileSync(p, 'utf8') : '';
  if (text && !text.endsWith('\n')) text += '\n';
  text += unlisted.map(m => m.v).join('\n') + '\n';
  writeFileSync(p, text);
  console.log('appended ' + unlisted.length + ' to ' + p + ' (commit it)');
}
process.exit(bad ? 1 : 0);
