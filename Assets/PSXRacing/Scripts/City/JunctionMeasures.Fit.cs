using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Road-surface round 2: WHERE LANES COME FROM (the coordinator's ruling). Measured kerbs and islands first, lanes
    /// second: a lane never lies on a kerb or an island, the lanes are FITTED INTO the clear pavement. Two providers behind
    /// one socket (a FitE per measured edge, read by LineModel.LinesAt, the lane centre, the lane check and the gates):
    ///   FIT    at each station the clear width of a carriageway = from its measured outer kerb (a one-way's right edge,
    ///          less a shoulder where the measure names one) to the nearest measured island or median edge, less a bay
    ///          where the rule lays one (the bay keeps its place against the median). Lane count = the smaller of the
    ///          map's count (capped by a count the measure carries: "lanes N", the state's count later) and what fits at
    ///          FitMinLaneM; lane width = clear / count held to 3.0 - 3.66 m; what is left over lies on the median side
    ///          (a buffer between the lanes' left line and the bay or the median). A change of count along the edge is a
    ///          taper (FitTaperM a lane). A two-way edge is fitted between its two measured edges about their middle.
    ///   PICKS  (hand file only) "line INDEX KIND" + "p STATION OFFSET" rows under an edge: boundary INDEX of the lane
    ///          band counted from the outer (right) edge, 0 = the outer edge of lane 1; its offset from the map line (+ =
    ///          left of a -> b) at stations and its paint. Where present they replace FIT's place for that boundary and
    ///          name the paint (CityMeshes.Marks draws the kinds and the lines beyond the layout's own).
    /// </summary>
    public static partial class JunctionMeasures
    {
        public const float FitMinLaneM = 3.0f, FitTaperM = 30f, FitIslandSlope = 1f / 15f;
        /// <summary>One measured edge's lanes, every FeedStepM along it.</summary>
        public sealed class FitE
        {
            public int edge, nRule; public bool oneway;
            /// <summary>wgt: the fit's weight over the rule (1 on the measure). bnd[k][i]: boundary i's offset (0 the
            /// outer edge of lane 1 .. nRule; NaN: no such boundary there). edgeP: where a one-way's left edge line
            /// lies (the band's left edge; the median's edge where a bay runs). nEff: lanes (a fraction through a
            /// taper). laneW; leftPave: the measured pavement's left edge less its shoulder; shift: the measure's shift
            /// ease; scale + anchor: a two-way's lines (lat = anchor + metres from the right lane edge * scale).</summary>
            public float[] wgt, edgeP, nEff, laneW, leftPave, shift, anchor, scale, buffer;
            public float[][] bnd;
            public List<LineM> lines;
            public float Lerp(float[] a, float s)
            {
                if (a.Length == 1) return a[0];
                float x = Mathf.Clamp(s / FeedStepM, 0f, a.Length - 1); int i = Mathf.Min((int)x, a.Length - 2);
                return Mathf.Lerp(a[i], a[i + 1], x - i);
            }
            public float Bnd(int i, float s)
            {
                float x = Mathf.Clamp(s / FeedStepM, 0f, bnd.Length - 1); int k = Mathf.Min((int)x, Mathf.Max(0, bnd.Length - 2));
                float a = bnd[k][i], b = bnd[Mathf.Min(k + 1, bnd.Length - 1)][i];
                if (float.IsNaN(a)) return b;
                if (float.IsNaN(b)) return a;
                return Mathf.Lerp(a, b, x - k);
            }
        }
        public static readonly Dictionary<int, FitE> Fits = new Dictionary<int, FitE>();
        public static FitE FitOf(int edge) => Fits.Count > 0 && Fits.TryGetValue(edge, out var f) ? f : null;
        /// <summary>PSX_CITY_LANEPICKS=0: the hand file's lane picks are not read (the FIT-only run of a measured junction).</summary>
        public static bool PicksOn => System.Environment.GetEnvironmentVariable("PSX_CITY_LANEPICKS") != "0";

        /// <summary>Where the section of an edge at s (the line through pt along left) first meets a measured island,
        /// from offset "from" leftward: the island's near edge, or +infinity.</summary>
        static float IslandFrom(Set set, Vector2 pt, Vector2 left, float from, float upTo)
        {
            float best = float.PositiveInfinity;
            var tan = new Vector2(left.y, -left.x);
            foreach (var j in set.junctions)
                foreach (var im in j.islands)
                {
                    int n = im.pts.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var a = im.pts[i] - pt; var b = im.pts[(i + 1) % n] - pt;
                        // along the section: u = dot(left); across it: v = dot(tangent); the island's side crosses v = 0
                        float va = Vector2.Dot(a, tan), vb = Vector2.Dot(b, tan);
                        if ((va > 0f) == (vb > 0f)) continue;
                        float t = va / (va - vb);
                        float u = Vector2.Dot(a + (b - a) * t, left);
                        if (u > from && u < upTo && u < best) best = u;
                    }
                }
            return best;
        }

        /// <summary>The lanes of a measured edge: called by FeedLineModel with the edge's measured sides per station
        /// (mL / mR: the pavement's left / right offset, wL / wR: the measure's weight there, 0 where a side is the
        /// rule's). Returns the shift ease's samples (the lanes' centre off the rule's), null when the edge is not fitted.</summary>
        static float[] BuildFit(Set set, CityMap.Edge e, EdgeM em, float[] mL, float[] mR, float[] wL, float[] wR)
        {
            int n = mL.Length;
            var pr = RoadProfiles.All[e.profile];
            bool oneway = pr.oneway;
            int nRule = pr.lanes;
            if (nRule < 1) return null;
            var f = new FitE { edge = e.index, oneway = oneway, nRule = nRule, wgt = new float[n], edgeP = new float[n], nEff = new float[n], laneW = new float[n],
                               leftPave = new float[n], shift = new float[n], anchor = new float[n], scale = new float[n], buffer = new float[n], bnd = new float[n][],
                               lines = PicksOn ? em.lines : null };
            float gL = em.kindL == 1 ? em.shoulderL : 0f, gR = em.kindR == 1 ? em.shoulderR : 0f;
            var clear = new float[n]; var A = new float[n]; var cnt = new int[n];
            int cap = em.lanes > 0 ? Mathf.Min(nRule, em.lanes) : nRule;
            bool any = false;
            for (int k = 0; k < n; k++)
            {
                float s = Mathf.Min(k * FeedStepM, e.length);
                float w = oneway ? wR[k] : Mathf.Min(wL[k], wR[k]);
                f.wgt[k] = w; any |= w > 0f;
                var pt = e.PointAt(s); var t = e.TangentAt(s); var left = new Vector2(-t.y, t.x);
                float auxP = LineModel.AuxWidth(e, s, 1), auxM = LineModel.AuxWidth(e, s, -1);
                A[k] = -mR[k] + gR + auxM;
                float lim = mL[k] - gL - auxP;
                f.leftPave[k] = mL[k] - gL;
                if (set != null) lim = Mathf.Min(lim, IslandFrom(set, pt, left, A[k] + 1.5f, mL[k]));
                clear[k] = lim - A[k];
            }
            if (!any) return null;
            // an island (or a narrowing) is met by a taper, not a step
            for (int k = 1; k < n; k++) clear[k] = Mathf.Min(clear[k], clear[k - 1] + FeedStepM * FitIslandSlope);
            for (int k = n - 2; k >= 0; k--) clear[k] = Mathf.Min(clear[k], clear[k + 1] + FeedStepM * FitIslandSlope);
            for (int k = 0; k < n; k++) cnt[k] = Mathf.Clamp(Mathf.FloorToInt(clear[k] / FitMinLaneM + 1e-3f), 1, cap);
            // a change of count is a taper of FitTaperM a lane, laid on the wider side of the change
            for (int k = 0; k < n; k++) f.nEff[k] = cnt[k];
            for (int k = 1; k < n; k++) f.nEff[k] = Mathf.Min(f.nEff[k], f.nEff[k - 1] + FeedStepM / FitTaperM);
            for (int k = n - 2; k >= 0; k--) f.nEff[k] = Mathf.Min(f.nEff[k], f.nEff[k + 1] + FeedStepM / FitTaperM);
            var sh = new float[n];
            for (int k = 0; k < n; k++)
            {
                float s = Mathf.Min(k * FeedStepM, e.length);
                float nE = f.nEff[k];
                float lw = Mathf.Clamp(clear[k] / Mathf.Max(1f, Mathf.Ceil(nE - 1e-3f)), Mathf.Min(FitMinLaneM, clear[k]), RoadProfiles.LaneM);
                f.laneW[k] = lw;
                float a0 = A[k];
                if (!oneway)
                {
                    // a two-way: the lanes about the middle of the two measured edges
                    float mid = 0.5f * ((mL[k] - gL) + (-mR[k] + gR));
                    a0 = mid - 0.5f * nE * lw;
                }
                f.anchor[k] = a0; f.scale[k] = lw / RoadProfiles.LaneM;
                var row = new float[nRule + 1];
                for (int i = 0; i <= nRule; i++) row[i] = i <= nE + 0.5f ? a0 + Mathf.Min(i, nE) * lw : float.NaN;
                // the picks replace the fit's place for their boundary
                if (f.lines != null)
                    foreach (var ln in f.lines)
                    {
                        if (ln.paintOnly || ln.index < 0 || ln.index > nRule) continue;
                        if (ln.At(s, out float off, out float pw)) row[ln.index] = float.IsNaN(row[ln.index]) ? off : Mathf.Lerp(row[ln.index], off, pw);
                    }
                f.bnd[k] = row;
                int top = Mathf.Clamp(Mathf.RoundToInt(nE), 1, nRule);
                float b0 = row[0], bT = float.IsNaN(row[top]) ? a0 + nE * lw : row[top];
                float auxP = LineModel.AuxWidth(e, s, 1);
                f.buffer[k] = (mL[k] - gL - auxP) - bT;
                f.edgeP[k] = oneway ? Mathf.Lerp(bT, Mathf.Max(bT, mL[k] - gL - e.shl), Mathf.Clamp01(auxP / 3f)) : bT;
                // the lanes' centre: the shift ease carries it (LineModel.LaneCentre, the traffic, the AI)
                LineModel.ExtentsBefore(e, s, out float lM, out float lP);
                float ruleC = 0.5f * ((lP - e.shl) - (lM - e.shr));
                sh[k] = f.wgt[k] * (0.5f * (b0 + bT) - ruleC);
                f.shift[k] = sh[k];
            }
            Fits[e.index] = f;
            return sh;
        }

        /// <summary>LineModel.LinesAt's hook: the lines of a fitted edge at s where the fit (or a pick) puts them.
        /// "into" holds the rule's lines (k, lat), lateral ascending; a boundary the fit drops is removed.</summary>
        public static void FitLines(CityMap.Edge e, float s, LineModel.Layout lay, List<LineModel.LineAt> into)
        {
            var f = FitOf(e.index);
            if (f == null) return;
            float wg = f.Lerp(f.wgt, s);
            if (wg <= 1e-4f) return;
            float shift = f.Lerp(f.shift, s);
            float mRight = 0f;
            for (int k = 0; k < lay.m.Length; k++) if (lay.m[k] > mRight) mRight = lay.m[k];
            int w = 0;
            for (int q = 0; q < into.Count; q++)
            {
                var ln = into[q]; byte kind = lay.kind[ln.k];
                float tm = mRight - lay.m[ln.k];
                float fit;
                if (f.oneway)
                {
                    if (kind == LineModel.KEdgeP) fit = f.Lerp(f.edgeP, s);
                    else
                    {
                        int idx = Mathf.Clamp(Mathf.RoundToInt(tm / RoadProfiles.LaneM), 0, f.nRule);
                        fit = f.Bnd(idx, s);
                        if (float.IsNaN(fit) || (idx > 0 && idx > f.Lerp(f.nEff, s) - 0.5f))
                        {
                            if (wg > 0.5f) continue;
                            fit = ln.lat - shift;
                        }
                    }
                }
                else fit = f.Lerp(f.anchor, s) + tm * f.Lerp(f.scale, s);
                ln.lat = Mathf.Lerp(ln.lat - shift, fit, wg);
                into[w++] = ln;
            }
            into.RemoveRange(w, into.Count - w);
            into.Sort((a, b) => a.lat.CompareTo(b.lat));
        }

        /// <summary>The lanes of an edge at s as the fit lays them (their centres, offsets from the map line, right to
        /// left) and the band they fill. False: the edge is not fitted there (the rule's lanes hold).</summary>
        public static bool LaneCentres(CityMap.Edge e, float s, List<float> into, out float lo, out float hi)
        {
            into.Clear(); lo = hi = 0f;
            var f = FitOf(e.index);
            if (f == null || f.Lerp(f.wgt, s) < 0.5f) return false;
            float nE = f.Lerp(f.nEff, s); int top = Mathf.Clamp(Mathf.RoundToInt(nE), 1, f.nRule);
            float prev = f.Bnd(0, s);
            lo = prev;
            for (int i = 1; i <= top; i++)
            {
                float b = f.Bnd(i, s);
                if (float.IsNaN(b)) break;
                into.Add(0.5f * (prev + b)); prev = b;
            }
            hi = prev;
            return into.Count > 0;
        }

        /// <summary>The left edge of the lane band of a fitted one-way at s, and the buffer's width beyond it (to the bay
        /// or the median's edge). False: not fitted.</summary>
        public static bool BandLeft(CityMap.Edge e, float s, out float lat, out float buffer)
        {
            lat = buffer = 0f;
            var f = FitOf(e.index);
            if (f == null || !f.oneway || f.Lerp(f.wgt, s) < 0.5f) return false;
            float nE = f.Lerp(f.nEff, s); int top = Mathf.Clamp(Mathf.RoundToInt(nE), 1, f.nRule);
            lat = f.Bnd(top, s); buffer = f.Lerp(f.buffer, s);
            return !float.IsNaN(lat);
        }
    }
}
