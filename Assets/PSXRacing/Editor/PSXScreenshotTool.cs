using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Renders verification screenshots of the built scene from a few angles
    /// without entering play mode. Triggered by "psx_screenshot.flag" at the
    /// project root, or via menu PSX Racing > Capture Screenshots.
    /// </summary>
    [InitializeOnLoad]
    public static class PSXScreenshotTool
    {
        static string RootDir => Directory.GetParent(Application.dataPath).FullName;
        static string FlagPath => Path.Combine(RootDir, "psx_screenshot.flag");
        static string OutDir => Path.Combine(RootDir, "Screenshots");
        const string ScenePath = "Assets/PSXRacing/Scenes/CityCircuit.unity";

        static PSXScreenshotTool()
        {
            if (File.Exists(FlagPath))
                EditorApplication.delayCall += TryCapture;
        }

        static void TryCapture()
        {
            if (!File.Exists(FlagPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryCapture;
                return;
            }
            File.Delete(FlagPath);
            Capture();
        }

        [MenuItem("PSX Racing/Capture Screenshots")]
        public static void Capture()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);

            Directory.CreateDirectory(OutDir);

            var player = GameObject.Find("RX-7 Player");
            var cam = GameObject.Find("PSXCamera")?.GetComponent<Camera>();
            if (cam == null || player == null)
            {
                Debug.LogError("[PSXShot] scene objects missing");
                return;
            }

            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            var t = player.transform;
            Shot(cam, "1_chase",
                t.position - t.forward * 5.4f + Vector3.up * 1.9f,
                Quaternion.LookRotation((t.position + Vector3.up * 0.8f + t.forward * 2f) -
                                        (t.position - t.forward * 5.4f + Vector3.up * 1.9f)));
            Shot(cam, "2_grid34",
                t.TransformPoint(new Vector3(3.6f, 2.0f, 5.5f)),
                Quaternion.LookRotation((t.position + Vector3.up * 0.6f) - t.TransformPoint(new Vector3(3.6f, 2.0f, 5.5f))));
            Shot(cam, "3_overview",
                new Vector3(40f, 150f, 60f),
                Quaternion.LookRotation(new Vector3(40f, 0f, 150f) - new Vector3(40f, 150f, 60f)));
            Shot(cam, "4_gasstation",
                new Vector3(80f, 4f, 268f),
                Quaternion.LookRotation(new Vector3(82f, 3f, 300f) - new Vector3(80f, 4f, 268f)));

            Debug.Log("[PSXShot] Screenshots written to " + OutDir);
        }

        static void Shot(Camera cam, string name, Vector3 pos, Quaternion rot)
        {
            var oldPos = cam.transform.position;
            var oldRot = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(pos, rot);

            var rt = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                File.WriteAllBytes(Path.Combine(OutDir, "psx_" + name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            else Debug.LogWarning("[PSXShot] RenderRequest unsupported");

            rt.Release();
            Object.DestroyImmediate(rt);
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
        }
    }
}
