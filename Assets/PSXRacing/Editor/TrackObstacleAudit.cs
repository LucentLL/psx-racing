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
            foreach (var def in TrackCatalog.All) AuditOne(def, log);

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
