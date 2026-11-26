using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// A RIVAL'S JUNCTION TURN IS A CORNER DRIVEN ON A RADIUS (cityai 11-25; the owner, CltC7: "AI can't even turn on
    /// this track, they make 90 degree turns. Very unnatural to see."). A routed path (<see cref="RoutedPath"/>) ran
    /// the lanes' centre of one road to the node and the next road's from it: a corner. Every rival drove the corner
    /// as written - straight to the junction, a pivot at the node - because nothing between the two lines was a
    /// line. This lays, at every join of <see cref="MinDeg"/> or more, the LARGEST circular arc tangent to the two
    /// lanes' lines (searched from <see cref="TanMaxM"/> of tangent down) that keeps the car on drawn pavement:
    /// - <see cref="KerbClearM"/> off every kerb stretch of the junction's fan (<see cref="CityMeshes.FanKerbs"/>:
    ///   corner returns, islands, medians' noses, 15 m up each arm), or as far as the corner itself was where that is
    ///   less;
    /// - inside each arm's own pavement (<see cref="CityMap.Edge.PaveEdgeM"/>, <see cref="PaveKeepM"/> in) where the
    ///   arc runs outside the junction box;
    /// - both checked on the arc AND on the line the car drives it on: a two-way road's own side
    ///   (<see cref="RoutedPath"/>'s ownLaneOff, the way AIDriver.LineOffset keeps right through a junction);
    /// - its tangent no further back than the arm's trim (where the junction's pavement starts) + <see cref="PastTrimM"/>.
    /// The geometry is the map's and the trims' at run time: nothing is baked. The arc's curvature is written as its
    /// own 1/r (RoutedPath), so PlanSpeed brakes before the corner to what the tyres hold on it (AIGrip, the yaw
    /// ceiling) and the car accelerates out. No arc of <see cref="MinR"/> or more fits: the corner as before.
    /// PSX_CP_TURNS=0 (LAB row AI RACING TURNS): the corners as before (each car's next re-plan).
    /// </summary>
    public static class RacingTurns
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_CP_TURNS") != "0";
        /// <summary>(round 2) Lay the route's turns at race load and keep each turn's whole arc, so a re-plan mid-race
        /// only reads (PSX_CP_TURNS_WARM=0: the round-1 search on every plan).</summary>
        /// <summary>(round 3) Lay every turn at the route's nodes at load, not only the route's (PSX_CP_TURNS_WARMALL=0: the route's only).</summary>
        public static bool WarmAllTurns = System.Environment.GetEnvironmentVariable("PSX_CP_TURNS_WARMALL") != "0";
        public static bool WarmOn = System.Environment.GetEnvironmentVariable("PSX_CP_TURNS_WARM") != "0";
        public const float MinDeg = 30f, MaxDeg = 170f;
        public static float KerbMinM = 1.15f, NearNodeM = 60f, CoarseStepM = 2f;
        public static float KerbClearM = 1.4f, PaveKeepM = 1.0f, TanMaxM = 30f, PastTrimM = 6f, MinR = 4f, StepM = 0.5f;

        public struct Join { public int idx, node, ein, eout; public float turnDeg; }
        public struct Cut { public int i0, i1; public float r; public float deg; }

        /// <summary>Totals over every build since the last <see cref="ResetStats"/> (the self-test and the play check).</summary>
        public static int Tried, Laid, NoFit;
        public static float MinLaidR = float.PositiveInfinity;
        public static readonly List<float> LaidR = new List<float>(256);
        public static void ResetStats() { Tried = Laid = NoFit = 0; ArcReads = ArcSearches = 0; MinLaidR = float.PositiveInfinity; LaidR.Clear(); }

        static readonly List<Vector2> np = new List<Vector2>(8192), lp = new List<Vector2>(256);
        static readonly List<float> ny = new List<float>(8192), no = new List<float>(8192);
        static readonly List<Vector2> kerbSegs = new List<Vector2>(256);
        static float[] arcBuf = new float[0];

        static float Cr(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static readonly List<Vector2> oneFan = new List<Vector2>(128);
        static readonly List<int> nearNodes = new List<int>(16);
        /// <summary>Each node's fan kerbs, once per map (the trims and the map do not change during a session).</summary>
        static readonly Dictionary<int, Vector2[]> kerbCache = new Dictionary<int, Vector2[]>(1024);
        static CityMap kerbCacheMap;
        /// <summary>Each turn's tangent (node, road in, road out), once per map (-1: no arc fits).</summary>
        static readonly Dictionary<long, float> tCache = new Dictionary<long, float>(1024), clrCache = new Dictionary<long, float>(1024);
        /// <summary>(round 2) Each turn's whole arc once laid (r 0: no arc fits), once per map.</summary>
        static readonly Dictionary<long, (float T, Vector2 c, float r, float ang0, float sweep)> arcCache = new Dictionary<long, (float, Vector2, float, float, float)>(1024);
        /// <summary>Plans' turns read from <see cref="arcCache"/> and searched (the harness; cleared with the stats).</summary>
        public static int ArcReads, ArcSearches;
        public static bool LogSearches = System.Environment.GetEnvironmentVariable("PSX_CP_TURNS_LOG") == "1"; static int searchLogs;
        /// <summary>(round 3, the islands check) Every cached arc's points, every <paramref name="step"/> metres, with its
        /// turn's key (node, road in, road out).</summary>
        public static void CachedArcPoints(List<Vector2> pts, List<long> keys, float step = 1f)
        {
            pts.Clear(); keys.Clear();
            foreach (var kv in arcCache)
            {
                var a = kv.Value;
                if (a.r <= 0f) continue;
                int m = Mathf.Max(2, Mathf.CeilToInt(a.r * Mathf.Abs(a.sweep) / Mathf.Max(step, 0.1f)));
                for (int q = 0; q <= m; q++)
                {
                    float ang = a.ang0 + a.sweep * q / m;
                    pts.Add(a.c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * a.r);
                    keys.Add(kv.Key);
                }
            }
        }
        /// <summary>The fan kerbs of the join's node AND of every node within <see cref="NearNodeM"/> of it, two hops
        /// along the graph: an arc may reach a neighbouring junction's island (a split's nose, a channelised turn's
        /// island at the next node).</summary>
        static void NearKerbs(CityMap map, CityMeshes.Trims tr, int node, List<Vector2> segs)
        {
            nearNodes.Clear(); nearNodes.Add(node);
            var c = map.nodes[node];
            for (int hop = 0, from = 0; hop < 2; hop++)
            {
                int to = nearNodes.Count;
                for (int q = from; q < to; q++)
                {
                    var ne = map.nodeEdges[nearNodes[q]];
                    if (ne == null) continue;
                    foreach (int ei in ne)
                    {
                        var e = map.edges[ei];
                        int o = e.a == nearNodes[q] ? e.b : e.a;
                        if (o < 0 || nearNodes.Contains(o) || (map.nodes[o] - c).sqrMagnitude > NearNodeM * NearNodeM) continue;
                        nearNodes.Add(o);
                    }
                }
                from = to;
            }
            if (kerbCacheMap != map) { kerbCache.Clear(); tCache.Clear(); clrCache.Clear(); arcCache.Clear(); kerbCacheMap = map; }
            foreach (int nd in nearNodes)
            {
                if (!kerbCache.TryGetValue(nd, out var arr))
                {
                    CityMeshes.FanKerbs(map, tr, nd, oneFan);
                    arr = oneFan.ToArray();
                    kerbCache[nd] = arr;
                }
                segs.AddRange(arr);
            }
        }

        static float KerbDist(Vector2 q, List<Vector2> segs)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < segs.Count; i += 2)
            {
                var a = segs[i]; var ab = segs[i + 1] - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (q - (a + ab * t)).magnitude);
            }
            return best;
        }

        /// <summary>Where q stands against edge e near its end at <paramref name="node"/>: metres from the node along
        /// the edge, and metres right of the edge's points' direction. False when q is not beside its first 80 m.</summary>
        static bool Beside(CityMap.Edge e, int node, Vector2 q, out float fromNode, out float sAt, out float lat)
        {
            fromNode = sAt = lat = 0f;
            var p = e.pts; var s = e.s;
            if (p == null || p.Length < 2) return false;
            bool atA = e.a == node;
            float best = float.MaxValue;
            for (int i = 0; i + 1 < p.Length; i++)
            {
                float segFrom = atA ? s[i] : e.length - s[i + 1];
                if (segFrom > 80f) continue;
                var a = p[i]; var ab = p[i + 1] - a;
                float L2 = ab.sqrMagnitude;
                if (L2 < 1e-8f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / L2);
                var c = a + ab * t;
                float d = (q - c).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    sAt = s[i] + t * (s[i + 1] - s[i]);
                    var dir = ab / Mathf.Sqrt(L2);
                    lat = Vector2.Dot(q - c, new Vector2(dir.y, -dir.x));
                    fromNode = atA ? sAt : e.length - sAt;
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>The trim (where the arm's ribbon meets the junction's pavement) of edge e at node, or 0.</summary>
        static float TrimAt(CityMeshes.Trims tr, CityMap.Edge e, int node)
        {
            if (tr == null || tr.atA == null || e.index >= tr.atA.Length) return 0f;
            return e.a == node ? tr.atA[e.index] : e.b == node ? tr.atB[e.index] : 0f;
        }

        /// <summary>True when q (the car's line) is on drawn pavement for this turn: off the fan's kerbs by
        /// <paramref name="clear"/>, and inside its arm's pavement where it is past the arm's trim.</summary>
        static bool OnPavement(CityMap map, CityMeshes.Trims tr, int node, CityMap.Edge ein, CityMap.Edge eout, Vector2 q, float clear, List<Vector2> kerbs, out float kd)
        {
            kd = kerbs != null ? KerbDist(q, kerbs) : float.MaxValue;
            if (kd < clear) return false;
            // past the box on either arm: inside that arm's own pavement
            for (int w = 0; w < 2; w++)
            {
                var e = w == 0 ? ein : eout;
                if (!Beside(e, node, q, out float from, out float s, out float lat)) continue;
                float trim = TrimAt(tr, e, node);
                if (from <= trim + 0.5f) continue;
                float right = e.PaveEdgeM(s, 1), left = e.PaveEdgeM(s, -1);
                // only the arm the point is actually beside (within its pavement's reach either way)
                if (lat > right + 6f || lat < -left - 6f) continue;
                if (lat > right - PaveKeepM || lat < -left + PaveKeepM) return false;
            }
            return true;
        }

        /// <summary>
        /// Fillet the routed polyline's joins in place. <paramref name="orig"/> carries each point's arc on the
        /// UNFILLETED line (RoutedPath maps a waypoint to its edge and arc through it); <paramref name="own"/> gives
        /// a join's own-side offsets in and out (metres right of travel; 0 on a one-way). Cuts come back as index
        /// ranges of the new polyline with their radius.
        /// </summary>
        public static void Apply(CityMap map, List<Vector2> pts, List<float> ys, List<float> orig, List<Join> joins,
                                 List<Vector2> ownInOut, List<Cut> cuts)
        {
            cuts.Clear();
            int n = pts.Count;
            if (!On || map == null || n < 3 || joins.Count == 0) return;
            if (arcBuf.Length < n) arcBuf = new float[n + 256];
            var arc = arcBuf;
            arc[0] = 0f;
            for (int i = 1; i < n; i++) arc[i] = arc[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            float total = arc[n - 1];
            int seg = 0;
            Vector2 At(float a, out float y, out float o)
            {
                a = Mathf.Clamp(a, 0f, total);
                if (seg > n - 2 || arc[seg] > a) seg = 0;
                while (seg < n - 2 && arc[seg + 1] < a) seg++;
                float L = arc[seg + 1] - arc[seg], t = L > 1e-5f ? Mathf.Clamp01((a - arc[seg]) / L) : 0f;
                y = Mathf.Lerp(ys[seg], ys[seg + 1], t);
                o = Mathf.Lerp(orig[seg], orig[seg + 1], t);
                return Vector2.Lerp(pts[seg], pts[seg + 1], t);
            }
            CityMeshes.Trims tr = null;
            var found = new List<(float a0, float a1, Vector2 c, float r, float ang0, float sweep, float deg)>();
            float lastA1 = 0f;
            for (int k = 0; k < joins.Count; k++)
            {
                var J = joins[k];
                int j = J.idx, j1 = j + 1;
                if (j <= 0 || j1 >= n - 1 || J.turnDeg < MinDeg || J.turnDeg > MaxDeg) continue;
                Tried++;
                float aJ = arc[j], aO = arc[j1];
                float prev = lastA1;
                // the next sharp join bounds this one's exit line (its own arc comes after)
                float next = total;
                for (int k2 = k + 1; k2 < joins.Count; k2++)
                    if (joins[k2].turnDeg >= 10f && joins[k2].idx > j1) { next = arc[Mathf.Min(joins[k2].idx, n - 1)]; break; }
                float probe = Mathf.Min(4f, 0.45f * (aJ - prev), 0.45f * (next - aO));
                if (probe < 0.5f) { NoFit++; continue; }
                var tIn = pts[j] - At(aJ - probe, out _, out _);
                var tOut = At(aO + probe, out _, out _) - pts[j1];
                if (tIn.sqrMagnitude < 1e-6f || tOut.sqrMagnitude < 1e-6f) { NoFit++; continue; }
                tIn.Normalize(); tOut.Normalize();
                float defl = Vector2.Angle(tIn, tOut);
                float den = Cr(tIn, tOut);
                if (defl < MinDeg || defl > MaxDeg || Mathf.Abs(den) < 1e-3f) continue;
                bool left = den > 0f;
                // where the two lanes' lines meet
                float uP = Cr(pts[j1] - pts[j], tOut) / den;
                var P = pts[j] + tIn * uP;
                float vP = Vector2.Dot(P - pts[j1], tOut);
                if (Mathf.Abs(uP) > 20f || Mathf.Abs(vP) > 20f) { NoFit++; continue; }
                // (round 2) READ, DO NOT SEARCH: a turn laid before (at race load, or by any car's plan) is the same arc;
                // only the path's own ends can refuse it here (no pavement or kerb test: 2-20 ms a plan in the exe)
                long fkey = ((long)J.node * 1000003L + J.ein) * 1000003L + J.eout;
                if (WarmOn && kerbCacheMap == map && arcCache.TryGetValue(fkey, out var ac))
                {
                    if (ac.r <= 0f) { NoFit++; continue; }
                    float a0c = aJ + uP - ac.T, a1c = aO + vP + ac.T;
                    Vector2 oc = ownInOut != null && k < ownInOut.Count ? ownInOut[k] : Vector2.zero;
                    if (a0c <= aJ + 1e-3f && a1c >= aO - 1e-3f && a0c >= prev + 0.5f && a1c <= next - 0.5f && (left || ac.r - oc.x >= MinR))
                    {
                        found.Add((a0c, a1c, ac.c, ac.r, ac.ang0, ac.sweep, defl));
                        lastA1 = a1c;
                        Laid++; LaidR.Add(ac.r); MinLaidR = Mathf.Min(MinLaidR, ac.r);
                        ArcReads++;
                        continue;
                    }
                }
                if (tr == null) tr = CityWorld.TrimsFor(map);
                var ein = map.edges[J.ein]; var eout = map.edges[J.eout];
                kerbSegs.Clear();
                if (tr != null) NearKerbs(map, tr, J.node, kerbSegs);
                var ks = kerbSegs.Count >= 2 ? kerbSegs : null;
                // the corner's own clearance (an arc need not beat what the lanes themselves keep)
                // (once per turn per map: 100 kerb distances against every stretch two hops round was most of a plan's cost)
                long ckey = ((long)J.node * 1000003L + J.ein) * 1000003L + J.eout;
                if (!clrCache.TryGetValue(ckey, out float lineClr))
                {
                    lineClr = float.MaxValue;
                    if (ks != null)
                        for (float a = Mathf.Max(prev, aJ - 25f); a <= Mathf.Min(next, aO + 25f); a += 0.5f)
                            lineClr = Mathf.Min(lineClr, KerbDist(At(a, out _, out _), ks));
                    if (prev <= aJ - 25f && next >= aO + 25f) clrCache[ckey] = lineClr;
                }
                // never closer than a half-width and a margin: the corner's own line may run over an island's nose
                // (CltC7's link off W T Harris: the arc laid at the corner's 0.0 m clearance put cars on node 8006's
                // nose, and the autopilot stood on it 184 s)
                float clear = Mathf.Max(KerbMinM, Mathf.Min(KerbClearM, lineClr - 0.05f));
                float tanH = Mathf.Tan(0.5f * defl * Mathf.Deg2Rad);
                float trimIn = TrimAt(tr, ein, J.node), trimOut = TrimAt(tr, eout, J.node);
                float Tcap = TanMaxM;
                if (trimIn > 0f) Tcap = Mathf.Min(Tcap, trimIn + PastTrimM + Mathf.Max(0f, -uP));
                if (trimOut > 0f) Tcap = Mathf.Min(Tcap, trimOut + PastTrimM + Mathf.Max(0f, -vP));
                Vector2 oIn = ownInOut != null && k < ownInOut.Count ? ownInOut[k] : Vector2.zero;
                // the arc of tangent T: false when it leaves the pavement (or its line is a turn the car cannot make)
                bool Arc(float T, out float a0, out float a1, out Vector2 c, out float r, out float ang0, out float sweep)
                {
                    r = T / tanH; a0 = aJ + uP - T; a1 = aO + vP + T; c = Vector2.zero; ang0 = 0f;
                    sweep = (left ? 1f : -1f) * defl * Mathf.Deg2Rad;
                    if (r < MinR) return false;
                    if (a0 > aJ + 1e-3f || a1 < aO - 1e-3f) return false;
                    if (a0 < prev + 0.5f || a1 > next - 0.5f) return false;
                    // the car's own line must still be a turn it can make: a right turn's own side is inside the arc
                    if (!left && r - oIn.x < MinR) return false;
                    var p0 = P - tIn * T;
                    var nIn = left ? new Vector2(-tIn.y, tIn.x) : new Vector2(tIn.y, -tIn.x);
                    c = p0 + nIn * r;
                    ang0 = Mathf.Atan2(p0.y - c.y, p0.x - c.x);
                    // the arc and the car's line on it (own side, eased from the in-road's to the out-road's)
                    int m = Mathf.Max(6, Mathf.CeilToInt(r * Mathf.Abs(sweep)));
                    for (int q = 0; q <= m; q++)
                    {
                        float u = q / (float)m, ang = ang0 + sweep * u;
                        var pt = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                        var tan = left ? new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang)) : new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang));
                        var rightV = new Vector2(tan.y, -tan.x);
                        float own = Mathf.Lerp(oIn.x, oIn.y, u);
                        if (!OnPavement(map, tr, J.node, ein, eout, pt, clear, ks, out _)) return false;
                        if (Mathf.Abs(own) > 0.2f && !OnPavement(map, tr, J.node, ein, eout, pt + rightV * own, clear, ks, out _)) return false;
                    }
                    return true;
                }
                // the largest that fits: 2 m steps down, then 0.5 m steps up from the first fit (cost: the planner
                // builds a whole road per gate, and 60 full trials a join was 5-30 ms a plan in the editor)
                bool fit = false; float bestT = -1f;
                // the same turn (node, road in, road out) is the same arc for every car: the first plan searches it,
                // the rest check the answer once (8 cars re-plan at every gate)
                long key = ((long)J.node * 1000003L + J.ein) * 1000003L + J.eout;
                bool memo = tCache.TryGetValue(key, out float tm);
                if (memo && tm > 0f && tm <= Tcap + 1e-3f && Arc(tm, out _, out _, out _, out _, out _, out _)) bestT = tm;
                else if (memo && tm < 0f) bestT = -1f;
                else
                {
                    for (float T = Tcap; T >= 1f - 1e-3f; T -= CoarseStepM)
                    {
                        if (T / tanH < MinR) break;
                        if (Arc(T, out _, out _, out _, out _, out _, out _)) { bestT = T; break; }
                    }
                    if (bestT > 0f)
                        for (float T = Mathf.Min(Tcap, bestT + CoarseStepM - StepM); T > bestT + 1e-3f; T -= StepM)
                            if (Arc(T, out _, out _, out _, out _, out _, out _)) { bestT = T; break; }
                    // a no-fit is remembered only for a whole corner (not one cut short by the path's own ends)
                    if (bestT > 0f || (prev <= 0.01f && next >= total - 0.01f) || Tcap >= TanMaxM - 0.01f) tCache[key] = bestT;
                }
                if (bestT > 0f)
                {
                    Arc(bestT, out float fa0, out float fa1, out var fc, out float fr, out float fang0, out float fsweep);
                    found.Add((fa0, fa1, fc, fr, fang0, fsweep, defl));
                    // kept only when the path's own ends did not limit the search (a shorter arc forced by them is not the turn's)
                    // (round 3) kept whatever bounded it: an arc cut short by the path's ends is still on the pavement, and
                    // re-searched it cost every later plan 3-5 ms (X1); one found unbounded replaces a bounded one
                    bool bounded = !(prev <= aJ + uP - Tcap - 0.5f && next >= aO + vP + Tcap + 0.5f);
                    if (!bounded || !arcCache.TryGetValue(fkey, out var had) || had.r <= 0f || had.T < bestT) arcCache[fkey] = (bestT, fc, fr, fang0, fsweep);
                    ArcSearches++;
                    if (LogSearches && searchLogs++ < 30) Debug.Log($"[RacingTurns] searched node {J.node} in {J.ein} out {J.eout} at ({pts[j].x:0},{pts[j].y:0}): r {fr:0.0} m, T {bestT:0.0} of cap {Tcap:0.0}, prev {prev:0.0} a0 {fa0:0.0}, next {next - aO:0.0} past the out point; cached {(prev <= aJ + uP - Tcap - 0.5f && next >= aO + vP + Tcap + 0.5f)}");
                    lastA1 = fa1;
                    fit = true;
                    Laid++; LaidR.Add(fr); MinLaidR = Mathf.Min(MinLaidR, fr);
                }
                if (!fit)
                {
                    NoFit++;
                    // a no-fit is kept only for a whole corner (not one cut short by the path's own ends)
                    if (prev <= 0.01f && next >= total - 0.01f) arcCache[fkey] = (0f, Vector2.zero, 0f, 0f, 0f);
                }
            }
            if (found.Count == 0) return;
            // rebuild the polyline with the arcs laid in
            np.Clear(); ny.Clear(); no.Clear();
            int ci = 0;
            for (int i = 0; i < n; i++)
            {
                if (ci < found.Count && arc[i] >= found[ci].a0)
                {
                    var f = found[ci];
                    At(f.a0, out float y0, out float o0);
                    At(f.a1, out float y1, out float o1);
                    int m = Mathf.Max(6, Mathf.CeilToInt(f.r * Mathf.Abs(f.sweep) / 1.0f));
                    int i0 = np.Count;
                    for (int q = 0; q <= m; q++)
                    {
                        float u = q / (float)m, ang = f.ang0 + f.sweep * u;
                        np.Add(f.c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * f.r);
                        ny.Add(Mathf.Lerp(y0, y1, u));
                        no.Add(Mathf.Lerp(o0, o1, u));
                    }
                    cuts.Add(new Cut { i0 = i0, i1 = np.Count - 1, r = f.r, deg = f.deg });
                    while (i < n && arc[i] <= f.a1 + 1e-4f) i++;
                    ci++;
                    i--;
                    continue;
                }
                np.Add(pts[i]); ny.Add(ys[i]); no.Add(orig[i]);
            }
            pts.Clear(); pts.AddRange(np);
            ys.Clear(); ys.AddRange(ny);
            orig.Clear(); orig.AddRange(no);
        }
    }
}
