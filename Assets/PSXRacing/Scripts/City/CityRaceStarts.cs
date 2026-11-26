using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using PSXRacing.LifeSim;

namespace PSXRacing.City
{
    /// <summary>
    /// DRIVE TO THE START (owner, 2026-10-19: "Or you can drive to the starting
    /// line to initiate", with NFS 2015's event gate as the reference: the
    /// race's name floating over the road at its line, START EVENT when you
    /// pull up). Every city race's start line in Charlotte's free roam carries
    /// a gate - a light band across the road, a post at each kerb, the name
    /// and the numbers above the lanes - and the radar marks where they are
    /// (CityMinimap). Roll up to one slowly and the HUD offers the race; the
    /// press loads it with the car you are in, the hour it is and the
    /// weather you picked, exactly as the race card's START would.
    ///
    /// CITY edition only: MAIN's races are booked into a career's calendar
    /// and a gate in the street would be a way round the diary. A deck run
    /// has no gate yet (its line is inside the deck).
    ///
    /// Built from glowing quads in PSX/Glow (the lamps' shader) and the menu
    /// font's own glyphs, so nothing here needs an asset: a few dozen quads
    /// per gate.
    ///
    /// GROWN OUT OF THE DISTANCE (roadevents, 2026-11-26; the owner's rule:
    /// nothing may switch on as you drive, lights only by a fade). A gate's
    /// light rises from nothing at <see cref="FadeFarM"/> to full at
    /// <see cref="FadeNearM"/> of the eye, every frame: PSX/Glow is additive,
    /// so its _Color scaled through a property block on each of the gate's
    /// renderers (the materials are shared) scales the light it adds. The
    /// root is switched off only while that light is 0, where the switch
    /// draws nothing. PSX_GATEFADE=0 / the LAB row RACE GATES FADE IN OLD:
    /// the old switch at 450 m, polled five times a second.
    /// </summary>
    public class CityRaceStarts : MonoBehaviour
    {
        /// <summary>The centre-banner line while a gate is offering its race,
        /// else null (RaceHUD's city chain).</summary>
        public static string Prompt { get; private set; }
        /// <summary>A gate is offering its race - the touch ACTION button says
        /// START.</summary>
        public static bool AtStart { get; private set; }

        public struct Gate
        {
            public int venue;
            public Vector2 plan;
            public Vector2 heading;
            public float y;
            public string name;
            public Transform root;
        }

        /// <summary>The gates standing now, for the radar's marks.</summary>
        public static readonly List<Gate> Gates = new List<Gate>();

        /// <summary>Whether gates stand in this build's free roam.</summary>
        public static bool Enabled => !Edition.HasCareer;

        /// <summary>Close enough to the line, and slow enough, to be offered it.</summary>
        const float OfferM = 16f, OfferKmh = 32f;
        /// <summary>Gates past this are switched off (the old switch).</summary>
        const float ShowM = 450f;
        /// <summary>The fade: no light at FadeFarM of the eye, full at FadeNearM.</summary>
        public const float FadeFarM = 450f, FadeNearM = 300f;
        /// <summary>The fade (the LAB's RACE GATES FADE IN); false = the old
        /// switch at 450 m. PSX_GATEFADE=0 for the old.</summary>
        public static bool FadeIn = System.Environment.GetEnvironmentVariable("PSX_GATEFADE") != "0";
        /// <summary>DRIVE TO THE START puts the car this far short of the line.</summary>
        const float ApproachM = 70f;

        CarController player;
        CityWorld world;
        float poll;
        bool launching;

        static readonly Color Amber = new Color(0.969f, 0.651f, 0.137f, 1f);

        /// <summary>Stand the gates for a free-roam session. Called by
        /// CityMode.Start; nothing in a race scene or the town.</summary>
        public static CityRaceStarts Attach(CityMode mode)
        {
            if (!Enabled || mode == null || mode.IsRace || mode.world == null || mode.world.Map == null) return null;
            var go = new GameObject("RaceStarts");
            var rs = go.AddComponent<CityRaceStarts>();
            rs.player = mode.player;
            rs.world = mode.world;
            rs.Build();
            return rs;
        }

