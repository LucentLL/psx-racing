using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Every shell with its lamps lit - front, rear and a low side view along
    /// the lamp line - so a lens off the car, in it, or ahead of it is a
    /// picture. Built the way traffic builds a car (CarShell + CarLights with
    /// a shellDef), which is the path that used to bury them; the racers'
    /// CarBody path shares the same Fit. Logs what CarLampFinder measured.
    ///
    /// tools\lamp-preview.ps1. Not -nographics: it renders.
    /// </summary>
    public static class CarLampPreview
    {
        static string RootDir => Directory.GetParent(Application.dataPath).FullName;
        static string OutDir => Path.Combine(RootDir, "Screenshots", "Lamps");

        public static void Capture()
        {
            Directory.CreateDirectory(OutDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.5f;
            sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            RenderSettings.fog = false;
            var globals = sun.gameObject.AddComponent<PSXGlobals>();
            globals.sun = sun;
            globals.ambient = new Color(0.30f, 0.30f, 0.36f);
            globals.fogNear = 400f;
            globals.fogFar = 900f;
            globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

            var cam = new GameObject("LampCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;

            var log = new List<string>();
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def == null) continue;
                CarLampFinder.DiagMarks = new List<(Vector2, int, bool)>();
                // Measured afresh every time (what the baker would write now),
                // so the finder can be iterated without a re-bake.
                CarLampFinder.Apply(def, log);
                AtlasMarks(def, m.key);
                CarLampFinder.DiagMarks = null;

                // As TrafficSystem builds one.
                var go = new GameObject("Lamps_" + m.key);
                OnFoot.CarShell.Spawn(go.transform, def, 0, out _, solid: false);
                var lights = go.AddComponent<CarLights>();
                lights.shellDef = def;
                lights.shellZ = -def.colliderCenter.z;
                lights.PreviewBuild(lit: true, brake: true);

                float zc = -def.colliderCenter.z;
                float nose = def.headLamp.z + zc, tail = def.tailLamp.z + zc;
                float hy = def.headLamp.y, ty = def.tailLamp.y;
                Shot(cam, m.key + "_a_front", new Vector3(1.2f, hy + 0.9f, nose + 5.5f), new Vector3(0f, hy, nose));
                Shot(cam, m.key + "_b_rear", new Vector3(-1.2f, ty + 0.9f, tail - 5.5f), new Vector3(0f, ty, tail));
                // Along the lamp line from the side: a lens in the air or in the
                // panel shows as a sliver off the silhouette.
                Shot(cam, m.key + "_c_sidefront", new Vector3(3.2f, hy + 0.1f, nose - 0.2f), new Vector3(0f, hy, nose - 0.2f));
                Shot(cam, m.key + "_d_siderear", new Vector3(3.2f, ty + 0.1f, tail + 0.2f), new Vector3(0f, ty, tail + 0.2f));
                Object.DestroyImmediate(go);
            }
            Debug.Log("[LampPreview]\n" + string.Join("\n", log));
        }

        /// <summary>The first livery's sheet, doubled, with every lamp-zone
        /// sample marked: front zone green, rear zone blue, read-as-lamp
        /// magenta (head) / yellow (tail). Where the glass is on the sheet, and
        /// whether the finder saw it.</summary>
        static void AtlasMarks(CarModelDef def, string key)
        {
            var marks = CarLampFinder.DiagMarks;
            var tex = def.SkinCount > 0 ? def.skinMaterials[0].mainTexture : null;
            string path = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
            if (marks == null || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(path));
            int k = Mathf.Max(1, 512 / src.width);
            var o = new Texture2D(src.width * k, src.height * k, TextureFormat.RGB24, false);
            for (int y = 0; y < o.height; y++)
                for (int x = 0; x < o.width; x++)
                    o.SetPixel(x, y, src.GetPixel(x / k, y / k) * 0.8f);
            foreach (var mk in marks)
            {
                int x = Mathf.FloorToInt(Mathf.Repeat(mk.uv.x, 1f) * o.width);
                int y = Mathf.FloorToInt(Mathf.Repeat(mk.uv.y, 1f) * o.height);
                Color c = mk.lamp ? (mk.dir > 0 ? Color.magenta : Color.yellow) : (mk.dir > 0 ? Color.green : Color.cyan);
                o.SetPixel(x, y, c);
            }
            o.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, key + "_atlas.png"), o.EncodeToPNG());
            Object.DestroyImmediate(src); Object.DestroyImmediate(o);
        }

        static void Shot(Camera cam, string name, Vector3 pos, Vector3 lookAt)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
            var rt = new RenderTexture(480, 360, 24, RenderTextureFormat.ARGB32);
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
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
