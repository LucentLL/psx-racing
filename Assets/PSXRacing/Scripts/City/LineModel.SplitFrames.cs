using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE SPLITS ROUND, PHASE B (J1, 2026-11-16): THE AXIS-FRAME PROFILE for
    /// long splits. The SPLT taper (LineModel.cs, THE MEDIAN TAPERS) only
    /// offset each carriageway's ribbon: it started where its lanes are in the
    /// undivided road and eased back onto OSM's line, which itself leaves the
    /// node at 20-40 degrees (the exporter's through-pair fillet makes a third
    /// of that) - so the shift ADDED to OSM's divergence: the lanes swung out
    /// 5-7 m past their own line and drifted back over 100-200 m (Beatties Ford
    /// n10035: 15.3 m drawn against OSM's 8.2 m at 20 m, a 10.4 m median
    /// against OSM's 3.0-3.4 m). A DOT design keeps the lanes straight and grows
    /// the island out of a narrow nose. Now, per in-scope split carriageway:
    ///   * THE AXIS: the undivided road's own direction into the node, a chord
    ///     of up to SplitChordM (30 m) behind it - not the 10 m ChordInto, so
    ///     the exporter's 10 degree bend over the road's last 6-7 m is not
    ///     followed; the undivided road's last metres are eased onto that
    ///     chord (a sampled shift, at most a few tenths of a metre).
    ///   * Each carriageway's lanes leave the node (a fan's edge at a junction
    ///     split) where they are in the undivided road (its outer edge flush
    ///     with the undivided road's), ALONG the axis, and join their own lane
    ///     line (OSM's line plus the joins' own eases) at u = L, tangent to it:
    ///     a cubic in the axis frame, L = max(30.5 m, r(S) d, sqrt(6 d Rmin(S)))
    ///     for the move d (MUTCD 3B.12 taper rate r(S); Green Book Rmin at -2 %),
    ///     S the undivided road's speed. Sampled as a shift ease along the
    ///     drawn path's own normal (the section frame, like a refuge island's),
    ///     so the lanes keep their width whatever OSM's line does underneath.
    ///   * THE NOSE (PSX_CITY_NOSE): where the two carriageways' lane bands are
    ///     2.4 m apart (a 1.2 m island and 0.6 m each side) the island starts
    ///     at a rounded tip and widens to the median; before it the inner
    ///     pavements meet flush (the undivided road's hatched centre lane runs
    ///     to the node). CurbMedian drops its 12 m set-back at these noses.
    /// Not drawn this way (listed in SplitSkipped, the old taper kept): a
    /// carriageway turning more than 35 degrees off the axis within its first
    /// 20 m, a separation over 15 m, a node whose mitre partner could not be
    /// drawn, a refuge island's carriageways (their own profile), an undivided
    /// road shorter than 8 m.
    /// </summary>
    public static partial class LineModel
    {
        public const float SplitChordM = 30f, SplitTurnMaxDeg = 35f, SplitSepMaxM = 15f, SplitFloorM = 30.5f, SplitIslandGapM = 2.4f;
        public const float SplitNoseH = 2.0f, SplitNoseLn = 8f, SplitNoseR = 0.45f;

        /// <summary>One carriageway end drawn by the axis-frame profile.</summary>
        public sealed class SplitFrame
        {
            public int node, u, edge, partner = -1; public int[] chain; public float chainLen;
            public Vector2 p0, tU, leftU;
            /// <summary>Lanes' centre at the node (axis frame, + leftU), where the
            /// drawn part starts (fan edge), the blend length, its end, the move,
            /// the own line's lateral and slope there, the taper rate, speed,
            /// Rmin, the room the chain gave; capped: L cut short by room.</summary>
            public float lambda0, uStart, L, uEnd, delta, qEnd, slopeEnd, rate, mph, rmin, room, lanesHalf, shIn;
            public bool fan, capped; public int innerSign; public string note = "";
            /// <summary>The drawn lanes' centre path (u from 0 at the node; past
            /// uEnd it is the own line), polyline with arc.</summary>
            public Vector2[] ax; public float[] axS;
            public int hintK; public long tag;
            /// <summary>The axis-frame lateral of the own line (the audit), by u.</summary>
            public float[] ownU, ownQ;
            public float TAt(float uu)
            {
                if (uu <= uStart) return lambda0;
                if (uu >= uEnd) return OwnQAt(uu);
                float t = (uu - uStart) / Mathf.Max(1e-3f, L), t2 = t * t, t3 = t2 * t;
                return lambda0 * (2f * t3 - 3f * t2 + 1f) + qEnd * (-2f * t3 + 3f * t2) + L * slopeEnd * (t3 - t2);
            }
            public float OwnQAt(float uu)
            {
                var U = ownU; var Q = ownQ;
                if (U == null || U.Length == 0) return lambda0;
                if (uu <= U[0]) return Q[0];
                int lo = 0, hi = U.Length - 1;
                if (uu >= U[hi]) return Q[hi];
                while (hi - lo > 1) { int m = (lo + hi) >> 1; if (U[m] <= uu) lo = m; else hi = m; }
                float f = (uu - U[lo]) / Mathf.Max(1e-4f, U[hi] - U[lo]);
                return Q[lo] + (Q[hi] - Q[lo]) * f;
            }
            /// <summary>The closest point of the path to p: its arc, the signed
            /// lateral (+ path-left) and the path's left normal there.</summary>
            public void Foot(Vector2 p, ref int hint, out float uu, out float lat, out Vector2 left)
            {
                int n = ax.Length - 1, lo = Mathf.Max(1, hint - 4), hi = Mathf.Min(n, hint + 4);
                int k = Closest(p, lo, hi, out uu, out var at, out left);
                if (hint <= 0 || (k == lo && lo > 1) || (k == hi && hi < n)) k = Closest(p, 1, n, out uu, out at, out left);
                hint = k;
                lat = Vector2.Dot(p - at, left);
            }
            int Closest(Vector2 p, int lo, int hi, out float uu, out Vector2 at, out Vector2 left)
            {
                float best = float.MaxValue, bt = 0f; int bk = -1;
                for (int k = lo; k <= hi; k++)
                {
                    Vector2 a = ax[k - 1], ab = ax[k] - a;
                    float l2 = ab.sqrMagnitude;
                    if (l2 < 1e-8f) continue;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                    float d = (p - (a + ab * t)).sqrMagnitude;
                    if (d < best) { best = d; bk = k; bt = t; }
                }
                if (bk < 0) { uu = 0f; at = ax[0]; left = leftU; return lo; }
                Vector2 a0 = ax[bk - 1], ab0 = ax[bk] - a0;
                at = a0 + ab0 * bt; uu = axS[bk - 1] + bt * (axS[bk] - axS[bk - 1]);
                var tn = ab0.normalized; left = new Vector2(-tn.y, tn.x);
                return bk;
            }
            /// <summary>The path's arc at the profile's end (where it joins the own line).</summary>
            public float sEnd;
        }

        static readonly Dictionary<int, SplitFrame> splitFrameEdge = new Dictionary<int, SplitFrame>();
        /// <summary>A mitred split node's one section normal (the axis): kept apart from the refuge
        /// islands' (IsRefugeNode must stay refuge-only: NoseMarks, the medians gate, the kerbs read it).</summary>
        static readonly Dictionary<int, Vector2> splitNodeDir = new Dictionary<int, Vector2>();
        public static readonly List<SplitFrame> SplitFrames = new List<SplitFrame>();
        /// <summary>In-scope split carriageway ends left on the old taper, each with why.</summary>
        public static readonly List<string> SplitSkipped = new List<string>();
        /// <summary>Nodes whose nose the splits round shapes and marks (both carriageways framed).</summary>
        static readonly HashSet<int> splitNoseNodes = new HashSet<int>();
        /// <summary>The trims of the solve being built (CityMeshes sets it before BuildEases): a fan's edge.</summary>
        public static System.Func<CityMap.Edge, int, float> SplitTrimHook;
        public static int SplitProfiled, SplitUEases;

        static void ClearSplitFrames()
        {
            splitFrameEdge.Clear(); SplitFrames.Clear(); SplitSkipped.Clear(); splitNoseNodes.Clear(); splitNodeDir.Clear();
            SplitProfiled = 0; SplitUEases = 0;
        }
        public static SplitFrame SplitFrameOf(int edge) => splitFrameEdge.Count > 0 && splitFrameEdge.TryGetValue(edge, out var f) ? f : null;
        public static bool SplitNoseMarked(CityMap map, int node) => NoseRuleOn && splitNoseNodes.Contains(node);
        public static bool SplitNoseShaped(int node) => NoseRuleOn && splitNoseNodes.Count > 0 && splitNoseNodes.Contains(node);
        public static IEnumerable<int> SplitNoseNodes => splitNoseNodes;

        /// <summary>The section normal on a framed carriageway (inside its
        /// profile): the drawn path's own, on e's left. False: past the
        /// profile's end, or not framed (the caller's own rule).</summary>
        static bool SplitFrameNormal(CityMap.Edge e, float s, out Vector2 left)
        {
            left = default;
            if (splitFrameEdge.Count == 0 || !splitFrameEdge.TryGetValue(e.index, out var f)) return false;
            int h = f.hintK;
            f.Foot(e.PointAt(s), ref h, out float uu, out _, out var lf);
            f.hintK = h;
            if (uu > f.sEnd + 1e-3f) return false;
            var t = e.TangentAt(s);
            left = Vector2.Dot(lf, new Vector2(-t.y, t.x)) >= 0f ? lf : -lf;
            return true;
        }

        /// <summary>Rmin (m) at e = -2 % (Green Book 2018 Table 3-7 via Iowa DM 2B), by mph.</summary>
        public static float RminAt(float mph)
        {
            float[] v = { 25f, 30f, 35f, 40f, 45f, 50f, 55f }, r = { 60.5f, 101.6f, 155.6f, 232.2f, 316.5f, 423.3f, 558.8f };
            if (mph <= v[0]) return r[0];
            for (int i = 1; i < v.Length; i++) if (mph <= v[i]) return Mathf.Lerp(r[i - 1], r[i], (mph - v[i - 1]) / (v[i] - v[i - 1]));
            return r[r.Length - 1];
        }
        /// <summary>MUTCD's taper rate L/W: S^2/60 to 40 mph, S from 45.</summary>
        public static float TaperRateAt(float mph) => mph <= 40f ? mph * mph / 60f : mph;
        public static float SplitSpeedMph(CityMap.Edge e) => SpeedMph(e);

        /// <summary>Walk back from a node along the undivided road (and its
        /// mitred continuations) up to want metres: the chord direction INTO
        /// the node, how far it reached, and the pieces walked.</summary>
        static Vector2 ChordBack(CityMap map, System.Func<CityMap.Edge, int, int> partner, CityMap.Edge U, int node, float want, out float got, List<(int e, int at)> run)
        {
            got = 0f; run?.Clear();
            var cur = U; int at = node; float rest = want; Vector2 pb = map.nodes[node];
            for (int g = 0; g < 8 && rest > 1e-3f; g++)
            {
                run?.Add((cur.index, at));
                bool towardAt = cur.b == at;
                if (cur.length >= rest) { pb = cur.PointAt(towardAt ? cur.length - rest : rest); got += rest; rest = 0f; break; }
                got += cur.length; rest -= cur.length;
                int far = cur.a == at ? cur.b : cur.a;
                pb = map.nodes[far];
                int pi = partner(cur, far);
                if (pi < 0) break;
                var p = map.edges[pi];
                if (p == cur || p.a == p.b || p.oneway != U.oneway) break;
                cur = p; at = far;
            }
            var v = map.nodes[node] - pb;
            if (v.sqrMagnitude > 1e-4f) return v.normalized;
            return U.a == node ? -U.TangentAt(0f) : U.TangentAt(U.length);
        }

        /// <summary>A carriageway's chain from its split node: same name,
        /// one-way the same way (a side street may join), to the next split
        /// node, a junction where it does not go on as one, or maxLen.</summary>
        static void CarriagewayChain(CityMap map, HashSet<int> splitNodes, int ei, int node, float maxLen, List<int> chain, out int far, out float len)
        {
            chain.Clear(); far = -1; len = 0f;
            var e = map.edges[ei]; int from = node;
            for (int g = 0; g < 40; g++)
            {
                if (e.a == e.b) return;
                chain.Add(e.index); len += e.length;
                far = e.a == from ? e.b : e.a;
                if (splitNodes.Contains(far) || len >= maxLen) return;
                bool arrives = e.b == far;
                CityMap.Edge next = null; int n = 0;
                foreach (int i in map.nodeEdges[far])
                {
                    if (i == e.index) continue;
                    var o = map.edges[i];
                    if (o.name != e.name || !o.oneway || o.link || o.a == o.b || (o.a == far) != arrives) continue;
                    next = o; n++;
                }
                if (n != 1) return;
                e = next; from = far;
            }
        }

        /// <summary>PSX_CITY_SPLITMEET=0: no profile at same-name fan meetings.</summary>
        public static bool SplitMeetOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SPLITMEET") != "0";
        public static int SplitMeetings;

        /// <summary>In-scope same-name meetings that have no SPLT record: a node (not a mitred
        /// continuation) where exactly one two-way edge of a name meets one-way edges of that name,
        /// one leaving and one arriving: synthesized splits (offsets from the geometry, the rate from
        /// the undivided road's speed).</summary>
        static List<Split> SameNameMeetings(CityMap map, Dictionary<int, Split> splitAt)
        {
            var res = new List<Split>(); SplitMeetings = 0;
            IEnumerable<int> nodes = SplitAxis == "all" ? null : BfrSplitNodes;
            var all = nodes ?? System.Linq.Enumerable.Range(0, map.nodes.Length);
            foreach (int n in all)
            {
                if (n < 0 || n >= map.nodes.Length || splitAt.ContainsKey(n) || !InSplitScope(map, n)) continue;
                var arms = map.nodeEdges[n];
                if (arms == null || arms.Count < 3) continue;
                // the one-way carriageways' name; exactly one two-way arm of it (a side street of another name may meet)
                string nm = null;
                foreach (int ei in arms) { var e = map.edges[ei]; if (e.oneway && !e.link && !string.IsNullOrEmpty(e.name)) { nm = e.name; break; } }
                if (nm == null) continue;
                int u = -1, a = -1, b = -1, nU = 0;
                foreach (int ei in arms) { var e = map.edges[ei]; if (!e.oneway && !e.link && e.a != e.b && e.name == nm) { u = ei; nU++; } }
                if (nU != 1) continue;
                var U = map.edges[u];
                foreach (int ei in arms)
                {
                    var e = map.edges[ei];
                    if (!e.oneway || e.link || e.name != U.name || e.a == e.b) continue;
                    if (e.a == n && a < 0) a = ei; else if (e.b == n && b < 0) b = ei;
                }
                if (a < 0 || b < 0) continue;
                // the undivided road's way into the node, and each carriageway's side 20 m out
                var tin = U.a == n ? -U.TangentAt(0f) : U.TangentAt(U.length);
                var left = new Vector2(-tin.y, tin.x);
                float Side(int ci) { var c = map.edges[ci]; var p = c.PointAt(c.a == n ? Mathf.Min(20f, c.length) : Mathf.Max(0f, c.length - 20f)); return Vector2.Dot(p - map.nodes[n], left); }
                float oa = Side(a), ob = Side(b);
                if (Mathf.Sign(oa) == Mathf.Sign(ob)) continue;
                res.Add(new Split { node = n, u = u, a = a, b = b, offA = oa, offB = ob, rate = TaperRateAt(SpeedMph(U)) });
                SplitMeetings++;
            }
            return res;
        }

        /// <summary>The audits: a carriageway's chain from its split node (see CarriagewayChain).</summary>
        public static void SplitChain(CityMap map, HashSet<int> splitNodes, int ei, int node, float maxLen, List<int> chain, out int far, out float len) =>
            CarriagewayChain(map, splitNodes, ei, node, maxLen, chain, out far, out len);
        /// <summary>The audits: the undivided road's chord INTO a node (see ChordBack).</summary>
        public static Vector2 SplitAxisChord(CityMap map, System.Func<CityMap.Edge, int, int> partner, CityMap.Edge U, int node, float want, out float got) =>
            ChordBack(map, partner, U, node, want, out got, null);

        static float LanesCentrePre(CityMap.Edge e, float s)
        {
            Ext(e, s, false, out float eM, out float eP);
            return 0.5f * ((eP - e.shl) - (eM - e.shr));
        }

        /// <summary>
        /// Build the axis-frame profile at one split node (both carriageways
        /// where they qualify). Returns the carriageway edges drawn by it (the
        /// SPLT block skips their old taper).
        /// </summary>
        static HashSet<int> BuildSplitNode(CityMap map, List<Ease>[] add, System.Func<CityMap.Edge, int, int> partner, Split sp, HashSet<int> splitNodes, HashSet<long> refugeEnds)
        {
            var done = new HashSet<int>();
            if (sp.u < 0 || sp.u >= map.edges.Length) return done;
            var U = map.edges[sp.u];
            void Skip(int ci, string why) { if (SplitSkipped.Count < 2000) SplitSkipped.Add($"n{sp.node} e{ci} '{U.name}': {why}"); }
            if (U.oneway || U.a == U.b || (U.lmPlus == 0f && U.lmMinus == 0f)) { Skip(-1, "undivided road not two-way / no line model"); return done; }
            var runU = new List<(int e, int at)>(4);
            Vector2 tU = ChordBack(map, partner, U, sp.node, SplitChordM, out float chordGot, runU);
            if (chordGot < 8f) { Skip(-1, $"undivided road only {chordGot:0.0} m before the node (no axis)"); return done; }
            var leftU = new Vector2(-tU.y, tU.x);
            Vector2 P0 = map.nodes[sp.node];
            float mph = SpeedMph(U), rate = TaperRateAt(mph), rmin = RminAt(mph);
            int pUC = partner(U, sp.node);
            var frames = new List<SplitFrame>(2);
            var chain = new List<int>(16);
            for (int c = 0; c < 2; c++)
            {
                int ci = c == 0 ? sp.a : sp.b;
                float offC = c == 0 ? sp.offA : sp.offB;
                if (ci < 0 || ci >= map.edges.Length) continue;
                if (refugeEnds.Contains(RefugeKey(sp.node, ci))) continue;   // a refuge island's: its own profile
                var C = map.edges[ci];
                if (C.a != sp.node && C.b != sp.node) continue;
                if (C.lmPlus == 0f && C.lmMinus == 0f) { Skip(ci, "no line model"); continue; }
                CarriagewayChain(map, splitNodes, ci, sp.node, 420f, chain, out int far, out float clen);
                if (chain.Count == 0 || clen < 20f) { Skip(ci, $"carriageway chain only {clen:0.0} m"); continue; }
                // a short island (both ends split nodes within RefugeMaxM) takes the refuge path: a pair
                // the medians gate did not accept draws as live, never a one-ended profile
                if (splitNodes.Contains(far) && clen <= RefugeMaxM) { Skip(ci, $"short island to n{far} ({clen:0.0} m): the refuge path"); continue; }
                // the carriageway's frame against the axis (as the SPLT block): its outer side
                var tc = C.a == sp.node ? C.TangentAt(0f) : C.TangentAt(C.length);
                float sign = Vector2.Dot(leftU, new Vector2(-tc.y, tc.x)) >= 0f ? 1f : -1f;
                float out1 = Mathf.Sign(offC * sign);
                if (out1 == 0f) { Skip(ci, "no side"); continue; }
                bool outIsLeftU = out1 * sign > 0f;
                float uOut = DrawnToward(add, U, sp.node, outIsLeftU ? leftU : -leftU);
                float cOutRel = out1 > 0f ? C.lmPlus - C.lmOff : C.lmMinus + C.lmOff;
                float dirU = outIsLeftU ? 1f : -1f;                   // the outer side, axis frame
                var f = new SplitFrame { node = sp.node, u = sp.u, edge = ci, chain = chain.ToArray(), chainLen = clen, p0 = P0, tU = tU, leftU = leftU,
                                         lambda0 = dirU * (uOut - cOutRel), rate = rate, mph = mph, rmin = rmin,
                                         lanesHalf = 0.5f * (C.width - C.shl - C.shr), innerSign = -(int)dirU };
                // the inner shoulder (the side away from the outer edge), C's own profile
                f.shIn = out1 > 0f ? C.shr : C.shl;
                // the fan's edge (a junction split): the drawn part starts there
                float trim = SplitTrimHook != null ? SplitTrimHook(C, sp.node) : 0f;
                f.fan = trim > 0.05f;
                f.uStart = f.fan ? Mathf.Max(0f, Vector2.Dot(C.PointAt(C.a == sp.node ? Mathf.Min(trim, C.length) : Mathf.Max(0f, C.length - trim)) - P0, tU)) : 0f;
                // the own lanes' centre along the chain, in the axis frame
                var us = new List<float>(256); var qs = new List<float>(256);
                {
                    int from = sp.node; float d0 = 0f;
                    foreach (int ei in f.chain)
                    {
                        var e = map.edges[ei];
                        bool fwd = e.a == from;
                        var keep = e.lmEase; e.lmEase = PreEases(add, e);
                        int n = Mathf.Max(2, Mathf.CeilToInt(e.length / 0.5f) + 1);
                        float ds = e.length / (n - 1);
                        for (int k = 0; k < n; k++)
                        {
                            float s = fwd ? k * ds : e.length - k * ds;
                            var t = e.TangentAt(s); var nl = new Vector2(-t.y, t.x);
                            var o = e.PointAt(s) + nl * LanesCentrePre(e, s);
                            float uu = Vector2.Dot(o - P0, tU), qq = Vector2.Dot(o - P0, leftU);
                            if (us.Count > 0 && uu <= us[us.Count - 1] + 1e-3f) continue;
                            us.Add(uu); qs.Add(qq);
                        }
                        e.lmEase = keep;
                        d0 += e.length; from = fwd ? e.b : e.a;
                    }
                }
                if (us.Count < 8) { Skip(ci, "own line too short"); continue; }
                f.ownU = us.ToArray(); f.ownQ = qs.ToArray();
                float uMax = us[us.Count - 1];
                // a turning roadway: the own line's heading 60 m on (the census's netTurn) over 35 degrees
                {
                    float uT = Mathf.Min(uMax - 2f, f.uStart + 60f);
                    float slope60 = (f.OwnQAt(uT + 2f) - f.OwnQAt(uT - 2f)) / 4f;
                    float turn = Mathf.Atan(Mathf.Abs(slope60)) * Mathf.Rad2Deg;
                    if (turn > SplitTurnMaxDeg) { Skip(ci, $"turning roadway: its own line {turn:0.0} deg off the axis {uT:0} m on"); continue; }
                    // a separate road: its lanes 15 m or more further out 60 m on than where they leave
                    if (Mathf.Abs(f.OwnQAt(uT)) - Mathf.Abs(f.lambda0) > SplitSepMaxM) { Skip(ci, $"separation over {SplitSepMaxM:0} m 60 m on (a separate road)"); continue; }
                }
                // the room: to the chain's end less a run-out (half way to the next split)
                f.room = Mathf.Max(0f, (splitNodes.Contains(far) ? 0.45f * uMax : uMax - 12f) - f.uStart);
                // L: MUTCD's length for the move, the move read at most 60 m on (a straight frame
                // reads a curving road's drift as a move: never past 60 m), the rate term with a 20 %
                // margin (a smoothstep's 5-95 % takes 0.87 L: the census measure)
                float L = SplitFloorM;
                for (int it = 0; it < 6; it++)
                {
                    float dq = Mathf.Abs(f.OwnQAt(f.uStart + Mathf.Min(L, 60f)) - f.lambda0);
                    float Ln = Mathf.Min(200f, Mathf.Max(SplitFloorM, Mathf.Max(1.2f * rate * dq, Mathf.Sqrt(6f * dq * rmin))));
                    if (Mathf.Abs(Ln - L) < 0.05f) { L = Ln; break; }
                    L = Ln;
                }
                if (L > f.room) { f.capped = true; f.note = $"room {f.room:0.0} m for L {L:0.0} m"; L = Mathf.Max(10f, f.room); }
                f.L = L; f.uEnd = f.uStart + L;
                f.qEnd = f.OwnQAt(f.uEnd);
                f.slopeEnd = Mathf.Clamp((f.OwnQAt(f.uEnd + 2f) - f.OwnQAt(f.uEnd - 2f)) / 4f, -0.15f, 0.15f);
                f.delta = f.qEnd - f.lambda0;
                // the path: the profile to uEnd, then the own line for a run-out
                var pts = new List<Vector2>(512);
                float uRun = Mathf.Min(uMax, f.uEnd + 8f);
                for (float uu = 0f; uu <= uRun + 1e-3f; uu += 0.5f) pts.Add(P0 + tU * uu + leftU * f.TAt(uu));
                f.ax = pts.ToArray(); f.axS = new float[f.ax.Length];
                for (int i = 1; i < f.ax.Length; i++) f.axS[i] = f.axS[i - 1] + Vector2.Distance(f.ax[i - 1], f.ax[i]);
                // the path's arc at uEnd (the profile's end: past it the own normal)
                f.sEnd = f.axS[Mathf.Clamp(Mathf.RoundToInt(f.uEnd / 0.5f), 0, f.axS.Length - 1)];
                f.tag = -(1000000 + sp.node * 2 + c);
                frames.Add(f);
            }
            if (frames.Count == 0) return done;
            // a mitred node: U's mitre partner must be drawn this way too (else its cut and U's would wedge)
            bool mitred = pUC >= 0;
            if (mitred)
            {
                bool partnerOk = false;
                foreach (var f in frames) if (f.edge == pUC) partnerOk = true;
                if (!partnerOk && (pUC == sp.a || pUC == sp.b) && !refugeEnds.Contains(RefugeKey(sp.node, pUC)))
                {
                    foreach (var f in frames) Skip(f.edge, $"its node's mitre partner e{pUC} is not drawn by the profile");
                    return done;
                }
            }
            // ---- THE NOSE: the island's tip where the lane bands are 2.4 m apart ----
            float uTip = float.NaN; SplitFrame fa = null, fb = null;
            if (frames.Count == 2) { fa = frames[0]; fb = frames[1]; }
            float GapLanes(float uu) => fa == null ? float.NaN : Mathf.Abs(fa.TAt(uu) - fb.TAt(uu)) - fa.lanesHalf - fb.lanesHalf;
            float noseEnd = 0f, noseH = SplitNoseH, noseLn = SplitNoseLn;
            if (fa != null && NoseRuleOn && fa.innerSign != fb.innerSign)
            {
                float u0 = Mathf.Max(fa.uStart, fb.uStart), u1 = Mathf.Max(fa.uEnd, fb.uEnd) + 10f;
                for (float uu = u0; uu <= u1; uu += 0.25f) if (GapLanes(uu) >= SplitIslandGapM) { uTip = uu + RefugeTipM; break; }
                if (!float.IsNaN(uTip))
                {
                    // the nose's full half width: the widest pavement gap its flank must reach (so the
                    // widening ends at 0 where the flank meets the pavement), the flank no steeper than 1:4
                    float gMax = 0f;
                    for (float uu = uTip; uu <= uTip + 40f; uu += 0.5f) gMax = Mathf.Max(gMax, GapLanes(uu) - fa.shIn - fb.shIn);
                    noseH = Mathf.Max(0.6f, 0.5f * gMax + 0.05f); noseLn = Mathf.Max(SplitNoseLn, 4f * noseH);
                    noseEnd = uTip + noseLn + 2f; splitNoseNodes.Add(sp.node);
                }
            }
            // ---- THE EASES ----
            foreach (var f in frames)
            {
                int from = sp.node; float d0 = 0f; int hint = 0;
                float uLast = Mathf.Max(f.uEnd, float.IsNaN(uTip) ? 0f : noseEnd) + 1f;
                foreach (int ei in f.chain)
                {
                    var e = map.edges[ei];
                    bool fwd = e.a == from;
                    var keep = e.lmEase; e.lmEase = PreEases(add, e);
                    int n = Mathf.Max(2, Mathf.CeilToInt(e.length / RefugeDs) + 1);
                    float ds = e.length / (n - 1);
                    var sh = new float[n]; var wd = new float[n]; bool anyIn = false, anyW = false; int wSide = 0;
                    for (int k = 0; k < n; k++)
                    {
                        float x = k * ds, s = fwd ? x : e.length - x;
                        var p = e.PointAt(s);
                        f.Foot(p, ref hint, out float uu, out float latF, out var lf);
                        var t = e.TangentAt(s); var el = new Vector2(-t.y, t.x);
                        var nL = Vector2.Dot(lf, el) >= 0f ? lf : -lf;
                        float uAx = Vector2.Dot(p - P0, tU);
                        if (uu <= f.sEnd + 1e-3f)
                        {
                            // the foot on the path: the lanes' centre goes there
                            float qL = Vector2.Dot(lf, nL) >= 0f ? latF : -latF;
                            sh[k] = -qL - LanesCentrePre(e, s);
                            anyIn = true;
                        }
                        // the nose: inner pavement widened to the island (flush before its tip)
                        if (!float.IsNaN(uTip) && uAx <= noseEnd)
                        {
                            var o = fa == f ? fb : fa;
                            float gapPave = GapLanes(uAx) - f.shIn - o.shIn;
                            float ih = uAx < uTip ? 0f : Mathf.Min(0.5f * Mathf.Max(0f, gapPave), NoseHalf(uAx - uTip, noseH, noseLn, SplitNoseR));
                            float w = Mathf.Max(0f, 0.5f * gapPave - ih);
                            wd[k] = w; if (w > 0.005f) anyW = true;
                            // the inner side in e's frame
                            int innerE = Vector2.Dot(leftU * f.innerSign, el) >= 0f ? 1 : -1;
                            wSide = innerE;
                        }
                    }
                    e.lmEase = keep;
                    if (anyIn)
                    {
                        // the profile's last samples settle to the own drawing (0)
                        var list = add[e.index] ??= new List<Ease>(3);
                        list.Add(new Ease { shift = true, fromA = fwd, dw = 1f, len = d0 + e.length + 1f, d0 = d0, prof = sh, profD0 = d0, profDs = ds, tag = f.tag });
                        if (!splitFrameEdge.ContainsKey(ei)) splitFrameEdge[ei] = f;
                    }
                    if (anyW && wSide != 0)
                    {
                        var list = add[e.index] ??= new List<Ease>(3);
                        list.Add(new Ease { widen = true, side = (sbyte)wSide, fromA = fwd, dw = 1f, len = d0 + e.length + 1f, d0 = d0, prof = wd, profD0 = d0, profDs = ds, tag = f.tag });
                    }
                    d0 += e.length; from = fwd ? e.b : e.a;
                    if (d0 > 0f && Vector2.Dot(map.nodes[from] - P0, tU) > uLast + 2f) break;
                }
                SplitFrames.Add(f);
                SplitProfiled++;
                done.Add(f.edge);
            }
            // ---- the undivided road's last metres onto the axis chord ----
            UChordEase(map, add, runU, sp.node, P0, tU, chordGot);
            // one section normal at a mitred node: U's end and the drawn carriageways' starts
            if (mitred)
            {
                splitNodeDir[sp.node] = tU;
                refugeNodeEdge.Add(RefugeKey(sp.node, sp.u));
                foreach (var f in frames) refugeNodeEdge.Add(RefugeKey(sp.node, f.edge));
            }
            return done;
        }

        /// <summary>The undivided road's last up-to-30 m eased onto the axis
        /// chord: the exporter's fillet bends its last 6-7 m by ~10 degrees;
        /// the drawn lanes run on the chord into the node (a sampled shift,
        /// faded out by the chord's far end).</summary>
        static void UChordEase(CityMap map, List<Ease>[] add, List<(int e, int at)> run, int node, Vector2 P0, Vector2 tU, float chord)
        {
            if (chord < 8f || run.Count == 0) return;
            var leftU = new Vector2(-tU.y, tU.x);
            // the deviation is from the chord LINE through the node along tU
            float dist = 0f; float maxDev = 0f;
            var pieces = new List<(CityMap.Edge e, bool towardNode, float d0, float[] sh, float ds)>();
            foreach (var (ei, at) in run)
            {
                var e = map.edges[ei];
                bool toward = e.b == at;      // a->b runs toward the node
                int n = Mathf.Max(2, Mathf.CeilToInt(e.length / RefugeDs) + 1);
                float ds = e.length / (n - 1);
                var sh = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float s = k * ds;
                    float dn = dist + (toward ? e.length - s : s);   // distance back from the node
                    if (dn > chord) { sh[k] = 0f; continue; }
                    var p = e.PointAt(s);
                    float dev = Vector2.Dot(p - P0, leftU);
                    float w = 1f - Smooth(dn / chord);
                    // U's plus side is leftU where it runs toward the node
                    float v = -dev * w * (toward ? 1f : -1f);
                    sh[k] = v; maxDev = Mathf.Max(maxDev, Mathf.Abs(dev * w));
                }
                pieces.Add((e, toward, dist, sh, ds));
                dist += e.length;
                if (dist >= chord) break;
            }
            if (maxDev < 0.02f) return;
            foreach (var pc in pieces)
            {
                // the ease's chain distance runs from the edge's a end (fromA) with d0 = 0: sampled by s
                var list = add[pc.e.index] ??= new List<Ease>(3);
                list.Add(new Ease { shift = true, fromA = true, dw = 1f, len = pc.e.length + 1f, d0 = 0f, prof = pc.sh, profD0 = 0f, profDs = pc.ds, tag = -(2000000 + node) });
            }
            SplitUEases++;
        }
    }
}
