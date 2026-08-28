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
        /// project's built-in car is an RX-7 FD, so its own voice is the least
        /// surprising stand-in.</summary>
        public const string DefaultFamily = "rotary_x7";

        const string Root = "Engines/";

        /// <summary>
        /// One band of the ladder: where it sits on the rev range and the takes
        /// that cover it. <c>off</c> is null for a single-take band.
        ///
        /// The fractions are DESIGN positions, not measured ones — EngineAudio
        /// converts them to home RPMs geometrically against each car's own idle
        /// and limiter, which is what lets one ladder serve a 6000 rpm pushrod
        /// V8 and a 9000 rpm four.
        /// </summary>
        public struct BandDef
        {
            public string name;
            public float frac;
            public string onClip;
            public string offClip;

            public BandDef(string name, float frac, string onClip, string offClip)
            {
                this.name = name; this.frac = frac;
                this.onClip = onClip; this.offClip = offClip;
            }
        }

        /// <summary>
        /// The player's ladder: eight rungs, each with an on- and off-throttle
        /// take. Spacing is the pack's own band layout.
        /// </summary>
        public static readonly BandDef[] PlayerBands =
        {
            new BandDef("idle",      0.00f, "idle",          "idle"),
            new BandDef("idle_low",  0.10f, "idle_low_on",   "idle_low_off"),
            new BandDef("low",       0.22f, "low_on",        "low_off"),
            new BandDef("low_med",   0.35f, "low_med_on",    "low_med_off"),
            new BandDef("med",       0.48f, "med_on",        "med_off"),
            new BandDef("med_high",  0.62f, "med_high_on",   "med_high_off"),
            new BandDef("high",      0.75f, "high_on",       "high_off"),
            new BandDef("very_high", 0.88f, "very_high_on",  "very_high_off"),
        };

        /// <summary>
        /// Opponents run a 5-rung on-throttle-only ladder. The spacing is still
        /// geometric (each home RPM ~1.58x the last), which keeps every band's
        /// playback rate inside the 0.66-1.50 clamp while it is audible. Five
        /// rungs rather than eight because every resident band is a live
        /// AudioSource, and past the mixer's real-voice budget Unity virtualizes
        /// the quiet ones — stopping and restarting loops, which is exactly the
        /// artifact the whole design exists to avoid.
        /// </summary>
        public static readonly BandDef[] OpponentBands =
        {
            new BandDef("idle",      0.00f, "idle",         null),
            new BandDef("low",       0.21f, "low_on",       null),
            new BandDef("med",       0.42f, "med_on",       null),
            new BandDef("high",      0.63f, "high_on",      null),
            new BandDef("very_high", 0.84f, "very_high_on", null),
        };

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
            if (spec != null && !string.IsNullOrEmpty(spec.engineFamily) &&
                Has(spec.engineFamily))
                return spec.engineFamily;
            return DefaultFamily;
        }
    }
}
