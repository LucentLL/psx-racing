using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PSXRacing.City;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ROAD LOGIC JUNCTIONS ROUND 3: THE SIGNAL HEADS EVERY APPROACH SHOWS.
    /// Builds every tile that owns a signalised junction the way play does
    /// (the tile's meshes, its occupancy mask with the tile's houses and lamps,
    /// then CitySignals.Build first on it) and counts the heads facing each
    /// (junction, approach) - BEFORE (PSX_CITY_HEADS=0: a span wire or a
    /// far-right mast arm, nothing else) and AFTER (CitySignals.HeadsEvery) -
    /// in one editor on the same data. Then the driver's view on approach,
    /// before and after, at Colony Road / Carnegie Boulevard (junction 1000)
    /// and the two other junctions with the most approaches that had no head
    /// (PSX_HEADS_SHOTS "x,z,label;..." overrides), at a clock time when that
    /// approach has had its green 5 s. Writes heads.txt and the PNGs to
    /// Screenshots/City/heads.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunSignalHeads
    /// PSX_HEADS_SHOTS=0 skips the shots (a -nographics count).
    /// </summary>
    public static partial class CityRefSpots
    {
        sealed class HeadCensus
        {
            public int nearSupps, backSpans, junctions, approaches, a0, a1, a2, t0, jAny0, jAll0, spans, masts, leftArms, poleHeads, postHeads, poles, heads, tiles;
            /// <summary>Per (junction, approach): heads in the driver's view (CitySignals.InView).</summary>
            public readonly Dictionary<(int j, int a), int> by = new Dictionary<(int, int), int>();
            /// <summary>... and every head facing it, wherever it hangs.</summary>
            public readonly Dictionary<(int j, int a), int> total = new Dictionary<(int, int), int>();
            public readonly Dictionary<byte, int> how = new Dictionary<byte, int>();
            public double ms;
        }

        /// <summary><see cref="RunSignalHeads"/>, then the road meshes hashed
        /// (<see cref="CityRoadMeshHash"/>: the furniture changes none of them), one editor.</summary>
        public static void RunSignalHeadsAndHash()
        {
            RunSignalHeads();
            CityRoadMeshHash.Run();
        }

        public static void RunSignalHeads()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string dir = Path.Combine(ProjectRoot(), "Screenshots", "City", "heads");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            string txt = Path.Combine(dir, "heads.txt");
            var log = new StringBuilder();
            bool shots = System.Environment.GetEnvironmentVariable("PSX_HEADS_SHOTS") != "0";
            PSXRacingBuilder.EnsureCityTextures();
            var go = new GameObject("~citySignalHeads");
            var world = go.AddComponent<CityWorld>();
            bool every0 = CitySignals.HeadsEvery;
            try
            {
                world.EnsureRing(new Vector3(map.uptown.x, 0f, map.uptown.y), 0);
                world.DropAll();
                var trims = world.NodeTrims;
                var bld = world.Buildings;
                var all = CitySignals.All(map, trims);
                // the tiles that own a signalised junction (CitySignals: the tile
                // of the junction's centre owns all of it)
                var sigTiles = new SortedSet<(int z, int x)>();
                foreach (var j in all)
                    if (j.signal) sigTiles.Add((Mathf.FloorToInt(j.centre.y / CityMeshes.TileSize), Mathf.FloorToInt(j.centre.x / CityMeshes.TileSize)));
                var census = new Dictionary<bool, HeadCensus> { [false] = new HeadCensus(), [true] = new HeadCensus() };
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (var (tz, tx) in sigTiles)
                {
                    var tm = CityMeshes.Build(map, trims, bld, tx, tz);
                    foreach (bool on in new[] { false, true })
                    {
                        CitySignals.HeadsEvery = on;
                        var c = census[on];
                        var t0 = clock.Elapsed.TotalMilliseconds;
                        var occ = RoadsideOccupancy.Build(map, trims, bld, tm, tx, tz);
                        var st = CitySignals.Build(map, trims, bld, tm, occ, tx, tz);
                        c.tiles++;
                        c.spans += st.spanWires; c.masts += st.mastArms; c.leftArms += st.leftArms; c.backSpans += st.backSpans; c.nearSupps += st.nearSupps;
                        c.poleHeads += st.poleHeads; c.postHeads += st.postHeads; c.poles += st.poles.Count; c.heads += st.heads.Count;
                        foreach (var ah in st.approachHeads)
                        {
                            c.by[(ah.junction, ah.approach)] = ah.view;
                            c.total[(ah.junction, ah.approach)] = ah.heads;
                            c.how.TryGetValue(ah.how, out int k); c.how[ah.how] = k + 1;
                        }
                        foreach (var m in new[] { st.mesh, st.lamps, st.halos }) if (m != null) Object.DestroyImmediate(m);
                        c.ms += clock.Elapsed.TotalMilliseconds - t0;
                    }
                    DestroyTileMeshes(tm);
                }
                CitySignals.HeadsEvery = every0;
                log.AppendLine($"SIGNAL HEADS: {sigTiles.Count} tiles own a signalised junction, built as play builds them (meshes, occupancy with the tile's houses and lamps); {clock.ElapsedMilliseconds} ms");
                var zeroBefore = new List<(CitySignals.Junction j, int a)>();
                foreach (bool on in new[] { false, true })
                {
                    var c = census[on];
                    var perJ = new Dictionary<int, (int n, int zero)>();
                    foreach (var kv in c.total) if (kv.Value == 0) c.t0++;
                    foreach (var kv in c.by)
                    {
                        c.approaches++;
                        if (kv.Value == 0) c.a0++; else if (kv.Value == 1) c.a1++; else c.a2++;
                        perJ.TryGetValue(kv.Key.j, out var p);
                        perJ[kv.Key.j] = (p.n + 1, p.zero + (kv.Value == 0 ? 1 : 0));
                    }
                    c.junctions = perJ.Count;
                    foreach (var p in perJ.Values) { if (p.zero > 0) c.jAny0++; if (p.zero == p.n) c.jAll0++; }
                    log.AppendLine($"{(on ? "AFTER (PSX_CITY_HEADS on)" : "BEFORE (PSX_CITY_HEADS=0)")}: {c.junctions} signalised junctions, {c.approaches} approaches");
                    log.AppendLine($"    HEADS IN THE DRIVER'S VIEW (6 m short of the stop line to 55 m past it, 20 m either side): approaches with NONE {c.a0}, ONE {c.a1}, two or more {c.a2}; junctions with an approach with none {c.jAny0} (none on any approach {c.jAll0}), with every approach headed {c.junctions - c.jAny0}");
                    log.AppendLine($"    (approaches with no head facing them anywhere: {c.t0})");
                    log.AppendLine($"    {c.heads} heads, {c.poles} poles and posts; {c.spans} span wires (set back past a run-off {c.backSpans}), {c.masts} mast arms (far-left {c.leftArms}); heads on a pole's side {c.poleHeads}, post-mounted heads {c.postHeads}; near-side supplements at deep junctions (far faces 120 ft+ past the stop line) {c.nearSupps}; {c.ms:0} ms");
                    log.AppendLine("    carried by (approaches): " + string.Join(", ", c.how.OrderByDescending(kv => kv.Value).Select(kv => $"{HowName(kv.Key)} {kv.Value}")));
                    var zero = c.by.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
                    if (!on) foreach (var z in zero) zeroBefore.Add((all[z.j], z.a));
                    foreach (var g in zero.GroupBy(z => z.j).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(on ? 60 : 25))
                    {
                        var j = all[g.Key];
                        var names = string.Join(" / ", j.approaches.Select(a => map.edges[a.edge].name).Where(s => !string.IsNullOrEmpty(s)).Distinct());
                        log.AppendLine($"      junction {j.id} at ({j.centre.x:0}, {j.centre.y:0}) {CityAudit.LatLon(j.centre.x, j.centre.y)} '{names}'{(j.uptown ? " uptown" : "")}{(j.warrant ? " (warrant)" : "")}: {g.Count()} of {j.approaches.Count} approaches with no head ({string.Join(", ", g.Select(z => "e" + j.approaches[z.a].edge))})");
                    }
                }
                {
                    int fixedA = 0, worse = 0;
                    foreach (var kv in census[false].by)
                    {
                        census[true].by.TryGetValue(kv.Key, out int after);
                        if (kv.Value == 0 && after > 0) fixedA++;
                        if (after < kv.Value) worse++;
                    }
                    log.AppendLine($"BEFORE -> AFTER: {fixedA} approaches that showed no head now show one; approaches with fewer heads than before {worse}");
                }
                var c1000 = all.Count > 1000 ? all[1000] : null;
                if (c1000 != null)
                    for (int i = 0; i < c1000.approaches.Count; i++)
                    {
                        census[false].by.TryGetValue((1000, i), out int b); census[true].by.TryGetValue((1000, i), out int a);
                        log.AppendLine($"  junction 1000 ({c1000.centre.x:0}, {c1000.centre.y:0}) approach {i} e{c1000.approaches[i].edge} '{map.edges[c1000.approaches[i].edge].name}' lanes {c1000.approaches[i].lanesIn}: heads {b} -> {a}");
                    }

                if (shots)
                {
                    // ---- the driver's view: Colony / Carnegie, then the two junctions
                    // with the most approaches that had no head
                    var targets = new List<(Vector2 p, string label)>();
                    var env = System.Environment.GetEnvironmentVariable("PSX_HEADS_SHOTS");
                    if (!string.IsNullOrEmpty(env) && env != "1")
                        foreach (var part in env.Split(';'))
                        {
                            var q = part.Split(',');
                            if (q.Length >= 2 && float.TryParse(q[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                                && float.TryParse(q[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                                targets.Add((new Vector2(x, z), q.Length > 2 ? q[2] : $"j{targets.Count}"));
                        }
                    if (targets.Count == 0)
                    {
                        targets.Add((new Vector2(-393f, -3680f), "colony_carnegie"));
                        foreach (var g in zeroBefore.GroupBy(z => z.j.id).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
                        {
                            var j = all[g.Key];
                            if (Vector2.Distance(j.centre, targets[0].p) < 200f) continue;
                            if (targets.Count == 2 && Vector2.Distance(j.centre, targets[1].p) < 200f) continue;
                            targets.Add((j.centre, $"j{j.id}"));
                            if (targets.Count == 3) break;
                        }
                    }
                    DayLight();
                    NightGlow.PreviewAll(false);
                    foreach (var (p, label) in targets)
                    {
                        var j = all.Where(q => q.signal).OrderBy(q => (q.centre - p).sqrMagnitude).FirstOrDefault();
                        if (j == null || Vector2.Distance(j.centre, p) > 80f) { log.AppendLine($"shot {label}: no signalised junction within 80 m"); continue; }
                        // the approach to look down: one that showed no head before
                        // the first junction (Colony / Carnegie) down every approach; the
                        // others down the one with the fewest heads in view before
                        var shotApps = new List<int>();
                        if (label == targets[0].label) for (int i = 0; i < j.approaches.Count; i++) shotApps.Add(i);
                        else
                        {
                            int fa = 0, fewest = int.MaxValue;
                            for (int i = 0; i < j.approaches.Count; i++)
                                if (census[false].by.TryGetValue((j.id, i), out int h0) && h0 < fewest) { fewest = h0; fa = i; }
                            shotApps.Add(fa);
                        }
                        foreach (int ai in shotApps)
                        {
                        string lab = $"{label}_a{ai}";
                        var A = j.approaches[ai];
                        double t = CitySignals.GreenTime(A.channel, 5f);
                        var eye = Eye(A, out var look, j);
                        census[false].by.TryGetValue((j.id, ai), out int hb); census[true].by.TryGetValue((j.id, ai), out int ha);
                        log.AppendLine($"shot {lab}: junction {j.id} at ({j.centre.x:0}, {j.centre.y:0}) {CityAudit.LatLon(j.centre.x, j.centre.y)}{(j.uptown ? " uptown" : "")}, approach {ai} e{A.edge} '{map.edges[A.edge].name}' ({A.lanesIn} lanes in), 22 m short of its stop line, clock {t:0.0} s ({CitySignals.AspectOf(A.channel, t)}): heads {hb} -> {ha}");
                        foreach (bool on in new[] { false, true })
                        {
                            // where every head facing that approach hangs (the census's build)
                            CitySignals.HeadsEvery = on;
                            {
                                int tx = Mathf.FloorToInt(j.centre.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(j.centre.y / CityMeshes.TileSize);
                                var tm = CityMeshes.Build(map, trims, bld, tx, tz);
                                var occ = RoadsideOccupancy.Build(map, trims, bld, tm, tx, tz);
                                var st = CitySignals.Build(map, trims, bld, tm, occ, tx, tz);
                                foreach (var h in st.heads)
                                {
                                    var hp = new Vector2(h.pos.x, h.pos.z);
                                    if (Vector2.Dot(h.face, -A.u) < 0.9f || Vector2.Distance(hp, j.centre) > 120f) continue;
                                    var d = hp - A.at;
                                    log.AppendLine($"    {(on ? "after " : "before")} head {Vector2.Dot(d, A.u):0.0} m past the stop line, {Vector2.Dot(d, A.Right) - (A.inL + A.inR) * 0.5f:0.0} m right of the lanes' middle, {h.pos.y - A.roadY:0.0} m up{(CitySignals.InView(A, hp) ? "" : "  (NOT in view)")}");
                                }
                                foreach (var m in new[] { st.mesh, st.lamps, st.halos }) if (m != null) Object.DestroyImmediate(m);
                                DestroyTileMeshes(tm);
                            }
                            world.DropAll();
                            world.EnsureRing(new Vector3(j.centre.x, 0f, j.centre.y), 1);
                            CitySignals.Tick(t);
                            Shoot(dir, $"{lab}_{(on ? "after" : "before")}", eye, Quaternion.LookRotation(look - eye), 0f, 960, 540);
                            // and from 45 m back (the heads' reading distance at 35 mph)
                            var p45 = A.at - A.u * 45f + A.Right * ((A.inL + A.inR) * 0.5f);
                            var eye45 = new Vector3(p45.x, A.roadY + 1.15f, p45.y);
                            Shoot(dir, $"{lab}_{(on ? "after" : "before")}_45m", eye45, Quaternion.LookRotation(look - eye45), 0f, 960, 540);
                        }
                        world.DropAll();
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                log.AppendLine("RUN FAILED: " + e);
                Debug.LogException(e);
            }
            finally
            {
                CitySignals.HeadsEvery = every0;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
                File.WriteAllText(txt, log.ToString());
                Debug.Log("[CityRefSpots] heads:\n" + log);
            }
        }

        static string HowName(byte how)
        {
            if (how == 0) return "nothing";
            var parts = new List<string>();
            if ((how & CitySignals.HowSpan) != 0) parts.Add("span wire");
            if ((how & CitySignals.HowMast) != 0) parts.Add("mast arm");
            if ((how & CitySignals.HowMastLeft) != 0) parts.Add("far-left mast arm");
            if ((how & CitySignals.HowPole) != 0) parts.Add("pole side");
            if ((how & CitySignals.HowPostFar) != 0) parts.Add("post far-right");
            if ((how & CitySignals.HowPostLeft) != 0) parts.Add("post far-left");
            if ((how & CitySignals.HowPostNear) != 0) parts.Add("post near-right");
            return string.Join("+", parts);
        }

        /// <summary>Every mesh a tile build made (the census builds hundreds).</summary>
        static void DestroyTileMeshes(CityMeshes.TileMeshes tm)
        {
            if (tm == null) return;
            foreach (var f in typeof(CityMeshes.TileMeshes).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(Mesh)) { var m = f.GetValue(tm) as Mesh; if (m != null) Object.DestroyImmediate(m); }
                else if (typeof(IEnumerable<Mesh>).IsAssignableFrom(f.FieldType) && f.GetValue(tm) is IEnumerable<Mesh> ms)
                    foreach (var m in ms.ToList()) if (m != null) Object.DestroyImmediate(m);
            }
        }
    }
}