        void OnDestroy()
        {
            Gates.Clear();
            Looks.Clear();
            Prompt = null;
            AtStart = false;
        }

        void Build()
        {
            Gates.Clear();
            Looks.Clear();
            var map = world.Map;
            for (int i = 0; i < TrackCatalog.Count; i++)
            {
                var t = TrackCatalog.At(i);
                if (!t.IsCityRace || t.IsDeckRun || !TrackCatalog.Offered(i)) continue;
                if (!RouteGeo.StartPose(t, out var p, out var h)) continue;
                var route = map.RouteById(t.cityRoute);
                if (!GroundAt(map, route, p, out float y)) continue;
                var g = new Gate { venue = i, plan = p, heading = h, y = y, name = Plain(t.name) };
                MakeGate(ref g, t);
                Gates.Add(g);
                Looks.Add(LookOf(g.root));
            }
            Debug.Log("[RaceStarts] " + Gates.Count + " gates in free roam");
        }

        /// <summary>The road's solved height at the line, read off the
        /// route's own edges (the start of the 277 loop is on a freeway with
        /// streets under it; the nearest road of any kind could be one of
        /// those).</summary>
        static bool GroundAt(CityMap map, CityMap.Route route, Vector2 p, out float y)
        {
            y = 0f;
            float best = float.MaxValue;
            if (route != null && route.edges != null)
                foreach (int ei in route.edges)
                {
                    if (ei < 0 || ei >= map.edges.Length) continue;
                    var e = map.edges[ei];
                    for (int si = 0; si + 1 < e.pts.Length; si++)
                    {
                        Vector2 a = e.pts[si], b = e.pts[si + 1], d = b - a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                        float dd = Vector2.Distance(p, a + d * t);
                        if (dd < best) { best = dd; y = e.YAt(e.s[si] + Mathf.Sqrt(L2) * t); }
                    }
                }
            if (best < 40f) return true;
            if (!map.NearestRoadPoint(p, 40f, false, out int ni, out float ns, out _)) return false;
            y = map.edges[ni].YAt(ns);
            return true;
        }

        /// <summary>The player's font has no em dash (CityFrontEnd's note); a
        /// gate is a sign, and a sign with a hole in it reads as broken.</summary>
        static string Plain(string s) => (s ?? "").Replace("—", "-").Replace("–", "-").ToUpperInvariant();

        // ------------------------------------------------------------------
        //  the gate
        // ------------------------------------------------------------------
        // Drawn in PSX/Glow - the headlights' and street lamps' additive
        // shader, kept in every build by LampGlow.mat - as plain meshes. A
        // world-space UI canvas was the first try and drew NOTHING through the
        // PSX camera, so the name is a mesh of glyph quads cut from the menu
        // font's own atlas (its glyphs live in the alpha, which is exactly
        // what PSX/Glow reads), rebuilt if the atlas is.

        /// <summary>The glyph meshes, to re-cut when the font atlas is rebuilt.</summary>
        readonly List<(MeshFilter mf, string text, float capM)> signs = new List<(MeshFilter, string, float)>();
        static Material bandMat, postMat, textMat, kindMat;

