using System.Globalization;
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
        }
    }
}
