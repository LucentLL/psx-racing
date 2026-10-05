using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    public static partial class CityAudit
    {
        // ------------------------------------------------------------------
        //  POLE AUDIT (plan WP-15: "LampAudit extended"). The utility poles
        //  are decided from the static roadside mask; this checks what stood
        //  against the geometry, measured again, and against the tile's FULL
        //  mask (its fill houses and lamp posts too):
        //    * no pole on pavement or in a clear zone, under a deck, in a
        //      building, lot, lake or race run-off, or on a cell the mask
        //      reserves (sight triangles, corner spots); none on a freeway,
        //      uptown or in Myers Park; none on a fill house or a lamp post;
        //    * every wire at least 5.5 m over every road it crosses;
        //    * the pitch along a line 40-60 m at p50;
        //    * every pole in the tile that stands it, none twice across a
        //      seam, the next tile standing the pole a span strings to, the
        //      same poles on a second build;
        //    * one mesh a tile (the poles, wires and lamps: one draw), a
        //      cobra-head on every pole, the poles solid;
        //    * uptown's streets lit by acorn posts.
        // ------------------------------------------------------------------

        const float PolePitchLo = 40f, PolePitchHi = 60f;
        const float PoleLampClashM = 0.8f;
        /// <summary>Uptown's race streets: a point of the road this far from
        /// every light is dark (printed, not a check); at least this share of
        /// their lamp stations stand a post.</summary>
        const float UptownDarkM = 20f, UptownStationShareMin = 0.85f;

        /// <summary>The pole audit alone: writes city_pole_audit.txt at the
        /// project root. Headless: -executeMethod PSXRacing.EditorTools.CityAudit.RunPoles</summary>
        [UnityEditor.MenuItem("PSX Racing/Audit City Poles")]
        public static void RunPoles()
        {
            outLog = new System.Text.StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); return; }
            PoleAudit(map, CityMeshes.NodeTrims(map), CityBuildings.Precompute(map));
            outLog.AppendLine(failures == 0 ? "POLE AUDIT OK" : $"POLE AUDIT: {failures} FAILURES");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "city_pole_audit.txt"), outLog.ToString());
        }

        /// <summary>Where the pole audit looks (the plan's shots: Albemarle,
        /// Rocky River, Central Ave, uptown) and the other kinds of road.</summary>
        static readonly (string id, double lat, double lon)[] PoleSpots =
        {
            ("albemarle", 35.20239, -80.72967), ("rocky river", 35.27495, -80.69288), ("central ave", 35.22019, -80.80899),
            ("uptown", 35.2270, -80.8431), ("brentwood", 35.21548, -80.87690), ("south blvd", 35.15993, -80.87621),
            ("providence", 35.11889, -80.77971), ("myers park", 35.19280, -80.83678), ("beatties ford", 35.33571, -80.87534),
        };

        static void PoleAudit(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            PSXRacingBuilder.EnsureCityTextures();
            CityPoles.DecideMs = CityPoles.SpanMs = 0; CityPoles.Decisions = CityPoles.Spans = 0;
            RoadsideOccupancy.StaticBuilds = 0; RoadsideOccupancy.StaticMs = 0;
            var kit = CityKit.Get();
            var fm = kit != null ? kit.furniture : null;
            Check(fm != null && fm.mainTexture != null && fm.IsKeywordEnabled("PSX_FURNITURE") && !fm.IsKeywordEnabled("PSX_ATLAS_RECT"),
                  "the city kit's furniture material wears the pack atlas on PSX/Lit's PSX_FURNITURE variant (pole audit)");
            bool treesWere = CityTrees.Enabled, signsWere = CitySigns.Enabled, polesWere = CityPoles.Enabled;
            CityTrees.Enabled = false; CitySigns.Enabled = false; CityPoles.Enabled = true;
            try { PoleAuditInner(map, trims, buildings); }
            finally { CityTrees.Enabled = treesWere; CitySigns.Enabled = signsWere; CityPoles.Enabled = polesWere; CityMeshes.LampTrace = null; }
        }

        static void PoleAuditInner(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            float ts = CityMeshes.TileSize;
            var tiles = new List<(int tx, int tz, string why)>();
            var seen = new HashSet<long>();
            void Add(Vector2 at, string why)
            {
                int tx = Mathf.FloorToInt(at.x / ts), tz = Mathf.FloorToInt(at.y / ts);
                if (seen.Add(TileKey(tx, tz))) tiles.Add((tx, tz, why));
            }
            foreach (var s in PoleSpots)
            {
                var c = LLtoGame(s.lat, s.lon);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++) Add(c + new Vector2(dx, dz) * ts, s.id);
            }
            int spotTiles = tiles.Count;
            // every tile of the city race routes (their run-off)
            if (map.routes != null)
                foreach (var r in map.routes)
                    foreach (int ei in r.edges)
                    {
                        var e = map.edges[ei];
                        for (float s = 0f; s <= e.length; s += 32f) Add(e.PointAt(s), "race " + r.id);
                    }

            var bad = new Dictionary<string, int>();
            var badWhere = new Dictionary<string, string>();
            void Bad(string what, Vector2 at, string detail)
            {
                bad.TryGetValue(what, out int n);
                bad[what] = n + 1;
                if (n == 0) badWhere[what] = $"({at.x:0.0},{at.y:0.0}) {LatLon(at.x, at.y)} {detail}";
            }
            int poles = 0, spans = 0, wires = 0, heads = 0, stations = 0, refused = 0, lampsDrawn = 0, multiSub = 0, tilesWith = 0;
            int spanLong = 0, spanRoad = 0, spanGround = 0, spanBlocked = 0, acorns = 0, uptownAcorns = 0, clash = 0;
            float worstClear = float.MaxValue, msSum = 0f, msMax = 0f;
            string worstClearAt = "";
            int verts = 0;
            var whyTotals = new int[12];
            var owned = new Dictionary<long, (Vector2 f, int tile)>();
            var byChain = new Dictionary<int, List<(int k, Vector2 f)>>();
            var ends = new List<(Vector2 f, long tileKey)>();
            var tileSet = new HashSet<long>();
            foreach (var t in tiles) tileSet.Add(TileKey(t.tx, t.tz));
            long FootKey(Vector2 f) => ((long)Mathf.RoundToInt(f.x * 4f) << 32) ^ (uint)Mathf.RoundToInt(f.y * 4f);
            List<Vector3> firstPoles = null;
            float darkM = 0f, poleRoadM = 0f;
            // every light the audited tiles stand (lamp heads and cobra-heads),
            // bucketed: how well uptown's race streets are lit (the WP-15 review)
            var lightCells = new Dictionary<long, List<Vector2>>();
            long LightCell(Vector2 p) => ((long)Mathf.FloorToInt(p.x / 32f) << 32) ^ (uint)Mathf.FloorToInt(p.y / 32f);
            void AddLight(Vector2 p) { long k = LightCell(p); if (!lightCells.TryGetValue(k, out var l)) lightCells[k] = l = new List<Vector2>(); l.Add(p); }
            int lampsBreakaway = 0, lampsSolid = 0, colliderTiles = 0, colliderBad = 0, acornsBreakaway = 0;
            // why each lamp station of uptown's race streets stood a post or not
            var acornRace = new HashSet<int>();
            if (map.routes != null)
                foreach (var r in map.routes)
                    foreach (int ei in r.edges)
                        if (CityPoles.IsAcornEdge(map, map.edges[ei])) acornRace.Add(ei);
            var lampStations = new Dictionary<int, List<(float s, int why)>>();
            CityMeshes.LampTrace = (ei, s, w) =>
            {
                if (!acornRace.Contains(ei)) return;
                if (!lampStations.TryGetValue(ei, out var l)) lampStations[ei] = l = new List<(float, int)>();
                l.Add((s, w));
            };

            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var (tx, tz, why) = tiles[ti];
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                var fresh = RoadsideOccupancy.Build(map, trims, buildings, tm, tx, tz);
                var tt = CityTrees.Build(map, trims, buildings, tm, tx, tz);
                var pt = tt.poles;
                var min = new Vector2(tx * ts, tz * ts);
                var max = min + Vector2.one * ts;
                int tileBreakaway = 0;
                foreach (var l in tm.lamps)
                {
                    if (l.kind == CityMeshes.LampAcorn) { acorns++; if (why == "uptown") uptownAcorns++; }
                    // a lamp post in a race route's run-off breaks away (no
                    // collider; the WP-15 review), every other one is solid
                    var lf = tm.origin + l.foot;
                    bool inRun = RaceRunOff.Inside(map, trims, new Vector2(lf.x, lf.z));
                    if (inRun && !l.breakaway) Bad("a solid lamp post in race run-off", new Vector2(lf.x, lf.z), $"kind {l.kind}");
                    // (a deck's light is the deck's, no post: breakaway anywhere)
                    if (!inRun && l.breakaway && l.kind != CityMeshes.LampDeck) Bad("a breakaway lamp post outside race run-off", new Vector2(lf.x, lf.z), $"kind {l.kind}");
                    if (l.breakaway) { lampsBreakaway++; tileBreakaway++; if (l.kind == CityMeshes.LampAcorn) acornsBreakaway++; } else lampsSolid++;
                    var lh = tm.origin + l.head;
                    AddLight(new Vector2(lh.x, lh.z));
                }
                // stood up as the game does: a box on the LampPost object for
                // every solid post and none for a breakaway one
                if (tileBreakaway > 0)
                {
                    var root = new GameObject("PoleAuditLamps");
                    try
                    {
                        CityWorld.Attach(root, tm, null);
                        var lp = root.transform.Find(CityWorld.LampPostName);
                        int boxes = lp != null ? lp.GetComponents<BoxCollider>().Length : 0;
                        colliderTiles++;
                        if (boxes != tm.lamps.Count - tileBreakaway) { colliderBad++; Bad("lamp colliders not one a solid post", min + Vector2.one * (ts * 0.5f), $"{boxes} boxes for {tm.lamps.Count - tileBreakaway} solid posts"); }
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                if (pt == null) { DiscardMeshes(tm); continue; }
                if (ti == 0) { firstPoles = new List<Vector3>(); foreach (var p in pt.poles) firstPoles.Add(p.foot); }
                msSum += pt.ms; msMax = Mathf.Max(msMax, pt.ms);
                stations += pt.stations; refused += pt.refused; lampsDrawn += pt.lampsDrawn;
                for (int b = 0; b < 12; b++) whyTotals[b] += pt.refusedWhy[b];
                spanLong += pt.spanLong; spanRoad += pt.spanRoad; spanGround += pt.spanGround; spanBlocked += pt.spanBlocked;
                if (pt.poles.Count > 0) tilesWith++;
                if (pt.mesh != null) { if (pt.mesh.subMeshCount != 1) multiSub++; verts += pt.mesh.vertexCount; }
                heads += pt.heads.Count;
                foreach (var h in pt.heads) AddLight(new Vector2(h.x, h.z));
                wires += pt.wires;

                foreach (var p in pt.poles)
                {
                    poles++;
                    var f = new Vector2(p.foot.x, p.foot.z);
                    var e = map.edges[p.edge];
                    string of = $"e{p.edge} '{e.name}' cls{e.cls} line {p.chain} k {p.k}";
                    if (f.x < min.x || f.y < min.y || f.x >= max.x || f.y >= max.y) Bad("a pole outside the tile that stands it", f, of);
                    byte sb = RoadsideOccupancy.StaticAt(map, trims, buildings, f);
                    if (sb != 0) Bad("a pole on a reserved cell of the mask", f, $"{of} bits {sb} ({string.Join(", ", BitList(sb))})");
                    byte fb = fresh.At(f);
                    if ((fb & RoadsideOccupancy.Building) != 0) Bad("a pole on a fill house or building", f, of);
                    foreach (var l in tm.lamps)
                    {
                        var lf = tm.origin + l.foot;
                        if (Vector2.Distance(new Vector2(lf.x, lf.z), f) < PoleLampClashM) { clash++; Bad("a pole in a lamp post", f, of); }
                    }
                    float w = CitySigns.WorstRoad(map, trims, f, out int we, out string what);
                    if (w < -0.05f) Bad("a pole " + what, f, $"{of}; e{we} '{(we >= 0 ? map.edges[we].name : "")}' {w:0.00} m inside");
                    if (!map.FootprintClear(f, 0.6f)) Bad("a pole in a real building", f, of);
                    if (map.InLake(f)) Bad("a pole in a lake", f, of);
                    if (RaceRunOff.Inside(map, trims, f)) Bad("a pole in race run-off", f, of);
                    if (e.cls >= 5 || e.link) Bad("a pole on a freeway or a ramp", f, of);
                    if (CityPoles.Uptown(map, f)) Bad("a pole uptown (inside the freeway loop)", f, of);
                    if (CityPoles.InMyersPark(f)) Bad("a pole in Myers Park", f, of);
                    if (p.top - p.foot.y < CityPoles.MinHeightM - 0.01f || p.top - p.foot.y > CityPoles.MaxHeightM + 0.01f) Bad("a pole not 12-14 m", f, of);
                    long fk = FootKey(f);
                    if (owned.TryGetValue(fk, out var prev) && prev.tile != ti) Bad("a pole stood twice (a seam)", f, of);
                    else owned[fk] = (f, ti);
                    if (!byChain.TryGetValue(p.chain, out var list)) byChain[p.chain] = list = new List<(int, Vector2)>();
                    list.Add((p.k, f));
                }
                foreach (var (a, b) in pt.spans)
                {
                    spans++;
                    var pa = new Vector2(a.foot.x, a.foot.z); var pb = new Vector2(b.foot.x, b.foot.z);
                    float L = Vector2.Distance(pa, pb);
                    int n = Mathf.Max(2, Mathf.CeilToInt(L));
                    for (int i = 0; i <= n; i++)
                    {
                        float t = i / (float)n;
                        var q = Vector2.Lerp(pa, pb, t);
                        float road = CitySigns.RoadTopAt(map, trims, q, 0f);
                        if (float.IsNegativeInfinity(road)) continue;
                        float clear = CityPoles.LowestWireAt(a, b, t) - road;
                        if (clear < worstClear) { worstClear = clear; worstClearAt = $"({q.x:0},{q.y:0}) {LatLon(q.x, q.y)} line {a.chain}"; }
                        if (clear < CityPoles.WireClearM - 0.01f) { Bad("a wire under 5.5 m over a road", q, $"{clear:0.00} m, line {a.chain} k {a.k}"); break; }
                    }
                    ends.Add((pb, TileKey(Mathf.FloorToInt(pb.x / ts), Mathf.FloorToInt(pb.y / ts))));
                }
                // how much of the tile's pole roads a light reaches (not a check:
                // the cobra-heads replaced their lamp posts)
                var lights = new List<Vector2>();
                foreach (var l in tm.lamps) { var h = tm.origin + l.head; lights.Add(new Vector2(h.x, h.z)); }
                foreach (var h in pt.heads) lights.Add(new Vector2(h.x, h.z));
                var segs = new HashSet<int>();
                map.EdgeSegsInRect(min, max, segs);
                var eh = new HashSet<int>();
                foreach (int packed in segs) eh.Add(packed >> 12);
                foreach (int ei in eh)
                {
                    var e = map.edges[ei];
                    if (e.bridge || !CityPoles.IsPoleEdge(map, e)) continue;
                    for (float s = 2.5f; s < e.length; s += 5f)
                    {
                        var q = e.PointAt(s);
                        if (q.x < min.x || q.y < min.y || q.x >= max.x || q.y >= max.y) continue;
                        poleRoadM += 5f;
                        float best = float.MaxValue;
                        foreach (var lp in lights) best = Mathf.Min(best, (lp - q).sqrMagnitude);
                        if (best > 40f * 40f) darkM += 5f;
                    }
                }
                if (pt.mesh != null) Object.DestroyImmediate(pt.mesh);
                if (tt.mesh != null) Object.DestroyImmediate(tt.mesh);
                DiscardMeshes(tm);
            }

            // UPTOWN'S RACE STREETS LIT (the WP-15 review: N Tryon lost every
            // post to the run-off rule, bare by day and black at night): along
            // every city race route's uptown streets (acorn roads), in the
            // audited tiles, the share of the road more than UptownDarkM from
            // any light
            float upRaceM = 0f, upDarkM = 0f;
            string upDarkAt = "";
            var upRouteDark = new Dictionary<string, (float m, float dark)>();
            var upNearest = new List<float>();
            var darkRuns = new List<(float m, string what)>();
            if (map.routes != null)
                foreach (var r in map.routes)
                {
                    var routeEdges = new HashSet<int>(r.edges);
                    foreach (int ei in routeEdges)
                    {
                        var e = map.edges[ei];
                        if (e.bridge || e.tunnel || !CityPoles.IsAcornEdge(map, e)) continue;
                        float runM = 0f, runS = 0f;
                        void EndRun(float sEnd)
                        {
                            if (runM <= 0f) return;
                            var q0 = e.PointAt(runS);
                            // what is there: a structure (no lamp on a bridge's approach), a junction's mouth, or neither
                            bool structure = false;
                            for (float s2 = runS - 30f; s2 <= sEnd + 30f && !structure; s2 += 5f)
                                if (s2 >= 0f && s2 <= e.length && e.ElevatedAt(s2)) structure = true;
                            bool node = runS < 30f || e.length - sEnd < 30f;
                            // the lamp stations round it, and what became of them
                            var sts = new List<string>();
                            if (lampStations.TryGetValue(ei, out var ls))
                                foreach (var (sq, w) in ls)
                                {
                                    if (sq == -1f) sts.Add("none: the edge is shorter than its junctions' fans leave room for");
                                    else if (sq == -2f) sts.Add("none: no span of it drawn");
                                    else if (sq >= runS - 20f && sq <= sEnd + 20f)
                                        sts.Add($"{sq:0} {(w == 0 ? "placed" : w > 0 && w < CityMeshes.LampRejectNames.Length ? CityMeshes.LampRejectNames[w] : "?")}");
                                }
                            darkRuns.Add((runM, $"{r.id} e{ei} '{e.name}' cls{e.cls} len {e.length:0} {runM:0} m from s {runS:0} ({q0.x:0},{q0.y:0}) {LatLon(q0.x, q0.y)}, " +
                                               (structure ? "by a structure" : node ? "at a junction's mouth" : "mid-block") +
                                               $"; its stations there: {(sts.Count > 0 ? string.Join(", ", sts) : "none")}"));
                            runM = 0f;
                        }
                        for (float s = 2.5f; s < e.length; s += 5f)
                        {
                            var q = e.PointAt(s);
                            if (!tileSet.Contains(TileKey(Mathf.FloorToInt(q.x / ts), Mathf.FloorToInt(q.y / ts)))) { EndRun(s); continue; }
                            float best = float.MaxValue;
                            int cx = Mathf.FloorToInt(q.x / 32f), cz = Mathf.FloorToInt(q.y / 32f);
                            for (int dz = -1; dz <= 1; dz++)
                                for (int dx = -1; dx <= 1; dx++)
                                    if (lightCells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var cell))
                                        foreach (var lp in cell) best = Mathf.Min(best, (lp - q).sqrMagnitude);
                            upNearest.Add(Mathf.Sqrt(best));
                            upRaceM += 5f;
                            upRouteDark.TryGetValue(r.id, out var rd);
                            rd.m += 5f;
                            if (best > UptownDarkM * UptownDarkM)
                            {
                                upDarkM += 5f; rd.dark += 5f;
                                if (upDarkAt.Length == 0) upDarkAt = $"{r.id} e{ei} '{e.name}' ({q.x:0},{q.y:0}) {LatLon(q.x, q.y)}";
                                if (runM <= 0f) runS = s;
                                runM += 5f;
                            }
                            else EndRun(s);
                            upRouteDark[r.id] = rd;
                        }
                        EndRun(e.length);
                    }
                }
            var upRows = new List<string>();
            foreach (var kv in upRouteDark) upRows.Add($"{kv.Key} {kv.Value.m / 1000f:0.00} km, {100f * kv.Value.dark / Mathf.Max(1f, kv.Value.m):0.0}% dark");
            upNearest.Sort();
            float UpQ(float q) => upNearest.Count == 0 ? 0f : upNearest[Mathf.Clamp(Mathf.RoundToInt(q * (upNearest.Count - 1)), 0, upNearest.Count - 1)];
            darkRuns.Sort((a, b) => b.m.CompareTo(a.m));
            // the lamp stations of uptown's race streets: how many stood their
            // post (the reviewed build's run-off rule refused nearly all of
            // Tryon's: buildings at the sidewalk left no room to step back)
            int upStations = 0, upPlaced = 0, upEdgesShort = 0;
            var upWhy = new int[CityMeshes.LampRejectNames.Length];
            foreach (var kv in lampStations)
            {
                bool shortEdge = false;
                foreach (var (sq, w) in kv.Value)
                {
                    if (sq < 0f) { shortEdge = true; continue; }
                    upStations++;
                    if (w == 0) upPlaced++;
                    else if (w > 0 && w < upWhy.Length) upWhy[w]++;
                }
                if (shortEdge) upEdgesShort++;
            }
            var upWhyRow = new List<string>();
            for (int w = 1; w < upWhy.Length; w++) if (upWhy[w] > 0) upWhyRow.Add($"{CityMeshes.LampRejectNames[w]} {upWhy[w]}");

            // a span's far pole, where its tile was audited, stands there
            int endsChecked = 0, endsMissing = 0;
            foreach (var (f, tk) in ends)
            {
                if (!tileSet.Contains(tk)) continue;
                endsChecked++;
                if (!owned.ContainsKey(FootKey(f))) { endsMissing++; Bad("a span to a pole its tile does not stand", f, ""); }
            }
            // the same poles on a second build (the first tile, from a clear cache)
            bool same = true;
            if (tiles.Count > 0 && firstPoles != null)
            {
                CityPoles.ClearCache();
                var tm = CityMeshes.Build(map, trims, buildings, tiles[0].tx, tiles[0].tz);
                var tt = CityTrees.Build(map, trims, buildings, tm, tiles[0].tx, tiles[0].tz);
                var again = tt.poles != null ? tt.poles.poles : new List<CityPoles.Pole>();
                same = again.Count == firstPoles.Count;
                for (int i = 0; same && i < again.Count; i++) same = (again[i].foot - firstPoles[i]).sqrMagnitude < 1e-8f;
                if (tt.poles != null && tt.poles.mesh != null) Object.DestroyImmediate(tt.poles.mesh);
                DiscardMeshes(tm);
            }
            // the pitch: consecutive stations of a line that both stood
            var pitch = new List<float>();
            foreach (var kv in byChain)
            {
                kv.Value.Sort((a, b) => a.k.CompareTo(b.k));
                for (int i = 1; i < kv.Value.Count; i++)
                    if (kv.Value[i].k == kv.Value[i - 1].k + 1) pitch.Add(Vector2.Distance(kv.Value[i].f, kv.Value[i - 1].f));
            }
            pitch.Sort();
            float P(float q) => pitch.Count == 0 ? 0f : pitch[Mathf.Clamp(Mathf.RoundToInt(q * (pitch.Count - 1)), 0, pitch.Count - 1)];

            Line($"    profile: {CityPoles.Decisions} stations decided in {CityPoles.DecideMs:0} ms ({CityPoles.DecideMs / Mathf.Max(1, CityPoles.Decisions):0.00} a station), " +
                 $"{CityPoles.Spans} spans judged in {CityPoles.SpanMs:0} ms (their far poles' decisions included); " +
                 $"{RoadsideOccupancy.StaticBuilds} static masks built in {RoadsideOccupancy.StaticMs:0} ms, whoever asked");
            Line($"pole audit (WP-15): {tiles.Count} tiles ({spotTiles} at the shot spots, the rest along the race routes), {tilesWith} with poles; " +
                 $"{poles} poles of {stations} stations ({refused} stood none: no free ground), {spans} spans, {wires} wires, {heads} cobra-heads; " +
                 $"{lampsDrawn} lamp posts drawn on the furniture mesh, {acorns} acorn posts ({uptownAcorns} on uptown's 3x3); {verts} furniture verts; " +
                 $"poles and wires {msSum / Mathf.Max(1, tiles.Count):0.0} ms a tile (max {msMax:0.0})");
            var whyRow = new List<string>();
            for (int b = 0; b < 12; b++) if (whyTotals[b] > 0) whyRow.Add($"{CityPoles.WhyNames[b]} {whyTotals[b]}");
            Line("    stations that stood no pole, why their own spot was refused: " + string.Join(", ", whyRow));
            Line($"    spans not strung: no next pole within two stations or over {CityPoles.MaxSpanM:0} m {spanLong}, a road under them too close {spanRoad}, the ground too close {spanGround}, a deck or building in the way {spanBlocked}; " +
                 $"span ends in audited tiles {endsChecked}, standing there {endsChecked - endsMissing}");
            Line($"    pitch along a line: p10 {P(0.1f):0.0} p50 {P(0.5f):0.0} p90 {P(0.9f):0.0} m over {pitch.Count} pairs; lowest wire over a road {worstClear:0.00} m at {worstClearAt}");
            Line($"    pole roads on these tiles {poleRoadM / 1000f:0.0} km, {darkM / 1000f:0.00} km ({(poleRoadM > 0f ? 100f * darkM / poleRoadM : 0f):0.0}%) more than 40 m from any light (not a check)");
            foreach (var kv in bad) Line($"    {kv.Key}: {kv.Value}  e.g. {badWhere[kv.Key]}");
            int badTotal = 0;
            foreach (var kv in bad) badTotal += kv.Value;
            Check(poles > 0 && spans > 0, "the city stands utility poles and strings wires (pole audit)", $"{poles} poles, {spans} spans");
            Check(badTotal == 0, "no pole on pavement, in a clear zone, under a deck, in a building, lot, lake or race run-off, on a reserved cell (sight triangle, corner spot), on a fill house or a lamp post; none on a freeway, uptown or in Myers Park; every wire 5.5 m over every road; every span's far pole standing; no SOLID lamp post in race run-off, none breaking away outside it (pole audit)", badTotal);
            Check(pitch.Count > 0 && P(0.5f) >= PolePitchLo && P(0.5f) <= PolePitchHi, $"the pitch along a line p50 {PolePitchLo:0}-{PolePitchHi:0} m (pole audit)", $"{P(0.5f):0.0} m");
            Check(worstClear >= CityPoles.WireClearM - 0.01f, $"every wire at least {CityPoles.WireClearM} m over every road (pole audit)", $"{worstClear:0.00} m");
            Check(same, "a tile stands the same poles every build (pole audit)");
            Check(multiSub == 0, "a tile's poles, wires and lamps are one mesh with one material: no draw added (pole audit)", multiSub);
            Check(heads == poles, "a cobra-head on every pole, lit at night (pole audit)", $"{heads} heads, {poles} poles");
            Check(uptownAcorns > 0, "uptown's streets are lit by acorn posts (pole audit)", uptownAcorns);
            Line($"    lamp posts on these tiles: {lampsSolid} solid, {lampsBreakaway} breaking away in race run-off ({acornsBreakaway} of them acorn posts; their colliders counted on {colliderTiles} tiles stood up as the game does)");
            Check(colliderBad == 0, "a box collider for every solid lamp post and none for a breakaway one (pole audit)", colliderBad);
            Line($"    uptown's race streets (acorn roads on the city race routes): {upRaceM / 1000f:0.00} km, {upDarkM / 1000f:0.00} km more than {UptownDarkM:0} m from any light" +
                 (upDarkAt.Length > 0 ? $", first at {upDarkAt}" : "") + "; " + string.Join("; ", upRows) +
                 $"; the nearest light p50 {UpQ(0.5f):0.0} p90 {UpQ(0.9f):0.0} max {UpQ(1f):0.0} m");
            for (int i = 0; i < Mathf.Min(10, darkRuns.Count); i++) Line("      dark: " + darkRuns[i].what);
            Line($"    (not a check: the 10 m either side of a junction are the lamps' fan clearance, older than WP-15; the long runs are named above)");
            Check(upStations > 0 && upPlaced >= UptownStationShareMin * upStations,
                  $"uptown's race streets stand their acorn posts: at least {UptownStationShareMin * 100f:0}% of their lamp stations (pole audit, the WP-15 review: the run-off rule had refused Tryon's)",
                  $"{upPlaced} of {upStations} ({100f * upPlaced / Mathf.Max(1, upStations):0.0}%); refused: {(upWhyRow.Count > 0 ? string.Join(", ", upWhyRow) : "none")}; {upEdgesShort} edges too short between their junctions' fans for one");
        }
    }
}
