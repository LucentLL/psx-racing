using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S MESH TREES, ROUND 3, MEASURED AND PROVED (trees2,
    /// 2026-11-15; owner: "trees don't seem accurate for Charlotte and are
    /// often too tall"). One run, one kit, the switch flipped in process
    /// (CityMeshTrees.Round3 = PSX_CT_ROUND3): at each place the ring is
    /// fitted round 2's way (BEFORE) and round 3's (AFTER), and for both
    ///   * THE SURVEY: every drawn tree's height and crown width by its site
    ///     (uptown grate, median row, old-city street, other roadside, yard,
    ///     lot / strip, woodland, open ground) and the models used;
    ///   * the driver's-eye frame at the same pose (noon, summer);
    ///   * the cost: frame time and triangles at the eye, the ring's
    ///     triangles per FOREST DETAIL, the fit's milliseconds and tests;
    ///   * the city foliage audit (trees through solids, trunks v drawn trees).
    /// Places: the owner's West Trade St median, a Tryon St block, Dilworth,
    /// Myers Park, a 1980s subdivision, South Blvd's strip, a wooded road,
    /// Johnston Rd's median row, Beatties Ford Rd at JCSU.
    ///
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunMeshTrees3
    ///   PSX_CT_REKIT=1 (rebuild the kit first)  PSX_CT3_ONLY=place,place  PSX_CT3_NOSHOTS=1  PSX_CT3_AUDIT0=0
    /// Output: Screenshots/ctrees3/&lt;place&gt;_&lt;before|after&gt;.png, ctrees3_log.txt, ctrees3_survey.csv
    /// </summary>
    public static partial class CityRefSpots
    {
        struct T3Place { public string id, what; public string[] roads; public double lat, lon; public float hdg; public CtKind kind; public bool shot, onewayOnly, wooded; public int ring; }

        static readonly T3Place[] T3Places =
        {
            new T3Place { id = "wtrade", lat = 35.2331, lon = -80.8512, roads = new[] { "West Trade Street" }, hdg = 325f, kind = CtKind.Fixed, shot = true, onewayOnly = true, ring = 2, what = "West Trade St, the owner's median (25.webp), toward Johnson C. Smith" },
            new T3Place { id = "uptown", lat = 35.22745, lon = -80.84275, roads = new[] { "North Tryon Street", "South Tryon Street" }, hdg = 50f, kind = CtKind.Street, shot = true, ring = 2, what = "Tryon St, Uptown: a block of grate street trees" },
            new T3Place { id = "dilworth", lat = 35.20225, lon = -80.84758, roads = new[] { "Dilworth" }, hdg = 192f, kind = CtKind.Fixed, shot = true, ring = 2, what = "Dilworth Rd E: an old-neighbourhood street" },
            new T3Place { id = "subdivision", lat = 35.0600, lon = -80.8350, roads = new string[0], hdg = 0f, kind = CtKind.Any, shot = true, ring = 2, what = "a south Charlotte subdivision street" },
            new T3Place { id = "arterial", lat = 35.1650, lon = -80.8010, roads = new[] { "Providence Road" }, hdg = 150f, kind = CtKind.Fixed, shot = true, ring = 2, what = "Providence Rd: a four-lane arterial" },
            new T3Place { id = "strip", lat = 35.1702, lon = -80.8762, roads = new[] { "South Boulevard" }, hdg = 200f, kind = CtKind.Fixed, shot = false, ring = 1, what = "South Blvd: a commercial strip (survey only)" },
            new T3Place { id = "woodland", lat = 35.1300, lon = -80.8300, roads = new string[0], hdg = 0f, kind = CtKind.Fixed, shot = true, wooded = true, ring = 2, what = "a road through woods (the canopy map's densest stretch within 3 km)" },
            new T3Place { id = "median", lat = 35.0560, lon = -80.8440, roads = new[] { "Johnston Road", "W T Harris" }, hdg = float.NaN, kind = CtKind.Median, shot = true, ring = 2, what = "a planted median row (Johnston Rd / W.T. Harris Blvd)" },
            new T3Place { id = "myerspark", lat = 35.19280, lon = -80.83678, roads = new[] { "Queens Road West", "Queens Road" }, hdg = 200f, kind = CtKind.Fixed, shot = true, ring = 1, what = "Queens Rd W, Myers Park: the willow oak avenue" },
            new T3Place { id = "beatties", lat = 35.2437, lon = -80.8573, roads = new[] { "Beatties Ford Road" }, hdg = 340f, kind = CtKind.Fixed, shot = false, ring = 1, what = "Beatties Ford Rd at Johnson C. Smith" },
        };

        class T3Acc
        {
            public readonly List<float> h = new List<float>(), w = new List<float>();
            public readonly Dictionary<string, int> models = new Dictionary<string, int>();
            public double tris0;
        }

        static readonly float[] T3Bins = { 0f, 4f, 6f, 8f, 10f, 12f, 15f, 18f, 22f, 26f, 99f };

        public static void RunMeshTrees3()
        {
            var log = new StringBuilder("CITY MESH TREES ROUND 3 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "ctrees3");
            Directory.CreateDirectory(dir);
            var csv = new StringBuilder("mode,place,site,species,street,model,height_m,crown_w_m,scale\n");
            int code = 0, shots = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                var kitAsset = PSXRacingBuilder.EnsureCityTreeKit(System.Environment.GetEnvironmentVariable("PSX_CT_REKIT") == "1");
                if (kitAsset == null) throw new System.Exception("no city tree kit (store files?)");
                log.Append($"kit ready at {clock.Elapsed.TotalSeconds:0} s\n");
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
                CityMeshTrees.RecordSites = true;
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                var kit = mt.Kit;
                if (!mt.Round3Ready) throw new System.Exception("the kit has no round-3 forms");
                log.Append($"kit: {kit.variants.Length} species; LOD {mt.Forest.lod0To:0}/{mt.Forest.lod1To:0}/{mt.Forest.meshRange:0} m, impostors to {mt.Forest.farRange:0} m (FOREST DETAIL {ForestDetailPrefs.Label}); pool {mt.Forest.PoolSlots} slots\n");
                for (int v = 0; v < kit.variants.Length; v++)
                    log.Append($"  {v} {kit.names[v]} [{kit.roles[v]}{(kit.Group3(v) != 0 ? " g3:" + kit.Group3(v) : "")}] {kit.variants[v].height:0.0} m crown from {kit.crownBase[v]:0.00}, widest r {MaxR(kit, v):0.0} m, tris {LT(kit, v, 0)}/{LT(kit, v, 1)}/{LT(kit, v, 2)}\n");

                string only = System.Environment.GetEnvironmentVariable("PSX_CT3_ONLY");
                bool noShots = System.Environment.GetEnvironmentVariable("PSX_CT3_NOSHOTS") == "1";
                bool audit0On = System.Environment.GetEnvironmentVariable("PSX_CT3_AUDIT0") != "0";
                var audits = new[] { new AuditSum(), new AuditSum() };
                var acc = new Dictionary<int, T3Acc>[] { new Dictionary<int, T3Acc>(), new Dictionary<int, T3Acc>() };
                var tot = new double[2, 12];
                int tilesSurveyed = 0;
                var seenTiles = new HashSet<long>();

                // (the owner's street: every divided piece of it, for the record)
                {
                    var sbw = new StringBuilder("West Trade Street, one-way (divided) pieces:");
                    foreach (var e in map.edges)
                        if (e.name == "West Trade Street" && e.oneway && !e.tunnel && e.length > 30f)
                        {
                            var q = e.PointAt(e.length * 0.5f);
                            sbw.Append($" [{CityAudit.LatLon(q.x, q.y)} len {e.length:0} w {e.width:0.0}]");
                        }
                    log.Append(sbw).Append('\n');
                }

                foreach (var pl in T3Places)
                {
                    if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), pl.id) < 0) continue;
                    CityMap.Edge e; float s; bool named = true;
                    if (pl.wooded) { if (!T3Wooded(map, LL(pl.lat, pl.lon), 3000f, out e, out s)) { log.Append($"{pl.id}: no wooded road\n"); continue; } }
                    else if (pl.onewayOnly) { if (!T3SnapOneway(map, LL(pl.lat, pl.lon), pl.roads[0], pl.hdg, out e, out s)) { log.Append($"{pl.id}: no divided piece\n"); continue; } }
                    else if (!CtSnap(map, LL(pl.lat, pl.lon), pl.roads, out e, out s, out named)) { log.Append($"{pl.id}: no road near\n"); continue; }
                    var p0 = e.PointAt(s);
                    var anchor = new Vector3(p0.x, e.YAt(s), p0.y);
                    Vector3 eye = Vector3.zero, fwd = Vector3.forward, farEye; Quaternion rot = Quaternion.identity; string how = "";
                    bool posed = false;
                    for (int mode = 0; mode < 2; mode++)
                    {
                        CityMeshTrees.Round3 = mode == 1;
                        world.DropAll(); mt.Flush();
                        CityMeshTrees.ResetCounts();
                        var tc = System.Diagnostics.Stopwatch.StartNew();
                        world.EnsureRing(anchor, pl.ring); mt.Flush();
                        if (!posed)
                        {
                            var cp = new CtPlace { id = pl.id, lat = pl.lat, lon = pl.lon, roads = pl.roads, hdg = pl.hdg, kind = pl.kind, what = pl.what };
                            if (!CtPose(cp, map, mt, e, s, out eye, out fwd, out farEye, out how)) { log.Append($"{pl.id}: no pose ({how})\n"); break; }
                            rot = Quaternion.LookRotation(fwd + Vector3.down * 0.03f);
                            posed = true;
                            log.Append($"\n{pl.id}\t'{e.name}' cls {e.cls}{(e.oneway ? " one-way" : "")} w {e.width:0.0}{(named ? "" : " (NOT a named road of the place)")}\t{CityAudit.LatLon(eye.x, eye.z)} eye ({eye.x:0.0}, {eye.y:0.0}, {eye.z:0.0}) fwd ({fwd.x:0.00}, {fwd.z:0.00}); {how}\t{pl.what}\n");
                        }
                        if ((new Vector2(eye.x, eye.z) - p0).magnitude > 120f) { world.EnsureRing(eye, pl.ring); mt.Flush(); }
                        double ringMs = tc.Elapsed.TotalMilliseconds;
                        string tag = mode == 0 ? "before" : "after";
                        int all = CityMeshTrees.Placed + CityMeshTrees.Dropped;
                        log.Append($"  {tag}: {CityMeshTrees.Fitted} tiles, {all} trees, placed {CityMeshTrees.Placed}, dropped {CityMeshTrees.Dropped} ({Pct(CityMeshTrees.Dropped, all)}), moved {CityMeshTrees.Moved}; street {CityMeshTrees.StreetIn} dropped {CityMeshTrees.StreetDropped} ({Pct(CityMeshTrees.StreetDropped, CityMeshTrees.StreetIn)}) as a bush {CityMeshTrees.StreetAsShrub}; made breakaway {CityMeshTrees.Softened}; " +
                                   $"fit {CityMeshTrees.FitMs:0} ms ({CityMeshTrees.FitMs / Mathf.Max(1, CityMeshTrees.Fitted):0.0} a tile, worst tile {CityMeshTrees.FitMsMaxTile:0.0}, worst tree {CityMeshTrees.TreeMsMax:0.00} ms), tests max {CityMeshTrees.TestsMax}, capped {CityMeshTrees.CappedTrees}, data tests {CityMeshTrees.DataTests}; ring built in {ringMs / 1000.0:0.0} s\n");
                        tot[mode, 0] += all; tot[mode, 1] += CityMeshTrees.Dropped; tot[mode, 2] += CityMeshTrees.StreetIn; tot[mode, 3] += CityMeshTrees.StreetDropped;
                        tot[mode, 4] += CityMeshTrees.FitMs; tot[mode, 5] += CityMeshTrees.Fitted; tot[mode, 6] = System.Math.Max(tot[mode, 6], CityMeshTrees.FitMsMaxTile);
                        tot[mode, 7] = System.Math.Max(tot[mode, 7], CityMeshTrees.TreeMsMax); tot[mode, 8] += CityMeshTrees.StreetAsShrub; tot[mode, 9] += CityMeshTrees.Softened;
                        // ---- the survey: every drawn tree of the ring ----
                        foreach (long key in mt.FittedKeys)
                        {
                            var plants = mt.PlantsOf(key); var fr = mt.FrameOf(key); var cx = mt.CtxOf(key);
                            if (plants == null || fr == null || cx == null) continue;
                            if (mode == 1 && seenTiles.Add(key)) tilesSurveyed++;
                            for (int i = 0; i < plants.Count && i < cx.Count; i++)
                            {
                                var pp = plants[i];
                                float hh = kit.variants[pp.variant].height * pp.scale, ww = 2f * MaxR(kit, pp.variant) * pp.scale;
                                if (!acc[mode].TryGetValue(cx[i], out var a)) acc[mode][cx[i]] = a = new T3Acc();
                                a.h.Add(hh); a.w.Add(ww);
                                a.models.TryGetValue(kit.names[pp.variant], out int mc); a.models[kit.names[pp.variant]] = mc + 1;
                                a.tris0 += LT(kit, pp.variant, 0);
                                var t = i < fr.trees.Count ? fr.trees[i] : default;
                                csv.Append(tag).Append(',').Append(pl.id).Append(',').Append(CityMeshTrees.CtxNames[cx[i]]).Append(',').Append(t.species).Append(',').Append(t.street ? 1 : 0).Append(',')
                                   .Append(kit.names[pp.variant]).Append(',').Append(hh.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                                   .Append(ww.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(pp.scale.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                            }
                        }
                        if (mode == 1 || audit0On) AuditRing(mt, map, world, audits[mode], log);
                        if (pl.shot && !noShots)
                        {
                            if (car != null) car.TeleportTo(eye - fwd * 3f - Vector3.up * 0.8f, Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z)));
                            RaceHandoff.CalendarDay = 0;
                            TimeOfDay.Apply(TimeOfDay.Noon, sun);
                            var globals = Object.FindAnyObjectByType<PSXGlobals>();
                            if (globals != null) globals.Apply();
                            NightGlow.PreviewAll(false);
                            SetDress(dressing, world, (int)Season.Summer);
                            CarLights.PushGlobals();
                            cam.transform.SetPositionAndRotation(eye, rot);
                            SunShadows.RenderFor(cam);
                            MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                            PSXScreenshotTool.ShotAs(cam, $"ctrees3/{pl.id}_{tag}", eye, rot);
                            shots++;
                            Cost(cam, mt, pl.id + " " + tag, eye, rot, log);
                        }
                    }
                }
                CityMeshTrees.Round3 = true;
                world.DropAll(); mt.Flush();

                // ---------------- THE SURVEY'S TABLES ----------------
                for (int mode = 0; mode < 2; mode++)
                {
                    log.Append($"\nSURVEY {(mode == 0 ? "BEFORE (round 2, PSX_CT_ROUND3=0)" : "AFTER (round 3)")}: height and crown width (widest reach x 2) of every drawn tree, by site\n");
                    for (int c = 0; c < CityMeshTrees.CtxNames.Length; c++)
                    {
                        if (!acc[mode].TryGetValue(c, out var a) || a.h.Count == 0) continue;
                        var hs = new List<float>(a.h); hs.Sort();
                        var ws = new List<float>(a.w); ws.Sort();
                        float Q(List<float> l, float q) => l[Mathf.Min(l.Count - 1, (int)(q * l.Count))];
                        double mh = 0, mw = 0; for (int i = 0; i < a.h.Count; i++) { mh += a.h[i]; mw += a.w[i]; }
                        mh /= a.h.Count; mw /= a.w.Count;
                        var bins = new int[T3Bins.Length - 1];
                        foreach (float x in a.h) for (int b = 0; b < bins.Length; b++) if (x >= T3Bins[b] && x < T3Bins[b + 1]) { bins[b]++; break; }
                        var sbb = new StringBuilder();
                        for (int b = 0; b < bins.Length; b++) sbb.Append($" {T3Bins[b]:0}-{(T3Bins[b + 1] > 90f ? "+" : T3Bins[b + 1].ToString("0"))}:{100f * bins[b] / a.h.Count:0}%");
                        var ml = new List<KeyValuePair<string, int>>(a.models); ml.Sort((x, y) => y.Value.CompareTo(x.Value));
                        var sbm = new StringBuilder();
                        for (int i = 0; i < Mathf.Min(6, ml.Count); i++) sbm.Append($" {ml[i].Key} {100f * ml[i].Value / a.h.Count:0}%;");
                        log.Append($"  {CityMeshTrees.CtxNames[c],-16} n {a.h.Count,6}  height mean {mh:0.0} p10 {Q(hs, 0.1f):0.0} p50 {Q(hs, 0.5f):0.0} p90 {Q(hs, 0.9f):0.0} max {hs[hs.Count - 1]:0.0} m | crown width mean {mw:0.0} p10 {Q(ws, 0.1f):0.0} p50 {Q(ws, 0.5f):0.0} p90 {Q(ws, 0.9f):0.0} m | width/height {mw / mh:0.00} | LOD0 tris a tree {a.tris0 / a.h.Count:0}\n" +
                                   $"      heights (m):{sbb}\n      models ({ml.Count}):{sbm}\n");
                    }
                    log.Append($"  TOTAL {(mode == 0 ? "before" : "after")}: {tot[mode, 0]:0} trees, dropped {tot[mode, 1]:0} ({Pct((int)tot[mode, 1], (int)tot[mode, 0])}); street {tot[mode, 2]:0} dropped {tot[mode, 3]:0} ({Pct((int)tot[mode, 3], (int)tot[mode, 2])}), as a bush {tot[mode, 8]:0}; made breakaway {tot[mode, 9]:0}; " +
                               $"fit {tot[mode, 4]:0} ms over {tot[mode, 5]:0} tiles ({tot[mode, 4] / System.Math.Max(1, tot[mode, 5]):0.0} ms a tile), worst tile {tot[mode, 6]:0.0} ms, worst tree {tot[mode, 7]:0.00} ms\n");
                }
                log.Append($"  tiles surveyed: {tilesSurveyed}\n");
                for (int mode = 0; mode < 2; mode++)
                {
                    var audit = audits[mode];
                    if (audit.tiles == 0) continue;
                    log.Append($"\nCITY FOLIAGE AUDIT {(mode == 0 ? "BEFORE" : "AFTER (round 3)")}: {audit.tiles} tiles, {audit.trees} mesh trees, {audit.points} crown points tested -> trees through solids {audit.bad} (must be 0): road envelope {audit.road}, building {audit.bld}, pole/wire {audit.pole}, signal {audit.sig}, collider {audit.col}; trunks standing with no drawn tree {audit.colliderOnly} (must be 0)\n");
                    foreach (var w in audit.worst) log.Append("  " + w + "\n");
                }
                log.Append($"\npool growths {CityMeshTrees.PoolGrowths}; {shots} frames to {dir}; {clock.Elapsed.TotalSeconds:0} s\nCITY MESH TREES 3 OK\n");
            }
            catch (System.Exception ex) { log.Append("CITY MESH TREES 3 FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                CityMeshTrees.Round3 = true;
                CityMeshTrees.RecordSites = false;
                MeshForest.LodEyeOn = false;
                File.WriteAllText(Path.Combine(dir, "ctrees3_log.txt"), log.ToString());
                File.WriteAllText(Path.Combine(dir, "ctrees3_survey.csv"), csv.ToString());
                Debug.Log("[CityMeshTrees3]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The nearest one-way (divided) piece of a named road, its middle.</summary>
        static bool T3SnapOneway(CityMap map, Vector2 p, string road, float hdg, out CityMap.Edge edge, out float s)
        {
            edge = null; s = 0f;
            float best = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e.name != road || !e.oneway || e.tunnel || e.bridge || e.length < 60f) continue;
                var tg = e.TangentAt(e.length * 0.5f);
                if (tg.x * Mathf.Sin(hdg * Mathf.Deg2Rad) + tg.y * Mathf.Cos(hdg * Mathf.Deg2Rad) < 0.3f) continue;   // (its traffic runs the way the pose looks)
                float d = Vector2.Distance(e.PointAt(e.length * 0.5f), p);
                if (d < best) { best = d; edge = e; s = e.length * 0.5f; }
            }
            return edge != null;
        }

        /// <summary>The stretch of a through road within <paramref name="r"/>
        /// of a point with the densest canopy either side (the canopy map), 80 m of it ahead.</summary>
        static bool T3Wooded(CityMap map, Vector2 p, float r, out CityMap.Edge edge, out float s)
        {
            edge = null; s = 0f;
            float best = 0f;
            foreach (var e in map.edges)
            {
                if (e.tunnel || e.bridge || e.link || e.cls < 1 || e.cls > 3 || e.length < 200f) continue;
                if (Vector2.Distance(e.PointAt(e.length * 0.5f), p) > r) continue;
                for (float at = 40f; at < e.length - 120f; at += 60f)
                {
                    float sc = 0f;
                    for (float a = 0f; a <= 80f; a += 40f)
                    {
                        var c = e.PointAt(at + a); var t = e.TangentAt(at + a); var n = new Vector2(-t.y, t.x);
                        sc += CityCanopy.Fraction(c.x + n.x * 25f, c.y + n.y * 25f) + CityCanopy.Fraction(c.x - n.x * 25f, c.y - n.y * 25f);
                    }
                    if (sc > best) { best = sc; edge = e; s = at; }
                }
            }
            return edge != null;
        }
    }
}
