using System.IO;
using System.Text;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The terrain audit on already-built scenes (PSX_TERRAIN_VENUES, comma
    /// list), no rebuild - with PSX_LATTICE_TRACE ("836R") it prints one
    /// half-section's shoulder-over-lattice probe row. Writes
    /// PSXRacing_terrainspot.txt beside the project.
    /// </summary>
    public static class TerrainSpotProbe
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            string ids = System.Environment.GetEnvironmentVariable("PSX_TERRAIN_VENUES") ?? "LittleSwitzerland";
            foreach (var raw in ids.Split(','))
            {
                string id = raw.Trim();
                TrackCatalog.TrackDef def = null;
                foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
                if (def == null) { sb.AppendLine("no venue " + id); continue; }
                TerrainAudit.AuditOne(def, sb);
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_terrainspot.txt"), sb.ToString());
        }
    }
}
