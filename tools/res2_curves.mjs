/* The vendor rig's per-clip curves, read straight out of its HQ prefab YAML.

   The pack ships each engine as a prefab whose RealisticEngineSound component
   carries one volume AnimationCurve and one pitch AnimationCurve per take, all
   over clipsValue = rpm / maxRPMLimit, plus onLoad/offLoadVolumeByRPM (the
   load-axis level), maxRPMVolCurve (the limiter take), the intake and
   aggressiveness layers and a handful of scalars. Those curves ARE the tuning:
   they say where on the rev range each recording is meant to sit and how far
   it is allowed to stretch. This module parses them, evaluates them exactly as
   Unity does (cubic Hermite per segment, clamped outside the keys - every key
   in the pack is non-weighted, checked), and can bake a table for the game.

   Usage:
     node tools/res2_curves.mjs --bake      writes Assets/PSXRacing/Resources/res2_rig.json
     node tools/res2_curves.mjs --dump Rotary_x8_Free    prints one prefab's table
   Imported by tools/audio_render_probe.mjs for the offline vendor-rig render.
   Read-only on the vendor folder; never writes there. */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const RES2 = 'C:/Users/mcgee/code/RES2_v2.3.1/Assets/RealisticEngineSound/Assets';  // v2.3.1 (2.2.0 lives under Racing-Game-2/sfx; engine curves are identical)
const HQ = RES2 + '/Prefabs/Engine_Prefabs/High Quality';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '..');

/* ---- guid -> sound file, from the .meta files beside every WAV ------------- */
let guidMap = null;
export function soundByGuid(guid) {
  if (!guidMap) {
    guidMap = new Map();
    const walk = (d) => {
      for (const e of fs.readdirSync(d, { withFileTypes: true })) {
        const p = d + '/' + e.name;
        if (e.isDirectory()) walk(p);
        else if (e.name.endsWith('.wav.meta')) {
          const m = /guid: ([0-9a-f]{32})/.exec(fs.readFileSync(p, 'utf8'));
          if (m) guidMap.set(m[1], p.slice(0, -5));
        }
      }
    };
    walk(RES2 + '/Sounds');
  }
  return guidMap.get(guid) || null;
}

/* ---- prefab parse ---------------------------------------------------------- */
const CURVE_RE = /^  (\w+):\n    serializedVersion: 2\n    m_Curve:(?: \[\])?\n((?:    - serializedVersion: 3\n(?:      .*\n)+)*)    m_PreInfinity: (\d)\n    m_PostInfinity: (\d)/gm;
const KEY_RE = /time: (\S+)\n\s+value: (\S+)\n\s+inSlope: (\S+)\n\s+outSlope: (\S+)\n\s+tangentMode: \S+\n\s+weightedMode: (\S+)/g;
const REF_RE = /^  (\w+): \{fileID: (\d+)(?:, guid: ([0-9a-f]{32}))?[^}]*\}/gm;
const SCALAR_RE = /^  (\w+): ([-0-9.eE]+)$/gm;
const num = (s) => (s === 'Infinity' ? Infinity : s === '-Infinity' ? -Infinity : parseFloat(s));

export function parsePrefab(file) {
  const txt = fs.readFileSync(file, 'utf8');
  const curves = {}, clips = {}, scalars = {};
  for (const m of txt.matchAll(CURVE_RE)) {
    const keys = [];
    for (const k of m[2].matchAll(KEY_RE)) {
      if (k[5] !== '0') throw new Error(`${file}: ${m[1]} has a weighted key`);
      keys.push([num(k[1]), num(k[2]), num(k[3]), num(k[4])]);
    }
    curves[m[1]] = keys;
  }
  for (const m of txt.matchAll(REF_RE)) {
    if (m[3] && m[2] === '8300000') clips[m[1]] = soundByGuid(m[3]);
  }
  for (const m of txt.matchAll(SCALAR_RE)) scalars[m[1]] = parseFloat(m[2]);
  return { file, curves, clips, scalars };
}

export function loadHQ(name) {
  return parsePrefab(`${HQ}/${name}_HQ.prefab`);
}

/** The Medium Quality prefab ("Three" sound levels: idle, low, med, very_high).
 *  File names are not case-consistent (Rotary_x8_free_MQ), so match loosely. */
