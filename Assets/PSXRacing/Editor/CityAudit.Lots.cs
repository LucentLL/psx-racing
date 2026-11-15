using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// LOT ENTRANCES (2026-11-15; the owner: "many parking lots with no
    /// driveable entrance. They just have curbs all around"). City-wide and
    /// data-level (CityMeshes.LotEntranceAudit: the tile builder's own rules,
    /// no tile built): for every drawn lot the entrances of the map that reach
    /// it, the ones a car can really use (a 3 m path square off the nearest
    /// lane: the walk's curb cut, paving all the way, no lot kerb, island,
    /// building, stall row or other road across it, no metre steeper than
    /// 20%), the lots reached through a lot that has one, the lots with none
    /// and why, the lots shut all round, and the 20 largest with none (their
    /// street, lat/lon). Then the owner's spot (Beatties Ford Road at Johnson
    /// C. Smith University) lot by lot, and - PSX_LOT_PROBE=1 - the REAL
    /// cross-section of sample entrances: the tiles built and stood up
    /// (CityWorld.Attach: the game's own colliders), a ray down every 2 cm
    /// along the path's three lines, the largest step and what solid stands
    /// on the path.
    /// -executeMethod PSXRacing.EditorTools.CityAudit.LotEntrancesOnly; writes
    /// PSXRacing_lot_entrances.txt (PSX_LOT_OUT: another name) in the project
    /// root. PSX_CITY_LOTENT=0 in the environment: the count before.
    /// </summary>
    public static partial class CityAudit
    {
        const double LeLat0 = 35.18456015184093, LeLon0 = -80.81770185962013;
        static string LeLatLon(float x, float z)
        {
            double lat = LeLat0 + z / 111132.0, lon = LeLon0 + x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0));
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00000}, {1:0.00000}", lat, lon);
        }
        static readonly string[] LeCls = { "local", "tertiary", "secondary", "primary", "trunk", "motorway" };
        static readonly HashSet<int> leAuditSegs = new HashSet<int>();

        /// <summary>The road nearest a lot's ring (any class): name, class, distance.</summary>
        static string LeStreetOf(CityMap map, CityMap.Lot lot, out float dist)
        {
            dist = float.MaxValue; string best = "no road within 80 m";
            var b = lot.box;
            leAuditSegs.Clear();
            map.EdgeSegsInRect(new Vector2(b.x - 80f, b.y - 80f), new Vector2(b.z + 80f, b.w + 80f), leAuditSegs);
            foreach (int packed in leAuditSegs)
            {
                var e = map.edges[packed >> 12]; int si = packed & 0xFFF;
                if (si + 1 >= e.pts.Length || e.tunnel) continue;
                foreach (var p in lot.ring)
                {
                    float d = RoadsideOccupancy.DistToSeg(p, e.pts[si], e.pts[si + 1]) - e.HalfMax;
                    if (d < dist)
                    {
                        dist = d;
                        best = $"{(string.IsNullOrEmpty(e.name) ? "(unnamed)" : e.name)} [{LeCls[Mathf.Clamp(e.cls, 0, 5)]}{(e.link ? " link" : "")}{(e.bridge ? " bridge" : "")}]";
                    }
                }
            }
            return best;
        }

        public static void LotEntrancesOnly()
        {
            outLog = new System.Text.StringBuilder();
            var I = CultureInfo.InvariantCulture;
            var map = CityMap.Get();
            string outName = System.Environment.GetEnvironmentVariable("PSX_LOT_OUT");
            if (string.IsNullOrEmpty(outName)) outName = "PSXRacing_lot_entrances.txt";
            if (map == null) Line("charlotte_city.bytes missing from Resources");
            else
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.NodeTrims(map);
                var proc = CityBuildings.Precompute(map);
                Line($"graph hash {map.graphHash:x8} (GHSH), matches {map.GraphHashMatches}; {map.nodes.Length} nodes, {map.edges.Length} edges; trims + buildings in {sw.ElapsedMilliseconds} ms");
                Line($"parse {CityMap.LastParseMs:0} ms, solve {CityMap.LastSolveMs:0} ms; lots {map.lots.Length} (stations' from {map.stationLotsFrom}, this pass's from {CityMeshes.LotEntLotsFrom}), entrances {map.lotEntrances.Length} (this pass's from {CityMeshes.LotEntEntrancesFrom})");
                Line(CityMeshes.LotEntStats.Line());
                foreach (var l in CityMeshes.LotEntStats.leftAt) Line("  left: " + l);
                sw.Restart();
                var A = CityMeshes.LotEntranceAudit(map, trims, proc);
                Line(string.Format(I, "LOT ENTRANCE AUDIT ({0}; {1} ms): {2} drawn lots ({3} more under a restaurant's own lot: not drawn), {4} entrances in the map; " +
                    "{5} lots an entrance of the map reaches; {6} with an entrance a car can use, {7} more reached through a lot that has one; " +
                    "{8} LOTS WITH NO USABLE ENTRANCE ({9:0.0} ha), of them {10} shut all round (kerb, wall or uncut street curb on every side); lots shut all round in all {11}",
                    CityMeshes.LotEntOn ? "PSX_CITY_LOTENT on" : "PSX_CITY_LOTENT=0: the data's entrances alone", sw.ElapsedMilliseconds,
                    A.lots, A.propLots, A.dataEntrances, A.withDataEntrance, A.usable, A.served, A.none, A.noneM2 / 1e4, A.noneCurbedAllRound, A.curbedAllRound));
                foreach (var kv in A.whyCount) Line($"  why none: {kv.Value,5}  {kv.Key}");
                // the 20 largest with none
                var none = new List<int>();
                for (int li = 0; li < A.state.Length; li++) if (A.state[li] == 3) none.Add(li);
                none.Sort((a, b) => A.areaM2[b].CompareTo(A.areaM2[a]));
                for (int k = 0; k < Mathf.Min(20, none.Count); k++)
                {
                    int li = none[k]; var b = map.lots[li].box;
                    string st = LeStreetOf(map, map.lots[li], out float d);
                    Line(string.Format(I, "  none #{0}: lot {1}, {2:0} m2, at ({3:0}, {4:0}) = {5}; nearest road {6} {7:0.0} m off; {8}",
                        k + 1, li, A.areaM2[li], 0.5f * (b.x + b.z), 0.5f * (b.y + b.w), LeLatLon(0.5f * (b.x + b.z), 0.5f * (b.y + b.w)), st, d, A.why[li]));
                }
                // the owner's spot
                var spot = new Vector2((float)((-80.8560 - LeLon0) * 111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)), (float)((35.2437 - LeLat0) * 111132.0));
                Line(string.Format(I, "OWNER'S SPOT (Beatties Ford Road at JCSU, {0:0}, {1:0}): the lots within 600 m and 45 m of Beatties Ford Road", spot.x, spot.y));
                for (int li = 0; li < map.lots.Length; li++)
                {
                    var L = map.lots[li]; var b = L.box;
                    if (L.ring == null || L.ring.Length < 3 || L.aisleOnly) continue;
                    var c = new Vector2(0.5f * (b.x + b.z), 0.5f * (b.y + b.w));
                    if ((c - spot).magnitude > 600f) continue;
                    string st = LeStreetOf(map, L, out float d);
                    // within 45 m of Beatties Ford Road itself
                    float dBf = float.MaxValue;
                    leAuditSegs.Clear();
                    map.EdgeSegsInRect(new Vector2(b.x - 60f, b.y - 60f), new Vector2(b.z + 60f, b.w + 60f), leAuditSegs);
                    foreach (int packed in leAuditSegs)
                    {
                        var e = map.edges[packed >> 12]; int si = packed & 0xFFF;
                        if (si + 1 >= e.pts.Length || e.name != "Beatties Ford Road") continue;
                        foreach (var p in L.ring) dBf = Mathf.Min(dBf, RoadsideOccupancy.DistToSeg(p, e.pts[si], e.pts[si + 1]) - e.HalfMax);
                    }
                    if (dBf > 45f) continue;
                    st += string.Format(I, " (Beatties Ford Road {0:0.0} m off)", dBf);
                    Line(string.Format(I, "  lot {0}{1}: {2:0} m2 at ({3:0}, {4:0}) = {5}, {6:0} m from the spot; {7} {8:0.0} m off; {9}",
                        li, li >= CityMeshes.LotEntLotsFrom && CityMeshes.LotEntLotsFrom >= 0 ? " (corridor)" : "", CityMeshes.LotRingAreaForTools(L.ring), c.x, c.y, LeLatLon(c.x, c.y), (c - spot).magnitude, st, d,
                        A.state[li] == 1 ? "USABLE entrance" : A.state[li] == 2 ? "reached through a lot that has one" : A.state[li] == 3 ? "NONE: " + A.why[li] : "not counted"));
                }
                // the lots by kind of street, for the record
                if (System.Environment.GetEnvironmentVariable("PSX_LOT_PROBE") == "1") LeProbes(map, trims, proc, spot);
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, outName), outLog.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---- the real cross-section of sample entrances -----------------------
        static void LeProbes(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> proc, Vector2 spot)
        {
            var I = CultureInfo.InvariantCulture;
            int n = map.lotEntrances.Length, from = CityMeshes.LotEntEntrancesFrom;
            var pick = new List<(int en, string label)>();
            // the owner's: every entrance within 150 m of the spot (4 at most)
            var near = new List<(float d, int en)>();
            for (int i = 0; i < n; i++)
            {
                CityMeshes.LotEntranceFrame(map, i, out var m, out _, out _);
                near.Add(((m - spot).magnitude, i));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            for (int k = 0; k < near.Count && k < 4 && near[k].d < 150f; k++) pick.Add((near[k].en, $"owner's spot ({near[k].d:0} m)"));
            // nearest the Square: three of the data's, three derived, two stations' (the stations' follow the data's)
            near.Clear();
            for (int i = 0; i < n; i++)
            {
                CityMeshes.LotEntranceFrame(map, i, out var m, out _, out _);
                near.Add(((m - map.uptown).magnitude, i));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            int nd = 0, nx = 0;
            foreach (var (d, en) in near)
            {
                bool derived = from >= 0 && en >= from;
                if (derived && nx < 3) { pick.Add((en, "derived, near the Square")); nx++; }
                else if (!derived && nd < 3) { pick.Add((en, "the data's, near the Square")); nd++; }
            }
            string extra = System.Environment.GetEnvironmentVariable("PSX_LOT_PROBE_ENT");
            if (!string.IsNullOrEmpty(extra))
                foreach (var f in extra.Split(',')) if (int.TryParse(f, out int en) && en >= 0 && en < n) pick.Add((en, "asked for"));
            var root = new GameObject("LotProbeRoot");
            var stood = new HashSet<long>();
            int mask = ~((1 << 2) | (1 << CityWorld.SolidLayer) | (1 << CityTrees.FoliageLayer));
            try
            {
                foreach (var (en, label) in pick)
                {
                    var E = map.lotEntrances[en]; var e = map.edges[E.edge];
                    CityMeshes.LotEntranceFrame(map, en, out var mouth, out var nrm, out var t);
                    int li = CityMeshes.LotOfEntrance(map, en, out float gap);
                    float to = (gap < 0f ? 8f : gap) + 4f;
                    for (float d = -4f; d <= to + 4f; d += 4f)
                        for (int k = -1; k <= 1; k++)
                        {
                            var q = mouth + nrm * d + t * (k * 4f);
                            int tx = Mathf.FloorToInt(q.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(q.y / CityMeshes.TileSize);
                            if (!stood.Add(((long)tx << 32) ^ (uint)tz)) continue;
                            var tm = CityMeshes.Build(map, trims, proc, tx, tz);
                            var go = new GameObject($"tile_{tx}_{tz}");
                            go.transform.SetParent(root.transform, false);
                            go.transform.position = tm.origin;
                            CityWorld.Attach(go, tm, null);
                        }
                    Physics.SyncTransforms();
                    float y0 = e.YAt(Mathf.Clamp(E.s, 0f, e.length));
                    Line(string.Format(I, "PROBE entrance {0} ({1}): e{2} '{3}' [{4}] s {5:0} side {6}, half {7:0.00}, mouth ({8:0.0}, {9:0.0}) = {10}; lot {11}, its asphalt {12:0.0} m from the pavement; walk side {13}",
                        en, label, e.index, e.name, LeCls[Mathf.Clamp(e.cls, 0, 5)], E.s, E.side, E.half, mouth.x, mouth.y, LeLatLon(mouth.x, mouth.y), li, gap, CityMeshes.WalkSide(map, e, E.side)));
                    for (int k = -1; k <= 1; k++)
                    {
                        var sb = new System.Text.StringBuilder();
                        float prev = float.NaN, maxStep = 0f, stepAt = 0f, yRoad = float.NaN, maxRise05 = 0f;
                        var ys = new List<float>();
                        string lastName = "";
                        for (float d = -2f; d <= to; d += 0.02f)
                        {
                            var q = mouth + nrm * d + t * (k * 1.5f);
                            float top = float.NaN; string nm = "";
                            foreach (var h in Physics.RaycastAll(new Vector3(q.x, y0 + 4f, q.y), Vector3.down, 9f, mask, QueryTriggerInteraction.Ignore))
                                if (float.IsNaN(top) || h.point.y > top) { top = h.point.y; nm = h.collider.name; }
                            if (float.IsNaN(yRoad) && !float.IsNaN(top)) yRoad = top;
                            ys.Add(top);
                            if (!float.IsNaN(top) && !float.IsNaN(prev) && Mathf.Abs(top - prev) > maxStep) { maxStep = Mathf.Abs(top - prev); stepAt = d; }
                            prev = top;
                            int idx = ys.Count - 1;
                            if (idx % 25 == 0 || nm != lastName)
                            {
                                sb.Append(string.Format(I, "{0:0.0}:{1}{2} ", d, float.IsNaN(top) ? "nan" : ((top - yRoad) * 100f).ToString("0", I), nm != lastName ? "(" + nm + ")" : ""));
                                lastName = nm;
                            }
                        }
                        for (int a = 0; a + 25 < ys.Count; a++) if (!float.IsNaN(ys[a]) && !float.IsNaN(ys[a + 25])) maxRise05 = Mathf.Max(maxRise05, Mathf.Abs(ys[a + 25] - ys[a]));
                        Line(string.Format(I, "  line {0:+0.0;-0.0} m: largest step between 2 cm samples {1:0.0} cm at {2:0.00} m, largest change over 0.5 m {3:0.0} cm; cm over the lane (d m: cm): {4}", k * 1.5f, maxStep * 100f, stepAt, maxRise05 * 100f, sb));
                    }
                    // what stands on the path (any layer), 0.3-1.5 m over the surface, from the lane to 3 m inside
                    var mid = mouth + nrm * (0.5f * (to - 1f - 1f));
                    var hits = Physics.OverlapBox(new Vector3(mid.x, y0 + 0.9f, mid.y), new Vector3(1.5f, 0.6f, 0.5f * (to - 1f + 1f)),
                        Quaternion.LookRotation(new Vector3(nrm.x, 0f, nrm.y), Vector3.up), ~0, QueryTriggerInteraction.Ignore);
                    var names = new SortedDictionary<string, int>();
                    foreach (var h in hits) { string key = h.name + "/L" + h.gameObject.layer; names[key] = (names.TryGetValue(key, out int c) ? c : 0) + 1; }
                    Line("  on the path 0.3-1.5 m up (collider/layer x count): " + (names.Count == 0 ? "nothing" : string.Join(", ", System.Linq.Enumerable.Select(names, kv => kv.Key + " x" + kv.Value))));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
