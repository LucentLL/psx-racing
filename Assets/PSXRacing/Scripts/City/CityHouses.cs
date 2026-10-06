using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE HOUSES ON THEIR LOTS (leftover item 6, 2026-10-03). The owner:
    /// "houses lack driveways and many houses are above ground with their
    /// concrete foundations".
    ///
    /// One table of every building the city stands, by 256 m tile, from global
    /// data only (so every tile that asks finds the same answer): the real
    /// footprints, CityBuildings' lots and the interior fill (whose placement
    /// moved here from CityMeshes.BuildHouses, unchanged). A HOUSE is a real
    /// footprint's gabled box or house polygon, a frontage gable, a fill
    /// house, the prefab house or a trailer; everything else is a blocker.
    ///
    /// THE LOT IS GRADED (<see cref="Pad"/>, read by CityMeshes.LatticeVertex):
    /// every lattice corner whose NEAREST building is a house within
    /// <see cref="PadBlendM"/> of its walls is pulled to that house's pad
    /// height - fully within <see cref="PadFlatM"/>, eased (smoothstep) to the
    /// land beyond - so the house stands on a level lot instead of on a slope.
    /// The pad is the natural ground at the middle of the house's FRONT (the
    /// face to its street), never more than <see cref="PadMaxM"/> off the
    /// ground at its middle: the door is at grade, the back is cut or filled
    /// to it. Roads keep their DOT section: a graded corner never stands above
    /// any road's cap or back slope, nor a deck's, and never below a road's
    /// fill floor (CityElevation.Ground's own terms), and the land by a creek
    /// is not touched. A corner nearer a building that is not a house keeps
    /// its land. Everything that stands on the lattice (the houses, trees,
    /// poles, signs, lots, verges) stands on the graded ground.
    ///
    /// THE DRIVEWAY (<see cref="DrivewayOf"/>): from the street edge to the
    /// house's front, at its garage end, <see cref="DriveW"/> wide; drawn in
    /// the ground's pavement concrete cut into the lattice triangles (flush:
    /// no lift, no second surface), and the street's verge across its mouth is
    /// poured concrete with flared wings (the curb cut). A driveway never
    /// crosses another building or its lot, a parking lot, water, another
    /// road or a junction's mouth (two neighbours' may meet); the occupancy mask keeps
    /// every pole, sign, signal and tree off it, and no street lamp stands on
    /// it. There is no OSM service=driveway in the cache, so every driveway is
    /// synthesized.
    ///
    /// PSX_CITY_HOUSEPADS=0 / PSX_CITY_DRIVEWAYS=0 turn either off.
    /// </summary>
    public static class CityHouses
    {
        public static bool PadsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEPADS") != "0";
        public static bool DrivewaysOn = System.Environment.GetEnvironmentVariable("PSX_CITY_DRIVEWAYS") != "0";

        /// <summary>A lattice corner this near a house's walls takes its pad.</summary>
        public const float PadFlatM = 8f;
        /// <summary>...eased back to the land by this far out.</summary>
        public const float PadBlendM = 16f;
        /// <summary>The pad stays this close to the ground at the house's middle.</summary>
        public const float PadMaxM = 1.5f;
        /// <summary>A driveway's width (a single car's, 10 ft) and longest run.</summary>
        public const float DriveW = 3.0f, DriveMaxM = 60f;
        /// <summary>The curb cut's wings at the street, and how far out they run.</summary>
        public const float FlareM = 1.0f, FlareRunM = 2.5f;
        /// <summary>How far a house looks for its street.</summary>
        public const float StreetReachM = 75f;
        /// <summary>A driveway's foot stands this far past a junction fan's
        /// reach: clear of the corner, its sight triangle, its STOP sign and
        /// signal pole.</summary>
        public const float JunctionClearM = 4f;
        /// <summary>How far a prefab may sit into its high corner (its model's
        /// own plinth over the ground there), so the low side shows less.</summary>
        public static float SeatBuryM(CityProps.Def def) => PadsOn && def.baseM > 0f ? Mathf.Clamp(def.baseM - def.sink - 0.08f, 0f, 0.25f) : 0f;

        const float TileM = 256f, BucketM = 32f;
        const int Ext = 2, BN = 8 + 2 * Ext;
        const float LatCell = TileM / CityMeshes.GroundRes;

        public sealed class House
        {
            /// <summary>Global, deterministic: (tile, index in the tile's table).</summary>
            public long id;
            /// <summary>0 a blocker (any building that is not a house); 1 a real
            /// footprint's gabled box, 2 a real house polygon, 3 a frontage
            /// gable, 4 a fill house, 5 the prefab house, 6 a trailer.</summary>
            public byte kind;
            public Vector2 c, u;
            public float hu, hv;
            /// <summary>The way it faces its street where that is known (a
            /// frontage lot: toward the road it was placed from); zero to find.</summary>
            public Vector2 front;
            /// <summary>Fill houses: the walls' and the roof's heights.</summary>
            public float eaveH, riseH;
            internal byte streetState; internal int street = -1; internal float streetS;
            internal int[] fStreet; internal float[] fDist, fS;
            internal Vector2 faceN; internal float faceHalf, faceDepth;
            internal bool padDone; internal float pad;
            internal byte driveState; internal Driveway drive;
            internal int stamp;
            /// <summary>Why it has no driveway (null when it has one), and
            /// (the audit's) what stood in the first one's way.</summary>
            public string why, blockedBy;
            public bool IsHome => kind != 0;
            public Vector2 V => new Vector2(-u.y, u.x);
        }

        public sealed class Driveway
        {
            /// <summary>Anticlockwise in plan: the road end (q0, q1) on the
            /// street's edge, the house end (q2, q3) on its front.</summary>
            public Vector2[] quad = new Vector2[4];
            /// <summary>The curb cut: the verge across the mouth, wings flared
            /// <see cref="FlareM"/> each side at the edge (anticlockwise).</summary>
            public Vector2[] flare = new Vector2[4];
            public Vector2 foot, target, nrm, along;
            public int edge; public float s, len;
            public Vector2 min, max;
            public long owner;
            /// <summary>It runs from a parking lot's pavement (no curb cut).</summary>
            public bool toLot;
        }

        sealed class Tile
        {
            public int tx, tz;
            public Vector2 bmin;
            public readonly List<House> houses = new List<House>(64);
            public readonly List<House> fill = new List<House>(32);
            public readonly List<House>[] buckets = new List<House>[BN * BN];
        }

        static CityMap mapB;
        static CityMeshes.Trims trimsB;
        static Dictionary<long, List<CityBuildings.B>> bldB;
        static readonly Dictionary<long, Tile> tiles = new Dictionary<long, Tile>();
        static readonly Queue<long> order = new Queue<long>();
        const int TileCap = 96;
        static readonly Dictionary<int, float> fanReach = new Dictionary<int, float>();

        /// <summary>The world the table is of (every tile build binds it; a
        /// change of map, trims or lots empties it).</summary>
        public static void Bind(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            if (map == mapB && trims == trimsB && buildings == bldB) return;
            mapB = map; trimsB = trims; bldB = buildings;
            tiles.Clear(); order.Clear(); natCache.Clear(); fanReach.Clear();
        }
        public static bool Bound => mapB != null;

        static long TKey(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);
        static long LKey(int ix, int iz) => ((long)ix << 32) ^ (uint)iz;

        // ------------------------------------------------------------------
        //  THE TABLE
        // ------------------------------------------------------------------

        static Tile TileOf(int tx, int tz)
        {
            long k = TKey(tx, tz);
            if (tiles.TryGetValue(k, out var t)) return t;
            t = new Tile { tx = tx, tz = tz, bmin = new Vector2(tx * TileM - Ext * BucketM, tz * TileM - Ext * BucketM) };
            int idx = 0;
            var map = mapB;
            var fl = map.FootprintsInTile(tx, tz);
            if (fl != null)
                foreach (int fi in fl)
                {
                    var f = map.footprints[fi];
                    byte kind = f.propKind != 0 ? (byte)0 : f.gable ? (byte)1 : f.style == 3 ? (byte)2 : (byte)0;
                    Add(t, new House { kind = kind, c = f.centre, u = f.u, hu = f.hu, hv = f.hv }, ref idx);
                }
            if (bldB != null && bldB.TryGetValue(TKey(tx, tz), out var lots))
                foreach (var b in lots)
                {
                    float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                    byte kind = b.kind == 0 ? (b.gable ? (byte)3 : (byte)0)
                              : CityProps.IsHome(b.kind) ? (b.kind == CityProps.House ? (byte)5 : (byte)6) : (byte)0;
                    float m = kind == 0 ? RoadsideOccupancy.LotMarginOf(b.kind) : 0f;
                    Add(t, new House { kind = kind, c = b.pos, u = new Vector2(cy, -sy), hu = b.w * 0.5f + m, hv = b.d * 0.5f + m,
                                       front = kind != 0 ? new Vector2(sy, cy) : Vector2.zero }, ref idx);
                }
            FillInto(map, t);
            foreach (var h in t.fill) Add(t, h, ref idx);
            tiles[k] = t;
            order.Enqueue(k);
            while (order.Count > TileCap) tiles.Remove(order.Dequeue());
            return t;
        }

        static void Add(Tile t, House h, ref int idx)
        {
            h.id = ((long)(t.tx + 0x80000) << 40) | ((long)(t.tz + 0x80000) << 20) | (long)(idx++ & 0xFFFFF);
            t.houses.Add(h);
            var v = h.V;
            float ex = Mathf.Abs(h.u.x) * h.hu + Mathf.Abs(v.x) * h.hv + PadBlendM;
            float ez = Mathf.Abs(h.u.y) * h.hu + Mathf.Abs(v.y) * h.hv + PadBlendM;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((h.c.x - ex - t.bmin.x) / BucketM), 0, BN - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((h.c.x + ex - t.bmin.x) / BucketM), 0, BN - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((h.c.y - ez - t.bmin.y) / BucketM), 0, BN - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((h.c.y + ez - t.bmin.y) / BucketM), 0, BN - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    (t.buckets[z * BN + x] ??= new List<House>(8)).Add(h);
        }

        /// <summary>The houses whose box (grown by <see cref="PadBlendM"/>)
        /// may reach the point: every table whose grid covers it.</summary>
        static void Near(Vector2 p, List<House> into)
        {
            into.Clear();
            float reach = Ext * BucketM;
            int tx0 = Mathf.FloorToInt((p.x - reach) / TileM), tx1 = Mathf.FloorToInt((p.x + reach) / TileM);
            int tz0 = Mathf.FloorToInt((p.y - reach) / TileM), tz1 = Mathf.FloorToInt((p.y + reach) / TileM);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var t = TileOf(tx, tz);
                    int bx = Mathf.FloorToInt((p.x - t.bmin.x) / BucketM), bz = Mathf.FloorToInt((p.y - t.bmin.y) / BucketM);
                    if (bx < 0 || bz < 0 || bx >= BN || bz >= BN) continue;
                    var l = t.buckets[bz * BN + bx];
                    if (l != null) into.AddRange(l);
                }
        }

        public static float BoxDist(House h, Vector2 p)
        {
            var q = p - h.c;
            float du = Mathf.Abs(Vector2.Dot(q, h.u)) - h.hu, dv = Mathf.Abs(Vector2.Dot(q, h.V)) - h.hv;
            du = du > 0f ? du : 0f; dv = dv > 0f ? dv : 0f;
            return Mathf.Sqrt(du * du + dv * dv);
        }

        // ------------------------------------------------------------------
        //  THE INTERIOR FILL (was CityMeshes.BuildHouses; the same choices)
        // ------------------------------------------------------------------
        const float HouseCell = 24f;
        static readonly HashSet<int> fillSegs = new HashSet<int>();

        static void FillInto(CityMap map, Tile t)
        {
            var min = new Vector2(t.tx * TileM, t.tz * TileM);
            int cells = Mathf.RoundToInt(TileM / HouseCell);
            for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    int gx = t.tx * cells + cx, gz = t.tz * cells + cz;
                    var c = new Vector2(min.x + (cx + 0.5f) * HouseCell + (Hash01(gx, gz, 1) - 0.5f) * 9f,
                                        min.y + (cz + 0.5f) * HouseCell + (Hash01(gx, gz, 2) - 0.5f) * 9f);
                    if (map.footprintBounds.Contains(c)) continue;

                    // nearest street, and the corridor test against every road
                    fillSegs.Clear();
                    map.EdgeSegsInRect(c - Vector2.one * 260f, c + Vector2.one * 260f, fillSegs);
                    float dRoad = float.MaxValue; Vector2 tanRoad = Vector2.right, nearQ = c;
                    bool blocked = false;
                    foreach (var packed in fillSegs)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        var e = map.edges[ei];
                        Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(c - a, d) / L2) : 0f;
                        float dist = Vector2.Distance(c, a + d * tt);
                        if (dist < e.CorridorHalf + 24f) { blocked = true; break; }
                        if (!e.link && e.cls < 5 && dist < dRoad) { dRoad = dist; tanRoad = d.normalized; nearQ = a + d * tt; }
                    }
                    if (blocked || dRoad > 250f) continue;

                    float distUp = Vector2.Distance(c, map.uptown);
                    float keep = distUp < 7000f ? 0.62f : distUp < 12000f ? 0.42f : distUp < 16000f ? 0.22f : 0.07f;
                    keep *= Mathf.Lerp(1f, 0.3f, Mathf.Clamp01((dRoad - 110f) / 140f));
                    if (Hash01(gx, gz, 3) > keep) continue;

                    fillSegs.Clear();
                    map.WaterSegsInRect(c - Vector2.one * 30f, c + Vector2.one * 30f, fillSegs);
                    if (fillSegs.Count > 0) continue;
                    if (map.NearRavine(c, CityMeshes.RavineClearM) || map.InLake(c)) continue;
                    if (OnRealSite(map, c, 10f)) continue;

                    float hu = 4.6f + Hash01(gx, gz, 4) * 2.2f;   // half length, along the street
                    float hv = 3.8f + Hash01(gx, gz, 5) * 1.6f;   // half depth
                    float eaveH = 3.0f + Hash01(gx, gz, 6) * 0.6f;
                    float riseH = 1.7f + Hash01(gx, gz, 7) * 0.9f;
                    var u = tanRoad;
                    var v = new Vector2(-u.y, u.x);
                    t.fill.Add(new House { kind = 4, c = c, u = u, hu = hu, hv = hv, eaveH = eaveH, riseH = riseH,
                                           front = Vector2.Dot(nearQ - c, v) >= 0f ? v : -v });
                }
        }

        /// <summary>Coverage (2026-10-06): outside the core box the real
        /// non-residential footprints and the parking lots stand among the
        /// fill (a campus, a shopping centre and its lot). Is the point within
        /// <paramref name="r"/> of one? Nothing procedural is stood there.</summary>
        public static bool OnRealSite(CityMap map, Vector2 c, float r)
        {
            if (!map.FootprintClear(c, r)) return true;
            if (map.lots == null || map.lots.Length == 0) return false;
            for (int k = 0; k < 5; k++)
            {
                var p = c + (k == 0 ? Vector2.zero : k == 1 ? new Vector2(r, 0f) : k == 2 ? new Vector2(-r, 0f)
                                    : k == 3 ? new Vector2(0f, r) : new Vector2(0f, -r));
                var ll = CityMeshes.LotsInTile(map, Mathf.FloorToInt(p.x / TileM), Mathf.FloorToInt(p.y / TileM));
                if (ll == null) continue;
                foreach (int li in ll)
                {
                    var L = map.lots[li]; var b = L.box;
                    if (p.x < b.x || p.x > b.z || p.y < b.y || p.y > b.w) continue;
                    if (InRing(L.ring, p)) return true;
                }
            }
            return false;
        }

        /// <summary>The fill houses whose middle is in tile (tx, tz), in the
        /// order BuildHouses stood them.</summary>
        public static List<House> FillOf(CityMap map, int tx, int tz)
        {
            if (map != mapB) Bind(map, null, null);   // (a tile build binds its world first)
            return TileOf(tx, tz).fill;
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

        // ------------------------------------------------------------------
        //  THE STREETS A HOUSE FACES
        // ------------------------------------------------------------------
        static readonly HashSet<int> segs = new HashSet<int>();

        static bool StreetEdge(CityMap.Edge e) => !e.link && !e.tunnel && e.cls <= 3 && e.a != e.b && e.pts.Length >= 2;

        /// <summary>Face f's outward normal: 0 +u, 1 -u, 2 +v, 3 -v.</summary>
        static Vector2 FaceN(House h, int f) => f == 0 ? h.u : f == 1 ? -h.u : f == 2 ? h.V : -h.V;

        /// <summary>For each face, the nearest street point within
        /// <see cref="StreetReachM"/> that the face looks toward (within 60
        /// degrees of its normal). A frontage lot (one that knows the road it
        /// faces) only its front.</summary>
        static void FaceStreets(CityMap map, House h)
        {
            if (h.fStreet != null) return;
            h.fStreet = new[] { -1, -1, -1, -1 }; h.fDist = new[] { StreetReachM, StreetReachM, StreetReachM, StreetReachM }; h.fS = new float[4];
            segs.Clear();
            map.EdgeSegsInRect(h.c - Vector2.one * StreetReachM, h.c + Vector2.one * StreetReachM, segs);
            foreach (int packed in segs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (!StreetEdge(e) || si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float tt = Mathf.Clamp01(Vector2.Dot(h.c - a, d) / L2);
                var q = a + d * tt;
                float dist = Vector2.Distance(h.c, q);
                if (dist < 1e-3f) continue;
                var dir = (q - h.c) / dist;
                for (int f = 0; f < 4; f++)
                {
                    if (dist >= h.fDist[f]) continue;
                    var n = FaceN(h, f);
                    if (h.front != Vector2.zero && Vector2.Dot(n, h.front) < 0.9f) continue;
                    if (Vector2.Dot(dir, n) < 0.5f) continue;
                    h.fDist[f] = dist; h.fStreet[f] = ei; h.fS[f] = e.s[si] + Mathf.Sqrt(L2) * tt;
                }
            }
        }

        /// <summary>The nearest of the faces' streets: the house's FRONT.</summary>
        static void FindStreet(CityMap map, House h)
        {
            if (h.streetState != 0) return;
            h.streetState = 1;
            FaceStreets(map, h);
            int best = -1;
            for (int f = 0; f < 4; f++) if (h.fStreet[f] >= 0 && (best < 0 || h.fDist[f] < h.fDist[best])) best = f;
            if (best < 0) return;
            h.street = h.fStreet[best]; h.streetS = h.fS[best];
            h.faceN = FaceN(h, best);
            h.faceDepth = best < 2 ? h.hu : h.hv; h.faceHalf = best < 2 ? h.hv : h.hu;
        }

        // ------------------------------------------------------------------
        //  THE PAD
        // ------------------------------------------------------------------

        /// <summary>A lattice corner's land (CityElevation.Ground) and the
        /// limits the roads' DOT section puts on grading it: no higher than
        /// any road's cap, back slope (where the land was not already over it)
        /// or deck protection, no lower than a road's fill floor; a creek's
        /// or lake's banks not at all.</summary>
        struct NatRec { public float nat, up, lo; public bool carve; }
        static readonly Dictionary<long, NatRec> natCache = new Dictionary<long, NatRec>(8192);
        const int NatCap = 150000;

        static NatRec Rec(CityMap map, int ix, int iz)
        {
            long k = LKey(ix, iz);
            if (natCache.TryGetValue(k, out var r)) return r;
            float nat = CityElevation.Ground(map, ix * LatCell, iz * LatCell, out var t);
            float up = float.PositiveInfinity, lo = float.NegativeInfinity;
            if (!float.IsNaN(t.cut)) up = Mathf.Max(t.cut, nat);
            if (!float.IsNaN(t.protect)) up = Mathf.Min(up, t.protect);
            if (!float.IsNaN(t.deckProtect)) up = Mathf.Min(up, t.deckProtect);
            if (!float.IsNaN(t.deckCap)) up = Mathf.Min(up, t.deckCap);
            if (!float.IsNaN(t.floor)) lo = Mathf.Min(t.floor, nat);
            r = new NatRec { nat = nat, up = Mathf.Max(up, nat), lo = Mathf.Min(lo, nat), carve = t.carve > 0.01f };
            if (natCache.Count > NatCap) natCache.Clear();
            natCache[k] = r;
            return r;
        }

        /// <summary>The natural lattice at a plan point (the triangle the
        /// ground draws there, as CityMeshes.LatticeY).</summary>
        static float NatAt(CityMap map, Vector2 p)
        {
            float fx = p.x / LatCell, fz = p.y / LatCell;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            float h00 = Rec(map, ix, iz).nat, h11 = Rec(map, ix + 1, iz + 1).nat;
            if (tx >= tz) { float h10 = Rec(map, ix + 1, iz).nat; return h00 + tx * (h10 - h00) + tz * (h11 - h10); }
            float h01 = Rec(map, ix, iz + 1).nat;
            return h00 + tz * (h01 - h00) + tx * (h11 - h01);
        }

        /// <summary>
        /// A house's pad height: the natural ground at the middle of its front
        /// (its middle where it has no street), within <see cref="PadMaxM"/>
        /// of the ground at its middle - then inside the limits of every
        /// lattice corner it grades (its own within <see cref="PadFlatM"/>):
        /// never above a corner the road's section caps (the lot is cut down
        /// to it instead), never below one a road's fill holds up. NaN: no pad.
        /// </summary>
        public static float PadOf(House h)
        {
            if (h.padDone) return h.pad;
            h.padDone = true; h.pad = float.NaN;
            if (!h.IsHome || mapB == null) return h.pad;
            var map = mapB;
            FindStreet(map, h);
            float mid = NatAt(map, h.c);
            float front = h.street >= 0 ? NatAt(map, h.c + h.faceN * h.faceDepth) : mid;
            float P = Mathf.Clamp(front, mid - PadMaxM, mid + PadMaxM);
            float loMax = float.NegativeInfinity, upMin = float.PositiveInfinity;
            var v = h.V;
            float ex = Mathf.Abs(h.u.x) * h.hu + Mathf.Abs(v.x) * h.hv + PadFlatM;
            float ez = Mathf.Abs(h.u.y) * h.hu + Mathf.Abs(v.y) * h.hv + PadFlatM;
            for (int iz = Mathf.CeilToInt((h.c.y - ez) / LatCell); iz <= Mathf.FloorToInt((h.c.y + ez) / LatCell); iz++)
                for (int ix = Mathf.CeilToInt((h.c.x - ex) / LatCell); ix <= Mathf.FloorToInt((h.c.x + ex) / LatCell); ix++)
                {
                    var p = new Vector2(ix * LatCell, iz * LatCell);
                    if (BoxDist(h, p) > PadFlatM) continue;
                    var r = Rec(map, ix, iz);
                    if (r.carve || OwnerOf(map, p) != h) continue;
                    if (r.up < upMin) upMin = r.up;
                    if (r.lo > loMax) loMax = r.lo;
                }
            if (loMax <= upMin) P = Mathf.Clamp(P, loMax, upMin);
            else P = Mathf.Min(P, upMin);
            h.pad = P;
            return h.pad;
        }

        static readonly List<House> near = new List<House>(64);

        /// <summary>
        /// THE GRADED LATTICE CORNER (CityMeshes.LatticeVertex's height
        /// before the junction fans' floors): the land, or - where the
        /// nearest building is a house within <see cref="PadBlendM"/> - pulled
        /// to its pad (fully within <see cref="PadFlatM"/>), inside the
        /// corner's road limits.
        /// </summary>
        public static float Lattice(CityMap map, int ix, int iz)
        {
            if (map != mapB) return CityElevation.GroundY(map, ix * LatCell, iz * LatCell);
            var r = Rec(map, ix, iz);
            if (!PadsOn || r.carve) return r.nat;   // a creek's or a lake's banks keep their land
            var p = new Vector2(ix * LatCell, iz * LatCell);
            var best = Owner(p, out float bestD);
            if (best == null || !best.IsHome) return r.nat;
            float P = PadOf(best);
            if (float.IsNaN(P)) return r.nat;
            float w = bestD <= PadFlatM ? 1f : Mathf.SmoothStep(0f, 1f, (PadBlendM - bestD) / (PadBlendM - PadFlatM));
            return Mathf.Clamp(r.nat + w * (P - r.nat), r.lo, r.up);
        }

        /// <summary>The building nearest a point within <see cref="PadBlendM"/>
        /// of its walls (ties: the lower id), and how far.</summary>
        static House Owner(Vector2 p, out float bestD)
        {
            Near(p, near);
            House best = null; bestD = PadBlendM;
            foreach (var h in near)
            {
                float d = BoxDist(h, p);
                if (d < bestD - 1e-5f || (best != null && Mathf.Abs(d - bestD) <= 1e-5f && h.id < best.id)) { best = h; bestD = d; }
            }
            return best;
        }

        /// <summary>The house a lattice corner is graded to (null: none).</summary>
        public static House OwnerOf(CityMap map, Vector2 p) => Owner(p, out _);

        /// <summary>For the house audit: why a lattice corner stands where it
        /// does (its land, the house it was graded to and how far off, the
        /// weight, and its road limits).</summary>
        public static string Explain(CityMap map, int ix, int iz)
        {
            var r = Rec(map, ix, iz);
            var p = new Vector2(ix * LatCell, iz * LatCell);
            var best = Owner(p, out float bestD);
            if (best == null) return $"nat {r.nat:0.00} no house within {PadBlendM} m";
            float P = best.IsHome ? PadOf(best) : float.NaN;
            float w = bestD <= PadFlatM ? 1f : Mathf.SmoothStep(0f, 1f, (PadBlendM - bestD) / (PadBlendM - PadFlatM));
            float y = float.IsNaN(P) ? r.nat : r.nat + w * (P - r.nat);
            return $"nat {r.nat:0.00} house {best.id & 0xFFFFF} kind {best.kind} d {bestD:0.0} w {w:0.00} P {P:0.00} -> {y:0.00} limits {r.lo:0.00}..{r.up:0.00}{(r.carve ? " CARVE" : "")}";
        }

        /// <summary>The drawn ground's lowest and highest along an oriented
        /// box's walls (every metre at most) and at every lattice corner
        /// inside it: what a procedural house is seated on.</summary>
        public static void GroundRange(CityMap map, Vector2 c, Vector2 u, float hu, float hv, out float lo, out float hi)
        {
            var v = new Vector2(-u.y, u.x);
            lo = float.MaxValue; hi = float.MinValue;
            k4[0] = c + u * hu + v * hv; k4[1] = c - u * hu + v * hv; k4[2] = c - u * hu - v * hv; k4[3] = c + u * hu - v * hv;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = k4[i], b = k4[(i + 1) & 3];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int j = 0; j < n; j++)
                {
                    var p = Vector2.Lerp(a, b, j / (float)n);
                    float g = CityMeshes.LatticeAt(map, p.x, p.y);
                    if (g < lo) lo = g; if (g > hi) hi = g;
                }
            }
            float r = Mathf.Sqrt(hu * hu + hv * hv);
            for (int z = Mathf.CeilToInt((c.y - r) / LatCell); z <= Mathf.FloorToInt((c.y + r) / LatCell); z++)
                for (int x = Mathf.CeilToInt((c.x - r) / LatCell); x <= Mathf.FloorToInt((c.x + r) / LatCell); x++)
                {
                    var q = new Vector2(x * LatCell, z * LatCell) - c;
                    if (Mathf.Abs(Vector2.Dot(q, u)) > hu || Mathf.Abs(Vector2.Dot(q, v)) > hv) continue;
                    float g = CityMeshes.LatticeAt(map, x * LatCell, z * LatCell);
                    if (g < lo) lo = g; if (g > hi) hi = g;
                }
        }
        static readonly Vector2[] k4 = new Vector2[4];

        // ------------------------------------------------------------------
        //  THE DRIVEWAY
        // ------------------------------------------------------------------

        /// <summary>The shape a house's driveway would take from face
        /// <paramref name="face"/> to the street that face looks toward, at
        /// <paramref name="lat"/> (-1..1) of the way along the face from its
        /// middle toward one end (a garage end) - before anything is asked of
        /// the ground it crosses. Null (and why) when there is none.</summary>
        static Driveway Shape(CityMap map, House h, int face, float lat, out string why)
        {
            why = null;
            FaceStreets(map, h);
            if (h.fStreet[face] < 0) { why = "no street within reach"; return null; }
            var n = FaceN(h, face); var t = new Vector2(-n.y, n.x);
            float depth = face < 2 ? h.hu : h.hv, half = face < 2 ? h.hv : h.hu;
            float off = Mathf.Max(0f, half - DriveW * 0.5f - 0.6f) * lat;
            var T = h.c + n * depth + t * off;
            return MakeDrive(map, h, T, n, t, T, out why);
        }

        /// <summary>The nearest point of any street (<see cref="StreetEdge"/>)
        /// to a plan point, within <see cref="StreetReachM"/>: the street a
        /// driveway meets is the one square in front of it, whichever OSM way
        /// that stretch is.</summary>
        static bool StreetAt(CityMap map, Vector2 p, out CityMap.Edge e, out float s)
        {
            e = null; s = 0f;
            float best = StreetReachM;
            segs2.Clear();
            map.EdgeSegsInRect(p - Vector2.one * StreetReachM, p + Vector2.one * StreetReachM, segs2);
            foreach (int packed in segs2)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[ei];
                if (!StreetEdge(o) || si + 1 >= o.pts.Length) continue;
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float tt = Mathf.Clamp01(Vector2.Dot(p - a, d) / L2);
                float dist = Vector2.Distance(p, a + d * tt);
                if (dist < best || (dist == best && e != null && ei < e.index)) { best = dist; e = o; s = o.s[si] + Mathf.Sqrt(L2) * tt; }
            }
            return e != null;
        }
        static readonly HashSet<int> segs2 = new HashSet<int>();

        /// <summary>The driveway from the house's face at <paramref name="T"/>
        /// (<paramref name="n"/> out of the face, <paramref name="t"/> along
        /// it) to the edge of the street nearest <paramref name="aim"/>: its
        /// road end square to the street, its house end along the face, no
        /// more than 32 degrees off square to either. Null (and why) where
        /// that cannot be one.</summary>
        static Driveway MakeDrive(CityMap map, House h, Vector2 T, Vector2 n, Vector2 t, Vector2 aim, out string why)
        {
            why = null;
            if (!StreetAt(map, aim, out var e, out float sT)) { why = "no street within reach"; return null; }
            sT = Mathf.Clamp(sT, 0f, e.length);
            if (e.ElevatedAt(sT)) { why = "street on a structure"; return null; }
            var Q = e.PointAt(sT); var tan = e.TangentAt(sT);
            var left = new Vector2(-tan.y, tan.x);
            float sideS = Vector2.Dot(T - Q, left) >= 0f ? 1f : -1f;
            LineModel.Extents(e, sT, out float eM, out float eP);
            var nrm = left * sideS;
            var F = Q + nrm * (sideS > 0f ? eP : eM);
            var dv = T - F;
            float len = Vector2.Dot(dv, nrm);
            if (len < 0.5f) { why = "front on the street edge"; return null; }
            if (dv.magnitude > DriveMaxM) { why = "street too far"; return null; }
            if (Vector2.Dot(dv.normalized, nrm) < SquareDot || Vector2.Dot(-dv.normalized, n) < SquareDot) { why = "skewed to its street"; return null; }
            float hw = DriveW * 0.5f;
            if (Vector2.Dot(t, tan) < 0f) t = -t;
            var d = new Driveway { foot = F, target = T, nrm = nrm, along = tan, edge = e.index, s = sT, len = dv.magnitude, owner = h.id };
            // anticlockwise: with nrm to the LEFT of tan the road end runs -tan
            // to +tan; with it to the right, the other way
            Vector2 a = F - tan * hw, b = F + tan * hw, c = T + t * hw, dd = T - t * hw;
            if (sideS > 0f) { d.quad[0] = a; d.quad[1] = b; d.quad[2] = c; d.quad[3] = dd; }
            else { d.quad[0] = b; d.quad[1] = a; d.quad[2] = dd; d.quad[3] = c; }
            if (!Convex(d.quad)) { why = "skewed to its street"; return null; }
            float fw = hw + FlareM, fr = Mathf.Min(FlareRunM, len);
            Vector2 fa = F - tan * fw, fb = F + tan * fw, fc = F + nrm * fr + tan * hw, fd = F + nrm * fr - tan * hw;
            if (sideS > 0f) { d.flare[0] = fa; d.flare[1] = fb; d.flare[2] = fc; d.flare[3] = fd; }
            else { d.flare[0] = fb; d.flare[1] = fa; d.flare[2] = fd; d.flare[3] = fc; }
            d.min = Vector2.Min(Vector2.Min(Vector2.Min(fa, fb), Vector2.Min(c, dd)), Vector2.Min(a, b));
            d.max = Vector2.Max(Vector2.Max(Vector2.Max(fa, fb), Vector2.Max(c, dd)), Vector2.Max(a, b));
            return d;
        }

        /// <summary>cos 32 degrees: how far off square a driveway may run.</summary>
        const float SquareDot = 0.85f;

        static bool Convex(Vector2[] q)
        {
            for (int i = 0; i < 4; i++)
            {
                var a = q[i]; var b = q[(i + 1) & 3]; var c = q[(i + 2) & 3];
                if (Cross(b - a, c - b) <= 1e-4f) return false;
            }
            return true;
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>A house's driveway, decided once (null: none, and
        /// <see cref="House.why"/> says why).</summary>
        public static Driveway DrivewayOf(House h)
        {
            if (h.driveState != 0) return h.drive;
            h.driveState = 1;
            if (!DrivewaysOn || !h.IsHome || mapB == null) { h.why = h.IsHome ? "driveways off" : null; return null; }
            // the faces that look toward a street, nearest street first; on
            // each, the garage end its hash picks, the other end, then nearer
            // the middle
            FaceStreets(mapB, h);
            string why = null;
            float sign = Hash01((int)(h.id & 0xFFFFF), (int)(h.id >> 20), 41) < 0.5f ? -1f : 1f;
            int tried = 0;
            for (int k = 0; k < 4 && h.drive == null; k++)
            {
                int face = -1;
                for (int f = 0; f < 4; f++)
                    if (h.fStreet[f] >= 0 && (tried & (1 << f)) == 0 && (face < 0 || h.fDist[f] < h.fDist[face])) face = f;
                if (face < 0) break;
                tried |= 1 << face;
                foreach (float lat in Lats)
                {
                    var d = Shape(mapB, h, face, lat * sign, out string w);
                    if (d == null) { why ??= w; if (w != "skewed to its street") break; continue; }
                    string c = Conflict(mapB, trimsB, h, d);
                    if (c == null) { h.drive = d; break; }
                    // a parking lot between the house and its street is its
                    // way out: the driveway runs from the lot's pavement
                    if (c == LotWhy)
                    {
                        var d2 = ToLot(mapB, d);
                        if (d2 != null && Conflict(mapB, trimsB, h, d2) == null) { h.drive = d2; break; }
                    }
                    why ??= c;
                }
                // the buildings in front leave a gap: aim through it
                if (h.drive == null && h.blockedBy != null)
                {
                    var d = Corridor(mapB, h, face);
                    if (d != null && Conflict(mapB, trimsB, h, d) == null) h.drive = d;
                }
            }
            if (h.drive == null && why == null) why = "no street within reach";
            h.why = h.drive == null ? why : null;
            return h.drive;
        }

        static readonly float[] Lats = { 1f, -1f, 0.5f, -0.5f, 0f };
        const string LotWhy = "a parking lot";
        static readonly List<House> corridorNear = new List<House>(64);
        static readonly List<Vector2> blockedAlong = new List<Vector2>(32);

        /// <summary>
        /// A driveway threaded between the buildings that stand between a
        /// face and its street (a house behind another, a narrow side yard):
        /// every building in that strip blocks its span along the street,
        /// grown by its margin and half a driveway; the free spot along the
        /// street nearest the face's own usable span is the road end, and the
        /// house end is the face's point nearest it (so the driveway runs
        /// skewed where it must). Null where no gap is free.
        /// </summary>
        static Driveway Corridor(CityMap map, House h, int face)
        {
            if (h.fStreet[face] < 0) return null;
            var e = map.edges[h.fStreet[face]];
            var n = FaceN(h, face); var t = new Vector2(-n.y, n.x);
            float depth = face < 2 ? h.hu : h.hv, half = face < 2 ? h.hv : h.hu;
            var Fc = h.c + n * depth;
            CityElevation.ProjectOn(e, Fc, out float s0);
            s0 = Mathf.Clamp(s0, 0f, e.length);
            var Q = e.PointAt(s0); var tan = e.TangentAt(s0);
            var left = new Vector2(-tan.y, tan.x);
            float sideS = Vector2.Dot(Fc - Q, left) >= 0f ? 1f : -1f;
            LineModel.Extents(e, s0, out float eM, out float eP);
            var nrm = left * sideS;
            var E0 = Q + nrm * (sideS > 0f ? eP : eM);
            float across = Vector2.Dot(Fc - E0, nrm);
            if (across < 1f || across > DriveMaxM) return null;
            float hw = DriveW * 0.5f;
            float ta = Vector2.Dot(t, tan);
            if (Mathf.Abs(ta) < 0.3f) return null;   // a face square to its street
            float fc = Vector2.Dot(Fc - E0, tan);
            float lo = fc - (half - hw - 0.6f) * Mathf.Abs(ta), hi = fc + (half - hw - 0.6f) * Mathf.Abs(ta);
            if (lo > hi) lo = hi = fc;
            const float Look = 25f;
            var bmin = Vector2.Min(Vector2.Min(E0 + tan * (lo - Look), E0 + tan * (hi + Look)), Vector2.Min(E0 + tan * (lo - Look) + nrm * across, E0 + tan * (hi + Look) + nrm * across));
            var bmax = Vector2.Max(Vector2.Max(E0 + tan * (lo - Look), E0 + tan * (hi + Look)), Vector2.Max(E0 + tan * (lo - Look) + nrm * across, E0 + tan * (hi + Look) + nrm * across));
            CollectBox(bmin, bmax, corridorNear);
            blockedAlong.Clear();
            foreach (var o in corridorNear)
            {
                if (o == h) continue;
                float m = o.IsHome ? 0.3f : 0.6f;
                var ov = o.V;
                float oa = float.MaxValue, ob = float.MinValue, oc = float.MaxValue, od = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    var q = o.c + o.u * ((k & 1) == 0 ? o.hu + m : -o.hu - m) + ov * ((k & 2) == 0 ? o.hv + m : -o.hv - m) - E0;
                    float al = Vector2.Dot(q, tan), ac = Vector2.Dot(q, nrm);
                    oa = Mathf.Min(oa, al); ob = Mathf.Max(ob, al); oc = Mathf.Min(oc, ac); od = Mathf.Max(od, ac);
                }
                if (od < 0f || oc > across) continue;
                blockedAlong.Add(new Vector2(oa - hw - 0.05f, ob + hw + 0.05f));
            }
            // the free spot nearest the face's usable span, in half-metre steps
            float best = float.NaN, bestCost = float.MaxValue;
            for (float x = lo - Look; x <= hi + Look; x += 0.5f)
            {
                float cost = x < lo ? lo - x : x > hi ? x - hi : 0f;
                if (cost >= bestCost) continue;
                bool free = true;
                foreach (var b in blockedAlong) if (x > b.x && x < b.y) { free = false; break; }
                if (free) { best = x; bestCost = cost; }
            }
            if (float.IsNaN(best)) return null;
            // the house end: the face's point nearest that spot
            float k0 = Mathf.Clamp((best - fc) / ta, -(half - hw - 0.6f), half - hw - 0.6f);
            if (half - hw - 0.6f < 0f) k0 = 0f;
            var T = Fc + t * k0;
            return MakeDrive(map, h, T, n, t, E0 + tan * best, out _);
        }

        /// <summary>A driveway whose way to the street crosses a parking lot,
        /// cut back to start just inside the lot's pavement (the lot is its
        /// way out); null when the lot is at the house's door.</summary>
        static Driveway ToLot(CityMap map, Driveway d)
        {
            var dir = d.foot - d.target;
            float L = dir.magnitude;
            if (L < 1e-3f) return null;
            dir /= L;
            for (float s = 2f; s <= L; s += 0.5f)
            {
                var p = d.target + dir * s;
                var ll = CityMeshes.LotsInTile(map, Mathf.FloorToInt(p.x / TileM), Mathf.FloorToInt(p.y / TileM));
                if (ll == null) continue;
                foreach (int li in ll)
                {
                    var Lt = map.lots[li]; var b = Lt.box;
                    if (p.x < b.x || p.x > b.z || p.y < b.y || p.y > b.w || !InRing(Lt.ring, p)) continue;
                    // the road end square to the driveway, 0.5 m inside the lot
                    var F = p + dir * 0.5f;
                    var side = new Vector2(-dir.y, dir.x) * (DriveW * 0.5f);
                    var n = new Driveway { foot = F, target = d.target, nrm = -dir, along = d.along, edge = d.edge, s = d.s, len = s + 0.5f, owner = d.owner, toLot = true };
                    // anticlockwise, as Shape lays its quads: road end, then house end
                    Vector2 a = F - side, bb = F + side, c = d.quad[2], dd = d.quad[3];
                    n.quad[0] = a; n.quad[1] = bb; n.quad[2] = c; n.quad[3] = dd;
                    if (!Convex(n.quad)) { n.quad[0] = bb; n.quad[1] = a; }
                    if (!Convex(n.quad)) return null;
                    System.Array.Copy(n.quad, n.flare, 4);
                    n.min = Vector2.Min(Vector2.Min(a, bb), Vector2.Min(c, dd));
                    n.max = Vector2.Max(Vector2.Max(a, bb), Vector2.Max(c, dd));
                    return n;
                }
            }
            return null;
        }
        static readonly List<House> nearQ = new List<House>(64);
        static readonly HashSet<int> qSegs = new HashSet<int>();
        static readonly HashSet<int> qWater = new HashSet<int>();
        static readonly List<Vector2> samples = new List<Vector2>(64);

        /// <summary>What the driveway would run through, or null.</summary>
        static string Conflict(CityMap map, CityMeshes.Trims trims, House h, Driveway d)
        {
            // other buildings and their lots (two neighbours' driveways may
            // meet: concrete beside concrete, cut into the ground once)
            CollectBox(d.min - Vector2.one * 2f, d.max + Vector2.one * 2f, nearQ);
            foreach (var o in nearQ)
            {
                if (o == h) continue;
                float m = o.IsHome ? 0.3f : 0.6f;
                if (QuadHitsBox(d.quad, o.c, o.u, o.hu + m, o.hv + m))
                {
                    h.blockedBy ??= string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "kind {0} {1:0} m2 at ({2:0.0}, {3:0.0}), {4:0.0} m off the house's walls", o.kind, 4f * o.hu * o.hv, o.c.x, o.c.y, MinBoxGap(h, o));
                    return "crosses a building or its lot";
                }
            }
            // samples: the middle line and both sides, every 1.5 m from 1 m past the street's edge
            samples.Clear();
            int ns = Mathf.Max(2, Mathf.CeilToInt(d.len / 1.5f));
            for (int i = 0; i <= ns; i++)
            {
                float f = i / (float)ns;
                var a = Vector2.Lerp(d.quad[0], d.quad[3], f); var b = Vector2.Lerp(d.quad[1], d.quad[2], f);
                if (f * d.len < 1.0f) continue;
                samples.Add(a); samples.Add(b); samples.Add((a + b) * 0.5f);
            }
            // parking lots
            int tx0 = Mathf.FloorToInt(d.min.x / TileM), tx1 = Mathf.FloorToInt(d.max.x / TileM);
            int tz0 = Mathf.FloorToInt(d.min.y / TileM), tz1 = Mathf.FloorToInt(d.max.y / TileM);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var ll = CityMeshes.LotsInTile(map, tx, tz);
                    if (ll == null) continue;
                    foreach (int li in ll)
                    {
                        var L = map.lots[li]; var b = L.box;
                        if (b.z < d.min.x || b.x > d.max.x || b.w < d.min.y || b.y > d.max.y) continue;
                        foreach (var s in samples) if ((!d.toLot || Vector2.Distance(s, d.foot) > 2.5f) && InRing(L.ring, s)) return LotWhy;
                    }
                }
            // water, lakes, ravines
            qWater.Clear();
            map.WaterSegsInRect(d.min - Vector2.one * 8f, d.max + Vector2.one * 8f, qWater);
            foreach (int packed in qWater)
            {
                var w = map.waters[packed >> 12]; int si = packed & 0xFFF;
                if (w.lake || si + 1 >= w.pts.Length) continue;
                foreach (var s in samples)
                    if (RoadsideOccupancy.DistToSeg(s, w.pts[si], w.pts[si + 1]) < w.width * 0.5f + 3f) return "water or a culvert";
            }
            // (a lake or a ravine's channel at the ends and the middle: the
            // house itself was placed clear of both)
            for (int i = 0; i < samples.Count; i += Mathf.Max(1, samples.Count / 3))
                if (map.InLake(samples[i]) || map.NearRavine(samples[i], 3f)) return "water or a culvert";
            // roads: no sample on (or within 0.3 m of) any drawn carriageway,
            // its own street's included past the mouth
            qSegs.Clear();
            map.EdgeSegsInRect(d.min - Vector2.one * 30f, d.max + Vector2.one * 30f, qSegs);
            foreach (int packed in qSegs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[ei];
                if (o.tunnel || si + 1 >= o.pts.Length) continue;
                Vector2 a = o.pts[si], dd = o.pts[si + 1] - a;
                float L2 = dd.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float len = Mathf.Sqrt(L2);
                foreach (var s in samples)
                {
                    float tt = Mathf.Clamp01(Vector2.Dot(s - a, dd) / L2);
                    var q = a + dd * tt;
                    float dist = Vector2.Distance(s, q);
                    if (dist > o.HalfMax + 2f) continue;
                    float at = o.s[si] + len * tt;
                    LineModel.Extents(o, at, out float eM, out float eP);
                    float side = Cross(dd, s - a) >= 0f ? eP : eM;
                    if (dist < side + (o.ElevatedAt(at) ? 1.0f : 0.3f)) return "crosses a road";
                }
                // the mouth of a junction: a node of three or more roads whose
                // fan reaches the driveway's foot (its own street's ends are
                // measured along it, from where the fan stops: its trim)
                if (trims != null && !d.toLot)
                    for (int k = 0; k < 2; k++)
                    {
                        int nd = k == 0 ? o.a : o.b;
                        if (!trims.patch[nd] || map.nodeEdges[nd].Count < 3) continue;
                        if (d.edge >= 0 && (nd == map.edges[d.edge].a || nd == map.edges[d.edge].b)) continue;
                        if (Vector2.Distance(map.nodes[nd], d.foot) < FanReach(map, trims, nd) + JunctionClearM) return "a junction's mouth";
                    }
            }
            if (trims != null && !d.toLot && d.edge >= 0)
            {
                var own = map.edges[d.edge];
                float hw = DriveW * 0.5f + FlareM;
                for (int k = 0; k < 2; k++)
                {
                    int nd = k == 0 ? own.a : own.b;
                    if (!trims.patch[nd] || map.nodeEdges[nd].Count < 3) continue;
                    float along = k == 0 ? d.s : own.length - d.s;
                    if (along < trims.TrimAt(own, nd) + JunctionClearM + hw) return "a junction's mouth";
                }
            }
            return null;
        }

        static float FanReach(CityMap map, CityMeshes.Trims trims, int nd)
        {
            if (fanReach.TryGetValue(nd, out float r)) return r;
            r = 0f;
            foreach (int oi in map.nodeEdges[nd])
            {
                var o = map.edges[oi];
                float tr = trims.TrimAt(o, nd), hw = o.HalfMax;
                r = Mathf.Max(r, Mathf.Sqrt(tr * tr + hw * hw));
            }
            fanReach[nd] = r;
            return r;
        }

        /// <summary>Every house whose box (grown by <see cref="PadBlendM"/>)
        /// reaches the rectangle, once each: the buckets it covers in every
        /// table whose grid meets it.</summary>
        static void CollectBox(Vector2 min, Vector2 max, List<House> into)
        {
            into.Clear();
            stampNow++;
            float reach = Ext * BucketM;
            int tx0 = Mathf.FloorToInt((min.x - reach) / TileM), tx1 = Mathf.FloorToInt((max.x + reach) / TileM);
            int tz0 = Mathf.FloorToInt((min.y - reach) / TileM), tz1 = Mathf.FloorToInt((max.y + reach) / TileM);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var t = TileOf(tx, tz);
                    int bx0 = Mathf.Max(0, Mathf.FloorToInt((min.x - t.bmin.x) / BucketM)), bx1 = Mathf.Min(BN - 1, Mathf.FloorToInt((max.x - t.bmin.x) / BucketM));
                    int bz0 = Mathf.Max(0, Mathf.FloorToInt((min.y - t.bmin.y) / BucketM)), bz1 = Mathf.Min(BN - 1, Mathf.FloorToInt((max.y - t.bmin.y) / BucketM));
                    for (int bz = bz0; bz <= bz1; bz++)
                        for (int bx = bx0; bx <= bx1; bx++)
                        {
                            var l = t.buckets[bz * BN + bx];
                            if (l == null) continue;
                            foreach (var h in l) if (h.stamp != stampNow) { h.stamp = stampNow; into.Add(h); }
                        }
                }
        }
        static int stampNow;

        static bool InRing(Vector2[] ring, Vector2 q)
        {
            bool c = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                Vector2 a = ring[i], b = ring[j];
                if ((a.y > q.y) != (b.y > q.y) && q.x < (b.x - a.x) * (q.y - a.y) / (b.y - a.y) + a.x) c = !c;
            }
            return c;
        }

        /// <summary>Separating axes: a convex quad against an oriented box.</summary>
        static bool QuadHitsBox(Vector2[] q, Vector2 c, Vector2 u, float hu, float hv)
        {
            var v = new Vector2(-u.y, u.x);
            k4[0] = c + u * hu + v * hv; k4[1] = c - u * hu + v * hv; k4[2] = c - u * hu - v * hv; k4[3] = c + u * hu - v * hv;
            return QuadsOverlap(q, k4);
        }

        static bool QuadsOverlap(Vector2[] a, Vector2[] b)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var P = pass == 0 ? a : b;
                for (int i = 0; i < 4; i++)
                {
                    var e = P[(i + 1) & 3] - P[i];
                    var ax = new Vector2(-e.y, e.x);
                    float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                    for (int k = 0; k < 4; k++)
                    {
                        float pa = Vector2.Dot(a[k], ax), pb = Vector2.Dot(b[k], ax);
                        aMin = Mathf.Min(aMin, pa); aMax = Mathf.Max(aMax, pa); bMin = Mathf.Min(bMin, pb); bMax = Mathf.Max(bMax, pb);
                    }
                    if (aMax <= bMin + 1e-4f || bMax <= aMin + 1e-4f) return false;
                }
            }
            return true;
        }

        /// <summary>Every driveway that reaches the rectangle (decided now if
        /// it was not yet).</summary>
        public static void DrivewaysNear(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                         Vector2 min, Vector2 max, List<Driveway> into)
        {
            into.Clear();
            if (!DrivewaysOn || map == null) return;
            Bind(map, trims, buildings);
            DrivewaysNearBound(map, min, max, into);
        }

        /// <summary><see cref="DrivewaysNear"/> in the world a tile build has
        /// bound (<see cref="Bind"/>).</summary>
        public static void DrivewaysNearBound(CityMap map, Vector2 min, Vector2 max, List<Driveway> into)
        {
            into.Clear();
            if (!DrivewaysOn || map == null || map != mapB) return;
            float reach = StreetReachM + 20f;
            int tx0 = Mathf.FloorToInt((min.x - reach) / TileM), tx1 = Mathf.FloorToInt((max.x + reach) / TileM);
            int tz0 = Mathf.FloorToInt((min.y - reach) / TileM), tz1 = Mathf.FloorToInt((max.y + reach) / TileM);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                    foreach (var h in TileOf(tx, tz).houses)
                    {
                        if (!h.IsHome) continue;
                        if (h.c.x < min.x - reach || h.c.x > max.x + reach || h.c.y < min.y - reach || h.c.y > max.y + reach) continue;
                        var d = DrivewayOf(h);
                        if (d == null || d.max.x < min.x || d.min.x > max.x || d.max.y < min.y || d.min.y > max.y) continue;
                        into.Add(d);
                    }
        }

        /// <summary>The houses whose middle is in tile (tx, tz), blockers
        /// included (the house audit).</summary>
        public static List<House> HousesOf(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            Bind(map, trims, buildings);
            return TileOf(tx, tz).houses;
        }

        /// <summary>Is a plan point on a driveway (or its curb cut), within
        /// <paramref name="pad"/>? Of the list given.</summary>
        public static bool OnAny(List<Driveway> list, Vector2 p, float pad, bool flare)
        {
            foreach (var d in list)
            {
                if (p.x < d.min.x - pad || p.x > d.max.x + pad || p.y < d.min.y - pad || p.y > d.max.y + pad) continue;
                if (InConvex(d.quad, p, pad) || (flare && InConvex(d.flare, p, pad))) return true;
            }
            return false;
        }

        /// <summary>Inside an anticlockwise convex quad grown by <paramref name="pad"/>.</summary>
        public static bool InConvex(Vector2[] q, Vector2 p, float pad)
        {
            for (int i = 0; i < 4; i++)
            {
                var a = q[i]; var e = q[(i + 1) & 3] - a;
                float l = e.magnitude;
                if (l < 1e-5f) continue;
                if (Cross(e, p - a) / l < -pad) return false;
            }
            return true;
        }

        /// <summary>The least plan gap between two oriented boxes (their
        /// corners against each other's walls; 0 where they touch).</summary>
        static float MinBoxGap(House a, House b)
        {
            float g = float.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                var p = pass == 0 ? a : b; var o = pass == 0 ? b : a; var v = p.V;
                for (int k = 0; k < 4; k++)
                {
                    var q = p.c + p.u * ((k & 1) == 0 ? p.hu : -p.hu) + v * ((k & 2) == 0 ? p.hv : -p.hv);
                    g = Mathf.Min(g, BoxDist(o, q));
                }
            }
            return g;
        }

        // ------------------------------------------------------------------
        //  THE SEATS: a house on its own pad
        // ------------------------------------------------------------------

        /// <summary>How far the ground may stand over a procedural house's
        /// floor at its highest wall (a neighbour's pad uphill): past it the
        /// house is lifted, its siding running down to the low side's ground.</summary>
        public const float FloorBuryM = 0.6f;
        /// <summary>The same for a prefab, past its own plinth's allowance:
        /// its back sits this much further into a neighbour's higher ground.</summary>
        public const float PropBackBuryM = 0.5f;

        /// <summary>The pad of the house standing at <paramref name="c"/>
        /// (its table centre: a real footprint's own, a lot's position, a fill
        /// house's); NaN where none.</summary>
        public static float PadAt(CityMap map, Vector2 c)
        {
            if (!PadsOn || map != mapB) return float.NaN;
            var t = TileOf(Mathf.FloorToInt(c.x / TileM), Mathf.FloorToInt(c.y / TileM));
            foreach (var h in t.houses)
                if (h.IsHome && (h.c - c).sqrMagnitude < 1e-4f) return PadOf(h);
            return float.NaN;
        }

        /// <summary>
        /// A procedural house's floor (where its siding starts its storeys)
        /// and the lowest drawn ground at its walls (where the walls stop, so
        /// none stands over air): on its own pad, never more than
        /// <see cref="FloorBuryM"/> under the highest ground at its walls nor
        /// below the lowest.
        /// </summary>
        public static float Floor(CityMap map, Vector2 key, Vector2 c, Vector2 u, float hu, float hv, out float lo)
        {
            GroundRange(map, c, u, hu, hv, out lo, out float hi);
            float P = PadAt(map, key);
            float floor = float.IsNaN(P) ? lo : Mathf.Min(P, hi);
            return Mathf.Max(floor, Mathf.Max(lo, hi - FloorBuryM));
        }

        /// <summary>
        /// A prefab home's seat between the ground at its walls (<paramref name="lo"/>
        /// .. <paramref name="hi"/>): on its own pad where it has one, never
        /// higher than its plinth allows over the high corner (<paramref name="bury"/>)
        /// nor sunk more than <see cref="PropBackBuryM"/> past that, never
        /// under the lowest ground.
        /// </summary>
        public static float PropSeatOn(CityMap map, Vector2 key, float lo, float hi, float bury)
        {
            float seatMax = Mathf.Max(hi - bury, lo);
            float P = PadAt(map, key);
            if (float.IsNaN(P)) return seatMax;
            float seatMin = Mathf.Max(lo, hi - bury - PropBackBuryM);
            return Mathf.Clamp(P, seatMin, seatMax);
        }
    }
}
