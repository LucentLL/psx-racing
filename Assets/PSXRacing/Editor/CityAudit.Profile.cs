using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE PROFILE AUDIT (2026-10-02, plan B2; the hook <see cref="ProfileReport"/>).
    ///
    /// The owner: "Roads should be smooth. That means laterally and
    /// vertically. I notice a lot of roads dip down and go up under bridges,
    /// but they do so at an angle, not smooth transitions like DOT requires."
    /// Before this nothing measured a road's vertical DESIGN: the city audit
    /// checked the minimum clearance and a 16% grade, the drive audit 12 cm
    /// steps, the launch audit crests a car leaves. This reads the SOLVED
    /// stations (no tile is built: seconds) and measures, by tier:
    ///
    ///   CURVES     every chain (edges joined at nodes through their through
    ///              pairs, the rule VerticalCurves pairs arms by): consecutive
    ///              same-sign station breaks of 0.1% or more are ONE vertical
    ///              curve (A = their sum, L = their span); a curve of A 0.5% or
    ///              more is judged L &gt;= max(K A, 3V ft) at the class's design
    ///              speed (owner_decisions: motorway 65 mph, trunk and primary
    ///              50, secondary and tertiary 40, local 30, ramps 40 and loops
    ///              30; the last 30 m before a STOP or a signal 25). A sag fails
    ///              under AASHTO's comfort K (V^2/46.5), its target is the
    ///              headlight K; a crest is judged by stopping sight distance.
    ///              REPORT: these are plan B4's to fix (R2).
    ///   GRADES     the steepest station-to-station grade by class against the
    ///              class maximum (motorway 5%, trunk/primary 7%, secondary and
    ///              tertiary 9%, local 12%, ramps 8%). REPORT.
    ///   CROSSINGS  every enforced grade separation: the clearance under the
    ///              deck (min 4.9 m over a freeway, 4.4 m over a street) and the
    ///              SEPARATION's maximum (ClearanceM + DeckThick + 1.5 m = 7.05 m:
    ///              a street lifted over a road that was also dug). Checked:
    ///              DOUBLE SEPARATIONS - a crossing whose freeway was dug into a
    ///              trench AND whose street still stands more than 7.05 m over
    ///              it (the street humped by another crossing it carries) - T1 0.
    ///   DECISIONS  the trench rule's crossings (a street over a freeway
    ///              mainline) grouped by (over road, under road, within 60 m):
    ///              a group with some dug and some humped is MIXED (West 5th
    ///              St over I-77: northbound dug, southbound left at grade for
    ///              a creek 240 m away, so the street humps over it). And a
    ///              ramp under the street beside a dug mainline, left out of
    ///              the cut so it humps the street, is mixed too. Checked: T1 0.
    ///   PAIRS      a divided road's two carriageways where one street crosses
    ///              both: their heights at the two crossing points. Checked:
    ///              |dy| &lt;= 0.5 m on T1 (real roads: the two carriageways
    ///              of I-77 under West 5th are 0.4 m apart in 3DEP; the game
    ///              had them 5.0 m apart).
    ///   W 5TH      the owner's spot, line by line.
    ///
    /// Station-only, so it runs every chain city-wide whatever the box, in
    /// the scope's tiers (<see cref="ScopeFor"/>("PROFILE")); the box only
    /// limits the listed lines. Every crossing goes to profile_crossings.csv
    /// beside city_audit.txt (tools/city/metrics.mjs VERTICAL is the offline
    /// emulation of the same numbers).
    /// </summary>
    public static partial class CityAudit
    {
        /// <summary>A separation over this is two moves where one would do
        /// (plan B2: ClearanceM + DeckThick + 1.5 m).</summary>
        public const float SeparationMaxM = CityElevation.ClearanceM + CityElevation.DeckThick + 1.5f;
        /// <summary>A divided road's two carriageways at one street's
        /// crossings: at most this apart in height (plan B2).</summary>
        public const float PairDyMaxM = 0.5f;
        /// <summary>Crossings of one decision group lie within this of each
        /// other; a ramp beside a dug mainline within this of its crossing.</summary>
        public const float DecisionReachM = 60f;

        // ---- AASHTO (Green Book 2018), K in ft per % of A --------------------
        static readonly float[] KMph = { 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f };
        static readonly float[] KSagHeadlight = { 17f, 26f, 37f, 49f, 64f, 79f, 96f, 115f, 136f, 157f, 181f };
        static readonly float[] KCrestStop = { 7f, 12f, 19f, 29f, 44f, 61f, 84f, 114f, 151f, 193f, 247f };
        static float KAt(float[] tab, float mph)
        {
            if (mph <= KMph[0]) return tab[0];
            for (int i = 1; i < KMph.Length; i++)
                if (mph <= KMph[i]) return Mathf.Lerp(tab[i - 1], tab[i], (mph - KMph[i - 1]) / (KMph[i] - KMph[i - 1]));
            return tab[tab.Length - 1];
        }
        /// <summary>AASHTO's comfort sag K (ft/%): V^2/46.5.</summary>
        static float KSagComfort(float mph) => mph * mph / 46.5f;
        /// <summary>Metres of curve a break of A (a fraction) needs at K:
        /// max(K A, 3V) ft.</summary>
        static float NeedL(float K, float A, float mph) => Mathf.Max(K * Mathf.Abs(A) * 100f, 3f * mph) * 0.3048f;
        /// <summary>The equivalent radius (m) of a K: 100 K ft.</summary>
        static float RadiusOfK(float K) => K * 100f * 0.3048f;

        /// <summary>The vertical design speed (mph) of a road (owner_decisions,
        /// 2026-10-02): motorway 65, trunk and primary 50, secondary and
        /// tertiary 40, local 30, ramps 40, loop ramps 30.</summary>
        public static float VerticalMph(CityMap.Edge e) => CityElevation.VerticalMph(e);

        /// <summary>A loop ramp (CityElevation.IsLoopRamp: the solve's sag
        /// limiter reads the same table since plan L3).</summary>
        public static bool IsLoopRamp(CityMap.Edge e) => CityElevation.IsLoopRamp(e);

        /// <summary>The class's steepest grade (plan B2): motorway 5%,
        /// trunk/primary 7%, secondary/tertiary 9%, local 12%, ramps 8%.</summary>
        static float ClassMaxGrade(CityMap.Edge e) =>
            e.link ? 0.08f : e.cls >= 5 ? 0.05f : e.cls >= 3 ? 0.07f : e.cls >= 1 ? 0.09f : 0.12f;

        static string ClassKey(CityMap.Edge e) =>
            (e.cls >= 5 ? "motorway" : e.cls == 4 ? "trunk" : e.cls == 3 ? "primary" : e.cls == 2 ? "secondary" : e.cls == 1 ? "tertiary" : "local") + (e.link ? "_link" : "");

        struct PSt { public float d, y; public int e, i; public float mph; }
        sealed class VCurve { public bool sag; public float A, L, maxDg, mph; public int tier, e, i; public string cls; }

        static partial void ProfileReport(CityMap map, CityMeshes.Trims trims)
        {
            var sc = ScopeFor("PROFILE");
            Line("PROFILE AUDIT (plan B2): " + sc.Describe() + " - stations only, so every chain and crossing city-wide in the scope's tiers; the box only limits the listed lines");
            var inv = CultureInfo.InvariantCulture;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var E = map.edges;
            Line($"  design speeds (owner_decisions): motorway 65, trunk/primary 50, secondary/tertiary 40, local 30, ramps 40, loop ramps 30 ({CountLoops(map)} loop edges), last 30 m before a stop or signal 25 mph; " +
                 $"radii (m) at 65/50/40/30/25 mph: crest SSD {RadiusOfK(KAt(KCrestStop, 65)):0}/{RadiusOfK(KAt(KCrestStop, 50)):0}/{RadiusOfK(KAt(KCrestStop, 40)):0}/{RadiusOfK(KAt(KCrestStop, 30)):0}/{RadiusOfK(KAt(KCrestStop, 25)):0}, " +
                 $"sag headlight {RadiusOfK(KAt(KSagHeadlight, 65)):0}/{RadiusOfK(KAt(KSagHeadlight, 50)):0}/{RadiusOfK(KAt(KSagHeadlight, 40)):0}/{RadiusOfK(KAt(KSagHeadlight, 30)):0}/{RadiusOfK(KAt(KSagHeadlight, 25)):0}, " +
                 $"sag comfort {RadiusOfK(KSagComfort(65)):0}/{RadiusOfK(KSagComfort(50)):0}/{RadiusOfK(KSagComfort(40)):0}/{RadiusOfK(KSagComfort(30)):0}/{RadiusOfK(KSagComfort(25)):0}");

            // ---- CURVES and GRADES ------------------------------------------------
            var chains = ThroughChains(map);
            var curves = new List<VCurve>(40000);
            var gradeWorst = new Dictionary<string, (float g, int e, float s)>();
            var gradeOver = new int[4]; var gradeEdges = new HashSet<int>[4];
            for (int t = 0; t < 4; t++) gradeEdges[t] = new HashSet<int>();
            var S = new List<PSt>(512);
            foreach (var chain in chains)
            {
                S.Clear();
                float baseD = 0f;
                foreach (var (ei, fwd) in chain)
                {
                    var e = E[ei];
                    int n = e.stS.Length;
                    int endNode = fwd ? e.b : e.a;
                    bool ctl = map.nodeControl != null && endNode < map.nodeControl.Length && (map.nodeControl[endNode] & 6) != 0;
                    float mph = VerticalMph(e);
                    for (int k = 0; k < n; k++)
                    {
                        int i = fwd ? k : n - 1 - k;
                        float along = fwd ? e.stS[i] : e.length - e.stS[i];
                        float d = baseD + along;
                        if (S.Count > 0 && Mathf.Abs(S[S.Count - 1].d - d) < 0.05f) continue;   // the shared node station
                        float m = ctl && e.length - along <= 30f ? 25f : mph;
                        S.Add(new PSt { d = d, y = e.stY[i], e = ei, i = i, mph = m });
                    }
                    baseD += e.length;
                }
                // grades
                for (int k = 1; k < S.Count; k++)
                {
                    float h = S[k].d - S[k - 1].d;
                    if (h < 2f) continue;
                    var e = E[S[k].e];
                    if (!sc.HasTier(CityTier.Of(e))) continue;
                    float g = Mathf.Abs(S[k].y - S[k - 1].y) / h;
                    string key = ClassKey(e);
                    if (!gradeWorst.TryGetValue(key, out var w) || g > w.g) gradeWorst[key] = (g, e.index, e.stS[S[k].i]);
                    if (g > ClassMaxGrade(e) + 1e-4f) { int t = CityTier.Of(e); gradeOver[t]++; gradeEdges[t].Add(e.index); }
                }
                // breaks -> curves
                VCurve cur = null;
                void Flush() { if (cur != null && Mathf.Abs(cur.A) >= 0.005f) curves.Add(cur); cur = null; }
                for (int k = 1; k + 1 < S.Count; k++)
                {
                    float h1 = S[k].d - S[k - 1].d, h2 = S[k + 1].d - S[k].d;
                    if (h1 < 0.5f || h2 < 0.5f) continue;
                    float dg = (S[k + 1].y - S[k].y) / h2 - (S[k].y - S[k - 1].y) / h1;
                    if (Mathf.Abs(dg) < 0.001f) { Flush(); continue; }
                    bool sag = dg > 0f;
                    if (cur == null || cur.sag != sag) { Flush(); cur = new VCurve { sag = sag, e = S[k].e, i = S[k].i, mph = S[k].mph, cls = ClassKey(E[S[k].e]), tier = CityTier.Of(E[S[k].e]) }; }
                    cur.A += dg; cur.L += (h1 + h2) * 0.5f;
                    if (Mathf.Abs(dg) > Mathf.Abs(cur.maxDg)) { cur.maxDg = dg; cur.e = S[k].e; cur.i = S[k].i; cur.mph = S[k].mph; cur.cls = ClassKey(E[S[k].e]); cur.tier = CityTier.Of(E[S[k].e]); }
                }
                Flush();
            }
            int[] sagN = new int[4], sagComfort = new int[4], sagHead = new int[4], crestN = new int[4], crestShort = new int[4], corners = new int[4], sagRadius = new int[4];
            var worstSag = new List<(float A, string what)>();
            var byCls = new SortedDictionary<string, int[]>();   // sags, short comfort, crests, short crest
            foreach (var c in curves)
            {
                int t = c.tier;
                if (!sc.HasTier(t)) continue;
                if (!byCls.TryGetValue(c.cls, out var row)) byCls[c.cls] = row = new int[4];
                float A = Mathf.Abs(c.A);
                if (c.sag)
                {
                    sagN[t]++; row[0]++;
                    bool shortC = c.L < NeedL(KSagComfort(c.mph), A, c.mph) * 0.999f;
                    if (shortC) { sagComfort[t]++; row[1]++; }
                    if (c.L < NeedL(KAt(KSagHeadlight, c.mph), A, c.mph) * 0.999f) sagHead[t]++;
                    // plan L3/B4's own measure: the curve's mean radius L / A under
                    // the comfort radius (K's 3V minimum length left out)
                    if (c.L < RadiusOfK(KSagComfort(c.mph)) * A * 0.999f) sagRadius[t]++;
                    if (A >= 0.02f && c.L <= 10.5f) corners[t]++;
                    if (t == 1 && shortC)
                    {
                        var e = E[c.e]; var p = e.PointAt(e.stS[c.i]);
                        worstSag.Add((A, $"A {A * 100f:0.0}% over {c.L:0} m (comfort needs {NeedL(KSagComfort(c.mph), A, c.mph):0} m at {c.mph:0} mph) e{e.index} '{e.name}' {c.cls} s {e.stS[c.i]:0} ({p.x:0},{p.y:0}) {LatLon(p.x, p.y)}"));
                    }
                }
                else
                {
                    crestN[t]++; row[2]++;
                    if (c.L < NeedL(KAt(KCrestStop, c.mph), A, c.mph) * 0.999f) { crestShort[t]++; row[3]++; }
                }
            }
            Line($"  CURVES (REPORT; plan B4 rounds them): {curves.Count} vertical curves of A >= 0.5% on {chains.Count} chains");
            for (int t = 1; t <= 3; t++)
            {
                if (!sc.HasTier(t)) continue;
                Line($"    {CityTier.Short(t)}: sags {sagN[t]} - short of the comfort K {sagComfort[t]} (of its RADIUS, no 3V minimum: {sagRadius[t]}), of the headlight K {sagHead[t]}; corners (A >= 2% over <= 10 m) {corners[t]}; crests {crestN[t]} - short of stopping sight {crestShort[t]}");
            }
            foreach (var kv in byCls) Line($"      {kv.Key,-15} sags {kv.Value[0],6} short {kv.Value[1],6} | crests {kv.Value[2],6} short {kv.Value[3],6}");
            worstSag.Sort((a, b) => b.A.CompareTo(a.A));
            for (int i = 0; i < Mathf.Min(6, worstSag.Count); i++) Line("      T1 sag " + worstSag[i].what);

            var gl = new StringBuilder();
            foreach (var kv in gradeWorst) gl.Append($"{kv.Key} {kv.Value.g * 100f:0.0}% (e{kv.Value.e} s {kv.Value.s:0}), ");
            Line($"  GRADES (REPORT): station grades over the class maximum (5/7/9/12%, ramps 8%): " +
                 $"T1 {gradeOver[1]} on {gradeEdges[1].Count} edges, T2 {gradeOver[2]} on {gradeEdges[2].Count}, T3 {gradeOver[3]} on {gradeEdges[3].Count}; steepest by class: {gl.ToString().TrimEnd(',', ' ')}");

            // ---- CROSSINGS ------------------------------------------------------
            var on = CityElevation.EnforcedCrossings;
            var tr = CityElevation.TrenchedCrossings;
            var why = CityElevation.TrenchWhy;
            int NC = map.crossings.Length;
            var sO = new float[NC]; var sU = new float[NC]; var sep = new float[NC];
            for (int ci = 0; ci < NC; ci++)
            {
                var c = map.crossings[ci];
                CityElevation.ProjectOn(E[c.over], c.at, out sO[ci]);
                CityElevation.ProjectOn(E[c.under], c.at, out sU[ci]);
                sep[ci] = E[c.over].YAt(sO[ci]) - E[c.under].YAt(sU[ci]);
            }
            int TierOfX(int ci) { var c = map.crossings[ci]; return Mathf.Min(CityTier.Of(E[c.over]), CityTier.Of(E[c.under])); }
            bool Enf(int ci) => on == null || (ci < on.Length && on[ci]);
            bool Trench(int ci) => tr != null && ci < tr.Length && tr[ci];
            bool FreewayUnder(CityMap.Edge u) => u.cls >= 4 && !u.link;

            int[] lowClear = new int[4], tall = new int[4], doubles = new int[4];
            float minClear = float.MaxValue; int minClearCi = -1;
            var doubleLines = new List<(float sep, string what)>();
            var tallLines = new List<(float sep, string what)>();
            for (int ci = 0; ci < NC; ci++)
            {
                if (!Enf(ci)) continue;
                int t = TierOfX(ci);
                if (!sc.HasTier(t)) continue;
                var c = map.crossings[ci]; var O = E[c.over]; var U = E[c.under];
                float clear = sep[ci] - CityElevation.DeckThick;
                float need = FreewayUnder(U) ? 4.9f : 4.4f;
                if (clear < need - 1e-3f) lowClear[t]++;
                if (clear < minClear) { minClear = clear; minClearCi = ci; }
                if (sep[ci] > SeparationMaxM)
                {
                    tall[t]++;
                    string w = $"x{ci} '{O.name}' e{O.index} over '{U.name}' e{U.index}{(Trench(ci) ? " (dug)" : "")}: separation {sep[ci]:0.00} m at ({c.at.x:0},{c.at.y:0}) {LatLon(c.at.x, c.at.y)}";
                    if (Trench(ci)) { doubles[t]++; doubleLines.Add((sep[ci], w)); }
                    else if (sc.Contains(c.at)) tallLines.Add((sep[ci], w));
                }
            }
            Line($"  CROSSINGS: clearance under the deck below 4.9 m over a freeway / 4.4 m over a street: T1 {lowClear[1]}, T2 {lowClear[2]}, T3 {lowClear[3]} (lowest {minClear:0.00} m at x{minClearCi}); " +
                 $"separations over {SeparationMaxM:0.00} m: T1 {tall[1]}, T2 {tall[2]}, T3 {tall[3]} (REPORT: a hump on a hill, a stack); of them DOUBLE (the freeway dug AND the street still over {SeparationMaxM:0.00} m): T1 {doubles[1]}, T2 {doubles[2]}, T3 {doubles[3]}");
            doubleLines.Sort((a, b) => b.sep.CompareTo(a.sep));
            for (int i = 0; i < Mathf.Min(12, doubleLines.Count); i++) Line("    DOUBLE " + doubleLines[i].what);
            tallLines.Sort((a, b) => b.sep.CompareTo(a.sep));
            for (int i = 0; i < Mathf.Min(5, tallLines.Count); i++) Line("    tall (in the box) " + tallLines[i].what);

            // ---- the trench rule's decisions -------------------------------------
            // candidates: a street over a freeway mainline (the rule's domain)
            var cand = new List<int>();
            for (int ci = 0; ci < NC; ci++)
            {
                if (!Enf(ci)) continue;
                var c = map.crossings[ci]; var O = E[c.over]; var U = E[c.under];
                if (U.cls < 5 || U.link || U.tunnel) continue;
                if (O.cls >= 5 || O.link) continue;
                cand.Add(ci);
            }
            var parent = new Dictionary<int, int>();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (int ci in cand) parent[ci] = ci;
            string RoadKey(CityMap.Edge e) => string.IsNullOrEmpty(e.name) ? "#" + e.index : e.name;
            for (int a = 0; a < cand.Count; a++)
                for (int b = a + 1; b < cand.Count; b++)
                {
                    var ca = map.crossings[cand[a]]; var cb = map.crossings[cand[b]];
                    if (Vector2.Distance(ca.at, cb.at) > DecisionReachM) continue;
                    if (RoadKey(E[ca.over]) != RoadKey(E[cb.over]) || RoadKey(E[ca.under]) != RoadKey(E[cb.under])) continue;
                    parent[Find(cand[a])] = Find(cand[b]);
                }
            var groups = new Dictionary<int, List<int>>();
            foreach (int ci in cand) { int r = Find(ci); if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>(); l.Add(ci); }
            int mixedGroups = 0, mixedX = 0, dugGroups = 0, rampsOut = 0;
            var mixedLines = new List<string>();
            var groupOf = new int[NC]; for (int i = 0; i < NC; i++) groupOf[i] = -1;
            var mixedOf = new bool[NC];
            foreach (var kv in groups)
            {
                int dug = 0;
                foreach (int ci in kv.Value) { groupOf[ci] = kv.Key; if (Trench(ci)) dug++; }
                if (dug > 0) dugGroups++;
                bool mixed = dug > 0 && dug < kv.Value.Count;
                // a ramp under the same street beside a dug mainline, left
                // out of the cut (standing more than a metre over the cut's
                // bottom at its own crossing): it humps the street
                var rampsHere = new List<int>();
                if (dug > 0)
                {
                    for (int ri = 0; ri < NC; ri++)
                    {
                        if (!Enf(ri)) continue;
                        var rc = map.crossings[ri]; var RU = E[rc.under];
                        // (a ramp the trench rule dug is in the cut by decision;
                        // what it still stands over its street shows as DOUBLE)
                        if (!RU.link || RU.tunnel || Trench(ri)) continue;
                        if (RoadKey(E[rc.over]) != RoadKey(E[map.crossings[kv.Value[0]].over])) continue;
                        foreach (int ci in kv.Value)
                        {
                            if (!Trench(ci)) continue;
                            var mc = map.crossings[ci];
                            if (Vector2.Distance(rc.at, mc.at) > DecisionReachM) continue;
                            if (Mathf.Abs(Vector2.Dot(RU.TangentAt(sU[ri]), E[mc.under].TangentAt(sU[ci]))) < 0.9f) continue;
                            float bottom = E[mc.under].YAt(sU[ci]);
                            if (RU.YAt(sU[ri]) > bottom + 1f) { rampsHere.Add(ri); groupOf[ri] = kv.Key; }
                            break;
                        }
                    }
                }
                if (!mixed && rampsHere.Count == 0) continue;
                mixedGroups++; mixedX += kv.Value.Count + rampsHere.Count; rampsOut += rampsHere.Count;
                foreach (int ci in kv.Value) mixedOf[ci] = true;
                foreach (int ri in rampsHere) mixedOf[ri] = true;
                var c0 = map.crossings[kv.Value[0]];
                if (mixedLines.Count < 40)
                {
                    var sb = new StringBuilder($"'{E[c0.over].name}' over '{E[c0.under].name}' at {LatLon(c0.at.x, c0.at.y)}:");
                    foreach (int ci in kv.Value)
                        sb.Append($" x{ci} e{map.crossings[ci].under}{(Trench(ci) ? " DUG" : " humped (" + (why != null && ci < why.Length ? why[ci] : "?") + ")")} sep {sep[ci]:0.0};");
                    foreach (int ri in rampsHere) sb.Append($" ramp x{ri} e{map.crossings[ri].under} outside the cut, sep {sep[ri]:0.0};");
                    mixedLines.Add(sb.ToString());
                }
            }
            var whyCount = new SortedDictionary<string, int>();
            int notDug = 0;
            if (why != null)
                for (int ci = 0; ci < why.Length && ci < NC; ci++)
                    if (why[ci] != null && Enf(ci)) { notDug++; whyCount[why[ci]] = whyCount.TryGetValue(why[ci], out int n) ? n + 1 : 1; }
            var wl = new StringBuilder();
            foreach (var kv in whyCount) wl.Append($"{kv.Key} {kv.Value}, ");
            Line($"  DECISIONS: {CityElevation.TrenchCount} crossings dug; freeway-mainline-under crossings left at grade {notDug} ({wl.ToString().TrimEnd(',', ' ')}); " +
                 $"{cand.Count} street-over-freeway crossings in {groups.Count} groups (over road, under road, within {DecisionReachM:0} m), {dugGroups} with a dug member; " +
                 $"MIXED (dug and humped in one group, or a ramp beside the cut left out of it): {mixedGroups} groups, {mixedX} crossings ({rampsOut} ramps)");
            foreach (var l in mixedLines) if (l.Length > 0) Line("    MIXED " + l);
            if (CityElevation.TrenchDecisionReport != null)
            {
                Line("    solver: " + CityElevation.TrenchDecisionReport + $"; cuts given back where the street still rose: {CityElevation.TrenchRelaxRaised} stations, up to {CityElevation.TrenchRelaxGroups} groups in a round");
                var red = CityElevation.TrenchRedecided;
                if (red != null && red.Count > 0)
                {
                    string rpath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "trench_redecided.txt");
                    File.WriteAllLines(rpath, red);
                    Line($"    re-decided crossings: {red.Count} (every one in {rpath}); the first:");
                    for (int i = 0; i < Mathf.Min(8, red.Count); i++) Line("      " + red[i]);
                }
            }

            // ---- PAIRS: one street over both carriageways -------------------------
            bool Carriageway(CityMap.Edge e) => e.oneway && !e.link && !string.IsNullOrEmpty(e.name) && e.cls >= 2;
            int[] pairN = new int[4], pairFail = new int[4], pairRawFail = new int[4];
            float pairWorst = 0f;
            var pairLines = new List<(float dy, string what)>();
            var pairDyOf = new float[NC]; for (int i = 0; i < NC; i++) pairDyOf[i] = float.NaN;
            for (int i = 0; i < NC; i++)
            {
                if (!Enf(i)) continue;
                var ci = map.crossings[i]; var Ui = E[ci.under];
                if (!Carriageway(Ui)) continue;
                for (int j = i + 1; j < NC; j++)
                {
                    if (!Enf(j)) continue;
                    var cj = map.crossings[j]; var Uj = E[cj.under];
                    if (cj.under == ci.under || !Carriageway(Uj) || Uj.name != Ui.name) continue;
                    if (RoadKey(E[cj.over]) != RoadKey(E[ci.over])) continue;
                    if (Vector2.Distance(ci.at, cj.at) > CityElevation.PairReachM + 15f) continue;
                    if (Vector2.Dot(Ui.TangentAt(sU[i]), Uj.TangentAt(sU[j])) > -0.85f) continue;   // the other way
                    int t = Mathf.Min(CityTier.Of(Ui), CityTier.Of(Uj));
                    if (!sc.HasTier(t)) continue;
                    // ACROSS THE MEDIAN: each crossing point against the other
                    // carriageway at the same section (its foot there), the
                    // worse of the two - two crossing points 30 m apart along
                    // a 4% grade differ by 1.2 m and are no fault
                    float rawDy = Mathf.Abs(Ui.YAt(sU[i]) - Uj.YAt(sU[j]));
                    pairRawFail[t] += rawDy > PairDyMaxM ? 1 : 0;
                    CityElevation.ProjectOn(Uj, ci.at, out float fj);
                    CityElevation.ProjectOn(Ui, cj.at, out float fi);
                    float dy = Mathf.Max(Mathf.Abs(Ui.YAt(sU[i]) - Uj.YAt(fj)), Mathf.Abs(Uj.YAt(sU[j]) - Ui.YAt(fi)));
                    pairN[t]++;
                    pairDyOf[i] = float.IsNaN(pairDyOf[i]) ? dy : Mathf.Max(pairDyOf[i], dy);
                    pairDyOf[j] = float.IsNaN(pairDyOf[j]) ? dy : Mathf.Max(pairDyOf[j], dy);
                    if (dy > PairDyMaxM)
                    {
                        pairFail[t]++;
                        if (t == 1) pairWorst = Mathf.Max(pairWorst, dy);
                        pairLines.Add((dy, $"{CityTier.Short(t)} '{E[ci.over].name}' over '{Ui.name}' e{Ui.index}/e{Uj.index} (x{i}/x{j}{(Trench(i) ? " dug" : "")}/{(Trench(j) ? "dug" : "")}): dy {dy:0.00} m at {LatLon(ci.at.x, ci.at.y)}"));
                    }
                }
            }
            Line($"  PAIRS: a divided road's two carriageways under one street (same name, running opposite, crossings within {CityElevation.PairReachM + 15f:0} m), across the median at each crossing's section: T1 {pairN[1]} pairs, {pairFail[1]} over {PairDyMaxM} m apart (worst {pairWorst:0.00} m); T2 {pairN[2]}, {pairFail[2]} over; T3 {pairN[3]}, {pairFail[3]} over " +
                 $"(the two crossing POINTS compared, the first B2 run's measure: T1 {pairRawFail[1]}, T2 {pairRawFail[2]}, T3 {pairRawFail[3]} over)");
            pairLines.Sort((a, b) => b.dy.CompareTo(a.dy));
            for (int i = 0; i < Mathf.Min(10, pairLines.Count); i++) Line("    PAIR " + pairLines[i].what);

            // ---- the owner's spot -------------------------------------------------
            var w5 = new StringBuilder("  W 5TH (the owner's example): ");
            float nb = float.NaN, sb5 = float.NaN, sepNB = 0f; int w5n = 0;
            for (int ci = 0; ci < NC; ci++)
            {
                var c = map.crossings[ci]; var O = E[c.over]; var U = E[c.under];
                if (O.name == null || !O.name.Contains("West 5th") || U.name != "I-77" || U.link) continue;
                w5n++;
                float yU = U.YAt(sU[ci]);
                w5.Append($"x{ci} e{O.index} over e{U.index} (way {U.wayId}): street {O.YAt(sO[ci]):0.00}, I-77 {yU:0.00}, separation {sep[ci]:0.00} m{(Trench(ci) ? " dug" : " (" + (why != null && ci < why.Length ? why[ci] : "?") + ")")}; ");
                if (U.wayId == 122082585u) { nb = float.IsNaN(nb) ? yU : Mathf.Min(nb, yU); sepNB = Mathf.Max(sepNB, sep[ci]); }
                if (U.wayId == 648861047u) sb5 = float.IsNaN(sb5) ? yU : Mathf.Min(sb5, yU);
            }
            Line(w5.ToString());
            float w5dy = Mathf.Abs(nb - sb5);
            Line($"    I-77 northbound (way 122082585) vs southbound (way 648861047) under it: {w5dy:0.00} m apart (real 0.4); separation over the northbound {sepNB:0.00} m (real 4.9-5.5)");

            Line($"  ({clock.ElapsedMilliseconds} ms)");
            WriteProfileCsv(map, sO, sU, sep, groupOf, mixedOf, pairDyOf);

            Check(doubles[1] == 0, "PROFILE: no T1 double separation - no freeway dug into a trench under a street that still stands over 7.05 m above it (plan B2)", $"{doubles[1]}");
            Check(mixedGroups == 0, "PROFILE: one trench decision per crossing group - no street over a freeway with one carriageway dug and the other humped, and no ramp beside the cut left out of it (plan B2)", $"{mixedGroups} groups, {mixedX} crossings");
            Check(pairFail[1] == 0, $"PROFILE: a T1 divided road's two carriageways under one street within {PairDyMaxM} m of each other (plan B2)", $"{pairFail[1]} of {pairN[1]}, worst {pairWorst:0.00} m");
            if (w5n > 0)
                Check(w5dy <= PairDyMaxM + 1e-3f && sepNB <= SeparationMaxM + 1e-3f, $"PROFILE: W 5th St over I-77 - the two carriageways within {PairDyMaxM} m, the separation over the northbound at most {SeparationMaxM:0.00} m (the owner's example)", $"dy {w5dy:0.00} m, separation {sepNB:0.00} m");
        }

        static int CountLoops(CityMap map)
        {
            int n = 0;
            foreach (var e in map.edges) if (IsLoopRamp(e)) n++;
            return n;
        }

        /// <summary>Every edge in chains through the nodes' through pairs (the
        /// rule VerticalCurves pairs arms by: a two-arm node always unless the
        /// arms fold back, else arms within 45 degrees of straight; each arm
        /// to its best partner, same name and class first). Each chain is a
        /// list of (edge, forward).</summary>
        static List<List<(int e, bool fwd)>> ThroughChains(CityMap map)
        {
            var E = map.edges;
            var partner = new Dictionary<long, int>();
            long Key(int e, int n) => ((long)e << 32) | (uint)n;
            var arms = new List<CityMap.Edge>(8);
            var cand = new List<(float key, int p, int q)>(16);
            for (int n = 0; n < map.nodes.Length; n++)
            {
                arms.Clear();
                foreach (int ei in map.nodeEdges[n]) { var e = E[ei]; if (e.a != e.b && e.length >= 0.5f) arms.Add(e); }
                cand.Clear();
                for (int p = 0; p < arms.Count; p++)
                    for (int q = p + 1; q < arms.Count; q++)
                    {
                        Vector2 dp = arms[p].a == n ? arms[p].TangentAt(0f) : -arms[p].TangentAt(arms[p].length);
                        Vector2 dq = arms[q].a == n ? arms[q].TangentAt(0f) : -arms[q].TangentAt(arms[q].length);
                        float dot = Vector2.Dot(dp, dq);
                        if (!(arms.Count == 2 ? dot < 0.3f : dot < -0.7f)) continue;
                        float key = dot - (!string.IsNullOrEmpty(arms[p].name) && arms[p].name == arms[q].name ? 0.5f : 0f) - (arms[p].cls == arms[q].cls ? 0.2f : 0f);
                        cand.Add((key, p, q));
                    }
                cand.Sort((x, y) => x.key.CompareTo(y.key));
                var used = new HashSet<int>();
                foreach (var (_, p, q) in cand)
                {
                    int ep = arms[p].index, eq = arms[q].index;
                    if (used.Contains(ep) || used.Contains(eq)) continue;
                    used.Add(ep); used.Add(eq);
                    partner[Key(ep, n)] = eq; partner[Key(eq, n)] = ep;
                }
            }
            var seen = new bool[E.Length];
            var chains = new List<List<(int, bool)>>();
            foreach (var e0 in E)
            {
                if (seen[e0.index] || e0.a == e0.b || e0.stS == null) continue;
                // walk back to the chain's head
                var e = e0; int node = e0.a; int guard = 0;
                while (guard++ < 10000)
                {
                    if (!partner.TryGetValue(Key(e.index, node), out int p) || p == e0.index) break;
                    var o = E[p]; node = o.a == node ? o.b : o.a; e = o;
                    if (e == e0) break;
                }
                var list = new List<(int, bool)>();
                int fwdNode = node; guard = 0;
                while (e != null && !seen[e.index] && guard++ < 10000)
                {
                    seen[e.index] = true;
                    bool fromA = e.a == fwdNode;
                    list.Add((e.index, fromA));
                    int far = fromA ? e.b : e.a;
                    fwdNode = far;
                    e = partner.TryGetValue(Key(e.index, far), out int p) && !seen[p] && E[p].stS != null ? E[p] : null;
                }
                chains.Add(list);
            }
            return chains;
        }

        static void WriteProfileCsv(CityMap map, float[] sO, float[] sU, float[] sep, int[] groupOf, bool[] mixedOf, float[] pairDy)
        {
            var inv = CultureInfo.InvariantCulture;
            var on = CityElevation.EnforcedCrossings; var tr = CityElevation.TrenchedCrossings; var why = CityElevation.TrenchWhy;
            var sb = new StringBuilder("ci,over,under,overName,underName,overCls,underCls,overLink,underLink,enforced,trenched,why,group,mixed,sO,sU,yOver,yUnder,sep,pairDy,x,z,latlon\n");
            for (int ci = 0; ci < map.crossings.Length; ci++)
            {
                var c = map.crossings[ci]; var O = map.edges[c.over]; var U = map.edges[c.under];
                string Q(string s) => "\"" + (s ?? "").Replace("\"", "") + "\"";
                sb.AppendLine(string.Format(inv, "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14:0.0},{15:0.0},{16:0.000},{17:0.000},{18:0.000},{19},{20:0.0},{21:0.0},{22}",
                    ci, c.over, c.under, Q(O.name), Q(U.name), O.cls, U.cls, O.link ? 1 : 0, U.link ? 1 : 0,
                    on == null || on[ci] ? 1 : 0, tr != null && tr[ci] ? 1 : 0, Q(why != null ? why[ci] : null), groupOf[ci], mixedOf[ci] ? 1 : 0,
                    sO[ci], sU[ci], O.YAt(sO[ci]), U.YAt(sU[ci]), sep[ci], float.IsNaN(pairDy[ci]) ? "" : pairDy[ci].ToString("0.000", inv),
                    c.at.x, c.at.y, LatLon(c.at.x, c.at.y).Replace(",", " ")));
            }
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "profile_crossings.csv");
            File.WriteAllText(path, sb.ToString());
            Line($"  crossings written to {path}");
        }
    }
}
