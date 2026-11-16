using System.IO;
using System.Text;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The bank round's scene builds without the audits (2026-11-16): each
    /// venue in PSX_LAB_IDS built exactly as StageLab builds it
    /// (PSXRacingBuilder.BuildOneForLab, the scene saved over the sandbox's),
    /// its build log kept per venue (PSXRacing_lab_build_log_(id).txt), one line
    /// per venue in PSXRacing_bank_lab.txt. For the before/after scenes the
    /// play checks and the proof frames read, where StageLab's audits have
    /// already been run on the same code.
    /// </summary>
    public static class BankLab
    {
        public static void BuildOnly()
        {
            var sb = new StringBuilder();
            string root = Path.GetDirectoryName(Application.dataPath);
            string ids = System.Environment.GetEnvironmentVariable("PSX_LAB_IDS") ?? "";
            foreach (var raw in ids.Split(','))
            {
                string id = raw.Trim();
                if (id.Length == 0) continue;
                TrackCatalog.TrackDef def = null;
                foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
                if (def == null) { sb.AppendLine("no venue " + id); continue; }
                float t0 = Time.realtimeSinceStartup;
                string path;
                try { path = PSXRacingBuilder.BuildOneForLab(def); }
                catch (System.Exception ex) { sb.AppendLine("FAIL " + id + ": " + ex.Message); continue; }
                string log = Path.Combine(root, "PSXRacing_lab_build_log.txt");
                if (File.Exists(log)) File.Copy(log, Path.Combine(root, "PSXRacing_lab_build_log_" + id + ".txt"), true);
                sb.AppendLine("built " + id + " -> " + path + " in " + ((Time.realtimeSinceStartup - t0) / 60f).ToString("0.0") + " min, PSX_STAGE_BANK=" +
                              (System.Environment.GetEnvironmentVariable("PSX_STAGE_BANK") ?? "(unset: on)"));
                File.WriteAllText(Path.Combine(root, "PSXRacing_bank_lab.txt"), sb.ToString());
            }
            File.WriteAllText(Path.Combine(root, "PSXRacing_bank_lab.txt"), sb.ToString());
            Debug.Log("[BankLab] " + sb);
        }
    }
}
