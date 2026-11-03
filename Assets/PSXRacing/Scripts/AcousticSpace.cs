using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE SPACE THE CAR IS IN, HEARD (owner, 2026-11-02: "I want sound
    /// (especially engine, exhaust, tire screech, crashes) to move properly
    /// based on environment (tunnel, mountain top, between buildings, in
    /// parking deck, on open highway)").
    ///
    /// Lives on the AudioListener (AudioToneChain adds it), so its reverb and
    /// echo filters act on everything the player hears - their own engine,
    /// exhaust, tyres, turbo and crashes, and every other car - after the tone
    /// chain. What the space is, it measures rather than being told: short
    /// rays every tenth of a second round the listener (level all round, up at
    /// 35 and 65 degrees, straight up) and, once a second, long level rays out
    /// to 2.5 km. From them:
    ///   cover      how much of the sky above is closed within 25 m;
    ///   ceiling    how low what is straight above is;
    ///   walls      how much of the level circle is closed within 60 m, how far;
    ///   ridge      the nearest big return 150-850 m out (hills, a far tower).
    /// Five spaces, blended continuously (a tunnel's mouth fades in, it does
    /// not switch):
    ///   TUNNEL     covered, the roof high: long (~3 s), dense, dark reverb;
    ///   DECK       covered, the slab low: short dense concrete reflections;
    ///   CANYON     open above, walls round: ~1.3 s reverb and a slapback off
    ///              the walls, its delay from their distance;
    ///   OPEN       nothing near: nearly dry (a little ground and verge);
    ///   RIDGE      open with a ridge out there: a faint echo off it, late.
    /// A stage's tunnel BORE (WeatherShelter.EnsureVenueBores, the catalogue's
    /// tunnel table) is a TUNNEL whatever the rays find: its drawn roof has no
    /// collider and the mountain over it is cut away, and at 5.2 m it is as
    /// low as a deck's slab (2026-11-03: the Little Switzerland Tunnel heard
    /// as a CANYON). Cars and tree trunks are not walls (the player's own car
    /// stood in the chase camera's level rays; a forest is not a street
    /// canyon). Menus,
    /// music and UI bypass the listener's effects (bypassListenerEffects on
    /// their sources). WebGL has no filter DSP at all (see AudioToneChain), so
    /// there the game stays dry; Windows and Android hear the space.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public class AcousticSpace : MonoBehaviour
    {
        /// <summary>PSX_ACOUSTICS=0 turns the spaces off (A/B).</summary>
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_ACOUSTICS") != "0";
        /// <summary>PSX_ACOUSTICS_LOG=1: every change of space logged with what was measured (the checks).</summary>
        public static bool LogChanges = System.Environment.GetEnvironmentVariable("PSX_ACOUSTICS_LOG") == "1";

        const float ProbeEvery = 0.1f, RidgeEvery = 1f;
        const float NearM = 60f, CoverM = 25f, RidgeMinM = 150f, RidgeMaxM = 850f, LongM = 2500f;
        const int Level = 16, Tilt35 = 8, Tilt65 = 4;
        /// <summary>How quickly the heard space follows the measured one (1/s).</summary>
        const float Follow = 2.5f;
        const float SpeedOfSound = 343f;

        AudioReverbFilter reverb;
        AudioEchoFilter echo;
        float probeAt, ridgeAt;
        int mask;

        // what was measured, and what is heard (smoothed)
        public float Cover { get; private set; }
        public float Ceiling { get; private set; } = 100f;
        public float Walls { get; private set; }
        public float WallM { get; private set; } = NearM;
        public float RidgeM { get; private set; }
        /// <summary>Inside one of the venue's tunnel bores (WeatherShelter.InBore).</summary>
        public bool InBore { get; private set; }
        /// <summary>The blend weights now (tunnel, deck, canyon, open, ridge) - for the FPS overlay and the checks.</summary>
        public readonly float[] Weights = new float[5];
        public string Label { get; private set; } = "OPEN";

        struct Preset
        {
            public float room, roomHF, decay, decayHF, refl, reflDelay, rev, revDelay, diffusion, density;
            public Preset(float room, float roomHF, float decay, float decayHF, float refl, float reflDelay, float rev, float revDelay, float diffusion, float density)
            { this.room = room; this.roomHF = roomHF; this.decay = decay; this.decayHF = decayHF; this.refl = refl; this.reflDelay = reflDelay; this.rev = rev; this.revDelay = revDelay; this.diffusion = diffusion; this.density = density; }
        }

        // millibels, seconds, percent - FMOD's I3DL2 terms as AudioReverbFilter takes them
        static readonly Preset Tunnel = new Preset(-350f, -600f, 2.9f, 0.55f, -150f, 0.012f, -250f, 0.020f, 90f, 100f);
        static readonly Preset Deck   = new Preset(-500f, -350f, 1.45f, 0.8f, -250f, 0.006f, -650f, 0.010f, 85f, 100f);
        static readonly Preset Canyon = new Preset(-900f, -800f, 1.3f, 0.7f, -1200f, 0.020f, -1350f, 0.030f, 70f, 80f);
        static readonly Preset Open   = new Preset(-3600f, -2600f, 0.9f, 0.5f, -3200f, 0.020f, -3600f, 0.030f, 50f, 50f);
        static readonly Preset Ridge  = new Preset(-2600f, -3400f, 1.6f, 0.4f, -3000f, 0.050f, -2400f, 0.080f, 30f, 40f);

        Preset heard = Open;
        float echoDelay = 120f, echoWet;

        void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            enabled = false;
            return;
#else
            if (!Enabled) { enabled = false; return; }
            // every solid thing but cars (layer 2, the traffic) and the trigger volumes
            mask = Physics.DefaultRaycastLayers & ~(1 << 2);
            reverb = GetComponent<AudioReverbFilter>();
            if (reverb == null) reverb = gameObject.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.dryLevel = 0f;
            reverb.hfReference = 5000f;
            reverb.lfReference = 250f;
            reverb.roomLF = 0f;
            echo = GetComponent<AudioEchoFilter>();
            if (echo == null) echo = gameObject.AddComponent<AudioEchoFilter>();
            echo.dryMix = 1f;
            echo.wetMix = 0f;
            echo.decayRatio = 0.3f;
            echo.delay = 120f;
            Apply();
#endif
        }

        void Update()
        {
            if (Time.unscaledTime >= probeAt) { probeAt = Time.unscaledTime + ProbeEvery; ProbeNear(); }
            if (Time.unscaledTime >= ridgeAt) { ridgeAt = Time.unscaledTime + RidgeEvery; ProbeRidge(); }
            Blend(Time.unscaledDeltaTime);
            Apply();
        }

        /// <summary>A ray that hit something that counts as a surface: not a
        /// car (anything with a rigidbody that drives), not a tree trunk.</summary>
        static bool Surface(in RaycastHit h)
        {
            var c = h.collider;
            if (c == null) return false;
            if (c is CapsuleCollider) return false;                       // tree trunks (TreeTrunks)
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic) return false;              // cars, debris
            if (rb != null && rb.GetComponent<CarController>() != null) return false;
            return true;
        }

        bool Cast(Vector3 from, Vector3 dir, float max, out float dist)
        {
            dist = max;
            if (!Physics.Raycast(from, dir, out var h, max, mask, QueryTriggerInteraction.Ignore)) return false;
            if (!Surface(h))
            {
                // through a car or a trunk: once more from just past it
                var from2 = h.point + dir * 0.6f;
                if (!Physics.Raycast(from2, dir, out h, max - h.distance - 0.6f, mask, QueryTriggerInteraction.Ignore) || !Surface(h)) return false;
                dist = h.distance + (from2 - from).magnitude;
                return true;
            }
            dist = h.distance;
            return true;
        }

        void ProbeNear()
        {
            var p = transform.position + Vector3.up * 0.3f;
            // level ring: walls
            int hits = 0; float sum = 0f;
            for (int i = 0; i < Level; i++)
            {
                float a = i * Mathf.PI * 2f / Level;
                var d = new Vector3(Mathf.Cos(a), 0.08f, Mathf.Sin(a)).normalized;
                if (Cast(p, d, NearM, out float dist)) { hits++; sum += dist; }
            }
            Walls = hits / (float)Level;
            WallM = hits > 0 ? sum / hits : NearM;
            // up: cover and the ceiling
            int up = 0, upN = 0;
            for (int i = 0; i < Tilt35; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / Tilt35;
                var d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(-35f, Vector3.right) * Vector3.forward;
                upN++; if (Cast(p, d, CoverM, out _)) up++;
            }
            for (int i = 0; i < Tilt65; i++)
            {
                float a = i * Mathf.PI * 2f / Tilt65;
                var d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(-65f, Vector3.right) * Vector3.forward;
                upN++; if (Cast(p, d, CoverM, out _)) up++;
            }
            bool roof = Cast(p, Vector3.up, CoverM, out float ceil);
            upN++; if (roof) up++;
            Cover = up / (float)upN;
            Ceiling = roof ? ceil : 100f;
            WeatherShelter.EnsureVenueBores();
            InBore = WeatherShelter.InBore(transform.position);
        }

        void ProbeRidge()
        {
            var p = transform.position + Vector3.up * 1.5f;
            float best = 0f;
            int n = 12;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.25f) * Mathf.PI * 2f / n;
                var d = new Vector3(Mathf.Cos(a), 0.03f, Mathf.Sin(a)).normalized;
                if (Cast(p, d, LongM, out float dist) && dist >= RidgeMinM && dist <= RidgeMaxM && (best == 0f || dist < best)) best = dist;
            }
            RidgeM = best;
        }

        void Blend(float dt)
        {
            // the weights of the five spaces from what was measured
            float covered = InBore ? 1f : Mathf.InverseLerp(0.35f, 0.8f, Cover);
            float low = InBore ? 0f : Mathf.InverseLerp(6f, 3.6f, Ceiling);         // a deck's slab vs a tunnel's roof
            float walled = Mathf.InverseLerp(0.25f, 0.7f, Walls);
            float wTunnel = covered * (1f - low);
            float wDeck = covered * low;
            float wCanyon = (1f - covered) * walled;
            float rest = Mathf.Max(0f, 1f - wTunnel - wDeck - wCanyon);
            float ridgy = RidgeM > 0f ? 1f : 0f;
            float wRidge = rest * ridgy * 0.6f;
            float wOpen = rest - wRidge;
            float k = 1f - Mathf.Exp(-Follow * dt);
            Weights[0] = Mathf.Lerp(Weights[0], wTunnel, k);
            Weights[1] = Mathf.Lerp(Weights[1], wDeck, k);
            Weights[2] = Mathf.Lerp(Weights[2], wCanyon, k);
            Weights[3] = Mathf.Lerp(Weights[3], wOpen, k);
            Weights[4] = Mathf.Lerp(Weights[4], wRidge, k);
            float sum = 0f; for (int i = 0; i < 5; i++) sum += Weights[i];
            if (sum < 1e-4f) { Weights[3] = 1f; sum = 1f; }
            int top = 0; for (int i = 1; i < 5; i++) if (Weights[i] > Weights[top]) top = i;
            string was = Label;
            Label = top == 0 ? "TUNNEL" : top == 1 ? "DECK" : top == 2 ? "CANYON" : top == 3 ? "OPEN" : "RIDGE";
            if (LogChanges && Label != was)
                Debug.Log($"[Acoustics] {was} -> {Label} at {transform.position:F0} t={Time.time:F1}: cover {Cover:F2} ceiling {Ceiling:F1} m walls {Walls:F2} at {WallM:F0} m ridge {RidgeM:F0} m{(InBore ? " (tunnel bore)" : "")}");

            Preset P(int i) => i == 0 ? Tunnel : i == 1 ? Deck : i == 2 ? Canyon : i == 3 ? Open : Ridge;
            var mix = new Preset();
            for (int i = 0; i < 5; i++)
            {
                float w = Weights[i] / sum; var q = P(i);
                mix.room += q.room * w; mix.roomHF += q.roomHF * w; mix.decay += q.decay * w; mix.decayHF += q.decayHF * w;
                mix.refl += q.refl * w; mix.reflDelay += q.reflDelay * w; mix.rev += q.rev * w; mix.revDelay += q.revDelay * w;
                mix.diffusion += q.diffusion * w; mix.density += q.density * w;
            }
            heard = mix;

            // the echo: a canyon's slapback off its walls, or a ridge's, late and faint
            float wc = Weights[2] / sum, wr = Weights[4] / sum, wt = (Weights[0] + Weights[1]) / sum;
            float slap = 2f * Mathf.Clamp(WallM, 6f, NearM) / SpeedOfSound * 1000f;
            float far = RidgeM > 0f ? Mathf.Min(4800f, 2f * RidgeM / SpeedOfSound * 1000f) : slap;
            float wantDelay = wr > wc ? far : slap;
            float wantWet = Mathf.Max(wc * 0.22f, wr * 0.12f) * (1f - wt);
            echoDelay = Mathf.Lerp(echoDelay, wantDelay, k);
            echoWet = Mathf.Lerp(echoWet, wantWet, k);
        }

        void Apply()
        {
            if (reverb == null) return;
            reverb.room = heard.room;
            reverb.roomHF = heard.roomHF;
            reverb.decayTime = Mathf.Max(0.1f, heard.decay);
            reverb.decayHFRatio = Mathf.Clamp(heard.decayHF, 0.1f, 2f);
            reverb.reflectionsLevel = heard.refl;
            reverb.reflectionsDelay = Mathf.Clamp(heard.reflDelay, 0f, 0.3f);
            reverb.reverbLevel = heard.rev;
            reverb.reverbDelay = Mathf.Clamp(heard.revDelay, 0f, 0.1f);
            reverb.diffusion = heard.diffusion;
            reverb.density = heard.density;
            if (echo != null)
            {
                // the delay only moves while the echo is quiet: moving it under a
                // loud echo is a pitch sweep
                if (echoWet < 0.03f || Mathf.Abs(echo.delay - echoDelay) > 400f) echo.delay = Mathf.Clamp(echoDelay, 10f, 5000f);
                echo.wetMix = Mathf.Clamp01(echoWet);
                echo.decayRatio = Weights[4] > Weights[2] ? 0.15f : 0.3f;
            }
        }
    }
}
