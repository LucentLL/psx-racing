using UnityEngine;
using Set = PSXRacing.EngineVoiceLibrary.Set;
using Avail = PSXRacing.EngineVoiceLibrary.Avail;

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
    /// ON-DEMAND FAMILIES (phase 2). A car's family is an AssetBundle that may
    /// still be downloading when the car appears (EngineVoiceLibrary). The voice
    /// is a <see cref="VoiceSet"/> built only once its clips are present: until
    /// then the car is silent for <see cref="StandInDelay"/>, then speaks
    /// quietly through the default family, and the real voice fades in when it
    /// lands. A family that cannot be fetched keeps the default at full level.
    /// Nothing here blocks or throws on a missing family.
    ///
    /// What is kept from before, because it is about Unity/WebGL rather than
    /// about the sound: one dedicated AudioSource per clip, always playing and
    /// volume-gated (never reassign .clip at runtime - it resets timeSamples; a
    /// family change builds NEW sources and fades the old ones out);
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

        /// <summary>One family's live sources: the take ladder plus the extras,
        /// all under one child object so a teardown is one Destroy.</summary>
        sealed class VoiceSet
        {
            public string family;
            public Set set;
            /// <summary>The default family speaking for a family still on its way.</summary>
            public bool standIn;
            public bool fromPack;
            public EngineVoiceLibrary.Rig rig;
            public Voice[] voices;
            public AudioSource maxRpm, intakeOn, intakeOff;
            public AudioClip startup, engineStop;
            public GameObject root;
            /// <summary>Fade gain, and where it is heading.</summary>
            public float gain, target;
        }

        public CarController car;

        [Header("Voice")]
        /// <summary>Recorded family key (EngineVoiceLibrary). Set by the
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

        /// <summary>Seconds of silence a car waits for its own family before the
        /// default family stands in. Prefetch normally lands a family during
        /// the scene load; a stand-in for a brief gap is worse than the gap.</summary>
        const float StandInDelay = 1.5f;
        /// <summary>The stand-in speaks quietly: it is the wrong engine.</summary>
        const float StandInLevel = 0.45f;
        /// <summary>Fade of a voice arriving, and of one leaving.</summary>
        const float FadeInSeconds = 0.4f, FadeOutSeconds = 0.25f;
        /// <summary>A starter that could not play yet (family downloading) is
        /// still played if the voice lands within this long of when it was due.</summary>
        const float StartupGrace = 2f;

        VoiceSet live, fading;
        string wantFamily;
        float waitSince = -1f;
        AudioSource oneShotSrc;
        float pendingStartupAt = -1f;

        // ---- per-car voice (RG2's engineVoice.ts) ----------------------------
        // voiceRateMul rides as the vendor's own pitchMultiplier (+-2.5 st over
        // the catalog, median +0.1), voiceLevelMul as its masterVolume. The lope
        // only exists on 33 big-cam V8s and fades out by ~2500 rpm.
        float voiceRateMul = 1f;
        float voiceLevelMul = 1f;
        bool voiceSet;
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

        /// <summary>The family this car's live voice is built from (the
        /// default while it stands in), or null before the first build.</summary>
        public string LiveFamily => live != null ? live.family : null;
        /// <summary>True while the default family stands in for a download.</summary>
        public bool StandingIn => live != null && live.standIn;

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

        void OnDestroy()
        {
            Teardown(ref live);
            Teardown(ref fading);
        }

        /// <summary>
        /// Point this engine at a different recorded family. No-ops when the
        /// family is unchanged - a teardown mid-race would restart every loop
        /// from sample zero. A family still downloading is waited on (see the
        /// class notes); one that is here is built at once.
        /// </summary>
        public void SetFamily(string key)
        {
            key = EngineVoiceLibrary.Canonical(key);
            string resolved = EngineVoiceLibrary.Has(key) ? key : EngineVoiceLibrary.DefaultFamily;
            family = resolved;
            if (resolved == wantFamily) return;
            wantFamily = resolved;
            waitSince = Time.time;
            EngineVoiceLibrary.Request(resolved, Set.Exterior);
            UpdateVoice(0f);
        }

        public void SetFamily(CarSpec spec) => SetFamily(EngineVoiceLibrary.Resolve(spec));

        /// <summary>The car's own character on top of the shared recording:
        /// pitch multiplier, level and (V8s) lope. Zeroed fields fall back to
        /// neutral.</summary>
        public void SetVoice(CarSpec spec)
        {
            if (spec == null) return;
            voiceSet = true;
            voiceRateMul = spec.voiceRateMul > 0.05f ? spec.voiceRateMul : 1f;
            voiceLevelMul = spec.voiceLevelMul > 0.05f ? spec.voiceLevelMul : 1f;
            lopeDepth = Mathf.Max(0f, spec.lopeDepth);
            lopeOrder = spec.lopeOrder > 0f ? spec.lopeOrder : 0.5f;
            lopeFadeTop = spec.lopeFadeTop;
            lopePhaseSeed = spec.lopePhase;
        }

        // ------------------------------------------------------------------
        //  Building and swapping voices
        // ------------------------------------------------------------------

        /// <summary>
        /// Keep the live voice on the wanted family: build it the moment its
        /// clips are here, stand the default in after a short wait, fade the
        /// outgoing voice. Called every frame (cheap when settled).
        /// </summary>
        void UpdateVoice(float dt)
        {
            if (wantFamily == null)
            {
                // Nobody called SetFamily (standalone play): the serialized key.
                if (!voiceSet && car != null) SetVoice(car.activeSpec);
                string k = EngineVoiceLibrary.Canonical(family);
                wantFamily = EngineVoiceLibrary.Has(k) ? k : EngineVoiceLibrary.DefaultFamily;
                waitSince = Time.time;
            }

            bool settled = live != null && !live.standIn && live.family == wantFamily;
            if (!settled)
            {
                EngineVoiceLibrary.Request(wantFamily, Set.Exterior);
                var st = EngineVoiceLibrary.State(wantFamily, Set.Exterior);
                if (st == Avail.Ready)
                {
                    var built = Build(wantFamily, Set.Exterior, false);
                    if (built != null) SwapTo(built);
                    else if (live == null) SwapTo(Build(EngineVoiceLibrary.DefaultFamily, Set.Exterior, false));
                }
                else if (st == Avail.Loading || st == Avail.None)
                {
                    if (live == null && Time.time - waitSince >= StandInDelay)
                        SwapTo(Build(EngineVoiceLibrary.DefaultFamily, Set.Exterior, true));
                }
                else
                {
                    // Failed or missing: the default IS this car's voice now.
                    if (live != null && live.family == EngineVoiceLibrary.DefaultFamily) { live.standIn = false; live.target = 1f; }
                    else SwapTo(Build(EngineVoiceLibrary.DefaultFamily, Set.Exterior, false));
                    wantFamily = EngineVoiceLibrary.DefaultFamily;
                }
            }

            if (live != null) live.gain = Mathf.MoveTowards(live.gain, live.target, dt / FadeInSeconds);
            if (fading != null)
            {
                fading.gain = Mathf.MoveTowards(fading.gain, 0f, dt / FadeOutSeconds);
                if (fading.gain <= 0f) Teardown(ref fading);
            }
        }

        void SwapTo(VoiceSet next)
        {
            if (next == null) return;
            if (fading != null) Teardown(ref fading);
            fading = live;
            if (fading != null) fading.target = 0f;
            live = next;
            // The first voice of a car starts at full when its family was
            // already here (prefetched): nothing was audible before it, and a
            // fade-in on scene start reads as a car slow to "start". A voice
            // that had to be waited for, or replaces another, fades in.
            live.gain = fading == null && !live.standIn && Time.time - waitSince < 0.1f ? 1f : 0f;
            live.target = live.standIn ? StandInLevel : 1f;
        }

        VoiceSet Build(string fam, Set set, bool standIn)
        {
            fam = EngineVoiceLibrary.Canonical(fam);
            var rig = EngineVoiceLibrary.RigFor(fam);
            if (rig == null) return null;
            if (EngineVoiceLibrary.State(fam, set) != Avail.Ready) return null;

            var s = new VoiceSet { family = fam, set = set, standIn = standIn, rig = rig };
            s.root = new GameObject("snd_" + (set == Set.Interior ? "int_" : "") + fam);
            s.root.transform.SetParent(transform, false);

            var takes = useOffTakes ? rig.hq : rig.mq;
            var built = new System.Collections.Generic.List<Voice>(takes.Length);
            foreach (var t in takes)
            {
                var onClip = EngineVoiceLibrary.Clip(fam, t.onClip, set);
                if (onClip == null) continue;
                var v = new Voice { take = t, on = MakeLoop(s, onClip, t.onClip) };
                if (useOffTakes && t.offClip != null)
                    v.off = MakeLoop(s, EngineVoiceLibrary.Clip(fam, t.offClip, set), t.offClip);
                built.Add(v);
            }
            s.voices = built.ToArray();
            if (s.voices.Length == 0) { Object.Destroy(s.root); return null; }

            // Opponents skip the limiter take and the intake pair: three more
            // always-resident voices each, for detail nobody hears from another
            // car. The one-shots stay - a grid of engines firing up is the point
            // of the countdown.
            if (useOffTakes)
            {
                s.maxRpm = MakeLoop(s, EngineVoiceLibrary.Clip(fam, "maxRPM", set), "maxRPM");
                s.intakeOn = MakeLoop(s, EngineVoiceLibrary.Clip(fam, "intake_on", set), "intake_on");
                s.intakeOff = MakeLoop(s, EngineVoiceLibrary.Clip(fam, "intake_off", set), "intake_off");
            }
            s.startup = EngineVoiceLibrary.Clip(fam, "startup", set);
            s.engineStop = EngineVoiceLibrary.Clip(fam, "engine_stop", set);

            s.fromPack = !(set == Set.Exterior && fam == EngineVoiceLibrary.DefaultFamily);
            if (s.fromPack) EngineVoiceLibrary.Acquire(fam, set);
            return s;
        }

        void Teardown(ref VoiceSet s)
        {
            if (s == null) return;
            if (s.root != null) Destroy(s.root);
            if (s.fromPack) EngineVoiceLibrary.Release(s.family, s.set);
            s = null;
        }

        AudioSource MakeLoop(VoiceSet s, AudioClip clip, string label)
        {
            if (clip == null) return null;
            var go = new GameObject("snd_" + label);
            go.transform.SetParent(s.root.transform, false);
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

        // ------------------------------------------------------------------
        //  One-shots
        // ------------------------------------------------------------------

        /// <summary>The starter, <paramref name="delay"/> seconds from now. If
        /// the car's family is still on its way it plays when the voice lands
        /// (within <see cref="StartupGrace"/> of when it was due) - never the
        /// stand-in's starter, which would be another engine's.</summary>
        public void PlayStartup(float delay = 0f)
        {
            pendingStartupAt = Time.time + Mathf.Max(0f, delay);
            TryStartup();
        }

        void TryStartup()
        {
            if (pendingStartupAt < 0f || live == null || live.standIn) return;
            if (Time.time > pendingStartupAt + StartupGrace) { pendingStartupAt = -1f; return; }
            if (live.startup != null && oneShotSrc != null)
            {
                oneShotSrc.volume = AudioPrefs.Engine;   // the ENGINE volume (settings, AUDIO)
                AudioLoopStarter.PlayDelayed(oneShotSrc, live.startup, Mathf.Max(0f, pendingStartupAt - Time.time));
            }
            pendingStartupAt = -1f;
        }

        public void PlayShutdown()
        {
            pendingStartupAt = -1f;
            if (live == null || live.engineStop == null || oneShotSrc == null) return;
            oneShotSrc.volume = AudioPrefs.Engine;
            AudioLoopStarter.PlayOneShot(oneShotSrc, live.engineStop, 0.8f);
        }

        // ------------------------------------------------------------------
        //  The mix
        // ------------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateVoice(dt);
            TryStartup();
            if (car == null || live == null) return;

            UpdateAudibleRPM(dt);
            float lim = LimiterRPM;
            float cv = audibleRPM / lim;              // the vendor's clipsValue

            // ---- gas pedal -> engineLoad. The vendor lerps toward the pedal at
            // gasPedalSimSpeed x 0.02 per FixedUpdate (~70 ms); its controller
            // integrations release the pedal for 0.2 s on every gear change.
            if (car.currentGear != lastGear) { lastGear = car.currentGear; shiftLiftTimer = ShiftLift; }
            shiftLiftTimer = Mathf.Max(0f, shiftLiftTimer - dt);
            float pedal = shiftLiftTimer > 0f ? 0f : Mathf.Clamp01(car.throttleInput);
            float gasF = Mathf.Clamp(0.02f * live.rig.gasPedalSimSpeed, 0.001f, 1f);
            load = Mathf.Lerp(load, pedal, 1f - Mathf.Pow(1f - gasF, dt / 0.02f));

            // ---- v2.3 Engine Load: adsrVol from the last workload, then update
            float sustain = live.rig.sustainAdsrVol;
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
            float rate = voiceRateMul * lopeWobble;
            Mix(live, cv, master, rate);
            if (fading != null) Mix(fading, cv, master, rate);
        }

        /// <summary>One voice set through the vendor's arithmetic, at its fade gain.</summary>
        void Mix(VoiceSet s, float cv, float master, float rate)
        {
            var rig = s.rig;
            master *= s.gain;
            float pitchMul = rig.pitchMultiplier * rate;

            var onCurve = useOffTakes ? rig.onLoad : rig.mqOnLoad;
            var offCurve = useOffTakes ? rig.offLoad : rig.mqOffLoad;
            float onL = onCurve.Evaluate(cv), offL = offCurve.Evaluate(cv);

            // The limiter take is the vendor's maxRPMVolCurve (a step at 98.8% of
            // the limiter) and is NOT gated on the pedal. While it is up, every
            // other take drops by revLimiterTweaker - DynamicSound compares
            // against 0.95 x loadCurve, the idle take against 0.95 flat.
            float mx = s.maxRpm != null ? rig.maxRPMVol.Evaluate(cv) * onL : 0f;
            float div = mx > 0f && mx >= 0.95f * onL ? rig.revLimiterTweaker : 1f;
            float divIdle = mx >= 0.95f ? rig.revLimiterTweaker : 1f;
            float idleLoad = Mathf.Lerp(offL, onL, load);

            foreach (var v in s.voices)
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

            Apply(s.maxRpm, mx * master,
                  Mathf.Clamp(rig.hq[rig.hq.Length - 1].pitch.Evaluate(cv) * pitchMul, PitchMin, PitchMax));

            // Intake: the vendor's own volume curves x onLoad x load, pitched by
            // its aggressiveness pitch curve, and mixed by where the listener is -
            // full in front of the car or inside it, nearly nothing from behind.
            if (s.intakeOn != null || s.intakeOff != null)
            {
                float mix = IntakeMixing() * rig.intakeMaster * onL * master;
                float ip = Mathf.Clamp(rig.intakePitch.Evaluate(cv) * pitchMul, PitchMin, PitchMax);
                Apply(s.intakeOn, rig.intakeOnVol.Evaluate(cv) * load * mix, ip);
                Apply(s.intakeOff, rig.intakeOffVol.Evaluate(cv) * (1f - load) * mix, ip);
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
                float adj = rate * Mathf.Lerp(1f, 2f, Mathf.InverseLerp(0f, top, kmh)) * live.rig.effortSensitivity;
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
