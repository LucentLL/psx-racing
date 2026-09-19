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
            }
            finally
            {
                SpeedBlur.Preview(null, 0f);
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

            // The far chase view, reconstructed the way CaptureCameras does.
            var t = player.transform;
            var box = player.GetComponent<BoxCollider>();
            Vector3 size = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            Vector3 fwd = t.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            float fit = Mathf.Clamp(size.z / 4.1f, 0.9f, 1.3f);
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            ChaseCamera.ChaseParams(ChaseCamera.View.Chase, out float dm, out float hm, out float lm);
            Vector3 pos = t.position - fwd * (5.4f * fit * dm) + Vector3.up * (1.8f * hm);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(
                t.position + Vector3.up * (0.9f * lm) + fwd * 1.5f - pos, Vector3.up));
            cam.fieldOfView = ChaseCamera.ViewFOV(ChaseCamera.View.Chase, 58f);

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
        static Color32[] Render(Camera cam, SpeedBlur blur, int w, int h, float speedMps, Vector2 focus,
                                string name, string outDir, bool write)
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
            Canvas.ForceUpdateCanvases();
            SpeedBlur.Preview(cam, speedMps, focus);

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
                    File.WriteAllBytes(Path.Combine(outDir, "speedblur_" + name + ".png"), big.EncodeToPNG());
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
