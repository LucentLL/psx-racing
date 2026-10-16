/* Offline engine-voice renderer + analyser: hear and MEASURE a mix change
   without a 30-minute WebGL build.

   Three rigs over the SAME rpm/throttle/speed profile:
     vendor   - a port of the vendor rig, RES2 Pro v2.3.1: RealisticEngineSound.cs
                (incl. the v2.3 Engine Load "adsrVol" and Distance Acoustics) and
                DynamicSoundController.dll (decompiled with Unity's Cecil), over the
                HQ prefab curves (res2_curves.mjs), playing the vendor's own WAVs.
     current  - EngineAudio.Update() + AudioToneChain as shipped at main 1efbfe31
                (geometric 8-rung ladder, rate = rpm/home, intake at fixed pitch).
     fixed    - the new EngineAudio: the vendor rig over the baked res2_rig.json,
                on the game's own imported Ogg takes.
   Every looping take goes through a port of LoopSeam first, as in the game.

   Usage (needs ffmpeg on PATH):
     node tools/audio_render_probe.mjs report <carId> [family]  - analysis tables
     node tools/audio_render_probe.mjs ab                       - the owner's A/B set
     node tools/audio_render_probe.mjs wav <rig> <carId> [family|vendorPrefab] [out.wav]
*/
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadHQ, evalCurve, PREFAB_OF, TAKES } from './res2_curves.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '..');
const ENGINES = path.join(REPO, 'Assets/PSXRacing/Resources/Engines');   // the default family
const ENGINE_SRC = path.join(REPO, 'Assets/PSXRacing/EngineSrc');          // every bundled family
const SR = 44100, CH = 2, FPS = 60, DT = 1 / FPS;
const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * clamp(t, 0, 1);
const invLerp = (a, b, v) => (a === b ? 0 : clamp((v - a) / (b - a), 0, 1));
const smooth = (c, t, tc, dt) => c + (t - c) * (1 - Math.exp(-dt / Math.max(tc, 1e-4)));

/* ---- clips ---------------------------------------------------------------- */
const clipCache = new Map();
/** A take as the game plays it: decoded, stereo 44.1k, and through LoopSeam
 *  (seam = false for a one-shot, which the game plays as recorded). */
