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
    ///   * past it, to the far forest's reach: the far grid's 60 m slope gives
    ///     the share of rock the near grid shows at that slope (measured over
    ///     the two grids' overlap on five stages: 5 % at 30-35 degrees, 16 % at
    ///     35-40, 40 % at 40-45), laid as 12 m cells in clumps;
    ///   * past the far forest: PSX/Lit paints the same share on the far land
    ///     (_FarRock, Horizon.cs), fading in from 1.5 km so the two never
    ///     switch.
    /// A stage whose world file carries the lidar's cliff cells (Lake Lure,
    /// Chimney Rock) keeps those as its rock near the road: they are the better
    /// data, and the hill the owner liked is exactly them.
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
            farRockKeep != null && farRockKeep.Contains(CliffKey(Mathf.FloorToInt(x / CliffCell), Mathf.FloorToInt(z / CliffCell)));

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
            bool lidar = world != null && world.cliffs != null && world.cliffs.Length >= 6 && WorldSwitch("PSX_LURE_GRANITE");
            var cells = new List<Vector4>();   // x, z, downhill x, downhill z
            int nearCells = 0, farCells = 0, skipRoad = 0, skipKeep = 0;
            float halfW = RoadWidth * 0.5f;
            float reach = Mathf.Min(FarForestMaxM, FarCoverage - FarCell) + FarCell;
            var nm = demNearMeta;
            bool InNear(float x, float z) =>
                x > nm.originX + 2f * nm.cell && x < nm.originX + (nm.cols - 3) * nm.cell &&
                z > nm.originZ + 2f * nm.cell && z < nm.originZ + (nm.rows - 3) * nm.cell;
            bool Clear(float x, float z)
            {
                if (RouteDistanceCoarse(x, z) < FarRockRoadM + 40f && StageCorridor(x, z, Mathf.Max(FarRockRoadM, halfW + 14f), out _)) { skipRoad++; return false; }
                if (world != null && (WorldKeepBits(x, z) & 7) != 0) { skipKeep++; return false; }
                if (StageKeepOut(x, z)) { skipKeep++; return false; }
                return true;
            }
            if (!lidar)
            {
                // the near grid: slope by central differences over 24 m, relief over 5 x 5 cells
                int cols = nm.cols, rows = nm.rows;
                float c2 = 2f * nm.cell, tanT = Mathf.Tan(FarRockSlopeDeg * Mathf.Deg2Rad);
                for (int r = 2; r < rows - 2; r++)
                    for (int c = 2; c < cols - 2; c++)
                    {
                        float dx = (demNear[r * cols + c + 1] - demNear[r * cols + c - 1]) * 0.1f / c2;
                        float dz = (demNear[(r + 1) * cols + c] - demNear[(r - 1) * cols + c]) * 0.1f / c2;
                        if (dx * dx + dz * dz < tanT * tanT) continue;
                        short lo = short.MaxValue, hi = short.MinValue;
                        for (int b = -2; b <= 2; b++)
                            for (int a = -2; a <= 2; a++)
                            {
                                short v = demNear[(r + b) * cols + c + a];
                                if (v < lo) lo = v; if (v > hi) hi = v;
                            }
                        if ((hi - lo) * 0.1f < FarRockReliefM) continue;
                        float x = nm.originX + c * nm.cell, z = nm.originZ + r * nm.cell;
                        if (RouteDistanceCoarse(x, z) > reach) continue;
                        if (!Clear(x, z)) continue;
                        float g = Mathf.Sqrt(dx * dx + dz * dz);
                        cells.Add(new Vector4(x, z, -dx / g, -dz / g));
                        nearCells++;
                    }
            }
            // past the near grid (and on a lidar stage, past the lidar's reach
            // as well as the near grid), to the far forest's reach: the far
            // grid's share, laid as 12 m cells in clumps
            {
                var fm = demFarMeta;
                int cols = fm.cols, rows = fm.rows;
                float c2 = 2f * fm.cell;
                for (int r = 1; r < rows - 1; r++)
                    for (int c = 1; c < cols - 1; c++)
                    {
                        float x0 = fm.originX + c * fm.cell, z0 = fm.originZ + r * fm.cell;
                        if (InNear(x0, z0)) continue;
                        float dx = (demFar[r * cols + c + 1] - demFar[r * cols + c - 1]) * 0.1f / c2;
                        float dz = (demFar[(r + 1) * cols + c] - demFar[(r - 1) * cols + c]) * 0.1f / c2;
                        float g = Mathf.Sqrt(dx * dx + dz * dz);
                        float share = FarRockShare(Mathf.Atan(g) * Mathf.Rad2Deg);
                        if (share < 0.01f) continue;
                        if (RouteDistanceCoarse(x0, z0) > reach + fm.cell) continue;
                        for (int j = -2; j <= 2; j++)
                            for (int i = -2; i <= 2; i++)
                            {
                                float x = x0 + i * CliffCell, z = z0 + j * CliffCell;
                                if (InNear(x, z)) continue;
                                if (FarRockClump(x, z) >= share) continue;
                                if (RouteDistanceCoarse(x, z) > reach) continue;
                                if (lidar && CliffKeepAt(x, z)) continue;
                                if (!Clear(x, z)) continue;
                                cells.Add(new Vector4(x, z, -dx / g, -dz / g));
                                farCells++;
                            }
                    }
            }
            if (cells.Count == 0) { Log($"Far mountain template: no rock cells ({(lidar ? "the lidar granite is this stage's rock" : "nothing that steep")})."); return; }
            farRockKeep = new HashSet<long>();
            foreach (var cl in cells)
            {
                int cx = Mathf.FloorToInt(cl.x / CliffCell), cz = Mathf.FloorToInt(cl.y / CliffCell);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) farRockKeep.Add(CliffKey(cx + dx, cz + dz));
            }
            var root = new GameObject("FarMtn");
            root.transform.SetParent(parent, false);
            root.AddComponent<PCOnly>();
            var rock = new GameObject("FarRock");
            rock.transform.SetParent(root.transform, false);
            var sw = root.AddComponent<FarMtnSwitch>();
            sw.rock = rock;
            Physics.SyncTransforms();
            int chunks = BuildRockSkin(rock.transform, FarRockMat(), cells, "StageFarRock_", "FarRock_");
            Log($"Far mountain template: {cells.Count} rock cells of 12 m ({nearCells} from the near grid's rule, {farCells} from the far grid's share" +
                $"{(lidar ? "; the lidar granite is the rock near the road" : "")}), {cells.Count * CliffCell * CliffCell / 1e6f:0.000} km2, " +
                $"{chunks} chunks; left out: {skipRoad} by the road, {skipKeep} on the world's keep-out; keep-out {farRockKeep.Count} cells.");
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

        /// <summary>The far land's own rock (PSX/Lit _FarRock): past the far
        /// forest the land is painted with the same share of rock the cells
        /// above follow, in the stage's rock, fading in from 1.5 km (where the
        /// skin's cells carry it) to 2.4 km.</summary>
        static void SetFarRock(Material m)
        {
            if (m == null || !m.HasProperty("_FarRock")) return;
            if (!FarMtnBuild) { m.SetFloat("_FarRock", 0f); return; }
            // the photoscan's own mean, linear, under the stage's tint
            var tint = FarRockTint;
            var mean = FarRockAlbedoMean;
            m.SetColor("_FarRockCol", new Color(mean.r * tint.r, mean.g * tint.g, mean.b * tint.b, 1f));
            m.SetVector("_FarRockShare", new Vector4(Mathf.Cos(FarRockShareLo * Mathf.Deg2Rad), Mathf.Cos(FarRockShareHi * Mathf.Deg2Rad), FarRockShareMax, FarRockClumpM));
            m.SetVector("_FarRockFade", new Vector4(1500f, 2400f, 0f, 0f));
            m.SetFloat("_FarRock", 1f);
        }

        /// <summary>T_Photoscanned_Cliff_01_BC's mean albedo, linear (the 4096 px
        /// file measured whole, sRGB decoded: a greenish grey, lichen and moss).</summary>
        static readonly Color FarRockAlbedoMean = new Color(0.139f, 0.153f, 0.123f);
    }
}
