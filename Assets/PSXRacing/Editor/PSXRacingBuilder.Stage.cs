using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;   // CityMeshes.Surface: the urban shoulder is painted in the city's own concrete

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
    ///   - THE ROADSIDE is graded the way a DOT section is (RoadsideRules):
    ///     a flush shoulder, a recoverable foreslope that meets the real land,
    ///     a ditch and a rock face where the road is cut into the hill. WALLS
    ///     are the parkway's own low stone guard walls, and only where one is
    ///     WARRANTED — every bridge deck and its approaches, a fall grading
    ///     cannot make recoverable, water, a tunnel mouth. PlanStageRoadside
    ///     decides all of it once, and every pass below reads that plan.
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
        /// <summary>Metres between the tarmac edge and a warranted guard wall's
        /// drawn stone: the 0.9 m verge strip, a 0.2 m paved shoulder, and the
        /// 5 cm the stone stands behind its own collider face. Where a wall is
        /// warranted it still hugs the shoulder, which is also why the decks
        /// (DeckHalfWidth derives from this) have not moved.</summary>
        internal const float StageVerge = 1.15f;

        // ------------------------------------------------------------------
        //  The graded roadside — numbers local to the stage section. The
        //  rules themselves (slopes, clear zone, hide margin, warrant) are
        //  RoadsideRules'; these are the dimensions this section builds them
        //  to.
        // ------------------------------------------------------------------
        /// <summary>Shoulder past the verge strip: the strip's top (the
        /// shoulder emitter's KerbStripLift, flush with the tarmac), the
        /// Safety Edge bevel down RoadsideRules.EdgeDropM, then
        /// RoadsideRules.ShoulderCrossFall. Ends exactly on the wall face
        /// (StageVerge - StageWallFaceIn), so a walled and an open side share
        /// their first 1.1 m.</summary>
        const float StageShoulderM = 0.2f;
        /// <summary>How far in front of the drawn stone a wall's collider face
        /// stands (the obstacle audit's GhostBack reads this gap).</summary>
        const float StageWallFaceIn = 0.05f;
        /// <summary>Drawn thickness of a guard wall: the stone has a top and a
        /// back now, so a wall seen from the valley, or from the end of a run,
        /// is masonry rather than a sheet with nothing behind it. Half a metre
        /// so the shoulder ribbon's toe tuck and skirt, which the emitter lays
        /// 0.5 m past the profile's last point (the collider face), end inside
        /// the stone rather than hanging out of its back.</summary>
        const float StageWallDrawThick = 0.5f;
        /// <summary>
        /// The highest the ground beside a graded section may stand, tarmac-
        /// relative: where the land RISES the section is graded down to this
        /// and no further, and the corridor carries it back up to the hill
        /// past CorridorR as it always has.
        ///
        /// Why a floor at all: the ground under the section is a 12 m lattice,
        /// and its facets run straight from a vertex under the tarmac (the
        /// roadbed dig, -0.45) to one beside the road. A beside-the-road vertex
        /// at road level would put a chord 5 cm under a shoulder in a sag; at
        /// -0.30 the chord keeps the hide margin it is meant to have (python
        /// replica of the lattice over all eight stages: under the first 1.1 m
        /// of every open section the lattice sits at least 0.22 m below the
        /// ribbon, p50 0.39-0.41 m).
        /// </summary>
        const float StageBenchDy = -0.30f;
        /// <summary>RoadsideRules.TraversableSlope run past WarrantReachM: a
        /// fill the 1V:6H/1V:4H section cannot catch within the reach may
        /// still be graded at 1V:3H if the land flattens within this — the
        /// "traversable only with a runout" clause. Past it the side is
        /// critical.</summary>
        const float StageTraversableRunM = 3f;
        /// <summary>Metres inside an open section's catch over which the hide
        /// margin fades to nothing, so the lattice meets the ribbon's toe at
        /// its own height and the two CROSS there (RoadsideRules.ToeTuckM)
        /// instead of the toe standing a hide margin proud of the land.</summary>
        const float StageHideFadeM = 1.5f;
        /// <summary>Extra hide (over RoadsideRules.HideMarginM) under an open
        /// section's outer foreslope. Measured with a python replica of the
        /// lattice and of the terrain audit's shoulder probe: lattice within
        /// 3 cm of the ribbon outside the designed crossing fell from 34 to 19
        /// probes on Blowing Rock (of ~27,000) and grass through it from 14
        /// to 6; the remainder is DEM dips narrower than a cell, which no hide
        /// in the FIELD reaches — the field is only sampled at the vertices.
        /// PrepareStageLattice takes that remainder, on the triangles
        /// themselves.</summary>
        const float StageOuterHideM = 0.2f;
        /// <summary>Behind a wall the ground holds the shoulder's height for
        /// this far past the collider face (under the stone and its footing),
        /// then falls at <see cref="StageFillBatter"/> until it meets the real
        /// land. The same shelf-and-batter the old verge had, now only where a
        /// wall stands in front of it.</summary>
        const float StageWallBackFlatM = 0.75f;
        /// <summary>1 in 1.8 — the batter a highway fill is built to. Only
        /// ever behind a wall now: an open side is graded at the RoadsideRules
        /// slopes instead.</summary>
        const float StageFillBatter = 0.55f;
        /// <summary>The COMPACT cut (synthesis 7.1): 0.8 m of foreslope at
        /// RoadsideRules.SteepestRecoverableSlope into a ditch 0.4 m wide, then
        /// RoadsideRules.BackSlope up to the rock face's toe at road level —
        /// which lands the toe about 3 m past the tarmac edge, so the face
        /// stays close enough to the road to read as a parkway cut.</summary>
        const float CutForeslopeRunM = 0.8f, CutDitchFloorM = 0.4f;
        /// <summary>Water the coast walls a side for: deeper than this, within
        /// this far of the tarmac edge (the RDG's 0.6 m / 6 m).</summary>
        const float StageWaterReachM = 6f, StageWaterDepthM = 0.6f;
        /// <summary>Stations a tunnel mouth is guarded for on each side that
        /// has no rock face running into it: the portal face stops a car, but
        /// the ground beside the approach is holed for a lattice cell in front
        /// of it (see GridChunkMesh) and nothing may be able to reach that.
        /// Four stations is the measured hole (LittleSwitzerland 995-997 and
        /// 1046-1049).</summary>
        const int PortalGuardStations = 4;
        /// <summary>Shortest warranted wall worth building.</summary>
        const int MinWallRunStations = 4;
        /// <summary>Where the hill behind a cut face is released to the real
        /// land: held down for BankPinM past the toe, blended up over
        /// BankReleaseM. Measured, not chosen: a python replica of the 12 m
        /// lattice put facets through the ditch (up to +0.62 m) at 8/8, while
        /// at 12/12 the lattice under the cut section stays below the ribbon
        /// on all but 0.1% of samples across the five mountains (worst +0.03 m;
        /// the exception is one Mount Mitchell switchback, wp 1494, where the
        /// upper leg's fill stands 7 m over the ditch). A lattice vertex weighs
        /// on points up to one cell diagonal (17 m) away.</summary>
        const float BankPinM = 12f, BankReleaseM = 12f;
        /// <summary>The rock top's last metres, over which it comes down onto
        /// the released lattice and tucks under it. With pin and release at
        /// 12/12 the lattice is within about 0.3 m of the DEM (p50) by the
        /// tail's end, 30 m past the toe, so the tail is a gentle slope onto
        /// it rather than a step.</summary>
        const float BankTopTailM = 6f;
        /// <summary>Stations over which the release (and the rock top that
        /// covers it) fades in from each end of a bank run: the neighbouring
        /// station's section is held down, and its lattice facets reach a
        /// cell along the road as well as across it.</summary>
        const int BankReleaseFadeStations = 5;

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
        /// <summary>
        /// Fog band multiplier over the hour presets, for a venue that can see
        /// a valley. 4.0 puts noon's 355 m fogFar at 1,420 m — the far wall,
        /// hazy, which is what the Blue Ridge is named for.
        ///
        /// It was 3.2, and that closed the fog at 1,136 m in front of a far
        /// plane at 1,500: the last 364 m of everything the stage drew was
        /// rasterised, shaded, and then painted over in flat fog colour. So
        /// this is the rare change that is free — no extra geometry, no extra
        /// draw distance, just the terrain that was already being drawn
        /// allowed to be itself. Landing fogFar INSIDE the far plane is the
        /// constraint: fog has to reach full strength before the clip, or the
        /// edge of the world appears through it.
        /// </summary>
        const float StageFogScale = 4.0f;
        const float StageFarClip = 1500f;

        /// <summary>
        /// The same two numbers for everything that is not a stage — the
        /// generated circuits, the strips, and the streamed city.
        ///
        /// They were 1.0 and 360 m, which is where "objects in the distance
        /// are white" came from: noon's band ran 150..355 m, so anything more
        /// than a couple of blocks away was fully replaced by fog colour, and
        /// a view with any distance in it came back as a white wall with a
        /// road leading into it. 1.4 and 500 m put the band at 210..497 and
        /// leave the far plane just past it.
        ///
        /// 500 m and not further because of THE CITY TILE RING, which is the
        /// real limit here: CityWorld keeps two 256 m tiles around the player,
        /// so the built world can be as little as 512 m away and a far plane
        /// past that would show the edge of it. At 500 m the fog is already
        /// full strength before anything can be missing behind it. The
        /// circuits could see further; one number for every non-stage venue is
        /// worth more than the last 15% on a venue you can see across anyway.
        /// </summary>
        const float CircuitFogScale = 1.4f;
        const float CircuitFarClip = 500f;

        /// <summary>How far from the centreline geometry trees exist. Past
        /// this the far slopes are painted as forest by the mottle texture,
        /// which at 150 m+ through PSX fog is indistinguishable.</summary>
        const float ForestBand = 150f;
        /// <summary>Candidate grid pitch for the forest, and how far from the
        /// road every candidate is kept.
        ///
        /// It was 13 m everywhere ("reads as closed canopy once the crowns are
        /// 10 m wide"), and it did not: owner, 2026-09-19, over a frame of an
        /// alpine road walled in by spruce - "notice how thick the trees are.
        /// Trees in this game are too sparse." At 13 m a driver looks straight
        /// THROUGH the first rank to the sky and the ground behind it. 7.5 m
        /// is three times the trees per acre, and it is spent where it is
        /// SEEN: every candidate is kept out to ForestDenseTo, then the keep
        /// rate falls to the old forest's density by 90 m, and thins on from
        /// there as it always has (the far slopes are the mottle's job). About
        /// twice the trees a stage for three times the forest at the roadside.
        /// 32 x 7.5 is the chunk exactly, so there is no bare strip at a
        /// chunk's far edge either (18 x 13 was 234 of 240 m).</summary>
        const float ForestPitch = 7.5f;
        const float ForestDenseTo = 45f;
        /// <summary>The old 13.3 m grid's share of a 7.5 m grid's candidates.</summary>
        const float ForestFarKeep = 0.32f;
        /// <summary>In the dense band, this share of trees are UNDERSTORY: half
        /// to seven tenths the height, so the gap under a ten-metre crown -
        /// which is where a driver's eye actually is - has leaves in it.</summary>
        const float ForestUnderstory = 0.28f;
        /// <summary>The species table's heights were "game-scale, not botany":
        /// nine to twelve metres, two and a half cars. The owner's reference
        /// road is walled in by trees five and six times the height of the
        /// cars on it, and THICK is as much that as it is the count - a ten
        /// metre tree standing on a falling verge barely tops the guard wall.
        /// 1.3 puts the canopy at 12-16 m: still short of a real cove
        /// hardwood, and as far as a 128 px billboard stretches.</summary>
        const float ForestHeightScale = 1.3f;

        const float NearCell = 12f, NearCoverage = 340f, NearChunk = 240f;
        const float FarCell = 60f, FarCoverageDefault = 2300f, FarChunk = 960f;
        /// <summary>How far past the route the far ring reaches — the theme's
        /// number (the mountain's 2300 m by default; 1200 on the flat
        /// Charlotte venues, where the far chunks are download and nothing
        /// else).</summary>
        static float FarCoverage => theme.farCoverage;
        /// <summary>The warm tint on a forest stage's near ground: untinted,
        /// the dirt texture reads grey-green against the mottle's orange and
        /// the border between the two draws itself as a band across the
        /// hills. The Theme default; a city theme sets null.</summary>
        internal static readonly Color StageAutumnTint = new Color(1.0f, 0.90f, 0.70f);
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
        /// <summary>Where the GENERATED art (atlases, mottles, turf, the
        /// cut-bank and shoulder tiles) and the copied tree billboards live.
        /// The theme may point several stages at one folder: every forest
        /// stage composes the same sixteen billboards into the same five
        /// atlases, and three copies of them were three megabytes of build
        /// each — with two of the three clamped to 256 px by the importer's
        /// one-folder exemption, so their trees were half the Parkway's.</summary>
        static string StageShareDir => string.IsNullOrEmpty(theme.artShareDir) ? theme.stageDir : theme.artShareDir;
        static string StageGenDir => StageShareDir + "/Gen";
        static string StageTreesDir => StageShareDir + "/Trees";

        /// <summary>Per station: inside a tunnel span. Built with the DEM;
        /// null on a stage with no tunnels.</summary>
        static bool[] tunnelIn;
        static bool hasTunnels;

        /// <summary>Station index normaliser for the stage passes: modulo on
        /// a loop, clamped on a route with ends. The wall, bank and post
        /// passes clamped everywhere, which on a LOOP stage left a gap in
        /// every run that crossed the start line.</summary>
        static int WrapIdx(int i, int n) => Loop ? ((i % n) + n) % n : Mathf.Clamp(i, 0, n - 1);

        /// <summary>Stations apart along the route, the short way round on a loop.</summary>
        static int StationSep(int i, int j, int n)
        {
            int d = Mathf.Abs(i - j);
            return Loop ? Mathf.Min(d, n - d) : d;
        }

        /// <summary>
        /// Maximal runs of wanted stations, with the three-clear hysteresis
        /// the tunnel and cut-face passes use (two clear stations inside a run
        /// do not end it; the trailing clears are not part of it). Warranted
        /// walls do NOT use it — see MaximalRuns. On a LOOP the scan
        /// starts at a station that is NOT wanted, so a run that crosses the
        /// start line is one run rather than two ending at the seam.
        /// </summary>
        static List<(int from, int len)> StationRuns(bool[] want, int minLen)
        {
            int n = want.Length;
            var runs = new List<(int from, int len)>();
            int origin = 0;
            if (Loop)
            {
                while (origin < n && want[origin]) origin++;
                if (origin >= n) { runs.Add((0, n)); return runs; }
            }
            for (int k = 0; k < n; )
            {
                int i = (origin + k) % n;
                if (!want[i]) { k++; continue; }
                int len = 1, clear = 0;
                while (k + len < n && clear < 3)
                {
                    if (want[(origin + k + len) % n]) clear = 0; else clear++;
                    len++;
                }
                len -= clear;
                if (len >= minLen) runs.Add((i, len));
                k += len + clear;
            }
            return runs;
        }
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
        static Vector3[] stageRight;             // RightAt per waypoint
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
            stageWp = null; stageHash = null; stageRight = null;
            tunnelIn = null; hasTunnels = false;
            ClearStageRoadside();
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
            // The previous venue's roadside plan is keyed by station index,
            // and two stages can have the same station count.
            ClearStageRoadside();
            // Per station, so the ground field can tell which SIDE of the road
            // a point is on without re-deriving the tangent a million times.
            stageRight = new Vector3[stageWp.Count];
            for (int i = 0; i < stageWp.Count; i++) stageRight[i] = RightAt(stageWp, i);
            stageHash = new Dictionary<long, List<int>>();
            for (int i = 0; i < stageWp.Count; i++)
            {
                long k = HashKey(stageWp[i].x, stageWp[i].z);
                if (!stageHash.TryGetValue(k, out var list)) stageHash[k] = list = new List<int>();
                list.Add(i);
            }
            stageDemLoaded = true;

            // The tunnel table: which stations the road passes UNDER the
            // mountain at. Read off the bake's spans, loop-aware.
            tunnelIn = new bool[stageWp.Count];
            hasTunnels = false;
            for (int i = 0; i < stageWp.Count; i++)
            {
                tunnelIn[i] = TrackCatalog.InTunnel(track, i * Spacing);
                hasTunnels |= tunnelIn[i];
            }
            if (hasTunnels) Log("Stage tunnels: " + track.tunnels.Length + " span(s).");

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

        /// <summary>Where a point stands against the centreline: plan
        /// distance, the road height at its foot, the segment the foot lies on
        /// (s0 to s1, t along it; <see cref="station"/> = s0 + t) and which
        /// SIDE of the road the point is on (-1 left, +1 right, as RightAt
        /// points). The side is what lets the ground field grade a fill on one
        /// shoulder and a cut on the other.</summary>
        struct RoadFoot
        {
            public float d, roadY, station, t, side;
            public int s0, s1;
        }

        /// <summary>
        /// Nearest point on the centreline within <paramref name="reach"/>:
        /// distance, the road height there, and how much bridge (BridgeBlend)
        /// that station carries. False when the route is further than reach.
        /// </summary>
        static bool StageCorridor(float x, float z, float reach,
                                  out float d, out float roadY, out float bridge)
        {
            bool hit = StageCorridor(x, z, reach, out RoadFoot foot);
            d = foot.d; roadY = foot.roadY;
            bridge = hit ? BridgeAt(foot.station) : 0f;
            return hit;
        }

        /// <summary>Stations either side of a station that count as the SAME
        /// piece of road. Past this a nearby station is the route coming back
        /// on itself: a parallel stretch, or a grade separation.</summary>
        const int OverlapSep = 40;
        /// <summary>How far below the nearest road another part of the route
        /// has to be, at the same spot in plan, for the ground to follow it
        /// instead. A grade separation clears by 6.5 m; parallel roads on a
        /// hillside differ by less.</summary>
        const float OverlapDropM = 3f;

        static bool StageCorridor(float x, float z, float reach, out RoadFoot foot)
        {
            foot = new RoadFoot { d = float.MaxValue, side = 1f };
            int cells = Mathf.CeilToInt(reach / StageHashCell);
            int cx = Mathf.FloorToInt(x / StageHashCell);
            int cz = Mathf.FloorToInt(z / StageHashCell);
            int n = stageWp.Count;
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

            Refine(best, x, z, Mathf.Sqrt(bestD2), out foot);
            return true;
        }

        /// <summary>How much deck a fractional station carries (0 on the
        /// ground, 1 at mid-span), from the per-station BridgeBlend.</summary>
        static float BridgeAt(float station)
        {
            if (bridgeBlend == null) return 0f;
            int s0 = Mathf.Clamp(Mathf.FloorToInt(station), 0, bridgeBlend.Length - 1);
            int s1 = Loop ? (s0 + 1) % bridgeBlend.Length : Mathf.Min(s0 + 1, bridgeBlend.Length - 1);
            return Mathf.Lerp(bridgeBlend[s0], bridgeBlend[s1], station - s0);
        }

        /// <summary>The nearest station of ANOTHER piece of road: more than
        /// OverlapSep stations along the route from <paramref name="near"/>,
        /// within <paramref name="reach"/> in plan, refined onto its two
        /// segments. False when no other road comes that close.</summary>
        static bool OtherRoadNear(float x, float z, float reach, int near, out RoadFoot foot)
        {
            foot = new RoadFoot { d = float.MaxValue, side = 1f };
            int cells = Mathf.CeilToInt(reach / StageHashCell);
            int cx = Mathf.FloorToInt(x / StageHashCell);
            int cz = Mathf.FloorToInt(z / StageHashCell);
            int n = stageWp.Count;
            int alt = -1; float altD2 = reach * reach;
            for (int oz = -cells; oz <= cells; oz++)
                for (int ox = -cells; ox <= cells; ox++)
                {
                    long k = ((long)(cx + ox) << 32) ^ (uint)(cz + oz);
                    if (!stageHash.TryGetValue(k, out var list)) continue;
                    foreach (int i in list)
                    {
                        if (StationSep(i, near, n) <= OverlapSep) continue;
                        float dx = stageWp[i].x - x, dz = stageWp[i].z - z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 < altD2) { altD2 = d2; alt = i; }
                    }
                }
            if (alt < 0) return false;
            Refine(alt, x, z, Mathf.Sqrt(altD2), out foot);
            return foot.d < reach;
        }

        /// <summary>Refine a nearest-station answer onto the two segments
        /// touching it, same as the circuit field does: the shelf must
        /// follow the LINE, not step from waypoint to waypoint. Wraps on a
        /// loop, so the closing segment is a segment too.</summary>
        static void Refine(int best, float x, float z, float d0, out RoadFoot foot)
        {
            int n = stageWp.Count;
            foot = new RoadFoot
            {
                d = d0, roadY = stageWp[best].y, station = best,
                s0 = best, s1 = WrapIdx(best + 1, n), t = 0f, side = 1f,
            };
            for (int o = -1; o <= 0; o++)
            {
                int a = WrapIdx(best + o, n);
                int b = WrapIdx(a + 1, n);
                if (a == b) continue;
                float ax = stageWp[a].x, az = stageWp[a].z;
                float ex = stageWp[b].x - ax, ez = stageWp[b].z - az;
                float len2 = ex * ex + ez * ez;
                if (len2 < 1e-6f) continue;
                float t = Mathf.Clamp01(((x - ax) * ex + (z - az) * ez) / len2);
                float px = ax + ex * t, pz = az + ez * t;
                float dd = Mathf.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z));
                if (dd < foot.d)
                {
                    foot.d = dd;
                    foot.roadY = Mathf.Lerp(stageWp[a].y, stageWp[b].y, t);
                    foot.station = a + t;
                    foot.s0 = a; foot.s1 = b; foot.t = t;
                }
            }
            // Which side: the offset from the foot against the right vector
            // interpolated along the segment, so the answer turns with the
            // road rather than flipping at each waypoint.
            if (stageRight != null)
            {
                Vector3 pa = stageWp[foot.s0], pb = stageWp[foot.s1];
                Vector3 r = Vector3.Lerp(stageRight[foot.s0], stageRight[foot.s1], foot.t);
                float fx = Mathf.Lerp(pa.x, pb.x, foot.t), fz = Mathf.Lerp(pa.z, pb.z, foot.t);
                foot.side = (x - fx) * r.x + (z - fz) * r.z >= 0f ? 1f : -1f;
            }
        }

        /// <summary>Inside a tunnel's own footprint: within the tube's walls
        /// (and a metre) of a station the road passes under the mountain at.
        /// No ground vertex may stand here — the tube is the floor's roof.
        ///
        /// This reached RoadWidth/2 + 7 m, and because a quad is dropped when
        /// ANY corner is a hole, the ground was missing out to ~20 m beside
        /// the tube and a lattice cell in front of each portal, with nothing
        /// under a car that left the approach. The wide reach is still what
        /// catches a quad STRADDLING the mouth (see <see cref="InTunnelZone"/>
        /// and GridChunkMesh); only the vertex hole is the tube's width now.
        /// </summary>
        static bool InTunnelHole(float x, float z)
        {
            if (!hasTunnels) return false;
            if (!StageCorridor(x, z, RoadWidth * 0.5f + TunnelWallOut + TunnelFootprintMarginM, out RoadFoot foot))
                return false;
            return tunnelIn[Mathf.Clamp(Mathf.RoundToInt(foot.station), 0, tunnelIn.Length - 1)];
        }

        /// <summary>Near a tunnel: within the old hole reach of a tunnel
        /// station. A ground quad with a corner in here has to pass
        /// <see cref="TunnelQuadClear"/> to be built.</summary>
        static bool InTunnelZone(float x, float z)
        {
            if (!hasTunnels) return false;
            if (!StageCorridor(x, z, RoadWidth * 0.5f + TunnelHoleMargin, out RoadFoot foot)) return false;
            return tunnelIn[Mathf.Clamp(Mathf.RoundToInt(foot.station), 0, tunnelIn.Length - 1)];
        }

        /// <summary>Samples per side of a quad TunnelQuadClear reads.</summary>
        const int TunnelQuadSamples = 5;

        /// <summary>
        /// Do a near-tube lattice quad's own triangles keep out of the tube and
        /// out of the approach? Sampled on a 5x5 grid over the quad, read the
        /// way GridChunkMesh triangulates it: over the bore (within the tube's
        /// wall, and half a metre) the facet must clear the ceiling by
        /// TunnelCoverM; beside a station that is NOT a tunnel's — the approach
        /// and its graded roadside — it may stand no higher than the field there
        /// plus the hide margin, which under a shoulder ribbon is the ribbon.
        /// Python replica, Little Switzerland: of the 56 quads the old any-corner
        /// rule dropped, 12 pass (the ridge beside the bore) and none of those
        /// touches the tube or an approach section.
        /// </summary>
        static bool TunnelQuadClear(float x0, float z0, float cell, float ha, float hb, float hc, float he)
        {
            float reach = RoadWidth * 0.5f + TunnelHoleMargin;
            for (int iu = 0; iu < TunnelQuadSamples; iu++)
                for (int iw = 0; iw < TunnelQuadSamples; iw++)
                {
                    float u = iu / (TunnelQuadSamples - 1f), w = iw / (TunnelQuadSamples - 1f);
                    float y = w >= u ? ha + (hc - hb) * u + (hb - ha) * w
                                     : ha + (he - ha) * u + (hc - he) * w;
                    float x = x0 + u * cell, z = z0 + w * cell;
                    if (!StageCorridor(x, z, reach, out RoadFoot foot)) continue;
                    if (tunnelIn[Mathf.Clamp(Mathf.RoundToInt(foot.station), 0, tunnelIn.Length - 1)])
                    {
                        if (foot.d <= RoadWidth * 0.5f + TunnelWallOut + 0.5f && y < foot.roadY + TunnelH + TunnelCoverM)
                            return false;
                    }
                    else if (y > StageGroundHeightAt(x, z) + RoadsideRules.HideMarginM)
                        return false;
                }
            return true;
        }

        /// <summary>
        /// Ground height on the stage: the real DEM, with the road corridor
        /// pinned exactly the way the circuits pin theirs — graded at the
        /// roadside to the plan's section — and released back to the real
        /// slope through a bridge span, where the deck carries the road and
        /// the mountainside is allowed to fall away underneath.
        /// </summary>
        static float StageGroundHeightAt(float x, float z)
        {
            float dem = StageDemY(x, z);
            // The roadside plan is what the field is graded to. PlanStageRoadside
            // runs from Build() before the road; this only covers a caller
            // that asks for ground first, and only once the bridge table is
            // this venue's.
            if (rsKind == null && bridgeBlend != null && bridgeBlend.Length == stageWp.Count)
                PlanStageRoadside(stageWp);
            if (!StageCorridor(x, z, CorridorR + CorridorBlend + 4f, out RoadFoot foot))
                return dem;
            float g = GroundFromCorridor(dem, foot);
            float f = BridgeAt(foot.station);
            float roadY = foot.roadY, d = foot.d;

            // THE OTHER ROAD. Where the route crosses itself at a grade
            // separation (both loops pass under their own Parkway bridge)
            // the stations of the two roads share a spot in plan, and on the
            // lower road's tarmac "nearest" can be the deck overhead: its pin
            // releases to the raw DEM at mid-span, and SRTM noise comes up
            // through the lower road as a hump. So UNDER A DECK (f past a
            // half) the ground is what the road beneath wants -- its own
            // shelf, fall and blend -- faded in over the second half of the
            // deck's blend so the abutments keep their embankments, and still
            // capped under the soffit.
            //
            // Only under a deck. The first cut of this asked "is another part
            // of the route lower, within reach" EVERYWHERE, which is also what
            // a hairpin's upper leg and a parallel stretch's higher road look
            // like, and took the pin off both: three buried probes on Mount
            // Mitchell, thirty-four on Little Switzerland's ridge, and a flat
            // apron on four stages' shoulders (2026-09-11).
            if (f > 0.5f
                && OtherRoadNear(x, z, CorridorR + CorridorBlend,
                                 WrapIdx(Mathf.RoundToInt(foot.station), stageWp.Count),
                                 out RoadFoot below)
                && below.roadY < roadY - OverlapDropM && BridgeAt(below.station) < 0.5f)
            {
                float under = GroundFromCorridor(dem, below);
                g = Mathf.Lerp(g, under, Mathf.InverseLerp(0.5f, 1f, f));
                if (d < DeckHalfWidth + 2f) g = Mathf.Min(g, roadY - DeckThick - 0.4f);
            }
            return g;
        }

        /// <summary>The ground the corridor wants at a point, given the road
        /// it is measured from. Split out so the road UNDER a deck can be
        /// asked the same question.</summary>
        static float GroundFromCorridor(float dem, in RoadFoot foot)
        {
            float d = foot.d, roadY = foot.roadY, station = foot.station;
            // THE MOUNTAIN STANDS OVER A TUNNEL. The corridor pin would dig
            // the ridge out into a trench thirty metres deep; inside a
            // tunnel span the ground is simply the real land, and the road
            // runs under it in its tube (BuildStageTunnels). The ground mesh
            // has no quads over the road there, so nothing rises through it.
            if (hasTunnels && tunnelIn[Mathf.Clamp(Mathf.RoundToInt(station), 0, tunnelIn.Length - 1)])
                return dem;

            float f = BridgeAt(station);
            float blend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(CorridorR, CorridorR + CorridorBlend, d));

            // The dig under the pavement (RoadbedSinkAt), then the graded
            // roadside the plan asked for beside it.
            float shelf = GradedRoadside(roadY - RoadbedSinkAt(d), dem, foot);

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
            //
            // THE URBAN DIG. In a flat city SRTM reads street level under an
            // overpass, so releasing to the DEM inside a span gives the deck
            // no daylight at all — the terrain audit wants three metres under
            // every full-blend station, and the piers want a trench to stand
            // in. Where the theme says so, the ground under a span is dug to
            // the track's bridgeDepth, faded by the corridor blend so the
            // trench is the corridor's width and slopes back to the real
            // ground by the outer edge of it. It can only ever LOWER ground,
            // and it is a theme flag rather than a rule because the
            // mountains' spans cross real gorges the DEM already has: turning
            // it on there would move three shipped abutments.
            if (theme.stageBridgeDig)
                released = Mathf.Min(released, roadY - track.bridgeDepth * f * (1f - blend));
            return Mathf.Min(released, pinned);
        }

        /// <summary>
        /// The shelf beside the road, graded to the plan's section at the two
        /// stations either side of the foot and blended between them along the
        /// segment — which is exactly how the shoulder ribbon between those two
        /// stations is laid, so the lattice and the ribbon are sampled from
        /// the same surface.
        ///
        /// Under the section the lattice is held <see cref="RoadsideRules.HideMarginM"/>
        /// under it (the coarse grid may stay sunk under an exact surface; it
        /// may not poke through one). Everywhere it only ever LOWERS the
        /// roadbed shelf, so the dig under the tarmac and every clearance it
        /// buys are unchanged — except behind a cut face, where the whole point
        /// is to let the hill come back up (see <see cref="RoadsideDy"/>).
        /// </summary>
        static float GradedRoadside(float shelf, float dem, in RoadFoot foot)
        {
            if (rsKind == null) return shelf;
            int s = SideIx(foot.side);
            float e = foot.d - RoadWidth * 0.5f;
            float tarmac = foot.roadY + RoadLift;
            float demDy = dem - tarmac;
            float g0 = RoadsideDy(foot.s0, s, e, demDy, out bool rel0);
            float g1 = RoadsideDy(foot.s1, s, e, demDy, out bool rel1);
            float g = tarmac + Mathf.Lerp(g0, g1, foot.t);
            return rel0 || rel1 ? g : Mathf.Min(shelf, g);
        }

        /// <summary>
        /// Tarmac-relative height the ground wants at <paramref name="e"/>
        /// metres past the tarmac edge of station <paramref name="st"/>, on
        /// side index <paramref name="s"/>. <paramref name="released"/> says
        /// the answer may stand ABOVE the roadbed shelf (behind a cut face).
        /// </summary>
        static float RoadsideDy(int st, int s, float e, float demDy, out bool released)
        {
            released = false;
            float hide = RoadsideRules.HideMarginM;
            e = Mathf.Max(e, KerbWidth);
            // Where the land rises, the graded side goes no higher than the
            // bench (see StageBenchDy); where it falls, it is the land.
            float land = Mathf.Min(demDy, StageBenchDy);
            var kind = GradedKind(s, st);
            switch (kind)
            {
                case Roadside.Open:
                {
                    // Full hide under the section — deeper past the clear
                    // zone's first metre, where a long foreslope runs down
                    // into a dip the lattice's chords bridge over — fading to
                    // none AT the catch: the lattice arrives at the ribbon's
                    // toe at the toe's own height and the two cross
                    // (RoadsideRules.ToeTuckM) rather than the toe standing a
                    // hide margin proud of the land.
                    float ec = rsCatchE[s][st];
                    float keep = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ec - StageHideFadeM, ec, e));
                    float depth = hide + StageOuterHideM * Mathf.InverseLerp(ShoulderEndE + 1f,
                                      ShoulderEndE + RoadsideRules.ClearZoneM, e);
                    return Mathf.Max(OpenSectionDy(e) - depth * keep, land);
                }
                case Roadside.Cut:
                {
                    if (e <= CutToeE) return CutSectionDy(e) - hide;
                    // BEHIND THE FACE THE HILL COMES BACK.
                    //
                    // The old corridor held the shelf flat to CorridorR behind
                    // every cut: a bench 9.65 m wide on the Parkway, at -0.32 m,
                    // hidden behind a face drawn from one side only. It cannot
                    // simply be the DEM, because a lattice vertex a cell
                    // diagonal (17 m) away still weighs on the ditch; so it is
                    // held for BankPinM, released over BankReleaseM (faded in
                    // from each run end), and the rock top BuildStageBanks lays
                    // from the crest back over it is what a car finds there.
                    float pin = Mathf.Min(CutDitchDy, StageBenchDy) - hide;
                    float r = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CutToeE + BankPinM,
                                  CutToeE + BankPinM + BankReleaseM, e)) * rsRelease[s][st];
                    released = true;
                    return Mathf.Min(Mathf.Lerp(pin, demDy, r), Mathf.Max(demDy, pin));
                }
                default:
                {
                    // Walled (standing stone — a buried terminal is graded
                    // Open, see BuriedTerminal), Deck (the parapet) and Tunnel
                    // (the tube's wall): the shoulder to the face, then — under
                    // the stone and past it — the old shelf-and-fill-batter,
                    // never below the land.
                    float ew = kind == Roadside.Walled ? rsWallE[s][st]
                             : kind == Roadside.Tunnel ? TunnelWallOut : WallFaceE;
                    float dw = e <= ew ? ShoulderDy(e)
                             : ShoulderDy(ew) - Mathf.Max(0f, e - ew - StageWallBackFlatM) * StageFillBatter;
                    float keep = 1f - Mathf.Clamp01((e - ew) / StageWallDrawThick);
                    return Mathf.Max(dw, land) - hide * keep;
                }
            }
        }

        /// <summary>The height of the built near-ground LATTICE at a point —
        /// the 12 m grid GridChunkMesh triangulates, read back the same way
        /// (vertices on multiples of NearCell, the a-c diagonal, each vertex as
        /// <see cref="StageLatticeVertexY"/> has it once PrepareStageLattice
        /// has solved it) — rather than the smooth field it samples. What a
        /// wall's footing, a rock top's tuck and the shoulder emitter's toe
        /// (ShoulderLatticeY) are measured against: between vertices the
        /// lattice can be a metre off the field on a falling verge.</summary>
        static float StageLatticeY(float x, float z)
        {
            int gx = Mathf.FloorToInt(x / NearCell), gz = Mathf.FloorToInt(z / NearCell);
            float u = x / NearCell - gx, w = z / NearCell - gz;
            float ha = StageLatticeVertexY(gx, gz), hc = StageLatticeVertexY(gx + 1, gz + 1);
            if (w >= u)
            {
                float hb = StageLatticeVertexY(gx, gz + 1);
                return ha + (hc - hb) * u + (hb - ha) * w;
            }
            float he = StageLatticeVertexY(gx + 1, gz);
            return ha + (he - ha) * u + (hc - he) * w;
        }

        // ------------------------------------------------------------------
        //  The near lattice under the road and shoulder
        // ------------------------------------------------------------------
        /// <summary>The stage field at each near-grid vertex (keyed by
        /// <see cref="LatticeKey"/>), read once the plan is final
        /// (<see cref="StageLatticeSolved"/>). The field is a hash walk, a
        /// segment refinement and a DEM read per call, and the solve, the
        /// toes, the walls, the rock tops and the ground mesh all ask for the
        /// same vertices over and over.</summary>
        static Dictionary<long, float> stageLatticeField;
        /// <summary>How far <see cref="PrepareStageLattice"/> lowered each
        /// near-grid vertex below the field. Absent is not at all.</summary>
        static Dictionary<long, float> stageLatticeSink;
        /// <summary>Per vertex: does a near-tube quad's clearance test read
        /// it (<see cref="StageLatticeNearTube"/>)?</summary>
        static Dictionary<long, bool> stageLatticeTube;
        /// <summary>The plan table and the waypoint list the solve ran under.
        /// A new venue loads a new route, a re-plan makes a new table, and
        /// either one retires the solve and everything cached against it.</summary>
        static object stageLatticePlan, stageLatticeRoute;

        /// <summary>Samples along each station's own cross-section are at most
        /// this far apart — the terrain audit's ShoulderPitchM — with one more
        /// on every profile point and every lattice crease between them
        /// (<see cref="StageLatticeCreases"/>), so the line the audit rays is
        /// held along its whole length, wherever its probes happen to land.</summary>
        const float StageLatticeLinePitchM = 0.25f;
        /// <summary>Across-pitch of the samples between two stations, where
        /// the ribbon is the zipper of two sections.</summary>
        const float StageLatticeQuadPitchM = 0.5f;
        /// <summary>Samples per station gap along the road, counting the
        /// station line itself as the first: every metre of a 4 m gap.</summary>
        const int StageLatticeAlong = 4;
        /// <summary>
        /// The least margin a section that ends on a slope keeps over the
        /// lattice, all the way to its last point (see StageLatticeHold).
        ///
        /// RoadsideDy fades the field's hide to nothing at an open section's
        /// catch (StageHideFadeM) so the lattice meets the ribbon's toe there
        /// and the two cross. Held to that, a sample a few centimetres inside
        /// the catch is legitimately "lattice within 3 cm", and the terrain
        /// audit only excuses such a probe while it trails unbroken into the
        /// toe — a toe the emitter may have carried a metre further on, past
        /// a probe that has the margin, after which the close ones count.
        /// Five centimetres is the audit's 3 cm, the stage chunk's own vertex
        /// quantisation (16 bits over the chunk's whole range, world height
        /// included: a 1.2 cm step, so up to 6 mm either way, on Beech Gap's
        /// 776 m summit) and a centimetre to spare; below a 1V:4H carried slope
        /// it moves the crossing 0.2 m out.
        /// </summary>
        const float StageCatchHoldM = 0.05f;
        /// <summary>Added to every correction so a sample the solve fixed is
        /// not left a float's width over its target.</summary>
        const float StageLatticeSolveSlackM = 0.002f;

        static long LatticeKey(int gx, int gz) => ((long)gx << 32) ^ (uint)gz;

        /// <summary>
        /// Has PrepareStageLattice run for the roadside plan and the route now
        /// loaded? Until it has, the lattice is simply the field, read fresh
        /// every time and never cached: the plan itself reads the lattice while
        /// it is still deciding (the fall walk reads it after each laying, and
        /// a wall that walk adds grades the field behind its stone differently
        /// on the next; FinishStageCuts lands every crest on it), so a value
        /// cached then would be a value from a plan that no longer exists.
        /// From the solve on the plan is final, and a new venue or a re-plan
        /// (a new route list, a new plan table) puts it back to unsolved.
        /// </summary>
        static bool StageLatticeSolved =>
            stageLatticeField != null && ReferenceEquals(stageLatticePlan, rsKind)
            && ReferenceEquals(stageLatticeRoute, stageWp);

        /// <summary>
        /// One near-grid vertex of the lattice: the stage field at
        /// (gx, gz) x NearCell, less whatever PrepareStageLattice took off it.
        /// GridChunkMesh builds the near ground from exactly this, and
        /// StageLatticeY reads it back, so the ground a toe or a footing was
        /// measured against is the ground that gets built.
        /// </summary>
        static float StageLatticeVertexY(int gx, int gz)
        {
            if (!StageLatticeSolved) return StageGroundHeightAt(gx * NearCell, gz * NearCell);
            long k = LatticeKey(gx, gz);
            if (!stageLatticeField.TryGetValue(k, out float h))
                stageLatticeField[k] = h = StageGroundHeightAt(gx * NearCell, gz * NearCell);
            return stageLatticeSink.TryGetValue(k, out float sink) ? h - sink : h;
        }

        /// <summary>Does any of the four quads round this vertex have a corner
        /// within a tunnel's zone (<see cref="InTunnelZone"/>) — is any of the
        /// nine vertices from (gx-1, gz-1) to (gx+1, gz+1) in it? Such a quad
        /// is built only if TunnelQuadClear passes its own facets over the
        /// bore, so a vertex it reads is not the solve's to move.</summary>
        static bool StageLatticeNearTube(int gx, int gz)
        {
            if (!hasTunnels) return false;
            long k = LatticeKey(gx, gz);
            bool cache = StageLatticeSolved;
            if (cache && stageLatticeTube.TryGetValue(k, out bool known)) return known;
            bool near = false;
            for (int oz = -1; oz <= 1 && !near; oz++)
                for (int ox = -1; ox <= 1 && !near; ox++)
                    near = InTunnelZone((gx + ox) * NearCell, (gz + oz) * NearCell);
            if (cache) stageLatticeTube[k] = near;
            return near;
        }

        /// <summary>
        /// THE NEAR LATTICE STAYS UNDER THE SHOULDER BETWEEN ITS VERTICES TOO.
        ///
        /// RoadsideDy holds the FIELD HideMarginM under every graded section
        /// (StageOuterHideM more under a long foreslope), but the field is only
        /// what the lattice samples: GridChunkMesh puts a vertex on it every
        /// 12 m and a flat triangle between, and the triangle is what a wheel
        /// and an eye meet. Over a gully in the DEM, across a crest, and where
        /// a switchback's lower roadside lies under a triangle whose far vertex
        /// the upper leg's hillside holds up, that triangle crosses above the
        /// shoulder between vertices that each kept the margin. The terrain
        /// audit on the round-one bake: 1 to 82 probes of grass through the
        /// ribbon on every mountain and on the coast, worst 0.86 m in the
        /// ditch at Mount Mitchell wp 1494 R — measured off the saved meshes,
        /// a facet rising from the kerb (+0.03 m) to +2.6 m 8 m out, over a
        /// ditch 0.23 m under the tarmac.
        ///
        /// So the stage does what PrepareCircuitLattice does: sample every
        /// designed surface — the tarmac and strip, and each station's section
        /// along its own cross-section line at the audit's quarter metre and on
        /// every lattice crease it crosses, and between stations every metre —
        /// and wherever the lattice triangle
        /// over a sample stands higher than the surface less its margin, lower
        /// that triangle's vertices until it does not. Each vertex takes its
        /// least-squares share of the excess (excess x w / sum of w squared),
        /// so the vertex the sample sits on carries the correction and one a
        /// cell away barely moves: the hide stays a trough under the roadside
        /// rather than a pit round it. The largest share asked of a vertex
        /// wins, which satisfies every sample at once; a second pass takes the
        /// float residue. It only ever LOWERS, so nothing loses clearance, and
        /// it runs before a single toe is placed (BuildShoulders), so the toes,
        /// the walls' footings, the rock tops' tucks and the ground mesh all
        /// read the solved lattice. The margin is <see cref="StageLatticeHold"/>'s:
        /// the full hide, fading to StageCatchHoldM where a section ends on a
        /// slope and its toe is meant to cross the land.
        ///
        /// Python replica on the saved round-one meshes (the built ribbon
        /// standing in for the design surface, the chunks' vertices for the
        /// field): grass through the ribbon and lattice within 3 cm of it both
        /// went to zero on all seven stages, moving 54 (Langston) to 1,156
        /// (Blowing Rock) vertices, p50 under 1.5 cm, p90 4-23 cm. (That
        /// replica held the lattice under everything a station's audit line
        /// crossed; this holds it under the designed sections only, and the
        /// toes are laid on it afterwards. On the round-one bake the counted
        /// probes it does not reach were other stations' toes — Blowing Rock's
        /// deck stations over its own lower road, three insides of bends on
        /// Little Switzerland — which the audit no longer reads as this
        /// station's shoulder: TerrainAudit.OwnSection.) The deepest,
        /// 0.5-1.45 m, are mostly vertices 5-8 m from the centreline — under
        /// the road's edge or its shoulder, where the tarmac and the ribbon
        /// cover what moved — whose triangles reach land two metres above or
        /// below (Mount Mitchell wp 1494's dig vertex, next to one 6.7 m up the
        /// released hill behind the cut).
        ///
        /// Not inside a tunnel (the mountain stands over the road there on
        /// purpose), and not at a vertex a near-tube quad is measured by.
        /// </summary>
        static void PrepareStageLattice(List<Vector3> pts)
        {
            // The plan is final from here (BuildShoulders has just read every
            // section out of it): start the caches clean, against it.
            stageLatticeField = new Dictionary<long, float>();
            stageLatticeSink = new Dictionary<long, float>();
            stageLatticeTube = new Dictionary<long, bool>();
            stageLatticePlan = rsKind;
            stageLatticeRoute = stageWp;
            if (shoulderProfiles == null || pts == null || pts.Count < 2) return;

            var want = new Dictionary<long, float>();
            int samples = 0, nearTube = 0;
            float worst = 0f;
            long worstKey = 0;
            void Want(long k, float v)
            {
                if (v > 0f && (!want.TryGetValue(k, out float had) || v > had)) want[k] = v;
            }
            // One sample: 1 when the facet over it asked its vertices to come
            // down, -1 when it would have but a near-tube quad owns them.
            int WantUnder(float x, float z, float target)
            {
                int gx = Mathf.FloorToInt(x / NearCell), gz = Mathf.FloorToInt(z / NearCell);
                float u = x / NearCell - gx, w = z / NearCell - gz;
                // GridChunkMesh's two triangles per cell, weighted the way
                // StageLatticeY interpolates them.
                int bx, bz;
                float wa, wb, wc;
                if (w >= u) { bx = gx; bz = gz + 1; wa = 1f - w; wb = w - u; wc = u; }
                else { bx = gx + 1; bz = gz; wa = 1f - u; wb = u - w; wc = w; }
                float excess = StageLatticeVertexY(gx, gz) * wa + StageLatticeVertexY(bx, bz) * wb
                             + StageLatticeVertexY(gx + 1, gz + 1) * wc - target;
                if (excess <= 0f) return 0;
                if (StageLatticeNearTube(gx, gz) || StageLatticeNearTube(bx, bz)
                    || StageLatticeNearTube(gx + 1, gz + 1))
                    return -1;
                excess += StageLatticeSolveSlackM;
                float sw2 = wa * wa + wb * wb + wc * wc;
                Want(LatticeKey(gx, gz), excess * wa / sw2);
                Want(LatticeKey(bx, bz), excess * wb / sw2);
                Want(LatticeKey(gx + 1, gz + 1), excess * wc / sw2);
                return 1;
            }
            HashSet<long> heldKeys = null;
            void Apply()
            {
                foreach (var kv in want)
                {
                    stageLatticeSink.TryGetValue(kv.Key, out float had);
                    stageLatticeSink[kv.Key] = had + kv.Value;
                    if (had + kv.Value > worst) { worst = had + kv.Value; worstKey = kv.Key; }
                    heldKeys?.Add(kv.Key);
                }
            }
            // The same two passes over a list of extra (x, target, z) holds;
            // how many of its samples asked anything of the lattice.
            int SolveHolds(List<Vector3> holds)
            {
                int asked = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    want.Clear();
                    foreach (var h in holds)
                        if (WantUnder(h.x, h.z, h.y) > 0 && pass == 0) asked++;
                    if (want.Count == 0) break;
                    Apply();
                }
                return asked;
            }
            for (int pass = 0; pass < 2; pass++)
            {
                want.Clear();
                bool first = pass == 0;
                ForEachStageLatticeSample(pts, (x, z, target) =>
                {
                    if (first) samples++;
                    if (WantUnder(x, z, target) < 0 && first) nearTube++;
                });
                if (want.Count == 0) break;
                Apply();
            }
            // Behind the stone, then under every carried slope: the two places
            // a car meets the lattice that no designed section covers, each
            // lowered only where it stood where the plan did not put it.
            heldKeys = new HashSet<long>();
            var holdList = new List<Vector3>();
            int behindWalls = CollectBehindWallHolds(pts, holdList);
            if (holdList.Count > 0) SolveHolds(holdList);
            int grazes = 0, clearZones = 0, carryPasses = 0;
            bool carryConverged = false;
            for (int pass = 0; pass < StageCarryHoldPasses; pass++)
            {
                holdList.Clear();
                CollectCarryHolds(pts, holdList, out int g, out int c);
                if (pass == 0) { grazes = g; clearZones = c; }
                if (holdList.Count == 0 || SolveHolds(holdList) == 0) { carryConverged = true; break; }
                carryPasses = pass + 1;
            }

            // Where the deepest one is, so a bake log can be checked against
            // the ground there without a replica.
            float wx = (int)(worstKey >> 32) * NearCell, wz = (int)(uint)worstKey * NearCell;
            Log((stageLatticeSink.Count == 0
                ? $"Stage ground lattice: already {RoadsideRules.HideMarginM:0.00} m under the road and shoulder " +
                  $"between its vertices ({samples} samples)"
                : $"Stage ground lattice: {stageLatticeSink.Count} near-grid vertices lowered (up to {worst:0.000} m, " +
                  $"at {wx:0},{wz:0}) so every facet sits {RoadsideRules.HideMarginM:0.00} m under the road and " +
                  $"shoulder ({StageCatchHoldM:0.00} m at a catch), from {samples} samples" +
                  (nearTube > 0 ? $"; {nearTube} samples over a tunnel's measured quads left to TunnelQuadClear" : "")) +
                $"; then {heldKeys.Count} of them (further) for the fill behind {behindWalls} walled half-section(s) " +
                $"and under {grazes} carried slope(s) that grazed it and {clearZones} clear zone(s) it fell away from " +
                $"faster than 1V:{1f / RoadsideRules.SteepestRecoverableSlope:0}H ({carryPasses} pass(es)" +
                (carryConverged ? ")." : ", and its last pass still asked: raise StageCarryHoldPasses)."));
        }

        /// <summary>Solves of the carried-slope holds (<see cref="CollectCarryHolds"/>):
        /// a hold lowers the lattice, which carries a slope further, which can
        /// meet a new crease. In the replica Blue Ridge and Little Switzerland
        /// each needed three solves (3, 2, 1 and 2, 1, 1 grazes) and a fourth
        /// look found nothing; the rest is headroom, and the loop stops at the
        /// first look that asks nothing — so the log can say it converged.
        /// Since the tail (ShoulderTailSlope) took over the carries that ran out
        /// of depth, Blue Ridge needs three (2, 1, 1) and Little Switzerland
        /// and Blowing Rock none: their grazes were on those carries.</summary>
        const int StageCarryHoldPasses = 5;
        /// <summary>Pitch at which a carried slope's gap to the lattice is
        /// read, and at which its holds are laid.</summary>
        const float StageCarryReadPitchM = 0.05f, StageCarryHoldPitchM = 0.1f;
        /// <summary>A graze is a carried slope within the terrain audit's
        /// RoadsideRules.LatticeUnderMinM of the lattice and this much more —
        /// the stage chunks' own vertex quantisation and a float's width, as
        /// in <see cref="StageCatchHoldM"/>.</summary>
        const float StageGrazeSlackM = 0.01f;
        /// <summary>How much of the terrain audit's RoadsideRules.ToeCrossingMaxM
        /// the run-in to a real catch may use before its inner end is a graze:
        /// the audit counts that crossing from the toe, so the toe itself
        /// (ShoulderStation's tuck and skirt, laid past the catch) comes off
        /// it — 1.5 m.</summary>
        const float StageCrossingAllowM = RoadsideRules.ToeCrossingMaxM - (RoadsideRules.ToeTuckRunM + ShoulderSkirtRunM);
        /// <summary>Lattice steeper than 1V:4H for this long past a carry's
        /// catch, inside the clear zone, is a clear zone it falls away from —
        /// half the audit's RoadsideRules.SlopeSustainM, over its
        /// RoadsideRules.SlopeWindowM, so the builder holds what the audit
        /// would only be close to failing.</summary>
        const float StageClearZoneSteepM = 0.5f;
        /// <summary>Grade allowance over 1V:4H before a window counts as
        /// steeper (a third of the audit's own SlopeNoise).</summary>
        const float StageClearZoneNoise = 0.005f;

        /// <summary>
        /// THE LATTICE UNDER A CARRIED SLOPE, AND IN FRONT OF IT.
        ///
        /// The solve holds the lattice under what the plan DESIGNED; a sloped
        /// end's carry (ShoulderCarry) is laid afterwards on whatever the
        /// lattice turned out to be, and two things about that lattice showed
        /// in the 2026-09-13 bake, both at the buried terminal of a critical
        /// fill run, where the 12 m facets fall from a catch the solve had just
        /// held flat to the fill's low vertices beyond:
        ///   * A GRAZE. The carried 1V:4H line passes a facet crease a
        ///     centimetre or two over it and goes on over steeper lattice to
        ///     its real catch or the end of its run. The emitter calls that
        ///     "above", the terrain audit calls it "the ground lattice within
        ///     0.03 m of the shoulder" — not the crossing into the toe it
        ///     excuses, because the gap opens again after it: Blue Ridge wp
        ///     1094 L (+0.019 m at 4.53 m) and Little Switzerland wp 610 L
        ///     (+0.011 m at 7.78 m), the only two failing probes of 66,000.
        ///     Wherever a carried line comes within RoadsideRules
        ///     LatticeUnderMinM (and StageGrazeSlackM) of the lattice other
        ///     than in the last StageCrossingAllowM of its run into a real
        ///     catch, the lattice is held StageCatchHoldM under the line up to
        ///     that run-in — the solve's catch margin and the audit's band
        ///     agreeing on what a catch is.
        ///   * A CLEAR ZONE THE LAND FALLS AWAY FROM. The carry meets a facet
        ///     the solve held at the catch and stops, and the next facet falls
        ///     at 1V:3.1H inside the clear zone: Blue Ridge wp 1227 L, "a
        ///     foreslope steeper than 1V:4H for 1.30 m from 3.35 m". No ribbon
        ///     from that catch can cover it (a 1V:4H line from the catch lies
        ///     under the flat facet all the way), so where the lattice past a
        ///     catch inside the clear zone is steeper than 1V:4H for
        ///     StageClearZoneSteepM, it is held StageCatchHoldM under the
        ///     carried line to the clear zone's end and half a metre past: the
        ///     slope then carries over it, and the clear zone is the 1V:4H
        ///     ribbon RDG grades it to.
        /// Only half-sections graded open (the carry of a cut's backslope goes
        /// down under its own rock top). Python replica of the plan, walk,
        /// lattice, solve and emitter on all five mountains: the two probes and
        /// the one slope gone, no new face, fall or slope, 3-10 more vertices a
        /// venue lowered by a few centimetres.
        /// </summary>
        static void CollectCarryHolds(List<Vector3> pts, List<Vector3> into, out int grazes, out int clearZones)
        {
            grazes = clearZones = 0;
            int n = pts.Count;
            float half = RoadWidth * 0.5f;
            float czEnd = KerbWidth + RoadsideRules.ClearZoneM;
            float window = RoadsideRules.SlopeWindowM;
            var gaps = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                if ((hasTunnels && tunnelIn != null && i < tunnelIn.Length && tunnelIn[i]) || DeckCoversStation(i)) continue;
                for (int s = 0; s < 2; s++)
                {
                    if (GradedKind(s, i) != Roadside.Open) continue;
                    var prof = shoulderProfiles[s][i];
                    int m = prof != null ? prof.Count : 0;
                    if (m < 2) continue;
                    float slope = (prof[m - 2].y - prof[m - 1].y) / Mathf.Max(prof[m - 1].x - prof[m - 2].x, 1e-5f);
                    if (Mathf.Abs(slope) < ShoulderSlopedEnd) continue;
                    float side = s == 0 ? -1f : 1f;
                    Vector3 at = pts[i], outw = rsRight[i] * side;
                    float eEnd = prof[m - 1].x, yEnd = at.y + RoadLift + prof[m - 1].y;
                    float Land(float e)
                    {
                        Vector3 q = at + outw * (half + e);
                        return StageLatticeY(q.x, q.z);
                    }
                    if (Land(eEnd) >= yEnd) continue;
                    float fall = Mathf.Max(slope, RoadsideRules.SteepestRecoverableSlope);
                    if (!ShoulderCarry(eEnd, yEnd, fall, ShoulderBendReach(pts, i, side), ShoulderFoldReach(pts, i, side),
                                       ShoulderTailE, Land,
                                       out float kneeE, out float kneeY, out float catchE, out float catchY, out bool met, out _,
                                       out bool tailed))
                        continue;
                    float Line(float e) => !float.IsNaN(kneeE) && e > kneeE
                        ? kneeY + (catchY - kneeY) * (e - kneeE) / Mathf.Max(catchE - kneeE, 1e-5f)
                        : yEnd - fall * (e - eEnd);

                    // Not along a tail (ShoulderTailSlope): a 1V:2.5H tail and a
                    // hillside nearly as steep run side by side, and holding the
                    // lattice under the one chases the crossing down the other —
                    // Blue Ridge 1093 L in the replica grazed and met 1.1-1.5 m
                    // further out on every pass (9.7, 11.1, 12.4, 13.5 m) and the
                    // holds never converged in StageCarryHoldPasses. The tail's own
                    // crossing is exact (at or under the land), and in the
                    // replica, left alone, no tail came within the terrain audit's
                    // 3 cm of the lattice short of it.
                    float readTo = tailed ? kneeE : catchE;
                    float holdTo = eEnd;
                    gaps.Clear();
                    for (float e = eEnd; e < readTo; e += StageCarryReadPitchM) gaps.Add(new Vector2(e, Line(e) - Land(e)));
                    int inner = gaps.Count;
                    if (met)
                        while (inner > 0 && gaps[inner - 1].y < StageCatchHoldM && catchE - gaps[inner - 1].x <= StageCrossingAllowM)
                            inner--;
                    bool graze = false;
                    for (int k = 0; k < inner && !graze; k++)
                        graze = gaps[k].y < RoadsideRules.LatticeUnderMinM + StageGrazeSlackM;
                    if (graze) { holdTo = gaps[inner - 1].x; grazes++; }

                    if (catchE < czEnd + window)
                    {
                        int steep = 0, sustained = Mathf.RoundToInt(StageClearZoneSteepM / StageCarryReadPitchM);
                        for (int k = 0; catchE + k * StageCarryReadPitchM <= czEnd && steep < sustained; k++)
                        {
                            float e = catchE + k * StageCarryReadPitchM;
                            steep = (Land(e) - Land(e + window)) / window > RoadsideRules.SteepestRecoverableSlope + StageClearZoneNoise
                                ? steep + 1 : 0;
                        }
                        if (steep >= sustained) { holdTo = Mathf.Max(holdTo, czEnd + 0.5f); clearZones++; }
                    }
                    for (float e = eEnd + StageCarryReadPitchM; e <= holdTo; e += StageCarryHoldPitchM)
                    {
                        Vector3 q = at + outw * (half + e);
                        into.Add(new Vector3(q.x, Line(e) - StageCatchHoldM, q.z));
                    }
                }
            }
        }

        /// <summary>
        /// THE FILL BEHIND A STONE IS BELOW THE ROAD.
        ///
        /// A wall stands where the land falls away, and RoadsideDy grades the
        /// ground behind its stone as the fill it is: the shoulder's height
        /// under the footing, then StageFillBatter down to the land. But the
        /// lattice is 12 m cells, and where a warranted run ends into a cut a
        /// vertex a few metres along the road already stands on the cut's level
        /// land, and the facet from it lifts the ground 1.5 m behind a stone
        /// back up to road height over a fill that is a metre and more lower —
        /// Beech Gap wp 2167 L, "land -0.29 m from road height 1.5 m behind
        /// Track/Walls/WallColl (2.43 m out), on GroundN_2_-5", where the land
        /// there is 1.28 m under the tarmac. A car behind that stone would be
        /// standing on a lattice the plan never asked for.
        ///
        /// So behind every walled half-section — around where a car behind it
        /// would stand, RoadsideRules.PocketBehindM past the collider's back,
        /// give or take StageBehindHoldM — wherever the plan graded a FILL
        /// (the land below the bench), the lattice is held under that fill, and
        /// no deeper than the bench less a hide margin: the level a cut pins
        /// its hill down to behind a face, so a vertex shared with the level
        /// land beyond is not dug into a pit. Level land behind a stone (an
        /// approach rail over a bench, the padding of a run) is the land, and
        /// is left alone. Python replica: nine half-sections on three
        /// mountains, at most eight vertices a venue, lowered 0.09-0.21 m.
        /// Returns how many half-sections asked.
        /// </summary>
        static int CollectBehindWallHolds(List<Vector3> pts, List<Vector3> into)
        {
            int n = pts.Count, asked = 0;
            float half = RoadWidth * 0.5f;
            for (int i = 0; i < n; i++)
            {
                if ((hasTunnels && tunnelIn != null && i < tunnelIn.Length && tunnelIn[i]) || DeckCoversStation(i)) continue;
                for (int s = 0; s < 2; s++)
                {
                    if (rsKind[s][i] != Roadside.Walled || BuriedTerminal(s, i)) continue;
                    float side = s == 0 ? -1f : 1f;
                    Vector3 at = pts[i], outw = rsRight[i] * side;
                    float tarmac = at.y + RoadLift, faceE = rsWallE[s][i];
                    float standE = StageWallContactE(pts, i, s, side) + StageWallCollThick + RoadsideRules.PocketBehindM;
                    bool any = false;
                    for (float e = standE - StageBehindHoldM; e <= standE + StageBehindHoldM; e += StageCarryHoldPitchM)
                    {
                        Vector3 q = at + outw * (half + e);
                        float land = Mathf.Min(StageDemY(q.x, q.z) - tarmac, StageBenchDy);
                        float fill = Mathf.Max(ShoulderDy(faceE) - Mathf.Max(0f, e - faceE - StageWallBackFlatM) * StageFillBatter, land);
                        if (fill >= StageBenchDy - 0.005f) continue;
                        float target = tarmac + Mathf.Max(fill, StageBenchDy - RoadsideRules.HideMarginM);
                        // Only where the lattice stands over it: every critical
                        // fill's stone has a fill behind it, and all but a handful
                        // already have their lattice well down it — which is what
                        // the count in the log is for.
                        if (StageLatticeY(q.x, q.z) <= target) continue;
                        into.Add(new Vector3(q.x, target, q.z));
                        any = true;
                    }
                    if (any) asked++;
                }
            }
            return asked;
        }

        /// <summary>How far either side of where a car behind a stone would
        /// stand (<see cref="CollectBehindWallHolds"/>) the fill is held.</summary>
        const float StageBehindHoldM = 0.75f;

        /// <summary>
        /// The margin the lattice keeps under a section at <paramref name="e"/>:
        /// the full RoadsideRules.HideMarginM, except on a section that ENDS ON
        /// A SLOPE (the emitter's own test, ShoulderSlopedEnd) — a foreslope
        /// falling onto its catch, a backslope climbing to a toe or a graded
        /// crest — where it fades over StageHideFadeM the way RoadsideDy fades
        /// an open section's field, to <see cref="StageCatchHoldM"/> rather
        /// than to nothing: that is where the ribbon's toe is meant to cross
        /// the land, so the solve must not dig the land away from it. A flat
        /// end (a wall's face, a tube's wall) keeps the full margin to its last
        /// point, as the field does under it.
        /// </summary>
        static float StageLatticeHold(List<Vector2> prof, float e)
        {
            int m = prof.Count;
            if (m < 2) return RoadsideRules.HideMarginM;
            float eLast = prof[m - 1].x;
            float slope = (prof[m - 2].y - prof[m - 1].y) / Mathf.Max(eLast - prof[m - 2].x, 1e-5f);
            if (Mathf.Abs(slope) < ShoulderSlopedEnd) return RoadsideRules.HideMarginM;
            float keep = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(eLast - StageHideFadeM, eLast, e));
            return Mathf.Max(StageCatchHoldM, RoadsideRules.HideMarginM * keep);
        }

        /// <summary>
        /// Every point PrepareStageLattice holds the lattice under, with the
        /// height it may reach there: the tarmac and strip (at the tarmac's
        /// height) and each station's section along its own cross-section
        /// line out to its last point; then between each pair of stations the
        /// same, out to the shorter section. The designed surface only — never
        /// a toe, which is placed from the solved lattice. Nothing at a tunnel
        /// station.
        /// </summary>
        static void ForEachStageLatticeSample(List<Vector3> pts, Action<float, float, float> visit)
        {
            int n = pts.Count, last = Loop ? n : n - 1;
            float half = RoadWidth * 0.5f;
            int roadSteps = Mathf.CeilToInt((half + KerbWidth) / 1.5f);
            var right = new Vector3[n];
            for (int i = 0; i < n; i++) right[i] = RightAt(pts, i);
            bool Tube(int i) => hasTunnels && tunnelIn != null && i < tunnelIn.Length && tunnelIn[i];
            var across = new List<float>();

            // Each station's own line: what the terrain audit rays.
            for (int i = 0; i < n; i++)
            {
                if (Tube(i)) continue;
                float roadY = pts[i].y + RoadLift;
                for (int s = 0; s < 2; s++)
                {
                    Vector3 outw = right[i] * (s == 0 ? -1f : 1f);
                    for (int k = 0; k <= roadSteps; k++)
                    {
                        Vector3 p = pts[i] + outw * (half + (-half + (half + KerbWidth) * k / roadSteps));
                        visit(p.x, p.z, roadY - RoadsideRules.HideMarginM);
                    }
                    var prof = shoulderProfiles[s][i];
                    if (prof == null || prof.Count == 0) continue;
                    across.Clear();
                    AddLatticeAcross(across, prof, prof[prof.Count - 1].x, StageLatticeLinePitchM);
                    for (int k = 0; k < across.Count; k++)
                    {
                        float e = across[k];
                        // And every lattice crease the line crosses since the
                        // last sample, so the line is held exactly, not only
                        // at its quarter metres.
                        if (k > 0)
                            StageLatticeCreases(pts[i], outw, half, across[k - 1], e, prof, roadY, visit);
                        float dy = EvalShoulderProfile(prof, e, false);
                        if (float.IsNaN(dy)) continue;
                        Vector3 p = pts[i] + outw * (half + e);
                        visit(p.x, p.z, roadY + dy - StageLatticeHold(prof, e));
                    }
                }
            }

            // Between stations, a metre apart along the road.
            for (int i = 0; i < last; i++)
            {
                int a = i, b = Loop ? (i + 1) % n : i + 1;
                if (Tube(a) || Tube(b)) continue;
                float ya = pts[a].y + RoadLift, yb = pts[b].y + RoadLift;
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    var pa = shoulderProfiles[s][a];
                    var pb = shoulderProfiles[s][b];
                    across.Clear();
                    for (int k = 0; k <= roadSteps; k++)
                        across.Add(-half + (half + KerbWidth) * k / roadSteps);
                    int roadCount = across.Count;
                    if (pa != null && pb != null && pa.Count > 0 && pb.Count > 0)
                    {
                        float reach = Mathf.Min(pa[pa.Count - 1].x, pb[pb.Count - 1].x);
                        AddLatticeAcross(across, pa, reach, StageLatticeQuadPitchM);
                        AddLatticeAcross(across, pb, reach, StageLatticeQuadPitchM);
                    }
                    for (int k = 0; k < across.Count; k++)
                    {
                        float e = across[k];
                        float dyA = 0f, dyB = 0f, hold = RoadsideRules.HideMarginM;
                        if (k >= roadCount)
                        {
                            dyA = EvalShoulderProfile(pa, e, false);
                            dyB = EvalShoulderProfile(pb, e, false);
                            if (float.IsNaN(dyA) || float.IsNaN(dyB)) continue;
                            hold = Mathf.Min(StageLatticeHold(pa, e), StageLatticeHold(pb, e));
                        }
                        Vector3 pA = pts[a] + right[a] * (side * (half + e));
                        Vector3 pB = pts[b] + right[b] * (side * (half + e));
                        for (int q = 1; q < StageLatticeAlong; q++)
                        {
                            float t = q / (float)StageLatticeAlong;
                            visit(Mathf.Lerp(pA.x, pB.x, t), Mathf.Lerp(pA.z, pB.z, t),
                                  Mathf.Lerp(ya + dyA, yb + dyB, t) - hold);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The creases of the near lattice that a station's line crosses
        /// between two of its samples (<paramref name="e0"/>,
        /// <paramref name="e1"/>), visited as samples themselves.
        ///
        /// GridChunkMesh splits each 12 m cell on its a-c diagonal, so the
        /// plane under a point changes only where x or z is a multiple of
        /// NearCell or x - z is. Between two creases the lattice is one plane,
        /// and between two consecutive samples the section is one line
        /// (AddLatticeAcross puts a sample on every profile point), so the gap
        /// between them is least at a sample or at a crease. Held only at the
        /// quarter-metre samples, a ridge crease between two of them stands up
        /// to an eighth of a metre times the change of slope across it closer
        /// than either — about 3 cm where a dig vertex meets a hillside one,
        /// which under a catch's <see cref="StageCatchHoldM"/> is the terrain
        /// audit's whole margin.
        /// </summary>
        static void StageLatticeCreases(Vector3 at, Vector3 outw, float half, float e0, float e1,
                                        List<Vector2> prof, float roadY, Action<float, float, float> visit)
        {
            if (e1 <= e0) return;
            Vector3 p0 = at + outw * (half + e0), p1 = at + outw * (half + e1);
            for (int family = 0; family < 3; family++)
            {
                // x / cell, z / cell, then (x - z) / cell: integer on a crease.
                float f0 = (family == 0 ? p0.x : family == 1 ? p0.z : p0.x - p0.z) / NearCell;
                float f1 = (family == 0 ? p1.x : family == 1 ? p1.z : p1.x - p1.z) / NearCell;
                if (Mathf.Abs(f1 - f0) < 1e-6f) continue;
                int lo = Mathf.FloorToInt(Mathf.Min(f0, f1)) + 1, hi = Mathf.CeilToInt(Mathf.Max(f0, f1)) - 1;
                for (int c = lo; c <= hi; c++)
                {
                    float e = Mathf.Lerp(e0, e1, (c - f0) / (f1 - f0));
                    float dy = EvalShoulderProfile(prof, e, false);
                    if (float.IsNaN(dy)) continue;
                    Vector3 p = at + outw * (half + e);
                    visit(p.x, p.z, roadY + dy - StageLatticeHold(prof, e));
                }
            }
        }

        /// <summary>Every point of a profile out to <paramref name="reach"/>,
        /// enough between them that none is more than
        /// <paramref name="pitch"/> from the next, and the reach itself.</summary>
        static void AddLatticeAcross(List<float> into, List<Vector2> p, float reach, float pitch)
        {
            float lastAdded = float.NegativeInfinity;
            for (int k = 0; k < p.Count; k++)
            {
                float e0 = p[k].x;
                if (e0 > reach) break;
                into.Add(lastAdded = e0);
                float e1 = k + 1 < p.Count ? Mathf.Min(p[k + 1].x, reach) : e0;
                int parts = Mathf.CeilToInt((e1 - e0) / pitch);
                for (int q = 1; q < parts; q++) into.Add(lastAdded = e0 + (e1 - e0) * q / parts);
            }
            if (reach > lastAdded + 1e-3f && p.Count > 0 && reach <= p[p.Count - 1].x) into.Add(reach);
        }

        // ------------------------------------------------------------------
        //  The roadside plan
        // ------------------------------------------------------------------
        // HOW THE ROAD MEETS THE MOUNTAIN, DECIDED ONCE.
        //
        // The owner, 2026-09-13: "Roads sitting cm above the ground do not need
        // rails/walls, they should meet the ground properly by DOT standards."
        // What the stage had instead, measured on the built scenes that day:
        //
        //   * the shoulder was a 0.46 m slab face at 61 degrees on every
        //     unwalled station (a collider, and wall class to the car);
        //   * walls stood on LEVEL land wherever the DEM stayed within 0.9 m
        //     of the road — the "open verge" rule a shoulder-width audit had
        //     pushed onto 9.2 km of mountain — so about half of every
        //     mountain's walled half-sections had drivable ground behind
        //     stone drawn from one side only;
        //   * every cut bank hid a flat bench out to 16 m, and ended in a
        //     collider 3 cm over the tarmac: passable outward, a ledge back;
        //   * a deck began at bridge blend 0.001 and its parapet at 0.35,
        //     leaving ~7.75 m of unrailed deck edge at each affected span end.
        //
        // So each shoulder is a SECTION, per station and side, and the walls,
        // the cut faces, the ground lattice, the posts, the forest and the
        // shoulder ribbon all read this one plan:
        //
        //   Open    shoulder, 1V:6H through the clear zone, 1V:4H to the reach,
        //           1V:3H for a short runout, until it meets the land. No wall.
        //   Walled  a WARRANTED wall: every deck station, ApproachRailStations
        //           past each deck end, a fill the Open section cannot catch,
        //           water, a tunnel mouth. The shoulder runs to its face;
        //           at a run's buried terminal the open section runs past it.
        //   Cut     the land rises a face's height: the compact cut to the
        //           face's toe, the face, a solid rock top back into the hill.
        //   Deck    the deck is the surface, and it is walled.
        //   Tunnel  the tube's wall is the wall.

        enum Roadside : byte { Open, Walled, Cut, Deck, Tunnel }

        /// <summary>The plan, per [side][station]; side 0 is left (-1),
        /// 1 is right (+1).</summary>
        static Roadside[][] rsKind;
        /// <summary>Open: where the graded section meets the land, in metres
        /// past the tarmac edge.</summary>
        static float[][] rsCatchE;
        /// <summary>Walled: e of the wall's collider face — WallFaceE, or
        /// further out through an end flare.</summary>
        static float[][] rsWallE;
        /// <summary>Walled: how much of the stone stands. 1 is full height, 0
        /// buried into the shoulder at a run's terminal.</summary>
        static float[][] rsWallUp;
        /// <summary>Cut: drawn face height over the waypoint plane, smoothed
        /// and tapered at the run ends that meet land.</summary>
        static float[][] rsFaceH;
        /// <summary>Cut: how far the lattice behind the face is released, 0 at
        /// a run end to 1 BankReleaseFadeStations in.</summary>
        static float[][] rsRelease;
        /// <summary>Cut: GRADED rather than faced — the ditch's backslope
        /// carries on at RoadsideRules.BackSlope up to the crest
        /// (<see cref="CutCrestE"/>), and no rock stands vertical. Decided by
        /// FinishStageCuts once the face heights have been landed on the hill
        /// behind them.</summary>
        static bool[][] rsCutGraded;
        /// <summary>Cut: this station's release fades toward a run end that
        /// runs INTO something — a warranted wall or a tunnel portal — rather
        /// than tapering into graded land, so its rock top holds the crest
        /// instead of stepping down onto a lattice that is not released yet.
        /// </summary>
        static bool[][] rsCutHold;
        static Vector3[] rsRight;
        static List<(int from, int len, float side)> rsWallRuns;
        static List<(int[] stations, float side)> rsBankRuns;
        /// <summary>The rock top laid over each Cut station — (e, world y)
        /// outward from the crest — keyed side * RsStationKey + station. The
        /// forest stands its trees on it.</summary>
        static Dictionary<int, Vector2[]> rsBankTop;
        const int RsStationKey = 1 << 20;

        static void ClearStageRoadside()
        {
            rsKind = null;
            rsCatchE = rsWallE = rsWallUp = rsFaceH = rsRelease = null;
            rsCutGraded = rsCutHold = null;
            rsRight = null;
            rsWallRuns = null; rsBankRuns = null; rsBankTop = null;
        }

        static void EnsureStageRoadside(List<Vector3> pts)
        {
            if (rsKind == null || rsKind[0].Length != pts.Count) PlanStageRoadside(pts);
        }

        static int SideIx(float side) => side < 0f ? 0 : 1;

        /// <summary>
        /// A warranted run's BURIED TERMINAL: a station whose stone is going
        /// down into the ground at a run end beside graded land (rsWallUp
        /// under 1). Its section is the OPEN one, not the walled shoulder, and
        /// the stone stands in that foreslope.
        ///
        /// Graded as a walled shoulder, a terminal was the one place the
        /// walled section's shortcuts showed. That section ends at the wall
        /// face in a skirt and leaves everything behind it to the stone — the
        /// 12 m lattice under a fill runs well below its own field there — and
        /// at a terminal the stone is sinking out of the way. Python replica
        /// of the plan, the lattice and the shoulder emitter's toe: the ribbon
        /// ended 0.57-0.73 m (p50) over the lattice at every terminal of Blue
        /// Ridge, Blowing Rock and Mount Mitchell, 100% of them deeper than
        /// 0.2 m — a drop off the end of the shoulder where the stone no
        /// longer guards it, and a face a car could not climb back up. Graded
        /// open, the foreslope is carried down to the lattice as everywhere
        /// else: p50 0.00-0.05 m, 5-10% deeper than 0.2 m.
        /// </summary>
        static bool BuriedTerminal(int s, int st) =>
            rsKind[s][st] == Roadside.Walled && rsWallUp[s][st] < 0.999f;

        /// <summary>The section a station and side is GRADED to — the ground
        /// field and the shoulder profile ask this, the walls and faces the
        /// plan's own kind: a buried terminal is walled and graded open.</summary>
        static Roadside GradedKind(int s, int st) =>
            BuriedTerminal(s, st) ? Roadside.Open : rsKind[s][st];

        // ---- the sections, tarmac-relative, e metres past the tarmac edge ----
        static float ShoulderEndE => KerbWidth + StageShoulderM;
        /// <summary>A warranted wall's collider face, un-flared.</summary>
        static float WallFaceE => StageVerge - StageWallFaceIn;
        /// <summary>End of the Safety Edge bevel off the strip.</summary>
        static float BevelEndE => KerbWidth + SafetyEdgeRunM;
        /// <summary>The shoulder: the strip's top, the bevel down the owner's
        /// inch, then the cross-fall.</summary>
        static float ShoulderDy(float e)
        {
            if (e <= KerbWidth) return KerbStripLift;
            if (e <= BevelEndE)
                return KerbStripLift - RoadsideRules.EdgeDropM * Mathf.InverseLerp(KerbWidth, BevelEndE, e);
            return KerbStripLift - RoadsideRules.EdgeDropM - (e - BevelEndE) * RoadsideRules.ShoulderCrossFall;
        }
        static float ShoulderEndDy => ShoulderDy(ShoulderEndE);
        static float CutDitchDy => ShoulderEndDy - CutForeslopeRunM * RoadsideRules.SteepestRecoverableSlope;
        /// <summary>The rock face's toe: the ditch's backslope back up to road
        /// level, about 3 m past the tarmac edge.</summary>
        static float CutToeE => ShoulderEndE + CutForeslopeRunM + CutDitchFloorM
                                - CutDitchDy / RoadsideRules.BackSlope;

        /// <summary>The open section: shoulder, RecoverableSlope for the clear
        /// zone, SteepestRecoverableSlope to the warrant reach, TraversableSlope
        /// beyond. Continues past any catch, so the lattice's Max against the
        /// land is the section "filled up to, never below".</summary>
        static float OpenSectionDy(float e)
        {
            float e0 = ShoulderEndE;
            if (e <= e0) return ShoulderDy(e);
            float run = e - e0, cz = RoadsideRules.ClearZoneM, reach = RoadsideRules.WarrantReachM;
            float y = ShoulderEndDy - Mathf.Min(run, cz) * RoadsideRules.RecoverableSlope;
            if (run > cz) y -= (Mathf.Min(run, reach) - cz) * RoadsideRules.SteepestRecoverableSlope;
            if (run > reach) y -= (run - reach) * RoadsideRules.TraversableSlope;
            return y;
        }

        /// <summary>The compact cut: shoulder, foreslope, ditch, backslope to
        /// the toe, level after it (the face stands there).</summary>
        static float CutSectionDy(float e)
        {
            float e0 = ShoulderEndE;
            if (e <= e0) return ShoulderDy(e);
            float e1 = e0 + CutForeslopeRunM;
            if (e <= e1) return ShoulderEndDy - (e - e0) * RoadsideRules.SteepestRecoverableSlope;
            float e2 = e1 + CutDitchFloorM;
            if (e <= e2) return CutDitchDy;
            return Mathf.Min(0f, CutDitchDy + (e - e2) * RoadsideRules.BackSlope);
        }

        /// <summary>How far a Cut station's crest stands over the tarmac: the
        /// top of its rock face or, where the cut is graded, where its
        /// backslope stops climbing. Never under the toe, which is at road
        /// level: the ditch's backslope always climbs that far, and the field
        /// under it is held to the section.</summary>
        static float CutRiseDy(int s, int st) => Mathf.Max(0f, rsFaceH[s][st] - RoadLift);

        /// <summary>e of a Cut station's crest: past a face's vertical plinth by
        /// its batter, or past a graded cut's toe by the run a
        /// RoadsideRules.BackSlope takes to climb its rise.</summary>
        static float CutCrestE(int s, int st) =>
            rsCutGraded != null && rsCutGraded[s][st]
                ? CutToeE + CutRiseDy(s, st) / RoadsideRules.BackSlope
                : CutToeE + Mathf.Max(0f, rsFaceH[s][st] - BankPlinth) * BankBatter;

        /// <summary>The land an open section has to meet at e, tarmac-relative:
        /// the DEM along the station's right vector, no higher than the bench.
        /// </summary>
        static float RoadsideLandDy(List<Vector3> pts, int i, float side, float e)
        {
            Vector3 p = pts[i] + rsRight[i] * (side * (RoadWidth * 0.5f + e));
            return Mathf.Min(StageDemY(p.x, p.z) - (pts[i].y + RoadLift), StageBenchDy);
        }

        const float CatchStepM = 0.25f;

        /// <summary>
        /// THE ONE HELPER BOTH THE PROFILE AND THE WARRANT ASK. Walk the open
        /// section out from the shoulder until it meets the land; the catch is
        /// where the ribbon ends. True — CRITICAL, the side is walled — when
        /// the land at the warrant reach is already a RoadsideRules
        /// IsCriticalFall below the shoulder, or when even the 1V:3H runout
        /// has not met it <see cref="StageTraversableRunM"/> past the reach.
        /// </summary>
        static bool OpenSideCritical(List<Vector3> pts, int i, float side, out float catchE)
        {
            float e0 = ShoulderEndE;
            float reachE = e0 + RoadsideRules.WarrantReachM;
            float lastE = reachE + StageTraversableRunM;
            // The land is never above the bench, so at the shoulder the gap is
            // always positive and the first crossing is a real catch.
            float prevE = e0, prevGap = OpenSectionDy(e0) - RoadsideLandDy(pts, i, side, e0);
            for (int k = 1; ; k++)
            {
                float e = Mathf.Min(e0 + k * CatchStepM, lastE);
                float gap = OpenSectionDy(e) - RoadsideLandDy(pts, i, side, e);
                if (gap <= 0f)
                {
                    catchE = Mathf.Lerp(prevE, e, prevGap / (prevGap - gap));
                    return false;
                }
                if (prevE < reachE && e >= reachE - 1e-3f
                    && RoadsideRules.IsCriticalFall(ShoulderEndDy - RoadsideLandDy(pts, i, side, reachE),
                                                    RoadsideRules.WarrantReachM))
                {
                    catchE = reachE;
                    return true;
                }
                if (e >= lastE) { catchE = lastE; return true; }
                prevE = e; prevGap = gap;
            }
        }

        /// <summary>Open water deeper than StageWaterDepthM within
        /// StageWaterReachM of the tarmac edge — the coast's warrant. The
        /// bake holds land 0.4 m over the sea plane and the seabed 4 m under
        /// it, so the DEM answers this without the surface mask.</summary>
        static bool WaterBeside(List<Vector3> pts, int i, float side)
        {
            if (track.stageWaterY <= 0f) return false;
            for (float e = 0f; e <= StageWaterReachM + 1e-3f; e += 1f)
            {
                Vector3 p = pts[i] + rsRight[i] * (side * (RoadWidth * 0.5f + e));
                if (StageDemY(p.x, p.z) < track.stageWaterY - StageWaterDepthM) return true;
            }
            return false;
        }

        /// <summary>Maximal runs of wanted stations with no hysteresis — a
        /// graded gap between two warranted runs stays open. Loop-aware the
        /// way StationRuns is.</summary>
        static List<(int from, int len)> MaximalRuns(bool[] want, int minLen)
        {
            int n = want.Length;
            var runs = new List<(int from, int len)>();
            int origin = 0;
            if (Loop)
            {
                while (origin < n && want[origin]) origin++;
                if (origin >= n) { if (n >= minLen) runs.Add((0, n)); return runs; }
            }
            for (int k = 0; k < n; )
            {
                int i = (origin + k) % n;
                if (!want[i]) { k++; continue; }
                int len = 1;
                while (k + len < n && want[(origin + k + len) % n]) len++;
                if (len >= minLen) runs.Add((i, len));
                k += len;
            }
            return runs;
        }

        static bool[] RunMask(List<(int from, int len)> runs, int n)
        {
            var mask = new bool[n];
            foreach (var run in runs)
                for (int k = 0; k < run.len; k++) mask[WrapIdx(run.from + k, n)] = true;
            return mask;
        }

        /// <summary>The station a run ends on (end 0 its first, 1 its last)
        /// and the station just past it. False at the end of a route with
        /// ends, and for a run that is the whole loop.</summary>
        static bool RunEnd((int from, int len) run, int end, int n, out int endSt, out int beyond)
        {
            endSt = end == 0 ? run.from : WrapIdx(run.from + run.len - 1, n);
            beyond = endSt;
            if (run.len >= n) return false;
            int b = end == 0 ? run.from - 1 : run.from + run.len;
            if (!Loop && (b < 0 || b >= n)) return false;
            beyond = WrapIdx(b, n);
            return true;
        }

        /// <summary>
        /// How tall a cut face would be here, or 0 where the land does not rise.
        ///
        /// THE TOP OF A CUT LANDS ON THE HILLSIDE. That is the whole shape of
        /// the thing: you take a bite out of a slope, and the face is exactly
        /// as tall as the slope is where the face stops. Sizing it off a rise
        /// measured at some fixed distance instead gives a wall standing a
        /// metre proud of a hill that is not there yet — a lip along the top,
        /// visible from the road, on every gentle gradient.
        ///
        /// So it is solved rather than sampled: h is the height at which the
        /// top of a battered face of height h meets the real DEM, a metre past
        /// the top so the cut bites into the hill. The iteration converges from
        /// below in three or four passes because the DEM rises with distance
        /// and the batter is shallower than 1:1; six is free and covers the
        /// odd bench. The bottom BankPlinth of the face carries the top no
        /// further out, because that part of it is vertical.
        ///
        /// Pure terrain: whether a face is BUILT here (not on a deck, a tube,
        /// or a station a wall has) is the plan's business, not this one's.
        /// </summary>
        static float StageBankSolve(List<Vector3> pts, int i, float side)
        {
            Vector3 right = rsRight[i];
            float h = 0f;
            for (int it = 0; it < 6; it++)
            {
                float probe = RoadWidth * 0.5f + CutToeE + Mathf.Max(0f, h - BankPlinth) * BankBatter + 1f;
                float px = pts[i].x + right.x * side * probe;
                float pz = pts[i].z + right.z * side * probe;
                h = Mathf.Clamp(StageDemY(px, pz) - pts[i].y, 0f, BankMaxH);
                if (h <= 0f) return 0f;
            }
            return h;
        }

        /// <summary>How many times the plan is laid and then walked against
        /// the section it will build (<see cref="BuiltSectionCritical"/>). A
        /// walk that finds a fall walls it, flare and all, and the plan is laid
        /// again; a new wall grades the ground behind it differently, which
        /// can move a neighbour's lattice, so the next walk looks again — but
        /// only near what changed.</summary>
        const int StageWarrantPasses = 3;
        /// <summary>How many times the plan is laid again for wall run ends
        /// whose cut FinishStageCuts graded where they met it (softRockBy). Each laying
        /// can only turn more ends soft, never fewer; one is what the five
        /// mountains need (Blue Ridge wp 100 L), the rest is headroom.</summary>
        const int StageSoftRockRelays = 3;
        /// <summary>Stations either side of a station whose section changed
        /// that the next walk reads again: a lattice cell is three stations,
        /// and a vertex weighs on points a cell diagonal away.</summary>
        const int StageWarrantNearStations = 5;
        /// <summary>
        /// Fall slack on the plan's walk (RoadsideRules.WorstCriticalFall).
        /// The plan reads the lattice before a shoulder exists, and
        /// PrepareStageLattice, which runs once one does, only ever LOWERS
        /// vertices under the ribbon; the stage chunks are also quantised to a
        /// few millimetres. Five centimetres of fall is what the builder
        /// concedes, so that where it stops a wall the audit — walking the
        /// built meshes with no slack — agrees.
        /// </summary>
        const float StageWarrantSlackM = 0.05f;
        /// <summary>The walk's pitch across the section — the edge audit's own
        /// 5 cm, so the pairs it can choose are the pairs the audit chooses.
        /// The lattice is read exactly at every one of them, off vertices
        /// cached for the laying (BuiltSectionCritical).</summary>
        const float StageWarrantPitchM = 0.05f;

        /// <summary>
        /// CONTRACT C3. Decide, per station and side, what the roadside is —
        /// warranted walls (parapets and approach runs included) with their
        /// flared, buried ends; cut faces with their overlap and tapers; the
        /// open section's catch — into the rs* tables that BuildStageWalls,
        /// BuildStageBanks, PlaceStagePosts, StageGroundHeightAt,
        /// StageEdgeProfile and the forest read. Called from Build() for a
        /// stage after PlanFuelStop and before BuildRoad.
        ///
        /// Laid from bridgeBlend, the tunnel table and the raw DEM. Then — and
        /// only then, once the tables exist to grade it — it reads the ground
        /// it grades (StageLatticeY) twice over: it walks every open section
        /// against the fall warrant exactly as the edge audit will
        /// (<see cref="BuiltSectionCritical"/>), walling what that finds, and it
        /// lands every cut's crest on the hill behind it, grading the ones too
        /// low to be a face (<see cref="FinishStageCuts"/>).
        /// </summary>
        internal static void PlanStageRoadside(List<Vector3> pts)
        {
            ClearStageRoadside();
            if (!stageDemLoaded || pts == null || pts.Count < 2) return;
            int n = pts.Count;
            var rightAt = new Vector3[n];
            for (int i = 0; i < n; i++) rightAt[i] = RightAt(pts, i);
            rsRight = rightAt;

            // ONE SPAN TEST for the deck, the parapet, the bank cut-off and
            // the posts: the deck's own (BuildBridges builds a deck wherever
            // blend > 0.001). The parapet used to wait for 0.35.
            var deck = new bool[n];
            var tube = new bool[n];
            for (int i = 0; i < n; i++)
            {
                deck[i] = bridgeBlend != null && i < bridgeBlend.Length && bridgeBlend[i] > DeckBlendMin;
                tube[i] = hasTunnels && tunnelIn != null && i < tunnelIn.Length && tunnelIn[i];
            }
            // A coast has no rock to cut: its sections are shoulder and
            // foreslope to the sand, which is also what removes the 104 m of
            // cut face Emerald Isle's dunes used to get.
            bool banksOn = theme.stageBanks && track.stageWaterY <= 0f;
            var deckRuns = MaximalRuns(deck, 1);
            var tubeRuns = MaximalRuns(tube, 1);

            // THE TERRAIN, READ ONCE: the face solve, the DEM's own warrant,
            // water and the open catch depend on nothing the plan decides, and
            // the plan may be laid more than once below.
            var riseBy = new float[2][];
            var cutBy = new bool[2][];
            var demCriticalBy = new bool[2][];
            var waterBy = new bool[2][];
            var catchBy = new float[2][];
            for (int si = 0; si < 2; si++)
            {
                float sd = si == 0 ? -1f : 1f;
                riseBy[si] = new float[n]; cutBy[si] = new bool[n]; demCriticalBy[si] = new bool[n];
                waterBy[si] = new bool[n]; catchBy[si] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    if (deck[i] || tube[i]) continue;
                    if (banksOn)
                    {
                        riseBy[si][i] = StageBankSolve(pts, i, sd);
                        cutBy[si][i] = riseBy[si][i] >= BankRiseM;
                    }
                    demCriticalBy[si][i] = OpenSideCritical(pts, i, sd, out catchBy[si][i]);
                    waterBy[si][i] = WaterBeside(pts, i, sd);
                }
            }
            // Stations the walk over the built section found critical that the
            // DEM's own test did not (BuiltSectionCritical), per side.
            var builtCriticalBy = new[] { new bool[n], new bool[n] };
            // Cut stations a warranted wall run ended beside whose cut
            // FinishStageCuts graded near the hand-over — no rock to flare the
            // stone into — per side: the run ends there as it would beside
            // graded land (a buried terminal), and the cut does not overlap it.
            var softRockBy = new[] { new bool[n], new bool[n] };

            // walled metres by warrant, indexed like `reason`
            var metresBy = new float[8];
            float openM = 0f, cutM = 0f;
            int flaredOpen = 0, flaredRock = 0;
            var catches = new List<float>();

            // One laying of the plan, from the terrain above and whatever the
            // walk below has found so far.
            void LayPlan()
            {
                rsKind = new Roadside[2][];
                rsCatchE = new float[2][]; rsWallE = new float[2][]; rsWallUp = new float[2][];
                rsFaceH = new float[2][]; rsRelease = new float[2][]; rsCutHold = new bool[2][];
                rsCutGraded = null;
                rsWallRuns = new List<(int from, int len, float side)>();
                rsBankRuns = new List<(int[] stations, float side)>();
                rsBankTop = new Dictionary<int, Vector2[]>();
                Array.Clear(metresBy, 0, metresBy.Length);
                openM = cutM = 0f;
                flaredOpen = flaredRock = 0;
                catches.Clear();

                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    var rise = riseBy[s];
                    var cut = cutBy[s];
                    var water = waterBy[s];
                    var catchE = catchBy[s];
                    var builtCritical = builtCriticalBy[s];
                    var critical = new bool[n];
                    for (int i = 0; i < n; i++) critical[i] = demCriticalBy[s][i] || builtCritical[i];

                    // Where a face WOULD stand with no wall in front of it: what the
                    // wall pass needs to know which of its run ends lead into rock.
                    var tentBank = RunMask(StationRuns(cut, 4), n);

                    // THE WARRANT. reason: 1 deck, 2 critical fill, 3 water,
                    // 4 approach, 5 tunnel mouth, 6 the theme walls everything,
                    // 7 a critical fall only the walk over the built section found.
                    var reason = new byte[n];
                    void Want(int j, byte why) { if (!tube[j] && reason[j] == 0) reason[j] = why; }
                    for (int i = 0; i < n; i++)
                    {
                        if (deck[i]) reason[i] = 1;
                        else if (theme.stageWallAlways) Want(i, 6);
                    }
                    // A fill grading cannot catch, and water: the station and
                    // EndFlareStations either side, so every critical station has
                    // full-height stone and a run's flared, buried terminals lie
                    // OUTSIDE the critical length, over land the section grades. A
                    // station the DEM calls a cut is left to its face — unless the
                    // walk over what will actually be built there found the fall.
                    for (int i = 0; i < n; i++)
                    {
                        if (!(critical[i] || water[i]) || (cut[i] && !builtCritical[i])) continue;
                        byte why = demCriticalBy[s][i] ? (byte)2 : builtCritical[i] ? (byte)7 : (byte)3;
                        for (int o = -RoadsideRules.EndFlareStations; o <= RoadsideRules.EndFlareStations; o++)
                        {
                            int j = i + o;
                            if (!Loop && (j < 0 || j >= n)) continue;
                            Want(WrapIdx(j, n), why);
                        }
                    }
                    // Past each deck end, onto the approach: "all sections of
                    // bridges should have walls", and the abutment is where a car
                    // gets outboard of a parapet.
                    foreach (var run in deckRuns)
                        for (int dir = -1; dir <= 1; dir += 2)
                            for (int o = 1; o <= RoadsideRules.ApproachRailStations; o++)
                            {
                                int j = dir < 0 ? run.from - o : run.from + run.len - 1 + o;
                                if (!Loop && (j < 0 || j >= n)) break;
                                j = WrapIdx(j, n);
                                if (tube[j]) break;
                                Want(j, 4);
                            }
                    // Beside a tunnel mouth, wherever no rock face runs into it.
                    foreach (var run in tubeRuns)
                        for (int dir = -1; dir <= 1; dir += 2)
                            for (int o = 1; o <= PortalGuardStations; o++)
                            {
                                int j = dir < 0 ? run.from - o : run.from + run.len - 1 + o;
                                if (!Loop && (j < 0 || j >= n)) break;
                                j = WrapIdx(j, n);
                                if (tube[j]) break;
                                if (!tentBank[j]) Want(j, 5);
                            }

                    var want = new bool[n];
                    for (int i = 0; i < n; i++) want[i] = reason[i] != 0;
                    // No hysteresis: a gap between two warranted runs is graded
                    // land, and under the owner's rule graded land is left open.
                    // A run into a portal is kept however short — the portal face
                    // closes its other end.
                    var wallRuns = new List<(int from, int len)>();
                    foreach (var run in MaximalRuns(want, 1))
                    {
                        bool portal = (RunEnd(run, 0, n, out _, out int b0) && tube[b0])
                                   || (RunEnd(run, 1, n, out _, out int b1) && tube[b1]);
                        if (run.len >= MinWallRunStations || portal) wallRuns.Add(run);
                    }
                    var wall = RunMask(wallRuns, n);

                    // WALL AND FACE OVERLAP BY A STATION where one hands over to the
                    // other, so there is no station with neither — unless an
                    // earlier laying found that cut graded where the wall meets it
                    // (softRockBy).
                    var softRock = softRockBy[s];
                    var overlap = new bool[n];
                    foreach (var run in wallRuns)
                        for (int end = 0; end < 2; end++)
                            if (RunEnd(run, end, n, out int endSt, out int beyond) && !tube[beyond] && tentBank[beyond]
                                && !softRock[beyond])
                                overlap[endSt] = true;

                    // Cut faces defer to BUILT walls (not wanted ones: a wanted
                    // station in a run too short to build used to take the face
                    // away and give nothing back).
                    var faceWant = new bool[n];
                    for (int i = 0; i < n; i++)
                        faceWant[i] = !deck[i] && !tube[i] && ((cut[i] && !wall[i]) || overlap[i]);
                    var bankRuns = new List<int[]>();
                    var cur = new List<int>();
                    foreach (var run in StationRuns(faceWant, 4))
                    {
                        // StationRuns bridges two-station holes, which must not
                        // carry a face through a wall it defers to.
                        cur.Clear();
                        for (int k = 0; k <= run.len; k++)
                        {
                            int st = k < run.len ? WrapIdx(run.from + k, n) : -1;
                            if (st >= 0 && !(wall[st] && !overlap[st]) && !deck[st] && !tube[st]) { cur.Add(st); continue; }
                            if (cur.Count >= 4) bankRuns.Add(cur.ToArray());
                            cur.Clear();
                        }
                    }
                    var banked = new bool[n];
                    foreach (var r in bankRuns) foreach (int st in r) banked[st] = true;

                    // Face heights: smooth the solved rise along the road (the DEM
                    // is 30 m posts read bilinearly, which saws at the scale a 4 m
                    // ring samples it), then put a wobble BACK, because a smoothed
                    // top edge is a milled one — two incommensurate periods, 80 m
                    // and 33 m, deterministic in the station index.
                    var faceH = new float[n];
                    var release = new float[n];
                    var hold = new bool[n];
                    foreach (var run in bankRuns)
                    {
                        int L = run.Length;
                        // A face runs full height into a wall that overlaps it or a
                        // portal; anywhere else it tapers into the land over four
                        // stations, and its release fades in from the end.
                        bool taper0 = !(RunEnd((run[0], L), 0, n, out _, out int b0) && (wall[b0] || tube[b0]));
                        bool taper1 = !(RunEnd((run[0], L), 1, n, out _, out int b1) && (wall[b1] || tube[b1]));
                        for (int k = 0; k < L; k++)
                        {
                            int i = run[k];
                            float sum = 0f;
                            for (int o = -3; o <= 3; o++)
                            {
                                int j = WrapIdx(i + o, n);
                                if (cut[j]) sum += rise[j];
                            }
                            float sm = sum / 7f * (1f + 0.11f * Mathf.Sin(i * 0.37f + 1.1f)
                                                      + 0.06f * Mathf.Sin(i * 0.91f));
                            float t0 = taper0 ? Mathf.InverseLerp(-0.5f, 3.5f, k) : 1f;
                            float t1 = taper1 ? Mathf.InverseLerp(-0.5f, 3.5f, L - 1 - k) : 1f;
                            faceH[i] = sm * Mathf.SmoothStep(0f, 1f, Mathf.Min(t0, t1));
                            release[i] = Mathf.SmoothStep(0f, 1f,
                                Mathf.Min(k, L - 1 - k) / (float)BankReleaseFadeStations);
                            // Which end the release fades toward: one that runs into
                            // a wall or a portal keeps its face, so its top must
                            // hold that height (rsCutHold); one that tapers into
                            // land lowers its face onto what the top can reach.
                            int toLand = Mathf.Min(taper0 ? k : int.MaxValue, taper1 ? L - 1 - k : int.MaxValue);
                            int toStop = Mathf.Min(taper0 ? int.MaxValue : k, taper1 ? int.MaxValue : L - 1 - k);
                            hold[i] = toStop < toLand && toStop < BankReleaseFadeStations;
                        }
                        rsBankRuns.Add((run, side));
                        cutM += L * Spacing;
                    }

                    // WALL ENDS. A run that ends beside graded land flares away
                    // from the road at EndFlareRatio over its last EndFlareStations
                    // chords and is buried into the foreslope, the way a guardrail
                    // terminal is: a car running along the shoulder meets a ramp of
                    // stone angled away from it, not a square end, and the ground
                    // round the sinking stone is the open section (BuriedTerminal),
                    // not a shoulder that stops at it. One that ends at
                    // a rock face flares all the way out to the face's toe line —
                    // steeper where the run is short — so the ditch in front of the
                    // face leads into the wall's face rather than its end. Into a
                    // portal, or at the end of the route, it is carried square.
                    var wallE = new float[n];
                    var wallUp = new float[n];
                    for (int i = 0; i < n; i++) { wallE[i] = WallFaceE; wallUp[i] = 1f; }
                    foreach (var run in wallRuns)
                    {
                        for (int end = 0; end < 2; end++)
                        {
                            if (!RunEnd(run, end, n, out int endSt, out int beyond) || tube[beyond]) continue;
                            bool rock = banked[beyond] && !softRock[beyond];
                            int stations;
                            float offset;
                            if (rock)
                            {
                                offset = CutToeE - StageWallFaceIn - WallFaceE;
                                stations = Mathf.Min(Mathf.CeilToInt(offset / (RoadsideRules.EndFlareRatio * Spacing)),
                                                     run.len / 2);
                                flaredRock++;
                            }
                            else
                            {
                                stations = Mathf.Min(RoadsideRules.EndFlareStations, run.len / 2);
                                offset = stations * RoadsideRules.EndFlareRatio * Spacing;
                                flaredOpen++;
                            }
                            if (stations <= 0) continue;
                            for (int j = 0; j <= stations; j++)
                            {
                                int st = WrapIdx(endSt + (end == 0 ? j : -j), n);
                                if (deck[st]) break;
                                float frac = (stations - j) / (float)stations;   // 1 on the end station
                                wallE[st] = Mathf.Max(wallE[st], WallFaceE + offset * frac);
                                if (!rock) wallUp[st] = Mathf.Min(wallUp[st], 1f - frac);
                            }
                        }
                        rsWallRuns.Add((run.from, run.len, side));
                    }

                    var kind = new Roadside[n];
                    for (int i = 0; i < n; i++)
                    {
                        kind[i] = tube[i] ? Roadside.Tunnel
                                : deck[i] ? Roadside.Deck
                                : wall[i] ? Roadside.Walled
                                : banked[i] ? Roadside.Cut
                                : Roadside.Open;
                        if (wall[i]) metresBy[reason[i]] += Spacing;
                        if (kind[i] == Roadside.Open) { openM += Spacing; catches.Add(catchE[i]); }
                    }
                    rsKind[s] = kind; rsCatchE[s] = catchE; rsWallE[s] = wallE; rsWallUp[s] = wallUp;
                    rsFaceH[s] = faceH; rsRelease[s] = release; rsCutHold[s] = hold;
                }
            }

            // LAY, WALK, LAY AGAIN. The plan's own fall test reads the DEM at
            // the warrant reach; what a car goes over — and what the edge
            // audit measures — is the section as built on the 12 m lattice,
            // which sags under the DEM on a falling verge. On the round-one
            // bake eight barrier run ends stopped short of a critical fall the
            // plan had not seen (BlueRidge wp 1237 L; Little Switzerland wp
            // 612 L, 843 R, 854 R, 904 R; Blowing Rock wp 1616 R and 1623 R
            // among them), most of them one station short — the fall under the
            // run's own sinking terminal, just past the last stone the barrier
            // rays still meet. So every open section, and every buried terminal
            // (which is graded open), is walked the audit's way once the tables
            // exist to grade it; what that finds is walled with its
            // EndFlareStations either side, which carries the terminal out past
            // the fall.
            int passes = 0, builtFound = 0, stillCritical = 0;
            void LayAndWalk()
            {
                Roadside[][] walked = null;
                float[][] walkedUp = null, walkedE = null;
                stillCritical = 0;
                for (int pass = 0; pass < StageWarrantPasses; pass++)
                {
                    LayPlan();
                    passes = pass + 1;
                    int found = MarkBuiltSectionFalls(pts, builtCriticalBy, walked, walkedUp, walkedE);
                    if (found == 0) break;
                    if (pass + 1 == StageWarrantPasses) { stillCritical = found; break; }
                    builtFound += found;
                    walked = new Roadside[2][];
                    for (int si = 0; si < 2; si++)
                    {
                        walked[si] = new Roadside[n];
                        for (int i = 0; i < n; i++) walked[si][i] = GradedKind(si, i);
                    }
                    // LayPlan allocates fresh tables, so these stay the walked plan's.
                    walkedUp = rsWallUp;
                    walkedE = rsWallE;
                }
            }
            LayAndWalk();
            FinishStageCuts(pts, softRockBy, out float facedM, out float gradedM, out int lowered,
                            out int handOvers, out int softened);

            // A WALL THAT ENDS INTO A CUT THAT IS NOT ROCK. FinishStageCuts
            // grades a cut whose land a backslope reaches, and where it graded
            // every station of a cut near where a wall run flared into it, there
            // is no face for the stone to meet: the run's end was flared full
            // height out to a toe line with level land behind it (Blue Ridge wp 100 L,
            // "land +0.00 m from road height 1.5 m behind WallColl (2.95 m out),
            // on BankTop0"). That end is laid again as it would be beside any
            // graded land — flared and buried — and walked again, because a
            // buried terminal is graded open and the walk has to see it.
            int softEnds = 0, relays = 0;
            while (softened > 0 && relays < StageSoftRockRelays)
            {
                softEnds += softened;
                relays++;
                LayAndWalk();
                FinishStageCuts(pts, softRockBy, out facedM, out gradedM, out lowered, out handOvers, out softened);
            }

            catches.Sort();
            float wallM = 0f;
            foreach (float m in metresBy) wallM += m;
            Log($"Stage roadside plan: {rsWallRuns.Count} warranted wall runs over {wallM:0} m " +
                $"(deck {metresBy[1]:0}, approach {metresBy[4]:0}, critical fill {metresBy[2]:0}, " +
                $"critical on the built section {metresBy[7]:0}, " +
                $"water {metresBy[3]:0}, tunnel mouth {metresBy[5]:0}" +
                (metresBy[6] > 0f ? $", theme {metresBy[6]:0}" : "") + $"); " +
                $"{flaredOpen} run ends flared and buried into graded land, {flaredRock} flared into a rock face " +
                $"({handOvers} hand-over station(s) faced at least {BankRiseM:0.0} m where the cut there was graded; " +
                $"{softEnds} end(s) laid again as buried terminals because their cut was graded where they met it" +
                (softened > 0 ? $", and {softened} more this plan still flares into graded cut (raise StageSoftRockRelays)" : "") +
                $"); " +
                $"{rsBankRuns.Count} cut faces over {cutM:0} m ({facedM:0} m faced in rock, " +
                $"{gradedM:0} m graded to a 1V:{1f / RoadsideRules.BackSlope:0}H backslope; " +
                $"{lowered} face heights landed lower on the hill behind them); " +
                $"{openM:0} m of open graded roadside" +
                (catches.Count > 0
                    ? $" meeting the land {catches[catches.Count / 2]:0.00} m (p50) / {catches[catches.Count - 1]:0.00} m (max) past the tarmac edge"
                    : "") +
                $"; the walk over the built section found {builtFound} critical half-section(s) the DEM test missed " +
                $"in {passes} pass(es)" +
                (stillCritical > 0
                    ? $", and {stillCritical} more on its last pass that this plan does NOT wall (raise StageWarrantPasses)."
                    : "."));
        }

        /// <summary>
        /// Walk every half-section graded OPEN — open sides and the buried
        /// terminals of warranted runs — against the fall warrant as it will be
        /// built (<see cref="BuiltSectionCritical"/>) and mark the critical
        /// ones in <paramref name="built"/>. With <paramref name="walked"/>
        /// (the graded kinds the last walk saw) and <paramref name="walkedUp"/>
        /// and <paramref name="walkedE"/> (that plan's rsWallUp and rsWallE —
        /// a terminal's stone is part of what is walked, and a run end that
        /// moves re-flares the far end of a short run too), only stations near
        /// one whose section has changed since are read again. Returns how
        /// many it marked.
        /// </summary>
        static int MarkBuiltSectionFalls(List<Vector3> pts, bool[][] built, Roadside[][] walked,
                                         float[][] walkedUp, float[][] walkedE)
        {
            int n = pts.Count, found = 0;
            float reachE = KerbWidth + RoadsideRules.WarrantReachM;
            var ys = new float[Mathf.RoundToInt(reachE / StageWarrantPitchM) + 1];
            // The field under the lattice's vertices is fixed for this laying.
            var vertexY = new Dictionary<long, float>();
            var near = walked != null ? new bool[n] : null;
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                if (near != null)
                {
                    Array.Clear(near, 0, n);
                    for (int i = 0; i < n; i++)
                    {
                        if (GradedKind(s, i) == walked[s][i]
                            && Mathf.Abs(rsWallUp[s][i] - walkedUp[s][i]) < 1e-4f
                            && Mathf.Abs(rsWallE[s][i] - walkedE[s][i]) < 1e-4f) continue;
                        for (int o = -StageWarrantNearStations; o <= StageWarrantNearStations; o++)
                        {
                            int j = i + o;
                            if (!Loop && (j < 0 || j >= n)) continue;
                            near[WrapIdx(j, n)] = true;
                        }
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    if (built[s][i] || (near != null && !near[i]) || GradedKind(s, i) != Roadside.Open) continue;
                    if (!BuiltSectionCritical(pts, i, side, ys, vertexY)) continue;
                    built[s][i] = true;
                    found++;
                }
            }
            return found;
        }

        /// <summary>
        /// Does the open section at station <paramref name="i"/>, AS BUILT,
        /// warrant a barrier? The same walk as the edge audit's EDGE FALL
        /// (RoadsideRules.WorstCriticalFall), over the same span — the tarmac
        /// edge out to the kerb strip plus RoadsideRules.WarrantReachM — at the
        /// same 5 cm pitch, over the surfaces the audit's rays will land on as
        /// far as a plan can know them:
        ///   * the flush strip, then the open section's ribbon to its last
        ///     point (its catch, or where TidyShoulderProfile clips it on the
        ///     inside of a bend) — the lattice is solved to stay under it;
        ///   * past that, the ribbon's slope CARRIED ON by the emitter's own
        ///     walk (ShoulderCarry: never gentler than 1V:4H, no deeper than
        ///     ShoulderCarryMaxM, stopping at the first ShoulderCatchStepM
        ///     where it has met the lattice, and on a tight bend's inside
        ///     steepening to 1V:3H past the section's limit) and its toe
        ///     tuck, each over the lattice where the lattice is higher. A
        ///     stage's tail (ShoulderTailSlope) turns down at ShoulderTailE,
        ///     past this walk's last sample, so of a carry that reaches it the
        ///     walk reads the same slope with or without it;
        ///   * then the lattice alone, read at every sample on its own facet
        ///     the way StageLatticeY reads it (a 0.5 m read with a lerp between
        ///     cut the corner of every facet crease it crossed by up to an
        ///     eighth of the change in grade — more than the slack);
        ///   * and at a buried terminal the top of its sinking stone's collider
        ///     over the stone's width (StageWallRing's height), wherever that
        ///     top is under the lower barrier ray and so is ground the audit's
        ///     profile rides over rather than a barrier that stops it: 7 cm
        ///     over the tarmac on the station one short of a three-station
        ///     flare's end, which is more than the slack, and that station is
        ///     the first one past the last stone the barrier rays meet — the
        ///     first a RUN END looks at.
        /// <paramref name="vertexY"/> caches the lattice's vertices for one
        /// laying of the plan (the field under them is fixed until the next).
        /// </summary>
        static bool BuiltSectionCritical(List<Vector3> pts, int i, float side, float[] ys,
                                         Dictionary<long, float> vertexY)
        {
            int s = SideIx(side);
            float half = RoadWidth * 0.5f;
            float tarmac = pts[i].y + RoadLift;
            Vector3 outw = rsRight[i] * side;

            float Vertex(int gx, int gz)
            {
                long key = LatticeKey(gx, gz);
                if (!vertexY.TryGetValue(key, out float h)) vertexY[key] = h = StageLatticeVertexY(gx, gz);
                return h;
            }
            // StageLatticeY's triangulation (GridChunkMesh's a-c diagonal),
            // tarmac-relative, over the cached vertices.
            float Ground(float e)
            {
                Vector3 p = pts[i] + outw * (half + e);
                int gx = Mathf.FloorToInt(p.x / NearCell), gz = Mathf.FloorToInt(p.z / NearCell);
                float u = p.x / NearCell - gx, w = p.z / NearCell - gz;
                float ha = Vertex(gx, gz), hc = Vertex(gx + 1, gz + 1);
                if (w >= u)
                {
                    float hb = Vertex(gx, gz + 1);
                    return ha + (hc - hb) * u + (hb - ha) * w - tarmac;
                }
                float he = Vertex(gx + 1, gz);
                return ha + (he - ha) * u + (hc - he) * w - tarmac;
            }

            // The open profile's last point, as the emitter is given it.
            float bendReach = ShoulderBendReach(pts, i, side);
            float endE = Mathf.Min(rsCatchE[s][i], Mathf.Max(bendReach, KerbWidth));
            float endDy = OpenSectionDy(endE);
            // Its end slope (ShoulderSlopedEnd): the 4% shoulder is flat, the
            // foreslope past it is 1V:6H, 1V:4H or 1V:3H by where it ends.
            float run0 = endE - ShoulderEndE;
            float slope = run0 <= 0.01f ? RoadsideRules.ShoulderCrossFall
                        : run0 <= RoadsideRules.ClearZoneM ? RoadsideRules.RecoverableSlope
                        : run0 <= RoadsideRules.WarrantReachM ? RoadsideRules.SteepestRecoverableSlope
                        : RoadsideRules.TraversableSlope;
            bool sloped = slope >= ShoulderSlopedEnd;
            float fall = Mathf.Max(slope, RoadsideRules.SteepestRecoverableSlope);
            // The emitter's own carry (ShoulderCarry), knee and all.
            float carryE = endE, carryDy = endDy, kneeE = float.NaN, kneeDy = 0f;
            if (sloped && Ground(endE) < endDy)
                ShoulderCarry(endE, endDy, fall, bendReach, ShoulderFoldReach(pts, i, side), ShoulderTailE, Ground,
                              out kneeE, out kneeDy, out carryE, out carryDy, out _, out _, out _);
            float CarriedDy(float e) => !float.IsNaN(kneeE) && e > kneeE
                ? kneeDy + (carryDy - kneeDy) * (e - kneeE) / Mathf.Max(carryE - kneeE, 1e-5f)
                : endDy - fall * (e - endE);
            float tuckE = carryE + RoadsideRules.ToeTuckRunM;
            float tuckDy = carryDy - RoadsideRules.ToeTuckM;
            if (sloped)
                tuckDy = Mathf.Min(tuckDy, Mathf.Max(carryDy - ShoulderSkirtMaxM, Ground(tuckE) - RoadsideRules.ToeTuckM));

            // A buried terminal's stone, where it is ground to the audit.
            float stoneFrom = float.PositiveInfinity, stoneTo = float.NegativeInfinity, stoneDy = 0f;
            if (BuriedTerminal(s, i))
            {
                float faceE = rsWallE[s][i];
                float buryDy = OpenSectionDy(faceE + StageWallFaceIn + StageWallDrawThick);
                stoneDy = Mathf.Lerp(buryDy - 0.05f, StageWallH - RoadLift, rsWallUp[s][i]);
                if (stoneDy < RoadsideRules.BarrierRayHeights[0])
                {
                    stoneFrom = faceE;
                    stoneTo = faceE + StageWallCollThick;
                }
            }

            for (int k = 0; k < ys.Length; k++)
            {
                float e = k * StageWarrantPitchM;
                float y;
                if (e <= KerbWidth) y = KerbStripLift;
                else if (e <= endE) y = OpenSectionDy(e);
                else if (e <= carryE) y = Mathf.Max(CarriedDy(e), Ground(e));
                else if (e <= tuckE)
                    y = Mathf.Max(Mathf.Lerp(carryDy, tuckDy, (e - carryE) / RoadsideRules.ToeTuckRunM), Ground(e));
                else y = Ground(e);
                if (e >= stoneFrom && e <= stoneTo) y = Mathf.Max(y, stoneDy);
                ys[k] = y;
            }
            return RoadsideRules.WorstCriticalFall(ys, 0, ys.Length - 1, StageWarrantPitchM,
                                                   StageWarrantSlackM, out _) > 0f;
        }

        /// <summary>
        /// Shortest run of stations that stands as a KERB-HIGH rock face — one
        /// under BankRiseM, a face only because its land sits a few centimetres
        /// past what a 1V:3H backslope reaches by the rock top's first sample.
        /// A face like that one or two stations long between graded ones is a
        /// nub of rock in a backslope, and the chords either side of it slant;
        /// graded, its backslope simply climbs a little further (to 2.3 m past
        /// the toe at most) and it is one more stretch of backslope. A nub a
        /// metre or more tall is a real outcrop and keeps its face, and so does
        /// a face held into a wall or a portal.
        ///
        /// (The first cut of this demoted only faces the backslope already
        /// reached — which by construction are never faces, since reaching
        /// means land under 0.67 m and a face that tall or taller cannot. It
        /// never fired.)
        /// </summary>
        const int CutFaceMinStations = 3;

        /// <summary>
        /// THE TOP OF A CUT LANDS ON WHAT IS BEHIND IT — after the smoothing,
        /// the wobble, the taper and the release fade have had their say, not
        /// only in StageBankSolve.
        ///
        /// The round-one bake measured what happened where they disagreed. A
        /// run's face tapers over four stations and its rock top's release
        /// fades over five, and the top was lerped toward a lattice still
        /// pinned 0.45 m under the tarmac: one station in from every run end
        /// that met land, a face 0.33-0.38 m tall stood 2.9 m out with its top
        /// just under the lower barrier ray (EDGE FACE, 23 of them) and the
        /// rock top falling away behind it at 1V:2.6H to 1V:3.6H (EDGE SLOPE,
        /// 25); a station or two further in, a box-backed face over rock top
        /// that came back down to road height 1.5 m behind it (UNWARRANTED
        /// BARRIER, ~19). A ridge, a moat and a kerb-high face — the shape of
        /// none of the things a cut is.
        ///
        /// So each face is lowered until the rock top at its first two samples
        /// stands at least as high as its crest — reading the release the top
        /// will really have, or the full hill where the run holds its height
        /// into a wall or a portal. Then, per station, the AASHTO question:
        /// can a RoadsideRules.BackSlope (1V:3H) backslope climbing out of the
        /// ditch reach the land right behind the toe by the rock top's first
        /// sample?
        ///   * Yes, and the face left is under BankRiseM (under a metre it is a
        ///     kerb, not a cut): GRADED. The ditch's backslope carries on up to
        ///     that land and daylights into the rock top, with no vertical rock
        ///     and no box — the crest is the land, so there is no lip either.
        ///   * No — the land rises faster than the backslope can follow: a
        ///     FACE, at least BankRiseM tall, whose hill behind holds its crest:
        ///     a natural barrier, never a pocket.
        /// A kerb-high face run shorter than <see cref="CutFaceMinStations"/>
        /// is graded too, its backslope carried up to its land: run
        /// hysteresis, so a cut does not flicker between rock and slope a
        /// station at a time where its land hovers at the backslope's reach.
        ///
        /// THE STATION A WALL HANDS OVER AT IS NEVER GRADED. A warranted run
        /// that ends beside a cut flares its stone out to the face's toe line
        /// and overlaps the cut by a station (PlanStageRoadside), so behind
        /// that last stone is the cut's rock top. Graded, that top was the
        /// land a backslope reaches — level with the road — held flat behind a
        /// full-height stone: every "UNWARRANTED BARRIER ... behind
        /// Track/Walls/WallColl (2.94-2.97 m out), on Track/Banks/BankTopN" of
        /// the 2026-09-13 bake, ten of them at +0.00 to +0.28 m on four
        /// mountains (Blue Ridge 1332 L, Beech Gap 21 L and 2169 L, Blowing
        /// Rock 596 L, Little Switzerland 316 L, 494 R, 512 R, 897 R, 1054 R,
        /// and 100 L below). A stone flared into a cut is a buried-in-backslope
        /// terminal, and what it is buried in is rock: where the run has a
        /// face within BankReleaseFadeStations of it, the hand-over station is
        /// faced at least BankRiseM — as tall as the stone it receives — and
        /// its top holds that height behind the stone
        /// (<paramref name="handOvers"/> counts the ones that were graded).
        /// Where it has none (Blue Ridge 100 L: a cut graded on all four
        /// stations), there is no rock to flare into, and the cut
        /// station beside the wall is marked in <paramref name="softRock"/> so
        /// the plan lays that end again as a buried terminal
        /// (<paramref name="softened"/> newly marked).
        /// </summary>
        static void FinishStageCuts(List<Vector3> pts, bool[][] softRock, out float facedM, out float gradedM,
                                    out int lowered, out int handOvers, out int softened)
        {
            facedM = gradedM = 0f;
            lowered = handOvers = softened = 0;
            int n = pts.Count;
            rsCutGraded = new[] { new bool[n], new bool[n] };
            if (rsBankRuns == null) return;
            var face = new List<bool>();
            var kerbHigh = new List<bool>();
            var landDy = new List<float>();
            foreach (var (run, side) in rsBankRuns)
            {
                int s = SideIx(side), L = run.Length;
                face.Clear(); kerbHigh.Clear(); landDy.Clear();
                for (int k = 0; k < L; k++)
                {
                    int i = run[k];
                    float h0 = rsFaceH[s][i], h = h0;
                    float keep = rsCutHold[s][i] ? 1f : rsRelease[s][i];
                    // Twice: a lower crest moves the batter's limit on the hill
                    // and, for a tall face, the samples themselves.
                    //
                    // The whole ring, and each of its first two samples no higher
                    // than the shaped top's line down to its last point: where
                    // the top stops early — a switchback's other leg owns the
                    // hillside a few metres back, or a tight inside — that line
                    // pulls the top down right behind the crest, and a face
                    // landed on the raw base there had road-height land behind
                    // it again.
                    float holds = h, firstE = CutToeE + BankTopSampleE[0];
                    for (int it = 0; it < 2; it++)
                    {
                        float crestE = CutToeE + Mathf.Max(0f, h - BankPlinth) * BankBatter;
                        var ring = BankTopRing(pts, i, side, Mathf.Max(h, RoadLift), crestE, keep, false, false);
                        Vector2 far = ring[ring.Length - 1];
                        float y1 = Mathf.Min(ring[1].y, far.y + (far.x - ring[1].x) * RoadsideRules.TraversableSlope);
                        float y2 = Mathf.Min(ring[2].y, far.y + (far.x - ring[2].x) * RoadsideRules.TraversableSlope);
                        holds = Mathf.Min(y1, y2) - pts[i].y;
                        firstE = ring[1].x;
                        if (holds >= h) break;
                        h = holds;
                    }
                    // The land right behind the toe, over the tarmac, and whether
                    // a backslope out of the ditch gets there by the first sample.
                    float land = holds - RoadLift;
                    bool reach = land <= (firstE - CutToeE) * RoadsideRules.BackSlope;
                    h = Mathf.Max(h, RoadLift);
                    if (!reach && h < BankRiseM) h = Mathf.Min(holds, BankRiseM);
                    if (h < h0 - 0.01f) lowered++;
                    rsFaceH[s][i] = h;
                    face.Add(h >= BankRiseM || !reach);
                    // Under BankRiseM a face stands only because the land is
                    // just out of the backslope's reach (land under 0.78 m).
                    kerbHigh.Add(h < BankRiseM && !rsCutHold[s][i]);
                    landDy.Add(land);
                }
                // Hysteresis: no kerb-high face shorter than CutFaceMinStations;
                // its backslope climbs the extra few centimetres instead.
                for (int k = 0; k < L; )
                {
                    if (!face[k]) { k++; continue; }
                    int len = 1;
                    while (k + len < L && face[k + len]) len++;
                    if (len < CutFaceMinStations)
                        for (int q = k; q < k + len; q++)
                            if (kerbHigh[q]) face[q] = false;
                    k += len;
                }
                // The hand-over stations: the run's walled ones (only a wall's
                // overlap station is both walled and in a cut run). The rock
                // one is buried in is a face NEAR it — within the stations over
                // which a run end holds its top into the wall — not anywhere
                // along a run that can be 224 stations long: faced from rock
                // half a kilometre away, a hand-over over level land would be
                // a lone BankRiseM block, which is no backslope to bury a
                // terminal in. (Python replica, all five mountains: every
                // hand-over has its face within two stations.)
                for (int k = 0; k < L; k++)
                {
                    int i = run[k];
                    if (rsKind[s][i] != Roadside.Walled) continue;
                    bool rock = false;
                    for (int q = Mathf.Max(0, k - BankReleaseFadeStations);
                         q <= Mathf.Min(L - 1, k + BankReleaseFadeStations) && !rock; q++)
                        rock = face[q] && rsKind[s][run[q]] != Roadside.Walled;
                    if (rock)
                    {
                        if (!face[k] || rsFaceH[s][i] < BankRiseM) handOvers++;
                        face[k] = true;
                        rsFaceH[s][i] = Mathf.Max(rsFaceH[s][i], BankRiseM);
                        continue;
                    }
                    // The wall run's first station past its end is this one's
                    // neighbour in the cut run.
                    for (int o = -1; o <= 1; o += 2)
                    {
                        int q = k + o;
                        if (q < 0 || q >= L || rsKind[s][run[q]] == Roadside.Walled || softRock[s][run[q]]) continue;
                        softRock[s][run[q]] = true;
                        softened++;
                    }
                }
                for (int k = 0; k < L; k++)
                {
                    int i = run[k];
                    rsCutGraded[s][i] = !face[k];
                    if (face[k]) { facedM += Spacing; continue; }
                    // A graded crest IS the land behind it (never under the toe),
                    // so the rock top leaves it level.
                    rsFaceH[s][i] = RoadLift + Mathf.Max(0f, landDy[k]);
                    gradedM += Spacing;
                }
            }
        }

        /// <summary>
        /// CONTRACT C4 (C1's EdgeProfileFn). The section beside station
        /// <paramref name="idx"/> on <paramref name="side"/> as (e, dy) points
        /// ordered outward: e metres past the tarmac edge, dy over the tarmac
        /// surface. Starts at the verge strip's outer edge, at its top; empty
        /// on a deck, where the deck is the surface. The emitter adds the toe
        /// tuck and the collider.
        /// </summary>
        internal static void StageEdgeProfile(List<Vector3> pts, int idx, float side, List<Vector2> profile)
        {
            profile.Clear();
            EnsureStageRoadside(pts);
            if (rsKind == null) return;
            int s = SideIx(side);
            var kind = GradedKind(s, idx);
            if (kind == Roadside.Deck) return;
            // Every section starts on the strip and bevels down the inch.
            profile.Add(new Vector2(KerbWidth, KerbStripLift));
            switch (kind)
            {
                case Roadside.Tunnel:
                    profile.Add(new Vector2(BevelEndE, ShoulderDy(BevelEndE)));
                    profile.Add(new Vector2(TunnelWallOut, ShoulderDy(TunnelWallOut)));
                    return;
                case Roadside.Walled:
                {
                    // The shoulder runs to the wall's face and ends in it — out
                    // through a flare into a rock face too, so the shoulder
                    // widens with the stone. (A buried terminal never gets
                    // here: it is graded Open.) At the station a deck is carried onto
                    // (DeckCoversStation) it runs at the deck's top instead, so
                    // the concrete's leading edge and end cap are flush with it
                    // rather than a centimetre proud.
                    //
                    // It ends where the car meets the wall's COLLIDER, not its
                    // drawn face: on the outside of a bend the chord boxes stand
                    // out by their sag (StageWallContactE), and a shoulder that
                    // stopped at the drawing tucked down into the gap in front of
                    // the box — measured as a 0.09-0.25 m EDGE DROP 1.10 m out on
                    // four mountains. The extra centimetres run under the stone.
                    float ew = StageWallContactE(pts, idx, s, side);
                    if (DeckCoversStation(idx))
                    {
                        profile.Add(new Vector2(BevelEndE, -DeckTopBelowTarmac));
                        profile.Add(new Vector2(ew, -DeckTopBelowTarmac));
                        return;
                    }
                    profile.Add(new Vector2(BevelEndE, ShoulderDy(BevelEndE)));
                    profile.Add(new Vector2(ew, ShoulderDy(ew)));
                    return;
                }
                case Roadside.Cut:
                {
                    float e1 = ShoulderEndE + CutForeslopeRunM, e2 = e1 + CutDitchFloorM;
                    profile.Add(new Vector2(BevelEndE, ShoulderDy(BevelEndE)));
                    profile.Add(new Vector2(ShoulderEndE, ShoulderEndDy));
                    profile.Add(new Vector2(e1, CutDitchDy));
                    profile.Add(new Vector2(e2, CutDitchDy));
                    profile.Add(new Vector2(CutToeE, 0f));
                    // A GRADED cut (FinishStageCuts): the backslope carries on at
                    // the same RoadsideRules.BackSlope up to the crest its rock
                    // top starts from, so the section climbs out of the ditch
                    // onto the hill with no lip. A faced cut stops at the toe,
                    // where its rock stands.
                    if (rsCutGraded != null && rsCutGraded[s][idx])
                    {
                        float crestE = CutCrestE(s, idx);
                        if (crestE > CutToeE + 0.01f) profile.Add(new Vector2(crestE, CutRiseDy(s, idx)));
                    }
                    return;
                }
                default:
                {
                    // The land an open section meets is never above the bench,
                    // so the catch is always well past the shoulder (2.7 m at
                    // the nearest).
                    float ec = rsCatchE[s][idx];
                    float e0 = ShoulderEndE;
                    profile.Add(new Vector2(BevelEndE, ShoulderDy(BevelEndE)));
                    profile.Add(new Vector2(e0, ShoulderEndDy));
                    float cz = e0 + RoadsideRules.ClearZoneM, reach = e0 + RoadsideRules.WarrantReachM;
                    if (cz < ec - 0.01f) profile.Add(new Vector2(cz, OpenSectionDy(cz)));
                    if (reach < ec - 0.01f) profile.Add(new Vector2(reach, OpenSectionDy(reach)));
                    profile.Add(new Vector2(ec, OpenSectionDy(ec)));
                    return;
                }
            }
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
                                  tint: sandy ? (Color?)null : theme.groundTint);
            var sandMat = sandy ? MakeMat(MeshPrefix + "Sand", theme.sand, affine: 0f) : null;
            var marshMat = sandy ? MakeMat(MeshPrefix + "Marsh",
                                           string.IsNullOrEmpty(theme.marsh) ? theme.ground : theme.marsh,
                                           affine: 0f) : null;
            var nearMats = sandy ? new[] { nearMat, sandMat, marshMat } : new[] { nearMat };
            if (theme.stageForest)
                RegisterSeasonalTexture(MeshPrefix + "Ground", nearMat, DressTurfPath, "ground");
            else
                RegisterSeasonalGround(MeshPrefix + "Ground", theme.ground, nearMat,
                                       (sandy ? (Color?)null : theme.groundTint) ?? Color.white, "ground");
            if (marshMat != null)
                RegisterSeasonalGround(MeshPrefix + "Marsh",
                                       string.IsNullOrEmpty(theme.marsh) ? theme.ground : theme.marsh,
                                       marshMat, Color.white, "ground");
            // Far: the mountain paints its distance as autumn forest. An island
            // has no distance to paint — what is out there is water, and the
            // sea plane covers it — so the far ring reuses the near ground.
            //
            // SCRUB, not sand. Painting it sand was the obvious choice and it
            // was wrong: the overview came back with the whole mainland and
            // both shores rendered as one continuous beach, because a beach is
            // a narrow strip and everything BEHIND it is not. Distant land is
            // scrub; the sand is where the mask says it is, in the near band.
            //
            // A city names its own far texture (theme.farGround): there is
            // no forest to paint and no island to reuse, just more of the
            // same ground out to the fog.
            var farMat = !string.IsNullOrEmpty(theme.farGround)
                ? MakeMat(MeshPrefix + "GroundFar", theme.farGround, affine: 0f)
                : string.IsNullOrEmpty(theme.sand)
                ? MakeMat(MeshPrefix + "GroundFar", StageGenDir + "/FallMottle.png", affine: 0f)
                : MakeMat(MeshPrefix + "GroundFar", theme.ground, affine: 0f);
            if (string.IsNullOrEmpty(theme.farGround) && string.IsNullOrEmpty(theme.sand))
                RegisterSeasonalTexture(MeshPrefix + "GroundFar", farMat, DressMottlePath, "far");

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
                    var col = go.AddComponent<MeshCollider>();
                    col.sharedMesh = mesh;
                    // The verge beside a mountain road is the surface a player
                    // rides the edge of, and it is the car's SHELL that touches
                    // it — see CarSlideFriction.
                    col.sharedMaterial = SlidePhys();
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
            var hole = hasTunnels ? new bool[verts.Length] : null;
            var zone = hasTunnels ? new bool[verts.Length] : null;
            var tris = new List<int>(cells * cells * 6);
            var sandTris = sandy ? new List<int>(cells * cells * 2) : null;
            var marshTris = sandy ? new List<int>(cells * cells * 2) : null;
            for (int gz = 0, v = 0; gz <= cells; gz++)
                for (int gx = 0; gx <= cells; gx++, v++)
                {
                    float wx = ox + gx * cell, wz = oz + gz * cell;
                    // The near grid is the LATTICE the shoulder's toes, the
                    // walls' footings and the rock tops were measured against:
                    // the field less PrepareStageLattice's sink, read through
                    // the same vertex function StageLatticeY reads. (Chunk
                    // origins are multiples of the cell, so the world index is
                    // exact.) The far grid is the field, sunk.
                    float groundY = cell == NearCell && yOffset == 0f
                        ? StageLatticeVertexY(Mathf.RoundToInt(wx / NearCell), Mathf.RoundToInt(wz / NearCell))
                        : StageGroundHeightAt(wx, wz);
                    verts[v] = new Vector3(gx * cell, groundY + yOffset, gz * cell);
                    uvs[v] = new Vector2(wx / tile, wz / tile);
                    if (surf != null) surf[v] = StageSurfAt(wx, wz);
                    if (routeD != null) routeD[v] = RouteDistanceCoarse(wx, wz);
                    if (hole != null)
                    {
                        hole[v] = InTunnelHole(wx, wz);
                        zone[v] = InTunnelZone(wx, wz);
                    }
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
                    // ANY corner inside the tube's footprint: the quad goes.
                    // A quad that straddles the tube's edge would run its
                    // diagonal from the ridge above down through the road
                    // inside the tube, with a collider on it.
                    //
                    // A quad merely NEAR the tube is measured rather than
                    // dropped on sight: kept only if its own triangles clear
                    // the tube's ceiling everywhere over the bore and stand no
                    // higher than the field (by the hide margin) everywhere
                    // over the approach (<see cref="TunnelQuadClear"/>). That
                    // keeps the mountain beside the bore and still drops the
                    // diagonal from the ridge down across a mouth, and the
                    // facet under a thin-covered exit where SRTM puts the hill
                    // 1.2-3.8 m over the road, UNDER the 5.2 m roof. The tube's
                    // walls, the portal face and its colliders, and the guard
                    // or rock face carried to the mouth, cover what is left.
                    if (hole != null)
                    {
                        if (hole[a] || hole[b] || hole[c] || hole[e2]) continue;
                        if ((zone[a] || zone[b] || zone[c] || zone[e2])
                            && !TunnelQuadClear(ox + gx * cell, oz + gz * cell, cell,
                                                verts[a].y - yOffset, verts[b].y - yOffset,
                                                verts[c].y - yOffset, verts[e2].y - yOffset))
                            continue;
                    }
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
        /// coincides with a visible wall so it never reads as a force field.
        /// None of it over a buried terminal's lowered stone: a collider
        /// taller than its drawing is an invisible wall.</summary>
        const float StageWallCollH = 1.7f;
        /// <summary>
        /// HOW DEEP A GUARD WALL IS TO THE SOLVER, as opposed to how deep it
        /// looks. 1.2 m, matching the circuits' WallCollThick, and grown
        /// entirely OUTWARD so the surface the player touches does not move.
        ///
        /// It was 0.4 — the drawn thickness — and the circuits' own wall pass
        /// says in as many words why that is not enough: "the collider is far
        /// thicker than the drawn wall and grows only OUTWARD, so the contact
        /// surface is unchanged while a fast car has real depth to catch
        /// against." The stages never got it. At 40 m/s a car covers 0.8 m in
        /// one physics step, so a 0.4 m box is thinner than a single tick of
        /// travel and a corner arriving at a seam between two of them can find
        /// the far side. Reported as "walls don't have thickness like they
        /// should — it's easy to get off the track".
        /// </summary>
        const float StageWallCollThick = 1.2f;
        /// <summary>A wall's footing goes this far under the lattice BEHIND
        /// it, and never deeper than StageWallMaxFoot under the waypoint plane.
        /// It stood at a fixed plane - 0.45, and on a fill the 12 m lattice
        /// right behind the stone runs a metre and more under that (a chord
        /// across a falling verge lies below the field it samples), so from
        /// the valley the wall floated — and its collider, which stopped at
        /// plane - 0.4, left a window under itself.</summary>
        const float StageWallFootSink = 0.3f, StageWallMaxFoot = 6f;
        /// <summary>A buried terminal's chord with less stone than this over
        /// the foreslope at its face gets no collider (the wall solid ends
        /// there): what is left is a kerb of stone the shoulder ribbon rides
        /// over.</summary>
        const float StageWallCollMinH = 0.1f;
        /// <summary>
        /// Bends tighter than this get half-station chords on their inside.
        ///
        /// A wall collider was a box per station chord, and where the heading
        /// turns 4 m / R per station its ends, which overlapped the next box,
        /// swung toward the road: the obstacle audit found 22 on the Blowing
        /// Rock loop's hairpins the first time a wall stood on an inside. The
        /// answer then was to leave insides unwalled. A warranted wall cannot
        /// be left out, so its inside chords are halved (a ring between the
        /// stations) and set back by their own sag and end overlap, and the
        /// drawn stone takes the same rings. The collider is one solid per run
        /// now (BuildWallSolid), with no overlapping ends to swing; the
        /// half rings and the set-back (StageWallContactE) stay, because the
        /// walled shoulder is built to that line and the stone to those rings.
        /// </summary>
        const float StageTightBendR = 80f;

        /// <summary>Is <paramref name="side"/> the inside of a bend tighter
        /// than <see cref="StageTightBendR"/> at station i, and how tight?
        /// Radius from the chord over six stations and its sagitta; the
        /// inside is the side the chord's midpoint lies on.
        ///
        /// SYMMETRIC, OR NOT AT ALL. On a point-to-point route WrapIdx clamps a
        /// neighbour past either end to the end station itself, so the "chord"
        /// at wp 0 ran from the station to three stations on and its midpoint
        /// stood 6 m off it: a 3 m radius (8 m at wp 1, 25 m at wp 2) on a
        /// straight, and bogus set-back half chords built at both ends of
        /// Mount Mitchell and Blue Ridge (an EDGE FACE on WallColl at 1.10 m,
        /// the box 0.3 m off the shoulder). Near an end the chord is narrowed
        /// to what fits either side — r = c^2 / 8 sag holds for a symmetric
        /// chord of any span — and at the end station itself there is no bend
        /// to read.</summary>
        static bool TightInside(List<Vector3> pts, int i, float side, out float radius)
        {
            int n = pts.Count;
            radius = 0f;
            int span = Loop ? 3 : Mathf.Min(3, Mathf.Min(i, n - 1 - i));
            if (span < 1) return false;
            Vector3 a = pts[WrapIdx(i - span, n)], b = pts[i], c = pts[WrapIdx(i + span, n)];
            Vector3 chord = c - a; chord.y = 0f;
            Vector3 toMid = (a + c) * 0.5f - b; toMid.y = 0f;
            float len = chord.magnitude, sag = toMid.magnitude;
            if (len < 1e-3f || sag < 1e-4f) return false;
            float r = len * len / (8f * sag);
            if (r >= StageTightBendR || Vector3.Dot(toMid, rsRight[i] * side) <= 0f) return false;
            radius = r;
            return true;
        }

        static void BuildStageWalls(List<Vector3> pts, Transform parent)
        {
            EnsureStageRoadside(pts);
            if (rsWallRuns == null) return;
            int n = pts.Count;
            var mat = MakeMat(MeshPrefix + "Wall", theme.wall, affine: 0f);
            var phys = GetOrCreatePhysMat("WallPhys", 0.05f, 0.05f);
            var root = new GameObject("Walls");
            root.transform.SetParent(parent, false);

            int runs = 0, walled = 0;
            int solids0 = wallSolidCount;
            float solidM0 = wallSolidM;
            foreach (var run in rsWallRuns)
            {
                BuildOneStageWall(pts, run.from, run.len, run.side, root.transform, mat, phys, runs++);
                walled += run.len;
            }
            Log($"Stage guard walls: {runs} warranted runs covering {walled * Spacing:0} m of shoulder " +
                $"(of {n * Spacing * 2:0} m of roadside); {wallSolidCount - solids0} wall solid(s), " +
                $"{wallSolidM - solidM0:0} m of collider face.");
            PlaceStagePosts(pts, parent);
        }

        // ------------------------------------------------------------------
        //  Reflector posts along the guard walls (sense of speed)
        // ------------------------------------------------------------------
        /// <summary>Gap between the drawn wall's back and a post's face.</summary>
        const float StagePostGap = 0.05f;
        const float StagePostW = 0.10f;
        /// <summary>How far the post shows above the wall's top. The stone is
        /// 0.85 m; a parkway reflector post stands about 1.5 m, so 0.65 m of
        /// white-and-red rises behind the wall.</summary>
        const float StagePostAboveWall = 0.65f;
        /// <summary>Sunk into the ground like the wall's own footing, so a
        /// coarse facet can never show daylight under the foot.</summary>
        const float StagePostSink = 0.1f;

        /// <summary>
        /// Reflector posts down every guard-wall run, one combined mesh, no
        /// colliders. Only where there IS a wall: the stage keeps its
        /// "nothing built" rule everywhere else. Not where a deck covers the
        /// station (the deck's own test, DeckCoversStation — a post there
        /// would hang through the concrete to the ground under it), not on
        /// a buried terminal, where there is no wall top to stand behind, and
        /// not where the wall hands over to a rock face (the station both
        /// stand at), where the post would come up through the rock top.
        /// Each post follows its wall out through a flare and stands on the
        /// lattice behind the stone, which on a fill is well under the road.
        /// </summary>
        static void PlaceStagePosts(List<Vector3> pts, Transform parent)
        {
            if (theme.postEvery <= 0 || rsWallRuns == null || rsWallRuns.Count == 0) return;
            var mat = MakeMat("RoadPost", PostTexPath, affine: 0f);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int n = pts.Count;
            float half = RoadWidth * 0.5f;
            int placed = 0, spans = 0, terminals = 0;
            foreach (var run in rsWallRuns)
            {
                int s = SideIx(run.side);
                for (int k = 1; k < run.len; k += theme.postEvery)
                {
                    int i = WrapIdx(run.from + k, n);
                    if (DeckCoversStation(i)) { spans++; continue; }
                    if (rsWallUp[s][i] < 0.999f || rsFaceH[s][i] > 0f) { terminals++; continue; }
                    Vector3 right = rsRight[i];
                    float postE = rsWallE[s][i] + StageWallFaceIn + StageWallDrawThick + StagePostGap + StagePostW * 0.5f;
                    Vector3 baseP = pts[i] + right * (run.side * (half + postE));
                    float ground = StageLatticeY(baseP.x, baseP.z);
                    baseP.y = Mathf.Max(Mathf.Min(pts[i].y - 0.45f, ground), pts[i].y - StageWallMaxFoot) - StagePostSink;
                    float top = pts[i].y + StageWallH + StagePostAboveWall;
                    AppendPost(verts, uvs, tris, baseP, right, StagePostW, top - baseP.y);
                    placed++;
                }
            }
            CombinedPosts(verts, uvs, tris, "StagePosts", "StagePosts", mat, parent);
            Log($"Placed {placed} reflector posts along the guard walls " +
                $"({spans} span stations and {terminals} terminal or rock hand-over stations skipped).");
        }

        /// <summary>How far a one-station chord of the wall line on
        /// <paramref name="side"/> sags toward the road at station i: zero on
        /// a straight and on the inside of a bend, c^2 / 8R on the outside,
        /// with R from the two neighbouring stations. Capped at half a metre
        /// so a kink in the data cannot throw a box into the forest. Zero at
        /// either end of a point-to-point route: the clamped neighbour there
        /// is the station itself, which read half a chord along the road as a
        /// 0.5 m sag and stood the first and last boxes 0.3-0.5 m off the
        /// shoulder (a 1 m EDGE DROP at wp 0 L on Blue Ridge and Mount
        /// Mitchell).</summary>
        static float WallChordSag(List<Vector3> pts, int i, float side)
        {
            int n = pts.Count;
            if (!Loop && (i <= 0 || i >= n - 1)) return 0f;
            Vector3 a = pts[WrapIdx(i - 1, n)], b = pts[i], c = pts[WrapIdx(i + 1, n)];
            Vector3 toMid = (a + c) * 0.5f - b; toMid.y = 0f;
            float sag2 = toMid.magnitude;                 // the two-station chord's sag
            if (sag2 < 1e-4f) return 0f;
            // The bend turns toward toMid; this side is the OUTSIDE when it
            // faces the other way.
            if (Vector3.Dot(toMid, RightAt(pts, i) * side) > 0f) return 0f;
            // sag(c) = c^2 / 8R, and sag2 = (2c)^2 / 8R, so a one-station
            // chord sags a quarter of what the two-station one does.
            return Mathf.Min(0.5f, sag2 * 0.25f);
        }

        /// <summary>
        /// Where a car meets a guard wall's COLLIDER at station i, as e past the
        /// tarmac edge: the drawn face (rsWallE) moved out by the one-station
        /// chord's sag on the outside of a bend, and on a tight inside by a
        /// half chord's sag and the end swing the old overlapping boxes had
        /// (a few centimetres; kept, as the shoulder is built to it). It is
        /// the ONE offset BuildOneStageWall gives the wall solid's face at this
        /// station's ring, for both chords that share it. The walled shoulder
        /// runs to here, so the surface in front of the collider is shoulder
        /// all the way to it.
        /// </summary>
        static float StageWallContactE(List<Vector3> pts, int i, int s, float side)
        {
            float faceE = rsWallE[s][i];
            float extra = WallChordSag(pts, i, side);
            if (TightInside(pts, i, side, out float r))
            {
                // The set-back the per-chord boxes took for a half chord (overlap
                // 0.25 m) on the wall line, whose radius is the bend's less the
                // offset. The wall solid has no overlap to swing, but this line
                // is where the shoulder ends, so the solid's face stays on it.
                float rw = Mathf.Max(r - (RoadWidth * 0.5f + faceE), 1f);
                float c = Spacing * 0.5f * rw / Mathf.Max(r, 1f);
                extra = Mathf.Max(extra, c * c / (8f * rw) + 0.25f * 0.5f * c / rw);
            }
            return faceE + extra;
        }

        /// <summary>One cross-section of a guard wall. The drawing and the
        /// collider are both built from these, which is what keeps a collider
        /// from ever standing taller or further in than its stone.</summary>
        struct WallRing
        {
            public Vector3 centre, right;
            /// <summary>Collider face (e past the tarmac edge), how much stone
            /// stands, footing, stone top, collider top, the shoulder's height
            /// at the face, the outside-of-bend chord sag, and the inside-of-
            /// bend radius (0 when not a tight inside).</summary>
            public float faceE, up, baseY, topY, collTopY, groundY, sag, tightR;
            public bool mid;
        }

        static WallRing StageWallRing(List<Vector3> pts, int i, int s, float side)
        {
            var g = new WallRing
            {
                centre = pts[i], right = rsRight[i],
                faceE = rsWallE[s][i], up = rsWallUp[s][i],
                sag = WallChordSag(pts, i, side),
            };
            if (TightInside(pts, i, side, out float r)) g.tightR = r;
            // What a car meets at the collider face: the walled shoulder, or at
            // a buried terminal the open foreslope the stone is sinking into.
            bool terminal = BuriedTerminal(s, i);
            float faceDy = terminal ? OpenSectionDy(g.faceE) : ShoulderDy(g.faceE);
            g.groundY = pts[i].y + RoadLift + faceDy;
            // On a deck (and the station either end it is carried onto) the
            // stone stands IN the deck; anywhere else its footing goes under
            // the lattice behind it.
            g.baseY = pts[i].y - 0.45f;
            if (!DeckCoversStation(i))
            {
                Vector3 behind = pts[i] + g.right * (side * (RoadWidth * 0.5f + g.faceE + StageWallFaceIn
                                                              + StageWallDrawThick + StageWallFootSink));
                float lattice = StageLatticeY(behind.x, behind.z);
                g.baseY = Mathf.Max(Mathf.Min(g.baseY, lattice - StageWallFootSink), pts[i].y - StageWallMaxFoot);
            }
            // A buried terminal's stone goes down to just under the foreslope —
            // measured at the stone's BACK, the lower edge of a falling slope,
            // so no lip of its flat top pokes out behind — and the invisible
            // extra over the stone stands only where the stone is full height:
            // the wall solid takes collTopY only at a ring whose neighbours are
            // full height too, so on a terminal it never stands over its
            // drawing.
            float buryDy = terminal
                ? OpenSectionDy(g.faceE + StageWallFaceIn + StageWallDrawThick) : faceDy;
            g.topY = Mathf.Lerp(pts[i].y + RoadLift + buryDy - 0.05f, pts[i].y + StageWallH, g.up);
            g.collTopY = g.up >= 0.999f ? g.topY + (StageWallCollH - StageWallH) : g.topY;
            return g;
        }

        static WallRing MidWallRing(in WallRing a, in WallRing b) => new WallRing
        {
            centre = (a.centre + b.centre) * 0.5f,
            right = (a.right + b.right).normalized,
            faceE = (a.faceE + b.faceE) * 0.5f,
            up = (a.up + b.up) * 0.5f,
            baseY = Mathf.Min(a.baseY, b.baseY),
            topY = (a.topY + b.topY) * 0.5f,
            collTopY = Mathf.Min(a.collTopY, b.collTopY),
            groundY = (a.groundY + b.groundY) * 0.5f,
            tightR = Mathf.Max(a.tightR, b.tightR),
            mid = true,
        };

        static void WallQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                             Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                             Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3, Vector3 facing)
        {
            int v = verts.Count;
            verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
            uvs.Add(u0); uvs.Add(u1); uvs.Add(u2); uvs.Add(u3);
            QuadFacing(verts, tris, v, v + 1, v + 2, v + 3, facing);
        }

        static void BuildOneStageWall(List<Vector3> pts, int from, int stations, float side,
                                      Transform parent, Material mat, PhysicsMaterial phys, int no)
        {
            int n = pts.Count, s = SideIx(side);
            float half = RoadWidth * 0.5f;

            // The rings: one per station, a half-station ring on a tight
            // inside, and — where the run ends into a tunnel — one on the
            // portal's plane, so the stone meets the portal face instead of
            // stopping a chord short of it.
            var rings = new List<WallRing>(stations + 4);
            var (before, after) = (-1, -1);
            if (RunEnd((from, stations), 0, n, out _, out int b0) && hasTunnels && tunnelIn[b0]) before = b0;
            if (RunEnd((from, stations), 1, n, out _, out int b1) && hasTunnels && tunnelIn[b1]) after = b1;
            void Add(WallRing ring)
            {
                if (rings.Count > 0)
                {
                    var prev = rings[rings.Count - 1];
                    if (prev.tightR > 0f || ring.tightR > 0f) rings.Add(MidWallRing(prev, ring));
                }
                rings.Add(ring);
            }
            if (before >= 0) Add(StageWallRing(pts, before, s, side));
            for (int k = 0; k < stations; k++) Add(StageWallRing(pts, WrapIdx(from + k, n), s, side));
            if (after >= 0) Add(StageWallRing(pts, after, s, side));

            int R = rings.Count;
            var fb = new Vector3[R]; var ft = new Vector3[R];
            var bt = new Vector3[R]; var bb = new Vector3[R];
            var along = new float[R];
            for (int k = 0; k < R; k++)
            {
                var g = rings[k];
                Vector3 outw = g.right * side;
                Vector3 front = g.centre + outw * (half + g.faceE + StageWallFaceIn);
                Vector3 back = front + outw * StageWallDrawThick;
                fb[k] = new Vector3(front.x, g.baseY, front.z);
                ft[k] = new Vector3(front.x, g.topY, front.z);
                bt[k] = new Vector3(back.x, g.topY, back.z);
                bb[k] = new Vector3(back.x, g.baseY, back.z);
                if (k > 0)
                {
                    Vector3 step = ft[k] - ft[k - 1]; step.y = 0f;
                    along[k] = along[k - 1] + step.magnitude;
                }
            }

            // THE STONE HAS A FRONT, A TOP AND A BACK, each on its own vertices
            // (both windings on one ribbon cancel in RecalculateNormals — see
            // BuildWalls). It was a single road-facing sheet: invisible from
            // the valley, from the end of a run, and from anywhere a car that
            // got behind it could be.
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const float Tex = 3.2f;
            Vector2 UV(float u, Vector3 p, int k) => new Vector2(u / Tex, (p.y - (rings[k].centre.y - 0.45f)) / Tex);
            for (int k = 1; k < R; k++)
            {
                Vector3 toRoad = -(rings[k - 1].right + rings[k].right) * side;
                Vector3 away = -toRoad;
                WallQuad(verts, uvs, tris, fb[k - 1], ft[k - 1], ft[k], fb[k],
                         UV(along[k - 1], fb[k - 1], k - 1), UV(along[k - 1], ft[k - 1], k - 1),
                         UV(along[k], ft[k], k), UV(along[k], fb[k], k), toRoad);
                WallQuad(verts, uvs, tris, ft[k - 1], bt[k - 1], bt[k], ft[k],
                         new Vector2(along[k - 1] / Tex, 0f), new Vector2(along[k - 1] / Tex, StageWallDrawThick / Tex),
                         new Vector2(along[k] / Tex, StageWallDrawThick / Tex), new Vector2(along[k] / Tex, 0f), Vector3.up);
                WallQuad(verts, uvs, tris, bb[k - 1], bt[k - 1], bt[k], bb[k],
                         UV(along[k - 1], bb[k - 1], k - 1), UV(along[k - 1], bt[k - 1], k - 1),
                         UV(along[k], bt[k], k), UV(along[k], bb[k], k), away);
            }
            // End caps, so the run's end reads as a block of masonry.
            for (int e = 0; e < 2; e++)
            {
                int k = e == 0 ? 0 : R - 1, k2 = e == 0 ? Mathf.Min(1, R - 1) : Mathf.Max(R - 2, 0);
                Vector3 outward = ft[k] - ft[k2]; outward.y = 0f;
                if (outward.sqrMagnitude < 1e-6f) continue;
                WallQuad(verts, uvs, tris, fb[k], ft[k], bt[k], bb[k],
                         UV(0f, fb[k], k), UV(0f, ft[k], k),
                         UV(StageWallDrawThick, bt[k], k), UV(StageWallDrawThick, bb[k], k), outward);
            }

            // THE COLLIDER: ONE SOLID PER RUN, built from the same rings.
            //
            // It was a box per chord (a box spanning four stations cuts the
            // corner — at the stage's 27 m minimum radius a 16 m chord sags
            // 1.2 m inside the wall line, which the obstacle audit reported as
            // an invisible face in the kerb band at 151 spots), each running
            // 0.25 m past its stations to overlap the next. Every one of those
            // joints put the next box's square END FACE across the plane a car
            // slides along: 5-48 mm proud on the inside of every bend, where
            // the chain is convex to the road; ~20 mm where a tight inside's
            // set-back switched on, because each chord computed its own; a
            // metre-tall riser at every buried terminal, where the box top
            // jumped from the lowered stone to the full invisible height; and
            // flush but REAL on the straights and the drag bridges, where a car
            // pressed into the wall by its contact offset met the next end face
            // within one step. PhysX suppresses contacts only on edges internal
            // to one triangle mesh, so each of those was a dead stop.
            //
            // BuildWallSolid makes the run one closed, 1.2 m-deep concave
            // mesh: a traffic face, top, back and bottom, capped only at the
            // run's own ends. (The single-sided ribbon, rejected here once for
            // letting cars nose through, is not this: this is closed and
            // StageWallCollThick deep, every face wound outward.)
            //
            // Per ring, ONE contact offset, which both chords that share the
            // ring use — the old per-chord extras disagreed at a shared station
            // and that disagreement was a step:
            //  * a station ring stands at StageWallContactE, the line the walled
            //    shoulder is run to — the drawn face moved out by the sag its
            //    bend gives a one-station chord on the outside (so the chord
            //    between two rings meets, and never leads, the stone), and set
            //    back on a tight inside;
            //  * a half-station ring takes the MEAN of its two stations'
            //    extras, so the face between them is straight in offset.
            // Heights per ring: the footing is the lowest of the ring's and its
            // neighbours' along the run's chords, 0.1 m under (never a window
            // under the stone); the top is the ring's own collTopY — the stone
            // plus the invisible extra where the stone is full height, the
            // lowered stone alone on a buried terminal — so the collider ramps
            // down across the chord into a terminal instead of standing over its
            // lowered stone, and no riser is left where the box tops used to
            // step. NOT "full only where both neighbours are full too": that
            // dropped the last full-height station before every flare to the
            // bare 0.85 m stone, and the deck-rail self-test's 0.8 m rail cast
            // went over the top at the end of every bridge approach (Blue
            // Ridge, Langston, Atlantic Beach, both loops) — a wall a fast car
            // could be lifted over, exactly where the approach rail is for.
            //
            // A chord with less stone over the ground at its face than
            // StageWallCollMinH gets no collider (a kerb of stone the shoulder
            // ribbon rides over), and splits the run: each side of it is its
            // own solid, with its own caps.
            float[] extra = new float[R];
            {
                var order = new List<int>(stations + 2);
                if (before >= 0) order.Add(before);
                for (int k = 0; k < stations; k++) order.Add(WrapIdx(from + k, n));
                if (after >= 0) order.Add(after);
                int q = 0;
                for (int k = 0; k < R; k++)
                    if (!rings[k].mid)
                        extra[k] = StageWallContactE(pts, order[q++], s, side) - rings[k].faceE;
                // A mid ring is only ever inserted BETWEEN two station rings.
                for (int k = 1; k + 1 < R; k++)
                    if (rings[k].mid) extra[k] = 0.5f * (extra[k - 1] + extra[k + 1]);
            }
            bool ChordSolid(int k) =>   // the chord from ring k-1 to ring k
                Mathf.Min(rings[k - 1].collTopY, rings[k].collTopY)
                - Mathf.Max(rings[k - 1].groundY, rings[k].groundY) >= StageWallCollMinH;
            int sub = 0;
            for (int k = 1; k < R; )
            {
                if (!ChordSolid(k)) { k++; continue; }
                int k0 = k - 1, k1 = k;               // rings k0..k1 of this sub-run
                while (k1 + 1 < R && ChordSolid(k1 + 1)) k1++;
                var faceBottom = new List<Vector3>(k1 - k0 + 1);
                var faceTop = new List<Vector3>(k1 - k0 + 1);
                var outward = new List<Vector3>(k1 - k0 + 1);
                for (int r = k0; r <= k1; r++)
                {
                    var g = rings[r];
                    Vector3 face = g.centre + g.right * (side * (half + g.faceE + extra[r]));
                    float bottom = g.baseY;
                    if (r > k0) bottom = Mathf.Min(bottom, rings[r - 1].baseY);
                    if (r < k1) bottom = Mathf.Min(bottom, rings[r + 1].baseY);
                    float top = g.collTopY;
                    faceBottom.Add(new Vector3(face.x, bottom - 0.1f, face.z));
                    faceTop.Add(new Vector3(face.x, top, face.z));
                    outward.Add(g.right * side);
                }
                BuildWallSolid("WallColl", parent, faceBottom, faceTop, outward, StageWallCollThick,
                               false, phys, "CollGuard" + no + "_" + sub++);
                k = k1 + 1;
            }
            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray(),
            };
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
        // A mountain road has a valley on one side and a hillside on the other.
        // Where the land RISES the road is cut into it, and a cut has a shape:
        // the compact section's ditch at the foot, a face of blasted rock, and
        // the hill carrying on up from the face's crest.
        //
        // It used to be only the face — a ribbon drawn from the road side,
        // standing on the corridor's flat shelf — so behind every cut lay a
        // level bench out to 16 m that you could not see from the road and
        // could not see the rock from, and every run ended in a collider chord
        // 3 cm over the tarmac. Now the face stands at the foot of the ditch
        // (CutToeE), the lattice behind it is released back to the hill
        // (RoadsideDy), and a solid rock top covers the ground in between: from
        // the crest back over the hillside, down onto the released lattice at
        // its far edge. A run's ends taper the face into the land and fade the
        // top in, rather than stopping at a box.
        //
        // Walls win where both could stand (the plan defers faces to BUILT
        // walls), and a face and a wall overlap by a station where one hands
        // over to the other.

        /// <summary>Shortest face worth building. Under a metre it is a kerb
        /// rather than a cut — you would drive up it — and a hillside that
        /// gentle is graded instead (StageBenchDy).</summary>
        const float BankRiseM = 0.9f;
        /// <summary>Tallest face built. Past this you are looking at a rock
        /// wall the fog will hide the top of anyway, and the collider becomes
        /// more expensive than the mountain behind it.</summary>
        const float BankMaxH = 5.5f;
        /// <summary>Metres the face leans back per metre of height. 0.5 is a
        /// 2:1 cut — steeper than earth stands but exactly what a blasted
        /// Appalachian road cut looks like. The rock top rises no steeper than
        /// this from the crest either.</summary>
        const float BankBatter = 0.5f;
        /// <summary>
        /// How much of the face is VERTICAL before the batter starts.
        ///
        /// A battered face and a box collider disagree by construction: the
        /// rock leans away with height and the box does not, so a collider
        /// seated on the toe stands half a metre in front of the stone by the
        /// time you reach a car's shoulder line, and one seated where the rock
        /// is at shoulder height lets a bumper into the base of it. 1.4 m of
        /// vertical plinth is the band a car on its wheels can touch at all, so
        /// inside that band the drawn rock and the solid rock are one surface
        /// and the argument goes away. Above it the batter carries on exactly
        /// as before, which is the part you look at rather than hit — and a
        /// blasted cut is near-vertical at the base in any case, with the
        /// batter reading as the weathered slope above it.
        /// </summary>
        const float BankPlinth = 1.4f;
        /// <summary>A chord whose lower end has less face than this gets no
        /// box: the face there is a tapered end, and the rock top's own
        /// MeshCollider (which carries the face too) is the whole of it. The
        /// old floor was a 0.15 m box on every run end — 3 cm over the tarmac,
        /// passable outward and a ledge coming back.</summary>
        const float BankCollMinH = 0.5f;
        /// <summary>Where the rock top is sampled, metres past the face's toe.
        /// The last is where it has come down onto the released lattice
        /// (BankPinM + BankReleaseM + BankTopTailM).</summary>
        static readonly float[] BankTopSampleE = { 2f, 4f, 6f, 9f, 12f, 15f, 18f, 21f, 24f, 27f, 30f };
        /// <summary>A rock-top point belongs to this station's hillside only
        /// while the nearest road to it is within this many stations and on
        /// the same side. Past that it is another leg of a switchback, whose
        /// own section the lattice follows there.</summary>
        const int BankTopOwnStations = 8;

        static void BuildStageBanks(List<Vector3> pts, Transform parent)
        {
            EnsureStageRoadside(pts);
            if (rsBankRuns == null) return;
            string tex = System.IO.File.Exists(StageGenDir + "/CutBank.png")
                       ? StageGenDir + "/CutBank.png" : theme.wall;
            var mat = MakeMat(MeshPrefix + "Bank", tex, affine: 0f);
            // The rock top IS the hillside: the near ground's own material
            // (BuildStageGround makes the same asset and registers its
            // seasons, which SeasonDress applies to every renderer wearing it)
            // and the same world-metre UVs, so the top and the lattice beyond
            // it are one surface to look at.
            bool sandy = surfNear != null && !string.IsNullOrEmpty(theme.sand);
            var topMat = MakeMat(MeshPrefix + "Ground", theme.ground, affine: 0f,
                                 tint: sandy ? (Color?)null : theme.groundTint);
            var phys = GetOrCreatePhysMat("WallPhys", 0.05f, 0.05f);
            var root = new GameObject("Banks");
            root.transform.SetParent(parent, false);

            int runs = 0, banked = 0;
            int solids0 = wallSolidCount;
            float solidM0 = wallSolidM;
            foreach (var run in rsBankRuns)
            {
                BuildOneStageBank(pts, run.stations, run.side, root.transform, mat, topMat, phys, runs++);
                banked += run.stations.Length;
            }
            Log($"Stage cut banks: {runs} runs closing {banked * Spacing:0} m of uphill shoulder, " +
                "each with a solid rock top over its released hillside; " +
                $"{wallSolidCount - solids0} rock-face solid(s), {wallSolidM - solidM0:0} m of collider face.");
        }

        /// <summary>Is <paramref name="p"/> still this station's own hillside —
        /// nearest to a station within BankTopOwnStations, on this side?</summary>
        static bool BankTopOwns(Vector3 p, int i, float side)
        {
            float reach = RoadWidth * 0.5f + CutToeE + BankTopSampleE[BankTopSampleE.Length - 1] + 8f;
            if (!StageCorridor(p.x, p.z, reach, out RoadFoot foot)) return true;
            int near = WrapIdx(Mathf.RoundToInt(foot.station), stageWp.Count);
            return foot.side == side && StationSep(near, i, stageWp.Count) <= BankTopOwnStations;
        }

        /// <summary>
        /// The rock top over one station, as (e, world y) from the crest at
        /// <paramref name="crestE"/> outward. It follows the hill (never rising
        /// steeper than the face's batter from the crest), comes down over
        /// BankTopTailM onto the lattice and tucks RoadsideRules.ToeTuckM under
        /// it, so a car that reaches it from the hillside rides one surface onto
        /// the other. Where the lattice is not yet released (a run's ends) the
        /// base keeps to the lattice instead. It stops early — collapsing onto
        /// its last point — at a tight inside, and where the hillside becomes
        /// another road's.
        ///
        /// SHAPED (the default), the base is then held between two lines:
        ///   * it never drops off its own crest faster than
        ///     RoadsideRules.RecoverableSlope — or at all, where the run
        ///     <paramref name="hold"/>s its height into a wall or a portal —
        ///     because a top that fell away behind its crest toward a lattice
        ///     still pinned under the road was a ridge with a moat behind it:
        ///     EDGE SLOPE 1V:2.6H-3.6H from 3.0 m out, and road-height land
        ///     1.5 m behind a face;
        ///   * it comes down to its own last point no steeper than
        ///     RoadsideRules.TraversableSlope, so where it stops early the edge
        ///     it stops at is a slope onto the lattice and not a step.
        /// The second line binds the samples, not the crest: FinishStageCuts
        /// lands every crest no higher than the first two samples under it, so
        /// the top never drops off the crest to meet it.
        /// Unshaped, it is the raw base: what FinishStageCuts lands a crest on
        /// (with the second line applied there), and a closing station's
        /// section under its lattice.
        /// </summary>
        static Vector2[] BankTopRing(List<Vector3> pts, int i, float side, float hh, float crestE, float release,
                                     bool hold, bool shaped = true)
        {
            float half = RoadWidth * 0.5f;
            int count = BankTopSampleE.Length;
            var ring = new Vector2[count + 1];
            float crestY = pts[i].y + hh;
            ring[0] = new Vector2(crestE, crestY);
            float farE = CutToeE + BankTopSampleE[BankTopSampleE.Length - 1];
            float limitE = farE;
            if (TightInside(pts, i, side, out float r)) limitE = Mathf.Min(limitE, r - half - 1f);
            float lastE = crestE, lastY = crestY;
            bool stopped = false;
            for (int k = 0; k < count; k++)
            {
                float e = Mathf.Max(CutToeE + BankTopSampleE[k], crestE + 0.5f * (k + 1));
                Vector3 p = pts[i] + rsRight[i] * (side * (half + e));
                if (!stopped && (e > limitE || !BankTopOwns(p, i, side)))
                {
                    stopped = true;
                    Vector3 q = pts[i] + rsRight[i] * (side * (half + lastE));
                    lastY = Mathf.Min(lastY, StageLatticeY(q.x, q.z) - RoadsideRules.ToeTuckM);
                }
                if (stopped) { ring[k + 1] = new Vector2(lastE, lastY); continue; }
                float lattice = StageLatticeY(p.x, p.z);
                float hill = Mathf.Min(StageDemY(p.x, p.z), crestY + (e - crestE) / BankBatter);
                float keep = release * (1f - Mathf.SmoothStep(0f, 1f,
                                 Mathf.InverseLerp(farE - BankTopTailM, farE, e)));
                float y = Mathf.Lerp(lattice - RoadsideRules.ToeTuckM, hill, keep);
                ring[k + 1] = new Vector2(e, y);
                lastE = e; lastY = y;
            }
            if (!shaped) return ring;

            // The last point is the anchor both lines are drawn to — the tail
            // tucked under the lattice, or the point an early stop collapsed
            // onto — and stays where it is.
            Vector2 far = ring[count];
            float fall = hold ? 0f : RoadsideRules.RecoverableSlope;
            for (int k = 1; k < count; k++)
            {
                float y = Mathf.Max(ring[k].y, crestY - (ring[k].x - crestE) * fall);
                ring[k].y = Mathf.Min(y, far.y + (far.x - ring[k].x) * RoadsideRules.TraversableSlope);
            }
            return ring;
        }

        static void BuildOneStageBank(List<Vector3> pts, int[] run, float side, Transform parent,
                                      Material mat, Material topMat, PhysicsMaterial phys, int no)
        {
            int n = pts.Count, s = SideIx(side);
            float half = RoadWidth * 0.5f;

            // The stations the cut is drawn at: the run; a portal's plane where
            // the run ends into a tunnel (full height to the portal face, its
            // top held there); and, where the run ends into open graded land, a
            // CLOSING station one past it — that station's cross-section with
            // every point tucked under its own lattice — so the rock top comes
            // down along the road onto the land the cut ends in, rather than
            // ending in an edge that stands over it.
            //
            // A run that ends into a WALL closes the same way. Its end is held
            // (rsCutHold): the top stands at the crest all the way back to the
            // tail, over a lattice that is still pinned under the road there
            // and over the bench RoadsideDy grades behind the wall's stone — so
            // left open, the last station's top was a sheet edge metres over
            // the ground behind the wall, with a cave under it a car behind the
            // stone could drive into. Closed, the top and the end of the face
            // come down along the road onto that ground inside one chord,
            // behind (and through) the flared stone that stands in front of
            // them.
            var st = new List<int>(run.Length + 2);
            var hh = new List<float>(run.Length + 2);
            var rel = new List<float>(run.Length + 2);
            var graded = new List<bool>(run.Length + 2);
            var held = new List<bool>(run.Length + 2);
            var closing = new List<bool>(run.Length + 2);
            void Station(int i, float h, float r, bool g, bool hold, bool close)
            {
                st.Add(i); hh.Add(h); rel.Add(r); graded.Add(g); held.Add(hold); closing.Add(close);
            }
            int first = run[0], last = run[run.Length - 1];
            if (RunEnd((run[0], run.Length), 0, n, out _, out int b0))
            {
                if (hasTunnels && tunnelIn[b0]) Station(b0, rsFaceH[s][first], 0f, rsCutGraded[s][first], true, false);
                else if (rsKind[s][b0] == Roadside.Open || rsKind[s][b0] == Roadside.Walled)
                    Station(b0, 0f, 0f, true, false, true);
            }
            foreach (int i in run) Station(i, rsFaceH[s][i], rsRelease[s][i], rsCutGraded[s][i], rsCutHold[s][i], false);
            if (RunEnd((run[0], run.Length), 1, n, out _, out int b1))
            {
                if (hasTunnels && tunnelIn[b1]) Station(b1, rsFaceH[s][last], 0f, rsCutGraded[s][last], true, false);
                else if (rsKind[s][b1] == Roadside.Open || rsKind[s][b1] == Roadside.Walled)
                    Station(b1, 0f, 0f, true, false, true);
            }
            int R = st.Count;

            // The face: toe (a skirt under the ditch's backslope), the top of
            // the vertical plinth, the battered crest — all three ON the crest
            // where the cut is graded, which the shoulder's own backslope climbs
            // to. v runs up the FACE so the rock does not stretch where it
            // leans back.
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // The rock top's collider carries the face as well: where no box
            // stands (a graded station either end of the chord, or less rock
            // than BankCollMinH) the face that is drawn must still be solid.
            var cVerts = new List<Vector3>();
            var cTris = new List<int>();
            var tVerts = new List<Vector3>();
            var tUvs = new List<Vector2>();
            var tTris = new List<int>();
            int M = BankTopSampleE.Length + 1;
            float dist = 0f;
            var toe = new Vector3[R]; var plinthTop = new Vector3[R]; var crest = new Vector3[R];
            for (int k = 0; k < R; k++)
            {
                int i = st[k];
                float h = hh[k];
                Vector3 outw = rsRight[i] * side;
                Vector3 atToe = pts[i] + outw * (half + CutToeE);
                Vector2[] ring;
                if (closing[k])
                {
                    float y = StageLatticeY(atToe.x, atToe.z) - RoadsideRules.ToeTuckM;
                    toe[k] = plinthTop[k] = crest[k] = new Vector3(atToe.x, y, atToe.z);
                    ring = BankTopRing(pts, i, side, y - pts[i].y, CutToeE, 0f, false, false);
                }
                else if (graded[k])
                {
                    float rise = Mathf.Max(0f, h - RoadLift);
                    float crestE = CutToeE + rise / RoadsideRules.BackSlope;
                    crest[k] = pts[i] + outw * (half + crestE);
                    crest[k].y = pts[i].y + RoadLift + rise;
                    toe[k] = plinthTop[k] = crest[k];
                    ring = BankTopRing(pts, i, side, RoadLift + rise, crestE, rel[k], held[k]);
                }
                else
                {
                    float plinth = Mathf.Min(h, BankPlinth);
                    float crestE = CutToeE + (h - plinth) * BankBatter;
                    toe[k] = atToe; toe[k].y = pts[i].y - 0.5f;
                    plinthTop[k] = atToe; plinthTop[k].y = pts[i].y + plinth;
                    crest[k] = pts[i] + outw * (half + crestE);
                    crest[k].y = pts[i].y + h;
                    ring = BankTopRing(pts, i, side, h, crestE, rel[k], held[k]);
                }

                if (k > 0)
                {
                    Vector3 step = toe[k] - toe[k - 1]; step.y = 0f;
                    dist += step.magnitude;
                }
                int v = verts.Count;
                verts.Add(toe[k]); verts.Add(plinthTop[k]); verts.Add(crest[k]);
                float vSh = (plinthTop[k].y - toe[k].y) / 4.5f;
                float vTop = vSh + Vector3.Distance(plinthTop[k], crest[k]) / 4.5f;
                uvs.Add(new Vector2(dist / 4.5f, 0f));
                uvs.Add(new Vector2(dist / 4.5f, vSh));
                uvs.Add(new Vector2(dist / 4.5f, vTop));
                if (k > 0)
                {
                    // Facing the road: the back of the face is inside the rock
                    // top now, which is drawn and solid. Between two graded
                    // stations there is no face, and nothing is drawn.
                    QuadFacingSkipFlat(verts, tris, v - 3, v - 2, v + 1, v, -outw);
                    QuadFacingSkipFlat(verts, tris, v - 2, v - 1, v + 2, v + 1, -outw);
                }

                int c = cVerts.Count;
                cVerts.Add(toe[k]); cVerts.Add(plinthTop[k]); cVerts.Add(crest[k]);
                if (k > 0)
                {
                    QuadFacingSkipFlat(cVerts, cTris, c - 3, c - 2, c + 1, c, -outw);
                    QuadFacingSkipFlat(cVerts, cTris, c - 2, c - 1, c + 2, c + 1, -outw);
                }

                // The rock top over this station — the forest's too, except
                // over a tube. A closing station's ring (under its lattice) is
                // registered as well, never over a cut's own: without it the
                // forest read the end station's top across the whole closing
                // chord, and a held top stands metres over the slope that
                // actually comes down there — trees in the air.
                int topKey = s * RsStationKey + i;
                if (!(hasTunnels && tunnelIn[i]) && !(closing[k] && rsBankTop.ContainsKey(topKey)))
                    rsBankTop[topKey] = ring;
                int t = tVerts.Count;
                for (int j = 0; j < M; j++)
                {
                    Vector3 p = pts[i] + outw * (half + ring[j].x);
                    p.y = ring[j].y;
                    if (j == 0) p = crest[k];
                    tVerts.Add(p);
                    tUvs.Add(new Vector2(p.x / theme.groundTile, p.z / theme.groundTile));
                }
                if (k > 0)
                    for (int j = 0; j + 1 < M; j++)
                        QuadFacingSkipFlat(tVerts, tTris, t - M + j, t - M + j + 1, t + j + 1, t + j, Vector3.up);
            }

            // THE ROCK FACE'S SOLID: one closed MeshCollider per stretch of
            // faced chords (BuildWallSolid), where there was a box per station
            // chord overlapping the next by 0.25 m each way. Cuts sit mostly on
            // the inside of a bend — the road wraps a spur — where that chain is
            // convex to the road and every joint put the next box's square end
            // 18-48 mm proud of the face a car slides on: a dead stop every
            // 40-180 m of cut. One mesh has no joints to be proud of.
            //
            // Chords, not a box spanning four stations, for the reason the
            // wall's comment gives: that cuts the corner and ends up inside the
            // kerb band on the stage's tightest radius. Only between two FACED
            // stations: a graded one has no rock at the toe to stand a collider
            // in front of, and one there would be an invisible wall across a
            // backslope.
            //
            // AS TALL AS THE ROCK IS AND NO TALLER. This was Max(hh,
            // StageWallCollH) — the guard wall's 1.7 m collider height, borrowed
            // on the reasoning that a barrier ought to be a barrier. On a cut
            // bank it is a force field, because the face tapers to nothing at
            // both ends of every run: measured off the built scenes, 1.0 km of
            // Mount Mitchell's shoulder carried a collider taller than its rock,
            // 180 m of it where the drawn face was 0.15 m. Reported as
            // "invisible wall on edge of road that knocked me off the track". A
            // chord stands only where both its stations have BankCollMinH of
            // face, and each ring's top is the LEAST face of the chords either
            // side of it: the solid is straight between rings where the drawn
            // ribbon is a ramp, so erring short leaves at worst a hand's breadth
            // of rock it does not reach (the rock top's collider does), and
            // erring tall puts the fault back. Footings as the boxes had them,
            // 0.6 m under the road, the lower of the two chords at a ring.
            //
            // Seated so the INNER face lands 0.05 m inside the drawn toe, and
            // every millimetre of its depth grows into the hill
            // (StageWallCollThick's seating rule).
            bool BankChord(int k) =>   // the chord from station k-1 to station k
                !graded[k] && !graded[k - 1] && Mathf.Min(hh[k - 1], hh[k]) >= BankCollMinH;
            float seatE = half + CutToeE - 0.05f;
            int sub = 0;
            for (int k = 1; k < R; )
            {
                if (!BankChord(k)) { k++; continue; }
                int k0 = k - 1, k1 = k;
                while (k1 + 1 < R && BankChord(k1 + 1)) k1++;
                var faceBottom = new List<Vector3>(k1 - k0 + 1);
                var faceTop = new List<Vector3>(k1 - k0 + 1);
                var outward = new List<Vector3>(k1 - k0 + 1);
                for (int r = k0; r <= k1; r++)
                {
                    int i = st[r];
                    Vector3 face = pts[i] + rsRight[i] * (side * seatE);
                    float bottom = float.MaxValue, ch = hh[r];
                    if (r > k0)
                    {
                        bottom = Mathf.Min(bottom, (pts[st[r - 1]].y + pts[i].y) * 0.5f);
                        ch = Mathf.Min(ch, hh[r - 1]);
                    }
                    if (r < k1)
                    {
                        bottom = Mathf.Min(bottom, (pts[st[r + 1]].y + pts[i].y) * 0.5f);
                        ch = Mathf.Min(ch, hh[r + 1]);
                    }
                    faceBottom.Add(new Vector3(face.x, bottom - 0.6f, face.z));
                    faceTop.Add(new Vector3(face.x, pts[i].y + ch, face.z));
                    outward.Add(rsRight[i] * side);
                }
                BuildWallSolid("BankColl", parent, faceBottom, faceTop, outward, StageWallCollThick,
                               false, phys, "CollBank" + no + "_" + sub++);
                k = k1 + 1;
            }

            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray(),
            };
            SaveMesh(mesh, "StageBank" + no);
            var go = new GameObject("Bank" + no);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;

            // THE ROCK TOP: drawn in the hillside's material, and solid — its
            // collider is the top AND the face, so nothing about this cut is a
            // surface drawn from one side only.
            if (tTris.Count == 0) return;
            var top = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = tVerts.ToArray(), uv = tUvs.ToArray(), triangles = tTris.ToArray(),
            };
            // "BankTopMesh", not "StageBank...": a surface a car can drive on
            // stays out of CompressibleMesh's 16-bit quantisation.
            SaveMesh(top, "BankTopMesh" + no);
            int off = cVerts.Count;
            cVerts.AddRange(tVerts);
            foreach (int ix in tTris) cTris.Add(ix + off);
            var coll = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = cVerts.ToArray(), triangles = cTris.ToArray(),
            };
            SaveMesh(coll, "BankTopColl" + no);
            var topGo = new GameObject("BankTop" + no);
            topGo.transform.SetParent(parent, false);
            topGo.AddComponent<MeshFilter>().sharedMesh = top;
            topGo.AddComponent<MeshRenderer>().sharedMaterial = topMat;
            var mc = topGo.AddComponent<MeshCollider>();
            mc.sharedMesh = coll;
            // The shell's friction off the road, like the ground it continues.
            mc.sharedMaterial = SlidePhys();
            topGo.isStatic = true;
        }

        /// <summary><see cref="QuadFacing"/>, skipping a quad with no area —
        /// a rock top that stopped early collapses its last samples onto one
        /// point, and a MeshCollider has no use for slivers.
        ///
        /// The winding is chosen by the WHOLE quad's normal (both triangles'
        /// crosses summed), not QuadFacing's first triangle alone. A cut's
        /// quads are routinely half degenerate — a graded station's toe,
        /// plinth and crest are one point, and so is a closing station's whole
        /// section — and where the degenerate half came first its zero normal
        /// could not say which way to wind the half that is real: a rock face
        /// wedge wound away from the road, unlit from it and invisible to a
        /// ray cast from it (queries do not hit back faces). For a planar quad
        /// the sum points the same way the first cross does, so nothing that
        /// was right changes. The degenerate half itself is left out: a
        /// triangle with no area lights nothing, collides with nothing, and
        /// leaves its lone vertex a zero normal for SaveMesh to warn about.</summary>
        static void QuadFacingSkipFlat(List<Vector3> verts, List<int> tris, int a, int b, int c, int d, Vector3 wantNormal)
        {
            Vector3 n1 = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            Vector3 n2 = Vector3.Cross(verts[c] - verts[a], verts[d] - verts[a]);
            bool t1 = n1.sqrMagnitude >= 1e-8f, t2 = n2.sqrMagnitude >= 1e-8f;
            if (!t1 && !t2) return;
            bool keep = Vector3.Dot(n1 + n2, wantNormal) >= 0f;
            if (t1) { if (keep) tris.AddRange(new[] { a, b, c }); else tris.AddRange(new[] { a, c, b }); }
            if (t2) { if (keep) tris.AddRange(new[] { a, c, d }); else tris.AddRange(new[] { a, d, c }); }
        }

        /// <summary>The rock top's height at a point on the stage, or negative
        /// infinity off any rock top — the forest stands its trees on the
        /// higher of this and the lattice.</summary>
        static float StageBankTopY(in RoadFoot foot)
        {
            if (rsBankTop == null || rsBankTop.Count == 0) return float.NegativeInfinity;
            int s = SideIx(foot.side);
            float e = foot.d - RoadWidth * 0.5f;
            float y0 = rsBankTop.TryGetValue(s * RsStationKey + foot.s0, out var r0) ? RingY(r0, e) : float.NegativeInfinity;
            float y1 = rsBankTop.TryGetValue(s * RsStationKey + foot.s1, out var r1) ? RingY(r1, e) : float.NegativeInfinity;
            if (float.IsNegativeInfinity(y0) || float.IsNegativeInfinity(y1)) return Mathf.Max(y0, y1);
            return Mathf.Lerp(y0, y1, foot.t);
        }

        static float RingY(Vector2[] ring, float e)
        {
            if (e < ring[0].x) return float.NegativeInfinity;
            for (int j = 1; j < ring.Length; j++)
            {
                if (e > ring[j].x) continue;
                float span = ring[j].x - ring[j - 1].x;
                return span < 1e-4f ? ring[j - 1].y : Mathf.Lerp(ring[j - 1].y, ring[j].y, (e - ring[j - 1].x) / span);
            }
            return float.NegativeInfinity;
        }

        // ------------------------------------------------------------------
        //  Tunnels
        // ------------------------------------------------------------------
        /// <summary>Height of the tube's ceiling over the road, and how far
        /// its walls stand outside the tarmac edge: the verge strip, and a
        /// little. The Little Switzerland Tunnel is a two-lane bore.</summary>
        const float TunnelH = 5.2f, TunnelWallOut = 1.1f;
        /// <summary>How far past the tarmac edge a ground quad is still "near
        /// the tube" (<see cref="InTunnelZone"/>) and has to be measured
        /// before it is built (<see cref="TunnelQuadClear"/>).</summary>
        const float TunnelHoleMargin = 7f;
        /// <summary>Past the tube's wall, how far the vertex hole itself
        /// reaches (<see cref="InTunnelHole"/>).</summary>
        const float TunnelFootprintMarginM = 1f;
        /// <summary>How far over the tube's ceiling a kept near-tube facet
        /// must stand wherever it is over the bore.</summary>
        const float TunnelCoverM = 1f;
        /// <summary>The portal face: a rock front either side of the mouth and
        /// over it, big enough to hide the ground's step from the approach
        /// cut to the ridge.</summary>
        const float PortalHalfW = 18f, PortalH = 16f;
        /// <summary>How high over the road a portal's side panels are solid —
        /// well past anything a car can reach; the rest of the face is
        /// scenery.</summary>
        const float PortalCollH = TunnelH + 1.5f;
        const float TunnelTexM = 6f;

        /// <summary>
        /// A tube round the road wherever it passes under the mountain: two
        /// walls, a ceiling, a rock portal face at each mouth, and one wall
        /// solid (BuildWallSolid) down each wall. The road, its kerb strips and
        /// its colliders run through unchanged — the tube is what the ground
        /// mesh's hole (GridChunkMesh) is covered by from inside, and the
        /// portal faces are what covers it from outside.
        /// </summary>
        static void BuildStageTunnels(List<Vector3> pts, Transform parent)
        {
            if (!hasTunnels) return;
            var runs = StationRuns(tunnelIn, 3);
            if (runs.Count == 0) return;
            string tex = File.Exists(ProjectRootPath(StageGenDir + "/CutBank.png"))
                       ? StageGenDir + "/CutBank.png" : theme.wall;
            var mat = MakeMat(MeshPrefix + "Tunnel", tex, affine: 0f, tint: new Color(0.62f, 0.60f, 0.58f));
            var phys = GetOrCreatePhysMat("WallPhys", 0.05f, 0.05f);
            var root = new GameObject("Tunnels");
            root.transform.SetParent(parent, false);
            int no = 0, metres = 0;
            int solids0 = wallSolidCount;
            foreach (var run in runs)
            {
                BuildOneTunnel(pts, run.from, run.len, root.transform, mat, phys, no++);
                metres += run.len * (int)Spacing;
            }
            Log($"Stage tunnels: {no} tube(s), {metres} m bored, {wallSolidCount - solids0} wall solid(s).");
        }

        static void BuildOneTunnel(List<Vector3> pts, int from, int stations, Transform parent,
                                   Material mat, PhysicsMaterial phys, int no)
        {
            int n = pts.Count;
            float halfW = RoadWidth * 0.5f + TunnelWallOut;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float dist = 0f;
            var ring = new int[stations, 4];

            for (int k = 0; k < stations; k++)
            {
                int i = WrapIdx(from + k, n);
                Vector3 right = RightAt(pts, i);
                Vector3 c = pts[i];
                float y0 = c.y - 0.45f, y1 = c.y + TunnelH;
                Vector3 l = c - right * halfW, r = c + right * halfW;
                float u = dist / TunnelTexM;
                ring[k, 0] = verts.Count; verts.Add(new Vector3(l.x, y0, l.z)); uvs.Add(new Vector2(u, 0f));
                ring[k, 1] = verts.Count; verts.Add(new Vector3(l.x, y1, l.z)); uvs.Add(new Vector2(u, TunnelH / TunnelTexM));
                ring[k, 2] = verts.Count; verts.Add(new Vector3(r.x, y1, r.z)); uvs.Add(new Vector2(u, TunnelH / TunnelTexM + halfW * 2f / TunnelTexM));
                ring[k, 3] = verts.Count; verts.Add(new Vector3(r.x, y0, r.z)); uvs.Add(new Vector2(u, TunnelH / TunnelTexM * 2f + halfW * 2f / TunnelTexM));
                dist += Spacing;

                if (k > 0)
                {
                    // Every face turned INWARD: the player is inside the tube.
                    Vector3 inwardL = right, inwardR = -right, down = Vector3.down;
                    QuadFacing(verts, tris, ring[k - 1, 0], ring[k - 1, 1], ring[k, 1], ring[k, 0], inwardL);
                    QuadFacing(verts, tris, ring[k - 1, 1], ring[k - 1, 2], ring[k, 2], ring[k, 1], down);
                    QuadFacing(verts, tris, ring[k - 1, 2], ring[k - 1, 3], ring[k, 3], ring[k, 2], inwardR);
                }
            }

            // The tube's walls, solid: ONE closed MeshCollider down each wall
            // (BuildWallSolid), seated on the drawn face less 0.05 m and grown
            // OUTWARD — the guard wall's rule — as tall as the drawn ceiling and
            // 0.4 m under the road. It was a box per station chord overlapping
            // the next by 0.25 m each way, and every joint laid a square end
            // face across the plane a car scraping the tube wall slides on; one
            // mesh has no joints.
            //
            // At each PORTAL ring the face sits on the drawn wall itself rather
            // than 0.05 m in front of it, easing back in just inside the mouth. An
            // approach guard wall meets the tube at the portal plane with its
            // own face on that line (StageVerge less StageWallFaceIn, plus its
            // bend's sag), and the tube's end cap standing 0.05 m in front of
            // it was a real step — 52 mm at Little Switzerland's st 998, where a
            // car scraping the approach wall into the tube stopped dead.
            //
            // The ease is over the portal's own depth (StageWallCollThick, a
            // ring of its own), not the whole first chord. The mouth's
            // WallPortal boxes reach that far into the tube with their inner
            // edge on the drawn wall line, so their inner END FACE — square
            // across the tube wall, facing a car on its way OUT — would stand
            // only 0.05 x 1.2 / 4 = 15 mm behind a face eased over the whole
            // 4 m chord: inside the 20 mm the two shapes' contact offsets add
            // up to, a dead stop waiting at every exit for a car scraping the
            // tube wall (and proud of the face outright on the inside of a bend
            // tighter than ~160 m, where the chord falls away from the box's
            // straight edge). Back on the contract line by the end of the box,
            // it is the 50 mm it always was.
            foreach (float side in new[] { -1f, 1f })
            {
                var faceBottom = new List<Vector3>(stations + 2);
                var faceTop = new List<Vector3>(stations + 2);
                var outward = new List<Vector3>(stations + 2);
                void Ring(Vector3 centre, Vector3 right, float faceW, float bottom, float top)
                {
                    Vector3 face = centre + right * (side * faceW);
                    faceBottom.Add(new Vector3(face.x, bottom - 0.4f, face.z));
                    faceTop.Add(new Vector3(face.x, top, face.z));
                    outward.Add(right * side);
                }
                // The ring StageWallCollThick in from the portal station kp,
                // on the chord toward its neighbour kn: back on the contract
                // line, with the chord's footing and the drawn ceiling there.
                void PortalDepthRing(int kp, int kn)
                {
                    int ip = WrapIdx(from + kp, n), inb = WrapIdx(from + kn, n);
                    Vector3 chord = pts[inb] - pts[ip]; chord.y = 0f;
                    float t = Mathf.Min(0.5f, StageWallCollThick / Mathf.Max(chord.magnitude, 1e-3f));
                    Vector3 c = Vector3.Lerp(pts[ip], pts[inb], t);
                    Vector3 right = Vector3.Lerp(RightAt(pts, ip), RightAt(pts, inb), t).normalized;
                    Ring(c, right, halfW - 0.05f, (pts[ip].y + pts[inb].y) * 0.5f, c.y + TunnelH);
                }
                for (int k = 0; k < stations; k++)
                {
                    int i = WrapIdx(from + k, n);
                    bool portal = k == 0 || k == stations - 1;
                    if (k == stations - 1) PortalDepthRing(k, k - 1);
                    float bottom = float.MaxValue;
                    if (k > 0) bottom = Mathf.Min(bottom, (pts[WrapIdx(from + k - 1, n)].y + pts[i].y) * 0.5f);
                    if (k < stations - 1) bottom = Mathf.Min(bottom, (pts[WrapIdx(from + k + 1, n)].y + pts[i].y) * 0.5f);
                    Ring(pts[i], RightAt(pts, i), portal ? halfW : halfW - 0.05f, bottom, pts[i].y + TunnelH);
                    if (k == 0) PortalDepthRing(0, 1);
                }
                BuildWallSolid("WallTunnel", parent, faceBottom, faceTop, outward, StageWallCollThick,
                               false, phys, "CollTnl" + no + (side < 0f ? "L" : "R"));
            }

            // The portals: a rock face across the mouth with the tube's
            // opening left in it. Facing OUT of the mountain at each end.
            Portal(pts, WrapIdx(from, n), -1f, halfW, verts, uvs, tris);
            Portal(pts, WrapIdx(from + stations - 1, n), 1f, halfW, verts, uvs, tris);
            PortalColliders(pts, WrapIdx(from, n), -1f, halfW, parent, phys);
            PortalColliders(pts, WrapIdx(from + stations - 1, n), 1f, halfW, parent, phys);

            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray(),
            };
            SaveMesh(mesh, "Tunnel" + no);
            var go = new GameObject("Tunnel" + no);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        /// <summary>The face at one mouth: two side panels from the tube wall
        /// out to PortalHalfW, and a lintel over the opening up to PortalH,
        /// all in the station's own plane, all facing <paramref name="dir"/>
        /// along the road (-1 back toward the approach at the entry mouth,
        /// +1 onward at the exit).</summary>
        static void Portal(List<Vector3> pts, int i, float dir, float halfW,
                           List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            Vector3 right = RightAt(pts, i);
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Vector3 face = fwd * dir;
            Vector3 c = pts[i];
            float yBase = c.y - 1.5f, yTop = c.y + PortalH, yLintel = c.y + TunnelH;
            void Panel(float x0, float x1, float y0, float y1)
            {
                int v = verts.Count;
                verts.Add(new Vector3(c.x + right.x * x0, y0, c.z + right.z * x0)); uvs.Add(new Vector2(x0 / TunnelTexM, y0 / TunnelTexM));
                verts.Add(new Vector3(c.x + right.x * x0, y1, c.z + right.z * x0)); uvs.Add(new Vector2(x0 / TunnelTexM, y1 / TunnelTexM));
                verts.Add(new Vector3(c.x + right.x * x1, y1, c.z + right.z * x1)); uvs.Add(new Vector2(x1 / TunnelTexM, y1 / TunnelTexM));
                verts.Add(new Vector3(c.x + right.x * x1, y0, c.z + right.z * x1)); uvs.Add(new Vector2(x1 / TunnelTexM, y0 / TunnelTexM));
                QuadFacing(verts, tris, v, v + 1, v + 2, v + 3, face);
            }
            Panel(-PortalHalfW, -halfW, yBase, yTop);
            Panel(halfW, PortalHalfW, yBase, yTop);
            Panel(-halfW, halfW, yLintel, yTop);
        }

        /// <summary>
        /// The portal's side panels, solid. They were drawn only: a car that
        /// left the approach beside the mouth drove straight through the rock
        /// face into the ground hole behind it and out of the world. A box per
        /// panel, from the tube's wall out to PortalHalfW, its contact face on
        /// the drawn plane and its depth grown INTO the mountain (the guard
        /// wall's seating rule), so the drawing and the solid are one surface.
        /// </summary>
        static void PortalColliders(List<Vector3> pts, int i, float dir, float halfW,
                                    Transform parent, PhysicsMaterial phys)
        {
            Vector3 right = RightAt(pts, i);
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Vector3 into = -fwd * dir;          // the face looks along fwd * dir
            float width = PortalHalfW - halfW;
            float y0 = pts[i].y - 1.5f, y1 = pts[i].y + PortalCollH;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 p = pts[i] + right * (side * (halfW + width * 0.5f)) + into * (StageWallCollThick * 0.5f);
                var seg = new GameObject("WallPortal");
                seg.transform.SetParent(parent, false);
                seg.transform.position = new Vector3(p.x, (y0 + y1) * 0.5f, p.z);
                seg.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                var box = seg.AddComponent<BoxCollider>();
                box.size = new Vector3(width, y1 - y0, StageWallCollThick);
                box.sharedMaterial = phys;
                seg.layer = SolidLayer;
                seg.isStatic = true;
            }
        }

        /// <summary>Two triangles for a quad, wound so the face points the
        /// way <paramref name="wantNormal"/> does. Corner order is otherwise
        /// free — which is the whole point: every inside-out strip this
        /// project has shipped came from a winding chosen by hand.</summary>
        static void QuadFacing(List<Vector3> verts, List<int> tris, int a, int b, int c, int d, Vector3 wantNormal)
        {
            Vector3 nrm = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            bool flip = Vector3.Dot(nrm, wantNormal) < 0f;
            if (!flip) { tris.AddRange(new[] { a, b, c, a, c, d }); }
            else { tris.AddRange(new[] { a, c, b, a, d, c }); }
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
            // tree022 IS red AND green, on purpose: one sheet, a copper crown on
            // the left and a green one on the right (55% green by pixel, 20%
            // red). Seen from both sides it looks like two trees in one, and it
            // was asked about ("why are two different tree models getting
            // combined as one?") and then KEPT by the owner: "It's fine.
            // tree022 is red and green. I just wanted to make sure trees
            // weren't being cross contaminated." They are not: both cards of
            // every tree read this one cell, which FoliageAudit checks tree
            // for tree. Do not swap it for a "cleaner" red.
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
                AssetDatabase.CreateFolder(StageShareDir, "Gen");
            // The CC0 tree pack is 16 PNGs copied out of a folder on this
            // machine. A stage with no forest must not need it to exist — the
            // island builds on a checkout that has never seen the pack.
            if (!theme.stageForest) return;
            if (!AssetDatabase.IsValidFolder(StageTreesDir))
                AssetDatabase.CreateFolder(StageShareDir, "Trees");
            int copied = 0;
            foreach (var s in StageTrees)
            {
                string dst = ProjectRootPath(StageTreesDir + "/" + s.file + ".png");
                if (File.Exists(dst)) continue;
                string src = Path.Combine(TreesSrcDir, s.file + ".png");
                if (!File.Exists(src)) throw new Exception("Tree source missing: " + src);
                File.Copy(src, dst);
                copied++;
            }
            if (copied > 0) Log($"Copied {copied} tree billboards from the CC0 pack.");
            EnsureSeasonTrees();
        }

        /// <summary>The 4x4, 512px tree atlas, composed from the copied pack
        /// billboards — one material for the whole forest is what keeps ten
        /// thousand trees at a few dozen draw calls.</summary>
        static void GenerateStageTextures()
        {
            if (theme.stageUrban) { GenerateUrbanTextures(); return; }
            if (!theme.stageForest) { GenerateCoastTextures(); return; }

            // All five seasons of it, each billboard slid until its painted
            // trunk stands where the trunk collider does, plus their grounds.
            ComposeForestAtlases();
            WriteSeasonGroundTextures();

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
        /// The Charlotte set: ONE texture, the shoulder strip, because the
        /// ground is the city circuit's own JPEG and there is no far forest
        /// to paint. A stage's strip is its verge (KerbStyleFor answers Verge
        /// for every stage) and BuildKerbs draws it with THIS file — so an
        /// urban stage gets a concrete curb by drawing its verge as one:
        /// weathered concrete (RG2's ConcreteOld, the colour the city's own
        /// bridge decks wear), a dark seam down each long edge where it meets
        /// tarmac and grass, and a joint every metre. The strip is laid at
        /// one repeat per 2 m, so a joint at x = 0 and x = 16 is a metre of
        /// slab. Not the raised StreetKerb section — the stage's guard wall
        /// and falling verge were built round a flat strip.
        /// </summary>
        static void GenerateUrbanTextures()
        {
            var slab = SurfaceBase[(int)CityMeshes.Surface.ConcreteOld];
            WriteTexture(StageGenDir + "/Shoulder.png", 32, 16, (x, y) =>
            {
                int grain = (int)((Noise(x + 31, y + 7) - 0.5f) * 20f);
                int shade = grain;
                if (y == 0 || y == 15) shade -= 34;              // the seams
                if (x == 0 || x == 1 || x == 16 || x == 17) shade -= 30;  // 1 m joints
                return new Color32((byte)Mathf.Clamp(slab.r + shade, 0, 255),
                                   (byte)Mathf.Clamp(slab.g + shade, 0, 255),
                                   (byte)Mathf.Clamp(slab.b + shade, 0, 255), 255);
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

        /// <summary>Clear of the trunk of the nearest tree past the section.</summary>
        const float StageTreeClearM = 1f;

        /// <summary>Is a point on the roadside the plan graded — a shoulder
        /// and foreslope out to their catch, a wall with its posts, a cut's
        /// ditch and face up to the crest? No tree stands there: the section is
        /// the clear zone, and its lattice is held under a ribbon a trunk
        /// would stand beneath.</summary>
        static bool OnGradedRoadside(in RoadFoot foot)
        {
            if (rsKind == null) return false;
            int s = SideIx(foot.side);
            int st = WrapIdx(Mathf.RoundToInt(foot.station), stageWp.Count);
            float e = foot.d - RoadWidth * 0.5f, clear;
            switch (rsKind[s][st])
            {
                case Roadside.Open: clear = rsCatchE[s][st]; break;
                case Roadside.Walled:
                    clear = rsWallE[s][st] + StageWallFaceIn + StageWallDrawThick + StagePostGap + StagePostW;
                    // A buried terminal is graded open out to its catch.
                    if (BuriedTerminal(s, st)) clear = Mathf.Max(clear, rsCatchE[s][st]);
                    break;
                case Roadside.Cut:
                    // To the crest: past a graded cut's backslope as well.
                    clear = CutCrestE(s, st);
                    break;
                default: return false;
            }
            return e < clear + StageTreeClearM;
        }

        static void BuildStageForest(List<Vector3> pts, Transform parent)
        {
            var root = new GameObject("Forest");
            root.transform.SetParent(parent, false);

            // Both faces drawn, in every season's dress — see PSX/Lit's _Cull.
            var mat = MakeMat(MeshPrefix + "Forest", StageGenDir + "/TreeAtlas.png", cutoff: 0.5f, twoSided: true);
            RegisterSeasonalTexture(MeshPrefix + "Forest", mat, DressAtlasPath, "forest", cutoff: 0.5f,
                                    twoSided: true);
            var rng = new System.Random(41);
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);

            float roadHalf = RoadWidth * 0.5f;
            int planted = 0, cliffSkip = 0, chunks = 0, underDeck = 0;
            // Every tree's trunk, from the very position its billboard is
            // planted at: base in world space and a radius off its card. Too
            // many to bake a collider each — TreeTrunks stands them up round
            // the cars at runtime.
            var trunks = new List<Vector4>();
            var trunkCells = new List<byte>();

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
                        // Drawn for every candidate, kept or not, so moving a
                        // threshold never reshuffles the trees that stay.
                        double keepRoll = rng.NextDouble(), storyRoll = rng.NextDouble();

                        if (!StageCorridor(wx, wz, ForestBand + 10f, out RoadFoot foot)) continue;
                        float d = foot.d, roadY = foot.roadY, f = BridgeAt(foot.station);
                        if (d > ForestBand) continue;

                        // Thick where the driver sees into it, the old forest's
                        // spacing behind that, thinner again toward the band's
                        // end - the mottle takes over anyway.
                        float keep = d <= ForestDenseTo ? 1f
                                   : d <= 90f ? Mathf.Lerp(1f, ForestFarKeep, (d - ForestDenseTo) / (90f - ForestDenseTo))
                                   : ForestFarKeep * (1f - Mathf.InverseLerp(90f, ForestBand, d) * 0.55f);
                        if (keepRoll >= keep) continue;

                        // On a cut's rock top where there is one: the lattice
                        // under it is held down for a dozen metres behind the
                        // face, and a tree stood on that is buried to its crown.
                        //
                        // And on the LATTICE, not the field it samples: the
                        // built near ground is flat triangles between 12 m
                        // vertices, which on a falling verge lie a metre off the
                        // field between them, and PrepareStageLattice has since
                        // lowered some near-road vertices by up to a metre and a
                        // half. A trunk stood on the field there floated past its
                        // own sink. ForestBand is well inside NearCoverage, so
                        // every tree stands on a near chunk.
                        float ground = Mathf.Max(StageLatticeY(wx, wz), StageBankTopY(foot));

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
                        float h = sp.height * ForestHeightScale * (0.85f + (float)rng.NextDouble() * 0.4f);
                        // The understory: young trees under the canopy, where
                        // the roadside forest is thick enough to have one.
                        if (d <= 90f && storyRoll < ForestUnderstory)
                            h *= 0.5f + (float)(storyRoll / ForestUnderstory) * 0.2f;

                        bool deckOver = f > 0.4f && roadY - ground > h + 3.5f;

                        // NO BILLBOARD ACROSS THE LANE AT A HEIGHT A CAR OR ITS
                        // CAMERA REACHES. A card is a flat plane sixteen metres
                        // wide, and a tree seven metres from the centreline puts
                        // the end of one over the tarmac. High up, that is the
                        // canopy closing over the road and is the look that was
                        // asked for. But a tree on a FALLING verge has its whole
                        // crown at road level - the first thick forest put orange
                        // leaves through the guard wall into the lane at eye
                        // height - so unless the foliage starts well clear of
                        // anything driving under it, the tree is shrunk until its
                        // card stays on its own side of the edge line, and done
                        // without if that leaves a shrub.
                        float halfCard = StageTreeWidth(sp, h) * 0.5f;
                        if (!deckOver && d - halfCard < roadHalf + 0.4f)
                        {
                            float foliageFoot = ground + h * (sp.conifer ? 0.10f : 0.28f);
                            if (foliageFoot - roadY < 4.2f)
                            {
                                float fit = (d - roadHalf - 0.4f) / halfCard;
                                if (fit < 0.6f) continue;
                                h *= fit;
                            }
                        }

                        // NO LEAVES AT BUMPER HEIGHT OVER THE TARMAC. A third of
                        // the species carry foliage to the ground in some season,
                        // metres wide, and inside it a car is dragged down
                        // (TreeTrunks.Brush). That is a hazard for whoever leaves
                        // the road, never for whoever is on it: such a tree stands
                        // back by its own low crown plus a car's width.
                        float lowReach = Mathf.Min(TreeTrunks.MaxBrush, ForestLowReachFrac(idx) * StageTreeWidth(sp, h));
                        if (!deckOver && lowReach > 0f && d < roadHalf + 1.8f + lowReach) continue;
                        if (deckOver)
                        {
                            // Down on the slope with the deck riding over the
                            // canopy — the Linn Cove look. Any lateral offset
                            // is fine; the tree is metres BELOW the road.
                            underDeck++;
                        }
                        else if (d < roadHalf + 2.6f || (d < StageWallOffset + 1.2f && f > 0.35f)
                                 || OnGradedRoadside(foot))
                        {
                            // On the tarmac, the shoulder, or through a
                            // parapet. 2.6 m past the kerb is the same margin
                            // the circuits give their tree line — and past that,
                            // nothing on the graded roadside the plan laid out
                            // (a foreslope's clear zone, a wall and its posts,
                            // a cut's ditch and face).
                            continue;
                        }

                        planted++;
                        AddTreeQuads(verts, uvs, tris, sp,
                            new Vector3(wx - ox, ground - 0.25f, wz - oz), h,
                            (float)rng.NextDouble() * 360f);
                        // The card's WIDTH, and the cell it wears: the table
                        // takes the trunk's radius (and, per season, how far the
                        // low foliage reaches) off what the billboard paints.
                        trunks.Add(new Vector4(wx, ground - 0.25f, wz, StageTreeWidth(sp, h)));
                        trunkCells.Add((byte)idx);
                    }

                if (verts.Count == 0) return;
                var mesh = new Mesh
                {
                    // A full chunk of the dense band is 1024 trees, 8192
                    // corners: sixteen bits of index, and half the index bytes.
                    indexFormat = verts.Count < 65000 ? UnityEngine.Rendering.IndexFormat.UInt16
                                                      : UnityEngine.Rendering.IndexFormat.UInt32,
                    vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray(),
                };
                var go = ChunkGO(root.transform, "Forest_" + cx + "_" + cz, mesh, new[] { mat },
                                 ox, oz, "StageForest_" + cx + "_" + cz);
                go.layer = FoliageLayer;
                chunks++;
            });

            root.AddComponent<TreeTrunks>().SetForest(trunks, trunkCells, ForestTrunkFrac, ForestBrushFrac);

            Log($"Stage forest: {planted} trees in {chunks} chunks " +
                $"({underDeck} under bridge decks, {cliffSkip} sites left bare as cliff), " +
                $"{trunks.Count} trunks in the table.");
        }

        /// <summary>A stage tree's card width: its height for a broadleaf
        /// crown, narrower for a spire.</summary>
        static float StageTreeWidth(TreeSpecies sp, float h) => h * (sp.conifer ? 0.62f : 1.0f);

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

        /// <summary>One tree: two crossed quads, four corners each in the order
        /// bottom-left, top-left, top-right, bottom-right — TreeKit.VertsPerTree
        /// reads the forest back by that layout, so the audit can check every
        /// trunk in the table against the billboard it belongs to.</summary>
        static void AddTreeQuads(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                 TreeSpecies sp, Vector3 basePos, float h, float yawDeg)
        {
            float w = StageTreeWidth(sp, h);
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
                // One winding; the forest material draws both faces. (This
                // said each plane "is seen from behind via the other plane of
                // the cross". From behind BOTH planes it is not seen at all.)
                tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
        }
    }
}
