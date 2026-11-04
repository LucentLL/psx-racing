using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CABLE MEDIAN CHECK (dividers round 2, 2026-11-02). The review asked two things
    /// of the I-485 / NC 16 cable barrier before it ships:
    ///
    ///   1. A 250 km/h hit on its collider must not tunnel: the car is stepped into the
    ///      cable's own collider strip (CityMeshes.CableColliderTestMesh - the tile's pieces,
    ///      turned-down ends and all) on a flat slab, the way <see cref="TreeCrashSim"/>
    ///      drives into a tree: Physics.simulationMode Script, the car's FixedUpdate by hand.
    ///      Cases at 250 km/h at 10, 25 and 90 degrees, one at 120 km/h into the turned-down
    ///      end, and the control (no cable: the car must sail across the line).
    ///   2. Nothing that drives itself is sent across a cable median: race routes on a
    ///      cable or open median (AI racers drive only their route's path and are respawned
    ///      onto it), and the clearance from each carriageway's paved median edge - which the
    ///      traffic lanes never leave - to the cable line as built.
    ///
    /// Writes PSXRacing_cablecrash.txt at the project root. Headless:
    /// -executeMethod PSXRacing.EditorTools.CableCrashSim.Run
    /// </summary>
    public static class CableCrashSim
    {
        const float Dt = 0.02f;
        const float LineX = 6f;

        public static void Run()
        {
            var log = new StringBuilder();
            bool ok = true;
            log.AppendLine("=== THE CABLE MEDIAN: CRASHES ===");
            foreach (var c in new[]
            {
                (name: "250 km/h at 10 deg (glancing)", kmh: 250f, deg: 10f, cable: true, atEnd: false),
                (name: "250 km/h at 25 deg", kmh: 250f, deg: 25f, cable: true, atEnd: false),
                (name: "250 km/h at 90 deg (square on)", kmh: 250f, deg: 90f, cable: true, atEnd: false),
                (name: "120 km/h along into the turned-down end", kmh: 120f, deg: 0f, cable: true, atEnd: true),
                (name: "250 km/h at 25 deg, no cable (control)", kmh: 250f, deg: 25f, cable: false, atEnd: false),
            })
            {
                var r = Case(c.kmh, c.deg, c.cable, c.atEnd);
                bool pass = c.atEnd ? !r.fellThrough && r.maxY < 1.5f       // stopped at the anchor or ridden up the turn-down, never launched or through
                          : c.cable ? r.maxX < LineX - 0.2f && !r.fellThrough
                          : r.maxX > LineX + 5f;
                ok &= pass;
                log.AppendLine($"  {(pass ? "ok  " : "FAIL")} {c.name,-44} car centre got to x {r.maxX,6:0.00} (cable line x {LineX:0.0}), still doing {r.endSpeed,5:0.0} m/s, max climb {r.maxY:0.00} m{(r.fellThrough ? " FELL THROUGH" : "")}");
            }
            log.AppendLine("=== THE CABLE MEDIAN: WHO COULD CROSS IT ===");
            log.Append(Exposure());
            log.AppendLine(ok ? "CABLE CRASH OK" : "CABLE CRASH FAILED");
            File.WriteAllText("PSXRacing_cablecrash.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        struct Reading { public float maxX, endSpeed, maxY; public bool fellThrough, stopped; }

        static Reading Case(float kmh, float deg, bool cable, bool atEnd)
        {
            var prevMode = Physics.simulationMode;
            float prevDt = Time.fixedDeltaTime;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var made = new List<Object>();
            var reading = new Reading { maxX = float.MinValue };
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Time.fixedDeltaTime = Dt;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.transform.position = new Vector3(0f, -0.5f, 200f);
                ground.transform.localScale = new Vector3(400f, 1f, 800f);
                made.Add(ground);
                if (cable)
                {
                    var go = new GameObject("CableCollider");
                    go.layer = CityWorld.SolidLayer;
                    // along z at x = LineX; for the end case the run starts just ahead of the car
                    var a = atEnd ? new Vector3(0f, 0f, 60f) : new Vector3(LineX, 0f, -50f);
                    var b = atEnd ? new Vector3(0f, 0f, 400f) : new Vector3(LineX, 0f, 450f);
                    var mesh = CityMeshes.CableColliderTestMesh(a, b);
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    made.Add(go); made.Add(mesh);
                }
                var carGo = new GameObject("CrashCar");
                var box = carGo.AddComponent<BoxCollider>();
                box.size = new Vector3(1.72f, 1.0f, 4.1f);
                box.center = new Vector3(0f, 0.6f, 0f);
                carGo.layer = 2;
                carGo.AddComponent<Rigidbody>();
                var car = carGo.AddComponent<CarController>();
                typeof(CarController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(car, null);
                var dir = Quaternion.Euler(0f, deg, 0f);
                // start so the car meets the line about 40 m on
                float back = deg > 1f ? 40f : 0f;
                var start = atEnd ? new Vector3(0f, 0.8f, 20f) : new Vector3(LineX - back * Mathf.Sin(deg * Mathf.Deg2Rad), 0.8f, 100f - back * Mathf.Cos(deg * Mathf.Deg2Rad));
                carGo.transform.SetPositionAndRotation(start, dir);
                made.Add(carGo);
                Physics.SyncTransforms();
                var carStep = typeof(CarController).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < 40; i++) { carStep?.Invoke(car, null); Physics.Simulate(Dt); }
                car.Body.linearVelocity = dir * Vector3.forward * (kmh / 3.6f);
                car.throttleInput = 0.6f;
                int steps = Mathf.RoundToInt(4f / Dt);
                for (int i = 0; i < steps; i++)
                {
                    carStep?.Invoke(car, null);
                    Physics.Simulate(Dt);
                    var p = carGo.transform.position;
                    reading.maxX = Mathf.Max(reading.maxX, atEnd ? Mathf.Abs(p.x) : p.x);
                    reading.maxY = Mathf.Max(reading.maxY, p.y - 0.8f);
                    if (p.y < -2f) reading.fellThrough = true;
                }
                reading.endSpeed = car.Body.linearVelocity.magnitude;
                reading.stopped = reading.endSpeed < 5f;
            }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                Physics.simulationMode = prevMode;
                Time.fixedDeltaTime = prevDt;
            }
            return reading;
        }

        static string Exposure()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) return "  no city map\n";
            CityMeshes.NodeTrims(map);   // classifies the dividers
            int routeEdges = 0, routeOnDiv = 0;
            var names = new HashSet<string>();
            foreach (var r in map.routes)
                foreach (int ei in r.edges)
                {
                    routeEdges++;
                    if (CityMeshes.DividerAt(map.edges[ei]) != CityMeshes.DividerKind.Concrete) { routeOnDiv++; names.Add(r.id); }
                }
            sb.AppendLine($"  race routes: {map.routes.Length} routes, {routeEdges} route edges, {routeOnDiv} of them on a cable or open median{(names.Count > 0 ? " (" + string.Join(",", names) + ")" : "")} - an AI racer drives its route's path and RaceManager.RespawnCar puts it back on that path");
            // clearance from the paved median edge (traffic lanes stay inside it) to the cable
            int samples = 0, under = 0; float minOff = float.MaxValue; string minAt = "";
            foreach (var e in map.edges)
            {
                if (CityMeshes.DividerAt(e) != CityMeshes.DividerKind.Cable) continue;
                for (float s = 1f; s < e.length; s += 10f)
                {
                    float off = CityMeshes.CableOffsetIfBuilt(e, s);
                    if (float.IsNaN(off)) continue;
                    samples++;
                    if (off < 1.5f) under++;
                    if (off < minOff) { minOff = off; var p = e.PointAt(s); minAt = $"e{e.index} '{e.name}' s {s:0} at ({p.x:0},{p.y:0})"; }
                }
            }
            sb.AppendLine($"  cable line vs the paved median edge: {samples} samples, closest {(samples > 0 ? minOff.ToString("0.00") : "-")} m ({minAt}), {under} closer than 1.5 m");
            sb.AppendLine($"  {CityMeshes.LastDividerReport}");
            sb.AppendLine($"  {CityMeshes.LastDividerGate.Replace("\n", "\n  ")}");
            return sb.ToString();
        }
    }
}
