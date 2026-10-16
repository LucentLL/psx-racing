/* Engine audio phase 2: the vendor layers the game did not have yet, encoded
   into the project (RES2 Pro v2.3.1, owner-licensed; the vendor folder is
   read-only and never written).

     - aggressiveness_on/off: the exhaust "fx" layer of every family's HQ
       prefab (aggressivnessOnClip/OffClip), next to the family's other
       exterior takes (EngineSrc/<family>, or Resources/Engines/<default>);
     - Interior/<take>.ogg: every family's cockpit recordings, exactly the
       clips its HQ INTERIOR prefab references, renamed to the exterior take
       names (int_low_on.wav -> Interior/low_on.ogg) plus the interior
       startup / engine_stop; built into <family>_int.bundle;
     - Resources/Crackle: twelve short muffler crackles (the vendor's
       Random_ShortMufflerCrackle sets) and their interior twins, the one-shots
       of the lift-off pops - universal, so in the main data.

   Every .ogg gets a .meta with a fresh GUID and the AudioImporter settings
   the builder would give it (so the GUID is fixed in git, not minted by
   whichever editor imports it first). Existing files are left alone.

   Usage: node tools/import_engine_layers.mjs [--dry]   (needs ffmpeg) */
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { RES2, PREFAB_OF, parsePrefab, loadHQ } from './res2_curves.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '..');
const RES_ENGINES = path.join(REPO, 'Assets/PSXRacing/Resources/Engines');
const SRC = path.join(REPO, 'Assets/PSXRacing/EngineSrc');
const CRACKLE = path.join(REPO, 'Assets/PSXRacing/Resources/Crackle');
const DEFAULT_FAMILY = 'rotary_x7';
const dry = process.argv.includes('--dry');

/** Source .ogg quality (libvorbis -q:a). Exterior takes match the pack import
 *  (RG2 importEnginePack.mjs, q6). Interior takes carry ~23 dB less energy
 *  above 10 kHz (engine_interior_quality.mjs), so q5 holds them. */
const Q_EXT = 6, Q_INT = 5, Q_CRACKLE = 5;
/** Importer quality written into the .meta (EngineBundleBuilder /
 *  PSXRacingBuilder apply the same, guarded, on every build). */
const IMP_EXT = 0.8, IMP_INT = 0.4, IMP_CRACKLE = 0.6;

const INT_DIR = RES2 + '/Prefabs/Engine_Prefabs/High Quality/High_Interior';
export function interiorPrefab(prefab) {
  const want = prefab.toLowerCase() === 'v8_formula' ? 'v8_formula1_int' : prefab.toLowerCase() + '_int';
  const f = fs.readdirSync(INT_DIR).find((n) => n.endsWith('.prefab') && n.toLowerCase().startsWith(want));
  return f ? path.join(INT_DIR, f) : null;
}

/** Prefab clip field -> the game's take name. */
const FIELD = {
  idleClip: 'idle', idle_lowOnClip: 'idle_low_on', idle_lowOffClip: 'idle_low_off',
  lowOnClip: 'low_on', lowOffClip: 'low_off', low_medOnClip: 'low_med_on', low_medOffClip: 'low_med_off',
  medOnClip: 'med_on', medOffClip: 'med_off', med_highOnClip: 'med_high_on', med_highOffClip: 'med_high_off',
  highOnClip: 'high_on', highOffClip: 'high_off', very_highOnClip: 'very_high_on', very_highOffClip: 'very_high_off',
  maxRPMClip: 'maxRPM', intakeOnClip: 'intake_on', intakeOffClip: 'intake_off',
  aggressivnessOnClip: 'aggressiveness_on', aggressivnessOffClip: 'aggressiveness_off',
};

