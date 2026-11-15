using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  LOT ENTRANCES (2026-11-15; the owner on Beatties Ford Road: "I see
    //  many parking lots with no driveable entrance. They just have curbs
    //  all around.")
    //
    //  THE CAUSE. Section LENT (tools/city/lib/lots.mjs) gives a lot an
    //  entrance only on a LOCAL or TERTIARY street (rank <= 1) - where one
    //  of its own parking aisles ends within 6 m of it, else at the street
    //  point nearest a ring corner within 25 m. A lot whose only street is
    //  an arterial (Beatties Ford Road is one) got none: the walk and its
    //  15 cm curb ran unbroken across its front. And an entrance the data
    //  did give stopped at the apron, 2.5 m past the pavement: a lot
    //  standing further back kept its own kerb (CityMeshes.Walks'
    //  EmitLotCurbs: every ring edge more than walk + 3 m from a road) and
    //  a strip of grass between it and the apron.
    //
    //  THE RULE (InstallLotEntrances, once per map at the end of
    //  CityMap.Parse, from the LOTS / LENT / road data alone - the road
    //  graph is not touched):
    //   - every lot that no entrance of the data reaches by a clear 3 m
    //     path (LotPath: the walk's curb cut, paved all the way, no lot
    //     kerb, island, building, stall row or other road across it) and
    //     that touches no lot that has one gets its own;
    //   - first the data's entrance, where its path is only short of paving
    //     or shut by the lot's kerb (REPAIRED: the corridor below); else
    //     DERIVED on the lot's street frontage: the lowest class of street
    //     it fronts (a trunk road only when there is no other; never a
    //     freeway, ramp, bridge, tunnel or raised stretch), 15 m or more
    //     from a junction corner, in the middle of the frontage - two (at
    //     its quarter points) where the frontage is over 60 m;
    //   - each entrance is the data's kind (CityMap.LotEntrance: the
    //     street's curb and gutter drop to the 2 cm lip with 5 m wings, the
    //     walk carries across, no lamp, tree, inlet or bench on it), plus a
    //     CORRIDOR from the pavement to the lot: paving (a lot rectangle,
    //     as a filling station's driveway is) where the lot stands back,
    //     and a THROAT where it meets the lot's ring, which opens the
    //     lot's own kerb (ApronAt is true on both).
    //  PSX_CITY_LOTENT=0: the lots and entrances as the data has them.
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool LotEntOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LOTENT") != "0";

        /// <summary>Off a junction corner (and the relaxed pass).</summary>
        const float LotEntCornerM = 15f, LotEntCornerMinM = 6f;
        /// <summary>How far from the pavement a lot may stand (and the last pass).</summary>
        const float LotEntReachM = 30f, LotEntFarReachM = 60f;
        /// <summary>A lot with no street nearer takes a DRIVE up to this long (paved, 7.3 m): the
        /// service road OSM has there is not in the road graph.</summary>
        const float LotEntDriveReachM = 150f;
        /// <summary>The path a car needs: half its width, and the clear run inside the lot.</summary>
        const float LotEntCarHalf = 1.5f, LotEntInM = 3.0f;
        /// <summary>The lot's kerb opens this far past the corridor either side
        /// (EmitLotCurbs lays 4 m pieces, tested at their middles).</summary>
        const float LotEntThroatM = 2.3f;
        /// <summary>A frontage over this takes two entrances.</summary>
        const float LotEntTwoM = 60f;
        const float LotEntStepS = 5f;

        public static class LotEntStats
        {
            public static int lots, usableBefore, served, repaired, derived, derivedSecond, onTrunk, nearJunction, throughStalls, farReach, longDrive, left, connectors, throats;
            public static float ms, msUsable, msTouch, msDerive;
            public static readonly List<string> leftAt = new List<string>();
            public static void Reset() { lots = usableBefore = served = repaired = derived = derivedSecond = onTrunk = nearJunction = throughStalls = farReach = longDrive = left = connectors = throats = 0; ms = msUsable = msTouch = msDerive = 0f; leftAt.Clear(); }
            public static string Line() =>
                $"lot entrances ({(LotEntOn ? "ON" : "OFF (PSX_CITY_LOTENT=0)")}): {lots} lots; {usableBefore} had a usable entrance in the data, {served} reached through a lot that has one; " +
                $"{repaired} entrances of the data paved through to their lot, {derived} derived on the street frontage (+{derivedSecond} second ones on a frontage over {LotEntTwoM:0} m; " +
                $"{onTrunk} on a trunk road, {nearJunction} within {LotEntCornerM:0} m of a corner, {throughStalls} through a stall row (its lines taken out), {farReach} {LotEntReachM:0}-{LotEntFarReachM:0} m from the street, {longDrive} by a drive of {LotEntFarReachM:0}-{LotEntDriveReachM:0} m); " +
                $"{left} lots left with none; {connectors} paved corridors, {throats} kerb openings; {LotEntSource}, {ms:0} ms (the data's entrances {msUsable:0}, lots that join {msTouch:0}, frontage {msDerive:0})";
        }

        static CityMap lotEntMapFor;
        /// <summary>The corridors and throats (convex quads) of the map's
        /// repaired and derived entrances: ApronAt's, after the data's aprons.</summary>
        static readonly List<Vector2[]> lotEntQuads = new List<Vector2[]>();
        /// <summary>The first lot and entrance this pass added (-1: none).</summary>
        public static int LotEntLotsFrom = -1, LotEntEntrancesFrom = -1;

        /// <summary>A path from a street into a lot (LotPath).</summary>
        public struct LotPathInfo
        {
            /// <summary>Null when the path is clear.</summary>
            public string why;
            /// <summary>From the pavement's edge to the lot's asphalt, m; the
            /// ground on the way that is neither lot nor apron, m.</summary>
            public float gap, unpaved;
            /// <summary>(heights) the steepest metre on the way, and the lot over the street.</summary>
            public float slope, rise;
            /// <summary>(stalls not asked for) a stall row lies across the entrance.</summary>
            public bool stallsAcross;
            public Vector2 mouth, n, t;
        }

        static readonly HashSet<int> lePathSegs = new HashSet<int>();

        static void LotEntFrame(CityMap.Edge e, float s, int side, out Vector2 mouth, out Vector2 n, out Vector2 t)
        {
            s = Mathf.Clamp(s, 0f, e.length);
            var p = e.PointAt(s);
            t = e.TangentAt(s);
            var left = LineModel.SectionLeft(e, s);
            LineModel.Extents(e, s, out float eMinus, out float ePlus);
            n = left * side;
            mouth = p + n * (side > 0 ? ePlus : eMinus);
        }

        /// <summary>The lot's pavement (not a hole, not an island) at a plan point.</summary>
        static bool LotPavedBy(CityMap map, int li, Vector2 q)
        {
            var b = map.lots[li].box;
            if (q.x < b.x || q.x > b.z || q.y < b.y || q.y > b.w) return false;
            return LotClass(map, li, q, false) == 1;
        }

        /// <summary>Any lot's pavement at a plan point (-1: none).</summary>
        static int LotPavementAt(CityMap map, Vector2 q)
        {
            var ll = LotsInTile(map, Mathf.FloorToInt(q.x / TileSize), Mathf.FloorToInt(q.y / TileSize));
            if (ll == null) return -1;
            foreach (int li in ll) if (LotPavedBy(map, li, q)) return li;
            return -1;
        }

        static bool InStallBand(CityMap.Lot lot, Vector2 q)
        {
            for (int r = 0; r < lot.runFoot.Length; r++)
            {
                var u = lot.runU != null && lot.runU.Length > r ? lot.runU[r] : lot.u;
                var v = new Vector2(-u.y, u.x);
                var d = q - lot.runFoot[r];
                float a = Vector2.Dot(d, u), b = Vector2.Dot(d, v);
                if (a > -0.2f && a < lot.runN[r] * StallW + 0.2f && b > -0.1f && b < StallD + 0.1f) return true;
            }
            return false;
        }

        /// <summary>
        /// THE PATH from the street at (e, s, side), <paramref name="o"/>
        /// metres along it, square off the pavement into lot <paramref name="li"/>:
        /// 3 m wide. Plan only: no building on it, no other road across it,
        /// the lot reached within <paramref name="reach"/> and 3 m of its
        /// asphalt clear of islands, holes and (<paramref name="stalls"/>)
        /// stall rows. <paramref name="asBuilt"/>: and as the tile builder
        /// lays it - the walk's curb cut, paving all the way, no lot kerb
        /// across it. <paramref name="heights"/>: and no metre steeper than 20%.
        /// </summary>
        public static bool LotPath(CityMap map, CityMap.Edge e, float s, int side, float o, int li, float reach, bool stalls, bool asBuilt, bool heights, out LotPathInfo info)
        {
            info = default;
            LotEntFrame(e, s, side, out var mouth, out var n, out var t);
            mouth += t * o;
            info.mouth = mouth; info.n = n; info.t = t;
            var lot = map.lots[li];
            // the lot's asphalt along the centre line
            var bx = lot.box;
            float dIn = -1f;
            {
                // where the ray first crosses the ring (or starts inside it), then on to asphalt every 0.25 m
                var ring = lot.ring;
                float dx = float.MaxValue;
                if (LotPavedBy(map, li, mouth + n * 0.01f)) dx = 0f;
                else
                    for (int k = 0; k < ring.Length; k++)
                    {
                        Vector2 a = ring[k], ab = ring[(k + 1) % ring.Length] - a;
                        float den = n.x * ab.y - n.y * ab.x;
                        if (Mathf.Abs(den) < 1e-9f) continue;
                        var am = a - mouth;
                        float d = (am.x * ab.y - am.y * ab.x) / den, u = (am.x * n.y - am.y * n.x) / den;
                        if (d >= 0f && u >= 0f && u <= 1f && d < dx) dx = d;
                    }
                if (dx <= reach)
                    for (float d = dx == 0f ? 0f : Mathf.Ceil(dx * 4f + 0.01f) * 0.25f; d <= Mathf.Min(reach, dx + 12f) + 0.01f && dIn < 0f; d += 0.25f)
                        if (LotPavedBy(map, li, mouth + n * d)) dIn = d;
            }
            if (dIn < 0f) { info.why = "the lot is not reached"; return false; }
            info.gap = dIn;
            // nothing on the way: a building, another road
            float clearR = LotEntCarHalf + 0.3f;
            for (float d = 0.75f; d < dIn + LotEntInM; d += dIn > 20f ? 1.5f : 0.75f)
                if (!map.FootprintClear(mouth + n * d, dIn > 20f ? clearR + 0.5f : clearR)) { info.why = "a building on the way"; return false; }
            if (dIn > 1.5f)
            {
                var pa = mouth + n * 1.0f; var pb = mouth + n * dIn;
                lePathSegs.Clear();
                map.EdgeSegsInRect(Vector2.Min(pa, pb) - Vector2.one * 20f, Vector2.Max(pa, pb) + Vector2.one * 20f, lePathSegs);
                foreach (int packed in lePathSegs)
                {
                    int ei = packed >> 12, si = packed & 0xFFF;
                    if (ei == e.index) continue;
                    var oe = map.edges[ei];
                    if (oe.tunnel || si + 1 >= oe.pts.Length) continue;
                    float sm = 0.5f * (oe.s[si] + oe.s[si + 1]);
                    if (oe.bridge || oe.ElevatedAt(sm)) continue;   // a deck overhead
                    if (RoadsideOccupancy.SegSegDistance(pa, pb, oe.pts[si], oe.pts[si + 1]) < oe.HalfMax + 0.5f) { info.why = "another road on the way"; return false; }
                }
            }
            if (dIn > 6f)
            {
                var pa = mouth + n * 1.0f; var pb = mouth + n * dIn;
                lePathSegs.Clear();
                map.WaterSegsInRect(Vector2.Min(pa, pb) - Vector2.one * 12f, Vector2.Max(pa, pb) + Vector2.one * 12f, lePathSegs);
                foreach (int packed in lePathSegs)
                {
                    var w = map.waters[packed >> 12]; int si = packed & 0xFFF;
                    if (si + 1 >= w.pts.Length) continue;
                    if (RoadsideOccupancy.SegSegDistance(pa, pb, w.pts[si], w.pts[si + 1]) < (w.lake ? 1f : w.width * 0.5f + 3f)) { info.why = "water on the way"; return false; }
                }
                for (float d = 3f; d < dIn; d += 3f)
                    if (map.InLake(mouth + n * d) || map.NearRavine(mouth + n * d, 3f)) { info.why = "water on the way"; return false; }
            }
            // inside: 3 m of the lot's own asphalt (or a lot's it joins), no island, no stall row
            for (float dd = 1.0f; dd <= LotEntInM + 1e-3f; dd += 1.0f)
                for (int k = -1; k <= 1; k++)
                {
                    var q = mouth + n * (dIn + dd) + t * (k * LotEntCarHalf);
                    int cls = bx.x <= q.x && q.x <= bx.z && bx.y <= q.y && q.y <= bx.w ? LotClass(map, li, q, false) : 0;
                    if (cls == 2) { info.why = "an island inside the entrance"; return false; }
                    if (cls == 0)
                    {
                        int other = LotPavementAt(map, q);
                        if (other < 0) { info.why = "the lot is under 3 m deep or 3 m wide there"; return false; }
                        if (InStallBand(map.lots[other], q)) info.stallsAcross = true;
                    }
                    else if (InStallBand(lot, q)) info.stallsAcross = true;
                }
            if (stalls && info.stallsAcross) { info.why = "a stall row across the entrance"; return false; }
            if (asBuilt)
            {
                // the street's curb: cut where the side has a walk
                if (WalkSide(map, e, side))
                    for (int k = -1; k <= 1; k++)
                    {
                        var q = mouth + n * (0.5f * WalkW) + t * (k * LotEntCarHalf);
                        if (!ApronAt(map, q.x, q.y)) { info.why = "the street's curb is not cut"; return false; }
                    }
                // paved all the way (the apron reaches 2.5 m)
                for (float d = 2.5f; d < dIn; d += 0.25f)
                {
                    var q = mouth + n * d;
                    if (LotPavementAt(map, q) < 0 && !ApronAt(map, q.x, q.y)) info.unpaved += 0.25f;
                }
                if (info.unpaved > 1.5f) { info.why = "ground, not paving, between the street and the lot"; return false; }
                // the lot's own kerb across the path
                if (LotKerbAcross(map, li, mouth, n, t, dIn)) { info.why = "the lot's own kerb"; return false; }
            }
            if (heights)
            {
                float yPrev = float.NaN, y0 = e.YAt(Mathf.Clamp(s, 0f, e.length));
                for (float d = 2.5f; d <= dIn + LotEntInM; d += 1f)
                {
                    var q = mouth + n * d;
                    float y = LeGround(map, q);
                    if (!float.IsNaN(yPrev)) info.slope = Mathf.Max(info.slope, Mathf.Abs(y - yPrev));
                    yPrev = y;
                }
                var qi = mouth + n * (dIn + 1f);
                info.rise = LeGround(map, qi) - y0;
                if (info.slope > 0.20f) { info.why = "steeper than 20% on the way"; return false; }
            }
            return true;
        }

        /// <summary>(the audit) the trims and buildings the heights are read with.</summary>
        static Trims leTrims; static Dictionary<long, List<CityBuildings.B>> leProc;
        static float LeGround(CityMap map, Vector2 q) => leTrims != null ? SharedGroundAt(map, leTrims, leProc, q.x, q.y) : LatticeY(map, q.x, q.y);

        /// <summary>Does a kerb piece of the lot's ring (EmitLotCurbs' 4 m
        /// pieces, LotCurbHere's rule) lie across the 3 m path?</summary>
        static bool LotKerbAcross(CityMap map, int li, Vector2 mouth, Vector2 n, Vector2 t, float dIn)
        {
            if (!WalksOn || !LotCurbsOn) return false;
            var lot = map.lots[li];
            if (lot.aisleOnly) return false;
            var r = lot.ring; int nr = r.Length;
            float area = 0f;
            for (int i = 0; i < nr; i++) { var p = r[i]; var q = r[(i + 1) % nr]; area += p.x * q.y - q.x * p.y; }
            float sgn = area > 0f ? 1f : -1f;
            for (int i = 0; i < nr; i++)
            {
                Vector2 a = r[i], b = r[(i + 1) % nr];
                var d = b - a; float len = d.magnitude;
                if (len < 0.05f) continue;
                var outw = new Vector2(d.y, -d.x) / len * sgn;
                int k = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
                for (int j = 0; j < k; j++)
                {
                    var pa = a + d * ((float)j / k); var pb = a + d * ((float)(j + 1) / k);
                    // in the path's frame: across (t), along (n)
                    var la = new Vector2(Vector2.Dot(pa - mouth, t), Vector2.Dot(pa - mouth, n));
                    var lb = new Vector2(Vector2.Dot(pb - mouth, t), Vector2.Dot(pb - mouth, n));
                    if (!SegHitsBox(la, lb, -LotEntCarHalf, 0.5f, LotEntCarHalf, dIn + 1.5f)) continue;
                    var m = 0.5f * (pa + pb);
                    var land = m + outw * 0.8f;
                    tileLots.Clear();
                    var ll = LotsInTile(map, Mathf.FloorToInt(land.x / TileSize), Mathf.FloorToInt(land.y / TileSize));
                    if (ll != null) tileLots.AddRange(ll);
                    bool kerb = LotCurbHere(map, li, m, outw);
                    tileLots.Clear();
                    if (kerb) return true;
                }
            }
            return false;
        }

        /// <summary>The first asphalt the path meets is this lot's own (not
        /// another lot's on the way; an aisle or a corridor may be).</summary>
        static bool LotPathFirstIs(CityMap map, in LotPathInfo info, int li)
        {
            for (float d = 1.0f; d < info.gap - 0.3f; d += info.gap > 20f ? 2f : 0.5f)
            {
                int other = LotPavementAt(map, info.mouth + info.n * d);
                if (other >= 0 && other != li && !map.lots[other].aisleOnly) return false;
            }
            return true;
        }

        // ---- THE RAMP behind the curb cut ------------------------------------
        /// <summary>The slope of the ground behind an entrance's curb cut, down
        /// (or up) to the lot: 1 in 14.</summary>
        public const float LotEntRampSlope = 1f / 14f;
        /// <summary>The ramp holds across the cut and this far either side, then
        /// eases back to the verge's own slope over <see cref="LotEntRampEaseM"/>.</summary>
        const float LotEntRampSideM = 1.2f, LotEntRampEaseM = 4f;
        static Vector4[] lotEntRampA, lotEntRampB;   // per entrance: mouth x, z, t x, z; n x, z, half, 0
        static Dictionary<long, List<int>> lotEntRampByTile;
        static CityMap lotEntRampMapFor;

        static void EnsureLotEntRamps(CityMap map)
        {
            int na = map.lotEntrances != null ? map.lotEntrances.Length : 0;
            if (lotEntRampMapFor == map && lotEntRampByTile != null && lotEntRampA.Length == na) return;
            lock (lotEntRampLock) { if (!(lotEntRampMapFor == map && lotEntRampByTile != null && lotEntRampA.Length == na)) BuildLotEntRamps(map, na); }
        }
        static readonly object lotEntRampLock = new object();

        /// <summary>(Built at the first strip a tile lays, not at the pass: the roads' eases - a turn
        /// bay, a taper - are in the pavement's extents only after ComputeTrims.)</summary>
        static void BuildLotEntRamps(CityMap map, int na)
        {
            var A = new Vector4[na]; var B = new Vector4[na];
            var by = new Dictionary<long, List<int>>();
            for (int i = 0; i < na; i++)
            {
                var en = map.lotEntrances[i];
                LotEntFrame(map.edges[en.edge], en.s, en.side, out var mouth, out var n, out var t);
                A[i] = new Vector4(mouth.x, mouth.y, t.x, t.y); B[i] = new Vector4(n.x, n.y, en.half, 0f);
                float r = en.half + LotEntRampSideM + LotEntRampEaseM + 5f;
                for (int tx = Mathf.FloorToInt((mouth.x - r) / TileSize); tx <= Mathf.FloorToInt((mouth.x + r) / TileSize); tx++)
                    for (int tz = Mathf.FloorToInt((mouth.y - r) / TileSize); tz <= Mathf.FloorToInt((mouth.y + r) / TileSize); tz++)
                    {
                        long k = LotTileKey(tx, tz);
                        if (!by.TryGetValue(k, out var l)) by[k] = l = new List<int>(4);
                        l.Add(i);
                    }
            }
            lotEntRampByTile = null; lotEntRampA = A; lotEntRampB = B; lotEntRampMapFor = map; lotEntRampByTile = by;
        }

        /// <summary>
        /// THE RAMP (SolveStrip's one call). The ground behind a walk (its
        /// backing) and the verge of a street with none came down to the lot
        /// - cut flush into the lattice, which the road's corridor holds 10 cm
        /// under the tarmac - at the fill's 1V:4H: behind every curb cut a
        /// 13-14 cm drop within half a metre (measured on the colliders at
        /// North Poplar Street's lot aprons, 28%), which a wheel at 40 km/h
        /// leaves the ground over. Where the strip starts within a lot
        /// entrance's cut (its width + 1.2 m either side, from the pavement's
        /// edge to 4 m out) it is THE APRON'S RAMP instead: no level run, 1 in
        /// 14 to the lot (concrete: ApronAt is true on the corridor), easing
        /// back to the plain slope over 4 m either side as the curb cut's own
        /// wings do. PSX_CITY_LOTENT=0: as before.
        /// </summary>
        static void LotEntRamp(CityMap map, Vector2 s0, Vector2 outw, float y0, float run, ref float shoulder, ref float slope)
        {
            if (!LotEntOn || !LotsOn || map.lotEntrances == null || map.lotEntrances.Length == 0) return;
            EnsureLotEntRamps(map);
            if (!lotEntRampByTile.TryGetValue(LotTileKey(Mathf.FloorToInt(s0.x / TileSize), Mathf.FloorToInt(s0.y / TileSize)), out var l)) return;
            float k = 0f;
            foreach (int i in l)
            {
                var a = lotEntRampA[i]; var b = lotEntRampB[i];
                float dx = s0.x - a.x, dz = s0.y - a.y;
                float dep = dx * b.x + dz * b.y;
                if (dep < -0.6f || dep > 4f) continue;
                float lat = Mathf.Abs(dx * a.z + dz * a.w) - (b.z + LotEntRampSideM);
                if (lat >= LotEntRampEaseM) continue;
                k = Mathf.Max(k, lat <= 0f ? 1f : 1f - LineModel.Smooth(lat / LotEntRampEaseM));
            }
            if (k <= 0f) return;
            // THE GRADE TO THE LOT: the first lot's asphalt out along the strip (not one of this pass's own
            // corridors) and the land's height just inside it. The strip runs straight from its start to
            // that height at the lot's edge - DOWN where the lot lies lower (never steeper than 1 in 6,
            // never shorter than 14 times the drop: a lot at the walk's back is met 2 m in), UP over the
            // road's sunk corridor where the lot stands higher (to 1 in 8): the lattice's own way up out
            // of the corridor is its 1V:3H cut slope, which threw a car at 40 km/h.
            float eLot = -1f;
            for (float e = 0f; e <= Mathf.Min(run, 30f) && eLot < 0f; e += 0.5f)
            {
                int li = LotPavementAt(map, s0 + outw * e);
                if (li >= 0 && (LotEntLotsFrom < 0 || li < LotEntLotsFrom || lotEntMapFor != map)) eLot = e;
            }
            float want;
            if (eLot >= 0f)
            {
                var q = s0 + outw * (eLot + 0.3f);
                float drop = y0 - LatticeY(map, q.x, q.y) + 0.02f;   // (2 cm under the lot: the strip tucks under its edge)
                want = Mathf.Clamp(drop / Mathf.Max(eLot + 0.3f, Mathf.Abs(drop) / LotEntRampSlope), -1f / 8f, 1f / 6f);
            }
            else
            {
                // no lot within reach along this strip (the cut's flare beside the corridor): the gentlest
                // ramp down that lands within 6 m; none that does: the strip as it was
                float reach = Mathf.Min(run, LotEntRampMaxM); want = -1f;
                foreach (float sl in lotEntRampSlopes)
                {
                    for (float e = 0.5f; e <= reach + 1e-3f && want < 0f; e += 0.5f)
                    {
                        var q = s0 + outw * e;
                        if (y0 - sl * e - LatticeY(map, q.x, q.y) <= 0f) want = sl;
                    }
                    if (want >= 0f) break;
                }
                if (want < 0f) return;
            }
            if (want >= slope) return;
            shoulder = Mathf.Lerp(shoulder, 0f, k);
            slope = Mathf.Lerp(slope, want, k);
        }
        const float LotEntRampMaxM = 6f;
        static readonly float[] lotEntRampSlopes = { LotEntRampSlope, 1f / 10f, 1f / 8f, 1f / 6f };

        /// <summary>Is the plan point on a lot entrance's way (its apron or
        /// corridor) or within <paramref name="pad"/> of it? No street lamp
        /// stands there (TryLamp: it kept off the houses' driveways only, so a
        /// post could stand in a lot's curb cut). PSX_CITY_LOTENT=0: as before.</summary>
        static bool LotEntOnWay(CityMap map, Vector2 p, float pad)
        {
            if (!LotEntOn || !LotsOn || map.lots == null) return false;
            EnsureLots(map);
            if (apronsByTile == null || lotMapFor != map) return false;
            for (int k = 0; k < 5; k++)
            {
                var q = k == 0 ? p : p + new Vector2(k == 1 ? pad : k == 2 ? -pad : 0f, k == 3 ? pad : k == 4 ? -pad : 0f);
                if (!apronsByTile.TryGetValue(LotTileKey(Mathf.FloorToInt(q.x / TileSize), Mathf.FloorToInt(q.y / TileSize)), out var l)) continue;
                foreach (int i in l)
                {
                    var Q = apronQuads[i];
                    bool inside = true; float s0 = 0f;
                    for (int c = 0; c < 4 && inside; c++)
                    {
                        var a = Q[c]; var b = Q[(c + 1) % 4];
                        float s = (b.x - a.x) * (q.y - a.y) - (b.y - a.y) * (q.x - a.x);
                        if (c == 0) s0 = s; else if (s * s0 < 0f) inside = false;
                    }
                    if (inside) return true;
                }
            }
            return false;
        }

        // ---- the entrances by tile (the pass and the audit) ------------------
        static Dictionary<long, List<int>> leEntByTile;
        static int leEntIndexed;
        static void LotEntIndex(CityMap map)
        {
            leEntByTile = new Dictionary<long, List<int>>();
            int na = map.lotEntrances != null ? map.lotEntrances.Length : 0;
            leEntIndexed = na;
            for (int i = 0; i < na; i++)
            {
                var en = map.lotEntrances[i];
                LotEntFrame(map.edges[en.edge], en.s, en.side, out var mouth, out _, out _);
                long k = LotTileKey(Mathf.FloorToInt(mouth.x / TileSize), Mathf.FloorToInt(mouth.y / TileSize));
                if (!leEntByTile.TryGetValue(k, out var l)) leEntByTile[k] = l = new List<int>(4);
                l.Add(i);
            }
        }

        static readonly List<int> leNear = new List<int>(32);
        /// <summary>The entrances whose mouth is within <paramref name="r"/> of the lot's box.</summary>
        static void LotEntNear(CityMap map, Vector4 b, float r, List<int> into)
        {
            into.Clear();
            for (int tx = Mathf.FloorToInt((b.x - r) / TileSize); tx <= Mathf.FloorToInt((b.z + r) / TileSize); tx++)
                for (int tz = Mathf.FloorToInt((b.y - r) / TileSize); tz <= Mathf.FloorToInt((b.w + r) / TileSize); tz++)
                    if (leEntByTile.TryGetValue(LotTileKey(tx, tz), out var l)) into.AddRange(l);
        }

        static readonly float[] leOffsets = { 0f, 1f, -1f, 2f, -2f };

        /// <summary>Has the lot an entrance of the data a car can use (as
        /// built)? <paramref name="reached"/>: one whose path reaches it at
        /// all; <paramref name="best"/> the first that does, <paramref name="why"/> what stops it.</summary>
        public static bool LotHasUsableEntrance(CityMap map, int li, bool heights, out bool reached, out int best, out string why)
        {
            reached = false; best = -1; why = "no entrance in the data";
            LotEntNear(map, map.lots[li].box, LotEntFarReachM + 8f, leNear);
            foreach (int ei in leNear)
            {
                var en = map.lotEntrances[ei];
                var e = map.edges[en.edge];
                foreach (float o in leOffsets)
                {
                    if (Mathf.Abs(o) > en.half - LotEntCarHalf + 0.01f) continue;
                    bool ok = LotPath(map, e, en.s, en.side, o, li, LotEntFarReachM, true, true, heights, out var info);
                    if (ok) { reached = true; best = ei; why = null; return true; }
                    if (info.why != "the lot is not reached" && !reached && info.gap <= LotEntReachM && LotPathFirstIs(map, info, li)) { reached = true; best = ei; why = info.why; }
                }
            }
            return false;
        }

        /// <summary>Do two lots join (one's edge on the other's asphalt, no
        /// kerb or building between)?</summary>
        static bool LotsTouch(CityMap map, int la, int lb)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int li = pass == 0 ? la : lb, lj = pass == 0 ? lb : la;
                var r = map.lots[li].ring; int nr = r.Length;
                var bj = map.lots[lj].box;
                float area = 0f;
                for (int i = 0; i < nr; i++) { var p = r[i]; var q = r[(i + 1) % nr]; area += p.x * q.y - q.x * p.y; }
                float sgn = area > 0f ? 1f : -1f;
                for (int i = 0; i < nr; i++)
                {
                    Vector2 a = r[i], b = r[(i + 1) % nr];
                    var d = b - a; float len = d.magnitude;
                    if (len < 0.05f) continue;
                    if (Mathf.Max(a.x, b.x) < bj.x - 1f || Mathf.Min(a.x, b.x) > bj.z + 1f || Mathf.Max(a.y, b.y) < bj.y - 1f || Mathf.Min(a.y, b.y) > bj.w + 1f) continue;
                    var outw = new Vector2(d.y, -d.x) / len * sgn;
                    int k = Mathf.Max(1, Mathf.CeilToInt(len / 3f));
                    int hits = 0;
                    for (int j = 0; j < k; j++)
                    {
                        var m = a + d * ((j + 0.5f) / k);
                        if (LotPavedBy(map, lj, m + outw * 0.8f) && map.FootprintClear(m, 1.0f)) { if (++hits >= 1 && len / k >= 2.5f || hits >= 2) return true; }
                        else hits = 0;
                    }
                }
            }
            return false;
        }

        // ---- the pass --------------------------------------------------------
        struct LeRun { public int edge, side, cls; public float s0, s1, minGap; public bool trunk; public List<float> ss; }
        struct LeSample { public int edge, side; public float s, gap; public int tier; }
        static readonly HashSet<int> leSegs = new HashSet<int>();
        static readonly SortedDictionary<int, Vector2> leEdgeRange = new SortedDictionary<int, Vector2>();
        static readonly List<LeSample> leSamples = new List<LeSample>(256);

        /// <summary>How far along an edge from a node its corner is taken to
        /// be (plan: the widest other road there, half of it, + 3 m of curb
        /// return; 0 where the road only runs on).</summary>
        static float LotEntCornerAt(CityMap map, CityMap.Edge e, int node)
        {
            var ne = map.nodeEdges[node];
            if (ne.Count < 3) return 0f;
            float w = 0f;
            foreach (int oi in ne) if (oi != e.index) w = Mathf.Max(w, map.edges[oi].HalfMax);
            return w + 3f;
        }

        /// <summary>
        /// The lot's street frontage: every place (each 2 m) on an eligible
        /// street within <paramref name="reach"/> a clear path leads from into
        /// the lot, with its TIER - 0 clear of stalls, 15 m off a corner, no
        /// trunk road, within 30 m; 1 through a stall row; 2 nearer a corner
        /// (6 m); 3 a trunk road; 4 within 60 m; 5 a drive up to 150 m - and
        /// the runs of the best tier there is.
        /// </summary>
        static void LotFrontage(CityMap map, int li, float reach, List<LeRun> runs, out int bestTier)
        {
            runs.Clear(); leSamples.Clear(); bestTier = 99;
            var lot = map.lots[li]; var b = lot.box; var ring = lot.ring;
            leSegs.Clear(); leEdgeRange.Clear();
            float R = reach + 16f;
            map.EdgeSegsInRect(new Vector2(b.x - R, b.y - R), new Vector2(b.z + R, b.w + R), leSegs);
            foreach (int packed in leSegs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.link || e.tunnel || e.bridge || e.a == e.b || e.cls >= 5 || si + 1 >= e.pts.Length || e.length < 12f) continue;
                // near the ring?
                Vector2 a = e.pts[si], ab = e.pts[si + 1] - a;
                float lim = reach + e.HalfMax + 1f;
                if (Mathf.Max(a.x, a.x + ab.x) < b.x - lim || Mathf.Min(a.x, a.x + ab.x) > b.z + lim || Mathf.Max(a.y, a.y + ab.y) < b.y - lim || Mathf.Min(a.y, a.y + ab.y) > b.w + lim) continue;
                bool near = false;
                for (int k = 0; k < ring.Length && !near; k++)
                    if (RoadsideOccupancy.SegSegDistance(a, a + ab, ring[k], ring[(k + 1) % ring.Length]) < lim) near = true;
                if (!near) continue;
                var rg = new Vector2(e.s[si], e.s[si + 1]);
                if (leEdgeRange.TryGetValue(ei, out var have)) rg = new Vector2(Mathf.Min(have.x, rg.x), Mathf.Max(have.y, rg.y));
                leEdgeRange[ei] = rg;
            }
            foreach (var kv in leEdgeRange)
            {
                var e = map.edges[kv.Key];
                float half = e.oneway ? 2.15f : 3.65f;
                float cA = LotEntCornerAt(map, e, e.a), cB = LotEntCornerAt(map, e, e.b);
                float lo = Mathf.Max(kv.Value.x, Mathf.Max(half + 2.5f, cA > 0f ? cA + LotEntCornerMinM : 0f));
                float hi = Mathf.Min(kv.Value.y, e.length - Mathf.Max(half + 2.5f, cB > 0f ? cB + LotEntCornerMinM : 0f));
                if (hi < lo) continue;
                float step = reach > LotEntFarReachM ? 2f * LotEntStepS : LotEntStepS;
                int n = Mathf.Max(1, Mathf.CeilToInt((hi - lo) / step));
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k <= n; k++)
                    {
                        float s = lo + (hi - lo) * k / n;
                        if (e.ElevatedAt(s)) continue;
                        // (only the side the lot's box is on)
                        {
                            var sp = e.PointAt(s); var st = e.TangentAt(s);
                            float c0 = st.x * (b.y - sp.y) - st.y * (b.x - sp.x), c1 = st.x * (b.y - sp.y) - st.y * (b.z - sp.x);
                            float c2 = st.x * (b.w - sp.y) - st.y * (b.x - sp.x), c3 = st.x * (b.w - sp.y) - st.y * (b.z - sp.x);
                            if (side > 0 ? Mathf.Max(Mathf.Max(c0, c1), Mathf.Max(c2, c3)) <= 0f : Mathf.Min(Mathf.Min(c0, c1), Mathf.Min(c2, c3)) >= 0f) continue;
                        }
                        if (!LotPath(map, e, s, side, 0f, li, reach, false, false, false, out var info) || !LotPathFirstIs(map, info, li)) continue;
                        int tier = info.stallsAcross ? 1 : 0;
                        if ((cA > 0f && s < cA + LotEntCornerM) || (cB > 0f && s > e.length - cB - LotEntCornerM)) tier = 2;
                        if (e.cls == 4) tier = 3;
                        if (info.gap > LotEntReachM) tier = 4;
                        if (info.gap > LotEntFarReachM) tier = 5;
                        if (tier < bestTier) bestTier = tier;
                        leSamples.Add(new LeSample { edge = e.index, side = side, s = s, gap = info.gap, tier = tier });
                    }
            }
            // the runs of the best tier (samples come in edge, side, s order)
            LeRun run = default; bool open = false; float lastS = 0f;
            foreach (var sm in leSamples)
            {
                bool ok = sm.tier <= bestTier;
                if (open && (!ok || sm.edge != run.edge || sm.side != run.side || sm.s - lastS > (sm.gap > LotEntFarReachM ? 2f : 1f) * LotEntStepS * 1.5f)) { runs.Add(run); open = false; }
                if (!ok) continue;
                if (!open) { run = new LeRun { edge = sm.edge, side = sm.side, cls = map.edges[sm.edge].cls, s0 = sm.s, s1 = sm.s, minGap = sm.gap, trunk = map.edges[sm.edge].cls == 4, ss = new List<float>(8) }; open = true; }
                run.s1 = sm.s; run.minGap = Mathf.Min(run.minGap, sm.gap); run.ss.Add(sm.s); lastS = sm.s;
            }
            if (open) runs.Add(run);
        }

        static float LeRunRank(in LeRun r) =>
            (r.trunk ? 1000f : 0f) + (r.minGap <= 12f ? 0f : 200f) + r.cls * 30f + r.minGap - Mathf.Min(r.s1 - r.s0, 60f) * 0.2f;

        static CityMap.Lot LeNewLot(Vector2[] ring, Vector2 u) => new CityMap.Lot
        {
            ring = ring, box = CityStations.BoxOf(ring), u = u,
            runFoot = System.Array.Empty<Vector2>(), runN = System.Array.Empty<byte>(), runU = System.Array.Empty<Vector2>(),
            aisleOnly = true,
        };

        /// <summary>The corridor of an entrance into lot <paramref name="li"/>:
        /// its paving (a lot rectangle from 1 m past the pavement to 0.5 m
        /// inside the lot, where the lot stands back), the corridor's quad
        /// and the throat along the lot's ring.</summary>
        static void LotEntCorridor(CityMap map, CityMap.Edge e, float s, int side, float half, int li, float gap, List<CityMap.Lot> lots)
        {
            LotEntFrame(e, s, side, out var mouth, out var n, out var t);
            var lot = map.lots[li];
            // from INSIDE the pavement as this pass reads it: the road's eases (a turn bay, a taper: after
            // ComputeTrims) move its drawn edge a lane either way, and the tile cuts the lot along the
            // pavement as drawn (LotRoadCutOn), so the paving always starts at the real edge
            float a0 = -3.5f, a1 = gap + CityStations.PaveOverlapM;
            if (gap > CityStations.DriveFromEdgeM)
            {
                var ring = CityStations.Corners(mouth, n, t, a0, a1, -half, half);
                if (Cross2(ring[1] - ring[0], ring[2] - ring[1]) < 0f) System.Array.Reverse(ring);
                // the lot's own asphalt (IsFresh reads a ring's first point)
                bool want = IsFresh(lot.ring[0]);
                for (int k = 0; k < 4 && IsFresh(ring[0]) != want; k++) { var f = ring[0]; ring[0] = ring[1]; ring[1] = ring[2]; ring[2] = ring[3]; ring[3] = f; }
                for (int k = 1; k < 40 && IsFresh(ring[0]) != want; k++) ring[0] += n * 0.011f;
                lots.Add(LeNewLot(ring, n));
                LotEntStats.connectors++;
            }
            // the corridor: the verge over it is the apron's concrete, nothing stands on it
            lotEntQuads.Add(new[] { mouth - t * half, mouth + t * half, mouth + n * (gap + 1f) + t * half, mouth + n * (gap + 1f) - t * half });
            // the throat: the lot's ring where the corridor (and 2.3 m either side) meets it
            var r = lot.ring; int nr = r.Length;
            float area = 0f;
            for (int i = 0; i < nr; i++) { var p = r[i]; var q = r[(i + 1) % nr]; area += p.x * q.y - q.x * p.y; }
            float sgn = area > 0f ? 1f : -1f;
            float w = half + LotEntThroatM;
            for (int i = 0; i < nr; i++)
            {
                Vector2 a = r[i], b = r[(i + 1) % nr];
                var d = b - a; float len = d.magnitude;
                if (len < 0.05f) continue;
                // the part of the edge within the widened corridor, near where the path comes in
                float u0 = 0f, u1 = 1f;
                float ta = Vector2.Dot(a - mouth, t), tb = Vector2.Dot(b - mouth, t);
                if (!ClipRange(ta, tb, -w, w, ref u0, ref u1)) continue;
                float da = Vector2.Dot(a - mouth, n), db = Vector2.Dot(b - mouth, n);
                if (!ClipRange(da, db, gap - 6f, gap + 6f, ref u0, ref u1)) continue;
                if ((u1 - u0) * len < 0.2f) continue;
                var outw = new Vector2(d.y, -d.x) / len * sgn;
                Vector2 pa = a + d * u0, pb = a + d * u1;
                lotEntQuads.Add(new[] { pa - outw * 0.4f, pb - outw * 0.4f, pb + outw * 1.7f, pa + outw * 1.7f });
                LotEntStats.throats++;
            }
        }

        /// <summary>The share [u0, u1] of a segment whose value (va at 0, vb
        /// at 1) lies in [lo, hi]; false when none.</summary>
        static bool ClipRange(float va, float vb, float lo, float hi, ref float u0, ref float u1)
        {
            float dv = vb - va;
            if (Mathf.Abs(dv) < 1e-6f) return va >= lo && va <= hi;
            float ua = (lo - va) / dv, ub = (hi - va) / dv;
            if (ua > ub) { float x = ua; ua = ub; ub = x; }
            u0 = Mathf.Max(u0, ua); u1 = Mathf.Min(u1, ub);
            return u1 > u0;
        }

        /// <summary>The stall lines of the lot on a derived entrance's way
        /// (its width + 0.5 m either side, 7 m in) are taken out: each run
        /// split round the gap.</summary>
        static void LotEntClearStalls(CityMap.Lot lot, Vector2 mouth, Vector2 n, Vector2 t, float half, float gap)
        {
            if (lot.runFoot.Length == 0) return;
            if (!lotEntStallsWas.ContainsKey(lot)) lotEntStallsWas[lot] = (lot.runFoot, lot.runN, lot.runU);
            var feet = new List<Vector2>(); var ns = new List<byte>(); var us = new List<Vector2>();
            bool changed = false;
            for (int r = 0; r < lot.runFoot.Length; r++)
            {
                var u = lot.runU != null && lot.runU.Length > r ? lot.runU[r] : lot.u;
                var v = new Vector2(-u.y, u.x);
                int cnt = lot.runN[r];
                int start = -1;
                for (int k = 0; k <= cnt + 1; k++)
                {
                    bool keep = false;
                    if (k <= cnt)
                    {
                        var f = lot.runFoot[r] + u * (k * StallW);
                        keep = true;
                        for (int q = 0; q <= 4 && keep; q++)
                        {
                            var p = f + v * (StallD * q / 4f) - mouth;
                            float lat = Vector2.Dot(p, t), dep = Vector2.Dot(p, n);
                            if (Mathf.Abs(lat) < half + 0.5f && dep > gap - 1f && dep < gap + 7f) keep = false;
                        }
                        if (!keep) changed = true;
                    }
                    if (keep && start < 0) start = k;
                    if (!keep && start >= 0)
                    {
                        feet.Add(lot.runFoot[r] + u * (start * StallW)); ns.Add((byte)(k - 1 - start)); us.Add(u);
                        start = -1;
                    }
                }
            }
            if (!changed) return;
            lot.runFoot = feet.ToArray(); lot.runN = ns.ToArray(); lot.runU = us.ToArray();
            lotEntStallsNow[lot] = (lot.runFoot, lot.runN, lot.runU);
        }
        static readonly Dictionary<CityMap.Lot, (Vector2[] foot, byte[] n, Vector2[] u)> lotEntStallsWas = new Dictionary<CityMap.Lot, (Vector2[], byte[], Vector2[])>(),
                                                                                      lotEntStallsNow = new Dictionary<CityMap.Lot, (Vector2[], byte[], Vector2[])>();

        /// <summary>For the tools' before/after from one map: the pass's lots,
        /// entrances, corridors, ramps and stall cuts shown (true) or not, and
        /// the lots' caches dropped. (PSX_CITY_LOTENT=0 at start is the real
        /// before: the pass never runs.)</summary>
        public static void LotEntSet(CityMap map, bool on)
        {
            LotEntOn = on;
            if (lotEntMapFor == map)
                foreach (var kv in on ? lotEntStallsNow : lotEntStallsWas) { kv.Key.runFoot = kv.Value.foot; kv.Key.runN = kv.Value.n; kv.Key.runU = kv.Value.u; }
            lotMapFor = null; lotEntRampByTile = null;
        }

        /// <summary>A lot or an entrance this pass added, with the pass switched off (LotEntSet).</summary>
        static bool LotEntHiddenLot(CityMap map, int li) => !LotEntOn && lotEntMapFor == map && LotEntLotsFrom >= 0 && li >= LotEntLotsFrom;
        static bool LotEntHiddenEntrance(CityMap map, int i) => !LotEntOn && lotEntMapFor == map && LotEntEntrancesFrom >= 0 && i >= LotEntEntrancesFrom;

        /// <summary>
        /// THE PASS (once per map, the end of CityMap.Parse; the lots'
        /// caches are rebuilt after it): see the header. Deterministic from
        /// the map alone, in lot order.
        /// </summary>
        public static void InstallLotEntrances(CityMap map)
        {
            lotEntQuads.Clear(); lotEntMapFor = map; lotEntRampByTile = null; lotEntStallsWas.Clear(); lotEntStallsNow.Clear();
            LotEntLotsFrom = LotEntEntrancesFrom = -1;
            LotEntStats.Reset();
            LotEntSource = "off";
            if (!LotEntOn || !LotsOn || map == null || map.lots == null || map.lots.Length == 0) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // the kept result (Resources/charlotte_lotent.bytes, made by the editor's LotEntranceBake from this
            // same pass): read when it was made for this data and this rule, else worked out now
            lotEntKey = LotEntKeyOf(map);
            if (System.Environment.GetEnvironmentVariable("PSX_LOTENT_LIVE") != "1")
            {
                var ta = Resources.Load<TextAsset>(LotEntResName);
                if (ta != null)
                {
                    bool read = LotEntRead(map, ta.bytes, lotEntKey);
                    Resources.UnloadAsset(ta);
                    if (read)
                    {
                        lotMapFor = null;
                        LotEntStats.ms = (float)clock.Elapsed.TotalMilliseconds;
                        LotEntSource = "read from " + LotEntResName + ".bytes";
                        Debug.Log("[City] " + LotEntStats.Line());
                        return;
                    }
                    Debug.LogWarning("[City] charlotte_lotent.bytes was made for other city data or another rule: the lot entrances are worked out now " +
                                     "(seconds at load). Remake it: Unity -executeMethod PSXRacing.EditorTools.CityAudit.LotEntranceBake");
                }
            }
            LotEntSource = "worked out at load";
            lotMapFor = null;
            EnsureLots(map);
            LotEntIndex(map);
            int nl = map.lots.Length;
            int osmEnd = map.stationLotsFrom >= 0 ? map.stationLotsFrom : nl;
            // the lots that want an entrance: OSM's own (not an aisle alone) and each station's forecourt
            var target = new bool[nl];
            for (int li = 0; li < osmEnd; li++) target[li] = !map.lots[li].aisleOnly && map.lots[li].ring != null && map.lots[li].ring.Length >= 3;
            foreach (var st in map.stations) if (st.lotIndex >= 0 && st.lotIndex < nl) target[st.lotIndex] = true;
            var usable = new bool[nl];
            var reachedBy = new int[nl];
            var whyNot = new string[nl];
            for (int li = 0; li < nl; li++)
            {
                reachedBy[li] = -1;
                if (map.lots[li].ring == null || map.lots[li].ring.Length < 3) continue;
                if (target[li]) LotEntStats.lots++;
                // (an aisle or a station's driveway with its own entrance leads on into the lots it joins)
                usable[li] = LotHasUsableEntrance(map, li, false, out bool reached, out int best, out string why);
                if (usable[li]) { if (target[li]) LotEntStats.usableBefore++; }
                else { whyNot[li] = why; if (reached) reachedBy[li] = best; }
            }
            LotEntStats.msUsable = (float)clock.Elapsed.TotalMilliseconds;
            // the lots that join one another (asphalt to asphalt)
            var touch = new List<int>[nl];
            var boxNear = new List<int>(16);
            for (int li = 0; li < nl; li++)
            {
                if (map.lots[li].ring == null || map.lots[li].ring.Length < 3) continue;
                var b = map.lots[li].box;
                boxNear.Clear();
                for (int tx = Mathf.FloorToInt((b.x - 1f) / TileSize); tx <= Mathf.FloorToInt((b.z + 1f) / TileSize); tx++)
                    for (int tz = Mathf.FloorToInt((b.y - 1f) / TileSize); tz <= Mathf.FloorToInt((b.w + 1f) / TileSize); tz++)
                    {
                        var ll = LotsInTile(map, tx, tz);
                        if (ll == null) continue;
                        foreach (int lj in ll)
                        {
                            if (lj <= li || boxNear.Contains(lj)) continue;
                            var ob = map.lots[lj].box;
                            if (ob.z < b.x - 1f || ob.x > b.z + 1f || ob.w < b.y - 1f || ob.y > b.w + 1f) continue;
                            boxNear.Add(lj);
                        }
                    }
                foreach (int lj in boxNear)
                {
                    if (!LotsTouch(map, li, lj)) continue;
                    (touch[li] ?? (touch[li] = new List<int>(2))).Add(lj);
                    (touch[lj] ?? (touch[lj] = new List<int>(2))).Add(li);
                }
            }
            var queue = new Queue<int>();
            void Spread(int from)
            {
                queue.Enqueue(from);
                while (queue.Count > 0)
                {
                    int a = queue.Dequeue();
                    if (touch[a] == null) continue;
                    foreach (int c in touch[a])
                    {
                        if (usable[c]) continue;
                        usable[c] = true; if (target[c]) LotEntStats.served++;
                        queue.Enqueue(c);
                    }
                }
            }
            for (int li = 0; li < nl; li++) if (usable[li]) Spread(li);
            LotEntStats.msTouch = (float)clock.Elapsed.TotalMilliseconds - LotEntStats.msUsable;

            var lots = new List<CityMap.Lot>(map.lots);
            var ents = new List<CityMap.LotEntrance>(map.lotEntrances ?? System.Array.Empty<CityMap.LotEntrance>());
            int lots0 = lots.Count, ents0 = ents.Count;
            var runs = new List<LeRun>(8);
            for (int li = 0; li < nl; li++)
            {
                if (!target[li] || usable[li]) continue;
                // the data's own entrance, where its path is clear but for the paving or the lot's kerb
                bool done = false;
                if (reachedBy[li] >= 0 && (whyNot[li] == "ground, not paving, between the street and the lot" || whyNot[li] == "the lot's own kerb"))
                {
                    var en = map.lotEntrances[reachedBy[li]];
                    var e = map.edges[en.edge];
                    if (LotPath(map, e, en.s, en.side, 0f, li, LotEntFarReachM, true, false, false, out var info))
                    {
                        LotEntCorridor(map, e, en.s, en.side, en.half, li, info.gap, lots);
                        LotEntStats.repaired++; done = true;
                    }
                }
                // derived on the street frontage: the best tier of place there is (LotFrontage)
                if (!done)
                {
                    float reach = LotEntFarReachM;
                    LotFrontage(map, li, reach, runs, out int tier);
                    if (runs.Count == 0) { reach = LotEntDriveReachM; LotFrontage(map, li, reach, runs, out tier); }
                    if (runs.Count > 0)
                    {
                        int bi = 0;
                        for (int k = 1; k < runs.Count; k++) if (LeRunRank(runs[k]) < LeRunRank(runs[bi])) bi = k;
                        var run = runs[bi];
                        var e = map.edges[run.edge];
                        float half = e.oneway ? 2.15f : 3.65f;
                        float F = run.s1 - run.s0;
                        int count = F > LotEntTwoM ? 2 : 1;
                        float lastS = float.NaN;
                        for (int c = 0; c < count; c++)
                        {
                            float want = count == 1 ? 0.5f * (run.s0 + run.s1) : run.s0 + F * (c == 0 ? 0.25f : 0.75f);
                            float s = run.ss[0];
                            foreach (float cs in run.ss) if (Mathf.Abs(cs - want) < Mathf.Abs(s - want)) s = cs;
                            if (!float.IsNaN(lastS) && Mathf.Abs(s - lastS) < 2f * half + 6f) continue;
                            if (!LotPath(map, e, s, run.side, 0f, li, reach, false, false, false, out var info)) continue;
                            ents.Add(new CityMap.LotEntrance { edge = run.edge, s = s, side = (sbyte)run.side, half = half });
                            LotEntCorridor(map, e, s, run.side, half, li, info.gap, lots);
                            if (info.stallsAcross) LotEntClearStalls(map.lots[li], info.mouth, info.n, info.t, half, info.gap);
                            if (c == 0) LotEntStats.derived++; else LotEntStats.derivedSecond++;
                            lastS = s; done = true;
                        }
                        if (done)
                        {
                            if (tier == 1) LotEntStats.throughStalls++;
                            else if (tier == 2) LotEntStats.nearJunction++;
                            else if (tier == 3) LotEntStats.onTrunk++;
                            else if (tier == 4) LotEntStats.farReach++;
                            else if (tier == 5) LotEntStats.longDrive++;
                        }
                    }
                }
                if (done) { usable[li] = true; Spread(li); }
                else
                {
                    LotEntStats.left++;
                    var b = map.lots[li].box;
                    if (LotEntStats.leftAt.Count < 400) LotEntStats.leftAt.Add($"lot {li} ({0.5f * (b.x + b.z):0}, {0.5f * (b.y + b.w):0}): {whyNot[li]}");
                }
            }
            if (lots.Count > lots0) { LotEntLotsFrom = lots0; map.lots = lots.ToArray(); }
            if (ents.Count > ents0) { LotEntEntrancesFrom = ents0; map.lotEntrances = ents.ToArray(); }
            // the lots' caches again, with the corridors in
            lotMapFor = null; leEntByTile = null;
            LotEntStats.ms = (float)clock.Elapsed.TotalMilliseconds;
            LotEntStats.msDerive = LotEntStats.ms - LotEntStats.msUsable - LotEntStats.msTouch;
            Debug.Log("[City] " + LotEntStats.Line());
        }

        // ---- THE PASS'S RESULT, KEPT ------------------------------------------
        public const string LotEntResName = "charlotte_lotent";
        const uint LotEntMagic = 0x544E454Cu;   // "LENT"
        /// <summary>Bump when the rule changes: a kept result of another version is not read.</summary>
        const int LotEntVersion = 1;
        /// <summary>Where the last pass's result came from.</summary>
        public static string LotEntSource = "";
        static uint lotEntKey;

        /// <summary>What the pass reads, hashed (before it adds anything): the graph, the roads' sizes,
        /// the lots, the entrances, the buildings' count and the rule's version.</summary>
        static uint LotEntKeyOf(CityMap map)
        {
            unchecked
            {
                uint h = 2166136261u;
                void Mix(int v) { h = (h ^ (uint)v) * 16777619u; }
                void MixF(float f) { Mix(Mathf.RoundToInt(f * 100f)); }
                Mix((int)map.graphHash); Mix(LotEntVersion); Mix(map.lots.Length); Mix(map.stationLotsFrom);
                Mix(map.footprints != null ? map.footprints.Length : 0); Mix(map.edges.Length);
                foreach (var e in map.edges) { MixF(e.length); MixF(e.width); Mix(e.cls); }
                foreach (var L in map.lots)
                {
                    Mix(L.ring != null ? L.ring.Length : 0); MixF(L.box.x); MixF(L.box.y); MixF(L.box.z); MixF(L.box.w);
                    Mix(L.holes.Length); Mix(L.runFoot.Length); Mix(L.aisleOnly ? 1 : 0);
                }
                int na = map.lotEntrances != null ? map.lotEntrances.Length : 0;
                Mix(na);
                for (int i = 0; i < na; i++) { var E = map.lotEntrances[i]; Mix(E.edge); MixF(E.s); Mix(E.side); MixF(E.half); }
                return h;
            }
        }

        /// <summary>The last pass's result as bytes (the editor writes them to
        /// Resources/charlotte_lotent.bytes): the key, the counts, the entrances and the lots it
        /// added, the corridors' and throats' quads, the stall rows it cut.</summary>
        public static byte[] LotEntBytes(CityMap map)
        {
            if (lotEntMapFor != map) return null;
            using (var ms = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write(LotEntMagic); w.Write(LotEntVersion); w.Write(lotEntKey);
                foreach (int v in new[] { LotEntStats.lots, LotEntStats.usableBefore, LotEntStats.served, LotEntStats.repaired, LotEntStats.derived, LotEntStats.derivedSecond,
                                          LotEntStats.onTrunk, LotEntStats.nearJunction, LotEntStats.throughStalls, LotEntStats.farReach, LotEntStats.longDrive, LotEntStats.left,
                                          LotEntStats.connectors, LotEntStats.throats }) w.Write(v);
                int e0 = LotEntEntrancesFrom < 0 ? map.lotEntrances.Length : LotEntEntrancesFrom;
                w.Write(map.lotEntrances.Length - e0);
                for (int i = e0; i < map.lotEntrances.Length; i++) { var E = map.lotEntrances[i]; w.Write(E.edge); w.Write(E.s); w.Write(E.side); w.Write(E.half); }
                int l0 = LotEntLotsFrom < 0 ? map.lots.Length : LotEntLotsFrom;
                w.Write(map.lots.Length - l0);
                for (int i = l0; i < map.lots.Length; i++)
                {
                    var L = map.lots[i];
                    for (int k = 0; k < 4; k++) { w.Write(L.ring[k].x); w.Write(L.ring[k].y); }
                    w.Write(L.u.x); w.Write(L.u.y);
                }
                w.Write(lotEntQuads.Count);
                foreach (var Q in lotEntQuads) for (int k = 0; k < 4; k++) { w.Write(Q[k].x); w.Write(Q[k].y); }
                w.Write(lotEntStallsNow.Count);
                foreach (var kv in lotEntStallsNow)
                {
                    w.Write(System.Array.IndexOf(map.lots, kv.Key));
                    w.Write(kv.Value.foot.Length);
                    for (int r = 0; r < kv.Value.foot.Length; r++)
                    {
                        w.Write(kv.Value.foot[r].x); w.Write(kv.Value.foot[r].y); w.Write(kv.Value.n[r]);
                        w.Write(kv.Value.u[r].x); w.Write(kv.Value.u[r].y);
                    }
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>The kept result put on the map; false when it was made for other data or another rule.</summary>
        static bool LotEntRead(CityMap map, byte[] bytes, uint key)
        {
            try
            {
                using (var r = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes)))
                {
                    if (r.ReadUInt32() != LotEntMagic || r.ReadInt32() != LotEntVersion || r.ReadUInt32() != key) return false;
                    var st = new int[14];
                    for (int i = 0; i < 14; i++) st[i] = r.ReadInt32();
                    int ne = r.ReadInt32();
                    var ents = new List<CityMap.LotEntrance>(map.lotEntrances ?? System.Array.Empty<CityMap.LotEntrance>());
                    int e0 = ents.Count;
                    for (int i = 0; i < ne; i++)
                    {
                        var E = new CityMap.LotEntrance { edge = r.ReadInt32(), s = r.ReadSingle(), side = r.ReadSByte(), half = r.ReadSingle() };
                        if (E.edge < 0 || E.edge >= map.edges.Length) return false;
                        ents.Add(E);
                    }
                    int nlots = r.ReadInt32();
                    var lots = new List<CityMap.Lot>(map.lots);
                    int l0 = lots.Count;
                    for (int i = 0; i < nlots; i++)
                    {
                        var ring = new Vector2[4];
                        for (int k = 0; k < 4; k++) ring[k] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        lots.Add(LeNewLot(ring, new Vector2(r.ReadSingle(), r.ReadSingle())));
                    }
                    int nq = r.ReadInt32();
                    var quads = new List<Vector2[]>(nq);
                    for (int i = 0; i < nq; i++)
                    {
                        var Q = new Vector2[4];
                        for (int k = 0; k < 4; k++) Q[k] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        quads.Add(Q);
                    }
                    int nst = r.ReadInt32();
                    var stalls = new List<(int li, Vector2[] foot, byte[] n, Vector2[] u)>(nst);
                    for (int i = 0; i < nst; i++)
                    {
                        int li = r.ReadInt32(), nr = r.ReadInt32();
                        if (li < 0 || li >= l0) return false;
                        var foot = new Vector2[nr]; var n = new byte[nr]; var u = new Vector2[nr];
                        for (int k = 0; k < nr; k++) { foot[k] = new Vector2(r.ReadSingle(), r.ReadSingle()); n[k] = r.ReadByte(); u[k] = new Vector2(r.ReadSingle(), r.ReadSingle()); }
                        stalls.Add((li, foot, n, u));
                    }
                    // all read: onto the map
                    LotEntStats.lots = st[0]; LotEntStats.usableBefore = st[1]; LotEntStats.served = st[2]; LotEntStats.repaired = st[3];
                    LotEntStats.derived = st[4]; LotEntStats.derivedSecond = st[5]; LotEntStats.onTrunk = st[6]; LotEntStats.nearJunction = st[7];
                    LotEntStats.throughStalls = st[8]; LotEntStats.farReach = st[9]; LotEntStats.longDrive = st[10]; LotEntStats.left = st[11];
                    LotEntStats.connectors = st[12]; LotEntStats.throats = st[13];
                    foreach (var sc in stalls)
                    {
                        var L = map.lots[sc.li];
                        lotEntStallsWas[L] = (L.runFoot, L.runN, L.runU);
                        L.runFoot = sc.foot; L.runN = sc.n; L.runU = sc.u;
                        lotEntStallsNow[L] = (sc.foot, sc.n, sc.u);
                    }
                    lotEntQuads.AddRange(quads);
                    if (nlots > 0) { LotEntLotsFrom = l0; map.lots = lots.ToArray(); }
                    if (ne > 0) { LotEntEntrancesFrom = e0; map.lotEntrances = ents.ToArray(); }
                    return true;
                }
            }
            catch (System.Exception) { return false; }
        }

        // ---- THE AUDIT (Editor/CityAudit.Lots.cs) ------------------------------
        public sealed class LotEntAudit
        {
            public int lots, propLots, dataEntrances, withDataEntrance, usable, served, none, curbedAllRound, noneCurbedAllRound;
            public double noneM2;
            /// <summary>Per lot: 0 not counted, 1 its own usable entrance, 2 through a lot that has one, 3 none.</summary>
            public byte[] state;
            public string[] why;
            public float[] areaM2;
            public readonly SortedDictionary<string, int> whyCount = new SortedDictionary<string, int>();
        }

        static float LeRingArea(Vector2[] r)
        {
            float a = 0f;
            for (int k = 0; k < r.Length; k++) { var p = r[k]; var q = r[(k + 1) % r.Length]; a += p.x * q.y - q.x * p.y; }
            return Mathf.Abs(0.5f * a);
        }

        /// <summary>Is every piece of the lot's ring shut: its own kerb, a
        /// building, or a street's walk whose curb is not cut there?</summary>
        static bool LotCurbedAllRound(CityMap map, int li)
        {
            var lot = map.lots[li];
            var r = lot.ring; int nr = r.Length;
            float area = 0f;
            for (int i = 0; i < nr; i++) { var p = r[i]; var q = r[(i + 1) % nr]; area += p.x * q.y - q.x * p.y; }
            float sgn = area > 0f ? 1f : -1f;
            for (int i = 0; i < nr; i++)
            {
                Vector2 a = r[i], b = r[(i + 1) % nr];
                var d = b - a; float len = d.magnitude;
                if (len < 0.05f) continue;
                var outw = new Vector2(d.y, -d.x) / len * sgn;
                int k = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
                for (int j = 0; j < k; j++)
                {
                    var m = a + d * ((j + 0.5f) / k);
                    var land = m + outw * 0.8f;
                    if (map.AnyFootprintNear(m, 1.5f)) continue;                       // a wall
                    tileLots.Clear();
                    var ll = LotsInTile(map, Mathf.FloorToInt(land.x / TileSize), Mathf.FloorToInt(land.y / TileSize));
                    if (ll != null) tileLots.AddRange(ll);
                    bool kerb = WalksOn && LotCurbsOn && !lot.aisleOnly && LotCurbHere(map, li, m, outw);
                    tileLots.Clear();
                    if (kerb) continue;
                    if (ApronAt(map, land.x, land.y) || LotPavementAt(map, land) >= 0) return false;   // an apron, a lot it joins
                    // a street's frontage: shut where that side has a walk (its curb)
                    lePathSegs.Clear();
                    map.EdgeSegsInRect(m - Vector2.one * 30f, m + Vector2.one * 30f, lePathSegs);
                    bool walk = false, road = false;
                    foreach (int packed in lePathSegs)
                    {
                        var e = map.edges[packed >> 12]; int seg = packed & 0xFFF;
                        if (seg + 1 >= e.pts.Length) continue;
                        Vector2 ea = e.pts[seg], eab = e.pts[seg + 1] - ea;
                        float l2 = eab.sqrMagnitude;
                        float u = l2 > 1e-4f ? Mathf.Clamp01(Vector2.Dot(m - ea, eab) / l2) : 0f;
                        if (Vector2.Distance(m, ea + eab * u) >= e.width * 0.5f + WalkW + 3f) continue;
                        road = true;
                        int side = eab.x * (m.y - ea.y) - eab.y * (m.x - ea.x) > 0f ? 1 : -1;
                        if (!WalkSide(map, e, side)) continue;
                        // its curb: cut here?
                        LotEntFrame(e, e.s[seg] + Mathf.Sqrt(l2) * u, side, out var wm, out var wn, out _);
                        var wq = wm + wn * (0.5f * WalkW);
                        if (!ApronAt(map, wq.x, wq.y)) walk = true;
                    }
                    if (road && !walk) return false;      // a verge: no curb
                    if (!road) return false;              // open ground (LotCurbHere said no kerb)
                }
            }
            return true;
        }

        /// <summary>
        /// THE ENTRANCE AUDIT, city-wide, as built: for every drawn lot (OSM's
        /// own and the stations' forecourts; not an aisle alone, not one under
        /// a restaurant's own lot) the entrances of the map that reach it, the
        /// ones a car can use (LotPath, as built, with heights), the lots
        /// reached through a lot that has one, and the lots with none.
        /// </summary>
        public static LotEntAudit LotEntranceAudit(CityMap map, Trims trims, Dictionary<long, List<CityBuildings.B>> proc)
        {
            var A = new LotEntAudit();
            if (!LotsOn || map.lots == null) return A;
            EnsureLots(map);
            LotEntIndex(map);
            var prevLamp = lampBuildings;
            if (proc != null) lampBuildings = proc;
            leTrims = trims; leProc = proc;
            int nl = map.lots.Length;
            A.state = new byte[nl]; A.why = new string[nl]; A.areaM2 = new float[nl];
            A.dataEntrances = map.lotEntrances != null ? map.lotEntrances.Length : 0;
            int osmEnd = map.stationLotsFrom >= 0 ? map.stationLotsFrom : nl;
            var target = new bool[nl];
            for (int li = 0; li < osmEnd; li++) target[li] = !map.lots[li].aisleOnly && map.lots[li].ring != null && map.lots[li].ring.Length >= 3;
            foreach (var st in map.stations) if (st.lotIndex >= 0 && st.lotIndex < nl) target[st.lotIndex] = true;
            var queue = new Queue<int>();
            var seen = new bool[nl];
            var boxNear = new List<int>(16);
            for (int li = 0; li < nl; li++)
            {
                if (map.lots[li].ring == null || map.lots[li].ring.Length < 3) continue;
                if (OverPropLot(map.lots[li])) { if (target[li]) A.propLots++; target[li] = false; continue; }
                bool ok = LotHasUsableEntrance(map, li, true, out bool reached, out _, out string why);
                // (an aisle or a driveway with its own entrance leads on into the lots it joins)
                if (ok) { queue.Enqueue(li); seen[li] = true; }
                if (!target[li]) continue;
                A.lots++;
                A.areaM2[li] = LeRingArea(map.lots[li].ring);
                if (reached || ok) A.withDataEntrance++;
                A.state[li] = (byte)(ok ? 1 : 3);
                A.why[li] = why;
            }
            // through a lot that has one
            while (queue.Count > 0)
            {
                int a = queue.Dequeue();
                var b = map.lots[a].box;
                boxNear.Clear();
                for (int tx = Mathf.FloorToInt((b.x - 1f) / TileSize); tx <= Mathf.FloorToInt((b.z + 1f) / TileSize); tx++)
                    for (int tz = Mathf.FloorToInt((b.y - 1f) / TileSize); tz <= Mathf.FloorToInt((b.w + 1f) / TileSize); tz++)
                    {
                        var ll = LotsInTile(map, tx, tz);
                        if (ll == null) continue;
                        foreach (int lj in ll)
                        {
                            if (seen[lj] || boxNear.Contains(lj) || map.lots[lj].ring == null || map.lots[lj].ring.Length < 3) continue;
                            var ob = map.lots[lj].box;
                            if (ob.z < b.x - 1f || ob.x > b.z + 1f || ob.w < b.y - 1f || ob.y > b.w + 1f) continue;
                            boxNear.Add(lj);
                        }
                    }
                foreach (int lj in boxNear)
                {
                    if (!LotsTouch(map, a, lj)) continue;
                    seen[lj] = true;
                    if (A.state[lj] == 3) A.state[lj] = 2;
                    queue.Enqueue(lj);
                }
            }
            for (int li = 0; li < nl; li++)
            {
                if (A.state[li] == 0) continue;
                bool shut = LotCurbedAllRound(map, li);
                if (shut) A.curbedAllRound++;
                if (A.state[li] == 1) A.usable++;
                else if (A.state[li] == 2) A.served++;
                else
                {
                    A.none++; A.noneM2 += A.areaM2[li];
                    if (shut) A.noneCurbedAllRound++;
                    string w = A.why[li] ?? "?";
                    A.whyCount[w] = (A.whyCount.TryGetValue(w, out int c) ? c : 0) + 1;
                }
            }
            lampBuildings = prevLamp;
            leEntByTile = null; leTrims = null; leProc = null;
            return A;
        }

        public static float LotRingAreaForTools(Vector2[] r) => LeRingArea(r);
        /// <summary>For the tools: the entrance whose mouth is nearest a plan point (-1: none).</summary>
        public static int LotEntranceNear(CityMap map, Vector2 p, out float dist)
        {
            int best = -1; dist = float.MaxValue;
            for (int i = 0; i < (map.lotEntrances != null ? map.lotEntrances.Length : 0); i++)
            {
                if (LotEntHiddenEntrance(map, i)) continue;
                LotEntFrame(map.edges[map.lotEntrances[i].edge], map.lotEntrances[i].s, map.lotEntrances[i].side, out var m, out _, out _);
                float d = (m - p).magnitude;
                if (d < dist) { dist = d; best = i; }
            }
            return best;
        }
        /// <summary>For the tools: the lot whose asphalt (not a hole or an island) the plan point is on, -1 none.</summary>
        public static int LotPavedForTools(CityMap map, Vector2 q) => LotsOn && map.lots != null ? LotPavementAt(map, q) : -1;

        /// <summary>For the tools: the frame of an entrance (its mouth on the
        /// pavement's edge, the way in, along the street).</summary>
        public static void LotEntranceFrame(CityMap map, int entrance, out Vector2 mouth, out Vector2 n, out Vector2 t)
        {
            var en = map.lotEntrances[entrance];
            LotEntFrame(map.edges[en.edge], en.s, en.side, out mouth, out n, out t);
        }

        /// <summary>For the tools: the lot an entrance leads into and how far
        /// from the pavement its asphalt starts (-1: none within reach).</summary>
        public static int LotOfEntrance(CityMap map, int entrance, out float gap)
        {
            gap = -1f;
            EnsureLots(map);
            var en = map.lotEntrances[entrance];
            LotEntFrame(map.edges[en.edge], en.s, en.side, out var mouth, out var n, out _);
            for (float d = 0f; d <= LotEntFarReachM; d += 0.25f)
            {
                int li = LotPavementAt(map, mouth + n * d);
                if (li >= 0 && !map.lots[li].aisleOnly) { gap = d; return li; }
            }
            return -1;
        }
    }
}
