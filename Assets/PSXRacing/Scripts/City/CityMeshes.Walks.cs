using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  STREETS PASS 4 (2026-10-28; the owner on the Tryon Sprint start, NFS
    //  2015 as the bar: "the roads not connecting (I see grass as meeting
    //  points) and sidewalks/parking lots/concretes just feel sloppy").
    //
    //  THE CAUSE. A city street met its land exactly as a rural road does:
    //  an inch down, a 0.6 m shoulder, then 1V:4H to the lattice (EmitSide's
    //  verge), every piece of it grass or concrete by the 8 m lattice cell it
    //  falls in (PavedCell: within 1 km of the Square, or with a non-house
    //  footprint within 20 m of the cell's centre). At a junction the curb
    //  return (plan A10, R 7.5-15 m) carried that same verge round its arc,
    //  and the land INSIDE the corner - between the two streets' edges and
    //  the arc, about 0.21 R^2, 12-48 m2 - was bare lattice: grass wherever
    //  the corner cell's centre stood more than 20 m from a commercial
    //  building, which outside the core is most corners. Nothing was
    //  missing from the road; the corner was a lawn by rule.
    //
    //  WALKS. An urban street side (WalkSide) is edged by a 6 in (0.15 m)
    //  concrete curb and a 2.4 m sidewalk with a control joint every 1.5 m
    //  (Slot.Sidewalk, the pack's concrete_pt_4), and its verge starts at
    //  the back of the walk. The curb is a mountable profile (13.5 cm up
    //  over 10 cm, then the rounded top to 0.15 m at 25 cm): a wheel ray
    //  climbs it over a quarter of a metre, never a slot, and the body box
    //  (0.22 m up on the default car) clears it. At a junction the walk turns
    //  the corner as one piece: the curb follows the curb return, and the
    //  whole corner between it and the two back-of-walk lines is concrete,
    //  fanned from the property corner P where those lines meet; the verges
    //  behind run T1'-P-T2', mitred on P's bisector (CornerWalk). A walk ends
    //  in a ramp - width and curb to nothing over its last span - wherever
    //  its side stops being a plain verge (a deck, a rail, a squeeze, a
    //  branch, a lot that crowds it, a corner that cannot take one).
    //
    //  Out of every lane by construction: the walk starts at the drawn edge
    //  and goes out; a section whose way out meets another road's pavement
    //  (ClearRun) narrows it, below 1.2 m it ramps out.
    //
    //  PSX_CITY_WALKS=0: the verges as before (the A/B).
    // ======================================================================
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_WALKS=0 turns the curbs and sidewalks off (the before).
        /// Not readonly: the street shots photograph both in one run.</summary>
        public static bool WalksOn = System.Environment.GetEnvironmentVariable("PSX_CITY_WALKS") != "0";

        /// <summary>A sidewalk's width behind the curb (AASHTO/CDOT: 5-10 ft).</summary>
        public const float WalkW = 2.4f;
        /// <summary>Narrower than this (another road's pavement crowding it) the walk ramps out.</summary>
        const float WalkMinM = 1.2f;
        /// <summary>The curb: 6 in.</summary>
        public const float CurbH = 0.15f;
        /// <summary>The curb's face: from an inch under the edge to this height, this far out.</summary>
        const float CurbFaceOut = 0.10f, CurbFaceH = 0.11f;
        /// <summary>The rounded top: on to CurbH this far out of the edge.</summary>
        const float CurbNoseOut = 0.25f;
        /// <summary>A walk drains to the gutter: it rises 2% away from the curb.</summary>
        const float WalkCrossFall = 0.02f;
        /// <summary>The walk keeps this clear of another road's pavement.</summary>
        const float WalkPadM = 0.3f;
        /// <summary>One repeat of the walk's texture: three 1.5 m panels.</summary>
        const float WalkTexM = 4.5f;
        /// <summary>Every street this close to the Square is urban.</summary>
        const float UrbanCoreM = 2200f;
        /// <summary>Else a street with a non-house building this close to it.</summary>
        const float UrbanFootM = 40f;
        /// <summary>A corner's property point P no further than this from either back-of-walk end.</summary>
        const float CornerMaxM = 22f;

        /// <summary>The last builds' walk counts (the street shots print them).</summary>
        public static class WalkStats
        {
            public static float walkM;
            public static int spans, ramps, cornersPoly, cornersStrip, cornersNone, sideMismatch;
            /// <summary>Why corners were left as verges: shape, arm, width, acute, strip, P.</summary>
            public static readonly int[] noneWhy = new int[8];
            public static void Reset() { walkM = 0f; spans = ramps = cornersPoly = cornersStrip = cornersNone = sideMismatch = 0; System.Array.Clear(noneWhy, 0, noneWhy.Length); lotCurbM = 0f; }
            public static string Line() =>
                $"walks {walkM:0} m in {spans} spans ({ramps} ramp ends), corners {cornersPoly} fanned from P + {cornersStrip} strips, {cornersNone} left as verges (shape/arm/width/acute/strip/P/paved {noneWhy[1]}/{noneWhy[2]}/{noneWhy[3]}/{noneWhy[4]}/{noneWhy[5]}/{noneWhy[6]}/{noneWhy[7]}), {sideMismatch} side mismatches, lot curbs {lotCurbM:0} m";
        }

        static CityMap walkMapFor;
        static sbyte[] walkEdgeCache, walkTwinCache;

        static void EnsureWalkCache(CityMap map)
        {
            if (walkMapFor == map && walkEdgeCache != null && walkEdgeCache.Length == map.edges.Length) return;
            walkMapFor = map;
            walkEdgeCache = new sbyte[map.edges.Length];
            walkTwinCache = new sbyte[map.edges.Length];
        }

        /// <summary>Does this street get curbs and sidewalks? A surface
        /// street (local to primary, no ramp, tunnel or bridge), at least a
        /// two-lane width (an alley or a drive keeps its verge), in the city:
        /// near the Square or with a non-house building beside it.</summary>
        public static bool WalkEdge(CityMap map, CityMap.Edge e)
        {
            if (!WalksOn) return false;
            EnsureWalkCache(map);
            ref sbyte c = ref walkEdgeCache[e.index];
            if (c == 0) c = (sbyte)(WalkEdgeUncached(map, e) ? 1 : -1);
            return c > 0;
        }

        static bool WalkEdgeUncached(CityMap map, CityMap.Edge e)
        {
            if (e.link || e.cls > 3 || e.tunnel || e.bridge || e.a == e.b) return false;
            if (e.width < (e.oneway ? 4f : 6f)) return false;
            for (int k = 1; k <= 3; k++)
            {
                var p = e.PointAt(e.length * k * 0.25f);
                if (Vector2.Distance(p, map.uptown) < UrbanCoreM) return true;
                if (map.AnyFootprintNear(p, UrbanFootM, nonHouseOnly: true)) return true;
            }
            return false;
        }

        /// <summary>Does this SIDE of a street get a walk? Side +1 is the
        /// left of travel (CornerFill's armOut): on a one-way carriageway of a
        /// divided road it faces the median, which keeps its verge.</summary>
        public static bool WalkSide(CityMap map, CityMap.Edge e, int side)
        {
            if (!WalkEdge(map, e)) return false;
            if (!e.oneway || side < 0) return true;
            ref sbyte c = ref walkTwinCache[e.index];
            if (c == 0) c = (sbyte)(HasTwin(map, e) ? 1 : -1);
            return c < 0;
        }

        static readonly HashSet<int> walkTwinSegs = new HashSet<int>();

        /// <summary>A one-way carriageway with its other carriageway (same
        /// name, one-way, the other way) within 45 m on its left.</summary>
        static bool HasTwin(CityMap map, CityMap.Edge e)
        {
            if (string.IsNullOrEmpty(e.name) || e.length < 1f) return false;
            float sm = e.length * 0.5f;
            var p = e.PointAt(sm); var t = e.TangentAt(sm);
            var left = new Vector2(-t.y, t.x);
            walkTwinSegs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 45f, p + Vector2.one * 45f, walkTwinSegs);
            foreach (int packed in walkTwinSegs)
            {
                var o = map.edges[packed >> 12];
                if (o.index == e.index || !o.oneway || o.name != e.name) continue;
                int seg = packed & 0xFFF;
                if (seg + 1 >= o.pts.Length) continue;
                Vector2 a = o.pts[seg], b = o.pts[seg + 1], ab = b - a;
                float l2 = ab.sqrMagnitude;
                if (l2 < 1e-4f) continue;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                var q = a + ab * u;
                float lat = Vector2.Dot(q - p, left);
                if (lat > 1f && lat < 45f && Vector2.Dot(ab / Mathf.Sqrt(l2), t) < -0.7f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  Per edge: the walk's width at every section, per side (0 = none)
        // ------------------------------------------------------------------
        static float[] walkL = new float[128], walkR = new float[128];
        static int walkEdgeNow = -1;
        static float[] WalkArr(int side) => side < 0 ? walkL : walkR;

        /// <summary>Is span i's side a plain verge (what EmitSide draws last)?</summary>
        static bool WalkableSpan(int i, int side)
        {
            if (i < 1 || i >= sections.Count || i >= spanFlags.Count) return false;
            var f = spanFlags[i];
            if (f.elev || f.skip) return false;
            var sf = f[side];
            if (sf.union || sf.rail || sf.cut || sf.gap || sf.median) return false;
            return sections[i - 1].Strip(side) < 0f && sections[i].Strip(side) < 0f;
        }

        /// <summary>
        /// The walk's width at each of the edge's sections, both sides,
        /// after the side flags: the edge's sections (BuildSections) and
        /// flags (DecideSideFlagsSteps) are this tile's. A section takes a
        /// walk when every span touching it is a plain verge, its way out is
        /// clear of other pavement for the width, and - at the edge's ends -
        /// the node carries the walk on (a corner that takes one, a plain
        /// continuation of another walk street). Else 0, and the span beside
        /// it is the ramp.
        /// </summary>
        static IEnumerable<int> PrepareWalks(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            int n = sections.Count;
            if (walkL.Length < n) { walkL = new float[n * 2]; walkR = new float[n * 2]; }
            System.Array.Clear(walkL, 0, n); System.Array.Clear(walkR, 0, n);
            walkEdgeNow = e.index;
            if (!WalkEdge(map, e)) yield break;
            var o = tm.origin;
            const float near = 30f;
            for (int side = -1; side <= 1; side += 2)
            {
                if (!WalkSide(map, e, side)) continue;
                var arr = WalkArr(side);
                for (int k = 0; k < n; k++)
                {
                    if ((k & 7) == 7) yield return 0;   // WP-09: eight sections a step
                    var S = sections[k];
                    var ed = S.Edge(side);
                    float wx = ed.x + o.x, wz = ed.z + o.z;
                    if (wx < min.x - near || wx > max.x + near || wz < min.y - near || wz > max.y + near) continue;
                    if (k > 0 && !WalkableSpan(k, side)) continue;
                    if (k + 1 < n && !WalkableSpan(k + 1, side)) continue;
                    var outw = S.Out(side);
                    var pw = new Vector2(wx, wz);
                    if (k == 0 || k == n - 1)
                    {
                        int node = k == 0 ? e.a : e.b;
                        if (!WalkEndKeeps(map, trims, e, node, side, pw)) continue;
                    }
                    float w = WalkClear(map, trims, pw, outw, ed.y + CurbH, WalkW);
                    if (w < WalkMinM || WalkOnPavement(map, trims, pw, outw, w, ed.y, e.index)) continue;
                    arr[k] = w;
                }
                // and between the sections: a span whose walk would lie on
                // another road at its middle takes none (both ends ramp)
                for (int k = 1; k < n; k++)
                {
                    if (arr[k - 1] <= 0f && arr[k] <= 0f) continue;
                    var A = sections[k - 1]; var B = sections[k];
                    var em = 0.5f * (A.Edge(side) + B.Edge(side));
                    var om = (A.Out(side) + B.Out(side)).normalized;
                    if (WalkOnPavement(map, trims, new Vector2(em.x + o.x, em.z + o.z), om, Mathf.Max(arr[k - 1], arr[k]), em.y, e.index))
                        arr[k - 1] = arr[k] = 0f;
                }
            }
        }

        /// <summary>How wide a walk fits out from an edge point: the clear run
        /// to another road's pavement less <see cref="WalkPadM"/>.</summary>
        static float WalkClear(CityMap map, Trims trims, Vector2 p, Vector2 outw, float y, float want)
        {
            GatherNear(map, p, p, want + WalkPadM + 1f);
            float run = ClearRun(map, trims, p, outw, y, want + WalkPadM, out float otherY);
            if (float.IsNaN(otherY)) return want;
            return Mathf.Min(want, run + VergeClearPadM - WalkPadM);
        }

        /// <summary>
        /// Would a walk out from this edge point lie on any pavement - another
        /// road's ribbon or a junction's fan, at its level? Read at the walk's
        /// middle, its back and its curb's nose. ClearRun does not meet
        /// pavement its ray STARTS on, so a walk from an edge drawn inside
        /// another road (a split, a clipped mouth) passed it: GRASS ON TOP of
        /// a link's lane at W 5th, a curb in East 7th Street's.
        /// </summary>
        static bool WalkOnPavement(CityMap map, Trims trims, Vector2 p, Vector2 outw, float w, float y, int own)
        {
            return OnPavement(map, trims, p + outw * (0.5f * w), y, own, own) || OnPavement(map, trims, p + outw * (w - 0.05f), y, own, own);
        }

        /// <summary>Is the plan point on a drawn road ribbon or fan within a
        /// car's height of <paramref name="y"/>?</summary>
        static bool OnPavement(CityMap map, Trims trims, Vector2 q, float y, int skipA = -1, int skipB = -1, bool fans = true)
        {
            GatherNear(map, q, q, 1f);
            GatherPavement(map, trims);
            foreach (int ei in nearEdges)
            {
                if (ei == skipA || ei == skipB) continue;   // its own ribbon: the walk starts at its edge
                var ol = OutlineOf(map, trims, ei);
                float h = OutlineHeightAt(ol, q);
                if (!float.IsNaN(h) && Mathf.Abs(h - y) < RoadsideRules.CarBandM) return true;
            }
            if (fans)
            foreach (int n in nearFans)
                if (OnFan(FanPolyOf(map, trims, n), q, out float fy) && Mathf.Abs(fy - y) < RoadsideRules.CarBandM) return true;
            return false;
        }

        /// <summary>Does the walk carry on at this end of the edge?</summary>
        static bool WalkEndKeeps(CityMap map, Trims trims, CityMap.Edge e, int node, int side, Vector2 edgePt)
        {
            var arms = map.nodeEdges[node];
            if (arms.Count < 2) return false;
            if (!trims.patch[node])
            {
                // a plain continuation (or a through road past a clipped
                // branch, whose side there is a gap): walks on if every road
                // here is a walk street
                foreach (int ai in arms) if (!WalkEdge(map, map.edges[ai])) return false;
                return true;
            }
            return CornerKindAt(map, trims, node, e.index, side, edgePt) != 0;
        }

        static readonly Dictionary<long, sbyte> cornerKindCache = new Dictionary<long, sbyte>();
        static readonly List<FanCorner> walkRing = new List<FanCorner>(24);

        /// <summary>The kind of walk corner (0 none, 1 fanned from P, 2 a
        /// strip round the ring) on this side of this arm at its fan, asked
        /// by the arm. The fan's own build asks <see cref="CornerWalkOf"/>
        /// of the ring it draws: one function, so both agree.</summary>
        static int CornerKindAt(CityMap map, Trims trims, int node, int edge, int side, Vector2 edgePt)
        {
            long key = ((long)node << 24) ^ ((long)edge << 1) ^ (side > 0 ? 1L : 0L);
            if (cornerKindCache.TryGetValue(key, out var kv)) return kv;
            int kind = 0;
            if (!(trims.bulb != null && trims.bulb[node]) && trims.ClusterOfNode(node) == null)
            {
                FanCorners(map, trims, node, Vector3.zero, walkRing);
                for (int i = 0; i < walkRing.Count; i++)
                {
                    var k = walkRing[i];
                    if (k.edge != edge || k.side != side || k.arc) continue;
                    if (Vector2.Distance(new Vector2(k.pos.x, k.pos.z), edgePt) > 0.5f) WalkStats.sideMismatch++;
                    kind = CornerWalkOf(map, trims, node, walkRing, i, Vector3.zero, out _);
                    break;
                }
            }
            cornerKindCache[key] = (sbyte)kind;
            return kind;
        }

        /// <summary>One junction corner's walk: the ring's indices from the
        /// first arm's corner (g0) to the next arm's (g1), the arms' outward
        /// normals and walk widths there, and P.</summary>
        struct CornerWalk
        {
            public int g0, g1;
            public Vector2 n0, n1;
            public float w0, w1;
            public Vector2 P;
        }

        /// <summary>The arm's outward unit at its fan corner k: the ribbon's
        /// own cross-line there (RightAt), pointing off the pavement.</summary>
        static Vector2 ArmOut(CityMap map, Trims trims, FanCorner k, Vector3 origin)
        {
            var arm = map.edges[k.edge];
            float at = arm.a == k.node ? trims.atA[arm.index] : arm.length - trims.atB[arm.index];
            var right = RightAt(map, trims, arm, at, out _);
            var c = arm.PointAt(at);
            var d = new Vector2(k.pos.x + origin.x - c.x, k.pos.z + origin.z - c.y);
            return Vector2.Dot(right, d) >= 0f ? right : -right;
        }

        /// <summary>
        /// The walk round the corner at ring index i (either arm's corner of
        /// it): 0 none, 1 one concrete fan from P (two streets at 60-150
        /// degrees with room for the curb return's walk), 2 a strip of walk
        /// round the ring (a straight-on side, phi of 150 degrees or more).
        /// </summary>
        static int CornerWalkOf(CityMap map, Trims trims, int node, List<FanCorner> ring, int i, Vector3 origin, out CornerWalk cw)
        {
            cw = default;
            int nR = ring.Count;
            if (nR < 3) return 0;
            int g0, g1;
            var k = ring[i];
            if (k.arc) return 0;
            if (k.mouthNext)
            {
                // the corner group ends here: back over its arc to the arm before
                g1 = i; g0 = (i + nR - 1) % nR;
                int guard = 0;
                while (ring[g0].arc && guard++ < nR) g0 = (g0 + nR - 1) % nR;
            }
            else
            {
                g0 = i; g1 = (i + 1) % nR;
                int guard = 0;
                while (ring[g1].arc && guard++ < nR) g1 = (g1 + 1) % nR;
            }
            var k0 = ring[g0]; var k1 = ring[g1];
            noneWhyNow = 1;
            if (k0.arc || k1.arc || k0.mouthNext || !k1.mouthNext || g0 == g1) return 0;
            if (k0.edge < 0 || k1.edge < 0 || k0.edge == k1.edge) return 0;
            noneWhyNow = 2;
            if (!WalkSide(map, map.edges[k0.edge], k0.side) || !WalkSide(map, map.edges[k1.edge], k1.side)) return 0;
            cw.g0 = g0; cw.g1 = g1;
            cw.n0 = ArmOut(map, trims, k0, origin);
            cw.n1 = ArmOut(map, trims, k1, origin);
            var p0 = new Vector2(k0.pos.x + origin.x, k0.pos.z + origin.z);
            var p1 = new Vector2(k1.pos.x + origin.x, k1.pos.z + origin.z);
            cw.w0 = WalkClear(map, trims, p0, cw.n0, k0.pos.y + CurbH, WalkW);
            cw.w1 = WalkClear(map, trims, p1, cw.n1, k1.pos.y + CurbH, WalkW);
            noneWhyNow = 3;
            if (cw.w0 < WalkMinM || cw.w1 < WalkMinM) return 0;
            noneWhyNow = 1;
            for (int j = g0; j != g1; j = (j + 1) % ring.Count)
                if (ChordRailed(map, trims, node, ring[j], ring[(j + 1) % ring.Count], origin)) return 0;   // a deck edge or a drop: its face and rail stand
            // every chord's walk off any other road (a link collapsed into the
            // fan's mouth is no corner of the ring, and its lanes ran under it)
            noneWhyNow = 7;
            for (int j = g0; j != g1; j = (j + 1) % ring.Count)
            {
                int jn = (j + 1) % ring.Count;
                var ka = ring[j]; var kb = ring[jn];
                var oa = j == g0 ? cw.n0 : GroupOut(ring, cw, j); var ob = jn == g1 ? cw.n1 : GroupOut(ring, cw, jn);
                for (int t4 = 1; t4 <= 3; t4++)
                {
                    float t = t4 * 0.25f;
                    var pq = Vector3.Lerp(ka.pos, kb.pos, t);
                    var oq = Vector2.Lerp(oa, ob, t).normalized;
                    var q = new Vector2(pq.x + origin.x, pq.z + origin.z);
                    float wq = Mathf.Min(cw.w0, cw.w1);
                    if (OnPavement(map, trims, q + oq * (0.5f * wq), pq.y, k0.edge, k1.edge, fans: false)) return 0;   // ribbons only: the fans refused 99 of 135 uptown corners
                }
            }
            float c = Vector2.Dot(cw.n0, cw.n1);
            noneWhyNow = 5;
            if (c >= 0.866f) return RingStripFits(ring, cw, origin) ? 2 : 0;
            noneWhyNow = 4;
            if (c < -0.5f) return 0;
            noneWhyNow = 6;
            // P: where the two back-of-walk lines meet
            var b0 = p0 + cw.n0 * cw.w0; var b1 = p1 + cw.n1 * cw.w1;
            var d0 = new Vector2(-cw.n0.y, cw.n0.x); var d1 = new Vector2(-cw.n1.y, cw.n1.x);
            float den = d0.x * d1.y - d0.y * d1.x;
            if (Mathf.Abs(den) < 0.25f) return RingStripFits(ring, cw, origin) ? 2 : 0;
            var w = b1 - b0;
            float u = (w.x * d1.y - w.y * d1.x) / den;
            var P = b0 + d0 * u;
            cw.P = P;
            // a curb return wider than ~3.4 walk widths cuts the corner past the
            // back-of-walk lines: P stands on the junction's own pavement, and
            // the walk follows the return as a strip instead
            if (OnPavement(map, trims, P, 0.5f * (k0.pos.y + k1.pos.y), k0.edge, k1.edge)) { noneWhyNow = 7; return RingStripFits(ring, cw, origin) ? 2 : 0; }
            // P lies toward the junction from both ends (the curb return is
            // longer than the walk is wide), not far, and off other pavement
            var toJ0 = new Vector2(k1.pos.x - k0.pos.x, k1.pos.z - k0.pos.z);
            if (Vector2.Distance(P, b0) > CornerMaxM || Vector2.Distance(P, b1) > CornerMaxM) return RingStripFits(ring, cw, origin) ? 2 : 0;
            if (Vector2.Dot(P - b0, toJ0) < -0.05f || Vector2.Dot(P - b1, -toJ0) < -0.05f) return RingStripFits(ring, cw, origin) ? 2 : 0;
            if (!CornerClear(map, trims, b0, P, k0.pos.y + CurbH) || !CornerClear(map, trims, b1, P, k1.pos.y + CurbH))
                return RingStripFits(ring, cw, origin) ? 2 : 0;
            int mid = MidArc(ring, g0, g1);
            if (mid >= 0)
            {
                var km = ring[mid];
                var pm = new Vector2(km.pos.x + origin.x, km.pos.z + origin.z);
                if (!CornerClear(map, trims, pm, P, km.pos.y + CurbH)) return RingStripFits(ring, cw, origin) ? 2 : 0;
            }
            return 1;
        }

        /// <summary>Why the last CornerWalkOf returned 0 (WalkStats.noneWhy).</summary>
        static int noneWhyNow;

        static int MidArc(List<FanCorner> ring, int g0, int g1)
        {
            int nR = ring.Count, cnt = (g1 - g0 + nR) % nR;
            return cnt >= 2 ? (g0 + cnt / 2) % nR : -1;
        }

        /// <summary>Nothing paved between a and b at walk height.</summary>
        static bool CornerClear(CityMap map, Trims trims, Vector2 a, Vector2 b, float y)
        {
            var d = b - a; float len = d.magnitude;
            if (len < 0.05f) return true;
            GatherNear(map, a, b, 1f);
            ClearRun(map, trims, a, d / len, y, len, out float otherY);
            return float.IsNaN(otherY);
        }

        /// <summary>The outward unit at ring corner j of a corner group: the
        /// arms' own at its two ends, the chords' bisector on the arc.</summary>
        static Vector2 GroupOut(List<FanCorner> ring, in CornerWalk cw, int j)
        {
            if (j == cw.g0) return cw.n0;
            if (j == cw.g1) return cw.n1;
            int nR = ring.Count;
            var a = ring[(j + nR - 1) % nR].pos; var k = ring[j].pos; var b = ring[(j + 1) % nR].pos;
            Vector2 c0 = new Vector2(k.x - a.x, k.z - a.z), c1 = new Vector2(b.x - k.x, b.z - k.z);
            // the perimeter runs anticlockwise: outward is each chord's right
            Vector2 m0 = c0.sqrMagnitude > 1e-6f ? new Vector2(c0.y, -c0.x).normalized : Vector2.zero;
            Vector2 m1 = c1.sqrMagnitude > 1e-6f ? new Vector2(c1.y, -c1.x).normalized : Vector2.zero;
            var m = m0 + m1;
            return m.sqrMagnitude > 1e-6f ? m.normalized : (cw.n0 + cw.n1).normalized;
        }

        /// <summary>The walk width at ring corner j: the ends' widths, eased along the group.</summary>
        static float GroupW(List<FanCorner> ring, in CornerWalk cw, int j)
        {
            int nR = ring.Count, cnt = (cw.g1 - cw.g0 + nR) % nR;
            if (cnt <= 0) return cw.w0;
            float t = (float)((j - cw.g0 + nR) % nR) / cnt;
            return Mathf.Lerp(cw.w0, cw.w1, t);
        }

        /// <summary>A strip of walk round the ring keeps its own direction
        /// all the way (no offset chord turns back on itself).</summary>
        static bool RingStripFits(List<FanCorner> ring, in CornerWalk cw, Vector3 origin)
        {
            int nR = ring.Count;
            for (int j = cw.g0; j != cw.g1; j = (j + 1) % nR)
            {
                int jn = (j + 1) % nR;
                var a = ring[j].pos; var b = ring[jn].pos;
                var oa = GroupOut(ring, cw, j) * GroupW(ring, cw, j);
                var ob = GroupOut(ring, cw, jn) * GroupW(ring, cw, jn);
                var ch = new Vector2(b.x - a.x, b.z - a.z);
                var och = ch + ob - oa;
                if (Vector2.Dot(ch, och) <= 0.05f * ch.sqrMagnitude) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        //  Drawing
        // ------------------------------------------------------------------
        static readonly Vector3[] walkProfA = new Vector3[4], walkProfB = new Vector3[4];

        /// <summary>A walk's cross-section at a drawn edge point (tile-local,
        /// at tarmac height): the curb's foot an inch under the edge, the
        /// face's top, the nose, the back of the walk. Collapsed onto the
        /// foot where the width is 0 (a ramp's end).</summary>
        static void WalkProfile(Vector3 e, Vector2 outw, float w, Vector3[] p, float curb = 1f)
        {
            float yFoot = e.y - RoadsideRules.EdgeDropM;
            if (w <= 0f) { p[0] = p[1] = p[2] = p[3] = new Vector3(e.x, yFoot, e.z); return; }
            var o = new Vector3(outw.x, 0f, outw.y);
            p[0] = new Vector3(e.x, yFoot, e.z);
            p[1] = e + o * CurbFaceOut; p[1].y = e.y + Mathf.Max(CurbFaceH * curb, -0.5f * RoadsideRules.EdgeDropM);
            p[2] = e + o * CurbNoseOut; p[2].y = e.y + CurbH * curb;
            p[3] = e + o * w; p[3].y = e.y + CurbH * curb + WalkCrossFall * (w - CurbNoseOut);
        }

        /// <summary>A curb cut's lip over the gutter (a driveway or a lot
        /// entrance's apron): 2 cm of the 15.</summary>
        const float CurbCutShare = 0.15f;
        /// <summary>How finely a walk span is read for curb cuts.</summary>
        const float CurbCutStepM = 1.0f;
        static readonly List<float> cutT = new List<float>(64);
        static readonly List<float> cutC = new List<float>(64);

        static readonly float[] walkBandV = { 0f, CurbFaceOut / WalkTexM, CurbNoseOut / WalkTexM };

        /// <summary>The curb and the walk between two cross-sections, U along
        /// (<paramref name="uA"/>, <paramref name="uB"/>: metres / WalkTexM),
        /// V across from the curb's foot.</summary>
        static void LayWalk(Bucket bk, Vector3[] pa, Vector3[] pb, float wA, float wB, float uA, float uB)
        {
            for (int q = 0; q < 3; q++)
            {
                float vA0 = walkBandV[q], vB0 = walkBandV[q];
                float vA1 = q < 2 ? walkBandV[q + 1] : wA / WalkTexM, vB1 = q < 2 ? walkBandV[q + 1] : wB / WalkTexM;
                if (wA <= 0f) { vA0 = vA1 = 0f; }
                if (wB <= 0f) { vB0 = vB1 = 0f; }
                bk.Up(pa[q], pa[q + 1], pb[q + 1], pb[q],
                      new Vector2(uA, vA0), new Vector2(uA, vA1), new Vector2(uB, vB1), new Vector2(uB, vB0));
            }
        }

        /// <summary>The back of a walk as a verge's start: EmitStrip lays a
        /// verge from an inch under the point it is given, so the point is
        /// the walk's back an inch up. Width 0: the drawn edge itself.</summary>
        static Vector3 WalkBack(Vector3 e, Vector2 outw, float w, float curb = 1f)
        {
            if (w <= 0f) return e;
            var b = e + new Vector3(outw.x, 0f, outw.y) * w;
            b.y = e.y + CurbH * curb + WalkCrossFall * (w - CurbNoseOut) + RoadsideRules.EdgeDropM;
            return b;
        }

        static Bucket WalkBucket => buckets[(int)Slot.Sidewalk];

        /// <summary>Span i's side has a walk at both ends (no ramp): its curb hides the inch under the edge.</summary>
        static bool FullWalkSpan(CityMap.Edge e, int i, int side) =>
            walkEdgeNow == e.index && i >= 1 && i < sections.Count && WalkArr(side)[i - 1] > 0f && WalkArr(side)[i] > 0f;

        /// <summary>
        /// EmitSide's plain-verge span with a walk at either end: the curb and
        /// walk, then the verge from the walk's back. False where the span
        /// has no walk (the verge as before).
        /// </summary>
        static bool EmitWalkSpan(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int i, int side,
                                 Vector3 eA, Vector3 eB, Vector2 outA, Vector2 outB, float sA, float sB, StripShape verge)
        {
            if (walkEdgeNow != e.index || i < 1 || i >= sections.Count) return false;
            var arr = WalkArr(side);
            float wA = arr[i - 1], wB = arr[i];
            if (wA <= 0f && wB <= 0f) return false;
            float len = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            // CURB CUTS: where the walk crosses a lot entrance's apron or a
            // driveway's (ApronAt), its curb drops to a lip, so a car turns
            // in over 2 cm, not 15; read every metre, eased over one
            cutT.Clear(); cutC.Clear();
            var o = tm.origin;
            int steps = Mathf.Clamp(Mathf.CeilToInt(len / CurbCutStepM), 1, 63);
            bool anyCut = false;
            for (int j = 0; j <= steps; j++)
            {
                float t = (float)j / steps;
                float w = Mathf.Lerp(wA, wB, t);
                var ou = Vector2.Lerp(outA, outB, t).normalized;
                var ep = Vector3.Lerp(eA, eB, t);
                bool cut = w > 0f && ApronAt(map, ep.x + o.x + ou.x * w * 0.5f, ep.z + o.z + ou.y * w * 0.5f);
                anyCut |= cut;
                cutT.Add(t); cutC.Add(cut ? CurbCutShare : 1f);
            }
            if (!anyCut) { cutT.Clear(); cutC.Clear(); cutT.Add(0f); cutC.Add(1f); cutT.Add(1f); cutC.Add(1f); }
            for (int j = 1; j < cutT.Count; j++)
            {
                float t0 = cutT[j - 1], t1 = cutT[j];
                var pA = Vector3.Lerp(eA, eB, t0); var pB = Vector3.Lerp(eA, eB, t1);
                var oA = Vector2.Lerp(outA, outB, t0).normalized; var oB = Vector2.Lerp(outA, outB, t1).normalized;
                float w0 = Mathf.Lerp(wA, wB, t0), w1 = Mathf.Lerp(wA, wB, t1);
                // a ramp end keeps its zero width: the lerp only reaches it at the end
                if (j == 1 && wA <= 0f) w0 = 0f;
                if (j == cutT.Count - 1 && wB <= 0f) w1 = 0f;
                WalkProfile(pA, oA, w0, walkProfA, cutC[j - 1]);
                WalkProfile(pB, oB, w1, walkProfB, cutC[j]);
                LayWalk(WalkBucket, walkProfA, walkProfB, w0, w1, Mathf.Lerp(sA, sB, t0) / WalkTexM, Mathf.Lerp(sA, sB, t1) / WalkTexM);
                EmitStrip(map, trims, tm, WalkBack(pA, oA, w0, cutC[j - 1]), WalkBack(pB, oB, w1, cutC[j]), oA, oB, verge);
            }
            WalkStats.walkM += len; WalkStats.spans++;
            if (wA <= 0f || wB <= 0f) WalkStats.ramps++;
            return true;
        }

        /// <summary>
        /// A junction corner's walk (the fan's perimeter loop, ring in tile
        /// space): the curb round the return, the walk to P (kind 1) or a
        /// strip round the ring (kind 2), and the verges behind it.
        /// </summary>
        static void EmitCornerWalk(CityMap map, Trims trims, TileMeshes tm, List<FanCorner> ring, int kind, in CornerWalk cw)
        {
            var o = tm.origin;
            var bk = WalkBucket;
            int nR = ring.Count;
            var verge = new StripShape { shoulder = VergeShoulderM, maxRun = VergeMaxRunM, noFins = true };
            if (kind == 2)
            {
                float u = 0f;
                for (int j = cw.g0; j != cw.g1; j = (j + 1) % nR)
                {
                    int jn = (j + 1) % nR;
                    var a = ring[j].pos; var b = ring[jn].pos;
                    var oa = GroupOut(ring, cw, j); var ob = GroupOut(ring, cw, jn);
                    float wa = GroupW(ring, cw, j), wb = GroupW(ring, cw, jn);
                    float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                    WalkProfile(a, oa, wa, walkProfA);
                    WalkProfile(b, ob, wb, walkProfB);
                    LayWalk(bk, walkProfA, walkProfB, wa, wb, u / WalkTexM, (u + len) / WalkTexM);
                    EmitStrip(map, trims, tm, WalkBack(a, oa, wa), WalkBack(b, ob, wb), oa, ob, verge);
                    u += len;
                }
                WalkStats.cornersStrip++;
                return;
            }

            // kind 1: the curb round the return, nose to nose ...
            var k0 = ring[cw.g0]; var k1 = ring[cw.g1];
            float u2 = 0f;
            Vector3 prevNose = default; float prevU = 0f;
            var P3 = new Vector3(cw.P.x - o.x, 0f, cw.P.y - o.z);
            var B0 = WalkBack(k0.pos, cw.n0, cw.w0); B0.y -= RoadsideRules.EdgeDropM;
            var B1 = WalkBack(k1.pos, cw.n1, cw.w1); B1.y -= RoadsideRules.EdgeDropM;
            P3.y = 0.5f * (B0.y + B1.y);
            Vector2 UV(Vector3 p) => new Vector2((p.x + o.x) / WalkTexM, (p.z + o.z) / WalkTexM);
            for (int j = cw.g0; ; j = (j + 1) % nR)
            {
                var kp = ring[j].pos;
                WalkProfile(kp, GroupOut(ring, cw, j), CurbNoseOut + 0.01f, walkProfB);
                if (j != cw.g0)
                {
                    float len = Vector2.Distance(new Vector2(prevNose.x, prevNose.z), new Vector2(walkProfB[2].x, walkProfB[2].z));
                    // the curb's two bands (foot to face top, face top to nose)
                    for (int q = 0; q < 2; q++)
                        bk.Up(walkProfA[q], walkProfA[q + 1], walkProfB[q + 1], walkProfB[q],
                              new Vector2(prevU / WalkTexM, walkBandV[q]), new Vector2(prevU / WalkTexM, walkBandV[q + 1]),
                              new Vector2((u2 + len) / WalkTexM, walkBandV[q + 1]), new Vector2((u2 + len) / WalkTexM, walkBandV[q]));
                    // ... and the walk from the nose back to P
                    UpTri(bk, P3, walkProfA[2], walkProfB[2], UV(P3), UV(walkProfA[2]), UV(walkProfB[2]));
                    u2 += len;
                }
                System.Array.Copy(walkProfB, walkProfA, 4);
                prevNose = walkProfB[2]; prevU = u2;
                if (j == cw.g1) break;
            }
            // the two ends: from each arm's nose out to its back-of-walk point, to P
            WalkProfile(k0.pos, cw.n0, CurbNoseOut + 0.01f, walkProfA);
            UpTri(bk, P3, B0, walkProfA[2], UV(P3), UV(B0), UV(walkProfA[2]));
            WalkProfile(k1.pos, cw.n1, CurbNoseOut + 0.01f, walkProfB);
            UpTri(bk, P3, walkProfB[2], B1, UV(P3), UV(walkProfB[2]), UV(B1));
            // the verges behind: T0' to P to T1', mitred on P's bisector
            var bis = cw.n0 + cw.n1;
            bis = bis.sqrMagnitude > 1e-6f ? bis.normalized : cw.n0;
            var up = Vector3.up * RoadsideRules.EdgeDropM;
            EmitStrip(map, trims, tm, B0 + up, P3 + up, cw.n0, bis, verge);
            EmitStrip(map, trims, tm, P3 + up, B1 + up, bis, cw.n1, verge);
            WalkStats.cornersPoly++;
        }

        // ------------------------------------------------------------------
        //  THE FRONTAGE. Past the walk the land was grass or concrete by the
        //  8 m lattice cell (PavedCell), so a walk street outside the core
        //  ran past a checkerboard of grass squares - the corners' "grass
        //  triangles" were mostly those cells. A cell whose centre is within
        //  a walk street's frontage (the walk and FrontageM behind it) is
        //  paved: the street's land reads as one paved frontage, and the
        //  lawns start behind it.
        // ------------------------------------------------------------------
        /// <summary>How far past a walk's back the frontage is paved.</summary>
        const float FrontageM = 7f;
        static readonly HashSet<int> frontSegs = new HashSet<int>();

        /// <summary>Is a lattice cell's centre on a walk street's frontage?</summary>
        static bool WalkFrontage(CityMap map, Vector2 c)
        {
            if (!WalksOn) return false;
            EnsureWalkCache(map);
            float reach = WalkW + FrontageM + 10f;
            frontSegs.Clear();
            map.EdgeSegsInRect(c - Vector2.one * reach, c + Vector2.one * reach, frontSegs);
            foreach (int packed in frontSegs)
            {
                var e = map.edges[packed >> 12];
                if (!WalkEdge(map, e)) continue;
                int seg = packed & 0xFFF;
                if (seg + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[seg], b = e.pts[seg + 1], ab = b - a;
                float l2 = ab.sqrMagnitude;
                float u = l2 > 1e-4f ? Mathf.Clamp01(Vector2.Dot(c - a, ab) / l2) : 0f;
                if (Vector2.Distance(c, a + ab * u) < e.width * 0.5f + WalkW + FrontageM) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  LOT CURBS. A lot was cut flush into the lattice (L8: no lift),
        //  so where it met the land behind it there was nothing - an OSM
        //  outline in asphalt on grass. Its ring now carries a curb wherever
        //  it is not the street's frontage (the walk and its apron are there),
        //  a building's wall or another lot: a 6 in concrete kerb on the land
        //  side, its face from under the lot's edge, its back tucked under
        //  the lattice, a 0.65 m ridge a wheel rides over.
        //  PSX_CITY_LOTCURBS=0: none.
        // ------------------------------------------------------------------
        public static bool LotCurbsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LOTCURBS") != "0";
        static readonly float[] lotCurbU = { -0.02f, 0.10f, 0.25f, 0.55f };
        static readonly float[] lotCurbH = { -0.02f, 0.13f, 0.15f, -0.03f };
        static readonly Vector3[] lotCurbA = new Vector3[4], lotCurbB = new Vector3[4];
        public static float lotCurbM;

        static void EmitLotCurbs(CityMap map, TileMeshes tm, Vector2 min)
        {
            if (!WalksOn || !LotCurbsOn || !LotsOn || map.lots == null) return;
            var o = tm.origin;
            var bk = WalkBucket;
            foreach (int li in tileLots)
            {
                var lot = map.lots[li];
                if (lot.aisleOnly || lot.ring == null || lot.ring.Length < 3) continue;
                var r = lot.ring;
                int n = r.Length;
                float area = 0f;
                for (int i = 0; i < n; i++) { var p = r[i]; var q = r[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
                float sgn = area > 0f ? 1f : -1f;   // anticlockwise: the lot on the left, the land on the right
                float uAcc = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = r[i], b = r[(i + 1) % n];
                    var d = b - a; float len = d.magnitude;
                    if (len < 0.05f) continue;
                    var outw = new Vector2(d.y, -d.x) / len * sgn;
                    int k = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
                    for (int j = 0; j < k; j++)
                    {
                        var pa = a + d * ((float)j / k); var pb = a + d * ((float)(j + 1) / k);
                        var m = 0.5f * (pa + pb);
                        float uA = uAcc + len * j / k, uB = uAcc + len * (j + 1) / k;
                        if (m.x < min.x || m.x >= min.x + TileSize || m.y < min.y || m.y >= min.y + TileSize) continue;
                        if (!LotCurbHere(map, li, m, outw)) continue;
                        for (int q = 0; q < 4; q++)
                        {
                            var qa = pa + outw * lotCurbU[q]; var qb = pb + outw * lotCurbU[q];
                            lotCurbA[q] = new Vector3(qa.x - o.x, LatticeY(map, qa.x, qa.y) + lotCurbH[q], qa.y - o.z);
                            lotCurbB[q] = new Vector3(qb.x - o.x, LatticeY(map, qb.x, qb.y) + lotCurbH[q], qb.y - o.z);
                        }
                        for (int q = 0; q < 3; q++)
                            bk.Up(lotCurbA[q], lotCurbA[q + 1], lotCurbB[q + 1], lotCurbB[q],
                                  new Vector2(uA / WalkTexM, lotCurbU[q] / WalkTexM), new Vector2(uA / WalkTexM, lotCurbU[q + 1] / WalkTexM),
                                  new Vector2(uB / WalkTexM, lotCurbU[q + 1] / WalkTexM), new Vector2(uB / WalkTexM, lotCurbU[q] / WalkTexM));
                        lotCurbM += len / k;
                    }
                    uAcc += len;
                }
            }
        }

        static readonly HashSet<int> lotCurbSegs = new HashSet<int>();

        /// <summary>A lot's edge takes a curb here: not on a street's
        /// frontage (any road's pavement, walk and apron within reach), not
        /// along a building, not against another lot.</summary>
        static bool LotCurbHere(CityMap map, int li, Vector2 m, Vector2 outw)
        {
            var land = m + outw * 0.8f;
            if (ApronAt(map, land.x, land.y) || map.AnyFootprintNear(m, 1.5f)) return false;
            lotCurbSegs.Clear();
            const float reach = 30f;
            map.EdgeSegsInRect(m - Vector2.one * reach, m + Vector2.one * reach, lotCurbSegs);
            foreach (int packed in lotCurbSegs)
            {
                var e = map.edges[packed >> 12];
                int seg = packed & 0xFFF;
                if (seg + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[seg], b = e.pts[seg + 1], ab = b - a;
                float l2 = ab.sqrMagnitude;
                float u = l2 > 1e-4f ? Mathf.Clamp01(Vector2.Dot(m - a, ab) / l2) : 0f;
                if (Vector2.Distance(m, a + ab * u) < e.width * 0.5f + WalkW + 3f) return false;
            }
            foreach (int lj in tileLots)
            {
                if (lj == li) continue;
                var r = map.lots[lj].ring;
                if (r != null && InRing(r, land)) return false;
            }
            return true;
        }

        /// <summary>A triangle facing up whatever order its corners come in.</summary>
        static void UpTri(Bucket bk, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return;
            // Tri(a, b, c) draws (a, c, b), whose normal is Cross(c - a, b - a)
            if (n.y > 0f) bk.Tri(a, c, b, ua, uc, ub);
            else bk.Tri(a, b, c, ua, ub, uc);
        }
    }
}
