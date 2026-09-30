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
    /// nodes, the creeks, ravines and lakes (county and USGS lines with their
    /// beds from 3DEP since WP-04b), every grade separation with OSM's
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
    /// Version 2 (Charlotte refinement WP-02) is a SECTION TABLE: {tag,
    /// offset, length} per section, then META NODE NAME EDGE PNTS WATR WBED
    /// XING SPAN ROUT GHSH (WBED, the water's beds, is optional: WP-04b).
    /// The reader takes the sections it knows by tag and
    /// skips any other, so a later export can add one (lanes, controls,
    /// station heights) without breaking this build. GHSH is the GRAPH HASH
    /// (<see cref="GraphHashOf"/>): derived data keyed by (edge, s) carries
    /// it, and data made for another graph is refused. The layout is written
    /// down once, in tools/city/lib/citydata.mjs.
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
        /// <summary>The PSXC version this reader expects.</summary>
        public const int CityVersion = 2;
        /// <summary>A section tag as the u32 the table stores (its four ASCII
        /// bytes, little-endian).</summary>
        static uint Tag(string t) => (uint)(t[0] | t[1] << 8 | t[2] << 16 | t[3] << 24);

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

            /// <summary>CHAIN CONTINUITY (WP-11): the road's texture V at arc
            /// position s is (vOff + vDir * s) / RoadVTile, running on along the
            /// chain of mitred through joints, so the dash phase carries across
            /// way splits, decks and tile seams; and the surface age is chosen
            /// once per chain from ageSeed (the chain head's first point). Set by
            /// CityMeshes.ComputeTrims; until then V restarts per edge.</summary>
            public float vOff, vDir = 1f;
            public Vector2 ageSeed;
            public bool hasAgeSeed;

            // Elevation stations, every ~StationStep metres along the edge
            // (solved once at load by CityElevation).
            public float[] stS;      // arc position of each station
            public float[] stY;      // road surface height
            public bool[] stElev;    // true where the road is ON STRUCTURE
                                     // (bridge/overpass): deck mesh, no ground pin
            /// <summary>How much the grade INCREASES at each station (a sag;
            /// never negative), end stations carrying their node's worst arm
            /// pair — measured by CityElevation.MeasureSags. The ground
            /// lattice is straight between its corners and the road bends
            /// here, so the land near a sag is sunk by <see cref="SagAt"/>.</summary>
            public float[] stSag;
            /// <summary>Stations SEATED on a host road: a branch running inside
            /// its host's pavement takes the host's height there, and no raise
            /// may lift it off. Null on an edge with none. See
            /// CityElevation.SeatBranches.</summary>
            public bool[] stSeat;
            public bool SeatedAt(int station) => stSeat != null && stSeat[station];

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

            /// <summary>
            /// The sag allowance at an arc position: how far a lattice corner
            /// pinned here must sit below the tarmac (beyond the plain sink)
            /// so that no straight lattice chord through a sag within
            /// <see cref="CityElevation.SagChordM"/> rises above the road.
            ///
            /// For one break of dg at distance d, a chord of length C with an
            /// end here overshoots the bent road by at most dg * d (C - d) / C,
            /// and interpolating that allowance between the corners of any
            /// chord or lattice triangle no longer than C covers the overshoot
            /// at every point between them (breaks superpose). Zero on a
            /// straight grade and over a crest.
            /// </summary>
            public float SagAt(float at)
            {
                if (stSag == null || stSag.Length == 0) return 0f;
                at = Mathf.Clamp(at, 0f, length);
                int lo = 0, hi = stS.Length - 2;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (stS[mid] <= at) lo = mid; else hi = mid - 1;
                }
                float c = CityElevation.SagChordM, sum = 0f;
                for (int i = lo; i >= 0; i--)
                {
                    float d = at - stS[i];
                    if (d >= c) break;
                    if (stSag[i] > 0f) sum += stSag[i] * d * (c - d) / c;
                }
                for (int i = lo + 1; i < stS.Length; i++)
                {
                    float d = stS[i] - at;
                    if (d >= c) break;
                    if (stSag[i] > 0f) sum += stSag[i] * d * (c - d) / c;
                }
                return sum;
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
            /// <summary>WATR's kind: 0 a creek (a water sheet, and every road
            /// over it is a water span), 1 a lake (a closed ring), 2 a RAVINE
            /// (WP-04b: a small stream the ground is carved for, with no water
            /// and no span; a road over one keeps its embankment). Ravines
            /// live in their own spatial hash (<see cref="RavineSegsInRect"/>),
            /// so everything that avoids water still sees only creeks and
            /// lakes.</summary>
            public int kind;
            public bool lake, ravine;
            public Vector2[] pts;
            /// <summary>Arc length at each point (for the bed).</summary>
            public float[] s;
            public float length;
            public float surfaceY; // solved by CityElevation (flat per lake)
            /// <summary>Section WBED (WP-04b; null in older data): the bed,
            /// metres ASL, every <see cref="bedStep"/> m along
            /// <see cref="pts"/> from its first point (the last sample at its
            /// end); a lake's single value is its level. CityElevation turns
            /// it into <see cref="bedY"/>, metres above the datum.</summary>
            public float[] bedASL;
            public float bedStep;
            public float[] bedY;
            public Vector2 bbMin, bbMax;
            /// <summary>A lake's edges by band of <see cref="LakeRowM"/> of z,
            /// for <see cref="LakeContains"/>.</summary>
            public int[][] rowEdges;
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
        /// <summary>The graph hash the file carries (section GHSH), checked
        /// against <see cref="GraphHashOf"/> at parse; see
        /// <see cref="GraphHashMatches"/>.</summary>
        public uint graphHash;
        /// <summary>False when the recomputed hash disagreed with GHSH (logged
        /// as an error at parse; CityAudit fails on it).</summary>
        public bool GraphHashMatches { get; private set; }
        public Vector2 uptown;
        public Vector2[] nodes;
        public float[] nodeY;
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
        /// <summary>The roads' (edge, segment) pairs by 64 m cell. A
        /// <see cref="CellIndex"/> since WP-04: as a List per cell (about
        /// 100,000 of them) it held several MB of the heap, which paid for the
        /// water's own index.</summary>
        readonly CellIndex segCells = new CellIndex(Cell);
        // Entries pack (edge << 12 | segment) — supports 4096 segments per edge.
        static long CellKey(int cx, int cz) => ((long)cx << 24) ^ (cz & 0xFFFFFF);
        public static int PackSeg(int edge, int seg) => (edge << 12) | seg;

        /// <summary>The water's (water, segment) pairs by cell: creeks and
        /// lake shores on <see cref="Cell"/>, ravines on
        /// <see cref="RavineCell"/> (WP-04b).</summary>
        readonly CellIndex waterCells = new CellIndex(Cell);
        readonly CellIndex ravineCells = new CellIndex(RavineCell);

        /// <summary>
        /// A frozen cell -> entries index, built once from (cell, entry) pairs:
        /// one exact-size dictionary from a cell to its slot, and each slot's
        /// entries as a range of one flat array. STABLE: a cell's entries come
        /// back in the order they were added, as they did from the List per
        /// cell this replaced (the ground sums what it finds in that order).
        /// </summary>
        sealed class CellIndex
        {
            readonly float cell;
            List<long> keys = new List<long>(1024);
            List<int> vals = new List<int>(1024);
            Dictionary<long, int> slots = new Dictionary<long, int>();
            int[] starts = { 0 };
            int[] entries = System.Array.Empty<int>();
            public CellIndex(float cell) { this.cell = cell; }
            public int CellCount => starts.Length - 1;
            public int EntryCount => entries.Length;
            public void Add(long key, int val) { keys.Add(key); vals.Add(val); }
            public void Freeze()
            {
                int n = keys.Count;
                var slotOf = new int[n];
                var counts = new List<int>(n / 2 + 1);
                slots = new Dictionary<long, int>();
                for (int i = 0; i < n; i++)
                {
                    if (!slots.TryGetValue(keys[i], out int s)) { s = counts.Count; slots[keys[i]] = s; counts.Add(0); }
                    slotOf[i] = s;
                    counts[s]++;
                }
                starts = new int[counts.Count + 1];
                for (int s = 0; s < counts.Count; s++) starts[s + 1] = starts[s] + counts[s];
                var cursor = (int[])starts.Clone();
                entries = new int[n];
                for (int i = 0; i < n; i++) entries[cursor[slotOf[i]]++] = vals[i];
                slots.TrimExcess();
                keys = null; vals = null;
            }
            public void Query(Vector2 min, Vector2 max, HashSet<int> outSegs)
            {
                int x0 = Mathf.FloorToInt(min.x / cell), x1 = Mathf.FloorToInt(max.x / cell);
                int z0 = Mathf.FloorToInt(min.y / cell), z1 = Mathf.FloorToInt(max.y / cell);
                for (int cx = x0; cx <= x1; cx++)
                    for (int cz = z0; cz <= z1; cz++)
                        if (slots.TryGetValue(CellKey(cx, cz), out int s))
                            for (int i = starts[s], end = starts[s + 1]; i < end; i++) outSegs.Add(entries[i]);
            }
        }
        /// <summary>Ravines hash on coarser cells: 1,545 small streams on
        /// 64 m cells were 29,000 lists; only the ground carve and
        /// <see cref="NearRavine"/> ask.</summary>
        public const float RavineCell = 128f;
        /// <summary>The lakes' indices in <see cref="waters"/>.</summary>
        public int[] lakes = new int[0];
        /// <summary>Band height of a lake's edge index.</summary>
        public const float LakeRowM = 32f;
        readonly Dictionary<long, List<int>> footCells = new Dictionary<long, List<int>>();
        public const float FootCell = 256f;   // one bucket per tile
        static long FootKey(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);
        /// <summary>How far each footprint's farthest corner stands from the
        /// centre it is bucketed by (parallel to <see cref="footprints"/>),
        /// the most of that over each bucket, and over the whole map (186.9 m
        /// on charlotte_bld: a building that big, centred in the NEXT bucket,
        /// still covers this one). Measured off the corners themselves, not
        /// the oriented box, because a degenerate footprint's box falls back
        /// to a 1 m square at its first corner. Set once in BuildHashes, for
        /// <see cref="FootprintClear"/>.</summary>
        float[] footReach = new float[0];
        readonly Dictionary<long, float> footCellReach = new Dictionary<long, float>();
        float footMaxReach;

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

        /// <summary>What the last <see cref="Parse"/> cost, milliseconds: the
        /// bytes into the graph, footprints and hashes; and the elevation
        /// solve after it. For the FPS overlay's budget and CityBudgetProbe.</summary>
        public static float LastParseMs { get; private set; }
        public static float LastSolveMs { get; private set; }

        public static CityMap Parse(byte[] city, byte[] bld)
        {
            var parseClock = System.Diagnostics.Stopwatch.StartNew();
            var map = new CityMap();
            using (var r = new BinaryReader(new MemoryStream(city)))
            {
                if (r.ReadUInt32() != MagicCity) throw new Exception("charlotte_city.bytes: bad magic");
                int version = r.ReadInt32();
                if (version != CityVersion) throw new Exception("charlotte_city.bytes: version " + version + " (reader expects " + CityVersion + ": re-export with tools/city/export_osm.mjs)");
                // The section table. Unknown tags are skipped; a missing one
                // is an error, as is a section read short or long.
                int nsec = r.ReadInt32();
                var table = new Dictionary<uint, (int offset, int length)>(nsec);
                for (int i = 0; i < nsec; i++)
                {
                    uint tag = r.ReadUInt32();
                    int off = r.ReadInt32(), len = r.ReadInt32();
                    if (off < 0 || len < 0 || (long)off + len > city.Length) throw new Exception("charlotte_city.bytes: a section runs past the end");
                    table[tag] = (off, len);
                }
                bool Has(string t) => table.ContainsKey(Tag(t));
                void Open(string t)
                {
                    if (!table.TryGetValue(Tag(t), out var sec)) throw new Exception("charlotte_city.bytes: no " + t + " section");
                    r.BaseStream.Position = sec.offset;
                }
                void Close(string t)
                {
                    var sec = table[Tag(t)];
                    if (r.BaseStream.Position != sec.offset + sec.length)
                        throw new Exception("charlotte_city.bytes: section " + t + " read " + (r.BaseStream.Position - sec.offset) + " of " + sec.length + " bytes");
                }

                Open("META");
                map.attribution = r.ReadString();
                map.uptown = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale;
                Close("META");

                Open("NODE");
                int nn = r.ReadInt32();
                map.nodes = new Vector2[nn];
                map.nodeControl = new int[nn];
                for (int i = 0; i < nn; i++)
                {
                    map.nodes[i] = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale;
                    map.nodeControl[i] = r.ReadByte();
                }
                Close("NODE");

                Open("NAME");
                int ns = r.ReadInt32();
                var names = new string[ns];
                for (int i = 0; i < ns; i++) names[i] = r.ReadString();
                Close("NAME");

                // EDGE holds each edge's record and its point COUNT; the
                // points themselves are in PNTS, every edge's in edge order.
                Open("EDGE");
                int ne = r.ReadInt32();
                var pointCount = new int[ne];
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
                    pointCount[i] = r.ReadUInt16();
                    map.edges[i] = e;
                    if (e.a >= 0 && e.a < nn) map.nodeEdges[e.a].Add(i);
                    if (e.b >= 0 && e.b < nn) map.nodeEdges[e.b].Add(i);
                }
                Close("EDGE");

                Open("PNTS");
                int totalPts = r.ReadInt32();
                long sumPts = 0;
                for (int i = 0; i < ne; i++) sumPts += pointCount[i];
                if (sumPts != totalPts) throw new Exception("charlotte_city.bytes: PNTS holds " + totalPts + " points, the edges " + sumPts);
                for (int i = 0; i < ne; i++)
                {
                    var e = map.edges[i];
                    int np = pointCount[i];
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
                }
                Close("PNTS");

                Open("WATR");
                int nw = r.ReadInt32();
                map.waters = new Water[nw];
                for (int i = 0; i < nw; i++)
                {
                    var w = new Water();
                    w.name = names[r.ReadInt32()];
                    float wid = r.ReadSingle();
                    w.kind = r.ReadByte();
                    w.lake = w.kind == 1; w.ravine = w.kind == 2;
                    // a ravine is a channel a metre or two wide; a creek at
                    // least four, as before
                    w.width = w.ravine ? Mathf.Max(1f, wid) : Mathf.Max(4f, wid);
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
                    w.s = new float[np];
                    for (int p = 1; p < np; p++) w.s[p] = w.s[p - 1] + Vector2.Distance(w.pts[p - 1], w.pts[p]);
                    w.length = np > 0 ? w.s[np - 1] : 0f;
                    map.waters[i] = w;
                }
                Close("WATR");

                // WBED (WP-04b): the beds, if this export has them. Layout in
                // tools/city/lib/citydata.mjs: u32 n | f32 base | f32 scale |
                // per water { u32 m; f32 step; u16 first; (m-1) x i16 step }.
                if (Has("WBED"))
                {
                    Open("WBED");
                    int nb = r.ReadInt32();
                    if (nb != nw) throw new Exception("charlotte_city.bytes: WBED has " + nb + " beds for " + nw + " waters");
                    float bedBase = r.ReadSingle(), bedScale = r.ReadSingle();
                    for (int i = 0; i < nw; i++)
                    {
                        int m = r.ReadInt32();
                        float step = r.ReadSingle() * LayoutScale;
                        var bed = new float[m];
                        int u = 0;
                        for (int k = 0; k < m; k++)
                        {
                            u = k == 0 ? r.ReadUInt16() : u + r.ReadInt16();
                            bed[k] = bedBase + u * bedScale;
                        }
                        map.waters[i].bedASL = m > 0 ? bed : null;
                        map.waters[i].bedStep = step;
                    }
                    Close("WBED");
                }

                Open("XING");
                int nc = r.ReadInt32();
                map.crossings = new Crossing[nc];
                for (int i = 0; i < nc; i++)
                    map.crossings[i] = new Crossing
                    {
                        over = r.ReadInt32(), under = r.ReadInt32(),
                        at = new Vector2(r.ReadSingle(), r.ReadSingle()) * LayoutScale,
                        forced = r.ReadByte() != 0,
                    };
                Close("XING");

                Open("SPAN");
                int nws = r.ReadInt32();
                map.wspans = new WaterSpan[nws];
                for (int i = 0; i < nws; i++)
                    map.wspans[i] = new WaterSpan
                    {
                        edge = r.ReadInt32(),
                        s0 = r.ReadSingle() * LayoutScale,
                        s1 = r.ReadSingle() * LayoutScale,
                    };
                Close("SPAN");

                Open("ROUT");
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
                Close("ROUT");

                Open("GHSH");
                map.graphHash = r.ReadUInt32();
                Close("GHSH");
            }
            uint computed = GraphHashOf(map.edges);
            map.GraphHashMatches = computed == map.graphHash;
            if (!map.GraphHashMatches)
                Debug.LogError($"[City] charlotte_city.bytes: GHSH says {map.graphHash:x8}, the graph hashes to {computed:x8} - the file is damaged or was written by a different exporter");

            if (bld != null) map.ParseFootprints(bld);
            map.BuildHashes();
            LastParseMs = (float)parseClock.Elapsed.TotalMilliseconds;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CityElevation.Solve(map);
            LastSolveMs = (float)clock.Elapsed.TotalMilliseconds;
            Debug.Log($"[City] parsed in {LastParseMs:0} ms, elevation solved in {clock.ElapsedMilliseconds} ms ({CityElevation.SeatedStationCount} ramp stations seated on their mainlines, found in {CityElevation.SeatPrepMs} ms)");
            return map;
        }

        // ---- the graph hash --------------------------------------------
        static uint[] crcTable;
        static uint Crc(uint c, uint v)
        {
            for (int k = 0; k < 4; k++, v >>= 8) c = crcTable[(c ^ v) & 0xFF] ^ (c >> 8);
            return c;
        }

        /// <summary>
        /// THE GRAPH HASH: CRC-32 (zlib's, the IEEE polynomial) of the
        /// little-endian stream  u32 edge count | per edge u32 a, u32 b,
        /// u32 length in cm  - the length summed in DOUBLE precision from the
        /// edge's stored float points, sqrt(dx*dx + dz*dz) per segment, and
        /// rounded half up. The exporter (tools/city/lib/citydata.mjs
        /// graphHash) writes the same number into section GHSH; derived data
        /// keyed by (edge, s) is stamped with it, so a graph whose edges,
        /// ends or lengths moved refuses data made for the old one.
        /// </summary>
        public static uint GraphHashOf(Edge[] edges)
        {
            if (crcTable == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                crcTable = t;
            }
            uint h = 0xFFFFFFFFu;
            h = Crc(h, (uint)edges.Length);
            foreach (var e in edges)
            {
                double len = 0.0;
                var P = e.pts;
                for (int k = 1; k < P.Length; k++)
                {
                    // the file's floats (LayoutScale multiplies geometry at parse)
                    double dx = (double)(P[k].x / LayoutScale) - (double)(P[k - 1].x / LayoutScale);
                    double dz = (double)(P[k].y / LayoutScale) - (double)(P[k - 1].y / LayoutScale);
                    double sq = dx * dx;
                    sq += dz * dz;
                    len += Math.Sqrt(sq);
                }
                h = Crc(h, (uint)e.a);
                h = Crc(h, (uint)e.b);
                h = Crc(h, (uint)Math.Floor(len * 100.0 + 0.5));
            }
            return ~h;
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
                    int packed = PackSeg(e.index, i);
                    ForCellsOnSeg(e.pts[i], e.pts[i + 1], Cell, (cx, cz) => segCells.Add(CellKey(cx, cz), packed));
                }
            }
            segCells.Freeze();
            var lakeList = new List<int>();
            for (int w = 0; w < waters.Length; w++)
            {
                var wt = waters[w];
                var cells = wt.ravine ? ravineCells : waterCells;
                float cell = wt.ravine ? RavineCell : Cell;
                for (int i = 0; i + 1 < wt.pts.Length; i++)
                {
                    int packed = PackSeg(w, i);
                    ForCellsOnSeg(wt.pts[i], wt.pts[i + 1], cell, (cx, cz) => cells.Add(CellKey(cx, cz), packed));
                }
                if (wt.lake)
                {
                    lakeList.Add(w);
                    BuildLakeRows(wt);
                }
                // A lake's INSIDE is no longer registered cell by cell: with
                // Lake Wylie at 2,800 shoreline points that was a point-in-
                // polygon test on each of 39,000 cells at load (0.9 s) and
                // 25,000 lists (WP-04b). LakeContains answers from the
                // lake's own edge bands instead; the shore segments are in
                // the hash as before.
            }
            lakes = lakeList.ToArray();
            waterCells.Freeze();
            ravineCells.Freeze();
            footReach = new float[footprints.Length];
            footMaxReach = 0f;
            for (int i = 0; i < footprints.Length; i++)
            {
                var f = footprints[i];
                long k = FootKey(Mathf.FloorToInt(f.centre.x / FootCell), Mathf.FloorToInt(f.centre.y / FootCell));
                if (!footCells.TryGetValue(k, out var list)) footCells[k] = list = new List<int>(32);
                list.Add(i);
                float far2 = 0f;
                foreach (var q in f.pts) far2 = Mathf.Max(far2, (q - f.centre).sqrMagnitude);
                // A gabled house is DRAWN (and collided) as its oriented box,
                // whose corners can stand further out than any corner of the
                // polygon (an L-shape's notch): FootprintClear tests that box,
                // so the reach has to cover it too.
                if (f.gable) far2 = Mathf.Max(far2, f.hu * f.hu + f.hv * f.hv);
                float reach = Mathf.Sqrt(far2);
                footReach[i] = reach;
                footMaxReach = Mathf.Max(footMaxReach, reach);
                footCellReach[k] = footCellReach.TryGetValue(k, out float cellReach) ? Mathf.Max(cellReach, reach) : reach;
            }
        }

        static void ForCellsOnSeg(Vector2 a, Vector2 b, Action<int, int> visit) => ForCellsOnSeg(a, b, Cell, visit);

        static void ForCellsOnSeg(Vector2 a, Vector2 b, float cell, Action<int, int> visit)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / cell);
            int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / cell), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / cell);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                    visit(cx, cz);
        }

        /// <summary>Visit every (edge, segment) whose segment's cells overlap
        /// the world-space rectangle, deduplicated per edge-segment.</summary>
        public void EdgeSegsInRect(Vector2 min, Vector2 max, HashSet<int> outSegs) => segCells.Query(min, max, outSegs);

        public void WaterSegsInRect(Vector2 min, Vector2 max, HashSet<int> outSegs) => waterCells.Query(min, max, outSegs);

        /// <summary>The (ravine, segment) pairs whose cells overlap the
        /// rectangle (WP-04b). Ravines are kept out of
        /// <see cref="WaterSegsInRect"/>: they carve the ground, but nothing
        /// that avoids water (lamps, lots, houses, the map) should treat a
        /// dry ravine as a creek.</summary>
        public void RavineSegsInRect(Vector2 min, Vector2 max, HashSet<int> outSegs) => ravineCells.Query(min, max, outSegs);

        static void BuildLakeRows(Water w)
        {
            int n = w.pts.Length;
            int rows = Mathf.Max(1, Mathf.FloorToInt((w.bbMax.y - w.bbMin.y) / LakeRowM) + 1);
            var lists = new List<int>[rows];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float y0 = Mathf.Min(w.pts[i].y, w.pts[j].y), y1 = Mathf.Max(w.pts[i].y, w.pts[j].y);
                int r0 = Mathf.Clamp(Mathf.FloorToInt((y0 - w.bbMin.y) / LakeRowM), 0, rows - 1);
                int r1 = Mathf.Clamp(Mathf.FloorToInt((y1 - w.bbMin.y) / LakeRowM), 0, rows - 1);
                for (int r = r0; r <= r1; r++) (lists[r] ??= new List<int>(4)).Add(i);
            }
            w.rowEdges = new int[rows][];
            for (int r = 0; r < rows; r++) w.rowEdges[r] = lists[r] != null ? lists[r].ToArray() : System.Array.Empty<int>();
        }

        /// <summary>Is the point inside the lake's ring? The even-odd rule,
        /// over only the edges of the point's band (<see cref="LakeRowM"/>):
        /// the same answer as <see cref="PointInPoly"/> without walking the
        /// whole shore.</summary>
        public static bool LakeContains(Water w, Vector2 p)
        {
            if (!w.lake || p.x < w.bbMin.x || p.x > w.bbMax.x || p.y < w.bbMin.y || p.y > w.bbMax.y) return false;
            var rows = w.rowEdges;
            if (rows == null) return PointInPoly(w.pts, p);
            int r = Mathf.Clamp(Mathf.FloorToInt((p.y - w.bbMin.y) / LakeRowM), 0, rows.Length - 1);
            bool inside = false;
            var pts = w.pts;
            int n = pts.Length;
            foreach (int i in rows[r])
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Is the point in any lake?</summary>
        public bool InLake(Vector2 p)
        {
            foreach (int li in lakes) if (LakeContains(waters[li], p)) return true;
            return false;
        }

        static readonly HashSet<int> ravineScratch = new HashSet<int>();
        /// <summary>Is a ravine's line within <paramref name="r"/> metres of
        /// the point? For the procedural lots and houses, which should not
        /// stand in a carved channel.</summary>
        public bool NearRavine(Vector2 p, float r)
        {
            ravineScratch.Clear();
            RavineSegsInRect(p - Vector2.one * r, p + Vector2.one * r, ravineScratch);
            foreach (int packed in ravineScratch)
            {
                var w = waters[packed >> 12];
                int si = packed & 0xFFF;
                if (si + 1 >= w.pts.Length) continue;
                Vector2 a = w.pts[si], d = w.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                if (Vector2.Distance(p, a + d * t) < r) return true;
            }
            return false;
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

        /// <summary>
        /// Is a point outside every real building AND at least
        /// <paramref name="r"/> metres from every one of their walls? Asked by
        /// the street lamps (CityMeshes.TryLamp), 2026-09-21, after the review
        /// of the night pass found posts standing inside or flush against
        /// downtown buildings: West 6th, West 4th, East 7th, West 3rd.
        ///
        /// <see cref="AnyFootprintNear"/> is NOT that question, twice over, and
        /// is left alone because its callers (the procedural houses, the paved
        /// verge, the drive-thrus) are signed off on its answers:
        ///   * it opens only the 256 m buckets under p ± r, and a footprint
        ///     lives in the bucket of its CENTRE, so a building centred just
        ///     over a bucket line is never asked even when its wall is a metre
        ///     away (every tile seam downtown);
        ///   * it measures to the CORNERS, so a foot 0-0.3 m off the middle of
        ///     a long facade, nowhere near a corner, passed.
        /// Here the buckets opened are those under p ± (r + the farthest any
        /// footprint reaches from its centre), a bucket is skipped when even
        /// its own farthest-reaching footprint cannot get within r, a
        /// footprint when its centre is further than r + its reach, and what
        /// is left is measured to every EDGE. Per call that is the one or two
        /// buckets AnyFootprintNear opened plus a neighbour only where a big
        /// building can reach across: bounded, no scan over all 32,000 (the
        /// tile builds while the car drives). Measured offline on uptown: 20
        /// footprints looked at per call, 63 with the map-wide reach alone,
        /// and the answer matched a brute-force scan at 3,000 random points.
        /// </summary>
        public bool FootprintClear(Vector2 p, float r)
        {
            if (footprints.Length == 0) return true;
            float wide = r + footMaxReach, r2 = r * r;
            int x0 = Mathf.FloorToInt((p.x - wide) / FootCell), x1 = Mathf.FloorToInt((p.x + wide) / FootCell);
            int z0 = Mathf.FloorToInt((p.y - wide) / FootCell), z1 = Mathf.FloorToInt((p.y + wide) / FootCell);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    long k = FootKey(cx, cz);
                    if (!footCellReach.TryGetValue(k, out float cellReach)) continue;
                    // every centre in the bucket is at least this far from p
                    float dx = Mathf.Max(0f, Mathf.Max(cx * FootCell - p.x, p.x - (cx + 1) * FootCell));
                    float dz = Mathf.Max(0f, Mathf.Max(cz * FootCell - p.y, p.y - (cz + 1) * FootCell));
                    float cellR = r + cellReach;
                    if (dx * dx + dz * dz >= cellR * cellR) continue;
                    foreach (int fi in footCells[k])
                    {
                        var f = footprints[fi];
                        float reach = r + footReach[fi];
                        if ((f.centre - p).sqrMagnitude >= reach * reach) continue;
                        var pts = f.pts;
                        for (int a = pts.Length - 1, b = 0; b < pts.Length; a = b++)
                        {
                            Vector2 d = pts[b] - pts[a];
                            float L2 = d.sqrMagnitude;
                            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - pts[a], d) / L2) : 0f;
                            if ((pts[a] + d * t - p).sqrMagnitude < r2) return false;
                        }
                        if (PointInPoly(pts, p)) return false;
                        // A GABLED house is not drawn as its polygon: BuildFootprints
                        // emits it as the oriented box (EmitGableHouse), and the
                        // Buildings collider is that box. For an L-shaped house the
                        // notch is wall and collider, however clear of the polygon it
                        // is - found by the second review (edge 490 near
                        // (-3177.5, 2106.4): 5 m from the polygon, inside the walls).
                        // The uncut box is tested: FitHouse can only shrink it, so
                        // this is the conservative side.
                        if (f.gable)
                        {
                            Vector2 q = p - f.centre;
                            Vector2 v = new Vector2(-f.u.y, f.u.x);
                            float ou = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, f.u)) - f.hu);
                            float ov = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, v)) - f.hv);
                            if (ou * ou + ov * ov < r2) return false;
                        }
                    }
                }
            return true;
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
