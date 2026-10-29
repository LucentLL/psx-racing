using System.Collections.Generic;
using UnityEngine;
namespace PSXRacing.City
{
    /// <summary>
    /// THE CURVE ADVISORIES (road-logic-bends, 2026-10-29): ONE source of truth
    /// for the exit-speed advisory sign (CitySigns.Guide, W13-2 "EXIT 25 MPH"),
    /// the traffic's speeds (CityTraffic) and the traffic census
    /// (CityTraffic.Census).
    ///
    /// The census (Docs/ROAD_LOGIC_TRAFFIC.md, run 1) counted 13,174 bends
    /// tighter than their limit allows - 7,720 "bend-tighter-than-limit" and
    /// 5,454 "ramp-bend-no-limit" (a ramp with no speed at all) - and 99% of
    /// them lie where the drawn centreline itself bends as tight: real curves
    /// signed at the road's limit. Where the line was wrong (a mapped curve
    /// drawn as short tight arcs, the freeways) the exporter re-fits it
    /// (tools/city/lib/fillet.mjs, BENDS); what is left is real, and real
    /// practice gives such a curve an ADVISORY speed (MUTCD 2C.08 / 2C.14): the
    /// ball-bank speed, here AASHTO's V^2 = 15 R (e + f) (V mph, R ft) at the
    /// e + f the highway-signs pass already used (0.22), rounded DOWN to 5 mph.
    ///
    ///   * An edge whose OSM way posts maxspeed:advisory (section ADVS) keeps
    ///     that number over its whole length - the mapped sign.
    ///   * Every other edge: R every <see cref="StepM"/> from the drawn
    ///     centreline's heading change over <see cref="WindowM"/>, taken at the
    ///     INNERMOST lane (R less the lanes' half width less half a lane: the
    ///     tightest path a car on it drives). Where the speed that gives is under
    ///     <see cref="CapMph"/> a SPAN runs over it, padded <see cref="PadM"/>
    ///     either side (the census looks back 6 m; the traffic brakes to the
    ///     span's start); spans closer than <see cref="MergeM"/> are one, at the
    ///     lower speed.
    ///
    /// A caller takes min(its limit, the advisory): a span over the limit
    /// changes nothing. Lazy per edge; <see cref="Reset"/> on a new map.
    /// </summary>
    public static class CityCurves
    {
        /// <summary>AASHTO side friction + superelevation for an advisory curve
        /// (the signs pass's number since the highway-signs pass).</summary>
        public const float EPlusF = 0.22f;
        public const float WindowM = 10f, StepM = 2f, PadM = 12f, MergeM = 20f;
        /// <summary>Advisories run 10 mph and up (a hairpin is "10 MPH").</summary>
        public const int MinMph = 10;
        /// <summary>No span at or over this: Charlotte's highest limit is 70 mph.</summary>
        public const int CapMph = 70;
        /// <summary>The exit sign skips a span over within this of the diverge.</summary>
        public const float GoreM = 40f;
        const float Mph = 0.44704f;

        public struct Span { public float s0, s1; public int mph; }

        static CityMap mapFor;
        static Span[][] spans;
        static readonly Span[] None = new Span[0];

        public static void Reset() { mapFor = null; spans = null; }

        /// <summary>The advisory a curve of radius r (metres) is signed at, mph:
        /// AASHTO's V^2 = 15 R (e + f) rounded down to 5, never under MinMph.</summary>
        public static int MphForRadius(float r)
        {
            float mph = Mathf.Sqrt(15f * Mathf.Max(0f, r) * 3.2808f * EPlusF);
            return Mathf.Max(MinMph, Mathf.FloorToInt(mph / 5f) * 5);
        }

        /// <summary>The advisory spans of edge ei, in arc position on the edge
        /// (an edge with a posted advisory: one span over all of it).</summary>
        public static Span[] Of(CityMap map, int ei)
        {
            if (map == null || ei < 0 || ei >= map.edges.Length) return None;
            if (!ReferenceEquals(mapFor, map) || spans == null || spans.Length != map.edges.Length)
            { mapFor = map; spans = new Span[map.edges.Length][]; }
            return spans[ei] ?? (spans[ei] = Find(map.edges[ei]));
        }

        /// <summary>The advisory at arc position s on edge ei, metres per
        /// second; float.MaxValue where none applies.</summary>
        public static float MpsAt(CityMap map, int ei, float s)
        {
            var sp = Of(map, ei);
            for (int i = 0; i < sp.Length; i++) if (s >= sp[i].s0 && s <= sp[i].s1) return sp[i].mph * Mph;
            return float.MaxValue;
        }