function decode(file, seam = true) {
  const key = file + (seam ? '' : '#raw');
  if (clipCache.has(key)) return clipCache.get(key);
  if (!file || !fs.existsSync(file)) { clipCache.set(key, null); return null; }
  const b = execFileSync('ffmpeg', ['-v', 'error', '-i', file, '-ac', '2', '-ar', String(SR), '-f', 'f32le', '-'], { maxBuffer: 1 << 28 });
  const raw = new Float32Array(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength));
  const x = seam ? loopSeam(raw) : raw;
  clipCache.set(key, x);
  return x;
}
/** Deterministic randomness for the crackle delays and picks (repeatable renders). */
function rng(seed) {
  let a = seed >>> 0;
  return () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
/** LoopSeam.cs: cut where the tail best matches the head, fold the tail on. */
function loopSeam(d) {
  const n = d.length / CH;
  if (n < 4096) return d;
  const fade = clamp(Math.round(0.012 * SR), 128, n >> 3);
  const search = clamp(Math.round(0.035 * SR), 0, n - fade * 3);
  const nominal = n - fade;
  let best = nominal, bestScore = -Infinity;
  for (let cut = nominal - search; cut <= nominal; cut++) {
    let dot = 0, mag = 1e-9;
    for (let i = 0; i < fade; i += 8) for (let c = 0; c < CH; c++) {
      const a = d[i * CH + c], b = d[(cut + i) * CH + c]; dot += a * b; mag += b * b;
    }
    const s = dot / Math.sqrt(mag);
    if (s > bestScore) { bestScore = s; best = cut; }
  }
  const out = d.slice(0, best * CH);
  for (let i = 0; i < fade; i++) {
    const w = 0.5 - 0.5 * Math.cos(Math.PI * i / fade);
    for (let c = 0; c < CH; c++) out[i * CH + c] = d[i * CH + c] * w + d[(best + i) * CH + c] * (1 - w);
  }
  return out;
}
/** The game's own take: Resources/Engines (default family) or EngineSrc; set
 *  'int' reads the cockpit (Interior) recordings. */
export const oursClipPath = (fam, name, set = 'ext') => {
  const sub = set === 'int' ? ['Interior', name + '.ogg'] : [name + '.ogg'];
  const a = path.join(ENGINES, fam, ...sub);
  return set !== 'int' && fs.existsSync(a) ? a : path.join(ENGINE_SRC, fam, ...sub);
};
const oursClip = (fam, name, set = 'ext') => decode(oursClipPath(fam, name, set));

/** One AudioSource: fractional read head, linear interpolation, volume and
 *  pitch stepped once per control frame like Unity's per-frame writes. */
class Voice {
  constructor(buf, label) { this.b = buf; this.n = buf ? buf.length / CH : 0; this.p = 0; this.g = 0; this.r = 1; this.label = label; }
  set(g, r) { this.g = g > 0.0005 ? g : 0; this.r = r; }
  mix(out) {
    if (!this.b) return;
    if (this.g > 0) {
      const i0 = Math.floor(this.p), f = this.p - i0, i1 = (i0 + 1) % this.n;
      out[0] += this.g * (this.b[i0 * 2] * (1 - f) + this.b[i1 * 2] * f);
      out[1] += this.g * (this.b[i0 * 2 + 1] * (1 - f) + this.b[i1 * 2 + 1] * f);
    }
    this.p = (this.p + this.r * (this.dop || 1)) % this.n;
  }
}

/** A one-shot AudioSource: plays a buffer once from the start when fired. */
class OneShot {
  constructor(label) { this.b = null; this.n = 0; this.p = 0; this.g = 0; this.r = 1; this.label = label; }
  get playing() { return this.b != null && this.p < this.n - 1; }
  fire(buf, g, r = 1) { this.b = buf; this.n = buf ? buf.length / CH : 0; this.p = 0; this.g = g; this.r = r; }
  set() {}
  mix(out) {
    if (!this.b || this.p >= this.n - 1) return;
    const i0 = Math.floor(this.p), f = this.p - i0, i1 = i0 + 1;
    out[0] += this.g * (this.b[i0 * 2] * (1 - f) + this.b[i1 * 2] * f);
    out[1] += this.g * (this.b[i0 * 2 + 1] * (1 - f) + this.b[i1 * 2 + 1] * f);
    this.p += this.r * (this.dop || 1);
  }
}

/* ---- cars ----------------------------------------------------------------- */
const CARS = JSON.parse(fs.readFileSync(path.join(REPO, 'Assets/PSXRacing/Resources/rg2_cars.json'), 'utf8'));
const carList = Array.isArray(CARS) ? CARS : CARS.cars || Object.values(CARS);
export function car(id) {
  const c = carList.find((x) => x.id === id);
  if (!c) throw new Error('no car ' + id);
  return { ...c, revLimit: c.revLimit || c.redline + 500 };
}

/* ---- profile: idle, free rev to the limiter, lift, 2nd-gear pull, shift,
   3rd-gear pull, lift-off coast. ~11 s, one frame per 1/60 s. */
export function profile(c) {
  const idle = c.idleRPM, red = c.redline, lim = c.revLimit;
  const f = [], seg = {};
  const mark = (n) => { seg[n] = f.length * DT; };
  let gear = 2;                       // the game has no neutral free-rev; keep the shift lift for real shifts
  const push = (rpm, thr, kmh = 0) => f.push({ rpm, thr, kmh, gear });
  let rpm = idle;
  mark('idle');
  for (let t = 0; t < 1.0; t += DT) push(idle, 0);
  mark('rev');
  while (rpm < lim) { rpm = Math.min(lim, rpm + 7000 * DT); push(rpm, 1); }
  for (let t = 0; t < 0.35; t += DT) push((Math.floor(t * 12) % 2) ? lim - 250 : lim, 1);   // limiter bounce
  mark('lift');
  while (rpm > idle * 1.05) { rpm = Math.max(idle, rpm - 4500 * DT); push(rpm, 0); }
  for (let t = 0; t < 0.3; t += DT) push(idle, 0);
  mark('pull2');
  gear = 2;
  rpm = Math.max(idle * 2.5, 3000);
  const k2 = 95 / red, k3 = 135 / red;
  const r2 = (red * 0.97 - rpm) / 2.4;
  while (rpm < red * 0.97) { rpm += r2 * DT; push(rpm, 1, rpm * k2); }
  mark('shift');
  gear = 3;
  const drop = red * 0.97 * 0.72, kmhShift = red * 0.97 * k2;
  for (let t = 0; t < 0.25; t += DT) { rpm = Math.max(drop, rpm - 12000 * DT); push(rpm, 0, kmhShift); }
  mark('pull3');
  const r3 = (red * 0.99 - rpm) / 2.6;
  while (rpm < red * 0.99) { rpm += r3 * DT; push(rpm, 1, rpm * k3); }
  mark('coast');
  const coastTo = Math.max(idle * 3, 3200);
  while (rpm > coastTo) { rpm -= (red - coastTo) / 2.0 * DT; push(rpm, 0, rpm * k3); }
  for (let t = 0; t < 0.4; t += DT) push(coastTo, 0, coastTo * k3);
  mark('end');
  f.seg = seg;
  return f;
}

/* ---- v2.3 Engine Load: RealisticEngineSound.EngineEffort + adsrVol --------- */
function engineLoadV23(maxRPMLimit, carMaxSpeed, effortSensitivity, sustain) {
  let sample = 0, timer = 0, work = 0;
  return {
    get work() { return work; },
    adsr(cv, load, master) { return lerp(Math.max(1 - cv, sustain), lerp(sustain, 1, work), load) * master; },
    effort(rpm, kmh, dt) {
      timer += dt;
      if (timer < 0.05) return work;
      if (rpm < 900) work = 0;
      else {
        const rate = Math.max(0, (rpm - sample) / timer) / Math.max(rpm, 1);
        const adj = rate * lerp(1, 2, invLerp(0, carMaxSpeed, kmh)) * effortSensitivity;
        const tgt = Math.max(invLerp(0.03, 0.35, adj), invLerp(carMaxSpeed / 2, carMaxSpeed, kmh),
                             invLerp(0.75, 0.9, rpm / maxRPMLimit));
        work = lerp(work, tgt, (tgt > work ? 10 : 2) * timer);
      }
      sample = rpm; timer = 0;
      return work;
    },
  };
}

/* ---- rig: vendor (RES2 v2.3.1) --------------------------------------------- */
export function vendorRig(prefabName, c, opt = {}) {
  const p = loadHQ(prefabName), s = p.scalars, K = p.curves;
  const maxRPM = opt.maxRPMLimit || c.revLimit;
  const pitchMul = opt.pitchMul ?? (s.pitchMultiplier ?? 1);
  const gasSpeed = s.gasPedalSimSpeed ?? 5.5, tweak = s.revLimiterTweaker ?? 3;
  const aggMaster = (s.enableAggressivness ?? 0) && opt.aggressiveness !== false ? (s.aggressivnessMaster ?? 0) : 0;
  const intakeMaster = (s.enableIntakeSFX ?? 0) ? (s.intakeMaster ?? 1) : 0;
  const intakeMix = opt.intakeMix ?? 0.1;               // chase view: listener behind the car
  const clip = (field) => decode(p.clips[field]);
  const takes = TAKES.map(([n, base, vol, pit]) => ({
    n, vol: K[vol], pit: K[pit],
    on: new Voice(clip(n === 'idle' ? 'idleClip' : base + 'OnClip'), n + '_on'),
    off: n === 'idle' ? null : new Voice(clip(base + 'OffClip'), n + '_off'),
  }));
  const maxV = new Voice(clip('maxRPMClip'), 'maxRPM');
  const inOn = new Voice(clip('intakeOnClip'), 'intake_on'), inOff = new Voice(clip('intakeOffClip'), 'intake_off');
  const agOn = new Voice(aggMaster ? clip('aggressivnessOnClip') : null, 'agg_on');
  const agOff = new Voice(aggMaster ? clip('aggressivnessOffClip') : null, 'agg_off');
  const voices = [...takes.flatMap((t) => [t.on, t.off].filter(Boolean)), maxV, inOn, inOff, agOn, agOff];
  const EL = engineLoadV23(maxRPM, s.carMaxSpeed ?? 250, s.effortSensitivity ?? 1, s.sustainAdsrVol ?? 0.4);
  const v23 = opt.v23 !== false;
  let load = 0, fixedAcc = 0, cv = 0;
  const rig = {
    voices, name: 'vendor:' + prefabName, distance: opt.distance ?? 5, minDistance: s.minDistance ?? 1.5, maxDistance: s.maxDistance ?? 250,
    acoustics: opt.acoustics !== false, distortion: 0,
    step(fr) {
      fixedAcc += DT;                                       // FixedUpdate, 50 Hz
      while (fixedAcc >= 0.02) { fixedAcc -= 0.02; load = lerp(load, fr.thr > 0.5 ? 1 : 0, 0.02 * gasSpeed); cv = fr.rpm / maxRPM; }
      const master = v23 ? EL.adsr(cv, load, 1) : 1;        // Update: adsrVol from LAST frame's workload
      if (v23) EL.effort(fr.rpm, fr.kmh || 0, DT);
      rig.distortion = v23 ? EL.work * master / 3.3 : 0;
      const onL = evalCurve(K.onLoadVolumeByRPM, cv), offL = evalCurve(K.offLoadVolumeByRPM, cv);
      const mx = evalCurve(K.maxRPMVolCurve, cv) * master * onL;
      const lim = maxV.b && mx >= 0.95 * master * onL && mx > 0, limIdle = maxV.b && mx >= 0.95 * master && mx > 0;
      for (const t of takes) {
        const v = evalCurve(t.vol, cv) * master, pt = evalCurve(t.pit, cv) * pitchMul;
        if (!t.off) { t.on.set(v * lerp(offL, onL, load) / (limIdle ? tweak : 1), pt); continue; }
        const d = lim ? tweak : 1;
        t.on.set(v * onL * load / d, pt);
        t.off.set(v * offL * (1 - load) / d, pt);
      }
      maxV.set(mx, evalCurve(K.very_highPitchCurve, cv) * pitchMul);
      const agP = evalCurve(K.aggressivnessPitchCurve, cv) * pitchMul;
      inOn.set(evalCurve(K.intakeOnVolCurve, cv) * master * onL * load * intakeMaster * intakeMix, agP);
      inOff.set(evalCurve(K.intakeOffVolCurve, cv) * master * onL * (1 - load) * intakeMaster * intakeMix, agP);
      const agV = evalCurve(K.aggressivnessVolCurve, cv) * master * aggMaster, da = lim ? (s.revLimiterAggressTweaker ?? 2) : 1;
      agOn.set(agV * onL * load / da, agP);
      agOff.set(agV * offL * (1 - load) / da, agP);
      rig.cv = cv; rig.load = load; rig.master = master;
    },
  };
  return rig;
}

/* ---- rig: current (EngineAudio.cs + AudioToneChain.cs at 1efbfe31) -------- */
const PLAYER_BANDS = [
  ['idle', 0.00, 'idle', null], ['idle_low', 0.10, 'idle_low_on', 'idle_low_off'],
  ['low', 0.22, 'low_on', 'low_off'], ['low_med', 0.35, 'low_med_on', 'low_med_off'],
  ['med', 0.48, 'med_on', 'med_off'], ['med_high', 0.62, 'med_high_on', 'med_high_off'],
  ['high', 0.75, 'high_on', 'high_off'], ['very_high', 0.88, 'very_high_on', 'very_high_off'],
];
export function currentRig(fam, c) {
  const idle = c.idleRPM, top = c.redline > idle ? c.redline : c.revLimit, lim = c.revLimit;
  const rateMul = c.voiceRateMul > 0.05 ? c.voiceRateMul : 1, levelMul = c.voiceLevelMul > 0.05 ? c.voiceLevelMul : 1;
  const lope = c.lopeDepth > 0 && c.lopeFadeTop > idle ? { d: c.lopeDepth, o: c.lopeOrder || 0.5, top: c.lopeFadeTop, ph: c.lopePhase } : null;
  const S = top / idle, logSpan = Math.log(S);
  const bands = PLAYER_BANDS.map(([n, frac, on, off]) => ({
    n, frac, home: idle * Math.pow(S, frac), gain: 0, rate: 1,
    on: new Voice(oursClip(fam, on), on), off: off ? new Voice(oursClip(fam, off), off) : null,
  }));
  const maxV = new Voice(oursClip(fam, 'maxRPM'), 'maxRPM');
  const inOn = new Voice(oursClip(fam, 'intake_on'), 'intake_on'), inOff = new Voice(oursClip(fam, 'intake_off'), 'intake_off');
  const voices = [...bands.flatMap((b) => [b.on, b.off].filter(Boolean)), maxV, inOn, inOff];
  let audible = idle, master = 0, lo = -1, hi = -1, limT = 0, maxG = 0, lopePh = 0;
  return {
    voices, name: 'current:' + fam, tone: [c.voicePeakHz, c.voicePeakDb, c.voiceShelfDb], bands,
    step(fr) {
      const raw = fr.rpm;
      if (raw >= audible || limT > 0 || audible - raw > 5000) audible = raw; else audible = Math.max(raw, audible - 4200 * DT);
      limT = fr.rpm >= lim - 120 && fr.thr > 0.5 ? 0.2 : Math.max(0, limT - DT);
      const frac = clamp(Math.log(Math.max(audible, idle) / idle) / logSpan, 0, 1);
      const ok = lo >= 0 && frac >= bands[lo].frac - 0.025 && frac <= bands[hi].frac + 0.025;
      if (!ok) {
        lo = 0; hi = 0;
        for (let i = 0; i < bands.length - 1; i++) if (frac >= bands[i].frac && frac <= bands[i + 1].frac) { lo = i; hi = i + 1; break; }
        if (frac > bands[bands.length - 1].frac) lo = hi = bands.length - 1;
      }
      let wob = 1, lvl = 1;
      if (lope) {
        lopePh = (lopePh + 2 * Math.PI * (audible / 60) * lope.o * DT) % (2 * Math.PI);
        const fade = clamp((lope.top - audible) / Math.max(1, lope.top - idle), 0, 1), d = lope.d * fade * fade;
        wob = 1 + d * Math.sin(lopePh + lope.ph); lvl = 1 + d * 1.2 * Math.sin(lopePh + lope.ph + 1.1);
      }
      const load = clamp(fr.thr, 0, 1);
      master = smooth(master, Math.min(0.62, 0.32 + 0.30 * load) * levelMul, 0.05, DT);
      const mix = master * lvl;
      const t = hi === lo ? 0 : clamp((frac - bands[lo].frac) / Math.max(bands[hi].frac - bands[lo].frac, 1e-4), 0, 1);
      bands.forEach((b, i) => {
        let tg = 0; if (i === lo) tg = 1 - t; if (i === hi) tg = Math.max(tg, t); if (lo === hi && i === lo) tg = 1;
        b.gain = smooth(b.gain, tg, 0.04, DT);
        b.rate = smooth(b.rate, clamp(audible / Math.max(b.home, 1) * rateMul * wob, 0.66, 1.60), 0.012, DT);
        const g = b.gain * mix;
        if (b.off) { b.on.set(g * load, b.rate); b.off.set(g * (1 - load), b.rate); } else b.on.set(g, b.rate);
      });
      maxG = smooth(maxG, limT > 0 ? 1 : 0, 0.07, DT);
      maxV.set(maxG * mix, rateMul);
      const ig = mix * 0.22;
      inOn.set(ig * frac * load, rateMul);
      inOff.set(ig * clamp((frac - 0.3) / 0.7, 0, 1) * 0.4 * (1 - load), rateMul);
    },
  };
}

/* ---- rig: fixed (the new EngineAudio.cs, mirrored line for line) ---------- */
const RIG = JSON.parse(fs.readFileSync(path.join(REPO, 'Assets/PSXRacing/Resources/res2_rig.json'), 'utf8'));
export function bakedCurves(fam) {
  const F = RIG.families.find((x) => x.family === fam);
  if (!F) throw new Error('no baked rig for ' + fam);
  const cur = Object.fromEntries(F.curves.map((x) => {
    const k = [], inf = (v) => (Math.abs(v) > 1e8 ? Math.sign(v) * Infinity : v);
    for (let i = 0; i < x.k.length; i += 4) k.push([x.k[i], x.k[i + 1], inf(x.k[i + 2]), inf(x.k[i + 3])]);
    return [x.name, k];
  }));
  return { F, cur };
}
/** EngineAudio.cs (vendor rig) mirrored line for line. opt.player=false is the
 *  opponents' Medium Quality rig with the vendor's "Simulated" off-load. */
/** EngineVoiceLibrary.Alias */
export const canonical = (fam) => fam;   // EngineVoiceLibrary.Alias is empty since the owner chose x7
export const LEVEL_GAIN = 1.0;           // EngineAudio.LevelGain
/** AudioToneChain since 2026-10-16: the five middle stages at 0 dB. */
export const NEW_CHAIN = { lowShelfDb: 0, cutDb: 0, highShelfDb: 0 };
export const FLAT_VOICE = [1000, 0, 0];
export function fixedRig(fam, c, opt = {}) {
  const { F, cur: ext } = bakedCurves(fam);
  // The cockpit set (phase 2, item 4): the interior recordings under the
  // interior prefab's curves (only those that differ are baked, in F.int).
  const set = opt.set === 'int' ? 'int' : 'ext';
  const inner = set === 'int' && F.int ? F.int : null;
  const cur = inner ? { ...ext, ...Object.fromEntries((inner.curves || []).map((x) => {
    const k = [], inf = (v) => (Math.abs(v) > 1e8 ? Math.sign(v) * Infinity : v);
    for (let i = 0; i < x.k.length; i += 4) k.push([x.k[i], x.k[i + 1], inf(x.k[i + 2]), inf(x.k[i + 3])]);
    return [x.name, k];
  })) } : ext;
  const mq = Object.fromEntries((RIG.families.find((x) => x.family === fam).mq || []).map((x) => {
    const k = [], inf = (v) => (Math.abs(v) > 1e8 ? Math.sign(v) * Infinity : v);
    for (let i = 0; i < x.k.length; i += 4) k.push([x.k[i], x.k[i + 1], inf(x.k[i + 2]), inf(x.k[i + 3])]);
    return [x.name, k];
  }));
  const lim = Math.max(c.revLimit, c.idleRPM + 100);
  const rateMul = opt.rateMul ?? (c.voiceRateMul > 0.05 ? c.voiceRateMul : 1);
  const levelMul = (c.voiceLevelMul > 0.05 ? c.voiceLevelMul : 1) * (opt.masterVolume ?? 1);
  const player = opt.player !== false;
  const K = player ? cur : mq;
  const names = player ? TAKES.map((t) => t[0]) : ['idle', 'low', 'med', 'very_high'];
  const takes = names.map((n) => ({
    n, vol: K[n + 'VolCurve'], pit: K[n + 'PitchCurve'],
    on: new Voice(oursClip(fam, n === 'idle' ? 'idle' : n + '_on', set), n + '_on'),
    off: n === 'idle' || !player ? null : new Voice(oursClip(fam, n + '_off', set), n + '_off'),
  }));
  const maxV = new Voice(player ? oursClip(fam, 'maxRPM', set) : null, 'maxRPM');
  const inOn = new Voice(player ? oursClip(fam, 'intake_on', set) : null, 'intake_on');
  const inOff = new Voice(player ? oursClip(fam, 'intake_off', set) : null, 'intake_off');
  // EngineAudio's aggressiveness layer (phase 2, item 3): on unless opt.agg === false.
  const aggMaster = opt.agg === false || !player ? 0 : (F.aggressivnessMaster || 0);
  const agOn = new Voice(aggMaster ? oursClip(fam, 'aggressiveness_on', set) : null, 'agg_on');
  const agOff = new Voice(aggMaster ? oursClip(fam, 'aggressiveness_off', set) : null, 'agg_off');
  // EngineAudio's muffler crackle (item 3): opt.crackle = the car's level (0 = off).
  const crackleLevel = player ? (opt.crackle || 0) : 0;
  const crackleSrc = new OneShot('crackle');
  const cracklePool = crackleLevel > 0 ? Array.from({ length: 12 }, (_, i) =>
    decode(path.join(REPO, 'Assets/PSXRacing/Resources/Crackle', (set === 'int' ? 'crackle_int_' : 'crackle_') + String(i + 1).padStart(2, '0') + '.ogg'), false)).filter(Boolean) : [];
  const rand = rng(opt.seed ?? 7);
  let crackleCtl = 0, fireAt = -1, disarmAt = -1, t = 0;
  const CRACKLE_VOL = [[0, 0, 1.1173617, 1.1173617], [0.8970616, 1.0023422, 0, 0], [1, 1, -0.022753645, -0.022753645]];
  const voices = [...takes.flatMap((t) => [t.on, t.off].filter(Boolean)), maxV, inOn, inOff, agOn, agOff, crackleSrc];
  const masterVol = (inner ? inner.masterVolume : 1) * (opt.interiorGain ?? 1);
  const intakeMaster = inner ? inner.intakeMaster : F.intakeMaster;
  const gasF = clamp(0.02 * (F.gasPedalSimSpeed || 12), 0.001, 1), intakeMix = set === 'int' ? 1 : (opt.intakeMix ?? 0.1);
  const EL = engineLoadV23(lim, opt.carMaxKmh || (c.topSpeedMps || 69.4) * 3.6, F.effortSensitivity || 1, F.sustainAdsrVol || 0.4);
  let load = 0, audible = c.idleRPM, lastGear = 0, shiftT = 0;
  const level = (opt.level ?? LEVEL_GAIN) * masterVol;
  const rig = {
    voices, name: 'fixed:' + fam + (set === 'int' ? ':int' : ''), distortion: 0, pops: 0,
    step(fr) {
      t += DT;
      const raw = fr.rpm;
      if (raw >= audible || audible - raw > 5000) audible = raw; else audible = Math.max(raw, audible - 4200 * DT);
      const cv = audible / lim;
      if ((fr.gear || 0) !== lastGear) { lastGear = fr.gear || 0; shiftT = 0.2; }
      shiftT = Math.max(0, shiftT - DT);
      const pedal = shiftT > 0 ? 0 : clamp(fr.thr, 0, 1);
      load = lerp(load, pedal, 1 - Math.pow(1 - gasF, DT / 0.02));
      const adsr = EL.adsr(cv, load, 1);
      EL.effort(audible, fr.kmh || 0, DT);
      const master = adsr * levelMul * level;
      rig.distortion = EL.work * adsr / 3.3;
      const onL = evalCurve(K.onLoadVolumeByRPM, cv), offL = evalCurve(K.offLoadVolumeByRPM, cv);
      const mx = player ? evalCurve(cur.maxRPMVolCurve, cv) * onL : 0;
      const div = mx > 0 && mx >= 0.95 * onL ? (F.revLimiterTweaker || 3) : 1;
      const divIdle = mx >= 0.95 ? (F.revLimiterTweaker || 3) : 1;
      const idleLoad = lerp(offL, onL, load);
      for (const t of takes) {
        const v = evalCurve(t.vol, cv) * master, pt = clamp(evalCurve(t.pit, cv) * rateMul, 0.05, 3);
        if (t.n === 'idle') t.on.set(v * idleLoad / divIdle, pt);
        else if (t.off) { t.on.set(v * onL * load / div, pt); t.off.set(v * offL * (1 - load) / div, pt); }
        else t.on.set(v * idleLoad, pt);
      }
      maxV.set(mx * master, clamp(evalCurve(cur.very_highPitchCurve, cv) * rateMul, 0.05, 3));
      const ip = clamp(evalCurve(cur.aggressivnessPitchCurve, cv) * rateMul, 0.05, 3);
      const mix = intakeMix * intakeMaster * onL * master;
      inOn.set(evalCurve(cur.intakeOnVolCurve, cv) * load * mix, ip);
      inOff.set(evalCurve(cur.intakeOffVolCurve, cv) * (1 - load) * mix, ip);
      if (aggMaster) {
        const da = mx > 0 && mx >= 0.95 * onL ? (F.revLimiterAggressTweaker || 2) : 1;
        const av = evalCurve(cur.aggressivnessVolCurve, cv) * master * aggMaster / da;
        agOn.set(av * onL * load, ip);
        agOff.set(av * offL * (1 - load), ip);
      }
      if (cracklePool.length) {
        // EngineAudio.UpdateCrackle, line for line.
        const pressing = shiftT <= 0 && fr.thr > 0.1, vol = evalCurve(CRACKLE_VOL, cv) * crackleLevel;
        const playing = crackleSrc.playing;
        const pop = () => { crackleSrc.fire(cracklePool[Math.floor(rand() * cracklePool.length)], vol * levelMul); rig.pops++; };
        if (!pressing && vol > 0.09 && crackleCtl === 2 && disarmAt < 0 && !playing) {
          const at = t + 0.1 + rand() * 0.4;
          fireAt = fireAt < 0 ? at : Math.min(fireAt, at);
        }
        if (fireAt >= 0 && t >= fireAt) { fireAt = -1; if (crackleCtl !== 0 && !playing) pop(); disarmAt = t + 0.5; }
        if (disarmAt >= 0 && t >= disarmAt) { crackleCtl = 0; disarmAt = -1; }
        if (pressing) { if (crackleCtl !== 2) crackleCtl = 1; } else if (crackleCtl === 1) crackleCtl = 2;
        if (evalCurve(cur.maxRPMVolCurve, cv) > 0.9 && !crackleSrc.playing) { crackleCtl = 2; pop(); }
      }
      rig.cv = cv; rig.load = load; rig.master = master;
    },
  };
  return rig;
}

/* ---- render ----------------------------------------------------------------- */
/** opts.acoustics: the vendor's v2.3 Distance Acoustics on the engine group
 *  (low-pass 18k->2k over min..maxDistance, 20 ms echo at distance/25 wet,
 *  distortion = workload * adsrVol / 3.3), FMOD's DSPs approximated. */
export function render(rig, frames, { tone = null, trace = null, acoustics = false } = {}) {
  const per = Math.round(SR * DT), N = frames.length * per, out = new Float32Array(N * CH);
  const acc = [0, 0];
  const D = rig.distance ?? 5;
  const echoLen = Math.round(0.020 * SR), echo = [new Float32Array(echoLen), new Float32Array(echoLen)];
  const wet = D / 25, decay = Math.min(0.7, 0.3 + D / 500);
  const lpHz = lerp(18000, 2000, invLerp(rig.minDistance ?? 1.5, rig.maxDistance ?? 250, D));
  const lp = [biquadLP(lpHz, 1), biquadLP(lpHz, 1)];
  let ei = 0;
  for (let f = 0; f < frames.length; f++) {
    rig.step(frames[f]);
    if (trace) trace(f, frames[f], rig);
    const L = clamp(rig.distortion || 0, 0, 0.99), k = 2 * L / (1 - L);
    for (let s = 0; s < per; s++) {
      acc[0] = 0; acc[1] = 0;
      for (const v of rig.voices) v.mix(acc);
      if (acoustics) {
        for (let c = 0; c < 2; c++) {
          let x = acc[c];
          x = runBQ(lp[c], x);
          const d = echo[c][ei]; echo[c][ei] = x + d * decay; x = x + d * wet;
          if (L > 0) x = (1 + k) * x / (1 + k * Math.abs(x));
          acc[c] = x;
        }
        ei = (ei + 1) % echoLen;
      }
      const n = (f * per + s) * 2; out[n] = acc[0]; out[n + 1] = acc[1];
    }
  }
  return tone ? applyTone(out, tone) : out;
}
function biquadLP(f0, q) {
  const w = 2 * Math.PI * Math.min(f0, SR * 0.45) / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * q), a0 = 1 + al;
  return { b0: (1 - c) / 2 / a0, b1: (1 - c) / a0, b2: (1 - c) / 2 / a0, a1: -2 * c / a0, a2: (1 - al) / a0, x1: 0, x2: 0, y1: 0, y2: 0 };
}
function runBQ(q, v) { const y = q.b0 * v + q.b1 * q.x1 + q.b2 * q.x2 - q.a1 * q.y1 - q.a2 * q.y2; q.x2 = q.x1; q.x1 = v; q.y2 = q.y1; q.y1 = y; return y; }

