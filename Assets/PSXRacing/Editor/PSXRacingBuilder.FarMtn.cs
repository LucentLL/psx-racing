using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FAR MOUNTAIN TEMPLATE (farmtn, owner 2026-11-17, PC49 on the Bat
    /// Cave race in fall: "I like the rockface and distant tree textures on
    /// distant mountains. This should be a template for distant mountains on
    /// other maps").
    ///
    /// What he saw on that hill is two things, both found in the code:
    ///   * the CANOPY - the far forest's trees (BuildStageFarForest, to 2.1 km
    ///     from the road) over the far canopy's crown field (PSX/Lit
    ///     _HorizonCrowns, PSXRacingBuilder.Horizon). Both were already every
    ///     mountain stage's;
    ///   * the ROCK FACES - Lake Lure's own: the 2018 lidar's cliff cells
    ///     (tools/roads/bake_world.mjs: steeper than 52 degrees over 18 m of
    ///     relief) laid in the Meadow pack's photoscanned granite over the
    ///     drawn ground, no tree on them (StageLand: BuildWorldGranite,
    ///     BuildWorldGraniteSkin, the keep-out). Only Lake Lure and the
    ///     Chimney Rock stage (the same world file in its frame) had them.
    ///
    /// The template gives every stage the rock by one rule, from the stage's
    /// own 3DEP heights - no hand placement, no new data:
    ///   * where the near grid is (12 m, 3DEP 1/3 arc-second, ~1 km round
    ///     the route): a cell is rock where the ground stands at 45 degrees or
    ///     more and the land within 24 m spans 18 m or more. On Lake Lure this
    ///     rule against the 1 m lidar's own cliff cells (scratch farmtn
    ///     demrock.py): precision 0.90, recall 0.99;
    ///   * past it, over the whole far grid: the far grid's 60 m slope gives
    ///     the share of rock the near grid shows at that slope (measured over
    ///     the two grids' overlap on five stages: 5 % at 30-35 degrees, 16 % at
    ///     35-40, 40 % at 40-45), laid as 12 m cells in clumps.
    /// Both go into ONE ROCK MASK (12 m texels on the near grid's lattice over
    /// the far grid). Within the far forest's reach its cells are drawn as the
    /// skin; PSX/Lit paints the far land from the same mask (_FarRockMask; its
    /// mips are the share where a cell is under a pixel), so when the skin
    /// dissolves at the detail's edge (~2 km) the land under it shows the same
    /// faces: nothing switches, nothing changes pattern as you drive.
    /// A stage whose world file carries the lidar's cliff cells (Lake Lure,
    /// Chimney Rock) keeps those as its rock near the road (its own skin and
    /// keep-out): they are the better data, and the hill the owner liked is
    /// exactly them; its mask is those cells, the share past the lidar's reach.
    /// Rock cells take the same keep-out the lidar granite does (StageKeepOut:
    /// no tree, plant or fence) and the same skin, in the stage's own rock:
    /// Hickory Nut Gorge's pale granite gneiss, the Blue Ridge's greyer gneiss
    /// and quartzite elsewhere.
    ///
    /// PSX_FARMTN_TEMPLATE=0 at the scene build: nothing of this (the base
    /// exactly); at load: the rock's root off and the far land unpainted
    /// (FarMtnSwitch). The PC scene pass only (PCContent).
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        static bool FarMtnBuild => PCContent && Environment.GetEnvironmentVariable("PSX_FARMTN_TEMPLATE") != "0";

        /// <summary>The rule on the 12 m grid (calibrated on Lake Lure).</summary>
        const float FarRockSlopeDeg = 45f, FarRockReliefM = 18f;
        /// <summary>Nothing within this of the centreline: the road's own cuts
        /// are the stage builder's (Cliffs, RockRelief).</summary>
        const float FarRockRoadM = 30f;
        /// <summary>The far grid's slope (degrees) to the share of rock:
        /// 0.5 x smoothstep(lo, hi) - 4 % at 32.5, 21 % at 37.5, 40 % at 42.5.</summary>
        internal const float FarRockShareLo = 29f, FarRockShareHi = 48f, FarRockShareMax = 0.5f;
        /// <summary>The clumps the share is laid in (m).</summary>
        internal const float FarRockClumpM = 36f;

        /// <summary>Rock cells (12 m keys, grown one cell): no tree or plant.</summary>
        static HashSet<long> farRockKeep;
        static bool FarRockKeepAt(float x, float z) =>
            farRockKeep != null && farRockKeep.Contains(CliffKey(Mathf.FloorToInt(x / CliffCell), Mathf.FloorToInt(z / CliffCell)))
            && RouteDistanceCoarse(x, z) >= ForestBand;   // never in the roadside band (see FarRockBandM)
        /// <summary>THE ROADSIDE BAND IS LEFT AS IT IS: no rock cell nearer the
        /// route than the roadside forest's band and two cells. A clearing in the
        /// band moves its fill trees (and the title postcard's foreground at Linn
        /// Cove with them - the first build showed it); near the road the rock is
        /// the road builder's (the cuts, the boulders, Linn Cove's granite). The
        /// band's forest, its trunks and every collider stay exactly the base's.</summary>
        const float FarRockBandM = ForestBand + 2f * CliffCell;

        /// <summary>The stage's rock (linear tint on the photoscanned granite):
        /// Hickory Nut Gorge's pale granite gneiss (the Lake Lure granite's
        /// own), the Blue Ridge's greyer gneiss and quartzite.</summary>
        static Color FarRockTint => StagePrefix == "lure" || StagePrefix == "chimney"
            ? new Color(0.84f, 0.83f, 0.80f) : new Color(0.74f, 0.73f, 0.70f);

        static Material FarRockMat()
        {
            bool gorge = StagePrefix == "lure" || StagePrefix == "chimney";
            return NMLitMat(gorge ? "NM_meadow_granite" : "NM_meadow_gneiss", MeadowCliffDir, "T_Photoscanned_Cliff_01_BC",
                            "T_Photoscanned_Cliff_01_Normal", null, false, FarRockTint);
        }

        /// <summary>The share of rock at a 60 m slope (degrees).</summary>
        internal static float FarRockShare(float slopeDeg)
        {
            float t = Mathf.Clamp01((slopeDeg - FarRockShareLo) / (FarRockShareHi - FarRockShareLo));
            return FarRockShareMax * t * t * (3f - 2f * t);
        }

        /// <summary>A smooth value noise 0..1 at the clump scale (integer hash:
        /// the same everywhere it is asked).</summary>
        static float FarRockClump(float x, float z)
        {
            float fx = x / FarRockClumpM, fz = z / FarRockClumpM;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            tx = tx * tx * (3f - 2f * tx); tz = tz * tz * (3f - 2f * tz);
            float H(int a, int b) { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663); h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15; return (h & 0xffffu) / 65535f; }
            float v = Mathf.Lerp(Mathf.Lerp(H(ix, iz), H(ix + 1, iz), tx), Mathf.Lerp(H(ix, iz + 1), H(ix + 1, iz + 1), tx), tz);
            // a value noise's middle is crowded: spread it back toward uniform
            return Mathf.Clamp01(0.5f + (v - 0.5f) * 1.6f);
        }

        /// <summary>THE ROCK MASK: one byte a 12 m cell over the whole far grid,
        /// on the near grid's own lattice (a near node is a mask texel); 255 =
        /// bare rock. It is the template's one source: the skin's cells are its
        /// rock within the far forest's reach, and PSX/Lit paints the far land
        /// from it (_FarRockMask) - so a face the skin draws near is the face the
        /// land draws far, cell for cell, and nothing changes as the skin
        /// dissolves at the detail's edge.</summary>
        class FarRockMaskData { public byte[] v; public float ox, oz, cell; public int cols, rows; }
        /// <summary>The planned cells' slopes (tangent), in the cells' order.</summary>
        static List<float> planSteep;
        /// <summary>The template's cliff pieces' triangle budget on the PC pass
        /// (Lake Lure's granite has 1.6 M; its 2,977 cells use 0.7 M).</summary>
        const long FarRockPieceBudget = 800000;

        /// <summary>The mask and the skin's cells (x, z, downhill x, downhill z)
        /// for the loaded stage: the near grid's rule, the far grid's share past
        /// it; on a lidar stage (its world file's cliff cells) those cells within
        /// 2.2 km of the road (the lidar's own reach) and the share past them,
        /// and no skin of the template's (the lidar granite has its own). Needs
        /// the DEM and the route (StageLoadDem).</summary>
        static List<Vector4> PlanFarMtnCells(out bool lidar, out int nearCells, out int farCells, out int skipRoad, out int skipKeep,
                                             out FarRockMaskData mask)
        {
            lidar = world != null && world.cliffs != null && world.cliffs.Length >= 6 && WorldSwitch("PSX_LURE_GRANITE");
            var cells = new List<Vector4>();
            planSteep = new List<float>();
            int nC = 0, fC = 0, sR = 0, sK = 0;
            float halfW = RoadWidth * 0.5f;
            float reach = Mathf.Min(FarForestMaxM, FarCoverage - FarCell) + FarCell;
            const float LidarReachM = 2200f;   // bake_world.mjs: cliffs within 2.2 km of the road
            var nm = demNearMeta; var fm = demFarMeta;
            float c = nm.cell;
            int kx = Mathf.CeilToInt((nm.originX - fm.originX) / c), kz = Mathf.CeilToInt((nm.originZ - fm.originZ) / c);
            var M = new FarRockMaskData { cell = c, ox = nm.originX - kx * c, oz = nm.originZ - kz * c };
            M.cols = Mathf.FloorToInt((fm.originX + (fm.cols - 1) * fm.cell - M.ox) / c) + 1;
            M.rows = Mathf.FloorToInt((fm.originZ + (fm.rows - 1) * fm.cell - M.oz) / c) + 1;
            M.v = new byte[M.cols * M.rows];
            var dir = new Dictionary<int, Vector2>();   // a rock texel's downhill
            var tanAt = new Dictionary<int, float>();   // and its slope's tangent
            bool InNear(int i, int j)
            {
                int cc = i - kx, rr = j - kz;
                return cc >= 2 && cc < nm.cols - 2 && rr >= 2 && rr < nm.rows - 2;
            }
            // the far grid's slope share, per far node
            var share = new float[fm.cols * fm.rows];
            var fdir = new Vector2[fm.cols * fm.rows];
            var ftan = new float[fm.cols * fm.rows];
            for (int r = 1; r < fm.rows - 1; r++)
                for (int q = 1; q < fm.cols - 1; q++)
                {
                    float dx = (demFar[r * fm.cols + q + 1] - demFar[r * fm.cols + q - 1]) * 0.1f / (2f * fm.cell);
                    float dz = (demFar[(r + 1) * fm.cols + q] - demFar[(r - 1) * fm.cols + q]) * 0.1f / (2f * fm.cell);
                    float g = Mathf.Sqrt(dx * dx + dz * dz);
                    share[r * fm.cols + q] = FarRockShare(Mathf.Atan(g) * Mathf.Rad2Deg);
                    fdir[r * fm.cols + q] = g > 1e-4f ? new Vector2(-dx / g, -dz / g) : Vector2.up;
                    ftan[r * fm.cols + q] = g;
                }
            if (lidar)
            {
                // the lidar's cells, each on its nearest texel
                var C = world.cliffs;
                for (int k = 0; k + 5 < C.Length; k += 6)
                {
                    int i = Mathf.RoundToInt((C[k] - M.ox) / c), j = Mathf.RoundToInt((C[k + 1] - M.oz) / c);
                    if (i < 0 || j < 0 || i >= M.cols || j >= M.rows) continue;
                    M.v[j * M.cols + i] = 255;
                }
            }
            else
            {
                // the near grid's rule: slope by central differences over 24 m, relief over 5 x 5 nodes
                float tanT = Mathf.Tan(FarRockSlopeDeg * Mathf.Deg2Rad);
                for (int r = 2; r < nm.rows - 2; r++)
                    for (int q = 2; q < nm.cols - 2; q++)
                    {
                        float dx = (demNear[r * nm.cols + q + 1] - demNear[r * nm.cols + q - 1]) * 0.1f / (2f * c);
                        float dz = (demNear[(r + 1) * nm.cols + q] - demNear[(r - 1) * nm.cols + q]) * 0.1f / (2f * c);
                        if (dx * dx + dz * dz < tanT * tanT) continue;
                        short lo = short.MaxValue, hi = short.MinValue;
                        for (int bb = -2; bb <= 2; bb++)
                            for (int aa = -2; aa <= 2; aa++)
                            {
                                short v = demNear[(r + bb) * nm.cols + q + aa];
                                if (v < lo) lo = v;
                                if (v > hi) hi = v;
                            }
                        if ((hi - lo) * 0.1f < FarRockReliefM) continue;
                        int idx = (r + kz) * M.cols + q + kx;
                        if (idx < 0 || idx >= M.v.Length) continue;
                        M.v[idx] = 255;
                        float g = Mathf.Sqrt(dx * dx + dz * dz);
                        dir[idx] = new Vector2(-dx / g, -dz / g);
                        tanAt[idx] = g;
                    }
            }
            // the far grid's share, laid in clumps: past the near grid (past the
            // lidar's reach on a lidar stage)
            for (int j = 0; j < M.rows; j++)
                for (int i = 0; i < M.cols; i++)
                {
                    if (!lidar && InNear(i, j)) continue;
                    float x = M.ox + i * c, z = M.oz + j * c;
                    int fq = Mathf.Clamp(Mathf.RoundToInt((x - fm.originX) / fm.cell), 0, fm.cols - 1);
                    int fr = Mathf.Clamp(Mathf.RoundToInt((z - fm.originZ) / fm.cell), 0, fm.rows - 1);
                    float sh = share[fr * fm.cols + fq];
                    if (sh < 0.01f || FarRockClump(x, z) >= sh) continue;
                    if (lidar && RouteDistanceCoarse(x, z) < LidarReachM) continue;
                    int idx = j * M.cols + i;
                    M.v[idx] = 255;
                    dir[idx] = fdir[fr * fm.cols + fq];
                    tanAt[idx] = ftan[fr * fm.cols + fq];
                }
            // within the far forest's reach: nothing by the road or on the world's
            // keep-out (in the mask too: the land is never painted where the skin
            // may not be), and the rest are the skin's cells
            bool Clear(float x, float z)
            {
                if (RouteDistanceCoarse(x, z) < FarRockRoadM + 40f && StageCorridor(x, z, Mathf.Max(FarRockRoadM, halfW + 14f), out _)) { sR++; return false; }
                if (world != null && (WorldKeepBits(x, z) & 7) != 0) { sK++; return false; }
                if (StageKeepOut(x, z)) { sK++; return false; }
                return true;
            }
            for (int idx = 0; idx < M.v.Length; idx++)
            {
                if (M.v[idx] == 0) continue;
                int i = idx % M.cols, j = idx / M.cols;
                float x = M.ox + i * c, z = M.oz + j * c;
                if (lidar) continue;   // the lidar granite has its own skin and keep-out
                float dRoute = RouteDistanceCoarse(x, z);
                if (dRoute < FarRockBandM) { M.v[idx] = 0; continue; }
                if (dRoute > reach) continue;
                if (!Clear(x, z)) { M.v[idx] = 0; continue; }
                var dd = dir.TryGetValue(idx, out var d0) ? d0 : Vector2.up;
                cells.Add(new Vector4(x, z, dd.x, dd.y));
                planSteep.Add(tanAt.TryGetValue(idx, out var t0) ? t0 : 1f);
                if (InNear(i, j)) nC++; else fC++;
            }
            nearCells = nC; farCells = fC; skipRoad = sR; skipKeep = sK;
            mask = M;
            return cells;
        }

        /// <summary>The mask as a texture (one channel, linear, mips: far away a
        /// texel's mean is the share of rock) and its world-to-uv transform.
        /// Under Generated, out of the Art folder's 256 px importer sweep.</summary>
        static Texture2D WriteFarRockMask(FarRockMaskData M, out Vector4 st)
        {
            string path = GenDir + "/" + MeshPrefix + "FarRockMask.png";
            var tex = new Texture2D(M.cols, M.rows, TextureFormat.RGBA32, false);
            var px = new Color32[M.cols * M.rows];
            for (int k = 0; k < px.Length; k++) { byte v = M.v[k]; px[k] = new Color32(v, v, v, 255); }
            tex.SetPixels32(px); tex.Apply();
            System.IO.File.WriteAllBytes(ProjectRootPath(path), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.SingleChannel;
                var ts = new TextureImporterSettings();
                imp.ReadTextureSettings(ts);
                ts.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                imp.SetTextureSettings(ts);
                imp.sRGBTexture = false; imp.mipmapEnabled = true; imp.filterMode = FilterMode.Trilinear; imp.anisoLevel = 4;
                imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 4096; imp.npotScale = TextureImporterNPOTScale.None;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            // texel i's centre is the lattice node ox + i * cell
            float w = M.cols * M.cell, h = M.rows * M.cell;
            st = new Vector4(1f / w, 1f / h, (0.5f * M.cell - M.ox) / w, (0.5f * M.cell - M.oz) / h);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>The mask on every far-land material dress that wears the
        /// template's rock (SetFarRock set _FarRock on them in HorizonDress).</summary>
        static int SetFarRockMask(IEnumerable<Material> mats, Texture2D mask, Vector4 st)
        {
            int n = 0;
            var seen = new HashSet<Material>();
            foreach (var m in mats)
            {
                if (m == null || !seen.Add(m) || !m.HasProperty("_FarRockMask") || m.GetFloat("_FarRock") < 0.5f) continue;
                m.SetTexture("_FarRockMask", mask);
                m.SetVector("_FarRockMaskST", mask != null ? st : Vector4.zero);
                EditorUtility.SetDirty(m);
                n++;
            }
            return n;
        }

        static IEnumerable<Material> SeasonMaterials()
        {
            foreach (var e in seasonEntries)
            {
                if (e == null) continue;
                if (e.baseMat != null) yield return e.baseMat;
                if (e.variants != null) foreach (var m in e.variants) if (m != null) yield return m;
            }
        }

        /// <summary>
        /// THE TEMPLATE, from BuildTrack after the landmark rocks and before
        /// the forest (which takes its keep-out): plan the rock cells, lay the
        /// skin. The far land's own rock is set on its materials by
        /// HorizonDress.
        /// </summary>
        static void BuildFarMtn(List<Vector3> pts, Transform parent)
        {
            farRockKeep = null;
            if (!FarMtnBuild || stageWp == null || demNear == null || demFar == null) return;
            // a mountain's distance only (the horizon's and the far forest's own rule)
            if (!string.IsNullOrEmpty(theme.farGround) || !string.IsNullOrEmpty(theme.sand) || track.stageWaterY > 0f) return;
            var cells = PlanFarMtnCells(out bool lidar, out int nearCells, out int farCells, out int skipRoad, out int skipKeep, out var mask);
            var maskTex = WriteFarRockMask(mask, out var maskST);
            int dressed = SetFarRockMask(SeasonMaterials(), maskTex, maskST);
            int rockTexels = 0; foreach (var v in mask.v) if (v != 0) rockTexels++;
            Log($"Far mountain template: rock mask {mask.cols} x {mask.rows} texels of {mask.cell:0} m, {rockTexels} rock ({rockTexels * mask.cell * mask.cell / 1e6f:0.00} km2), on {dressed} far-land material dresses.");
            // the root and its switch on every stage the template is built on, rock or
            // not: the switch is also the far land's (PSX/Lit _PSXFarMtnOff)
            var root = new GameObject("FarMtn");
            root.transform.SetParent(parent, false);
            root.AddComponent<PCOnly>();
            var rock = new GameObject("FarRock");
            rock.transform.SetParent(root.transform, false);
            var sw = root.AddComponent<FarMtnSwitch>();
            sw.rock = rock;
            if (cells.Count == 0) { Log($"Far mountain template: no rock cells ({(lidar ? "the lidar granite is this stage's rock near the road" : "nothing that steep")}); the far land's rock past 2 km."); return; }
            farRockKeep = new HashSet<long>();
            foreach (var cl in cells)
            {
                int cx = Mathf.FloorToInt(cl.x / CliffCell), cz = Mathf.FloorToInt(cl.y / CliffCell);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) farRockKeep.Add(CliffKey(cx + dx, cz + dz));
            }
            Physics.SyncTransforms();
            var rmat = FarRockMat();
            int chunks = BuildRockSkin(rock.transform, rmat, cells, "StageFarRock_", "FarRock_");
            // and the photoscanned cliffs on them, as Lake Lure's granite has (the faces' relief and shade)
            int pieces = 0;
            if (System.Environment.GetEnvironmentVariable("PSX_FARMTN_PIECES") != "0" &&
                AssetDatabase.LoadAssetAtPath<GameObject>(MeadowCliffDir + "/" + CliffNames[0] + ".FBX") != null)
                pieces = PlaceRockPieces(rock.transform, rmat, LoadGraniteVars(rmat), cells, planSteep, FarRockPieceBudget,
                                         "StageFarRockPiece_", "FarRockPiece_", "Far mountain template, cliff pieces");
            Log($"Far mountain template: {cells.Count} rock cells of 12 m ({nearCells} from the near grid's rule, {farCells} from the far grid's share), " +
                $"{cells.Count * CliffCell * CliffCell / 1e6f:0.000} km2, " +
                $"{chunks} chunks, {pieces} cliff pieces; left out: {skipRoad} by the road, {skipKeep} on the world's keep-out; keep-out {farRockKeep.Count} cells.");
        }

        /// <summary>
        /// A ROCK SKIN: each 12 m cell laid over the drawn ground, a hair above
        /// it (the far ground's own triangles; the near lattice by a ray),
        /// mapped across the fall line so the rock's grain stands upright -
        /// Lake Lure's granite skin (BuildWorldGraniteSkin), shared. Cells are
        /// (x, z, downhill x, downhill z). Returns the chunks made.
        /// </summary>
        static int BuildRockSkin(Transform parent, Material mat, List<Vector4> cells, string meshPrefix, string goPrefix)
        {
            var bufs = new Dictionary<long, (List<Vector3> v, List<Vector2> uv, List<int> t)>();
            const float H = CliffCell * 0.5f, Tex = 9f;
            foreach (var cl in cells)
            {
                float x = cl.x, z = cl.y, dx = cl.z, dz = cl.w;
                bool near = RouteDistanceCoarse(x, z) < NearCoverage - NearCell;
                long ck = ((long)Mathf.FloorToInt(x / 500f) << 32) ^ (uint)Mathf.FloorToInt(z / 500f);
                if (!bufs.TryGetValue(ck, out var b)) bufs[ck] = b = (new List<Vector3>(), new List<Vector2>(), new List<int>());
                int v0 = b.v.Count;
                Vector2 across = new Vector2(-dz, dx);
                for (int j = 0; j <= 2; j++)
                    for (int i = 0; i <= 2; i++)
                    {
                        float px = x - H + i * H, pz = z - H + j * H;
                        float py = near ? DrapeY(px, pz) + 0.4f : FarSurfaceY(px, pz) + 0.9f;   // clear of the ground's creases
                        b.v.Add(new Vector3(px, py, pz));
                        b.uv.Add(new Vector2((px * across.x + pz * across.y) / Tex, py / Tex));
                    }
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int a = v0 + j * 3 + i, bb = a + 3, c = a + 4, e = a + 1;
                        b.t.Add(a); b.t.Add(bb); b.t.Add(c);
                        b.t.Add(a); b.t.Add(c); b.t.Add(e);
                    }
            }
            int n = 0;
            foreach (var kv in bufs)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(kv.Value.v); m.SetUVs(0, kv.Value.uv); m.SetTriangles(kv.Value.t, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                SaveMesh(m, meshPrefix + n);
                var go = new GameObject(goPrefix + n);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.isStatic = true;
                if (HorizonLayer >= 0) go.layer = HorizonLayer;   // drawn as far as the far ground is
                n++;
            }
            return n;
        }

        /// <summary>The far land's own rock (PSX/Lit _FarRock), set by
        /// HorizonDress on every far-land dress: the stage's rock colour and the
        /// 60 m share past the mask; BuildFarMtn hands it the mask itself
        /// (SetFarRockMask) once the cells are planned.</summary>
        static void SetFarRock(Material m)
        {
            if (m == null || !m.HasProperty("_FarRock")) return;
            if (!FarMtnBuild) { m.SetFloat("_FarRock", 0f); return; }
            // the photoscan's own mean, linear, under the stage's tint
            var tint = FarRockTint;
            var mean = FarRockAlbedoMean;
            m.SetColor("_FarRockCol", new Color(mean.r * tint.r, mean.g * tint.g, mean.b * tint.b, 1f));
            m.SetVector("_FarRockShare", new Vector4(Mathf.Cos(FarRockShareLo * Mathf.Deg2Rad), Mathf.Cos(FarRockShareHi * Mathf.Deg2Rad), FarRockShareMax, FarRockClumpM));
            m.SetVector("_FarRockMaskST", Vector4.zero);
            m.SetFloat("_FarRock", 1f);
        }

        /// <summary>A stage's builder state without building it: the waypoints,
        /// the DEM and the world file, the terrain field (the road plan's own
        /// first steps), in an empty scene.</summary>
        static void LoadStageForLab(TrackCatalog.TrackDef def)
        {
            log = new System.Text.StringBuilder();
            EnsureFolders(); GenerateTrackTextures(); EnsureRoadLayer();
            psxLit = Shader.Find("PSX/Lit");
            track = def; theme = ThemeFor(def);
            matByTex.Clear(); matByKey.Clear(); ClearSeasonEntries();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var waypoints = BuildWaypoints(out _);
            StageLoadDem();
            BuildTerrainField(waypoints);
        }

        /// <summary>
        /// THE LAB'S PATCH for a stage whose rock is its world file's lidar
        /// granite (Lake Lure, Chimney Rock): there the template builds no skin
        /// and plants exactly as before, so a scene built without it differs
        /// from one built with it only by the far land's materials (SetFarRock
        /// and the mask) and the switch's root. This plans the mask, then makes
        /// those changes on the built scene and saves it - the scene a build
        /// with the template would make, in minutes instead of forty. Refuses a
        /// stage without lidar cliffs (its cells change the forest: build it).
        /// Returns a line for the log.
        /// </summary>
        internal static string PatchLidarStageForLab(TrackCatalog.TrackDef def)
        {
            LoadStageForLab(def);
            if (!FarMtnBuild) return def.id + ": PSX_FARMTN_TEMPLATE=0 / not the PC pass - nothing patched";
            var cells = PlanFarMtnCells(out bool lidar, out _, out _, out _, out _, out var mask);
            if (!lidar || cells.Count > 0) return def.id + ": not a lidar stage (its cells change the forest) - build it instead";
            var maskTex = WriteFarRockMask(mask, out var maskST);
            int rockTexels = 0; foreach (var v in mask.v) if (v != 0) rockTexels++;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/PSXRacing/Scenes/" + def.id + ".unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
            if (UnityEngine.Object.FindAnyObjectByType<FarMtnSwitch>(FindObjectsInactive.Include) != null) return def.id + ": already has the template's root";
            var mats = new HashSet<Material>();
            void Take(Material m) { if (m != null && m.HasProperty("_FarRock") && m.GetFloat("_Horizon") > 0.5f && m.GetFloat("_HorizonCrowns") > 0.5f && m.GetFloat("_HorizonFringe") < 0.5f) mats.Add(m); }
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)) foreach (var m in r.sharedMaterials) Take(m);
            foreach (var sd in UnityEngine.Object.FindObjectsByType<SeasonDress>(FindObjectsInactive.Include))
                if (sd.entries != null) foreach (var e in sd.entries) if (e != null) { Take(e.baseMat); if (e.variants != null) foreach (var m in e.variants) Take(m); }
            foreach (var m in mats) SetFarRock(m);
            int dressed = SetFarRockMask(mats, maskTex, maskST);
            var tp = UnityEngine.Object.FindAnyObjectByType<TrackPath>();
            var parent = tp != null ? tp.transform : null;
            var root = new GameObject("FarMtn");
            if (parent != null) root.transform.SetParent(parent, false);
            root.AddComponent<PCOnly>();
            var rock = new GameObject("FarRock");
            rock.transform.SetParent(root.transform, false);
            root.AddComponent<FarMtnSwitch>().rock = rock;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            return $"{def.id}: rock mask {mask.cols}x{mask.rows} ({rockTexels} rock texels), far land's rock on {dressed} of {mats.Count} material dresses, " +
                   $"the switch's root under {(parent != null ? parent.name : "the scene")}, saved {ok}";
        }

        /// <summary>The template's cells for a stage without building anything
        /// (the lab's dry run: the road plan's own first steps, then the plan).</summary>
        public static string PlanFarMtnForLab(TrackCatalog.TrackDef def)
        {
            if (!def.stage) return def.id + " is not a stage";
            LoadStageForLab(def);
            if (!FarMtnBuild) return def.id + ": the template is off (PSX_FARMTN_TEMPLATE=0 or not the PC pass)";
            var cells = PlanFarMtnCells(out bool lidar, out int nearCells, out int farCells, out int skipRoad, out int skipKeep, out var mask);
            int rockTexels = 0; foreach (var v in mask.v) if (v != 0) rockTexels++;
            // (PSX_FM_PLANWRITE=1: the mask texture written and imported too - the build's own step, tried)
            if (System.Environment.GetEnvironmentVariable("PSX_FM_PLANWRITE") == "1") { var t = WriteFarRockMask(mask, out var st0); rockTexels = t != null ? rockTexels : -1; }
            return $"{def.id}: mask {mask.cols}x{mask.rows}, {rockTexels} rock texels; skin " +
                   $"{def.id}: {cells.Count} rock cells ({nearCells} near rule, {farCells} far share{(lidar ? ", lidar stage" : "")}), " +
                   $"{cells.Count * CliffCell * CliffCell / 1e6f:0.000} km2; left out {skipRoad} by the road, {skipKeep} on the keep-out; tint {FarRockTint}";
        }

        /// <summary>T_Photoscanned_Cliff_01_BC's mean albedo, linear (the 4096 px
        /// file measured whole, sRGB decoded: a greenish grey, lichen and moss).</summary>
        static readonly Color FarRockAlbedoMean = new Color(0.139f, 0.153f, 0.123f);
    }
}
