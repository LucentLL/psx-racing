using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// The Charlotte road network, parsed from Resources/charlotte_city.bytes
    /// (baked by tools/city/export_osm.mjs straight out of OpenStreetMap).
    ///
    /// This is DATA, not scene: ~25,000 edges between ~20,000 real junction
    /// nodes, the traced creeks and lakes, every grade separation with OSM's
    /// word on which road is on top, the water spans, the three race routes
    /// and (from charlotte_bld.bytes) 32,000 building footprints — all in
    /// real metres around the I-485 centroid (x east, z north). CityWorld
    /// streams tile meshes out of it; nothing here touches a GameObject.
    ///
    /// Binary rather than JSON since the graph grew fivefold: a 2.5 MB blob
    /// reads in a few hundred milliseconds through a BinaryReader where the
    /// equivalent JSON would be 9 MB and a second and a half of parsing on a
    /// phone. The exporter writes exactly the layout the reader below walks;
    /// the magic + version at the top is what turns a mismatch into an error
    /// message instead of a graph of garbage.
    ///
    /// The one scale knob is <see cref="LayoutScale"/>: it multiplies graph
    /// GEOMETRY only, at parse time. Road widths, lane widths and building
    /// sizes are section-currency (the car is real-size at any layout scale)
    /// and are never multiplied.
    /// </summary>
    public class CityMap
    {
        public const float LayoutScale = 1.0f;
        const uint MagicCity = 0x43585350;   // "PSXC"
        const uint MagicBld = 0x444C4250;    // "PBLD"

        // ---- runtime graph ------------------------------------------------
        public class Edge
        {
            public int index;
            public int a, b;
            public string name;
            /// <summary>0 local street … 5 motorway (a link keeps its base class).</summary>
            public int cls;
            public bool link;
            public bool oneway;      // pts run in the direction of travel
            public bool bridge;      // OSM bridge=yes: the whole edge is a deck
            public bool tunnel;
            public bool turnLane;    // two-way with a centre turn lane
            public bool roundabout;
            public int lanes;
            public int layer;        // OSM stacking level; decides over/under
            public int profile;      // RoadProfiles.All index
            public float width;      // full paved width, metres (section scale)
            public float shl, shr;   // paved shoulders, left / right of travel
            public int speedKmh;
            public uint wayId;

            public Vector2[] pts;    // plan polyline, metres
            public float[] s;        // cumulative arc length per pt
            public float length;

            // Elevation stations, every ~StationStep metres along the edge
            // (solved once at load by CityElevation).
            public float[] stS;      // arc position of each station
            public float[] stY;      // road surface height
            public bool[] stElev;    // true where the road is ON STRUCTURE
                                     // (bridge/overpass): deck mesh, no ground pin
            /// <summary>How far each station stands above the chord of its
            /// neighbours — the crest of a grade break, measured by
            /// CityElevation.MeasureCrests. The ground lattice is straight
            /// between its vertices and the road bends here, so the land
            /// under a crest is sunk by this much extra.</summary>
            public float[] stCrest;

            /// <summary>How far either side of the centreline the land is
            /// graded to the road. Wider than the pavement so the verge and
            /// the median between two carriageways come out level.</summary>
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

            /// <summary>The crest allowance at an arc position (see
            /// <see cref="stCrest"/>): zero on a straight grade.</summary>
            public float CrestAt(float at)
            {
                if (stCrest == null || stCrest.Length == 0) return 0f;
                at = Mathf.Clamp(at, 0f, length);
                int lo = 0, hi = stS.Length - 2;
                if (hi < 0) return stCrest[0];
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (stS[mid] <= at) lo = mid; else hi = mid - 1;
                }
                float seg = stS[lo + 1] - stS[lo];
                float t = seg > 1e-6f ? (at - stS[lo]) / seg : 0f;
                return Mathf.LerpUnclamped(stCrest[lo], stCrest[lo + 1], t);
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

        /// <summary>A grade separation: <c>over</c> crosses above
        /// <c>under</c> at <c>at</c>. <c>forced</c> means OSM said so (a
        /// layer, bridge or tunnel tag); the rest were decided by class.</summary>
        public struct Crossing { public int over, under; public Vector2 at; public bool forced; }
        public struct WaterSpan { public int edge; public float s0, s1; }

        /// <summary>A race route through the graph: an ordered chain of edges,
        /// each driven forward (+1) or backward (-1) along its own points.</summary>
        public class Route
        {
            public string id, name;
            public bool loop, oneway;
            public float roadWidth;
            public int speedKmh;
            public float lengthM, startM, finishM;
            public int[] edges;
            public sbyte[] dirs;
        }

        /// <summary>One real building from OSM, as the tile builder wants it:
        /// a counter-clockwise footprint, a height, a facade style and the
        /// oriented box round it (for the roof ridge and the collider).</summary>
        public class Footprint
        {
            public Vector2[] pts;      // counter-clockwise in map view
            public float h;
            public byte style;         // 0 glass, 1 mid, 2 brick, 3 house, 4 shops
            public bool gable;
            public Vector2 centre;     // OBB centre
            public Vector2 u;          // OBB long axis, unit
            public float hu, hv;       // OBB half extents along u and across it
            /// <summary>A CityProps tower stands on this lot instead of an
            /// extruded prism (CityBuildings decides). 0 = none.</summary>
            public byte propKind;
        }

        public string attribution;
        public Vector2 uptown;
        public Vector2[] nodes;
        public float[] nodeY;
        /// <summary>The crest allowance at each junction: a node is a
        /// station every arm shares (CityElevation.MeasureCrests).</summary>
        public float[] nodeCrest;
        public int[] nodeControl;         // 0 none, 1 yield, 2 stop, 4 signal
        public Edge[] edges;
        public List<int>[] nodeEdges;     // edges touching each node
        public Water[] waters;
        public Crossing[] crossings;
        public WaterSpan[] wspans;
        public Route[] routes;
        public Footprint[] footprints = new Footprint[0];
        /// <summary>Where footprint data exists. Inside it the procedural
        /// frontage pass stands down: the real buildings are the buildings.</summary>
        public Rect footprintBounds;

        // ---- spatial hash over edge SEGMENTS ------------------------------
        public const float Cell = 64f;
        readonly Dictionary<long, List<int>> segCells = new Dictionary<long, List<int>>();
        // Entries pack (edge << 12 | segment) — supports 4096 segments per edge.
        static long CellKey(int cx, int cz) => ((long)cx << 24) ^ (cz & 0xFFFFFF);
        public static int PackSeg(int edge, int seg) => (edge << 12) | seg;

        readonly Dictionary<long, List<int>> waterCells = new Dictionary<long, List<int>>();
        readonly Dictionary<long, List<int>> footCells = new Dictionary<long, List<int>>();
        public const float FootCell = 256f;   // one bucket per tile
        static long FootKey(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);

        static CityMap loaded;
        /// <summary>The parsed map, loaded once per process. Synchronous
        /// Resources.Load of a TextAsset's bytes — the one loading path this
        /// project trusts on WebGL.</summary>
        public static CityMap Get()
        {
            if (loaded != null) return loaded;
            var ta = Resources.Load<TextAsset>("charlotte_city");
            if (ta == null) { Debug.LogError("charlotte_city.bytes missing from Resources — run tools/city/export_osm.mjs"); return null; }
            var bld = Resources.Load<TextAsset>("charlotte_bld");
            loaded = Parse(ta.bytes, bld != null ? bld.bytes : null);
            Resources.UnloadAsset(ta);
            if (bld != null) Resources.UnloadAsset(bld);
            return loaded;
        }

        public static CityMap Parse(byte[] city, byte[] bld)
        {
            var map = new CityMap();
            using (var r = new BinaryReader(new MemoryStream(city)))
            {
                if (r.ReadUInt32() != MagicCity) throw new Exception("charlotte_city.bytes: bad magic");
                int version = r.ReadInt32();
                if (version != 1) throw new Exception("charlotte_city.bytes: version " + version + " (reader expects 1)");
                map.attribution = r.ReadString();
                map.uptown = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale;

                int nn = r.ReadInt32();
                map.nodes = new Vector2[nn];
                map.nodeControl = new int[nn];
                for (int i = 0; i < nn; i++)
                {
                    map.nodes[i] = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale;
                    map.nodeControl[i] = r.ReadByte();
                }

                int ns = r.ReadInt32();
                var names = new string[ns];
                for (int i = 0; i < ns; i++) names[i] = r.ReadString();

                int ne = r.ReadInt32();
                map.edges = new Edge[ne];
                map.nodeEdges = new List<int>[nn];
                for (int i = 0; i < nn; i++) map.nodeEdges[i] = new List<int>(3);
                for (int i = 0; i < ne; i++)
                {
                    var e = new Edge { index = i };
                    e.a = r.ReadInt32(); e.b = r.ReadInt32();
                    e.name = names[r.ReadInt32()];
                    e.cls = r.ReadByte();
                    int flags = r.ReadByte();
                    e.link = (flags & 1) != 0;
                    e.oneway = (flags & 2) != 0;
                    e.bridge = (flags & 4) != 0;
                    e.tunnel = (flags & 8) != 0;
                    e.turnLane = (flags & 16) != 0;
                    e.roundabout = (flags & 32) != 0;
                    e.lanes = Mathf.Max(1, r.ReadByte());
                    e.layer = r.ReadSByte();
                    r.ReadSingle();                       // the exporter's width: superseded by the profile
                    e.shl = r.ReadSingle(); e.shr = r.ReadSingle();
                    e.speedKmh = r.ReadByte();
                    e.wayId = r.ReadUInt32();
                    e.profile = RoadProfiles.IndexFor(e.cls, e.link, e.oneway, e.lanes, e.turnLane);
                    var prof = RoadProfiles.All[e.profile];
                    e.width = prof.Width;
                    e.shl = prof.shl; e.shr = prof.shr;
                    int np = r.ReadUInt16();
                    e.pts = new Vector2[np];
                    e.s = new float[np];
                    for (int p = 0; p < np; p++)
                        e.pts[p] = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale;
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

                int nw = r.ReadInt32();
                map.waters = new Water[nw];
                for (int i = 0; i < nw; i++)
                {
                    var w = new Water();
                    w.name = names[r.ReadInt32()];
                    w.width = Mathf.Max(4f, r.ReadSingle());
                    w.lake = r.ReadByte() != 0;
                    int np = r.ReadInt32();
                    w.pts = new Vector2[np];
                    var mn = new Vector2(float.MaxValue, float.MaxValue);
                    var mx = new Vector2(float.MinValue, float.MinValue);
                    for (int p = 0; p < np; p++)
                    {
                        w.pts[p] = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale;
                        mn = Vector2.Min(mn, w.pts[p]); mx = Vector2.Max(mx, w.pts[p]);
                    }
                    w.bbMin = mn; w.bbMax = mx;
                    map.waters[i] = w;
                }

                int nc = r.ReadInt32();
                map.crossings = new Crossing[nc];
                for (int i = 0; i < nc; i++)
                    map.crossings[i] = new Crossing
                    {
                        over = r.ReadInt32(), under = r.ReadInt32(),
                        at = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale,
                        forced = r.ReadByte() != 0,
                    };

                int nws = r.ReadInt32();
                map.wspans = new WaterSpan[nws];
                for (int i = 0; i < nws; i++)
                    map.wspans[i] = new WaterSpan
                    {
                        edge = r.ReadInt32(),
                        s0 = r.ReadSingle() * LayoutScale,
                        s1 = r.ReadSingle() * LayoutScale,
                    };

                int nr = r.ReadInt32();
                map.routes = new Route[nr];
                for (int i = 0; i < nr; i++)
                {
                    var rt = new Route();
                    rt.id = r.ReadString(); rt.name = r.ReadString();
                    rt.loop = r.ReadByte() != 0; rt.oneway = r.ReadByte() != 0;
                    rt.roadWidth = r.ReadSingle(); rt.speedKmh = r.ReadByte();
                    rt.lengthM = r.ReadSingle() * LayoutScale;
                    rt.startM = r.ReadSingle() * LayoutScale;
                    rt.finishM = r.ReadSingle() * LayoutScale;
                    int n = r.ReadInt32();
                    rt.edges = new int[n]; rt.dirs = new sbyte[n];
                    for (int k = 0; k < n; k++) { rt.edges[k] = r.ReadInt32(); rt.dirs[k] = r.ReadSByte(); }
                    map.routes[i] = rt;
                }
            }

            if (bld != null) map.ParseFootprints(bld);
            map.BuildHashes();
            CityElevation.Solve(map);
            return map;
        }

        void ParseFootprints(byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (r.ReadUInt32() != MagicBld) { Debug.LogError("charlotte_bld.bytes: bad magic"); return; }
                int version = r.ReadInt32();
                if (version != 1) { Debug.LogError("charlotte_bld.bytes: version " + version); return; }
                float x0 = r.ReadSingle(), z0 = r.ReadSingle(), x1 = r.ReadSingle(), z1 = r.ReadSingle();
                footprintBounds = Rect.MinMaxRect(x0 * LayoutScale, z0 * LayoutScale, x1 * LayoutScale, z1 * LayoutScale);
                int n = r.ReadInt32();
                footprints = new Footprint[n];
                for (int i = 0; i < n; i++)
                {
                    var f = new Footprint();
                    int sb = r.ReadByte();
                    f.style = (byte)(sb & 0x7F);
                    f.gable = (sb & 0x80) != 0;
                    f.h = r.ReadSingle();
                    int np = r.ReadByte();
                    f.pts = new Vector2[np];
                    for (int p = 0; p < np; p++)
                        f.pts[p] = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale;
                    OrientedBox(f);
                    footprints[i] = f;
                }
            }
        }

        /// <summary>Smallest-area box aligned with one of the footprint's own
        /// edges — a gable ridge runs along its long axis and the collider
        /// is this box. Edge-aligned is exact for rectangles, which nearly
        /// every house is, and close enough for the rest.</summary>
        static void OrientedBox(Footprint f)
        {
            float bestArea = float.MaxValue;
            int n = f.pts.Length;
            int tries = Mathf.Min(n, 12);
            for (int i = 0; i < tries; i++)
            {
                Vector2 d = f.pts[(i + 1) % n] - f.pts[i];
                float m = d.magnitude;
                if (m < 0.2f) continue;
                d /= m;
                Vector2 v = new Vector2(-d.y, d.x);
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                foreach (var p in f.pts)
                {
                    float pu = Vector2.Dot(p, d), pv = Vector2.Dot(p, v);
                    if (pu < u0) u0 = pu; if (pu > u1) u1 = pu;
                    if (pv < v0) v0 = pv; if (pv > v1) v1 = pv;
                }
                float area = (u1 - u0) * (v1 - v0);
                if (area < bestArea)
                {
                    bestArea = area;
                    float hu = (u1 - u0) * 0.5f, hv = (v1 - v0) * 0.5f;
                    Vector2 c = d * ((u0 + u1) * 0.5f) + v * ((v0 + v1) * 0.5f);
                    // the long axis is u, always
                    if (hu >= hv) { f.u = d; f.hu = hu; f.hv = hv; }
                    else { f.u = v; f.hu = hv; f.hv = hu; }
                    f.centre = c;
                }
            }
            if (bestArea == float.MaxValue)
            {
                f.centre = f.pts[0]; f.u = Vector2.right; f.hu = f.hv = 1f;
            }
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
            for (int i = 0; i < footprints.Length; i++)
            {
                var f = footprints[i];
                long k = FootKey(Mathf.FloorToInt(f.centre.x / FootCell), Mathf.FloorToInt(f.centre.y / FootCell));
                if (!footCells.TryGetValue(k, out var list)) footCells[k] = list = new List<int>(32);
                list.Add(i);
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

        /// <summary>The footprints whose centre lies in a 256 m tile, or
        /// null. Ownership by centre, so no building is built twice.</summary>
        public List<int> FootprintsInTile(int tx, int tz) =>
            footCells.TryGetValue(FootKey(tx, tz), out var list) ? list : null;

        /// <summary>Is any real building within <paramref name="r"/> metres
        /// of a point? Asked by the procedural passes before they put a house
        /// or a drive-thru on top of one.</summary>
        public bool AnyFootprintNear(Vector2 p, float r, bool nonHouseOnly = false)
        {
            if (footprints.Length == 0 || !footprintBounds.Contains(p)) return false;
            int x0 = Mathf.FloorToInt((p.x - r) / FootCell), x1 = Mathf.FloorToInt((p.x + r) / FootCell);
            int z0 = Mathf.FloorToInt((p.y - r) / FootCell), z1 = Mathf.FloorToInt((p.y + r) / FootCell);
            float r2 = r * r;
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!footCells.TryGetValue(FootKey(cx, cz), out var list)) continue;
                    foreach (var fi in list)
                    {
                        var f = footprints[fi];
                        if (nonHouseOnly && (f.style == 3 || f.gable)) continue;
                        float reach = r + Mathf.Max(f.hu, f.hv);
                        if ((f.centre - p).sqrMagnitude < reach * reach)
                        {
                            foreach (var q in f.pts) if ((q - p).sqrMagnitude < r2) return true;
                            if (PointInPoly(f.pts, p)) return true;
                        }
                    }
                }
            return false;
        }

        public Route RouteById(string id)
        {
            if (routes == null || string.IsNullOrEmpty(id)) return null;
            foreach (var r in routes) if (r.id == id) return r;
            return null;
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