/* AudioToneChain.cs (seven biquads + tanh) with its 1efbfe31 defaults; pass NEW_CHAIN + FLAT_VOICE for
   the chain as it stands since 2026-10-16. voice = [peakHz, peakDb, shelfDb]. */
export function applyTone(x, voice, chain = {}) {
  const Nz = (b0, b1, b2, a0, a1, a2) => ({ b0: b0 / a0, b1: b1 / a0, b2: b2 / a0, a1: a1 / a0, a2: a2 / a0, x1: 0, x2: 0, y1: 0, y2: 0 });
  const shelf = (hi) => (f, db) => {
    const A = 10 ** (db / 40), w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w);
    const al = s / 2 * Math.sqrt((A + 1 / A) * (1 / 0.707 - 1) + 2), t = 2 * Math.sqrt(A) * al;
    return hi
      ? Nz(A * ((A + 1) + (A - 1) * c + t), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - t), (A + 1) - (A - 1) * c + t, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - t)
      : Nz(A * ((A + 1) - (A - 1) * c + t), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - t), (A + 1) + (A - 1) * c + t, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - t);
  };
  const hp = (f, q) => { const w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * q); return Nz((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al); };
  const lp = (f, q) => { const w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * q); return Nz((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al); };
  const pk = (f, db, q) => { const A = 10 ** (db / 40), w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * q); return Nz(1 + al * A, -2 * c, 1 - al * A, 1 + al / A, -2 * c, 1 - al / A); };
  const [phz, pdb, sdb] = voice;
  const C = { lowShelfDb: 5.0, cutDb: -4.0, highShelfDb: -2.0, drive: 1.30, trim: 0.90, ...chain };
  const mk = () => [hp(40, 0.707), shelf(false)(200, C.lowShelfDb), pk(phz, pdb, 0.9), shelf(true)(2600, sdb),
    pk(3200, C.cutDb, 1.0), shelf(true)(6500, C.highShelfDb), lp(11000, 0.707)];
  const L = mk(), R = mk();
  const y = new Float32Array(x.length);
  for (let n = 0; n < x.length; n += 2) {
    let l = x[n], r = x[n + 1];
    for (const q of L) l = runBQ(q, l);
    for (const q of R) r = runBQ(q, r);
    y[n] = Math.tanh(l * C.drive) * C.trim; y[n + 1] = Math.tanh(r * C.drive) * C.trim;
  }
  return y;
}

