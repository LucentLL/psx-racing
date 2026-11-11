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
    /// THE HD TIER'S IMPORT AND SHIPPING (TextureHD). Codex delivers each HD
    /// set to the art workspace (AGENTS.md: 4x the authoring size, a PBR set
    /// with the original's basename): <c>&lt;name&gt;_overhaul1_hd.png</c> (colour),
    /// <c>_overhaul1_hd_normal</c>, <c>_overhaul1_hd_rough</c>, <c>_overhaul1_hd_ao</c>.
    /// The PC build's sandbox imports them from there (PSX_HD_SRC, default the
    /// workspace's Environment_HD folder) into Assets/PSXRacing/HDSrc/TexHD
    /// (gitignored: the repo is public and the sets are gigabytes), each with
    /// its import (colour sRGB BC7, normal BC5, roughness/AO linear BC4, mips
    /// streamed, 8x aniso), writes the TextureHDMap to Resources, and after the
    /// player is built packs the sets into ONE bundle copied straight into
    /// the player's StreamingAssets - never into the project, so no web build
    /// can pick it up.
    /// </summary>
    public static class TextureHDTool
    {
        public const string MapAsset = "Assets/PSXRacing/Resources/TextureHDMap.asset";
        // the RELEASE folder only: Codex's drafts live beside it in _Authoring
        const string DefaultSrc = @"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Environment_HD\Release";
        const string Suffix = "_overhaul1_hd";
        const string WorkDir = "Temp/TexHDBundle";

        static string SrcDir
        {
            get
            {
                string e = System.Environment.GetEnvironmentVariable("PSX_HD_SRC");
                return string.IsNullOrEmpty(e) ? DefaultSrc : e;
            }
        }

        [MenuItem("PSX Racing/Textures/Import HD Sets")]
        static void ImportMenu() => Debug.Log("[TextureHD] " + (Import(out int n) ?? $"{n} HD sets imported"));

        /// <summary>Import every HD set found in the source folder and write the
        /// map. Null on success; the number of sets in <paramref name="sets"/>.</summary>
        public static string Import(out int sets)
        {
            sets = 0;
            var pairs = new List<TextureHDMap.Pair>();
            // PSX_HD_SRC may name several folders, ';' between (a stage plus a test set)
            var dirs = new List<string>();
            foreach (var d in SrcDir.Split(';')) if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d.Trim())) dirs.Add(d.Trim());
            if (dirs.Count == 0)
            {
                WriteMap(pairs);
                return null;   // no HD sets yet: an empty tier, not an error
            }
            Directory.CreateDirectory(TextureHD.EditorSrcRoot);
            var log = new System.Text.StringBuilder();
            var bases = new HashSet<string>();
            string facadeParts = null, gutter = null;
            var seen = new HashSet<string>();
            var colours = new List<string>();
            foreach (var d in dirs) colours.AddRange(Directory.GetFiles(d, "*" + Suffix + ".png", SearchOption.AllDirectories));
            foreach (var colour in colours)
            {
                string key = Path.GetFileNameWithoutExtension(colour);
                key = key.Substring(0, key.Length - Suffix.Length);
                if (!seen.Add(key)) { log.AppendLine("  " + key + ": a second copy at " + colour + " - skipped (the first found wins)"); continue; }
                if (key.StartsWith(RoadBasePrefix))
                {
                    // a road BASE set has no game texture of its own: the road path binds it (RoadBases)
                    bases.Add(key);
                    CopySet(colour, key);
                    log.AppendLine($"  {key}: road base set");
                    continue;
                }
                // the facade atlas's column 7 parts and the road's gutter pan: bound by the shader (TextureHDMap)
                if (key == TextureHDMap.FacadePartsKey) { facadeParts = key; CopySet(colour, key); log.AppendLine($"  {key}: facade column 7 parts set"); continue; }
                if (key == TextureHDMap.GutterKey) { gutter = key; CopySet(colour, key); log.AppendLine($"  {key}: gutter pan set"); continue; }
                var original = FindOriginal(key);
                if (original == null) { log.AppendLine("  no game texture named " + key + " - skipped"); continue; }
                CopySet(colour, key);
                pairs.Add(new TextureHDMap.Pair { original = original, key = key });
                log.AppendLine($"  {key}: {AssetDatabase.GetAssetPath(original)}");
            }
            var roads = RoadBases(bases, log);
            WriteMap(pairs, roads, facadeParts, gutter);
            sets = pairs.Count + bases.Count + (facadeParts != null ? 1 : 0) + (gutter != null ? 1 : 0);
            Debug.Log($"[TextureHD] {pairs.Count} HD sets + {bases.Count} road base sets ({roads.Count} road textures drawn on them)" +
                      $"{(facadeParts != null ? " + facade parts" : "")}{(gutter != null ? " + gutter pan" : "")} from {SrcDir}\n{log}");
            return null;
        }

        static void CopySet(string colour, string key)
        {
            string dir = Path.GetDirectoryName(colour);
            Copy(colour, key, TexKind.Colour);
            Copy(Path.Combine(dir, key + Suffix + "_normal.png"), key + TextureHD.NormalSuffix, TexKind.Normal);
            Copy(Path.Combine(dir, key + Suffix + "_rough.png"), key + TextureHD.RoughSuffix, TexKind.Data);
            Copy(Path.Combine(dir, key + Suffix + "_ao.png"), key + TextureHD.AOSuffix, TexKind.Data);
        }

        // ---- THE ROAD BASE (Charlotte texture batch 01; TextureHDMap.Road) ----
        public const string RoadBasePrefix = "city_road_base_";
        const string RoadArtDir = "Assets/PSXRacing/Art/City";

        /// <summary>Every Charlotte carriageway texture whose surface has a base
        /// set: its paint mask written beside the sets (PSXRacingBuilder.RoadPaintMask)
        /// and its row in the table; the junction slabs on the base too (no mask,
        /// the fan's world XZ / 12 m UV at three base tiles a repeat).</summary>
        static List<TextureHDMap.Road> RoadBases(HashSet<string> bases, System.Text.StringBuilder log)
        {
            var roads = new List<TextureHDMap.Road>();
            if (bases.Count == 0) return roads;
            int masks = 0, unknown = 0;
            foreach (var png in Directory.GetFiles(RoadArtDir, "city_road_*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(png);
                if (!PSXRacingBuilder.RoadTexOf(name, out int profile, out var surf, out bool junction)) continue;
                string baseKey = RoadBasePrefix + PSXRacingBuilder.RoadSurfaceKey(surf);
                if (!bases.Contains(baseKey)) continue;
                string asset = RoadArtDir + "/" + name + ".png";
                var original = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                if (original == null) continue;
                if (junction)
                {
                    roads.Add(new TextureHDMap.Road { original = original, paint = null, baseKey = baseKey, repeat = new Vector2(12f, 12f) / TextureHDMap.TileM });
                    continue;
                }
                var mask = PSXRacingBuilder.RoadPaintMask(png, surf, out int unk, out int painted);
                string maskKey = name + TextureHD.PaintSuffix;
                string dst = TextureHD.EditorSrcRoot + maskKey + ".png";
                byte[] bytes = mask.EncodeToPNG();
                Object.DestroyImmediate(mask);
                if (!File.Exists(dst) || !SameBytes(File.ReadAllBytes(dst), bytes)) File.WriteAllBytes(dst, bytes);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
                MaskImport(dst);
                masks++; unknown += unk;
                if (unk > 0) log.AppendLine($"  {name}: {unk} texels neither paint, grain nor rumble (drawn as pavement)");
                float w = PSXRacingBuilder.RoadProfileWidth(profile);
                roads.Add(new TextureHDMap.Road { original = original, paint = maskKey, baseKey = baseKey,
                                                  repeat = new Vector2(w, City.CityMeshes.RoadVTile) / TextureHDMap.TileM,
                                                  widthM = w, gutter = PSXRacingBuilder.RoadProfileCurbed(profile) });
            }
            log.AppendLine($"  road base: {masks} paint masks, {roads.Count - masks} junction slabs, {unknown} unclassified texels in all");
            return roads;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>A paint mask: linear data, filtered and mipped (the paint's
        /// edge is now a filtered line, not the Classic point-sampled step),
        /// uncompressed (256 x 64: a BC block would smear a 2-texel line).</summary>
        static void MaskImport(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            bool dirty = false;
            void Want<T>(T now, T want, System.Action<T> set) { if (!EqualityComparer<T>.Default.Equals(now, want)) { set(want); dirty = true; } }
            Want(imp.textureType, TextureImporterType.Default, v => imp.textureType = v);
            Want(imp.sRGBTexture, false, v => imp.sRGBTexture = v);
            Want(imp.mipmapEnabled, true, v => imp.mipmapEnabled = v);
            Want(imp.anisoLevel, 8, v => imp.anisoLevel = v);
            Want(imp.filterMode, FilterMode.Trilinear, v => imp.filterMode = v);
            Want(imp.wrapMode, TextureWrapMode.Repeat, v => imp.wrapMode = v);
            Want(imp.textureCompression, TextureImporterCompression.Uncompressed, v => imp.textureCompression = v);
            Want(imp.npotScale, TextureImporterNPOTScale.None, v => imp.npotScale = v);
            var ps = imp.GetPlatformTextureSettings("Standalone");
            if (!ps.overridden || ps.format != TextureImporterFormat.RGBA32)
            {
                ps.overridden = true; ps.format = TextureImporterFormat.RGBA32; ps.maxTextureSize = 1024;
                imp.SetPlatformTextureSettings(ps);
                dirty = true;
            }
            if (dirty) imp.SaveAndReimport();
        }

        enum TexKind { Colour, Normal, Data }

        static void Copy(string src, string name, TexKind kind)
        {
            if (!File.Exists(src)) return;
            string dst = TextureHD.EditorSrcRoot + name + ".png";
            if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length || File.GetLastWriteTimeUtc(dst) < File.GetLastWriteTimeUtc(src))
                File.Copy(src, dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
            var imp = AssetImporter.GetAtPath(dst) as TextureImporter;
            if (imp == null) return;
            bool dirty = false;
            void Want<T>(T now, T want, System.Action<T> set) { if (!EqualityComparer<T>.Default.Equals(now, want)) { set(want); dirty = true; } }
            Want(imp.textureType, kind == TexKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => imp.textureType = v);
            Want(imp.sRGBTexture, kind == TexKind.Colour, v => imp.sRGBTexture = v);
            Want(imp.mipmapEnabled, true, v => imp.mipmapEnabled = v);
            Want(imp.streamingMipmaps, true, v => imp.streamingMipmaps = v);
            Want(imp.anisoLevel, 8, v => imp.anisoLevel = v);
            Want(imp.filterMode, FilterMode.Trilinear, v => imp.filterMode = v);
            Want(imp.maxTextureSize, 4096, v => imp.maxTextureSize = v);
            Want(imp.textureCompression, TextureImporterCompression.Compressed, v => imp.textureCompression = v);
            var ps = imp.GetPlatformTextureSettings("Standalone");
            var fmt = kind == TexKind.Colour ? TextureImporterFormat.BC7 : kind == TexKind.Normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC4;
            if (!ps.overridden || ps.format != fmt || ps.maxTextureSize != 4096)
            {
                ps.overridden = true; ps.format = fmt; ps.maxTextureSize = 4096;
                imp.SetPlatformTextureSettings(ps);
                dirty = true;
            }
            if (dirty) imp.SaveAndReimport();
        }

        static Texture2D FindOriginal(string key)
        {
            Texture2D found = null;
            foreach (var guid in AssetDatabase.FindAssets(key + " t:Texture2D", new[] { "Assets/PSXRacing" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.Contains("/HDSrc/") || p.Contains("/EnhancedSrc/")) continue;
                if (Path.GetFileNameWithoutExtension(p) != key) continue;
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t == null) continue;
                if (found != null) { Debug.LogWarning($"[TextureHD] two game textures named {key}: keeping {AssetDatabase.GetAssetPath(found)}, not {p}"); continue; }
                found = t;
            }
            return found;
        }

        static void WriteMap(List<TextureHDMap.Pair> pairs) => WriteMap(pairs, new List<TextureHDMap.Road>(), null, null);

        static void WriteMap(List<TextureHDMap.Pair> pairs, List<TextureHDMap.Road> roads, string facadeParts, string gutter)
        {
            var map = AssetDatabase.LoadAssetAtPath<TextureHDMap>(MapAsset);
            if (pairs.Count == 0 && roads.Count == 0 && facadeParts == null && gutter == null)
            {
                if (map != null) AssetDatabase.DeleteAsset(MapAsset);
                return;
            }
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<TextureHDMap>();
                AssetDatabase.CreateAsset(map, MapAsset);
            }
            map.pairs = pairs.ToArray();
            map.roads = roads.ToArray();
            map.paintWhite = PSXRacingBuilder.PaintWhiteLinear;
            map.paintYellow = PSXRacingBuilder.PaintYellowLinear;
            map.facadeParts = facadeParts;
            map.gutter = gutter;
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        /// <summary>After the player is built: the sets as ONE bundle in its
        /// StreamingAssets. Null on success (or nothing to ship).</summary>
        public static string ShipBundle(BuildTarget target, string playerDataStreamingAssets, out long bytes)
        {
            bytes = 0;
            var map = AssetDatabase.LoadAssetAtPath<TextureHDMap>(MapAsset);
            if (map == null || ((map.pairs == null || map.pairs.Length == 0) && (map.roads == null || map.roads.Length == 0)
                                && string.IsNullOrEmpty(map.facadeParts) && string.IsNullOrEmpty(map.gutter))) return null;
            var names = new List<string>();
            var paths = new List<string>();
            var keys = new List<string>();
            if (map.pairs != null) foreach (var p in map.pairs) keys.Add(p.key);
            if (!string.IsNullOrEmpty(map.facadeParts)) keys.Add(map.facadeParts);
            if (!string.IsNullOrEmpty(map.gutter)) keys.Add(map.gutter);
            if (map.roads != null)
                foreach (var r in map.roads)
                {
                    if (!keys.Contains(r.baseKey)) keys.Add(r.baseKey);
                    if (!string.IsNullOrEmpty(r.paint))
                    {
                        string mp = TextureHD.EditorSrcRoot + r.paint + ".png";
                        if (AssetDatabase.LoadAssetAtPath<Texture2D>(mp) != null && !names.Contains(r.paint)) { names.Add(r.paint); paths.Add(mp); }
                    }
                }
            foreach (var key in keys)
                foreach (var suffix in new[] { "", TextureHD.NormalSuffix, TextureHD.RoughSuffix, TextureHD.AOSuffix })
                {
                    string path = TextureHD.EditorSrcRoot + key + suffix + ".png";
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null) continue;
                    names.Add(key + suffix);
                    paths.Add(path);
                }
            Directory.CreateDirectory(WorkDir);
            var build = new AssetBundleBuild { assetBundleName = TextureHD.BundleFile, assetNames = paths.ToArray(), addressableNames = names.ToArray() };
            var manifest = BuildPipeline.BuildAssetBundles(WorkDir, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) return "BuildAssetBundles failed for " + target;
            string built = Path.Combine(WorkDir, TextureHD.BundleFile);
            if (!File.Exists(built)) return "nothing written at " + built;
            Directory.CreateDirectory(playerDataStreamingAssets);
            File.Copy(built, Path.Combine(playerDataStreamingAssets, TextureHD.BundleFile), true);
            bytes = new FileInfo(built).Length;
            return null;
        }
    }

    /// <summary>The HD tier's shader code ships in the Windows player only: the
    /// PSX_HD variants of PSX/Lit are dropped from every other build (the web
    /// player and the phone never turn the keyword on).</summary>
    class HDVariantStripper : IPreprocessShaders
    {
        public int callbackOrder => 0;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader == null || shader.name != "PSX/Lit") return;
            var t = EditorUserBuildSettings.activeBuildTarget;
            if (t == BuildTarget.StandaloneWindows64 || t == BuildTarget.StandaloneWindows) return;
            var kw = new ShaderKeyword(shader, TextureHD.Keyword);
            for (int i = data.Count - 1; i >= 0; i--)
                if (data[i].shaderKeywordSet.IsEnabled(kw)) data.RemoveAt(i);
        }
    }
}
