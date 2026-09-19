using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// DRIVE A CAR INTO A TREE AND SEE WHETHER THE TREE WINS.
    ///
    /// "Trees should also occupy space with their trunks, stopping a car if it
    /// drives into it." A collider in a scene file proves a collider exists;
    /// it says nothing about whether a car doing 100 km/h is stopped by thirty
    /// centimetres of it, and the stage's trunks do not exist in any scene
    /// file at all — <see cref="TreeTrunks"/> stands them up round the cars at
    /// runtime, a cell ahead. So this steps the solver, the way
    /// <see cref="ParkedCarSim"/> does: Physics.simulationMode to Script, the
    /// car's FixedUpdate (and the trunk table's) called by hand, Physics.Simulate
    /// behind them, on a flat slab in an empty scene.
    ///
    /// Every case asks the same question — did the trunk's axis ever get
    /// inside the car's body, i.e. did the car drive THROUGH the tree — and
    /// the control asks the opposite one: with no tree there, the same car at
    /// the same speed must sail past the spot. Without the control a car that
    /// never moved would pass every case.
    /// </summary>
    public static class TreeCrashSim
    {
        const float Dt = 0.02f;
        const float Speed = 100f / 3.6f;     // a proper accident, not a nudge

        public struct Reading
        {
            public string name;
            public float trunkZ;
            public float maxZ;          // furthest the car's centre got
            public float endSpeed;
            public bool droveThrough;   // the trunk axis was inside the car's body
            public int liveTrunks;      // capsules the table had standing at impact
        }

        [MenuItem("PSX Racing/Preview Tree Crash Sim")]
        public static void Shoot()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== A CAR INTO A TREE AT " + (Speed * 3.6f).ToString("0") + " KM/H ===");
            foreach (var x in Run())
                log.AppendLine(string.Format(
                    "  {6} {0,-34} trunk at z {1,5:0.0}  car got to z {2,6:0.0}  still doing {3,5:0.0} m/s  {4}  ({5} live trunks)",
                    x.name, x.trunkZ, x.maxZ, x.endSpeed,
                    x.droveThrough ? "DROVE THROUGH IT" : "did not pass through", x.liveTrunks,
                    Passed(x) ? "ok  " : "FAIL"));
            File.WriteAllText("PSXRacing_treecrash.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        public static Reading[] Run()
        {
            var prevMode = Physics.simulationMode;
            float prevDt = Time.fixedDeltaTime;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject ground = null;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Time.fixedDeltaTime = Dt;
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Slab";
                ground.transform.position = new Vector3(0f, -0.5f, 100f);
                ground.transform.localScale = new Vector3(80f, 1f, 400f);
                ground.layer = 0;       // what the suspension rays can see
                return new[]
                {
                    // A stage tree, 90 m out: two cells away from the car's
                    // start, so it is NOT standing when the run begins — the
                    // table has to put it up on the way.
                    Case("stage forest, head on", table: true, x: 0f, z: 90f, r: 0.30f),
                    // A corner: the trunk 0.7 m off the car's centreline, inside
                    // its 0.86 m half-width. A glancing hit must not slip through.
                    Case("stage forest, front corner", table: true, x: 0.7f, z: 90f, r: 0.30f),
                    // A circuit tree: its own object with a baked Trunk child.
                    Case("circuit tree, head on", table: false, x: 0f, z: 60f, r: 0.15f),
                    // The thinnest trunk the kit will make.
                    Case("sapling (r 0.12), head on", table: true, x: 0f, z: 60f, r: 0.12f),
                    // THE CONTROL: the same run, no tree.
                    Case("nothing there (control)", table: true, x: 0f, z: 90f, r: -1f),
                };
            }
            finally
            {
                if (ground != null) Object.DestroyImmediate(ground);
                foreach (var t in Object.FindObjectsByType<TreeTrunks>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(t.gameObject);
                foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (go != null && (go.name == "Trunks" || go.name == "CrashTree")) Object.DestroyImmediate(go);
                Physics.simulationMode = prevMode;
                Time.fixedDeltaTime = prevDt;
            }
        }

        static Reading Case(string name, bool table, float x, float z, float r)
        {
            var reading = new Reading { name = name, trunkZ = z };
            TreeTrunks trunks = null;
            GameObject tree = null;
            if (table)
            {
                var host = new GameObject("TrunkTable");
                trunks = host.AddComponent<TreeTrunks>();
                var list = new System.Collections.Generic.List<Vector4>();
                if (r > 0f) list.Add(new Vector4(x, -0.25f, z, r));
                trunks.SetTrunks(list);
            }
            else if (r > 0f)
            {
                tree = new GameObject("CrashTree");
                tree.transform.position = new Vector3(x, -0.2f, z);
                tree.transform.localScale = Vector3.one * 1.1f;
                TreeKit.AddTrunk(tree.transform, tree.transform.position, r, 3f);
            }

            var go = new GameObject("CrashCar");
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1.72f, 1.0f, 4.1f);
            box.center = new Vector3(0f, 0.6f, 0f);
            go.layer = 2;
            go.AddComponent<Rigidbody>();
            var car = go.AddComponent<CarController>();
            typeof(CarController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(car, null);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.8f, 0f), Quaternion.identity);
            Physics.SyncTransforms();

            var carStep = typeof(CarController).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            var tableStep = typeof(TreeTrunks).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

            // Settle on the springs, then go.
            for (int i = 0; i < 40; i++)
            {
                carStep?.Invoke(car, null);
                if (trunks != null) tableStep?.Invoke(trunks, null);
                Physics.Simulate(Dt);
            }
            car.Body.linearVelocity = Vector3.forward * Speed;
            car.throttleInput = 0.6f;

            float maxZ = go.transform.position.z;
            int steps = Mathf.RoundToInt(5f / Dt);
            for (int i = 0; i < steps; i++)
            {
                carStep?.Invoke(car, null);
                if (trunks != null) tableStep?.Invoke(trunks, null);
                Physics.Simulate(Dt);
                maxZ = Mathf.Max(maxZ, go.transform.position.z);
                if (r > 0f)
                {
                    // The trunk's axis, in the car's own frame. Inside the
                    // body by more than half the trunk's radius is a car that
                    // went through the tree rather than into it.
                    Vector3 local = go.transform.InverseTransformPoint(new Vector3(x, go.transform.position.y, z));
                    float slack = r * 0.5f;
                    if (Mathf.Abs(local.x) < 0.86f - slack && Mathf.Abs(local.z) < 2.05f - slack)
                        reading.droveThrough = true;
                }
                if (trunks != null) reading.liveTrunks = Mathf.Max(reading.liveTrunks, trunks.LiveColliders);
            }
            reading.maxZ = maxZ;
            reading.endSpeed = car.Body.linearVelocity.magnitude;

            Object.DestroyImmediate(go);
            if (tree != null) Object.DestroyImmediate(tree);
            if (trunks != null) Object.DestroyImmediate(trunks.gameObject);
            // The capsules the table stood up are NOT its children, and edit
            // mode never calls OnDestroy — so without this the next case drives
            // into the last case's tree. The first run did exactly that: the
            // control stopped dead at the sapling's trunk, 30 m short of
            // anywhere a tree had been asked for.
            ClearStoodTrunks();
            return reading;
        }

        static void ClearStoodTrunks()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && go.name == "Trunks") Object.DestroyImmediate(go);
        }

        /// <summary>Pass/fail for one reading — here, beside the cases, so the
        /// harness and the self-test cannot disagree about what a pass is.
        ///
        /// A head-on hit stops the car short of the trunk. A CORNER hit is
        /// allowed to spin the car round the tree and on past it — which is
        /// what an offset pole impact at 100 km/h does, and what the first run
        /// of this case measured (down to 6 m/s, trunk never inside the body):
        /// it must not pass THROUGH, and it must be a real hit, not a scrape.</summary>
        public static bool Passed(Reading x)
        {
            if (x.name.Contains("control")) return x.maxZ > x.trunkZ + 10f;   // sailed past the empty spot
            if (x.name.Contains("corner")) return !x.droveThrough && x.endSpeed < Speed * 0.5f;
            return !x.droveThrough && x.maxZ < x.trunkZ && x.endSpeed < 3f;
        }
    }
}
