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
            finally { CityTrees.Enabled = treesWere; CitySigns.Enabled = signsWere; CityPoles.Enabled = polesWere; }
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

            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var (tx, tz, why) = tiles[ti];
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                var fresh = RoadsideOccupancy.Build(map, trims, buildings, tm, tx, tz);
                var tt = CityTrees.Build(map, trims, buildings, tm, tx, tz);
                var pt = tt.poles;
                var min = new Vector2(tx * ts, tz * ts);
                var max = min + Vector2.one * ts;
                foreach (var l in tm.lamps)
                {
                    if (l.kind == CityMeshes.LampAcorn) { acorns++; if (why == "uptown") uptownAcorns++; }
                    // the lamp posts too keep out of a race route's run-off (WP-15)
                    var lf = tm.origin + l.foot;
                    if (RaceRunOff.Inside(map, trims, new Vector2(lf.x, lf.z))) Bad("a lamp post in race run-off", new Vector2(lf.x, lf.z), $"kind {l.kind}");
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
            Check(badTotal == 0, "no pole on pavement, in a clear zone, under a deck, in a building, lot, lake or race run-off, on a reserved cell (sight triangle, corner spot), on a fill house or a lamp post; none on a freeway, uptown or in Myers Park; every wire 5.5 m over every road; every span's far pole standing; no lamp post in race run-off (pole audit)", badTotal);
            Check(pitch.Count > 0 && P(0.5f) >= PolePitchLo && P(0.5f) <= PolePitchHi, $"the pitch along a line p50 {PolePitchLo:0}-{PolePitchHi:0} m (pole audit)", $"{P(0.5f):0.0} m");
            Check(worstClear >= CityPoles.WireClearM - 0.01f, $"every wire at least {CityPoles.WireClearM} m over every road (pole audit)", $"{worstClear:0.00} m");
            Check(same, "a tile stands the same poles every build (pole audit)");
            Check(multiSub == 0, "a tile's poles, wires and lamps are one mesh with one material: no draw added (pole audit)", multiSub);
            Check(heads == poles, "a cobra-head on every pole, lit at night (pole audit)", $"{heads} heads, {poles} poles");
            Check(uptownAcorns > 0, "uptown's streets are lit by acorn posts (pole audit)", uptownAcorns);
        }
    }
}
