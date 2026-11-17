using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Damage round 3's pictures (owner, 2026-11-17, on round 2's "left glass
    /// shattered": "I'm not a fan of the low-poly blocky interior when windows
    /// are broken. If shown, the cockpit model should be used."). ONE process
    /// per switch state: PSX_DMG_CABIN=0 (round 2's boxes) or unset (the
    /// cockpit model); PSX_D3_TAG names the folder; PSX_CAR_HD=1 the HD tier.
    /// Five shells (PSX_D3_SHELLS: the EG Civic, an FD, the '67 pony coupe, a
    /// sedan, the pickup), each a career car whose left door and tail were
    /// crumpled through the game's own bank (DamageBank via the self-test's
    /// planted crash): the left glass and the rear glass SHATTERED.
    ///   A  the CAR MENU's showroom stage (the owner's view, the real page);
    ///   B  the turntable rig, camera moved in: the left glass at 2.7 m and at
    ///      1.4 m (the alignment: sills, roof, pillars), the rear three-quarter;
    ///   C  a venue (BeechGap), the player's car in the shell: the chase camera,
    ///      a replay's trackside camera, beside the left door, and the cockpit
    ///      looking at the left glass from the seat (round 2's leftover: the side
    ///      glass now breaks there too) - day for every shell, night and rain for
    ///      the EG and the FD;
    ///   D  the seller's lot: EIGHT holed cars on screen, and the cost probe -
    ///      the same frame drawn with the cabins, with the dark cabin only
    ///      (Suspend), with round 2's boxes, with the cars' wheel rash only and
    ///      mint (the glass and rash shader paths), wall clock per frame with
    ///      read-back, interleaved, medians.
    /// Writes Screenshots/damage3/&lt;tag&gt;/ and PSXRacing_damage3_&lt;tag&gt;.txt.
    /// </summary>
    public static class Damage3Shots
    {
        static StringBuilder log;
        static string outDir;
        static readonly (string name, int w, int h)[] Size = { ("landscape_16x9", 1280, 720) };
        const string DefaultShells = "civic_eg,rx7_fd_93,pony_coupe_67,sedan_98,jdm_pickup";
        const int S = DamageModel.ZSide, R = DamageModel.ZRear;

        static void L(string s) { log.AppendLine(s); }

        public static void Capture()
        {
            log = new StringBuilder();
            string tag = System.Environment.GetEnvironmentVariable("PSX_D3_TAG");
            if (string.IsNullOrEmpty(tag)) tag = DamageCabin.On ? "cab" : "r2";
            string root = Directory.GetParent(Application.dataPath).FullName;
            outDir = Path.Combine(root, "Screenshots", "damage3", tag);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            string parts = System.Environment.GetEnvironmentVariable("PSX_D3_PARTS");
            if (string.IsNullOrEmpty(parts)) parts = "A,B,C,D";
            L($"damage3 shots: PSX_DMG_CABIN {(DamageCabin.On ? "ON" : "0")}, HD tier {CarHD.Want}, PC profile {PlatformProfile.PC}, tag {tag}, parts {parts}");
            L($"  reach {DamageCabin.ReachM} m, full mesh within {DamageCabin.FullM} m, at most {DamageCabin.MaxCabins} cabins");
            try
            {
                UnityEngine.Random.InitState(1999);
                LifeSimManager.UseScratchSave("damage3shots");
                TitleScreen.Skip = true;
                LifeSimManager.StartNewGame("CABIN", 25, LifeRules.DefaultJobIndex);
                var st = LifeSimManager.State;
                LifeRules.SeedFallbackCar(st);
                var cars = new List<(string key, OwnedCar car, CarSpec spec)>();
                string shells = System.Environment.GetEnvironmentVariable("PSX_D3_SHELLS");
                if (string.IsNullOrEmpty(shells)) shells = DefaultShells;
                int hit = 93100;
                foreach (var k0 in shells.Split(','))
                {
                    string key = k0.Trim();
                    var spec = SpecFor(key);
                    if (spec == null) { L("FAIL no catalog row drives " + key); continue; }
                    var car = CarMarket.MakeOwnedCar(st, spec, 90, 60000f, spec.price);
                    car.id = "d3_" + key;
                    st.cars.Add(car);
                    Wreck(st, car, ref hit);
                    var look = DamageLook.Read(car.faults, null);
                    L($"{key}: {spec.name} ({spec.id}, {(DriveSide.IsRHD(spec.id) ? "RHD" : "LHD")}): rows " +
                      string.Join(", ", car.faults.ConvertAll(f => f.id + " s" + f.stage)) + $" -> panes {look.panes}");
                    cars.Add((key, car, spec));
                }
                if (cars.Count > 0) st.activeCar = cars[0].car.id;
                LifeSimManager.Save();

                if (parts.Contains("A")) foreach (var c in cars) Stage(c.key, c.car);
                if (parts.Contains("B")) foreach (var c in cars) Rig(c.key, c.car, c.spec);
                if (parts.Contains("W")) Severities(st, cars);
                if (parts.Contains("C")) Venue(cars, parts.Contains("W") ? st : null);
                if (parts.Contains("D")) Lot(st, cars);
            }
            catch (System.Exception e) { L("FAIL " + e); Debug.LogError("[Damage3] " + e); }
            finally
            {
                DamageCabin.Suspend = false;
                L($"cabins built {DamageCabin.Built}, live {DamageCabin.Live}; reduced mesh ready {CockpitLod.Ready}: cabin {CockpitLod.CabinTris} tris, wheel {CockpitLod.WheelTris}, built in {CockpitLod.BuildMs:F0} ms");
                L("DAMAGE3 SHOTS DONE");
                File.WriteAllText(Path.Combine(root, "PSXRacing_damage3_" + tag + ".txt"), log.ToString());
                Debug.Log(log.ToString());
                LifeSimManager.UseCareerSave();
                TitleScreen.Skip = false;
            }
        }

        /// <summary>A catalog car this shell draws (CarModelLibrary.KeyFor).</summary>
        static CarSpec SpecFor(string key)
        {
            if (key == "civic_eg") { var eg = CarCatalog.Get("honda_civic_sir_ii__eg___91"); if (eg != null) return eg; }
            // the shell's own car first (rx7_fd is also the fallback shell of many rows)
            string hint = key.StartsWith("rx7_fd") ? "RX-7" : key == "pony_coupe_67" ? "Camaro" : null;
            CarSpec any = null;
            foreach (var s in CarCatalog.All)
            {
                if (CarModelLibrary.KeyFor(s) != key) continue;
                if (hint == null || (s.name != null && s.name.Contains(hint))) return s;
                if (any == null) any = s;
            }
            return any;
        }

        /// <summary>The left door crumpled (left glass shattered) and the tail
        /// crumpled with its bar pushed back (rear glass shattered).</summary>
        static void Wreck(LifeState st, OwnedCar car, ref int hit)
        {
            car.faults.Clear();
            LifeSimSelfTest.DvCrash(st, car, hit++, S, -1, 7f, (DamageModel.PanelId, 3, S, -1));
            LifeSimSelfTest.DvCrash(st, car, hit++, R, 0, 7.5f, (DamageModel.PanelId, 3, R, 0), (DamageModel.BarId, 2, R, 0));
        }

        // ------------------------------------------------------------------
        //  A: the car menu's stage
        // ------------------------------------------------------------------
        static void Stage(string key, OwnedCar car)
        {
            try
            {
                LifeHomePreview.Shoot(outDir, key + "_stage", "carmenu", garageCar: car.id, sizes: Size);
                L($"  A {key}: stage shot; cabins live {DamageCabin.Live}");
            }
            catch (System.Exception e) { L("FAIL stage " + key + ": " + e.Message); }
        }

        // ------------------------------------------------------------------
        //  W: the body at every glass severity (owner 11-17: "The wavy lines
        //  when glass breaks also appear on the car body")
        // ------------------------------------------------------------------
        const int F = DamageModel.ZFront, T = DamageModel.ZRoof;

        /// <summary>Round 2's glass cases, each through the game's bank.</summary>
        static readonly (string name, System.Action<LifeState, OwnedCar> wreck)[] Sev =
        {
            ("g1_chip", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88101, T, 0, 3.5f, (DamageModel.GlassId, 1, T, 0))),
            ("g2_crack", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88102, T, 0, 6f, (DamageModel.GlassId, 1, T, 0))),
            ("g3_web", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88103, T, 0, 7f, (DamageModel.GlassId, 2, T, 0))),
            ("g4_crushed", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88104, T, 0, 12f, (DamageModel.GlassId, 3, T, 0))),
            ("f_web", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88105, F, -1, 9f, (DamageModel.PanelId, 3, F, -1), (DamageModel.BarId, 2, F, 0),
                                                         (DamageModel.LampId, 1, F, -1))),
            ("s2_crazed", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88106, S, -1, 4.4f, (DamageModel.PanelId, 2, S, -1))),
            ("s3_shattered", (s, c) => LifeSimSelfTest.DvCrash(s, c, 88107, S, -1, 7f, (DamageModel.PanelId, 3, S, -1))),
        };

        static void Severities(LifeState st, List<(string key, OwnedCar car, CarSpec spec)> cars)
        {
            foreach (var (key, car, spec) in cars)
            {
                bool eg = key == "civic_eg";
                foreach (var c in Sev)
                {
                    // the EG at every severity; the other shells at the crumpled door and the crushed roof
                    if (!eg && c.name != "s3_shattered" && c.name != "g4_crushed") continue;
                    car.faults.Clear();
                    if (car.history != null) car.history.Clear();
                    c.wreck(st, car);
                    Rig(key + "_" + c.name, car, spec, "door,nose,chase");
                }
                int hit = 93300;
                Wreck(st, car, ref hit);   // back to the standard holes for the parts after
            }
        }

        // ------------------------------------------------------------------
        //  B: the turntable rig, close
        // ------------------------------------------------------------------
        static void Rig(string key, OwnedCar car, CarSpec spec) => Rig(key, car, spec, "side,close,rearq");

        static void Rig(string key, OwnedCar car, CarSpec spec, string frames)
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var host = new GameObject("D3_Viewer");
                var viewer = host.AddComponent<CarViewer>();
                viewer.width = 1280;
                viewer.height = 800;
                viewer.ShowOwned(car, spec);
                viewer.SetVisible(true);
                viewer.RenderNow();
                var cam = host.GetComponentInChildren<Camera>(true);
                var rig = viewer.BodyRenderer.transform.parent;
                var def = viewer.Shown;
                var fit = CarModelLibrary.Fit(def, spec);
                Vector3 o = viewer.ShownSlide;
                float half = def.colliderSize.z * fit.sz * 0.5f, cz = fit.P(def.colliderCenter).z;
                float cowl = def.cowlZ * fit.sz, belt = def.cowlY + fit.dy;
                Vector3 P(Vector3 local) => rig.TransformPoint(o + local);
                var dc = rig.GetComponent<DamageCabin>();
                L($"  B {key}: cabin {(dc != null && dc.HasCabin ? "BUILT" : "none")}" +
                  (dc != null && dc.HasCabin ? $", fit3 {dc.CabinFit.fit3} scale x/y/z {dc.CabinFit.scale * dc.CabinFit.widthFit:0.000}/{dc.CabinFit.ScaleY:0.000}/{dc.CabinFit.scale:0.000} eye {dc.CabinFit.eye}" : ""));
                void Frame(string f, Vector3 from, Vector3 at, float fov)
                {
                    cam.fieldOfView = fov;
                    cam.transform.SetPositionAndRotation(P(from), Quaternion.LookRotation(P(at) - P(from), Vector3.up));
                    viewer.RenderNow();
                    SaveRT(viewer.Picture, Path.Combine(outDir, key + "_rig_" + f + ".png"));
                }
                foreach (var f in frames.Split(','))
                    switch (f)
                    {
                        case "side": Frame(f, new Vector3(-2.7f, belt + 0.35f, cowl - 0.6f), new Vector3(-0.6f, belt + 0.14f, cowl - 0.95f), 32f); break;
                        case "close": Frame(f, new Vector3(-1.45f, belt + 0.22f, cowl - 0.85f), new Vector3(-0.5f, belt + 0.12f, cowl - 0.9f), 38f); break;
                        case "rearq": Frame(f, new Vector3(-2.2f, belt + 0.9f, cz - half - 2.6f), new Vector3(0f, belt + 0.15f, cz - half * 0.45f), 34f); break;
                        // the body: the left door and wing square on, the nose's left quarter, the chase camera's distance
                        case "door": Frame(f, new Vector3(-3.2f, belt - 0.05f, cowl - 0.7f), new Vector3(-0.7f, belt - 0.25f, cowl - 0.55f), 40f); break;
                        case "nose": Frame(f, new Vector3(-2.3f, belt + 0.45f, cz + half + 2.0f), new Vector3(-0.3f, belt - 0.2f, cz + half * 0.45f), 36f); break;
                        case "chase": Frame(f, new Vector3(-2.6f, 2.0f, cz - half - 5.6f), new Vector3(0f, 0.75f, cz), 34f); break;
                    }
                Object.DestroyImmediate(host);
            }
            catch (System.Exception e) { L("FAIL rig " + key + ": " + e); }
        }

        static void SaveRT(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ------------------------------------------------------------------
        //  C: a venue - chase, trackside, beside, the seat; day, night, rain
        // ------------------------------------------------------------------
        static void Venue(List<(string key, OwnedCar car, CarSpec spec)> cars, LifeState sevSt)
        {
            var vdef = TrackCatalog.At(TrackCatalog.CheckVenue);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (string.IsNullOrEmpty(only)) only = "BeechGap";
            foreach (var d in TrackCatalog.All) if (d.id == only) { vdef = d; break; }
            if (!PSXScreenshotTool.Open(vdef, out var cam, out var player)) { L("FAIL venue " + only + " did not open"); return; }
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (output != null) output.height = 720;
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            PSXScreenshotTool.AspectOverride = 16f / 9f;
            var t = player.transform;
            var body = player.GetComponent<CarBody>();
            var box = player.GetComponent<BoxCollider>();
            var cv = Object.FindFirstObjectByType<CockpitView>();
            var car = player.GetComponent<CarController>();
            if (car != null) { car.currentRPM = 2500f; car.speedKmh = 0f; }
            float keepNear = cam.nearClipPlane, keepFov = cam.fieldOfView;
            var keepView = ChaseCamera.Current;
            string sub = "damage3/" + Path.GetFileName(outDir) + "/";
            try
            {
                foreach (var (key, owned, spec) in cars)
                {
                    var shell = CarModelLibrary.Load(key);
                    if (shell == null || body == null) { L("FAIL venue: no shell " + key); continue; }
                    DriveSide.Override = DriveSide.IsRHD(spec.id);
                    body.Apply(shell, 0);
                    Physics.SyncTransforms();
                    var wheels = body.wheelRenderers;
                    var radii = new float[4];
                    for (int i = 0; i < 4; i++) radii[i] = car.WheelRadiusOf(i);
                    DamageView.Dress(t, body.bodyRenderer, box.center, box.size, owned, shell, body.LengthScale, wheels, radii);
                    var look = DamageView.LookOf(owned);
                    bool full = key == "civic_eg" || key.StartsWith("rx7_fd");
                    var hours = full ? new[] { "day", "night", "rain" } : new[] { "day" };
                    foreach (var h in hours)
                    {
                        SetHour(h, sun, globals, cam);
                        var frame = ChaseCamera.FrameOf(player);
                        var dials = ChaseCamera.LiveDials();
                        // outside: the cockpit's own view down, the shell whole
                        ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                        if (cv != null) cv.PreviewShow(false);
                        var ci0 = player.GetComponentInChildren<CockpitInterior>(true);
                        if (ci0 != null) ci0.Show(false, false);
                        CabinClip.Close();
                        DamageCabin.Step(true);
                        ChaseCamera.SteadyPose(ChaseCamera.View.Chase, 16f / 9f, 0f, 40f, t, frame, dials, out var eye, out var rot, out var fov, out _);
                        // the chase camera from behind-left: the rear glass and the left glass both in frame
                        cam.fieldOfView = fov;
                        cam.nearClipPlane = 0.05f;
                        PSXScreenshotTool.ShotAs(cam, sub + key + "_chase_" + h, eye, rot);
                        Vector3 sideEye = t.TransformPoint(new Vector3(-3.4f, 1.35f, -0.4f));
                        cam.fieldOfView = 34f;
                        PSXScreenshotTool.ShotAs(cam, sub + key + "_beside_" + h, sideEye,
                                                 Quaternion.LookRotation(t.TransformPoint(new Vector3(-0.5f, 1.0f, -0.2f)) - sideEye, t.up));
                        // a replay's trackside camera: low at the rear-left quarter, looking at the car
                        Vector3 tv = t.TransformPoint(new Vector3(-4.5f, 1.2f, -6.5f));
                        cam.fieldOfView = 30f;
                        PSXScreenshotTool.ShotAs(cam, sub + key + "_replay_" + h, tv, Quaternion.LookRotation(t.TransformPoint(new Vector3(0f, 0.9f, 0f)) - tv, t.up));
                        // inside: the seat, looking at the left glass
                        ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
                        if (cv != null) cv.PreviewShow(true);
                        var ci = player.GetComponentInChildren<CockpitInterior>(true);
                        if (ci == null) { L("  C " + key + ": no cockpit on the player"); continue; }
                        ci.Refit();
                        ci.Show(true, true);
                        ci.SetSteer(0f, 34f);
                        ci.Tick(0f, null);
                        ci.SetPaneBreaks(DamageGlass.CockpitPane(look.panes.z, look.hitL, false),
                                         DamageGlass.CockpitPane(look.panes.w, look.hitT, false),
                                         DamageGlass.CockpitPane(look.panes.y, look.hitX, true));
                        if (h == "rain") CockpitInterior.PreviewRain(1f, 0f, 3f);
                        DamageCabin.Step(true);
                        Vector3 ceye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, box.center, box.size, shell, body.LengthScale, body.BodyLift, ci.RHD) + ci.EyeShift;
                        float yaw = ci.RHD ? -60f : -45f;
                        var crot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(yaw), yaw, 0f);
                        cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                        cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, yaw * CockpitLook.PassengerSign / CockpitLook.MaxYaw);
                        ci.HideShell(true);
                        PSXScreenshotTool.ShotAs(cam, sub + key + "_seat_" + h, t.TransformPoint(ceye), crot);
                        ci.HideShell(false);
                        if (h == "rain") CockpitInterior.PreviewRain(0f, 0f, 0f);
                        var dc = player.GetComponent<DamageCabin>();
                        L($"  C {key} {h}: cabin {(dc != null && dc.HasCabin ? "built" : "none")}, cockpit panes L {ci.PaneBreak(ci.RHD ? "door_r" : "door_l")} rear {ci.PaneBreak("rear")}");
                        ci.Show(false, false);
                        if (cv != null) cv.PreviewShow(false);
                    }
                    // W: the EG's body at the glass severities, day and night, on the road
                    if (sevSt != null && key == "civic_eg")
                    {
                        ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                        CabinClip.Close();
                        foreach (var c in Sev)
                        {
                            if (c.name == "g1_chip" || c.name == "g2_crack") continue;
                            owned.faults.Clear();
                            if (owned.history != null) owned.history.Clear();
                            c.wreck(sevSt, owned);
                            DamageView.Dress(t, body.bodyRenderer, box.center, box.size, owned, shell, body.LengthScale, wheels, radii);
                            foreach (var h in new[] { "day", "night" })
                            {
                                SetHour(h, sun, globals, cam);
                                DamageCabin.Step(true);
                                var frame = ChaseCamera.FrameOf(player);
                                ChaseCamera.SteadyPose(ChaseCamera.View.Chase, 16f / 9f, 0f, 40f, t, frame, ChaseCamera.LiveDials(), out var eye, out var rot, out var fov, out _);
                                cam.fieldOfView = fov;
                                PSXScreenshotTool.ShotAs(cam, sub + key + "_" + c.name + "_chase_" + h, eye, rot);
                                Vector3 se = t.TransformPoint(new Vector3(-3.4f, 1.1f, 0.4f));
                                cam.fieldOfView = 40f;
                                PSXScreenshotTool.ShotAs(cam, sub + key + "_" + c.name + "_beside_" + h, se,
                                                         Quaternion.LookRotation(t.TransformPoint(new Vector3(-0.6f, 0.75f, 0.4f)) - se, t.up));
                                Vector3 ne = t.TransformPoint(new Vector3(-2.4f, 1.3f, 4.2f));
                                cam.fieldOfView = 38f;
                                PSXScreenshotTool.ShotAs(cam, sub + key + "_" + c.name + "_nose_" + h, ne,
                                                         Quaternion.LookRotation(t.TransformPoint(new Vector3(-0.3f, 0.7f, 1.2f)) - ne, t.up));
                            }
                            L($"  W {key} {c.name}: venue day + night");
                        }
                        int hh = 93400;
                        Wreck(sevSt, owned, ref hh);
                    }
                }
            }
            catch (System.Exception e) { L("FAIL venue: " + e); }
            finally
            {
                Shader.SetGlobalFloat(PaintGloss.CarWetId, 0f);
                Shader.SetGlobalVector(PaintGloss.CarDropsId, Vector4.zero);
                WeatherFx.PreviewClear();
                DriveSide.Override = null;
                PSXScreenshotTool.AspectOverride = 0f;
                ChaseCamera.PreviewView(keepView);
                cam.nearClipPlane = keepNear;
                cam.fieldOfView = keepFov;
            }
            // the frames land in Screenshots/damage3/<tag>/ through ShotAs
        }

        static void SetHour(string h, Light sun, PSXGlobals globals, Camera cam)
        {
            TimeOfDay.Apply(h == "night" ? TimeOfDay.Night : TimeOfDay.Noon, sun);
            if (globals != null) globals.Apply();
            bool rain = h == "rain";
            Shader.SetGlobalFloat(PaintGloss.CarWetId, rain ? 1f : 0f);
            Shader.SetGlobalVector(PaintGloss.CarDropsId, new Vector4(rain ? 1f : 0f, 0f, 0f, 0f));
            if (rain) WeatherFx.Preview(Weather.Rain, cam, 2.5f); else WeatherFx.PreviewClear();
        }

        // ------------------------------------------------------------------
        //  D: the seller's lot - eight holed cars, and the cost probe
        // ------------------------------------------------------------------
        static void Lot(LifeState st, List<(string key, OwnedCar car, CarSpec spec)> cars)
        {
            try
            {
                EditorSceneManager.OpenScene(SellerLotSceneBuilder.ScenePath, OpenSceneMode.Single);
                var world = Object.FindFirstObjectByType<SellerLotWorld>();
                if (world == null) { L("FAIL no SellerLotWorld"); return; }
                world.PreviewBuild();
                var spot = world.plots[world.plots.Length / 2].Find("CarSpot");
                spot.rotation = Quaternion.identity;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (world.player != null && r.transform.IsChildOf(world.player)) r.enabled = false;
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                var cam = output != null ? output.GetComponent<Camera>() : null;
                if (cam == null) cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.targetTexture == null) ?? Object.FindFirstObjectByType<Camera>();
                Light sun = null;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
                if (sun != null) TimeOfDay.Apply(TimeOfDay.Noon, sun);
                var g = Object.FindFirstObjectByType<PSXGlobals>();
                if (g != null) g.Apply();
                cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;
                // eight cars, two rows, each with its holes (the five shells, then again)
                var shells = new List<Transform>();
                var specs = new List<(Transform at, CarModelDef def, CarSpec spec, OwnedCar car)>();
                for (int i = 0; i < 8; i++)
                {
                    var c = cars[i % cars.Count];
                    var a = new GameObject("D3Spot" + i).transform;
                    a.position = spot.position + spot.right * ((i % 2) * 3.4f - 1.7f) + spot.forward * (4.5f + (i / 2) * 6.2f);
                    a.rotation = spot.rotation * Quaternion.Euler(0f, (i % 2) == 0 ? 90f : -90f, 0f);   // the holed left side toward the aisle on one row
                    var def = CarModelLibrary.Load(c.key);
                    var sh = CarShell.Spawn(a, def, 0, out _, solid: false, spec: c.spec, owned: c.car);
                    if (sh != null) { PSXTexDecode.StampTree(sh.gameObject); shells.Add(sh); specs.Add((sh, def, c.spec, c.car)); }
                }
                DamageCabin.Step(true);
                Vector3 c0 = spot.position, f0 = spot.forward;
                Vector3 eye = c0 + spot.right * 5.5f - f0 * 1.5f + Vector3.up * 1.65f;
                var rot = Quaternion.LookRotation(c0 + f0 * 12f + Vector3.up * 0.9f - eye);
                int built = 0;
                foreach (var d in DamageCabin.All) if (d != null && d.HasCabin) built++;
                L($"  D lot: {shells.Count} holed cars spawned, cabins built {built}");
                Save(Render(cam, eye, rot, 1280, 720), "lot_8cars");
                var eye2 = c0 - spot.right * 5.5f - f0 * 1.5f + Vector3.up * 1.65f;
                var rot2 = Quaternion.LookRotation(c0 + f0 * 12f + Vector3.up * 0.9f - eye2);
                Save(Render(cam, eye2, rot2, 1280, 720), "lot_8cars_b");

                // THE COST: one frame, five ways, interleaved
                int W = PlatformProfile.PC ? 1920 : 852, H = PlatformProfile.PC ? 1080 : 480;
                var mint = CarMarket.MakeOwnedCar(st, cars[0].spec, 90, 60000f, cars[0].spec.price);
                mint.id = "d3_mint";
                var rash = CarMarket.MakeOwnedCar(st, cars[0].spec, 90, 60000f, cars[0].spec.price);
                rash.id = "d3_rash";
                for (int w = 0; w < 4; w++) LifeSimSelfTest.D2Wheel(st, rash, 93500 + w, w, 3, "curb");
                var times = new Dictionary<string, List<double>>();
                string[] modes = { "cabin", "dark", "boxes", "rash", "mint" };
                foreach (var m in modes) times[m] = new List<double>();
                void Mode(string m)
                {
                    DamageCabin.Suspend = m != "cabin";
                    Shader.SetGlobalFloat("_PSXGlsDark", m == "boxes" ? 0f : (DamageCabin.On ? 1f : 0f));
                    foreach (var s in specs)
                    {
                        var who = m == "mint" ? mint : m == "rash" ? rash : s.car;
                        CarShell.Dress(s.at, s.def, s.spec, 0, who);
                    }
                    DamageCabin.Step(true);
                    if (m == "boxes") Shader.SetGlobalFloat("_PSXGlsDark", 0f);
                }
                // the glass shader's own branch: holes on screen, but an eye a step back so all eight fill the frame
                Vector3 ceye = c0 + spot.right * 4.2f + f0 * 1.0f + Vector3.up * 1.5f;
                var crot = Quaternion.LookRotation(c0 + f0 * 11f + Vector3.up * 0.8f - ceye);
                for (int round = 0; round < 3; round++)
                    foreach (var m in modes)
                    {
                        Mode(m);
                        for (int k = 0; k < 3; k++) Object.DestroyImmediate(Render(cam, ceye, crot, W, H));
                        for (int k = 0; k < 25; k++)
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            Object.DestroyImmediate(Render(cam, ceye, crot, W, H));
                            times[m].Add(sw.Elapsed.TotalMilliseconds);
                        }
                        if (round == 0) Save(Render(cam, ceye, crot, 1280, 720), "cost_" + m);
                    }
                double Med(List<double> v) { var a = v.OrderBy(x => x).ToList(); return a.Count == 0 ? 0 : a[a.Count / 2]; }
                L($"  COST (8 cars, the lot, {W}x{H}, editor wall clock per frame incl. read-back, median of {times["cabin"].Count}):");
                foreach (var m in modes) L($"    {m,-6} {Med(times[m]):0.00} ms (p10 {Pct(times[m], 0.1):0.00}, p90 {Pct(times[m], 0.9):0.00})");
                L($"    cabin - dark  = {Med(times["cabin"]) - Med(times["dark"]):+0.00;-0.00} ms (the eight cockpit models)");
                L($"    dark  - mint  = {Med(times["dark"]) - Med(times["mint"]):+0.00;-0.00} ms (the glass path, holes dark)");
                L($"    boxes - mint  = {Med(times["boxes"]) - Med(times["mint"]):+0.00;-0.00} ms (round 2's glass path, the boxes)");
                L($"    rash  - mint  = {Med(times["rash"]) - Med(times["mint"]):+0.00;-0.00} ms (wheel rash on 32 wheels)");
                // the CPU side: the per-camera pass over the holed cars, and a frame's step
                Mode("cabin");
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                for (int k = 0; k < 2000; k++) DamageCabin.ForCamera(cam);
                double camUs = sw2.Elapsed.TotalMilliseconds * 1000.0 / 2000.0;
                L($"    CPU: DamageCabin.ForCamera with {DamageCabin.All.Count} holed cars: {camUs:0.0} us a camera");
                Save(Render(cam, ceye, crot, 1280, 720), "cost_cabin_after");
                DamageCabin.Suspend = false;
            }
            catch (System.Exception e) { L("FAIL lot: " + e); }
        }

        static double Pct(List<double> v, double p)
        {
            var a = v.OrderBy(x => x).ToList();
            return a.Count == 0 ? 0 : a[Mathf.Clamp((int)(a.Count * p), 0, a.Count - 1)];
        }

        static Texture2D Render(Camera cam, Vector3 pos, Quaternion rot, int w, int h)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            StreetLights.Push(pos, rot * Vector3.forward);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var request = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(cam, request);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            rt.Release(); Object.DestroyImmediate(rt);
            return tex;
        }

        static void Save(Texture2D t, string name)
        {
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }
    }
}
