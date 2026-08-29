using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// What is actually IN the pizzeria pack, and how big is it.
    ///
    /// Written because the first pass at the delivery job assumed the shop had
    /// no interior — from the wrong model entirely, and without ever standing
    /// it up to look. The pack has a full front-of-house, a kitchen and a walk-
    /// in. Nothing here is guessed: the room the player spawns in, where the
    /// counter is and which way the door faces all have to come out of a
    /// measurement, because the one thing this scene cannot survive is the
    /// player being seated inside a wall.
    ///
    /// Writes PSXRacing_pizzeria.txt.
    /// </summary>
    public static class PizzeriaProbe
    {
        const string Dir = "Assets/PSXRacing/Art/LifeSim/PizzeriaScene";

        public static void Run()
        {
            var sb = new StringBuilder();
            try { Probe(sb); }
            catch (System.Exception e) { sb.AppendLine("PROBE THREW: " + e); }
            File.WriteAllText("PSXRacing_pizzeria.txt", sb.ToString());
            Debug.Log("PIZZERIA PROBE\n" + sb);
        }

        static void Probe(StringBuilder sb)
        {
            foreach (var file in new[] { "Pizzeria_Scene", "Pizzeria_Props" })
            {
                string path = Dir + "/" + file + ".fbx";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.AppendLine("=== " + file + " ===");
                if (prefab == null) { sb.AppendLine("  MISSING at " + path); continue; }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;

                var rs = inst.GetComponentsInChildren<MeshRenderer>(true);
                sb.AppendLine("  renderers " + rs.Length);
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    sb.AppendLine("  bounds size " + b.size.ToString("0.00") +
                                  "  min " + b.min.ToString("0.00") +
                                  "  max " + b.max.ToString("0.00"));
                }

                int tris = 0;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                sb.AppendLine("  triangles " + tris);

                // Every top-level child with its own size, which is the map of
                // the place: it names the rooms, the counter and the fittings.
                sb.AppendLine("  top-level children:");
                foreach (Transform t in inst.transform)
                {
                    var cr = t.GetComponentsInChildren<MeshRenderer>(true);
                    if (cr.Length == 0) { sb.AppendLine("    " + t.name + "  (no renderers)"); continue; }
                    var cb = cr[0].bounds;
                    foreach (var r in cr) cb.Encapsulate(r.bounds);
                    sb.AppendLine("    " + t.name + "  size " + cb.size.ToString("0.00") +
                                  "  centre " + cb.center.ToString("0.00") +
                                  "  (" + cr.Length + " renderers)");
                }

                // Named parts worth finding later: doors set the scale, the
                // counter is where the order sits, the oven says which end the
                // kitchen is.
                var interesting = new List<string>();
                foreach (var r in rs)
                {
                    string n = r.name.ToLowerInvariant();
                    if (n.Contains("door") || n.Contains("counter") || n.Contains("oven") ||
                        n.Contains("floor") || n.Contains("wall") || n.Contains("table") ||
                        n.Contains("box") || n.Contains("window"))
                        interesting.Add("      " + r.name + "  size " + r.bounds.size.ToString("0.00") +
                                        "  centre " + r.bounds.center.ToString("0.00"));
                }
                sb.AppendLine("  named parts (" + interesting.Count + "):");
                interesting.Sort();
                for (int i = 0; i < Mathf.Min(interesting.Count, 45); i++) sb.AppendLine(interesting[i]);

                Object.DestroyImmediate(inst);
            }

            sb.AppendLine();
            sb.AppendLine("A real shop door is 2.03 m tall and a counter is 1.05 m.");
            sb.AppendLine("The player is " + FootRig.StandingH.ToString("0.00") +
                          " m with eyes at " + FootRig.EyeH.ToString("0.00") + " m.");
        }
    }
}
