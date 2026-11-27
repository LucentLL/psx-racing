using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Road-surface round 2: THE PAINT OF A MEASURED EDGE (PSX_CITY_ROADSURFACE only). The fitted lanes' lines are the
    /// layout's own, moved by LineModel.LinesAt's hook; this adds what the layout has no line for, as marks cut into the
    /// ribbon (CityMeshes.Marks):
    ///   FIT, city-wide rules
    ///     - beside a bay the lanes' left line is its own solid white line (the bay's line is the pocket's / the zone's);
    ///     - a side the measure calls PAINT (a painted median): the lanes' left line doubled in yellow and the buffer
    ///       between it and the measured edge hatched in yellow, diagonal, slanting away from the lanes;
    ///     - a side the measure calls a SHOULDER keeps its edge line (the fit lays it the shoulder's width in).
    ///   PICKS (hand file)
    ///     - a picked boundary's kind (solid white, solid yellow, none) overrides the layout's line there;
    ///     - a line of paint that bounds no lane (a bay's line, a buffer's far side), an arrow at a station.
    /// </summary>
    public static partial class CityMeshes
    {
        public const byte MkBand = 9, MkPick = 10;
        const float MeasHatchStepM = 3.0f, MeasHatchBarW = 0.30f, MeasHatchMinW = 0.8f;

        static void MeasureMarks(CityMap map, Trims t)
        {
            if (!RoadSurfaceOn || JunctionMeasures.Fits.Count == 0) return;
            var set = JunctionMeasures.Current;
            int bands = 0, picks = 0, hatches = 0, arrows = 0;
            foreach (var f in JunctionMeasures.Fits.Values)
            {
                var e = map.edges[f.edge];
                var lay = LineModel.LayoutOf(e);
                float mRight = 0f;
                foreach (float m in lay.m) mRight = Mathf.Max(mRight, m);
                int KOf(int index) { for (int k = 0; k < lay.m.Length; k++) if (Mathf.Abs(mRight - lay.m[k] - index * RoadProfiles.LaneM) < 0.3f) return k; return -1; }
                JunctionMeasures.EdgeM em = null;
                if (set != null) set.edges.TryGetValue(f.edge, out em);
                float sLo = t.TrimAt(e, e.a), sHi = e.length - t.TrimAt(e, e.b);
                if (f.oneway)
                {
                    // beside a bay: the lanes' own left line
                    Runs(e, sLo, sHi, s => f.Lerp(f.wgt, s) >= 0.5f && LineModel.AuxWidth(e, s, 1) > 0.3f, (s0, s1) =>
                    {
                        AddMark(new PaintMark { style = MkBand, edge = e.index, s0 = s0, s1 = s1, side = 1, zone = -1, what = "fit: the lanes' left line beside a bay" });
                        bands++;
                    });
                    // a painted median: the second yellow line and the hatching
                    if (em != null && em.kindL == 2)
                        Runs(e, sLo, sHi, s => f.Lerp(f.wgt, s) >= 0.5f && LineModel.AuxWidth(e, s, 1) <= 0.3f && f.Lerp(f.leftRaw, s) - BandL(f, s) > MeasHatchMinW, (s0, s1) =>
                        {
                            AddMark(new PaintMark { style = MkBand, edge = e.index, s0 = s0, s1 = s1, side = 1, yellow = true, off = 0.25f, zone = -1, what = "fit: a painted median's double yellow" });
                            if (Hatch(e, f, s0, s1)) hatches++;
                            bands++;
                        });
                }
                if (f.lines != null)
                    foreach (var ln in f.lines)
                    {
                        if (ln.st.Count < 2) continue;
                        float a = Mathf.Max(sLo, ln.st[0].x), b = Mathf.Min(sHi, ln.st[ln.st.Count - 1].x);
                        if (b - a < 0.5f) continue;
                        if (ln.paintOnly || ln.index > f.nRule)
                        {
                            if (ln.kind != JunctionMeasures.LkNone) { AddMark(new PaintMark { style = MkPick, edge = e.index, s0 = a, s1 = b, pick = ln, zone = -1, what = "pick: a line of paint" }); picks++; }
                            continue;
                        }
                        int k = f.oneway ? KOf(ln.index) : -1;
                        if (k < 0) continue;
                        byte st = ln.kind == JunctionMeasures.LkSolid ? MkLS : ln.kind == JunctionMeasures.LkNone ? MkOff : ln.kind == JunctionMeasures.LkYellow ? MkYS : (byte)255;
                        if (st == 255) continue;
                        // the left edge line lies at the median where a bay runs (the band's own line is MkBand there)
                        if (ln.index == f.nRule) Runs(e, a, b, s => LineModel.AuxWidth(e, s, 1) <= 0.3f, (s0, s1) => AddMark(new PaintMark { style = st, edge = e.index, s0 = s0, s1 = s1, k = k, zone = -1, what = "pick: line kind" }));
                        else AddMark(new PaintMark { style = st, edge = e.index, s0 = a, s1 = b, k = k, zone = -1, what = "pick: line kind" });
                        picks++;
                    }
                if (em != null && JunctionMeasures.PicksOn)
                    foreach (var ar in em.arrows)
                        if (ar.x - ArrowH > sLo + 0.5f && ar.x < sHi - 0.5f && MeasureArrow(e, ar.x, ar.y, ar.z < 0f ? -1 : 1)) arrows++;
            }
            if (bands + picks + hatches + arrows > 0) Debug.Log($"[City] measured edges' paint: {bands} fitted line runs, {hatches} painted medians hatched, {picks} picked lines, {arrows} picked arrows");
        }

        static float BandL(JunctionMeasures.FitE f, float s)
        {
            int top = Mathf.Clamp(Mathf.RoundToInt(f.Lerp(f.nEff, s)), 1, f.nRule);
            return f.Bnd(top, s);
        }

        /// <summary>The runs of [sLo, sHi] where a test holds (1 m samples; runs under 2 m dropped).</summary>
        static void Runs(CityMap.Edge e, float sLo, float sHi, System.Func<float, bool> on, System.Action<float, float> run)
        {
            float start = float.NaN;
            for (float s = sLo; ; s += 1f)
            {
                bool end = s >= sHi; float sc = Mathf.Min(s, sHi);
                bool v = on(sc);
                if (v && float.IsNaN(start)) start = sc;
                if ((!v || end) && !float.IsNaN(start))
                {
                    float stop = v ? sc : sc - 1f;
                    if (stop - start >= 2f) run(start, stop);
                    start = float.NaN;
                }
                if (end) break;
            }
        }

        /// <summary>Yellow diagonal bars over a painted median's half on this edge: from the doubled yellow line to the
        /// measured edge (the median's middle), 45 degrees, slanting away from the lanes in the direction of travel.</summary>
        static bool Hatch(CityMap.Edge e, JunctionMeasures.FitE f, float s0, float s1)
        {
            var pieces = new List<Vector2[]>(16); var bps = new List<float>(64);
            float h = MeasHatchBarW * 1.41421356f;
            float Lo(float s) => BandL(f, s) + 0.25f + 0.2f;
            float Hi(float s) => f.Lerp(f.leftRaw, s);
            for (float y = s0 + 0.5f; y < s1 - 0.5f; y += MeasHatchStepM)
            {
                float lo = Lo(y), hi = Hi(y);
                float rise = hi - lo;
                if (rise < 0.4f || y + rise + h > s1) continue;
                var pc = new[] { new Vector2(lo, y), new Vector2(Lo(y + h), y + h), new Vector2(Hi(y + rise + h), y + rise + h), new Vector2(Hi(y + rise), y + rise) };
                pieces.Add(pc);
                foreach (var v in pc) bps.Add(v.y);
            }
            if (pieces.Count < 2) return false;
            bps.Sort();
            var u = new List<float>(bps.Count);
            foreach (float b in bps) if (u.Count == 0 || b - u[u.Count - 1] > 1e-3f) u.Add(b);
            var tg = e.TangentAt(0.5f * (s0 + s1));
            AddMark(new PaintMark { style = MkGlyph, edge = e.index, s0 = s0, s1 = s1, pieces = pieces, bps = u.ToArray(), side = 1, yellow = true, zone = -1, what = "fit: a painted median's hatching",
                                    hdg = Mathf.Repeat(Mathf.Atan2(tg.x, tg.y) * Mathf.Rad2Deg, 360f) });
            return true;
        }

        /// <summary>A turn arrow (the existing stamp), its head at sRef, centred on lat.</summary>
        static bool MeasureArrow(CityMap.Edge e, float sRef, float lat, int turn)
        {
            float sLo = sRef - ArrowH, sHi = sRef;
            glyphLocal.Clear();
            Arrow(glyphLocal, turn);
            var pieces = new List<Vector2[]>(glyphLocal.Count);
            var bps = new List<float>(16);
            foreach (var p in glyphLocal)
            {
                var q = new Vector2[p.Length];
                for (int i = 0; i < p.Length; i++)
                {
                    float s = sRef - (ArrowH - p[i].y);
                    q[i] = new Vector2(lat - p[i].x, s);
                    bps.Add(s);
                }
                pieces.Add(q);
            }
            bps.Sort();
            var u = new List<float>(bps.Count);
            foreach (float b in bps) if (u.Count == 0 || b - u[u.Count - 1] > 1e-3f) u.Add(b);
            var tg = e.TangentAt(0.5f * (sLo + sHi));
            AddMark(new PaintMark { style = MkGlyph, edge = e.index, s0 = sLo, s1 = sHi, pieces = pieces, bps = u.ToArray(), side = -1, zone = -1, what = "pick: arrow",
                                    hdg = Mathf.Repeat(Mathf.Atan2(tg.x, tg.y) * Mathf.Rad2Deg, 360f) });
            return true;
        }
    }
}
