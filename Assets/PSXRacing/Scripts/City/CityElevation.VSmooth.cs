using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE DRAWN PROFILE: vertical curves on every grade break (2026-10-25).
    ///
    /// The owner, from a night race through Charlotte at ~150 mph: "Every bump
    /// in the road is angular instead of rounded/smooth and causes harsh
    /// bottoming out ... Roads need to be smoothed, no harsh angles to adjust
    /// height/elevation, ever."
    ///
    /// The solve's heights live at 10 m stations and every reader joined them
    /// with straight lines, so every station was a grade break: the 4.5% feet
    /// of a crossing's hump and of a raised node's cone, the trench V under a
    /// street bridge, a ramp's 8% climb-out off its seat, the DEM's cell
    /// breaks, two arms meeting at a node. The crest limiter (VerticalCurves)
    /// spreads a crest over stations, but each station is still a corner, and
    /// sags it leaves alone - and a sag is where a car at speed bottoms out.
    ///
    /// Three earlier attempts rounded sags INSIDE the solve (PSX_CITY_VSAG,
    /// raise-only sags, designed trench cuts) and each pumped against the
    /// solve's pins: decks that may only rise, seats that may not move,
    /// clearances. So this is a PRESENTATION pass, after the solve has
    /// settled, that never feeds back into it. The stations stay exactly as
    /// solved (stS/stY, and every audit that reads them); the edge gets a
    /// fine drawn profile (<see cref="CityMap.Edge.vS"/>) that YAt returns, so
    /// the ribbon, its collider, the paint, verges, decks, the land graded to
    /// the road, the props and every path all read one curved surface.
    ///
    /// THE CURVES. Along every chain of edges - through two-arm nodes, and
    /// through each junction's MAJOR through pair (the one the junction
    /// landings keep level across) - each break A at a station or node gets a
    /// symmetric parabolic vertical curve of length L centred on it:
    ///     y += A / (2L) (L/2 - |u - u_k|)^2 inside the window.
    /// The sum of those is C1 everywhere (each one cancels its own corner),
    /// its curvature A/L, so L = R A gives radius R: AASHTO's comfort radius
    /// at the road's design speed for a sag (VerticalMph, SagComfortR), the
    /// crest limiter's own radius for a crest (CrestR). A sag's curve lies
    /// inside its V - it only RAISES - and a crest's lowers, by A L / 8.
    ///
    /// WHAT MAY NOT MOVE, and how far: each window is shortened until the
    /// sum respects every station's bounds -
    ///   - under a deck, the road below may rise only while the clearance
    ///     stays over ClearanceM - 0.1 m (and never where it is already less);
    ///     the deck over it may fall 3 cm at most, never under that floor;
    ///   - on structure, over a water span or a creek pipe: 3 cm of lowering;
    ///   - where a deck or structure holds the curve's own side, the other
    ///     side makes room: a trench's floor is DUG round its break (up to
    ///     0.6 m, eased back over tapers adding at most 1.5% of grade) or a
    ///     crest on a deck LIFTED, only where nothing holds that side;
    ///   - a SEATED station (a ramp, or a street branch, inside its host's
    ///     pavement) follows its host's drawn profile to within 4 cm: the
    ///     host's change is carried onto the branch and eased off past the
    ///     seat (a C1 Hermite), and the branch's own curves stay within 4 cm;
    ///   - otherwise a sag rises at most 1.0 m on a freeway or a ramp, 0.5 m
    ///     on a street (its driveways and buildings stay where they are), and
    ///     a crest falls at most 0.15 m (the land is graded to the drawn
    ///     road, so neither opens a gap);
    ///   - a window never runs past a chain's end (a dead end, a minor arm at
    ///     its junction), so an end keeps its height and grade - except that a
    ///     minor arm takes its junction's change (the major road's height and
    ///     cross-grade at the node) and eases it off the same way as a seat.
    /// What a window could not have (its radius under the need, and why) is
    /// listed for the audit (<see cref="VsCapped"/>).
    ///
    /// THE KNOTS. Between two breakpoints (stations, window ends, ease ends)
    /// the curvature is a constant sum, so each piece is cut into as many
    /// chords as keep the grade change from one to the next under
    /// <see cref="VsKnotTheta"/> (0.45%), and no shorter than 0.6 m (the tile
    /// builder merges sections closer than 0.5 m). CityMeshes samples the
    /// ribbon at every knot (<see cref="CityMap.Edge.ProfileKnots"/>), so the
    /// drawn surface IS this profile. An edge that needed nothing keeps
    /// vS = null and draws exactly as before.
    ///
    /// PSX_CITY_VSMOOTH=0 switches it off (the A/B; <see cref="ApplyVSmooth"/>
    /// switches it in the editor without a re-solve).
    /// </summary>
    public static partial class CityElevation
    {
        public static bool VSmoothOn = System.Environment.GetEnvironmentVariable("PSX_CITY_VSMOOTH") != "0";
        /// <summary>What the last presentation pass did, for the load log and the audits.</summary>
        public static string VSmoothReport { get; private set; } = "";
        public static bool VSmoothApplied { get; private set; }
        /// <summary>PSX_VS_DUMP="e1,e2": the chains holding these edges, point
        /// by point, with their eases, shifts and curves (the census writes it).</summary>
        public static readonly System.Text.StringBuilder VsDump = new System.Text.StringBuilder();
        static readonly HashSet<int> vsDumpEdges = ParseDump();
        static HashSet<int> ParseDump()
        {
            var h = new HashSet<int>();
            foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_VS_DUMP") ?? "").Split(','))
                if (int.TryParse(t.Trim().TrimStart('e'), out int v)) h.Add(v);
            return h;
        }
        public static float VSmoothMs { get; private set; }
        /// <summary>The node heights as the solve left them (the pass moves a
        /// node with the curve through it; switching off puts them back).</summary>
        static float[] nodeYSolved;

        /// <summary>A curve that got less than its road needs (L &lt; R A): the
        /// edge and arc position of its centre, the break, its length and the
        /// length it wanted, and what stopped it.</summary>
        public struct VsFillet { public int edge; public float s, A, L, Lwant; public byte cause; }
        public static readonly List<VsFillet> VsCapped = new List<VsFillet>();
        public const byte VsCauseEnds = 1, VsCauseDeck = 2, VsCauseSeat = 3, VsCauseRaise = 4, VsCauseLower = 5, VsCauseStructure = 6, VsCauseMaxL = 7;
        public static string VsCauseName(byte c) =>
            c == VsCauseEnds ? "chain end (junction or dead end)" : c == VsCauseDeck ? "deck clearance" : c == VsCauseSeat ? "seated on its host"
            : c == VsCauseRaise ? "raise cap" : c == VsCauseLower ? "lowering cap" : c == VsCauseStructure ? "on structure / pipe" : c == VsCauseMaxL ? "max length" : "-";

        /// <summary>Breaks smaller than this are left as they are (0.1% of grade).</summary>
        const float VsMinBreak = 0.001f;
        /// <summary>The most two consecutive knots' chords may differ in grade.</summary>
        public const float VsKnotTheta = 0.0045f;
        const float VsKnotMinM = 0.6f;
        /// <summary>The clearance under a deck the curves may not take a road below.</summary>
        const float VsClearFloorM = ClearanceM - 0.1f;
        const float VsRaiseMaxM = 1.0f, VsLowerMaxM = 0.15f, VsLowerStructM = 0.03f, VsSeatTolM = 0.04f;
        /// <summary>A street's sag rises at most this (a freeway or a ramp
        /// VsRaiseMaxM): its kerbs, driveways and the buildings along it do
        /// not follow the road up (the play check's drive-off found a raised
        /// North Tryon Street fill running into a building).</summary>
        const float VsRaiseStreetM = 0.5f;
        const float VsMaxLM = 400f;
        /// <summary>The most a floor is dug (or a crest lifted) to make a curve
        /// room, and the steepest its tapers may add to the grade.</summary>
        const float VsShiftMaxM = 0.6f, VsShiftGrade = 0.015f;
        static readonly float[] VsShiftTries = { 1f, 0.6f, 0.35f, 0.2f, 0.1f, 0.05f };
        /// <summary>A seat run's last station (where the branch climbs out of
        /// its host) may stand this far off the host, the others VsSeatTolM.</summary>
        const float VsSeatEndTolM = 0.10f;
        /// <summary>The shortest ease a seat's or a junction's change is taken off over.</summary>
        const float VsFadeMinM = 40f, VsFadeMaxM = 150f;
        /// <summary>Two arms whose ends stand further apart than this at a node are not one chain.</summary>
        const float VsJoinTolM = 0.02f;

        /// <summary>The editor's A/B switch: off puts back the solve's
        /// profile exactly (no knots, the solved node heights); on runs the
        /// pass again. No re-solve either way.</summary>
        public static void ApplyVSmooth(CityMap map, bool on)
        {
            if (map == null) return;
            if (nodeYSolved != null && nodeYSolved.Length == map.nodes.Length)
                System.Array.Copy(nodeYSolved, map.nodeY, nodeYSolved.Length);
            foreach (var e in map.edges) { e.vS = null; e.vY = null; }
            VSmoothApplied = false;
            if (on) DrawnProfile(map);
            else VSmoothReport = "drawn profile OFF (the stations, straight)";
        }

        // ---- per-solve scratch ------------------------------------------
        struct CPt { public float u, y, hi, lo, rSag, rCrest; public byte hiCause, loCause; public int edge, st; }
        struct Win { public int k; public float u, A, L, Lwant; public byte cause; }
        struct Anchor { public float u, t, m, D; }
        struct Link { public int edge; public bool fwd; }
        /// <summary>Per edge station: the bounds on the drawn change (raise &gt; 0).</summary>
        static float[][] vsHi, vsLo;
        static byte[][] vsHiC, vsLoC;
        /// <summary>Per edge station: the seat's host and arc (-1: not seated).</summary>
        static int[][] vsHost;
        static float[][] vsHostS;
        /// <summary>A node's change and plan gradient of change, once the
        /// chain through it is drawn (its minor arms ease from it).</summary>
        static float[] vsNodeD;
        static Vector2[] vsNodeG;
        static bool[] vsNodeDone;

        /// <summary>THE PASS (see the class comment). Called once at the end of
        /// <see cref="Solve"/>; reads the solve's settled state.</summary>
        static void DrawnProfile(CityMap map)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int E = map.edges.Length, N = map.nodes.Length;
            if (nodeYSolved == null || nodeYSolved.Length != N) nodeYSolved = (float[])map.nodeY.Clone();
            VsCapped.Clear();
            if (!VSmoothOn) { VSmoothReport = "drawn profile OFF (PSX_CITY_VSMOOTH=0)"; return; }

            BuildBounds(map);

            // ---- the chains: through two-arm nodes and each junction's major pair
            var next = new int[E * 2];   // (edge, end) -> partner (edge * 2 + end) or -1; end 0 = a, 1 = b
            for (int i = 0; i < next.Length; i++) next[i] = -1;
            var majorOf = new int[N];    // the node's major pair's first (edge * 2 + end), or -1
            for (int n = 0; n < N; n++) majorOf[n] = -1;
            var arms = new List<(int key, Vector2 d, float y)>(8);
            for (int n = 0; n < N; n++)
            {
                arms.Clear();
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || e.length < 0.5f || e.stY == null || e.stY.Length < 2) continue;
                    bool atA = e.a == n;
                    arms.Add((ei * 2 + (atA ? 0 : 1), atA ? e.TangentAt(0f) : -e.TangentAt(e.length), atA ? e.stY[0] : e.stY[e.stY.Length - 1]));
                }
                int p = -1, q = -1;
                if (arms.Count == 2)
                {
                    if (Vector2.Dot(arms[0].d, arms[1].d) < 0.3f) { p = 0; q = 1; }
                }
                else if (arms.Count >= 3)
                {
                    float bestKey = float.MinValue;
                    for (int i = 0; i < arms.Count; i++)
                        for (int j = i + 1; j < arms.Count; j++)
                        {
                            if (Vector2.Dot(arms[i].d, arms[j].d) >= -0.7f) continue;
                            var P = map.edges[arms[i].key >> 1]; var Q = map.edges[arms[j].key >> 1];
                            float key = Mathf.Min(P.link ? P.cls - 0.5f : P.cls, Q.link ? Q.cls - 0.5f : Q.cls) * 100f + P.width + Q.width;
                            if (key > bestKey) { bestKey = key; p = i; q = j; }
                        }
                }
                if (p < 0 || Mathf.Abs(arms[p].y - arms[q].y) > VsJoinTolM) continue;
                next[arms[p].key] = arms[q].key;
                next[arms[q].key] = arms[p].key;
                if (arms.Count >= 3) majorOf[n] = arms[p].key;
            }

            var chains = new List<List<Link>>();
            var chainOf = new int[E];
            for (int i = 0; i < E; i++) chainOf[i] = -1;
            for (int start = 0; start < E; start++)
            {
                var e0 = map.edges[start];
                if (chainOf[start] >= 0 || e0.a == e0.b || e0.length < 0.5f || e0.stY == null || e0.stY.Length < 2) continue;
                // walk back from start's a end to the head of its chain
                int headEdge = start; bool headFwd = true;
                var seen = new HashSet<int> { start };
                while (true)
                {
                    int key = headEdge * 2 + (headFwd ? 0 : 1);   // the end we entered the edge by
                    int pk = next[key];
                    if (pk < 0) break;
                    int pe = pk >> 1;
                    if (!seen.Add(pe)) break;                      // a ring: start anywhere
                    headEdge = pe; headFwd = (pk & 1) == 1;       // arrived at pe's b end: it runs forward into us
                }
                var chain = new List<Link>();
                int ce = headEdge; bool cf = headFwd;
                while (true)
                {
                    if (chainOf[ce] >= 0) break;
                    chainOf[ce] = chains.Count;
                    chain.Add(new Link { edge = ce, fwd = cf });
                    int outKey = ce * 2 + (cf ? 1 : 0);
                    int nk = next[outKey];
                    if (nk < 0) break;
                    ce = nk >> 1; cf = (nk & 1) == 0;
                }
                chains.Add(chain);
            }

            // ---- order: a chain after the chains its seats and its ends' junctions read
            int C = chains.Count;
            var prio = new float[C];
            var deps = new List<int>[C];
            for (int c = 0; c < C; c++)
            {
                float best = 0f;
                var dl = new HashSet<int>();
                var ch = chains[c];
                foreach (var l in ch)
                {
                    var e = map.edges[l.edge];
                    best = Mathf.Max(best, (e.link ? e.cls - 0.5f : e.cls) * 100f + e.width);
                    var hs = vsHost[l.edge];
                    if (hs != null) foreach (int h in hs) if (h >= 0 && chainOf[h] >= 0 && chainOf[h] != c) dl.Add(chainOf[h]);
                }
                for (int end = 0; end < 2; end++)
                {
                    var l = end == 0 ? ch[0] : ch[ch.Count - 1];
                    var e = map.edges[l.edge];
                    int node = end == 0 ? (l.fwd ? e.a : e.b) : (l.fwd ? e.b : e.a);
                    int mk = majorOf[node];
                    if (mk >= 0 && chainOf[mk >> 1] >= 0 && chainOf[mk >> 1] != c) dl.Add(chainOf[mk >> 1]);
                }
                prio[c] = best;
                deps[c] = new List<int>(dl);
            }
            var done = new bool[C];
            var order = new List<int>(C);
            {
                var byPrio = new List<int>(C);
                for (int c = 0; c < C; c++) byPrio.Add(c);
                byPrio.Sort((x, y) => prio[y].CompareTo(prio[x]));
                var remaining = byPrio;
                while (remaining.Count > 0)
                {
                    var still = new List<int>(remaining.Count);
                    foreach (int c in remaining)
                    {
                        bool ready = true;
                        foreach (int d in deps[c]) if (!done[d]) { ready = false; break; }
                        if (ready) { done[c] = true; order.Add(c); } else still.Add(c);
                    }
                    if (still.Count == remaining.Count) { int c = still[0]; done[c] = true; order.Add(c); still.RemoveAt(0); }   // a cycle: the highest first
                    remaining = still;
                }
            }

            vsNodeD = new float[N]; vsNodeG = new Vector2[N]; vsNodeDone = new bool[N];
            int anchorsSeat = 0, anchorsNode = 0, shifts = 0;
            var pts = new List<CPt>(4096);
            var ws = new List<Win>(1024);
            var anchors = new List<Anchor>(256);
            var knotU = new List<float>(8192);
            var cut = new List<float>(4096);
            var chainWins = new int[C];
            var chainCapped = new List<VsFillet>[C];
            // per chain: the change it read at its two end nodes (NaN: a node nothing had moved)
            var usedEnd = new float[C * 2];
            long tPts = 0, tCurves = 0, tKnots = 0;
            var sw = new System.Diagnostics.Stopwatch();

            void DrawChain(int c)
            {
                var ch = chains[c];
                sw.Restart();
                // the chain's points: every station, in travel order, a node once
                pts.Clear();
                var edgeU0 = new float[ch.Count];
                float u0 = 0f;
                for (int li = 0; li < ch.Count; li++)
                {
                    var l = ch[li]; var e = map.edges[l.edge];
                    edgeU0[li] = u0;
                    int n = e.stS.Length;
                    float rSag = SagComfortR(VerticalMph(e)), rCrest = CrestR(e);
                    for (int k = 0; k < n; k++)
                    {
                        int i = l.fwd ? k : n - 1 - k;
                        float u = u0 + (l.fwd ? e.stS[i] : e.length - e.stS[i]);
                        var cp = new CPt
                        {
                            u = u, y = e.stY[i], hi = vsHi[l.edge][i], lo = vsLo[l.edge][i], hiCause = vsHiC[l.edge][i], loCause = vsLoC[l.edge][i],
                            rSag = rSag, rCrest = rCrest, edge = l.edge, st = i
                        };
                        if (k == 0 && li > 0)
                        {
                            // the node joint: one point, both arms' bounds and needs
                            var pj = pts[pts.Count - 1];
                            if (cp.hi < pj.hi) { pj.hi = cp.hi; pj.hiCause = cp.hiCause; }
                            if (cp.lo > pj.lo) { pj.lo = cp.lo; pj.loCause = cp.loCause; }
                            pj.rSag = Mathf.Max(pj.rSag, cp.rSag); pj.rCrest = Mathf.Max(pj.rCrest, cp.rCrest);
                            pts[pts.Count - 1] = pj;
                            continue;
                        }
                        if (pts.Count > 0 && u - pts[pts.Count - 1].u < 0.05f) continue;   // a station on a station
                        pts.Add(cp);
                    }
                    u0 += e.length;
                }
                int P = pts.Count;
                if (P < 2) return;
                float uEnd = pts[P - 1].u;

                // ---- the eases: a chain's end holds its node's change (a minor
                // arm takes its junction's, anything else nothing); a seated
                // station follows its host
                anchors.Clear();
                for (int end = 0; end < 2; end++)
                {
                    var l = end == 0 ? ch[0] : ch[ch.Count - 1];
                    var e = map.edges[l.edge];
                    int node = end == 0 ? (l.fwd ? e.a : e.b) : (l.fwd ? e.b : e.a);
                    var cp = pts[end == 0 ? 0 : P - 1];
                    float t = 0f, m = 0f;
                    usedEnd[c * 2 + end] = float.NaN;
                    if (vsNodeDone[node])
                    {
                        // the arm's plan direction along +u at this end
                        Vector2 away = e.a == node ? e.TangentAt(0f) : -e.TangentAt(e.length);
                        t = vsNodeD[node]; m = Vector2.Dot(vsNodeG[node], end == 0 ? away : -away);
                        usedEnd[c * 2 + end] = t;
                        if (Mathf.Abs(t) >= 0.002f || Mathf.Abs(m) >= 0.0002f) anchorsNode++;
                    }
                    anchors.Add(new Anchor { u = cp.u, t = t, m = m, D = FadeLen(t, m, cp.rSag) });
                }
                if (anchors[1].u - anchors[0].u < 0.05f) anchors.RemoveAt(1);
                for (int j = 1; j + 1 < P; j++)
                {
                    var cp = pts[j];
                    var hs = vsHost[cp.edge];
                    if (hs == null || hs[cp.st] < 0) continue;
                    var H = map.edges[hs[cp.st]];
                    if (H.vS == null) continue;
                    float hsAt = vsHostS[cp.edge][cp.st];
                    float t = H.YAt(hsAt) - H.StationYAt(hsAt);
                    var e = map.edges[cp.edge];
                    // the host's change along the branch's direction of travel in the chain
                    var li = LinkOf(ch, cp.edge);
                    Vector2 dir = e.TangentAt(e.stS[cp.st]) * (li.fwd ? 1f : -1f);
                    float sg = Vector2.Dot(dir, H.TangentAt(hsAt)) >= 0f ? 1f : -1f;
                    float a1 = Mathf.Clamp(hsAt + sg, 0f, H.length), a0 = Mathf.Clamp(hsAt - sg, 0f, H.length);
                    float m = Mathf.Abs(a1 - a0) > 0.1f ? ((H.YAt(a1) - H.StationYAt(a1)) - (H.YAt(a0) - H.StationYAt(a0))) / ((a1 - a0) * sg) : 0f;
                    if (Mathf.Abs(t) < 0.002f && Mathf.Abs(m) < 0.0002f) continue;
                    anchors.Add(new Anchor { u = cp.u, t = t, m = m, D = FadeLen(t, m, cp.rSag) });
                    anchorsSeat++;
                }
                anchors.Sort((x, y) => x.u.CompareTo(y.u));
                for (int i = anchors.Count - 1; i > 0; i--) if (anchors[i].u - anchors[i - 1].u < 1f) anchors.RemoveAt(i == anchors.Count - 1 && i > 1 ? i - 1 : i);
                bool easing = false;
                foreach (var a in anchors) if (Mathf.Abs(a.t) >= 0.002f || Mathf.Abs(a.m) >= 0.0002f) { easing = true; break; }
                if (!easing) anchors.Clear();
                var fAt = new float[P];
                for (int j = 0; j < P; j++) fAt[j] = EaseAt(anchors, pts[j].u);
                tPts += sw.ElapsedTicks; sw.Restart();

                // ---- THE SHIFT. A sag under a deck may not rise into the
                // clearance, and a crest on a deck or a bridge may not fall:
                // there the curve is made room for the other way - the V's
                // floor is dug (lowered) round the break, or the crest lifted,
                // by what the curve needs, on a plateau the curve's length and
                // eased back over tapers of at most 1% of grade (rounded like
                // every other break below). Never where that side is held too.
                float[] shift = null;
                for (int k = 1; k + 1 < P; k++)
                {
                    var pk = pts[k];
                    float h0 = pk.u - pts[k - 1].u, h1 = pts[k + 1].u - pk.u;
                    if (h0 < 0.05f || h1 < 0.05f) continue;
                    float A = (pts[k + 1].y - pk.y) / h1 - (pk.y - pts[k - 1].y) / h0;
                    bool sag = A > 0f;
                    byte why = sag ? pk.hiCause : pk.loCause;
                    if (Mathf.Abs(A) < 0.01f || !(why == VsCauseDeck || why == VsCauseStructure)) continue;
                    float want = Mathf.Min((sag ? pk.rSag : pk.rCrest) * Mathf.Abs(A), VsMaxLM);
                    float room = sag ? Mathf.Max(0f, pk.hi - fAt[k]) : Mathf.Max(0f, fAt[k] - pk.lo);
                    float need = Mathf.Abs(A) * want / 8f - room;
                    if (need < 0.01f) continue;
                    // the deepest shift (of a few, deepest first) that the stations
                    // over its plateau and tapers allow, and the curve it makes room for
                    float dl = 0f, half = 0f, tau = 0f;
                    for (int tri = 0; tri < VsShiftTries.Length; tri++)
                    {
                        float d0 = Mathf.Min(need, VsShiftMaxM * VsShiftTries[tri]);
                        float hf = 0.5f * Mathf.Min(want, 8f * (room + d0) / Mathf.Abs(A));
                        float tu = Mathf.Max(StationStep, d0 / VsShiftGrade);
                        float reach = hf + tu;
                        if (pk.u - reach < 0f || pk.u + reach > uEnd) continue;   // not past a chain's end
                        bool ok = true;
                        for (int j = k; ok && j >= 0 && pk.u - pts[j].u < reach; j--) ok = ShiftRoom(pts[j], sag) >= d0 * ShiftT(pk.u - pts[j].u, hf, tu) - 1e-4f;
                        for (int j = k + 1; ok && j < P && pts[j].u - pk.u < reach; j++) ok = ShiftRoom(pts[j], sag) >= d0 * ShiftT(pts[j].u - pk.u, hf, tu) - 1e-4f;
                        if (!ok) continue;
                        dl = d0; half = hf; tau = tu;
                        break;
                    }
                    if (dl < 0.01f) continue;
                    shift ??= new float[P];
                    float sgn = sag ? -1f : 1f;
                    for (int j = 0; j < P; j++)
                    {
                        float d = Mathf.Abs(pts[j].u - pk.u);
                        if (d >= half + tau) continue;
                        float v = sgn * dl * ShiftT(d, half, tau);
                        shift[j] = sag ? Mathf.Min(shift[j], v) : Mathf.Max(shift[j], v);
                    }
                    shifts++;
                }
                // the bounds on the curves are what the eases and the shift leave
                var yd = new float[P];
                var hiC = new float[P]; var loC = new float[P];
                for (int j = 0; j < P; j++)
                {
                    float sh = shift != null ? shift[j] : 0f;
                    yd[j] = pts[j].y + sh;
                    bool seat = vsHost[pts[j].edge] != null && vsHost[pts[j].edge][pts[j].st] >= 0;
                    float hi = pts[j].hi, lo = pts[j].lo;
                    if (sh < 0f && pts[j].loCause == VsCauseLower) lo = Mathf.Min(lo, -VsShiftMaxM);
                    hiC[j] = seat ? hi : Mathf.Max(0f, hi - fAt[j] - sh);
                    loC[j] = seat ? lo : Mathf.Min(0f, lo - fAt[j] - sh);
                    if (j == 0 || j == P - 1) { hiC[j] = 0f; loC[j] = 0f; }
                }

                // ---- the curves, sized to the need and shortened to the bounds
                ws.Clear();
                for (int k = 1; k + 1 < P; k++)
                {
                    float h0 = pts[k].u - pts[k - 1].u, h1 = pts[k + 1].u - pts[k].u;
                    if (h0 < 0.05f || h1 < 0.05f) continue;
                    float A = (yd[k + 1] - yd[k]) / h1 - (yd[k] - yd[k - 1]) / h0;
                    if (Mathf.Abs(A) < VsMinBreak) continue;
                    float r = A > 0f ? pts[k].rSag : pts[k].rCrest;
                    float want = Mathf.Min(r * Mathf.Abs(A), VsMaxLM);
                    float L = want; byte cause = want >= VsMaxLM ? VsCauseMaxL : (byte)0;
                    float geo = 2f * Mathf.Min(pts[k].u - pts[0].u, uEnd - pts[k].u);
                    if (geo < L) { L = geo; cause = VsCauseEnds; }
                    ws.Add(new Win { k = k, u = pts[k].u, A = A, L = L, Lwant = r * Mathf.Abs(A), cause = cause });
                }
                var pos = new float[P]; var neg = new float[P];
                var scale = new float[ws.Count];
                if (causeScratch.Length < ws.Count) causeScratch = new byte[ws.Count * 2];
                shrinkMaxL = 0f; foreach (var x in ws) shrinkMaxL = Mathf.Max(shrinkMaxL, x.L);
                for (int it = 0; it < 12; it++)
                {
                    System.Array.Clear(pos, 0, P); System.Array.Clear(neg, 0, P);
                    for (int w = 0; w < ws.Count; w++) Accumulate(pts, ws[w], pos, neg);
                    for (int w = 0; w < ws.Count; w++) scale[w] = 1f;
                    bool any = false;
                    for (int j = 0; j < P; j++)
                    {
                        float sum = pos[j] + neg[j];
                        if (sum > hiC[j] + 5e-4f && pos[j] > 1e-6f)
                        {
                            float f = Mathf.Clamp01((hiC[j] - neg[j]) / pos[j]) * 0.995f;
                            ShrinkCovering(pts, ws, scale, j, true, f, pts[j].hiCause);
                            any = true;
                        }
                        else if (sum < loC[j] - 5e-4f && neg[j] < -1e-6f)
                        {
                            float f = Mathf.Clamp01((loC[j] - pos[j]) / neg[j]) * 0.995f;
                            ShrinkCovering(pts, ws, scale, j, false, f, pts[j].loCause);
                            any = true;
                        }
                    }
                    if (!any) break;
                    for (int w = 0; w < ws.Count; w++)
                        if (scale[w] < 1f) { var x = ws[w]; x.L *= scale[w]; x.cause = causeScratch[w]; ws[w] = x; }
                    if (it == 11)
                        for (int w = 0; w < ws.Count; w++) if (scale[w] < 1f) { var x = ws[w]; x.L = 0f; ws[w] = x; }   // still over: no curve
                }
                var capList = chainCapped[c] ??= new List<VsFillet>();
                capList.Clear();
                for (int w = ws.Count - 1; w >= 0; w--)
                {
                    var x = ws[w];
                    if (x.L < 0.999f * x.Lwant) capList.Add(new VsFillet { edge = pts[x.k].edge, s = StationS(map, pts[x.k]), A = x.A, L = x.L, Lwant = x.Lwant, cause = x.cause == 0 ? VsCauseEnds : x.cause });
                    if (x.L < 2f * VsKnotMinM) ws.RemoveAt(w);
                }
                chainWins[c] = ws.Count;
                tCurves += sw.ElapsedTicks; sw.Restart();
                // windows by start, for the curvature of each piece and CurvesAt
                ws.Sort((x, y) => (x.u - 0.5f * x.L).CompareTo(y.u - 0.5f * y.L));
                wsMaxL = 0f; foreach (var x in ws) wsMaxL = Mathf.Max(wsMaxL, x.L);
                float DigAt(float u)
                {
                    if (shift == null) return 0f;
                    int lo = 0, hi = P - 2;
                    while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (pts[mid].u <= u) lo = mid; else hi = mid - 1; }
                    float du = pts[lo + 1].u - pts[lo].u;
                    float t = du > 1e-6f ? Mathf.Clamp01((u - pts[lo].u) / du) : 0f;
                    return Mathf.Lerp(shift[lo], shift[lo + 1], t);
                }
                if (ws.Count == 0 && anchors.Count == 0 && shift == null)
                {
                    foreach (var l in ch) { var e = map.edges[l.edge]; e.vS = null; e.vY = null; }
                    MarkNodes(map, ch, edgeU0, ws, anchors, null);
                    return;
                }

                // ---- the knots
                cut.Clear();
                for (int j = 0; j < P; j++) cut.Add(pts[j].u);
                foreach (var x in ws) { cut.Add(Mathf.Max(0f, x.u - 0.5f * x.L)); cut.Add(Mathf.Min(uEnd, x.u + 0.5f * x.L)); }
                foreach (var a in anchors) { cut.Add(a.u); cut.Add(Mathf.Max(0f, a.u - a.D)); cut.Add(Mathf.Min(uEnd, a.u + a.D)); }
                cut.Sort();
                knotU.Clear();
                knotU.Add(cut[0]);
                int pi = 0;
                for (int i = 1; i < cut.Count; i++)
                {
                    float a = knotU[knotU.Count - 1], b = cut[i];
                    if (b - a < 0.05f) continue;
                    float mid = 0.5f * (a + b), kap = KappaAt(ws, mid);
                    float fpp = 0f;
                    if (anchors.Count > 0)
                    {
                        float hh = Mathf.Min(0.5f, 0.25f * (b - a));
                        for (int zi = 0; zi < 3; zi++)
                        {
                            float z = zi == 0 ? a + hh : zi == 1 ? mid : b - hh;
                            fpp = Mathf.Max(fpp, Mathf.Abs(EaseAt(anchors, z + hh) - 2f * EaseAt(anchors, z) + EaseAt(anchors, z - hh)) / (hh * hh));
                        }
                    }
                    // the road's class sets how fine: a quarter of a degree of
                    // grade from chord to chord on the fast roads, twice that on
                    // a local street
                    while (pi + 1 < P && pts[pi + 1].u <= mid) pi++;
                    var pe = map.edges[pts[pi].edge];
                    float theta = pe.link || pe.cls >= 2 ? VsKnotTheta : 2f * VsKnotTheta;
                    float len = b - a;
                    int nPieces = Mathf.Clamp(Mathf.CeilToInt((Mathf.Abs(kap) + fpp) * len / theta), 1, Mathf.Max(1, Mathf.FloorToInt(len / VsKnotMinM)));
                    for (int q = 1; q < nPieces; q++) knotU.Add(a + len * q / nPieces);
                    knotU.Add(b);
                }

                // ---- the drawn heights, per edge
                for (int li = 0; li < ch.Count; li++)
                {
                    var l = ch[li]; var e = map.edges[l.edge];
                    float ua = edgeU0[li], ub = ua + e.length;
                    int i0 = LowerBound(knotU, ua - 0.01f), i1 = LowerBound(knotU, ub + 0.01f);
                    var sl = new List<float>(Mathf.Max(2, i1 - i0 + 2));
                    sl.Add(0f);
                    for (int i = i0; i < i1; i++)
                    {
                        float s = l.fwd ? knotU[i] - ua : ub - knotU[i];
                        if (s > 0.05f && s < e.length - 0.05f) sl.Add(s);
                    }
                    sl.Add(e.length);
                    sl.Sort();
                    var vy = new float[sl.Count];
                    float worst = 0f;
                    for (int i = 0; i < sl.Count; i++)
                    {
                        float u = l.fwd ? ua + sl[i] : ub - sl[i];
                        float d = DigAt(u) + EaseAt(anchors, u) + CurvesAt(ws, u);
                        vy[i] = e.StationYAt(sl[i]) + d;
                        worst = Mathf.Max(worst, Mathf.Abs(d));
                    }
                    if (worst < 0.001f) { e.vS = null; e.vY = null; continue; }
                    e.vS = sl.ToArray(); e.vY = vy;
                }
                MarkNodes(map, ch, edgeU0, ws, anchors, shift != null ? (System.Func<float, float>)DigAt : null);
                tKnots += sw.ElapsedTicks;
                if (vsDumpEdges.Count > 0)
                {
                    bool want = false;
                    foreach (var l in ch) if (vsDumpEdges.Contains(l.edge)) want = true;
                    if (want)
                    {
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        VsDump.Append("CHAIN ").Append(c).Append(": ");
                        foreach (var l in ch) VsDump.Append('e').Append(l.edge).Append(l.fwd ? "+ " : "- ");
                        VsDump.AppendLine();
                        for (int j = 0; j < P; j++)
                        {
                            var q = pts[j];
                            bool st = vsHost[q.edge] != null && vsHost[q.edge][q.st] >= 0;
                            VsDump.AppendLine(string.Format(inv, "  pt u {0,8:0.00} e{1} st {2} s {3:0.00} y {4:0.000} yd {5:0.000} F {6:+0.000;-0.000} hi {7:0.00}({8}) lo {9:0.00}({10}) hiC {11:0.000} loC {12:0.000}{13}",
                                q.u, q.edge, q.st, map.edges[q.edge].stS[q.st], q.y, yd[j], fAt[j], q.hi, q.hiCause, q.lo, q.loCause, hiC[j], loC[j], st ? $" SEAT on e{vsHost[q.edge][q.st]}@{vsHostS[q.edge][q.st]:0.0}" : ""));
                        }
                        foreach (var a in anchors) VsDump.AppendLine(string.Format(inv, "  anchor u {0:0.00} t {1:+0.000;-0.000} m {2:+0.0000;-0.0000} D {3:0.0}", a.u, a.t, a.m, a.D));
                        foreach (var x in ws) VsDump.AppendLine(string.Format(inv, "  curve u {0:0.00} A {1:+0.00;-0.00}% L {2:0.0} of {3:0.0} cause {4}", x.u, x.A * 100f, x.L, x.Lwant, x.cause));
                        foreach (var l in ch)
                        {
                            if (!vsDumpEdges.Contains(l.edge)) continue;
                            var e = map.edges[l.edge];
                            VsDump.Append("  drawn e").Append(l.edge).Append(':');
                            if (e.vS != null) for (int i = 0; i < e.vS.Length; i++) VsDump.Append(string.Format(inv, " {0:0.0}:{1:0.000}", e.vS[i], e.vY[i] - e.StationYAt(e.vS[i])));
                            VsDump.AppendLine();
                        }
                    }
                }
            }

            foreach (int c in order) DrawChain(c);
            // A chain drawn before the junction it ends at was moved (the order
            // has cycles: two streets each the major road of the other's end)
            // is drawn again with the junction's change, until nothing is stale.
            int redrawn = 0, rounds = 0;
            for (int round = 0; round < 3; round++)
            {
                var stale = new List<int>();
                foreach (int c in order)
                {
                    var ch = chains[c];
                    for (int end = 0; end < 2; end++)
                    {
                        var l = end == 0 ? ch[0] : ch[ch.Count - 1];
                        var e = map.edges[l.edge];
                        int node = end == 0 ? (l.fwd ? e.a : e.b) : (l.fwd ? e.b : e.a);
                        float now = vsNodeDone[node] ? vsNodeD[node] : 0f, used = float.IsNaN(usedEnd[c * 2 + end]) ? 0f : usedEnd[c * 2 + end];
                        if (Mathf.Abs(now - used) > 0.003f) { stale.Add(c); break; }
                    }
                }
                if (stale.Count == 0) break;
                rounds++;
                foreach (int c in stale) DrawChain(c);
                redrawn += stale.Count;
            }

            // the census of what was drawn
            int edgesDrawn = 0, knots = 0, wins = 0, endMismatch = 0;
            float maxRaise = 0f, maxLower = 0f;
            for (int c = 0; c < C; c++) { wins += chainWins[c]; if (chainCapped[c] != null) VsCapped.AddRange(chainCapped[c]); }
            foreach (var e in map.edges)
            {
                if (e.vS != null)
                {
                    edgesDrawn++; knots += e.vS.Length;
                    for (int i = 0; i < e.vS.Length; i++)
                    {
                        float d = e.vY[i] - e.StationYAt(e.vS[i]);
                        if (d > maxRaise) maxRaise = d;
                        if (-d > maxLower) maxLower = -d;
                    }
                }
                if (e.a == e.b || e.stY == null || e.stY.Length < 2) continue;
                // each end against its node: the fan's centre and the ribbon's end must agree
                for (int end = 0; end < 2; end++)
                {
                    int node = end == 0 ? e.a : e.b;
                    float s = end == 0 ? 0f : e.length;
                    float dEnd = e.YAt(s) - e.StationYAt(s), dNode = map.nodeY[node] - nodeYSolved[node];
                    if (Mathf.Abs(dEnd - dNode) > 0.01f) endMismatch++;
                }
            }
            VSmoothApplied = true;
            VSmoothMs = (float)clock.Elapsed.TotalMilliseconds;
            double tk = System.Diagnostics.Stopwatch.Frequency / 1000.0;
            VSmoothReport = $"drawn profile: {chains.Count} chains, {wins} vertical curves ({VsCapped.Count} short of their road's radius), {shifts} floors dug / crests lifted to make room, {anchorsSeat} seat + {anchorsNode} junction eases, {redrawn} chains redrawn in {rounds} rounds, {edgesDrawn} edges drawn with {knots} knots, raised up to {maxRaise:0.00} m / lowered up to {maxLower:0.00} m, {endMismatch} edge ends off their node, {VSmoothMs:0} ms (points {tPts / tk:0}, curves {tCurves / tk:0}, knots {tKnots / tk:0})";
            vsHi = vsLo = null; vsHiC = vsLoC = null; vsHost = null; vsHostS = null;
        }

        /// <summary>How far a station may be shifted the other way to make a
        /// curve room: down for a dug sag (on open ground, as deep as the
        /// shift allows), up for a lifted crest.</summary>
        static float ShiftRoom(CPt p, bool sag)
        {
            if (sag) return p.loCause == VsCauseLower ? VsShiftMaxM : Mathf.Max(0f, -p.lo);
            return Mathf.Max(0f, p.hi);
        }
        /// <summary>The shift's shape: 1 on the plateau (the curve's half
        /// length), easing to 0 over the taper.</summary>
        static float ShiftT(float d, float half, float tau) => d <= half ? 1f : d >= half + tau ? 1e-6f : 1f - (d - half) / tau;

        static byte[] causeScratch = new byte[0];

        static Link LinkOf(List<Link> ch, int edge)
        {
            foreach (var l in ch) if (l.edge == edge) return l;
            return ch[0];
        }

        static float StationS(CityMap map, CPt p) => map.edges[p.edge].stS[p.st];

        static int LowerBound(List<float> a, float v)
        {
            int lo = 0, hi = a.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (a[mid] < v) lo = mid + 1; else hi = mid; }
            return lo;
        }

        /// <summary>How long a change of t (and grade m) is eased off over: at
        /// least <see cref="VsFadeMinM"/>, long enough that the ease's own
        /// curvature stays inside radius r.</summary>
        static float FadeLen(float t, float m, float r) =>
            Mathf.Clamp(Mathf.Max(VsFadeMinM, Mathf.Sqrt(6f * Mathf.Abs(t) * r), 4f * Mathf.Abs(m) * r), VsFadeMinM, VsFadeMaxM);

        /// <summary>The eases at u: a cubic Hermite through the anchors'
        /// values and grades where they are close, each eased to nothing
        /// (value and grade) over its own length where they are not. C1.</summary>
        static float EaseAt(List<Anchor> an, float u)
        {
            int n = an.Count;
            if (n == 0) return 0f;
            int lo = 0, hi = n;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (an[mid].u <= u) lo = mid + 1; else hi = mid; }
            int ip = lo - 1, inx = lo;   // last anchor at or before u, first after
            if (ip >= 0 && inx < n)
            {
                var a = an[ip]; var b = an[inx];
                float gap = b.u - a.u;
                if (gap <= a.D + b.D)
                {
                    float x = (u - a.u) / gap, x2 = x * x, x3 = x2 * x;
                    return a.t * (2f * x3 - 3f * x2 + 1f) + a.m * gap * (x3 - 2f * x2 + x) + b.t * (-2f * x3 + 3f * x2) + b.m * gap * (x3 - x2);
                }
                return Fade(a, u - a.u, 1f) + Fade(b, b.u - u, -1f);
            }
            if (ip >= 0) return Fade(an[ip], u - an[ip].u, 1f);
            return Fade(an[inx], an[inx].u - u, -1f);
        }

        static float Fade(Anchor a, float x, float sg)
        {
            if (x >= a.D || x < 0f) return x < 0f ? a.t : 0f;
            float X = x / a.D, X2 = X * X, X3 = X2 * X;
            return a.t * (1f - 3f * X2 + 2f * X3) + sg * a.m * a.D * (X - 2f * X2 + X3);
        }

        /// <summary>The curvature of the vertical curves at u (the windows
        /// sorted by start, as for CurvesAt).</summary>
        static float KappaAt(List<Win> ws, float u)
        {
            int lo = 0, hi = ws.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (ws[mid].u - 0.5f * ws[mid].L <= u) lo = mid + 1; else hi = mid; }
            float k = 0f;
            for (int w = lo - 1; w >= 0; w--)
            {
                var x = ws[w];
                if (x.u - 0.5f * x.L < u - wsMaxL) break;
                if (Mathf.Abs(u - x.u) < 0.5f * x.L) k += x.A / x.L;
            }
            return k;
        }

        /// <summary>The longest window of the chain being drawn (CurvesAt's reach).</summary>
        static float wsMaxL;
        /// <summary>The sum of the vertical curves at u (the windows sorted by
        /// their start: only those starting within the longest window's
        /// length before u can cover it).</summary>
        static float CurvesAt(List<Win> ws, float u)
        {
            int lo = 0, hi = ws.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (ws[mid].u - 0.5f * ws[mid].L <= u) lo = mid + 1; else hi = mid; }
            float sum = 0f;
            for (int w = lo - 1; w >= 0; w--)
            {
                var x = ws[w];
                float h = 0.5f * x.L;
                if (x.u - h < u - wsMaxL) break;
                float d = Mathf.Abs(u - x.u);
                if (d >= h) continue;
                sum += x.A / (2f * x.L) * (h - d) * (h - d);
            }
            return sum;
        }

        static void Accumulate(List<CPt> pts, Win x, float[] pos, float[] neg)
        {
            if (x.L <= 0f) return;
            float h = 0.5f * x.L;
            for (int dir = -1; dir <= 1; dir += 2)
                for (int j = dir < 0 ? x.k : x.k + 1; j >= 0 && j < pts.Count; j += dir)
                {
                    float d = Mathf.Abs(pts[j].u - x.u);
                    if (d >= h) break;
                    float c = x.A / (2f * x.L) * (h - d) * (h - d);
                    if (c > 0f) pos[j] += c; else neg[j] += c;
                }
        }

        /// <summary>Shorten (to f of their length, at most) the windows of
        /// one sign that cover point j. The windows are in order of their
        /// centres (made station by station), none longer than shrinkMaxL.</summary>
        static void ShrinkCovering(List<CPt> pts, List<Win> ws, float[] scale, int j, bool positive, float f, byte cause)
        {
            float u = pts[j].u;
            int lo = 0, hi = ws.Count;
            float from = u - 0.5f * shrinkMaxL;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (ws[mid].u < from) lo = mid + 1; else hi = mid; }
            for (int w = lo; w < ws.Count; w++)
            {
                var x = ws[w];
                if (x.u > u + 0.5f * shrinkMaxL) break;
                float d = Mathf.Abs(u - x.u);
                if ((x.A > 0f) != positive || x.L <= 0f || d >= 0.5f * x.L) continue;
                // shortened by f its share there is at most f of what it was;
                // shortened to end short of the point, it has none: the longer
                // of the two (a curve beside a deck need only stop before it)
                float fw = Mathf.Max(f, 0.999f * 2f * d / x.L);
                if (fw < scale[w]) { scale[w] = fw; causeScratch[w] = cause; }
            }
        }
        static float shrinkMaxL;

        /// <summary>Each node the chain passes through takes the drawn change
        /// there (both arms' ends and the fan's centre agree); a junction
        /// records it and its plan gradient for its minor arms, which ease
        /// from it. A chain's own ends keep their node as it is (or as the
        /// major chain through it left it).</summary>
        static void MarkNodes(CityMap map, List<Link> ch, float[] edgeU0, List<Win> ws, List<Anchor> anchors, System.Func<float, float> dig)
        {
            float D(float u) => (dig != null ? dig(u) : 0f) + EaseAt(anchors, u) + (ws != null ? CurvesAt(ws, u) : 0f);
            for (int li = 1; li < ch.Count; li++)
            {
                var l = ch[li];
                var e = map.edges[l.edge];
                int node = l.fwd ? e.a : e.b;
                float u = edgeU0[li];
                float d = D(u);
                map.nodeY[node] = nodeYSolved[node] + d;
                float d1 = D(u + 0.5f), d0 = D(u - 0.5f);
                var prev = map.edges[ch[li - 1].edge];
                Vector2 dirIn = ch[li - 1].fwd ? prev.TangentAt(prev.length) : -prev.TangentAt(0f);
                Vector2 dirOut = l.fwd ? e.TangentAt(0f) : -e.TangentAt(e.length);
                Vector2 dir = dirIn + dirOut; dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : dirOut;
                vsNodeD[node] = d; vsNodeG[node] = dir * (d1 - d0); vsNodeDone[node] = true;
            }
        }

        /// <summary>Every station's bounds on the drawn change, and which
        /// stations follow a host (see the class comment).</summary>
        static void BuildBounds(CityMap map)
        {
            int E = map.edges.Length;
            vsHi = new float[E][]; vsLo = new float[E][]; vsHiC = new byte[E][]; vsLoC = new byte[E][];
            vsHost = new int[E][]; vsHostS = new float[E][];
            foreach (var e in map.edges)
            {
                int n = e.stS != null ? e.stS.Length : 0;
                var hi = vsHi[e.index] = new float[n]; var lo = vsLo[e.index] = new float[n];
                var hc = vsHiC[e.index] = new byte[n]; var lc = vsLoC[e.index] = new byte[n];
                for (int i = 0; i < n; i++)
                {
                    hi[i] = e.link || e.cls >= 4 ? VsRaiseMaxM : VsRaiseStreetM; hc[i] = VsCauseRaise;
                    bool st = e.stElev != null && e.stElev[i];
                    lo[i] = st ? -VsLowerStructM : -VsLowerMaxM; lc[i] = st ? VsCauseStructure : VsCauseLower;
                }
            }
            void LowerCap(CityMap.Edge e, float s0, float s1, float cap, byte cause)
            {
                for (int i = 0; i < e.stS.Length; i++)
                    if (e.stS[i] >= s0 && e.stS[i] <= s1 && vsLo[e.index][i] < -cap) { vsLo[e.index][i] = -cap; vsLoC[e.index][i] = cause; }
            }
            if (map.wspans != null)
                foreach (var w in map.wspans) { var e = map.edges[w.edge]; LowerCap(e, w.s0 - StationStep, w.s1 + StationStep, VsLowerStructM, VsCauseStructure); }
            if (map.creekCulverts != null)
                foreach (var x in map.creekCulverts)
                    if (x.edge >= 0 && x.edge < E) { var e = map.edges[x.edge]; LowerCap(e, x.s - x.halfAlong - StationStep, x.s + x.halfAlong + StationStep, VsLowerStructM, VsCauseStructure); }

            // the crossings: the road below rises only into the clearance's
            // spare; the deck over it falls 3 cm at most, and only out of it
            var on = crossingOn;
            for (int ci = 0; ci < map.crossings.Length; ci++)
            {
                if (on != null && ci < on.Length && !on[ci]) continue;
                var c = map.crossings[ci];
                var over = map.edges[c.over]; var under = map.edges[c.under];
                if (over.stS == null || under.stS == null) continue;
                ProjectOn(over, c.at, out float sO);
                ProjectOn(under, c.at, out float sU);
                float cos = Vector2.Dot(over.TangentAt(sO), under.TangentAt(sU));
                float sin = Mathf.Max(0.4f, Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos)));
                float hwO = over.HalfMax, hwU = under.HalfMax;
                float acrossO = (hwU + 1f) / sin, acrossU = (hwO + 1f) / sin;
                float soffit = float.MaxValue, underTop = float.MinValue;
                for (int k = -2; k <= 2; k++)
                {
                    soffit = Mathf.Min(soffit, over.StationYAt(sO + k * 0.5f * acrossO) - DeckThick);
                    underTop = Mathf.Max(underTop, under.StationYAt(sU + k * 0.5f * acrossU));
                }
                float spare = soffit - underTop - VsClearFloorM;
                float overShare = Mathf.Clamp(0.25f * spare, 0f, VsLowerStructM);
                float footU = (hwO + hwU * Mathf.Abs(cos)) / sin + 1f + StationStep;
                for (int i = 0; i < under.stS.Length; i++)
                {
                    if (Mathf.Abs(under.stS[i] - sU) > footU) continue;
                    float avail = Mathf.Max(0f, soffit - under.stY[i] - VsClearFloorM - overShare);
                    if (avail < vsHi[under.index][i]) { vsHi[under.index][i] = avail; vsHiC[under.index][i] = VsCauseDeck; }
                }
                float footO = (hwU + hwO * Mathf.Abs(cos)) / sin + 1f + StationStep;
                for (int i = 0; i < over.stS.Length; i++)
                {
                    if (Mathf.Abs(over.stS[i] - sO) > footO) continue;
                    float avail = Mathf.Clamp(over.stY[i] - DeckThick - underTop - VsClearFloorM, 0f, overShare);
                    if (-avail > vsLo[over.index][i]) { vsLo[over.index][i] = -avail; vsLoC[over.index][i] = VsCauseDeck; }
                }
            }

            // the seats: a ramp's (stSeat) and a street branch's (soft seats)
            void Seat(int ei, int st, int host, float hostS)
            {
                if (host < 0 || ei < 0 || ei >= E || host == ei) return;
                var e = map.edges[ei];
                if (st < 0 || st >= e.stS.Length) return;
                var H = map.edges[host];
                if ((e.PointAt(e.stS[st]) - H.PointAt(hostS)).sqrMagnitude > 30f * 30f) return;
                if (vsHost[ei] == null) { vsHost[ei] = new int[e.stS.Length]; vsHostS[ei] = new float[e.stS.Length]; for (int i = 0; i < e.stS.Length; i++) vsHost[ei][i] = -1; }
                vsHost[ei][st] = host; vsHostS[ei][st] = hostS;
            }
            // The seat lists were made before UnionAbutments inserted stations,
            // so their station indices (and the host arcs that go with them)
            // can be one station out on an edge that gained one: every seated
            // station finds its host again by projection, from the hosts its
            // edge was seated on.
            var hostsOf = new Dictionary<int, List<int>>();
            void Cand(int ei, int h)
            {
                if (h < 0 || ei < 0 || ei >= E || h == ei) return;
                if (!hostsOf.TryGetValue(ei, out var l)) hostsOf[ei] = l = new List<int>(2);
                if (!l.Contains(h)) l.Add(h);
            }
            int HostByProjection(int ei, int i, out float hostS)
            {
                hostS = 0f;
                if (!hostsOf.TryGetValue(ei, out var l)) return -1;
                var e = map.edges[ei];
                var pnt = e.PointAt(e.stS[i]);
                int best = -1; float bestD = float.MaxValue;
                foreach (int h in l)
                {
                    var H = map.edges[h];
                    ProjectOn(H, pnt, out float hs);
                    float d = (H.PointAt(hs) - pnt).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = h; hostS = hs; }
                }
                return best;
            }
            foreach (var (ei, st, h, hs) in seated) Cand(ei, h);
            foreach (var kv in hostsOf)
            {
                var e = map.edges[kv.Key];
                for (int i = 0; i < e.stS.Length; i++)
                {
                    if (!e.SeatedAt(i)) continue;
                    int h = HostByProjection(kv.Key, i, out float hs);
                    if (h >= 0) Seat(kv.Key, i, h, hs);
                }
            }
            hostsOf.Clear();
            foreach (var (ei, st, h, hs) in streetSeats) Cand(ei, h);
            foreach (var (ei, st, h, hs) in streetSeats)
            {
                if (ei < 0 || ei >= E || st < 0 || st >= map.edges[ei].stS.Length) continue;
                int hh = HostByProjection(ei, st, out float hsp);
                if (hh >= 0) Seat(ei, st, hh, hsp);
            }
            foreach (var e in map.edges)
            {
                var hs = vsHost[e.index];
                if (hs == null) continue;
                for (int i = 0; i < hs.Length; i++)
                {
                    if (hs[i] < 0) continue;
                    bool runEnd = i > 0 && i + 1 < hs.Length && (hs[i - 1] < 0 || hs[i + 1] < 0);
                    float tol = runEnd ? VsSeatEndTolM : VsSeatTolM;
                    if (tol < vsHi[e.index][i]) { vsHi[e.index][i] = tol; vsHiC[e.index][i] = VsCauseSeat; }
                    if (-tol > vsLo[e.index][i]) { vsLo[e.index][i] = -tol; vsLoC[e.index][i] = VsCauseSeat; }
                }
            }
        }
    }
}
