using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Sample-based engine voice: the vendor's own rig (Realistic Engine Sounds 2
    /// Pro v2.3.1) over the vendor's own recordings, one family per car.
    ///
    /// WHAT THE VENDOR RIG IS. Every take of a family (idle, then seven
    /// on/off-throttle pairs from idle_low to very_high) has a VOLUME curve and a
    /// PITCH curve over clipsValue = rpm / limiter, authored in the vendor's HQ
    /// prefab. The volume curves are flat-topped windows with short overlaps, so
    /// most of the time one take carries the engine; the pitch curves say how
    /// fast each recording plays at each rpm, and where two windows overlap the
    /// two takes land on the SAME note (measured on rotary_x7: 0.0-0.1 semitone
    /// apart from 3000 rpm up). On-throttle takes are scaled by onLoadVolumeByRPM
    /// x load, off-throttle takes by offLoadVolumeByRPM x (1 - load), the idle
    /// take by a lerp of the two; at the limiter the maxRPM take comes in and
    /// everything else drops by revLimiterTweaker. v2.3's Engine Load puts one
    /// more gain over all of it (adsrVol, from an rpm-acceleration "workload").
    /// The curves are baked out of the prefabs by tools/res2_curves.mjs into
    /// Resources/res2_rig.json; this file is the vendor's DynamicSoundController
    /// arithmetic (decompiled with Cecil) applied to them.
    ///
    /// WHAT IT REPLACED, AND WHY (2026-10-16, "the RX-7s sound very very bad",
    /// "the 240ZG has a strange hum"). The old voice was a geometric ladder that
    /// assumed the eight takes sit at fixed fractions between the CAR's idle and
    /// redline and played each at rate = rpm / home. The recordings do not sit
    /// there: on the FD (idle 500, redline 7500) that ladder put the "med" take
    /// at 1834 rpm where the vendor plays it around 4500, so every note came out
    /// 4.4-10.9 semitones above the vendor rig's (an octave too high at
    /// 1000-2000 rpm, +6.9 st at the limiter), and the two takes it crossfaded
    /// were 1.5-4.1 semitones apart - two engines at once. It also played the
    /// intake takes at a FIXED pitch: those are tonal high-rpm engine takes
    /// (i6_japanese_2's is 40% one 498 Hz line), so a constant tone that never
    /// moved with rpm sat 13-14 dB under the engine whenever the throttle was
    /// down. That was the hum. The vendor pitches intake with its
    /// aggressiveness pitch curve and, from a chase camera, barely plays it.
    /// tools/engine_audio_report.mjs and tools/engine_pitch_shift.mjs measure all
    /// of this against an offline port of the vendor rig.
    ///
    /// What is kept from before, because it is about Unity/WebGL rather than
    /// about the sound: one dedicated AudioSource per clip, always playing and
    /// volume-gated (never reassign .clip at runtime - it resets timeSamples);
    /// AudioLoopStarter for the browser's late decode; LoopSeam on every loop;
    /// the capped rpm fall so an upshift does not read as a slide whistle.
    /// </summary>
    public class EngineAudio : MonoBehaviour
    {
        sealed class Voice
        {
            public EngineVoiceLibrary.Take take;
            public AudioSource on, off;
        }

        public CarController car;

        [Header("Voice")]
        /// <summary>Recorded family folder under Resources/Engines. Set by the
        /// builder to the default and re-set per car by RaceHandoffApplier once
        /// the catalog spec is known.</summary>
        public string family = EngineVoiceLibrary.DefaultFamily;

        [Header("Mix")]
        [Range(0f, 1f)] public float masterVolume = 1f;
        public bool spatial;
        public float spatialMaxDistance = 90f;
        /// <summary>TRUE (player): the vendor's HQ rig - idle, seven on/off
        /// pairs, the limiter take and the intake pair, 18 voices. FALSE
        /// (opponents): the vendor's Medium Quality rig with its "Simulated"
        /// off-load - idle + low/med/very_high on-throttle takes only, 4 voices,
        /// so a full grid stays inside the real-voice budget.</summary>
        public bool useOffTakes = true;

        /// <summary>Overall level against the rest of the game. At 1.0 a
        /// full-throttle pull through the now-flat tone chain lands where the
        /// old voice sat through the old one: measured on the probe, -0.5 to
        /// +1.0 dB across the FD, 240ZG, 270R and Z31. Off-throttle is 5-7 dB
        /// quieter than before - that is the vendor's offLoad curve and v2.3
        /// Engine Load doing what they are for.</summary>
        const float LevelGain = 1.0f;
        const float RpmFallRateCap = 4200f;    // rpm/s
        const float RpmFallSnap = 5000f;       // a drop bigger than this snaps through
        const float PitchMin = 0.05f, PitchMax = 3f;
        /// <summary>The vendor's controller integrations lift the gas pedal for
        /// this long on every gear change (CustomCarController_RES2.waitAtShifting).</summary>
        const float ShiftLift = 0.2f;

        EngineVoiceLibrary.Rig rig;
        Voice[] voices;
        AudioClip maxRpmClip, startupClip, engineStopClip, intakeOnClip, intakeOffClip;
        AudioSource maxRpmSrc, intakeOnSrc, intakeOffSrc, oneShotSrc;

        // ---- per-car voice (RG2's engineVoice.ts) ----------------------------
        // voiceRateMul rides as the vendor's own pitchMultiplier (+-2.5 st over
        // the catalog, median +0.1), voiceLevelMul as its masterVolume. The lope
        // only exists on 33 big-cam V8s and fades out by ~2500 rpm.
        float voiceRateMul = 1f;
        float voiceLevelMul = 1f;
        float lopeDepth, lopeOrder = 0.5f, lopeFadeTop, lopePhaseSeed;
        float lopePhase;

        float audibleRPM;
        float load;                 // the vendor's engineLoad: smoothed gas pedal
        float workload, effortTimer, effortSample;   // v2.3 EngineEffort
        int lastGear;
        float shiftLiftTimer;
        AudioListener listener;
        float listenerRetry;

        float IdleRPM => car != null ? car.idleRPM : 900f;
        /// <summary>The vendor's maxRPMLimit: "car's maximum RPM", i.e. the
        /// limiter. clipsValue = rpm / this.</summary>
        float LimiterRPM => car != null ? Mathf.Max(car.revLimitRPM, car.idleRPM + 100f) : 8000f;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            audibleRPM = IdleRPM;

            oneShotSrc = gameObject.AddComponent<AudioSource>();
            oneShotSrc.playOnAwake = false;
            oneShotSrc.loop = false;
            ConfigureSpatial(oneShotSrc);

            // The voice is NOT built here: RaceHandoffApplier learns which car
            // this is during Start, and building the default family now would
            // decompress a family nothing then plays. Standalone editor play,
            // where nothing calls SetFamily, still gets the default on the first
            // frame that needs sound.
        }

        void EnsureVoice()
        {
            if (voices != null) return;
            if (voiceRateMul == 1f && car != null) SetVoice(car.activeSpec);
            BuildVoice();
        }

        /// <summary>
        /// Point this engine at a different recorded family and rebuild. No-ops
        /// when the family is unchanged - a teardown mid-race would restart every
        /// loop from sample zero.
        /// </summary>
        public void SetFamily(string key)
        {
            key = EngineVoiceLibrary.Canonical(key);
            string resolved = EngineVoiceLibrary.Has(key) ? key : EngineVoiceLibrary.DefaultFamily;
            if (resolved == family && voices != null) return;
            family = resolved;
            BuildVoice();
        }

        public void SetFamily(CarSpec spec) => SetFamily(EngineVoiceLibrary.Resolve(spec));

        /// <summary>The car's own character on top of the shared recording:
        /// pitch multiplier, level and (V8s) lope. Zeroed fields fall back to
        /// neutral.</summary>
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
            family = EngineVoiceLibrary.Canonical(family);
            rig = EngineVoiceLibrary.RigFor(family);
            if (rig == null && family != EngineVoiceLibrary.DefaultFamily)
            {
                family = EngineVoiceLibrary.DefaultFamily;
                rig = EngineVoiceLibrary.RigFor(family);
            }
            if (rig == null) { voices = new Voice[0]; return; }

            var takes = useOffTakes ? rig.hq : rig.mq;
            var built = new System.Collections.Generic.List<Voice>(takes.Length);
            foreach (var t in takes)
            {
                var onClip = EngineVoiceLibrary.Clip(family, t.onClip);
                if (onClip == null) continue;
                var v = new Voice { take = t, on = MakeLoop(onClip, t.onClip) };
                if (useOffTakes && t.offClip != null)
                    v.off = MakeLoop(EngineVoiceLibrary.Clip(family, t.offClip), t.offClip);
                built.Add(v);
            }
            voices = built.ToArray();

            // Opponents skip the limiter take and the intake pair: three more
            // always-resident voices each, for detail nobody hears from another
            // car. The one-shots stay - a grid of engines firing up is the point
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

        /// <summary>Destroy the previous family's sources - each loop lives on
        /// its own child GameObject.</summary>
        void TeardownVoice()
        {
            if (voices != null)
            {
                foreach (var v in voices)
                {
                    if (v.on != null) Destroy(v.on.gameObject);
                    if (v.off != null) Destroy(v.off.gameObject);
                }
                voices = null;
            }
            if (maxRpmSrc != null) Destroy(maxRpmSrc.gameObject);
            if (intakeOnSrc != null) Destroy(intakeOnSrc.gameObject);
            if (intakeOffSrc != null) Destroy(intakeOffSrc.gameObject);
            maxRpmSrc = intakeOnSrc = intakeOffSrc = null;
            rig = null;
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
            // undecoded clip loops a null buffer forever. See AudioLoopStarter.
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
            oneShotSrc.volume = AudioPrefs.Engine;   // the ENGINE volume (settings, AUDIO)
            AudioLoopStarter.PlayDelayed(oneShotSrc, startupClip, delay);
        }

        public void PlayShutdown()
        {
            EnsureVoice();
            if (engineStopClip == null || oneShotSrc == null) return;
            oneShotSrc.volume = AudioPrefs.Engine;
            AudioLoopStarter.PlayOneShot(oneShotSrc, engineStopClip, 0.8f);
        }

        void Update()
        {
            EnsureVoice();
            if (car == null || rig == null || voices == null || voices.Length == 0) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateAudibleRPM(dt);
            float lim = LimiterRPM;
            float cv = audibleRPM / lim;              // the vendor's clipsValue

            // ---- gas pedal -> engineLoad. The vendor lerps toward the pedal at
            // gasPedalSimSpeed x 0.02 per FixedUpdate (~70 ms); its controller
            // integrations release the pedal for 0.2 s on every gear change.
            if (car.currentGear != lastGear) { lastGear = car.currentGear; shiftLiftTimer = ShiftLift; }
            shiftLiftTimer = Mathf.Max(0f, shiftLiftTimer - dt);
            float pedal = shiftLiftTimer > 0f ? 0f : Mathf.Clamp01(car.throttleInput);
            float gasF = Mathf.Clamp(0.02f * rig.gasPedalSimSpeed, 0.001f, 1f);
            load = Mathf.Lerp(load, pedal, 1f - Mathf.Pow(1f - gasF, dt / 0.02f));

            // ---- v2.3 Engine Load: adsrVol from the last workload, then update
            float sustain = rig.sustainAdsrVol;
            float adsr = Mathf.Lerp(Mathf.Max(1f - cv, sustain), Mathf.Lerp(sustain, 1f, workload), load);
            UpdateWorkload(dt, lim);

            // ---- per-car lope (V8s only): rpm-locked pitch + level wobble
            float lopeWobble = 1f, lopeLevel = 1f;
            if (lopeDepth > 0f && lopeFadeTop > IdleRPM)
            {
                lopePhase = Mathf.Repeat(
                    lopePhase + Mathf.PI * 2f * (audibleRPM / 60f) * lopeOrder * dt, Mathf.PI * 2f);
                float fade = Mathf.Clamp01((lopeFadeTop - audibleRPM) / Mathf.Max(1f, lopeFadeTop - IdleRPM));
                float d = lopeDepth * fade * fade;
                lopeWobble = 1f + d * Mathf.Sin(lopePhase + lopePhaseSeed);
                lopeLevel = 1f + d * 1.2f * Mathf.Sin(lopePhase + lopePhaseSeed + 1.1f);
            }

            float master = adsr * masterVolume * voiceLevelMul * LevelGain * lopeLevel;
            float pitchMul = rig.pitchMultiplier * voiceRateMul * lopeWobble;

            var onCurve = useOffTakes ? rig.onLoad : rig.mqOnLoad;
            var offCurve = useOffTakes ? rig.offLoad : rig.mqOffLoad;
            float onL = onCurve.Evaluate(cv), offL = offCurve.Evaluate(cv);

            // The limiter take is the vendor's maxRPMVolCurve (a step at 98.8% of
            // the limiter) and is NOT gated on the pedal. While it is up, every
            // other take drops by revLimiterTweaker - DynamicSound compares
            // against 0.95 x loadCurve, the idle take against 0.95 flat.
            float mx = maxRpmSrc != null ? rig.maxRPMVol.Evaluate(cv) * onL : 0f;
            float div = mx > 0f && mx >= 0.95f * onL ? rig.revLimiterTweaker : 1f;
            float divIdle = mx >= 0.95f ? rig.revLimiterTweaker : 1f;
            float idleLoad = Mathf.Lerp(offL, onL, load);

            foreach (var v in voices)
            {
                var t = v.take;
                float vol = t.vol.Evaluate(cv) * master;
                float pitch = Mathf.Clamp(t.pitch.Evaluate(cv) * pitchMul, PitchMin, PitchMax);
                if (t.offClip == null) Apply(v.on, vol * idleLoad / divIdle, pitch);       // idle
                else if (v.off != null)
                {
                    Apply(v.on, vol * onL * load / div, pitch);
                    Apply(v.off, vol * offL * (1f - load) / div, pitch);
                }
                else Apply(v.on, vol * idleLoad, pitch);   // opponents: vendor "Simulated" off-load
            }

            Apply(maxRpmSrc, mx * master,
                  Mathf.Clamp(rig.hq[rig.hq.Length - 1].pitch.Evaluate(cv) * pitchMul, PitchMin, PitchMax));

            // Intake: the vendor's own volume curves x onLoad x load, pitched by
            // its aggressiveness pitch curve, and mixed by where the listener is -
            // full in front of the car or inside it, nearly nothing from behind.
            if (intakeOnSrc != null || intakeOffSrc != null)
            {
                float mix = IntakeMixing() * rig.intakeMaster * onL * master;
                float ip = Mathf.Clamp(rig.intakePitch.Evaluate(cv) * pitchMul, PitchMin, PitchMax);
                Apply(intakeOnSrc, rig.intakeOnVol.Evaluate(cv) * load * mix, ip);
                Apply(intakeOffSrc, rig.intakeOffVol.Evaluate(cv) * (1f - load) * mix, ip);
            }
        }

        /// <summary>
        /// v2.3 EngineEffort: every 50 ms, how hard the engine is working, from
        /// the relative rpm rise (more sensitive at speed), the road speed and
        /// the last 25% of the rev range. Attack 10/s, release 2/s.
        /// </summary>
        void UpdateWorkload(float dt, float lim)
        {
            effortTimer += dt;
            if (effortTimer < 0.05f) return;
            float rpm = audibleRPM;
            if (rpm < 900f) workload = 0f;
            else
            {
                float top = Mathf.Max(car.topSpeedMps * 3.6f, 1f);
                float kmh = Mathf.Abs(car.speedKmh);
                float rate = Mathf.Max(0f, (rpm - effortSample) / effortTimer) / Mathf.Max(rpm, 1f);
                float adj = rate * Mathf.Lerp(1f, 2f, Mathf.InverseLerp(0f, top, kmh)) * rig.effortSensitivity;
                float target = Mathf.Max(Mathf.InverseLerp(0.03f, 0.35f, adj),
                                         Mathf.Max(Mathf.InverseLerp(top * 0.5f, top, kmh),
                                                   Mathf.InverseLerp(0.75f, 0.9f, rpm / lim)));
                workload = Mathf.Lerp(workload, target, (target > workload ? 10f : 2f) * effortTimer);
            }
            effortSample = rpm;
            effortTimer = 0f;
        }

        /// <summary>The vendor's IntakeMixing: LerpUnclamped(0.5, 1, z / d) of
        /// the listener in the car's frame - 1 in front, 0.5 beside, ~0 behind -
        /// and 1 inside the car (its interior prefabs). Opponents have no intake.</summary>
        float IntakeMixing()
        {
            if (listener == null)
            {
                if (Time.unscaledTime < listenerRetry) return 0f;
                listenerRetry = Time.unscaledTime + 1f;
                listener = FindAnyObjectByType<AudioListener>();
                if (listener == null) return 0f;
            }
            Vector3 local = transform.InverseTransformPoint(listener.transform.position);
            float d = local.magnitude;
            if (d < 1.6f) return 1f;
            return Mathf.Clamp01(Mathf.LerpUnclamped(0.5f, 1f, local.z / d));
        }

        /// <summary>
        /// RPM may rise instantly but falls at a capped mechanical rate, so an
        /// auto upshift does not read as a descending slide.
        /// </summary>
        void UpdateAudibleRPM(float dt)
        {
            float raw = car.currentRPM;
            if (raw >= audibleRPM || (audibleRPM - raw) > RpmFallSnap)
                audibleRPM = raw;
            else
                audibleRPM = Mathf.Max(raw, audibleRPM - RpmFallRateCap * dt);
        }

        static void Apply(AudioSource src, float volume, float pitch)
        {
            if (src == null) return;
            // Most takes sit at zero gain; skip the native writes for those, but
            // keep them playing so they never restart out of phase.
            if (volume < 0.0005f)
            {
                if (src.volume != 0f) src.volume = 0f;
                return;
            }
            // The ENGINE volume (settings, AUDIO) on every engine voice.
            src.volume = volume * AudioPrefs.Engine;
            src.pitch = pitch;
        }
    }
}
