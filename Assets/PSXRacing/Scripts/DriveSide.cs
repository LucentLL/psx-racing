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
        static HashSet<string> both = new HashSet<string>();

        /// <summary>The table: catalog id -> right-hand drive.</summary>
        public static Dictionary<string, bool> Table
        {
            get
            {
                if (rhd != null) return rhd;
                rhd = new Dictionary<string, bool>();
                both = new HashSet<string>();
                var ta = Resources.Load<TextAsset>(ResourcePath);
                if (ta == null) { Debug.LogWarning("[DriveSide] Resources/" + ResourcePath + ".csv missing - every car LHD"); return rhd; }
                bool header = true;
                foreach (var line in ta.text.Split('\n'))
                {
                    if (header) { header = false; continue; }
                    var f = SplitCsv(line);
                    if (f.Count >= 3 && f[0].Length > 0) rhd[f[0]] = f[2].Trim() == "RHD";
                    if (f.Count >= 5 && f[0].Length > 0 && f[4].Trim() == "yes") both.Add(f[0]);
                }
                return rhd;
            }
        }

        /// <summary>The real car was sold BOTH ways (the CSV's "ambiguous"
        /// column: the EG Civic, the Honda-badged NSX). The row still names
        /// one side, the one the game drives it from; only these cars offer
        /// the debug bench's DRIVE SIDE choice (owner, 2026-10-16: "In Debug
        /// mode, option to choose RHD or LHD should be an option if there is
        /// a choice").</summary>
        public static bool Either(string specId)
        {
            if (string.IsNullOrEmpty(specId)) return false;
            var _ = Table;
            return both.Contains(specId);
        }

        /// <summary>The spec-sheet word for a side: "RHD" or "LHD".</summary>
        public static string Label(bool isRhd) => isRhd ? "RHD" : "LHD";

        /// <summary>The spec-sheet word for a catalog row (owner, 2026-10-16:
        /// "RHD and LHD should be a spec shown when viewing/inspecting the
        /// car"). An owned car can carry a debug pick on top of this: ask
        /// LifeSim.DebugCarOps.SideLabel for one of those.</summary>
        public static string Label(string specId) => Label(IsRHD(specId));

        /// <summary>Right-hand drive for a catalog id (false when unknown).</summary>
        public static bool IsRHD(string specId) =>
            !string.IsNullOrEmpty(specId) && Table.TryGetValue(specId, out bool r) && r;

        /// <summary>Right-hand drive for a car: a preview's <see cref="Override"/>,
        /// else the debug bench's pick for THIS car (<see cref="Pick"/>), else
        /// its own catalog row. Read every frame by the cockpit
        /// (CockpitInterior.Refit re-fits when it changes; ChaseCamera mounts
        /// the eye off it), so a pick lands on the next frame with no scene
        /// reload: variant, eye, cargo seat, look-over and hands together.</summary>
        public static bool IsRHD(CarController car)
        {
            if (Override.HasValue) return Override.Value;
            if (car == null) return false;
            if (pickedCar != null && car == pickedCar && car.activeSpec != null && Either(car.activeSpec.id))
                return pickedRhd;
            return car.activeSpec != null && IsRHD(car.activeSpec.id);
        }

        static CarController pickedCar;
        static bool pickedRhd;

        /// <summary>
        /// The debug career's DRIVE SIDE pick, put on the live car by
        /// RaceHandoffApplier (on scene load and on every bench ReapplyCar):
        /// "RHD" / "LHD", or empty for the car's own side. One slot, because
        /// only the player's car can carry one. A pick on a car whose catalog
        /// row was NOT sold both ways is ignored, so a stale request can never
        /// move a US-market car's wheel to the right.
        /// </summary>
        public static void Pick(CarController car, string side)
        {
            bool on = car != null && (side == "RHD" || side == "LHD") &&
                      car.activeSpec != null && Either(car.activeSpec.id);
            if (on) { pickedCar = car; pickedRhd = side == "RHD"; }
            else if (car == null || car == pickedCar) pickedCar = null;
        }

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
