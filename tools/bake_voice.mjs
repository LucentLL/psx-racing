/**
 * Bake RG2's per-car engine voice into the Unity catalog.
 *
 * The port carried the BAND LADDER across from sampleEngine.ts but not the
 * per-car character that rides on it (engineVoice.ts + iconicVoices.ts), so
 * every car plays its family recording at rate 1.0 with no formant, no level
 * trim and no firing lope. This computes the STOCK voice with RG2's own code
 * — same inputs as gameLoop.ts's call site — and writes it into rg2_cars.json.
 *
 * Usage, from this directory:
 *
 *   # 1. bundle RG2's audio + catalog modules (they are TypeScript)
 *   cd C:/Users/mcgee/code/Racing-Game-2
 *   npx esbuild tools/audiolab/voiceentry.ts --bundle --alias:@=./src \
 *       --format=esm --outfile="C:/Users/mcgee/PSX Racing/tools/ve.mjs"
 *
 *   # 2. bake (ve.mjs is a build artefact, not checked in)
 *   cd "C:/Users/mcgee/PSX Racing/tools"
 *   node bake_voice.mjs --check     # report, write nothing
 *   node bake_voice.mjs             # rewrite Resources/rg2_cars.json in place
 *
 * Re-run whenever RG2's voice rules change. It preserves the file's 1-space
 * JSON formatting so the diff is only the fields it owns.
 */
import fs from 'node:fs';
import * as rg2 from './ve.mjs';

const UNITY = 'C:/Users/mcgee/PSX Racing/Assets/PSXRacing/Resources/rg2_cars.json';
const check = process.argv.includes('--check');

const byId = rg2.CAR_CATALOG;                       // keyed by id
const bundle = JSON.parse(fs.readFileSync(UNITY, 'utf8'));
const cars = bundle.cars;

let missing = 0, lopes = 0, pitched = 0;
const r4 = (v) => Math.round(v * 10000) / 10000;
const r2 = (v) => Math.round(v * 100) / 100;

for (const u of cars) {
  const c = byId[u.id];
  if (!c) { missing++; console.warn('no RG2 row for ' + u.id); continue; }
  const fam = rg2.resolveEngineFamily(c);
  const v = rg2.computeEngineVoice({
    id: c.id, name: c.name, redline: c.redline, hp: c.hp, weight: c.kg,
    aspiration: c.asp, idleRPM: c.idleRPM,
    cc: rg2.carVoiceCc(c.name, c.eType),
    familyMedianCc: rg2.familyMedianCc(fam),
    eType: c.eType, modelYear: c.modelYear,
  }, { exhaustLevel: 0, straightPipe: false });        // STOCK — the C# side walks the ladder

  u.voiceRateMul = r4(v.rateMul);
  u.voiceLevelMul = r4(v.levelMul);
  u.voicePeakHz = Math.round(v.peakHz);
  u.voicePeakDb = r2(v.peakDb);
  u.voiceShelfDb = r2(v.shelfDb);
  u.lopeDepth = v.lope ? r4(v.lope.depth) : 0;
  u.lopeOrder = v.lope ? v.lope.order : 0.5;
  u.lopeFadeTop = v.lope ? Math.round(v.lope.fadeTop) : 0;
  u.lopePhase = v.lope ? r4(v.lope.phase) : 0;
  if (v.lope) lopes++;
  if (Math.abs(v.rateMul - 1) > 0.02) pitched++;
  // Sanity: the RG2 family key must match what the port already baked.
  if (u.engineFamily && u.engineFamily !== fam) {
    console.warn(`family drift ${u.id}: unity=${u.engineFamily} rg2=${fam}`);
  }
}

const rates = cars.map((c) => c.voiceRateMul).filter(Boolean).sort((a, b) => a - b);
console.log(`${cars.length} cars, ${missing} unmatched, ${lopes} with a firing lope, ` +
  `${pitched} pitched more than 2% off 1.0`);
console.log(`rateMul  min ${rates[0]}  p50 ${rates[rates.length >> 1]}  max ${rates[rates.length - 1]}`);
for (const id of ['dodge_charger_440_r_t__70', 'dodge_charger_super_bee_426_hemi__71',
  'mazda_rx_7_type_rs__99', 'honda_s2000__99']) {
  const c = cars.find((x) => x.id === id);
  if (c) {
    console.log(`  ${c.name.padEnd(40)} fam=${(c.engineFamily || '-').padEnd(24)} ` +
      `rate=${c.voiceRateMul} lvl=${c.voiceLevelMul} peak=${c.voicePeakHz}Hz/${c.voicePeakDb}dB ` +
      `shelf=${c.voiceShelfDb}dB lope=${c.lopeDepth}@${c.lopeFadeTop}`);
  }
}

if (!check) {
  fs.writeFileSync(UNITY, JSON.stringify(bundle, null, 1));
  console.log('wrote ' + UNITY + ' (' + fs.statSync(UNITY).size + ' bytes)');
} else {
  console.log('--check: nothing written');
}
