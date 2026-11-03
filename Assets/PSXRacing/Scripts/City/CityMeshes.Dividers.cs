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
    /// section - from the run's median width (the median of its samples) with hysteresis,
    /// so it changes only at a run boundary:
    ///
    ///   CONCRETE  the narrow medians (and every corridor in <see cref="ConcreteCorridors"/>,
    ///             whose medians are concrete in the real road: I-77, I-85, I-277, US 74):
    ///             exactly as before - the shared Jersey, the union strip, two walls.
    ///   CABLE     a grass median: the median verge grades to the ditch like any roadside
    ///             (and a drop it cannot grade still gets its W-beam), and ONE high-tension
    ///             four-cable run on posts stands <see cref="CableOffM"/> off the median's
    ///             centre toward one carriageway (the chain with the lower edge index - the
    ///             same side all the way along the chain), continuous through the run, turned
    ///             down to an anchor at its ends. Its collider is a box strip 0.5 m thick.
    ///   OPEN      wider than <see cref="DivOpenEnterM"/>: grass, no barrier.
    ///
    /// Where a road on structure crosses over a CABLE median (an overpass: its piers stand in
    /// the median) the cable ends and each carriageway gets a W-beam on its median side over
    /// the crossing and its length of need, flared away and turned down at both ends
    /// (WBeamFlareM / WBeamTermM): the double barrier around the piers in the owner's photo.
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

        /// <summary>The width rule (metres of unpaved median between the two paved edges).
        /// OSM draws I-485's west and north arcs at a nominal ~9.1-9.5 m apart where the real
        /// median is wide grass with cable, so the narrow cut sits well below that peak: a run
        /// becomes Concrete below <see cref="DivNarrowLeaveM"/> and only leaves it again above
        /// <see cref="DivNarrowEnterM"/>; Open past <see cref="DivOpenEnterM"/>, back to cable
        /// below <see cref="DivOpenLeaveM"/>. A run shorter than <see cref="DivMinRunM"/> keeps
        /// the kind of the run before it.</summary>
        const float DivStepM = 10f, DivReachM = 45f, DivNarrowLeaveM = 5f, DivNarrowEnterM = 7f,
                    DivOpenEnterM = 24f, DivOpenLeaveM = 18f, DivStartOpenM = 21f, DivMinRunM = 400f;
        /// <summary>The cable: off the median's centre toward its carriageway, posts every
        /// CablePostM, four cables (<see cref="CableH"/>), turned down to the anchor over
        /// CableTermM at each end of a run.</summary>
        public const float CableOffM = 1.2f, CablePostM = 4f, CableTermM = 8f, CablePostTopM = 1.02f,
                           CableColliderHalfM = 0.25f, CableColliderTopM = 0.95f, CableMinGapM = 3f,
                           CableDrawM = 0.07f, CablePostHalfM = 0.1f;
        static readonly float[] CableH = { 0.5f, 0.65f, 0.8f, 0.95f };
        /// <summary>A crossing deck's W-beam: its length of need past the deck's edge each way,
        /// and the cable's clearance past the beam's buried ends.</summary>
        const float BeamNeedM = 8f, CableBeamClearM = 3f;

        class DivEdge
        {
            public DividerKind kind;
            public float[] gap;         // per DivStepM sample, NaN where no twin was found
            public bool drawer;         // this carriageway carries the pair's cable
            public bool contA, contB;   // the cable carries on through the node at that end
            public int chain;
            public List<Vector2> zones; // crossing decks over the median: core (s0, s1)
        }
        static DivEdge[] divEdges;
        public static string LastDividerReport { get; private set; } = "";
        static readonly HashSet<int> divSegs = new HashSet<int>();
        static readonly StringBuilder divRunLog = new StringBuilder();

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

        /// <summary>Does this span carry the cable (the drawer's side, out of the overpass
        /// zones, with room for it)?</summary>
        static bool CableSpanAt(CityMap.Edge e, float s0, float s1)
        {
            var d = DivOf(e);
            if (d == null || d.kind != DividerKind.Cable || !d.drawer) return false;
            if (InDivZone(d, s0, s1, WBeamTermM + CableBeamClearM)) return false;
            float g = DivGapAt(d, 0.5f * (s0 + s1));
            return !float.IsNaN(g) && g >= CableMinGapM;
        }

        /// <summary>Does this span carry an overpass zone's median W-beam?</summary>
        static bool BeamSpanAt(CityMap.Edge e, float s0, float s1)
        {
            var d = DivOf(e);
            return d != null && d.kind == DividerKind.Cable && InDivZone(d, s0, s1, WBeamTermM);
        }

        // ------------------------------------------------------------------
        //  THE CLASSIFICATION (once per trims build, after the structure)
        // ------------------------------------------------------------------
        /// <summary>The opposing twin beside arc <paramref name="x"/> of X on its median side
        /// and the unpaved width between the two (LineModel extents).</summary>
        static bool DivTwinAt(CityMap map, CityMap.Edge X, float x, out CityMap.Edge Y, out float y, out float gap)
        {
            Y = null; y = 0f; gap = float.NaN;
            var p = X.PointAt(x);
            var tan = X.TangentAt(x);
            var left = new Vector2(-tan.y, tan.x);
            float r = X.HalfMax + DivReachM + 15f, best = float.MaxValue;
            divSegs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, divSegs);
            foreach (int seg in divSegs)
            {
                var c = map.edges[seg >> 12];
                if (c == X || c.a == c.b || !c.oneway || c.link || c.name != X.name) continue;
                int si = seg & 0xFFF;
                if (si + 1 >= c.pts.Length) continue;
                Vector2 a0 = c.pts[si], dseg = c.pts[si + 1] - a0;
                float L2 = dseg.sqrMagnitude;
                if (L2 < 1e-8f) continue;
                float L = Mathf.Sqrt(L2);
                var dirC = dseg / L;
                if (Vector2.Dot(tan, dirC) > -0.5f) continue;
                float along = Vector2.Dot(p - a0, dirC);
                bool first = si == 0, lastSeg = si + 2 >= c.pts.Length;
                if (along < -(first ? UnionFootSlackM : 0.5f) || along > L + (lastSeg ? UnionFootSlackM : 0.5f)) continue;
                float tcl = Mathf.Clamp(along, 0f, L);
                var q = a0 + dirC * tcl;
                if (Vector2.Dot(q - p, left) <= 0f) continue;   // the median is on the left of travel
                float dd = Vector2.Distance(p, q);
                if (dd < best) { best = dd; Y = c; y = c.s[si] + tcl; }
            }
            if (Y == null) return false;
            LineModel.Extents(X, x, out _, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            gap = best - xp - (SideToward(Y, y, p) > 0 ? yp : ym);
            if (best > X.HalfMax + Y.HalfMax + DivReachM) { Y = null; gap = float.NaN; return false; }
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
            if (found < 0.3f || float.IsNaN(med)) return state ?? DividerKind.Open;   // no twin beside: apart
            if (state == null)
                return med < 0.5f * (DivNarrowLeaveM + DivNarrowEnterM) ? DividerKind.Concrete : med > DivStartOpenM ? DividerKind.Open : DividerKind.Cable;
            if (len < DivMinRunM) return state.Value;
            switch (state.Value)
            {
                case DividerKind.Concrete: return med > DivNarrowEnterM ? (med > DivOpenEnterM ? DividerKind.Open : DividerKind.Cable) : DividerKind.Concrete;
                case DividerKind.Cable: return med < DivNarrowLeaveM ? DividerKind.Concrete : med > DivOpenEnterM ? DividerKind.Open : DividerKind.Cable;
                default: return med < DivNarrowLeaveM ? DividerKind.Concrete : med < DivOpenLeaveM ? DividerKind.Cable : DividerKind.Open;
            }
        }

        class DivRun { public List<CityMap.Edge> edges = new List<CityMap.Edge>(); public float len, med, found; public DividerKind kind; }

        /// <summary>Classify every candidate carriageway (called by BuildDeckUnions once the
        /// structure is marked, before the ground-twin walk reads it).</summary>
        static void BuildDividers(CityMap map)
        {
            divEdges = null;
            LastDividerReport = DividersOn ? "" : "dividers off (PSX_CITY_DIVIDERS=0)";
            if (!DividersOn || map?.edges == null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var E = map.edges;
            divEdges = new DivEdge[E.Length];
            // samples
            foreach (var e in E)
            {
                if (!DivCandidate(e)) continue;
                int ns = Mathf.Max(1, Mathf.CeilToInt(e.length / DivStepM));
                var d = new DivEdge { gap = new float[ns], chain = -1, kind = DividerKind.Concrete };
                for (int k = 0; k < ns; k++)
                {
                    float s = Mathf.Min((k + 0.5f) * DivStepM, e.length);
                    d.gap[k] = DivTwinAt(map, e, s, out _, out _, out float g) ? g : float.NaN;
                }
                divEdges[e.index] = d;
            }
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
            int runsN = 0;
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
                    foreach (var x in rr.edges) divEdges[x.index].kind = rr.kind;
                    divRunLog.Append($"\n  run {rr.edges[0].name} e{rr.edges[0].index}..e{rr.edges[rr.edges.Count - 1].index} {rr.len:0} m median {rr.med:0.0} m found {rr.found:0.00} -> {rr.kind}");
                }
                runsN += runs.Count;
            }
            // ONE KIND A PAIR: each carriageway's runs are decided on its own, and their
            // hysteresis can part across the median (one side open, the twin cable). An
            // edge takes the more barriered of its own kind and its twins' majority (each
            // side reads the other's own kind, so a one-to-one pair always agrees)
            var own = new DividerKind[E.Length];
            foreach (var e in E) if (divEdges[e.index] != null) own[e.index] = divEdges[e.index].kind;
            int reconciled = 0;
            var tally = new int[3];
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null) continue;
                tally[0] = tally[1] = tally[2] = 0;
                for (int k = 0; k < d.gap.Length; k += 2)
                {
                    float s = Mathf.Min((k + 0.5f) * DivStepM, e.length);
                    if (DivTwinAt(map, e, s, out var Y, out _, out _) && Y != null && divEdges[Y.index] != null) tally[(int)own[Y.index]]++;
                }
                int tk = tally[0] >= tally[1] && tally[0] >= tally[2] ? 0 : tally[1] >= tally[2] ? 1 : 2;
                if (tally[tk] == 0) continue;
                var kk = (DividerKind)Mathf.Min((int)own[e.index], tk);
                if (kk != d.kind) { d.kind = kk; reconciled++; }
            }
            // the drawer: the chain with the lower key, by the edge's samples' majority
            int pairsBoth = 0;
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null) continue;
                int votes = 0, n = 0;
                for (int k = 0; k < d.gap.Length; k += 2)
                {
                    float s = Mathf.Min((k + 0.5f) * DivStepM, e.length);
                    if (!DivTwinAt(map, e, s, out var Y, out _, out _) || Y == null) continue;
                    var dy = divEdges[Y.index];
                    if (dy == null || dy.chain < 0) continue;
                    n++;
                    if (chainKey[d.chain] < chainKey[dy.chain]) votes++;
                }
                d.drawer = n > 0 && votes * 2 > n;
            }
            // continuity through nodes, the overpass zones
            int zonesN = 0;
            var seen = new HashSet<long>();
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null) continue;
                var nx = DivNext(map, e); var pv = DivPrev(map, e);
                d.contB = nx != null && divEdges[nx.index].kind == d.kind && divEdges[nx.index].drawer == d.drawer;
                d.contA = pv != null && divEdges[pv.index].kind == d.kind && divEdges[pv.index].drawer == d.drawer;
                if (d.kind != DividerKind.Cable) continue;
                seen.Clear();
                for (int k = 0; k + 1 < e.pts.Length; k++)
                {
                    Vector2 a = e.pts[k], b = e.pts[k + 1];
                    divSegs.Clear();
                    map.EdgeSegsInRect(Vector2.Min(a, b) - Vector2.one * 2f, Vector2.Max(a, b) + Vector2.one * 2f, divSegs);
                    foreach (int seg in divSegs)
                    {
                        var c = map.edges[seg >> 12];
                        int si = seg & 0xFFF;
                        if (c == e || c.name == e.name || si + 1 >= c.pts.Length || !seen.Add(((long)c.index << 20) ^ ((long)si << 8) ^ k)) continue;
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
                if (d.zones != null && ((d.zones[0].x <= WBeamTermM + CableBeamClearM) || false)) d.contA = false;
                if (d.zones != null && d.zones[d.zones.Count - 1].y >= e.length - WBeamTermM - CableBeamClearM) d.contB = false;
            }
            // the report: carriageway-km of each kind per corridor
            var km = new SortedDictionary<string, float[]>();
            int drawers = 0;
            foreach (var e in E)
            {
                var d = divEdges[e.index];
                if (d == null) continue;
                if (!km.TryGetValue(e.name, out var a)) km[e.name] = a = new float[3];
                a[(int)d.kind] += e.length / 1000f;
                if (d.drawer && d.kind == DividerKind.Cable) drawers++;
            }
            var sb = new StringBuilder();
            sb.Append($"dividers: {chains.Count} chains, {runsN} runs, {reconciled} edges given their twin's kind, {zonesN} overpass crossings, {drawers} cable-drawing edges, {clock.ElapsedMilliseconds} ms;");
            foreach (var kv in km) sb.Append($" {kv.Key}: concrete {kv.Value[0]:0.0} cable {kv.Value[1]:0.0} open {kv.Value[2]:0.0} km;");
            LastDividerReport = sb.ToString();
            Debug.Log("[city] " + LastDividerReport);
            if (System.Environment.GetEnvironmentVariable("PSX_DIV_RUNS") == "1") Debug.Log("[dividers] runs:" + divRunLog);
            divRunLog.Clear();
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
        //  THE CABLE AND THE OVERPASS W-BEAMS (per span, by the tile that draws it)
        // ------------------------------------------------------------------
        /// <summary>Distance along the run to the cable's nearest turned-down end at each
        /// section (+inf where it carries on), from the spans' flags alone.</summary>
        static readonly List<float> cableDn = new List<float>(64);
        static void PlanCableEnds(CityMap.Edge e)
        {
            int n = sections.Count;
            cableDn.Clear();
            for (int k = 0; k < n; k++) cableDn.Add(float.PositiveInfinity);
            var d = DivOf(e);
            if (d == null || d.kind != DividerKind.Cable) return;
            for (int i = 1; i < n;)
            {
                if (!spanFlags[i].r.cable) { i++; continue; }
                int j = i;
                while (j < n && spanFlags[j].r.cable) j++;
                bool startEnd = i > 1 || !d.contA, finishEnd = j < n || !d.contB;
                float s0 = sections[i - 1].s, s1 = sections[j - 1].s;
                for (int k = i - 1; k <= j - 1; k++)
                {
                    float dd = float.PositiveInfinity;
                    if (startEnd) dd = Mathf.Min(dd, sections[k].s - s0);
                    if (finishEnd) dd = Mathf.Min(dd, s1 - sections[k].s);
                    cableDn[k] = Mathf.Max(0f, dd);
                }
                i = j;
            }
        }

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
        /// buried a cable measured from one side only), the lattice where neither reaches.</summary>
        static float MedianGroundY(CityMap map, Trims trims, CityMap.Edge e, float s, Vector2 pW, Vector2 outw, float yEdge, float off)
        {
            var p = pW + outw * off;
            float lat = LatticeY(map, p.x, p.y);
            float own = StripYAt(map, trims, e, 1, pW, outw, yEdge, off), twin = float.NaN;
            if (DivTwinAt(map, e, s, out var Y, out float y, out _) && Y != null)
            {
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
                Debug.Log($"[dividers] e{e.index} s {s:0.0} at ({p.x:0.0},{p.y:0.0}) off {off:0.00} gap {DivGapAt(DivOf(e), s):0.00} edgeY {yEdge:0.00} own {own:0.00} twin e{(Y != null ? Y.index : -1)} {twin:0.00} lattice {lat:0.00} -> {g:0.00}");
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

        public static int cableSpans, cablePosts, beamZonesDrawn;

        static void EmitCableSpan(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int i)
        {
            var d = DivOf(e);
            if (d == null) return;
            var A = sections[i - 1]; var B = sections[i];
            Vector3 eA = A.Edge(1), eB = B.Edge(1);
            Vector2 oA = A.Out(1), oB = B.Out(1);
            float len = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            if (len < 0.05f) return;
            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / CablePostM));
            float dA = cableDn[i - 1], dB = cableDn[i];
            var bk = structBucket;
            cableSpans++;
            // the line's points
            var P = new Vector3[pieces + 1];
            var D = new float[pieces + 1];
            var O = new Vector2[pieces + 1];
            for (int q = 0; q <= pieces; q++)
            {
                float t = (float)q / pieces;
                var ed = Vector3.Lerp(eA, eB, t);
                var ow = Vector2.Lerp(oA, oB, t).normalized;
                float s = Mathf.Lerp(A.s, B.s, t);
                float off = CableOffAt(d, s);
                var pW = new Vector2(ed.x + tm.origin.x, ed.z + tm.origin.z);
                float gy = MedianGroundY(map, trims, e, s, pW, ow, ed.y, off);
                P[q] = new Vector3(ed.x + ow.x * off, gy, ed.z + ow.y * off);
                O[q] = ow;
                D[q] = float.IsInfinity(dA) && float.IsInfinity(dB) ? float.PositiveInfinity
                     : float.IsInfinity(dA) ? dB + (1f - t) * len : float.IsInfinity(dB) ? dA + t * len : Mathf.Lerp(dA, dB, t);
            }
            if (divDebugR > 0f && Vector2.Distance(new Vector2(P[0].x + tm.origin.x, P[0].z + tm.origin.z), divDebugAt) < divDebugR)
                Debug.Log($"[dividers] cable span e{e.index} i{i} s {A.s:0.0}..{B.s:0.0} tile ({tm.origin.x:0},{tm.origin.z:0}) P0 ({P[0].x + tm.origin.x:0.0},{P[0].y:0.00},{P[0].z + tm.origin.z:0.0}) edgeY {eA.y:0.00} dA {dA:0.0} dB {dB:0.0} pieces {pieces} bucket {bk.Count}");
            float H(float h, float dd) => dd >= CableTermM ? h : Mathf.Lerp(0.12f, h, Mathf.Clamp01(dd / CableTermM));
            var gv0 = new Vector2(CellGalv0, BandMisc0); var gv1 = new Vector2(CellGalv0, BandMisc1); var gv2 = new Vector2(CellGalv1, BandMisc1); var gv3 = new Vector2(CellGalv1, BandMisc0);
            for (int q = 0; q < pieces; q++)
            {
                Vector3 a = P[q], b = P[q + 1];
                var o3 = new Vector3(O[q].x + O[q + 1].x, 0f, O[q].y + O[q + 1].y).normalized;
                // four cables: thin ribbons with a face to each side and a top
                foreach (float h in CableH)
                {
                    float ha = H(h, D[q]), hb = H(h, D[q + 1]);
                    // drawn a little over the 19 mm rope (CableDrawM): at the game's
                    // resolution a true-size cable vanished into the grass
                    var w = o3 * (0.5f * CableDrawM);
                    var a0 = a + Vector3.up * (ha - 0.5f * CableDrawM); var a1 = a + Vector3.up * (ha + 0.5f * CableDrawM);
                    var b0 = b + Vector3.up * (hb - 0.5f * CableDrawM); var b1 = b + Vector3.up * (hb + 0.5f * CableDrawM);
                    bk.Face(a0 + w, a1 + w, b1 + w, b0 + w, o3, gv0, gv1, gv2, gv3);
                    bk.Face(a0 - w, a1 - w, b1 - w, b0 - w, -o3, gv0, gv1, gv2, gv3);
                    bk.Face(a1 - w, a1 + w, b1 + w, b1 - w, Vector3.up, gv0, gv1, gv2, gv3);
                }
                // the collider: one closed box strip along the line
                float ta = H(CableColliderTopM, D[q]), tb = H(CableColliderTopM, D[q + 1]);
                CableColliderPiece(a, b, o3, a.y - 0.3f, a.y + Mathf.Max(0.35f, ta), b.y - 0.3f, b.y + Mathf.Max(0.35f, tb));
            }
            // posts at the road's chain distance, so the spacing runs on across spans and tiles
            float mA = e.vOff + e.vDir * A.s, mB = e.vOff + e.vDir * B.s;
            float lo = Mathf.Min(mA, mB), hi = Mathf.Max(mA, mB);
            for (int m = Mathf.FloorToInt(lo / CablePostM) + 1; m * CablePostM <= hi + 1e-4f; m++)
            {
                float t = Mathf.InverseLerp(mA, mB, m * CablePostM) * pieces;
                int q = Mathf.Clamp(Mathf.FloorToInt(t), 0, pieces - 1);
                float f = t - q;
                var p = Vector3.Lerp(P[q], P[q + 1], f);
                float dd = Mathf.Lerp(D[q], D[q + 1], f);
                if (float.IsNaN(dd) || dd < 1.5f) continue;
                var along = (P[q + 1] - P[q]); along.y = 0f; along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
                var side = new Vector3(-along.z, 0f, along.x);
                EmitBlock(bk, p + Vector3.down * 0.5f, p + Vector3.up * H(CablePostTopM, dd), along * CablePostHalfM, side * (0.8f * CablePostHalfM), CellGalv0, CellGalv1, BandMisc0, BandMisc1);
                cablePosts++;
            }
            // the anchors at the run's ends
            for (int end = 0; end < 2; end++)
            {
                float de = end == 0 ? dA : dB;
                if (!(de <= 0.01f)) continue;
                var p = end == 0 ? P[0] : P[pieces];
                var along = (P[pieces] - P[0]); along.y = 0f; along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
                var side = new Vector3(-along.z, 0f, along.x);
                EmitBlock(bk, p + Vector3.down * 0.4f, p + Vector3.up * 0.2f, along * 0.45f, side * 0.35f, 0f, 0.25f, BandWall0, BandWall1);
            }
        }

        static void CableColliderPiece(Vector3 a, Vector3 b, Vector3 o3, float lowA, float topA, float lowB, float topB)
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
            bk.Face(new Vector3(fa.x, lowA, fa.z), new Vector3(fa.x, topA, fa.z), new Vector3(ba.x, topA, ba.z), new Vector3(ba.x, lowA, ba.z),
                    -along, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            bk.Face(new Vector3(fb.x, lowB, fb.z), new Vector3(fb.x, topB, fb.z), new Vector3(bb.x, topB, bb.z), new Vector3(bb.x, lowB, bb.z),
                    along, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        }

        /// <summary>An overpass zone's W-beam on this carriageway's median side over the span:
        /// the beam a fixed distance off the edge over the zone's core, flared WBeamFlareM away
        /// from the traffic over WBeamTermM past each end and turned down at its tip (the
        /// lead-in's shape), on the ground the verge lays there; posts at chain distance; its
        /// collider the drawn beam.</summary>
        static void EmitMedianBeamSpan(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int i)
        {
            var d = DivOf(e);
            if (d?.zones == null) return;
            var A = sections[i - 1]; var B = sections[i];
            Vector3 eA = A.Edge(1), eB = B.Edge(1);
            Vector2 oA = A.Out(1), oB = B.Out(1);
            float len = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            if (len < 0.05f || B.s - A.s < 1e-3f) return;
            foreach (var z in d.zones)
            {
                float z0 = z.x - WBeamTermM, z1 = z.y + WBeamTermM;
                float s0 = Mathf.Max(A.s, z0), s1 = Mathf.Min(B.s, z1);
                if (s1 - s0 < 0.05f) continue;
                beamZonesDrawn++;
                int pieces = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / WBeamStepM));
                void At(float s, out Vector3 front, out Vector2 outw, out float top)
                {
                    float t = (s - A.s) / (B.s - A.s);
                    var ed = Vector3.Lerp(eA, eB, t);
                    outw = Vector2.Lerp(oA, oB, t).normalized;
                    float g = DivGapAt(d, s); if (float.IsNaN(g)) g = 10f;
                    float baseOff = Mathf.Clamp(0.5f * g - 1.6f, 0.6f, 3.0f);
                    float flareMax = Mathf.Clamp(0.5f * g - 0.2f - baseOff, 0f, WBeamFlareM);
                    float u = Mathf.Max(0f, Mathf.Max(z.x - s, s - z.y));   // into the terminal
                    float off = baseOff + flareMax * Mathf.Clamp01(u / WBeamTermM);
                    var pW = new Vector2(ed.x + tm.origin.x, ed.z + tm.origin.z);
                    float gy = MedianGroundY(map, trims, e, s, pW, outw, ed.y, off);
                    front = new Vector3(ed.x + outw.x * off, gy, ed.z + outw.y * off);
                    float toTip = WBeamTermM - u;
                    top = gy + (toTip >= WBeamDownM ? WBeamTopM : Mathf.Lerp(-0.15f, WBeamTopM, Mathf.Clamp01(toTip / WBeamDownM)));
                }
                for (int q = 0; q < pieces; q++)
                {
                    float sa = Mathf.Lerp(s0, s1, (float)q / pieces), sb = Mathf.Lerp(s0, s1, (float)(q + 1) / pieces);
                    At(sa, out var fa, out var na, out float ta);
                    At(sb, out var fb, out var nb, out float tb);
                    tm.wbeams.Add(new WBeamPiece
                    {
                        a = fa, b = fb, outA = na, outB = nb,
                        botA = ta - WBeamDepthM, topA = ta, botB = tb - WBeamDepthM, topB = tb,
                        endA = q == 0 && sa <= z0 + 1e-3f, endB = q == pieces - 1 && sb >= z1 - 1e-3f,
                    });
                    WBeamColliderPiece(fa, fb, na, nb, ta, tb);
                }
                float mA = e.vOff + e.vDir * s0, mB = e.vOff + e.vDir * s1;
                float lo = Mathf.Min(mA, mB), hi = Mathf.Max(mA, mB);
                for (int m = Mathf.FloorToInt(lo / WBeamPostM) + 1; m * WBeamPostM <= hi + 1e-4f; m++)
                {
                    float s = Mathf.Lerp(s0, s1, Mathf.InverseLerp(mA, mB, m * WBeamPostM));
                    At(s, out var fp, out var np, out float tp);
                    if (tp - fp.y < 0.35f) continue;   // turned down here
                    tm.wbeamPosts.Add(new WBeamPost { at = fp, outward = np, top = tp - 0.03f });
                }
            }
        }
    }
}
