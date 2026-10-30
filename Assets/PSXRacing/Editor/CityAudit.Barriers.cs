using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// SHORT BARRIER PIECES, CITY-WIDE (hotfix 2026-10-03, the owner: stray
    /// concrete median blocks "between roads where they shouldn't exist" - a
    /// free-standing Jersey-height block about 5 m long on the grass between a
    /// merge ramp and its freeway). Every tile a barriered road (motorway,
    /// trunk, one-way primary: <see cref="CityMeshes.Barriered"/>) crosses is
    /// built with the span record on (<see cref="CityMeshes.AuditView"/>), and
    /// each side's runs of a Jersey-height piece - a median Jersey on the
    /// median side, a cut wall on the outside - are joined over the tiles. A
    /// run shorter than <see cref="ShortBarrierM"/> that ends inside its edge at
    /// BOTH ends (one reaching a node carries on into the next road) is an
    /// isolated piece. So is a union median (A2: a Jersey or mountable curbs)
    /// drawn over a run that short with both ends closed. Rails are not
    /// counted: a rail is warranted by a drop, however short. The count must
    /// read 0; the worst (shortest) are listed with where they stand.
    /// PSX_CITY_KEEP_SHORT=1 keeps the pieces (the before-count).
    /// </summary>
    public static partial class CityAudit
    {
        const float ShortBarrierM = 30f;

        static void ShortBarrierCensus(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var tiles = new List<long>();
            var seen = new HashSet<long>();
            foreach (var e in map.edges)
            {
                if (!CityMeshes.Barriered(e)) continue;
                for (float s = 0f; s <= e.length + 15.99f; s += 16f)
                {
                    var p = e.PointAt(Mathf.Min(s, e.length));
                    long k = TileKey(Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                    if (seen.Add(k)) tiles.Add(k);
                }
            }
            CityMeshes.medianGapLog = new HashSet<string>();
            CityMeshes.AuditView.BeginRecord();
            try
            {
                foreach (long k in tiles)
                {
                    var tm = CityMeshes.Build(map, trims, buildings, (int)(k >> 32), (int)(uint)k);
                    foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.banks, tm.buildings })
                        if (m != null) Object.DestroyImmediate(m);
                }
            }
            finally { CityMeshes.AuditView.EndRecord(); }

            // each side's Jersey-height runs, joined over the tiles; and every
            // other barrier on that side (a rail, a union's median), which a
            // run carrying on into is one barrier changing kind, not a block
            var runs = new Dictionary<(int edge, int side), List<(float s0, float s1, int kind)>>();
            var others = new Dictionary<(int edge, int side), List<(float s0, float s1)>>();
            foreach (var sp in CityMeshes.AuditView.Spans)
                for (int side = -1; side <= 1; side += 2)
                {
                    var sv = sp.Side(side);
                    // a short cut wall kept because it holds land (CityMeshes.CutWhyHolds,
                    // 2026-10-04) is a retaining wall, not a stray piece: kind 3
                    int kind = sv.median ? 1 : sv.cut && !sv.rail ? (sv.cutWhy == CityMeshes.CutWhyHolds ? 3 : 2) : 0;
                    if (kind == 0)
                    {
                        if (!(sv.rail || sv.union || sv.retain)) continue;
                        if (!others.TryGetValue((sp.edge, side), out var ol)) others[(sp.edge, side)] = ol = new List<(float, float)>();
                        ol.Add((sp.s0, sp.s1));
                        continue;
                    }
                    if (!runs.TryGetValue((sp.edge, side), out var list)) runs[(sp.edge, side)] = list = new List<(float, float, int)>();
                    list.Add((sp.s0, sp.s1, kind));
                }
            bool Touches((int, int) key, float s)
            {
                if (!others.TryGetValue(key, out var ol)) return false;
                foreach (var o in ol) if (s >= o.s0 - 0.05f && s <= o.s1 + 0.05f) return true;
                return false;
            }
            var pieces = new List<(float len, string what)>();
            int holding = 0; float holdingM = 0f;
            float metres = 0f; int total = 0;
            foreach (var kv in runs)
            {
                var e = map.edges[kv.Key.edge];
                var list = kv.Value;
                list.Sort((a, b) => a.s0.CompareTo(b.s0));
                float sMin = trims.atA[e.index], sMax = e.length - trims.atB[e.index];
                int i = 0;
                while (i < list.Count)
                {
                    float r0 = list[i].s0, r1 = list[i].s1; int kind = list[i].kind;
                    int j = i + 1;
                    while (j < list.Count && list[j].kind == kind && list[j].s0 <= r1 + 0.05f) { r1 = Mathf.Max(r1, list[j].s1); j++; }
                    i = j;
                    total++;
                    float len = r1 - r0;
                    bool isolated = r0 > sMin + 0.5f && r1 < sMax - 0.5f && !Touches(kv.Key, r0) && !Touches(kv.Key, r1);
                    if (!isolated || len >= ShortBarrierM) continue;
                    if (kind == 3) { holding++; holdingM += len; continue; }
                    metres += len;
                    var p = e.PointAt(0.5f * (r0 + r1));
                    pieces.Add((len, $"{(kind == 1 ? "median Jersey" : "cut wall")} {len:0.0} m on e{e.index} '{e.name}'{(e.link ? " L" : "")} side {(kv.Key.side < 0 ? "L (right of travel)" : "R (left of travel)")} s {r0:0.0}..{r1:0.0}/{e.length:0} at ({p.x:0},{p.y:0}) {LatLon(p.x, p.y)}"));
                }
            }
            // union medians drawn solid over a run that short, both ends closed
            var uv = new List<CityMeshes.AuditView.UnionRunView>();
            int unionShort = 0;
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                if (CityMeshes.AuditView.UnionRunsOf(trims, ei, uv) == 0) continue;
                foreach (var r in uv)
                {
                    if (!r.owner || !r.solid || !r.open0 || !r.open1 || r.s1 - r.s0 >= ShortBarrierM) continue;
                    unionShort++;
                    var e = map.edges[ei];
                    var p = e.PointAt(0.5f * (r.s0 + r.s1));
                    pieces.Add((r.s1 - r.s0, $"union {r.median} median {r.s1 - r.s0:0.0} m on e{ei} '{e.name}' side {(r.side < 0 ? "L" : "R")}{(r.approach ? " (approach)" : "")} s {r.s0:0.0}..{r.s1:0.0} with e{r.nb} at ({p.x:0},{p.y:0}) {LatLon(p.x, p.y)}"));
                }
            }
            pieces.Sort((a, b) => a.len.CompareTo(b.len));
            bool kept = System.Environment.GetEnvironmentVariable("PSX_CITY_KEEP_SHORT") == "1";
            Line($"short barrier pieces (hotfix 2026-10-03; city-wide, {tiles.Count} tiles of barriered roads built in {clock.Elapsed.TotalSeconds:0} s{(kept ? "; PSX_CITY_KEEP_SHORT=1: the pieces KEPT, the before-count" : "")}): " +
                 $"{pieces.Count} isolated Jersey-height pieces under {ShortBarrierM:0} m ({pieces.Count - unionShort} median Jerseys / cut walls, {metres:0} m, of {total} runs; {unionShort} union medians)");
            Line($"    short cut walls KEPT because they hold land (the graded ground over {CityMeshes.CutHoldM:0.0} m above the road behind them; 2026-10-04): {holding}, {holdingM:0} m");
            for (int k = 0; k < Mathf.Min(10, pieces.Count); k++) Line("    SHORT " + pieces[k].what);
            {
                // MEDIANS RUN CONTINUOUS (2026-10-05): gaps inside an edge
                var gl = CityMeshes.medianGapLog; CityMeshes.medianGapLog = null;
                int bridged = 0, bareLeft = 0; var open = new Dictionary<string, int>();
                foreach (var g in gl)
                {
                    if (g.StartsWith("BRIDGED")) bridged++;
                    else if (g.StartsWith("BARE")) bareLeft++;
                    else { string w = g.Substring(5, g.IndexOf(' ', 5) - 5); open[w] = open.TryGetValue(w, out int c) ? c + 1 : 1; }
                }
                var ow = new List<string>(); foreach (var kv in open) ow.Add($"{kv.Key} {kv.Value}");
                Line($"median gaps inside an edge (MEDIANS RUN CONTINUOUS, 2026-10-05): unexplained {bareLeft} (bridged {bridged}); open for a reason: {string.Join(", ", ow)}");
                int shown = 0;
                foreach (var g in gl) if (!g.StartsWith("OPEN") && shown++ < 6) Line("    " + g);
                Check(bareLeft == 0, "no unexplained median gap inside an edge (median gap census)", bareLeft);
            }
            {
                // MEDIAN CARRIED THROUGH A SHORT SINGLE CARRIAGEWAY (leftovers-b):
                // a two-way piece between a divided road's splits
                int carried = 0; float carriedM = 0f; var why = new Dictionary<string, int>(); var shownC = new List<string>();
                foreach (var e in map.edges)
                {
                    string w = CityMeshes.CentreMedianWhy(map, e);
                    if (w == "-") continue;
                    if (w == null)
                    {
                        carried++; carriedM += e.length;
                        if (shownC.Count < 6) { var p = e.PointAt(e.length * 0.5f); shownC.Add($"    CARRY e{e.index} '{e.name}' cls{e.cls} {e.length:0} m at ({p.x:0},{p.y:0})"); }
                    }
                    else why[w] = why.TryGetValue(w, out int c) ? c + 1 : 1;
                }
                var ww = new List<string>(); foreach (var kv in why) ww.Add($"{kv.Key} {kv.Value}");
                Line($"median through a short single carriageway (leftovers-b, <= {CityMeshes.MedianCarryMaxM:0} m, both ends a plain split): " +
                     $"gaps before {carried} ({carriedM:0} m), after {(CityMeshes.MedianCarryOn ? 0 : carried)} (carried {(CityMeshes.MedianCarryOn ? carried : 0)}); " +
                     $"pieces meeting a divided road left open: {string.Join(", ", ww)}");
                foreach (var l in shownC) Line(l);
            }
            Check(pieces.Count == 0, $"no isolated barrier or median piece shorter than {ShortBarrierM:0} m, city-wide (short barrier census)", pieces.Count);
            ShortRailCensus(map, trims);
        }

        /// <summary>
        /// SHORT RAILS AND PARAPETS (REAL HEIGHTS round 4; the owner: "There
        /// are chunks of concrete everywhere. Randomly in highway lanes,
        /// randomly in bridges", N Davidson St's short parapet blocks standing
        /// in the road). The census above counts only the Jersey runs of
        /// barriered roads; a rail (a deck's parapet, an approach rail, a rail
        /// over a drop) shorter than <see cref="ShortBarrierM"/> that starts and
        /// ends inside its edge, touching no other barrier on that side, is a
        /// block standing alone - on any road of the tiles built above (every
        /// tile a barriered road crosses). Counted on structure and off it.
        /// </summary>
        static void ShortRailCensus(CityMap map, CityMeshes.Trims trims)
        {
            var rails = new Dictionary<(int edge, int side), List<(float s0, float s1)>>();
            var other = new Dictionary<(int edge, int side), List<(float s0, float s1)>>();
            foreach (var sp in CityMeshes.AuditView.Spans)
                for (int side = -1; side <= 1; side += 2)
                {
                    var sv = sp.Side(side);
                    var dict = sv.rail ? rails : (sv.median || sv.cut || sv.union || sv.retain) ? other : null;
                    if (dict == null) continue;
                    if (!dict.TryGetValue((sp.edge, side), out var l)) dict[(sp.edge, side)] = l = new List<(float, float)>();
                    l.Add((sp.s0, sp.s1));
                }
            int runs = 0, shortOn = 0, shortOff = 0; float mOn = 0f, mOff = 0f;
            var list = new List<(float len, string what)>();
            foreach (var kv in rails)
            {
                var e = map.edges[kv.Key.edge];
                var l = kv.Value; l.Sort((a, b) => a.s0.CompareTo(b.s0));
                float sMin = trims.atA[e.index], sMax = e.length - trims.atB[e.index];
                other.TryGetValue(kv.Key, out var ol);
                bool Touch(float s) { if (ol != null) foreach (var o in ol) if (s >= o.s0 - 0.3f && s <= o.s1 + 0.3f) return true; return false; }
                int i = 0;
                while (i < l.Count)
                {
                    float r0 = l[i].s0, r1 = l[i].s1; int j = i + 1;
                    while (j < l.Count && l[j].s0 <= r1 + 0.3f) { r1 = Mathf.Max(r1, l[j].s1); j++; }
                    i = j; runs++;
                    float len = r1 - r0;
                    if (len >= ShortBarrierM || r0 <= sMin + 0.5f || r1 >= sMax - 0.5f || Touch(r0) || Touch(r1)) continue;
                    bool on = e.ElevatedAt(0.5f * (r0 + r1));
                    if (on) { shortOn++; mOn += len; } else { shortOff++; mOff += len; }
                    var p = e.PointAt(0.5f * (r0 + r1));
                    list.Add((len, $"rail {len:0.0} m {(on ? "ON STRUCTURE" : "on the ground")} e{e.index} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} cls{e.cls} side {(kv.Key.side < 0 ? "L" : "R")} s {r0:0.0}..{r1:0.0}/{e.length:0} (trims {sMin:0.0}/{e.length - sMax:0.0}) at ({p.x:0},{p.y:0}) {LatLon(p.x, p.y)}"));
                }
            }
            list.Sort((a, b) => a.len.CompareTo(b.len));
            Line($"SHORT RAILS / PARAPETS (round 4; the tiles above, every road): {runs} rail runs, {shortOn + shortOff} isolated under {ShortBarrierM:0} m inside their edge ({shortOn} on structure {mOn:0} m, {shortOff} on the ground {mOff:0} m)");
            // (round 11) every one up to 60 - the census FAILs on the count, and each one is named
            for (int k = 0; k < Mathf.Min(60, list.Count); k++) Line("    SHORT " + list[k].what);
            Check(shortOn + shortOff == 0, $"no isolated rail or parapet piece shorter than {ShortBarrierM:0} m inside its edge (short rail census)", shortOn + shortOff);
        }
    }
}