/* ---- analysis --------------------------------------------------------------- */
function fft(re, im) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) { let b = n >> 1; for (; j & b; b >>= 1) j ^= b; j ^= b; if (i < j) { let t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; } }
  for (let len = 2; len <= n; len <<= 1) {
    const a = -2 * Math.PI / len, wr = Math.cos(a), wi = Math.sin(a), h = len >> 1;
    for (let i = 0; i < n; i += len) {
      let cr = 1, ci = 0;
      for (let k = 0; k < h; k++) {
        const p = i + k, q = p + h, vr = re[q] * cr - im[q] * ci, vi = re[q] * ci + im[q] * cr;
        re[q] = re[p] - vr; im[q] = im[p] - vi; re[p] += vr; im[p] += vi;
        const t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
      }
    }
  }
}
/** Power spectrum of the mono sum over [a,b) seconds, Hann windows of n, 50% hop. */
export function spectrum(x, a, b, n = 8192) {
  const p = new Float64Array(n / 2), w = new Float64Array(n);
  for (let i = 0; i < n; i++) w[i] = 0.5 - 0.5 * Math.cos(2 * Math.PI * i / n);
  let cnt = 0;
  const re = new Float64Array(n), im = new Float64Array(n);
  for (let s = Math.floor(a * SR); s + n <= Math.min(b * SR, x.length / 2); s += n >> 1) {
    for (let i = 0; i < n; i++) { re[i] = (x[(s + i) * 2] + x[(s + i) * 2 + 1]) * 0.5 * w[i]; im[i] = 0; }
    fft(re, im);
    for (let k = 0; k < n / 2; k++) p[k] += re[k] * re[k] + im[k] * im[k];
    cnt++;
  }
  for (let k = 0; k < n / 2; k++) p[k] /= Math.max(cnt, 1);
  p.n = n;
  return p;
}
export const OCT = [31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
/** Share of energy per octave band, dB re the total. */
export function octaves(p) {
  const e = OCT.map(() => 0);
  for (let k = 1; k < p.length; k++) {
    const f = k * SR / p.n;
    for (let i = 0; i < OCT.length; i++) if (f >= OCT[i] / Math.SQRT2 && f < OCT[i] * Math.SQRT2) { e[i] += p[k]; break; }
  }
  const tot = e.reduce((a, b) => a + b, 0) || 1;
  return e.map((v) => 10 * Math.log10(v / tot + 1e-12));
}
export function rmsDb(x, a = 0, b = Infinity) {
  let s = 0, c = 0;
  for (let i = Math.floor(a * SR) * 2; i < Math.min(b * SR * 2, x.length); i++) { s += x[i] * x[i]; c++; }
  return 10 * Math.log10(s / Math.max(c, 1) + 1e-20);
}
/** Steady lines: bins whose prominence over their +-1/6-octave neighbourhood
 *  stays high in EVERY window, across windows at different rpm. A tone that
 *  tracks rpm moves between windows; a hum does not. */
export function steadyLines(x, wins, n = 16384, minDb = 8) {
  const specs = wins.map(([a, b]) => spectrum(x, a, b, n));
  const tots = specs.map((p) => p.reduce((s, v) => s + v, 0));
  const df = SR / n, hits = [];
  for (let k = Math.round(40 / df); k < Math.round(5000 / df); k++) {
    let minProm = Infinity, lvl = 0;
    const per = [];
    for (let w = 0; w < specs.length; w++) {
      const p = specs[w];
      const half = Math.max(8, Math.round(k * 0.26));                  // +-1/3 octave, >= 8 bins
      const nb = []; for (let j = Math.max(1, k - half); j <= Math.min(p.length - 1, k + half); j++) if (Math.abs(j - k) > 3) nb.push(p[j]);
      nb.sort((u, v) => u - v);
      const med = Math.max(nb[nb.length >> 1] || 0, 1e-12 * tots[w]), local = Math.max(p[k - 1], p[k], p[k + 1]);
      const pr = 10 * Math.log10(local / med);
      per.push(pr);
      minProm = Math.min(minProm, pr);
      lvl += 10 * Math.log10(local / tots[w]) / specs.length;
    }
    if (minProm > minDb) hits.push({ hz: +(k * df).toFixed(1), minProm: +minProm.toFixed(1), relDb: +lvl.toFixed(1), per: per.map((v) => +v.toFixed(0)) });
  }
  const out = [];
  for (const h of hits) { const l = out[out.length - 1]; if (l && h.hz - l.hz < 4 * df) { if (h.minProm > l.minProm) Object.assign(l, h); } else out.push({ ...h }); }
  return out;
}

export function writeWav(file, x, gain = 1) {
  const bytes = x.length * 2, buf = Buffer.alloc(44 + bytes);
  buf.write('RIFF', 0); buf.writeUInt32LE(36 + bytes, 4); buf.write('WAVE', 8); buf.write('fmt ', 12);
  buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(CH, 22); buf.writeUInt32LE(SR, 24);
  buf.writeUInt32LE(SR * CH * 2, 28); buf.writeUInt16LE(CH * 2, 32); buf.writeUInt16LE(16, 34);
  buf.write('data', 36); buf.writeUInt32LE(bytes, 40);
  for (let i = 0; i < x.length; i++) buf.writeInt16LE(Math.round(clamp(x[i] * gain, -1, 1) * 32767), 44 + i * 2);
  fs.writeFileSync(file, buf);
}
export const peakOf = (x) => { let p = 0; for (let i = 0; i < x.length; i++) p = Math.max(p, Math.abs(x[i])); return p; };

/* ---- CLI -------------------------------------------------------------------- */
export const SAMPLE_RATE = SR, FRAME_DT = DT;
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [cmd, kind, id, fam, out] = process.argv.slice(2);
  if (cmd === 'wav') {
    const c = car(id), f = fam || c.engineFamily;
    const rig = kind === 'vendor' ? vendorRig(PREFAB_OF[f] || f, c)
      : kind === 'current' ? currentRig(f, c) : fixedRig(f, c, { carMaxKmh: (c.topSpeedMps || 70) * 3.6 });
    const x = render(rig, profile(c), { tone: kind === 'current' ? rig.tone : null, acoustics: kind === 'vendor' });
    writeWav(out || `${kind}_${id}.wav`, x, 0.89 / peakOf(x));
  }
}

