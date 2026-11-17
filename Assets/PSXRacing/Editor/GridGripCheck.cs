using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARS AT REST ON THE REAL GRIDS (tools\grid-grip-check.ps1; owner
    /// 2026-11-17: "I noticed that cars slide on banked roads. Even when
    /// resting in place at the start of a race. This means tires don't
    /// actually grip the roads.").
    ///
    /// Each venue in PSX_GRIP_VENUES is loaded as a booked race (the player and
    /// three rivals, no traffic) and its countdown is HELD, so the field stands
    /// on its own grid as long as the check wants. Then, for every car:
    ///   hold      - the game's own pre-race hold (PlayerCarInput with input
    ///               off: brake 0.3 and the lever; AIDriver not driving: brake
    ///               0.4), measured from the game's own spawn;
    ///   no pedals - inputs taken over, nothing pressed (this model has no
    ///               neutral gear; engine braking is off below 0.5 m/s);
    ///   brake     - the pedal held, reverse off;
    ///   lever     - the parking brake alone;
    ///   launch    - from the lever, a light throttle, straight ahead.
    /// Each rest case restands the car on the spawn pose the game gave it,
    /// lets it settle 1 s, then watches PSX_GRIP_SECONDS (20) and reports how
    /// far it went ACROSS itself and ALONG itself in the road's plane, with
    /// the tyre numbers at the half-way mark.
    ///
    /// Then the player alone at the venue's most banked spot (raycast over the
    /// whole route), the same cases. PSX_GRIP_LAP=N: last of all the race goes
    /// live with the player on autopilot for N seconds, and the at-speed
    /// telemetry (distance, peak lateral g, slip angles) is reported.
    ///
    /// Reads no switch itself: run fresh processes with PSX_STATIC_GRIP=0 / 1.
    /// Writes PSXRacing_grip_check[_TAG].txt and .csv in the project folder.
    /// </summary>
    public static class GridGripCheck
    {
        internal static StringBuilder log, csv;
        static readonly Queue<string> queue = new Queue<string>();
        internal static string venue;
        internal static string Tag => System.Environment.GetEnvironmentVariable("PSX_GRIP_TAG") ?? "";
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        static string OutBase => Path.Combine(ProjectDir, "PSXRacing_grip_check" + (Tag.Length > 0 ? "_" + Tag : ""));

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            queue.Clear();
            string v = System.Environment.GetEnvironmentVariable("PSX_GRIP_VENUES");
            if (string.IsNullOrEmpty(v)) v = "LakeLure,ChimneyRock,SwissNC226A,BlueRidge,GillespieGap,UptownLoop";
            foreach (var s in v.Split(',')) if (s.Trim().Length > 0) queue.Enqueue(s.Trim());
            log = new StringBuilder();
            csv = new StringBuilder("venue,where,case,car,role,bank_pct,grade_pct,side_mm,fore_mm,settle_mm,peak_mps,held_pct,yaw_deg,corner_mm\n");
            string sw = System.Environment.GetEnvironmentVariable("PSX_STATIC_GRIP");
            log.AppendLine("GRID GRIP CHECK  PSX_STATIC_GRIP=" + (string.IsNullOrEmpty(sw) ? "(unset)" : sw) +
                           "  tag " + (Tag.Length > 0 ? Tag : "-"));
            StartNext();
        }

        static void StartNext()
        {
            if (queue.Count == 0)
            {
                File.WriteAllText(OutBase + ".txt", log.ToString());
                File.WriteAllText(OutBase + ".csv", csv.ToString());
                EditorApplication.Exit(0);
                return;
            }
            venue = queue.Dequeue();
            int index = TrackCatalog.IndexOf(venue);
            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            log.AppendLine();
            log.AppendLine("=== " + venue + " ===");
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                log.AppendLine("  NOT BUILT here - skipped");
                StartNext();
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Morning;
            var fld = typeof(RaceHandoff).GetField("TrafficLevel",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (fld != null) fld.SetValue(null, 0);
            var cars = CarCatalog.All;
            int b = Mathf.Clamp(20, 0, cars.Count - 5);
            RaceHandoff.CarSpecId = cars[b].id;
            RaceHandoff.OpponentSpecIds = cars[b + 1].id + ";" + cars[b + 2].id + ";" + cars[b + 3].id;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            // PSX_GRIP_LAPONLY=1: the at-speed telemetry alone, SOLO from a
            // ROLLING start (60 km/h): no grid, no launch, no rivals to tangle
            // with - the cleanest before/after of the handling at speed, since
            // nothing in it ever goes below the static grip's 2 m/s.
            if (System.Environment.GetEnvironmentVariable("PSX_GRIP_LAPONLY") == "1")
            {
                RaceHandoff.Solo = true;
                RaceHandoff.RollingStartKmh = 60f;
            }
            string pick = System.Environment.GetEnvironmentVariable("PSX_GRIP_CARS");
            if (!string.IsNullOrEmpty(pick))
            {
                var ids = pick.Split(';');
                RaceHandoff.CarSpecId = ids[0];
                RaceHandoff.OpponentSpecIds = string.Join(";", ids, 1, ids.Length - 1);
            }
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("GridGripRunner").AddComponent<GridGripRunner>();
        }

        internal static void Line(string s) { log.AppendLine(s); }

        internal static void Done()
        {
            // Written after every venue, so a run that dies later keeps what it has.
            File.WriteAllText(OutBase + ".txt", log.ToString());
            File.WriteAllText(OutBase + ".csv", csv.ToString());
            EditorApplication.playModeStateChanged += OnExited;
            EditorApplication.ExitPlaymode();
        }

        static void OnExited(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnExited;
            EditorApplication.delayCall += StartNext;
        }
    }

    /// <summary>Runs before CarController in each physics step, so the inputs
    /// it writes are the ones the step uses.</summary>
    [DefaultExecutionOrder(-500)]
    public class GridGripRunner : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        RaceManager rm;
        bool holding = true;
        System.Func<bool> prevHold, myHold;
        int tick;

        // the scripted inputs, when scripted
        bool scripted;
        float sThrottle, sBrake, sSteer;
        bool sLever;
        readonly List<CarController> scriptedCars = new List<CarController>();

        // the case being sampled
        class Rec
        {
            public CarController car;
            public string role;
            public Vector3 p0, p1;
            /// <summary>Where it was the first tick all four tyres touched:
            /// the settle is measured from there, so a car dropped straight
            /// down onto a slope is not charged with the slope's share of the
            /// fall.</summary>
            public Vector3 touch;
            public bool touched;
            public Quaternion r1;
            public float peak;
            public int parked, ticks;
            public string wheels;
        }
        readonly List<Rec> recs = new List<Rec>();
        bool sampling;
        int midTick;

        readonly Dictionary<CarController, Vector3> spawnPos = new Dictionary<CarController, Vector3>();
        readonly Dictionary<CarController, Vector3> gridPos = new Dictionary<CarController, Vector3>();
        readonly Dictionary<CarController, Quaternion> gridRot = new Dictionary<CarController, Quaternion>();
        readonly Dictionary<CarController, Quaternion> spawnRot = new Dictionary<CarController, Quaternion>();

        float watchSeconds = 20f;

        void Update()
        {
            // Hold the countdown, chained over whatever else holds it (a city
            // race's course preload), and re-asserted if something replaces it.
            if (holding && RaceManager.HoldCountdown != myHold)
            {
                prevHold = RaceManager.HoldCountdown;
                RaceManager.HoldCountdown = myHold;
            }
        }

        System.Action onTick;

        void FixedUpdate()
        {
            tick++;
            onTick?.Invoke();
            if (scripted)
                foreach (var c in scriptedCars)
                {
                    if (c == null) continue;
                    c.throttleInput = sThrottle;
                    c.brakeInput = sBrake;
                    c.handbrakeInput = sLever;
                    c.steerInput = sSteer;
                    c.allowReverse = false;
                    if (c.currentGear < 1) c.currentGear = 1;
                }
            foreach (var r in recs)
            {
                if (r.touched || r.car == null || r.car.Body == null) continue;
                bool all = true;
                for (int i = 0; i < 4; i++) all &= r.car.wheelContacts[i].grounded;
                if (all) { r.touched = true; r.touch = r.car.Body.position; }
            }
            if (sampling)
                foreach (var r in recs)
                {
                    if (r.car == null || r.car.Body == null) continue;
                    Vector3 up = r.r1 * Vector3.up;
                    float sp = Vector3.ProjectOnPlane(r.car.Body.linearVelocity, up).magnitude;
                    if (sp > r.peak) r.peak = sp;
                    if (r.car.Parked) r.parked++;
                    r.ticks++;
                    if (tick == midTick) r.wheels = Wheels(r.car);
                }
        }

        IEnumerator WaitTicks(int n)
        {
            int until = tick + n;
            while (tick < until) yield return null;
        }

        int Ticks(float seconds) => Mathf.RoundToInt(seconds / Time.fixedDeltaTime);

        IEnumerator CamerasOff()
        {
            while (true)
            {
                foreach (var c in Camera.allCameras) if (c != null) c.enabled = false;
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        IEnumerator Start()
        {
            myHold = () => holding || (prevHold != null && prevHold());
            prevHold = RaceManager.HoldCountdown;
            RaceManager.HoldCountdown = myHold;
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            rm = RaceManager.Instance;
            if (rm == null || rm.playerCar == null) { GridGripCheck.Line("  no race in the scene"); Finish(); yield break; }
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_GRIP_SECONDS") ?? "20", NumberStyles.Float, Inv, out watchSeconds);
            if (watchSeconds <= 0f) watchSeconds = 20f;
            bool headless = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
            Time.timeScale = 4f;
            Time.maximumDeltaTime = 0.4f;
            if (headless)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 240;
                StartCoroutine(CamerasOff());
            }
            var cars = new List<CarController>();
            foreach (var c in rm.allCars) if (c != null) cars.Add(c);
            foreach (var c in cars) { spawnPos[c] = gridPos[c] = c.Body.position; spawnRot[c] = gridRot[c] = c.Body.rotation; }
            GridGripCheck.Line($"  state {rm.State}, countdown {rm.CountdownRemaining:0.00} s, {cars.Count} cars, path {rm.path.Count} pts, banked {rm.path.HasBank}" +
                               (rm.path.HasBank ? $", path max bank {MaxBank(rm.path) * 100f:0.0}%" : ""));
            foreach (var c in cars)
                GridGripCheck.Line($"    {Role(c),-6} {c.name}  {c.massKg:0} kg");
            var stuck = rm.playerCar.GetComponent<StuckRecovery>();
            if (stuck != null) stuck.enabled = false;
            if (System.Environment.GetEnvironmentVariable("PSX_GRIP_LAPONLY") == "1")
            {
                float lapOnly = 120f;
                float.TryParse(System.Environment.GetEnvironmentVariable("PSX_GRIP_LAP") ?? "120", NumberStyles.Float, Inv, out lapOnly);
                if (lapOnly <= 0f) lapOnly = 120f;
                yield return Lap(cars, lapOnly, rolling: true);
                Finish();
                yield break;
            }

            // PSX_GRIP_SHOTS=1 (a run WITH graphics): a frame strip of the
            // game's own hold, from overhead, at four moments of the watch.
            shots = System.Environment.GetEnvironmentVariable("PSX_GRIP_SHOTS") == "1" && !headless;
            // PSX_GRIP_ONLYHOLD=1: the game's own hold only (the strip run).
            bool onlyHold = System.Environment.GetEnvironmentVariable("PSX_GRIP_ONLYHOLD") == "1";

            // 1. THE GAME'S OWN HOLD, from the game's own spawn.
            yield return Case("grid", "hold", cars, restand: false, settleSeconds: 1f);
            if (!onlyHold)
            {
                // 2-4. Inputs taken over.
                yield return Scripted("grid", "no pedals", cars, 0f, 0f, false);
                yield return Scripted("grid", "brake", cars, 0f, 1f, false);
                yield return Scripted("grid", "lever", cars, 0f, 0f, true);
                // The player alone: a field launched together runs into itself.
                yield return Launch("grid", new List<CarController> { rm.playerCar }, cars, 0.35f);
            }

            // 5. THE MOST BANKED SPOT, the player alone.
            if (System.Environment.GetEnvironmentVariable("PSX_GRIP_SPOT") != "0" &&
                FindSpot(rm.path, out Vector3 at, out Quaternion rot, out string where))
            {
                GridGripCheck.Line("  the most banked spot: " + where);
                var me = new List<CarController> { rm.playerCar };
                GameMode(cars);
                rm.playerCar.ResetTo(at, rot, seated: true);
                yield return WaitTicks(2);
                spawnPos[rm.playerCar] = rm.playerCar.Body.position;
                spawnRot[rm.playerCar] = rm.playerCar.Body.rotation;
                yield return Case("spot", "hold", me, restand: true, settleSeconds: 1f);
                if (!onlyHold)
                {
                    yield return Scripted("spot", "no pedals", me, 0f, 0f, false);
                    yield return Scripted("spot", "brake", me, 0f, 1f, false);
                    yield return Scripted("spot", "lever", me, 0f, 0f, true);
                    yield return Launch("spot", me, me, 0.35f);
                    yield return Launch("spot", me, me, 1.0f);
                }
            }

            // 6. AT SPEED (PSX_GRIP_LAP seconds after the green).
            float lap = 0f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_GRIP_LAP") ?? "0", NumberStyles.Float, Inv, out lap);
            if (lap > 0f) yield return Lap(cars, lap);
            Finish();
        }

        static float MaxBank(TrackPath p)
        {
            float m = 0f;
            for (int i = 0; i < p.Count; i++) m = Mathf.Max(m, Mathf.Abs(p.bankL[i]), Mathf.Abs(p.bankR[i]));
            return m;
        }

        string Role(CarController c) => c == rm.playerCar ? "player" : "AI";

        void GameMode(List<CarController> cars)
        {
            scripted = false;
            scriptedCars.Clear();
            foreach (var c in cars)
            {
                var pi = c.GetComponent<PlayerCarInput>();
                if (pi != null) { pi.enabled = true; pi.inputEnabled = false; }
                var ai = c.GetComponent<AIDriver>();
                if (ai != null) { ai.enabled = true; ai.driving = false; }
            }
        }

        void ScriptMode(List<CarController> cars, float throttle, float brake, bool lever, float steer = 0f)
        {
            scripted = true;
            scriptedCars.Clear();
            scriptedCars.AddRange(cars);
            sThrottle = throttle; sBrake = brake; sLever = lever; sSteer = steer;
            foreach (var c in cars)
            {
                var pi = c.GetComponent<PlayerCarInput>();
                if (pi != null) pi.enabled = false;
                var ai = c.GetComponent<AIDriver>();
                if (ai != null) ai.enabled = false;
            }
        }

        IEnumerator Scripted(string where, string name, List<CarController> cars, float throttle, float brake, bool lever)
        {
            ScriptMode(cars, throttle, brake, lever);
            yield return Case(where, name, cars, restand: true, settleSeconds: 1f);
            GameMode(cars);
        }

        IEnumerator Case(string where, string name, List<CarController> cars, bool restand, float settleSeconds)
        {
            if (restand)
                foreach (var c in cars) c.TeleportTo(spawnPos[c], spawnRot[c]);
            recs.Clear();
            foreach (var c in cars) recs.Add(new Rec { car = c, role = Role(c), p0 = c.Body.position });
            yield return WaitTicks(Ticks(settleSeconds));
            foreach (var r in recs) { r.p1 = r.car.Body.position; r.r1 = r.car.Body.rotation; }
            midTick = tick + Ticks(watchSeconds * 0.5f);
            sampling = true;
            bool shoot = shots && name == "hold";
            if (shoot) yield return Shot(where, cars, 0f);
            for (int q = 1; q <= 4; q++)
            {
                yield return WaitTicks(Ticks(watchSeconds * 0.25f));
                if (shoot) yield return Shot(where, cars, watchSeconds * 0.25f * q);
            }
            sampling = false;
            foreach (var r in recs)
            {
                Vector3 p2 = r.car.Body.position;
                Vector3 right = r.r1 * Vector3.right, fwd = r.r1 * Vector3.forward;
                Vector3 d = p2 - r.p1;
                float side = Vector3.Dot(d, right), fore = Vector3.Dot(d, fwd);
                float settle = Vector3.ProjectOnPlane(r.p1 - (r.touched ? r.touch : r.p0), r.r1 * Vector3.up).magnitude;
                float bank = right.y / Mathf.Max(0.2f, Mathf.Sqrt(1f - right.y * right.y));
                float grade = fwd.y / Mathf.Max(0.2f, Mathf.Sqrt(1f - fwd.y * fwd.y));
                float held = r.ticks > 0 ? r.parked / (float)r.ticks : 0f;
                // THE HEADING, and the CORNERS: a car can keep its centre and
                // still turn on the spot, and what the eye sees slide is the
                // body's ends. The worst of four points 2 m ahead / behind and
                // 0.8 m either side of the centre, in the road's plane.
                Quaternion r2 = r.car.Body.rotation;
                Vector3 up = r.r1 * Vector3.up;
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(fwd, up), Vector3.ProjectOnPlane(r2 * Vector3.forward, up), up);
                float corner = 0f;
                foreach (var o in new[] { new Vector3(0.8f, 0f, 2f), new Vector3(-0.8f, 0f, 2f), new Vector3(0.8f, 0f, -2f), new Vector3(-0.8f, 0f, -2f) })
                    corner = Mathf.Max(corner, Vector3.ProjectOnPlane((p2 + r2 * o) - (r.p1 + r.r1 * o), up).magnitude);
                GridGripCheck.Line(string.Format(Inv,
                    "  {0,-5} {1,-10} {2,-6} bank {3,6:+0.0;-0.0}% grade {4,6:+0.0;-0.0}%  SIDE {5,8:+0.0;-0.0} mm  FORE {6,8:+0.0;-0.0} mm  YAW {10,6:+0.00;-0.00} deg  CORNER {11,7:0.0} mm  settle {7,6:0.0} mm  peak {8:0.000} m/s  held {9,3:0}%",
                    where, name, r.role, bank * 100f, grade * 100f, side * 1000f, fore * 1000f, settle * 1000f, r.peak, held * 100f, yaw, corner * 1000f));
                if (!string.IsNullOrEmpty(r.wheels) && (r.role == "player" || where == "grid" && r == recs[recs.Count - 1]))
                    GridGripCheck.log.Append(r.wheels);
                GridGripCheck.csv.AppendLine(string.Format(Inv, "{0},{1},{2},{3},{4},{5:0.0},{6:0.0},{7:0.0},{8:0.0},{9:0.0},{10:0.000},{11:0},{12:0.00},{13:0.0}",
                    GridGripCheck.venue, where, name, r.car.name.Replace(",", " "), r.role, bank * 100f, grade * 100f,
                    side * 1000f, fore * 1000f, settle * 1000f, r.peak, held * 100f, yaw, corner * 1000f));
            }
        }

        bool shots;
        Camera stripCam;

        /// <summary>One overhead frame of these cars, rendered on request (a
        /// hidden run draws no frames of its own): straight down from above
        /// their middle, the image's up along the first car's heading, high
        /// enough for the whole group. Screenshots/GripStrip/.</summary>
        IEnumerator Shot(string where, List<CarController> cars, float t)
        {
            yield return null;
            if (stripCam == null)
            {
                var go = new GameObject("GripStripCam");
                DontDestroyOnLoad(go);
                stripCam = go.AddComponent<Camera>();
                stripCam.enabled = false;
                stripCam.fieldOfView = 40f;
                stripCam.nearClipPlane = 0.3f;
                stripCam.farClipPlane = 400f;
            }
            Vector3 mid = Vector3.zero; float span = 0f;
            foreach (var c in cars) mid += spawnPos[c];
            mid /= Mathf.Max(1, cars.Count);
            foreach (var c in cars) span = Mathf.Max(span, Vector3.Distance(spawnPos[c], mid));
            Vector3 head = spawnRot[cars[0]] * Vector3.forward; head.y = 0f; head.Normalize();
            float h = Mathf.Max(9f, (span + 4f) / Mathf.Tan(20f * Mathf.Deg2Rad));
            stripCam.transform.SetPositionAndRotation(mid + Vector3.up * h, Quaternion.LookRotation(Vector3.down, head));
            var rt = new RenderTexture(960, 720, 24);
            rt.Create();
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(stripCam, request))
                for (int i = 0; i < 2; i++) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(stripCam, request);
            else { stripCam.targetTexture = rt; stripCam.Render(); stripCam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "GripStrip");
            Directory.CreateDirectory(dir);
            string tag = GridGripCheck.Tag.Length > 0 ? GridGripCheck.Tag : "x";
            File.WriteAllBytes(Path.Combine(dir, string.Format(Inv, "{0}_{1}_{2}_t{3:00}.png", GridGripCheck.venue, where, tag, t)), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }

        static string Wheels(CarController car)
        {
            var sb = new StringBuilder();
            float need = -Vector3.Dot(Physics.gravity, car.transform.right) * car.massKg;
            float got = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = car.wheelContacts[i];
                if (!w.grounded) { sb.AppendLine("        wheel " + i + " in the air"); continue; }
                float cv = car.Body.GetPointVelocity(w.point).magnitude;
                got += w.latForce;
                sb.AppendLine(string.Format(Inv,
                    "        wheel {0}: Fz {1,6:0} N  circle {2,6:0} N  lat {3,8:0.0} N  slip {4,8:0.000} deg  contact {5:0.0000} m/s  {6}",
                    i, w.load, w.gripCircle, w.latForce, w.slipAngle * Mathf.Rad2Deg, cv, w.onRoad ? "road" : "OFF ROAD"));
            }
            sb.AppendLine(string.Format(Inv, "        across the car: gravity asks {0:0} N, the tyres give {1:0} N; parked {2}",
                need, -got, car.Parked));
            return sb.ToString();
        }

        /// <summary>From the lever on the spawn pose, a straight launch: the
        /// hand-over from standing grip to rolling grip. Sideways speed, yaw
        /// and the tick-to-tick change in sideways acceleration.</summary>
        IEnumerator Launch(string where, List<CarController> go, List<CarController> all, float throttle)
        {
            // Everyone on the lever on their own pose; then the launchers go.
            ScriptMode(all, 0f, 0f, true);
            foreach (var c in all) c.TeleportTo(spawnPos[c], spawnRot[c]);
            yield return WaitTicks(Ticks(3f));
            var start = new Dictionary<CarController, Vector3>();
            var prevV = new Dictionary<CarController, float>();
            var prevA = new Dictionary<CarController, float>();
            var maxV = new Dictionary<CarController, float>();
            var maxYaw = new Dictionary<CarController, float>();
            var maxJerk = new Dictionary<CarController, float>();
            var ks = new Dictionary<CarController, int>();
            foreach (var c in go)
            {
                start[c] = c.Body.position; prevV[c] = Vector3.Dot(c.Body.linearVelocity, c.transform.right);
                prevA[c] = 0f; maxV[c] = 0f; maxYaw[c] = 0f; maxJerk[c] = 0f; ks[c] = 0;
            }
            float dt = Time.fixedDeltaTime;
            // The launchers on the throttle; the rest keep the lever (their
            // inputs are left as the lever case wrote them, nothing else
            // writes them while their input components are off).
            scriptedCars.Clear(); scriptedCars.AddRange(go);
            sThrottle = throttle; sBrake = 0f; sLever = false; sSteer = 0f;
            onTick = () =>
            {
                foreach (var c in go)
                {
                    float v = Vector3.Dot(c.Body.linearVelocity, c.transform.right);
                    float a = (v - prevV[c]) / dt;
                    if (ks[c] > 0) maxJerk[c] = Mathf.Max(maxJerk[c], Mathf.Abs(a - prevA[c]) / dt);
                    maxV[c] = Mathf.Max(maxV[c], Mathf.Abs(v));
                    maxYaw[c] = Mathf.Max(maxYaw[c], Mathf.Abs(Vector3.Dot(c.Body.angularVelocity, c.transform.up)));
                    prevV[c] = v; prevA[c] = a; ks[c]++;
                }
            };
            yield return WaitTicks(Ticks(1.5f));
            onTick = null;
            foreach (var c in go)
            {
                Vector3 d = c.Body.position - start[c];
                GridGripCheck.Line(string.Format(Inv,
                    "  {0,-5} launch {1:0.00} {2,-6} went {3:0.00} m in 1.5 s, across {4:+0.000;-0.000} m, max |vLat| {5:0.000} m/s, max |yaw| {6:0.000} rad/s, max lateral jerk {7:0} m/s^3",
                    where, throttle, Role(c), Vector3.Dot(d, spawnRot[c] * Vector3.forward),
                    Vector3.Dot(d, spawnRot[c] * Vector3.right), maxV[c], maxYaw[c], maxJerk[c]));
            }
            GameMode(all);
        }

        /// <summary>The most banked place on the route a car could be left:
        /// both edges of a 1.8 m patch either side of each lane centre on the
        /// road layer, the steepest cross slope under 15% on a grade under
        /// 12%. Raycast, so it is what is BUILT, banked file or not.</summary>
        static bool FindSpot(TrackPath path, out Vector3 at, out Quaternion rot, out string where)
        {
            at = Vector3.zero; rot = Quaternion.identity; where = "";
            const int Road = 1 << 8;
            float best = 0f; int bestI = -1; float bestL = 0f; Vector3 bestAt = Vector3.zero;
            float bestGrade = 0f;
            int n = path.Count;
            for (int i = 6; i < n - 6; i += 2)
            {
                Vector3 p = path.GetPoint(i);
                Vector3 t = path.GetTangent(i); t.y = 0f; if (t.sqrMagnitude < 1e-4f) continue; t.Normalize();
                Vector3 r = Vector3.Cross(Vector3.up, t);
                foreach (float lat in new[] { -1.8f, 1.8f })
                {
                    if (!Hit(p + r * (lat - 0.9f), Road, out float hA) || !Hit(p + r * (lat + 0.9f), Road, out float hB)) continue;
                    if (!Hit(p + r * lat - t * 1.5f, Road, out float hF) || !Hit(p + r * lat + t * 1.5f, Road, out float hN)) continue;
                    if (!Hit(p + r * lat, Road, out float h0)) continue;
                    float slope = (hB - hA) / 1.8f, grade = (hN - hF) / 3f;
                    if (Mathf.Abs(slope) > 0.15f || Mathf.Abs(grade) > 0.12f) continue;
                    if (Mathf.Abs(slope) > Mathf.Abs(best)) { best = slope; bestI = i; bestL = lat; bestAt = new Vector3(p.x + r.x * lat, h0, p.z + r.z * lat); bestGrade = grade; }
                }
            }
            if (bestI < 0) return false;
            Vector3 tt = path.GetTangent(bestI); tt.y = 0f; tt.Normalize();
            at = bestAt;
            rot = CarController.RollToRoad(bestAt, Quaternion.LookRotation(tt, Vector3.up));
            where = string.Format(Inv, "waypoint {0} of {1}, {2:+0.0;-0.0} m from the centre, cross slope {3:+0.0;-0.0}%, grade {4:+0.0;-0.0}%",
                                  bestI, n, bestL, best * 100f, bestGrade * 100f);
            return true;
        }

        static bool Hit(Vector3 p, int mask, out float y)
        {
            y = 0f;
            if (!Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out var h, 10f, mask, QueryTriggerInteraction.Ignore)) return false;
            y = h.point.y;
            return true;
        }

        /// <summary>The race goes live with the player on autopilot; the
        /// player's telemetry for <paramref name="seconds"/> after the green.
        /// </summary>
        IEnumerator Lap(List<CarController> cars, float seconds, bool rolling = false)
        {
            GameMode(cars);
            if (!rolling) foreach (var c in cars) c.TeleportTo(gridPos[c], gridRot[c]);
            Time.timeScale = 2f;
            var me = rm.playerCar;
            var input = me.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var auto = me.gameObject.AddComponent<AIDriver>();
            auto.path = rm.path;
            auto.skill = 0.9f;
            auto.driving = false;
            me.allowReverse = false;
            Random.InitState(9173);
            holding = false;
            float until = Time.realtimeSinceStartup + 30f;
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup < until) yield return null;
            GridGripCheck.Line("  AT SPEED: the race goes live " + (rm.State == RaceManager.RaceState.Racing ? "(ok)" : "(DID NOT)"));
            auto.driving = true;
            float dt = Time.fixedDeltaTime;
            Vector3 prevV = me.Body.linearVelocity;
            float latG = 0f, peakLatG = 0f, peakSpeed = 0f, sumSpeed = 0f;
            var front = new List<float>(); var rear = new List<float>(); var body = new List<float>();
            var lats = new List<float>();
            int drifting = 0, ticks = 0, hits = 0;
            System.Action<CollisionResponder, float, bool, string> onHit = (who, sp, hard, what) =>
            { if (who != null && who.GetComponent<CarController>() == me && sp > 3f) hits++; };
            CollisionResponder.HitReported += onHit;
            float[] marks = { 250f, 500f, 1000f, 2000f, 3000f };
            var markT = new float[marks.Length];
            onTick = () =>
            {
                Vector3 v = me.Body.linearVelocity;
                float a = Vector3.Dot((v - prevV) / dt, me.transform.right);
                prevV = v;
                latG = Mathf.Lerp(latG, a / 9.81f, dt / 0.10f);
                peakLatG = Mathf.Max(peakLatG, Mathf.Abs(latG));
                lats.Add(Mathf.Abs(latG));
                float sp = v.magnitude;
                peakSpeed = Mathf.Max(peakSpeed, sp); sumSpeed += sp;
                if (sp > 5f)
                {
                    front.Add(Mathf.Abs(me.frontSlipAngle) * Mathf.Rad2Deg);
                    rear.Add(Mathf.Abs(me.rearSlipAngle) * Mathf.Rad2Deg);
                    float bs = Mathf.Abs(me.chassisSlipAngle);
                    if (bs > Mathf.PI * 0.5f) bs = Mathf.PI - bs;
                    body.Add(bs * Mathf.Rad2Deg);
                }
                if (me.Drifting) drifting++;
                ticks++;
                var pr = rm.GetProgress(me);
                float dist = pr != null ? pr.progress : 0f;
                for (int m = 0; m < marks.Length; m++) if (markT[m] == 0f && dist >= marks[m]) markT[m] = ticks * dt;
            };
            yield return WaitTicks(Ticks(seconds));
            onTick = null;
            CollisionResponder.HitReported -= onHit;
            var prog = rm.GetProgress(me);
            var sbm = new StringBuilder();
            for (int m = 0; m < marks.Length; m++)
                sbm.Append(string.Format(Inv, " {0:0}m@{1}", marks[m], markT[m] > 0f ? markT[m].ToString("0.00", Inv) + "s" : "-"));
            GridGripCheck.Line(string.Format(Inv,
                "  AT SPEED {0:0} s: distance {1:0.0} m, avg {2:0.0} m/s, top {3:0.0} m/s, peak lateral {4:0.000} g, drifting {5:0.0}% of ticks",
                seconds, prog != null ? prog.progress : -1f, sumSpeed / Mathf.Max(1, ticks), peakSpeed, peakLatG, drifting * 100f / Mathf.Max(1, ticks)));
            GridGripCheck.Line("    splits" + sbm);
            GridGripCheck.Line(string.Format(Inv, "    lateral g (0.1 s smoothed) p50 {0:0.000} p95 {1:0.000} p99 {2:0.000}; player hits over 3 m/s: {3}",
                Pct(lats, 0.5f), Pct(lats, 0.95f), Pct(lats, 0.99f), hits));
            GridGripCheck.Line(string.Format(Inv,
                "    slip angle |deg| over 5 m/s: front p50 {0:0.00} p95 {1:0.00} max {2:0.00}; rear p50 {3:0.00} p95 {4:0.00} max {5:0.00}; body p95 {6:0.00} max {7:0.00}",
                Pct(front, 0.5f), Pct(front, 0.95f), Pct(front, 1f), Pct(rear, 0.5f), Pct(rear, 0.95f), Pct(rear, 1f), Pct(body, 0.95f), Pct(body, 1f)));
            foreach (var c in cars)
            {
                if (c == me) continue;
                var p = rm.GetProgress(c);
                GridGripCheck.Line(string.Format(Inv, "    AI {0}: distance {1:0.0} m", c.name, p != null ? p.progress : -1f));
            }
        }

        static float Pct(List<float> xs, float q)
        {
            if (xs.Count == 0) return 0f;
            xs.Sort();
            return xs[Mathf.Clamp(Mathf.RoundToInt(q * (xs.Count - 1)), 0, xs.Count - 1)];
        }

        void Finish()
        {
            holding = false;
            Time.timeScale = 1f;
            Time.maximumDeltaTime = 1f / 3f;
            Application.targetFrameRate = -1;
            StopAllCoroutines();
            GridGripCheck.Done();
        }
    }
}
