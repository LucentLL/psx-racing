using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Set = PSXRacing.EngineVoiceLibrary.Set;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ENGINE FAMILIES AS ON-DEMAND BUNDLES (engine audio phase 2,
    /// 2026-10-16). MAIN's WebGL.data was ~96.5 MB against GitHub's 100 MB
    /// file wall and ~23 MB of it was 28 engine families, of which a race
    /// plays five. Every family but the default (EngineVoiceLibrary.DefaultFamily,
    /// in Resources so a car always has a voice) lives in
    /// Assets/PSXRacing/EngineSrc/&lt;family&gt; - outside Resources, so no
    /// player build packs it - and goes here into one bundle per family and
    /// set:
    ///   StreamingAssets/engines/&lt;family&gt;.bundle      exterior takes
    ///   StreamingAssets/engines/&lt;family&gt;_int.bundle  cockpit (interior) takes
    /// Built by PSXBuildWebGL right after the cockpit bundle, for every
    /// edition, so the clips carry this build's WebGL (AAC) encodes; the
    /// content hashes go into Resources/EngineBundles.txt as the URLs' ?v=.
    /// The texenhanced.bundle / cockpit.bundle pattern.
    /// </summary>
    public static class EngineBundleBuilder
    {
        public const string SrcRoot = "Assets/PSXRacing/EngineSrc";
        public const string BundleDir = "Assets/StreamingAssets/" + EngineVoiceLibrary.BundleFolder;
        const string BundleWork = "Library/PSXEngineBundles";
        public const string ManifestAsset = "Assets/PSXRacing/Resources/" + EngineVoiceLibrary.ManifestPath + ".txt";

        /// <summary>Importer quality of the exterior takes: the 0.8 they had in
        /// Resources (PSXRacingBuilder.EngineClipQuality), so moving them into
        /// bundles changes where they are, not how they sound.</summary>
        public const float ExteriorQuality = 0.8f;
        /// <summary>Importer quality of the interior takes. See
        /// tools/engine_interior_quality.mjs: the cabin recordings carry
        /// almost nothing above 10 kHz, so they hold up at a lower rate.</summary>
        public const float InteriorQuality = 0.6f;
        /// <summary>A guard: a family's set is ~0.5-2 MB; anything near this is wrong.</summary>
        const long PackCap = 8L << 20;

        public struct Pack
        {
            public string name, family;
            public Set set;
            public string[] paths;
        }

        /// <summary>Every bundle the source tree asks for, in name order.</summary>
        public static List<Pack> Plan()
        {
            var list = new List<Pack>();
            if (!Directory.Exists(SrcRoot)) return list;
            foreach (var dir in Directory.GetDirectories(SrcRoot).OrderBy(d => d, System.StringComparer.Ordinal))
            {
                string fam = Path.GetFileName(dir);
                // The default family's exterior set is in Resources (main data).
                if (fam != EngineVoiceLibrary.DefaultFamily)
                {
                    var ext = Oggs(dir);
                    if (ext.Length > 0) list.Add(new Pack { name = EngineVoiceLibrary.PackName(fam, Set.Exterior), family = fam, set = Set.Exterior, paths = ext });
                }
                string idir = dir + "/Interior";
                if (Directory.Exists(idir))
                {
                    var inn = Oggs(idir);
                    if (inn.Length > 0) list.Add(new Pack { name = EngineVoiceLibrary.PackName(fam, Set.Interior), family = fam, set = Set.Interior, paths = inn });
                }
            }
            return list;
        }

        static string[] Oggs(string dir) =>
            Directory.GetFiles(dir, "*.ogg", SearchOption.TopDirectoryOnly)
                     .Select(p => p.Replace('\\', '/'))
                     .OrderBy(p => p, System.StringComparer.Ordinal).ToArray();

        /// <summary>
        /// Every bundled clip to its import settings: Vorbis (AAC on WebGL),
        /// decompress on load, NOT preloaded (a family decodes only the takes a
        /// car plays, when it plays them), stereo. Guarded: only an importer
        /// whose settings differ is re-imported (PSXRacingBuilder's lesson -
        /// an unconditional SaveAndReimport of hundreds of clips per build is
        /// minutes of re-encoding and has crashed the editor).
        /// </summary>
        public static int ConfigureImporters()
        {
            int n = 0;
            foreach (var p in Plan())
                foreach (var path in p.paths)
                    if (Configure(path, p.set == Set.Interior ? InteriorQuality : ExteriorQuality, false)) n++;
            return n;
        }

        /// <summary>One clip to the engine-clip settings; true if it was re-imported.</summary>
        public static bool Configure(string path, float quality, bool preload)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) return false;
            var s = imp.defaultSampleSettings;
            if (s.loadType == AudioClipLoadType.DecompressOnLoad
                && s.compressionFormat == AudioCompressionFormat.Vorbis
                && Mathf.Abs(s.quality - quality) < 0.005f
                && s.preloadAudioData == preload
                && !imp.forceToMono && !imp.loadInBackground) return false;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = quality;
            s.preloadAudioData = preload;
            imp.defaultSampleSettings = s;
            imp.forceToMono = false;
            imp.loadInBackground = false;
            imp.SaveAndReimport();
            return true;
        }

        [MenuItem("PSX Racing/Build Engine Bundles (WebGL)")]
        public static void BuildMenu()
        {
            string err = Build(BuildTarget.WebGL, out long bytes, out var lines);
            if (err != null) Debug.LogError("[EngineBundles] FAILED: " + err);
            else foreach (var l in lines) Debug.Log("[EngineBundles] " + l);
        }

        /// <summary>
        /// Batchmode entry. PSX_ENGINE_TARGET=editor builds for the editor's
        /// own platform into PSX_ENGINE_OUT (an editor load test: a WebGL
        /// bundle's AAC audio does not play in the Windows editor), leaving the
        /// manifest alone; anything else builds the WebGL set into
        /// StreamingAssets. Writes PSXRacing_engine_bundles.txt.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            bool editor = System.Environment.GetEnvironmentVariable("PSX_ENGINE_TARGET") == "editor";
            string outDir = System.Environment.GetEnvironmentVariable("PSX_ENGINE_OUT");
            var target = editor ? EditorUserBuildSettings.activeBuildTarget : BuildTarget.WebGL;
            if (editor && (target == BuildTarget.WebGL || target == BuildTarget.NoTarget)) target = BuildTarget.StandaloneWindows64;
            string err = Build(target, out long bytes, out var lines,
                               string.IsNullOrEmpty(outDir) ? BundleDir : outDir, writeManifest: !editor);
            lines.Insert(0, err == null ? $"OK {target} {bytes} bytes" : "FAILED: " + err);
            File.WriteAllLines("PSXRacing_engine_bundles.txt", lines);
            Debug.Log("[EngineBundles] " + string.Join("\n", lines));
            EditorApplication.Exit(err == null ? 0 : 1);
        }

        /// <summary>Null on success, else why not. <paramref name="lines"/>:
        /// one "name bytes clips" line per bundle for the report.</summary>
        public static string Build(BuildTarget target, out long bytes, out List<string> lines,
                                   string outDir = BundleDir, bool writeManifest = true)
        {
            bytes = 0;
            lines = new List<string>();
            var plan = Plan();
            if (plan.Count == 0) return "nothing under " + SrcRoot;
            int reimported = ConfigureImporters();
            if (reimported > 0) lines.Add("re-imported " + reimported + " clips to the engine-clip settings");

            var builds = plan.Select(p => new AssetBundleBuild
            {
                assetBundleName = p.name + ".bundle",
                assetNames = p.paths,
                addressableNames = p.paths.Select(Path.GetFileNameWithoutExtension).ToArray(),
            }).ToArray();
            foreach (var p in plan)
                foreach (var a in p.paths)
                    if (AssetDatabase.LoadMainAssetAtPath(a) == null) return "missing " + a;

            string work = BundleWork + "/" + target;
            Directory.CreateDirectory(work);
            var manifest = BuildPipeline.BuildAssetBundles(work, builds,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) return "BuildAssetBundles failed for " + target;

            bool inAssets = outDir.Replace('\\', '/').StartsWith("Assets/");
            if (inAssets && !Directory.Exists("Assets/StreamingAssets"))
            {
                Directory.CreateDirectory("Assets/StreamingAssets");
                AssetDatabase.ImportAsset("Assets/StreamingAssets");
            }
            Directory.CreateDirectory(outDir);
            var keep = new HashSet<string>();
            var man = new List<string>
            {
                "# Engine family bundles (StreamingAssets/" + EngineVoiceLibrary.BundleFolder + "/<name>.bundle):",
                "# name content-hash bytes. Written by EngineBundleBuilder with the build; do not hand-edit.",
            };
            long ext = 0, inn = 0;
            foreach (var p in plan)
            {
                string file = p.name + ".bundle";
                string built = Path.Combine(work, file);
                if (!File.Exists(built)) return "nothing written at " + built;
                long len = new FileInfo(built).Length;
                if (len >= PackCap) return $"{p.name}: {len / 1048576f:F1} MB - over the {PackCap >> 20} MB cap";
                string dst = Path.Combine(outDir, file);
                if (!File.Exists(dst) || new FileInfo(dst).Length != len || !File.ReadAllBytes(built).SequenceEqual(File.ReadAllBytes(dst)))
                    File.Copy(built, dst, true);
                keep.Add(file);
                bytes += len;
                if (p.set == Set.Interior) inn += len; else ext += len;
                string hash = manifest.GetAssetBundleHash(file).ToString();
                man.Add(p.name + " " + hash + " " + len);
                lines.Add($"{p.name,-28} {len / 1024f,8:F0} KB  {p.paths.Length,2} clips");
            }
            // Bundles of families that no longer exist.
            foreach (var f in Directory.GetFiles(outDir, "*.bundle"))
                if (!keep.Contains(Path.GetFileName(f))) { File.Delete(f); if (File.Exists(f + ".meta")) File.Delete(f + ".meta"); }
            if (inAssets) AssetDatabase.Refresh();
            lines.Add($"total {bytes / 1048576f:F2} MB in {plan.Count} bundles: exterior {ext / 1048576f:F2} MB, interior {inn / 1048576f:F2} MB ({target})");

            if (writeManifest)
            {
                string text = string.Join("\n", man) + "\n";
                if (!File.Exists(ManifestAsset) || File.ReadAllText(ManifestAsset) != text)
                {
                    File.WriteAllText(ManifestAsset, text);
                    AssetDatabase.ImportAsset(ManifestAsset);
                }
            }
            return null;
        }
    }
}
