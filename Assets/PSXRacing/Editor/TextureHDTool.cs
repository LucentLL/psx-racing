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
            foreach (var d in SrcDir.Split(';', '|')) if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d.Trim())) dirs.Add(d.Trim());
            if (dirs.Count == 0)
            {
                WriteMap(pairs);
                return null;   // no HD sets yet: an empty tier, not an error
            }
            Directory.CreateDirectory(TextureHD.EditorSrcRoot);
            var log = new System.Text.StringBuilder();
            var bases = new HashSet<string>();
            string facadeParts = null, gutter = null;
            var facadeColour = new string[TextureHDMap.FacadeCols];
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
                // round 3: one facade atlas column in HD (city_facade_c00..c15), stacked below
                if (key.StartsWith(TextureHDMap.FacadeColPrefix) && key.Length == TextureHDMap.FacadeColPrefix.Length + 2
                    && int.TryParse(key.Substring(TextureHDMap.FacadeColPrefix.Length), out int fcol) && fcol >= 0 && fcol < TextureHDMap.FacadeCols && fcol != 7)
                {
                    facadeColour[fcol] = colour;
                    continue;
                }
                var original = FindOriginal(key);
                if (original == null) { log.AppendLine("  no game texture named " + key + " - skipped"); continue; }
                CopySet(colour, key);
                pairs.Add(new TextureHDMap.Pair { original = original, key = key });
                log.AppendLine($"  {key}: {AssetDatabase.GetAssetPath(original)}");
            }
            var roads = RoadBases(bases, log);
            var facadeCols = FacadeArrays(facadeColour, log);
            WriteMap(pairs, roads, facadeParts, gutter);
            {
                var m = AssetDatabase.LoadAssetAtPath<TextureHDMap>(MapAsset);
                if (m == null && facadeCols != null) { m = ScriptableObject.CreateInstance<TextureHDMap>(); m.pairs = pairs.ToArray(); m.roads = roads.ToArray(); AssetDatabase.CreateAsset(m, MapAsset); }
                if (m != null) { m.facadeCols = facadeCols; EditorUtility.SetDirty(m); AssetDatabase.SaveAssets(); }
            }
            sets = pairs.Count + bases.Count + (facadeParts != null ? 1 : 0) + (gutter != null ? 1 : 0);
            Debug.Log($"[TextureHD] {pairs.Count} HD sets + {bases.Count} road base sets ({roads.Count} road textures drawn on them)" +
                      $"{(facadeParts != null ? " + facade parts" : "")}{(gutter != null ? " + gutter pan" : "")} from {SrcDir}\n{log}");
            return null;
        }

        // ---- THE FACADE ATLAS IN HD (round 3; TextureHDMap.facadeCols) ----
        /// <summary>Stack the delivered facade columns into three
        /// Texture2DArrays beside the sets (colour; normal; R rough, G ao,
        /// B glass), one slice per atlas column. Null when none was delivered.</summary>
        static bool[] FacadeArrays(string[] colourOf, System.Text.StringBuilder log)
        {
            int n = TextureHDMap.FacadeCols, W = TextureHDMap.FacadeSliceW, H = TextureHDMap.FacadeSliceH;
            var col = new Texture2D[n]; var nrm = new Texture2D[n]; var msk = new Texture2D[n];
            bool any = false;
            for (int c = 0; c < n; c++)
            {
                string src = colourOf[c];
                if (src == null) continue;
                string key = TextureHDMap.FacadeColPrefix + c.ToString("00");
                string dir = Path.GetDirectoryName(src);
                Copy(src, key, TexKind.Colour, readable: true);
                Copy(Path.Combine(dir, key + Suffix + "_normal.png"), key + TextureHD.NormalSuffix, TexKind.Normal, readable: true);
                string maskPath = TextureHD.EditorSrcRoot + key + "_mask.png";
                if (!PackMask(Path.Combine(dir, key + Suffix), maskPath, W, H, out string why)) { log.AppendLine($"  {key}: SKIPPED - {why}"); continue; }
                AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceSynchronousImport);
                MaskSliceImport(maskPath);
                var tc = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureHD.EditorSrcRoot + key + ".png");
                var tn = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureHD.EditorSrcRoot + key + TextureHD.NormalSuffix + ".png");
                var tm = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                if (tc == null || tn == null || tm == null) { log.AppendLine($"  {key}: SKIPPED - colour, normal or packed mask missing"); continue; }
                if (tc.width != W || tc.height != H || tn.width != W || tn.height != H) { log.AppendLine($"  {key}: SKIPPED - must be {W} x {H} (colour {tc.width} x {tc.height}, normal {tn.width} x {tn.height})"); continue; }
                col[c] = tc; nrm[c] = tn; msk[c] = tm; any = true;
                log.AppendLine($"  {key}: facade column {c} set");
            }
            if (!any) { foreach (var k in new[] { "_col", "_nrm", "_mask" }) AssetDatabase.DeleteAsset(TextureHD.EditorSrcRoot + TextureHDMap.FacadeArrayKey + k + ".asset"); return null; }
            var on = new bool[n];
            // tex4: the arrays hold the DELIVERED columns only, in column order
            // (16 slices of 1024 x 2048 were 128 MB for three columns of art);
            // TextureHD hands PSX/Lit each column's slice + 1
            var cc = new List<Texture2D>(); var cn = new List<Texture2D>(); var cm = new List<Texture2D>();
            for (int c = 0; c < n; c++) if (col[c] != null) { cc.Add(col[c]); cn.Add(nrm[c]); cm.Add(msk[c]); }
            var ca = cc.ToArray(); var na = cn.ToArray(); var ma = cm.ToArray();
            bool ok = BuildArray(TextureHDMap.FacadeArrayKey + "_col", ca, false, log)
                    & BuildArray(TextureHDMap.FacadeArrayKey + "_nrm", na, true, log)
                    & BuildArray(TextureHDMap.FacadeArrayKey + "_mask", ma, true, log);
            if (!ok) return null;
            // (a slice BuildArray had to leave empty - format/mips mismatch, in its
            // log - keeps its place so the numbering holds; it draws black)
            for (int c = 0; c < n; c++) on[c] = col[c] != null;
            return on;
        }

        /// <summary>R = rough, G = ao, B = glass (0 without a _glass map), at the
        /// slice size; false when rough or ao is missing or the wrong size.</summary>
        static bool PackMask(string stem, string dst, int W, int H, out string why)
        {
            string err = null;
            Color32[] Read(string p, bool need)
            {
                if (!File.Exists(p)) { if (need) err = "no " + Path.GetFileName(p); return null; }
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                t.LoadImage(File.ReadAllBytes(p));
                if (t.width != W || t.height != H) { err = Path.GetFileName(p) + $" is {t.width} x {t.height}"; Object.DestroyImmediate(t); return null; }
                var px = t.GetPixels32(); Object.DestroyImmediate(t); return px;
            }
            var r = Read(stem + "_rough.png", true);
            var a = r != null ? Read(stem + "_ao.png", true) : null;
            var g = a != null ? Read(stem + "_glass.png", false) : null;
            why = err;
            if (r == null || a == null || err != null) return false;
            var o = new Color32[W * H];
            for (int i = 0; i < o.Length; i++) o[i] = new Color32(r[i].r, a[i].r, g != null ? g[i].r : (byte)0, 255);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(o); tex.Apply();
            byte[] png = tex.EncodeToPNG(); Object.DestroyImmediate(tex);
            if (!File.Exists(dst) || !SameBytes(File.ReadAllBytes(dst), png)) File.WriteAllBytes(dst, png);
            return true;
        }

        static void MaskSliceImport(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            bool dirty = false;
            void Want<T>(T now, T want, System.Action<T> set) { if (!EqualityComparer<T>.Default.Equals(now, want)) { set(want); dirty = true; } }
            Want(imp.textureType, TextureImporterType.Default, v => imp.textureType = v);
            Want(imp.sRGBTexture, false, v => imp.sRGBTexture = v);
            Want(imp.mipmapEnabled, true, v => imp.mipmapEnabled = v);
            Want(imp.isReadable, true, v => imp.isReadable = v);
            Want(imp.maxTextureSize, 4096, v => imp.maxTextureSize = v);
            Want(imp.textureCompression, TextureImporterCompression.Compressed, v => imp.textureCompression = v);
            var ps = imp.GetPlatformTextureSettings("Standalone");
            if (!ps.overridden || ps.format != TextureImporterFormat.BC7) { ps.overridden = true; ps.format = TextureImporterFormat.BC7; ps.maxTextureSize = 4096; imp.SetPlatformTextureSettings(ps); dirty = true; }
            if (dirty) imp.SaveAndReimport();
        }

        /// <summary>One Texture2DArray asset from same-size, same-format slices
        /// (null = a column with no art: left empty, never sampled).</summary>
        static bool BuildArray(string name, Texture2D[] slices, bool linear, System.Text.StringBuilder log)
        {
            Texture2D first = null;
            foreach (var s in slices) if (s != null) { first = s; break; }
            if (first == null) return false;
            var arr = new Texture2DArray(first.width, first.height, slices.Length, first.format, first.mipmapCount, linear)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8,
            };
            for (int c = 0; c < slices.Length; c++)
            {
                var s = slices[c];
                if (s == null) continue;
                if (s.format != first.format || s.mipmapCount != first.mipmapCount) { log.AppendLine($"  {name}: slice {c} is {s.format}/{s.mipmapCount} mips, the array {first.format}/{first.mipmapCount} - left empty"); slices[c] = null; continue; }
                for (int mip = 0; mip < first.mipmapCount; mip++) Graphics.CopyTexture(s, 0, mip, arr, c, mip);
            }
            arr.Apply(false, false);
            string path = TextureHD.EditorSrcRoot + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            if (old != null) { EditorUtility.CopySerialized(arr, old); EditorUtility.SetDirty(old); Object.DestroyImmediate(arr); }
            else AssetDatabase.CreateAsset(arr, path);
            log.AppendLine($"  {name}: {first.width} x {first.height} x {slices.Length} slices, {first.format}");
            return true;
        }

        static void CopySet(string colour, string key)
        {
            string dir = Path.GetDirectoryName(colour);
            Copy(colour, key, TexKind.Colour);
            Copy(Path.Combine(dir, key + Suffix + "_normal.png"), key + TextureHD.NormalSuffix, TexKind.Normal);
            Copy(Path.Combine(dir, key + Suffix + "_rough.png"), key + TextureHD.RoughSuffix, TexKind.Data);
            Copy(Path.Combine(dir, key + Suffix + "_ao.png"), key + TextureHD.AOSuffix, TexKind.Data);
            // tex4: an optional night-window mask (the shopfronts), as the
            // Classic masks are imported - linear, exact, point, no mips - but
            // never capped at 256 (TextureHD.NightSuffix)
            string night = Path.Combine(dir, key + Suffix + TextureHD.NightSuffix + ".png");
            string nd = TextureHD.EditorSrcRoot + key + TextureHD.NightSuffix + ".png";
            if (!File.Exists(night)) { if (File.Exists(nd)) AssetDatabase.DeleteAsset(nd); return; }
            if (!File.Exists(nd) || new FileInfo(nd).Length != new FileInfo(night).Length || File.GetLastWriteTimeUtc(nd) < File.GetLastWriteTimeUtc(night))
                File.Copy(night, nd, true);
            AssetDatabase.ImportAsset(nd, ImportAssetOptions.ForceSynchronousImport);
            var imp = AssetImporter.GetAtPath(nd) as TextureImporter;
            if (imp == null) return;
            bool dirty = false;
            void Want<T>(T now, T want, System.Action<T> set) { if (!EqualityComparer<T>.Default.Equals(now, want)) { set(want); dirty = true; } }
            Want(imp.textureType, TextureImporterType.Default, v => imp.textureType = v);
            Want(imp.sRGBTexture, false, v => imp.sRGBTexture = v);
            Want(imp.mipmapEnabled, false, v => imp.mipmapEnabled = v);
            Want(imp.filterMode, FilterMode.Point, v => imp.filterMode = v);
            Want(imp.alphaSource, TextureImporterAlphaSource.FromInput, v => imp.alphaSource = v);
            Want(imp.npotScale, TextureImporterNPOTScale.None, v => imp.npotScale = v);
            Want(imp.maxTextureSize, 4096, v => imp.maxTextureSize = v);
            Want(imp.textureCompression, TextureImporterCompression.Uncompressed, v => imp.textureCompression = v);
            var ps = imp.GetPlatformTextureSettings("Standalone");
            if (!ps.overridden || ps.format != TextureImporterFormat.RGBA32 || ps.maxTextureSize != 4096)
            {
                ps.overridden = true; ps.format = TextureImporterFormat.RGBA32; ps.maxTextureSize = 4096;
                imp.SetPlatformTextureSettings(ps);
                dirty = true;
            }
            if (dirty) imp.SaveAndReimport();
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

        static void Copy(string src, string name, TexKind kind, bool readable = false)
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
            // a facade slice is stacked into an array (CPU copy): readable, not streamed
            Want(imp.streamingMipmaps, !readable, v => imp.streamingMipmaps = v);
            Want(imp.isReadable, readable, v => imp.isReadable = v);
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
                                && string.IsNullOrEmpty(map.facadeParts) && string.IsNullOrEmpty(map.gutter)
                                && (map.facadeCols == null || System.Array.IndexOf(map.facadeCols, true) < 0))) return null;
            var names = new List<string>();
            var paths = new List<string>();
            // the facade columns' three arrays
            if (map.facadeCols != null && System.Array.IndexOf(map.facadeCols, true) >= 0)
                foreach (var k in new[] { "_col", "_nrm", "_mask" })
                {
                    string ap = TextureHD.EditorSrcRoot + TextureHDMap.FacadeArrayKey + k + ".asset";
                    if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(ap) != null) { names.Add(TextureHDMap.FacadeArrayKey + k); paths.Add(ap); }
                }
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
                foreach (var suffix in new[] { "", TextureHD.NormalSuffix, TextureHD.RoughSuffix, TextureHD.AOSuffix, TextureHD.NightSuffix })
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
