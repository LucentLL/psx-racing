using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The DYNAMIC sky (PSXAtmosphere.cginc), photographed round the compass:
    /// for each daylight hour, toward the sun, 90 degrees off it, away from it,
    /// and up at 40 degrees - the four views that answer "is the sunset glow on
    /// the sun's side only" (owner, 2026-09-27: "Sunset/sunrise shouldn't be
    /// 360") and "is there more than one kind of cloud" (same day: "cloud
    /// variance, not just one type/size"). Clear weather, from 3 m over the
    /// player's grid slot. Screenshots\dsky_&lt;hour&gt;_&lt;view&gt;.png.
    ///
    ///   tools\dynsky-shots.ps1 [-Venue BlueRidge]
    /// </summary>
    public static class DynamicSkyShots
    {
        public static void Capture()
        {
            string venue = System.Environment.GetEnvironmentVariable("PSX_SKY_VENUE");
            if (string.IsNullOrEmpty(venue)) venue = "BlueRidge";
            var def = System.Array.Find(TrackCatalog.All, d => d.id == venue);
            if (def == null) { Debug.LogError("[DynSky] no venue " + venue); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;

            SkyModePrefs.Dynamic = true;
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            Vector3 eye = player.transform.position + Vector3.up * 3.2f;
            int[] hours = { 0, 1, 2, 3, 4 };
            try
            {
                foreach (int h in hours)
                {
                    var hour = TimeOfDay.At(h);
                    TimeOfDay.Apply(h, sun);
                    var globals = Object.FindFirstObjectByType<PSXGlobals>();
                    if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

                    Vector3 toSun = sun != null ? -sun.transform.forward
                                                : -(Quaternion.Euler(hour.sunEuler) * Vector3.forward);
                    Vector3 flat = new Vector3(toSun.x, 0f, toSun.z);
                    if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
                    flat.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, flat);
                    string tag = "dsky_" + h + "_" + hour.name.ToLower();
                    PSXScreenshotTool.ShotAs(cam, tag + "_a_sunward", eye, Quaternion.LookRotation(flat + Vector3.up * 0.14f));
                    PSXScreenshotTool.ShotAs(cam, tag + "_b_side", eye, Quaternion.LookRotation(side + Vector3.up * 0.14f));
                    PSXScreenshotTool.ShotAs(cam, tag + "_c_away", eye, Quaternion.LookRotation(-flat + Vector3.up * 0.14f));
                    PSXScreenshotTool.ShotAs(cam, tag + "_d_up", eye, Quaternion.LookRotation(-side + Vector3.up * 0.84f));
                }
            }
            finally
            {
                RaceHandoff.WeatherOverride = oldWeather;
                SkyModePrefs.Dynamic = false;
            }
            Debug.Log("[DynSky] Screenshots written to Screenshots\\dsky_*");
        }
    }
}
