using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Renders every baked body shell to PNG without entering play mode, so a
    /// bad bake is caught by looking at it rather than by driving into it.
    ///
    /// The failure modes this exists for are all visual and all silent: a car
    /// facing backwards, wheels sunk into the tarmac or hovering beside the
    /// arches, a livery whose UVs land on the wrong half of the sheet. None of
    /// them throw, and none of them show up in the numbers the baker logs.
    ///
    /// Triggered by "psx_carshot.flag" at the project root, or the menu item.
    /// </summary>
    [InitializeOnLoad]
    public static class CarModelPreview
    {
        static string RootDir => Directory.GetParent(Application.dataPath).FullName;
        static string FlagPath => Path.Combine(RootDir, "psx_carshot.flag");
        static string OutDir => Path.Combine(RootDir, "Screenshots", "Models");

        static CarModelPreview()
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

        [MenuItem("Tools/PSX Racing/Preview Car Models")]
        public static void Capture()
        {
            Directory.CreateDirectory(OutDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(46f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.56f);
            RenderSettings.fog = false;

            // PSX/Lit takes its sun and ambient from global shader uniforms, not
            // from the scene's lights. Without this every car renders as a black
            // silhouette — which still shows the shape, but says nothing about
            // whether the livery landed on the right part of the sheet.
            var globals = sun.gameObject.AddComponent<PSXGlobals>();
            globals.sun = sun;
            globals.ambient = new Color(0.55f, 0.55f, 0.60f);
            globals.fogNear = 400f;
            globals.fogFar = 900f;
            globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            var camGO = new GameObject("PreviewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.28f, 0.30f, 0.35f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            int shots = 0;
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def == null) { Debug.LogWarning("[CarShot] no bake for " + m.key); continue; }

                var go = new GameObject(m.key);
                Dress(go.transform, def);

                // Three-quarter front, plus a straight side-on that makes a
                // wheel sitting outside its arch obvious.
                float len = Mathf.Max(def.colliderSize.z, 3.5f);
                Shot(cam, m.key + "_34", new Vector3(len * 0.95f, len * 0.52f, len * 1.15f),
                     new Vector3(0f, def.colliderSize.y * 0.5f, 0f));
                Shot(cam, m.key + "_side", new Vector3(len * 2.1f, len * 0.22f, 0f),
                     new Vector3(0f, def.colliderSize.y * 0.55f, 0f));
                shots += 2;

                Object.DestroyImmediate(go);
            }

            Debug.Log($"[CarShot] {shots} images written to {OutDir}");
        }

        /// <summary>Same assembly the builder's parked cars use: body at the
        /// shell's own yaw and lift, four wheels at the measured axles.</summary>
        static void Dress(Transform root, CarModelDef def)
        {
            var mat = def.SkinCount > 0 ? def.skinMaterials[0] : null;
            var wheelMat = def.wheelMaterial != null ? def.wheelMaterial : mat;

            var body = new GameObject("Body");
            body.transform.SetParent(root, false);
            // Both offsets, exactly as CarBody applies them. Leaving the slide
            // out here would hide the one failure these shots exist to show:
            // this preview rendered every car with its wheels behind its arches
            // for a whole pass, because it pinned the body to the origin while
            // the game did not.
            body.transform.localPosition = new Vector3(0f, def.bodyYOffset, def.bodyZOffset);
            body.transform.localRotation = Quaternion.Euler(0f, def.bodyYaw, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = def.bodyMesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = mat;

            for (int w = 0; w < 4; w++)
            {
                bool left = w % 2 == 0;
                var wheel = new GameObject("Wheel" + w);
                wheel.transform.SetParent(root, false);
                wheel.transform.localPosition = new Vector3(
                    (left ? -0.5f : 0.5f) * def.trackWidth, def.wheelRadius,
                    (w < 2 ? 0.5f : -0.5f) * def.wheelbase);
                wheel.transform.localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f);
                wheel.transform.localScale = Vector3.one * def.wheelMeshScale;
                wheel.AddComponent<MeshFilter>().sharedMesh = def.wheelMesh;
                wheel.AddComponent<MeshRenderer>().sharedMaterial = wheelMat;
            }
        }

        static void Shot(Camera cam, string name, Vector3 pos, Vector3 lookAt)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));

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
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            else Debug.LogWarning("[CarShot] RenderRequest unsupported");

            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
