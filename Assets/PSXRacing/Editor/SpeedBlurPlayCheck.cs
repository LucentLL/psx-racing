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
            int s = TrackCatalog.SceneIndex(TrackCatalog.CheckVenue);
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
            RaceHandoff.TrackIndex = TrackCatalog.CheckVenue;
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
            // MOTION's history checks drive the lens by hand (the rig is off).
            if (cam != null && jumpNext) { cam.transform.position += moveDir * 100f; jumpNext = false; moveSpeed = 0f; jumpFrame = Time.frameCount; }
            else if (cam != null && moveSpeed > 0f) cam.transform.position += moveDir * (moveSpeed * Time.unscaledDeltaTime);
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();

            var keepPref = SpeedBlurPrefs.Mode;
            var keepPixels = PSXQuality.Current;
            // LENS FX OFF for the whole run. Since the lens pass (2026-09-21) the
            // HUD split is held by EITHER switch, so with the lens on (its
            // default) "SPEED BLUR: OFF hands the HUD back to the PSX camera"
            // would fail for a correct reason; and a night or rain scene would
            // add drops and bokeh to frames this check compares pixel for pixel.
            keepLens = LensFxPrefs.Enabled;
            LensFxPrefs.Enabled = false;
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
            // ZOOM, whatever the owner's saved mode is (put back by Done).
            SpeedBlurPrefs.Mode = SpeedBlurMode.Zoom;
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

            // ZOOM IGNORES THE OTHER CARS' LAYER (2026-10-16): MOTION masks
            // every car (SpeedBlur.OtherCarRenderingLayer), the zoom the
            // player's alone, as it always did. The same full-strength frame
            // with that layer stripped from every renderer must be the same
            // frame (within 2/255, as the same frame read twice is).
            {
                var stripped = new System.Collections.Generic.List<Renderer>();
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                    if ((r.renderingLayerMask & SpeedBlur.OtherCarRenderingLayer) != 0)
                    {
                        r.renderingLayerMask &= ~SpeedBlur.OtherCarRenderingLayer;
                        stripped.Add(r);
                    }
                yield return Frames(3);
                var maxBareUntagged = SpeedBlurPreview.Read(rt);
                int retagged = 0;
                foreach (var r in stripped)
                {
                    if (r == null) continue;
                    if ((r.renderingLayerMask & SpeedBlur.OtherCarRenderingLayer) != 0) retagged++;
                    r.renderingLayerMask |= SpeedBlur.OtherCarRenderingLayer;
                }
                // The control: the same tagged frame, read again. Renderers
                // whose tags change can be drawn in another order, and two
                // coplanar surfaces then trade a few pixels: that is the noise
                // floor, not the zoom.
                yield return Frames(3);
                var maxBareAgain = SpeedBlurPreview.Read(rt);
                int differ = 0, noise = 0;
                for (int i = 0; i < maxBare.Length; i++)
                {
                    if (Off(maxBare[i], maxBareUntagged[i])) differ++;
                    if (Off(maxBare[i], maxBareAgain[i])) noise++;
                }
                SpeedBlurPlayCheck.Check(stripped.Count > 0 && retagged == 0 && differ <= noise + maxBare.Length / 5000,
                                         "ZOOM ignores the other cars' layer: the 140 mph frame with it stripped is the same frame",
                                         stripped.Count + " renderers stripped, " + differ + " px off by more than 2/255 (the same frame read twice: " +
                                         noise + ", of " + maxBare.Length + ")");
            }

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

            yield return MotionChecks();
            yield return HalationChecks();
            Done(keepPref, keepPixels);
        }

        static bool Off(Color32 a, Color32 b) =>
            Mathf.Abs(a.r - b.r) > 2 || Mathf.Abs(a.g - b.g) > 2 || Mathf.Abs(a.b - b.b) > 2;

        /// <summary>
        /// THE HALATION'S GLOW (2026-10-16, Halation.cs): built from the
        /// running game's framebuffer every frame, wired into the display
        /// material, and what it costs - the GPU time of its passes (the
        /// "PSX Halation" sample) at 480 and at 240 lines.
        /// </summary>
        IEnumerator HalationChecks()
        {
            var output = cam != null ? cam.GetComponent<PSXCameraOutput>() : null;
            var mat = output != null && output.display != null ? output.display.material : null;
            if (!Halation.Wanted(mat))
            {
                SpeedBlurPlayCheck.Note("halation: FILM GRADE is off in this editor's prefs - no glow to build or time");
                yield break;
            }
            var rec = Halation.Profile.GetRecorder();
            rec.enabled = true;
            foreach (var pixels in new[] { PSXPixels.Sharp, PSXPixels.Retro })
            {
                PSXQuality.Current = pixels;
                yield return Frames(10);
                var glow = mat.GetTexture(Halation.GlowTexId) as RenderTexture;
                var fb = cam.targetTexture;
                bool built = glow != null && fb != null && mat.GetFloat(Halation.GlowOnId) > 0.5f &&
                             glow.height == Mathf.CeilToInt(fb.height / (float)Halation.Factor(fb.height));
                SpeedBlurPlayCheck.Check(built, "halation: the glow is built from the " + (fb != null ? fb.height : 0) +
                                         "-line frame and wired into the display",
                                         glow != null ? glow.width + "x" + glow.height : "none");
                double gpu = 0, cpu = 0;
                int n = 0;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    if (rec.sampleBlockCount > 0) { gpu += rec.gpuElapsedNanoseconds; cpu += rec.elapsedNanoseconds; n++; }
                }
                SpeedBlurPlayCheck.Note("halation cost at " + (fb != null ? fb.width + "x" + fb.height : "?") + ": " +
                                        (n > 0 ? (gpu / n / 1e6).ToString("0.000") + " ms GPU, " +
                                                 (cpu / n / 1e6).ToString("0.000") + " ms CPU a frame (" + n + " frames)"
                                               : "no samples (no GPU recorder here)") +
                                        "; GPU recorder " + (SystemInfo.supportsGpuRecorder ? "supported" : "unsupported"));
            }
            rec.enabled = false;
        }

        // ---- SPEED BLUR: MOTION (2026-10-13) ---------------------------------

        Vector3 moveDir;
        float moveSpeed;
        bool jumpNext;
        int jumpFrame = -1;
        /// <summary>Per rendered frame of the PSX camera: did MOTION's
        /// frame-to-frame history smear it (false = drawn sharp, a cut).</summary>
        readonly System.Collections.Generic.Dictionary<int, bool> movedAt = new System.Collections.Generic.Dictionary<int, bool>();
        Color32[] shot;

        void OnEndCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera c)
        {
            if (c == cam && MotionBlurFx.LastFrame == Time.frameCount) movedAt[Time.frameCount] = MotionBlurFx.LastMoved;
        }

        /// <summary>
        /// MOTION in the running game, on the Mobile pipeline, out of the real
        /// framebuffer: the same frames and verdicts as SpeedBlurPreview's (the
        /// previous lens handed in by MotionBlurFx.PreviewSet, the clock
        /// stopped), then the frame-to-frame history with the lens driven by
        /// hand: a steady lens smears every frame, and a 300 m jump, a view
        /// switch and a TeleportTo each give a frame drawn sharp.
        /// </summary>
        IEnumerator MotionChecks()
        {
            // The cabin and the instruments are screen OVERLAYS, composited
            // after the framebuffer is shown: neither blur can reach them.
            foreach (var type in new[] { typeof(CockpitView), typeof(GaugeCluster) })
            {
                var comp = FindAnyObjectByType(type, FindObjectsInactive.Include) as Component;
                var canvas = comp != null ? comp.GetComponentInParent<Canvas>(true) : null;
                var root = canvas != null ? canvas.rootCanvas : null;
                bool clear = root != null && (root.renderMode == RenderMode.ScreenSpaceOverlay ||
                                              (root.worldCamera != null && root.worldCamera != cam));
                SpeedBlurPlayCheck.Check(clear, "the " + type.Name + " canvas is not drawn by the PSX camera, so no blur pass can reach it",
                                         root != null ? root.renderMode.ToString() : "none");
            }

            var rt = cam.targetTexture;
            if (rt == null) yield break;
            int w = rt.width, h = rt.height;
            var t = car.transform;
            Vector3 pos = cam.transform.position;
            Quaternion rot = cam.transform.rotation;
            float v = SpeedBlurPreview.MotionKmh / 3.6f;
            var still = MotionBlurFx.ViewAt(pos, rot);
            var straightPrev = MotionBlurFx.ViewAt(pos - t.forward * (v * SpeedBlurPreview.MotionDt), rot);
            var yawPrev = MotionBlurFx.ViewAt(pos, Quaternion.AngleAxis(SpeedBlurPreview.MotionYawDeg, Vector3.up) * rot);
            var cutPrev = MotionBlurFx.ViewAt(pos - t.forward * 300f, rot);
            SpeedBlurPlayCheck.Note("MOTION frames at " + w + "x" + h + ", " + SpeedBlurPreview.MotionKmh + " km/h");

            // The zoom at the top of the table, before MOTION runs at all.
            SpeedBlurPrefs.Forced = SpeedBlurMode.Zoom;
            holdSpeed = 100f;
            yield return Settle(SpeedBlur.MaxStrength);
            blur.hudCanvas.enabled = false;
            yield return Frames(3);
            var zoomBefore = SpeedBlurPreview.Read(rt);
            blur.hudCanvas.enabled = true;

            SpeedBlurPrefs.Forced = SpeedBlurMode.Motion;
            holdSpeed = v;
            float shutter = SpeedBlur.ShutterForMph(v / SpeedBlur.MpsPerMph);
            float until = Time.realtimeSinceStartup + 4f;
            while (Mathf.Abs(SpeedBlur.ActiveShutter - shutter) > 0.002f && Time.realtimeSinceStartup < until)
                yield return null;
            SpeedBlurPlayCheck.Check(SpeedBlur.ActiveMode == SpeedBlurMode.Motion && Mathf.Abs(SpeedBlur.ActiveShutter - shutter) <= 0.002f,
                                     "SPEED BLUR: MOTION publishes the shutter for " + SpeedBlurPreview.MotionKmh + " km/h",
                                     SpeedBlur.ActiveShutter.ToString("0.000"));

            // Every frame at that speed; the references with a previous lens
            // where this one is (no motion: the pass hands every pixel back).
            yield return Shot(still, false);
            var off = shot;
            yield return Shot(straightPrev, false);
            var straight = shot;
            Save(straight, rt, "motion_straight");
            blur.hudCanvas.enabled = false;
            yield return Shot(still, false);
            var offBare = shot;
            yield return Shot(straightPrev, false);
            var straightBare = shot;
            yield return Shot(straightPrev, true);
            var straightVel = shot;
            yield return Shot(yawPrev, false);
            var yawBare = shot;
            Save(yawBare, rt, "motion_yaw");
            yield return Shot(yawPrev, true);
            var yawVel = shot;
            yield return Shot(cutPrev, false);
            var cutBare = shot;
            var body = SpeedBlurPreview.OpaqueRenderers(car.gameObject);
            foreach (var r in body) r.enabled = false;
            yield return Shot(still, false);
            var offNoCar = shot;
            foreach (var r in body) r.enabled = true;
            MotionBlurFx.PreviewSet(null, default, default, 0f);

            // And the zoom after it.
            SpeedBlurPrefs.Forced = SpeedBlurMode.Zoom;
            holdSpeed = 100f;
            yield return Settle(SpeedBlur.MaxStrength);
            yield return Frames(3);
            var zoomAfter = SpeedBlurPreview.Read(rt);
            blur.hudCanvas.enabled = true;

            foreach (var verdict in SpeedBlurPreview.MotionVerdicts(off, offBare, offNoCar, straight, straightBare, straightVel,
                                                                  yawBare, yawVel, cutBare, null, null, w, h))
                SpeedBlurPlayCheck.Check(verdict.ok, verdict.text);
            Compare(zoomBefore, zoomAfter, 2, out float zoomWithin, out int zoomWorst);
            SpeedBlurPlayCheck.Check(zoomWithin > 0.999f, "and ZOOM after MOTION is the ZOOM before",
                                     (zoomWithin * 100f).ToString("0.00") + "% within 2/255, worst " + zoomWorst);

            // ---- the history: a lens that really moves, frame after frame --
            SpeedBlurPrefs.Forced = SpeedBlurMode.Motion;
            holdSpeed = v;
            yield return Frames(6);
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndCamera;
            blur.hudCanvas.enabled = false;
            var keepView = ChaseCamera.Current;
            moveDir = t.forward;
            moveSpeed = v;
            yield return Frames(8);
            int from = Time.frameCount - 5;
            bool steady = true;
            for (int f = from; f < Time.frameCount; f++) if (!movedAt.TryGetValue(f, out bool m) || !m) steady = false;
            SpeedBlurPlayCheck.Check(steady, "a lens driven forward frame after frame smears every frame (no false cuts)",
                                     "exposure x" + MotionBlurFx.LastPerFrame.ToString("0.00") + " of the frame's motion");

            // A 100 m jump: that frame is drawn sharp, and so is the still
            // frame after it (within 2/255: anything streamed or culled by
            // the new position may land a frame late).
            jumpNext = true;
            yield return null;
            var cutFrame = SpeedBlurPreview.Read(rt);
            yield return null;
            var nextFrame = SpeedBlurPreview.Read(rt);
            bool cutSharp = jumpFrame > 0 && movedAt.TryGetValue(jumpFrame, out bool jm) && !jm;
            Compare(cutFrame, nextFrame, 2, out float jumpSame, out int jumpWorst);
            SpeedBlurPlayCheck.Check(cutSharp && jumpSame >= 0.99f,
                                     "a 100 m jump of the lens is a cut: that frame is not smeared (it matches the still frame after it)",
                                     (jumpSame * 100f).ToString("0.00") + "% within 2/255, worst " + jumpWorst + ", flagged " + cutSharp);

            // A view switch and a TeleportTo, with the lens still moving.
            moveSpeed = v;
            yield return Frames(5);
            int switchAt = Time.frameCount;
            ChaseCamera.PreviewView(keepView == ChaseCamera.View.Close ? ChaseCamera.View.Chase : ChaseCamera.View.Close);
            yield return Frames(4);
            SpeedBlurPlayCheck.Check(CutWithin(switchAt, 3), "a VIEW SWITCH is a cut (a frame drawn sharp)");
            ChaseCamera.PreviewView(keepView);
            yield return Frames(5);
            int teleportAt = Time.frameCount;
            car.TeleportTo(t.position, t.rotation);
            yield return Frames(4);
            SpeedBlurPlayCheck.Check(CutWithin(teleportAt, 3), "a TeleportTo is a cut (a frame drawn sharp)");
            moveSpeed = 0f;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndCamera;

            cam.transform.SetPositionAndRotation(pos, rot);
            blur.hudCanvas.enabled = true;
            SpeedBlurPrefs.Forced = null;
            holdSpeed = 0f;
            yield return Frames(3);
        }

        bool CutWithin(int from, int frames)
        {
            for (int f = from; f <= from + frames; f++)
                if (movedAt.TryGetValue(f, out bool m) && !m) return true;
            return false;
        }

        /// <summary>One MOTION frame of the running game, its previous lens
        /// handed in; read into <see cref="shot"/>.</summary>
        IEnumerator Shot(Matrix4x4 prevView, bool debug)
        {
            MotionBlurFx.PreviewSet(cam, prevView, cam.projectionMatrix, SpeedBlurPreview.MotionDt);
            MotionBlurFx.DebugVelocity = debug;
            yield return Frames(3);
            shot = SpeedBlurPreview.Read(cam.targetTexture);
            MotionBlurFx.DebugVelocity = false;
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
            ShotSidecar.WritePng(Path.Combine(dir, "speedblur_play_" + name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        /// <summary>The owner's LENS FX switch, put back by <see cref="Done"/>.</summary>
        bool keepLens = true;

        void Done(SpeedBlurMode keepPref, PSXPixels keepPixels)
        {
            // Both are PlayerPrefs, and the editor's are shared with the
            // owner's own project: leave them as they were found.
            SpeedBlurPrefs.Forced = null;
            MotionBlurFx.PreviewSet(null, default, default, 0f);
            MotionBlurFx.DebugVelocity = false;
            SpeedBlurPrefs.Mode = keepPref;
            LensFxPrefs.Enabled = keepLens;
            PSXQuality.Current = keepPixels;
            Time.timeScale = 1f;
            QualitySettings.SetQualityLevel(SpeedBlurPlayCheck.keepQuality, true);
            SpeedBlurPlayCheck.Finish();
            EditorApplication.Exit(SpeedBlurPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
