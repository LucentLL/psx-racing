/* Offline reproduction of PSXRacing.EngineAudio.Update() — a way to hear a mix
   change without a 30-minute WebGL build. Decodes the shipped Resources/Engines
   clips with ffmpeg, runs the real band ladder at a 60 Hz control rate over an
   rpm/throttle profile, and writes WAVs. Variants at the bottom.
   Usage: node audio_render_probe.mjs   (needs ffmpeg on PATH)
   A = as shipped.  B = per-car voice restored.  C = B + AudioToneChain.  D = shipped + AudioToneChain. */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const SR = 44100, CH = 2, DT = 1 / 60;               // 60 Hz control rate
const DIR = 'C:/Users/mcgee/PSX Racing/Assets/PSXRacing/Resources/Engines/v8_american_classic_1/';
const load1 = (n) => {
  const b = execFileSync('ffmpeg', ['-v', 'error', '-i', DIR + n + '.ogg', '-ac', '2', '-ar', String(SR), '-f', 'f32le', '-'], { maxBuffer: 1 << 28 });
  return new Float32Array(b.buffer, b.byteOffset, b.byteLength >> 2);   // interleaved L,R
};
const BANDS = [
  ['idle', 0.00, 'idle', null], ['idle_low', 0.10, 'idle_low_on', 'idle_low_off'],
  ['low', 0.22, 'low_on', 'low_off'], ['low_med', 0.35, 'low_med_on', 'low_med_off'],
  ['med', 0.48, 'med_on', 'med_off'], ['med_high', 0.62, 'med_high_on', 'med_high_off'],
  ['high', 0.75, 'high_on', 'high_off'], ['very_high', 0.88, 'very_high_on', 'very_high_off'],
];
const clips = {};
for (const [, , on, off] of BANDS) { clips[on] = load1(on); if (off) clips[off] = load1(off); }
for (const n of ['intake_on', 'intake_off', 'maxRPM']) clips[n] = load1(n);

/* --- one looping voice: fractional read head, linear interpolation --------- */
class Voice {
  constructor(buf) { this.b = buf; this.n = buf.length / CH; this.p = 0; }
  mix(out, gain, rate) {
    if (gain <= 0) { this.p = (this.p + rate) % this.n; return; }
    const i0 = Math.floor(this.p), f = this.p - i0, i1 = (i0 + 1) % this.n;
    out[0] += gain * (this.b[i0 * 2] * (1 - f) + this.b[i1 * 2] * f);
    out[1] += gain * (this.b[i0 * 2 + 1] * (1 - f) + this.b[i1 * 2 + 1] * f);
    this.p = (this.p + rate) % this.n;
  }
}

const smooth = (c, t, tc, dt) => c + (t - c) * (1 - Math.exp(-dt / Math.max(tc, 1e-4)));
const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);

/* --- constants straight out of EngineAudio.cs ----------------------------- */
const HYST = 0.025, RATE_TC = 0.012, GAIN_TC = 0.04, MASTER_TC = 0.05;
const RMIN = 0.66, RMAX = 1.50, L_BASE = 0.32, L_SLOPE = 0.30, L_CAP = 0.62;
const FALL_CAP = 4200, FALL_SNAP = 5000;

/* --- the drive: idle, launch, WOT pull, upshift, pull, lift, coast -------- */
const IDLE = 700;
function profile(t) {
  if (t < 1.2) return { rpm: IDLE, load: 0 };
  if (t < 2.0) return { rpm: IDLE + (t - 1.2) / 0.8 * 800, load: 1 };
  if (t < 6.0) return { rpm: 1500 + (t - 2.0) / 4.0 * 3500, load: 1 };
  if (t < 6.15) return { rpm: 5000 - (t - 6.0) / 0.15 * 1900, load: 0.2 };
  if (t < 9.5) return { rpm: 3100 + (t - 6.15) / 3.35 * 1900, load: 1 };
  if (t < 12.5) return { rpm: 5000 - (t - 9.5) / 3.0 * 3200, load: 0 };
  return { rpm: 1800, load: 0 };
}
const DUR = 13.5;

