using System.IO;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The world-in-the-paint check (CarReflections, 2026-10-12): the grid car
    /// at noon from three angles, once with the paint on the sky lookup alone
    /// and once with the six-view panorama, written side by side as
    /// Screenshots\refl_&lt;venue&gt;_&lt;view&gt;_off|on.png.
    /// PSX_REFL_VENUES=BlueRidge,CityCircuit picks the venues;
    /// PSX_REFL_HOUR=noon|sunset|dusk|night the hour.
    /// Headless: -executeMethod PSXRacing.EditorTools.ReflectionShots.Run
    /// </summary>
    public static class ReflectionShots
    {
        public static void Run()
        {
            string venues = System.Environment.GetEnvironmentVariable("PSX_REFL_VENUES");
            if (string.IsNullOrEmpty(venues)) venues = "BlueRidge,CityCircuit";
            string hourName = System.Environment.GetEnvironmentVariable("PSX_REFL_HOUR");
            if (string.IsNullOrEmpty(hourName)) hourName = "noon";
            int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hourName, System.StringComparison.OrdinalIgnoreCase));
            if (hour < 0) hour = TimeOfDay.Noon;
            foreach (var id in venues.Split(','))
            {
                var def = System.Array.Find(TrackCatalog.All, d => d.id == id.Trim());
                if (def == null) { Debug.LogWarning("[ReflShot] no venue " + id); continue; }
                if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogWarning("[ReflShot] " + id + " did not open"); continue; }
                var sun = GameObject.Find("Sun")?.GetComponent<Light>();
                TimeOfDay.Apply(hour, sun);
                var globals = Object.FindFirstObjectByType<PSXGlobals>();
                if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                var t = player.transform;
                Vector3 aim = t.position + Vector3.up * 0.7f;
                var views = new (string name, Vector3 local)[]
                {
                    ("front34", new Vector3(3.2f, 1.5f, 5.0f)),
                    ("side", new Vector3(5.0f, 1.2f, 0.4f)),
                    ("rear34", new Vector3(-3.0f, 2.2f, -5.2f)),
                    ("graze", new Vector3(2.4f, 0.9f, 4.2f)),
                };
                foreach (var v in views)
                {
                    Vector3 eye = t.TransformPoint(v.local);
                    var rot = Quaternion.LookRotation(aim - eye);
                    CarReflections.Off();
                    PSXScreenshotTool.ShotAs(cam, $"refl_{def.id}_{v.name}_off", eye, rot);
                    if (!CarReflections.CaptureNow(t, cam)) { Debug.LogError("[ReflShot] EnvPano shader missing"); return; }
                    PSXScreenshotTool.ShotAs(cam, $"refl_{def.id}_{v.name}_on", eye, rot);
                }
                // the panorama itself, for checking what the paint reads
                var pano = CarReflections.Panorama;
                if (pano != null)
                {
                    var prev = RenderTexture.active;
                    RenderTexture.active = pano;
                    var tex = new Texture2D(pano.width, pano.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, pano.width, pano.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
                    File.WriteAllBytes(Path.Combine(dir, $"refl_{def.id}_pano.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                CarReflections.Off();
                Debug.Log("[ReflShot] " + def.id + " done");
            }
        }
    }
}
