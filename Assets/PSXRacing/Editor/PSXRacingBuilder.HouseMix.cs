using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE HOUSE MIX'S MATERIALS (2026-11-15; CityMeshes.HouseWalls.cs, "HOUSE
    /// MIX"): a house's red brick, old brick, painted brick and stone.
    ///
    /// THE OWNER'S PBR BRICKS PACK IS THE BRICK ("Why aren't my PBR bricks ...
    /// added?", 2026-11-15; asked for on 11-08 and 11-11). The pack is a store
    /// file, so it is never committed (the repo is public): tools/store-assets.ps1
    /// extracts it from the local Asset Store cache into Assets/StoreAssets/PBRBricks
    /// (gitignored), and at KIT-BUILD TIME this makes the three texture tiers
    /// from it, none of them committed:
    ///   HD        &lt;project&gt;/StoreGen/HouseMixHD/&lt;name&gt;_overhaul1_hd.png + _normal
    ///             + _rough + _ao, 1024 px: the pack's own colour, normal and AO maps
    ///             (roughness from its height map); TextureHDTool.Import reads
    ///             that folder with the other HD sets;
    ///   ENHANCED  Assets/PSXRacing/EnhancedSrc/TexEnhanced/StoreGen/HouseMix, 512 px
    ///             (the Enhanced bundle; rows in Assets/StoreAssets/Gen/
    ///             TextureTierManifest_store.txt, which TextureTierTool reads
    ///             beside the committed manifest);
    ///   CLASSIC   Assets/StoreAssets/Gen/HouseMix, 256 px: the kit materials' textures.
    /// Which set goes where:
    ///   Brick02          the RED BRICK (houses after 1945, brick fronts). Its
    ///                    4096 px tile is 3.46 bricks wide, so its own wrap puts
    ///                    a 1.5-brick and a half-brick in alternate courses: cut
    ///                    to 3 whole bricks (x 100..3649, the cut inside brick
    ///                    faces, cross-faded over 64 px) and 8 courses, toned
    ///                    from the pack's orange to a Carolina red.
    ///   Brick01_TileMed  the OLD BRICK (pre-war houses, part of the ranches,
    ///                    chimneys): whole, its broad grey band evened out;
    ///                    and, painted over, the PAINTED BRICK (its relief under
    ///                    one coat: the colour from its height and AO maps).
    ///   Brick01_TileDown (a wall's foot going grey: does not repeat upward),
    ///   Brick03, Brick04 (derelict: plaster patches, spalled faces): not used.
    /// WITHOUT THE PACK (another sandbox, the web, a phone build): in-repo
    /// brick that already has Classic and Enhanced versions - the kit's own
    /// city_facade_brick, the pizzeria's brick_modern_03 and _04. The stone is
    /// in-repo in both cases (the pack has none): the stage walls' coursed stone.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string HmPackDir = "Assets/StoreAssets/PBRBricks";
        const string HmGenDir = "Assets/StoreAssets/Gen/HouseMix";
        const string HmEnhDir = "Assets/PSXRacing/EnhancedSrc/TexEnhanced/StoreGen/HouseMix";
        /// <summary>Rows for TextureTierTool, in the committed manifest's format (see the class).</summary>
        public const string HouseMixStoreManifest = "Assets/StoreAssets/Gen/TextureTierManifest_store.txt";
        /// <summary>Where the pack's HD sets are written (outside Assets: TextureHDTool.Import copies them in).</summary>
        public static string HouseMixHdStage => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "StoreGen", "HouseMixHD");
        const string HmVersion = "housemix-1";
        const int HmHd = 1024;
        static readonly string[] HmNames = { "house_brick_red", "house_brick_old", "house_brick_paint" };

        /// <summary>The last kit's brick came from the pack (CityKit.houseMixPack).</summary>
        static bool hmPackUsed;

        /// <summary>The four house masonry materials into the kit's slots.</summary>
        static void HouseMixMaterials(Material[] m)
        {
            bool pack = false; string why = "PSX_CITY_HOUSEPACK=0";
            if (System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEPACK") != "0")
            {
                try { pack = HouseMixFromPack(out why); }
                catch (System.Exception ex) { why = "the pack's textures failed: " + ex.Message; Debug.LogException(ex); }
            }
            hmPackUsed = pack;
            Log(pack ? "  house mix: the owner's PBR Bricks pack (Brick02 = red brick, Brick01_TileMed = old brick + painted brick) - " + why
                     : "  house mix: NO PACK - every house stays sided (in-repo brick on the slots only; " + why + ")");
            m[(int)CityMeshes.Slot.FacadeBrick] = HmMat("CityFacadeBrick", pack ? HmGenDir + "/house_brick_red.png" : CityTexDir + "/city_facade_brick.jpg");
            m[(int)CityMeshes.Slot.FacadeMid] = HmMat("CityHouseBrickOld", pack ? HmGenDir + "/house_brick_old.png" : Root + "/Art/LifeSim/Pizzeria/Textures/brick_modern_03.jpg");
            m[(int)CityMeshes.Slot.HousePaint] = HmMat("CityHousePaint", pack ? HmGenDir + "/house_brick_paint.png" : Root + "/Art/LifeSim/Pizzeria/Textures/brick_modern_04.jpg");
            m[(int)CityMeshes.Slot.FacadeTower] = HmMat("CityHouseStone", Root + "/Art/Roads/T (3).jpg");
        }

        static Material HmMat(string name, string tex)
        {
            var mat = WithNightWindows(MakeMat(name, tex), null);
            if (mat != null && mat.HasProperty("_HouseTint")) mat.SetFloat("_HouseTint", 1f);   // the house's brick / paint / stone colour (vertex colour)
            if (mat != null) EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Batch: the city kit alone (the house mix's materials and, with the pack
        /// extracted, its textures): -executeMethod PSXRacing.EditorTools.PSXRacingBuilder.HouseMixKit.
        /// Writes housemix_kit_log.txt at the project root.</summary>
        public static void HouseMixKit()
        {
            int code = HouseMixKitBuild() ? 0 : 1;
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The city kit rebuilt in this editor (no exit): a picture tool's first step
        /// in a sandbox another branch's jobs also build the kit in.</summary>
        public static bool HouseMixKitBuild()
        {
            log = new System.Text.StringBuilder();
            bool ok = true;
            try
            {
                psxLit = Shader.Find("PSX/Lit");
                if (psxLit == null) throw new System.Exception("PSX/Lit shader not found");
                var kit = EnsureCityKit();
                CityMeshes.HouseMixForget();
                Log("city kit: " + kit.slots.Length + " slots; house brick " + AssetDatabase.GetAssetPath(kit.slots[(int)CityMeshes.Slot.FacadeBrick].mainTexture) + "; from the pack " + kit.houseMixPack);
                Log("HOUSEMIX KIT OK");
            }
            catch (System.Exception ex) { Log("HOUSEMIX KIT FAILED " + ex); ok = false; }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "housemix_kit_log.txt"), log.ToString());
            return ok;
        }

        // =====================================================================
        //  THE PACK'S TEXTURES, THREE TIERS
        // =====================================================================
        /// <summary>Make (or keep, when up to date) the three tiers of the house brick from
        /// the pack. False: the pack is not extracted (<paramref name="why"/>).</summary>
        static bool HouseMixFromPack(out string why)
        {
            string[] need = { "Brick02.png", "Brick02_N.png", "Brick02_AO.png", "Brick02_DISP.png",
                              "Brick01_TileMed_D.png", "Brick01_TileMed_N.png", "Brick01_TileMed_AO.png", "Brick01_TileMed_DISP.png" };
            long sum = 0;
            foreach (var f in need)
            {
                string p = HmPackDir + "/" + f;
                if (!File.Exists(p)) { why = p + " is not here: run tools/store-assets.ps1"; return false; }
                sum += new FileInfo(p).Length;
            }
            string stage = HouseMixHdStage, stampFile = HmGenDir + "/housemix_gen.txt", stamp = HmVersion + ";" + sum;
            bool fresh = File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == stamp && File.Exists(HouseMixStoreManifest);
            foreach (var n in HmNames)
                fresh &= File.Exists(HmGenDir + "/" + n + ".png") && File.Exists(HmEnhDir + "/" + n + ".png")
                      && File.Exists(Path.Combine(stage, n + "_overhaul1_hd.png")) && File.Exists(Path.Combine(stage, n + "_overhaul1_hd_normal.png"))
                      && File.Exists(Path.Combine(stage, n + "_overhaul1_hd_rough.png")) && File.Exists(Path.Combine(stage, n + "_overhaul1_hd_ao.png"));
            if (fresh) { why = "tiers up to date"; return true; }

            Directory.CreateDirectory(HmGenDir); Directory.CreateDirectory(HmEnhDir); Directory.CreateDirectory(stage);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // ---- Brick02: 3 whole bricks x 8 courses (see the class)
            const float cx = 100f / 4096f, cw = 3549f / 4096f, bl = 64f / 4096f;
            {
                var col = HmSample(HmPackDir + "/Brick02.png", cx, cw, bl, HmHd);
                HmGrade(col, 0.85f, new Vector3(0.78f, 0.78f, 0.78f));
                var nrm = HmSample(HmPackDir + "/Brick02_N.png", cx, cw, bl, HmHd); HmNormalize(nrm);
                var ao = HmSample(HmPackDir + "/Brick02_AO.png", cx, cw, bl, HmHd);
                var disp = HmSample(HmPackDir + "/Brick02_DISP.png", cx, cw, bl, HmHd);
                HmWriteSet("house_brick_red", col, nrm, ao, HmRough(disp, 0.95f, 0.80f));
            }
            // ---- Brick01_TileMed: whole; and painted over
            {
                var col = HmSample(HmPackDir + "/Brick01_TileMed_D.png", 0f, 1f, 0f, HmHd);
                HmFlatten(col, HmHd, 0.85f);
                HmGrade(col, 1.15f, new Vector3(1.0f, 0.93f, 0.88f));
                var nrm = HmSample(HmPackDir + "/Brick01_TileMed_N.png", 0f, 1f, 0f, HmHd); HmNormalize(nrm);
                var ao = HmSample(HmPackDir + "/Brick01_TileMed_AO.png", 0f, 1f, 0f, HmHd);
                var disp = HmSample(HmPackDir + "/Brick01_TileMed_DISP.png", 0f, 1f, 0f, HmHd);
                HmWriteSet("house_brick_old", col, nrm, ao, HmRough(disp, 0.95f, 0.85f));
                // one coat of paint over the same wall: an off-white, the joints in their own shade
                var paint = new float[col.Length];
                for (int i = 0; i < paint.Length; i += 3)
                {
                    float k = (0.88f + 0.12f * disp[i]) * Mathf.Lerp(1f, ao[i], 0.35f);
                    paint[i] = 0.93f * k; paint[i + 1] = 0.925f * k; paint[i + 2] = 0.90f * k;
                }
                HmWriteSet("house_brick_paint", paint, nrm, ao, HmRough(disp, 0.75f, 0.55f));
            }
            File.WriteAllText(stampFile, stamp);
            AssetDatabase.Refresh();
            var rows = new System.Text.StringBuilder("# made by PSXRacingBuilder.HouseMix from the owner's PBR Bricks pack (never committed)\n");
            foreach (var n in HmNames)
            {
                string c = HmGenDir + "/" + n + ".png", e = HmEnhDir + "/" + n + ".png";
                HmImport(c, HmHd / 4); HmImport(e, HmHd / 2);
                rows.Append(AssetDatabase.AssetPathToGUID(c)).Append('\t').Append(c).Append('\t').Append(e).Append('\t')
                    .Append(HmHd / 2).Append('\t').Append(HmHd / 2).Append('\t').Append("StoreGen/HouseMix/" + n).Append("\tRGB\tRGB\n");
            }
            File.WriteAllText(HouseMixStoreManifest, rows.ToString());
            TextureTierTool.BuildMap();   // the Enhanced twins into the tier map
            why = $"three tiers made in {sw.ElapsedMilliseconds} ms (HD sets in {stage})";
            return true;
        }

        /// <summary>A kit texture's import: as the city's own (exact, point, repeat) but with
        /// mips (a brick course is under a pixel across the street: without them the walls crawl).</summary>
        static void HmImport(string path, int max)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Point;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.maxTextureSize = max;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        /// <summary>One set's files: the HD colour, normal, AO and roughness, the Enhanced
        /// colour (half) and the Classic colour (a quarter).</summary>
        static void HmWriteSet(string name, float[] col, float[] nrm, float[] ao, float[] rough)
        {
            string stage = HouseMixHdStage;
            HmPng(Path.Combine(stage, name + "_overhaul1_hd.png"), col, HmHd);
            HmPng(Path.Combine(stage, name + "_overhaul1_hd_normal.png"), nrm, HmHd);
            HmPng(Path.Combine(stage, name + "_overhaul1_hd_ao.png"), ao, HmHd);
            HmPng(Path.Combine(stage, name + "_overhaul1_hd_rough.png"), rough, HmHd);
            var half = HmHalf(col, HmHd);
            HmPng(HmEnhDir + "/" + name + ".png", half, HmHd / 2);
            HmPng(HmGenDir + "/" + name + ".png", HmHalf(half, HmHd / 2), HmHd / 4);
        }

        static void HmPng(string path, float[] p, int n)
        {
            var px = new Color32[n * n];
            for (int i = 0, k = 0; i < px.Length; i++, k += 3)
                px[i] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(p[k] * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(p[k + 1] * 255f), 0, 255),
                                    (byte)Mathf.Clamp(Mathf.RoundToInt(p[k + 2] * 255f), 0, 255), 255);
            var t = new Texture2D(n, n, TextureFormat.RGB24, false);
            t.SetPixels32(px);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }

        /// <summary>Half size, each pixel the mean of four.</summary>
        static float[] HmHalf(float[] p, int n)
        {
            int h = n / 2;
            var o = new float[h * h * 3];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < h; i++)
                    for (int c = 0; c < 3; c++)
                        o[(j * h + i) * 3 + c] = 0.25f * (p[((2 * j) * n + 2 * i) * 3 + c] + p[((2 * j) * n + 2 * i + 1) * 3 + c]
                                                        + p[((2 * j + 1) * n + 2 * i) * 3 + c] + p[((2 * j + 1) * n + 2 * i + 1) * 3 + c]);
            return o;
        }

        /// <summary>The source's columns x0..x0+w (fractions of its width; every row) as an
        /// n x n image, 0..1 a channel: each pixel the mean of the source pixels under it; the
        /// first <paramref name="blend"/> of the cut cross-faded from the columns past its
        /// right edge, so the cut wraps.</summary>
        static float[] HmSample(string path, float x0, float w, float blend, int n)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!t.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(t); throw new System.Exception("cannot read " + path); }
            int sw = t.width, sh = t.height;
            var src = t.GetPixels32();
            Object.DestroyImmediate(t);
            var o = new float[n * n * 3];
            float sx = w * sw / n, sy = (float)sh / n, px0 = x0 * sw, pw = w * sw, pb = blend * sw;
            int sub = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(sx, sy)), 1, 8);
            float inv = 1f / (sub * sub * 255f);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (int v = 0; v < sub; v++)
                    {
                        float y = (j + (v + 0.5f) / sub) * sy - 0.5f;
                        for (int u = 0; u < sub; u++)
                        {
                            float xc = (i + (u + 0.5f) / sub) * sx;
                            HmFetch(src, sw, sh, px0 + xc - 0.5f, y, sub > 1, out float r1, out float g1, out float b1);
                            if (xc < pb)
                            {
                                HmFetch(src, sw, sh, px0 + pw + xc - 0.5f, y, sub > 1, out float r2, out float g2, out float b2);
                                float k = xc / pb;
                                r1 = r2 + (r1 - r2) * k; g1 = g2 + (g1 - g2) * k; b1 = b2 + (b1 - b2) * k;
                            }
                            r += r1; g += g1; b += b1;
                        }
                    }
                    int o3 = (j * n + i) * 3;
                    o[o3] = r * inv; o[o3 + 1] = g * inv; o[o3 + 2] = b * inv;
                }
            return o;
        }

        static void HmFetch(Color32[] src, int sw, int sh, float x, float y, bool nearest, out float r, out float g, out float b)
        {
            if (nearest)
            {
                int xi = ((Mathf.RoundToInt(x) % sw) + sw) % sw, yi = ((Mathf.RoundToInt(y) % sh) + sh) % sh;
                var c = src[yi * sw + xi];
                r = c.r; g = c.g; b = c.b;
                return;
            }
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            int xa = ((x0 % sw) + sw) % sw, xb = (xa + 1) % sw, ya = ((y0 % sh) + sh) % sh, yb = (ya + 1) % sh;
            var c00 = src[ya * sw + xa]; var c10 = src[ya * sw + xb]; var c01 = src[yb * sw + xa]; var c11 = src[yb * sw + xb];
            r = Mathf.Lerp(Mathf.Lerp(c00.r, c10.r, fx), Mathf.Lerp(c01.r, c11.r, fx), fy);
            g = Mathf.Lerp(Mathf.Lerp(c00.g, c10.g, fx), Mathf.Lerp(c01.g, c11.g, fx), fy);
            b = Mathf.Lerp(Mathf.Lerp(c00.b, c10.b, fx), Mathf.Lerp(c01.b, c11.b, fx), fy);
        }

        /// <summary>Saturation about the pixel's own grey, then a multiplier.</summary>
        static void HmGrade(float[] p, float sat, Vector3 mul)
        {
            for (int i = 0; i < p.Length; i += 3)
            {
                float l = 0.3f * p[i] + 0.59f * p[i + 1] + 0.11f * p[i + 2];
                p[i] = Mathf.Clamp01((l + (p[i] - l) * sat) * mul.x);
                p[i + 1] = Mathf.Clamp01((l + (p[i + 1] - l) * sat) * mul.y);
                p[i + 2] = Mathf.Clamp01((l + (p[i + 2] - l) * sat) * mul.z);
            }
        }

        /// <summary>A resampled normal map: unit length again.</summary>
        static void HmNormalize(float[] p)
        {
            for (int i = 0; i < p.Length; i += 3)
            {
                var v = new Vector3(p[i] * 2f - 1f, p[i + 1] * 2f - 1f, p[i + 2] * 2f - 1f);
                if (v.z < 0.05f) v.z = 0.05f;
                v.Normalize();
                p[i] = v.x * 0.5f + 0.5f; p[i + 1] = v.y * 0.5f + 0.5f; p[i + 2] = v.z * 0.5f + 0.5f;
            }
        }

        /// <summary>Roughness from the height map: the joints (low) rougher than the faces.</summary>
        static float[] HmRough(float[] disp, float joint, float face)
        {
            var o = new float[disp.Length];
            for (int i = 0; i < o.Length; i += 3) { float k = Mathf.Lerp(joint, face, disp[i]); o[i] = k; o[i + 1] = k; o[i + 2] = k; }
            return o;
        }

        /// <summary>Even out the broad bands of a photo-made tile: each row, then each
        /// column, is scaled toward the image's mean brightness by its own mean smoothed
        /// over a third of the tile (round the wrap), so courses and bricks keep their
        /// contrast and no band a third of the tile wide repeats up a wall.</summary>
        static void HmFlatten(float[] p, int n, float strength)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                var mean = new double[n];
                double all = 0;
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int k = (j * n + i) * 3;
                        double l = 0.3 * p[k] + 0.59 * p[k + 1] + 0.11 * p[k + 2];
                        mean[axis == 0 ? j : i] += l; all += l;
                    }
                all /= (double)n * n;
                int rad = n / 6;
                var gain = new float[n];
                for (int a = 0; a < n; a++)
                {
                    double s = 0;
                    for (int d = -rad; d <= rad; d++) s += mean[((a + d) % n + n) % n];
                    s /= (2 * rad + 1) * (double)n;
                    gain[a] = Mathf.Lerp(1f, Mathf.Clamp((float)(all / System.Math.Max(s, 1e-4)), 0.75f, 1.33f), strength);
                }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int k = (j * n + i) * 3; float g = gain[axis == 0 ? j : i];
                        p[k] = Mathf.Clamp01(p[k] * g); p[k + 1] = Mathf.Clamp01(p[k + 1] * g); p[k + 2] = Mathf.Clamp01(p[k + 2] * g);
                    }
            }
        }
    }
}
