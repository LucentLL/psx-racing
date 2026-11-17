using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE APPROACH IN CHARLOTTE, IN A SEASON (the tree-season round, owner
    /// 2026-11-16: "Drive around Charlotte in Winter"). RunTreeFeet's walk -
    /// the driver's eye toward a place of round 3's survey, the game's levels
    /// (a) and the levels of one step earlier (b) - from PSX_TM_FROM (400) to
    /// PSX_TM_TO (20) m every PSX_TM_STEP (10) m, in each dress asked for
    /// (PSX_TM_DRESSES, default winter and fall), with the tile ring the game
    /// stands up (PSX_TM_RING, default 4 = 9 x 9).
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunTreeSeason
    ///   PSX_TM_TAG=..  PSX_TM_PLACES=arterial,wtrade  PSX_TM_DETAIL=1
    /// Output: Screenshots/treeseason/&lt;tag&gt;/Charlotte_&lt;place&gt;_&lt;DRESS&gt;_&lt;d&gt;_a|b.png, treeseason_&lt;tag&gt;_Charlotte_log.txt
    /// </summary>
    public static partial class CityRefSpots
    {
        static string TsEnv(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        public static void RunTreeSeason()
        {
            string tag = TsEnv("PSX_TM_TAG", "walk");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
            Directory.CreateDirectory(Path.Combine(root, tag));
            var log = new StringBuilder($"TREE SEASON WALK Charlotte ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}; PSX_TREE_MATCH={TsEnv("PSX_TREE_MATCH", "(default)")}\n");
            int code = 0, shots = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                // the kit as this process's switches make it (PSX_TREE_MATCH=0: today's kit) unless told to take the one on disk
                if (TsEnv("PSX_TM_KEEPKIT", "0") != "1" && PSXRacingBuilder.EnsureCityTreeKit(true) == null) throw new System.Exception("no city tree kit (store files?)");
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
                ForestDetailPrefs.Override = Mathf.Clamp(int.Parse(TsEnv("PSX_TM_DETAIL", "1"), CultureInfo.InvariantCulture), 0, 2);
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                int nMatch = 0;
                foreach (var v in mt.Kit.variants) if (v != null && v.HasMatch) nMatch++;
                log.Append($"kit: {mt.Kit.variants.Length} species, {nMatch} with match cards; forest draws match cards for {mt.Forest.MatchCount}\n");
                float from = float.Parse(TsEnv("PSX_TM_FROM", "400"), CultureInfo.InvariantCulture), to = float.Parse(TsEnv("PSX_TM_TO", "20"), CultureInfo.InvariantCulture);
                float step = Mathf.Max(1f, float.Parse(TsEnv("PSX_TM_STEP", "10"), CultureInfo.InvariantCulture));
                int ring = Mathf.Clamp(int.Parse(TsEnv("PSX_TM_RING", "4"), CultureInfo.InvariantCulture), 1, 5);
                bool pair = TsEnv("PSX_TM_PAIR", "1") == "1";
                foreach (var pid in TsEnv("PSX_TM_PLACES", "arterial,wtrade").Split(','))
                {
                    int pi = System.Array.FindIndex(T3Places, q => q.id == pid);
                    if (pi < 0) { log.Append(pid + ": no such place\n"); continue; }
                    var pl = T3Places[pi];
                    CityMap.Edge e; float s; bool named = true;
                    if (pl.onewayOnly) { if (!T3SnapOneway(map, LL(pl.lat, pl.lon), pl.roads[0], pl.hdg, out e, out s)) { log.Append(pid + ": no divided piece\n"); continue; } }
                    else if (!CtSnap(map, LL(pl.lat, pl.lon), pl.roads, out e, out s, out named)) { log.Append(pid + ": no road near\n"); continue; }
                    var p0 = e.PointAt(s);
                    var anchor = new Vector3(p0.x, e.YAt(s), p0.y);
                    world.DropAll(); mt.Flush();
                    world.EnsureRing(anchor, 2); mt.Flush();
                    var cp = new CtPlace { id = pl.id, lat = pl.lat, lon = pl.lon, roads = pl.roads, hdg = pl.hdg, kind = pl.kind, what = pl.what };
                    if (!CtPose(cp, map, mt, e, s, out var eye, out var fwd, out _, out string how)) { log.Append($"{pid}: no pose ({how})\n"); continue; }
                    var f = new Vector3(fwd.x, 0f, fwd.z).normalized;
                    // the ring as the game holds it round the middle of the walk
                    world.EnsureRing(eye - f * (0.5f * (from + to)), ring); mt.Flush();
                    log.Append($"\n{pid}\t'{e.name}' {CityAudit.LatLon(eye.x, eye.z)}; ring {ring} ({mt.LiveTiles} tiles, {mt.LivePlants()} trees); {clock.Elapsed.TotalSeconds:0} s\n");
                    mt.Rebuild();
                    foreach (var ds in TsEnv("PSX_TM_DRESSES", "0,3").Split(','))
                    {
                        if (!int.TryParse(ds.Trim(), out int dress) || dress < 0 || dress >= Seasons.DressCount) continue;
                        string dn = Seasons.DressNames[dress];
                        FeetLight(sun, dressing, world);
                        SetDress(dressing, world, dress);
                        log.Append($"  {pid} {dn} {ForestDetailPrefs.Label}: levels {mt.Forest.lod0To:0} / {mt.Forest.lod1To:0} / {mt.Forest.meshRange:0} m, bands {mt.Forest.band0:0} / {mt.Forest.band1:0} / {mt.Forest.band2:0} m\n");
                        Vector3 prevEye = Vector3.zero;
                        for (float d = from + step; d >= to - 0.01f; d -= step)
                        {
                            var q = eye - f * d;
                            var pos = new Vector3(q.x, FeetGroundAt(q, eye.y - 1.15f) + 1.15f, q.z);
                            if (d > from + 0.01f) { prevEye = pos; continue; }
                            var rot = Quaternion.LookRotation(f + Vector3.down * 0.03f);
                            if (car != null) car.TeleportTo(pos - f * 6f - Vector3.up * 0.8f, Quaternion.LookRotation(f));
                            cam.transform.SetPositionAndRotation(pos, rot);
                            SunShadows.RenderFor(cam);
                            MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                            PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/Charlotte_{pid}_{dn}_{d:000}_a", pos, rot);
                            shots++;
                            if (pair)
                            {
                                MeshForest.LodEyeOn = true; MeshForest.LodEye = prevEye; mt.Forest.DrawNow(cam);
                                PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/Charlotte_{pid}_{dn}_{d:000}_b", pos, rot);
                                MeshForest.LodEyeOn = false;
                                shots++;
                            }
                            prevEye = pos;
                        }
                    }
                }
                log.Append($"\n{shots} frames; {clock.Elapsed.TotalSeconds:0} s\nTREE SEASON WALK OK\n");
            }
            catch (System.Exception ex) { log.Append("TREE SEASON WALK FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                ForestDetailPrefs.Override = -1;
                MeshForest.LodEyeOn = false;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_Charlotte_log.txt"), log.ToString());
                Debug.Log("[TreeSeasonWalk]\n" + log);
            }
            if (Application.isBatchMode && !TreeMatchAudit.NoExit) EditorApplication.Exit(code);
        }

        /// <summary>
        /// CHARLOTTE'S CARDS AGAINST ITS MESH TREES: from 60 m over a place of round 3's survey, the game's
        /// ring of tiles stood up, each dress - the trees drawn as the game draws them in the view (meshes,
        /// far cards) and as the tiles' own cards (what shows past the far range, outside the view's cone and
        /// until a tile is fitted). The foliage alone is measured (TreeMatchAudit.FoliageStat).
        ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunTreeCards   PSX_TM_TAG  PSX_TM_PLACES  PSX_TM_DRESSES
        /// Output: Screenshots/treeseason/&lt;tag&gt;/cards_Charlotte_&lt;place&gt;_&lt;DRESS&gt;_mesh|cards.png, treeseason_&lt;tag&gt;_Charlotte_cards.txt
        /// </summary>
        /// <summary>
        /// THE TREE MATCH'S COST IN CHARLOTTE: the city forest rebuilt with the packs' far sheets and with
        /// the match cards in turn (MeshForest.TreeMatch, CityMeshTrees.Rebuild), ABBA, from the driver's eye
        /// 150 m short of a place looking down the road and from 60 m over it (TreeMatchAudit.CostAB).
        ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunTreeMatchCost   PSX_TM_TAG  PSX_TM_PLACES  PSX_TM_DRESSES (one)
        /// Output: Screenshots/treeseason/treeseason_&lt;tag&gt;_Charlotte_cost.txt
        /// </summary>
        public static void RunTreeMatchCost()
        {
            string tag = TsEnv("PSX_TM_TAG", "cost");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
            Directory.CreateDirectory(root);
            var log = new StringBuilder($"TREE MATCH COST Charlotte ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}\n");
            int code = 0;
            bool keepMatch = MeshForest.TreeMatch;
            var restoreKnobs = TreeMatchAudit.RunKnobs();
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                if (player != null) player.SetActive(false);
                CityMeshTrees.ResetCounts();
                ForestDetailPrefs.Override = 1;
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                int dress = int.Parse(TsEnv("PSX_TM_DRESSES", "3").Split(',')[0], CultureInfo.InvariantCulture);
                int rounds = int.Parse(TsEnv("PSX_TM_ROUNDS", "4"), CultureInfo.InvariantCulture);
                MeshForest Mode(int m) { MeshForest.TreeMatch = m == 1; mt.Rebuild(); return mt.Forest; }
                foreach (var pid in TsEnv("PSX_TM_PLACES", "arterial").Split(','))
                {
                    int pi = System.Array.FindIndex(T3Places, q => q.id == pid);
                    if (pi < 0) { log.Append(pid + ": no such place\n"); continue; }
                    var pl = T3Places[pi];
                    CityMap.Edge e; float s; bool named = true;
                    if (pl.onewayOnly) { if (!T3SnapOneway(map, LL(pl.lat, pl.lon), pl.roads[0], pl.hdg, out e, out s)) { log.Append(pid + ": no divided piece\n"); continue; } }
                    else if (!CtSnap(map, LL(pl.lat, pl.lon), pl.roads, out e, out s, out named)) { log.Append(pid + ": no road near\n"); continue; }
                    var p0 = e.PointAt(s);
                    var anchor = new Vector3(p0.x, e.YAt(s), p0.y);
                    world.DropAll(); mt.Flush();
                    world.EnsureRing(anchor, 2); mt.Flush();
                    var cp = new CtPlace { id = pl.id, lat = pl.lat, lon = pl.lon, roads = pl.roads, hdg = pl.hdg, kind = pl.kind, what = pl.what };
                    if (!CtPose(cp, map, mt, e, s, out var eye, out var fwd, out _, out string how)) { log.Append($"{pid}: no pose ({how})\n"); continue; }
                    var f = new Vector3(fwd.x, 0f, fwd.z).normalized;
                    world.EnsureRing(eye, 4); mt.Flush();
                    mt.Rebuild();
                    FeetLight(sun, dressing, world);
                    SetDress(dressing, world, dress);
                    // (tree-cost round: TWO STANDING FORESTS swapped by SetActive - A with the packs' far sheets, B with
                    // the switches under test - not one forest rebuilt every round, which drifted the editor's frame
                    // by a millisecond a round. PSX_TM_TWIN=0: the rebuild as before. PSX_TM_CONTROL=1: B without the match.)
                    System.Func<int, MeshForest> mode = Mode;
                    MeshForest twinB = null, fA = null;
                    if (TsEnv("PSX_TM_TWIN", "1") != "0")
                    {
                        bool control = TsEnv("PSX_TM_CONTROL", "0") == "1";
                        // (PSX_TM_AB = match | full | ring | atlas: TreeMatchAudit.AbSet)
                        string abMode = TsEnv("PSX_TM_AB", "match");
                        bool keepCards = MeshForest.MatchCardsOn;
                        float otherKnob = MeshForest.OtherRangeM;
                        TreeMatchAudit.AbSet(abMode, 0, control, otherKnob); mt.Rebuild(); fA = mt.Forest;
                        TreeMatchAudit.AbSet(abMode, 1, control, otherKnob); twinB = mt.TwinForest();
                        MeshForest.TreeMatch = keepMatch; MeshForest.MatchCardsOn = keepCards; MeshForest.OtherRangeM = otherKnob;
                        if (fA != null && twinB != null)
                        {
                            log.Append($"  (two forests [{abMode}]: A {fA.MatchCount} match species, ring {fA.RingOn}, far {fA.farRange:0} m; B {twinB.MatchCount}, ring {twinB.RingOn}, far {twinB.farRange:0} m{(control ? " - CONTROL" : "")})\n");
                            var a = fA; var b = twinB;
                            mode = m => { a.gameObject.SetActive(m == 0); b.gameObject.SetActive(m == 1); return m == 0 ? a : b; };
                        }
                    }
                    try
                    {
                        cam.transform.SetPositionAndRotation(eye - f * 150f, Quaternion.LookRotation(f + Vector3.down * 0.03f));
                        log.Append(TreeMatchAudit.CostAB(cam, mode, rounds, $"Charlotte {pid} {Seasons.DressNames[dress]}, driver's eye 150 m short"));
                        cam.transform.SetPositionAndRotation(eye + Vector3.up * 60f, Quaternion.LookRotation((f + Vector3.down * 0.16f).normalized));
                        log.Append(TreeMatchAudit.CostAB(cam, mode, rounds, $"Charlotte {pid} {Seasons.DressNames[dress]}, 60 m over the road"));
                    }
                    finally
                    {
                        if (twinB != null) Object.DestroyImmediate(twinB.gameObject);
                        if (fA != null) fA.gameObject.SetActive(true);
                    }
                }
                log.Append("TREE MATCH COST OK\n");
            }
            catch (System.Exception ex) { log.Append("TREE MATCH COST FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                MeshForest.TreeMatch = keepMatch; restoreKnobs();
                ForestDetailPrefs.Override = -1;
                MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_Charlotte_cost.txt"), log.ToString());
                Debug.Log("[TreeMatchCost]\n" + log);
            }
            if (Application.isBatchMode && !TreeMatchAudit.NoExit) EditorApplication.Exit(code);
        }

        public static void RunTreeCards()
        {
            string tag = TsEnv("PSX_TM_TAG", "cards");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
            Directory.CreateDirectory(Path.Combine(root, tag));
            var log = new StringBuilder($"TREE CARDS v MESH TREES Charlotte ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}; PSX_TREE_MATCH={TsEnv("PSX_TREE_MATCH", "(default)")}, PSX_TREE_MATCH_CARDS={TsEnv("PSX_TREE_MATCH_CARDS", "(default)")}\n");
            int code = 0;
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                if (TsEnv("PSX_TM_KEEPKIT", "1") != "1" && PSXRacingBuilder.EnsureCityTreeKit(true) == null) throw new System.Exception("no city tree kit");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                CityMeshTrees.ResetCounts();
                ForestDetailPrefs.Override = 1;
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                log.Append($"forest draws match cards for {mt.Forest.MatchCount} species; the tiles' cards wear the kit's own trees: {mt.CardsMatched}\n");
                int ring = Mathf.Clamp(int.Parse(TsEnv("PSX_TM_RING", "4"), CultureInfo.InvariantCulture), 1, 5);
                foreach (var pid in TsEnv("PSX_TM_PLACES", "arterial,woodland").Split(','))
                {
                    int pi = System.Array.FindIndex(T3Places, q => q.id == pid);
                    if (pi < 0) { log.Append(pid + ": no such place\n"); continue; }
                    var pl = T3Places[pi];
                    CityMap.Edge e; float s; bool named = true;
                    if (pl.onewayOnly) { if (!T3SnapOneway(map, LL(pl.lat, pl.lon), pl.roads[0], pl.hdg, out e, out s)) { log.Append(pid + ": no divided piece\n"); continue; } }
                    else if (!CtSnap(map, LL(pl.lat, pl.lon), pl.roads, out e, out s, out named)) { log.Append(pid + ": no road near\n"); continue; }
                    var p0 = e.PointAt(s);
                    var anchor = new Vector3(p0.x, e.YAt(s), p0.y);
                    world.DropAll(); mt.Flush();
                    world.EnsureRing(anchor, 2); mt.Flush();
                    var cp = new CtPlace { id = pl.id, lat = pl.lat, lon = pl.lon, roads = pl.roads, hdg = pl.hdg, kind = pl.kind, what = pl.what };
                    if (!CtPose(cp, map, mt, e, s, out var eye, out var fwd, out _, out string how)) { log.Append($"{pid}: no pose ({how})\n"); continue; }
                    var f = new Vector3(fwd.x, 0f, fwd.z).normalized;
                    world.EnsureRing(eye, ring); mt.Flush();
                    mt.Rebuild();
                    var pos = eye + Vector3.up * 60f;
                    var rot = Quaternion.LookRotation((f + Vector3.down * 0.16f).normalized);
                    foreach (var ds in TsEnv("PSX_TM_DRESSES", "0,3").Split(','))
                    {
                        if (!int.TryParse(ds.Trim(), out int dress) || dress < 0 || dress >= Seasons.DressCount) continue;
                        string dn = Seasons.DressNames[dress];
                        FeetLight(sun, dressing, world);
                        SetDress(dressing, world, dress);
                        cam.transform.SetPositionAndRotation(pos, rot);
                        SunShadows.RenderFor(cam);
                        MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                        PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/cards_Charlotte_{pid}_{dn}_mesh", pos, rot);
                        mt.Forest.DrawNow(cam);
                        string a = TreeMatchAudit.FoliageStat(cam, CityTrees.FoliageLayer, out float cA, out float hA, out float sA, out float lA);
                        MeshForest.MeshTreesOn = false; mt.Forest.CardsNow();
                        PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/cards_Charlotte_{pid}_{dn}_cards", pos, rot);
                        string b = TreeMatchAudit.FoliageStat(cam, CityTrees.FoliageLayer, out float cB, out float hB, out float sB, out float lB);
                        MeshForest.MeshTreesOn = true;
                        log.Append($"  Charlotte {pid} {dn}: mesh trees {a} | cards {b} -> {TreeMatchAudit.TwoWays(cA, hA, sA, lA, cB, hB, sB, lB)}\n");
                    }
                }
                log.Append("TREE CARDS OK\n");
            }
            catch (System.Exception ex) { log.Append("TREE CARDS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                ForestDetailPrefs.Override = -1;
                MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_Charlotte_cards.txt"), log.ToString());
                Debug.Log("[TreeCards]\n" + log);
            }
            if (Application.isBatchMode && !TreeMatchAudit.NoExit) EditorApplication.Exit(code);
        }

        /// <summary>
        /// CHARLOTTE'S FAR RING AND MIRRORS (tree-cost round, 2026-11-17): two standing pooled forests over the same
        /// tiles, both with the tree match - A with the tiles' cards where the forest does not draw (PSX_TREE_MATCH_CARDS
        /// off: past the 1.2 km far range, out of the view's cone), B with the trees' own far cards there
        /// (MeshForest.RingOn: the far range out to the live ring, the out-of-cone tiles' trees in the other cameras'
        /// list) - each dress:
        ///   MIRROR: the driver's eye 150 m short of the place looking BACK, as an OTHER camera after the forward cull,
        ///     against the TRUTH (the same camera as the main view);
        ///   FAR RING: 60 m over the road with the near clip at 1.2 km (only what stands past the old far range), A
        ///     against B; the full view shot too;
        ///   the mirror camera's render time, A against B.
        ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunCityRing   PSX_TM_TAG  PSX_TM_PLACES  PSX_TM_DRESSES
        /// Output: Screenshots/treeseason/&lt;tag&gt;/ring_Charlotte_&lt;place&gt;_&lt;DRESS&gt;_*.png, treeseason_&lt;tag&gt;_Charlotte_ring.txt
        /// </summary>
        public static void RunCityRing()
        {
            string tag = TsEnv("PSX_TM_TAG", "ring");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason");
            Directory.CreateDirectory(Path.Combine(root, tag));
            var log = new StringBuilder($"TREE FAR RING AND MIRRORS Charlotte ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}\n");
            int code = 0;
            bool keepMatch = MeshForest.TreeMatch, keepCards = MeshForest.MatchCardsOn;
            var restoreKnobs = TreeMatchAudit.RunKnobs();
            GameObject rearGo = null;
            RenderTexture fwdRt = null;
            MeshForest twinB = null, fA = null;
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                if (player != null) player.SetActive(false);
                CityMeshTrees.ResetCounts();
                ForestDetailPrefs.Override = 1;
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                cam.aspect = 16f / 9f;
                rearGo = new GameObject("TreeCostMirror") { hideFlags = HideFlags.HideAndDontSave };
                var rear = rearGo.AddComponent<Camera>();
                rear.CopyFrom(cam);
                rear.enabled = false;
                rear.fieldOfView = float.Parse(TsEnv("PSX_TM_MIRRORFOV", "40"), CultureInfo.InvariantCulture);
                rear.aspect = 16f / 9f;
                fwdRt = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
                var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                var keepTarget = cam.targetTexture;
                void Forward(MeshForest f) { f.DrawNow(cam, true); cam.targetTexture = fwdRt; cam.Render(); cam.targetTexture = keepTarget; }
                int rounds = int.Parse(TsEnv("PSX_TM_ROUNDS", "4"), CultureInfo.InvariantCulture);
                foreach (var pid in TsEnv("PSX_TM_PLACES", "arterial").Split(','))
                {
                    int pi = System.Array.FindIndex(T3Places, q => q.id == pid);
                    if (pi < 0) { log.Append(pid + ": no such place\n"); continue; }
                    var pl = T3Places[pi];
                    CityMap.Edge e; float s; bool named = true;
                    if (pl.onewayOnly) { if (!T3SnapOneway(map, LL(pl.lat, pl.lon), pl.roads[0], pl.hdg, out e, out s)) { log.Append(pid + ": no divided piece\n"); continue; } }
                    else if (!CtSnap(map, LL(pl.lat, pl.lon), pl.roads, out e, out s, out named)) { log.Append(pid + ": no road near\n"); continue; }
                    var p0 = e.PointAt(s);
                    var anchor = new Vector3(p0.x, e.YAt(s), p0.y);
                    world.DropAll(); mt.Flush();
                    world.EnsureRing(anchor, 2); mt.Flush();
                    var cp = new CtPlace { id = pl.id, lat = pl.lat, lon = pl.lon, roads = pl.roads, hdg = pl.hdg, kind = pl.kind, what = pl.what };
                    if (!CtPose(cp, map, mt, e, s, out var eye, out var fwd, out _, out string how)) { log.Append($"{pid}: no pose ({how})\n"); continue; }
                    var fdir = new Vector3(fwd.x, 0f, fwd.z).normalized;
                    world.EnsureRing(eye, int.Parse(TsEnv("PSX_TM_RING", "4"), CultureInfo.InvariantCulture)); mt.Flush();
                    string abMode = TsEnv("PSX_TM_AB", "ring");
                    float otherKnob = MeshForest.OtherRangeM;
                    TreeMatchAudit.AbSet(abMode, 0, false, otherKnob); mt.Rebuild(); fA = mt.Forest;
                    TreeMatchAudit.AbSet(abMode, 1, false, otherKnob); twinB = mt.TwinForest();
                    MeshForest.TreeMatch = keepMatch; MeshForest.MatchCardsOn = keepCards; MeshForest.OtherRangeM = otherKnob;
                    if (fA == null || twinB == null) throw new System.Exception("no twin forest");
                    // (atlas: the tiles' cards in the kit's own trees on both sides - CityMeshTrees' swap is the city's, not a forest's)
                    if (abMode == "atlas" || abMode == "full") mt.SetCardAtlas(true);
                    log.Append($"  {pid}: A ring {fA.RingOn}, far {fA.farRange:0} m; B ring {twinB.RingOn}, far {twinB.farRange:0} m, other cameras' trees to {MeshForest.OtherRangeM:0} m; live tiles {mt.LiveTiles}\n");
                    var a = fA; var b = twinB;
                    MeshForest Mode(int m) { a.gameObject.SetActive(m == 0); b.gameObject.SetActive(m == 1); return m == 0 ? a : b; }
                    var eyeD = eye;   // (the driver's eye at the place: 150 m short can stand under a rise or a deck)
                    var rotF = Quaternion.LookRotation((fdir + Vector3.down * 0.03f).normalized);
                    var rotR = Quaternion.LookRotation((-fdir + Vector3.down * 0.03f).normalized);
                    var posV = eye + Vector3.up * 60f;
                    var rotV = Quaternion.LookRotation((fdir + Vector3.down * 0.06f).normalized);
                    foreach (var ds in TsEnv("PSX_TM_DRESSES", "0,3").Split(','))
                    {
                        if (!int.TryParse(ds.Trim(), out int dress) || dress < 0 || dress >= Seasons.DressCount) continue;
                        string dn = Seasons.DressNames[dress];
                        FeetLight(sun, dressing, world);
                        SetDress(dressing, world, dress);
                        MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                        // MIRROR
                        cam.transform.SetPositionAndRotation(eyeD, rotF);
                        rear.transform.SetPositionAndRotation(eyeD, rotR);
                        var st = new (float c, float h, float s, float l, string txt)[3];
                        for (int m = 0; m < 2; m++)
                        {
                            var f = Mode(m);
                            Forward(f); f.DrawOtherNow(rear);
                            PSXScreenshotTool.ShotAs(rear, $"treeseason/{tag}/ring_Charlotte_{pid}_{dn}_mirror_{(m == 0 ? "A" : "B")}", eyeD, rotR);
                            Forward(f); f.DrawOtherNow(rear);
                            st[m].txt = TreeMatchAudit.FoliageStat(rear, CityTrees.FoliageLayer, out st[m].c, out st[m].h, out st[m].s, out st[m].l);
                            Forward(f);
                            string fl = f.DebugOther(out long oc);
                            log.Append($"  Charlotte {pid} {dn} mirror {(m == 0 ? "A (tile cards)" : "B (far cards)")}: {st[m].txt}; other cameras' list {oc} far cards; {fl}\n");
                        }
                        var fb = Mode(1);
                        fb.DrawNow(rear, true);
                        PSXScreenshotTool.ShotAs(rear, $"treeseason/{tag}/ring_Charlotte_{pid}_{dn}_mirror_truth", eyeD, rotR);
                        fb.DrawNow(rear, true);
                        st[2].txt = TreeMatchAudit.FoliageStat(rear, CityTrees.FoliageLayer, out st[2].c, out st[2].h, out st[2].s, out st[2].l);
                        log.Append($"  Charlotte {pid} {dn} mirror TRUTH (the trees turned round): {st[2].txt}\n");
                        for (int m = 0; m < 2; m++)
                            log.Append($"    {(m == 0 ? "A" : "B")} against the truth: cover {st[m].c / Mathf.Max(1e-5f, st[2].c):0.00} x -> {TreeMatchAudit.TwoWays(st[m].c, st[m].h, st[m].s, st[m].l, st[2].c, st[2].h, st[2].s, st[2].l)}\n");
                        // THE FAR WOODS: past the city's far plane (~1 km: CityWorld's RingFarM) nothing of the tiles is drawn;
                        // the woods there are CityFar's cards (drawn through the skyline's squeeze), wearing the city kit's card
                        // material: the retro atlas, or with the cards matched (CityMeshTrees.SetCardAtlas) the kit's own trees.
                        // From 60 m over the road; CityFar's trees alone measured (put on a spare layer for it).
                        var farTrees = new List<Renderer>();
                        var cfar = Object.FindAnyObjectByType<CityFar>();
                        if (cfar != null) foreach (var r in cfar.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.name.StartsWith("CityFarTree")) farTrees.Add(r);
                        const int SpareL = 30;
                        var keepL = new int[farTrees.Count];
                        for (int i = 0; i < farTrees.Count; i++) { keepL[i] = farTrees[i].gameObject.layer; }
                        var fs = new (float c, float h, float s, float l, string txt)[2];
                        var fview = Mode(1);
                        for (int atl = 0; atl < 2; atl++)
                        {
                            mt.SetCardAtlas(atl == 1);
                            cam.transform.SetPositionAndRotation(posV, rotV);
                            world.RefreshSkyline(cam);   // (CityFar's trees for this eye, in the material of the moment)
                            fview.DrawNow(cam, true);
                            PSXScreenshotTool.ShotAs(cam, $"treeseason/{tag}/ring_Charlotte_{pid}_{dn}_view_atlas{atl}", posV, rotV);
                            for (int i = 0; i < farTrees.Count; i++) farTrees[i].gameObject.layer = SpareL;
                            try
                            {
                                fview.DrawNow(cam, true);
                                fs[atl].txt = TreeMatchAudit.FoliageStat(cam, SpareL, out fs[atl].c, out fs[atl].h, out fs[atl].s, out fs[atl].l);
                            }
                            finally { for (int i = 0; i < farTrees.Count; i++) farTrees[i].gameObject.layer = keepL[i]; }
                            log.Append($"  Charlotte {pid} {dn} the far woods (CityFar, {farTrees.Count} renderers) {(atl == 0 ? "retro atlas" : "the kit's own trees")}: {fs[atl].txt}\n");
                        }
                        mt.SetCardAtlas(abMode == "atlas" || abMode == "full");
                        log.Append($"    the kit's trees against the retro atlas: cover {fs[1].c / Mathf.Max(1e-5f, fs[0].c):0.00} x\n");
                        // THE MIRROR'S COST, at the mirrors' and a reflection face's own sizes
                        cam.transform.SetPositionAndRotation(eyeD, rotF);
                        rear.transform.SetPositionAndRotation(eyeD, rotR);
                        log.Append(TreeMatchAudit.MirrorCost(rear, Mode, Forward, px, rounds));
                    }
                    Object.DestroyImmediate(twinB.gameObject); twinB = null;
                    fA.gameObject.SetActive(true);
                }
                Object.DestroyImmediate(px);
                log.Append("TREE FAR RING AND MIRRORS OK\n");
            }
            catch (System.Exception ex) { log.Append("TREE FAR RING AND MIRRORS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                if (CityMeshTrees.Instance != null) CityMeshTrees.Instance.SetCardAtlas(false);
                if (twinB != null) Object.DestroyImmediate(twinB.gameObject);
                if (fA != null) fA.gameObject.SetActive(true);
                if (rearGo != null) Object.DestroyImmediate(rearGo);
                if (fwdRt != null) Object.DestroyImmediate(fwdRt);
                MeshForest.TreeMatch = keepMatch; MeshForest.MatchCardsOn = keepCards; restoreKnobs();
                ForestDetailPrefs.Override = -1;
                MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true;
                File.WriteAllText(Path.Combine(root, $"treeseason_{tag}_Charlotte_ring.txt"), log.ToString());
                Debug.Log("[TreeRing]\n" + log);
            }
            if (Application.isBatchMode && !TreeMatchAudit.NoExit) EditorApplication.Exit(code);
        }
    }
}
