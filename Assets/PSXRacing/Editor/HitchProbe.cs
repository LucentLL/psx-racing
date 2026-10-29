using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE HITCH PROBE (branch city-build-hitches, 2026-10-29; the owner's
    /// I-277 race frame: "CITY 25 tiles ... build max 208.0 ms/10 s"). Two
    /// parts, one launch (tools\hitch-probe.ps1):
    ///
    ///   BUILD (edit mode, deterministic): every tile the 5x5 ring needs along
    ///     the Uptown Loop (I-277), the Tryon Street Sprint and I-77 through
    ///     Uptown, in the order a car meets them, built as a TileJob ONE STEP
    ///     A CALL: every step's ms, its label (phase / item / part) and where
    ///     its time went (CityMeshes.HitchClock's zones); then stood up
    ///     (CityWorld.StandUp), its signs and poles decided a slice a call and
    ///     its trees planted, each timed. Every tile hashed whole - every
    ///     vertex, normal, UV, colour and index of every mesh it stood up,
    ///     trees and furniture included - into PSXRacing_hitch_TAG_hashes.txt;
    ///     PSX_HITCH_BASE=that file of an earlier run: compared tile by tile.
    ///     Where CityMeshes has the FineSteps switch, each tile is built both
    ///     ways in the same run (old steps, then new) and the two compared.
    ///   PLAY (play mode): each race venue (PSX_HITCH_VENUES) on autopilot,
    ///     chase view, uncapped, PSX_HITCH_SECONDS: frames over 16.7 / 33 / 50
    ///     / 100 ms and the worst, every slow frame with what the city did in
    ///     it (ProfilerRecorders on the streaming markers, GC, the tile events,
    ///     the builder's steps over 8 ms with their labels).
    /// Report: PSXRacing_hitch_TAG.txt at the project root.
    /// </summary>
    public static class HitchProbe
    {
        internal static readonly StringBuilder log = new StringBuilder();
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        internal static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v.Trim(); }
        internal static float EnvF(string k, float d) => float.TryParse(Env(k, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : d;
        internal static void L(string s) { log.AppendLine(s); }
        static string Tag => Env("PSX_HITCH_TAG", "run");

        static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;

        [MenuItem("PSX Racing/Hitch Probe")]
        public static void Run()
        {
            log.Clear();
            L("HITCH PROBE " + Tag + "  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  unity " + Application.unityVersion + ", " + SystemInfo.processorType);
            string mode = Env("PSX_HITCH_MODE", "all");
            if (mode == "all" || mode == "build")
            {
                try { BuildPart(); }
                catch (System.Exception e) { L("BUILD PART THREW: " + e); }
                Write();
            }
            if (mode == "all" || mode == "play")
            {
                venues = new List<string>();
                foreach (var v in Env("PSX_HITCH_VENUES", "UptownLoop,TryonSprint").Split(','))
                {
                    if (Env("PSX_HITCH_PLAYAB", "0") != "1") { venues.Add(v); continue; }
                    venues.Add(v + ":old"); venues.Add(v + ":new");
                }
                NextVenue();
                return;
            }
            Finish();
        }

        static void Write()
        {
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_hitch_" + Tag + ".txt"), log.ToString());
        }

        internal static void Finish()
        {
            L("HITCH PROBE DONE.");
            Write();
            Debug.Log(log.ToString());
            EditorApplication.Exit(0);
        }

        // ==================================================================
        //  BUILD
        // ==================================================================
        static Vector2 LL(double lat, double lon)
        {
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale;
        }

        /// <summary>A race route's line, from its start (a loop: once round).</summary>
        static List<Vector2> RouteLine(CityMap map, string id)
        {
            var pts = new List<Vector2>();
            var r = map.RouteById(id);
            if (r == null) return pts;
            for (int k = 0; k < r.edges.Length; k++)
            {
                var e = map.edges[r.edges[k]];
                if (r.dirs[k] >= 0) for (int i = 0; i < e.pts.Length; i++) pts.Add(e.pts[i]);
                else for (int i = e.pts.Length - 1; i >= 0; i--) pts.Add(e.pts[i]);
            }
            return pts;
        }

        /// <summary>A freeway followed from the road nearest a point toward
        /// another, node to node, by name and the straightest way on.</summary>
        static List<Vector2> FollowRoad(CityMap map, Vector2 from, Vector2 toward, string nameHas, float maxM)
        {
            var pts = new List<Vector2>();
            // the nearest point of the named road whose way on heads toward the target
            CityMap.Edge e = null; float bestD = float.MaxValue; bool fwd = true;
            foreach (var c in map.edges)
            {
                if (c.name != nameHas || c.pts == null || c.pts.Length < 2) continue;
                for (int i = 0; i + 1 < c.pts.Length; i++)
                {
                    float d = (c.pts[i] - from).sqrMagnitude;
                    if (d >= bestD) continue;
                    var tc = c.pts[i + 1] - c.pts[i];
                    bool f = Vector2.Dot(tc, toward - from) >= 0f;
                    if (c.oneway && !f) continue;
                    bestD = d; e = c; fwd = f;
                }
            }
            if (e == null) return pts;
            float run = 0f;
            var seen = new HashSet<int>();
            for (int guard = 0; guard < 2000 && run < maxM && e != null; guard++)
            {
                seen.Add(e.index);
                if (fwd) for (int i = 0; i < e.pts.Length; i++) pts.Add(e.pts[i]);
                else for (int i = e.pts.Length - 1; i >= 0; i--) pts.Add(e.pts[i]);
                run += e.length;
                int n = fwd ? e.b : e.a;
                var dirIn = fwd ? e.pts[e.pts.Length - 1] - e.pts[e.pts.Length - 2] : e.pts[0] - e.pts[1];
                dirIn.Normalize();
                CityMap.Edge best = null; bool bestFwd = true; float bestScore = float.MinValue;
                foreach (int oi in map.nodeEdges[n])
                {
                    var o = map.edges[oi];
                    if (o == e || seen.Contains(oi) || o.pts.Length < 2) continue;
                    bool of = o.a == n;
                    if (o.oneway && !of) continue;
                    var dOut = of ? o.pts[1] - o.pts[0] : o.pts[o.pts.Length - 2] - o.pts[o.pts.Length - 1];
                    dOut.Normalize();
                    float score = Vector2.Dot(dirIn, dOut) + (o.name == nameHas ? 2f : 0f) - (o.link ? 1f : 0f);
                    if (score > bestScore) { bestScore = score; best = o; bestFwd = of; }
                }
                if (best == null || bestScore < 1.5f) break;
                e = best; fwd = bestFwd;
            }
            return pts;
        }

        /// <summary>The tiles a 5x5 ring (and, at speed, the row ahead) needs
        /// along a line, in the order the car first needs them.</summary>
        static List<Vector2Int> TileOrder(List<Vector2> line, int max)
        {
            var order = new List<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            float T = CityMeshes.TileSize;
            float acc = 0f;
            for (int i = 1; i < line.Count && order.Count < max; i++)
            {
                acc += Vector2.Distance(line[i - 1], line[i]);
                if (acc < 24f && i < line.Count - 1) continue;
                acc = 0f;
                var p = line[i];
                var dir = (line[i] - line[i - 1]).normalized;
                int ptx = Mathf.FloorToInt(p.x / T), ptz = Mathf.FloorToInt(p.y / T);
                var ring = new List<(Vector2Int k, float d)>();
                for (int dz = -3; dz <= 3; dz++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        bool inRing = Mathf.Abs(dx) <= 2 && Mathf.Abs(dz) <= 2;
                        var c = new Vector2((ptx + dx + 0.5f) * T, (ptz + dz + 0.5f) * T) - p;
                        if (!inRing && Vector2.Dot(c.normalized, dir) < 0.6f) continue;   // the row ahead only
                        ring.Add((new Vector2Int(ptx + dx, ptz + dz), c.magnitude - (Vector2.Dot(c, dir) > 0f ? 60f : 0f)));
                    }
                ring.Sort((a, b) => a.d.CompareTo(b.d));
                foreach (var r in ring) if (seen.Add(r.k)) order.Add(r.k);
            }
            if (order.Count > max) order.RemoveRange(max, order.Count - max);
            return order;
        }

        // ---- every step of every tile ----
        struct StepRec { public float ms; public int phase, item, part; public float[] zones; public string tile; }
        static readonly List<StepRec> bigNew = new List<StepRec>(), bigOld = new List<StepRec>();
        static readonly List<float> stepsNew = new List<float>(), stepsOld = new List<float>();

        static void BuildPart()
        {
            var map = CityMap.Get();
            if (map == null) { L("no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            var go = new GameObject("~hitchProbe");
            var world = go.AddComponent<CityWorld>();
            world.ring = 2;
            var standUp = typeof(CityWorld).GetMethod("StandUp", NP);
            var dropTile = typeof(CityWorld).GetMethod("DropTile", NP);
            var liveField = typeof(CityWorld).GetField("live", NP);
            var fine = typeof(CityMeshes).GetField("FineSteps", SP);
            bool ab = fine != null && Env("PSX_HITCH_AB", "1") == "1";
            var timings = new List<CityWorld.TileTiming>();
            System.Action<CityWorld.TileTiming> onBuilt = t => timings.Add(t);
            CityWorld.TileBuilt += onBuilt;
            var hashes = new StringBuilder();
            var baseHashes = new Dictionary<string, string>();
            string basePath = Env("PSX_HITCH_BASE", "");
            if (basePath.Length > 0 && File.Exists(basePath))
                foreach (var line in File.ReadAllLines(basePath))
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 3) baseHashes[parts[0]] = parts[1] + " " + parts[2];
                }
            int same = 0, differ = 0, compared = 0, abSame = 0, abDiffer = 0, abandonTests = 0;
            var standMs = new List<float>(); var cookMs = new List<float>(); var signMs = new List<float>(); var treeMs = new List<float>();
            var standWorst = new List<(float ms, string what)>();
            try
            {
                // warm-up: JIT and the static caches
                world.EnsureTile(Mathf.FloorToInt(map.uptown.x / CityMeshes.TileSize), Mathf.FloorToInt(map.uptown.y / CityMeshes.TileSize));
                world.DropAll();
                timings.Clear();

                var routes = new List<(string name, List<Vector2> line)>();
                foreach (var id in Env("PSX_HITCH_ROUTES", "uptown,tryon,i77").Split(','))
                {
                    if (id == "i77") routes.Add(("i77", FollowRoad(map, LL(35.2700, -80.8520), LL(35.1900, -80.8800), "I-77", 9000f)));
                    else routes.Add((id, RouteLine(map, id)));
                }
                int maxTiles = (int)EnvF("PSX_HITCH_MAXTILES", 70);
                var done = new HashSet<Vector2Int>();
                var liveOrder = new List<long>();
                foreach (var (name, line) in routes)
                {
                    var order = TileOrder(line, maxTiles);
                    float km = 0f; for (int i = 1; i < line.Count; i++) km += Vector2.Distance(line[i - 1], line[i]);
                    L($"route {name}: {km / 1000f:0.0} km, {order.Count} tiles");
                    foreach (var k in order)
                    {
                        if (!done.Add(k)) continue;
                        string tileName = k.x + "," + k.y;
                        long key = ((long)k.x << 24) ^ (k.y & 0xFFFFFF);
                        string sigOld = null;
                        if (ab)
                        {
                            fine.SetValue(null, false);
                            var jo = StepThrough(map, k.x, k.y, stepsOld, bigOld, tileName);
                            sigOld = MeshSig(jo.Result);
                            KillTile(jo.Result);
                            CityMeshes.TakeLattice();
                            fine.SetValue(null, true);
                        }
                        // every fifth tile: a job given up part way first (CityWorld.AbandonJob), so the
                        // build after it proves a fresh start leaves nothing of the one before
                        if (done.Count % 5 == 0)
                        {
                            var ja = CityMeshes.Begin(map, CityWorld.SharedTrims, CityWorld.SharedBuildings, k.x, k.y);
                            for (int g = 0; g < 400 + done.Count * 37 % 900 && !ja.Done; g++) ja.Step(0.0);
                            ja.Abandon(); KillTile(ja.Result); abandonTests++;
                        }
                        var job = StepThrough(map, k.x, k.y, stepsNew, bigNew, tileName);
                        string sig = MeshSig(job.Result);
                        if (Env("PSX_HITCH_NORMALS", "1") == "1") NormalsExperiment(job.Result);
                        if (ab) { if (sig == sigOld) abSame++; else { abDiffer++; L($"  A/B MISMATCH tile {tileName}: old steps {sigOld} / new {sig}"); } }
                        // stood up, then its signs/poles a slice a call, then its trees
                        timings.Clear();
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        standUp.Invoke(world, new object[] { key, k.x, k.y, job.Result, job.WorkMs, job.Slices, (float)job.MaxSliceMs });
                        float su = (float)clock.Elapsed.TotalMilliseconds;
                        standMs.Add(su);
                        if (timings.Count > 0) cookMs.Add(timings[0].cookMs);
                        standWorst.Add((su, $"stand-up {tileName} (attach {(timings.Count > 0 ? timings[0].attachMs : 0):0.0}, cook {(timings.Count > 0 ? timings[0].cookMs : 0):0.0}, props {(timings.Count > 0 ? timings[0].propsMs : 0):0.0})"));
                        for (int g = 0; g < 200; g++)
                        {
                            clock.Restart();
                            int r = world.PrepareSigns(key);
                            float ms = (float)clock.Elapsed.TotalMilliseconds;
                            if (r == 0) break;
                            signMs.Add(ms);
                            standWorst.Add((ms, $"signs/poles slice {tileName}"));
                        }
                        clock.Restart();
                        world.PlantTrees(key);
                        float tm = (float)clock.Elapsed.TotalMilliseconds;
                        treeMs.Add(tm);
                        standWorst.Add((tm, $"tree frame {tileName}"));
                        // the whole stood-up tile, trees and furniture included
                        var live = liveField.GetValue(world) as System.Collections.IDictionary;
                        string full = "-";
                        if (live != null && live.Contains(key))
                        {
                            var tile = live[key];
                            var tgo = tile.GetType().GetField("go").GetValue(tile) as GameObject;
                            full = FullSig(tgo);
                        }
                        hashes.AppendLine($"{tileName} {sig} {full}");
                        if (baseHashes.TryGetValue(tileName, out var b))
                        {
                            compared++;
                            if (b == sig + " " + full) same++;
                            else { differ++; if (differ <= 20) L($"  BASE MISMATCH tile {tileName}: base {b} / now {sig} {full}"); }
                        }
                        liveOrder.Add(key);
                        while (liveOrder.Count > 9) { dropTile.Invoke(world, new object[] { liveOrder[0] }); liveOrder.RemoveAt(0); }
                    }
                }
            }
            finally
            {
                CityWorld.TileBuilt -= onBuilt;
                if (fine != null) fine.SetValue(null, true);
                CityMeshes.HitchClock.On = false;
                world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_hitch_" + Tag + "_hashes.txt"), hashes.ToString());
            L("");
            ReportSteps(ab ? "NEW STEPS" : "STEPS", stepsNew, bigNew);
            if (ab) ReportSteps("OLD STEPS (FineSteps off, same run)", stepsOld, bigOld);
            if (ab) L($"A/B: {abSame} tiles identical old steps vs new, {abDiffer} MISMATCH(ES); {abandonTests} of the new builds came after a job given up part way");
            L(basePath.Length > 0 ? $"vs BASE {basePath}: {compared} tiles compared, {same} identical, {differ} MISMATCH(ES)" : "no base given (PSX_HITCH_BASE)");
            if (normalMeshes > 0)
            {
                L($"normals experiment ({normalMeshes} ground/road/building meshes, {normalChunks} chunks): vertices whose normal matches RecalculateNormals bit for bit -");
                for (int w = 0; w < NormalWays.Length; w++) L($"    {NormalWays[w],-40} {normalSame[w]} of {normalTotal[w]}");
            }
            standMs.Sort(); cookMs.Sort(); signMs.Sort(); treeMs.Sort();
            L($"stand-up: {standMs.Count} tiles p50 {P(standMs, 50):0.0} p95 {P(standMs, 95):0.0} max {P(standMs, 100):0.0} ms (collider cook p95 {P(cookMs, 95):0.0} max {P(cookMs, 100):0.0})");
            L($"signs/poles slices: {signMs.Count} p50 {P(signMs, 50):0.0} p95 {P(signMs, 95):0.0} max {P(signMs, 100):0.0} ms");
            L($"tree frames: {treeMs.Count} p50 {P(treeMs, 50):0.0} p95 {P(treeMs, 95):0.0} max {P(treeMs, 100):0.0} ms");
            standWorst.Sort((a, b) => b.ms.CompareTo(a.ms));
            L("  the longest stand-up / signs / tree frames:");
            for (int i = 0; i < Mathf.Min(14, standWorst.Count); i++) L($"    {standWorst[i].ms,6:0.0} ms  {standWorst[i].what}");
        }

        static CityMeshes.TileJob StepThrough(CityMap map, int tx, int tz, List<float> steps, List<StepRec> big, string tileName)
        {
            var job = CityMeshes.Begin(map, CityWorld.SharedTrims, CityWorld.SharedBuildings, tx, tz);
            var z0 = new long[CityMeshes.HitchClock.Count];
            CityMeshes.HitchClock.On = true;
            try
            {
                while (!job.Done)
                {
                    System.Array.Copy(CityMeshes.HitchClock.Ticks, z0, z0.Length);
                    var c = System.Diagnostics.Stopwatch.StartNew();
                    job.Step(0.0);
                    float ms = (float)c.Elapsed.TotalMilliseconds;
                    steps.Add(ms);
                    if (ms > 8f)
                    {
                        var zs = new float[z0.Length];
                        for (int i = 0; i < zs.Length; i++) zs[i] = (float)CityMeshes.HitchClock.Ms(CityMeshes.HitchClock.Ticks[i] - z0[i]);
                        big.Add(new StepRec { ms = ms, phase = CityMeshes.StepPhaseNow, item = CityMeshes.StepItemNow, part = CityMeshes.StepPartNow, zones = zs, tile = tileName });
                    }
                }
            }
            finally { CityMeshes.HitchClock.On = false; }
            return job;
        }

        static string PartName(int phase, int part)
        {
            string[] roadParts = { "sections", "side flags", "spans", "lamps" };
            return part == 4 ? "sectioning ahead" : part == 5 ? "a neighbour's sections" : part == 10 ? "side flags: setup + structure ends" : part == 11 ? "side flags: spans"
                 : part == 12 ? "side flags: runs + flares" : part == 20 ? "branch: chains" : part == 21 ? "branch: stations"
                 : part == 22 ? "branch: clips" : part == 23 ? "branch: nose" : part == 6 && phase == 2 ? "outlines ahead" : part == 30 ? "fan floor"
                 : phase == 2 ? roadParts[Mathf.Clamp(part, 0, 3)] : phase == 9 ? (part == 1 ? "normals (chunks)" : "mesh / item") : "part " + part;
        }

        static void ReportSteps(string title, List<float> steps, List<StepRec> big)
        {
            var s = new List<float>(steps); s.Sort();
            int o8 = 0, o16 = 0, o33 = 0;
            foreach (var x in s) { if (x > 8f) o8++; if (x > 16.7f) o16++; if (x > 33f) o33++; }
            L($"{title}: {s.Count} steps, p50 {P(s, 50):0.00} p99 {P(s, 99):0.0} max {P(s, 100):0.0} ms; over 8 ms {o8}, over 16.7 ms {o16}, over 33 ms {o33}");
            var l = new List<StepRec>(big);
            l.Sort((a, b) => b.ms.CompareTo(a.ms));
            var byKind = new Dictionary<string, (int n, float ms, float max)>();
            foreach (var b in l)
            {
                string k = CityMeshes.PhaseNames[Mathf.Clamp(b.phase, 0, CityMeshes.PhaseNames.Length - 1)] + " " + PartName(b.phase, b.part);
                byKind.TryGetValue(k, out var a);
                byKind[k] = (a.n + 1, a.ms + b.ms, Mathf.Max(a.max, b.ms));
            }
            for (int i = 0; i < Mathf.Min(25, l.Count); i++)
            {
                var b = l[i];
                var zs = new List<(float ms, string n)>();
                for (int z = 0; z < b.zones.Length; z++) if (b.zones[z] >= 0.5f) zs.Add((b.zones[z], CityMeshes.HitchClock.Names[z]));
                zs.Sort((x, y) => y.ms.CompareTo(x.ms));
                var zt = new StringBuilder();
                foreach (var z in zs) zt.Append($" {z.n} {z.ms:0.0};");
                L($"    {b.ms,6:0.0} ms  tile {b.tile,-10} {CityMeshes.PhaseNames[Mathf.Clamp(b.phase, 0, 9)]} {PartName(b.phase, b.part)} item {b.item}  |{zt}");
            }
            var kinds = new List<KeyValuePair<string, (int n, float ms, float max)>>(byKind);
            kinds.Sort((x, y) => y.Value.max.CompareTo(x.Value.max));
            L("  steps over 8 ms by kind (count, total ms, max ms):");
            foreach (var k in kinds) L($"    {k.Value.n,4} x {k.Value.ms,7:0} {k.Value.max,6:0.0}  {k.Key}");
        }

        static float P(List<float> sorted, float pct)
        {
            if (sorted.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.CeilToInt(pct / 100f * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[i];
        }

        // ---- the hashes ----
        static ulong H(ulong h, int v) { unchecked { h ^= (uint)v; h *= 1099511628211UL; } return h; }
        static int F(float f) => System.BitConverter.SingleToInt32Bits(f);
        static readonly List<Vector3> v3 = new List<Vector3>();
        static readonly List<Vector4> v4 = new List<Vector4>();
        static readonly List<Color32> c32 = new List<Color32>();
        static readonly List<int> idx = new List<int>();

        static ulong HashMesh(ulong h, Mesh m)
        {
            if (m == null) return H(h, -1);
            m.GetVertices(v3); h = H(h, v3.Count);
            foreach (var p in v3) { h = H(h, F(p.x)); h = H(h, F(p.y)); h = H(h, F(p.z)); }
            m.GetNormals(v3); h = H(h, v3.Count);
            foreach (var p in v3) { h = H(h, F(p.x)); h = H(h, F(p.y)); h = H(h, F(p.z)); }
            for (int ch = 0; ch < 2; ch++)
            {
                m.GetUVs(ch, v4); h = H(h, v4.Count);
                foreach (var p in v4) { h = H(h, F(p.x)); h = H(h, F(p.y)); h = H(h, F(p.z)); h = H(h, F(p.w)); }
            }
            m.GetColors(c32); h = H(h, c32.Count);
            foreach (var c in c32) h = H(h, c.r | c.g << 8 | c.b << 16 | c.a << 24);
            h = H(h, m.subMeshCount);
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                m.GetIndices(idx, sub); h = H(h, idx.Count);
                foreach (int i in idx) h = H(h, i);
            }
            var bc = m.bounds;
            h = H(h, F(bc.center.x)); h = H(h, F(bc.center.y)); h = H(h, F(bc.center.z));
            h = H(h, F(bc.extents.x)); h = H(h, F(bc.extents.y)); h = H(h, F(bc.extents.z));
            return h;
        }

        /// <summary>A built tile's meshes and its lamps, bit for bit.</summary>
        static string MeshSig(CityMeshes.TileMeshes tm)
        {
            ulong h = 14695981039346656037UL;
            int verts = 0;
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings, tm.buildingCollider })
            {
                h = HashMesh(h, m);
                if (m != null) verts += m.vertexCount;
            }
            foreach (var sl in new[] { tm.groundSlots, tm.roadSlots, tm.buildingSlots })
            {
                h = H(h, sl != null ? sl.Length : -1);
                if (sl != null) foreach (var s in sl) h = H(h, (int)s);
            }
            h = H(h, tm.lamps.Count);
            foreach (var l in tm.lamps) { h = H(h, F(l.head.x)); h = H(h, F(l.head.y)); h = H(h, F(l.head.z)); h = H(h, F(l.foot.x)); h = H(h, F(l.foot.y)); h = H(h, F(l.foot.z)); h = H(h, l.mercury ? 1 : 0); }
            h = H(h, tm.solids.Count); h = H(h, tm.goreNoses.Count); h = H(h, tm.wbeams.Count);
            h = H(h, F(tm.vergeMetres)); h = H(h, F(tm.railMetres));
            return verts + ":" + h.ToString("x16");
        }

        /// <summary>A stood-up tile, whole: every runtime mesh under it (the
        /// tile's, its trees', poles', signals' and signs'), by path, and every
        /// prop model's place.</summary>
        static string FullSig(GameObject root)
        {
            if (root == null) return "-";
            ulong h = 14695981039346656037UL;
            var mfs = root.GetComponentsInChildren<MeshFilter>(true);
            var rows = new List<(string path, MeshFilter mf)>();
            foreach (var mf in mfs) rows.Add((PathOf(mf.transform, root.transform), mf));
            rows.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            int n = 0;
            foreach (var (path, mf) in rows)
            {
                foreach (char ch in path) h = H(h, ch);
                var t = mf.transform;
                h = H(h, F(t.position.x)); h = H(h, F(t.position.y)); h = H(h, F(t.position.z));
                h = H(h, F(t.rotation.x)); h = H(h, F(t.rotation.y)); h = H(h, F(t.rotation.z)); h = H(h, F(t.rotation.w));
                var m = mf.sharedMesh;
                if (m != null && !EditorUtility.IsPersistent(m)) { h = HashMesh(h, m); n++; }
            }
            return n + ":" + h.ToString("x16");
        }

        static string PathOf(Transform t, Transform root)
        {
            var sb = new StringBuilder();
            for (var c = t; c != null && c != root; c = c.parent) sb.Insert(0, "/" + c.name + "#" + c.GetSiblingIndex());
            return sb.ToString();
        }

        // ---- THE NORMALS EXPERIMENT: which way of computing a mesh's normals
        // in pieces gives Unity's RecalculateNormals bit for bit ----
        static readonly string[] NormalWays = { "chunks of whole parts, Unity per chunk", "C# area-weighted, Normalize", "C# area-weighted, x 1/sqrt", "C# unit face normals, Normalize", "C# area-weighted (double)" };
        static readonly long[] normalSame = new long[5], normalTotal = new long[5];
        static int normalMeshes, normalChunks;

        static void NormalsExperiment(CityMeshes.TileMeshes tm)
        {
            foreach (var m in new[] { tm.ground, tm.roads, tm.buildings })
            {
                if (m == null) continue;
                normalMeshes++;
                var pos = new List<Vector3>(); m.GetVertices(pos);
                var nrm = new List<Vector3>(); m.GetNormals(nrm);
                int n = pos.Count;
                var tris = new List<int[]>();
                for (int s = 0; s < m.subMeshCount; s++) tris.Add(m.GetIndices(s));
                // 0: independent chunks (no vertex shared across a cut), each a temp mesh
                var got = new Vector3[n];
                var tmp = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var cv = new List<Vector3>(); var ci = new List<int>(); var cn = new List<Vector3>();
                foreach (var t in tris)
                {
                    int nt = t.Length / 3;
                    var sufMin = new int[nt + 1]; sufMin[nt] = int.MaxValue;
                    for (int k = nt - 1; k >= 0; k--) sufMin[k] = Mathf.Min(sufMin[k + 1], Mathf.Min(t[3 * k], Mathf.Min(t[3 * k + 1], t[3 * k + 2])));
                    int start = 0, maxV = -1, minV = int.MaxValue;
                    for (int k = 0; k < nt; k++)
                    {
                        maxV = Mathf.Max(maxV, Mathf.Max(t[3 * k], Mathf.Max(t[3 * k + 1], t[3 * k + 2])));
                        minV = Mathf.Min(minV, Mathf.Min(t[3 * k], Mathf.Min(t[3 * k + 1], t[3 * k + 2])));
                        bool cut = k == nt - 1 || (maxV < sufMin[k + 1] && (k + 1 - start) >= 2000);
                        if (!cut) continue;
                        cv.Clear(); ci.Clear();
                        for (int v = minV; v <= maxV; v++) cv.Add(pos[v]);
                        for (int j = 3 * start; j < 3 * (k + 1); j++) ci.Add(t[j] - minV);
                        tmp.Clear();
                        tmp.SetVertices(cv);
                        tmp.SetTriangles(ci, 0, false);
                        tmp.RecalculateNormals();
                        tmp.GetNormals(cn);
                        for (int v = minV; v <= maxV; v++) got[v] = cn[v - minV];
                        normalChunks++;
                        start = k + 1; maxV = -1; minV = int.MaxValue;
                    }
                }
                Object.DestroyImmediate(tmp);
                Tally(0, nrm, got);
                // 1-4: C#
                for (int way = 1; way <= 4; way++)
                {
                    var acc = new Vector3[n];
                    var accD = way == 4 ? new double[n * 3] : null;
                    foreach (var t in tris)
                        for (int j = 0; j + 2 < t.Length; j += 3)
                        {
                            Vector3 a = pos[t[j]], b = pos[t[j + 1]], c = pos[t[j + 2]];
                            var f = Vector3.Cross(b - a, c - a);
                            if (way == 3) f = f.normalized;
                            if (accD != null)
                            {
                                for (int q = 0; q < 3; q++) { int v = t[j + q]; accD[3 * v] += f.x; accD[3 * v + 1] += f.y; accD[3 * v + 2] += f.z; }
                                continue;
                            }
                            acc[t[j]] += f; acc[t[j + 1]] += f; acc[t[j + 2]] += f;
                        }
                    for (int v = 0; v < n; v++)
                    {
                        if (accD != null) acc[v] = new Vector3((float)accD[3 * v], (float)accD[3 * v + 1], (float)accD[3 * v + 2]);
                        got[v] = way == 2 ? (acc[v].sqrMagnitude > 0f ? acc[v] * (1f / Mathf.Sqrt(acc[v].sqrMagnitude)) : Vector3.zero) : Vector3.Normalize(acc[v]);
                    }
                    Tally(way, nrm, got);
                }
            }
        }

        static void Tally(int way, List<Vector3> want, Vector3[] got)
        {
            for (int v = 0; v < want.Count; v++)
            {
                normalTotal[way]++;
                if (F(want[v].x) == F(got[v].x) && F(want[v].y) == F(got[v].y) && F(want[v].z) == F(got[v].z)) normalSame[way]++;
            }
        }

        static void KillTile(CityMeshes.TileMeshes tm)
        {
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings, tm.buildingCollider })
                if (m != null) Object.DestroyImmediate(m);
        }

        // ==================================================================
        //  PLAY
        // ==================================================================
        static List<string> venues;

        /// <summary>A venue's race handed over (the hour, the weather, the car,
        /// three rivals); its scene's path, or null when it is not built here.</summary>
        internal static string SetupVenue(string venue)
        {
            // "Venue:old" / "Venue:new": the hitch fixes off / on for this race (the A/B)
            int colon = venue.IndexOf(':');
            if (colon > 0)
            {
                bool on = venue.Substring(colon + 1) != "old";
                CityWorld.NoForcedFinish = on; CityMeshes.FineSteps = on; HitchSwitches(on);
                venue = venue.Substring(0, colon);
            }
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == venue) index = i;
            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            string path = s >= 0 && s < scenes.Length && scenes[s].path.EndsWith("/" + venue + ".unity") ? scenes[s].path
                        : "Assets/PSXRacing/Scenes/" + venue + ".unity";
            if (index < 0 || !File.Exists(path)) { L("PLAY: the venue " + venue + " is not built here"); return null; }
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            int hourIdx = TimeOfDay.Morning;
            string hour = Env("PSX_HITCH_HOUR", "Night");
            for (int h = 0; h < TimeOfDay.All.Length; h++)
                if (string.Equals(TimeOfDay.All[h].name, hour, System.StringComparison.OrdinalIgnoreCase)) hourIdx = h;
            RaceHandoff.TimeOfDayIndex = hourIdx;
            if (System.Enum.TryParse(Env("PSX_HITCH_WEATHER", "Clear"), true, out Weather w)) RaceHandoff.WeatherOverride = (int)w;
            var cars = CarCatalog.All;
            int ci = 0;
            string carId = Env("PSX_HITCH_CAR", "");
            for (int i = 0; i < cars.Count; i++) if (cars[i].id == carId) ci = i;
            RaceHandoff.CarSpecId = cars[ci].id;
            RaceHandoff.OpponentSpecIds = cars[(ci + 1) % cars.Count].id + ";" + cars[(ci + 2) % cars.Count].id + ";" + cars[(ci + 3) % cars.Count].id;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            RaceHandoff.StartFuelPct = 100f;
            L("");
            L($"PLAY {venue}: hour {TimeOfDay.All[hourIdx].name}, weather {(RaceHandoff.WeatherOverride >= 0 ? ((Weather)RaceHandoff.WeatherOverride).ToString() : "calendar")}, car {cars[ci].id}, three rivals, traffic by the hour; " +
              $"no forced finish {(CityWorld.NoForcedFinish ? "ON" : "OFF")}, fine steps {(CityMeshes.FineSteps ? "ON" : "OFF")}");
            Write();
            return path;
        }

        /// <summary>The first venue's scene opened and play entered; the runner
        /// then loads every next venue inside the same play session (leaving
        /// play mode between venues left a batch editor idle).</summary>
        static void NextVenue()
        {
            HitchRunner.queue = new Queue<string>();
            foreach (var v in venues) if (v.Trim().Length > 0) HitchRunner.queue.Enqueue(v.Trim());
            while (HitchRunner.queue.Count > 0)
            {
                string venue = HitchRunner.queue.Dequeue();
                string path = SetupVenue(venue);
                if (path == null) continue;
                HitchRunner.venue = venue;
                EditorSceneManager.OpenScene(path);
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                EditorApplication.playModeStateChanged += OnState;
                EditorApplication.EnterPlaymode();
                return;
            }
            Finish();
        }

        /// <summary>The other hitch fixes' switches (reflection: a field a later
        /// fix adds is switched too, one absent is skipped).</summary>
        internal static void HitchSwitches(bool on)
        {
            foreach (var (type, field) in new[] { (typeof(StreetLights), "FieldCull"), (typeof(CityWorld), "SliceTrees"), (typeof(CityFar), "SliceRebuild"), (typeof(CityProxies), "SliceSteps") })
            {
                var f = type.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null && f.FieldType == typeof(bool)) f.SetValue(null, on);
            }
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            var go = new GameObject("HitchRunner");
            go.AddComponent<HitchRunner>();
        }
    }

    [DefaultExecutionOrder(-32000)]
    public class HitchRunner : MonoBehaviour
    {
        internal static string venue;
        static readonly string[] Markers =
        {
            "CityWorld.JobStep", "CityWorld.StandUp", "CityWorld.PlantTrees", "CityWorld.PrepareSigns", "CityWorld.BuildWholeTile", "CityWorld.FinishJob",
            "CityProxies.Step", "CityProxies.Refresh", "CityFar.Refresh", "CitySkyline.Refresh", "CitySkyline.Reindex", "StreetLights.BuildField",
            "CityFar.RebuildLand", "CityFar.RebuildTrees", "CityFar.RebuildLights",
            "GC.Collect", "BehaviourUpdate", "FixedBehaviourUpdate", "LateBehaviourUpdate", "FixedUpdate.PhysicsFixedUpdate",
            "PostLateUpdate.FinishFrameRendering", "Gfx.WaitForPresentOnGfxThread",
            "CityTraffic.FixedUpdate() [Invoke]", "CityTraffic.Update() [Invoke]", "CityWorld.Update() [Invoke]", "CityWorld.LateUpdate() [Invoke]",
            "TreeTrunks.FixedUpdate() [Invoke]",
        };
        ProfilerRecorder[] recs;
        static readonly FieldInfo jobField = typeof(CityWorld).GetField("job", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly List<(int phase, int item, int part, float ms, float missMs)> bigSteps = new List<(int, int, int, float, float)>();
        int bigSeen;
        readonly List<string> events = new List<string>();
        RaceManager rm; CarController car;
        bool measuring; float t0, until;
        int gcWas;
        readonly List<float> frames = new List<float>();
        readonly List<(float ms, string what)> slow = new List<(float, string)>();
        readonly Dictionary<string, (int over16, float max)> byMarker = new Dictionary<string, (int, float)>();
        float speedSum, distM; Vector3 lastPos;
        float bornAt;

        void OnEnable()
        {
            recs = new ProfilerRecorder[Markers.Length];
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var cats = new Dictionary<string, ProfilerCategory>();
            foreach (var h in handles) { var d = ProfilerRecorderHandle.GetDescription(h); if (!cats.ContainsKey(d.Name)) cats[d.Name] = d.Category; }
            for (int i = 0; i < Markers.Length; i++)
                recs[i] = ProfilerRecorder.StartNew(cats.TryGetValue(Markers[i], out var c) ? c : ProfilerCategory.Scripts, Markers[i]);
            CityWorld.TileBuilt += OnTile;
        }

        void OnDisable()
        {
            CityWorld.TileBuilt -= OnTile;
            if (recs != null) foreach (var r in recs) r.Dispose();
        }

        void OnTile(CityWorld.TileTiming t)
        {
            if (!measuring) return;
            events.Add(t.treeFrame ? (t.trees > 0 ? $"trees {t.totalMs:0.0}" : $"signs/poles {t.totalMs:0.0}")
                                   : t.slices > 0 ? $"stand-up {t.standUpMs:0.0} (tile of {t.slices} slices, worst {t.maxSliceMs:0.0})" : $"WHOLE TILE {t.totalMs:0.0}");
        }

        internal static Queue<string> queue;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            while (true)
            {
                yield return RunVenue();
                Report();
                ResetVenue();
                string path = null;
                while (path == null && queue != null && queue.Count > 0)
                {
                    venue = queue.Dequeue();
                    path = HitchProbe.SetupVenue(venue);
                }
                if (path == null) break;
                EditorSceneManager.LoadSceneInPlayMode(path, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
                yield return null;
                yield return null;
            }
            HitchProbe.Finish();
        }

        void ResetVenue()
        {
            frames.Clear(); slow.Clear(); byMarker.Clear(); bigSteps.Clear(); events.Clear();
            bigSeen = 0; speedSum = 0f; distM = 0f; measuring = false; rm = null; car = null;
        }

        int abandonedWas;

        IEnumerator RunVenue()
        {
            bornAt = Time.realtimeSinceStartup;
            yield return null;
            yield return new WaitForFixedUpdate();
            float find = Time.realtimeSinceStartup + 20f;
            while ((RaceManager.Instance == null || RaceManager.Instance.playerCar == null) && Time.realtimeSinceStartup < find) yield return null;
            rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            if (rm == null || car == null || rm.path == null) { HitchProbe.L("  no race / player / path"); yield break; }
            Random.InitState(9173);
            float wait = Time.realtimeSinceStartup + 25f;
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup < wait) yield return null;
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
            var auto = car.gameObject.AddComponent<AIDriver>();
            auto.path = rm.path;
            auto.skill = 0.95f;
            car.allowReverse = false;
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            lastPos = car.transform.position;
            gcWas = System.GC.CollectionCount(0);
            abandonedWas = CityWorld.JobsAbandoned;
            measuring = true;
            t0 = Time.realtimeSinceStartup;
            until = t0 + HitchProbe.EnvF("PSX_HITCH_SECONDS", 100f);
            while (Time.realtimeSinceStartup < until && rm != null && rm.State == RaceManager.RaceState.Racing) yield return null;
            measuring = false;
        }

        void Update()
        {
            if (Time.realtimeSinceStartup - bornAt > 900f) { HitchProbe.L("  HUNG"); HitchProbe.Finish(); return; }
            var cw = CityWorld.Active;
            if (cw != null && jobField != null && jobField.GetValue(cw) is CityMeshes.TileJob tj && tj.BigSteps == null) { tj.BigSteps = bigSteps; tj.BigStepMs = 8f; }
            if (!measuring) { bigSeen = bigSteps.Count; events.Clear(); return; }
            float ms = Time.unscaledDeltaTime * 1000f;
            frames.Add(ms);
            int gc = System.GC.CollectionCount(0);
            bool collected = gc != gcWas; gcWas = gc;
            if (car != null) { speedSum += Mathf.Abs(car.forwardSpeed); distM += Vector3.Distance(car.transform.position, lastPos); lastPos = car.transform.position; }
            for (int i = 0; i < recs.Length; i++)
            {
                if (!recs[i].Valid) continue;
                float m = recs[i].LastValue / 1e6f;
                byMarker.TryGetValue(Markers[i], out var a);
                byMarker[Markers[i]] = (a.over16 + (m > 16.7f ? 1 : 0), Mathf.Max(a.max, m));
            }
            if (ms > 16.7f)
            {
                var sb = new StringBuilder();
                sb.Append($"{Time.realtimeSinceStartup - t0,6:0.0}s {ms,6:0.0} ms{(collected ? " GC" : "")} |");
                for (int i = 0; i < recs.Length; i++)
                {
                    if (!recs[i].Valid) continue;
                    float m = recs[i].LastValue / 1e6f;
                    if (m >= 2f) sb.Append($" {Markers[i].Replace("() [Invoke]", "")} {m:0.0};");
                }
                for (int i = bigSeen; i < bigSteps.Count; i++)
                {
                    var b = bigSteps[i];
                    sb.Append($" STEP {b.ms:0.0} {CityMeshes.PhaseNames[Mathf.Clamp(b.phase, 0, 9)]}/{b.part} item {b.item} (miss {b.missMs:0.0});");
                }
                foreach (var e in events) sb.Append(" [" + e + "]");
                slow.Add((ms, sb.ToString()));
            }
            bigSeen = bigSteps.Count;
            events.Clear();
        }

        void Report()
        {
            var s = new List<float>(frames); s.Sort();
            int o16 = 0, o33 = 0, o50 = 0, o100 = 0;
            foreach (var m in frames) { if (m > 16.7f) o16++; if (m > 33.3f) o33++; if (m > 50f) o50++; if (m > 100f) o100++; }
            float secs = Time.realtimeSinceStartup - t0;
            HitchProbe.L($"  {venue}: {frames.Count} frames in {secs:0} s, {CityWorld.JobsAbandoned - abandonedWas} tile jobs given up, {(frames.Count > 0 ? speedSum / frames.Count * 2.23694f : 0):0} mph avg, {distM / 1000f:0.0} km; frame ms p50 {P(s, 50):0.0} p95 {P(s, 95):0.0} p99 {P(s, 99):0.0} max {P(s, 100):0.0}");
            HitchProbe.L($"  FRAMES over 16.7 ms {o16}, over 33 ms {o33}, over 50 ms {o50}, over 100 ms {o100}, worst {P(s, 100):0} ms   (race state {rm.State})");
            var w = CityWorld.Active;
            if (w != null) HitchProbe.L($"  city: {w.LiveTiles} tiles live, {w.TreesPending} tree frames pending, {w.anchors.Count} anchors");
            HitchProbe.L("  markers: frames over 16.7 ms / worst ms:");
            foreach (var kv in byMarker) if (kv.Value.max >= 1f) HitchProbe.L($"    {kv.Value.over16,4} / {kv.Value.max,6:0.0}  {kv.Key}");
            slow.Sort((a, b) => b.ms.CompareTo(a.ms));
            HitchProbe.L($"  the slowest frames ({Mathf.Min(40, slow.Count)} of {slow.Count} over 16.7 ms):");
            for (int i = 0; i < Mathf.Min(40, slow.Count); i++) HitchProbe.L("    " + slow[i].what);
            var l = new List<(int phase, int item, int part, float ms, float missMs)>(bigSteps);
            l.Sort((x, y) => y.ms.CompareTo(x.ms));
            HitchProbe.L($"  TILE BUILDER: {l.Count} steps over 8 ms in the run; the longest:");
            for (int i = 0; i < Mathf.Min(12, l.Count); i++)
                HitchProbe.L($"    {l[i].ms,6:0.0} ms  {CityMeshes.PhaseNames[Mathf.Clamp(l[i].phase, 0, 9)]} part {l[i].part} item {l[i].item} (sectioning unseen edges {l[i].missMs:0.0})");
        }

        static float P(List<float> sorted, float pct)
        {
            if (sorted.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.CeilToInt(pct / 100f * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[i];
        }
    }
}
