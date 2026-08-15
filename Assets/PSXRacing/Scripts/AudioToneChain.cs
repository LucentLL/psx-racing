using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Master-bus tone shaping. Lives on the AudioListener, so OnAudioFilterRead
    /// hands it the final mix after every source is summed.
    ///
    /// Unity has no parametric EQ component — only low/high-pass — so the tone
    /// offsets that the source game applied per car (a peaking formant around
    /// 620 Hz and a high shelf at 2.6 kHz for the 13B's "smooth high rotary
    /// brap") have to be done as biquads here.
    ///
    /// The chain is: low shelf (weight) -> peaking (body/formant) -> high shelf
    /// (air) -> soft saturation (glue and perceived loudness). Four biquads on a
    /// stereo bus is a few hundred thousand ops a second, which is nothing even
    /// on a phone.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public class AudioToneChain : MonoBehaviour
    {
        // The previous settings were measured to produce only 1.0 dB of tilt
        // between the engine's fundamental and the whine band — effectively a
        // no-op. Two reasons: an RBJ shelf delivers only HALF its nominal dB at
        // its own corner frequency (so "+7.5 dB at 110 Hz" was +2.16 dB at the
        // 170 Hz where the fundamental actually sits), and the high shelf was
        // *boosting* 3-5 kHz, which is exactly where "whiny" lives.
        // These values target 9.0 dB of tilt instead.
        [Header("High pass — recover headroom below the engine")]
        public float highPassHz = 40f;

        [Header("Low shelf — weight and bass")]
        public float lowShelfHz = 200f;
        public float lowShelfDb = 5.0f;

        [Header("Peaking — rotary formant / body")]
        public float peakHz = 280f;
        public float peakDb = 3.0f;
        public float peakQ = 0.8f;

        [Header("Peaking CUT — the whine")]
        public float cutHz = 3200f;
        public float cutDb = -4.0f;
        public float cutQ = 1.0f;

        [Header("High shelf — tame the top")]
        public float highShelfHz = 6500f;
        public float highShelfDb = -2.0f;

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

        // Six stages per channel, in this order: highpass -> low shelf -> body
        // peak -> whine cut -> high shelf -> lowpass -> tanh.
        Biquad hpL, hpR, lowL, lowR, midL, midR, cutL, cutR, highL, highR, lpL, lpR;
        bool ready;

        void OnEnable() => Build();

        void Build()
        {
            int sr = AudioSettings.outputSampleRate;
            if (sr <= 0) sr = 48000;
            var hp = HighPass(highPassHz, 0.707f, sr);
            var low = LowShelf(lowShelfHz, lowShelfDb, sr);
            var mid = Peaking(peakHz, peakDb, peakQ, sr);
            var cut = Peaking(cutHz, cutDb, cutQ, sr);
            var high = HighShelf(highShelfHz, highShelfDb, sr);
            var lp = LowPass(lowPassHz, 0.707f, sr);
            hpL = hp; hpR = hp;
            lowL = low; lowR = low;
            midL = mid; midR = mid;
            cutL = cut; cutR = cut;
            highL = high; highR = high;
            lpL = lp; lpR = lp;
            ready = true;
        }

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

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!ready) return;

            for (int n = 0; n < data.Length; n += channels)
            {
                float l = data[n];
                l = hpL.Process(l);
                l = lowL.Process(l);
                l = midL.Process(l);
                l = cutL.Process(l);
                l = highL.Process(l);
                l = lpL.Process(l);
                l = (float)System.Math.Tanh(l * drive) * outputTrim;
                data[n] = l;

                if (channels > 1)
                {
                    float r = data[n + 1];
                    r = hpR.Process(r);
                    r = lowR.Process(r);
                    r = midR.Process(r);
                    r = cutR.Process(r);
                    r = highR.Process(r);
                    r = lpR.Process(r);
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
