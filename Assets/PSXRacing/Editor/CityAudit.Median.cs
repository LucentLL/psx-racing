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
        /// <summary>(dividers, 2026-11-02) a cable or open median is read out to this width
        /// (the concrete rules stop at MedianGapMaxM; I-485's grass runs 10-35 m).</summary>
        const float MedianTwinDivM = 75f, MedianGapDivM = 45f;
        static int medSamples, medZero, medOne, medMany, medSplit, medWide;
        /// <summary>(dividers) rays across a cable or open median: cable one / none / two+,
        /// overpass zones met by a barrier / by none, open medians (expected none) / with one,
        /// split levels skipped.</summary>
        static int divOne, divZero, divMany, divZoneOk, divZoneZero, divOpen, divOpenHit, divSplit, divWide, divLap;
        /// <summary>(round 3) the two+ rays across a cable median that are no designed lap, by cause.</summary>
        static readonly SortedDictionary<string, int> divCause = new SortedDictionary<string, int>();
        /// <summary>(round 3) the last profile's Solid hits: collider bounds, for the notes.</summary>
        static string profSolid = "";
        /// <summary>(round 3) the cable medians' "rail at each own edge, no cable between" places
        /// (owner-visible exceptions), one per 60 m.</summary>
        static readonly List<string> divWideSpots = new List<string>();
        static readonly Dictionary<string, int[]> medByName = new Dictionary<string, int[]>();
        static readonly List<(Vector2 p, string note)> medSpots = new List<(Vector2, string)>();
        static readonly HashSet<long> medDone = new HashSet<long>();
        static readonly HashSet<int> medSegs = new HashSet<int>();
        static readonly Dictionary<int, (string route, float at)> medRouteAt = new Dictionary<int, (string, float)>();
        /// <summary>(round 7) every ray in route order: walls crossed (-1 skipped: split level or wider band), for the classes.</summary>
        static readonly List<(string route, float at, int n, Vector2 p, int edge, float s)> medSeq = new List<(string, float, int, Vector2, int, float)>(8192);

        /// <summary>(round 9) two+ walls that are a split's V (or just past its mouth); a wall each side
        /// of a median wider than the band measured from the walls themselves (the walk's reason read
        /// "fan" / "no twin" / "owned" beside Independence's 11-17 m busway median: two continuous
        /// walls, no box); rays across a route carriageway's own junction fans (the main rays never
        /// start inside one): none at a fan nothing leaves into the median from, none where a road
        /// does (an opening), one, two+.</summary>
        static int medV, medWideGeom, fanRays, fanZero, fanZeroCross, fanOne, fanMany;
        static readonly List<string> fanSpots = new List<string>();

        static void MedianLateralReset(CityMap map)
        {
            medSamples = medZero = medOne = medMany = medSplit = medWide = 0;
            divOne = divZero = divMany = divZoneOk = divZoneZero = divOpen = divOpenHit = divSplit = divWide = divLap = 0;
            divCause.Clear(); divWideSpots.Clear();
            medV = medWideGeom = fanRays = fanZero = fanZeroCross = fanOne = fanMany = 0;
            fanSpots.Clear();
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
                    bool divE = CityMeshes.DividerAt(e) != CityMeshes.DividerKind.Concrete;
                    float twinR = divE ? MedianTwinDivM : MedianTwinM;
                    medSegs.Clear();
                    map.EdgeSegsInRect(p - Vector2.one * twinR, p + Vector2.one * twinR, medSegs);
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
                    if (o == null) continue;
                    float gap = od - e.HalfMax - o.HalfMax;
                    // (dividers) what this median should stand: -1 the concrete rules as before
                    int dcls = CityMeshes.DividerRayClass(e, s, o, ot);
                    if (dcls < 0 && (od > MedianTwinM || gap > MedianGapMaxM)) continue;
                    if (dcls >= 0 && (od > MedianTwinDivM || gap > MedianGapDivM)) continue;
                    var po = o.PointAt(ot);
                    var right = new Vector2(-tan.y, tan.x);
                    int sideE = Vector2.Dot(po - p, right) >= 0f ? 1 : -1;
                    var tanO = o.TangentAt(ot);
                    int sideO = Vector2.Dot(p - po, new Vector2(-tanO.y, tanO.x)) >= 0f ? 1 : -1;
                    medRouteAt.TryGetValue(ei, out var rAt);
                    bool splitLvl = Mathf.Abs(e.YAt(s) - o.YAt(ot)) > CityMeshes.UnionSplitDyM;
                    if (splitLvl && dcls >= 0) divSplit++;   // (dividers) read from above all the same: a grass median between two levels is still one median
                    if (splitLvl && dcls < 0) { medSplit++; medSeq.Add((rAt.route, rAt.at + s, -1, p, ei, s)); continue; }
                    Vector3 a = MedianLaneIn(map, trims, e, s, sideE, 1.8f), b = MedianLaneIn(map, trims, o, ot, sideO, 1.8f);
                    var dir = b - a; float len = dir.magnitude;
                    if (len < 0.5f) continue;
                    dir /= len;
                    int n = 0; float done = 0f; var from = a;
                    string hits = "";
                    float firstHit = -1f, lastHit = -1f;
                    while (n < 8 && RaycastPastLamps(from, dir, out var hit, len - done, solid, QueryTriggerInteraction.Ignore))
                    {
                        // (round 3, a cable or open median) a hit within 0.75 m of the last is the
                        // same barrier line (a parapet end with its W-beam bolted along it)
                        if (!(dcls >= 0 && lastHit >= 0f && done + hit.distance - lastHit <= 0.75f)) n++;
                        if (hits.Length < 160) hits += $" {hit.collider.name}@{done + hit.distance:0.0}";
                        if (firstHit < 0f) firstHit = done + hit.distance;
                        lastHit = done + hit.distance;
                        float step = hit.distance + 0.05f; done += step;
                        if (done >= len) break;
                        from += dir * step;
                    }
                    // (dividers) a grass median falls to a ditch: the cable stands on its floor,
                    // under a ray 0.5 m over the lanes. Its barriers are counted from above
                    // instead - a ray down every 0.1 m across, each run of Solid hits one barrier
                    if (dcls >= 0) { int np = ProfileBarriers(a, b, solid, out string ph, out float pf, out float pl); if (np >= n) { n = np; hits = ph; firstHit = pf; lastHit = pl; } }
                    medSamples++;
                    // (round 9) a split's V: its two legs are its designed end, as a deck's V is
                    if (n >= 2 && CityMeshes.InSplitV(new Vector2(a.x, a.z), new Vector2(b.x, b.z))) { medV++; medSeq.Add((rAt.route, rAt.at + s, -1, p, ei, s)); continue; }
                    // (dividers) a cable or open median: an overpass zone's W-beam each side is
                    // the design (and the cable lapping in behind them); an open pair stands none;
                    // a cable median exactly one - the cable, or the concrete side's Jersey at a
                    // stagger - and none or two+ are the defects, never "wide by design"
                    if (dcls == 2 && n >= 1) { divZoneOk++; medSeq.Add((rAt.route, rAt.at + s, 1, p, ei, s)); continue; }
                    if (dcls == 0 && n == 0) { divOpen++; medSeq.Add((rAt.route, rAt.at + s, -1, p, ei, s)); continue; }
                    // (round 3) a DESIGNED LAP, named by the plan and the tiles that built it: the
                    // cable running on behind a median rail's end and its lead-in (CableLapM into
                    // the rail; CityMeshes.DividerLaps), or behind a narrow pinch's Jersey end. Two
                    // barriers there are the install, not a box; every other two+ is a defect and
                    // is reported by its cause below
                    if (dcls == 1 && n >= 2 && !(CityMeshes.CableBuiltAt(e, s) && CityMeshes.CableBuiltAt(o, ot)) && (CityMeshes.DividerLapAt(e, s) || CityMeshes.DividerLapAt(o, ot)))
                    { divLap++; medSeq.Add((rAt.route, rAt.at + s, 1, p, ei, s)); continue; }
                    if (dcls == 2) divZoneZero++;
                    else if (dcls == 0) divOpenHit++;
                    else if (dcls == 1) { if (n == 0) divZero++; else if (n == 1) divOne++; else divMany++; }
                    // a median wider than the shared one's band (Independence's
                    // busway): a wall each side is the design, not a box
                    string why = n == 1 ? "" : CityMeshes.GroundTwinWhy(map, trims, e, s) + " / from e" + o.index + ": " + CityMeshes.GroundTwinWhy(map, trims, o, ot);
                    if (dcls >= 0 && n != 1) why = $"divider {CityMeshes.DividerAt(e)}/{CityMeshes.DividerAt(o)} ray class {dcls}";
                    if (dcls < 0 && n >= 2 && (why.StartsWith("gap ") || why.Contains(": gap "))) { medWide++; medSeq.Add((rAt.route, rAt.at + s, -1, p, ei, s)); continue; }
                    // (round 9) ...measured from the walls: exactly two, each at its own carriageway's
                    // edge, the edges further apart than the band - whatever the walk's reason reads
                    if (n == 2 && (dcls < 0 || dcls == 1) && WideByWalls(e, s, sideE, o, ot, sideO, a, dir, firstHit, lastHit)) { if (dcls == 1) { divMany--; divWide++; if (divWideSpots.Count < 40 && !divWideSpots.Exists(w => w.Contains($" e{ei} s~{(int)(s / 60f) * 60} "))) divWideSpots.Add($"RAILS EACH EDGE e{ei} s~{(int)(s / 60f) * 60} '{e.name}' at ({p.x:0},{p.y:0}) {LatLon(p.x, p.y)} -> e{o.index} gap {gap:0.0} m:{hits}"); } medWide++; medWideGeom++; medSeq.Add((rAt.route, rAt.at + s, -1, p, ei, s)); continue; }
                    medSeq.Add((rAt.route, rAt.at + s, n, p, ei, s));
                    string cause = "";
                    if (dcls == 1 && n >= 2)
                    {
                        bool cE = CityMeshes.CableBuiltAt(e, s), cO = CityMeshes.CableBuiltAt(o, ot);
                        bool solidHit = hits.Contains("Solid@");
                        cause = cE && cO ? "cable on both sides" : solidHit ? "a solid object (pier, post, sign) among the barriers" : (cE || cO) ? "a rail in front of the cable, not lapped" : "two rails, no cable";
                        divCause.TryGetValue(cause, out int cc); divCause[cause] = cc + 1;
                        cause = $" | cause: {cause}; {CityMeshes.DescribeDivider(e, s)}; {CityMeshes.DescribeDivider(o, ot)}{profSolid}";
                    }
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
                                     $"at ({p.x:0},{p.y:0}) gap {gap:0.0} m centres {od:0.0} m:{hits} | ground twin: {why}{cause}"));
                }
                // (round 9) rays across this carriageway's OWN junction fans (the rays above start
                // only between its trims): a median that stops at a fan nothing leaves into it from
                for (int end = 0; end < 2; end++)
                {
                    int node = end == 0 ? e.a : e.b;
                    if (!trims.patch[node]) continue;
                    float f0 = end == 0 ? 1f : sMax - 1f + 0.5f * MedianStepM, f1 = end == 0 ? sMin + 1f - 0.5f * MedianStepM : e.length - 1f;
                    for (float s = f0; s <= f1 + 1e-3f; s += MedianStepM)
                    {
                        var p = e.PointAt(s);
                        if (p.x < min.x || p.x >= max.x || p.y < min.y || p.y >= max.y) continue;
                        int n = CastMedianRay(map, trims, e, s, solid, out var o, out float ot, out string hits);
                        if (n < 0) continue;
                        fanRays++;
                        if (n == 1) { fanOne++; continue; }
                        if (n >= 2) { fanMany++; continue; }
                        if (CityMeshes.NodeLeavesLeft(map, e, node)) { fanZeroCross++; continue; }
                        fanZero++;
                        bool nearF = false;
                        foreach (var fs in fanSpots) if (fs.Contains($" n{node} ")) { nearF = true; break; }
                        if (nearF || fanSpots.Count >= 40) continue;
                        string at = medRouteAt.TryGetValue(ei, out var ra) ? $"route {ra.route} at {ra.at + s:0} m" : "";
                        fanSpots.Add($"FAN GAP {at} e{ei} '{e.name}' s={s:0} fan n{node} -> e{o.index} at ({p.x:0},{p.y:0}):{hits}");
                    }
                }
            }
        }

        /// <summary>(dividers) Barriers across a cable or open median counted from above: rays
        /// down from 1.5 m over the higher lane every 0.05 m from a to b (lamps skipped), each
        /// run of Solid hits one barrier, wherever in the ditch it stands.</summary>
        static int ProfileBarriers(Vector3 a, Vector3 b, int solid, out string hits, out float first, out float last)
        {
            hits = ""; first = last = -1f; profSolid = "";
            float top = Mathf.Max(a.y, b.y) + 1.0f;   // a, b are 0.5 m over their lanes
            var d = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float L = d.magnitude;
            if (L < 1f) return 0;   // (first/last: horizontal metres from a, as the cast reports its hits)
            d /= L;
            int n = 0; bool inRun = false; float lastHitT = -1f;
            for (float t = 0.2f; t <= L - 0.2f; t += 0.05f)
            {
                var p = new Vector3(a.x + d.x * t, top, a.z + d.z * t);
                bool hit = RaycastPastLamps(p, Vector3.down, out var h, 14f, solid, QueryTriggerInteraction.Ignore);
                // (round 3) under either carriageway's own pavement (the first and last 1.6 m:
                // the rays start 1.8 m in) and below its surface: a pier under its own deck, no
                // barrier across the median
                if (hit && (t < 1.6f || t > L - 1.6f) && h.point.y < Mathf.Min(a.y, b.y) - 1.0f) hit = false;
                // (round 3) a run starting within 0.75 m of the last one is the same barrier line
                // (a parapet's end with the W-beam bolted along it, two colliders side by side)
                if (hit && !inRun && lastHitT >= 0f && t - lastHitT <= 0.75f) inRun = true;
                if (hit) lastHitT = t;
                if (hit && !inRun) { n++; if (first < 0f) first = t; last = t; if (hits.Length < 160) hits += $" {h.collider.name}@{t:0.0}"; if (h.collider.name == "Solid" && profSolid.Length < 200) profSolid += $" [Solid {h.collider.GetType().Name} at ({h.collider.bounds.center.x:0.0},{h.collider.bounds.center.y:0.0},{h.collider.bounds.center.z:0.0}) size ({h.collider.bounds.size.x:0.0},{h.collider.bounds.size.y:0.0},{h.collider.bounds.size.z:0.0}) hit y {h.point.y:0.00} lane y {Mathf.Min(a.y, b.y) - 0.5f:0.00}]"; }
                inRun = hit;
            }
            return n;
        }

        /// <summary>(round 9) The census ray across a divided road at arc s of e (the main loop's
        /// twin search, split-level skip and cast): walls crossed, or -1 (no twin in reach, too
        /// wide a gap, a split level).</summary>
        static int CastMedianRay(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, float s, int solid, out CityMap.Edge o, out float ot, out string hits)
        {
            o = null; ot = 0f; hits = "";
            var p = e.PointAt(s); var tan = e.TangentAt(s);
            medSegs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * MedianTwinM, p + Vector2.one * MedianTwinM, medSegs);
            float od = float.MaxValue;
            foreach (int seg in medSegs)
            {
                var c = map.edges[seg >> 12];
                if (c == e || !c.oneway || c.name != e.name) continue;
                CityElevation.ProjectOn(c, p, out float t);
                if (Vector2.Dot(c.TangentAt(t), tan) > -0.6f) continue;
                float d = Vector2.Distance(c.PointAt(t), p);
                if (d < od) { od = d; o = c; ot = t; }
            }
            if (o == null || od > MedianTwinM || od - e.HalfMax - o.HalfMax > MedianGapMaxM) return -1;
            if (Mathf.Abs(e.YAt(s) - o.YAt(ot)) > CityMeshes.UnionSplitDyM) return -1;
            var po = o.PointAt(ot);
            var right = new Vector2(-tan.y, tan.x);
            int sideE = Vector2.Dot(po - p, right) >= 0f ? 1 : -1;
            var tanO = o.TangentAt(ot);
            int sideO = Vector2.Dot(p - po, new Vector2(-tanO.y, tanO.x)) >= 0f ? 1 : -1;
            Vector3 a = MedianLaneIn(map, trims, e, s, sideE, 1.8f), b = MedianLaneIn(map, trims, o, ot, sideO, 1.8f);
            var dir = b - a; float len = dir.magnitude;
            if (len < 0.5f) return -1;
            dir /= len;
            int n = 0; float done = 0f; var from = a;
            while (n < 8 && RaycastPastLamps(from, dir, out var hit, len - done, solid, QueryTriggerInteraction.Ignore))
            {
                n++;
                if (hits.Length < 160) hits += $" {hit.collider.name}@{done + hit.distance:0.0}";
                float step = hit.distance + 0.05f; done += step;
                if (done >= len) break;
                from += dir * step;
            }
            return n;
        }

        /// <summary>(round 9) Two walls that are each carriageway's own, at its own edge (the first
        /// on e's median-side edge, the last on o's: the face toward the ray), the two edges further
        /// apart than the shared median's band: a wide median, walled each side by design.</summary>
        static bool WideByWalls(CityMap.Edge e, float s, int sideE, CityMap.Edge o, float ot, int sideO, Vector3 a, Vector3 dir, float first, float last)
        {
            if (first < 0f || last < 0f || last - first < 1f) return false;
            var hf = a + dir * first; var hl = a + dir * last;
            var pe = e.PointAt(s); var te = e.TangentAt(s); var re = new Vector2(-te.y, te.x);
            var po = o.PointAt(ot); var to = o.TangentAt(ot); var ro = new Vector2(-to.y, to.x);
            LineModel.Extents(e, s, out float eM, out float eP);
            LineModel.Extents(o, ot, out float oM, out float oP);
            float edgeE = sideE > 0 ? eP : eM, edgeO = sideO > 0 ? oP : oM;
            float latF = Vector2.Dot(new Vector2(hf.x, hf.z) - pe, re) * sideE;
            float latL = Vector2.Dot(new Vector2(hl.x, hl.z) - po, ro) * sideO;
            if (latF < edgeE - 0.4f || latF > edgeE + 0.9f || latL < edgeO - 0.4f || latL > edgeO + 0.9f) return false;
            var ee = pe + re * (edgeE * sideE); var eo = po + ro * (edgeO * sideO);
            return Vector2.Distance(ee, eo) > CityMeshes.SharedMedianBandM;
        }

        static void MedianLateralReport()
        {
            var names = new StringBuilder();
            foreach (var kv in medByName) names.Append($" '{kv.Key}' {kv.Value[1]} one / {kv.Value[2]} two+ / {kv.Value[0]} none;");
            Line($"MEDIAN ACROSS DIVIDED ROUTES (owner 2026-10-24, boxes and diamonds): {medSamples} rays every {MedianStepM:0} m from a one-way route carriageway's inner lane " +
                 $"to its opposing twin's (inner edges within {MedianGapMaxM:0} m), Solid walls crossed: one {medOne}, two or more {medMany}, none {medZero}; a wall each side of a median wider than the shared one's band (reported) {medWide}; split levels skipped {medSplit};{names}");
            foreach (var sp in medSpots) Line("    " + sp.note);
            Line($"MEDIAN ACROSS (dividers, CityMeshes.Dividers): cable medians one {divOne}, none {divZero}, two+ {divMany}, a rail at each carriageway's own median edge (no cable between: the rails are the barrier, reported with the wide medians) {divWide}; overpass zones (a W-beam each side by design) met by a barrier {divZoneOk}, by none {divZoneZero}; " +
                 $"open medians (none expected) {divOpen}, with a barrier {divOpenHit}; between two levels (read from above) {divSplit} (counted in the totals above: one/none/two+ include the cable rays)");
            // (round 9)
            Line($"MEDIAN ACROSS round 9: two+ walls that are a split's V (the shared median parting into each carriageway's own Jersey) {medV}; of the wide medians, read from the walls (two, each at its own edge, edges over {CityMeshes.SharedMedianBandM:0.0} m apart) {medWideGeom}; " +
                 $"rays across the routes' own junction fans {fanRays}: one {fanOne}, two+ {fanMany}, none {fanZero} where nothing leaves into the median, none {fanZeroCross} where a road does (an opening)");
            foreach (var fs in fanSpots) Line("    " + fs);
            // (round 3) the cable medians' two+ rays: the designed laps, and the rest by cause
            var causes = new StringBuilder();
            foreach (var kv in divCause) causes.Append($" {kv.Key} {kv.Value};");
            Line($"MEDIAN ACROSS (dividers, round 3): designed laps (the cable behind a median rail's end and lead-in, or a pinch's Jersey end; CityMeshes.DividerLapAt) {divLap} rays, read as one median; two+ that are no designed lap {divMany} rays, by cause:{(causes.Length > 0 ? causes.ToString() : " none")}");
            Line($"MEDIAN ACROSS (dividers, round 3): OWNER-VISIBLE EXCEPTION - a rail at each carriageway's own median edge and no cable between (the rails are the barrier) {divWide} rays, at:");
            foreach (var w in divWideSpots) Line("    " + w);
            Check(divMany == 0, "no undesigned double barrier across a cable median (MEDIAN ACROSS dividers: two+ outside a designed lap)", divMany);
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
            // an edge-set audit (PSX_DRIVE_EDGES) has no route: each edge is its own sequence
            // (one shared empty route interleaved every edge's rays into false breaks)
            var seq = new List<(string route, float at, int n, Vector2 p, int edge, float s)>(medSeq.Count);
            foreach (var x in medSeq) seq.Add((string.IsNullOrEmpty(x.route) ? "e" + x.edge : x.route, x.at, x.n, x.p, x.edge, x.s));
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
        /// <summary>(dividers round 3) PSX_DRIVE_DESCRIBE="edge:s0:s1:step;...": what each
        /// side of each span decided there (CityMeshes.DescribeSide) and the divider plan, for
        /// the notes of a spot under investigation.</summary>
        static void DescribeSpots(CityMap map, CityMeshes.Trims trims)
        {
            var v = System.Environment.GetEnvironmentVariable("PSX_DRIVE_DESCRIBE");
            if (string.IsNullOrWhiteSpace(v)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var item in v.Split(';'))
            {
                var f = item.Split(':');
                if (f.Length < 4 || !int.TryParse(f[0], out int ei) || ei < 0 || ei >= map.edges.Length) continue;
                var e = map.edges[ei];
                float s0 = float.Parse(f[1], ci), s1 = float.Parse(f[2], ci), st = Mathf.Max(0.5f, float.Parse(f[3], ci));
                for (float s = s0; s <= Mathf.Min(s1, e.length) + 1e-3f; s += st)
                {
                    var p = e.PointAt(s);
                    Line($"DESCRIBE e{ei} '{e.name}' s {s:0.0} at ({p.x:0.0},{p.y:0.0}) R(median){CityMeshes.DescribeSideAt(map, trims, e, s, 1)} L{CityMeshes.DescribeSideAt(map, trims, e, s, -1)} || {CityMeshes.DescribeDivider(e, s)}");
                }
            }
        }
    }
}
