using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WATER AROUND A STAGE'S ROAD, from the stage's world file
    /// (Art/&lt;stage&gt;/&lt;pre&gt;_world.json, tools/roads/bake_world.mjs): first
    /// Lake Lure at full pool and the Rocky Broad down the Hickory Nut Gorge, as
    /// they stood before Hurricane Helene (owner, 2026-11-16).
    ///
    /// Water is drawn by PSX/Water through the existing MakeWaterMat (the water
    /// agent owns the shader). Every water mesh here carries the VERTEX CONTRACT
    /// the water pass reads:
    ///   colour r = depth over the bed, 0.5 at the waterline, +/-12 m across the range (as the sea);
    ///   colour g = 0 (no ocean swell);
    ///   colour b = white water 0..1 (rapids, rocks, weirs; 0 on the lake);
    ///   uv2 (TEXCOORD1).xy = flow velocity in world x, z, m/s (0 on still water).
    ///
    /// Also here: the near-grid CARVE under the river (12 m cells cannot hold a
    /// 20 m channel), and the KEEP-OUT mask the forest's planting passes ask
    /// (StageKeepOut): nothing is planted in the water, on a building, a lot or a
    /// side road. Switches: PSX_LURE_WORLD=0 reads no world file at all (the
    /// bare stage as it was), PSX_LURE_WATER=0 builds no water.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        [Serializable] class WorldRing { public int hole; public float[] xz; }
        [Serializable] class WorldRiver { public float[] xz, y, w, white, speed; }
        [Serializable] class WorldCarve { public int[] idx, dm; }
        [Serializable] class WorldBuilding { public float[] xz; public float g0, g1; public string kind; public int levels; public string roof; public float height, st; public int side; public float dist, area, fx, fz; public string zone, role; }
        [Serializable] class WorldRoad { public float st; public int side; public string cls; public float w, lenM; public float[] xz, y; }
        [Serializable] class WorldLot { public float[] xz; public float y, st; public int gravel; }
        [Serializable] class WorldPath { public float[] xz, y; public int deck; public string kind; public float st; }
        [Serializable] class WorldPark { public float[] xz, y, leadXZ, leadY; public float st; public int side; public float d0, startLineM, spacing, deckFrom, deckTo, mouthSt; }
        [Serializable]
        class WorldData
        {
            public int version; public string stage, source; public float baseM;
            public float lakeLevelM, lakeLevelY, lidarWaterM, shoreSlope, maxDepthM;
            public WorldRing[] lake, riverAreas; public WorldRiver river; public WorldCarve carve;
            public WorldBuilding[] buildings; public WorldRoad[] roads, streets; public WorldLot[] lots; public WorldPath[] paths;
            public float[] crossings; public WorldSpeed[] speeds; public WorldPark parkRoad;
            // round 2: open ground, the marina's piers, the granite (x, z, y, downhill x, z, steepness per cell)
            public WorldOpen[] open; public WorldPier[] piers; public float[] cliffs;
        }

        static WorldData world;
        static string worldPath;
        /// <summary>The keep-out mask: 3 m cells over the near grid. Bit 1 lake
        /// or river water, bit 2 a building, bit 4 a lot, a side road or a path.</summary>
        static byte[] worldKeep;
        static float worldKeepX0, worldKeepZ0;
        static int worldKeepCols, worldKeepRows;
        const float WorldKeepCell = 3f;

        static Rect worldLakeBox;
        static bool WorldSwitch(string name) => Environment.GetEnvironmentVariable(name) != "0";

        /// <summary>From StageLoadDem, after the near grid is read: the world
        /// file of this stage (if it has one) and its carve.</summary>
        static void StageWorldLoad()
        {
            world = null; worldKeep = null; worldPath = null; worldCliffKeep = null;
            if (!WorldSwitch("PSX_LURE_WORLD")) return;
            // the Park Road stage shows the same village, river and bridge at its start (PSX_LURE_CHIMNEY=0: as it was)
            if (StagePrefix == "chimney" && !WorldSwitch("PSX_LURE_CHIMNEY")) return;
            string path = ProjectRootPath(StageArtDir + "/" + StagePrefix + "_world.json");
            if (!File.Exists(path)) return;
            world = JsonUtility.FromJson<WorldData>(File.ReadAllText(path));
            worldPath = path;
            int carved = 0;
            if (WorldSwitch("PSX_LURE_WATER") && world.carve != null && world.carve.idx != null && demNear != null)
                for (int k = 0; k < world.carve.idx.Length; k++)
                {
                    int i = world.carve.idx[k];
                    if (i < 0 || i >= demNear.Length) continue;
                    short want = (short)Mathf.Clamp(world.carve.dm[k], short.MinValue, short.MaxValue);
                    if (want < demNear[i]) { demNear[i] = want; carved++; }
                }
            int benched = WorldSwitch("PSX_LURE_TOWN") && world.stage == StagePrefix ? WorldStreetBench() : 0;
            if (WorldSwitch("PSX_LURE_MOUTHS") && world.stage == StagePrefix) WorldMouthBench();
            Log($"Stage world street bench: {benched} near cells lowered to the road beside downtown Lake Lure and Chimney Rock's Main Street.");
            Log($"Stage world: {Path.GetFileName(path)} - {(world.buildings?.Length ?? 0)} buildings, {(world.roads?.Length ?? 0)} side roads, " +
                $"lake at {world.lakeLevelM:0.0} m, river {(world.river?.y?.Length ?? 0)} stations; {carved} near cells carved under the river.");
        }

        /// <summary>The stretches that are a STREET, not a mountain road: downtown
        /// Lake Lure and Chimney Rock village (metres along the route).</summary>
        static readonly Vector2[] WorldStreets = { new Vector2(5050f, 6150f), new Vector2(6420f, 8040f) };
        static bool WorldOnStreet(float m) { foreach (var z in WorldStreets) if (m >= z.x && m <= z.y) return true; return false; }

        /// <summary>
        /// THE STREET BENCH. Beside a village street the land a 12 m lattice
        /// reads rises off the shoulder (the lidar's ground under the shops and
        /// the hill behind them, smoothed together), and the stage builder cuts
        /// a rock face into it - a stone wall in front of the storefronts. On
        /// the two street stretches the near grid is LOWERED (never raised: the
        /// river side falls away under the shops on posts) to the road's height
        /// out to 10 m past the tarmac edge, easing back to the land by 16 m, and
        /// only where the land stands under 3.5 m over the road (a real wall
        /// higher than that stays a wall).
        /// </summary>
        static int WorldStreetBench()
        {
            var pts = track.stagePts; if (pts == null || demNear == null) return 0;
            var m = demNearMeta; float half = track.roadWidth * 0.5f; int n = 0;
            // each near cell's nearest street station, visiting only the cells within 30 m of one
            var near = new Dictionary<int, Vector2>();   // cell -> (d^2, station)
            foreach (var zr in WorldStreets)
                for (int i = Mathf.Max(0, (int)(zr.x / Spacing)); i <= Mathf.Min(pts.Length - 1, (int)(zr.y / Spacing)); i++)
                {
                    int c0 = Mathf.Max(0, (int)((pts[i].x - 30f - m.originX) / m.cell)), c1 = Mathf.Min(m.cols - 1, (int)((pts[i].x + 30f - m.originX) / m.cell) + 1);
                    int r0 = Mathf.Max(0, (int)((pts[i].z - 30f - m.originZ) / m.cell)), r1 = Mathf.Min(m.rows - 1, (int)((pts[i].z + 30f - m.originZ) / m.cell) + 1);
                    for (int r = r0; r <= r1; r++)
                        for (int c = c0; c <= c1; c++)
                        {
                            float x = m.originX + c * m.cell, z = m.originZ + r * m.cell;
                            float d2 = (pts[i].x - x) * (pts[i].x - x) + (pts[i].z - z) * (pts[i].z - z);
                            int k = r * m.cols + c;
                            if (d2 < 900f && (!near.TryGetValue(k, out var v) || d2 < v.x)) near[k] = new Vector2(d2, i);
                        }
                }
            foreach (var kv in near)
            {
                int k = kv.Key, best = (int)kv.Value.y;
                float d = Mathf.Sqrt(kv.Value.x) - half;
                if (d < 0.5f || d > 16f) continue;
                float target = pts[best].y - 0.08f;
                float have = demNear[k] * 0.1f;
                if (have <= target || have - target > 3.5f) continue;
                float keep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 16f, d));
                short want = (short)Mathf.RoundToInt((target + (have - target) * keep) * 10f);
                if (want < demNear[k]) { demNear[k] = want; n++; }
            }
            return n;
        }

        static Vector3[] WorldPts(float[] xz, float y = 0f)
        {
            var o = new Vector3[xz.Length / 2];
            for (int i = 0; i < o.Length; i++) o[i] = new Vector3(xz[2 * i], y, xz[2 * i + 1]);
            return o;
        }

        /// <summary>The ground a thing stands on: the built lattice inside the
        /// near grid, the DEM beyond.</summary>
        static float WorldGroundY(float x, float z) =>
            RouteDistanceCoarse(x, z) < NearCoverage ? StageLatticeY(x, z) : StageDemY(x, z);

        // ------------------------------------------------------------ keep-out
        internal static bool StageKeepOut(float x, float z)
        {
            if (LandmarkKeepOut(x, z)) return true;   // a Linn Cove block (PSXRacingBuilder.LandmarkRocks)
            if (farRockKeep != null && FarRockKeepAt(x, z)) return true;   // the far mountain template's rock (PSXRacingBuilder.FarMtn)
            if (worldKeep == null) return false;
            if (worldCliffKeep != null && CliffKeepAt(x, z)) return true;
            int c = (int)((x - worldKeepX0) / WorldKeepCell), r = (int)((z - worldKeepZ0) / WorldKeepCell);
            if (c < 0 || r < 0 || c >= worldKeepCols || r >= worldKeepRows)
                return world != null && world.lakeLevelY > 0f && worldLakeBox.Contains(new Vector2(x, z)) && StageDemY(x, z) < world.lakeLevelY + 0.3f;
            byte v = worldKeep[r * worldKeepCols + c];
            // parkland (the town streets' bands, PSX_LURE_OPEN): open but for the odd grove
            if (v == KeepPark) return !ParkGrove(x, z);
            return v != 0;
        }

        /// <summary>Even-odd scanline fill of one or more rings into a mask
        /// (holes come out by the rule), then grown by <paramref name="grow"/> cells.</summary>
        static void WorldFill(byte[] mask, List<Vector3[]> rings, byte bit, int grow)
        {
            float minZ = float.MaxValue, maxZ = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (var r in rings) foreach (var p in r) { minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
            int r0 = Mathf.Max(0, (int)((minZ - worldKeepZ0) / WorldKeepCell) - 1), r1 = Mathf.Min(worldKeepRows - 1, (int)((maxZ - worldKeepZ0) / WorldKeepCell) + 1);
            var xs = new List<float>();
            var tmp = grow > 0 ? new List<int>() : null;
            for (int row = r0; row <= r1; row++)
            {
                float z = worldKeepZ0 + (row + 0.5f) * WorldKeepCell;
                xs.Clear();
                foreach (var ring in rings)
                    for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                    {
                        Vector3 a = ring[i], b = ring[j];
                        if ((a.z > z) != (b.z > z)) xs.Add(a.x + (z - a.z) / (b.z - a.z) * (b.x - a.x));
                    }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int c0 = Mathf.Max(0, Mathf.CeilToInt((xs[k] - worldKeepX0) / WorldKeepCell - 0.5f));
                    int c1 = Mathf.Min(worldKeepCols - 1, Mathf.FloorToInt((xs[k + 1] - worldKeepX0) / WorldKeepCell - 0.5f));
                    for (int c = c0; c <= c1; c++) { int idx = row * worldKeepCols + c; if ((mask[idx] & bit) == 0) { mask[idx] |= bit; tmp?.Add(idx); } }
                }
            }
            if (grow <= 0) return;
            foreach (int idx in tmp)
            {
                int rr = idx / worldKeepCols, cc = idx % worldKeepCols;
                for (int dr = -grow; dr <= grow; dr++)
                    for (int dc = -grow; dc <= grow; dc++)
                    {
                        int r2 = rr + dr, c2 = cc + dc;
                        if (r2 < 0 || c2 < 0 || r2 >= worldKeepRows || c2 >= worldKeepCols) continue;
                        mask[r2 * worldKeepCols + c2] |= bit;
                    }
            }
        }

        /// <summary>A band either side of a polyline into the mask.</summary>
        static void WorldBand(byte[] mask, Vector3[] line, float half, byte bit)
        {
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector3 a = line[i], b = line[i + 1];
                float L = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                int n = Mathf.Max(1, Mathf.CeilToInt(L / (WorldKeepCell * 0.5f)));
                for (int s = 0; s <= n; s++)
                {
                    Vector3 p = Vector3.Lerp(a, b, s / (float)n);
                    int g = Mathf.CeilToInt(half / WorldKeepCell);
                    int cc = (int)((p.x - worldKeepX0) / WorldKeepCell), rr = (int)((p.z - worldKeepZ0) / WorldKeepCell);
                    for (int dr = -g; dr <= g; dr++)
                        for (int dc = -g; dc <= g; dc++)
                        {
                            int r2 = rr + dr, c2 = cc + dc;
                            if (r2 < 0 || c2 < 0 || r2 >= worldKeepRows || c2 >= worldKeepCols) continue;
                            float x = worldKeepX0 + (c2 + 0.5f) * WorldKeepCell, z = worldKeepZ0 + (r2 + 0.5f) * WorldKeepCell;
                            if ((x - p.x) * (x - p.x) + (z - p.z) * (z - p.z) <= half * half) mask[r2 * worldKeepCols + c2] |= bit;
                        }
                }
            }
        }

        static List<Vector3[]> WorldRings(WorldRing[] rings, bool holesToo = true)
        {
            var o = new List<Vector3[]>();
            if (rings == null) return o;
            foreach (var r in rings) if (r.xz != null && r.xz.Length >= 6 && (holesToo || r.hole == 0)) o.Add(WorldPts(r.xz));
            return o;
        }

        static void BuildWorldKeepOut()
        {
            var m = demNearMeta;
            worldKeepX0 = m.originX; worldKeepZ0 = m.originZ;
            worldKeepCols = Mathf.CeilToInt((m.cols - 1) * m.cell / WorldKeepCell) + 1;
            worldKeepRows = Mathf.CeilToInt((m.rows - 1) * m.cell / WorldKeepCell) + 1;
            var mask = new byte[worldKeepCols * worldKeepRows];
            worldKeep = mask;
            if (WorldSwitch("PSX_LURE_WATER"))
            {
                // the lake: inside the full-pool outline grown 15 m, wherever the
                // land there is under the water (the drawdown band the lidar saw)
                var lake = WorldRings(world.lake);
                if (lake.Count > 0)
                {
                    float bx0 = float.MaxValue, bx1 = float.MinValue, bz0 = float.MaxValue, bz1 = float.MinValue;
                    foreach (var rr in lake) foreach (var p in rr) { bx0 = Mathf.Min(bx0, p.x); bx1 = Mathf.Max(bx1, p.x); bz0 = Mathf.Min(bz0, p.z); bz1 = Mathf.Max(bz1, p.z); }
                    worldLakeBox = Rect.MinMaxRect(bx0 - 100f, bz0 - 100f, bx1 + 100f, bz1 + 100f);
                    var tmp = new byte[mask.Length];
                    var keep = worldKeep; worldKeep = tmp;
                    WorldFill(tmp, lake, 1, 5);
                    worldKeep = keep;
                    float lvl = world.lakeLevelY + 0.5f;
                    for (int i = 0; i < tmp.Length; i++)
                    {
                        if (tmp[i] == 0) continue;
                        float x = worldKeepX0 + (i % worldKeepCols + 0.5f) * WorldKeepCell, z = worldKeepZ0 + (i / worldKeepCols + 0.5f) * WorldKeepCell;
                        if (StageLatticeY(x, z) < lvl) mask[i] |= 1;
                    }
                }
                var river = WorldRings(world.riverAreas);
                if (river.Count > 0) WorldFill(mask, river, 1, 1);
                if (world.river != null && world.river.xz != null)
                {
                    var line = WorldPts(world.river.xz);
                    float wMax = 0f; foreach (var w in world.river.w) wMax = Mathf.Max(wMax, w);
                    // the mapped area and the line disagree by metres here and there: a band on the line too
                    for (int i = 0; i + 1 < line.Length; i++)
                        WorldBand(mask, new[] { line[i], line[i + 1] }, world.river.w[i] * 0.5f + 2f, 1);
                }
            }
            if (WorldSwitch("PSX_LURE_TOWN") && world.buildings != null)
                foreach (var b in world.buildings) WorldFill(mask, new List<Vector3[]> { WorldPts(b.xz) }, 2, 2);
            if (WorldSwitch("PSX_LURE_TOWN") && world.lots != null)
                foreach (var l in world.lots) WorldFill(mask, new List<Vector3[]> { WorldPts(l.xz) }, 4, 0);
            if (WorldSwitch("PSX_LURE_MOUTHS") && world.roads != null)
                foreach (var r in world.roads) WorldBand(mask, WorldPts(r.xz), r.w * 0.5f + 1.5f, 4);
            if (world.parkRoad != null && world.parkRoad.xz != null) WorldBand(mask, WorldPts(world.parkRoad.xz), 6f, 4);
            if (world.streets != null) foreach (var r in world.streets) WorldBand(mask, WorldPts(r.xz), r.w * 0.5f + 1.5f, 4);
            if (world.paths != null) foreach (var p in world.paths) if (p.deck != 0) WorldBand(mask, WorldPts(p.xz), 2.5f, 4);
            WorldLandKeepOut(mask);
            int n1 = 0, n2 = 0, n4 = 0;
            foreach (var v in mask) { if ((v & 1) != 0) n1++; if ((v & 2) != 0) n2++; if ((v & 4) != 0) n4++; }
            float a = WorldKeepCell * WorldKeepCell / 1e6f;
            Log($"Stage world keep-out: water {n1 * a:0.00} km2, buildings {n2 * a:0.000} km2, lots/roads/paths {n4 * a:0.000} km2 (3 m cells; the forest plants none of it).");
        }

        // --------------------------------------------------------------- build
        /// <summary>Everything from the world file, after the ground is built and
        /// before the forest (which asks the keep-out mask).</summary>
        static void BuildStageWorld(List<Vector3> pts, Transform parent)
        {
            if (world == null) return;
            var root = new GameObject("World");
            root.transform.SetParent(parent, false);
            BuildWorldKeepOut();
            if (WorldSwitch("PSX_LURE_WATER")) { BuildWorldLake(root.transform); BuildWorldRiver(root.transform); }
            BuildWorldTown(pts, root.transform);
            BuildWorldLand(pts, root.transform);
        }

        static Material WorldWaterMat(string name)
        {
            // the owner's PSX Textures water sheets, as the coast uses them
            const string shallow = Root + "/Art/Water/water_2.png", deep = Root + "/Art/Water/water_1.png";
            // the HD water's preset (water-hd): the Rocky Broad runs as a river, Lake Lure as a lake
            return MakeWaterMat(MeshPrefix + name, shallow, deep, name == "RiverWater" ? WaterPreset.River : WaterPreset.Lake);
        }

        static void WorldWaterGO(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            // NO COLLIDER, as the sea: a car over the rail ends up in the water and StuckRecovery brings it back.
        }

        static byte DepthRed(float d) => (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f + d / (2f * SeaDepthRangeM)) * 255f);

        // ---------------------------------------------------------------- lake
        const float LakeCell = 12f, LakeChunk = 600f;

        static void BuildWorldLake(Transform parent)
        {
            var rings = WorldRings(world.lake);
            if (rings.Count == 0) return;
            float y = world.lakeLevelY, flatY = world.lidarWaterM - world.baseM;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var r in rings) foreach (var p in r) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            minX -= 40f; minZ -= 40f; maxX += 40f; maxZ += 40f;
            int cols = Mathf.CeilToInt((maxX - minX) / LakeCell), rows = Mathf.CeilToInt((maxZ - minZ) / LakeCell);
            // inside the outline (+ 2 cells), on a grid of its own; and the distance in from the shore for the depth
            var inside = new bool[(cols + 1) * (rows + 1)];
            var xs = new List<float>();
            for (int r = 0; r <= rows; r++)
            {
                float z = minZ + r * LakeCell; xs.Clear();
                foreach (var ring in rings)
                    for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                        if ((ring[i].z > z) != (ring[j].z > z)) xs.Add(ring[i].x + (z - ring[i].z) / (ring[j].z - ring[i].z) * (ring[j].x - ring[i].x));
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                    for (int c = Mathf.Max(0, Mathf.CeilToInt((xs[k] - minX) / LakeCell)); c <= Mathf.Min(cols, Mathf.FloorToInt((xs[k + 1] - minX) / LakeCell)); c++)
                        inside[r * (cols + 1) + c] = true;
            }
            // chamfer distance (m) from the nearest vertex outside the outline
            var dist = new float[inside.Length];
            for (int i = 0; i < dist.Length; i++) dist[i] = inside[i] ? 1e6f : 0f;
            float d1 = LakeCell, d2 = LakeCell * 1.4142f;
            for (int r = 0; r <= rows; r++) for (int c = 0; c <= cols; c++)
            {
                int i = r * (cols + 1) + c; if (dist[i] == 0f) continue;
                if (c > 0) dist[i] = Mathf.Min(dist[i], dist[i - 1] + d1);
                if (r > 0) { dist[i] = Mathf.Min(dist[i], dist[i - cols - 1] + d1); if (c > 0) dist[i] = Mathf.Min(dist[i], dist[i - cols - 2] + d2); if (c < cols) dist[i] = Mathf.Min(dist[i], dist[i - cols] + d2); }
            }
            for (int r = rows; r >= 0; r--) for (int c = cols; c >= 0; c--)
            {
                int i = r * (cols + 1) + c; if (dist[i] == 0f) continue;
                if (c < cols) dist[i] = Mathf.Min(dist[i], dist[i + 1] + d1);
                if (r < rows) { dist[i] = Mathf.Min(dist[i], dist[i + cols + 1] + d1); if (c < cols) dist[i] = Mathf.Min(dist[i], dist[i + cols + 2] + d2); if (c > 0) dist[i] = Mathf.Min(dist[i], dist[i + cols] + d2); }
            }
            var riverRings = WorldRings(world.riverAreas);
            bool InRiverArea(float x, float z)
            {
                bool c = false;
                foreach (var ring in riverRings)
                    for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                        if ((ring[i].z > z) != (ring[j].z > z) && x < ring[i].x + (z - ring[i].z) / (ring[j].z - ring[i].z) * (ring[j].x - ring[i].x)) c = !c;
                return c;
            }
            var ground = new float[inside.Length];
            var near = new bool[inside.Length];
            for (int r = 0; r <= rows; r++) for (int c = 0; c <= cols; c++)
            {
                int i = r * (cols + 1) + c; float x = minX + c * LakeCell, z = minZ + r * LakeCell;
                ground[i] = WorldGroundY(x, z);
                // within 2 cells of the outline, or anywhere the land is under the water inside it
                near[i] = dist[i] > 0f || ground[i] < y;
            }
            // a cell is drawn when any corner is in or by the lake and some corner is under the water;
            // not where the river's own area is (the river ribbon draws that)
            var mat = WorldWaterMat("LakeWater");
            int drawn = 0, chunks = 0, verts = 0;
            float maxD = 0f;
            int ccols = Mathf.CeilToInt((maxX - minX) / LakeChunk), crows = Mathf.CeilToInt((maxZ - minZ) / LakeChunk);
            int per = Mathf.RoundToInt(LakeChunk / LakeCell);
            for (int cr = 0; cr < crows; cr++)
                for (int cc = 0; cc < ccols; cc++)
                {
                    var v = new List<Vector3>(); var col = new List<Color32>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tri = new List<int>();
                    var at = new Dictionary<int, int>();
                    int Vert(int c, int r)
                    {
                        int i = r * (cols + 1) + c;
                        if (at.TryGetValue(i, out int k)) return k;
                        float x = minX + c * LakeCell, z = minZ + r * LakeCell;
                        float d = y - ground[i];
                        // THE LIDAR HAS NO LAKE BED: under its flattened (drawdown) water the
                        // depth is estimated from the shore slope it did see, capped
                        if (dist[i] > 0f && ground[i] <= flatY + 0.1f)
                            d = Mathf.Min(world.maxDepthM, Mathf.Max(d, world.shoreSlope * dist[i]));
                        maxD = Mathf.Max(maxD, d);
                        k = v.Count; at[i] = k;
                        v.Add(new Vector3(x, y, z));
                        col.Add(new Color32(DepthRed(d), 0, 0, 255));
                        uv.Add(new Vector2(x / 26f, z / 26f));
                        uv2.Add(Vector2.zero);
                        return k;
                    }
                    for (int r = cr * per; r < Mathf.Min(rows, (cr + 1) * per); r++)
                        for (int c = cc * per; c < Mathf.Min(cols, (cc + 1) * per); c++)
                        {
                            int i00 = r * (cols + 1) + c, i01 = i00 + 1, i10 = i00 + cols + 1, i11 = i10 + 1;
                            if (!(near[i00] || near[i01] || near[i10] || near[i11])) continue;
                            bool anyIn = inside[i00] || inside[i01] || inside[i10] || inside[i11] || dist[i00] > 0f;
                            if (!anyIn) continue;
                            bool wet = ground[i00] < y || ground[i01] < y || ground[i10] < y || ground[i11] < y;
                            if (!wet) continue;
                            // a corner outside the outline far under the water is another valley (below the dam), not the lake
                            bool deepOut = (dist[i00] == 0f && ground[i00] < y - 5f) || (dist[i01] == 0f && ground[i01] < y - 5f)
                                        || (dist[i10] == 0f && ground[i10] < y - 5f) || (dist[i11] == 0f && ground[i11] < y - 5f);
                            if (deepOut) continue;
                            float mx = minX + (c + 0.5f) * LakeCell, mz = minZ + (r + 0.5f) * LakeCell;
                            if (!inside[i00] && !inside[i11] && riverRings.Count > 0 && InRiverArea(mx, mz)) continue;
                            int a = Vert(c, r), b = Vert(c + 1, r), d = Vert(c, r + 1), e = Vert(c + 1, r + 1);
                            tri.Add(a); tri.Add(d); tri.Add(e); tri.Add(a); tri.Add(e); tri.Add(b);
                            drawn++;
                        }
                    if (tri.Count == 0) continue;
                    var norms = new Vector3[v.Count]; for (int k = 0; k < norms.Length; k++) norms[k] = Vector3.up;
                    var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.SetVertices(v); mesh.SetNormals(norms); mesh.SetColors(col); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv2); mesh.SetTriangles(tri, 0);
                    SaveMesh(mesh, "LureLake_" + cc + "_" + cr);
                    WorldWaterGO(parent, "Lake_" + cc + "_" + cr, mesh, mat);
                    chunks++; verts += v.Count;
                }
            Log($"Stage world lake: {drawn} cells of {LakeCell:0} m in {chunks} chunks ({verts} verts) at y={y:0.00} ({world.lakeLevelM:0.0} m, full pool; " +
                $"the 2018 lidar's water {world.lidarWaterM:0.00} m), estimated depth to {maxD:0.0} m (shore slope {world.shoreSlope:0.00}, cap {world.maxDepthM:0} m).");
        }

        // --------------------------------------------------------------- river
        static void BuildWorldRiver(Transform parent)
        {
            var R = world.river;
            if (R == null || R.xz == null || R.xz.Length < 4) return;
            var line = WorldPts(R.xz);
            int n = line.Length;
            var lakeRings = WorldRings(world.lake, holesToo: false);
            bool InLake(Vector3 p)
            {
                bool c = false;
                foreach (var ring in lakeRings)
                    for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                        if ((ring[i].z > p.z) != (ring[j].z > p.z) && p.x < ring[i].x + (p.z - ring[i].z) / (ring[j].z - ring[i].z) * (ring[j].x - ring[i].x)) c = !c;
                return c;
            }
            // down to the first station inside the lake (the lake draws the rest)
            int last = n - 1;
            for (int i = 0; i < n; i++) if (InLake(line[i])) { last = i; break; }
            var mat = WorldWaterMat("RiverWater");
            const int across = 6;
            const float chunkM = 300f;
            int built = 0, rocks = 0; float along = 0f;
            int i0 = 0;
            var rockMeshes = WorldRockMeshes(out Material rockMat);
            var rng = new System.Random(64);
            while (i0 < last)
            {
                var v = new List<Vector3>(); var col = new List<Color32>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tri = new List<int>();
                var combine = new List<CombineInstance>();
                int i1 = i0; float m0 = along;
                for (int i = i0; i <= last; i++)
                {
                    Vector3 a = line[Mathf.Max(0, i - 1)], b = line[Mathf.Min(n - 1, i + 1)];
                    Vector3 t = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
                    Vector3 nrm = new Vector3(-t.z, 0f, t.x);
                    float hw = R.w[i] * 0.5f + 1.2f, y = R.y[i];
                    if (i > i0) along += Vector3.Distance(new Vector3(line[i].x, 0, line[i].z), new Vector3(line[i - 1].x, 0, line[i - 1].z));
                    int baseV = v.Count;
                    for (int k = 0; k <= across; k++)
                    {
                        float u = -1f + 2f * k / across;          // -1 right bank .. +1 left bank
                        Vector3 p = line[i] + nrm * (u * hw);
                        float depth = Mathf.Lerp(-0.3f, 1.6f, 1f - u * u) * Mathf.Clamp(R.w[i] / 18f, 0.6f, 1.6f);
                        float white = R.white[i] * Mathf.Lerp(0.55f, 1f, 1f - Mathf.Abs(u));
                        v.Add(new Vector3(p.x, y, p.z));
                        col.Add(new Color32(DepthRed(depth), 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(white) * 255f), 255));
                        uv.Add(new Vector2(u * hw / 8f, along / 8f));
                        uv2.Add(new Vector2(t.x * R.speed[i], t.z * R.speed[i]));
                    }
                    if (i > i0)
                        for (int k = 0; k < across; k++)
                        {
                            int p0 = baseV - (across + 1) + k, p1 = p0 + 1, q0 = baseV + k, q1 = q0 + 1;
                            tri.Add(p0); tri.Add(q1); tri.Add(q0); tri.Add(p0); tri.Add(p1); tri.Add(q1);
                        }
                    // boulders: in the rapids, and along both banks
                    if (rockMeshes.Count > 0 && i % 2 == 0)
                    {
                        int want = R.white[i] > 0.25f ? 1 + rng.Next(3) : (rng.NextDouble() < 0.35 ? 1 : 0);
                        for (int q = 0; q < want; q++)
                        {
                            bool bank = rng.NextDouble() < 0.45;
                            float u = bank ? (rng.Next(2) == 0 ? -1f : 1f) * (0.85f + 0.25f * (float)rng.NextDouble())
                                           : -0.75f + 1.5f * (float)rng.NextDouble();
                            Vector3 p = line[i] + nrm * (u * (R.w[i] * 0.5f)) + t * ((float)rng.NextDouble() * 6f - 3f);
                            float size = bank ? 1.4f + 2.2f * (float)rng.NextDouble() : 0.7f + 1.6f * (float)rng.NextDouble() * (0.4f + R.white[i]);
                            float gy = bank ? Mathf.Min(WorldGroundY(p.x, p.z), y + 0.6f) : y;
                            if (StageKeepOutRoad(p.x, p.z, size)) continue;
                            var mesh = rockMeshes[rng.Next(rockMeshes.Count)];
                            var rot = Quaternion.Euler((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f - 10f);
                            float ext = Mathf.Max(0.01f, Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z));
                            float s = size / ext;
                            var pos = new Vector3(p.x, gy - mesh.bounds.min.y * s - size * 0.38f, p.z);
                            combine.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, rot, Vector3.one * s) });
                            rocks++;
                        }
                    }
                    i1 = i;
                    if (along - m0 >= chunkM && i < last) break;
                }
                if (tri.Count > 0)
                {
                    var norms = new Vector3[v.Count]; for (int k = 0; k < norms.Length; k++) norms[k] = Vector3.up;
                    var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.SetVertices(v); mesh.SetNormals(norms); mesh.SetColors(col); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv2); mesh.SetTriangles(tri, 0);
                    SaveMesh(mesh, "LureRiver_" + built);
                    WorldWaterGO(parent, "River_" + built, mesh, mat);
                }
                if (combine.Count > 0)
                {
                    var rm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    rm.CombineMeshes(combine.ToArray(), true, true, false);
                    SaveMesh(rm, "LureRiverRocks_" + built);
                    var go = new GameObject("RiverRocks_" + built);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = rm;
                    go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
                    go.isStatic = true;
                    // no collider: below the road's barriers, in or at the water
                }
                built++;
                if (i1 <= i0) break;
                i0 = i1;
            }
            float whiteM = 0f; for (int i = 0; i < last; i++) if (R.white[i] > 0.3f) whiteM += 6f;
            Log($"Stage world river: {built} chunks, {along / 1000f:0.00} km of the Rocky Broad from {R.y[0] + world.baseM:0.0} m down to the lake at {R.y[last] + world.baseM:0.0} m, " +
                $"white water on {whiteM:0} m, {rocks} boulders ({(rockMeshes.Count > 0 ? "NatureManufacture forest stones" : "NO stone meshes: store packs missing")}).");
        }

        /// <summary>Nothing of the river's is put within the road's own reach:
        /// the roadside there is the road builder's.</summary>
        static bool StageKeepOutRoad(float x, float z, float r) =>
            StageCorridor(x, z, RoadWidth * 0.5f + 6f + r, out _);

        static List<Mesh> worldRockMeshes;
        static Material worldRockMat;
        static List<Mesh> WorldRockMeshes(out Material mat)
        {
            if (worldRockMeshes == null)
            {
                worldRockMeshes = new List<Mesh>();
                worldRockMat = NMLitMat("NM_boulder", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01_BC_SM", "T_beech_forest_stones_01_N", null, false,
                                        new Color(0.86f, 0.86f, 0.84f), maskIsHdrp: false);
                for (int k = 1; k <= 14; k++)
                {
                    string p = NMDebrisDir + "/beech_forest_stones_01_" + k + ".fbx";
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                        if (o is Mesh m && m.vertexCount > 24) { worldRockMeshes.Add(m); break; }
                }
            }
            mat = worldRockMat;
            return worldRockMeshes;
        }
    }
}
