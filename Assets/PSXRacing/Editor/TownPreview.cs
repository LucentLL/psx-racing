using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Photographs the two places this pass built by hand: the home lot (the
    /// house, the open garage, the fixtures inside) and the Emerald Isle beach
    /// town. Headless: -executeMethod PSXRacing.EditorTools.TownPreview.Run —
    /// PNGs land in Screenshots/Town. Every failure mode here is visual and
    /// silent: a hovering house, a fixture inside a wall, a trailer in the sea.
    /// </summary>
    public static class TownPreview
    {
        [MenuItem("PSX Racing/Preview Home + Town")]
        public static void Run()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Screenshots", "Town");
            Directory.CreateDirectory(dir);

            // PSX/Lit shades from globals; without a sun everything is a
            // silhouette (the model-preview tool learned this the hard way).
            Shader.SetGlobalFloat("_PSXFogNear", 500f);
            Shader.SetGlobalFloat("_PSXFogFar", 1500f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.95f, 0.9f, 0.82f));

            // ---- the home lot ----
            // The driveway is laid out from the MEASURED garage door, so the
            // cameras read their X from the driveway rather than guessing the
            // side of the house the garage ended up on.
            EditorSceneManager.OpenScene(GarageSceneBuilder.ScenePath);
            var driveGO = GameObject.Find("Driveway");
            float dx = driveGO != null ? driveGO.transform.position.x : 4.45f;
            Shoot(dir, "home_street", new Vector3(-Mathf.Sign(dx) * 6f, 3.2f, -20f),
                  Quaternion.LookRotation(new Vector3(Mathf.Sign(dx) * 0.35f, -0.12f, 1f)));
            Shoot(dir, "home_drive", new Vector3(dx, 1.7f, -12.5f),
                  Quaternion.LookRotation(new Vector3(0f, -0.06f, 1f)));
            Shoot(dir, "home_garage", new Vector3(dx, 1.5f, -8.2f),
                  Quaternion.LookRotation(new Vector3(0.05f, -0.05f, 1f)));
            // fixtures are laid out on the +X side of the door datum whichever
            // wing the garage lands in, so this camera does not mirror
            Shoot(dir, "home_fixtures", new Vector3(dx - 0.8f, 1.5f, -5.2f),
                  Quaternion.LookRotation(new Vector3(0.55f, -0.08f, 0.75f)));
            Shoot(dir, "home_top", new Vector3(0f, 55f, -6f),
                  Quaternion.Euler(90f, 0f, 0f), ortho: 30f);

            // ---- the beach town ----
            int idx = TrackCatalog.IndexOf("EmeraldIsle");
            var scenes = EditorBuildSettings.scenes;
            int sceneIdx = TrackCatalog.SceneIndex(idx);
            if (sceneIdx < scenes.Length && File.Exists(scenes[sceneIdx].path))
            {
                EditorSceneManager.OpenScene(scenes[sceneIdx].path);
                var path = Object.FindFirstObjectByType<TrackPath>();
                if (path != null && path.Count > 40)
                {
                    Vector3 P(int i) => path.waypoints[Mathf.Clamp(i, 0, path.Count - 1)];
                    // the staging area, a town stretch, and a long top-down
                    ShootAlong(dir, "isle_start", path, 15, 4.2f);
                    ShootAlong(dir, "isle_town1", path, 260, 4.8f);
                    ShootAlong(dir, "isle_town2", path, 520, 4.8f);
                    Shoot(dir, "isle_top", P(300) + Vector3.up * 300f,
                          Quaternion.Euler(90f, 0f, 0f), ortho: 320f);
                }
                else Debug.LogWarning("[TownPreview] EmeraldIsle path missing");
            }
            Debug.Log("[TownPreview] shots written to " + dir);
        }

        static void ShootAlong(string dir, string name, TrackPath path, int idx, float height)
        {
            Vector3 at = path.waypoints[Mathf.Clamp(idx, 0, path.Count - 1)];
            Vector3 tan = path.GetTangent(Mathf.Clamp(idx, 0, path.Count - 1));
            Shoot(dir, name, at - tan * 14f + Vector3.up * height,
                  Quaternion.LookRotation((tan + new Vector3(0f, -0.08f, 0f)).normalized));
        }

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float ortho = 0f)
        {
            var camGO = new GameObject("~townCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2500f;
            cam.fieldOfView = 62f;
            if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; }

            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
        }
    }
}
