using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Sample-based rotary engine voice, ported from Racing Game 2's
    /// src/engine/audio/sampleEngine.ts.
    ///
    /// A ladder of RPM-band loops (the Rotary_x7 set from the Realistic Engine
    /// Sound pack) is played continuously. At any instant exactly two adjacent
    /// bands are audible, linearly crossfaded by the engine's position between
    /// them; inside each band an on-throttle and an off-throttle take are
    /// crossfaded by throttle position. Every band is pitched by
    /// rpm / thatBandsHomeRPM, so each recording sits untouched at its home RPM
    /// and only ever stretches partway toward its neighbour.
    ///
    /// Three details from the original are what keep it from sounding broken:
    ///  - Band pairs are bound with hysteresis, so a band that is already
    ///    playing is never torn down and restarted when the pair shifts along.
    ///    (Restarting produced two phase-offset copies of the same loop.)
    ///  - Playback rate is smoothed with a very short time constant. At 0.04 s
    ///    the two slots sit 72 cents apart right after a band change, which is
    ///    audible; 0.012 s puts them 5 cents apart, which is not.
    ///  - The RPM the audio follows is not raw physics RPM. It may rise
    ///    instantly but falls at a capped rate, because a kinematic RPM dive of
    ///    ~50% in 100 ms on an upshift reads as a slide whistle through a tonal
    ///    voice.
    /// </summary>
    public class EngineAudio : MonoBehaviour
    {
        [System.Serializable]
        public class RpmBand
        {
            public string name;
            /// <summary>Design position on the rev range: 0 = idle, 1 = limiter.</summary>
            public float frac;
            public AudioClip onClip;
            public AudioClip offClip;

            [System.NonSerialized] public float homeRPM;
            [System.NonSerialized] public AudioSource onSrc;
            [System.NonSerialized] public AudioSource offSrc;
            [System.NonSerialized] public float gain;      // smoothed band gain
            [System.NonSerialized] public float rate;      // smoothed playback rate
        }

        public CarController car;

        [Header("Band ladder (idle -> very_high)")]
        public RpmBand[] bands;

        [Header("One-shots and overlays")]
        public AudioClip maxRpmClip;      // engine-on-the-limiter take
        public AudioClip startupClip;
        public AudioClip engineStopClip;
        /// <summary>Induction takes, played underneath the whole band ladder.
        /// This is the "breath" of the engine — the band loops carry the note,
        /// but without intake noise the result is a tone rather than a machine
        /// moving air.</summary>
        public AudioClip intakeOnClip;
        public AudioClip intakeOffClip;
        [Range(0f, 2f)] public float intakeLevel = 0.85f;

        [Header("Mix")]
        [Range(0f, 1f)] public float masterVolume = 1f;
        public bool spatial;
        public float spatialMaxDistance = 90f;
        /// <summary>Play the separate off-throttle takes. Doubles the voice count,
        /// so opponent cars run with it off — every source stays resident and
        /// audible, and exceeding the real-voice limit would make Unity virtualize
        /// (stop and restart) loops, which is the artifact this design avoids.</summary>
        public bool useOffTakes = true;

        // ---- constants from sampleEngine.ts ----------------------------------
        const float BandHysteresis = 0.025f;   // quarter of the narrowest band gap
        const float RateTC = 0.012f;           // 5 cents of spread after a band change
        const float GainTC = 0.04f;
        const float MasterTC = 0.05f;
        const float RateMin = 0.66f;
        const float RateMax = 1.50f;
        // The source's 0.24/0.48/0.50 were level-matched to sit under its own
        // procedural synth. There is no synth here, so the recordings were just
        // playing quiet — which reads as thin and synthetic.
        const float LevelBase = 0.32f;         // closed throttle
        const float LevelLoadSlope = 0.30f;    // WOT = 0.62
        const float LevelCap = 0.62f;
        const float RpmFallRateCap = 4200f;    // rpm/s
        const float RpmFallSnap = 5000f;       // a drop bigger than this snaps through
        const float LimiterHold = 0.2f;        // debounce on the limiter flag
        const float LimiterBlend = 0.03f;

        float audibleRPM;
        float masterGain;
        int loIndex = -1, hiIndex = -1;
        float limiterTimer;
        float maxRpmGain;
        AudioSource maxRpmSrc;
        AudioSource intakeOnSrc, intakeOffSrc;
        float intakeRate = 1f;
        AudioSource oneShotSrc;

        float IdleRPM => car != null ? car.idleRPM : 900f;
        float LimiterRPM => car != null ? car.revLimitRPM : 8000f;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            audibleRPM = IdleRPM;

            if (bands != null)
            {
                foreach (var b in bands)
                {
                    // Geometric, NOT linear. AudioSource.pitch is a ratio, so the
                    // ladder must be spaced by ratio: on a linear ladder the bottom
                    // two rungs sit 2.4x and 1.7x apart, both past the 1.5 clamp,
                    // and a clamped slot holds a fixed wrong note under the right
                    // one. That is the "two engines revving at once" artifact.
                    b.homeRPM = IdleRPM * Mathf.Pow(LimiterRPM / IdleRPM, b.frac);
                    b.onSrc = MakeLoop(b.onClip, b.name + "_on");
                    b.offSrc = useOffTakes && b.offClip != b.onClip
                        ? MakeLoop(b.offClip, b.name + "_off") : null;
                    b.rate = 1f;
                }
            }
            maxRpmSrc = MakeLoop(maxRpmClip, "maxRPM");
            intakeOnSrc = MakeLoop(intakeOnClip, "intake_on");
            intakeOffSrc = MakeLoop(intakeOffClip, "intake_off");

            oneShotSrc = gameObject.AddComponent<AudioSource>();
            oneShotSrc.playOnAwake = false;
            oneShotSrc.loop = false;
            ConfigureSpatial(oneShotSrc);
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
            ConfigureSpatial(src);
            src.Play();
            return src;
        }

        void ConfigureSpatial(AudioSource src)
        {
            src.spatialBlend = spatial ? 1f : 0f;
            src.dopplerLevel = spatial ? 0.6f : 0f;
            if (spatial)
            {
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = spatialMaxDistance;
                src.minDistance = 6f;
            }
        }

        public void PlayStartup(float delay = 0f)
        {
            if (startupClip == null || oneShotSrc == null) return;
            oneShotSrc.clip = startupClip;
            if (delay > 0f) oneShotSrc.PlayDelayed(delay);
            else oneShotSrc.Play();
        }

        public void PlayShutdown()
        {
            if (engineStopClip == null || oneShotSrc == null) return;
            oneShotSrc.PlayOneShot(engineStopClip, 0.8f);
        }

        void Update()
        {
            if (car == null || bands == null || bands.Length == 0) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateAudibleRPM(dt);

            bool atLimiter = car.currentRPM >= car.revLimitRPM - 120f && car.throttleInput > 0.5f;
            limiterTimer = atLimiter ? LimiterHold : Mathf.Max(0f, limiterTimer - dt);
            bool limiterActive = limiterTimer > 0f;

            // Master position on the ladder, in the same geometric space as the
            // band home RPMs: r = log(rpm/idle) / log(limiter/idle).
            float logSpan = Mathf.Log(LimiterRPM / IdleRPM);
            float frac = Mathf.Clamp01(Mathf.Log(Mathf.Max(audibleRPM, IdleRPM) / IdleRPM) / logSpan);

            SelectBandPair(frac);

            // Load carries very little level: the timbre difference between a
            // closed and open throttle comes from the recordings themselves.
            float load = Mathf.Clamp01(car.throttleInput);
            float masterTarget = Mathf.Min(LevelCap, LevelBase + LevelLoadSlope * load) * masterVolume;
            masterGain = Smooth(masterGain, masterTarget, MasterTC, dt);

            float loFrac = bands[loIndex].frac;
            float hiFrac = bands[hiIndex].frac;
            float t = hiIndex == loIndex ? 0f
                    : Mathf.Clamp01((frac - loFrac) / Mathf.Max(hiFrac - loFrac, 1e-4f));

            for (int i = 0; i < bands.Length; i++)
            {
                var b = bands[i];
                float target = 0f;
                if (i == loIndex) target = 1f - t;
                if (i == hiIndex) target = Mathf.Max(target, t);
                if (loIndex == hiIndex && i == loIndex) target = 1f;

                b.gain = Smooth(b.gain, target, GainTC, dt);

                // A rate-clamped slot stops tracking RPM and holds a wrong note
                // under the right one, so the ladder spacing is chosen to keep
                // this from binding while a band is actually audible.
                float rateTarget = Mathf.Clamp(audibleRPM / Mathf.Max(b.homeRPM, 1f), RateMin, RateMax);
                b.rate = Smooth(b.rate, rateTarget, RateTC, dt);

                float g = b.gain * masterGain;
                if (b.offSrc != null)
                {
                    Apply(b.onSrc, g * load, b.rate);
                    Apply(b.offSrc, g * (1f - load), b.rate);
                }
                else Apply(b.onSrc, g, b.rate);
            }

            // The limiter take is gated on the physics flag, never on RPM position.
            float maxTarget = limiterActive ? 1f : 0f;
            maxRpmGain = Smooth(maxRpmGain, maxTarget, LimiterBlend + GainTC, dt);
            Apply(maxRpmSrc, maxRpmGain * masterGain, 1f);

            // Intake sits under everything, pitched across the whole rev range
            // rather than per band, so it reads as one continuous breath.
            float intakeTargetRate = Mathf.Clamp(0.75f + frac * 0.7f, RateMin, RateMax);
            intakeRate = Smooth(intakeRate, intakeTargetRate, RateTC * 4f, dt);
            float intakeGain = masterGain * intakeLevel * (0.35f + 0.65f * frac);
            Apply(intakeOnSrc, intakeGain * load, intakeRate);
            Apply(intakeOffSrc, intakeGain * (1f - load) * 0.7f, intakeRate);
        }

        /// <summary>
        /// RPM may rise instantly but falls at a capped mechanical rate, so an
        /// auto upshift does not read as a descending slide. The cap is bypassed
        /// on the limiter, where the real ~12 Hz bounce is the point.
        /// </summary>
        void UpdateAudibleRPM(float dt)
        {
            float raw = car.currentRPM;
            if (raw >= audibleRPM || limiterTimer > 0f || (audibleRPM - raw) > RpmFallSnap)
                audibleRPM = raw;
            else
                audibleRPM = Mathf.Max(raw, audibleRPM - RpmFallRateCap * dt);
        }

        /// <summary>
        /// Bind the crossfade pair to band identity with a dead zone, so cruising
        /// on a band edge cannot re-derive the pair several times a second.
        /// </summary>
        void SelectBandPair(float frac)
        {
            bool valid = loIndex >= 0 && hiIndex >= 0 &&
                         frac >= bands[loIndex].frac - BandHysteresis &&
                         frac <= bands[hiIndex].frac + BandHysteresis;
            if (valid) return;

            loIndex = 0; hiIndex = 0;
            for (int i = 0; i < bands.Length - 1; i++)
            {
                if (frac >= bands[i].frac && frac <= bands[i + 1].frac)
                {
                    loIndex = i; hiIndex = i + 1; return;
                }
            }
            // Past the top of the ladder: hold the highest band and let it pitch up.
            if (frac > bands[bands.Length - 1].frac)
                loIndex = hiIndex = bands.Length - 1;
        }

        static void Apply(AudioSource src, float volume, float pitch)
        {
            if (src == null) return;
            // Most bands sit at zero gain; skip the native writes for those, but
            // keep them playing so they never restart out of phase when they
            // become audible again.
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
