using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// DRIVE THE REAL CAR INTO THE REAL TREES, IN THE RUNNING GAME.
    ///
    /// <see cref="TreeCrashSim"/> proved a box on a slab is stopped by a capsule
    /// the table stands up. The owner then drove the shipped game "straight
    /// through a tree without impact" on Mount Mitchell — so the question that
    /// harness cannot answer is the one that matters: in the race scene as it
    /// loads, with the player's own car, the builder's own table and the
    /// forest's own billboards, does a tree stop the car — and does it stop it
    /// WHERE THE TREE IS DRAWN, not only on a thirty-centimetre axis inside a
    /// crown seven metres wide?
    ///
    /// For the trees nearest the road it drives the car at the trunk dead on,
    /// and again offset to the side by a fraction of the card's width, which
    /// is where a driver who "hit a tree" actually hits it.
    ///
    /// Venue: PSX_TREE_VENUE (default MtMitchell). Menu: PSX Racing/Check Tree
    /// Trunks (play mode). Report: PSXRacing_tree_play_check.txt.
    /// </summary>
    public static class TreePlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Tree Trunks (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            string id = System.Environment.GetEnvironmentVariable("PSX_TREE_VENUE");
            if (string.IsNullOrEmpty(id)) id = "MtMitchell";
            int t = -1;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) { t = i; break; }
            var scenes = EditorBuildSettings.scenes;
            if (t < 0 || TrackCatalog.SceneIndex(t) >= scenes.Length ||
                !System.IO.File.Exists(scenes[TrackCatalog.SceneIndex(t)].path))
            {
                Check(false, id + ": scene is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }

            Line("=== THE REAL CAR INTO THE REAL TREES: " + TrackCatalog.At(t).name + " ===");
            EditorSceneManager.OpenScene(scenes[TrackCatalog.SceneIndex(t)].path);
            ReverseRaceCheck.Prime(t);

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TreePlayCheckRunner").AddComponent<TreePlayCheckRunner>();
        }

        internal static void Line(string s) => log.AppendLine(s);

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "TREES STOP CARS." : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                                       "PSXRacing_tree_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    /// <summary>Runs the cases from inside play mode; see TreePlayCheck.</summary>
    public class TreePlayCheckRunner : MonoBehaviour
    {
        const float Speed = 80f / 3.6f;
        const int SolidLayer = TreeTrunks.SolidLayer;

        struct Pick { public int i; public float road; public Vector3 toTree; }

        IEnumerator Start()
        {
            float until = Time.realtimeSinceStartup + 40f;
            while ((RaceManager.Instance == null || RaceManager.Instance.State != RaceManager.RaceState.Racing)
                   && Time.realtimeSinceStartup < until) yield return null;

            var rm = RaceManager.Instance;
            var table = FindFirstObjectByType<TreeTrunks>();
            TreePlayCheck.Check(rm != null && rm.playerCar != null && rm.path != null, "the race is live with a player car");
            TreePlayCheck.Check(table != null && table.Count > 0, "the scene carries a trunk table",
                                table != null ? table.Count + " trunks" : "none");
            if (rm == null || rm.playerCar == null || table == null || table.Count == 0) { Done(); yield break; }

            var car = rm.playerCar;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            TreePlayCheck.Check(table.LiveColliders > 0, "trunks are standing round the grid before anything is asked",
                                table.LiveColliders + " capsules");

            // Nothing may fight the harness for the car.
            foreach (var mb in car.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;

            // The trees a driver can actually reach: nearest the tarmac first.
            var path = rm.path;
            var picks = new List<Pick>();
            for (int i = 0; i < table.Count; i++)
            {
                Vector3 b = table.BaseOf(i);
                float best = float.MaxValue; int at = -1;
                for (int w = 0; w < path.Count; w += 2)
                {
                    Vector3 p = path.waypoints[w];
                    float dx = p.x - b.x, dz = p.z - b.z, d2 = dx * dx + dz * dz;
                    if (d2 < best) { best = d2; at = w; }
                }
                if (at < 0 || best > 22f * 22f) continue;
                Vector3 to = b - path.waypoints[at]; to.y = 0f;
                picks.Add(new Pick { i = i, road = Mathf.Sqrt(best), toTree = to.normalized });
            }
            picks.Sort((a, c) => a.road.CompareTo(c.road));
            TreePlayCheck.Line("  " + picks.Count + " trunks stand within 22 m of the centreline; nearest " +
                               (picks.Count > 0 ? picks[0].road.ToString("0.0") + " m" : "-"));

            int wanted = 6, run = 0, tried = 0;
            foreach (var pk in picks)
            {
                if (run >= wanted || tried >= 60) break;
                tried++;
                // Dead on, then offset by a car's half-width and a bit: the hit
                // a driver makes when he "drives into a tree".
                bool clear = false;
                foreach (float off in new[] { 0f, 1.0f })
                {
                    var r = new Result();
                    yield return StartCoroutine(Drive(car, table, pk, off, r));
                    if (r.blocked) break;
                    clear = true;
                    string name = "tree " + pk.i + " (" + pk.road.ToString("0.0") + " m off the centreline, r " +
                                  table.RadiusOf(pk.i).ToString("0.00") + ") " + (off == 0f ? "dead on" : "offset " + off.ToString("0.0") + " m");
                    string got = "closest " + r.closest.ToString("0.00") + " m, arrived at " + (r.arriveSpeed * 3.6f).ToString("0") +
                                 " km/h, left at " + (r.endSpeed * 3.6f).ToString("0") + " km/h" + (r.capsuleThere ? "" : ", NO CAPSULE AT THE TREE");
                    if (!r.reached) { TreePlayCheck.Line("  --   " + name + ": never got there  [" + got + "]"); continue; }
                    TreePlayCheck.Check(!r.droveThrough && r.capsuleThere, name + " stops the car", got);
                }
                if (clear) run++;
            }
            TreePlayCheck.Check(run >= 3, "at least three reachable trees were driven at", run + " of " + tried + " tried");

            // THE LEAVES. A third of the species carry foliage down to the
            // bumper, metres wide round a 25 cm trunk; driving through that
            // with nothing happening is driving through a tree. Past the
            // trunk, inside the crown: the car must come out slower, and the
            // table must say it was in the brush.
            int dress = Seasons.CurrentDress, brushRun = 0; tried = 0;
            foreach (var pk in picks)
            {
                if (brushRun >= 3 || tried >= 80) break;
                float brush = table.BrushOf(pk.i, dress);
                if (brush < 2f) continue;
                tried++;
                var r = new Result();
                // Clear of the trunk by a body's half-width and a bit, inside the leaves.
                float off = table.RadiusOf(pk.i) + 0.86f + 0.6f;
                yield return StartCoroutine(Drive(car, table, pk, off, r, brush + 0.9f));
                if (r.blocked || !r.reached || r.brushIn <= 0f) continue;
                if (r.offRoadSteps < 5)
                {
                    TreePlayCheck.Line("  --   tree " + pk.i + " (leaves out to " + brush.ToString("0.0") +
                                       " m): the car kept its wheels on the road all the way through - brush never acts there");
                    continue;
                }
                brushRun++;
                int contacts = r.brushSteps;
                // Through the leaves and out the far side: slower by more than
                // the same stretch of open ground would cost, and the table
                // says the car was in the brush while it happened.
                TreePlayCheck.Check(contacts > 0 && r.brushOut < r.brushIn - 1.5f,
                    "tree " + pk.i + " (leaves out to " + brush.ToString("0.0") + " m): through the crown " +
                    off.ToString("0.0") + " m off the trunk, the car is dragged down",
                    "in at " + (r.brushIn * 3.6f).ToString("0") + " km/h, out at " + (r.brushOut * 3.6f).ToString("0") +
                    " km/h, " + contacts + " steps in the brush");
            }
            TreePlayCheck.Line("  " + brushRun + " leafy tree(s) driven through in dress " + dress +
                               (brushRun == 0 ? " (none reachable with leaves this low - not a failure in a bare season)" : ""));

            // THE CONTROL: the same pass beside a tree with NO low leaves.
            // Without it, "came out slower" could be the verge, the slope or
            // the throttle; with it, the slowing is the brush's and nothing
            // else's.
            int bareRun = 0; tried = 0;
            foreach (var pk in picks)
            {
                if (bareRun >= 2 || tried >= 80) break;
                if (table.BrushOf(pk.i, dress) > 0f) continue;
                // ...and no leafy neighbour's crown across the path either.
                bool leafyNear = false;
                for (int j = 0; j < table.Count && !leafyNear; j++)
                    if (j != pk.i && table.BrushOf(j, dress) > 0f &&
                        (table.BaseOf(j) - table.BaseOf(pk.i)).sqrMagnitude < 20f * 20f) leafyNear = true;
                if (leafyNear) continue;
                tried++;
                var r = new Result();
                float off = table.RadiusOf(pk.i) + 0.86f + 0.6f;
                yield return StartCoroutine(Drive(car, table, pk, off, r, 3.5f));
                if (r.blocked || !r.reached || r.brushIn <= 0f) continue;
                bareRun++;
                int contacts = r.brushSteps;
                TreePlayCheck.Check(contacts == 0 && r.brushOut > r.brushIn - 1.5f,
                    "tree " + pk.i + " (bare to the bumper): past it at the same " + off.ToString("0.0") +
                    " m, nothing slows the car (the control)",
                    "in at " + (r.brushIn * 3.6f).ToString("0") + " km/h, slowest " + (r.brushOut * 3.6f).ToString("0") +
                    " km/h, " + contacts + " steps in the brush");
            }
            Done();
        }

        class Result
        {
            public bool blocked, reached, droveThrough, capsuleThere;
            public float closest = float.MaxValue, arriveSpeed, endSpeed;
            /// <summary>Speed as the car's centre came inside the brush, and
            /// the slowest it got before it was out the far side.</summary>
            public float brushIn, brushOut;
            /// <summary>Steps the table counted this car in the brush WHILE it
            /// was inside the measured zone - not over the whole run, which
            /// goes on for fifty metres past the tree and through whatever
            /// else is growing there.</summary>
            public int brushSteps;
            /// <summary>Steps inside the measured zone with the car OFF the
            /// road. Brush never acts on a car whose wheels are on the road
            /// (a crown may hang over a shoulder; the shoulder is still road),
            /// so a pass that never left it proves nothing either way.</summary>
            public int offRoadSteps;
        }

        /// <param name="brushReach">0 for a run AT the trunk. For a run
        /// THROUGH the leaves, how far from the axis the brush is felt: the
        /// corridor is then checked clear at a car's full width all the way
        /// past the tree, because in a forest this thick the next trunk is
        /// seven metres on and a run that ends against it measures that.</param>
        IEnumerator Drive(CarController car, TreeTrunks table, Pick pk, float offset, Result r, float brushReach = 0f)
        {
            Vector3 trunk = table.BaseOf(pk.i);
            Vector3 dir = pk.toTree;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 start = trunk - dir * 16f + side * offset;

            // Stand the car on whatever ground is there.
            int groundMask = ~((1 << 2) | (1 << SolidLayer));
            if (!Physics.Raycast(start + Vector3.up * 40f, Vector3.down, out RaycastHit g, 120f, groundMask))
            { r.blocked = true; yield break; }
            car.TeleportTo(g.point + Vector3.up * 0.6f, Quaternion.LookRotation(dir, Vector3.up));
            car.throttleInput = 0f; car.brakeInput = 1f; car.steerInput = 0f;
            for (int k = 0; k < 30; k++) yield return new WaitForFixedUpdate();

            // Is the way to the tree open — no wall, bank or other trunk first?
            Vector3 eye = car.transform.position + Vector3.up * 0.7f;
            Vector3 aim = new Vector3(trunk.x + side.x * offset, eye.y, trunk.z + side.z * offset) - eye;
            float reach = aim.magnitude;
            var hits = brushReach > 0f
                ? Physics.SphereCastAll(eye, 1.0f, aim.normalized, reach + brushReach + 3f, 1 << SolidLayer)
                : Physics.SphereCastAll(eye, 0.5f, aim.normalized, reach - 1.5f, 1 << SolidLayer);
            if (hits.Length > 0) { r.blocked = true; yield break; }

            // The capsule the table owes this tree.
            foreach (var c in Physics.OverlapSphere(trunk + Vector3.up * 1.5f, 0.6f, 1 << SolidLayer))
                if (c is CapsuleCollider) r.capsuleThere = true;

            car.brakeInput = 0f;
            car.Body.linearVelocity = car.transform.forward * Speed;
            int steps = Mathf.RoundToInt(2.5f / Time.fixedDeltaTime);
            int stepsAtEntry = 0;
            for (int k = 0; k < steps; k++)
            {
                car.throttleInput = 0.6f; car.steerInput = 0f; car.brakeInput = 0f;
                yield return new WaitForFixedUpdate();
                // The trunk's AXIS against the car's BOX, in three dimensions:
                // a car that has been stood on its nose by the impact has its
                // own idea of "level", and a plan-view test in its frame then
                // calls a trunk that is past the bonnet "inside".
                float slack = table.RadiusOf(pk.i) * 0.5f, dist = float.MaxValue;
                bool inside = false;
                for (float up = 0f; up <= 4f; up += 0.2f)
                {
                    Vector3 local = car.transform.InverseTransformPoint(trunk + Vector3.up * up) - new Vector3(0f, 0.72f, 0.05f);
                    Vector3 over = new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - 0.86f),
                                               Mathf.Max(0f, Mathf.Abs(local.y) - 0.5f),
                                               Mathf.Max(0f, Mathf.Abs(local.z) - 2.05f));
                    dist = Mathf.Min(dist, over.magnitude);
                    if (Mathf.Abs(local.x) < 0.86f - slack && Mathf.Abs(local.y) < 0.5f - slack &&
                        Mathf.Abs(local.z) < 2.05f - slack) inside = true;
                }
                if (brushReach > 0f)
                {
                    Vector3 flat = car.transform.position - trunk; flat.y = 0f;
                    float speed = car.Body.linearVelocity.magnitude;
                    if (flat.magnitude < brushReach)
                    {
                        int counted = table.BrushStepsOf(car);
                        if (r.brushIn <= 0f) { r.brushIn = speed; r.brushOut = speed; stepsAtEntry = counted; }
                        r.brushOut = Mathf.Min(r.brushOut, speed);
                        r.brushSteps = counted - stepsAtEntry;
                        if (!car.onRoad) r.offRoadSteps++;
                    }
                }
                if (dist < r.closest) r.closest = dist;
                if (!r.reached && dist < 2.5f) { r.reached = true; r.arriveSpeed = car.Body.linearVelocity.magnitude; }
                if (inside) r.droveThrough = true;
            }
            r.endSpeed = car.Body.linearVelocity.magnitude;
        }

        void Done()
        {
            TreePlayCheck.Finish();
            EditorApplication.Exit(TreePlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
