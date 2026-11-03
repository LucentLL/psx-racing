using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE FREEWAY DIVIDER TYPE (2026-11-02, the owner: "a lot of roads that don't have
    /// medians in real life (like 485), they often use other types of dividers" - his photos
    /// at I-485's Wildlife Rd overpass: a wide grass median, a high-tension CABLE barrier
    /// (four cables on posts) along it, and double-faced W-beam around the overpass piers;
    /// no concrete wall).
    ///
    /// Every grounded freeway carriageway (<see cref="Barriered"/>) stood a Jersey on its
    /// median edge, whatever the median's width: on I-485 and NC 16 two concrete walls faced
    /// each other across grass, and up to 9.5 m apart GroundTwinRuns paved the whole median
    /// flush and stood one Jersey on it. The divider is now decided per CORRIDOR RUN - one
    /// carriageway of one named road between interchanges (nodes a ramp joins), never per
    /// section - from the run's median width (the median of its samples), and then per RUN
    /// PAIR: a run keeps a concrete or open kind only where the runs across the median have
    /// it too, otherwise both sides are cable. So it changes only at a run boundary:
    ///
    ///   CONCRETE  a pair of narrow runs (and every corridor in <see cref="ConcreteCorridors"/>,
    ///             whose medians are concrete in the real road: I-77, I-85, I-277, US 74):
    ///             exactly as before - the shared Jersey, the union strip, two walls.
    ///   CABLE     a grass median: the median verge grades to the ditch like any roadside,
    ///             and ONE high-tension four-cable run on posts stands <see cref="CableOffM"/>
    ///             off the median's centre toward the carriageway that carries it, continuous
    ///             through the run, turned down to an anchor only at its real ends. It is drawn
    ///             per edge from the classification's own intervals (never from a span's
    ///             flags, which flicker), on through the junction fans at the nodes. Its
    ///             collider is one closed box strip 0.5 m thick.
    ///   OPEN      a pair of runs both wider than <see cref="DivOpenM"/> over at least
    ///             <see cref="DivOpenMinRunM"/>: grass, no barrier (NCDOT runs the cable all
    ///             round I-485, so a bare stretch must be truly wide and long).
    ///
    /// Who carries the cable is decided at each DivStepM (20 m) sample: a cable side
    /// opposite a concrete one draws nothing (the Jersey is the barrier), opposite an open
    /// one or no plan it draws, and between two cable sides ONE side of the corridor does -
    /// the chains coloured by direction, side 0 (the lowest-key chain's) all along it. Where
    /// the twin is on structure the cable is cut exactly opposite it (the parapet is the
    /// barrier). A NARROW PINCH (gap under <see cref="CableMinGapM"/>) is concrete: a Jersey
    /// on the median edge through it, the cable lapped behind its ends. The plan is made
    /// ONCE per map and solve state (<see cref="PlanDividers"/>), never in the seat pass.
    ///
    /// Where a road on structure crosses over a cable or open median (an overpass: its piers
    /// stand in the median) each carriageway gets a W-beam <see cref="BeamOffM"/> off its
    /// median edge (between the traffic and any pier PierClearFor allows) over the crossing
    /// and its length of need, flared away and turned down at both ends (WBeamFlareM /
    /// WBeamTermM): the double barrier around the piers in the owner's photo. The cable laps
    /// in behind the beam: it runs on to the beam's full section and is turned down there.
    /// <see cref="Barriered"/> itself is untouched (the elevation solve and the twin hold read
    /// it). At-grade class-4 trunks are out of scope: Concrete, as before.
    /// PSX_CITY_DIVIDERS=0 restores the before exactly.
    /// </summary>
    public static partial class CityMeshes
    {
        public static bool DividersOn = System.Environment.GetEnvironmentVariable("PSX_CITY_DIVIDERS") != "0";
        public enum DividerKind : byte { Concrete, Cable, Open }

        /// <summary>Corridors whose real median is concrete wall (the 2026-11-02 design
        /// review's owner-set table: OSM tags 14 km of Jersey in I-77's median, 10.8 km in the
        /// US 74 expressway's, 4 km in I-85's, and none at all on I-485 or NC 16).</summary>
        static readonly HashSet<string> ConcreteCorridors = new HashSet<string>
        {
            "I-77", "I-85", "I-277", "Brookshire Freeway", "John Belk Freeway",
            "US 74", "East Independence Expressway", "Independence Boulevard", "East Independence Boulevard", "B. Bayles Mack Highway",
        };

        /// <summary>The width rule (metres of unpaved median between the two paved edges,
        /// LineModel extents). Concrete below <see cref="DivNarrowLeaveM"/>, back to cable only
        /// above <see cref="DivNarrowEnterM"/>; a run shorter than <see cref="DivMinRunM"/> keeps
        /// the kind before it (never open). Open takes no hysteresis: a run is open only on its
        /// own width and length.</summary>
        const float DivStepM = 20f, DivReachM = 45f, DivNarrowLeaveM = 5f, DivNarrowEnterM = 7f,
                    DivOpenM = 30f, DivOpenMinRunM = 1000f, DivMinRunM = 400f;
        /// <summary>The cable: off the median's centre toward its carriageway, posts every
        /// CablePostM, four cables (<see cref="CableH"/>), turned down to the anchor over
        /// CableTermM at each real end. A cable piece shorter than CableMinRunM between two
        /// real ends is not built (the short-piece rule of the Jerseys).</summary>
        public const float CableOffM = 1.2f, CablePostM = 4f, CableTermM = 8f, CablePostTopM = 1.02f,
                           CableColliderHalfM = 0.25f, CableColliderTopM = 0.95f, CableMinGapM = 3f,
                           CableDrawM = 0.1f, CablePostHalfM = 0.12f, CableMinRunM = 30f;
        static readonly float[] CableH = { 0.5f, 0.65f, 0.8f, 0.95f };
        /// <summary>The delineator heads' texels: a few pixels inside one yellow stripe of the
        /// OM-3 cell (tools/city/structure_atlas.py: x 19.5-21.5, rows 467-469, x + y on a
        /// yellow band's middle). Round 2 mapped the whole yellow-and-black cell and the heads
        /// read olive brown.</summary>
        const float DelinU0 = 0.038f, DelinU1 = 0.042f, DelinV0 = 0.083f, DelinV1 = 0.087f;
        /// <summary>A crossing deck's W-beam: its length of need past the deck's edge each way,
        /// and its front's offset off the median edge (a median pier stands at least
        /// PierClearM 1.5 m off the pavement, so the beam is always in front of it).</summary>
        const float BeamNeedM = 8f, BeamOffM = 0.6f, BeamCableClearM = 0.75f, DivDeckLapM = 4f;
        /// <summary>The ground under the cable is solved every CableGroundEvery posts and
        /// interpolated between (the verge it stands on is linear between sections anyway).</summary>
        const int CableGroundEvery = 3;

        struct CableIv
        {
            public float s0, s1;
            /// <summary>Metres past s0 (s1) along the chain to the cable's real end: 0 = it
            /// ends here, turned down to its anchor; large = it carries on.</summary>
            public float extA, extB;
            public bool contA, contB;   // carries on into the next edge at that node
        }

        class DivEdge
        {
            public DividerKind kind;
            public int chain = -1, run = -1;
            public float[] gap;         // per DivStepM sample, NaN where no twin was found
            public int[] twin;          // the opposing carriageway at each sample, -1 none
            public float[] twinS;       // its arc there
            public bool[] draw;         // this side carries the pair's cable at that sample
            public List<Vector2> zones; // crossing decks over the median: core (s0, s1)
            public List<CableIv> ivs;   // the cable as built: arcs on this edge
            public List<Vector2> laps;      // where the cable laps behind a twin deck's parapet (DivDeckLapM past its projected end)
            public List<Vector2> dropped;   // cable pieces shorter than CableMinRunM, not built (the gate names them)
            public List<Vector2> pinchRun, pinchCore;   // a narrow pinch's Jersey run and the cable's cut in it
        }

        /// <summary>Arcs of e over the samples marked true (sample k is [k, k + 1] DivStepM).</summary>
        static List<Vector2> DivArcs(CityMap.Edge e, bool[] m)
        {
            List<Vector2> r = null;
            for (int k = 0; k < m.Length;)
            {
                if (!m[k]) { k++; continue; }
                int j = k; while (j < m.Length && m[j]) j++;
                (r ??= new List<Vector2>()).Add(new Vector2(k * DivStepM, j >= m.Length ? e.length : Mathf.Min(e.length, j * DivStepM)));
                k = j;
            }
            return r;
        }

        static bool InArcs(List<Vector2> arcs, float s)
        {
            if (arcs == null) return false;
            foreach (var a in arcs) if (s >= a.x - 1e-3f && s <= a.y + 1e-3f) return true;
            return false;
        }

        /// <summary>Is arc s of e in a narrow pinch's Jersey run (a cable edge whose median is
        /// concrete there, CityMeshes' span flags and the shared-barrier test read it)?</summary>
        static bool DivPinchAt(CityMap.Edge e, float s) => InArcs(DivOf(e)?.pinchRun, s);
        static bool DivPinchSpan(CityMap.Edge e, float s0, float s1)
        {
            var r = DivOf(e)?.pinchRun;
            if (r == null) return false;
            foreach (var a in r) if (s1 > a.x && s0 < a.y) return true;
            return false;
        }

        /// <summary>The structure arcs of any edge (ElevatedAt's own rule, unpadded), cached
        /// for one plan build.</summary>
        static readonly Dictionary<int, List<Vector2>> divElevCache = new Dictionary<int, List<Vector2>>();
        static List<Vector2> DivElevatedOf(CityMap.Edge e)
        {
            if (!divElevCache.TryGetValue(e.index, out var l)) divElevCache[e.index] = l = DivElevated(e, 0f);
            return l;
        }

        /// <summary>Two colours over a corridor's chains by direction: chains paired as twins
        /// over more than two samples take opposite colours, the lowest-key chain of each
        /// connected group colour 0.</summary>
        static int[] DivChainColours(CityMap.Edge[] E, List<List<CityMap.Edge>> chains, int[] chainKey)
        {
            int nc = chains.Count;
            var w = new Dictionary<long, int>();
            var nb = new List<int>[nc];
            for (int c = 0; c < nc; c++) nb[c] = new List<int>();
            foreach (var ch in chains)
                foreach (var x in ch)
                {
                    var d = divEdges[x.index];
                    foreach (int yi in d.twin)
                    {
                        if (yi < 0 || divEdges[yi] == null || divEdges[yi].chain < 0 || divEdges[yi].chain == d.chain) continue;
                        int a = Mathf.Min(d.chain, divEdges[yi].chain), b = Mathf.Max(d.chain, divEdges[yi].chain);
                        long key = ((long)a << 32) | (uint)b;
                        w.TryGetValue(key, out int n); w[key] = ++n;
                        if (n == 3) { nb[a].Add(b); nb[b].Add(a); }
                    }
                }
            var col = new int[nc];
            for (int c = 0; c < nc; c++) col[c] = -1;
            var order = new List<int>(); for (int c = 0; c < nc; c++) order.Add(c);
            order.Sort((p, q) => chainKey[p].CompareTo(chainKey[q]));
            var queue = new Queue<int>();
            foreach (int c0 in order)
            {
                if (col[c0] >= 0) continue;
                col[c0] = 0; queue.Enqueue(c0);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    foreach (int o in nb[c]) if (col[o] < 0) { col[o] = 1 - col[c]; queue.Enqueue(o); }
                }
            }
            return col;
        }
        static DivEdge[] divEdges;
        static CityMap divMap;
        public static string LastDividerReport { get; private set; } = "";
        /// <summary>The planning gate (every candidate median metre, both carriageways):
        /// km with exactly one barrier, in an overpass zone, deliberately open, with none,
        /// with two or more; and the first places with none or two.</summary>
        public static string LastDividerGate { get; private set; } = "";
        static readonly HashSet<int> divSegs = new HashSet<int>();
        static readonly StringBuilder divRunLog = new StringBuilder();
        /// <summary>Editor/runtime clock: ms spent emitting the cable and the beams (the tile
        /// builds since the last reset) and the worst single edge's.</summary>
        public static double DividerEmitMs, DividerEmitMaxMs;

        static bool DivCandidate(CityMap.Edge e) =>
            e.a != e.b && e.oneway && !e.link && e.cls >= 5 && Barriered(e) && !string.IsNullOrEmpty(e.name) && !ConcreteCorridors.Contains(e.name);

        /// <summary>The divider between this carriageway and its twin. Concrete (as before)
        /// for everything but a cable or open run, and whenever the switch is off.</summary>
        public static DividerKind DividerAt(CityMap.Edge e)
        {
            if (!DividersOn || divEdges == null || e.index >= divEdges.Length) return DividerKind.Concrete;
            var d = divEdges[e.index];
            return d == null ? DividerKind.Concrete : d.kind;
        }
        static bool ConcreteMedian(CityMap.Edge e) => DividerAt(e) == DividerKind.Concrete;
        static DivEdge DivOf(CityMap.Edge e) =>
            !DividersOn || divEdges == null || e.index >= divEdges.Length ? null : divEdges[e.index];

        static int DivSample(DivEdge d, float s) =>
            d?.gap == null || d.gap.Length == 0 ? -1 : Mathf.Clamp(Mathf.FloorToInt(s / DivStepM), 0, d.gap.Length - 1);

        /// <summary>The median's unpaved width at arc <paramref name="s"/> (the nearest sample
        /// with a twin), NaN where none.</summary>
        static float DivGapAt(DivEdge d, float s)
        {
            if (d?.gap == null || d.gap.Length == 0) return float.NaN;
            float f = s / DivStepM - 0.5f;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, d.gap.Length - 1), i1 = Mathf.Min(i0 + 1, d.gap.Length - 1);
            float g0 = d.gap[i0], g1 = d.gap[i1];
            if (!float.IsNaN(g0) && !float.IsNaN(g1)) return Mathf.Lerp(g0, g1, Mathf.Clamp01(f - i0));
            if (!float.IsNaN(g0)) return g0;
            if (!float.IsNaN(g1)) return g1;
            for (int k = 1; k < 4; k++)
            {
                if (i0 - k >= 0 && !float.IsNaN(d.gap[i0 - k])) return d.gap[i0 - k];
                if (i1 + k < d.gap.Length && !float.IsNaN(d.gap[i1 + k])) return d.gap[i1 + k];
            }
            return float.NaN;
        }

        /// <summary>Does [s0, s1] meet a crossing deck's zone grown by <paramref name="pad"/>?</summary>
        static bool InDivZone(DivEdge d, float s0, float s1, float pad)
        {
            if (d?.zones == null) return false;
            foreach (var z in d.zones) if (s1 > z.x - pad && s0 < z.y + pad) return true;
            return false;
        }

        /// <summary>Is the cable built at arc s of this edge?</summary>
        static bool CableAt(DivEdge d, float s)
        {
            if (d?.ivs == null) return false;
            foreach (var iv in d.ivs) if (s >= iv.s0 - 1e-3f && s <= iv.s1 + 1e-3f) return true;
            return false;
        }

        /// <summary>For the drive audit's median rays (CityAudit.Median): what a ray across the
        /// median at arc s of e (to o at ot) should meet. -1 not a divider median (the concrete
        /// rules as before), 0 a deliberately open median (none), 1 a cable median (exactly one
        /// collider: the cable, or the concrete side's Jersey where the pair is concrete on the
        /// other side), 2 an overpass zone (a W-beam each side by design, the cable lapping in
        /// behind them).</summary>
        public static int DividerRayClass(CityMap.Edge e, float s, CityMap.Edge o, float ot)
        {
            var d = DivOf(e); var q = o != null ? DivOf(o) : null;
            if (d == null && q == null) return -1;
            var kd = d?.kind ?? DividerKind.Concrete; var kq = q?.kind ?? DividerKind.Concrete;
            if (kd == DividerKind.Concrete && kq == DividerKind.Concrete) return -1;
            if (InDivZone(d, s, s, WBeamTermM) || InDivZone(q, ot, ot, WBeamTermM)) return 2;
            if (kd == DividerKind.Open && kq == DividerKind.Open) return 0;
            return 1;   // (a narrow pinch too: one barrier, its Jersey; DividerLapAt names the cable's lap)
        }

        /// <summary>For the drive audit's median rays: is arc s of e in a DESIGNED LAP - where
        /// the cable overlaps another barrier on purpose: the length it runs on behind a median
        /// rail's end (CableLapM into the rail, and the rail's lead-in or sloped end before it,
        /// recorded by the tiles that cut it: <see cref="DividerLaps"/>), or a pinch's Jersey
        /// past the cable's cut. Overpass zones are their own class.</summary>
        public static bool DividerLapAt(CityMap.Edge e, float s)
        {
            var d = DivOf(e);
            if (d == null) return false;
            if (InArcs(d.pinchRun, s) && !InArcs(d.pinchCore, s)) return true;
            if (InArcs(d.laps, s)) return true;
            if (InDivZone(d, s, s, WBeamTermM + 2f)) return true;   // behind an overpass beam's terminal
            return DividerLaps.TryGetValue(e.index, out var l) && InArcs(l, s);
        }
        /// <summary>The laps the tiles built, per cable edge (arcs of that edge), recorded when
        /// <see cref="DividerLapRecord"/> is on (the audits).</summary>
        public static readonly Dictionary<int, List<Vector2>> DividerLaps = new Dictionary<int, List<Vector2>>();
        public static bool DividerLapRecord;
        /// <summary>Is the cable in the plan at arc s of e (a tile cuts it further behind rails)?</summary>
        public static bool CableBuiltAt(CityMap.Edge e, float s) => CableAt(DivOf(e), s);
        /// <summary>The plan at arc s of e, for audit notes.</summary>
        public static string DescribeDivider(CityMap.Edge e, float s)
        {
            var d = DivOf(e);
            if (d == null) return $"e{e.index}: no divider plan";
            int k = DivSample(d, s);
            string ivs = "";
            if (d.ivs != null) foreach (var iv in d.ivs) if (iv.s1 > s - 60f && iv.s0 < s + 60f) ivs += $" [{iv.s0:0}..{iv.s1:0}]";
            string laps = "";
            if (DividerLaps.TryGetValue(e.index, out var l)) foreach (var a in l) if (a.y > s - 60f && a.x < s + 60f) laps += $" [{a.x:0}..{a.y:0}]";
            return $"e{e.index} s {s:0} {d.kind} chain {d.chain} sample {k} gap {d.gap[k]:0.0} twin e{d.twin[k]} y {d.twinS[k]:0} draw {d.draw?[k]} cable{ivs} pinch {InArcs(d.pinchRun, s)}/{InArcs(d.pinchCore, s)} zone {InDivZone(d, s, s, 0f)} laps{laps} offset {CableOffAt(d, s):0.00}";
        }

        /// <summary>For the cable check (Editor CableCrashSim): metres from e's paved median
        /// edge out to the cable line where the cable is built at arc s, else NaN.</summary>
        public static float CableOffsetIfBuilt(CityMap.Edge e, float s)
        {
            var d = DivOf(e);
            return CableAt(d, s) ? CableOffAt(d, s) : float.NaN;
        }

        // ------------------------------------------------------------------
        //  THE CLASSIFICATION (once per trims build, after the structure)
        // ------------------------------------------------------------------
        /// <summary>The candidate corridors' own segments in 64 m cells: the twin search asks
        /// only these (the city-wide segment grid made the classification ~160 ms).</summary>
        static readonly Dictionary<long, List<int>> divGrid = new Dictionary<long, List<int>>();
        const float DivCellM = 64f, DivHintHoldM = 1.5f;
        const int DivFullEvery = 4;
        static long DivCell(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        /// <summary>Each edge's corridor name as a small number (-1 not a candidate name): the
        /// twin search compares these, not strings.</summary>
        static int[] divNameOf = new int[0];

        static void BuildDivGrid(CityMap map, HashSet<string> names)
        {
            divGrid.Clear();
            if (divNameOf.Length != map.edges.Length) divNameOf = new int[map.edges.Length];
            var ids = new Dictionary<string, int>();
            foreach (var c in map.edges)
            {
                divNameOf[c.index] = -1;
                if (c.a == c.b || !c.oneway || c.link || string.IsNullOrEmpty(c.name) || !names.Contains(c.name)) continue;
                if (!ids.TryGetValue(c.name, out int id)) ids[c.name] = id = ids.Count;
                divNameOf[c.index] = id;
                for (int si = 0; si + 1 < c.pts.Length && si < 0xFFF; si++)
                {
                    Vector2 a = c.pts[si], b = c.pts[si + 1];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / DivCellM), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / DivCellM);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / DivCellM), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / DivCellM);
                    for (int cx = x0; cx <= x1; cx++)
                        for (int cz = z0; cz <= z1; cz++)
                        {
                            long key = DivCell(cx, cz);
                            if (!divGrid.TryGetValue(key, out var l)) divGrid[key] = l = new List<int>(4);
                            l.Add((c.index << 12) | si);
                        }
                }
            }
        }

        /// <summary>The opposing twin beside arc <paramref name="x"/> of X on its median side
        /// and the unpaved width between the two (LineModel extents).</summary>
        static bool DivTwinAt(CityMap map, CityMap.Edge X, float x, CityMap.Edge hint, float hintS, float hintDist, bool full, out CityMap.Edge Y, out float y, out float gap, out float dist)
        {
            Y = null; y = 0f; gap = float.NaN; dist = float.NaN;
            var p = X.PointAt(x);
            var tan = X.TangentAt(x);
            var left = new Vector2(-tan.y, tan.x);
            float r = X.HalfMax + DivReachM + 15f, best = float.MaxValue;
            CityMap.Edge bY = null; float by = 0f;
            // one segment of a candidate twin: the nearest point on it, on the median side
            void Try(CityMap.Edge c, int si)
            {
                Vector2 a0 = c.pts[si], dseg = c.pts[si + 1] - a0;
                float L2 = dseg.sqrMagnitude;
                if (L2 < 1e-8f) return;
                float L = Mathf.Sqrt(L2);
                var dirC = dseg / L;
                if (Vector2.Dot(tan, dirC) > -0.5f) return;
                float along = Vector2.Dot(p - a0, dirC);
                bool first = si == 0, lastSeg = si + 2 >= c.pts.Length;
                if (along < -(first ? UnionFootSlackM : 0.5f) || along > L + (lastSeg ? UnionFootSlackM : 0.5f)) return;
                float tcl = Mathf.Clamp(along, 0f, L);
                var q = a0 + dirC * tcl;
                if (Vector2.Dot(q - p, left) <= 0f) return;   // the median is on the left of travel
                float dd = Vector2.Distance(p, q);
                if (dd < best) { best = dd; bY = c; by = c.s[si] + tcl; }
            }
            // WARM START (round 3): the last sample's twin, near where it was. Its distance
            // bounds the grid search below to the cells within it - the same answer (any
            // nearer segment has a point in one of those cells), a quarter of the cells
            if (hint != null)
            {
                int s0 = hint.SegmentAt(Mathf.Clamp(hintS - 2f * DivStepM, 0f, hint.length), out _);
                for (int si = s0; si + 1 < hint.pts.Length && si < 0xFFF && hint.s[si] <= hintS + 2f * DivStepM; si++) Try(hint, si);
            }
            if (best < r) r = best + 0.05f;
            // ...and between full searches (every DivFullEvery samples) the hint's answer is
            // taken while its distance holds within DivHintHoldM of the last one's: the
            // carriageways run on side by side (the gate is the check)
            bool trust = !full && hint != null && bY != null && !float.IsNaN(hintDist) && Mathf.Abs(best - hintDist) <= DivHintHoldM;
            if (trust) r = -1f;
            int cx0 = Mathf.FloorToInt((p.x - r) / DivCellM), cx1 = Mathf.FloorToInt((p.x + r) / DivCellM);
            int cz0 = Mathf.FloorToInt((p.y - r) / DivCellM), cz1 = Mathf.FloorToInt((p.y + r) / DivCellM);
            if (trust) cx1 = cx0 - 1;
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    if (!divGrid.TryGetValue(DivCell(cx, cz), out var cell)) continue;
                    foreach (int seg in cell)
                    {
                        var c = map.edges[seg >> 12];
                        if (c == X || divNameOf[c.index] != divNameOf[X.index]) continue;
                        Try(c, seg & 0xFFF);
                    }
                }
            Y = bY; y = by; dist = best;
            if (Y == null) return false;
            if (best > X.HalfMax + Y.HalfMax + DivReachM) { Y = null; return false; }
            LineModel.Extents(X, x, out _, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            gap = best - xp - (SideToward(Y, y, p) > 0 ? yp : ym);
            return true;
        }

        static CityMap.Edge DivStep(CityMap map, CityMap.Edge e, bool fwd)
        {
            int node = fwd ? e.b : e.a;
            var ne = map.nodeEdges?[node];
            if (ne == null) return null;
            var t = fwd ? e.TangentAt(e.length) : e.TangentAt(0f);
            CityMap.Edge best = null; float bd = 0.85f;
            foreach (int gi in ne)
            {
                var g = map.edges[gi];
                if (g == e || !DivCandidate(g) || g.name != e.name || (fwd ? g.a != node : g.b != node)) continue;
                float d = Vector2.Dot(t, fwd ? g.TangentAt(0f) : g.TangentAt(g.length));
                if (d > bd) { bd = d; best = g; }
            }
            return best;
        }
        static CityMap.Edge DivNext(CityMap map, CityMap.Edge e)
        {
            var g = DivStep(map, e, true);
            return g != null && DivStep(map, g, false) == e ? g : null;
        }
        static CityMap.Edge DivPrev(CityMap map, CityMap.Edge e)
        {
            var g = DivStep(map, e, false);
            return g != null && DivStep(map, g, true) == e ? g : null;
        }
        static bool RampAt(CityMap map, int node)
        {
            var ne = map.nodeEdges?[node];
            if (ne == null) return false;
            foreach (int gi in ne) if (map.edges[gi].link) return true;
            return false;
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return float.NaN;
            v.Sort();
            return v[v.Count / 2];
        }

        static DividerKind DivDecide(DividerKind? state, float med, float found, float len)
        {
            if (found < 0.3f || float.IsNaN(med)) return state ?? DividerKind.Cable;   // no twin beside: as before it
            if (med > DivOpenM && len >= DivOpenMinRunM) return DividerKind.Open;
            if (state == null) return med < 0.5f * (DivNarrowLeaveM + DivNarrowEnterM) ? DividerKind.Concrete : DividerKind.Cable;
            if (len < DivMinRunM) return state.Value == DividerKind.Open ? DividerKind.Cable : state.Value;
            if (state.Value == DividerKind.Concrete) return med > DivNarrowEnterM ? DividerKind.Cable : DividerKind.Concrete;
            return med < DivNarrowLeaveM ? DividerKind.Concrete : DividerKind.Cable;
        }

        class DivRun { public List<CityMap.Edge> edges = new List<CityMap.Edge>(); public float len, med, found; public DividerKind kind, own; }

        /// <summary>The arcs of e on structure (ElevatedAt's own rule: a station's neighbours
        /// both ways), padded by <paramref name="pad"/>.</summary>
        static List<Vector2> DivElevated(CityMap.Edge e, float pad)
        {
            List<Vector2> r = null;
            if (e.stS == null || e.stElev == null || e.stS.Length == 0) return null;
            int n = Mathf.Min(e.stS.Length, e.stElev.Length);
            for (int i = 0; i < n; i++)
            {
                if (!e.stElev[i]) continue;
                float a = e.stS[Mathf.Max(0, i - 1)] - pad, b = e.stS[Mathf.Min(n - 1, i + 1)] + pad;
                r ??= new List<Vector2>();
                if (r.Count > 0 && a <= r[r.Count - 1].y) r[r.Count - 1] = new Vector2(r[r.Count - 1].x, Mathf.Max(b, r[r.Count - 1].y));
                else r.Add(new Vector2(a, b));
            }
            return r;
        }

        /// <summary>Subtract the intervals <paramref name="cut"/> from [a, b] into <paramref name="into"/>.</summary>
        static void DivSubtract(float a, float b, List<Vector2> cut, List<Vector2> into)
        {
            into.Clear();
            into.Add(new Vector2(a, b));
            if (cut == null) return;
            foreach (var c in cut)
                for (int k = into.Count - 1; k >= 0; k--)
                {
                    var v = into[k];
                    if (c.y <= v.x || c.x >= v.y) continue;
                    into.RemoveAt(k);
                    if (c.y < v.y) into.Insert(k, new Vector2(c.y, v.y));
                    if (c.x > v.x) into.Insert(k, new Vector2(v.x, c.x));
                }
        }

        /// <summary>Classify every candidate carriageway (called by BuildDeckUnions once the
        /// structure is marked, before the ground-twin walk reads it).</summary>
        /// <summary>Set by the elevation solve around its seat pass (CityElevation.PrepareSeats):
        /// the trims it asks for come before any structure but the OSM bridges is marked, so
        /// no divider plan is made from them - that pass reads every median as it was before
        /// the divider type (concrete), and the solve is the same with the switch on or off.</summary>
        public static bool DividerSeatPass;
        /// <summary>The plan, ONE per map and solve state (round 3): ComputeTrims runs two or
        /// three times a load (the game's node trims, an audit's own) and each built the plan
        /// again - and the seat pass's, from before the solve, disagreed with the rest.</summary>
        static CityMap divCacheMap;
        static ulong divCacheKey;
        static DivEdge[] divCacheEdges;
        static string divCacheReport = "", divCacheGate = "";
        /// <summary>Clock: every divider plan request of this load (builds, reuses, the state
        /// check) and the planning gate's share of it, in ms.</summary>
        public static double DividerPlanMsTotal, DividerGateMsTotal;
        public static int DividerPlanBuilds, DividerPlanReuses;

        /// <summary>The solve state the plan is made from: every edge's stations, structure
        /// flags and heights (to 5 cm).</summary>
        static ulong DivStateKey(CityMap map)
        {
            ulong h = 1469598103934665603UL;
            void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
            Mix((ulong)map.edges.Length);
            foreach (var e in map.edges)
            {
                // per edge: its station count, structure flags and three heights - the
                // solve moves a whole edge's profile, so its ends and middle tell
                if (e.stS == null) { Mix(0xFFFF); continue; }
                int ns = e.stS.Length;
                Mix((ulong)ns);
                if (e.stElev != null) { ulong bits = 0; for (int i = 0; i < e.stElev.Length; i++) if (e.stElev[i]) bits = bits * 31UL + (ulong)i + 1UL; Mix(bits); }
                if (e.stY != null && e.stY.Length > 0) { Mix((ulong)(long)Mathf.RoundToInt(e.stY[0] * 20f)); Mix((ulong)(long)Mathf.RoundToInt(e.stY[e.stY.Length / 2] * 20f)); Mix((ulong)(long)Mathf.RoundToInt(e.stY[e.stY.Length - 1] * 20f)); }
            }
            return h;
        }

        /// <summary>The divider plan for this map (called by BuildDeckUnions once the structure
        /// is marked): built once on the final solve state and reused by every later trims
        /// build of the same state; none in the seat pass.</summary>
        static void PlanDividers(CityMap map)
        {
            if (DividerSeatPass || !DividersOn || map?.edges == null)
            {
                divEdges = null; divMap = map;
                LastDividerReport = DividersOn ? (DividerSeatPass ? "dividers: none in the seat pass" : "") : "dividers off (PSX_CITY_DIVIDERS=0)";
                LastDividerGate = "";
                return;
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            ulong key = DivStateKey(map);
            if (divCacheMap == map && divCacheKey == key && divCacheEdges != null)
            {
                divEdges = divCacheEdges; divMap = map;
                LastDividerReport = divCacheReport; LastDividerGate = divCacheGate;
                DividerPlanReuses++;
                DividerPlanMsTotal += clock.Elapsed.TotalMilliseconds;
                Debug.Log($"[city] dividers: the plan reused (same map and solve state, {clock.Elapsed.TotalMilliseconds:0.0} ms); this load {DividerPlanBuilds} built, {DividerPlanReuses} reused, {DividerPlanMsTotal:0.0} ms in all");
                return;
            }
            BuildDividers(map);
            double first = clock.Elapsed.TotalMilliseconds;
            divCacheMap = map; divCacheKey = key; divCacheEdges = divEdges;
            divCacheReport = LastDividerReport; divCacheGate = LastDividerGate;
            DividerPlanBuilds++;
            DividerPlanMsTotal += clock.Elapsed.TotalMilliseconds;
            // (the log lines come after the clock: they are the editor's, not the plan's cost)
            Debug.Log("[city] " + LastDividerReport);
            Debug.Log("[city] " + LastDividerGate);
            Debug.Log($"[city] dividers: plan built in {clock.Elapsed.TotalMilliseconds:0.0} ms with the state check (gate {divGateMs:0.0} ms of it); this load {DividerPlanBuilds} built, {DividerPlanReuses} reused, {DividerPlanMsTotal:0.0} ms in all");
            // PSX_DIV_BENCH=1 (measurement only): the same build three more times, warm (the
            // editor's first call carries the JIT of every method it runs; a player build has none)
            if (System.Environment.GetEnvironmentVariable("PSX_DIV_BENCH") == "1")
            {
                double best = double.MaxValue, bestGate = 0;
                for (int b = 0; b < 3; b++)
                {
                    var bc = System.Diagnostics.Stopwatch.StartNew();
                    DivStateKey(map); BuildDividers(map);
                    double ms = bc.Elapsed.TotalMilliseconds;
                    if (ms < best) { best = ms; bestGate = divGateMs; }
                }
                divCacheEdges = divEdges; divCacheReport = LastDividerReport; divCacheGate = LastDividerGate;
                Debug.Log($"[city] dividers bench: first build {first:0.0} ms (cold, with the state check), warm best of three {best:0.0} ms (gate {bestGate:0.0} ms of it)");
                DividerBenchLine = $"first build {first:0.0} ms cold, warm best of three {best:0.0} ms (gate {bestGate:0.0} ms)";
            }
        }
        static double divGateMs;
        public static string DividerBenchLine = "";

        static void BuildDividers(CityMap map)
        {
            divEdges = null; divMap = map;
            divElevCache.Clear();
            LastDividerReport = "";
            LastDividerGate = "";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var E = map.edges;
            divEdges = new DivEdge[E.Length];
            var names = new HashSet<string>();
            foreach (var e in E) if (DivCandidate(e)) names.Add(e.name);
            BuildDivGrid(map, names);
            long tGrid = clock.ElapsedMilliseconds;
            // samples, the twin at each kept
            foreach (var e in E)
            {
                if (!DivCandidate(e)) continue;
                int ns = Mathf.Max(1, Mathf.CeilToInt(e.length / DivStepM));
                var d = new DivEdge { gap = new float[ns], twin = new int[ns], twinS = new float[ns], kind = DividerKind.Cable };
                CityMap.Edge hint = null; float hintS = 0f, hintD = float.NaN;
                for (int k = 0; k < ns; k++)
                {
                    float s = Mathf.Min((k + 0.5f) * DivStepM, e.length);
                    bool full = k % DivFullEvery == 0 || k == ns - 1;
                    if (DivTwinAt(map, e, s, hint, hintS - DivStepM, hintD, full, out var Y, out float y, out float g, out float dd)) { d.gap[k] = g; d.twin[k] = Y.index; d.twinS[k] = y; hint = Y; hintS = y; hintD = dd; }
                    else { d.gap[k] = float.NaN; d.twin[k] = -1; hint = null; hintD = float.NaN; }
                }
                divEdges[e.index] = d;
            }
            long tSamples = clock.ElapsedMilliseconds;
            // chains of one carriageway through plain nodes
            var chains = new List<List<CityMap.Edge>>();
            foreach (var e in E)
            {
                var d0 = divEdges[e.index];
                if (d0 == null || d0.chain >= 0) continue;
                var start = e; int guard = 0;
                for (var p = DivPrev(map, start); p != null && p != e && divEdges[p.index].chain < 0 && guard < 100000; p = DivPrev(map, start), guard++) start = p;
                var ch = new List<CityMap.Edge>();
                for (var cur = start; cur != null && divEdges[cur.index].chain < 0; cur = DivNext(map, cur))
                {
                    divEdges[cur.index].chain = chains.Count;
                    ch.Add(cur);
                }
                // a loop: start it just past a ramp node, so no run wraps
                if (ch.Count > 1 && DivNext(map, ch[ch.Count - 1]) == ch[0])
                {
                    int k0 = ch.FindIndex(x => RampAt(map, x.a));
                    if (k0 > 0) { var rot = new List<CityMap.Edge>(ch.GetRange(k0, ch.Count - k0)); rot.AddRange(ch.GetRange(0, k0)); ch = rot; }
                }
                chains.Add(ch);
            }
            int[] chainKey = new int[chains.Count];
            for (int c = 0; c < chains.Count; c++) { int m = int.MaxValue; foreach (var x in chains[c]) m = Mathf.Min(m, x.index); chainKey[c] = m; }
            // runs between ramps, decided with hysteresis from the longest one out
            var vals = new List<float>(1024);
            var allRuns = new List<DivRun>();
            foreach (var ch in chains)
            {
                var runs = new List<DivRun>();
                var r = new DivRun();
                foreach (var x in ch)
                {
                    r.edges.Add(x); r.len += x.length;
                    if (RampAt(map, x.b)) { runs.Add(r); r = new DivRun(); }
                }
                if (r.edges.Count > 0) runs.Add(r);
                foreach (var rr in runs)
                {
                    vals.Clear(); int all = 0;
                    foreach (var x in rr.edges)
                    {
                        var g = divEdges[x.index].gap;
                        for (int k = 0; k < g.Length; k++)
                        {
                            float s = Mathf.Min((k + 0.5f) * DivStepM, x.length);
                            if (x.bridge || x.ElevatedAt(s)) continue;
                            all++;
                            if (!float.IsNaN(g[k])) vals.Add(g[k]);
                        }
                    }
                    rr.found = all > 0 ? (float)vals.Count / all : 0f;
                    rr.med = Median(vals);
                }
                int kL = 0;
                for (int k = 1; k < runs.Count; k++) if (runs[k].len > runs[kL].len) kL = k;
                DividerKind? st = null;
                bool loop = ch.Count > 1 && DivNext(map, ch[ch.Count - 1]) == ch[0];
                for (int q = 0; q < runs.Count; q++)
                {
                    int k = loop ? (kL + q) % runs.Count : kL + q;
                    if (k >= runs.Count) break;
                    st = DivDecide(st, runs[k].med, runs[k].found, runs[k].len); runs[k].kind = st.Value;
                }
                if (!loop)
                {
                    st = runs.Count > 0 ? runs[kL].kind : (DividerKind?)null;
                    for (int k = kL - 1; k >= 0; k--) { st = DivDecide(st, runs[k].med, runs[k].found, runs[k].len); runs[k].kind = st.Value; }
                }
                foreach (var rr in runs)
                {
                    rr.own = rr.kind;
                    foreach (var x in rr.edges) divEdges[x.index].run = allRuns.Count;
                    allRuns.Add(rr);
                }
            }
            // ONE KIND A RUN PAIR: each carriageway's runs are decided on its own, and the
            // two sides' run boundaries are staggered (each side's ramps join at its own
            // nodes). A concrete or open run stands only where the runs across the median
            // are that kind over most of its length; otherwise it is cable (the corridor's
            // kind), and that is iterated until no run changes - so a pair is one kind run
            // for run, and only cable can be what is left at a stagger.
            int reconciled = 0;
            var tally = new int[3];
            for (int iter = 0; iter < 12; iter++)
            {
                bool changed = false;
                foreach (var rr in allRuns)
                {
                    if (rr.kind == DividerKind.Cable) continue;
                    tally[0] = tally[1] = tally[2] = 0;
                    foreach (var x in rr.edges)
                    {
                        var d = divEdges[x.index];
                        for (int k = 0; k < d.twin.Length; k++)
                        {
                            if (d.twin[k] < 0) continue;
                            var dy = divEdges[d.twin[k]];
                            tally[dy == null || dy.run < 0 ? (int)DividerKind.Cable : (int)allRuns[dy.run].kind]++;
                        }
                    }
                    int tot = tally[0] + tally[1] + tally[2];
                    if (tot > 0 && tally[(int)rr.kind] * 2 <= tot) { rr.kind = DividerKind.Cable; changed = true; reconciled++; }
                }
                if (!changed) break;
            }
            foreach (var rr in allRuns)
            {
                foreach (var x in rr.edges) divEdges[x.index].kind = rr.kind;
                divRunLog.Append($"\n  run {rr.edges[0].name} e{rr.edges[0].index}..e{rr.edges[rr.edges.Count - 1].index} {rr.len:0} m median {rr.med:0.0} m found {rr.found:0.00} -> {rr.own}{(rr.own != rr.kind ? " -> " + rr.kind + " (pair)" : "")}");
            }
            long tRuns = clock.ElapsedMilliseconds;
            // WHO CARRIES THE CABLE (round 3): ONE SIDE OF THE PAIR, the same all along the
            // corridor. The chains are coloured by direction from their twin pairings (the
            // corridor's lowest-key chain and every chain running its way side 0, the chains
            // across from them side 1) and side 0 carries the pair's cable wherever both are
            // cable; side 1 never does. Round 2 compared the two chains at each sample and
            // smoothed lone samples over, so a sample whose twin answered from another
            // sample, or one smoothed over a deck or concrete rule, stood the cable on both
            // sides or on neither. The rules across are hard and never smoothed: across from
            // a concrete run nothing (its Jersey is the barrier); across from an open run or
            // from no plan, this side. Where the twin is on structure the cable is cut
            // exactly there, below with the as-built arcs (its parapet is the barrier).
            var minKey = new Dictionary<string, int>();
            foreach (var ch in chains) { var nm = ch[0].name; int key = chainKey[divEdges[ch[0].index].chain]; minKey[nm] = minKey.TryGetValue(nm, out int had) ? Mathf.Min(had, key) : key; }
            var colour = DivChainColours(E, chains, chainKey);
            int loneEdges = 0;
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null) continue;
                int ns = d.gap.Length;
                d.draw = new bool[ns];
                if (d.kind != DividerKind.Cable) continue;
                var known = new bool[ns];
                bool any = false;
                for (int k = 0; k < ns; k++)
                {
                    int yi = d.twin[k];
                    if (yi < 0) continue;
                    var dy = divEdges[yi];
                    known[k] = any = true;
                    if (dy == null || dy.kind == DividerKind.Open) d.draw[k] = true;            // no divider plan across, or open: ours
                    else if (dy.kind == DividerKind.Concrete) d.draw[k] = false;                // its Jersey is the barrier
                    else
                    {
                        int cd = colour[d.chain], cy = colour[dy.chain];
                        d.draw[k] = cd != cy ? cd == 0 : chainKey[d.chain] < chainKey[dy.chain] || (chainKey[d.chain] == chainKey[dy.chain] && e.index < yi);
                    }
                }
                // no twin found at a sample: the nearest known sample's answer; an edge with
                // no twin anywhere is a lone carriageway, carried by the corridor's lowest
                // chain (counted in the gate's lone column)
                if (!any) loneEdges++;
                for (int k = 0; k < ns; k++)
                {
                    if (known[k]) continue;
                    int best = -1;
                    for (int dk = 1; dk < ns && best < 0; dk++)
                    {
                        if (k - dk >= 0 && known[k - dk]) best = k - dk;
                        else if (k + dk < ns && known[k + dk]) best = k + dk;
                    }
                    d.draw[k] = best >= 0 ? d.draw[best] : chainKey[d.chain] == minKey[e.name];
                }
            }
            // NARROW PINCHES (round 3, the review's e2508 at a bridge approach: the two
            // carriageways close to a 1.2-1.4 m gap inside a cable run, and a median rail's
            // end met traffic square there). A cable needs CableMinGapM of median; where the
            // gap is less (the core) the median is concrete, as before the divider type: a
            // Jersey stands on the median edge (the span flags read DivPinchAt) through the
            // core and one sample on each side and on while the gap stays under
            // CableMinGapM + 1 m, the rails hand over to it as they did, and the cable is cut
            // only at the core - so it laps at least one sample behind the Jersey's end.
            // Marked on both carriageways (each side's samples and its twin's).
            int pinchRuns = 0;
            {
                var coreM = new Dictionary<int, bool[]>(); var runM = new Dictionary<int, bool[]>();
                foreach (var e in E)
                {
                    var d = divEdges[e.index];
                    if (d == null || d.kind != DividerKind.Cable) continue;
                    int ns = d.gap.Length;
                    bool[] core = null, run = null;
                    for (int k = 0; k < ns; k++)
                    {
                        float g = d.gap[k], s = Mathf.Min((k + 0.5f) * DivStepM, e.length);
                        if (float.IsNaN(g) || g >= CableMinGapM || e.ElevatedAt(s) || (d.twin[k] >= 0 && E[d.twin[k]].ElevatedAt(d.twinS[k]))) continue;
                        core ??= new bool[ns]; core[k] = true;
                    }
                    if (core == null) continue;
                    run = new bool[ns];
                    for (int k = 0; k < ns; k++)
                    {
                        if (!core[k]) continue;
                        run[k] = true;
                        for (int dir = -1; dir <= 1; dir += 2)
                            for (int q = k + dir, step = 1; q >= 0 && q < ns; q += dir, step++)
                            {
                                if (step > 1 && (float.IsNaN(d.gap[q]) || d.gap[q] >= CableMinGapM + 1f)) break;
                                run[q] = true;
                            }
                    }
                    coreM[e.index] = core; runM[e.index] = run;
                }
                // ...and across: the twin's samples opposite them
                var add = new List<(int ei, int k, bool core)>();
                foreach (var kv in runM)
                {
                    var d = divEdges[kv.Key]; var core = coreM[kv.Key];
                    for (int k = 0; k < kv.Value.Length; k++)
                    {
                        if (!kv.Value[k] || d.twin[k] < 0) continue;
                        var dy = divEdges[d.twin[k]];
                        if (dy == null || dy.kind != DividerKind.Cable) continue;
                        add.Add((d.twin[k], DivSample(dy, d.twinS[k]), core[k]));
                    }
                }
                foreach (var (ei, k, isCore) in add)
                {
                    int ns = divEdges[ei].gap.Length;
                    if (!runM.TryGetValue(ei, out var run)) { runM[ei] = run = new bool[ns]; coreM[ei] = new bool[ns]; }
                    run[k] = true;
                    if (isCore) coreM[ei][k] = true;
                }
                foreach (var kv in runM)
                {
                    var e = E[kv.Key]; var d = divEdges[kv.Key];
                    d.pinchRun = DivArcs(e, kv.Value);
                    d.pinchCore = DivArcs(e, coreM[kv.Key]);
                    if (d.pinchRun != null) pinchRuns += d.pinchRun.Count;
                }
            }
            // the overpass zones (cable and open medians)
            int zonesN = 0;
            var seen = new HashSet<long>();
            // the structure's own segments in cells (a crossing deck is on structure there)
            long tDeck0 = clock.ElapsedMilliseconds;
            // the candidate corridors' 256 m cells and their ring: structure elsewhere never
            // crosses one, and is not registered
            var nearCoarse = new HashSet<long>();
            foreach (var key in divGrid.Keys)
            {
                int kx = (int)(key >> 32), kz = (int)(uint)key;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        nearCoarse.Add(DivCell((kx + dx) >> 2, (kz + dz) >> 2));
            }
            var deckGrid = new Dictionary<long, List<int>>();
            foreach (var c in E)
            {
                if (c.stElev == null || c.stS == null) continue;
                bool any = false; foreach (bool b in c.stElev) if (b) { any = true; break; }
                if (!any) continue;
                for (int si = 0; si + 1 < c.pts.Length && si < 0xFFF; si++)
                {
                    Vector2 a = c.pts[si], b = c.pts[si + 1];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / DivCellM), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / DivCellM);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / DivCellM), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / DivCellM);
                    // only near a candidate corridor (its cells and their ring): the rest of
                    // the city's structure never crosses one
                    bool near = false;
                    for (int cx = x0 >> 2; cx <= x1 >> 2 && !near; cx++)
                        for (int cz = z0 >> 2; cz <= z1 >> 2 && !near; cz++)
                            near = nearCoarse.Contains(DivCell(cx, cz));
                    if (!near) continue;
                    if (!c.ElevatedAt(c.s[si]) && !c.ElevatedAt(c.s[si + 1]) && !c.ElevatedAt(0.5f * (c.s[si] + c.s[si + 1]))) continue;
                    for (int cx = x0; cx <= x1; cx++)
                        for (int cz = z0; cz <= z1; cz++)
                        {
                            long key = DivCell(cx, cz);
                            if (!deckGrid.TryGetValue(key, out var l)) deckGrid[key] = l = new List<int>(4);
                            l.Add((c.index << 12) | si);
                        }
                }
            }
            long tDeck1 = clock.ElapsedMilliseconds;
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null || d.kind == DividerKind.Concrete) continue;
                seen.Clear();
                for (int k = 0; k + 1 < e.pts.Length; k++)
                {
                    Vector2 a = e.pts[k], b = e.pts[k + 1];
                    divSegs.Clear();
                    {
                        Vector2 lo = Vector2.Min(a, b) - Vector2.one * 2f, hi = Vector2.Max(a, b) + Vector2.one * 2f;
                        for (int cx = Mathf.FloorToInt(lo.x / DivCellM); cx <= Mathf.FloorToInt(hi.x / DivCellM); cx++)
                            for (int cz = Mathf.FloorToInt(lo.y / DivCellM); cz <= Mathf.FloorToInt(hi.y / DivCellM); cz++)
                                if (deckGrid.TryGetValue(DivCell(cx, cz), out var cell)) foreach (int sg in cell) divSegs.Add(sg);
                    }
                    foreach (int seg in divSegs)
                    {
                        var c = map.edges[seg >> 12];
                        int si = seg & 0xFFF;
                        if (c == e || c.name == e.name || si + 1 >= c.pts.Length || c.stElev == null || !seen.Add(((long)c.index << 20) ^ ((long)si << 8) ^ k)) continue;
                        Vector2 c0 = c.pts[si], c1 = c.pts[si + 1];
                        if (!SegCross(a, b, c0, c1, out float ta, out float tc)) continue;
                        float atE = e.s[k] + (e.s[k + 1] - e.s[k]) * ta, atC = c.s[si] + (c.s[si + 1] - c.s[si]) * tc;
                        if (!c.ElevatedAt(atC) || e.ElevatedAt(atE) || c.YAt(atC) < e.YAt(atE) + 3f) continue;
                        var de = (b - a).normalized; var dc = (c1 - c0).normalized;
                        float sin = Mathf.Abs(de.x * dc.y - de.y * dc.x);
                        float half = c.HalfMax / Mathf.Max(0.3f, sin) + BeamNeedM;
                        (d.zones ??= new List<Vector2>()).Add(new Vector2(atE - half, atE + half));
                        zonesN++;
                    }
                }
                if (d.zones != null && d.zones.Count > 1)
                {
                    d.zones.Sort((p, q) => p.x.CompareTo(q.x));
                    var m = new List<Vector2> { d.zones[0] };
                    for (int k = 1; k < d.zones.Count; k++)
                    {
                        var z = d.zones[k]; var l = m[m.Count - 1];
                        if (z.x <= l.y + 2f * WBeamTermM) m[m.Count - 1] = new Vector2(l.x, Mathf.Max(l.y, z.y));
                        else m.Add(z);
                    }
                    d.zones = m;
                }
            }
            // the zones in a narrow median (the flare is clamped clear of the cable there)
            var narrowZones = new StringBuilder();
            int narrowN = 0;
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d?.zones == null) continue;
                foreach (var z in d.zones)
                {
                    float g = DivGapAt(d, 0.5f * (z.x + z.y));
                    if (float.IsNaN(g) || g >= 6f) continue;
                    if (narrowN++ < 6) { var p = e.PointAt(Mathf.Clamp(0.5f * (z.x + z.y), 0f, e.length)); narrowZones.Append($" e{e.index} s {z.x:0}..{z.y:0} at ({p.x:0},{p.y:0}) gap {g:0.0};"); }
                }
            }
            long tZones = clock.ElapsedMilliseconds;
            // THE CABLE AS BUILT: the drawing samples' arcs, less this carriageway's own
            // structure (exactly where ElevatedAt says so, the rule the gate and the audit
            // read), the twin's structure projected across (its parapet is the barrier
            // there; round 2 dropped a whole 20 m sample whose middle faced a deck, which
            // left the rest of it bare), the pinches' cores and the zones' cores (the cable
            // runs on to the beam's full section, lapped behind its flared terminal, and is
            // turned down there)
            var cut = new List<Vector2>(); var pieces = new List<Vector2>();
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null || d.kind != DividerKind.Cable) continue;
                cut.Clear();
                var el = DivElevated(e, 0f);
                if (el != null)
                {
                    cut.AddRange(el);
                    // the deck's approach W-beam lead-in stands in front of the cable's last
                    // WBeamLenM before the abutment: the cable laps behind it there
                    foreach (var z in el) (d.laps ??= new List<Vector2>()).Add(new Vector2(z.x - WBeamLenM - 1f, z.y + WBeamLenM + 1f));
                }
                if (d.zones != null) cut.AddRange(d.zones);
                if (d.pinchCore != null) cut.AddRange(d.pinchCore);
                for (int k = 0; k < d.twin.Length; k++)
                {
                    if (!d.draw[k] || d.twin[k] < 0) continue;
                    var ey = DivElevatedOf(E[d.twin[k]]);
                    if (ey == null) continue;
                    float sc = Mathf.Min((k + 0.5f) * DivStepM, e.length), ty = d.twinS[k];
                    float lo = k * DivStepM - 0.5f * DivStepM, hi = Mathf.Min(e.length, (k + 1) * DivStepM) + 0.5f * DivStepM;
                    foreach (var z in ey)
                    {
                        // antiparallel: the twin's arc y faces our sc - (y - ty)
                        float a = sc - (z.y - ty), b = sc - (z.x - ty);
                        if (b < lo || a > hi) continue;
                        // the cable runs DivDeckLapM on past the deck's end across (the
                        // projection is a few metres out at a node), lapped behind its parapet
                        (d.laps ??= new List<Vector2>()).Add(new Vector2(a - WBeamLenM - 1f, b + WBeamLenM + 1f));
                        a = Mathf.Max(a + DivDeckLapM, lo); b = Mathf.Min(b - DivDeckLapM, hi);
                        if (b > a) cut.Add(new Vector2(a, b));
                    }
                }
                int ns = d.draw.Length;
                for (int k = 0; k < ns;)
                {
                    if (!d.draw[k]) { k++; continue; }
                    int j = k;
                    while (j < ns && d.draw[j]) j++;
                    float a = k * DivStepM, b = j >= ns ? e.length : j * DivStepM;
                    DivSubtract(a, Mathf.Min(b, e.length), cut, pieces);
                    foreach (var p in pieces)
                        if (p.y - p.x > 0.05f) (d.ivs ??= new List<CableIv>()).Add(new CableIv { s0 = p.x, s1 = p.y });
                    k = j;
                }
            }
            // continuity through the nodes: an arc reaching its edge's end carries on into the
            // next edge's arc that starts at the node
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d?.ivs == null) continue;
                var nx = DivNext(map, e);
                var dn = nx != null ? divEdges[nx.index] : null;
                int last = d.ivs.Count - 1;
                var iv = d.ivs[last];
                if (iv.s1 >= e.length - 0.05f && dn?.ivs != null && dn.ivs[0].s0 <= 0.05f)
                {
                    iv.contB = true; d.ivs[last] = iv;
                    var w = dn.ivs[0]; w.contA = true; dn.ivs[0] = w;
                }
            }
            // the distance to each arc's real ends along the chain (a few edges is enough:
            // past the turn-down length it no longer matters), then the short pieces
            const float far = 1e4f;
            foreach (var e in E) { var d = divEdges[e.index]; if (d?.ivs == null) continue; for (int k = 0; k < d.ivs.Count; k++) { var iv = d.ivs[k]; iv.extA = iv.contA ? far : 0f; iv.extB = iv.contB ? far : 0f; d.ivs[k] = iv; } }
            for (int pass = 0; pass < 6; pass++)
                foreach (var e in E)
                {
                    var d = divEdges[e.index];
                    if (d?.ivs == null) continue;
                    var ivA = d.ivs[0];
                    if (ivA.contA)
                    {
                        var pv = DivPrev(map, e); var dp = divEdges[pv.index]; var pl = dp.ivs[dp.ivs.Count - 1];
                        ivA.extA = Mathf.Min(far, (pl.s1 - pl.s0) + pl.extA); d.ivs[0] = ivA;
                    }
                    var ivB = d.ivs[d.ivs.Count - 1];
                    if (ivB.contB)
                    {
                        var nx = DivNext(map, e); var dn = divEdges[nx.index]; var nf = dn.ivs[0];
                        ivB.extB = Mathf.Min(far, (nf.s1 - nf.s0) + nf.extB); d.ivs[d.ivs.Count - 1] = ivB;
                    }
                }
            int shortDropped = 0;
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d?.ivs == null) continue;
                for (int k = d.ivs.Count - 1; k >= 0; k--)
                {
                    var iv = d.ivs[k];
                    if (iv.extA + (iv.s1 - iv.s0) + iv.extB < CableMinRunM) { (d.dropped ??= new List<Vector2>()).Add(new Vector2(iv.s0, iv.s1)); d.ivs.RemoveAt(k); shortDropped++; }
                }
                if (d.ivs.Count == 0) d.ivs = null;
            }
            // (a dropped piece is a whole chained group shorter than CableMinRunM: its
            // neighbours' ends were real ends already, so nothing else changes)
            long tIvs = clock.ElapsedMilliseconds;

            // the report: carriageway-km of each kind per corridor, km of cable as built
            var km = new SortedDictionary<string, float[]>();
            int drawers = 0;
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null) continue;
                if (!km.TryGetValue(e.name, out var a)) km[e.name] = a = new float[5];
                a[(int)d.kind] += e.length / 1000f;
                if (d.ivs != null) { drawers++; foreach (var iv in d.ivs) a[3] += (iv.s1 - iv.s0) / 1000f; }
                if (d.zones != null) a[4] += d.zones.Count;
            }
            var sb = new StringBuilder();
            sb.Append($"dividers: {chains.Count} chains, {allRuns.Count} runs, {reconciled} runs given their pair's kind, {zonesN} overpass crossings, {drawers} cable-carrying edges, {shortDropped} short cable pieces dropped, {pinchRuns} narrow-pinch Jersey runs, {loneEdges} lone carriageways (no twin), " +
                      $"{clock.ElapsedMilliseconds} ms (grid {tGrid}, samples {tSamples - tGrid}, runs {tRuns - tSamples}, zones {tZones - tRuns} (draw+pinch {tDeck0 - tRuns}, deck grid {tDeck1 - tDeck0}), cable {tIvs - tZones});");
            foreach (var kv in km) sb.Append($" {kv.Key}: carriageway-km concrete {kv.Value[0]:0.0} cable {kv.Value[1]:0.0} open {kv.Value[2]:0.0}, cable line {kv.Value[3]:0.0} km, {kv.Value[4]:0} zone sides;");
            sb.Append($" overpass zones in a median under 6 m: {narrowN}{(narrowN > 0 ? ":" + narrowZones : "")}");
            LastDividerReport = sb.ToString();
            var gclock = System.Diagnostics.Stopwatch.StartNew();
            DividerGate(map);
            divGateMs = gclock.Elapsed.TotalMilliseconds;
            DividerGateMsTotal += divGateMs;
            if (System.Environment.GetEnvironmentVariable("PSX_DIV_RUNS") == "1") Debug.Log("[dividers] runs:" + divRunLog);
            divRunLog.Clear();
        }

        /// <summary>THE PLANNING GATE, on the plan as built (the cable's arcs after every cut,
        /// the pinches' Jerseys, the zones): at every DivStepM sample of every candidate
        /// carriageway off structure where the pair is not concrete on both sides, the barriers
        /// standing across the median - a concrete side's Jersey or a pinch's, a deck's parapet
        /// across, the cable on either side. Every sample has exactly one, is in an overpass
        /// zone, is a deliberately open pair, or a lone carriageway's own cable (no twin within
        /// DivReachM); NONE and TWO+ are listed with what each side's plan holds there.</summary>
        static void DividerGate(CityMap map)
        {
            var E = map.edges;
            var per = new SortedDictionary<string, float[]>();   // one, zone, open, NONE, TWO+, lone, pinch (km)
            var spots = new StringBuilder();
            var seenSpot = new HashSet<long>();
            int spotsN = 0;
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null) continue;
                if (!per.TryGetValue(e.name, out var a)) per[e.name] = a = new float[7];
                for (int k = 0; k < d.gap.Length; k++)
                {
                    float s = Mathf.Min((k + 0.5f) * DivStepM, e.length);
                    if (e.bridge || e.ElevatedAt(s)) continue;
                    int yi = d.twin[k];
                    var dy = yi >= 0 ? divEdges[yi] : null;
                    var ky = yi < 0 ? DividerKind.Open : dy?.kind ?? DividerKind.Concrete;
                    if (d.kind == DividerKind.Concrete && ky == DividerKind.Concrete) continue;
                    float m = Mathf.Min(DivStepM, e.length - k * DivStepM) / 1000f;
                    if (yi < 0)
                    {
                        a[CableAt(d, s) ? 5 : 3] += m;
                        continue;
                    }
                    bool twinDeck = E[yi].ElevatedAt(d.twinS[k]);
                    bool pinch = InArcs(d.pinchRun, s) || (dy != null && InArcs(dy.pinchRun, d.twinS[k]));
                    int cls;
                    bool cE = CableAt(d, s), cY = dy != null && !twinDeck && CableAt(dy, d.twinS[k]);
                    if (InDivZone(d, s, s, WBeamTermM) || (dy != null && InDivZone(dy, d.twinS[k], d.twinS[k], WBeamTermM))) cls = 1;
                    else if (pinch) cls = cE && cY ? 4 : 6;   // the Jersey (the cable laps behind its ends)
                    else if (twinDeck && cE && !cY && InArcs(d.laps, s)) cls = 0;   // the cable lapped behind the deck's parapet across
                    else
                    {
                        int n = (d.kind == DividerKind.Concrete ? 1 : 0) + (ky == DividerKind.Concrete || twinDeck ? 1 : 0) + (cE ? 1 : 0) + (cY ? 1 : 0);
                        cls = n == 1 ? 0 : n == 0 ? (d.kind == DividerKind.Open && ky == DividerKind.Open ? 2 : 3) : 4;
                    }
                    a[cls] += m;
                    if ((cls == 3 || cls == 4) && spotsN < 40 && seenSpot.Add(((long)e.index << 20) | (uint)(s / 60f)))
                    {
                        spotsN++;
                        var p = e.PointAt(s);
                        int ky0 = dy != null ? DivSample(dy, d.twinS[k]) : -1;
                        string why = InArcs(d.dropped, s) || (dy != null && InArcs(dy.dropped, d.twinS[k])) ? " (a cable piece under CableMinRunM dropped)" : "";
                        spots.Append($"\n    {(cls == 3 ? "NONE" : "TWO+")} e{e.index} s {s:0} '{e.name}' at ({p.x:0},{p.y:0}) {d.kind}/{ky} gap {d.gap[k]:0.0} draw {d.draw[k]} cable {cE}; twin e{yi} y {d.twinS[k]:0}{(twinDeck ? " on a deck" : "")} its sample {ky0} draw {(dy != null && ky0 >= 0 ? dy.draw[ky0].ToString() : "-")} cable {cY}{why}");
                    }
                }
            }
            var sb = new StringBuilder("divider gate (the plan as built: barriers across each candidate median, carriageway-km off structure):");
            foreach (var kv in per) sb.Append($" {kv.Key}: one {kv.Value[0]:0.00} zone {kv.Value[1]:0.00} pinch-Jersey {kv.Value[6]:0.00} open {kv.Value[2]:0.00} lone-carriageway cable {kv.Value[5]:0.00} NONE {kv.Value[3]:0.00} TWO+ {kv.Value[4]:0.00};");
            sb.Append(spots);
            LastDividerGate = sb.ToString();
        }

        static bool SegCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float ta, out float tc)
        {
            ta = tc = 0f;
            Vector2 r = b - a, s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-9f) return false;
            Vector2 w = c - a;
            ta = (w.x * s.y - w.y * s.x) / den;
            tc = (w.x * r.y - w.y * r.x) / den;
            return ta >= 0f && ta <= 1f && tc >= 0f && tc <= 1f;
        }

        // ------------------------------------------------------------------
        //  THE CABLE AND THE OVERPASS W-BEAMS (per edge, by the tile a piece's middle is in)
        // ------------------------------------------------------------------
        static readonly Vector3[] divProf = new Vector3[4];
        /// <summary>The verge the strip solver lays from a median edge point (world plan
        /// <paramref name="pW"/>, road height <paramref name="yEdge"/>) at <paramref name="off"/>
        /// metres out; NaN past its toe (the lattice there) or where it is refused.</summary>
        static float StripYAt(CityMap map, Trims trims, CityMap.Edge e, int side, Vector2 pW, Vector2 outw, float yEdge, float off)
        {
            GatherNear(map, pW, pW, VergeMaxRunM + RoadsideRules.ToeTuckRunM);
            var sh = new StripShape { shoulder = ShoulderOf(e, side), maxRun = VergeMaxRunM };
            if (!SolveStrip(map, trims, pW, yEdge, outw, sh, divProf)) return float.NaN;
            float prevD = 0f, prevY = yEdge;
            for (int k = 0; k < divProf.Length; k++)
            {
                var q = new Vector2(divProf[k].x, divProf[k].z);
                float dk = Vector2.Dot(q - pW, outw);
                if (dk >= off && dk > prevD + 1e-4f) return Mathf.Lerp(prevY, divProf[k].y, (off - prevD) / (dk - prevD));
                if (dk > prevD) { prevD = dk; prevY = divProf[k].y; }
            }
            return float.NaN;
        }

        /// <summary>The finished ground of the median at <paramref name="off"/> metres out from
        /// arc <paramref name="s"/>'s median edge point: the higher of the two carriageways'
        /// verges there (each falls into the ditch from its own edge; the other one's fill
        /// buried a cable measured from one side only), the lattice where neither reaches.
        /// The twin is the classification's own at the nearest sample (no search).</summary>
        static float MedianGroundY(CityMap map, Trims trims, CityMap.Edge e, float s, Vector2 pW, Vector2 outw, float yEdge, float off)
        {
            var p = pW + outw * off;
            float lat = LatticeY(map, p.x, p.y);
            float own = StripYAt(map, trims, e, 1, pW, outw, yEdge, off), twin = float.NaN;
            var d = DivOf(e);
            int k = DivSample(d, s);
            CityMap.Edge Y = k >= 0 && d.twin[k] >= 0 ? map.edges[d.twin[k]] : null;
            if (Y != null)
            {
                float y = Mathf.Clamp(d.twinS[k] - (s - Mathf.Min((k + 0.5f) * DivStepM, e.length)), 0f, Y.length);   // antiparallel: our step back is its step on
                var pY = Y.PointAt(y);
                int sy = SideToward(Y, y, p);
                LineModel.Extents(Y, y, out float ym, out float yp);
                var tY = Y.TangentAt(y);
                var nY = new Vector2(-tY.y, tY.x) * sy;   // toward the median from Y's line
                var eY = pY + nY * (sy > 0 ? yp : ym);
                float offY = Vector2.Dot(p - eY, nY);
                if (offY > 0.1f) twin = StripYAt(map, trims, Y, sy, eY, nY, Y.YAt(y), offY);
            }
            float g = lat;
            if (!float.IsNaN(own)) g = Mathf.Max(g, own);
            if (!float.IsNaN(twin)) g = Mathf.Max(g, twin);
            if (divDebugR > 0f && Vector2.Distance(p, divDebugAt) < divDebugR && divDebugN++ < 60)
                Debug.Log($"[dividers] e{e.index} s {s:0.0} at ({p.x:0.0},{p.y:0.0}) off {off:0.00} gap {DivGapAt(d, s):0.00} edgeY {yEdge:0.00} own {own:0.00} twin e{(Y != null ? Y.index : -1)} {twin:0.00} lattice {lat:0.00} -> {g:0.00}");
            return g;
        }
        static readonly float divDebugR = DebugR(out divDebugAt);
        static Vector2 divDebugAt;
        static int divDebugN;
        static float DebugR(out Vector2 at)
        {
            at = Vector2.zero;
            var v = System.Environment.GetEnvironmentVariable("PSX_DIV_DEBUG");
            if (string.IsNullOrEmpty(v)) return 0f;
            var f = v.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            at = new Vector2(float.Parse(f[0], ci), float.Parse(f[1], ci));
            return f.Length > 2 ? float.Parse(f[2], ci) : 60f;
        }

        static float CableOffAt(DivEdge d, float s)
        {
            float g = DivGapAt(d, s);
            if (float.IsNaN(g)) g = 10f;
            return Mathf.Max(0.5f * g - CableOffM, Mathf.Min(1.8f, 0.5f * g));
        }

        /// <summary>The median edge point of e at arc s (world, the ribbon's R vertex the way
        /// BuildSections lays it: the line model's extent, RightAt's direction and widening)
        /// and outward toward the median - for any arc, inside the junction fans too.</summary>
        static void DivEdgePoint(CityMap map, Trims trims, CityMap.Edge e, float s, out Vector3 ed, out Vector2 outw)
        {
            var p = e.PointAt(s);
            var right = RightAt(map, trims, e, s, out float widen);
            LineModel.Extents(e, s, out float em, out float ep);
            float off = 0.5f * (ep - em) + 0.5f * (em + ep) * widen;
            ed = new Vector3(p.x + right.x * off, e.YAt(s), p.y + right.y * off);
            outw = right;
        }

        public static int cableSpans, cablePosts, beamZonesDrawn;
        static readonly List<float> divBnd = new List<float>(256);
        static Vector3[] divP = new Vector3[256];
        static Vector2[] divO = new Vector2[256];
        static float[] divG = new float[256], divD = new float[256];
        static bool[] divMine = new bool[256];

        static bool InTile(Vector2 p, Vector2 min, Vector2 max) => p.x >= min.x && p.x < max.x && p.y >= min.y && p.y < max.y;

        /// <summary>Piece boundaries over [s0, s1]: both ends and every arc where the road's
        /// chain distance is a multiple of <paramref name="step"/> (posts line up across edges
        /// and tiles).</summary>
        static void DivBoundaries(CityMap.Edge e, float s0, float s1, float step)
        {
            divBnd.Clear();
            divBnd.Add(s0);
            float m0 = e.vOff + e.vDir * s0, m1 = e.vOff + e.vDir * s1;
            float lo = Mathf.Min(m0, m1), hi = Mathf.Max(m0, m1);
            int first = divBnd.Count;
            for (int m = Mathf.FloorToInt(lo / step) + 1; m * step < hi - 0.05f; m++)
            {
                float s = Mathf.Lerp(s0, s1, Mathf.InverseLerp(m0, m1, m * step));
                if (s > s0 + 0.05f && s < s1 - 0.05f) divBnd.Add(s);
            }
            if (m1 < m0) divBnd.Reverse(first, divBnd.Count - first);
            divBnd.Add(s1);
        }

        static void DivEnsure(int n)
        {
            if (divP.Length >= n) return;
            int m = Mathf.NextPowerOfTwo(n);
            divP = new Vector3[m]; divO = new Vector2[m]; divG = new float[m]; divD = new float[m]; divMine = new bool[m];
        }

        /// <summary>The cable and the overpass W-beams of one edge, the pieces whose middle is
        /// in this tile. A step every few ground solves (city-build-hitches).</summary>
        static IEnumerable<int> EmitDividerSteps(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            var d = DivOf(e);
            if (d == null || (d.ivs == null && d.zones == null)) yield break;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double spent = 0;
            var org = new Vector2(tm.origin.x, tm.origin.z);
            // the cable laps behind a median-side rail (its own or its twin's): it runs
            // CableLapM into the rail's run and is turned down there, the rail being the
            // median's barrier along it
            divCut.Clear();
            if (d.ivs != null) { DivSuppress(map, trims, e, d, divSupp); foreach (var iv0 in d.ivs) DivCutRails(iv0, divSupp, divCut); }
            foreach (var iv in divCut)
                {
                    DivBoundaries(e, iv.s0, iv.s1, CablePostM);
                    int nb = divBnd.Count;
                    DivEnsure(nb);
                    int jA = -1, jB = -1;
                    for (int j = 0; j + 1 < nb; j++)
                    {
                        divMine[j] = InTile(e.PointAt(0.5f * (divBnd[j] + divBnd[j + 1])), min, max);
                        if (divMine[j]) { if (jA < 0) jA = j; jB = j; }
                    }
                    if (jA < 0) continue;
                    // the points from jA to jB + 1, the ground every CableGroundEvery
                    int g0 = jA - jA % CableGroundEvery, g1 = Mathf.Min(nb - 1, jB + 1 + (CableGroundEvery - (jB + 1) % CableGroundEvery) % CableGroundEvery);
                    int solves = 0;
                    for (int j = g0; j <= g1; j++)
                    {
                        float s = divBnd[j];
                        DivEdgePoint(map, trims, e, s, out var ed, out var ow);
                        float off = CableOffAt(d, s);
                        divP[j] = new Vector3(ed.x + ow.x * off, ed.y, ed.z + ow.y * off);
                        divO[j] = ow;
                        divD[j] = Mathf.Min(s - iv.s0 + iv.extA, iv.s1 - s + iv.extB);
                        divG[j] = float.NaN;
                        if (j % CableGroundEvery == 0 || j == g1 || j == nb - 1 || j == 0)
                        {
                            divG[j] = MedianGroundY(map, trims, e, s, new Vector2(ed.x, ed.z), ow, ed.y, off);
                            if (++solves % 6 == 0) { spent += clock.Elapsed.TotalMilliseconds; yield return 0; clock.Restart(); }
                        }
                    }
                    for (int j = g0; j <= g1; j++)
                    {
                        if (!float.IsNaN(divG[j])) continue;
                        int a = j - 1; while (float.IsNaN(divG[a])) a--;
                        int b = j + 1; while (float.IsNaN(divG[b])) b++;
                        divG[j] = Mathf.Lerp(divG[a], divG[b], (divBnd[j] - divBnd[a]) / Mathf.Max(1e-3f, divBnd[b] - divBnd[a]));
                    }
                    for (int j = g0; j <= g1; j++) divP[j] = new Vector3(divP[j].x - org.x, divG[j], divP[j].z - org.y);
                    for (int j = jA; j <= jB; j++)
                        if (divMine[j]) EmitCablePiece(tm, j, nb);
                }
            if (d.zones != null)
                foreach (var z in d.zones)
                {
                    float z0 = Mathf.Max(0f, z.x - WBeamTermM), z1 = Mathf.Min(e.length, z.y + WBeamTermM);
                    if (z1 - z0 < 1f) continue;
                    beamPieces.Clear();
                    DivSubtract(z0, z1, DivElevated(e, 1f), beamPieces);
                    foreach (var r in beamPieces)
                    {
                        if (r.y - r.x < 1f) continue;
                        int pieces = Mathf.Max(1, Mathf.CeilToInt((r.y - r.x) / WBeamStepM));
                        DivEnsure(pieces + 1);
                        int jA = -1, jB = -1;
                        for (int j = 0; j < pieces; j++)
                        {
                            divMine[j] = InTile(e.PointAt(Mathf.Lerp(r.x, r.y, (j + 0.5f) / pieces)), min, max);
                            if (divMine[j]) { if (jA < 0) jA = j; jB = j; }
                        }
                        if (jA < 0) continue;
                        beamZonesDrawn++;
                        float g = 0f;
                        int solves = 0;
                        for (int j = jA; j <= jB + 1; j++)
                        {
                            float s = Mathf.Lerp(r.x, r.y, (float)j / pieces);
                            DivEdgePoint(map, trims, e, s, out var ed, out var ow);
                            float gap = DivGapAt(d, s); if (float.IsNaN(gap)) gap = 10f;
                            // (round 3) the flare stays BeamCableClearM in front of the cable line
                            // whichever side carries it (a 5.4-5.8 m median put the flared
                            // terminal's 1.82 m on the cable's 1.8 m inside the lap)
                            float cOff = CableOffAt(d, s);
                            float flareMax = Mathf.Clamp(Mathf.Min(0.5f * gap - 0.2f, Mathf.Min(cOff, gap - cOff) - BeamCableClearM) - BeamOffM, 0f, WBeamFlareM);
                            float u = Mathf.Max(0f, Mathf.Max(z.x - s, s - z.y));   // into the terminal
                            float off = BeamOffM + flareMax * Mathf.Clamp01(u / WBeamTermM);
                            if ((j - jA) % 2 == 0 || j == jB + 1)
                            {
                                g = MedianGroundY(map, trims, e, s, new Vector2(ed.x, ed.z), ow, ed.y, off);
                                if (++solves % 6 == 0) { spent += clock.Elapsed.TotalMilliseconds; yield return 0; clock.Restart(); }
                            }
                            else g = float.NaN;
                            float toTip = WBeamTermM - u;
                            divP[j] = new Vector3(ed.x + ow.x * off - org.x, g, ed.z + ow.y * off - org.y);
                            divO[j] = ow;
                            divD[j] = toTip >= WBeamDownM ? WBeamTopM : Mathf.Lerp(-0.15f, WBeamTopM, Mathf.Clamp01(toTip / WBeamDownM));
                        }
                        for (int j = jA + 1; j <= jB; j++)
                            if (float.IsNaN(divP[j].y)) divP[j].y = 0.5f * (divP[j - 1].y + divP[j + 1].y);
                        for (int j = jA; j <= jB; j++)
                        {
                            if (!divMine[j]) continue;
                            Vector3 fa = divP[j], fb = divP[j + 1];
                            float ta = fa.y + divD[j], tb = fb.y + divD[j + 1];
                            tm.wbeams.Add(new WBeamPiece
                            {
                                a = fa, b = fb, outA = divO[j], outB = divO[j + 1],
                                botA = ta - WBeamDepthM, topA = ta, botB = tb - WBeamDepthM, topB = tb,
                                endA = j == 0 && r.x <= z.x - WBeamTermM + 1e-3f, endB = j == pieces - 1 && r.y >= z.y + WBeamTermM - 1e-3f,
                            });
                            WBeamColliderPiece(fa, fb, divO[j], divO[j + 1], ta, tb, j == 0, j == pieces - 1);
                            // posts at the road's chain distance inside this piece
                            float sa = Mathf.Lerp(r.x, r.y, (float)j / pieces), sb = Mathf.Lerp(r.x, r.y, (float)(j + 1) / pieces);
                            float mA = e.vOff + e.vDir * sa, mB = e.vOff + e.vDir * sb;
                            float lo = Mathf.Min(mA, mB), hi = Mathf.Max(mA, mB);
                            for (int m = Mathf.FloorToInt(lo / WBeamPostM) + 1; m * WBeamPostM <= hi + 1e-4f; m++)
                            {
                                float t = Mathf.InverseLerp(mA, mB, m * WBeamPostM);
                                var fp = Vector3.Lerp(fa, fb, t);
                                float tp = Mathf.Lerp(ta, tb, t);
                                if (tp - fp.y < 0.35f) continue;   // turned down here
                                tm.wbeamPosts.Add(new WBeamPost { at = fp, outward = Vector2.Lerp(divO[j], divO[j + 1], t).normalized, top = tp - 0.03f });
                            }
                        }
                    }
                }
            spent += clock.Elapsed.TotalMilliseconds;
            DividerEmitMs += spent;
            if (spent > DividerEmitMaxMs) DividerEmitMaxMs = spent;
        }
        static readonly List<Vector2> beamPieces = new List<Vector2>(4);

        /// <summary>The cable's lap behind a rail at each end of the rail's run.</summary>
        const float CableLapM = 10f;
        static readonly Dictionary<int, List<Vector2>> divRails = new Dictionary<int, List<Vector2>>();
        static readonly List<Vector2> divSupp = new List<Vector2>(8), divSub = new List<Vector2>(8);
        static readonly List<CableIv> divCut = new List<CableIv>(8);
        static readonly HashSet<int> divTwinSet = new HashSet<int>();

        /// <summary>The tile's record of a divider edge's median-side rail runs off structure
        /// (BuildRoadsAndDecks, from the side flags it just drew from: decided from data for
        /// every span within FlagReachM, so every tile reads the same runs near its pieces).</summary>
        static void RecordDivRails(CityMap.Edge e, int n)
        {
            List<Vector2> l = null;
            for (int i = 1; i < n; i++)
            {
                var f = spanFlags[i];
                if (f.elev || f.skip || !f.r.rail) continue;
                float a = sections[i - 1].s, b = sections[i].s;
                l ??= new List<Vector2>();
                if (l.Count > 0 && a <= l[l.Count - 1].y + 0.5f) l[l.Count - 1] = new Vector2(l[l.Count - 1].x, b);
                else l.Add(new Vector2(a, b));
            }
            if (l != null) divRails[e.index] = l; else divRails.Remove(e.index);
        }

        /// <summary>The arcs of e where no cable stands: every median-side rail run of e or of
        /// its twin (projected onto e), less the lap at each end that is a real end of the rail
        /// (not an edge end, where the rail carries on).</summary>
        static void DivSuppress(CityMap map, Trims trims, CityMap.Edge e, DivEdge d, List<Vector2> into)
        {
            into.Clear();
            float lo = trims.atA[e.index] + 1f, hi = e.length - trims.atB[e.index] - 1f;
            void Add(float a, float b, bool lapA, bool lapB, int railEdge, float ra, float rb)
            {
                float a1 = lapA ? a + CableLapM : a, b1 = lapB ? b - CableLapM : b;
                if (b1 - a1 <= 0.05f) return;
                into.Add(new Vector2(a1, b1));
                // the audits' record of the designed laps: where the cable runs on behind
                // the rail's end and its lead-in (a W-beam's WBeamLenM, or the concrete's
                // sloped end inside that) - only where the plan's cable reaches the cut
                if (!DividerLapRecord) return;
                bool inA = lapA && CableAt(d, a1 - 0.5f), inB = lapB && CableAt(d, b1 + 0.5f);
                if (inA) DivLapAdd(e.index, a - WBeamLenM, a1 + 0.5f);
                if (inB) DivLapAdd(e.index, b1 - 0.5f, b + WBeamLenM);
                // ...and the rails the cable is cut and lapped around at both ends (the short
                // rail census exempts exactly these: they are the median's barrier there)
                // (an end running on onto structure is no end: the parapet carries on)
                if ((inA || inB) && (inA || !lapA) && (inB || !lapB)) { var lr = (railEdge, ra, rb); if (!DividerLappedRails.Contains(lr)) DividerLappedRails.Add(lr); }
            }
            if (divRails.TryGetValue(e.index, out var own))
                // a rail end where the road goes on onto structure is no end (its parapet carries on)
                foreach (var r in own) Add(r.x, r.y, r.x > lo && !e.ElevatedAt(r.x - 1f), r.y < hi && !e.ElevatedAt(r.y + 1f), e.index, r.x, r.y);
            divTwinSet.Clear();
            foreach (int t in d.twin) if (t >= 0) divTwinSet.Add(t);
            foreach (int t in divTwinSet)
            {
                if (!divRails.TryGetValue(t, out var tr)) continue;
                var Y = map.edges[t];
                float yLo = trims.atA[t] + 1f, yHi = Y.length - trims.atB[t] - 1f;
                foreach (var r in tr)
                {
                    Vector2 pa = Y.PointAt(r.x), pb = Y.PointAt(r.y);
                    CityElevation.ProjectOn(e, pa, out float sa);
                    CityElevation.ProjectOn(e, pb, out float sb);
                    if (Vector2.Distance(e.PointAt(sa), pa) > 80f && Vector2.Distance(e.PointAt(sb), pb) > 80f) continue;
                    bool lapA = r.x > yLo && !Y.ElevatedAt(r.x - 1f) && sa > 0.5f && sa < e.length - 0.5f, lapB = r.y < yHi && !Y.ElevatedAt(r.y + 1f) && sb > 0.5f && sb < e.length - 0.5f;
                    // antiparallel: the twin's start is our end
                    if (sa <= sb) Add(sa, sb, lapA, lapB, t, r.x, r.y); else Add(sb, sa, lapB, lapA, t, r.x, r.y);
                }
            }
            into.Sort((p, q) => p.x.CompareTo(q.x));
        }

        /// <summary>TILE-INDEPENDENT LAPS (round 3, the review's risk 5): the twin of a cable
        /// edge in this tile whose own spans this tile did not decide (it lies beyond the
        /// tile's edge set, in a median wider than ~35 m) has its median-side rails decided
        /// here too - its sections and side flags over the same FlagReachM window, drawn
        /// nowhere - so every tile cuts the cable behind the same rails near its pieces.</summary>
        static IEnumerable<int> DivTwinRailsSteps(CityMap map, Trims trims, TileMeshes tm, HashSet<int> inTile, Vector2 min, Vector2 max)
        {
            divTwinRailDone.Clear();
            foreach (int ei in inTile)
            {
                var d = divEdges[ei];
                if (d?.ivs == null) continue;
                foreach (int t in d.twin)
                {
                    if (t < 0 || inTile.Contains(t) || !divTwinRailDone.Add(t)) continue;
                    var Y = map.edges[t];
                    float sMin = trims.atA[t], sMax = Y.length - trims.atB[t];
                    if (sMax - sMin < 0.6f) continue;
                    divTwinTm ??= new TileMeshes();
                    divTwinTm.origin = tm.origin;
                    foreach (var _ in BuildSectionsSteps(map, trims, divTwinTm, Y, sMin, sMax, FineSteps ? WarmEvery : 0)) yield return 0;
                    if (sections.Count < 2) { divRails.Remove(t); continue; }
                    foreach (var _ in DecideSideFlagsSteps(map, trims, divTwinTm, Y, min, max)) yield return 0;
                    RecordDivRails(Y, sections.Count);
                    divTwinRails++;
                }
            }
        }
        static readonly HashSet<int> divTwinRailDone = new HashSet<int>();
        static TileMeshes divTwinTm;
        public static int divTwinRails;

        /// <summary>Median rails the cable was cut and lapped around at both ends (their edge
        /// and arcs), recorded with <see cref="DividerLaps"/>.</summary>
        public static readonly List<(int edge, float s0, float s1)> DividerLappedRails = new List<(int, float, float)>();
        public static bool RailLappedByCable(CityMap.Edge e, float s0, float s1)
        {
            foreach (var r in DividerLappedRails) if (r.edge == e.index && s0 >= r.s0 - 1.5f && s1 <= r.s1 + 1.5f) return true;
            return false;
        }

        /// <summary>DecideSideFlags' stray median rails: on a cable edge's median side (R), a
        /// rail run off structure shorter than 2 x CableLapM, with nothing beside either end
        /// (no other barrier, union or gap) and the plan's cable standing past both ends (on
        /// this edge or the twin across), is cleared - the cable is the barrier there.</summary>
        static void StrayMedianRails(CityMap.Edge e, int n)
        {
            var d = DivOf(e);
            if (d == null || d.kind != DividerKind.Cable) return;
            const int side = 1;
            for (int i = 1; i < n;)
            {
                if (!spanFlags[i][side].rail) { i++; continue; }
                int j = i;
                bool anyElev = false;
                while (j < n && spanFlags[j][side].rail) { anyElev |= spanFlags[j].elev; j++; }
                float r0 = sections[i - 1].s, r1 = sections[j - 1].s;
                bool Bare(int k) { var b = spanFlags[k][side]; return !(b.rail || b.cut || b.median || b.union || b.gap); }
                if (!anyElev && i > 1 && j < n && spanFlags[i].decided && spanFlags[j - 1].decided && Bare(i - 1) && Bare(j)
                    && r1 - r0 < 2f * CableLapM && CablePast(e, d, r0 - 3f) && CablePast(e, d, r1 + 3f))
                {
                    for (int k = i; k < j; k++)
                    {
                        var fk = spanFlags[k]; var sk = fk[side];
                        sk.rail = false; sk.retain = false;
                        fk[side] = sk; spanFlags[k] = fk;
                    }
                    divStrayRails++;
                }
                i = j;
            }
        }
        public static int divStrayRails;
        /// <summary>The plan's cable at arc s of e, on e or across on its twin.</summary>
        static bool CablePast(CityMap.Edge e, DivEdge d, float s)
        {
            s = Mathf.Clamp(s, 0f, e.length);
            if (CableAt(d, s)) return true;
            int k = DivSample(d, s);
            if (k < 0 || d.twin[k] < 0) return false;
            var dy = divEdges[d.twin[k]];
            float y = d.twinS[k] - (s - Mathf.Min((k + 0.5f) * DivStepM, e.length));
            return dy != null && CableAt(dy, Mathf.Clamp(y, 0f, divMap.edges[d.twin[k]].length));
        }

        static void DivLapAdd(int ei, float a, float b)
        {
            if (!DividerLaps.TryGetValue(ei, out var l)) DividerLaps[ei] = l = new List<Vector2>();
            foreach (var x in l) if (Mathf.Abs(x.x - a) < 0.05f && Mathf.Abs(x.y - b) < 0.05f) return;
            l.Add(new Vector2(a, b));
        }

        /// <summary>One cable arc less the rails' arcs; a piece left shorter than CableMinRunM
        /// between two cuts is not built.</summary>
        static void DivCutRails(CableIv iv, List<Vector2> supp, List<CableIv> into)
        {
            DivSubtract(iv.s0, iv.s1, supp, divSub);
            foreach (var p in divSub)
            {
                var c = new CableIv { s0 = p.x, s1 = p.y, contA = iv.contA && p.x <= iv.s0 + 1e-3f, contB = iv.contB && p.y >= iv.s1 - 1e-3f };
                c.extA = p.x <= iv.s0 + 1e-3f ? iv.extA : 0f;
                c.extB = p.y >= iv.s1 - 1e-3f ? iv.extB : 0f;
                if (c.extA + (c.s1 - c.s0) + c.extB < CableMinRunM) continue;
                into.Add(c);
            }
        }

        /// <summary>One 4 m piece of the cable (points divP, tile-local, on the ground): the four
        /// cables, the post at its start, the anchor at a real end, the collider strip.</summary>
        static void EmitCablePiece(TileMeshes tm, int j, int nb)
        {
            var bk = structBucket;
            cableSpans++;
            Vector3 a = divP[j], b = divP[j + 1];
            float dA = divD[j], dB = divD[j + 1];
            var o3 = new Vector3(divO[j].x + divO[j + 1].x, 0f, divO[j].y + divO[j + 1].y).normalized;
            float H(float h, float dd) => dd >= CableTermM ? h : Mathf.Lerp(0.12f, h, Mathf.Clamp01(dd / CableTermM));
            var gv0 = new Vector2(CellGalv0, BandMisc0); var gv1 = new Vector2(CellGalv0, BandMisc1); var gv2 = new Vector2(CellGalv1, BandMisc1); var gv3 = new Vector2(CellGalv1, BandMisc0);
            // four cables: ribbons with a face to each side and a top, drawn a little over the
            // 19 mm rope (CableDrawM): at the game's resolution a true-size cable vanished
            foreach (float h in CableH)
            {
                float ha = H(h, dA), hb = H(h, dB);
                var w = o3 * (0.5f * CableDrawM);
                var a0 = a + Vector3.up * (ha - 0.5f * CableDrawM); var a1 = a + Vector3.up * (ha + 0.5f * CableDrawM);
                var b0 = b + Vector3.up * (hb - 0.5f * CableDrawM); var b1 = b + Vector3.up * (hb + 0.5f * CableDrawM);
                bk.Face(a0 + w, a1 + w, b1 + w, b0 + w, o3, gv0, gv1, gv2, gv3);
                bk.Face(a0 - w, a1 - w, b1 - w, b0 - w, -o3, gv0, gv1, gv2, gv3);
                bk.Face(a1 - w, a1 + w, b1 + w, b1 - w, Vector3.up, gv0, gv1, gv2, gv3);
            }
            // the collider: one closed box strip along the line, following the turn-down;
            // end faces only at the cable's real ends (inner caps between pieces were hit by
            // oblique rays as a second wall)
            float ta = Mathf.Max(0.15f, H(CableColliderTopM, dA)), tb = Mathf.Max(0.15f, H(CableColliderTopM, dB));
            CableColliderPiece(a, b, o3, a.y - 0.3f, a.y + ta, b.y - 0.3f, b.y + tb, j == 0 && dA <= 0.01f, j == nb - 2 && dB <= 0.01f);
            // the post at the piece's start (interior boundaries are at chain-distance multiples)
            if (j > 0 && dA >= 1.5f)
            {
                var along = b - a; along.y = 0f; along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
                var side = new Vector3(-along.z, 0f, along.x);
                float pt = H(CablePostTopM, dA);
                EmitBlock(bk, a + Vector3.down * 0.5f, a + Vector3.up * pt, along * CablePostHalfM, side * (0.8f * CablePostHalfM), CellGalv0, CellGalv1, BandMisc0, BandMisc1);
                // the post's reflective delineator head (NCDOT cable posts carry one): at the
                // game's resolution it is what reads the run from the lanes, post by post
                if (dA >= CableTermM) EmitBlock(bk, a + Vector3.up * (pt - 0.16f), a + Vector3.up * (pt + 0.02f), along * (CablePostHalfM + 0.015f), side * (0.8f * CablePostHalfM + 0.015f), DelinU0, DelinU1, DelinV0, DelinV1);
                cablePosts++;
            }
            // the anchors at the cable's real ends
            for (int end = 0; end < 2; end++)
            {
                bool isEnd = end == 0 ? j == 0 && dA <= 0.01f : j == nb - 2 && dB <= 0.01f;
                if (!isEnd) continue;
                var p = end == 0 ? a : b;
                var along = b - a; along.y = 0f; along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
                var side = new Vector3(-along.z, 0f, along.x);
                EmitBlock(bk, p + Vector3.down * 0.4f, p + Vector3.up * 0.2f, along * 0.45f, side * 0.35f, 0f, 0.25f, BandWall0, BandWall1);
            }
        }

        static void CableColliderPiece(Vector3 a, Vector3 b, Vector3 o3, float lowA, float topA, float lowB, float topB, bool capA, bool capB)
        {
            var bk = guardBucket;
            var n = new Vector2(o3.x, o3.z);
            var off = o3 * CableColliderHalfM;
            Vector3 fa = a - off, fb = b - off, ba = a + off, bb = b + off;
            bk.WallSloped(fa, fb, lowA, topA, lowB, topB, -n, 0f, 1f, 0f, 1f);
            bk.WallSloped(ba, bb, lowA, topA, lowB, topB, n, 0f, 1f, 0f, 1f);
            bk.Up(new Vector3(fa.x, topA, fa.z), new Vector3(fb.x, topB, fb.z), new Vector3(bb.x, topB, bb.z), new Vector3(ba.x, topA, ba.z),
                  Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var along = new Vector3(b.x - a.x, 0f, b.z - a.z);
            if (capA)
                bk.Face(new Vector3(fa.x, lowA, fa.z), new Vector3(fa.x, topA, fa.z), new Vector3(ba.x, topA, ba.z), new Vector3(ba.x, lowA, ba.z),
                        -along, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            if (capB)
                bk.Face(new Vector3(fb.x, lowB, fb.z), new Vector3(fb.x, topB, fb.z), new Vector3(bb.x, topB, bb.z), new Vector3(bb.x, lowB, bb.z),
                        along, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        }

        /// <summary>For the crash check (Editor CableCrashSim): the collider mesh of a straight
        /// cable from a to b (on flat ground at their y), built by the same pieces as a tile's,
        /// with turned-down ends.</summary>
        public static Mesh CableColliderTestMesh(Vector3 a, Vector3 b)
        {
            guardBucket.Clear();
            float len = Vector3.Distance(a, b);
            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / CablePostM));
            var o = Vector3.Cross(Vector3.up, (b - a).normalized);
            for (int q = 0; q < pieces; q++)
            {
                float t0 = (float)q / pieces, t1 = (float)(q + 1) / pieces;
                Vector3 pa = Vector3.Lerp(a, b, t0), pb = Vector3.Lerp(a, b, t1);
                float dA = Mathf.Min(t0, 1f - t0) * len, dB = Mathf.Min(t1, 1f - t1) * len;
                float H(float dd) => Mathf.Max(0.15f, dd >= CableTermM ? CableColliderTopM : Mathf.Lerp(0.12f, CableColliderTopM, dd / CableTermM));
                CableColliderPiece(pa, pb, o, pa.y - 0.3f, pa.y + H(dA), pb.y - 0.3f, pb.y + H(dB), q == 0, q == pieces - 1);
            }
            var m = MeshFromBucket("cable collider test", guardBucket);
            guardBucket.Clear();
            return m;
        }
    }
}
