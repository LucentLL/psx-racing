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
        const string DefaultSrc = @"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Environment_HD";
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
            if (!Directory.Exists(SrcDir))
            {
                WriteMap(pairs);
                return null;   // no HD sets yet: an empty tier, not an error
            }
            Directory.CreateDirectory(TextureHD.EditorSrcRoot);
            var log = new System.Text.StringBuilder();
            foreach (var colour in Directory.GetFiles(SrcDir, "*" + Suffix + ".png", SearchOption.AllDirectories))
            {
                string key = Path.GetFileNameWithoutExtension(colour);
                key = key.Substring(0, key.Length - Suffix.Length);
                var original = FindOriginal(key);
                if (original == null) { log.AppendLine("  no game texture named " + key + " - skipped"); continue; }
                string dir = Path.GetDirectoryName(colour);
                Copy(colour, key, TexKind.Colour);
                Copy(Path.Combine(dir, key + Suffix + "_normal.png"), key + TextureHD.NormalSuffix, TexKind.Normal);
                Copy(Path.Combine(dir, key + Suffix + "_rough.png"), key + TextureHD.RoughSuffix, TexKind.Data);
                Copy(Path.Combine(dir, key + Suffix + "_ao.png"), key + TextureHD.AOSuffix, TexKind.Data);
                pairs.Add(new TextureHDMap.Pair { original = original, key = key });
                log.AppendLine($"  {key}: {AssetDatabase.GetAssetPath(original)}");
            }
            WriteMap(pairs);
            sets = pairs.Count;
            Debug.Log($"[TextureHD] {sets} HD sets from {SrcDir}\n{log}");
            return null;
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

        static void WriteMap(List<TextureHDMap.Pair> pairs)
        {
            var map = AssetDatabase.LoadAssetAtPath<TextureHDMap>(MapAsset);
            if (pairs.Count == 0)
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
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        /// <summary>After the player is built: the sets as ONE bundle in its
        /// StreamingAssets. Null on success (or nothing to ship).</summary>
        public static string ShipBundle(BuildTarget target, string playerDataStreamingAssets, out long bytes)
        {
            bytes = 0;
            var map = AssetDatabase.LoadAssetAtPath<TextureHDMap>(MapAsset);
            if (map == null || map.pairs == null || map.pairs.Length == 0) return null;
            var names = new List<string>();
            var paths = new List<string>();
            foreach (var p in map.pairs)
                foreach (var suffix in new[] { "", TextureHD.NormalSuffix, TextureHD.RoughSuffix, TextureHD.AOSuffix })
                {
                    string path = TextureHD.EditorSrcRoot + p.key + suffix + ".png";
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null) continue;
                    names.Add(p.key + suffix);
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
