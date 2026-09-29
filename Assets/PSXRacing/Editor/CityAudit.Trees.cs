using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    public static partial class CityAudit
    {
        // ------------------------------------------------------------------
        //  TREE AUDIT (plan WP-08). The trees are planted from the tile's
        //  RoadsideOccupancy mask; this checks what was planted AGAINST THE
        //  GEOMETRY ITSELF, measured again here, so a mask that lost a road
        //  or a building shows up as a trunk where it must not be:
        //    * no trunk on pavement or in its class's clear zone, within a
        //      junction's fan, in a real building or a lot or a fill house,
        //      in a creek or a lake, or under a deck;
        //    * every trunk on an empty cell of its tile's mask (the sight
        //      triangles and corner spots are only in the mask);
        //    * every tree inside the tile that planted it (none twice across
        //      a seam), and the same trees on a second build;
        //    * the planted count against what the canopy asks, per tile;
        //    * the canopy the crowns make within 25 m of the centreline, by
        //    class, against the canopy map's own at the same points.
        // ------------------------------------------------------------------

        /// <summary>
        /// The canopy map's own value within 25 m of the centreline, by class,
        /// over EXACTLY this audit's samples (its tiles, stations and offsets),
        /// read off the 30 m USFS raster by tools/city/canopy.mjs, which prints
        /// this table: the game ships the 60 m grid, whose cells smear the
        /// woods beside a road over it. (class, percent, sample points).
        /// </summary>
        static readonly (string cls, float pct, int n)[] CanopyBandTruth =
        {
            ("secondary", 13.0f, 23746), ("motorway", 14.9f, 10739), ("core arterials", 8.1f, 12997), ("core residential", 31.0f, 23608),
        };
        /// <summary>The plan's tolerance on the canopy within 25 m, points.</summary>
        const float CanopyBandTolPts = 5f;
        /// <summary>The plan's tolerance on a tile's planted count.</summary>
        const float PlantedTol = 0.2f;

        /// <summary>The tree audit alone (the fast loop while tuning the
        /// planting): writes city_tree_audit.txt at the project root.
        /// Headless: -executeMethod PSXRacing.EditorTools.CityAudit.RunTrees</summary>
        [UnityEditor.MenuItem("PSX Racing/Audit City Trees")]
        public static void RunTrees()
        {
            outLog = new System.Text.StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); return; }
            TreeAudit(map, CityMeshes.NodeTrims(map), CityBuildings.Precompute(map));
            outLog.AppendLine(failures == 0 ? "TREE AUDIT OK" : $"TREE AUDIT: {failures} FAILURES");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "city_tree_audit.txt"), outLog.ToString());
        }

        static void TreeAudit(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            Line("tree audit (WP-08): canopy " + (CityCanopy.Loaded ? CityCanopy.Year + " grid loaded" : "MISSING"));
            Check(CityCanopy.Loaded, "the canopy grid (charlotte_canopy.bytes) loads");
            // the kit carries the atlas measurements the planting reads
            PSXRacingBuilder.EnsureCityTextures();
            var kit = CityKit.Get();
            Check(kit != null && kit.trees != null && kit.trees.Length == Seasons.DressCount && System.Array.TrueForAll(kit.trees, m => m != null)
                  && kit.treeLowReach != null && kit.treeLowReach.Length == 16 * CityTrees.ReachLevels,
                  "the city kit holds a tree material per season dress and the atlas's low-foliage table");
            if (!CityCanopy.Loaded) return;
            var keepOverride = CityTrees.DensityOverride;
            CityTrees.DensityOverride = 1f;
            try { TreeAuditInner(map, trims, buildings); }
            finally { CityTrees.DensityOverride = keepOverride; }
        }

        static void TreeAuditInner(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            float ts = CityMeshes.TileSize;
            var tiles = new List<(int tx, int tz, string why)>();
            var seen = new HashSet<long>();
            void Add(Vector2 at, string why)
            {
                int tx = Mathf.FloorToInt(at.x / ts), tz = Mathf.FloorToInt(at.y / ts);
                if (seen.Add(TileKey(tx, tz))) tiles.Add((tx, tz, why));
            }
            // the plan's shot spots, each with its neighbours, then a spread
            var spots = new (string id, double lat, double lon)[]
            {
                ("myers park", 35.19280, -80.83678), ("dilworth", 35.20225, -80.84758), ("plaza midwood", 35.22019, -80.80899),
                ("independence strip", 35.16804, -80.74309), ("i-485 south", 35.06068, -80.76398), ("uptown", 35.22662, -80.84248),
            };
            foreach (var s in spots)
            {
                var c = LLtoGame(s.lat, s.lon);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++) Add(c + new Vector2(dx, dz) * ts, s.id);
            }
            // a spread across the beltway: every 9th tile of a lattice over the road network's box
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = -lo;
            foreach (var n in map.nodes) { lo = Vector2.Min(lo, n); hi = Vector2.Max(hi, n); }
            for (float z = lo.y + 4 * ts; z < hi.y; z += 9 * ts)
                for (float x = lo.x + 4 * ts; x < hi.x; x += 9 * ts) Add(new Vector2(x, z), "spread");
            // and every tile the three city race routes run through (their
            // run-off: WP-08 review). Not in the canopy bands, whose truth
            // table is over exactly the tiles above.
            int bandTiles = tiles.Count;
            if (map.routes != null)
                foreach (var r in map.routes)
                    foreach (int ei in r.edges)
                    {
                        var e = map.edges[ei];
                        for (float s = 0f; s <= e.length; s += 32f) Add(e.PointAt(s), "route " + r.id);
                        Add(e.PointAt(e.length), "route " + r.id);
                    }

            int trees = 0, solids = 0, emptyTiles = 0, breakaway = 0, runOffTrees = 0, routeTiles = tiles.Count - bandTiles;
            int qBad = 0; string qBadAt = "";
            var solidBySpecies = new int[6];
            var bad = new Dictionary<string, int>();
            var badWhere = new Dictionary<string, string>();
            void Bad(string what, Vector3 at, string detail)
            {
                bad.TryGetValue(what, out int n);
                bad[what] = n + 1;
                if (n == 0) badWhere[what] = $"({at.x:0.0},{at.z:0.0}) {LatLon(at.x, at.z)} {detail}";
            }
            var rejects = new int[CityTrees.RejectCount];
            var perTile = new List<int>();
            var ratios = new List<(float ratio, float wanted, int planted, string where)>();
            float msSum = 0f, msMax = 0f, occSum = 0f;
            int grown = 0, shrunk = 0, lined = 0;
            var segs = new HashSet<int>();
            var wsegs = new HashSet<int>();
            bool same = true;
            int outside = 0;
            var byClass = new Dictionary<string, (int n, float data, float planted)>();

            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var (tx, tz, why) = tiles[ti];
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                var tt = CityTrees.Build(map, trims, buildings, tm, tx, tz);
                msSum += tt.ms; msMax = Mathf.Max(msMax, tt.ms); occSum += tt.occMs;
                trees += tt.trees.Count; solids += tt.solids; grown += tt.grown; shrunk += tt.shrunk; lined += tt.lined; breakaway += tt.breakaway;
                for (int k = 0; k < rejects.Length; k++) rejects[k] += tt.rejects[k];
                perTile.Add(tt.trees.Count);
                if (tt.trees.Count == 0) emptyTiles++;
                float target = Mathf.Min(tt.wanted, CityTrees.MaxPerTile);
                if (target >= 20f)
                    ratios.Add((tt.trees.Count / target, target, tt.trees.Count, $"tile {tx},{tz} ({why})"));

                // the same trees again
                var again = CityTrees.Build(map, trims, buildings, tm, tx, tz);
                if (again.trees.Count != tt.trees.Count) same = false;
                else for (int i = 0; i < tt.trees.Count && same; i++)
                        if ((again.trees[i].foot - tt.trees[i].foot).sqrMagnitude > 1e-6f || again.trees[i].h != tt.trees[i].h) same = false;
                if (again.mesh != null) Object.DestroyImmediate(again.mesh);

                var min = new Vector2(tx * ts, tz * ts);
                foreach (var t in tt.trees)
                {
                    var p = new Vector2(t.foot.x, t.foot.z);
                    if (p.x < min.x || p.y < min.y || p.x >= min.x + ts || p.y >= min.y + ts) { outside++; Bad("outside its tile", t.foot, ""); }
                    byte bits = tt.occ.At(p);
                    if (bits != 0) Bad("on a reserved cell of the mask", t.foot, "bits " + bits);
                    // race run-off, measured against the route marks themselves
                    if (RaceRunOff.Inside(map, trims, p)) { runOffTrees++; Bad("in race run-off", t.foot, why); }
                    // Q15: solid only for a trunk of 30 cm or more, within reach of a road
                    if (t.solid) solidBySpecies[(int)t.species]++;
                    bool q15 = t.solid == (t.road <= CityTrees.TrunkReachM && t.dbh >= CityTrees.SolidTrunkM)
                               && !(t.solid && (t.species == CityTrees.Species.Myrtle || t.species == CityTrees.Species.Bare));
                    if (!q15) { qBad++; if (qBad == 1) qBadAt = $"({t.foot.x:0},{t.foot.z:0}) {t.species} dbh {t.dbh:0.00} road {t.road:0.0} solid {t.solid}"; }

                    // roads, measured again: every grounded piece's pavement and
                    // clear zone, every deck's plan plus its margin
                    segs.Clear();
                    map.EdgeSegsInRect(p - Vector2.one * 40f, p + Vector2.one * 40f, segs);
                    foreach (int packed in segs)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        var e = map.edges[ei];
                        if (e.tunnel) continue;
                        Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float tq = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                        float at = e.s[si] + Mathf.Sqrt(L2) * tq;
                        RoadsideOccupancy.RoadEdgeAt(e, trims, at, out var cp, out _, out float hwL, out float hwR);
                        float off = Vector2.Distance(p, a + d * tq) - Mathf.Max(hwL, hwR);
                        bool deck = e.bridge || e.ElevatedAt(at);
                        if (deck) { if (off < RoadsideOccupancy.DeckMarginM - 0.05f) Bad("under a deck", t.foot, $"e{ei} '{e.name}' {off:0.00} m"); }
                        else if (off < 0f) Bad("on pavement", t.foot, $"e{ei} '{e.name}' {off:0.00} m");
                        else if (off < RoadsideOccupancy.ClearZoneOf(e) - 0.05f) Bad("in a clear zone", t.foot, $"e{ei} '{e.name}' cls{e.cls} {off:0.00} m of {RoadsideOccupancy.ClearZoneOf(e)}");
                        if (off < t.w * 0.5f + 0.4f)
                        {
                            // the painted leaves under 4.2 m over this road, toward it, on either card
                            float need = e.YAt(at) + CityTrees.OverhangClearM - t.foot.y;
                            float reach = CityTrees.LowReach(t.cell, need / t.h) * t.w;
                            float hw = Vector2.Distance(p, a + d * tq) - off;
                            for (int q = 0; q < 2; q++)
                            {
                                float ang = (t.yawDeg + q * 90f) * Mathf.Deg2Rad;
                                var u = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * reach;
                                float gap = RoadsideOccupancy.SegSegDistance(p - u, p + u, a, a + d) - hw;
                                if (gap < 0.4f - 0.05f)
                                    Bad("a card paints leaves over the road below 4.2 m", t.foot, $"e{ei} '{e.name}' its low leaves {gap:0.00} m off the pavement (reach {reach:0.00}, trunk {off:0.00} m off)");
                            }
                        }
                    }
                    // junction fans
                    foreach (int packed in segs)
                    {
                        var e = map.edges[packed >> 12];
                        foreach (int nd in new[] { e.a, e.b })
                        {
                            if (!trims.patch[nd]) continue;
                            float reach = 0f;
                            foreach (int oi in map.nodeEdges[nd])
                            {
                                var o = map.edges[oi];
                                float tr = trims.TrimAt(o, nd), hw = o.width * 0.5f;
                                reach = Mathf.Max(reach, Mathf.Sqrt(tr * tr + hw * hw));
                            }
                            if (Vector2.Distance(map.nodes[nd], p) < reach) Bad("in a junction fan", t.foot, $"node {nd}");
                        }
                    }
                    // buildings: real footprints, lots, fill houses
                    if (!map.FootprintClear(p, t.r)) Bad("in a real building", t.foot, "");
                    for (int bz = tz - 1; bz <= tz + 1; bz++)
                        for (int bx = tx - 1; bx <= tx + 1; bx++)
                        {
                            if (!buildings.TryGetValue(((long)bx << 24) ^ (bz & 0xFFFFFF), out var lots)) continue;
                            foreach (var b in lots)
                            {
                                var q = p - b.pos;
                                float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                                float m = RoadsideOccupancy.LotMarginOf(b.kind) + t.r;
                                if (Mathf.Abs(q.x * cy - q.y * sy) < b.w * 0.5f + m && Mathf.Abs(q.x * sy + q.y * cy) < b.d * 0.5f + m)
                                    Bad(b.kind == 0 ? "in a procedural building" : "on a prop lot", t.foot, "kind " + b.kind);
                            }
                        }
                    foreach (var h in tm.houseBoxes)
                    {
                        var q = p - h.c; var v = new Vector2(-h.u.y, h.u.x);
                        if (Mathf.Abs(Vector2.Dot(q, h.u)) < h.hu + t.r && Mathf.Abs(Vector2.Dot(q, v)) < h.hv + t.r) Bad("in a fill house", t.foot, "");
                    }
                    // water
                    wsegs.Clear();
                    map.WaterSegsInRect(p - Vector2.one * 40f, p + Vector2.one * 40f, wsegs);
                    foreach (int packed in wsegs)
                    {
                        var w = map.waters[packed >> 12]; int si = packed & 0xFFF;
                        if (w.lake || w.ravine || si + 1 >= w.pts.Length) continue;
                        if (RoadsideOccupancy.DistToSeg(p, w.pts[si], w.pts[si + 1]) < w.width * 0.5f) Bad("in a creek", t.foot, w.name);
                    }
                    if (map.InLake(p)) Bad("in a lake", t.foot, "");
                }

                // the canopy the crowns make within 25 m, for the class bands
                // whose edges run through this tile (samples inside it only)
                if (ti < bandTiles) CanopyBands(map, tt, tx, tz, byClass);
                if (tt.mesh != null) Object.DestroyImmediate(tt.mesh);
                DiscardMeshes(tm);
            }

            perTile.Sort();
            int P(float q) => perTile.Count == 0 ? 0 : perTile[Mathf.Min(perTile.Count - 1, (int)(q * perTile.Count))];
            Line($"    {tiles.Count} tiles, {trees} trees ({solids} with a trunk collider), {emptyTiles} tiles with none; per tile p50 {P(0.5f)} p90 {P(0.9f)} max {P(1f)}; " +
                 $"plant {msSum / Mathf.Max(1, tiles.Count):0.0} ms a tile (the mask {occSum / Mathf.Max(1, tiles.Count):0.0}; max {msMax:0.0}); grown for the overhang {grown}, shrunk {shrunk}; in freeway tree walls {lined}");
            var rj = new List<string>();
            for (int k = 0; k < rejects.Length; k++) if (rejects[k] > 0) rj.Add($"{CityTrees.RejectNames[k]} {rejects[k]}");
            Line("    candidates refused: " + string.Join(", ", rj));
            foreach (var kv in bad) Line($"    {kv.Key}: {kv.Value}  e.g. {badWhere[kv.Key]}");
            int badTotal = 0; foreach (var v in bad.Values) badTotal += v;
            Check(trees > 0, "the city plants trees (tree audit)", trees);
            Check(badTotal == 0, "no trunk on pavement, in a clear zone or fan, a building or lot, water, under a deck, or on a reserved cell; no low leaves over a road (tree audit)", badTotal);
            Check(outside == 0, "every tree stands in the tile that planted it: none doubled across a seam (tree audit)", outside);
            Check(same, "a tile plants the same trees every build (tree audit)");
            Line($"    race run-off (RaceRunOff: {RaceRunOff.RunOffM:0} m past the edge along the routes, {RaceRunOff.CornerRunOffM:0} m on the outside of bends): " +
                 $"{RaceRunOff.Tiles(map, trims)} tiles; {routeTiles} route tiles audited besides the {bandTiles} above");
            Check(runOffTrees == 0, "no tree in the city race routes' run-off (tree audit)", runOffTrees);
            Line($"    Q15: {solids} solid (a trunk of {CityTrees.SolidTrunkM * 100f:0} cm or more within {CityTrees.TrunkReachM:0} m of a road: oak {solidBySpecies[0]}, hardwood {solidBySpecies[1]}, pine {solidBySpecies[2]}, sycamore {solidBySpecies[4]}), " +
                 $"{breakaway} within reach that break away (crape myrtles, snags, young trees, thin trunks)");
            Check(qBad == 0 && solidBySpecies[(int)CityTrees.Species.Myrtle] == 0 && solidBySpecies[(int)CityTrees.Species.Bare] == 0,
                  "Q15: only trunks of 30 cm or more are solid; crape myrtles, snags and young trees break away (tree audit)", qBad == 0 ? "0" : qBad + " e.g. " + qBadAt);

            ratios.Sort((a, b) => a.ratio.CompareTo(b.ratio));
            int inside = 0; foreach (var r in ratios) if (Mathf.Abs(r.ratio - 1f) <= PlantedTol) inside++;
            float med = ratios.Count > 0 ? ratios[ratios.Count / 2].ratio : 0f;
            string worst = ratios.Count > 0 ? $"lowest {ratios[0].ratio:0.00} ({ratios[0].planted} of {ratios[0].wanted:0} at {ratios[0].where})" : "";
            Line($"    planted / what the canopy asks, {ratios.Count} tiles asking 20+: median {med:0.00}, {inside} within +-{PlantedTol * 100:0}%; {worst}");
            Check(ratios.Count > 0 && Mathf.Abs(med - 1f) <= PlantedTol, "the planted count per tile matches the canopy map (median within +-20%) (tree audit)",
                  $"median {med:0.00}, {inside} of {ratios.Count} tiles within +-20%");

            foreach (var (cls, truth, truthN) in CanopyBandTruth)
            {
                byClass.TryGetValue(cls, out var v);
                if (v.n < 200) { Check(false, $"canopy within 25 m, {cls}: sampled", v.n + " points"); continue; }
                float grid = 100f * v.data / v.n, planted = 100f * v.planted / v.n;
                Check(Mathf.Abs(planted - truth) <= CanopyBandTolPts,
                      $"canopy within 25 m of the centreline, {cls}: the crowns within {CanopyBandTolPts:0} points of the canopy map (tree audit)",
                      $"crowns {planted:0.0}%, the 30 m map {truth:0.0}% ({truthN} points; here {v.n}), the shipped 60 m grid {grid:0.0}%");
            }
        }

        /// <summary>
        /// Along every edge of a band's class through this tile, every 10 m and
        /// at 0, 5 .. 25 m either side, inside the tile only: the canopy map's
        /// value and whether a crown covers the point.
        /// </summary>
        static void CanopyBands(CityMap map, CityTrees.TreeTile tt, int tx, int tz, Dictionary<string, (int n, float data, float planted)> acc)
        {
            float ts = CityMeshes.TileSize;
            var min = new Vector2(tx * ts, tz * ts);
            var max = min + Vector2.one * ts;
            var segs = new HashSet<int>();
            // a road just outside the tile still has samples inside it
            map.EdgeSegsInRect(min - Vector2.one * 26f, max + Vector2.one * 26f, segs);
            // crowns bucketed by 16 m
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < tt.trees.Count; i++)
            {
                var f = tt.trees[i].foot;
                long k = ((long)Mathf.FloorToInt(f.x / 16f) << 32) ^ (uint)Mathf.FloorToInt(f.z / 16f);
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                l.Add(i);
            }
            bool Covered(Vector2 q)
            {
                int cx = Mathf.FloorToInt(q.x / 16f), cz = Mathf.FloorToInt(q.y / 16f);
                for (int z = cz - 1; z <= cz + 1; z++)
                    for (int x = cx - 1; x <= cx + 1; x++)
                    {
                        if (!grid.TryGetValue(((long)x << 32) ^ (uint)z, out var l)) continue;
                        foreach (int i in l)
                        {
                            var t = tt.trees[i];
                            float r = CityTrees.CrownRadius(t);
                            float dx = t.foot.x - q.x, dz = t.foot.z - q.y;
                            if (dx * dx + dz * dz <= r * r) return true;
                        }
                    }
                return false;
            }
            var core = new Rect(map.uptown - Vector2.one * 4000f, Vector2.one * 8000f);
            foreach (int packed in segs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.tunnel) continue;
                string c = e.link ? null : e.cls >= 5 ? "motorway" : e.cls == 2 ? "secondary" : null;
                bool inCore = core.Contains(e.PointAt(e.length * 0.5f));
                string c2 = !inCore || e.link ? null : e.cls == 0 ? "core residential" : (e.cls == 2 || e.cls == 3) ? "core arterials" : null;
                if (c == null && c2 == null) continue;
                Vector2 a = e.pts[si], b = e.pts[si + 1];
                float L = Vector2.Distance(a, b);
                if (L < 1e-3f) continue;
                var dir = (b - a) / L; var nrm = new Vector2(-dir.y, dir.x);
                // stations on the edge's own 10 m lattice, so a segment split across tiles is sampled once
                float s0 = e.s[si];
                for (float s = Mathf.Ceil((s0 - 5f) / 10f) * 10f + 5f; s < s0 + L; s += 10f)
                {
                    if (s < s0) continue;
                    var cpt = a + dir * (s - s0);
                    for (int o = -25; o <= 25; o += 5)
                    {
                        var q = cpt + nrm * o;
                        if (q.x < min.x || q.y < min.y || q.x >= max.x || q.y >= max.y) continue;
                        float data = CityCanopy.Fraction(q.x, q.y);
                        float cov = Covered(q) ? 1f : 0f;
                        foreach (var k in new[] { c, c2 })
                        {
                            if (k == null) continue;
                            acc.TryGetValue(k, out var v);
                            acc[k] = (v.n + 1, v.data + data, v.planted + cov);
                        }
                    }
                }
            }
        }
    }
}
