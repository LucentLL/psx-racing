using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Master-bus tone shaping. Lives on the AudioListener, so OnAudioFilterRead
    /// hands it the final mix after every source is summed.
    ///
    /// Unity has no parametric EQ component — only low/high-pass — so any
    /// shaping of the final mix has to be done as biquads here.
    ///
    /// The chain is: high pass -> low shelf -> peaking -> high shelf -> peaking
    /// cut -> high shelf -> low pass -> soft saturation. Since 2026-10-16 the
    /// five middle stages sit at 0 dB (see the note on the fields), leaving the
    /// 40 Hz high pass, the 11 kHz band limit and the tanh drive.
    /// Seven biquads on a stereo bus is a few hundred thousand ops a second,
    /// which is nothing even on a phone.
    ///
    /// ---------------------------------------------------------------------
    /// THE WEB BUILD DOES NOT GET OnAudioFilterRead, AND NOTHING SAYS SO.
    ///
    /// Unity's Web audio backend is a thin wrapper over WebAudio: it creates a
    /// buffer source and a gain per AudioSource, wires the gain straight to
    /// audioContext.destination, and exposes nothing but play/stop/volume/
    /// pitch/pan. The shipped framework contains zero createScriptProcessor,
    /// zero AudioWorklet and zero createBiquadFilter — there is no path for a
    /// managed DSP callback to exist, so this component ran in the editor and
    /// silently did NOTHING in the browser. Measured, that is 9.0 dB of tilt
    /// between the engine fundamental and the whine band, plus the saturation:
    /// the whole difference between "beefy" and "whiny and fake" on the
    /// deployed build, for content that sounded right locally.
    ///
    /// So on Web the same coefficients are handed to Plugins/WebGL/
    /// PSXToneChain.jslib, which rebuilds the chain as IIRFilterNodes and
    /// splices it in front of the destination. One set of numbers, two
    /// runtimes; Build() below is the only place they are computed.
    /// ---------------------------------------------------------------------
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public class AudioToneChain : MonoBehaviour
    {
        // THE ENGINE TILT IS GONE (2026-10-16). The low shelf (+5 dB at 200 Hz),
        // the "whine" cut (-4 dB at 3.2 kHz), the top shelf (-2 dB at 6.5 kHz)
        // and the per-car formant/rasp (RG2's engineVoice.ts, written for a
        // procedural synth) were all there to fight a whine. Measured on
        // 2026-10-16, that whine was the old engine voice itself: a ladder that
        // played every take 4-11 semitones sharp, and an intake take held at a
        // fixed pitch. With the voice now following the vendor's own rig, the
        // same chain pushed it 5-12 dB BELOW the vendor reference in the
        // 4k/8k/16k octaves and 3-4 dB above it at 125 Hz - away from the sound
        // the owner named as correct - and the 240ZG's 460 Hz formant sat right
        // on its 498 Hz intake line. The stages stay (the Web plugin and the
        // editor path both run the chain) but at 0 dB, and NOT serialized:
        // every baked scene carries the old numbers on its listener, and a
        // serialized field would quietly put them back.
        [Header("High pass — recover headroom below the engine")]
        public float highPassHz = 40f;

        [System.NonSerialized] public float lowShelfHz = 200f;
        [System.NonSerialized] public float lowShelfDb = 0f;
        [System.NonSerialized] public float peakHz = 1000f;
        [System.NonSerialized] public float peakDb = 0f;
        [System.NonSerialized] public float peakQ = 0.9f;
        [System.NonSerialized] public float voiceShelfHz = 2600f;
        [System.NonSerialized] public float voiceShelfDb = 0f;
        [System.NonSerialized] public float cutHz = 3200f;
        [System.NonSerialized] public float cutDb = 0f;
        [System.NonSerialized] public float cutQ = 1.0f;
        [System.NonSerialized] public float highShelfHz = 6500f;
        [System.NonSerialized] public float highShelfDb = 0f;

        [Header("Low pass — PSX-era band limit")]
        public float lowPassHz = 11000f;

        [Header("Drive")]
        /// <summary>Pre-saturation gain. Above 1 the tanh curve starts rounding
        /// peaks, which thickens the mid-bass and raises perceived loudness far
        /// more than a straight volume increase would.</summary>
        public float drive = 1.30f;
        [Range(0f, 1f)] public float outputTrim = 0.90f;

        struct Biquad
        {
            public float b0, b1, b2, a1, a2;
            public float x1, x2, y1, y2;

            public float Process(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }
        }

        // Seven stages per channel, in signal order: highpass -> low shelf ->
        // body peak -> voice shelf -> whine cut -> high shelf -> lowpass, then
        // tanh. One array so the editor loop and the Web plugin cannot drift.
        Biquad[] chainL, chainR;
        bool ready;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int PSXToneChainInstall(string json);
        /// <summary>Sample rate the live coefficients were cooked at, and the
        /// one the browser reported. Zero until the plugin has a context.</summary>
        int installedSr;
        float retryAt;
#endif

        void OnEnable() => Build();

        void Build() => Build(AudioSettings.outputSampleRate);

        void Build(int sr)
        {
            if (sr <= 0) sr = 48000;
            var stages = new[]
            {
                HighPass(highPassHz, 0.707f, sr),
                LowShelf(lowShelfHz, lowShelfDb, sr),
                Peaking(peakHz, peakDb, peakQ, sr),
                HighShelf(voiceShelfHz, voiceShelfDb, sr),
                Peaking(cutHz, cutDb, cutQ, sr),
                HighShelf(highShelfHz, highShelfDb, sr),
                LowPass(lowPassHz, 0.707f, sr),
            };
            chainL = (Biquad[])stages.Clone();
            chainR = (Biquad[])stages.Clone();
            ready = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            InstallWeb(stages, sr);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        void InstallWeb(Biquad[] stages, int sr)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"stages\":[");
            for (int i = 0; i < stages.Length; i++)
            {
                var q = stages[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(F(q.b0)).Append(',').Append(F(q.b1)).Append(',')
                  .Append(F(q.b2)).Append(',').Append(F(q.a1)).Append(',').Append(F(q.a2)).Append(']');
            }
            sb.Append("],\"drive\":").Append(F(drive))
              .Append(",\"trim\":").Append(F(outputTrim)).Append('}');

            int ctxSr = 0;
            try { ctxSr = PSXToneChainInstall(sb.ToString()); }
            catch (System.Exception e)
            {
                Debug.LogWarning("AudioToneChain: web install failed - " + e.Message);
                return;
            }

            installedSr = ctxSr;
            // 0 means the browser has not built an AudioContext yet, which is
            // normal before the first user gesture. Retry from Update.
            if (ctxSr == 0) { retryAt = Time.unscaledTime + 0.5f; return; }
            retryAt = 0f;
            // AudioSettings.outputSampleRate is not always what the browser
            // actually runs at, and cooking a 200 Hz shelf against 44100 while
            // the context runs at 48000 puts it at 218 Hz. Re-cook once — the
            // flag is there because this is a mutual call with Build and a
            // browser that ever answered differently twice would spin.
            if (ctxSr != sr && !recooking)
            {
                recooking = true;
                Build(ctxSr);
                recooking = false;
            }
        }

        bool recooking;

        void Update()
        {
            if (retryAt <= 0f || Time.unscaledTime < retryAt) return;
            retryAt = Time.unscaledTime + 0.5f;
            Build(installedSr > 0 ? installedSr : AudioSettings.outputSampleRate);
        }

        static string F(float v) =>
            v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
#endif

        // ---- RBJ audio-EQ cookbook coefficients ----------------------------
        static Biquad Normalize(float b0, float b1, float b2, float a0, float a1, float a2)
        {
            var q = new Biquad();
            q.b0 = b0 / a0; q.b1 = b1 / a0; q.b2 = b2 / a0;
            q.a1 = a1 / a0; q.a2 = a2 / a0;
            return q;
        }

        static Biquad LowShelf(float f0, float dbGain, int sr)
        {
            float A = Mathf.Pow(10f, dbGain / 40f);
            float w0 = 2f * Mathf.PI * f0 / sr;
            float cos = Mathf.Cos(w0), sin = Mathf.Sin(w0);
            float alpha = sin / 2f * Mathf.Sqrt((A + 1f / A) * (1f / 0.707f - 1f) + 2f);
            float twoSqrtAalpha = 2f * Mathf.Sqrt(A) * alpha;
            return Normalize(
                A * ((A + 1f) - (A - 1f) * cos + twoSqrtAalpha),
                2f * A * ((A - 1f) - (A + 1f) * cos),
                A * ((A + 1f) - (A - 1f) * cos - twoSqrtAalpha),
                (A + 1f) + (A - 1f) * cos + twoSqrtAalpha,
                -2f * ((A - 1f) + (A + 1f) * cos),
                (A + 1f) + (A - 1f) * cos - twoSqrtAalpha);
        }

        static Biquad HighShelf(float f0, float dbGain, int sr)
        {
            float A = Mathf.Pow(10f, dbGain / 40f);
            float w0 = 2f * Mathf.PI * f0 / sr;
            float cos = Mathf.Cos(w0), sin = Mathf.Sin(w0);
            float alpha = sin / 2f * Mathf.Sqrt((A + 1f / A) * (1f / 0.707f - 1f) + 2f);
            float twoSqrtAalpha = 2f * Mathf.Sqrt(A) * alpha;
            return Normalize(
                A * ((A + 1f) + (A - 1f) * cos + twoSqrtAalpha),
                -2f * A * ((A - 1f) + (A + 1f) * cos),
                A * ((A + 1f) + (A - 1f) * cos - twoSqrtAalpha),
                (A + 1f) - (A - 1f) * cos + twoSqrtAalpha,
                2f * ((A - 1f) - (A + 1f) * cos),
                (A + 1f) - (A - 1f) * cos - twoSqrtAalpha);
        }

        static Biquad HighPass(float f0, float q, int sr)
        {
            float w0 = 2f * Mathf.PI * f0 / sr;
            float cos = Mathf.Cos(w0), sin = Mathf.Sin(w0);
            float alpha = sin / (2f * q);
            return Normalize(
                (1f + cos) / 2f, -(1f + cos), (1f + cos) / 2f,
                1f + alpha, -2f * cos, 1f - alpha);
        }

        static Biquad LowPass(float f0, float q, int sr)
        {
            float w0 = 2f * Mathf.PI * f0 / sr;
            float cos = Mathf.Cos(w0), sin = Mathf.Sin(w0);
            float alpha = sin / (2f * q);
            return Normalize(
                (1f - cos) / 2f, 1f - cos, (1f - cos) / 2f,
                1f + alpha, -2f * cos, 1f - alpha);
        }

        static Biquad Peaking(float f0, float dbGain, float q, int sr)
        {
            float A = Mathf.Pow(10f, dbGain / 40f);
            float w0 = 2f * Mathf.PI * f0 / sr;
            float cos = Mathf.Cos(w0), sin = Mathf.Sin(w0);
            float alpha = sin / (2f * q);
            return Normalize(
                1f + alpha * A, -2f * cos, 1f - alpha * A,
                1f + alpha / A, -2f * cos, 1f - alpha / A);
        }

        // Never called on Web — see the class note. The plugin carries the same
        // coefficients there.
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!ready) return;
            var L = chainL; var R = chainR;
            int stages = L.Length;

            for (int n = 0; n < data.Length; n += channels)
            {
                float l = data[n];
                for (int s = 0; s < stages; s++) l = L[s].Process(l);
                l = (float)System.Math.Tanh(l * drive) * outputTrim;
                data[n] = l;

                if (channels > 1)
                {
                    float r = data[n + 1];
                    for (int s = 0; s < stages; s++) r = R[s].Process(r);
                    r = (float)System.Math.Tanh(r * drive) * outputTrim;
                    data[n + 1] = r;

                    // Any further channels just mirror the right, rather than
                    // being left unprocessed and sounding detached.
                    for (int c = 2; c < channels; c++) data[n + c] = r;
                }
            }
        }
    }
}
