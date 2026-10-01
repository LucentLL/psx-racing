using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// WHAT IS ALREADY BESIDE THE ROAD, per tile (plan WP-08, section 2.3
    /// "Placement"): a 2 m bitmask saying which ground is taken, built once
    /// per tile build, which every roadside object asks before it stands.
    /// Trees are the first to ask; poles, lamps' successors and signs (WP-15,
    /// WP-23) take their places from it before trees in later packages, in the
    /// plan's priority: junction furniture, poles, lamps, signs, trees.
    ///
    /// It reserves:
    ///   * PAVEMENT and the CLEAR ZONE past its edge (freeway 9 m, arterial
    ///     3.5 m, collector 2.5 m, local 2 m; a ramp 4.5 m): the DOT roadside
    ///     rule - nothing a car leaving the road at speed would hit; and along
    ///     the city race routes their RUN-OFF (<see cref="RaceRunOff"/>);
    ///   * every junction's fan and, at each corner of a real junction, the
    ///     SIGHT TRIANGLE (10 m legs along the two kerb lines from where they
    ///     meet) and a FURNITURE SPOT just behind the corner, whether the
    ///     junction is signalled or not, so a signal or a STOP sign never has
    ///     to push a tree aside (WP-19 replaces the triangles with exact
    ///     per-corner ones);
    ///   * real footprints, the procedural lots and prop lots, the fill
    ///     houses, creeks and lakes, the lamp posts' feet, and the ground
    ///     under every deck.
    /// Every mark is widened by half a cell's diagonal, so a point anywhere
    /// in a free cell is itself clear of what the mark stands for.
    ///
    /// THE ROAD'S EDGES ARE READ IN ONE PLACE, <see cref="RoadEdgeAt"/>: the
    /// centreline and the drawn half width at an arc position. The lines
    /// release (R4) replaces the line model; it replaces that one function,
    /// and everything placed from this mask re-seats with the lines.
    /// </summary>
    public sealed class RoadsideOccupancy
    {
        public const float CellM = 2f;
        public const int Res = 128;   // CityMeshes.TileSize / CellM
        /// <summary>Half a cell's diagonal: every mark is widened by it.</summary>
        public const float CellPadM = 1.4143f;

        public const byte Pavement = 1, Clear = 2, Sight = 4, Corner = 8, Building = 16, Water = 32, Deck = 64, Other = 128;
        public static readonly string[] BitNames = { "pavement", "clear zone", "sight triangle", "corner spot", "building", "water", "under a deck", "lot, lamp or sign" };

        /// <summary>Sight triangle legs along each kerb line, metres.</summary>
        public const float SightLegM = 10f;
        /// <summary>The furniture spot's radius and how far behind the corner
        /// (past the clear zone) it stands.</summary>
        public const float CornerSpotR = 1.5f, CornerSpotBackM = 1.0f;
        /// <summary>How far past a deck's edge the ground under it is kept.</summary>
        public const float DeckMarginM = 1.0f;
        /// <summary>A lamp post's foot, kept clear.</summary>
        public const float LampFootR = 1.2f;
        /// <summary>Creek water plus this either side.</summary>
        public const float WaterMarginM = 1.0f;
        /// <summary>Buildings plus this all round.</summary>
        public const float BuildingMarginM = 0.6f;
        /// <summary>How far round the tile roads are gathered: past the widest
        /// half width plus the widest clear zone plus <see cref="NearReachM"/>.</summary>
        public const float NearReachM = 25f;
        const float GatherM = 50f;
        /// <summary>Road pieces are cut at most this long.</summary>
        const float PieceM = 8f;
        const float BucketM = 16f;

        public readonly int tx, tz;
        public readonly Vector2 min;
        public readonly byte[] bits;

        /// <summary>A stretch of carriageway: plan ends, drawn half width,
        /// clear zone, and whether it is on structure (a deck).</summary>
        public struct Piece { public Vector2 a, b; public float sa, sb, hw, clear, y; public bool deck; public int edge; }
        public readonly List<Piece> pieces;
        /// <summary>The creeks round the tile (plan ends, half the drawn width).</summary>
        public readonly List<(Vector2 a, Vector2 b, float hw)> creeks;
        // pieces bucketed on a 16 m grid over the tile and its GatherM margin
        readonly List<int>[] buckets;
        readonly int bn;
        readonly Vector2 bmin;

        readonly CityMap map;

        RoadsideOccupancy(CityMap map, int tx, int tz)
        {
            this.map = map;
            this.tx = tx; this.tz = tz;
            min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
            bmin = min - Vector2.one * GatherM;
            bn = Mathf.CeilToInt((CityMeshes.TileSize + 2f * GatherM) / BucketM);
            buckets = new List<int>[bn * bn];
            bits = new byte[Res * Res];
            pieces = new List<Piece>(256);
            creeks = new List<(Vector2, Vector2, float)>();
        }

        /// <summary>A tile's own mask on the static one: the bits copied, the
        /// road pieces, creeks and buckets shared (nothing changes them after
        /// the build).</summary>
        RoadsideOccupancy(RoadsideOccupancy src)
        {
            map = src.map;
            tx = src.tx; tz = src.tz;
            min = src.min; bmin = src.bmin; bn = src.bn;
            buckets = src.buckets;
            pieces = src.pieces;
            creeks = src.creeks;
            bits = (byte[])src.bits.Clone();
        }

        // ------------------------------------------------------------------
        //  THE STATIC MASK: everything but the tile build's own fill houses
        //  and lamp feet, from global data only (the graph, the trims, the
        //  footprints and lots, the water, the race routes). The signs that
        //  cross a tile seam (gantries, billboards: CitySigns) are decided on
        //  it, so every tile they reach finds the same ones; a tile's full
        //  mask is its static mask plus its fill houses and lamps. Cached for
        //  the few tiles round the one building (the neighbours' are asked
        //  for, then built themselves).
        // ------------------------------------------------------------------

        public const int StaticCacheSize = 32;
        static readonly Dictionary<long, RoadsideOccupancy> staticCache = new Dictionary<long, RoadsideOccupancy>();
        static readonly Queue<long> staticOrder = new Queue<long>();
        static CityMap staticMap;
        static CityMeshes.Trims staticTrims;
        static Dictionary<long, List<CityBuildings.B>> staticBuildings;

        /// <summary>The static mask of a tile (shared: never mark it).</summary>
        public static RoadsideOccupancy Static(CityMap map, CityMeshes.Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            if (staticMap != map || staticTrims != trims || staticBuildings != buildings)
            {
                staticCache.Clear(); staticOrder.Clear(); lastStatic = null;
                staticMap = map; staticTrims = trims; staticBuildings = buildings;
            }
            long key = ((long)tx << 24) ^ (tz & 0xFFFFFF);
            if (staticCache.TryGetValue(key, out var o)) return o;
            o = BuildStatic(map, trims, buildings, tx, tz);
            staticCache[key] = o;
            staticOrder.Enqueue(key);
            while (staticOrder.Count > StaticCacheSize) staticCache.Remove(staticOrder.Dequeue());
            return o;
        }

        /// <summary>The static mask's bits at a world point, whatever tile it is in.</summary>
        public static byte StaticAt(CityMap map, CityMeshes.Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, Vector2 p)
        {
            int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.y / CityMeshes.TileSize);
            if (lastStatic == null || lastStatic.tx != tx || lastStatic.tz != tz || staticMap != map || staticTrims != trims || staticBuildings != buildings)
                lastStatic = Static(map, trims, buildings, tx, tz);
            return lastStatic.At(p);
        }
        static RoadsideOccupancy lastStatic;

        // ------------------------------------------------------------------
        //  Queries
        // ------------------------------------------------------------------

        /// <summary>The bits of the cell a world point is in (Other outside
        /// the tile: a tile never places on another's ground).</summary>
        public byte At(Vector2 p)
        {
            int cx = Mathf.FloorToInt((p.x - min.x) / CellM), cz = Mathf.FloorToInt((p.y - min.y) / CellM);
            if (cx < 0 || cz < 0 || cx >= Res || cz >= Res) return Other;
            return bits[cz * Res + cx];
        }

        /// <summary>Metres from a point to the nearest GROUNDED carriageway's
        /// drawn edge (negative on the pavement), looked for out to
        /// <see cref="NearReachM"/>; past that, NearReachM + 1. Also the
        /// road's height there and its edge index.</summary>
        public float RoadEdgeDistance(Vector2 p, out float roadY, out int edge) => RoadEdgeDistance(p, out roadY, out edge, out _);

        /// <summary>As above, and the direction that stretch of road runs.</summary>
        public float RoadEdgeDistance(Vector2 p, out float roadY, out int edge, out Vector2 dir) =>
            RoadEdgeDistance(p, NearReachM, out roadY, out edge, out dir);

        /// <summary>As above, looked for only out to <paramref name="reach"/>
        /// (at most <see cref="NearReachM"/>): exact within it; past it, only
        /// "farther than reach" (the nearest the scan saw, or NearReachM + 1).</summary>
        public float RoadEdgeDistance(Vector2 p, float reach, out float roadY, out int edge, out Vector2 dir)
        {
            roadY = 0f; edge = -1; dir = Vector2.right;
            reach = Mathf.Min(reach, NearReachM);
            float best = NearReachM + 1f;
            // a piece is in every bucket its capsule touches, so the buckets
            // within the reach of the point are enough; each piece once
            int stamp = NextStamp();
            BucketRange(p, reach, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || z < 0 || x >= bn || z >= bn) continue;
                    var l = buckets[z * bn + x];
                    if (l == null) continue;
                    foreach (int i in l)
                    {
                        if (seenAt[i] == stamp) continue;
                        seenAt[i] = stamp;
                        var pc = pieces[i];
                        if (pc.deck) continue;
                        float d = DistToSeg(p, pc.a, pc.b) - pc.hw;
                        if (d < best) { best = d; roadY = pc.y; edge = pc.edge; dir = pc.b - pc.a; }
                    }
                }
            if (dir.sqrMagnitude > 1e-6f) dir.Normalize(); else dir = Vector2.right;
            return best;
        }

        /// <summary>Every carriageway (decks included) whose drawn edge is within
        /// <paramref name="r"/> of a point: how far off its edge the point is,
        /// the unit direction from the road to the point, and its height.
        /// One entry per piece.</summary>
        public void NearRoads(Vector2 p, float r, List<NearRoad> outList)
        {
            int stamp = NextStamp();
            BucketRange(p, r, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || z < 0 || x >= bn || z >= bn) continue;
                    var l = buckets[z * bn + x];
                    if (l == null) continue;
                    foreach (int i in l)
                    {
                        if (seenAt[i] == stamp) continue;
                        seenAt[i] = stamp;
                        var pc = pieces[i];
                        // decks too: leaves over a bridge are leaves over a road
                        var d = pc.b - pc.a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - pc.a, d) / L2) : 0f;
                        var q = pc.a + d * t;
                        float dist = Vector2.Distance(p, q), off = dist - pc.hw;
                        if (off > r) continue;
                        var away = dist > 1e-4f ? (p - q) / dist : new Vector2(-d.y, d.x).normalized;
                        // the road's height where the point is square to it, not the piece's middle
                        float y = map.edges[pc.edge].YAt(pc.sa + (pc.sb - pc.sa) * t);
                        // every piece, not one per edge: a bend's pieces face different ways
                        outList.Add(new NearRoad { a = pc.a, b = pc.b, hw = pc.hw, y = y, off = off, away = away, clear = pc.clear, deck = pc.deck, edge = pc.edge });
                    }
                }
        }
        /// <summary>Per piece, the last query that looked at it.</summary>
        int[] seenAt = new int[0];
        int stampNow;
        int NextStamp()
        {
            if (seenAt.Length < pieces.Count) seenAt = new int[pieces.Count];
            return ++stampNow;
        }

        void BucketRange(Vector2 p, float r, out int x0, out int z0, out int x1, out int z1)
        {
            x0 = Mathf.Max(0, Mathf.FloorToInt((p.x - r - bmin.x) / BucketM)); x1 = Mathf.Min(bn - 1, Mathf.FloorToInt((p.x + r - bmin.x) / BucketM));
            z0 = Mathf.Max(0, Mathf.FloorToInt((p.y - r - bmin.y) / BucketM)); z1 = Mathf.Min(bn - 1, Mathf.FloorToInt((p.y + r - bmin.y) / BucketM));
        }

        /// <summary>A stretch of road near a point: its centreline chord and
        /// half width, the road's height square to the point, how far off its
        /// edge the point is and the unit direction from it to the point; its
        /// clear zone, whether it is a deck, and its edge.</summary>
        public struct NearRoad { public Vector2 a, b, away; public float hw, y, off, clear; public bool deck; public int edge; }

        /// <summary>The distance between two segments in plan.</summary>
        public static float SegSegDistance(Vector2 p0, Vector2 p1, Vector2 q0, Vector2 q1)
        {
            if (SegmentsCross(p0, p1, q0, q1)) return 0f;
            return Mathf.Min(Mathf.Min(DistToSeg(p0, q0, q1), DistToSeg(p1, q0, q1)),
                             Mathf.Min(DistToSeg(q0, p0, p1), DistToSeg(q1, p0, p1)));
        }

        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
            return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
        }

        /// <summary>Is a creek's bank within <paramref name="r"/> of a point?</summary>
        public bool NearCreek(Vector2 p, float r)
        {
            foreach (var c in creeks)
                if (DistToSeg(p, c.a, c.b) - c.hw < r) return true;
            return false;
        }

        // ------------------------------------------------------------------
        //  The road's edges: THE one accessor (R4 replaces its body)
        // ------------------------------------------------------------------

        /// <summary>
        /// The drawn road at arc <paramref name="s"/> of an edge: the plan
        /// point on its centreline, the unit vector to its right, and the
        /// ribbon's extent each side off it (hwL toward -right, hwR toward
        /// +right): the OSM polyline and the LINE MODEL's extents (WP-11b: a
        /// lane added on one side moves the ribbon off its line; tapers
        /// included; a squeeze only ever narrows it, so this errs toward
        /// keeping clear).
        /// </summary>
        public static void RoadEdgeAt(CityMap.Edge e, CityMeshes.Trims trims, float s,
                                      out Vector2 p, out Vector2 right, out float hwL, out float hwR)
        {
            p = e.PointAt(s);
            var tan = e.TangentAt(s);
            right = new Vector2(-tan.y, tan.x);
            LineModel.Extents(e, s, out hwL, out hwR);
        }

        /// <summary>How far round a lot's box its use reaches: a drive-thru's
        /// lane, order bay and parking round a restaurant (the play check stops
        /// a car in the bay; the first plant put a trunk in the pizzeria's), a
        /// yard's driveway round a house or trailer; nothing round a
        /// procedural building.</summary>
        public static float LotMarginOf(byte kind) => kind == 0 ? 0f : CityProps.IsFood(kind) ? 10f : 2f;

        /// <summary>Clear zone past the drawn edge by class (DOT: a freeway's
        /// 30 ft; the plan's arterial 3.5 m and local 2 m).</summary>
        public static float ClearZoneOf(CityMap.Edge e)
        {
            if (e.link) return 4.5f;
            if (e.cls >= 5) return 9f;
            if (e.cls == 4) return 6f;
            if (e.cls >= 2) return RoadsideRules.ClearZoneM;
            if (e.cls == 1) return 2.5f;
            return 2f;
        }

        // ------------------------------------------------------------------
        //  Build
        // ------------------------------------------------------------------

        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly HashSet<int> edgeScratch = new HashSet<int>();
        static readonly HashSet<int> nodeScratch = new HashSet<int>();
        static readonly List<(float ang, Vector2 p, Vector2 d, float hw, float clear)> arms = new List<(float, Vector2, Vector2, float, float)>(8);

        /// <summary>Build the mask of one tile: its static mask (<see cref="Static"/>)
        /// and <paramref name="tm"/>, the tile's own build (its fill houses and
        /// lamps); null leaves them out.</summary>
        public static RoadsideOccupancy Build(CityMap map, CityMeshes.Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, CityMeshes.TileMeshes tm, int tx, int tz)
        {
            var o = new RoadsideOccupancy(Static(map, trims, buildings, tx, tz));
            if (tm != null)
            {
                foreach (var h in tm.houseBoxes) o.MarkBox(h.c, h.u, h.hu, h.hv, BuildingMarginM + CellPadM, Building);
                foreach (var l in tm.lamps)
                    o.MarkDisc(new Vector2(l.foot.x + tm.origin.x, l.foot.z + tm.origin.z), LampFootR + CellPadM, Other);
                // WP-25: a culvert's headwall and its backfill (or its pipe's
                // barrel), the apron in front and the clay ditch down the
                // ravine: no tree grows through the wall or in the ditch
                foreach (var c in tm.culvertEnds)
                {
                    const float Apron = 3f;
                    float back = CityCulverts.WallThickM + c.backfillM;
                    o.MarkBox(c.at + c.outward * ((Apron - back) * 0.5f), c.outward, (Apron + back) * 0.5f, c.wallW * 0.5f + 0.5f, CellPadM, Water);
                    MarkDitch(o, c.at, c.downstream, c.pipeD);
                }
            }
            return o;
        }

        /// <summary>How many static masks were built, and in how many
        /// milliseconds all told (the pole audit's profile).</summary>
        public static int StaticBuilds;
        public static double StaticMs;

        static RoadsideOccupancy BuildStatic(CityMap map, CityMeshes.Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return BuildStaticNow(map, trims, buildings, tx, tz); }
            finally
            {
                StaticBuilds++;
                StaticMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
        }

        static RoadsideOccupancy BuildStaticNow(CityMap map, CityMeshes.Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            var o = new RoadsideOccupancy(map, tx, tz);
            var lo = o.min - Vector2.one * GatherM;
            var hi = o.min + Vector2.one * (CityMeshes.TileSize + GatherM);

            // ---- roads: pieces, pavement, clear zone, decks
            segScratch.Clear(); edgeScratch.Clear(); nodeScratch.Clear();
            map.EdgeSegsInRect(lo, hi, segScratch);
            foreach (int packed in segScratch) edgeScratch.Add(packed >> 12);
            foreach (int ei in edgeScratch)
            {
                var e = map.edges[ei];
                nodeScratch.Add(e.a); nodeScratch.Add(e.b);
                if (e.tunnel) continue;
                float clear = ClearZoneOf(e);
                // pieces at most PieceM long, cut at every vertex of the line
                // so a chord never cuts inside a bend
                float sPrev = 0f;
                RoadEdgeAt(e, trims, 0f, out var pPrev, out _, out float l0, out float r0);
                for (int si = 0; si + 1 < e.pts.Length; si++)
                {
                    float sa = e.s[si], sb = e.s[si + 1];
                    Vector2 va = e.pts[si], vb = e.pts[si + 1];
                    if (Mathf.Max(va.x, vb.x) < lo.x - 30f || Mathf.Min(va.x, vb.x) > hi.x + 30f ||
                        Mathf.Max(va.y, vb.y) < lo.y - 30f || Mathf.Min(va.y, vb.y) > hi.y + 30f)
                    {
                        // nowhere near: carry the chain on to the segment's end
                        RoadEdgeAt(e, trims, sb, out pPrev, out _, out l0, out r0);
                        sPrev = sb;
                        continue;
                    }
                    int n = Mathf.Max(1, Mathf.CeilToInt((sb - sa) / PieceM));
                    for (int k = 1; k <= n; k++)
                    {
                        float s1 = k == n ? sb : sa + (sb - sa) * k / n, sm = 0.5f * (sPrev + s1);
                        RoadEdgeAt(e, trims, s1, out var p1, out _, out float l1, out float r1);
                        var a = pPrev; float s0p = sPrev; pPrev = p1; sPrev = s1;
                        float hw = Mathf.Max(Mathf.Max(l0, r0), Mathf.Max(l1, r1));
                        l0 = l1; r0 = r1;
                        if (Mathf.Max(a.x, p1.x) < lo.x || Mathf.Min(a.x, p1.x) > hi.x ||
                            Mathf.Max(a.y, p1.y) < lo.y || Mathf.Min(a.y, p1.y) > hi.y) continue;
                        bool deck = e.bridge || e.ElevatedAt(sm);
                        int idx = o.pieces.Count;
                        o.pieces.Add(new Piece { a = a, b = p1, sa = s0p, sb = s1, hw = hw, clear = clear, y = e.YAt(sm), deck = deck, edge = ei });
                        o.Bucket(idx, a, p1, hw);
                        if (deck) o.MarkCapsule(a, p1, hw + DeckMarginM + CellPadM, Deck);
                        else o.MarkRoad(a, p1, hw + CellPadM, hw + clear + CellPadM);
                    }
                }
            }

            // ---- race run-off along the city routes (RaceRunOff): clear zone
            var runOff = RaceRunOff.For(map, trims, tx, tz);
            if (runOff != null) foreach (var m in runOff) o.MarkCapsule(m.a, m.b, m.r + CellPadM, Clear);

            // ---- junctions: fans, sight triangles, furniture spots
            foreach (int nd in nodeScratch)
            {
                var np = map.nodes[nd];
                if (np.x < lo.x || np.x > hi.x || np.y < lo.y || np.y > hi.y) continue;
                if (!trims.patch[nd]) continue;
                arms.Clear();
                float reach = 0f, clearMax = 0f;
                foreach (int ei in map.nodeEdges[nd])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || e.tunnel) continue;
                    float trim = trims.TrimAt(e, nd);
                    float at = e.a == nd ? Mathf.Min(trim, e.length) : Mathf.Max(0f, e.length - trim);
                    RoadEdgeAt(e, trims, at, out var p, out _, out float hwL, out float hwR);
                    float hw = Mathf.Max(hwL, hwR);
                    var tan = e.TangentAt(at);
                    var d = e.a == nd ? tan : -tan;
                    float clear = ClearZoneOf(e);
                    reach = Mathf.Max(reach, Mathf.Sqrt(trim * trim + hw * hw));
                    clearMax = Mathf.Max(clearMax, clear);
                    arms.Add((Mathf.Atan2(d.y, d.x), p, d, hw, clear));
                }
                if (arms.Count == 0) continue;
                // the raw DEM: a junction up on a structure (GroundY's corridor
                // pinning is the costliest read here, and not needed)
                bool onDeck = map.nodeY[nd] - CityElevation.BaseY(np.x, np.y) > 3f;
                o.MarkDisc(np, reach + (onDeck ? DeckMarginM : clearMax) + CellPadM, onDeck ? Deck : Clear);
                if (arms.Count < 3 || onDeck) continue;
                arms.Sort((x, y) => x.ang.CompareTo(y.ang));
                for (int i = 0; i < arms.Count; i++)
                {
                    var A = arms[i]; var B = arms[(i + 1) % arms.Count];
                    float gap = B.ang - A.ang;
                    if (gap <= 0f) gap += 2f * Mathf.PI;
                    // a fork (clipped, no corner) or a straight run past a stub
                    if (gap < 25f * Mathf.Deg2Rad || gap > 170f * Mathf.Deg2Rad) continue;
                    var nA = new Vector2(-A.d.y, A.d.x);   // A's kerb facing the corner
                    var nB = new Vector2(B.d.y, -B.d.x);   // B's kerb facing it
                    var qa = A.p + nA * A.hw; var qb = B.p + nB * B.hw;
                    // where the two kerb lines meet
                    float den = A.d.x * B.d.y - A.d.y * B.d.x;
                    Vector2 c;
                    if (Mathf.Abs(den) < 1e-3f) c = (qa + qb) * 0.5f;
                    else
                    {
                        var w = qb - qa;
                        float t = (w.x * B.d.y - w.y * B.d.x) / den;
                        float u = (w.x * A.d.y - w.y * A.d.x) / den;
                        c = t > -6f && t < 20f && u > -6f && u < 20f ? qa + A.d * t : (qa + qb) * 0.5f;
                    }
                    o.MarkTriangle(c, c + A.d * SightLegM, c + B.d * SightLegM, CellPadM, Sight);
                    var bis = (A.d + B.d);
                    if (bis.sqrMagnitude < 1e-4f) continue;
                    bis.Normalize();
                    // behind the corner: diagonally past both clear zones
                    float back = Mathf.Max(A.clear, B.clear) + CornerSpotBackM;
                    o.MarkDisc(c + bis * back, CornerSpotR + CellPadM, Corner);
                }
            }

            // ---- buildings: real footprints (every bucket one could reach
            // in from), procedural and prop lots, the fill houses
            for (int bz = tz - 1; bz <= tz + 1; bz++)
                for (int bx = tx - 1; bx <= tx + 1; bx++)
                {
                    var list = map.FootprintsInTile(bx, bz);
                    if (list != null)
                        foreach (int fi in list)
                        {
                            var f = map.footprints[fi];
                            if (f.gable) o.MarkBox(f.centre, f.u, f.hu, f.hv, BuildingMarginM + CellPadM, Building);
                            else o.MarkPolygon(f.pts, BuildingMarginM + CellPadM, Building);
                        }
                    if (buildings != null && buildings.TryGetValue(((long)bx << 24) ^ (bz & 0xFFFFFF), out var lots))
                        foreach (var b in lots)
                        {
                            // rgt = (cy, -sy) carries the width, fwd = (sy, cy) the depth (BuildBuildings)
                            float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                            float m = LotMarginOf(b.kind);
                            o.MarkBox(b.pos, new Vector2(cy, -sy), b.w * 0.5f + m, b.d * 0.5f + m, BuildingMarginM + CellPadM,
                                      b.kind == 0 ? Building : Other);
                        }
                }

            // ---- WP-25: every culvert end on the tile - the wall and its
            // backfill (or the pipe's barrel), the apron in front of it and
            // the clay ditch down the ravine - so no sign post or tree is
            // decided onto a headwall or into its ditch
            if (!CityMeshes.HydroOff)
                foreach (var c in CityCulverts.ForTile(map, trims, tx, tz))
                {
                    if (c.skip != null) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        if (!(k == 0 ? c.hasLo : c.hasHi)) continue;
                        var end = k == 0 ? c.lo : c.hi;
                        const float Apron = 3f;
                        float back = end.headwall ? CityCulverts.WallThickM + end.backfillM + 1f : CityCulverts.PipeBarrelM;
                        o.MarkBox(end.at + end.outward * ((Apron - back) * 0.5f), end.outward, (Apron + back) * 0.5f, end.wallW * 0.5f + 0.5f, CellPadM, Water);
                        MarkDitch(o, end.at, end.downstream, end.pipeD);
                    }
                }

            // ---- water: creeks by their drawn width, lakes by their shore
            segScratch.Clear();
            map.WaterSegsInRect(lo, hi, segScratch);
            foreach (int packed in segScratch)
            {
                int wi = packed >> 12, si = packed & 0xFFF;
                var w = map.waters[wi];
                if (w.lake || w.ravine || si + 1 >= w.pts.Length) continue;
                o.creeks.Add((w.pts[si], w.pts[si + 1], w.width * 0.5f));
                o.MarkCapsule(w.pts[si], w.pts[si + 1], w.width * 0.5f + WaterMarginM + CellPadM, Water);
            }
            var tmax = o.min + Vector2.one * CityMeshes.TileSize;
            foreach (int li in map.lakes)
            {
                var w = map.waters[li];
                if (w.bbMax.x < o.min.x || w.bbMin.x > tmax.x || w.bbMax.y < o.min.y || w.bbMin.y > tmax.y) continue;
                // the shore ring, filled a row at a time (5 point-in-lake tests a
                // cell cost a lakeside tile tens of milliseconds)
                o.MarkPolygon(w.pts, WaterMarginM + CellPadM, Water);
            }
            return o;
        }

        // ------------------------------------------------------------------
        //  Marking
        // ------------------------------------------------------------------

        void Bucket(int idx, Vector2 a, Vector2 b, float hw)
        {
            float r = hw;
            int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - r - bmin.x) / BucketM), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + r - bmin.x) / BucketM);
            int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - r - bmin.y) / BucketM), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + r - bmin.y) / BucketM);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(bn - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(bn - 1, x1); x++)
                    (buckets[z * bn + x] ??= new List<int>(8)).Add(idx);
        }

        /// <summary>Cells (index range) covering a world rectangle, clipped
        /// to the tile; false when it misses the tile.</summary>
        bool Range(Vector2 lo, Vector2 hi, out int x0, out int z0, out int x1, out int z1)
        {
            x0 = Mathf.Max(0, Mathf.FloorToInt((lo.x - min.x) / CellM));
            z0 = Mathf.Max(0, Mathf.FloorToInt((lo.y - min.y) / CellM));
            x1 = Mathf.Min(Res - 1, Mathf.FloorToInt((hi.x - min.x) / CellM));
            z1 = Mathf.Min(Res - 1, Mathf.FloorToInt((hi.y - min.y) / CellM));
            return x0 <= x1 && z0 <= z1;
        }

        Vector2 Centre(int x, int z) => new Vector2(min.x + (x + 0.5f) * CellM, min.y + (z + 0.5f) * CellM);

        public void MarkCapsule(Vector2 a, Vector2 b, float r, byte bit)
        {
            if (!Range(Vector2.Min(a, b) - Vector2.one * r, Vector2.Max(a, b) + Vector2.one * r, out int x0, out int z0, out int x1, out int z1)) return;
            var d = b - a;
            float L2 = d.sqrMagnitude, r2 = r * r;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = Centre(x, z) - a;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q, d) / L2) : 0f;
                    if ((q - d * t).sqrMagnitude <= r2) bits[z * Res + x] |= bit;
                }
        }

        public void MarkDisc(Vector2 c, float r, byte bit) => MarkCapsule(c, c, r, bit);

        /// <summary>A road piece in one pass: Pavement within
        /// <paramref name="rPave"/> of its chord, Clear within
        /// <paramref name="rClear"/>. The hottest loop of the mask (every
        /// piece round the tile), so plain floats.</summary>
        void MarkRoad(Vector2 a, Vector2 b, float rPave, float rClear)
        {
            if (!Range(Vector2.Min(a, b) - Vector2.one * rClear, Vector2.Max(a, b) + Vector2.one * rClear, out int x0, out int z0, out int x1, out int z1)) return;
            float dx = b.x - a.x, dz = b.y - a.y, L2 = dx * dx + dz * dz, inv = L2 > 1e-8f ? 1f / L2 : 0f;
            float p2 = rPave * rPave, c2 = rClear * rClear;
            for (int z = z0; z <= z1; z++)
            {
                float qz = min.y + (z + 0.5f) * CellM - a.y;
                int row = z * Res;
                for (int x = x0; x <= x1; x++)
                {
                    float qx = min.x + (x + 0.5f) * CellM - a.x;
                    float t = (qx * dx + qz * dz) * inv;
                    t = t < 0f ? 0f : t > 1f ? 1f : t;
                    float ex = qx - dx * t, ez = qz - dz * t, d2 = ex * ex + ez * ez;
                    if (d2 <= c2) bits[row + x] |= d2 <= p2 ? (byte)(Pavement | Clear) : Clear;
                }
            }
        }

        /// <summary>A triangle, widened by <paramref name="pad"/>.</summary>
        public void MarkTriangle(Vector2 a, Vector2 b, Vector2 c, float pad, byte bit)
        {
            var lo = Vector2.Min(a, Vector2.Min(b, c)) - Vector2.one * pad;
            var hi = Vector2.Max(a, Vector2.Max(b, c)) + Vector2.one * pad;
            if (!Range(lo, hi, out int x0, out int z0, out int x1, out int z1)) return;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = Centre(x, z);
                    if (InTriangle(q, a, b, c) || DistToSeg(q, a, b) <= pad || DistToSeg(q, b, c) <= pad || DistToSeg(q, c, a) <= pad)
                        bits[z * Res + x] |= bit;
                }
        }

        /// <summary>An oriented box (centre, unit long axis, half extents),
        /// widened by <paramref name="pad"/>.</summary>
        /// <summary>WP-25: a culvert end's clay ditch (CityMeshes.EmitDitch),
        /// <see cref="CityCulverts.DitchM"/> down the ravine from the end.</summary>
        static void MarkDitch(RoadsideOccupancy o, Vector2 at, Vector2 downstream, float pipeD) =>
            o.MarkBox(at + downstream * (CityCulverts.DitchM * 0.5f), downstream, CityCulverts.DitchM * 0.5f,
                      (pipeD + CityCulverts.DitchOverPipeM) * 0.5f + 0.5f, CellPadM, Water);

        public void MarkBox(Vector2 c, Vector2 u, float hu, float hv, float pad, byte bit)
        {
            var v = new Vector2(-u.y, u.x);
            float reach = Mathf.Sqrt(hu * hu + hv * hv) + pad;
            if (!Range(c - Vector2.one * reach, c + Vector2.one * reach, out int x0, out int z0, out int x1, out int z1)) return;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = Centre(x, z) - c;
                    float du = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, u)) - hu), dv = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, v)) - hv);
                    if (du * du + dv * dv <= pad * pad) bits[z * Res + x] |= bit;
                }
        }

        public void MarkPolygon(Vector2[] poly, float pad, byte bit)
        {
            if (poly == null || poly.Length < 3) return;
            Vector2 lo = poly[0], hi = poly[0];
            foreach (var p in poly) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            if (!Range(lo - Vector2.one * pad, hi + Vector2.one * pad, out int x0, out int z0, out int x1, out int z1)) return;
            // the inside, a row of cell centres at a time (even-odd crossings)
            int n = poly.Length;
            for (int z = z0; z <= z1; z++)
            {
                float cz = min.y + (z + 0.5f) * CellM;
                xings.Clear();
                for (int k = 0; k < n; k++)
                {
                    Vector2 a = poly[k], b = poly[(k + 1) % n];
                    if ((a.y > cz) == (b.y > cz)) continue;
                    xings.Add(a.x + (cz - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xings.Sort();
                for (int i = 0; i + 1 < xings.Count; i += 2)
                {
                    int xa = Mathf.Max(x0, Mathf.CeilToInt((xings[i] - min.x) / CellM - 0.5f));
                    int xb = Mathf.Min(x1, Mathf.FloorToInt((xings[i + 1] - min.x) / CellM - 0.5f));
                    for (int x = xa; x <= xb; x++) bits[z * Res + x] |= bit;
                }
            }
            // and everything within pad of a wall
            for (int k = 0; k < n; k++) MarkCapsule(poly[k], poly[(k + 1) % n], pad, bit);
        }
        static readonly List<float> xings = new List<float>(16);

        public static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a;
            float L2 = d.sqrMagnitude;
            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
            return Vector2.Distance(p, a + d * t);
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p - a, b - a), d2 = Cross(p - b, c - b), d3 = Cross(p - c, a - c);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
