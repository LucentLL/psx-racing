using System.IO;
using UnityEngine.SceneManagement;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ONE STAGE, BUILT AND AUDITED, in minutes rather than a forty-minute
    /// verify: for shaping the stage builder against a road it does not yet
    /// handle (Chimney Rock's 6 m switchbacks, NC 226's cut banks - both in
    /// TrackCatalog.HeldBack). Builds each venue named in PSX_LAB_IDS (a held
    /// venue included), then runs the self-test's roadside checks and the
    /// obstacle audit's passes on that scene alone.
    ///
    ///   tools\stage-lab.ps1 ChimneyRock,GillespieGap  ->  PSXRacing_stage_lab.txt
    /// </summary>
    public static class StageLab
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            string ids = System.Environment.GetEnvironmentVariable("PSX_LAB_IDS") ?? "";
            try
            {
                foreach (var raw in ids.Split(','))
                {
                    string id = raw.Trim();
                    if (id.Length == 0) continue;
                    TrackCatalog.TrackDef def = null;
                    foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
                    foreach (var d in TrackCatalog.HeldBack) if (d.id == id) def = d;
                    if (def == null) { sb.AppendLine("no venue " + id); continue; }
                    string path = PSXRacingBuilder.BuildOneForLab(def);
                    sb.AppendLine("=== " + id + " -> " + path);
                    sb.AppendLine(LifeSimSelfTest.LabRoadside(def, path));
                    sb.AppendLine(TrackObstacleAudit.AuditForLab(def, path));
                    sb.AppendLine(DownFaces(path));
                    string probes = System.Environment.GetEnvironmentVariable("PSX_LAB_WP") ?? "";
                    foreach (var pr in probes.Split(','))
                        if (pr.Trim().Length > 1) sb.AppendLine(Section(path, pr.Trim()));
                }
            }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_stage_lab.txt"), sb.ToString());
            Debug.Log(sb.ToString());
        }

        /// <summary>Every roadside triangle that faces DOWN (a fold), with the
        /// nearest waypoint, the side and how far out it is.</summary>
        static string DownFaces(string scenePath)
        {
            var sb = new StringBuilder("down-facing roadside triangles:" + System.Environment.NewLine);
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var tp = Object.FindFirstObjectByType<TrackPath>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                string nm = mf.gameObject.name;
                if (!(nm.StartsWith("RoadEdge") || nm.StartsWith("Kerb"))) continue;
                var m = mf.sharedMesh; if (m == null) continue;
                var v = m.vertices; var t = m.triangles; var xf = mf.transform;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = xf.TransformPoint(v[t[i]]), b = xf.TransformPoint(v[t[i + 1]]), c = xf.TransformPoint(v[t[i + 2]]);
                    var n = Vector3.Cross(b - a, c - a);
                    if (n.sqrMagnitude < 1e-10f || n.normalized.y >= 0f) continue;
                    Vector3 cen = (a + b + c) / 3f;
                    int w = tp.NearestIndex(cen);
                    Vector3 r = Vector3.Cross(Vector3.up, tp.GetTangent(w)).normalized;
                    float lat = Vector3.Dot(cen - tp.GetPoint(w), r);
                    sb.AppendLine("  " + nm + " wp " + w + (lat < 0 ? " L " : " R ") + Mathf.Abs(lat).ToString("0.00") +
                                  " m out, normal.y " + n.normalized.y.ToString("0.00") + ", area " + (n.magnitude * 0.5f).ToString("0.000") +
                                  ", curvature R " + (tp.curvatures != null && tp.curvatures[w] > 1e-5f ? (1f / tp.curvatures[w]).ToString("0.0") : "-"));
                }
            }
            return sb.ToString();
        }

        /// <summary>A cross-section the way the audit takes one: straight down
        /// every 5 cm from just inside the tarmac edge outward, at waypoint N on
        /// side L or R ("1300L"). Prints where the surface or the collider
        /// changes, so a lip is a pair of lines.</summary>
        static string Section(string scenePath, string spec)
        {
            var sb = new StringBuilder("section " + spec + ":" + System.Environment.NewLine);
            if (EditorSceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var tp = Object.FindFirstObjectByType<TrackPath>();
            int w = int.Parse(spec.Substring(0, spec.Length - 1));
            float side = char.ToUpperInvariant(spec[spec.Length - 1]) == 'L' ? -1f : 1f;
            Vector3 c = tp.GetPoint(w);
            Vector3 r = Vector3.Cross(Vector3.up, tp.GetTangent(w)).normalized * side;
            float half = tp.roadWidth * 0.5f;
            string lastName = null; float lastY = float.NaN;
            for (float e = -0.1f; e <= 14f; e += 0.05f)
            {
                Vector3 o = c + r * (half + e) + Vector3.up * 30f;
                string name = "(none)"; float y = float.NaN;
                if (Physics.Raycast(o, Vector3.down, out var hit, 80f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                { name = hit.collider.name; y = hit.point.y - c.y; }
                bool jump = !float.IsNaN(lastY) && !float.IsNaN(y) && Mathf.Abs(y - lastY) > 0.04f;
                if (name != lastName || jump || Mathf.Abs(e - Mathf.Round(e)) < 0.026f)
                    sb.AppendLine("  e " + e.ToString("0.00") + "  dy " + (float.IsNaN(y) ? "-" : y.ToString("0.000")) +
                                  "  " + name + (jump ? "   <-- step " + (y - lastY).ToString("0.000") : ""));
                lastName = name; lastY = y;
            }
            return sb.ToString();
        }
    }
}
