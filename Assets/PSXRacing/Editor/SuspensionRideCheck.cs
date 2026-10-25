using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// One car's suspension, tallied while it drives (owner, 2026-10-25: "Stock
    /// suspension almost NEVER bottoms out, but every car currently bottoms out
    /// on every bump"). Counts the bottoming HITS the car reports (closing speed
    /// at the hard stop over CarController.BottomHitMps) and the CLUNKS (2 m/s
    /// and up: the impact voice, CollisionResponder.OnBottomed), by road speed;
    /// the share of ticks with a wheel in its jounce rubber or on its stop; the
    /// travel used over the settled ride; the least room left to the stop; the
    /// longest all-four-off flight. Play mode samples itself in FixedUpdate;
    /// an edit-mode harness calls <see cref="Sample"/> after each tick.
    /// </summary>
    public class SuspTally : MonoBehaviour
    {
        public CarController car;
        public bool manual;
        /// <summary>Non-null: one CSV line per sample (distance, speed, road
        /// height under each axle, compression over the settled ride).</summary>
        public StringBuilder trace;
        float lastHit;
        public int ticks, rubberTicks, stopTicks, hits, clunks;
        public float worst, vmax, dist, air, airMax;
        public readonly int[] hitsBySpeed = new int[4], ticksBySpeed = new int[4], strikes = new int[4];
        public readonly float[] travelMax = new float[2], roomMin = { 9f, 9f };
        public readonly int[] travelHist = new int[24];
        static readonly string[] SpeedBand = { "<50", "50-100", "100-130", "130+" };

        void Awake() => Hook();

        public void Hook()
        {
            if (car != null) return;
            car = GetComponent<CarController>();
            if (car != null) car.Bottomed += OnBottomed;
        }

        void OnDestroy() { if (car != null) car.Bottomed -= OnBottomed; }

        static int Band(float mps)
        {
            float mph = mps * 2.23694f;
            return mph < 50f ? 0 : mph < 100f ? 1 : mph < 130f ? 2 : 3;
        }

        void OnBottomed(float mps, int wheel)
        {
            hits++;
            if (mps >= 2f) clunks++;
            worst = Mathf.Max(worst, mps);
            strikes[mps < 1.5f ? 0 : mps < 2f ? 1 : mps < 3f ? 2 : 3]++;
            hitsBySpeed[Band(car.Body.linearVelocity.magnitude)]++;
            lastHit = Mathf.Max(lastHit, mps);
        }

        void FixedUpdate() { if (!manual) Sample(Time.fixedDeltaTime); }

        public float Settled(int i) =>
            CarController.StaticSagRef + (i < 2 ? 0.5f : -0.5f) *
            (car.rideRakeM + car.WheelRadiusOf(0) - car.WheelRadiusOf(2));

        public void Sample(float dt)
        {
            if (car == null || car.Body == null) return;
            ticks++;
            float v = car.Body.linearVelocity.magnitude;
            vmax = Mathf.Max(vmax, v);
            dist += v * dt;
            ticksBySpeed[Band(v)]++;
            if (!car.anyWheelGrounded) { air += dt; airMax = Mathf.Max(airMax, air); } else air = 0f;
            bool rub = false, stop = false;
            float most = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (car.InBumpRubber(i)) rub = true;
                if (car.OnBumpStop(i)) stop = true;
                if (!car.wheelContacts[i].grounded) continue;
                float comp = car.PhysicsHubY(i) - (car.mountHeight - car.restLength);
                float used = comp - Settled(i);
                travelMax[i < 2 ? 0 : 1] = Mathf.Max(travelMax[i < 2 ? 0 : 1], used);
                most = Mathf.Max(most, used);
                float lim = car.BumpLimit(i);
                if (!float.IsInfinity(lim)) roomMin[i < 2 ? 0 : 1] = Mathf.Min(roomMin[i < 2 ? 0 : 1], lim - comp);
            }
            travelHist[Mathf.Clamp(Mathf.FloorToInt(most * 100f), 0, travelHist.Length - 1)]++;
            if (trace != null)
            {
                // the road under each axle (mean of its grounded contacts, world y) and its compression
                float gF = float.NaN, gR = float.NaN, cF = 0f, cR = 0f;
                for (int ax = 0; ax < 2; ax++)
                {
                    float sum = 0f, comp = 0f; int n = 0;
                    for (int i = ax * 2; i < ax * 2 + 2; i++)
                    {
                        if (!car.wheelContacts[i].grounded) continue;
                        sum += car.wheelContacts[i].point.y; n++;
                        comp += car.PhysicsHubY(i) - (car.mountHeight - car.restLength) - Settled(i);
                    }
                    if (n == 0) continue;
                    if (ax == 0) { gF = sum / n; cF = comp / n; } else { gR = sum / n; cR = comp / n; }
                }
                trace.Append(System.FormattableString.Invariant($"{dist:0.000},{v:0.00},{gF:0.0000},{gR:0.0000},{cF:0.0000},{cR:0.0000},{lastHit:0.00}\n"));
                lastHit = 0f;
            }
            if (rub) rubberTicks++;
            if (stop) stopTicks++;
        }

        /// <summary>The travel (cm over the settled ride) that this share of
        /// ticks stayed under, worst wheel each tick.</summary>
        public int TravelPercentile(float p)
        {
            int n = 0;
            for (int i = 0; i < travelHist.Length; i++) n += travelHist[i];
            int want = Mathf.CeilToInt(n * p), run = 0;
            for (int i = 0; i < travelHist.Length; i++) { run += travelHist[i]; if (run >= want) return i + 1; }
            return travelHist.Length;
        }

        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append($"{dist / 1000f:0.00} km, vmax {vmax * 2.23694f:0} mph; HITS {hits} (CLUNKS >=2 m/s {clunks}), worst {worst:0.00} m/s");
                sb.Append($"; strikes 1-1.5/1.5-2/2-3/3+ m/s {strikes[0]}/{strikes[1]}/{strikes[2]}/{strikes[3]}");
                sb.Append("; hits by mph");
                for (int b = 0; b < 4; b++) sb.Append($" {SpeedBand[b]}:{hitsBySpeed[b]}/{ticksBySpeed[b] / 60f:0}s");
                sb.Append($"; rubber {100f * rubberTicks / Mathf.Max(1, ticks):0.0}% on-stop {100f * stopTicks / Mathf.Max(1, ticks):0.0}% of ticks");
                sb.Append($"; travel max F {travelMax[0] * 100f:0.0} R {travelMax[1] * 100f:0.0} cm, p50 {TravelPercentile(0.5f)} p99 {TravelPercentile(0.99f)} cm");
                sb.Append($"; least room to the stop F {roomMin[0] * 100f:0.0} R {roomMin[1] * 100f:0.0} cm; longest flight {airMax:0.00} s");
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// THE SUSPENSION RIDE CHECK (2026-10-25). Edit mode first, on the bump-stop
    /// harness's flat world (BumpStopShots): for the reference FD, the '70
    /// Charger and a stage-4 S13 -
    ///   - the CONTROL drive (full throttle, a 0.45-lock corner, a stop, a
    ///     slalom): where the car ends up, the travel it used, rubber/stop ticks
    ///     - an unchanged path = unchanged ordinary handling;
    ///   - a 1 m landing at 72 km/h and a 15 cm kerb at 50 km/h: the strikes,
    ///     the tyre drawn through the body, the flight after the kerb;
    ///   - a straight FREEWAY at 150 mph, flat, then on a KINKED profile (a
    ///     polyline with a random grade break every 15 m, 0.5 deg typical) at
    ///     150 and 90 mph: hits, clunks, travel.
    /// Then (unless PSX_SUSP_RACE=0) it hands over to RacePlayCheck with
    /// PSX_RACE_SUSP=1, which tallies every car of the race the same way. Writes
    /// PSXRacing_susp_ride.txt. tools\susp-ride-check.ps1 runs it.
    /// </summary>
    public static class SuspensionRideCheck
    {
        const float Dt = BumpStopShots.Dt;
        static StringBuilder log;
        static GameObject road;

        public static void Run()
        {
            log = new StringBuilder();
            log.AppendLine("SUSPENSION RIDE CHECK " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            var prevMode = Physics.simulationMode;
            float prevDt = Time.fixedDeltaTime;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Time.fixedDeltaTime = Dt;
                ArchCeilings.ClearCache();
                BumpStopShots.BuildWorld();
                foreach (var (key, byId, stage) in new[]
                         {
                             ("mazda_rx_7_type_rs__fd___98", true, 0),
                             ("dodge_charger_440_r_t__70", true, 0),
                             ("nissan_180sx", false, 4),
                         })
                {
                    var spec = byId ? SpecById(key) : BumpStopShots.SpecFor(key);
                    if (spec == null) { log.AppendLine("SKIP " + key); continue; }
                    log.AppendLine($"=== {spec.name} stage {stage} ===");
                    var d = BumpStopShots.Drive(spec, stage);
                    log.AppendLine($"  CONTROL end ({d.pos.x:0.00}, {d.pos.z:0.00}) yaw {d.yaw:0.00}; {d.Text}");
                    Event(spec, stage, "landing 1 m @72 km/h", 1.0f, 20f, false);
                    Event(spec, stage, "kerb 15 cm @50 km/h", 0f, 13.9f, true);
                    Freeway(spec, stage, false, 67f);
                    Freeway(spec, stage, true, 67f);
                    Freeway(spec, stage, true, 40f);
                }
            }
            catch (Exception e) { log.AppendLine("FAIL " + e); }
            finally
            {
                Physics.simulationMode = prevMode;
                Time.fixedDeltaTime = prevDt;
                if (road != null) UnityEngine.Object.DestroyImmediate(road);
                if (BumpStopShots.world != null) UnityEngine.Object.DestroyImmediate(BumpStopShots.world);
                File.WriteAllText("PSXRacing_susp_ride.txt", log.ToString());
                Debug.Log("[SuspensionRideCheck]\n" + log);
            }
            if (Environment.GetEnvironmentVariable("PSX_SUSP_RACE") == "0") { EditorApplication.Exit(0); return; }
            Environment.SetEnvironmentVariable("PSX_RACE_SUSP", "1");
            RacePlayCheck.Run();
        }

        static CarSpec SpecById(string id)
        {
            foreach (var s in CarCatalog.All) if (s.id == id) return s;
            return null;
        }

        static void Event(CarSpec spec, int stage, string what, float dropM, float speed, bool kerb)
        {
            BumpStopShots.kerbGo.SetActive(kerb);
            Physics.SyncTransforms();
            var car = BumpStopShots.MakeCar(spec, stage, new Vector3(0f, dropM, 0f), speed);
            var body = car.GetComponent<CarBody>();
            var lean = car.GetComponent<CarBodyLean>();
            var responder = car.GetComponent<CollisionResponder>();
            var tally = car.gameObject.AddComponent<SuspTally>();
            tally.manual = true; tally.Hook();
            float y0 = car.Body.position.y, peakRise = 0f, upMax = 0f, clip = 0f;
            bool touched = false;
            int ticks = Mathf.RoundToInt(2.4f / Dt);
            for (int t = 0; t < ticks; t++)
            {
                if (kerb) car.throttleInput = 0.25f;
                BumpStopShots.CarFixed.Invoke(car, null);
                lean.Pose(Dt);
                tally.Sample(Dt);
                if (car.anyWheelGrounded) touched = true;
                if (touched)
                {
                    peakRise = Mathf.Max(peakRise, car.Body.position.y - y0);
                    upMax = Mathf.Max(upMax, car.Body.linearVelocity.y);
                }
                if (t % 2 == 0) clip = Mathf.Max(clip, ArchProbe.DrawnClip(body, out _, out _));
                Physics.Simulate(Dt);
            }
            float vEnd = Vector3.ProjectOnPlane(car.Body.linearVelocity, Vector3.up).magnitude;
            log.AppendLine($"  {what}: strikes {responder.BottomHits}, hardest {responder.WorstBottom:0.00} m/s; " +
                           $"travel max F {tally.travelMax[0] * 100f:0.0} R {tally.travelMax[1] * 100f:0.0} cm; " +
                           $"body up to {upMax:0.00} m/s, {peakRise * 100f:0} cm over its start" +
                           (kerb ? $" (kerb is 15 cm); flight {tally.airMax:0.00} s" : "") +
                           $"; tyre drawn into the body {clip * 1000f:0} mm; {speed * 3.6f:0} -> {vEnd * 3.6f:0} km/h");
            UnityEngine.Object.DestroyImmediate(car.transform.parent.gameObject);
        }

        const float RoadX = 2000f, Station = 15f, RoadLen = 3600f;

        /// <summary>A straight road 300 m wide along +z at x = RoadX: flat, or a
        /// polyline whose grade breaks by a random ~0.5 deg every 15 m (held
        /// within +-3 deg; fixed seed). The road layer, so the tyres have road
        /// grip.</summary>
        static void BuildRoad(bool kinked)
        {
            if (road != null) UnityEngine.Object.DestroyImmediate(road);
            int n = Mathf.CeilToInt(RoadLen / Station) + 1;
            var verts = new Vector3[n * 2];
            var tris = new int[(n - 1) * 6];
            var rng = new System.Random(20261025);
            float h = 0f, grade = 0f;
            for (int i = 0; i < n; i++)
            {
                float z = -100f + i * Station;
                if (kinked && i > 10)
                {
                    // Box-Muller: a normal break, 0.5 deg typical.
                    double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                    float g = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
                    grade = Mathf.Clamp(grade + g * 0.5f * Mathf.Deg2Rad, -3f * Mathf.Deg2Rad, 3f * Mathf.Deg2Rad);
                    // drift back toward level so the road does not climb away
                    grade *= 0.97f;
                }
                if (i > 0) h += Mathf.Tan(grade) * Station;
                verts[i * 2] = new Vector3(RoadX - 150f, h, z);
                verts[i * 2 + 1] = new Vector3(RoadX + 150f, h, z);
                if (i == 0) continue;
                int a = (i - 1) * 2, k = (i - 1) * 6;
                tris[k] = a; tris[k + 1] = a + 2; tris[k + 2] = a + 1;
                tris[k + 3] = a + 1; tris[k + 4] = a + 2; tris[k + 5] = a + 3;
            }
            var mesh = new Mesh { name = kinked ? "KinkedRoad" : "FlatRoad", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            road = new GameObject(mesh.name);
            road.transform.SetParent(BumpStopShots.world.transform);
            road.layer = 8;
            road.AddComponent<MeshCollider>().sharedMesh = mesh;
            Physics.SyncTransforms();
        }

        static void Freeway(CarSpec spec, int stage, bool kinked, float mps)
        {
            BuildRoad(kinked);
            BumpStopShots.kerbGo.SetActive(false);
            var car = BumpStopShots.MakeCar(spec, stage, new Vector3(RoadX, 0.02f, 0f), mps);
            car.SetRolling(mps);
            car.Body.linearVelocity = Vector3.forward * mps;
            var lean = car.GetComponent<CarBodyLean>();
            var tally = car.gameObject.AddComponent<SuspTally>();
            tally.manual = true; tally.Hook();
            int ticks = Mathf.RoundToInt((RoadLen - 300f) / mps / Dt);
            float settle = 1.5f;
            for (int t = 0; t < ticks; t++)
            {
                float v = Vector3.Dot(car.Body.linearVelocity, car.transform.forward);
                car.throttleInput = Mathf.Clamp01(0.6f + (mps - v) * 0.5f);
                car.brakeInput = Mathf.Clamp01((v - mps - 3f) * 0.2f);
                car.steerInput = 0f;
                BumpStopShots.CarFixed.Invoke(car, null);
                lean.Pose(Dt);
                if (t * Dt > settle) tally.Sample(Dt);
                Physics.Simulate(Dt);
                if (car.Body.position.y < -50f) { log.AppendLine("  (fell off the road)"); break; }
            }
            log.AppendLine($"  FREEWAY {(kinked ? "KINKED" : "FLAT  ")} {mps * 2.23694f:0} mph: {tally.Text}");
            UnityEngine.Object.DestroyImmediate(car.transform.parent.gameObject);
        }
    }
}
