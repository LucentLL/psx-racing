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
                $"far plane {stageHorizonFar:0} m, detail to {detail:0} m.");
            return stageHorizonFar;
        }
    }
}
