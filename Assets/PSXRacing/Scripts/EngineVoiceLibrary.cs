using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The recorded engine voices, one folder per family under
    /// Resources/Engines/. A family is a complete set of RPM-band loops taken
    /// from one real engine; which family a car speaks through is baked into
    /// <see cref="CarSpec.engineFamily"/> by RG2's resolver, so a 660 cc kei
    /// car, a 13B-REW and a race V8 all arrive here as a folder name.
    ///
    /// Loaded lazily and cached: a race touches at most five families (player
    /// plus four opponents), and pulling all 28 into memory on boot would cost
    /// a few hundred megabytes of decompressed PCM for no reason. Note that
    /// lazy LOADING does not mean lazy DOWNLOADING — everything under Resources
    /// is packed into the WebGL data file regardless — so the size lever is the
    /// import quality and the clip list, not this cache.
    /// </summary>
    public static class EngineVoiceLibrary
    {
        /// <summary>Family used when a car has no recording of its own. The
        /// project's built-in car is an RX-7 FD, so the rotary the RX-7s now
        /// speak through is the least surprising stand-in.</summary>
        public const string DefaultFamily = "rotary_x8_free";

        const string Root = "Engines/";

        /// <summary>
        /// Catalog family keys that are played through a DIFFERENT recording.
        /// The catalog's engineFamily comes from RG2's resolver and is re-baked
        /// from there, so a voice decision made here has to survive a re-bake —
        /// hence a table rather than an edit to rg2_cars.json.
        ///
        /// rotary_x7 -> rotary_x8_free (all 13 RX-7s, FC and FD): the owner's
        /// reference for a correct rotary is the vendor's own Rotary_x8_Free
        /// rig. Measured through the vendor rig on the same FD profile
        /// (tools/rotary_family_pick.mjs), NO other pack on disk comes close to
        /// it: rotary_x7 sits 5.4 dB from it in octave balance and 3.1 st off in
        /// steady-rpm spectra (r 0.74); x8_2 r 0.80 but 6.5 dB; x8_1 6.6 dB /
        /// 6.3 st; 4-rotor 10.3 dB.
        /// </summary>
        static readonly Dictionary<string, string> Alias = new Dictionary<string, string>
        {
            { "rotary_x7", "rotary_x8_free" },
        };

        public static string Canonical(string family) =>
            family != null && Alias.TryGetValue(family, out var to) ? to : family;

        // ------------------------------------------------------------------
        //  The vendor rig, baked
        // ------------------------------------------------------------------
        /// <summary>
        /// One take of the vendor rig: its clip names and the vendor's own
        /// volume and pitch curves over clipsValue = rpm / limiter. These curves
        /// ARE the tuning — they say at which rpm each recording sits and how far
        /// it may stretch — and they are read out of the vendor's HQ/MQ prefabs
        /// by tools/res2_curves.mjs into Resources/res2_rig.json.
        /// </summary>
        public sealed class Take
        {
            public string name, onClip, offClip;
            public AnimationCurve vol, pitch;
        }

        /// <summary>A family's whole rig: the HQ ladder for the player, the
        /// Medium Quality ladder for opponents, and the shared load curves.</summary>
        public sealed class Rig
        {
            public string family;
            public float pitchMultiplier = 1f, revLimiterTweaker = 3f, gasPedalSimSpeed = 12f;
            public float intakeMaster, effortSensitivity = 1f, sustainAdsrVol = 0.4f;
            public Take[] hq, mq;
            public AnimationCurve onLoad, offLoad, mqOnLoad, mqOffLoad;
            public AnimationCurve maxRPMVol, intakeOnVol, intakeOffVol, intakePitch;
        }

        [System.Serializable] class CurveJ { public string name; public float[] k; }
        [System.Serializable] class FamilyJ
        {
            public string family;
            public float pitchMultiplier = 1f, revLimiterTweaker = 3f, gasPedalSimSpeed = 12f;
            public float intakeMaster, effortSensitivity = 1f, sustainAdsrVol = 0.4f;
            public CurveJ[] curves, mq;
        }
        [System.Serializable] class TableJ { public FamilyJ[] families; }

        static Dictionary<string, Rig> rigs;

        /// <summary>The vendor rig for a family, or null if it was not baked.</summary>
        public static Rig RigFor(string family)
        {
            if (rigs == null) LoadRigs();
            return family != null && rigs.TryGetValue(family, out var r) ? r : null;
        }

        static void LoadRigs()
        {
            rigs = new Dictionary<string, Rig>();
            var ta = Resources.Load<TextAsset>("res2_rig");
            if (ta == null) { Debug.LogWarning("EngineVoiceLibrary: Resources/res2_rig.json missing - run tools/res2_curves.mjs --bake"); return; }
            var t = JsonUtility.FromJson<TableJ>(ta.text);
            if (t?.families == null) return;
            foreach (var f in t.families)
            {
                AnimationCurve C(CurveJ[] set, string n)
                {
                    if (set != null) foreach (var c in set) if (c.name == n) return Curve(c.k);
                    return new AnimationCurve();
                }
                Take T(CurveJ[] set, string n, bool single) => new Take
                {
                    name = n,
                    onClip = single ? n : n + "_on",
                    offClip = single ? null : n + "_off",
                    vol = C(set, n + "VolCurve"),
                    pitch = C(set, n + "PitchCurve"),
                };
                rigs[f.family] = new Rig
                {
                    family = f.family,
                    pitchMultiplier = f.pitchMultiplier, revLimiterTweaker = f.revLimiterTweaker,
                    gasPedalSimSpeed = f.gasPedalSimSpeed, intakeMaster = f.intakeMaster,
                    effortSensitivity = f.effortSensitivity, sustainAdsrVol = f.sustainAdsrVol,
                    hq = new[]
                    {
                        T(f.curves, "idle", true), T(f.curves, "idle_low", false), T(f.curves, "low", false),
                        T(f.curves, "low_med", false), T(f.curves, "med", false), T(f.curves, "med_high", false),
                        T(f.curves, "high", false), T(f.curves, "very_high", false),
                    },
                    mq = new[]
                    {
                        T(f.mq, "idle", true), T(f.mq, "low", false), T(f.mq, "med", false), T(f.mq, "very_high", false),
                    },
                    onLoad = C(f.curves, "onLoadVolumeByRPM"), offLoad = C(f.curves, "offLoadVolumeByRPM"),
                    mqOnLoad = C(f.mq, "onLoadVolumeByRPM"), mqOffLoad = C(f.mq, "offLoadVolumeByRPM"),
                    maxRPMVol = C(f.curves, "maxRPMVolCurve"),
                    intakeOnVol = C(f.curves, "intakeOnVolCurve"), intakeOffVol = C(f.curves, "intakeOffVolCurve"),
                    // The vendor pitches the intake takes with its AGGRESSIVENESS
                    // pitch curve; there is no intake pitch curve of its own.
                    intakePitch = C(f.curves, "aggressivnessPitchCurve"),
                };
            }
        }

        /// <summary>Flat [time, value, inSlope, outSlope]* keys -> a Unity curve,
        /// so it is evaluated by the same Hermite code the vendor's prefab is.
        /// The bake writes a stepped (infinite) tangent as +-1e9.</summary>
        static AnimationCurve Curve(float[] k)
        {
            if (k == null || k.Length < 4) return new AnimationCurve();
            var keys = new Keyframe[k.Length / 4];
            for (int i = 0; i < keys.Length; i++)
            {
                float a = k[i * 4 + 2], b = k[i * 4 + 3];
                keys[i] = new Keyframe(k[i * 4], k[i * 4 + 1],
                    Mathf.Abs(a) > 1e8f ? float.PositiveInfinity * Mathf.Sign(a) : a,
                    Mathf.Abs(b) > 1e8f ? float.PositiveInfinity * Mathf.Sign(b) : b);
            }
            var c = new AnimationCurve(keys);
            c.preWrapMode = WrapMode.ClampForever;
            c.postWrapMode = WrapMode.ClampForever;
            return c;
        }

        static readonly Dictionary<string, AudioClip> cache =
            new Dictionary<string, AudioClip>();
        static readonly HashSet<string> knownMissing = new HashSet<string>();

        /// <summary>
        /// Load one take. Returns null when the family was not imported, which
        /// every caller treats as "skip this layer" rather than as an error —
        /// a family missing its intake takes should still drive.
        /// </summary>
        public static AudioClip Clip(string family, string clip)
        {
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(clip)) return null;
            string path = Root + family + "/" + clip;
            if (cache.TryGetValue(path, out var hit)) return hit;

            var loaded = Resources.Load<AudioClip>(path);
            // The engine clips import with preloadAudioData off — 560 clips of
            // decompressed PCM on scene load would be hundreds of megabytes in a
            // browser tab. Resources.Load therefore hands back an asset with no
            // sample data, and an AudioSource pointed at one plays silence.
            // Pulling it here (blocking, loadInBackground is off on these) means
            // a family is ready the moment it is selected, and only the four or
            // five families a race actually uses ever get decompressed.
            if (loaded != null && loaded.loadState != AudioDataLoadState.Loaded)
                loaded.LoadAudioData();
            cache[path] = loaded;
            if (loaded == null && knownMissing.Add(family))
                Debug.LogWarning("EngineVoiceLibrary: no clips for family '" + family +
                                 "' (looked under Resources/" + Root + family + "). " +
                                 "Re-run the engine pack importer.");
            return loaded;
        }

        /// <summary>Does this family have at least its idle take? Cheap enough to
        /// call per car; the result is cached by <see cref="Clip"/>.</summary>
        public static bool Has(string family) => Clip(family, "idle") != null;

        /// <summary>
        /// The family a spec should speak through, after falling back. Kept in
        /// one place so the builder, the race applier and any future audio probe
        /// cannot disagree about what a given car sounds like.
        /// </summary>
        public static string Resolve(CarSpec spec)
        {
            string fam = spec != null ? Canonical(spec.engineFamily) : null;
            if (!string.IsNullOrEmpty(fam) && Has(fam)) return fam;
            return DefaultFamily;
        }
    }
}
