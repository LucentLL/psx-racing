using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE MEDIAN ACROSS A DIVIDED ROAD (2026-10-24, the owner's Uptown Loop
    /// screenshots: "median not connecting properly or having random sections
    /// that make boxes or diamonds that result in wrecks"). Every 2 m along a
    /// race route's one-way carriageway that has its opposing twin (same name,
    /// travelling the other way) within <see cref="MedianTwinM"/> and the two
    /// inner edges at most <see cref="MedianGapMaxM"/> apart (a median a real
    /// freeway walls), a ray 0.5 m over the road runs from this carriageway's
    /// inner lane (1.8 m in) to the twin's and counts the Solid-layer walls it
    /// crosses. ONE is a median. Two or more is a box, a diamond or a double
    /// wall; none is a gap. The drive audit calls <see cref="MedianLateral"/>
    /// per route tile (the tile and its ring stood up through CityWorld) and
    /// <see cref="MedianLateralReport"/> once.
    /// </summary>
    public static partial class CityAudit
    {
        const float MedianTwinM = 40f, MedianGapMaxM = 15f, MedianStepM = 2f, MedianSpotM = 25f;
        static int medSamples, medZero, medOne, medMany, medSplit, medWide;
        static readonly Dictionary<string, int[]> medByName = new Dictionary<string, int[]>();
        static readonly List<(Vector2 p, string note)> medSpots = new List<(Vector2, string)>();
        static readonly HashSet<long> medDone = new HashSet<long>();
        static readonly HashSet<int> medSegs = new HashSet<int>();
        static readonly Dictionary<int, (string route, float at)> medRouteAt = new Dictionary<int, (string, float)>();
        /// <summary>(round 7) every ray in route order: walls crossed (-1 skipped: split level or wider band), for the classes.</summary>
        static readonly List<(string route, float at, int n, Vector2 p, int edge, float s)> medSeq = new List<(string, float, int, Vector2, int, float)>(8192);

        static void MedianLateralReset(CityMap map)
        {
            medSamples = medZero = medOne = medMany = medSplit = medWide = 0;
            medByName.Clear(); medSpots.Clear(); medDone.Clear(); medRouteAt.Clear(); medSeq.Clear();
            foreach (var r in map.routes)
            {
                float acc = 0f;
                foreach (int ei in r.edges)
                {
                    if (!medRouteAt.ContainsKey(ei)) medRouteAt[ei] = (r.id, acc);
                    acc += map.edges[ei].length;
                }
            }
        }

        static Vector3 MedianLaneIn(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, float s, int side, float inward)
        {
            CityMeshes.LaneExtents(map, trims, e, s, out float hwL, out float hwR);
            var p = e.PointAt(s); var tan = e.TangentAt(s);
            var right = new Vector2(-tan.y, tan.x);
            float lat = side > 0 ? hwR - inward : -(hwL - inward);
            var q = p + right * lat;
            return new Vector3(q.x, e.YAt(s) + 0.5f, q.y);
        }

        /// <summary>The median rays of the route edges on one built tile.</summary>
        static void MedianLateral(CityMap map, CityMeshes.Trims trims, int ptx, int ptz, HashSet<int> routeEdges)
        {
            if (!medDone.Add(TileKey(ptx, ptz))) return;
            var min = new Vector2(ptx * CityMeshes.TileSize, ptz * CityMeshes.TileSize);
            var max = min + Vector2.one * CityMeshes.TileSize;
            int solid = 1 << CityWorld.SolidLayer;
            foreach (int ei in routeEdges)
            {
                var e = map.edges[ei];
                if (!e.oneway || !CityMeshes.Barriered(e) || string.IsNullOrEmpty(e.name)) continue;   // freeways: a city street's twins stand no median
                float sMin = trims.atA[ei], sMax = e.length - trims.atB[ei];
                for (float s = sMin + 1f; s <= sMax - 1f; s += MedianStepM)
                {
                    var p = e.PointAt(s);
                    if (p.x < min.x || p.x >= max.x || p.y < min.y || p.y >= max.y) continue;
                    var tan = e.TangentAt(s);
                    // the opposing twin: same name, one-way, the other way, nearest
                    medSegs.Clear();
                    map.EdgeSegsInRect(p - Vector2.one * MedianTwinM, p + Vector2.one * MedianTwinM, medSegs);
                    CityMap.Edge o = null; float ot = 0f, od = float.MaxValue;
                    foreach (int seg in medSegs)
                    {
                        var c = map.edges[seg >> 12];
                        if (c == e || !c.oneway || c.name != e.name) continue;
                        CityElevation.ProjectOn(c, p, out float t);
                        if (Vector2.Dot(c.TangentAt(t), tan) > -0.6f) continue;
                        float d = Vector2.Distance(c.PointAt(t), p);
                        if (d < od) { od = d; o = c; ot = t; }
                    }
                    if (o == null || od > MedianTwinM) continue;
                    float gap = od - e.HalfMax - o.HalfMax;
                    if (gap > MedianGapMaxM) continue;
                    var po = o.PointAt(ot);
                    var right = new Vector2(-tan.y, tan.x);
                    int sideE = Vector2.Dot(po - p, right) >= 0f ? 1 : -1;
                    var tanO = o.TangentAt(ot);
                    int sideO = Vector2.Dot(p - po, new Vector2(-tanO.y, tanO.x)) >= 0f ? 1 : -1;
                    medRouteAt.TryGetValue(ei, out var rAt);
                    if (Mathf.Abs(e.YAt(s) - o.YAt(ot)) > CityMeshes.UnionSplitDyM) { medSplit++; medSeq.Add((rAt.route, rAt.at + s, -1, p, ei, s)); continue; }
                    Vector3 a = MedianLaneIn(map, trims, e, s, sideE, 1.8f), b = MedianLaneIn(map, trims, o, ot, sideO, 1.8f);
                    var dir = b - a; float len = dir.magnitude;
                    if (len < 0.5f) continue;
                    dir /= len;
                    int n = 0; float done = 0f; var from = a;
                    string hits = "";
                    while (n < 8 && RaycastPastLamps(from, dir, out var hit, len - done, solid, QueryTriggerInteraction.Ignore))
                    {
                        n++;
                        if (hits.Length < 160) hits += $" {hit.collider.name}@{done + hit.distance:0.0}";
                        float step = hit.distance + 0.05f; done += step;
                        if (done >= len) break;
                        from += dir * step;
                    }
                    medSamples++;
                    // a median wider than the shared one's band (Independence's
                    // busway): a wall each side is the design, not a box
                    string why = n == 1 ? "" : CityMeshes.GroundTwinWhy(map, trims, e, s) + " / from e" + o.index + ": " + CityMeshes.GroundTwinWhy(map, trims, o, ot);
                    if (n >= 2 && (why.StartsWith("gap ") || why.Contains(": gap "))) { medWide++; medSeq.Add((rAt.route, rAt.at + s, -1, p, ei, s)); continue; }
                    medSeq.Add((rAt.route, rAt.at + s, n, p, ei, s));
                    if (!medByName.TryGetValue(e.name, out var byN)) medByName[e.name] = byN = new int[3];
                    int k = n == 0 ? 0 : n == 1 ? 1 : 2;
                    byN[k]++;
                    if (n == 0) medZero++; else if (n == 1) medOne++; else medMany++;
                    if (n == 1) continue;
                    bool near = false;
                    foreach (var sp in medSpots) if ((sp.p - p).sqrMagnitude < MedianSpotM * MedianSpotM) { near = true; break; }
                    if (near || medSpots.Count >= 60) continue;
                    string at = medRouteAt.TryGetValue(ei, out var ra) ? $"route {ra.route} at {ra.at + s:0} m" : "";
                    medSpots.Add((p, $"{(n == 0 ? "GAP" : "BOX")} {n} walls across, {at} e{ei} '{e.name}'{(e.bridge ? " B" : "")} s={s:0} -> e{o.index} " +
                                     $"at ({p.x:0},{p.y:0}) gap {gap:0.0} m centres {od:0.0} m:{hits} | ground twin: {why}"));
                }
            }
        }

        static void MedianLateralReport()
        {
            var names = new StringBuilder();
            foreach (var kv in medByName) names.Append($" '{kv.Key}' {kv.Value[1]} one / {kv.Value[2]} two+ / {kv.Value[0]} none;");
            Line($"MEDIAN ACROSS DIVIDED ROUTES (owner 2026-10-24, boxes and diamonds): {medSamples} rays every {MedianStepM:0} m from a one-way route carriageway's inner lane " +
                 $"to its opposing twin's (inner edges within {MedianGapMaxM:0} m), Solid walls crossed: one {medOne}, two or more {medMany}, none {medZero}; a wall each side of a median wider than the shared one's band (reported) {medWide}; split levels skipped {medSplit};{names}");
            foreach (var sp in medSpots) Line("    " + sp.note);
            MedianClasses();
            Check(medMany == 0, "no box, diamond or double wall between a divided route's carriageways (MEDIAN ACROSS: two or more walls)", medMany);
            Check(medZero == 0, "a divided route keeps one median between its carriageways (MEDIAN ACROSS: none)", medZero);
        }

        /// <summary>
        /// (round 7, the owner on the live round 6: "rectangle cutouts with holes, spots that go
        /// down then back up, and empty box areas"): the rays in route order, each run of
        /// samples with no wall or with two+ as one PLACE, classed: (a/b) a BREAK - a stretch of
        /// no wall at most <see cref="MedianBreakM"/> long with the median standing within 4 m
        /// both sides of it (a notch, a hole, a nose turned down and back up); an OPEN gap - any
        /// other stretch of none; (c) a BOX - a stretch of two+ walls (an empty box, a double
        /// wall, a diamond). Each place listed with where it is.
        /// </summary>
        const float MedianBreakM = 16f;
        static void MedianClasses()
        {
            var seq = new List<(string route, float at, int n, Vector2 p, int edge, float s)>(medSeq);
            seq.Sort((x, y) => x.route != y.route ? string.CompareOrdinal(x.route ?? "", y.route ?? "") : x.at.CompareTo(y.at));
            int breaks = 0, gaps = 0, boxes = 0;
            var notes = new StringBuilder();
            int i = 0;
            while (i < seq.Count)
            {
                int n = seq[i].n;
                if (n == 1 || n < 0) { i++; continue; }
                bool none = n == 0;
                int j = i;
                while (j + 1 < seq.Count && seq[j + 1].route == seq[i].route && seq[j + 1].at - seq[j].at <= 2f * MedianStepM + 0.5f
                       && (none ? seq[j + 1].n == 0 : seq[j + 1].n >= 2)) j++;
                float len = seq[j].at - seq[i].at;
                bool wallBefore = i > 0 && seq[i - 1].route == seq[i].route && seq[i].at - seq[i - 1].at <= 4f && seq[i - 1].n >= 1;
                bool wallAfter = j + 1 < seq.Count && seq[j + 1].route == seq[i].route && seq[j + 1].at - seq[j].at <= 4f && seq[j + 1].n >= 1;
                string cls;
                if (!none) { boxes++; cls = "BOX (c)"; }
                else if (len <= MedianBreakM && wallBefore && wallAfter) { breaks++; cls = "BREAK (a/b)"; }
                else { gaps++; cls = "OPEN GAP"; }
                if (notes.Length < 6000)
                    notes.Append($"      {cls} route {seq[i].route} {seq[i].at:0}-{seq[j].at:0} m ({len + MedianStepM:0} m) e{seq[i].edge} s {seq[i].s:0} at ({seq[i].p.x:0},{seq[i].p.y:0})\n");
                i = j + 1;
            }
            Line($"MEDIAN CLASSES (round 7, the owner's cutouts / down-and-up / empty boxes) on the routes' rays: BREAKS (a/b: no wall for at most {MedianBreakM:0} m with the median standing both sides) {breaks}, OPEN GAPS {gaps}, BOXES (c: two+ walls) {boxes} places");
            if (notes.Length > 0) Line(notes.ToString().TrimEnd('\n'));
            Check(breaks == 0, "no median cutout, notch or down-and-up break on a divided route (MEDIAN CLASSES a/b)", breaks);
            Check(boxes == 0, "no empty boxed area in a divided route's median (MEDIAN CLASSES c)", boxes);
        }
    }
}
