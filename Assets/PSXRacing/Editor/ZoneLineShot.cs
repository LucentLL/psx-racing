using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The zone line, from the driver's approach.
    ///
    /// One picture per line: the built scene is opened, a camera stood on the
    /// road twenty-five metres before the line at a driver's eye height,
    /// looking straight down it, and the frame saved. The question this
    /// answers is the one a builder cannot: do the knots sit on the tarmac
    /// and read as a line across it, or do they float, sink, or scatter.
    /// </summary>
    public static class ZoneLineShot
    {
        [MenuItem("PSX Racing/Shoot Zone Lines")]
        public static void Run()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      "Screenshots", "ZoneLines");
            Directory.CreateDirectory(dir);

            Shoot(PSXRacingBuilder.NeighborhoodScenePath, "junction", dir);
            Shoot(PSXRacingBuilder.TownScenePath, "town_west", dir, "TownEdgeW");
            Shoot(PSXRacingBuilder.TownScenePath, "town_east", dir, "TownEdgeE");
        }

        static void Shoot(string scenePath, string tag, string dir, string edgeName = null)
        {
            if (!File.Exists(scenePath)) { Debug.LogWarning("[ZoneLine] no scene " + scenePath); return; }
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Town.TownEdge edge = null;
            foreach (var e in Object.FindObjectsByType<Town.TownEdge>(FindObjectsSortMode.None))
                if (edgeName == null || e.name == edgeName) { edge = e; break; }
            if (edge == null) { Debug.LogWarning("[ZoneLine] no edge in " + scenePath); return; }

            var line = edge.transform.Find("ZoneLine");
            if (line == null)
            {
                // The neighbourhood's row hangs off the scene root, beside the
                // volume rather than under it.
                var go = GameObject.Find("ZoneLine");
                line = go != null ? go.transform : null;
            }
            if (line == null) { Debug.LogWarning("[ZoneLine] no ZoneLine row for " + tag); return; }

            // Stand 25 m OUTSIDE the line looking in along `inward`, at a
            // driver's eye: this is the view the line exists for.
            Vector3 at = line.position;
            Vector3 fwd = edge.inward.normalized;
            var camGO = new GameObject("~zoneCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.position = at - fwd * 25f + Vector3.up * 1.2f;
            cam.transform.rotation = Quaternion.LookRotation((at - cam.transform.position).normalized
                                                             + Vector3.up * 0.02f, Vector3.up);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.45f, 0.65f, 1f);

            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            string path = Path.Combine(dir, "zoneline_" + tag + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
            Debug.Log("[ZoneLine] " + tag + " -> " + path);
        }
    }
}
