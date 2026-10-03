using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE MARKS AUDIT (roads pass L6; plan A8 + owner Q5 (b)/(c); part of
    /// <see cref="PaintReport"/>). Reads the lane-use marks as the builder
    /// draws them (<see cref="CityMeshes.EnsureMarks"/>, the same rule the
    /// ribbon follows: <see cref="CityMeshes.OverrideAt"/>), in plan, no tile
    /// build - seconds:
    ///   DW     every merge zone: metres of its aux lane's full-width run (N
    ///          to the taper start) with the wide dotted line at the through
    ///          lanes' edge, and metres with a solid line there instead
    ///   V6     gores: white channelizing lines on both sides from N to P,
    ///          closed at N (the two lines within 0.10 m)
    ///   V5     tier-1 hosts: a branch clipped at over 35 degrees whose host
    ///          edge line still runs across its mouth
    ///   V7     turn-only lane boundaries (turn:lanes, TAPR bays) not solid
    ///   V8     style G diverges (an exit-only lane OSM drops at the node)
    ///          without the wide dotted line before it
    ///   glyphs arrow + ONLY groups and yield lines, where they were not laid
    ///   E1     every mark is a column of the ribbon: lift 0 (critic C5)
    /// GATES (Check), in scope (the OwnerBox by default), tier 1: DW 100 %,
    /// solid at the through edge 0 m, V5 = V6 = V7 = V8 = 0.
    /// </summary>
    public static partial class CityAudit
    {
        static void MarksReport(CityMap map, CityMeshes.Trims trims, AuditScope sc)
        {
            CityMeshes.EnsureMarks(map, trims);
            var st = CityMeshes.MarkStats;
            var I = CultureInfo.InvariantCulture;
            Line(string.Format(I, "MARKS (roads pass L6: lane-use paint, cut into the ribbon - lift 0, no new draw; {0}): built in {1:0} ms ({2})",
                CityMeshes.MarksOn ? "ON" : "OFF (PSX_CITY_MARKS=0)", st.buildMs, st.partsMs));
            Line(string.Format(I, "  city-wide: {0} aux lanes dotted ({1:0} m on {2} pieces), {3} gores white to their nose ({4} host / {5} ramp pieces; {6} zones without a gore), " +
                "{7} exit-only drops ({8:0} m, {9} pieces), {10} mouths stopped ({11} skipped), {12} turn-only lines solid on {13} edges ({14} TAPR bays), " +
                "{15} arrow+ONLY groups (left {16}, right {17}; {18} not laid: taper/short/no junction), {19} yield lines ({20} tagged give_way; {21} not laid)",
                st.auxZones, st.auxM, st.auxMarks, st.goreZones, st.goreHostPieces, st.goreRampPieces, st.noGore, st.exitDrops, st.exitDropM, st.exitDropPieces,
                st.mouths, st.mouthsSkipped, st.lsLines, st.lsEdges, st.bays, st.groups, st.arrowsL, st.arrowsR, st.groupsSkipped, st.yields, st.yieldsTagged, st.yieldsSkipped));

            // ---- per zone: DW, gores, exit drops ----
            var zoneHasExitDW = new HashSet<int>();
            foreach (var kv in CityMeshes.Marks)
                foreach (var m in kv.Value)
                    if (m.style == CityMeshes.MkDW && m.zone > 0) zoneHasExitDW.Add(m.zone);
            var lines = new List<LineModel.LineAt>(16);
            for (int pass = 0; pass < 2; pass++)
            {
                bool city = pass == 1;
                int zones = 0, gores = 0, goresOk = 0, goresOpen = 0, drops = 0, dropsOk = 0;
                float need = 0f, have = 0f, solid = 0f, worstClose = 0f, worstKey = -1f;
                string dwWorst = null, goreWorst = null, dropWorst = null;
                foreach (var z in CityMeshes.Zones)
                {
                    if (!city && !sc.Contains(map.nodes[z.node])) continue;
                    zones++;
                    // DW over the aux lane's full width: [xf, D] on the host chain
                    float x0 = z.style == 'G' ? 0f : z.xf, miss = 0f;
                    for (float x = x0 + 0.5f; x < z.D - 0.5f; x += 1f)
                    {
                        if (!z.HostAt(map, x, out var H, out float s, out int sideH)) continue;
                        need += 1f;
                        bool dw = false;
                        var ml = CityMeshes.MarksOf(H.index);
                        if (ml != null)
                            foreach (var m in ml)
                                if (m.style == CityMeshes.MkAuxDW && m.side == sideH && s >= m.s0 && s <= m.s1) { dw = true; break; }
                        if (dw) have += 1f; else miss += 1f;
                        // a solid line at the through lanes' edge
                        float lat = CityMeshes.AuxThroughLatAt(H, s, sideH);
                        if (float.IsNaN(lat)) continue;
                        LineModel.LinesAt(H, s, lines);
                        var lay = LineModel.LayoutOf(H);
                        foreach (var l in lines)
                        {
                            if (Mathf.Abs(l.lat - lat) > 0.25f) continue;
                            byte ov = CityMeshes.OverrideAt(H, l.k, s);
                            if (ov == CityMeshes.MkOff || ov == CityMeshes.MkDW || lay.kind[l.k] == LineModel.KWhiteDash) continue;
                            solid += 1f; break;
                        }
                    }
                    if (miss > 0f && dwWorst == null) dwWorst = string.Format(I, "zone n{0} e{1} ({2} {3}): {4:0} m without DW", z.node, z.branch, z.merge ? "merge" : "diverge", z.style, miss);
                    // the gore: both sides white and closed at N
                    if (CityMeshes.MarkStats.gore.TryGetValue(z.id, out var g))
                    {
                        gores++;
                        // the two lines' clear gap, smallest over the first 6 m past N (the V's point)
                        float close = float.NaN; string at = null;
                        for (float x = z.D + 0.05f; x <= Mathf.Min(z.D + 6f, g.xP); x += 0.25f)
                        {
                            if (!z.HostAt(map, x, out var H, out float s, out int sideH) || !CityMeshes.GoreLatAt(H, s, out float gl, out float hl) || float.IsNaN(hl)) continue;
                            float gp = Mathf.Max(0f, Mathf.Abs(gl - hl) - 0.20f);
                            if (float.IsNaN(close) || gp < close) { close = gp; at = string.Format(I, "e{0} s {1:0.0} side {2:+0;-0}: host line {3:0.00}, ramp line {4:0.00}", H.index, s, sideH, hl, gl); }
                        }
                        bool ok = !float.IsNaN(close) && close <= 0.10f;
                        if (ok) goresOk++;
                        else
                        {
                            goresOpen++;
                            float key = float.IsNaN(close) ? 1e6f : close;
                            if (key > worstKey)
                            {
                                worstKey = key;
                                goreWorst = string.Format(I, "zone n{0} e{1} ({2} {3}, D {4:0.0}, P at x {5:0.0}): {6}", z.node, z.branch, z.merge ? "merge" : "diverge", z.style, z.D, g.xP,
                                    float.IsNaN(close) ? "a side not white past N" : $"{close:0.00} m apart - {at}");
                            }
                        }
                        if (!float.IsNaN(close)) worstClose = Mathf.Max(worstClose, close);
                    }
                    if (z.style == 'G' && !z.merge)
                    {
                        drops++;
                        if (zoneHasExitDW.Contains(z.id)) dropsOk++;
                        else dropWorst ??= string.Format(I, "zone n{0} e{1} (exit e{2})", z.node, z.host, z.branch);
                    }
                }
                Line(string.Format(I, "  {0}: {1} zones; DW from N to the taper start {2:0} of {3:0} m ({4:0.0} %){5}; a solid line at the through lanes' edge there {6:0} m; " +
                    "gores {7}: white both sides and closed at N {8} (worst {9:0.00} m), open {10}{11}; V8 exit-only drops {12}: DW {13}, without {14}{15}",
                    city ? "T1 CITY-WIDE" : "in scope", zones, have, need, need > 0f ? 100f * have / need : 100f, dwWorst != null ? " - " + dwWorst : "", solid,
                    gores, goresOk, worstClose, goresOpen, goreWorst != null ? " - " + goreWorst : "", drops, dropsOk, drops - dropsOk, dropWorst != null ? " - " + dropWorst : ""));
                if (!city)
                {
                    Check(need - have < 0.5f, "MARKS in scope: the wide dotted line from N to the taper start in every merge zone (A8)", string.Format(I, "{0:0} of {1:0} m", have, need));
                    Check(solid < 0.5f, "MARKS in scope: no solid line between an aux lane and the through lanes", string.Format(I, "{0:0} m", solid));
                    Check(goresOpen == 0, "MARKS V6 in scope: every gore white on both sides and closed at N", $"{goresOpen} of {gores} open");
                    Check(drops == dropsOk, "MARKS V8 in scope: every exit-only drop dotted before its exit", $"{drops - dropsOk} of {drops}");
                }
            }

            // ---- V5 mouths, V7 turn-only, by tier-1 edge ----
            float cos35 = Mathf.Cos(35f * Mathf.Deg2Rad);
            for (int pass = 0; pass < 2; pass++)
            {
                bool city = pass == 1;
                int mouths = 0, open = 0; string mouthWorst = null;
                foreach (var L in map.edges)
                {
                    if (L.a == L.b) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        int node = end == 0 ? L.a : L.b;
                        int hi = trims.BranchAt(L, node);
                        if (hi < 0 || !trims.mitre[node]) continue;
                        var M = map.edges[hi];
                        if (CityTier.Of(M) != 1) continue;
                        if (!city && !sc.Contains(map.nodes[node])) continue;
                        bool zone = false;
                        foreach (var z in CityMeshes.Zones) if (z.branch == L.index && z.node == node) { zone = true; break; }
                        if (zone) continue;
                        Vector2 dB = node == L.a ? L.TangentAt(0f) : -L.TangentAt(L.length), dM = node == M.a ? M.TangentAt(0f) : -M.TangentAt(M.length);
                        if (Vector2.Dot(dB, dM) >= cos35) continue;
                        float sN = M.a == node ? 0f : M.length;
                        var tM = M.TangentAt(sN);
                        int sigma = Vector2.Dot(dB, new Vector2(-tM.y, tM.x)) >= 0f ? 1 : -1;
                        var lay = LineModel.LayoutOf(M);
                        int k = -1;
                        for (int i = 0; i < lay.kind.Length; i++) if (lay.kind[i] == (sigma > 0 ? LineModel.KEdgeP : LineModel.KEdgeM)) { k = i; break; }
                        if (k < 0) continue;   // no edge line to stop
                        mouths++;
                        // where the branch's centre line crosses the host's pavement edge
                        LineModel.Extents(M, sN, out float eMn, out float ePn);
                        float E = sigma > 0 ? ePn : eMn;
                        var yAx = new Vector2(-dM.y, dM.x);
                        if (Vector2.Dot(yAx, dB) < 0f) yAx = -yAx;
                        float xc = E * Vector2.Dot(dB, dM) / Mathf.Max(0.05f, Vector2.Dot(dB, yAx));
                        float sAt = M.a == node ? Mathf.Clamp(xc, 0f, M.length) : Mathf.Clamp(M.length - xc, 0f, M.length);
                        if (CityMeshes.OverrideAt(M, k, sAt) != CityMeshes.MkOff)
                        {
                            open++;
                            mouthWorst ??= string.Format(I, "e{0} into e{1} at n{2} ({3:0}, {4:0})", L.index, M.index, node, map.nodes[node].x, map.nodes[node].y);
                        }
                    }
                }
                int tb = 0, tbSolid = 0; float v7Km = 0f; string v7Worst = null;
                foreach (var e in map.edges)
                {
                    if (e.a == e.b || CityTier.Of(e) != 1 || !e.hasLset || (e.lsFlags & 1) == 0) continue;
                    if (!city && !sc.Takes(e)) continue;
                    CityMeshes.TurnOnly(e, out int fwd, out int bwd, out int nF, out int nB, out _);
                    int want = 0;
                    for (int i = 0; i + 1 < nF; i++) if ((((fwd >> i) ^ (fwd >> (i + 1))) & 1) != 0) want++;
                    for (int i = 0; i + 1 < nB; i++) if ((((bwd >> i) ^ (bwd >> (i + 1))) & 1) != 0) want++;
                    if (want == 0) continue;
                    int got = 0;
                    var ml = CityMeshes.MarksOf(e.index);
                    if (ml != null) foreach (var m in ml) if (m.style == CityMeshes.MkLS) got++;
                    tb += want; tbSolid += Mathf.Min(got, want);
                    if (got < want) { v7Km += e.length / 1000f; v7Worst ??= string.Format(I, "e{0} '{1}' turn-only {2:X4}, {3} of {4} lines solid", e.index, e.name, e.lsTurnOnly, got, want); }
                }
                Line(string.Format(I, "  {0}: V5 tier-1 host mouths over 35 deg: {1}, edge line still across {2}{3}; V7 turn-only boundaries {4}, solid {5}, not solid {6:0.00} km{7}",
                    city ? "T1 CITY-WIDE" : "in scope", mouths, open, mouthWorst != null ? " - " + mouthWorst : "", tb, tbSolid, v7Km, v7Worst != null ? " - " + v7Worst : ""));
                if (!city)
                {
                    Check(open == 0, "MARKS V5 in scope: the host edge line stops across every tier-1 mouth over 35 deg", $"{open} of {mouths}");
                    Check(v7Km < 1e-6f, "MARKS V7 in scope: every tier-1 turn-only boundary solid", string.Format(I, "{0:0.00} km", v7Km));
                }
            }

            // ---- the glyphs in scope, for the shots ----
            int shown = 0;
            foreach (var kv in CityMeshes.Marks)
            {
                var e = map.edges[kv.Key];
                foreach (var m in kv.Value)
                {
                    if (m.style != CityMeshes.MkGlyph || shown >= 24) continue;
                    var p = e.PointAt(0.5f * (m.s0 + m.s1));
                    if (!sc.Contains(p)) continue;
                    Line(string.Format(I, "    glyph: {0} on e{1} '{2}' {8} cls {9}{10} s {3:0.0}..{4:0.0} at ({5:0.0}, {6:0.0}), travel heading {7:0}",
                        m.what, e.index, e.name, m.s0, m.s1, p.x, p.y, m.hdg, e.oneway ? "one-way" : "two-way", e.cls, e.link ? " link" : ""));
                    shown++;
                }
            }
            foreach (var n in st.notes) Line("    note: " + n);
            Line("  E1 FLOAT: every mark is a column of its span (coplanar with the pavement around it): lift 0 m; draws +0 (the road's own slot and solid-white texels)");
            // PSX_MARKS_EDGES=e,e,...: every mark on those edges (diagnostics)
            foreach (var tok in (System.Environment.GetEnvironmentVariable("PSX_MARKS_EDGES") ?? "").Split(','))
            {
                if (!int.TryParse(tok.Trim().TrimStart('e'), out int ei) || ei < 0 || ei >= map.edges.Length) continue;
                var ml = CityMeshes.MarksOf(ei);
                Line(string.Format(I, "    marks on e{0} '{1}' (length {2:0.0}): {3}", ei, map.edges[ei].name, map.edges[ei].length, ml == null ? "none" : ml.Count.ToString()));
                if (ml != null)
                    foreach (var m in ml)
                        Line(string.Format(I, "      {0} s {1:0.00}..{2:0.00} k {3} zone {4} {5}", CityMeshes.MarkNames[m.style], m.s0, m.s1, m.k, m.zone, m.what));
            }
        }
    }
}
