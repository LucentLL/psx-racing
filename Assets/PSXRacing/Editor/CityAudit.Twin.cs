using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// TWIN DECKS (2026-10-02, plan B1; the hook <see cref="TwinReport"/>).
    ///
    /// The owner's example: West 5th Street over I-77 is one bridge with a
    /// median, drawn as two decks 1.5 m apart - four parapets, a slot down to
    /// the freeway, two pier lines out of step. This report measures every
    /// pair of decks the twin-deck table (<see cref="DeckPairs"/>,
    /// <see cref="CityMap.deckPairs"/>) calls ONE structure, and looks for
    /// pairs the table missed with its own finder:
    ///
    ///   (a) INNER RAIL   metres of parapet on the side facing the twin
    ///   (b) OPEN SLOT    down-rays every 0.5 m at 5 points across the gap
    ///                    that find no deck slab within 0.15 m
    ///   (c) WALLS        rays 0.5 m over the deck from one carriageway's
    ///                    inner lane to the other's: walls crossed beyond
    ///                    the designed median (a Jersey: 1; curbs or paint: 0)
    ///   (d) PIERS        the partner's piers inside the pair (one bent each)
    ///   (e) HEIGHT       the two decks' height difference every 2 m (the
    ///                    solve's twin holds, plan B1: p95 2 cm, max 5 cm)
    ///   (f) MISSED       pairs this report's own finder sees on the SOLVED
    ///                    structure (ElevatedAt every 2 m, the drawn
    ///                    extents) within the table's G that the table keeps
    ///                    apart with no reason
    ///
    /// (a)-(d) build tiles, so they run in the report's scope (BOXED unless
    /// PSX_AUDIT_FULL=1: <see cref="ScopeFor"/>). (e), (f) and the table read
    /// the solved map only (no tile, a second or two), so they cover every
    /// pair whatever the box, in the scope's tiers. The table goes to
    /// twin_pairs.csv beside city_audit.txt for tools/city/deckpairs.mjs
    /// --compare (the offline twin of the same rule).
    ///
    /// GATES. (e) and (f) are the solve's and the table's (plan B1): checks.
    /// (a)-(d) are the mesh's (plan A2) and REPORT until A2 draws one deck:
    /// A2 flips <see cref="TwinMeshGated"/> (the one line of this file the
    /// other lane changes).
    /// </summary>
    public static partial class CityAudit
    {
        /// <summary>Plan A2 sets this true in its commit: (a)-(d) become checks.</summary>
        const bool TwinMeshGated = true;

        static partial void TwinReport(CityMap map, CityMeshes.Trims trims)
        {
            var sc = ScopeFor("TWIN");
            Line("TWIN DECKS (plan B1): tile probes (a)-(d): " + sc.Describe() + "; table, heights (e) and the finder (f): every pair in the scope's tiers (no tile builds)");
            var pairs = map.deckPairs;
            if (pairs == null) { Check(false, "the twin-deck table is built (DeckPairs.Build)"); return; }
            Line($"  table: {DeckPairs.LastReport}");
            Line($"  solve: {CityElevation.TwinHoldReport}");
            var inv = CultureInfo.InvariantCulture;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            // ---- the table, by tier, and its CSV --------------------------------
            var tiers = new[] { 1, 2, 3 };
            foreach (int t in tiers)
            {
                if (!sc.HasTier(t)) continue;
                int up = 0, sp = 0, cul = 0; float um = 0f;
                var why = new SortedDictionary<string, int>();
                foreach (var pr in pairs)
                {
                    if (pr.tier != t) continue;
                    if (pr.union) { up++; um += pr.overlap; } else sp++;
                    if (pr.why.StartsWith("culvert") && DeckPairs.WouldUnion(map, pr)) cul++;
                    string k = (pr.union ? "UNION " : "separate ") + pr.why;
                    why[k] = why.TryGetValue(k, out int n) ? n + 1 : 1;
                }
                Line($"  {CityTier.Short(t)}: {up} pairs one structure ({um:0} m of carriageway pair, {2f * um:0} m of inner parapet for A2 to take away), {sp} kept apart ({cul} culvert creeks, Q8)");
                foreach (var kv in why) Line($"      {kv.Key}: {kv.Value}");
            }
            WriteTwinCsv(map);

            // ---- (e) one height --------------------------------------------------
            var dys = new List<float>();
            var worst = new List<(float dy, string what)>();
            var worstAt = new List<(float dy, DeckPairs.Pair pr, float s)>();
            int split = 0, solvedN = 0; float solvedMax = 0f;
            var splits = new List<(float dy, string what)>();
            foreach (var pr in pairs)
            {
                if (!pr.union || !sc.HasTier(pr.tier)) continue;
                var A = map.edges[pr.a]; var B = map.edges[pr.b];
                float pairWorst = 0f; float worstS = 0f, splitWorst = 0f, splitS = 0f;
                for (float s = pr.a0; s <= pr.a1 + 0.01f; s += 2f)
                {
                    if (!A.ElevatedAt(s)) continue;
                    float at = pr.ArcOnOther(pr.a, s);
                    if (at < 0f || !B.ElevatedAt(at)) continue;
                    float dy = Mathf.Abs(A.YAt(s) - B.YAt(at));
                    // what the solve's holds govern: both decks by the facts
                    // (DeckPairs.DeckAt). The rest - decks only the solve
                    // made, and the stations it carried a deck on by - took
                    // no hold, and is reported.
                    if (pr.solved || !DeckPairs.DeckAt(pr.a, s) || !DeckPairs.DeckAt(pr.b, at))
                    { solvedN++; solvedMax = Mathf.Max(solvedMax, dy); continue; }
                    if (dy > CityElevation.TwinHoldMaxDyM) { split++; if (dy > splitWorst) { splitWorst = dy; splitS = s; } continue; }
                    dys.Add(dy);
                    if (dy > pairWorst) { pairWorst = dy; worstS = s; }
                }
                if (splitWorst > 0f)
                {
                    var p = A.PointAt(splitS);
                    splits.Add((splitWorst, $"e{pr.a}/e{pr.b} '{A.name}'/'{B.name}' {pr.kind} {pr.median} dy {splitWorst:0.00} m at s {splitS:0} {LatLon(p.x, p.y)}"));
                }
                if (pairWorst > 0.02f)
                {
                    var p = A.PointAt(worstS);
                    worst.Add((pairWorst, $"e{pr.a}/e{pr.b} '{A.name}'/'{B.name}' {pr.kind} {pr.median} dy {pairWorst:0.000} m at s {worstS:0} ({p.x:0},{p.y:0}) {LatLon(p.x, p.y)}"));
                    worstAt.Add((pairWorst, pr, worstS));
                }
            }
            dys.Sort();
            float P(float q) => dys.Count == 0 ? 0f : dys[Mathf.Clamp(Mathf.CeilToInt(q * dys.Count) - 1, 0, dys.Count - 1)];
            float p50 = P(0.5f), p95 = P(0.95f), dmax = dys.Count > 0 ? dys[dys.Count - 1] : 0f;
            Line($"  (e) height: {dys.Count} samples every 2 m on the unions' decks: p50 {p50:0.000} m, p95 {p95:0.000} m, max {dmax:0.000} m; {split} more over {CityElevation.TwinHoldMaxDyM} m apart (a split level: the union ends there, A2); pairs over 2 cm: {worst.Count}");
            Line($"      where the solve made the deck (no hold; reported): {solvedN} samples, max dy {solvedMax:0.000} m");
            splits.Sort((x, y) => y.dy.CompareTo(x.dy));
            for (int i = 0; i < Mathf.Min(8, splits.Count); i++) Line("      split level (left apart): " + splits[i].what);
            worst.Sort((x, y) => y.dy.CompareTo(x.dy));
            for (int i = 0; i < Mathf.Min(10, worst.Count); i++) Line("      " + worst[i].what);
            // the three worst as the solve left them: each deck's stations within
            // 25 m (arc, height, s = seated, d = a deck by the facts, E = on
            // structure), the twin's beside each
            worstAt.Sort((x, y) => y.dy.CompareTo(x.dy));
            for (int w = 0; w < Mathf.Min(3, worstAt.Count); w++)
            {
                var (wdy, wpr, ws) = worstAt[w];
                var A = map.edges[wpr.a]; var B = map.edges[wpr.b];
                var sbp = new StringBuilder($"      profile e{wpr.a} (len {A.length:0}, nodes {A.a}/{A.b}) beside e{wpr.b} (len {B.length:0}, nodes {B.a}/{B.b}), dy {wdy:0.000} at s {ws:0}:");
                for (int i = 0; i < A.stS.Length; i++)
                {
                    if (Mathf.Abs(A.stS[i] - ws) > 25f) continue;
                    float at = wpr.ArcOnOther(wpr.a, A.stS[i]);
                    sbp.Append($" [{A.stS[i]:0} {A.stY[i]:0.000}{(A.SeatedAt(i) ? "s" : "")}{(DeckPairs.DeckAt(A.index, A.stS[i]) ? "d" : "")}{(A.stElev[i] ? "E" : "")} | {(at >= 0f ? $"{at:0} {B.YAt(at):0.000}" : "-")}]");
                }
                Line(sbp.ToString());
            }
            // the plan's named spots: the owner's W 5th, the I-277 viaduct, the skewed-hump test
            foreach (var (sa, sb, label) in new[] { (1253, 5445, "W 5th St over I-77"), (1910, 1921, "I-277 viaduct"), (2026, 2029, "I-277 skewed humps") })
            {
                DeckPairs.Pair hit = null;
                foreach (var pr in pairs) if (pr.a == sa && pr.b == sb) { hit = pr; break; }
                if (hit == null) { Line($"      {label} e{sa}/e{sb}: not a pair in this graph"); continue; }
                var A = map.edges[hit.a]; var B = map.edges[hit.b];
                float mx = 0f; int n = 0;
                for (float s = hit.a0; s <= hit.a1 + 0.01f; s += 2f)
                {
                    float at = hit.ArcOnOther(hit.a, s);
                    if (!A.ElevatedAt(s) || at < 0f || !B.ElevatedAt(at)) continue;
                    mx = Mathf.Max(mx, Mathf.Abs(A.YAt(s) - B.YAt(at))); n++;
                }
                Line($"      {label} e{sa}/e{sb}: {(hit.union ? "ONE structure" : "kept apart")} ({hit.why}), gap {hit.gap:0.00} m, {hit.median}; dy max {mx:0.000} m over {n} samples");
            }
            Check(p95 <= 0.02f && dmax <= 0.05f, "twin decks stand at one height: p95 <= 2 cm, max <= 5 cm (TWIN e; plan B1)", $"p95 {p95:0.000} m, max {dmax:0.000} m over {dys.Count} samples");

            // ---- (f) the finder ----------------------------------------------------
            TwinFinder(map, sc);

            // ---- (a)-(d) the mesh, in the box ---------------------------------------
            TwinMeshProbes(map, trims, sc);
            Line($"  TWIN report {clock.ElapsedMilliseconds / 1000f:0.0} s");
        }

        static void WriteTwinCsv(CityMap map)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("a,b,wayA,wayB,union,G,gap,gapMin,overlap,tier,class,kind,outlineA,outlineB,culvert,a0,a1,b0,b1,why,median,solved\n");
            foreach (var pr in map.deckPairs)
            {
                var A = map.edges[pr.a]; var B = map.edges[pr.b];
                string Out(uint id) => id == 0 ? "" : (id & 0x80000000u) != 0 ? "r" + (id & 0x7FFFFFFFu) : "w" + id;
                sb.Append(string.Format(inv, "{0},{1},{2},{3},{4},{5},{6:0.000},{7:0.000},{8:0},{9},{10},{11},{12},{13},{14},{15:0.0},{16:0.0},{17:0.0},{18:0.0},\"{19}\",{20},{21}\n",
                    pr.a, pr.b, A.wayId, B.wayId, pr.union ? 1 : 0, pr.G, pr.gap, pr.gapMin, pr.overlap, pr.tier, pr.cls, pr.kind,
                    Out(pr.structA), Out(pr.structB), pr.culvert ? 1 : 0, pr.a0, pr.a1, pr.b0, pr.b1, pr.why, pr.median, pr.solved ? 1 : 0));
            }
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "twin_pairs.csv");
            File.WriteAllText(path, sb.ToString());
            Line($"  table written to {path} (node tools/city/deckpairs.mjs --compare it)");
        }

        // ------------------------------------------------------------------
        //  (f): an independent finder on the SOLVED structure
        // ------------------------------------------------------------------
        sealed class TwinCand { public int a, b; public float cos, fromA, fromB; public List<float> gaps = new List<float>(); }

        static void TwinFinder(CityMap map, AuditScope sc)
        {
            var cands = new Dictionary<long, TwinCand>();
            var segs = new HashSet<int>();
            var near = new Dictionary<int, (float d, float at, Vector2 q, Vector2 t)>();
            float cosMin = Mathf.Cos(15f * Mathf.Deg2Rad);
            int samples = 0;
            foreach (var e in map.edges)
            {
                if (e.stElev == null) continue;
                bool any = false;
                foreach (bool b in e.stElev) if (b) { any = true; break; }
                if (!any) continue;
                for (float s = 1f; s < e.length; s += 2f)
                {
                    if (!e.ElevatedAt(s)) continue;
                    samples++;
                    var p = e.PointAt(s); var t = e.TangentAt(s);
                    segs.Clear(); near.Clear();
                    map.EdgeSegsNear(p - Vector2.one * 50f, p + Vector2.one * 50f, segs);
                    foreach (int packed in segs)
                    {
                        int oi = packed >> 12, si = packed & 0xFFF;
                        if (oi == e.index) continue;
                        var o = map.edges[oi];
                        Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude;
                        if (L2 < 1e-6f) continue;
                        float L = Mathf.Sqrt(L2);
                        var to = d / L;
                        if (Mathf.Abs(Vector2.Dot(to, t)) < cosMin) continue;
                        float u = Vector2.Dot(p - a, d) / L2;
                        if (u < 0f || u > 1f) continue;
                        var q = a + d * u;
                        float dist = Vector2.Distance(p, q);
                        if (!near.TryGetValue(oi, out var h) || dist < h.d) near[oi] = (dist, o.s[si] + L * u, q, to);
                    }
                    LineModel.Extents(e, s, out float eM, out float eP);
                    int bestL = -1, bestR = -1; float gL = 0f, gR = 0f, cL = 0f, cR = 0f;
                    foreach (var kv in near)
                    {
                        var o = map.edges[kv.Key]; var h = kv.Value;
                        if (!o.ElevatedAt(h.at)) continue;
                        int side = ((h.q.x - p.x) * -t.y + (h.q.y - p.y) * t.x) >= 0f ? 1 : -1;
                        int sideO = ((p.x - h.q.x) * -h.t.y + (p.y - h.q.y) * h.t.x) >= 0f ? 1 : -1;
                        LineModel.Extents(o, h.at, out float oM, out float oP);
                        float gap = h.d - (side > 0 ? eP : eM) - (sideO > 0 ? oP : oM);
                        if (gap >= DeckPairs.GapMax) continue;
                        if (side > 0) { if (bestL < 0 || gap < gL) { bestL = kv.Key; gL = gap; cL = Vector2.Dot(h.t, t); } }
                        else { if (bestR < 0 || gap < gR) { bestR = kv.Key; gR = gap; cR = Vector2.Dot(h.t, t); } }
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        int oi = k == 0 ? bestR : bestL;
                        if (oi < 0) continue;
                        int lo = Mathf.Min(e.index, oi), hi = Mathf.Max(e.index, oi);
                        long key = ((long)lo << 32) | (uint)hi;
                        if (!cands.TryGetValue(key, out var c)) cands[key] = c = new TwinCand { a = lo, b = hi, cos = k == 0 ? cR : cL };
                        c.gaps.Add(k == 0 ? gR : gL);
                        if (e.index == lo) c.fromA += 2f; else c.fromB += 2f;
                    }
                }
            }
            var table = new Dictionary<long, DeckPairs.Pair>();
            foreach (var pr in map.deckPairs) table[((long)pr.a << 32) | (uint)pr.b] = pr;
            var missed = new List<string>(); var disagree = new List<string>();
            int seen = 0, within = 0, notInTable = 0;
            var counts = new int[4];
            foreach (var c in cands.Values)
            {
                if (Mathf.Max(c.fromA, c.fromB) < DeckPairs.OverlapMin) continue;
                c.gaps.Sort();
                float gap = c.gaps[c.gaps.Count >> 1];
                if (gap <= DeckPairs.SqueezeM) continue;
                var A = map.edges[c.a]; var B = map.edges[c.b];
                if (A.layer != B.layer) continue;
                var hiE = A.cls >= B.cls ? A : B;
                int tier = CityTier.Of(hiE);
                if (!sc.HasTier(tier)) continue;
                seen++;
                // the table's G for this pair (the same rule, from the audit's own gap)
                float G;
                if (A.structId != 0 && B.structId != 0) G = A.structId == B.structId ? DeckPairs.GOutline : 0f;
                else
                {
                    bool fwy = hiE.cls >= 4;
                    bool dual = !A.link && !B.link && c.cos < 0f && !string.IsNullOrEmpty(A.name) && A.name == B.name;
                    G = dual ? (fwy ? DeckPairs.GDualFreeway : DeckPairs.GDualArterial) : A.link || B.link ? DeckPairs.GRamp : DeckPairs.GOther;
                }
                if (gap > G) continue;
                within++;
                var mid = A.PointAt(A.length * 0.5f);
                string what = $"{CityTier.Short(tier)} e{c.a}/e{c.b} '{A.name}'/'{B.name}' gap {gap:0.00} m (G {G:0.0#}) x {Mathf.Max(c.fromA, c.fromB):0} m near {LatLon(mid.x, mid.y)}";
                if (!table.TryGetValue(((long)c.a << 32) | (uint)c.b, out var pr))
                {
                    notInTable++; counts[tier]++;
                    missed.Add(what + " - not in the table");
                }
                else if (!pr.union && pr.why.StartsWith("rule") && gap <= pr.G - 0.25f)
                {
                    counts[tier]++;
                    disagree.Add(what + $" - the table keeps it apart at gap {pr.gap:0.00} m ({pr.why})");
                }
            }
            int unexplained = missed.Count + disagree.Count;
            Line($"  (f) finder on the solved structure: {seen} pairs over 0.3 m in the scope's tiers, {within} within their G; unexplained {unexplained} (T1 {counts[1]}, T2 {counts[2]}, T3 {counts[3]}): {notInTable} not in the table, {disagree.Count} the table measured past G");
            foreach (var m in missed.GetRange(0, Mathf.Min(12, missed.Count))) Line("      " + m);
            foreach (var m in disagree.GetRange(0, Mathf.Min(12, disagree.Count))) Line("      " + m);
            Check(counts[1] == 0, "T1: no twin decks within their G kept apart without a reason (TWIN f; plan B1)", $"{counts[1]} (T2 {counts[2]}, T3 {counts[3]} reported)");
        }

        // ------------------------------------------------------------------
        //  (a)-(d): the built tiles
        // ------------------------------------------------------------------
        static void TwinMeshProbes(CityMap map, CityMeshes.Trims trims, AuditScope sc)
        {
            var todo = new List<DeckPairs.Pair>();
            foreach (var pr in map.deckPairs)
            {
                if (!pr.union || !sc.HasTier(pr.tier)) continue;
                var m = map.edges[pr.a].PointAt((pr.a0 + pr.a1) * 0.5f);
                if (sc.Contains(m)) todo.Add(pr);
            }
            // tile by tile: pairs whose middles share a tile are probed together
            todo.Sort((p, q) =>
            {
                var mp = map.edges[p.a].PointAt((p.a0 + p.a1) * 0.5f); var mq = map.edges[q.a].PointAt((q.a0 + q.a1) * 0.5f);
                int tp = Mathf.FloorToInt(mp.y / CityMeshes.TileSize) * 100000 + Mathf.FloorToInt(mp.x / CityMeshes.TileSize);
                int tq = Mathf.FloorToInt(mq.y / CityMeshes.TileSize) * 100000 + Mathf.FloorToInt(mq.x / CityMeshes.TileSize);
                return tp.CompareTo(tq);
            });
            var built = new Dictionary<long, GameObject>();
            var root = new GameObject("~twinAudit");
            CityMeshes.railLog = new List<CityMeshes.RailRecord>();
            CityMeshes.AuditView.BeginRecord();
            var tally = new float[4, 5];   // per tier: pairs, inner rail m, hole m, undesigned wall samples, partner piers
            var rows = new List<(float sev, string what)>();
            string w5th = null;
            int tilesBuilt = 0;
            try
            {
                foreach (var pr in todo)
                {
                    var A = map.edges[pr.a]; var B = map.edges[pr.b];
                    // the tiles under both decks' pair runs, 10 m on (a span is its midpoint tile's)
                    var need = new HashSet<long>();
                    void Cover(CityMap.Edge e, float s0, float s1)
                    {
                        for (float s = s0 - 10f; s <= s1 + 10f; s += 5f)
                        {
                            var p = e.PointAt(Mathf.Clamp(s, 0f, e.length));
                            for (int dx = -1; dx <= 1; dx++)
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    var q = p + new Vector2(dx, dz) * 12f;
                                    need.Add(TwinTileKey(Mathf.FloorToInt(q.x / CityMeshes.TileSize), Mathf.FloorToInt(q.y / CityMeshes.TileSize)));
                                }
                        }
                    }
                    Cover(A, pr.a0, pr.a1); Cover(B, pr.b0, pr.b1);
                    var drop = new List<long>();
                    foreach (var kv in built) if (!need.Contains(kv.Key)) drop.Add(kv.Key);
                    foreach (long k in drop) { Object.DestroyImmediate(built[k]); built.Remove(k); }
                    foreach (long k in need)
                    {
                        if (built.ContainsKey(k)) continue;
                        int tx = (int)(k >> 32), tz = (int)(k & 0xFFFFFFFF);
                        var tm = CityMeshes.Build(map, trims, AuditBuildings, tx, tz);
                        var go = new GameObject($"tile_{tx}_{tz}");
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = tm.origin;
                        CityWorld.Attach(go, tm, null);
                        built[k] = go;
                        tilesBuilt++;
                    }
                    Physics.SyncTransforms();
                    var r = ProbeTwin(map, trims, pr);
                    int t = pr.tier;
                    tally[t, 0]++; tally[t, 1] += r.innerRail; tally[t, 2] += r.holeM; tally[t, 3] += r.walls; tally[t, 4] += r.piers;
                    var mid = A.PointAt((pr.a0 + pr.a1) * 0.5f);
                    string what = $"{CityTier.Short(t)} e{pr.a}/e{pr.b} '{A.name}'/'{B.name}' {pr.kind} {pr.median} gap {pr.gap:0.00} x {pr.overlap:0} m: inner rail {r.innerRail:0} m, open slot {r.holeM:0.0} m, walls {r.walls} samples ({r.wallMax} max crossed), partner piers {r.piers} @ {LatLon(mid.x, mid.y)}";
                    rows.Add((r.innerRail + r.holeM + r.walls + r.piers, what));
                    if ((pr.a == 1253 && pr.b == 5445) || (A.name == "West 5th Street" && A.bridge && B.bridge)) w5th ??= what;
                }
            }
            finally
            {
                foreach (var go in built.Values) Object.DestroyImmediate(go);
                Object.DestroyImmediate(root);
                CityMeshes.AuditView.EndRecord();
                CityMeshes.railLog = null;
            }
            Line($"  (a)-(d) the mesh on {todo.Count} union pairs in the box ({tilesBuilt} tile builds){(TwinMeshGated ? "" : " - REPORT until plan A2 draws one deck")}:");
            for (int t = 1; t <= 3; t++)
            {
                if (tally[t, 0] == 0) continue;
                Line($"    {CityTier.Short(t)}: {tally[t, 0]:0} pairs: (a) inner rail {tally[t, 1]:0} m, (b) open slot {tally[t, 2]:0.0} m, (c) {tally[t, 3]:0} samples with a wall past the designed median, (d) {tally[t, 4]:0} partner piers");
            }
            if (w5th != null) Line("    W 5th St over I-77 (the owner's example): " + w5th);
            rows.Sort((x, y) => y.sev.CompareTo(x.sev));
            for (int i = 0; i < Mathf.Min(12, rows.Count); i++) Line("      " + rows[i].what);
            float rail = 0f, hole = 0f, walls = 0f, piers = 0f;
            for (int t = 1; t <= 3; t++) { rail += tally[t, 1]; hole += tally[t, 2]; walls += tally[t, 3]; piers += tally[t, 4]; }
            if (TwinMeshGated)
            {
                Check(rail < 0.5f, "union decks carry no inner rail (TWIN a)", $"{rail:0} m");
                Check(hole < 0.25f, "no open slot between union decks (TWIN b)", $"{hole:0.0} m");
                Check(walls == 0f, "no wall across a union deck but its designed median (TWIN c)", walls);
                Check(piers == 0f, "a union stands on one pier line (TWIN d)", piers);
            }
        }

        static long TwinTileKey(int tx, int tz) => ((long)tx << 32) | (uint)tz;

        struct TwinProbe { public float innerRail, holeM; public int walls, wallMax, piers; }

        /// <summary>(a)-(d) for one pair, its tiles standing.</summary>
        static TwinProbe ProbeTwin(CityMap map, CityMeshes.Trims trims, DeckPairs.Pair pr)
        {
            var r = new TwinProbe();
            var A = map.edges[pr.a]; var B = map.edges[pr.b];
            // (a) the rails each tile drew on the side facing the twin, inside the pair
            var seenRail = new HashSet<(int, int, int)>();
            foreach (var rr in CityMeshes.railLog)
            {
                if (rr.edge != pr.a && rr.edge != pr.b) continue;
                if (rr.side != pr.SideOf(rr.edge)) continue;
                float s0 = pr.From(rr.edge), s1 = pr.To(rr.edge);
                float lo = Mathf.Max(Mathf.Min(rr.s0, rr.s1), s0), hi = Mathf.Min(Mathf.Max(rr.s0, rr.s1), s1);
                if (hi <= lo) continue;
                if (!seenRail.Add((rr.edge, Mathf.RoundToInt(rr.s0 * 10f), Mathf.RoundToInt(rr.s1 * 10f)))) continue;
                r.innerRail += hi - lo;
            }
            // (d) the partner's piers inside the pair
            var seenPier = new HashSet<(int, int)>();
            foreach (var pi in CityMeshes.AuditView.Piers)
                if (pi.edge == pr.b && pi.s >= pr.b0 - 1f && pi.s <= pr.b1 + 1f && seenPier.Add((pi.edge, Mathf.RoundToInt(pi.s * 10f)))) r.piers++;
            // (b) down-rays across the gap, (c) lane-to-lane rays over the deck
            var fr = new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };
            int designed = pr.median == DeckPairs.Median.Barrier ? 1 : 0;
            int k = 0;
            for (float s = pr.a0; s <= pr.a1 + 0.01f; s += 0.5f, k++)
            {
                if (!A.ElevatedAt(s)) continue;
                float at = pr.ArcOnOther(pr.a, s);
                if (at < 0f || !B.ElevatedAt(at)) continue;
                // (hotfix 2026-10-03) within a closed end's V the median's two
                // legs are its design: two walls, each a Jersey on the slab
                bool inV = CityMeshes.InUnionEndV(map, trims, A, s) || CityMeshes.InUnionEndV(map, trims, B, at);
                int designedHere = inV ? 2 : designed;
                var ia = InnerEdge(A, s, pr.sideA, 0f); var ib = InnerEdge(B, at, pr.sideB, 0f);
                if (Vector2.Distance(new Vector2(ia.x, ia.z), new Vector2(ib.x, ib.z)) > 0.3f)
                {
                    bool hole = false;
                    foreach (float f in fr)
                    {
                        var p = Vector3.Lerp(ia, ib, f);
                        // (plan A2) a Barrier median's own Jersey stands on the slab
                        // at the gap's centre: its top, 0.81 m up, is cover too
                        if (!RaycastPastLamps(p + Vector3.up * 2f, Vector3.down, out var hit, 4f) || !IsSlab(hit.collider) ||
                            (Mathf.Abs(hit.point.y - p.y) > 0.15f && !(designedHere > 0 && hit.collider.name == "Barriers"))) { hole = true; break; }
                    }
                    if (hole) r.holeM += 0.5f;
                }
                if (k % 4 != 0) continue;
                // lane to lane: 1.8 m in from each inner edge, half a metre up
                var la = InnerEdge(A, s, pr.sideA, 1.8f) + Vector3.up * 0.5f;
                var lb = InnerEdge(B, at, pr.sideB, 1.8f) + Vector3.up * 0.5f;
                int n = WallsCrossed(la, lb);
                if (n > designedHere) r.walls++;
                r.wallMax = Mathf.Max(r.wallMax, n);
            }
            return r;
        }

        /// <summary>A deck's drawn edge on side <paramref name="sideLeft"/>
        /// (+1 the left of travel), <paramref name="inward"/> metres in, at
        /// the solved height.</summary>
        static Vector3 InnerEdge(CityMap.Edge e, float s, int sideLeft, float inward)
        {
            LineModel.Extents(e, s, out float eM, out float eP);
            var p = e.PointAt(s); var t = e.TangentAt(s);
            var left = new Vector2(-t.y, t.x);
            float lat = sideLeft > 0 ? eP - inward : -(eM - inward);
            var q = p + left * lat;
            return new Vector3(q.x, e.YAt(s), q.y);
        }

        static bool IsSlab(Collider c) => c != null && (c.name == "Roads" || c.name == "Barriers");

        /// <summary>How many separate solids a segment passes through (each
        /// entered face once: the ray is re-cast from just past every hit).</summary>
        static int WallsCrossed(Vector3 a, Vector3 b)
        {
            var dir = b - a; float len = dir.magnitude;
            if (len < 0.1f) return 0;
            dir /= len;
            int n = 0; float done = 0f;
            var from = a;
            while (n < 8 && RaycastPastLamps(from, dir, out var hit, len - done))
            {
                n++;
                float step = hit.distance + 0.05f;
                done += step;
                if (done >= len) break;
                from += dir * step;
            }
            return n;
        }
    }
}
