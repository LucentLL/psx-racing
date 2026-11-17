using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing
{
    /// <summary>
    /// RAIN 2's one live object (see <see cref="Rain2"/>): every frame it tells
    /// the shaders how hard it is raining and how much water stands, and while
    /// rain falls it keeps THE GROUND ROUND THE CAMERA:
    ///
    /// PROBES. A few rays a frame (six on a PC, two on a phone) come straight
    /// down on random points round where the camera is looking, and from each
    /// landing point one goes straight up. That gives, for nothing baked:
    /// <list type="bullet">
    /// <item>THE COVER MASK: a 64 x 64 m world-aligned grid, one metre a cell,
    /// of where a deck, a roof or a tunnel bore stands between the ground and
    /// the sky. The wet road's rings read it (PSXRain2.cginc), so no drop
    /// lands under a bridge. It wraps (cell = world metre mod 64): moving the
    /// camera moves nothing, a cell's old answer is dropped when the window
    /// has left it.</item>
    /// <item>THE ANCHORS: the last 128 open-sky points of road. Every drop
    /// that is DRAWN landing - a small crown thrown up, a ring spreading on
    /// the surface - is born on one of them, so splashes sit on the road's
    /// own height and slope, bridges and banks included.</item>
    /// </list>
    ///
    /// THE SPLASHES are two pooled particle systems (crowns: billboards;
    /// rings: quads laid on the surface), drawn with PSX/Rain so the lamps and
    /// beams light them and a night's are invisible outside the light. Their
    /// rate follows the rain's own intensity and falls away with the camera's
    /// speed: nobody sees a 6 cm crown at 100 mph, and nothing is spent on it.
    ///
    /// Nothing is allocated per frame; with no rain falling the whole thing is
    /// one global vector a frame.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public class Rain2Driver : MonoBehaviour
    {
        static Rain2Driver instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            // (no hide flags: a HideAndDontSave object outlives play mode in the editor)
            var go = new GameObject("Rain2");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Rain2Driver>();
            if (Rain2.Audio) go.AddComponent<Rain2Audio>();
        }

        // ---- per tier -------------------------------------------------------
        static int ProbesFor(Rain2.Tier t) => t == Rain2.Tier.High ? 6 : t == Rain2.Tier.Low ? 4 : 2;
        static float CrownRateFor(Rain2.Tier t) => t == Rain2.Tier.High ? 700f : t == Rain2.Tier.Low ? 350f : 150f;
        static float RingRateFor(Rain2.Tier t) => t == Rain2.Tier.High ? 450f : t == Rain2.Tier.Low ? 220f : 0f;
        const int CrownCap = 256, RingCap = 256;

        /// <summary>Probe disc: its radius, how far ahead of the lens its
        /// centre sits, and how many seconds of the camera's motion lead it.</summary>
        const float ProbeRadius = 15f, ProbeAhead = 6f, ProbeLead = 0.3f;
        /// <summary>Camera speed (m/s) where the drawn splashes start to thin, and where they stop.</summary>
        const float SplashFadeFrom = 10f, SplashFadeTo = 26f;
        /// <summary>An anchor further than this from the lens is not used.</summary>
        const float AnchorReach = 26f;
        const int GroundMask = (1 << 0) | (1 << 8) | (1 << 9);     // Default, Road, Solid
        const int RoadLayer = 8;

        // ---- the cover mask -------------------------------------------------
        const int MaskN = 64;
        const float CellM = 1f;
        readonly byte[] mask = new byte[MaskN * MaskN];
        readonly int[] stampX = new int[MaskN * MaskN], stampZ = new int[MaskN * MaskN];
        readonly bool[] known = new bool[MaskN * MaskN];
        Texture2D maskTex;
        bool maskDirty;
        float maskNextUpload;

        // ---- the anchors ----------------------------------------------------
        const int AnchorN = 128;
        readonly Vector3[] anchorP = new Vector3[AnchorN], anchorNrm = new Vector3[AnchorN];
        int anchorCount, anchorNext;

        ParticleSystem crowns, rings;
        Material crownMat, ringMat;
        Texture2D crownTex, ringTex;
        Mesh ringMesh;
        float crownOwed, ringOwed;

        float rain, level;
        bool snap = true, wasOn;
        Vector3 camVel, lastCam;
        bool haveLastCam;
        static readonly int WetWaterId = Shader.PropertyToID("_PSXWetWater");

        /// <summary>For the checks: live splash particles, anchors held, cells
        /// of the mask that say "covered".</summary>
        public static int LiveSplashes => instance == null ? 0 :
            (instance.crowns != null ? instance.crowns.particleCount : 0) + (instance.rings != null ? instance.rings.particleCount : 0);
        public static int Anchors => instance == null ? 0 : instance.anchorCount;
        public static int CoveredCells
        {
            get
            {
                if (instance == null) return 0;
                int n = 0;
                for (int i = 0; i < instance.mask.Length; i++) if (instance.known[i] && instance.mask[i] < 128) n++;
                return n;
            }
        }

        void OnEnable() { SceneManager.sceneLoaded += OnScene; }
        void OnDisable() { SceneManager.sceneLoaded -= OnScene; }
        void OnScene(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) { snap = true; ClearGround(); } }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            Kill(crownMat); Kill(ringMat); Kill(crownTex); Kill(ringTex); Kill(maskTex); Kill(ringMesh);
            if (crowns != null) Kill(crowns.gameObject);
            if (rings != null) Kill(rings.gameObject);
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void ClearGround()
        {
            anchorCount = 0; anchorNext = 0;
            for (int i = 0; i < mask.Length; i++) { mask[i] = 255; known[i] = false; }
            maskDirty = true;
            haveLastCam = false;
            camVel = Vector3.zero;
            if (crowns != null) crowns.Clear();
            if (rings != null) rings.Clear();
        }

        void LateUpdate()
        {
            if (!Rain2.Measure) { Step(); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp(), a0 = System.GC.GetAllocatedBytesForCurrentThread();
            Step();
            long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            Rain2.DriverTicks += dt;
            if (dt > Rain2.DriverMaxTicks) Rain2.DriverMaxTicks = dt;
            Rain2.DriverAlloc += System.GC.GetAllocatedBytesForCurrentThread() - a0;
            Rain2.DriverFrames++;
        }

        void Step()
        {
            if (!Rain2.On)
            {
                if (wasOn) { wasOn = false; Rain2.Push(0f, 0f); Rain2.PushCover(null, 1f); ClearGround(); }
                return;
            }
            wasOn = true;
            float dt = Time.deltaTime;

            // 1 - the two numbers
            var wn = Seasons.Now;
            bool raining = WeatherFx.Raining;
            float rainT = raining ? Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(wn.precipRate)) : 0f;
            float levelT = Rain2.LevelFor(raining, wn.precipRate, Shader.GetGlobalFloat(WetWaterId));
            if (snap) { rain = rainT; level = levelT; snap = false; }
            else
            {
                rain = Mathf.MoveTowards(rain, rainT, dt * 0.5f);
                level = Mathf.MoveTowards(level, levelT, dt * 0.25f);
            }
            Rain2.Push(rain, level);

            // 2 - the ground round the camera, while it rains
            var cam = Camera.main;
            if (rain <= 0.01f || cam == null || (!Rain2.Rings && !Rain2.Splashes))
            {
                if (maskTex != null && rain <= 0.01f) Rain2.PushCover(null, 1f);
                return;
            }
            var tier = Rain2.CurrentTier;
            Vector3 eye = cam.transform.position;
            if (haveLastCam && dt > 1e-4f)
            {
                Vector3 v = (eye - lastCam) / dt;
                v.y = 0f;
                // a cut (respawn, replay camera): start again where the lens now is
                if (v.sqrMagnitude > 120f * 120f) { camVel = Vector3.zero; anchorCount = 0; anchorNext = 0; }
                else camVel = Vector3.Lerp(camVel, v, 1f - Mathf.Exp(-6f * dt));
            }
            lastCam = eye; haveLastCam = true;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 centre = eye + fwd * ProbeAhead + camVel * ProbeLead;

            int probes = ProbesFor(tier);
            for (int i = 0; i < probes; i++) Probe(centre, eye.y);
            UploadMask(eye);

            // 3 - the drops that are drawn landing
            if (!Rain2.Splashes || dt <= 0f) return;
            float speed = camVel.magnitude;
            float k = rain * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SplashFadeFrom, SplashFadeTo, speed)));
            if (k <= 0.01f || anchorCount == 0) { crownOwed = 0f; ringOwed = 0f; return; }
            if (crowns == null && !BuildSplashes()) return;
            crownOwed = Mathf.Min(crownOwed + CrownRateFor(tier) * k * dt, 40f);
            ringOwed = Mathf.Min(ringOwed + RingRateFor(tier) * k * dt, 30f);
            int nc = Mathf.FloorToInt(crownOwed), nr = Mathf.FloorToInt(ringOwed);
            crownOwed -= nc; ringOwed -= nr;
            for (int i = 0; i < nc; i++) EmitCrown(eye);
            for (int i = 0; i < nr; i++) EmitRing(eye);
        }

        // ------------------------------------------------------------------
        //  probes, the mask, the anchors
        // ------------------------------------------------------------------
        void Probe(Vector3 centre, float eyeY)
        {
            float r = ProbeRadius * Mathf.Pow(Random.value, 0.7f);
            float a = Random.value * (Mathf.PI * 2f);
            var from = new Vector3(centre.x + Mathf.Cos(a) * r, eyeY + 2f, centre.z + Mathf.Sin(a) * r);
            if (!Physics.Raycast(from, Vector3.down, out var hit, 16f, GroundMask, QueryTriggerInteraction.Ignore)) return;
            if (hit.rigidbody != null) return;                       // a car's roof is not the ground
            Vector3 p = hit.point;
            bool covered = WeatherShelter.Contains(p + Vector3.up * 0.3f);
            if (!covered && Physics.Raycast(p + Vector3.up * 0.5f, Vector3.up, out var up, 45f, GroundMask, QueryTriggerInteraction.Ignore))
                covered = up.rigidbody == null;
            WriteMask(p.x, p.z, covered);
            if (covered || hit.normal.y < 0.85f || hit.collider.gameObject.layer != RoadLayer) return;
            anchorP[anchorNext] = p;
            anchorNrm[anchorNext] = hit.normal;
            anchorNext = (anchorNext + 1) % AnchorN;
            if (anchorCount < AnchorN) anchorCount++;
        }

        static int Wrap(int i) { int m = i % MaskN; return m < 0 ? m + MaskN : m; }

        void WriteMask(float x, float z, bool covered)
        {
            int cx = Mathf.FloorToInt(x / CellM), cz = Mathf.FloorToInt(z / CellM);
            byte v = covered ? (byte)0 : (byte)255;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ix = cx + dx, iz = cz + dz;
                    int t = Wrap(iz) * MaskN + Wrap(ix);
                    bool own = dx == 0 && dz == 0;
                    // a probe is the last word on its own cell, and a first
                    // guess for the eight round it (a deck is wider than a metre)
                    if (!own && known[t] && stampX[t] == ix && stampZ[t] == iz) continue;
                    if (mask[t] != v) { mask[t] = v; maskDirty = true; }
                    if (own) { known[t] = true; stampX[t] = ix; stampZ[t] = iz; }
                    else if (!known[t] || stampX[t] != ix || stampZ[t] != iz) { known[t] = false; stampX[t] = ix; stampZ[t] = iz; }
                }
        }

        void UploadMask(Vector3 eye)
        {
            if (maskTex == null)
            {
                maskTex = new Texture2D(MaskN, MaskN, TextureFormat.R8, false, true)
                {
                    name = "Rain2 cover", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                for (int i = 0; i < mask.Length; i++) mask[i] = 255;
                maskDirty = true;
                maskNextUpload = 0f;
            }
            if (Time.unscaledTime < maskNextUpload) return;
            maskNextUpload = Time.unscaledTime + 0.2f;
            // cells the window has left answer "open" until they are probed again
            int cx = Mathf.FloorToInt(eye.x / CellM), cz = Mathf.FloorToInt(eye.z / CellM);
            const int half = MaskN / 2 - 1;
            for (int t = 0; t < mask.Length; t++)
            {
                if (mask[t] == 255) continue;
                if (Mathf.Abs(stampX[t] - cx) <= half && Mathf.Abs(stampZ[t] - cz) <= half) continue;
                mask[t] = 255; known[t] = false; maskDirty = true;
            }
            if (maskDirty)
            {
                maskTex.SetPixelData(mask, 0);
                maskTex.Apply(false, false);
                maskDirty = false;
            }
            Rain2.PushCover(maskTex, MaskN * CellM);
        }

        // ------------------------------------------------------------------
        //  the splashes
        // ------------------------------------------------------------------
        bool PickAnchor(Vector3 eye, out Vector3 p, out Vector3 n)
        {
            int i = Random.Range(0, anchorCount);
            p = anchorP[i]; n = anchorNrm[i];
            if ((p - eye).sqrMagnitude > AnchorReach * AnchorReach) return false;
            // anywhere on the plane within a stride of the probed point
            float dx = Random.Range(-0.35f, 0.35f), dz = Random.Range(-0.35f, 0.35f);
            p.x += dx; p.z += dz;
            p.y -= (n.x * dx + n.z * dz) / Mathf.Max(n.y, 0.5f);
            return true;
        }

        void EmitCrown(Vector3 eye)
        {
            if (!PickAnchor(eye, out var p, out var n)) return;
            var e = new ParticleSystem.EmitParams
            {
                position = p + n * 0.012f,
                velocity = n * Random.Range(0.10f, 0.35f),
                startLifetime = Random.Range(0.14f, 0.26f),
                startSize = Random.Range(0.035f, 0.075f),
                startColor = new Color(0.86f, 0.90f, 0.96f, 0.55f),
            };
            crowns.Emit(e, 1);
        }

        void EmitRing(Vector3 eye)
        {
            if (!PickAnchor(eye, out var p, out var n)) return;
            var e = new ParticleSystem.EmitParams
            {
                position = p + n * 0.015f,
                velocity = Vector3.zero,
                startLifetime = Random.Range(0.30f, 0.48f),
                startSize = Random.Range(0.14f, 0.26f),
                startColor = new Color(0.86f, 0.90f, 0.96f, 0.20f),
                rotation3D = Quaternion.FromToRotation(Vector3.up, n).eulerAngles,
            };
            rings.Emit(e, 1);
        }

        bool BuildSplashes()
        {
            var shader = Shader.Find("PSX/Rain");
            if (shader == null) { Rain2.Splashes = false; return false; }
            crownTex = CrownTexture();
            ringTex = RingTexture();
            crownMat = SplashMat(shader, crownTex, "Rain2 crown");
            ringMat = SplashMat(shader, ringTex, "Rain2 ring");

            crowns = NewSystem("Rain2 Crowns", CrownCap);
            var cr = crowns.GetComponent<ParticleSystemRenderer>();
            cr.renderMode = ParticleSystemRenderMode.Billboard;
            cr.alignment = ParticleSystemRenderSpace.View;
            // the sprite stands ON its point: a crown rises from the surface
            cr.pivot = new Vector3(0f, 0.5f, 0f);
            // half of them mirrored: one drawing, two crowns
            cr.flip = new Vector3(0.5f, 0f, 0f);
            cr.sharedMaterial = crownMat;
            // thrown up fast, then it hangs: grows over its short life
            var cs = crowns.sizeOverLifetime;
            cs.enabled = true;
            cs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f, 4f, 4f), new Keyframe(1f, 1f, 0.2f, 0.2f)));
            Fade(crowns, 0.0f, 1f, 0.35f);
            crowns.Play();

            rings = NewSystem("Rain2 Rings", RingCap);
            var rr = rings.GetComponent<ParticleSystemRenderer>();
            ringMesh = FlatQuad();
            rr.renderMode = ParticleSystemRenderMode.Mesh;
            rr.mesh = ringMesh;
            rr.alignment = ParticleSystemRenderSpace.World;
            rr.enableGPUInstancing = false;
            rr.sharedMaterial = ringMat;
            var rs = rings.sizeOverLifetime;
            rs.enabled = true;
            rs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.18f, 2.4f, 2.4f), new Keyframe(1f, 1f, 0.3f, 0.3f)));
            Fade(rings, 0.0f, 1f, 0.15f);
            rings.Play();
            return true;
        }

        static Material SplashMat(Shader shader, Texture2D tex, string name)
        {
            var m = new Material(shader) { name = name + " (runtime)", hideFlags = HideFlags.HideAndDontSave, mainTexture = tex };
            m.SetColor("_Tint", Color.white);
            // a 5 cm crown a pace from the lens would be drawn a hand tall: it fades in from 1 m to 3
            m.SetFloat("_NearM", 1.0f);
            m.SetFloat("_NearRamp", 2.0f);
            return m;
        }

        ParticleSystem NewSystem(string name, int cap)
        {
            var go = new GameObject(name) { layer = CarReflections.NoReflectLayer, hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = cap;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startRotation3D = true;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return ps;
        }

        static void Fade(ParticleSystem ps, float a0, float aPeak, float peakAt)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(aPeak, peakAt), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>A unit quad lying on the ground (its normal +Y), for the rings.</summary>
        static Mesh FlatQuad()
        {
            var m = new Mesh { name = "Rain2 ring quad", hideFlags = HideFlags.HideAndDontSave };
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A crown seen from the side: a low cup of water at the foot
        /// and three uneven jets leaning out of it, a droplet off each tip.</summary>
        static Texture2D CrownTexture()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Rain2 crown", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int yy = 0; yy < n; yy++)
                for (int xx = 0; xx < n; xx++)
                {
                    float x = (xx + 0.5f) / n * 2f - 1f, y = (yy + 0.5f) / n;
                    float a = Mathf.Clamp01(1f - Mathf.Abs(x) / 0.70f) * Mathf.Clamp01(1f - y / 0.16f) * 0.8f;
                    for (int j = 0; j < 3; j++)
                    {
                        float ang = j == 0 ? -0.62f : j == 1 ? 0.08f : 0.50f;
                        float len = j == 0 ? 0.52f : j == 1 ? 0.74f : 0.40f;
                        float sa = Mathf.Sin(ang), ca = Mathf.Cos(ang);
                        float along = x * sa + y * ca, across = Mathf.Abs(x * ca - y * sa);
                        if (along > 0.04f && along < len)
                            a = Mathf.Max(a, Mathf.Clamp01(1f - across / 0.06f) * (0.75f - along / len * 0.45f));
                        float ddx = x - sa * (len + 0.09f), ddy = y - ca * (len + 0.09f);
                        a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Sqrt(ddx * ddx + ddy * ddy) / 0.075f) * 0.9f);
                    }
                    px[yy * n + xx] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(a)));
                }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>A ring: one thin bright wave near the sprite's edge.</summary>
        static Texture2D RingTexture()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Rain2 ring", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int yy = 0; yy < n; yy++)
                for (int xx = 0; xx < n; xx++)
                {
                    float x = (xx + 0.5f) / n * 2f - 1f, y = (yy + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(x * x + y * y);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.80f) / 0.10f);
                    px[yy * n + xx] = new Color32(255, 255, 255, (byte)(255f * a * a));
                }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        // ------------------------------------------------------------------
        //  tools
        // ------------------------------------------------------------------
        /// <summary>
        /// EDIT-MODE entry for the shot tools (as WeatherFx.Preview): the
        /// rain's ground as the game would show it round <paramref name="cam"/>
        /// after raining a while - the globals pushed, the ground probed, the
        /// mask uploaded and a steady state of splashes simulated. Call
        /// <see cref="PreviewClear"/> afterwards.
        /// </summary>
        public static void Preview(Camera cam, float rain, float level, float camSpeed = 0f, int seed = 20261116)
        {
            if (!Rain2.On) { Rain2.Push(0f, 0f); return; }
            if (instance == null)
            {
                var go = new GameObject("Rain2 (preview)") { hideFlags = HideFlags.HideAndDontSave };
                instance = go.AddComponent<Rain2Driver>();
            }
            var d = instance;
            Rain2.Push(rain, level);
            d.ClearGround();
            if (cam == null || rain <= 0.01f) return;
            var old = Random.state;
            Random.InitState(seed);
            Physics.SyncTransforms();
            Vector3 eye = cam.transform.position;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 centre = eye + fwd * ProbeAhead;
            for (int i = 0; i < 900; i++) d.Probe(centre, eye.y);
            d.maskNextUpload = 0f;
            d.UploadMask(eye);
            float k = rain * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SplashFadeFrom, SplashFadeTo, camSpeed)));
            if (Rain2.Splashes && k > 0.01f && d.anchorCount > 0 && (d.crowns != null || d.BuildSplashes()))
            {
                var tier = Rain2.CurrentTier;
                d.crowns.Clear(); d.rings.Clear();
                const float step = 0.03f;
                for (int s = 0; s < 18; s++)
                {
                    int nc = Mathf.RoundToInt(CrownRateFor(tier) * k * step), nr = Mathf.RoundToInt(RingRateFor(tier) * k * step);
                    for (int i = 0; i < nc; i++) d.EmitCrown(eye);
                    for (int i = 0; i < nr; i++) d.EmitRing(eye);
                    d.crowns.Simulate(step, true, false, false);
                    d.rings.Simulate(step, true, false, false);
                }
            }
            Random.state = old;
        }

        public static void PreviewClear()
        {
            Rain2.Push(0f, 0f);
            Rain2.PushCover(null, 1f);
            if (instance == null) return;
            var d = instance;
            instance = null;
            if (Application.isPlaying) Destroy(d.gameObject);
            else { d.OnDestroy(); DestroyImmediate(d.gameObject); }
        }
    }
}
