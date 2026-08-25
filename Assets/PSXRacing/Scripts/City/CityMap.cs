using System;
using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// The Charlotte road network, parsed from Resources/charlotte_city.json
    /// (baked by tools/city/export_charlotte.mjs out of Racing-Game-2's OSM
    /// import + hand-traced water).
    ///
    /// This is DATA, not scene: ~7,300 edges, ~5,400 nodes, water bodies,
    /// grade separations and water bridge spans, in real metres around the
    /// I-485 centroid (x east, z north). CityWorld streams tile meshes out of
    /// it; nothing here touches a GameObject.
    ///
    /// The one scale knob is <see cref="LayoutScale"/>: it multiplies graph
    /// GEOMETRY only, at parse time. Road widths, lane widths and building
    /// sizes are section-currency (the car is real-size at any layout scale)
    /// and are never multiplied. RG2 ran this city at layout 1:6; here it is
    /// 1:1 — see Docs/CHARLOTTE.md.
    /// </summary>
    public class CityMap
    {
        public const float LayoutScale = 1.0f;

        // ---- JSON schema (mirrors export_charlotte.mjs) -------------------
        [Serializable] public class EdgeJson
        {
            public int a, b;
            public string name;
            public int cls, link, z, lanes;
            public float w, med;
            public int medGrass, oneway, deck;
            public float[] pts;
        }
        [Serializable] public class IsectJson { public int n, c; }
        [Serializable] public class WaterJson { public string name; public float w; public int lake; public float[] pts; }
        [Serializable] public class CrossJson { public int over, under; public float x, z; }
        [Serializable] public class WSpanJson { public int e; public float s0, s1; }
        [Serializable] public class CityJson
        {
            public string attribution;
            public float uptownX, uptownZ;
            public float[] nodes;
            public EdgeJson[] edges;
            public IsectJson[] isects;
            public WaterJson[] waters;
            public CrossJson[] crossings;
            public WSpanJson[] wspans;
        }

        // ---- runtime graph ------------------------------------------------
        public class Edge
        {
            public int index;
            public int a, b;
            public string name;
            /// <summary>1 tertiary … 5 motorway (link keeps its base class).</summary>
            public int cls;
            public bool link;
            public int z;
            public int lanes;
            public float width;      // full paved width, metres (section scale)
            public float median;     // painted median width inside it
            public bool medianGrass;
            public bool oneway;
            public bool deckFlag;

            public Vector2[] pts;    // plan polyline, metres
            public float[] s;        // cumulative arc length per pt
            public float length;

            // Elevation stations, every ~StationStep metres along the edge
            // (solved once at load by CityElevation).
            public float[] stS;      // arc position of each station
            public float[] stY;      // road surface height
            public bool[] stElev;    // true where the road is ON STRUCTURE
                                     // (bridge/overpass): deck mesh, no ground pin

            public float CorridorHalf => width * 0.5f + 6.5f;

            public Vector2 PointAt(float at)
            {
                at = Mathf.Clamp(at, 0f, length);
                int i = SegmentAt(at, out float t);
                return Vector2.LerpUnclamped(pts[i], pts[i + 1], t);
            }

            public Vector2 TangentAt(float at)
            {
                int i = SegmentAt(Mathf.Clamp(at, 0f, length), out _);
                Vector2 d = pts[i + 1] - pts[i];
                float m = d.magnitude;
                return m > 1e-5f ? d / m : Vector2.up;
            }

            public int SegmentAt(float at, out float t)
            {
                int lo = 0, hi = s.Length - 2;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (s[mid] <= at) lo = mid; else hi = mid - 1;
                }
                float seg = s[lo + 1] - s[lo];
                t = seg > 1e-6f ? (at - s[lo]) / seg : 0f;
                return lo;
            }

            /// <summary>Road surface height at an arc position, from the solved
            /// stations.</summary>
            public float YAt(float at)
            {
                at = Mathf.Clamp(at, 0f, length);
                int lo = 0, hi = stS.Length - 2;
                if (hi < 0) return stY.Length > 0 ? stY[0] : 0f;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (stS[mid] <= at) lo = mid; else hi = mid - 1;
                }
                float seg = stS[lo + 1] - stS[lo];
                float t = seg > 1e-6f ? (at - stS[lo]) / seg : 0f;
                return Mathf.LerpUnclamped(stY[lo], stY[lo + 1], t);
            }

            public bool ElevatedAt(float at)
            {
                if (stS == null || stS.Length == 0) return false;
                at = Mathf.Clamp(at, 0f, length);
                int lo = 0, hi = stS.Length - 2;
                if (hi < 0) return stElev[0];
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (stS[mid] <= at) lo = mid; else hi = mid - 1;
                }
                return stElev[lo] || stElev[Mathf.Min(lo + 1, stElev.Length - 1)];
            }
        }

        public class Water
        {
            public string name;
            public float width;
            public bool lake;
            public Vector2[] pts;
            public float surfaceY; // solved by CityElevation (flat per lake)
            public Vector2 bbMin, bbMax;
        }

        public struct Crossing { public int over, under; public Vector2 at; }
        public struct WaterSpan { public int edge; public float s0, s1; }

        public string attribution;
        public Vector2 uptown;
        public Vector2[] nodes;
        public float[] nodeY;
        public int[] nodeControl;         // 0 none, 1 yield, 2 stop, 4 signal
        public Edge[] edges;
        public List<int>[] nodeEdges;     // edges touching each node
        public Water[] waters;
        public Crossing[] crossings;
        public WaterSpan[] wspans;

        // ---- spatial hash over edge SEGMENTS ------------------------------
        public const float Cell = 64f;
        readonly Dictionary<long, List<int>> segCells = new Dictionary<long, List<int>>();
        // Entries pack (edge << 12 | segment) — supports 4096 segments per edge.
        static long CellKey(int cx, int cz) => ((long)cx << 24) ^ (cz & 0xFFFFFF);
        public static int PackSeg(int edge, int seg) => (edge << 12) | seg;

        readonly Dictionary<long, List<int>> waterCells = new Dictionary<long, List<int>>();

        static CityMap loaded;
        /// <summary>The parsed map, loaded once per process. ~1.5 MB of JSON;
        /// synchronous Resources.Load, the one loading path this project
        /// trusts on WebGL.</summary>
        public static CityMap Get()
        {
            if (loaded != null) return loaded;
            var ta = Resources.Load<TextAsset>("charlotte_city");
            if (ta == null) { Debug.LogError("charlotte_city.json missing from Resources"); return null; }
            loaded = Parse(ta.text);
            Resources.UnloadAsset(ta);
            return loaded;
        }

        public static CityMap Parse(string json)
        {
            var raw = JsonUtility.FromJson<CityJson>(json);
            var map = new CityMap();
            map.attribution = raw.attribution;
            map.uptown = new Vector2(raw.uptownX, raw.uptownZ) * LayoutScale;

            int nn = raw.nodes.Length / 2;
            map.nodes = new Vector2[nn];
            for (int i = 0; i < nn; i++)
                map.nodes[i] = new Vector2(raw.nodes[i * 2], raw.nodes[i * 2 + 1]) * LayoutScale;

            map.nodeControl = new int[nn];
            foreach (var ic in raw.isects)
                if (ic.n >= 0 && ic.n < nn) map.nodeControl[ic.n] = ic.c;

            map.edges = new Edge[raw.edges.Length];
            map.nodeEdges = new List<int>[nn];
            for (int i = 0; i < nn; i++) map.nodeEdges[i] = new List<int>(3);

            for (int i = 0; i < raw.edges.Length; i++)
            {
                var ej = raw.edges[i];
                int np = ej.pts.Length / 2;
                var e = new Edge
                {
                    index = i,
                    a = ej.a, b = ej.b,
                    name = ej.name ?? "",
                    cls = ej.cls, link = ej.link != 0, z = ej.z,
                    lanes = Mathf.Max(1, ej.lanes),
                    width = Mathf.Max(4f, ej.w),
                    median = ej.med,
                    medianGrass = ej.medGrass != 0,
                    oneway = ej.oneway != 0,
                    deckFlag = ej.deck != 0,
                    pts = new Vector2[np],
                    s = new float[np],
                };
                for (int p = 0; p < np; p++)
                    e.pts[p] = new Vector2(ej.pts[p * 2], ej.pts[p * 2 + 1]) * LayoutScale;
                float acc = 0f;
                for (int p = 1; p < np; p++)
                {
                    acc += Vector2.Distance(e.pts[p - 1], e.pts[p]);
                    e.s[p] = acc;
                }
                e.length = acc;
                map.edges[i] = e;
                if (e.a >= 0 && e.a < nn) map.nodeEdges[e.a].Add(i);
                if (e.b >= 0 && e.b < nn) map.nodeEdges[e.b].Add(i);
            }

            map.waters = new Water[raw.waters.Length];
            for (int i = 0; i < raw.waters.Length; i++)
            {
                var wj = raw.waters[i];
                int np = wj.pts.Length / 2;
                var w = new Water
                {
                    name = wj.name ?? "",
                    width = Mathf.Max(4f, wj.w),
                    lake = wj.lake != 0,
                    pts = new Vector2[np],
                };
                var mn = new Vector2(float.MaxValue, float.MaxValue);
                var mx = new Vector2(float.MinValue, float.MinValue);
                for (int p = 0; p < np; p++)
                {
                    w.pts[p] = new Vector2(wj.pts[p * 2], wj.pts[p * 2 + 1]) * LayoutScale;
                    mn = Vector2.Min(mn, w.pts[p]); mx = Vector2.Max(mx, w.pts[p]);
                }
                w.bbMin = mn; w.bbMax = mx;
                map.waters[i] = w;
            }

            map.crossings = new Crossing[raw.crossings.Length];
            for (int i = 0; i < raw.crossings.Length; i++)
                map.crossings[i] = new Crossing
                {
                    over = raw.crossings[i].over,
                    under = raw.crossings[i].under,
                    at = new Vector2(raw.crossings[i].x, raw.crossings[i].z) * LayoutScale,
                };

            map.wspans = new WaterSpan[raw.wspans.Length];
            for (int i = 0; i < raw.wspans.Length; i++)
                map.wspans[i] = new WaterSpan
                {
                    edge = raw.wspans[i].e,
                    s0 = raw.wspans[i].s0 * LayoutScale,
                    s1 = raw.wspans[i].s1 * LayoutScale,
                };

            map.BuildHashes();
            CityElevation.Solve(map);
            return map;
        }

        void BuildHashes()
        {
            foreach (var e in edges)
            {
                for (int i = 0; i + 1 < e.pts.Length; i++)
                {
                    ForCellsOnSeg(e.pts[i], e.pts[i + 1], (cx, cz) =>
                    {
                        long k = CellKey(cx, cz);
                        if (!segCells.TryGetValue(k, out var list)) segCells[k] = list = new List<int>(4);
                        list.Add(PackSeg(e.index, i));
                    });
                }
            }
            for (int w = 0; w < waters.Length; w++)
            {
                var wt = waters[w];
                for (int i = 0; i + 1 < wt.pts.Length; i++)
                {
                    ForCellsOnSeg(wt.pts[i], wt.pts[i + 1], (cx, cz) =>
                    {
                        long k = CellKey(cx, cz);
                        if (!waterCells.TryGetValue(k, out var list)) waterCells[k] = list = new List<int>(4);
                        list.Add(PackSeg(w, i));
                    });
                }
                // a lake polygon also needs its INTERIOR cells registered, so a
                // ground vertex in the middle of the lake finds it
                if (wt.lake)
                {
                    int x0 = Mathf.FloorToInt(wt.bbMin.x / Cell), x1 = Mathf.FloorToInt(wt.bbMax.x / Cell);
                    int z0 = Mathf.FloorToInt(wt.bbMin.y / Cell), z1 = Mathf.FloorToInt(wt.bbMax.y / Cell);
                    for (int cx = x0; cx <= x1; cx++)
                        for (int cz = z0; cz <= z1; cz++)
                        {
                            var centre = new Vector2((cx + 0.5f) * Cell, (cz + 0.5f) * Cell);
                            if (!PointInPoly(wt.pts, centre)) continue;
                            long k = CellKey(cx, cz);
                            if (!waterCells.TryGetValue(k, out var list)) waterCells[k] = list = new List<int>(4);
                            list.Add(PackSeg(w, 0));
                        }
                }
            }
        }

        static void ForCellsOnSeg(Vector2 a, Vector2 b, Action<int, int> visit)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / Cell);
            int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / Cell);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                    visit(cx, cz);
        }

        /// <summary>Visit every (edge, segment) whose segment's cells overlap
        /// the world-space rectangle, deduplicated per edge-segment.</summary>
        public void EdgeSegsInRect(Vector2 min, Vector2 max, HashSet<int> outSegs)
        {
            int x0 = Mathf.FloorToInt(min.x / Cell), x1 = Mathf.FloorToInt(max.x / Cell);
            int z0 = Mathf.FloorToInt(min.y / Cell), z1 = Mathf.FloorToInt(max.y / Cell);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                    if (segCells.TryGetValue(CellKey(cx, cz), out var list))
                        foreach (var p in list) outSegs.Add(p);
        }

        public void WaterSegsInRect(Vector2 min, Vector2 max, HashSet<int> outSegs)
        {
            int x0 = Mathf.FloorToInt(min.x / Cell), x1 = Mathf.FloorToInt(max.x / Cell);
            int z0 = Mathf.FloorToInt(min.y / Cell), z1 = Mathf.FloorToInt(max.y / Cell);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                    if (waterCells.TryGetValue(CellKey(cx, cz), out var list))
                        foreach (var p in list) outSegs.Add(p);
        }

        readonly HashSet<int> nearScratch = new HashSet<int>();

        /// <summary>
        /// Nearest point on the road network, for respawn and the HUD street
        /// name. Returns false only when nothing is within <paramref name="r"/>.
        /// Ramps are skipped when <paramref name="skipLinks"/> — a beached car
        /// belongs back on a street, not on a slip road's nose.
        /// </summary>
        public bool NearestRoadPoint(Vector2 p, float r, bool skipLinks,
            out int edgeIdx, out float arcS, out float dist)
        {
            edgeIdx = -1; arcS = 0f; dist = float.MaxValue;
            nearScratch.Clear();
            EdgeSegsInRect(new Vector2(p.x - r, p.y - r), new Vector2(p.x + r, p.y + r), nearScratch);
            foreach (var packed in nearScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = edges[ei];
                if (skipLinks && e.link) continue;
                Vector2 a = e.pts[si], b = e.pts[si + 1];
                Vector2 d = b - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                Vector2 q = a + d * t;
                float dd = Vector2.Distance(p, q);
                if (dd < dist)
                {
                    dist = dd;
                    edgeIdx = ei;
                    arcS = e.s[si] + Mathf.Sqrt(L2) * t;
                }
            }
            return edgeIdx >= 0 && dist <= r;
        }

        public static bool PointInPoly(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
