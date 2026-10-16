using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The terrain detail sheet's import (Scripts/TerrainDetail.cs): kept
    /// here by path so it is the same on every machine and every sandbox.
    /// The sheet is DATA, not colour - four luminance tiles at mean 0.5 - so
    /// it is linear (no sRGB decode), uncompressed RGBA32 on every target (a
    /// block format would bleed one class's grain into the next channel),
    /// and point-filtered like every texel in the game, WITH mips: at a 2 m
    /// repeat a 256 px tile is 8 mm a texel, and its mips are what take the
    /// grain to grey (no modulation) as the ground recedes instead of
    /// shimmering. Outside ReleaseBudget's 16-bit set (linear, unlabelled,
    /// with alpha).
    /// </summary>
    class TerrainDetailImport : AssetPostprocessor
    {
        internal const string SheetPath = "Assets/PSXRacing/Resources/TerrainDetail.png";

        void OnPreprocessTexture()
        {
            if (assetPath.Replace('\\', '/') != SheetPath) return;
            var imp = (TextureImporter)assetImporter;
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.mipmapEnabled = true;
            imp.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            imp.streamingMipmaps = false;
            imp.filterMode = FilterMode.Point;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.isReadable = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.maxTextureSize = 256;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            foreach (var platform in new[] { "WebGL", "Standalone" })
            {
                var s = imp.GetPlatformTextureSettings(platform);
                s.overridden = true;
                s.maxTextureSize = 256;
                s.format = TextureImporterFormat.RGBA32;
                s.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SetPlatformTextureSettings(s);
            }
        }
    }

    public static class TerrainDetailTool
    {
        /// <summary>The layer's promises, through the suite's Check (also
        /// run by TerrainDetailShots ahead of its frames).</summary>
        public static void SelfTest(System.Action<bool, string, object> check)
        {
            // the sheet: present, the import above, four channels at mean 0.5
            var sheet = Resources.Load<Texture2D>(TerrainDetail.ResourcePath);
            check(sheet != null, "terrain detail: the sheet is in Resources", null);
            if (sheet != null)
            {
                check(sheet.width == 256 && sheet.height == 256 && sheet.mipmapCount > 1 && sheet.filterMode == FilterMode.Point
                      && sheet.wrapMode == TextureWrapMode.Repeat && !sheet.isDataSRGB,
                      "terrain detail: sheet 256 px, mipped, point, repeat, linear",
                      $"{sheet.width}x{sheet.height} mips {sheet.mipmapCount} {sheet.filterMode} {sheet.wrapMode} sRGB {sheet.isDataSRGB} {sheet.format}");
                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (raw.LoadImage(File.ReadAllBytes(TerrainDetailImport.SheetPath)))
                {
                    var px = raw.GetPixels32();
                    var sum = new double[4];
                    foreach (var p in px) { sum[0] += p.r; sum[1] += p.g; sum[2] += p.b; sum[3] += p.a; }
                    bool flat = true;
                    string got = "";
                    for (int c = 0; c < 4; c++)
                    {
                        double mean = sum[c] / px.Length / 255.0;
                        flat &= System.Math.Abs(mean - 0.5) < 0.01;
                        got += mean.ToString("0.000") + " ";
                    }
                    check(flat, "terrain detail: every channel averages 0.5 (no net darkening or lightening)", got.Trim());
                }
                Object.DestroyImmediate(raw);
            }

            // the shader: the amounts are a material property, the sheet and
            // its switch are NOT (a global named there reads the material's 0)
            var lit = Shader.Find("PSX/Lit");
            check(lit != null && lit.FindPropertyIndex("_PSXDetailMask") >= 0
                  && lit.FindPropertyIndex("_PSXDetailTex") < 0 && lit.FindPropertyIndex("_PSXDetailParams") < 0,
                  "terrain detail: PSX/Lit has _PSXDetailMask and no property shadowing the globals", null);

            // the classes, off the texture names the stages wear
            var want = new (string, TerrainDetail.Kind)[]
            {
                ("Turf_Summer", TerrainDetail.Kind.Grass), ("Turf_Spring", TerrainDetail.Kind.Grass),
                ("Turf_Snow", TerrainDetail.Kind.Snow), ("Mottle_Snow", TerrainDetail.Kind.Snow),
                ("FallMottle", TerrainDetail.Kind.Grass), ("grass_7_city", TerrainDetail.Kind.Grass),
                ("Ground", TerrainDetail.Kind.Dirt), ("dirt_pt_5", TerrainDetail.Kind.Dirt),
                ("rock_pt_2", TerrainDetail.Kind.Rock),
                ("T (3)", TerrainDetail.Kind.None), ("asphalt_old_pt_1", TerrainDetail.Kind.None),
                ("concrete_pt_5", TerrainDetail.Kind.None), ("TreeAtlas", TerrainDetail.Kind.None),
            };
            string wrong = "";
            foreach (var (n, k) in want) if (TerrainDetail.KindOf(n) != k) wrong += n + "=" + TerrainDetail.KindOf(n) + " ";
            check(wrong.Length == 0, "terrain detail: grass/dirt/rock/snow by texture name; roads, walls, concrete, trees none", wrong.Length > 0 ? wrong : null);

            // the stamp, on Blue Ridge's own materials (when this checkout has
            // them built): right class, and the .mat is not left dirty
            string dir = "Assets/PSXRacing/Materials/";
            var stamped = new (string, TerrainDetail.Kind)[]
            {
                ("BlueRidge_Ground_Summer", TerrainDetail.Kind.Grass), ("BlueRidge_Ground", TerrainDetail.Kind.Dirt),
                ("BlueRidge_Ground_Snow", TerrainDetail.Kind.Snow), ("BlueRidge_Bank", TerrainDetail.Kind.Rock),
                ("BlueRidge_Road", TerrainDetail.Kind.None), ("BlueRidge_Wall", TerrainDetail.Kind.None),
            };
            int seen = 0;
            string bad = "";
            foreach (var (n, k) in stamped)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(dir + n + ".mat");
                if (m == null) continue;
                seen++;
                bool wasDirty = EditorUtility.IsDirty(m);
                PSXTexDecode.Stamp(m);
                if (m.GetVector(TerrainDetail.MaskId) != TerrainDetail.MaskFor(k)) bad += n + "=" + m.GetVector(TerrainDetail.MaskId) + " ";
                if (!wasDirty && EditorUtility.IsDirty(m)) bad += n + " left dirty ";
            }
            if (seen > 0) check(bad.Length == 0, $"terrain detail: Blue Ridge's grounds stamped by class, never dirtied ({seen} materials)", bad.Length > 0 ? bad : null);
        }
    }
}
