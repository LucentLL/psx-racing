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

        static void MedianLateralReset(CityMap map)
        {
            medSamples = medZero = medOne = medMany = medSplit = medWide = 0;
            medByName.Clear(); medSpots.Clear(); medDone.Clear(); medRouteAt.Clear();
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
                    if (Mathf.Abs(e.YAt(s) - o.YAt(ot)) > CityMeshes.UnionSplitDyM) { medSplit++; continue; }
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
                    if (n >= 2 && (why.StartsWith("gap ") || why.Contains(": gap "))) { medWide++; continue; }
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
            Check(medMany == 0, "no box, diamond or double wall between a divided route's carriageways (MEDIAN ACROSS: two or more walls)", medMany);
            Check(medZero == 0, "a divided route keeps one median between its carriageways (MEDIAN ACROSS: none)", medZero);
        }
    }
}
