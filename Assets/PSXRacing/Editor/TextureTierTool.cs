using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CLASSIC / ENHANCED, the editor half (2026-10-11).
    ///
    /// BUILD: reads Editor/TextureTierManifest.txt (written from the owner's
    /// registry by tools/texenhanced/make_enhanced.py), checks every original
    /// against its GUID, gives each Enhanced twin its original's import
    /// settings - filter, wrap, mips, alpha, colour space, platform formats,
    /// the psx16 label - at its own 2x size, and writes
    /// Resources/TextureTierMap.asset. The twins live under
    /// EnhancedSrc/TexEnhanced, outside Art/, so the 256 cap
    /// (PSXTextureCaps, applied by the builder's sweep over Art/) never sees
    /// them and the Classic cap stays where it is - and outside Resources,
    /// so the player never carries them (they took MAIN's WebGL.data to
    /// 106.6 MB; GitHub refuses 100).
    ///
    /// BUNDLE: <see cref="BuildBundle"/> packs every twin into ONE
    /// AssetBundle, Assets/StreamingAssets/texenhanced.bundle, which ships
    /// BESIDE the build (not in .data) and is fetched by the player only when
    /// ENHANCED is on. PSXBuildWebGL builds it right before every player
    /// build, after ReleaseBudget, so the twins carry the same release
    /// imports as their originals. LZ4 (ChunkBasedCompression), not LZMA: a
    /// WebGL player has no worker threads, and an LZMA bundle is unpacked
    /// whole on the main thread when it lands (a long freeze, and the whole
    /// bundle uncompressed in the browser's heap); LZ4 is read chunk by chunk
    /// as each texture loads. The download is bigger than LZMA's, once.
    ///
    /// The ORIGINALS are never touched: not their files, GUIDs or imports.
    ///
    /// Batch: -executeMethod PSXRacing.EditorTools.TextureTierTool.BuildMap
    ///        -executeMethod PSXRacing.EditorTools.TextureTierTool.SelfTest
    ///        -executeMethod PSXRacing.EditorTools.TextureTierTool.BuildAndTest
    ///        -executeMethod PSXRacing.EditorTools.TextureTierTool.BundleAndTest
    /// They write PSXRacing_textier_build.txt / PSXRacing_textier.txt at the project root.
    /// </summary>
    [InitializeOnLoad]
    public static class TextureTierTool
    {
        const string Manifest = "Assets/PSXRacing/Editor/TextureTierManifest.txt";
        const string MapAsset = "Assets/PSXRacing/Resources/TextureTierMap.asset";
        const string Prefix = TextureTier.EditorSrcRoot;
        public const string BundleDir = "Assets/StreamingAssets";
        /// <summary>The bundle build's own output (kept, so a rebuild is
        /// incremental); only the bundle itself is copied to StreamingAssets,
        /// not the manifests Unity writes beside it.</summary>
        const string BundleWork = "Library/PSXTexBundle";
        /// <summary>GitHub refuses 100 MiB; the publish ratchet is 95.</summary>
        const long BundleCap = 90L << 20;
        const string LogFile = "PSXRacing_textier.txt";
        const string BuildLogFile = "PSXRacing_textier_build.txt";

        static TextureTierTool()
        {
            // Play mode swaps the material ASSETS; the editor must not keep that.
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingPlayMode || s == PlayModeStateChange.EnteredEditMode)
                    RestoreClassic();
            };
            EditorApplication.quitting += RestoreClassic;
        }

        /// <summary>This material back to its originals. True when it changed.</summary>
        public static bool ToClassic(Material m)
        {
            TextureTier.ForceClassic = true;
            try { return TextureTier.Swap(m); }
            finally { TextureTier.ForceClassic = false; }
        }

        /// <summary>Every material in memory back to its originals, and the
        /// twins released.</summary>
        public static void RestoreClassic()
        {
            if (TextureTier.LoadedCount == 0 && !TextureTier.BundleLoaded) return;
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (ToClassic(m)) n++;
            TextureTier.ResetMap();
            if (n > 0) Debug.Log("[TexTier] restored " + n + " material(s) to CLASSIC");
        }

        struct Row
        {
            public string guid, orig, enh, id;
            public int w, h;
        }

        static List<Row> ReadManifest()
        {
            var rows = new List<Row>();
            foreach (var line in File.ReadAllLines(Manifest))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                rows.Add(new Row { guid = c[0], orig = c[1], enh = c[2], w = int.Parse(c[3]), h = int.Parse(c[4]), id = c[5] });
            }
            return rows;
        }

        static string ResPath(string enh) =>
            enh.Substring(Prefix.Length, enh.Length - Prefix.Length - Path.GetExtension(enh).Length);

        [MenuItem("PSX Racing/Textures/Build Enhanced Map")]
        public static void BuildMap()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh();
            var rows = ReadManifest();
            var pairs = new List<TextureTierMap.Pair>();
            var configured = new Dictionary<string, string>();   // enhanced path -> the original that set it
            int reimported = 0, fallback = 0;
            foreach (var r in rows)
            {
                string at = AssetDatabase.GUIDToAssetPath(r.guid);
                var orig = AssetDatabase.LoadAssetAtPath<Texture2D>(r.orig);
                if (at != r.orig || orig == null)
                {
                    // No match: that original keeps its own texture (fallback).
                    log.AppendLine("FALLBACK " + r.orig + " (guid resolves to '" + at + "')");
                    fallback++;
                    continue;
                }
                var oi = AssetImporter.GetAtPath(r.orig) as TextureImporter;
                var ei = AssetImporter.GetAtPath(r.enh) as TextureImporter;
                if (oi == null || ei == null) { log.AppendLine("FALLBACK " + r.orig + " (no importer for " + r.enh + ")"); fallback++; continue; }
                if (configured.TryGetValue(r.enh, out string first))
                {
                    // One artwork, several originals: they must agree on import.
                    string a = ImportKey(AssetImporter.GetAtPath(first) as TextureImporter), b = ImportKey(oi);
                    if (a != b) log.AppendLine("WARN " + r.enh + ": originals disagree on import (" + first + " vs " + r.orig + ")");
                }
                else
                {
                    configured[r.enh] = r.orig;
                    if (CopyImport(oi, ei, Mathf.NextPowerOfTwo(Mathf.Max(r.w, r.h)), r.enh)) reimported++;
                }
                pairs.Add(new TextureTierMap.Pair { original = orig, enhanced = ResPath(r.enh), id = r.id });
            }

            var map = AssetDatabase.LoadAssetAtPath<TextureTierMap>(MapAsset);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<TextureTierMap>();
                map.pairs = pairs.ToArray();
                AssetDatabase.CreateAsset(map, MapAsset);
            }
            else
            {
                map.pairs = pairs.ToArray();
                EditorUtility.SetDirty(map);
            }
            AssetDatabase.SaveAssets();
            TextureTier.ResetMap();
            log.Insert(0, $"[TexTier] BUILD OK {pairs.Count} pairs, {configured.Count} Enhanced files, {reimported} reimported, {fallback} fallback\n");
            File.WriteAllText(BuildLogFile, log.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>Batch: the same frames under CLASSIC and then ENHANCED -
        /// a Gillespie Gap start (StartGrid, roadside), the home street
        /// (garage door, windows) and two Charlotte streets - copied to
        /// PSX_TEX_OUT as classic_*.png / enhanced_*.png.</summary>
        public static void CaptureCompare()
        {
            string outDir = System.Environment.GetEnvironmentVariable("PSX_TEX_OUT");
            string root = Directory.GetParent(Application.dataPath).FullName;
            string shots = Path.Combine(root, "Screenshots");
            Directory.CreateDirectory(outDir);
            System.Environment.SetEnvironmentVariable("PSX_SHOT_VENUE", "GillespieGap");
            System.Environment.SetEnvironmentVariable("PSX_SHOT_SPOTS", "0:start");
            System.Environment.SetEnvironmentVariable("PSX_REF_ONLY", "sv_a1_trade,sv_a11_central");
            foreach (var tier in new[] { "classic", "enhanced" })
            {
                System.Environment.SetEnvironmentVariable("PSX_TEX", tier == "enhanced" ? "enhanced" : null);
                TextureTier.ApplyAll();
                void Grab(string dir, string pattern)
                {
                    if (!Directory.Exists(dir)) return;
                    foreach (var f in Directory.GetFiles(dir, pattern))
                        File.Copy(f, Path.Combine(outDir, tier + "_" + Path.GetFileName(f)), true);
                }
                PSXScreenshotTool.CaptureSpotsOnly();
                Grab(shots, "psx_spot_GillespieGap_0_*.png");
                PSXScreenshotTool.CaptureNeighborhood();
                Grab(shots, "psx_nb_*.png");
                CityRefSpots.Run();
                Grab(Path.Combine(shots, "City", "ref"), "*.png");
                Debug.Log($"[TexTier] captured {tier} (twins loaded {TextureTier.LoadedCount})");
            }
            System.Environment.SetEnvironmentVariable("PSX_TEX", null);
            RestoreClassic();
            Debug.Log("[TexTier] CAPTURE DONE");
        }

        /// <summary>Batch: build the map and the bundle, then the self-test.</summary>
        public static void BuildAndTest()
        {
            BuildMap();
            BundleAndTest();
        }

        /// <summary>Batch: the bundle for the active target, then the
        /// self-test (run with -buildTarget WebGL).</summary>
        public static void BundleAndTest()
        {
            string err = BuildBundle(EditorUserBuildSettings.activeBuildTarget, out long bytes);
            Debug.Log(err == null ? $"[TexTier] BUNDLE OK {bytes / 1048576f:F2} MB" : "[TexTier] BUNDLE FAILED: " + err);
            SelfTest();
        }

        [MenuItem("PSX Racing/Textures/Build Enhanced Bundle")]
        public static void BuildBundleMenu()
        {
            string err = BuildBundle(EditorUserBuildSettings.activeBuildTarget, out long bytes);
            if (err != null) Debug.LogError("[TexTier] BUNDLE FAILED: " + err);
            else Debug.Log($"[TexTier] BUNDLE OK {bytes / 1048576f:F2} MB for {EditorUserBuildSettings.activeBuildTarget}");
        }

        /// <summary>Every twin the map names, into
        /// StreamingAssets/texenhanced.bundle for this target, each under its
        /// key (TextureTierMap.Pair.enhanced); the bundle's content hash goes
        /// into the map as bundleVersion (the URL's ?v=). Null on success,
        /// else why not. The twins' imports are used as they stand.</summary>
        public static string BuildBundle(BuildTarget target, out long bytes)
        {
            bytes = 0;
            var map = AssetDatabase.LoadAssetAtPath<TextureTierMap>(MapAsset);
            if (map == null || map.pairs == null || map.pairs.Length == 0) return "no pairs in " + MapAsset;
            var keys = new List<string>();
            var paths = new List<string>();
            var seen = new HashSet<string>();
            foreach (var p in map.pairs)
            {
                if (string.IsNullOrEmpty(p.enhanced) || !seen.Add(p.enhanced)) continue;
                string path = TextureTier.EditorSrcRoot + p.enhanced + ".png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null) return "twin missing: " + path;
                keys.Add(p.enhanced);
                paths.Add(path);
            }
            Directory.CreateDirectory(BundleWork);
            var build = new AssetBundleBuild
            {
                assetBundleName = TextureTier.BundleFile,
                assetNames = paths.ToArray(),
                addressableNames = keys.ToArray(),
            };
            var manifest = BuildPipeline.BuildAssetBundles(BundleWork, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) return "BuildAssetBundles failed for " + target;
            string built = Path.Combine(BundleWork, TextureTier.BundleFile);
            if (!File.Exists(built)) return "nothing written at " + built;
            bytes = new FileInfo(built).Length;
            if (bytes >= BundleCap) return $"{bytes / 1048576f:F1} MB - over the {BundleCap >> 20} MB cap";

            bool newDir = !Directory.Exists(BundleDir);
            Directory.CreateDirectory(BundleDir);
            if (newDir) AssetDatabase.ImportAsset(BundleDir);
            string dst = BundleDir + "/" + TextureTier.BundleFile;
            if (!File.Exists(dst) || !SameBytes(built, dst))
            {
                File.Copy(built, dst, true);
                AssetDatabase.ImportAsset(dst);
            }
            string v = manifest.GetAssetBundleHash(TextureTier.BundleFile).ToString();
            // The bundle build unloads what nothing holds: the map loaded
            // above is gone by now, so it is loaded again.
            map = AssetDatabase.LoadAssetAtPath<TextureTierMap>(MapAsset);
            if (map != null && map.bundleVersion != v)
            {
                map.bundleVersion = v;
                EditorUtility.SetDirty(map);
                AssetDatabase.SaveAssetIfDirty(map);
            }
            TextureTier.ResetMap();
            Debug.Log($"[TexTier] bundle {dst}: {keys.Count} twins, {bytes / 1048576f:F2} MB LZ4, {target}, version {v}");
            return null;
        }

        static bool SameBytes(string a, string b)
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            return System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(a), File.ReadAllBytes(b));
        }

        /// <summary>Everything about an import but its size and identity.</summary>
        static string ImportKey(TextureImporter i)
        {
            var s = new TextureImporterSettings();
            i.ReadTextureSettings(s);
            var sb = new StringBuilder(EditorJsonUtility.ToJson(s));
            sb.Append('|').Append(i.textureType).Append(i.filterMode).Append(i.wrapModeU).Append(i.wrapModeV)
              .Append(i.mipmapEnabled).Append(i.sRGBTexture).Append(i.alphaSource).Append(i.alphaIsTransparency)
              .Append(i.npotScale).Append(i.isReadable).Append(i.anisoLevel);
            sb.Append('|').Append(i.textureCompression).Append('|').Append(string.Join(",", AssetDatabase.GetLabels(AssetImporterGuid(i))));
            foreach (var p in Platforms)
            {
                var ps = i.GetPlatformTextureSettings(p);
                sb.Append('|').Append(p).Append(ps.overridden).Append(ps.format).Append(ps.textureCompression)
                  .Append(ps.compressionQuality).Append(ps.crunchedCompression).Append(ps.resizeAlgorithm);
            }
            return sb.ToString();
        }

        static readonly string[] Platforms = { "Standalone", "WebGL", "Android", "iPhone" };

        static string SizeKey(TextureImporter i)
        {
            var sb = new StringBuilder().Append(i.maxTextureSize);
            foreach (var p in Platforms) sb.Append(',').Append(i.GetPlatformTextureSettings(p).maxTextureSize);
            return sb.ToString();
        }

        static GUID AssetImporterGuid(AssetImporter i) => AssetDatabase.GUIDFromAssetPath(i.assetPath);

        static bool CopyImport(TextureImporter src, TextureImporter dst, int max, string dstPath)
        {
            string before = ImportKey(dst) + "|" + SizeKey(dst);
            var s = new TextureImporterSettings();
            src.ReadTextureSettings(s);
            dst.SetTextureSettings(s);
            dst.textureCompression = src.textureCompression;
            dst.maxTextureSize = max;
            foreach (var p in Platforms)
            {
                var ps = src.GetPlatformTextureSettings(p);
                var pd = dst.GetPlatformTextureSettings(p);
                if (!ps.overridden && !pd.overridden) continue;
                ps.maxTextureSize = max;
                dst.SetPlatformTextureSettings(ps);
            }
            bool dirty = before != ImportKey(dst) + "|" + SizeKey(dst);
            if (dirty) dst.SaveAndReimport();
            // Labels last (they live in the .meta and change no import).
            var want = AssetDatabase.GetLabels(AssetImporterGuid(src));
            var have = AssetDatabase.GetLabels(AssetImporterGuid(dst));
            if (string.Join(",", want) != string.Join(",", have))
            {
                AssetDatabase.SetLabels(AssetDatabase.LoadMainAssetAtPath(dstPath), want);
                dirty = true;
            }
            return dirty;
        }

        static long Bytes(Texture2D t)
        {
            if (t == null) return 0;
            long n = 0;
            for (int m = 0; m < t.mipmapCount; m++)
                n += GraphicsFormatUtility.ComputeMipmapSize(Mathf.Max(1, t.width >> m), Mathf.Max(1, t.height >> m), t.graphicsFormat);
            return n;
        }

        /// <summary>Every pair resolves, every twin is 2x its original with
        /// the original's import, the swap goes both ways without dirtying
        /// the asset, and the size each way.</summary>
        public static void SelfTest()
        {
            if (File.Exists(LogFile)) File.Delete(LogFile);
            var log = new StringBuilder();
            int fail = 0, warn = 0;
            void Fail(string s) { fail++; log.AppendLine("FAIL " + s); }
            void Warn(string s) { warn++; log.AppendLine("WARN " + s); }
            log.AppendLine("target " + EditorUserBuildSettings.activeBuildTarget);

            var rows = ReadManifest();
            TextureTier.ResetMap();
            var map = TextureTier.Map;
            if (map == null) { Fail("no TextureTierMap in Resources"); goto done; }
            foreach (var p in map.pairs)
                if (Resources.Load<Texture2D>(p.enhanced) != null) Fail("twin is in Resources (it would ship in the player): " + p.enhanced);
            var byOrig = new Dictionary<string, TextureTierMap.Pair>();
            foreach (var p in map.pairs) if (p.original != null) byOrig[AssetDatabase.GetAssetPath(p.original)] = p;

            var origSeen = new HashSet<Texture2D>();
            var enhSeen = new HashSet<Texture2D>();
            long origBytes = 0, enhBytes = 0;
            var sizes = new List<(long, string)>();
            foreach (var r in rows)
            {
                if (AssetDatabase.GUIDToAssetPath(r.guid) != r.orig) { Fail("guid " + r.guid + " no longer " + r.orig); continue; }
                if (!byOrig.TryGetValue(r.orig, out var pair)) { Fail("no pair for " + r.orig); continue; }
                var o = pair.original;
                var e = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureTier.EditorSrcRoot + pair.enhanced + ".png");
                if (e == null) { Fail("twin missing: " + pair.enhanced); continue; }
                if (e.width != r.w || e.height != r.h)
                    Fail($"{r.orig}: twin {e.width}x{e.height}, registry {r.w}x{r.h}");
                else if (e.width != 2 * o.width || e.height != 2 * o.height)
                    Warn($"{r.orig}: original imports {o.width}x{o.height}; twin is 2x the registry baseline, {e.width}x{e.height}");
                var oi = (TextureImporter)AssetImporter.GetAtPath(r.orig);
                var ei = (TextureImporter)AssetImporter.GetAtPath(r.enh);
                if (ImportKey(oi) != ImportKey(ei)) Fail("import differs: " + r.orig + " vs " + r.enh);
                if (ei.maxTextureSize < Mathf.Max(r.w, r.h)) Fail("capped: " + r.enh);
                if ((o.mipmapCount > 1) != (e.mipmapCount > 1)) Fail("mips differ: " + r.orig);
                if (o.isDataSRGB != e.isDataSRGB) Fail("colour space differs: " + r.orig);
                if (o.filterMode != e.filterMode || o.wrapModeU != e.wrapModeU || o.wrapModeV != e.wrapModeV) Fail("filter/wrap differs: " + r.orig);
                if (o.format != e.format) Warn($"{r.orig}: format {o.format} vs twin {e.format}");
                bool lo = ReleaseBudget.HasPsx16Label(r.guid), le = ReleaseBudget.HasPsx16Label(AssetDatabase.AssetPathToGUID(r.enh));
                if (ReleaseBudget.InSet(r.orig, oi, lo) != ReleaseBudget.InSet(r.enh, ei, le))
                    Warn($"{r.orig}: 16-bit set {ReleaseBudget.InSet(r.orig, oi, lo)} vs twin {ReleaseBudget.InSet(r.enh, ei, le)}");
                if (origSeen.Add(o)) origBytes += Bytes(o);
                if (enhSeen.Add(e)) { enhBytes += Bytes(e); sizes.Add((Bytes(e), $"{r.enh} {e.width}x{e.height} {e.graphicsFormat} mips={e.mipmapCount}")); }
            }

            // The swap, both ways, on a real material asset.
            {
                Material probe = null;
                Texture2D was = null;
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/PSXRacing" }))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (m == null || !m.HasProperty("_MainTex")) continue;
                    if (m.mainTexture is Texture2D t && byOrig.ContainsKey(AssetDatabase.GetAssetPath(t))) { probe = m; was = t; break; }
                }
                if (probe == null) Fail("no material wears a mapped original");
                else
                {
                    bool dirtyBefore = EditorUtility.IsDirty(probe);
                    System.Environment.SetEnvironmentVariable("PSX_TEX", "enhanced");
                    PSXTexDecode.Stamp(probe);
                    var on = probe.mainTexture;
                    System.Environment.SetEnvironmentVariable("PSX_TEX", null);
                    TextureTier.ApplyAll();
                    var off = probe.mainTexture;
                    log.AppendLine($"swap {probe.name}: {was.name} -> {(on ? on.name : "null")} -> {(off ? off.name : "null")}");
                    if (on == was || on == null) Fail("ENHANCED did not swap " + probe.name);
                    if (off != was) Fail("CLASSIC did not restore " + probe.name);
                    if (!dirtyBefore && EditorUtility.IsDirty(probe)) Fail("swap dirtied " + probe.name);
                    if (TextureTier.LoadedCount != 0) Warn("twins still loaded after CLASSIC: " + TextureTier.LoadedCount);
                }
            }

            // The bundle: every twin the map names, under its key, the size
            // and format the editor imports it at.
            {
                string bp = BundleDir + "/" + TextureTier.BundleFile;
                if (!File.Exists(bp)) Fail("no bundle at " + bp + " (PSX Racing/Textures/Build Enhanced Bundle)");
                else
                {
                    var ab = AssetBundle.LoadFromFile(bp);
                    if (ab == null) Fail("the bundle does not load: " + bp);
                    else
                    {
                        try
                        {
                            var keys = new HashSet<string>();
                            int held = 0;
                            foreach (var p in map.pairs)
                            {
                                if (!keys.Add(p.enhanced)) continue;
                                var bt = ab.LoadAsset<Texture2D>(p.enhanced);
                                if (bt == null) { Fail("bundle lacks " + p.enhanced); continue; }
                                held++;
                                var st = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureTier.EditorSrcRoot + p.enhanced + ".png");
                                if (st == null) continue;
                                if (bt.width != st.width || bt.height != st.height) Fail($"bundle {p.enhanced} is {bt.width}x{bt.height}, EnhancedSrc imports {st.width}x{st.height}");
                                if (bt.format != st.format || bt.mipmapCount != st.mipmapCount || bt.filterMode != st.filterMode)
                                    Warn($"bundle {p.enhanced}: {bt.format} mips={bt.mipmapCount} {bt.filterMode} vs EnhancedSrc {st.format} mips={st.mipmapCount} {st.filterMode}");
                            }
                            int extra = ab.GetAllAssetNames().Length - held;
                            if (extra != 0) Warn("bundle holds " + extra + " asset(s) beyond the map");
                            log.AppendLine($"BUNDLE {bp}: {new FileInfo(bp).Length / 1048576f:F2} MB, {held}/{keys.Count} twins, version '{map.bundleVersion}'");
                        }
                        finally { ab.Unload(true); }
                    }
                }
            }

            sizes.Sort((a, b) => b.Item1.CompareTo(a.Item1));
            foreach (var z in sizes) log.AppendLine($"  twin {z.Item1 / 1024} KB {z.Item2}");
            log.AppendLine($"SIZE originals {origSeen.Count} tex {origBytes / 1048576f:F2} MB | twins {enhSeen.Count} tex {enhBytes / 1048576f:F2} MB (this target's import format, mips included)");
            log.AppendLine($"MEMORY classic {origBytes / 1048576f:F2} MB | enhanced {(origBytes + enhBytes) / 1048576f:F2} MB (the map keeps the originals)");
        done:
            log.Insert(0, (fail == 0 ? "[TexTier] SELF-TEST OK" : "[TexTier] SELF-TEST FAILED") + $" {rows.Count} rows, {fail} fail, {warn} warn\n");
            File.WriteAllText(LogFile, log.ToString());
            Debug.Log(log.ToString());
        }
    }
}
