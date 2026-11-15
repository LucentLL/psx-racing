using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// DECK PLATES (2026-11-15, agent "spui"; the owner: "review all roads for
    /// accuracy ... bridges"; the bridges audit: steps to 22 cm, grade breaks
    /// to 13 %, a hole and a slit where the ramps of a single-point interchange
    /// meet ON one deck).
    ///
    /// Where several junction nodes stand on ONE structure closer together
    /// than their own junctions are wide (I-85 at Freedom Drive: twelve nodes
    /// in 80 m, the left turns crossing in a 2.5 m quad of four), every short
    /// edge between them is solved on its own profile and drawn as its own
    /// ribbon, clipped and trimmed against the others by rules written for a
    /// ramp that runs out beside its mainline for a hundred metres. The
    /// ribbons overlap, their surfaces stand apart, and each one's paint
    /// crosses the others' lanes. A real deck is one slab.
    ///
    /// THE RULE (from the map alone, no list of places): a DECK KNOT is a set
    /// of <see cref="MinNodes"/> or more junction nodes (three arms or more,
    /// every arm on structure at the node) joined by edges that are on
    /// structure end to end and no longer than <see cref="ShortEdgeM"/>. Its
    /// PLATE is the convex hull of those edges' pavements and of each arm's
    /// pavement out to where it stops overlapping the knot's
    /// (<see cref="MarginM"/> wider). Inside the plate every road at the
    /// knot's level stands at ONE height H: the mean of what the solve left
    /// there, and never under what any piece of deck over a lower road's
    /// pavement had (the clearance the solve gave is kept everywhere). Past
    /// the plate each road eases from H back to its own profile over
    /// <see cref="BlendM"/> of road (graph distance, so a ramp that loops
    /// under the deck is never touched), level at the plate's rim and with
    /// the least extra grade a level start allows (<see cref="U"/>).
    ///
    /// The heights are written into the DRAWN profile (Edge.vS / vY, and the
    /// node heights) after the solve and its presentation pass have finished:
    /// the solve's stations stay as solved, and everything that reads
    /// Edge.YAt (the ribbons, the junction fans, kerbs, rails, girders, the
    /// land, traffic's lane paths) follows. The tile builder then lays the
    /// plate as ONE unpainted slab and cuts every road sheet to its rim
    /// (CityMeshes.DeckPlates.cs). The road graph is not touched.
    /// PSX_CITY_SPUI=0: the picture as it was. PSX_CITY_SPUI_CUT=0: the
    /// heights only (the ribbons as before, at one height).
    /// </summary>
    public static class CityDeckPlates
    {
        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);
        /// <summary>PSX_CITY_SPUI=0: the picture as it was.</summary>
        public static readonly bool EnvOn = Env("PSX_CITY_SPUI") != "0";
        /// <summary>The plates' heights are in the map now (the pictures' tool switches it between frames).</summary>
        public static bool On { get; private set; }
        /// <summary>PSX_CITY_SPUI_CUT=0: no slab, the ribbons as before at the plate's height.</summary>
        public static bool CutOn = Env("PSX_CITY_SPUI_CUT") != "0";
        /// <summary>PSX_CITY_SPUI_ONLY="x,z;x,z": only the knots within 150 m of these points (a test switch).</summary>
        static readonly string onlySpec = Env("PSX_CITY_SPUI_ONLY");

        public const float ShortEdgeM = 45f;
        public const int MinNodes = 4;
        public const float BlendM = 60f;
        const float MarginM = 1f, SepMaxM = 60f, LevelTolM = 2f, MaxNodeRangeM = 2.5f, MaxRaiseM = 2f, MaxExtentM = 160f;
        const float SampleM = 0.5f;
        /// <summary>A deck's top over the road below, as the solve stands one: its clearance, the slab and a girder (measured at the three knots: 6.2-7.0 m).</summary>
        const float NeedM = CityElevation.ClearanceM + CityElevation.DeckThick + 0.85f;

        public sealed class Plate
        {
            public int id;
            public int[] nodes, inner, arms;
            /// <summary>Convex, counter-clockwise in (x, z).</summary>
            public Vector2[] hull;
            public Vector2 bbMin, bbMax, centre;
            public float H, hFit, hOver, underMax, yRef, maxRaise, maxLower, longestBlend;
            public int edgesTouched, loweredOverRoad, lowerRoads;
            public int widest = -1;
            public string skipped;
            public readonly List<int> levelEdges = new List<int>();
            /// <summary>The tile builder's slab pieces (convex, counter-clockwise), made on first use.</summary>
            public List<Vector2[]> cover;
            public List<Vector4> coverBB;
            /// <summary>The cover made disjoint: what is laid.</summary>
            public List<Vector2[]> slab;
            /// <summary>The slab could not be made within its bounds: the tile builder leaves this plate's ribbons alone.</summary>
            public bool cutFailed;
            /// <summary>The slab's surface (CityMeshes.Surface): what most of the deck it replaces is.</summary>
            public int surface;
            /// <summary>How far the plate reaches (ModeNames) and how long its ease is.</summary>
            public int mode;
            public float blend;
            public object coverFor;

            public bool Inside(Vector2 p) => InHull(hull, p);
        }

        public static readonly List<Plate> Plates = new List<Plate>();
        /// <summary>Changes whenever the plates are rebuilt or switched (the tile builder's caches key on it).</summary>
        public static int Version { get; private set; }
        static readonly Dictionary<int, (float[] s, float[] y)> savedProf = new Dictionary<int, (float[], float[])>();
        static readonly Dictionary<int, (float[] s, float[] y)> newProf = new Dictionary<int, (float[], float[])>();
        static readonly Dictionary<int, float> savedNodeY = new Dictionary<int, float>(), newNodeY = new Dictionary<int, float>();
        static string lastError = "";

        // ---- the ease past the rim ---------------------------------------
        // Y = H + (Yown - H) * U(d / BlendM). With a plain smoothstep a road
        // falling away at g ran 1.69 g inside the ease (it starts level and
        // has the same height to lose). U is the integral of a grade factor
        // that rises to c over the first 35 %, holds, and comes back to 1 over
        // the last 30 %: c = 1.26, the least a level start allows over that
        // length.
        const float UTau = 0.35f, URho = 0.3f;
        static float[] uTab;
        static float U(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            if (uTab == null)
            {
                const int N = 400;
                float c = (1f - URho * 0.5f) / (1f - UTau * 0.5f - URho * 0.5f);
                float F(float x)
                {
                    if (x < UTau) { float q = x / UTau; return c * q * q * (3f - 2f * q); }
                    if (x <= 1f - URho) return c;
                    float r = (x - (1f - URho)) / URho; r = r * r * (3f - 2f * r);
                    return c + (1f - c) * r;
                }
                var tab = new float[N + 1];
                float acc = 0f, prev = F(0f);
                for (int i = 1; i <= N; i++)
                {
                    float x = i / (float)N, fx = F(x);
                    acc += 0.5f * (prev + fx) / N; prev = fx;
                    tab[i] = acc / x;
                }
                float end = tab[N];
                for (int i = 1; i <= N; i++) tab[i] /= end;   // exactly 1 at the far end
                uTab = tab;
            }
            float f = t * (uTab.Length - 1);
            int k = Mathf.Min(uTab.Length - 2, (int)f);
            return Mathf.Lerp(uTab[k], uTab[k + 1], f - k);
        }

        // ---- plan helpers -------------------------------------------------
        public static bool InHull(Vector2[] h, Vector2 p)
        {
            for (int i = 0, j = h.Length - 1; i < h.Length; j = i++)
            {
                Vector2 a = h[j], b = h[i];
                if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < 0f) return false;
            }
            return true;
        }
        static bool SegSeg(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float d2 = (b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x);
            float d3 = (d.x - c.x) * (a.y - c.y) - (d.y - c.y) * (a.x - c.x);
            float d4 = (d.x - c.x) * (b.y - c.y) - (d.y - c.y) * (b.x - c.x);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }
        static bool SegHitsHull(Vector2[] h, Vector2 a, Vector2 b)
        {
            if (InHull(h, a) || InHull(h, b)) return true;
            for (int i = 0, j = h.Length - 1; i < h.Length; j = i++)
                if (SegSeg(a, b, h[j], h[i])) return true;
            return false;
        }
        static void CrossSection(CityMap.Edge e, float s, out Vector2 L, out Vector2 R)
        {
            var p = e.PointAt(s);
            var t = e.TangentAt(s);
            var right = new Vector2(-t.y, t.x);
            LineModel.Extents(e, s, out float eM, out float eP);
            L = p - right * eM; R = p + right * eP;
        }
        /// <summary>Is a plan point on an edge's pavement (its line model's extents, <paramref name="tol"/> wider)?</summary>
        static bool InRibbon(CityMap.Edge o, Vector2 p, float tol, out float at)
        {
            at = 0f;
            float best = float.MaxValue; int bi = -1; float bt = 0f;
            for (int i = 0; i + 1 < o.pts.Length; i++)
            {
                Vector2 a = o.pts[i], d = o.pts[i + 1] - a;
                float l2 = d.sqrMagnitude;
                float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / l2) : 0f;
                float dd = (p - (a + d * t)).sqrMagnitude;
                if (dd < best) { best = dd; bi = i; bt = t; }
            }
            if (bi < 0) return false;
            Vector2 sa = o.pts[bi], sd = o.pts[bi + 1] - sa;
            float len = sd.magnitude;
            if (len < 1e-4f) return false;
            var tan = sd / len;
            at = o.s[bi] + len * bt;
            float along = Vector2.Dot(p - sa, tan);
            // past the edge's own end: not on it
            if (bi == 0 && along < -0.25f) return false;
            if (bi == o.pts.Length - 2 && along > len + 0.25f) return false;
            var right = new Vector2(-tan.y, tan.x);
            float lat = Vector2.Dot(p - (sa + sd * bt), right);
            LineModel.Extents(o, at, out float eM, out float eP);
            return lat >= -eM - tol && lat <= eP + tol;
        }

        static List<Vector2> ConvexHull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>(pts.Count + 1);
            float Cr(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int i = 0; i < pts.Count; i++)
            {
                while (h.Count >= 2 && Cr(h[h.Count - 2], h[h.Count - 1], pts[i]) <= 1e-4f) h.RemoveAt(h.Count - 1);
                h.Add(pts[i]);
            }
            int lower = h.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--)
            {
                while (h.Count >= lower && Cr(h[h.Count - 2], h[h.Count - 1], pts[i]) <= 1e-4f) h.RemoveAt(h.Count - 1);
                h.Add(pts[i]);
            }
            h.RemoveAt(h.Count - 1);
            return h;   // counter-clockwise
        }

        /// <summary>The hull moved out by m (each vertex along its two edges' normals).</summary>
        static Vector2[] Grow(List<Vector2> h, float m)
        {
            int n = h.Count;
            var o = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = h[(i + n - 1) % n], b = h[i], c = h[(i + 1) % n];
                Vector2 e0 = (b - a).normalized, e1 = (c - b).normalized;
                // counter-clockwise: outward is the right of travel
                Vector2 n0 = new Vector2(e0.y, -e0.x), n1 = new Vector2(e1.y, -e1.x);
                float k = 1f + Vector2.Dot(n0, n1);
                o[i] = b + (n0 + n1) * (m / Mathf.Max(0.3f, k));
            }
            return o;
        }

        // ---- the pass ------------------------------------------------------
        /// <summary>Called at the end of CityElevation.Solve (and after the
        /// editor's drawn-profile switch): finds the knots on the solved map,
        /// works out each plate, and (when on) writes the heights.</summary>
        public static void AfterSolve(CityMap map)
        {
            Plates.Clear(); savedProf.Clear(); newProf.Clear(); savedNodeY.Clear(); newNodeY.Clear();
            On = false; Version++; lastError = "";
            if (!EnvOn || map == null || map.edges == null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { Build(map); }
            catch (System.Exception ex)
            {
                // the map as the solve left it
                foreach (var kv in savedProf) { map.edges[kv.Key].vS = kv.Value.s; map.edges[kv.Key].vY = kv.Value.y; }
                foreach (var kv in savedNodeY) map.nodeY[kv.Key] = kv.Value;
                Plates.Clear(); savedProf.Clear(); newProf.Clear(); savedNodeY.Clear(); newNodeY.Clear();
                lastError = ex.ToString();
                Debug.LogError("[DeckPlates] " + ex);
                return;
            }
            // Build wrote as it went (a later plate reads an earlier one's
            // heights): keep the result, and the map is ON
            foreach (var kv in savedProf) newProf[kv.Key] = (map.edges[kv.Key].vS, map.edges[kv.Key].vY);
            foreach (var kv in savedNodeY) newNodeY[kv.Key] = map.nodeY[kv.Key];
            On = true;
            BuildMs = (float)clock.Elapsed.TotalMilliseconds;
        }
        public static float BuildMs { get; private set; }

        /// <summary>Switch the plates' heights in or out of the map without a
        /// re-solve (the pictures' before / after in one job). The caller
        /// drops its tiles.</summary>
        public static void SetOn(CityMap map, bool on)
        {
            if (on == On || map == null) return;
            foreach (var kv in on ? newProf : savedProf) { map.edges[kv.Key].vS = kv.Value.s; map.edges[kv.Key].vY = kv.Value.y; }
            foreach (var kv in on ? newNodeY : savedNodeY) map.nodeY[kv.Key] = kv.Value;
            On = on; Version++;
        }

        static void Build(CityMap map)
        {
            int N = map.nodes.Length, E = map.edges.Length;
            bool ElevEnd(CityMap.Edge e, int n) => e.stElev != null && e.stElev.Length > 0 && e.stElev[n == e.a ? 0 : e.stElev.Length - 1];
            bool AllElev(CityMap.Edge e)
            {
                if (e.stElev == null || e.stElev.Length == 0) return false;
                foreach (bool b in e.stElev) if (!b) return false;
                return true;
            }
            var deckNode = new bool[N];
            for (int n = 0; n < N; n++)
            {
                var list = map.nodeEdges[n];
                if (list == null || list.Count < 3) continue;
                int arms = 0; bool all = true;
                foreach (int ei in list)
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || e.length < 0.5f) continue;
                    arms++;
                    if (e.tunnel || !ElevEnd(e, n)) { all = false; break; }
                }
                deckNode[n] = all && arms >= 3;
            }
            var parent = new int[N];
            for (int i = 0; i < N; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            bool Short(CityMap.Edge e) => e.a != e.b && !e.tunnel && e.length <= ShortEdgeM && deckNode[e.a] && deckNode[e.b] && AllElev(e);
            foreach (var e in map.edges)
                if (Short(e)) { int ra = Find(e.a), rb = Find(e.b); if (ra != rb) parent[Mathf.Max(ra, rb)] = Mathf.Min(ra, rb); }
            var groups = new SortedDictionary<int, List<int>>();
            for (int n = 0; n < N; n++)
            {
                if (!deckNode[n]) continue;
                int r = Find(n);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                g.Add(n);
            }
            List<Vector2> only = null;
            if (!string.IsNullOrWhiteSpace(onlySpec))
            {
                only = new List<Vector2>();
                foreach (var it in onlySpec.Split(';'))
                {
                    var f = it.Split(',');
                    if (f.Length >= 2 && float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                                      && float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z)) only.Add(new Vector2(x, z));
                }
            }
            foreach (var kv in groups)
            {
                var g = kv.Value;
                if (g.Count < MinNodes) continue;
                var pl = new Plate { id = Plates.Count, nodes = g.ToArray() };
                Vector2 c = Vector2.zero; float yLo = float.MaxValue, yHi = float.MinValue, ySum = 0f;
                foreach (int n in g) { c += map.nodes[n]; yLo = Mathf.Min(yLo, map.nodeY[n]); yHi = Mathf.Max(yHi, map.nodeY[n]); ySum += map.nodeY[n]; }
                pl.centre = c / g.Count; pl.yRef = ySum / g.Count;
                Plates.Add(pl);
                if (only != null)
                {
                    bool near = false;
                    foreach (var o in only) near |= (o - pl.centre).sqrMagnitude <= 150f * 150f;
                    if (!near) { pl.skipped = "not asked for (PSX_CITY_SPUI_ONLY)"; continue; }
                }
                if (yHi - yLo > MaxNodeRangeM) { pl.skipped = $"its nodes stand {yHi - yLo:0.00} m apart (over {MaxNodeRangeM:0.0} m: not one level deck)"; continue; }
                BuildPlate(map, pl);
            }
        }

        struct EdgeScan { public float[] s; public bool[] ins; public float outA, outB; public bool any; }

        /// <summary>The plate's reach, tried widest first: the whole DECK (each arm out to where its
        /// structure ends: one slab to the abutments), the knot's own pavements and 10 m, the
        /// knot's own pavements. A wider plate is taken only if it moves nothing more than
        /// <see cref="MaxDeckChangeM"/>: Tyvola Road climbs 3 % across its deck, and its whole
        /// deck as one level slab moved it 1.5 m.</summary>
        static readonly string[] ModeNames = { "the whole deck", "the knot's pavements + 10 m", "the knot's pavements" };
        const float MaxDeckChangeM = 1.0f;
        /// <summary>The ease is as long as the change needs for one curvature (60 m for 0.4 m,
        /// 95 m for 1 m: a car at 60 mph feels under 0.2 g over it), never under BlendM.</summary>
        const float BlendPerRootM = 95f, MaxBlendM = 120f;

        static void BuildPlate(CityMap map, Plate pl)
        {
            for (int mode = 0; mode < 3; mode++)
            {
                if (!TryPlate(map, pl, mode, BlendM, false)) { if (mode == 2) return; continue; }
                float change = Mathf.Max(pl.maxRaise, pl.maxLower);
                if (mode < 2 && change > MaxDeckChangeM) continue;
                float blend = Mathf.Clamp(BlendPerRootM * Mathf.Sqrt(change), BlendM, MaxBlendM);
                if (blend > BlendM + 5f && !TryPlate(map, pl, mode, blend, false)) return;
                if (!TryPlate(map, pl, mode, blend, true)) return;
                pl.mode = mode; pl.blend = blend;
                return;
            }
        }

        static bool TryPlate(CityMap map, Plate pl, int mode, float blend, bool write)
        {
            pl.skipped = null; pl.levelEdges.Clear();
            var inG = new HashSet<int>(pl.nodes);
            var inner = new List<int>(); var arms = new List<(int e, int node)>();
            foreach (int n in pl.nodes)
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || e.tunnel) continue;
                    bool both = inG.Contains(e.a) && inG.Contains(e.b) && e.length <= 2f * ShortEdgeM;
                    if (both) { if (e.a == n) inner.Add(ei); }
                    else arms.Add((ei, n));
                }
            inner.Sort();
            arms.Sort((x, y) => x.e != y.e ? x.e.CompareTo(y.e) : x.node.CompareTo(y.node));
            pl.inner = inner.ToArray();
            var armIds = new List<int>();
            foreach (var a in arms) if (!armIds.Contains(a.e)) armIds.Add(a.e);
            pl.arms = armIds.ToArray();

            // ---- the plate: the inner pavements, and each arm's out to where it parts from the knot's
            var pts = new List<Vector2>(256);
            float widest = 0f;
            foreach (int ei in inner)
            {
                var e = map.edges[ei];
                int k = Mathf.Max(1, Mathf.CeilToInt(e.length / 2f));
                for (int i = 0; i <= k; i++)
                {
                    CrossSection(e, e.length * i / k, out var L, out var R);
                    pts.Add(L); pts.Add(R);
                }
                LineModel.Extents(e, e.length * 0.5f, out float eM, out float eP);
                if (eM + eP > widest + 0.01f) { widest = eM + eP; pl.widest = ei; }
            }
            // Each arm: out to where it parts from the knot's pavements, AND out to where its
            // structure ends - the deck is one slab to its abutments. (The first cut stopped at
            // the knot's own pavements: 13 of the Tyvola Road deck's 50 findings and the Freedom
            // Drive ramp's 5.5 m hole stood 1-12 m past that, where two arms of one node still
            // run over each other.) At most SepMaxM of road from the knot, on through the nodes
            // a deck's short pieces end at.
            var walk = new List<(int e, int node, float travelled)>();
            foreach (var a in arms) walk.Add((a.e, a.node, 0f));
            var walked = new HashSet<int>(inner);
            for (int wi = 0; wi < walk.Count; wi++)
            {
                var (ai, node, travelled) = walk[wi];
                if (!walked.Add(ai)) continue;
                var e = map.edges[ai];
                float reach = Mathf.Min(e.length, SepMaxM - travelled), sep = 0f, run = 0f;
                if (reach <= 0f) continue;
                if (mode == 0)
                    for (float d = 1f; d <= reach; d += 1f)
                    {
                        if (!e.ElevatedAt(e.a == node ? d : e.length - d)) break;
                        run = d;
                    }
                if (travelled == 0f)
                    for (float d = 1f; d <= reach; d += 1f)
                    {
                        float s = e.a == node ? d : e.length - d;
                        CrossSection(e, s, out var L, out var R);
                        bool hit = false;
                        for (int q = 0; q < 5 && !hit; q++)
                        {
                            var p = Vector2.Lerp(L, R, q / 4f);
                            foreach (int oi in inner) if (InRibbon(map.edges[oi], p, 0f, out _)) { hit = true; break; }
                            if (hit) break;
                            foreach (var (bi, bnode) in arms)
                            {
                                if (bi == ai || bnode == node) continue;
                                var o = map.edges[bi];
                                if (!InRibbon(o, p, 0f, out float at)) continue;
                                if ((o.a == bnode ? at : o.length - at) > SepMaxM) continue;
                                hit = true; break;
                            }
                        }
                        if (hit) sep = d;
                    }
                bool whole = run >= e.length - 1f;
                float upTo = Mathf.Min(e.length, Mathf.Max(sep + 1f, whole ? e.length : run));
                int k = Mathf.Max(1, Mathf.CeilToInt(upTo / 2f));
                for (int i = 0; i <= k; i++)
                {
                    float d = upTo * i / k, s = e.a == node ? d : e.length - d;
                    CrossSection(e, s, out var L, out var R);
                    pts.Add(L); pts.Add(R);
                }
                if (!whole || travelled + e.length >= SepMaxM) continue;
                int far = e.a == node ? e.b : e.a;
                foreach (int oi in map.nodeEdges[far])
                {
                    var o = map.edges[oi];
                    if (oi == ai || o.a == o.b || o.tunnel || o.stS == null || o.stS.Length < 2) continue;
                    walk.Add((oi, far, travelled + e.length));
                }
            }
            foreach (int n in pl.nodes) pts.Add(map.nodes[n]);
            var hull = ConvexHull(pts);
            if (hull.Count < 3) { pl.skipped = "no area"; return false; }
            pl.hull = Grow(hull, mode == 1 ? MarginM + 10f : MarginM);
            pl.bbMin = pl.hull[0]; pl.bbMax = pl.hull[0];
            foreach (var p in pl.hull) { pl.bbMin = Vector2.Min(pl.bbMin, p); pl.bbMax = Vector2.Max(pl.bbMax, p); }
            if (Mathf.Max(pl.bbMax.x - pl.bbMin.x, pl.bbMax.y - pl.bbMin.y) > MaxExtentM) { pl.skipped = $"{Mathf.Max(pl.bbMax.x - pl.bbMin.x, pl.bbMax.y - pl.bbMin.y):0} m across (over {MaxExtentM:0} m)"; return false; }

            // ---- the roads at this level, by graph distance from the plate
            var scans = new Dictionary<int, EdgeScan>();
            EdgeScan Scan(CityMap.Edge e)
            {
                if (scans.TryGetValue(e.index, out var sc)) return sc;
                int n = Mathf.Max(2, Mathf.CeilToInt(e.length / SampleM) + 1);
                sc = new EdgeScan { s = new float[n], ins = new bool[n], outA = float.MaxValue, outB = float.MaxValue };
                for (int i = 0; i < n; i++)
                {
                    float s = i == n - 1 ? e.length : i * e.length / (n - 1);
                    sc.s[i] = s;
                    if (Mathf.Abs(e.YAt(s) - pl.yRef) > LevelTolM) continue;
                    CrossSection(e, s, out var L, out var R);
                    sc.ins[i] = SegHitsHull(pl.hull, L, R);
                    if (sc.ins[i]) { sc.any = true; if (sc.outA == float.MaxValue) sc.outA = s; sc.outB = e.length - s; }
                }
                scans[e.index] = sc;
                return sc;
            }
            var dist = new Dictionary<int, float>();
            var open = new List<int>();
            foreach (int n in pl.nodes) dist[n] = 0f;
            var done = new HashSet<int>();
            var level = new SortedSet<int>();
            var dds = new Dictionary<int, float[]>();
            float Dn(int n) => dist.TryGetValue(n, out float v) ? v : float.MaxValue;
            // SEATS MOVE WITH THEIR HOSTS. A ramp seated on another road (CityElevation's seats: it
            // runs inside that road's pavement at that road's height) must be eased exactly as the
            // road is, there, or the two part again: the first cut eased each by its own distance
            // along the graph, and where a ramp coming FROM the plate merges into one that does not
            // (the Brookshire deck's e2893 into e9799, 60 m out) the pair stood 0.39 m apart. So a
            // seated station and its host's point take the NEARER of their two distances, and that
            // distance runs on along both (a seed on the edge), round by round until none moves.
            var seeds = new Dictionary<int, List<(float s, float d)>>();
            var seatOf = new Dictionary<long, (int host, float hostS)>();
            float At(float[] dd, EdgeScan sc, float len, float s) => dd[Mathf.Clamp(Mathf.RoundToInt(s / Mathf.Max(len, 1e-3f) * (sc.s.Length - 1)), 0, sc.s.Length - 1)];
            void AddSeed(int ei, float s, float d)
            {
                if (!seeds.TryGetValue(ei, out var l)) seeds[ei] = l = new List<(float, float)>();
                for (int i = 0; i < l.Count; i++)
                    if (Mathf.Abs(l[i].s - s) < 1f) { if (d < l[i].d) l[i] = (s, d); return; }
                l.Add((s, d));
            }
            for (int round = 0; round < 8; round++)
            {
                // (a) the nodes, from every distance in hand
                open.Clear(); done.Clear();
                foreach (var kv in dist) open.Add(kv.Key);
                open.Sort();
                while (open.Count > 0)
                {
                    int bi = 0;
                    for (int i = 1; i < open.Count; i++) if (dist[open[i]] < dist[open[bi]]) bi = i;
                    int n = open[bi]; open.RemoveAt(bi);
                    if (!done.Add(n)) continue;
                    float d = dist[n];
                    if (d >= blend) continue;
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.a == e.b || e.tunnel || e.stS == null || e.stS.Length < 2) continue;
                        level.Add(ei);
                        var sc = Scan(e);
                        void Relax(int m, float dm)
                        {
                            if (done.Contains(m)) return;
                            if (!dist.TryGetValue(m, out float old) || dm < old) { dist[m] = dm; open.Add(m); }
                        }
                        if (sc.any) { Relax(e.a, sc.outA); Relax(e.b, sc.outB); }
                        else Relax(n == e.a ? e.b : e.a, d + e.length);
                    }
                }
                // (b) every level road's distance from the plate, sample by sample: from its nodes,
                // from its own samples inside the plate, from its seeds
                bool changed = false;
                foreach (int ei in new List<int>(level))
                {
                    var e = map.edges[ei];
                    var sc = Scan(e);
                    int n = sc.s.Length;
                    var dd = new float[n];
                    float f = Dn(e.a);
                    for (int i = 0; i < n; i++)
                    {
                        if (sc.ins[i]) f = 0f; else if (i > 0 && f < float.MaxValue) f += sc.s[i] - sc.s[i - 1];
                        dd[i] = f;
                    }
                    f = Dn(e.b);
                    for (int i = n - 1; i >= 0; i--)
                    {
                        if (sc.ins[i]) f = 0f; else if (i < n - 1 && f < float.MaxValue) f += sc.s[i + 1] - sc.s[i];
                        dd[i] = Mathf.Min(dd[i], f);
                    }
                    if (seeds.TryGetValue(ei, out var sl))
                        foreach (var (ss, sd) in sl)
                            for (int i = 0; i < n; i++) { float v = sd + Mathf.Abs(sc.s[i] - ss); if (v < dd[i]) dd[i] = v; }
                    dds[ei] = dd;
                    // (a seed can bring an end nearer than its node stood)
                    if (dd[0] < Dn(e.a) - 0.5f) { dist[e.a] = dd[0]; changed = true; }
                    if (dd[n - 1] < Dn(e.b) - 0.5f) { dist[e.b] = dd[n - 1]; changed = true; }
                }
                // (c) the seats: of every road at the level, and of every road that meets one at a
                // node (a ramp seated ON a level road is not itself reached by the ease: e299 on the
                // Freedom Drive ramps stood 16 cm over its host for 30 m)
                var cand = new SortedSet<int>(level);
                foreach (int li in level)
                {
                    foreach (int oi in map.nodeEdges[map.edges[li].a]) cand.Add(oi);
                    foreach (int oi in map.nodeEdges[map.edges[li].b]) cand.Add(oi);
                }
                foreach (int ei in cand)
                {
                    var e = map.edges[ei];
                    if (e.stSeat == null || e.a == e.b || e.tunnel || e.stS == null || e.stS.Length < 2) continue;
                    dds.TryGetValue(ei, out var dd); scans.TryGetValue(ei, out var sc);
                    for (int st = 0; st < e.stS.Length; st++)
                    {
                        if (!e.stSeat[st]) continue;
                        long key = ((long)ei << 20) | (uint)st;
                        if (!seatOf.TryGetValue(key, out var ho)) { int h = CityElevation.SeatHost(ei, st, out float hs); ho = (h, hs); seatOf[key] = ho; }
                        if (ho.host < 0 || ho.host == ei) continue;
                        var H = map.edges[ho.host];
                        if (H.tunnel || H.stS == null || H.stS.Length < 2 || Mathf.Abs(H.YAt(ho.hostS) - e.YAt(e.stS[st])) > 0.7f || Mathf.Abs(H.YAt(ho.hostS) - pl.yRef) > LevelTolM) continue;
                        float dE = dd != null ? At(dd, sc, e.length, e.stS[st]) : float.MaxValue;
                        float dH = dds.TryGetValue(ho.host, out var ddH) ? At(ddH, scans[ho.host], H.length, ho.hostS) : float.MaxValue;
                        if (dE < blend && dE + 0.75f < dH) { AddSeed(ho.host, ho.hostS, dE); level.Add(ho.host); Scan(H); changed = true; }
                        else if (dH < blend && dH + 0.75f < dE) { AddSeed(ei, e.stS[st], dH); level.Add(ei); Scan(e); changed = true; }
                    }
                }
                if (!changed) break;
            }
            // (only the roads the ease reaches)
            foreach (int ei in new List<int>(dds.Keys))
            {
                bool touched = false;
                foreach (float v in dds[ei]) if (v < blend) { touched = true; break; }
                if (!touched) dds.Remove(ei);
            }

            // ---- H: the mean of what stands inside the plate; and no piece of deck over a
            // lower road comes down under the clearance the solve stands a deck at (NeedM
            // over the lower road's surface), nor at all where it already has less
            double sum = 0, wsum = 0;
            var lower = new List<CityMap.Edge>();
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(pl.bbMin - Vector2.one * (blend + 10f), pl.bbMax + Vector2.one * (blend + 10f), segs);
            var seen = new HashSet<int>();
            foreach (int packed in segs)
            {
                int oi = packed >> 12;
                if (!seen.Add(oi) || level.Contains(oi)) continue;
                var o = map.edges[oi];
                if (o.stS == null || o.stS.Length < 2) continue;
                lower.Add(o);
            }
            lower.Sort((x, y) => x.index.CompareTo(y.index));
            pl.lowerRoads = lower.Count;
            float UnderY(Vector2 p, float yTop)
            {
                float best = float.MinValue;
                foreach (var o in lower)
                    if (InRibbon(o, p, 0.5f, out float at)) { float yo = o.YAt(at); if (yo < yTop - 2.5f && yo > best) best = yo; }
                return best;
            }
            float hOver = float.MinValue, underMax = float.MinValue;
            foreach (var kv in dds)
            {
                var e = map.edges[kv.Key];
                var sc = scans[kv.Key];
                var dd = kv.Value;
                for (int i = 0; i < sc.s.Length; i++)
                {
                    float y = e.YAt(sc.s[i]);
                    if (sc.ins[i]) { sum += y; wsum += 1; }
                    if (i % 2 != 0 || dd[i] >= blend) continue;   // every metre
                    if (!sc.ins[i] && !e.ElevatedAt(sc.s[i])) continue;
                    float w = 1f - U(dd[i] / blend);
                    if (w < 1e-3f) continue;
                    CrossSection(e, sc.s[i], out var L, out var R);
                    for (int q = 0; q < 3; q++)
                    {
                        float yu = UnderY(Vector2.Lerp(L, R, q * 0.5f), y);
                        if (yu == float.MinValue) continue;
                        if (sc.ins[i]) underMax = Mathf.Max(underMax, yu);
                        float spare = Mathf.Max(0f, y - (yu + NeedM));
                        hOver = Mathf.Max(hOver, y - (spare + 0.02f) / w);
                    }
                }
            }
            if (wsum < 1) { pl.skipped = "nothing inside"; return false; }
            pl.hFit = (float)(sum / wsum);
            pl.hOver = hOver; pl.underMax = underMax;
            pl.H = Mathf.Max(pl.hFit, hOver);

            // ---- the new drawn profiles
            var prof = new List<(int e, float[] s, float[] y)>();
            float raise = 0f, lowerMax = 0f, longest = 0f; int lowOver = 0;
            var ks = new List<float>(64);
            foreach (var kv in dds)
            {
                int ei = kv.Key;
                var e = map.edges[ei];
                var sc = scans[ei];
                int n = sc.s.Length;
                var dd = kv.Value;
                float DAt(float s)
                {
                    float u = s / Mathf.Max(e.length, 1e-3f) * (n - 1);
                    int k = Mathf.Clamp((int)u, 0, n - 2);
                    float a = dd[k], b = dd[k + 1];
                    if (a == float.MaxValue || b == float.MaxValue) return Mathf.Min(a, b) == float.MaxValue ? float.MaxValue : Mathf.Min(a, b) + SampleM;
                    return Mathf.Lerp(a, b, Mathf.Clamp01(u - k));
                }
                ks.Clear();
                var baseS = e.vS ?? e.stS;
                foreach (float s in baseS) ks.Add(s);
                ks.Add(0f); ks.Add(e.length);
                // the rim, exactly: where the edge's cross-section first touches the plate
                for (int i = 0; i + 1 < n; i++)
                {
                    if (sc.ins[i] == sc.ins[i + 1]) continue;
                    float lo = sc.s[i], hi = sc.s[i + 1]; bool loIn = sc.ins[i];
                    for (int it = 0; it < 12; it++)
                    {
                        float mid = 0.5f * (lo + hi);
                        CrossSection(e, mid, out var L, out var R);
                        bool min = Mathf.Abs(e.YAt(mid) - pl.yRef) <= LevelTolM && SegHitsHull(pl.hull, L, R);
                        if (min == loIn) lo = mid; else hi = mid;
                    }
                    // the knot on the inside of the crossing: the plate's height holds to it
                    ks.Add(loIn ? lo : hi);
                }
                for (int i = 0; i < n; i++)
                    // a knot every 2 m over the ease's first two fifths (where it bends most: under 0.25 % a
                    // metre, within the drawn profile's 0.45 % a knot), every 4 m beyond
                    if (dd[i] > 0f && dd[i] < blend + 2f && i % (dd[i] < 0.4f * blend ? 4 : 8) == 0) ks.Add(sc.s[i]);
                ks.Sort();
                var outS = new List<float>(ks.Count); var outY = new List<float>(ks.Count);
                for (int i = 0; i < ks.Count; i++)
                {
                    float s = Mathf.Clamp(ks[i], 0f, e.length);
                    if (outS.Count == 0 || s - outS[outS.Count - 1] >= 0.2f) outS.Add(s);
                }
                outS[0] = 0f;
                if (outS.Count > 1 && e.length - outS[outS.Count - 1] < 0.2f) outS[outS.Count - 1] = e.length;
                else outS.Add(e.length);
                if (outS.Count < 2) continue;
                for (int i = 0; i < outS.Count; i++) outY.Add(0f);
                for (int i = 0; i < outS.Count; i++)
                {
                    float s = outS[i], y0 = e.YAt(s), d = DAt(s);
                    float w = d >= blend ? 0f : 1f - U(d / blend);
                    float y = y0 + w * (pl.H - y0);
                    outY[i] = y;
                    raise = Mathf.Max(raise, y - y0); lowerMax = Mathf.Max(lowerMax, y0 - y);
                    if (w > 0f && d > longest) longest = d;
                    if (y < y0 - 0.03f && (d <= 0f || e.ElevatedAt(s)))
                    {
                        float yu = UnderY(e.PointAt(s), y0);
                        if (yu != float.MinValue && y < yu + NeedM) lowOver++;
                    }
                }
                prof.Add((ei, outS.ToArray(), outY.ToArray()));
            }
            pl.maxRaise = raise; pl.maxLower = lowerMax; pl.longestBlend = Mathf.Min(longest, blend); pl.loweredOverRoad = lowOver;
            if (raise > MaxRaiseM) { pl.skipped = $"would raise the deck {raise:0.00} m (over {MaxRaiseM:0.0} m)"; return false; }

            if (!write) return true;
            // ---- write (the solve's stations stay; the drawn profile and the nodes move)
            foreach (var (ei, s, y) in prof)
            {
                var e = map.edges[ei];
                if (!savedProf.ContainsKey(ei)) savedProf[ei] = (e.vS, e.vY);
                e.vS = s; e.vY = y;
                pl.levelEdges.Add(ei);
            }
            pl.edgesTouched = prof.Count;
            foreach (var kv in dist)
            {
                if (kv.Value >= blend) continue;
                int n = kv.Key;
                float y0 = map.nodeY[n], w = 1f - U(kv.Value / blend);
                if (!savedNodeY.ContainsKey(n)) savedNodeY[n] = y0;
                map.nodeY[n] = y0 + w * (pl.H - y0);
            }
            return true;
        }

        /// <summary>The plate whose rim a plan point lies inside (heights in the map), or null.</summary>
        public static Plate PlateAt(Vector2 p)
        {
            if (!On) return null;
            for (int i = 0; i < Plates.Count; i++)
            {
                var pl = Plates[i];
                if (pl.skipped != null || pl.hull == null || p.x < pl.bbMin.x || p.x > pl.bbMax.x || p.y < pl.bbMin.y || p.y > pl.bbMax.y) continue;
                if (InHull(pl.hull, p)) return pl;
            }
            return null;
        }

        /// <summary>The plates, one line each (the probe and the hand-off read it).</summary>
        public static string Report(CityMap map)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"DECK PLATES: {Plates.Count} knots found, {Plates.FindAll(p => p.skipped == null).Count} plated; env {(EnvOn ? "on" : "OFF")}, heights {(On ? "in" : "out")}, slab {(CutOn ? "on" : "off")}; {BuildMs:0} ms{(lastError.Length > 0 ? "; ERROR " + lastError : "")}");
            foreach (var p in Plates)
            {
                float lat = 35.18456015184093f + p.centre.y / 111132f, lon = -80.81770185962013f + p.centre.x / (111320f * Mathf.Cos(35.18456f * Mathf.Deg2Rad));
                string names = "";
                if (map != null && p.inner != null)
                {
                    var seen = new List<string>();
                    foreach (int ei in p.inner) { string nm = map.edges[ei].name; if (!string.IsNullOrEmpty(nm) && !seen.Contains(nm)) seen.Add(nm); }
                    if (p.arms != null) foreach (int ei in p.arms) { string nm = map.edges[ei].name; if (!string.IsNullOrEmpty(nm) && !seen.Contains(nm)) seen.Add(nm); }
                    names = string.Join(" / ", seen);
                }
                sb.Append($"PLATE {p.id} ({p.centre.x:0},{p.centre.y:0}) {lat:0.0000},{lon:0.0000} '{names}' nodes {p.nodes.Length} inner {(p.inner != null ? p.inner.Length : 0)} arms {(p.arms != null ? p.arms.Length : 0)}");
                if (p.skipped != null) { sb.AppendLine(" SKIPPED: " + p.skipped); continue; }
                sb.AppendLine($" hull {p.hull.Length} pts {p.bbMax.x - p.bbMin.x:0}x{p.bbMax.y - p.bbMin.y:0} m; H {p.H:0.00} (mean inside {p.hFit:0.00}, clearance floor {(p.hOver > -1e9f ? p.hOver.ToString("0.00") : "-")}, the road below up to {(p.underMax > -1e9f ? p.underMax.ToString("0.00") : "-")}: {(p.underMax > -1e9f ? (p.H - p.underMax).ToString("0.00") : "-")} m over it); raise up to {p.maxRaise:0.00}, lower up to {p.maxLower:0.00}; {p.edgesTouched} edges, ease {p.blend:0} m ({ModeNames[p.mode]}); stations brought under the clearance {p.loweredOverRoad}; inner e{string.Join(",e", p.inner)}");
            }
            return sb.ToString();
        }
    }
}
