using System.IO;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FAR HAZE v2 (owner 2026-11-23, PC60: a far mountain "glowing white
    /// like a white dwarf star" under a rain sky). What fails without a
    /// picture: a fog that is not held to the sky behind it, a weather or hour
    /// whose cap lets the haze outshine its sky, the switch not restoring the
    /// old picture. The picture itself is FarHazeCheck's (tools: farhaze_check.txt,
    /// far land under the skyline against the sky over it, every venue, weather
    /// and hour); its last report in this project is held to its limit here.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void FarHazeSelfTest()
        {
            string sh = Path.Combine(Application.dataPath, "PSXRacing", "Shaders");
            foreach (var f in new[] { "PSXLit.shader", "PSXLitTransparent.shader", "PSXWater.shader", "PSXWaterHD.cginc", "PSXCarPaint.shader", "PSXCabin.shader", "PSXCarLens.shader" })
            {
                string src = File.ReadAllText(Path.Combine(sh, f));
                Check(src.Contains("PSXFarHazeRing("), "far haze: " + f + "'s fog is held to the sky behind it");
            }
            Check(File.ReadAllText(Path.Combine(sh, "PSXDecal.shader")).Contains("PSXFarHazeScale("),
                  "far haze: PSX/Decal's marks fade no brighter than the road under them");
            Check(File.ReadAllText(Path.Combine(sh, "PSXSky.shader")).Contains("PSXFarHazeKeep("),
                  "far haze: PSX/Sky's horizon band is held to the dome over it");

            bool allCapped = true, onEverywhere = true;
            string worst = "";
            for (int h = 0; h < TimeOfDay.Count; h++)
                foreach (Weather w in System.Enum.GetValues(typeof(Weather)))
                    foreach (bool dyn in new[] { false, true })
                    {
                        var v = TimeOfDay.FarHazeVector(h, w, dyn);
                        if (v.x < 0.5f || v.w <= 0f || (v.z != 0f && v.z != 2f)) onEverywhere = false;
                        bool clearDay = w == Weather.Clear && (h == TimeOfDay.Morning || h == TimeOfDay.Noon || h == TimeOfDay.Afternoon);
                        if (v.y > (clearDay ? TimeOfDay.FarHazeCapClearDay : 1.0f) + 1e-4f) { allCapped = false; worst = TimeOfDay.At(h).name + " " + w + " cap " + v.y; }
                    }
            if (TimeOfDay.FarHaze2)
            {
                Check(onEverywhere, "far haze: on at every hour, in every weather, in both sky modes");
                Check(allCapped, "far haze: the haze is at most the sky's brightness in overcast, rain, fog, snow, dawn, dusk and night", worst);
            }
            else Skip("far haze caps: PSX_FARHAZE2=0 in this run");
            string keepEnv = System.Environment.GetEnvironmentVariable("PSX_FARHAZE2");
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_FARHAZE2", "0");
                Check(TimeOfDay.FarHazeVector(TimeOfDay.Noon, Weather.Rain, true) == Vector4.zero,
                      "far haze: PSX_FARHAZE2=0 hands the shaders nothing (the old picture)");
            }
            finally { System.Environment.SetEnvironmentVariable("PSX_FARHAZE2", keepEnv); }

            string rep = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "farhaze", "on", FarHazeCheck.ReportName);
            if (!File.Exists(rep)) { Skip("far haze picture: no FarHazeCheck report in this project (tools: farhaze-check)"); return; }
            string txt = File.ReadAllText(rep);
            int fails = 0;
            foreach (var line in txt.Split('\n')) if (line.TrimEnd().EndsWith("| FAIL")) fails++;
            Check(fails == 0 && txt.Contains("# FARHAZE PASS"),
                  "far haze picture: no far land brighter than " + FarHazeCheck.Limit + " x the sky over it (overcast, rain, fog, snow, dusk, night)",
                  fails + " frame(s) over");
        }
    }
}
