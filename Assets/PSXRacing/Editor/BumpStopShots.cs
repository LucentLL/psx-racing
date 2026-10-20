using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// PROVE THE BUMP STOPS (owner, 2026-10-19: "tires clipping up through
    /// fenders and hood ... This should never happen").
    ///
    /// Three cars that bracket the problem - a tuner on its lowest suspension
    /// stage, a muscle car, a hatch - each put through the two things that
    /// drove tyres through the bodywork: a 1 m landing at 72 km/h and a 15 cm
    /// kerb at 50 km/h. Every case runs twice, stops OFF (the suspension as it
    /// was) and ON, and reports:
    ///   - the deepest the DRAWN tyre reached up through the body mesh
    ///     (ArchProbe.DrawnClip: wheel vertices against body triangles, no
    ///     field involved) and how many vertices went over 1 mm;
    ///   - the strike speed at the stop, and the speed the landing cost;
    ///   - a side shot of the hardest-hit wheel at its deepest compression.
    /// Then the CONTROL that matters most: a hard normal drive (full throttle,
    /// a 0.45-lock corner, a stop from speed, a slalom) on each car at stock
    /// ride, stops off twice (the solver's own noise) and on once - the rubber
    /// must never engage and the path must not move.
    ///
    /// Drives the physics the way ParkedCarSim does: Script simulation,
    /// CarController.FixedUpdate by hand, Physics.Simulate behind it. Needs
    /// graphics for the shots (-force-glcore, no -nographics).
    /// `-executeMethod PSXRacing.EditorTools.BumpStopShots.Run`
    /// </summary>
    public static class BumpStopShots
    {
        const float Dt = 1f / 60f;
        const string OutDir = "Screenshots/BumpStop";

        struct Case { public string label, key; public int stage; }
        static readonly Case[] Cases =
        {
            new Case { label = "tuner, suspension stage 4", key = "nissan_180sx", stage = 4 },
            new Case { label = "muscle car", key = "charger_70", stage = 0 },
            new Case { label = "hatch", key = "civic_eg", stage = 0 },
            new Case { label = "old pack hatch", key = "euro_hatch", stage = 0 },
        };

        static readonly MethodInfo BuildOne = typeof(PSXRacingBuilder).GetMethod("BuildOneCar",
            BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo CarFixed = typeof(CarController).GetMethod("FixedUpdate",
            BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo Compression = typeof(CarController).GetField("suspensionCompression",
            BindingFlags.NonPublic | BindingFlags.Instance);

        static StringBuilder log;
        static Camera cam;
        static GameObject world;

        /// <summary>The whole pass in one editor: measure the arches, report
        /// every car's room, then the tests.</summary>
        public static void BakeAndRun()
        {
            ArchProbe.Bake();
            ArchProbe.BakeExact();
            ArchProbe.Report();
            Run();
        }

        [MenuItem("Tools/PSX Racing/Bump Stop Shots")]
        public static void Run()
        {
            log = new StringBuilder();
            Directory.CreateDirectory(OutDir);
            var prevMode = Physics.simulationMode;
            float prevDt = Time.fixedDeltaTime;
            bool prevStops = CarController.BumpStopsEnabled;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Time.fixedDeltaTime = Dt;
                ArchCeilings.ClearCache();
                BuildWorld();
                string only = Environment.GetEnvironmentVariable("PSX_BUMP_ONLY");
                bool fast = Environment.GetEnvironmentVariable("PSX_BUMP_FAST") == "1";
                foreach (var c in Cases)
                {
                    if (!string.IsNullOrEmpty(only) && c.key != only) continue;
                    var spec = SpecFor(c.key);
                    if (spec == null) { log.AppendLine($"SKIP {c.key}: no catalog car on this shell"); continue; }
                    log.AppendLine($"=== {spec.name} ({c.key}) - {c.label} ===");
                    foreach (bool on in new[] { false, true })
                    {
                        CarController.BumpStopsEnabled = on;
                        Event(spec, c, on, "landing", 1.0f, 20f, kerb: false);
                        Event(spec, c, on, "kerb", 0f, 13.9f, kerb: true);
                    }
                    if (fast) continue;
                    CarController.BumpStopsEnabled = false;
                    var off1 = Drive(spec, 0, c.key + "_rest_before");
                    var off2 = Drive(spec, 0);
                    CarController.BumpStopsEnabled = true;
                    var onRun = Drive(spec, 0, c.key + "_rest_after");
                    log.AppendLine($"  NORMAL DRIVE (stock ride): {onRun.Text}; " +
                                   $"path off-vs-off {Vector3.Distance(off1.pos, off2.pos) * 100f:0.00} cm, off-vs-on {Vector3.Distance(off1.pos, onRun.pos) * 100f:0.00} cm, " +
                                   $"heading off-vs-on {Mathf.Abs(Mathf.DeltaAngle(off1.yaw, onRun.yaw)):0.000} deg; stops off: {off1.Text}");
                    if (c.stage > 0)
                    {
                        CarController.BumpStopsEnabled = false;
                        var lowOff = Drive(spec, c.stage, c.key + "_stage" + c.stage + "_before");
                        CarController.BumpStopsEnabled = true;
                        log.AppendLine($"  NORMAL DRIVE (stage {c.stage}): {Drive(spec, c.stage, c.key + "_stage" + c.stage + "_after").Text}; stops off: {lowOff.Text}");
                    }
                }
            }
            catch (Exception e) { log.AppendLine("FAIL " + e); }
            finally
            {
                CarController.BumpStopsEnabled = prevStops;
                Physics.simulationMode = prevMode;
                Time.fixedDeltaTime = prevDt;
                if (world != null) UnityEngine.Object.DestroyImmediate(world);
                File.WriteAllText("PSXRacing_bumpstop.txt", log.ToString());
                Debug.Log("[BumpStopShots]\n" + log);
            }
        }

        static CarSpec SpecFor(string key)
        {
            foreach (var s in CarCatalog.All)
            {
                var d = CarModelLibrary.LoadFor(s);
                if (d != null && d.key == key) return s;
            }
            return null;
        }

        static void BuildWorld()
        {
            world = new GameObject("BumpWorld");
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(world.transform);
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(40f, -60f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.56f);
            RenderSettings.fog = false;
            var globals = sun.gameObject.AddComponent<PSXGlobals>();
            globals.sun = sun;
            globals.ambient = new Color(0.55f, 0.55f, 0.60f);
            globals.fogNear = 400f;
            globals.fogFar = 900f;
            globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material Flat(Color c)
            {
                var m = new Material(shader);
                m.SetColor("_BaseColor", c);
                return m;
            }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(world.transform);
            ground.transform.position = new Vector3(0f, -0.5f, 150f);
            ground.transform.localScale = new Vector3(400f, 1f, 600f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.33f, 0.34f, 0.35f));
            ground.layer = 0;
            // The kerb: 15 cm, 60 cm deep, square-edged, across the whole lane.
            var kerb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            kerb.name = "Kerb";
            kerb.transform.SetParent(world.transform);
            kerb.transform.position = new Vector3(0f, 0.075f, 30f);
            kerb.transform.localScale = new Vector3(20f, 0.15f, 0.6f);
            kerb.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.75f, 0.72f, 0.66f));
            kerb.layer = 0;
            kerb.SetActive(false);
            kerbGo = kerb;

            var camGo = new GameObject("ShotCam");
            camGo.transform.SetParent(world.transform);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.62f, 0.70f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            Physics.SyncTransforms();
        }
        static GameObject kerbGo;

        static CarController MakeCar(CarSpec spec, int stage, Vector3 pos, float speed)
        {
            var def = CarModelLibrary.LoadFor(spec);
            string skin = def != null && def.skinNames != null && def.skinNames.Length > 0 ? def.skinNames[0] : "";
            var holder = new GameObject("Cars");
            holder.transform.SetParent(world.transform);
            var car = (CarController)BuildOne.Invoke(null, new object[]
            {
                holder.transform, ValueTuple.Create("BumpTestCar", def.key, skin, 1f, 0f), true,
                pos, Quaternion.identity, null, null,
            });
            // The builder's blob shadow takes a material the harness does not
            // have (it would draw error-pink); the sun lights the shots anyway.
            var blob = car.transform.Find("BlobShadow");
            if (blob != null) blob.gameObject.SetActive(false);
            // Its voices are built in play mode only; a strike would play into nothing.
            UnityEngine.Object.DestroyImmediate(car.GetComponent<CollisionAudio>());
            Awake(car);
            Awake(car.GetComponent<CollisionResponder>());
            Awake(car.GetComponent<CarBodyLean>());
            var body = car.GetComponent<CarBody>();
            body.ApplySpec(spec);
            car.ApplySpec(spec, new CarTune.Stages { suspension = stage });
            // Seat it: the origin goes where full droop puts the tyres "pos.y" off the ground.
            float r = Mathf.Max(car.WheelRadiusOf(0), car.WheelRadiusOf(2));
            Vector3 p = new Vector3(pos.x, pos.y + r - (car.mountHeight - car.restLength), pos.z);
            car.transform.position = p;
            car.Body.position = p;
            car.Body.rotation = Quaternion.identity;
            car.Body.linearVelocity = new Vector3(0f, 0f, speed);
            car.Body.angularVelocity = Vector3.zero;
            car.throttleInput = 0f; car.brakeInput = 0f; car.steerInput = 0f; car.handbrakeInput = false;
            Physics.SyncTransforms();
            return car;
        }

        static void Awake(Component c)
        {
            if (c == null) return;
            c.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(c, null);
        }

        static void Event(CarSpec spec, Case c, bool on, string what, float dropM, float speed, bool kerb)
        {
            kerbGo.SetActive(kerb);
            Physics.SyncTransforms();
            var car = MakeCar(spec, c.stage, new Vector3(0f, dropM, 0f), speed);
            var body = car.GetComponent<CarBody>();
            var responder = car.GetComponent<CollisionResponder>();
            var comp = (float[])Compression.GetValue(car);

            float deepest = -1f, worstClip = 0f, worstClipAny = 0f; int worstWheel = 1, over = 0, overAny = 0;
            float speedBefore = speed, speedAfter = -1f, landedAt = -1f;
            Texture2D shot = null;
            int ticks = Mathf.RoundToInt(2.4f / Dt);
            for (int t = 0; t < ticks; t++)
            {
                if (kerb) car.throttleInput = 0.25f;     // hold roughly the pace over the kerb
                CarFixed.Invoke(car, null);
                car.GetComponent<CarBodyLean>().Pose(Dt);   // the shell as this frame draws it
                // Measured between the car's tick and the step, when the hubs
                // and the body are the pose this frame draws.
                float worstHere = 0f; int wheelHere = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (comp[i] > worstHere) { worstHere = comp[i]; wheelHere = i; }
                }
                if (car.anyWheelGrounded && landedAt < 0f && t > 0) landedAt = t * Dt;
                if (landedAt >= 0f && speedAfter < 0f && t * Dt >= landedAt + 0.4f)
                    speedAfter = Vector3.ProjectOnPlane(car.Body.linearVelocity, Vector3.up).magnitude;
                if (worstHere > deepest + 1e-4f && wheelHere >= 0)
                {
                    deepest = worstHere; worstWheel = wheelHere;
                    float clip = ArchProbe.DrawnClip(body, out int n, out _);
                    worstClip = clip; over = n;
                    if (shot != null) UnityEngine.Object.DestroyImmediate(shot);
                    shot = Shoot(car, worstWheel);
                }
                else
                {
                    float clip = ArchProbe.DrawnClip(body, out int n, out int w);
                    if (clip > worstClipAny) { worstClipAny = clip; overAny = n; }
                    if (on && clip > 0.001f && w >= 0)
                    {
                        var br = body.bodyRoot;
                        log.AppendLine($"    clip t={t} wheel {w}: {clip * 1000f:0} mm ({n} verts); shell heave {(br.localPosition.y - body.SeatPosition.y) * 100f:0.0} cm " +
                                       $"pitch {Mathf.DeltaAngle(0f, br.localEulerAngles.x):0.00} roll {Mathf.DeltaAngle(0f, br.localEulerAngles.z):0.00}; " +
                                       $"hub drawn {car.wheelHubs[w].localPosition.y:0.000} physics {car.PhysicsHubY(w):0.000} arch-at-rest {car.HubMax(w):0.000}");
                    }
                }
                Physics.Simulate(Dt);
            }
            worstClipAny = Mathf.Max(worstClipAny, worstClip);
            overAny = Mathf.Max(overAny, over);
            float archAt = car.HubMax(worstWheel) - (car.mountHeight - car.restLength);
            float stopAt = car.BumpLimit(worstWheel);
            string name = $"{c.key}_{what}_{(on ? "after" : "before")}";
            if (shot != null)
            {
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), shot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(shot);
            }
            log.AppendLine($"  {what,-8} stops {(on ? "ON " : "OFF")}: deepest compression {deepest * 100f:0.0} cm on wheel {worstWheel} " +
                           $"(arch at {archAt * 100f:0.0} cm, hard stop at {(float.IsInfinity(stopAt) ? 0f : stopAt * 100f):0.0} cm); drawn tyre through the body: worst {worstClipAny * 1000f:0} mm, " +
                           $"{overAny} points > 1 mm (at the deepest frame {worstClip * 1000f:0} mm); " +
                           $"strikes {responder.BottomHits}, hardest {responder.WorstBottom:0.0} m/s, tyre cost {responder.BottomScore:0.00}; " +
                           $"speed {speedBefore * 3.6f:0.0} -> {(speedAfter < 0f ? 0f : speedAfter) * 3.6f:0.0} km/h 0.4 s after touching down -> {name}.png");
            UnityEngine.Object.DestroyImmediate(car.transform.parent.gameObject);
        }

        struct Pose
        {
            public Vector3 pos; public float yaw;
            public int rubberTicks, stopTicks;
            public float leastRoom, travelF, travelR, restClip, driveClip, straightClip, restSeat, unseatedClip;
            public int unseatedOver;
            public int restOver, driveOver;
            public string worstAt;
            public string Text =>
                $"rubber engaged {rubberTicks} ticks, on the stop {stopTicks}; travel used F {travelF * 100f:0.0} / R {travelR * 100f:0.0} cm " +
                $"over the settled ride, least room left to the rubber {leastRoom * 100f:0.0} cm; at rest the tyres reach {restClip * 1000f:0} mm into the body ({restOver} points > 1 mm); driving, at worst {driveClip * 1000f:0} mm ({driveOver} points > 1 mm) [{worstAt}], with the front wheels straight {straightClip * 1000f:0} mm; shell seated {restSeat * 100f:+0.0;-0.0} cm over its rest at rest (without that ride-up the tyres would reach {unseatedClip * 1000f:0} mm, {unseatedOver} points)";
        }

        /// <summary>A hard drive on flat ground: settle 1 s (and look for a tyre
        /// already in the body at rest), full throttle to ~80 km/h, a 0.45-lock
        /// corner for 3 s, a full stop, a slalom. Counts ticks with any wheel in
        /// its bump rubber or on its stop, the most compression any axle used
        /// over its settled ride, and the least room left before the rubber
        /// (for stops OFF: the room the rubber WOULD have had).</summary>
        static Pose Drive(CarSpec spec, int stage, string restShot = null)
        {
            kerbGo.SetActive(false);
            Physics.SyncTransforms();
            var car = MakeCar(spec, stage, new Vector3(0f, 0.02f, -120f), 0f);
            var body = car.GetComponent<CarBody>();
            var comp = (float[])Compression.GetValue(car);
            var res = new Pose { leastRoom = float.PositiveInfinity };
            var settled = new float[4];
            float t = 0f, phaseStart = 0f; int phase = -1;
            int ticks = Mathf.RoundToInt(17f / Dt);
            for (int k = 0; k < ticks; k++, t += Dt)
            {
                float v = car.Body.linearVelocity.magnitude;
                switch (phase)
                {
                    case -1: car.throttleInput = 0f; car.steerInput = 0f; car.brakeInput = 0f;
                        if (t > 1f)
                        {
                            for (int i = 0; i < 4; i++) settled[i] = comp[i];
                            res.restClip = ArchProbe.DrawnClip(body, out res.restOver, out _);
                            res.restSeat = body.bodyRoot.localPosition.y - body.SeatPosition.y;
                            if (CarController.BumpStopsEnabled)
                            {
                                // The same instant with the ride-up taken away: is the seat's lift real?
                                var br = body.bodyRoot;
                                Vector3 keepP = br.localPosition; Quaternion keepR = br.localRotation;
                                var keepH = new float[4];
                                for (int i = 0; i < 4; i++) keepH[i] = car.wheelHubs[i].localPosition.y;
                                br.localPosition = body.SeatPosition;
                                br.localRotation = Quaternion.Euler(0f, body.Def.bodyYaw, 0f);
                                for (int i = 0; i < 4; i++)
                                {
                                    var lp = car.wheelHubs[i].localPosition; lp.y = car.PhysicsHubY(i); car.wheelHubs[i].localPosition = lp;
                                }
                                res.unseatedClip = ArchProbe.DrawnClip(body, out res.unseatedOver, out _);
                                br.localPosition = keepP; br.localRotation = keepR;
                                for (int i = 0; i < 4; i++)
                                {
                                    var lp = car.wheelHubs[i].localPosition; lp.y = keepH[i]; car.wheelHubs[i].localPosition = lp;
                                }
                            }
                            if (restShot != null)
                            {
                                var tex = Shoot(car, 1);
                                if (tex != null)
                                {
                                    File.WriteAllBytes(Path.Combine(OutDir, restShot + ".png"), tex.EncodeToPNG());
                                    UnityEngine.Object.DestroyImmediate(tex);
                                }
                            }
                            phase = 0; phaseStart = t;
                        }
                        break;
                    case 0: car.throttleInput = 1f; car.steerInput = 0f; car.brakeInput = 0f;
                        if (v > 22f || t - phaseStart > 9f) { phase = 1; phaseStart = t; } break;
                    case 1: car.throttleInput = 0.6f; car.steerInput = 0.45f;
                        if (t - phaseStart > 3f) { phase = 2; phaseStart = t; } break;
                    case 2: car.throttleInput = 0f; car.steerInput = 0f; car.brakeInput = 1f;
                        if (t - phaseStart > 1.5f) { phase = 3; phaseStart = t; } break;
                    default: car.brakeInput = 0f; car.throttleInput = 0.7f;
                        car.steerInput = Mathf.Sin((t - phaseStart) * Mathf.PI * 2f) > 0f ? 0.4f : -0.4f; break;
                }
                CarFixed.Invoke(car, null);
                car.GetComponent<CarBodyLean>().Pose(Dt);   // the shell as this frame draws it
                if (phase >= 0)
                {
                    bool rubber = false, stop = false;
                    for (int i = 0; i < 4; i++)
                    {
                        if (car.InBumpRubber(i)) rubber = true;
                        if (car.OnBumpStop(i)) stop = true;
                        if (comp[i] <= 0f) continue;
                        float used = comp[i] - settled[i];
                        if (i < 2) res.travelF = Mathf.Max(res.travelF, used); else res.travelR = Mathf.Max(res.travelR, used);
                        float h = car.HubMax(i);
                        if (!float.IsPositiveInfinity(h))
                            res.leastRoom = Mathf.Min(res.leastRoom, h - (car.mountHeight - car.restLength) - CarController.BumpMargin
                                                                     - CarController.BumpRubberLen - comp[i]);
                    }
                    if (rubber) res.rubberTicks++;
                    if (stop) res.stopTicks++;
                    if (k % 3 == 0)
                    {
                        float clip = ArchProbe.DrawnClip(body, out int over, out int cw);
                        if (Mathf.Abs(Mathf.DeltaAngle(0f, car.wheelHubs[0].localEulerAngles.y)) < 1f)
                            res.straightClip = Mathf.Max(res.straightClip, clip);
                        if (clip > res.driveClip)
                        {
                            res.driveClip = clip; res.driveOver = over;
                            if (cw >= 0)
                            {
                                var br = body.bodyRoot;
                                res.worstAt = $"t={t:0.00}s phase {phase} wheel {cw} steer {car.wheelHubs[cw].localEulerAngles.y:0.0} deg; shell heave {(br.localPosition.y - body.SeatPosition.y) * 100f:0.0} cm " +
                                              $"pitch {Mathf.DeltaAngle(0f, br.localEulerAngles.x):0.00} roll {Mathf.DeltaAngle(0f, br.localEulerAngles.z):0.00}; hub drawn {car.wheelHubs[cw].localPosition.y:0.000} " +
                                              $"physics {car.PhysicsHubY(cw):0.000} arch-at-rest {car.HubMax(cw):0.000}";
                            }
                        }
                    }
                }
                Physics.Simulate(Dt);
            }
            res.pos = car.Body.position;
            res.yaw = car.Body.rotation.eulerAngles.y;
            UnityEngine.Object.DestroyImmediate(car.transform.parent.gameObject);
            return res;
        }

        /// <summary>From the side and above the wheel, looking down onto its
        /// arch, the wing top and the bonnet edge - where a tyre through the
        /// paint shows.</summary>
        static Texture2D Shoot(CarController car, int wheel)
        {
            var hub = car.wheelHubs[wheel];
            Vector3 side = car.transform.right * (wheel % 2 == 0 ? -1f : 1f);
            Vector3 at = hub.position + car.transform.up * 0.30f;
            Vector3 from = at + side * 2.5f + car.transform.up * 0.95f + car.transform.forward * 0.35f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            var rt = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            Texture2D tex = null;
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
            }
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return tex;
        }
    }
}
