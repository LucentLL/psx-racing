using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// THE REFLECTION MAP, CHECKED IN THE PLAYER (2026-10-23; the owner, a
    /// fourth time: the reflections move "horizontally (right to left) across
    /// the car instead of front to back"). ReflectionMapCheck proved the
    /// panorama and the paint unmirrored in the EDITOR, on OpenGL Core and
    /// D3D11 - and the player the owner drives is WebGL, which no editor run
    /// is. This is that test in the player: seven coloured boxes round an eye
    /// far above the world, CarReflections' own capture, then
    ///   PANORAMA  every texel's colour against a CPU trace from the eye;
    ///   BALL      a PSX/CarPaint sphere at the eye seen from a chase lens,
    ///             the lacquer's reflection alone (debug 6), against the
    ///             paint's own read (PSXEnvRay) traced on the CPU.
    /// One line each ("[ReflTour] ok|FAIL ..."), a SHOW line with both
    /// pictures on screen for a screenshot, then "[ReflTour] DONE PASS|FAIL".
    /// tools\refl-tour.mjs serves a built player locally and reads them.
    ///
    /// INERT ON THE SITE: like the door tour, only a player served from this
    /// machine whose URL asks for it (?refltour), never in the editor.
    /// </summary>
    public class ReflTour : MonoBehaviour
    {
        const int None = 0, Red = 1, Green = 2, Blue = 3, Yellow = 4, Cyan = 5, Magenta = 6, White = 7;
        static readonly string[] Names = { "-", "RED", "GREEN", "BLUE", "YELLOW", "CYAN", "MAGENTA", "WHITE" };
        static readonly Color[] Cols =
        {
            Color.black, Color.red, Color.green, Color.blue, new Color(1, 1, 0), new Color(0, 1, 1), new Color(1, 0, 1), Color.white,
        };
        struct Box { public int label; public Transform t; }

        Texture2D showPano, showBall, showBallWant;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            if (!RequestedBy(Application.absoluteURL)) return;
            var go = new GameObject("ReflTour");
            DontDestroyOnLoad(go);
            go.AddComponent<ReflTour>();
        }

        /// <summary>Local hosts only, and only with a <c>refltour</c> key.</summary>
        public static bool RequestedBy(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var u)) return false;
            if (u.Scheme != "http" && u.Scheme != "https") return false;
            string host = u.Host.ToLowerInvariant();
            if (host != "127.0.0.1" && host != "localhost" && host != "[::1]" && host != "::1") return false;
            foreach (var part in u.Query.TrimStart('?').Split('&'))
                if (string.Equals(part.Split('=')[0], "refltour", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // the rig, kept between frames
        Transform root, car;
        Camera view;
        List<Box> boxes;
        Vector3 P;
        const float EyeUp = 1.15f, BallR = 0.9f;
        static readonly Quaternion[] FaceRot =
        {
            Quaternion.LookRotation(Vector3.right, Vector3.up),
            Quaternion.LookRotation(Vector3.left, Vector3.up),
            Quaternion.LookRotation(Vector3.up, Vector3.back),
            Quaternion.LookRotation(Vector3.down, Vector3.forward),
            Quaternion.LookRotation(Vector3.forward, Vector3.up),
            Quaternion.LookRotation(Vector3.back, Vector3.up),
        };

        IEnumerator Start()
        {
            // the front end up and settled first
            for (float t = 0f; t < 6f; t += Time.unscaledDeltaTime) yield return null;
            Debug.Log($"[ReflTour] BEGIN {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName}), uvStartsAtTop {SystemInfo.graphicsUVStartsAtTop}");
            bool ok = true, built = false;
            try { built = Setup(); }
            catch (System.Exception e) { Debug.Log("[ReflTour] FAIL setup " + e); }
            if (!built) ok = false;
            else
            {
                // how the six views are drawn, as the game draws them (a render
                // request, 2 a frame at MAX) and the other ways, one line a face
                var variants = new (int path, int per)[] { (0, 6), (0, 2), (0, 1), (1, 6), (1, 2) };
                foreach (var v in variants)
                {
                    CarReflections.FacePath = v.path;
                    bool whole = false, fresh = true;
                    int frames = 0;
                    while (!whole && frames < 20)
                    {
                        try { whole = CarReflections.StepNow(car, view, v.per, fresh); }
                        catch (System.Exception e) { Debug.Log("[ReflTour] FAIL step " + e); break; }
                        fresh = false;
                        if (!whole) { frames++; yield return null; }
                    }
                    string tag = $"{(v.path == 0 ? "request" : "Render()")} {v.per}/frame";
                    try { ok &= ScoreViews(tag, v.path == 0 && v.per == 2); }
                    catch (System.Exception e) { Debug.Log("[ReflTour] FAIL score " + e); ok = false; }
                    yield return null;
                }
                // the paint, on the game's path and on Camera.Render
                foreach (int path in new[] { 0, 1 })
                {
                    CarReflections.FacePath = path;
                    try { ok &= Ball(path == 0 ? "request" : "Render()", path == 0); }
                    catch (System.Exception e) { Debug.Log("[ReflTour] FAIL ball " + e); ok = false; }
                    yield return null;
                }
            }
            CarReflections.FacePath = 0;
            CarReflections.Off();
            Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
            if (root != null) Destroy(root.gameObject);
            Debug.Log("[ReflTour] SHOW");
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime) yield return null;
            Debug.Log("[ReflTour] DONE " + (ok ? "PASS" : "FAIL"));
        }

        bool Setup()
        {
            // the paint's own vertex fog would paint the ball the fog colour
            Shader.SetGlobalFloat("_PSXFogNear", 10000f);
            Shader.SetGlobalFloat("_PSXFogFar", 20000f);
            Shader.SetGlobalFloat("_PSXToneOn", 0f);
            Shader.SetGlobalFloat("_PSXEmitWrite", 0f);
            Shader.SetGlobalFloat("_PSXPaintWorld", 0f);

            root = new GameObject("ReflTourRig").transform;
            view = new GameObject("ReflTourView").AddComponent<Camera>();
            view.transform.SetParent(root, false);
            view.enabled = false;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.black;
            view.nearClipPlane = 0.1f;
            view.farClipPlane = 500f;
            view.fieldOfView = 30f;
            var vd = view.gameObject.AddComponent<UniversalAdditionalCameraData>();
            vd.renderPostProcessing = false;
            vd.antialiasing = AntialiasingMode.None;

            // far above any scene (the probe sees 220 m)
            car = new GameObject("ReflTourCar").transform;
            car.SetParent(root, false);
            var ball = new GameObject("Ball");
            ball.transform.SetParent(car, false);
            ball.transform.localPosition = new Vector3(0f, EyeUp, 0f);
            ball.AddComponent<MeshFilter>().sharedMesh = Sphere(BallR, 160, 80);
            var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            sheet.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
            sheet.Apply();
            var paintSh = Shader.Find("PSX/CarPaint");
            var flatSh = Shader.Find("Sprites/Default");
            if (paintSh == null || flatSh == null) { Debug.Log($"[ReflTour] FAIL shaders missing (paint {paintSh != null}, flat {flatSh != null})"); return false; }
            var paint = new Material(paintSh);
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
            car.SetPositionAndRotation(new Vector3(3f, 3000f, -7f), Quaternion.identity);
            P = car.position + car.up * EyeUp;
            boxes = new List<Box>();
            foreach (var d in dirs)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "ReflTourBox" + Names[d.label];
                go.transform.SetParent(root, false);
                go.GetComponent<MeshRenderer>().sharedMaterial = new Material(flatSh) { color = Cols[d.label] };
                go.transform.localScale = Vector3.one * 10f;
                go.transform.SetPositionAndRotation(P + d.local * 25f, Quaternion.identity);
                boxes.Add(new Box { label = d.label, t = go.transform });
            }
            return true;
        }

        /// <summary>Each of the six views, then the panorama stitched from them.</summary>
        bool ScoreViews(string tag, bool keep)
        {
            bool ok = true;
            for (int i = 0; i < 6; i++)
            {
                var rt = CarReflections.Face(i);
                if (rt == null) { Debug.Log($"[ReflTour] FAIL {tag} face {i}: none"); ok = false; continue; }
                var tex = Read(rt);
                int w = tex.width, h = tex.height;
                var px = tex.GetPixels32();
                var obs = new int[px.Length]; var exp = new int[px.Length];
                var hist = new int[8];
                Vector3 f = FaceRot[i] * Vector3.forward, r = FaceRot[i] * Vector3.right, u = FaceRot[i] * Vector3.up;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int j = y * w + x;
                        obs[j] = Label(px[j]); hist[obs[j]]++;
                        var d = (f + r * ((x + 0.5f) / w * 2f - 1f) + u * ((y + 0.5f) / h * 2f - 1f)).normalized;
                        exp[j] = Trace(boxes, P, d);
                    }
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < 8; k++) if (hist[k] > 0) sb.Append($" {Names[k]} {hist[k] * 100f / px.Length:0.#}%");
                ok &= Score($"{tag} face {i} ({FaceName(f)}) seen:{sb}", obs, exp, 0.90f);
                Destroy(tex);
            }
            {
                var tex = Read(CarReflections.Panorama);
                int w = tex.width, h = tex.height;
                var px = tex.GetPixels32();
                var obs = new int[px.Length]; var exp = new int[px.Length];
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
                    }
                }
                ok &= Score($"{tag} panorama", obs, exp, 0.90f);
                if (keep) { if (showPano != null) Destroy(showPano); showPano = tex; } else Destroy(tex);
            }
            return ok;
        }

        /// <summary>A PSX/CarPaint ball at the eye from a chase lens: the
        /// lacquer's reflection alone against the paint's own read, traced.</summary>
        bool Ball(string tag, bool keep)
        {
            CarReflections.Off();
            if (!CarReflections.CaptureNow(car, view)) { Debug.Log("[ReflTour] FAIL EnvPano shader missing"); return false; }
            const int S = 480;
            view.aspect = 1f;
            Vector3 C = P;
            Vector3 eye = car.TransformPoint(new Vector3(0f, 2.6f, -5.0f));
            view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(C - eye));
            Shader.SetGlobalFloat("_PSXPaintDebug", 6f);   // what the lacquer sees
            var tex = Shoot(view, S, S);
            Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
            var px = tex.GetPixels32();
            var obs = new List<int>(); var exp = new List<int>();
            var want = new Color32[px.Length];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var ray = view.ViewportPointToRay(new Vector3((x + 0.5f) / S, (y + 0.5f) / S, 0f));
                    Vector3 oc = ray.origin - C;
                    float b = Vector3.Dot(ray.direction, oc), c = oc.sqrMagnitude - BallR * BallR, disc = b * b - c;
                    if (disc <= 0f) continue;
                    Vector3 hit = ray.origin + ray.direction * (-b - Mathf.Sqrt(disc));
                    Vector3 n = (hit - C) / BallR;
                    Vector3 V = (eye - hit).normalized;
                    if (Vector3.Dot(n, V) < 0.06f) continue;   // the very rim: mesh vs sphere
                    Vector3 R = Vector3.Reflect(-V, n);
                    int j = y * S + x;
                    int wl = Trace(boxes, P, EnvRay(hit, R));
                    obs.Add(Label(px[j])); exp.Add(wl);
                    want[j] = Cols[wl] * 0.6f;
                }
            bool ok = Score($"{tag} ball (chase)", obs.ToArray(), exp.ToArray(), 0.90f);
            if (keep)
            {
                showBall = tex;
                showBallWant = new Texture2D(S, S, TextureFormat.RGBA32, false);
                showBallWant.SetPixels32(want); showBallWant.Apply();
            }
            else Destroy(tex);
            return ok;
        }

        static string FaceName(Vector3 f) =>
            f.x > 0.5f ? "+X" : f.x < -0.5f ? "-X" : f.y > 0.5f ? "+Y" : f.y < -0.5f ? "-Y" : f.z > 0.5f ? "+Z" : "-Z";

        void OnGUI()
        {
            if (showPano == null) return;
            float h = Screen.height * 0.45f;
            GUI.DrawTexture(new Rect(0, 0, h * 2f, h), showPano, ScaleMode.StretchToFill, false);
            if (showBall != null) GUI.DrawTexture(new Rect(0, h, h, h), showBall, ScaleMode.StretchToFill, false);
            if (showBallWant != null) GUI.DrawTexture(new Rect(h, h, h, h), showBallWant, ScaleMode.StretchToFill, false);
        }

        // ------------------------------------------------------------------
        //  the editor check's helpers (ReflectionMapCheck), for the player
        // ------------------------------------------------------------------

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

        static bool Score(string what, int[] obs, int[] exp, float need)
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
            Debug.Log($"[ReflTour] {(ok ? "ok  " : "FAIL")} {what}: {agree * 100f:0.0}% of {want} coloured texels right (need {need * 100f:0}%)"
                      + (worst.Length > 0 ? $", worst: {worst}" : "") + $", colour where none belongs {spill}");
            return ok;
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
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var tex = Read(rt);
            rt.Release();
            Destroy(rt);
            return tex;
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
    }
}
