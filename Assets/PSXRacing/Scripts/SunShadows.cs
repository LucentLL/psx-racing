using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PSXRacing
{
    /// <summary>
    /// THE SUN CASTS SHADOWS, AND A ROOF HIDES THE SKY (the DAY PASS,
    /// 2026-09-21).
    ///
    /// The owner, with five daylight frames of Forza Horizon: improved
    /// lighting "during morning, day, and afternoon. Basically, when
    /// headlights and street lights are not the major driving factors of the
    /// lighting." What those frames have and this renderer had none of is a
    /// sun that things get in the way of: a car's shadow raking across the
    /// road, an avenue dappling it, a gallery's pillars striping it, a tunnel
    /// that is DARK. Here the sun was a per-vertex lambert and nothing else,
    /// so the road under a bridge was lit exactly like the road in a field.
    ///
    /// So everything opaque near the camera is drawn again, through
    /// PSX/ShadowCaster (which writes how far along the ray each texel's first
    /// SOLID surface is, and its first LEAF - solids first, cutouts after, for
    /// the reason that file gives), into TWO square maps:
    ///
    ///   * THE SUN'S, every frame, looking down the sun's rays. PSX/Lit and
    ///     PSX/CarPaint ask it how much of the SUN reaches each pixel.
    ///   * THE SKY'S, looking straight down, and only when the camera has
    ///     moved on (nothing in it moves: cars are left out). They ask it
    ///     whether anything is OVERHEAD, and a pixel with a roof over it loses
    ///     most of its AMBIENT. Shade from a tower still has the whole sky
    ///     above it; the inside of a tunnel does not, and no map drawn from
    ///     the sun can tell the two apart.
    ///
    /// Plain shadow maps, fitted to this game:
    ///
    ///   * THEY ARE OURS, NOT URP's. Every surface here is drawn by a
    ///     hand-written unlit pass URP's lights know nothing about, the light
    ///     is a global vector, and the target is WebGL on a phone. One command
    ///     buffer, two RGBA8 textures, no second camera, nothing of the
    ///     pipeline's.
    ///   * ONE BOX, AHEAD OF THE CAMERA. 180 m square, centred 58 m up the
    ///     road from the eye; for the sun a column 600 m long down its rays -
    ///     so a tower 400 m toward a setting sun still shadows the road, which
    ///     it does. 17 cm a texel at 1024, under a framebuffer 240 lines tall.
    ///     The boxes move in WHOLE TEXELS: a map that slid continuously would
    ///     re-decide every shadow's edge every frame, and the edges would
    ///     crawl as the car moved.
    ///   * OFF UNLESS AN HOUR TURNED THEM ON. The strengths come from
    ///     PSXGlobals (sunShadow, skyShade), which only TimeOfDay.Apply
    ///     writes: by day, never at dusk or at night - the lamps' hours, and
    ///     the frames where they cost the most. At zero nothing is drawn,
    ///     nothing is sampled, and every scene that never applied an hour -
    ///     each interior - is the picture it always was.
    ///
    /// The casters are found by looking (every MeshRenderer wearing PSX/Lit or
    /// PSX/CarPaint), remembered, and re-counted every few seconds; a streamed
    /// city tile announces itself (<see cref="Register"/>) so its towers cast
    /// on the frame they appear. Anything under a Rigidbody is asked where it
    /// is every frame; everything else is asked once.
    ///
    /// THE COST IS DRAW CALLS, and on WebGL on a phone a draw call is dear. A
    /// city tile's buildings are one renderer with eight materials - eight
    /// calls to say "solid" eight times - so a renderer with several solid
    /// submeshes gets a PROXY the first time it is drawn: the same triangles
    /// as one submesh, positions only, one call (<see cref="ProxyFor"/>). The
    /// city's ground, kerbs, water and lamp posts are left out altogether
    /// (<see cref="Exclude"/>): nothing is under them to shade. The first
    /// Charlotte frames were 94-244 calls a map; with both, 30-60.
    /// </summary>
    public static class SunShadows
    {
        // ------------------------------------------------------------------
        //  The numbers
        // ------------------------------------------------------------------
        /// <summary>Half the side of the square both maps cover, metres, and
        /// how far up the road from the eye it is centred (a chase camera
        /// looks forward, so most of a box centred ON it would be spent behind
        /// the player). The second pair is a TOUCH device's: under half the
        /// area, so under half the casters and draw calls, and what it gives
        /// up is shadows more than 100 m away on a screen the size of a hand.</summary>
        public const float HalfExtentM = 90f, AheadM = 58f;
        public const float HalfExtentTouchM = 60f, AheadTouchM = 40f;
        /// <summary>The two in use (the touch panel is up, or it is not).</summary>
        public static float HalfExtent => small ? HalfExtentTouchM : HalfExtentM;
        public static float Ahead => small ? AheadTouchM : AheadM;
        /// <summary>The sun column's reach toward the sun and away from it,
        /// from the square's centre. Toward is long for what a low sun does:
        /// at 7 degrees a 50 m tower's shadow is 400 m.</summary>
        public const float TowardSunM = 420f, AwayM = 180f;
        /// <summary>The sky column's reach above the square's centre and
        /// below it: a tower's top, and the bottom of the valley a mountain
        /// road hangs over.</summary>
        public const float SkyAboveM = 170f, SkyBelowM = 130f;
        /// <summary>The sun map's side in texels. The smaller one is for a
        /// touch device - the only thing this game can tell a phone by -
        /// where the extra pass is the most expensive thing on the frame.</summary>
        public const int Resolution = 1024, ResolutionTouch = 768;
        /// <summary>The sky map's side: half the sun's. What it draws is
        /// "under something", which has no fine edges worth a texel.</summary>
        public const int SkyResolution = 512, SkyResolutionTouch = 384;
        /// <summary>The normal offset for a face edge-on to the rays, in
        /// TEXELS of the map (PSXSunShadow.cginc says why it exists): two,
        /// because the four-texel compare reaches one texel past the point.</summary>
        public const float NormalOffsetTexels = 2.0f;
        /// <summary>The constant biases, metres along the ray, over what the
        /// normal offset already does for a sloping face. The sky's was 0.30
        /// for its bigger texels, and the top foot of a tunnel's wall - that
        /// close under its own roof - came out lit, in stripes.</summary>
        public const float DepthBiasM = 0.06f, SkyDepthBiasM = 0.10f;
        /// <summary>Nothing smaller than this casts, metres across: a bolt's
        /// shadow is under a pixel and is a draw call all the same. And
        /// nothing under <see cref="MinRoofM"/> across is a ROOF.</summary>
        public const float MinCasterM = 0.30f, MinRoofM = 2.0f;
        /// <summary>
        /// A caster smaller than this many radians across, seen from the
        /// camera, is not drawn into the sun's map. Its shadow is about its
        /// own size and lies about where it stands, so a marker post 80 m up
        /// the road - a fraction of a pixel of shadow on a 240-line frame - is
        /// a draw call and nothing else, and on a phone the draw calls are the
        /// cost of this whole pass. 0.012 is a little under three pixels.
        /// </summary>
        public const float MinCasterAngle = 0.012f;
        /// <summary>How far the square has to move before the sky's map is
        /// drawn again, metres. Nothing in that map moves, its rim fades out
        /// over the last 7 m, and at 300 km/h this is still only one redraw
        /// in eight frames; parked, it is none.</summary>
        public const float SkyRedrawM = 10f;
        /// <summary>
        /// THE GOVERNOR. Nobody can measure a phone from here, so the pass
        /// measures itself: over each window of <see cref="GovernorFrames"/>
        /// frames in play, if more than <see cref="GovernorSlowShare"/> of
        /// them took longer than <see cref="GovernorSlowSeconds"/> (under 20
        /// fps - a game already in trouble, where giving the shadows up can
        /// only help; not 24, which a phone that is merely busy hovers round,
        /// and whose shadows would then vanish fifteen seconds into every
        /// race), the sun's map steps DOWN a level and stays there for the
        /// scene - first to the big casters only (cars, and anything over
        /// <see cref="LiteCasterM"/> across: buildings, decks, tree lines),
        /// then off. It never steps back up, so it cannot hunt; a new scene
        /// starts at full again. The first seconds of a scene (loading,
        /// streaming in) are not counted. The sky's map is left alone: it is
        /// drawn once every ten metres.
        /// </summary>
        public const int GovernorFrames = 150;
        public const float GovernorSlowSeconds = 1f / 20f, GovernorSlowShare = 0.6f, GovernorGraceSeconds = 6f;
        public const float LiteCasterM = 8f;
        /// <summary>0 = everything casts, 1 = the big casters and the cars,
        /// 2 = the sun's map is off. See the governor.</summary>
        public static int Level { get; private set; }
        /// <summary>A proxy mesh nothing has drawn for this long is destroyed,
        /// seconds: the city streams, and a tile two blocks behind the car
        /// will not be in the box again.</summary>
        public const float ProxyIdleSeconds = 20f;
        /// <summary>Seconds between re-counts of the scene's renderers in
        /// play. Known renderers cost a hash lookup each, so this is cheap;
        /// what it buys is a car swapped at the start line or a prop spawned
        /// mid-race casting within a moment of existing.</summary>
        public const float RescanSeconds = 3f;

        // ------------------------------------------------------------------
        //  State
        // ------------------------------------------------------------------
        /// <summary>The sun's shadows, 0..1, from PSXGlobals every frame.</summary>
        public static float Strength { get; private set; }
        /// <summary>The sky's, the same.</summary>
        public static float SkyStrength { get; private set; }
        /// <summary>How many casters the last sun map drew, and in how many
        /// draw calls (a renderer with three casting submeshes is three).
        /// For the tools and the self-test.</summary>
        public static int DrawnLast { get; private set; }
        public static int DrawCallsLast { get; private set; }
        /// <summary>The same for the last sky map that was actually drawn.</summary>
        public static int SkyDrawnLast { get; private set; }
        /// <summary>How many renderers are known as casters.</summary>
        public static int Known => casters.Count;
        /// <summary>The maps themselves, for the tools.</summary>
        public static RenderTexture Map => map;
        public static RenderTexture SkyMap => skyMap;
        /// <summary>World to each map, as last pushed (xy texel 0..1, z depth 0..1).</summary>
        public static Matrix4x4 WorldToMap { get; private set; }
        public static Matrix4x4 WorldToSkyMap { get; private set; }

        static Vector3 dirToSun = Vector3.up;

        class Caster
        {
            public Renderer r;
            /// <summary>Per submesh: null = does not cast, <see cref="opaque"/>
            /// = solid, anything else = that cutout's own caster.</summary>
            public Material[] mats;
            public int solids;
            public bool moves;
            public Vector3 centre;
            public float radius;
            /// <summary>Every solid submesh as one, positions only; null until
            /// first drawn, and for good if it cannot be made (see ProxyFor).</summary>
            public Mesh proxy;
            public bool proxyTried;
            public float drawnAt;
        }

        static readonly List<Caster> casters = new List<Caster>();
        static readonly Dictionary<Renderer, Caster> known = new Dictionary<Renderer, Caster>();
        static readonly HashSet<Renderer> notCasters = new HashSet<Renderer>();
        /// <summary>Renderers somebody has said never cast (see Exclude).</summary>
        static readonly HashSet<Renderer> excluded = new HashSet<Renderer>();
        /// <summary>The casters that passed this map's cull, kept between its
        /// two passes so the box is tested once.</summary>
        static readonly List<Caster> inBox = new List<Caster>();
        static readonly List<CombineInstance> combineBuf = new List<CombineInstance>();
        /// <summary>Source material to its caster (null = not a caster).
        /// Every opaque source shares <see cref="opaque"/>; a cutout gets a
        /// twin carrying its texture, so a tree casts its leaves.</summary>
        static readonly Dictionary<Material, Material> casterFor = new Dictionary<Material, Material>();
        static readonly List<Material> matBuf = new List<Material>();
        static readonly List<Material> owned = new List<Material>();

        static RenderTexture map, skyMap;
        static Material opaque;
        static Shader casterShader;
        static CommandBuffer cmd;
        static bool hooked, scanned, warned, small, skyStale = true;
        static int govFrames, govSlow;
        static float govSince;
        static Vector3 skyDrawnAt;
        static float nextScan;
        static int renderedFrame = -1;

        static readonly int MapId = Shader.PropertyToID("_PSXShadowMap");
        static readonly int MatrixId = Shader.PropertyToID("_PSXShadowMatrix");
        static readonly int ParamsId = Shader.PropertyToID("_PSXShadowParams");
        static readonly int SkyMapId = Shader.PropertyToID("_PSXSkyMap");
        static readonly int SkyMatrixId = Shader.PropertyToID("_PSXSkyMatrix");
        static readonly int SkyParamsId = Shader.PropertyToID("_PSXSkyParams");
        static readonly int CasterMatrixId = Shader.PropertyToID("_PSXCasterMatrix");

        // ------------------------------------------------------------------
        //  Wiring
        // ------------------------------------------------------------------

        /// <summary>
        /// Install the per-camera render, once. Called from
        /// PSXGlobals.OnEnable, beside StreetLights' hook and for its reason:
        /// the map has to be drawn for the pose the camera RENDERS from, which
        /// is only known once the cars and the chase camera have moved in
        /// LateUpdate. It runs in edit mode too, so a tool's render request
        /// gets shadows with no tool asking.
        /// </summary>
        public static void EnsureHook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        /// <summary>
        /// A new scene: forget the last one's renderers and TURN BOTH MAPS
        /// OFF until somebody in the new scene turns them on. A global nothing
        /// writes is stale, not empty - a menu scene with no PSXGlobals in it
        /// would otherwise go on sampling the race's last map.
        /// </summary>
        static void OnSceneLoaded(Scene s, LoadSceneMode mode) { if (mode == LoadSceneMode.Single) Forget(); }
        static void OnSceneUnloaded(Scene s) { scanned = false; }

        static void Forget()
        {
            foreach (var c in casters) DropProxy(c);
            excluded.RemoveWhere(r => r == null);
            casters.Clear();
            known.Clear();
            notCasters.Clear();
            foreach (var m in owned)
            {
                if (m == null) continue;
                if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m);
            }
            owned.Clear();
            casterFor.Clear();
            scanned = false;
            skyStale = true;
            Strength = 0f;
            SkyStrength = 0f;
            Level = 0;
            govFrames = govSlow = 0;
            govSince = Time.unscaledTime;
            PushOff();
        }

        /// <summary>One frame of the governor (play only; see the constants).</summary>
        static void Govern()
        {
            if (Level >= 2 || Strength <= 0.001f) return;
            if (Time.unscaledTime - govSince < GovernorGraceSeconds) return;
            // A paused game's frames say nothing about the renderer.
            if (Time.timeScale <= 0f) return;
            govFrames++;
            if (Time.unscaledDeltaTime > GovernorSlowSeconds) govSlow++;
            if (govFrames < GovernorFrames) return;
            if (govSlow > GovernorFrames * GovernorSlowShare)
            {
                Level++;
                Debug.Log("SunShadows: " + govSlow + " of " + govFrames + " frames under 20 fps - " +
                          (Level == 1 ? "big casters only from here" : "sun shadows off for this scene"));
            }
            govFrames = govSlow = 0;
        }

        /// <summary>From PSXGlobals.Apply, every frame: how strong each map
        /// is, and where the sun is.</summary>
        public static void Configure(float sunStrength, float skyStrength, Vector3 directionToSun)
        {
            Strength = Mathf.Clamp01(sunStrength);
            SkyStrength = Mathf.Clamp01(skyStrength);
            if (directionToSun.sqrMagnitude > 1e-6f) dirToSun = directionToSun.normalized;
            // A sun at or under the horizon shadows everything with the
            // ground itself; that is the ambient's job, not a map's.
            if (dirToSun.y < 0.02f) Strength = 0f;
            if (Strength <= 0.001f) Shader.SetGlobalVector(ParamsId, Vector4.zero);
            if (SkyStrength <= 0.001f) { Shader.SetGlobalVector(SkyParamsId, Vector4.zero); skyStale = true; }
        }

        static bool Off => Strength <= 0.001f && SkyStrength <= 0.001f;

        static void PushOff()
        {
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            Shader.SetGlobalVector(SkyParamsId, Vector4.zero);
        }

        /// <summary>A streamed piece of world has just been built (a city
        /// tile): its renderers cast from this frame, not from the next
        /// re-count.</summary>
        public static void Register(GameObject root)
        {
            // With the maps off nothing is kept: the first map of an hour
            // that has them counts the whole scene itself.
            if (root == null || Off) return;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false)) Consider(r);
        }

        /// <summary>
        /// This renderer never casts. For what lies ON the ground with nothing
        /// under it to shade - a city tile's ground, kerbs and water - and for
        /// its lamp posts, a hundred 30 cm poles a tile whose shadows are a
        /// pixel wide. Each would be a draw call (the ground is five) a tile a
        /// frame. Safe to call before the renderer has materials, and with the
        /// maps off.
        /// </summary>
        public static void Exclude(GameObject go)
        {
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r != null) excluded.Add(r);
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || Off) return;
            // The HUD overlay and the output blit draw only the UI layer.
            if ((cam.cullingMask & ~(1 << 5)) == 0) return;
            if (cam.cameraType == CameraType.Preview) return;
            if (Application.isPlaying)
            {
                // Once a frame, for the view the player is looking through.
                // The mirror and the pizza cam render before it and read the
                // maps as the last frame left them, with the matrices they
                // were drawn with: a frame old and exactly right for it.
                if (Time.frameCount == renderedFrame || !StreetLights.IsDriver(cam)) return;
                renderedFrame = Time.frameCount;
            }
            RenderFor(cam);
        }

        // ------------------------------------------------------------------
        //  The maps
        // ------------------------------------------------------------------

        /// <summary>
        /// Draw the maps for this camera's pose and push them. Public for the
        /// tools, which stand a camera somewhere and photograph through it
        /// (the hook does the same for them; belt and braces, as the lamp
        /// table is).
        /// </summary>
        public static void RenderFor(Camera cam)
        {
            if (cam == null || Off || !Ready()) { PushOff(); return; }
            if (!scanned || !Application.isPlaying || Time.unscaledTime >= nextScan) Scan();
            if (Application.isPlaying) Govern();
            bool sunOn = Strength > 0.001f && Level < 2;
            if (!sunOn) Shader.SetGlobalVector(ParamsId, Vector4.zero);

            var ct = cam.transform;
            Vector3 eyeAt = ct.position;
            Vector3 flat = ct.forward; flat.y = 0f;
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 centre = eyeAt + flat * Ahead;

            cmd.Clear();
            bool any = false;

            if (sunOn)
            {
                Vector3 fwd = -dirToSun;
                Vector3 upHint = Mathf.Abs(fwd.y) > 0.98f ? Vector3.forward : Vector3.up;
                WorldToMap = Draw(map, centre, Quaternion.LookRotation(fwd, upHint), TowardSunM, AwayM,
                                  eyeAt, false, out int drawn, out int calls);
                DrawnLast = drawn;
                DrawCallsLast = calls;
                any = true;
            }

            // The sky's map: only when the square has moved on, or something
            // new has come to stand in it. Edit mode has no "moved on": a tool
            // teleports the camera and must get the map for where it is.
            bool skyNow = SkyStrength > 0.001f &&
                          (skyStale || !Application.isPlaying || (centre - skyDrawnAt).sqrMagnitude > SkyRedrawM * SkyRedrawM);
            if (skyNow)
            {
                WorldToSkyMap = Draw(skyMap, centre, Quaternion.LookRotation(Vector3.down, Vector3.forward),
                                     SkyAboveM, SkyBelowM, eyeAt, true, out int drawn, out _);
                SkyDrawnLast = drawn;
                skyDrawnAt = centre;
                skyStale = false;
                any = true;
            }

            if (any) Graphics.ExecuteCommandBuffer(cmd);

            if (sunOn)
            {
                Shader.SetGlobalTexture(MapId, map);
                Shader.SetGlobalMatrix(MatrixId, WorldToMap);
                float texel = HalfExtent * 2f / map.width;
                Shader.SetGlobalVector(ParamsId, new Vector4(Strength, map.width, texel * NormalOffsetTexels,
                                                             DepthBiasM / (TowardSunM + AwayM)));
            }
            if (SkyStrength > 0.001f)
            {
                Shader.SetGlobalTexture(SkyMapId, skyMap);
                Shader.SetGlobalMatrix(SkyMatrixId, WorldToSkyMap);
                float texel = HalfExtent * 2f / skyMap.width;
                Shader.SetGlobalVector(SkyParamsId, new Vector4(SkyStrength, skyMap.width, texel * NormalOffsetTexels,
                                                                SkyDepthBiasM / (SkyAboveM + SkyBelowM)));
            }
        }

        /// <summary>
        /// Queue one map: a box <see cref="HalfExtentM"/> either side of
        /// <paramref name="centre"/> across the rays, reaching
        /// <paramref name="towardM"/> back up them and <paramref name="awayM"/>
        /// on down. Returns world-to-map. <paramref name="roofsOnly"/> is the
        /// sky's rule: nothing that moves (the map is kept for many frames)
        /// and nothing too small to be a roof.
        /// </summary>
        static Matrix4x4 Draw(RenderTexture target, Vector3 centre, Quaternion rot, float towardM, float awayM,
                              Vector3 eyeAt, bool roofsOnly, out int drawn, out int calls)
        {
            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right, up = rot * Vector3.up;
            float half = HalfExtent;
            float texel = half * 2f / target.width;
            float minRadius = !roofsOnly && Level >= 1 ? LiteCasterM * 0.5f : 0f;
            // Whole texels, in the rays' own plane.
            float lx = Vector3.Dot(centre, right), ly = Vector3.Dot(centre, up);
            centre += right * (Mathf.Floor(lx / texel) * texel - lx) + up * (Mathf.Floor(ly / texel) * texel - ly);

            Vector3 from = centre - fwd * towardM;
            float far = towardM + awayM;
            // A view matrix looks down MINUS z (the OpenGL convention every
            // Unity view matrix keeps, whatever the platform).
            Matrix4x4 view = Matrix4x4.TRS(from, rot, Vector3.one).inverse;
            view.SetRow(2, -view.GetRow(2));
            Matrix4x4 proj = Matrix4x4.Ortho(-half, half, -half, half, 0f, far);
            // Clip (-1..1 on every axis, in that same convention) to 0..1.
            Matrix4x4 toUnit = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));
            Matrix4x4 worldToMap = toUnit * proj * view;

            cmd.SetRenderTarget(target);
            // White decodes to just past 1: nothing there, so nothing shadowed.
            cmd.ClearRenderTarget(true, true, Color.white);
            cmd.SetViewProjectionMatrices(view, proj);
            cmd.SetGlobalMatrix(CasterMatrixId, worldToMap);

            drawn = 0;
            calls = 0;
            inBox.Clear();
            float now = Time.unscaledTime;
            for (int i = casters.Count - 1; i >= 0; i--)
            {
                var c = casters[i];
                if (c.r == null)
                {
                    DropProxy(c);
                    casters.RemoveAt(i);
                    continue;
                }
                if (roofsOnly)
                {
                    if (c.moves || c.radius * 2f < MinRoofM) continue;
                }
                else if (c.moves) Measure(c);
                else if (c.radius < minRadius) continue;
                // In the column? A sphere against the box, in the rays' frame.
                Vector3 d = c.centre - centre;
                if (Mathf.Abs(Vector3.Dot(d, right)) > half + c.radius) continue;
                if (Mathf.Abs(Vector3.Dot(d, up)) > half + c.radius) continue;
                float along = Vector3.Dot(d, fwd);
                if (along < -towardM - c.radius || along > awayM + c.radius) continue;
                if (!roofsOnly && c.radius < MinCasterAngle * (c.centre - eyeAt).magnitude) continue;
                if (!c.r.enabled || !c.r.gameObject.activeInHierarchy) continue;
                inBox.Add(c);
                c.drawnAt = now;
                drawn++;
            }

            // EVERY SOLID FIRST (pass 0, into r,g), then every cutout (pass 1,
            // into b,a) against the depth the solids left: PSX/ShadowCaster
            // says why the order is the whole point.
            foreach (var c in inBox)
            {
                if (c.solids == 0) continue;
                var proxy = c.solids > 1 ? ProxyFor(c) : null;
                if (proxy != null) { cmd.DrawMesh(proxy, c.r.localToWorldMatrix, opaque, 0, 0); calls++; continue; }
                for (int m = 0; m < c.mats.Length; m++)
                    if (c.mats[m] == opaque) { cmd.DrawRenderer(c.r, opaque, m, 0); calls++; }
            }
            foreach (var c in inBox)
            {
                if (c.solids == c.mats.Length) continue;
                for (int m = 0; m < c.mats.Length; m++)
                    if (c.mats[m] != null && c.mats[m] != opaque) { cmd.DrawRenderer(c.r, c.mats[m], m, 1); calls++; }
            }
            inBox.Clear();
            return worldToMap;
        }

        static bool Ready()
        {
            if (casterShader == null) casterShader = Shader.Find("PSX/ShadowCaster");
            if (casterShader == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("SunShadows: PSX/ShadowCaster is missing (not in the always-included list?) - no sun shadows.");
                }
                return false;
            }
            if (opaque == null)
                opaque = new Material(casterShader) { name = "SunShadowOpaque", hideFlags = HideFlags.HideAndDontSave };
            if (cmd == null) cmd = new CommandBuffer { name = "SunShadows" };

            var touch = TouchControls.Instance;
            bool wasSmall = small;
            small = touch != null && touch.Visible;
            if (small != wasSmall) skyStale = true;
            Fit(ref map, small ? ResolutionTouch : Resolution, "PSXSunShadowMap");
            if (Fit(ref skyMap, small ? SkyResolutionTouch : SkyResolution, "PSXSkyShadeMap")) skyStale = true;
            return true;
        }

        /// <summary>Make, remake or revive one map; true if what is in it now
        /// is not what was (so the sky's must be drawn before it is read).</summary>
        static bool Fit(ref RenderTexture rt, int size, string name)
        {
            if (rt != null && rt.width != size)
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                rt = null;
            }
            if (rt == null)
            {
                // LINEAR, point-filtered, no mips, no MSAA: the rgb is a
                // packed number, and anything that blends two texels or bends
                // one through an sRGB curve makes it a different number.
                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = name,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    antiAliasing = 1,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                rt.Create();
                return true;
            }
            if (!rt.IsCreated()) { rt.Create(); return true; }
            return false;
        }

        // ------------------------------------------------------------------
        //  Who casts
        // ------------------------------------------------------------------

        static void Scan()
        {
            scanned = true;
            nextScan = Time.unscaledTime + RescanSeconds;
            var all = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++) Consider(all[i]);
            // In edit mode nothing announces a move: ask everything again.
            if (!Application.isPlaying)
                for (int i = 0; i < casters.Count; i++)
                    if (casters[i].r != null) Measure(casters[i]);
            // Proxies nothing has drawn lately: the tile is behind the car.
            float idle = Time.unscaledTime - ProxyIdleSeconds;
            for (int i = 0; i < casters.Count; i++)
                if (casters[i].proxy != null && casters[i].drawnAt < idle) { DropProxy(casters[i]); casters[i].proxyTried = false; }
            // A set that only ever grew would hold every tile the city has
            // streamed past. Destroyed keys never come back, so drop them.
            if (notCasters.Count > 4096) notCasters.RemoveWhere(r => r == null);
            if (excluded.Count > 1024) excluded.RemoveWhere(r => r == null);
            if (known.Count > casters.Count + 512)
            {
                known.Clear();
                foreach (var c in casters) if (c.r != null) known[c.r] = c;
            }
        }

        static void Consider(MeshRenderer r)
        {
            if (r == null || known.ContainsKey(r) || notCasters.Contains(r)) return;
            // The UI layer's meshes (a world-space HUD) are nobody's shadow.
            if (r.gameObject.layer == 5 || excluded.Contains(r)) { notCasters.Add(r); return; }

            r.GetSharedMaterials(matBuf);
            Material[] mats = null;
            int solids = 0;
            for (int m = 0; m < matBuf.Count; m++)
            {
                var cast = CasterFor(matBuf[m]);
                if (cast == null) continue;
                if (mats == null) mats = new Material[matBuf.Count];
                mats[m] = cast;
                if (cast == opaque) solids++;
            }
            matBuf.Clear();
            if (mats == null) { notCasters.Add(r); return; }

            var c = new Caster { r = r, mats = mats, solids = solids, moves = r.GetComponentInParent<Rigidbody>() != null };
            Measure(c);
            if (!c.moves && c.radius * 2f < MinCasterM) { notCasters.Add(r); return; }
            known[r] = c;
            casters.Add(c);
            // Something new that stands still may be a roof over the square.
            if (!c.moves && c.radius * 2f >= MinRoofM) skyStale = true;
        }

        /// <summary>
        /// Every solid submesh of a caster as ONE submesh, positions only: one
        /// draw call where the renderer's own mesh is one a material. Made the
        /// first time the caster is drawn, not when it is found - the city
        /// keeps twenty-five tiles alive and four are ever in the box.
        ///
        /// Null, and never tried again, when it cannot be made: a mesh the
        /// CPU cannot read (an imported one whose importer did not ask), or a
        /// renderer Unity has folded into a static batch - in play every
        /// static renderer's MeshFilter holds the COMBINED mesh, and a proxy
        /// of that would be the whole scene drawn at one renderer's transform.
        /// Those keep the per-submesh DrawRenderer, which Unity resolves
        /// itself (SunShadowPlayCheck is what proves it does).
        /// </summary>
        static Mesh ProxyFor(Caster c)
        {
            if (c.proxy != null || c.proxyTried) return c.proxy;
            c.proxyTried = true;
            if (c.r.isPartOfStaticBatch) return null;
            var mf = c.r.GetComponent<MeshFilter>();
            var src = mf != null ? mf.sharedMesh : null;
            if (src == null || !src.isReadable) return null;
            combineBuf.Clear();
            int subs = Mathf.Min(src.subMeshCount, c.mats.Length);
            for (int m = 0; m < subs; m++)
                if (c.mats[m] == opaque)
                    combineBuf.Add(new CombineInstance { mesh = src, subMeshIndex = m, transform = Matrix4x4.identity });
            if (combineBuf.Count < 2) { combineBuf.Clear(); return null; }
            // 32-bit indices, whatever the source has: CombineMeshes gives each
            // submesh its own copy of the vertices it uses, and a city tile's
            // 60,000 shared ones came out as 75,600 - past a 16-bit index, and
            // an exception that took the whole city out of the first run.
            var proxy = new Mesh { name = src.name + " (shadow proxy)", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            try { proxy.CombineMeshes(combineBuf.ToArray(), true, false, false); }
            catch (System.Exception e)
            {
                Debug.LogWarning("SunShadows: no proxy for " + src.name + " (" + e.Message + ") - drawn a submesh at a time.");
                if (Application.isPlaying) Object.Destroy(proxy); else Object.DestroyImmediate(proxy);
                combineBuf.Clear();
                return null;
            }
            combineBuf.Clear();
            // Positions are all a depth needs; the rest is a tile's worth of
            // memory for nothing. Then hand it to the GPU and let the CPU go.
            proxy.normals = null;
            proxy.tangents = null;
            proxy.colors32 = null;
            proxy.uv = null;
            proxy.uv2 = null;
            proxy.UploadMeshData(true);
            c.proxy = proxy;
            return proxy;
        }

        static void DropProxy(Caster c)
        {
            if (c.proxy == null) return;
            if (Application.isPlaying) Object.Destroy(c.proxy); else Object.DestroyImmediate(c.proxy);
            c.proxy = null;
        }

        static void Measure(Caster c)
        {
            var b = c.r.bounds;
            c.centre = b.center;
            c.radius = b.extents.magnitude;
        }

        /// <summary>
        /// What a source material casts with: nothing unless it is one of the
        /// two opaque world shaders; the shared opaque caster if it has no
        /// cutoff; its own twin, carrying its texture, if it has.
        /// </summary>
        static Material CasterFor(Material src)
        {
            if (src == null) return null;
            if (casterFor.TryGetValue(src, out var have)) return have;
            Material made = null;
            var sh = src.shader;
            string name = sh != null ? sh.name : null;
            if (name == "PSX/Lit" || name == "PSX/CarPaint")
            {
                float cutoff = src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0f;
                if (cutoff <= 0.001f) made = opaque;
                else
                {
                    made = new Material(casterShader) { name = src.name + " (shadow)", hideFlags = HideFlags.HideAndDontSave };
                    made.SetFloat("_Cutoff", cutoff);
                    if (src.HasProperty("_MainTex"))
                    {
                        made.mainTexture = src.mainTexture;
                        made.mainTextureScale = src.mainTextureScale;
                        made.mainTextureOffset = src.mainTextureOffset;
                    }
                    if (src.HasProperty("_Color")) made.color = src.color;
                    owned.Add(made);
                }
            }
            casterFor[src] = made;
            return made;
        }
    }
}
