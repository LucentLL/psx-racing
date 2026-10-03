using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    public static partial class CityAudit
    {
        // ------------------------------------------------------------------
        //  HYDRO AUDIT (plan WP-25): the culverts, the water the player can
        //  see, and the ponds.
        //    * CULVERTS: every ravine that crosses a road, what took each
        //      crossing (a deck, a culvert, a road too low for a pipe) and
        //      where each end's headwall stands. Every culvert KEEPS ITS
        //      EMBANKMENT (the plan's new rule): the road is on the ground
        //      over the pipe, the ground under the road's line is no lower
        //      than the road less its sink (never a hole in a road), and every
        //      headwall stands past its road's pavement and clear zone. Built
        //      on a sample of tiles, each end once, in the tile that owns it.
        //    * WATER SHOWN: along every creek, every 8 m, is the water's
        //      surface over the drawn ground (the 8 m lattice can bury a
        //      narrow sheet)? Reported, not a check.
        //    * PONDS (0.2-2 ha, WP-25): none within 4 m of a road's pavement,
        //      and no pond's water stands over a grounded road near it.
        // ------------------------------------------------------------------

        /// <summary>The hydro audit alone: city_hydro_audit.txt at the
        /// project root. Headless:
        /// -executeMethod PSXRacing.EditorTools.CityAudit.RunHydro</summary>
        [UnityEditor.MenuItem("PSX Racing/Audit City Water")]
        public static void RunHydro()
        {
            outLog = new System.Text.StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); return; }
            HydroAudit(map, CityMeshes.NodeTrims(map), CityBuildings.Precompute(map));
            outLog.AppendLine(failures == 0 ? "HYDRO AUDIT OK" : $"HYDRO AUDIT: {failures} FAILURES");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "city_hydro_audit.txt"), outLog.ToString());
        }

        /// <summary>The ground under a road over a culvert stays within this of
        /// the road: over the smallest pipe's crown (the minimum fill less the
        /// 0.9 m pipe, less a hand). Under a road the lattice is sunk by the
        /// corridor sink and the sag allowance, and a lower road's cap can
        /// take it further; a ravine's channel cut through the fill would be
        /// the whole fill deep.</summary>
        const float HeldUnderRoadM = CityCulverts.MinFillM - 0.5f;

        /// <summary>How many tiles the audit builds to see the ends drawn.</summary>
        const int HydroTileSample = 24;

        /// <summary>The creek crossings WP-25 was asked to show (road, creek,
        /// a hint near the crossing): CityHydroShots' three.</summary>
        static readonly (string road, string water, double lat, double lon)[] NamedBridges =
        {
            ("Trade", "Irwin", 35.2345, -80.8560),
            ("State Street", "Stewart", 35.2395, -80.8665),
            ("Archdale", "Little Sugar", 35.1500, -80.8500),
        };

        static void HydroAudit(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // ---- culverts, everywhere
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var w in map.waters) if (w.ravine) { lo = Vector2.Min(lo, w.bbMin); hi = Vector2.Max(hi, w.bbMax); }
            // (plan B2, owner Q8) and the creeks OSM pipes under a road
            if (map.creekCulverts != null)
                foreach (var x in map.creekCulverts)
                    if (x.edge >= 0 && x.edge < map.edges.Length) { var q = map.edges[x.edge].PointAt(x.s); lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
            var all = new List<CityCulverts.Culvert>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (lo.x < hi.x) CityCulverts.Near(map, trims, lo, hi, all);
            double solveMs = clock.Elapsed.TotalMilliseconds;
            var skips = new SortedDictionary<string, int>();
            var misses = new SortedDictionary<string, int>();
            int culverts = 0, ends = 0, both = 0, holes = 0, inClear = 0, headwalls = 0;
            float maxUnder = 0f;
            var pipes = new SortedDictionary<float, int>();
            var holeNotes = new List<string>();
            var clearNotes = new List<string>();
            float minPast = float.MaxValue;
            foreach (var c in all)
            {
                if (c.skip != null) { skips.TryGetValue(c.skip, out int n); skips[c.skip] = n + 1; continue; }
                culverts++;
                var e = map.edges[c.edge];
                // the embankment is kept: the road on the ground over the pipe,
                // and the ground under the road's line no lower than the road
                // less its sink (a road's own verge and floor hold it up)
                float g = CityElevation.GroundY(map, c.at.x, c.at.y);
                maxUnder = Mathf.Max(maxUnder, c.roadY - g);
                if (e.ElevatedAt(c.es) || g < c.roadY - HeldUnderRoadM)
                {
                    holes++;
                    if (holeNotes.Count < 12)
                    {
                        CityElevation.Ground(map, c.at.x, c.at.y, out var gt);
                        holeNotes.Add($"HOLE  e{c.edge} '{e.name}'{(c.creek ? " (creek)" : "")} at ({c.at.x:0},{c.at.y:0}): road {c.roadY:0.00}, ground under it {g:0.00}{(e.ElevatedAt(c.es) ? " (elevated)" : "")}" +
                                      $" [floor {gt.floor:0.00} e{gt.floorEdge}, protect {gt.protect:0.00} e{gt.protectEdge}, deck cap {gt.deckCap:0.00} / protect {gt.deckProtect:0.00} e{gt.deckEdge}, carve {gt.carve:0.00}]");
                    }
                }
                int got = 0;
                foreach (var (has, end, miss) in new[] { (c.hasLo, c.lo, c.missLo), (c.hasHi, c.hi, c.missHi) })
                {
                    if (!has) { misses.TryGetValue(miss ?? "?", out int m); misses[miss ?? "?"] = m + 1; continue; }
                    got++; ends++;
                    if (end.headwall) headwalls++;
                    pipes.TryGetValue(end.pipeD, out int pn); pipes[end.pipeD] = pn + 1;
                    minPast = Mathf.Min(minPast, end.pastPave);
                    // past its nearest road's pavement AND clear zone
                    if (end.edge >= 0)
                    {
                        float need = RoadsideOccupancy.ClearZoneOf(map.edges[end.edge]);
                        if (end.pastPave < need)
                        {
                            inClear++;
                            if (clearNotes.Count < 12) clearNotes.Add($"CLEAR headwall {end.pastPave:0.0} m past e{end.edge}'s pavement (clear zone {need:0.0}) at ({end.at.x:0},{end.at.y:0})");
                        }
                    }
                }
                if (got == 2) both++;
            }
            var sk = new System.Text.StringBuilder();
            foreach (var kv in skips) sk.Append($"{kv.Key} {kv.Value}, ");
            var ms = new System.Text.StringBuilder();
            foreach (var kv in misses) ms.Append($"{kv.Key} {kv.Value}, ");
            var ps = new System.Text.StringBuilder();
            foreach (var kv in pipes) ps.Append($"{kv.Key:0.0} m x{kv.Value}, ");
            Line($"hydro audit (WP-25): {all.Count} ravine crossings of a road: {culverts} culverts ({sk.ToString().TrimEnd(',', ' ')} take none), solved in {solveMs:0} ms");
            {
                // (plan B2, owner Q8) of them, the creeks OSM pipes under a road
                int cx = 0, cc = 0, cEnds = 0, cWalls = 0, cBoth = 0;
                var cSkip = new SortedDictionary<string, int>(); var cMiss = new SortedDictionary<string, int>();
                foreach (var c in all)
                {
                    if (!c.creek) continue;
                    cx++;
                    if (c.skip != null) { cSkip.TryGetValue(c.skip, out int n); cSkip[c.skip] = n + 1; continue; }
                    cc++;
                    if (c.hasLo) { cEnds++; if (c.lo.headwall) cWalls++; } else { cMiss.TryGetValue(c.missLo ?? "?", out int n); cMiss[c.missLo ?? "?"] = n + 1; }
                    if (c.hasHi) { cEnds++; if (c.hi.headwall) cWalls++; } else { cMiss.TryGetValue(c.missHi ?? "?", out int n); cMiss[c.missHi ?? "?"] = n + 1; }
                    if (c.hasLo && c.hasHi) cBoth++;
                }
                var cs = new StringBuilder(); foreach (var kv in cSkip) cs.Append($"{kv.Key} {kv.Value}, ");
                var cm = new StringBuilder(); foreach (var kv in cMiss) cm.Append($"{kv.Key} {kv.Value}, ");
                Line($"    of them CREEKS OSM pipes under the road (plan B2, owner Q8; {map.creekCulverts?.Length ?? 0} in section CULV, no span): {cx} found, {cc} culverts ({cs.ToString().TrimEnd(',', ' ')} take none); " +
                     $"{cEnds} ends ({cWalls} headwalls), {cBoth} with both; ends not stood: {cm.ToString().TrimEnd(',', ' ')}; roads held over the pipe's cover {CityElevation.CulvertCrossingsHeld} (up to {CityElevation.CulvertHoldMaxM:0.00} m)");
            }
            Line($"    culvert ends: {ends} ({headwalls} headwalls set into the fill, {ends - headwalls} pipes projecting from the toe; {both} culverts with both ends), pipes {ps.ToString().TrimEnd(',', ' ')}; ends not stood: {ms.ToString().TrimEnd(',', ' ')}; nearest end {(minPast < float.MaxValue ? minPast.ToString("0.0", inv) : "-")} m past a pavement");
            foreach (var l in holeNotes) Line("    " + l);
            foreach (var l in clearNotes) Line("    " + l);
            Check(holes == 0, $"every culvert keeps its embankment: the road on the ground over the pipe, the ground under its line held within {HeldUnderRoadM} m of it, over the smallest pipe's crown: the ravine's carve never cuts the road (hydro audit)", $"{holes} of {culverts}; deepest {maxUnder:0.00} m");
            Check(inClear == 0, "every culvert end stands past its road's pavement and clear zone (hydro audit)", $"{inClear} of {ends}");

            // ---- the ends as the tiles draw them: a sample of tiles, each end
            // in the tile that owns it, once
            var endTiles = new SortedDictionary<long, int>();
            foreach (var c in all)
            {
                if (c.skip != null) continue;
                foreach (var (has, end) in new[] { (c.hasLo, c.lo), (c.hasHi, c.hi) })
                    if (has)
                    {
                        long k = ((long)Mathf.FloorToInt(end.at.x / CityMeshes.TileSize) << 32) ^ (uint)Mathf.FloorToInt(end.at.y / CityMeshes.TileSize);
                        endTiles.TryGetValue(k, out int n); endTiles[k] = n + 1;
                    }
            }
            int tilesBuilt = 0, drawn = 0, expected = 0, wrongTile = 0, facing = 0, metFill = 0, walls = 0, pipesDrawn = 0, ditched = 0;
            var backfills = new List<float>();
            var stride = Mathf.Max(1, endTiles.Count / HydroTileSample);
            int idx = 0;
            foreach (var kv in endTiles)
            {
                if (idx++ % stride != 0) continue;
                int tx = (int)(kv.Key >> 32), tz = (int)(uint)(kv.Key & 0xffffffff);
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                tilesBuilt++;
                expected += kv.Value;
                drawn += tm.culvertEnds.Count;
                facing += tm.wallFacingErrors;
                foreach (var ce in tm.culvertEnds)
                {
                    if (Mathf.FloorToInt(ce.at.x / CityMeshes.TileSize) != tx || Mathf.FloorToInt(ce.at.y / CityMeshes.TileSize) != tz) wrongTile++;
                    if (!ce.headwall) { pipesDrawn++; continue; }
                    walls++;
                    backfills.Add(ce.backfillM);
                    if (ce.backfillMet) metFill++;
                }
                if (tm.culvertEnds.Count > 0 && tm.banks != null) ditched++;
                DestroyTileMeshes(tm);
            }
            backfills.Sort();
            Line($"    {tilesBuilt} tiles with ends built: {drawn} ends drawn of the {expected} the solve puts there ({walls} headwalls, {pipesDrawn} projecting pipes), {wrongTile} outside their tile, {facing} walls facing the wrong way; " +
                 $"the fill behind met the backfill at {metFill} of the {walls} headwalls (backfill p50 {(backfills.Count > 0 ? backfills[backfills.Count / 2] : 0f):0} m, max {(backfills.Count > 0 ? backfills[backfills.Count - 1] : 0f):0} m); {ditched} of the {tilesBuilt} tiles drew their ends' clay ditches");
            Check(drawn == expected && wrongTile == 0, "every culvert end is drawn once, by the tile it stands in (hydro audit)", $"{drawn} of {expected}, {wrongTile} outside");
            Check(metFill == walls, $"no headwall stands free on the lawn: the fill behind every one rises through its backfill within {CityCulverts.BackfillFlatM} m (hydro audit, review 2026-09-30)", $"{walls - metFill} of {walls}");

            // ---- the water the player can see
            float shownM = 0f, hiddenM = 0f, deckM = 0f;
            var byName = new Dictionary<string, (float shown, float total)>();
            const float Step = 8f;
            int sampled = 0;
            foreach (var w in map.waters)
            {
                if (w.lake || w.ravine || w.bedY == null) continue;
                for (float s = Step * 0.5f; s < w.length; s += Step)
                {
                    var p = CityCulverts.PointAt(w, s);
                    if (UnderDeck(map, p)) { deckM += Step; continue; }
                    float surf = CityElevation.CreekSurfaceY(w, s, p);
                    bool shown = CityMeshes.WaterShows(map, p, surf);
                    if (shown) shownM += Step; else hiddenM += Step;
                    if (!string.IsNullOrEmpty(w.name))
                    {
                        byName.TryGetValue(w.name, out var t);
                        byName[w.name] = (t.shown + (shown ? Step : 0f), t.total + Step);
                    }
                    if (++sampled % 20000 == 0) CityMeshes.TakeLattice();
                }
            }
            CityMeshes.TakeLattice();
            float tot = shownM + hiddenM;
            Line($"    water shown along the creeks (every {Step:0} m, surface over the drawn ground): {shownM / 1000f:0.0} of {tot / 1000f:0.0} km ({(tot > 0 ? 100f * shownM / tot : 0f):0.0}%), {deckM / 1000f:0.0} km under decks not judged");
            var named = new System.Text.StringBuilder();
            foreach (var n in new[] { "Irwin Creek", "Stewart Creek", "Little Sugar Creek", "Briar Creek", "McAlpine Creek", "Sugar Creek" })
                if (byName.TryGetValue(n, out var t) && t.total > 0f) named.Append($"{n} {100f * t.shown / t.total:0}%, ");
            Line($"    shown by creek: {named.ToString().TrimEnd(',', ' ')}");
            // the three bridges the owner named (WP-25's shots): the creek
            // every 4 m within 60 m of the crossing, both ways, off the deck
            var bridges = new StringBuilder();
            foreach (var (road, water, lat, lon) in NamedBridges)
            {
                var hint = CityCreekShots.LL(lat, lon);
                CityCreekShots.Hit hit = default; bool found = false; float best = 2500f;
                foreach (var h in CityCreekShots.Crossings(map, road, water))
                {
                    if (h.water == null || h.water.ravine) continue;
                    float d = Vector2.Distance(h.at, hint);
                    if (d < best) { best = d; hit = h; found = true; }
                }
                if (!found) { bridges.Append($"{road} x {water}: no crossing, "); continue; }
                int n = 0, shownN = 0, near = 0, nearShown = 0;
                for (float a = -60f; a <= 60.01f; a += 4f)
                {
                    float s = hit.ws + a;
                    if (s < 0f || s > hit.water.length) continue;
                    var p = CityCulverts.PointAt(hit.water, s);
                    if (UnderDeck(map, p)) continue;
                    bool sh = CityMeshes.WaterShows(map, p, CityElevation.CreekSurfaceY(hit.water, s, p));
                    n++; if (sh) shownN++;
                    if (Mathf.Abs(a) <= 24f) { near++; if (sh) nearShown++; }
                }
                bridges.Append($"{hit.edge.name} over {hit.water.name} {shownN} of {n} (within 24 m of the deck {nearShown} of {near}), ");
            }
            CityMeshes.TakeLattice();
            Line($"    at the named bridges, water shown every 4 m within 60 m off the deck: {bridges.ToString().TrimEnd(',', ' ')}");

            // ---- ponds
            int ponds = 0, pondNearRoad = 0, pondOverRoad = 0, pondShown = 0;
            float pondHa = 0f;
            var pondNotes = new List<string>();
            var segs = new HashSet<int>();
            foreach (int li in map.lakes)
            {
                var w = map.waters[li];
                float area = RingArea(w.pts);
                if (area >= 2e4f) continue;
                ponds++; pondHa += area / 1e4f;
                var c = Vector2.zero; foreach (var q in w.pts) c += q; c /= w.pts.Length;
                if (CityMap.LakeContains(w, c) && CityMeshes.LatticeAt(map, c.x, c.y) < w.surfaceY) pondShown++;
                segs.Clear();
                map.EdgeSegsInRect(w.bbMin - Vector2.one * 24f, w.bbMax + Vector2.one * 24f, segs);
                bool near = false, over = false;
                foreach (int packed in segs)
                {
                    var e = map.edges[packed >> 12]; int si = packed & 0xFFF;
                    if (si + 1 >= e.pts.Length) continue;
                    for (int i = 0; i < w.pts.Length && !near; i++)
                    {
                        float d = RoadsideOccupancy.SegSegDistance(w.pts[i], w.pts[(i + 1) % w.pts.Length], e.pts[si], e.pts[si + 1]);
                        if (d < e.width * 0.5f + 3.5f) near = true;
                        // the road's line at the foot of this shore point
                        Vector2 ra = e.pts[si], rd = e.pts[si + 1] - ra;
                        float L2 = rd.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(w.pts[i] - ra, rd) / L2) : 0f;
                        if (Vector2.Distance(w.pts[i], ra + rd * t) < e.width * 0.5f + 16f)
                        {
                            float at = e.s[si] + Mathf.Sqrt(L2) * t;
                            if (!e.ElevatedAt(at) && e.YAt(at) < w.surfaceY + 0.3f) over = true;
                        }
                    }
                }
                if (near) { pondNearRoad++; if (pondNotes.Count < 8) pondNotes.Add($"POND  within 3.5 m of a road's pavement at ({c.x:0},{c.y:0})"); }
                if (over) { pondOverRoad++; if (pondNotes.Count < 8) pondNotes.Add($"POND  surface {w.surfaceY:0.00} over a grounded road within 16 m at ({c.x:0},{c.y:0})"); }
            }
            CityMeshes.TakeLattice();
            Line($"    ponds (0.2-2 ha): {ponds} ({pondHa:0} ha), water shown at the middle of {pondShown}");
            foreach (var l in pondNotes) Line("    " + l);
            Check(pondNearRoad == 0, "no pond within 3.5 m of a road's pavement (hydro audit)", pondNearRoad);
            Check(pondOverRoad == 0, "no pond's water stands over a grounded road within 16 m (hydro audit)", pondOverRoad);
        }

        static float RingArea(Vector2[] r)
        {
            float a = 0f;
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++) a += (r[j].x + r[i].x) * (r[j].y - r[i].y);
            return Mathf.Abs(a) * 0.5f;
        }

        static readonly HashSet<int> deckScratch = new HashSet<int>();
        /// <summary>Is p under (or on) an elevated road's deck?</summary>
        static bool UnderDeck(CityMap map, Vector2 p)
        {
            deckScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 20f, p + Vector2.one * 20f, deckScratch);
            foreach (int packed in deckScratch)
            {
                var e = map.edges[packed >> 12]; int si = packed & 0xFFF;
                if (si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                if (Vector2.Distance(p, a + d * t) > e.width * 0.5f + 2f) continue;
                if (e.ElevatedAt(e.s[si] + Mathf.Sqrt(L2) * t)) return true;
            }
            return false;
        }

        static void DestroyTileMeshes(CityMeshes.TileMeshes tm)
        {
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.banks, tm.buildings, tm.lampPosts })
                if (m != null) Object.DestroyImmediate(m);
        }
    }
}
