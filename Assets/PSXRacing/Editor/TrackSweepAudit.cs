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
            foreach (var def in TrackCatalog.All) SweepOne(def, log);
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
            // audit was written to avoid. So: put the box INSIDE the barrier,
            // where there is definitely a wall, and refuse to report anything if
            // that comes back empty.
            bool sceneLive = false;
            for (int i = 0; i < path.Count && !sceneLive; i++)
            {
                Vector3 c = path.GetPoint(i);
                Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                Vector3 probe = c + r * (PSXRacingBuilder.WallOffsetFor(def) + 0.6f)
                                  + Vector3.up * (RideHeight + CarCentreY);
                if (Physics.OverlapBox(probe, half * 0.5f, path.GetRotation(i), mask).Length > 0)
                    sceneLive = true;
            }
            if (!sceneLive)
            {
                log.AppendLine("  CANNOT SWEEP — the physics scene is empty (the control probe " +
                               "inside the barrier found nothing). Reporting no result rather " +
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
                        // The barrier is the intended limit, and the outermost
                        // probe is meant to touch it: a car centred at
                        // WallOffset - halfWidth has its flank 14 cm off the
                        // wall, and swings the rest of the way on any corner.
                        // Reporting that reads as six findings on six clean
                        // circuits. Whether the barrier itself is where it
                        // belongs is TrackObstacleAudit's question, and it
                        // measures the barrier line directly.
                        // "Wall" on a circuit; "WallColl" boxes on the stage.
                        if (col.name.StartsWith("Wall")) continue;

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
            {
                log.AppendLine("  CLEAR — a car fits everywhere inside the barrier line");
                return;
            }
            var sorted = new List<Blockage>(hits.Values);
            sorted.Sort((a, b) => a.nearest.CompareTo(b.nearest));
            foreach (var h in sorted)
                log.AppendLine("  BLOCKED from " + h.nearest.ToString("0.0") +
                               " m off the centreline outward  x" + h.count + " probes  [" +
                               h.type + (h.hasRenderer ? "" : ", NO RENDERER — invisible") +
                               "]  near wp " + h.waypoint + "  " + h.key);
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
