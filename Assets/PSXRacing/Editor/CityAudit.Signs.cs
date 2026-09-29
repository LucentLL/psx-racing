using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    public static partial class CityAudit
    {
        // ------------------------------------------------------------------
        //  SIGN AUDIT (plan WP-23). The billboards, business pole signs and
        //  exit gantries are placed from the tile's RoadsideOccupancy mask;
        //  this checks what was placed against the geometry, measured again:
        //    * no post or leg on pavement, in a clear zone or under a deck,
        //      in a building or water, in race run-off, or on a cell the mask
        //      reserves (sight triangles, corner spots, lots, lamp feet);
        //    * nothing over pavement but a gantry's panels, and those at
        //      least 5.5 m over the highest road under them;
        //    * every face turned to its traffic (dot 0.9 or better to a driver
        //      150 m up the road, who is on a carriageway coming toward it);
        //    * every sign in the tile that placed it, none twice across a
        //      seam, the same signs on a second build;
        //    * the billboards per km of every route against NCDOT's figure,
        //      per class within +-20%, over every tile the routes run through;
        //    * one draw a tile; the billboards lit; posts solid, cabinets
        //      breakaway (Q15).
        // ------------------------------------------------------------------

        const float SignDensityTol = 0.2f;
        const float FaceDotMin = 0.9f;
        const float GantryClearMin = 5.5f;

        /// <summary>The sign audit alone: writes city_sign_audit.txt at the
        /// project root. Headless: -executeMethod PSXRacing.EditorTools.CityAudit.RunSigns</summary>
        [UnityEditor.MenuItem("PSX Racing/Audit City Signs")]
        public static void RunSigns()
        {
            outLog = new System.Text.StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); return; }
            SignAudit(map, CityMeshes.NodeTrims(map), CityBuildings.Precompute(map));
            outLog.AppendLine(failures == 0 ? "SIGN AUDIT OK" : $"SIGN AUDIT: {failures} FAILURES");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "city_sign_audit.txt"), outLog.ToString());
        }

        static void SignAudit(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            Line("sign audit (WP-23): data " + (CitySignData.Loaded ? $"{CitySignData.Routes.Length} routes, {CitySignData.Boards.Length} OSM billboards, {CitySignData.Pois.Length} businesses" : "MISSING"));
            Check(CitySignData.Loaded, "the sign data (charlotte_signs.bytes) loads");
            PSXRacingBuilder.EnsureCityTextures();
            var kit = CityKit.Get();
            var sm = kit != null ? kit.signs : null;
            Check(sm != null && sm.mainTexture != null && sm.HasProperty("_NightFace") && sm.GetFloat("_NightFace") > 0.5f
                  && sm.GetFloat("_NightWin") > 0.5f && sm.GetTexture("_NightMask") != null,
                  "the city kit's sign material wears the atlas and lights its faces at night (_NightMask, _NightWin, _NightFace)");
            if (!CitySignData.Loaded) return;
            bool keepTrees = CityTrees.Enabled;
            CityTrees.Enabled = false;   // the signs only: the mask is the same, the trees come after them
            try { SignAuditInner(map, trims, buildings); }
            finally { CityTrees.Enabled = keepTrees; }
        }

        static void SignAuditInner(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            float ts = CityMeshes.TileSize;
            var tiles = new List<(int tx, int tz, string why)>();
            var seen = new HashSet<long>();
            void Add(Vector2 at, string why)
            {
                int tx = Mathf.FloorToInt(at.x / ts), tz = Mathf.FloorToInt(at.y / ts);
                if (seen.Add(TileKey(tx, tz))) tiles.Add((tx, tz, why));
            }
            // the plan's shot spots (I-77, I-277, South Blvd) and the strip, with neighbours
            foreach (var s in SignSpots)
            {
                var c = LLtoGame(s.lat, s.lon);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++) Add(c + new Vector2(dx, dz) * ts, s.id);
            }
            int spotTiles = tiles.Count;
            // every tile a route runs through, 45 m either side (a post stands
            // at most that far off its carriageway): the density's truth
            var routeKm = new double[CitySignData.Routes.Length];
            foreach (var e in map.edges)
            {
                if (e.link || e.tunnel) continue;
                int ri = CitySignData.RouteOf(e.wayId);
                if (ri < 0) continue;
                routeKm[ri] += e.length / 1000.0 * (e.oneway ? 0.5 : 1.0);
                for (float s = 0f; s <= e.length + 1f; s += 32f)
                {
                    float sc = Mathf.Min(s, e.length);
                    var p = e.PointAt(sc);
                    var t = e.TangentAt(sc);
                    var n = new Vector2(t.y, -t.x);
                    Add(p, "route"); Add(p + n * 45f, "route"); Add(p - n * 45f, "route");
                }
            }
            // and every city race route's tiles (their run-off)
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
            var perRoute = new int[CitySignData.Routes.Length];
            int bulletins = 0, posters = 0, poles = 0, gantries = 0, osm = 0, posts = 0, lamps = 0, faces = 0, cantilevers = 0;
            int refusedB = 0, refusedP = 0, refusedG = 0, multiSub = 0, outside = 0, runOff = 0;
            int tilesWith = 0, faceBad = 0, lampBad = 0;
            float worstDot = 1f, worstClear = float.MaxValue, msSum = 0f, msMax = 0f;
            string worstDotAt = "", worstClearAt = "";
            bool same = true;
            var all = new Dictionary<long, (CitySigns.Kind kind, Vector2 p)>();
            int twice = 0;

            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var (tx, tz, why) = tiles[ti];
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                var tt = CityTrees.Build(map, trims, buildings, tm, tx, tz);
                var st = tt.signs;
                if (st == null) { DiscardMeshes(tm); continue; }
                msSum += st.ms; msMax = Mathf.Max(msMax, st.ms);
                // the mask as it stood before the signs took their ground
                var fresh = RoadsideOccupancy.Build(map, trims, buildings, tm, tx, tz);
                var again = CityTrees.Build(map, trims, buildings, tm, tx, tz).signs;
                if (again == null || again.signs.Count != st.signs.Count) same = false;
                else for (int i = 0; i < st.signs.Count && same; i++)
                        if ((again.signs[i].pos - st.signs[i].pos).sqrMagnitude > 1e-6f || again.signs[i].kind != st.signs[i].kind) same = false;
                if (again != null && again.mesh != null) Object.DestroyImmediate(again.mesh);

                if (st.signs.Count > 0) tilesWith++;
                if (st.mesh != null && st.mesh.subMeshCount != 1) multiSub++;
                bulletins += st.bulletins; posters += st.posters; poles += st.poleSigns; gantries += st.gantries; osm += st.osm;
                posts += st.posts.Count; lamps += st.lamps.Count; faces += st.faces.Count;
                refusedB += st.refusedBoards; refusedP += st.refusedPoleSigns; refusedG += st.refusedGantries;
                var min = new Vector2(tx * ts, tz * ts);

                foreach (var sg in st.signs)
                {
                    // one owner
                    if (sg.owner.x < min.x || sg.owner.y < min.y || sg.owner.x >= min.x + ts || sg.owner.y >= min.y + ts) { outside++; Bad("owned by another tile", sg.pos, sg.kind.ToString()); }
                    long key = ((long)Mathf.RoundToInt(sg.pos.x * 2f) << 32) ^ (uint)Mathf.RoundToInt(sg.pos.y * 2f);
                    if (all.TryGetValue(key, out var prev) && prev.kind == sg.kind) { twice++; Bad("placed twice (a seam)", sg.pos, sg.kind.ToString()); }
                    else all[key] = (sg.kind, sg.pos);
                    if (sg.kind == CitySigns.Kind.Bulletin || sg.kind == CitySigns.Kind.Poster) { if (sg.route >= 0) perRoute[sg.route]++; }

                    // posts and legs, against the geometry and the mask before the signs
                    var feet = new List<Vector2>();
                    if (sg.kind == CitySigns.Kind.Gantry) { feet.Add(sg.legB); if ((sg.legA - sg.legB).sqrMagnitude > 0.01f) feet.Add(sg.legA); else cantilevers++; }
                    else feet.Add(sg.pos);
                    foreach (var f in feet)
                    {
                        float w = CitySigns.WorstRoad(map, trims, f, out int we, out string what);
                        if (w < -0.05f) Bad("a post " + what, f, $"{sg.kind} e{we} '{(we >= 0 ? map.edges[we].name : "")}' {w:0.00} m inside");
                        bool inTile = f.x >= min.x && f.y >= min.y && f.x < min.x + ts && f.y < min.y + ts;
                        if (inTile)
                        {
                            byte b = fresh.At(f);
                            if (b != 0) Bad("a post on a reserved cell of the mask", f, $"{sg.kind} bits {b} ({string.Join(", ", BitList(b))})");
                        }
                        if (!map.FootprintClear(f, 0.6f)) Bad("a post in a real building", f, sg.kind.ToString());
                        if (map.InLake(f)) Bad("a post in a lake", f, sg.kind.ToString());
                        if (RaceRunOff.Inside(map, trims, f)) { runOff++; Bad("a post in race run-off", f, sg.kind.ToString()); }
                    }

                    for (int i = sg.firstFace; i < sg.firstFace + sg.faces; i++)
                    {
                        var fc = st.faces[i];
                        // turned to its traffic
                        var to = fc.viewer - fc.centre;
                        float dot = Vector3.Dot(fc.normal, to.normalized);
                        if (dot < worstDot) { worstDot = dot; worstDotAt = $"{fc.kind} ({fc.centre.x:0},{fc.centre.z:0}) {LatLon(fc.centre.x, fc.centre.z)}"; }
                        if (dot < FaceDotMin) { faceBad++; Bad("a face not turned to its traffic", P2(fc.centre), $"{fc.kind} dot {dot:0.000}"); }
                        var vw = new Vector2(fc.viewer.x, fc.viewer.z);
                        var se = map.edges[sg.edge];
                        string of = $"{fc.kind} of e{sg.edge} '{se.name}' cls{se.cls}{(se.oneway ? " one-way" : "")} at ({sg.pos.x:0.0},{sg.pos.y:0.0}), face {i - sg.firstFace}";
                        switch (CitySigns.DriverOn(map, trims, vw, P2(fc.centre), out string vwhat))
                        {
                            case 0: Bad("a face's driver is not on a road", vw, of); break;
                            case 1: Bad("a face's driver is driving away from it", vw, $"{of}; at the driver only {vwhat}"); break;
                        }
                        // footprint
                        var n2 = new Vector2(fc.normal.x, fc.normal.z).normalized;
                        var u = new Vector2(-n2.y, n2.x);
                        for (int k = 0; k <= 8; k++)
                        {
                            var q = P2(fc.centre) + u * ((k / 8f - 0.5f) * fc.w);
                            float top = CitySigns.RoadTopAt(map, trims, q, 0f);
                            if (fc.kind == CitySigns.Kind.Gantry)
                            {
                                if (float.IsNegativeInfinity(top)) continue;
                                float clear = fc.centre.y - fc.h * 0.5f - top;
                                if (clear < worstClear) { worstClear = clear; worstClearAt = $"({q.x:0},{q.y:0}) {LatLon(q.x, q.y)}"; }
                                if (clear < GantryClearMin) Bad("a gantry panel under 5.5 m over a road", q, $"{clear:0.00} m");
                            }
                            else if (!float.IsNegativeInfinity(top)) Bad("a sign face over pavement", q, fc.kind.ToString());
                        }
                    }
                    if ((sg.kind == CitySigns.Kind.Bulletin || sg.kind == CitySigns.Kind.Poster) && !sg.lit) lampBad++;
                }
                if (st.mesh != null) Object.DestroyImmediate(st.mesh);
                if (tt.mesh != null) Object.DestroyImmediate(tt.mesh);
                DiscardMeshes(tm);
            }

            // billboards counted by route: the OSM ones on their nearest route
            Line($"    {tiles.Count} tiles ({spotTiles} at the shot spots, the rest along the routes and the race routes), {tilesWith} with signs; " +
                 $"{bulletins} bulletins, {posters} posters ({osm} of them OSM's), {poles} business pole signs, {gantries} exit gantries ({cantilevers} cantilevers); " +
                 $"{posts} solid posts and legs, {lamps} floodlights, {faces} faces; place {msSum / Mathf.Max(1, tiles.Count):0.0} ms a tile (max {msMax:0.0})");
            Line($"    candidates that won their spacing but found no free ground: billboards {refusedB}, pole signs {refusedP}, gantries {refusedG}");
            foreach (var kv in bad) Line($"    {kv.Key}: {kv.Value}  e.g. {badWhere[kv.Key]}");
            int badTotal = 0;
            foreach (var kv in bad) if (!kv.Key.StartsWith("a face not turned")) badTotal += kv.Value;
            Check(bulletins + posters > 0 && poles > 0 && gantries > 0, "the city stands billboards, business pole signs and exit gantries (sign audit)", $"{bulletins + posters}, {poles}, {gantries}");
            Check(badTotal == 0, "no post on pavement, in a clear zone, under a deck, in a building, lake or run-off, or on a reserved cell; nothing over pavement but a gantry's panels, 5.5 m up (sign audit)", badTotal);
            Check(faceBad == 0, $"every face turned to its traffic: dot {FaceDotMin} or better to a driver {CitySigns.ViewAheadM:0} m up the road ({CitySigns.PoleViewM:0} m for a business's cabinet) (sign audit)", $"{faceBad} faces below; worst {worstDot:0.000} at {worstDotAt}");
            Line($"    gantry panels: the lowest {worstClear:0.00} m over the road under it, at {worstClearAt}");
            Check(outside == 0 && twice == 0, "every sign in the tile that placed it, none placed twice across a seam (sign audit)", $"{outside} outside, {twice} twice");
            Check(same, "a tile places the same signs every build (sign audit)");
            Check(multiSub == 0, "a tile's signs are one mesh with one material: at most +1 draw (sign audit)", multiSub);
            Check(lampBad == 0 && lamps >= 2 * bulletins + posters, "every billboard lit: two floodlights under a bulletin face, one under a poster (sign audit)", $"{lamps} lamps for {bulletins} bulletins, {posters} posters");
            Check(posts == 0 || posts >= bulletins + posters, "billboard monopoles and gantry legs are solid; pole-sign cabinets break away (Q15) (sign audit)", $"{posts} posts");

            // density per route, and per class
            double[] clsKm = new double[3], clsWant = new double[3], clsGot = new double[3];
            var rows = new List<string>();
            for (int r = 0; r < CitySignData.Routes.Length; r++)
            {
                var rt = CitySignData.Routes[r];
                if (routeKm[r] < 3.0) continue;
                double want = rt.density * routeKm[r];
                clsKm[rt.cls] += routeKm[r]; clsWant[rt.cls] += want; clsGot[rt.cls] += perRoute[r];
                rows.Add($"{rt.name} {perRoute[r]}/{want:0} ({perRoute[r] / routeKm[r]:0.00} per km of {routeKm[r]:0} km, NCDOT {rt.density:0.00})");
            }
            Line("    billboards per route (placed / NCDOT's figure): " + string.Join("; ", rows));
            for (int c = 1; c <= 2; c++)
            {
                if (clsKm[c] < 1.0) continue;
                double ratio = clsWant[c] > 0 ? clsGot[c] / clsWant[c] : 1.0;
                Check(System.Math.Abs(ratio - 1.0) <= SignDensityTol,
                      $"billboard density on {(c == 1 ? "interstates" : "US and NC routes")} within +-{SignDensityTol * 100:0}% of NCDOT's (sign audit)",
                      $"{clsGot[c]:0} placed for {clsWant[c]:0} ({clsGot[c] / clsKm[c]:0.00} per km against {clsWant[c] / clsKm[c]:0.00}; {clsKm[c]:0} km)");
            }
            double mwKm = 0; foreach (var e in map.edges) if (!e.link && e.cls >= 5 && !e.tunnel) mwKm += e.length / 1000.0 * (e.oneway ? 0.5 : 1.0);
            Line($"    exit gantries {gantries} over {mwKm:0} km of motorway in the audited tiles' reach (NCDOT counts 1.01 per km; one per exit here; {refusedG} exits found no legs)");
        }

        static Vector2 P2(Vector3 p) => new Vector2(p.x, p.z);

        static IEnumerable<string> BitList(byte b)
        {
            for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) yield return RoadsideOccupancy.BitNames[i];
        }

        /// <summary>Where the sign shots look (plan: I-77, I-277, South Blvd),
        /// and the Independence strip.</summary>
        static readonly (string id, double lat, double lon)[] SignSpots =
        {
            ("i-77 north", 35.2600, -80.8350), ("i-277 uptown", 35.2195, -80.8500), ("south blvd", 35.1930, -80.8680),
            ("independence strip", 35.16804, -80.74309), ("i-85 sugar creek", 35.2650, -80.7780),
        };
    }
}
