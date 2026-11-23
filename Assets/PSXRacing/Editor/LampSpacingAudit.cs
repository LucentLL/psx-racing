using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LAMP SPACING AUDIT (the city-night round, 2026-11-23). The owner: "the
    /// road is completely dark, then is illuminated as I drive closer"; along
    /// W Trade St the approach curves found 60 m and more of street with no lamp
    /// within 40 m. Every audited road is built tile by tile as the game builds it
    /// (CityMeshes.Build, the tree frame's poles), and every light that stands by
    /// it (lamp posts, acorns, high masts, the poles' cobra-heads) is projected onto
    /// it: lamps per km, the gaps between them (p50 / p90 / worst), each station's
    /// fate (placed, or why not: CityMeshes.LampTrace), and the DARKEST patch of the
    /// road's centre between two lamps as the static field's light there (the
    /// pool and glow of every light, NIGHT 3's shape and the old field's).
    /// Plus lamps per km by road class over the tiles built.
    ///   -executeMethod PSXRacing.EditorTools.LampSpacingAudit.Run   (PSX_LAMPAUDIT_TAG=name)
    /// Report: PSXRacing_lampspacing_&lt;tag&gt;.txt at the project root.
    /// </summary>
    public static class LampSpacingAudit
    {
        static readonly (string id, double la, double lo, double tla, double tlo, string name, float len)[] Roads =
        {
            ("w_trade",    35.2345, -80.8540, 35.2271, -80.8431, "Trade Street", 1300f),
            ("n_tryon",    35.2271, -80.8431, 35.2360, -80.8340, "Tryon Street", 1300f),
            ("w_4th",      35.2245, -80.8495, 35.2290, -80.8420, "4th Street", 900f),
            ("providence", 35.2060, -80.8250, 35.1700, -80.8000, "Providence Road", 2500f),
            ("queens",     35.2035, -80.8335, 35.1900, -80.8400, "Queens Road", 1500f),
            ("i77",        35.2400, -80.8540, 35.2100, -80.8640, "I-77", 3000f),
            ("i277",       35.2200, -80.8500, 35.2240, -80.8360, "I-277", 2000f),
        };

        struct Light { public Vector3 p; public float gain; public string src; }

        public static void Run()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_LAMPAUDIT_TAG") ?? "run";
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            var map = CityMap.Get();
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            PSXRacingBuilder.EnsureCityTextures();
            float ts = CityMeshes.TileSize;
            bool signsWere = CitySigns.Enabled;
            CitySigns.Enabled = false;
            var tiles = new Dictionary<long, (int tx, int tz)>();
            var routes = new List<(string id, CityMap.Route r, List<Vector3> cl, float hw, int cls)>();
            foreach (var rd in Roads)
            {
                var r = PerfTour.FollowRoad(map, rd.id, PerfTour.LL(rd.la, rd.lo), PerfTour.LL(rd.tla, rd.tlo), rd.name, rd.len);
                if (r == null) { sb.AppendLine(rd.id + ": NO ROAD"); continue; }
                var cl = new List<Vector3>();
                float hw = 0f; int cls = 0;
                foreach (var (ei, k) in Iter(r))
                {
                    var e = map.edges[ei]; bool fw = r.dirs[k] > 0;
                    hw = Mathf.Max(hw, e.width * 0.5f); cls = Mathf.Max(cls, e.cls);
                    for (float s = 0f; s < e.length; s += 2f)
                    {
                        float a = fw ? s : e.length - s;
                        var q = e.PointAt(a);
                        cl.Add(new Vector3(q.x, e.YAt(a), q.y));
                        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                        {
                            int tx = Mathf.FloorToInt(q.x / ts) + dx, tz = Mathf.FloorToInt(q.y / ts) + dz;
                            tiles[((long)tx << 32) ^ (uint)tz] = (tx, tz);
                        }
                    }
                }
                routes.Add((rd.id, r, cl, hw, cls));
            }
            // build every tile once: its lights, its stations' fates
            var lights = new List<Light>();
            var stations = new List<(Vector2 p, int why)>();
            CityMeshes.LampTrace = (ei, s, w) => { if (s >= 0f) { var q = map.edges[ei].PointAt(s); stations.Add((q, w)); } };
            var clsLen = new float[6]; var clsLamps = new int[6];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int posts = 0, acorns = 0, masts = 0, poleHeads = 0, postTris = 0;
            try
            {
                foreach (var t in tiles.Values)
                {
                    var tm = CityMeshes.Build(map, trims, buildings, t.tx, t.tz);
                    RoadsideOccupancy.Build(map, trims, buildings, tm, t.tx, t.tz);
                    var tt = CityTrees.Build(map, trims, buildings, tm, t.tx, t.tz);
                    foreach (var l in tm.lamps)
                    {
                        lights.Add(new Light { p = tm.origin + l.head, gain = 1f, src = CityMeshes.IsAcornKind(l.kind) ? "acorn" : "post" });
                        if (CityMeshes.IsAcornKind(l.kind)) acorns++; else posts++;
                    }
                    foreach (var h in tm.mastHeads) { lights.Add(new Light { p = tm.origin + h, gain = 1f, src = "mast" }); masts++; }
                    if (tt.poles != null) foreach (var h in tt.poles.heads) { lights.Add(new Light { p = h, gain = 1f, src = "pole" }); poleHeads++; }
                    if (tm.lampPosts != null) postTris += (int)(tm.lampPosts.GetIndexCount(0) / 3);
                    foreach (var f in typeof(CityMeshes.TileMeshes).GetFields())
                        if (f.FieldType == typeof(Mesh) && f.GetValue(tm) is Mesh m && m != null) Object.DestroyImmediate(m);
                }
            }
            finally { CityMeshes.LampTrace = null; CitySigns.Enabled = signsWere; }
            sb.AppendLine($"LAMP SPACING AUDIT ({tag}) {tiles.Count} tiles in {sw.Elapsed.TotalSeconds:0} s; lights: posts {posts}, acorns {acorns}, masts {masts}, pole heads {poleHeads}; lamp-post triangles {postTris}");
            // per class over the tiles: each light to its nearest edge within 25 m
            var lens = new float[6];
            var seenE = new HashSet<int>();
            foreach (var t in tiles.Values)
            {
                var mn = new Vector2(t.tx * ts, t.tz * ts);
                foreach (var e in map.edges)
                {
                    if (e.link || e.tunnel || e.pts == null || e.pts.Length < 2 || seenE.Contains(e.index)) continue;
                    var c = e.PointAt(e.length * 0.5f);
                    if (c.x < mn.x || c.y < mn.y || c.x >= mn.x + ts || c.y >= mn.y + ts) continue;
                    seenE.Add(e.index); lens[Mathf.Clamp(e.cls, 0, 5)] += e.length;
                }
            }
            foreach (var l in lights)
            {
                if (!map.NearestRoadPoint(new Vector2(l.p.x, l.p.z), 25f, false, out int ei, out _, out _)) continue;
                if (seenE.Contains(ei)) clsLamps[Mathf.Clamp(map.edges[ei].cls, 0, 5)]++;
            }
            sb.Append("lamps per km by class (lights within 25 m of an edge whose middle is in the tiles):");
            for (int c = 0; c < 6; c++) sb.Append($"  cls{c} {(lens[c] > 1f ? clsLamps[c] / (lens[c] / 1000f) : 0f):0.0} ({lens[c] / 1000f:0.0} km)");
            sb.AppendLine();
            var whyAll = new int[CityMeshes.LampRejectCount];
            foreach (var st in stations) if (st.why >= 0 && st.why < whyAll.Length) whyAll[st.why]++;
            sb.Append("stations in the built tiles:");
            for (int k = 0; k < whyAll.Length; k++) if (whyAll[k] > 0) sb.Append($" {CityMeshes.LampRejectNames[k]} {whyAll[k]};");
            sb.AppendLine();
            foreach (var rt in routes)
            {
                // project each light within reach onto the road: arc metres
                float reach = rt.cls >= 5 ? 45f : Mathf.Max(18f, rt.hw + 12f);
                var at = new List<float>();
                var srcN = new Dictionary<string, int>();
                foreach (var l in lights)
                {
                    int best = -1; float bd = reach * reach;
                    for (int i = 0; i < rt.cl.Count; i += 2)
                    {
                        float dx = rt.cl[i].x - l.p.x, dz = rt.cl[i].z - l.p.z, d2 = dx * dx + dz * dz;
                        if (d2 < bd) { bd = d2; best = i; }
                    }
                    if (best < 0) continue;
                    float hh = l.p.y - rt.cl[best].y;
                    if (hh < -1f || hh > 40f) continue;   // a lamp on a deck over it or a road far below
                    at.Add(best * 2f);
                    srcN.TryGetValue(l.src, out int n); srcN[l.src] = n + 1;
                }
                at.Sort();
                float L = rt.cl.Count * 2f;
                var gaps = new List<float>();
                float prev = 0f;
                foreach (float a in at) { gaps.Add(a - prev); prev = a; }
                gaps.Add(L - prev);
                var g2 = new List<float>(gaps); g2.Sort();
                float p50 = g2[g2.Count / 2], p90 = g2[Mathf.Min(g2.Count - 1, (int)(g2.Count * 0.9f))], worst = g2[g2.Count - 1];
                // the darkest centre-line patch (skip the ends), the field's light, both shapes
                float darkN3 = float.MaxValue, darkOld = float.MaxValue; float darkAt = 0f;
                for (int i = 10; i < rt.cl.Count - 10; i++)
                {
                    var p = rt.cl[i];
                    float n3 = 0f, old = 0f;
                    foreach (var l in lights)
                    {
                        float dx = l.p.x - p.x, dz = l.p.z - p.z, d2 = dx * dx + dz * dz;
                        if (d2 > 1600f) continue;
                        float I = StreetLights.StreetIntensity * l.gain;
                        float un = l.p.y - p.y;
                        if (un >= -1f && un <= 16f) old += I * (1.8f * W(d2, 28f) + 0.30f * W(d2, 40f));
                        // NIGHT 3: a mast's light counts from N3MastLiftM over its road (StreetLights.Night3)
                        float un3 = l.src == "mast" ? Mathf.Min(un, StreetLights.N3MastLiftM) : un;
                        if (un3 >= -1f && un3 <= 16f) n3 += I * (StreetLights.N3PoolGain * W(d2, StreetLights.N3PoolM) + StreetLights.N3GlowGain * W(d2, StreetLights.N3GlowM));
                    }
                    if (n3 < darkN3) { darkN3 = n3; darkAt = i * 2f; }
                    darkOld = Mathf.Min(darkOld, old);
                }
                var why = new int[CityMeshes.LampRejectCount];
                foreach (var st in stations)
                {
                    bool on = false;
                    for (int i = 0; i < rt.cl.Count && !on; i += 3)
                        on = (new Vector2(rt.cl[i].x, rt.cl[i].z) - st.p).sqrMagnitude < 16f;
                    if (on && st.why >= 0 && st.why < why.Length) why[st.why]++;
                }
                var srcs = new StringBuilder();
                foreach (var kv in srcN) srcs.Append($" {kv.Key} {kv.Value}");
                var whys = new StringBuilder();
                for (int k = 0; k < why.Length; k++) if (why[k] > 0) whys.Append($" {CityMeshes.LampRejectNames[k]} {why[k]};");
                sb.AppendLine(string.Format(ci, "{0,-11} cls{1} {2,5:0} m: {3,3} lights ({4:0.0}/km:{5}) gaps p50 {6:0} p90 {7:0} worst {8:0} m, {9} gaps over 60 m | darkest centre: NIGHT3 {10:0.00} (at {11:0} m) old {12:0.00} | stations:{13}",
                    rt.id, rt.cls, L, at.Count, at.Count / (L / 1000f), srcs, p50, p90, worst, gaps.FindAll(g => g > 60f).Count, darkN3, darkAt, darkOld, whys));
            }
            string path = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "PSXRacing_lampspacing_" + tag + ".txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log("[LampSpacingAudit] wrote " + path + "\n" + sb);
        }


        static float W(float d2, float r) { float x = 1f - d2 / (r * r); return x > 0f ? x * x : 0f; }

        static IEnumerable<(int ei, int k)> Iter(CityMap.Route r) { for (int k = 0; k < r.edges.Length; k++) yield return (r.edges[k], k); }
    }
}
