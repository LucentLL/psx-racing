using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PSXRacing
{
    /// <summary>
    /// The recorded engine voices, one family per real engine. A family is a
    /// complete set of rpm-band loops; which family a car speaks through is
    /// baked into <see cref="CarSpec.engineFamily"/> by RG2's resolver, so a
    /// 660 cc kei car, a 13B-REW and a race V8 all arrive here as a key.
    ///
    /// WHERE THE CLIPS LIVE (2026-10-16, engine audio phase 2). Until now every
    /// family sat in Resources/Engines and rode in WebGL.data whether a player
    /// ever heard it or not: ~23 MB of a data file that had reached 96.5 MB
    /// against GitHub's hard 100 MB per-file limit. Now:
    ///  - ONE family stays in Resources (<see cref="DefaultFamily"/>, the
    ///    RX-7s' rotary_x7), so a car always has a voice, offline included;
    ///  - every other family's exterior set is its own AssetBundle,
    ///    StreamingAssets/engines/&lt;family&gt;.bundle, and every family's
    ///    cockpit (interior) set is &lt;family&gt;_int.bundle. Both are built
    ///    with the player by EngineBundleBuilder (from Assets/PSXRacing/
    ///    EngineSrc, outside Resources) and versioned by content hash through
    ///    Resources/EngineBundles.txt (the URL's ?v=, the texenhanced/cockpit
    ///    pattern);
    ///  - a family is fetched before it is needed (<see cref="PrefetchHandoff"/>:
    ///    the player's car and the field, as the run is set up and again as
    ///    the scene boots), held while a car speaks through it
    ///    (<see cref="Acquire"/>/<see cref="Release"/>), and unloaded once
    ///    idle and crowded out.
    /// A family that is still downloading is <see cref="Avail.Loading"/>;
    /// EngineAudio waits on it (a beat of silence, then the default family
    /// quietly) and fades the real voice in when it lands. A failed fetch
    /// (offline, 404) logs once and the car keeps the default family.
    ///
    /// In the editor the clips load straight from EngineSrc (no bundle build
    /// needed for play mode); PSX_ENGINE_BUNDLE=1 or <see cref="ForceBundle"/>
    /// takes the player's path instead.
    /// </summary>
    public static class EngineVoiceLibrary
    {
        /// <summary>Family used when a car has no recording of its own, and
        /// the stand-in while a car's own family downloads. The project's
        /// built-in car is an RX-7 FD, so the rotary the RX-7s speak through is
        /// the least surprising stand-in. The only family in Resources.</summary>
        public const string DefaultFamily = "rotary_x7";

        const string Root = "Engines/";

        /// <summary>The two recordings of every family: from outside the car
        /// (chase views, other cars, replays) and from the driver's seat.</summary>
        public enum Set { Exterior = 0, Interior = 1 }

        /// <summary>Where a family's clips stand. None = never asked for.</summary>
        public enum Avail { None, Missing, Loading, Ready, Failed }

        /// <summary>StreamingAssets/&lt;this&gt;/&lt;pack&gt;.bundle.</summary>
        public const string BundleFolder = "engines";
        /// <summary>Resources TextAsset: one line per bundle, "name hash bytes",
        /// written by EngineBundleBuilder with the build.</summary>
        public const string ManifestPath = "EngineBundles";
#if UNITY_EDITOR
        /// <summary>Where the bundled families live in the project (outside
        /// Resources, so the main data never carries them): &lt;family&gt;/*.ogg
        /// and &lt;family&gt;/Interior/*.ogg.</summary>
        public const string EditorSrc = "Assets/PSXRacing/EngineSrc/";
#endif

        /// <summary>The bundle (pack) name of a family's set.</summary>
        public static string PackName(string family, Set set) =>
            set == Set.Interior ? family + "_int" : family;

        /// <summary>
        /// Catalog family keys that are played through a DIFFERENT recording.
        /// The catalog's engineFamily comes from RG2's resolver and is re-baked
        /// from there, so a voice decision made here has to survive a re-bake —
        /// hence a table rather than an edit to rg2_cars.json.
        ///
        /// Empty today. The RX-7s were briefly aliased rotary_x7 -> rotary_x8_free
        /// (closest to the owner's x8 reference: x7 sits 5.4 dB / 3.1 st from it),
        /// but once x7 played at its true pitch through the vendor rig the owner
        /// chose it (2026-10-16: "We can use the proper X7 sfx for RX7s") for its
        /// growlier idle. The old ladder had x7 4-11 st sharp, which is what
        /// made it sound wrong.
        /// </summary>
        static readonly Dictionary<string, string> Alias = new Dictionary<string, string>();

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

        // ------------------------------------------------------------------
        //  The bundles
        // ------------------------------------------------------------------

        /// <summary>One fetched set: a family's exterior or interior bundle.</summary>
        sealed class Pack
        {
            public string name;
            public Avail state;
            public AssetBundle bundle;
            public int refs;
            public float idleSince, requestedAt;
            public readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        }

        static readonly Dictionary<string, Pack> packs = new Dictionary<string, Pack>();
        static Dictionary<string, string> manifest;
        static readonly HashSet<string> failLogged = new HashSet<string>();
        /// <summary>Idle packs (no car speaking through them) kept loaded so a
        /// restart or the next race does not download them again; beyond this
        /// the longest-idle is unloaded. A race touches the player's family,
        /// up to seven opponents' and maybe the cockpit set.</summary>
        const int MaxIdlePacks = 6;

#if UNITY_EDITOR
        /// <summary>Editor play: fetch the bundles from StreamingAssets the way
        /// a player does, instead of reading EngineSrc.</summary>
        public static bool ForceBundle;
        /// <summary>Editor tests: fetch "&lt;this&gt;/&lt;pack&gt;.bundle" (a
        /// file:/// folder URL) instead of StreamingAssets/engines.</summary>
        public static string UrlOverride;
        /// <summary>Editor tests: this pack reports Loading for this long
        /// after it is asked for (a slow network, to exercise the stand-in).</summary>
        public static string DebugSlowPack;
        public static float DebugSlowSeconds;
        static bool UseBundle => Application.isPlaying &&
            (ForceBundle || System.Environment.GetEnvironmentVariable("PSX_ENGINE_BUNDLE") == "1");
#else
        static bool UseBundle => true;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Play mode without a domain reload: a fetch state must not
            // outlive its bundle.
            foreach (var p in packs.Values)
                if (p.bundle == null && p.state != Avail.Ready) p.state = Avail.None;
            failLogged.Clear();
        }

        /// <summary>Bundle name -> content hash, from Resources/EngineBundles.txt.</summary>
        static Dictionary<string, string> Manifest
        {
            get
            {
                if (manifest != null) return manifest;
                manifest = new Dictionary<string, string>();
                var ta = Resources.Load<TextAsset>(ManifestPath);
                if (ta == null) return manifest;
                foreach (var raw in ta.text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var f = line.Split(' ');
                    manifest[f[0]] = f.Length > 1 ? f[1] : "";
                }
                return manifest;
            }
        }

        /// <summary>Does this family's set exist at all (in Resources, in the
        /// build's bundle list, or in the editor's EngineSrc)?</summary>
        public static bool Exists(string family, Set set)
        {
            if (string.IsNullOrEmpty(family)) return false;
            if (set == Set.Exterior && family == DefaultFamily) return true;
            if (!UseBundle)
            {
#if UNITY_EDITOR
                return UnityEditor.AssetDatabase.IsValidFolder(
                    EditorSrc + family + (set == Set.Interior ? "/Interior" : ""));
#else
                return false;
#endif
            }
            return Manifest.ContainsKey(PackName(family, set));
        }

        /// <summary>Does this family have a recording? Cheap enough to call per
        /// car (no download, no load).</summary>
        public static bool Has(string family) => Exists(family, Set.Exterior);

        /// <summary>Where a family's set stands right now.</summary>
        public static Avail State(string family, Set set)
        {
            if (!Exists(family, set)) return Avail.Missing;
            if (set == Set.Exterior && family == DefaultFamily) return Avail.Ready;
            if (!UseBundle) return Avail.Ready;
            if (!packs.TryGetValue(PackName(family, set), out var p)) return Avail.None;
#if UNITY_EDITOR
            if (p.state == Avail.Ready && p.name == DebugSlowPack &&
                Time.realtimeSinceStartup < p.requestedAt + DebugSlowSeconds) return Avail.Loading;
#endif
            return p.state;
        }

        public static bool Ready(string family, Set set) => State(family, set) == Avail.Ready;

        /// <summary>The bundle URL: StreamingAssets beside the page, with the
        /// bundle's content hash as ?v= over http so a cached older bundle is
        /// never reused.</summary>
        public static string BundleUrl(string pack)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(UrlOverride)) return UrlOverride.TrimEnd('/') + "/" + pack + ".bundle";
