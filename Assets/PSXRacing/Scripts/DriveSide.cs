using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// WHICH SIDE EACH CAR IS DRIVEN FROM (owner, 2026-10-16): "RHD when real
    /// car is RHD. Same for LHD. Some cars can be either, like EG Civic or NSX.
    /// Also, aren't British cars RHD?"
    ///
    /// One row per catalog car, decided for THAT row's market variant by
    /// tools/cockpit/drive_side.py (Japanese-market names and trims RHD,
    /// US-market names LHD, British makes RHD, the rest of Europe and America
    /// LHD; the cars sold both ways flagged for the owner). The owner edits
    /// Docs/drive_side.csv; `py tools/cockpit/drive_side.py --sync` copies it
    /// here, Resources/DriveSide.csv, which this reads. A car not in the table
    /// (a harness's, a new row before the script ran) is LHD, the side every
    /// cabin was before.
    /// </summary>
    public static class DriveSide
    {
        public const string ResourcePath = "DriveSide";

        static Dictionary<string, bool> rhd;

        /// <summary>The table: catalog id -> right-hand drive.</summary>
        public static Dictionary<string, bool> Table
        {
            get
            {
                if (rhd != null) return rhd;
                rhd = new Dictionary<string, bool>();
                var ta = Resources.Load<TextAsset>(ResourcePath);
                if (ta == null) { Debug.LogWarning("[DriveSide] Resources/" + ResourcePath + ".csv missing - every car LHD"); return rhd; }
                bool header = true;
                foreach (var line in ta.text.Split('\n'))
                {
                    if (header) { header = false; continue; }
                    var f = SplitCsv(line);
                    if (f.Count >= 3 && f[0].Length > 0) rhd[f[0]] = f[2].Trim() == "RHD";
                }
                return rhd;
            }
        }

        /// <summary>Right-hand drive for a catalog id (false when unknown).</summary>
        public static bool IsRHD(string specId) =>
            !string.IsNullOrEmpty(specId) && Table.TryGetValue(specId, out bool r) && r;

        /// <summary>Right-hand drive for a car's own catalog row.</summary>
        public static bool IsRHD(CarController car) =>
            Override ?? (car != null && car.activeSpec != null && IsRHD(car.activeSpec.id));

        /// <summary>A preview tool's say (the cockpit shots photograph both
        /// sides on one car); null in the game.</summary>
        public static bool? Override;

        /// <summary>One CSV line: commas, double-quoted fields with "" inside.</summary>
        public static List<string> SplitCsv(string line)
        {
            var f = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') q = false;
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { f.Add(sb.ToString()); sb.Length = 0; }
                else if (c != '\r') sb.Append(c);
            }
            f.Add(sb.ToString());
            return f;
        }
    }
}
