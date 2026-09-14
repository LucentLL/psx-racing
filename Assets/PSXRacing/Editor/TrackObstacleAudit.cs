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
    /// And then THE EDGE (<see cref="AuditEdges"/>), which is the other half of
    /// the same question and does use rays: not "is something standing where a
    /// car drives" but "can a car that ran wide get back on, can it fall off,
    /// and is every barrier beside the road one that is warranted" — measured
    /// against <see cref="RoadsideRules"/>, the table the builders aim under.
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
            // The edge pass asks the physics scene, and an edit-mode physics
            // scene only knows where a freshly loaded collider is once it has
            // been told.
            Physics.SyncTransforms();
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
            AuditEdges(def, path, log);
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
        /// The shoulder ribbon (`BuildShoulders`, once `BuildRoadEdge`) and
        /// `BuildOneStageWall` have always branched on side. `BuildKerbs` and
        /// the circuits' `BuildWalls` never did, so the
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
                // Every chunk of the shoulder ribbon (no chunk carries more
                // than 240 m of road): "RoadEdge" today, and a "RoadEdge_<n>"
                // name would be found the same way.
                bool wantUp = n == "Road" || n == "KerbL" || n == "KerbR" || n.StartsWith("RoadEdge");
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
                    // A vertical triangle faces neither up nor down: the
                    // render-only edge face that shows a shoulder's inch is
                    // one. Inside out means DOWN, and only that is counted.
                    if (wantUp && Mathf.Abs(nrm.y) < 0.05f) continue;
                    seen++;
                    if (wantUp) { if (nrm.y < 0f) bad++; }
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

        /// <summary>
        /// The height of the SURFACE under a point on the driving line — the
        /// thing a wheel would rest on, and nothing else. <see
        /// cref="AuditSurface"/>'s rule.
        ///
        /// A plain downward raycast is not that. The first version of this
        /// walked into the grid and reported a 1.5 m step in the middle of the
        /// road, which is the roof of a parked car; a lamp arm or a tree canopy
        /// would have done the same. The rule that separates them is the one
        /// the obstacle test above already uses from the other direction: a
        /// concave mesh collider is a surface (road, ground, deck, forecourt,
        /// kerb, verge), and everything solid enough to be an obstacle is a
        /// box, a capsule or a convex hull — which on the tarmac is the main
        /// pass's business. ACROSS the edge the rule is deliberately different
        /// (see <see cref="ProfileAt"/>): there a box or a steep face is exactly
        /// what a car trying to get back on meets.
        /// </summary>
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

        // ==================================================================
        //  THE EDGE: can a car that ran wide get back on, can it fall off,
        //  and is every barrier beside the road one that is warranted?
        // ==================================================================
        //
        // This replaced AuditVerge, which never measured a mountain road. It
        // worked its outer limit out from WallOffsetFor(def) — the builder's
        // own idea of where the barrier stands — and on a stage that lands on
        // the kerb, so for its whole life it printed "no run-off to profile
        // (the barrier is on the kerb)" for all eight stages while the build
        // log said walls and banks covered part of their roadside and the rest
        // was a 0.46 m slab face at 61 degrees: BlueRidge wp 224, 0.41 m of
        // drop inside 0.25 m, sixteen times the owner's inch, on 21 km of
        // stage roadside. The audit could not see the builder's mistake because
        // it had been handed the same assumption. It also profiled every tenth
        // station, exempted 12 m either side of every span (which is where the
        // unrailed deck ends and approach fills were), and allowed 0.17 m of
        // step per 0.25 m — twice the body box's clearance at the lowest ride
        // height, and at a pitch that cannot tell a slope from a wall.
        //
        // So this one assumes NOTHING about where anything is. Every station,
        // both sides, every scened venue, no exemption. The tarmac comes from a
        // Road-layer ray, the barrier from horizontal rays at the heights the
        // body occupies, the land from downward rays every 5 cm that take EVERY
        // non-trigger collider (a steep face is what a car trying to get back
        // on meets, not something to skip because it is not a floor), and the
        // thresholds from RoadsideRules — the one table the builders aim under,
        // so the rule cannot drift between the thing that builds a roadside and
        // the thing that measures it. Seven findings per half-section:
        //
        //   EDGE DROP   the land beside the tarmac or kerb strip falls away at
        //               the join by more than the owner's inch (fail at two);
        //   EDGE FACE   anywhere across the edge, a rise the body box meets as
        //               a wall (FaceRiseFailM within FaceRunM) — except the
        //               sinking stone of a warranted run's buried terminal,
        //               which is reported as info;
        //   EDGE SLOPE  a foreslope steeper than 1V:4H sustained inside the
        //               clear zone (warn past 1V:6H);
        //   EDGE FALL   a critical fall (RoadsideRules.IsCriticalFall) that no
        //               barrier guards;
        //   UNWARRANTED BARRIER  a barrier with drivable land at road height
        //               behind it — the pocket a car gets into through a gap
        //               and cannot leave. A rail on a structure or its approach
        //               is warranted whatever is behind it, and a circuit's
        //               continuous perimeter wall is not a trap, so those two
        //               are reported but do not fail;
        //   DECK RAIL   a gap in the barrier along a deck or the full-height
        //               part of its approach run, cast every half metre ALONG
        //               the edge, not per station;
        //   RUN END     a barrier run that stops within eight stations of a
        //               critical fall.

        /// <summary>Lateral sample pitch across the edge. At 0.25 m a 0.17 m
        /// slope and a 0.17 m wall are the same two numbers; at 5 cm a face
        /// inside the body box's 13 cm overhang is at least two samples.</summary>
        const float EdgePitch = 0.05f;
        /// <summary>Where the profile starts, in metres past the tarmac edge:
        /// just inside it, so the join between the tarmac and whatever lies
        /// beside it is itself a pair of samples.</summary>
        const float EdgeStartM = -0.1f;
        /// <summary>How far past the tarmac edge the profile and the barrier
        /// rays reach when nothing stops them first. Past the clear zone
        /// (kerb + 3.5 m) and the barrier warrant (kerb + 5 m).</summary>
        const float EdgeReachM = 8f;
        /// <summary>How far an OPEN half-section's profile is carried on past
        /// <see cref="EdgeReachM"/>, for information only (MeasureFarFace): the
        /// toe of the longest foreslope a stage shoulder lays — its ribbon to
        /// the clear zone's end past the kerb strip, the slope carried on from
        /// there no gentler than 1V:4H to RoadsideRules.CriticalFallM of depth
        /// (ShoulderCarry), and the toe's crossing run.</summary>
        const float FarReachM = PSXRacingBuilder.KerbWidth + RoadsideRules.ClearZoneM +
                                RoadsideRules.CriticalFallM / RoadsideRules.SteepestRecoverableSlope +
                                RoadsideRules.ToeCrossingMaxM;
        /// <summary>Samples of the main profile the far walk starts with, so a
        /// face whose top stands just past EdgeReachM is inside a window whole.</summary>
        const int FarLead = 3;
        /// <summary>"Within 0.3 m of the strip edge": how far past the outer
        /// edge of the kerb strip a drop still counts as the EDGE's drop rather
        /// than as the slope's.</summary>
        const float EdgeDropZoneM = 0.3f;
        /// <summary>
        /// Slack on the WARN line only (RoadsideRules.EdgeDropM is a target the
        /// builders aim AT, not under). The circuit's Safety Edge is exactly
        /// the inch over 0.043 m, and the 5 cm sample pair that holds it also
        /// carries up to a pitch of the run-off's own cross-fall plus float32
        /// rounding at km-scale world coordinates: a python replica of that
        /// bevel over 2000 jittered stations read 0.0250-0.0255 and warned on
        /// 1319 of them at a bare 0.025, and on none at 0.028. The FAIL line
        /// (EdgeDropFailM) has no slack.
        /// </summary>
        const float EdgeDropNoiseM = 0.003f;
        /// <summary>The tarmac is read this far inside its own edge, and the
        /// barrier rays start there.</summary>
        internal const float TarmacInsetM = 0.3f;
        /// <summary>A hit whose |normal.y| is at or under this is a WALL to the
        /// car, over it a floor. CollisionResponder.LandingNormalDot (0.7,
        /// private there): the same line the car's own crash response draws,
        /// so a face this calls a barrier is one the car calls a crash.
        /// </summary>
        const float WallNormalY = 0.7f;
        /// <summary>A profile ray starts this far above the last surface it
        /// found. Low enough to pass under anything overhead; the land cannot
        /// rise this much in one 5 cm sample without being a wall the barrier
        /// rays already stopped the profile at.</summary>
        const float ProfileHeadM = 1.2f;
        /// <summary>A sample that lands more than this below the last one is
        /// re-cast from <see cref="ProfileRecastM"/> up, in case the low origin
        /// started INSIDE something (a rock box, a raised bench) and saw
        /// through it to the lattice underneath.</summary>
        const float ProfileSuddenM = 0.3f;
        const float ProfileRecastM = 6f;
        /// <summary>How far down a profile ray looks. Nothing within this is a
        /// void, and a void beside the road is a fall by any measure.</summary>
        const float ProfileDepthM = 40f;
        /// <summary>Horizontal run a foreslope grade is measured over. Long
        /// enough that one triangle's quantisation cannot fake a grade, short
        /// against the 1 m a grade has to be sustained to fail.</summary>
        const float SlopeWindowM = RoadsideRules.SlopeWindowM;
        /// <summary>Grade tolerance on the slope limits. Stage ground and
        /// shoulder chunks are mesh-compressed (about 4 mm over a 240 m chunk),
        /// and a design section built at exactly 1V:6H must not flicker a WARN
        /// down its whole length.</summary>
        const float SlopeNoise = 0.015f;
        /// <summary>The land behind a barrier is read from this far above the
        /// road, so a rock top the face stands for is found rather than seen
        /// through to the bench under it.</summary>
        const float PocketRayHeadM = 20f;
        /// <summary>How far past the barrier's first face a back-cast looks for
        /// its outer face. A wall box is 1.2 m; a rock is a face with no back.
        /// </summary>
        const float BarrierBackM = 4f;
        /// <summary>Lateral offsets past the tarmac edge at which a BridgeDeck
        /// collider under the kerb or shoulder marks a structure station.
        /// </summary>
        static readonly float[] DeckProbeE = { 0.5f, 1.2f, 2.0f };
        /// <summary>Pitch along a deck edge of the rail casts.</summary>
        const float RailPitchM = 0.5f;
        /// <summary>A missed rail cast is re-cast this far either side along the
        /// edge before it is called a gap: a ray that slips through the seam
        /// between two abutting boxes is not a hole a car fits through.</summary>
        const float RailConfirmM = 0.05f;
        /// <summary>How far past the MEASURED deck edge a rail cast still
        /// counts. The edge is found at a 0.25 m pitch (so it reads short by up
        /// to that), and a parapet standing on the deck's outer lip has its
        /// inner face right at the edge: without this, a rail exactly where a
        /// rail belongs would read as a gap.</summary>
        const float RailEdgeSlackM = 0.5f;
        /// <summary>Runs listed per venue, worst first.</summary>
        const int EdgeWorstListed = 20;

        /// <summary>Every query here ignores layer 2: the cars.</summary>
        const int NotCars = ~(1 << 2);

        static readonly RaycastHit[] EdgeHits = new RaycastHit[64];
        static readonly IComparer<RaycastHit> ByDistance =
            Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        /// <summary>One station, one side.</summary>
        class EdgeHalf
        {
            public bool measured, barrier, deck, structure, fall, pocket, faceIn, terminal;
            public float tarmacY, barrierE = float.PositiveInfinity;
            public float drop, dropE, face, faceE, slopeFail, slopeWarn, slopeGrade, slopeE, backslope, backE;
            public float fallM, fallE, pocketDy;
            public float farFace, farFaceE;
            public bool farFaceIn;
            public string barrierOn, dropOn, faceOn, fallOn, pocketOn, farFaceOn;
            public Collider barrierCol, faceCol;
            public Vector3 edge;
        }

        /// <summary>A finding, grouped over consecutive stations on one side.
        /// </summary>
        class EdgeRun
        {
            public string label, what;
            public bool fail;
            public int side, from, to, stations;
            public float worst, severity;
            public Vector3 where;
        }

        static void AuditEdges(TrackCatalog.TrackDef def, TrackPath path, StringBuilder log)
        {
            int roadLayer = LayerMask.NameToLayer("Road");
            if (roadLayer < 0)
            {
                log.AppendLine("  FAIL edge not measured: no layer is named Road, so the tarmac cannot be found");
                return;
            }
            int roadMask = 1 << roadLayer;
            int n = path.Count;
            bool loop = !path.HasEnds;
            float half = path.roadWidth * 0.5f;
            // Where to LOOK, not what to expect: the strip BuildKerbs draws is
            // this wide, and the shoulder begins at its outer edge.
            float kerb = PSXRacingBuilder.KerbWidth;
            float lap = Mathf.Max(def.LengthM, 1f);
            // A circuit and a drag strip keep a continuous perimeter wall over
            // graded run-off (the owner's decision, 2026-09-13); land behind it
            // is the rest of the venue, not a pocket.
            bool closedCourse = !def.stage;

            int samples = Mathf.CeilToInt((EdgeReachM - EdgeStartM) / EdgePitch) + 1;
            var ys = new float[samples];
            var ny = new float[samples];
            var on = new Collider[samples];
            var farYs = new float[FarLead + Mathf.CeilToInt((FarReachM - EdgeReachM) / EdgePitch) + 1];
            var farOn = new Collider[farYs.Length];
            var halves = new EdgeHalf[n, 2];
            int measured = 0, unmeasured = 0, barriers = 0;

            for (int i = 0; i < n; i++)
            {
                Vector3 c = path.GetPoint(i);
                Vector3 right = RightAt(path, i);
                for (int si = 0; si < 2; si++)
                {
                    var h = halves[i, si] = new EdgeHalf();
                    Vector3 o = right * (si == 0 ? -1f : 1f);
                    Vector3 inset = c + o * (half - TarmacInsetM);
                    if (!Physics.Raycast(inset + Vector3.up * 3f, Vector3.down, out RaycastHit tar, 6f,
                                         roadMask, QueryTriggerInteraction.Ignore))
                    { unmeasured++; continue; }
                    measured++;
                    h.measured = true;
                    h.tarmacY = tar.point.y;
                    h.edge = c + o * half;
                    h.edge.y = h.tarmacY;
                    foreach (float e in DeckProbeE)
                        if (DeckUnder(c + o * (half + e), h.tarmacY)) { h.deck = true; break; }

                    // The barrier, at the heights the body occupies.
                    h.barrier = EdgeBarrier(inset, o, h.tarmacY, out h.barrierE, out Collider barrierCol, out float barrierH);
                    if (h.barrier) { barriers++; h.barrierOn = Key(barrierCol.transform); h.barrierCol = barrierCol; }

                    // The land, outward, up to the barrier's face.
                    float limit = h.barrier ? Mathf.Min(h.barrierE - EdgePitch, EdgeReachM) : EdgeReachM;
                    int count = 0;
                    float prevY = h.tarmacY;
                    for (int k = 0; k < samples; k++)
                    {
                        float e = EdgeStartM + k * EdgePitch;
                        if (e > limit + 1e-4f) break;
                        ys[k] = ProfileAt(c + o * (half + e), prevY, out on[k], out ny[k]);
                        if (!float.IsNaN(ys[k])) prevY = ys[k];
                        count = k + 1;
                    }
                    // The barrier's own foot is the barrier, not land in front
                    // of it. A face that LEANS BACK (a battered rock, a sloped
                    // parapet, a hillside steeper than 45 degrees) is met by the
                    // horizontal ray at body height a little way up it, and the
                    // profile, which runs to 5 cm short of that hit, then walks
                    // up the lower part of the same face and reports it as an
                    // EDGE FACE a car "runs wide" into — which it is: the
                    // barrier. So the trailing samples that stand on the barrier
                    // collider itself, at a wall-class normal, are dropped. Only
                    // the barrier's collider: a separate face in front of it is
                    // still a face.
                    if (h.barrier)
                        while (count > 0 && on[count - 1] == barrierCol && !float.IsNaN(ys[count - 1]) &&
                               Mathf.Abs(ny[count - 1]) <= WallNormalY)
                            count--;
                    if (count >= 2) MeasureProfile(h, ys, on, count, kerb);
                    // and on past the reach where nothing stands in the way
                    if (!h.barrier && count == samples && !float.IsNaN(ys[count - 1]))
                        MeasureFarFace(h, c + o * half, o, ys, on, count, farYs, farOn);

                    // Behind the barrier: somewhere to stand at road height?
                    if (h.barrier &&
                        PocketBehind(c, o, half, inset, h.tarmacY, h.barrierE, barrierCol, barrierH,
                                     out h.pocketDy, out Collider pocketCol))
                    {
                        h.pocket = true;
                        h.pocketOn = Key(pocketCol.transform);
                    }
                }
            }

            // STRUCTURE STATIONS: every station a deck collider stands under,
            // every station the catalog calls a span (a span with no deck built
            // is still a structure, and falls off twice), and the approach
            // either side of both. Measured, never exempted: the two blend
            // thresholds the stage builder used to disagree on (deck from
            // 0.001, wall from 0.35) were the bug.
            var structure = new bool[n];
            var deckStation = new bool[n];
            // WHERE A RAIL MUST STAND UNBROKEN: every deck station, and from each
            // SPAN station (the catalog's blend, which is what the builders
            // count an approach run from) as far as the full-height part of
            // its approach run reaches. A warranted run's last EndFlareStations
            // chords are its terminal — flared away from the road and buried
            // into the foreslope (RoadsideRules.EndFlareRatio), the stone
            // sinking under the lower barrier ray by design — so they are the
            // run's end, not a gap in it. Measured to those chords a rebuilt
            // stage failed DECK RAIL at both ends of every span on both sides.
            // Pockets behind approach stone are still excused over the whole
            // ApproachRailStations (structure), because the stone is there.
            var railed = new bool[n];
            int railCarry = RoadsideRules.ApproachRailStations - RoadsideRules.EndFlareStations;
            for (int i = 0; i < n; i++)
            {
                bool span = !def.drag && TrackCatalog.BridgeBlend(def, Mathf.Repeat(i * path.spacing, lap)) > 0.001f;
                bool deck = halves[i, 0].deck || halves[i, 1].deck || span;
                if (!deck) continue;
                deckStation[i] = true;
                railed[i] = true;
                for (int k = -RoadsideRules.ApproachRailStations; k <= RoadsideRules.ApproachRailStations; k++)
                {
                    int j = loop ? path.Wrap(i + k) : i + k;
                    if (j < 0 || j >= n) continue;
                    structure[j] = true;
                    if (span && Mathf.Abs(k) <= railCarry) railed[j] = true;
                }
            }
            for (int i = 0; i < n; i++) { halves[i, 0].structure = structure[i]; halves[i, 1].structure = structure[i]; }

            // A WARRANTED RUN'S BURIED TERMINAL. RoadsideRules has every
            // warranted barrier run end flare away from the road and sink into
            // the foreslope over its last EndFlareStations chords, and a stone
            // sinking from full height to nothing stands, at some station, lower
            // than the barrier rays and taller than a face: the stage's collider
            // chord there tops out 7 cm over the tarmac and 15-19 cm over the
            // foreslope in front of it (lane C's section: the chord beside the
            // last-but-one station of every flared end). That is the terminal the
            // rule asks for, not an edge built proud, so it is reported as its
            // own info line rather than as an EDGE FACE: the face stands on a
            // solid barrier box, this station has no barrier at body height, and
            // one within EndFlareStations along the same side does, on a collider
            // of the same name.
            //
            // Only a GUARD WALL's chord (WallColl) is ever a terminal. A cut's
            // BankColl never sinks — the stage builds no box at all where a
            // face tapers (BankCollMinH), and a face low enough to be graded
            // is laid back as a backslope instead — so a BankColl box met as a
            // face with no barrier in front of it is not a terminal the rule
            // asked for; it is a face, and reads as one. (The one case the
            // 2026-09-13 bake filed under terminals, Mount Mitchell wp 1494 R,
            // is the switchback BankPinM's note names, where the upper leg's
            // fill weighs on the lattice under the lower leg's ditch: the probe
            // read ground 0.1 m OVER the ditch there, the barrier rays met that
            // rising ground as a floor before the box, and the profile then
            // found 1.36 m of box — a face, whatever put the ground there.)
            int solidLayer = PSXRacing.EditorTools.WorldKit.SolidLayer;
            for (int si = 0; si < 2; si++)
                for (int i = 0; i < n; i++)
                {
                    var h = halves[i, si];
                    if (!h.measured || h.barrier || h.face <= RoadsideRules.FaceRiseFailM) continue;
                    if (!(h.faceCol is BoxCollider) || h.faceCol.gameObject.layer != solidLayer) continue;
                    if (h.faceCol.name != "WallColl") continue;
                    for (int k = 1; k <= RoadsideRules.EndFlareStations && !h.terminal; k++)
                        foreach (int dir in new[] { -1, 1 })
                        {
                            int j = i + dir * k;
                            if (loop) j = ((j % n) + n) % n;
                            else if (j < 0 || j >= n) continue;
                            var hj = halves[j, si];
                            if (hj.measured && hj.barrier && hj.barrierCol != null &&
                                hj.barrierCol.name == h.faceCol.name)
                            { h.terminal = true; break; }
                        }
                }

            var runs = new List<EdgeRun>();
            float sp = path.spacing;
            EdgeRuns(runs, halves, n, loop, "EDGE DROP", true,
                h => h.drop > RoadsideRules.EdgeDropFailM, h => h.drop, RoadsideRules.EdgeDropFailM,
                h => string.Format("{0:0.000} m drop at {1:0.00} m past the tarmac edge, onto {2}",
                                   h.drop, h.dropE, h.dropOn));
            EdgeRuns(runs, halves, n, loop, "warn edge drop", false,
                h => h.drop > RoadsideRules.EdgeDropM + EdgeDropNoiseM && h.drop <= RoadsideRules.EdgeDropFailM,
                h => h.drop, RoadsideRules.EdgeDropM,
                h => string.Format("{0:0.000} m drop at {1:0.00} m past the tarmac edge, onto {2}",
                                   h.drop, h.dropE, h.dropOn));
            EdgeRuns(runs, halves, n, loop, "EDGE FACE", true,
                h => h.face > RoadsideRules.FaceRiseFailM && !h.terminal, h => h.face, RoadsideRules.FaceRiseFailM,
                h => string.Format("{0:0.00} m rise within {1:0.00} m at {2:0.00} m past the tarmac edge ({3}), on {4}",
                                   h.face, RoadsideRules.FaceRunM, h.faceE,
                                   h.faceIn ? "climbing back in" : "running wide", h.faceOn));
            EdgeRuns(runs, halves, n, loop, "info buried terminal", false,
                h => h.terminal, h => h.face, RoadsideRules.FaceRiseFailM,
                h => string.Format("{0:0.00} m of sinking stone at {1:0.00} m past the tarmac edge, on {2}",
                                   h.face, h.faceE, h.faceOn));
            EdgeRuns(runs, halves, n, loop, "info edge face past the reach", false,
                h => h.farFace > RoadsideRules.FaceRiseFailM, h => h.farFace, RoadsideRules.FaceRiseFailM,
                h => string.Format("{0:0.00} m rise within {1:0.00} m at {2:0.00} m past the tarmac edge ({3}), on {4}",
                                   h.farFace, RoadsideRules.FaceRunM, h.farFaceE,
                                   h.farFaceIn ? "climbing back in" : "running wide", h.farFaceOn));
            EdgeRuns(runs, halves, n, loop, "EDGE SLOPE", true,
                h => h.slopeFail > RoadsideRules.SlopeSustainM, h => h.slopeFail, RoadsideRules.SlopeSustainM,
                h => string.Format("foreslope steeper than 1V:4H for {0:0.00} m from {1:0.00} m past the tarmac edge (steepest 1V:{2:0.0}H)",
                                   h.slopeFail, h.slopeE, 1f / Mathf.Max(h.slopeGrade, 1e-3f)));
            EdgeRuns(runs, halves, n, loop, "warn edge slope", false,
                h => h.slopeFail <= RoadsideRules.SlopeSustainM && h.slopeWarn > RoadsideRules.SlopeSustainM,
                h => h.slopeWarn, RoadsideRules.SlopeSustainM,
                h => string.Format("foreslope steeper than 1V:6H for {0:0.00} m inside the clear zone", h.slopeWarn));
            EdgeRuns(runs, halves, n, loop, "warn backslope", false,
                h => h.backslope > RoadsideRules.SlopeSustainM, h => h.backslope, RoadsideRules.SlopeSustainM,
                h => string.Format("land rises steeper than 1V:3H for {0:0.00} m from {1:0.00} m past the tarmac edge",
                                   h.backslope, h.backE));
            EdgeRuns(runs, halves, n, loop, "EDGE FALL", true,
                h => h.fall, h => h.fallM, RoadsideRules.CriticalFallM,
                h => string.Format("{0:0.00} m critical fall reaching {1:0.00} m past the tarmac edge, onto {2}{3}",
                                   h.fallM, h.fallE, h.fallOn,
                                   h.barrier ? " (in front of " + h.barrierOn + ")" : ", no barrier"));
            EdgeRuns(runs, halves, n, loop, "UNWARRANTED BARRIER", true,
                h => h.pocket && !h.structure && !closedCourse, h => 1f, 1f,
                h => string.Format("land {0:+0.00;-0.00} m from road height {1:0.0} m behind {2} ({3:0.00} m out), on {4}",
                                   h.pocketDy, RoadsideRules.PocketBehindM, h.barrierOn, h.barrierE, h.pocketOn));
            EdgeRuns(runs, halves, n, loop, "info pocket behind a structure rail", false,
                h => h.pocket && h.structure, h => 1f, 1f,
                h => "land at road height behind " + h.barrierOn + " (warranted: a deck or its approach)");
            EdgeRuns(runs, halves, n, loop, "info pocket behind the perimeter wall", false,
                h => h.pocket && !h.structure && closedCourse, h => 1f, 1f,
                h => "land at road height behind " + h.barrierOn + " (a closed course's continuous wall)");

            int railGaps = 0, railStations = 0, railUnmeasured = 0;
            float railGapM = 0f;
            AuditDeckRails(path, halves, railed, deckStation, loop, half, roadMask, runs,
                           ref railGaps, ref railGapM, ref railStations, ref railUnmeasured);
            int runEnds = AuditRunEnds(halves, n, loop, runs);

            // ---- the report ----
            int structureStations = 0, deckStations = 0;
            for (int i = 0; i < n; i++) { if (structure[i]) structureStations++; if (deckStation[i]) deckStations++; }
            log.AppendLine(string.Format(
                "  edge: {0} half-sections measured, every {1:0} m station both sides ({2} with no tarmac under the probe); " +
                "a barrier within {3:0} m on {4}; {5} deck stations, {6} with their approaches",
                measured, sp, unmeasured, EdgeReachM, barriers, deckStations, structureStations));
            EdgeLine(log, runs, "EDGE DROP", "warn edge drop", sp,
                "the land beside the tarmac or kerb strip falls more than " + RoadsideRules.EdgeDropFailM.ToString("0.000") + " m at the join",
                "more than the owner's inch (" + RoadsideRules.EdgeDropM.ToString("0.000") + " m, read with " +
                EdgeDropNoiseM.ToString("0.000") + " m of slack)",
                "edge drop: nothing beside the tarmac or strip falls more than " + RoadsideRules.EdgeDropM.ToString("0.000") +
                " m (+" + EdgeDropNoiseM.ToString("0.000") + " m slack)");
            EdgeLine(log, runs, "EDGE FACE", null, sp,
                "a rise of more than " + RoadsideRules.FaceRiseFailM.ToString("0.00") + " m within " +
                RoadsideRules.FaceRunM.ToString("0.00") + " m, a wall to the body box at the lowest ride height",
                null, "edge face: nothing across the edge rises like a wall");
            EdgeLine(log, runs, null, "info buried terminal", sp, null,
                "a warranted barrier run's flared end sinking into the foreslope, lower than the barrier rays " +
                "(RoadsideRules.EndFlareStations; not counted as an edge face)", null);
            EdgeLine(log, runs, null, "info edge face past the reach", sp, null,
                "on an open half-section, a rise of more than " + RoadsideRules.FaceRiseFailM.ToString("0.00") + " m within " +
                RoadsideRules.FaceRunM.ToString("0.00") + " m from " + EdgeReachM.ToString("0") + " to " +
                FarReachM.ToString("0.0") + " m past the tarmac edge, beyond the clear zone and the warrant " +
                "(measured, not failed: a carried foreslope's toe at its depth cap)", null);
            EdgeLine(log, runs, "EDGE SLOPE", "warn edge slope", sp,
                "a foreslope steeper than 1V:4H for more than " + RoadsideRules.SlopeSustainM.ToString("0.0") + " m inside the clear zone",
                "steeper than 1V:6H for more than " + RoadsideRules.SlopeSustainM.ToString("0.0") + " m",
                "edge slope: recoverable (1V:6H or flatter) across the clear zone");
            EdgeLine(log, runs, null, "warn backslope", sp, null,
                "land rising steeper than 1V:3H for more than " + RoadsideRules.SlopeSustainM.ToString("0.0") + " m", null);
            EdgeLine(log, runs, "EDGE FALL", null, sp,
                "a critical fall (more than " + RoadsideRules.CriticalFallM.ToString("0.0") +
                " m, steeper than 1V:3H) within reach of the shoulder that no barrier guards",
                null, "edge falls: none unguarded");
            EdgeLine(log, runs, "UNWARRANTED BARRIER", null, sp,
                "a barrier with drivable land within " + RoadsideRules.PocketBandM.ToString("0.0") + " m of road height " +
                RoadsideRules.PocketBehindM.ToString("0.0") + " m behind it (a pocket)",
                null, "barriers: none stands in front of a pocket" + (closedCourse ? " (outside the perimeter wall)" : ""));
            EdgeLine(log, runs, null, "info pocket behind a structure rail", sp, null,
                "structure rails with land at road height behind them (warranted)", null);
            EdgeLine(log, runs, null, "info pocket behind the perimeter wall", sp, null,
                "perimeter wall with land at road height behind it (not a trap on a closed course)", null);
            if (railGaps > 0)
                log.AppendLine(string.Format(
                    "  FAIL DECK RAIL: {0} gap(s) totalling about {1:0.0} m along {2} deck/approach half-sections " +
                    "where nothing solid stands between the tarmac and the deck edge{3}",
                    railGaps, railGapM, railStations,
                    railUnmeasured > 0 ? " (" + railUnmeasured + " casts found no tarmac)" : ""));
            else
                log.AppendLine(railStations == 0
                    ? "  ok   deck rails: no structure on this venue"
                    : string.Format("  ok   deck rails: continuous along all {0} deck/approach half-sections, cast every {1:0.0} m{2}",
                                    railStations, RailPitchM,
                                    railUnmeasured > 0 ? " (" + railUnmeasured + " casts found no tarmac)" : ""));
            log.AppendLine(runEnds > 0
                ? "  FAIL RUN END: " + runEnds + " barrier run end(s) stop within " +
                  RoadsideRules.ApproachRailStations + " stations of a critical fall"
                : "  ok   barrier run ends: none stops short of a critical fall");

            if (runs.Count == 0)
            {
                log.AppendLine("  EDGE OK - every edge meets the ground, and every barrier is warranted");
                return;
            }
            runs.Sort((a, b) =>
            {
                if (a.fail != b.fail) return a.fail ? -1 : 1;
                int s = b.severity.CompareTo(a.severity);
                return s != 0 ? s : b.stations.CompareTo(a.stations);
            });
            // Worst first, but no kind may take more than a quarter of the list
            // on the first pass: a stage with two hundred slab-face runs would
            // otherwise list twenty of them and never name its one open deck.
            var listed = new List<EdgeRun>();
            var perKind = new Dictionary<string, int>();
            foreach (var run in runs)
            {
                if (listed.Count >= EdgeWorstListed) break;
                perKind.TryGetValue(run.label, out int had);
                if (had >= EdgeWorstListed / 4) continue;
                perKind[run.label] = had + 1;
                listed.Add(run);
            }
            foreach (var run in runs)
            {
                if (listed.Count >= EdgeWorstListed) break;
                if (!listed.Contains(run)) listed.Add(run);
            }
            listed.Sort((a, b) => runs.IndexOf(a).CompareTo(runs.IndexOf(b)));
            log.AppendLine("  worst " + listed.Count + " of " + runs.Count + " edge runs:");
            foreach (var run in listed)
            {
                string span = run.from == run.to ? "wp " + run.from : "wp " + run.from + "-" + run.to;
                log.AppendLine(string.Format("    {0}{1} {2} {3} ({4:0} m): {5}  [{6:0.0}, {7:0.0}, {8:0.0}]",
                    run.fail ? "FAIL " : "", run.label, run.side < 0 ? "left" : "right", span,
                    run.stations * sp, run.what, run.where.x, run.where.y, run.where.z));
            }
        }

        /// <summary>
        /// THE BARRIER beside a station, as this pass and EdgeProbe both find
        /// it: a horizontal ray from <paramref name="inset"/> (TarmacInsetM in
        /// from the tarmac edge) at each of RoadsideRules.BarrierRayHeights over
        /// the tarmac, and the first thing each meets if that is a wall to the
        /// car (<see cref="FirstBarrier"/>); the nearer of the two.
        /// <paramref name="barrierE"/> is its face in metres past the tarmac
        /// edge, <paramref name="rayH"/> the height that met it.
        /// </summary>
        internal static bool EdgeBarrier(Vector3 inset, Vector3 o, float tarmacY,
                                         out float barrierE, out Collider col, out float rayH)
        {
            barrierE = float.PositiveInfinity; col = null; rayH = 0f;
            foreach (float bh in RoadsideRules.BarrierRayHeights)
            {
                var from = new Vector3(inset.x, tarmacY + bh, inset.z);
                if (FirstBarrier(from, o, EdgeReachM + TarmacInsetM, out float d, out Collider bc) &&
                    d - TarmacInsetM < barrierE)
                { barrierE = d - TarmacInsetM; col = bc; rayH = bh; }
            }
            return col != null;
        }

        /// <summary>
        /// THE POCKET, one definition for this pass and EdgeProbe: standable
        /// land (a hit at a floor's normal, <see cref="WallNormalY"/>) within
        /// RoadsideRules.PocketBandM of road height, RoadsideRules.PocketBehindM
        /// past the barrier's BACK face — found by casting back at the barrier's
        /// own collider, because a guard wall's box is 1.2 m deep and a car
        /// cannot stand inside it — read from <see cref="PocketRayHeadM"/> over
        /// the road so a rock top is found rather than seen through, on every
        /// collider. <paramref name="dy"/> is the land over the tarmac.
        /// </summary>
        internal static bool PocketBehind(Vector3 c, Vector3 o, float half, Vector3 inset, float tarmacY,
                                          float barrierE, Collider barrierCol, float rayH,
                                          out float dy, out Collider on)
        {
            dy = 0f; on = null;
            float outerE = barrierE;
            var from = new Vector3(inset.x, tarmacY + rayH, inset.z) + o * (barrierE + TarmacInsetM + BarrierBackM);
            if (barrierCol.Raycast(new Ray(from, -o), out RaycastHit back, BarrierBackM))
                outerE = barrierE + BarrierBackM - back.distance;
            Vector3 behind = c + o * (half + outerE + RoadsideRules.PocketBehindM);
            if (!SurfaceBelow(new Vector3(behind.x, tarmacY + PocketRayHeadM, behind.z), PocketRayHeadM + 10f,
                              out RaycastHit land) ||
                land.normal.y < WallNormalY || Mathf.Abs(land.point.y - tarmacY) > RoadsideRules.PocketBandM)
                return false;
            dy = land.point.y - tarmacY;
            on = land.collider;
            return true;
        }

        /// <summary>Road-right at a waypoint from its two neighbours — the
        /// builder's RightAt shape, and square to the road on a bend where a
        /// forward difference is half a chord angle off.</summary>
        internal static Vector3 RightAt(TrackPath path, int i)
        {
            Vector3 t = path.GetPoint(i + 1) - path.GetPoint(i - 1);
            t.y = 0f;
            if (t.sqrMagnitude < 1e-6f) t = path.GetTangent(i);
            t.y = 0f;
            return Vector3.Cross(Vector3.up, t.normalized).normalized;
        }

        static bool IsCar(Collider c) =>
            c.gameObject.layer == 2 ||
            (c.attachedRigidbody != null && c.GetComponentInParent<CarController>() != null);

        /// <summary>The first non-car surface straight down. EVERY collider
        /// counts: box, hull, concave mesh.</summary>
        static bool SurfaceBelow(Vector3 from, float reach, out RaycastHit hit)
        {
            if (!Physics.Raycast(from, Vector3.down, out hit, reach, NotCars, QueryTriggerInteraction.Ignore))
                return false;
            if (!IsCar(hit.collider)) return true;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, EdgeHits, reach, NotCars,
                                                QueryTriggerInteraction.Ignore);
            System.Array.Sort(EdgeHits, 0, count, ByDistance);
            for (int k = 0; k < count; k++)
                if (!IsCar(EdgeHits[k].collider)) { hit = EdgeHits[k]; return true; }
            return false;
        }

        /// <summary>
        /// The height of whatever a car beside the road would be standing on or
        /// running into at this point, or NaN for nothing within <see
        /// cref="ProfileDepthM"/>.
        ///
        /// Every non-trigger collider, whatever its type and however steep its
        /// normal. EdgeProbe's first version dropped hits with normal.y under
        /// 0.5 as "a face, not a floor", and the face it dropped was the stage's
        /// 61-degree shoulder batter: it read the dirt underneath and reported
        /// a lip, while the car was meeting the batter.
        /// </summary>
        static float ProfileAt(Vector3 p, float prevY, out Collider on, out float normalY)
        {
            on = null; normalY = 1f;
            bool found = SurfaceBelow(new Vector3(p.x, prevY + ProfileHeadM, p.z), ProfileDepthM, out RaycastHit hit);
            if (!found || hit.point.y < prevY - ProfileSuddenM)
            {
                if (SurfaceBelow(new Vector3(p.x, prevY + ProfileRecastM, p.z), ProfileDepthM + ProfileRecastM,
                                 out RaycastHit high) && high.point.y >= prevY - ProfileSuddenM)
                { hit = high; found = true; }
            }
            if (!found) return float.NaN;
            on = hit.collider;
            normalY = hit.normal.y;
            return hit.point.y;
        }

        /// <summary>
        /// The first thing a horizontal ray meets, if it is a WALL to the car
        /// (|normal.y| at or under <see cref="WallNormalY"/>). If the first
        /// thing is a floor — the land itself rising into the ray on a cut or
        /// a bank — there is no barrier at this height: the land is the next
        /// surface, and the profile measures it.
        /// </summary>
        static bool FirstBarrier(Vector3 from, Vector3 dir, float len, out float dist, out Collider col)
        {
            dist = 0f; col = null;
            int count = Physics.RaycastNonAlloc(from, dir, EdgeHits, len, NotCars, QueryTriggerInteraction.Ignore);
            System.Array.Sort(EdgeHits, 0, count, ByDistance);
            for (int k = 0; k < count; k++)
            {
                var h = EdgeHits[k];
                if (IsCar(h.collider)) continue;
                if (Mathf.Abs(h.normal.y) > WallNormalY) return false;
                dist = h.distance;
                col = h.collider;
                return true;
            }
            return false;
        }

        /// <summary>Is a BridgeDeck collider anywhere under this point, within
        /// a metre and a half below the road? Any hit, not only the top one:
        /// the kerb strip lies ON the deck.</summary>
        static bool DeckUnder(Vector3 at, float tarmacY)
        {
            int count = Physics.RaycastNonAlloc(new Vector3(at.x, tarmacY + 1f, at.z), Vector3.down, EdgeHits,
                                                2.5f, NotCars, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++)
                if (EdgeHits[k].collider.name.StartsWith("BridgeDeck")) return true;
            return false;
        }

        /// <summary>
        /// Turn one half-section's profile into its findings. ys[k] is the
        /// surface <see cref="EdgePitch"/> × k past <see cref="EdgeStartM"/>
        /// (metres past the tarmac edge), NaN where nothing is under it.
        /// </summary>
        static void MeasureProfile(EdgeHalf h, float[] ys, Collider[] on, int count, float kerb)
        {
            int KAt(float e) => Mathf.Clamp(Mathf.RoundToInt((e - EdgeStartM) / EdgePitch), 0, count - 1);
            float EAt(int k) => EdgeStartM + k * EdgePitch;
            // With the slope tolerance: a foreslope built at exactly 1V:3H is
            // the slope check's to fail, and float rounding must not also make
            // it an edge drop the whole way down.
            float steepPair = (RoadsideRules.TraversableSlope + SlopeNoise) * EdgePitch;

            // EDGE DROP: the height lost across a run of samples steeper than
            // 1V:3H that BEGINS at the tarmac edge or within EdgeDropZoneM of
            // the strip's outer edge. A foreslope at 1V:4H or flatter is never
            // a drop, however far it falls — that is the slope's question; a
            // vertical inch is all drop; a 30-degree safety-edge bevel is its
            // own height. A void under the next sample is a drop of everything.
            //
            // (Colliders are named once, at the end, from the worst sample of
            // each finding: a collider path is a walk up the hierarchy, and the
            // worst sample improves many times per half-section.)
            string nothing = "nothing within " + ProfileDepthM.ToString("0") + " m";
            int dropK = -1, faceK = -1, fallK = -1;
            int zoneEnd = Mathf.Min(count - 2, KAt(kerb + EdgeDropZoneM));
            for (int k = 0; k <= zoneEnd; k++)
            {
                if (float.IsNaN(ys[k])) continue;
                if (float.IsNaN(ys[k + 1]))
                {
                    if (ProfileDepthM > h.drop) { h.drop = ProfileDepthM; h.dropE = EAt(k + 1); dropK = k + 1; }
                    continue;
                }
                int j = k;
                while (j + 1 < count && !float.IsNaN(ys[j + 1]) && ys[j] - ys[j + 1] > steepPair) j++;
                if (j == k) continue;
                float d = ys[k] - ys[j];
                if (d > h.drop) { h.drop = d; h.dropE = EAt(k); dropK = j; }
                k = j - 1;      // a run cannot begin inside another
            }

            // EDGE FACE: the biggest rise within FaceRunM, either way, anywhere
            // across the edge. Pairs 5 and 10 cm apart exactly, and the third
            // sample interpolated back to FaceRunM, so a slope is judged over
            // the run the rule names and a step between any two samples is
            // inside some window whole.
            float over = (RoadsideRules.FaceRunM - 2f * EdgePitch) / EdgePitch;
            for (int k = 0; k < count; k++)
            {
                if (float.IsNaN(ys[k])) continue;
                for (int d = 1; d <= 3 && k + d < count; d++)
                {
                    if (float.IsNaN(ys[k + d])) break;
                    float y = d < 3 ? ys[k + d] : Mathf.Lerp(ys[k + 2], ys[k + 3], over);
                    float rise = y - ys[k];
                    if (Mathf.Abs(rise) <= h.face) continue;
                    h.face = Mathf.Abs(rise);
                    h.faceE = EAt(k);
                    h.faceIn = rise < 0f;        // outer sample lower: met driving back in
                    faceK = rise < 0f ? k : k + Mathf.Min(d, 2);   // the top of the face
                }
            }

            // EDGE SLOPE: the grade over SlopeWindowM, inside the clear zone
            // measured from the strip's outer edge. FALLING grades are the
            // foreslope a car has to climb back up; a RISING grade is a cut's
            // backslope, which RDG practice allows at 1V:3H, so only steeper
            // than that is worth a warning.
            int w = Mathf.RoundToInt(SlopeWindowM / EdgePitch);
            int czFrom = KAt(kerb), czTo = Mathf.Min(count - 1 - w, KAt(kerb + RoadsideRules.ClearZoneM));
            int fFirst = -1, fLast = -1, wFirst = -1, wLast = -1, bFirst = -1, bLast = -1;
            float fGrade = 0f;
            for (int k = czFrom; k <= czTo + 1; k++)
            {
                bool valid = k <= czTo && !float.IsNaN(ys[k]) && !float.IsNaN(ys[k + w]);
                float g = valid ? (ys[k] - ys[k + w]) / SlopeWindowM : 0f;
                bool fail = valid && g > RoadsideRules.SteepestRecoverableSlope + SlopeNoise;
                if (fail) { if (fFirst < 0) { fFirst = k; fGrade = 0f; } fLast = k; fGrade = Mathf.Max(fGrade, g); }
                else if (fFirst >= 0)
                {
                    float len = (fLast - fFirst) * EdgePitch + SlopeWindowM;
                    if (len > h.slopeFail) { h.slopeFail = len; h.slopeE = EAt(fFirst); h.slopeGrade = fGrade; }
                    fFirst = -1;
                }
                if (valid && g > RoadsideRules.RecoverableSlope + SlopeNoise) { if (wFirst < 0) wFirst = k; wLast = k; }
                else if (wFirst >= 0)
                {
                    h.slopeWarn = Mathf.Max(h.slopeWarn, (wLast - wFirst) * EdgePitch + SlopeWindowM);
                    wFirst = -1;
                }
                if (valid && -g > RoadsideRules.BackSlope + SlopeNoise) { if (bFirst < 0) bFirst = k; bLast = k; }
                else if (bFirst >= 0)
                {
                    float len = (bLast - bFirst) * EdgePitch + SlopeWindowM;
                    if (len > h.backslope) { h.backslope = len; h.backE = EAt(bFirst); }
                    bFirst = -1;
                }
            }

            // EDGE FALL: any two points from the tarmac edge out to the warrant
            // reach past the strip, the outer one lower, that RoadsideRules
            // calls critical. A void is a fall with no bottom. Reported whether
            // or not a barrier stands further out: a car falls before it gets
            // there.
            //
            // The walk is RoadsideRules.WorstCriticalFall — the same one the
            // stage plan takes over the section it is about to build, from the
            // same tarmac edge to the same reach, so a wall the plan ends and a
            // fall this reports cannot disagree about where the warrant stops.
            // No slack here: the builder takes the slack, the audit measures.
            int a0 = KAt(0f), bEnd = Mathf.Min(count - 1, KAt(kerb + RoadsideRules.WarrantReachM));
            float worstFall = RoadsideRules.WorstCriticalFall(ys, a0, bEnd, EdgePitch, 0f, out int fallFoot);
            if (fallFoot >= 0)
            {
                h.fall = true;
                h.fallM = float.IsPositiveInfinity(worstFall) ? ProfileDepthM : worstFall;
                h.fallE = EAt(fallFoot);
                fallK = fallFoot;
            }

            if (dropK >= 0) h.dropOn = float.IsNaN(ys[dropK]) ? nothing : Name(on[dropK]);
            if (faceK >= 0) { h.faceOn = Name(on[faceK]); h.faceCol = on[faceK]; }
            if (fallK >= 0) h.fallOn = float.IsNaN(ys[fallK]) ? nothing : Name(on[fallK]);
        }

        /// <summary>
        /// PAST THE REACH, for information. A stage shoulder's carried
        /// foreslope that reaches its depth cap without meeting the lattice
        /// ends in a tuck, and on a hillside falling faster than the carry the
        /// drop there stands 8.7-10.9 m out (Blue Ridge 1227 L, Little
        /// Switzerland 608-611 L in round three's replica) — past EdgeReachM,
        /// where EDGE FACE never looked. An open half-section's profile is
        /// carried on to <see cref="FarReachM"/>, and the biggest rise within
        /// FaceRunM whose window reaches past the main profile is kept, so
        /// those drops are counted before any of them is made to fail: they
        /// lie beyond the clear zone and the warrant, which is what the DOT
        /// asks of a roadside, but a car that ran that wide still meets them.
        /// </summary>
        static void MeasureFarFace(EdgeHalf h, Vector3 edge, Vector3 o, float[] ys, Collider[] on, int count,
                                   float[] farYs, Collider[] farOn)
        {
            int m = 0;
            for (int k = count - FarLead; k < count; k++, m++) { farYs[m] = ys[k]; farOn[m] = on[k]; }
            float e0 = EdgeStartM + (count - FarLead) * EdgePitch;
            float prevY = ys[count - 1];
            for (; m < farYs.Length; m++)
            {
                float e = e0 + m * EdgePitch;
                if (e > FarReachM + 1e-4f) break;
                farYs[m] = ProfileAt(edge + o * e, prevY, out farOn[m], out _);
                if (!float.IsNaN(farYs[m])) prevY = farYs[m];
            }
            // the same windows as MeasureProfile's EDGE FACE
            float over = (RoadsideRules.FaceRunM - 2f * EdgePitch) / EdgePitch;
            int topK = -1;
            for (int k = 0; k < m; k++)
            {
                if (float.IsNaN(farYs[k])) continue;
                for (int d = 1; d <= 3 && k + d < m; d++)
                {
                    if (float.IsNaN(farYs[k + d])) break;
                    if (k + d < FarLead) continue;      // wholly inside the profile: judged there
                    float y = d < 3 ? farYs[k + d] : Mathf.Lerp(farYs[k + 2], farYs[k + 3], over);
                    float rise = y - farYs[k];
                    if (Mathf.Abs(rise) <= h.farFace) continue;
                    h.farFace = Mathf.Abs(rise);
                    h.farFaceE = e0 + k * EdgePitch;
                    h.farFaceIn = rise < 0f;
                    topK = rise < 0f ? k : k + Mathf.Min(d, 2);
                }
            }
            if (topK >= 0) h.farFaceOn = Name(farOn[topK]);
        }

        /// <summary>Group the half-sections a predicate picks into runs of
        /// consecutive stations per side, worst station named. On a loop a run
        /// that crosses the start line stays one run.</summary>
        static void EdgeRuns(List<EdgeRun> into, EdgeHalf[,] halves, int n, bool loop, string label, bool fail,
                             System.Func<EdgeHalf, bool> pick, System.Func<EdgeHalf, float> metric,
                             float threshold, System.Func<EdgeHalf, string> what)
        {
            for (int si = 0; si < 2; si++)
            {
                bool Hit(int i) => halves[i, si].measured && pick(halves[i, si]);
                int start = 0;
                if (loop)
                {
                    start = -1;
                    for (int i = 0; i < n; i++) if (!Hit(i)) { start = i; break; }
                    if (start < 0) start = 0;        // every station: one run
                }
                EdgeRun run = null;
                for (int step = 0; step < n; step++)
                {
                    int i = (start + step) % n;
                    if (!Hit(i)) { run = null; continue; }
                    var h = halves[i, si];
                    float m = metric(h);
                    if (run == null)
                    {
                        run = new EdgeRun { label = label, fail = fail, side = si == 0 ? -1 : 1, from = i, worst = float.MinValue };
                        into.Add(run);
                    }
                    run.to = i;
                    run.stations++;
                    if (m > run.worst)
                    {
                        run.worst = m;
                        run.severity = m / Mathf.Max(threshold, 1e-4f);
                        run.what = what(h) + " (worst at wp " + i + ")";
                        run.where = h.edge;
                    }
                }
            }
        }

        /// <summary>One summary line per finding kind. A line that matches a
        /// failure pattern in tools\verify.ps1 (FAIL, or the finding's name in
        /// capitals) is printed ONLY when something failed: ok and warn lines
        /// are lower case, and verify matches case-sensitively.</summary>
        static void EdgeLine(StringBuilder log, List<EdgeRun> runs, string failLabel, string warnLabel, float spacing,
                             string failWhat, string warnWhat, string okWhat)
        {
            int fStations = 0, fRuns = 0, wStations = 0, wRuns = 0;
            foreach (var r in runs)
            {
                if (failLabel != null && r.label == failLabel) { fRuns++; fStations += r.stations; }
                if (warnLabel != null && r.label == warnLabel) { wRuns++; wStations += r.stations; }
            }
            if (fRuns > 0)
                log.AppendLine(string.Format("  FAIL {0} on {1} half-sections in {2} runs ({3:0} m of edge): {4}",
                                             failLabel, fStations, fRuns, fStations * spacing, failWhat));
            if (wRuns > 0)
                log.AppendLine(string.Format("  {0} on {1} half-sections in {2} runs ({3:0} m of edge): {4}",
                                             warnLabel, wStations, wRuns, wStations * spacing, warnWhat));
            if (fRuns == 0 && okWhat != null)
                log.AppendLine("  ok   " + okWhat);
        }

        /// <summary>
        /// DECK RAIL. Along every railed half-section (<paramref name="structure"/>:
        /// the deck stations and the full-height part of each approach run),
        /// every <see cref="RailPitchM"/>: from inside the tarmac edge,
        /// horizontal casts at each of RoadsideRules.BarrierRayHeights must meet
        /// a barrier (see <see cref="RailAt"/>) before the deck's own edge, give
        /// or take <see cref="RailEdgeSlackM"/>. Per station was how 7.75 m of
        /// unrailed deck at each end of the parkway's spans survived every
        /// probe that looked for it at 8 m pitch. Only chords with a railed
        /// station at BOTH ends are cast: the chord leaving the last one is the
        /// run's terminal.
        /// </summary>
        static void AuditDeckRails(TrackPath path, EdgeHalf[,] halves, bool[] structure, bool[] deckStation,
                                   bool loop, float half, int roadMask, List<EdgeRun> runs,
                                   ref int gaps, ref float gapM, ref int stations, ref int unmeasuredCasts)
        {
            int n = path.Count;
            int perChord = Mathf.Max(1, Mathf.RoundToInt(path.spacing / RailPitchM));
            for (int si = 0; si < 2; si++)
            {
                float side = si == 0 ? -1f : 1f;

                // The deck's lateral edge, measured where there is a deck, and
                // carried to the approach stations from the nearest one: an
                // approach rail stands where the parapet stood.
                var reach = new float[n];
                for (int i = 0; i < n; i++)
                {
                    reach[i] = -1f;
                    if (!deckStation[i] || !halves[i, si].measured) continue;
                    Vector3 c = path.GetPoint(i);
                    Vector3 o = RightAt(path, i) * side;
                    float edge = -1f;
                    for (float e = 0f; e <= EdgeReachM; e += 0.25f)
                        if (DeckUnder(c + o * (half + e), halves[i, si].tarmacY)) edge = e;
                    reach[i] = edge;
                }
                // Only as far as the approach reaches: a structure station is
                // never further than that from its deck, and a search the
                // length of the stage would take some other bridge's width.
                for (int i = 0; i < n; i++)
                {
                    if (!structure[i] || reach[i] > 0f) continue;
                    float best = -1f;
                    for (int k = 1; k <= RoadsideRules.ApproachRailStations && best < 0f; k++)
                        for (int dir = -1; dir <= 1; dir += 2)
                        {
                            int j = i + dir * k;
                            int jj = loop ? path.Wrap(j) : j;
                            if (jj < 0 || jj >= n || !deckStation[jj]) continue;
                            if (reach[jj] > 0f && (best < 0f || reach[jj] > best)) best = reach[jj];
                        }
                    reach[i] = best > 0f ? best : EdgeReachM;
                }

                int start = 0;
                if (loop)
                {
                    start = -1;
                    for (int i = 0; i < n; i++) if (!structure[i]) { start = i; break; }
                    if (start < 0) start = 0;
                }
                int gapSamples = 0, gapFrom = -1, gapTo = -1;
                bool gapOnDeck = false;
                for (int step = 0; step <= n; step++)
                {
                    int i = (start + step) % n;
                    bool railedHere = step < n && structure[i];
                    if (railedHere) stations++;
                    bool need = railedHere && (loop || i < n - 1) && structure[(i + 1) % n];
                    for (int s = 0; s < perChord && need; s++)
                    {
                        int state = RailAt(path, i, (float)s / perChord, side, half, reach[i], roadMask);
                        if (state == 0)
                        {
                            float dt = RailConfirmM / path.spacing;
                            int before = RailAt(path, i, (float)s / perChord - dt, side, half, reach[i], roadMask);
                            int after = RailAt(path, i, (float)s / perChord + dt, side, half, reach[i], roadMask);
                            if (before == 1 || after == 1) state = 1;
                        }
                        if (state < 0) { unmeasuredCasts++; continue; }
                        if (state == 0)
                        {
                            if (gapSamples == 0) { gapFrom = i; gapOnDeck = false; }
                            gapSamples++;
                            gapTo = i;
                            gapOnDeck |= deckStation[i];
                            continue;
                        }
                        CloseRailGap(path, halves, si, runs, ref gapSamples, gapFrom, gapTo, gapOnDeck, reach, ref gaps, ref gapM);
                    }
                    if (!need) CloseRailGap(path, halves, si, runs, ref gapSamples, gapFrom, gapTo, gapOnDeck, reach, ref gaps, ref gapM);
                }
            }
        }

        static void CloseRailGap(TrackPath path, EdgeHalf[,] halves, int si, List<EdgeRun> runs, ref int gapSamples,
                                 int from, int to, bool onDeck, float[] reach, ref int gaps, ref float gapM)
        {
            if (gapSamples == 0) return;
            float len = gapSamples * RailPitchM;
            gapSamples = 0;
            if (len < RoadsideRules.DeckRailGapFailM) return;
            gaps++;
            gapM += len;
            int stations = to >= from ? to - from + 1 : to + path.Count - from + 1;
            runs.Add(new EdgeRun
            {
                label = "DECK RAIL", fail = true, side = si == 0 ? -1 : 1, from = from, to = to, stations = stations,
                worst = len, severity = len / RoadsideRules.DeckRailGapFailM,
                what = string.Format("about {0:0.0} m of {1} edge with nothing solid within {2:0.00} m of the tarmac edge",
                                     len, onDeck ? "DECK" : "approach", reach[from] + RailEdgeSlackM),
                where = halves[from, si].edge,
            });
        }

        /// <summary>1 when a barrier stands across the edge at <paramref
        /// name="t"/> of the chord from station i, 0 for a gap, -1 when there is
        /// no tarmac under the cast to measure from.
        ///
        /// A barrier is a WALL-class hit (<see cref="FirstBarrier"/>). A cast
        /// that meets a floor first — land rising through body height — does
        /// not close the edge: at that grade the car's crash response calls the
        /// contact a landing and the car climbs it, so a hump beside a deck is
        /// a ramp toward the drop, not a rail in front of it.</summary>
        static int RailAt(TrackPath path, int i, float t, float side, float half, float reachE, int roadMask)
        {
            int i0 = i, i1 = i + 1;
            if (t < 0f) { t += 1f; i0 = i - 1; i1 = i; }
            else if (t >= 1f) { t -= 1f; i0 = i + 1; i1 = i + 2; }
            Vector3 c = Vector3.Lerp(path.GetPoint(i0), path.GetPoint(i1), t);
            Vector3 r = Vector3.Lerp(RightAt(path, i0), RightAt(path, i1), t);
            r.y = 0f;
            Vector3 o = r.normalized * side;
            Vector3 inset = c + o * (half - TarmacInsetM);
            if (!Physics.Raycast(inset + Vector3.up * 3f, Vector3.down, out RaycastHit tar, 6f, roadMask,
                                 QueryTriggerInteraction.Ignore))
                return -1;
            float len = reachE + TarmacInsetM + RailEdgeSlackM;
            foreach (float bh in RoadsideRules.BarrierRayHeights)
            {
                var from = new Vector3(inset.x, tar.point.y + bh, inset.z);
                if (FirstBarrier(from, o, len, out _, out _)) return 1;
            }
            return 0;
        }

        /// <summary>
        /// RUN END. A barrier run that stops, followed within the approach-rail
        /// distance by a critical fall with nothing guarding it: the end of the
        /// wall is where the car goes over. Counted once per end.
        /// </summary>
        static int AuditRunEnds(EdgeHalf[,] halves, int n, bool loop, List<EdgeRun> runs)
        {
            int ends = 0;
            for (int si = 0; si < 2; si++)
                for (int i = 0; i < n; i++)
                {
                    var h = halves[i, si];
                    if (!h.measured || !h.barrier) continue;
                    foreach (int dir in new[] { 1, -1 })
                    {
                        int next = i + dir;
                        if (loop) next = (next + n) % n;
                        else if (next < 0 || next >= n) continue;
                        if (halves[next, si].measured && halves[next, si].barrier) continue;   // not an end
                        for (int k = 1; k <= RoadsideRules.ApproachRailStations; k++)
                        {
                            int m = i + dir * k;
                            if (loop) m = ((m % n) + n) % n;
                            else if (m < 0 || m >= n) break;
                            var hm = halves[m, si];
                            if (!hm.measured) continue;
                            if (hm.barrier) break;                // the next run began first
                            if (!hm.fall) continue;
                            ends++;
                            runs.Add(new EdgeRun
                            {
                                label = "RUN END", fail = true, side = si == 0 ? -1 : 1, from = i, to = i, stations = 1,
                                worst = hm.fallM, severity = hm.fallM / RoadsideRules.CriticalFallM,
                                what = string.Format("{0} ends at wp {1}; a {2:0.00} m critical fall begins {3} station(s) {4}, at wp {5}",
                                                     h.barrierOn, i, hm.fallM, k, dir > 0 ? "on" : "back", m),
                                where = h.edge,
                            });
                            break;
                        }
                    }
                }
            return ends;
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
        /// <see cref="AuditEdges"/> asks this question ACROSS the edge, and
        /// TerrainAudit asks whether the ground is above the tarmac at three
        /// points per waypoint. Neither is this: three probes every 4.7 m cannot
        /// see a ridge of hillside that surfaces between two of them. The
        /// report was "sections of the parkway have mountains clipping through
        /// that launch cars into the air", on a track the audits of the day
        /// called clean.
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
