using System.Collections;
using System.Collections.Generic;
using System.Text;
using PSXRacing.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// FIVE POINTS ROUND 2 (2026-11-03): the owner's views, as the game shows
    /// them. PLAYS Charlotte's free roam at noon (no traffic) and, per shot,
    /// either parks the player's car at a place and heading and lets the
    /// game's own chase camera settle behind it ("car"), or puts the camera
    /// itself at an eye looking at a point ("cam": a low oblique, a top-down).
    /// PSX_FP_SHOTS="name;car;x;z;hx;hz|name;cam;ex;ez;tx;tz;eyeH|...".
    /// Renders the game camera through the game's dither (CityPlayCheck's
    /// way) to Screenshots\fp_&lt;name&gt;.png. Uses only what live main
    /// (a7db98e7) has as well, so the same file shoots the before and the after.
    /// </summary>
    public static class FivePointsShots
    {
        internal static readonly StringBuilder log = new StringBuilder();

        [MenuItem("PSX Racing/Five Points Shots (play mode)")]
        public static void Run()
        {
            log.Clear();
            int roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int s = roamIdx >= 0 ? TrackCatalog.SceneIndex(roamIdx) : -1;
            if (s < 0 || s >= scenes.Length || !System.IO.File.Exists(scenes[s].path)) { log.AppendLine("FAIL no Charlotte scene"); Finish(1); return; }
            EditorSceneManager.OpenScene(scenes[s].path);
            bool ok = PSXRacing.LifeSim.CityFrontEnd.FillFreeRoam(PSXRacing.LifeSim.CityFrontEnd.DefaultCarId(), TimeOfDay.Noon, 0, out int scene);
            log.AppendLine("free roam handoff " + (ok ? "filled" : "NOT FILLED"));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("FivePointsShotsRunner").AddComponent<FivePointsShotsRunner>();
        }

        internal static void Finish(int code)
        {
            System.IO.File.WriteAllText("PSXRacing_fp_shots.txt", log.ToString());
            EditorApplication.Exit(code);
        }
    }

    public class FivePointsShotsRunner : MonoBehaviour
    {
        float bornAt;
        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }
        void Update() { if (Time.realtimeSinceStartup - bornAt > 900f) { FivePointsShots.log.AppendLine("HUNG"); FivePointsShots.Finish(1); } }

        static float GroundY(Vector2 q)
        {
            float best = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(q.x, 400f, q.y), Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.attachedRigidbody != null) continue;
                if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
            }
            return best;
        }

        IEnumerator Start()
        {
            yield return null; yield return new WaitForFixedUpdate(); yield return null;
            var mode = CityMode.Instance;
            if (mode == null || mode.player == null) { FivePointsShots.log.AppendLine("FAIL free roam did not start"); FivePointsShots.Finish(1); yield break; }
            var carGo = mode.player.gameObject;
            var car = carGo.GetComponent<CarController>();
            var rb = carGo.GetComponent<Rigidbody>();
            foreach (var mb in carGo.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var spec in (System.Environment.GetEnvironmentVariable("PSX_FP_SHOTS") ?? "").Split('|'))
            {
                var f = spec.Split(';');
                if (f.Length < 6) continue;
                string name = f[0].Trim(), kind = f[1].Trim();
                var a = new Vector2(float.Parse(f[2], ci), float.Parse(f[3], ci));
                var b = new Vector2(float.Parse(f[4], ci), float.Parse(f[5], ci));
                // stream the tiles round the place (the car brings them)
                var at = kind == "car" ? a : b;
                rb.isKinematic = true;
                car.TeleportTo(new Vector3(at.x, 200f, at.y), Quaternion.identity);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 120f && float.IsNaN(GroundY(at))) yield return null;
                for (int i = 0; i < 60; i++) yield return null;
                float gy = GroundY(at);
                if (float.IsNaN(gy)) { FivePointsShots.log.AppendLine($"  {name}: no ground streamed in"); continue; }
                if (kind == "car")
                {
                    var hd = new Vector3(b.x, 0f, b.y).normalized;
                    car.TeleportTo(new Vector3(a.x, gy + 0.4f, a.y), Quaternion.LookRotation(hd, Vector3.up));
                    rb.isKinematic = false;
                    rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                    float t1 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t1 < 4f) yield return null;   // the chase camera settles behind it
                    Shot(name, null, Vector3.zero);
                }
                else
                {
                    float eyeH = f.Length > 6 ? float.Parse(f[6], ci) : 2f;
                    float ey = GroundY(a); if (float.IsNaN(ey)) ey = gy;
                    var eye = new Vector3(a.x, ey + eyeH, a.y);
                    var tgt = new Vector3(b.x, gy, b.y);
                    // the car out of the way, well behind the eye
                    car.TeleportTo(new Vector3(a.x, 300f, a.y), Quaternion.identity);
                    rb.isKinematic = true;
                    for (int i = 0; i < 5; i++) yield return null;
                    Shot(name, eye, tgt);
                }
            }
            FivePointsShots.log.AppendLine("FP SHOTS DONE");
            FivePointsShots.Finish(0);
        }

        /// <summary>The game camera (or, with an eye, the camera put there for
        /// this one frame) through the game's dither, to Screenshots\fp_name.png.</summary>
        static void Shot(string name, Vector3? eye, Vector3 target)
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "fp_" + name + ".png");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { FivePointsShots.log.AppendLine($"  {name}: no camera/graphics"); return; }
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
            if (eye.HasValue)
            {
                var d = target - eye.Value;
                cam.transform.SetPositionAndRotation(eye.Value, Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.99f ? Vector3.forward : Vector3.up));
            }
            var targetTex = cam.targetTexture;
            int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                var shown = PSXScreenshotTool.Dithered(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev == rt || prev == shown ? null : prev;
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                ShotSidecar.WritePng(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            cam.targetTexture = targetTex;
            rt.Release(); Object.DestroyImmediate(rt);
            if (eye.HasValue) cam.transform.SetPositionAndRotation(p0, r0);
            FivePointsShots.log.AppendLine($"  {name}: " + (System.IO.File.Exists(path) ? path : "NOT WRITTEN") + $" (camera at {cam.transform.position})");
        }
    }
}
