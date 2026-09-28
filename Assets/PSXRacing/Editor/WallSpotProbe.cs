using System.IO;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Wall colliders against what is drawn, round one spot of a built stage:
    /// every vertex of every collider mesh and drawn mesh under Track/Walls and
    /// Track/Banks within PSX_SPOT_R metres of waypoint PSX_SPOT_WP, printed as
    /// (along, out, up) against that waypoint. PSX_SPOT_VENUE names the scene.
    /// Writes PSXRacing_wallspot.txt beside the project.
    /// </summary>
    public static class WallSpotProbe
    {
        public static void Run()
        {
            string id = System.Environment.GetEnvironmentVariable("PSX_SPOT_VENUE") ?? "BlueRidge";
            int wp = int.Parse(System.Environment.GetEnvironmentVariable("PSX_SPOT_WP") ?? "977");
            float r = float.Parse(System.Environment.GetEnvironmentVariable("PSX_SPOT_R") ?? "10");
            var sb = new StringBuilder();
            EditorSceneManager.OpenScene("Assets/PSXRacing/Scenes/" + id + ".unity", OpenSceneMode.Single);
            var tp = Object.FindFirstObjectByType<TrackPath>();
            Vector3 c = tp.GetPoint(wp);
            Vector3 fwd = tp.GetTangent(wp); fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            sb.AppendLine($"{id} wp {wp} at {c:F1}, right {right:F2}");
            void Dump(string label, Transform t, Mesh m)
            {
                if (m == null) return;
                var v = m.vertices;
                int shown = 0;
                var line = new StringBuilder();
                foreach (var p0 in v)
                {
                    Vector3 p = t.TransformPoint(p0);
                    Vector3 d = p - c;
                    if (new Vector2(d.x, d.z).magnitude > r) continue;
                    if (shown++ > 60) break;
                    line.Append($" ({Vector3.Dot(d, fwd):0.0},{Vector3.Dot(d, right):0.00},{d.y:0.00})");
                }
                if (shown > 0) sb.AppendLine($"  {label} {t.name} [{m.name}] {shown} verts (along,out,up):{line}");
            }
            foreach (var root in new[] { "Track/Walls", "Track/Banks" })
            {
                var go = GameObject.Find(root);
                if (go == null) { sb.AppendLine("  no " + root); continue; }
                foreach (var mc in go.GetComponentsInChildren<MeshCollider>(true)) Dump("COLLIDER", mc.transform, mc.sharedMesh);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) Dump("DRAWN", mf.transform, mf.sharedMesh);
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_wallspot.txt"), sb.ToString());
        }
    }
}