const guid = () => crypto.randomBytes(16).toString('hex');
function audioMeta(quality) {
  return `fileFormatVersion: 2
guid: ${guid()}
AudioImporter:
  externalObjects: {}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: ${quality}
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {}
  forceToMono: 0
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
`;
}
const folderMeta = () => `fileFormatVersion: 2
guid: ${guid()}
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
`;
function ensureDir(dir) {
  if (fs.existsSync(dir)) return;
  if (!dry) { fs.mkdirSync(dir, { recursive: true }); fs.writeFileSync(dir + '.meta', folderMeta()); }
}

let encoded = 0, skipped = 0, bytes = 0;
const missing = [];
function put(wav, dst, q, impQ) {
  if (!wav || !fs.existsSync(wav)) { missing.push(dst); return; }
  if (fs.existsSync(dst)) { skipped++; bytes += fs.statSync(dst).size; return; }
  if (dry) { encoded++; return; }
  execFileSync('ffmpeg', ['-v', 'error', '-y', '-i', wav, '-c:a', 'libvorbis', '-q:a', String(q), dst]);
  fs.writeFileSync(dst + '.meta', audioMeta(impQ));
  encoded++;
  bytes += fs.statSync(dst).size;
}

const fams = [...new Set([...fs.readdirSync(SRC, { withFileTypes: true }).filter((e) => e.isDirectory()).map((e) => e.name), DEFAULT_FAMILY])]
  .filter((f) => PREFAB_OF[f]).sort();
const perFam = [];
for (const fam of fams) {
  const before = bytes;
  const ext = loadHQ(PREFAB_OF[fam]);
  const extDir = fam === DEFAULT_FAMILY ? path.join(RES_ENGINES, fam) : path.join(SRC, fam);
  ensureDir(extDir);
  put(ext.clips.aggressivnessOnClip, path.join(extDir, 'aggressiveness_on.ogg'), Q_EXT, IMP_EXT);
  put(ext.clips.aggressivnessOffClip, path.join(extDir, 'aggressiveness_off.ogg'), Q_EXT, IMP_EXT);
  const aggBytes = bytes - before;

  const ip = interiorPrefab(PREFAB_OF[fam]);
  if (!ip) { missing.push(fam + ' (no interior prefab)'); continue; }
  const inner = parsePrefab(ip);
  const intDir = path.join(SRC, fam, 'Interior');
  ensureDir(path.join(SRC, fam));
  ensureDir(intDir);
  const ib = bytes;
  for (const [field, name] of Object.entries(FIELD)) put(inner.clips[field], path.join(intDir, name + '.ogg'), Q_INT, IMP_INT);
  // startup / engine_stop are not prefab fields; the interior pack carries both.
  const wavDir = path.dirname(inner.clips.idleClip || '');
  put(path.join(wavDir, 'int_startup.wav'), path.join(intDir, 'startup.ogg'), Q_INT, IMP_INT);
  put(path.join(wavDir, 'int_engine_stop.wav'), path.join(intDir, 'engine_stop.ogg'), Q_INT, IMP_INT);
  perFam.push(`${fam.padEnd(24)} aggressiveness ${(aggBytes / 1024).toFixed(0).padStart(4)} KB  interior ${((bytes - ib) / 1024).toFixed(0).padStart(5)} KB  (${path.basename(ip)})`);
}

ensureDir(CRACKLE);
const MUF = RES2 + '/Sounds/Muffler/Short_Cracles';
for (let i = 1; i <= 12; i++) {
  const n = String(i).padStart(2, '0');
  put(`${MUF}/crackle_${i}.wav`, path.join(CRACKLE, `crackle_${n}.ogg`), Q_CRACKLE, IMP_CRACKLE);
  put(`${MUF}/Interior/int_crackle_${i}.wav`, path.join(CRACKLE, `crackle_int_${n}.ogg`), Q_CRACKLE, IMP_CRACKLE);
}

console.log(perFam.join('\n'));
console.log(`${dry ? 'DRY: would encode' : 'encoded'} ${encoded}, kept ${skipped}, ${(bytes / 1048576).toFixed(2)} MB of .ogg in place`);
if (missing.length) console.log('MISSING:', missing.join(', '));
