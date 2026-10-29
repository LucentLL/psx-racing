using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ACROSS A DIVIDED STREET'S MEDIAN (2026-10-25, the owner: "I just crashed
    /// into the same West Trade Street median that was supposed to be
    /// removed"). The lane audits sweep the lanes; this slices ACROSS the
    /// median: every PSX_MED_STEP (2) m of edge PSX_MED_EDGE between
    /// PSX_MED_S0 and PSX_MED_S1, from the edge's centre line to its opposing
    /// twin's (same name, one-way, the other way, nearest within 40 m), the
    /// built world's surface every 0.5 m (height over the edge's road, which
    /// collider), and horizontal rays at 0.3 / 0.6 m over the road from the
    /// inner lane toward the twin (the first solid met). The tiles are the
    /// game's (CityWorld.EnsureRing). Writes median_profile.txt at the
    /// project root. Headless: -executeMethod PSXRacing.EditorTools.MedianProfileProbe.Run
    /// </summary>
    public static class MedianProfileProbe
    {
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[MedianProbe] no city data"); return; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            float step = float.Parse(System.Environment.GetEnvironmentVariable("PSX_MED_STEP") ?? "2", ci);
            // (round 4) PSX_MED_EDGES="edge:s0:s1;...": several slices in one job
            var specs = new System.Collections.Generic.List<(int ei, float s0, float s1)>();
            foreach (var sp in (System.Environment.GetEnvironmentVariable("PSX_MED_EDGES") ?? "").Split(';'))
            {
                var c = sp.Split(':');
                if (c.Length < 3) continue;
                specs.Add((int.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci)));
            }
            if (specs.Count == 0)
            {
                int e0 = int.Parse(System.Environment.GetEnvironmentVariable("PSX_MED_EDGE") ?? "5781", ci);
                specs.Add((e0, float.Parse(System.Environment.GetEnvironmentVariable("PSX_MED_S0") ?? "0", ci),
                           float.Parse(System.Environment.GetEnvironmentVariable("PSX_MED_S1") ?? map.edges[e0].length.ToString(ci), ci)));
            }
            foreach (var (ei, s0in, s1in) in specs)
            {
            var e = map.edges[ei];
            float s0 = s0in;
            float s1 = Mathf.Min(e.length, s1in);
            var go = new GameObject("~medianProbe");
            var world = go.AddComponent<CityWorld>();
            try
            {
                var pm = e.PointAt(0.5f * (s0 + s1));
                world.EnsureRing(new Vector3(pm.x, e.YAt(0.5f * (s0 + s1)), pm.y), 1);
                Physics.SyncTransforms();
                var hits = new RaycastHit[16];
                sb.AppendLine($"MEDIAN PROFILE e{ei} '{e.name}' s {s0:0}..{s1:0} of {e.length:0} m, every {step} m; surface over the road every 0.5 m toward the twin (h@d collider), then rays at 0.3/0.6 m");
                sb.AppendLine($"street halves held to one height city-wide: {CityElevation.StreetHalvesRaised} stations raised, up to {CityElevation.StreetHalvesRaiseMax:0.00} m");
                for (float s = s0; s <= s1 + 1e-3f; s += step)
                {
                    var p = e.PointAt(s);
                    float y = e.YAt(s);
                    var t = e.TangentAt(s).normalized;
                    // the opposing twin's nearest point
                    CityMap.Edge tw = null; float bestD = 40f; Vector2 q = Vector2.zero; float qs = 0f;
                    foreach (var c in map.edges)
                    {
                        if (c == e || !c.oneway || c.name != e.name || c.pts == null) continue;
                        for (int k = 0; k + 1 < c.pts.Length; k++)
                        {
                            var a = c.pts[k]; var ab = c.pts[k + 1] - a;
                            float L2 = ab.sqrMagnitude;
                            if (L2 < 1e-6f || Vector2.Dot(ab.normalized, t) > -0.5f) continue;
                            float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / L2);
                            var f = a + ab * u;
                            float d = Vector2.Distance(p, f);
                            if (d < bestD) { bestD = d; tw = c; q = f; qs = c.s[k] + u * Mathf.Sqrt(L2); }
                        }
                    }
                    if (tw == null) { sb.AppendLine($"s={s:0.0} ({p.x:0.0},{p.y:0.0}) y {y:0.00}: no twin within 40 m"); continue; }
                    float ty = tw.YAt(qs);
                    var dir = (q - p).normalized;
                    var line = new StringBuilder($"e{ei} s={s:0.0} ({p.x:0.0},{p.y:0.0}) y {y:0.00}{(e.ElevatedAt(s) ? " DECK" : "")} | twin e{tw.index}{(tw.bridge ? " B" : "")} s={qs:0.0} at {bestD:0.0} m, y {ty:0.00}{(tw.ElevatedAt(qs) ? " DECK" : "")} (dy {ty - y:+0.00;-0.00}) | surface:");
                    string lastName = null;
                    for (float d = 0f; d <= bestD + 1e-3f; d += 0.5f)
                    {
                        var w = p + dir * d;
                        int n = Physics.RaycastNonAlloc(new Vector3(w.x, y + 8f, w.y), Vector3.down, hits, 20f, ~0, QueryTriggerInteraction.Ignore);
                        float top = float.NegativeInfinity; string name = "-";
                        for (int i = 0; i < n; i++) if (hits[i].point.y > top) { top = hits[i].point.y; name = hits[i].collider.name; }
                        string nm = name == lastName ? "" : " " + name;
                        lastName = name;
                        line.Append($" {(float.IsNegativeInfinity(top) ? "none" : (top - y).ToString("+0.00;-0.00", ci))}@{d:0.0}{nm}");
                    }
                    // the first solid toward the twin from 1.8 m inside the inner edge
                    LineModel.Extents(e, s, out float em, out float ep);
                    float inner = Mathf.Max(0.5f, Mathf.Max(em, ep) - 1.8f);
                    foreach (float h in new[] { 0.3f, 0.6f })
                    {
                        var o = p + dir * inner;
                        var from = new Vector3(o.x, y + h, o.y);
                        int n = Physics.RaycastNonAlloc(from, new Vector3(dir.x, 0f, dir.y), hits, bestD + 5f, ~0, QueryTriggerInteraction.Ignore);
                        float near = float.MaxValue; int bi = -1;
                        for (int i = 0; i < n; i++) if (hits[i].distance < near) { near = hits[i].distance; bi = i; }
                        line.Append(bi < 0 ? $" | ray {h:0.0}: clear" : $" | ray {h:0.0}: {hits[bi].collider.name} at {inner + near:0.0} m (y {hits[bi].point.y - y:+0.00;-0.00}, n {hits[bi].normal.x:0.00},{hits[bi].normal.y:0.00},{hits[bi].normal.z:0.00})");
                    }
                    sb.AppendLine(line.ToString());
                }
            }
            finally { Object.DestroyImmediate(go); }
            }
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "median_profile.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[MedianProbe] wrote " + path);
        }

        /// <summary>
        /// A PLAN of one spot in an edge's frame (round 5): PSX_SPOTS=
        /// "edge:s0:s1:lat0:lat1;..." - every 1 m along the edge, every 0.5 m
        /// across (lateral along the section's right, +1 = side R), a ray down
        /// from 3 m over the edge's road: '#' a Barriers / Guardrail collider
        /// standing 0.2 m+ over it, else the letter of the edge whose drawn
        /// ribbon covers the point ('+' two or more), '=' other road collider,
        /// '.' ground, ' ' nothing. Then each edge met: its sections there,
        /// its union runs and the walled sides of its spans. Writes spot_probe.txt.
        /// Headless: -executeMethod PSXRacing.EditorTools.MedianProfileProbe.Spots
        /// </summary>
        public static void Spots()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[SpotProbe] no city data"); return; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            var hits = new RaycastHit[16];
            var secs = new System.Collections.Generic.List<CityMeshes.AuditView.SectionView>(256);
            var runs = new System.Collections.Generic.List<CityMeshes.AuditView.UnionRunView>(8);
            bool heightsRow = System.Environment.GetEnvironmentVariable("PSX_SPOT_HEIGHTS") == "1";
            foreach (var sp in (System.Environment.GetEnvironmentVariable("PSX_SPOTS") ?? "").Split(';'))
            {
                var c = sp.Split(':');
                if (c.Length < 5) continue;
                int ei = int.Parse(c[0], ci);
                float s0 = float.Parse(c[1], ci), s1 = float.Parse(c[2], ci), l0 = float.Parse(c[3], ci), l1 = float.Parse(c[4], ci);
                var e = map.edges[ei];
                s1 = Mathf.Min(s1, e.length); s0 = Mathf.Max(0f, s0);
                var go = new GameObject("~spotProbe");
                var world = go.AddComponent<CityWorld>();
                try
                {
                    var pm = e.PointAt(0.5f * (s0 + s1));
                    CityMeshes.AuditView.BeginRecord();
                    world.EnsureRing(new Vector3(pm.x, e.YAt(0.5f * (s0 + s1)), pm.y), 1);
                    CityMeshes.AuditView.EndRecord();
                    Physics.SyncTransforms();
                    var trims = CityWorld.SharedTrims;
                    // the edges near the slice
                    var segs = new System.Collections.Generic.HashSet<int>();
                    var near = new System.Collections.Generic.List<int>();
                    for (float s = s0; s <= s1 + 1e-3f; s += 5f)
                    {
                        var p = e.PointAt(s); float r = Mathf.Max(Mathf.Abs(l0), Mathf.Abs(l1)) + 2f;
                        segs.Clear();
                        map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, segs);
                        foreach (int sg in segs) if (!near.Contains(sg >> 12)) near.Add(sg >> 12);
                    }
                    near.Remove(ei); near.Insert(0, ei);
                    const string letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
                    if (near.Count > letters.Length) near.RemoveRange(letters.Length, near.Count - letters.Length);
                    sb.AppendLine($"SPOT e{ei} '{e.name}' s {s0:0}..{s1:0} lat {l0:0.0}..{l1:0.0} (columns lat from {l1:0.0} down to {l0:0.0}, 0.5 m)");
                    var secOf = new System.Collections.Generic.Dictionary<int, CityMeshes.AuditView.SectionView[]>();
                    for (int k = 0; k < near.Count; k++)
                    {
                        var o = map.edges[near[k]];
                        CityMeshes.AuditView.SectionsOf(o.index, secs);
                        secOf[o.index] = secs.ToArray();
                        sb.AppendLine($"  {letters[k]} = e{o.index} '{o.name}' cls {o.cls}{(o.link ? " link" : "")}{(o.oneway ? " 1way" : "")}{(o.bridge ? " BRIDGE" : "")} len {o.length:0.0} a n{o.a} b n{o.b} hw {o.HalfMax:0.0}, {secs.Count} sections");
                    }
                    // the plan
                    for (float s = s0; s <= s1 + 1e-3f; s += 1f)
                    {
                        var p = e.PointAt(s); var t = e.TangentAt(s).normalized; var right = new Vector2(-t.y, t.x);
                        float y = e.YAt(s);
                        var row = new StringBuilder($"{s,6:0.0} {y,7:0.00} |");
                        // (round 8) PSX_SPOT_HEIGHTS=1: under each row the top surface against this
                        // road's height: v under -1 m, l -1..-0.3, 0 within 0.3, h to 0.6, H to 1.2, X over
                        var hrow = heightsRow ? new StringBuilder("        heights |") : null;
                        for (float l = l1; l >= l0 - 1e-3f; l -= 0.5f)
                        {
                            var w = p + right * l;
                            int n = Physics.RaycastNonAlloc(new Vector3(w.x, y + 3f, w.y), Vector3.down, hits, 12f, ~0, QueryTriggerInteraction.Ignore);
                            float top = float.NegativeInfinity; string name = null;
                            for (int i = 0; i < n; i++) if (hits[i].point.y > top) { top = hits[i].point.y; name = hits[i].collider.name; }
                            if (hrow != null)
                            {
                                float dy = top - y;
                                hrow.Append(name == null ? ' ' : dy < -1f ? 'v' : dy < -0.3f ? 'l' : dy <= 0.3f ? '0' : dy <= 0.6f ? 'h' : dy <= 1.2f ? 'H' : 'X');
                            }
                            char ch = ' ';
                            if (name != null)
                            {
                                if ((name.Contains("Barrier") || name.Contains("Guardrail")) && top > y + 0.2f) ch = '#';
                                else
                                {
                                    int cover = -1, count = 0;
                                    for (int k = 0; k < near.Count; k++)
                                    {
                                        var o = map.edges[near[k]];
                                        var ss = secOf[o.index];
                                        if (ss.Length < 2) continue;
                                        CityElevation.ProjectOn(o, w, out float to);
                                        if (to < ss[0].s - 0.05f || to > ss[ss.Length - 1].s + 0.05f) continue;
                                        int j = 0; while (j + 2 < ss.Length && ss[j + 1].s <= to) j++;
                                        var A = ss[j]; var B = ss[j + 1];
                                        float f = B.s - A.s > 1e-5f ? Mathf.Clamp01((to - A.s) / (B.s - A.s)) : 0f;
                                        var P = Vector2.Lerp(A.P, B.P, f); var rr = Vector2.Lerp(A.right, B.right, f).normalized;
                                        float lat = Vector2.Dot(w - P, rr);
                                        float lo = Mathf.Lerp(A.LatL, B.LatL, f), hi = Mathf.Lerp(A.LatR, B.LatR, f);
                                        if (lat >= Mathf.Min(lo, hi) && lat <= Mathf.Max(lo, hi) && Mathf.Abs(hi - lo) > 0.05f) { if (cover < 0) cover = k; count++; }
                                    }
                                    ch = count > 1 ? '+' : cover >= 0 ? letters[cover] : name.Contains("Road") ? '=' : '.';
                                }
                            }
                            row.Append(ch);
                        }
                        sb.AppendLine(row.ToString());
                        if (hrow != null) sb.AppendLine(hrow.ToString());
                    }
                    // each edge met: sections in range, union runs, walled spans
                    foreach (int oi in near)
                    {
                        var o = map.edges[oi];
                        var ss = secOf[oi];
                        CityElevation.ProjectOn(o, e.PointAt(s0), out float ta); CityElevation.ProjectOn(o, e.PointAt(s1), out float tb);
                        float lo = Mathf.Min(ta, tb) - 5f, hi = Mathf.Max(ta, tb) + 5f;
                        var line = new StringBuilder($"  e{oi} sections {lo:0}..{hi:0}:");
                        foreach (var q in ss) if (q.s >= lo && q.s <= hi)
                            line.Append($" [{q.s:0.0} y{q.L.y:0.00}/{q.R.y:0.00} L{q.LatL:+0.0;-0.0} R{q.LatR:+0.0;-0.0}{(q.collapsed ? " COL" : q.clippedIn ? " clip" + q.innerSide : "")}{(q.squeezedL ? " sqL" : "")}{(q.squeezedR ? " sqR" : "")}{(q.elev ? " E" : "")}]");
                        sb.AppendLine(line.ToString());
                        CityMeshes.AuditView.UnionRunsOf(trims, oi, runs);
                        foreach (var u in runs)
                            sb.AppendLine($"    union side {u.side} s {u.s0:0.0}..{u.s1:0.0} nb e{u.nb} {u.nb0:0.0}..{u.nb1:0.0} {u.median}{(u.owner ? " OWNER" : "")}{(u.approach ? " approach" : "")}{(u.open0 ? " open0" : "")}{(u.open1 ? " open1" : "")}{(u.solid ? " solid" : " flat")}");
                        // (round 8) each run's road across, its seams owned across, a gap fill
                        var ul = trims?.unions != null && oi < trims.unions.Length ? trims.unions[oi] : null;
                        if (ul != null)
                            foreach (var r in ul)
                                sb.AppendLine($"      run {(r.owner ? "OWN" : "par")} s {r.s0:0.00}..{r.s1:0.00} side {r.side} across [{string.Join(",", r.across ?? new int[0])}]{(r.fill ? " FILL" : "")}" +
                                              $"{(r.seamEdge0 >= 0 ? $" seam0 e{r.seamEdge0}@{r.seamArc0:0.00}" : "")}{(r.seamEdge1 >= 0 ? $" seam1 e{r.seamEdge1}@{r.seamArc1:0.00}" : "")}");
                        // and why the ground-twin walk would (or would not) lay a median along it now
                        if (o.oneway && CityMeshes.Barriered(o) && !string.IsNullOrEmpty(o.name) && trims != null)
                        {
                            var why = new StringBuilder("    why:");
                            string last = null;
                            for (float s = Mathf.Max(0f, lo); s <= Mathf.Min(o.length, hi) + 1e-3f; s += 1f)
                            {
                                string w = CityMeshes.GroundTwinWhy(map, trims, o, s);
                                if (w == last) continue;
                                why.Append($" {s:0}:{w};");
                                last = w;
                            }
                            sb.AppendLine(why.ToString());
                        }
                        var sl = new StringBuilder("    walled spans:");
                        foreach (var r in CityMeshes.AuditView.Spans)
                        {
                            if (r.edge != oi || r.s1 < lo || r.s0 > hi) continue;
                            string Side(CityMeshes.AuditView.SideView v) => (v.gap ? "g" : "") + (v.rail ? "R" : "") + (v.retain ? "T" : "") + (v.cut ? "C" + v.cutWhy : "") + (v.median ? "M" : "") + (v.union ? "U" : "") + (v.capStart ? "<" : "") + (v.capEnd ? ">" : "");
                            string a = Side(r.l), b = Side(r.r);
                            if (a.Length + b.Length == 0) continue;
                            sl.Append($" {r.s0:0.0}-{r.s1:0.0}{(r.elev ? "E" : "")}{(r.approach ? "A" : "")} L:{a} R:{b};");
                        }
                        sb.AppendLine(sl.ToString());
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "spot_probe.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[SpotProbe] wrote " + path);
        }
    }
}
