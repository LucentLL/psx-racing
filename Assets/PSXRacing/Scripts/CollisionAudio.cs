using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Impact and scrape voices for <see cref="CollisionResponder"/>.
    ///
    /// The audio pack shipped with this project contains engine, turbo and tire
    /// recordings only — there is no crash sample anywhere in Assets/PSXRacing/
    /// Audio. Rather than block collision feel on sourcing new WAVs, the clips
    /// are synthesised once at first use and shared statically across every car:
    /// a bandpassed noise transient for the crunch, a handful of INHARMONIC
    /// damped partials for the panel ring (harmonic ratios read as a musical
    /// note, which is exactly what a car body does not sound like), and a low
    /// damped sine for the thump you feel rather than hear.
    ///
    /// The seed is fixed, so the synthesis is deterministic — two cars hitting
    /// the same wall produce the same clip, and a build sounds like the editor.
    /// </summary>
    public class CollisionAudio : MonoBehaviour
    {
        public bool spatial = true;
        public float volumeScale = 1f;

        const int SampleRate = 44100;
        const int Tiers = 3;                 // 0 light, 1 medium, 2 heavy

        static AudioClip[] impactClips;
        static AudioClip scrapeClip;

        AudioSource[] oneShots;              // round-robin, so hits can overlap
        int nextShot;
        AudioSource scrapeSrc;

        void Awake()
        {
            EnsureClips();

            // Three voices is enough for a multi-panel crash without stealing
            // from the engine's 18-voice budget (see PSXRacingBuilder's
            // ConfigureAudioVoiceLimits — real voices are a scarce resource here).
            oneShots = new AudioSource[3];
            for (int i = 0; i < oneShots.Length; i++) oneShots[i] = MakeSource(false);

            scrapeSrc = MakeSource(true);
            scrapeSrc.clip = scrapeClip;
            scrapeSrc.volume = 0f;
            // Always playing, volume-gated: assigning .clip at runtime resets
            // timeSamples and yields phase-offset copies (see the EngineAudio
            // post-mortem). These clips are synthesised rather than imported so
            // they are ready immediately, but every loop in the game starts the
            // same way — one exception is how the next one gets missed.
            AudioLoopStarter.PlayLoop(scrapeSrc);
        }

        AudioSource MakeSource(bool loop)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = spatial ? 1f : 0f;
            if (spatial)
            {
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 5f;
                src.maxDistance = 70f;
            }
            return src;
        }

        /// <summary>Fire a one-shot sized to the hit. <paramref name="severity"/>
        /// is the velocity the impact removed, in m/s.</summary>
        public void PlayImpact(float severity)
        {
            int tier = severity < 2.5f ? 0 : (severity < 7f ? 1 : 2);
            // Loudness inside a tier still tracks the hit, so a 3 m/s and a
            // 6.5 m/s scuff are not the same event with the same volume.
            float lo = tier == 0 ? 0.4f : (tier == 1 ? 2.5f : 7f);
            float hi = tier == 0 ? 2.5f : (tier == 1 ? 7f : 16f);
            float t = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, severity));
            float vol = Mathf.Lerp(0.35f, 1f, t) * volumeScale;

            var src = oneShots[nextShot];
            nextShot = (nextShot + 1) % oneShots.Length;
            src.pitch = Random.Range(0.92f, 1.08f);   // no two panels ring alike
            src.PlayOneShot(impactClips[tier], vol);
        }

        /// <summary>Continuous grind. <paramref name="intensity"/> 0..1 from the
        /// tangential speed along the surface, <paramref name="load"/> 0..1 from
        /// how hard the car is pressed into it.</summary>
        public void SetScrape(float intensity, float load)
        {
            if (scrapeSrc == null) return;
            float target = Mathf.Clamp01(intensity) * Mathf.Clamp01(load) * 0.55f * volumeScale;
            // Attack fast so the grind starts with the contact; release slower so
            // a wall that bumps in and out does not machine-gun the voice.
            float rate = target > scrapeSrc.volume ? 22f : 7f;
            scrapeSrc.volume = Mathf.MoveTowards(scrapeSrc.volume, target, rate * Time.deltaTime);
            scrapeSrc.pitch = Mathf.Lerp(0.72f, 1.35f, Mathf.Clamp01(intensity));
        }

        // ==================== synthesis ====================

        static void EnsureClips()
        {
            if (impactClips != null) return;
            impactClips = new AudioClip[Tiers];
            for (int t = 0; t < Tiers; t++) impactClips[t] = MakeImpact(t);
            scrapeClip = MakeScrape();
        }

        /// <summary>
        /// One impact = crunch + ring + thump. Heavier tiers are LONGER, LOWER
        /// and DULLER, not merely louder: a light scuff is a bright tick, a heavy
        /// hit is a bass thud with a long panel ring after it.
        /// </summary>
        static AudioClip MakeImpact(int tier)
        {
            float dur = tier == 0 ? 0.35f : (tier == 1 ? 0.62f : 0.95f);
            int n = Mathf.RoundToInt(SampleRate * dur);
            var buf = new float[n];
            var rnd = new System.Random(1971 + tier * 977);

            // --- crunch: bandpassed white noise, near-instant attack.
            float noiseDecay = tier == 0 ? 52f : (tier == 1 ? 30f : 19f);
            float bpFreq = tier == 0 ? 2700f : (tier == 1 ? 1500f : 850f);
            float f = 2f * Mathf.Sin(Mathf.PI * bpFreq / SampleRate);
            const float q = 0.7f;                 // state-variable filter damping
            float low = 0f, band = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)(rnd.NextDouble() * 2.0 - 1.0);
                float high = white - low - q * band;
                band += f * high;
                low += f * band;
                // A 2 ms rise takes the click off the front without softening
                // the transient into a "whoomph".
                float attack = Mathf.Clamp01(t / 0.002f);
                buf[i] += band * Mathf.Exp(-noiseDecay * t) * attack * 0.9f;
            }

            // --- panel ring: inharmonic damped partials.
            float ringBase = tier == 0 ? 320f : (tier == 1 ? 210f : 148f);
            float[] ratios = { 1f, 1.71f, 2.43f, 3.19f, 4.57f };
            float[] gains = { 1f, 0.62f, 0.44f, 0.3f, 0.19f };
            float ringDecay = tier == 0 ? 26f : (tier == 1 ? 14f : 8.5f);
            for (int p = 0; p < ratios.Length; p++)
            {
                float freq = ringBase * ratios[p] * (float)(0.97 + rnd.NextDouble() * 0.06);
                float phase = (float)(rnd.NextDouble() * Mathf.PI * 2f);
                // Higher partials die faster — that is what makes metal sound
                // like metal instead of a bell.
                float d = ringDecay * (1f + p * 0.45f);
                float g = gains[p] * 0.28f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    buf[i] += g * Mathf.Exp(-d * t) * Mathf.Sin(2f * Mathf.PI * freq * t + phase);
                }
            }

            // --- thump: the body blow. Only tiers 1-2 get a real one.
            float thumpGain = tier == 0 ? 0.1f : (tier == 1 ? 0.34f : 0.6f);
            float thumpFreq = tier == 0 ? 95f : (tier == 1 ? 68f : 52f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                buf[i] += thumpGain * Mathf.Exp(-16f * t) * Mathf.Sin(2f * Mathf.PI * thumpFreq * t);
            }

            NormalizeAndFade(buf, 0.9f, Mathf.RoundToInt(SampleRate * 0.02f));
            var clip = AudioClip.Create("psx_impact_" + tier, n, 1, SampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        /// <summary>
        /// Seamless metal-on-concrete grind. Filtered noise loops badly at the
        /// seam, so the tail is crossfaded back over the head — with noise the
        /// blend is inaudible, and the clip becomes truly periodic.
        /// </summary>
        static AudioClip MakeScrape()
        {
            float dur = 1.2f;
            int n = Mathf.RoundToInt(SampleRate * dur);
            int fade = Mathf.RoundToInt(SampleRate * 0.06f);
            var raw = new float[n + fade];
            var rnd = new System.Random(4409);

            float f = 2f * Mathf.Sin(Mathf.PI * 1800f / SampleRate);
            float low = 0f, band = 0f;
            // Two resonant peaks give the grind a pitched edge, so it reads as
            // metal dragging rather than as radio static.
            float r1 = 0f, r2 = 0f, b1 = 0f, b2 = 0f;
            float f1 = 2f * Mathf.Sin(Mathf.PI * 1150f / SampleRate);
            float f2 = 2f * Mathf.Sin(Mathf.PI * 3050f / SampleRate);

            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)(rnd.NextDouble() * 2.0 - 1.0);
                float high = white - low - 0.9f * band;
                band += f * high; low += f * band;

                float h1 = band - r1 - 0.16f * b1; b1 += f1 * h1; r1 += f1 * b1;
                float h2 = band - r2 - 0.22f * b2; b2 += f2 * h2; r2 += f2 * b2;

                // Slow amplitude wobble: a real scrape is not a steady tone, the
                // panel chatters against the surface.
                float t = i / (float)SampleRate;
                float wobble = 0.78f + 0.22f * Mathf.Sin(2f * Mathf.PI * 7.3f * t)
                                     * Mathf.Sin(2f * Mathf.PI * 2.9f * t);
                raw[i] = (band * 0.55f + b1 * 0.5f + b2 * 0.3f) * wobble;
            }

            var buf = new float[n];
            System.Array.Copy(raw, buf, n);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                buf[i] = Mathf.Lerp(raw[n + i], raw[i], t);
            }

            NormalizeAndFade(buf, 0.55f, 0);
            var clip = AudioClip.Create("psx_scrape", n, 1, SampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        static void NormalizeAndFade(float[] buf, float peak, int fadeOutSamples)
        {
            float max = 0f;
            for (int i = 0; i < buf.Length; i++) max = Mathf.Max(max, Mathf.Abs(buf[i]));
            if (max > 0.0001f)
            {
                float g = peak / max;
                for (int i = 0; i < buf.Length; i++) buf[i] *= g;
            }
            for (int i = 0; i < fadeOutSamples && i < buf.Length; i++)
            {
                int idx = buf.Length - 1 - i;
                buf[idx] *= i / (float)fadeOutSamples;
            }
        }
    }
}
