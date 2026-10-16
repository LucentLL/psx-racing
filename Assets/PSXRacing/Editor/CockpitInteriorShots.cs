using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The owner's v08 cockpit, photographed (tools/cockpit/cockpit-shots.ps1):
    /// eight frames on one car, both drive sides (DriveSide.Override), at
    /// the owner's camera - LHD and RHD by day, the gauges close up at speed,
    /// night, rain on the glass, the look-over to the cargo each side, and
    /// the mirror. Edit mode: the cabin is ticked by hand (no frames), the
    /// mirror rendered once (CockpitInterior.PreviewMirror), the rain set as
    /// if it had been falling a while (CockpitInterior.PreviewRain).
    /// </summary>
    public static class CockpitInteriorShots
    {
        [MenuItem("PSX Racing/Capture Cockpit Interior")]
        public static void Capture()
        {
            var def = TrackCatalog.At(0);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (!string.IsNullOrEmpty(only))
                foreach (var d in TrackCatalog.All) if (d.id == only) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;

            var t = player.transform;
            var car = player.GetComponent<CarController>();
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
            Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
            var keepView = ChaseCamera.Current;
            int keepWeather = RaceHandoff.WeatherOverride;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);

            var cargo = SettledCargo(car);
            var cv = Object.FindFirstObjectByType<CockpitView>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            const float Max = CockpitLook.MaxYaw;
            // (name, rhd, hour, weather, look yaw, steer, rpm, mph, special)
            var shots = new (string, bool, int, int, float, float, float, float, string)[]
            {
                ("lhd_day_forward", false, TimeOfDay.Noon, -1, 0f, 0f, 3200f, 45f, ""),
                ("rhd_day_forward", true, TimeOfDay.Noon, -1, 0f, 0f, 3200f, 45f, ""),
                ("gauges_speed", false, TimeOfDay.Noon, -1, 0f, 0f, 6400f, 92f, "gauges"),
                ("night_forward", false, TimeOfDay.Night, -1, 0f, 0f, 3000f, 40f, ""),
                ("rain_forward", false, TimeOfDay.Noon, (int)Weather.Rain, 0f, 0f, 3400f, 34f, "rain"),
                ("lhd_look_cargo", false, TimeOfDay.Noon, -1, Max, 0f, 3200f, 45f, ""),
                ("rhd_look_cargo", true, TimeOfDay.Noon, -1, -Max, 0f, 3200f, 45f, ""),
                ("mirror", false, TimeOfDay.Noon, -1, 0f, 0f, 3200f, 45f, "mirror"),
            };
            int lastHour = -1, lastWeather = -2;
            CockpitInterior ci = null;
            foreach (var (name, rhd, hour, weather, yaw, steer, rpm, mph, special) in shots)
            {
                DriveSide.Override = rhd;
                if (globals != null && globals.sun != null && (hour != lastHour || weather != lastWeather))
                {
                    RaceHandoff.WeatherOverride = weather;
                    TimeOfDay.Apply(hour, globals.sun);
                    globals.Apply();
                    lastHour = hour; lastWeather = weather;
                }
                PSXScreenshotTool.AspectOverride = 16f / 9f;
                if (car != null) { car.currentRPM = rpm; car.speedKmh = mph * 1.609344f; }
                if (cv != null) cv.PreviewShow(true);
                if (ci == null) ci = player.GetComponentInChildren<CockpitInterior>(true);
                if (ci == null) { Debug.LogError("[Cockpit] no CockpitInterior was built on the player"); break; }
                ci.Refit();
                ci.Show(true, true);
                ci.HideShell(true);
                ci.SetSteer(steer, car != null ? car.maxSteerLowSpeedDeg : 34f);
                CockpitInterior.PreviewRain(special == "rain" ? 1f : 0f, mph * 0.44704f, 37f);
                ci.Tick(0f, cargo);
                foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                Canvas.ForceUpdateCanvases();

                Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, body != null ? body.Def : null,
                                                      body != null ? body.LengthScale : 1f, body != null ? body.BodyLift : 0f, rhd);
                Vector3 pos = t.TransformPoint(eye);
                var rot = t.rotation * Quaternion.Euler(
                    ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(yaw), yaw, 0f);
                cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, yaw * CockpitLook.PassengerSign / Max);
                var lay = CockpitInterior.L;
                if (special == "gauges" || special == "mirror")
                {
                    Vector3 aim = Vector3.zero;
                    if (special == "gauges") { foreach (var g in lay.gauges) aim += g.centre; aim /= lay.gauges.Length; }
                    else { aim = lay.mirror.centre; ci.PreviewMirror(cam); }
                    aim = ci.Model.TransformPoint(aim);
                    rot = Quaternion.LookRotation(aim - pos, t.up);
                    cam.fieldOfView = special == "gauges" ? 21f : 24f;
                }
                PSXScreenshotTool.ShotAs(cam, "cockpitv08_" + name, pos, rot);
                Debug.Log($"[Cockpit] {name}: {(rhd ? "RHD" : "LHD")} eye x {eye.x:0.000} scale {ci.Current.scale:0.000} " +
                          $"near {cam.nearClipPlane:0.0000} tach 0-{ci.Gauges?.TachMax:0} speedo 0-{ci.Gauges?.SpeedMax:0} " +
                          $"{(ci.Gauges != null && ci.Gauges.OuterMph ? "MPH" : "km/h")}");
            }
            DriveSide.Override = null;
            RaceHandoff.WeatherOverride = keepWeather;
            PSXScreenshotTool.AspectOverride = 0f;
            CockpitInterior.PreviewRain(0f, 0f, 0f);
            if (ci != null) { ci.HideShell(false); Report(ci, cargo); }
            if (cargo != null) Object.DestroyImmediate(cargo.gameObject);
            if (cv != null) cv.PreviewShow(false);
            ChaseCamera.PreviewView(keepView);
            cam.fieldOfView = keepFov;
            cam.nearClipPlane = keepNear;
            Debug.Log("[Cockpit] shots of " + def.id + " written");
        }

        /// <summary>
        /// The debug DRIVE SIDE pick, photographed (2026-10-16): the player's
        /// car specced as an EG Civic SiR-II (sold both ways; the catalog
        /// drives it from the right), shot once on its catalog side and once
        /// after a debug career's bench flips it to LHD. The flip goes the
        /// bench's own way - DebugCarOps.SetDriveSide, FillCarRequestFor,
        /// RaceHandoffApplier.ReapplyCar - and the cabin is only asked to
        /// Refit, the call CockpitView makes every frame, so the frame shows
        /// what the next frame of a drive would. No DriveSide.Override here.
        /// Writes driveside_cockpit_{rhd,lhd_flipped}.png beside the other
        /// screenshots.
        /// </summary>
        public static void CaptureDriveSide()
        {
            const string EG = "honda_civic_sir_ii__eg___91";
            var egSpec = CarCatalog.Get(EG);
            if (egSpec == null) { Debug.LogError("[DriveSideShot] no EG Civic row"); return; }
            var def = TrackCatalog.At(0);
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;
            var t = player.transform;
            var car = player.GetComponent<CarController>();
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            var applier = Object.FindFirstObjectByType<RaceHandoffApplier>();
            if (applier != null && applier.playerCar == null) applier.playerCar = car;
            var cv = Object.FindFirstObjectByType<CockpitView>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            var keepView = ChaseCamera.Current;
            float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            DriveSide.Override = null;

            var s = LifeRules.SeedNewGame("SIDES", 25, LifeRules.DefaultJobIndex);
            s.debugMode = true;
            var owned = new OwnedCar { id = "ds_eg", specId = EG, displayName = egSpec.name };
            s.cars.Add(owned);
            s.activeCar = owned.id;
            if (globals != null && globals.sun != null) { TimeOfDay.Apply(TimeOfDay.Noon, globals.sun); globals.Apply(); }
            var cargo = SettledCargo(car);
            CockpitInterior ci = null;
            try
            {
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.CarId = owned.id;
                RaceHandoff.CarSpecId = EG;
                foreach (var (name, flip) in new[] { ("rhd", false), ("lhd_flipped", true) })
                {
                    if (flip) DebugCarOps.SetDriveSide(s, owned, false);
                    LifeHomeScreen.FillCarRequestFor(s, owned);
                    if (applier != null) applier.ReapplyCar();
                    else { car.ApplySpec(egSpec, new CarTune.Stages()); DriveSide.Pick(car, RaceHandoff.DriveSidePick); }

                    PSXScreenshotTool.AspectOverride = 16f / 9f;
                    car.currentRPM = 3200f; car.speedKmh = 45f * 1.609344f;
                    if (cv != null) cv.PreviewShow(true);
                    if (ci == null) ci = player.GetComponentInChildren<CockpitInterior>(true);
                    if (ci == null) { Debug.LogError("[DriveSideShot] no CockpitInterior was built on the player"); break; }
                    ci.Refit();
                    ci.Show(true, true);
                    ci.HideShell(true);
                    ci.SetSteer(0f, car.maxSteerLowSpeedDeg);
                    ci.Tick(0f, cargo);
                    foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                    Canvas.ForceUpdateCanvases();

                    bool rhd = DriveSide.IsRHD(car);
                    Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
                    Vector3 sz = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
                    Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, sz, body != null ? body.Def : null,
                                                          body != null ? body.LengthScale : 1f, body != null ? body.BodyLift : 0f, rhd);
                    var rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
                    cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                    cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, 0f);
                    PSXScreenshotTool.ShotAs(cam, "driveside_cockpit_" + name, t.TransformPoint(eye), rot);
                    Debug.Log($"[DriveSideShot] {name}: pick '{RaceHandoff.DriveSidePick}' -> IsRHD {rhd}, cabin {(ci.RHD ? "RHD" : "LHD")}, " +
                              $"eye x {eye.x:0.000}, look-over sign {CockpitLook.PassengerSign:+0;-0}");
                }
            }
            finally
            {
                DriveSide.Pick(car, null);
                RaceHandoff.ClearAll();
                PSXScreenshotTool.AspectOverride = 0f;
                if (ci != null) ci.HideShell(false);
                if (cargo != null) Object.DestroyImmediate(cargo.gameObject);
                if (cv != null) cv.PreviewShow(false);
                ChaseCamera.PreviewView(keepView);
                cam.fieldOfView = keepFov;
                cam.nearClipPlane = keepNear;
            }
        }

        /// <summary>
        /// The windscreen's reflection of the cabin (CockpitReflection),
        /// photographed by tools/cockpit/cockpit-shots.ps1 -Reflect: noon at
        /// the spot along the venue where the view through the glass is
        /// DARKEST (trees, shade) without and with it, noon where it is
        /// BRIGHTEST (sky), night, and RHD. Writes wsr_*.png; logs the
        /// render's draws, tris, size and time ([Reflect] lines) and the
        /// self-test's line.
        /// </summary>
        public static void CaptureReflection()
        {
            string want = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (string.IsNullOrEmpty(want)) want = "BlueRidge";
            var def = TrackCatalog.At(0);
            foreach (var d in TrackCatalog.All) if (d.id == want) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;
            var t = player.transform;
            var car = player.GetComponent<CarController>();
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            var path = Object.FindFirstObjectByType<TrackPath>();
            var cv = Object.FindFirstObjectByType<CockpitView>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
            var keepView = ChaseCamera.Current;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            Debug.Log("[Reflect] self-test line: " + (CockpitReflection.SelfCheck(out string why) ? "PASS " : "FAIL ") + why);
            CockpitInterior ci = null;
            float lift = path != null ? t.position.y - path.GetPoint(path.NearestIndex(t.position)).y : 0f;
            int hourNow = -1;
            void Hour(int h)
            {
                if (h == hourNow || globals == null || globals.sun == null) return;
                TimeOfDay.Apply(h, globals.sun);
                globals.Apply();
                hourNow = h;
            }
            void PlaceAt(int i)
            {
                if (path == null) return;
                Vector3 at = path.GetPoint(i);
                Vector3 fwd = path.GetPoint(i + 1) - path.GetPoint(i - 1); fwd.y = 0f; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                t.SetPositionAndRotation(at + right * (path.roadWidth * 0.25f) + Vector3.up * lift, Quaternion.LookRotation(fwd, Vector3.up));
                Physics.SyncTransforms();
            }
            bool Pose(bool rhd, bool cabin, out Vector3 pos, out Quaternion rot)
            {
                DriveSide.Override = rhd;
                pos = Vector3.zero; rot = Quaternion.identity;
                if (car != null) { car.currentRPM = 3000f; car.speedKmh = 60f; }
                if (cv != null) cv.PreviewShow(true);
                if (ci == null) ci = player.GetComponentInChildren<CockpitInterior>(true);
                if (ci == null) { Debug.LogError("[Reflect] no CockpitInterior was built on the player"); return false; }
                ci.Refit();
                ci.Show(cabin, cabin);
                ci.HideShell(true);
                ci.SetSteer(0f, car != null ? car.maxSteerLowSpeedDeg : 34f);
                CockpitInterior.PreviewRain(0f, 0f, 0f);
                ci.Tick(0f, null);
                Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
                Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
                Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, body != null ? body.Def : null,
                                                      body != null ? body.LengthScale : 1f, body != null ? body.BodyLift : 0f, rhd);
                pos = t.TransformPoint(eye);
                rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
                cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, 0f);
                return true;
            }

            try
            {
                // The darkest and brightest view through the glass by day,
                // along the whole venue: the band of the frame the screen
                // fills, measured with the cabin hidden.
                Hour(TimeOfDay.Noon);
                int dark = -1, bright = -1;
                float lumDark = float.MaxValue, lumBright = float.MinValue;
                if (path != null)
                {
                    var probe = new RenderTexture(160, 90, 24, RenderTextureFormat.ARGB32);
                    var read = new Texture2D(160, 90, TextureFormat.RGBA32, false);
                    for (int k = 1; k < 40; k++)
                    {
                        int i = Mathf.Clamp(Mathf.RoundToInt(k / 40f * (path.Count - 1)), 2, path.Count - 3);
                        PlaceAt(i);
                        if (!Pose(false, false, out var pos, out var rot)) break;
                        cam.transform.SetPositionAndRotation(pos, rot);
                        var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = probe };
                        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                        RenderTexture.active = probe;
                        read.ReadPixels(new Rect(0, 0, 160, 90), 0, 0);
                        RenderTexture.active = null;
                        float sum = 0f; int n = 0;
                        for (int y = 43; y < 67; y++)
                            for (int x = 48; x < 112; x++) { var px = read.GetPixel(x, y); sum += 0.2126f * px.r + 0.7152f * px.g + 0.0722f * px.b; n++; }
                        float lum = sum / Mathf.Max(1, n);
                        if (lum < lumDark) { lumDark = lum; dark = i; }
                        if (lum > lumBright) { lumBright = lum; bright = i; }
                    }
                    probe.Release();
                    Object.DestroyImmediate(probe);
                    Object.DestroyImmediate(read);
                }
                Debug.Log($"[Reflect] {def.id}: darkest view through the glass at wp {dark} (luma {lumDark:0.000}), brightest at wp {bright} (luma {lumBright:0.000})");

                // (name, spot, rhd, hour, reflection on)
                var shots = new (string, int, bool, int, bool)[]
                {
                    ("day_trees_before", dark, false, TimeOfDay.Noon, false),
                    ("day_trees_after", dark, false, TimeOfDay.Noon, true),
                    ("day_sky_after", bright, false, TimeOfDay.Noon, true),
                    ("night_after", dark, false, TimeOfDay.Night, true),
                    ("rhd_day_trees_after", dark, true, TimeOfDay.Noon, true),
                };
                foreach (var (name, spot, rhd, hour, on) in shots)
                {
                    if (spot >= 0) PlaceAt(spot);
                    Hour(hour);
                    PSXScreenshotTool.AspectOverride = 16f / 9f;
                    if (!Pose(rhd, true, out var pos, out var rot)) break;
                    CockpitReflection.Override = on;
                    // One throwaway frame first: the cabin's lighting reads
                    // what the last camera left (its car's shadow map, the
                    // lamp table, the exposure) - in play that is the last
                    // frame at this spot, here it would be the last SHOT.
                    cam.transform.SetPositionAndRotation(pos, rot);
                    StreetLights.Push(pos, rot * Vector3.forward);
                    var warm = RenderTexture.GetTemporary(160, 90, 24);
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam,
                        new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = warm });
                    RenderTexture.ReleaseTemporary(warm);
                    ci.PreviewReflection();
                    PSXScreenshotTool.ShotAs(cam, "wsr_" + name, pos, rot);
                    var r = ci.Reflection;
                    if (on && r != null && r.Texture != null)
                    {
                        // The reflection itself, for judging (not a frame of the game).
                        var rt = r.Texture;
                        void Dump(string tag)
                        {
                            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                            RenderTexture.active = rt;
                            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                            RenderTexture.active = null;
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine("Screenshots", "reflrt_" + tag + ".png"), tex.EncodeToPNG());
                            Object.DestroyImmediate(tex);
                        }
                        Dump(name);
                    }
                    Debug.Log($"[Reflect] {name}: {(rhd ? "RHD" : "LHD")} wp {spot} on {on} active {r != null && r.Active} " +
                              (r != null && r.Texture != null ? $"rt {r.Texture.width}x{r.Texture.height} draws {r.LastDraws} tris {r.LastTris} rake {r.RakeDeg:0.0} deg" : "no reflection"));
                }

                // The cost: forced renders, then one read back to wait for the GPU.
                var rf = ci != null ? ci.Reflection : null;
                if (rf != null && rf.Texture != null)
                {
                    CockpitReflection.Override = true;
                    Hour(TimeOfDay.Noon);
                    var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    void Sync() { RenderTexture.active = rf.Texture; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); one.Apply(); RenderTexture.active = null; }
                    Sync();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int k = 0; k < 10; k++) Sync();
                    double syncMs = sw.Elapsed.TotalMilliseconds / 10.0;
                    const int N = 60;
                    sw.Restart();
                    for (int k = 0; k < N; k++) ci.PreviewReflection();
                    Sync();
                    double each = (sw.Elapsed.TotalMilliseconds - syncMs) / N;
                    Debug.Log($"[Reflect] cost: {N} renders {each:0.000} ms each (CPU record + submit + GPU, editor {SystemInfo.graphicsDeviceType} on {SystemInfo.graphicsDeviceName}); " +
                              $"{rf.LastDraws} draws, {rf.LastTris} tris, {rf.Texture.width}x{rf.Texture.height}, every {CockpitReflection.Every} frames");
                    Object.DestroyImmediate(one);
                }
            }
            finally
            {
                CockpitReflection.Override = null;
                DriveSide.Override = null;
                PSXScreenshotTool.AspectOverride = 0f;
                if (ci != null) ci.HideShell(false);
                if (cv != null) cv.PreviewShow(false);
                ChaseCamera.PreviewView(keepView);
                cam.fieldOfView = keepFov;
                cam.nearClipPlane = keepNear;
            }
            Debug.Log("[Reflect] shots of " + def.id + " written");
        }

        static PizzaCargo SettledCargo(CarController car)
        {
            var cargo = PizzaCargo.Spawn(car, new[] { 0, 3, 6 }, 2, seatStage: 0);
            if (cargo == null || cargo.BoxCount == 0) { Debug.LogError("[Cockpit] the cargo did not build"); return cargo; }
            var held = new List<Rigidbody>();
            foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                if (!rb.isKinematic && !rb.transform.IsChildOf(cargo.transform)) { rb.isKinematic = true; held.Add(rb); }
            var mode = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                const float dt = 1f / 60f;
                for (int i = 0; i < 120; i++)
                {
                    cargo.Tick(Vector3.zero, Quaternion.identity, dt);
                    Physics.Simulate(dt);
                }
            }
            finally
            {
                Physics.simulationMode = mode;
                foreach (var rb in held) if (rb != null) rb.isKinematic = false;
            }
            Debug.Log($"[Cockpit] cargo settled: {cargo.BoxCount} boxes, condition {cargo.Condition:0.000}");
            return cargo;
        }

        /// <summary>The fit, the draw cost, the textures, and the rest gap
        /// under the bottom box on the owner's v08 seat.</summary>
        static void Report(CockpitInterior ci, PizzaCargo cargo)
        {
            var f = ci.Current;
            Debug.Log($"[Cockpit] fit eye {f.eye.x:0.000},{f.eye.y:0.000},{f.eye.z:0.000} scale {f.scale:0.000} " +
                      $"measured {f.measured} rhd {f.rhd} near {CockpitInterior.ActiveNearClip:0.0000}");
            int tris = 0, draws = 0;
            var cargoRoot = ci.transform.Find("CargoMirror");
            foreach (var mf in ci.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null || (cargoRoot != null && mf.transform.IsChildOf(cargoRoot))) continue;
                for (int sm = 0; sm < m.subMeshCount; sm++) tris += (int)m.GetIndexCount(sm) / 3;
                draws += m.subMeshCount;
            }
            long texBytes = 0;
            var texs = new System.Text.StringBuilder();
            foreach (var n in new[] { "v08_vinyl", "v08_abs", "v08_headliner", "v08_leather", "v08_atlas", "hands_skin" })
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CockpitModel.EditorSrc + n + ".png");
                if (tex == null) { texs.Append(" " + n + "=MISSING"); continue; }
                long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                texBytes += b;
                texs.Append($" {n}={tex.width}px {tex.format} {b / 1024}KiB");
            }
            var geo = AssetDatabase.LoadAssetAtPath<TextAsset>(CockpitModel.EditorSrc + CockpitModel.GeoName + ".bytes");
            Debug.Log($"[Cockpit] cabin: {tris} tris in {draws} draws (cargo copies not: {(ci.Mirror != null ? ci.Mirror.Count : 0)}); " +
                      $"geometry {(geo != null ? geo.bytes.Length / 1024 : -1)} KiB; textures{texs} (sum {texBytes / 1024} KiB in the editor)");

            if (cargo == null || cargo.BoxCount == 0 || ci.Mirror == null || cargoRoot == null) return;
            var parts = cargo.ReplayParts();
            Transform box0 = parts.Count > 1 ? parts[1] : null;
            if (box0 == null) return;
            var tray = cargo.Tray;
            var seatQ = tray.rotation * cargo.StackSeatRotLocal;
            float island = (Quaternion.Inverse(seatQ) * (box0.position - tray.TransformPoint(cargo.StackSeatLocal))).y;
            var cabin = ci.Model.Find("cabin");
            var mf0 = cabin != null ? cabin.GetComponent<MeshFilter>() : null;
            if (mf0 == null || mf0.sharedMesh == null || !mf0.sharedMesh.isReadable) return;
            var toMesh = mf0.transform.worldToLocalMatrix * cargoRoot.localToWorldMatrix;
            var vs = mf0.sharedMesh.vertices;
            var ts = mf0.sharedMesh.triangles;
            var gaps = new List<string>();
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var (u, v) in new[] { (0f, 0f), (-0.18f, -0.18f), (0.18f, -0.18f), (-0.18f, 0.18f), (0.18f, 0.18f) })
            {
                Vector3 isl = box0.position + seatQ * new Vector3(u, 0f, v);
                Vector3 q = toMesh.MultiplyPoint3x4(ci.Mirror.MapSeat(isl));
                float best = float.NaN;
                for (int k = 0; k < ts.Length; k += 3)
                {
                    Vector3 a = vs[ts[k]], b = vs[ts[k + 1]], cc = vs[ts[k + 2]];
                    float d = (b.z - cc.z) * (a.x - cc.x) + (cc.x - b.x) * (a.z - cc.z);
                    if (Mathf.Abs(d) < 1e-9f) continue;
                    float l1 = ((b.z - cc.z) * (q.x - cc.x) + (cc.x - b.x) * (q.z - cc.z)) / d;
                    float l2 = ((cc.z - a.z) * (q.x - cc.x) + (a.x - cc.x) * (q.z - cc.z)) / d;
                    float l3 = 1f - l1 - l2;
                    if (l1 < 0f || l2 < 0f || l3 < 0f) continue;
                    float y = l1 * a.y + l2 * b.y + l3 * cc.y;
                    if (y > q.y + 0.10f) continue;
                    if (float.IsNaN(best) || y > best) best = y;
                }
                if (float.IsNaN(best)) { gaps.Add("miss"); continue; }
                float g = q.y - best;
                lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
                gaps.Add((g * 100f).ToString("0.0"));
            }
            Debug.Log($"[Cockpit] rest gap, bottom box ({(f.rhd ? "RHD" : "LHD")}): island {island * 100f:0.0} cm off its pan; cabin (centre, 4 corners) " +
                      string.Join(" / ", gaps) + $" cm (min {lo * 100f:0.0}, max {hi * 100f:0.0}; negative = sunk)");
        }
    }
}
