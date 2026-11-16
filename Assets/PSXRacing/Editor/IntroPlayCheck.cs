using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// NEW GAME's opening, played (NewGameIntro, owner 2026-11-16): a scratch career, the real wizard over
    /// black (name, the license question, the car pick), DRIVE THIS ONE, then the tunnel - the hold, the
    /// headlights, the pass, the attach, the exit, the drive (the intro's own autopilot holds the wheel:
    /// NewGameIntro.AutoDrive), CHARLOTTE - and the home screen. Frames of every phase (the PSX camera's
    /// framebuffer with the intro's overlay laid over it as the screen draws it) to Screenshots/intro/, a
    /// 10 fps video sequence to Screenshots/intro/video/, and the end state against the state DRIVE THIS ONE
    /// saved: the same career, untouched. PSX_INTRO=0 runs the same wizard straight home (the before).
    /// Writes Screenshots/intro/intro_check.txt.
    /// </summary>
    public static class IntroPlayCheck
    {
        internal static StringBuilder log = new StringBuilder();
        internal static int failures;
        internal static string OutDir, VideoDir;
        internal const string ViewPref = "psx.cameraView";   // ChaseCamera.PrefKey
        internal static int prevView = -1;

        [MenuItem("PSX Racing/New Game Intro (play mode)")]
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            string root = Directory.GetParent(Application.dataPath).FullName;
            OutDir = Path.Combine(root, "Screenshots", "intro");
            VideoDir = Path.Combine(OutDir, "video");
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(VideoDir);
            // the intro stands down for batch tools; this one asks for it (PSX_INTRO=0 still turns it off)
            NewGameIntro.ForTools = Environment.GetEnvironmentVariable("PSX_INTRO") != "0";
            foreach (var f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);
            foreach (var f in Directory.GetFiles(VideoDir, "*.jpg")) File.Delete(f);
            Line("edition " + Edition.Name(Edition.Current) + ", intro " + (NewGameIntro.On ? "ON" : "OFF (PSX_INTRO=0)") +
                 ", colour space " + QualitySettings.activeColorSpace);
            // a career of its own: the sandbox's own save is never read or written
            LifeSimManager.UseScratchSave("INTROCHECK");
            TitleScreen.Skip = true;
            NewGameIntro.AutoDrive = true;
            // PSX_INTRO_CHECK_VIEW=Chase|Cockpit|...: the player's saved view for this run (this editor's prefs,
            // put back at the end)
            prevView = PlayerPrefs.GetInt(ViewPref, -1);
            string want = Environment.GetEnvironmentVariable("PSX_INTRO_CHECK_VIEW");
            if (!string.IsNullOrEmpty(want) && Enum.TryParse(want, true, out ChaseCamera.View v))
            {
                PlayerPrefs.SetInt(ViewPref, (int)v);
                PlayerPrefs.Save();
            }
            Line("the player's view: " + (ChaseCamera.View)PlayerPrefs.GetInt(ViewPref, 0));
            EditorSceneManager.OpenScene(EditorBuildSettings.scenes[0].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("IntroPlayRunner").AddComponent<IntroPlayRunner>();
        }

        internal static void Line(string s) { log.AppendLine(s); Debug.Log("[IntroPlayCheck] " + s); }
        internal static void Check(bool ok, string what, object got = null)
        {
            string d = got != null ? "  (" + got + ")" : "";
            if (ok) Line("PASS " + what + d); else { failures++; Line("FAIL " + what + d); }
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
            File.WriteAllText(Path.Combine(OutDir, "intro_check.txt"), log.ToString());
        }
    }

    public class IntroPlayRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => IntroPlayCheck.Check(ok, what, got);
        static void Note(string s) => IntroPlayCheck.Line(s);
        const int ShotLayer = 31;
        const int ShotW = 1280, ShotH = 720, VidW = 640, VidH = 360;

        float bornAt;
        bool finished;
        int videoFrame;
        float nextVideo = -1f;
        bool recording;

        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < 900f) return;
            Check(false, "HUNG: 900 s");
            Done();
        }

        void Done()
        {
            if (finished) return;
            finished = true;
            NewGameIntro.AutoDrive = false;
            NewGameIntro.ForTools = false;
            TitleScreen.Skip = false;
            if (IntroPlayCheck.prevView >= 0) { PlayerPrefs.SetInt(IntroPlayCheck.ViewPref, IntroPlayCheck.prevView); PlayerPrefs.Save(); }
            IntroPlayCheck.Finish();
            LifeSimManager.UseCareerSave();
            EditorApplication.Exit(IntroPlayCheck.failures == 0 ? 0 : 1);
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static Button Named(Transform under, string label)
        {
            if (under == null) return null;
            foreach (var b in under.GetComponentsInChildren<Button>())
            {
                var t = b.GetComponentInChildren<Text>();
                if (t != null && t.text == label) return b;
            }
            return null;
        }

        // ---- the end state ----
        struct Snap { public int money, day, slot, cars, log; public string car, active; }

        static Snap Take()
        {
            var s = LifeSimManager.State;
            var c = s.ActiveCar;
            return new Snap
            {
                money = s.money, day = s.day, slot = s.slotIndex, cars = s.cars.Count, log = s.calendarLog.Count,
                active = s.activeCar, car = c != null ? JsonUtility.ToJson(c) : "",
            };
        }

        static string Say(Snap a) => $"${a.money}, day {a.day} slot {a.slot}, {a.cars} car(s), active {a.active}, diary {a.log}";

        IEnumerator Start()
        {
            yield return Frames(30);
            var screen = FindAnyObjectByType<LifeHomeScreen>();
            Check(screen != null, "the home screen is up");
            if (screen == null) { Done(); yield break; }
            bool on = NewGameIntro.On;

            // ---- the wizard ----
            var wiz = GameObject.Find("Wizard");
            Check(wiz != null, "no save: the wizard is up");
            if (wiz == null) { Done(); yield break; }
            var bd = GameObject.Find("Backdrop");
            var bdImg = bd != null ? bd.GetComponent<Image>() : null;
            Check(bdImg != null && (on ? bdImg.color == Color.black : bdImg.color != Color.black),
                  on ? "the wizard stands on black" : "the wizard stands on the charcoal (as before)", bdImg != null ? bdImg.color.ToString() : "none");
            Check(NewGameIntro.Carrier.Exists == on, on ? "the tunnel's sound plays" : "no tunnel sound", NewGameIntro.Carrier.Exists);
            yield return Frames(5);
            ShotMenus("01_wizard_black");

            var field = wiz.GetComponentInChildren<InputField>();
            if (field != null) field.text = "INTRO";
            var next = Named(wiz.transform, "NEXT: PICK A CAR");
            Check(next != null, "NEXT is there");
            if (next == null) { Done(); yield break; }
            next.onClick.Invoke();
            yield return Frames(3);
            var ask = wiz.transform.Find("License");
            var yes = Named(ask, "YES");
            Check(yes != null, "the license question");
            if (yes == null) { Done(); yield break; }
            yes.onClick.Invoke();
            yield return Frames(3);
            var skip = Named(ask, "YES, SKIP IT");
            Check(skip != null, "YES, SKIP IT");
            if (skip == null) { Done(); yield break; }
            skip.onClick.Invoke();
            yield return Frames(30);

            var pick = GameObject.Find("CarPick");
            Check(pick != null, "WHAT ARE YOU DRIVING? is up");
            if (pick == null) { Done(); yield break; }
            ShotMenus("02_carpick_black");
            var drive = Named(pick.transform, "DRIVE THIS ONE");
            Check(drive != null, "DRIVE THIS ONE");
            if (drive == null) { Done(); yield break; }
            drive.onClick.Invoke();
            var a = Take();
            Note("DRIVE THIS ONE saved: " + Say(a) + ", car " + LifeSimManager.State.ActiveCar?.specId);

            if (on)
            {
                yield return Frames(1);
                ShotMenus("02b_black_after_pick");
                yield return Tunnel();
            }

            // ---- home ----
            float t0 = Time.realtimeSinceStartup;
            LifeHomeScreen home = null;
            while (Time.realtimeSinceStartup - t0 < 90f)
            {
                home = FindAnyObjectByType<LifeHomeScreen>();
                if (home != null && SceneManager.GetActiveScene().buildIndex == 0 && GameObject.Find("Header") != null) break;
                yield return null;
            }
            Check(home != null && GameObject.Find("Header") != null, "the career's home screen is up",
                  SceneManager.GetActiveScene().name);
            yield return Frames(20);
            var b = Take();
            Note("home: " + Say(b));
            Check(a.money == b.money && a.day == b.day && a.slot == b.slot && a.cars == b.cars && a.active == b.active,
                  "the home screen's career is the one DRIVE THIS ONE saved (money, day, block, cars)", Say(a) + " -> " + Say(b));
            Check(a.car == b.car, "the car is untouched (km, fuel, wear, faults, paint)",
                  a.car == b.car ? "identical" : a.car + "\n  -> " + b.car);
            Check(a.log == b.log, "nothing written in the diary", a.log + " -> " + b.log);
            Check(!RaceHandoff.FromLifeSim && !RaceHandoff.TestDrive && !RaceHandoff.ResultReady, "the handoff is clear");
            Check(GameObject.Find("Wizard") == null && GameObject.Find("CarPick") == null, "not back in the wizard");
            Check(!NewGameIntro.Carrier.Exists, "the intro's overlay and sound are gone");
            Check(!NewGameIntro.EarOutside && ExposureAdapt.Scripted < 0f && !ExposureAdapt.ForceTunnel, "the intro's hooks are released");
            float tw = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tw < 2.5f) yield return null;
            Check(AudioListener.volume > 0.5f * AudioPrefs.Master, "the sound is back up", AudioListener.volume.ToString("0.00"));
            ShotMenus("14_home");
            Done();
        }

        // ---- the tunnel ----
        IEnumerator Tunnel()
        {
            float t0 = Time.realtimeSinceStartup;
            NewGameIntro intro = null;
            while (Time.realtimeSinceStartup - t0 < 120f)
            {
                intro = NewGameIntro.Instance;
                if (intro != null) break;
                yield return null;
            }
            Check(intro != null, "the stage loads and the intro takes it", SceneManager.GetActiveScene().name);
            if (intro == null) yield break;
            Check(intro.Now == NewGameIntro.Phase.Approach && intro.BlackAlpha > 0.99f, "black, the car behind the lens",
                  $"phase {intro.Now}, black {intro.BlackAlpha:0.00}, along {intro.Along:0} m");
            Check(RaceHandoff.TestDrive && RaceHandoff.Solo && RaceHandoff.TrafficLevel == TrafficLevels.None,
                  "a drive that banks nothing, no rivals, no traffic");
            var rm = RaceManager.Instance;
            int others = 0;
            if (rm != null) foreach (var c in rm.allCars) if (c != null && c != intro.Car && c.gameObject.activeInHierarchy) others++;
            Check(others == 0, "no other car on the road", others);
            Note($"bore {intro.BoreIn}..{intro.BoreOut}, hour {RaceHandoff.TimeOfDayIndex}, day {RaceHandoff.CalendarDay} " +
                 $"({LifeRules.DateLabel(RaceHandoff.CalendarDay)}), season {Seasons.Current}, sky {RaceHandoff.SkyOverride}");

            // PSX_INTRO_CHECK=skip: a virtual pad holds START once the ease has begun (the real input path)
            bool skipRun = Environment.GetEnvironmentVariable("PSX_INTRO_CHECK") == "skip";
            Gamepad pad = null;
            float pressedAt = -1f;
            string endReason = null;
            float start = Time.time;
            recording = !skipRun;
            nextVideo = Time.time;
            bool hold = false, lamps = false, pass = false, pass2 = false, attach = false, inside = false, reveal = false,
                 caption = false, drv = false, fade = false, card = false;
            float maxAlongGap = 0f, lastAlong = intro.Along;
            float minSpeed = float.MaxValue, botMin = float.MaxValue, driveSeenAt = -1f;
            bool earOut = false, earBack = false;
            float eyeInBore = 0f, eyeOut = 9f;

            while (intro != null && NewGameIntro.Instance == intro)
            {
                float t = Time.time - start;
                var car = intro.Car;
                float kmh = car != null ? car.speedKmh : 0f;
                // the scripted part: the approach, the ease, and three seconds of the hand-over (the drive after
                // that is the tools' own driver, which brakes for the bends)
                if (intro.Now == NewGameIntro.Phase.Drive && driveSeenAt < 0f) driveSeenAt = t;
                bool scripted = intro.Now == NewGameIntro.Phase.Approach || intro.Now == NewGameIntro.Phase.Attach ||
                                (intro.Now == NewGameIntro.Phase.Drive && t - driveSeenAt < 3f);
                if (scripted && t > 2f && kmh < minSpeed) minSpeed = kmh;
                if (intro.Now == NewGameIntro.Phase.Drive && !scripted && kmh < botMin) botMin = kmh;
                maxAlongGap = Mathf.Max(maxAlongGap, Mathf.Abs(intro.Along - lastAlong));
                lastAlong = intro.Along;
                if (intro.Now == NewGameIntro.Phase.Approach && NewGameIntro.EarOutside) earOut = true;
                if (intro.Now == NewGameIntro.Phase.Drive && !NewGameIntro.EarOutside) earBack = true;
                endReason = intro.EndReason;
                if (skipRun && attach && pad == null && pressedAt < 0f)
                {
                    try { pad = InputSystem.AddDevice<Gamepad>(); }
                    catch (Exception e) { Note("no virtual pad: " + e.Message); }
                    pressedAt = Time.realtimeSinceStartup;
                    if (pad != null) InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Start));
                    Note($"  START held at {t:0.0} s (along {intro.Along:0} m)");
                }
                if (skipRun && pad != null && intro.Now != NewGameIntro.Phase.Outro && Time.realtimeSinceStartup - pressedAt > 0.5f &&
                    Time.realtimeSinceStartup - pressedAt < 0.6f)
                    yield return Shot("s1_skip_held", intro);

                if (!hold && t >= 0.6f) { hold = true; yield return Shot("03_black_hold", intro); }
                if (!lamps && intro.Along >= -26f) { lamps = true; yield return Shot("04_headlights", intro); }
                if (!pass && intro.Along >= -0.3f) { pass = true; yield return Shot("05_pass_through", intro); }
                if (!pass2 && intro.Along >= 3.2f) { pass2 = true; yield return Shot("05b_pass_by", intro); }
                if (!attach && intro.Now == NewGameIntro.Phase.Attach && intro.Blend >= 0.5f) { attach = true; yield return Shot("06_attach", intro); }
                if (!inside && intro.Now == NewGameIntro.Phase.Drive && intro.CarIndex >= intro.BoreOut - 9)
                {
                    inside = true;
                    eyeInBore = ExposureAdapt.Current;
                    yield return Shot("07_inside_exit", intro);
                }
                if (!reveal && intro.Exited && Time.time - intro.ExitedAt >= 0.35f) { reveal = true; yield return Shot("08_reveal", intro); }
                if (!caption && intro.Exited && Time.time - intro.ExitedAt >= NewGameIntro.CaptionAfterExitS + 1.6f)
                {
                    caption = true;
                    eyeOut = ExposureAdapt.Current;
                    yield return Shot("09_caption", intro);
                }
                if (!drv && intro.PastExitM >= 900f) { drv = true; yield return Shot("10_drive", intro); }
                if (!fade && intro.Now == NewGameIntro.Phase.Outro && intro.BlackAlpha >= 0.5f) { fade = true; recording = true; yield return Shot("11_fade", intro); }
                if (!card && intro.CardUp) { card = true; yield return Shot("12_charlotte", intro); }

                // the video: the tunnel and the first 400 m out of it, then the fade and the card
                if (intro.PastExitM > 450f && intro.Now != NewGameIntro.Phase.Outro) recording = false;
                if (recording && Time.time >= nextVideo)
                {
                    nextVideo += 0.1f;
                    VideoFrame(intro);
                }
                yield return null;
            }
            Note($"intro ran {Time.time - start:0.0} s; ended: {endReason}");
            if (skipRun)
            {
                if (pad != null) { InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.RemoveDevice(pad); }
                Check(endReason == "skipped" && card, "holding START for a second skips to CHARLOTTE",
                      $"{endReason}, {Time.realtimeSinceStartup - pressedAt:0.0} s real from the press to home");
                yield break;
            }
            Check(hold && lamps && pass && attach && inside && reveal && drv && card, "every phase came",
                  $"hold {hold} lamps {lamps} pass {pass} attach {attach} inside {inside} reveal {reveal} caption {caption} drive {drv} fade {fade} card {card}");
            Check(earOut && earBack, "the car is heard from outside on its way up, from its chase rig after", $"outside {earOut}, back {earBack}");
            Check(minSpeed > 40f, "the car never stops dead: the approach, the pass, the ease and the hand-over", minSpeed.ToString("0") + " km/h at the slowest");
            Note($"  (the tools' driver after that: slowest {botMin:0} km/h, for a bend)");
            Check(eyeInBore > 1.3f && eyeOut < eyeInBore, "the eye opens in the bore and closes on the day",
                  $"in the bore {eyeInBore:0.00}, out {eyeOut:0.00}");
            Note("video frames: " + videoFrame);
        }

        // ---- pictures ----

        IEnumerator Shot(string name, NewGameIntro intro)
        {
            yield return null;
            var tex = Grab(intro, ShotW, ShotH);
            if (tex == null) { Note("  shot " + name + " not taken"); yield break; }
            File.WriteAllBytes(Path.Combine(IntroPlayCheck.OutDir, name + ".png"), tex.EncodeToPNG());
            DestroyImmediate(tex);
            var car = intro.Car;
            Note($"  shot {name}: phase {intro.Now}, along {intro.Along:0.0} m, blend {intro.Blend:0.00}, black {intro.BlackAlpha:0.00}, " +
                 $"{(car != null ? car.speedKmh : 0f):0} km/h, eye {ExposureAdapt.Current:0.00}, past exit {intro.PastExitM:0} m");
        }

        void VideoFrame(NewGameIntro intro)
        {
            var tex = Grab(intro, VidW, VidH);
            if (tex == null) return;
            File.WriteAllBytes(Path.Combine(IntroPlayCheck.VideoDir, $"f{videoFrame:0000}.jpg"), tex.EncodeToJPG(88));
            DestroyImmediate(tex);
            videoFrame++;
        }

        /// <summary>The picture as the screen has it: the PSX camera's framebuffer (the world and the HUD),
        /// and the intro's overlay canvas laid over it - rendered over black and over white, so its alpha is
        /// exact - in linear light, as the display composites it.</summary>
        static Texture2D Grab(NewGameIntro intro, int w, int h)
        {
            var lens = intro != null ? intro.Lens : Camera.main;
            if (lens == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            var fb = lens.targetTexture;
            var under = new Texture2D(w, h, TextureFormat.RGB24, false);
            var small = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            if (fb != null) Graphics.Blit(fb, small);
            else
            {
                // no framebuffer of its own: the world rendered on request (no HUD)
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = small };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(lens, request))
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(lens, request);
            }
            var prev = RenderTexture.active;
            RenderTexture.active = small;
            under.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            under.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(small);

            if (!NewGameIntro.Carrier.Exists) return under;
            var canvas = NewGameIntro.Carrier.Get().Canvas;
            if (canvas == null) return under;
            var pb = RenderCanvas(new[] { canvas }, w, h, Color.black);
            var pw = RenderCanvas(new[] { canvas }, w, h, Color.white);
            var u = under.GetPixels();
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < u.Length; i++)
            {
                Color cb = pb[i], cw = pw[i];
                float al = 1f - Mathf.Clamp01(((cw.r - cb.r) + (cw.g - cb.g) + (cw.b - cb.b)) / 3f);
                Color basec = lin ? u[i].linear : u[i];
                Color o = new Color(cb.r + basec.r * (1f - al), cb.g + basec.g * (1f - al), cb.b + basec.b * (1f - al), 1f);
                u[i] = lin ? o.gamma : o;
            }
            under.SetPixels(u);
            under.Apply();
            return under;
        }

        /// <summary>Overlay canvases rendered by a camera of their own, on a solid background, into a linear
        /// target (the values come back as the blend left them).</summary>
        static Color[] RenderCanvas(Canvas[] canvases, int w, int h, Color bg)
        {
            var go = new GameObject("IntroShotCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bg;
            cam.cullingMask = 1 << ShotLayer;
            cam.transform.position = new Vector3(0f, -5000f, 0f);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            cam.targetTexture = rt;
            var undo = new List<Action>();
            foreach (var canvas in canvases)
            {
                var c = canvas;
                var mode0 = c.renderMode; var cam0 = c.worldCamera; float pd0 = c.planeDistance;
                var layers = new List<(GameObject, int)>();
                foreach (var t in c.GetComponentsInChildren<Transform>(true)) { layers.Add((t.gameObject, t.gameObject.layer)); t.gameObject.layer = ShotLayer; }
                var cs = c.GetComponent<CanvasScaler>();
                var sm = cs != null ? cs.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize;
                float sf = cs != null ? cs.scaleFactor : 1f;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 10f;
                if (cs != null) { cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = h / MenuKit.DesignHeight; }
                undo.Add(() =>
                {
                    c.renderMode = mode0; c.worldCamera = cam0; c.planeDistance = pd0;
                    if (cs != null) { cs.uiScaleMode = sm; cs.scaleFactor = sf; }
                    foreach (var (g, l) in layers) if (g != null) g.layer = l;
                });
            }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            DestroyImmediate(tex);
            foreach (var u in undo) u();
            Canvas.ForceUpdateCanvases();
            cam.targetTexture = null;
            rt.Release();
            DestroyImmediate(rt);
            DestroyImmediate(go);
            return px;
        }

        /// <summary>The menus (scene 0): every overlay canvas - the home's and the intro's - over black.</summary>
        void ShotMenus(string name)
        {
            try
            {
                var list = new List<Canvas>();
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                    if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.enabled) list.Add(c);
                var px = RenderCanvas(list.ToArray(), ShotW, ShotH, Color.black);
                var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
                bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
                for (int i = 0; i < px.Length; i++) { var c = px[i]; c.a = 1f; px[i] = lin ? c.gamma : c; }
                tex.SetPixels(px);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(IntroPlayCheck.OutDir, name + ".png"), tex.EncodeToPNG());
                DestroyImmediate(tex);
                Note("  shot " + name + " (" + list.Count + " canvas(es))");
            }
            catch (Exception e) { Note("  shot " + name + " failed: " + e.Message); }
        }
    }
}
