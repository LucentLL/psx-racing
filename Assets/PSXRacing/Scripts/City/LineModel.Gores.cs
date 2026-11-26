using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// c7roads round 3, R1: SPLITS IN THE LINE MODEL (owner 11-25, CltC7 shots 80 and 81).
    ///
    /// Where two arms leave or join a node on the same side within GoreMaxDeg (a split, a merge, a link or slip
    /// mouth; at least one arm one-way), OSM draws both centre lines from the node, so their travelled ways overlap
    /// for metres (81: e3187 and e10102 overlap by 10.7 m at n4449 and still 0.9 m 12 m out). Every generator then
    /// lays its own edge stuff (verges, foreslopes, faces) from its own edge - into the other arm's lane.
    ///
    /// Now each such arm pair is pushed apart in the line model: a lateral SHIFT ease (the same kind the median
    /// tapers use) moves the moving arm(s) outward, away from the other, so the two travelled ways share one
    /// boundary from the node and part where their own lines part. Who moves: the arm that is NOT the third arm's
    /// mitred through partner (the through lanes stay put); with no through partner both move, half each. The
    /// shift fades with the median tapers' smoothstep over the shortest length (from GoreLenSteps) that leaves no
    /// overlap anywhere: the drawn lanes run on along the shared boundary while OSM's lines part beneath them, so
    /// the lanes do not move sideways in the world (the line gate's SHIFT / KINK classes judge the result). The ribbons, lines, traffic, the AI
    /// and the race line all read the same extents (LineModel.Extents / LaneCentre), so all of them follow.
    ///
    /// THE NOSE: where the gap between the two travelled ways reaches GoreNoseM (2.4 m: a 1.2 m nose and its 0.6 m
    /// offsets, Iowa DM 6A-11 / MUTCD 3B.10, the same figure as the split noses' SplitIslandGapM). From the node to
    /// the nose the land between is PAVEMENT (CityMeshes.GoreFill: a gore at road height in the arm's own road
    /// slot and texture frame); past it the land is what it was.
    ///
    /// PSX_CITY_GORESPLIT=1 turns R1 on; it is OFF by default (it failed the line gate). R0 (the bays) runs either way.
    /// Not touched: the SPLT carriageway splits and refuge nodes (their own profiles), two two-way arms (a Y
    /// junction: a fan), decks and tunnels, an arm pair that are each other's mitre partners (a bend).
    /// </summary>
    public static partial class LineModel
    {
        /// <summary>Opt-in (=1): measured city-wide it moves too many lanes (line gate SHIFT +619, SJOG +156, STEP +48, MEDIAN +4 at 8265a347).</summary>
        public static bool GoreSplitOn = System.Environment.GetEnvironmentVariable("PSX_CITY_GORESPLIT") == "1";
        public const float GoreMaxDeg = 60f, GoreNoseM = SplitIslandGapM, GoreLaneMarginM = 0.15f, GoreChordM = 10f, GoreReachM = 70f;
        static readonly float[] GoreLenSteps = { 6f, 8f, 10f, 12f, 15f, 18f, 22f, 26f, 30f, 36f, 42f, 50f, 60f, 70f, 85f, 100f };

        public sealed class GoreSplit
        {
            public int node, x, y;          // the two arms (edges at the node)
            public float angle, overlap0, overlapMax, shiftX, shiftY, len, dShared, dNose, after;
            public bool both, ok; public string note = "";
        }
        public static readonly List<GoreSplit> GoreSplits = new List<GoreSplit>();
        public static int GorePairs, GoreOverlapBefore, GoreOverlapAfter, GoreSkippedDeck, GoreZoneLeft;
        public static float GoreBuildMs;
        static readonly Dictionary<long, GoreSplit> goreAt = new Dictionary<long, GoreSplit>();
        static long GoreKey(int node, int edge) => ((long)node << 32) | (uint)edge;
        /// <summary>The gore pair an arm end belongs to (null: none).</summary>
        public static GoreSplit GoreOf(int node, int edge) => goreAt.Count > 0 && goreAt.TryGetValue(GoreKey(node, edge), out var g) ? g : null;

        /// <summary>One station of an arm's chain walked away from a node.</summary>
        struct GoreSt { public Vector2 p, left; public float plus, minus; public CityMap.Edge e; public float s; }

        /// <summary>Walk an arm away from node through its mitred partners: stations every ds up to reach.</summary>
        static List<GoreSt> GoreWalk(CityMap map, System.Func<CityMap.Edge, int, int> partner, CityMap.Edge e, int node, float ds, float reach)
        {
            var o = new List<GoreSt>((int)(reach / ds) + 2);
            float d0 = 0f; int from = node; var E = e;
            for (int depth = 0; depth < 8 && E != null; depth++)
            {
                bool fwd = E.a == from;
                for (float d = Mathf.Max(0f, o.Count * ds - d0); d <= E.length + 1e-3f; d += ds)
                {
                    float s = fwd ? d : E.length - d;
                    var t = E.TangentAt(s); if (!fwd) t = -t;
                    Extents(E, s, out float eM, out float eP);
                    o.Add(new GoreSt { p = E.PointAt(s), left = new Vector2(-t.y, t.x), plus = fwd ? eP : eM, minus = fwd ? eM : eP, e = E, s = s });
                    if (o.Count * ds > reach) return o;
                }
                d0 += E.length;
                int far = fwd ? E.b : E.a;
                int pi = partner(E, far);
                if (pi < 0 || map.edges[pi] == E || map.edges[pi].a == map.edges[pi].b) break;
                E = map.edges[pi]; from = far;
            }
            return o;
        }

        static float GoreShiftAt(float s0, float len, float d) => d >= len ? 0f : s0 * (1f - Smooth(Mathf.Max(0f, d) / len));

        /// <summary>The overlap of X's travelled way (grown by margin) into Y's at each X station, with X shifted
        /// outward by shX(d) and Y by shY(d) (both away from each other). Returns the worst; gapOut (optional) gets
        /// the gap at each station (negative = overlap).</summary>
        static float GoreOverlap(List<GoreSt> X, List<GoreSt> Y, int sideX, int sideY, float sX0, float sY0, float len, float ds, float margin, float[] gapOut, float fromX = 0f, float fromY = 0f)
        {
            // sideX: the side of X (in X's away frame, +1 left) that faces Y; sideY likewise for Y
            float worst = -1e9f; int j = 0;
            for (int i = 0; i < X.Count; i++)
            {
                var a = X[i]; float d = i * ds;
                float shx = GoreShiftAt(sX0, len, d);
                float inX = sideX > 0 ? a.plus : a.minus;
                var q = a.p + a.left * (sideX * (inX + margin) - sideX * shx);
                // nearest Y station (moving window)
                float best = (Y[j].p - q).sqrMagnitude;
                int lo = Mathf.Max(0, j - 4), hi = Mathf.Min(Y.Count - 1, j + (i == 0 ? 30 : 6));
                for (int k = lo; k <= hi; k++) { float dd = (Y[k].p - q).sqrMagnitude; if (dd < best) { best = dd; j = k; } }
                if (j == Y.Count - 1 && i > 0) { if (gapOut != null) for (int r = i; r < X.Count; r++) gapOut[r] = 99f; break; }
                var b = Y[j]; float dy = j * ds;
                float shy = GoreShiftAt(sY0, len, dy);
                float inY = sideY > 0 ? b.plus : b.minus;
                float lat = Vector2.Dot(q - b.p, b.left);                 // q in Y's away frame
                float edgeY = sideY * (inY + margin) - sideY * shy;        // Y's inner edge (grown)
                float gap = sideY * (lat - edgeY);                         // + : q lies outside Y on X's side
                // q past Y's far edge entirely: still inside (the bands cross); count it as overlap
                if (gapOut != null) gapOut[i] = gap;
                // inside either arm's junction fan (its trim) the fan is the one surface: not an overlap of ribbons
                if (d < fromX || dy < fromY) continue;
                if (-gap > worst) worst = -gap;
            }
            return worst;
        }

        /// <summary>PropagateShift without the room cap (the gore's length was chosen on the chain it runs on).</summary>
        static void GoreShift(CityMap map, List<Ease>[] add, System.Func<CityMap.Edge, int, int> partner, CityMap.Edge e, int fromNode, float shift, float len, float d0, int depth)
        {
            (add[e.index] ??= new List<Ease>(2)).Add(new Ease { shift = true, fromA = e.a == fromNode, dw = shift, len = len, d0 = d0 });
            if (d0 + e.length >= len || depth >= 8) return;
            int far = e.a == fromNode ? e.b : e.a;
            int pi = partner(e, far);
            if (pi < 0) return;
            var p = map.edges[pi];
            if (p == e || p.a == p.b) return;
            GoreShift(map, add, partner, p, far, ((e.a == fromNode) == (p.a == far)) ? shift : -shift, len, d0 + e.length, depth + 1);
        }

        // ------------------------------------------------------------------
        //  R0 (owner 11-26 on spot 80, "What are these roads?"): A TURN BAY IS A LANE OF ITS HOST.
        //  OSM draws a left- or right-turn bay as its own line from its carriageway, often diagonally across the
        //  median, to the junction node. A one-way link leaving a tier-1 through host within 35 degrees whose way
        //  (under BayLenMaxM) ends on a road of its host's name and lies within BayCorridorM of the host's
        //  pavement for BayShareMin of its stations is a BAY: its lanes are laid against the host's through lanes
        //  (a sampled shift toward the host: the gap less BayInsetM, so the merge zone reads it as inside the host
        //  and carries it as the host's aux lane) from where it leaves the through lanes to the TURN POINT, then
        //  ease back onto its own line over the crossing run (BayCrossK x the lateral move, at least
        //  BayCrossFloorM) to its end node. The merge zone (CityMeshes.Merge) then cuts the link's ribbon to the
        //  nose at the turn point and widens the host there: no ribbon, verge or land of the link along the bay.
        // ------------------------------------------------------------------
        public const float BayMedianSeekM = 40f, BayLenMaxM = 250f, BayCorridorM = 20f, BayShareMin = 0.6f, BayInsetM = 0.3f, BayCrossK = 1.6f, BayCrossFloorM = 15f;
        public sealed class Bay { public int node, branch, host; public float wayLen, turnAt, gapEnd, inside; public bool median; }
        public static readonly List<Bay> Bays = new List<Bay>();
        static readonly HashSet<int> bayEdges = new HashSet<int>();
        /// <summary>R0: is this link edge part of a bay laid along its host?</summary>
        public static bool IsBayEdge(int edge) => bayEdges.Count > 0 && bayEdges.Contains(edge);

        static void BayShift(CityMap map, System.Func<CityMap.Edge, int, int> partner, List<Ease>[] add, CityMap.Edge B, CityMap.Edge H, int n, List<GoreSt> WB, int sideB, int sideH)
        {
            if (bayEdges.Contains(B.index)) return;
            const float ds = 1f;
            // the bay's own way along its walk (walked again to the longest bay)
            WB = GoreWalk(map, partner, B, n, ds, BayLenMaxM + 5f);
            int nB = 0; while (nB < WB.Count && WB[nB].e.wayId == B.wayId) nB++;
            if (nB < 10) return;
            float wayLen = (nB - 1) * ds;
            if (wayLen > BayLenMaxM) return;
            var wb = WB.GetRange(0, nB);
            // its far node must be on a road of the host's name
            var last = wb[nB - 1].e; int far = (wb[nB - 1].s > last.length * 0.5f) ? last.b : last.a;
            bool named = false;
            foreach (int i in map.nodeEdges[far]) { var h = map.edges[i]; if (!h.link && !string.IsNullOrEmpty(H.name) && h.name == H.name) { named = true; break; } }
            if (!named) return;
            var WH = GoreWalk(map, partner, H, n, ds, wayLen + 40f);
            if (WH.Count < 4) return;
            var gaps = new float[nB];
            GoreOverlap(wb, WH, sideB, sideH, 0f, 0f, 1f, ds, 0f, gaps);
            // the corridor: a median bay (another carriageway of the host's name lies beyond it, away from the host,
            // within BayMedianSeekM) may run anywhere in the median; an outer bay stays within one lane + 1 m
            var mid = wb[nB / 2];
            int awaySide = -sideB;   // away from the host, in the bay's away frame
            bool median = false;
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(mid.p - Vector2.one * BayMedianSeekM, mid.p + Vector2.one * BayMedianSeekM, segs);
            foreach (int pk in segs)
            {
                var o = map.edges[pk >> 12];
                if (o.link || o.name != H.name || o.index == H.index) continue;
                int k = pk & 0xFFF; var a0 = o.pts[k]; var ab = o.pts[k + 1] - a0;
                float tt = Mathf.Clamp01(Vector2.Dot(mid.p - a0, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                var q = a0 + ab * tt;
                if ((q - mid.p).magnitude > BayMedianSeekM) continue;
                if (Vector2.Dot(q - mid.p, mid.left) * awaySide > 2f) { median = true; break; }
            }
            float corridor = median ? BayCorridorM : RoadProfiles.LaneM + 1f;
            int inside = 0; for (int i = 0; i < nB; i++) if (gaps[i] <= corridor) inside++;
            if (inside < BayShareMin * nB) return;
            // the turn point: the crossing run before the end node
            int iEnd = nB - 1; while (iEnd > 0 && gaps[iEnd] > 90f) iEnd--;
            float gEnd = Mathf.Max(0f, gaps[iEnd]);
            float cross = Mathf.Max(BayCrossFloorM, BayCrossK * (gEnd + RoadProfiles.LaneM));
            float turnAt = wayLen - cross;
            if (turnAt < 10f) return;
            int iT = Mathf.Min(nB - 1, Mathf.RoundToInt(turnAt / ds));
            var want = new float[nB + 1];
            // the gap is between the pavements; the zone reads lane edges: both shoulders and the inset inside
            float inset = BayInsetM + B.shl + B.shr + H.shl + H.shr;
            for (int i = 0; i <= iT; i++) want[i] = gaps[i] > 90f ? 0f : Mathf.Max(0f, gaps[i] + inset);
            // round the start (where the lanes leave the through lanes) over 4 m
            for (int i = 1; i <= iT; i++) want[i] = Mathf.Min(want[i], want[i - 1] + 2f * ds);
            for (int i = iT + 1; i < nB; i++) want[i] = want[iT] * (1f - Smooth((i - iT) * ds / Mathf.Max(1f, wayLen - iT * ds)));
            want[nB] = 0f;
            // one profile ease per piece of the way, in its own frame (toward the host = +sideB in the away frame)
            float d0 = 0f; int from = n; var E = B;
            for (int guard = 0; guard < 8 && E != null && E.wayId == B.wayId; guard++)
            {
                bool fwd = E.a == from;
                var prof = new float[want.Length];
                for (int i = 0; i < want.Length; i++) prof[i] = (fwd ? 1f : -1f) * sideB * want[i];
                (add[E.index] ??= new List<Ease>(2)).Add(new Ease { shift = true, fromA = fwd, prof = prof, profD0 = 0f, profDs = ds, d0 = d0, len = wayLen + 1f });
                bayEdges.Add(E.index);
                d0 += E.length;
                int nf = fwd ? E.b : E.a;
                if (nf == far) break;
                int pi = partner(E, nf);
                if (pi < 0) break;
                E = map.edges[pi]; from = nf;
            }
            Bays.Add(new Bay { node = n, branch = B.index, host = H.index, wayLen = wayLen, turnAt = turnAt, gapEnd = gEnd, inside = (float)inside / nB, median = median });
        }

        static void BuildGoreSplits(CityMap map, System.Func<CityMap.Edge, int, int> partner, Dictionary<int, Split> splitAt)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            GoreSplits.Clear(); goreAt.Clear(); Bays.Clear(); bayEdges.Clear();
            GorePairs = 0; GoreOverlapBefore = 0; GoreOverlapAfter = 0; GoreSkippedDeck = 0; GoreZoneLeft = 0;
            if ((!GoreSplitOn && !CityMeshes.BayLanesOn) || map.nodeEdges == null) { GoreBuildMs = 0f; return; }
            const float ds = 1f;
            var add = new List<Ease>[map.edges.Length];
            var moved = new HashSet<int>();
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var ne = map.nodeEdges[n];
                if (ne == null || ne.Count < 3) continue;
                if (splitAt.ContainsKey(n) || IsRefugeNode(n)) continue;
                for (int i = 0; i < ne.Count; i++)
                    for (int k = i + 1; k < ne.Count; k++)
                    {
                        var X = map.edges[ne[i]]; var Y = map.edges[ne[k]];
                        if (X == Y || X.a == X.b || Y.a == Y.b) continue;
                        if (!X.oneway && !Y.oneway) continue;
                        if (partner(X, n) == Y.index) continue;
                        Vector2 dX = (X.PointAt(X.a == n ? Mathf.Min(GoreChordM, X.length) : Mathf.Max(0f, X.length - GoreChordM)) - map.nodes[n]);
                        Vector2 dY = (Y.PointAt(Y.a == n ? Mathf.Min(GoreChordM, Y.length) : Mathf.Max(0f, Y.length - GoreChordM)) - map.nodes[n]);
                        if (dX.sqrMagnitude < 1e-4f || dY.sqrMagnitude < 1e-4f) continue;
                        float ang = Vector2.Angle(dX, dY);
                        if (ang >= GoreMaxDeg) continue;
                        GorePairs++;
                        if (X.bridge || Y.bridge || X.tunnel || Y.tunnel) { GoreSkippedDeck++; continue; }
                        var WX = GoreWalk(map, partner, X, n, ds, GoreReachM);
                        var WY = GoreWalk(map, partner, Y, n, ds, GoreReachM + 20f);
                        if (WX.Count < 4 || WY.Count < 4) continue;
                        // the side of each that faces the other (away frames)
                        int sideX = Vector2.Dot(WX[0].left, dY) >= 0f ? 1 : -1;
                        int sideY = Vector2.Dot(WY[0].left, dX) >= 0f ? 1 : -1;
                        float trX = SplitTrimHook != null ? SplitTrimHook(X, n) : 0f, trY = SplitTrimHook != null ? SplitTrimHook(Y, n) : 0f;
                        float ov0 = GoreOverlap(WX, WY, sideX, sideY, 0f, 0f, 1f, ds, GoreLaneMarginM, null, trX, trY);
                        // who moves: the third arm's through partner stays
                        bool moveX = true, moveY = true;
                        for (int u = 0; u < ne.Count; u++)
                        {
                            if (u == i || u == k) continue;
                            int pu = partner(map.edges[ne[u]], n);
                            if (pu == Y.index) moveY = false; else if (pu == X.index) moveX = false;
                        }
                        if (!moveX && !moveY) moveX = moveY = true;
                        // a one-way link branching off a tier-1 through host within the merge zones' 35 degrees is the
                        // zones' (CityMeshes.Merge: the host's aux lane to the nose, the ramp cut there; R0's bays)
                        if (CityMeshes.MergeZonesOn && ang <= 35f && moveX != moveY)
                        {
                            var B = moveX ? X : Y; var H = moveX ? Y : X;
                            if (B.link && B.oneway && CityTier.Of(H) == CityTier.T1)
                            {
                                GoreZoneLeft++;
                                if (CityMeshes.BayLanesOn) BayShift(map, partner, add, B, H, n, B == X ? WX : WY, B == X ? sideX : sideY, B == X ? sideY : sideX);
                                continue;
                            }
                        }
                        if (!GoreSplitOn || ov0 <= 0.02f) continue;   // R1 off: the bays alone (R0)
                        GoreOverlapBefore++;
                        if (moved.Contains(X.index) && moveY) moveX = false;      // an arm already moved at this node keeps its shift
                        if (moved.Contains(Y.index) && moveX) moveY = false;
                        if (!moveX && !moveY) { GoreOverlapAfter++; continue; }
                        // the overlap at the node, laterally (the shift that makes the two share one boundary there)
                        var gaps = new float[WX.Count];
                        GoreOverlap(WX, WY, sideX, sideY, 0f, 0f, 1f, ds, 0f, gaps);
                        float need0 = Mathf.Max(0f, ov0) / Mathf.Max(0.5f, Mathf.Cos(ang * Mathf.Deg2Rad)) + 0.05f;   // the worst past the trims
                        float fx = moveX && moveY ? 0.5f : moveX ? 1f : 0f;
                        float sX0 = fx * need0, sY0 = (1f - fx) * need0;
                        // the shortest length that clears it: the drawn lanes run on along the shared boundary
                        // while OSM's lines part, then follow their own line (a taper rate does not apply: the
                        // lanes do not move sideways in the world, OSM's line moves out from under them)
                        float len = -1f, after = 0f;
                        foreach (var L in GoreLenSteps)
                        {
                            after = GoreOverlap(WX, WY, sideX, sideY, sX0, sY0, L, ds, 0f, null, trX, trY);
                            if (after <= 0.03f) { len = L; break; }
                        }
                        var g = new GoreSplit { node = n, x = X.index, y = Y.index, angle = ang, overlap0 = need0, overlapMax = ov0, both = moveX && moveY };
                        if (len < 0f)
                        {
                            g.ok = false; g.note = $"no length up to {GoreLenSteps[GoreLenSteps.Length - 1]:0} m clears it (left {after:0.00} m)";
                            GoreOverlapAfter++; GoreSplits.Add(g); continue;
                        }
                        g.ok = true; g.len = len; g.after = after;
                        g.shiftX = sX0; g.shiftY = sY0;
                        // where the two part, and where the gap reaches the nose (after the shift)
                        GoreOverlap(WX, WY, sideX, sideY, sX0, sY0, len, ds, 0f, gaps);
                        g.dShared = 0f; g.dNose = -1f;
                        for (int r = 0; r < gaps.Length; r++)
                        {
                            if (gaps[r] <= 0.1f) g.dShared = r * ds;
                            if (gaps[r] >= GoreNoseM) { g.dNose = r * ds; break; }
                        }
                        // the shift eases, in each arm's own frame (outward = away from the other)
                        if (sX0 > 0.005f) { GoreShift(map, add, partner, X, n, (X.a == n ? 1f : -1f) * -sideX * sX0, len, 0f, 0); moved.Add(X.index); }
                        if (sY0 > 0.005f) { GoreShift(map, add, partner, Y, n, (Y.a == n ? 1f : -1f) * -sideY * sY0, len, 0f, 0); moved.Add(Y.index); }
                        GoreSplits.Add(g);
                        goreAt[GoreKey(n, X.index)] = g; goreAt[GoreKey(n, Y.index)] = g;
                    }
            }
            for (int i = 0; i < add.Length; i++)
            {
                if (add[i] == null) continue;
                var e = map.edges[i];
                var old = e.lmEase;
                int n0 = old == null ? 0 : old.Length;
                var nz = new Ease[n0 + add[i].Count];
                if (old != null) System.Array.Copy(old, nz, n0);
                for (int k = 0; k < add[i].Count; k++) nz[n0 + k] = add[i][k];
                e.lmEase = nz;
            }
            GoreBuildMs = (float)clock.Elapsed.TotalMilliseconds;
        }
    }
}
