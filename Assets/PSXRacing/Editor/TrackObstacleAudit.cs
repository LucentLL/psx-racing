using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Finds colliders a car can hit that the player cannot see coming.
    ///
    /// Written for a report of "invisible barriers that stopped my car, many of
    /// them around the track", and widened after a second one: finding all of
    /// them by driving is exactly the job a computer should be doing instead.
    ///
    /// TWO BANDS, because a car goes to both of them:
    ///
    ///   ON TRACK  — the tarmac plus its kerb. Nothing solid belongs here at
    ///               all; anything that is, is a wall across the racing line.
    ///   RUN-OFF   — kerb to barrier. On these circuits that is 4 m of gravel
    ///               either side, and running wide onto it is a normal part of
    ///               a lap. The first version of this audit stopped at the kerb
    ///               and called every circuit clean, which is how a barrier
    ///               standing in the gravel survived it: the audit was not
    ///               wrong about the tarmac, it just never looked past it.
    ///
    /// Works off collider geometry rather than physics queries: no baked physics
    /// scene is needed and the answer cannot silently be "nothing" because the
    /// scene was not simulated.
    ///
    /// Menu: PSX Racing/Audit Track Obstacles.
    /// </summary>
    public static class TrackObstacleAudit
    {
        /// <summary>Only things low enough to hit. A bridge deck 8 m up is not a
        /// barrier, it is scenery.</summary>
        const float ClearHeight = 3.0f;

        /// <summary>How far under the road surface still counts. A car sits ON
        /// the road, so anything whose TOP is below it cannot be struck from the
        /// side — that is what a bridge pier is, holding the deck up from ten
        /// metres down. Half a metre of slack for the verge, which is graded
        /// slightly below the tarmac.</summary>
        const float BelowRoad = 0.6f;

        // The run-off band reaches to the venue's own barrier line, pulled in
        // 0.4 m: a wall segment is a straight chord between waypoints 4 m
        // apart, so on the inside of a hairpin its midpoint sits a decimetre
        // nearer the centreline than the waypoints it was built from, and
        // reporting the barrier against itself would bury everything else.
        // Per-venue via PSXRacingBuilder.WallOffsetFor — see AuditOne.

        [MenuItem("PSX Racing/Audit Track Obstacles")]
        public static void Run()
        {
            var log = new StringBuilder();
            foreach (var def in TrackCatalog.Scened) AuditOne(def, log);

            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_obstacle_audit.txt"),
                log.ToString());
        }

        static void AuditOne(TrackCatalog.TrackDef def, StringBuilder log)
        {
            string scenePath = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
            if (!System.IO.File.Exists(scenePath))
            {
                log.AppendLine("MISSING SCENE " + scenePath);
                return;
            }
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count == 0)
            {
                log.AppendLine("no TrackPath in " + scene.name);
                return;
            }

            // The road is per-circuit — the dock is 10.5 m wide and the airfield
            // 14 — so both bands are measured off THIS track, never off a
            // constant that happens to be right for the city one. The barrier
            // line is per-venue too: the stage's guard walls hug the shoulder
            // at 5.9 m, and measuring it to the circuits' 10 m line would
            // report the stage's own masonry as an obstacle.
            float trackHalf = path.roadWidth * 0.5f + PSXRacingBuilder.KerbWidth;
            // A drag strip is 18 m of tarmac inside a barrier line drawn at 10 m,
            // so there the kerb reaches PAST the wall and there is no run-off at
            // all. Never let the outer band come in behind the inner one, or the
            // report claims to have audited less ground than it did.
            float reachHalf = Mathf.Max(PSXRacingBuilder.WallOffsetFor(def) - 0.4f, trackHalf);

            log.AppendLine("");
            log.AppendLine("track obstacle audit — " + scene.name);
            log.AppendLine("  on track: +/-" + trackHalf.ToString("0.0") +
                           " m (tarmac + kerb)   run-off: out to +/-" +
                           reachHalf.ToString("0.0") + " m (the barrier line)");

            // Group by the offending object's path so 292 wall boxes report as
            // one line rather than 292.
            var offenders = new Dictionary<string, Offense>();
            var unmeasured = new SortedSet<string>();
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            int considered = 0;

            // Can a car actually GET to the pumps?
            //
            // Everything else in this file asks whether something solid stands
            // where the player drives. This asks the opposite question, and it
            // is the one the fuel stop fails silently at: a forecourt is only a
            // feature if there is a way in, and the way in is a hole in a
            // barrier that is GENERATED. A driveway that did not get cut, or a
            // station collider that grew over the approach, leaves a pump
            // nobody can reach — and nothing else here would say a word.
            AuditForecourt(path, colliders, log);
            AuditFacing(path, log);
            AuditVerge(def, path, trackHalf, reachHalf, log);
            AuditSurface(path, trackHalf, log);
            AuditPosts(def, path, trackHalf, log);
            AuditGhostBarriers(def, path, colliders, trackHalf, log);

            foreach (var col in colliders)
            {
                if (col == null || col.isTrigger) continue;
                // Cars are on layer 2 (Ignore Raycast) and are supposed to be on
                // the track — they are the traffic, not an obstacle.
                if (col.gameObject.layer == 2) continue;
                if (col.GetComponentInParent<CarController>() != null) continue;

                // A concave MeshCollider has no ClosestPoint: Unity hands back
                // the query point unchanged, which reads as "touching the
                // centreline" for every one of them. Falling back to the
                // bounding box says the same thing for anything the centreline
                // runs through. So they are not measurable here, and pretending
                // otherwise is what filled the old report with the road, the
                // ground and every bridge deck — all three of which are surfaces
                // you are supposed to be driving on. Named, counted, skipped.
                var mc = col as MeshCollider;
                if (mc != null && !mc.convex) { unmeasured.Add(Key(col.transform)); continue; }
                considered++;

                var b = col.bounds;

                // Walk the waypoints near this collider and measure how close it
                // reaches to the centreline.
                float nearest = float.MaxValue;
                int worstIdx = -1;
                for (int i = 0; i < path.Count; i++)
                {
                    Vector3 c = path.GetPoint(i);
                    // Cheap reject: the bounds cannot matter if the waypoint is
                    // nowhere near them.
                    if (Mathf.Abs(c.x - b.center.x) > b.extents.x + reachHalf + 1f) continue;
                    if (Mathf.Abs(c.z - b.center.z) > b.extents.z + reachHalf + 1f) continue;
                    // Height is measured against THIS PIECE OF ROAD, not against
                    // sea level. The circuits climb — the mountain pass by 28 m
                    // — so a single absolute ceiling either skips every barrier
                    // on the high side of the track or reports the floor of a
                    // gorge as an overhead obstruction.
                    if (b.min.y > c.y + ClearHeight) continue;   // overhead only
                    if (b.max.y < c.y - BelowRoad) continue;     // under the road

                    Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                    // Closest point ON THE COLLIDER, not on its bounding box.
                    // A yawed BoxCollider's world AABB is its diagonal, so
                    // measuring the box would report every rotated building as
                    // ~40% wider than it is — which is the same mistake that
                    // caused the original bug, and it would make the fix look
                    // like it had not worked.
                    Vector3 closest = col.ClosestPoint(c);
                    float lateral = Mathf.Abs(Vector3.Dot(closest - c, right));
                    float along = Mathf.Abs(Vector3.Dot(closest - c, path.GetTangent(i)));
                    if (along > path.spacing) continue;       // belongs to another waypoint

                    if (lateral < nearest) { nearest = lateral; worstIdx = i; }
                }

                if (nearest >= reachHalf) continue;

                string key = Key(col.transform);
                if (!offenders.TryGetValue(key, out var o))
                    offenders[key] = o = new Offense { key = key, nearest = float.MaxValue };
                o.count++;
                if (nearest < o.nearest)
                {
                    o.nearest = nearest;
                    o.waypoint = worstIdx;
                    o.type = col.GetType().Name;
                    o.layer = LayerMask.LayerToName(col.gameObject.layer);
                    if (string.IsNullOrEmpty(o.layer)) o.layer = col.gameObject.layer.ToString();
                    o.hasRenderer = col.GetComponentInChildren<MeshRenderer>() != null;
                }
            }

            log.AppendLine("  checked " + considered + " measurable non-car colliders");
            foreach (var u in unmeasured)
                log.AppendLine("    not measurable (concave mesh — a surface, not an obstacle): " + u);

            if (offenders.Count == 0)
            {
                log.AppendLine("  CLEAR — nothing solid stands inside the barrier line");
                return;
            }

            var sorted = new List<Offense>(offenders.Values);
            sorted.Sort((a, b) => a.nearest.CompareTo(b.nearest));
            foreach (var o in sorted)
            {
                string band = o.nearest < trackHalf ? "ON TRACK" : "RUN-OFF ";
                log.AppendLine("  " + band + "  reaches to " + o.nearest.ToString("0.00") +
                               " m off the centreline  x" + o.count +
                               "  [" + o.type + ", layer " + o.layer +
                               (o.hasRenderer ? "" : ", NO RENDERER — invisible") +
                               "]  near wp " + o.waypoint + "  " + o.key);
            }
        }

        // ==================================================================
        //  Is the track furniture facing the driver?
        // ==================================================================
        /// <summary>
        /// Every single-sided surface beside the road is drawn once and has to
        /// be drawn the right way round, and the corner order that does that
        /// FLIPS with the side of the track — because "outward" and "toward the
        /// road" are opposite vectors on the two sides.
        ///
        /// `BuildRoadEdge` and `BuildOneStageWall` have always branched on
        /// side. `BuildKerbs` and the circuits' `BuildWalls` never did, so the
        /// LEFT kerb of every circuit faced downward and the LEFT barrier faced
        /// out over the scenery: from the driving seat there was no kerb and no
        /// wall on that side at all, only a collider that stopped you. It had
        /// been that way since the circuits were built and no screenshot showed
        /// it, because a picture of a missing thing looks like a picture of a
        /// track that has nothing there.
        ///
        /// Cheap to assert and impossible to see by eye, so it is asserted:
        /// road-like surfaces face UP, barriers face the centreline.
        /// </summary>
        static void AuditFacing(TrackPath path, StringBuilder log)
        {
            int checkedObjs = 0, wrong = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                string n = mf.gameObject.name;
                bool wantUp = n == "Road" || n == "KerbL" || n == "KerbR" || n == "RoadEdge";
                bool wantIn = n == "WallL" || n == "WallR";
                if (!wantUp && !wantIn) continue;

                var mesh = mf.sharedMesh;
                var v = mesh.vertices;
                var t = mesh.triangles;
                var xf = mf.transform;
                int bad = 0, seen = 0;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = xf.TransformPoint(v[t[i]]);
                    Vector3 b = xf.TransformPoint(v[t[i + 1]]);
                    Vector3 c = xf.TransformPoint(v[t[i + 2]]);
                    Vector3 nrm = Vector3.Cross(b - a, c - a);
                    if (nrm.sqrMagnitude < 1e-8f) continue;
                    nrm.Normalize();
                    Vector3 mid = (a + b + c) / 3f;
                    seen++;
                    if (wantUp) { if (nrm.y <= 0f) bad++; }
                    else
                    {
                        // Toward the road, measured in plan against the nearest
                        // point on the centreline.
                        Vector3 toRoad = path.GetPoint(path.NearestIndex(mid)) - mid;
                        toRoad.y = 0f;
                        if (toRoad.sqrMagnitude < 1e-6f) continue;
                        if (Vector3.Dot(new Vector3(nrm.x, 0f, nrm.z), toRoad.normalized) <= 0f) bad++;
                    }
                }
                if (seen == 0) continue;
                checkedObjs++;
                if (bad > 0)
                {
                    wrong++;
                    log.AppendLine("  BACKWARDS  " + n + " — " + bad + " of " + seen +
                                   " faces point " + (wantUp ? "DOWN" : "away from the road") +
                                   " and are invisible from the car");
                }
            }
            log.AppendLine(wrong == 0
                ? "  FACING OK — all " + checkedObjs + " single-sided surfaces face the driver"
                : "  FACING: " + wrong + " of " + checkedObjs + " surfaces are inside out");
        }

        // ==================================================================
        //  Can a car that ran wide get back on?
        // ==================================================================
        /// <summary>Lateral sample pitch across the run-off.</summary>
        const float VergeStep = 0.25f;
        /// <summary>
        /// Tallest step up a car can take at this pitch and still climb it.
        ///
        /// The wheels are 0.666 m across, so a 0.33 m obstacle is exactly axle
        /// height and a car meets it as a wall rather than as a ramp. Half of
        /// that is the line between "a jolt" and "you are not getting back on".
        /// </summary>
        const float VergeMaxStep = 0.17f;
        /// <summary>Waypoints between profiles. Every tenth is a section every
        /// 40 m, which is finer than any feature the ground grid can hold.
        /// </summary>
        const int VergeEvery = 10;
        /// <summary>How far past each end of a span the gorge still counts as
        /// the bridge's. Three waypoints — the abutment and its approach.
        /// </summary>
        const float AbutmentM = 12f;

        /// <summary>
        /// Walk a section across the run-off at intervals down the whole track
        /// and measure the biggest STEP UP a car driving back toward the
        /// centreline has to climb.
        ///
        /// The obstacle test above asks whether anything is standing in the
        /// run-off. This asks the other half of the same question, which
        /// nothing was asking: whether the run-off is a place you can leave.
        /// The ground beside the road is dug to the bottom of the road slab, so
        /// a car that runs wide lands in a trench — and because that dig is
        /// sampled by a nine-metre ground lattice, how deep the trench is
        /// varies along the track. That is why it was reported as SOME sections
        /// being impossible to drive back onto: the failure is real, it is
        /// everywhere, and it is only severe where the lattice happens to fall.
        /// </summary>
        /// <summary>
        /// The height of the SURFACE under a point — the thing a wheel would
        /// rest on, and nothing else.
        ///
        /// A plain downward raycast is not that. The first version of this
        /// walked into the grid and reported a 1.5 m step in the middle of the
        /// road, which is the roof of a parked car; a lamp arm or a tree canopy
        /// over the run-off would have done the same. The rule that separates
        /// them is the one the obstacle test above already uses from the other
        /// direction: a concave mesh collider is a surface (road, ground, deck,
        /// forecourt, kerb, verge), and everything solid enough to be an
        /// obstacle is a box, a capsule or a convex hull.
        /// </summary>
        static bool SurfaceUnder(Vector3 from, out float y) =>
            SurfaceUnder(from, 12f, out y, out _);

        static bool SurfaceUnder(Vector3 from, float reach, out float y, out Collider on)
        {
            y = 0f; on = null;
            var hits = Physics.RaycastAll(from, Vector3.down, reach, ~0,
                                          QueryTriggerInteraction.Ignore);
            bool found = false;
            foreach (var h in hits)
            {
                var mc = h.collider as MeshCollider;
                if (mc == null || mc.convex) continue;
                // Highest surface wins: that is the one the car stands on.
                if (!found || h.point.y > y) { y = h.point.y; on = h.collider; found = true; }
            }
            return found;
        }

        // ==================================================================
        //  The roadside posts: clear of the kerb, and dense enough to matter
        // ==================================================================
        /// <summary>Metres every post vertex must sit past the kerb's outer
        /// edge. The builder puts the verge post's inner face 0.31 m out;
        /// this is the fence under it.</summary>
        const float PostClearance = 0.2f;
        /// <summary>Posts per kilometre a circuit must carry, both sides and
        /// both kinds. 12 m verge pitch on two sides is 167/km and the wall
        /// seams add 250; a strip with no verge room has the seams alone.
        /// Under this, the sense-of-speed pass has silently not run.</summary>
        const float PostsPerKmMin = 60f;

        /// <summary>
        /// The posts carry no collider, so nothing else in this file can see
        /// them — and a post line that drifted onto the kerb would be a fence
        /// down the racing line that every audit called clear. Read the
        /// combined meshes' vertices directly, hold every one of them past the
        /// kerb, and count them against the venue's length. Stages are only
        /// reported: their posts follow the guard walls, which exist only
        /// where the mountain falls away.
        /// </summary>
        static void AuditPosts(TrackCatalog.TrackDef def, TrackPath path,
                               float trackHalf, StringBuilder log)
        {
            string[] names = { "PostsL", "PostsR", "WallPostsL", "WallPostsR", "StagePosts" };
            int posts = 0, bad = 0;
            float nearest = float.MaxValue;
            string worst = "";
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (System.Array.IndexOf(names, mf.name) < 0) continue;
                var verts = mf.sharedMesh.vertices;
                posts += verts.Length / PSXRacingBuilder.PostVerts;
                int hint = -1;
                foreach (var local in verts)
                {
                    Vector3 v = mf.transform.TransformPoint(local);
                    hint = path.NearestIndex(v, hint);
                    Vector3 c = path.GetPoint(hint);
                    Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(hint)).normalized;
                    float lateral = Mathf.Abs(Vector3.Dot(v - c, right));
                    if (lateral < nearest) { nearest = lateral; worst = mf.name + " near wp " + hint; }
                    if (lateral < trackHalf + PostClearance) bad++;
                }
            }

            if (posts == 0)
            {
                log.AppendLine(def.stage
                    ? "  posts: none (no guard-wall runs on this stage)"
                    : "  POSTS: NONE — the sense-of-speed pass did not run on this circuit");
                return;
            }
            float perKm = posts / Mathf.Max(def.LengthM / 1000f, 0.01f);
            log.AppendLine("  posts: " + posts + " (" + perKm.ToString("0") + "/km), nearest vertex " +
                           nearest.ToString("0.00") + " m off the centreline (" + worst + ")" +
                           (bad > 0 ? "  " + bad + " VERTICES INSIDE THE KERB + " +
                                      PostClearance.ToString("0.0") + " m BAND" : ""));
            if (!def.stage && perKm < PostsPerKmMin)
                log.AppendLine("  POSTS TOO SPARSE: " + perKm.ToString("0") + "/km, want " + PostsPerKmMin);
        }

        static void AuditVerge(TrackCatalog.TrackDef def, TrackPath path,
                               float trackHalf, float reachHalf, StringBuilder log)
        {
            if (reachHalf <= trackHalf + VergeStep)
            {
                log.AppendLine("  no run-off to profile (the barrier is on the kerb)");
                return;
            }

            float worst = 0f;
            int worstIdx = -1;
            float worstAt = 0f, worstSide = 0f;
            int profiles = 0, bad = 0, spans = 0;
            float lap = Mathf.Max(def.LengthM, 1f);

            for (int i = 0; i < path.Count; i += VergeEvery)
            {
                // A bridge is exempt, and honestly so. The run-off beside a
                // viaduct is a fourteen-metre gorge: there IS a step at the
                // deck edge, it is meant to be there, and no amount of grading
                // lets a car drive back up onto a bridge from underneath it.
                // Counted and reported rather than silently dropped — a silent
                // exemption is how an audit stops measuring the thing it was
                // written for.
                // Reaches PAST the span at both ends, because the abutment is
                // the point: BridgeBlend is zero at the first metre of a bridge
                // and the ground beside that station has already fallen into
                // the gorge the deck crosses. RidgePass's span starts at 920 m,
                // waypoint 230 lands exactly on it, and testing the station
                // alone exempted its neighbours and flagged the abutment.
                float s = Mathf.Repeat(i * TrackCatalog.Spacing, lap);
                bool overSpan = false;
                for (float o = -AbutmentM; o <= AbutmentM && !overSpan; o += AbutmentM)
                    if (TrackCatalog.BridgeBlend(def, Mathf.Repeat(s + o + lap, lap)) > 0.001f)
                        overSpan = true;
                if (overSpan) { spans++; continue; }

                Vector3 c = path.GetPoint(i);
                Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                profiles++;
                foreach (float side in new[] { -1f, 1f })
                {
                    float prevY = 0f;
                    bool havePrev = false;
                    bool sideBad = false;
                    // Outside in, the way a car drives back onto the track.
                    for (float d = reachHalf; d >= 0f; d -= VergeStep)
                    {
                        Vector3 probe = c + right * (side * d) + Vector3.up * 4f;
                        if (!SurfaceUnder(probe, out float y)) { havePrev = false; continue; }
                        if (havePrev)
                        {
                            float step = y - prevY;      // positive = climbing
                            if (step > worst) { worst = step; worstIdx = i; worstAt = d; worstSide = side; }
                            if (step > VergeMaxStep) sideBad = true;
                        }
                        prevY = y;
                        havePrev = true;
                    }
                    if (sideBad) bad++;
                }
            }

            string where = worstIdx >= 0
                ? " (wp " + worstIdx + ", " + worstAt.ToString("0.0") + " m " +
                  (worstSide < 0f ? "left" : "right") + " of the centreline)"
                : "";
            log.AppendLine("  run-off profile: " + profiles + " sections" +
                           (spans > 0 ? " (+" + spans + " over bridges, exempt)" : "") +
                           ", worst step up " + worst.ToString("0.00") + " m per " +
                           VergeStep.ToString("0.00") + " m" + where);
            log.AppendLine(bad == 0
                ? "  RE-ENTRY OK — nothing steeper than " + VergeMaxStep.ToString("0.00") +
                  " m stands between the run-off and the tarmac"
                : "  RE-ENTRY BLOCKED on " + bad + " of " + (profiles * 2) +
                  " half-sections — a car that runs wide there cannot climb back on");
        }

        // ==================================================================
        //  Is the racing surface itself smooth?
        // ==================================================================
        /// <summary>Sample pitch ALONG the road. A wheel is 0.666 m across, so
        /// anything shorter than this is a bump the tyre rolls over rather than
        /// a face it hits.</summary>
        const float SurfStep = 0.35f;
        /// <summary>
        /// Biggest rise per <see cref="SurfStep"/> that is still road.
        ///
        /// The steepest grade any of these routes is allowed is 8.5%, which is
        /// 3 cm over this pitch. 12 cm is four times that: a face a car meets
        /// at speed rather than a slope it climbs, and at 140 km/h a 12 cm ramp
        /// in 35 cm is a 19-degree launch pad.
        /// </summary>
        const float SurfMaxStep = 0.12f;
        /// <summary>How many distinct launch sites to name before summarising.
        /// One line per fault, not one per sample: a single crest of hillside
        /// through the tarmac is twenty consecutive samples.</summary>
        const int SurfReportMax = 12;
        /// <summary>Samples that have to come back clean before the next bad one
        /// counts as a NEW site rather than more of the same.</summary>
        const int SurfSiteGap = 6;

        /// <summary>
        /// Walk the driving surface in the direction of travel and find the
        /// steps a car would be launched off.
        ///
        /// <see cref="AuditVerge"/> asks this question ACROSS the run-off, and
        /// TerrainAudit asks whether the ground is above the tarmac at three
        /// points per waypoint. Neither is this. The stage has no run-off at all
        /// — its barrier stands on the kerb, so AuditVerge prints "nothing to
        /// profile" and returns — and three probes every 4.7 m cannot see a
        /// ridge of hillside that surfaces between two of them. The report was
        /// "sections of the parkway have mountains clipping through that launch
        /// cars into the air", on a track both of those audits called clean.
        ///
        /// So this one walks where the WHEELS go, at a third of a metre, over
        /// bridges as well (a step at a deck joint launches a car exactly as
        /// well as a step in the dirt), and names the collider it is standing on
        /// at the moment it climbs — which is the difference between a ground
        /// chunk through the road and a road that is genuinely that steep.
        /// </summary>
        static void AuditSurface(TrackPath path, float trackHalf, StringBuilder log)
        {
            // Five lanes: the centre, both wheel tracks, and both edges pulled
            // in far enough not to ride the kerb face. A ridge that surfaces on
            // the outside of a corner leaves the middle of the lane perfectly
            // clean — the same reason TerrainAudit samples across the width.
            float edge = Mathf.Max(0.5f, trackHalf - 0.5f);
            float[] lanes = { 0f, -edge * 0.55f, edge * 0.55f, -edge, edge };

            float worst = 0f;
            Vector3 worstAt = Vector3.zero;
            string worstOn = null;
            int bad = 0, samples = 0, sites = 0;
            var named = new List<string>();

            foreach (float lane in lanes)
            {
                float prevY = 0f;
                bool havePrev = false;
                int clean = SurfSiteGap;      // start a fresh site on the first fault
                float total = path.TotalLength;
                for (float s = 0f; s < total; s += SurfStep)
                {
                    float fi = s / path.spacing;
                    int i = Mathf.FloorToInt(fi);
                    float t = fi - i;
                    Vector3 c = Vector3.Lerp(path.GetPoint(i), path.GetPoint(i + 1), t);
                    Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                    Vector3 at = c + right * lane;

                    // From only just above the road: a ray dropped from 4 m up
                    // finds the underside of a bridge deck the road passes
                    // beneath and calls it a two-metre step.
                    if (!SurfaceUnder(at + Vector3.up * 1.2f, 3f, out float y, out var on))
                    { havePrev = false; clean = SurfSiteGap; continue; }
                    samples++;

                    if (havePrev)
                    {
                        float step = y - prevY;
                        if (step > worst) { worst = step; worstAt = at; worstOn = Name(on); }
                        if (step > SurfMaxStep)
                        {
                            bad++;
                            if (clean >= SurfSiteGap)
                            {
                                sites++;
                                if (named.Count < SurfReportMax)
                                    named.Add(string.Format(
                                        "    LAUNCH  {0:0.00} m rise in {1:0.00} m at wp {2} " +
                                        "({3:0.0} m {4} of the centreline), standing on {5}  " +
                                        "[{6:0.0}, {7:0.0}, {8:0.0}]",
                                        step, SurfStep, i, Mathf.Abs(lane),
                                        lane < 0f ? "left" : "right", Name(on),
                                        at.x, y, at.z));
                            }
                            clean = 0;
                        }
                        else clean++;
                    }
                    prevY = y;
                    havePrev = true;
                }
            }

            log.AppendLine("  surface profile: " + samples + " probes in " + lanes.Length +
                           " lanes, worst rise " + worst.ToString("0.00") + " m per " +
                           SurfStep.ToString("0.00") + " m" +
                           (worstOn != null ? " on " + worstOn : "") +
                           (worst > SurfMaxStep
                              ? string.Format(" [{0:0.0}, {1:0.0}]", worstAt.x, worstAt.z) : ""));
            if (bad == 0)
            {
                log.AppendLine("  SURFACE OK — nothing on the driving line rises more than " +
                               SurfMaxStep.ToString("0.00") + " m in " + SurfStep.ToString("0.00") + " m");
                return;
            }
            log.AppendLine("  SURFACE: " + sites + " launch site(s), " + bad + " of " + samples +
                           " probes climb a face");
            foreach (var line in named) log.AppendLine(line);
            if (sites > named.Count)
                log.AppendLine("    ... and " + (sites - named.Count) + " more not listed");
        }

        static string Name(Collider c) => c == null ? "?" : Key(c.transform);

        /// <summary>Half a car, and then some. The corridor a driver needs to
        /// get through an opening without scraping down one side of it.</summary>
        const float CarHalfWidth = 1.2f;
        /// <summary>Waypoints either side of the forecourt to look for a
        /// driveway in. Twenty is 80 m — wider than any apron this builds.
        /// </summary>
        const int DrivewaySearch = 20;
        /// <summary>Narrowest opening that counts as a driveway: three
        /// waypoints, 12 m. Anything less is a gap you would have to thread.
        /// </summary>
        const int DrivewayMinRun = 3;
        /// <summary>How far short of a pump the approach test stops. A car
        /// draws up ALONGSIDE a nozzle; the last few metres of any line drawn
        /// at one end inside the island it stands on.</summary>
        const float StopShortOfPump = 5f;

        static void AuditForecourt(TrackPath path, Collider[] colliders, StringBuilder log)
        {
            var pumps = Object.FindObjectsByType<GasPump>(FindObjectsSortMode.None);
            if (pumps.Length == 0) return;      // no forecourt on this circuit

            // The pump nearest the road is the one a driver would aim at.
            Transform target = null;
            float bestD = float.MaxValue;
            int pumpIdx = 0;
            foreach (var p in pumps)
            {
                int i = path.NearestIndex(p.transform.position);
                float d = Vector3.Distance(path.GetPoint(i), p.transform.position);
                if (d < bestD) { bestD = d; target = p.transform; pumpIdx = i; }
            }
            if (target == null) return;

            Vector3 tangentAt = path.GetTangent(pumpIdx);
            Vector3 rightAt = Vector3.Cross(Vector3.up, tangentAt).normalized;
            float side = Vector3.Dot(target.position - path.GetPoint(pumpIdx), rightAt) >= 0f ? 1f : -1f;

            // Walk the barrier line looking for a run of it that is not there.
            int runStart = -1, runLen = 0, bestStart = -1, bestLen = 0;
            for (int o = -DrivewaySearch; o <= DrivewaySearch; o++)
            {
                int i = path.Wrap(pumpIdx + o);
                Vector3 t = path.GetTangent(i);
                Vector3 r = Vector3.Cross(Vector3.up, t).normalized;
                Vector3 gate = path.GetPoint(i) + r * side * PSXRacingBuilder.WallOffset;
                bool open = !Blocked(colliders, gate, out _);
                if (open)
                {
                    if (runStart < 0) { runStart = o; runLen = 0; }
                    runLen++;
                    if (runLen > bestLen) { bestLen = runLen; bestStart = runStart; }
                }
                else { runStart = -1; runLen = 0; }
            }

            if (bestLen < DrivewayMinRun)
            {
                log.AppendLine("  FORECOURT WALLED IN — no opening of " +
                               (DrivewayMinRun * path.spacing).ToString("0") +
                               " m or more in the barrier beside " + pumps.Length + " pump(s)");
                return;
            }

            // From the middle of the widest opening, straight at each pump in
            // turn. ANY of them being reachable is the question — they are all
            // the same nozzle, and one tucked in behind the shop says nothing
            // about whether the player can buy fuel here.
            int gateIdx = path.Wrap(pumpIdx + bestStart + bestLen / 2);
            Vector3 gTan = path.GetTangent(gateIdx);
            Vector3 gRight = Vector3.Cross(Vector3.up, gTan).normalized;
            Vector3 from = path.GetPoint(gateIdx) + gRight * side * (PSXRacingBuilder.WallOffset - 2f);

            string firstBlocker = null;
            float firstAt = 0f;
            foreach (var pump in pumps)
            {
                Vector3 to = pump.transform.position;
                float dist = Vector3.Distance(from, to);
                // STOP SHORT. A car parks beside a pump, not on top of one, and
                // the pump's own island collider is at the end of every one of
                // these lines — walking all the way in reports the destination
                // as the obstacle, which is what the first version of this did.
                float reach = dist - StopShortOfPump;
                if (reach <= 1f) continue;

                bool clear = true;
                int steps = Mathf.Max(6, Mathf.CeilToInt(reach / 0.6f));
                for (int s = 1; s <= steps; s++)
                {
                    Vector3 p = from + (to - from).normalized * (reach * s / steps);
                    if (!Blocked(colliders, p, out string who)) continue;
                    clear = false;
                    if (firstBlocker == null)
                    {
                        firstBlocker = who;
                        firstAt = Vector3.Distance(from, p);
                    }
                    break;
                }

                if (!clear) continue;
                log.AppendLine("  FORECOURT REACHABLE — " + pumps.Length + " pump(s), a " +
                               (bestLen * path.spacing).ToString("0") +
                               " m opening in the barrier, and " + dist.ToString("0") +
                               " m of clear apron to the nearest nozzle");
                return;
            }

            log.AppendLine("  FORECOURT BLOCKED — no pump reachable; nearest attempt stopped " +
                           firstAt.ToString("0.0") + " m in from the barrier at " +
                           (firstBlocker ?? "nothing measurable"));
        }

        /// <summary>
        /// Is a car standing here touching something solid?
        ///
        /// Measured with <see cref="Collider.ClosestPoint"/>, not against the
        /// bounding box. The station's shop collider is a 42 m box YAWED to
        /// face the road, and on a circuit whose forecourt faces a diagonal its
        /// world-axis box is half as big again as the box itself — big enough
        /// to swallow the pumps standing in front of it and report a perfectly
        /// open forecourt as walled off. It did exactly that on Harbor Point,
        /// which is a good demonstration of why an audit that cries wolf is
        /// worse than no audit.
        ///
        /// The bounding box survives as a cheap reject before the real test.
        /// </summary>
        static bool Blocked(Collider[] colliders, Vector3 p, out string who)
        {
            who = null;
            foreach (var col in colliders)
            {
                if (col == null || col.isTrigger) continue;
                if (col.gameObject.layer == 2) continue;
                if (col.GetComponentInParent<CarController>() != null) continue;
                // Surfaces, not obstacles: the ground, the road and the apron
                // are all concave meshes you are supposed to be driving on —
                // and ClosestPoint cannot answer for them anyway.
                if (col is MeshCollider mc && !mc.convex) continue;

                var b = col.bounds;
                if (b.max.y < p.y - BelowRoad || b.min.y > p.y + ClearHeight) continue;
                if (Mathf.Abs(b.center.x - p.x) > b.extents.x + CarHalfWidth) continue;
                if (Mathf.Abs(b.center.z - p.z) > b.extents.z + CarHalfWidth) continue;

                // At the height the collider actually occupies, so a low kerb is
                // not reported as clear just because the sample sits above it.
                var probe = new Vector3(p.x, Mathf.Clamp(p.y, b.min.y, b.max.y), p.z);
                Vector3 near = col.ClosestPoint(probe);
                float dx = near.x - probe.x, dz = near.z - probe.z;
                if (dx * dx + dz * dz > CarHalfWidth * CarHalfWidth) continue;

                who = Key(col.transform);
                return true;
            }
            return false;
        }


        // ==================================================================
        //  Is the barrier you hit the barrier you can see?
        // ==================================================================
        //
        // EVERYTHING ELSE IN THIS FILE STOPS AT THE BARRIER LINE, because
        // outside it the world is SUPPOSED to be solid. That is exactly where
        // this fault lives: not something standing in the run-off, but the
        // barrier itself, solid in a place where nothing is drawn.
        //
        // Reported from the car as "invisible wall on edge of road that knocked
        // me off the track" on Mount Mitchell, and the cause was a cut bank
        // whose collider height was floored at the guard wall's 1.7 m while the
        // drawn rock face tapered to 0.15 m at the end of every run: 1.0 km of
        // that stage's shoulder carried a collider taller than its rock and
        // 180 m of it stood in open gravel. Nothing here would have said a
        // word, because a bank collider is 5.8 m off the centreline and every
        // band this file measures ends at 5.25.
        //
        // TWO QUESTIONS, and they are different questions:
        //
        //   GAP       — at the height a car touches it, how far out from the
        //               contact face is the nearest thing that is drawn? This
        //               is the everyday form of the fault: you stop a foot
        //               short of rock you can see, every time, all the way
        //               down the mountain.
        //   OVERSHOOT — how far does the collider carry on above the top of
        //               the drawn barrier? A guard wall is allowed some: the
        //               parkway's stone is 0.85 m and its collider 1.7, and
        //               that extra is deliberately what stops a car arriving at
        //               120 km/h from stepping over a low wall into a gorge. It
        //               coincides with a wall you can see and hit, so it reads
        //               as the wall. A metre and a half of it standing where
        //               NOTHING is drawn does not.
        //
        // Measured by ray against the triangles of everything the player can
        // actually see — not against collider bounds, which is the same answer
        // to a different question.

        /// <summary>Highest a car on its wheels can touch. Sampling above the
        /// roofline would report the top of every tall cut face, which no car
        /// will ever reach, as an invisible wall.</summary>
        const float GhostSampleTop = 1.4f;
        /// <summary>Sample pitch up the contact face.</summary>
        const float GhostStep = 0.2f;
        /// <summary>How far outward to look for something drawn before calling
        /// it nothing. Four metres past the barrier line is well into the
        /// hillside on one side of a stage and out over the valley on the
        /// other.</summary>
        const float GhostReach = 4f;
        /// <summary>Start the ray this far back INSIDE the contact face. A
        /// circuit barrier is drawn exactly coplanar with its collider, and a
        /// ray starting on that plane misses it — which would report every
        /// wall on every circuit as a ghost.</summary>
        const float GhostBack = 0.15f;
        /// <summary>How far a car may be stopped in front of the nearest drawn
        /// surface. Half a metre is about a bumper; past that you are being
        /// held off something by nothing.</summary>
        const float GhostGap = 0.5f;
        /// <summary>How far a collider may stand above the drawn barrier it
        /// belongs to. The parkway wall spends 0.85 of its 1.7 m above its own
        /// stone on purpose (see StageWallCollH); this is the line between that
        /// and a collider with no barrier under it at all.</summary>
        const float GhostOver = 1.0f;
        /// <summary>How far outside the barrier line a collider can be and
        /// still be this venue's barrier rather than scenery.</summary>
        const float GhostOut = 5f;

        static void AuditGhostBarriers(TrackCatalog.TrackDef def, TrackPath path,
                                       Collider[] colliders, float trackHalf, StringBuilder log)
        {
            float barrier = PSXRacingBuilder.WallOffsetFor(def);
            var world = new DrawnWorld(path, barrier + GhostOut + GhostReach + 2f);
            if (world.Triangles == 0)
            {
                log.AppendLine("  BARRIER FACES: nothing drawn is readable here — not measured");
                return;
            }

            var faults = new Dictionary<string, GhostFault>();
            int measured = 0;

            foreach (var col in colliders)
            {
                if (col == null || col.isTrigger) continue;
                if (col.gameObject.layer == 2) continue;
                if (col.GetComponentInParent<CarController>() != null) continue;
                // A concave mesh has no ClosestPoint, and is a surface anyway.
                var mc = col as MeshCollider;
                if (mc != null && !mc.convex) continue;

                int i = path.NearestIndex(col.bounds.center);
                Vector3 road = path.GetPoint(i);
                Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                Vector3 near = col.ClosestPoint(road);
                float lat = Vector3.Dot(near - road, right);
                float side = lat >= 0f ? 1f : -1f;
                float reach = Mathf.Abs(lat);
                // Inside the barrier line is the main pass's business; well
                // outside it is scenery no car reaches.
                if (reach < trackHalf || reach > barrier + GhostOut) continue;
                float top = col.bounds.max.y - road.y;
                // A kerb is not a barrier: nothing under 0.3 m holds a car off
                // anything, and half the furniture beside a road is a kerb.
                if (top < 0.3f) continue;
                measured++;

                // THE TEST IS FACE-LOCAL: every plan face of the collider,
                // outward from each, nearest wins.
                //
                // The obvious version asks the road which way is out — the
                // waypoint's right vector, or the direction from the waypoint
                // to the collider — and on an interchange there is no answer.
                // A wall box beside the 277 belt is often NEAREST to a
                // waypoint on the ramp running alongside it, so "away from the
                // road" comes out pointing ALONG the wall; the ray then flies
                // four metres parallel to the stone it was sent to find, hits
                // a lamp post, and the report says 0.86 m of air. Sixty-one
                // times round Uptown, every one of them a wall that is there.
                //
                // A collider does not need the road to know where its own
                // faces are. Asking every face and keeping the nearest answers
                // the question that matters — when a car touches this thing,
                // is there something drawn where it touches — and it cannot be
                // fooled by geometry that runs beside itself.
                var faces = new List<(Vector3 origin, Vector3 dir)>(4);
                {
                    Vector3 c = col.bounds.center;
                    var box = col as BoxCollider;
                    if (box != null)
                    {
                        // EACH RAY STARTS OUTSIDE ITS FACE AND FIRES INWARD,
                        // through the collider. Not outward: a barrier's
                        // drawn surface lives just INSIDE its box — the stage
                        // wall's stone is 0.05 m in from the contact face, and
                        // the collider is deliberately thicker than it — so a
                        // ray leaving the face travels away from the only
                        // thing it was sent to find and reports the far side
                        // of the world.
                        c = box.transform.TransformPoint(box.center);
                        Vector3 sx = Vector3.Scale(box.size, box.transform.lossyScale);
                        Vector3 ax = box.transform.right, az = box.transform.forward;
                        faces.Add((c + ax * (sx.x * 0.5f + GhostBack), -ax));
                        faces.Add((c - ax * (sx.x * 0.5f + GhostBack), ax));
                        faces.Add((c + az * (sx.z * 0.5f + GhostBack), -az));
                        faces.Add((c - az * (sx.z * 0.5f + GhostBack), az));
                    }
                    else
                    {
                        // Anything else is measured off the road: start just
                        // inside the nearest point and fire outward.
                        Vector3 o = right * side;
                        faces.Add((near - o * GhostBack, o));
                    }
                }
                for (int f2 = 0; f2 < faces.Count; f2++)
                {
                    var d = faces[f2].dir; d.y = 0f;
                    if (d.sqrMagnitude < 1e-4f) { faces.RemoveAt(f2--); continue; }
                    faces[f2] = (faces[f2].origin, d.normalized);
                }
                if (faces.Count == 0) continue;
                // SAMPLE THE HEIGHTS THIS COLLIDER ACTUALLY OCCUPIES.
                //
                // Starting at road level and walking up assumes the barrier
                // starts at the road, which a barrier does and a building on a
                // raised lot does not: the beach houses stand on their highest
                // corner with a skirt below, so a ray fired at bumper height
                // passes UNDER the house and finds nothing, and the house is
                // then reported as a force field. The band is the overlap of
                // what a car can reach with what the collider is.
                float lo = Mathf.Max(GhostStep, col.bounds.min.y - road.y + 0.05f);
                float hi = Mathf.Min(top, GhostSampleTop);
                if (hi < lo) continue;      // overhead, or buried under the road
                float drawnTop = lo - GhostStep, worstGap = 0f;
                bool anyDrawn = false;
                string hitName = null;
                for (float y = lo; y <= hi + 1e-3f; y += GhostStep)
                {
                    string who = null;
                    float hit = -1f;
                    foreach (var fc in faces)
                    {
                        Vector3 o = fc.origin; o.y = road.y + y;
                        string w2;
                        float h2 = world.RayOut(o, fc.dir, GhostReach + GhostBack, out w2);
                        if (h2 < 0f) continue;
                        if (hit < 0f || h2 < hit) { hit = h2; who = w2; }
                    }
                    // The first height with nothing in front of it is the top
                    // of the drawn barrier. Everything above that is overshoot,
                    // whether or not something reappears higher up.
                    if (hit < 0f) break;
                    anyDrawn = true;
                    drawnTop = y;
                    float gap = Mathf.Max(0f, hit - GhostBack);
                    if (gap > worstGap) { worstGap = gap; hitName = who; }
                }
                float over = hi - drawnTop;

                if (worstGap <= GhostGap && over <= GhostOver) continue;

                string key = Key(col.transform);
                GhostFault f;
                if (!faults.TryGetValue(key, out f))
                    faults[key] = f = new GhostFault { key = key, waypoint = i };
                f.count++;
                if (worstGap > f.gap) { f.gap = worstGap; f.nearest = hitName; }
                if (over > f.over)
                {
                    f.over = over;
                    f.top = top;
                    f.drawn = anyDrawn ? drawnTop : -1f;
                    f.waypoint = i;
                    f.where = col.bounds.center;
                }
            }

            if (faults.Count == 0)
            {
                log.AppendLine("  BARRIER FACES OK — all " + measured +
                               " barrier colliders have the thing they stand for drawn " +
                               "where a car meets them" + world.Note);
                return;
            }

            var sorted = new List<GhostFault>(faults.Values);
            sorted.Sort((a, b) => (b.over + b.gap).CompareTo(a.over + a.gap));
            log.AppendLine("  GHOST BARRIERS — " + faults.Count + " of " + measured +
                           " barrier colliders are solid where nothing is drawn" +
                           world.Note + ":");
            foreach (var f in sorted)
            {
                log.AppendLine("    " + f.key + "  x" + f.count +
                               (f.gap > GhostGap
                                  ? "  held off by " + f.gap.ToString("0.00") + " m of air"
                                  : "") +
                               (f.over > GhostOver
                                  ? "  collider " + f.top.ToString("0.00") + " m tall over " +
                                    (f.drawn < 0f ? "NOTHING drawn" : f.drawn.ToString("0.0") +
                                     " m of drawn barrier")
                                  : "") +
                               (f.nearest != null ? "  (nearest drawn: " + f.nearest + ")" : "") +
                               "  near wp " + f.waypoint +
                               "  at " + f.where.x.ToString("0") + "," +
                               f.where.y.ToString("0") + "," + f.where.z.ToString("0"));
            }
        }

        class GhostFault
        {
            public string key, nearest;
            public int count, waypoint;
            public float gap, over, top, drawn;
            public Vector3 where;
        }

        /// <summary>
        /// Every triangle the player can see, in world space, in a coarse XZ
        /// hash so a four-metre ray touches a handful of them instead of half a
        /// million.
        ///
        /// Only what is DRAWN goes in: a renderer that is off is not there,
        /// cars are traffic, and the foliage layer is billboards with no
        /// collider — a tree standing behind an invisible wall does not make
        /// the wall visible.
        ///
        /// Only the corridor goes in, too. A stage carries 2.3 km of far
        /// terrain either side of the route and none of it is within reach of a
        /// barrier, so the waypoints stamp a band of interesting cells first
        /// and a triangle outside it is never even transformed.
        /// </summary>
        class DrawnWorld
        {
            const float Cell = 4f;
            readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
            readonly List<Vector3> va = new List<Vector3>();
            readonly List<Vector3> vb = new List<Vector3>();
            readonly List<Vector3> vc = new List<Vector3>();
            readonly List<int> owner = new List<int>();
            readonly List<string> names = new List<string>();

            int unreadable;

            public int Triangles { get { return va.Count; } }
            /// <summary>What this could only measure as a box, for the report,
            /// because an audit that quietly downgrades its own resolution is
            /// worse than one that says so.</summary>
            public string Note
            {
                get
                {
                    return unreadable == 0 ? "" :
                        " (" + unreadable + " imported meshes measured as their bounding box — " +
                        "not readable)";
                }
            }

            /// <summary>The 12 triangles of a box, wound over the corner order
            /// used above (bit 0 = x, bit 1 = y, bit 2 = z).</summary>
            static readonly int[] BoxTris =
            {
                0,1,3, 0,3,2,   4,7,5, 4,6,7,     // -z, +z
                0,4,5, 0,5,1,   2,7,6, 2,3,7,     // -y, +y
                0,2,6, 0,6,4,   1,5,7, 1,7,3      // -x, +x
            };

            static long K(int x, int z) { return ((long)x << 32) ^ (uint)z; }
            static int C(float v) { return Mathf.FloorToInt(v / Cell); }

            public DrawnWorld(TrackPath path, float band)
            {
                var interest = new HashSet<long>();
                int r = Mathf.CeilToInt(band / Cell);
                for (int i = 0; i < path.Count; i++)
                {
                    Vector3 p = path.GetPoint(i);
                    int cx = C(p.x), cz = C(p.z);
                    for (int x = -r; x <= r; x++)
                        for (int z = -r; z <= r; z++)
                            interest.Add(K(cx + x, cz + z));
                }

                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (!mf.gameObject.activeInHierarchy) continue;
                    int layer = mf.gameObject.layer;
                    if (layer == 2 || layer == 10) continue;          // cars, foliage
                    var ren = mf.GetComponent<MeshRenderer>();
                    if (ren == null || !ren.enabled) continue;
                    if (mf.GetComponentInParent<CarController>() != null) continue;
                    var mesh = mf.sharedMesh;
                    var xf = mf.transform;
                    int me = names.Count;
                    names.Add(Key(xf));
                    Vector3[] world;
                    int[] tri;
                    if (mesh.isReadable)
                    {
                        var verts = mesh.vertices;
                        world = new Vector3[verts.Length];
                        for (int v = 0; v < verts.Length; v++) world[v] = xf.TransformPoint(verts[v]);
                        tri = mesh.triangles;
                    }
                    else
                    {
                        // AN IMPORTED MODEL IS NOT READABLE, AND ITS BOX IS THE
                        // HONEST ANSWER. Every .obj in this project imports with
                        // Read/Write off, so a building's triangles cannot be
                        // walked at all — and a house that cannot be walked
                        // reads as a house that is not drawn, which is how the
                        // first run of this pass reported 114 perfectly solid
                        // buildings as force fields. Their box colliders ARE
                        // the local mesh bounds, so the oriented bounding box
                        // is exactly the surface those colliders stand for.
                        // Mesh.bounds is serialised and needs no read flag.
                        unreadable++;
                        var bb = mesh.bounds;
                        world = new Vector3[8];
                        for (int c = 0; c < 8; c++)
                            world[c] = xf.TransformPoint(bb.center + Vector3.Scale(
                                bb.extents,
                                new Vector3((c & 1) == 0 ? -1f : 1f,
                                            (c & 2) == 0 ? -1f : 1f,
                                            (c & 4) == 0 ? -1f : 1f)));
                        tri = BoxTris;
                    }
                    for (int t = 0; t + 2 < tri.Length; t += 3)
                    {
                        Vector3 a = world[tri[t]], b = world[tri[t + 1]], c = world[tri[t + 2]];
                        int x0 = C(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
                        int x1 = C(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                        int z0 = C(Mathf.Min(a.z, Mathf.Min(b.z, c.z)));
                        int z1 = C(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                        // A far-terrain triangle is 60 m across, and one that
                        // big is never a barrier face.
                        if ((x1 - x0 + 1) * (z1 - z0 + 1) > 256) continue;
                        bool want = false;
                        for (int x = x0; x <= x1 && !want; x++)
                            for (int z = z0; z <= z1 && !want; z++)
                                if (interest.Contains(K(x, z))) want = true;
                        if (!want) continue;

                        int id = va.Count;
                        va.Add(a); vb.Add(b); vc.Add(c); owner.Add(me);
                        for (int x = x0; x <= x1; x++)
                            for (int z = z0; z <= z1; z++)
                            {
                                long k = K(x, z);
                                List<int> bucket;
                                if (!grid.TryGetValue(k, out bucket))
                                    grid[k] = bucket = new List<int>();
                                bucket.Add(id);
                            }
                    }
                }
            }

            /// <summary>Metres along <paramref name="dir"/> to the first drawn
            /// triangle, or -1 if there is nothing within <paramref
            /// name="len"/>. Two-sided: a barrier ribbon is drawn facing the
            /// road, and this ray is travelling away from it.</summary>
            public float RayOut(Vector3 origin, Vector3 dir, float len, out string who)
            {
                who = null;
                float best = -1f;
                int bestOwner = -1;
                var seen = new HashSet<int>();
                int steps = Mathf.CeilToInt(len / (Cell * 0.5f)) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    float walked = len * s / steps;
                    Vector3 p = origin + dir * walked;
                    int cx = C(p.x), cz = C(p.z);
                    for (int x = -1; x <= 1; x++)
                        for (int z = -1; z <= 1; z++)
                        {
                            List<int> bucket;
                            if (!grid.TryGetValue(K(cx + x, cz + z), out bucket)) continue;
                            foreach (int id in bucket)
                            {
                                if (!seen.Add(id)) continue;
                                float t;
                                if (!RayTri(origin, dir, va[id], vb[id], vc[id], out t)) continue;
                                if (t > len) continue;
                                if (best < 0f || t < best) { best = t; bestOwner = owner[id]; }
                            }
                        }
                    // Everything nearer than the cells already walked has been
                    // seen, so the nearest hit is final once it is behind us.
                    if (best >= 0f && best <= walked) break;
                }
                if (bestOwner >= 0) who = names[bestOwner];
                return best;
            }

            static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
            {
                t = 0f;
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 pv = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-9f) return false;   // parallel; either facing counts
                float inv = 1f / det;
                Vector3 tv = o - a;
                float u = Vector3.Dot(tv, pv) * inv;
                if (u < -1e-5f || u > 1f + 1e-5f) return false;
                Vector3 qv = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, qv) * inv;
                if (v < -1e-5f || u + v > 1f + 1e-5f) return false;
                t = Vector3.Dot(e2, qv) * inv;
                return t > 1e-4f;
            }
        }

        class Offense
        {
            public string key, type, layer;
            public int count, waypoint;
            public float nearest;
            public bool hasRenderer;
        }

        /// <summary>Collapse siblings into one bucket: the 292 wall segment boxes
        /// are all "Track/WallL/Wall", and listing them individually would bury
        /// a single genuinely misplaced building.</summary>
        static string Key(Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
