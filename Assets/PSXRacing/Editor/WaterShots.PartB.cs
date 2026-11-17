using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WATER PART B's frames (2026-11-17; the owner: "Why hasn't the upgraded
    /// bodies of water been implemented?", then on PC49's HD Lake Lure: "the
    /// current/tide/waves were moving very fast for a lake. It looked like an
    /// ocean"). The places he drives, the way he drives them: the player's car
    /// in lane, the game's lamps and its headlights at night, the game's rain
    /// falling, the chase camera and the driver's eye (1.15 m over the road,
    /// the cabin hidden). Sets (PSX_WATERSHOT_SETS):
    ///   lure      LakeLure: the lake from US 64/74A, the marina (v0525), the
    ///             Rocky Broad beside the road, the Bat Cave river in heavy rain
    ///             (his screenshot's place), and a STAGED steep look into the
    ///             lake; noon / night / rain / night rain; plus motion strips
    ///             (three frames 0.5 s apart, the clock frozen and stepped).
    ///   drive     Charlotte: in lane on the road beside Lake Wylie and on a
    ///             bridge over the Catawba arm; noon / night / night rain; tiers
    ///             0 and 1 and a rear-view (tier 0) frame; a motion strip; the
    ///             gusts seen from 30 m up (STAGED) for the long-repeat check.
    ///   seastrip  EmeraldIsle / Langston: a motion strip of the sea (unchanged).
    ///   rivertest the staged test river seen from above at four headings (the
    ///             refraction must stay with the flow, item 16).
    ///   compile   PSX/Water and its depth copy compiled for GLES3 and Vulkan
    ///             (both variants), errors logged (item 13).
    /// Every pose logs its eye's height over the road and the water.
    /// </summary>
    public static partial class WaterShots
    {
        const float DriverEyeM = 1.15f;
        /// <summary>The tier the frames are drawn at (2 = a PC with REFLECTIONS on).</summary>
        static int shotTier = 2;
        /// <summary>The frozen clock's instant for the next render (the strips step it).</summary>
        static float testTime = 37.25f;

        // ------------------------------------------------------------------
        //  Driving poses
        // ------------------------------------------------------------------

        struct Drive
        {
            public string name;
            public Vector3 road;       // the lane's middle, on the road
            public Vector3 fwd;        // the way the car faces (flat)
            public float yawToWater;   // the driver's look turned this far (deg, + = right)
            public float pitch;        // the driver's look pitch (deg, - = down)
            public float waterY;       // the water's level here (NaN: unknown)
            public bool staged;        // a camera the game could not put there
            public string what;
        }

        static void PlaceCar(GameObject car, Vector3 road, Vector3 fwd)
        {
            if (car == null) return;
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            var pos = road + Vector3.up * 0.35f;
            var cc = car.GetComponent<CarController>();
            if (cc != null) cc.TeleportTo(pos, rot); else car.transform.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
        }

        static void CarLightsFor(GameObject car, int hour)
        {
            bool lit = TimeOfDay.At(hour).lightsOn || Seasons.LightsOn(Seasons.CurrentWeather);
            NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
            if (car != null) foreach (var l in car.GetComponentsInChildren<CarLights>(true)) l.PreviewBuild(lit, false);
            CarLights.PushGlobals();
        }

        static int HourIndex(string hourName)
        {
            int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hourName, System.StringComparison.OrdinalIgnoreCase));
            return hour < 0 ? TimeOfDay.Noon : hour;
        }

        /// <summary>One frame from an exact pose (fov, the chase rig's
        /// projection shift), the game's exposure for it, rain falling if the
        /// weather says so, at <see cref="shotTier"/>.</summary>
        static void ShootAt(Camera cam, string name, Vector3 eye, Quaternion rot, float fov, float shift, GameObject car,
                            PSXGlobals globals, Light sun, TrackPath path, TrackCatalog.TrackDef def, bool rain, string note)
        {
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.fieldOfView = fov;
            cam.aspect = 16f / 9f;
            if (Mathf.Abs(shift) > 1e-5f) cam.projectionMatrix = ChaseCamera.ShiftedProjection(fov, cam.aspect, cam.nearClipPlane, cam.farClipPlane, shift);
            else cam.ResetProjectionMatrix();
            if (globals != null && car != null)
            {
                globals.adapt = ExposureAdapt.SteadyFor(eye, car.transform, sun != null ? -sun.transform.forward : Vector3.up,
                                                        globals.night, path, def, globals.dayFill);
                globals.Apply();
            }
            if (rain) WeatherFx.Preview(Weather.Rain, cam, 2.5f);
            WaterLook.ForceCamera = cam; WaterLook.ForceTier = shotTier;
            WetReflections.PreviewSet(cam, 1);
            SunShadows.RenderFor(cam);
            var tex = Render(cam, width, lines);
            File.WriteAllBytes(Path.Combine(dir, name + "_" + tag + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            if (rain) WeatherFx.PreviewClear();
            cam.ResetProjectionMatrix(); cam.ResetAspect();
            Debug.Log($"[WaterShot] {name}_{tag}: {note}; eye {eye} fov {fov:0.0} tier {shotTier} wind {Shader.GetGlobalVector("_PSXWaterWind")} " +
                      $"rain {Shader.GetGlobalFloat("_PSXWaterRain"):0.00} keyword {(Shader.IsKeywordEnabled(WaterLook.Keyword) ? "on" : "off")} " +
                      $"headlights {CarLights.PushedCount}");
        }

        /// <summary>A driven pose at one hour: the chase view and the driver's
        /// eye (cabin and body hidden; the lamps still lit).</summary>
        static void ShootDrive(Camera cam, Drive d, string hourName, bool rain, bool driver, bool chase, GameObject car,
                               PSXGlobals globals, Light sun, TrackPath path, TrackCatalog.TrackDef def)
        {
            SetHour(hourName, rain, sun, globals);
            int hour = HourIndex(hourName);
            PlaceCar(car, d.road, d.fwd);
            CarLightsFor(car, hour);
            string cond = (rain ? (hourName.ToUpperInvariant() == "NIGHT" ? "night_rain" : "rain") : hourName.ToLowerInvariant());
            Vector3 right = Vector3.Cross(Vector3.up, d.fwd).normalized;
            string over = float.IsNaN(d.waterY) ? "" : $", {d.road.y + DriverEyeM - d.waterY:0.0} m over the water";
            if (chase && car != null)
            {
                ChaseCamera.SteadyPose(ChaseCamera.View.Chase, 16f / 9f, 0f, ChaseCamera.DefaultSpeedFullMps, car.transform,
                                       ChaseCamera.FrameOf(car), default, out Vector3 eye, out Quaternion look, out float fov, out float shift);
                ShootAt(cam, $"{d.name}_chase_{cond}", eye, look, fov, shift, car, globals, sun, path, def, rain,
                        $"CHASE (the game's rig at rest) {d.what}; eye {eye.y - d.road.y:0.0} m over the road");
            }
            if (driver)
            {
                var rs = car != null ? car.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                var was = new bool[rs.Length];
                for (int k = 0; k < rs.Length; k++) { was[k] = rs[k].forceRenderingOff; rs[k].forceRenderingOff = true; }
                Vector3 eye = d.road + Vector3.up * DriverEyeM - right * 0.35f;
                Vector3 look = Quaternion.Euler(0f, d.yawToWater, 0f) * d.fwd;
                var rot = Quaternion.LookRotation(look) * Quaternion.Euler(-d.pitch, 0f, 0f);
                ShootAt(cam, $"{d.name}_{(d.staged ? "staged" : "driver")}_{cond}", d.staged ? d.road : eye, rot, d.staged ? 55f : 60f, 0f,
                        car, globals, sun, path, def, rain,
                        (d.staged ? "STAGED " : $"DRIVER'S EYE {DriverEyeM:0.00} m over the road, 0.35 m left of the lane's middle, looking {d.yawToWater:0} deg {(d.yawToWater >= 0 ? "right" : "left")} of the road, cabin hidden; ")
                        + d.what + over);
                for (int k = 0; k < rs.Length; k++) rs[k].forceRenderingOff = was[k];
            }
        }

        /// <summary>A MOTION STRIP: three frames 0.5 s apart from one pose, the
        /// frozen clock stepped (PSX_WATERSHOT_FREEZE=1 sandbox), plus the
        /// water's own pixels as a mask (drawn vs hidden).</summary>
        static void Strip(Camera cam, string name, Vector3 eye, Quaternion rot, float fov, Renderer[] water)
        {
            cam.transform.SetPositionAndRotation(eye, rot);
            cam.fieldOfView = fov; cam.aspect = 16f / 9f; cam.ResetProjectionMatrix();
            WaterLook.ForceCamera = cam; WaterLook.ForceTier = shotTier;
            WetReflections.PreviewSet(cam, 1);
            SunShadows.RenderFor(cam);
            float keep = testTime;
            for (int k = 0; k < 3; k++)
            {
                testTime = keep + 0.5f * k;
                var tex = Render(cam, width, lines);
                File.WriteAllBytes(Path.Combine(dir, $"{name}_t{k}_{tag}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            testTime = keep;
            Show(water, false);
            var bare = Render(cam, width, lines);
            Show(water, true);
            var full = Render(cam, width, lines);
            var a = bare.GetPixels32(); var b = full.GetPixels32();
            var m = new Texture2D(bare.width, bare.height, TextureFormat.R8, false);
            var mp = new Color32[a.Length];
            int n = 0;
            for (int k = 0; k < a.Length; k++)
            {
                bool w = Mathf.Abs(a[k].r - b[k].r) + Mathf.Abs(a[k].g - b[k].g) + Mathf.Abs(a[k].b - b[k].b) > 6;
                mp[k] = w ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                if (w) n++;
            }
            m.SetPixels32(mp); m.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{name}_mask_{tag}.png"), m.EncodeToPNG());
            Object.DestroyImmediate(m); Object.DestroyImmediate(bare); Object.DestroyImmediate(full);
            Debug.Log($"[WaterShot] STRIP {name}_{tag}: 3 frames 0.5 s apart (frozen clock {keep:0.00}..{keep + 1f:0.00} s), water {100f * n / Mathf.Max(a.Length, 1):0.0}% of the frame, eye {eye}");
        }

        static void QuietKeepCar()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            foreach (var cv in Object.FindObjectsByType<CockpitView>()) cv.PreviewShow(false);
            CarReflections.Off();
            SunGlareFx.PreviewSet(null, 0f, 0f, true);
        }

        static List<Renderer> WaterRenderers()
        {
            var list = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name == WaterLook.ShaderName) { list.Add(r); break; }
            return list;
        }

        // ------------------------------------------------------------------
        //  Lake Lure / Chimney Rock / Bat Cave
        // ------------------------------------------------------------------

        static void Lure()
        {
            TrackCatalog.TrackDef def = null;
            foreach (var d0 in TrackCatalog.Scened) if (d0.id == "LakeLure") def = d0;
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var car)) { Debug.LogError("[WaterShot] cannot open LakeLure"); return; }
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            var path = Object.FindAnyObjectByType<TrackPath>();
            if (path == null || path.Count < 10) { Debug.LogError("[WaterShot] LakeLure: no path"); return; }
            QuietKeepCar();
            var waters = WaterRenderers();
            var lakePts = new List<Vector3>(); var riverPts = new List<Vector3>();
            foreach (var r in waters)
            {
                LogMesh(r);
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                bool river = r.sharedMaterial != null && r.sharedMaterial.name.Contains("River");
                var vs = mf.sharedMesh.vertices;
                int step = Mathf.Max(1, vs.Length / 1500);
                for (int k = 0; k < vs.Length; k += step) (river ? riverPts : lakePts).Add(r.transform.TransformPoint(vs[k]));
            }
            Debug.Log($"[WaterShot] LakeLure: {waters.Count} water renderers, {lakePts.Count} lake + {riverPts.Count} river sample points, {path.Count} stations");

            // how much of a body of water lies ahead of station i, in a cone
            int Ahead(List<Vector3> pts, int i, float near, float far, float coneDeg, out float yAvg)
            {
                Vector3 at = path.GetPoint(i);
                Vector3 f = path.GetPoint(Mathf.Min(path.Count - 1, i + 2)) - path.GetPoint(Mathf.Max(0, i - 2)); f.y = 0f; f.Normalize();
                float c = Mathf.Cos(coneDeg * Mathf.Deg2Rad);
                int n = 0; double ys = 0;
                foreach (var p in pts)
                {
                    Vector3 v = p - at; v.y = 0f;
                    float dd = v.magnitude;
                    if (dd < near || dd > far) continue;
                    if (Vector3.Dot(v / dd, f) < c) continue;
                    n++; ys += p.y;
                }
                yAvg = n > 0 ? (float)(ys / n) : float.NaN;
                return n;
            }
            int Best(List<Vector3> pts, int from, int to, float near, float far, float cone)
            {
                int best = -1, bestN = 0;
                for (int i = Mathf.Max(2, from); i < Mathf.Min(path.Count - 3, to); i += 3)
                {
                    int n = Ahead(pts, i, near, far, cone, out _);
                    if (n > bestN) { bestN = n; best = i; }
                }
                return best;
            }
            Drive Make(string name, int i, List<Vector3> pts, string what, float yawCone)
            {
                Vector3 at = path.GetPoint(i);
                Vector3 f = path.GetPoint(Mathf.Min(path.Count - 1, i + 2)) - path.GetPoint(Mathf.Max(0, i - 2)); f.y = 0f; f.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, f).normalized;
                // which side the water lies on, for the driver's glance
                float side = 0f; int n = 0; float wy = float.NaN; double ys = 0;
                foreach (var p in pts)
                {
                    Vector3 v = p - at; v.y = 0f;
                    float dd = v.magnitude;
                    if (dd < 8f || dd > 160f || Vector3.Dot(v / dd, f) < 0.2f) continue;
                    side += Mathf.Sign(Vector3.Dot(v, right)); n++; ys += p.y;
                }
                if (n > 0) wy = (float)(ys / n);
                return new Drive
                {
                    name = name, road = at + right * (path.roadWidth * 0.25f), fwd = f,
                    yawToWater = n == 0 ? 0f : Mathf.Sign(side) * yawCone, pitch = -2f, waterY = wy,
                    what = $"wp {i} ({i * (path.TotalLength / Mathf.Max(1, path.Count - 1)):0} m), {what}",
                };
            }

            var poses = new List<Drive>();
            int lakeI = Best(lakePts, 0, path.Count, 15f, 400f, 35f);
            if (lakeI >= 0) poses.Add(Make("lure_lake_lane", lakeI, lakePts, "Lake Lure ahead and beside US 64/74A", 25f));
            if (1439 < path.Count) poses.Add(Make("lure_marina", 1439, lakePts, "above the marina (the owner's video at 8:45, v0525)", 20f));
            int riverI = Best(riverPts, (int)(path.Count * 0.55f), (int)(path.Count * 0.85f), 8f, 150f, 40f);
            if (riverI >= 0) poses.Add(Make("lure_river_lane", riverI, riverPts, "the Rocky Broad beside US 64/74A in the gorge", 30f));
            int batI = Best(riverPts, (int)(path.Count * 0.85f), path.Count, 8f, 150f, 40f);
            if (batI >= 0) poses.Add(Make("lure_batcave", batI, riverPts, "the Bat Cave end: the Rocky Broad beside the road (the owner's PC49 screenshot: chase, heavy rain)", 30f));
            foreach (var p in poses) Debug.Log($"[WaterShot] LakeLure pose {p.name}: {p.what}, road {p.road}, water y {p.waterY:0.0}");

            // the owner's screenshot first: Bat Cave, chase, heavy rain (the game's own rain, then a downpour)
            foreach (var p in poses)
            {
                if (p.name != "lure_batcave") continue;
                ShootDrive(cam, p, "AFTERNOON", true, false, true, car, globals, sun, path, def);
                WaterLook.ForceRain = 1f;
                var q = p; q.name = "lure_batcave_downpour";
                ShootDrive(cam, q, "AFTERNOON", true, false, true, car, globals, sun, path, def);
                WaterLook.ForceRain = -1f;
            }
            foreach (var p in poses)
            {
                ShootDrive(cam, p, "NOON", false, true, true, car, globals, sun, path, def);
                ShootDrive(cam, p, "NIGHT", false, true, true, car, globals, sun, path, def);
                ShootDrive(cam, p, "AFTERNOON", true, true, p.name != "lure_batcave", car, globals, sun, path, def);
                ShootDrive(cam, p, "NIGHT", true, true, true, car, globals, sun, path, def);
            }
            // the steep look into the lake (item 11: the lidar's drawdown floor
            // must not show): STAGED, 7 m above the road where the lake is
            // nearest, pitched down 35 degrees
            int steepI = -1; float steepD = 1e9f; Vector3 steepTo = Vector3.zero;
            for (int i = 2; i < path.Count - 3; i += 4)
            {
                Vector3 at = path.GetPoint(i);
                foreach (var lp in lakePts)
                {
                    float dd = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(lp.x, lp.z));
                    if (dd > 12f && dd < steepD) { steepD = dd; steepI = i; steepTo = lp; }
                }
            }
            if (steepI >= 0)
            {
                Vector3 at = path.GetPoint(steepI);
                Vector3 to = steepTo - at; to.y = 0f; to.Normalize();
                var st = new Drive { name = "lure_lake_steep", road = at + Vector3.up * 7f, fwd = to, yawToWater = 0f, pitch = -35f,
                                     waterY = steepTo.y, staged = true, what = $"wp {steepI}: 7 m above the road, looking down 35 deg into the lake {steepD:0} m off" };
                ShootDrive(cam, st, "NOON", false, true, false, car, globals, sun, path, def);
            }
            // MOTION (the owner: "moving very fast for a lake"): strips of the
            // lake and the river from the driver's eye at noon
            foreach (var p in poses)
            {
                if (p.name != "lure_lake_lane" && p.name != "lure_river_lane" && p.name != "lure_batcave") continue;
                SetHour("NOON", false, sun, globals);
                PlaceCar(car, p.road, p.fwd);
                Vector3 right = Vector3.Cross(Vector3.up, p.fwd).normalized;
                var rs = car.GetComponentsInChildren<Renderer>(true);
                foreach (var r in rs) r.forceRenderingOff = true;
                Vector3 look = Quaternion.Euler(0f, p.yawToWater, 0f) * p.fwd;
                Strip(cam, p.name + "_motion", p.road + Vector3.up * DriverEyeM - right * 0.35f, Quaternion.LookRotation(look) * Quaternion.Euler(4f, 0f, 0f), 60f, waters.ToArray());
                foreach (var r in rs) r.forceRenderingOff = false;
            }
            // tiers 0 and 1 at the lake (a PC with REFLECTIONS off; a phone / a mirror)
            foreach (var p in poses)
            {
                if (p.name != "lure_lake_lane") continue;
                for (int tier = 0; tier <= 1; tier++)
                {
                    shotTier = tier;
                    var q = p; q.name = "lure_lake_lane_tier" + tier;
                    ShootDrive(cam, q, "NOON", false, true, false, car, globals, sun, path, def);
                }
                shotTier = 2;
            }
            if (WaterLook.On && System.Environment.GetEnvironmentVariable("PSX_WATERSHOT_STAGETIME") == "1" && lakeI >= 0)
            {
                var p = poses[0];
                Vector3 right = Vector3.Cross(Vector3.up, p.fwd).normalized;
                TimeTiers(cam, "lure_lake_lane", p.road + Vector3.up * DriverEyeM - right * 0.35f, Quaternion.Euler(0f, p.yawToWater, 0f) * p.fwd, -2f, waters.ToArray());
            }
        }

        // ------------------------------------------------------------------
        //  Charlotte, from the driver's seat
        // ------------------------------------------------------------------

        static void DriveCharlotte()
        {
            var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
            if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var car)) { Debug.LogError("[WaterShot] Charlotte did not open"); return; }
            var world = Object.FindAnyObjectByType<CityWorld>();
            var map = CityMap.Get();
            if (world == null || map == null) { Debug.LogError("[WaterShot] no CityWorld / city data"); return; }
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            var path = Object.FindAnyObjectByType<TrackPath>();
            QuietKeepCar();
            PSXRacingBuilder.EnsureCityTextures();
            PSXRacingBuilder.CityWaterMat();
            RaceHandoff.CalendarDay = ColourSpots.DayIn(Season.Summer);
            CityMap.Water wylie = null;
            foreach (var w in map.waters)
                if (w.lake && (w.name ?? "").Contains("Wylie") && (wylie == null || Area(w) > Area(wylie))) wylie = w;
            if (wylie == null) { Debug.LogError("[WaterShot] no Lake Wylie"); return; }
            var poses = new List<Drive>();
            // in lane beside it (a road on the ground 12-60 m off the water) and on a bridge over it
            if (LaneBy(map, wylie, false, out var beside)) { beside.name = "wylie_lane"; poses.Add(beside); }
            if (LaneBy(map, wylie, true, out var bridge)) { bridge.name = "wylie_bridge_seat"; poses.Add(bridge); }
            foreach (var p in poses) Debug.Log($"[WaterShot] Charlotte pose {p.name}: {p.what}, road {p.road}");
            foreach (var p in poses)
            {
                world.DropAll();
                world.EnsureRing(p.road + p.fwd * 60f, 2);
                var waters = WaterRenderers().ToArray();
                ShootDrive(cam, p, "NOON", false, true, true, car, globals, sun, path, def);
                ShootDrive(cam, p, "NIGHT", false, true, true, car, globals, sun, path, def);
                ShootDrive(cam, p, "NIGHT", true, true, true, car, globals, sun, path, def);
                if (p.name == "wylie_lane")
                {
                    ShootDrive(cam, p, "AFTERNOON", true, true, false, car, globals, sun, path, def);
                    for (int tier = 0; tier <= 1; tier++)
                    {
                        shotTier = tier;
                        var q = p; q.name = "wylie_lane_tier" + tier;
                        ShootDrive(cam, q, "NOON", false, true, false, car, globals, sun, path, def);
                    }
                    shotTier = 0;
                    // the rear-view mirror's view (tier 0: mirrors never get the copies)
                    var m = p; m.name = "wylie_mirror"; m.yawToWater = 180f - p.yawToWater * 0.5f;
                    ShootDrive(cam, m, "NOON", false, true, false, car, globals, sun, path, def);
                    shotTier = 2;
                    SetHour("NOON", false, sun, globals);
                    PlaceCar(car, p.road, p.fwd);
                    Vector3 right = Vector3.Cross(Vector3.up, p.fwd).normalized;
                    var rs = car.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in rs) r.forceRenderingOff = true;
                    Strip(cam, "wylie_lane_motion", p.road + Vector3.up * DriverEyeM - right * 0.35f,
                          Quaternion.LookRotation(Quaternion.Euler(0f, p.yawToWater, 0f) * p.fwd) * Quaternion.Euler(4f, 0f, 0f), 60f, waters);
                    foreach (var r in rs) r.forceRenderingOff = false;
                    // the gusts from 30 m up in a 6 m/s wind (item 17's long repeat): STAGED
                    WaterLook.ForceWind = new Vector2(6f, 0f);
                    var hi = new Drive { name = "wylie_gusts_high", road = p.road + Vector3.up * 30f, fwd = Quaternion.Euler(0f, p.yawToWater, 0f) * p.fwd,
                                         pitch = -20f, staged = true, waterY = float.NaN, what = "30 m above the road, 6 m/s wind, looking 20 deg down over the lake to the far shore" };
                    ShootDrive(cam, hi, "NOON", false, true, false, car, globals, sun, path, def);
                    WaterLook.ForceWind = null;
                }
            }
        }

        static float Area(CityMap.Water w) => (w.bbMax - w.bbMin).x * (w.bbMax - w.bbMin).y;

        /// <summary>The right-hand lane of the nearest road beside (or on a
        /// bridge over) the water, facing along the road, the driver's look
        /// turned toward the water.</summary>
        static bool LaneBy(CityMap map, CityMap.Water w, bool onBridge, out Drive d)
        {
            d = default;
            float best = float.MaxValue; int bi = -1; float bs = 0f; Vector2 near = Vector2.zero;
            Vector2 c = (w.bbMin + w.bbMax) * 0.5f;
            float reach = Mathf.Max((w.bbMax - w.bbMin).magnitude * 0.5f, 50f) + 200f;
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (e.length < 30f) continue;
                var pa = e.PointAt(0f); var pb = e.PointAt(e.length);
                if (Vector2.Distance(pa, c) > reach + e.length && Vector2.Distance(pb, c) > reach + e.length) continue;
                for (float s = 10f; s < e.length - 10f; s += 8f)
                {
                    var p = e.PointAt(s);
                    if (Vector2.Distance(p, c) > reach) continue;
                    float dd = WaterDistance(w, p, out Vector2 nearest);
                    bool el = e.ElevatedAt(s);
                    float score;
                    if (onBridge) { if (!el || dd > -8f) continue; score = -dd > 30f ? 0f : 30f + dd; }   // well out over it
                    else { if (el || dd < 12f || dd > 60f) continue; score = dd; }
                    if (score >= best) continue;
                    best = score; bi = ei; bs = s; near = nearest;
                }
            }
            if (bi < 0) return false;
            var ed = map.edges[bi];
            var t2 = ed.TangentAt(bs).normalized;
            var r2 = new Vector2(t2.y, -t2.x);
            float divTR = ed.oneway ? 0f : -LineModel.DividerLat(ed);
            var pe = ed.PointAt(bs) + r2 * (ed.oneway ? 0f : divTR + 1.83f);
            var fwd = new Vector3(t2.x, 0f, t2.y);
            Vector2 toW = (near - pe).normalized;
            float yaw = Vector2.SignedAngle(toW, new Vector2(fwd.x, fwd.z));   // + = the water lies to the right
            d = new Drive
            {
                road = new Vector3(pe.x, ed.YAt(bs), pe.y), fwd = fwd,
                yawToWater = onBridge ? Mathf.Sign(yaw) * 30f : Mathf.Clamp(yaw, -50f, 50f), pitch = -2f, waterY = float.NaN,
                what = $"'{ed.name}' {(onBridge ? "on a bridge over" : "beside")} '{w.name}' (edge {bi} at {bs:0} m), the right-hand lane",
            };
            return true;
        }

        // ------------------------------------------------------------------
        //  The sea, unchanged (a strip), and the test river's headings
        // ------------------------------------------------------------------

        static void SeaStrip()
        {
            if (!OpenStage("EmeraldIsle", out var cam, out var sun, out var globals, out var path)) return;
            var sea = GameObject.Find("Sea")?.GetComponent<Renderer>();
            if (sea == null) return;
            ApplyLook(sea, PSXRacingBuilder.WaterPreset.Sea);
            float seaY = sea.bounds.center.y;
            // the beach pose of the Emerald set, found the same way
            Vector3 best = Vector3.zero; float bestD = 1e9f;
            for (int i = 5; i < path.Count - 5; i += 6)
            {
                Vector3 pt = path.GetPoint(i);
                for (float m = 30f; m < 900f; m += 10f)
                {
                    if (!Physics.Raycast(pt + Vector3.back * m + Vector3.up * 80f, Vector3.down, out RaycastHit hh, 200f)) continue;
                    if (hh.point.y > seaY + 0.05f) continue;
                    if (m < bestD) { bestD = m; best = pt + Vector3.back * m; }
                    break;
                }
            }
            if (bestD > 1e8f) return;
            Vector3 beach = best + Vector3.forward * 25f;
            beach.y = Ground(beach, seaY + 30f) + 1.7f;
            SetHour("NOON", false, sun, globals);
            Strip(cam, "emerald_surf_motion", beach, Quaternion.LookRotation(Vector3.back) * Quaternion.Euler(5f, 0f, 0f), 55f, new[] { sea });
        }

        static void RiverHeadings()
        {
            if (!OpenStage("ChimneyRock", out var cam, out var sun, out var globals, out var path)) return;
            var site = WaterTestSite.Build(path);
            if (site == null) return;
            noSunMap = true;
            SetHour("NOON", false, sun, globals);
            // straight over the river 14 m up, looking 50 deg down, turned four ways
            Vector3 at = site.riverEye + site.riverLook * 25f;
            at.y = site.riverEye.y + 6f;
            for (int h = 0; h < 360; h += 90)
                Shoot(cam, "test_river_heading" + h, at, Quaternion.Euler(0f, h, 0f) * site.riverLook, -50f);
            noSunMap = false;
            Object.DestroyImmediate(site.root);
        }

        // ------------------------------------------------------------------
        //  GLES3 and Vulkan (item 13)
        // ------------------------------------------------------------------

        static void CompileCheck()
        {
            foreach (var name in new[] { WaterLook.ShaderName, WaterLook.DepthCopyShader })
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogError("[WaterShot] COMPILE: no shader " + name); continue; }
                var data = ShaderUtil.GetShaderData(sh);
                for (int si = 0; si < data.SubshaderCount; si++)
                {
                    var sub = data.GetSubshader(si);
                    for (int pi = 0; pi < sub.PassCount; pi++)
                    {
                        var pass = sub.GetPass(pi);
                        foreach (var kw in name == WaterLook.ShaderName ? new[] { new string[0], new[] { WaterLook.Keyword } } : new[] { new string[0] })
                            foreach (var plat in new[] { UnityEditor.Rendering.ShaderCompilerPlatform.GLES3x, UnityEditor.Rendering.ShaderCompilerPlatform.Vulkan, UnityEditor.Rendering.ShaderCompilerPlatform.D3D })
                                foreach (var stage in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
                                {
                                    var target = plat == UnityEditor.Rendering.ShaderCompilerPlatform.D3D ? BuildTarget.StandaloneWindows64 : BuildTarget.Android;
                                    var info = pass.CompileVariant(stage, kw, plat, target);
                                    int errs = 0;
                                    var sb = new System.Text.StringBuilder();
                                    foreach (var msg in info.Messages)
                                    {
                                        if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errs++;
                                        sb.Append(" | ").Append(msg.severity).Append(": ").Append(msg.message).Append(" (line ").Append(msg.line).Append(')');
                                    }
                                    Debug.Log($"[WaterShot] COMPILE {name} pass {pi} [{string.Join(" ", kw)}] {plat} {stage}: {(info.Success ? "OK" : "FAILED")}, {errs} error(s), {info.ShaderData?.Length ?? 0} bytes{sb}");
                                }
                    }
                }
            }
        }
    }
}
