using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The speed blur, photographed and MEASURED on a real circuit, through
    /// both render pipelines the project has.
    ///
    /// A blur is the kind of effect that fails as a picture rather than as an
    /// error, and every way this one can fail looks like "something is
    /// happening at the edge of the frame":
    ///   - the pass reads an empty source, and the edge of the frame goes
    ///     dark at speed (checked: the ring keeps its brightness);
    ///   - the pass is on the PC renderer and not the Mobile one, so every
    ///     preview shows a blur no phone gets (checked: both quality levels);
    ///   - the middle of the frame — the car, the road ahead — is resampled
    ///     and goes soft with it (checked: bit-for-bit identical);
    ///   - the HUD is still drawn by the world camera and the lap counter is
    ///     smeared across the corner (checked: the HUD's footprint is the same
    ///     with the blur at full strength as with none — and a CONTROL frame,
    ///     with the HUD deliberately left on the world camera, must FAIL that
    ///     same measurement, or the measurement is not measuring);
    ///   - the tunnel closes over the player's own car and smears it
    ///     (checked: every pixel of the car outside the tunnel is identical
    ///     at 140 mph — and a second CONTROL, the car untagged, must fail).
    ///
    /// Measured on the raw framebuffer; the PNGs are the same frames through
    /// PSX/Blit's dither, point-doubled, which is what the player sees.
    ///
    ///   tools\speedblur-preview.ps1  ->  Screenshots\speedblur_*.png
    /// </summary>
    public static class SpeedBlurPreview
    {
        static readonly (string name, int w, int h)[] Sizes =
        {
            // The owner's phone aspect (2000x923) at RETRO's 240 lines and at
            // SHARP's 480 — the shipped default, where the smear is twice as
            // many pixels long for the same sixteen taps — and 16:9.
            ("phone", 520, 240),
            ("phone_sharp", 1040, 480),
            ("16x9", 426, 240),
        };

        [MenuItem("PSX Racing/Preview Speed Blur")]
        public static void Capture()
        {
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);
            int keepQuality = QualitySettings.GetQualityLevel();
            int fails = 0;
            try
            {
                for (int q = 0; q < QualitySettings.count; q++)
                {
                    if (QualitySettings.GetRenderPipelineAssetAt(q) == null) continue;
                    QualitySettings.SetQualityLevel(q, true);
                    string pipe = QualitySettings.names[q].ToLower();
                    foreach (var s in Sizes)
                        fails += Shoot(outDir, pipe, s.name, s.w, s.h, night: false);
                    // After dark, for the eye: lamp glows streaking is most of
                    // what the reference looks like. Not measured twice.
                    if (q == 0) fails += Shoot(outDir, pipe, "phone_night", 520, 240, night: true);
                }

                // SPEED BLUR: MOTION (2026-10-13), after EVERY zoom shot so
                // the zoom's pictures are taken exactly as they were before it
                // existed (tools\speedblur-preview.ps1 -Baseline compares them).
                // The phone's 240 lines (12 taps) on Mobile, SHARP's 480 (16)
                // on both.
                if (System.Environment.GetEnvironmentVariable("PSX_SPEEDBLUR_MOTION") != "0")
                {
                    for (int q = 0; q < QualitySettings.count; q++)
                    {
                        if (QualitySettings.GetRenderPipelineAssetAt(q) == null) continue;
                        QualitySettings.SetQualityLevel(q, true);
                        string pipe = QualitySettings.names[q].ToLower();
                        if (q == 0) fails += ShootMotion(outDir, pipe, "phone", 520, 240);
                        fails += ShootMotion(outDir, pipe, "phone_sharp", 1040, 480);
                    }
                    // The owner's pictures: what WebGL ships (quality 0).
                    QualitySettings.SetQualityLevel(0, true);
                    fails += OwnerShots(outDir);
                }
            }
            finally
            {
                SpeedBlur.Preview(null, 0f);
                MotionBlurFx.PreviewSet(null, default, default, 0f);
                MotionBlurFx.DebugVelocity = false;
                Wake.Clear();
                QualitySettings.SetQualityLevel(keepQuality, true);
            }
            Debug.Log(fails == 0 ? "[SpeedBlur] ALL CHECKS OK"
                                 : "[SpeedBlur] FAIL — " + fails + " check(s) failed");
            Debug.Log("[SpeedBlur] done");
        }

        static int Shoot(string outDir, string pipe, string label, int w, int h, bool night)
        {
            string tag = pipe + "_" + label;
            if (!PSXScreenshotTool.Open(TrackCatalog.At(0), out var cam, out var player))
                return Fail(tag, "could not open " + TrackCatalog.At(0).id + " (run the scene build)");
            var blur = cam.GetComponent<SpeedBlur>();
            if (blur == null)
            {
                // A sandbox whose scenes were baked before SpeedBlur existed:
                // wire it the way the builder does, and SAY so — the pictures
                // are good for judging the look, not for certifying the bake.
                blur = cam.gameObject.AddComponent<SpeedBlur>();
                blur.car = player.GetComponent<CarController>();
                blur.hudCanvas = GameObject.Find("HUDCanvas")?.GetComponent<Canvas>();
                Debug.LogWarning("[SpeedBlur] " + tag + " note  this scene was baked before SpeedBlur — wired by the tool");
            }
            if (blur.hudCanvas == null)
                return Fail(tag, "SpeedBlur has no HUD canvas wired — nothing here can show the HUD staying sharp");

            // Only the framebuffer's own contents. Open() points the cluster,
            // the cabin and the touch panel at the camera so other shots can
            // show them; in the game they are screen-resolution overlays drawn
            // AFTER the framebuffer and no pass on this camera can touch them.
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (c != blur.hudCanvas && c.isRootCanvas) c.enabled = false;

            if (night)
            {
                int hour = TimeOfDay.Count - 1;
                for (int i = 0; i < TimeOfDay.Count; i++)
                    if (TimeOfDay.At(i).lightsOn) hour = i;
                TimeOfDay.Apply(hour, GameObject.Find("Sun")?.GetComponent<Light>());
                var globals = Object.FindFirstObjectByType<PSXGlobals>();
                if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                PSXScreenshotTool.SetNightGlow(true);
                foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude))
                    lights.PreviewBuild(true);
            }

            // The far chase view at rest, through the game's own fit
            // (ChaseCamera.SteadyPose), the way CaptureCameras frames it. No
            // lens shift: the tunnel focus is measured on the unshifted frame.
            var t = player.transform;
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            ChaseCamera.SteadyPose(ChaseCamera.View.Chase, w / (float)h, 0f, ChaseCamera.DefaultSpeedFullMps, t,
                                   ChaseCamera.FrameOf(player), default,
                                   out Vector3 pos, out Quaternion rot, out float chaseFov, out _);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = chaseFov;

            int fails = 0;
            // The owner's four speeds, and the last of them is as far as the
            // table goes: every measurement below is taken there.
            const float Mph = SpeedBlur.MpsPerMph;
            var rows = SpeedBlur.Stages;
            float top = rows[rows.Length - 1].mph * Mph;
            // What Update does every frame, by hand: tag the car for the mask
            // and point the tunnel down the road it is facing.
            blur.MarkCar();
            Vector2 focus = blur.Heading();
            float inner = SpeedBlur.LookFor(top).inner;

            // ---- the control: the HUD left on the world camera ------------
            var control = Render(cam, blur, w, h, top, focus, tag + "_control_hud_smeared", outDir, write: !night);

            // ---- the game's arrangement: the HUD on its stacked camera ----
            if (!blur.SetSplit(true))
                return fails + Fail(tag, "THE HUD COULD NOT BE SPLIT OFF — URP refused the camera stack");

            var off = Render(cam, blur, w, h, 0f, focus, tag + "_0_off", outDir, write: true);
            Render(cam, blur, w, h, 40f * Mph, focus, tag + "_1_040mph", outDir, write: true);
            Render(cam, blur, w, h, 60f * Mph, focus, tag + "_2_060mph", outDir, write: true);
            Render(cam, blur, w, h, 100f * Mph, focus, tag + "_3_100mph", outDir, write: true);
            var max = Render(cam, blur, w, h, top, focus, tag + "_4_140mph", outDir, write: true);
            if (night) { blur.SetSplit(false); return fails; }

            blur.hudCanvas.enabled = false;
            var offBare = Render(cam, blur, w, h, 0f, focus, null, outDir, write: false);
            var maxBare = Render(cam, blur, w, h, top, focus, null, outDir, write: false);

            // The player's car: where it IS (the frame with its body hidden
            // says), and — the control — what 140 mph does to it when the pass
            // is not told which renderers are the car.
            var body = OpaqueRenderers(player);
            foreach (var r in body) r.enabled = false;
            var offNoCar = Render(cam, blur, w, h, 0f, focus, null, outDir, write: false);
            foreach (var r in body) r.enabled = true;
            foreach (var r in body) r.renderingLayerMask &= ~SpeedBlur.CarRenderingLayer;
            var maxUnmasked = Render(cam, blur, w, h, top, focus, tag + "_control_car_smeared", outDir, write: true);
            blur.MarkCar();

            blur.hudCanvas.enabled = true;
            blur.SetSplit(false);

            // ---- measure -------------------------------------------------
            foreach (var v in Verdicts(off, max, offBare, maxBare, control, offNoCar, maxUnmasked, w, h, focus, inner))
            {
                if (v.ok) Ok(tag, v.text);
                else fails += Fail(tag, v.text);
            }
            return fails;
        }

        // ---- SPEED BLUR: MOTION (2026-10-13) ---------------------------------

        /// <summary>MOTION is judged at the reference's road speed (Touge
        /// Attack was measured at 90-140 km/h) and a 60 fps frame.</summary>
        internal const float MotionKmh = 110f, MotionDt = 1f / 60f;
        /// <summary>The turning frame: the lens yawed this far since the last
        /// frame (90 deg/s, a hairpin) and did not move.</summary>
        internal const float MotionYawDeg = 1.5f;
        /// <summary>Depth classes for the straight frame: at or beyond
        /// FarMetres (the sky included) is "far"; within NearMetres is the
        /// near road and roadside.</summary>
        const float FarMetres = 250f, NearMetres = 20f;
        /// <summary>The motion readout's resolution near zero, in pixels:
        /// one sRGB byte at 0.5 is ~0.0058 of the 256 px range, ~1.5 px.</summary>
        const float ReadoutStepPx = 2f;

        /// <summary>The scene's SpeedBlur, wired the builder's way if a stale
        /// bake has none (and said so).</summary>
        static SpeedBlur Wire(Camera cam, GameObject player, string tag)
        {
            var blur = cam.GetComponent<SpeedBlur>();
            if (blur != null) return blur;
            blur = cam.gameObject.AddComponent<SpeedBlur>();
            blur.car = player.GetComponent<CarController>();
            blur.hudCanvas = GameObject.Find("HUDCanvas")?.GetComponent<Canvas>();
            Debug.LogWarning("[SpeedBlur] " + tag + " note  this scene was baked before SpeedBlur — wired by the tool");
            return blur;
        }

        /// <summary>
        /// SPEED BLUR: MOTION, measured on the first circuit's grid with the
        /// chase lens: the car and the HUD untouched; on a STRAIGHT (the lens
        /// 110 km/h x 1/60 s further back last frame) the sky and anything
        /// past 250 m bit-for-bit what the camera drew while the near road
        /// and roadside smear; on a TURN (the lens yawed 1.5 deg) the sides
        /// of the frame smear sideways, sky included; a 300 m jump (a cut)
        /// draws a sharp frame; and the zoom after all of it is the zoom
        /// before. The pixel motions are read from the pass itself
        /// (MotionBlurFx.DebugVelocity writes them as a colour).
        /// </summary>
        static int ShootMotion(string outDir, string pipe, string label, int w, int h)
        {
            string tag = pipe + "_" + label + "_motion";
            if (!PSXScreenshotTool.Open(TrackCatalog.At(0), out var cam, out var player))
                return Fail(tag, "could not open " + TrackCatalog.At(0).id + " (run the scene build)");
            var blur = Wire(cam, player, tag);
            if (blur.hudCanvas == null)
                return Fail(tag, "SpeedBlur has no HUD canvas wired — nothing here can show the HUD staying sharp");
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (c != blur.hudCanvas && c.isRootCanvas) c.enabled = false;

            var t = player.transform;
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            ChaseCamera.SteadyPose(ChaseCamera.View.Chase, w / (float)h, 0f, ChaseCamera.DefaultSpeedFullMps, t,
                                   ChaseCamera.FrameOf(player), default,
                                   out Vector3 pos, out Quaternion rot, out float chaseFov, out _);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = chaseFov;
            // The previous lens has to be the same lens: pin the aspect to the
            // shot's before reading the projection back.
            cam.aspect = w / (float)h;
            blur.MarkCar();
            Vector2 focus = blur.Heading();
            if (!blur.SetSplit(true))
                return Fail(tag, "THE HUD COULD NOT BE SPLIT OFF — URP refused the camera stack");

            float v = MotionKmh / 3.6f;
            float top = SpeedBlur.Stages[SpeedBlur.Stages.Length - 1].mph * SpeedBlur.MpsPerMph;
            Matrix4x4 proj = cam.projectionMatrix;
            var straightPrev = MotionBlurFx.ViewAt(pos - t.forward * (v * MotionDt), rot);
            var yawPrev = MotionBlurFx.ViewAt(pos, Quaternion.AngleAxis(MotionYawDeg, Vector3.up) * rot);
            var cutPrev = MotionBlurFx.ViewAt(pos - t.forward * 300f, rot);

            blur.hudCanvas.enabled = false;
            var zoomBefore = Render(cam, blur, w, h, top, focus, null, outDir, write: false);
            blur.hudCanvas.enabled = true;
            var off = Render(cam, blur, w, h, 0f, focus, null, outDir, write: false);
            var straight = RenderMotion(cam, blur, w, h, v, straightPrev, proj, false, tag + "_straight", outDir, true);
            blur.hudCanvas.enabled = false;
            var offBare = Render(cam, blur, w, h, 0f, focus, null, outDir, write: false);
            var straightBare = RenderMotion(cam, blur, w, h, v, straightPrev, proj, false, null, outDir, false);
            var straightVel = RenderMotion(cam, blur, w, h, v, straightPrev, proj, true, null, outDir, false);
            var yawBare = RenderMotion(cam, blur, w, h, v, yawPrev, proj, false, tag + "_yaw", outDir, true);
            var yawVel = RenderMotion(cam, blur, w, h, v, yawPrev, proj, true, null, outDir, false);
            var cutBare = RenderMotion(cam, blur, w, h, v, cutPrev, proj, false, null, outDir, false);
            var body = OpaqueRenderers(player);
            foreach (var r in body) r.enabled = false;
            var offNoCar = Render(cam, blur, w, h, 0f, focus, null, outDir, write: false);
            foreach (var r in body) r.enabled = true;
            var zoomAfter = Render(cam, blur, w, h, top, focus, null, outDir, write: false);
            blur.hudCanvas.enabled = true;
            blur.SetSplit(false);
            cam.ResetAspect();

            int fails = 0;
            foreach (var verdict in MotionVerdicts(off, offBare, offNoCar, straight, straightBare, straightVel,
                                                   yawBare, yawVel, cutBare, zoomBefore, zoomAfter, w, h))
            {
                if (verdict.ok) Ok(tag, verdict.text);
                else fails += Fail(tag, verdict.text);
            }
            return fails;
        }

        /// <summary>One MOTION frame: the lens as it is, its previous frame
        /// seen through <paramref name="prevView"/> 1/60 s ago. With
        /// <paramref name="debug"/> the pass writes each pixel's smear instead
        /// (<see cref="Vel"/> reads it).</summary>
        internal static Color32[] RenderMotion(Camera cam, SpeedBlur blur, int w, int h, float speedMps,
                                               Matrix4x4 prevView, Matrix4x4 prevProj, bool debug,
                                               string name, string outDir, bool write)
        {
            MotionBlurFx.PreviewSet(cam, prevView, prevProj, MotionDt);
            MotionBlurFx.DebugVelocity = debug;
            try
            {
                return Render(cam, blur, w, h, speedMps, new Vector2(0.5f, 0.5f), name, outDir,
                              write && !debug, SpeedBlurMode.Motion);
            }
            finally
            {
                MotionBlurFx.PreviewSet(null, default, default, 0f);
                MotionBlurFx.DebugVelocity = false;
            }
        }

        /// <summary>How the debug colour was stored: linearly, or sRGB-encoded
        /// (a linear project's default RenderTexture), and the stored value
        /// of NO motion per channel. Calibrated off the sky of the straight
        /// frame, whose motion is exactly zero: an sRGB byte near 0.5 is
        /// +-0.75 px of readout, so a zero taken from the curve alone read the
        /// still sky as moving a pixel.</summary>
        static bool velSrgb;
        static float zeroR = 0.5f, zeroG = 0.5f;

        static float Lin(float c) => !velSrgb ? c
            : c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// <summary>A debug pixel: its smear in pixels (x, y) and its depth in
        /// metres (infinity for the sky or past 250 m).</summary>
        static void Vel(Color32 c, out float vx, out float vy, out float depthM)
        {
            vx = (Lin(c.r / 255f) - zeroR) * 256f;
            vy = (Lin(c.g / 255f) - zeroG) * 256f;
            float b = Lin(c.b / 255f);
            depthM = b >= 0.985f ? float.PositiveInfinity : b * 255f;
        }

        static float Median(System.Collections.Generic.List<float> xs, float q = 0.5f)
        {
            if (xs.Count == 0) return 0f;
            xs.Sort();
            return xs[Mathf.Clamp((int)(q * (xs.Count - 1)), 0, xs.Count - 1)];
        }

        /// <summary>What a MOTION frame has to be (see ShootMotion). Shared
        /// with SpeedBlurPlayCheck, which takes the same frames out of the
        /// running game's framebuffer.</summary>
        internal static System.Collections.Generic.List<(bool ok, string text)> MotionVerdicts(
            Color32[] off, Color32[] offBare, Color32[] offNoCar, Color32[] straight, Color32[] straightBare,
            Color32[] straightVel, Color32[] yawBare, Color32[] yawVel, Color32[] cutBare,
            Color32[] zoomBefore, Color32[] zoomAfter, int w, int h)
        {
            var verdicts = new System.Collections.Generic.List<(bool ok, string text)>();
            int n = w * h;

            // Calibrate the debug colour on the sky (blue 255, zero motion).
            var zero = new System.Collections.Generic.List<float>();
            var zeroGs = new System.Collections.Generic.List<float>();
            for (int i = 0; i < n; i++)
                if (straightVel[i].b == 255) { zero.Add(straightVel[i].r); zeroGs.Add(straightVel[i].g); }
            float z = Median(zero) / 255f, zg = Median(zeroGs) / 255f;
            velSrgb = false;
            bool linearOk = Mathf.Abs(z - 0.5f) < 0.012f;
            velSrgb = true;
            bool srgbOk = Mathf.Abs(Lin(z) - 0.5f) < 0.012f;
            velSrgb = !linearOk && srgbOk;
            zeroR = Lin(z);
            zeroG = Lin(zg);
            if (!linearOk && !srgbOk)
            {
                verdicts.Add((false, "THE MOTION READOUT DOES NOT DECODE: the still sky reads " + (z * 255f).ToString("0") +
                                     " (" + zero.Count + " px) — the sky is moving, or the pass never ran"));
                return verdicts;
            }

            int carN = 0, carSameS = 0, carSameY = 0, hudOff = 0, hudMot = 0, hudBoth = 0;
            int farN = 0, farStill = 0, farSame = 0, nearN = 0, cutSame = 0, zoomSame = 0;
            double farDiff = 0, nearDiff = 0, nearGradOff = 0, nearGradMot = 0;
            double gxOff = 0, gxYaw = 0, gyOff = 0, gyYaw = 0;
            var nearLen = new System.Collections.Generic.List<float>();
            var allLen = new System.Collections.Generic.List<float>();
            var sideVx = new System.Collections.Generic.List<float>();
            var sideVy = new System.Collections.Generic.List<float>();
            var skyTurn = new System.Collections.Generic.List<float>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    bool hO = Differs(off[i], offBare[i], 24), hM = Differs(straight[i], straightBare[i], 24);
                    if (hO) hudOff++;
                    if (hM) hudMot++;
                    if (hO && hM) hudBoth++;
                    if (!Differs(cutBare[i], offBare[i], 0)) cutSame++;
                    if (zoomBefore != null && zoomAfter != null && !Differs(zoomBefore[i], zoomAfter[i], 0)) zoomSame++;
                    if (Differs(offBare[i], offNoCar[i], 12))
                    {
                        carN++;
                        if (!Differs(straightBare[i], offBare[i], 0)) carSameS++;
                        if (!Differs(yawBare[i], offBare[i], 0)) carSameY++;
                        continue;
                    }
                    bool inner = x + 1 < w && y + 1 < h;

                    Vel(straightVel[i], out float vx, out float vy, out float depth);
                    float len = Mathf.Sqrt(vx * vx + vy * vy);
                    allLen.Add(len);
                    float d = Mathf.Abs(Luma(offBare[i]) - Luma(straightBare[i]));
                    if (depth >= FarMetres)
                    {
                        farN++;
                        // Within one step of the readout: an sRGB byte near
                        // 0.5 is ~1.5 px, so a far ridge's true 0.3 px reads 1.5.
                        if (len <= ReadoutStepPx) farStill++;
                        if (!Differs(straightBare[i], offBare[i], 0)) farSame++;
                        farDiff += d;
                    }
                    else if (depth <= NearMetres)
                    {
                        nearN++;
                        nearLen.Add(len);
                        nearDiff += d;
                        if (inner)
                        {
                            nearGradOff += Mathf.Abs(Luma(offBare[i]) - Luma(offBare[i + 1])) +
                                           Mathf.Abs(Luma(offBare[i]) - Luma(offBare[i + w]));
                            nearGradMot += Mathf.Abs(Luma(straightBare[i]) - Luma(straightBare[i + 1])) +
                                           Mathf.Abs(Luma(straightBare[i]) - Luma(straightBare[i + w]));
                        }
                    }

                    // The turn: the left and right fifths of the frame.
                    float fx = (x + 0.5f) / w;
                    if ((fx < 0.2f || fx > 0.8f) && inner)
                    {
                        Vel(yawVel[i], out float yx, out float yy, out float yd);
                        sideVx.Add(Mathf.Abs(yx));
                        sideVy.Add(Mathf.Abs(yy));
                        if (yd >= FarMetres) skyTurn.Add(Mathf.Sqrt(yx * yx + yy * yy));
                        gxOff += Mathf.Abs(Luma(offBare[i]) - Luma(offBare[i + 1]));
                        gxYaw += Mathf.Abs(Luma(yawBare[i]) - Luma(yawBare[i + 1]));
                        gyOff += Mathf.Abs(Luma(offBare[i]) - Luma(offBare[i + w]));
                        gyYaw += Mathf.Abs(Luma(yawBare[i]) - Luma(yawBare[i + w]));
                    }
                }

            string Pct(float px) => (px / w * 100f).ToString("0.0") + "% of the width";

            // The car and the HUD.
            float kept = carN > 0 ? Mathf.Min(carSameS, carSameY) / (float)carN : 0f;
            verdicts.Add(carN < 200
                ? (false, "no player's car found to measure (" + carN + " px)")
                : kept >= 0.995f
                    ? (true, "MOTION: the player's car is pixel-for-pixel untouched on the straight and the turn (" +
                             carN + " px, " + (kept * 100).ToString("0.0") + "%)")
                    : (false, "MOTION IS SMEARING THE PLAYER'S CAR: only " + (kept * 100).ToString("0.0") + "% of " + carN + " px kept"));
            float iou = Iou(hudOff, hudMot, hudBoth);
            verdicts.Add(hudOff < 40
                ? (false, "no HUD found to measure (" + hudOff + " px)")
                : iou >= 0.8f
                    ? (true, "MOTION: the HUD is untouched: " + (iou * 100).ToString("0") + "% the same footprint (" + hudOff + " px)")
                    : (false, "MOTION IS SMEARING THE HUD: only " + (iou * 100).ToString("0") + "% of its footprint survives"));

            // The straight: the far stays, the near goes.
            float farKept = farN > 0 ? farSame / (float)farN : 0f, farQuiet = farN > 0 ? farStill / (float)farN : 0f;
            verdicts.Add(farN < 500
                ? (false, "no sky or far land in the frame to measure (" + farN + " px)")
                : farKept >= 0.97f && farQuiet >= 0.98f
                    ? (true, "straight at " + MotionKmh.ToString("0") + " km/h: the sky and everything past " + FarMetres.ToString("0") +
                             " m is bit-for-bit sharp (" + (farKept * 100).ToString("0.0") + "% of " + farN + " px; " +
                             (farQuiet * 100).ToString("0.0") + "% read as still, within the readout's " + ReadoutStepPx + " px step)")
                    : (false, "THE SKY SMEARS ON A STRAIGHT: " + (farKept * 100).ToString("0.0") + "% of " + farN +
                              " far px kept, " + (farQuiet * 100).ToString("0.0") + "% still — the far plane is getting translation"));
            float nearMed = Median(nearLen), edge = Median(allLen, 0.98f);
            double nearMean = nearN > 0 ? nearDiff / nearN : 0, farMean = farN > 0 ? farDiff / farN : 0;
            double nearDetail = nearGradOff > 0 ? nearGradMot / nearGradOff : 1;
            verdicts.Add(nearN < 300
                ? (false, "nothing within " + NearMetres.ToString("0") + " m of the lens to measure (" + nearN + " px)")
                : nearMed >= 0.004f * w && nearDetail < 0.9 && nearMean > 4 * farMean
                    ? (true, "while everything within " + NearMetres.ToString("0") + " m smears: median " + nearMed.ToString("0.0") +
                             " px (" + Pct(nearMed) + "), the longest 2% " + Pct(edge) + "; " +
                             ((1 - nearDetail) * 100).ToString("0") + "% of its detail gone, mean change " +
                             (nearMean * 100).ToString("0.0") + "% vs " + (farMean * 100).ToString("0.00") + "% far")
                    : (false, "THE NEAR ROADSIDE DOES NOT SMEAR: median " + nearMed.ToString("0.0") + " px, detail kept " +
                              (nearDetail * 100).ToString("0") + "%, change " + (nearMean * 100).ToString("0.0") + "% vs " +
                              (farMean * 100).ToString("0.00") + "% far"));

            // The turn: sideways, sky and all.
            float sx = Median(sideVx), sy = Median(sideVy), sk = Median(skyTurn);
            double gxR = gxOff > 0 ? gxYaw / gxOff : 1, gyR = gyOff > 0 ? gyYaw / gyOff : 1;
            verdicts.Add(sideVx.Count < 1000
                ? (false, "no sides of the frame to measure")
                : sx >= 0.004f * w && sy < 0.35f * sx && (skyTurn.Count < 200 || sk >= 0.004f * w) && gxR < gyR - 0.05
                    ? (true, "turning " + MotionYawDeg.ToString("0.0") + " deg a frame: the sides smear SIDEWAYS — median " +
                             sx.ToString("0.0") + " px across vs " + sy.ToString("0.0") + " px up/down, the sky " + sk.ToString("0.0") +
                             " px; " + ((1 - gxR) * 100).ToString("0") + "% of the across detail gone vs " +
                             ((1 - gyR) * 100).ToString("0") + "% up/down")
                    : (false, "THE TURN DOES NOT SMEAR SIDEWAYS: across " + sx.ToString("0.0") + " px, up/down " + sy.ToString("0.0") +
                              " px, sky " + sk.ToString("0.0") + " px, detail kept across " + (gxR * 100).ToString("0") +
                              "% vs up/down " + (gyR * 100).ToString("0") + "%"));

            // The cut, and the zoom.
            verdicts.Add(cutSame == n
                ? (true, "a 300 m jump of the lens is a CUT: the frame is bit-for-bit the unblurred one")
                : (false, "A CAMERA CUT SMEARED: " + (n - cutSame) + " px changed"));
            if (zoomBefore != null && zoomAfter != null)
            verdicts.Add(zoomSame == n
                ? (true, "and ZOOM after all of it is bit-for-bit the ZOOM before")
                : (false, "MOTION LEFT STATE BEHIND: the ZOOM frame changed in " + (n - zoomSame) + " px"));
            return verdicts;
        }

        /// <summary>
        /// The owner's pictures (four, by his budget): a mountain stage at
        /// 110 km/h on a straight, ZOOM and MOTION, from the chase lens and
        /// from the cockpit. Blue Ridge, a third of the way down the road,
        /// the car in the right-hand lane. The cabin and the instruments are
        /// screen overlays in the game, drawn after the framebuffer is shown;
        /// here they ride the stacked HUD camera instead — after the blur, as
        /// in the game — and are rasterised at the shot's 480 lines.
        /// </summary>
        static int OwnerShots(string outDir)
        {
            const int W = 854, H = 480;
            const string Venue = "BlueRidge";
            string tag = "owner_" + Venue;
            var def = TrackCatalog.At(TrackCatalog.IndexOf(Venue));
            if (def.id != Venue) return Fail(tag, "no " + Venue + " in the catalog");
            if (!PSXScreenshotTool.Open(def, out var cam, out var player))
                return Fail(tag, "could not open " + Venue + " (run the scene build)");
            var blur = Wire(cam, player, tag);
            if (blur.hudCanvas == null) return Fail(tag, "SpeedBlur has no HUD canvas wired");

            // Down the road on the straightest stretch of its second quarter,
            // away from the parked field.
            var t = player.transform;
            var rm = Object.FindFirstObjectByType<RaceManager>();
            var path = rm != null ? rm.path : null;
            if (path != null && path.Count > 200 && path.curvatures != null && path.curvatures.Length == path.Count)
            {
                int grid = path.NearestIndex(t.position);
                float lift = t.position.y - path.GetPoint(grid).y;
                int at = path.Count / 4;
                float best = float.MaxValue;
                for (int i = path.Count / 4; i < path.Count / 2; i += 5)
                {
                    float c = path.MaxCurvatureAhead(i, 30);
                    if (c < best) { best = c; at = i; }
                }
                Vector3 tan = path.GetTangent(at);
                Vector3 flat = new Vector3(tan.x, 0f, tan.z).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, flat);
                t.SetPositionAndRotation(path.GetPoint(at) + right * (path.roadWidth * 0.25f) + Vector3.up * lift,
                                         Quaternion.LookRotation(tan, Vector3.up));
                Debug.Log("[SpeedBlur] " + tag + " note  waypoint " + at + " of " + path.Count +
                          ", curvature " + best.ToString("0.0000"));
            }

            // The HUD on; the cabin and the instruments onto the HUD camera;
            // every other canvas (touch panel, display) off.
            if (!blur.SetSplit(true)) return Fail(tag, "THE HUD COULD NOT BE SPLIT OFF");
            Wake.Clear();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (!c.isRootCanvas || c == blur.hudCanvas) continue;
                bool cabin = c.GetComponentInChildren<CockpitView>(true) != null ||
                             c.GetComponentInChildren<GaugeCluster>(true) != null;
                c.enabled = false;
                if (!cabin) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = blur.HudCamera;
                c.planeDistance = 1f;
                foreach (var tr in c.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = SpeedBlur.HudLayer;
                c.enabled = true;
                Wake.Add(c);
            }
            blur.MarkCar();
            cam.aspect = W / (float)H;
            float v = MotionKmh / 3.6f;
            int fails = 0;

            // The chase lens, at speed.
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
            foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None)) cv.PreviewShow(false);
            ChaseCamera.SteadyPose(ChaseCamera.View.Chase, W / (float)H, v, ChaseCamera.DefaultSpeedFullMps, t,
                                   ChaseCamera.FrameOf(player), default,
                                   out Vector3 pos, out Quaternion rot, out float fov, out _);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
            OntoHudLayer();
            fails += OwnerPair(cam, blur, W, H, v, t.forward, "owner_chase", outDir);

            // The cockpit.
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
            foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None)) cv.PreviewShow(true);
            var box = player.GetComponent<BoxCollider>();
            var shell = player.GetComponent<CarBody>();
            if (box == null) { Wake.Clear(); return fails + Fail(tag, "the player has no box to seat the cockpit lens in"); }
            pos = t.TransformPoint(ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, box.center, box.size,
                                                           shell != null ? shell.Def : null));
            rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
            cam.fieldOfView = ChaseCamera.ViewFOV(ChaseCamera.View.Cockpit, 58f);
            cam.nearClipPlane = ChaseCamera.ViewNearClip(ChaseCamera.View.Cockpit, cam.nearClipPlane);
            cam.transform.SetPositionAndRotation(pos, rot);
            OntoHudLayer();
            fails += OwnerPair(cam, blur, W, H, v, t.forward, "owner_cockpit", outDir);

            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            Wake.Clear();
            blur.SetSplit(false);
            cam.ResetAspect();
            return fails;
        }

        /// <summary>The woken canvases' whole trees onto the HUD camera's
        /// layer — AFTER the cabin and the cluster have built themselves, or
        /// what they just made is on the Default layer and drawn by nobody.</summary>
        static void OntoHudLayer()
        {
            foreach (var c in Wake)
                if (c != null)
                    foreach (var tr in c.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = SpeedBlur.HudLayer;
        }

        /// <summary>ZOOM and MOTION from one lens, written as the owner's
        /// pictures, and how much of each frame each one changed.</summary>
        static int OwnerPair(Camera cam, SpeedBlur blur, int w, int h, float v, Vector3 fwd, string name, string outDir)
        {
            var pos = cam.transform.position;
            var rot = cam.transform.rotation;
            var off = Render(cam, blur, w, h, 0f, blur.Heading(), null, null, write: false);
            var zoom = Render(cam, blur, w, h, v, blur.Heading(), name + "_zoom", outDir, write: true);
            var motion = RenderMotion(cam, blur, w, h, v, MotionBlurFx.ViewAt(pos - fwd * (v * MotionDt), rot),
                                      cam.projectionMatrix, false, name + "_motion", outDir, true);
            int zc = 0, mc = 0;
            for (int i = 0; i < off.Length; i++)
            {
                if (Differs(off[i], zoom[i], 2)) zc++;
                if (Differs(off[i], motion[i], 2)) mc++;
            }
            Ok("owner", name + ": ZOOM changes " + (zc * 100f / off.Length).ToString("0.0") + "% of the frame, MOTION " +
                        (mc * 100f / off.Length).ToString("0.0") + "% (by more than 2/255)");
            return 0;
        }

        /// <summary>The renderers the pass masks: everything under the car in
        /// the opaque queues. Its lamp glows, smoke and blob shadow are
        /// transparent, belong to the world, and smear with it.</summary>
        internal static System.Collections.Generic.List<Renderer> OpaqueRenderers(GameObject car)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in car.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && r.sharedMaterial != null && r.sharedMaterial.renderQueue <= 2500) list.Add(r);
            return list;
        }

        /// <summary>
        /// What a blurred frame has to be, judged from frames of ONE still
        /// scene: blur off and blur at the top of the table, each with the HUD
        /// and without it; the control for the HUD — the top of the table with
        /// the HUD left on the world camera; the frame with the player's car
        /// hidden, which says where the car is; and the control for THAT — the
        /// top of the table with the car untagged (may be null: the play check
        /// leaves the running game's tags alone). Radii are measured from
        /// <paramref name="focus"/>, where the tunnel points. Shared with
        /// SpeedBlurPlayCheck, which takes the same frames out of the running game.
        /// </summary>
        internal static System.Collections.Generic.List<(bool ok, string text)> Verdicts(
            Color32[] off, Color32[] max, Color32[] offBare, Color32[] maxBare, Color32[] control,
            Color32[] offNoCar, Color32[] maxUnmasked, int w, int h, Vector2 focus, float inner)
        {
            var verdicts = new System.Collections.Generic.List<(bool ok, string text)>();
            float aspect = w / (float)h;
            float corner = 0.5f * Mathf.Sqrt(aspect * aspect + 1f);
            int centreN = 0, centreChanged = 0, ringN = 0;
            double ringDiff = 0, lumaOff = 0, lumaMax = 0, gradOff = 0, gradMax = 0;
            int hudOff = 0, hudMax = 0, hudBoth = 0, hudCtl = 0, hudCtlBoth = 0;
            int carN = 0, carSame = 0, carSameUnmasked = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float dx = ((x + 0.5f) / w - focus.x) * aspect, dy = (y + 0.5f) / h - focus.y;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / corner;

                    // The car, outside the tunnel — inside it nothing smears
                    // and a car that is untouched there proves nothing.
                    if (offNoCar != null && r > inner && Differs(offBare[i], offNoCar[i], 12))
                    {
                        carN++;
                        if (!Differs(offBare[i], maxBare[i], 0)) carSame++;
                        if (maxUnmasked != null && !Differs(offBare[i], maxUnmasked[i], 0)) carSameUnmasked++;
                    }

                    bool isHudOff = Differs(off[i], offBare[i], 24);
                    bool isHudMax = Differs(max[i], maxBare[i], 24);
                    bool isHudCtl = Differs(control[i], maxBare[i], 24);
                    // Only where the smear is long enough to matter.
                    if (r > 0.5f)
                    {
                        if (isHudOff) hudOff++;
                        if (isHudMax) hudMax++;
                        if (isHudOff && isHudMax) hudBoth++;
                        if (isHudCtl) hudCtl++;
                        if (isHudOff && isHudCtl) hudCtlBoth++;
                    }

                    if (r < inner * 0.8f)
                    {
                        centreN++;
                        if (Differs(offBare[i], maxBare[i], 0)) centreChanged++;
                    }
                    else if (r > 0.6f && !isHudOff && !isHudMax && x + 1 < w)
                    {
                        ringN++;
                        ringDiff += Mathf.Abs(Luma(offBare[i]) - Luma(maxBare[i]));
                        lumaOff += Luma(offBare[i]);
                        lumaMax += Luma(maxBare[i]);
                        gradOff += Mathf.Abs(Luma(offBare[i]) - Luma(offBare[i + 1]));
                        gradMax += Mathf.Abs(Luma(maxBare[i]) - Luma(maxBare[i + 1]));
                    }
                }

            verdicts.Add(centreChanged == 0 && centreN > 20
                ? (true, "the tunnel is bit-for-bit untouched at 140 mph (" + centreN + " px round the heading)")
                : (false, "THE TUNNEL CHANGED: " + centreChanged + " of " + centreN +
                          " px — the road ahead is being resampled"));

            double meanDiff = ringN > 0 ? ringDiff / ringN : 0;
            verdicts.Add(meanDiff > 0.01
                ? (true, "the edge of the frame smears: mean change " + (meanDiff * 100).ToString("0.0") + "% of full scale")
                : (false, "NO BLUR AT THE EDGE (mean change " + (meanDiff * 100).ToString("0.00") +
                          "%) — the pass is not on this pipeline's renderer, or never ran"));

            double keep = lumaOff > 0 ? lumaMax / lumaOff : 0;
            verdicts.Add(keep > 0.9 && keep < 1.1
                ? (true, "and keeps its brightness (" + (keep * 100).ToString("0") + "%) — it is the world that is smeared")
                : (false, "THE EDGE CHANGED BRIGHTNESS to " + (keep * 100).ToString("0") +
                          "% — the pass is reading something other than the frame"));

            double detail = gradOff > 0 ? gradMax / gradOff : 1;
            verdicts.Add(detail < 0.8
                ? (true, "with " + ((1 - detail) * 100).ToString("0") + "% of its pixel-to-pixel detail gone — a blur, not noise")
                : (false, "THE EDGE KEPT " + (detail * 100).ToString("0") + "% OF ITS DETAIL — that is not a blur"));

            float iou = Iou(hudOff, hudMax, hudBoth);
            float iouCtl = Iou(hudOff, hudCtl, hudCtlBoth);
            if (hudOff < 40)
                verdicts.Add((false, "no HUD found in the outer frame to measure (" + hudOff + " px)"));
            else
                verdicts.Add(iou >= 0.8f
                    ? (true, "the HUD is untouched by it: " + (iou * 100).ToString("0") + "% the same footprint (" + hudOff + " px)")
                    : (false, "THE HUD IS BEING SMEARED: only " + (iou * 100).ToString("0") +
                              "% of its footprint survives full strength"));
            verdicts.Add(iouCtl < iou - 0.2f
                ? (true, "and the control agrees — left on the world camera the same HUD keeps " +
                         (iouCtl * 100).ToString("0") + "%, so the stacked camera is doing the work")
                : (false, "THE CONTROL DID NOT FAIL (" + (iouCtl * 100).ToString("0") + "% vs " +
                          (iou * 100).ToString("0") + "%) — this measurement cannot see a smeared HUD"));

            if (offNoCar != null)
            {
                float kept = carN > 0 ? carSame / (float)carN : 0f;
                if (carN < 200)
                    verdicts.Add((false, "no player's car found outside the tunnel to measure (" + carN + " px)"));
                else
                    verdicts.Add(kept >= 0.995f
                        ? (true, "the player's car is pixel-for-pixel untouched with the tunnel closed over it (" +
                                 carN + " px, " + (kept * 100).ToString("0.0") + "%)")
                        : (false, "THE PLAYER'S CAR IS BEING SMEARED: only " + (kept * 100).ToString("0.0") +
                                  "% of " + carN + " px survive 140 mph"));
                if (maxUnmasked != null && carN >= 200)
                {
                    float keptUnmasked = carSameUnmasked / (float)carN;
                    verdicts.Add(keptUnmasked < 0.9f
                        ? (true, "and the control agrees — untagged, the same car keeps " +
                                 (keptUnmasked * 100).ToString("0") + "%, so the mask is doing the work")
                        : (false, "THE CAR CONTROL DID NOT FAIL (" + (keptUnmasked * 100).ToString("0") +
                                  "% untagged) — this measurement cannot see a smeared car"));
                }
            }
            return verdicts;
        }

        static float Iou(int a, int b, int both)
        {
            int union = a + b - both;
            return union > 0 ? both / (float)union : 0f;
        }

        /// <summary>One frame of the PSX camera at a road speed, the tunnel
        /// pointed at <paramref name="focus"/>, raw. With
        /// <paramref name="write"/>, the same frame through PSX/Blit as a
        /// point-doubled PNG.</summary>
        /// <summary>Canvases besides the HUD that ride the HUD camera for a
        /// shot (the cockpit's cabin and cluster in the owner's pictures) and
        /// have to be woken like it. Empty for every measured frame.</summary>
        static readonly System.Collections.Generic.List<Canvas> Wake = new System.Collections.Generic.List<Canvas>();

        static Color32[] Render(Camera cam, SpeedBlur blur, int w, int h, float speedMps, Vector2 focus,
                                string name, string outDir, bool write, SpeedBlurMode mode = SpeedBlurMode.Zoom)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            // The HUD canvas sizes itself off ITS camera's target and stands
            // itself in front of ITS camera — and outside play mode it does
            // neither until it is woken, so a canvas left alone is laid out
            // for the editor window and parked where the lens was when the
            // scene was saved. Both cameras get the target first (in play,
            // PSXCameraOutput and SpeedBlur.LateUpdate keep them on one
            // framebuffer), then the canvas is switched off and on.
            var hudCam = blur.HudCamera;
            var keepTarget = cam.targetTexture;
            cam.targetTexture = rt;
            if (hudCam != null) hudCam.targetTexture = rt;
            if (blur.hudCanvas != null && blur.hudCanvas.enabled)
            {
                blur.hudCanvas.enabled = false;
                blur.hudCanvas.enabled = true;
            }
            foreach (var c in Wake)
                if (c != null && c.enabled) { c.enabled = false; c.enabled = true; }
            Canvas.ForceUpdateCanvases();
            SpeedBlur.Preview(cam, speedMps, focus, mode);

            // A render REQUEST hands the pipeline a list of one camera, and
            // Unity emits canvas geometry only for the cameras on the list it
            // was handed. In a real frame the stacked HUD camera is on that
            // list like any enabled camera; here it has to be asked for.
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
                px = Read(rt);
                if (write && name != null)
                {
                    var shown = PSXScreenshotTool.Dithered(rt);
                    var big = Doubled(Read(shown), w, h, h < 400 ? 2 : 1);
                    ShotSidecar.WritePng(Path.Combine(outDir, "speedblur_" + name + ".png"), big.EncodeToPNG());
                    Object.DestroyImmediate(big);
                    if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                }
            }
            else Debug.LogError("[SpeedBlur] RenderRequest unsupported");

            RenderPipelineManager.beginCameraRendering -= emitHud;
            SpeedBlur.Preview(null, 0f);
            cam.targetTexture = keepTarget;
            if (hudCam != null) hudCam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            return px ?? new Color32[w * h];
        }

        internal static Color32[] Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        /// <summary>Nearest-neighbour blow-up for the PNG only, and only while
        /// the framebuffer is small enough to need it.</summary>
        internal static Texture2D Doubled(Color32[] src, int w, int h, int k)
        {
            var big = new Texture2D(w * k, h * k, TextureFormat.RGB24, false);
            var dst = new Color32[w * h * k * k];
            for (int y = 0; y < h * k; y++)
                for (int x = 0; x < w * k; x++)
                    dst[y * w * k + x] = src[(y / k) * w + x / k];
            big.SetPixels32(dst);
            big.Apply();
            return big;
        }

        static bool Differs(Color32 a, Color32 b, int tol) =>
            Mathf.Abs(a.r - b.r) > tol || Mathf.Abs(a.g - b.g) > tol || Mathf.Abs(a.b - b.b) > tol;

        static float Luma(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        static void Ok(string tag, string what) => Debug.Log("[SpeedBlur] " + tag + " ok   " + what);

        static int Fail(string tag, string what)
        {
            Debug.LogError("[SpeedBlur] " + tag + " FAIL " + what);
            return 1;
        }
    }
}
