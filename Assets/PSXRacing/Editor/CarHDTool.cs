using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE HD CAR TIER'S IMPORT AND SHIPPING (CarHD), on TextureHDTool's
    /// pattern. The source is a STAGE folder (PSX_CAR_HD_SRC, default
    /// C:\Users\mcgee\PSXHDStage\carhd) holding each delivered set under the
    /// game's names - made from the artist's folders by the stage script
    /// (tools/carmodels/stage_carhd.py) - one set per GAME LIVERY SHEET:
    ///   &lt;body&gt;__&lt;sheet&gt;_hd.png         colour, sRGB (alpha = material class)
    ///   &lt;body&gt;__&lt;sheet&gt;_hd_normal.png  tangent normal, +Y
    ///   &lt;body&gt;__&lt;sheet&gt;_hd_orm.png     linear R ao, G rough, B metal, A coat
    /// and per body &lt;body&gt;_lightmask_hd.png (linear RGBA lamp map). &lt;sheet&gt;
    /// is the basename of the 512 sheet under Art/Car/Models/&lt;body&gt;/textures.
    /// The sets are copied into Assets/PSXRacing/HDSrc/CarHD (gitignored) with
    /// their imports (colour BC7 sRGB, normal BC5, ORM and lamp map BC7 linear;
    /// mipped, trilinear, 16x / 8x aniso, clamped), the CarHDMap goes to
    /// Resources, and after the player is built the sets go into ONE bundle in
    /// its StreamingAssets (carhd.bundle) - never into the project.
    /// </summary>
    public static class CarHDTool
    {
        public const string MapAsset = "Assets/PSXRacing/Resources/CarHDMap.asset";
        const string DefaultSrc = @"C:\Users\mcgee\PSXHDStage\carhd";
        const string ModelDir = "Assets/PSXRacing/Art/Car/Models/";
        const string WorkDir = "Temp/CarHDBundle";
        public const string ColourSuffix = "_hd", LampSuffix = "_lightmask_hd";

        static string SrcDir
        {
            get
            {
                string e = System.Environment.GetEnvironmentVariable("PSX_CAR_HD_SRC");
                return string.IsNullOrEmpty(e) ? DefaultSrc : e;
            }
        }

        [MenuItem("PSX Racing/Textures/Import HD Car Sets")]
        static void ImportMenu() => Debug.Log("[CarHD] " + (Import(out int n) ?? $"{n} HD car sets imported"));

        /// <summary>Batch entry: import and write the log to Screenshots/carhd_import.txt.</summary>
        public static void ImportBatch()
        {
            string err = Import(out int n);
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "carhd_import.txt"), (err ?? "OK") + "\n" + n + " sets\n" + lastLog);
        }
        static string lastLog = "";

        /// <summary>Import every staged set and write the map. Null on success.</summary>
        public static string Import(out int sets)
        {
            sets = 0;
            var log = new System.Text.StringBuilder();
            var sheets = new List<CarHDMap.Sheet>();
            var lamps = new List<CarHDMap.Lamp>();
            string src = SrcDir;
            if (!Directory.Exists(src))
            {
                WriteMap(sheets, lamps);
                lastLog = "no stage at " + src;
                return null;   // no sets: an empty tier, not an error
            }
            Directory.CreateDirectory(CarHD.EditorSrcRoot);
            foreach (var colour in Directory.GetFiles(src, "*__*" + ColourSuffix + ".png"))
            {
                string key = Path.GetFileNameWithoutExtension(colour);
                key = key.Substring(0, key.Length - ColourSuffix.Length);
                int sep = key.IndexOf("__", System.StringComparison.Ordinal);
                string body = key.Substring(0, sep), sheet = key.Substring(sep + 2);
                var original = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelDir + body + "/textures/" + sheet + ".png");
                if (original == null) { log.AppendLine($"  {key}: no game sheet {ModelDir}{body}/textures/{sheet}.png - skipped"); continue; }
                string nrm = Path.Combine(src, key + ColourSuffix + "_normal.png"), orm = Path.Combine(src, key + ColourSuffix + "_orm.png");
                if (!File.Exists(nrm) || !File.Exists(orm)) { log.AppendLine($"  {key}: normal or ORM missing - skipped"); continue; }
                var c = Copy(colour, key, Kind.Colour);
                var n = Copy(nrm, key + CarHD.NormalSuffix, Kind.Normal);
                var o = Copy(orm, key + CarHD.OrmSuffix, Kind.Data);
                if (c == null || n == null || o == null) { log.AppendLine($"  {key}: import failed - skipped"); continue; }
                // the HD sheet must be the 512 sheet at a whole multiple, same aspect (same UVs)
                if (c.width % original.width != 0 || c.width / original.width != c.height / original.height || n.width != c.width || o.width != c.width)
                { log.AppendLine($"  {key}: sizes {c.width}x{c.height} / normal {n.width} / orm {o.width} do not match the game sheet {original.width}x{original.height} - skipped"); continue; }
                sheets.Add(new CarHDMap.Sheet { original = original, body = body, key = key });
                log.AppendLine($"  {key}: {c.width}x{c.height} {c.format} / {n.format} / {o.format} over {AssetDatabase.GetAssetPath(original)}");
            }
            foreach (var lamp in Directory.GetFiles(src, "*" + LampSuffix + ".png"))
            {
                string key = Path.GetFileNameWithoutExtension(lamp);
                string body = key.Substring(0, key.Length - LampSuffix.Length);
                if (!Directory.Exists(ModelDir + body)) { log.AppendLine($"  {key}: no body {body} - skipped"); continue; }
                var t = Copy(lamp, key, Kind.Data);
                if (t == null) continue;
                lamps.Add(new CarHDMap.Lamp { body = body, key = key });
                log.AppendLine($"  {key}: lamp map {t.width}x{t.height} {t.format}");
            }
            WriteMap(sheets, lamps);
            CarHD.ResetMap();
            sets = sheets.Count;
            lastLog = log.ToString();
            Debug.Log($"[CarHD] {sheets.Count} livery sets + {lamps.Count} lamp maps from {src}\n{log}");
            return null;
        }

        enum Kind { Colour, Normal, Data }

        static Texture2D Copy(string src, string name, Kind kind)
        {
            if (!File.Exists(src)) return null;
            string dst = CarHD.EditorSrcRoot + name + ".png";
            if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length || File.GetLastWriteTimeUtc(dst) < File.GetLastWriteTimeUtc(src))
                File.Copy(src, dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
            var imp = AssetImporter.GetAtPath(dst) as TextureImporter;
            if (imp == null) return null;
            bool dirty = false;
            void Want<T>(T now, T want, System.Action<T> set) { if (!EqualityComparer<T>.Default.Equals(now, want)) { set(want); dirty = true; } }
            Want(imp.textureType, kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => imp.textureType = v);
            Want(imp.sRGBTexture, kind == Kind.Colour, v => imp.sRGBTexture = v);
            Want(imp.alphaSource, kind == Kind.Normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput, v => imp.alphaSource = v);
            Want(imp.alphaIsTransparency, false, v => imp.alphaIsTransparency = v);
            Want(imp.mipmapEnabled, true, v => imp.mipmapEnabled = v);
            Want(imp.streamingMipmaps, false, v => imp.streamingMipmaps = v);
            Want(imp.isReadable, false, v => imp.isReadable = v);
            Want(imp.anisoLevel, kind == Kind.Data ? 8 : 16, v => imp.anisoLevel = v);
            Want(imp.filterMode, FilterMode.Trilinear, v => imp.filterMode = v);
            Want(imp.wrapMode, TextureWrapMode.Clamp, v => imp.wrapMode = v);
            Want(imp.npotScale, TextureImporterNPOTScale.None, v => imp.npotScale = v);
            Want(imp.maxTextureSize, 4096, v => imp.maxTextureSize = v);
            Want(imp.textureCompression, TextureImporterCompression.CompressedHQ, v => imp.textureCompression = v);
            var ps = imp.GetPlatformTextureSettings("Standalone");
            var fmt = kind == Kind.Normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7;
            if (!ps.overridden || ps.format != fmt || ps.maxTextureSize != 4096)
            {
                ps.overridden = true; ps.format = fmt; ps.maxTextureSize = 4096;
                imp.SetPlatformTextureSettings(ps);
                dirty = true;
            }
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static void WriteMap(List<CarHDMap.Sheet> sheets, List<CarHDMap.Lamp> lamps)
        {
            var map = AssetDatabase.LoadAssetAtPath<CarHDMap>(MapAsset);
            if (sheets.Count == 0)
            {
                if (map != null) AssetDatabase.DeleteAsset(MapAsset);
                return;
            }
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<CarHDMap>();
                AssetDatabase.CreateAsset(map, MapAsset);
            }
            map.sheets = sheets.ToArray();
            map.lamps = lamps.ToArray();
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The bundle's asset paths and names (the sets in the map).</summary>
        static void Contents(CarHDMap map, List<string> paths, List<string> names)
        {
            foreach (var s in map.sheets)
                foreach (var suffix in new[] { "", CarHD.NormalSuffix, CarHD.OrmSuffix })
                {
                    string p = CarHD.EditorSrcRoot + s.key + suffix + ".png";
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(p) == null) continue;
                    paths.Add(p); names.Add(s.key + suffix);
                }
            if (map.lamps != null)
                foreach (var l in map.lamps)
                {
                    string p = CarHD.EditorSrcRoot + l.key + ".png";
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(p) == null || names.Contains(l.key)) continue;
                    paths.Add(p); names.Add(l.key);
                }
        }

        /// <summary>After the player is built: the sets as ONE bundle in its
        /// StreamingAssets. Null on success (or nothing to ship).</summary>
        public static string ShipBundle(BuildTarget target, string playerDataStreamingAssets, out long bytes)
        {
            bytes = 0;
            var map = AssetDatabase.LoadAssetAtPath<CarHDMap>(MapAsset);
            if (map == null || map.sheets == null || map.sheets.Length == 0) return null;
            var names = new List<string>();
            var paths = new List<string>();
            Contents(map, paths, names);
            Directory.CreateDirectory(WorkDir);
            var build = new AssetBundleBuild { assetBundleName = CarHD.BundleFile, assetNames = paths.ToArray(), addressableNames = names.ToArray() };
            var manifest = BuildPipeline.BuildAssetBundles(WorkDir, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) return "BuildAssetBundles failed for " + target;
            string built = Path.Combine(WorkDir, CarHD.BundleFile);
            if (!File.Exists(built)) return "nothing written at " + built;
            Directory.CreateDirectory(playerDataStreamingAssets);
            File.Copy(built, Path.Combine(playerDataStreamingAssets, CarHD.BundleFile), true);
            bytes = new FileInfo(built).Length;
            return null;
        }

        /// <summary>Batch entry for a size check: the bundle built for
        /// Windows into Temp/CarHDBundle (no player), its size and the GPU
        /// memory of each set written to Screenshots/carhd_bundle.txt.</summary>
        public static void BundleReport()
        {
            var sb = new System.Text.StringBuilder();
            var map = AssetDatabase.LoadAssetAtPath<CarHDMap>(MapAsset);
            if (map == null) { sb.AppendLine("no map"); }
            else
            {
                string err = ShipBundle(BuildTarget.StandaloneWindows64, Path.Combine(WorkDir, "out"), out long bytes);
                sb.AppendLine(err ?? $"carhd.bundle {bytes / 1048576f:0.0} MB (LZ4 chunks)");
                var paths = new List<string>(); var names = new List<string>();
                Contents(map, paths, names);
                long total = 0;
                for (int i = 0; i < paths.Count; i++)
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
                    long gpu = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                    var imp = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
                    var ps = imp != null ? imp.GetPlatformTextureSettings("Standalone") : null;
                    total += gpu;
                    sb.AppendLine($"  {names[i]}: {t.width}x{t.height} editor {t.format} / Standalone {(ps != null ? ps.format.ToString() : "?")} mips {t.mipmapCount} aniso {t.anisoLevel} {t.filterMode} - {gpu / 1048576f:0.00} MB in the editor");
                }
                sb.AppendLine($"total {total / 1048576f:0.0} MB (editor sizes)");
            }
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "carhd_bundle.txt"), sb.ToString());
        }
    }

    /// <summary>The HD car tier's shader code ships in the Windows player
    /// only: the PSX_CAR_HD variants of PSX/CarPaint are dropped from every
    /// other build (the web player and the phone never turn it on).</summary>
    class CarHDVariantStripper : IPreprocessShaders
    {
        public int callbackOrder => 0;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader == null || shader.name != "PSX/CarPaint") return;
            var t = EditorUserBuildSettings.activeBuildTarget;
            if (t == BuildTarget.StandaloneWindows64 || t == BuildTarget.StandaloneWindows) return;
            var kw = new ShaderKeyword(shader, CarHD.Keyword);
            for (int i = data.Count - 1; i >= 0; i--)
                if (data[i].shaderKeywordSet.IsEnabled(kw)) data.RemoveAt(i);
        }
    }
}
