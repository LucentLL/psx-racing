using UnityEngine;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// The sounds of getting out of a car and filling it up: a door latch and
    /// thunk, the exhaust ticking as it cools, and a fuel pump running.
    ///
    /// GENERATED, not sampled. The project ships 560 engine clips and not one
    /// door, and the two that matter most here — the engine starting and
    /// stopping — already exist as real recordings on <see cref="EngineAudio"/>
    /// (<c>PlayStartup</c> / <c>PlayShutdown</c>), so the gap is small and
    /// exactly the shape <see cref="CollisionAudio"/> already fills the same
    /// way: short, percussive, physical noises that are cheaper to synthesise
    /// than to license.
    ///
    /// Everything is built once and cached statically — a door is the same door
    /// on every circuit.
    /// </summary>
    public static class FootAudio
    {
        const int SampleRate = 22050;

        static AudioClip doorOpen, doorClose, crackle, pumpLoop;

        /// <summary>The handle, the hinge and the seal letting go. Short and
        /// dry — a car door opening is mostly mechanism, not resonance.</summary>
        public static AudioClip DoorOpen => doorOpen != null ? doorOpen : doorOpen = MakeDoorOpen();

        /// <summary>The one everybody knows: a soft thump with a metallic latch
        /// on top of it. The thump carries the weight of the door and the latch
        /// carries the fact that it CLOSED, which is the half a player is
        /// actually listening for.</summary>
        public static AudioClip DoorClose => doorClose != null ? doorClose : doorClose = MakeDoorClose();

        /// <summary>Hot metal contracting after shutdown — the irregular tick
        /// of an exhaust cooling. Random spacing on purpose: an even one reads
        /// as a machine still running.</summary>
        public static AudioClip Crackle => crackle != null ? crackle : crackle = MakeCrackle();

        /// <summary>A pump delivering: the motor's hum, the shudder of the
        /// hose, and the click of the litre counter turning over. Loops.
        /// </summary>
        public static AudioClip PumpLoop => pumpLoop != null ? pumpLoop : pumpLoop = MakePumpLoop();

        // ------------------------------------------------------------------
        //  doors
        // ------------------------------------------------------------------
        static AudioClip MakeDoorOpen()
        {
            const float dur = 0.42f;
            int n = Mathf.RoundToInt(SampleRate * dur);
            var buf = new float[n];
            var rnd = new System.Random(9111);

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;

                // The handle: a hard little click right at the start.
                if (t < 0.05f)
                    buf[i] += 0.5f * Mathf.Exp(-90f * t) *
                              (float)(rnd.NextDouble() * 2.0 - 1.0);

                // The seal peeling off the frame — a short breath of noise that
                // rises as the door swings.
                if (t > 0.04f && t < 0.30f)
                {
                    float u = (t - 0.04f) / 0.26f;
                    buf[i] += 0.16f * Mathf.Sin(u * Mathf.PI) *
                              (float)(rnd.NextDouble() * 2.0 - 1.0);
                }

                // The hinge, low and creaking, sliding up a little as it opens.
                if (t < 0.34f)
                {
                    float f = 210f + 90f * (t / 0.34f);
                    buf[i] += 0.10f * Mathf.Exp(-6f * t) * Mathf.Sin(2f * Mathf.PI * f * t);
                }
            }

            LowPass(buf, 3800f);
            NormalizeAndFade(buf, 0.75f, Mathf.RoundToInt(SampleRate * 0.02f));
            var clip = AudioClip.Create("psx_door_open", n, 1, SampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        static AudioClip MakeDoorClose()
        {
            const float dur = 0.5f;
            int n = Mathf.RoundToInt(SampleRate * dur);
            var buf = new float[n];
            var rnd = new System.Random(4242);

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;

                // The body thump: two low partials, heavily damped. This is the
                // mass of the door arriving.
                buf[i] += 0.85f * Mathf.Exp(-26f * t) * Mathf.Sin(2f * Mathf.PI * 62f * t);
                buf[i] += 0.35f * Mathf.Exp(-34f * t) * Mathf.Sin(2f * Mathf.PI * 96f * t);

                // The latch, a few milliseconds behind the thump and much
                // brighter. Without it the door reads as a box being dropped.
                float lt = t - 0.012f;
                if (lt > 0f)
                {
                    buf[i] += 0.30f * Mathf.Exp(-120f * lt) *
                              (float)(rnd.NextDouble() * 2.0 - 1.0);
                    buf[i] += 0.18f * Mathf.Exp(-70f * lt) * Mathf.Sin(2f * Mathf.PI * 1650f * lt);
                }

                // Cabin rattle: the trim answering the thump, gone in a blink.
                if (t < 0.14f)
                    buf[i] += 0.08f * Mathf.Exp(-30f * t) * Mathf.Sin(2f * Mathf.PI * 380f * t);
            }

            LowPass(buf, 5200f);
            NormalizeAndFade(buf, 0.95f, Mathf.RoundToInt(SampleRate * 0.02f));
            var clip = AudioClip.Create("psx_door_close", n, 1, SampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        // ------------------------------------------------------------------
        //  cooling exhaust
        // ------------------------------------------------------------------
        static AudioClip MakeCrackle()
        {
            const float dur = 3.4f;
            int n = Mathf.RoundToInt(SampleRate * dur);
            var buf = new float[n];
            var rnd = new System.Random(7734);

            // Ticks thin out as the metal cools, so the gaps grow. An even
            // spacing sounds like an indicator, not a hot exhaust.
            float t = 0.06f;
            float gap = 0.055f;
            while (t < dur - 0.1f)
            {
                int at = Mathf.RoundToInt(t * SampleRate);
                // Each tick is its own little resonance — pitch and level vary,
                // because no two pieces of contracting steel are the same.
                float freq = 900f + (float)rnd.NextDouble() * 2600f;
                float amp = 0.25f + (float)rnd.NextDouble() * 0.6f;
                // Quieter as it cools.
                amp *= Mathf.Lerp(1f, 0.25f, t / dur);
                int len = Mathf.Min(n - at, Mathf.RoundToInt(SampleRate * 0.05f));
                for (int i = 0; i < len; i++)
                {
                    float lt = i / (float)SampleRate;
                    buf[at + i] += amp * Mathf.Exp(-150f * lt) *
                                   Mathf.Sin(2f * Mathf.PI * freq * lt);
                    buf[at + i] += amp * 0.4f * Mathf.Exp(-260f * lt) *
                                   (float)(rnd.NextDouble() * 2.0 - 1.0);
                }
                gap *= 1.06f + (float)rnd.NextDouble() * 0.10f;
                t += gap;
            }

            NormalizeAndFade(buf, 0.5f, Mathf.RoundToInt(SampleRate * 0.03f));
            var clip = AudioClip.Create("psx_exhaust_crackle", n, 1, SampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        // ------------------------------------------------------------------
        //  the pump
        // ------------------------------------------------------------------
        /// <summary>
        /// Seamless, by the same trick the scrape loop uses: generate a little
        /// extra and crossfade the tail back over the head. Anything with noise
        /// in it clicks at the seam otherwise.
        /// </summary>
        static AudioClip MakePumpLoop()
        {
            const float dur = 1.6f;
            int n = Mathf.RoundToInt(SampleRate * dur);
            int fade = Mathf.RoundToInt(SampleRate * 0.08f);
            var raw = new float[n + fade];
            var rnd = new System.Random(2255);

            float low = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)SampleRate;

                // The motor: a mains hum with its second harmonic, which is
                // what a pump under a canopy actually sounds like from a metre
                // away.
                float v = 0.30f * Mathf.Sin(2f * Mathf.PI * 51f * t)
                        + 0.16f * Mathf.Sin(2f * Mathf.PI * 102f * t)
                        + 0.07f * Mathf.Sin(2f * Mathf.PI * 153f * t);

                // Fuel moving through the hose: filtered noise, gently pulsing
                // with the pump's own stroke.
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.06f;                 // one-pole low pass
                float stroke = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 8.5f * t);
                v += low * 0.55f * stroke;

                raw[i] = v;
            }

            // The litre counter, ticking over four times a second. Placed after
            // the hum so the crossfade below carries it too.
            for (float ct = 0.11f; ct < dur + 0.08f; ct += 0.25f)
            {
                int at = Mathf.RoundToInt(ct * SampleRate);
                int len = Mathf.Min(raw.Length - at, Mathf.RoundToInt(SampleRate * 0.02f));
                for (int i = 0; i < len; i++)
                {
                    float lt = i / (float)SampleRate;
                    raw[at + i] += 0.22f * Mathf.Exp(-300f * lt) *
                                   Mathf.Sin(2f * Mathf.PI * 2400f * lt);
                }
            }

            var buf = new float[n];
            for (int i = 0; i < n; i++) buf[i] = raw[i];
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                buf[n - fade + i] = Mathf.Lerp(raw[n - fade + i], raw[i], w);
            }

            Normalize(buf, 0.55f);
            var clip = AudioClip.Create("psx_fuel_pump", n, 1, SampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        // ------------------------------------------------------------------
        //  helpers
        // ------------------------------------------------------------------
        /// <summary>One-pole low pass. These are PS1-era noises and the top end
        /// is where synthesis gives itself away.</summary>
        static void LowPass(float[] buf, float cutoff)
        {
            float a = Mathf.Clamp01(cutoff / SampleRate);
            float y = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                y += (buf[i] - y) * a;
                buf[i] = y;
            }
        }

        static void Normalize(float[] buf, float peak)
        {
            float max = 0f;
            for (int i = 0; i < buf.Length; i++) max = Mathf.Max(max, Mathf.Abs(buf[i]));
            if (max < 1e-6f) return;
            float g = peak / max;
            for (int i = 0; i < buf.Length; i++) buf[i] *= g;
        }

        static void NormalizeAndFade(float[] buf, float peak, int fadeOut)
        {
            Normalize(buf, peak);
            for (int i = 0; i < fadeOut && i < buf.Length; i++)
            {
                int k = buf.Length - 1 - i;
                buf[k] *= i / (float)fadeOut;
            }
        }
    }
}