/* ---- the owner's A/B set: same profile per car, each file peak-normalised to
   -1 dBFS, MP3 via ffmpeg. node tools/audio_render_probe.mjs ab ----------- */
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url) && process.argv[2] === 'ab') {
  const OUT = path.join(REPO, 'tools/audio_ab');
  fs.mkdirSync(OUT, { recursive: true });
  const FD = car('mazda_rx_7_type_r__fd__j___91'), ZG = car('nissan_240zg__hs30___71'), R = car('nismo_270r__94');
  const cur = (c) => { const r = currentRig(c.engineFamily, c); return render(r, profile(c), { tone: r.tone }); };
  const fix = (c, chain = NEW_CHAIN, voice = FLAT_VOICE) => applyTone(render(fixedRig(canonical(c.engineFamily), c), profile(c)), voice, chain);
  const set = [
    ['1_reference_vendor_rig_Rotary_x8_Free_on_FD', () => render(vendorRig('Rotary_x8_Free', FD), profile(FD), { acoustics: true })],
    ['2_FD_RX7_current', () => cur(FD)],
    ['3_FD_RX7_fixed', () => fix(FD)],
    ['4_FD_RX7_fixed_through_OLD_tone_chain', () => fix(FD, {}, [FD.voicePeakHz, FD.voicePeakDb, FD.voiceShelfDb])],
    ['5_240ZG_current', () => cur(ZG)],
    ['6_240ZG_fixed', () => fix(ZG)],
    ['7_270R_i4_japanese_2_current', () => cur(R)],
    ['8_270R_i4_japanese_2_fixed', () => fix(R)],
  ];
  for (const [name, fn] of set) {
    const x = fn(), wavPath = path.join(OUT, name + '.wav');
    writeWav(wavPath, x, 0.891 / peakOf(x));
    execFileSync('ffmpeg', ['-v', 'error', '-y', '-i', wavPath, '-c:a', 'libmp3lame', '-q:a', '2', path.join(OUT, name + '.mp3')]);
    fs.unlinkSync(wavPath);
    console.log(name.padEnd(42), (x.length / 2 / SR).toFixed(1) + ' s', 'rms', rmsDb(x).toFixed(1), 'dB before normalising');
  }
}


