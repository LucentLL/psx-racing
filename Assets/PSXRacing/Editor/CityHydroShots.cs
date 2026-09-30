using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WATER SHOTS (WP-25, 2026-09-30): the creeks, a culvert and a pond,
    /// before and after, from cameras that depend only on the city data and
    /// the ground - never on what WP-25 draws - so one label's shots and the
    /// next line up frame for frame.
    ///
    /// Per creek (W Trade St over Irwin Creek, State St over Stewart Creek,
    /// Archdale Dr over Little Sugar Creek; the crossing found in the data as
    /// CityCreekShots does):
    ///   *_deck    from the bridge's downstream edge, looking down the creek;
    ///   *_bank    from the bank 45 m downstream, looking back at the bridge;
    ///   *_raised  35 m over the water 120 m downstream, looking at the bridge.
    /// One culvert (CityCulverts: a ravine under a named street near Queens
    /// Road, the deepest fill of those with both ends):
    ///   culvert_channel  from the channel 16 m out, at the wall;
    ///   culvert_road     from the road's edge over it, down at the wall.
    /// One pond (a county pond of 0.56 ha 31 m off Tyvola Road, which the
    /// data before WP-25 does not carry):
    ///   pond_tyvola      from the road, at the pond.
    ///
    /// Headless: -executeMethod PSXRacing.EditorTools.CityHydroShots.Run
    /// (tools\city-hydro-shots.ps1 -Label before|after). PNGs and
    /// hydro_shots.txt to Screenshots/City/hydro/&lt;label&gt;.
    /// </summary>
    public static class CityHydroShots
    {
        struct Creek { public string id, road, water; public double lat, lon; }

        static readonly Creek[] Creeks =
        {
            new Creek { id = "irwin_trade", road = "Trade", water = "Irwin", lat = 35.2345, lon = -80.8560 },
            new Creek { id = "stewart_state", road = "State Street", water = "Stewart", lat = 35.2395, lon = -80.8665 },
            new Creek { id = "littlesugar_archdale", road = "Archdale", water = "Little Sugar", lat = 35.1500, lon = -80.8500 },
        };
        const double CulvertLat = 35.1928, CulvertLon = -80.8368;
        const double PondLat = 35.15615, PondLon = -80.85092;
        const float FarM = 500f;

        public static void Run()
        {
            string label = System.Environment.GetEnvironmentVariable("PSX_HYDRO_LABEL");
            if (string.IsNullOrEmpty(label)) label = "now";
            // "before": the tiles without WP-25's culverts and banks (the
            // data is whatever Resources holds)
            CityMeshes.HydroOff = label.StartsWith("before");
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityHydroShots] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "hydro", label);
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);

            PSXRacingBuilder.EnsureCityTextures();
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", FarM);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var go = new GameObject("~cityHydroShots");
            var world = go.AddComponent<CityWorld>();
            world.materials = PSXRacingBuilder.CityMaterials();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var log = new StringBuilder();
            log.Append("shot\teye_x\teye_y\teye_z\tlook_x\tlook_y\tlook_z\twhat\n");
            int shots = 0;
            void Take(string name, Vector3 eye, Vector3 look, float fov, string what)
            {
                world.EnsureRing(eye, 1);
                world.EnsureRing(look, 1);
                CityCreekShots.Shoot(dir, name, eye, Quaternion.LookRotation(look - eye), fov, FarM);
                shots++;
                log.Append(string.Format(inv, "{0}\t{1:0.0}\t{2:0.00}\t{3:0.0}\t{4:0.0}\t{5:0.00}\t{6:0.0}\t{7}\n", name, eye.x, eye.y, eye.z, look.x, look.y, look.z, what));
                Debug.Log($"[CityHydroShots] {name}: {what}");
                world.DropAll();
            }
            try
            {
                // ---- the creeks
                foreach (var c in Creeks)
                {
                    var hint = CityCreekShots.LL(c.lat, c.lon);
                    var hits = CityCreekShots.Crossings(map, c.road, c.water);
                    CityCreekShots.Hit hit = default; bool found = false; float best = 2500f;
                    foreach (var h in hits)
                    {
                        if (h.water == null || h.water.ravine) continue;
                        float d = Vector2.Distance(h.at, hint);
                        if (d < best) { best = d; hit = h; found = true; }
                    }
                    if (!found) { Debug.LogWarning($"[CityHydroShots] {c.id}: no crossing of '{c.road}' and '{c.water}'"); continue; }
                    var w = hit.water; var e = hit.edge;
                    // downstream: the way the bed falls
                    float bUp = CityElevation.BedYAt(w, Mathf.Max(0f, hit.ws - 80f)), bDn = CityElevation.BedYAt(w, Mathf.Min(w.length, hit.ws + 80f));
                    int ds = bDn <= bUp ? 1 : -1;
                    float Surf(float s) { var p = CityCulverts.PointAt(w, s); return CityElevation.CreekSurfaceY(w, s, p); }
                    Vector2 At(float s) => CityCulverts.PointAt(w, Mathf.Clamp(s, 0f, w.length));
                    float roadY = e.YAt(hit.s);
                    Vector2 tan = CityCulverts.TangentAt(w, hit.ws) * ds;
                    Vector2 rt = e.TangentAt(hit.s); var side = new Vector2(-rt.y, rt.x);
                    if (Vector2.Dot(side, tan) < 0f) side = -side;
                    // on the deck's downstream edge, down the creek
                    var deckEye = hit.at + side * (e.width * 0.5f + 0.3f);
                    var t1 = At(hit.ws + ds * 40f);
                    Take(c.id + "_deck", new Vector3(deckEye.x, roadY + 1.6f, deckEye.y), new Vector3(t1.x, Surf(hit.ws + ds * 40f), t1.y), 62f,
                         $"from '{e.name}' over '{w.name}', down the creek (road {roadY:0.0}, water {Surf(hit.ws):0.0})");
                    // over the water 40 m downstream, back at the bridge
                    var b0 = At(hit.ws + ds * 40f);
                    float bankY = Mathf.Max(CityElevation.GroundY(map, b0.x, b0.y) + 1.5f, Surf(hit.ws + ds * 40f) + 2.5f);
                    Take(c.id + "_bank", new Vector3(b0.x, bankY, b0.y), new Vector3(hit.at.x, Surf(hit.ws) + 1.5f, hit.at.y), 62f,
                         $"2.5 m over the water 40 m downstream, back at '{e.name}'");
                    // raised, 120 m downstream
                    var r0 = At(hit.ws + ds * 120f);
                    var rt2 = CityCulverts.TangentAt(w, Mathf.Clamp(hit.ws + ds * 120f, 0f, w.length));
                    var raised = r0 + new Vector2(-rt2.y, rt2.x) * 40f;
                    Take(c.id + "_raised", new Vector3(raised.x, Surf(hit.ws + ds * 120f) + 35f, raised.y), new Vector3(hit.at.x, Surf(hit.ws), hit.at.y), 55f,
                         $"35 m over '{w.name}' 120 m downstream, at '{e.name}'");
                }

                // ---- the culvert
                {
                    var hint = CityCreekShots.LL(CulvertLat, CulvertLon);
                    var list = new List<CityCulverts.Culvert>();
                    CityCulverts.Near(map, null, hint - Vector2.one * 2500f, hint + Vector2.one * 2500f, list);
                    CityCulverts.Culvert pick = default; bool got = false; float bestD = float.MaxValue;
                    int both = 0;
                    var ranked = new List<(float score, CityCulverts.Culvert c, CityCulverts.End end)>();
                    // a named street on a fill of 2.5-6 m, its nearer end in open
                    // ground (no building but houses within 20 m) and as close
                    // to the road as any: the one a driver could see
                    foreach (var cv in list)
                    {
                        if (cv.skip != null || (!cv.hasLo && !cv.hasHi)) continue;
                        if (cv.hasLo && cv.hasHi) both++;
                        var e = map.edges[cv.edge];
                        if (e.link || string.IsNullOrEmpty(e.name)) continue;
                        float fillM = cv.roadY - cv.floorY;
                        if (fillM < 2.5f || fillM > 6f) continue;
                        var near = !cv.hasHi || (cv.hasLo && cv.lo.pastPave <= cv.hi.pastPave) ? cv.lo : cv.hi;
                        if (map.AnyFootprintNear(near.at, 30f, nonHouseOnly: true)) continue;
                        float d = near.pastPave + Vector2.Distance(cv.at, hint) * 0.002f;
                        ranked.Add((d, cv, near));
                        if (d < bestD) { bestD = d; pick = cv; got = true; }
                    }
                    ranked.Sort((a, b) => a.score.CompareTo(b.score));
                    Debug.Log($"[CityHydroShots] culverts within 2.5 km of the hint: {list.Count} crossings, {both} with both ends");
                    if (got)
                    {
                        var e = map.edges[pick.edge];
                        var end = !pick.hasHi || (pick.hasLo && pick.lo.pastPave <= pick.hi.pastPave) ? pick.lo : pick.hi;
                        var o = end.outward;
                        var eyeP = end.at + o * 16f;
                        float eyeY = Mathf.Max(CityElevation.GroundY(map, eyeP.x, eyeP.y), end.groundY) + 1.7f;
                        var wall = new Vector3(end.at.x, end.groundY + end.wallH * 0.5f, end.at.y);
                        Take("culvert_channel", new Vector3(eyeP.x, eyeY, eyeP.y), wall, 60f,
                             $"ravine under '{e.name}' (e{pick.edge}, water {pick.water} s={pick.ws:0}), fill {pick.roadY - pick.floorY:0.0} m; the end {end.pastPave:0.0} m past the pavement, pipe {end.pipeD:0.0} m");
                        // three-quarter, 8 m out and 8 m aside, 3.5 m up
                        var r2 = new Vector2(o.y, -o.x);
                        var sideP = end.at + o * 8f + r2 * 8f;
                        float sideY = Mathf.Max(CityElevation.GroundY(map, sideP.x, sideP.y), end.groundY) + 3.5f;
                        Take("culvert_side", new Vector3(sideP.x, sideY, sideP.y), wall, 60f, "the culvert's end three-quarter on, 8 m out and 8 m aside");
                        var rt = e.TangentAt(pick.es); var side = new Vector2(-rt.y, rt.x);
                        if (Vector2.Dot(side, end.at - pick.at) < 0f) side = -side;
                        var roadEye = pick.at + side * (e.width * 0.5f + 0.5f);
                        Take("culvert_road", new Vector3(roadEye.x, pick.roadY + 2f, roadEye.y), new Vector3(end.at.x, end.groundY + 0.6f, end.at.y), 60f,
                             $"from '{e.name}''s edge, down at the culvert's end");
                    }
                    else Debug.LogWarning("[CityHydroShots] no culvert end near the hint");
                    // four more, three-quarter on: the next best
                    for (int k = 1; k < ranked.Count && k <= 4; k++)
                    {
                        var (_, cv, end) = ranked[k];
                        Vector2 o = end.outward, r2 = new Vector2(o.y, -o.x);
                        var sideP = end.at + o * 8f + r2 * 8f;
                        float sideY = Mathf.Max(CityElevation.GroundY(map, sideP.x, sideP.y), end.groundY) + 3.5f;
                        Take("culvert_more_" + k, new Vector3(sideP.x, sideY, sideP.y), new Vector3(end.at.x, end.groundY + end.wallH * 0.5f, end.at.y), 60f,
                             $"under '{map.edges[cv.edge].name}' (e{cv.edge}), fill {cv.roadY - cv.floorY:0.0} m, {end.pastPave:0.0} m past the pavement, pipe {end.pipeD:0.0} m");
                    }
                }

                // ---- the pond
                {
                    var pond = CityCreekShots.LL(PondLat, PondLon);
                    if (map.NearestRoadPoint(pond, 120f, true, out int pe, out float ps, out _))
                    {
                        var e = map.edges[pe];
                        var rp = e.PointAt(ps);
                        var toward = (pond - rp).normalized;
                        var eyeP = pond - toward * 60f;
                        float gy = CityElevation.GroundY(map, pond.x, pond.y);
                        Take("pond_tyvola", new Vector3(eyeP.x, gy + 25f, eyeP.y), new Vector3(pond.x, gy, pond.y), 62f,
                             $"from '{e.name}' at the pond {Vector2.Distance(rp, pond):0} m off (ground at the pond {gy:0.0})");
                    }
                }
            }
            finally
            {
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, "hydro_shots.txt"), log.ToString());
            Debug.Log($"[CityHydroShots] {shots} shots to {dir}");
        }
    }
}
