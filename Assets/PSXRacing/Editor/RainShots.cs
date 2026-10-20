using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FALLING RAIN, PHOTOGRAPHED (owner, 2026-10-19: "the raindrops that
    /// fall are too big and white. They should look more like the rain on the
    /// glass"). Charlotte on PSX_RAIN_ROAD (default I-277), the camera at a
    /// chase lens's height looking down the road, a settled rain over it
    /// (WeatherFx.Preview), at a rainy AFTERNOON and a rainy NIGHT, at
    /// PSX_RAIN_LINES (default 1080: the PC's HD picture). PNGs:
    /// Screenshots\rain_&lt;hour&gt;_&lt;PSX_RAIN_TAG&gt;.png, so a before and an
    /// after run sit side by side. Headless, not -nographics:
    /// -executeMethod PSXRacing.EditorTools.RainShots.Run
    /// </summary>
    public static class RainShots
    {
        public static void Run()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { Debug.LogError("[Rain] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[Rain] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            int lines = 1080;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_RAIN_LINES") ?? "1080", out lines);
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (output != null) output.height = lines;
            string road = System.Environment.GetEnvironmentVariable("PSX_RAIN_ROAD");
            if (string.IsNullOrEmpty(road)) road = "I-277";
            string tag = System.Environment.GetEnvironmentVariable("PSX_RAIN_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "now";

            // the road 1.2 km from uptown, looking along it
            int bei = -1; float bs = 0f, best = float.MaxValue;
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (e.link || e.name != road || e.length < 60f) continue;
                for (float s = 20f; s <= e.length - 30f; s += 10f)
                {
                    float d = Mathf.Abs(Vector2.Distance(e.PointAt(s), map.uptown) - 1200f);
                    if (d < best) { best = d; bei = ei; bs = s; }
                }
            }
            if (bei < 0) { Debug.LogError("[Rain] no " + road); return; }
            var be = map.edges[bei];
            Vector2 at = be.PointAt(bs), ahead = be.PointAt(bs + 25f);
            float y0 = be.YAt(bs), y1 = be.YAt(bs + 25f);
            var eye = new Vector3(at.x, y0 + 1.6f, at.y);
            var rot = Quaternion.LookRotation(new Vector3(ahead.x, y1 + 1.0f, ahead.y) - eye);

            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = (int)Weather.Rain;
            var go = new GameObject("~rainWorld");
            var world = go.AddComponent<CityWorld>();
            try
            {
                world.EnsureRing(eye, world.ring);
                foreach (var hourName in new[] { "AFTERNOON", "NIGHT" })
                {
                    int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name == hourName);
                    if (hour < 0) continue;
                    TimeOfDay.Apply(hour, sun);
                    NightGlow.PreviewAll(hourName == "NIGHT");
                    if (globals != null) globals.Apply();
                    cam.transform.SetPositionAndRotation(eye, rot);
                    WeatherFx.Preview(Weather.Rain, cam, 2.5f);
                    string f = "rain_" + hourName.ToLowerInvariant() + "_" + tag;
                    PSXScreenshotTool.ShotAs(cam, f, eye, rot);
                    Debug.Log($"[Rain] {f}: {road} at ({eye.x:0},{eye.z:0}), {lines} lines");
                }
            }
            finally
            {
                WeatherFx.PreviewClear();
                RaceHandoff.WeatherOverride = oldWeather;
                world.DropAll();
                Object.DestroyImmediate(go);
            }
            Debug.Log("[Rain] done");
        }
    }
}
