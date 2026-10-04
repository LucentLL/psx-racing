using System.Globalization;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// MINOR (roads pass L8): the through lines across minor mouths (plan
    /// A13), the cul-de-sac bulbs and clean dead ends (A17) and the parking
    /// lots (B9/B10 lean), counted from the run-time decisions themselves.
    ///   A13  junctions whose through road's lines run on across the mouth, the
    ///        lines and metres, the near-side edge lines broken, and why every
    ///        other lone fan with a through pair does not (signal, all-way stop,
    ///        a stop on the through road, rank, angle, no lines both sides...)
    ///   A17  turning circles tagged / drawn as bulbs / skipped (short, on
    ///        structure, a link); plain dead ends given a verge across the end
    ///   LOTS lots, their area, stall lines, entrances (aprons); rings that
    ///        will not triangulate (gate: 0)
    ///   NECK (leftover item 4) every bulb's two necks: the tightest turn from
    ///        the street edge round to the circle (gate: none under the class
    ///        curb radius)
    ///   LOT AUDIT (leftover item 4) the lots in scope sampled every 0.5 m:
    ///        pavement on a fan, a ribbon, a building or another lot (gate: 0
    ///        drawn), islands, holes, aisle-only lots
    /// Headless on its own (no tiles but nine round lot 139 and the Victorian
    /// Place bulb): -executeMethod PSXRacing.EditorTools.CityAudit.LotsOnly,
    /// writes lots_audit.txt beside the project.
    /// </summary>
    public static partial class CityAudit
    {
        static void MinorReport(CityMap map, CityMeshes.Trims trims, AuditScope sc)
        {
            var I = CultureInfo.InvariantCulture;
            // ---- A13
            for (int pass = 0; pass < 2; pass++)
            {
                bool city = pass == 1;
                if (!city && sc.Full) continue;
                var tiers = new int[4];
                Vector4? box = city ? (Vector4?)null : new Vector4(sc.Box.xMin, sc.Box.yMin, sc.Box.xMax, sc.Box.yMax);
                CityMeshes.ThroughCensus(map, trims, box, tiers);
                Line(string.Format(I, "MINOR A13 ({0}; {1}): {2} junctions carry their through road's lines across the mouth (T1 {3}, T2 {4}, T3 {5}), {6} lines, {7:0} m ({8} broken lines cut into dashes), " +
                    "{9} near-side edge lines broken at the mouth; not carried: signal {10}, all-way stop {11}, stop on the through road {12}, a side road of higher rank {13}, " +
                    "no straight-through pair or a side road under 60 deg {14}, no line both sides {15}, cluster {16}, on structure {17}, roundabout {18}",
                    city ? "CITY-WIDE" : "in scope", CityMeshes.ThroughPaintOn ? "ON" : "OFF (PSX_CITY_THROUGHPAINT=0)",
                    CityMeshes.ThroughStats.junctions, tiers[1], tiers[2], tiers[3], CityMeshes.ThroughStats.lines, CityMeshes.ThroughStats.metres,
                    CityMeshes.ThroughStats.synthLines, CityMeshes.ThroughStats.nearEdgesDropped,
                    CityMeshes.ThroughStats.skipSignal, CityMeshes.ThroughStats.skipAllWay, CityMeshes.ThroughStats.skipStop, CityMeshes.ThroughStats.skipRank,
                    CityMeshes.ThroughStats.skipAngle, CityMeshes.ThroughStats.skipNoLines, CityMeshes.ThroughStats.skipCluster, CityMeshes.ThroughStats.skipStructure,
                    CityMeshes.ThroughStats.skipRoundabout));
            }
            Line("  E1 FLOAT: the through lines are cut into the fan (each strip on its fan triangle's plane, the fan drawn round it): lift 0 m; draws: the through road's own slot");
            // a few in scope, for the shots
            {
                int shown = 0;
                for (int n = 0; n < map.nodes.Length && shown < 8; n++)
                {
                    if (!trims.patch[n] || !sc.Contains(map.nodes[n])) continue;
                    int before = CityMeshes.ThroughStats.junctions;
                    CityMeshes.ThroughCensus(map, trims, new Vector4(map.nodes[n].x - 0.01f, map.nodes[n].y - 0.01f, map.nodes[n].x + 0.01f, map.nodes[n].y + 0.01f), null);
                    if (CityMeshes.ThroughStats.junctions == 0) continue;
                    var sb = new System.Text.StringBuilder();
                    foreach (int ei in map.nodeEdges[n]) sb.Append($" e{ei} '{map.edges[ei].name}' cls{map.edges[ei].cls};");
                    Line(string.Format(I, "    through lines at n{0} ({1:0.0}, {2:0.0}) {3}: {4} lines;{5}", n, map.nodes[n].x, map.nodes[n].y,
                        LatLon(map.nodes[n].x, map.nodes[n].y), CityMeshes.ThroughStats.lines, sb));
                    shown++;
                }
            }
            // ---- A17
            int bsT = CityMeshes.BulbStats.tagged, bsD = CityMeshes.BulbStats.drawn, bsS = CityMeshes.BulbStats.skipShort, bsR = CityMeshes.BulbStats.skipStructure, bsL = CityMeshes.BulbStats.skipLink;
            int deadEnds = 0, deadInScope = 0;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (map.nodeEdges[n].Count != 1 || trims.patch[n]) continue;
                deadEnds++;
                if (sc.Contains(map.nodes[n])) deadInScope++;
            }
            Line(string.Format(I, "MINOR A17 ({0}): turning circles tagged {1}, drawn as bulbs {2} (skipped: too short for the bulb {3}, on structure {4}, a link {5}); " +
                "plain dead ends {6} city-wide, {7} in scope - each laid a verge across its end where on the ground (tiles built this run: {8})",
                CityMeshes.BulbsOn ? "ON" : "OFF (PSX_CITY_BULBS=0)", bsT, bsD, bsS, bsR, bsL, deadEnds, deadInScope, CityMeshes.DeadEndVerges));
            Check(bsD + bsS + bsR + bsL >= bsT, "MINOR A17: every tagged turning circle drawn as a bulb or listed", $"{bsD} of {bsT}");
            NeckReport(map, trims);
            if (trims.bulb != null)
            {
                int shown = 0;
                for (int n = 0; n < map.nodes.Length && shown < 6; n++)
                {
                    if (!trims.bulb[n]) continue;
                    var e = map.edges[map.nodeEdges[n][0]];
                    Line(string.Format(I, "    bulb at n{0} ({1:0.0}, {2:0.0}) {3}: e{4} '{5}' R {6:0.0} m{7}", n, map.nodes[n].x, map.nodes[n].y, LatLon(map.nodes[n].x, map.nodes[n].y),
                        e.index, e.name, trims.bulbRad[n], sc.Contains(map.nodes[n]) ? " (in scope)" : ""));
                    shown++;
                }
            }
            // ---- LOTS
            CityMeshes.LotCensus(map, trims, out int lots, out int noTris, out int stallLines, out float ringM2);
            int aprons = map.lotEntrances != null ? map.lotEntrances.Length : 0;
            Line(string.Format(I, "MINOR LOTS ({0}): {1} surface lots ({2:0.00} km2 after the road bands), {3} stall lines, {4} entrances (concrete aprons); " +
                "{5} rings that will not triangulate; laid in the tiles built this run: {6} pieces ({7:0} m2), {8} stall-line pieces ({9:0} m2), {10} lots under a restaurant's own lot",
                CityMeshes.LotsOn ? "ON" : "OFF (PSX_CITY_LOTS=0)", lots, ringM2 / 1e6f, stallLines, aprons, noTris,
                CityMeshes.LotStats.pieces, CityMeshes.LotStats.lotM2, CityMeshes.LotStats.stallPieces, CityMeshes.LotStats.stallM2, CityMeshes.LotStats.skippedProp));
            Check(noTris == 0, "MINOR LOTS: every lot ring triangulates", $"{noTris} of {lots}");
            if (map.lots != null)
            {
                // the biggest few in scope
                var idx = new System.Collections.Generic.List<int>();
                for (int i = 0; i < map.lots.Length; i++)
                {
                    var b = map.lots[i].box;
                    if (sc.Contains(new Vector2(0.5f * (b.x + b.z), 0.5f * (b.y + b.w)))) idx.Add(i);
                }
                idx.Sort((p, q) => ((map.lots[q].box.z - map.lots[q].box.x) * (map.lots[q].box.w - map.lots[q].box.y)).CompareTo((map.lots[p].box.z - map.lots[p].box.x) * (map.lots[p].box.w - map.lots[p].box.y)));
                Line($"  {idx.Count} lots in scope; the biggest:");
                for (int k = 0; k < Mathf.Min(5, idx.Count); k++)
                {
                    var L = map.lots[idx[k]]; var b = L.box;
                    int stalls = 0; foreach (var c in L.runN) stalls += c;
                    Line(string.Format(I, "    lot {0} centre ({1:0.0}, {2:0.0}) {3}, box {4:0} x {5:0} m, {6} ring points, {7} stalls in {8} rows",
                        idx[k], 0.5f * (b.x + b.z), 0.5f * (b.y + b.w), LatLon(0.5f * (b.x + b.z), 0.5f * (b.y + b.w)), b.z - b.x, b.w - b.y, L.ring.Length, stalls, L.runN.Length));
                }
            }
            Line("  E1 FLOAT: lots and stall lines are cut into the lattice (each piece on its lattice triangle's plane): lift 0 m; draws: the junction slab's slot (+ a two-lane street's for the stall paint)");
            LotAuditReport(map, trims, ScopeFor("LOT"), null);
        }

        /// <summary>NECK (leftover item 4): the bulbs' necks as drawn.</summary>
        static void NeckReport(CityMap map, CityMeshes.Trims trims)
        {
            var I = CultureInfo.InvariantCulture;
            var rows = new System.Collections.Generic.List<string>();
            CityMeshes.BulbNeckCensus(map, trims, rows, out int bulbs, out int sharp, out float minR, out float kinkMax);
            Line(string.Format(I, "MINOR NECK ({0}): {1} bulbs, {2} necks; curb returns at the class radius {3}, reduced to fit the street {4}, none {5}; " +
                "necks sharper than their class curb radius {6}; tightest turn {7:0.00} m; worst kink at a mouth corner {8:0} deg; bulbs made smaller to keep the class radius on a short street {9}",
                CityMeshes.BulbNeckOn ? "ON" : "OFF (PSX_CITY_BULBNECK=0)", bulbs, 2 * bulbs, CityMeshes.BulbStats.neckFull, CityMeshes.BulbStats.neckReduced,
                CityMeshes.BulbStats.neckNone, sharp, minR, kinkMax, CityMeshes.BulbStats.neckShrunk));
            int shownN = 0;
            foreach (var r in rows) if (r.EndsWith(" SHARP") && shownN++ < 12) Line("    " + r);
            if (shownN == 0) for (int i = 0; i < Mathf.Min(6, rows.Count); i++) Line("    " + rows[i]);
            Check(sharp == 0, "MINOR NECK: no bulb neck sharper than its class curb radius (leftover item 4)", $"{sharp} of {2 * bulbs} necks");
        }

        /// <summary>LOT AUDIT (leftover item 4).</summary>
        static void LotAuditReport(CityMap map, CityMeshes.Trims trims, AuditScope sc, System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<CityBuildings.B>> proc)
        {
            var I = CultureInfo.InvariantCulture;
            if (proc == null) proc = CityBuildings.Precompute(map);
            var box = sc.Full ? Rect.MinMaxRect(-1e6f, -1e6f, 1e6f, 1e6f) : sc.Box;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var A = CityMeshes.LotOverlapAudit(map, trims, box, proc);
            double over = A.OverlapM2(CityMeshes.LotRoadCutOn);
            Line(string.Format(I, "LOT AUDIT ({0}; road cut {1}): {2} lots ({3} aisle-only), {4} stall rows, {5} stalls, {6} islands ({7:0} m2 of island top), {8} holes (buildings, OSM inner rings); " +
                "pavement sampled every 0.5 m {9:0} m2; under a junction fan {10:0.0} m2{11}; cut out on a ribbon's band {19:0.0} m2; DRAWN over a road's ribbon {12:0.0} m2, a footprint {13:0.0} m2, a placed building {14:0.0} m2, another lot {15:0.0} m2; " +
                "{16} lots overlap something ({17:0.0} m2) - {18} ms",
                sc.Full ? "CITY-WIDE" : sc.Describe(), CityMeshes.LotRoadCutOn ? "ON" : "OFF", A.lots, A.aisleLots, A.runs, A.stalls, A.islands, A.islandM2, A.holes,
                A.lotM2, A.fanM2, CityMeshes.LotRoadCutOn ? " (cut out: the fan's)" : " (drawn under it)", A.ribbonM2, A.bldM2, A.procM2, A.lotLotM2,
                A.lotsWithOverlap, over, sw.ElapsedMilliseconds, A.bandCutM2));
            foreach (var w in A.worst) Line("    " + w);
            Check(over < 0.5, "LOT AUDIT: no lot pavement drawn over a road, a fan, a building or another lot (leftover item 4)",
                  string.Format(I, "{0:0.0} m2 in {1} lots", over, A.lotsWithOverlap));
        }

        /// <summary>
        /// THE LOTS REPORT ALONE (leftover item 4): the map, the trims, the
        /// bulbs' necks, the LOT AUDIT on its scope (PSX_LOT_BOX, else the
        /// default box), and nine tiles built round lot 139 and the Victorian
        /// Place bulb, timed, with what they laid (lots, stall lines, islands,
        /// the fans' cut) and their ground + roads submeshes. lots_audit.txt.
        /// </summary>
        public static void LotsOnly()
        {
            outLog = new System.Text.StringBuilder();
            var map = CityMap.Get();
            if (map == null) Line("charlotte_city.bytes missing from Resources");
            else
            {
                var I = CultureInfo.InvariantCulture;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.NodeTrims(map);
                var proc = CityBuildings.Precompute(map);
                Line($"trims + buildings in {sw.ElapsedMilliseconds} ms; lots {(map.lots != null ? map.lots.Length : 0)}");
                NeckReport(map, trims);
                if (System.Environment.GetEnvironmentVariable("PSX_LOTS_NECKONLY") == "1") goto write;
                LotAuditReport(map, trims, ScopeFor("LOT"), proc);
                CityMeshes.LotCensus(map, trims, out int lots, out int noTris, out int stallLines, out float ringM2);
                Line(string.Format(I, "LOTS census: {0} lots, {1:0.00} km2, {2} stall lines, {3} rings that will not triangulate", lots, ringM2 / 1e6f, stallLines, noTris));
                // the same tiles twice: the lots without the road cut (L8's laying), then as drawn
                bool cutWas = CityMeshes.LotRoadCutOn;
                for (int pass = 0; pass < 2; pass++)
                {
                CityMeshes.LotRoadCutOn = pass == 1 && cutWas;
                CityMeshes.LotStats.Reset();
                var ms = new System.Collections.Generic.List<double>();
                int draws = 0, tiles = 0;
                foreach (var at in new[] { new Vector2(-1562.8f, 5101.9f), new Vector2(-3517.0f, 2391.9f) })
                {
                    int tx0 = Mathf.FloorToInt(at.x / CityMeshes.TileSize), tz0 = Mathf.FloorToInt(at.y / CityMeshes.TileSize);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var t0 = System.Diagnostics.Stopwatch.StartNew();
                            var tm = CityMeshes.Build(map, trims, proc, tx0 + dx, tz0 + dz);
                            ms.Add(t0.Elapsed.TotalMilliseconds);
                            tiles++;
                            draws += (tm.groundSlots != null ? tm.groundSlots.Length : 0) + (tm.roadSlots != null ? tm.roadSlots.Length : 0);
                            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings })
                                if (m != null) Object.DestroyImmediate(m);
                        }
                }
                ms.Sort();
                Line(string.Format(I, "LOTS tiles (road cut {11}): {0} built (lot 139 + Victorian Place rings), build p50 {1:0.0} / max {2:0.0} ms (the lots' own {12:0.0} ms in all), ground + roads submeshes {3}; laid {4} lot pieces ({5:0} m2), {6} stall-line pieces ({7:0} m2), " +
                    "{8} islands ({9:0} m2 of top), {10} road-cut classifications",
                    tiles, ms[ms.Count / 2], ms[ms.Count - 1], draws, CityMeshes.LotStats.pieces, CityMeshes.LotStats.lotM2, CityMeshes.LotStats.stallPieces, CityMeshes.LotStats.stallM2,
                    CityMeshes.LotStats.islands, CityMeshes.LotStats.islandM2, CityMeshes.LotStats.fanCutPieces, CityMeshes.LotRoadCutOn ? "ON" : "OFF",
                    CityMeshes.LotStats.ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
                }
                CityMeshes.LotRoadCutOn = cutWas;
            }
            write:
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "lots_audit.txt"), outLog.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