/* ---- TurboAudio.cs (Turbo aspiration), line for line ------------------------ */
const AUDIO = path.join(REPO, 'Assets/PSXRacing/Audio');
export function turboRig(c, opt = {}) {
  const spool = new Voice(decode(path.join(AUDIO, 'turbo_spool.wav')), 'turbo_spool');
  const maxL = new Voice(decode(path.join(AUDIO, 'turbo_maxloop.wav')), 'turbo_max');
  const shot = new OneShot('bov');
  const longs = [1, 2, 3].map((i) => decode(path.join(AUDIO, `turbo_bov_long_${i}.wav`), false));
  const shorts = [1, 2, 3].map((i) => decode(path.join(AUDIO, `turbo_bov_short_${i}.wav`), false));
  const rand = rng(opt.seed ?? 11);
  const lim = c.revLimit, idle = c.idleRPM;
  let boost = 0, prevThr = 0, cool = 0, sg = 0, mg = 0, idx = 0, lastGear = 0;
  const sm = (cur, tgt, tc) => cur + (tgt - cur) * (1 - Math.exp(-DT / Math.max(tc, 1e-4)));
  const fire = (pool, vol) => { idx = (idx + 1) % pool.length; shot.fire(pool[idx], vol * 0.5 * 1.6, 0.94 + rand() * 0.12); };
  return {
    voices: [spool, maxL, shot], name: 'turbo',
    step(fr) {
      const rpmNorm = clamp((fr.rpm - idle) / Math.max(lim - idle, 1), 0, 1), thr = clamp(fr.thr, 0, 1);
      const flow = clamp((rpmNorm - 0.22) / 0.5, 0, 1), tgt = thr > 0.1 ? flow : 0;
      const rate = tgt > boost ? 2.8 : 9.0;
      boost = boost < tgt ? Math.min(tgt, boost + rate * DT) : Math.max(tgt, boost - rate * DT);
      cool = Math.max(0, cool - DT);
      if (prevThr > 0.45 && thr < 0.2 && boost > 0.3 && cool <= 0) {
        const v = clamp((rpmNorm - 0.5) / 0.4, 0, 1);
        if (v > 0.01) fire(rpmNorm > 0.8 ? longs : shorts, v);
        cool = 0.6;
      }
      if ((fr.gear || 0) > lastGear && lastGear > 0 && cool <= 0.38) {      // upshift flutter
        const v = clamp((rpmNorm - 0.5) / 0.4, 0, 1) * 0.55;
        if (v > 0.01) { fire(shorts, v); cool = 0.22; }
      }
      lastGear = fr.gear || 0;
      prevThr = thr;
      const loopVol = rpmNorm <= 0.4 ? 0 : (() => { const u = clamp((rpmNorm - 0.4) / 0.6, 0, 1); return 0.353 * u * u * (3 - 2 * u); })();
      sg = sm(sg, loopVol * 2.25 * 0.5 * Math.max(0.25, boost), 0.05);
      spool.set(sg, 0.5 + rpmNorm);
      const limiter = fr.rpm >= lim - 260 && fr.thr > 0.5;
      mg = sm(mg, limiter ? 0.353 * 2.25 * 0.5 : 0, 0.08);
      maxL.set(mg, 1);
    },
  };
}

