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
    /// per gate, switched off past 450 m.
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
        /// <summary>Gates past this are switched off.</summary>
        const float ShowM = 450f;
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
            Prompt = null;
            AtStart = false;
        }

        void Build()
        {
            Gates.Clear();
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

            // Show/fade a few times a second: four gates, no need for more.
            poll -= Time.unscaledDeltaTime;
            if (poll <= 0f)
            {
                poll = 0.2f;
                var eye = cam != null ? cam.transform.position : player.transform.position;
                foreach (var g in Gates)
                {
                    float d = Vector2.Distance(new Vector2(eye.x, eye.z), g.plan);
                    bool on = d < ShowM;
                    if (g.root.gameObject.activeSelf != on) g.root.gameObject.SetActive(on);
                }
            }

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
