using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Renders verification screenshots of the built scenes from a few angles
    /// without entering play mode. Triggered by "psx_screenshot.flag" at the
    /// project root, or via menu PSX Racing > Capture Screenshots.
    ///
    /// It now sweeps every circuit and, on one of them, every hour. Both of
    /// those additions fail SILENTLY when they fail — a barrier textured with
    /// the wrong JPEG, a circuit whose ground plane does not reach its own back
    /// straight, a sky gradient that turns the horizon to mud at dusk — and
    /// none of them throw. A contact sheet is the only test that catches them.
    /// </summary>
    [InitializeOnLoad]
    public static class PSXScreenshotTool
    {
        static string RootDir => Directory.GetParent(Application.dataPath).FullName;
        static string FlagPath => Path.Combine(RootDir, "psx_screenshot.flag");
        static string OutDir => Path.Combine(RootDir, "Screenshots");

        static PSXScreenshotTool()
        {
            if (File.Exists(FlagPath))
                EditorApplication.delayCall += TryCapture;
        }

        static void TryCapture()
        {
            if (!File.Exists(FlagPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryCapture;
                return;
            }
            File.Delete(FlagPath);
            Capture();
        }

        [MenuItem("PSX Racing/Capture Screenshots")]
        public static void Capture()
        {
            Directory.CreateDirectory(OutDir);
            foreach (var def in TrackCatalog.All) CaptureTrack(def);
            // The hour sweep and the camera sweep both go on the city circuit:
            // it is the one with buildings, trees, parked cars and a forecourt
            // all in shot, so it shows what an hour does to every kind of
            // surface at once.
            CaptureHours(TrackCatalog.At(0));
            CaptureCameras(TrackCatalog.At(0));
            CaptureHoodCams(TrackCatalog.At(0));
            // The forecourt is a PLACE now — a hole in the barrier, an apron on
            // the road layer and four pump volumes — and every one of those
            // fails silently. A car that cannot get in, an apron floating over a
            // hillside, a station facing its own back wall at the road: all of
            // them build without a word and all of them are obvious in a
            // picture.
            CaptureFuelStop(TrackCatalog.At(0));
            CaptureGarage();
            Debug.Log("[PSXShot] Screenshots written to " + OutDir);
        }

        /// <summary>
        /// The garage on its own. The full pass opens six circuits, sweeps
        /// seven hours and six cameras across one of them, and takes minutes;
        /// iterating on a fixture that lives in one room should not have to pay
        /// for any of that.
        /// </summary>
        [MenuItem("PSX Racing/Capture Garage")]
        public static void CaptureGarageOnly()
        {
            Directory.CreateDirectory(OutDir);
            CaptureGarage();
            Debug.Log("[PSXShot] Garage shots written to " + OutDir);
        }

        /// <summary>
        /// The seven driving views on one circuit, and nothing else.
        ///
        /// The same argument as the garage pass: the cabin overlay is a dozen
        /// fractions of the frame that can ONLY be judged by looking at the
        /// picture, and iterating on them should not cost six circuits and a
        /// seven-hour sweep per attempt.
        /// </summary>
        [MenuItem("PSX Racing/Capture Camera Views")]
        public static void CaptureCamerasOnly()
        {
            Directory.CreateDirectory(OutDir);
            CaptureCameras(TrackCatalog.At(0));
            Debug.Log("[PSXShot] Camera-view shots written to " + OutDir);
        }

        // ------------------------------------------------------------------
        //  One pass per circuit
        // ------------------------------------------------------------------
        static void CaptureTrack(TrackCatalog.TrackDef def)
        {
            if (!Open(def, out var cam, out var player)) return;
            string tag = def.id;
            var t = player.transform;

            Vector3 eye = t.position - t.forward * 5.4f + Vector3.up * 1.9f;
            Shot(cam, tag + "_1_chase", eye,
                Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 2f - eye));

            Vector3 grid = t.TransformPoint(new Vector3(3.6f, 2.0f, 5.5f));
            Shot(cam, tag + "_2_grid34", grid,
                Quaternion.LookRotation(t.position + Vector3.up * 0.6f - grid));

            // Overview framed off the circuit's own bounds rather than a literal
            // — a fixed (40, 150, 60) was the city's middle and is 300 m off the
            // road on the airfield.
            var b = TrackCatalog.BoundsOf(def);
            float span = Mathf.Max(b.size.x, b.size.z);
            Vector3 high = b.center + new Vector3(0f, span * 0.55f, -span * 0.55f);
            // The far plane is 360 m and the fog closes at ~265, so an overhead
            // shot of a 660 m circuit is a photograph of fog — which is what the
            // first one came back as. Push both out for this frame only; the
            // point of the overview is the SHAPE, and the shape is the one thing
            // the in-car views can never show.
            float keepFar = cam.farClipPlane;
            float keepNear = Shader.GetGlobalFloat("_PSXFogNear");
            float keepFogFar = Shader.GetGlobalFloat("_PSXFogFar");
            cam.farClipPlane = span * 3f;
            Shader.SetGlobalFloat("_PSXFogNear", span * 2f);
            Shader.SetGlobalFloat("_PSXFogFar", span * 3f);
            Shot(cam, tag + "_3_overview", high, Quaternion.LookRotation(b.center - high));
            cam.farClipPlane = keepFar;
            Shader.SetGlobalFloat("_PSXFogNear", keepNear);
            Shader.SetGlobalFloat("_PSXFogFar", keepFogFar);

            // Kerbside: framed off an actual piece of scenery rather than a
            // literal, because the scenery pass places them from the waypoint
            // list and a hard-coded position goes stale the next time the track
            // changes shape.
            var scenery = GameObject.Find("Track/Scenery")?.transform;
            Transform prop = null;
            if (scenery != null)
                foreach (Transform child in scenery)
                    if (child.name.StartsWith("Parked_") || child.name == "Building") { prop = child; break; }
            if (prop != null)
            {
                Vector3 kerb = prop.position + prop.right * 9f + Vector3.up * 3.2f;
                Shot(cam, tag + "_4_kerbside", kerb,
                    Quaternion.LookRotation(prop.position + Vector3.up * 0.7f - kerb));
            }

            CaptureShoulder(def, cam, tag);
            CaptureBridge(def, cam, tag);
        }

        /// <summary>
        /// The SHOULDER, from a driver's eye, a third and two thirds of the way
        /// along.
        ///
        /// Every other frame here is taken at the start line, which on a stage
        /// is a graded pad with a start apron — the one place the shoulder is
        /// supposed to be wide. "Most of the blue ridge does not have big runoff
        /// areas to drive onto" was reported against a road every existing shot
        /// photographed at its widest point. Two frames out on the route, aimed
        /// slightly down and to the side, are what shows whether the guard wall,
        /// the cut bank and the falling verge are where they should be.
        /// </summary>
        static void CaptureShoulder(TrackCatalog.TrackDef def, Camera cam, string tag)
        {
            if (!def.stage) return;
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count < 12) return;

            foreach (float f in new[] { 0.34f, 0.68f })
            {
                int i = Mathf.Clamp(Mathf.RoundToInt(path.Count * f), 1, path.Count - 3);
                Vector3 here = path.GetPoint(i);
                Vector3 fwd = (path.GetPoint(i + 2) - path.GetPoint(i)).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                // Off the centreline and a driver's height up, looking down the
                // road and 22 degrees off it: a shoulder photographed head-on is
                // a stripe two pixels tall.
                Vector3 eye = here + Vector3.up * 1.35f - right * 2.2f;
                Vector3 aim = here + fwd * 26f + right * 9f - Vector3.up * 1.2f;
                Shot(cam, tag + "_9_shoulder_" + Mathf.RoundToInt(f * 100f),
                     eye, Quaternion.LookRotation(aim - eye));
            }
        }

        /// <summary>
        /// A bridge from BESIDE and BELOW, which is the only place any of it is
        /// visible. Everything a deck can get wrong — a soffit facing the wrong
        /// way, piers standing on the wrong ground, a span that stops short of
        /// its own abutment — looks completely correct from the driving line,
        /// because from there a bridge is just road.
        /// </summary>
        static void CaptureBridge(TrackCatalog.TrackDef def, Camera cam, string tag)
        {
            if (def.bridges == null || def.bridges.Length == 0) return;
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count == 0) return;

            var span = def.bridges[0];
            float lap = Mathf.Max(def.LengthM, 1f);
            float mid = Mathf.Repeat(span.x + Mathf.Repeat(span.y - span.x, lap) * 0.5f, lap);
            int i = path.Wrap(Mathf.RoundToInt(mid / path.spacing));
            Vector3 deck = path.GetPoint(i);
            Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;

            // INSIDE the gorge, not out on the hillside beyond it. The floor is
            // flat for CorridorR either side of the centreline and climbs back
            // to field height over the next fifty; standing off by half the span
            // put the lens inside the hill, looking up at the underside of a
            // terrain mesh — a picture of an orange sky with a bridge in it.
            float len = Mathf.Repeat(span.y - span.x, lap);
            Vector3 eye = deck + right * 26f
                        - Vector3.up * (def.bridgeDepth * 0.55f)
                        - path.GetTangent(i) * (len * 0.32f);
            Shot(cam, tag + "_5_bridge", eye,
                 Quaternion.LookRotation(deck - Vector3.up * (def.bridgeDepth * 0.25f) - eye));
        }

        // ------------------------------------------------------------------
        //  The fuel stop
        // ------------------------------------------------------------------
        /// <summary>
        /// The forecourt from the three places that can prove it works: the
        /// approach down the road (is there a way IN?), a car's eye view at the
        /// pumps (is the tarmac under the wheels and the station facing the
        /// right way?), and from above (does the apron meet the road, or is it
        /// a slab hanging off a hillside?).
        /// </summary>
        static void CaptureFuelStop(TrackCatalog.TrackDef def)
        {
            if (!Open(def, out var cam, out _)) return;

            var scenery = GameObject.Find("Track/Scenery")?.transform;
            if (scenery == null) { Debug.LogWarning("[PSXShot] no scenery root"); return; }

            Transform station = scenery.Find("GasStation");
            Transform pump = null, apron = scenery.Find("Forecourt");
            foreach (Transform child in scenery)
                if (child.name == "Pump") { pump = child; break; }
            if (station == null || apron == null)
            {
                Debug.LogWarning("[PSXShot] no forecourt on " + def.id);
                return;
            }

            Vector3 target = pump != null ? pump.position : station.position;
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null) return;

            int i = path.NearestIndex(target);
            Vector3 road = path.GetPoint(i);
            Vector3 tangent = path.GetTangent(i);

            // Coming up on it, from the racing line. High enough to see over
            // the barrier: at driver height the approach is a photograph of a
            // wall, which says nothing about whether there is a way through it.
            Vector3 eye = road - tangent * 30f + Vector3.up * 5f;
            Shot(cam, def.id + "_6_pumps_approach", eye,
                 Quaternion.LookRotation(target + Vector3.up * 1.2f - eye));

            // Parked at the nozzle, at driver height.
            Vector3 bay = target + (road - target).normalized * 7f + Vector3.up * 1.5f;
            Shot(cam, def.id + "_7_pumps_bay", bay,
                 Quaternion.LookRotation(target + Vector3.up * 0.8f - bay));

            // The whole stop from above: apron, opening, station, road.
            Vector3 high = Vector3.Lerp(road, target, 0.5f) + Vector3.up * 42f - tangent * 26f;
            Shot(cam, def.id + "_8_pumps_plan", high,
                 Quaternion.LookRotation(Vector3.Lerp(road, target, 0.55f) - high));
        }

        // ------------------------------------------------------------------
        //  The walk-in garage
        // ------------------------------------------------------------------
        /// <summary>
        /// Five shots of a room whose contents do not exist until something
        /// calls Start. GarageWorld.PreviewBuild is what makes this possible at
        /// all — without it every one of these would be a photograph of an
        /// empty concrete box, which is exactly what "the garage looks fine"
        /// would then mean.
        /// </summary>
        static void CaptureGarage()
        {
            const string path = "Assets/PSXRacing/Scenes/Garage.unity";
            if (!File.Exists(path)) { Debug.LogWarning("[PSXShot] no garage scene"); return; }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            var cam = GameObject.Find("PSXCamera")?.GetComponent<Camera>();
            var world = Object.FindFirstObjectByType<PSXRacing.OnFoot.GarageWorld>();
            if (cam == null || world == null)
            {
                Debug.LogError("[PSXShot] garage scene is missing its camera or its world");
                return;
            }

            SeedDemoCareer();

            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            world.PreviewBuild();

            // Standing inside the door, which is where a player arrives.
            Shot(cam, "garage_1_entry", new Vector3(0f, 1.62f, -6.4f), Quaternion.Euler(3f, 0f, 0f));
            // Down the row of bays from the corner.
            Shot(cam, "garage_2_bays", new Vector3(-9.4f, 1.62f, -1.2f), Quaternion.Euler(3f, 38f, 0f));
            // The parts rack, from where you would stand to read it.
            Shot(cam, "garage_3_rack", new Vector3(-7.2f, 1.62f, 0f), Quaternion.Euler(6f, -90f, 0f));
            // Tool board and bench on the opposite wall.
            Shot(cam, "garage_4_bench", new Vector3(7.4f, 1.62f, 0f), Quaternion.Euler(4f, 90f, 0f));
            // Under the strip lights looking down the room, so the plan reads.
            Shot(cam, "garage_5_wide", new Vector3(0f, 4.1f, -7.4f), Quaternion.Euler(26f, 0f, 0f));

            // The two shots the raise gear exists to be checked by. Bays run
            // along X at 4.2 m spacing from -8.4, so the second car is at -4.2
            // and the third at 0 — the demo career leaves those two up.
            //
            // Standing beside a car on stands, and then UNDER the one on the
            // lift. Every way this feature fails is visible only from here: a
            // car floating over stands that are too short, a lift arm through a
            // sill, or a raise so low that the head-height camera is inside the
            // floorpan.
            Vector3 standsEye = new Vector3(-6.5f, 1.62f, 1.9f);
            Shot(cam, "garage_6_stands", standsEye,
                 Quaternion.LookRotation(new Vector3(-4.2f, 0.7f, 4.4f) - standsEye));
            Vector3 underEye = new Vector3(0f, 1.55f, 6.6f);
            Shot(cam, "garage_7_underlift", underEye,
                 Quaternion.LookRotation(new Vector3(0f, 2.0f, 3.4f) - underEye));

            LifeSimManager.DeleteSave();
        }

        /// <summary>
        /// A career with enough in it that the garage has something to show:
        /// three cars, a stocked tool board and parts bought. A new save has one
        /// car, a floor jack and nothing on the rack, which photographs as a
        /// room with a bug in it rather than as an empty one.
        /// </summary>
        static void SeedDemoCareer()
        {
            LifeSimManager.DeleteSave();
            LifeSimManager.StartNewGame("TEST", 27, 3);
            LifeRules.SeedFallbackCar(LifeSimManager.State);
            var s = LifeSimManager.State;
            s.money = 9400;
            s.garageSlots = 4;

            if (CarCatalog.Ready && CarCatalog.All.Count > 3)
            {
                CarMarket.MakeOwnedCar(s, CarCatalog.All[1], 84, 62000f, 7800);
                CarMarket.MakeOwnedCar(s, CarCatalog.All[2], 61, 118000f, 3200);
            }

            var car = s.ActiveCar;
            if (car != null)
            {
                car.engine = 74f; car.tires = 52f; car.carHP = 66f; car.fuel = 38f;
                car.upPower = 2; car.upTires = 1; car.upSuspension = 1;
            }

            Toolbox.Buy(s, Toolbox.Lamp);
            Toolbox.Buy(s, Toolbox.Impact);
            Toolbox.Buy(s, Toolbox.Scope);

            // A lift, and two of the three cars up in the air — one on stands
            // and one on the lift. The raise gear is spawned from the SAVE at
            // PreviewBuild time, so a demo career with every car sat on the
            // floor photographs a room where none of it exists, which is
            // indistinguishable from a room where it is broken.
            s.tools.Add(Toolbox.Lift);
            if (s.cars.Count > 1) Toolbox.SetRaise(s, s.cars[1], Toolbox.Raise.Stands);
            if (s.cars.Count > 2) Toolbox.SetRaise(s, s.cars[2], Toolbox.Raise.Lift);
            LifeSimManager.Save();
        }

        // ------------------------------------------------------------------
        //  Every hour, on one circuit
        // ------------------------------------------------------------------
        static void CaptureHours(TrackCatalog.TrackDef def)
        {
            if (!Open(def, out var cam, out var player)) return;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var t = player.transform;

            // Nothing on a car is built until Start runs, so outside play mode
            // the grid has no lamps at all. Build them here or the whole point
            // of a night shot is missing from it.
            foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsSortMode.None))
                lights.PreviewBuild(false);

            for (int h = 0; h < TimeOfDay.Count; h++)
            {
                var hour = TimeOfDay.At(h);
                TimeOfDay.Apply(h, sun);
                // PSXGlobals pushes to the shaders from Update, which does not
                // run outside play mode — so push it by hand.
                var globals = Object.FindFirstObjectByType<PSXGlobals>();
                if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                // Same story for the toggles the hour drives: their components
                // are asleep in edit mode, so drive the renderers directly.
                SetNightGlow(hour.lightsOn);
                foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsSortMode.None))
                    lights.PreviewBuild(hour.lightsOn);
                // The cluster is one of the things the hour drives: white
                // printed dials by day, the chosen bulb once the street lights
                // are on. In play it rebuilds itself the frame the hour turns
                // over; here nothing calls Update, so every hour in the sweep
                // came back wearing the palette of whatever hour the scene was
                // baked at.
                foreach (var c in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None))
                    c.Build();
                foreach (var hud in Object.FindObjectsByType<RaceHUD>(FindObjectsSortMode.None))
                    HudOnTop.Apply(hud.gameObject);

                Vector3 eye = t.position - t.forward * 8f + Vector3.up * 2.6f;
                Shot(cam, "hour_" + h + "_" + hour.name.ToLower(), eye,
                    Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 20f - eye));

                // From in front, once the lamps are lit. Whether a headlight
                // quad ends up buried in the bodywork, or its pool lands under
                // the car instead of down the road, is only visible from here.
                if (!hour.lightsOn) continue;
                Vector3 front = t.position + t.forward * 11f + Vector3.up * 1.7f;
                Shot(cam, "hour_" + h + "_" + hour.name.ToLower() + "_front", front,
                    Quaternion.LookRotation(t.position + Vector3.up * 0.7f - front));
            }
        }

        // ------------------------------------------------------------------
        //  Every camera view
        // ------------------------------------------------------------------
        /// <summary>
        /// One frame from each of the six driving views, framed through
        /// ChaseCamera's own offset and FOV tables rather than through a copy
        /// of them. A mounted camera can only be wrong visually — a lens inside
        /// the windscreen, a bumper cam clipping through its own nose, a bonnet
        /// that fills two thirds of the frame — and none of it throws.
        ///
        /// The chase views are reconstructed geometrically instead of by
        /// running the follow code: that code lerps by Time.deltaTime, which is
        /// zero outside play mode, so the rig would never leave the car.
        /// </summary>
        static void CaptureCameras(TrackCatalog.TrackDef def)
        {
            if (!Open(def, out var cam, out var player)) return;
            var box = player.GetComponent<BoxCollider>();
            Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
            Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            var t = player.transform;
            float keepFov = cam.fieldOfView;
            float keepNear = cam.nearClipPlane;
            const float baseFOV = 58f;

            Vector3 fwd = t.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            float fit = Mathf.Clamp(s.z / 4.1f, 0.9f, 1.3f);

            var keepView = ChaseCamera.Current;
            foreach (ChaseCamera.View v in System.Enum.GetValues(typeof(ChaseCamera.View)))
            {
                // Two things now decide what to draw from which view is
                // CURRENT rather than from being handed one: the cabin overlay,
                // which only exists in the cockpit, and the binnacle, which is
                // one big rev counter there and two dials everywhere else.
                // Neither runs its own Update in edit mode, so both are driven
                // by hand here — otherwise the cockpit shot is the only one of
                // the seven that does not show what the game shows.
                ChaseCamera.PreviewView(v);
                foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None))
                    gc.Build();
                foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None))
                    cv.PreviewShow(v == ChaseCamera.View.Cockpit);
                foreach (var h in Object.FindObjectsByType<RaceHUD>(FindObjectsSortMode.None))
                    HudOnTop.Apply(h.gameObject);
                Canvas.ForceUpdateCanvases();

                cam.fieldOfView = ChaseCamera.ViewFOV(v, baseFOV);
                // The mounted views tighten the near plane in the game, so they
                // have to tighten it here. A reference shot taken through a
                // different near plane than the build uses is the one shot that
                // cannot show the failure it exists to catch.
                cam.nearClipPlane = ChaseCamera.ViewNearClip(v, keepNear);
                Vector3 pos;
                Quaternion rot;
                switch (v)
                {
                    case ChaseCamera.View.Chase:
                    case ChaseCamera.View.Close:
                        ChaseCamera.ChaseParams(v, out float dm, out float hm, out float lm);
                        pos = t.position - fwd * (5.4f * fit * dm) + Vector3.up * (1.8f * hm);
                        rot = Quaternion.LookRotation(
                            t.position + Vector3.up * (0.9f * lm) + fwd * 1.5f - pos, Vector3.up);
                        break;
                    case ChaseCamera.View.TopDown:
                        float h = 14f * fit;
                        pos = t.position + Vector3.up * h + fwd * (h * 0.16f);
                        rot = Quaternion.LookRotation(Vector3.down, fwd);
                        break;
                    default:
                        var shell = player.GetComponent<CarBody>();
                        pos = t.TransformPoint(ChaseCamera.MountOffset(
                            v, c, s, shell != null ? shell.Def : null));
                        rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(v), 0f, 0f);
                        break;
                }
                Shot(cam, "cam_" + (int)v + "_" + v.ToString().ToLower(), pos, rot);
            }
            ChaseCamera.PreviewView(keepView);
            cam.fieldOfView = keepFov;
            cam.nearClipPlane = keepNear;
        }

        /// <summary>
        /// The bonnet camera on four deliberately different shells.
        ///
        /// Where the cowl sits is MEASURED off each body mesh, and every way
        /// that measurement can be wrong produces a picture rather than an
        /// error: a lens inside the cabin, or one hanging out over the front
        /// bumper with no bonnet in shot at all. Two versions of the scan
        /// shipped past a numeric check and were only obviously wrong here.
        ///
        /// The five are chosen as the extremes the scan has to survive: the
        /// longest bonnet in the pack, the shortest, a cab-over with no bonnet,
        /// the reference FD, and the GTO — which is the car that was reported
        /// with the lens inside its own engine bay, so it stays in the set.
        /// </summary>
        static void CaptureHoodCams(TrackCatalog.TrackDef def)
        {
            if (!Open(def, out var cam, out var player)) return;
            var body = player.GetComponent<CarBody>();
            var box = player.GetComponent<BoxCollider>();
            if (body == null || box == null) return;

            float keepFov = cam.fieldOfView;
            float keepNear = cam.nearClipPlane;
            cam.fieldOfView = ChaseCamera.ViewFOV(ChaseCamera.View.Hood, 58f);
            cam.nearClipPlane = ChaseCamera.ViewNearClip(ChaseCamera.View.Hood, keepNear);

            foreach (string key in new[] { "daytona_69", "euro_hatch", "classic_van", "rx7_fd", "gto_66" })
            {
                var shell = CarModelLibrary.Load(key);
                if (shell == null) continue;
                body.Apply(shell, 0);

                var t = player.transform;
                Vector3 pos = t.TransformPoint(ChaseCamera.MountOffset(
                    ChaseCamera.View.Hood, box.center, box.size, shell));
                Shot(cam, "hood_" + key, pos,
                     t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Hood), 0f, 0f));
            }
            cam.fieldOfView = keepFov;
            cam.nearClipPlane = keepNear;
        }

        static void SetNightGlow(bool lit)
        {
            foreach (var ng in Object.FindObjectsByType<NightGlow>(FindObjectsSortMode.None))
                foreach (var r in ng.GetComponentsInChildren<Renderer>(true))
                    r.enabled = lit;
        }

        // ------------------------------------------------------------------
        static bool Open(TrackCatalog.TrackDef def, out Camera cam, out GameObject player)
        {
            cam = null; player = null;
            string path = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
            if (!File.Exists(path)) { Debug.LogError("[PSXShot] missing scene " + path); return false; }
            // Always reopen, even if it is already the active scene: the hour
            // sweep and the CarLights preview both leave the scene dirty, and a
            // second pass over a scene already carrying lamps would double them.
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            player = GameObject.Find("RX-7 Player");
            cam = GameObject.Find("PSXCamera")?.GetComponent<Camera>();
            if (cam == null || player == null)
            {
                Debug.LogError("[PSXShot] scene objects missing in " + def.id);
                return false;
            }
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            // The instrument cluster builds itself in Start, and Start does not
            // run outside play mode — so without this every reference shot of
            // this game comes back with the bottom third of the HUD empty, which
            // is the one part of it that is new.
            foreach (var c in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None))
                c.Build();
            foreach (var h in Object.FindObjectsByType<RaceHUD>(FindObjectsSortMode.None))
                HudOnTop.Apply(h.gameObject);
            // And the cabin, which is the same story with the opposite default:
            // it builds itself visible and only Update hides it, so every shot
            // in every sweep would be taken through a dashboard.
            foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None))
                cv.PreviewShow(false);

            // A ScreenSpaceOverlay canvas is composited onto the DISPLAY, not
            // rendered by any camera, so it does not exist as far as a
            // RenderTexture capture is concerned. The cluster moved onto one of
            // those when it left the framebuffer, and every reference shot of
            // this game would otherwise come back with no instruments in it —
            // the exact fault the Build() call above was added to fix, returning
            // by a different door. Point them at the shot camera for the
            // duration; the scene is reopened per circuit and never saved.
            //
            // CAVEAT for anyone reading the output: a cluster captured this way
            // is rasterised at the SHOT's line count, not at the device
            // resolution it actually ships at, so it will look coarser here than
            // it does on a phone. Judge the instruments from
            // `PSX Racing/Preview Touch Control Panel`, which renders them on
            // their own canvas at the reference resolution. These shots are for
            // "are they there and in the right place".
            //
            // EXCEPT the display canvas. That one exists to show the
            // framebuffer, and outside play mode PSXCameraOutput.OnEnable never
            // ran — so its RawImage has no texture and draws as flat white.
            // Pulled in front of the shot camera it painted a white rectangle
            // over two thirds of EVERY reference shot in this project, which is
            // what the pass had been quietly producing since the cluster moved
            // onto an overlay: a set of pictures of a white box.
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            var displayCanvas = output != null && output.display != null
                ? output.display.canvas : null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (c == displayCanvas) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 1f;
            }
            Canvas.ForceUpdateCanvases();
            return true;
        }

        /// <summary>
        /// The framebuffer the GAME renders into: 240 lines, width from the
        /// display. See PSXCameraOutput — the line count is the era, and the
        /// HUD canvas is ConstantPixelSize on that same camera, so HUD elements
        /// are sized in these pixels and nothing else.
        ///
        /// This used to shoot 640x480, and that is not a smaller version of the
        /// game, it is a DIFFERENT one: at 480 lines the instrument cluster
        /// covered a fifth of the frame and at the real 240 it covers two
        /// fifths. Every reference shot of the HUD was half the size it ships
        /// at, which is exactly the sort of thing reference shots exist to
        /// prevent.
        /// </summary>
        /// <summary>Read off the scene's own PSXCameraOutput, so a change to
        /// the shipped resolution moves the reference shots with it instead of
        /// leaving them a record of a version nobody plays.</summary>
        static int ShotHeight
        {
            get
            {
                var o = Object.FindFirstObjectByType<PSXCameraOutput>();
                return o != null ? Mathf.Clamp(o.height, 120, 720) : 240;
            }
        }
        static int ShotWidth => Mathf.RoundToInt(ShotHeight * 16f / 9f) & ~1;
        /// <summary>Nearest-neighbour blow-up for the PNG only, and only while
        /// the framebuffer is small enough to need it. The picture is the
        /// game's either way; point-doubling adds no information and hides
        /// none.</summary>
        static int ShotScale => ShotHeight >= 400 ? 1 : 2;

        static void Shot(Camera cam, string name, Vector3 pos, Quaternion rot)
        {
            var oldPos = cam.transform.position;
            var oldRot = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(pos, rot);

            var rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point,
            };
            rt.Create();
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                var big = PointDouble(tex);
                File.WriteAllBytes(Path.Combine(OutDir, "psx_" + name + ".png"), big.EncodeToPNG());
                if (big != tex) Object.DestroyImmediate(big);
                Object.DestroyImmediate(tex);
            }
            else Debug.LogWarning("[PSXShot] RenderRequest unsupported");

            rt.Release();
            Object.DestroyImmediate(rt);
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
        }

        static Texture2D PointDouble(Texture2D src)
        {
            if (ShotScale <= 1) return src;
            int w = src.width * ShotScale, h = src.height * ShotScale;
            var big = new Texture2D(w, h, TextureFormat.RGB24, false);
            var srcPx = src.GetPixels32();
            var dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int sy = y / ShotScale;
                for (int x = 0; x < w; x++)
                    dst[y * w + x] = srcPx[sy * src.width + x / ShotScale];
            }
            big.SetPixels32(dst);
            big.Apply();
            return big;
        }
    }
}
