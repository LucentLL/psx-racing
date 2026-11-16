using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE STAGE HORIZON (owner, 2026-11-09, a dusk frame on NC 226A: "I still
    /// see the haze on the dusk horizon and haze on distant mountains ... This
    /// game needs to meet standard distance quality of games like Forza
    /// Horizon").
    ///
    /// Measured before anything was changed (Editor/HazeProbe, tools: the
    /// skyline script against the bake's own DEM):
    ///   * the pale band was a real ridge 2.0-2.24 km away, painted in the
    ///     end-of-view fade - which toward a low sun is the twilight's pink;
    ///   * the camera's far plane sat right behind it, 2.24 km out, at the far
    ///     ground's edge (2.3 km from the road);
    ///   * the bake's far DEM carries the real mountains ~8 km past the route
    ///     on every side, and from the stages' roads the true skyline stands
    ///     behind the drawn land in 10-44% of bearings, by 1-12 degrees, up to
    ///     15 km away.
    ///
    /// So the land is drawn to where it really ends. Three pieces, all on the
    /// Horizon layer, which the camera draws to its far plane while every
    /// layer with detail in it stops where it always did (StageCulling):
    ///
    ///   TIER 0   the far DEM at its own 60 m, from the far ground's edge to
    ///            the DEM's - on the far ground's lattice, vertex for vertex,
    ///            so the seam between them is watertight with no overlap.
    ///   APRON    past the DEM: the real land of the stage's wide grid
    ///            (tools/roads/bake_wide.mjs: the road bake's own SRTM tiles,
    ///            240 m cells, 36 km further on every side) drawn at 480 m,
    ///            sinking with the earth's curve - what stops a high road's
    ///            view ending at a cliff of sky. A stage without a wide grid
    ///            gets the DEM mirrored outward instead.
    ///   STAND-INS  the far grid has a hole along the road (the corridor is
    ///            the near ground's), and the near ground stops at the
    ///            detail's edge: the dropped quads, kept as meshes that draw
    ///            only past that edge (PSX/Lit _NearCut).
    ///
    /// The PC scene pass only (PCContent). The far ring itself moves onto the
    /// Horizon layer in every build, which changes nothing where no camera
    /// treats that layer differently.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        /// <summary>Tier 0's chunk: four far chunks a side, 64 x 64 DEM cells.</summary>
        const float HorizonChunk = FarChunk * 4f;
        const float ApronCell = 480f, ApronChunk = HorizonChunk * 4f, ApronReachM = 24000f;
        /// <summary>The corridor stand-in sits this far under the lowest
        /// ground round each of its vertices, and starts this far inside the
        /// detail's edge.</summary>
        const float StandInSink = 2f, StandInOverlapM = 400f;
        /// <summary>Chunk edges that end against a coarser tier or nothing
        /// hang a skirt this deep: no sky through a T-junction.</summary>
        const float HorizonSkirtM = 250f;
        /// <summary>Effective earth radius (standard refraction).</summary>
        const float HorizonEarthR = 6371000f * 1.15f;

        /// <summary>The far plane the horizon asks for (0: no horizon built).</summary>
        static float stageHorizonFar;

        static int HorizonLayer => LayerMask.NameToLayer(StageCulling.HorizonLayerName);

        // ---- the far land's own texture --------------------------------------
        // The mottle the far slopes always wore is a 256 px tile, point-sampled
        // with no mips - the PS1's page. Laid over a mountain 5 km away it was
        // a moire of its own grid (the first horizon shots, 2026-11-09). The PC
        // pass paints its distance with this instead: the same five colours a
        // dress, but as a continuous field with crown-sized grain, 512 px,
        // trilinear with mips (PSXTextureCaps.IsFarLand), and PSX/Lit blends it
        // with itself at a seventh of the scale (_Horizon). The web keeps the
        // mottle: nothing there is drawn past 1.5 km.
        static string FarLandPath(int dress) => StageGenDir + "/FarLand_" + DressSuffix[dress] + ".png";

        /// <summary>The far ground's texture for a dress: the far land on the
        /// PC pass, the mottle everywhere else.</summary>
        static string FarGroundPath(int dress) => PCContent ? FarLandPath(dress) : DressMottlePath(dress);

        /// <summary>The fall mottle's own five bands (GenerateStageTextures
        /// paints FallMottle.png by thresholds on these).</summary>
        static readonly Color32[] FarLandFallBands =
        {
            new Color32(146, 64, 34, 255), new Color32(164, 108, 36, 255), new Color32(140, 114, 40, 255),
            new Color32(82, 90, 42, 255), new Color32(48, 64, 40, 255),
        };

        /// <summary>How bright the far land stands against the forest it
        /// continues, per dress (Winter, Spring, Summer, Fall, Snow): set from
        /// the probe's frames, slope under trees against slope past them.</summary>
        static readonly Color[] FarLandTint =
        {
            new Color(0.88f, 0.80f, 0.68f), new Color(0.85f, 0.85f, 0.85f), new Color(0.85f, 0.85f, 0.85f),
            new Color(0.85f, 0.85f, 0.85f), new Color(0.95f, 0.95f, 0.95f),
        };

        static void WriteFarLandTextures()
        {
            if (!PCContent) return;
            for (int d = 0; d < Seasons.DressCount; d++)
            {
                var bands = d == (int)Season.Fall ? FarLandFallBands : MottleBands[d];
                Color gain = FarLandTint[d];
                string path = FarLandPath(d);
                const int N = 512;
                WriteTexture(path, N, N, (x, y) =>
                {
                    float u = x * (Mathf.PI * 2f / N), v = y * (Mathf.PI * 2f / N);
                    // the mottle's clumps (whole periods: the tile is seamless)
                    float n1 = Mathf.Sin(u * 3f + 1.3f) * Mathf.Cos(v * 2f)
                             + 0.8f * Mathf.Sin(u * 5f - v * 3f + 0.7f) * Mathf.Cos(u * 2f + v * 4f)
                             + 0.6f * Mathf.Cos(u * 7f + v * 5f + 2.9f) * Mathf.Sin(v * 3f - u * 1f);
                    // crowns: a few metres across at this tile's 300 m
                    float n2 = Mathf.Sin(u * 29f + 2.3f) * Mathf.Cos(v * 23f - 1.1f)
                             + 0.6f * Mathf.Sin(u * 53f - v * 47f)
                             + 0.5f * Mathf.Cos(u * 71f + v * 61f + 0.4f) * Mathf.Sin(v * 83f - u * 37f);
                    // a continuous run through the five bands, brightest first
                    float t = Mathf.Clamp01(Mathf.InverseLerp(1.5f, -1.3f, n1)) * 4f;
                    int i0 = Mathf.Min(3, Mathf.FloorToInt(t));
                    Color c = Color.Lerp(bands[i0], bands[i0 + 1], Mathf.SmoothStep(0f, 1f, t - i0));
                    uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                    float g = 1f + n2 * 0.055f + ((h & 255u) / 255f - 0.5f) * 0.05f;
                    c = new Color(c.r * gain.r, c.g * gain.g, c.b * gain.b);
                    return new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(c.r * g * 255f), 0, 255),
                                       (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * g * 255f), 0, 255),
                                       (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * g * 255f), 0, 255), 255);
                });
                // Its importer now, not on the next build's sweep: a texture this
                // build wrote would otherwise ship as its first import left it.
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                bool dirty = imp.filterMode != FilterMode.Trilinear || !imp.mipmapEnabled || imp.maxTextureSize != 512 ||
                             imp.textureCompression != TextureImporterCompression.Uncompressed || imp.anisoLevel != 4 ||
                             imp.wrapMode != TextureWrapMode.Repeat;
                imp.filterMode = FilterMode.Trilinear; imp.mipmapEnabled = true; imp.maxTextureSize = 512;
                imp.textureCompression = TextureImporterCompression.Uncompressed; imp.anisoLevel = 4; imp.mipMapBias = -0.4f;
                imp.wrapMode = TextureWrapMode.Repeat;
                if (dirty) imp.SaveAndReimport();
            }
        }

        delegate float HorizonHeight(float x, float z);
        /// <summary>A quad by its low corner and cell: is it this mesh's?</summary>
        delegate bool HorizonKeep(float x0, float z0, float cell);

        static float HorizonFold(float v, float lo, float hi)
        {
            float w = hi - lo;
            float t = Mathf.Repeat(v - lo, 2f * w);
            return lo + (t <= w ? t : 2f * w - t);
        }

        /// <summary>The far DEM's rectangle, a cell in from its edge (the
        /// bilinear read clamps at the last cell).</summary>
        static void HorizonDemRect(out float x0, out float x1, out float z0, out float z1)
        {
            var m = demFarMeta;
            x0 = m.originX + m.cell; x1 = m.originX + (m.cols - 2) * m.cell;
            z0 = m.originZ + m.cell; z1 = m.originZ + (m.rows - 2) * m.cell;
        }

        /// <summary>The far DEM hands over to the wide one across its last
        /// stretch (m): the far grid reads treetops at 60 m, the wide one the
        /// mean of a 240 m cell, and a seam between them is a step.</summary>
        const float WideBlendM = 1200f;

        /// <summary>The land anywhere: the stage's own field inside the far
        /// DEM; outside it the real land of the wide grid (bake_wide.mjs), or -
        /// a stage without one - the far DEM mirrored about its edges. Either
        /// way continuous across the far DEM's edge.</summary>
        static float HorizonFieldY(float x, float z)
        {
            HorizonDemRect(out float x0, out float x1, out float z0, out float z1);
            bool inside = x >= x0 && x <= x1 && z >= z0 && z <= z1;
            if (demWide == null)
                return inside ? StageGroundHeightAt(x, z)
                              : DemBilinear(demFar, demFarMeta, HorizonFold(x, x0, x1), HorizonFold(z, z0, z1));
            float wide = DemBilinear(demWide, demWideMeta, x, z);
            if (!inside) return wide;
            float edge = Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(z - z0, z1 - z));
            float t = 1f - Mathf.Clamp01(edge / WideBlendM);
            float own = StageGroundHeightAt(x, z);
            return t <= 0f ? own : Mathf.Lerp(own, wide, Mathf.SmoothStep(0f, 1f, t));
        }

        /// <summary>How far the ground has fallen away with the earth's curve,
        /// past the far ring (nothing inside it moves).</summary>
        static float HorizonDrop(float x, float z)
        {
            float d = Mathf.Max(0f, RouteDistanceCoarse(x, z) - (FarCoverage + FarChunk * 1.5f));
            return d * d / (2f * HorizonEarthR);
        }

        /// <summary>The far ring's own test (BuildStageGround): does far chunk
        /// (cx, cz) exist?</summary>
        static bool FarChunkBuilt(Bounds b, int cx, int cz)
        {
            int x0 = Mathf.FloorToInt((b.min.x - FarCoverage) / FarChunk), x1 = Mathf.CeilToInt((b.max.x + FarCoverage) / FarChunk);
            int z0 = Mathf.FloorToInt((b.min.z - FarCoverage) / FarChunk), z1 = Mathf.CeilToInt((b.max.z + FarCoverage) / FarChunk);
            if (cx < x0 || cx >= x1 || cz < z0 || cz >= z1) return false;
            float mid = RouteDistanceCoarse(cx * FarChunk + FarChunk * 0.5f, cz * FarChunk + FarChunk * 0.5f);
            return mid <= FarCoverage + FarChunk * 0.71f;
        }

        /// <summary>
        /// One horizon chunk: the kept quads of a square grid, vertices local
        /// to its origin, normals from the height function itself (so chunks
        /// agree along their borders), unused vertices never made. With
        /// <paramref name="skirt"/>, every kept edge whose neighbour is not
        /// kept - in this chunk or the next - hangs a two-sided skirt.
        /// </summary>
        static Mesh HorizonMesh(float ox, float oz, float size, float cell, float tile,
                                HorizonHeight height, HorizonKeep keep, bool skirt)
        {
            int cells = Mathf.RoundToInt(size / cell), n1 = cells + 1;
            var kept = new bool[cells * cells];
            int any = 0;
            for (int gz = 0; gz < cells; gz++)
                for (int gx = 0; gx < cells; gx++)
                    if (kept[gz * cells + gx] = keep(ox + gx * cell, oz + gz * cell, cell)) any++;
            if (any == 0) return null;

            var index = new int[n1 * n1];
            for (int i = 0; i < index.Length; i++) index[i] = -1;
            var verts = new List<Vector3>(any * 2);
            var norms = new List<Vector3>(any * 2);
            var uvs = new List<Vector2>(any * 2);
            var tris = new List<int>(any * 6);
            int V(int gx, int gz)
            {
                int i = gz * n1 + gx;
                if (index[i] >= 0) return index[i];
                float wx = ox + gx * cell, wz = oz + gz * cell, e = cell * 0.5f;
                float dhdx = (height(wx + e, wz) - height(wx - e, wz)) / (2f * e);
                float dhdz = (height(wx, wz + e) - height(wx, wz - e)) / (2f * e);
                verts.Add(new Vector3(gx * cell, height(wx, wz), gz * cell));
                norms.Add(new Vector3(-dhdx, 1f, -dhdz).normalized);
                uvs.Add(new Vector2(wx / tile, wz / tile));
                return index[i] = verts.Count - 1;
            }
            bool Kept(int gx, int gz) =>
                gx >= 0 && gx < cells && gz >= 0 && gz < cells
                    ? kept[gz * cells + gx]
                    : keep(ox + gx * cell, oz + gz * cell, cell);
            void Skirt(int a, int b)
            {
                int a2 = verts.Count, b2 = a2 + 1;
                verts.Add(verts[a] + Vector3.down * HorizonSkirtM); norms.Add(norms[a]); uvs.Add(uvs[a]);
                verts.Add(verts[b] + Vector3.down * HorizonSkirtM); norms.Add(norms[b]); uvs.Add(uvs[b]);
                tris.Add(a); tris.Add(b); tris.Add(b2); tris.Add(a); tris.Add(b2); tris.Add(a2);
                tris.Add(a); tris.Add(b2); tris.Add(b); tris.Add(a); tris.Add(a2); tris.Add(b2);
            }
            for (int gz = 0; gz < cells; gz++)
                for (int gx = 0; gx < cells; gx++)
                {
                    if (!kept[gz * cells + gx]) continue;
                    int a = V(gx, gz), b = V(gx, gz + 1), c = V(gx + 1, gz + 1), d = V(gx + 1, gz);
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    if (!skirt) continue;
                    if (!Kept(gx - 1, gz)) Skirt(a, b);
                    if (!Kept(gx + 1, gz)) Skirt(c, d);
                    if (!Kept(gx, gz - 1)) Skirt(d, a);
                    if (!Kept(gx, gz + 1)) Skirt(b, c);
                }
            var mesh = new Mesh
            {
                indexFormat = verts.Count < 65000 ? UnityEngine.Rendering.IndexFormat.UInt16
                                                  : UnityEngine.Rendering.IndexFormat.UInt32,
            };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        static GameObject HorizonGO(Transform parent, string name, Mesh mesh, Material mat, float ox, float oz, string meshName)
        {
            var go = ChunkGO(parent, name, mesh, new[] { mat }, ox, oz, meshName);
            if (HorizonLayer >= 0) go.layer = HorizonLayer;
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ---- the land at distance, from the owner's packs ---------------------
        // (owner, 2026-11-09: "should be prioritizing Meadow and Forest Dynamic
        // unity asset packs from Nature Manufacture. Especially for distant
        // mountain terrain, trees, rockface, LODs, etc since it matches the
        // Appalachian mountains closely")
        const string MeadowHillMap = "Assets/StoreAssets/Meadow/Hills/T_Meadow_Hill_01_A.png";
        /// <summary>One copy of the hill map covers this many metres (mirrored
        /// beyond), and its mean luminance as the shader reads it - LINEAR (the
        /// project is; measured off the file: 0.34 in the PNG's own sRGB).</summary>
        internal const float HillMapM = 6000f, HillMapLum = 0.0993f;
        /// <summary>Where the floor starts to turn into the canopy, and where
        /// it has (m from the eye).</summary>
        internal const float CanopyFromM = 300f, CanopyByM = 1800f;
        /// <summary>The forest seen from above, per dress (Winter, Spring,
        /// Summer, Fall, Snow). MEASURED, 2026-11-09 (Editor/HazeProbe with
        /// PSX_HAZE_CANOPY, NC 226A from 25 m over its first half kilometre at
        /// noon): two trial colours a dress, the far land's frame value fitted
        /// as a power of the colour (k 0.41-0.75 a channel), solved for the
        /// forested slope beside it. Winter is that solution as it stands. The
        /// leafed dresses are 15% under theirs and a little greyer: a flat tone
        /// at a speckled forest's exact mean reads brighter than the forest
        /// does. Snow is set by eye between the white ground and the dark
        /// crowns (the mean of the two, 0.29, is a grey no hillside is).</summary>
        static readonly Color[] HorizonCanopy =
        {
            new Color(0.216f, 0.173f, 0.109f), new Color(0.235f, 0.284f, 0.077f), new Color(0.231f, 0.293f, 0.070f),
            new Color(0.326f, 0.226f, 0.054f), new Color(0.42f, 0.41f, 0.40f),
        };

        // ---- THE FAR CANOPY (linncove, owner 2026-11-16: "It would be nice to
        // find a solution to the barren mountains in the distance on mountain
        // maps"; his title frame showed the far hills as bare, smooth brown
        // ground). PSX/Lit _HorizonCrowns paints the land past the forest as
        // crowns; these are its colours, per dress (Winter, Spring, Summer,
        // Fall, Snow), linear albedo BEFORE the dress's level is set (below).
        // Taken from the near forest's own leaf sheets (leafmeans.py over the
        // NatureManufacture sheets): the autumn beech sheets' mean is (0.156,
        // 0.082, 0.015), and the forest wears it plain, under FallTints' red
        // and gold, and late green; the oaks' russet; the bare sheets'
        // grey-brown (0.132, 0.108, 0.090) with the winter oaks' tan.
        static readonly Color[][] CanopyBroad =
        {
            new[] { new Color(0.132f, 0.108f, 0.090f), new Color(0.110f, 0.090f, 0.086f), new Color(0.150f, 0.118f, 0.082f),
                    new Color(0.122f, 0.106f, 0.096f), new Color(0.094f, 0.079f, 0.068f) },
            new[] { new Color(0.070f, 0.100f, 0.020f), new Color(0.086f, 0.116f, 0.028f), new Color(0.060f, 0.086f, 0.030f),
                    new Color(0.096f, 0.106f, 0.046f), new Color(0.054f, 0.076f, 0.034f) },
            new[] { new Color(0.040f, 0.070f, 0.018f), new Color(0.055f, 0.090f, 0.020f), new Color(0.035f, 0.060f, 0.020f),
                    new Color(0.066f, 0.096f, 0.026f), new Color(0.045f, 0.074f, 0.030f) },
            new[] { new Color(0.156f, 0.082f, 0.015f), new Color(0.150f, 0.044f, 0.012f), new Color(0.156f, 0.100f, 0.012f),
                    new Color(0.086f, 0.040f, 0.016f), new Color(0.078f, 0.092f, 0.018f) },
            new[] { new Color(0.30f, 0.29f, 0.29f), new Color(0.36f, 0.36f, 0.37f), new Color(0.25f, 0.23f, 0.22f),
                    new Color(0.40f, 0.40f, 0.41f), new Color(0.22f, 0.20f, 0.19f) },
        };
        /// <summary>Spruce, fir, hemlock and the rhododendron under them: the
        /// dark of every dress (the snow's dusted).</summary>
        static readonly Color[] CanopyConiferRaw =
        {
            new Color(0.020f, 0.032f, 0.020f), new Color(0.022f, 0.038f, 0.020f), new Color(0.018f, 0.034f, 0.018f),
            new Color(0.022f, 0.040f, 0.020f), new Color(0.050f, 0.065f, 0.055f),
        };
        /// <summary>Between the crowns: the shaded floor (the snow's is snow).</summary>
        static readonly Color[] CanopyGapCol =
        {
            new Color(0.030f, 0.025f, 0.020f), new Color(0.012f, 0.012f, 0.008f), new Color(0.008f, 0.012f, 0.006f),
            new Color(0.012f, 0.010f, 0.007f), new Color(0.55f, 0.56f, 0.59f),
        };
        /// <summary>Where the spruce-fir takes over (m above sea level), and
        /// the conifers' share below and above: hemlock and white pine a tenth
        /// of the coves, spruce-fir most of the Black Mountains and
        /// Grandfather's crest.</summary>
        const float SpruceFromM = 1450f, SpruceByM = 1750f, ConiferLow = 0.10f, ConiferHigh = 0.85f;
        /// <summary>The mean light the crowns take (PSX/Lit: AO 0.8, the domes'
        /// sun ~0.85 of a flat face's) and the gaps' share of the land.</summary>
        const float CanopyMeanLight = 0.82f, CanopyGapShare = 0.12f, CanopyConiferMid = 0.15f;

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>The level a dress's crowns are set to: their mean, lit as
        /// PSX/Lit lights them, is the measured canopy (HorizonCanopy) - so the
        /// land past the forest keeps the brightness the 2026-11-09 probe fitted
        /// to the forest beside it, and gains only its grain. The conifers
        /// rise by the root of it: they stay dark.</summary>
        static float CanopyLevel(int d)
        {
            var broad = CanopyBroad[d];
            float lb = 0f; foreach (var c in broad) lb += Lum(c); lb /= broad.Length;
            float lc = Lum(CanopyConiferRaw[d]), lg = Lum(CanopyGapCol[d]);
            float target = Lum(HorizonCanopy[Mathf.Clamp(d, 0, HorizonCanopy.Length - 1)]) / CanopyMeanLight;
            float lo = 0.1f, hi = 20f;
            for (int it = 0; it < 40; it++)
            {
                float k = 0.5f * (lo + hi);
                float mean = (1f - CanopyGapShare) * ((1f - CanopyConiferMid) * lb * k + CanopyConiferMid * lc * Mathf.Sqrt(k)) + CanopyGapShare * lg;
                if (mean < target) lo = k; else hi = k;
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>A far-land material's crowns for a dress (PSX/Lit
        /// _HorizonCrowns, _CanopyP0-4, _CanopyConifer, _CanopyGap, _CanopySpruce).</summary>
        static void SetFarCanopy(Material m, int d)
        {
            if (m == null || !m.HasProperty("_HorizonCrowns")) return;
            d = Mathf.Clamp(d, 0, CanopyBroad.Length - 1);
            float k = CanopyLevel(d), kc = Mathf.Sqrt(k);
            var broad = CanopyBroad[d];
            for (int i = 0; i < 5; i++)
            {
                var c = broad[i] * k;
                m.SetColor("_CanopyP" + i, new Color(Mathf.Min(c.r, 0.95f), Mathf.Min(c.g, 0.95f), Mathf.Min(c.b, 0.95f), 1f));
            }
            var cc = CanopyConiferRaw[d] * kc;
            m.SetColor("_CanopyConifer", new Color(cc.r, cc.g, cc.b, 1f));
            m.SetColor("_CanopyGap", CanopyGapCol[d]);
            m.SetVector("_CanopySpruce", new Vector4(SpruceFromM - stageBaseM, SpruceByM - stageBaseM, ConiferLow, ConiferHigh));
            m.SetFloat("_HorizonCrowns", 1f);
        }

        /// <summary>Every dress of a far-ground material, by dress index (the
        /// fall one is the base).</summary>
        static Material[] DressesOf(Material baseMat)
        {
            foreach (var e in seasonEntries)
                if (e != null && e.baseMat == baseMat && e.variants != null) return e.variants;
            var one = new Material[Seasons.DressCount];
            one[(int)Season.Fall] = baseMat;
            return one;
        }

        /// <summary>Make a far-ground material the horizon's land in every
        /// dress: exempt from the detail's dissolve, its canopy at distance,
        /// and - where it wears the near ground's floor - that floor at the near
        /// ground's own size (the far meshes' UVs are metres / 300, the near
        /// ground's metres / the theme's tile).</summary>
        static void HorizonDress(Material baseMat, float nearCut = 0f)
        {
            var macro = AssetDatabase.LoadAssetAtPath<Texture2D>(MeadowHillMap);
            var dresses = DressesOf(baseMat);
            for (int d = 0; d < dresses.Length; d++)
            {
                var m = dresses[d];
                if (m == null) continue;
                m.SetFloat("_Horizon", 1f);
                if (nearCut > 0f) m.SetFloat("_NearCut", nearCut);
                var c = HorizonCanopy[Mathf.Clamp(d, 0, HorizonCanopy.Length - 1)];
                m.SetTexture("_HorizonMacro", macro);
                m.SetColor("_HorizonCanopy", new Color(c.r, c.g, c.b, macro != null ? 1f : 0f));
                m.SetVector("_HorizonFade", new Vector4(CanopyFromM, CanopyByM, 1f / HillMapM, HillMapLum));
                SetFarCanopy(m, d);
                bool floor = m.mainTexture != null && !PSXTextureCaps.IsFarLand(AssetDatabase.GetAssetPath(m.mainTexture));
                if (floor && NMGroundOn)
                {
                    float k = 300f / Mathf.Max(1f, theme.groundTile);
                    float s = m.mainTexture.name.StartsWith("T_ground_beech") ? NMGroundScale : 1f;
                    m.mainTextureScale = new Vector2(s * k, s * k);
                }
                EditorUtility.SetDirty(m);
            }
            if (macro == null) Log("Stage horizon: no Meadow hill map (tools/store-assets.ps1) - the far land keeps its own tile at distance.");
        }

        /// <summary>The corridor stand-in's material: the far ground's own, per
        /// dress, each with the near cut.</summary>
        static Material HorizonStandInMat(float cut)
        {
            var baseMat = MakeMat(MeshPrefix + "GroundFarS", FarGroundPath((int)Season.Fall), affine: 0f);
            if (!(theme.stageForest && ApplyNMGround(baseMat, MeshPrefix + "GroundFarS", "far")))
                RegisterSeasonalTexture(MeshPrefix + "GroundFarS", baseMat, FarGroundPath, "far");
            HorizonDress(baseMat, cut);
            return baseMat;
        }

        /// <summary>Build the horizon under <paramref name="groundRoot"/>
        /// (BuildStageGround calls it with the far ring's material and the
        /// route's bounds). Returns the far plane it needs, 0 if none.</summary>
        static float BuildStageHorizon(Bounds b, Transform groundRoot, Material farMat)
        {
            stageHorizonFar = 0f;
            if (!PCContent || HorizonLayer < 0) return 0f;
            // A mountain's distance only (the far forest's own rule): a
            // coast's is the sea plane, a city's its own far land.
            if (!string.IsNullOrEmpty(theme.farGround) || !string.IsNullOrEmpty(theme.sand) || track.stageWaterY > 0f) return 0f;
            if (demFar == null || demFarMeta == null) return 0f;

            // The land never takes the detail's dissolve and wears its canopy at
            // distance (PSX/Lit _Horizon): the far ground's material in every dress.
            HorizonDress(farMat);

            var root = new GameObject("Horizon");
            root.transform.SetParent(groundRoot, false);
            root.AddComponent<PCOnly>();
            HorizonDemRect(out float dx0, out float dx1, out float dz0, out float dz1);

            // ---- the corridor stand-ins: the far ring's dropped quads -------
            float detail = FarCoverage - FarCell;                 // StageCulling.pcFarClip
            float drop = NearCoverage - FarCell;                  // the far ring's dropInside
            var standMat = HorizonStandInMat(Mathf.Max(200f, detail - StandInOverlapM));
            HorizonKeep standKeep = (x0, z0, c) =>
                RouteDistanceCoarse(x0, z0) < drop && RouteDistanceCoarse(x0 + c, z0) < drop &&
                RouteDistanceCoarse(x0, z0 + c) < drop && RouteDistanceCoarse(x0 + c, z0 + c) < drop;
            // A vertex on the hole's rim is the far ring's own vertex, at the far
            // ring's own height: the first shots showed sky through the slit
            // between the ring's edge and a stand-in sunk under it. Only a vertex
            // with stand-in quads all round it goes down to the lowest ground
            // near it. (Off the lattice - the normals' half-cell reads - it is
            // the field.)
            HorizonHeight standY = (x, z) =>
            {
                float lx = x / FarCell, lz = z / FarCell;
                bool lattice = Mathf.Abs(lx - Mathf.Round(lx)) < 0.01f && Mathf.Abs(lz - Mathf.Round(lz)) < 0.01f;
                bool inside = lattice && standKeep(x - FarCell, z - FarCell, FarCell) && standKeep(x, z - FarCell, FarCell) &&
                              standKeep(x - FarCell, z, FarCell) && standKeep(x, z, FarCell);
                if (!inside) return StageGroundHeightAt(x, z) - FarSink;
                float y = float.MaxValue;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                        y = Mathf.Min(y, StageGroundHeightAt(x + i * FarCell * 0.5f, z + j * FarCell * 0.5f));
                return y - StandInSink;
            };
            int standIns = 0, standVerts = 0;
            ForEachChunk(b, FarChunk, FarCoverage, (cx, cz, ox, oz) =>
            {
                if (!FarChunkBuilt(b, cx, cz)) return;
                var mesh = HorizonMesh(ox, oz, FarChunk, FarCell, 300f, standY, standKeep, false);
                if (mesh == null) return;
                standVerts += mesh.vertexCount;
                HorizonGO(root.transform, "StandIn_" + cx + "_" + cz, mesh, standMat, ox, oz, "StageHorizonS_" + cx + "_" + cz);
                standIns++;
            });

            // ---- tier 0: the DEM at its own grid, past the far ring ---------
            int tx0 = Mathf.FloorToInt(dx0 / HorizonChunk), tx1 = Mathf.CeilToInt(dx1 / HorizonChunk);
            int tz0 = Mathf.FloorToInt(dz0 / HorizonChunk), tz1 = Mathf.CeilToInt(dz1 / HorizonChunk);
            bool InTier0(float x, float z)
            {
                int cx = Mathf.FloorToInt(x / HorizonChunk), cz = Mathf.FloorToInt(z / HorizonChunk);
                return cx >= tx0 && cx < tx1 && cz >= tz0 && cz < tz1;
            }
            HorizonHeight landY = (x, z) => HorizonFieldY(x, z) - FarSink - HorizonDrop(x, z);
            HorizonKeep tierKeep = (x0, z0, c) =>
            {
                float mx = x0 + c * 0.5f, mz = z0 + c * 0.5f;
                return InTier0(mx, mz) && !FarChunkBuilt(b, Mathf.FloorToInt(mx / FarChunk), Mathf.FloorToInt(mz / FarChunk));
            };
            int tier = 0, tierVerts = 0;
            for (int cz = tz0; cz < tz1; cz++)
                for (int cx = tx0; cx < tx1; cx++)
                {
                    float ox = cx * HorizonChunk, oz = cz * HorizonChunk;
                    var mesh = HorizonMesh(ox, oz, HorizonChunk, FarCell, 300f, landY, tierKeep, true);
                    if (mesh == null) continue;
                    tierVerts += mesh.vertexCount;
                    HorizonGO(root.transform, "Tier_" + cx + "_" + cz, mesh, farMat, ox, oz, "StageHorizonT_" + cx + "_" + cz);
                    tier++;
                }

            // ---- the apron: the wide grid's real land (or, without one, the
            // DEM mirrored outward), coarse, curving away -----------------------
            float ex0 = tx0 * HorizonChunk - ApronReachM, ex1 = tx1 * HorizonChunk + ApronReachM;
            float ez0 = tz0 * HorizonChunk - ApronReachM, ez1 = tz1 * HorizonChunk + ApronReachM;
            if (demWide != null)
            {
                // to the wide grid's own edge, a cell in
                var wm = demWideMeta;
                ex0 = wm.originX + wm.cell; ex1 = wm.originX + (wm.cols - 2) * wm.cell;
                ez0 = wm.originZ + wm.cell; ez1 = wm.originZ + (wm.rows - 2) * wm.cell;
            }
            int ax0 = Mathf.FloorToInt(ex0 / ApronChunk), ax1 = Mathf.CeilToInt(ex1 / ApronChunk);
            int az0 = Mathf.FloorToInt(ez0 / ApronChunk), az1 = Mathf.CeilToInt(ez1 / ApronChunk);
            HorizonHeight apronY = (x, z) =>
            {
                // a 480 m cell reads a 60 m field: the mean of nine, so one
                // sharp peak is not a spike and the next cell a pit
                float y = 0f;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                        y += HorizonFieldY(x + i * ApronCell / 3f, z + j * ApronCell / 3f);
                return y / 9f - FarSink - HorizonDrop(x, z);
            };
            HorizonKeep apronKeep = (x0, z0, c) =>
            {
                float mx = x0 + c * 0.5f, mz = z0 + c * 0.5f;
                return !InTier0(mx, mz) && mx > ex0 && mx < ex1 && mz > ez0 && mz < ez1;
            };
            int apron = 0, apronVerts = 0;
            for (int cz = az0; cz < az1; cz++)
                for (int cx = ax0; cx < ax1; cx++)
                {
                    float ox = cx * ApronChunk, oz = cz * ApronChunk;
                    var mesh = HorizonMesh(ox, oz, ApronChunk, ApronCell, 300f, apronY, apronKeep, true);
                    if (mesh == null) continue;
                    apronVerts += mesh.vertexCount;
                    HorizonGO(root.transform, "Apron_" + cx + "_" + cz, mesh, farMat, ox, oz, "StageHorizonA_" + cx + "_" + cz);
                    apron++;
                }

            // the ridges' crown fringe (PSX_FARCANOPY=0 clips it in the shader)
            int fringeCards = BuildHorizonFringe(root.transform, detail);

            // The far plane: past the apron's furthest corner from anywhere on the route.
            float reach = 0f;
            foreach (var corner in new[] { new Vector2(ex0, ez0), new Vector2(ex1, ez0), new Vector2(ex0, ez1), new Vector2(ex1, ez1) })
            {
                var w = new Vector3(corner.x, 0f, corner.y);
                foreach (var e in new[] { b.min, b.max, new Vector3(b.min.x, 0f, b.max.z), new Vector3(b.max.x, 0f, b.min.z) })
                    reach = Mathf.Max(reach, Vector2.Distance(new Vector2(w.x, w.z), new Vector2(e.x, e.z)));
            }
            stageHorizonFar = Mathf.Ceil(reach / 1000f) * 1000f + 2000f;
            var culling = Object.FindFirstObjectByType<StageCulling>();
            if (culling != null) culling.horizonFarClip = stageHorizonFar;
            Log($"Stage horizon, PC: {standIns} corridor stand-ins ({standVerts} verts), tier 0 {tier} chunks ({tierVerts} verts) over the " +
                $"{(dx1 - dx0) / 1000f:0.0} x {(dz1 - dz0) / 1000f:0.0} km DEM, apron {apron} chunks ({apronVerts} verts) over {(ex1 - ex0) / 1000f:0} x {(ez1 - ez0) / 1000f:0} km " +
                $"({(demWide != null ? "the wide grid's real land" : "the DEM mirrored: no wide grid, tools/roads/bake_wide.mjs")}); " +
                $"far plane {stageHorizonFar:0} m, detail to {detail:0} m; ridge fringe {fringeCards} cards.");
            return stageHorizonFar;
        }

        // ---- THE RIDGES' CROWN FRINGE (linncove 2026-11-16) ------------------
        // The DEM is bare earth: past the forest's reach a ridge met the sky as
        // a smooth line of 60 m facets, 20-30 m under where the real treetops
        // stand. Every crest of the 60 m lattice (higher than both neighbours
        // across it, by a convex 6 m over 240 m) carries a pair of upright
        // cards, one along the ridge and one across, whose alpha is a row of
        // crowns - broadleaf domes, or spruce-fir spires up high - and whose
        // colour is the land's own crown field (PSX/Lit _HorizonFringe). Drawn
        // on the Horizon layer, dithered in past the detail's edge (where the
        // mesh forest has stopped), clipped whole by PSX_FARCANOPY=0.
        const float FringeW = 72f, FringeCrossW = 48f, FringeH = 30f, FringeSink = 8f, FringeTexM = 96f;
        static string FringePath(bool spruce) => StageGenDir + (spruce ? "/HorizonFringeSpruce.png" : "/HorizonFringe.png");

        static float FringeHash(int a, int b) { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663); h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15; return (h & 0xffffu) / 65535f; }

        /// <summary>A row of crowns across 96 m, the card's 30 m tall with the
        /// ground 8 m up it: alpha the forest's outline, grey its shading
        /// (lighter toward each crown's top). Seamless across.</summary>
        static void WriteFringeTexture(bool spruce)
        {
            const int W = 512, H = 128;
            var top = new float[W];
            var depth = new float[W];   // how deep the crown that makes the top runs (for the shading)
            for (int x = 0; x < W; x++) { top[x] = 6f; depth[x] = 6f; }
            float spacing = spruce ? 5.2f : 8.6f;
            int n = Mathf.RoundToInt(FringeTexM / spacing);
            for (int k = 0; k < n; k++)
            {
                float cx = (k + 0.15f + 0.7f * FringeHash(k, spruce ? 7 : 3)) * FringeTexM / n;
                float ty = (spruce ? 17f : 16f) + (spruce ? 8f : 6.5f) * FringeHash(k, 11);
                float rx = spruce ? 2.4f + 1.0f * FringeHash(k, 13) : 4.2f + 2.4f * FringeHash(k, 13);
                float ry = spruce ? 10f : 5.5f + 2.5f * FringeHash(k, 17);
                for (int x = 0; x < W; x++)
                {
                    float xm = (x + 0.5f) * FringeTexM / W;
                    float dx = Mathf.Abs(xm - cx); dx = Mathf.Min(dx, FringeTexM - dx);   // seamless
                    if (dx >= rx) continue;
                    float y = spruce ? ty - ry * (dx / rx)                                      // a spire
                                     : ty - ry + ry * Mathf.Sqrt(1f - (dx / rx) * (dx / rx));     // a dome
                    if (y > top[x]) { top[x] = y; depth[x] = ry; }
                }
            }
            WriteTexture(FringePath(spruce), W, H, (x, y) =>
            {
                float ym = (y + 0.5f) * FringeH / H - FringeSink;   // over the ground
                // the outline's leaves: a ragged edge half a metre deep
                float rag = 0.35f * Mathf.Sin(x * 1.7f + y * 0.3f) + 0.25f * Mathf.Sin(x * 3.1f + 1.3f);
                bool on = ym < top[x] + rag;
                float under = Mathf.Clamp01((top[x] - ym) / Mathf.Max(depth[x], 1f));
                float g = Mathf.Lerp(0.62f, 0.30f, under);
                byte v = (byte)Mathf.RoundToInt(g * 255f);
                return new Color32(v, v, v, (byte)(on ? 255 : 0));
            });
            var imp = AssetImporter.GetAtPath(FringePath(spruce)) as TextureImporter;
            if (imp == null) return;
            bool dirty = !imp.mipmapEnabled || !imp.mipMapsPreserveCoverage || Mathf.Abs(imp.alphaTestReferenceValue - 0.5f) > 0.01f ||
                         imp.wrapModeU != TextureWrapMode.Repeat || imp.wrapModeV != TextureWrapMode.Clamp ||
                         imp.filterMode != FilterMode.Trilinear || !imp.alphaIsTransparency || imp.sRGBTexture;
            imp.mipmapEnabled = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.5f;
            imp.wrapModeU = TextureWrapMode.Repeat; imp.wrapModeV = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Trilinear; imp.anisoLevel = 4; imp.alphaIsTransparency = true;
            imp.sRGBTexture = false;   // the shading is a factor, not a colour
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            if (dirty) imp.SaveAndReimport();
        }

        /// <summary>The fringe's material, in every dress (the land's crowns).</summary>
        static Material FringeMat(bool spruce, float detail)
        {
            WriteFringeTexture(spruce);
            string key = MeshPrefix + (spruce ? "HorizonFringeS" : "HorizonFringe");
            var baseMat = MakeMat(key, FringePath(spruce), cutoff: 0.5f, affine: 0f, twoSided: true);
            RegisterSeasonalTexture(key, baseMat, d => FringePath(spruce), "far", cutoff: 0.5f, twoSided: true);
            var dresses = DressesOf(baseMat);
            for (int d = 0; d < dresses.Length; d++)
            {
                var m = dresses[d];
                if (m == null) continue;
                m.SetFloat("_Horizon", 1f);
                m.SetFloat("_HorizonFringe", 1f);
                SetFarCanopy(m, d);
                // spires all the way up: the share of conifers 1 on the spruce cards
                if (spruce) m.SetVector("_CanopySpruce", new Vector4(-1e5f, -1e5f + 1f, 1f, 1f));
                // dithered in from just inside the detail's edge to just past it
                m.SetVector("_HorizonFade", new Vector4(detail - 260f, detail + 60f, 1f / HillMapM, HillMapLum));
                EditorUtility.SetDirty(m);
            }
            return baseMat;
        }

        /// <summary>The land as the horizon draws it (tier 0's own height).</summary>
        static float FringeGroundY(float x, float z) => HorizonFieldY(x, z) - FarSink - HorizonDrop(x, z);

        static int BuildHorizonFringe(Transform parent, float detail)
        {
            if (demFar == null || demFarMeta == null) return 0;
            var broadMat = FringeMat(false, detail);
            var spruceMat = FringeMat(true, detail);
            var root = new GameObject("RidgeFringe");
            root.transform.SetParent(parent, false);
            HorizonDemRect(out float dx0, out float dx1, out float dz0, out float dz1);
            const float S = FarCell;
            int gx0 = Mathf.CeilToInt((dx0 + 2f * S) / S), gx1 = Mathf.FloorToInt((dx1 - 2f * S) / S);
            int gz0 = Mathf.CeilToInt((dz0 + 2f * S) / S), gz1 = Mathf.FloorToInt((dz1 - 2f * S) / S);
            int cols = gx1 - gx0 + 1, rows = gz1 - gz0 + 1;
            if (cols < 5 || rows < 5) return 0;
            var h = new float[cols * rows];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    h[r * cols + c] = HorizonFieldY((gx0 + c) * S, (gz0 + r) * S);
            float H(int c, int r) => h[Mathf.Clamp(r, 0, rows - 1) * cols + Mathf.Clamp(c, 0, cols - 1)];
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) };
            // per horizon chunk: broadleaf and spruce card lists
            var meshes = new Dictionary<long, (List<Vector3> v, List<Vector3> n, List<Vector2> u, List<int> t, List<Vector3> sv, List<Vector3> sn, List<Vector2> su, List<int> st)>();
            int cards = 0;
            void Card(Vector3 centre, Vector3 along, float width, Vector3 nrm, bool spruce, float u0)
            {
                int cx = Mathf.FloorToInt(centre.x / HorizonChunk), cz = Mathf.FloorToInt(centre.z / HorizonChunk);
                long key = ((long)cx << 32) ^ (uint)cz;
                if (!meshes.TryGetValue(key, out var m))
                    meshes[key] = m = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>(),
                                       new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
                var vs = spruce ? m.sv : m.v; var ns = spruce ? m.sn : m.n; var us = spruce ? m.su : m.u; var ts = spruce ? m.st : m.t;
                float ox = cx * HorizonChunk, oz = cz * HorizonChunk;
                Vector3 a = centre - along * (width * 0.5f), b = centre + along * (width * 0.5f);
                float ya = FringeGroundY(a.x, a.z), yb = FringeGroundY(b.x, b.z);
                int i0 = vs.Count;
                vs.Add(new Vector3(a.x - ox, ya - FringeSink, a.z - oz));
                vs.Add(new Vector3(a.x - ox, ya - FringeSink + FringeH, a.z - oz));
                vs.Add(new Vector3(b.x - ox, yb - FringeSink + FringeH, b.z - oz));
                vs.Add(new Vector3(b.x - ox, yb - FringeSink, b.z - oz));
                float u1 = u0 + width / FringeTexM;
                us.Add(new Vector2(u0, 0f)); us.Add(new Vector2(u0, 1f)); us.Add(new Vector2(u1, 1f)); us.Add(new Vector2(u1, 0f));
                for (int q = 0; q < 4; q++) ns.Add(nrm);
                ts.Add(i0); ts.Add(i0 + 1); ts.Add(i0 + 2); ts.Add(i0); ts.Add(i0 + 2); ts.Add(i0 + 3);
                cards++;
            }
            for (int r = 2; r < rows - 2; r++)
                for (int c = 2; c < cols - 2; c++)
                {
                    float h0 = H(c, r);
                    float best = 0f; int bestD = -1;
                    for (int k = 0; k < dirs.Length; k++)
                    {
                        var d = dirs[k];
                        float a1 = H(c - d.x, r - d.y), b1 = H(c + d.x, r + d.y);
                        if (h0 <= a1 || h0 <= b1) continue;
                        float a2 = H(c - 2 * d.x, r - 2 * d.y), b2 = H(c + 2 * d.x, r + 2 * d.y);
                        float convex = h0 - 0.5f * (a2 + b2);
                        if (d.x != 0 && d.y != 0) convex *= 0.7071f;   // the diagonals' 85 m steps, per 60
                        if (convex > best) { best = convex; bestD = k; }
                    }
                    if (bestD < 0 || best < 6f) continue;
                    float x = (gx0 + c) * S, z = (gz0 + r) * S;
                    if (RouteDistanceCoarse(x, z) < 25f) continue;   // never over the road's own line
                    var dd = new Vector3(dirs[bestD].x, 0f, dirs[bestD].y).normalized;
                    var along = new Vector3(-dd.z, 0f, dd.x);
                    float y = FringeGroundY(x, z);
                    float gxs = (H(c + 1, r) - H(c - 1, r)) / (2f * S), gzs = (H(c, r + 1) - H(c, r - 1)) / (2f * S);
                    var nrm = new Vector3(-gxs, 1f, -gzs).normalized;
                    float elev = stageBaseM + y;
                    float pSpruce = Mathf.Lerp(ConiferLow, ConiferHigh, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SpruceFromM, SpruceByM, elev)));
                    bool spruce = FringeHash(c * 3 + 1, r * 7 + 5) < pSpruce;
                    var centre = new Vector3(x, y, z);
                    Card(centre, along, FringeW, nrm, spruce, FringeHash(c, r));
                    Card(centre, dd, FringeCrossW, nrm, spruce, FringeHash(r, c));
                }
            int chunks = 0;
            foreach (var kv in meshes)
            {
                int cx = (int)(kv.Key >> 32), cz = (int)(kv.Key & 0xffffffff);
                float ox = cx * HorizonChunk, oz = cz * HorizonChunk;
                var m = kv.Value;
                for (int s = 0; s < 2; s++)
                {
                    var vs = s == 1 ? m.sv : m.v;
                    if (vs.Count == 0) continue;
                    var mesh = new Mesh { indexFormat = vs.Count < 65000 ? UnityEngine.Rendering.IndexFormat.UInt16 : UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.SetVertices(vs); mesh.SetNormals(s == 1 ? m.sn : m.n); mesh.SetUVs(0, s == 1 ? m.su : m.u);
                    mesh.SetTriangles(s == 1 ? m.st : m.t, 0);
                    mesh.RecalculateBounds();
                    HorizonGO(parent: root.transform, name: "Fringe" + (s == 1 ? "S_" : "_") + cx + "_" + cz, mesh: mesh,
                              mat: s == 1 ? spruceMat : broadMat, ox: ox, oz: oz, meshName: "StageHorizonF" + (s == 1 ? "S_" : "_") + cx + "_" + cz);
                    chunks++;
                }
            }
            Log($"Stage horizon: ridge fringe {cards} cards in {chunks} meshes over {cols} x {rows} lattice points.");
            return cards;
        }
    }
}