        /// <summary>The lowest advisory between arc positions a and b (either
        /// order) on edge ei, m/s, and the arc position where it starts seen
        /// from a; float.MaxValue where none.</summary>
        public static float MinMpsBetween(CityMap map, int ei, float a, float b, out float at)
        {
            float lo = Mathf.Min(a, b), hi = Mathf.Max(a, b), best = float.MaxValue;
            at = a;
            var sp = Of(map, ei);
            for (int i = 0; i < sp.Length; i++)
            {
                if (sp[i].s1 < lo || sp[i].s0 > hi) continue;
                float v = sp[i].mph * Mph;
                if (v >= best) continue;
                best = v;
                at = b >= a ? Mathf.Max(a, sp[i].s0) : Mathf.Min(a, sp[i].s1);
            }
            return best;
        }

        /// <summary>The exit-speed advisory of a ramp, mph: its posted advisory,
        /// else the lowest span within 300 m of the diverge along the ramp
        /// (its one-way link chain); 0 when none applies. A span over before
        /// <see cref="GoreM"/> is the diverge's own swing off the mainline (OSM
        /// joins the ramp's line to the freeway's at the gore, at an angle):
        /// the traffic slows for it, the sign shows the ramp's curve.</summary>
        public static int ExitMph(CityMap map, CityMap.Edge link)
        {
            if (map == null || link == null) return 0;
            int best = int.MaxValue;
            var e = link; float walked = 0f;
            for (int hop = 0; hop < 8 && walked < 300f; hop++)
            {
                float reach = Mathf.Min(e.length, 300f - walked);
                foreach (var sp in Of(map, e.index))
                    if (sp.s0 <= reach && (e.advisoryMph > 0 || walked + sp.s1 - PadM > GoreM)) best = Mathf.Min(best, sp.mph);
                walked += e.length;
                CityMap.Edge next = null;
                foreach (int oi in map.nodeEdges[e.b])
                {
                    var o = map.edges[oi];
                    if (o != e && o.a == e.b && o.link && o.oneway) { next = o; break; }
                }
                if (next == null) break;
                e = next;
            }
            return best == int.MaxValue ? 0 : best;
        }

        // ------------------------------------------------------------------
        static Span[] Find(CityMap.Edge e)
        {
            if (e.advisoryMph > 0)
                return new[] { new Span { s0 = 0f, s1 = e.length, mph = e.advisoryMph } };
            if (e.length < WindowM * 0.5f || e.pts == null || e.pts.Length < 3) return None;
            // the innermost lane's offset from the centreline
            float inner = Mathf.Max(0f, e.lanes * LaneW(e) * 0.5f - LaneW(e) * 0.5f);
            List<Span> outS = null;
            float runS0 = -1f, runS1 = 0f; int runMph = int.MaxValue;
            float half = WindowM * 0.5f;
            for (float s = 0f; s <= e.length + 1e-3f; s += StepM)
            {
                float a = Mathf.Max(0f, s - half), b = Mathf.Min(e.length, s + half);
                int mph = int.MaxValue;
                if (b - a >= WindowM * 0.5f)
                {
                    float turn = Vector2.Angle(e.TangentAt(a + 0.05f), e.TangentAt(b - 0.05f)) * Mathf.Deg2Rad;
                    if (turn > 1e-4f)
                    {
                        float r = (b - a) / turn - inner;
                        int m = MphForRadius(Mathf.Max(1f, r));
                        if (m < CapMph) mph = m;
                    }
                }
                if (mph != int.MaxValue)
                {
                    if (runS0 < 0f) { runS0 = s; runMph = mph; }
                    else runMph = Mathf.Min(runMph, mph);
                    runS1 = s;
                }
                else if (runS0 >= 0f)
                {
                    Add(ref outS, runS0, runS1, runMph, e.length);
                    runS0 = -1f; runMph = int.MaxValue;
                }
            }
            if (runS0 >= 0f) Add(ref outS, runS0, runS1, runMph, e.length);
            return outS == null ? None : outS.ToArray();
        }

        static void Add(ref List<Span> l, float s0, float s1, int mph, float len)
        {
            var sp = new Span { s0 = Mathf.Max(0f, s0 - PadM), s1 = Mathf.Min(len, s1 + PadM), mph = mph };
            if (l == null) l = new List<Span>();
            if (l.Count > 0 && sp.s0 - l[l.Count - 1].s1 < MergeM)
            {
                var p = l[l.Count - 1];
                p.s1 = sp.s1; p.mph = Mathf.Min(p.mph, sp.mph);
                l[l.Count - 1] = p;
            }
            else l.Add(sp);
        }

        /// <summary>The lane width the line model draws for this edge (the
        /// LANW class width when the data has it).</summary>
        static float LaneW(CityMap.Edge e) => e.lanes > 0 && e.width > 0f ? Mathf.Clamp((e.width - e.shl - e.shr) / Mathf.Max(1, e.lanes), 2.7f, 3.7f) : 3.6576f;
    }
}
