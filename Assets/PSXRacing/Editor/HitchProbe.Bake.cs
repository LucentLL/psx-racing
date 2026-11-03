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
    ///   whole  - every mesh under the stood-up tile (FullSig: the tile's, its
    ///            trees', poles', signals' and signs', every prop model's place).
    /// Any difference fails. The full bake is used when it is fresh and holds
    /// every route tile; else the gate bakes the route tiles into CityTiles_gate.
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
                bool ok = CityTileBake.Bake(dir, order, out string rep);
                L("gate bake: " + rep);
                if (!ok) { L("BAKE GATE FAIL (the bake failed)"); return; }
            }

            var go = new GameObject("~bakeGate");
            var world = go.AddComponent<CityWorld>();
            world.ring = 2;
            var liveField = typeof(CityWorld).GetField("live", NP);
            var pendField = typeof(CityWorld).GetField("treesPending", NP);
            var dropTile = typeof(CityWorld).GetMethod("DropTile", NP);
            var res = new Dictionary<Vector2Int, (string data, string lat, string whole, float ms)>[2];
            res[0] = new Dictionary<Vector2Int, (string data, string lat, string whole, float ms)>();
            res[1] = new Dictionary<Vector2Int, (string data, string lat, string whole, float ms)>();
            bool wasEnabled = CityTileCache.Enabled, wasForce = CityTileCache.Force;
            string wasDir = System.Environment.GetEnvironmentVariable("PSX_TILECACHE_DIR");
            int baked = 0, latExtra = 0;
            var builtLattice = new Dictionary<Vector2Int, Dictionary<long, float>>();
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
                    int stood0 = CityWorld.BakedStood;
                    var window = new List<long>();
                    foreach (var k in order)
                    {
                        long key = ((long)k.x << 24) ^ (k.y & 0xFFFFFF);
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        world.EnsureTile(k.x, k.y);
                        float ms = (float)clock.Elapsed.TotalMilliseconds;
                        string data = "-", lat = "-", whole = "-";
                        Dictionary<long, float> lattice = null;
                        if (pendField.GetValue(world) is IDictionary pend && pend.Contains(key))
                        {
                            var job = ((CityMeshes.TileMeshes tm, Dictionary<long, float> lattice))pend[key];
                            data = CityTileCache.DataHash(job.tm);
                            lattice = job.lattice;
                        }
                        for (int g = 0; g < 400 && world.PrepareSigns(key) != 0; g++) { }
                        world.PlantTrees(key);
                        // the lattice once the planting has read it too. Built: kept; baked:
                        // every corner the built tile read must be in it, bit for bit (the
                        // bake may hold a few more - sign sites another order decided on it)
                        if (lattice != null)
                        {
                            if (pass == 0) { builtLattice[k] = new Dictionary<long, float>(lattice); lat = "built " + lattice.Count; }
                            else if (builtLattice.TryGetValue(k, out var bl))
                            {
                                int missing = 0, wrong = 0;
                                foreach (var kv in bl)
                                {
                                    if (!lattice.TryGetValue(kv.Key, out float y)) missing++;
                                    else if (System.BitConverter.SingleToInt32Bits(y) != System.BitConverter.SingleToInt32Bits(kv.Value)) wrong++;
                                }
                                lat = missing == 0 && wrong == 0 ? "built " + bl.Count : $"{missing} corners missing, {wrong} differ of {bl.Count} (baked {lattice.Count})";
                                latExtra += lattice.Count - bl.Count;
                            }
                        }
                        if (liveField.GetValue(world) is IDictionary live && live.Contains(key))
                        {
                            var tile = live[key];
                            whole = FullSig(tile.GetType().GetField("go").GetValue(tile) as GameObject);
                        }
                        res[pass][k] = (data, lat, whole, ms);
                        window.Add(key);
                        while (window.Count > 9) { dropTile.Invoke(world, new object[] { window[0] }); window.RemoveAt(0); }
                    }
                    world.DropAll();
                    if (pass == 1) baked = CityWorld.BakedStood - stood0;
                }
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("PSX_TILECACHE_DIR", wasDir);
                CityTileCache.Enabled = wasEnabled; CityTileCache.Force = wasForce; CityTileCache.Reset();
                world.DropAll();
                Object.DestroyImmediate(go);
            }

            int equal = 0, differ = 0, dData = 0, dLat = 0, dWhole = 0;
            var liveMs = new List<float>(); var bakedMs = new List<float>();
            foreach (var k in order)
            {
                if (!res[0].TryGetValue(k, out var a) || !res[1].TryGetValue(k, out var b)) continue;
                liveMs.Add(a.ms); bakedMs.Add(b.ms);
                bool sd = a.data == b.data, sl = a.lat == b.lat, sw = a.whole == b.whole;
                if (sd && sl && sw && a.whole != "-") { equal++; continue; }
                differ++;
                if (!sd) dData++;
                if (!sl) dLat++;
                if (!sw) dWhole++;
                if (differ <= 25)
                    L($"  DIFFERS tile {k.x},{k.y}:{(sd ? "" : $" data built {a.data} / baked {b.data}")}{(sl ? "" : $" ground built {a.lat} / baked {b.lat}")}{(sw ? "" : $" whole built {a.whole} / baked {b.whole}")}");
            }
            liveMs.Sort(); bakedMs.Sort();
            L($"tile now (EnsureTile, main thread): built p50 {P(liveMs, 50):0.0} p95 {P(liveMs, 95):0.0} max {P(liveMs, 100):0.0} ms / baked (read + make + stand-up, colliders cooked here) p50 {P(bakedMs, 50):0.0} p95 {P(bakedMs, 95):0.0} max {P(bakedMs, 100):0.0} ms");
            L($"{baked} of {order.Count} tiles stood up from the bake in the baked pass; the baked lattices hold {latExtra} corners more than the built ones read (sign sites decided in another order)");
            L($"BAKE GATE: {order.Count} tiles, {equal} EQUAL, {differ} DIFFER (data {dData}, ground {dLat}, whole {dWhole})");
            L(differ == 0 && equal == order.Count && baked == order.Count ? "BAKE GATE PASS" : "BAKE GATE FAIL");
        }
    }
}
