using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// "Map does not always appear while racing/free roaming" (owner,
    /// 2026-10-29): both of his captures showed the arrow, the dots and the
    /// distance under the arrow - and no streets, no route. A PLAY-MODE check,
    /// because the map only exists once RaceHUD.Update has built it, and
    /// because what hid it is how the frame is drawn, not what the map holds:
    /// the edit-mode shots (MinimapShots) render the HUD through a camera of
    /// their own and always showed the roads.
    ///
    /// At each stop it renders the game camera AS THE GAME DOES (the world
    /// camera plus the HUD camera stacked on it when SpeedBlur has split the
    /// HUD off) twice - once with the map's roads canvas on, once off - and
    /// counts the pixels that differ inside the map: ROAD PIXELS. Zero means
    /// the player sees the arrow and no map.
    ///
    /// Stops: free roam at the spawn and on West Trade Street (the owner's
    /// video), the map rebuilt (what a new HUD height or a touch change
    /// does), the cockpit, after a pause, every PICTURE setting; the 277 race
    /// (UptownLoop, the owner's screenshot) in chase and cockpit, rebuilt in
    /// each; a mountain stage (BlueRidge) in chase and cockpit, rebuilt.
    /// The first run, before the fix, found the cause: every stop after a
    /// rebuild had 0 road pixels, the roads canvas left on Default under a
    /// HUD camera that draws the UI layer only (HudMinimap.KeepLayer).
    /// Report: &lt;sandbox&gt;\PSXRacing_minimap_play_check.txt, crops of
    /// the map: &lt;sandbox&gt;\Screenshots\minimap_play_*.png.
    ///
    ///   powershell -ExecutionPolicy Bypass -File tools\minimap-play-check.ps1
    /// </summary>
    public static class MinimapPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static int roamIdx, raceIdx, stageIdx;
        internal const string ReportName = "PSXRacing_minimap_play_check.txt";
        internal const string RoamScene = "Assets/PSXRacing/Scenes/Charlotte.unity";
        internal const string RaceScene = "Assets/PSXRacing/Scenes/UptownLoop.unity";
        internal const string StageScene = "Assets/PSXRacing/Scenes/BlueRidge.unity";

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            roamIdx = TrackCatalog.IndexOf("Charlotte");
            raceIdx = TrackCatalog.IndexOf("UptownLoop");
            stageIdx = TrackCatalog.IndexOf("BlueRidge");
            log.AppendLine("edition " + Edition.Name(Edition.Current) + "; speed blur " + SpeedBlurPrefs.Mode +
                           ", lens fx " + LensFxPrefs.Enabled);
            if (roamIdx < 0 || !File.Exists(RoamScene)) { Fail("Charlotte scene missing"); Finish(); EditorApplication.Exit(1); return; }
            EditorSceneManager.OpenScene(RoamScene);
            PrimeRoam();
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("MinimapPlayCheckRunner").AddComponent<MinimapPlayCheckRunner>();
        }

        static void Prime(int idx, bool roam)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = roam;
            RaceHandoff.TrackIndex = idx;
            var cars = CarCatalog.All;
            if (cars.Count > 4)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                if (!roam)
                {
                    RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id + ";" + cars[3].id;
                    RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
                }
            }
            RaceHandoff.StartFuelPct = 100f;
        }

        internal static void PrimeRoam() => Prime(roamIdx, true);
        internal static void PrimeRace() => Prime(raceIdx, false);
        internal static void PrimeStage() => Prime(stageIdx, false);

        internal static void Line(string s) => log.AppendLine(s);
        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + what + (got != null ? " (" + got + ")" : ""));
        }
        internal static void Fail(string what) { failures++; log.AppendLine("  FAIL " + what); }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "RESULT: PASS" : "RESULT: " + failures + " FAIL");
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), ReportName);
            File.WriteAllText(path, log.ToString());
            Debug.Log("[MinimapPlayCheck]\n" + log);
        }
    }

    public class MinimapPlayCheckRunner : MonoBehaviour
    {
        bool finished;
        float startedAt;
        int exceptions;
        readonly StringBuilder errs = new StringBuilder();

        void Awake()
        {
            startedAt = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy() => Application.logMessageReceived -= OnLog;

        void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (msg.StartsWith("[MinimapPlayCheck]")) return;
            if (++exceptions <= 8) errs.AppendLine("    " + type + ": " + msg.Split('\n')[0]);
        }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - startedAt < 900f) return;
            finished = true;
            MinimapPlayCheck.Fail("the check hung (15 min)");
            MinimapPlayCheck.Finish();
            EditorApplication.Exit(1);
        }

        static IEnumerator Wait(float s)
        {
            float t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < s) yield return null;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return Wait(5f);

            // ---------------- free roam ----------------
            MinimapPlayCheck.Line("FREE ROAM (Charlotte):");
            yield return Probe("roam_spawn");
            var mode = CityMode.Instance;
            if (mode != null && mode.player != null && mode.world != null && mode.world.Map != null)
            {
                var map = mode.world.Map;
                int best = -1; float bl = 0f;
                for (int i = 0; i < map.edges.Length; i++)
                {
                    var e = map.edges[i];
                    if (e.name == "West Trade Street" && e.length > bl) { bl = e.length; best = i; }
                }
                if (best >= 0)
                {
                    var e = map.edges[best];
                    float s = e.length * 0.5f;
                    Vector2 p = e.PointAt(s), t = e.TangentAt(s);
                    mode.player.TeleportTo(new Vector3(p.x, e.YAt(s) + 0.45f, p.y),
                                           Quaternion.LookRotation(new Vector3(t.x, 0f, t.y), Vector3.up));
                    mode.world.EnsureRing(new Vector3(p.x, e.YAt(s), p.y), 1);
                    yield return Wait(3f);
                    yield return Probe("roam_trade");
                }
                else MinimapPlayCheck.Fail("no West Trade Street in the graph");
            }
            else MinimapPlayCheck.Fail("no free-roam session");

            yield return Rebuild("roam_rebuilt");
            ChaseCamera.SetView(ChaseCamera.View.Cockpit);
            yield return Wait(1.5f);
            yield return Probe("roam_cockpit");
            ChaseCamera.SetView(ChaseCamera.View.Chase);
            Time.timeScale = 0f;
            yield return Wait(1f);
            Time.timeScale = 1f;
            yield return Wait(0.5f);
            yield return Probe("roam_unpaused");
            yield return LayerExperiment("roam");

            // PICTURE, every setting: the player's own way to a new
            // framebuffer, a new HUD height and so a rebuilt map.
            var keepPix = PSXQuality.Current;
            for (int i = 0; i < 4; i++)
            {
                var pix = (PSXPixels)i;
                var hudNow = Object.FindFirstObjectByType<RaceHUD>();
                var before = hudNow != null ? hudNow.Minimap : null;
                PSXQuality.Current = pix;
                yield return Wait(1.5f);
                MinimapPlayCheck.Line("  PICTURE " + pix + ": the map " +
                    (hudNow != null && hudNow.Minimap != before ? "was rebuilt" : "stayed"));
                yield return Probe("roam_picture_" + pix.ToString().ToLowerInvariant());
            }
            PSXQuality.Current = keepPix;
            yield return Wait(1f);

            // ---------------- the 277 race ----------------
            if (MinimapPlayCheck.raceIdx >= 0 && File.Exists(MinimapPlayCheck.RaceScene))
            {
                MinimapPlayCheck.PrimeRace();
                EditorSceneManager.LoadSceneInPlayMode(MinimapPlayCheck.RaceScene, new LoadSceneParameters(LoadSceneMode.Single));
                yield return Wait(8f);
                MinimapPlayCheck.Line("RACE (UptownLoop, the 277 loop):");
                yield return Probe("race_chase");
                yield return Rebuild("race_chase_rebuilt");
                ChaseCamera.SetView(ChaseCamera.View.Cockpit);
                yield return Wait(1.5f);
                yield return Probe("race_cockpit");
                yield return Rebuild("race_cockpit_rebuilt");
                ChaseCamera.SetView(ChaseCamera.View.Chase);
                yield return Wait(0.5f);
            }
            else MinimapPlayCheck.Line("RACE: UptownLoop not available here - skipped");

            // ---------------- a mountain stage ----------------
            if (MinimapPlayCheck.stageIdx >= 0 && File.Exists(MinimapPlayCheck.StageScene))
            {
                MinimapPlayCheck.PrimeStage();
                EditorSceneManager.LoadSceneInPlayMode(MinimapPlayCheck.StageScene, new LoadSceneParameters(LoadSceneMode.Single));
                yield return Wait(8f);
                MinimapPlayCheck.Line("STAGE (BlueRidge):");
                yield return Probe("stage_chase");
                ChaseCamera.SetView(ChaseCamera.View.Cockpit);
                yield return Wait(1.5f);
                yield return Probe("stage_cockpit");
                yield return Rebuild("stage_cockpit_rebuilt");
                ChaseCamera.SetView(ChaseCamera.View.Chase);
            }
            else MinimapPlayCheck.Line("STAGE: BlueRidge not available here - skipped");

            MinimapPlayCheck.Line("errors/exceptions logged during play: " + exceptions);
            if (errs.Length > 0) MinimapPlayCheck.Line(errs.ToString().TrimEnd());
            if (finished) yield break;
            finished = true;
            MinimapPlayCheck.Finish();
            EditorApplication.Exit(MinimapPlayCheck.failures == 0 ? 0 : 1);
        }

        static readonly FieldInfo MinimapH = typeof(RaceHUD).GetField("minimapH", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo Built = typeof(HudMinimap).GetField("built", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo Layered = typeof(SpeedBlur).GetField("layeredCount", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>What a PICTURE change, a window that changes the HUD's
        /// height or a touch toggle does: RaceHUD destroys the map and builds
        /// a new one in the same Update.</summary>
        IEnumerator Rebuild(string tag)
        {
            var hud = Object.FindFirstObjectByType<RaceHUD>();
            if (hud == null || MinimapH == null) { MinimapPlayCheck.Fail(tag + ": no RaceHUD to rebuild"); yield break; }
            var before = hud.Minimap;
            MinimapH.SetValue(hud, -1);
            yield return null;
            yield return null;
            var fresh = hud.Minimap;
            var freshCanvas = fresh != null ? fresh.GetComponent<Canvas>() : null;
            if (fresh != null && fresh != before && freshCanvas != null && freshCanvas.rootCanvas != null)
                MinimapPlayCheck.Check(fresh.gameObject.layer == freshCanvas.rootCanvas.gameObject.layer,
                    tag + ": the new map's roads are on the HUD canvas's layer from their first frame",
                    "roads " + fresh.gameObject.layer + ", HUD canvas " + freshCanvas.rootCanvas.gameObject.layer);
            yield return Wait(1f);
            MinimapPlayCheck.Check(hud.Minimap != null && hud.Minimap != before, tag + ": the map was rebuilt");
            yield return Probe(tag);
        }

        /// <summary>The nested roads canvas moved off the drawing camera's
        /// layers and back: does that alone hide it?</summary>
        IEnumerator LayerExperiment(string tag)
        {
            yield return null;
            var hud = Object.FindFirstObjectByType<RaceHUD>();
            var mm = hud != null ? hud.Minimap : null;
            if (mm == null) yield break;
            int keep = mm.gameObject.layer;
            mm.gameObject.layer = 0;
            int at0 = RoadPixels(mm, null, out _);
            mm.gameObject.layer = SpeedBlur.HudLayer;
            int at5 = RoadPixels(mm, null, out _);
            mm.gameObject.layer = keep;
            MinimapPlayCheck.Line("  [" + tag + " layer experiment] roads canvas on Default: " + at0 +
                                  " road px; on UI (the HUD camera's): " + at5 + " road px");
        }

        IEnumerator Probe(string tag)
        {
            yield return null;
            var hud = Object.FindFirstObjectByType<RaceHUD>();
            var mm = hud != null ? hud.Minimap : null;
            var blur = Object.FindFirstObjectByType<SpeedBlur>();
            if (mm == null || mm.Root == null)
            {
                MinimapPlayCheck.Fail(tag + ": no map on the HUD");
                yield break;
            }
            var canvas = mm.GetComponent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas : null;
            var drawCam = root != null ? root.worldCamera : null;
            var sb = new StringBuilder("  [" + tag + "] view " + ChaseCamera.Current);
            sb.Append(", split " + (blur != null && blur.IsSplit));
            if (blur != null && blur.hudCanvas != null && Layered != null)
                sb.Append(" (hierarchy " + blur.hudCanvas.transform.hierarchyCount + ", relayered at " + Layered.GetValue(blur) + ")");
            sb.Append("; HUD canvas '" + (root != null ? root.name : "-") + "' layer " + (root != null ? root.gameObject.layer : -1));
            sb.Append(" on " + (drawCam != null ? drawCam.name + " mask 0x" + drawCam.cullingMask.ToString("X") : "no camera"));
            sb.Append("; roads layer " + mm.gameObject.layer + (canvas != null && canvas.isActiveAndEnabled ? "" : " CANVAS OFF"));
            sb.Append(" built " + (Built != null ? Built.GetValue(mm) : "?") + " verts " + mm.LastVerts + " cull " + mm.canvasRenderer.cull);
            sb.Append("; marks layer " + mm.Marks.gameObject.layer + " verts " + mm.Marks.LastVerts);
            sb.Append("; distance '" + (mm.DistanceText != null ? mm.DistanceText.text : "") + "'");
            int px = RoadPixels(mm, tag, out string png);
            sb.Append("; ROAD PIXELS " + px);
            if (drawCam != null && (drawCam.cullingMask & (1 << mm.gameObject.layer)) == 0)
                sb.Append(" - the roads canvas's layer is NOT in its camera's mask");
            MinimapPlayCheck.Line(sb.ToString());
            if (png != null) MinimapPlayCheck.Line("    " + png);
            MinimapPlayCheck.Check(px >= 40, tag + ": the map's streets/route are on the screen", px + " px");
        }

        /// <summary>Pixels inside the map that the roads canvas changes: the
        /// frame drawn with it minus the frame drawn without it.</summary>
        static int RoadPixels(HudMinimap mm, string tag, out string png)
        {
            png = null;
            var canvas = mm.GetComponent<Canvas>();
            var cam = Camera.main;
            if (canvas == null || cam == null) return -1;
            var blur = Object.FindFirstObjectByType<SpeedBlur>();
            var hudCam = blur != null ? blur.HudCamera : null;
            var drawCam = canvas.rootCanvas.worldCamera != null ? canvas.rootCanvas.worldCamera : cam;
            int w = cam.targetTexture != null ? cam.targetTexture.width : cam.pixelWidth;
            int h = cam.targetTexture != null ? cam.targetTexture.height : cam.pixelHeight;
            var rootRT = (RectTransform)mm.Root.transform;
            var corners = new Vector3[4];
            rootRT.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(drawCam, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(drawCam, corners[2]);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x)), 0, w - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x)), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y)), 0, h - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y)), 0, h - 1);
            if (x1 <= x0 || y1 <= y0) return -2;

            var with = Frame(cam, hudCam, w, h);
            bool was = canvas.enabled;
            canvas.enabled = false;
            var without = Frame(cam, hudCam, w, h);
            canvas.enabled = was;
            Canvas.ForceUpdateCanvases();
            if (with == null || without == null) return -3;
            int n = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = with[y * w + x]; var q = without[y * w + x];
                    if (Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b) > 24) n++;
                }
            if (tag != null)
            {
                const int K = 3;
                int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
                var tex = new Texture2D(cw * K, ch * K, TextureFormat.RGB24, false);
                var outPx = new Color32[cw * K * ch * K];
                for (int y = 0; y < ch * K; y++)
                    for (int x = 0; x < cw * K; x++)
                        outPx[y * cw * K + x] = with[(y0 + y / K) * w + x0 + x / K];
                tex.SetPixels32(outPx);
                tex.Apply();
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
                Directory.CreateDirectory(dir);
                png = Path.Combine(dir, "minimap_play_" + tag + ".png");
                File.WriteAllBytes(png, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            return n;
        }

        /// <summary>The game camera's frame, with the HUD camera stacked on it
        /// (SpeedBlurPreview.Render's way: a render request lists one camera,
        /// so the stacked HUD camera's canvas geometry has to be asked for).</summary>
        static Color32[] Frame(Camera cam, Camera hudCam, int w, int h)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var keep = cam.targetTexture;
            var keepHud = hudCam != null ? hudCam.targetTexture : null;
            cam.targetTexture = rt;
            if (hudCam != null) hudCam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            System.Action<ScriptableRenderContext, Camera> emitHud = (ctx, c) =>
            {
                if (hudCam != null && c == hudCam) ScriptableRenderContext.EmitGeometryForCamera(c);
            };
            RenderPipelineManager.beginCameraRendering += emitHud;
            Color32[] px = null;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                px = SpeedBlurPreview.Read(rt);
            }
            RenderPipelineManager.beginCameraRendering -= emitHud;
            cam.targetTexture = keep;
            if (hudCam != null) hudCam.targetTexture = keepHud;
            rt.Release();
            Object.DestroyImmediate(rt);
            return px;
        }
    }
}
