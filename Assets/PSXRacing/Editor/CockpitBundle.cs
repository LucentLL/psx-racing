using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE v08 COCKPIT AS AN ON-DEMAND BUNDLE (2026-10-16). MAIN's WebGL.data
    /// was 92.8 MB against GitHub's 100 MB file wall with 3.5 MB more already
    /// queued, so the owner's cockpit - 1.25 MB of geometry and ~2.1 MB of
    /// textures - may not ride in it. Its assets live in CockpitSrc (outside
    /// Resources, so no player build packs them) and go here into
    /// StreamingAssets/cockpit.bundle, LZ4, which a player fetches the first
    /// time the cockpit view is entered (CockpitModel.Request). Built by
    /// PSXBuildWebGL right after the Enhanced texture bundle, so the textures
    /// carry the release imports; the content hash goes into
    /// Resources/Cockpit/v08_bundle.txt as the URL's ?v=.
    /// The texenhanced.bundle pattern (TextureTierTool.BuildBundle).
    /// </summary>
    public static class CockpitBundle
    {
        const string BundleDir = "Assets/StreamingAssets";
        const string BundleWork = "Library/PSXCockpitBundle";
        const string VersionAsset = "Assets/PSXRacing/Resources/Cockpit/v08_bundle.txt";
        /// <summary>A guard: the bundle is ~2.5 MB; anything near this is wrong.</summary>
        const long BundleCap = 12L << 20;

        [MenuItem("PSX Racing/Build Cockpit Bundle (WebGL)")]
        public static void BuildMenu()
        {
            string err = Build(BuildTarget.WebGL, out long bytes);
            if (err != null) Debug.LogError("[CockpitBundle] FAILED: " + err);
        }

        /// <summary>Batchmode entry: build for WebGL and report.</summary>
        public static void BuildFromCommandLine()
        {
            string err = Build(BuildTarget.WebGL, out long bytes);
            Debug.Log(err == null ? $"[CockpitBundle] OK {bytes} bytes" : "[CockpitBundle] FAILED: " + err);
        }

        /// <summary>Null on success, else why not.</summary>
        public static string Build(BuildTarget target, out long bytes)
        {
            bytes = 0;
            var paths = new string[CockpitModel.BundleAssets.Length];
            var names = new string[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = CockpitModel.EditorSrc + CockpitModel.BundleAssets[i];
                names[i] = Path.GetFileNameWithoutExtension(paths[i]);
                if (AssetDatabase.LoadMainAssetAtPath(paths[i]) == null) return "missing " + paths[i];
            }
            Directory.CreateDirectory(BundleWork);
            var build = new AssetBundleBuild
            {
                assetBundleName = CockpitModel.BundleFile,
                assetNames = paths,
                addressableNames = names,
            };
            var manifest = BuildPipeline.BuildAssetBundles(BundleWork, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) return "BuildAssetBundles failed for " + target;
            string built = Path.Combine(BundleWork, CockpitModel.BundleFile);
            if (!File.Exists(built)) return "nothing written at " + built;
            bytes = new FileInfo(built).Length;
            if (bytes >= BundleCap) return $"{bytes / 1048576f:F1} MB - over the {BundleCap >> 20} MB cap";

            bool newDir = !Directory.Exists(BundleDir);
            Directory.CreateDirectory(BundleDir);
            if (newDir) AssetDatabase.ImportAsset(BundleDir);
            string dst = BundleDir + "/" + CockpitModel.BundleFile;
            if (!File.Exists(dst) || new FileInfo(dst).Length != bytes || !System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(built), File.ReadAllBytes(dst)))
            {
                File.Copy(built, dst, true);
                AssetDatabase.ImportAsset(dst);
            }
            string v = manifest.GetAssetBundleHash(CockpitModel.BundleFile).ToString();
            if (!File.Exists(VersionAsset) || File.ReadAllText(VersionAsset).Trim() != v)
            {
                File.WriteAllText(VersionAsset, v + "\n");
                AssetDatabase.ImportAsset(VersionAsset);
            }
            Debug.Log($"[CockpitBundle] {dst}: {paths.Length} assets, {bytes / 1048576f:F2} MB LZ4, {target}, version {v}");
            return null;
        }
    }
}
