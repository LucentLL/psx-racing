using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S MESH TREES, PROVED (ctrees round 2, 2026-11-11): three
    /// pictures from one build at each pose - the CARDS (F8's A/B, in the
    /// dress of the row), ROUND 1's fit (CityMeshTrees.Round1Mode: no street
    /// forms, the old collider skip and capsule) and ROUND 2's. Poses stand
    /// where the trees are: an Uptown block with its grate trees in view, the
    /// owner's N Graham pose (graham_b), Dilworth, a south subdivision, a real
    /// I-277 verge, a planted median row; each from the driver's eye and from
    /// 150 m back along the road. Noon; dusk and night for Uptown and Graham;
    /// fall and winter for Dilworth.
    /// Then: the DROP SURVEY on round 1's own six rings (150 tiles), both
    /// fits; the CITY FOLIAGE AUDIT over every round-2 tile (LOD0 vertices
    /// against roads, buildings, poles, signals and colliders - gate 0); the
    /// COST at each eye (frame cards v mesh, triangles drawn, the ring's
    /// triangles in every direction per FOREST DETAIL); the POP measure (the
    /// lag pair: the same view with the forest's levels from 6 m back) along
    /// two roads; the IMPOSTOR pair (one view, mesh v impostor).
    ///
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunMeshTrees
    ///   PSX_CT_ONLY=place,place  PSX_CT_NOSHOTS=1  PSX_CT_SURVEY=0  PSX_CT_POP=0  PSX_CT_REKIT=1
    /// Output: Screenshots/ctrees2/&lt;place&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;dress&gt;_&lt;cards|r1|r2&gt;.png, ctrees2_log.txt
    /// </summary>
    public static partial class CityRefSpots
    {
        enum CtKind { Fixed, Street, Verge, Median, Any }
        struct CtPlace { public string id, what; public string[] roads; public double lat, lon; public float hdg; public bool hours, seasons, pop; public CtKind kind; }

        static readonly CtPlace[] CtPlaces =
        {
            new CtPlace { id = "uptown", lat = 35.22745, lon = -80.84275, roads = new[] { "North Tryon Street", "South Tryon Street" }, hdg = 50f, hours = true, kind = CtKind.Street, what = "Tryon St, Uptown: a block of grate street trees" },
            new CtPlace { id = "graham", lat = 35.23456, lon = -80.84152, roads = new[] { "Graham" }, hdg = 229f, hours = true, kind = CtKind.Fixed, what = "N Graham St SW past W 10th (the owner's graham_b)" },
            new CtPlace { id = "dilworth", lat = 35.20225, lon = -80.84758, roads = new[] { "Dilworth" }, hdg = 192f, seasons = true, pop = true, kind = CtKind.Fixed, what = "Dilworth Rd E: old-city willow oaks, yards" },
            new CtPlace { id = "subdivision", lat = 35.0600, lon = -80.8350, roads = new string[0], hdg = 0f, pop = true, kind = CtKind.Any, what = "a south Charlotte subdivision street (reals.json subdivision)" },
            new CtPlace { id = "verge", lat = 35.2165, lon = -80.8475, roads = new[] { "John Belk Freeway", "I-277", "Brookshire Freeway" }, hdg = float.NaN, kind = CtKind.Verge, what = "I-277 (John Belk Fwy): freeway verge trees" },
            new CtPlace { id = "median", lat = 35.0560, lon = -80.8440, roads = new[] { "Johnston Road", "W T Harris" }, hdg = float.NaN, kind = CtKind.Median, what = "a planted median row (Johnston Rd / W.T. Harris Blvd)" },
        };

        /// <summary>Round 1's six rings exactly as it snapped them (its drop numbers' ground).</summary>
        static readonly (string id, double lat, double lon, string road)[] CtSurvey =
        {
            ("graham", 35.23456, -80.84152, "Graham"), ("uptown", 35.22745, -80.84275, "Tryon"), ("dilworth", 35.20225, -80.84758, "Dilworth"),
            ("south_sub", 35.09500, -80.80200, ""), ("fwy_verge", 35.16100, -80.87900, "I-77"), ("median", 35.13864, -80.78400, "Providence"),
        };

        public static void RunMeshTrees()
        {
            var log = new StringBuilder("CITY MESH TREES ROUND 2 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "ctrees2");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                var kitAsset = PSXRacingBuilder.EnsureCityTreeKit(System.Environment.GetEnvironmentVariable("PSX_CT_REKIT") == "1");
                if (kitAsset == null) throw new System.Exception("no city tree kit (store files?)");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                CityMeshTrees.ResetCounts();
                CityMeshTrees.Round1Mode = false;
                string lo = System.Environment.GetEnvironmentVariable("PSX_CT_LOD");
                if (!string.IsNullOrEmpty(lo))
                {
                    var p3 = lo.Split(',');
                    CityMeshTrees.LodOverride = new Vector3(float.Parse(p3[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(p3[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p3[2], System.Globalization.CultureInfo.InvariantCulture));
                }
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                var kit = mt.Kit;
                log.Append($"kit: {kit.variants.Length} species; LOD {mt.Forest.lod0To:0}/{mt.Forest.lod1To:0}/{mt.Forest.meshRange:0} m, impostors to {mt.Forest.farRange:0} m (FOREST DETAIL {ForestDetailPrefs.Label})\n");
                for (int v = 0; v < kit.variants.Length; v++)
                    log.Append($"  {v} {kit.names[v]} [{kit.roles[v]}] {kit.variants[v].height:0.0} m crown from {kit.crownBase[v]:0.00}, widest r {MaxR(kit, v):0.0} m, tris {LT(kit, v, 0)}/{LT(kit, v, 1)}/{LT(kit, v, 2)}\n");

                string only = System.Environment.GetEnvironmentVariable("PSX_CT_ONLY");
                bool noShots = System.Environment.GetEnvironmentVariable("PSX_CT_NOSHOTS") == "1";
                bool doPop = System.Environment.GetEnvironmentVariable("PSX_CT_POP") != "0";
                var audit = new AuditSum();

                // ---------------- THE DROP SURVEY on round 1's 150 tiles ----------------
                if (System.Environment.GetEnvironmentVariable("PSX_CT_SURVEY") != "0")
                {
                    log.Append("\nDROP SURVEY (round 1's six rings, 5 x 5 tiles each): round 1's fit | round 2's\n");
                    var tot = new int[2, 4];
                    foreach (var sv in CtSurvey)
                    {
                        if (!Snap(map, LL(sv.lat, sv.lon), sv.road, out var e, out float s, out _)) continue;
                        Vector2 p = e.PointAt(s);
                        var pos = new Vector3(p.x, e.YAt(s), p.y);
                        var line = new StringBuilder($"  {sv.id,-10}");
                        for (int mode = 0; mode < 2; mode++)
                        {
                            CityMeshTrees.Round1Mode = mode == 0;
                            world.DropAll(); mt.Flush();
                            CityMeshTrees.ResetCounts();
                            world.EnsureRing(pos, 2); mt.Flush();
                            int all = CityMeshTrees.Placed + CityMeshTrees.Dropped;
                            tot[mode, 0] += all; tot[mode, 1] += CityMeshTrees.Dropped; tot[mode, 2] += CityMeshTrees.StreetIn; tot[mode, 3] += CityMeshTrees.StreetDropped;
                            line.Append($" | {(mode == 0 ? "r1" : "r2")}: {all} trees, dropped {CityMeshTrees.Dropped} ({Pct(CityMeshTrees.Dropped, all)}), street {CityMeshTrees.StreetIn} dropped {CityMeshTrees.StreetDropped} ({Pct(CityMeshTrees.StreetDropped, CityMeshTrees.StreetIn)}) as street form {CityMeshTrees.StreetAsStreetForm} as bush {CityMeshTrees.StreetAsShrub}; fit {CityMeshTrees.FitMs:0} ms, worst tile {CityMeshTrees.FitMsMaxTile:0.0} ms");
                            if (mode == 1)
                            {
                                AuditRing(mt, map, world, audit, line);
                                line.Append("\n      r2 drops by cause (other | street):");
                                for (int w = 0; w < 7; w++) if (CityMeshTrees.DropWhy[0, w] + CityMeshTrees.DropWhy[1, w] > 0) line.Append($" {(w < 6 ? CityMeshTrees.RejectNames[w] : "no size meets the clear stem")} {CityMeshTrees.DropWhy[0, w]}|{CityMeshTrees.DropWhy[1, w]};");
                                for (int k = 0; k < Mathf.Min(8, CityMeshTrees.DropSamples.Count); k++) line.Append("\n        " + CityMeshTrees.DropSamples[k]);
                            }
                        }
                        log.Append(line).Append('\n');
                    }
                    for (int mode = 0; mode < 2; mode++)
                        log.Append($"  TOTAL {(mode == 0 ? "round 1" : "round 2")}: {tot[mode, 0]} trees, dropped {tot[mode, 1]} ({Pct(tot[mode, 1], tot[mode, 0])}); street {tot[mode, 2]}, dropped {tot[mode, 3]} ({Pct(tot[mode, 3], tot[mode, 2])})\n");
                    CityMeshTrees.Round1Mode = false;
                    world.DropAll(); mt.Flush();
                }

                foreach (var pl in CtPlaces)
                {
                    if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), pl.id) < 0) continue;
                    CityMeshTrees.Round1Mode = false;
                    world.DropAll(); mt.Flush();
                    CityMeshTrees.ResetCounts();
                    if (!CtSnap(map, LL(pl.lat, pl.lon), pl.roads, out var e, out float s, out bool named)) { log.Append($"{pl.id}: no road near\n"); continue; }
                    var p0 = e.PointAt(s);
                    var anchor = new Vector3(p0.x, e.YAt(s), p0.y);
                    world.EnsureRing(anchor, 2); mt.Flush();
                    // THE POSE: on the road, the trees of the kind wanted ahead
                    if (!CtPose(pl, map, mt, e, s, out var eye, out var fwd, out var farEye, out string how)) { log.Append($"{pl.id}: no pose ({how})\n"); continue; }
                    if ((new Vector2(eye.x, eye.z) - p0).magnitude > 120f) { world.EnsureRing(eye, 2); mt.Flush(); }
                    var rot = Quaternion.LookRotation(fwd + Vector3.down * 0.03f);
                    var farRot = Quaternion.LookRotation((eye - farEye).normalized + Vector3.up * 0.02f);
                    if (car != null) car.TeleportTo(eye - fwd * 3f - Vector3.up * 0.8f, Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z)));
                    log.Append($"\n{pl.id}\t'{e.name}' cls {e.cls}{(named ? "" : " (NOT a named road of the place)")}\teye ({eye.x:0.0}, {eye.y:0.0}, {eye.z:0.0}) fwd ({fwd.x:0.00}, {fwd.z:0.00}); {how}\t{pl.what}\n");
                    log.Append($"  r2 ring: {CityMeshTrees.Fitted} tiles, {CityMeshTrees.Placed} mesh trees, dropped {CityMeshTrees.Dropped} ({Pct(CityMeshTrees.Dropped, CityMeshTrees.Placed + CityMeshTrees.Dropped)}), street {CityMeshTrees.StreetIn} dropped {CityMeshTrees.StreetDropped}, as street form {CityMeshTrees.StreetAsStreetForm}, as bush {CityMeshTrees.StreetAsShrub}, as other {CityMeshTrees.StreetAsOther}; others narrowed to a street form {CityMeshTrees.NarrowedOther}; fit {CityMeshTrees.FitMs:0} ms (worst tile {CityMeshTrees.FitMsMaxTile:0.0} ms)\n");
                    AuditRing(mt, map, world, audit, log);

                    var poses = new List<(string, Vector3, Quaternion)> { ("eye", eye, rot), ("far150", farEye, farRot) };
                    if (!noShots)
                    {
                        var hours = pl.hours ? new[] { "noon", "dusk", "night" } : new[] { "noon" };
                        foreach (var h in hours)
                            shots += CtShootSet(cam, sun, dressing, world, mt, pl.id, h == "noon" ? poses : poses.GetRange(0, 1), h, (int)Season.Summer, new[] { "cards", "r2" }, log);
                        if (pl.seasons)
                        {
                            shots += CtShootSet(cam, sun, dressing, world, mt, pl.id, poses.GetRange(0, 1), "noon", (int)Season.Fall, new[] { "cards", "r2" }, log);
                            shots += CtShootSet(cam, sun, dressing, world, mt, pl.id, poses.GetRange(0, 1), "noon", (int)Season.Winter, new[] { "cards", "r2" }, log);
                        }
                        SetDress(dressing, world, (int)Season.Summer);
                        TimeOfDay.Apply(TimeOfDay.Noon, sun);
                        Cost(cam, mt, pl.id, eye, rot, log);
                        if (doPop && pl.pop) PopWalk(cam, mt, pl.id, e, eye, fwd, dir, log);
                        if (pl.id == "dilworth") ImpostorPair(cam, mt, pl.id, farEye, farRot, log);
                    }
                    // ROUND 1's fit at the same poses
                    CityMeshTrees.Round1Mode = true;
                    world.DropAll(); mt.Flush();
                    CityMeshTrees.ResetCounts();
                    world.EnsureRing(anchor, 2); mt.Flush();
                    if ((new Vector2(eye.x, eye.z) - p0).magnitude > 120f) { world.EnsureRing(eye, 2); mt.Flush(); }
                    log.Append($"  r1 ring: {CityMeshTrees.Placed} mesh trees, dropped {CityMeshTrees.Dropped} ({Pct(CityMeshTrees.Dropped, CityMeshTrees.Placed + CityMeshTrees.Dropped)}), street {CityMeshTrees.StreetIn} dropped {CityMeshTrees.StreetDropped}\n");
                    if (!noShots)
                    {
                        var hours = pl.hours ? new[] { "noon", "dusk", "night" } : new[] { "noon" };
                        foreach (var h in hours)
                            shots += CtShootSet(cam, sun, dressing, world, mt, pl.id, h == "noon" ? poses : poses.GetRange(0, 1), h, (int)Season.Summer, new[] { "r1" }, log);
                        if (pl.seasons)
                        {
                            shots += CtShootSet(cam, sun, dressing, world, mt, pl.id, poses.GetRange(0, 1), "noon", (int)Season.Fall, new[] { "r1" }, log);
                            shots += CtShootSet(cam, sun, dressing, world, mt, pl.id, poses.GetRange(0, 1), "noon", (int)Season.Winter, new[] { "r1" }, log);
                        }
                    }
                    CityMeshTrees.Round1Mode = false;
                }
                world.DropAll(); mt.Flush();
                log.Append($"\nCITY FOLIAGE AUDIT (round 2): {audit.tiles} tiles, {audit.trees} mesh trees, {audit.points} crown points tested -> trees through solids {audit.bad} (must be 0): road envelope {audit.road}, building {audit.bld}, pole/wire {audit.pole}, signal {audit.sig}, collider {audit.col}\n");
                foreach (var w in audit.worst) log.Append("  " + w + "\n");
                log.Append("  colliders that refused candidates:");
                foreach (var kv in CityMeshTrees.RejectColliders) log.Append($" {kv.Key} {kv.Value};");
                log.Append($"\n{shots} frames to {dir}\nCITY MESH TREES OK\n");
            }
            catch (System.Exception ex) { log.Append("CITY MESH TREES FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                CityMeshTrees.Round1Mode = false;
                MeshForest.LodEyeOn = false;
                File.WriteAllText(Path.Combine(dir, "ctrees2_log.txt"), log.ToString());
                Debug.Log("[CityMeshTrees]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string Pct(int a, int b) => b > 0 ? (100f * a / b).ToString("0.0") + "%" : "-";
        static float MaxR(CityTreeKit kit, int v) { float m = 0f; for (int b = 0; b < CityTreeKit.Bands; b++) m = Mathf.Max(m, kit.CrownR(v, b)); return m; }
        static int LT(CityTreeKit kit, int v, int l) => kit.lodTris != null && v * 3 + l < kit.lodTris.Length ? kit.lodTris[v * 3 + l] : -1;

        /// <summary>The nearest edge of any of the named roads (anywhere), else the nearest road.</summary>
        static bool CtSnap(CityMap map, Vector2 p, string[] roads, out CityMap.Edge edge, out float s, out bool named)
        {
            edge = null; s = 0f; named = false;
            foreach (var r in roads)
            {
                float best = float.MaxValue;
                foreach (var e in map.edges)
                {
                    if (e.name == null || e.name.IndexOf(r, System.StringComparison.OrdinalIgnoreCase) < 0 || e.tunnel) continue;
                    for (int i = 0; i + 1 < e.pts.Length; i++)
                    {
                        Vector2 q0 = e.pts[i], d = e.pts[i + 1] - q0;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, d) / L2) : 0f;
                        float dd = Vector2.Distance(p, q0 + d * t);
                        if (dd < best) { best = dd; edge = e; s = e.s[i] + Mathf.Sqrt(L2) * t; }
                    }
                }
                if (edge != null && best < 3000f) { named = true; return true; }
                edge = null;
            }
            if (map.NearestRoadPoint(p, 400f, false, out int ei, out float s2, out _)) { edge = map.edges[ei]; s = s2; return true; }
            return false;
        }

        /// <summary>Where a point projects on an edge: station and signed lateral distance (left +).</summary>
        static float StationOf(CityMap.Edge e, Vector2 p, out float lat)
        {
            float best = float.MaxValue, sb = 0f; lat = 0f;
            for (int i = 0; i + 1 < e.pts.Length; i++)
            {
                Vector2 q0 = e.pts[i], d = e.pts[i + 1] - q0;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, d) / L2) : 0f;
                var c = q0 + d * t;
                float dd = Vector2.Distance(p, c);
                if (dd < best) { best = dd; sb = e.s[i] + Mathf.Sqrt(L2) * t; var n = new Vector2(-d.y, d.x).normalized; lat = Vector2.Dot(p - c, n); }
            }
            return sb;
        }

        /// <summary>The eye (and the 150 m eye behind it) for a place: a fixed
        /// heading for the owner's poses, else on a road of the place with the
        /// nearest tree of the kind wanted 30 m ahead and to the side.</summary>
        static bool CtPose(CtPlace pl, CityMap map, CityMeshTrees mt, CityMap.Edge e0, float s0, out Vector3 eye, out Vector3 fwd, out Vector3 farEye, out string how)
        {
            eye = fwd = farEye = Vector3.zero; how = "";
            CityMap.Edge e = e0; float s = s0; float dirSign = 1f;
            if (pl.kind != CtKind.Fixed && pl.kind != CtKind.Any)
            {
                // the trees of the kind, nearest the anchor
                var anchor = e0.PointAt(s0);
                float best = float.MaxValue; Vector3 bestFoot = Vector3.zero; CityMap.Edge be = null;
                var segs = new HashSet<int>();
                foreach (long key in mt.FittedKeys)
                {
                    var plants = mt.PlantsOf(key); var fr = mt.FrameOf(key);
                    if (plants == null || fr == null) continue;
                    for (int i = 0; i < plants.Count; i++)
                    {
                        var f = plants[i].pos; var f2 = new Vector2(f.x, f.z);
                        bool ok = false; CityMap.Edge on = null;
                        if (pl.kind == CtKind.Street) { ok = i < fr.trees.Count && fr.trees[i].street; if (ok) { map.NearestRoadPoint(f2, 15f, false, out int ei, out _, out _); on = ei >= 0 ? map.edges[ei] : null; ok = on != null && on.name != null && System.Array.Exists(pl.roads, r => on.name.IndexOf(r, System.StringComparison.OrdinalIgnoreCase) >= 0); } }
                        else
                        {
                            segs.Clear();
                            map.EdgeSegsInRect(f2 - Vector2.one * 30f, f2 + Vector2.one * 30f, segs);
                            var near = new List<(CityMap.Edge e, float lat)>();
                            foreach (int packed in segs)
                            {
                                var ce = map.edges[packed >> 12];
                                if (ce.name == null || !System.Array.Exists(pl.roads, r => ce.name.IndexOf(r, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                                StationOf(ce, f2, out float lat);
                                if (Mathf.Abs(lat) < ce.width * 0.5f + (pl.kind == CtKind.Verge ? 25f : 7f) && !near.Exists(x => x.e == ce)) near.Add((ce, lat));
                            }
                            if (pl.kind == CtKind.Verge) { ok = near.Count > 0 && Mathf.Abs(near[0].lat) > near[0].e.width * 0.5f + 4f; if (ok) on = near[0].e; }
                            else
                            {
                                // a median tree: carriageways of the road on both sides of it
                                for (int a = 0; a < near.Count && !ok; a++)
                                    for (int b = a + 1; b < near.Count && !ok; b++)
                                    {
                                        var pa = near[a].e.PointAt(StationOf(near[a].e, f2, out _)); var pb = near[b].e.PointAt(StationOf(near[b].e, f2, out _));
                                        if (Vector2.Dot(pa - f2, pb - f2) < 0f) { ok = true; on = near[a].e; }
                                    }
                            }
                        }
                        if (!ok) continue;
                        float dd = (f2 - anchor).sqrMagnitude;
                        if (dd < best) { best = dd; bestFoot = f; be = on; }
                    }
                }
                if (be == null) { how = "no tree of the kind in the ring"; return false; }
                e = be;
                float st = StationOf(e, new Vector2(bestFoot.x, bestFoot.z), out float tl);
                // stand 28 m before it (whichever way has the room), looking at it
                if (st - 28f >= 0f) { s = st - 28f; dirSign = 1f; } else { s = Mathf.Min(e.length, st + 28f); dirSign = -1f; }
                how = $"tree of the kind at ({bestFoot.x:0.0}, {bestFoot.z:0.0}), {Mathf.Abs(tl):0.0} m off '{e.name}', 28 m ahead";
            }
            var t2 = e.TangentAt(s) * dirSign;
            fwd = new Vector3(t2.x, 0f, t2.y).normalized;
            if (pl.kind == CtKind.Fixed && !float.IsNaN(pl.hdg))
            {
                float hr = pl.hdg * Mathf.Deg2Rad;
                if (Vector3.Dot(fwd, new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr))) < 0f) { fwd = -fwd; dirSign = -dirSign; }
            }
            var p = e.PointAt(s);
            float lane = e.oneway ? 0f : -e.width * 0.25f;   // the right-hand lane
            var right = Vector3.Cross(Vector3.up, fwd);
            eye = new Vector3(p.x, e.YAt(s) + 1.15f, p.y) + right * (-lane);
            // 150 m back along the same road
            float sf = Mathf.Clamp(s - 150f * dirSign, 0f, e.length);
            var pf = e.PointAt(sf);
            farEye = new Vector3(pf.x, e.YAt(sf) + 1.6f, pf.y) + right * (-lane);
            if (Vector3.Distance(farEye, eye) < 60f) farEye = eye - fwd * 150f + Vector3.up * 0.5f;
            return true;
        }

        static void SetDress(SeasonDress dressing, CityWorld world, int dress)
        {
            if (dressing != null) dressing.Apply(dress);
            // the cards in the dress of the row (round 1's "before" kept the dress they stood up in)
            var m = CityTrees.MaterialFor(dress);
            if (m == null) return;
            foreach (var r in world.GetComponentsInChildren<MeshRenderer>(true))
                if (r.gameObject.name == "Trees") r.sharedMaterial = m;
        }

        static int CtShootSet(Camera cam, Light sun, SeasonDress dressing, CityWorld world, CityMeshTrees mt, string place,
                              List<(string name, Vector3 eye, Quaternion r)> poses, string h, int dress, string[] modes, StringBuilder log)
        {
            int n = 0;
            int hour = HourOf(h);
            RaceHandoff.CalendarDay = 0;
            TimeOfDay.Apply(hour, sun);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();
            var hd = TimeOfDay.At(hour);
            NightGlow.PreviewAll(hd.lightsOn);
            foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) lights.PreviewBuild(hd.lightsOn);
            SetDress(dressing, world, dress);
            string dn = dress == (int)Season.Winter ? "winter" : dress == (int)Season.Fall ? "fall" : dress == (int)Season.Spring ? "spring" : dress == Seasons.DressSnow ? "snow" : "summer";
            foreach (var p in poses)
            {
                foreach (var mode in modes)
                {
                    CarLights.PushGlobals();
                    cam.transform.SetPositionAndRotation(p.eye, p.r);
                    SunShadows.RenderFor(cam);
                    if (mode == "cards") { MeshForest.MeshTreesOn = false; mt.Forest.CardsNow(); }
                    else { MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam); }
                    PSXScreenshotTool.ShotAs(cam, $"ctrees2/{place}_{p.name}_{h}_{dn}_{mode}", p.eye, p.r);
                    n++;
                }
            }
            MeshForest.MeshTreesOn = true;
            log.Append($"  {place} {h} {dn} {string.Join("+", modes)}: {n} frames\n");
            return n;
        }

        /// <summary>Frame time and triangles at one eye, cards v mesh trees
        /// (editor, PC profile; the render waited for with a read-back), and the
        /// ring's mesh-tree triangles in every direction per FOREST DETAIL.</summary>
        static void Cost(Camera cam, CityMeshTrees mt, string place, Vector3 eye, Quaternion r, StringBuilder log)
        {
            cam.transform.SetPositionAndRotation(eye, r);
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            double Time(bool mesh, int frames)
            {
                var sw = new System.Diagnostics.Stopwatch();
                for (int i = 0; i < frames + 2; i++)
                {
                    if (i == 2) sw.Start();
                    if (mesh) { MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam); } else { MeshForest.MeshTreesOn = false; mt.Forest.CardsNow(); }
                    cam.Render();
                    RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
                }
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds / frames;
            }
            // (interleaved, twice: the editor's own noise shows in the spread)
            double c1 = Time(false, 12), m1 = Time(true, 12), c2 = Time(false, 12), m2 = Time(true, 12);
            MeshForest.StatsOn = true; MeshForest.StatsReset();
            for (int i = 0; i < 12; i++) { mt.Forest.DrawNow(cam); cam.Render(); }
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            int sf = Mathf.Max(1, MeshForest.StatFrames);
            log.Append($"  COST {place}: frame (1920x1080, editor, render + read-back) cards {c1:0.00}/{c2:0.00} ms, mesh trees {m1:0.00}/{m2:0.00} ms (+{(m1 + m2 - c1 - c2) * 0.5:0.00}); drawn {MeshForest.SumPlants / sf:0} plants, {MeshForest.SumTris / sf / 1e6:0.00} M tris, {MeshForest.SumDraws / sf:0} draws; {MeshForest.StatsBreakdown()}\n");
            MeshForest.StatsOn = false;
            MeshForest.MeshTreesOn = true;
            cam.targetTexture = old;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(px);
            // the ring in every direction (no view cull): each plant at the level its distance picks
            var kit = mt.Kit;
            float[] sc = { 0.65f, 1f, 1.5f };
            Vector3[] mul = { new Vector3(2.15f, 1.2f, 1.3f), new Vector3(1.85f, 1f, 1f), new Vector3(1.5f, 1f, 1f) };
            var sb = new StringBuilder($"  RING TRIS {place} ({mt.LivePlants()} plants in {mt.LiveTiles} tiles, from the eye, all directions):");
            var b0 = CityMeshTrees.LodOverride.x > 0f ? CityMeshTrees.LodOverride : new Vector3(kit.lod0To, kit.lod1To, kit.meshRange);
            for (int q = 0; q < 3; q++)
            {
                float l0 = b0.x * sc[q] * mul[q].x, l1 = b0.y * sc[q] * mul[q].y, mr = b0.z * sc[q] * mul[q].z;
                double tris = 0; int[] at = new int[4];
                foreach (long key in mt.FittedKeys)
                {
                    var plants = mt.PlantsOf(key); if (plants == null) continue;
                    foreach (var p in plants)
                    {
                        float d = Vector3.Distance(p.pos, eye);
                        int lv = d < l0 ? 0 : d < l1 ? 1 : d < mr ? 2 : 3;
                        at[lv]++;
                        tris += lv < 3 ? LT(kit, p.variant, lv) : 4;
                    }
                }
                sb.Append($" {new[] { "LOW", "MEDIUM", "HIGH" }[q]} ({l0:0}/{l1:0}/{mr:0} m) {tris / 1e6:0.00} M [near {at[0]}, mid {at[1]}, low {at[2]}, impostor {at[3]}];");
            }
            log.Append(sb).Append('\n');
        }

        /// <summary>THE POP MEASURE on a city road (the stage tour's lag pair,
        /// in the editor): at each step the same view drawn twice, the forest's
        /// levels as seen from the eye and from 6 m back; the share of 16 px
        /// blocks whose light changes more than 3% is what a driver would see
        /// pop in that 6 m. A same-eye pair every fifth step is the noise.</summary>
        static void PopWalk(Camera cam, CityMeshTrees mt, string place, CityMap.Edge e, Vector3 eye, Vector3 fwd, string dir, StringBuilder log)
        {
            const int W = 960, Hh = 540, BS = 16;
            var rt = new RenderTexture(W, Hh, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            var tex = new Texture2D(W, Hh, TextureFormat.RGBA32, false);
            float[] Grab(Vector3 at, Quaternion r, bool lag, Vector3 lagEye)
            {
                cam.transform.SetPositionAndRotation(at, r);
                MeshForest.LodEyeOn = lag; MeshForest.LodEye = lagEye;
                MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, Hh), 0, 0); tex.Apply(); RenderTexture.active = null;
                MeshForest.LodEyeOn = false;
                var px = tex.GetPixels32();
                var L = new float[(W / BS) * (Hh / BS)];
                for (int y = 0; y < Hh; y++)
                    for (int x = 0; x < W; x++)
                    {
                        var c = px[y * W + x];
                        L[(y / BS) * (W / BS) + x / BS] += (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
                    }
                for (int b = 0; b < L.Length; b++) L[b] /= BS * BS;
                return L;
            }
            var sets = new List<(string name, Vector3 lod)>
            {
                ("now", Vector3.zero),
                ("x1.5", (CityMeshTrees.LodOverride.x > 0f ? CityMeshTrees.LodOverride : new Vector3(mt.Kit.lod0To, mt.Kit.lod1To, mt.Kit.meshRange)) * 1.5f),
            };
            var keepOverride = CityMeshTrees.LodOverride;
            foreach (var set in sets)
            {
                if (set.lod != Vector3.zero) { CityMeshTrees.LodOverride = set.lod; mt.Rebuild(); }
                float sumShare = 0f, maxShare = 0f, noise = 0f; int n = 0, nn = 0; double tris = 0;
                var csv = new StringBuilder("step,m,share\n");
                for (int k = 0; k < 30; k++)
                {
                    var at = eye + fwd * (k * 10f);
                    var r = Quaternion.LookRotation(fwd + Vector3.down * 0.03f);
                    var A = Grab(at, r, false, at);
                    bool noiseStep = k % 5 == 4;
                    var B = Grab(at, r, true, noiseStep ? at : at - fwd * 6f);
                    int pop = 0;
                    for (int b = 0; b < A.Length; b++) if (Mathf.Abs(A[b] - B[b]) > 0.03f) pop++;
                    float share = pop / (float)A.Length;
                    if (noiseStep) { noise += share; nn++; continue; }
                    sumShare += share; maxShare = Mathf.Max(maxShare, share); n++;
                    csv.Append(k).Append(',').Append(k * 10).Append(',').Append(share.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                }
                File.WriteAllText(Path.Combine(dir, $"pop_{place}_{set.name}.csv"), csv.ToString());
                log.Append($"  POP {place} levels {set.name} ({mt.Forest.lod0To:0}/{mt.Forest.lod1To:0}/{mt.Forest.meshRange:0} m): blocks changed by a 6 m level lag mean {100f * sumShare / Mathf.Max(1, n):0.00}% (worst step {100f * maxShare:0.00}%), noise {100f * noise / Mathf.Max(1, nn):0.00}% over 300 m\n");
            }
            CityMeshTrees.LodOverride = keepOverride; mt.Rebuild();
            cam.targetTexture = old;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        /// <summary>One view drawn with every tree at its mesh level, then with
        /// every tree as its impostor: colour and silhouette side by side.</summary>
        static void ImpostorPair(Camera cam, CityMeshTrees mt, string place, Vector3 eye, Quaternion r, StringBuilder log)
        {
            var keep = CityMeshTrees.LodOverride;
            var b = keep.x > 0f ? keep : new Vector3(mt.Kit.lod0To, mt.Kit.lod1To, mt.Kit.meshRange);
            for (int k = 0; k < 2; k++)
            {
                // k 0: meshes out to 400 m; k 1: impostors from 1 m
                CityMeshTrees.LodOverride = k == 0 ? new Vector3(b.x, b.y, 400f) : new Vector3(0.4f, 0.6f, 1f);
                mt.Rebuild();
                cam.transform.SetPositionAndRotation(eye, r);
                SunShadows.RenderFor(cam);
                MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                PSXScreenshotTool.ShotAs(cam, $"ctrees2/{place}_imp_noon_summer_{(k == 0 ? "mesh" : "impostor")}", eye, r);
            }
            CityMeshTrees.LodOverride = keep; mt.Rebuild();
            log.Append($"  IMPOSTOR PAIR {place}: {place}_imp_noon_summer_mesh / _impostor\n");
        }

        // ------------------------------------------------------------------
        //  THE CITY FOLIAGE AUDIT
        // ------------------------------------------------------------------
        class AuditSum
        {
            public int tiles, trees, points, bad, road, bld, pole, sig, col;
            public readonly HashSet<long> seen = new HashSet<long>();
            public readonly List<string> worst = new List<string>();
        }

        static readonly Dictionary<Mesh, Vector3[]> samples = new Dictionary<Mesh, Vector3[]>();
        static readonly Collider[] auditHits = new Collider[64];

        static Vector3[] SampleOf(Mesh m)
        {
            if (samples.TryGetValue(m, out var s)) return s;
            var l = new List<Vector3>();
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                if (!TreeKit.Read(m, sub, out var verts, out var tris)) continue;
                int step = Mathf.Max(1, tris.Length / 900);
                for (int i = 0; i < tris.Length; i += step) l.Add(verts[tris[i]]);
            }
            s = l.ToArray();
            samples[m] = s;
            return s;
        }

        static void AuditRing(CityMeshTrees mt, CityMap map, CityWorld world, AuditSum sum, StringBuilder log)
        {
            Physics.SyncTransforms();
            var kit = mt.Kit;
            var segSet = new HashSet<int>();
            int mask = (1 << CityWorld.SolidLayer) | 1;
            int ringBad = 0, ringTrees = 0;
            foreach (long key in new List<long>(mt.FittedKeys))
            {
                if (!sum.seen.Add(key)) continue;
                var plants = mt.PlantsOf(key);
                var frame = mt.FrameOf(key);
                if (plants == null) continue;
                sum.tiles++;
                foreach (var pp in plants)
                {
                    sum.trees++; ringTrees++;
                    var v = kit.variants[pp.variant];
                    var M = Matrix4x4.TRS(pp.pos, Quaternion.Euler(0f, pp.yaw, 0f), Vector3.one * pp.scale) * v.pre;
                    var pts = SampleOf(v.lods[0]);
                    var p2 = new Vector2(pp.pos.x, pp.pos.z);
                    float reach = 0f;
                    for (int b = 0; b < CityTreeKit.Bands; b++) reach = Mathf.Max(reach, kit.CrownR(pp.variant, b) * pp.scale);
                    segSet.Clear();
                    map.EdgeSegsInRect(p2 - Vector2.one * (reach + 25f), p2 + Vector2.one * (reach + 25f), segSet);
                    var rs = new List<(Vector2 a, Vector2 b, float hw, CityMap.Edge e, float s0, float len)>();
                    foreach (int packed in segSet)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        var e = map.edges[ei];
                        if (e.tunnel) continue;
                        rs.Add((e.pts[si], e.pts[si + 1], e.width * 0.5f, e, e.s[si], Vector2.Distance(e.pts[si], e.pts[si + 1])));
                    }
                    var bl = new List<CityBuildings.B>();
                    int tx = Mathf.FloorToInt(pp.pos.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(pp.pos.z / CityMeshes.TileSize);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (world.Buildings != null && world.Buildings.TryGetValue(((long)(tx + dx) << 24) ^ ((tz + dz) & 0xFFFFFF), out var list))
                                foreach (var b in list) if ((b.pos - p2).sqrMagnitude < (reach + Mathf.Max(b.w, b.d) + 2f) * (reach + Mathf.Max(b.w, b.d) + 2f)) bl.Add(b);
                    int nc = Physics.OverlapCapsuleNonAlloc(pp.pos, pp.pos + Vector3.up * (v.height * pp.scale + 1f), reach + 0.5f, auditHits, mask, QueryTriggerInteraction.Ignore);
                    bool anyCol = false;
                    for (int i = 0; i < nc; i++) { var c = auditHits[i]; if (c != null && Eligible(c)) { anyCol = true; break; } }
                    int hitRoad = 0, hitBld = 0, hitPole = 0, hitSig = 0, hitCol = 0;
                    string colName = null;
                    foreach (var lp in pts)
                    {
                        var w = M.MultiplyPoint3x4(lp);
                        sum.points++;
                        var w2 = new Vector2(w.x, w.z);
                        if (w.y < pp.pos.y + 0.4f) continue;   // (roots and the trunk's foot in the ground)
                        foreach (var r in rs)
                        {
                            var d = r.b - r.a; float L2 = d.sqrMagnitude;
                            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(w2 - r.a, d) / L2) : 0f;
                            if (Vector2.Distance(w2, r.a + d * t) >= r.hw) continue;
                            float ry = r.e.YAt(r.s0 + r.len * t);
                            if (w.y < ry + 4.5f && w.y > ry - 1.0f) { hitRoad++; break; }
                        }
                        foreach (var b in bl)
                        {
                            float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                            var q = w2 - b.pos;
                            if (Mathf.Abs(q.x * cy - q.y * sy) >= b.w * 0.5f || Mathf.Abs(q.x * sy + q.y * cy) >= b.d * 0.5f) continue;
                            float roof = b.gable ? 0.45f * Mathf.Min(b.w, b.d) : 0f;
                            if (w.y < pp.pos.y + b.h + roof) { hitBld++; break; }
                        }
                        if (frame != null && frame.poles != null)
                        {
                            foreach (var pl in frame.poles.poles)
                                if (Vector2.Distance(w2, new Vector2(pl.foot.x, pl.foot.z)) < 0.2f && w.y < pl.top) { hitPole++; break; }
                            foreach (var (a, b) in frame.poles.spans)
                            {
                                Vector2 a2 = new Vector2(a.foot.x, a.foot.z), b2 = new Vector2(b.foot.x, b.foot.z), d = b2 - a2;
                                float L2 = d.sqrMagnitude; float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(w2 - a2, d) / L2) : 0f;
                                if (Vector2.Distance(w2, a2 + d * t) > 0.15f) continue;
                                float top = Mathf.Lerp(a.top, b.top, t);
                                if (w.y < top + 0.1f && w.y > top - 0.4f - 1.2f * Mathf.Max(1, a.levels - 1) - 1.2f) { hitPole++; break; }
                            }
                        }
                        if (frame != null && frame.signals != null)
                            foreach (var (sf, top) in frame.signals.poles)
                                if (Vector2.Distance(w2, new Vector2(sf.x, sf.z)) < 0.25f && w.y < top) { hitSig++; break; }
                        if (anyCol)
                        {
                            int n2 = Physics.OverlapSphereNonAlloc(w, 0.04f, auditHits, mask, QueryTriggerInteraction.Ignore);
                            for (int i = 0; i < n2; i++) if (auditHits[i] != null && Eligible(auditHits[i])) { hitCol++; colName = auditHits[i].gameObject.name + "/" + LayerMask.LayerToName(auditHits[i].gameObject.layer) + $" top {auditHits[i].bounds.max.y - pp.pos.y:0.00} m over the foot, point {w.y - pp.pos.y:0.00}"; break; }
                        }
                    }
                    if (hitRoad + hitBld + hitPole + hitSig + hitCol > 0)
                    {
                        sum.bad++; ringBad++;
                        if (hitRoad > 0) sum.road++; if (hitBld > 0) sum.bld++; if (hitPole > 0) sum.pole++; if (hitSig > 0) sum.sig++; if (hitCol > 0) sum.col++;
                        if (sum.worst.Count < 40)
                            sum.worst.Add($"{kit.names[pp.variant]} x{pp.scale:0.00} at ({pp.pos.x:0.0}, {pp.pos.y:0.0}, {pp.pos.z:0.0}): points in road {hitRoad}, building {hitBld}, pole/wire {hitPole}, signal {hitSig}, collider {hitCol}{(colName != null ? " (" + colName + ")" : "")}");
                    }
                }
            }
            log.Append($"  audit: {ringTrees} mesh trees in this ring's new tiles, {ringBad} through a solid\n");
        }

        static bool Eligible(Collider c)
        {
            if (c.name == CityTrees.TrunkName) return false;
            string n = c.gameObject.name;
            if (n == "Ground" || n == "Kerbs" || n == "Roads" || n == "Banks" || n == "Water") return false;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;
            return true;
        }
    }
}