/** Several rigs as one (engine + turbo), each stepping on the same frame. */
export function layered(...rigs) {
  const r = rigs.filter(Boolean);
  return { voices: r.flatMap((x) => x.voices), name: r.map((x) => x.name).join('+'), distortion: 0,
    get pops() { return r.reduce((s, x) => s + (x.pops || 0), 0); },
    step(fr) { for (const x of r) x.step(fr); } };
}

/* ---- a replay fly-by: the car past a trackside lens -------------------------
   The game's ReplayCamera: a HIGH lens 7 m off the road edge (6 m half-road ->
   13 m from the line the car drives) and 3.6 m up, panning to hold the car at
   PanLag 9/s - the AudioListener rides on that camera, so the car stays near
   the middle of the stereo image and only leads it as it sweeps past. The
   player's sources are full 3D there (EngineAudio.Ear.Outside): Unity's linear
   rolloff 6..90 m, doppler 0.6 (FMOD: velocities x dopplerLevel, c = 343),
   and an equal-power panner on the stereo takes (Web Audio's PannerNode rule
   for a stereo input, which is what the WebGL build runs). */
export function flybyProfile(c) {
  const red = c.redline, lim = c.revLimit, f = [], seg = {};
  const k4 = 175 / red;                              // km/h per rpm in 4th: ~175 km/h at redline
  let rpm = red * 0.62, x = -150;
  const push = (thr, gear = 4) => { const kmh = rpm * k4; x += kmh / 3.6 * DT; f.push({ rpm, thr, kmh, gear, x }); };
  seg.pull = 0;
  while (x < 15 && rpm < red * 0.99) { rpm += (red * 0.99 - red * 0.62) / 4.2 * DT; push(1); }
  seg.lift = f.length * DT;                          // just past the lens: lift for the corner
  for (let t = 0; t < 2.6; t += DT) { rpm = Math.max(red * 0.55, rpm - 1700 * DT); push(0); }
  seg.end = f.length * DT;
  f.seg = seg;
  return f;
}
export function renderFlyby(rig, frames, { standoff = 13, height = 3.6, minD = 6, maxD = 90, doppler = 0.6 } = {}) {
  const per = Math.round(SR * DT), out = new Float32Array(frames.length * per * CH);
  const acc = [0, 0];
  let aim = null, prevD = null;
  const C = 343;
  for (let fi = 0; fi < frames.length; fi++) {
    const fr = frames[fi];
    rig.step(fr);
    // listener at (0, height, -standoff) looking at the car (x along the road)
    const dx = fr.x, dz = standoff, d = Math.hypot(dx, dz, height);
    const want = Math.atan2(dx, dz);
    aim = aim == null ? want : aim + (want - aim) * (1 - Math.exp(-9 * DT));
    const az = clamp(want - aim, -Math.PI / 2, Math.PI / 2);          // car's azimuth in the lens's frame
    const gain = clamp(1 - (d - minD) / (maxD - minD), 0, 1);
    const vr = prevD == null ? 0 : (prevD - d) / DT;                   // closing speed, m/s
    prevD = d;
    const dop = C / (C - clamp(vr * doppler, -C * 0.5, C * 0.5));
    for (const v of rig.voices) v.dop = dop;
    // Web Audio equal-power panning of a stereo input
    const a = az * 180 / Math.PI, xp = a <= 0 ? (a + 90) / 90 : a / 90;
    const gL = Math.cos(xp * Math.PI / 2), gR = Math.sin(xp * Math.PI / 2);
    for (let s2 = 0; s2 < per; s2++) {
      acc[0] = 0; acc[1] = 0;
      for (const v of rig.voices) v.mix(acc);
      let L, R;
      if (a <= 0) { L = acc[0] + acc[1] * gL; R = acc[1] * gR; } else { L = acc[0] * gL; R = acc[1] + acc[0] * gR; }
      const n = (fi * per + s2) * 2;
      out[n] = L * gain; out[n + 1] = R * gain;
    }
  }
  return out;
}

