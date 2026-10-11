using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PSXRacing
{
    /// <summary>
    /// TEXTURES: ENHANCED / CLASSIC (owner, 2026-10-11: "overhauled textures
    /// that should be used at highest graphical setting. the current/lower
    /// resolution textures can be used at lower visual settings").
    ///
    /// ENHANCED is the owner's 2x art; CLASSIC is the untouched originals.
    /// Defaults: ENHANCED on a desktop, CLASSIC on a phone (the 2x set is 4x
    /// the texels, and a phone browser is where memory runs out).
    /// </summary>
    public static class TexturePrefs
    {
        const string PrefKey = "psx.texEnhanced";
        static int cached = -2;   // -1 = never chosen: follow the device

        public static bool Enhanced
        {
            get
            {
                if (cached == -2) cached = PlayerPrefs.GetInt(PrefKey, -1);
                return cached < 0 ? !Application.isMobilePlatform : cached != 0;
            }
            set
            {
                if (Enhanced == value && cached >= 0) return;
                cached = value ? 1 : 0;
                PlayerPrefs.SetInt(PrefKey, cached);
                PlayerPrefs.Save();
                Changed++;
                // Picked again after a failed download: try once more.
                if (value) TextureTier.RetryDownload();
                TextureTier.ApplyAll();
            }
        }

        public static int Changed { get; private set; }
        public static void Toggle() => Enhanced = !Enhanced;
        public static string Label => Enhanced ? "ENHANCED" : "CLASSIC";
    }

    /// <summary>
    /// The switch itself: every texture slot of a material that holds a mapped
    /// original gets the Enhanced twin, or back. It works on the SHARED
    /// material assets, so every renderer that wears one follows, later
    /// spawns included, and runtime copies made from them (the skyline's, the
    /// shadow casters') are swept with everything else in memory.
    ///
    /// It rides on <see cref="PSXTexDecode.Stamp"/>, which already runs on
    /// every material the game shows before it is drawn - scene loads, the
    /// Resources prefabs, CityKit, the season's dresses, the car dressers -
    /// and must run AFTER a swap anyway, because the 16-bit decode flag is
    /// read off the texture the slot holds.
    ///
    /// WHERE THE TWINS COME FROM (2026-10-11). Not the player: they took
    /// MAIN's WebGL.data to 106.6 MB and GitHub refuses 100. They ship as ONE
    /// AssetBundle, StreamingAssets/<see cref="BundleFile"/>, beside the
    /// build, and a player fetches it only when ENHANCED is on - at boot if it
    /// is set, or when it is picked. Until it arrives every material stays
    /// CLASSIC; when it lands, everything in memory is swapped at once. A
    /// failed fetch (offline, 404) stays CLASSIC and logs once; picking
    /// ENHANCED again tries again. CLASSIC puts the originals back and
    /// unloads the bundle with every twin it gave (a phone that never picks
    /// ENHANCED never downloads a byte of it).
    ///
    /// The editor reads the twins straight from Assets/PSXRacing/EnhancedSrc,
    /// so editor play needs no bundle build; PSX_TEX_BUNDLE=1 (or
    /// <see cref="ForceBundle"/>) makes it use the player's path instead.
    ///
    /// The editor never keeps a swap: play mode puts the originals back on
    /// exit, and the .mat save guard puts them back before any save. In edit
    /// mode the batch screenshot tools can ask for ENHANCED with
    /// PSX_TEX=enhanced (that process only).
    /// </summary>
    public static class TextureTier
    {
        /// <summary>The bundle's file name under StreamingAssets.</summary>
        public const string BundleFile = "texenhanced.bundle";
#if UNITY_EDITOR
        /// <summary>Where the twins live in the project (the key is the path
        /// under here, without the .png).</summary>
        public const string EditorSrcRoot = "Assets/PSXRacing/EnhancedSrc/";
#endif

        static TextureTierMap map;
        static bool mapTried;
        static readonly Dictionary<Texture, int> byOriginal = new Dictionary<Texture, int>();
        static readonly Dictionary<Texture, int> byEnhanced = new Dictionary<Texture, int>();
        static Texture2D[] loaded;
        static int loadedCount;
        static readonly List<int> ids = new List<int>();

        enum Fetch { None, Loading, Ready, Failed }
        static Fetch fetch;
        static AssetBundle bundle;
        static bool failLogged;
        static int fetchGen;          // a reset while a fetch is in flight orphans it

        /// <summary>The tier materials should show right now.</summary>
        public static bool WantEnhanced
        {
            get
            {
#if UNITY_EDITOR
                if (ForceClassic) return false;
                if (!Application.isPlaying)
                    return System.Environment.GetEnvironmentVariable("PSX_TEX") == "enhanced";
#endif
                return TexturePrefs.Enhanced;
            }
        }

#if UNITY_EDITOR
        /// <summary>Set by the editor while it restores the originals.</summary>
        public static bool ForceClassic;

        /// <summary>Editor play: fetch the bundle from StreamingAssets the way
        /// a player does, instead of reading EnhancedSrc.</summary>
        public static bool ForceBundle;

        /// <summary>Editor tests: fetch from here instead (a missing file
        /// is the offline / 404 case).</summary>
        public static string UrlOverride;

        static bool UseBundle => Application.isPlaying &&
            (ForceBundle || System.Environment.GetEnvironmentVariable("PSX_TEX_BUNDLE") == "1");
#else
        static bool UseBundle => true;
#endif

        /// <summary>A player set to ENHANCED starts the download before the
        /// first scene, so it is usually there before the first race.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (fetch == Fetch.Failed) fetch = Fetch.None;   // editor: play mode without a domain reload
            failLogged = false;
            if (UseBundle && WantEnhanced && EnsureMap()) RequestBundle();
        }

        static bool EnsureMap()
        {
            if (map != null) return true;
            if (mapTried) return false;
            mapTried = true;
            map = Resources.Load<TextureTierMap>(TextureTierMap.ResourcePath);
            if (map == null || map.pairs == null) { map = null; return false; }
            loaded = new Texture2D[map.pairs.Length];
            for (int i = 0; i < map.pairs.Length; i++)
                if (map.pairs[i].original != null) byOriginal[map.pairs[i].original] = i;
            return true;
        }

        /// <summary>The Enhanced twin of pair i, loaded on first use; null
        /// (keep the original) when it is missing or still downloading.</summary>
        static Texture2D Twin(int i)
        {
            if (loaded[i] != null) return loaded[i];
            string key = map.pairs[i].enhanced;
            Texture2D t = null;
            if (UseBundle)
            {
                if (fetch != Fetch.Ready) { RequestBundle(); return null; }
                t = bundle.LoadAsset<Texture2D>(key);
            }
#if UNITY_EDITOR
            else t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(EditorSrcRoot + key + ".png");
#endif
            if (t == null) return null;
            loaded[i] = t;
            loadedCount++;
            byEnhanced[t] = i;
            return t;
        }

        /// <summary>Put the wanted tier's textures in this material's slots.
        /// True when a slot changed. Cheap when nothing is Enhanced: CLASSIC
        /// with no twin ever loaded never even loads the map.</summary>
        public static bool Swap(Material m)
        {
            if (m == null) return false;
            bool want = WantEnhanced;
            if (!want && loadedCount == 0) return false;
            if (!EnsureMap()) return false;
            ids.Clear();
            m.GetTexturePropertyNameIDs(ids);
            bool changed = false;
            for (int k = 0; k < ids.Count; k++)
            {
                var t = m.GetTexture(ids[k]);
                if (t == null) continue;
                Texture put = null;
                if (want)
                {
                    if (byOriginal.TryGetValue(t, out int i)) put = Twin(i);
                }
                else if (byEnhanced.TryGetValue(t, out int j)) put = map.pairs[j].original;
                if (put == null || put == t) continue;
                Set(m, ids[k], put);
                changed = true;
            }
            return changed;
        }

        static void Set(Material m, int id, Texture t)
        {
#if UNITY_EDITOR
            // A material ASSET swapped in the editor must not become a saved
            // value (the same rule PSXTexDecode keeps for its flag).
            if (UnityEditor.EditorUtility.IsPersistent(m))
            {
                bool wasDirty = UnityEditor.EditorUtility.IsDirty(m);
                m.SetTexture(id, t);
                if (!wasDirty) UnityEditor.EditorUtility.ClearDirty(m);
                return;
            }
#endif
            m.SetTexture(id, t);
        }

        /// <summary>Every material in memory to the wanted tier (and its
        /// decode flag after it); on CLASSIC the twins are then released, so
        /// switching back really gives the memory back.</summary>
        public static void ApplyAll()
        {
            PSXTexDecode.StampAll();
            if (!WantEnhanced) Release();
        }

        // ---- the bundle --------------------------------------------------

        /// <summary>The bundle's URL: StreamingAssets beside the page (an
        /// http URL in a browser, a file path in the editor), with the
        /// build's content hash as ?v= over http so a cached older bundle is
        /// never reused.</summary>
        public static string BundleUrl
        {
            get
            {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(UrlOverride)) return UrlOverride;
#endif
                string url = Application.streamingAssetsPath + "/" + BundleFile;
                if (url.Contains("://"))
                {
                    string v = map != null ? map.bundleVersion : null;
                    if (url.StartsWith("http") && !string.IsNullOrEmpty(v)) url += "?v=" + v;
                    return url;
                }
                return "file:///" + url.TrimStart('/');
            }
        }

        static void RequestBundle()
        {
            if (fetch != Fetch.None) return;
            fetch = Fetch.Loading;
            int gen = ++fetchGen;
            string url = BundleUrl;
            float t0 = Time.realtimeSinceStartup;
            UnityWebRequest req;
            try
            {
                req = UnityWebRequestAssetBundle.GetAssetBundle(url);
                req.SendWebRequest().completed += _ => OnBundle(req, url, gen, t0);
            }
            catch (System.Exception e)
            {
                Failed(url, e.Message);
                return;
            }
            Debug.Log("[TexTier] fetching ENHANCED textures: " + url);
        }

        static void OnBundle(UnityWebRequest req, string url, int gen, float t0)
        {
            AssetBundle b = null;
            string err = null;
            ulong bytes = req.downloadedBytes;
            try
            {
                if (req.result == UnityWebRequest.Result.Success) b = DownloadHandlerAssetBundle.GetContent(req);
                else err = req.error;
            }
            catch (System.Exception e) { err = e.Message; }
            finally { req.Dispose(); }

            if (gen != fetchGen)
            {
                // Reset (CLASSIC, or the editor left play) while in flight.
                if (b != null) b.Unload(true);
                return;
            }
            if (b == null) { Failed(url, err ?? "not an AssetBundle"); return; }
            bundle = b;
            fetch = Fetch.Ready;
            Debug.Log($"[TexTier] ENHANCED textures ready: {bytes / 1048576f:F1} MB in {Time.realtimeSinceStartup - t0:F1} s");
            if (WantEnhanced) ApplyAll();
            else Release();
        }

        static void Failed(string url, string why)
        {
            fetch = Fetch.Failed;
            if (failLogged) return;
            failLogged = true;
            Debug.LogWarning("[TexTier] ENHANCED textures unavailable (" + url + ": " + why + ") - staying CLASSIC");
        }

        /// <summary>ENHANCED picked again after a failed download: the next
        /// swap fetches once more (logged only the first time).</summary>
        public static void RetryDownload()
        {
            if (fetch == Fetch.Failed) fetch = Fetch.None;
        }

        /// <summary>Downloading, ready, failed or none (logs and tests).</summary>
        public static string BundleState => fetch.ToString();
        public static bool BundleLoaded => bundle != null;

        static void Release()
        {
            if (loaded != null && loadedCount > 0)
            {
                for (int i = 0; i < loaded.Length; i++)
                {
                    if (loaded[i] == null) continue;
                    // A bundle's textures go with the bundle, below.
                    if (bundle == null) Resources.UnloadAsset(loaded[i]);
                    loaded[i] = null;
                }
            }
            byEnhanced.Clear();
            loadedCount = 0;
            // The bundle and every twin it gave. The materials already hold
            // the originals again (ApplyAll swaps before it releases).
            if (bundle != null) { bundle.Unload(true); bundle = null; }
            if (fetch == Fetch.Ready) fetch = Fetch.None;
            else if (fetch == Fetch.Loading) { fetch = Fetch.None; fetchGen++; }
        }

        /// <summary>How many Enhanced twins are in memory (for logs/tests).</summary>
        public static int LoadedCount => loadedCount;

        /// <summary>The map (editor tools and the self-test).</summary>
        public static TextureTierMap Map => EnsureMap() ? map : null;

        /// <summary>Forget the map so a rebuilt one is read again.</summary>
        public static void ResetMap()
        {
            Release();
            map = null;
            mapTried = false;
            byOriginal.Clear();
        }
    }
}
