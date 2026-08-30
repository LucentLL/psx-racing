using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Photograph the baked cargo: the box, the box with its pizza in it, and a
    /// three-box stack, from the Pizza Cam's own angle.
    ///
    /// The three things that can go wrong here are all silent and all visual.
    /// A box baked on its edge still instantiates, still collides and still
    /// pays a tip — it is just held like a briefcase, which is the bug that
    /// started this pass. A pizza scaled to its own idea of correct sits
    /// proud of the box it is supposed to be inside. A stack pitched by the
    /// wrong height interpenetrates and the solver fires it across the car on
    /// frame one. None of them throw. So they get looked at.
    ///
    /// Writes to Screenshots/PizzaCargo.
    /// </summary>
    public static class PizzaCargoPreview
    {
        const string ResDir = "Assets/PSXRacing/Resources/PizzaCargo";

        public static void Run()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var box = AssetDatabase.LoadAssetAtPath<GameObject>(ResDir + "/pizza_box.prefab");
            var pizza = AssetDatabase.LoadAssetAtPath<GameObject>(ResDir + "/pizza_top_0.prefab");
            if (box == null)
            {
                Debug.LogError("[PizzaShot] no baked box at " + ResDir +
                               " — run PSX Racing/Bake Pizza Cargo");
                return;
            }

            Report("box", box);
            if (pizza != null) Report("pizza", pizza);

            // The globals PSX/Lit shades from. Nothing in this scene provides
            // them and without them every surface is black — the lesson the
            // garage and the pizza shop both had to learn the hard way.
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.35f, 0.85f, -0.4f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(1f, 0.95f, 0.86f));
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.60f));
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.1f, 0.1f, 0.12f));
            Shader.SetGlobalFloat("_PSXFogNear", 60f);
            Shader.SetGlobalFloat("_PSXFogFar", 240f);
            Shader.SetGlobalFloat("_PSXSnap", 0f);

            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      "Screenshots", "PizzaCargo");
            Directory.CreateDirectory(dir);

            float step = Size(box).y + 0.004f;

            // 1. one box, shut, on its own.
            var solo = Stack(box, pizza, 1, step, withLid: true);
            Shoot(dir, "cargo_1_box", 0.30f);
            Object.DestroyImmediate(solo);

            // 2. the same box OPEN — lid off, pizza showing. This is the frame
            //    that answers "is there actually a pizza in there".
            var open = Stack(box, pizza, 1, step, withLid: false);
            Shoot(dir, "cargo_2_open", 0.30f);
            Object.DestroyImmediate(open);

            // 3. the full three-box order as it leaves the counter.
            var three = Stack(box, pizza, 3, step, withLid: true);
            Shoot(dir, "cargo_3_stack", 0.46f);
            Object.DestroyImmediate(three);

            Debug.Log("[PizzaShot] wrote 3 cargo shots to " + dir);
        }

        static void Report(string what, GameObject prefab)
        {
            var s = Size(prefab);
            bool flat = s.y <= s.x && s.y <= s.z;
            Debug.Log("[PizzaShot] " + what + " " + s.ToString("0.000") +
                      (flat ? "  FLAT" : "  *** ON ITS EDGE ***"));
        }

        static Vector3 Size(GameObject prefab)
        {
            var probe = (GameObject)Object.Instantiate(prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rs = probe.GetComponentsInChildren<MeshRenderer>(true);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Object.DestroyImmediate(probe);
            return b.size;
        }

        /// <summary>A stack of assembled boxes. `withLid` false takes the lid
        /// off the top one, which is the frame that answers "is there actually a
        /// pizza in there".</summary>
        static GameObject Stack(GameObject box, GameObject pizza,
                                int count, float step, bool withLid)
        {
            var root = new GameObject("Stack");
            for (int i = 0; i < count; i++)
            {
                var b = (GameObject)Object.Instantiate(box);
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = new Vector3(0f, i * step, 0f);
                if (pizza != null)
                {
                    var pz = (GameObject)Object.Instantiate(pizza);
                    pz.transform.SetParent(b.transform, false);
                    pz.transform.localPosition = new Vector3(0f, step * 0.22f, 0f);
                }
                if (!withLid)
                    foreach (var t in b.GetComponentsInChildren<Transform>(true))
                        if (t.name == "Lid") { Object.DestroyImmediate(t.gameObject); break; }
            }
            return root;
        }

        static void Shoot(string dir, string name, float height)
        {
            const int W = 480, H = 320;
            var go = new GameObject("~cargoCam");
            var cam = go.AddComponent<Camera>();
            // The Pizza Cam's own framing, scaled to the subject: front
            // three-quarter from above, so a lid coming off reads and a box
            // heading for the edge stays in shot.
            var look = new Vector3(0f, height * 0.45f, 0f);
            var eye = look + new Vector3(0.42f, 0.34f, 0.60f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = 44f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.07f, 0.09f);

            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }
}
