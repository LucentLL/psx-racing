using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE PAINT AUDIT (roads pass L2; plan B3/A5, minimal; the hook
    /// <see cref="PaintReport"/>). The owner: "the white and yellow lines need
    /// to mean what they mean" - MUTCD's: yellow between the two directions,
    /// white within one, white edge lines (yellow on a one-way roadway's left),
    /// local streets unmarked (owner Q1).
    ///
    /// Reads the lines AS DRAWN (<see cref="LineModel.LinesAt"/> through the
    /// builder's own <see cref="CityMeshes.AuditView.DrawnLines"/>, at an
    /// unclipped full section: no tile is built, so it is seconds) every 10 m
    /// along every edge in scope, between its junction trims, against the
    /// EXPECTATION its line set says (section LSET: lanes each way, the
    /// centre, marked):
    ///   V1  centre kind: two-way, marked - the drawn yellow is not the
    ///       expected double yellow / TWLTL / none
    ///   V2  the drawn centre more than 5 cm off the boundary between the
    ///       directions' lanes (outside tapers, where lines ease by design)
    ///   V3  colour: a yellow line away from the centre (a two-way) or
    ///       anywhere but a one-way's left edge, or a white line in the
    ///       centre zone
    ///   V4  any line on an UNMARKED edge (Q1)
    ///   V9  a line drawn from fewer than two texels of its texture (Q3)
    /// in km by tier (V9 in lines), with BEFORE: the same expectation against
    /// what the profile's own texture painted before L2 (the profile's
    /// symmetric split, a TWLTL on every odd profile, every road marked).
    /// GATES (Check): V1 = V2 = V3 = V4 = 0 on T1 in scope and city-wide
    /// (cheap), V9 = 0. T2/T3 are reported.
    /// </summary>
    public static partial class CityAudit
    {
        static partial void PaintReport(CityMap map, CityMeshes.Trims trims)
        {
            var sc = ScopeFor("PAINT");
            Line("PAINT (roads pass L2: line meaning, MUTCD; LSET " + (map.edges.Length > 0 && map.edges[0].hasLset ? "present" : "ABSENT - the profiles' own lines") + "): " + sc.Describe());
            var inBox = Measure(map, trims, sc, false);
            var t1City = Measure(map, trims, sc, true);
            for (int t = 1; t <= 3; t++)
            {
                if (!sc.HasTier(t)) continue;
                var r = inBox[t];
                Line(string.Format(CultureInfo.InvariantCulture,
                    "  {0} in scope: two-way marked {1:0.0} km (taper {2:0.0} km skipped), one-way marked {3:0.0} km, unmarked {4:0.0} km; " +
                    "V1 centre kind {5:0.00} km, V2 centre off > 5 cm {6:0.00} km, V3 colour {7:0.00} km, V4 lines on unmarked {8:0.00} km " +
                    "(BEFORE L2, the profiles' lines: V1 {9:0.00} V2 {10:0.00} V3 {11:0.00} V4 {12:0.00} km; {13:0.0} of {14:0.0} two-way km wrong)",
                    CityTier.Short(t), r.tw, r.taper, r.ow, r.unmarked, r.v1, r.v2, r.v3, r.v4, r.b1, r.b2, r.b3, r.b4, r.bWrongTw, r.tw));
                if (r.worst != null) Line("    worst: " + r.worst);
            }
            var c1 = t1City[1];
            Line(string.Format(CultureInfo.InvariantCulture,
                "  T1 CITY-WIDE: two-way marked {0:0.0} km; V1 {1:0.00} V2 {2:0.00} V3 {3:0.00} V4 {4:0.00} km (BEFORE L2: {5:0.0} of {0:0.0} two-way km wrong; V4 {6:0.00} km)",
                c1.tw, c1.v1, c1.v2, c1.v3, c1.v4, c1.bWrongTw, c1.b4));
            if (c1.worst != null) Line("    worst: " + c1.worst);
            // V9: every layout drawn in scope, and every profile texture
            int v9 = 0, v9Lines = 0; string v9Worst = null;
            var seen = new HashSet<LineModel.Layout>();
            for (int p = 0; p < RoadProfiles.Count; p++) seen.Add(LineModel.LayoutOf(p));
            foreach (var e in map.edges) if (sc.Takes(e)) seen.Add(LineModel.LayoutOf(e));
            int layouts = 0, synthLines = 0, pairKept = 0; string pairWhere = null;
            foreach (var lay in seen)
            {
                layouts++;
                for (int k = 0; k < lay.m.Length; k++)
                {
                    v9Lines++;
                    if (lay.synth[k]) synthLines++;
                    int n = lay.x1[k] - lay.x0[k] + 1;
                    if (n >= 2) continue;
                    // a centre pair too fine for two texels and a gap keeps its
                    // 12 cm lines (RoadProfiles.YellowHalfOf): listed, not a fault
                    bool yellow = lay.kind[k] == LineModel.KYellow || lay.kind[k] == LineModel.KYellowDash;
                    if (lay.twoWay && yellow && lay.yHalf < lay.half) { pairKept++; pairWhere ??= $"{lay.W:0.00} m"; continue; }
                    v9++; v9Worst ??= $"{lay.W:0.00} m layout, line {k} kind {lay.kind[k]} at m {lay.srcM[k]:0.000}: {n} texel of {lay.texW}";
                }
            }
            Line($"  V9 texels per line (Q3: 256 px, at least 2): {v9} of {v9Lines} lines in {layouts} layouts under 2 texels{(v9Worst != null ? " - " + v9Worst : "")}; " +
                 $"{pairKept} centre-pair lines kept at 12 cm so the pair keeps its gap (the {pairWhere ?? "-"} profile: two 2-texel lines and a gap do not fit the pair's 24 cm); " +
                 $"{synthLines} broken lines cut from a solid column (no broken column of that colour in their texture)");
            var b = inBox[1];
            if (sc.HasTier(1))
                Check(b.v1 + b.v2 + b.v3 + b.v4 < 1e-6, "PAINT T1 in scope: V1 = V2 = V3 = V4 = 0",
                      string.Format(CultureInfo.InvariantCulture, "V1 {0:0.00} V2 {1:0.00} V3 {2:0.00} V4 {3:0.00} km", b.v1, b.v2, b.v3, b.v4));
            Check(c1.v1 + c1.v2 + c1.v3 + c1.v4 < 1e-6, "PAINT T1 city-wide: V1 = V2 = V3 = V4 = 0",
                  string.Format(CultureInfo.InvariantCulture, "V1 {0:0.00} V2 {1:0.00} V3 {2:0.00} V4 {3:0.00} km", c1.v1, c1.v2, c1.v3, c1.v4));
            Check(v9 == 0, "PAINT V9: every line at least two texels (Q3)", $"{v9} lines under");
        }

        sealed class PaintTally
        {
            public double tw, ow, unmarked, taper, v1, v2, v3, v4, b1, b2, b3, b4, bWrongTw;
            public string worst; public double worstD;
        }

        /// <summary>The paint tallies by tier: in the report's scope, or
        /// (t1City) T1 city-wide.</summary>
        static PaintTally[] Measure(CityMap map, CityMeshes.Trims trims, AuditScope sc, bool t1City)
        {
            const float Step = 10f, KmStep = Step / 1000f;
            var tally = new PaintTally[4];
            for (int t = 0; t < 4; t++) tally[t] = new PaintTally();
            var lines = new List<LineModel.LineAt>(16);
            foreach (var e in map.edges)
            {
                int tier = CityTier.Of(e);
                if (t1City) { if (tier != 1) continue; }
                else if (!sc.Takes(e)) continue;
                if (e.a == e.b) continue;
                var T = tally[tier];
                var lay = LineModel.LayoutOf(e);
                var pr = RoadProfiles.All[e.profile];
                RoadProfiles.DefaultSplit(pr, out int dF, out int dB, out int dC);
                bool marked = !e.hasLset || (e.lsFlags & 1) != 0;
                int expC = e.hasLset ? e.lsCentre : dC;
                int expB = e.hasLset ? e.lsNB : dB;
                float plus = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmPlus : e.width * 0.5f;
                float divider = LineModel.DividerLat(e);
                // BEFORE (the profile's own lines, every road marked): its centre
                // and where it was
                float oldDivider = plus - e.shl - (dB + (dC == 2 ? 0.5f : 0f)) * RoadProfiles.LaneM;
                bool bKind = !e.oneway && marked && expC != dC;
                bool bOff = !e.oneway && marked && expC != 0 && dC != 0 && Mathf.Abs(oldDivider - divider) > 0.05f;
                bool bV4 = !marked;
                float s0 = trims.TrimAt(e, e.a) + Step * 0.5f, s1 = e.length - trims.TrimAt(e, e.b);
                for (float s = s0; s < s1; s += Step)
                {
                    if (!t1City && !sc.Contains(e.PointAt(s))) continue;
                    if (!marked) T.unmarked += KmStep; else if (e.oneway) T.ow += KmStep; else T.tw += KmStep;
                    if (bV4) T.b4 += KmStep;
                    if (bKind) T.b1 += KmStep;
                    if (bOff) T.b2 += KmStep;
                    if (bKind || bOff) T.b3 += KmStep;   // a moved / changed centre paints yellow where white belongs
                    if (!e.oneway && marked && (bKind || bOff)) T.bWrongTw += KmStep;
                    LineModel.Extents(e, s, out float eM, out float eP);
                    float shift = LineModel.ShiftAt(e, s);
                    bool taper = Mathf.Abs(eM - e.lmMinus) > 1e-3f || Mathf.Abs(eP - e.lmPlus) > 1e-3f || Mathf.Abs(shift) > 1e-3f;
                    var view = new CityMeshes.AuditView.SectionView
                    {
                        s = s, L = new Vector3(-eM, 0f, 0f), R = new Vector3(eP, 0f, 0f), P = Vector2.zero, right = Vector2.right,
                        innerSide = 0, nbL = -1, nbR = -1, stripL = -1f, stripR = -1f,
                    };
                    CityMeshes.AuditView.DrawnLines(e, view, lines);
                    if (!marked)
                    {
                        if (lines.Count > 0) { T.v4 += KmStep; Worst(T, 1f, e, s, $"{lines.Count} lines on an unmarked {SubName(e)}"); }
                        continue;
                    }
                    if (taper) { if (!e.oneway) T.taper += KmStep; continue; }
                    if (e.oneway)
                    {
                        // a one-way: yellow only as the left edge line
                        foreach (var l in lines)
                        {
                            byte k = lay.kind[l.k];
                            if (k == LineModel.KYellow || k == LineModel.KYellowDash) { T.v3 += KmStep; Worst(T, 1f, e, s, "a yellow line inside a one-way"); break; }
                        }
                        continue;
                    }
                    int ys = 0, yd = 0; float ySum = 0f;
                    foreach (var l in lines)
                    {
                        byte k = lay.kind[l.k];
                        if (k == LineModel.KYellow) { ys++; ySum += l.lat; }
                        else if (k == LineModel.KYellowDash) { yd++; ySum += l.lat; }
                    }
                    int drawnC = ys == 0 && yd == 0 ? 0 : ys == 2 && yd == 0 ? 1 : ys == 2 && yd == 2 ? 2 : -1;
                    float centreLat = divider + shift;
                    if (drawnC != expC) { T.v1 += KmStep; Worst(T, 2f, e, s, $"centre drawn {CentreName(drawnC)}, expected {CentreName(expC)}"); }
                    if (ys + yd > 0)
                    {
                        float off = Mathf.Abs(ySum / (ys + yd) - centreLat);
                        if (off > 0.05f) { T.v2 += KmStep; Worst(T, 1f + off, e, s, $"centre {off:0.00} m off the boundary"); }
                    }
                    // colour: yellows inside the centre zone, whites outside it
                    float zone = expC == 2 ? RoadProfiles.LaneM * 0.5f + 0.3f : 0.3f;
                    foreach (var l in lines)
                    {
                        byte k = lay.kind[l.k];
                        bool yellow = k == LineModel.KYellow || k == LineModel.KYellowDash;
                        bool inZone = expC != 0 && Mathf.Abs(l.lat - centreLat) < zone;
                        if (yellow != inZone && k != LineModel.KEdgeP && k != LineModel.KEdgeM)
                        { T.v3 += KmStep; Worst(T, 1.5f, e, s, yellow ? "a yellow line within one direction" : "a white line between the directions"); break; }
                    }
                }
            }
            return tally;
        }

        static void Worst(PaintTally T, float d, CityMap.Edge e, float s, string what)
        {
            if (T.worst != null && d <= T.worstD) return;
            var p = e.PointAt(s);
            T.worstD = d;
            T.worst = string.Format(CultureInfo.InvariantCulture, "e{0} '{1}' {2} s={3:0} ({4:0}, {5:0}): {6} [LSET {7}/{8}/{9} flags {10}]",
                e.index, e.name, RoadProfiles.All[e.profile].key, s, p.x, p.y, what, e.lsNF, e.lsNB, e.lsCentre, e.lsFlags);
        }
        static string CentreName(int c) => c == 0 ? "none" : c == 1 ? "double yellow" : c == 2 ? "TWLTL" : "other";
        static string SubName(CityMap.Edge e) => e.lsSub switch
        {
            1 => "residential", 2 => "unclassified", 3 => "living street", 4 => "service road", 5 => "parking aisle",
            6 => "driveway", 7 => "alley", 8 => "drive-through", _ => CityTier.Name(CityTier.Of(e)),
        };
    }
}
