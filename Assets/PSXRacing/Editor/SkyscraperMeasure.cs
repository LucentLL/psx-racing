using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// One-shot: report the real-world size of every model in the skyscraper
    /// pack so the CityProps footprints can be written from measurements
    /// instead of from guesses.
    ///
    /// The footprints in <see cref="PSXRacing.City.CityProps"/> are load-bearing
    /// — the placement pass tests them against road corridors, water and its own
    /// occupancy grid before it commits a lot — so a tower whose table row says
    /// 20 m and whose mesh is 60 m gets planted through a motorway. Every other
    /// row in that table came from a pass like this one; this is the pass.
    /// </summary>
    public static class SkyscraperMeasure
    {
        const string Dir = "Assets/PSXRacing/Art/LifeSim/Skyscrapers";

        [MenuItem("PSX Racing/Measure Skyscrapers")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var guids = AssetDatabase.FindAssets("t:GameObject", new[] { Dir });
            sb.AppendLine("model, width(X), depth(Z), height(Y), pivotY, centreOffXZ");
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith(".fbx")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                // Instantiated rather than read off the asset: an FBX's own
                // bounds are in ITS units and before the importer's scale
                // factor, which is exactly the number that would be wrong.
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.identity;

                bool any = false;
                var b = new Bounds();
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (any)
                    sb.AppendLine(string.Format(
                        "{0}, {1:0.00}, {2:0.00}, {3:0.00}, {4:0.00}, ({5:0.00} {6:0.00})",
                        Path.GetFileNameWithoutExtension(path),
                        b.size.x, b.size.z, b.size.y, b.min.y, b.center.x, b.center.z));
                else
                    sb.AppendLine(Path.GetFileNameWithoutExtension(path) + ", NO RENDERERS");

                Object.DestroyImmediate(inst);
            }

            string outPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "skyscraper_sizes.txt");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[Skyscrapers] measured " + guids.Length + " assets -> " + outPath +
                      "\n" + sb);
        }
    }
}
