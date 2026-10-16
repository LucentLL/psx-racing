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
            public AudioSource maxRpm, intakeOn, intakeOff, aggOn, aggOff;
            public AudioClip startup, engineStop;
            public GameObject root;
            /// <summary>Every source of the set, for the spatial updates.</summary>
            public readonly System.Collections.Generic.List<AudioSource> all =
                new System.Collections.Generic.List<AudioSource>();
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
        /// <summary>The cockpit set (interior recordings), while the camera is
        /// in this car's seat; <see cref="insideMix"/> crossfades the two.</summary>
        VoiceSet inner;
        float insideMix;
        /// <summary>The family the exterior voice was built from before it was
        /// parked (fully inside, the exterior set is torn down).</summary>
        string parkedFamily;
        /// <summary>The crossfade between the chase and cockpit sets.</summary>
        const float ViewFadeSeconds = 0.25f;
        string wantFamily;
        Ear ear = Ear.Chase;
        float blendNow = -1f;
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
        public string LiveFamily => live != null ? live.family : inner != null ? inner.family : null;
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
            Teardown(ref inner);
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
            crackleLevel = CrackleLevelFor(spec, EngineVoiceLibrary.Resolve(spec));
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

            // Fully in the seat: the exterior voice is parked (torn down) so the
            // two sets never both run at full voice count past the crossfade.
            bool cabinOnly = inner != null && inner.family == wantFamily && insideMix >= 1f && ear == Ear.Cockpit;
            if (cabinOnly)
            {
                if (live != null) { parkedFamily = live.family; Teardown(ref live); }
                if (fading != null) Teardown(ref fading);
            }

            bool settled = cabinOnly || (live != null && !live.standIn && live.family == wantFamily);
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
            // that had to be waited for, or replaces another, fades in. Back
            // out of the seat, the view crossfade (insideMix) does the fading.
            bool unparking = parkedFamily != null && parkedFamily == live.family;
            live.gain = fading == null && !live.standIn && (unparking || Time.time - waitSince < 0.1f) ? 1f : 0f;
            live.target = live.standIn ? StandInLevel : 1f;
            parkedFamily = null;
        }

        VoiceSet Build(string fam, Set set, bool standIn)
        {
            fam = EngineVoiceLibrary.Canonical(fam);
            var rig = set == Set.Interior ? EngineVoiceLibrary.InteriorRigFor(fam) : EngineVoiceLibrary.RigFor(fam);
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
                // The exhaust "fx" layer (phase 2, item 3): what the owner
                // missed on the FD - "not as airy as a real exhaust".
                if (rig.aggMaster > 0f)
                {
                    s.aggOn = MakeLoop(s, EngineVoiceLibrary.Clip(fam, "aggressiveness_on", set), "aggressiveness_on");
                    s.aggOff = MakeLoop(s, EngineVoiceLibrary.Clip(fam, "aggressiveness_off", set), "aggressiveness_off");
                }
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
            if (s.set == Set.Interior)
            {
                // The cabin is AT the listener: 2D, and created that way, so
                // no blend ever crosses zero on it (see CockpitBlend).
                src.spatialBlend = 0f;
                src.dopplerLevel = 0f;
            }
            else ConfigureSpatial(src);
            s.all.Add(src);
            // Deferred rather than a straight Play(): on WebGL the browser has
            // not decoded this clip yet, and a source started against an
            // undecoded clip loops a null buffer forever. See AudioLoopStarter.
            AudioLoopStarter.PlayLoop(src);
            return src;
        }

        void ConfigureSpatial(AudioSource src)
        {
            if (spatial)
            {
                // Opponents: always a car somewhere else on the road.
                src.spatialBlend = 1f;
                src.dopplerLevel = OutsideDoppler;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = spatialMaxDistance;
                src.minDistance = OutsideMinDistance;
                return;
            }
            ApplyPlayerSpatial(src, blendNow >= 0f ? blendNow : BlendFor(ear), ear,
                               OutsideMinDistance, spatialMaxDistance, OutsideDoppler);
        }

        // ------------------------------------------------------------------
        //  Where the listener is (phase 2, item 2)
        // ------------------------------------------------------------------

        /// <summary>
        /// Where the listener is relative to a car, for that car's OWN sounds
        /// (engine, tyres, turbo, wind).
        ///   Outside - a trackside replay lens, the chase rig following another
        ///             car, the player on foot: the car is a sound source out in
        ///             the world, full 3D with the opponents' rolloff and
        ///             doppler, so it comes at the lens and flies by.
        ///   Chase   - the rig following THIS car from outside (chase, close,
        ///             roof, hood, bumper, top down): mostly 2D, see ChaseBlend.
        ///   Cockpit - the rig in this car's driver's seat: 2D (and the
        ///             interior recordings).
        /// Before phase 2 the player's car was 2D in every one of these
        /// (PSXRacingBuilder: spatial = !isPlayer), so a replay's lenses heard
        /// the opponents pass and the player's own car not move at all.
        /// </summary>
        public enum Ear { Outside, Chase, Cockpit }

        public static Ear EarFor(CarController car)
        {
            if (car == null) return Ear.Outside;
            var rr = RaceReplay.Instance;
            if (RaceReplay.Playing && rr != null && rr.Mode == RaceReplay.CamMode.Trackside) return Ear.Outside;
            var cc = ChaseCamera.Active;
            if (cc == null || !cc.isActiveAndEnabled) return Ear.Outside;
            bool mine = cc.targetCar == car || (cc.targetCar == null && cc.target == car.transform);
            if (!mine) return Ear.Outside;
            return ChaseCamera.Current == ChaseCamera.View.Cockpit ? Ear.Cockpit : Ear.Chase;
        }

        /// <summary>
        /// Spatial blend of the player's car heard from its own chase rig.
        ///
        /// The chase listener sits 5-7 m behind and above the car and turns
        /// with it, so a full 3D source would add nothing but artefacts (a
        /// constant distance, a centred pan, and doppler from every camera
        /// lag). What a little 3D does add is the car moving in the stereo
        /// image when the rig swings off it - the drift camera's hold, a
        /// crash, a hairpin's camera lag. Unity's (and the WebGL build's)
        /// blend is a dry/wet mix: (1 - b) of the signal unpanned plus b
        /// through an equal-power panner (Web Audio's rule for a stereo
        /// source, which the WebGL build runs). At b = 0.35 a car 30 deg
        /// off-axis - about as far as a chase swing goes - comes out 1.8 dB
        /// louder in the near ear, and one at 90 deg 6.3 dB: heard, never a
        /// car that seems to be somewhere else. At 0.5 that is 2.5 / 9.5 dB
        /// and the far side of the image goes thin on every drift; at 0.25,
        /// 1.3 / 4.4 dB, which a drift barely moves.
        /// </summary>
        public const float ChaseBlend = 0.35f;
        /// <summary>The cockpit: 2D in effect. Not 0, because the WebGL backend
        /// rewires a source's audio nodes whenever its blend crosses zero
        /// (Audio.js setSpatialBlendLevel), a click on the tyres and the
        /// turbo at every view change; 2% wet is inaudible.</summary>
        public const float CockpitBlend = 0.02f;
        /// <summary>Outside: the opponents' linear rolloff (6..90 m for the
        /// engine) and doppler (0.6).</summary>
        public const float OutsideMinDistance = 6f, OutsideDoppler = 0.6f;
        /// <summary>From the chase rig nothing attenuates (the rig never gets
        /// this far from its own car unless it is cutting back to it).</summary>
        public const float ChaseMinDistance = 12f;
        /// <summary>Seconds for a blend change (replay camera modes, views).</summary>
        public const float BlendSeconds = 0.25f;

        public static float BlendFor(Ear e) => e == Ear.Outside ? 1f : e == Ear.Chase ? ChaseBlend : CockpitBlend;

        /// <summary>One player-car source to the listener's place. Doppler only
        /// Outside: from the chase rig or the seat, relative motion is camera
        /// lag, and doppler on it is a pitch wobble on a tuned voice.</summary>
        public static void ApplyPlayerSpatial(AudioSource src, float blend, Ear e,
                                              float outsideMin, float outsideMax, float outsideDoppler)
        {
            if (src == null) return;
            src.spatialBlend = Mathf.Max(CockpitBlend, blend);
            src.dopplerLevel = e == Ear.Outside ? outsideDoppler : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = e == Ear.Outside ? outsideMin : Mathf.Max(outsideMin, ChaseMinDistance);
            src.maxDistance = outsideMax;
        }

        /// <summary>Follow the listener (player car only): the ear, and the
        /// blend eased toward it; sources are only touched when it moves.</summary>
        void UpdateSpatial(float dt)
        {
            if (spatial) return;
            var e = EarFor(car);
            float target = BlendFor(e);
            if (blendNow < 0f) blendNow = target;
            float nb = Mathf.MoveTowards(blendNow, target, dt / BlendSeconds);
            if (e == ear && Mathf.Abs(nb - blendNow) < 1e-4f) return;
            ear = e;
            blendNow = nb;
            ApplyPlayerSpatial(oneShotSrc, blendNow, ear, OutsideMinDistance, spatialMaxDistance, OutsideDoppler);
            ApplyPlayerSpatial(crackleSrc, blendNow, ear, OutsideMinDistance, spatialMaxDistance, OutsideDoppler);
            ApplySet(live);
            ApplySet(fading);
        }

        void ApplySet(VoiceSet s)
        {
            if (s == null || s.set == Set.Interior) return;
            foreach (var src in s.all)
                ApplyPlayerSpatial(src, blendNow, ear, OutsideMinDistance, spatialMaxDistance, OutsideDoppler);
        }

        /// <summary>The listener's place for this car right now (tests, HUD).</summary>
        public Ear CurrentEar => spatial ? Ear.Outside : ear;
        public float CurrentBlend => spatial ? 1f : blendNow;

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
            var s = Speaking;
            if (pendingStartupAt < 0f || s == null || s.standIn) return;
            if (Time.time > pendingStartupAt + StartupGrace) { pendingStartupAt = -1f; return; }
            if (s.startup != null && oneShotSrc != null)
            {
                oneShotSrc.volume = AudioPrefs.Engine;   // the ENGINE volume (settings, AUDIO)
                AudioLoopStarter.PlayDelayed(oneShotSrc, s.startup, Mathf.Max(0f, pendingStartupAt - Time.time));
            }
            pendingStartupAt = -1f;
        }

        public void PlayShutdown()
        {
            pendingStartupAt = -1f;
            var s = Speaking;
            if (s == null || s.engineStop == null || oneShotSrc == null) return;
            oneShotSrc.volume = AudioPrefs.Engine;
            AudioLoopStarter.PlayOneShot(oneShotSrc, s.engineStop, 0.8f);
        }

        /// <summary>The set the one-shots come from: the cabin's while the
        /// camera is (mostly) in the seat, else the exterior voice.</summary>
        VoiceSet Speaking => inner != null && insideMix >= 0.5f ? inner : live;

        /// <summary>
        /// THE COCKPIT SET (phase 2, item 4). With the camera in this car's
        /// seat the engine is the vendor's INTERIOR recordings of the family
        /// (their own bundle, &lt;family&gt;_int, asked for the first time the
        /// seat camera is on this car - which is when CockpitInterior shows -
        /// and kept), under the interior prefab's curves and level, 2D, intake
        /// at full as the vendor plays it inside. Out of the seat, the exterior
        /// set. A view change crossfades the two over 0.25 s; past it, only one
        /// set is running (the exterior one is parked inside, the cabin torn
        /// down outside). Until the cabin set is here, the exterior voice keeps
        /// playing in the seat.
        /// </summary>
        void UpdateInterior(float dt)
        {
            if (!useOffTakes || spatial) return;
            string fam = live != null && !live.standIn ? live.family : (parkedFamily == wantFamily ? wantFamily : null);
            bool wantInside = ear == Ear.Cockpit && fam != null && fam == wantFamily;
            if (wantInside)
            {
                EngineVoiceLibrary.Request(fam, Set.Interior);
                if (inner != null && inner.family != fam) Teardown(ref inner);
                if (inner == null && EngineVoiceLibrary.State(fam, Set.Interior) == Avail.Ready)
                {
                    inner = Build(fam, Set.Interior, false);
                    if (inner != null) inner.gain = inner.target = 1f;
                }
            }
            float target = wantInside && inner != null ? 1f : 0f;
            // Leaving the seat: hold the cabin until the exterior voice is back.
            if (!wantInside && inner != null && live == null) target = 1f;
            insideMix = Mathf.MoveTowards(insideMix, target, dt / ViewFadeSeconds);
            if (insideMix <= 0f && inner != null && !wantInside) Teardown(ref inner);
        }

        /// <summary>0 = the exterior set, 1 = the cockpit set (tests).</summary>
        public float InsideMix => insideMix;
        /// <summary>The family of the cockpit set, or null (tests).</summary>
        public string CabinFamily => inner != null ? inner.family : null;

        // ------------------------------------------------------------------
        //  The mix
        // ------------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateVoice(dt);
            UpdateSpatial(dt);
            UpdateInterior(dt);
            TryStartup();
            var main = live ?? inner;
            if (car == null || main == null) return;

            UpdateAudibleRPM(dt);
            float lim = LimiterRPM;
            float cv = audibleRPM / lim;              // the vendor's clipsValue

            // ---- gas pedal -> engineLoad. The vendor lerps toward the pedal at
            // gasPedalSimSpeed x 0.02 per FixedUpdate (~70 ms); its controller
            // integrations release the pedal for 0.2 s on every gear change.
            if (car.currentGear != lastGear) { lastGear = car.currentGear; shiftLiftTimer = ShiftLift; }
            shiftLiftTimer = Mathf.Max(0f, shiftLiftTimer - dt);
            float pedal = shiftLiftTimer > 0f ? 0f : Mathf.Clamp01(car.throttleInput);
            float gasF = Mathf.Clamp(0.02f * main.rig.gasPedalSimSpeed, 0.001f, 1f);
            load = Mathf.Lerp(load, pedal, 1f - Mathf.Pow(1f - gasF, dt / 0.02f));

            // ---- v2.3 Engine Load: adsrVol from the last workload, then update
            float sustain = main.rig.sustainAdsrVol;
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
            float outside = 1f - insideMix;
            if (live != null && outside > 0f) Mix(live, cv, master * outside, rate);
            if (fading != null && outside > 0f) Mix(fading, cv, master * outside, rate);
            if (inner != null && insideMix > 0f) Mix(inner, cv, master * insideMix, rate);
            else if (inner != null) Silence(inner);
            if (live != null && outside <= 0f) Silence(live);
            UpdateCrackle(main, cv, master, shiftLiftTimer > 0f);
        }

        static void Silence(VoiceSet s)
        {
            foreach (var src in s.all) Apply(src, 0f, 1f);
        }

        /// <summary>One voice set through the vendor's arithmetic, at its fade gain.</summary>
        void Mix(VoiceSet s, float cv, float master, float rate)
        {
            var rig = s.rig;
            master *= s.gain * rig.level;
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
                float mix = (s.set == Set.Interior ? 1f : IntakeMixing()) * rig.intakeMaster * onL * master;
                float ip = Mathf.Clamp(rig.intakePitch.Evaluate(cv) * pitchMul, PitchMin, PitchMax);
                Apply(s.intakeOn, rig.intakeOnVol.Evaluate(cv) * load * mix, ip);
                Apply(s.intakeOff, rig.intakeOffVol.Evaluate(cv) * (1f - load) * mix, ip);
            }

            // Aggressiveness, the vendor's DynamicSound with its "aggressive"
            // flag: the layer's volume curve x adsr x aggressivnessMaster x the
            // load curve x the pedal (on take) or its release (off take), pitched
            // by the aggressiveness pitch curve, and divided by
            // revLimiterAggressTweaker while the limiter take is up (the same
            // test the takes use: maxRPM >= 0.95 x master x onLoad). Heard from
            // every side, unlike the intake.
            if (s.aggOn != null || s.aggOff != null)
            {
                float da = mx > 0f && mx >= 0.95f * onL ? rig.aggTweak : 1f;
                float av = rig.aggVol.Evaluate(cv) * master * rig.aggMaster / da;
                float ap = Mathf.Clamp(rig.intakePitch.Evaluate(cv) * pitchMul, PitchMin, PitchMax);
                Apply(s.aggOn, av * onL * load, ap);
                Apply(s.aggOff, av * offL * (1f - load), ap);
            }
        }

        // ------------------------------------------------------------------
        //  Muffler crackle (phase 2, item 3)
        // ------------------------------------------------------------------

        /// <summary>
        /// The vendor's MufflerCrackleRandom (its DynamicSoundController half
        /// decompiled), as its Random_ShortMufflerCrackle prefab sets it up:
        /// lift off the gas from any rpm where the crackle curve tops 0.09 and,
        /// after a short random delay, one pop from the pool; nothing more until
        /// the pedal has been pressed and released again; at the limiter, pop
        /// after pop. The prefab re-picks a 0.1-0.5 s delay every frame the
        /// lift is armed and the EARLIEST wins, so in practice it fires 0.1-0.2 s
        /// after the lift; that emergent timing is kept. Its 0.5 s "play time"
        /// pauses the source mid-clip (a click on 0.64 s clips); here it only
        /// disarms, and the pop rings out.
        /// Player only (the vendor's add-on sits on one HQ engine), and only on
        /// the cars that would pop - see <see cref="CrackleLevelFor"/>.
        /// </summary>
        AudioSource crackleSrc;
        int crackleCtl;                       // vendor oneShotController: 0 idle, 1 gas held, 2 armed
        float crackleFireAt = -1f, crackleDisarmAt = -1f;
        float crackleLevel;
        const float CracklePlayTime = 0.5f, CrackleDelayMin = 0.1f, CrackleDelayMax = 0.5f;
        /// <summary>Gas below this is a released pedal (the vendor's bool).</summary>
        const float CrackleGasPressed = 0.1f;
        /// <summary>The prefab's crackleVolCurve, over clipsValue.</summary>
        static readonly AnimationCurve CrackleVol = new AnimationCurve(
            new Keyframe(0f, 0f, 1.1173617f, 1.1173617f),
            new Keyframe(0.8970616f, 1.0023422f, 0f, 0f),
            new Keyframe(1f, 1f, -0.022753645f, -0.022753645f));

        /// <summary>
        /// How hard a car pops on a lift, 0..1 (the crackle prefab's master).
        /// Not every car does: a stock economy car's exhaust stays quiet on the
        /// overrun. Rotaries pop (the FD reference the owner gave: lift-off
        /// pops and twin-turbo whoosh), turbo cars and big-power cars pop, the
        /// rest do not. OWNER CALL - the rule is here in one place.
        /// </summary>
        public static float CrackleLevelFor(CarSpec spec, string family)
        {
            if (family != null && family.StartsWith("rotary")) return 1f;
            if (spec == null) return 0f;
            if (spec.IsTurbo) return 0.7f;
            if (spec.hp >= 250) return 0.5f;
            return 0f;
        }

        /// <summary>The crackle level for this car, overriding the rule.</summary>
        public void SetCrackle(float level) => crackleLevel = Mathf.Clamp01(level);
        public float CrackleLevel => crackleLevel;
        /// <summary>Pops fired so far (tests).</summary>
        public int CracklesFired { get; private set; }

        void UpdateCrackle(VoiceSet s, float cv, float onLMaster, bool shifting)
        {
            if (!useOffTakes || crackleLevel <= 0f || s == null) return;
            if (crackleSrc == null)
            {
                var go = new GameObject("snd_crackle");
                go.transform.SetParent(transform, false);
                crackleSrc = go.AddComponent<AudioSource>();
                crackleSrc.playOnAwake = false;
                crackleSrc.loop = false;
                ConfigureSpatial(crackleSrc);
            }
            var pool = EngineVoiceLibrary.Crackles(insideMix >= 0.5f);
            if (pool.Length == 0) return;

            float now = Time.time;
            bool pressing = !shifting && car.throttleInput > CrackleGasPressed;
            float vol = CrackleVol.Evaluate(cv) * crackleLevel;
            bool playing = crackleSrc.isPlaying;

            // Lift-off: armed, audible, nothing playing - keep the earliest of
            // the prefab's per-frame random delays.
            if (!pressing && vol > 0.09f && crackleCtl == 2 && crackleDisarmAt < 0f && !playing)
            {
                float at = now + Random.Range(CrackleDelayMin, CrackleDelayMax);
                crackleFireAt = crackleFireAt < 0f ? at : Mathf.Min(crackleFireAt, at);
            }
            if (crackleFireAt >= 0f && now >= crackleFireAt)
            {
                crackleFireAt = -1f;
                if (crackleCtl != 0 && !playing) Pop(pool, vol);
                crackleDisarmAt = now + CracklePlayTime;
            }
            if (crackleDisarmAt >= 0f && now >= crackleDisarmAt) { crackleCtl = 0; crackleDisarmAt = -1f; }

            if (pressing) { if (crackleCtl != 2) crackleCtl = 1; }
            else if (crackleCtl == 1) crackleCtl = 2;

            // The limiter: pop after pop.
            if (s.rig.maxRPMVol.Evaluate(cv) > 0.9f && !crackleSrc.isPlaying)
            {
                crackleCtl = 2;
                Pop(pool, vol);
            }
        }

        void Pop(AudioClip[] pool, float vol)
        {
            var clip = pool[Random.Range(0, pool.Length)];
            // A pop that is not decoded yet is dropped: late, it has no cause.
            if (clip == null || clip.loadState != AudioDataLoadState.Loaded) return;
            crackleSrc.clip = clip;          // a one-shot source, never reassigned while playing
            crackleSrc.pitch = 1f;
            crackleSrc.volume = vol * masterVolume * voiceLevelMul * AudioPrefs.Engine;
            crackleSrc.Play();
            CracklesFired++;
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
                float adj = rate * Mathf.Lerp(1f, 2f, Mathf.InverseLerp(0f, top, kmh)) * (live ?? inner).rig.effortSensitivity;
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