export function loadMQ(name) {
  const dir = RES2 + '/Prefabs/Engine_Prefabs/Medium Quality';
  const f = fs.readdirSync(dir).find((n) => n.toLowerCase() === (name + '_MQ.prefab').toLowerCase());
  if (!f) throw new Error('no MQ prefab for ' + name);
  return parsePrefab(dir + '/' + f);
}

/* ---- AnimationCurve.Evaluate, non-weighted, clamped ------------------------ */
export function evalCurve(keys, x) {
  if (!keys || keys.length === 0) return 0;
  if (keys.length === 1 || x <= keys[0][0]) return keys[0][1];
  const last = keys[keys.length - 1];
  if (x >= last[0]) return last[1];
  let i = 0;
  while (i < keys.length - 2 && x > keys[i + 1][0]) i++;
  const [t0, v0, , o0] = keys[i], [t1, v1, i1] = keys[i + 1];
  const dx = t1 - t0;
  if (!isFinite(o0) || !isFinite(i1)) return v0;          // stepped key
  const t = (x - t0) / dx, t2 = t * t, t3 = t2 * t;
  return (2 * t3 - 3 * t2 + 1) * v0 + (t3 - 2 * t2 + t) * o0 * dx +
         (t3 - t2) * i1 * dx + (-2 * t3 + 3 * t2) * v1;
}

/* ---- our family key -> vendor prefab -------------------------------------- */
export const PREFAB_OF = {
  boxer_german: 'Boxer_German', boxer_japanese_1: 'Boxer_Japanese_1', boxer_japanese_2: 'Boxer_Japanese_2',
  i4_german_1: 'i4_German_1', i4_german_2: 'i4_German_2', i4_german_3: 'i4_German_3',
  i4_japanese_1: 'i4_Japanese_1', i4_japanese_2: 'i4_Japanese_2', i4_serbian: 'i4_Serbian',
  i6_german: 'i6_German', i6_german_free: 'i6_German_free', i6_german_sport_1: 'i6_German_Sport_1',
  i6_japanese_1: 'i6_Japanese_1', i6_japanese_2: 'i6_Japanese_2',
  rotary_4_rotor: 'Rotary_4_Rotor', rotary_x7: 'Rotary_x7', rotary_x8_1: 'Rotary_x8_1',
  rotary_x8_2: 'Rotary_x8_2', rotary_x8_3: 'Rotary_x8_3', rotary_x8_free: 'Rotary_x8_Free', rotary_x3: 'Rotary_x3',
  v10_german: 'V10_German', v12_british: 'V12_British', v12_italian: 'V12_Italian',
  v6_japanese_1: 'V6_Japanese_1', v6_japanese_2: 'V6_Japanese_2',
  v8_american_classic_1: 'V8_American_Classic_1', v8_american_classic_2: 'V8_American_Classic_2',
  v8_american_modern_1: 'V8_American_Modern_1', v8_american_modern_2: 'V8_American_Modern_2',
  v8_formula: 'V8_Formula', v8_german_sport_2: 'V8_German_Sport_2',
};

/** The takes the game plays, in ladder order, with the prefab fields that
 *  drive them. idle and maxRPM are single takes; the rest are on/off pairs. */
export const TAKES = [
  ['idle', 'idle', 'idleVolCurve', 'idlePitchCurve'],
  ['idle_low', 'idle_low', 'idle_lowVolCurve', 'idle_lowPitchCurve'],
  ['low', 'low', 'lowVolCurve', 'lowPitchCurve'],
  ['low_med', 'low_med', 'low_medVolCurve', 'low_medPitchCurve'],
  ['med', 'med', 'medVolCurve', 'medPitchCurve'],
  ['med_high', 'med_high', 'med_highVolCurve', 'med_highPitchCurve'],
  ['high', 'high', 'highVolCurve', 'highPitchCurve'],
  ['very_high', 'very_high', 'very_highVolCurve', 'very_highPitchCurve'],
];

