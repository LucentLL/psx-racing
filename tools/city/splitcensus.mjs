// THE SPLITS CENSUS, OFFLINE (J1, 2026-11-16): the cross-check of CityAudit.RunSplits.
// A JavaScript replica of the C# line model as PC45 draws it (splitcensus/lm.mjs: LineModel's
// SPLT tapers, RoomAlong, relays, refuge list; the investigation's align/census/lm.mjs), and the
// census over every carriageway end at a split node, measured in the undivided road's straight
// frame (splitcensus/census_a4.mjs). It reads the shipped charlotte_city.bytes and the raw OSM in
// tools/city/cache (PSX_CITY_CACHE: another checkout's cache). It models PC45 only: the splits
// round's axis-frame profile (PSX_CITY_SPLITAXIS) is measured in the editor by CityAudit.RunSplits.
//   PSX_SPLITCENSUS_OUT=<dir> node tools/city/splitcensus.mjs        -> census_a_all.csv, census_a_rows.json
//   node tools/city/splitcensus.mjs --model2                          -> the independent second model (verify-census)
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const which = process.argv.includes('--model2') ? 'model2.mjs' : 'census_a4.mjs';
await import(pathToFileURL(path.join(here, 'splitcensus', which)).href);
