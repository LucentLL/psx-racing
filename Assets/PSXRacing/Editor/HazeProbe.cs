using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHAT IS THE PALE STUFF AT THE END OF THE VIEW (owner 2026-11-09, a dusk
    /// frame on NC 226A: "I still see the haze on the dusk horizon and haze on
    /// distant mountains"). The same frame several times, each with one
    /// suspect painted a colour nothing else in the picture has:
    ///
    ///   asis      the PC player's picture (far plane, forest reach and the
    ///             end-of-view fade as StageCulling sets them in a player)
    ///   fogmag    the fade colour magenta: every pixel the fade touches
    ///   skygreen  the sky flat green: every pixel that is sky, above the
    ///             horizon AND below it (where no land is drawn)
    ///   nofade    no fade at all: where the drawn land really ends
    ///   old       the picture before the stage horizon (far plane at the
    ///             detail's edge, the end-of-view fade), from the same scene
    ///   aerial    LookChoices.AerialDepth on (the owner's A/B)
    ///
    /// Venue PSX_HAZE_VENUE (SwissNC226A), hours PSX_HAZE_HOURS (dusk,night,noon),
    /// stations PSX_HAZE_M (metres along the road). Writes Screenshots\hz_*.png
    /// and hz_probe.txt; tools read the pictures, nobody judges them by eye.
    /// </summary>
    public static class HazeProbe
    {
        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        public static void Capture()
        {
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "hz_*")) File.Delete(f);
            var log = new System.Text.StringBuilder();

            string id = Env("PSX_HAZE_VENUE", "SwissNC226A");
            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { Debug.LogError("[HazeProbe] no scene " + id); return; }

            var path = Object.FindFirstObjectByType<TrackPath>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            var dressing = Object.FindFirstObjectByType<SeasonDress>();
            var forest = Object.FindFirstObjectByType<MeshForest>();
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (path == null || globals == null || sun == null) { Debug.LogError("[HazeProbe] scene parts missing"); return; }
            if (output != null) output.height = 720;

            // THE PC PLAYER'S VIEW: StageCulling.OnEnable never runs in edit mode.
            var sc = cam.GetComponent<StageCulling>();
            float sceneFar = cam.farClipPlane;
            float keepHorizon = sc != null ? sc.horizonFarClip : 0f;
            if (sc != null) sc.Configure(cam);
            log.AppendLine($"venue {id}: scene far plane {sceneFar:0} m, camera far plane {cam.farClipPlane:0} m, detail to {TimeOfDay.FarEdgeM:0} m, horizon {TimeOfDay.HorizonM:0} m, fogScale {globals.fogScale:0.00}, mesh forest {(forest != null ? "yes" : "no")}, shot {(output != null ? output.height : 0)} lines, PC profile {PlatformProfile.PC}");

            int day = int.Parse(Env("PSX_HAZE_DAY", "0"), CultureInfo.InvariantCulture);
            int keepDay = RaceHandoff.CalendarDay;
            if (day > 0) RaceHandoff.CalendarDay = day;
            int dress = (int)(day > 0 ? Seasons.Of(day) : Season.Fall);
            string dressName = Env("PSX_HAZE_DRESS", "");
            if (dressName.Length > 0) dress = dressName.ToLower() == "snow" ? Seasons.DressSnow : (int)(Season)System.Enum.Parse(typeof(Season), dressName, true);
            if (dressing != null)
            {
                typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic |
                                              System.Reflection.BindingFlags.Instance)?.Invoke(dressing, null);
                dressing.Apply(dress);
            }
            log.AppendLine($"day {day}, dress {Seasons.DressNames[dress]}");
            // The land's materials carry _Horizon from the builder; a scene built
            // before that flag existed gets it here, for this run only.
            int hl = LayerMask.NameToLayer(StageCulling.HorizonLayerName), flagged = 0, horizonRenderers = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.gameObject.layer != hl) continue;
                horizonRenderers++;
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.HasProperty("_Horizon") && m.GetFloat("_Horizon") < 0.5f) { m.SetFloat("_Horizon", 1f); flagged++; }
            }
            log.AppendLine($"horizon layer: {horizonRenderers} renderers, {flagged} material(s) flagged here");
            // PSX_HAZE_CANOPY=r,g,b: the far land's canopy colour for this run, for
            // setting PSXRacingBuilder.HorizonCanopy off a frame without a scene build.
            string canopyArg = Env("PSX_HAZE_CANOPY", "");
            if (canopyArg.Length > 0)
            {
                var tp = canopyArg.Split(',');
                var canopy = new Color(float.Parse(tp[0], CultureInfo.InvariantCulture), float.Parse(tp[1], CultureInfo.InvariantCulture), float.Parse(tp[2], CultureInfo.InvariantCulture), 1f);
                var done = new System.Collections.Generic.HashSet<Material>();
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (r.gameObject.layer == hl)
                        foreach (var m in r.sharedMaterials)
                            if (m != null && done.Add(m) && m.HasProperty("_HorizonCanopy"))
                            {
                                m.SetColor("_HorizonCanopy", canopy);
                                m.SetVector("_HorizonFade", new Vector4(PSXRacingBuilder.CanopyFromM, PSXRacingBuilder.CanopyByM, 1f / PSXRacingBuilder.HillMapM, PSXRacingBuilder.HillMapLum));
                            }
                log.AppendLine("far land canopy " + canopyArg + " on " + done.Count + " material(s)");
            }

            bool keepDyn = SkyModePrefs.Dynamic;
            int keepWeather = RaceHandoff.WeatherOverride;
            bool dyn = Env("PSX_HAZE_DYNSKY", "1") == "1";
            try
            {
                SkyModePrefs.Dynamic = dyn;
                RaceHandoff.WeatherOverride = -1;
                Seasons.Forced = null; Seasons.Invalidate();
                string skyWord = Env("PSX_HAZE_SKY", day > 0 ? "" : "clear");
                if (skyWord.Length > 0)
                {
                    var w = Seasons.Now;
                    w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f;
                    w.sky = skyWord == "partly" ? SkyKind.PartlyCloudy : skyWord == "overcast" ? SkyKind.Overcast : SkyKind.Clear;
                    Seasons.Forced = w;
                }
                string[] views = Env("PSX_HAZE_VIEWS", "chase,vista").Split(',');
                string[] variants = Env("PSX_HAZE_VARIANTS", "asis,fogmag,skygreen,nofade").Split(',');

                foreach (string hourName in Env("PSX_HAZE_HOURS", "dusk,night,noon").Split(','))
                {
                    int hour = -1;
                    for (int h = 0; h < TimeOfDay.Count; h++) if (TimeOfDay.At(h).name.ToLower() == hourName.Trim().ToLower()) hour = h;
                    if (hour < 0) continue;
                    TimeOfDay.Apply(hour, sun);
                    var wn = Seasons.Now;
                    log.AppendLine($"hour {hourName.Trim()}: sky {wn.sky}, precip {wn.precip}, fog {wn.fog}, headline {Seasons.CurrentWeather}");
                    if (dyn) { DynamicSky.PrimeClouds(path.GetPoint(path.Count / 2) + Vector3.up * 3f); DumpSky(Path.Combine(outDir, "hz_sky_" + hourName.Trim() + ".png")); }
                    // PSX_HAZE_SHOTS ("4608:chase,4608:under,7000:cockpit"): those poses
                    // alone, in place of every station x every view
                    var poses = new System.Collections.Generic.List<(string ms, string view)>();
                    string shotList = Env("PSX_HAZE_SHOTS", "");
                    if (shotList.Length > 0)
                    {
                        foreach (var sp in shotList.Split(','))
                        {
                            var kv = sp.Split(':');
                            if (kv.Length == 2) poses.Add((kv[0].Trim(), kv[1].Trim()));
                        }
                    }
                    else
                        foreach (string ms0 in Env("PSX_HAZE_M", "8400,9000,9600,10200").Split(','))
                            foreach (string view0 in views) poses.Add((ms0, view0));
                    foreach (var (ms, view) in poses)
                    {
                        float m = float.Parse(ms, CultureInfo.InvariantCulture);
                        int at = Mathf.Clamp(Mathf.RoundToInt(m / TrackCatalog.Spacing), 2, path.Count - 12);
                        Vector3 p = path.GetPoint(at);
                        Vector3 fwd = path.GetTangent(at); fwd.y = 0f; fwd.Normalize();
                        {
                            // chase: the game's camera behind the car. vista: 25 m up, level,
                            // turned 60 degrees off the road toward the open side - distance in frame.
                            Vector3 eye; Quaternion rot;
                            if (view == "chase" || view == "rev")
                            {
                                Vector3 f = view == "rev" ? -fwd : fwd;      // rev: driving the road the other way
                                eye = p - f * 8f + Vector3.up * 2.6f;
                                rot = Quaternion.LookRotation(p + Vector3.up * 0.8f + f * 20f - eye);
                            }
                            else if (view == "cockpit")
                            {
                                // the driver's eye: the right-hand lane, the left-hand seat,
                                // 1.15 m over the tarmac, looking down the road 30 m ahead
                                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                                float lane = path.roadWidth * 0.25f - 0.37f;
                                eye = p + right * lane + Vector3.up * (PSXRacingBuilder.RoadLift + 1.15f);
                                int ahead = Mathf.Clamp(at + 8, 0, path.Count - 1);
                                Vector3 pa = path.GetPoint(ahead);
                                Vector3 fa = path.GetTangent(ahead); fa.y = 0f; fa.Normalize();
                                rot = Quaternion.LookRotation(pa + Vector3.Cross(Vector3.up, fa) * lane + Vector3.up * (PSXRacingBuilder.RoadLift + 0.9f) - eye);
                            }
                            else if (view == "under")
                            {
                                // A deck from below: 34 m out on the open side, 9 m under the
                                // road and 30 m back, looking along the span at its underside
                                // and piers (the lidar re-bake put up to 23 m of air under Linn Cove).
                                Vector3 side = Quaternion.Euler(0f, OpenSide(path, at) * 90f, 0f) * fwd;
                                eye = p + side * 34f - Vector3.up * 9f - fwd * 30f;
                                rot = Quaternion.LookRotation(p - Vector3.up * 3f + fwd * 20f - eye);
                            }
                            else
                            {
                                eye = p + Vector3.up * 25f;
                                rot = Quaternion.LookRotation(Quaternion.Euler(0f, OpenSide(path, at) * 60f, 0f) * fwd);
                            }
                            foreach (string variant in variants)
                            {
                                // "old": the same scene as the player drew it before the
                                // horizon (far plane at the detail's edge, the end-of-view fade)
                                if (sc != null) { sc.horizonFarClip = variant == "old" ? 0f : keepHorizon; sc.Configure(cam); }
                                LookChoices.AerialForce = variant == "aerial" ? true : (bool?)null;
                                TimeOfDay.Apply(hour, sun);
                                var keepFlags = cam.clearFlags; var keepBg = cam.backgroundColor;
                                if (variant == "fogmag") globals.fogColor = new Color(1f, 0f, 1f, 1f);
                                if (variant == "nofade") { globals.fogNear = 1e7f; globals.fogFar = 2e7f; }
                                if (variant == "skygreen") { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0f, 1f, 0f, 1f); }
                                globals.Apply();
                                if (dyn) DynamicSky.PrimeClouds(eye);
                                cam.transform.SetPositionAndRotation(eye, rot);
                                SunShadows.RenderFor(cam);
                                if (forest != null) forest.DrawNow(cam);
                                string name = $"hz_{hourName.Trim()}_{ms.Trim()}_{view}_{variant}";
                                PSXScreenshotTool.ShotAs(cam, name, eye, rot);
                                cam.clearFlags = keepFlags; cam.backgroundColor = keepBg;
                                if (variant == "asis")
                                    log.AppendLine($"{name}: eye ({eye.x:0},{eye.y:0},{eye.z:0}) yaw {rot.eulerAngles.y:0} pitch {rot.eulerAngles.x:0.0} fov {cam.fieldOfView:0}; fade {globals.fogNear:0}..{globals.fogFar:0} m colour ({globals.fogColor.r:0.000},{globals.fogColor.g:0.000},{globals.fogColor.b:0.000}); ambient ({globals.ambient.r:0.000},{globals.ambient.g:0.000},{globals.ambient.b:0.000}); weather {Seasons.CurrentWeather}");
                            }
                        }
                    }
                }
            }
            finally
            {
                LookChoices.AerialForce = null;
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather;
                RaceHandoff.CalendarDay = keepDay;
                SkyModePrefs.Dynamic = keepDyn;
                TimeOfDay.FarEdgeM = 0f; TimeOfDay.HorizonM = 0f;
            }
            File.WriteAllText(Path.Combine(outDir, "hz_probe.txt"), log.ToString());
            Debug.Log("[HazeProbe] written to " + outDir + "\n" + log);
        }

        /// <summary>The dynamic sky's own panorama (the whole dome, no land): what the sky alone paints.</summary>
        static void DumpSky(string file)
        {
            var rt = Shader.GetGlobalTexture("_PSXSkyTex") as RenderTexture;
            if (rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>+1 if the land falls away to the road's right at this station, -1 to its left.</summary>
        static float OpenSide(TrackPath path, int at)
        {
            Vector3 p = path.GetPoint(at);
            Vector3 fwd = path.GetTangent(at); fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float yr = GroundY(p + right * 60f, p.y), yl = GroundY(p - right * 60f, p.y);
            return yr <= yl ? 1f : -1f;
        }

        static float GroundY(Vector3 at, float fallback)
        {
            return Physics.Raycast(at + Vector3.up * 400f, Vector3.down, out var hit, 900f) ? hit.point.y : fallback;
        }
    }
}
