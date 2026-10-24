using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S BIG DATA AS AN ON-DEMAND BUNDLE (2026-10-24, one build
    /// again). The five files <see cref="CharlotteData.Bundled"/> names stay in
    /// Resources in the project (every tool reads them there); a WebGL build
    /// that carries Charlotte packs them here into
    /// StreamingAssets/charlotte.bundle, LZ4, writes the content hash to
    /// Resources/charlotte_bundle.txt (the player's sign that they are in the
    /// bundle, and the URL's ?v=), and EditionParking parks the five out of
    /// Resources for the BuildPlayer call, so WebGL.data never holds them.
    /// The CockpitBundle pattern.
    /// </summary>
    public static class CharlotteBundle
    {
        const string BundleDir = "Assets/StreamingAssets";
        const string BundleWork = "Library/PSXCharlotteBundle";
        const string Res = "Assets/PSXRacing/Resources/";
        public const string VersionAsset = Res + CharlotteData.VersionPath + ".txt";
        /// <summary>A guard: the five are ~16 MB raw; anything near this is wrong.</summary>
        const long BundleCap = 40L << 20;

        [MenuItem("PSX Racing/Build Charlotte Bundle (WebGL)")]
        public static void BuildMenu()
        {
            string err = Build(BuildTarget.WebGL, out long bytes);
            if (err != null) Debug.LogError("[CharlotteBundle] FAILED: " + err);
        }

        /// <summary>Null on success, else why not.</summary>
        public static string Build(BuildTarget target, out long bytes)
        {
            bytes = 0;
            var paths = new string[CharlotteData.Bundled.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = Res + CharlotteData.Bundled[i] + ".bytes";
                if (AssetDatabase.LoadMainAssetAtPath(paths[i]) == null) return "missing " + paths[i];
            }
            Directory.CreateDirectory(BundleWork);
            var build = new AssetBundleBuild
            {
                assetBundleName = CharlotteData.BundleFile,
                assetNames = paths,
                addressableNames = CharlotteData.Bundled,
            };
            var manifest = BuildPipeline.BuildAssetBundles(BundleWork, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) return "BuildAssetBundles failed for " + target;
            string built = Path.Combine(BundleWork, CharlotteData.BundleFile);
            if (!File.Exists(built)) return "nothing written at " + built;
            bytes = new FileInfo(built).Length;
            if (bytes >= BundleCap) return $"{bytes / 1048576f:F1} MB - over the {BundleCap >> 20} MB cap";

            bool newDir = !Directory.Exists(BundleDir);
            Directory.CreateDirectory(BundleDir);
            if (newDir) AssetDatabase.ImportAsset(BundleDir);
            string dst = BundleDir + "/" + CharlotteData.BundleFile;
            if (!File.Exists(dst) || new FileInfo(dst).Length != bytes || !System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(built), File.ReadAllBytes(dst)))
            {
                File.Copy(built, dst, true);
                AssetDatabase.ImportAsset(dst);
            }
            string v = manifest.GetAssetBundleHash(CharlotteData.BundleFile).ToString();
            if (!File.Exists(VersionAsset) || File.ReadAllText(VersionAsset).Trim() != v)
            {
                File.WriteAllText(VersionAsset, v + "\n");
                AssetDatabase.ImportAsset(VersionAsset);
            }
            Debug.Log($"[CharlotteBundle] {dst}: {paths.Length} assets, {bytes / 1048576f:F2} MB LZ4, {target}, version {v}");
            return null;
        }

        /// <summary>A build without Charlotte ships neither the bundle nor its
        /// version (MAIN also parks every charlotte_* file; this keeps
        /// StreamingAssets from carrying 6 MB nobody fetches).</summary>
        public static void Remove()
        {
            string dst = BundleDir + "/" + CharlotteData.BundleFile;
            if (File.Exists(dst)) AssetDatabase.DeleteAsset(dst);
        }
    }
}