function render({ rateMul = 1, lope = null, ladderTop = 5500, intakeLevel = 0.85,
                  intakeVolOn = (f) => 0.35 + 0.65 * f,
                  intakeVolOff = (f) => (0.35 + 0.65 * f) * 0.7,
                  intakeRateOf = (f) => 0.75 + f * 0.7,
                  tone = null }) {
  const S = ladderTop / IDLE, logSpan = Math.log(S);
  const bands = BANDS.map(([name, frac, on, off]) => ({
    name, frac, home: IDLE * Math.pow(S, frac),
    on: new Voice(clips[on]), off: off ? new Voice(clips[off]) : null,
    gain: 0, rate: 1, gOn: 0, gOff: 0, rOn: 1, rOff: 1,
  }));
  const inOn = new Voice(clips.intake_on), inOff = new Voice(clips.intake_off);
  let audible = IDLE, master = 0, lo = -1, hi = -1, iRate = 1, lopePhase = 0;
  let gInOn = 0, gInOff = 0, rIn = 1;
  const N = Math.round(DUR * SR), out = new Float32Array(N * CH);
  let nextCtl = 0;
  for (let n = 0; n < N; n++) {
    if (n >= nextCtl) {                                        // ---- control frame
      const t = n / SR, { rpm, load } = profile(t);
      if (rpm >= audible || (audible - rpm) > FALL_SNAP) audible = rpm;
      else audible = Math.max(rpm, audible - FALL_CAP * DT);
      const frac = clamp(Math.log(Math.max(audible, IDLE) / IDLE) / logSpan, 0, 1);
      const ok = lo >= 0 && frac >= bands[lo].frac - HYST && frac <= bands[hi].frac + HYST;
      if (!ok) {
        lo = 0; hi = 0;
        for (let i = 0; i < bands.length - 1; i++) {
          if (frac >= bands[i].frac && frac <= bands[i + 1].frac) { lo = i; hi = i + 1; break; }
        }
        if (frac > bands[bands.length - 1].frac) { lo = hi = bands.length - 1; }
      }
      master = smooth(master, Math.min(L_CAP, L_BASE + L_SLOPE * load), MASTER_TC, DT);
      const t01 = hi === lo ? 0 : clamp((frac - bands[lo].frac) / Math.max(bands[hi].frac - bands[lo].frac, 1e-4), 0, 1);
      let wob = 1, lvl = 1;
      if (lope) {
        lopePhase = (lopePhase + 2 * Math.PI * (audible / 60) * lope.order * DT) % (2 * Math.PI);
        const fade = clamp((lope.fadeTop - audible) / Math.max(1, lope.fadeTop - IDLE), 0, 1);
        const d = lope.depth * fade * fade;
        wob = 1 + d * Math.sin(lopePhase + lope.phase);
        lvl = 1 + d * 1.2 * Math.sin(lopePhase + lope.phase + 1.1);
      }
      const m = master * lvl;
      for (let i = 0; i < bands.length; i++) {
        const b = bands[i];
        let tgt = 0;
        if (i === lo) tgt = 1 - t01;
        if (i === hi) tgt = Math.max(tgt, t01);
        if (lo === hi && i === lo) tgt = 1;
        b.gain = smooth(b.gain, tgt, GAIN_TC, DT);
        b.rate = smooth(b.rate, clamp(audible / Math.max(b.home, 1) * rateMul * wob, RMIN, RMAX), RATE_TC, DT);
        const g = b.gain * m;
        if (b.off) {
          const a = g * load, c = g * (1 - load);
          if (a >= 0.0005) { b.gOn = a; b.rOn = b.rate; } else b.gOn = 0;
          if (c >= 0.0005) { b.gOff = c; b.rOff = b.rate; } else b.gOff = 0;
        } else {
          if (g >= 0.0005) { b.gOn = g; b.rOn = b.rate; } else b.gOn = 0;
          b.gOff = 0;
        }
      }
      iRate = smooth(iRate, clamp(intakeRateOf(frac), RMIN, RMAX), RATE_TC * 4, DT);
      const a = m * intakeLevel * intakeVolOn(frac) * load;
      const c = m * intakeLevel * intakeVolOff(frac) * (1 - load);
      gInOn = a >= 0.0005 ? a : 0;
      gInOff = c >= 0.0005 ? c : 0;
      rIn = iRate;
      nextCtl += Math.round(SR * DT);
    }
    const acc = [0, 0];
    for (const b of bands) { b.on.mix(acc, b.gOn, b.rOn); if (b.off) b.off.mix(acc, b.gOff, b.rOff); }
    inOn.mix(acc, gInOn, rIn); inOff.mix(acc, gInOff, rIn);
    out[n * 2] = acc[0]; out[n * 2 + 1] = acc[1];
  }
  if (!tone) return out;
  out.__voice = tone;                 // [peakHz, peakDb, voiceShelfDb]
  return applyTone(out);
}

