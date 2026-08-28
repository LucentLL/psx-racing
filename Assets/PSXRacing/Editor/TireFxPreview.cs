using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Drives a fake car through a slide and photographs the tyre marks and
    /// smoke it leaves, without the game, the physics or play mode.
    ///
    /// Both effects are meshes built vertex by vertex at runtime, and every way
    /// that goes wrong is a picture rather than an exception: a ring buffer
    /// that overwrites the wrong slot, quads wound so the whole strip is
    /// back-facing, a material in the wrong queue so the marks paint over the
    /// cars, colours written to the wrong four vertices so the trail is
    /// uniformly black, bounds that leave the mesh culled from every angle it
    /// is actually seen at. None of those throw, and none of them are visible
    /// in a debugger.
    ///
    /// The trick that makes this cheap is that neither component knows anything
    /// about physics: they read <see cref="CarController.wheelContacts"/>, four
    /// plain structs, and that array can be written by hand. So the "car" here
    /// is a position, a heading and four contact patches stepped along a curve
    /// — the same input a real lap would produce, with no solver in the way.
    /// </summary>
    public static class TireFxPreview
    {
        [MenuItem("PSX Racing/Preview Tyre Marks and Smoke")]
        public static void Dump()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // The decal shader fogs by the same globals every surface in this
            // game does, and they are ZERO in an empty scene — which makes
            // (dist - near) / (far - near) saturate at about one metre and
            // paints the whole effect in fog colour. PSXGlobals does this from
            // its Update in a real scene.
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.7f, 0.7f, 0.75f));
            Shader.SetGlobalFloat("_PSXFogNear", 400f);
            Shader.SetGlobalFloat("_PSXFogFar", 900f);
            Shader.SetGlobalFloat("_PSXSnap", 0f);

            Ground();

            var carGO = new GameObject("FakeCar");
            var car = carGO.AddComponent<CarController>();

            var marks = carGO.AddComponent<SkidMarks>();
            marks.car = car;
            marks.material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/PSXRacing/Materials/SkidMark.mat");
            marks.capacity = 224;

            var smoke = carGO.AddComponent<TireSmoke>();
            smoke.car = car;
            smoke.material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/PSXRacing/Materials/TireSmoke.mat");
            smoke.capacity = 56;

            if (marks.material == null || smoke.material == null)
            {
                Debug.LogError("[TyreFX] materials missing — run the scene build first, " +
                               "which is what creates them.");
                return;
            }

            Invoke(marks, "Awake");
            Invoke(smoke, "Awake");

            var cam = MakeCamera();
            Simulate(car, marks, smoke, cam);

            // Close overhead, over the middle of the S. High enough to see the
            // shape of the trail, low enough that a mark is more than a
            // hairline — at 34 m up it is four pixels wide and the picture
            // cannot tell a faint mark from a thin one.
            Shot(cam, Path.Combine(outDir, "tyrefx_marks.png"), new Vector3(6f, 15f, -2f),
                 Quaternion.Euler(72f, 0f, 0f));
            // And from where the car would be, which is the height the smoke is
            // actually judged at. From straight above a cloud is a smudge.
            Shot(cam, Path.Combine(outDir, "tyrefx_chase.png"), new Vector3(9f, 2.4f, -14f),
                 Quaternion.Euler(6f, -18f, 0f));
            Debug.Log("[TyreFX] wrote tyrefx_marks.png and tyrefx_chase.png");
        }

        /// <summary>
        /// Two and a half seconds of a car crossing the frame with the rear
        /// axle stepped out, at 60 samples a second.
        ///
        /// The wheels are laid out and the contacts filled exactly as
        /// CarController fills them: patch position on the ground, surface
        /// normal, wheel heading, and a scrub speed that is zero for the fronts
        /// and large for the rears. A slide that only marks two of the four
        /// wheels is also the check that it is reading the array per wheel and
        /// not per car.
        /// </summary>
        static void Simulate(CarController car, SkidMarks marks, TireSmoke smoke, Camera cam)
        {
            const int Steps = 150;
            const float Dt = 1f / 60f;
            var t = car.transform;

            for (int i = 0; i < Steps; i++)
            {
                float u = i / (float)(Steps - 1);
                // An S: into the slide, hold, out of it.
                float x = Mathf.Lerp(-26f, 26f, u);
                float z = Mathf.Sin(u * Mathf.PI * 1.6f) * 7f;
                // Heading along the path, plus the yaw of a car pointing well
                // out of its own direction of travel.
                float dz = Mathf.Cos(u * Mathf.PI * 1.6f) * 7f * Mathf.PI * 1.6f / 52f;
                float yaw = Mathf.Atan2(1f, dz) * Mathf.Rad2Deg - 90f;
                yaw += Mathf.Sin(u * Mathf.PI) * 26f;

                t.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f));

                // Rear axle sliding hard through the middle of the run, fronts
                // scrubbing lightly at the extremes of the S.
                float rear = Mathf.Lerp(1.5f, 9f, Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI));
                float front = Mathf.Lerp(0f, 3.2f, Mathf.Abs(Mathf.Sin(u * Mathf.PI * 1.6f)));

                for (int wI = 0; wI < 4; wI++)
                {
                    bool isFront = wI < 2;
                    float side = wI % 2 == 0 ? -0.73f : 0.73f;
                    Vector3 local = new Vector3(side, 0f, isFront ? 1.21f : -1.21f);
                    car.wheelContacts[wI].grounded = true;
                    car.wheelContacts[wI].point = t.TransformPoint(local);
                    car.wheelContacts[wI].normal = Vector3.up;
                    car.wheelContacts[wI].forward = t.forward;
                    car.wheelContacts[wI].slide = isFront ? front : rear;
                    car.wheelContacts[wI].load = 3100f;
                    car.wheelContacts[wI].onRoad = true;
                }

                // The marks need no clock — a segment is laid by DISTANCE — but
                // the smoke does, and Time.deltaTime outside play mode is zero.
                // TireSmoke.Tick takes the step for exactly this reason.
                Invoke(marks, "LateUpdate");
                smoke.Tick(Dt);
            }
        }

        static void Ground()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Ground";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 12f;
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.33f, 0.33f, 0.35f));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Camera MakeCamera()
        {
            var go = new GameObject("PreviewCam");
            // TireSmoke billboards face Camera.main, and an untagged camera
            // leaves every puff facing world +X — which from here is edge on,
            // and reads as no smoke at all rather than as a bug.
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.62f, 0.72f);
            cam.fieldOfView = 55f;
            cam.farClipPlane = 500f;
            return cam;
        }

        static void Shot(Camera cam, string path, Vector3 pos, Quaternion rot)
        {
            const int W = 1280, H = 720;
            cam.transform.SetPositionAndRotation(pos, rot);

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        static void Invoke(object obj, string method) =>
            obj.GetType().GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
               ?.Invoke(obj, null);
    }
}
