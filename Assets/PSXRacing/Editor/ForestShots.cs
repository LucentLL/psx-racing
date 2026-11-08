using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHAT THE FOREST LOOKS LIKE FROM THE DRIVER'S SEAT.
    ///
    /// "Notice how thick the trees are. Trees in this game are too sparse" is
    /// a statement about a view — a driver's eye a metre and a half up,
    /// looking along the road — and the reference shots this project already
    /// takes are of the CAR (a chase camera on the grid). So: a handful of
    /// stations spread down a stage, the camera on the centreline at eye
    /// height looking where the road goes, in the dresses that differ most
    /// (summer's full canopy, winter's bare one, and the fall the game was
    /// baked in), through the display material like every other shot.
    ///
    /// Venue: PSX_TREE_VENUE (default MtMitchell). Menu: PSX Racing/Capture
    /// Forest Views. Writes Screenshots/psx_forest_*.png.
    /// </summary>
    public static class ForestShots
    {
        [MenuItem("PSX Racing/Capture Forest Views")]
        public static void Capture()
        {
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "psx_forest_*.png")) File.Delete(f);

            string id = System.Environment.GetEnvironmentVariable("PSX_TREE_VENUE");
            if (string.IsNullOrEmpty(id)) id = "MtMitchell";
            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) return;

            var path = Object.FindFirstObjectByType<TrackPath>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var dressing = Object.FindFirstObjectByType<SeasonDress>();
            if (path == null || path.Count < 50) { Debug.LogError("[ForestShots] no path"); return; }

            int noon = 0;
            for (int h = 0; h < TimeOfDay.Count; h++)
                if (TimeOfDay.At(h).name.ToLower() == "noon") noon = h;
            if (sun != null) TimeOfDay.Apply(noon, sun);
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            // SeasonDress learns its materials in Awake, which edit mode never
            // calls: without this every Apply below swaps nothing, and all
            // three "dresses" come back as the fall the scene was baked in.
            if (dressing != null)
                typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic |
                                              System.Reflection.BindingFlags.Instance)?.Invoke(dressing, null);

            // Mesh broadleaf trees (MeshForest, PC scene pass): every shot
            // twice, "_mesh" and "_cards", for the owner's A/B.
            var meshForest = Object.FindFirstObjectByType<MeshForest>();
            Debug.Log("[ForestShots] mesh forest: " + (meshForest != null ? "yes" : "no"));
            var dresses = new[] { ((int)Season.Summer, "summer"), ((int)Season.Winter, "winter"), ((int)Season.Fall, "fall") };
            var stations = new[] { 0.12f, 0.3f, 0.47f, 0.66f, 0.85f };
            foreach (var (dress, dressName) in dresses)
            {
                if (dressing != null) dressing.Apply(dress);
                // Summer at every station; the other two dresses at two of them.
                for (int s = 0; s < stations.Length; s++)
                {
                    if (dressName != "summer" && s != 1 && s != 3) continue;
                    int at = Mathf.Clamp(Mathf.RoundToInt(stations[s] * path.Count), 2, path.Count - 12);
                    Vector3 eye = path.GetPoint(at) + Vector3.up * 1.5f;
                    Vector3 look = path.GetPoint(at + 10) + Vector3.up * 1.2f;
                    var rot = Quaternion.LookRotation(look - eye);
                    if (meshForest == null)
                    {
                        PSXScreenshotTool.Shot(cam, "forest_" + id + "_" + dressName + "_" + s, eye, rot);
                        continue;
                    }
                    cam.transform.SetPositionAndRotation(eye, rot);
                    meshForest.DrawNow(cam);
                    PSXScreenshotTool.Shot(cam, "forest_" + id + "_" + dressName + "_" + s + "_mesh", eye, rot);
                    meshForest.CardsNow();
                    PSXScreenshotTool.Shot(cam, "forest_" + id + "_" + dressName + "_" + s + "_cards", eye, rot);
                }
            }
            // THE TALLEST ROAD CUT, from the road beside it (the rock relief,
            // owner 2026-11-08: "The rock faces seem very flat in game").
            {
                MeshRenderer best = null;
                foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if (mr.name.StartsWith("Bank") && !mr.name.StartsWith("BankTop") &&
                        (best == null || mr.bounds.size.y > best.bounds.size.y)) best = mr;
                if (best != null)
                {
                    Vector3 c = best.bounds.center;
                    int at = 2; float bd = float.MaxValue;
                    for (int q = 2; q < path.Count - 12; q++)
                    {
                        float dq = (path.GetPoint(q) - c).sqrMagnitude;
                        if (dq < bd) { bd = dq; at = q; }
                    }
                    Vector3 eye = path.GetPoint(Mathf.Max(2, at - 4)) + Vector3.up * 1.5f;
                    var rot = Quaternion.LookRotation(Vector3.Lerp(path.GetPoint(at + 4), c, 0.55f) - eye);
                    foreach (var (dress, dn) in new[] { ((int)Season.Summer, "summer"), ((int)Season.Fall, "fall") })
                    {
                        if (dressing != null) dressing.Apply(dress);
                        cam.transform.SetPositionAndRotation(eye, rot);
                        if (meshForest != null) meshForest.DrawNow(cam);
                        PSXScreenshotTool.Shot(cam, "forest_" + id + "_cut_" + dn, eye, rot);
                    }
                    Debug.Log($"[ForestShots] tallest cut {best.name}: {best.bounds.size.y:0.0} m");
                }
            }
            // A FENCE RUN (PSXRacingBuilder.Fences), from the road beside it
            var fm = GameObject.Find("FenceShotMarker");
            if (fm != null)
            {
                Vector3 c = fm.transform.position;
                int at = 2; float bd = float.MaxValue;
                for (int q = 2; q < path.Count - 12; q++)
                {
                    float dq = (path.GetPoint(q) - c).sqrMagnitude;
                    if (dq < bd) { bd = dq; at = q; }
                }
                Vector3 eye = path.GetPoint(Mathf.Max(2, at - 6)) + Vector3.up * 1.5f;
                var rot = Quaternion.LookRotation(Vector3.Lerp(path.GetPoint(at + 6), c, 0.35f) + Vector3.up * 0.5f - eye);
                foreach (var (dress, dn) in new[] { ((int)Season.Summer, "summer"), ((int)Season.Fall, "fall") })
                {
                    if (dressing != null) dressing.Apply(dress);
                    cam.transform.SetPositionAndRotation(eye, rot);
                    if (meshForest != null) meshForest.DrawNow(cam);
                    PSXScreenshotTool.Shot(cam, "forest_" + id + "_fence_" + dn, eye, rot);
                }
            }
            // NIGHT, summer, station 1: the forest must darken with the world
            // (owner 2026-11-08: the first mesh trees "glow at night").
            if (meshForest != null && sun != null)
            {
                foreach (var hourName in new[] { "dusk", "night" })
                {
                int night = -1;
                for (int h = 0; h < TimeOfDay.Count; h++)
                    if (TimeOfDay.At(h).name.ToLower() == hourName) night = h;
                if (night >= 0)
                {
                    TimeOfDay.Apply(night, sun);
                    if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                    if (dressing != null) dressing.Apply((int)Season.Fall);
                    int at = Mathf.Clamp(Mathf.RoundToInt(stations[1] * path.Count), 2, path.Count - 12);
                    Vector3 eye = path.GetPoint(at) + Vector3.up * 1.5f;
                    var rot = Quaternion.LookRotation(path.GetPoint(at + 10) + Vector3.up * 1.2f - eye);
                    cam.transform.SetPositionAndRotation(eye, rot);
                    meshForest.DrawNow(cam);
                    PSXScreenshotTool.Shot(cam, "forest_" + id + "_" + hourName + "_1_mesh", eye, rot);
                    meshForest.CardsNow();
                    PSXScreenshotTool.Shot(cam, "forest_" + id + "_" + hourName + "_1_cards", eye, rot);
                    TimeOfDay.Apply(noon, sun);
                }
                }
            }
            if (dressing != null) dressing.Apply((int)Season.Fall);
            Debug.Log("[ForestShots] written to " + outDir);
        }
    }
}