        void MakeGate(ref Gate g, TrackCatalog.TrackDef t)
        {
            var root = new GameObject("Gate_" + t.id).transform;
            root.SetParent(transform, false);
            root.position = new Vector3(g.plan.x, g.y, g.plan.y);
            root.rotation = Quaternion.LookRotation(new Vector3(g.heading.x, 0f, g.heading.y), Vector3.up);
            g.root = root;
            EnsureMats();
            float half = Mathf.Max(t.roadWidth, 8f) * 0.5f + 0.8f;

            // The line: a band of light across the lanes, lying on the road.
            Quad(root, "Line", new Vector3(0f, 0.12f, 0f), Vector3.right, Vector3.forward,
                new Vector2(half * 2f, 2.2f), bandMat);
            // A post at each kerb: two crossed quads, bright at the foot and
            // gone by the top, so it reads from any side.
            foreach (float side in new[] { -half, half })
            {
                Quad(root, "Post", new Vector3(side, 3.6f, 0f), Vector3.right, Vector3.up, new Vector2(0.8f, 7.2f), postMat);
                Quad(root, "Post", new Vector3(side, 3.6f, 0f), Vector3.forward, Vector3.up, new Vector2(0.8f, 7.2f), postMat);
            }
            // The sign over the lanes, facing the way in: the name, its
            // numbers under it, a chevron down at the line.
            string km = (t.RaceMeters / 1000f).ToString("0.0") + " KM";
            string kind = RouteGeo.KindOf(t) + "  -  " + km +
                          (t.loop ? "  -  " + t.laps + (t.laps == 1 ? " LAP" : " LAPS") : "");
            Sign(root, "Name", g.name, new Vector3(0f, 6.0f, 0f), 1.25f, textMat);
            Sign(root, "Kind", kind, new Vector3(0f, 5.05f, 0f), 0.55f, kindMat);
            Quad(root, "Chevron", new Vector3(0f, 4.45f, 0f), Vector3.right, Vector3.up, new Vector2(1.3f, 0.7f),
                 Mat(ChevronTex(), Amber, 1.6f));
        }

        static void EnsureMats()
        {
            if (bandMat != null) return;
            bandMat = Mat(BandTex(), Amber, 1.1f);
            postMat = Mat(PostTex(), Amber, 1.2f);
            textMat = Mat(null, Amber, 1.7f);
            kindMat = Mat(null, Color.white, 1.2f);
        }

        static Material Mat(Texture tex, Color col, float strength)
        {
            var sh = Shader.Find("PSX/Glow");
            if (sh == null) { Debug.LogWarning("[RaceStarts] PSX/Glow shader missing"); return null; }
            var m = new Material(sh) { name = "RaceGate", hideFlags = HideFlags.DontSave };
            if (tex != null) m.mainTexture = tex;
            m.SetColor("_Color", col);
            m.SetFloat("_Strength", strength);
            return m;
        }

        /// <summary>A quad centred at <paramref name="c"/> (root-local),
        /// <paramref name="size"/> metres along <paramref name="u"/> and
        /// <paramref name="v"/>, its texture's v up <paramref name="v"/>.</summary>
        static void Quad(Transform parent, string name, Vector3 c, Vector3 u, Vector3 v, Vector2 size, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            Vector3 a = u * (size.x * 0.5f), b = v * (size.y * 0.5f);
            mesh.vertices = new[] { c - a - b, c - a + b, c + a + b, c + a - b };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void Sign(Transform parent, string name, string text, Vector3 baseline, float capM, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = baseline;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            signs.Add((mf, text, capM));
            CutGlyphs(mf, text, capM);
        }

        const int GlyphPx = 96;

        /// <summary>One quad per glyph, centred on x = 0, baseline at y = 0,
        /// capitals <paramref name="capM"/> metres tall.</summary>
        static void CutGlyphs(MeshFilter mf, string text, float capM)
        {
            var font = MenuKit.Font;
            if (font == null || mf == null) return;
            font.RequestCharactersInTexture(text, GlyphPx, FontStyle.Bold);
            float scale = capM / (GlyphPx * 0.72f);
            float total = 0f;
            foreach (char ch in text)
                if (font.GetCharacterInfo(ch, out var ci, GlyphPx, FontStyle.Bold)) total += ci.advance;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float x = -total * 0.5f;
            foreach (char ch in text)
            {
                if (!font.GetCharacterInfo(ch, out var ci, GlyphPx, FontStyle.Bold)) continue;
                if (ci.maxX > ci.minX && ci.maxY > ci.minY)
                {
                    int i = verts.Count;
                    verts.Add(new Vector3((x + ci.minX) * scale, ci.minY * scale, 0f));
                    verts.Add(new Vector3((x + ci.minX) * scale, ci.maxY * scale, 0f));
                    verts.Add(new Vector3((x + ci.maxX) * scale, ci.maxY * scale, 0f));
                    verts.Add(new Vector3((x + ci.maxX) * scale, ci.minY * scale, 0f));
                    uvs.Add(ci.uvBottomLeft); uvs.Add(ci.uvTopLeft); uvs.Add(ci.uvTopRight); uvs.Add(ci.uvBottomRight);
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
                }
                x += ci.advance;
            }
            var mesh = mf.sharedMesh;
            if (mesh == null) { mesh = new Mesh { name = "Glyphs" }; mf.sharedMesh = mesh; }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null) mr.sharedMaterial.mainTexture = font.material.mainTexture;
        }

