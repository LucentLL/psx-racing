using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE NIGHT CITY IN ONE JOB (owner, 2026-10-26: "Uptown buildings are
    /// still very gray and windows are washed out yellow vs blacks and vibrant
    /// yellow of NFS screenshots I've shared"). The far shots (CityFarShots:
    /// I-277 1 km south, 45 m over I-77 3 km north) at night, dusk and noon,
    /// then the street-level eyes (CityPreview.RunEyePlay: S Tryon by the
    /// Square, W Trade inbound by I-77, I-277 by E 4th St) at night and noon,
    /// every PNG suffixed with PSX_NC_TAG; PSX_NC_GLES=1 also compiles PSX/Lit,
    /// CarPaint and the skies for WebGL 2 (VolCloudsCheck). One editor launch
    /// instead of three. Screenshots\far_*_TAG.png, Screenshots\eye_*_TAG.png,
    /// Screenshots\nightcity_TAG.txt.
    /// Headless: -executeMethod PSXRacing.EditorTools.NightCityShots.RunBatch
    /// </summary>
    public static class NightCityShots
    {
        const string FarSpots = "i277s10,high45,i77s16";
        const string FarHours = "night,dusk,noon";
        const string EyeViews = "jb_tryon_eye,ht_trade_i77,i277b_2745";
        const string EyeHours = "night,noon";

        public static void RunBatch()
        {
            int code = 0;
            try { code = Run(); }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static int Run()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_NC_TAG");
            tag = string.IsNullOrEmpty(tag) ? "nc" : tag;
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);
            var log = new System.Text.StringBuilder();

            System.Environment.SetEnvironmentVariable("PSX_FAR_SPOTS", Env("PSX_NC_FAR_SPOTS", FarSpots));
            System.Environment.SetEnvironmentVariable("PSX_FAR_HOURS", Env("PSX_NC_FAR_HOURS", FarHours));
            System.Environment.SetEnvironmentVariable("PSX_FAR_TAG", tag);
            CityFarShots.Run();
            log.Append("far: ").Append(Read(Path.Combine(dir, "far_shots.txt"))).Append('\n');

            System.Environment.SetEnvironmentVariable("PSX_EYE_VIEWS", Env("PSX_NC_EYE_VIEWS", EyeViews));
            System.Environment.SetEnvironmentVariable("PSX_EYE_HOURS", Env("PSX_NC_EYE_HOURS", EyeHours));
            foreach (var f in Directory.GetFiles(dir, "eye_*.png"))
                if (!Path.GetFileNameWithoutExtension(f).EndsWith("_" + tag)) File.Delete(f);
            CityPreview.RunEyePlay();
            foreach (var f in Directory.GetFiles(dir, "eye_*.png"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (n.EndsWith("_" + tag)) continue;
                string to = Path.Combine(dir, n + "_" + tag + ".png");
                if (File.Exists(to)) File.Delete(to);
                File.Move(f, to);
                log.Append("eye: ").Append(n).Append('_').Append(tag).Append('\n');
            }

            int bad = 0;
            if (System.Environment.GetEnvironmentVariable("PSX_NC_GLES") == "1")
            {
                bad = VolCloudsCheck.Run();
                log.Append("GLES3 compile (VolCloudsCheck): ").Append(bad == 0 ? "OK" : bad + " error(s)").Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, "nightcity_" + tag + ".txt"), log.ToString());
            Debug.Log("[NightCity] DONE " + tag + " | " + log.ToString().Replace("\n", " | "));
            return bad == 0 ? 0 : 1;
        }

        static string Env(string name, string def)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? def : v;
        }

        static string Read(string path) => File.Exists(path) ? File.ReadAllText(path).Replace("\n", " | ") : "(no log)";
    }
}
