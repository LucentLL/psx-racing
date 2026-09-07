using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The wind bed: the one sound in the game that scales with SPEED rather
    /// than with revs, and the reason "gear feel" exists in audio at all.
    /// EngineAudio pitches by rpm/homeRPM, so fourth at 200 km/h sounds like
    /// second at 80 — correct, and exactly why the engine cannot carry the
    /// sense of speed on its own. This layer rises under it and never resets
    /// at a shift.
    ///
    /// Two loops, volume and pitch ONLY. There is no filter anywhere in this
    /// component on purpose: OnAudioFilterRead and every audio filter
    /// component are silently dead on Web (see AudioToneChain), so a "whoosh"
    /// that opens a lowpass with speed would ship as a whoosh that does not.
    /// The crossfade IS the filter — the same trick the engine ladder uses: a
    /// band-limited LOW loop (60-400 Hz) that comes in first, and a HIGH loop
    /// (800 Hz-6 kHz) that comes in later and pitches up, so the spectrum
    /// opens with speed without a filter ever touching a sample.
    ///
    /// Both loops are generated offline by tools/gen_wind.mjs, seamless by
    /// construction, and still go through <see cref="AudioLoopStarter.PlayLoop"/>
    /// so LoopSeam de-clicks whatever the importer's Vorbis pass did to them.
    /// Player only: the field's wind is inaudible from the driving seat.
    /// </summary>
    public class WindAudio : MonoBehaviour
    {
        public CarController car;
        [Range(0f, 1f)] public float masterVolume = 1f;

        /// <summary>Resources paths. Under Sfx/ rather than Audio/ because the
        /// component loads them itself at Awake — the builder never sees them
        /// — and Resources is the one folder a runtime load can reach.</summary>
        public const string LowClipPath = "Sfx/wind_low";
        public const string HighClipPath = "Sfx/wind_high";

        // ---- the two volume curves (starting values for tuning) ----------
        /// <summary>The low bed starts at 8 m/s (29 km/h) and is full by
        /// 48 m/s. Expo 1.2: a little slower than linear off the line, so
        /// pulling out of the pits does not switch a rumble on.</summary>
        public const float LowStartMps = 8f;
        public const float LowSpanMps = 40f;
        public const float LowMax = 0.30f;
        public const float LowExpo = 1.2f;
        /// <summary>The high hiss starts at 25 m/s (90 km/h) and is full by
        /// 70 m/s (252 km/h). Expo 1.6: most of it arrives in the top half of
        /// the range, which is where the engine note has stopped telling the
        /// player anything new.</summary>
        public const float HighStartMps = 25f;
        public const float HighSpanMps = 45f;
        public const float HighMax = 0.30f;
        public const float HighExpo = 1.6f;
        /// <summary>The high loop pitches 0.90 -> 1.15 over 0-70 m/s, so the
        /// hiss brightens with speed the way real wind noise does — that is
        /// the "filter opening" without a filter.</summary>
        public const float HighPitchBase = 0.90f;
        public const float HighPitchRise = 0.25f;
        public const float HighPitchFullMps = 70f;
        /// <summary>Half in COCKPIT: glass between the ear and the airstream.</summary>
        public const float CockpitMul = 0.5f;
        /// <summary>Gain smoothing. Fast enough that a lift reads, slow enough
        /// that a collision's velocity spike does not click the bed.</summary>
        const float GainTau = 0.08f;

        public static float LowGain(float speedMps) =>
            LowMax * Mathf.Pow(Mathf.Clamp01((Mathf.Abs(speedMps) - LowStartMps) / LowSpanMps), LowExpo);

        public static float HighGain(float speedMps) =>
            HighMax * Mathf.Pow(Mathf.Clamp01((Mathf.Abs(speedMps) - HighStartMps) / HighSpanMps), HighExpo);

        public static float HighPitch(float speedMps) =>
            HighPitchBase + HighPitchRise * Mathf.Clamp01(Mathf.Abs(speedMps) / HighPitchFullMps);

        AudioSource low, high;
        float lowGain, highGain;
        float pitch = HighPitchBase;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            low = MakeLoop(Resources.Load<AudioClip>(LowClipPath), "wind_low");
            high = MakeLoop(Resources.Load<AudioClip>(HighClipPath), "wind_high");
        }

        AudioSource MakeLoop(AudioClip clip, string label)
        {
            if (clip == null) return null;
            var go = new GameObject("snd_" + label);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.playOnAwake = false;
            src.spatialBlend = 0f;        // the airstream is AT the listener
            src.dopplerLevel = 0f;
            // Deferred: on WebGL the browser has not decoded this clip yet, and
            // a source started against an undecoded clip loops silence forever.
            AudioLoopStarter.PlayLoop(src);
            return src;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // Body speed, not forward speed: a car going sideways at 100 km/h
            // is still in a 100 km/h airstream.
            float v = car != null ? car.speedKmh / 3.6f : 0f;

            float mul = masterVolume;
            if (ChaseCamera.Current == ChaseCamera.View.Cockpit) mul *= CockpitMul;
            // Muted on foot and in the menu. AudioListener.pause already holds
            // the menu case, but the gain is driven to zero as well so the bed
            // does not resume at full strength for the first frame after it.
            if (PauseMenu.IsOpen || OnFoot.ForecourtMode.OnFoot) mul = 0f;

            lowGain = Smooth(lowGain, LowGain(v) * mul, GainTau, dt);
            highGain = Smooth(highGain, HighGain(v) * mul, GainTau, dt);
            pitch = Smooth(pitch, HighPitch(v), GainTau, dt);

            Apply(low, lowGain, 1f);
            Apply(high, highGain, pitch);
        }

        static void Apply(AudioSource src, float volume, float pitch)
        {
            if (src == null) return;
            // Same idiom as EngineAudio: skip the native writes at zero gain but
            // keep the loop playing so it never restarts out of phase.
            if (volume < 0.0005f)
            {
                if (src.volume != 0f) src.volume = 0f;
                return;
            }
            src.volume = volume;
            src.pitch = pitch;
        }

        static float Smooth(float current, float target, float tc, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / Mathf.Max(tc, 1e-4f)));
    }
}
