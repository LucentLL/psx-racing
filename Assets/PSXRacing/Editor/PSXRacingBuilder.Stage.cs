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
        /// <summary>Metres of verge between the tarmac edge and the stage's
        /// guard wall. The parkway's wall hugs the shoulder, and most of this
        /// is the kerb strip, so what is left is a hand's breadth of gravel.
        /// </summary>
        internal const float StageVerge = 1.15f;

        /// <summary>How far from the centreline the corridor shelf stays dead
        /// level: the tarmac, its kerb strip, the slab batter, the guard wall,
        /// and a hand's breadth of gravel behind it. Everything further out is
        /// hillside.</summary>
        static float StageVergeFlat => StageWallOffset + 0.7f;

        /// <summary>
        /// How fast the shoulder falls away past <see cref="StageVergeFlat"/>,
        /// in metres down per metre out. 0.55 is about 1 in 1.8 — the batter a
        /// highway fill is built to, and steep enough that a car which leaves
        /// the road is leaving it rather than parking beside it.
        /// </summary>
        const float StageVergeSlope = 0.55f;

        /// <summary>Metres the shoulder has dropped by distance <paramref
        /// name="d"/> from the centreline, before the mountain has its say.
        /// </summary>
        static float StageVergeFallAt(float d) =>
            d <= StageVergeFlat ? 0f : (d - StageVergeFlat) * StageVergeSlope;

        /// <summary>
        /// Barrier line for a stage, measured from ITS road.
        ///
        /// This was frozen at 5.9 — "RoadWidth/2 (4.75) + 1.15 m of verge",
        /// which is exactly right for the 9.5 m parkway it was written for and
        /// wrong for every stage added afterwards. Langston is 11 m wide and
        /// Atlantic Beach 13, so a wall drawn at 5.9 m stood 0.6 m and 1.6 m
        /// INSIDE the tarmac: 692 and 584 collider segments of invisible
        /// masonry down the driving surface, which the obstacle audit reported
        /// the moment it was asked. Derived from the width, it lands back on
        /// 5.9 for the parkway and moves out for the rest.
        /// </summary>
        internal static float StageWallOffsetFor(TrackCatalog.TrackDef def) =>
            (def != null ? def.roadWidth : 9.5f) * 0.5f + StageVerge;

        /// <summary>The barrier line for a given venue — what the audits must
        /// measure to. The circuits' 10 m constant is wrong for the stage,
        /// whose guard walls hug the shoulder.</summary>
        internal static float WallOffsetFor(TrackCatalog.TrackDef def) =>
            def != null && def.stage ? StageWallOffsetFor(def) : WallOffset;

        /// <summary>The barrier line for the stage being built right now.</summary>
        static float StageWallOffset => StageWallOffsetFor(track);
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

        /// <summary>Where THIS stage's bake and generated art live. Was a const
        /// pointing at the parkway until Bogue Banks arrived; a second region
        /// sharing the folder would have loaded the mountain's DEM and put a
        /// barrier island 1200 m up the Blue Ridge.</summary>
        static string StageArtDir => theme.stageDir;
        static string StageGenDir => theme.stageDir + "/Gen";
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

        /// <summary>Surface classes the bake writes beside the near DEM. The
        /// numbers are a file format — fetch_bogue.mjs writes them.</summary>
        enum Surf : byte { Land = 0, Sand = 1, Water = 2, Marsh = 3 }
        /// <summary>One byte per NEAR cell, or null on a stage with no mask —
        /// which is every inland bake, and is why every read of this is
        /// null-guarded rather than the mountain being given a beach.</summary>
        static byte[] surfNear;

        static void StageUnloadDem()
        {
            stageDemLoaded = false;
            demNear = demFar = null;
            surfNear = null;
            stageWp = null; stageHash = null;
        }

        /// <summary>Load the fetch script's bake and copy/generate the stage
        /// art. Called before ANY stage height is asked for.</summary>
        static void StageLoadDem()
        {
            string pre = StageArtDir + "/" + StagePrefix;
            string metaPath = ProjectRootPath(pre + "_dem_meta.json");
            if (!File.Exists(metaPath))
                throw new Exception("Stage DEM missing — run the region's fetch script first ("
                    + metaPath + ")");
            var meta = JsonUtility.FromJson<DemMeta>(File.ReadAllText(metaPath));
            demNearMeta = meta.near; demFarMeta = meta.far;
            demNear = ReadDemBytes(pre + "_dem_near.bytes", meta.near);
            demFar = ReadDemBytes(pre + "_dem_far.bytes", meta.far);

            // The surface mask is optional: a stage inland has nothing to
            // classify. Its absence is not an error, but a mask that does not
            // MATCH the near grid is — it would paint the beach in the wrong
            // place with no symptom an audit could catch.
            surfNear = null;
            string maskPath = ProjectRootPath(pre + "_mask_near.bytes");
            if (File.Exists(maskPath))
            {
                var bytes = File.ReadAllBytes(maskPath);
                if (bytes.Length != meta.near.cols * meta.near.rows)
                    throw new Exception($"{maskPath}: {bytes.Length} bytes, expected "
                        + (meta.near.cols * meta.near.rows));
                surfNear = bytes;
            }

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
                $"far {meta.far.cols}x{meta.far.rows} @ {meta.far.cell} m, base {meta.baseM} m ASL" +
                (surfNear != null ? ", surface mask present" : "") +
                (track.stageWaterY > 0f ? $", sea at y={track.stageWaterY:0.0}" : "") + ".");
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

        /// <summary>
        /// Surface class at a world point — NEAREST cell, not interpolated: the
        /// mask is categorical, and the average of "sand" and "sea" is not a
        /// surface. Land outside the near grid, and on any stage with no mask.
        /// </summary>
        static Surf StageSurfAt(float x, float z)
        {
            if (surfNear == null) return Surf.Land;
            var m = demNearMeta;
            int c = Mathf.RoundToInt((x - m.originX) / m.cell);
            int r = Mathf.RoundToInt((z - m.originZ) / m.cell);
            if (c < 0 || r < 0 || c >= m.cols || r >= m.rows) return Surf.Land;
            return (Surf)surfNear[r * m.cols + c];
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

            // The bench the corridor holds level under and beside the road:
            // the roadbed dig under the pavement, the shoulder shelf outside
            // it. Written out rather than folded into the Lerp below because
            // the verge fall has to be measured from it.
            float shelf = roadY - RoadbedSinkAt(d);

            // THE SHOULDER FALLS AWAY. THE MOUNTAIN DECIDES HOW FAR.
            //
            // The corridor holds a flat shelf out to CorridorR — sixteen
            // metres, on a road nine and a half wide — and it held it whatever
            // the mountain was doing, so the parkway ran down the middle of an
            // eleven-metre gravel apron on each side. "Most of the blue ridge
            // does not have big runoff areas to drive onto", and the reference
            // photograph is the Linn Cove Viaduct: two lanes, a stone wall, and
            // then nothing at all.
            //
            // Two lines, and the second is what makes it a road rather than a
            // trench. The fall is a 1-in-1.8 fill batter starting just behind
            // the guard wall; the clamp says it may never dig BELOW the real
            // mountainside, so:
            //
            //   * where the land drops away, the shoulder drops with it and
            //     meets the real slope within a few metres — no apron;
            //   * where the land is gentle, the shelf lands on the DEM almost
            //     at once and simply becomes the hillside;
            //   * where the land RISES — the cut side — Min picks the shelf and
            //     the fall cancels itself, because you do not dig a five-metre
            //     ditch beside a hill. That side is closed by the cut bank
            //     instead (BuildStageBanks).
            //
            // It can only ever LOWER the shelf, so every clearance the roadbed
            // had under the tarmac it still has. That is the whole reason it is
            // written as a subtraction with a floor rather than as a narrower
            // CorridorR: a narrower corridor would let the coarse ground grid
            // reach up through the road, and this cannot.
            float fall = StageVergeFallAt(d);
            if (fall > 0f) shelf = Mathf.Max(shelf - fall, Mathf.Min(dem, shelf));

            float pinned = Mathf.Lerp(shelf, dem, blend);

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
            // A SPAN ONLY EVER DIGS.
            //
            // Without this line the release is free to lift the ground as well
            // as drop it, and on the uphill side of a cut it does: the abutment
            // approaches sit in rock 5-7 m above the tarmac, and blending
            // toward that raises a hump of (delta - 1.6)^2 / (4 * delta) metres
            // over the road, peaking around f = 0.4 and therefore always at the
            // approach rather than out over the gorge. Measured: 1.0 m of
            // hillside standing at the edge of the tarmac at all eight parkway
            // spans, which is what "mountains clipping through that launch cars
            // into the air" was. The soffit cap above could not catch it — it
            // is itself faded by f, so at f = 0.4 it only takes back 40% of a
            // hump the same f put there.
            //
            // Clamping to the pin rather than to the road keeps the embankment
            // honest: `pinned` has already blended most of the way to the DEM
            // by the outer edge of the corridor, so this binds where the
            // corridor is actually holding the road up and nowhere else.
            return Mathf.Min(released, pinned);
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
            // Sand needs no such correction — it is already the colour it is.
            bool sandy = surfNear != null && !string.IsNullOrEmpty(theme.sand);
            var nearMat = MakeMat(MeshPrefix + "Ground", theme.ground, affine: 0f,
                                  tint: sandy ? (Color?)null : new Color(1.0f, 0.90f, 0.70f));
            var sandMat = sandy ? MakeMat(MeshPrefix + "Sand", theme.sand, affine: 0f) : null;
            var marshMat = sandy ? MakeMat(MeshPrefix + "Marsh",
                                           string.IsNullOrEmpty(theme.marsh) ? theme.ground : theme.marsh,
                                           affine: 0f) : null;
            var nearMats = sandy ? new[] { nearMat, sandMat, marshMat } : new[] { nearMat };
            // Far: the mountain paints its distance as autumn forest. An island
            // has no distance to paint — what is out there is water, and the
            // sea plane covers it — so the far ring reuses the near ground.
            //
            // SCRUB, not sand. Painting it sand was the obvious choice and it
            // was wrong: the overview came back with the whole mainland and
            // both shores rendered as one continuous beach, because a beach is
            // a narrow strip and everything BEHIND it is not. Distant land is
            // scrub; the sand is where the mask says it is, in the near band.
            var farMat = string.IsNullOrEmpty(theme.sand)
                ? MakeMat(MeshPrefix + "GroundFar", StageGenDir + "/FallMottle.png", affine: 0f)
                : MakeMat(MeshPrefix + "GroundFar", theme.ground, affine: 0f);

            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);

            int nearChunks = 0, farChunks = 0, nearVerts = 0, farVerts = 0, withColl = 0;

            // NEAR: 12 m cells in 240 m chunks over the corridor band.
            ForEachChunk(b, NearChunk, NearCoverage, (cx, cz, ox, oz) =>
            {
                float mid = RouteDistanceCoarse(ox + NearChunk * 0.5f, oz + NearChunk * 0.5f);
                if (mid > NearCoverage + NearChunk * 0.71f) return;
                var mesh = GridChunkMesh(ox, oz, NearChunk, NearCell, theme.groundTile, 0f,
                                         splitSand: sandy, sandTile: theme.sandTile,
                                         marshTile: theme.marshTile);
                if (mesh == null) return;
                nearVerts += mesh.vertexCount;
                var go = ChunkGO(root.transform, "GroundN_" + cx + "_" + cz, mesh,
                                 mesh.subMeshCount > 1 ? nearMats : new[] { nearMat },
                                 ox, oz, "StageGroundN_" + cx + "_" + cz);
                // On a mountain, 120 m of collider is generous — anything
                // further from the road is a slope you hit on the way there.
                // Over water it is not: the parapet of a bridge 20 m up is
                // easy to clear, there is nothing between it and the sound, and
                // a car that lands past the band falls through a seabed with no
                // collider and keeps going. Flat water chunks are cheap to
                // cook, so a stage with a sea collides its whole near band.
                float band = track.stageWaterY > 0f ? NearCoverage : ColliderBand;
                if (mid < band + NearChunk * 0.71f)
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
                // The old "fully under near" test compared the chunk CENTRE's
                // route distance against the near band, which for a 960 m
                // chunk could never be true — so every far chunk was built,
                // road corridor and all, and their 60 m cells draped the
                // parkway. The cutout is per-QUAD now (dropInside), which is
                // the only resolution at which the question makes sense.
                var mesh = GridChunkMesh(ox, oz, FarChunk, FarCell, 300f, -FarSink,
                                         NearCoverage - FarCell);
                if (mesh == null) return;
                farVerts += mesh.vertexCount;
                ChunkGO(root.transform, "GroundF_" + cx + "_" + cz, mesh,
                        new[] { farMat }, ox, oz, "StageGroundF_" + cx + "_" + cz);
                farChunks++;
            });

            Log($"Stage ground: {nearChunks} near chunks ({nearVerts} verts, {withColl} with colliders), " +
                $"{farChunks} far chunks ({farVerts} verts).");

            BuildStageSea(b, root.transform);
        }

        /// <summary>Cell size of the sea grid. The plane is dead flat, so this
        /// is not about shape — it is about VERTEX fog and affine mapping,
        /// which are per-vertex, and one horizon-sized quad would get one fog
        /// value for the whole ocean.</summary>
        const float SeaCell = 90f;

        /// <summary>
        /// The sea, as a single flat plane at the bake's water height.
        ///
        /// The tempting design is to classify each terrain cell and build water
        /// geometry only where the mask says water — and it is wrong, because
        /// then the SHORELINE is a polygon boundary you have to keep aligned
        /// with the terrain, and every disagreement is a crack you can see the
        /// sky through. A flat plane at a known height has no shoreline at all:
        /// the coast is wherever the ground rises through it, which is exact by
        /// construction and free. The bake guarantees the clearance — land is
        /// held 0.4 m above the plane and the seabed 4 m below it — so there is
        /// nothing to z-fight either.
        /// </summary>
        static void BuildStageSea(Bounds routeBounds, Transform parent)
        {
            float y = track != null ? track.stageWaterY : 0f;
            if (y <= 0f || string.IsNullOrEmpty(theme.water)) return;

            var mat = MakeMat(MeshPrefix + "Sea", theme.water, affine: 0f,
                              tint: new Color(0.86f, 0.94f, 1f));
            // Out to the far ring, so the water reaches the fog wall on every
            // heading rather than ending in a visible edge over the shoulder.
            float reach = FarCoverage + FarChunk;
            float minX = routeBounds.min.x - reach, maxX = routeBounds.max.x + reach;
            float minZ = routeBounds.min.z - reach, maxZ = routeBounds.max.z + reach;
            int cols = Mathf.CeilToInt((maxX - minX) / SeaCell);
            int rows = Mathf.CeilToInt((maxZ - minZ) / SeaCell);

            var verts = new Vector3[(cols + 1) * (rows + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var tris = new int[cols * rows * 6];
            for (int r = 0, v = 0; r <= rows; r++)
                for (int c = 0; c <= cols; c++, v++)
                {
                    float wx = minX + c * SeaCell, wz = minZ + r * SeaCell;
                    verts[v] = new Vector3(wx, y, wz);
                    uvs[v] = new Vector2(wx / theme.waterTile, wz / theme.waterTile);
                    norms[v] = Vector3.up;
                }
            for (int r = 0, t = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int v = r * (cols + 1) + c;
                    tris[t++] = v; tris[t++] = v + cols + 1; tris[t++] = v + cols + 2;
                    tris[t++] = v; tris[t++] = v + cols + 2; tris[t++] = v + 1;
                }
            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts, normals = norms, uv = uvs, triangles = tris,
            };
            SaveMesh(mesh, "StageSea");
            var go = new GameObject("Sea");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            // NO COLLIDER, deliberately. A car that goes over the parapet
            // should end up in the sound, and StuckRecovery is what brings it
            // back — a collider here would let it drive on the water instead.
            Log($"Stage sea: {cols}x{rows} @ {SeaCell} m at y={y:0.0} ({verts.Length} verts).");
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
        /// <param name="dropInside">Omit quads whose four corners are ALL
        /// closer to the route than this. The far grid's 60 m cells cannot
        /// resolve a road corridor: one corner lands on the pinned shelf and
        /// the next is 60 m up the mountainside, and the triangle between them
        /// runs straight through the tarmac. It did — six metres above the
        /// road, over a third of the parkway, invisible to an audit that rayed
        /// colliders because the far chunks have none. The near grid already
        /// covers everything inside NearCoverage, so the fix is for the far
        /// grid to stop pretending it can. 0 keeps every quad.</param>
        /// <returns>Null when nothing survived the cutout — a chunk entirely
        /// under the near band has no geometry left to build.</returns>
        /// <param name="splitSand">Emit a SECOND submesh for quads the surface
        /// mask calls beach, so one chunk can be scrub inland and sand at the
        /// waterline. Ignored where there is no mask.</param>
        static Mesh GridChunkMesh(float ox, float oz, float size, float cell,
                                  float tile, float yOffset, float dropInside = 0f,
                                  bool splitSand = false, float sandTile = 8f,
                                  float marshTile = 6f)
        {
            bool sandy = splitSand && surfNear != null;
            int cells = Mathf.RoundToInt(size / cell);
            var verts = new Vector3[(cells + 1) * (cells + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var surf = sandy ? new Surf[verts.Length] : null;
            var routeD = dropInside > 0f ? new float[verts.Length] : null;
            var tris = new List<int>(cells * cells * 6);
            var sandTris = sandy ? new List<int>(cells * cells * 2) : null;
            var marshTris = sandy ? new List<int>(cells * cells * 2) : null;
            for (int gz = 0, v = 0; gz <= cells; gz++)
                for (int gx = 0; gx <= cells; gx++, v++)
                {
                    float wx = ox + gx * cell, wz = oz + gz * cell;
                    verts[v] = new Vector3(gx * cell, StageGroundHeightAt(wx, wz) + yOffset, gz * cell);
                    uvs[v] = new Vector2(wx / tile, wz / tile);
                    if (surf != null) surf[v] = StageSurfAt(wx, wz);
                    if (routeD != null) routeD[v] = RouteDistanceCoarse(wx, wz);
                    // Central differences at half a cell: a function of world
                    // position alone, so both sides of a chunk border compute
                    // the identical normal.
                    float e = cell * 0.5f;
                    float dhdx = (StageGroundHeightAt(wx + e, wz) - StageGroundHeightAt(wx - e, wz)) / (2f * e);
                    float dhdz = (StageGroundHeightAt(wx, wz + e) - StageGroundHeightAt(wx, wz - e)) / (2f * e);
                    norms[v] = new Vector3(-dhdx, 1f, -dhdz).normalized;
                }
            for (int gz = 0; gz < cells; gz++)
                for (int gx = 0; gx < cells; gx++)
                {
                    int v = gz * (cells + 1) + gx;
                    int a = v, b = v + cells + 1, c = v + cells + 2, e2 = v + 1;
                    // All four corners inside the near band: the near mesh owns
                    // this quad. A quad that STRADDLES the line is kept, so the
                    // two grids overlap by a cell and the seam stays sealed.
                    if (routeD != null && routeD[a] < dropInside && routeD[b] < dropInside &&
                        routeD[c] < dropInside && routeD[e2] < dropInside) continue;
                    var into = tris;
                    if (sandy)
                    {
                        // Marsh wins over everything. It is the surface that
                        // borders open water here, so any "half the corners are
                        // wet" rule would eat it — and it is the single most
                        // visible thing about this coast from the air.
                        int nM = 0, nS = 0;
                        foreach (int k in new[] { a, b, c, e2 })
                        {
                            if (surf[k] == Surf.Marsh) nM++;
                            // Counting WATER as sand matters: the cells seaward
                            // of the waterline are submerged sand, and they are
                            // what shows through the sea plane in the shallows
                            // — leave them scrub and the shore reads as a lawn
                            // running into the surf.
                            else if (surf[k] == Surf.Sand || surf[k] == Surf.Water) nS++;
                        }
                        if (nM >= 2) into = marshTris;
                        else if (nS >= 2) into = sandTris;
                    }
                    into.Add(a); into.Add(b); into.Add(c);
                    into.Add(a); into.Add(c); into.Add(e2);
                }
            bool split = sandy && (sandTris.Count > 0 || marshTris.Count > 0);
            if (tris.Count == 0 && !split) return null;

            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts, normals = norms, uv = uvs,
            };
            if (!split) { mesh.triangles = tris.ToArray(); return mesh; }

            // Each surface gets its OWN uv scale: a beach at the same 11 m
            // repeat as the scrub behind it reads as one surface in two
            // colours, and marsh grass wants a tighter repeat than either.
            // uv2 is not an option — the PSX shader samples uv only — so each
            // submesh gets its own copy of the vertex block, re-UV'd. 169 verts
            // a chunk, so three copies is still nothing.
            //
            // ALWAYS three blocks, even when a chunk has no marsh in it: the
            // renderer's material array is indexed by submesh, so a chunk that
            // sometimes has two and sometimes three would need the material
            // list rebuilt per chunk to match. Empty submeshes cost no
            // triangles and keep slot N meaning the same thing everywhere.
            int vn = verts.Length;
            var vAll = new Vector3[vn * 3];
            var nAll = new Vector3[vn * 3];
            var uAll = new Vector2[vn * 3];
            for (int b = 0; b < 3; b++)
            {
                verts.CopyTo(vAll, vn * b);
                norms.CopyTo(nAll, vn * b);
            }
            uvs.CopyTo(uAll, 0);
            for (int i = 0; i < vn; i++)
            {
                uAll[vn + i] = new Vector2((ox + verts[i].x) / sandTile,
                                           (oz + verts[i].z) / sandTile);
                uAll[vn * 2 + i] = new Vector2((ox + verts[i].x) / marshTile,
                                                (oz + verts[i].z) / marshTile);
            }
            for (int i = 0; i < sandTris.Count; i++) sandTris[i] += vn;
            for (int i = 0; i < marshTris.Count; i++) marshTris[i] += vn * 2;

            mesh.vertices = vAll; mesh.normals = nAll; mesh.uv = uAll;
            mesh.subMeshCount = 3;
            mesh.SetTriangles(tris, 0);
            mesh.SetTriangles(sandTris, 1);
            mesh.SetTriangles(marshTris, 2);
            return mesh;
        }

        static GameObject ChunkGO(Transform parent, string name, Mesh mesh,
                                  Material[] mats, float ox, float oz, string meshName)
        {
            SaveMesh(mesh, meshName);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(ox, 0f, oz);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            // sharedMaterialS: a two-submesh chunk with one material draws the
            // sand submesh in the scrub material and nothing looks wrong until
            // you notice the beach is green.
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
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
                for (int i = 0; i < n; i++) want[i] = StageWallWanted(pts, i, side);
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

        /// <summary>Does the parkway have a guard wall on this shoulder? Both
        /// sides of every span, and anywhere the mountain falls
        /// <see cref="WallDropM"/> within thirty metres. Shared with the cut
        /// bank pass so the two can never claim the same station.</summary>
        static bool StageWallWanted(List<Vector3> pts, int i, float side)
        {
            if (bridgeBlend != null && bridgeBlend[i] > 0.35f) return true;
            Vector3 right = RightAt(pts, i);
            float px = pts[i].x + right.x * side * 30f;
            float pz = pts[i].z + right.z * side * 30f;
            return pts[i].y - StageDemY(px, pz) > WallDropM;
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
        //  Cut banks
        // ------------------------------------------------------------------
        // THE OTHER SIDE OF THE ROAD.
        //
        // A mountain road has a valley on one side and a hillside on the other,
        // and until this pass only the valley was closed: the guard wall went
        // up where the land fell away, and where the land ROSE the corridor
        // held eleven metres of dead-level bench either side of the tarmac that
        // you could drive onto, through the trees, and sit on. "Most of the
        // blue ridge does not have big runoff areas to drive onto" — that bench
        // was most of them.
        //
        // It cannot be fixed in the height field. The corridor shelf is what
        // stops the coarse ground grid (12 m cells on a 9.5 m road) pushing a
        // facet up through the tarmac between its own vertices, so nothing
        // inside CorridorR is allowed to rise. A cut bank is ADDED geometry
        // standing on the shelf, exactly like the guard wall, so it closes the
        // shoulder without touching that guarantee at all.
        //
        // Where the wall and the bank could both claim a shoulder the wall wins
        // — StageBankHeight asks the wall's own question first — so no station is
        // ever built twice.

        /// <summary>Shortest face worth building. Under a metre it is a kerb
        /// rather than a cut — you would drive up it — and a hillside that
        /// gentle is a shoulder you can see over, which is a lot of the
        /// parkway and should stay that way.</summary>
        const float BankRiseM = 0.9f;
        /// <summary>Tallest face built. Past this you are looking at a rock
        /// wall the fog will hide the top of anyway, and the collider becomes
        /// more expensive than the mountain behind it.</summary>
        const float BankMaxH = 5.5f;
        /// <summary>Metres the face leans back per metre of height. 0.5 is a
        /// 2:1 cut — steeper than earth stands but exactly what a blasted
        /// Appalachian road cut looks like.</summary>
        const float BankBatter = 0.5f;
        /// <summary>Where the toe of the bank sits, past the barrier line. Far
        /// enough out that its collider face never reaches the audit's reach
        /// band (barrier line minus 0.4), close enough that there is nothing to
        /// park on in front of it.</summary>
        static float BankToe => StageWallOffset + 0.45f;

        static void BuildStageBanks(List<Vector3> pts, Transform parent)
        {
            int n = pts.Count;
            string tex = System.IO.File.Exists(StageGenDir + "/CutBank.png")
                       ? StageGenDir + "/CutBank.png" : theme.wall;
            var mat = MakeMat(MeshPrefix + "Bank", tex, affine: 0f);
            var phys = GetOrCreatePhysMat("WallPhys", 0.05f, 0.05f);
            var root = new GameObject("Banks");
            root.transform.SetParent(parent, false);

            int runs = 0, banked = 0;
            foreach (float side in new[] { -1f, 1f })
            {
                var h = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float face = StageBankHeight(pts, i, side);
                    h[i] = face >= BankRiseM ? face : 0f;
                }
                // Smooth the top line. The DEM is 30 m posts read through a
                // bilinear filter, so raw station-to-station rise is noisy at
                // exactly the scale a 4 m ring samples it — an unsmoothed top
                // edge saws visibly along a cut that is, on the ground, one
                // continuous face.
                var sm = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f; int cnt = 0;
                    for (int o = -3; o <= 3; o++)
                    {
                        int j = Mathf.Clamp(i + o, 0, n - 1);
                        sum += h[j]; cnt++;
                    }
                    // ...and then put a wobble BACK, because a smoothed top
                    // edge is a milled one. Two incommensurate periods, 80 m
                    // and 33 m, which are the scales a rock face actually
                    // varies at — and deterministic in the station index, so
                    // the same bake gives the same mountain every time.
                    sm[i] = sum / cnt * (1f + 0.11f * Mathf.Sin(i * 0.37f + 1.1f)
                                            + 0.06f * Mathf.Sin(i * 0.91f));
                }

                var want = new bool[n];
                for (int i = 0; i < n; i++) want[i] = h[i] > 0f;

                for (int i = 0; i < n; )
                {
                    if (!want[i]) { i++; continue; }
                    int len = 1, clear = 0;
                    while (i + len < n && clear < 3)
                    {
                        if (want[i + len]) clear = 0; else clear++;
                        len++;
                    }
                    len -= clear;
                    // Same four-station floor the walls use: a cut that lasts
                    // twelve metres is a bump in the DEM, not a road cut, and
                    // building it gives the shoulder a picket line of stubs.
                    if (len >= 4)
                    {
                        BuildOneStageBank(pts, i, len, side, sm, root.transform, mat, phys, runs++);
                        banked += len;
                    }
                    i += len + clear;
                }
            }
            Log($"Stage cut banks: {runs} runs closing {banked * Spacing:0} m of uphill shoulder.");
        }

        /// <summary>
        /// How tall the cut face is here, or 0 where a bank would be wrong.
        ///
        /// THE TOP OF A CUT LANDS ON THE HILLSIDE. That is the whole shape of
        /// the thing: you take a bite out of a slope, and the face is exactly
        /// as tall as the slope is where the face stops. Sizing it off a rise
        /// measured at some fixed distance instead gives a wall standing a
        /// metre proud of a hill that is not there yet — a lip along the top,
        /// visible from the road, on every gentle gradient.
        ///
        /// So it is solved rather than sampled: h is the height at which the
        /// top of a battered face of height h meets the real DEM. The
        /// iteration converges from below in three or four passes because the
        /// DEM rises with distance and the batter is shallower than 1:1; six
        /// is free and covers the odd bench.
        ///
        /// Zero over a span and its approaches (a deck has a parapet, and the
        /// abutment cuts sit five metres above the tarmac — a bank there would
        /// be a wall across the bridge mouth), and zero wherever the guard wall
        /// already wants this shoulder.
        /// </summary>
        static float StageBankHeight(List<Vector3> pts, int i, float side)
        {
            if (bridgeBlend != null && bridgeBlend[i] > 0.05f) return 0f;
            if (StageWallWanted(pts, i, side)) return 0f;
            Vector3 right = RightAt(pts, i);
            float h = 0f;
            for (int it = 0; it < 6; it++)
            {
                // A metre PAST the top of the face, so the cut bites into the
                // hill rather than balancing on the surface of it.
                float probe = BankToe + h * BankBatter + 1f;
                float px = pts[i].x + right.x * side * probe;
                float pz = pts[i].z + right.z * side * probe;
                h = Mathf.Clamp(StageDemY(px, pz) - pts[i].y, 0f, BankMaxH);
                if (h <= 0f) return 0f;
            }
            return h;
        }

        static void BuildOneStageBank(List<Vector3> pts, int from, int stations, float side,
                                      float[] h, Transform parent, Material mat,
                                      PhysicsMaterial phys, int no)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float dist = 0f;
            for (int k = 0; k < stations; k++)
            {
                int i = Mathf.Min(from + k, pts.Count - 1);
                // Taper both ends into the ground over four stations. A cut
                // face that simply stops leaves a vertical edge standing in
                // open hillside, which reads as a missing chunk of world.
                float taper = Mathf.Min(Mathf.InverseLerp(-0.5f, 3.5f, k),
                                        Mathf.InverseLerp(-0.5f, 3.5f, stations - 1 - k));
                float hh = Mathf.Max(0.15f, h[i] * Mathf.SmoothStep(0f, 1f, taper));

                Vector3 right = RightAt(pts, i);
                Vector3 toe = pts[i] + right * side * BankToe;
                // Half a metre of skirt below the shelf, for the same reason
                // the guard wall has one: a coarse ground facet must never be
                // able to show daylight under the bottom of it.
                toe.y = pts[i].y - 0.5f;
                Vector3 top = pts[i] + right * side * (BankToe + hh * BankBatter);
                top.y = pts[i].y + hh;

                int v = verts.Count;
                verts.Add(toe); verts.Add(top);
                uvs.Add(new Vector2(dist / 4.5f, 0f));
                uvs.Add(new Vector2(dist / 4.5f, (hh + 0.5f) / 4.5f));
                if (k > 0)
                {
                    // Facing the road, one winding, like the wall — the back of
                    // a cut face is inside the mountain.
                    if (side < 0f) tris.AddRange(new[] { v - 2, v - 1, v, v - 1, v + 1, v });
                    else tris.AddRange(new[] { v - 2, v, v - 1, v - 1, v, v + 1 });
                }
                dist += Spacing;

                // One box per station chord, for the reason the wall's comment
                // gives: a box spanning four stations cuts the corner and ends
                // up inside the kerb band on the stage's tightest radius.
                if (k + 1 < stations)
                {
                    int j = Mathf.Min(from + k + 1, pts.Count - 1);
                    Vector3 a = pts[i] + RightAt(pts, i) * side * (BankToe + 0.3f);
                    Vector3 bPos = pts[j] + RightAt(pts, j) * side * (BankToe + 0.3f);
                    float ch = Mathf.Max(hh, StageWallCollH);
                    var seg = new GameObject("BankColl");
                    seg.transform.SetParent(parent, false);
                    seg.transform.position = (a + bPos) * 0.5f + Vector3.up * (ch * 0.5f - 0.3f);
                    Vector3 dir = bPos - a; dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
                    seg.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    var box = seg.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.6f, ch + 0.6f, dir.magnitude + 0.5f);
                    box.sharedMaterial = phys;
                    seg.layer = SolidLayer;
                    seg.isStatic = true;
                }
            }
            var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
            SaveMesh(mesh, "StageBank" + no);
            var go = new GameObject("Bank" + no);
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
            if (!AssetDatabase.IsValidFolder(StageArtDir))
                AssetDatabase.CreateFolder(
                    Path.GetDirectoryName(StageArtDir).Replace('\\', '/'),
                    Path.GetFileName(StageArtDir));
            if (!AssetDatabase.IsValidFolder(StageGenDir))
                AssetDatabase.CreateFolder(StageArtDir, "Gen");
            // The CC0 tree pack is 16 PNGs copied out of a folder on this
            // machine. A stage with no forest must not need it to exist — the
            // island builds on a checkout that has never seen the pack.
            if (!theme.stageForest) return;
            if (!AssetDatabase.IsValidFolder(StageArtDir + "/Trees"))
                AssetDatabase.CreateFolder(StageArtDir, "Trees");
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
            if (!theme.stageForest) { GenerateCoastTextures(); return; }

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

            // The cut bank: blasted Blue Ridge gneiss.
            //
            // Vertical, because both the foliation and the drill lines run up
            // the face and it is the verticality that makes a cut read as cut.
            //
            // TWO THINGS IT MUST NOT HAVE, both learned by photographing them:
            // even spacing (clean sines at one frequency are FLUTING, and 5 km
            // of that is a precast retaining wall), and any strong HORIZONTAL
            // feature. Dark bedding joints across the face were the second
            // attempt and they came back as swags of bunting draped along the
            // parkway — a curve that crosses the direction of travel reads as
            // decoration however geological the intent. So: four incommensurate
            // vertical frequencies leaning very slightly, and everything else
            // is COLOUR blotching rather than lines.
            WriteTexture(StageGenDir + "/CutBank.png", 64, 64, (x, y) =>
            {
                float u = x / 64f, v = y / 64f;
                float band = Mathf.Sin(u * 31f + v * 2.2f)
                           + 0.9f * Mathf.Sin(u * 19f - v * 1.4f + 2.1f)
                           + 0.6f * Mathf.Sin(u * 53f + 0.8f)
                           + 0.4f * Mathf.Sin(u * 7f + v * 1.1f + 4.3f);
                // Weathering and lichen, coarse and soft, over the top of it.
                float stain = Mathf.Sin(u * 9f + 1.4f) * Mathf.Cos(v * 4.5f - 0.6f)
                            + 0.7f * Mathf.Sin(u * 3f - v * 5f + 2.6f);
                float grit = ((x * 29 + y * 71) % 23) / 23f;
                float shade = 0.52f + band * 0.075f + grit * 0.15f;
                byte r = (byte)Mathf.Clamp(116 * shade + 44 + stain * 10f, 0, 255);
                byte g = (byte)Mathf.Clamp(108 * shade + 40 + stain * 5f, 0, 255);
                byte b = (byte)Mathf.Clamp(94 * shade + 36 - stain * 4f, 0, 255);
                // Soil and scrub at the toe, over the bottom fifth.
                float soil = Mathf.Clamp01((0.2f - v) * 5f);
                r = (byte)Mathf.Lerp(r, 74 + grit * 22f, soil);
                g = (byte)Mathf.Lerp(g, 72 + grit * 26f, soil);
                b = (byte)Mathf.Lerp(b, 46 + grit * 16f, soil);
                return new Color32(r, g, b, 255);
            });
        }

        /// <summary>
        /// The Crystal Coast ground set: sand, dune scrub, sea, and a shell-
        /// gravel shoulder. Generated rather than sourced, like every other
        /// ground texture in the game — a 64 px tile of beach is a noise
        /// function, and hand-painting one would be four things to redraw the
        /// moment the tile scale changes.
        ///
        /// All three tile at different world scales (see Theme.sandTile /
        /// groundTile / waterTile) because they are seen at completely
        /// different distances: you drive ON the sand, past the scrub, and look
        /// across two kilometres of water.
        /// </summary>
        static void GenerateCoastTextures()
        {
            // Beach sand. Warm, pale, and very low contrast — the grain is
            // there to stop 24-bit banding across a flat surface, not to be
            // seen as texture. A couple of darker grains per tile read as shell
            // fragments at the scale a wheel passes over them.
            WriteTexture(StageGenDir + "/Sand.png", 64, 64, (x, y) =>
            {
                float n = Noise(x, y);
                float m = Noise(x >> 2, y >> 2);          // coarse tonal drift
                byte r = (byte)(206 + n * 16 + m * 12);
                byte g = (byte)(191 + n * 16 + m * 12);
                byte b = (byte)(163 + n * 18 + m * 10);
                if (Noise(x + 91, y + 17) > 0.965f) { r -= 34; g -= 30; b -= 24; }
                return new Color32(r, g, b, 255);
            });

            // Behind the dune line: sea oats and wax myrtle over sand, so the
            // green is thin and the sand shows through it. Blending TOWARD the
            // sand colour rather than using a green of its own is what keeps
            // the scrub/sand boundary from reading as a painted edge.
            WriteTexture(StageGenDir + "/Scrub.png", 64, 64, (x, y) =>
            {
                float n = Noise(x, y);
                float clump = Noise(x >> 3, y >> 3);      // patchy, not uniform
                // 0.18..0.68 rather than 0.35..0.90: the first pass came back
                // reading as mown lawn either side of the road. Dune scrub is
                // mostly the sand it is growing out of.
                float green = Mathf.Clamp01(0.18f + clump * 0.5f);
                byte r = (byte)Mathf.Lerp(200 + n * 14, 108 + n * 26, green);
                byte g = (byte)Mathf.Lerp(186 + n * 14, 126 + n * 28, green);
                byte b = (byte)Mathf.Lerp(158 + n * 14, 74 + n * 20, green);
                return new Color32(r, g, b, 255);
            });

            // The sea. Anisotropic on purpose: the swell runs in lines, so the
            // noise is stretched along x and the wave terms are sines of y
            // alone. Wrap-friendly (all terms are k*2pi*n/64) or the tile seam
            // draws a straight line across the sound every 24 m.
            WriteTexture(StageGenDir + "/Sea.png", 64, 64, (x, y) =>
            {
                float u = x * (Mathf.PI * 2f / 64f), v = y * (Mathf.PI * 2f / 64f);
                float swell = Mathf.Sin(v * 3f + Mathf.Sin(u * 2f) * 0.6f)
                            + 0.5f * Mathf.Sin(v * 7f - u * 1f + 1.1f)
                            + 0.3f * Mathf.Sin(v * 11f + u * 3f + 2.2f);
                float t = Mathf.InverseLerp(-1.8f, 1.8f, swell);
                // Green-grey inshore water, not tropical blue: this is the
                // Atlantic off North Carolina in the same frame as the sound.
                byte r = (byte)Mathf.Lerp(28, 74, t);
                byte g = (byte)Mathf.Lerp(66, 116, t);
                byte b = (byte)Mathf.Lerp(78, 122, t);
                // Sparse glint on the crests. Rare enough to read as sun on
                // water rather than as noise.
                if (t > 0.86f && Noise(x + 7, y + 53) > 0.90f) { r += 46; g += 44; b += 38; }
                return new Color32(r, g, b, 255);
            });

            // Salt marsh: smooth cordgrass over dark tidal mud, cut through by
            // creeks. The photographs of the Langston crossing are more than
            // half this, and it was rendering as open sound.
            //
            // The creeks are the point. A flat olive field reads as a lawn from
            // 20 m up; what makes marsh look like marsh from a bridge is the
            // braided drainage running through it, so a couple of wrapping sine
            // terms carve dark channels and the grass sits between them.
            WriteTexture(StageGenDir + "/Marsh.png", 64, 64, (x, y) =>
            {
                float u = x * (Mathf.PI * 2f / 64f), v = y * (Mathf.PI * 2f / 64f);
                float creek = Mathf.Sin(u * 2f + Mathf.Sin(v * 3f) * 1.1f)
                            + 0.7f * Mathf.Sin(v * 3f - u * 1f + 2.0f);
                float n = Noise(x, y);
                if (Mathf.Abs(creek) < 0.16f)
                {
                    // Tidal channel: dark water over mud.
                    byte b = (byte)(52 + n * 16);
                    return new Color32((byte)(b - 8), b, (byte)(b + 10), 255);
                }
                // Cordgrass. Olive-brown and desaturated — Spartina is not a
                // lawn green, and against the sea it must not read as one.
                float clump = Noise(x >> 2, y >> 2);
                byte r = (byte)(104 + clump * 34 + n * 12);
                byte g = (byte)(112 + clump * 30 + n * 12);
                byte bl = (byte)(62 + clump * 22 + n * 10);
                return new Color32(r, g, bl, 255);
            });

            // The verge: crushed shell and sand, which is what a shoulder on
            // this island actually is.
            WriteTexture(StageGenDir + "/Shoulder.png", 32, 16, (x, y) =>
            {
                int h = (x * 7 + y * 13) % 19;
                byte v = (byte)(172 + (h * 4) % 34);
                return new Color32(v, (byte)(v - 6), (byte)(v - 20), 255);
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
                var go = ChunkGO(root.transform, "Forest_" + cx + "_" + cz, mesh, new[] { mat },
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
