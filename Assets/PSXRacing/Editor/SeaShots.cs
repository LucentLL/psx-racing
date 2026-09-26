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
            // PSX_SKY_DYNAMIC=1: the computed sky (SkyModePrefs), frames named
            // psx_sea_dyn_*; the pref is put back afterwards.
            bool dyn = System.Environment.GetEnvironmentVariable("PSX_SKY_DYNAMIC") == "1";
            bool wasDyn = SkyModePrefs.Dynamic;
            SkyModePrefs.Dynamic = dyn;
            prefix = dyn ? "sea_dyn_" : "sea_";
            foreach (var f in Directory.GetFiles(outDir, "psx_" + prefix + "*.png")) File.Delete(f);
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
                    Shot(cam, key + "_up", eye, -toLight, 35f);
                }
                Debug.Log("[SeaShots] " + id + " crest wp " + crest + " at " + at);

                // THE BEACH: a station with sand at the waterline beside it -
                // ground 0.4-1.2 m over the sea 10-40 m off the road - shot at
                // noon and sunset from above the shoulder, looking at it.
                var seaGo = GameObject.Find("Sea");
                if (seaGo == null) continue;
                float seaY = seaGo.GetComponent<Renderer>().bounds.center.y;
                int noon = -1, sunsetH = -1;
                for (int h = 0; h < TimeOfDay.Count; h++)
                {
                    string nm = TimeOfDay.At(h).name.ToLower();
                    if (nm == "noon") noon = h;
                    if (nm == "sunset") sunsetH = h;
                }
                int beaches = 0;
                for (int i = 10; i < path.Count - 10 && beaches < 2; i += 6)
                {
                    Vector3 pt = path.GetPoint(i);
                    Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                    foreach (float side in new[] { 1f, -1f })
                    {
                        bool found = false;
                        for (float off = 10f; off <= 40f && !found; off += 5f)
                        {
                            Vector3 probe = pt + r * (side * off) + Vector3.up * 60f;
                            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 120f)) continue;
                            float over = hit.point.y - seaY;
                            if (over < 0.4f || over > 1.2f) continue;
                            if (hit.collider.gameObject.layer == 8) continue;   // a deck or the road, not the beach
                            found = true;
                            Vector3 beachEye = pt + Vector3.up * 3.5f;
                            Vector3 flat = hit.point - beachEye; flat.y = 0f;
                            float pitch = -Mathf.Atan2(beachEye.y - hit.point.y, flat.magnitude) * Mathf.Rad2Deg;
                            foreach (var (hi, hn) in new[] { (noon, "noon"), (sunsetH, "sunset") })
                            {
                                if (hi < 0) continue;
                                TimeOfDay.Apply(hi, sun);
                                if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                                Shot(cam, id + "_beach" + beaches + "_" + hn, beachEye, flat.normalized, pitch);
                            }
                            Debug.Log("[SeaShots] " + id + " beach " + beaches + " at wp " + i + ", sand " +
                                      over.ToString("0.00") + " m over the sea");
                            beaches++;
                        }
                        if (found) break;
                    }
                }
            }
            Debug.Log("[SeaShots] written to " + outDir);
            SkyModePrefs.Dynamic = wasDyn;
        }

        static string prefix = "sea_";

        static void Shot(Camera cam, string name, Vector3 eye, Vector3 flatDir, float pitchDeg)
        {
            var rot = Quaternion.LookRotation(flatDir) * Quaternion.Euler(-pitchDeg, 0f, 0f);
            PSXScreenshotTool.Shot(cam, prefix + name, eye, rot);
        }
    }
}