/* ---- bake for the game ----------------------------------------------------- */
const BAKE_CURVES = [
  'idleVolCurve', 'idlePitchCurve', 'idle_lowVolCurve', 'idle_lowPitchCurve',
  'lowVolCurve', 'lowPitchCurve', 'low_medVolCurve', 'low_medPitchCurve',
  'medVolCurve', 'medPitchCurve', 'med_highVolCurve', 'med_highPitchCurve',
  'highVolCurve', 'highPitchCurve', 'very_highVolCurve', 'very_highPitchCurve',
  'maxRPMVolCurve', 'onLoadVolumeByRPM', 'offLoadVolumeByRPM',
  'intakeOnVolCurve', 'intakeOffVolCurve',
  // The vendor pitches the intake takes with the AGGRESSIVENESS pitch curve
  // (RealisticEngineSound.cs passes aggressivnessPitchCurve to both intake
  // DynamicSound calls). There is no intake pitch curve of its own.
  'aggressivnessPitchCurve',
  // The aggressiveness (exhaust fx) layer itself: aggressiveness_on/off_fx
  // under this volume curve x aggressivnessMaster (phase 2, item 3).
  'aggressivnessVolCurve',
];
const r5 = (v) => (isFinite(v) ? Math.round(v * 1e4) / 1e4 : (v > 0 ? 1e9 : -1e9));   // 1e9 = stepped key
/** Opponents run the vendor's Medium Quality rig (idle + low/med/very_high,
 *  on-throttle takes only, the vendor's "Simulated" off-load): four voices a car. */
const BAKE_MQ = ['idleVolCurve', 'idlePitchCurve', 'lowVolCurve', 'lowPitchCurve', 'medVolCurve', 'medPitchCurve',
  'very_highVolCurve', 'very_highPitchCurve', 'onLoadVolumeByRPM', 'offLoadVolumeByRPM'];

export function bake(families = Object.keys(PREFAB_OF)) {
  const out = { note: 'Baked by tools/res2_curves.mjs from the vendor HQ + MQ prefabs (RES2 Pro v2.3.1). Do not hand-edit.', families: [] };
  for (const fam of families) {
    const p = loadHQ(PREFAB_OF[fam]), mq = loadMQ(PREFAB_OF[fam]);
    const s = p.scalars;
    out.families.push({
      family: fam,
      prefab: PREFAB_OF[fam],
      maxRPMLimit: s.maxRPMLimit,
      pitchMultiplier: s.pitchMultiplier ?? 1,
      idlePitchMultiplier: s.idlePitchMultiplier ?? 1,
      revLimiterTweaker: s.revLimiterTweaker ?? 3,
      gasPedalSimSpeed: s.gasPedalSimSpeed ?? 5.5,
      intakeMaster: (s.enableIntakeSFX ?? 0) ? (s.intakeMaster ?? 1) : 0,
      // The aggressiveness layer: its level (0 when the prefab disables it)
      // and how far it drops while the limiter take plays.
      aggressivnessMaster: (s.enableAggressivness ?? 0) ? (s.aggressivnessMaster ?? 1) : 0,
      revLimiterAggressTweaker: s.revLimiterAggressTweaker ?? 2,
      // v2.3 Engine Load (adsrVol / EngineEffort)
      effortSensitivity: s.effortSensitivity ?? 1,
      sustainAdsrVol: s.sustainAdsrVol ?? 0.4,
      curves: BAKE_CURVES.map((n) => ({ name: n, k: (p.curves[n] || []).flat().map(r5) })),
      mq: BAKE_MQ.map((n) => ({ name: n, k: (mq.curves[n] || []).flat().map(r5) })),
    });
  }
  return out;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const a = process.argv.slice(2);
  if (a[0] === '--bake') {
    const dst = path.join(REPO, 'Assets/PSXRacing/Resources/res2_rig.json');
    // The default family lives in Resources/Engines, every other one in
    // EngineSrc (built into on-demand bundles): a family is a folder with an idle take.
    const fams = [...new Set(['Assets/PSXRacing/Resources/Engines', 'Assets/PSXRacing/EngineSrc'].flatMap((d) => {
      const dir = path.join(REPO, d);
      return fs.existsSync(dir) ? fs.readdirSync(dir, { withFileTypes: true })
        .filter((e) => e.isDirectory() && fs.existsSync(path.join(dir, e.name, 'idle.ogg'))).map((e) => e.name) : [];
    }))].filter((f) => PREFAB_OF[f]).sort();
    const b = bake(fams);
    fs.writeFileSync(dst, JSON.stringify(b));
    console.log('wrote', dst, fs.statSync(dst).size, 'bytes,', b.families.length, 'families');
  } else if (a[0] === '--dump') {
    const p = loadHQ(a[1]);
    console.log(JSON.stringify(p.scalars, null, 0));
    for (const [n, k] of Object.entries(p.curves)) console.log(n.padEnd(22), JSON.stringify(k.map((x) => x.map((v) => +v.toFixed(4)))));
    for (const [n, c] of Object.entries(p.clips)) console.log(n.padEnd(22), c && c.replace(RES2, ''));
  }
}
