using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Turns one 256 m tile of the CityMap into meshes. Pure geometry — no
    /// GameObjects; CityWorld wraps the results. Everything is deterministic
    /// and TILE-LOCAL: vertices are relative to the tile's origin corner, so
    /// float precision never depends on how far from uptown the tile sits.
    ///
    /// Ownership rule at tile seams: a road span, river span, junction or
    /// building belongs to the tile that contains its MIDPOINT (or node, or
    /// footprint centre), so nothing is ever emitted twice. Adjacent tiles
    /// derive shared boundary vertices from the same stations and the same
    /// GroundY, so edges meet exactly.
    ///
    /// WINDING, ONCE. Three helpers decide which way every face points so no
    /// call site has to: <see cref="Bucket.Up"/> orders a horizontal quad so
    /// it faces UP (its corners read counter-clockwise in map view),
    /// <see cref="Bucket.Down"/> the reverse, and <see cref="Bucket.Wall"/>
    /// orders a vertical quad so it faces the OUTWARD normal it is given. The
    /// inside-out buildings and the roads visible only from underneath were
    /// both per-call-site winding errors; so were the kerbs, the deck
    /// fascias and the soffits (2026-09-12: every one of them faced INTO the
    /// road, so a bridge seen from beside or below was a floating ribbon
    /// with rails). The road ribbon keeps its verified order.
    ///
    /// JUNCTIONS (2026-09-12). A node with three or more arms used to trim
    /// every arm back by the widest road's half width plus two metres and
    /// fill the hole with a FLAT fan at the node's height. Two things were
    /// wrong with that. The fan was flat while the arms are not: a ramp
    /// leaving a junction at 8% has dropped nearly a metre by the time its
    /// ribbon starts, so every junction on a grade had a step at every
    /// mouth — the "ledges" at every bridge approach. And on a freeway a
    /// merge node is not a junction at all: OSM joins the ramp to the
    /// carriageway at the END of the taper, so the ramp's last hundred
    /// metres run INSIDE the mainline ribbon, through the mainline's
    /// barrier. Now: fan corners take each arm's own height at its own trim;
    /// trims are per ARM and only as long as the arms that actually overlap
    /// need; and a node whose arms are a through pair plus shallow BRANCHES
    /// (a merge, a diverge, a freeway fork) draws no fan at all — the
    /// through road runs on unbroken and each branch's ribbon is CLIPPED
    /// against it, starting as a zero-width wedge at the mainline's edge and
    /// widening as it leaves, with the barrier and the rails standing down
    /// along the attachment.
    /// </summary>
    public static class CityMeshes
    {
        public const float TileSize = 256f;
        public const int GroundRes = 32;          // 8 m cells
        /// <summary>Metres of road per texture repeat — and therefore the
        /// length of ONE DASH CYCLE, because the painter draws the broken lane
        /// line as the first quarter of the repeat. 12.192 m is 40 feet, the
        /// US standard: a 10 ft stripe and a 30 ft gap.</summary>
        public const float RoadVTile = 12.192f;
        public const float RailH = 0.95f;
        public const float RailW = 0.3f;
        public const float PierEvery = 26f;
        public const float BuildingSink = 0.55f;
        /// <summary>A Jersey barrier: 81 cm tall, half a metre thick, on the
        /// paved edge of every freeway carriageway that is on the ground
        /// (decks carry rails). It is what keeps a race on the freeway and
        /// what a freeway looks like.</summary>
        public const float BarrierH = 0.81f;
        public const float BarrierW = 0.5f;
        /// <summary>How far the tarmac hangs below its own surface at the
        /// edge. A KERB, not a cliff — the roads mesh carries the collider,
        /// so this face is what the car climbs back onto the road over.</summary>
        public const float KerbDepth = 0.32f;

        /// <summary>Longest edge of one facade panel, in metres. Affine warp is
        /// proportional to how much a triangle spans, so a 40 m wall as one
        /// quad swims; PS1 content subdivided, and so does this.</summary>
        const float FacadePanelMax = 9f;
        const int FacadePanelCap = 4;
        /// <summary>Up a TOWER the cap is doubled: four panels over a 200 m
        /// wall are 50 m each, and the curtain wall visibly bowed in every
        /// preview. Only walls past this height pay for it.</summary>
        const float TallWallM = 40f;
        const int TallPanelCap = 8;

        /// <summary>What a stretch of road is MADE of, which is a separate
        /// question from what is painted on it: the same carriageway is fresh
        /// blacktop on the ground and poured concrete where it crosses a
        /// river. Ported from RG2's (material x age) pair.</summary>
        public enum Surface { AsphaltNew = 0, AsphaltOld, ConcreteNew, ConcreteOld }
        public const int SurfaceCount = 4;

        /// <summary>Driving surfaces are one row per RoadProfiles entry plus
        /// the unpainted junction slab, times four surfaces.</summary>
        public const int JunctionProfile = RoadProfiles.ProfileCount;
        public const int RoadClassCount = RoadProfiles.ProfileCount + 1;

        public enum Slot
        {
            Ground = 0, Concrete, Water, Pavement,
            FacadeTower, FacadeMid, FacadeBrick, Shops,
            FacadeGlass, FacadeHouse, RoofTiles, RoofFlat,
            // Driving surfaces, laid out as RoadFirst + profile * SurfaceCount +
            // surface. Arithmetic rather than eighty named members.
            RoadFirst,
            COUNT = RoadFirst + RoadClassCount * SurfaceCount,
        }

        public static Slot SlotOf(int profile, Surface surf) =>
            (Slot)((int)Slot.RoadFirst + profile * SurfaceCount + (int)surf);

        /// <summary>
        /// Has this stretch been resurfaced recently? RG2's own _roadAge hash,
        /// ported constant for constant. 40% new, deterministic from position
        /// alone — so a road is the same age on every visit, across a tile
        /// unload, with nothing to store.
        /// </summary>
        public static bool IsFresh(Vector2 seed)
        {
            unchecked
            {
                int x = (int)(seed.x * 100f), y = (int)(seed.y * 100f);
                uint h = (uint)((x * unchecked((int)0x9e3779b1)) ^ (y * unchecked((int)0x6a09e667)));
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae35;
                h ^= h >> 16;
                return h % 100u < 40u;
            }
        }

        /// <summary>Concrete where the road is on structure, asphalt where it
        /// is on the ground. A bridge deck IS poured concrete.</summary>
        public static Surface SurfaceOf(CityMap.Edge e, bool elevated)
        {
            bool fresh = IsFresh(e.pts[0]);
            return elevated ? (fresh ? Surface.ConcreteNew : Surface.ConcreteOld)
                            : (fresh ? Surface.AsphaltNew : Surface.AsphaltOld);
        }

        public static Slot RoadSlot(CityMap.Edge e, bool elevated) =>
            SlotOf(e.profile, SurfaceOf(e, elevated));

        /// <summary>A freeway carriageway on the ground carries a barrier
        /// on its median side (and on the outside only in a cut — see the
        /// span loop). Expressway carriageways too; ramps and two-way roads
        /// do not (a ramp's edge is where its gore is).</summary>
        public static bool Barriered(CityMap.Edge e) =>
            !e.link && ((e.cls >= 5) || (e.cls == 4 && e.oneway));

        // facade texture footprints in metres (how much wall one repeat covers)
        static readonly Vector2[] FacadeMeters =
        {
            new Vector2(9.5f, 12.5f),   // FacadeTower
            new Vector2(10.5f, 13.5f),  // FacadeMid
            new Vector2(6.5f, 6.5f),    // FacadeBrick
            new Vector2(24.0f, 4.2f),   // Shops (the atlas carries FOUR 6 m fronts per repeat)
            new Vector2(8.0f, 8.0f),    // FacadeGlass: 4x4 panes of 2 m
            new Vector2(6.0f, 3.1f),    // FacadeHouse: siding, one window per repeat, one storey tall
        };
        const float ShopFloorH = 4.2f;
        const float RoofTileM = 3.5f;
        const float RoofFlatM = 8f;

        public class SolidBox
        {
            public Vector3 center;   // tile-local
            public Vector3 size;
            public float yawDeg;
        }

        public class TileMeshes
        {
            public Vector3 origin;
            public Mesh ground;     public Slot[] groundSlots;
            public Mesh roads;      public Slot[] roadSlots;
            public Mesh barriers;   // concrete, its own collider
            public Mesh water;
            /// <summary>Every facade and roof on the tile. It is its own
            /// collider now: a footprint's oriented box reached into the
            /// street wherever the footprint was not a rectangle, and an
            /// invisible wall across a lane is exactly what that felt like.</summary>
            public Mesh buildings;  public Slot[] buildingSlots;
            /// <summary>Piers. Buildings collide as their own mesh.</summary>
            public List<SolidBox> solids = new List<SolidBox>();
            public List<Vector4> lamps = new List<Vector4>(); // xyz + yaw, future use
            /// <summary>Walls the emitter caught pointing the wrong way. Zero,
            /// or the audit fails the build.</summary>
            public int wallFacingErrors;
            public int footprintCount, houseCount, goreCount, patchCount, branchCount;
        }

        // ---- growable buckets, one per slot, reused across tiles ----------
        class Bucket
        {
            public List<Vector3> v = new List<Vector3>(512);
            public List<Vector2> uv = new List<Vector2>(512);
            public List<int> t = new List<int>(1024);
            public void Clear() { v.Clear(); uv.Clear(); t.Clear(); }
            public int Count => v.Count;

            /// <summary>Raw quad: emits (a,c,b)+(a,d,c). Shows the side from
            /// which a→b→c→d reads anticlockwise. Prefer <see cref="Up"/>,
            /// <see cref="Down"/> and <see cref="Wall"/>, which decide that
            /// for you.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                uv.Add(ua); uv.Add(ub); uv.Add(uc);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
            }

            /// <summary>A horizontal-ish quad that faces UP whatever order
            /// its corners arrive in: the map-view signed area decides.</summary>
            public void Up(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                           Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)
                           + (c.x - a.x) * (d.z - a.z) - (d.x - a.x) * (c.z - a.z);
                if (area >= 0f) Quad(a, b, c, d, ua, ub, uc, ud);
                else Quad(a, d, c, b, ua, ud, uc, ub);
            }

            /// <summary>A horizontal-ish quad that faces DOWN: a soffit.</summary>
            public void Down(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)
                           + (c.x - a.x) * (d.z - a.z) - (d.x - a.x) * (c.z - a.z);
                if (area < 0f) Quad(a, b, c, d, ua, ub, uc, ud);
                else Quad(a, d, c, b, ua, ud, uc, ub);
            }

            /// <summary>A vertical quad between two plan points, facing the
            /// side <paramref name="outward"/> points to. u runs along the
            /// wall from p to q, v up.</summary>
            public void Wall(Vector3 p, Vector3 q, float y0, float y1, Vector2 outward,
                             float u0, float u1, float v0, float v1)
            {
                // a wall from a to c faces the LEFT of a→c in map view
                Vector2 d = new Vector2(q.x - p.x, q.z - p.z);
                Vector2 left = new Vector2(-d.y, d.x);
                if (Vector2.Dot(left, outward) < 0f) { var t = p; p = q; q = t; var tu = u0; u0 = u1; u1 = tu; }
                Quad(new Vector3(p.x, y0, p.z), new Vector3(p.x, y1, p.z),
                     new Vector3(q.x, y1, q.z), new Vector3(q.x, y0, q.z),
                     new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
            }

            /// <summary>A vertical quad whose two ends stand at DIFFERENT
            /// heights (a kerb along a grade, a deck fascia): p's face spans
            /// pY0..pY1, q's spans qY0..qY1. Faces <paramref name="outward"/>.</summary>
            public void WallSloped(Vector3 p, Vector3 q, float pY0, float pY1, float qY0, float qY1,
                                   Vector2 outward, float u0, float u1, float v0, float v1)
            {
                Vector2 d = new Vector2(q.x - p.x, q.z - p.z);
                Vector2 left = new Vector2(-d.y, d.x);
                if (Vector2.Dot(left, outward) < 0f)
                {
                    var t = p; p = q; q = t;
                    var tu = u0; u0 = u1; u1 = tu;
                    var t0 = pY0; pY0 = qY0; qY0 = t0;
                    var t1 = pY1; pY1 = qY1; qY1 = t1;
                }
                Quad(new Vector3(p.x, pY0, p.z), new Vector3(p.x, pY1, p.z),
                     new Vector3(q.x, qY1, q.z), new Vector3(q.x, qY0, q.z),
                     new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
            }

            /// <summary>Normal of the most recent triangle, for the emitter's
            /// own facing check.</summary>
            public Vector3 LastNormal()
            {
                int n = t.Count;
                if (n < 3) return Vector3.zero;
                Vector3 a = v[t[n - 3]], b = v[t[n - 2]], c = v[t[n - 1]];
                return Vector3.Cross(b - a, c - a);
            }
        }

        static readonly Bucket[] buckets = NewBuckets();
        static readonly Bucket barrierBucket = new Bucket();
        static Bucket[] NewBuckets()
        {
            var b = new Bucket[(int)Slot.COUNT];
            for (int i = 0; i < b.Length; i++) b[i] = new Bucket();
            return b;
        }

        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly HashSet<int> edgeScratch = new HashSet<int>();
        static readonly List<Vector2> polyScratch = new List<Vector2>();

        // ==================================================================
        //  Junction geometry, decided once per map.
        // ==================================================================

        /// <summary>
        /// What every edge end does at its node, computed once from the graph
        /// (<see cref="ComputeTrims"/>) and read by every tile build.
        /// </summary>
        public class Trims
        {
            /// <summary>Ribbon trim at each end of each edge: the ribbon runs
            /// from atA to length - atB, the fan patch covers the rest.</summary>
            public float[] atA, atB;
            /// <summary>Half width the ribbon has AT each end (its own half
            /// width unless it tapers to a narrower neighbour there).</summary>
            public float[] hwA, hwB;
            /// <summary>Metres over which the taper from the edge's own half
            /// width to hwA / hwB runs. Zero when there is none.</summary>
            public float[] taperA, taperB;
            /// <summary>The THROUGH edge this end's ribbon is clipped against
            /// (a ramp at its merge, the minor branch of a fork), or -1.</summary>
            public int[] branchA, branchB;
            /// <summary>The node draws a fan patch between its arms.</summary>
            public bool[] patch;
            /// <summary>The node is a plain continuation (two arms, or a
            /// through pair with clipped branches): the through ribbons meet
            /// there with a MITRED cross-section.</summary>
            public bool[] mitre;
            /// <summary>For a mitred node: the two edges that meet through it.</summary>
            public int[] throughA, throughB;

            public float TrimAt(CityMap.Edge e, int node) => e.a == node ? atA[e.index] : atB[e.index];
            public int BranchAt(CityMap.Edge e, int node) => e.a == node ? branchA[e.index] : branchB[e.index];

            /// <summary>The ribbon's half width at an arc position, tapers
            /// included: the LOWER of what each end asks for.</summary>
            public float HalfWidthAt(CityMap.Edge e, float s)
            {
                float hw = e.width * 0.5f;
                float ha = hw, hb = hw;
                int i = e.index;
                if (taperA[i] > 0f && s < taperA[i]) ha = Mathf.Lerp(hwA[i], hw, s / taperA[i]);
                if (taperB[i] > 0f && e.length - s < taperB[i]) hb = Mathf.Lerp(hwB[i], hw, (e.length - s) / taperB[i]);
                return Mathf.Min(ha, hb);
            }
        }

        /// <summary>The direction an edge LEAVES a node in.</summary>
        static Vector2 OutDir(CityMap.Edge e, int node) =>
            e.a == node ? e.TangentAt(0f) : -e.TangentAt(e.length);

        const float ThroughCos = -0.85f;    // arms this opposite are one road going through
        /// <summary>Arms closer than this in direction are CLIPPED against
        /// each other rather than trimmed: a ramp beside its mainline, the
        /// minor arm of a fork, a side street meeting a road at 50 degrees.
        /// Sixty degrees. Below that a fan's corner cones overlap and its
        /// triangles fold over the arms; a clipped mouth is exact.</summary>
        const float BranchCos = 0.5f;
        const float ContinueCos = -0.906f;  // a two-arm node bending less than 25 deg is a bend in the ribbon
        const float TaperPerLane = 30f;
        const float TaperMin = 25f, TaperMax = 80f;

        /// <summary>Kept for callers that only ever wanted a per-node
        /// "is there a patch here" — the full table is <see cref="ComputeTrims"/>.</summary>
        public static Trims NodeTrims(CityMap map) => ComputeTrims(map);

        public static Trims ComputeTrims(CityMap map)
        {
            int ne = map.edges.Length, nn = map.nodes.Length;
            var t = new Trims
            {
                atA = new float[ne], atB = new float[ne],
                hwA = new float[ne], hwB = new float[ne],
                taperA = new float[ne], taperB = new float[ne],
                branchA = new int[ne], branchB = new int[ne],
                patch = new bool[nn], mitre = new bool[nn],
                throughA = new int[nn], throughB = new int[nn],
            };
            for (int i = 0; i < ne; i++)
            {
                t.hwA[i] = t.hwB[i] = map.edges[i].width * 0.5f;
                t.branchA[i] = t.branchB[i] = -1;
            }
            for (int i = 0; i < nn; i++) { t.throughA[i] = t.throughB[i] = -1; }

            var arms = new List<(CityMap.Edge e, Vector2 dir, float hw)>(8);
            var clipped = new List<int>(8);     // arm index -> arm it clips against, or -1
            for (int n = 0; n < nn; n++)
            {
                var list = map.nodeEdges[n];
                if (list.Count < 2) continue;
                arms.Clear();
                foreach (var ei in list)
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    arms.Add((e, OutDir(e, n), e.width * 0.5f));
                }
                if (arms.Count < 2) continue;

                // The through pair: the two arms that most nearly continue
                // each other, mainline first (a ramp is never the through road).
                int tA = -1, tB = -1; float best = 1f;
                for (int pass = 0; pass < 2 && tA < 0; pass++)
                    for (int i = 0; i < arms.Count; i++)
                        for (int j = i + 1; j < arms.Count; j++)
                        {
                            if (pass == 0 && (arms[i].e.link || arms[j].e.link)) continue;
                            float d = Vector2.Dot(arms[i].dir, arms[j].dir);
                            if (d < best) { best = d; tA = i; tB = j; }
                        }
                bool through = tA >= 0 && best < ThroughCos;

                // A width taper on the wider of two through arms whenever
                // the lane count changes at a mitred node — a plain
                // continuation OR a merge, where the mainline gains or loses
                // its auxiliary lane. Without it the edge line stepped a
                // whole lane at the node and the narrower edge's barrier
                // ended in the middle of the wider road's outside lane. The
                // taper is symmetric about the centreline; MUTCD would put
                // it on one side, but a lane that simply narrows away is
                // what a PS1 road can afford.
                void Taper((CityMap.Edge e, Vector2 dir, float hw) a0, (CityMap.Edge e, Vector2 dir, float hw) a1)
                {
                    if (Mathf.Abs(a0.hw - a1.hw) <= 0.05f) return;
                    var wide = a0.hw > a1.hw ? a0 : a1;
                    var narrow = a0.hw > a1.hw ? a1 : a0;
                    float dropped = (wide.hw - narrow.hw) * 2f / RoadProfiles.LaneM;
                    float len = Mathf.Clamp(dropped * TaperPerLane, TaperMin, TaperMax);
                    len = Mathf.Min(len, wide.e.length * 0.9f);
                    if (wide.e.a == n) { t.hwA[wide.e.index] = narrow.hw; t.taperA[wide.e.index] = len; }
                    else { t.hwB[wide.e.index] = narrow.hw; t.taperB[wide.e.index] = len; }
                }

                if (arms.Count == 2)
                {
                    if (best < ContinueCos)
                    {
                        // A continuation: mitred ends.
                        t.mitre[n] = true;
                        t.throughA[n] = arms[0].e.index; t.throughB[n] = arms[1].e.index;
                        Taper(arms[0], arms[1]);
                        continue;
                    }
                    // a real bend: a small fan fills the outside corner
                }

                // Shallow pairs are CLIPPED, not trimmed: the arm that is a
                // link, or narrower, or of the lower class hugs the other.
                clipped.Clear();
                for (int i = 0; i < arms.Count; i++) clipped.Add(-1);
                for (int i = 0; i < arms.Count; i++)
                    for (int j = i + 1; j < arms.Count; j++)
                    {
                        if (Vector2.Dot(arms[i].dir, arms[j].dir) < BranchCos) continue;
                        var ai = arms[i]; var aj = arms[j];
                        bool iClips;
                        if (ai.e.link != aj.e.link) iClips = ai.e.link;
                        else if (ai.e.cls != aj.e.cls) iClips = ai.e.cls < aj.e.cls;
                        else if (Mathf.Abs(ai.hw - aj.hw) > 0.05f) iClips = ai.hw < aj.hw;
                        else iClips = true;
                        if (through && (i == tA || i == tB) && (j == tA || j == tB)) continue;
                        if (through && (j == tA || j == tB)) iClips = true;
                        if (through && (i == tA || i == tB)) iClips = false;
                        int c = iClips ? i : j, h = iClips ? j : i;
                        if (clipped[c] < 0) clipped[c] = h;
                    }

                // A through road with nothing but branches beside it draws
                // no patch: the through ribbons meet mitred, the branches
                // clip. Anything else is a fan.
                bool allBranch = through;
                if (through)
                    for (int i = 0; i < arms.Count && allBranch; i++)
                        if (i != tA && i != tB && clipped[i] < 0) allBranch = false;
                if (allBranch)
                {
                    t.mitre[n] = true;
                    t.throughA[n] = arms[tA].e.index; t.throughB[n] = arms[tB].e.index;
                    Taper(arms[tA], arms[tB]);
                    for (int i = 0; i < arms.Count; i++)
                    {
                        if (clipped[i] < 0) continue;
                        var e = arms[i].e;
                        if (e.a == n) t.branchA[e.index] = arms[clipped[i]].e.index;
                        else t.branchB[e.index] = arms[clipped[i]].e.index;
                    }
                    continue;
                }

                // A fan. Every arm is trimmed back by the same distance: as
                // far as the most-overlapping pair demands. Two ribbons from
                // one node at angle theta overlap until r sin(theta) exceeds
                // hw_j + hw_i cos(theta); trimming to that also keeps the
                // corner cones from interleaving in the angular sort, which
                // is what folded fan triangles over the arms. Arms going
                // straight through need nothing from each other, and a pair
                // shallower than BranchCos is clipped instead.
                t.patch[n] = true;
                float trimN = 0f;
                for (int i = 0; i < arms.Count; i++)
                    for (int j = 0; j < arms.Count; j++)
                    {
                        if (j == i) continue;
                        float d = Vector2.Dot(arms[i].dir, arms[j].dir);
                        if (d < ThroughCos) continue;                       // straight through: no overlap
                        if (clipped[i] == j || clipped[j] == i) continue;   // handled by clipping
                        float sin = Mathf.Max(0.5f, Mathf.Sqrt(Mathf.Max(0f, 1f - d * d)));
                        trimN = Mathf.Max(trimN, (arms[j].hw + arms[i].hw * Mathf.Abs(d) + 0.6f) / sin);
                    }
                for (int i = 0; i < arms.Count; i++)
                {
                    var e = arms[i].e;
                    float trim = Mathf.Min(trimN, e.length * 0.49f);
                    if (e.a == n) t.atA[e.index] = trim; else t.atB[e.index] = trim;
                    if (clipped[i] >= 0)
                    {
                        if (e.a == n) t.branchA[e.index] = arms[clipped[i]].e.index;
                        else t.branchB[e.index] = arms[clipped[i]].e.index;
                    }
                }
            }

            // An edge shorter than its two trims has no ribbon: the two fans
            // meet in the middle, exactly, instead of leaving a sliver.
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                float sum = t.atA[i] + t.atB[i];
                if (sum > e.length - 0.6f && sum > 0f)
                {
                    float k = e.length / sum;
                    t.atA[i] *= k; t.atB[i] *= k;
                }
            }
            return t;
        }

        // ==================================================================
        public static TileMeshes Build(CityMap map, Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            var tm = new TileMeshes { origin = new Vector3(tx * TileSize, 0f, tz * TileSize) };
            var min = new Vector2(tx * TileSize, tz * TileSize);
            var max = min + new Vector2(TileSize, TileSize);

            foreach (var b in buckets) b.Clear();
            barrierBucket.Clear();
            goreGaps.Clear();
            clips.Clear();

            BuildGround(map, tm, min);
            clipPairs.Clear();
            BuildGores(map, trims, tm, min, max);
            BuildRoadsAndDecks(map, trims, tm, min, max);
            BuildJunctions(map, trims, tm, min, max);
            BuildWater(map, tm, min, max);
            BuildBuildings(map, buildings, tm, tx, tz);
            BuildFootprints(map, tm, tx, tz);
            BuildHouses(map, tm, tx, tz);

            tm.ground = MeshFrom("ground", new[] { Slot.Ground, Slot.Pavement }, out var gSlots);
            tm.groundSlots = gSlots;
            tm.roads = MeshFrom("roads", RoadAndStructureSlots, out var roadSlots);
            tm.roadSlots = roadSlots;
            tm.barriers = MeshFromBucket("barriers", barrierBucket);
            tm.water = MeshFrom("water", new[] { Slot.Water }, out _);
            tm.buildings = MeshFrom("bld", BuildingSlots, out var bSlots);
            tm.buildingSlots = bSlots;
            return tm;
        }

        static readonly Slot[] BuildingSlots =
        {
            Slot.FacadeTower, Slot.FacadeMid, Slot.FacadeBrick, Slot.Shops,
            Slot.FacadeGlass, Slot.FacadeHouse, Slot.RoofTiles, Slot.RoofFlat,
        };

        /// <summary>Every driving surface plus the structural concrete, in slot
        /// order. MeshFrom drops the empty ones, so a tile with two profiles on
        /// it still ends up with two submeshes and not eighty.</summary>
        static readonly Slot[] RoadAndStructureSlots = BuildRoadSlotList();
        static Slot[] BuildRoadSlotList()
        {
            var list = new Slot[RoadClassCount * SurfaceCount + 1];
            for (int i = 0; i < list.Length - 1; i++) list[i] = (Slot)((int)Slot.RoadFirst + i);
            list[list.Length - 1] = Slot.Concrete;
            return list;
        }

        static Mesh MeshFrom(string name, Slot[] wanted, out Slot[] usedSlots)
        {
            int totalV = 0;
            var used = new List<Slot>();
            foreach (var s in wanted)
                if (buckets[(int)s].Count > 0) { used.Add(s); totalV += buckets[(int)s].Count; }
            usedSlots = used.ToArray();
            if (totalV == 0) return null;

            var mesh = new Mesh { name = name };
            if (totalV > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new List<Vector3>(totalV);
            var uvs = new List<Vector2>(totalV);
            foreach (var s in used) { verts.AddRange(buckets[(int)s].v); uvs.AddRange(buckets[(int)s].uv); }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = used.Count;
            int baseV = 0;
            for (int i = 0; i < used.Count; i++)
            {
                var bk = buckets[(int)used[i]];
                var tris = new int[bk.t.Count];
                for (int j = 0; j < tris.Length; j++) tris[j] = bk.t[j] + baseV;
                mesh.SetTriangles(tris, i, false);
                baseV += bk.Count;
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh MeshFromBucket(string name, Bucket bk)
        {
            if (bk.Count == 0) return null;
            var mesh = new Mesh { name = name };
            if (bk.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(bk.v);
            mesh.SetUVs(0, bk.uv);
            mesh.SetTriangles(bk.t, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The ground, one quad per 8 m cell, grass or PAVEMENT. A downtown is
        /// paved edge to edge, and the first preview stood the skyline on a
        /// lawn: a cell within a kilometre of Trade & Tryon, or within 20 m of
        /// any real building that is not a house, is concrete. Per-cell quads
        /// rather than a shared lattice so a cell can change bucket; four
        /// times the ground vertices of a tile, which is still nothing.
        /// </summary>
        static void BuildGround(CityMap map, TileMeshes tm, Vector2 min)
        {
            var grass = buckets[(int)Slot.Ground];
            var pave = buckets[(int)Slot.Pavement];
            int res = GroundRes;
            float cell = TileSize / res;
            int stride = res + 1;
            var heights = new float[stride * stride];
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                    heights[z * stride + x] =
                        CityElevation.GroundY(map, min.x + x * cell, min.y + z * cell);

            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var c = new Vector2(min.x + (x + 0.5f) * cell, min.y + (z + 0.5f) * cell);
                    bool paved = Vector2.Distance(c, map.uptown) < 1000f ||
                                 map.AnyFootprintNear(c, 20f, nonHouseOnly: true);
                    var bk = paved ? pave : grass;
                    float tile = paved ? 6f : 24f;
                    Vector3 P(int dx, int dz) => new Vector3((x + dx) * cell, heights[(z + dz) * stride + x + dx], (z + dz) * cell);
                    Vector2 U(int dx, int dz) => new Vector2((min.x + (x + dx) * cell) / tile, (min.y + (z + dz) * cell) / tile);
                    bk.Up(P(0, 0), P(0, 1), P(1, 1), P(1, 0), U(0, 0), U(0, 1), U(1, 1), U(1, 0));
                }
        }

        // ------------------------------------------------------------------
        //  Branches: a ramp beside its mainline, the minor arm of a fork.
        //
        //  OSM joins the ramp to the carriageway at the END of the taper; the
        //  ramp's own geometry then diverges at a shallow angle, and for its
        //  first hundred metres its ribbon lies INSIDE the mainline's. So the
        //  branch is CLIPPED: while its inner edge is inside the host it is
        //  moved out to the host's edge, and while the whole ribbon is
        //  inside, both edges collapse onto it — the ribbon starts as a
        //  zero-width wedge at the host's edge and widens as it leaves. The
        //  painted gore (the wedge between the two pavements once they part)
        //  is filled for as long as they run within a few metres, and the
        //  host's barrier and both roads' rails stand down for the whole
        //  attachment.
        //
        //  Both roads are CHAINS walked away from the node: a mainline is cut
        //  at every ramp node, so a ramp that runs alongside for longer than
        //  the piece it joins is clipped against the next piece too; and the
        //  ramp itself is cut at its own nodes, so its earlier pieces are
        //  clipped as well. A chain is ONE polyline, projected onto as a
        //  whole — projecting onto the pieces one at a time and taking the
        //  nearest clamped its way onto the end of a three-metre sliver and
        //  hung the ramp's inner edge off a phantom line.
        // ------------------------------------------------------------------
        const float GoreStep = 6f;
        const float GoreReach = 420f;
        const float GoreMaxGap = 4.5f;
        const float ClipLift = 0f;      // a clipped vertex sits ON the host's edge (a 3 cm slot swallowed a wheel probe)
        /// <summary>A branch further than this above or below its host is
        /// climbing away from it, not running beside it, whatever the plan
        /// view says. Clipping it there hung slivers of ramp in the air.</summary>
        const float AttachDy = 0.6f;

        /// <summary>Barrier / rail gaps: (edge, side, s0, s1). Side is the
        /// ribbon's own: -1 the L vertex (p - right*hw), +1 the R vertex.</summary>
        static readonly List<(int edge, int side, float s0, float s1)> goreGaps = new List<(int, int, float, float)>();

        /// <summary>A road as one polyline: an edge and its through-
        /// continuations, walked away from a node.</summary>
        class Chain
        {
            public readonly List<Vector2> pts = new List<Vector2>(64);
            public readonly List<CityMap.Edge> seg = new List<CityMap.Edge>(64);   // per segment
            public readonly List<float> sA = new List<float>(64), sB = new List<float>(64);   // arc on seg at its ends
            public readonly List<CityMap.Edge> edges = new List<CityMap.Edge>(4);
            public readonly List<float> cum = new List<float>(64);   // cumulative length at each pt

            public float Length => cum.Count > 0 ? cum[cum.Count - 1] : 0f;

            /// <summary>Nearest point on the chain: the edge and arc it lies
            /// on, and the chain's own walking direction there.</summary>
            public void Project(Vector2 p, out CityMap.Edge H, out float sM, out Vector2 q, out Vector2 dir)
                => Project(p, out H, out sM, out q, out dir, out _);

            /// <summary><paramref name="atEnd"/>: the nearest point is the
            /// chain's LAST vertex — the road being projected on has run out
            /// and the distance measured there is to a corner, not to a side.</summary>
            public void Project(Vector2 p, out CityMap.Edge H, out float sM, out Vector2 q, out Vector2 dir, out bool atEnd)
            {
                float best = float.MaxValue; int bi = 0; float bt = 0f;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    Vector2 a = pts[i], d = pts[i + 1] - a;
                    float L2 = d.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                    float dd = (p - (a + d * t)).sqrMagnitude;
                    if (dd < best) { best = dd; bi = i; bt = t; }
                }
                H = seg[bi];
                sM = Mathf.Lerp(sA[bi], sB[bi], bt);
                q = pts[bi] + (pts[bi + 1] - pts[bi]) * bt;
                dir = (pts[bi + 1] - pts[bi]).normalized;
                atEnd = bi == pts.Count - 2 && bt > 0.999f;
            }

            /// <summary>The point at an arc distance along the chain.</summary>
            public bool Walk(float dist, out CityMap.Edge E, out float sE, out Vector2 p, out Vector2 dir)
            {
                E = null; sE = 0f; p = default; dir = Vector2.up;
                if (pts.Count < 2 || dist > Length) return false;
                int i = 0;
                while (i + 2 < pts.Count && cum[i + 1] < dist) i++;
                float segL = cum[i + 1] - cum[i];
                float t = segL > 1e-6f ? Mathf.Clamp01((dist - cum[i]) / segL) : 0f;
                E = seg[i];
                sE = Mathf.Lerp(sA[i], sB[i], t);
                p = pts[i] + (pts[i + 1] - pts[i]) * t;
                dir = (pts[i + 1] - pts[i]).normalized;
                return true;
            }
        }

        /// <summary>Walk a road away from a node through its through-
        /// continuations, out to a reach. A ramp's chain stays on ramps and
        /// never enters the host it is being clipped against.</summary>
        static Chain BuildChain(CityMap map, CityMap.Edge first, int node, bool linkChain, float reach, Chain avoid)
        {
            var ch = new Chain();
            var cur = first; int at = node; float len = 0f;
            for (int k = 0; k < 6; k++)
            {
                ch.edges.Add(cur);
                bool fromA = cur.a == at;
                int n = cur.pts.Length;
                for (int i = 0; i < n; i++)
                {
                    int pi = fromA ? i : n - 1 - i;
                    var p = cur.pts[pi];
                    float sOn = cur.s[pi];
                    if (i == 0)
                    {
                        if (ch.pts.Count == 0) { ch.pts.Add(p); ch.cum.Add(0f); }
                        continue;   // a later piece's first point is the shared node, already there
                    }
                    int prevPi = fromA ? pi - 1 : pi + 1;
                    ch.seg.Add(cur); ch.sA.Add(cur.s[prevPi]); ch.sB.Add(sOn);
                    ch.cum.Add(ch.cum[ch.cum.Count - 1] + Vector2.Distance(ch.pts[ch.pts.Count - 1], p));
                    ch.pts.Add(p);
                }
                len += cur.length;
                if (len >= reach) break;
                int far = fromA ? cur.b : cur.a;
                var nx = NextThrough(map, cur, far, linkChain);
                if (nx == null || ch.edges.Contains(nx)) break;
                if (avoid != null && avoid.edges.Contains(nx)) break;
                if (linkChain && (nx.link != first.link || (!first.link && nx.cls != first.cls))) break;
                cur = nx; at = far;
            }
            return ch;
        }

        /// <summary>The edge continuing <paramref name="e"/> through
        /// <paramref name="node"/>: the arm leaving the node most nearly the
        /// way e arrived. Ramps are skipped for a mainline chain.</summary>
        static CityMap.Edge NextThrough(CityMap map, CityMap.Edge e, int node, bool allowLinks)
        {
            var dIn = -OutDir(e, node);
            CityMap.Edge best = null; float bd = -ThroughCos;
            foreach (var oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == e || o.a == o.b) continue;
                if (o.link && !allowLinks) continue;
                float d = Vector2.Dot(dIn, OutDir(o, node));
                if (d < bd) continue;
                if (best == null || d > bd) { best = o; bd = d; }
            }
            return best;
        }

        /// <summary>One branch edge's attachment to a host chain: the arc
        /// range that is clipped, which side of the host it lies on (in the
        /// host CHAIN's frame) and which of its own vertices faces the host.</summary>
        class Clip
        {
            public Chain host;
            public float sFrom, sTo;
            public int side;        // host-chain frame: +1 = the chain's left (its rM)
            public int innerSide;   // the branch edge's own frame: +1 = its R vertex
        }
        static readonly Dictionary<int, List<Clip>> clips = new Dictionary<int, List<Clip>>();   // per branch edge

        static Clip ClipAt(CityMap.Edge e, float s)
        {
            if (!clips.TryGetValue(e.index, out var list)) return null;
            foreach (var c in list) if (s >= c.sFrom && s <= c.sTo) return c;
            return null;
        }

        static void BuildGores(CityMap map, Trims trims, TileMeshes tm, Vector2 min, Vector2 max)
        {
            // Branch ends within reach of the tile: a clip starting outside
            // can run inside, and a barrier gap inside can come from a node outside.
            segScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * (GoreReach + 20f), max + Vector2.one * (GoreReach + 20f), segScratch);
            edgeScratch.Clear();
            foreach (var packed in segScratch) edgeScratch.Add(packed >> 12);
            foreach (var ei in edgeScratch)
            {
                var L = map.edges[ei];
                for (int end = 0; end < 2; end++)
                {
                    int n = end == 0 ? L.a : L.b;
                    int hostIdx = end == 0 ? trims.branchA[ei] : trims.branchB[ei];
                    if (hostIdx < 0) continue;
                    var np = map.nodes[n];
                    if (np.x < min.x - GoreReach - 20f || np.x > max.x + GoreReach + 20f ||
                        np.y < min.y - GoreReach - 20f || np.y > max.y + GoreReach + 20f) continue;
                    EmitBranch(map, trims, tm, min, max, L, map.edges[hostIdx], n);
                }
            }
        }

        /// <summary>The host's half width on the branch's side, in the host
        /// CHAIN's frame: the host edge's squeezed extent, so a clipped
        /// vertex lands on the edge the host actually draws.</summary>
        static float HostHalf(CityMap map, Trims trims, CityMap.Edge H, float sM, Vector2 rMChain, int side)
        {
            LaneExtents(map, trims, H, sM, out float hl, out float hr);
            var tH = H.TangentAt(sM);
            bool same = Vector2.Dot(rMChain, new Vector2(-tH.y, tH.x)) >= 0f;
            int sideH = same ? side : -side;
            return sideH > 0 ? hr : hl;
        }

        static void EmitBranch(CityMap map, Trims trims, TileMeshes tm, Vector2 min, Vector2 max,
                               CityMap.Edge L, CityMap.Edge M, int node)
        {
            var host = BuildChain(map, M, node, linkChain: false, GoreReach + 60f, null);
            var br = BuildChain(map, L, node, linkChain: true, GoreReach + 10f, host);
            foreach (var h in host.edges) foreach (var b in br.edges) clipPairs.Add(PairKey(b.index, h.index));

            int side = 0;
            bool prevOk = false;
            Vector3 prevIn = default, prevOut = default;
            float prevSM = 0f;
            int quads = 0;
            float attachedTo = -1f;
            var gapOn = new Dictionary<int, (float s0, float s1, int sideH)>();   // host edge -> sM range attached
            var brOn = new Dictionary<int, (float s0, float s1, int inner)>();    // branch edge -> s range attached
            for (int k = 0; k <= 70; k++)
            {
                float travelled = k * GoreStep;
                if (travelled > GoreReach) break;
                if (!br.Walk(travelled, out var E, out float sE, out var p, out var dirL)) break;
                host.Project(p, out var H, out float sM, out var q, out var dirM, out bool hostOut);
                if (hostOut) break;   // the host chain ran out before the branch let go of it
                var rM = new Vector2(-dirM.y, dirM.x);
                float off = Vector2.Dot(p - q, rM);
                int sideNow = off >= 0f ? 1 : -1;
                // Which side of the host the branch lies on is read from the
                // first sample that is clearly off the host's centreline. OSM's
                // first segment at the node is often a metre of noise: read
                // from there, a wrong side collapsed the whole ramp onto the
                // FAR edge of its mainline.
                if (side == 0 && Mathf.Abs(off) > 0.4f) side = sideNow;
                var rL = new Vector2(-dirL.y, dirL.x);
                float mHalf = side == 0 ? trims.HalfWidthAt(H, sM) : HostHalf(map, trims, H, sM, rM, side);
                float lHalf = trims.HalfWidthAt(E, sE);
                var outer = q + rM * (side * mHalf);
                float lSign = Vector2.Dot(rL, rM) >= 0f ? -side : side;
                var inner = p + rL * (lSign * lHalf);
                float gap = side == 0 ? 0f : Vector2.Dot(inner - outer, rM) * side;
                float yIn = E.YAt(sE), yOut = H.YAt(sM);
                bool attached = (gap <= GoreMaxGap && sideNow == side || Mathf.Abs(off) < 0.4f)
                                && Mathf.Abs(yIn - yOut) < AttachDy;
                if (!attached) break;
                attachedTo = travelled;

                // the host's frame at this piece, for its gap
                var tH = H.TangentAt(sM);
                int sideH = side * (Vector2.Dot(rM, new Vector2(-tH.y, tH.x)) >= 0f ? 1 : -1);
                if (!gapOn.TryGetValue(H.index, out var r)) r = (sM, sM, sideH);
                gapOn[H.index] = (Mathf.Min(r.s0, sM), Mathf.Max(r.s1, sM), side == 0 ? r.sideH : sideH);
                // the branch's frame: which of ITS vertices faces the host
                var tE = E.TangentAt(sE);
                int innerE = side == 0 ? 0 : (Vector2.Dot(new Vector2(-tE.y, tE.x), rM) * side < 0f ? 1 : -1);
                if (!brOn.TryGetValue(E.index, out var b)) b = (sE, sE, innerE);
                brOn[E.index] = (Mathf.Min(b.s0, sE), Mathf.Max(b.s1, sE), b.inner == 0 ? innerE : b.inner);

                // the painted gore: only once the branch has actually left the host
                bool ok = gap > 0.02f && sM > 0.5f && sM < H.length - 0.5f;
                var vIn = new Vector3(inner.x - tm.origin.x, yIn, inner.y - tm.origin.z);
                var vOut = new Vector3(outer.x - tm.origin.x, yOut, outer.y - tm.origin.z);
                if (ok && prevOk)
                {
                    var mid = (inner + outer + (new Vector2(prevIn.x, prevIn.z) + new Vector2(prevOut.x, prevOut.z) + new Vector2(tm.origin.x, tm.origin.z) * 2f)) * 0.25f;
                    if (mid.x >= min.x && mid.x < max.x && mid.y >= min.y && mid.y < max.y)
                    {
                        bool elev = H.ElevatedAt(sM);
                        var bk = buckets[(int)SlotOf(JunctionProfile, SurfaceOf(H, elev))];
                        float v0 = prevSM / 12f, v1 = sM / 12f;
                        bk.Up(prevOut, vOut, vIn, prevIn,
                              new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v1), new Vector2(1f, v0));
                        quads++;
                    }
                }
                prevOk = ok; prevIn = vIn; prevOut = vOut; prevSM = sM;
            }
            if (attachedTo < 0f || side == 0) return;

            // Every branch piece touched is clipped over its attached range,
            // extended back to the node end it shares with the piece before.
            for (int i = 0; i < br.edges.Count; i++)
            {
                var E = br.edges[i];
                if (!brOn.TryGetValue(E.index, out var b) || b.inner == 0) continue;
                float s0 = b.s0 - 1f, s1 = b.s1 + 1f;
                int nodeEnd = i == 0 ? node : SharedNode(E, br.edges[i - 1]);
                if (E.a == nodeEnd) s0 = -1f; else if (E.b == nodeEnd) s1 = E.length + 1f;
                if (!clips.TryGetValue(E.index, out var list)) clips[E.index] = list = new List<Clip>(2);
                list.Add(new Clip { host = host, sFrom = s0, sTo = s1, side = side, innerSide = b.inner });
                goreGaps.Add((E.index, b.inner, s0 - 1f, s1 + 1f));
            }
            // ...and every host piece stands its barrier and rail down there.
            for (int i = 0; i < host.edges.Count; i++)
            {
                var H = host.edges[i];
                if (!gapOn.TryGetValue(H.index, out var g)) continue;
                float s0 = g.s0 - 1f, s1 = g.s1 + 1f;
                int nodeEnd = i == 0 ? node : SharedNode(H, host.edges[i - 1]);
                if (H.a == nodeEnd) s0 = -1f; else if (H.b == nodeEnd) s1 = H.length + 1f;
                goreGaps.Add((H.index, g.sideH, s0, s1));
            }
            if (quads > 0) tm.goreCount++;
            tm.branchCount++;
        }

        static int SharedNode(CityMap.Edge e, CityMap.Edge prev) =>
            e.a == prev.a || e.a == prev.b ? e.a : e.b;

        static bool InGoreGap(int edge, int side, float s0, float s1)
        {
            foreach (var g in goreGaps)
                if (g.edge == edge && g.side == side && s1 > g.s0 && s0 < g.s1) return true;
            return false;
        }

        // ------------------------------------------------------------------
        //  Road ribbons, kerbs, decks, rails, barriers, piers.
        // ------------------------------------------------------------------

        /// <summary>One cross-section of a ribbon.</summary>
        struct Section
        {
            public float s;
            public Vector3 L, R;      // tile-local, L = right of travel (see below)
            public Vector2 right;     // map-view unit, R - L direction
            public bool elev;
            public bool clippedIn;    // the inner edge was moved onto the host: no kerb there
            public bool collapsed;    // zero width: the whole ribbon is inside the host
            public int innerSide;     // -1 / +1 which vertex is the inner one while clipped, 0 otherwise
            /// <summary>A parallel neighbour this section was squeezed against
            /// on that side (edge index, or -1), and whether it is on
            /// structure there: the two share one barrier or one rail.</summary>
            public int nbL, nbR;
            public bool nbElevL, nbElevR;
            /// <summary>Texture U at each vertex, from its TRUE lateral
            /// offset. A squeezed or clipped ribbon crops the painted
            /// profile instead of compressing it, so the lane lines stay
            /// where the lanes are; compressed, they slalomed wherever a
            /// neighbour's mapped line wandered.</summary>
            public float uL, uR;
            /// <summary>How deep this section's kerb face reaches: the kerb
            /// plus the crest allowance the ground was sunk by here.</summary>
            public float kerb;
        }
        /// <summary>Edge pairs that are host and branch: they overlap by
        /// design and are never squeezed against each other.</summary>
        static readonly HashSet<long> clipPairs = new HashSet<long>();
        static long PairKey(int a, int b) => ((long)Mathf.Min(a, b) << 24) | (long)Mathf.Max(a, b);
        static readonly List<Section> sections = new List<Section>(64);
        static readonly List<float> sampleS = new List<float>(64);

        /// <summary>
        /// Where a ribbon is sampled: every elevation station, every polyline
        /// vertex (with a mitred cross-section, so a bend inside an edge does
        /// not pinch), the taper ends, and the exact trims.
        /// </summary>
        static void SamplePositions(CityMap map, CityMap.Edge e, Trims trims, float sMin, float sMax)
        {
            sampleS.Clear();
            sampleS.Add(sMin);
            for (int i = 0; i < e.stS.Length; i++) if (e.stS[i] > sMin + 0.6f && e.stS[i] < sMax - 0.6f) sampleS.Add(e.stS[i]);
            for (int i = 1; i + 1 < e.s.Length; i++) if (e.s[i] > sMin + 0.6f && e.s[i] < sMax - 0.6f) sampleS.Add(e.s[i]);
            int ei = e.index;
            if (trims.taperA[ei] > 0f && trims.taperA[ei] > sMin + 0.6f && trims.taperA[ei] < sMax - 0.6f) sampleS.Add(trims.taperA[ei]);
            float tb = e.length - trims.taperB[ei];
            if (trims.taperB[ei] > 0f && tb > sMin + 0.6f && tb < sMax - 0.6f) sampleS.Add(tb);
            // A clipped branch's inner edge is the host's edge: it has to
            // bend wherever the host bends, or a 10 m straight across a
            // kink in the mainline leaves a sliver of nothing between them.
            // And it has to be sampled where the cut STARTS, where it
            // COLLAPSES and wherever it moves fast: a road meeting its host
            // at forty-five degrees is cut across its whole width in ten
            // metres, and one section every ten metres drew that as a
            // triangle with a hole beside it.
            if (clips.TryGetValue(e.index, out var clist))
                foreach (var c in clist)
                {
                    foreach (var v in c.host.pts)
                    {
                        CityElevation.ProjectOn(e, v, out float sOn);
                        if (sOn < c.sFrom || sOn > c.sTo || sOn <= sMin + 0.6f || sOn >= sMax - 0.6f) continue;
                        if ((e.PointAt(sOn) - v).sqrMagnitude > 40f * 40f) continue;
                        sampleS.Add(sOn);
                    }
                    float from = Mathf.Max(sMin, c.sFrom), to = Mathf.Min(sMax, c.sTo);
                    int prevState = -1; float prevLam = 0f, prevS = from;
                    for (float sc = from; sc <= to + 0.01f; sc += 1f)
                    {
                        var pc = e.PointAt(sc);
                        var tc = e.TangentAt(sc);
                        var rc = new Vector2(-tc.y, tc.x);
                        int state = 0; float lam = 0f;
                        if (CutAgainstHost(map, trims, e, sc, pc, rc, trims.HalfWidthAt(e, sc), e.YAt(sc), c, out lam, out bool col))
                            state = col ? 2 : 1;
                        bool change = prevState >= 0 && (state != prevState || (state == 1 && Mathf.Abs(lam - prevLam) > 0.8f));
                        if (change)
                        {
                            if (prevS > sMin + 0.6f && prevS < sMax - 0.6f) sampleS.Add(prevS);
                            if (sc > sMin + 0.6f && sc < sMax - 0.6f) sampleS.Add(sc);
                        }
                        prevState = state; prevLam = lam; prevS = sc;
                    }
                }
            sampleS.Add(sMax);
            sampleS.Sort();
            // drop near-duplicates (a station on a vertex)
            int w = 1;
            for (int i = 1; i < sampleS.Count; i++)
                if (sampleS[i] - sampleS[w - 1] > 0.5f || i == sampleS.Count - 1) sampleS[w++] = sampleS[i];
            sampleS.RemoveRange(w, sampleS.Count - w);
        }

        /// <summary>The cross-section direction at an arc position: the
        /// segment tangent, or at a polyline vertex the mitre of its two
        /// segments (widened by 1/cos of the half turn, capped), and at a
        /// mitred NODE the mitre with the continuing edge.</summary>
        static Vector2 RightAt(CityMap map, Trims trims, CityMap.Edge e, float s, out float widen)
        {
            widen = 1f;
            Vector2 tan;
            int seg = e.SegmentAt(s, out float t);
            bool atVertexA = seg > 0 && t < 1e-3f;
            bool atVertexB = seg + 2 < e.s.Length && t > 1f - 1e-3f;
            if (atVertexA || atVertexB)
            {
                int v = atVertexA ? seg : seg + 1;
                Vector2 t0 = (e.pts[v] - e.pts[v - 1]).normalized;
                Vector2 t1 = (e.pts[v + 1] - e.pts[v]).normalized;
                tan = (t0 + t1);
                if (tan.sqrMagnitude < 1e-6f) tan = t0; else tan.Normalize();
                // The bisector direction, at the plain half width. The true
                // mitre offset is hw / cos(half turn); widening by that put
                // the host's edge outside every extent the clips and the
                // audit computed (a rail 20 cm into the ramp beside it), and
                // the notch this leaves at the outside of a bend is a few
                // centimetres on any bend OSM draws.
            }
            else if (s <= 1e-3f && trims.mitre[e.a] && IsThrough(trims, e, e.a))
            {
                tan = MitreAt(map, trims, e, e.a, out widen);
            }
            else if (s >= e.length - 1e-3f && trims.mitre[e.b] && IsThrough(trims, e, e.b))
            {
                tan = MitreAt(map, trims, e, e.b, out widen);
            }
            else tan = e.TangentAt(s);
            return new Vector2(-tan.y, tan.x);
        }

        static bool IsThrough(Trims trims, CityMap.Edge e, int node) =>
            trims.throughA[node] == e.index || trims.throughB[node] == e.index;

        /// <summary>The through tangent at a mitred node, in <paramref name="e"/>'s
        /// own direction of travel.</summary>
        static Vector2 MitreAt(CityMap map, Trims trims, CityMap.Edge e, int node, out float widen)
        {
            widen = 1f;
            int oi = trims.throughA[node] == e.index ? trims.throughB[node] : trims.throughA[node];
            var own = e.a == node ? e.TangentAt(0f) : e.TangentAt(e.length);   // e's travel direction at the node
            if (oi < 0 || oi == e.index) return own;
            var o = map.edges[oi];
            // travel through the node: in along e (the reverse of leaving it), out along o
            var m = OutDir(o, node) - OutDir(e, node);
            if (m.sqrMagnitude < 1e-6f) return own;
            m.Normalize();
            if (Vector2.Dot(m, own) < 0f) m = -m;
            return m;
        }

        static void BuildRoadsAndDecks(CityMap map, Trims trims, TileMeshes tm,
                                       Vector2 min, Vector2 max)
        {
            segScratch.Clear();
            edgeScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * 40f, max + Vector2.one * 40f, segScratch);
            foreach (var packed in segScratch) edgeScratch.Add(packed >> 12);

            foreach (var ei in edgeScratch)
            {
                var e = map.edges[ei];
                if (e.a == e.b && e.length < 1f) continue;
                float sMin = trims.atA[ei], sMax = e.length - trims.atB[ei];
                if (sMax - sMin < 0.6f) continue;   // the fans own all of it

                var con = buckets[(int)Slot.Concrete];
                bool barrier = Barriered(e);

                // ---- sections ----
                BuildSections(map, trims, tm, e, sMin, sMax);

                // ---- spans ----
                float sincePier = PierEvery * 0.6f;
                for (int i = 1; i < sections.Count; i++)
                {
                    var A = sections[i - 1]; var B = sections[i];
                    if (A.collapsed && B.collapsed) continue;
                    var mid = e.PointAt((A.s + B.s) * 0.5f);
                    bool mine = mid.x >= min.x && mid.x < max.x && mid.y >= min.y && mid.y < max.y;
                    if (!mine) { sincePier += B.s - A.s; continue; }

                    // Per SPAN, not per edge: a road that climbs onto a
                    // viaduct halfway along is asphalt up to the abutment and
                    // concrete over the water.
                    var bk = buckets[(int)RoadSlot(e, A.elev || B.elev)];
                    float v0 = A.s / RoadVTile, v1 = B.s / RoadVTile;
                    // U = 0 on the left of travel (the R vertex), so a one-way
                    // carriageway's narrow inside shoulder and wide outside
                    // shoulder land where the painter put them. The winding
                    // (near-left, far-left, far-right, near-right) is the
                    // verified face-up order.
                    bk.Quad(A.L, B.L, B.R, A.R,
                        new Vector2(A.uL, v0), new Vector2(B.uL, v1),
                        new Vector2(B.uR, v1), new Vector2(A.uR, v0));

                    bool gapL = InGoreGap(e.index, -1, A.s, B.s) || (A.innerSide == -1 && (A.clippedIn || A.collapsed)) || (B.innerSide == -1 && (B.clippedIn || B.collapsed));
                    bool gapR = InGoreGap(e.index, 1, A.s, B.s) || (A.innerSide == 1 && (A.clippedIn || A.collapsed)) || (B.innerSide == 1 && (B.clippedIn || B.collapsed));
                    // Two roads squeezed together share ONE barrier or rail
                    // between them: the lower-numbered edge draws it.
                    bool shareL = (A.nbL >= 0 && A.nbL < e.index) || (B.nbL >= 0 && B.nbL < e.index);
                    bool shareR = (A.nbR >= 0 && A.nbR < e.index) || (B.nbR >= 0 && B.nbR < e.index);
                    // a wedge tip has no rail and no barrier on either side:
                    // the stub would stand on the host's edge, in its lane
                    if (A.collapsed || B.collapsed) { gapL = true; gapR = true; }
                    if (A.elev || B.elev)
                    {
                        bool nbDeckL = A.nbElevL || B.nbElevL, nbDeckR = A.nbElevR || B.nbElevR;
                        EmitDeckSpan(con, A, B, v0, v1, railL: !gapL && !(shareL && nbDeckL), railR: !gapR && !(shareR && nbDeckR));
                        sincePier += B.s - A.s;
                        if (sincePier >= PierEvery)
                        {
                            sincePier = 0f;
                            EmitPier(map, e, tm, (A.s + B.s) * 0.5f);
                        }
                    }
                    else
                    {
                        // Grounded span: give the tarmac a side. An elevated
                        // one already has a whole deck box.
                        EmitKerb(con, A, B, v0, v1, kerbL: !gapL, kerbR: !gapR);
                        if (barrier)
                        {
                            // THE MEDIAN SIDE ONLY. A Jersey wall stood on
                            // both edges of every freeway carriageway, and
                            // the outside shoulder of a real interstate is
                            // open verge — the owner named it at once:
                            // "outside shoulder walls they don't have in
                            // real life". R is the LEFT of travel (see
                            // BuildSections), which on a one-way carriageway
                            // is the median. The outside gets a wall only
                            // where the road runs in a CUT — the retaining
                            // walls of the 277 trench and of I-77 through the
                            // near west side are real, and a car off the
                            // shoulder there would be inside the hill.
                            var outL = -A.right;
                            bool nbBarL = (A.nbL >= 0 && Barriered(map.edges[A.nbL])) || (B.nbL >= 0 && Barriered(map.edges[B.nbL]));
                            bool nbBarR = (A.nbR >= 0 && Barriered(map.edges[A.nbR])) || (B.nbR >= 0 && Barriered(map.edges[B.nbR]));
                            if (!gapL && !(shareL && nbBarL) && InCut(tm, A, B, outL)) EmitBarrier(A.L, B.L, outL, v0, v1);
                            if (!gapR && !(shareR && nbBarR)) EmitBarrier(A.R, B.R, A.right, v0, v1);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Two roads running side by side closer than their widths — I-77's
        /// general lanes beside its express lanes, a frontage road beside a
        /// freeway, the two carriageways of a divided road mapped tight —
        /// used to overlap, and each one's barrier stood inside the other's
        /// lane. The space between the centrelines is now split between
        /// them in proportion to their widths, and where the strip left is
        /// narrower than a barrier the two share one (the lower-numbered edge
        /// draws it). Host and branch pairs are excluded: they overlap by
        /// design and the clip owns them.
        /// </summary>
        static readonly HashSet<int> nbScratch = new HashSet<int>();
        static void SqueezeSection(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e,
                                   Vector2 p, Vector2 right, float hw, float y, ref Section sec)
        {
            if (sec.collapsed) return;
            float hwL = Vector2.Distance(new Vector2(sec.L.x + tm.origin.x, sec.L.z + tm.origin.z), p);
            float hwR = Vector2.Distance(new Vector2(sec.R.x + tm.origin.x, sec.R.z + tm.origin.z), p);
            float sqL = hwL, sqR = hwR;
            Squeeze(map, trims, e, p, right, hw, y, ref sqL, ref sqR,
                    out sec.nbL, out sec.nbR, out sec.nbElevL, out sec.nbElevR);
            if (sqR < hwR - 1e-3f)
                sec.R = new Vector3(p.x + right.x * sqR - tm.origin.x, y, p.y + right.y * sqR - tm.origin.z);
            if (sqL < hwL - 1e-3f)
                sec.L = new Vector3(p.x - right.x * sqL - tm.origin.x, y, p.y - right.y * sqL - tm.origin.z);
        }

        /// <summary>The ribbon's half width each side at an arc position —
        /// taper and squeeze applied — which is what the tile draws and what
        /// the drive audit must probe. (The audit's cross ray once ran to the
        /// nominal edge and reported every squeezed barrier as a wall.)</summary>
        public static void LaneExtents(CityMap map, Trims trims, CityMap.Edge e, float s, out float hwL, out float hwR)
        {
            var p = e.PointAt(s);
            var tan = e.TangentAt(s);
            var right = new Vector2(-tan.y, tan.x);
            float hw = trims.HalfWidthAt(e, s);
            hwL = hw; hwR = hw;
            Squeeze(map, trims, e, p, right, hw, e.YAt(s), ref hwL, ref hwR, out _, out _, out _, out _);
            // a clipped branch: its inner edge is the host's edge (or nothing at all)
            if (extentDepth > 2) return;   // two edges each the other's host: stop at the second level
            extentDepth++;
            try { ClipExtents(map, trims, e, s, p, right, hw, ref hwL, ref hwR); }
            finally { extentDepth--; }
        }
        static int extentDepth;
        static void ClipExtents(CityMap map, Trims trims, CityMap.Edge e, float s, Vector2 p, Vector2 right, float hw,
                                ref float hwL, ref float hwR)
        {
            var clip = ClipAt(e, s);
            if (clip == null) return;
            if (!CutAgainstHost(map, trims, e, s, p, right, hw, e.YAt(s), clip, out float lam, out bool collapsed)) return;
            if (collapsed) { hwL = 0f; hwR = 0f; return; }
            if (clip.innerSide > 0) hwR = Mathf.Clamp(lam, 0f, hwR); else hwL = Mathf.Clamp(-lam, 0f, hwL);
        }

        static void Squeeze(CityMap map, Trims trims, CityMap.Edge e, Vector2 p, Vector2 right, float hw, float y,
                            ref float hwL, ref float hwR, out int nbL, out int nbR, out bool nbElevL, out bool nbElevR)
        {
            nbL = -1; nbR = -1; nbElevL = false; nbElevR = false;
            nbScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 32f, p + Vector2.one * 32f, nbScratch);
            var tan = new Vector2(right.y, -right.x);
            // the nearest parallel road on each side, whatever it is
            float dL = float.MaxValue, dR = float.MaxValue;
            int eL = -1, eR = -1; float atL = 0f, atR = 0f;
            foreach (var packed in nbScratch)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                if (oi == e.index) continue;
                var o = map.edges[oi];
                if (o.a == e.a || o.a == e.b || o.b == e.a || o.b == e.b) continue;   // arms of one junction
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float t = Vector2.Dot(p - a, d) / L2;
                if (t <= 0f || t >= 1f) continue;                 // beside a segment, not off its end
                var tO = d / Mathf.Sqrt(L2);
                if (Mathf.Abs(Vector2.Dot(tan, tO)) < 0.9f) continue;
                var q = a + d * t;
                float at = o.s[si] + Mathf.Sqrt(L2) * t;
                if (Mathf.Abs(o.YAt(at) - y) > 1.0f) continue;   // a different level: nothing to share
                float dist = Vector2.Distance(p, q);
                if (dist >= hw + trims.HalfWidthAt(o, at) + 0.3f) continue;
                int side = Vector2.Dot(q - p, right) >= 0f ? 1 : -1;
                if (side > 0) { if (dist < dR) { dR = dist; eR = oi; atR = at; } }
                else { if (dist < dL) { dL = dist; eL = oi; atL = at; } }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                int oi = side > 0 ? eR : eL;
                if (oi < 0) continue;
                // a host or branch of ours on this side: the clip owns it
                if (clipPairs.Contains(PairKey(e.index, oi))) continue;
                var o = map.edges[oi];
                float at = side > 0 ? atR : atL, dist = side > 0 ? dR : dL;
                float hwO = trims.HalfWidthAt(o, at);
                float mine = Mathf.Max(1.2f, dist * hw / (hw + hwO) - 0.15f);
                float theirs = Mathf.Max(1.2f, dist * hwO / (hw + hwO) - 0.15f);
                if (side > 0) { if (mine < hwR) hwR = mine; } else { if (mine < hwL) hwL = mine; }
                if (dist - mine - theirs < 1.2f)
                {
                    bool oElev = o.ElevatedAt(at);
                    if (side > 0) { nbR = oi; nbElevR = oElev; } else { nbL = oi; nbElevL = oElev; }
                }
            }
        }

        /// <summary>For the audit: the clip state of an edge at an arc
        /// position, from the LAST tile built. Empty when the edge is not a
        /// clipped branch there.</summary>
        public static string DescribeClip(CityMap map, Trims trims, CityMap.Edge e, float s)
        {
            var clip = ClipAt(e, s);
            if (clip == null) return "";
            var p = e.PointAt(s);
            clip.host.Project(p, out var H, out float sM, out var q, out var dirM, out bool atEnd);
            var rM = new Vector2(-dirM.y, dirM.x);
            float dy = H.YAt(sM) - e.YAt(s);
            float off = Vector2.Dot(p - q, rM) * clip.side;
            float mHalf = HostHalf(map, trims, H, sM, rM, clip.side);
            var tan = e.TangentAt(s);
            var right = new Vector2(-tan.y, tan.x);
            bool cut = CutAgainstHost(map, trims, e, s, p, right, trims.HalfWidthAt(e, s), e.YAt(s), clip, out float lam, out bool collapsed);
            return $" [clip {clip.sFrom:0}..{clip.sTo:0} side{clip.side} inner{(clip.innerSide > 0 ? "R" : "L")} host e{H.index} '{H.name}' ({clip.host.edges.Count} pieces, {clip.host.Length:0} m) at sM={sM:0}/{H.length:0}{(atEnd ? " END" : "")} off {off:+0.0;-0.0} m dy {dy:+0.00;-0.00} hostHw {mHalf:0.0} cut {(cut ? lam.ToString("+0.0;-0.0") : "none")}{(collapsed ? " COLLAPSED" : "")}{(Mathf.Abs(dy) > AttachDy ? " DETACHED(dy)" : "")}]";
        }

        /// <summary>Every cross-section of one ribbon, into <see cref="sections"/>.</summary>
        static void BuildSections(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e,
                                  float sMin, float sMax)
        {
            SamplePositions(map, e, trims, sMin, sMax);
            sections.Clear();
            foreach (var s in sampleS)
            {
                var p = e.PointAt(s);
                var right = RightAt(map, trims, e, s, out float widen);
                // (-tan.y, tan.x) is tan turned 90 degrees COUNTER-clockwise
                // in map view, which is the LEFT of travel. So the vertex
                // called L below (p - right*hw) sits on the RIGHT of
                // travel and R on the left; the ribbon's winding is
                // verified with the names as they are, so the names stay
                // and the texture U is mirrored to match: u = 0, the
                // painter's left shoulder, goes on R.
                float y = e.YAt(s);
                if (s <= 1e-3f && trims.mitre[e.a]) y = map.nodeY[e.a];
                else if (s >= e.length - 1e-3f && trims.mitre[e.b]) y = map.nodeY[e.b];
                float hw = trims.HalfWidthAt(e, s) * widen;
                var sec = new Section
                {
                    s = s, right = right, elev = e.ElevatedAt(s), nbL = -1, nbR = -1,
                    L = new Vector3(p.x - right.x * hw - tm.origin.x, y, p.y - right.y * hw - tm.origin.z),
                    R = new Vector3(p.x + right.x * hw - tm.origin.x, y, p.y + right.y * hw - tm.origin.z),
                };
                var clip = ClipAt(e, s);
                if (clip != null) ClipSection(map, trims, tm, e, s, p, right, hw, y, clip, ref sec);
                SqueezeSection(map, trims, tm, e, p, right, hw, y, ref sec);
                // U from where the vertex actually is: 1 at the nominal L
                // edge (-hw), 0 at the nominal R edge (+hw), so a vertex
                // moved inward by a squeeze or a clip shows the painted
                // profile CROPPED at that lateral, not squashed into the gap.
                float latL = Vector2.Dot(new Vector2(sec.L.x + tm.origin.x, sec.L.z + tm.origin.z) - p, right);
                float latR = Vector2.Dot(new Vector2(sec.R.x + tm.origin.x, sec.R.z + tm.origin.z) - p, right);
                sec.uL = hw > 0.05f ? Mathf.Clamp01(0.5f - latL / (2f * hw)) : 1f;
                sec.uR = hw > 0.05f ? Mathf.Clamp01(0.5f - latR / (2f * hw)) : 0f;
                sec.kerb = KerbDepth + e.CrestAt(s);
                sections.Add(sec);
            }
        }

        /// <summary>For the audit: every section of an edge as the LAST tile
        /// would draw it — lateral offset of each vertex from the centreline
        /// and the clip / squeeze flags.</summary>
        public static string DescribeSections(CityMap map, Trims trims, CityMap.Edge e)
        {
            var tm = new TileMeshes { origin = Vector3.zero };
            BuildSections(map, trims, tm, e, trims.atA[e.index], e.length - trims.atB[e.index]);
            var sb = new System.Text.StringBuilder();
            sb.Append($"      sections of e{e.index} '{e.name}'{(e.link ? " L" : "")} len {e.length:0} hw {e.width * 0.5f:0.0}");
            if (clips.TryGetValue(e.index, out var clist))
                foreach (var c in clist) sb.Append($" clip {c.sFrom:0}..{c.sTo:0} side{c.side} inner{(c.innerSide > 0 ? "R" : "L")} host e{c.host.edges[0].index}+{c.host.edges.Count - 1}");
            sb.Append(":\n");
            foreach (var sec in sections)
            {
                var p = e.PointAt(sec.s);
                float latL = Vector2.Dot(new Vector2(sec.L.x, sec.L.z) - p, sec.right);
                float latR = Vector2.Dot(new Vector2(sec.R.x, sec.R.z) - p, sec.right);
                sb.Append($"        s={sec.s:0.0} y={sec.L.y:0.00} L{latL:+0.0;-0.0} R{latR:+0.0;-0.0}{(sec.elev ? " deck" : "")}{(sec.collapsed ? " COLLAPSED" : sec.clippedIn ? " clippedIn" : "")}{(sec.innerSide != 0 ? " inner" + (sec.innerSide > 0 ? "R" : "L") : "")}{(sec.nbL >= 0 ? " nbL" + sec.nbL : "")}{(sec.nbR >= 0 ? " nbR" + sec.nbR : "")}\n");
            }
            return sb.ToString();
        }

        /// <summary>For the audit: the host edge a branch is clipped against
        /// at an arc position, or -1.</summary>
        public static int HostEdgeAt(CityMap.Edge e, float s)
        {
            var clip = ClipAt(e, s);
            if (clip == null) return -1;
            clip.host.Project(e.PointAt(s), out var H, out _, out _, out _);
            return H.index;
        }

        /// <summary>Clip one branch section against its host chain.</summary>
        static void ClipSection(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, float s,
                                Vector2 p, Vector2 right, float hw, float y, Clip clip, ref Section sec)
        {
            if (!CutAgainstHost(map, trims, e, s, p, right, hw, y, clip, out float lam, out bool collapsed)) return;
            sec.innerSide = clip.innerSide;
            // on the host's edge, at the host's height there: the two
            // surfaces meet along the seam instead of stepping (the branch
            // may sit a few tens of centimetres above or below its host)
            var vp = p + right * lam;
            clip.host.Project(vp, out var Hv, out float sMv, out _, out _);
            float yv = Mathf.Abs(Hv.YAt(sMv) - y) <= AttachDy ? Hv.YAt(sMv) : y;
            var v = new Vector3(vp.x - tm.origin.x, yv, vp.y - tm.origin.z);
            if (collapsed)
            {
                // the whole ribbon is inside the host: a zero-width section on its edge
                sec.L = v; sec.R = v;
                sec.collapsed = true;
                sec.clippedIn = true;
            }
            else
            {
                if (clip.innerSide > 0) sec.R = v; else sec.L = v;
                sec.clippedIn = true;
            }
        }

        /// <summary>
        /// Where the host's edge line cuts this section's own cross-line:
        /// the lateral <paramref name="lam"/> (signed along <paramref name="right"/>)
        /// at which the inner vertex sits. Cutting along the cross-line
        /// rather than sliding the vertex sideways is what makes a road that
        /// meets its host at forty-five degrees end in a straight mouth on
        /// the host's edge instead of a skewed sliver; for a shallow merge
        /// the two are the same. False when nothing needs clipping.
        /// </summary>
        static bool CutAgainstHost(CityMap map, Trims trims, CityMap.Edge e, float s, Vector2 p, Vector2 right,
                                   float hw, float y, Clip clip, out float lam, out bool collapsed)
        {
            lam = 0f; collapsed = false;
            clip.host.Project(p, out var H, out float sM, out var q, out var dirM, out bool atEnd);
            if (atEnd) return false;                                // the host ran out here
            if (Mathf.Abs(H.YAt(sM) - y) > AttachDy) return false;   // climbed away: not beside the host here
            var rM = new Vector2(-dirM.y, dirM.x);
            int side = clip.side;
            float mHalf = HostHalf(map, trims, H, sM, rM, side) + ClipLift;
            float off = Vector2.Dot(p - q, rM) * side;             // our centreline, positive on our side of the host
            float denom = Vector2.Dot(right, rM) * side;           // our cross-line against the host's outward normal
            if (Mathf.Abs(denom) < 0.3f) return false;             // near-perpendicular: no branch geometry here
            lam = (mHalf - off) / denom;                           // the cut, signed along right
            // our inner vertex is at -hw (L) when denom > 0, +hw (R) when denom < 0
            float innerLam = denom > 0f ? -hw : hw;
            float outerLam = -innerLam;
            bool innerInside = denom > 0f ? lam > innerLam : lam < innerLam;
            if (!innerInside) return false;
            bool outerInside = denom > 0f ? lam >= outerLam - 0.15f : lam <= outerLam + 0.15f;
            if (outerInside) { collapsed = true; lam = Mathf.Clamp(lam, -hw - 6f, hw + 6f); }
            return true;
        }

        /// <summary>The natural ground stands this far above the tarmac
        /// beside the outside edge = the road is in a cut, and the outside
        /// edge gets a retaining wall.</summary>
        public const float CutWallM = 2.0f;

        /// <summary>Is the land beside this span's outside edge well above
        /// the road? Sampled from the raw DEM a few metres out from the
        /// pavement, at mid-span — the corridor grading has pulled the
        /// lattice down to the road there, but the DEM still says what the
        /// hill was.</summary>
        static bool InCut(TileMeshes tm, Section A, Section B, Vector2 outward)
        {
            var m = (A.L + B.L) * 0.5f;
            float wx = m.x + tm.origin.x + outward.x * 4f;
            float wz = m.z + tm.origin.z + outward.y * 4f;
            return CityElevation.BaseY(wx, wz) - m.y > CutWallM;
        }

        /// <summary>A Jersey barrier along one edge of a span: inner face, top
        /// and outer face, standing on the pavement edge and reaching outward.</summary>
        static void EmitBarrier(Vector3 a, Vector3 b, Vector2 outward, float v0, float v1)
        {
            var bk = barrierBucket;
            var o = new Vector3(outward.x, 0f, outward.y) * BarrierW;
            var up = Vector3.up * BarrierH;
            bk.WallSloped(a, b, a.y, a.y + BarrierH, b.y, b.y + BarrierH, -outward, v0, v1, 0.3f, 0.45f);          // inner face
            bk.WallSloped(a + o, b + o, a.y, a.y + BarrierH, b.y, b.y + BarrierH, outward, v0, v1, 0.3f, 0.45f);  // outer face
            bk.Up(a + up, b + up, b + o + up, a + o + up,
                  new Vector2(0.45f, v0), new Vector2(0.45f, v1), new Vector2(0.5f, v1), new Vector2(0.5f, v0));
        }

        /// <summary>The two outward faces that turn a road ribbon into a slab.
        /// Both face AWAY from the road; a kerb you cannot see from the
        /// grass is a kerb the car hits without warning.</summary>
        static void EmitKerb(Bucket con, Section A, Section B, float v0, float v1, bool kerbL, bool kerbR)
        {
            // each section's own depth: the kerb reaches down through the
            // crest allowance the ground was sunk by there
            if (kerbL)
                con.WallSloped(A.L, B.L, A.L.y - A.kerb, A.L.y, B.L.y - B.kerb, B.L.y, -A.right,
                               v0, v1, 0f, 0.15f);
            if (kerbR)
                con.WallSloped(A.R, B.R, A.R.y - A.kerb, A.R.y, B.R.y - B.kerb, B.R.y, A.right,
                               v0, v1, 0f, 0.15f);
        }

        static void EmitDeckSpan(Bucket con, Section A, Section B, float v0, float v1, bool railL, bool railR)
        {
            float dk = CityElevation.DeckThick;
            var dAL = A.L + Vector3.down * dk; var dAR = A.R + Vector3.down * dk;
            var dBL = B.L + Vector3.down * dk; var dBR = B.R + Vector3.down * dk;
            // fascias face out, the soffit faces down
            con.WallSloped(A.L, B.L, dAL.y, A.L.y, dBL.y, B.L.y, -A.right, v0, v1, 0f, 0.15f);
            con.WallSloped(A.R, B.R, dAR.y, A.R.y, dBR.y, B.R.y, A.right, v0, v1, 0f, 0.15f);
            con.Down(dAR, dAL, dBL, dBR, new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1));

            // rails, inner+top+outer, both sides
            var up = Vector3.up * RailH;
            var inw = new Vector3(A.right.x, 0f, A.right.y) * RailW;
            if (railL) EmitRail(con, A.L, B.L, up, inw, -A.right, v0, v1);
            if (railR) EmitRail(con, A.R, B.R, up, -inw, A.right, v0, v1);
        }

        static void EmitRail(Bucket con, Vector3 a, Vector3 b, Vector3 up, Vector3 inw, Vector2 outward, float v0, float v1)
        {
            con.WallSloped(a + inw, b + inw, a.y, a.y + up.y, b.y, b.y + up.y, -outward, v0, v1, 0.3f, 0.45f);
            con.Up(a + inw + up, a + up, b + up, b + inw + up,
                new Vector2(0.45f, v0), new Vector2(0.5f, v0), new Vector2(0.5f, v1), new Vector2(0.45f, v1));
            con.WallSloped(a, b, a.y, a.y + up.y, b.y, b.y + up.y, outward, v0, v1, 0.3f, 0.45f);
        }

        /// <summary>
        /// A pier under a deck — unless it would stand in the road the deck
        /// crosses. A pier every 26 m landed in the carriageway below at one
        /// crossing in three; now it is nudged along the deck to the nearest
        /// clear spot, or left out.
        /// </summary>
        static readonly float[] PierNudges = { 0f, -5f, 5f, -10f, 10f, -14f, 14f };
        static void EmitPier(CityMap map, CityMap.Edge e, TileMeshes tm, float sAt)
        {
            foreach (var dS in PierNudges)
            {
                float s = sAt + dS;
                if (s < 2f || s > e.length - 2f) continue;
                var p = e.PointAt(s);
                float deckY = e.YAt(s) - CityElevation.DeckThick;
                if (PierBlocked(map, e, p, deckY)) continue;
                float gy = CityElevation.GroundY(map, p.x, p.y);
                if (deckY - gy < 2.2f) return;

                var con = buckets[(int)Slot.Concrete];
                var tan = e.TangentAt(s);
                var right = new Vector3(-tan.y, 0f, tan.x);
                var fwd = new Vector3(tan.x, 0f, tan.y);
                var c = new Vector3(p.x - tm.origin.x, 0f, p.y - tm.origin.z);
                float hw = Mathf.Max(0.7f, e.width * 0.18f);
                var bottom = c + Vector3.up * (gy - 0.6f);
                var top = c + Vector3.up * deckY;
                EmitColumn(con, bottom, top, right * hw, fwd * 0.7f);

                tm.solids.Add(new SolidBox
                {
                    center = c + Vector3.up * ((gy - 0.6f + deckY) * 0.5f),
                    size = new Vector3(hw * 2f, deckY - gy + 0.6f, 1.4f),
                    yawDeg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg,
                });
                return;
            }
        }

        static readonly HashSet<int> pierScratch = new HashSet<int>();
        /// <summary>Is there a road passing BELOW the deck within its paved
        /// width of this point? (A road at the deck's own height is the deck's
        /// neighbour, not something it crosses.)</summary>
        static bool PierBlocked(CityMap map, CityMap.Edge deck, Vector2 p, float deckY)
        {
            pierScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 30f, p + Vector2.one * 30f, pierScratch);
            foreach (var packed in pierScratch)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                if (oi == deck.index) continue;
                var o = map.edges[oi];
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float dist = Vector2.Distance(p, a + d * t);
                if (dist > o.width * 0.5f + 1.3f) continue;
                float at = o.s[si] + Mathf.Sqrt(L2) * t;
                if (o.YAt(at) < deckY - 1.2f) return true;
            }
            return false;
        }

        static void EmitColumn(Bucket bk, Vector3 bottom, Vector3 top, Vector3 half1, Vector3 half2)
        {
            var b1 = bottom + half1 + half2; var b2 = bottom + half1 - half2;
            var b3 = bottom - half1 - half2; var b4 = bottom - half1 + half2;
            var t1 = top + half1 + half2; var t2 = top + half1 - half2;
            var t3 = top - half1 - half2; var t4 = top - half1 + half2;
            float vh = (top.y - bottom.y) / 6f;
            bk.Quad(b1, t1, t2, b2, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b2, t2, t3, b3, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b3, t3, t4, b4, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b4, t4, t1, b1, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The fan at a junction node: one triangle per pair of adjacent
        /// arm corners, from the node centre. Corners sit at each arm's own
        /// trim AT THAT ARM'S HEIGHT — the fan is the surface between the
        /// arms, not a plate laid over them — and the centre at the node's.
        /// Skirted on the gaps between arms, never across a road mouth.
        /// </summary>
        static void BuildJunctions(CityMap map, Trims trims, TileMeshes tm,
                                   Vector2 min, Vector2 max)
        {
            var con = buckets[(int)Slot.Concrete];
            var corners = new List<(float ang, Vector3 pos, int edge)>(12);

            segScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * 4f, max + Vector2.one * 4f, segScratch);
            var nodesHere = new HashSet<int>();
            foreach (var packed in segScratch)
            {
                var e = map.edges[packed >> 12];
                nodesHere.Add(e.a); nodesHere.Add(e.b);
            }

            foreach (var n in nodesHere)
            {
                if (!trims.patch[n]) continue;
                var np = map.nodes[n];
                if (np.x < min.x || np.x >= max.x || np.y < min.y || np.y >= max.y) continue;

                // Intersections are resurfaced on their own schedule, so a
                // junction takes its age from the NODE rather than inheriting
                // one of its arms'.
                var bk = buckets[(int)SlotOf(JunctionProfile,
                    IsFresh(np) ? Surface.AsphaltNew : Surface.AsphaltOld)];

                const float proud = 0.012f;   // a hair above the arm ends, against z-fighting
                corners.Clear();
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    float trim = trims.TrimAt(e, n);
                    float at = e.a == n ? trim : e.length - trim;
                    var p = e.PointAt(at);
                    var right = RightAt(map, trims, e, at, out float widen);
                    float hw = trims.HalfWidthAt(e, at) * widen;
                    float y = e.YAt(at) + proud;
                    var c1 = p - right * hw; var c2 = p + right * hw;
                    corners.Add((Mathf.Atan2(c1.y - np.y, c1.x - np.x),
                        new Vector3(c1.x - tm.origin.x, y, c1.y - tm.origin.z), ei));
                    corners.Add((Mathf.Atan2(c2.y - np.y, c2.x - np.x),
                        new Vector3(c2.x - tm.origin.x, y, c2.y - tm.origin.z), ei));
                }
                if (corners.Count < 3) continue;
                corners.Sort((a, b) => a.ang.CompareTo(b.ang));

                int centerI = bk.v.Count;
                bk.v.Add(new Vector3(np.x - tm.origin.x, map.nodeY[n] + proud, np.y - tm.origin.z));
                bk.uv.Add(new Vector2(np.x / 12f, np.y / 12f));
                for (int i = 0; i < corners.Count; i++)
                {
                    bk.v.Add(corners[i].pos);
                    bk.uv.Add(new Vector2((corners[i].pos.x + tm.origin.x) / 12f,
                                          (corners[i].pos.z + tm.origin.z) / 12f));
                }
                for (int i = 0; i < corners.Count; i++)
                {
                    int aI = centerI + 1 + i;
                    int bI = centerI + 1 + (i + 1) % corners.Count;
                    bk.t.Add(centerI); bk.t.Add(bI); bk.t.Add(aI);
                }
                tm.patchCount++;

                // Skirt the perimeter, minus the road mouths. The two corners
                // of one arm sort adjacent, so a same-edge pair IS the mouth;
                // the pairs BETWEEN arms are the ones facing open ground.
                for (int i = 0; i < corners.Count; i++)
                {
                    var k0 = corners[i];
                    var k1 = corners[(i + 1) % corners.Count];
                    if (k0.edge == k1.edge) continue;          // road mouth
                    var midOut = new Vector2((k0.pos.x + k1.pos.x) * 0.5f + tm.origin.x - np.x,
                                             (k0.pos.z + k1.pos.z) * 0.5f + tm.origin.z - np.y);
                    if (midOut.sqrMagnitude < 1e-4f) continue;
                    // through the junction's own crest allowance, like a ribbon's kerb
                    float kd = KerbDepth + (map.nodeCrest != null ? map.nodeCrest[n] : 0f);
                    con.WallSloped(k0.pos, k1.pos, k0.pos.y - kd, k0.pos.y, k1.pos.y - kd, k1.pos.y,
                                   midOut, 0f, 0.6f, 0f, 0.15f);
                }
            }
        }

        // ------------------------------------------------------------------
        static void BuildWater(CityMap map, TileMeshes tm, Vector2 min, Vector2 max)
        {
            var bk = buckets[(int)Slot.Water];

            foreach (var w in map.waters)
            {
                if (w.bbMax.x < min.x - 60f || w.bbMin.x > max.x + 60f ||
                    w.bbMax.y < min.y - 60f || w.bbMin.y > max.y + 60f) continue;

                if (!w.lake)
                {
                    float hw = w.width * 0.5f;
                    for (int i = 0; i + 1 < w.pts.Length; i++)
                    {
                        var mid = (w.pts[i] + w.pts[i + 1]) * 0.5f;
                        if (mid.x < min.x || mid.x >= max.x || mid.y < min.y || mid.y >= max.y) continue;
                        var d = (w.pts[i + 1] - w.pts[i]);
                        float len = d.magnitude;
                        if (len < 0.01f) continue;
                        d /= len;
                        var right = new Vector2(-d.y, d.x) * hw;
                        var a = w.pts[i]; var b = w.pts[i + 1];
                        float ya = CityElevation.RiverSurfaceY(a.x, a.y);
                        float yb = CityElevation.RiverSurfaceY(b.x, b.y);
                        bk.Quad(
                            new Vector3(a.x - right.x - tm.origin.x, ya, a.y - right.y - tm.origin.z),
                            new Vector3(b.x - right.x - tm.origin.x, yb, b.y - right.y - tm.origin.z),
                            new Vector3(b.x + right.x - tm.origin.x, yb, b.y + right.y - tm.origin.z),
                            new Vector3(a.x + right.x - tm.origin.x, ya, a.y + right.y - tm.origin.z),
                            new Vector2(0f, i / 3f), new Vector2(0f, (i + 1) / 3f),
                            new Vector2(1f, (i + 1) / 3f), new Vector2(1f, i / 3f));
                    }
                }
                else
                {
                    polyScratch.Clear();
                    polyScratch.AddRange(w.pts);
                    ClipPoly(polyScratch, min, max);
                    if (polyScratch.Count >= 3)
                        EarcutInto(bk, polyScratch, w.surfaceY, tm.origin, 26f);
                }
            }
        }

        static void ClipPoly(List<Vector2> poly, Vector2 min, Vector2 max)
        {
            ClipHalf(poly, p => p.x >= min.x, (a, b) => LerpX(a, b, min.x));
            ClipHalf(poly, p => p.x <= max.x, (a, b) => LerpX(a, b, max.x));
            ClipHalf(poly, p => p.y >= min.y, (a, b) => LerpY(a, b, min.y));
            ClipHalf(poly, p => p.y <= max.y, (a, b) => LerpY(a, b, max.y));
        }
        static Vector2 LerpX(Vector2 a, Vector2 b, float x) =>
            Vector2.Lerp(a, b, Mathf.Abs(b.x - a.x) < 1e-6f ? 0f : (x - a.x) / (b.x - a.x));
        static Vector2 LerpY(Vector2 a, Vector2 b, float y) =>
            Vector2.Lerp(a, b, Mathf.Abs(b.y - a.y) < 1e-6f ? 0f : (y - a.y) / (b.y - a.y));

        static readonly List<Vector2> clipScratch = new List<Vector2>();
        static void ClipHalf(List<Vector2> poly, System.Func<Vector2, bool> inside,
                             System.Func<Vector2, Vector2, Vector2> cross)
        {
            clipScratch.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var cur = poly[i];
                var prev = poly[(i + poly.Count - 1) % poly.Count];
                bool cIn = inside(cur), pIn = inside(prev);
                if (cIn)
                {
                    if (!pIn) clipScratch.Add(cross(prev, cur));
                    clipScratch.Add(cur);
                }
                else if (pIn) clipScratch.Add(cross(prev, cur));
            }
            poly.Clear();
            poly.AddRange(clipScratch);
        }

        /// <summary>Simple ear clipping, faces UP. Fine for the clipped lake
        /// pieces and for building roofs, which are small simple polygons.</summary>
        static void EarcutInto(Bucket bk, List<Vector2> poly, float y, Vector3 origin, float uvMeters)
        {
            var idx = new List<int>(poly.Count);
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                area += a.x * b.y - b.x * a.y;
            }
            bool ccw = area > 0f;
            for (int i = 0; i < poly.Count; i++) idx.Add(ccw ? i : poly.Count - 1 - i);

            int baseI = bk.v.Count;
            foreach (var p in poly)
            {
                bk.v.Add(new Vector3(p.x - origin.x, y, p.y - origin.z));
                bk.uv.Add(new Vector2(p.x / uvMeters, p.y / uvMeters));
            }

            int guard = poly.Count * poly.Count + 16;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % idx.Count];
                    var a = poly[i0]; var b = poly[i1]; var c = poly[i2];
                    if ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y) <= 1e-7f) continue;
                    bool holds = true;
                    foreach (var j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTri(a, b, c, poly[j])) { holds = false; break; }
                    }
                    if (!holds) continue;
                    bk.t.Add(baseI + i0); bk.t.Add(baseI + i2); bk.t.Add(baseI + i1);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3)
            {
                bk.t.Add(baseI + idx[0]); bk.t.Add(baseI + idx[2]); bk.t.Add(baseI + idx[1]);
            }
        }

        static bool InTri(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ------------------------------------------------------------------
        //  Procedural frontage boxes (outside the footprint data) — CityWorld
        //  instantiates the prefab lots itself.
        // ------------------------------------------------------------------
        static void BuildBuildings(CityMap map, Dictionary<long, List<CityBuildings.B>> buildings,
                                   TileMeshes tm, int tx, int tz)
        {
            long key = ((long)tx << 24) ^ (tz & 0xFFFFFF);
            if (buildings == null || !buildings.TryGetValue(key, out var list)) return;

            foreach (var b in list)
            {
                if (b.kind != 0) continue;

                float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                Vector2 fwd = new Vector2(sy, cy);           // faces the road
                Vector2 rgt = new Vector2(cy, -sy);
                Vector2 hw = rgt * (b.w * 0.5f);
                Vector2 hd = fwd * (b.d * 0.5f);

                var c1 = b.pos + hw + hd; var c2 = b.pos - hw + hd;
                var c3 = b.pos - hw - hd; var c4 = b.pos + hw - hd;
                float g = Mathf.Min(
                    Mathf.Min(CityElevation.GroundY(map, c1.x, c1.y), CityElevation.GroundY(map, c2.x, c2.y)),
                    Mathf.Min(CityElevation.GroundY(map, c3.x, c3.y), CityElevation.GroundY(map, c4.x, c4.y)));
                float y0 = g - BuildingSink;
                float y1 = y0 + b.h + BuildingSink;

                if (b.gable)
                {
                    // a suburban house: the long side faces the road
                    EmitGableHouse(tm, b.pos, rgt, b.w * 0.5f, b.d * 0.5f, y0, y0 + b.h * 0.7f, y1, Slot.FacadeHouse);
                    continue;
                }

                // Style 0 on a tall box is glass — a forty-storey slab wearing
                // a photographed brick office reads as a painted block.
                Slot wallSlot = b.style == 0 ? (b.h > 40f ? Slot.FacadeGlass : Slot.FacadeTower)
                              : b.style == 1 ? Slot.FacadeMid : Slot.FacadeBrick;
                bool shopFront = b.style == 3 && b.h > ShopFloorH + 1.5f;
                // walls: front (facing road), right, back, left — outward normals
                EmitWallStyled(tm, c2, c1, y0, y1, fwd, b.style == 3, shopFront, wallSlot);
                EmitWallStyled(tm, c1, c4, y0, y1, rgt, b.style == 3, false, wallSlot);
                EmitWallStyled(tm, c4, c3, y0, y1, -fwd, b.style == 3, false, wallSlot);
                EmitWallStyled(tm, c3, c2, y0, y1, -rgt, b.style == 3, false, wallSlot);

                var roof = buckets[(int)Slot.RoofFlat];
                roof.Up(L(c2, y1, tm), L(c3, y1, tm), L(c4, y1, tm), L(c1, y1, tm),
                    new Vector2(c2.x / RoofFlatM, c2.y / RoofFlatM), new Vector2(c3.x / RoofFlatM, c3.y / RoofFlatM),
                    new Vector2(c4.x / RoofFlatM, c4.y / RoofFlatM), new Vector2(c1.x / RoofFlatM, c1.y / RoofFlatM));

            }
        }

        static Vector3 L(Vector2 p, float y, TileMeshes tm) =>
            new Vector3(p.x - tm.origin.x, y, p.y - tm.origin.z);

        /// <summary>One wall of a retail-or-not box: a shopfront on the
        /// ground floor where asked, brick above and elsewhere.</summary>
        static void EmitWallStyled(TileMeshes tm, Vector2 a, Vector2 c, float y0, float y1,
                                   Vector2 outward, bool retail, bool shopFront, Slot wallSlot)
        {
            float wallW = Vector2.Distance(a, c);
            if (retail)
            {
                float split = Mathf.Min(y0 + BuildingSink + ShopFloorH, y1);
                if (shopFront)
                {
                    float reps = Mathf.Max(1f, Mathf.Round(wallW / FacadeMeters[3].x));
                    EmitPanels(tm, Slot.Shops, a, c, y0 + BuildingSink, split, outward, reps, 1f);
                }
                else EmitFacadeQuad(tm, Slot.FacadeMid, a, c, y0, split, outward);
                if (y1 > split + 0.2f) EmitFacadeQuad(tm, Slot.FacadeMid, a, c, split, y1, outward);
                return;
            }
            EmitFacadeQuad(tm, wallSlot, a, c, y0, y1, outward);
        }

        static void EmitFacadeQuad(TileMeshes tm, Slot style, Vector2 a, Vector2 c,
                                   float y0, float y1, Vector2 outward)
        {
            var fm = FacadeMeters[(int)style - (int)Slot.FacadeTower];
            float wallW = Vector2.Distance(a, c);
            float u = Mathf.Max(1f, Mathf.Round(wallW / fm.x));
            float v = Mathf.Max(1f, Mathf.Round((y1 - y0) / fm.y));
            EmitPanels(tm, style, a, c, y0, y1, outward, u, v);
        }

        static int PanelCount(float meters) =>
            Mathf.Clamp(Mathf.CeilToInt(meters / FacadePanelMax), 1, meters > TallWallM ? TallPanelCap : FacadePanelCap);

        /// <summary>
        /// One wall, facing <paramref name="outward"/>, subdivided into
        /// affine-sized panels with the UV repeats handed out ACROSS the panels.
        /// The emitter checks its own work: if the first panel's normal points
        /// against the outward direction it was given, the tile counts a
        /// facing error and the audit fails the build. That is the whole
        /// history of the inside-out buildings, made into an assertion.
        /// </summary>
        static void EmitPanels(TileMeshes tm, Slot slot, Vector2 a, Vector2 c,
                               float y0, float y1, Vector2 outward, float uReps, float vReps)
        {
            var bk = buckets[(int)slot];
            int nx = PanelCount(Vector2.Distance(a, c));
            int ny = PanelCount(y1 - y0);
            for (int j = 0; j < ny; j++)
            {
                float t0 = (float)j / ny, t1 = (float)(j + 1) / ny;
                float ya = Mathf.Lerp(y0, y1, t0), yb = Mathf.Lerp(y0, y1, t1);
                for (int i = 0; i < nx; i++)
                {
                    float s0 = (float)i / nx, s1 = (float)(i + 1) / nx;
                    Vector2 pa = Vector2.Lerp(a, c, s0), pc = Vector2.Lerp(a, c, s1);
                    bk.Wall(L(pa, 0f, tm), L(pc, 0f, tm), ya, yb, outward,
                            uReps * s0, uReps * s1, vReps * t0, vReps * t1);
                    if (i == 0 && j == 0)
                    {
                        var n = bk.LastNormal();
                        if (Vector3.Dot(n, new Vector3(outward.x, 0f, outward.y)) < 0f) tm.wallFacingErrors++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        //  Real buildings from OSM footprints
        // ------------------------------------------------------------------
        static readonly List<Vector2> roofScratch = new List<Vector2>(32);

        static void BuildFootprints(CityMap map, TileMeshes tm, int tx, int tz)
        {
            var list = map.FootprintsInTile(tx, tz);
            if (list == null) return;
            foreach (var fi in list)
            {
                var f = map.footprints[fi];
                if (f.propKind != 0) continue;   // a model stands here; CityWorld places it
                float g = float.MaxValue;
                foreach (var p in f.pts) g = Mathf.Min(g, CityElevation.GroundY(map, p.x, p.y));
                float y0 = g - BuildingSink;
                float top = y0 + BuildingSink + f.h;

                if (f.gable)
                {
                    float eave = y0 + BuildingSink + f.h * 0.68f;
                    EmitGableHouse(tm, f.centre, f.u, f.hu, f.hv, y0, eave, top, Slot.FacadeHouse);
                    tm.footprintCount++;
                    continue;
                }

                Slot wallSlot = f.style == 0 ? Slot.FacadeGlass
                              : f.style == 1 ? Slot.FacadeTower
                              : f.style == 3 ? Slot.FacadeHouse
                              : Slot.FacadeMid;
                // A shopfront goes on the wall that faces the nearest street.
                int frontWall = -1;
                if (f.style == 4 && f.h > ShopFloorH + 1.5f &&
                    map.NearestRoadPoint(f.centre, 70f, skipLinks: true, out int rei, out float rs, out _))
                {
                    var q = map.edges[rei].PointAt(rs);
                    var toRoad = (q - f.centre).normalized;
                    float bestDot = 0.5f;
                    for (int i = 0; i < f.pts.Length; i++)
                    {
                        var d = f.pts[(i + 1) % f.pts.Length] - f.pts[i];
                        if (d.sqrMagnitude < 4f) continue;
                        var n = new Vector2(d.y, -d.x).normalized;   // outward, for a CCW polygon
                        float dt = Vector2.Dot(n, toRoad);
                        if (dt > bestDot) { bestDot = dt; frontWall = i; }
                    }
                }
                int n0 = f.pts.Length;
                for (int i = 0; i < n0; i++)
                {
                    var a = f.pts[i]; var c = f.pts[(i + 1) % n0];
                    var d = c - a;
                    if (d.sqrMagnitude < 0.04f) continue;
                    var outward = new Vector2(d.y, -d.x).normalized;
                    EmitWallStyled(tm, a, c, y0, top, outward, f.style == 4, i == frontWall, wallSlot);
                }

                roofScratch.Clear();
                roofScratch.AddRange(f.pts);
                EarcutInto(buckets[(int)Slot.RoofFlat], roofScratch, top, tm.origin, RoofFlatM);

                // A tall tower gets a crown: a smaller prism on top, then a
                // smaller one still — enough silhouette to tell the Bank of
                // America Corporate Center from a box, at a distance, in fog.
                if (f.h > 120f) EmitCrown(map, tm, f, top, wallSlot);

                tm.footprintCount++;
            }
        }

        static void EmitCrown(CityMap map, TileMeshes tm, CityMap.Footprint f, float top, Slot slot)
        {
            float hu = f.hu, hv = f.hv;
            var v = new Vector2(-f.u.y, f.u.x);
            float y = top;
            for (int step = 0; step < 2; step++)
            {
                hu *= 0.62f; hv *= 0.62f;
                float h = f.h * (step == 0 ? 0.09f : 0.07f);
                var c1 = f.centre + f.u * hu + v * hv; var c2 = f.centre - f.u * hu + v * hv;
                var c3 = f.centre - f.u * hu - v * hv; var c4 = f.centre + f.u * hu - v * hv;
                EmitFacadeQuad(tm, slot, c2, c1, y, y + h, v);
                EmitFacadeQuad(tm, slot, c1, c4, y, y + h, f.u);
                EmitFacadeQuad(tm, slot, c4, c3, y, y + h, -v);
                EmitFacadeQuad(tm, slot, c3, c2, y, y + h, -f.u);
                var roof = buckets[(int)Slot.RoofFlat];
                roof.Up(L(c2, y + h, tm), L(c3, y + h, tm), L(c4, y + h, tm), L(c1, y + h, tm),
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                y += h;
            }
        }

        /// <summary>
        /// A gabled house on an oriented box: four walls to the eave, two roof
        /// planes to a ridge along the long axis, a gable triangle at each end.
        /// The same routine builds a real footprint's house and the interior
        /// fill's, so they cannot look like two kinds of house.
        /// </summary>
        static void EmitGableHouse(TileMeshes tm, Vector2 centre, Vector2 u, float hu, float hv,
                                   float y0, float eave, float ridge, Slot wallSlot)
        {
            var v = new Vector2(-u.y, u.x);
            var c1 = centre + u * hu + v * hv;   // +u +v
            var c2 = centre - u * hu + v * hv;   // -u +v
            var c3 = centre - u * hu - v * hv;   // -u -v
            var c4 = centre + u * hu - v * hv;   // +u -v
            EmitFacadeQuad(tm, wallSlot, c2, c1, y0, eave, v);
            EmitFacadeQuad(tm, wallSlot, c1, c4, y0, eave, u);
            EmitFacadeQuad(tm, wallSlot, c4, c3, y0, eave, -v);
            EmitFacadeQuad(tm, wallSlot, c3, c2, y0, eave, -u);

            var rN = centre + u * hu;   // ridge ends
            var rS = centre - u * hu;
            var roof = buckets[(int)Slot.RoofTiles];
            float uRep = Mathf.Max(1f, Mathf.Round(hu * 2f / RoofTileM));
            float vRep = Mathf.Max(1f, Mathf.Round(Mathf.Sqrt(hv * hv + (ridge - eave) * (ridge - eave)) / RoofTileM));
            // +v plane and -v plane, both facing up (Up() settles the order)
            roof.Up(L(rS, ridge, tm), L(rN, ridge, tm), L(c1, eave, tm), L(c2, eave, tm),
                    new Vector2(0f, 0f), new Vector2(uRep, 0f), new Vector2(uRep, vRep), new Vector2(0f, vRep));
            roof.Up(L(c3, eave, tm), L(c4, eave, tm), L(rN, ridge, tm), L(rS, ridge, tm),
                    new Vector2(0f, vRep), new Vector2(uRep, vRep), new Vector2(uRep, 0f), new Vector2(0f, 0f));

            // gable ends: vertical triangles facing +u and -u
            var wall = buckets[(int)wallSlot];
            EmitGableTri(wall, tm, c1, rN, c4, eave, ridge, u);
            EmitGableTri(wall, tm, c3, rS, c2, eave, ridge, -u);

        }

        static void EmitGableTri(Bucket bk, TileMeshes tm, Vector2 a, Vector2 apex, Vector2 c,
                                 float eave, float ridge, Vector2 outward)
        {
            // a triangle from a to c faces the LEFT of a->c, like a wall
            Vector2 d = c - a;
            Vector2 left = new Vector2(-d.y, d.x);
            if (Vector2.Dot(left, outward) < 0f) { var t = a; a = c; c = t; }
            var fm = FacadeMeters[(int)Slot.FacadeHouse - (int)Slot.FacadeTower];
            float uRep = Mathf.Max(1f, Mathf.Round(Vector2.Distance(a, c) / fm.x));
            bk.Tri(L(a, eave, tm), L(apex, ridge, tm), L(c, eave, tm),
                   new Vector2(0f, 0f), new Vector2(uRep * 0.5f, (ridge - eave) / fm.y), new Vector2(uRep, 0f));
        }

        // ------------------------------------------------------------------
        //  Interior fill: houses between the arterials, where OSM gave us
        //  roads but no footprints. A deterministic grid per tile, aligned to
        //  the nearest street, thinning with distance from uptown and from the
        //  road. Cheap gable boxes in the tile mesh — one draw call for a
        //  whole subdivision.
        // ------------------------------------------------------------------
        const float HouseCell = 24f;

        static void BuildHouses(CityMap map, TileMeshes tm, int tx, int tz)
        {
            var min = new Vector2(tx * TileSize, tz * TileSize);
            int cells = Mathf.RoundToInt(TileSize / HouseCell);
            for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    int gx = tx * cells + cx, gz = tz * cells + cz;
                    var c = new Vector2(min.x + (cx + 0.5f) * HouseCell + (Hash01(gx, gz, 1) - 0.5f) * 9f,
                                        min.y + (cz + 0.5f) * HouseCell + (Hash01(gx, gz, 2) - 0.5f) * 9f);
                    if (map.footprintBounds.Contains(c)) continue;

                    // nearest street, and the corridor test against every road
                    segScratch.Clear();
                    map.EdgeSegsInRect(c - Vector2.one * 260f, c + Vector2.one * 260f, segScratch);
                    float dRoad = float.MaxValue; Vector2 tanRoad = Vector2.right;
                    bool blocked = false;
                    foreach (var packed in segScratch)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        var e = map.edges[ei];
                        Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(c - a, d) / L2) : 0f;
                        float dist = Vector2.Distance(c, a + d * t);
                        if (dist < e.CorridorHalf + 24f) { blocked = true; break; }
                        if (!e.link && e.cls < 5 && dist < dRoad) { dRoad = dist; tanRoad = d.normalized; }
                    }
                    if (blocked || dRoad > 250f) continue;

                    float distUp = Vector2.Distance(c, map.uptown);
                    float keep = distUp < 7000f ? 0.62f : distUp < 12000f ? 0.42f : distUp < 16000f ? 0.22f : 0.07f;
                    keep *= Mathf.Lerp(1f, 0.3f, Mathf.Clamp01((dRoad - 110f) / 140f));
                    if (Hash01(gx, gz, 3) > keep) continue;

                    segScratch.Clear();
                    map.WaterSegsInRect(c - Vector2.one * 30f, c + Vector2.one * 30f, segScratch);
                    if (segScratch.Count > 0) continue;

                    float hu = 4.6f + Hash01(gx, gz, 4) * 2.2f;   // half length, along the street
                    float hv = 3.8f + Hash01(gx, gz, 5) * 1.6f;   // half depth
                    float eaveH = 3.0f + Hash01(gx, gz, 6) * 0.6f;
                    float riseH = 1.7f + Hash01(gx, gz, 7) * 0.9f;
                    var u = tanRoad;
                    var v = new Vector2(-u.y, u.x);
                    float g = float.MaxValue;
                    foreach (var corner in new[] { c + u * hu + v * hv, c - u * hu + v * hv, c - u * hu - v * hv, c + u * hu - v * hv })
                        g = Mathf.Min(g, CityElevation.GroundY(map, corner.x, corner.y));
                    float y0 = g - BuildingSink;
                    EmitGableHouse(tm, c, u, hu, hv, y0, y0 + BuildingSink + eaveH, y0 + BuildingSink + eaveH + riseH, Slot.FacadeHouse);
                    tm.houseCount++;
                }
        }

        static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2246822519) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
