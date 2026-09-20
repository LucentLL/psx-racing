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
                    PSXScreenshotTool.Shot(cam, "forest_" + id + "_" + dressName + "_" + s, eye,
                                           Quaternion.LookRotation(look - eye));
                }
            }
            if (dressing != null) dressing.Apply((int)Season.Fall);
            Debug.Log("[ForestShots] written to " + outDir);
        }
    }
}