        void OnEnable() => Font.textureRebuilt += OnFontRebuilt;
        void OnDisable() => Font.textureRebuilt -= OnFontRebuilt;

        /// <summary>The menu font's atlas was rebuilt (other text asked for
        /// glyphs it did not have): every UV here points at the old one.</summary>
        void OnFontRebuilt(Font f)
        {
            if (f != MenuKit.Font) return;
            foreach (var s in signs) CutGlyphs(s.mf, s.text, s.capM);
        }

        static Texture2D bandTex, postTex, chevTex;

        /// <summary>Bright across the middle, soft at both edges (along the
        /// road) and at both ends (the kerbs).</summary>
        static Texture2D BandTex()
        {
            if (bandTex != null) return bandTex;
            const int W = 64, H = 16;
            bandTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = 1f - Mathf.Abs((y + 0.5f) / H * 2f - 1f);
                    float u = Mathf.Clamp01(Mathf.Min(x + 0.5f, W - x - 0.5f) / 6f);
                    float a = Mathf.Pow(v, 0.8f) * u;
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            bandTex.SetPixels32(px);
            bandTex.Apply();
            return bandTex;
        }

        /// <summary>A post: full at the foot, gone at the top, soft sides.</summary>
        static Texture2D PostTex()
        {
            if (postTex != null) return postTex;
            const int W = 16, H = 64;
            postTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float across = 1f - Mathf.Abs((x + 0.5f) / W * 2f - 1f);
                    float up = 1f - (y + 0.5f) / H;
                    float a = Mathf.Pow(across, 1.4f) * Mathf.Pow(up, 1.6f);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            postTex.SetPixels32(px);
            postTex.Apply();
            return postTex;
        }

        /// <summary>A down-pointing chevron (a V of two strokes).</summary>
        static Texture2D ChevronTex()
        {
            if (chevTex != null) return chevTex;
            const int W = 64, H = 34;
            chevTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // the V's two arms meet at the bottom centre
                    float fx = Mathf.Abs((x + 0.5f) - W * 0.5f);
                    float fy = y + 0.5f;
                    float arm = Mathf.Abs(fy - (2f + fx * (H - 6f) / (W * 0.5f)));
                    float a = Mathf.Clamp01(4.5f - arm);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            chevTex.SetPixels32(px);
            chevTex.Apply();
            return chevTex;
        }

        // ------------------------------------------------------------------
        //  arriving
        // ------------------------------------------------------------------
        void Update()
        {
            Prompt = null;
            AtStart = false;
            if (player == null || launching || Gates.Count == 0) return;
            var cam = Camera.main;
            var me = new Vector2(player.transform.position.x, player.transform.position.z);

            ShowGates(cam != null ? cam.transform.position : player.transform.position);

            if (PauseMenu.IsOpen) return;
            var mode = CityMode.Instance;
            if (mode == null || !mode.Live) return;
            var input = player.GetComponent<PlayerCarInput>();
            if (input == null || !input.inputEnabled) return;

            Gate? at = null;
            float bestD = OfferM;
            foreach (var g in Gates)
            {
                float d = Vector2.Distance(me, g.plan);
                if (d < bestD) { bestD = d; at = g; }
            }
            if (at == null || Mathf.Abs(player.speedKmh) > OfferKmh) return;

            var gate = at.Value;
            AtStart = true;
            Prompt = UseControlName() + " - START  " + gate.name;
            if (UsePressed()) Launch(gate.venue);
        }

