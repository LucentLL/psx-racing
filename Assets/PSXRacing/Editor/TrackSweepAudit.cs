using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Drive a box the size of the player's car over every square metre of every
    /// circuit and report anything it touches.
    ///
    /// <see cref="TrackObstacleAudit"/> asks "is any collider's geometry inside
    /// the barrier line", which it answers well for boxes and badly for concave
    /// meshes — Unity gives those no ClosestPoint, so the road, the ground and
    /// every bridge deck are unmeasurable there and get skipped. Those three are
    /// surfaces you are supposed to be driving on, so skipping them is right,
    /// but it does leave a hole: a deck's abutment cap, a fold in the ground, a
    /// road ribbon crossing itself, are all concave mesh and all invisible to
    /// that test.
    ///
    /// This asks the question the other way round, and the way the player asks
    /// it: PUT THE CAR THERE. If a box the size of the car, sitting where the
    /// car sits, overlaps something at that station, then a car driving there
    /// stops — whatever kind of collider it is.
    ///
    /// Then the same box the other way: seated on the land BESIDE the road and
    /// stepped back on (<see cref="SweepReentry"/>), because getting back on is
    /// where the body box's overhang meets faces the wheel rays never see.
    ///
    /// Menu: PSX Racing/Sweep Track For Blockages.
    /// </summary>
    public static class TrackSweepAudit
    {
        // The player's collider, straight out of BuildCars. Kept as literals
        // rather than read from a prefab because the grid builds cars in code:
        // there is no prefab to read, and a sweep with the wrong box is a sweep
        // that answers about a car nobody drives.
        static readonly Vector3 CarSize = new Vector3(1.72f, 1.0f, 4.1f);
        const float CarCentreY = 0.72f;
        /// <summary>Where the body sits above the tarmac at rest. The grid seats
        /// cars at +0.35 and the suspension settles from there; taking the lower
        /// figure makes the sweep pessimistic, which is the right direction for
        /// a test whose false negatives are shipped bugs.</summary>
        const float RideHeight = 0.05f;
        /// <summary>Lateral step. Half a metre is finer than the 0.86 m of clear
        /// air either side of the car inside a 10.5 m road, so nothing narrow
        /// can hide between two probes.</summary>
        const float LateralStep = 0.5f;

        [MenuItem("PSX Racing/Sweep Track For Blockages")]
        public static void Run()
        {
            var log = new StringBuilder();
            foreach (var def in TrackCatalog.Scened) SweepOne(def, log);
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_sweep_audit.txt"),
                log.ToString());
        }

        static void SweepOne(TrackCatalog.TrackDef def, StringBuilder log)
        {
            string scenePath = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
            if (!System.IO.File.Exists(scenePath)) { log.AppendLine("MISSING SCENE " + scenePath); return; }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count == 0) { log.AppendLine("no TrackPath in " + def.id); return; }

            log.AppendLine("");
            log.AppendLine("sweep — " + def.id);

            // Cars are on layer 2 and are traffic, not obstacles.
            int mask = ~(1 << 2);
            float reach = PSXRacingBuilder.WallOffsetFor(def) - CarSize.x * 0.5f;
            Vector3 half = CarSize * 0.5f;

            // CONTROL PROBE, and the reason this tool is allowed to use physics
            // queries at all. An edit-mode physics scene that never got populated
            // answers every OverlapBox with "nothing", which is indistinguishable
            // from a clean circuit and is the exact failure the bounds-based
            // audit was written to avoid. So: put the box INSIDE the barrier, or
            // failing that on the tarmac, where there is definitely a collider,
            // and refuse to report anything if every one comes back empty.
            bool sceneLive = false;
            for (int i = 0; i < path.Count && !sceneLive; i++)
            {
                Vector3 c = path.GetPoint(i);
                Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                Vector3 probe = c + r * (PSXRacingBuilder.WallOffsetFor(def) + 0.6f)
                                  + Vector3.up * (RideHeight + CarCentreY);
                if (Physics.OverlapBox(probe, half * 0.5f, path.GetRotation(i), mask).Length > 0)
                    sceneLive = true;
                // OR THE TARMAC. A stage has walls only where one is warranted
                // now (RoadsideRules), and Emerald Isle has none at all, so a
                // barrier-only control read a live scene as an empty one there
                // and refused to sweep it. A thin box straddling the ribbon's
                // surface at the centreline finds the Road collider on any venue.
                else if (Physics.OverlapBox(c + Vector3.up * PSXRacingBuilder.RoadLift,
                                            new Vector3(0.5f, 0.05f, 0.5f), path.GetRotation(i), mask).Length > 0)
                    sceneLive = true;
            }
            if (!sceneLive)
            {
                log.AppendLine("  CANNOT SWEEP — the physics scene is empty (the control probes " +
                               "at the barrier line and on the tarmac found nothing). Reporting no result rather " +
                               "than a false all-clear.");
                return;
            }

            var hits = new Dictionary<string, Blockage>();
            int probes = 0;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 c = path.GetPoint(i);
                Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                Quaternion rot = path.GetRotation(i);

                for (float off = -reach; off <= reach + 0.001f; off += LateralStep)
                {
                    Vector3 at = c + right * off + Vector3.up * (RideHeight + CarCentreY);
                    probes++;
                    foreach (var col in Physics.OverlapBox(at, half, rot, mask))
                    {
                        if (col == null) continue;
                        // The surfaces the car RESTS on. A box seated 5 cm over
                        // the tarmac does not overlap the ribbon at +0.12, but a
                        // bridge deck rises to meet the road and the ground runs
                        // 0.21 m under it, so on a crest either can clip the
                        // bottom face by a centimetre. That is the car standing
                        // on the road, not the road blocking the car.
                        if (col.gameObject.layer == LayerMask.NameToLayer("Road")) continue;
                        // "Ground" on a circuit; "GroundN_x_z" chunks on the stage.
                        if (col.name.StartsWith("Ground") || col.name.StartsWith("BridgeDeck")) continue;
                        // The shoulder surface beside the kerb strip: a surface
                        // a wheel rolls on, exactly like Ground, and off the
                        // Road layer on purpose (it gives off-road grip). Its
                        // first vertex starts at the back edge of the kerb, and
                        // on a KerbStyle.Street venue that is the pavement at
                        // +0.28 over the waypoint plane — one centimetre into
                        // this box's floor at +0.27 — so without this line
                        // every street circuit reports BLOCKED down both
                        // sides. Whether its SHAPE is drivable is
                        // TrackObstacleAudit.AuditEdges' question, and the
                        // re-entry sweep below puts the car on it. By prefix,
                        // so a chunk named "RoadEdge_<n>" is exempt as well.
                        if (col.name.StartsWith("RoadEdge")) continue;
                        // The barrier is the intended limit, and the outermost
                        // probe is meant to touch it: a car centred at
                        // WallOffset - halfWidth has its flank 14 cm off the
                        // wall, and swings the rest of the way on any corner.
                        // Reporting that reads as six findings on six clean
                        // circuits. Whether the barrier itself is where it
                        // belongs is TrackObstacleAudit's question, and it
                        // measures the barrier line directly.
                        // "Wall" on a circuit; "WallColl" boxes on the stage.
                        //
                        // ...but only OUTSIDE the carriageway. A wall standing
                        // ON the road is still a wall, and excusing it by name
                        // is how a guard-rail chord over a 22 m ramp radius
                        // crossed both Charlotte freeways and reached the
                        // owner unplayed (2026-09-08).
                        if (col.name.StartsWith("Wall") &&
                            Mathf.Abs(off) > def.roadWidth * 0.5f) continue;

                        string key = Key(col.transform);
                        if (!hits.TryGetValue(key, out var h))
                            hits[key] = h = new Blockage { key = key, nearest = float.MaxValue };
                        h.count++;
                        if (Mathf.Abs(off) < h.nearest)
                        {
                            h.nearest = Mathf.Abs(off);
                            h.waypoint = i;
                            h.type = col.GetType().Name;
                            h.hasRenderer = col.GetComponentInChildren<MeshRenderer>() != null;
                        }
                    }
                }
            }

            log.AppendLine("  " + probes + " car-sized probes across +/-" +
                           reach.ToString("0.0") + " m of every waypoint" +
                           " (the barrier itself excluded — see TrackObstacleAudit for that)");
            if (hits.Count == 0)
                log.AppendLine("  CLEAR — a car fits everywhere inside the barrier line");
            else
            {
                var sorted = new List<Blockage>(hits.Values);
                sorted.Sort((a, b) => a.nearest.CompareTo(b.nearest));
                foreach (var h in sorted)
                    log.AppendLine("  BLOCKED from " + h.nearest.ToString("0.0") +
                                   " m off the centreline outward  x" + h.count + " probes  [" +
                                   h.type + (h.hasRenderer ? "" : ", NO RENDERER — invisible") +
                                   "]  near wp " + h.waypoint + "  " + h.key);
            }

            SweepReentry(def, path, log);
        }

        // ==================================================================
        //  RE-ENTRY: the car beside the road, driven back on
        // ==================================================================
        //
        // Everything above asks whether a car ON the road fits. The owner's
        // report was the other direction — "it is difficult to drive back onto
        // tracks" — and a car coming back on is a different shape of problem:
        // the wheels are rays that sit on the dirt while the flat-bottomed body
        // box overhangs them by 13 cm at the side and most of a metre at the
        // nose, so the first thing to meet a shoulder face is the body, at a
        // height the wheels never see. TrackObstacleAudit's edge pass measures
        // the land with rays at 5 cm; this puts the actual box on it.
        //
        // So: seat the box on the land beside the road, wheels on the surface
        // and the body RoadsideRules.CarClearanceFloorM over their plane (the
        // lowest ride height any setup reaches), and step it in toward the
        // tarmac a decimetre at a time at three headings. The first thing it
        // penetrates that pushes it back AWAY from the road with a wall-class
        // normal is a RE-ENTRY FACE. Every other station: a face is a
        // cross-section property and the edge pass already walks every one.

        /// <summary>Stations between re-entry sweeps (8 m).</summary>
        const int ReentryEvery = 2;
        /// <summary>Metres past the kerb strip's outer edge the car starts, or
        /// just inside the barrier if one stands nearer.</summary>
        const float ReentryStartM = 3f;
        const float ReentryStepM = 0.1f;
        /// <summary>Heading relative to the road: 0 is sliding back on
        /// parallel to it, 60 is nosing in. The long side and the nose overhang
        /// meet a face at different heights.</summary>
        static readonly float[] ReentryYaws = { 0f, 30f, 60f };
        /// <summary>Wheel contact points inset from the body box: the pack
        /// cars overhang their wheels by 0.06-0.26 m at the side and 0.46-1.21
        /// m at the ends (synthesis, measured off the baked shells). A middle
        /// figure; an audit of one car would be an audit of nobody's car.
        /// </summary>
        const float WheelInsetX = 0.15f, WheelInsetZ = 0.8f;
        /// <summary>|dir.y| at or under this is a wall to the car:
        /// CollisionResponder.LandingNormalDot (0.7, private there).</summary>
        const float FaceNormalY = 0.7f;
        /// <summary>Penetration shallower than this is contact, not a face.
        /// </summary>
        const float PenetrationTol = 0.01f;
        /// <summary>A penetration that pushes the car back out along the road's
        /// outward direction by at least this much of its length is a face the
        /// car is driving INTO. One pushing it inward is the barrier behind it.
        /// </summary>
        const float AwayFromRoad = 0.3f;
        const int ReentryListed = 12;

        static void SweepReentry(TrackCatalog.TrackDef def, TrackPath path, StringBuilder log)
        {
            int roadLayer = LayerMask.NameToLayer("Road");
            if (roadLayer < 0) { log.AppendLine("  re-entry not swept: no layer is named Road"); return; }
            int mask = ~(1 << 2);
            float half = path.roadWidth * 0.5f;
            float kerb = PSXRacingBuilder.KerbWidth;
            // The box has to be a real collider for ComputePenetration. Parked
            // far below the world, on the cars' layer, which every query here
            // masks out.
            var probeGo = new GameObject("ReentryProbe") { layer = 2 };
            probeGo.transform.position = new Vector3(0f, -10000f, 0f);
            var box = probeGo.AddComponent<BoxCollider>();
            box.size = CarSize;
            Physics.SyncTransforms();
            Vector3 half3 = CarSize * 0.5f;
            // Wheel contacts inset from the box's own corners, so the middle
            // of the four is straight under the middle of the box.
            float wx = half3.x - WheelInsetX;
            float zFront = half3.z - WheelInsetZ, zRear = -zFront;

            int tried = 0, faces = 0, seated = 0;
            var found = new List<(float rise, string line)>();
            var contacts = new Vector3[4];
            try
            {
                for (int i = 0; i < path.Count; i += ReentryEvery)
                {
                    Vector3 c = path.GetPoint(i);
                    Vector3 tan = path.GetTangent(i); tan.y = 0f; tan.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 outward = right * side;
                        Vector3 inset = c + outward * (half - 0.3f);
                        if (!Physics.Raycast(inset + Vector3.up * 3f, Vector3.down, out RaycastHit tar, 6f,
                                             1 << roadLayer, QueryTriggerInteraction.Ignore)) continue;
                        float tarmacY = tar.point.y;
                        float startE = kerb + ReentryStartM;
                        foreach (float bh in RoadsideRules.BarrierRayHeights)
                            if (Physics.Raycast(new Vector3(inset.x, tarmacY + bh, inset.z), outward, out RaycastHit wall,
                                                startE + 3f, mask, QueryTriggerInteraction.Ignore) &&
                                Mathf.Abs(wall.normal.y) <= FaceNormalY)
                                startE = Mathf.Min(startE, wall.distance - 0.3f - 2.3f);
                        if (startE < kerb + ReentryStepM) continue;   // the barrier is the shoulder: nothing to come back from

                        foreach (float yaw in ReentryYaws)
                        {
                            tried++;
                            Vector3 inward = -outward;
                            Vector3 fwd = (Mathf.Cos(yaw * Mathf.Deg2Rad) * tan +
                                           Mathf.Sin(yaw * Mathf.Deg2Rad) * inward).normalized;
                            Vector3 carRight = Vector3.Cross(Vector3.up, fwd).normalized;
                            float prevY = tarmacY;
                            bool anySeat = false;
                            for (float e = startE; e >= -2.3f; e -= ReentryStepM)
                            {
                                Vector3 root = c + outward * (half + e);
                                bool ok = true;
                                int w = 0;
                                foreach (float z in new[] { zRear, zFront })
                                    foreach (float x in new[] { -wx, wx })
                                    {
                                        Vector3 p = root + carRight * x + fwd * z;
                                        if (!SeatAt(p, Mathf.Max(prevY, tarmacY) + 1.5f, mask, out contacts[w])) ok = false;
                                        w++;
                                    }
                                if (!ok) continue;
                                anySeat = true;
                                // rear-left, rear-right, front-left, front-right
                                Vector3 up = Vector3.Cross(contacts[3] - contacts[0], contacts[2] - contacts[1]);
                                if (up.y < 0f) up = -up;
                                up.Normalize();
                                if (up.y < 0.3f) break;                   // standing on a wall: stop this heading
                                Vector3 f = Vector3.ProjectOnPlane(fwd, up).normalized;
                                var rot = Quaternion.LookRotation(f, up);
                                Vector3 mid = (contacts[0] + contacts[1] + contacts[2] + contacts[3]) * 0.25f;
                                prevY = mid.y;
                                Vector3 centre = mid + up * (RoadsideRules.CarClearanceFloorM + half3.y);

                                Collider hitCol = null;
                                foreach (var col in Physics.OverlapBox(centre, half3, rot, mask, QueryTriggerInteraction.Ignore))
                                {
                                    if (col == null || col == box) continue;
                                    if (col.attachedRigidbody != null && col.GetComponentInParent<CarController>() != null) continue;
                                    if (!Physics.ComputePenetration(box, centre, rot, col, col.transform.position,
                                                                    col.transform.rotation, out Vector3 dir, out float dist))
                                        continue;
                                    if (dist < PenetrationTol || Mathf.Abs(dir.y) > FaceNormalY) continue;
                                    Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
                                    if (Vector3.Dot(flat, outward) < AwayFromRoad) continue;
                                    hitCol = col;
                                    break;
                                }
                                if (hitCol == null) continue;

                                faces++;
                                // How much higher the land is under the box's
                                // inward edge than the wheels it is riding on.
                                Vector3 lead = centre + inward * (half3.x * Mathf.Abs(Vector3.Dot(carRight, inward)) +
                                                                  half3.z * Mathf.Abs(Vector3.Dot(fwd, inward)));
                                float rise = SeatAt(lead, mid.y + 3f, mask, out Vector3 leadAt) ? leadAt.y - mid.y : 0f;
                                found.Add((rise, string.Format(
                                    "    RE-ENTRY FACE wp {0} {1}, heading {2:0} deg: body meets {3} with the car at {4:0.00} m past the tarmac edge; land ahead {5:+0.00;-0.00} m over the wheels  [{6:0.0}, {7:0.0}, {8:0.0}]",
                                    i, side < 0f ? "left" : "right", yaw, Key(hitCol.transform), e, rise,
                                    root.x, mid.y, root.z)));
                                break;
                            }
                            if (anySeat) seated++;
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(probeGo);
            }

            if (tried > 0 && seated == 0)
            {
                log.AppendLine("  CANNOT SWEEP RE-ENTRY — no wheel ray found land beside the road on any of " + tried +
                               " attempts. Reporting no result rather than a false all-clear.");
                return;
            }
            log.AppendLine("  re-entry: " + seated + " approaches seated beside the road (every " +
                           ReentryEvery * path.spacing + " m, both sides, " + ReentryYaws.Length + " headings)");
            if (faces == 0)
            {
                log.AppendLine("  re-entry clear - the body box reaches the tarmac from " +
                               ReentryStartM.ToString("0") + " m out without meeting a face");
                return;
            }
            found.Sort((a, b) => b.rise.CompareTo(a.rise));
            log.AppendLine("  RE-ENTRY FACE on " + faces + " of " + seated + " approaches; tallest first:");
            for (int k = 0; k < Mathf.Min(ReentryListed, found.Count); k++) log.AppendLine(found[k].line);
            if (found.Count > ReentryListed)
                log.AppendLine("    ... and " + (found.Count - ReentryListed) + " more not listed");
        }

        /// <summary>Where a zero-radius wheel ray from above lands: the first
        /// non-car surface under the point, whatever kind of collider.</summary>
        static bool SeatAt(Vector3 p, float fromY, int mask, out Vector3 at)
        {
            at = p;
            if (!Physics.Raycast(new Vector3(p.x, fromY, p.z), Vector3.down, out RaycastHit hit, 60f, mask,
                                 QueryTriggerInteraction.Ignore))
                return false;
            at = hit.point;
            return true;
        }

        class Blockage
        {
            public string key, type;
            public int count, waypoint;
            public float nearest;
            public bool hasRenderer;
        }

        static string Key(Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
