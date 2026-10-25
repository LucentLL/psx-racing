using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The DYNAMIC sky (PSXAtmosphere.cginc), photographed round the compass:
    /// for each SET (an hour and a sky), toward the sun (the moon at night),
    /// 90 degrees off it, away from it, and up at 40 degrees - once with the
    /// OLD clouds (the puffs, at the cover the old one-word weather gave) and
    /// once with the VOLUMETRIC clouds (2026-10-25), from 3 m over the player's
    /// grid slot. First, the GLES3 (WebGL 2) compile of every cloud pass
    /// (VolCloudsCheck). Screenshots\dsky_&lt;set&gt;_&lt;view&gt;_&lt;old|new&gt;.png;
    /// tools/sky/cloud_sheets.py lays them out.
    ///
    ///   tools\dynsky-shots.ps1 [-Venue EmeraldIsle] [-Sets "noon:partly,sunset:partly,noon:overcast,night:partly"]
    ///   PSX_DSKY_MODES=old,new (default) | new;  PSX_CLOUD_DAY=n picks another day's clouds
    /// </summary>
    public static class DynamicSkyShots
    {
        public static void Capture()
        {
            int compileFailures = VolCloudsCheck.Run();
            string venue = Env("PSX_SKY_VENUE", "EmeraldIsle");
            var def = System.Array.Find(TrackCatalog.All, d => d.id == venue);
            if (def == null) { Debug.LogError("[DynSky] no venue " + venue); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;

            string[] sets = Env("PSX_DSKY_SETS", "noon:partly,sunset:partly,noon:overcast,night:partly").Split(',');
            string[] modes = Env("PSX_DSKY_MODES", "old,new").Split(',');
            SkyModePrefs.Dynamic = true;
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = -1;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            Vector3 eye = player.transform.position + Vector3.up * 3.2f;
            try
            {
                foreach (string set in sets)
                {
                    string[] hs = set.Split(':');
                    int h = HourIndex(hs[0]);
                    if (h < 0) { Debug.LogError("[DynSky] no hour " + hs[0]); continue; }
                    string skyWord = hs.Length > 1 ? hs[1] : "partly";
                    Seasons.Forced = null;
                    Seasons.Invalidate();
                    var w = Seasons.Now;
                    w.fog = false;
                    w.sky = skyWord == "clear" ? SkyKind.Clear : skyWord == "overcast" || skyWord == "rain" ? SkyKind.Overcast
                          : skyWord == "storm" ? SkyKind.Storm : SkyKind.PartlyCloudy;
                    bool rain = skyWord == "rain" || skyWord == "storm";
                    w.precip = rain ? PrecipKind.Rain : PrecipKind.None;
                    w.precipRate = rain ? 1f : 0f;
                    // a third field picks the day (its clouds): noon:partly:1
                    if (hs.Length > 2 && int.TryParse(hs[2], out int cday)) w.day = cday;
                    Seasons.Forced = w;
                    var hour = TimeOfDay.At(h);
                    foreach (string mode in modes)
                    {
                        bool old = mode == "old";
                        DynamicSky.LegacyCover = old;
                        VolumetricClouds.Override = !old;
                        TimeOfDay.Apply(h, sun);
                        var globals = Object.FindFirstObjectByType<PSXGlobals>();
                        if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                        DynamicSky.PrimeClouds(eye);
                        if (!old)
                        {
                            // The map's frame cost, timed: 300 frames back to back, drained.
                            var bench = VolumetricClouds.Benchmark(eye, 300);
                            DynamicSky.PrimeClouds(eye);
                            Debug.Log($"[DynSky] {set}: volumetric {(VolumetricClouds.Active ? "ON" : "OFF (fallback)")}, sky {w.sky}; " +
                                      $"map frame {bench.x:0.000} ms wall (GPU bound), submit CPU {bench.y * 1000f:0} us ({SystemInfo.graphicsDeviceName})");
                        }

                        Vector3 toSun = sun != null ? -sun.transform.forward
                                                    : -(Quaternion.Euler(hour.sunEuler) * Vector3.forward);
                        Vector3 flat = new Vector3(toSun.x, 0f, toSun.z);
                        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
                        flat.Normalize();
                        Vector3 side = Vector3.Cross(Vector3.up, flat);
                        string tag = "dsky_" + set.Replace(':', '-');
                        // A fourth field "drive": first 3 s of the game's own
                        // per-frame update at 70 m/s (reprojection, wind), shot as
                        // mode "drive"; then the still march from the same eye.
                        Vector3 at = eye;
                        if (!old && hs.Length > 3 && hs[3] == "drive")
                        {
                            at = VolumetricClouds.Drive(eye, side * 70f, 180, 1f / 60f);
                            Views(cam, tag, "drive", at, flat, side);
                            DynamicSky.PrimeClouds(at);
                        }
                        Views(cam, tag, mode, at, flat, side);
                    }
                }
            }
            finally
            {
                Seasons.Forced = null;
                Seasons.Invalidate();
                DynamicSky.LegacyCover = false;
                VolumetricClouds.Override = null;
                RaceHandoff.WeatherOverride = oldWeather;
                SkyModePrefs.Dynamic = false;
            }
            Debug.Log("[DynSky] Screenshots written to Screenshots\\dsky_* (GLES3 compile failures: " + compileFailures + ")");
        }

        static void Views(Camera cam, string tag, string mode, Vector3 eye, Vector3 flat, Vector3 side)
        {
            PSXScreenshotTool.ShotAs(cam, tag + "_a_sunward_" + mode, eye, Quaternion.LookRotation(flat + Vector3.up * 0.14f));
            PSXScreenshotTool.ShotAs(cam, tag + "_b_side_" + mode, eye, Quaternion.LookRotation(side + Vector3.up * 0.14f));
            PSXScreenshotTool.ShotAs(cam, tag + "_c_away_" + mode, eye, Quaternion.LookRotation(-flat + Vector3.up * 0.14f));
            PSXScreenshotTool.ShotAs(cam, tag + "_d_up_" + mode, eye, Quaternion.LookRotation(-side + Vector3.up * 0.84f));
        }

        static int HourIndex(string name)
        {
            for (int h = 0; h < TimeOfDay.All.Length; h++)
                if (string.Equals(TimeOfDay.All[h].name, name, System.StringComparison.OrdinalIgnoreCase)) return h;
            return -1;
        }

        static string Env(string key, string fallback)
        {
            string v = System.Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
    }
}
