using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE OWNER'S FRAME, REBUILT: a car from the chase camera with the sun
    /// AHEAD of it, in white, blue, red and black.
    ///
    /// "Cars have this very unrealistic white glow... this is not how real
    /// cars look" arrived over a white saloon on Mount Mitchell, driving
    /// toward the light. None of the paint pass's reference shots could have
    /// shown it: they photograph the baked grid car (a mid-grey sheet) on a
    /// circuit whose sun is wherever it happens to be. A glow is a statement
    /// about how much of a car is at the top of the scale, which depends on
    /// the SHEET and on WHERE THE SUN IS — so this turns the car to put the
    /// sun dead ahead (every up-facing panel inside the old highlight's
    /// lobe), dead behind, and abeam, and dresses it in the liveries that
    /// clip first and last.
    ///
    /// Every frame is written twice — with the car and without it — so
    /// tools/paint/glow_stats.py can take the difference as the car's mask
    /// and MEASURE it: share of the car at or over 0.97, its 99th percentile,
    /// its mean. The eye on a dithered thumbnail is not an instrument.
    /// Ungraded (the paint is what is being judged); one graded frame per
    /// livery goes beside them for the look. Menu: PSX Racing/Check Paint Glow.
    /// </summary>
    public static class PaintGlowCheck
    {
        static readonly (string key, string skin)[] Liveries =
        {
            ("audi_saloon", "glacier_white"),
            ("audi_saloon", "sky_blue"),
            ("charger_69", "redline"),
            ("charger_69", "midnight_blue"),
            ("charger_69", "snow_white"),
        };

        [MenuItem("PSX Racing/Check Paint Glow")]
        public static void Run()
        {
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "psx_glow_*.png")) File.Delete(f);

            if (!PSXScreenshotTool.Open(TrackCatalog.At(0), out var cam, out var player)) return;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var body = player.GetComponentInChildren<CarBody>(true);
            if (sun == null || body == null) { Debug.LogError("[PaintGlow] no sun or no CarBody"); return; }

            int noon = 0;
            for (int h = 0; h < TimeOfDay.Count; h++)
                if (TimeOfDay.At(h).name.ToLower().Contains("noon")) noon = h;
            TimeOfDay.Apply(noon, sun);
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            // Where the sun is, in plan.
            Vector3 toSun = -sun.transform.forward; toSun.y = 0f;
            if (toSun.sqrMagnitude < 1e-4f) toSun = Vector3.forward;
            toSun.Normalize();

            var t = player.transform;
            var keepRot = t.rotation;
            var log = new System.Text.StringBuilder();
            foreach (var lv in Liveries)
            {
                var def = CarModelLibrary.Load(lv.key);
                if (def == null) { log.AppendLine("missing model " + lv.key); continue; }
                int skin = System.Array.FindIndex(def.skinNames, n => n != null &&
                           n.ToLower().Replace(' ', '_').Contains(lv.skin));
                if (skin < 0) { log.AppendLine("missing skin " + lv.skin + " on " + lv.key + " (has: " + string.Join(", ", def.skinNames) + ")"); continue; }
                body.Apply(def, skin);

                foreach (var (tag, yaw) in new[] { ("sunahead", 0f), ("sunbehind", 180f), ("sunabeam", 90f) })
                {
                    t.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * toSun, Vector3.up);
                    Vector3 eye = t.position - t.forward * 6.2f + Vector3.up * 2.5f;
                    var rot = Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 14f - eye);
                    string name = "glow_" + lv.key + "_" + lv.skin + "_" + tag;

                    System.Environment.SetEnvironmentVariable("PSX_GRADE", "0");
                    PSXScreenshotTool.Shot(cam, name, eye, rot);
                    SetCarVisible(player, false);
                    PSXScreenshotTool.Shot(cam, name + "_nocar", eye, rot);
                    SetCarVisible(player, true);
                    if (tag == "sunahead")
                    {
                        System.Environment.SetEnvironmentVariable("PSX_GRADE", "1");
                        PSXScreenshotTool.Shot(cam, name + "_graded", eye, rot);
                    }
                }
            }
            System.Environment.SetEnvironmentVariable("PSX_GRADE", null);
            t.rotation = keepRot;
            log.AppendLine("PAINT GLOW SHOTS WRITTEN");
            File.WriteAllText(Path.Combine(outDir, "psx_glow.txt"), log.ToString());
            Debug.Log("[PaintGlow] " + log);
        }

        /// <summary>Only what wears the paint: the shell and the wheels. The
        /// blob shadow and the lamp glows stay, so the difference of the two
        /// frames is the PAINT's footprint and nothing else's.</summary>
        static void SetCarVisible(GameObject car, bool on)
        {
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                var m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name == "PSX/CarPaint") r.enabled = on;
            }
        }
    }
}
