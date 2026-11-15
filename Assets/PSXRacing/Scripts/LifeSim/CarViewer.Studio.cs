using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE SHOWROOM STAGE (owner, 2026-11-15: "I want car viewing and car
    /// selection to look like the early Gran Turismo games, with the car
    /// rotating (or able to be rotated by player)").
    ///
    /// The same rig, the same car, a different photograph. Where the classic
    /// turntable draws a 320 x 200 point-filtered frame for a small panel,
    /// the showroom draws the car big on a dark stage at the PS3-PS4 target:
    ///   * at the panel's own pixel size (capped at 1920 wide), drawn at twice
    ///     that and filtered down (the pipeline assets have MSAA off, so the
    ///     anti-aliasing is supersampled here), bilinear on screen;
    ///   * lit like a studio: a key light high in front, the fill as the
    ///     ambient, and an environment of softboxes (an overhead box and two
    ///     tall strips behind the car) in the lacquer through the paint
    ///     shader's own world-reflection path (_PSXEnvPano), which gives the
    ///     rim and the shoulder line as the car turns;
    ///   * on a floor: a soft pool of light under the car, a faint glow behind
    ///     it, and a contact shadow.
    /// Whatever HD car maps the car has (CarHD) come with it: they ride on the
    /// shared livery materials the classic frame uses too.
    ///
    /// It turns on its own at about 18 degrees a second; a drag (mouse or
    /// touch) turns it with a little inertia, the right stick and , . turn it
    /// (LifeHomeScreen), and the idle spin eases back in three seconds after
    /// the last input.
    ///
    /// The classic path is untouched: AttachTo, Snapshot and the inspection
    /// poses leave the showroom (LeaveStudio) and draw exactly what they drew.
    /// Switch: PSX_SHOWROOM=0 (Showroom.On) keeps every page on the classic one.
    /// </summary>
    public partial class CarViewer
    {
        public const float AutoSpinDegPerSec = 18f;
        public const float ResumeAfterSec = 3f;
        /// <summary>A long lens, low: a product photograph rather than a
        /// wide-angle close-up, which would bulge the near corner.</summary>
        const float StudioFov = 24f, StudioPitch = 8f;
        const int MaxStudioW = 1920;

        bool studio, modeStudio;
        RenderTexture studioRt, studioSs;
        RawImage studioImage;
        MeshRenderer floorPool, backGlow, softShadow;
        Material floorMat, glowMat, softShadowMat;
        float autoAt, spinNow = AutoSpinDegPerSec, turnVel, clock;
        bool turning;

        /// <summary>Showing the showroom stage (not the classic panel).</summary>
        public bool Studio => studio;
        public RenderTexture StudioTexture => studioRt;
        /// <summary>The turntable's angle, for the probes.</summary>
        public float Yaw => yaw;
        /// <summary>A drag across the whole stage is about one turn.</summary>
        public float StudioDegPerPixel => 300f / Mathf.Max(400f, studioRt != null ? studioRt.width : 1280f);

        /// <summary>
        /// Put the showroom stage in a rect: the picture fills it, dragging
        /// turns the car (CarViewerDrag). The car is whatever Show / ShowOwned
        /// put on the turntable.
        /// </summary>
        public RawImage AttachStudio(RectTransform panel)
        {
            EnsureRig();
            // The angle carries over a rebuild of the same page (a button
            // pressed under the stage); a browse to another car starts it at
            // the showroom angle (ResetStudioAngle), and so does a car first
            // shown after an inspection pose (SetPose).
            studio = true;
            turning = false;
            SetPose(ViewPose.Showroom);
            var go = new GameObject("ShowroomView");
            go.transform.SetParent(panel, false);
            var raw = go.AddComponent<RawImage>();
            var rrt = raw.rectTransform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            go.AddComponent<CarViewerDrag>().viewer = this;
            studioImage = raw;
            SetVisible(true);
            EnsureStudioTargets();
            raw.texture = studioRt;
            RenderNow();
            return raw;
        }

        /// <summary>Back to the classic turntable (a page without a showroom).</summary>
        public void LeaveStudio()
        {
            if (!studio) return;
            studio = false;
            studioImage = null;
            turning = false;
            if (rig != null) ApplyMode(false);
        }

        /// <summary>A new car on the stage: start it at the showroom angle.</summary>
        public void ResetStudioAngle()
        {
            yaw = 215f;
            turnVel = 0f;
        }

        // ------------------------------------------------------------------
        //  Turning
        // ------------------------------------------------------------------

        /// <summary>The showroom's clock: idle spin, inertia, the 3 s wait.
        /// Update calls it with real time; the probes call it with their own.</summary>
        public void Tick(float now, float dt)
        {
            clock = now;
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            if (!turning && turnVel != 0f)
            {
                yaw += turnVel * dt;
                turnVel *= Mathf.Exp(-5f * dt);
                if (Mathf.Abs(turnVel) < 1f) turnVel = 0f;
            }
            bool idle = !turning && now >= autoAt;
            // Eased back in over about a second rather than snapping on.
            spinNow = Mathf.MoveTowards(spinNow, idle ? AutoSpinDegPerSec : 0f, AutoSpinDegPerSec * dt);
            yaw += spinNow * dt + dragYaw;
            dragYaw = 0f;
            if (rig != null) rig.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void HoldAutoSpin()
        {
            autoAt = clock + ResumeAfterSec;
            spinNow = 0f;
            turnVel = 0f;
        }

        public void BeginTurn()
        {
            turning = true;
            HoldAutoSpin();
        }

        /// <summary>One drag step, in degrees. The release speed is what
        /// carries on as inertia.</summary>
        public void TurnBy(float degrees)
        {
            dragYaw += degrees;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 1f / 240f, 0.1f);
            turnVel = Mathf.Lerp(turnVel, degrees / dt, 0.4f);
            autoAt = clock + ResumeAfterSec;
            spinNow = 0f;
        }

        public void EndTurn()
        {
            turning = false;
            // A little inertia: a flick coasts a few tens of degrees, no more.
            turnVel = Mathf.Clamp(turnVel * 0.35f, -180f, 180f);
            autoAt = clock + ResumeAfterSec;
        }

        // ------------------------------------------------------------------
        //  The frame
        // ------------------------------------------------------------------

        static readonly int LightDirId = Shader.PropertyToID("_PSXLightDir");
        static readonly int LightColorId = Shader.PropertyToID("_PSXLightColor");
        static readonly int AmbientId = Shader.PropertyToID("_PSXAmbient");
        static readonly int SkyAmbientId = Shader.PropertyToID("_PSXSkyAmbient");
        static readonly int FogColorId = Shader.PropertyToID("_PSXFogColor");
        static readonly int PaintWorldId = Shader.PropertyToID("_PSXPaintWorld");
        static readonly int EnvPanoId = Shader.PropertyToID("_PSXEnvPano");
        static readonly int EnvOnId = Shader.PropertyToID("_PSXEnvOn");
        static readonly int EnvLodBiasId = Shader.PropertyToID("_PSXEnvLodBias");
        static readonly int EnvEyeId = Shader.PropertyToID("_PSXEnvEye");

        /// <summary>Supersampling: twice the panel's size on a PC (and in the
        /// editor's pictures), one and a half on a handheld.</summary>
        static float Supersample => PlatformProfile.PC || Application.isEditor ? 2f : 1.5f;

        void RenderStudio()
        {
            EnsureStudioTargets();
            if (studioSs == null || studioRt == null) return;
            EnsureStudioRig();
            ApplyMode(true);
            rig.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float aspect = (float)studioRt.width / Mathf.Max(1, studioRt.height);
            cam.orthographic = false;
            cam.fieldOfView = StudioFov;
            cam.aspect = aspect;
            PlaceStudioCamera(aspect);

            // The viewer's own lighting first (what the classic frame uses),
            // then the studio over it for this one render, then put back:
            // the world-reflection globals are CarReflections' in a race, and
            // a studio left in them would be in every car's lacquer next scene.
            if (globals != null) globals.Apply();
            var envTex = Shader.GetGlobalTexture(EnvPanoId);
            float envOn = Shader.GetGlobalFloat(EnvOnId), envBias = Shader.GetGlobalFloat(EnvLodBiasId);
            var envEye = Shader.GetGlobalVector(EnvEyeId);
            var pano = StudioPano;
            Shader.SetGlobalVector(LightDirId, new Vector4(-0.52f, 0.74f, -0.43f, 0f).normalized);
            Shader.SetGlobalColor(LightColorId, new Color(1.16f, 1.11f, 1.03f, 1f));
            Shader.SetGlobalColor(AmbientId, new Color(0.31f, 0.31f, 0.33f, 1f));
            Shader.SetGlobalColor(SkyAmbientId, new Color(0.50f, 0.50f, 0.53f, 1f));
            Shader.SetGlobalColor(FogColorId, new Color(0.035f, 0.035f, 0.04f, 1f));
            Shader.SetGlobalVector(PaintWorldId, new Vector4(1f, 0f, 0f, 0f));
            Shader.SetGlobalTexture(EnvPanoId, pano);
            Shader.SetGlobalFloat(EnvOnId, 1f);
            Shader.SetGlobalFloat(EnvLodBiasId, Mathf.Log(pano.width / 512f, 2f));
            Shader.SetGlobalVector(EnvEyeId, Vector4.zero);

            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = studioSs };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            else
            {
                var keep = cam.targetTexture;
                cam.targetTexture = studioSs;
                cam.Render();
                cam.targetTexture = keep;
            }
            Graphics.Blit(studioSs, studioRt);

            Shader.SetGlobalTexture(EnvPanoId, envTex);
            Shader.SetGlobalFloat(EnvOnId, envOn);
            Shader.SetGlobalFloat(EnvLodBiasId, envBias);
            Shader.SetGlobalVector(EnvEyeId, envEye);
            if (globals != null) globals.Apply();
            if (studioImage != null && studioImage.texture != studioRt) studioImage.texture = studioRt;
        }

        /// <summary>The panel's size in screen pixels: its resolved rect where
        /// the layout has run, else the size it was given (the frame it was
        /// built in), times the canvas scale.</summary>
        void EnsureStudioTargets()
        {
            Vector2 px = new Vector2(1280f, 520f);
            if (studioImage != null)
            {
                var c = studioImage.canvas;
                float s = c != null ? c.rootCanvas.scaleFactor : 1f;
                var r = studioImage.rectTransform.rect;
                if (r.width > 4f && r.height > 4f) px = r.size * s;
                else
                {
                    var p = studioImage.rectTransform.parent as RectTransform;
                    if (p != null && p.sizeDelta.x > 4f && p.sizeDelta.y > 4f) px = p.sizeDelta * s;
                }
            }
            float k = px.x > MaxStudioW ? MaxStudioW / px.x : 1f;
            int w = Mathf.Max(64, Mathf.RoundToInt(px.x * k));
            int h = Mathf.Max(32, Mathf.RoundToInt(px.y * k));
            if (studioRt != null && studioRt.width == w && studioRt.height == h) return;
            ReleaseStudioTargets();
            studioRt = new RenderTexture(w, h, 0, RenderTextureFormat.Default)
            {
                name = "ShowroomRT", filterMode = FilterMode.Bilinear, antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp,
            };
            studioRt.Create();
            float ss = Supersample;
            int sw = Mathf.Min(Mathf.RoundToInt(w * ss), 4096), sh = Mathf.Min(Mathf.RoundToInt(h * ss), 4096);
            studioSs = new RenderTexture(sw, sh, 24, RenderTextureFormat.Default)
            {
                name = "ShowroomSS", filterMode = FilterMode.Bilinear, antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp,
            };
            studioSs.Create();
            if (studioImage != null) studioImage.texture = studioRt;
        }

        void ReleaseStudioTargets()
        {
            foreach (var t in new[] { studioRt, studioSs })
            {
                if (t == null) continue;
                t.Release();
                if (Application.isPlaying) Destroy(t); else DestroyImmediate(t);
            }
            studioRt = null;
            studioSs = null;
        }

        /// <summary>Framed so the car fits the stage at every angle of the
        /// turn: its plan diagonal across, its height (plus what the downward
        /// look adds) up, from a little above the roof line.</summary>
        void PlaceStudioCamera(float aspect)
        {
            float vf = StudioFov * Mathf.Deg2Rad * 0.5f;
            float hf = Mathf.Atan(Mathf.Tan(vf) * aspect);
            float pitch = StudioPitch * Mathf.Deg2Rad;
            float halfW = 0.5f * Mathf.Sqrt(carLength * carLength + carWidth * carWidth);
            float halfH = 0.5f * (carHeight * Mathf.Cos(pitch) + 2f * halfW * Mathf.Sin(pitch));
            float d = Mathf.Max(halfW / Mathf.Sin(hf), halfH / Mathf.Tan(vf) + halfW * 0.5f) * 1.08f;
            var focus = rig.position + Vector3.up * (carHeight * 0.5f);
            cam.transform.position = focus + Quaternion.Euler(StudioPitch, 0f, 0f) * new Vector3(0f, 0f, -d);
            cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, Vector3.up);
            cam.farClipPlane = Mathf.Max(60f, d * 4f);

            float pool = halfW * 1.6f;
            if (floorPool != null) floorPool.transform.localScale = new Vector3(pool, 1f, pool);
            if (backGlow != null)
            {
                backGlow.transform.localPosition = new Vector3(0f, carHeight * 0.55f, halfW * 3.2f);
                backGlow.transform.localScale = new Vector3(halfW * 3.4f, 1f, halfW * 1.5f);
            }
            if (softShadow != null && shadow != null)
                softShadow.transform.localScale = new Vector3(shadow.localScale.x * 1.35f, shadow.localScale.y * 1.22f, 1f);
        }

        /// <summary>The studio's floor, glow and second shadow, made the
        /// first time a showroom opens (a classic-only session never has them).</summary>
        void EnsureStudioRig()
        {
            if (floorPool != null || rig == null) return;
            var root = rig.parent;
            var ui = Shader.Find("UI/Default");
            if (ui != null)
            {
                // Drawn first (queue 1000, before the car), no depth write: the
                // car simply draws over the floor, whatever the depth test is.
                floorMat = new Material(ui) { name = "ShowroomFloor", color = new Color(0.30f, 0.30f, 0.32f, 0.92f), renderQueue = 1000 };
                glowMat = new Material(ui) { name = "ShowroomGlow", color = new Color(0.085f, 0.085f, 0.095f, 1f), renderQueue = 999 };
                var f = new GameObject("ShowroomFloor");
                f.transform.SetParent(root, false);
                f.transform.localPosition = new Vector3(0f, 0.004f, 0f);
                f.AddComponent<MeshFilter>().sharedMesh = DiscMesh;
                floorPool = f.AddComponent<MeshRenderer>();
                floorPool.sharedMaterial = floorMat;
                var g = new GameObject("ShowroomGlow");
                g.transform.SetParent(root, false);
                g.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                g.AddComponent<MeshFilter>().sharedMesh = DiscMesh;
                backGlow = g.AddComponent<MeshRenderer>();
                backGlow.sharedMaterial = glowMat;
            }
            // A second, wider and fainter blob under the contact one: the soft
            // occlusion a car on a lit floor has round it.
            if (ShadowMat != null && shadow != null)
            {
                softShadowMat = new Material(ShadowMat) { name = "ShowroomSoftShadow" };
                if (softShadowMat.HasProperty("_Strength")) softShadowMat.SetFloat("_Strength", 0.5f);
                var s = new GameObject("ShowroomSoftShadow");
                s.transform.SetParent(rig, false);
                s.transform.localPosition = new Vector3(0f, 0.008f, 0f);
                s.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                s.AddComponent<MeshFilter>().sharedMesh = ShadowQuad;
                softShadow = s.AddComponent<MeshRenderer>();
                softShadow.sharedMaterial = softShadowMat;
            }
            foreach (var r in new Renderer[] { floorPool, backGlow, softShadow })
                if (r != null)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.enabled = false;
                }
            modeStudio = false;
        }

        /// <summary>Switch the rig between the two photographs (only when it
        /// changes, so the classic frame pays nothing).</summary>
        void ApplyMode(bool s)
        {
            if (modeStudio == s) return;
            modeStudio = s;
            foreach (var r in new Renderer[] { floorPool, backGlow, softShadow })
                if (r != null) r.enabled = s;
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = s ? new Color(0.016f, 0.016f, 0.02f, 1f) : new Color(0.14f, 0.13f, 0.21f, 1f);
            if (!s)
            {
                cam.fieldOfView = 34f;
                cam.farClipPlane = 60f;
                if (pose == ViewPose.Showroom) { cam.ResetAspect(); PlaceCamera(); }
                else cam.aspect = poseAspect;
            }
        }

        // ------------------------------------------------------------------
        //  Generated assets
        // ------------------------------------------------------------------

        static Mesh discMesh;
        /// <summary>A unit disc in the XZ plane, white with its alpha falling
        /// from the middle to nothing at the rim: the pool of light, and the
        /// glow behind the car.</summary>
        static Mesh DiscMesh
        {
            get
            {
                if (discMesh != null) return discMesh;
                const int Seg = 64;
                float[] ring = { 0f, 0.22f, 0.42f, 0.60f, 0.76f, 0.89f, 1f };
                float[] alpha = { 1f, 0.94f, 0.76f, 0.50f, 0.26f, 0.08f, 0f };
                var v = new System.Collections.Generic.List<Vector3>();
                var c = new System.Collections.Generic.List<Color>();
                var t = new System.Collections.Generic.List<int>();
                v.Add(Vector3.zero); c.Add(new Color(1f, 1f, 1f, alpha[0]));
                for (int r = 1; r < ring.Length; r++)
                    for (int i = 0; i < Seg; i++)
                    {
                        float a = i * Mathf.PI * 2f / Seg;
                        v.Add(new Vector3(Mathf.Cos(a) * ring[r], 0f, Mathf.Sin(a) * ring[r]));
                        c.Add(new Color(1f, 1f, 1f, alpha[r]));
                    }
                for (int i = 0; i < Seg; i++) { t.Add(0); t.Add(1 + (i + 1) % Seg); t.Add(1 + i); }
                for (int r = 1; r < ring.Length - 1; r++)
                {
                    int a0 = 1 + (r - 1) * Seg, b0 = 1 + r * Seg;
                    for (int i = 0; i < Seg; i++)
                    {
                        int i1 = (i + 1) % Seg;
                        t.Add(a0 + i); t.Add(a0 + i1); t.Add(b0 + i1);
                        t.Add(a0 + i); t.Add(b0 + i1); t.Add(b0 + i);
                    }
                }
                discMesh = new Mesh { name = "ShowroomDisc" };
                discMesh.SetVertices(v);
                discMesh.SetColors(c);
                discMesh.SetTriangles(t, 0);
                discMesh.RecalculateNormals();
                discMesh.RecalculateBounds();
                return discMesh;
            }
        }

        static Texture2D studioPano;
        /// <summary>
        /// The studio as the lacquer sees it, laid out like the world
        /// panorama CarReflections draws (u = atan2(z, x), v = asin(y);
        /// world-aligned, the camera looking down +Z): near black, a big soft
        /// box overhead, two tall strip lights behind the car either side (the
        /// rim down a flank as it turns past them), a dim panel behind the
        /// camera, and the floor's pool below. Mipped: the paint reads it
        /// blurred, the glass sharper.
        /// </summary>
        static Texture2D StudioPano
        {
            get
            {
                if (studioPano != null) return studioPano;
                const int W = 512, H = 256;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, true)
                {
                    name = "ShowroomPano", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                };
                var px = new Color32[W * H];
                var stripL = new Vector2(-0.72f, 0.69f).normalized;
                var stripR = new Vector2(0.72f, 0.69f).normalized;
                var box = new Vector3(0f, 1f, -0.22f).normalized;
                var front = new Vector3(0f, 0.18f, -1f).normalized;
                for (int y = 0; y < H; y++)
                {
                    float el = ((y + 0.5f) / H - 0.5f) * Mathf.PI;
                    float cy = Mathf.Cos(el), sy = Mathf.Sin(el);
                    for (int x = 0; x < W; x++)
                    {
                        float az = ((x + 0.5f) / W - 0.5f) * Mathf.PI * 2f;
                        var R = new Vector3(cy * Mathf.Cos(az), sy, cy * Mathf.Sin(az));
                        float c = 0.022f;
                        // the floor: dark at the horizon, the pool's grey straight down
                        if (R.y < 0f) c = Mathf.Lerp(0.03f, 0.13f, Mathf.SmoothStep(0f, 1f, -R.y * 1.4f));
                        // the overhead softbox
                        float boxA = Vector3.Angle(R, box);
                        c = Mathf.Max(c, 0.92f * (1f - Mathf.SmoothStep(26f, 40f, boxA)));
                        // the strips: narrow in heading, from the floor to well up
                        var hz = new Vector2(R.x, R.z);
                        if (hz.sqrMagnitude > 1e-4f)
                        {
                            hz.Normalize();
                            float aL = Vector2.Angle(hz, stripL), aR = Vector2.Angle(hz, stripR);
                            float across = 1f - Mathf.SmoothStep(3.5f, 7f, Mathf.Min(aL, aR));
                            float up = Mathf.SmoothStep(-0.06f, 0.02f, R.y) * (1f - Mathf.SmoothStep(0.55f, 0.72f, R.y));
                            c = Mathf.Max(c, 0.98f * across * up);
                        }
                        // a dim panel behind the camera
                        float fA = Vector3.Angle(R, front);
                        c = Mathf.Max(c, 0.16f * (1f - Mathf.SmoothStep(18f, 34f, fA)));
                        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255);
                        px[y * W + x] = new Color32(b, b, (byte)Mathf.Min(255, b + 2), 255);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply(true, false);
                studioPano = tex;
                return studioPano;
            }
        }

        /// <summary>The turn on the pad and the keyboard, wherever a turntable
        /// is showing: the RIGHT stick (the left one and the d-pad are the
        /// menu's cursor) and , . (the keys with the arrows printed on them).
        /// -1..1, positive = turn right.</summary>
        public static float TurnInput()
        {
            float turn = 0f;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (pad != null) turn += pad.rightStick.ReadValue().x;
            if (kb != null)
            {
                if (kb.commaKey.isPressed) turn -= 1f;
                if (kb.periodKey.isPressed) turn += 1f;
            }
            return Mathf.Abs(turn) > 0.15f ? Mathf.Clamp(turn, -1f, 1f) : 0f;
        }
    }
}