        // ------------------------------------------------------------------
        //  showing: the fade (or the old switch)
        // ------------------------------------------------------------------
        /// <summary>A gate's renderers, their materials' own colours and the
        /// light it shows now (0..1; -1 not yet written).</summary>
        public sealed class Look
        {
            public Renderer[] rends;
            public Color[] baseCol;
            public float k = -1f;
        }

        /// <summary>One per gate, beside <see cref="Gates"/>.</summary>
        public static readonly List<Look> Looks = new List<Look>();
        static MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        bool wasFade;

        static Look LookOf(Transform root)
        {
            var l = new Look { rends = root != null ? root.GetComponentsInChildren<Renderer>(true) : new Renderer[0] };
            l.baseCol = new Color[l.rends.Length];
            for (int i = 0; i < l.rends.Length; i++)
            {
                var m = l.rends[i].sharedMaterial;
                l.baseCol[i] = m != null && m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            }
            return l;
        }

        /// <summary>The fade's light at <paramref name="d"/> metres from the
        /// eye: 0 at FadeFarM, 1 at FadeNearM, eased at both ends.</summary>
        public static float FadeAt(float d)
        {
            float t = Mathf.Clamp01((FadeFarM - d) / (FadeFarM - FadeNearM));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Write a gate's light: each renderer's colour scaled
        /// through its property block (1 = no block: the materials as they
        /// are, exactly the old picture).</summary>
        static void SetLight(Look l, float k)
        {
            if (l == null || (l.k >= 0f && l.k == k)) return;
            l.k = k;
            if (block == null) block = new MaterialPropertyBlock();
            for (int i = 0; i < l.rends.Length; i++)
            {
                var r = l.rends[i];
                if (r == null) continue;
                if (k >= 1f) { r.SetPropertyBlock(null); continue; }
                var c = l.baseCol[i];
                block.Clear();
                block.SetColor(ColorId, new Color(c.r * k, c.g * k, c.b * k, c.a));
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Tools: time <see cref="ShowGates"/> into <see cref="ClockTicks"/>
        /// (Stopwatch ticks; the gate check's cost A/B).</summary>
        public static bool Clocked;
        public static long ClockTicks;

        void ShowGates(Vector3 eye)
        {
            if (!Clocked) { ShowGatesNow(eye); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ShowGatesNow(eye);
            ClockTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }

        void ShowGatesNow(Vector3 eye)
        {
            var e2 = new Vector2(eye.x, eye.z);
            if (FadeIn)
            {
                // every frame: a handful of gates, a distance and a block each
                wasFade = true;
                for (int i = 0; i < Gates.Count && i < Looks.Count; i++)
                {
                    var g = Gates[i];
                    float k = FadeAt(Vector2.Distance(e2, g.plan));
                    bool on = k > 0f;
                    // the light first, then the switch: a root comes on at its own (near 0) light
                    SetLight(Looks[i], k);
                    if (g.root.gameObject.activeSelf != on) g.root.gameObject.SetActive(on);
                }
                return;
            }
            // THE OLD SWITCH: a few times a second, full light inside ShowM
            if (wasFade) { wasFade = false; poll = 0f; foreach (var l in Looks) SetLight(l, 1f); }
            poll -= Time.unscaledDeltaTime;
            if (poll > 0f) return;
            poll = 0.2f;
            foreach (var g in Gates)
            {
                float d = Vector2.Distance(e2, g.plan);
                bool on = d < ShowM;
                if (g.root.gameObject.activeSelf != on) g.root.gameObject.SetActive(on);
            }
        }

        // ---- the light-pop meter's view (Dev/LightPopMeter.cs: the gate cause) ----
        /// <summary>Gate <paramref name="i"/>'s state now: its root switched on, its light.</summary>
        public static bool MeterRead(int i, out bool active, out float k)
        {
            active = false; k = 0f;
            if (i < 0 || i >= Gates.Count || i >= Looks.Count || Gates[i].root == null) return false;
            active = Gates[i].root.gameObject.activeSelf;
            k = Looks[i].k < 0f ? 1f : Looks[i].k;
            return true;
        }

        /// <summary>Put gate <paramref name="i"/> to a state (the meter's
        /// frame-before render, then back).</summary>
        public static void MeterWrite(int i, bool active, float k)
        {
            if (i < 0 || i >= Gates.Count || i >= Looks.Count || Gates[i].root == null) return;
            SetLight(Looks[i], k);
            if (Gates[i].root.gameObject.activeSelf != active) Gates[i].root.gameObject.SetActive(active);
        }

        /// <summary>Gate <paramref name="i"/>'s renderers (the meter collects them).</summary>
        public static Renderer[] MeterRenderers(int i) => i >= 0 && i < Looks.Count ? Looks[i].rends : null;

        // ---- the road into a gate (tools: the gate approach, the LAB check's stand) ----
        /// <summary>
        /// The road leading into gate <paramref name="gi"/>, about
        /// <paramref name="lengthM"/> metres of it, far end first, ending at
        /// the line: from the gate's own street backwards, at each node the
        /// street that carries on straightest (a turn of up to 100 degrees
        /// where nothing goes straight on; a one-way only the way it is driven), a quarter of the road right of the
        /// centre line on a two-way. A point every 2 m at the road's solved
        /// height. Shorter when the street ends.
        /// </summary>
        public static List<Vector3> ApproachLine(CityMap map, int gi, float lengthM)
        {
            var pts = new List<Vector3>();
            if (map == null || gi < 0 || gi >= Gates.Count) return pts;
            var g = Gates[gi];
            var t = TrackCatalog.At(g.venue);
            var route = t != null ? map.RouteById(t.cityRoute) : null;
            int ei = -1; float s = 0f, best = float.MaxValue;
            if (route != null && route.edges != null)
                foreach (int e0 in route.edges)
                {
                    if (e0 < 0 || e0 >= map.edges.Length) continue;
                    var e = map.edges[e0];
                    for (int si = 0; si + 1 < e.pts.Length; si++)
                    {
                        Vector2 a = e.pts[si], b = e.pts[si + 1], d = b - a;
                        float L2 = d.sqrMagnitude;
                        float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(g.plan - a, d) / L2) : 0f;
                        float dd = Vector2.Distance(g.plan, a + d * tt);
                        if (dd < best) { best = dd; ei = e0; s = e.s[si] + Mathf.Sqrt(L2) * tt; }
                    }
                }
            if (ei < 0 || best > 30f)
                if (!map.NearestRoadPoint(g.plan, 40f, false, out ei, out s, out _)) return pts;
            // walking backwards: -1 = toward the edge's first point
            var edge = map.edges[ei];
            int dir = Vector2.Dot(edge.TangentAt(s), g.heading) > 0f ? -1 : 1;
            const float Step = 2f;
            float walked = 0f;
            int guard = 0;
            while (walked < lengthM && guard++ < 4000)
            {
                var back = edge.TangentAt(s) * dir;      // the way we walk (against travel)
                var fwd = -back;                           // the way a car drives here
                var c = edge.PointAt(s);
                float off = edge.oneway ? 0f : Mathf.Min(edge.width * 0.25f, 3.5f);
                var q = c + new Vector2(fwd.y, -fwd.x) * off;
                pts.Add(new Vector3(q.x, edge.YAt(s), q.y));
                float ns = s + dir * Step;
                if (ns >= 0f && ns <= edge.length) { s = ns; walked += Step; continue; }
                // the node at this end: the street that carries on straightest
                int node = dir < 0 ? edge.a : edge.b;
                // straightest first; with none within 40 degrees, a turn (up to 100) rather than a dead end
                int bestE = -1, bestDir = 0; float bestDot = Mathf.Cos(100f * Mathf.Deg2Rad);
                if (map.nodeEdges != null && node >= 0 && node < map.nodeEdges.Length && map.nodeEdges[node] != null)
                    foreach (int ci in map.nodeEdges[node])
                    {
                        if (ci == edge.index || ci < 0 || ci >= map.edges.Length) continue;
                        var ce = map.edges[ci];
                        if (ce.length < 1f) continue;
                        // walking backwards out of the node along ce: +1 from its first point, -1 from its last
                        int cdir = ce.a == node ? 1 : ce.b == node ? -1 : 0;
                        if (cdir == 0) continue;
                        if (ce.oneway && cdir != -1) continue;   // a one-way is driven along its points
                        var outDir = ce.TangentAt(cdir > 0 ? 0f : ce.length) * cdir;
                        float dot = Vector2.Dot(outDir, back);
                        if (dot > bestDot) { bestDot = dot; bestE = ci; bestDir = cdir; }
                    }
                if (bestE < 0) break;
                walked += dir < 0 ? s : edge.length - s;
                edge = map.edges[bestE];
                dir = bestDir;
                s = dir > 0 ? 0f : edge.length;
            }
            pts.Reverse();
            return pts;
        }

        /// <summary>The race, with the car you are in, the hour it is now and
        /// the weather you picked - what the card's START sends.</summary>
        void Launch(int venue)
        {
            string spec = !string.IsNullOrEmpty(RaceHandoff.CarSpecId) ? RaceHandoff.CarSpecId : CityFrontEnd.CarId();
            int hour = TimeOfDay.Current;
            int weather = (int)Seasons.CurrentWeather;   // the bench's sky, else the pick
            if (!CityFrontEnd.FillRace(venue, spec, hour, weather, out int scene))
            {
                Debug.LogWarning("[RaceStarts] race " + venue + " is not in this build");
                return;
            }
            launching = true;
            Prompt = null;
            AtStart = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Debug.Log("[RaceStarts] START " + TrackCatalog.At(venue).id + " from the gate");
            if (!TrackCatalog.TryLoadScene(scene, TrackCatalog.At(venue).id)) launching = false;
        }

        /// <summary>
        /// DRIVE TO THE START: the free-roam car, put down on the race's own
        /// road <see cref="ApproachM"/> short of its line and pointing at it,
        /// the tiles under it built first. False when the venue has no gate.
        /// </summary>
        public static bool PlaceShort(CityWorld world, CarController car, int venue)
        {
            if (world == null || world.Map == null || car == null) return false;
            if (venue < 0 || venue >= TrackCatalog.Count) return false;
            var t = TrackCatalog.At(venue);
            if (!RouteGeo.StartPose(t, out var p, out var h)) return false;
            var map = world.Map;
            var back = p - h * ApproachM;
            var route = map.RouteById(t.cityRoute);
            // On the route's own edge nearest that point, at its lanes and
            // its solved height, pointing the way the race leaves.
            int bestE = -1; float bestS = 0f, best = float.MaxValue;
            if (route != null && route.edges != null)
                foreach (int ei in route.edges)
                {
                    if (ei < 0 || ei >= map.edges.Length) continue;
                    var e = map.edges[ei];
                    for (int si = 0; si + 1 < e.pts.Length; si++)
                    {
                        Vector2 a = e.pts[si], b = e.pts[si + 1], d = b - a;
                        float L2 = d.sqrMagnitude;
                        float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(back - a, d) / L2) : 0f;
                        float dd = Vector2.Distance(back, a + d * tt);
                        if (dd < best) { best = dd; bestE = ei; bestS = e.s[si] + Mathf.Sqrt(L2) * tt; }
                    }
                }
            if (bestE < 0 || best > 30f)
            {
                if (!map.NearestRoadPoint(back, 60f, true, out bestE, out bestS, out _)) return false;
            }
            var edge = map.edges[bestE];
            var q = LineModel.LanePoint(edge, bestS);
            var tan = edge.TangentAt(bestS);
            var fwd = new Vector3(tan.x, 0f, tan.y);
            if (Vector2.Dot(tan, h) < 0f) fwd = -fwd;
            var pos = new Vector3(q.x, edge.YAt(bestS), q.y);
            world.EnsureRing(pos, 1);
            car.ResetTo(pos, Quaternion.LookRotation(fwd, Vector3.up), seated: true);
            return true;
        }

        static bool UsePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;
            var touch = TouchControls.Instance;
            return touch != null && touch.Visible && touch.ActionPressed;
        }

        static string UseControlName()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible) return "TAP START";
            return Gamepad.current != null ? "PRESS X / A" : "PRESS F";
        }
    }
}
