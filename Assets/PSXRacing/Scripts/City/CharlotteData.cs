using UnityEngine;
using UnityEngine.Networking;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S BIG DATA AS ITS OWN DOWNLOAD (2026-10-24, the owner: "unless
    /// there is a way to load Charlotte map separately like the cockpit. Then
    /// we can just have one build again"). GitHub refuses a file over 100 MB
    /// and the game's WebGL.data is ~77 MB without Charlotte, so the city's
    /// five big files - the graph, the buildings, the ground, the signs and
    /// the canopy (<see cref="Bundled"/>, ~16 MB) - ship in
    /// StreamingAssets/<see cref="BundleFile"/>, fetched in the background
    /// once the game is up (<see cref="Prefetch"/>) and waited on, behind a
    /// LOADING CHARLOTTE screen, only if a Charlotte scene is asked for first
    /// (<see cref="SceneGate"/>). The browser caches it under its version.
    ///
    /// The files stay in Resources in the project: every tool, audit and
    /// editor play reads them there as before. The WebGL build packs them into
    /// the bundle (Editor/CharlotteBundle.cs), writes <see cref="VersionPath"/>
    /// and parks the five out of Resources for the one BuildPlayer call
    /// (EditionParking). A player with that version file reads the bundle; a
    /// player without it (a build that did not bundle them) reads Resources.
    /// </summary>
    public static class CharlotteData
    {
        public const string BundleFile = "charlotte.bundle";
        /// <summary>Resources path of the bundle's version, written by the
        /// build for its one BuildPlayer call: present = the big files are in
        /// the bundle, not in Resources.</summary>
        public const string VersionPath = "charlotte_bundle";
        /// <summary>The files the bundle carries, by their Resources name.</summary>
        public static readonly string[] Bundled = { "charlotte_city", "charlotte_bld", "charlotte_dem", "charlotte_signs", "charlotte_canopy" };

        enum Fetch { None, Loading, Ready, Failed }
        static Fetch fetch;
        static AssetBundle bundle;
        static UnityWebRequest req;
        static string version;
        static bool versionRead;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (bundle == null && fetch != Fetch.Ready) fetch = Fetch.None;
            versionRead = false;
        }

        /// <summary>The bundle's version when this player reads the big files
        /// from it, else null (the editor: always null).</summary>
        static string Version
        {
            get
            {
#if UNITY_EDITOR
                return null;
#else
                if (!versionRead)
                {
                    versionRead = true;
                    var v = Resources.Load<TextAsset>(VersionPath);
                    version = v != null && v.text.Trim().Length > 0 ? v.text.Trim() : null;
                }
                return version;
#endif
            }
        }

        /// <summary>This player reads the big files from the bundle.</summary>
        public static bool InBundle => Version != null;
        /// <summary>The big files can be read now (always, when they are not in a bundle).</summary>
        public static bool Ready => !InBundle || fetch == Fetch.Ready;
        /// <summary>The fetch failed: the files are read from Resources (absent
        /// in a bundled build - the city then reports its data missing).</summary>
        public static bool Failed => fetch == Fetch.Failed;
        /// <summary>Nothing more is coming: the data arrived, the fetch failed,
        /// or this player does not download it at all.</summary>
        public static bool Settled => !InBundle || fetch == Fetch.Ready || fetch == Fetch.Failed;

        /// <summary>Raised once, when the download settles (arrived or
        /// failed). TextureTier holds its own, bigger download until then
        /// (2026-10-28): a Charlotte scene can wait on this one behind LOADING
        /// CHARLOTTE, never on that one, so the two never share the line.</summary>
        public static event System.Action SettledNow;

        static void RaiseSettled()
        {
            var h = SettledNow;
            SettledNow = null;
            h?.Invoke();
        }

        /// <summary>0..1 of the download, for the loading screen.</summary>
        public static float Progress => Ready ? 1f : req != null ? req.downloadProgress : 0f;

        /// <summary>Start the download once the first scene is up, in every
        /// player that has the bundle (MAIN builds park the version too).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Prefetch() => Request();

        static string Url
        {
            get
            {
                string url = Application.streamingAssetsPath + "/" + BundleFile;
                if (url.Contains("://")) return url.StartsWith("http") ? url + "?v=" + Version : url;
                return "file:///" + url.TrimStart('/');
            }
        }

        /// <summary>Start fetching the bundle (once). Safe to call every frame.</summary>
        public static void Request()
        {
            if (!InBundle || fetch != Fetch.None) return;
            fetch = Fetch.Loading;
            string url = Url;
            float t0 = Time.realtimeSinceStartup;
            try
            {
                req = UnityWebRequestAssetBundle.GetAssetBundle(url);
                req.SendWebRequest().completed += _ => OnBundle(url, t0);
                Debug.Log("[Charlotte] fetching the city's data: " + url);
            }
            catch (System.Exception e) { fetch = Fetch.Failed; Debug.LogWarning("[Charlotte] the city's data is unavailable (" + url + ": " + e.Message + ")"); RaiseSettled(); }
        }

        static void OnBundle(string url, float t0)
        {
            AssetBundle b = null; string err = null;
            ulong bytes = req.downloadedBytes;
            try
            {
                if (req.result == UnityWebRequest.Result.Success) b = DownloadHandlerAssetBundle.GetContent(req);
                else err = req.error;
            }
            catch (System.Exception e) { err = e.Message; }
            finally { req.Dispose(); req = null; }
            if (b == null)
            {
                fetch = Fetch.Failed;
                Debug.LogWarning("[Charlotte] the city's data is unavailable (" + url + ": " + (err ?? "not an AssetBundle") + ")");
                RaiseSettled();
                return;
            }
            bundle = b;
            fetch = Fetch.Ready;
            Debug.Log($"[Charlotte] the city's data is ready: {bytes / 1048576f:F2} MB in {Time.realtimeSinceStartup - t0:F1} s");
            RaiseSettled();
        }

        /// <summary>One of the city's data files: from the bundle when this
        /// player has it and it has arrived, else from Resources.</summary>
        public static TextAsset Load(string name)
        {
            if (bundle != null)
            {
                var ta = bundle.LoadAsset<TextAsset>(name);
                if (ta != null) return ta;
            }
            if (InBundle && fetch != Fetch.Ready)
                Debug.LogWarning("[Charlotte] " + name + " asked for before the city's data arrived (" + fetch + ")");
            return Resources.Load<TextAsset>(name);
        }
    }
}
