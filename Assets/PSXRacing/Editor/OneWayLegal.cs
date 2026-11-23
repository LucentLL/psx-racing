using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LEGAL DIRECTION, MEASURED FROM OUTSIDE (tier1 11-23, the self-test the aipass one-way fix lacked): a traffic
    /// car is put on the street under it by its own position (the city map's edge whose pavement holds it, at its
    /// height, not in a junction's first or last 10 m), and its heading (its velocity, or its nose when standing) is
    /// compared with the street's legal direction. Nothing of the traffic code's own bookkeeping (its edge, its stream)
    /// is read, so the race strip (TrafficSystem) and the city's free-roam traffic (CityTraffic) are judged alike.
    /// - ONE-WAY street (OSM oneway: the edge's points run with the traffic): heading more than 60 degrees against it =
    ///   WRONG WAY. A DIVIDED road's carriageways are one-way edges with a twin of the same name running the other way
    ///   within 45 m; they are counted apart as "divided".
    /// - TWO-WAY street: a car heading along it on the far side of its centre divider by more than 2.5 m is listed as
    ///   LEFT OF THE DIVIDER (information only: a race strip's lanes are laid about the race line, not the divider).
    /// Used by LaneUseWatch (every race of race-play-check) and OneWayTrafficCheck (free roam).
    /// </summary>
    public static class OneWayLegal
    {
        public const float JunctionSkipM = 10f;
        public class Street { public string name; public bool divided; public int samples, wrong; public string firstWrong; }
        public static readonly Dictionary<string, Street> Streets = new Dictionary<string, Street>();
        public static int Samples, OnOneWay, Wrong, LeftOfDivider, OffMap, ForkExcused;
        public static readonly List<string> WrongLines = new List<string>();
        static readonly Dictionary<int, bool> twinCache = new Dictionary<int, bool>();
        static readonly HashSet<int> segs = new HashSet<int>();
        static CityMap cachedMap;

        public static void Reset()
        {
            Streets.Clear(); WrongLines.Clear();
            Samples = OnOneWay = Wrong = LeftOfDivider = OffMap = ForkExcused = 0;
        }

        /// <summary>Every street whose pavement holds the point (filled by StreetAt): edge, s, lat.</summary>
        public static readonly List<(int edge, float s, float lat)> Holding = new List<(int, float, float)>();

        /// <summary>The street under a point: the edge whose pavement (+1 m) holds it at its height (2.5 m), the
        /// nearest; every edge that holds it is in <see cref="Holding"/> (where two streets meet or fork, both).</summary>
        public static bool StreetAt(CityMap map, Vector3 pos, out int edge, out float s, out float lat)
        {
            edge = -1; s = 0f; lat = 0f;
            Holding.Clear();
            if (map == null) return false;
            var p = new Vector2(pos.x, pos.z);
            segs.Clear();
            map.EdgeSegsInRect(p - new Vector2(16f, 16f), p + new Vector2(16f, 16f), segs);
            float best = float.MaxValue;
            foreach (int packed in segs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.pts == null || si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float at = e.s[si] + Mathf.Sqrt(L2) * t;
                Vector2 q = a + d * t;
                Vector2 tan = L2 > 1e-8f ? d / Mathf.Sqrt(L2) : Vector2.up;
                float la = Vector2.Dot(p - q, new Vector2(-tan.y, tan.x));
                LineModel.Extents(e, Mathf.Clamp(at, 0f, e.length), out float eM, out float eP);
                if (la < -eM - 1f || la > eP + 1f) continue;
                float dy = Mathf.Abs(e.YAt(at) - pos.y);
                if (dy > 2.5f) continue;
                int had = Holding.FindIndex(h => h.edge == ei);
                if (had < 0) Holding.Add((ei, at, la));
                else if (Mathf.Abs(la) < Mathf.Abs(Holding[had].lat)) Holding[had] = (ei, at, la);
                float score = dy * 4f + (p - q).magnitude;
                if (score < best) { best = score; edge = ei; s = at; lat = la; }
            }
            return edge >= 0;
        }

        /// <summary>A one-way edge with a twin: another one-way edge of the same name within 45 m running the other way.</summary>
        public static bool Divided(CityMap map, int ei)
        {
            if (map != cachedMap) { twinCache.Clear(); cachedMap = map; }
            if (twinCache.TryGetValue(ei, out bool v)) return v;
            var e = map.edges[ei];
            v = false;
            if (e.oneway && !string.IsNullOrEmpty(e.name) && e.length > 1f)
            {
                Vector2 mid = e.PointAt(e.length * 0.5f), tan = e.TangentAt(e.length * 0.5f);
                var near = new HashSet<int>();
                map.EdgeSegsInRect(mid - new Vector2(45f, 45f), mid + new Vector2(45f, 45f), near);
                foreach (int packed in near)
                {
                    int oj = packed >> 12;
                    if (oj == ei) continue;
                    var o = map.edges[oj];
                    if (!o.oneway || o.name != e.name || o.link) continue;
                    // the twin's nearest point to our middle, its direction there
                    float bd = float.MaxValue, bs = 0f;
                    for (int k = 0; k + 1 < o.pts.Length; k++)
                    {
                        Vector2 a = o.pts[k], d = o.pts[k + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(mid - a, d) / L2) : 0f;
                        float dd = (a + d * t - mid).sqrMagnitude;
                        if (dd < bd) { bd = dd; bs = o.s[k] + Mathf.Sqrt(L2) * t; }
                    }
                    if (bd < 45f * 45f && Vector2.Dot(o.TangentAt(bs), tan) < -0.7f) { v = true; break; }
                }
            }
            twinCache[ei] = v;
            return v;
        }

        /// <summary>One car, one moment. heading: its velocity when moving (over 1 m/s), else its nose.</summary>
        public static void Sample(CityMap map, string who, Vector3 pos, Vector3 vel, Vector3 nose, string tag, float time)
        {
            Samples++;
            if (!StreetAt(map, pos, out int ei, out float s, out float lat)) { OffMap++; return; }
            var e = map.edges[ei];
            if (s < JunctionSkipM || s > e.length - JunctionSkipM) return;
            Vector3 h3 = new Vector2(vel.x, vel.z).sqrMagnitude > 1f ? vel : nose;
            var h = new Vector2(h3.x, h3.z);
            if (h.sqrMagnitude < 1e-6f) return;
            h.Normalize();
            float along = Vector2.Dot(h, e.TangentAt(s));
            // where streets meet or fork (Holding: every edge whose pavement holds the car), the car is legal if ANY
            // of them allows its heading - a two-way street, a one-way street it drives along, or one whose
            // junction end it is in (N Tryon St's one-way stretch forks into N Church St at node 9907: a car on
            // Tryon there stands on Church's pavement too)
            bool allowed = false;
            foreach (var (oe, os, _) in Holding)
            {
                if (oe == ei) continue;
                var o = map.edges[oe];
                if (!o.oneway || os < JunctionSkipM || os > o.length - JunctionSkipM || Vector2.Dot(h, o.TangentAt(os)) >= -0.5f) { allowed = true; break; }
            }
            if (e.oneway)
            {
                OnOneWay++;
                string key = string.IsNullOrEmpty(e.name) ? "e" + ei : e.name;
                if (!Streets.TryGetValue(key, out var st)) Streets[key] = st = new Street { name = key };
                if (Divided(map, ei)) st.divided = true;
                st.samples++;
                if (along < -0.5f && allowed) ForkExcused++;
                if (along < -0.5f && !allowed)
                {
                    st.wrong++; Wrong++;
                    string line = $"WRONG WAY ({tag}) {who} at {time:0.0}s on one-way '{key}' e{ei} s {s:0} lat {lat:+0.0;-0.0}: heading {Mathf.Acos(Mathf.Clamp(along, -1f, 1f)) * Mathf.Rad2Deg:0} deg off the legal direction, {new Vector2(vel.x, vel.z).magnitude * 3.6f:0} km/h at ({pos.x:0},{pos.z:0})";
                    st.firstWrong ??= line;
                    if (WrongLines.Count < 12) WrongLines.Add(line);
                }
            }
            else if (Mathf.Abs(along) > 0.7f)
            {
                float div = LineModel.DividerLat(e);
                if (along > 0f ? lat > div + 2.5f : lat < div - 2.5f) LeftOfDivider++;
            }
        }

        /// <summary>The tallies as report lines; the streets with samples, most first.</summary>
        public static string Describe(int minSamples)
        {
            var sb = new StringBuilder();
            sb.Append($"car samples {Samples} (off every street's pavement or in a junction's ends: {OffMap}), on one-way streets {OnOneWay}, WRONG WAY {Wrong} (against the nearest one-way street but along another street that holds it too, not counted: {ForkExcused}); two-way, left of the divider by 2.5 m+ (info) {LeftOfDivider}");
            var list = new List<Street>(Streets.Values);
            list.Sort((a, b) => b.samples.CompareTo(a.samples));
            int n = 0;
            foreach (var st in list)
            {
                if (st.samples < minSamples) continue;
                sb.Append($"\n    {(st.divided ? "divided" : "one-way")} '{st.name}': {st.samples} samples, wrong {st.wrong}");
                if (++n >= 14) break;
            }
            foreach (var l in WrongLines) sb.Append("\n    " + l);
            return sb.ToString();
        }

        public static int StreetsCovered(int minSamples, bool divided)
        {
            int n = 0;
            foreach (var st in Streets.Values) if (st.samples >= minSamples && st.divided == divided) n++;
            return n;
        }
    }
}
