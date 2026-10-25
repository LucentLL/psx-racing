using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE FAR CITY (owner, 2026-10-24, an NFS 2015 night frame: "What can be
    /// done to make the city look this good? The city buildings look black and
    /// lit up from distance. Distant building lights are visible from a mile
    /// away"). The streamed world stops at the camera's 500 m far plane, and
    /// past it only uptown's tallest towers stood (CitySkyline): from 500 m to
    /// the horizon the night was black, and by day the land ended in sky. Four
    /// things now carry the city out to the horizon, drawn by the skyline's
    /// own rules (PSX/Lit's squeeze and OWN DEPTH, before the world):
    ///   * THE FAR LAND: the real ground (the DEM) with the real tree cover (the
    ///     canopy grid) standing on it, a <see cref="CellM"/> grid in 2 km
    ///     chunks out to <see cref="RadiusM"/>, in two colours MEASURED off the
    ///     city's own textures for today's dress (the trees' atlas, the
    ///     ground's grass) - no colour is made up here. Drawn only past the
    ///     squeeze's start (PSX/Lit _SkyFarLand), so never over the streamed
    ///     ground. It hides far lights and towers behind hills and woods where
    ///     the real ones are hidden.
    ///   * THE FAR LIGHTS: a dot for every street lamp the tiles stand
    ///     (CityMeshes.FarLampPlan: the same pitch, sides and pole height) and
    ///     for the lit windows of every building the skyline does not draw -
    ///     houses, shops, low offices - as PSX/Halo lights in the skyline's own
    ///     depth (_OwnDepth: hidden exactly by what stands in front of them),
    ///     never under a pixel, ONE draw.
    ///   * THE GLOW: the lit air over uptown, in the night sky only (PSX/Sky
    ///     CityGlow), so the towers stand black against it.
    ///   * THE EDGE: with all that behind it, the streamed world no longer fades
    ///     into the horizon colour at its edge; the band is pushed out past the
    ///     far plane (<see cref="EdgeStartShare"/>).
    /// In Fog weather none of it draws and TimeOfDay's band stands. The
    /// skyline's mid-rise tier (CitySkyline.MidMinM) stands the city's taller
    /// buildings on this land, by day and by night.
    /// </summary>
    public class CityFar : MonoBehaviour
    {
        // ---- the far land ----
        /// <summary>The far land's grid, metres.</summary>
        public const float CellM = 128f;
        const int ChunkCells = 16;
        /// <summary>A chunk of land (and of lights): 2,048 m.</summary>
        public const float ChunkM = CellM * ChunkCells;
        /// <summary>How far the far land reaches round the eye (a phone: less).</summary>
        public static float RadiusM => Application.isMobilePlatform ? 6000f : 9000f;
        /// <summary>A whole canopy cell stands this tall (Charlotte's willow
        /// oaks and pines are 15-22 m; CityTrees' cards); a cell of fraction f,
        /// f x <see cref="CanopyRise"/> of it, at most all of it.</summary>
        const float CanopyM = 18f, CanopyRise = 1.6f;
        /// <summary>The woods' share of a vertex's colour runs from nothing at
        /// this canopy to all of it at <see cref="WoodsFull"/>.</summary>
        const float WoodsFrom = 0.05f, WoodsFull = 0.40f;
        /// <summary>The land sits this far under the DEM: where it meets the
        /// streamed ground at the squeeze's start, the streamed ground wins.</summary>
        const float SinkM = 1.5f;
        /// <summary>A wood's top seen from afar is its own shade under its
        /// crowns, darker than one tree's card.</summary>
        const float CanopyShade = 0.80f;
        /// <summary>Of the far woods, the share that are pines (CityTrees: 45%
        /// past 4.5 km of uptown, willow oaks inside).</summary>
        const float PineShare = 0.45f, PineShareOld = 0.15f, OldCityM = 4500f;
        /// <summary>THE FAR ROADS (the first shots: I-77 ran into an olive
        /// wall at the far plane). Every road of this class or more is a
        /// ribbon on the far land, sampled every <see cref="RoadStepM"/>, in
        /// the asphalt's measured colour, <see cref="RoadLeadM"/> nearer in
        /// the skyline's own depth than the land round it; and the land within
        /// <see cref="CorridorM"/> of it comes down to it, its trees cleared,
        /// as a real road's corridor is.</summary>
        const int RoadMinCls = 3, CorridorMinCls = 4;
        /// (Round 2: a 96 m corridor left the land between two 128 m nodes
        /// standing over a road in a cut, hiding it; it is fully down to the
        /// road within <see cref="CorridorInner"/> of the corridor now.)
        const float RoadStepM = 64f, RoadLeadM = 40f, RoadSinkM = 0.3f, CorridorM = 160f, CorridorInner = 0.6f;

        // ---- the far trees ----
        /// <summary>THE FAR TREES (owner, 2026-10-25: "yes, far tree layer" -
        /// by day the far land read as a smooth mottled plain). Real tree cards
        /// - the city's own atlas in today's dress, CityTrees' species, sizes
        /// and two crossed cards - stand on the canopy grid past the streamed
        /// ring out to this far: at most one a <see cref="TreeCellM"/> cell,
        /// <see cref="TreeThin"/> of the near planting's density, never on a
        /// road or in a building. Past it the land's own crowns (PSX/Lit
        /// _FarBump) are the woods, fading in over the last
        /// <see cref="TreeHandoverShare"/> of it.</summary>
        public static float TreeRadiusM => Application.isMobilePlatform ? 1400f : 2200f;
        const float TreeCellM = 32f, TreeThin = 0.5f, TreeRoadClearM = 4f, TreeLotClearM = 1.5f, TreeSinkM = 0.3f, TreeHandoverShare = 0.2f, TreeStandScale = 1.2f;
        static readonly byte[] OakCells = { 8, 9, 10, 11 }, BroadCells = { 0, 1, 2, 4, 6, 8, 9, 10, 11 }, PineCells = { 12, 13, 14 };

        // ---- the far lights ----
        /// <summary>The far lights round the eye (a phone: fewer).</summary>
        public static float LightRadiusM => Application.isMobilePlatform ? 5000f : 8000f;
        /// <summary>A far light's least half-size, in SHARP's (480-line)
        /// pixels (PSX/Halo _MinPixels): a spark, never a sub-pixel.</summary>
        const float LightPx = 0.65f;
        /// <summary>Past this a far light's dot dims (PSX/Halo _FarFall).</summary>
        const float LightFallM = 3500f;
        /// <summary>A far light is this much nearer in depth (PSX/Halo
        /// _FarLead): it shows through its own block's solid 128 m canopy
        /// (the first shots: every lamp in a wooded street was hidden), never
        /// through a hill.</summary>
        const float LightLeadM = 120f;
        /// <summary>The share of windows lit: a house's, a frontage box's, an
        /// office storey's run (the first cut drew 124k windows city-wide).</summary>
        const float HouseLit = 0.22f, BoxLit = 0.20f, OfficeLit = 0.16f, ShopLit = 0.45f;
        /// <summary>Halo half-sizes, as multiples of NightGlow.HaloSize.</summary>
        const float LampSize = 0.35f, WindowSize = 0.15f;
        /// <summary>A residential street's posts: every other one is drawn
        /// (they are dim and mostly behind the trees), at this brightness.</summary>
        const float StreetLampGain = 0.70f, ArterialLampGain = 0.90f;
        const float StoreyM = 3.6f;
        /// <summary>The windows' colours: ONE yellow a building type (owner,
        /// 2026-10-24: "No blue/white windows. Yellow like the NFS screenshot
        /// I shared", and no second shade): a house's PSX/Lit WIN_WARM, an
        /// office's FAC_WIN_WARM, a shopfront's WIN_SHOP - the same numbers.</summary>
        static readonly Color WinWarm = new Color(1.00f, 0.74f, 0.42f), WinOffice = new Color(1.00f, 0.64f, 0.24f), WinShop = new Color(1.00f, 0.80f, 0.48f);

        // ---- the glow ----
        /// <summary>The lit city's radius round uptown, the height of the lit
        /// air over it (metres), its strength (linear, at full night) and
        /// colour: a warm grey, the lamps' bulbs in the haze. (The first cut,
        /// 3.5 km / 900 m / 0.045, lifted the whole sky from 17 to 35-48.)</summary>
        const float GlowRadiusM = 2000f, GlowAirM = 300f, GlowStrength = 0.035f, GlowWet = 1.4f;
        static readonly Color GlowColour = new Color(1.00f, 0.84f, 0.68f);

        // ---- the edge ----
        /// <summary>The streamed world's edge fade starts this share of the far
        /// plane out, and would end <see cref="EdgeEndMul"/> far planes out:
        /// nothing fades before the far plane cuts it, and the squeeze's start
        /// (PSX/Lit sA) lands 30 m inside the far plane.</summary>
        const float EdgeStartShare = 0.94f, EdgeEndMul = 3f;

        static readonly int GlowId = Shader.PropertyToID("_PSXCityGlow");
        static readonly int GlowColId = Shader.PropertyToID("_PSXCityGlowCol");
        static readonly int FarWorldId = Shader.PropertyToID("_PSXFarWorld");

        CityMap map;
        System.Func<CityMeshes.Slot, Material> matFor;
        PSXGlobals globals;

        sealed class LandChunk
        {
            public Vector3[] v;
            public Vector3[] n;
            public int[] tri;              // chunk-local triangles
            public Vector2[] uv;           // the look: x the woods' share, y the pines' (PSX/Lit THE FAR LAND'S LOOK)
            public Vector3[] rv;           // the far roads' quads in it, four corners each
        }
        readonly Dictionary<long, LandChunk> land = new Dictionary<long, LandChunk>();
        Mesh landMesh;
        MeshRenderer[] landR;
        /// <summary>Per draw: ground, woods, roads.</summary>
        Material[] landMats;
        const int LandLooks = 2;
        Material srcGround, srcTrees, srcRoad;
        /// <summary>The far roads' segments (edge, point) by 256 m cell, for the corridors.</summary>
        readonly Dictionary<long, List<Vector2Int>> roadCells = new Dictionary<long, List<Vector2Int>>();
        /// <summary>The far roads' quads by land chunk.</summary>
        readonly Dictionary<long, List<Vector3>> roadQuads = new Dictionary<long, List<Vector3>>();
        const float RoadCellM = 256f;
        long landAt = long.MinValue;
        float landRadius = -1f;

        struct FarTree { public Vector3 foot; public float h, w, yaw; public byte cell; }
        readonly Dictionary<long, List<FarTree>> trees = new Dictionary<long, List<FarTree>>();
        Mesh treeMesh;
        MeshRenderer[] treeR;
        Material[] treeMats;
        Material srcTreeCards;
        long treeAt = long.MinValue;
        int treesShown;
        /// <summary>Every road's segments (edge, point) by 64 m cell, and every
        /// outline's index by its centre's 64 m cell: where no far tree stands.</summary>
        readonly Dictionary<long, List<Vector2Int>> anyRoadCells = new Dictionary<long, List<Vector2Int>>();
        readonly Dictionary<long, List<int>> lotCells = new Dictionary<long, List<int>>();
        const float ClearCellM = 64f;

        struct FarLight { public Vector3 p; public Color32 c; public float size; }
        readonly Dictionary<long, List<FarLight>> lights = new Dictionary<long, List<FarLight>>();
        Mesh lightMesh;
        MeshRenderer lightR;
        Material lightMat;
        long lightAt = long.MinValue;
        int lightsShown;

        public int LampCount { get; private set; }
        public int WindowCount { get; private set; }
        public string Summary => $"land {(landMesh != null ? landMesh.vertexCount : 0)} verts, trees {treesShown}, lights {lightsShown} of {LampCount} lamps + {WindowCount} windows";

        static long Key(int cx, int cz) => ((long)cx << 24) ^ (cz & 0xFFFFFF);
        static int ChunkOf(float v) => Mathf.FloorToInt(v / ChunkM);

        /// <summary>Build the far city under <paramref name="parent"/>:
        /// the lights' table now; the land as the eye goes. <paramref name="skylineOutlines"/>:
        /// the buildings the skyline draws (their windows are its own).</summary>
        public static CityFar Create(Transform parent, CityMap map, System.Func<CityMeshes.Slot, Material> matFor,
                                     ICollection<int> skylineOutlines, Dictionary<long, List<CityBuildings.B>> boxes)
        {
            if (map == null) return null;
            var go = new GameObject("CityFar") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var cf = go.AddComponent<CityFar>();
            cf.map = map;
            cf.matFor = matFor;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            cf.BuildLights(skylineOutlines, boxes);
            int quads = cf.BuildRoads();
            cf.BuildClearance();
            cf.CreateRenderers();
            Debug.Log($"[CityFar] {cf.LampCount} far lamps + {cf.WindowCount} far windows in {cf.lights.Count} chunks, {quads} far road quads ({sw.ElapsedMilliseconds} ms); land {CellM:0} m cells to {RadiusM / 1000f:0.#} km");
            return cf;
        }

        void CreateRenderers()
        {
            // the land: three renderers on one mesh, the skyline's three draws
            landMesh = new Mesh { name = "CityFarLand" };
            landMesh.MarkDynamic();
            landR = new MeshRenderer[CitySkyline.DrawCount];
            for (int d = 0; d < landR.Length; d++)
            {
                var dgo = new GameObject("CityFarLand " + (d + 1)) { hideFlags = HideFlags.DontSave };
                dgo.transform.SetParent(transform, false);
                dgo.AddComponent<MeshFilter>().sharedMesh = landMesh;
                landR[d] = Quiet(dgo.AddComponent<MeshRenderer>());
            }
            // the trees: three renderers on one mesh, the skyline's three draws
            treeMesh = new Mesh { name = "CityFarTrees", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            treeMesh.MarkDynamic();
            treeR = new MeshRenderer[CitySkyline.DrawCount];
            for (int d = 0; d < treeR.Length; d++)
            {
                var tgo = new GameObject("CityFarTrees " + (d + 1)) { hideFlags = HideFlags.DontSave };
                tgo.transform.SetParent(transform, false);
                tgo.AddComponent<MeshFilter>().sharedMesh = treeMesh;
                treeR[d] = Quiet(tgo.AddComponent<MeshRenderer>());
            }
            // the lights: one
            var sh = Shader.Find("PSX/Halo");
            if (sh != null)
            {
                lightMesh = new Mesh { name = "CityFarLights", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                lightMesh.MarkDynamic();
                var lgo = new GameObject("CityFarLights") { hideFlags = HideFlags.DontSave };
                lgo.transform.SetParent(transform, false);
                lgo.AddComponent<MeshFilter>().sharedMesh = lightMesh;
                lightR = Quiet(lgo.AddComponent<MeshRenderer>());
                lightMat = new Material(sh) { name = "CityFarLights", hideFlags = HideFlags.HideAndDontSave };
                lightMat.SetColor("_Color", Color.white);
                lightMat.SetFloat("_Size", NightGlow.HaloSize);
                lightMat.SetFloat("_Strength", 1f);
                lightMat.SetFloat("_HaloAmt", 0.25f);
                lightMat.SetFloat("_MinPixels", LightPx);
                lightMat.SetFloat("_OwnDepth", 1f);
                lightMat.SetFloat("_FarFall", LightFallM);
                lightMat.SetFloat("_FarLead", LightLeadM);
                lightMat.renderQueue = CitySkyline.FarLightsQueue;
                lightR.sharedMaterial = lightMat;
            }
        }

        static MeshRenderer Quiet(MeshRenderer r)
        {
            r.enabled = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            SunShadows.Exclude(r.gameObject);
            return r;
        }

        /// <summary>
        /// This camera's far city: the edge band, the glow, the land round the
        /// eye and the lights. Called BEFORE the skyline's own refresh (it
        /// reads the band this sets). The game calls it every frame; a tool
        /// that moves a camera by hand calls it before a shot.
        /// </summary>
        public void Refresh(Camera cam)
        {
            var weather = Seasons.CurrentWeather;
            bool on = cam != null && weather != Weather.Fog;
            if (globals == null) globals = Object.FindFirstObjectByType<PSXGlobals>();
            PushEdge(cam, on);
            PushGlow(on, weather);
            Shader.SetGlobalFloat(FarWorldId, on ? 1f : 0f);
            if (!on) { ShowLand(false); ShowTrees(false); if (lightR != null) lightR.enabled = false; return; }

            Vector3 eye = cam.transform.position;
            float far = cam.farClipPlane;
            // every squeezed point is inside the far plane round the eye
            var b = new Bounds(eye, Vector3.one * (far * 2.2f));

            bool landOk = EnsureLandMaterials();
            bool treesOk = EnsureTreeMaterials();
            long at = Key(ChunkOf(eye.x), ChunkOf(eye.z));
            // at most ONE rebuild a frame in the game (a new chunk ring is a
            // land, a forest and a light table: no single frame pays for all
            // three); a tool's shot takes them all at once
            int budget = Application.isPlaying ? 1 : 3;
            if (landOk && (at != landAt || landRadius != RadiusM) && budget-- > 0) RebuildLand(eye, at);
            if (treesOk && at != treeAt && budget-- > 0) RebuildTrees(eye, at);
            ShowLand(landOk && landMesh.vertexCount > 0);
            if (landR != null) foreach (var r in landR) r.bounds = b;
            ShowTrees(treesOk && treesShown > 0);
            if (treeR != null) foreach (var r in treeR) r.bounds = b;

            if (lightR != null)
            {
                if (at != lightAt && budget-- > 0) RebuildLights(eye, at);
                bool lit = NightGlow.On && lightsShown > 0;
                lightR.enabled = lit;
                lightR.bounds = b;
            }
        }

        void ShowLand(bool on)
        {
            if (landR == null) return;
            foreach (var r in landR) if (r.enabled != on) r.enabled = on;
        }

        void ShowTrees(bool on)
        {
            if (treeR == null) return;
            foreach (var r in treeR) if (r.enabled != on) r.enabled = on;
        }

        // ------------------------------------------------------------------
        //  THE EDGE AND THE GLOW
        // ------------------------------------------------------------------
        void PushEdge(Camera cam, bool on)
        {
            if (!on || globals == null || cam == null) return;
            float far = cam.farClipPlane;
            float n = far * EdgeStartShare, f = far * EdgeEndMul;
            if (Mathf.Abs(globals.fogNear - n) > 0.01f || Mathf.Abs(globals.fogFar - f) > 0.01f)
            {
                globals.fogNear = n;
                globals.fogFar = f;
                globals.Apply();
            }
        }

        void PushGlow(bool on, Weather weather)
        {
            float night = globals != null ? Mathf.Clamp01(globals.night) : 0f;
            float w = on ? GlowStrength * night * (weather == Weather.Rain || weather == Weather.Snow ? GlowWet : 1f) : 0f;
            Shader.SetGlobalVector(GlowId, new Vector4(map.uptown.x, GlowRadiusM, map.uptown.y, w));
            Shader.SetGlobalVector(GlowColId, new Vector4(GlowColour.r, GlowColour.g, GlowColour.b, GlowAirM));
        }

        void OnDisable()
        {
            // another venue's sky has no city in it
            Shader.SetGlobalVector(GlowId, Vector4.zero);
            Shader.SetGlobalFloat(FarWorldId, 0f);
        }

        // ------------------------------------------------------------------
        //  THE FAR LAND
        // ------------------------------------------------------------------
        /// <summary>The land's looks for today's dress: copies of the
        /// streamed ground's material (opaque, lit as the world is), white,
        /// with the colours MEASURED off the grass, the trees' atlas (its
        /// broadleaf cells and its pines) and the asphalt handed to PSX/Lit's
        /// far-land tint (_FarGround/_FarBroad/_FarPine/_FarRoad), which each
        /// vertex's UV mixes. Two materials a draw: the land, and the roads
        /// (with their lead). False with no material or texture to measure (no
        /// land is drawn then).</summary>
        bool EnsureLandMaterials()
        {
            var g = matFor != null ? matFor(CityMeshes.Slot.Ground) : null;
            var t = CityTrees.MaterialFor(CityTrees.DressNow());
            var rd = matFor != null ? matFor(CityMeshes.SlotOf(roadProfile, CityMeshes.Surface.AsphaltOld)) : null;
            if (g == null || t == null || g.mainTexture == null || t.mainTexture == null) return false;
            if (rd == null || rd.mainTexture == null) rd = g;
            if (g == srcGround && t == srcTrees && rd == srcRoad && landMats != null) return true;
            srcGround = g; srcTrees = t; srcRoad = rd;
            Color grass = MeanColour(g.mainTexture, 1, 0, 0);
            Color broad = MeanColour(t.mainTexture, 4, 0, 11) * CanopyShade;
            Color pine = MeanColour(t.mainTexture, 4, 12, 14) * CanopyShade;
            Color asphalt = MeanColour(rd.mainTexture, 1, 0, 0) * (rd.HasProperty("_Color") ? rd.color : Color.white);
            broad.a = 1f; pine.a = 1f; grass.a = 1f; asphalt.a = 1f;
            if (landMats != null) foreach (var m in landMats) Kill(m);
            landMats = new Material[CitySkyline.DrawCount * LandLooks];
            for (int d = 0; d < CitySkyline.DrawCount; d++)
            {
                landMats[d * LandLooks] = FarMaterial(g, d, "land", 0f, grass, broad, pine, asphalt);
                landMats[d * LandLooks + 1] = FarMaterial(g, d, "roads", RoadLeadM, grass, broad, pine, asphalt);
                landR[d].sharedMaterials = new[] { landMats[d * LandLooks], landMats[d * LandLooks + 1] };
            }
            Debug.Log($"[CityFar] land colours for {t.name}: grass {Hex(grass)}, broadleaf {Hex(broad)}, pine {Hex(pine)}, roads {Hex(asphalt)} ({rd.name})");
            return true;
        }

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        Material FarMaterial(Material src, int draw, string what, float lead, Color grass, Color broad, Color pine, Color asphalt)
        {
            var m = new Material(src) { name = "CityFarLand " + what + " " + (draw + 1), hideFlags = HideFlags.DontSave };
            // the plain PSX/Lit variant: no baked sky in the vertex colour, no atlas
            m.shaderKeywords = System.Array.Empty<string>();
            m.SetTexture("_MainTex", Texture2D.whiteTexture);
            m.SetTextureScale("_MainTex", Vector2.one);
            m.SetTextureOffset("_MainTex", Vector2.zero);
            m.SetColor("_Color", Color.white);
            // LINEAR, by hand: SetColor converts only a colour the shader
            // declares as a property, and these are bare uniforms (round 6: the
            // far land came out sand, its sRGB codes read as light)
            m.SetVector("_FarGround", (Vector4)grass.linear);
            m.SetVector("_FarBroad", (Vector4)broad.linear);
            m.SetVector("_FarPine", (Vector4)pine.linear);
            m.SetVector("_FarRoad", (Vector4)asphalt.linear);
            m.SetFloat("_Cutoff", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_Wet", 0f);
            m.SetFloat("_NightWin", 0f);
            m.SetFloat("_Emission", 0f);
            m.SetFloat("_Shore", 0f);
            m.SetFloat("_MainTexRaw", 0f);
            CitySkyline.SetDraw(m, draw);
            // 2: the land (its tint and its woods' crowns); 1 is a far tree card
            m.SetFloat("_SkyFarLand", 2f);
            m.SetFloat("_SkyLeadM", lead);
            m.SetVector("_FarBump", new Vector4(CanopyM, TreeRadiusM * (1f - TreeHandoverShare), TreeRadiusM, 0f));
            return m;
        }

        /// <summary>
        /// The mean colour of a texture - over atlas cells <paramref name="from"/>..
        /// <paramref name="to"/> of a <paramref name="grid"/> x grid atlas
        /// (grid 1: the whole of it), weighted by alpha, in linear light -
        /// read back off the GPU (the textures are not CPU-readable in a
        /// build). A texture the shader decodes itself (PSXTexDecode: linear
        /// data holding sRGB codes) is copied through untouched; any other is
        /// encoded back to sRGB by the target. Either way the bytes read are
        /// the painted sRGB codes. Returned as an sRGB colour, which a material
        /// colour property takes.
        /// </summary>
        static Color MeanColour(Texture tex, int grid, int from, int to)
        {
            const int S = 128;
            bool raw = PSXTexDecode.IsRaw(tex);
            var rt = RenderTexture.GetTemporary(S, S, 0, RenderTextureFormat.ARGB32,
                                                raw ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, S, S), 0, 0, false);
            t.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            Kill(t);
            grid = Mathf.Max(1, grid);
            int cs = S / grid;
            double r = 0, g = 0, bl = 0, w = 0;
            for (int cell = from; cell <= Mathf.Max(from, to); cell++)
            {
                int col = cell % grid, row = cell / grid;
                for (int y = row * cs; y < (row + 1) * cs; y++)
                for (int x = col * cs; x < (col + 1) * cs; x++)
                {
                    var p = px[y * S + x];
                    float a = p.a / 255f;
                    if (a < 0.5f) continue;
                    r += Mathf.GammaToLinearSpace(p.r / 255f) * a;
                    g += Mathf.GammaToLinearSpace(p.g / 255f) * a;
                    bl += Mathf.GammaToLinearSpace(p.b / 255f) * a;
                    w += a;
                }
            }
            if (w <= 0.0) return new Color(0.2f, 0.22f, 0.15f);
            return new Color(Mathf.LinearToGammaSpace((float)(r / w)), Mathf.LinearToGammaSpace((float)(g / w)),
                             Mathf.LinearToGammaSpace((float)(bl / w)), 1f);
        }

        /// <summary>The land's height at a point: the DEM less the sink, and
        /// the canopy standing on it - brought down to a far road within
        /// <see cref="CorridorM"/> and its trees cleared there.
        /// <paramref name="canopy"/> its fraction.</summary>
        float LandY(float x, float z, out float canopy)
        {
            canopy = CityCanopy.Fraction(x, z);
            float ground = CityElevation.BaseY(x, z) - SinkM;
            if (NearestRoad(x, z, out float d, out float roadY))
            {
                // the land down to it over the corridor; its trees cleared
                // over the inner part only (round 4: clearing all 160 m of
                // every road left the city's far land a bare field)
                float kg = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CorridorM * CorridorInner, CorridorM, d));
                ground = Mathf.Lerp(Mathf.Min(ground, roadY - 1f), ground, kg);
                canopy *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CorridorM * 0.25f, CorridorM * CorridorInner, d));
            }
            // the crowns stand on it in the shader (_FarBump), past the far trees
            return ground;
        }

        /// <summary>The nearest far road within <see cref="CorridorM"/>: its
        /// plan distance and its height there.</summary>
        bool NearestRoad(float x, float z, out float dist, out float y)
        {
            dist = float.MaxValue; y = 0f;
            var p = new Vector2(x, z);
            int ix = Mathf.FloorToInt(x / RoadCellM), iz = Mathf.FloorToInt(z / RoadCellM);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!roadCells.TryGetValue(Key(ix + dx, iz + dz), out var list)) continue;
                foreach (var sg in list)
                {
                    var e = map.edges[sg.x];
                    var a = e.pts[sg.y]; var b = e.pts[sg.y + 1];
                    var ab = b - a;
                    float len2 = ab.sqrMagnitude;
                    float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                    float dd = Vector2.Distance(p, a + ab * t);
                    if (dd < dist) { dist = dd; y = e.YAt(Mathf.Lerp(e.s[sg.y], e.s[sg.y + 1], t)); }
                }
            }
            return dist < CorridorM;
        }

        int roadProfile;

        /// <summary>THE FAR ROADS (<see cref="RoadMinCls"/>): the segments by
        /// cell for the corridors, and each road's ribbon - every
        /// <see cref="RoadStepM"/>, its own width, just under its own height -
        /// as quads by land chunk. Returns the quads made.</summary>
        int BuildRoads()
        {
            int quads = 0;
            bool profiled = false;
            foreach (var e in map.edges)
            {
                if (e.cls < RoadMinCls || e.tunnel || e.link || e.pts == null || e.pts.Length < 2) continue;
                if (!profiled && e.cls >= 4) { roadProfile = e.profile; profiled = true; }
                // the corridors: freeways and trunks only (their cuts and fills)
                for (int i = 0; e.cls >= CorridorMinCls && i + 1 < e.pts.Length; i++)
                {
                    // every cell the segment's box touches
                    var a = e.pts[i]; var b = e.pts[i + 1];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / RoadCellM), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / RoadCellM);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / RoadCellM), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / RoadCellM);
                    for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        long k = Key(cx, cz);
                        if (!roadCells.TryGetValue(k, out var list)) { list = new List<Vector2Int>(16); roadCells[k] = list; }
                        list.Add(new Vector2Int(e.index, i));
                    }
                }
                float hw = Mathf.Max(e.width, 7f) * 0.5f;
                int steps = Mathf.Max(1, Mathf.CeilToInt(e.length / RoadStepM));
                float step = e.length / steps;
                Vector2 pa = e.PointAt(0f);
                float ya = e.YAt(0f) - RoadSinkM;
                for (int k = 1; k <= steps; k++)
                {
                    float s = k * step;
                    var pb = e.PointAt(s);
                    float yb = e.YAt(s) - RoadSinkM;
                    var d = pb - pa;
                    if (d.sqrMagnitude > 1e-4f)
                    {
                        d.Normalize();
                        var n = new Vector2(-d.y, d.x) * hw;
                        var mid = (pa + pb) * 0.5f;
                        long ck = Key(ChunkOf(mid.x), ChunkOf(mid.y));
                        if (!roadQuads.TryGetValue(ck, out var q)) { q = new List<Vector3>(256); roadQuads[ck] = q; }
                        q.Add(new Vector3(pa.x - n.x, ya, pa.y - n.y));
                        q.Add(new Vector3(pa.x + n.x, ya, pa.y + n.y));
                        q.Add(new Vector3(pb.x + n.x, yb, pb.y + n.y));
                        q.Add(new Vector3(pb.x - n.x, yb, pb.y - n.y));
                        quads++;
                    }
                    pa = pb; ya = yb;
                }
            }
            return quads;
        }

        LandChunk BuildLandChunk(int cx, int cz)
        {
            const int N = ChunkCells + 1, B = N + 2;
            var c = new LandChunk { v = new Vector3[N * N], n = new Vector3[N * N] };
            var f = new float[N * N];
            float x0 = cx * ChunkM, z0 = cz * ChunkM;
            // the heights with a one-cell border, so the normals see the corridors too
            var h = new float[B * B];
            for (int j = 0; j < B; j++)
            for (int i = 0; i < B; i++)
            {
                h[j * B + i] = LandY(x0 + (i - 1) * CellM, z0 + (j - 1) * CellM, out float fr);
                if (i >= 1 && i <= N && j >= 1 && j <= N) f[(j - 1) * N + (i - 1)] = fr;
            }
            for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                int k = (j + 1) * B + (i + 1);
                c.v[j * N + i] = new Vector3(x0 + i * CellM, h[k], z0 + j * CellM);
                float hx = h[k + 1] - h[k - 1], hz = h[k + B] - h[k - B];
                c.n[j * N + i] = new Vector3(-hx / (2f * CellM), 1f, -hz / (2f * CellM)).normalized;
            }
            c.rv = roadQuads.TryGetValue(Key(cx, cz), out var rq) ? rq.ToArray() : System.Array.Empty<Vector3>();
            var tl = new List<int>(ChunkCells * ChunkCells * 6);
            c.uv = new Vector2[N * N];
            for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float x = x0 + i * CellM, z = z0 + j * CellM;
                float woods = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WoodsFrom, WoodsFull, f[j * N + i]));
                // pines: willow oaks in the old city, nearly half pine past it
                // (CityTrees), mottled node by node
                float pines = Vector2.Distance(new Vector2(x, z), map.uptown) < OldCityM ? PineShareOld : PineShare;
                float hp = Hash(Mathf.RoundToInt(x / CellM), Mathf.RoundToInt(z / CellM) + 7919);
                c.uv[j * N + i] = new Vector2(woods, hp < pines ? 0.85f : 0.1f * hp);
            }
            for (int j = 0; j < ChunkCells; j++)
            for (int i = 0; i < ChunkCells; i++)
            {
                int a = j * N + i, bq = a + 1, cq = a + N, dq = cq + 1;
                tl.Add(a); tl.Add(cq); tl.Add(dq);
                tl.Add(a); tl.Add(dq); tl.Add(bq);
            }
            c.tri = tl.ToArray();
            return c;
        }

        readonly List<Vector3> lv = new List<Vector3>(32768), ln = new List<Vector3>(32768);
        readonly List<Vector2> luv = new List<Vector2>(32768);
        readonly List<int> lg = new List<int>(98304), lr = new List<int>(32768);

        void RebuildLand(Vector3 eye, long at)
        {
            landAt = at;
            landRadius = RadiusM;
            float R = RadiusM;
            int ecx = ChunkOf(eye.x), ecz = ChunkOf(eye.z);
            int r = Mathf.CeilToInt(R / ChunkM) + 1;
            lv.Clear(); ln.Clear(); luv.Clear(); lg.Clear(); lr.Clear();
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = ecx + dx, cz = ecz + dz;
                // the chunk's nearest point to the eye, in plan
                float nx = Mathf.Clamp(eye.x, cx * ChunkM, (cx + 1) * ChunkM), nz = Mathf.Clamp(eye.z, cz * ChunkM, (cz + 1) * ChunkM);
                if ((nx - eye.x) * (nx - eye.x) + (nz - eye.z) * (nz - eye.z) > R * R) continue;
                long k = Key(cx, cz);
                if (!land.TryGetValue(k, out var c)) { c = BuildLandChunk(cx, cz); land[k] = c; }
                int v0 = lv.Count;
                lv.AddRange(c.v); ln.AddRange(c.n);
                luv.AddRange(c.uv);
                foreach (int t in c.tri) lg.Add(v0 + t);
                // the roads: flat quads, lit as faces looking up
                for (int q = 0; q + 3 < c.rv.Length; q += 4)
                {
                    int r0 = lv.Count;
                    for (int m = 0; m < 4; m++) { lv.Add(c.rv[q + m]); ln.Add(Vector3.up); luv.Add(new Vector2(2f, 0f)); }
                    lr.Add(r0); lr.Add(r0 + 1); lr.Add(r0 + 2); lr.Add(r0); lr.Add(r0 + 2); lr.Add(r0 + 3);
                }
            }
            landMesh.Clear();
            landMesh.indexFormat = lv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            landMesh.SetVertices(lv);
            landMesh.SetNormals(ln);
            landMesh.SetUVs(0, luv);
            landMesh.subMeshCount = LandLooks;
            landMesh.SetTriangles(lg, 0, false);
            landMesh.SetTriangles(lr, 1, false);
            landMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------
        //  THE FAR TREES
        // ------------------------------------------------------------------
        /// <summary>The far trees' looks: three copies of today's tree material
        /// (CityTrees.MaterialFor, the dress the near trees wear), one a draw
        /// of the skyline's three, clipped inside the squeeze's start. False
        /// without one.</summary>
        bool EnsureTreeMaterials()
        {
            var t = CityTrees.MaterialFor(CityTrees.DressNow());
            if (t == null) return false;
            if (t == srcTreeCards && treeMats != null) return true;
            srcTreeCards = t;
            if (treeMats != null) foreach (var m in treeMats) Kill(m);
            treeMats = new Material[CitySkyline.DrawCount];
            for (int d = 0; d < treeMats.Length; d++)
            {
                var m = new Material(t) { name = "CityFarTrees " + (d + 1), hideFlags = HideFlags.DontSave };
                CitySkyline.SetDraw(m, d);
                m.SetFloat("_SkyFarLand", 1f);
                treeMats[d] = m;
                treeR[d].sharedMaterial = m;
            }
            return true;
        }

        static long ClearKey(float x, float z) => Key(Mathf.FloorToInt(x / ClearCellM), Mathf.FloorToInt(z / ClearCellM));

        /// <summary>Where no far tree stands: every road's segments and every
        /// outline, bucketed by 64 m cell.</summary>
        void BuildClearance()
        {
            foreach (var e in map.edges)
            {
                if (e.tunnel || e.pts == null || e.pts.Length < 2) continue;
                for (int i = 0; i + 1 < e.pts.Length; i++)
                {
                    var a = e.pts[i]; var b = e.pts[i + 1];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / ClearCellM), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / ClearCellM);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / ClearCellM), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / ClearCellM);
                    for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        long k = Key(cx, cz);
                        if (!anyRoadCells.TryGetValue(k, out var list)) { list = new List<Vector2Int>(8); anyRoadCells[k] = list; }
                        list.Add(new Vector2Int(e.index, i));
                    }
                }
            }
            var fs = map.footprints;
            for (int i = 0; i < fs.Length; i++)
            {
                if (fs[i].part) continue;
                long k = ClearKey(fs[i].centre.x, fs[i].centre.y);
                if (!lotCells.TryGetValue(k, out var list)) { list = new List<int>(8); lotCells[k] = list; }
                list.Add(i);
            }
        }

        /// <summary>A far tree may not stand here: on or beside a road's
        /// pavement, or inside a building's box.</summary>
        bool TreeBlocked(Vector2 p)
        {
            int ix = Mathf.FloorToInt(p.x / ClearCellM), iz = Mathf.FloorToInt(p.y / ClearCellM);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                long k = Key(ix + dx, iz + dz);
                if (anyRoadCells.TryGetValue(k, out var segs))
                    foreach (var sg in segs)
                    {
                        var e = map.edges[sg.x];
                        var a = e.pts[sg.y]; var ab = e.pts[sg.y + 1] - a;
                        float len2 = ab.sqrMagnitude;
                        float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                        // a freeway's or trunk's corridor is clear as the land's
                        // crowns are (LandY), not only its pavement
                        float clear = e.cls >= CorridorMinCls ? Mathf.Max(e.width * 0.5f + TreeRoadClearM, CorridorM * 0.25f) : e.width * 0.5f + TreeRoadClearM;
                        if (Vector2.Distance(p, a + ab * t) < clear) return true;
                    }
                if (lotCells.TryGetValue(k, out var lots))
                    foreach (int i in lots)
                    {
                        var f = map.footprints[i];
                        var d = p - f.centre;
                        var v = new Vector2(-f.u.y, f.u.x);
                        if (Mathf.Abs(Vector2.Dot(d, f.u)) < f.hu + TreeLotClearM && Mathf.Abs(Vector2.Dot(d, v)) < f.hv + TreeLotClearM) return true;
                    }
            }
            return false;
        }

        /// <summary>One chunk's far trees: a jittered point a
        /// <see cref="TreeCellM"/> cell, kept with the canopy's odds (thinned),
        /// its species, height and width as CityTrees picks them.</summary>
        List<FarTree> BuildTreeChunk(int cx, int cz)
        {
            var list = new List<FarTree>(1024);
            int n = Mathf.RoundToInt(ChunkM / TreeCellM);
            float x0 = cx * ChunkM, z0 = cz * ChunkM;
            float odds = TreeCellM * TreeCellM / CityTrees.CrownM2 * TreeThin;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int gx = cx * n + i, gz = cz * n + j;
                var p = new Vector2(x0 + (i + 0.15f + 0.7f * Hash(gx, gz * 3 + 1)) * TreeCellM,
                                    z0 + (j + 0.15f + 0.7f * Hash(gx * 7 + 3, gz)) * TreeCellM);
                float f = CityCanopy.Fraction(p.x, p.y);
                if (f < 0.03f || Hash(gx + 101, gz + 57) > Mathf.Min(0.95f, f * odds)) continue;
                if (TreeBlocked(p)) continue;
                bool old = Vector2.Distance(p, map.uptown) < OldCityM;
                bool pine = Hash(gx + 13, gz + 29) < (old ? PineShareOld : PineShare);
                float hs = Hash(gx + 211, gz + 7);
                var cells = pine ? PineCells : old ? OakCells : BroadCells;
                byte cell = cells[Mathf.Min(cells.Length - 1, (int)(Hash(gx + 5, gz + 401) * cells.Length))];
                // a little over the near cards' sizes: a far tree stands for its stand
                float h = (pine ? Mathf.Lerp(15f, 22f, hs) : Mathf.Lerp(11.5f, 16f, hs)) * TreeStandScale;
                list.Add(new FarTree
                {
                    foot = new Vector3(p.x, CityElevation.BaseY(p.x, p.y) - TreeSinkM, p.y),
                    h = h,
                    w = pine ? h * 0.62f : h,
                    yaw = Hash(gx + 17, gz + 919) * 180f,
                    cell = cell,
                });
            }
            return list;
        }

        readonly List<Vector3> tv = new List<Vector3>(98304), tn = new List<Vector3>(98304);
        readonly List<Vector2> tuv = new List<Vector2>(98304);
        readonly List<int> tt = new List<int>(147456);

        void RebuildTrees(Vector3 eye, long at)
        {
            treeAt = at;
            float R = TreeRadiusM;
            int ecx = ChunkOf(eye.x), ecz = ChunkOf(eye.z);
            int r = Mathf.CeilToInt(R / ChunkM) + 1;
            tv.Clear(); tn.Clear(); tuv.Clear(); tt.Clear();
            const float pad = 1.5f / 512f;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = ecx + dx, cz = ecz + dz;
                float nx = Mathf.Clamp(eye.x, cx * ChunkM, (cx + 1) * ChunkM), nz = Mathf.Clamp(eye.z, cz * ChunkM, (cz + 1) * ChunkM);
                if ((nx - eye.x) * (nx - eye.x) + (nz - eye.z) * (nz - eye.z) > R * R) continue;
                long k = Key(cx, cz);
                if (!trees.TryGetValue(k, out var list)) { list = BuildTreeChunk(cx, cz); trees[k] = list; }
                foreach (var t in list)
                {
                    // CityTrees.BuildMesh's cards, the same atlas cells
                    int col = t.cell % 4, row = t.cell / 4;
                    float u0 = col * 0.25f + pad, u1 = (col + 1) * 0.25f - pad;
                    float v0 = row * 0.25f + pad, v1 = (row + 1) * 0.25f - pad;
                    for (int q = 0; q < 2; q++)
                    {
                        float a = (t.yaw + q * 90f) * Mathf.Deg2Rad;
                        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                        var half = new Vector3(ca, 0f, sa) * (t.w * 0.5f);
                        int v = tv.Count;
                        tv.Add(t.foot - half); tv.Add(t.foot - half + Vector3.up * t.h);
                        tv.Add(t.foot + half + Vector3.up * t.h); tv.Add(t.foot + half);
                        var nrm = new Vector3(sa, 0f, -ca);
                        tn.Add(nrm); tn.Add(nrm); tn.Add(nrm); tn.Add(nrm);
                        tuv.Add(new Vector2(u0, v0)); tuv.Add(new Vector2(u0, v1));
                        tuv.Add(new Vector2(u1, v1)); tuv.Add(new Vector2(u1, v0));
                        tt.Add(v); tt.Add(v + 1); tt.Add(v + 2);
                        tt.Add(v); tt.Add(v + 2); tt.Add(v + 3);
                    }
                }
            }
            treesShown = tv.Count / 8;
            treeMesh.Clear();
            treeMesh.SetVertices(tv);
            treeMesh.SetNormals(tn);
            treeMesh.SetUVs(0, tuv);
            treeMesh.SetTriangles(tt, 0, false);
            treeMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------
        //  THE FAR LIGHTS
        // ------------------------------------------------------------------
        void AddLight(Vector3 p, Color c, float gain, float size)
        {
            long k = Key(ChunkOf(p.x), ChunkOf(p.z));
            if (!lights.TryGetValue(k, out var list)) { list = new List<FarLight>(256); lights[k] = list; }
            list.Add(new FarLight
            {
                p = p,
                c = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(c.r * gain) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g * gain) * 255f),
                                (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b * gain) * 255f), 255),
                size = size,
            });
        }

        static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 2654435761u ^ (uint)b * 2246822519u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        void BuildLights(ICollection<int> skylineOutlines, Dictionary<long, List<CityBuildings.B>> boxes)
        {
            // the street lamps: the tiles' own plan, post for post
            foreach (var e in map.edges)
            {
                if (!CityMeshes.FarLampPlan(map, e, out float gap, out float height, out bool outsideOnly)) continue;
                bool street = e.cls <= 1;
                float gain = street ? StreetLampGain : e.cls >= 4 ? 1f : ArterialLampGain;
                int count = Mathf.Max(1, Mathf.RoundToInt(e.length / gap));
                float step = e.length / count;
                float lateral = e.width * 0.5f + 1.0f;
                for (int k = 0; k < count; k++)
                {
                    if (street && (k & 1) == 1) continue;
                    float s = (k + 0.5f) * step;
                    var p = e.PointAt(s);
                    var t = e.TangentAt(s);
                    if (t.sqrMagnitude < 1e-6f) continue;
                    t.Normalize();
                    var right = new Vector2(t.y, -t.x);
                    float side = outsideOnly ? 1f : ((k + e.index) & 1) == 0 ? -1f : 1f;
                    var q = p + right * (side * lateral);
                    AddLight(new Vector3(q.x, e.YAt(s) + height, q.y), StreetLights.Bulb, gain, LampSize);
                    LampCount++;
                }
            }

            // the lit windows of what the skyline does not draw: the real
            // footprints (an outline drawn by its parts still has windows)...
            var fs = map.footprints;
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                if (f.part || f.pts == null || f.pts.Length < 3) continue;
                if (skylineOutlines != null && skylineOutlines.Contains(i)) continue;
                float ground = CityElevation.BaseY(f.centre.x, f.centre.y);
                if (f.style == 3 || f.gable)
                {
                    // a house: a lamp in one window, some nights
                    if (Hash(i, 1) > HouseLit) continue;
                    var at = OnOutline(f.pts, Hash(i, 2), Hash(i, 3));
                    AddLight(new Vector3(at.x, ground + (Hash(i, 4) < 0.6f ? 1.8f : 4.6f), at.y), WinWarm, 0.60f, WindowSize);
                    WindowCount++;
                    continue;
                }
                bool shop = f.style == 4;
                int storeys = Mathf.Clamp(Mathf.RoundToInt(f.h / StoreyM), 1, 16);
                float perim = 0f;
                for (int k = 0; k < f.pts.Length; k++) perim += Vector2.Distance(f.pts[k], f.pts[(k + 1) % f.pts.Length]);
                int per = Mathf.Clamp(Mathf.RoundToInt(perim / 40f), 1, 4);
                for (int st = 0; st < storeys; st++)
                for (int u = 0; u < per; u++)
                {
                    int salt = st * 8 + u;
                    float pLit = shop && st == 0 ? ShopLit : OfficeLit;
                    if (Hash(i, 100 + salt) > pLit) continue;
                    var at = OnOutline(f.pts, Hash(i, 200 + salt), Hash(i, 300 + salt));
                    var col = shop && st == 0 ? WinShop : WinOffice;
                    AddLight(new Vector3(at.x, ground + (st + 0.45f) * StoreyM, at.y), col, shop && st == 0 ? 0.70f : 0.55f, WindowSize);
                    WindowCount++;
                }
            }

            // ...and the frontage boxes and houses the tiles place (CityBuildings)
            if (boxes != null)
            {
                int n = 0;
                foreach (var kv in boxes)
                foreach (var bx in kv.Value)
                {
                    n++;
                    if (bx.kind != 0 && !bx.gable) continue;   // a prop model: its own look
                    float pLit = bx.gable ? HouseLit : BoxLit;
                    if (Hash(n, 7) > pLit) continue;
                    float ang = Hash(n, 8) * Mathf.PI * 2f;
                    float reach = 0.5f * Mathf.Max(bx.w, bx.d);
                    var at = bx.pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * reach;
                    float ground = CityElevation.BaseY(bx.pos.x, bx.pos.y);
                    bool upstairs = bx.h > 5f && Hash(n, 9) < 0.4f;
                    AddLight(new Vector3(at.x, ground + (upstairs ? 4.6f : 1.8f), at.y), bx.gable ? WinWarm : WinShop, bx.gable ? 0.60f : 0.70f, WindowSize);
                    WindowCount++;
                }
            }
        }

        /// <summary>A point on a polygon's outline, edge chosen by
        /// <paramref name="he"/> (weighted by length), along it by
        /// <paramref name="ht"/>, a little outside it.</summary>
        static Vector2 OnOutline(Vector2[] pts, float he, float ht)
        {
            int n = pts.Length;
            float perim = 0f;
            for (int k = 0; k < n; k++) perim += Vector2.Distance(pts[k], pts[(k + 1) % n]);
            float want = he * perim;
            for (int k = 0; k < n; k++)
            {
                var a = pts[k]; var b = pts[(k + 1) % n];
                float len = Vector2.Distance(a, b);
                if (want <= len || k == n - 1)
                {
                    var d = len > 1e-4f ? (b - a) / len : Vector2.right;
                    // outward for a counter-clockwise outline: the edge turned clockwise
                    return Vector2.Lerp(a, b, Mathf.Lerp(0.1f, 0.9f, ht)) + new Vector2(d.y, -d.x) * 0.4f;
                }
                want -= len;
            }
            return pts[0];
        }

        readonly List<Vector3> gv = new List<Vector3>(65536);
        readonly List<Vector2> gu0 = new List<Vector2>(65536), gu1 = new List<Vector2>(65536);
        readonly List<Color32> gc = new List<Color32>(65536);
        readonly List<int> gt = new List<int>(98304);

        void RebuildLights(Vector3 eye, long at)
        {
            lightAt = at;
            float R = LightRadiusM;
            int ecx = ChunkOf(eye.x), ecz = ChunkOf(eye.z);
            int r = Mathf.CeilToInt(R / ChunkM) + 1;
            gv.Clear(); gu0.Clear(); gu1.Clear(); gc.Clear(); gt.Clear();
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = ecx + dx, cz = ecz + dz;
                float nx = Mathf.Clamp(eye.x, cx * ChunkM, (cx + 1) * ChunkM), nz = Mathf.Clamp(eye.z, cz * ChunkM, (cz + 1) * ChunkM);
                if ((nx - eye.x) * (nx - eye.x) + (nz - eye.z) * (nz - eye.z) > R * R) continue;
                if (!lights.TryGetValue(Key(cx, cz), out var list)) continue;
                foreach (var l in list)
                {
                    int v = gv.Count;
                    gv.Add(l.p); gv.Add(l.p); gv.Add(l.p); gv.Add(l.p);
                    gu0.Add(new Vector2(0f, 0f)); gu0.Add(new Vector2(1f, 0f)); gu0.Add(new Vector2(1f, 1f)); gu0.Add(new Vector2(0f, 1f));
                    var u1 = new Vector2(l.size, -0.5f);
                    gu1.Add(u1); gu1.Add(u1); gu1.Add(u1); gu1.Add(u1);
                    gc.Add(l.c); gc.Add(l.c); gc.Add(l.c); gc.Add(l.c);
                    gt.Add(v); gt.Add(v + 1); gt.Add(v + 2); gt.Add(v); gt.Add(v + 2); gt.Add(v + 3);
                }
            }
            lightsShown = gv.Count / 4;
            lightMesh.Clear();
            lightMesh.SetVertices(gv);
            lightMesh.SetUVs(0, gu0);
            lightMesh.SetUVs(1, gu1);
            lightMesh.SetColors(gc);
            lightMesh.SetTriangles(gt, 0, false);
            lightMesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (landMesh != null) Kill(landMesh);
            if (treeMesh != null) Kill(treeMesh);
            if (treeMats != null) foreach (var m in treeMats) if (m != null) Kill(m);
            if (lightMesh != null) Kill(lightMesh);
            if (lightMat != null) Kill(lightMat);
            if (landMats != null) foreach (var m in landMats) if (m != null) Kill(m);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
