using System.IO;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SEA AND THE SKY OVER IT (the Tidewater pass, 2026-09-26): from the
    /// crest of a Bogue Banks bridge, at noon, sunset, dusk and night, looking
    /// toward the light (the glitter path), away from it (the horizon ring,
    /// the far shore), down onto the shallows, and - at night - up.
    ///
    /// Venues: PSX_SEA_VENUES (default LangstonBridge,AtlanticBeachBridge).
    /// Writes Screenshots/psx_sea_*.png.
    /// </summary>
    public static class SeaShots
    {
        public static void Capture()
        {
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "psx_sea_*.png")) File.Delete(f);
            string ids = System.Environment.GetEnvironmentVariable("PSX_SEA_VENUES");
            if (string.IsNullOrEmpty(ids)) ids = "LangstonBridge,AtlanticBeachBridge";

            foreach (var raw in ids.Split(','))
            {
                string id = raw.Trim();
                TrackCatalog.TrackDef def = null;
                foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
                if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) continue;
                var path = Object.FindFirstObjectByType<TrackPath>();
                var sun = GameObject.Find("Sun")?.GetComponent<Light>();
                var globals = Object.FindFirstObjectByType<PSXGlobals>();
                if (path == null || sun == null) { Debug.LogError("[SeaShots] no path or sun in " + id); continue; }

                // The crest: the highest station, where the deck looks furthest.
                int crest = 0;
                for (int i = 0; i < path.Count; i++)
                    if (path.GetPoint(i).y > path.GetPoint(crest).y) crest = i;
                Vector3 at = path.GetPoint(crest);
                Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(crest)).normalized;
                Vector3 eye = at + Vector3.up * 4.2f;   // over the parapet

                foreach (var hourName in new[] { "noon", "sunset", "dusk", "night" })
                {
                    int hour = -1;
                    for (int h = 0; h < TimeOfDay.Count; h++)
                        if (TimeOfDay.At(h).name.ToLower() == hourName) hour = h;
                    if (hour < 0) continue;
                    TimeOfDay.Apply(hour, sun);
                    if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

                    Vector3 toLight = -sun.transform.forward; toLight.y = 0f;
                    if (toLight.sqrMagnitude < 1e-4f) toLight = right;
                    toLight.Normalize();
                    string key = id + "_" + hourName;
                    Shot(cam, key + "_tolight", eye, toLight, -7f);
                    Shot(cam, key + "_away", eye, -toLight, -5f);
                    Shot(cam, key + "_down", eye + right * 4f, right, -28f);
                    if (hourName == "night" || hourName == "dusk") Shot(cam, key + "_up", eye, -toLight, 35f);
                }
                Debug.Log("[SeaShots] " + id + " crest wp " + crest + " at " + at);
            }
            Debug.Log("[SeaShots] written to " + outDir);
        }

        static void Shot(Camera cam, string name, Vector3 eye, Vector3 flatDir, float pitchDeg)
        {
            var rot = Quaternion.LookRotation(flatDir) * Quaternion.Euler(-pitchDeg, 0f, 0f);
            PSXScreenshotTool.Shot(cam, "sea_" + name, eye, rot);
        }
    }
}