#endif
            string url = Application.streamingAssetsPath + "/" + BundleFolder + "/" + pack + ".bundle";
            if (url.Contains("://"))
            {
                if (url.StartsWith("http") && Manifest.TryGetValue(pack, out var v) && !string.IsNullOrEmpty(v))
                    url += "?v=" + v;
                return url;
            }
            return "file:///" + url.TrimStart('/');
        }

        /// <summary>Start fetching a family's set if it is not here yet. Safe
        /// to call every frame, from anywhere, before anything is loaded; never
        /// throws. A failed fetch is not retried (it logs once).</summary>
        public static void Request(string family, Set set)
        {
            try
            {
                if (!UseBundle || !Exists(family, set)) return;
                if (set == Set.Exterior && family == DefaultFamily) return;
                string name = PackName(family, set);
                if (!packs.TryGetValue(name, out var p)) packs[name] = p = new Pack { name = name, idleSince = Time.realtimeSinceStartup };
                if (p.state != Avail.None) return;
                p.state = Avail.Loading;
                p.requestedAt = Time.realtimeSinceStartup;
                string url = BundleUrl(name);
                float t0 = Time.realtimeSinceStartup;
                var req = UnityWebRequestAssetBundle.GetAssetBundle(url);
                req.SendWebRequest().completed += _ => OnBundle(p, req, url, t0);
            }
            catch (System.Exception e)
            {
                Fail(PackName(family, set), "request: " + e.Message);
            }
        }

        static void OnBundle(Pack p, UnityWebRequest req, string url, float t0)
        {
            AssetBundle b = null;
            string err = null;
            ulong bytes = 0;
            try
            {
                bytes = req.downloadedBytes;
                if (req.result == UnityWebRequest.Result.Success) b = DownloadHandlerAssetBundle.GetContent(req);
                else err = req.error;
            }
            catch (System.Exception e) { err = e.Message; }
            finally { req.Dispose(); }

            if (p.state != Avail.Loading)
            {
                // Forgotten while in flight (play mode ended).
                if (b != null) b.Unload(true);
                return;
            }
            if (b == null) { Fail(p.name, url + ": " + (err ?? "not an AssetBundle")); return; }
            p.bundle = b;
            p.state = Avail.Ready;
            Debug.Log($"[Engines] {p.name} ready: {bytes / 1024f:F0} KB in {Time.realtimeSinceStartup - t0:F2} s");
            Trim();
        }

        static void Fail(string name, string why)
        {
            if (!packs.TryGetValue(name, out var p)) packs[name] = p = new Pack { name = name };
            p.state = Avail.Failed;
            if (failLogged.Add(name))
                Debug.LogWarning("[Engines] " + name + " is unavailable (" + why + ") - the car keeps the default voice");
        }

        /// <summary>A car speaks through this set: keep it loaded.</summary>
        public static void Acquire(string family, Set set)
        {
            if (packs.TryGetValue(PackName(family, set), out var p)) p.refs++;
        }

        /// <summary>The car no longer speaks through this set. It stays
        /// loaded while there is room (<see cref="MaxIdlePacks"/>).</summary>
        public static void Release(string family, Set set)
        {
            if (!packs.TryGetValue(PackName(family, set), out var p) || p.refs <= 0) return;
            if (--p.refs == 0) { p.idleSince = Time.realtimeSinceStartup; Trim(); }
        }

        /// <summary>Unload the longest-idle packs beyond the idle allowance.
        /// Only packs no car is using; their repaired loop copies go too.</summary>
        static void Trim()
        {
            try
            {
                var idle = new List<Pack>();
                foreach (var p in packs.Values)
                    if (p.state == Avail.Ready && p.refs == 0 && p.bundle != null) idle.Add(p);
                if (idle.Count <= MaxIdlePacks) return;
                idle.Sort((a, b) => a.idleSince.CompareTo(b.idleSince));
                for (int i = 0; i < idle.Count - MaxIdlePacks; i++)
                {
                    var p = idle[i];
                    foreach (var c in p.clips.Values) LoopSeam.Forget(c);
                    p.clips.Clear();
                    p.bundle.Unload(true);
                    p.bundle = null;
                    p.state = Avail.None;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Engines] trim: " + e.Message); }
        }

        /// <summary>Loaded sets no car is speaking through (tests: never above
        /// the idle allowance once the downloads settle).</summary>
        public static int IdleLoaded
        {
            get
            {
                int n = 0;
                foreach (var p in packs.Values) if (p.state == Avail.Ready && p.refs == 0 && p.bundle != null) n++;
                return n;
            }
        }
        public const int IdleAllowance = MaxIdlePacks;

        /// <summary>How many sets are loaded / in use (logs and tests).</summary>
        public static string Census()
        {
            int ready = 0, used = 0, loading = 0, failed = 0;
            foreach (var p in packs.Values)
            {
                if (p.state == Avail.Ready) { ready++; if (p.refs > 0) used++; }
                else if (p.state == Avail.Loading) loading++;
                else if (p.state == Avail.Failed) failed++;
            }
            return $"{ready} loaded ({used} in use), {loading} loading, {failed} failed";
        }

        // ------------------------------------------------------------------
        //  Prefetch
        // ------------------------------------------------------------------

        /// <summary>Fetch a family's exterior set ahead of need.</summary>
        public static void Prefetch(string family)
        {
            family = Canonical(family);
            if (!string.IsNullOrEmpty(family)) Request(family, Set.Exterior);
        }

        /// <summary>Fetch the family a catalog car speaks through.</summary>
        public static void PrefetchSpec(string specId)
        {
            try
            {
                var spec = string.IsNullOrEmpty(specId) ? null : CarCatalog.Get(specId.Trim());
                if (spec != null) Prefetch(Resolve(spec));
            }
            catch (System.Exception e) { Debug.LogWarning("[Engines] prefetch " + specId + ": " + e.Message); }
        }

        /// <summary>Fetch every family the run being set up will need: the
        /// player's car and the field (RaceHandoff). Called as the run is
        /// arranged (the car request, the field pick) and again as the scene
        /// boots (RaceHandoffApplier.Awake), so the bundles are usually in
        /// before the countdown's first engine fires.</summary>
        public static void PrefetchHandoff()
        {
            PrefetchSpec(RaceHandoff.CarSpecId);
            var ids = RaceHandoff.OpponentSpecIds;
            if (string.IsNullOrEmpty(ids)) return;
            foreach (var id in ids.Split(';')) if (id.Trim().Length > 0) PrefetchSpec(id);
        }

        // ------------------------------------------------------------------
        //  Clips
        // ------------------------------------------------------------------

        static readonly Dictionary<string, AudioClip> cache =
            new Dictionary<string, AudioClip>();
        static readonly HashSet<string> knownMissing = new HashSet<string>();

        /// <summary>
        /// Load one take. Null when the set is not here (yet) or the take does
        /// not exist, which every caller treats as "skip this layer" rather than
        /// as an error — a family missing its intake takes should still drive.
        /// Callers wait for <see cref="Ready"/> before building a voice.
        /// </summary>
        public static AudioClip Clip(string family, string clip, Set set = Set.Exterior)
        {
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(clip)) return null;
            AudioClip loaded;
            if (set == Set.Exterior && family == DefaultFamily)
            {
                string path = Root + family + "/" + clip;
                if (cache.TryGetValue(path, out var hit)) return hit;
                loaded = Resources.Load<AudioClip>(path);
                cache[path] = loaded;
            }
            else if (!UseBundle)
            {
#if UNITY_EDITOR
                string path = EditorSrc + family + (set == Set.Interior ? "/Interior/" : "/") + clip + ".ogg";
                if (cache.TryGetValue(path, out var hit)) return hit;
                loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                cache[path] = loaded;
#else
                return null;
#endif
            }
            else
            {
                if (!packs.TryGetValue(PackName(family, set), out var p) || p.state != Avail.Ready || p.bundle == null)
                    return null;
                if (p.clips.TryGetValue(clip, out var hit)) return hit;
                try { loaded = p.bundle.LoadAsset<AudioClip>(clip); }
                catch (System.Exception e) { Debug.LogWarning("[Engines] " + p.name + "/" + clip + ": " + e.Message); loaded = null; }
                p.clips[clip] = loaded;
            }

            // The engine clips import with preloadAudioData off — a family of
            // decompressed PCM on load would be megabytes per car nobody hears.
            // The load therefore hands back an asset with no sample data, and a
            // source pointed at one plays silence: ask for it here (on WebGL
            // the browser decodes asynchronously; AudioLoopStarter waits on it),
            // so only the takes a car actually plays are ever decoded.
            if (loaded != null && loaded.loadState == AudioDataLoadState.Unloaded)
                loaded.LoadAudioData();
            if (loaded == null && clip == "idle" && knownMissing.Add(family + "/" + set))
                Debug.LogWarning("EngineVoiceLibrary: no idle take for '" + family + "' (" + set + ")");
            return loaded;
        }

        /// <summary>
        /// The family a spec should speak through, after falling back. Kept in
        /// one place so the builder, the race applier and any future audio probe
        /// cannot disagree about what a given car sounds like. A family that
        /// exists but is still downloading resolves to ITSELF: EngineAudio
        /// stands in for it until it lands.
        /// </summary>
        public static string Resolve(CarSpec spec)
        {
            string fam = spec != null ? Canonical(spec.engineFamily) : null;
            if (!string.IsNullOrEmpty(fam) && Has(fam)) return fam;
            return DefaultFamily;
        }
    }
}
