using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHICH WAY THE WORLD IS IN THE PAINT (owner, 2026-10-23: "As I drive down
    /// the road, they move in the blue arrow direction, when they should move
    /// in the red arrow direction" - sideways across the roof instead of down
    /// the flank - and the reflections flicker, the windows most, in rain and
    /// fog). Two parts, PSX_REFLMAP_PARTS=map,flicker:
    ///
    /// MAP. An empty scene: a chrome ball (PSX/CarPaint on a black sheet, so all
    /// of it is glass) where CarReflections' probe stands, and seven coloured
    /// boxes 25 m out in known directions from the "car" - red LEFT, blue
    /// RIGHT, green AHEAD, yellow BEHIND, cyan up-ahead and magenta up-left (in
    /// the top face), white down-right (in the bottom face). For every texel
    /// of each face, of the panorama and of the ball as a chase camera sees it,
    /// the colour that SHOULD be there is traced on the CPU (a ray from the
    /// probe against the boxes; for the ball, the reflected ray off the exact
    /// sphere) and compared with the colour that IS. A mirror or a turned
    /// azimuth anywhere in the chain shows as a confusion pair (RED->BLUE).
    /// The car is turned to two headings. Run it under -force-glcore (the
    /// WebGL conventions) and under Direct3D 11; "[ReflMap] RESULT PASS" only
    /// when every image agrees.
    ///
    /// FLICKER. The Neighborhood street on a rainy (and a foggy) afternoon,
    /// the player's car driven down it at 45 mph, 75 frames a second, the
    /// chase camera rigid behind it, the faces refreshed as the game does.
    /// For the car's glass and paint pixels: the mean frame-to-frame change
    /// (d1), the mean temporal second difference (d2 - smooth motion is small,
    /// a flash is large) and the share of pixel-frames that jump by more than
    /// 20 of 255. Modes: off (the sky lookup alone: the floor), game, and
    /// (tools) game with the rain hidden from the faces and all six faces every
    /// frame. Frame-time cost of the reflections at the chase eye too.
    ///
    /// Headless, NOT -nographics: -executeMethod PSXRacing.EditorTools.ReflectionMapCheck.Run
    /// (tools/refl-map-check.ps1 runs it on both graphics APIs).
    /// PNGs: Screenshots\reflmap_&lt;tag&gt;_*.png (PSX_REFLMAP_TAG).
    /// </summary>
    public static class ReflectionMapCheck
    {
        const int None = 0, Red = 1, Green = 2, Blue = 3, Yellow = 4, Cyan = 5, Magenta = 6, White = 7;
        static readonly string[] Names = { "-", "RED", "GREEN", "BLUE", "YELLOW", "CYAN", "MAGENTA", "WHITE" };
        static readonly Color[] Cols =
        {
            Color.black, Color.red, Color.green, Color.blue, new Color(1, 1, 0), new Color(0, 1, 1), new Color(1, 0, 1), Color.white,
        };

        struct Box { public int label; public Transform t; }

        public static void Run()
        {
            string parts = Env("PSX_REFLMAP_PARTS", "map,flicker");
            string api = SystemInfo.graphicsDeviceType.ToString();
            string tag = Env("PSX_REFLMAP_TAG", "now") + "_" + (api.Contains("OpenGL") ? "gl" : api.Contains("Direct3D") ? "d3d" : api.ToLowerInvariant());
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);
            Debug.Log($"[ReflMap] graphics {api} ({SystemInfo.graphicsDeviceName}), uvStartsAtTop {SystemInfo.graphicsUVStartsAtTop}, tag {tag}");
            bool ok = true;
            try
            {
                if (parts.Contains("map")) ok &= Map(dir, tag);
                if (parts.Contains("drive")) ok &= Drive(dir, tag);
                if (parts.Contains("street")) Street(dir, tag);
                if (parts.Contains("wall")) Wall(dir, tag);
                if (parts.Contains("gles")) ok &= Gles();
                if (parts.Contains("flicker")) Flicker(dir, tag);
            }
            catch (System.Exception e) { Debug.LogError("[ReflMap] " + e); ok = false; }
            finally
            {
                CarReflections.Off();
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
            }
            Debug.Log(ok ? "[ReflMap] RESULT PASS" : "[ReflMap] RESULT FAIL");
            Debug.Log("[ReflMap] DONE");
        }

        // ------------------------------------------------------------------
        //  MAP
        // ------------------------------------------------------------------
        static bool Map(string dir, string tag)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // the paint's own vertex fog would paint the ball the fog colour
            Shader.SetGlobalFloat("_PSXFogNear", 10000f);
            Shader.SetGlobalFloat("_PSXFogFar", 20000f);
            Shader.SetGlobalFloat("_PSXToneOn", 0f);
            Shader.SetGlobalFloat("_PSXEmitWrite", 0f);
            Shader.SetGlobalFloat("_PSXPaintWorld", 0f);

            var viewGo = new GameObject("MapView");
            var view = viewGo.AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.black;
            view.nearClipPlane = 0.1f;
            view.farClipPlane = 500f;
            view.fieldOfView = 30f;
            var vd = viewGo.AddComponent<UniversalAdditionalCameraData>();
            vd.renderPostProcessing = false;
            vd.antialiasing = AntialiasingMode.None;

            var car = new GameObject("MapCar").transform;
            const float eyeUp = 1.15f, ballR = 0.9f;
            var ball = new GameObject("Ball");
            ball.transform.SetParent(car, false);
            ball.transform.localPosition = new Vector3(0f, eyeUp, 0f);
            ball.AddComponent<MeshFilter>().sharedMesh = Sphere(ballR, 160, 80);
            var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            sheet.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
            sheet.Apply();
            var paint = new Material(Shader.Find("PSX/CarPaint"));
            paint.SetTexture("_MainTex", sheet);
            ball.AddComponent<MeshRenderer>().sharedMaterial = paint;

            float e = 62f * Mathf.Deg2Rad;
            var dirs = new (int label, Vector3 local)[]
            {
                (Red, Vector3.left), (Blue, Vector3.right), (Green, Vector3.forward), (Yellow, Vector3.back),
                (Cyan, new Vector3(0f, Mathf.Sin(e), Mathf.Cos(e))),
                (Magenta, new Vector3(-Mathf.Cos(e), Mathf.Sin(e), 0f)),
                (White, new Vector3(Mathf.Cos(e), -Mathf.Sin(e), 0f)),
            };
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var boxes = new List<Box>();
            foreach (var d in dirs)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.name = "Box" + Names[d.label];
                var m = new Material(unlit);
                m.SetColor("_BaseColor", Cols[d.label]);
                m.color = Cols[d.label];
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                go.transform.localScale = Vector3.one * 10f;
                boxes.Add(new Box { label = d.label, t = go.transform });
            }

            bool ok = true;
            foreach (float yaw in new[] { 0f, 120f })
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                car.SetPositionAndRotation(new Vector3(3f, 0f, -7f), rot);
                Vector3 P = car.position + car.up * eyeUp;
                for (int k = 0; k < dirs.Length; k++)
                {
                    boxes[k].t.position = P + rot * dirs[k].local * 25f;
                    boxes[k].t.rotation = rot;
                }
                string stem = $"reflmap_{tag}_yaw{yaw:0}";

                CarReflections.Off();
                if (!CarReflections.CaptureNow(car, view)) { Debug.LogError("[ReflMap] EnvPano shader missing"); return false; }
                var inst = Inst();
                var faces = (RenderTexture[])typeof(CarReflections).GetField("faces", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(inst);
                var faceRot = (Quaternion[])typeof(CarReflections).GetField("FaceRot", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

                // 1. each face against its camera's own basis
                for (int i = 0; i < 6; i++)
                {
                    var tex = Read(faces[i]);
                    int w = tex.width, h = tex.height;
                    var px = tex.GetPixels32();
                    var obs = new int[px.Length]; var exp = new int[px.Length];
                    Vector3 f = faceRot[i] * Vector3.forward, r = faceRot[i] * Vector3.right, u = faceRot[i] * Vector3.up;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int j = y * w + x;
                            obs[j] = Label(px[j]);
                            var d = (f + r * ((x + 0.5f) / w * 2f - 1f) + u * ((y + 0.5f) / h * 2f - 1f)).normalized;
                            exp[j] = Trace(boxes, P, d);
                        }
                    ok &= Score($"yaw {yaw:0} face {i} ({FaceName(f)})", obs, exp, 0.90f);
                    Object.DestroyImmediate(tex);
                }

                // 2. the panorama the paint reads (u = atan2(z, x) / 2pi + 0.5)
                {
                    var tex = Read(CarReflections.Panorama);
                    int w = tex.width, h = tex.height;
                    var px = tex.GetPixels32();
                    var obs = new int[px.Length]; var exp = new int[px.Length];
                    var expTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    var expPx = new Color32[px.Length];
                    for (int y = 0; y < h; y++)
                    {
                        float lat = ((y + 0.5f) / h - 0.5f) * Mathf.PI;
                        for (int x = 0; x < w; x++)
                        {
                            float lon = ((x + 0.5f) / w - 0.5f) * 2f * Mathf.PI;
                            var d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                            int j = y * w + x;
                            obs[j] = Label(px[j]);
                            exp[j] = Trace(boxes, P, d);
                            expPx[j] = Cols[exp[j]] * 0.6f;
                        }
                    }
                    ok &= Score($"yaw {yaw:0} panorama", obs, exp, 0.90f);
                    expTex.SetPixels32(expPx); expTex.Apply();
                    if (yaw == 0f)
                    {
                        File.WriteAllBytes(Path.Combine(dir, stem + "_pano.png"), tex.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(dir, stem + "_pano_expected.png"), expTex.EncodeToPNG());
                    }
                    Object.DestroyImmediate(tex); Object.DestroyImmediate(expTex);
                }

                // 3. the ball in the paint, from a chase camera behind the car
                {
                    const int S = 640;
                    view.aspect = 1f;
                    Vector3 C = P;
                    Vector3 eye = car.TransformPoint(new Vector3(0f, 2.6f, -5.0f));
                    view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(C - eye));
                    Shader.SetGlobalFloat("_PSXPaintDebug", 6f);   // what the lacquer sees
                    var tex = Shoot(view, S, S);
                    Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                    var px = tex.GetPixels32();
                    var obs = new List<int>(); var exp = new List<int>(); var truth = new List<int>();
                    var expPx = new Color32[px.Length];
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            var ray = view.ViewportPointToRay(new Vector3((x + 0.5f) / S, (y + 0.5f) / S, 0f));
                            Vector3 oc = ray.origin - C;
                            float b = Vector3.Dot(ray.direction, oc), c = oc.sqrMagnitude - ballR * ballR, disc = b * b - c;
                            if (disc <= 0f) continue;
                            Vector3 hit = ray.origin + ray.direction * (-b - Mathf.Sqrt(disc));
                            Vector3 n = (hit - C) / ballR;
                            Vector3 V = (eye - hit).normalized;
                            if (Vector3.Dot(n, V) < 0.06f) continue;   // the very rim: mesh vs sphere
                            Vector3 R = Vector3.Reflect(-V, n);
                            int j = y * S + x;
                            // what the paint should read: the panorama along the pixel's
                            // ray as PSXEnvRay bends it (R itself when no distances are
                            // published); and, for the record, the true mirror
                            int want = Trace(boxes, P, EnvRay(hit, R));
                            obs.Add(Label(px[j])); exp.Add(want); truth.Add(Trace(boxes, hit, R));
                            expPx[j] = Cols[want] * 0.6f;
                        }
                    ok &= Score($"yaw {yaw:0} ball (chase)", obs.ToArray(), exp.ToArray(), 0.90f);
                    Score($"yaw {yaw:0} ball vs a true mirror (info: no road in this scene, the paint assumes one)", obs.ToArray(), truth.ToArray(), 0f, true);
                    File.WriteAllBytes(Path.Combine(dir, stem + "_ball.png"), tex.EncodeToPNG());
                    var expTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                    expTex.SetPixels32(expPx); expTex.Apply();
                    File.WriteAllBytes(Path.Combine(dir, stem + "_ball_expected.png"), expTex.EncodeToPNG());
                    Object.DestroyImmediate(tex); Object.DestroyImmediate(expTex);
                }
            }
            CarReflections.Off();
            return ok;
        }

        /// <summary>Every pass of the shaders that read the panorama (and PSX/Lit,
        /// which includes the same file) compiled for WebGL 2 (GLES3), the
        /// way the build will - the editor's own API never compiles them.</summary>
        static bool Gles()
        {
            int bad = 0, done = 0;
            foreach (var name in new[] { "PSX/CarPaint", "PSX/CarLens", CarReflections.ShaderName, "PSX/Lit" })
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogError("[ReflMap] no shader " + name); bad++; continue; }
                var data = ShaderUtil.GetShaderData(sh);
                for (int s = 0; s < data.SubshaderCount; s++)
                {
                    var sub = data.GetSubshader(s);
                    for (int p = 0; p < sub.PassCount; p++)
                    {
                        var pass = sub.GetPass(p);
                        foreach (var st in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
                        {
                            if (!pass.HasShaderStage(st)) continue;
                            var r = pass.CompileVariant(st, new string[0], UnityEditor.Rendering.ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL);
                            done++;
                            if (r.Success) continue;
                            bad++;
                            foreach (var m in r.Messages) Debug.LogError($"[ReflMap] GLES3 FAIL {name} pass {p} {st} line {m.line}: {m.message}");
                        }
                    }
                }
            }
            Debug.Log(bad == 0 ? $"[ReflMap] ok   GLES3: {done} stages compiled" : $"[ReflMap] FAIL GLES3: {bad} of {done} stages");
            return bad == 0;
        }

        /// <summary>PSXSkyReflect.cginc's PSXEnvRay, on the CPU, from the
        /// globals CarReflections published.</summary>
        static Vector3 EnvRay(Vector3 wpos, Vector3 R)
        {
            Vector4 eye = Shader.GetGlobalVector("_PSXEnvEye"), r0 = Shader.GetGlobalVector("_PSXEnvRad0"), r1 = Shader.GetGlobalVector("_PSXEnvRad1");
            if (eye.w <= 0.001f) return R;
            float Radius(float x, float z)
            {
                float s = Mathf.Atan2(z, x) * (4f / Mathf.PI), r = 0f;
                for (int k = 0; k < 8; k++)
                {
                    float t = s - k; t -= 8f * Mathf.Round(t / 8f);
                    r += Mathf.Clamp01(1f - Mathf.Abs(t)) * (k < 4 ? r0[k] : r1[k - 4]);
                }
                return r;
            }
            Vector3 o = wpos - (Vector3)eye;
            float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 10f, o.magnitude));
            float a = Mathf.Max(R.x * R.x + R.z * R.z, 1e-6f), b = o.x * R.x + o.z * R.z, c0 = o.x * o.x + o.z * o.z;
            float rr = Radius(R.x, R.z);
            near *= Mathf.Clamp01((rr * rr - c0) / (0.25f * rr * rr));
            float tt = (-b + Mathf.Sqrt(Mathf.Max(b * b - a * (c0 - rr * rr), 0f))) / a;
            rr = Radius(o.x + R.x * tt, o.z + R.z * tt);
            tt = (-b + Mathf.Sqrt(Mathf.Max(b * b - a * (c0 - rr * rr), 0f))) / a;
            if (R.y < -1e-4f) tt = Mathf.Min(tt, Mathf.Max((-eye.w - o.y) / R.y, 0f));
            return Vector3.Lerp(R, (o + R * tt).normalized, near).normalized;
        }

        static string FaceName(Vector3 f) =>
            f.x > 0.5f ? "+X" : f.x < -0.5f ? "-X" : f.y > 0.5f ? "+Y" : f.y < -0.5f ? "-Y" : f.z > 0.5f ? "+Z" : "-Z";

        /// <summary>The box a ray from <paramref name="o"/> along <paramref name="d"/> meets first.</summary>
        static int Trace(List<Box> boxes, Vector3 o, Vector3 d)
        {
            float best = float.MaxValue; int lab = None;
            var unit = new Bounds(Vector3.zero, Vector3.one);
            foreach (var b in boxes)
            {
                var w2l = b.t.worldToLocalMatrix;
                Vector3 lo = w2l.MultiplyPoint3x4(o), ld = w2l.MultiplyVector(d);
                if (!unit.IntersectRay(new Ray(lo, ld), out float t)) continue;
                Vector3 hit = b.t.localToWorldMatrix.MultiplyPoint3x4(lo + ld.normalized * t);
                float dist = (hit - o).magnitude;
                if (dist < best && dist < 220f) { best = dist; lab = b.label; }
            }
            return lab;
        }

        static int Label(Color32 c)
        {
            int m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m < 16) return None;
            float k = m * 0.45f;
            bool r = c.r > k, g = c.g > k, b = c.b > k;
            if (r && g && b) return White;
            if (r && g) return Yellow;
            if (g && b) return Cyan;
            if (r && b) return Magenta;
            return r ? Red : g ? Green : Blue;
        }

        static bool Score(string what, int[] obs, int[] exp, float need, bool info = false)
        {
            int want = 0, match = 0, spill = 0;
            var conf = new int[8, 8];
            var per = new int[8];
            for (int i = 0; i < obs.Length; i++)
            {
                if (exp[i] == None) { if (obs[i] != None) spill++; continue; }
                want++; per[exp[i]]++;
                if (obs[i] == exp[i]) match++;
                else conf[exp[i], obs[i]]++;
            }
            float agree = want > 0 ? match / (float)want : 1f;
            string worst = ""; float worstShare = 0f;
            for (int a = 1; a < 8; a++)
                for (int b = 1; b < 8; b++)
                {
                    if (a == b || per[a] == 0) continue;
                    float s = conf[a, b] / (float)per[a];
                    if (s > worstShare) { worstShare = s; worst = $"{Names[a]} seen as {Names[b]} {s * 100f:0}%"; }
                }
            bool ok = agree >= need && worstShare < 0.10f;
            Debug.Log($"[ReflMap] {(info ? "info" : ok ? "ok  " : "FAIL")} {what}: {agree * 100f:0.0}% of {want} coloured texels right (need {need * 100f:0}%)"
                      + (worst.Length > 0 ? $", worst: {worst}" : "") + $", colour where none belongs {spill}");
            return ok;
        }

        // ------------------------------------------------------------------
        //  DRIVE: the owner's arrows, on a real body
        // ------------------------------------------------------------------
        /// <summary>The owner's two arrows, measured. The Neighborhood's player
        /// car dressed as the '69 Charger, everything else in the scene off, a
        /// row of white posts 6 m out down each side of a street (1.2 m wide,
        /// 8 m tall, every 5 m, with colliders - the probe's distance rays see
        /// them), the car driven a quarter metre a step at a 37-degree heading.
        /// Two lenses: the game's chase lens and a rear three-quarter from the
        /// left. On the roof, the left flank and the right flank (split by
        /// the body's own normals), the reflection alone (debug 6) is summed
        /// into a luminance profile along each of the car's screen axes, and
        /// consecutive steps are matched by the shift that lines the profiles
        /// up best: BACK (down the body, front to tail - the owner's red
        /// arrow) and RIGHT (across it - the blue arrow), median px a step.
        /// A mirror shows posts beside a door sliding to the tail as the car
        /// passes them; a flank that does not (|BACK| under a pixel a step)
        /// FAILS.</summary>
        static bool Drive(string dir, string tag)
        {
            string path = "Assets/PSXRacing/Scenes/Neighborhood.unity";
            if (!File.Exists(path)) { Debug.LogError("[ReflMap] missing " + path); return false; }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var carGo = GameObject.Find("RX-7 Player");
            if (carGo == null) carGo = Object.FindFirstObjectByType<CarController>()?.gameObject;
            var body = carGo != null ? carGo.GetComponentInChildren<CarBody>(true) : null;
            string key = Env("PSX_REFLMAP_CAR", "charger_69");
            var def = CarModelLibrary.Load(key);
            if (body == null || def == null) { Debug.LogError("[ReflMap] no car body or no model " + key); return false; }
            carGo.SetActive(true);
            body.Apply(def, 0);
            var carRoot = carGo.transform.root.gameObject;
            foreach (var r in scene.GetRootGameObjects()) if (r != carRoot) r.SetActive(false);
            Shader.SetGlobalFloat("_PSXFogNear", 10000f);
            Shader.SetGlobalFloat("_PSXFogFar", 20000f);
            Shader.SetGlobalFloat("_PSXToneOn", 0f);
            Shader.SetGlobalFloat("_PSXEmitWrite", 0f);
            Shader.SetGlobalFloat("_PSXSnap", 0f);

            var viewGo = new GameObject("DriveView");
            var view = viewGo.AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.black;
            view.nearClipPlane = 0.1f;
            view.farClipPlane = 500f;
            var vd = viewGo.AddComponent<UniversalAdditionalCameraData>();
            vd.renderPostProcessing = false;
            vd.antialiasing = AntialiasingMode.None;

            var car = carGo.transform;
            var heading = Quaternion.Euler(0f, 37f, 0f);
            Vector3 origin = new Vector3(5f, 0f, 3f), fwd = heading * Vector3.forward, right = heading * Vector3.right;
            var postMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            postMat.SetColor("_BaseColor", Color.white); postMat.color = Color.white;
            foreach (int side in new[] { -1, 1 })
                for (float z = -40f; z <= 80f; z += 5f)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Post";
                    go.GetComponent<MeshRenderer>().sharedMaterial = postMat;
                    go.transform.SetPositionAndRotation(origin + right * (side * 6f) + fwd * z + Vector3.up * 4f, heading);
                    go.transform.localScale = new Vector3(1.2f, 8f, 1.2f);
                }
            // what the probe's distance rays meet between the posts: an unseen
            // wall just behind each row, as a street's frontages would be
            foreach (int side in new[] { -1, 1 })
            {
                var wall = new GameObject("PostWall");
                wall.transform.SetPositionAndRotation(origin + right * (side * 6.7f) + fwd * 20f + Vector3.up * 4f, heading);
                wall.AddComponent<BoxCollider>().size = new Vector3(0.2f, 8f, 130f);
            }

            const int W = 1280, H = 720, steps = 24;
            const float stepM = 0.25f;
            view.aspect = W / (float)H;
            var frame = ChaseCamera.FrameOf(carGo);
            var dials = ChaseCamera.LiveDials();
            bool ok = true;
            foreach (string lens in new[] { "chase", "rear34" })
            {
                void Pose(int s)
                {
                    car.SetPositionAndRotation(origin + fwd * (s * stepM), heading);
                    Physics.SyncTransforms();
                    if (lens == "chase")
                    {
                        ChaseCamera.SteadyPose(ChaseCamera.View.Chase, W / (float)H, 20f, 40f, car, frame, dials,
                                               out var eye, out var rot, out var fov, out _);
                        view.transform.SetPositionAndRotation(eye, rot);
                        view.fieldOfView = fov;
                    }
                    else
                    {
                        var eye = car.TransformPoint(new Vector3(-2.6f, 1.5f, -4.4f));
                        view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(car.position + Vector3.up * 0.8f + fwd * 0.6f - eye));
                        view.fieldOfView = 40f;
                    }
                }

                // the body's pixels and their normals (debug 2 = N * 0.5 + 0.5), posts off
                Pose(0);
                var posts = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                foreach (var p in posts) if (p.name == "Post") p.enabled = false;
                CarReflections.Off();
                Shader.SetGlobalFloat("_PSXPaintDebug", 2f);
                var nTex = Shoot(view, W, H);
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                foreach (var p in posts) if (p.name == "Post") p.enabled = true;
                var np = nTex.GetPixels32();
                Object.DestroyImmediate(nTex);
                // regions: 0 none, 1 roof (and the glass on top), 2 left flank, 3 right flank
                var region = new int[np.Length];
                for (int i = 0; i < np.Length; i++)
                {
                    var c = np[i];
                    if (c.r + c.g + c.b < 30) continue;
                    var n = new Vector3(Mathf.GammaToLinearSpace(c.r / 255f), Mathf.GammaToLinearSpace(c.g / 255f), Mathf.GammaToLinearSpace(c.b / 255f)) * 2f - Vector3.one;
                    float side = Vector3.Dot(n, right);
                    region[i] = n.y > 0.75f ? 1 : side < -0.5f ? 2 : side > 0.5f ? 3 : 0;
                }
                // the car's own screen axes: BACK (its nose to its tail) and RIGHT
                Vector2 Scr(Vector3 w) { var p = view.WorldToViewportPoint(w); return new Vector2(p.x * W, p.y * H); }
                Vector3 mid = car.position + Vector3.up * 1.0f;
                Vector2 backAx = (Scr(mid - fwd * 1.5f) - Scr(mid + fwd * 1.5f)).normalized;
                Vector2 rightAx = (Scr(mid + right * 0.8f) - Scr(mid - right * 0.8f)).normalized;

                const int Bins = 900, Half = 450;
                // FLASHING: the reflection's brightness averaged over 16-pixel
                // blocks of one part of the body. A pattern sliding along a
                // panel keeps a block's average; a whole facet going from the
                // sky to a post at once changes it - that is the flash.
                const int Blk = 16;
                int bw = W / Blk, bh = H / Blk;
                var blkSum = new float[bw * bh]; var blkPrev = new float[bw * bh]; var blkN = new int[bw * bh];
                var blkRegion = new int[bw * bh];
                for (int i = 0; i < region.Length; i++) blkN[(i / W / Blk) * bw + (i % W) / Blk]++;
                for (int k = 0; k < blkRegion.Length; k++) blkRegion[k] = -1;
                {
                    // a block counts when one part of the body fills it
                    var cnt = new int[bw * bh, 4];
                    for (int i = 0; i < region.Length; i++) cnt[(i / W / Blk) * bw + (i % W) / Blk, region[i]]++;
                    for (int k = 0; k < blkRegion.Length; k++)
                        for (int rg = 1; rg <= 3; rg++) if (cnt[k, rg] >= Blk * Blk * 0.9f) blkRegion[k] = rg;
                }
                var flash = new double[4]; var flashN = new int[4];
                var prof = new float[4, 2, steps, Bins];
                var hits = new int[4, 2, steps, Bins];
                var regionPx = new int[4];
                for (int i = 0; i < region.Length; i++) regionPx[region[i]]++;
                Shader.SetGlobalFloat("_PSXPaintDebug", 6f);   // what the lacquer sees, alone
                for (int s = 0; s < steps; s++)
                {
                    Pose(s);
                    CarReflections.CaptureNow(car, view);
                    var tex = Shoot(view, W, H);
                    var px = tex.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                    {
                        int rg = region[i];
                        if (rg == 0) continue;
                        var c = px[i];
                        float lum = 0.30f * c.r + 0.59f * c.g + 0.11f * c.b;
                        var q = new Vector2(i % W - W * 0.5f, i / W - H * 0.5f);
                        int bb = Mathf.Clamp(Mathf.RoundToInt(Vector2.Dot(q, backAx)) + Half, 0, Bins - 1);
                        int br = Mathf.Clamp(Mathf.RoundToInt(Vector2.Dot(q, rightAx)) + Half, 0, Bins - 1);
                        prof[rg, 0, s, bb] += lum; hits[rg, 0, s, bb]++;
                        prof[rg, 1, s, br] += lum; hits[rg, 1, s, br]++;
                    }
                    System.Array.Clear(blkSum, 0, blkSum.Length);
                    for (int i = 0; i < px.Length; i++)
                    {
                        var c = px[i];
                        blkSum[(i / W / Blk) * bw + (i % W) / Blk] += 0.30f * c.r + 0.59f * c.g + 0.11f * c.b;
                    }
                    for (int k = 0; k < blkSum.Length; k++)
                    {
                        float m = blkSum[k] / (Blk * Blk);
                        if (s > 0 && blkRegion[k] > 0) { flash[blkRegion[k]] += Mathf.Abs(m - blkPrev[k]); flashN[blkRegion[k]]++; }
                        blkPrev[k] = m;
                    }
                    File.WriteAllBytes(Path.Combine(dir, $"reflmap_{tag}_drive_{lens}_{s:00}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    // ...and the paint as it is, for the film
                    Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                    var seen = Shoot(view, W, H);
                    File.WriteAllBytes(Path.Combine(dir, $"reflmap_{tag}_drive_{lens}_look_{s:00}.png"), seen.EncodeToPNG());
                    Object.DestroyImmediate(seen);
                    Shader.SetGlobalFloat("_PSXPaintDebug", 6f);
                }
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                // the finished look, for the owner: the same car, paint as it is
                Pose(steps / 2);
                CarReflections.CaptureNow(car, view);
                var look = Shoot(view, W, H);
                File.WriteAllBytes(Path.Combine(dir, $"reflmap_{tag}_drive_{lens}_look.png"), look.EncodeToPNG());
                Object.DestroyImmediate(look);
                CarReflections.Off();

                string[] regName = { "", "roof", "left flank", "right flank" };
                for (int rg = 1; rg <= 3; rg++)
                {
                    if (regionPx[rg] < 400) { Debug.Log($"[ReflMap] DRIVE {lens} {regName[rg]}: not in view ({regionPx[rg]} px)"); continue; }
                    float back = Flow(prof, hits, rg, 0, steps, Bins), across = Flow(prof, hits, rg, 1, steps, Bins);
                    // a flank this lens sees well must stream to the tail
                    bool judged = rg >= 2 && regionPx[rg] >= 3000;
                    bool want = !judged || back >= 1f;
                    ok &= want;
                    Debug.Log($"[ReflMap] DRIVE {(judged ? (want ? "ok  " : "FAIL") : "    ")} {lens} {regName[rg]} ({regionPx[rg]} px): the reflection moves"
                              + $" {back:+0.0;-0.0} px/step BACK (red arrow), {across:+0.0;-0.0} px/step RIGHT (blue arrow);"
                              + $" flashing {(flashN[rg] > 0 ? flash[rg] / flashN[rg] : 0):0.0} (mean change of a 16-px block's brightness a step, 0-255, {flashN[rg] / (steps - 1)} blocks)");
                }
            }
            return ok;
        }

        /// <summary>The median shift (px a step) that best lines up each
        /// step's luminance profile with the step before it, along one axis
        /// (0 BACK, 1 RIGHT) of one part of the body.</summary>
        static float Flow(float[,,,] prof, int[,,,] hits, int rg, int ax, int steps, int bins)
        {
            var shifts = new List<float>();
            for (int s = 1; s < steps; s++)
            {
                var a = new float[bins]; var b = new float[bins]; var ok = new bool[bins];
                for (int k = 0; k < bins; k++)
                {
                    int ha = hits[rg, ax, s - 1, k], hb = hits[rg, ax, s, k];
                    ok[k] = ha > 0 && hb > 0;
                    a[k] = ha > 0 ? prof[rg, ax, s - 1, k] / ha : 0f;
                    b[k] = hb > 0 ? prof[rg, ax, s, k] / hb : 0f;
                }
                int best = 0; float bestScore = float.MinValue;
                for (int d = -24; d <= 24; d++)
                {
                    double sum = 0, sa = 0, sb = 0; int n = 0;
                    for (int k = 0; k < bins; k++)
                    {
                        int j = k - d;
                        if (j < 0 || j >= bins || !ok[k] || !ok[j]) continue;
                        sa += a[j]; sb += b[k]; n++;
                    }
                    if (n < 20) continue;
                    double ma = sa / n, mb = sb / n, va = 0, vb = 0;
                    for (int k = 0; k < bins; k++)
                    {
                        int j = k - d;
                        if (j < 0 || j >= bins || !ok[k] || !ok[j]) continue;
                        sum += (a[j] - ma) * (b[k] - mb); va += (a[j] - ma) * (a[j] - ma); vb += (b[k] - mb) * (b[k] - mb);
                    }
                    if (va < 1e-3 || vb < 1e-3) continue;
                    float score = (float)(sum / System.Math.Sqrt(va * vb));
                    if (score > bestScore) { bestScore = score; best = d; }
                }
                if (bestScore > 0.3f) shifts.Add(best);
            }
            if (shifts.Count == 0) return 0f;
            shifts.Sort();
            return shifts[shifts.Count / 2];
        }

        // ------------------------------------------------------------------
        //  STREET: the owner's frame, as a film strip
        // ------------------------------------------------------------------
        /// <summary>The owner's frame (2026-10-23): the '69 Charger down the
        /// Neighborhood street at 45 mph, 75 frames a second, the faces
        /// refreshed two a frame as the game does, the chase lens. Writes the
        /// car's crop every PSX_REFLMAP_EVERY frames (paint as it is, and the
        /// reflection alone - debug 6) for a contact sheet / film.</summary>
        static void Street(string dir, string tag)
        {
            string path = "Assets/PSXRacing/Scenes/Neighborhood.unity";
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var cam = GameObject.Find("PSXCamera")?.GetComponent<Camera>();
            var carGo = GameObject.Find("RX-7 Player");
            if (carGo == null) carGo = Object.FindFirstObjectByType<CarController>()?.gameObject;
            var street = GameObject.Find("NbStreet")?.GetComponent<Renderer>();
            var body = carGo != null ? carGo.GetComponentInChildren<CarBody>(true) : null;
            string key = Env("PSX_REFLMAP_CAR", "charger_69");
            var def = CarModelLibrary.Load(key);
            if (cam == null || body == null || street == null || def == null) { Debug.LogError("[ReflMap] street: missing camera/car/street/model"); return; }
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
            foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None)) cv.PreviewShow(false);
            SunGlareFx.PreviewSet(null, 0f, 0f, true);
            carGo.SetActive(true);
            body.Apply(def, Mathf.Clamp(EnvInt("PSX_REFLMAP_SKIN", 0), 0, def.skinNames.Length - 1));
            var car = carGo.transform;
            var b = street.bounds;
            Vector3 fwd = Vector3.back;
            var start = new Vector3(b.center.x + 1.3f, 0f, b.max.z - 30f);
            const float speed = 20f, dt = 1f / 75f;
            int frames = Mathf.Clamp(EnvInt("PSX_REFLMAP_FRAMES", 90), 10, 400);
            int every = Mathf.Max(1, EnvInt("PSX_REFLMAP_EVERY", 3));
            const int W = 1920, H = 1080;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(Env("PSX_REFLMAP_HOUR", "SUNSET"), System.StringComparison.OrdinalIgnoreCase));
            TimeOfDay.Apply(hour >= 0 ? hour : TimeOfDay.Afternoon, sun);
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            var frame = ChaseCamera.FrameOf(carGo);
            var dials = ChaseCamera.LiveDials();
            var keepPixels = PSXQuality.Current;
            try
            {
                PSXQuality.Current = PSXPixels.HD;
                for (int f = 0; f < frames; f++)
                {
                    var at = start + fwd * (speed * dt * f);
                    at.y = GroundY(at, b.max.y);
                    car.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));
                    ChaseCamera.SteadyPose(ChaseCamera.View.Chase, W / (float)H, speed, 40f, car, frame, dials,
                                           out var eye, out var rot, out var fov, out _);
                    cam.transform.SetPositionAndRotation(eye, rot);
                    cam.fieldOfView = fov;
                    if (f == 0) CarReflections.CaptureNow(car, cam); else Step(car, cam, 2);
                    if (f % every != 0) continue;
                    // the crop: the car's box on screen, padded
                    var bounds = new Bounds(car.position + Vector3.up * 0.7f, Vector3.zero);
                    foreach (var r in carGo.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                    var lo = cam.WorldToViewportPoint(bounds.center);
                    int cw = 640, ch = 360;
                    int cx = Mathf.Clamp(Mathf.RoundToInt(lo.x * W) - cw / 2, 0, W - cw);
                    int cy = Mathf.Clamp(Mathf.RoundToInt(lo.y * H) - ch / 2 + 20, 0, H - ch);
                    foreach (int dbg in new[] { 0, 6 })
                    {
                        Shader.SetGlobalFloat("_PSXPaintDebug", dbg);
                        var tex = Shoot(cam, W, H);
                        var crop = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                        crop.SetPixels(tex.GetPixels(cx, cy, cw, ch));
                        crop.Apply();
                        File.WriteAllBytes(Path.Combine(dir, $"reflmap_{tag}_street_{(dbg == 0 ? "look" : "refl")}_{f:000}.png"), crop.EncodeToPNG());
                        Object.DestroyImmediate(tex); Object.DestroyImmediate(crop);
                    }
                    Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                }
                Debug.Log($"[ReflMap] STREET {key}: {frames} frames, every {every} written");
            }
            finally
            {
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                PSXQuality.Current = keepPixels;
                CarReflections.Off();
            }
        }

        // ------------------------------------------------------------------
        //  WALL: the owner's arrows where the world is close
        // ------------------------------------------------------------------
        /// <summary>The '69 Charger on BlueRidge at noon, on the straightest
        /// run with the stone wall on its left (ReflectionShots' "auto" spot),
        /// driven 0.4 m a step for PSX_REFLMAP_WALLSTEPS (24) steps; the chase lens
        /// and a rear three-quarter from the wall side, rigid on the car, a
        /// whole set of faces every step. Writes each step as it is and as the
        /// reflection alone (debug 6), for films:
        /// reflmap_&lt;tag&gt;_wall_&lt;lens&gt;_&lt;look|refl&gt;_NN.png.</summary>
        static void Wall(string dir, string tag)
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "BlueRidge");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[ReflMap] wall: BlueRidge did not open"); return; }
            string key = Env("PSX_REFLMAP_CAR", "charger_69");
            var body = player.GetComponentInChildren<CarBody>(true);
            var cdef = CarModelLibrary.Load(key);
            if (body == null || cdef == null) { Debug.LogError("[ReflMap] wall: cannot dress " + key); return; }
            body.Apply(cdef, 0);
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
            var car = player.transform;
            typeof(ReflectionShots).GetMethod("Place", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { car, "auto" });
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            TimeOfDay.Apply(TimeOfDay.Noon, sun);
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            Vector3 start = car.position, fwd = car.forward;
            var rot0 = car.rotation;
            int steps = Mathf.Clamp(EnvInt("PSX_REFLMAP_WALLSTEPS", 24), 4, 120);
            const float stepM = 0.4f;
            const int W = 1280, H = 720;
            var frame = ChaseCamera.FrameOf(player);
            var dials = ChaseCamera.LiveDials();
            float fov0 = cam.fieldOfView;
            var keepPixels = PSXQuality.Current;
            try
            {
                PSXQuality.Current = PSXPixels.HD;
                foreach (string lens in new[] { "chase", "rear34" })
                    for (int st = 0; st < steps; st++)
                    {
                        var at = start + fwd * (st * stepM);
                        if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var g, 8f)) at.y = g.point.y;
                        car.SetPositionAndRotation(at, rot0);
                        Physics.SyncTransforms();
                        Vector3 eye; Quaternion rot; float fov = fov0;
                        if (lens == "chase")
                            ChaseCamera.SteadyPose(ChaseCamera.View.Chase, W / (float)H, 20f, 40f, car, frame, dials, out eye, out rot, out fov, out _);
                        else
                        {
                            eye = car.TransformPoint(new Vector3(-2.3f, 1.55f, -3.9f));
                            rot = Quaternion.LookRotation(car.position + Vector3.up * 0.9f - eye);
                        }
                        cam.transform.SetPositionAndRotation(eye, rot);
                        cam.fieldOfView = fov;
                        CarReflections.CaptureNow(car, cam);
                        foreach (int dbg in new[] { 0, 6 })
                        {
                            Shader.SetGlobalFloat("_PSXPaintDebug", dbg);
                            var tex = Shoot(cam, W, H);
                            File.WriteAllBytes(Path.Combine(dir, $"reflmap_{tag}_wall_{lens}_{(dbg == 0 ? "look" : "refl")}_{st:00}.png"), tex.EncodeToPNG());
                            Object.DestroyImmediate(tex);
                        }
                        Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                    }
                Debug.Log($"[ReflMap] WALL {key}: {steps} steps of {stepM} m, chase + rear34");
            }
            finally
            {
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                cam.fieldOfView = fov0;
                PSXQuality.Current = keepPixels;
                CarReflections.Off();
            }
        }

        // ------------------------------------------------------------------
        //  FLICKER
        // ------------------------------------------------------------------
        static void Flicker(string dir, string tag)
        {
            string path = "Assets/PSXRacing/Scenes/Neighborhood.unity";
            if (!File.Exists(path)) { Debug.LogError("[ReflMap] missing " + path); return; }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var cam = GameObject.Find("PSXCamera")?.GetComponent<Camera>();
            var carGo = GameObject.Find("RX-7 Player");
            if (carGo == null) carGo = Object.FindFirstObjectByType<CarController>()?.gameObject;
            var street = GameObject.Find("NbStreet")?.GetComponent<Renderer>();
            if (cam == null || carGo == null || street == null) { Debug.LogError("[ReflMap] Neighborhood: no PSXCamera, car or NbStreet"); return; }
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
            foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None)) cv.PreviewShow(false);
            SunGlareFx.PreviewSet(null, 0f, 0f, true);
            carGo.SetActive(true);
            var car = carGo.transform;
            var b = street.bounds;
            Vector3 fwd = Vector3.back;
            var start = new Vector3(b.center.x + 1.3f, 0f, b.max.z - 30f);
            const float dt = 1f / 75f;
            int frames = Mathf.Clamp(EnvInt("PSX_REFLMAP_FRAMES", 90), 10, 400);
            int lines = Mathf.Clamp(EnvInt("PSX_REFLMAP_LINES", 1080), 240, 1440);
            int width = lines * 16 / 9; if ((width & 1) == 1) width++;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            var frame = ChaseCamera.FrameOf(carGo);
            var dials = ChaseCamera.LiveDials();
            int oldWeather = RaceHandoff.WeatherOverride;
            var keepPixels = PSXQuality.Current;
            try
            {
                PSXQuality.Current = PSXPixels.HD;
                // parked in the rain (anything that moves on the glass is the
                // rain itself, drawn into the reflection), then driving
                foreach (var (weather, speed) in new[] { (Weather.Rain, 0f), (Weather.Rain, 20f), (Weather.Fog, 20f) })
                {
                    string cond = weather.ToString().ToLowerInvariant() + (speed > 0f ? "" : "_parked");
                    RaceHandoff.WeatherOverride = (int)weather;
                    TimeOfDay.Apply(TimeOfDay.Afternoon, sun);
                    if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);

                    void Pose(int f)
                    {
                        var at = start + fwd * (speed * dt * f);
                        at.y = GroundY(at, b.max.y);
                        car.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));
                        ChaseCamera.SteadyPose(ChaseCamera.View.Chase, width / (float)lines, speed, 40f, car, frame, dials,
                                               out var eye, out var rot, out var fov, out _);
                        cam.transform.SetPositionAndRotation(eye, rot);
                        cam.fieldOfView = fov;
                    }

                    // the car's pixels (the pose is rigid, so they hold still):
                    // what changes when the car is hidden; glass = its dark,
                    // colourless sheet texels (PSX/CarPaint's own rule)
                    Pose(0);
                    CarReflections.Off();
                    var carR = carGo.GetComponentsInChildren<Renderer>();
                    var with = Shoot(cam, width, lines);
                    foreach (var r in carR) r.forceRenderingOff = true;
                    var without = Shoot(cam, width, lines);
                    foreach (var r in carR) r.forceRenderingOff = false;
                    Shader.SetGlobalFloat("_PSXPaintDebug", 5f);
                    var sheetShot = Shoot(cam, width, lines);
                    Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                    var pw = with.GetPixels32(); var po = without.GetPixels32(); var ps = sheetShot.GetPixels32();
                    var glass = new List<int>(); var body = new List<int>();
                    for (int i = 0; i < pw.Length; i++)
                    {
                        if (Mathf.Abs(pw[i].r - po[i].r) + Mathf.Abs(pw[i].g - po[i].g) + Mathf.Abs(pw[i].b - po[i].b) < 12) continue;
                        var s = ps[i];
                        int hi = Mathf.Max(s.r, Mathf.Max(s.g, s.b)), lo = Mathf.Min(s.r, Mathf.Min(s.g, s.b));
                        bool isGlass = hi < 120 && (hi - lo) < Mathf.Max(hi, 20) * 0.3f;
                        (isGlass ? glass : body).Add(i);
                    }
                    Object.DestroyImmediate(with); Object.DestroyImmediate(without); Object.DestroyImmediate(sheetShot);
                    Debug.Log($"[ReflMap] {cond}: car {glass.Count + body.Count} px ({glass.Count} glass), {frames} frames at {speed * 2.237f:0} mph, {1f / dt:0} fps, {width}x{lines}");

                    var modes = new List<string>(Env("PSX_REFLMAP_MODES", "off,game,game_norain,all6,every3").Split(','));
                    foreach (var mode in modes)
                    {
                        if ((mode == "game_norain" || mode == "every3" || mode == "all6") && speed <= 0f) continue;
                        if (mode == "game_norain" && weather != Weather.Rain) continue;
                        if (weather == Weather.Rain) WeatherFx.Preview(Weather.Rain, cam, 2.5f);
                        // (the preview's own instance: a DontSave object the scene search misses)
                        var fx = typeof(WeatherFx).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as WeatherFx;
                        var rainPs = fx != null ? fx.GetComponent<ParticleSystem>() : null;
                        if (weather == Weather.Rain && mode == "game") Debug.Log($"[ReflMap] {cond}: {(rainPs != null ? rainPs.particleCount : -1)} drops falling, layer {(fx != null ? fx.gameObject.layer : -1)}");
                        var rainR = fx != null ? fx.GetComponent<ParticleSystemRenderer>() : null;
                        // THE TYRE SPRAY, as the driving code throws it (edit mode
                        // has no physics to): the game's own TireSpray system, its
                        // own Spawn, at the rear contact patches at its own rate
                        ParticleSystem sprayPs = null; ParticleSystemRenderer sprayR = null;
                        TireSpray spray = null; MethodInfo spawn = null;
                        var sprayBudget = new float[2];
                        if (weather == Weather.Rain && speed > 0f)
                        {
                            spray = carGo.GetComponent<TireSpray>();
                            if (spray == null) spray = carGo.AddComponent<TireSpray>();
                            spray.car = carGo.GetComponent<CarController>();
                            var psField = typeof(TireSpray).GetField("ps", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (psField.GetValue(spray) == null) typeof(TireSpray).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(spray, null);
                            sprayPs = psField.GetValue(spray) as ParticleSystem;
                            spawn = typeof(TireSpray).GetMethod("Spawn", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (sprayPs != null)
                            {
                                sprayPs.Clear(true);
                                sprayR = sprayPs.GetComponent<ParticleSystemRenderer>();
                                OwnCarClip.Write(sprayR, car, carGo.GetComponent<BoxCollider>());
                                if (mode == "game") Debug.Log($"[ReflMap] {cond}: tyre spray on layer {sprayPs.gameObject.layer}");
                            }
                            Random.InitState(20261023);
                        }
                        float[] prevG = null, prev2G = null, prevB = null, prev2B = null;
                        double d1G = 0, d2G = 0, d1B = 0, d2B = 0, dPano = 0; long flashG = 0, nG = 0, n2 = 0, nPano = 0;
                        Color32[] prevPano = null;
                        Texture2D keep = null;
                        for (int f = 0; f < frames; f++)
                        {
                            Pose(f);
                            if (rainPs != null && f > 0)
                            {
                                // WeatherFx.SlabOver + RainLead: 13 m up, 6 m ahead,
                                // led by the camera's travel over a drop's 13/15 s fall
                                fx.transform.position = cam.transform.position + Vector3.up * 13f
                                    + Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized * 6f
                                    + fwd * (speed * 13f / 15f);
                                rainPs.Simulate(dt, true, false, false);
                            }
                            if (sprayPs != null && spawn != null && f > 0)
                            {
                                float rate = Mathf.Clamp01((speed * 3.6f - TireSpray.SpeedStart) / (TireSpray.SpeedFull - TireSpray.SpeedStart)) * TireSpray.RateAtFull;
                                for (int w = 0; w < 2; w++)
                                {
                                    var at = car.TransformPoint(new Vector3(w == 0 ? -0.78f : 0.78f, 0f, -1.35f));
                                    at.y = GroundY(at, b.max.y);
                                    var c = new CarController.WheelContact { grounded = true, point = at, normal = Vector3.up, forward = car.forward };
                                    sprayBudget[w] += rate * dt;
                                    int n = Mathf.Min(Mathf.FloorToInt(sprayBudget[w]), 4);
                                    sprayBudget[w] -= n;
                                    for (int j = 0; j < n; j++) spawn.Invoke(spray, new object[] { c, fwd * speed, 1f });
                                }
                                sprayPs.Simulate(dt, true, false, false);
                            }
                            if (mode == "off") CarReflections.Off();
                            else
                            {
                                // game_norain: the rain and the spray kept out of the faces by
                                // hand (what the NoReflectLayer does in the game now)
                                bool hideRain = mode != "game";
                                if (hideRain && rainR != null) rainR.forceRenderingOff = true;
                                if (hideRain && sprayR != null) sprayR.forceRenderingOff = true;
                                // every3: all six from one eye, every third frame (a
                                // whole set at the game's 2-a-frame rate, never mixed)
                                if (f == 0 || mode == "all6" || (mode == "every3" && f % 3 == 0)) CarReflections.CaptureNow(car, cam);
                                else if (mode != "every3") Step(car, cam, 2);
                                if (hideRain && rainR != null) rainR.forceRenderingOff = false;
                                if (hideRain && sprayR != null) sprayR.forceRenderingOff = false;
                            }
                            if (mode != "off" && CarReflections.Panorama != null)
                            {
                                // what the paint reads: how much of it changed since the last frame
                                var pt = Read(CarReflections.Panorama);
                                var pp = pt.GetPixels32();
                                Object.DestroyImmediate(pt);
                                if (prevPano != null && prevPano.Length == pp.Length)
                                {
                                    long ch = 0;
                                    for (int i = 0; i < pp.Length; i++)
                                        if (Mathf.Abs(pp[i].r - prevPano[i].r) + Mathf.Abs(pp[i].g - prevPano[i].g) + Mathf.Abs(pp[i].b - prevPano[i].b) > 24) ch++;
                                    dPano += ch / (double)pp.Length; nPano++;
                                }
                                prevPano = pp;
                            }
                            var tex = Shoot(cam, width, lines);
                            var px = tex.GetPixels32();
                            float[] g = Lum(px, glass), bo = Lum(px, body);
                            if (prevG != null)
                            {
                                for (int i = 0; i < g.Length; i++) { float dd = Mathf.Abs(g[i] - prevG[i]); d1G += dd; if (dd > 20f) flashG++; }
                                for (int i = 0; i < bo.Length; i++) d1B += Mathf.Abs(bo[i] - prevB[i]);
                                nG++;
                            }
                            if (prev2G != null)
                            {
                                for (int i = 0; i < g.Length; i++) d2G += Mathf.Abs(g[i] - 2f * prevG[i] + prev2G[i]);
                                for (int i = 0; i < bo.Length; i++) d2B += Mathf.Abs(bo[i] - 2f * prevB[i] + prev2B[i]);
                                n2++;
                            }
                            prev2G = prevG; prevG = g; prev2B = prevB; prevB = bo;
                            if (f == frames / 2) { if (keep != null) Object.DestroyImmediate(keep); keep = tex; }
                            else Object.DestroyImmediate(tex);
                        }
                        float gl = Mathf.Max(glass.Count, 1), bl = Mathf.Max(body.Count, 1);
                        Debug.Log($"[ReflMap] FLICKER {cond,-11} {mode,-11}: glass d1 {d1G / (nG * gl):0.00} d2 {d2G / (n2 * gl):0.00} jumps>20 {flashG * 100.0 / (nG * gl):0.00}%"
                                  + $" | paint d1 {d1B / (nG * bl):0.00} d2 {d2B / (n2 * bl):0.00}"
                                  + (nPano > 0 ? $" | panorama texels changing a frame {dPano * 100.0 / nPano:0.00}%" : ""));
                        if (keep != null)
                        {
                            File.WriteAllBytes(Path.Combine(dir, $"reflmap_{tag}_{cond}_{mode}.png"), keep.EncodeToPNG());
                            Object.DestroyImmediate(keep);
                        }
                        WeatherFx.PreviewClear();
                    }

                    // the cost at the chase eye: a full frame with the paint on
                    // the sky lookup vs the same frame + the game's face slice
                    if (speed > 0f) { Pose(0); Cost(cam, car, width, lines); }
                }
            }
            finally
            {
                WeatherFx.PreviewClear();
                RaceHandoff.WeatherOverride = oldWeather;
                PSXQuality.Current = keepPixels;
                CarReflections.Off();
            }
        }

        static void Cost(Camera cam, Transform car, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            CarReflections.CaptureNow(car, cam);
            var off = new List<double>(); var on = new List<double>();
            var sw = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < 60; i++)
            {
                bool refl = (i & 1) == 1;
                sw.Restart();
                if (refl) Step(car, cam, 2); else CarReflections.Off();
                Submit(cam, rt);
                Drain(rt, one);
                sw.Stop();
                if (i >= 6) (refl ? on : off).Add(sw.Elapsed.TotalMilliseconds);
            }
            off.Sort(); on.Sort();
            double mOff = 0, mOn = 0;
            foreach (var x in off) mOff += x;
            foreach (var x in on) mOn += x;
            mOff /= off.Count; mOn /= on.Count;
            Debug.Log($"[ReflMap] COST {w}x{h} {SystemInfo.graphicsDeviceType}: frame {off[off.Count / 2]:0.00} ms without, {on[on.Count / 2]:0.00} ms with the reflections (2 faces a frame) = +{on[on.Count / 2] - off[off.Count / 2]:0.00} ms median, +{mOn - mOff:0.00} ms mean (each render drained)");
            CarReflections.Off();
            Object.DestroyImmediate(one);
            rt.Release(); Object.DestroyImmediate(rt);
        }

        static float[] Lum(Color32[] px, List<int> idx)
        {
            var o = new float[idx.Count];
            for (int i = 0; i < o.Length; i++) { var c = px[idx[i]]; o[i] = 0.30f * c.r + 0.59f * c.g + 0.11f * c.b; }
            return o;
        }

        // ------------------------------------------------------------------
        //  plumbing
        // ------------------------------------------------------------------
        static CarReflections Inst() =>
            (CarReflections)typeof(CarReflections).GetField("inst", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        /// <summary>One frame of the game's refresh: <paramref name="n"/> faces
        /// round <paramref name="car"/>, restitched, published.</summary>
        static void Step(Transform car, Camera view, int n)
        {
            var m = typeof(CarReflections).GetMethod("RenderAround", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(Inst(), new object[] { car, view, n });
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        static Texture2D Shoot(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            Submit(cam, rt);
            var shown = PSXScreenshotTool.Dithered(rt);
            var tex = Read(shown);
            if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }

        static void Submit(Camera cam, RenderTexture rt)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            PSXTexDecode.StampAll();
            RenderPipeline.SubmitRenderRequest(cam, request);
        }

        static void Drain(RenderTexture rt, Texture2D one)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            one.Apply();
            RenderTexture.active = prev;
        }

        static float GroundY(Vector3 at, float top)
        {
            if (Physics.Raycast(new Vector3(at.x, top + 50f, at.z), Vector3.down, out var hit, 200f)) return hit.point.y;
            return top;
        }

        /// <summary>A UV sphere with exact normals.</summary>
        static Mesh Sphere(float r, int seg, int rings)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float th = Mathf.PI * i / rings;
                for (int j = 0; j <= seg; j++)
                {
                    float ph = 2f * Mathf.PI * j / seg;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(d * r); n.Add(d); uv.Add(new Vector2(j / (float)seg, i / (float)rings));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a = i * (seg + 1) + j, b2 = a + seg + 1;
                    tri.Add(a); tri.Add(a + 1); tri.Add(b2);
                    tri.Add(a + 1); tri.Add(b2 + 1); tri.Add(b2);
                }
            var m = new Mesh { indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        static string Env(string k, string d)
        {
            string v = System.Environment.GetEnvironmentVariable(k);
            return string.IsNullOrEmpty(v) ? d : v;
        }

        static int EnvInt(string k, int d) => int.TryParse(System.Environment.GetEnvironmentVariable(k), out int v) ? v : d;
    }
}