/* --- AudioToneChain.cs, verbatim ----------------------------------------- */
function applyTone(x) {
  const Nz = (b0, b1, b2, a0, a1, a2) => ({ b0: b0 / a0, b1: b1 / a0, b2: b2 / a0, a1: a1 / a0, a2: a2 / a0, x1: 0, x2: 0, y1: 0, y2: 0 });
  const lowShelf = (f, db) => {
    const A = 10 ** (db / 40), w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w);
    const al = s / 2 * Math.sqrt((A + 1 / A) * (1 / 0.707 - 1) + 2), t = 2 * Math.sqrt(A) * al;
    return Nz(A * ((A + 1) - (A - 1) * c + t), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - t),
      (A + 1) + (A - 1) * c + t, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - t);
  };
  const highShelf = (f, db) => {
    const A = 10 ** (db / 40), w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w);
    const al = s / 2 * Math.sqrt((A + 1 / A) * (1 / 0.707 - 1) + 2), t = 2 * Math.sqrt(A) * al;
    return Nz(A * ((A + 1) + (A - 1) * c + t), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - t),
      (A + 1) - (A - 1) * c + t, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - t);
  };
  const hp = (f, q) => { const w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * q); return Nz((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al); };
  const lp = (f, q) => { const w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * q); return Nz((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al); };
  const pk = (f, db, q) => { const A = 10 ** (db / 40), w = 2 * Math.PI * f / SR, c = Math.cos(w), s = Math.sin(w), al = s / (2 * q); return Nz(1 + al * A, -2 * c, 1 - al * A, 1 + al / A, -2 * c, 1 - al / A); };
  // The seven shipping stages, in signal order. peakHz/peakDb/voiceShelfDb are
  // the CAR's; everything else is the fixed PSX character.
  const [phz, pdb, sdb] = x.__voice || [280, 3.0, 0];
  const mk = () => [hp(40, 0.707), lowShelf(200, 5.0), pk(phz, pdb, 0.9),
                    highShelf(2600, sdb), pk(3200, -4.0, 1.0), highShelf(6500, -2.0),
                    lp(11000, 0.707)];
  const L = mk(), R = mk(), drive = 1.30, trim = 0.90;
  const run = (q, v) => { const y = q.b0 * v + q.b1 * q.x1 + q.b2 * q.x2 - q.a1 * q.y1 - q.a2 * q.y2; q.x2 = q.x1; q.x1 = v; q.y2 = q.y1; q.y1 = y; return y; };
  for (let n = 0; n < x.length; n += 2) {
    let l = x[n], r = x[n + 1];
    for (const q of L) l = run(q, l);
    for (const q of R) r = run(q, r);
    x[n] = Math.tanh(l * drive) * trim; x[n + 1] = Math.tanh(r * drive) * trim;
  }
  return x;
}

function wav(path, x) {
  const bytes = x.length * 2, buf = Buffer.alloc(44 + bytes);
  buf.write('RIFF', 0); buf.writeUInt32LE(36 + bytes, 4); buf.write('WAVE', 8); buf.write('fmt ', 12);
  buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(CH, 22); buf.writeUInt32LE(SR, 24);
  buf.writeUInt32LE(SR * CH * 2, 28); buf.writeUInt16LE(CH * 2, 32); buf.writeUInt16LE(16, 34);
  buf.write('data', 36); buf.writeUInt32LE(bytes, 40);
  let peak = 0, sum = 0;
  for (let i = 0; i < x.length; i++) {
    const v = clamp(x[i], -1, 1);
    if (Math.abs(x[i]) > peak) peak = Math.abs(x[i]);
    sum += v * v;
    buf.writeInt16LE(Math.round(v * 32767), 44 + i * 2);
  }
  fs.writeFileSync(path, buf);
  console.log(path.padEnd(34), 'peak', peak.toFixed(3), ' rms', (10 * Math.log10(sum / x.length)).toFixed(1), 'dB');
}

// Exactly what the patched EngineAudio + CarSpec now do for this car.
const LOPE = { depth: 0.0142, order: 0.5, fadeTop: 2500, phase: 1.7 };
const CHARGER_VOICE = [290, 2.8, 0.5];        // peakHz, peakDb, shelfDb
const FIX = {
  rateMul: 0.8758, lope: LOPE, ladderTop: 5000, intakeLevel: 0.22,
  intakeVolOn: (f) => f,
  intakeVolOff: (f) => clamp((f - 0.3) / 0.7, 0, 1) * 0.4,
  intakeRateOf: () => 0.8758,                 // vendor plays intake at fixed pitch
};
wav('charger_A_as_shipped.wav', render({}));
wav('charger_B_voice_restored.wav', render({ ...FIX }));
wav('charger_C_voice_plus_tone.wav', render({ ...FIX, tone: CHARGER_VOICE }));
wav('charger_D_shipped_plus_tone.wav', render({ tone: [280, 3.0, 0] }));
