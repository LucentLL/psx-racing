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
