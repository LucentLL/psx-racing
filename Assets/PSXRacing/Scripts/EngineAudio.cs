using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Sample-based engine voice, ported from Racing Game 2's
    /// src/engine/audio/sampleEngine.ts.
    ///
    /// A ladder of RPM-band loops is played continuously — which recordings
    /// depends on the car: <see cref="SetFamily"/> swaps in any of the 28
    /// families under Resources/Engines, so a kei four, a 13B-REW and a race V8
    /// each speak with their own engine rather than all sounding like the
    /// built-in RX-7. At any instant exactly two adjacent
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

        [Header("Voice")]
        /// <summary>Recorded family folder under Resources/Engines. Set by the
        /// builder to the default and re-set per car by RaceHandoffApplier once
        /// the catalog spec is known.</summary>
        public string family = EngineVoiceLibrary.DefaultFamily;

        /// <summary>Built at runtime from the family, NOT serialized: the clip
        /// set is a property of the car being driven, and a scene that baked one
        /// in would quietly override whatever the player actually bought.</summary>
        [System.NonSerialized] public RpmBand[] bands;

        // Overlays and one-shots, all resolved from the family.
        AudioClip maxRpmClip;      // engine-on-the-limiter take
        AudioClip startupClip;
        AudioClip engineStopClip;
        /// <summary>Induction takes, played underneath the whole band ladder.
        /// This is the "breath" of the engine — the band loops carry the note,
        /// but without intake noise the result is a tone rather than a machine
        /// moving air.
        ///
        /// It has to stay UNDER them, and it did not. Measured on
        /// v8_american_classic_1: intake_on carries ZERO energy below 240 Hz
        /// (83% of it sits between 480 Hz and 1.9 kHz) and its RMS is 2.9 dB
        /// HOTTER than the med/high/very_high band takes. At the old 0.85 level
        /// that put a band-limited whistle roughly 2 dB above the entire engine,
        /// and the old 0.75-1.45 pitch ramp shoved 45% of its energy into
        /// 1.9-3.8 kHz at high rpm. Muting it alone took that band from 16.5% of
        /// total energy to 5.6% — two thirds of the "whiny and fake".
        ///
        /// The pack's own prefab plays these takes at FIXED pitch (there is no
        /// intakePitchCurve — only intakeOnVolCurve/intakeOffVolCurve), so the
        /// ramp was invented here. Both are now the vendor's: on rises from
        /// silence at idle, off stays out until a third of the way up.</summary>
        AudioClip intakeOnClip;
        AudioClip intakeOffClip;
        [Range(0f, 2f)] public float intakeLevel = 0.22f;

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
        // 1.50 in the source, where it is the safety net that keeps a crossfade
        // PAIR in unison — a clamped slot stops tracking RPM and holds a wrong
        // note under the right one. Anchoring the ladder to the REDLINE (see
        // LadderTopRPM) makes that region provably clamp-free for all 317 cars
        // even with the per-car rateMul multiplied in, so the only place the
        // ceiling still binds is above the top rung, where one band plays alone
        // and a clamp merely holds it flat for the last 500 rpm to the limiter.
        // 1.60 clears that for the whole catalog; at 1.50 it bound on 38 cars.
        const float RateMax = 1.60f;
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

        // ---- per-car voice (RG2's engineVoice.ts + iconicVoices.ts) ----------
        // There is ONE recording per family, so without these the thirty cars
        // that share v8_american_classic_1 are the identical loop at the
        // identical rate. Baked per car into CarSpec; see SetVoice.
        float voiceRateMul = 1f;
        float voiceLevelMul = 1f;
        float lopeDepth, lopeOrder = 0.5f, lopeFadeTop, lopePhaseSeed;
        float lopePhase;

        float audibleRPM;
        float masterGain;
        int loIndex = -1, hiIndex = -1;
        float limiterTimer;
        float maxRpmGain;
        AudioSource maxRpmSrc;
        AudioSource intakeOnSrc, intakeOffSrc;
        AudioSource oneShotSrc;

        float IdleRPM => car != null ? car.idleRPM : 900f;
        float LimiterRPM => car != null ? car.revLimitRPM : 8000f;
        /// <summary>
        /// Top of the band ladder. The REDLINE, not the rev limiter.
        ///
        /// CarController sets revLimitRPM to redline + 500, and hanging the
        /// ladder off that stretched every home RPM ~1.5% per rung against the
        /// span RG2 places them on — so a recording sat at a different engine
        /// speed here than in the game these takes were tuned in. It also broke
        /// the safety property the per-car rateMul is derived under: that clamp
        /// window is computed from redline/idle, so the ladder has to use the
        /// same span or the guarantee does not transfer. Measured across the
        /// catalog: 4 cars would bind the pitch clamp mid-crossfade on the old
        /// anchor, 0 on this one.
        /// </summary>
        float LadderTopRPM => car != null && car.redlineRPM > car.idleRPM
            ? car.redlineRPM : LimiterRPM;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            audibleRPM = IdleRPM;

            oneShotSrc = gameObject.AddComponent<AudioSource>();
            oneShotSrc.playOnAwake = false;
            oneShotSrc.loop = false;
            ConfigureSpatial(oneShotSrc);

            // The voice is NOT built here, for two reasons.
            //
            // Cost: RaceHandoffApplier learns which car this is during Start,
            // one phase later, so building the default family in Awake would
            // decompress ~6 MB of RX-7 samples per car that nothing then plays.
            //
            // Correctness, and the more important one: a band's home RPM is
            // derived from the car's OWN idle and limiter, and both of those are
            // rewritten by ApplySpec — also in Start. Building in Awake anchored
            // every ladder to the built-in RX-7's 900/8000 and then let the
            // player's actual 500/6500 drive it, so every band sat at the wrong
            // pitch on every car that was not an FD.
            //
            // Standalone editor play, where nothing calls SetFamily, still gets
            // the default on the first frame that needs sound.
        }

        void EnsureVoice()
        {
            if (bands != null) return;
            // Standalone editor play never calls SetVoice, but the controller has
            // already read a spec by now — so take the character from there
            // rather than leaving the car neutral in the one mode that gets
            // driven most while tuning.
            if (voiceRateMul == 1f && car != null) SetVoice(car.activeSpec);
            BuildVoice();
        }

        /// <summary>
        /// Point this engine at a different recorded family and rebuild.
        /// No-ops when the family is unchanged — RaceHandoffApplier and the
        /// standalone-play default can both call it without tearing down a voice
        /// that is already correct (and a teardown mid-race would restart every
        /// loop from sample zero).
        /// </summary>
        public void SetFamily(string key)
        {
            string resolved = EngineVoiceLibrary.Has(key) ? key : EngineVoiceLibrary.DefaultFamily;
            if (resolved == family && bands != null) return;
            family = resolved;
            BuildVoice();
        }

        /// <summary>Convenience for the race applier: a car's spec knows its own
        /// family, and the fallback rule lives in one place.</summary>
        public void SetFamily(CarSpec spec) => SetFamily(EngineVoiceLibrary.Resolve(spec));

        /// <summary>
        /// Give this engine its car's own character on top of the shared family
        /// recording: how fast the takes play, how loud they sit, and whether
        /// the firing order lopes.
        ///
        /// Only the pitch and level axes live here. The formant and the exhaust
        /// shelf need a parametric EQ, which Unity has no component for, so
        /// they go to AudioToneChain on the listener instead — one filter set
        /// for the car being driven rather than one per source.
        ///
        /// Zeroed fields fall back to neutral, so a spec baked before these
        /// existed still plays exactly as it did.
        /// </summary>
        public void SetVoice(CarSpec spec)
        {
            if (spec == null) return;
            voiceRateMul = spec.voiceRateMul > 0.05f ? spec.voiceRateMul : 1f;
            voiceLevelMul = spec.voiceLevelMul > 0.05f ? spec.voiceLevelMul : 1f;
            lopeDepth = Mathf.Max(0f, spec.lopeDepth);
            lopeOrder = spec.lopeOrder > 0f ? spec.lopeOrder : 0.5f;
            lopeFadeTop = spec.lopeFadeTop;
            lopePhaseSeed = spec.lopePhase;
        }

        void BuildVoice()
        {
            TeardownVoice();

            // Eight rungs with on/off-throttle pairs for the player; five
            // on-throttle rungs for opponents, whose extra voices would cost
            // more than their off-throttle detail is worth at race distance.
            var defs = useOffTakes
                ? EngineVoiceLibrary.PlayerBands
                : EngineVoiceLibrary.OpponentBands;

            var built = new System.Collections.Generic.List<RpmBand>(defs.Length);
            foreach (var d in defs)
            {
                var onClip = EngineVoiceLibrary.Clip(family, d.onClip);
                if (onClip == null) continue;
                var offClip = string.IsNullOrEmpty(d.offClip) || d.offClip == d.onClip
                    ? null : EngineVoiceLibrary.Clip(family, d.offClip);

                var b = new RpmBand { name = d.name, frac = d.frac, onClip = onClip, offClip = offClip };
                // Geometric, NOT linear. AudioSource.pitch is a ratio, so the
                // ladder must be spaced by ratio: on a linear ladder the bottom
                // two rungs sit 2.4x and 1.7x apart, both past the 1.5 clamp,
                // and a clamped slot holds a fixed wrong note under the right
                // one. That is the "two engines revving at once" artifact.
                b.homeRPM = IdleRPM * Mathf.Pow(LadderTopRPM / IdleRPM, b.frac);
                b.onSrc = MakeLoop(b.onClip, b.name + "_on");
                b.offSrc = useOffTakes && b.offClip != null
                    ? MakeLoop(b.offClip, b.name + "_off") : null;
                b.rate = 1f;
                built.Add(b);
            }
            bands = built.ToArray();
            loIndex = hiIndex = -1;

            // Opponents skip the limiter take and the intake layer: three more
            // always-resident voices each, for detail nobody hears from another
            // car. The one-shots stay — a grid of engines firing up is the point
            // of the countdown.
            maxRpmClip = useOffTakes ? EngineVoiceLibrary.Clip(family, "maxRPM") : null;
            intakeOnClip = useOffTakes ? EngineVoiceLibrary.Clip(family, "intake_on") : null;
            intakeOffClip = useOffTakes ? EngineVoiceLibrary.Clip(family, "intake_off") : null;
            startupClip = EngineVoiceLibrary.Clip(family, "startup");
            engineStopClip = EngineVoiceLibrary.Clip(family, "engine_stop");

            maxRpmSrc = MakeLoop(maxRpmClip, "maxRPM");
            intakeOnSrc = MakeLoop(intakeOnClip, "intake_on");
            intakeOffSrc = MakeLoop(intakeOffClip, "intake_off");
        }

        /// <summary>
        /// Destroy the previous family's sources. Every loop lives on its own
        /// child GameObject (one AudioSource per clip is what keeps loops from
        /// restarting out of phase), so tearing the children down is the whole
        /// job — but only the ones this component made, hence the name test.
        /// </summary>
        void TeardownVoice()
        {
            if (bands != null)
            {
                foreach (var b in bands)
                {
                    if (b.onSrc != null) Destroy(b.onSrc.gameObject);
                    if (b.offSrc != null) Destroy(b.offSrc.gameObject);
                }
                bands = null;
            }
            if (maxRpmSrc != null) Destroy(maxRpmSrc.gameObject);
            if (intakeOnSrc != null) Destroy(intakeOnSrc.gameObject);
            if (intakeOffSrc != null) Destroy(intakeOffSrc.gameObject);
            maxRpmSrc = intakeOnSrc = intakeOffSrc = null;
            masterGain = 0f;
            maxRpmGain = 0f;
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
            // Deferred rather than a straight Play(): on WebGL the browser has
            // not decoded this clip yet, and a source started against an
            // undecoded clip loops a null buffer — silence, forever, with no
            // retry. See AudioLoopStarter.
            AudioLoopStarter.PlayLoop(src);
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
            EnsureVoice();
            if (startupClip == null || oneShotSrc == null) return;
            // The countdown fires this a fraction of a second after SetFamily,
            // which is the tightest race in the game against the browser's
            // decoder — so it goes through the waiter too.
            AudioLoopStarter.PlayDelayed(oneShotSrc, startupClip, delay);
        }

        public void PlayShutdown()
        {
            EnsureVoice();
            if (engineStopClip == null || oneShotSrc == null) return;
            AudioLoopStarter.PlayOneShot(oneShotSrc, engineStopClip, 0.8f);
        }

        void Update()
        {
            EnsureVoice();
            if (car == null || bands == null || bands.Length == 0) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateAudibleRPM(dt);

            bool atLimiter = car.currentRPM >= car.revLimitRPM - 120f && car.throttleInput > 0.5f;
            limiterTimer = atLimiter ? LimiterHold : Mathf.Max(0f, limiterTimer - dt);
            bool limiterActive = limiterTimer > 0f;

            // Master position on the ladder, in the same geometric space as the
            // band home RPMs: r = log(rpm/idle) / log(redline/idle).
            float logSpan = Mathf.Log(LadderTopRPM / IdleRPM);
            float frac = Mathf.Clamp01(Mathf.Log(Mathf.Max(audibleRPM, IdleRPM) / IdleRPM) / logSpan);

            SelectBandPair(frac);

            // THE FIRING WOBBLE. A physical lope is a once-per-cycle unevenness,
            // so the oscillator is rpm-locked (rpm/60 x order Hz — ~5.8 Hz for a
            // half-order lope at 700 rpm) and its depth fades to nothing by
            // fadeTop, where real pulses fuse smooth. Applied as pitch wobble on
            // the band rates (the 0.012 s rate smoothing passes 7 Hz cleanly)
            // plus a gentler level pulse on the master. Zero extra voices; two
            // multipliers a frame. Depth was capped against the car's own safe
            // pitch window at bake time, so it can never drive a slot into the
            // rate clamp.
            float lopeWobble = 1f, lopeLevel = 1f;
            if (lopeDepth > 0f && lopeFadeTop > IdleRPM)
            {
                lopePhase = Mathf.Repeat(
                    lopePhase + Mathf.PI * 2f * (audibleRPM / 60f) * lopeOrder * dt,
                    Mathf.PI * 2f);
                float fade = Mathf.Clamp01((lopeFadeTop - audibleRPM) /
                                           Mathf.Max(1f, lopeFadeTop - IdleRPM));
                float d = lopeDepth * fade * fade;
                lopeWobble = 1f + d * Mathf.Sin(lopePhase + lopePhaseSeed);
                lopeLevel = 1f + d * 1.2f * Mathf.Sin(lopePhase + lopePhaseSeed + 1.1f);
            }

            // Load carries very little level: the timbre difference between a
            // closed and open throttle comes from the recordings themselves.
            float load = Mathf.Clamp01(car.throttleInput);
            float masterTarget = Mathf.Min(LevelCap, LevelBase + LevelLoadSlope * load)
                               * masterVolume * voiceLevelMul;
            masterGain = Smooth(masterGain, masterTarget, MasterTC, dt);
            // The lope's level pulse rides OUTSIDE the smoothing — at MasterTC
            // 0.05 s the smoother would eat most of a 6 Hz wobble.
            float mix = masterGain * lopeLevel;

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
                // this from binding while a band is actually audible. The per-car
                // rateMul rides ON TOP of the band tracking: the recording still
                // sits at its home RPM, the car just is not the same engine as
                // the one that was recorded.
                float rateTarget = Mathf.Clamp(
                    audibleRPM / Mathf.Max(b.homeRPM, 1f) * voiceRateMul * lopeWobble,
                    RateMin, RateMax);
                b.rate = Smooth(b.rate, rateTarget, RateTC, dt);

                float g = b.gain * mix;
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
            Apply(maxRpmSrc, maxRpmGain * mix, voiceRateMul);

            // Intake sits under everything, at the vendor's own fixed pitch and
            // volume curves: the ON take rises from silence at idle to full at
            // the redline, the OFF take stays out of the way until a third of
            // the way up and tops out at 0.4. See the intakeLevel note.
            float intakeGain = mix * intakeLevel;
            Apply(intakeOnSrc, intakeGain * frac * load, voiceRateMul);
            Apply(intakeOffSrc,
                  intakeGain * Mathf.Clamp01((frac - 0.3f) / 0.7f) * 0.4f * (1f - load),
                  voiceRateMul);
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
