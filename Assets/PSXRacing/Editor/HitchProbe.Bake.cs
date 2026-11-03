using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE BAKED CITY'S GATE (PSX_HITCH_MODE=bake; phase 1 of Forza-style
    /// streaming, 2026-11-02). The bake is a cache of the generator, so a tile
    /// stood up from it must be the tile the generator builds, bit for bit:
    /// every tile of the probe's routes (PSX_HITCH_ROUTES, in the order a car
    /// meets them) is stood up through CityWorld.EnsureTile twice - BUILT LIVE
    /// (the bake off), then BAKED (read, made, stood up from its file) - each
    /// pass with the same 9-tile window, its signs and poles decided and its
    /// trees planted, and compared:
    ///   data   - every field and mesh of the TileMeshes (CityTileCache.DataHash);
    ///   ground - the tile's lattice once its stand-up and its planting read it;
    ///   trees  - (format 2, city-tree-bake) the tile's TREE FRAME as it is about
    ///            to stand up: PLANTED here in the built pass, read off the bake
    ///            (planted by the bake tool) in the baked pass - everything the
    ///            file carries and the trees' mesh (CityTileCache.TreeHash; a
    ///            difference names its part: trees, under, poles, signals, signs);
    ///   whole  - every mesh under the stood-up tile (FullSig: the tile's, its
    ///            trees', poles', signals' and signs', every prop model's place).
    /// Every sign and pole is decided afresh in each pass (their caches cleared),
    /// in the order the pass stands the tiles; the bake decided them in rows - so
    /// a baked frame that depended on which tile asked first would show here.
    /// Any difference fails, and so does a baked tile that planted its frame.
    /// The full bake is used when it is fresh and holds every route tile; else
    /// the gate bakes the route tiles (in rows) into CityTiles_gate.
    /// Report: PSXRacing_hitch_TAG.txt, the last line BAKE GATE PASS / FAIL.
    /// </summary>
    public static partial class HitchProbe
    {
        static void BakeGate()
        {
            var map = CityMap.Get();
            if (map == null) { L("no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            var routes = new List<(string name, List<Vector2> line)>();
            foreach (var id in Env("PSX_HITCH_ROUTES", "uptown,tryon,i77").Split(','))
            {
                if (id == "i77") routes.Add(("i77", FollowRoad(map, LL(35.2700, -80.8520), LL(35.1900, -80.8800), "I-77", 9000f)));
                else routes.Add((id, RouteLine(map, id)));
            }
            int maxTiles = (int)EnvF("PSX_HITCH_MAXTILES", 70);
            var order = new List<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            foreach (var (name, line) in routes)
            {
                var o = TileOrder(line, maxTiles);
                int fresh = 0;
                foreach (var k in o) if (seen.Add(k)) { order.Add(k); fresh++; }
                L($"route {name}: {o.Count} tiles ({fresh} not on an earlier route)");
            }

            // the bake: the full one if it is fresh and holds every route tile
            string dir = CityTileCache.DefaultDir();
            bool full = CityTileBake.IsFresh(dir, out var man, out string why);
            L("full bake at " + dir + ": " + why);
            if (full)
            {
                var have = new HashSet<Vector2Int>();
                foreach (var t in man.tiles) have.Add(new Vector2Int(t.tx, t.tz));
                foreach (var k in order) if (!have.Contains(k)) { full = false; L($"  ...but it lacks route tile {k.x},{k.y}"); break; }
            }
            if (!full || Env("PSX_HITCH_REBAKE", "0") == "1")
            {
                dir = Path.Combine(ProjectDir, CityTileCache.FolderName + "_gate");
                // in ROWS, as the full bake goes - not in the order the passes stand
                // the tiles, so another tile than in the passes decides each sign first
                var rows = new List<Vector2Int>(order);
                rows.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
                bool ok = CityTileBake.Bake(dir, rows, out string rep);
                L("gate bake: " + rep);
                if (!ok) { L("BAKE GATE FAIL (the bake failed)"); return; }
            }

            var go = new GameObject("~bakeGate");
            var world = go.AddComponent<CityWorld>();
            world.ring = 2;
            var liveField = typeof(CityWorld).GetField("live", NP);
            var dropTile = typeof(CityWorld).GetMethod("DropTile", NP);
            var res = new Dictionary<Vector2Int, (string data, string lat, string trees, string whole, float ms)>[2];
            res[0] = new Dictionary<Vector2Int, (string data, string lat, string trees, string whole, float ms)>();
            res[1] = new Dictionary<Vector2Int, (string data, string lat, string trees, string whole, float ms)>();
            var parts = new Dictionary<Vector2Int, string>[] { new Dictionary<Vector2Int, string>(), new Dictionary<Vector2Int, string>() };
            var treeMs = new List<float>[] { new List<float>(), new List<float>() };
            bool wasEnabled = CityTileCache.Enabled, wasForce = CityTileCache.Force;
            string wasDir = System.Environment.GetEnvironmentVariable("PSX_TILECACHE_DIR");
            int baked = 0, latExtra = 0, bakedTrees = 0, plantedBaked = 0, plantReads = 0, plantMissing = 0, plantWrong = 0;
            int[] frames = new int[2];
            var builtLattice = new Dictionary<Vector2Int, Dictionary<long, float>>();
            var standLattice = new Dictionary<Vector2Int, Dictionary<long, float>>();
            var slowTrees = new[] { new List<(float ms, Vector2Int k)>(), new List<(float ms, Vector2Int k)>() };
            // the tree frame as it is about to stand up (planted, or off the bake)
            long nowKey = long.MinValue;
            string nowTrees = null, nowParts = null;
            System.Action<long, CityTrees.TreeTile> onFrame = (k, tt) =>
            {
                if (k != nowKey) return;
                nowTrees = CityTileCache.TreeHash(tt);
                nowParts = CityTileCache.TreeHashParts(tt);
            };
            CityWorld.TreeFrameReady += onFrame;
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_TILECACHE_DIR", dir);
                CityTileCache.Force = true;
                // warm-up as BuildPart's: JIT and the static caches, built live
                CityTileCache.Enabled = false; CityTileCache.Reset();
                world.EnsureTile(Mathf.FloorToInt(map.uptown.x / CityMeshes.TileSize), Mathf.FloorToInt(map.uptown.y / CityMeshes.TileSize));
                world.DropAll();
                for (int pass = 0; pass < 2; pass++)
                {
                    CityTileCache.Enabled = pass == 1;
                    CityTileCache.Reset();
                    if (pass == 1 && !CityTileCache.On) { L("BAKE GATE FAIL: the bake did not open - " + CityTileCache.Why); return; }
                    if (pass == 1 && !CityTileCache.TreesOn) L("  (the bake's tree frames are not used: " + CityTileCache.Why + ")");
                    // every sign and pole decided afresh, in this pass's order (the bake's were in rows)
                    CitySigns.ClearCache();
                    CityPoles.ClearCache();
                    int stood0 = CityWorld.BakedStood, trees0 = CityWorld.BakedTreesStood;
                    var window = new List<long>();
                    foreach (var k in order)
                    {
                        long key = ((long)k.x << 24) ^ (k.y & 0xFFFFFF);
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        world.EnsureTile(k.x, k.y);
                        float ms = (float)clock.Elapsed.TotalMilliseconds;
                        string data = "-", lat = "-", whole = "-";
                        Dictionary<long, float> lattice = null;
                        bool bakedT = false;
                        if (world.TreesWaiting(key, out var wtm, out var wlat, out bakedT))
                        {
                            data = CityTileCache.DataHash(wtm);
                            lattice = wlat;
                            frames[pass]++;
                        }
                        // (built: the lattice as its stand-up left it - what a baked tile reads)
                        if (pass == 0 && lattice != null) standLattice[k] = new Dictionary<long, float>(lattice);
                        nowKey = key; nowTrees = null; nowParts = null;
                        clock.Restart();
                        // a baked frame decides nothing: only a planted one has signs and poles to decide
                        if (!bakedT) for (int g = 0; g < 400 && world.PrepareSigns(key) != 0; g++) { }
                        if (pass == 1 && !bakedT && data != "-") plantedBaked++;
                        world.PlantTrees(key);
                        if (data != "-")
                        {
                            float tms = (float)clock.Elapsed.TotalMilliseconds;
                            treeMs[pass].Add(tms);
                            slowTrees[pass].Add((tms, k));
                        }
                        string trees = nowTrees ?? "-";
                        if (nowParts != null) parts[pass][k] = nowParts;
                        // THE GROUND. Built: the lattice its build and stand-up read (kept
                        // before the planting) and the planting's reads past it. Baked: every
                        // corner the built stand-up read must be in the bake, bit for bit -
                        // a baked tile reads its lattice for its stand-up and nothing else
                        // (its frame is planted already). The built planting's own reads are
                        // counted, not required: only a tile planted live (the A/B) reads them.
                        if (lattice != null)
                        {
                            if (pass == 0) { builtLattice[k] = new Dictionary<long, float>(lattice); lat = "built " + (standLattice.TryGetValue(k, out var s0) ? s0.Count : -1); }
                            else if (standLattice.TryGetValue(k, out var sl))
                            {
                                int missing = 0, wrong = 0;
                                foreach (var kv in sl)
                                {
                                    if (!lattice.TryGetValue(kv.Key, out float y)) missing++;
                                    else if (System.BitConverter.SingleToInt32Bits(y) != System.BitConverter.SingleToInt32Bits(kv.Value)) wrong++;
                                }
                                lat = missing == 0 && wrong == 0 ? "built " + sl.Count : $"{missing} corners missing, {wrong} differ of {sl.Count} (baked {lattice.Count})";
                                latExtra += lattice.Count - sl.Count;
                                if (builtLattice.TryGetValue(k, out var bl))
                                    foreach (var kv in bl)
                                    {
                                        if (sl.ContainsKey(kv.Key)) continue;
                                        plantReads++;
                                        if (!lattice.TryGetValue(kv.Key, out float y)) plantMissing++;
                                        else if (System.BitConverter.SingleToInt32Bits(y) != System.BitConverter.SingleToInt32Bits(kv.Value)) plantWrong++;
                                    }
                            }
                        }
                        if (liveField.GetValue(world) is IDictionary live && live.Contains(key))
                        {
                            var tile = live[key];
                            whole = FullSig(tile.GetType().GetField("go").GetValue(tile) as GameObject);
                        }
                        res[pass][k] = (data, lat, trees, whole, ms);
                        window.Add(key);
                        while (window.Count > 9) { dropTile.Invoke(world, new object[] { window[0] }); window.RemoveAt(0); }
                    }
                    world.DropAll();
                    if (pass == 1) { baked = CityWorld.BakedStood - stood0; bakedTrees = CityWorld.BakedTreesStood - trees0; }
                }
            }
            finally
            {
                CityWorld.TreeFrameReady -= onFrame;
                System.Environment.SetEnvironmentVariable("PSX_TILECACHE_DIR", wasDir);
                CityTileCache.Enabled = wasEnabled; CityTileCache.Force = wasForce; CityTileCache.Reset();
                world.DropAll();
                Object.DestroyImmediate(go);
            }

            int equal = 0, differ = 0, dData = 0, dLat = 0, dTrees = 0, dWhole = 0;
            var liveMs = new List<float>(); var bakedMs = new List<float>();
            var partDiff = new Dictionary<string, int>();
            foreach (var k in order)
            {
                if (!res[0].TryGetValue(k, out var a) || !res[1].TryGetValue(k, out var b)) continue;
                liveMs.Add(a.ms); bakedMs.Add(b.ms);
                bool sd = a.data == b.data, sl = a.lat == b.lat, st = a.trees == b.trees, sw = a.whole == b.whole;
                if (sd && sl && st && sw && a.whole != "-") { equal++; continue; }
                differ++;
                if (!sd) dData++;
                if (!sl) dLat++;
                if (!sw) dWhole++;
                string partNote = "";
                if (!st)
                {
                    dTrees++;
                    // which parts: the per-part hashes side by side
                    parts[0].TryGetValue(k, out var pa); parts[1].TryGetValue(k, out var pb);
                    if (pa != null && pb != null)
                    {
                        var xa = pa.Split(' '); var xb = pb.Split(' ');
                        for (int i = 0; i < xa.Length && i < xb.Length; i++)
                            if (xa[i] != xb[i])
                            {
                                string nm = xa[i].Split('=')[0];
                                partDiff[nm] = partDiff.TryGetValue(nm, out int c) ? c + 1 : 1;
                                partNote += " " + nm;
                            }
                    }
                }
                if (differ <= 25)
                    L($"  DIFFERS tile {k.x},{k.y}:{(sd ? "" : $" data built {a.data} / baked {b.data}")}{(sl ? "" : $" ground built {a.lat} / baked {b.lat}")}" +
                      $"{(st ? "" : $" trees planted {a.trees} / baked {b.trees} (differ in:{partNote})")}{(sw ? "" : $" whole built {a.whole} / baked {b.whole}")}");
            }
            liveMs.Sort(); bakedMs.Sort(); treeMs[0].Sort(); treeMs[1].Sort();
            L($"tile now (EnsureTile, main thread): built p50 {P(liveMs, 50):0.0} p95 {P(liveMs, 95):0.0} max {P(liveMs, 100):0.0} ms / baked (read + make + stand-up, colliders cooked here) p50 {P(bakedMs, 50):0.0} p95 {P(bakedMs, 95):0.0} max {P(bakedMs, 100):0.0} ms");
            L($"tree frame now (main thread, one go): planted (signs and poles decided, then planted, stood up) p50 {P(treeMs[0], 50):0.0} p95 {P(treeMs[0], 95):0.0} max {P(treeMs[0], 100):0.0} ms / " +
              $"baked (meshes made, stood up) p50 {P(treeMs[1], 50):0.0} p95 {P(treeMs[1], 95):0.0} max {P(treeMs[1], 100):0.0} ms");
            for (int pass = 0; pass < 2; pass++)
            {
                slowTrees[pass].Sort((x, y) => y.ms.CompareTo(x.ms));
                var sw = new System.Text.StringBuilder(pass == 0 ? "  slowest planted tree frames:" : "  slowest baked tree frames:");
                for (int i = 0; i < Mathf.Min(6, slowTrees[pass].Count); i++) sw.Append($" {slowTrees[pass][i].k.x},{slowTrees[pass][i].k.y} {slowTrees[pass][i].ms:0.0} ms;");
                L(sw.ToString());
            }
            L($"{baked} of {order.Count} tiles stood up from the bake in the baked pass; tree frames: {frames[0]} planted in the built pass, {bakedTrees} stood up from the bake in the baked pass ({plantedBaked} planted there instead); the baked lattices hold {latExtra} corners more than the built stand-ups read");
            L($"  the built plantings read {plantReads} corners past their stand-ups (a baked frame reads none): the bake holds all but {plantMissing} of them, {plantWrong} differ");
            if (partDiff.Count > 0)
            {
                var pd = new System.Text.StringBuilder("tree frames differing, by part:");
                foreach (var kv in partDiff) pd.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value);
                L(pd.ToString());
            }
            L($"BAKE GATE: {order.Count} tiles, {equal} EQUAL, {differ} DIFFER (data {dData}, ground {dLat}, trees {dTrees}, whole {dWhole})");
            L(differ == 0 && equal == order.Count && baked == order.Count && bakedTrees == frames[0] && plantedBaked == 0 ? "BAKE GATE PASS" : "BAKE GATE FAIL");
        }
    }
}