/* ---- the owner's phase-2 A/B set: node tools/audio_render_probe.mjs ab2 ----
   Same profile as tools/audio_ab, peak-normalised to -1 dBFS, MP3 V2. The FD
   files carry the turbo layer the game puts on it (TurboAudio). */
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url) && process.argv[2] === 'ab2') {
  const OUT = path.join(REPO, 'tools/audio_ab2');
  fs.mkdirSync(OUT, { recursive: true });
  const FD = car('mazda_rx_7_type_r__fd__j___91'), ZG = car('nissan_240zg__hs30___71');
  const kmh = (c) => ({ carMaxKmh: (c.topSpeedMps || 70) * 3.6 });
  const FD_CRACKLE = 1.0;                              // EngineAudio.CrackleLevelFor: rotary
  const intGain = Number(process.env.PSX_INTERIOR_GAIN || 1);
  const only = process.argv[3] ? new Set(process.argv[3].split(',')) : null;
  const set = [
    ['1_FD_exterior_phase1_no_aggressiveness_no_crackle', () => render(layered(fixedRig('rotary_x7', FD, { ...kmh(FD), agg: false }), turboRig(FD)), profile(FD))],
    ['2_FD_exterior_with_aggressiveness_and_crackle', () => render(layered(fixedRig('rotary_x7', FD, { ...kmh(FD), crackle: FD_CRACKLE }), turboRig(FD)), profile(FD))],
    ['3_FD_interior_cockpit', () => render(layered(fixedRig('rotary_x7', FD, { ...kmh(FD), crackle: FD_CRACKLE, set: 'int', interiorGain: intGain }), turboRig(FD)), profile(FD))],
    ['4_FD_replay_flyby_trackside_lens', () => renderFlyby(layered(fixedRig('rotary_x7', FD, { ...kmh(FD), crackle: FD_CRACKLE }), turboRig(FD)), flybyProfile(FD))],
    ['5_240ZG_exterior_phase1', () => render(fixedRig('i6_japanese_2', ZG, { ...kmh(ZG), agg: false }), profile(ZG))],
    ['6_240ZG_exterior_phase2_with_aggressiveness', () => render(fixedRig('i6_japanese_2', ZG, kmh(ZG)), profile(ZG))],
  ];
  for (const [name, fn] of set) {
    if (only && !only.has(name.split('_')[0])) continue;
    const x = fn(), wavPath = path.join(OUT, name + '.wav');
    writeWav(wavPath, x, 0.891 / peakOf(x));
    execFileSync('ffmpeg', ['-v', 'error', '-y', '-i', wavPath, '-c:a', 'libmp3lame', '-q:a', '2', path.join(OUT, name + '.mp3')]);
    fs.unlinkSync(wavPath);
    console.log(name.padEnd(52), (x.length / 2 / SR).toFixed(1) + ' s', 'rms', rmsDb(x).toFixed(1), 'dB, peak', (20 * Math.log10(peakOf(x))).toFixed(1), 'dB before normalising');
  }
}
