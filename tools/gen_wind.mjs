/**
 * Render the two wind-bed loops WindAudio.cs plays under the engine.
 *
 *   node gen_wind.mjs          writes Assets/PSXRacing/Resources/Sfx/wind_low.wav
 *                                 and                            .../wind_high.wav
 *
 * Why generated and not recorded: the layer is volume-and-pitch only (no
 * filter reaches the browser — see AudioToneChain.cs), so the SPECTRUM of
 * each loop is the whole design. LOW is the pressure rumble that comes in at
 * town speed; HIGH is the hiss that arrives at motorway speed and pitches up.
 * Their crossfade is what "opens the filter" with speed.
 *
 * Both are seamless by construction: the tail is crossfaded into the head
 * over CROSS_MS so the loop point is continuous whatever the importer's
 * Vorbis pass does to it (LoopSeam repairs it again at runtime regardless).
 * Mono, short and 44.1 kHz on purpose — every playing loop costs one
 * decompressed copy, and these two play for the whole race.
 *
 * Deterministic (seeded PRNG) so a re-run writes byte-identical files and the
 * asset database does not reimport them for nothing. Same WAV writer as
 * audio_render_probe.mjs.
 */
import fs from 'node:fs';
import path from 'node:path';

const OUT_DIR = 'C:/Users/mcgee/PSX Racing/Assets/PSXRacing/Resources/Sfx';
const SR = 44100;
const CROSS_MS = 60;

// ---- deterministic noise ---------------------------------------------------
function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Paul Kellet's pink-noise filter: -3 dB/oct, close enough to real wind's
// falling spectrum that the band-passes below only have to shape the edges.
function pink(n, rnd) {
  const out = new Float32Array(n);
  let b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
  for (let i = 0; i < n; i++) {
    const w = rnd() * 2 - 1;
    b0 = 0.99886 * b0 + w * 0.0555179;
    b1 = 0.99332 * b1 + w * 0.0750759;
    b2 = 0.96900 * b2 + w * 0.1538520;
    b3 = 0.86650 * b3 + w * 0.3104856;
    b4 = 0.55000 * b4 + w * 0.5329522;
    b5 = -0.7616 * b5 - w * 0.0168980;
    out[i] = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362) * 0.11;
    b6 = w * 0.115926;
  }
  return out;
}

// ---- RBJ cookbook biquads ----------------------------------------------------
function biquad(type, f0, q) {
  const w0 = 2 * Math.PI * f0 / SR, cs = Math.cos(w0), sn = Math.sin(w0), al = sn / (2 * q);
  let b0, b1, b2, a0, a1, a2;
  if (type === 'lp') { b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = (1 - cs) / 2; }
  else { b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = (1 + cs) / 2; }
  a0 = 1 + al; a1 = -2 * cs; a2 = 1 - al;
  return { b0: b0 / a0, b1: b1 / a0, b2: b2 / a0, a1: a1 / a0, a2: a2 / a0, x1: 0, x2: 0, y1: 0, y2: 0 };
}
function run(q, x) {
  const y = q.b0 * x + q.b1 * q.x1 + q.b2 * q.x2 - q.a1 * q.y1 - q.a2 * q.y2;
  q.x2 = q.x1; q.x1 = x; q.y2 = q.y1; q.y1 = y;
  return y;
}
function filter(x, stages) {
  const y = new Float32Array(x.length);
  for (let i = 0; i < x.length; i++) {
    let v = x[i];
    for (const q of stages) v = run(q, v);
    y[i] = v;
  }
  return y;
}

// ---- loop finishing --------------------------------------------------------
function seamless(x) {
  // Render longer than needed, then fold the tail over the head so the loop
  // point is a crossfade rather than a cut.
  const cross = Math.round(SR * CROSS_MS / 1000);
  const n = x.length - cross;
  const y = new Float32Array(n);
  for (let i = 0; i < n; i++) y[i] = x[i];
  for (let i = 0; i < cross; i++) {
    const t = i / cross;
    const w = 0.5 - 0.5 * Math.cos(Math.PI * t);      // raised cosine
    y[i] = x[i] * w + x[n + i] * (1 - w);
  }
  return y;
}
function normalize(x, peak) {
  let m = 0;
  for (const v of x) m = Math.max(m, Math.abs(v));
  const g = m > 0 ? peak / m : 1;
  for (let i = 0; i < x.length; i++) x[i] *= g;
  return x;
}
function wav(file, x) {
  const CH = 1, bytes = x.length * 2, buf = Buffer.alloc(44 + bytes);
  buf.write('RIFF', 0); buf.writeUInt32LE(36 + bytes, 4); buf.write('WAVE', 8); buf.write('fmt ', 12);
  buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(CH, 22); buf.writeUInt32LE(SR, 24);
  buf.writeUInt32LE(SR * CH * 2, 28); buf.writeUInt16LE(CH * 2, 32); buf.writeUInt16LE(16, 34);
  buf.write('data', 36); buf.writeUInt32LE(bytes, 40);
  let sum = 0;
  for (let i = 0; i < x.length; i++) {
    const v = Math.max(-1, Math.min(1, x[i]));
    sum += v * v;
    buf.writeInt16LE(Math.round(v * 32767), 44 + i * 2);
  }
  fs.writeFileSync(file, buf);
  console.log(path.basename(file).padEnd(16), (x.length / SR).toFixed(2) + ' s',
              ' rms', (10 * Math.log10(sum / x.length)).toFixed(1), 'dB');
}

// ---- the two beds ------------------------------------------------------------
fs.mkdirSync(OUT_DIR, { recursive: true });
const cross = Math.round(SR * CROSS_MS / 1000);

// LOW: 60-400 Hz. Two lowpass stages at 400 Hz (24 dB/oct) so nothing of it
// competes with the engine's formant band, one highpass at 60 Hz so it does
// not pump the tone chain's +7.5 dB shelf at 110 Hz with sub-bass.
{
  const n = Math.round(SR * 2.0) + cross;
  const src = pink(n, mulberry32(0x57494E44));
  const y = filter(src, [biquad('lp', 400, 0.707), biquad('lp', 400, 0.707), biquad('hp', 60, 0.707)]);
  wav(path.join(OUT_DIR, 'wind_low.wav'), normalize(seamless(y), 0.85));
}

// HIGH: 800 Hz-6 kHz. Pitched up to 1.15x at speed by WindAudio, which puts
// its top at 6.9 kHz — still well inside what a phone speaker reproduces.
{
  const n = Math.round(SR * 1.6) + cross;
  const src = pink(n, mulberry32(0x48495353));
  const y = filter(src, [biquad('hp', 800, 0.707), biquad('hp', 800, 0.707),
                         biquad('lp', 6000, 0.707), biquad('lp', 6000, 0.707)]);
  wav(path.join(OUT_DIR, 'wind_high.wav'), normalize(seamless(y), 0.85));
}
