using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WARDROBE: every seasonable material baked five ways, and the
    /// component that puts the right one on at load.
    ///
    /// The game was baked in October. The forest is maples in colour, the
    /// near ground is gravel under a warm tint, the far slopes are an orange
    /// mottle — and the LifeSim's calendar walked through May with all of it
    /// still orange. So each of those materials now has a variant per dress
    /// (winter, spring, summer, fall, snow — <see cref="Seasons"/>), made
    /// here at bake time from the same CC0 tree pack and the same procedural
    /// textures, and <see cref="SeasonDress"/> swaps them in by the day.
    ///
    /// Baked, not tinted at runtime, for two reasons. A texture cannot be
    /// tinted into another texture — an orange maple tinted green is a brown
    /// maple, and the whole point is a GREEN maple. And tinting a shared
    /// material in play mode edits the asset in the editor; five materials
    /// cost five assets and touch nothing else.
    ///
    /// The fall variant is always the material the meshes were baked with,
    /// so a scene that never loads a dress looks exactly as it did.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        static readonly string[] DressSuffix = { "Winter", "Spring", "Summer", "Fall", "Snow" };

        // ------------------------------------------------------------------
        //  Trees
        // ------------------------------------------------------------------

        /// <summary>
        /// The pack's billboards per dress, in ATLAS CELL ORDER — the same
        /// sixteen slots <see cref="StageTrees"/> fills for the fall: twelve
        /// broadleaf crowns (three palette groups the cluster picker walks),
        /// three conifers, one bare. The forest mesh bakes a CELL per tree,
        /// not a species, so swapping the atlas re-dresses every tree in
        /// place — a slot that was an orange maple in October is a green one
        /// in July and a bare one in January, at the same height on the same
        /// slope.
        ///
        /// Chosen off a contact sheet of all 120, not off file names: the
        /// pack has no manifest. Winter keeps three brown-leaved oaks (012,
        /// 013, 015) among the bare crowns because a southern Appalachian
        /// ridge is never entirely bare; snow swaps the dark spruces for the
        /// pack's white ones (065, 056, 062).
        /// </summary>
        static readonly string[][] DressTreeFiles =
        {
            // Winter: bare hardwoods and a few holding brown, dark conifers.
            new[] { "tree001", "tree002", "tree003", "tree005", "tree006", "tree007",
                    "tree009", "tree047", "tree012", "tree013", "tree015", "tree004",
                    "tree057", "tree061", "tree063", "tree008" },
            // Spring: fresh greens, and NOT blossom.
            //
            // The first pass put the pack's flowering trees here — tree069,
            // tree068 and tree071, which measure 90%, 50% and 64% pink by the
            // blue-over-green test — and two of the three landed in the RED
            // cells. Those are the CLUSTERED slots (PickSpecies sends a whole
            // noise clump to RedGroup), so entire hillsides came out magenta:
            // "the trees are too pink and purple, that is very uncommon in NC
            // Appalachian mountains". It is: redbud is a piedmont tree and
            // serviceberry and dogwood are scattered understory, not canopy.
            // What a Carolina ridge actually does in April is turn a dozen
            // shades of new green at once, so the red cells now carry the
            // PALEST greens in the pack (090, 089, 092) and a cluster reads as
            // new growth catching the light. Nothing here measures above zero
            // pink.
            new[] { "tree084", "tree118", "tree090", "tree085", "tree083", "tree089",
                    "tree087", "tree092", "tree098", "tree018", "tree093", "tree106",
                    "tree066", "tree057", "tree061", "tree010" },
            // Summer: full canopy.
            new[] { "tree116", "tree099", "tree110", "tree097", "tree100", "tree088",
                    "tree106", "tree103", "tree107", "tree018", "tree027", "tree112",
                    "tree066", "tree057", "tree061", "tree082" },
            // Fall: StageTrees, the atlas as it has always been baked.
            null,
            // Snow: winter's crowns under white spruce.
            new[] { "tree001", "tree002", "tree003", "tree005", "tree006", "tree007",
                    "tree009", "tree047", "tree012", "tree013", "tree015", "tree004",
                    "tree065", "tree056", "tree062", "tree008" },
        };

        static string DressAtlasPath(int dress) =>
            StageGenDir + (dress == (int)Season.Fall ? "/TreeAtlas.png" : "/TreeAtlas_" + DressSuffix[dress] + ".png");

        /// <summary>Copy the extra seasons' billboards out of the pack, the way
        /// EnsureStageArt copies the fall's.</summary>
        static void EnsureSeasonTrees()
        {
            int copied = 0;
            foreach (var files in DressTreeFiles)
            {
                if (files == null) continue;
                foreach (var f in files)
                {
                    string dst = ProjectRootPath(StageArtDir + "/Trees/" + f + ".png");
                    if (File.Exists(dst)) continue;
                    string src = Path.Combine(TreesSrcDir, f + ".png");
                    if (!File.Exists(src)) throw new System.Exception("Tree source missing: " + src);
                    File.Copy(src, dst);
                    copied++;
                }
            }
            if (copied > 0) Log($"Copied {copied} seasonal tree billboards from the CC0 pack.");
        }

        /// <summary>The four atlases the fall one does not cover. Same 4x4 of
        /// 128 px cells, same cell order, so the forest mesh's UVs are right
        /// for all five.</summary>
        static void ComposeSeasonAtlases()
        {
            for (int d = 0; d < Seasons.DressCount; d++)
            {
                var files = DressTreeFiles[d];
                if (files == null) continue;
                string atlasPath = DressAtlasPath(d);
                if (File.Exists(ProjectRootPath(atlasPath))) continue;
                ComposeTreeAtlas(atlasPath, files, liftBare: d == (int)Season.Winter || d == Seasons.DressSnow);
            }
        }

        /// <param name="liftBare">Brighten the dark branch pixels of the
        /// twelve broadleaf cells toward grey-brown. The pack's bare crowns
        /// are photographed against the sky and come in near-black; a whole
        /// mountainside of them over the winter mottle reads as a burn, not
        /// a January ridge, which is grey-brown at any distance. Leaves that
        /// are still on (the brown oaks) are brighter than the threshold and
        /// are left alone.</param>
        static void ComposeTreeAtlas(string atlasPath, IList<string> files, bool liftBare = false)
        {
            const int cellPx = 128, atlasPx = cellPx * 4;
            var px = new Color32[atlasPx * atlasPx];
            var bareTone = new Color32(122, 108, 92, 255);
            for (int i = 0; i < files.Count && i < 16; i++)
            {
                int col = i % 4, row = i / 4;
                bool lift = liftBare && i < 12;
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                src.LoadImage(File.ReadAllBytes(ProjectRootPath(StageArtDir + "/Trees/" + files[i] + ".png")));
                var sp = src.GetPixels32();
                int sw = src.width, sh = src.height;
                for (int y = 0; y < cellPx; y++)
                    for (int x = 0; x < cellPx; x++)
                    {
                        int sx = Mathf.Clamp(x * sw / cellPx, 0, sw - 1);
                        int sy = Mathf.Clamp(y * sh / cellPx, 0, sh - 1);
                        var c = sp[sy * sw + sx];
                        if (lift && c.a > 0)
                        {
                            int v = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                            if (v < 96)
                            {
                                // Darker pixels get more of the lift.
                                float t = 0.55f * (1f - v / 96f);
                                c = new Color32((byte)Mathf.Lerp(c.r, bareTone.r, t),
                                                (byte)Mathf.Lerp(c.g, bareTone.g, t),
                                                (byte)Mathf.Lerp(c.b, bareTone.b, t), c.a);
                            }
                        }
                        px[(row * cellPx + y) * atlasPx + col * cellPx + x] = c;
                    }
                UnityEngine.Object.DestroyImmediate(src);
            }
            var tex = new Texture2D(atlasPx, atlasPx, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(ProjectRootPath(atlasPath), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
            Log("Tree atlas composed: " + atlasPath);
        }

        // ------------------------------------------------------------------
        //  Ground
        // ------------------------------------------------------------------

        /// <summary>The far-slope mottle per dress. FallMottle.png is the one
        /// the stage has always painted its distance with; these are the same
        /// five-band noise in the other palettes.</summary>
        static string DressMottlePath(int dress) =>
            StageGenDir + (dress == (int)Season.Fall ? "/FallMottle.png" : "/Mottle_" + DressSuffix[dress] + ".png");

        /// <summary>The near-ground turf per dress, or null where the dress
        /// keeps the gravel the stage was baked with (fall).</summary>
        static string DressTurfPath(int dress) =>
            dress == (int)Season.Fall ? null : StageGenDir + "/Turf_" + DressSuffix[dress] + ".png";

        // Five bands each, brightest clump first, matching the thresholds in
        // the fall mottle: n1 > 1.15, > 0.6, > 0.1, > -0.65, else.
        static readonly Color32[][] MottleBands =
        {
            // Winter: leaf litter and bare crowns, spruce in the hollows.
            new[] { new Color32(112, 98, 80, 255), new Color32(96, 84, 70, 255), new Color32(86, 78, 66, 255),
                    new Color32(72, 68, 58, 255), new Color32(44, 58, 42, 255) },
            // Spring: fresh greens. The brightest band was a pink-grey to
            // match the blossom the atlas no longer has, and at 150 m it was
            // painting whole distant ridges lilac. New growth instead.
            new[] { new Color32(158, 176, 96, 255), new Color32(140, 164, 90, 255), new Color32(120, 150, 80, 255),
                    new Color32(92, 120, 66, 255), new Color32(48, 66, 42, 255) },
            // Summer: canopy.
            new[] { new Color32(88, 124, 56, 255), new Color32(70, 112, 50, 255), new Color32(58, 98, 46, 255),
                    new Color32(50, 84, 44, 255), new Color32(40, 60, 38, 255) },
            // Fall: the existing FallMottle, kept for the table's shape.
            new[] { new Color32(146, 64, 34, 255), new Color32(164, 108, 36, 255), new Color32(140, 114, 40, 255),
                    new Color32(82, 90, 42, 255), new Color32(48, 64, 40, 255) },
            // Snow: white slopes, the conifers the only dark thing on them.
            new[] { new Color32(238, 240, 244, 255), new Color32(224, 228, 234, 255), new Color32(210, 216, 226, 255),
                    new Color32(190, 198, 210, 255), new Color32(52, 66, 50, 255) },
        };

        // Turf: base, clump, blade, and how many dark specks.
        struct TurfPalette { public Color32 baseCol, clump, blade, speck; public float speckAmt; }
        static readonly TurfPalette[] Turfs =
        {
            new TurfPalette { baseCol = new Color32(118, 104, 78, 255), clump = new Color32(92, 82, 64, 255),
                              blade = new Color32(140, 126, 96, 255), speck = new Color32(70, 60, 48, 255), speckAmt = 0.06f },
            new TurfPalette { baseCol = new Color32(96, 132, 58, 255), clump = new Color32(72, 112, 50, 255),
                              blade = new Color32(128, 156, 80, 255), speck = new Color32(60, 84, 40, 255), speckAmt = 0.04f },
            new TurfPalette { baseCol = new Color32(62, 104, 48, 255), clump = new Color32(48, 86, 40, 255),
                              blade = new Color32(86, 128, 58, 255), speck = new Color32(40, 66, 34, 255), speckAmt = 0.04f },
            default,
            new TurfPalette { baseCol = new Color32(222, 226, 232, 255), clump = new Color32(200, 208, 220, 255),
                              blade = new Color32(240, 242, 246, 255), speck = new Color32(96, 86, 74, 255), speckAmt = 0.02f },
        };

        static void WriteSeasonGroundTextures()
        {
            for (int d = 0; d < Seasons.DressCount; d++)
            {
                if (d == (int)Season.Fall) continue;
                WriteMottle(DressMottlePath(d), MottleBands[d]);
                WriteTurf(DressTurfPath(d), Turfs[d]);
            }
        }

        /// <summary>The fall mottle's noise in another palette. Same
        /// incommensurate frequencies for the same reason: a dominant term
        /// draws a stripe across every ridge.</summary>
        static void WriteMottle(string path, Color32[] bands)
        {
            WriteTexture(path, 256, 256, (x, y) =>
            {
                float u = x * (Mathf.PI * 2f / 256f), v = y * (Mathf.PI * 2f / 256f);
                float n1 = Mathf.Sin(u * 3f + 1.3f) * Mathf.Cos(v * 2f)
                         + 0.8f * Mathf.Sin(u * 5f - v * 3f + 0.7f) * Mathf.Cos(u * 2f + v * 4f)
                         + 0.6f * Mathf.Cos(u * 7f + v * 5f + 2.9f) * Mathf.Sin(v * 3f - u * 1f);
                float n2 = Mathf.Sin(u * 13f + 2.3f) * Mathf.Cos(v * 11f - 1.1f)
                         + 0.5f * Mathf.Sin(u * 23f - v * 17f);
                Color32 c;
                if (n1 > 1.15f) c = bands[0];
                else if (n1 > 0.6f) c = bands[1];
                else if (n1 > 0.1f) c = bands[2];
                else if (n1 > -0.65f) c = bands[3];
                else c = bands[4];
                int g = (int)(n2 * 10f);
                return new Color32((byte)Mathf.Clamp(c.r + g, 0, 255),
                                   (byte)Mathf.Clamp(c.g + g, 0, 255),
                                   (byte)Mathf.Clamp(c.b + g, 0, 255), 255);
            });
        }

        /// <summary>
        /// A grass-and-litter ground for the near band. The stage's own near
        /// ground is GRAVEL under a warm tint — cut aggregate, right for a
        /// parkway verge in October and the thing the owner read as "the
        /// grass was brown" in May. This is the grass: a low-frequency clump
        /// field over a base, fine grain so a 12 m cell does not band, and a
        /// scatter of dark specks for the litter. Seamless by construction.
        /// </summary>
        static void WriteTurf(string path, TurfPalette p)
        {
            WriteTexture(path, 128, 128, (x, y) =>
            {
                float u = x * (Mathf.PI * 2f / 128f), v = y * (Mathf.PI * 2f / 128f);
                float clump = Mathf.Sin(u * 2f + 0.4f) * Mathf.Cos(v * 3f + 1.1f)
                            + 0.7f * Mathf.Sin(u * 5f - v * 2f + 2.2f)
                            + 0.5f * Mathf.Cos(u * 3f + v * 7f + 0.6f);
                float grain = Mathf.Sin(u * 17f + v * 11f) * Mathf.Cos(u * 13f - v * 19f + 0.9f)
                            + 0.6f * Mathf.Sin(u * 29f - v * 23f);
                Color32 c = clump > 0.9f ? p.blade : clump < -0.8f ? p.clump : p.baseCol;
                // Specks off a hash, not the noise, so they do not line up
                // with the clumps.
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                if ((h % 1000u) < p.speckAmt * 1000f) c = p.speck;
                int g = (int)(grain * 7f);
                return new Color32((byte)Mathf.Clamp(c.r + g, 0, 255),
                                   (byte)Mathf.Clamp(c.g + g, 0, 255),
                                   (byte)Mathf.Clamp(c.b + g, 0, 255), 255);
            });
        }

        // ------------------------------------------------------------------
        //  Tints, for grounds that keep their texture and change colour
        // ------------------------------------------------------------------

        enum GroundKind { None, Dirt, Grass }

        /// <summary>What a ground texture IS, off its path, which decides
        /// whether a season can do anything with it. Concrete and asphalt
        /// do not have seasons.</summary>
        static GroundKind GroundKindOf(string texPath)
        {
            if (string.IsNullOrEmpty(texPath)) return GroundKind.None;
            string f = Path.GetFileName(texPath);
            if (f.Contains("Grass") || f.Contains("grass") || f.Contains("Scrub")) return GroundKind.Grass;
            if (f == "Ground.jpg" || f == "T (5).jpg") return GroundKind.Dirt;
            return GroundKind.None;
        }

        // Winter, Spring, Summer, Fall, Snow. Fall is the baked look.
        static readonly Color[] DirtTints =
        {
            new Color(0.92f, 0.90f, 0.90f), new Color(0.85f, 1.00f, 0.72f), new Color(0.72f, 0.92f, 0.60f),
            Color.white, new Color(1.8f, 1.8f, 1.9f),
        };
        static readonly Color[] GrassTints =
        {
            new Color(0.82f, 0.78f, 0.66f), new Color(0.95f, 1.00f, 0.85f), Color.white,
            new Color(1.00f, 0.92f, 0.72f), new Color(1.8f, 1.8f, 1.9f),
        };

        // ------------------------------------------------------------------
        //  The registry, and the component
        // ------------------------------------------------------------------

        static readonly List<SeasonDress.Entry> seasonEntries = new List<SeasonDress.Entry>();

        /// <summary>Forget the previous scene's wardrobe. Every scene builder
        /// calls this at its head.</summary>
        static void ClearSeasonEntries() => seasonEntries.Clear();

        static void RegisterSeasonal(Material baseMat, Material[] variants, string role)
        {
            if (baseMat == null) return;
            seasonEntries.Add(new SeasonDress.Entry { baseMat = baseMat, variants = variants, role = role });
        }

        /// <summary>
        /// A ground that keeps its texture and changes colour with the season:
        /// five materials off one texture, the fall one being
        /// <paramref name="baseMat"/> itself. Does nothing for concrete.
        /// </summary>
        static void RegisterSeasonalGround(string key, string texPath, Material baseMat, Color baseTint,
                                           string role, float affine = 0f)
        {
            var kind = GroundKindOf(texPath);
            if (kind == GroundKind.None || baseMat == null) return;
            var tints = kind == GroundKind.Grass ? GrassTints : DirtTints;
            var variants = new Material[Seasons.DressCount];
            for (int d = 0; d < Seasons.DressCount; d++)
            {
                variants[d] = d == (int)Season.Fall
                    ? baseMat
                    : MakeMat(key + "_" + DressSuffix[d], texPath, affine: affine, tint: baseTint * tints[d]);
            }
            RegisterSeasonal(baseMat, variants, role);
        }

        /// <summary>A material that changes TEXTURE with the season — the
        /// forest atlas, the far mottle, the near turf.</summary>
        static void RegisterSeasonalTexture(string key, Material baseMat, System.Func<int, string> texFor,
                                            string role, float cutoff = 0f, Color? tint = null, float affine = 0f)
        {
            if (baseMat == null) return;
            var variants = new Material[Seasons.DressCount];
            for (int d = 0; d < Seasons.DressCount; d++)
            {
                string tex = texFor(d);
                variants[d] = d == (int)Season.Fall || string.IsNullOrEmpty(tex)
                    ? baseMat
                    : MakeMat(key + "_" + DressSuffix[d], tex, cutoff: cutoff, tint: tint, affine: affine);
            }
            RegisterSeasonal(baseMat, variants, role);
        }

        /// <summary>Put the wardrobe into the scene. Called by every outdoor
        /// scene builder just before it saves.</summary>
        static void AttachSeasonDress(bool weatherFx = true)
        {
            if (seasonEntries.Count == 0) return;
            var go = new GameObject("Season");
            var dress = go.AddComponent<SeasonDress>();
            dress.entries = seasonEntries.ToArray();
            dress.weatherFx = weatherFx;
            Log($"Season dress: {seasonEntries.Count} seasonable materials.");
        }
    }
}
