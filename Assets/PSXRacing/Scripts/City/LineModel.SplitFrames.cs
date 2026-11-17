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
        /// <summary>THE NOSE (the fix round): the island's tip stands where the two lane bands are
        /// SplitIslandGapM apart, a SplitNoseR round tip, its sides SplitNoseOffM off each lane edge
        /// (Iowa DM 6A-11, MUTCD 3B.10: 1-2 ft), easing to the carriageway's own shoulder over
        /// noseLn (10:1, at least SplitNoseFlankMinM).</summary>
        public const float SplitNoseR = 0.6f, SplitNoseOffM = 0.6f, SplitNoseFlankMinM = 3f, SplitNoseLateM = 20f, SplitNoseFlare = 0.25f;

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
            /// <summary>The axis-frame lateral of the own line (the audit), by u, and the own lane
            /// band's half width there (a lane added or dropped on the carriageway changes it).</summary>
            public float[] ownU, ownQ, ownH;
            /// <summary>The node's nose (both frames carry it): the tip's u (NaN: none) and the
            /// flank's length; the audit's design table.</summary>
            public float uTip = float.NaN, noseLn;
            /// <summary>Phase C: the drawn lanes' furthest distance off their own line over the blend.</summary>
            public float devMax;
            public float HAt(float uu)
            {
                var U = ownU; var H = ownH;
                if (U == null || H == null || U.Length == 0) return lanesHalf;
                if (uu <= U[0]) return H[0];
                int lo = 0, hi = U.Length - 1;
                if (uu >= U[hi]) return H[hi];
                while (hi - lo > 1) { int m = (lo + hi) >> 1; if (U[m] <= uu) lo = m; else hi = m; }
                float f = (uu - U[lo]) / Mathf.Max(1e-4f, U[hi] - U[lo]);
                return H[lo] + (H[hi] - H[lo]) * f;
            }
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
            splitFrameEdge.Clear(); splitFrameEdge2.Clear(); SplitFrames.Clear(); SplitSkipped.Clear(); splitNoseNodes.Clear(); splitNodeDir.Clear(); SplitNoseTable.Clear();
            tightTwinEdges.Clear(); tightTwinNodes.Clear(); TightTwinLog.Clear(); TightTwinPairs = 0; TightTwinEdgeCount = 0; TightTwinM = 0f;
            SplitProfiled = 0; SplitUEases = 0; PhaseCLonger = 0; PhaseCCurving = 0; PhaseCJunctionCap = 0; PhaseCDevCap = 0; PhaseCSteepEnd = 0; PhaseCEndShortened = 0;
        }
        public static SplitFrame SplitFrameOf(int edge) => splitFrameEdge.Count > 0 && splitFrameEdge.TryGetValue(edge, out var f) ? f : null;
        /// <summary>Phase C (NoseMarks): the undivided edge of a framed node (a same-name meeting has no SPLT record).</summary>
        public static bool SplitMeetingU(int node, out int u)
        {
            foreach (var f in SplitFrames) if (f.node == node) { u = f.u; return true; }
            u = -1; return false;
        }
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
            if (FrameNormalOf(f, e, s, out left)) return true;
            // Phase C: a short island's carriageway carries a profile from each of its ends (the second one's
            // half of the island takes its own path's normal)
            return CityPhaseC && splitFrameEdge2.Count > 0 && splitFrameEdge2.TryGetValue(e.index, out var f2) && FrameNormalOf(f2, e, s, out left);
        }
        static bool FrameNormalOf(SplitFrame f, CityMap.Edge e, float s, out Vector2 left)
        {
            left = default;
            int h = f.hintK;
            f.Foot(e.PointAt(s), ref h, out float uu, out _, out var lf);
            f.hintK = h;
            if (uu > f.sEnd + 1e-3f) return false;
            var t = e.TangentAt(s);
            left = Vector2.Dot(lf, new Vector2(-t.y, t.x)) >= 0f ? lf : -lf;
            return true;
        }
        /// <summary>Phase C: the second frame on an edge (a short island profiled from both its ends).</summary>
        static readonly Dictionary<int, SplitFrame> splitFrameEdge2 = new Dictionary<int, SplitFrame>();

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
            IEnumerable<int> nodes = SplitAxis == "all" || SplitAxis == "city" ? null : BfrSplitNodes;
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

        /// <summary>The audits (Phase C): every same-name meeting city-wide, whatever the scope - a node with no
        /// SPLT record where exactly one two-way edge of a name meets one-way edges of that name, one leaving and
        /// one arriving, on opposite sides (SameNameMeetings' pattern without its scope test): (node, u, a, b).</summary>
        public static List<(int node, int u, int a, int b)> MeetingNodesAll(CityMap map)
        {
            var res = new List<(int, int, int, int)>();
            var spl = new HashSet<int>();
            if (map.splt != null) foreach (var sp in map.splt) spl.Add(sp.node);
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (spl.Contains(n)) continue;
                var arms = map.nodeEdges[n];
                if (arms == null || arms.Count < 3) continue;
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
                var tin = U.a == n ? -U.TangentAt(0f) : U.TangentAt(U.length);
                var left = new Vector2(-tin.y, tin.x);
                float Side(int ci) { var c = map.edges[ci]; var p = c.PointAt(c.a == n ? Mathf.Min(20f, c.length) : Mathf.Max(0f, c.length - 20f)); return Vector2.Dot(p - map.nodes[n], left); }
                if (Mathf.Sign(Side(a)) == Mathf.Sign(Side(b))) continue;
                res.Add((n, u, a, b));
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
        /// <summary>The lanes' centre and the lane band's half width at s (the eases already made).</summary>
        static float LanesPre(CityMap.Edge e, float s, out float half)
        {
            Ext(e, s, false, out float eM, out float eP);
            half = 0.5f * ((eP - e.shl) + (eM - e.shr));
            return 0.5f * ((eP - e.shl) - (eM - e.shr));
        }

        /// <summary>The audits: one framed node's design (u, the lane gap, each side's inner
        /// pavement edge off its lane edge), as BuildSplitNode laid the nose.</summary>
        public static readonly Dictionary<int, List<(float u, float gap, float offA, float offB)>> SplitNoseTable = new Dictionary<int, List<(float, float, float, float)>>();

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
                // Phase C (scope city, PSX_CITY_SHORTPROFILE=0 off): a short island the medians gate did not
                // draw as a refuge pair (PC45's taper: each end shifted out and RoomAlong letting both run to the
                // far node - a constant 5-7 m lens, 482 ends city-wide) takes the profile from each of its ends,
                // each blending onto its own line within 45 % of the island: OSM's island at its middle
                bool shortIsland = splitNodes.Contains(far) && clen <= RefugeMaxM;
                if (shortIsland && !(CityPhaseC && PhaseCShortOn)) { Skip(ci, $"short island to n{far} ({clen:0.0} m): the refuge path"); continue; }
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
                var us = new List<float>(256); var qs = new List<float>(256); var hs = new List<float>(256);
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
                            var o = e.PointAt(s) + nl * LanesPre(e, s, out float hh);
                            float uu = Vector2.Dot(o - P0, tU), qq = Vector2.Dot(o - P0, leftU);
                            if (us.Count > 0 && uu <= us[us.Count - 1] + 1e-3f) continue;
                            us.Add(uu); qs.Add(qq); hs.Add(hh);
                        }
                        e.lmEase = keep;
                        d0 += e.length; from = fwd ? e.b : e.a;
                    }
                }
                if (us.Count < 8) { Skip(ci, "own line too short"); continue; }
                f.ownU = us.ToArray(); f.ownQ = qs.ToArray(); f.ownH = hs.ToArray();
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
                // PHASE C (scope city): the move read over the whole blend, the end where the own line runs on
                // parallel, never a junction crossed off the own line (PhaseCLength)
                // (Beatties Ford's 19 nodes keep J1's lengths under city: the owner has those pictures - spot 37's
                // nose was tuned on them; a longer NB blend there moved the SB carriageway's outer edge 1.6 m)
                if (CityPhaseC && !BfrNode(map, sp.node)) L = PhaseCLength(map, f, rate, rmin, uMax, L);
                if (CityPhaseC) f.devMax = BlendDev(f, Mathf.Min(L, Mathf.Max(10f, f.room)));
                if (L > f.room) { f.capped = true; f.note = $"room {f.room:0.0} m for L {L:0.0} m" + (f.note.Length > 0 ? "; " + f.note : ""); L = Mathf.Max(10f, f.room); }
                // Phase C (the census, 15:45: 29 "corners in the line" mid-road on West Blvd, South Blvd, Eastway Dr,
                // none under bfr): the cubic's end slope is clamped at 0.15, so a blend that ends where the own line
                // still moves faster than that (a cap - room, a side street, the hold near the own line - landing
                // in the middle of a wide divergence) joins it with a corner. Under city the end must sit where the
                // own line runs at PhaseCEndSlopeMax or less: the longest such blend up to L, else no profile here
                // (the old taper, listed). Beatties Ford's 19 nodes keep J1's drawing.
                if (CityPhaseC && !BfrNode(map, sp.node))
                {
                    float SlopeRaw(float uu) => (f.OwnQAt(uu + 2f) - f.OwnQAt(uu - 2f)) / 4f;
                    if (Mathf.Abs(SlopeRaw(f.uStart + L)) > PhaseCEndSlopeMax)
                    {
                        float Lk = float.NaN;
                        for (float l = L; l >= 10f; l -= 1f) if (Mathf.Abs(SlopeRaw(f.uStart + l)) <= PhaseCEndSlopeMax) { Lk = l; break; }
                        if (float.IsNaN(Lk)) { PhaseCSteepEnd++; Skip(ci, $"the own line still moves {Mathf.Abs(SlopeRaw(f.uStart + L)):0.00} m/m where the blend can end (a corner in the lane): no profile"); continue; }
                        PhaseCEndShortened++;
                        f.note = $"Phase C: ended where the own line runs at {PhaseCEndSlopeMax:0.00} or less (L {L:0.0} -> {Lk:0.0})" + (f.note.Length > 0 ? "; " + f.note : "");
                        f.capped = f.capped || Lk < L - 0.5f;
                        L = Lk;
                    }
                }
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
            // the lane gap from each band's own half width there (the fix round: n10035's SB turn lane
            // narrows over e11017, so a constant half width read the gap 0.8 m too small and the
            // pavements, widened by that, left a grass crack)
            float GapLanes(float uu) => fa == null ? float.NaN : Mathf.Abs(fa.TAt(uu) - fb.TAt(uu)) - fa.HAt(uu) - fb.HAt(uu);
            // the two carriageways carry more lanes than the undivided road (Beatties Ford n7869: a 2-lane
            // road into 2 + 3 lanes): their lane bands would start on top of each other and cross while
            // they blend - no profile, the old taper (listed)
            if (fa != null)
            {
                // (judged on the carriageways' full lane counts, as the first pass did: a band that carries
                // more lanes than the undivided road gains them somewhere in the blend - n9152's lane gap
                // read along the chain reaches the nose's 2.4 m only 196 m on)
                float GapFull(float uu) => Mathf.Abs(fa.TAt(uu) - fb.TAt(uu)) - fa.lanesHalf - fb.lanesHalf;
                float u0 = Mathf.Max(fa.uStart, fb.uStart), worst = 0f;
                for (float uu = u0; uu <= Mathf.Max(fa.uEnd, fb.uEnd); uu += 1f) worst = Mathf.Min(worst, GapFull(uu));
                if (worst < -0.3f || Mathf.Sign(fa.TAt(u0) - fb.TAt(u0)) != Mathf.Sign(fa.OwnQAt(Mathf.Max(fa.uEnd, fb.uEnd)) - fb.OwnQAt(Mathf.Max(fa.uEnd, fb.uEnd))))
                {
                    foreach (var f in frames) Skip(f.edge, $"the two carriageways' lane bands overlap {-worst:0.00} m or cross while they blend (more lanes than the undivided road carries)");
                    return done;
                }
            }
            // THE NOSE (the fix round; the first pass grew the island from 0 along a flank sized to the
            // widest gap 40 m on - n10035: under 0.8 m for 10 m, a grass crack, then a 0.65 m step where
            // the widening ended - and let the shoulders overlap where the bands met). Now each side's
            // inner pavement edge is OffAt metres off its own lane edge:
            //   before the tip  half the lane gap (the two pavements meet in its middle: flush, no
            //                   overlap - a shoulder wider than half the gap is trimmed);
            //   the tip         where the gap reaches SplitIslandGapM: a SplitNoseR round end, the
            //                   island 1.2 m wide, SplitNoseOffM off each lane edge;
            //   the flank       SplitNoseOffM eased to the carriageway's own shoulder over noseLn;
            //   past it         its own shoulder (no widening, no step), never past the gap's middle.
            bool noseOn = fa != null && NoseRuleOn && fa.innerSign != fb.innerSign;
            float noseLn = 0f, noseEnd = 0f;
            if (noseOn)
            {
                // (searched along both own lines: a tip past the blends is late, never missing - a flush
                // widening that stopped where the profile does would leave a step)
                float u0 = Mathf.Max(fa.uStart, fb.uStart), u1 = Mathf.Min(fa.ownU[fa.ownU.Length - 1], fb.ownU[fb.ownU.Length - 1]) - 2f;
                if (GapLanes(u0) >= SplitIslandGapM) uTip = u0 + RefugeTipM;
                else for (float uu = u0; uu <= u1; uu += 0.25f) if (GapLanes(uu) >= SplitIslandGapM) { uTip = uu; break; }
                // the bands part only far past the blends (n9152: 196.6 m, a flush stretch that long is a
                // painted median nobody asked for) or never: no profile here, the old taper (listed)
                float uLim = Mathf.Max(fa.uEnd, fb.uEnd) + SplitNoseLateM;
                // (a tight twin has no island: its pavements stay flush all along, or to a tip however late)
                if ((float.IsNaN(uTip) || uTip > uLim) && !(CityPhaseC && tightTwinNodes.Contains(sp.node)))
                {
                    foreach (var f in frames) Skip(f.edge, float.IsNaN(uTip) ? "the lane bands never part 2.4 m (no nose)" : $"the lane bands part 2.4 m only at u {uTip:0.0}, past the blends (+{SplitNoseLateM:0} m: {uLim:0.0})");
                    return done;
                }
                if (!float.IsNaN(uTip))
                {
                    noseLn = Mathf.Max(SplitNoseFlankMinM, 10f * Mathf.Max(Mathf.Abs(fa.shIn - SplitNoseOffM), Mathf.Abs(fb.shIn - SplitNoseOffM)));
                    noseEnd = uTip + SplitNoseR + Mathf.Max(noseLn, (0.5f * GapLanes(uTip) - SplitNoseOffM) / SplitNoseFlare) + 1f;
                    splitNoseNodes.Add(sp.node);
                    fa.uTip = fb.uTip = uTip; fa.noseLn = fb.noseLn = noseLn;
                }
            }
            float OffAt(SplitFrame f, float uu)
            {
                float half = 0.5f * Mathf.Max(0f, GapLanes(uu)), off;
                if (float.IsNaN(uTip)) off = f.shIn;                 // no island anywhere: only the overlap trimmed
                else if (uu < uTip) off = half;
                else
                {
                    // the island's half width from the gap's middle: the round tip, then a 1:4 flare (where
                    // the lanes are already wider apart than 2.4 m at a fan's edge - n7868: 4.3 m - the
                    // island still starts 1.2 m wide and flares out); never closer to a lane edge than the
                    // nose's 0.6 m, eased to the carriageway's own shoulder over noseLn
                    float x = uu - uTip, r = SplitNoseR;
                    float ih = x < r ? Mathf.Sqrt(Mathf.Max(0f, r * r - (r - x) * (r - x))) : r + (x - r) * SplitNoseFlare;
                    float offMin = SplitNoseOffM + (f.shIn - SplitNoseOffM) * Smooth(Mathf.Max(0f, x - r) / Mathf.Max(0.1f, noseLn));
                    off = Mathf.Max(half - ih, offMin);
                }
                return Mathf.Min(off, half);
            }
            if (noseOn)
            {
                var tab = new List<(float, float, float, float)>(160);
                float uA = Mathf.Max(fa.uStart, fb.uStart), uB = Mathf.Min(Mathf.Max(fa.uEnd, fb.uEnd), (float.IsNaN(uTip) ? 0f : noseEnd) + 60f);
                for (float uu = uA; uu <= uB + 1e-3f; uu += 0.5f) tab.Add((uu, GapLanes(uu), OffAt(fa, uu), OffAt(fb, uu)));
                SplitNoseTable[sp.node] = tab;
            }
            // ---- THE EASES ----
            foreach (var f in frames)
            {
                int from = sp.node; float d0 = 0f; int hint = 0;
                float uLast = Mathf.Max(f.uEnd, float.IsNaN(uTip) ? 0f : noseEnd) + 1f;
                // a tight twin with no tip: the shoulders stay trimmed to the divider all along the pair
                if (float.IsNaN(uTip) && noseOn && CityPhaseC && tightTwinNodes.Contains(sp.node)) uLast = 1e6f;
                var fo = fa == f ? fb : fa;
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
                        // the nose: the inner pavement edge OffAt off the lane edge (flush before the
                        // tip, the round tip and flank, then the own shoulder - trimmed wherever the
                        // bands come closer than the two shoulders, over the whole profile)
                        if (noseOn && fo != null && uAx <= uLast)
                        {
                            float w = OffAt(f, uAx) - f.shIn;
                            wd[k] = w; if (Mathf.Abs(w) > 0.005f) anyW = true;
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
                        else if (CityPhaseC && splitFrameEdge[ei] != f && !splitFrameEdge2.ContainsKey(ei)) splitFrameEdge2[ei] = f;   // (bfr / all: J1 kept the first frame only)
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

        // ================================================================
        //  PHASE C (splits2, 2026-11-16 afternoon): the blend's length city-wide
        // ================================================================
        // J1 read the move d at most 60 m on (a straight frame reads a curving
        // road's drift as a move). City-wide, under 'all', 395 of the 664 ends
        // the profile drew still failed the census's L/2: the own line went on
        // diverging past 60 m (a median opening to 10-20 m), or bowed out and
        // back (n7868: 3.6 m), so the blend joined it mid-move and the drawn
        // lanes then followed OSM's divergence at its own rate. Now (scope city):
        //   * d is the own line's FURTHEST point from where the lanes start, over
        //     the whole blend (iterated: a longer blend reads further), so a
        //     divergence that goes on, or a bow, is taken in whole;
        //   * the blend ends where the own line runs on nearly parallel to the
        //     axis (|slope| <= PhaseCEndSlope) - a divergence that settles - and
        //     never at a point where it is still turning; a curving road (no
        //     such point within PhaseCMaxL) keeps J1's length;
        //   * a side street's junction on the carriageway within the blend: the
        //     blend ends PhaseCJunctionClearM before it where the drawn lanes
        //     would still be more than PhaseCJunctionOffM off the own line there
        //     (the side street meets the carriageway where OSM has it).
        public const float PhaseCMaxL = 240f, PhaseCEndSlope = 0.08f, PhaseCJunctionOffM = 0.5f, PhaseCJunctionClearM = 6f, PhaseCDevLaneM = 3.66f;
        /// <summary>PSX_CITY_SHORTPROFILE=0: short islands off the refuge list keep PC45's taper under city too.</summary>
        public static bool PhaseCShortOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SHORTPROFILE") != "0";
        public static int PhaseCLonger, PhaseCCurving, PhaseCJunctionCap, PhaseCDevCap, PhaseCSteepEnd, PhaseCEndShortened;
        /// <summary>Phase C: the own line's slope where a blend may end (the cubic's end tangent is clamped at 0.15).</summary>
        public const float PhaseCEndSlopeMax = 0.12f;

        /// <summary>Is this one of Beatties Ford's 19 split-type nodes (graph b2aa3453)?</summary>
        static bool BfrNode(CityMap map, int node)
        {
            if (map.graphHash != SplitsGraph) return false;
            bfrSplitSet ??= new HashSet<int>(BfrSplitNodes);
            return bfrSplitSet.Contains(node);
        }
        /// <summary>A blend of length L's furthest distance off the own line (the Hermite TAt would draw).</summary>
        static float BlendDev(SplitFrame f, float L)
        {
            L = Mathf.Max(1f, L);
            float qE = f.OwnQAt(f.uStart + L);
            float sE = Mathf.Clamp((f.OwnQAt(f.uStart + L + 2f) - f.OwnQAt(f.uStart + L - 2f)) / 4f, -0.15f, 0.15f);
            float dev = 0f;
            for (float x = 0f; x <= L + 1e-3f; x += 1f)
            {
                float t = x / L, t2 = t * t, t3 = t2 * t;
                float q = f.lambda0 * (2f * t3 - 3f * t2 + 1f) + qE * (-2f * t3 + 3f * t2) + L * sE * (t3 - t2);
                dev = Mathf.Max(dev, Mathf.Abs(q - f.OwnQAt(f.uStart + x)));
            }
            return dev;
        }

        static float PhaseCLength(CityMap map, SplitFrame f, float rate, float rmin, float uMax, float L1)
        {
            float uTop = Mathf.Min(uMax - 2f, f.uStart + PhaseCMaxL);
            float Slope(float uu) => (f.OwnQAt(uu + 2f) - f.OwnQAt(uu - 2f)) / 4f;
            // the length for the whole move (iterated: a longer blend reads further)
            float L = Mathf.Max(SplitFloorM, L1);
            for (int it = 0; it < 10; it++)
            {
                float uR = Mathf.Min(f.uStart + L, uTop), dq = 0f;
                for (float uu = f.uStart; uu <= uR + 1e-3f; uu += 1f) dq = Mathf.Max(dq, Mathf.Abs(f.OwnQAt(uu) - f.lambda0));
                float Ln = Mathf.Min(PhaseCMaxL, Mathf.Max(SplitFloorM, Mathf.Max(1.2f * rate * dq, Mathf.Sqrt(6f * dq * rmin))));
                if (Ln <= L + 0.05f) break;
                L = Ln;
            }
            // the end where the own line runs on parallel and straight (a divergence that settles; the top of
            // a bow is level but not straight: the next 30 m must stay within 0.4 m of the tangent there)
            bool Settled(float uu)
            {
                float sl = Slope(uu);
                if (Mathf.Abs(sl) > PhaseCEndSlope) return false;
                float q0 = f.OwnQAt(uu);
                for (float k = 5f; k <= 30f; k += 5f)
                {
                    if (uu + k > uMax) break;
                    if (Mathf.Abs(f.OwnQAt(uu + k) - (q0 + sl * k)) > 0.4f) return false;
                }
                return true;
            }
            float uEnd = float.NaN;
            // (the chain ends before the length wanted: the room decides, as J1)
            if (f.uStart + L >= uTop) uEnd = f.uStart + L;
            else for (float uu = f.uStart + L; uu <= uTop; uu += 1f)
                if (Settled(uu)) { uEnd = uu; break; }
            if (float.IsNaN(uEnd)) { PhaseCCurving++; f.note = "Phase C: the own line still turning to its end (a curving road): J1's length"; return L1; }
            L = Mathf.Max(L1, uEnd - f.uStart);
            // never further off the own line (OSM's carriageway, where the lots, kerbs and buildings are) than
            // J1's blend was, or than the lanes start off it at the node, plus half a metre - or a lane: a
            // longer blend across a fast divergence would draw the road metres from where it is (Shopton Rd: 14 m)
            {
                float dev0 = Mathf.Abs(f.lambda0 - f.OwnQAt(f.uStart));
                float lim = Mathf.Max(PhaseCDevLaneM, Mathf.Max(dev0, BlendDev(f, L1)) + 0.5f);
                if (BlendDev(f, L) > lim)
                {
                    float lo = L1, hi = L;
                    for (int it = 0; it < 12 && hi - lo > 1f; it++) { float mid = 0.5f * (lo + hi); if (BlendDev(f, mid) > lim) hi = mid; else lo = mid; }
                    PhaseCDevCap++;
                    f.note = $"Phase C: held to {lim:0.0} m off the own line (L {L:0.0} -> {lo:0.0})";
                    L = lo;
                }
            }
            if (L > L1 + 0.5f) PhaseCLonger++;
            // a side street's junction within the blend, where the lanes would still be off the own line
            float uJ = FirstSideStreetU(map, f);
            if (!float.IsNaN(uJ) && uJ < f.uStart + L)
            {
                float lam = f.lambda0, qE = f.OwnQAt(f.uStart + L), sE = Mathf.Clamp(Slope(f.uStart + L), -0.15f, 0.15f);
                float t = Mathf.Clamp01((uJ - f.uStart) / L), t2 = t * t, t3 = t2 * t;
                float q = lam * (2f * t3 - 3f * t2 + 1f) + qE * (-2f * t3 + 3f * t2) + L * sE * (t3 - t2);
                if (Mathf.Abs(q - f.OwnQAt(uJ)) > PhaseCJunctionOffM)
                {
                    float Lj = Mathf.Max(10f, uJ - PhaseCJunctionClearM - f.uStart);
                    if (Lj < L)
                    {
                        PhaseCJunctionCap++;
                        f.capped = Lj < L1 - 0.5f;
                        f.note = $"Phase C: a side street's junction at u {uJ:0.0}: the blend ends {PhaseCJunctionClearM:0} m before it (L {L:0.0} -> {Lj:0.0})";
                        L = Lj;
                    }
                }
            }
            return L;
        }

        /// <summary>The axis u of the first node along a framed carriageway's chain where a street of
        /// another name meets it (NaN: none).</summary>
        static float FirstSideStreetU(CityMap map, SplitFrame f)
        {
            int from = f.node;
            for (int k = 0; k < f.chain.Length; k++)
            {
                var e = map.edges[f.chain[k]];
                int far = e.a == from ? e.b : e.a;
                var arms = map.nodeEdges[far];
                if (arms != null)
                    foreach (int i in arms)
                    {
                        var o = map.edges[i];
                        if (o.a == o.b) continue;
                        if (o.name != e.name) return Vector2.Dot(map.nodes[far] - f.p0, f.tU);
                    }
                from = far;
            }
            return float.NaN;
        }

        // ================================================================
        //  PHASE C: THE TIGHT TWINS (the owner's N Tryon wishbone, 2026-11-17)
        // ================================================================
        // The owner on the split past his North Tryon Street spot (n3158), in both columns of the before /
        // after sheet: "this cannot be how the real roads are" - the road forks into two carriageways that
        // swing apart at 26 degrees round a wide grass wedge within 30 m. The real road (aerial and street
        // level, viewed only): seven lanes side by side, dead straight - three eastbound, the centre lane
        // (hatched paint, then the westbound left-turn lane) and three westbound; no grass, a painted
        // divider and, at the junction, a kerb a metre wide. OSM maps it from that point as two one-way
        // ways 12.0-12.3 m apart - CLOSER than their own lanes need (3 + 4 lanes of 3.66 m: 12.8 m): a
        // divided road only on paper. The line model drew each carriageway on its way plus the SPLT
        // taper's outward shift (7 m of grass at 20 m), and further on, where the shift had eased out,
        // the two pavements over each other.
        // THE RULE (scope city; PSX_CITY_TIGHTTWINS=0 off): where the two carriageways of a split run
        // alongside with their lane bands closer than TightGapM (the double yellow's gap: each roadway's
        // yellow left edge line 6 cm inside its band) once past the mapped fan, the pair is a TIGHT TWIN:
        // both are pushed apart by half the shortfall each, all along the run (one sampled shift ease an
        // edge, made once whichever end's split finds the pair) - so they run parallel, band to band, the
        // two yellow edge lines a double yellow between them. The split's profile then starts each
        // carriageway where its lanes are in the undivided road and blends onto that pushed line: no
        // fan, no wedge, no island (the bands never part 2.4 m: the pavements stay flush all along).
        public const float TightGapM = 0.12f, TightPastFanM = 40f, TightMinRunM = 15f;
        public static bool PhaseCTightOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TIGHTTWINS") != "0";
        static readonly HashSet<int> tightTwinEdges = new HashSet<int>();
        static readonly HashSet<int> tightTwinNodes = new HashSet<int>();
        public static int TightTwinPairs, TightTwinEdgeCount;
        public static float TightTwinM;
        public static readonly List<string> TightTwinLog = new List<string>();
        public static bool TightTwinNode(int node) => tightTwinNodes.Contains(node);
        public static bool TightTwinEdge(int edge) => tightTwinEdges.Contains(edge);

        struct TwinSample { public Vector2 p; public float half, d; }

        static List<TwinSample> TwinLine(CityMap map, List<Ease>[] add, List<int> chain, int node, float step)
        {
            var pts = new List<TwinSample>(256);
            int from = node; float d0 = 0f;
            foreach (int ei in chain)
            {
                var e = map.edges[ei];
                bool fwd = e.a == from;
                var keep = e.lmEase; e.lmEase = PreEases(add, e);
                int n = Mathf.Max(2, Mathf.CeilToInt(e.length / step) + 1);
                float ds = e.length / (n - 1);
                for (int k = pts.Count == 0 ? 0 : 1; k < n; k++)
                {
                    float s = fwd ? k * ds : e.length - k * ds;
                    var t = e.TangentAt(s); var nl = new Vector2(-t.y, t.x);
                    float c = LanesPre(e, s, out float hh);
                    pts.Add(new TwinSample { p = e.PointAt(s) + nl * c, half = hh, d = d0 + k * ds });
                }
                e.lmEase = keep;
                d0 += e.length; from = fwd ? e.b : e.a;
            }
            return pts;
        }

        /// <summary>The nearest point of a twin line to p: its distance, the lane band's half width there,
        /// and whether the foot is past the line's end (no twin beside p any more).</summary>
        static float TwinNearest(List<TwinSample> L, Vector2 p, out float half, out Vector2 at, out bool pastEnd)
        {
            float best = float.MaxValue; half = 0f; at = L[0].p; pastEnd = false;
            for (int i = 1; i < L.Count; i++)
            {
                Vector2 a = L[i - 1].p, ab = L[i].p - a;
                float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                var q = a + ab * t;
                float d = (p - q).sqrMagnitude;
                if (d < best) { best = d; at = q; half = Mathf.Lerp(L[i - 1].half, L[i].half, t); pastEnd = i == L.Count - 1 && t >= 0.999f; }
            }
            return Mathf.Sqrt(best);
        }

        static void BuildTightTwins(CityMap map, List<Ease>[] add, Dictionary<int, Split> splitAt, HashSet<int> splitNodes, HashSet<long> refugeEnds)
        {
            var chA = new List<int>(16); var chB = new List<int>(16);
            foreach (var sp in splitAt.Values)
            {
                if (!InSplitScope(map, sp.node) || BfrNode(map, sp.node)) continue;
                if (sp.a < 0 || sp.b < 0 || sp.a >= map.edges.Length || sp.b >= map.edges.Length) continue;
                if (refugeEnds.Contains(RefugeKey(sp.node, sp.a)) || refugeEnds.Contains(RefugeKey(sp.node, sp.b))) continue;
                var A = map.edges[sp.a]; var B = map.edges[sp.b];
                if (!A.oneway || !B.oneway || A.a == A.b || B.a == B.b) continue;
                CarriagewayChain(map, splitNodes, sp.a, sp.node, 420f, chA, out int farA, out float lenA);
                CarriagewayChain(map, splitNodes, sp.b, sp.node, 420f, chB, out int farB, out float lenB);
                if (chA.Count == 0 || chB.Count == 0 || Mathf.Min(lenA, lenB) < 25f) continue;
                // the pair made already from its other end: this node's split is a tight twin's too
                if (tightTwinEdges.Contains(sp.a) && tightTwinEdges.Contains(sp.b)) { tightTwinNodes.Add(sp.node); continue; }
                if (tightTwinEdges.Contains(sp.a) || tightTwinEdges.Contains(sp.b)) continue;
                var LA = TwinLine(map, add, chA, sp.node, 1f); var LB = TwinLine(map, add, chB, sp.node, 1f);
                // tight past the mapped fan: the shortfall's run from TightPastFanM on (half the pair on a short one)
                float past = Mathf.Min(TightPastFanM, 0.5f * Mathf.Min(lenA, lenB)), run = 0f, worst = 0f, runAll = 0f;
                for (int i = 1; i < LA.Count; i++)
                {
                    float d = TwinNearest(LB, LA[i].p, out float hb, out _, out bool end);
                    if (end) break;
                    float gap = d - LA[i].half - hb;
                    if (gap < TightGapM - 0.05f) { runAll += LA[i].d - LA[i - 1].d; if (LA[i].d >= past) { run += LA[i].d - LA[i - 1].d; worst = Mathf.Min(worst, gap); } }
                }
                if (run < TightMinRunM) continue;
                TightTwinPairs++; TightTwinM += runAll;
                tightTwinNodes.Add(sp.node);
                if (TightTwinLog.Count < 400) TightTwinLog.Add($"n{sp.node} e{sp.a}/e{sp.b} '{A.name}': the two carriageways' lane bands {worst:+0.00;-0.00} m apart at their closest past the fan, short of the divider's {TightGapM:0.00} m over {run:0} m: pushed apart (chains {lenA:0} / {lenB:0} m)");
                // the far ends: a pair that closes at one split node keeps its push to that node (its own
                // profile starts there); any other end eases the push out before it
                bool closes = farA == farB && farA >= 0 && splitNodes.Contains(farA);
                for (int c = 0; c < 2; c++)
                {
                    var chain = c == 0 ? chA : chB; var other = c == 0 ? LB : LA; var own = c == 0 ? LA : LB;
                    float chainLen = c == 0 ? lenA : lenB;
                    // pass 1: what each sample needs (m, unsigned) and which way (the edge's left = +1)
                    var needs = new List<float[]>(chain.Count); var sides = new List<float[]>(chain.Count);
                    var dsOf = new List<float>(chain.Count); var fwdOf = new List<bool>(chain.Count);
                    int from = sp.node; float mph = 0f;
                    foreach (int ei in chain)
                    {
                        var e = map.edges[ei];
                        bool fwd = e.a == from;
                        mph = Mathf.Max(mph, SpeedMph(e));
                        var keep = e.lmEase; e.lmEase = PreEases(add, e);
                        int n = Mathf.Max(2, Mathf.CeilToInt(e.length / RefugeDs) + 1);
                        float ds = e.length / (n - 1);
                        var nd = new float[n]; var sd = new float[n];
                        for (int k = 0; k < n; k++)
                        {
                            float s = fwd ? k * ds : e.length - k * ds;
                            var t = e.TangentAt(s); var el = new Vector2(-t.y, t.x);
                            float cc = LanesPre(e, s, out float hh);
                            var p = e.PointAt(s) + el * cc;
                            float d = TwinNearest(other, p, out float ho, out var at, out bool end);
                            if (end) continue;
                            // away from the other carriageway (at the fan, where the two lines cross: its side further on)
                            Vector2 nrm;
                            if (d > 0.5f) nrm = (p - at) / d;
                            else
                            {
                                var far = own[Mathf.Min(own.Count - 1, 30)].p;
                                TwinNearest(other, far, out _, out var atF, out _);
                                nrm = (far - atF).sqrMagnitude > 1e-4f ? (far - atF).normalized : el;
                            }
                            sd[k] = Vector2.Dot(nrm, el) >= 0f ? 1f : -1f;
                            nd[k] = Mathf.Max(0f, 0.5f * (TightGapM - (d - hh - ho)));
                        }
                        e.lmEase = keep;
                        needs.Add(nd); sides.Add(sd); dsOf.Add(ds); fwdOf.Add(fwd);
                        from = fwd ? e.b : e.a;
                    }
                    // pass 2: no step in the push. Inside the mapped fan (the first TightPastFanM from the fork,
                    // and before the closing fork) the need is steep by nature - it only undoes the fan - and
                    // stands as it is. Past the fan the push is the need's upper envelope under the shifting
                    // taper's slope (the lanes move sideways no faster than the speed allows), eased to nothing
                    // before an open end. (The envelope is built from the needs PAST the fan only: built from
                    // all of them it carried the fan's 6 m along the whole pair - an island again.)
                    float slope = 1f / Mathf.Max(10f, mph <= 40f ? mph * mph / 60f : mph);
                    {
                        var env = new List<float[]>(needs.Count);
                        float dAt = 0f;
                        for (int j = 0; j < needs.Count; j++)
                        {
                            var ev = new float[needs[j].Length];
                            for (int k = 0; k < ev.Length; k++)
                            {
                                float dd = dAt + k * dsOf[j];
                                bool fan = dd < TightPastFanM || (closes && chainLen - dd < TightPastFanM);
                                ev[k] = fan ? 0f : needs[j][k];
                            }
                            env.Add(ev); dAt += dsOf[j] * (ev.Length - 1);
                        }
                        float prev = 0f, lastSide = 0f;
                        for (int j = 0; j < env.Count; j++)
                            for (int k = 0; k < env[j].Length; k++)
                            {
                                env[j][k] = Mathf.Max(env[j][k], prev - (k == 0 ? 0f : slope * dsOf[j]));
                                prev = env[j][k];
                                if (sides[j][k] != 0f) lastSide = sides[j][k]; else sides[j][k] = lastSide;
                            }
                        prev = 0f; lastSide = 0f;
                        float dEnd = 0f;
                        for (int j = env.Count - 1; j >= 0; j--)
                            for (int k = env[j].Length - 1; k >= 0; k--)
                            {
                                float v = Mathf.Max(env[j][k], prev - (k == env[j].Length - 1 ? 0f : slope * dsOf[j]));
                                env[j][k] = v; prev = v;
                                float want = Mathf.Max(needs[j][k], v);
                                if (!closes) want = Mathf.Min(want, dEnd * slope);
                                needs[j][k] = want;
                                if (sides[j][k] != 0f) lastSide = sides[j][k]; else sides[j][k] = lastSide;
                                if (k > 0) dEnd += dsOf[j];
                            }
                    }
                    // pass 3: one sampled shift ease an edge
                    float d0 = 0f;
                    for (int j = 0; j < chain.Count; j++)
                    {
                        var e = map.edges[chain[j]];
                        var sh = new float[needs[j].Length]; bool any = false;
                        for (int k = 0; k < sh.Length; k++) { sh[k] = needs[j][k] * (sides[j][k] == 0f ? 1f : sides[j][k]); any |= needs[j][k] > 1e-3f; }
                        if (any)
                        {
                            (add[e.index] ??= new List<Ease>(3)).Add(new Ease { shift = true, fromA = fwdOf[j], dw = 1f, len = d0 + e.length + 1f, d0 = d0, prof = sh, profD0 = d0, profDs = dsOf[j], tag = -(3000000 + sp.node * 2 + c) });
                            TightTwinEdgeCount++;
                        }
                        tightTwinEdges.Add(chain[j]);
                        d0 += e.length;
                    }
                }
            }
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
