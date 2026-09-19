using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The speed blur in the RUNNING GAME: a race is started, stopped dead
    /// with the clock, and the framebuffer the player is shown is read back
    /// with the blur off, the blur full, and the HUD in and out of each.
    ///
    /// A PLAY-MODE check because the half of this feature that can hurt is
    /// the half no edit-mode tool runs: SpeedBlur.Update handing the HUD
    /// canvas to a camera stacked on the PSX one, URP drawing that camera
    /// after the blur, the canvas taking its 240-or-480-line size from a
    /// camera that is not the one PSXCameraOutput points at the framebuffer,
    /// and all of it surviving the framebuffer being REBUILT (a rotated phone,
    /// a PICTURE change) and the option being switched off from the pause
    /// menu. SpeedBlurPreview has to ask Unity for the HUD camera's canvas
    /// geometry by hand; here it is a frame like any other.
    ///
    /// Run under the MOBILE pipeline — the one the WebGL player ships with.
    /// The editor sits on PC by default, and a check of the wrong renderer
    /// asset is a check of a blur no phone gets.
    ///
    /// WITH a graphics device (no -nographics): it reads pixels.
    ///   tools\speedblur-play-check.ps1 -> PSXRacing_speedblur_play_check.txt
    /// </summary>
    public static class SpeedBlurPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static int keepQuality;

        [MenuItem("PSX Racing/Check Speed Blur (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;

            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(0);
            if (s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the city circuit is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }

            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = 0;
            var cars = CarCatalog.All;
            if (cars.Count > 4)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id + ";" + cars[3].id;
                RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            }

            keepQuality = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("SpeedBlurPlayCheckRunner").AddComponent<SpeedBlurPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE BLUR SMEARS THE WORLD AND NOTHING ELSE." : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_speedblur_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class SpeedBlurPlayCheckRunner : MonoBehaviour
    {
        Camera cam;
        SpeedBlur blur;
        CarController car;
        /// <summary>Held every frame while set: the car's own FixedUpdate is
        /// stopped with the clock, so the field stays where it is put, and
        /// SpeedBlur.Update reads it exactly as it reads a real one.</summary>
        float holdSpeed = -1f;
        /// <summary>The control's road speed, published by hand each frame
        /// while the component is switched off, the tunnel pointed where the
        /// component last had it.</summary>
        float holdPreview = -1f;
        Vector2 holdFocus = new Vector2(0.5f, 0.5f);

        void Update()
        {
            if (holdSpeed >= 0f && car != null) car.forwardSpeed = holdSpeed;
        }

        void LateUpdate()
        {
            if (holdPreview >= 0f && cam != null) SpeedBlur.Preview(cam, holdPreview, holdFocus);
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();

            bool keepPref = SpeedBlurPrefs.Enabled;
            var keepPixels = PSXQuality.Current;
            try { SpeedBlurPlayCheck.Note("pipeline: " + QualitySettings.names[QualitySettings.GetQualityLevel()]); }
            catch (System.Exception) { }

            cam = Camera.main;
            blur = cam != null ? cam.GetComponent<SpeedBlur>() : null;
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            SpeedBlurPlayCheck.Check(cam != null && car != null, "the race scene has a lens and a player");
            if (cam == null || car == null) { Done(keepPref, keepPixels); yield break; }
            if (blur == null)
            {
                // A sandbox baked before SpeedBlur: wire it as the builder does
                // and say so. Good for the machine, not for certifying the bake.
                blur = cam.gameObject.AddComponent<SpeedBlur>();
                blur.car = car;
                blur.hudCanvas = GameObject.Find("HUDCanvas")?.GetComponent<Canvas>();
                SpeedBlurPlayCheck.Note("this scene was baked before SpeedBlur — wired by the check");
            }
            SpeedBlurPlayCheck.Check(blur.car == car && blur.hudCanvas != null,
                                     "SpeedBlur is wired to the player's car and the HUD canvas");
            if (blur.hudCanvas == null) { Done(keepPref, keepPixels); yield break; }

            // RETRO first: 240 lines, the coarsest HUD there is.
            PSXQuality.Current = PSXPixels.Retro;
            SpeedBlurPrefs.Enabled = true;
            // Let the grid settle and the HUD print itself, then stop the clock:
            // five frames of one still scene are only comparable if it is still.
            yield return new WaitForSecondsRealtime(2.5f);
            Time.timeScale = 0f;
            // And the chase rig stands down. It reads the same speed field this
            // check is about to fake — its FOV widens with it, straight from
            // the number, clock or no clock — and a lens that zooms between
            // two frames makes every pixel of them different.
            var chase = cam.GetComponent<ChaseCamera>();
            if (chase != null) chase.enabled = false;
            holdSpeed = 0f;
            yield return Frames(6);

            var rt = cam.targetTexture;
            SpeedBlurPlayCheck.Check(rt != null, "the PSX camera is on its framebuffer", rt != null ? rt.width + "x" + rt.height : "none");
            if (rt == null) { Done(keepPref, keepPixels); yield break; }
            SpeedBlurPlayCheck.Check(blur.IsSplit && blur.HudCamera != null && blur.hudCanvas.worldCamera == blur.HudCamera,
                                     "with SPEED BLUR on, the HUD canvas is on the stacked camera");
            SpeedBlurPlayCheck.Check(blur.HudCamera != null && blur.HudCamera.targetTexture == rt,
                                     "which is pointed at the same framebuffer, so the canvas is sized in ITS pixels");
            SpeedBlurPlayCheck.Check(Camera.main == cam, "Camera.main is still the lens");

            // ---- the five frames ----------------------------------------
            yield return Settle(0f);
            var off = SpeedBlurPreview.Read(rt);
            SpeedBlurPlayCheck.Check(NotBlank(off), "the framebuffer has a picture in it (the editor is rendering)");
            blur.hudCanvas.enabled = false;
            yield return Frames(3);
            var offBare = SpeedBlurPreview.Read(rt);
            blur.hudCanvas.enabled = true;

            holdSpeed = 100f;
            yield return Settle(SpeedBlur.MaxStrength);
            SpeedBlurPlayCheck.Check(Mathf.Abs(SpeedBlur.ActiveStrength - SpeedBlur.MaxStrength) < 0.005f
                                     && SpeedBlur.ActiveCamera == cam,
                                     "past 140 mph SpeedBlur publishes the top of the table for this camera",
                                     SpeedBlur.ActiveStrength.ToString("0.000") + ", tunnel " + SpeedBlur.ActiveInner.ToString("0.00"));
            Vector2 focus = SpeedBlur.ActiveFocus;
            float inner = SpeedBlur.ActiveInner;
            SpeedBlurPlayCheck.Check(focus.x > 0.3f && focus.x < 0.7f && focus.y >= 0.4f && focus.y <= 0.72f,
                                     "and points the tunnel down the road the car is facing",
                                     focus.x.ToString("0.00") + ", " + focus.y.ToString("0.00"));
            var max = SpeedBlurPreview.Read(rt);
            Save(max, rt, "full");
            blur.hudCanvas.enabled = false;
            yield return Frames(3);
            var maxBare = SpeedBlurPreview.Read(rt);

            // Where the player's car IS: the same still frame with its body
            // hidden. This is the car the handoff DRESSED at load, not the
            // reference shell the scene was saved with — which is the point:
            // the tag has to have found renderers that did not exist at bake.
            holdSpeed = 0f;
            yield return Settle(0f);
            var body = SpeedBlurPreview.OpaqueRenderers(car.gameObject);
            foreach (var r in body) r.enabled = false;
            yield return Frames(3);
            var offNoCar = SpeedBlurPreview.Read(rt);
            foreach (var r in body) r.enabled = true;
            blur.hudCanvas.enabled = true;
            holdSpeed = 100f;
            yield return Settle(SpeedBlur.MaxStrength);

            // The control: the same strength with the HUD back on the world
            // camera, which is how this would look without the stacked one.
            blur.enabled = false;
            blur.SetSplit(false);
            holdFocus = focus;
            holdPreview = 100f;
            yield return Frames(4);
            var control = SpeedBlurPreview.Read(rt);
            Save(control, rt, "control_hud_smeared");
            holdPreview = -1f;
            SpeedBlur.Preview(null, 0f);
            blur.enabled = true;
            yield return Frames(3);

            foreach (var v in SpeedBlurPreview.Verdicts(off, max, offBare, maxBare, control, offNoCar, null,
                                                        rt.width, rt.height, focus, inner))
                SpeedBlurPlayCheck.Check(v.ok, v.text);

            // ---- SPEED BLUR: OFF, from a stopped clock (the pause menu) ----
            SpeedBlurPrefs.Enabled = false;
            yield return Settle(0f);
            SpeedBlurPlayCheck.Check(!blur.IsSplit && blur.hudCanvas.worldCamera == cam,
                                     "SPEED BLUR: OFF hands the HUD straight back to the PSX camera");
            var offAgain = SpeedBlurPreview.Read(rt);
            Save(off, rt, "off_stacked");
            Save(offAgain, rt, "off_single");
            // Within TWO LEVELS of 255, not identical: a stacked camera goes
            // through the pipeline's colour buffers by a different route than
            // a lone one and may round a channel the other way. What must not
            // happen is a HUD that moved, resized or changed colour, or a
            // world that came out lighter or darker.
            Compare(off, offAgain, 2, out float within, out int worst);
            SpeedBlurPlayCheck.Check(within > 0.995f,
                                     "and the picture is the one the game drew before any of this existed",
                                     (within * 100f).ToString("0.00") + "% within 2/255 of the stacked frame, worst channel off by " + worst);
            SpeedBlurPrefs.Enabled = true;
            yield return Frames(4);

            // ---- the framebuffer is rebuilt under it --------------------
            PSXQuality.Current = PSXPixels.Sharp;
            yield return Frames(6);
            var rt2 = cam.targetTexture;
            SpeedBlurPlayCheck.Check(rt2 != null && rt2 != rt && rt2.height == 480,
                                     "PICTURE: SHARP rebuilt the framebuffer", rt2 != null ? rt2.width + "x" + rt2.height : "none");
            SpeedBlurPlayCheck.Check(blur.IsSplit && blur.HudCamera != null && blur.HudCamera.targetTexture == rt2,
                                     "and the HUD camera followed it to the new one");
            if (rt2 != null)
            {
                yield return Settle(SpeedBlur.MaxStrength);
                var sharp = SpeedBlurPreview.Read(rt2);
                Save(sharp, rt2, "full_sharp");
                blur.hudCanvas.enabled = false;
                yield return Frames(3);
                var sharpBare = SpeedBlurPreview.Read(rt2);
                blur.hudCanvas.enabled = true;
                int hud = 0;
                for (int i = 0; i < sharp.Length; i++)
                    if (Mathf.Abs(sharp[i].r - sharpBare[i].r) > 24 || Mathf.Abs(sharp[i].g - sharpBare[i].g) > 24) hud++;
                SpeedBlurPlayCheck.Check(hud > 200, "with the HUD still drawn on it at 480 lines", hud + " px");
            }

            Done(keepPref, keepPixels);
        }

        /// <summary>Hold until the published strength has faded to a target.
        /// The fade runs on unscaled time, so it moves with the clock stopped.</summary>
        IEnumerator Settle(float target)
        {
            float until = Time.realtimeSinceStartup + 4f;
            while (Mathf.Abs(SpeedBlur.ActiveStrength - target) > 0.0005f && Time.realtimeSinceStartup < until)
                yield return null;
            yield return Frames(3);
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static bool NotBlank(Color32[] px)
        {
            int lo = 255, hi = 0;
            for (int i = 0; i < px.Length; i += 7)
            {
                int l = (px[i].r + px[i].g + px[i].b) / 3;
                if (l < lo) lo = l;
                if (l > hi) hi = l;
            }
            return hi - lo > 40;
        }

        static void Compare(Color32[] a, Color32[] b, int tol, out float within, out int worst)
        {
            within = 0f; worst = 255;
            if (a.Length != b.Length || a.Length == 0) return;
            int ok = 0; worst = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(a[i].r - b[i].r),
                                  Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b)));
                if (d <= tol) ok++;
                if (d > worst) worst = d;
            }
            within = ok / (float)a.Length;
        }

        static void Save(Color32[] px, RenderTexture rt, string name)
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(dir);
            var tex = SpeedBlurPreview.Doubled(px, rt.width, rt.height, rt.height < 400 ? 2 : 1);
            File.WriteAllBytes(Path.Combine(dir, "speedblur_play_" + name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        void Done(bool keepPref, PSXPixels keepPixels)
        {
            // Both are PlayerPrefs, and the editor's are shared with the
            // owner's own project: leave them as they were found.
            SpeedBlurPrefs.Enabled = keepPref;
            PSXQuality.Current = keepPixels;
            Time.timeScale = 1f;
            QualitySettings.SetQualityLevel(SpeedBlurPlayCheck.keepQuality, true);
            SpeedBlurPlayCheck.Finish();
            EditorApplication.Exit(SpeedBlurPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
