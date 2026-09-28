using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The obstacle audit on already-built scenes (PSX_GHOST_VENUES, comma list),
    /// no rebuild - for asking what one import setting does to its ghost-barrier
    /// pass. PSX_GHOST_COMP=off first imports the prop model folders
    /// ReleaseBudget compresses with mesh compression OFF. Writes
    /// PSXRacing_ghostprobe.txt beside the project.
    /// </summary>
    public static class GhostProbe
    {
        static readonly string[] PropDirs =
        {
            "Assets/PSXRacing/Art/LifeSim", "Assets/PSXRacing/Art/Beach", "Assets/PSXRacing/Art/Bogue",
            "Assets/PSXRacing/Art/Roadside",
        };

        public static void Run()
        {
            var sb = new StringBuilder();
            if (System.Environment.GetEnvironmentVariable("PSX_GHOST_COMP") == "off")
            {
                int n = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:Model", PropDirs))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(p) is ModelImporter mi)) continue;
                    if (mi.meshCompression == ModelImporterMeshCompression.Off &&
                        mi.importTangents == ModelImporterTangents.CalculateMikk) continue;
                    mi.meshCompression = ModelImporterMeshCompression.Off;
                    mi.importTangents = ModelImporterTangents.CalculateMikk;
                    mi.SaveAndReimport();
                    n++;
                }
                sb.AppendLine("mesh compression OFF on " + n + " prop model(s)");
            }
            string ids = System.Environment.GetEnvironmentVariable("PSX_GHOST_VENUES") ?? "EmeraldIsle";
            foreach (var raw in ids.Split(','))
            {
                string id = raw.Trim();
                TrackCatalog.TrackDef def = null;
                foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
                if (def == null) { sb.AppendLine("no venue " + id); continue; }
                sb.AppendLine(TrackObstacleAudit.AuditForLab(def, "Assets/PSXRacing/Scenes/" + id + ".unity"));
                // Each prop with a Solid: what draws it and where, against the box.
                var town = GameObject.Find("Track/BeachTown");
                if (town == null) continue;
                int shown = 0;
                foreach (Transform prop in town.transform)
                {
                    var solid = prop.Find("Solid");
                    if (solid == null || shown++ > 12) continue;
                    var col = solid.GetComponent<Collider>();
                    sb.AppendLine($"  prop {prop.name} at {prop.position:F1} solid {(col != null ? col.bounds.ToString("F1") : "-")}");
                    foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        sb.AppendLine($"    renderer {r.name} enabled {r.enabled} active {r.gameObject.activeInHierarchy} " +
                                      $"mesh {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " readable " + mf.sharedMesh.isReadable + " verts " + mf.sharedMesh.vertexCount : "NONE")} " +
                                      $"bounds {r.bounds.ToString("F1")}");
                    }
                }
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_ghostprobe.txt"), sb.ToString());
        }
    }
}
