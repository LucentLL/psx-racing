using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The Blue Ridge Parkway stage bake — the project's first REAL terrain.
    ///
    /// A circuit derives its land from the road; here the road came from the
    /// land (tools/brp/fetch_brp.mjs sampled it off the real DEM), so the
    /// ground truth is the DEM itself with the same corridor pinning the
    /// circuits use: every point within CorridorR of the centreline is held
    /// level with the road, blending out to the real mountainside over
    /// CorridorBlend. GroundHeightAt branches here whenever a stage's DEM is
    /// loaded, so the pier builder, footings and audits read the mountain
    /// without knowing it is one.
    ///
    /// What is stage-specific and why:
    ///   - GROUND is chunked, not one 144x144 grid: the run is 7 km long and
    ///     one grid sized to its bounds would have 50 m cells. Near chunks
    ///     (12 m cells, colliders) carry the drivable world; far chunks (60 m
    ///     cells, no colliders, painted as autumn forest) carry the vista and
    ///     sit 0.4 m low so the overlap ring never z-fights.
    ///   - WALLS are the parkway's own low stone guard walls, and only where
    ///     the mountain actually falls away — plus both sides of every bridge
    ///     deck. The uphill side is open: the slope is the barrier.
    ///   - The FOREST is the point. Thousands of crossed-quad billboards from
    ///     the CC0 retro tree pack, merged per chunk into one mesh on one
    ///     atlas material, on the Foliage layer so StageCulling can clip them
    ///     at ~500 m. Species follow the mountain: spruce-fir climbs with
    ///     elevation, the fall colours cluster the way a hillside does, cliffs
    ///     stay bare.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        // ------------------------------------------------------------------
        //  Stage constants
        // ------------------------------------------------------------------
        /// <summary>Barrier line for the stage: the parkway's guard wall hugs
        /// the shoulder. RoadWidth/2 (4.75) + 1.15 m of verge.</summary>
        internal const float StageWallOffset = 5.9f;

        /// <summary>The barrier line for a given venue — what the audits must
        /// measure to. The circuits' 10 m constant is wrong for the stage,
        /// whose guard walls hug the shoulder.</summary>
        internal static float WallOffsetFor(TrackCatalog.TrackDef def) =>
            def != null && def.stage ? StageWallOffset : WallOffset;
        /// <summary>Fog band multiplier over the hour presets. 3.2 puts noon's
        /// 355 m fogFar at ~1.1 km — the far wall of the valley, hazy, which
        /// is what the Blue Ridge is named for.</summary>
        const float StageFogScale = 3.2f;
        const float StageFarClip = 1500f;

        /// <summary>How far from the centreline geometry trees exist. Past
        /// this the far slopes are painted as forest by the mottle texture,
        /// which at 150 m+ through PSX fog is indistinguishable.</summary>
        const float ForestBand = 150f;
        /// <summary>Candidate grid pitch for the forest. Appalachian cove
        /// forest is nearly closed canopy; 13 m of billboard spacing reads as
        /// that once the crowns are 10 m wide.</summary>
        const float ForestPitch = 13f;

        const float NearCell = 12f, NearCoverage = 340f, NearChunk = 240f;
        const float FarCell = 60f, FarCoverage = 2300f, FarChunk = 960f;
        /// <summary>Near chunks further than this from the route skip their
        /// MeshCollider — nothing drivable ever gets there, and cooked
        /// collision for a mountainside is pure load time.</summary>
        const float ColliderBand = 120f;
        /// <summary>Far mesh drops this far so the near/far overlap ring can
        /// never z-fight. Invisible at the 300 m+ where far terrain lives.</summary>
        const float FarSink = 0.4f;

        const string StageArtDir = Root + "/Art/BRP";
        const string StageGenDir = Root + "/Art/BRP/Gen";
        const string TreesSrcDir =
            @"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\ultimate_retro_tree_pack\ultimate_retro_tree_pack\textures";

        // ------------------------------------------------------------------
        //  Stage DEM state
        // ------------------------------------------------------------------
        [Serializable] class DemGridMeta { public float originX, originZ, cell; public int cols, rows; }
        [Serializable] class DemMeta { public float baseM; public DemGridMeta near, far; }

        static bool stageDemLoaded;
        static short[] demNear, demFar;          // decimetres above baseM
        static DemGridMeta demNearMeta, demFarMeta;
        static List<Vector3> stageWp;            // the waypoints, world space
        static Dictionary<long, List<int>> stageHash;
        const float StageHashCell = 48f;

        static void StageUnloadDem()
        {
            stageDemLoaded = false;
            demNear = demFar = null;
            stageWp = null; stageHash = null;
        }

        /// <summary>Load the fetch script's bake and copy/generate the stage
        /// art. Called before ANY stage height is asked for.</summary>
        static void StageLoadDem()
        {
            string metaPath = ProjectRootPath(StageArtDir + "/brp_dem_meta.json");
            if (!File.Exists(metaPath))
                throw new Exception("Stage DEM missing — run tools/brp/fetch_brp.mjs first (" + metaPath + ")");
            var meta = JsonUtility.FromJson<DemMeta>(File.ReadAllText(metaPath));
            demNearMeta = meta.near; demFarMeta = meta.far;
            demNear = ReadDemBytes(StageArtDir + "/brp_dem_near.bytes", meta.near);
            demFar = ReadDemBytes(StageArtDir + "/brp_dem_far.bytes", meta.far);

            // The waypoints, for the corridor hash. TrackCatalog has already
            // loaded them (BuildWaypoints ran Sample), but ask again so this
            // does not depend on call order.
            TrackCatalog.EnsureStage(track);
            stageWp = new List<Vector3>(track.stagePts);
            stageHash = new Dictionary<long, List<int>>();
            for (int i = 0; i < stageWp.Count; i++)
            {
                long k = HashKey(stageWp[i].x, stageWp[i].z);
                if (!stageHash.TryGetValue(k, out var list)) stageHash[k] = list = new List<int>();
                list.Add(i);
            }
            stageDemLoaded = true;

            EnsureStageArt();
            GenerateStageTextures();
            AssetDatabase.Refresh();
            Log($"Stage DEM loaded: near {meta.near.cols}x{meta.near.rows} @ {meta.near.cell} m, " +
                $"far {meta.far.cols}x{meta.far.rows} @ {meta.far.cell} m, base {meta.baseM} m ASL.");
        }

        static short[] ReadDemBytes(string assetPath, DemGridMeta m)
        {
            var bytes = File.ReadAllBytes(ProjectRootPath(assetPath));
            if (bytes.Length != m.cols * m.rows * 2)
                throw new Exception($"{assetPath}: {bytes.Length} bytes, expected {m.cols * m.rows * 2}");
            var grid = new short[m.cols * m.rows];
            Buffer.BlockCopy(bytes, 0, grid, 0, bytes.Length);
            return grid;
        }

        static long HashKey(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / StageHashCell);
            int cz = Mathf.FloorToInt(z / StageHashCell);
            return ((long)cx << 32) ^ (uint)cz;
        }

        // ------------------------------------------------------------------
        //  Heights
        // ------------------------------------------------------------------
        static float DemBilinear(short[] grid, DemGridMeta m, float x, float z)
        {
            float fx = (x - m.originX) / m.cell;
            float fz = (z - m.originZ) / m.cell;
            int c0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, m.cols - 2);
            int r0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, m.rows - 2);
            float tx = Mathf.Clamp01(fx - c0), tz = Mathf.Clamp01(fz - r0);
            float h00 = grid[r0 * m.cols + c0], h01 = grid[r0 * m.cols + c0 + 1];
            float h10 = grid[(r0 + 1) * m.cols + c0], h11 = grid[(r0 + 1) * m.cols + c0 + 1];
            return (Mathf.Lerp(Mathf.Lerp(h00, h01, tx), Mathf.Lerp(h10, h11, tx), tz)) * 0.1f;
        }

        /// <summary>Raw DEM height (world Y), from the near grid where it
        /// covers, the far grid beyond.</summary>
        static float StageDemY(float x, float z)
        {
            var m = demNearMeta;
            if (x > m.originX + m.cell && x < m.originX + (m.cols - 2) * m.cell &&
                z > m.originZ + m.cell && z < m.originZ + (m.rows - 2) * m.cell)
                return DemBilinear(demNear, m, x, z);
            return DemBilinear(demFar, demFarMeta, x, z);
        }

        /// <summary>
        /// Nearest point on the centreline within <paramref name="reach"/>:
        /// distance, the road height there, and how much bridge (BridgeBlend)
        /// that station carries. False when the route is further than reach.
        /// </summary>
        static bool StageCorridor(float x, float z, float reach,
                                  out float d, out float roadY, out float bridge)
        {
            d = float.MaxValue; roadY = 0f; bridge = 0f;
            int cells = Mathf.CeilToInt(reach / StageHashCell);
            int cx = Mathf.FloorToInt(x / StageHashCell);
            int cz = Mathf.FloorToInt(z / StageHashCell);
            int best = -1; float bestD2 = reach * reach;
            for (int oz = -cells; oz <= cells; oz++)
                for (int ox = -cells; ox <= cells; ox++)
                {
                    long k = ((long)(cx + ox) << 32) ^ (uint)(cz + oz);
                    if (!stageHash.TryGetValue(k, out var list)) continue;
                    foreach (int i in list)
                    {
                        float dx = stageWp[i].x - x, dz = stageWp[i].z - z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 < bestD2) { bestD2 = d2; best = i; }
                    }
                }
            if (best < 0) return false;

            // Refine on the two segments touching the nearest waypoint, same
            // as the circuit field does — the shelf must follow the LINE, not
            // step from waypoint to waypoint.
            int n = stageWp.Count;
            d = Mathf.Sqrt(bestD2);
            roadY = stageWp[best].y;
            float station = best;
            for (int o = -1; o <= 0; o++)
            {
                int a = Mathf.Clamp(best + o, 0, n - 1);
                int b = Mathf.Min(a + 1, n - 1);
                if (a == b) continue;
                float ax = stageWp[a].x, az = stageWp[a].z;
                float ex = stageWp[b].x - ax, ez = stageWp[b].z - az;
                float len2 = ex * ex + ez * ez;
                if (len2 < 1e-6f) continue;
                float t = Mathf.Clamp01(((x - ax) * ex + (z - az) * ez) / len2);
                float px = ax + ex * t, pz = az + ez * t;
                float dd = Mathf.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z));
                if (dd < d)
                {
                    d = dd;
                    roadY = Mathf.Lerp(stageWp[a].y, stageWp[b].y, t);
                    station = a + t;
                }
            }
            if (bridgeBlend != null)
            {
                int s0 = Mathf.Clamp(Mathf.FloorToInt(station), 0, bridgeBlend.Length - 1);
                int s1 = Mathf.Min(s0 + 1, bridgeBlend.Length - 1);
                bridge = Mathf.Lerp(bridgeBlend[s0], bridgeBlend[s1], station - s0);
            }
            return true;
        }

        /// <summary>
        /// Ground height on the stage: the real DEM, with the road corridor
        /// pinned exactly the way the circuits pin theirs — and released back
        /// to the real slope through a bridge span, where the deck carries the
        /// road and the mountainside is allowed to fall away underneath.
        /// </summary>
        static float StageGroundHeightAt(float x, float z)
        {
            float dem = StageDemY(x, z);
            if (!StageCorridor(x, z, CorridorR + CorridorBlend + 4f,
                    out float d, out float roadY, out float f))
                return dem;

            float blend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(CorridorR, CorridorR + CorridorBlend, d));
            float pinned = Mathf.Lerp(roadY, dem, blend)
                         - CorridorSink * (1f - blend);

            if (f <= 0.001f) return pinned;

            // Inside a span the pin releases to the real slope. Under the deck
            // footprint itself the ground is additionally capped below the
            // soffit: the DEM is 30 m posts and the road was smoothed, so at
            // mid-span the raw slope can disagree with the deck by a couple of
            // metres, and ground poking up through a bridge is the one failure
            // everyone would see.
            float released = Mathf.Lerp(pinned, dem, f);
            if (d < DeckHalfWidth + 2f)
            {
                float cap = roadY - DeckThick - 0.4f;
                released = Mathf.Lerp(released, Mathf.Min(released, cap), f);
            }
            return released;
        }

        // ------------------------------------------------------------------
        //  Ground meshes
        // ------------------------------------------------------------------
        /// <summary>Coarse plan distance from a point to the route — for chunk
        /// keep/skip decisions, sampled every 8th waypoint. Not for geometry.</summary>
        static float RouteDistanceCoarse(float x, float z)
        {
            float best2 = float.MaxValue;
            for (int i = 0; i < stageWp.Count; i += 8)
            {
                float dx = stageWp[i].x - x, dz = stageWp[i].z - z;
                float d2 = dx * dx + dz * dz;
                if (d2 < best2) best2 = d2;
            }
            return Mathf.Sqrt(best2);
        }

        static void BuildStageGround(List<Vector3> pts, Transform parent)
        {
            var root = new GameObject("Ground");
            root.transform.SetParent(parent, false);

            // The near ground carries a warm autumn tint: untinted, the dirt
            // texture reads grey-green against the mottle's orange and the
            // border between the two draws itself as a band across the hills.
            var nearMat = MakeMat(MeshPrefix + "Ground", theme.ground, affine: 0f,
                                  tint: new Color(1.0f, 0.90f, 0.70f));
            var farMat = MakeMat(MeshPrefix + "GroundFar", StageGenDir + "/FallMottle.png", affine: 0f);

            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);

            int nearChunks = 0, farChunks = 0, nearVerts = 0, farVerts = 0, withColl = 0;

            // NEAR: 12 m cells in 240 m chunks over the corridor band.
            ForEachChunk(b, NearChunk, NearCoverage, (cx, cz, ox, oz) =>
            {
                float mid = RouteDistanceCoarse(ox + NearChunk * 0.5f, oz + NearChunk * 0.5f);
                if (mid > NearCoverage + NearChunk * 0.71f) return;
                var mesh = GridChunkMesh(ox, oz, NearChunk, NearCell, theme.groundTile, 0f);
                nearVerts += mesh.vertexCount;
                var go = ChunkGO(root.transform, "GroundN_" + cx + "_" + cz, mesh,
                                 nearMat, ox, oz, "StageGroundN_" + cx + "_" + cz);
                if (mid < ColliderBand + NearChunk * 0.71f)
                {
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    withColl++;
                }
                nearChunks++;
            });

            // FAR: 60 m cells in 960 m chunks out to the fog wall, skipping
            // cells the near band already covers (minus one cell of overlap so
            // the seam is sealed; FarSink hides the doubled ring).
            ForEachChunk(b, FarChunk, FarCoverage, (cx, cz, ox, oz) =>
            {
                float mid = RouteDistanceCoarse(ox + FarChunk * 0.5f, oz + FarChunk * 0.5f);
                if (mid > FarCoverage + FarChunk * 0.71f) return;
                if (mid + FarChunk * 0.71f < NearCoverage - FarCell) return;   // fully under near
                var mesh = GridChunkMesh(ox, oz, FarChunk, FarCell, 300f, -FarSink);
                farVerts += mesh.vertexCount;
                ChunkGO(root.transform, "GroundF_" + cx + "_" + cz, mesh,
                        farMat, ox, oz, "StageGroundF_" + cx + "_" + cz);
                farChunks++;
            });

            Log($"Stage ground: {nearChunks} near chunks ({nearVerts} verts, {withColl} with colliders), " +
                $"{farChunks} far chunks ({farVerts} verts).");
        }

        static void ForEachChunk(Bounds b, float chunk, float coverage,
                                 Action<int, int, float, float> visit)
        {
            int x0 = Mathf.FloorToInt((b.min.x - coverage) / chunk);
            int x1 = Mathf.CeilToInt((b.max.x + coverage) / chunk);
            int z0 = Mathf.FloorToInt((b.min.z - coverage) / chunk);
            int z1 = Mathf.CeilToInt((b.max.z + coverage) / chunk);
            for (int cz = z0; cz < z1; cz++)
                for (int cx = x0; cx < x1; cx++)
                    visit(cx, cz, cx * chunk, cz * chunk);
        }

        /// <summary>One terrain chunk, verts local to its origin, heights from
        /// the stage field. UVs in world metres so the texture is continuous
        /// across chunk seams — and NORMALS from the height field itself, for
        /// the same reason: RecalculateNormals only sees this chunk's
        /// triangles, so two chunks disagree along their shared edge and the
        /// border becomes a hard lighting seam across the hillside.</summary>
        static Mesh GridChunkMesh(float ox, float oz, float size, float cell,
                                  float tile, float yOffset)
        {
            int cells = Mathf.RoundToInt(size / cell);
            var verts = new Vector3[(cells + 1) * (cells + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var tris = new int[cells * cells * 6];
            for (int gz = 0, v = 0; gz <= cells; gz++)
                for (int gx = 0; gx <= cells; gx++, v++)
                {
                    float wx = ox + gx * cell, wz = oz + gz * cell;
                    verts[v] = new Vector3(gx * cell, StageGroundHeightAt(wx, wz) + yOffset, gz * cell);
                    uvs[v] = new Vector2(wx / tile, wz / tile);
                    // Central differences at half a cell: a function of world
                    // position alone, so both sides of a chunk border compute
                    // the identical normal.
                    float e = cell * 0.5f;
                    float dhdx = (StageGroundHeightAt(wx + e, wz) - StageGroundHeightAt(wx - e, wz)) / (2f * e);
                    float dhdz = (StageGroundHeightAt(wx, wz + e) - StageGroundHeightAt(wx, wz - e)) / (2f * e);
                    norms[v] = new Vector3(-dhdx, 1f, -dhdz).normalized;
                }
            int t = 0;
            for (int gz = 0; gz < cells; gz++)
                for (int gx = 0; gx < cells; gx++)
                {
                    int v = gz * (cells + 1) + gx;
                    tris[t++] = v; tris[t++] = v + cells + 1; tris[t++] = v + cells + 2;
                    tris[t++] = v; tris[t++] = v + cells + 2; tris[t++] = v + 1;
                }
            return new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts, normals = norms, uv = uvs, triangles = tris,
            };
        }

        static GameObject ChunkGO(Transform parent, string name, Mesh mesh,
                                  Material mat, float ox, float oz, string meshName)
        {
            SaveMesh(mesh, meshName);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(ox, 0f, oz);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------
        //  Guard walls
        // ------------------------------------------------------------------
        /// <summary>Visible stone parapet height. The parkway's guard walls
        /// are LOW — you see the valley over them, which is the point.</summary>
        const float StageWallH = 0.85f;
        /// <summary>Collider height above the visible stone. Low walls stop
        /// bumpers, not cars arriving at 120 km/h and 15 degrees — the extra
        /// (invisible) collider is what keeps a race on the mountain, and it
        /// coincides with a visible wall so it never reads as a force field.</summary>
        const float StageWallCollH = 1.7f;
        /// <summary>The mountain must fall at least this far, this close, for
        /// a guard wall to appear. Sampled from the RAW dem 30 m out —
        /// sampling the pinned field would measure the corridor's own shelf.</summary>
        const float WallDropM = 5.0f;

        static void BuildStageWalls(List<Vector3> pts, Transform parent)
        {
            int n = pts.Count;
            var mat = MakeMat(MeshPrefix + "Wall", theme.wall, affine: 0f);
            var phys = GetOrCreatePhysMat("WallPhys", 0.05f, 0.05f);
            var root = new GameObject("Walls");
            root.transform.SetParent(parent, false);

            int runs = 0, walled = 0;
            foreach (float side in new[] { -1f, 1f })
            {
                // Decide per waypoint, then emit maximal runs. The drop test
                // hysteresis (two clear waypoints end a run) is what keeps a
                // wall that flickers on and off along a marginal slope from
                // becoming a picket line of two-metre stubs.
                var want = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    if (bridgeBlend != null && bridgeBlend[i] > 0.35f) { want[i] = true; continue; }
                    Vector3 right = RightAt(pts, i);
                    float px = pts[i].x + right.x * side * 30f;
                    float pz = pts[i].z + right.z * side * 30f;
                    want[i] = pts[i].y - StageDemY(px, pz) > WallDropM;
                }
                for (int i = 0; i < n; )
                {
                    if (!want[i]) { i++; continue; }
                    int len = 1;
                    int clear = 0;
                    while (i + len < n && clear < 3)
                    {
                        if (want[i + len]) clear = 0; else clear++;
                        len++;
                    }
                    len -= clear;
                    if (len >= 4)
                    {
                        BuildOneStageWall(pts, i, len, side, root.transform, mat, phys, runs++);
                        walled += len;
                    }
                    i += len + clear;
                }
            }
            Log($"Stage guard walls: {runs} runs covering {walled * Spacing:0} m of shoulder " +
                $"(of {n * Spacing * 2:0} m of roadside).");
        }

        static void BuildOneStageWall(List<Vector3> pts, int from, int stations, float side,
                                      Transform parent, Material mat, PhysicsMaterial phys, int no)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float dist = 0f;
            int rings = 0;
            for (int k = 0; k < stations; k++)
            {
                int i = Mathf.Min(from + k, pts.Count - 1);
                Vector3 right = RightAt(pts, i);
                Vector3 basePos = pts[i] + right * side * StageWallOffset;
                // Seated on the corridor shelf, which is pinned to the road —
                // minus a skirt so a coarse ground facet can never show
                // daylight under the masonry.
                basePos.y = pts[i].y - 0.45f;
                Vector3 top = basePos + Vector3.up * (0.45f + StageWallH);
                int v = verts.Count;
                verts.Add(basePos); verts.Add(top);
                uvs.Add(new Vector2(dist / 3.2f, 0f));
                uvs.Add(new Vector2(dist / 3.2f, (0.45f + StageWallH) / 3.2f));
                if (k > 0)
                {
                    // One winding, facing the ROAD. The far face looks over the
                    // valley where no camera goes; the crossed-quad trick the
                    // trees use does not apply, but nothing ever sees it.
                    if (side < 0f) { tris.AddRange(new[] { v - 2, v - 1, v, v - 1, v + 1, v }); }
                    else { tris.AddRange(new[] { v - 2, v, v - 1, v - 1, v, v + 1 }); }
                }
                dist += Spacing;
                rings++;

                // Collider boxes PER STATION, one 4 m chord each: a box
                // spanning four stations cuts the corner — at the stage's
                // 27 m minimum radius a 16 m chord sags 1.2 m inside the wall
                // line, which the obstacle audit correctly reported as an
                // invisible face reaching into the kerb band at 151 spots.
                // A 4 m chord sags 7 cm. The box also sits slightly OUTSIDE
                // the stone (6.05 vs 5.9) so its face never leads the visual.
                // (A MeshCollider on the single-sided ribbon would be worse
                // still: single-sided contacts, cars nosing through.)
                if (k + 1 < stations)
                {
                    int j = Mathf.Min(from + k + 1, pts.Count - 1);
                    Vector3 a = pts[i] + RightAt(pts, i) * side * (StageWallOffset + 0.15f);
                    Vector3 bPos = pts[j] + RightAt(pts, j) * side * (StageWallOffset + 0.15f);
                    var seg = new GameObject("WallColl");
                    seg.transform.SetParent(parent, false);
                    seg.transform.position = (a + bPos) * 0.5f + Vector3.up * (StageWallCollH * 0.5f - 0.2f);
                    Vector3 dir = bPos - a; dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
                    seg.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    var box = seg.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.4f, StageWallCollH + 0.4f, dir.magnitude + 0.5f);
                    box.sharedMaterial = phys;
                    seg.layer = SolidLayer;
                    seg.isStatic = true;
                }
            }
            var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
            SaveMesh(mesh, "StageWall" + no);
            var go = new GameObject("Wall" + no);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        // ------------------------------------------------------------------
        //  Forest
        // ------------------------------------------------------------------
        /// <summary>One species: where it sits in the 4x4 atlas, what shape it
        /// is, and how tall. Heights are game-scale (a 4.3 m car), not
        /// botany.</summary>
        struct TreeSpecies
        {
            public string file; public int col, row;
            public bool conifer; public float height;
            public TreeSpecies(string f, int c, int r, bool k, float h)
            { file = f; col = c; row = r; conifer = k; height = h; }
        }

        // The picks from the CC0 "Ultimate Retro PSX Tree Pack" (elegantcrow,
        // itch.io) that read as southern Appalachian fall: maples, oaks and
        // hickories in colour, two greens still holding, spruce-fir for the
        // high ground, one bare late-fall crown.
        static readonly TreeSpecies[] StageTrees =
        {
            new TreeSpecies("tree016", 0, 0, false, 11.0f),   // tulip poplar gold
            new TreeSpecies("tree017", 1, 0, false, 10.5f),   // orange maple
            new TreeSpecies("tree019", 2, 0, false, 12.0f),   // big orange-red
            new TreeSpecies("tree020", 3, 0, false, 10.0f),   // orange
            new TreeSpecies("tree021", 0, 1, false, 11.0f),   // gold
            new TreeSpecies("tree022", 1, 1, false, 11.0f),   // red over green
            new TreeSpecies("tree025", 2, 1, false, 9.5f),    // russet oak
            new TreeSpecies("tree028", 3, 1, false, 9.0f),    // scarlet maple
            new TreeSpecies("tree030", 0, 2, false, 10.5f),   // brown oak
            new TreeSpecies("tree018", 1, 2, false, 10.0f),   // yellow-green
            new TreeSpecies("tree027", 2, 2, false, 10.5f),   // green hardwood
            new TreeSpecies("tree112", 3, 2, false, 11.0f),   // green hardwood
            new TreeSpecies("tree066", 0, 3, true, 12.0f),    // spruce
            new TreeSpecies("tree057", 1, 3, true, 12.5f),    // dark spruce
            new TreeSpecies("tree061", 2, 3, true, 11.0f),    // fir
            new TreeSpecies("tree008", 3, 3, false, 9.0f),    // bare late-fall
        };

        // Indexes into StageTrees by palette group, for the cluster picker.
        static readonly int[] FallGroup = { 0, 1, 3, 4, 6, 8 };
        static readonly int[] RedGroup = { 2, 5, 7 };
        static readonly int[] GreenGroup = { 9, 10, 11 };
        static readonly int[] ConiferGroup = { 12, 13, 14 };
        const int BareIdx = 15;

        static void EnsureStageArt()
        {
            foreach (var dir in new[] { StageArtDir + "/Trees", StageGenDir })
                if (!AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.CreateFolder(
                        Path.GetDirectoryName(dir).Replace('\\', '/'), Path.GetFileName(dir));
            int copied = 0;
            foreach (var s in StageTrees)
            {
                string dst = ProjectRootPath(StageArtDir + "/Trees/" + s.file + ".png");
                if (File.Exists(dst)) continue;
                string src = Path.Combine(TreesSrcDir, s.file + ".png");
                if (!File.Exists(src)) throw new Exception("Tree source missing: " + src);
                File.Copy(src, dst);
                copied++;
            }
            if (copied > 0) Log($"Copied {copied} tree billboards from the CC0 pack.");
        }

        /// <summary>The 4x4, 512px tree atlas, composed from the copied pack
        /// billboards — one material for the whole forest is what keeps ten
        /// thousand trees at a few dozen draw calls.</summary>
        static void GenerateStageTextures()
        {
            string atlasPath = StageGenDir + "/TreeAtlas.png";
            if (!File.Exists(ProjectRootPath(atlasPath)))
            {
                const int cellPx = 128, atlasPx = cellPx * 4;
                var px = new Color32[atlasPx * atlasPx];
                foreach (var s in StageTrees)
                {
                    var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    src.LoadImage(File.ReadAllBytes(ProjectRootPath(StageArtDir + "/Trees/" + s.file + ".png")));
                    var sp = src.GetPixels32();
                    int sw = src.width, sh = src.height;
                    for (int y = 0; y < cellPx; y++)
                        for (int x = 0; x < cellPx; x++)
                        {
                            // Point-sample; sources are 128 already, but stay
                            // correct if the pack ever ships another size.
                            int sx = Mathf.Clamp(x * sw / cellPx, 0, sw - 1);
                            int sy = Mathf.Clamp(y * sh / cellPx, 0, sh - 1);
                            px[(s.row * cellPx + y) * atlasPx + s.col * cellPx + x] = sp[sy * sw + sx];
                        }
                    UnityEngine.Object.DestroyImmediate(src);
                }
                var tex = new Texture2D(atlasPx, atlasPx, TextureFormat.RGBA32, false);
                tex.SetPixels32(px); tex.Apply();
                File.WriteAllBytes(ProjectRootPath(atlasPath), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                Log("Tree atlas composed: " + atlasPath);
            }

            // The far slopes: an autumn mottle so terrain past the tree band
            // still reads as forest. Low-frequency colour clumps, like the
            // reference photo, not confetti. The frequencies are chosen
            // incommensurate with each other AND with the 256 px tile — a
            // single dominant (x+y) term drew a 45-degree stripe across every
            // mountain in the overview shot, which is what a plaid looks like
            // draped over a ridge.
            WriteTexture(StageGenDir + "/FallMottle.png", 256, 256, (x, y) =>
            {
                // Wrap-friendly: all terms are sin/cos of k * 2pi * n / 256 so
                // the tile edge is seamless.
                float u = x * (Mathf.PI * 2f / 256f), v = y * (Mathf.PI * 2f / 256f);
                float n1 = Mathf.Sin(u * 3f + 1.3f) * Mathf.Cos(v * 2f)
                         + 0.8f * Mathf.Sin(u * 5f - v * 3f + 0.7f) * Mathf.Cos(u * 2f + v * 4f)
                         + 0.6f * Mathf.Cos(u * 7f + v * 5f + 2.9f) * Mathf.Sin(v * 3f - u * 1f);
                float n2 = Mathf.Sin(u * 13f + 2.3f) * Mathf.Cos(v * 11f - 1.1f)
                         + 0.5f * Mathf.Sin(u * 23f - v * 17f);
                Color32 c;
                if (n1 > 1.15f) c = new Color32(146, 64, 34, 255);        // red maple
                else if (n1 > 0.6f) c = new Color32(164, 108, 36, 255);   // orange
                else if (n1 > 0.1f) c = new Color32(140, 114, 40, 255);   // gold
                else if (n1 > -0.65f) c = new Color32(82, 90, 42, 255);   // olive green
                else c = new Color32(48, 64, 40, 255);                    // conifer dark
                // fine grain so the tile does not band
                int g = (int)(n2 * 10f);
                return new Color32((byte)Mathf.Clamp(c.r + g, 0, 255),
                                   (byte)Mathf.Clamp(c.g + g, 0, 255),
                                   (byte)Mathf.Clamp(c.b + g, 0, 255), 255);
            });

            // The shoulder: the parkway runs tarmac into a mown gravel-grass
            // verge, not a red-and-white racing kerb.
            WriteTexture(StageGenDir + "/Shoulder.png", 32, 16, (x, y) =>
            {
                int h = (x * 7 + y * 13) % 17;
                byte v = (byte)(96 + (h * 5) % 28);
                return new Color32(v, (byte)(v - 8), (byte)(v - 22), 255);
            });
        }

        /// <summary>Where the fall colour clumps. Same three-sine recipe as
        /// ReliefNoise so it is identical wherever it is evaluated.</summary>
        static float ForestClusterNoise(float x, float z)
        {
            return 0.6f * Mathf.Sin(x * 0.011f + 2.4f) * Mathf.Cos(z * 0.0093f)
                 + 0.4f * Mathf.Sin((x + z) * 0.0061f + 0.8f);
        }

        static void BuildStageForest(List<Vector3> pts, Transform parent)
        {
            var root = new GameObject("Forest");
            root.transform.SetParent(parent, false);

            var mat = MakeMat(MeshPrefix + "Forest", StageGenDir + "/TreeAtlas.png", cutoff: 0.5f);
            var rng = new System.Random(41);
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);

            float roadHalf = RoadWidth * 0.5f;
            int planted = 0, cliffSkip = 0, chunks = 0, underDeck = 0;

            ForEachChunk(b, NearChunk, ForestBand + 20f, (cx, cz, ox, oz) =>
            {
                float mid = RouteDistanceCoarse(ox + NearChunk * 0.5f, oz + NearChunk * 0.5f);
                if (mid > ForestBand + NearChunk * 0.75f) return;

                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();

                int cells = Mathf.RoundToInt(NearChunk / ForestPitch);
                for (int gz = 0; gz < cells; gz++)
                    for (int gx = 0; gx < cells; gx++)
                    {
                        float wx = ox + (gx + 0.18f + (float)rng.NextDouble() * 0.64f) * ForestPitch;
                        float wz = oz + (gz + 0.18f + (float)rng.NextDouble() * 0.64f) * ForestPitch;

                        if (!StageCorridor(wx, wz, ForestBand + 10f,
                                out float d, out float roadY, out float f)) continue;
                        if (d > ForestBand) continue;

                        // thin the outer band — the mottle takes over anyway
                        if (d > 90f && rng.NextDouble() <
                            Mathf.InverseLerp(90f, ForestBand, d) * 0.55f) continue;

                        float ground = StageGroundHeightAt(wx, wz);

                        // True cliffs stay bare — the boulder fields and rock
                        // faces under Grandfather are real, and 50 degrees is
                        // where forest actually gives out. Appalachian cove
                        // forest happily holds a 40 degree slope, so the
                        // threshold errs toward planting.
                        float s1 = StageDemY(wx + 8f, wz) - StageDemY(wx - 8f, wz);
                        float s2 = StageDemY(wx, wz + 8f) - StageDemY(wx, wz - 8f);
                        if (s1 * s1 + s2 * s2 > 19f * 19f) { cliffSkip++; continue; }

                        // pick the species before the shoulder test — the
                        // height decides whether it fits under a deck
                        int idx = PickSpecies(rng, wx, wz, ground);
                        var sp = StageTrees[idx];
                        float h = sp.height * (0.85f + (float)rng.NextDouble() * 0.4f);

                        bool deckOver = f > 0.4f && roadY - ground > h + 3.5f;
                        if (deckOver)
                        {
                            // Down on the slope with the deck riding over the
                            // canopy — the Linn Cove look. Any lateral offset
                            // is fine; the tree is metres BELOW the road.
                            underDeck++;
                        }
                        else if (d < roadHalf + 2.6f || (d < StageWallOffset + 1.2f && f > 0.35f))
                        {
                            // On the tarmac, the shoulder, or through a
                            // parapet. 2.6 m past the kerb is the same margin
                            // the circuits give their tree line.
                            continue;
                        }

                        planted++;
                        AddTreeQuads(verts, uvs, tris, sp,
                            new Vector3(wx - ox, ground - 0.25f, wz - oz), h,
                            (float)rng.NextDouble() * 360f);
                    }

                if (verts.Count == 0) return;
                var mesh = new Mesh
                {
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                    vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray(),
                };
                var go = ChunkGO(root.transform, "Forest_" + cx + "_" + cz, mesh, mat,
                                 ox, oz, "StageForest_" + cx + "_" + cz);
                go.layer = FoliageLayer;
                chunks++;
            });

            Log($"Stage forest: {planted} trees in {chunks} chunks " +
                $"({underDeck} under bridge decks, {cliffSkip} sites left bare as cliff).");
        }

        static int PickSpecies(System.Random rng, float x, float z, float groundY)
        {
            double roll = rng.NextDouble();
            // Spruce-fir climbs with elevation — the top of the run is real
            // red-spruce country. groundY is world (baseM-relative).
            float elev = Mathf.InverseLerp(35f, 105f, groundY);
            if (roll < 0.05) return BareIdx;
            if (roll < 0.05 + 0.06f + 0.30f * elev)
                return ConiferGroup[rng.Next(ConiferGroup.Length)];
            float c = ForestClusterNoise(x, z);
            if (c > 0.42f) return RedGroup[rng.Next(RedGroup.Length)];
            if (c < -0.5f) return GreenGroup[rng.Next(GreenGroup.Length)];
            return FallGroup[rng.Next(FallGroup.Length)];
        }

        static void AddTreeQuads(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                 TreeSpecies sp, Vector3 basePos, float h, float yawDeg)
        {
            float w = h * (sp.conifer ? 0.62f : 1.0f);
            const float pad = 1.5f / 512f;
            float u0 = sp.col * 0.25f + pad, u1 = (sp.col + 1) * 0.25f - pad;
            // No flip: the atlas compositor writes through SetPixels32, whose
            // array origin is the texture's BOTTOM-left — so species row 0
            // already lives at v 0..0.25, in the same bottom-up space UVs use.
            float v0 = sp.row * 0.25f + pad, v1 = (sp.row + 1) * 0.25f - pad;
            for (int q = 0; q < 2; q++)
            {
                Quaternion rot = Quaternion.Euler(0f, yawDeg + q * 90f, 0f);
                int v = verts.Count;
                verts.Add(basePos + rot * new Vector3(-w * 0.5f, 0f, 0f));
                verts.Add(basePos + rot * new Vector3(-w * 0.5f, h, 0f));
                verts.Add(basePos + rot * new Vector3(w * 0.5f, h, 0f));
                verts.Add(basePos + rot * new Vector3(w * 0.5f, 0f, 0f));
                uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v1));
                uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0));
                // One winding — each plane is seen from behind via the other
                // plane of the cross, exactly like PlaceTrees.
                tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
        }
    }
}
