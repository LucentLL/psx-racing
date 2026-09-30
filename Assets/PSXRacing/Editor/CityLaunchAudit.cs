using System;
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
    /// THE LAUNCH AUDIT (2026-09-30). The owner, after driving /city/: "many
    /// sections of road that meet at sharp angles that result in launching the
    /// car at high speed. All roads should meet at smooth junctions and
    /// transitions." The DRIVE AUDIT's step check is a HEIGHT step (12 cm in
    /// half a metre); a crest is an ANGLE, and a 9% tent over a crossing is no
    /// step at all.
    ///
    /// Every drivable path through the city is walked on the BUILT meshes
    /// (CityMeshes.Build, every road tile and its ring; the road and ground
    /// triangles a wheel's down ray would meet, back faces excluded as Physics
    /// excludes them): along every edge in each direction it may be driven,
    /// and through every node by every allowed movement (through and turning,
    /// onto and off every ramp, across every fan and mitred joint, across deck
    /// ends and tile seams), 30 m of each arm either side. The vertical profile
    /// is sampled every metre and a car is flown off it: from each sample,
    /// with the pitch of the last two metres, at the path's judged speed, the
    /// ballistic arc y0 + g0 x - G x^2 / (2 v^2); the SEPARATION is how far the
    /// road falls below that arc in the next 40 m. It is the whole rule in one
    /// number: a sharp grade break of dg separates (v dg)^2 / 2G, a crest of
    /// radius R under v^2/G separates without bound, and a gentle one not at
    /// all. LAUNCH past <see cref="LaunchSepM"/>, UNLOAD past
    /// <see cref="UnloadSepM"/> (the springs' static compression: the wheels
    /// lose their load). Judged speed: the class's (<see cref="ClassKmh"/>),
    /// capped by what the turn's plan radius allows; on a race route at least
    /// <see cref="RouteKmh"/>.
    ///
    /// Writes city_launch.txt (counts city-wide and per route, by cause, the
    /// worst spots), city_launch.csv (every launch spot) and
    /// launch_top_{label}.txt (the worst spots' paths, for the play-mode drive:
    /// CityLaunchDrive). Menu: PSX Racing/City Launch Audit.
    /// </summary>
    public static class CityLaunchAudit
    {
        public const float StepM = 1f;
        public const float LaunchSepM = 0.15f;
        public const float UnloadSepM = 0.05f;
        public const float ApproachM = 30f;
        public const float RouteKmh = CityElevation.RouteKmh;
        const float G = 9.81f;
        const float PitchBaseM = 2f;
        const float FlyM = 40f;
        const float Cell = 4f;
        const int TopForDrive = 20;

        /// <summary>The speed a class is judged at (km/h): what a driver in a
        /// hurry carries there, not the posted limit. ONE table, the solver's
        /// (its vertical curves are sized by it).</summary>
        public static float ClassKmh(CityMap.Edge e) => CityElevation.JudgedKmh(e);

        // ---- the paths ---------------------------------------------------
        sealed class PathRec
        {
            public byte kind;              // 0 along an edge, 1 through a node, 2 a turn
            public int start, count;
            public float v;                 // judged speed, m/s
            public int routeMask;
            public int node = -1;
            public int eA, eB = -1;         // edge path: eA; movement: in, out
            public int dirA, dirB;          // +1 = increasing arc position
            public float sA0, sB0;          // arc position of the first sample on each
            public int nA, nF;              // samples on arm A, then in the fan
        }

        static float[] X, Z, YE, YL, Y;
        /// <summary>PSX_LAUNCH_BOX=x0,z0,x1,z1: only the paths that start inside (a debug run).</summary>
        static Rect box = Rect.MinMaxRect(-1e9f, -1e9f, 1e9f, 1e9f);
        static bool[] OnRoad;
        static int nS;
        static readonly List<PathRec> paths = new List<PathRec>(200000);

        static void Add(Vector2 p, float yExp) => Add(p, yExp, yExp);
        static void Add(Vector2 p, float yExp, float yLow)
        {
            if (nS == X.Length)
            {
                int n = X.Length * 2;
                Array.Resize(ref X, n); Array.Resize(ref Z, n); Array.Resize(ref YE, n); Array.Resize(ref YL, n);
            }
            X[nS] = p.x; Z[nS] = p.y; YE[nS] = yExp; YL[nS] = yLow; nS++;
        }

        static Vector2 RightOf(Vector2 travel) => new Vector2(travel.y, -travel.x);

        static float LatFor(CityMeshes.Trims trims, CityMap.Edge e, float s) =>
            e.oneway ? 0f : trims.HalfWidthAt(e, s) * 0.5f;

        // ---- the surfaces -----------------------------------------------
        sealed class Surf
        {
            public float x0, z0; public int nx, nz;
            public Vector3[] v; public int[] tri; public bool[] road;
            public int[] cellStart, cellTris;
        }

        static Surf SurfOf(CityMeshes.TileMeshes tm)
        {
            var verts = new List<Vector3>(8192); var tris = new List<int>(16384); var road = new List<bool>(8192);
            foreach (var (m, isRoad) in new[] { (tm.roads, true), (tm.ground, false) })
            {
                if (m == null || m.vertexCount == 0) continue;
                int b = verts.Count;
                foreach (var q in m.vertices) verts.Add(q + tm.origin);
                var t = m.triangles;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = verts[b + t[i]], bb = verts[b + t[i + 1]], c = verts[b + t[i + 2]];
                    float ny = (bb.z - a.z) * (c.x - a.x) - (bb.x - a.x) * (c.z - a.z);
                    if (!(ny > 1e-6f)) continue;   // back face or vertical (or NaN): no down ray meets it
                    if (float.IsNaN(a.y + bb.y + c.y)) continue;
                    tris.Add(b + t[i]); tris.Add(b + t[i + 1]); tris.Add(b + t[i + 2]); road.Add(isRoad);
                }
            }
            if (tris.Count == 0) return null;
            var s = new Surf { v = verts.ToArray(), tri = tris.ToArray(), road = road.ToArray() };
            float mnx = float.MaxValue, mnz = float.MaxValue, mxx = float.MinValue, mxz = float.MinValue;
            foreach (var q in s.v) { mnx = Mathf.Min(mnx, q.x); mnz = Mathf.Min(mnz, q.z); mxx = Mathf.Max(mxx, q.x); mxz = Mathf.Max(mxz, q.z); }
            s.x0 = mnx; s.z0 = mnz;
            s.nx = Mathf.Max(1, Mathf.CeilToInt((mxx - mnx) / Cell) + 1); s.nz = Mathf.Max(1, Mathf.CeilToInt((mxz - mnz) / Cell) + 1);
            var count = new int[s.nx * s.nz + 1];
            int nt = s.tri.Length / 3;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    // count[k + 1] held cell k's triangles: prefix sums make count[k]
                    // cell k's first slot, and it is then the fill cursor
                    for (int i = 1; i < count.Length; i++) count[i] += count[i - 1];
                    s.cellStart = (int[])count.Clone();
                    s.cellTris = new int[count[count.Length - 1]];
                }
                for (int t = 0; t < nt; t++)
                {
                    Vector3 a = s.v[s.tri[3 * t]], b = s.v[s.tri[3 * t + 1]], c = s.v[s.tri[3 * t + 2]];
                    int cx0 = (int)((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - s.x0) / Cell), cx1 = (int)((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - s.x0) / Cell);
                    int cz0 = (int)((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - s.z0) / Cell), cz1 = (int)((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - s.z0) / Cell);
                    for (int cz = cz0; cz <= cz1; cz++)
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            int k = cz * s.nx + cx;
                            if (pass == 0) count[k + 1]++;
                            else s.cellTris[count[k]++] = t;
                        }
                }
            }
            return s;
        }

        /// <summary>The first up-facing surface a down ray from yTop meets.</summary>
        static void Query(Surf s, float x, float z, float yTop, ref float best, ref bool bestRoad)
        {
            int cx = (int)((x - s.x0) / Cell), cz = (int)((z - s.z0) / Cell);
            if (x < s.x0 || z < s.z0 || cx >= s.nx || cz >= s.nz) return;
            int c = cz * s.nx + cx;
            for (int k = s.cellStart[c]; k < s.cellStart[c + 1]; k++)
            {
                int t = s.cellTris[k];
                Vector3 a = s.v[s.tri[3 * t]], b = s.v[s.tri[3 * t + 1]], cc = s.v[s.tri[3 * t + 2]];
                float d = (b.z - cc.z) * (a.x - cc.x) + (cc.x - b.x) * (a.z - cc.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float w1 = ((b.z - cc.z) * (x - cc.x) + (cc.x - b.x) * (z - cc.z)) / d;
                float w2 = ((cc.z - a.z) * (x - cc.x) + (a.x - cc.x) * (z - cc.z)) / d;
                float w3 = 1f - w1 - w2;
                if (w1 < -1e-4f || w2 < -1e-4f || w3 < -1e-4f) continue;
                float y = w1 * a.y + w2 * b.y + w3 * cc.y;
                if (y <= yTop && y > best) { best = y; bestRoad = s.road[t]; }
            }
        }

        static void DestroyMeshes(CityMeshes.TileMeshes tm)
        {
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.buildings, tm.lampPosts })
                if (m != null) UnityEngine.Object.DestroyImmediate(m);
        }

        // ---- the run ------------------------------------------------------
        [MenuItem("PSX Racing/City Launch Audit")]
        public static void Run()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var log = new StringBuilder();
            void Line(string s) { log.AppendLine(s); Debug.Log("[Launch] " + s); }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string label = Environment.GetEnvironmentVariable("PSX_LAUNCH_LABEL");
            if (string.IsNullOrEmpty(label)) label = "run";
            int status = 1;
            humpsOn = trenchesOn = spansOn = null;
            try
            {
                CityElevation.KeepTerrainProfiles = true;
                var map = CityMap.Get();
                CityElevation.KeepTerrainProfiles = false;
                if (map == null) throw new Exception("no city data");
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                Line($"CITY LAUNCH AUDIT ({label}) - graph {map.graphHash:x8}, {map.edges.Length} edges, {map.nodes.Length} nodes; solve {CityMap.LastSolveMs:0} ms ({CityElevation.LastSolvePhases})");
                Line($"judged at: motorway 150, trunk 130, primary 110, secondary 100, tertiary 90, local 70, ramps 70-90 km/h (turns capped by their plan radius, sqrt(G R)); race routes at least {RouteKmh:0} km/h. LAUNCH = the road falls {LaunchSepM:0.00} m below a free-flying car within {FlyM:0} m; UNLOAD = {UnloadSepM:0.00} m");
                Line(SurveyFit(map));
                Line("vertical curves: " + (CityElevation.VerticalCurvesOn ? CityElevation.VcurveReport : "OFF (PSX_CITY_VCURVES=0)"));

                X = new float[1 << 22]; Z = new float[1 << 22]; YE = new float[1 << 22]; YL = new float[1 << 22]; nS = 0;
                var bx = Environment.GetEnvironmentVariable("PSX_LAUNCH_BOX");
                box = Rect.MinMaxRect(-1e9f, -1e9f, 1e9f, 1e9f);
                if (!string.IsNullOrEmpty(bx))
                {
                    var b = bx.Split(',');
                    box = Rect.MinMaxRect(float.Parse(b[0], CultureInfo.InvariantCulture), float.Parse(b[1], CultureInfo.InvariantCulture), float.Parse(b[2], CultureInfo.InvariantCulture), float.Parse(b[3], CultureInfo.InvariantCulture));
                    Line($"DEBUG BOX {box}");
                }
                paths.Clear();

                // route membership: (edge, dir) and (node, in, out)
                var routeEdge = new Dictionary<long, int>();
                var routeMove = new Dictionary<long, int>();
                for (int r = 0; r < map.routes.Length; r++)
                {
                    var rt = map.routes[r];
                    for (int k = 0; k < rt.edges.Length; k++)
                    {
                        long key = ((long)rt.edges[k] << 1) | (rt.dirs[k] >= 0 ? 1L : 0L);
                        routeEdge[key] = (routeEdge.TryGetValue(key, out int m) ? m : 0) | (1 << r);
                        int kn = k + 1 < rt.edges.Length ? k + 1 : (rt.loop ? 0 : -1);
                        if (kn < 0) continue;
                        var e0 = map.edges[rt.edges[k]];
                        int node = rt.dirs[k] >= 0 ? e0.b : e0.a;
                        long mk = MoveKey(node, rt.edges[k], rt.edges[kn]);
                        routeMove[mk] = (routeMove.TryGetValue(mk, out int mm) ? mm : 0) | (1 << r);
                    }
                }

                // ALONG EVERY EDGE, each way it may be driven
                foreach (var e in map.edges)
                {
                    if (e.length < 3f) continue;
                    for (int dir = 1; dir >= -1; dir -= 2)
                    {
                        if (dir < 0 && e.oneway) continue;
                        float s0 = trims.atA[e.index], s1 = e.length - trims.atB[e.index];
                        if (s1 - s0 < 3f) continue;
                        var pr = new PathRec { kind = 0, start = nS, eA = e.index, dirA = dir, sA0 = dir > 0 ? s0 : s1 };
                        int n = Mathf.FloorToInt((s1 - s0) / StepM) + 1;
                        for (int k = 0; k < n; k++)
                        {
                            float s = dir > 0 ? s0 + k * StepM : s1 - k * StepM;
                            var t = e.TangentAt(s) * dir;
                            Add(e.PointAt(s) + RightOf(t) * LatFor(trims, e, s), e.YAt(s));
                        }
                        pr.count = nS - pr.start;
                        pr.nA = pr.count;
                        if (!box.Contains(new Vector2(X[pr.start], Z[pr.start]))) { nS = pr.start; continue; }
                        float v = ClassKmh(e) / 3.6f;
                        routeEdge.TryGetValue(((long)e.index << 1) | (dir > 0 ? 1L : 0L), out pr.routeMask);
                        if (pr.routeMask != 0) v = Mathf.Max(v, RouteKmh / 3.6f);
                        pr.v = v;
                        paths.Add(pr);
                    }
                }
                int edgePaths = paths.Count;

                // THROUGH EVERY NODE, by every allowed movement
                int moves = 0;
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    var arms = map.nodeEdges[n];
                    if (arms.Count < 2) continue;
                    foreach (int ia in arms)
                        foreach (int ib in arms)
                        {
                            if (ia == ib) continue;
                            var A = map.edges[ia]; var B = map.edges[ib];
                            if (A.a == A.b || B.a == B.b || A.length < 1f || B.length < 1f) continue;
                            int dA = A.b == n ? 1 : -1;          // travel along A toward the node
                            int dB = B.a == n ? 1 : -1;          // travel along B away from it
                            if (A.oneway && dA < 0) continue;
                            if (B.oneway && dB < 0) continue;
                            float tA = trims.TrimAt(A, n), tB = trims.TrimAt(B, n);
                            float sAn = A.b == n ? A.length : 0f, sBn = B.a == n ? 0f : B.length;
                            var inDir = A.TangentAt(A.b == n ? A.length - Mathf.Min(tA, A.length) : Mathf.Min(tA, A.length)) * dA;
                            var outDir = B.TangentAt(B.a == n ? Mathf.Min(tB, B.length) : B.length - Mathf.Min(tB, B.length)) * dB;
                            float cos = Vector2.Dot(inDir, outDir);
                            if (cos < -0.5f) continue;           // a U-turn between two arms
                            var pr = new PathRec { kind = (byte)(cos > 0.85f ? 1 : 2), start = nS, node = n, eA = ia, eB = ib, dirA = dA, dirB = dB };
                            // arm A: from (trim + approach) back to the trim
                            float from = Mathf.Min(A.length, tA + ApproachM);
                            int na = Mathf.Max(1, Mathf.FloorToInt((from - tA) / StepM) + 1);
                            pr.sA0 = A.b == n ? A.length - from : from;
                            for (int k = 0; k < na; k++)
                            {
                                float dist = Mathf.Max(tA, from - k * StepM);
                                float s = A.b == n ? A.length - dist : dist;
                                var t = A.TangentAt(s) * dA;
                                Add(A.PointAt(s) + RightOf(t) * LatFor(trims, A, s), A.YAt(s));
                            }
                            pr.nA = nS - pr.start;
                            // the fan: a cubic from A's trim to B's
                            float sAt = A.b == n ? A.length - tA : tA, sBt = B.a == n ? tB : B.length - tB;
                            var p0 = A.PointAt(sAt) + RightOf(inDir) * LatFor(trims, A, sAt);
                            var p3 = B.PointAt(sBt) + RightOf(outDir) * LatFor(trims, B, sBt);
                            float chord = Vector2.Distance(p0, p3);
                            float yFan = Mathf.Max(map.nodeY[n], Mathf.Max(A.YAt(sAt), B.YAt(sBt)));
                            float yFanLo = Mathf.Min(map.nodeY[n], Mathf.Min(A.YAt(sAt), B.YAt(sBt)));
                            float rMin = float.MaxValue;
                            if (chord > 0.6f)
                            {
                                float kk = chord * 0.45f;
                                Vector2 p1 = p0 + inDir * kk, p2 = p3 - outDir * kk;
                                int nf = Mathf.Max(2, Mathf.CeilToInt(chord * 1.2f / StepM));
                                Vector2 prev = p0, prevT = inDir;
                                for (int k = 1; k < nf; k++)
                                {
                                    float u = k / (float)nf, w = 1f - u;
                                    var q = w * w * w * p0 + 3f * w * w * u * p1 + 3f * w * u * u * p2 + u * u * u * p3;
                                    Add(q, yFan, yFanLo);
                                    var tq = (q - prev).normalized;
                                    float seg = Vector2.Distance(q, prev);
                                    float ang = Vector2.Angle(prevT, tq) * Mathf.Deg2Rad;
                                    if (ang > 1e-3f && seg > 0.05f) rMin = Mathf.Min(rMin, seg / ang);
                                    prev = q; prevT = tq;
                                }
                            }
                            pr.nF = nS - pr.start - pr.nA;
                            // arm B: from its trim out
                            float to = Mathf.Min(B.length, tB + ApproachM);
                            int nb = Mathf.Max(1, Mathf.FloorToInt((to - tB) / StepM) + 1);
                            pr.sB0 = sBt;
                            for (int k = 0; k < nb; k++)
                            {
                                float dist = Mathf.Min(to, tB + k * StepM);
                                float s = B.a == n ? dist : B.length - dist;
                                var t = B.TangentAt(s) * dB;
                                Add(B.PointAt(s) + RightOf(t) * LatFor(trims, B, s), B.YAt(s));
                            }
                            pr.count = nS - pr.start;
                            if (!box.Contains(new Vector2(X[pr.start], Z[pr.start]))) { nS = pr.start; continue; }
                            float v = Mathf.Min(ClassKmh(A), ClassKmh(B)) / 3.6f;
                            routeMove.TryGetValue(MoveKey(n, ia, ib), out pr.routeMask);
                            if (pr.routeMask != 0) v = Mathf.Max(v, RouteKmh / 3.6f);
                            if (pr.kind == 2 && rMin < float.MaxValue) v = Mathf.Min(v, Mathf.Sqrt(G * Mathf.Max(rMin, 2f)));
                            pr.v = v;
                            paths.Add(pr);
                            moves++;
                        }
                }
                Line($"paths: {edgePaths} along edges, {moves} movements through {map.nodes.Length} nodes; {nS} samples ({nS * StepM / 1000f:0} km) - built {clock.Elapsed.TotalSeconds:0} s");

                // ---- sample the BUILT meshes, tile row by tile row ----------
                Y = new float[nS];
                var isRoad = OnRoad = new bool[nS];
                var keys = new long[nS]; var idx = new int[nS];
                float T = CityMeshes.TileSize;
                for (int i = 0; i < nS; i++)
                {
                    int tx = Mathf.FloorToInt(X[i] / T), tz = Mathf.FloorToInt(Z[i] / T);
                    keys[i] = ((long)tz << 32) | (uint)(tx + 100000);
                    idx[i] = i;
                    Y[i] = float.NaN;
                }
                Array.Sort(keys, idx);
                var surfs = new Dictionary<long, Surf>();
                var built = new HashSet<long>();
                int tilesBuilt = 0, rowStart = 0;
                var missed = new int[3];
                var missEx = new[] { new List<string>(), new List<string>(), new List<string>() };
                long SK(int tx, int tz) => ((long)tz << 32) | (uint)(tx + 100000);
                while (rowStart < nS)
                {
                    int tzRow = (int)(keys[rowStart] >> 32);
                    int rowEnd = rowStart;
                    while (rowEnd < nS && (int)(keys[rowEnd] >> 32) == tzRow) rowEnd++;
                    // evict rows two back
                    var drop = new List<long>();
                    foreach (var k in surfs.Keys) if ((int)(k >> 32) < tzRow - 1) drop.Add(k);
                    foreach (var k in drop) surfs.Remove(k);
                    int g0 = rowStart;
                    while (g0 < rowEnd)
                    {
                        long key = keys[g0];
                        int g1 = g0;
                        while (g1 < rowEnd && keys[g1] == key) g1++;
                        int tx = (int)(uint)(key & 0xFFFFFFFF) - 100000;
                        var ring = new List<Surf>(9);
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                long k = SK(tx + dx, tzRow + dz);
                                if (!surfs.TryGetValue(k, out var sf))
                                {
                                    if (built.Contains(k) && (int)(k >> 32) < tzRow - 1) { continue; }
                                    var tm = CityMeshes.Build(map, trims, buildings, tx + dx, tzRow + dz);
                                    sf = SurfOf(tm);
                                    DestroyMeshes(tm);
                                    surfs[k] = sf;
                                    built.Add(k);
                                    tilesBuilt++;
                                }
                                if (sf != null) ring.Add(sf);
                            }
                        for (int q = g0; q < g1; q++)
                        {
                            int i = idx[q];
                            float best = float.NegativeInfinity; bool road = false;
                            float yTop = YE[i] + 0.6f;
                            foreach (var sf in ring) Query(sf, X[i], Z[i], yTop, ref best, ref road);
                            // a valid sample is ROAD within 0.6 m of where the path's own
                            // surface is; anything else is not this path's pavement (the
                            // line left the ribbon, a lower road under a deck, a hole)
                            int why = float.IsNegativeInfinity(best) || best < YL[i] - 3f ? 0 : !road ? 1 : best < YL[i] - 0.6f ? 2 : -1;
                            if (why >= 0)
                            {
                                missed[why]++;
                                if (missEx[why].Count < 12 && (i % 97) == 0)
                                {
                                    float any = float.NegativeInfinity; bool anyRoad = false;
                                    foreach (var sf in ring) Query(sf, X[i], Z[i], 1e6f, ref any, ref anyRoad);
                                    missEx[why].Add(string.Format(CultureInfo.InvariantCulture, "({0:0.0},{1:0.0}) expected {2:0.00}..{3:0.00}, first surface {4:0.00} {5}, top surface {6:0.00} {7}; {8}",
                                        X[i], Z[i], YL[i], YE[i], best, road ? "road" : "land", any, anyRoad ? "road" : "land", Whose(map, X[i], Z[i])));
                                }
                                continue;
                            }
                            Y[i] = best; isRoad[i] = road;
                        }
                        g0 = g1;
                    }
                    rowStart = rowEnd;
                    if ((tilesBuilt & 255) == 0) Debug.Log($"[Launch] row {tzRow}: {tilesBuilt} tiles built, {clock.Elapsed.TotalSeconds:0} s");
                }
                surfs.Clear();
                keys = null; idx = null;
                Line($"sampled on the built meshes: {tilesBuilt} tiles; {nS - missed[0] - missed[1] - missed[2]} samples on their own pavement; not: nothing within 3 m {missed[0]}, land first {missed[1]}, a road more than 0.6 m under the path's own {missed[2]} (all left out of the flight) - {clock.Elapsed.TotalSeconds:0} s");
                string[] missName = { "nothing", "land", "lower road" };
                for (int w = 0; w < 3; w++) foreach (var ex in missEx[w]) Line($"    miss ({missName[w]}): {ex}");

                // ---- fly a car off every sample ------------------------------
                var spots = new List<Spot>(20000);
                var sep = new float[4096];
                for (int pi = 0; pi < paths.Count; pi++)
                {
                    var pr = paths[pi];
                    if (pr.count < 6) continue;
                    if (sep.Length < pr.count) sep = new float[pr.count * 2];
                    for (int k = 0; k < pr.count; k++) sep[k] = Separation(pr.start, pr.count, k, pr.v, out _);
                    // one spot per run of samples past UNLOAD (gaps under 8 m join a run), at its worst
                    int kk = 0;
                    while (kk < pr.count)
                    {
                        if (sep[kk] < UnloadSepM) { kk++; continue; }
                        int best = kk, last = kk, q = kk + 1;
                        while (q < pr.count && q - last <= 8)
                        {
                            if (sep[q] >= UnloadSepM) { last = q; if (sep[q] > sep[best]) best = q; }
                            q++;
                        }
                        spots.Add(MakeSpot(map, trims, pr, pi, best, sep[best]));
                        kk = last + 1;
                    }
                }
                int offRoadSpots = 0; foreach (var s in spots) if (s.offRoad && s.kind == 2) offRoadSpots++;
                Line($"flown: {spots.Count} separations past {UnloadSepM:0.00} m on {paths.Count} paths ({offRoadSpots} turns with the break on land, not road: the turn's drawn line off the pavement, not counted) - {clock.Elapsed.TotalSeconds:0} s");

                // ---- de-duplicate by place (6 m), worst first ------------------
                spots.Sort((a, b) => b.sep.CompareTo(a.sep));
                var city = Dedupe(spots, 0);
                var routeSpots = new List<Spot>[map.routes.Length];
                for (int r = 0; r < map.routes.Length; r++) routeSpots[r] = Dedupe(spots, 1 << r);
                int cityLaunch = 0, cityUnload = 0;
                foreach (var s in city) { if (s.sep >= LaunchSepM) cityLaunch++; else cityUnload++; }
                Line("");
                Line($"CITY-WIDE: {cityLaunch} LAUNCH spots, {cityUnload} UNLOAD spots (distinct places, 6 m)");
                for (int r = 0; r < map.routes.Length; r++)
                {
                    int l = 0, u = 0;
                    foreach (var s in routeSpots[r]) { if (s.sep >= LaunchSepM) l++; else u++; }
                    Line($"ROUTE {map.routes[r].id} ({map.routes[r].lengthM / 1000f:0.0} km): {l} LAUNCH, {u} UNLOAD");
                }
                // by cause
                var byCause = new SortedDictionary<string, int[]>();
                foreach (var s in city)
                {
                    if (!byCause.TryGetValue(s.cause, out var c)) byCause[s.cause] = c = new int[4];
                    if (s.sep >= LaunchSepM) c[0]++; else c[1]++;
                    if (s.onRoute && s.sep >= LaunchSepM) c[2]++;
                    if (s.cls >= 4 && s.sep >= LaunchSepM) c[3]++;
                }
                Line("by cause (launch / unload / launch on a route / launch on a trunk or motorway):");
                foreach (var kv in byCause) Line($"  {kv.Key,-26} {kv.Value[0],6} {kv.Value[1],7} {kv.Value[2],6} {kv.Value[3],6}");
                var byKind = new int[3];
                foreach (var s in city) if (s.sep >= LaunchSepM) byKind[s.kind]++;
                Line($"launch spots by path: along an edge {byKind[0]}, through a node {byKind[1]}, turning {byKind[2]}");
                // grade break histogram of launch spots
                var hist = new int[6];
                foreach (var s in city) if (s.sep >= LaunchSepM) hist[Mathf.Clamp((int)(Mathf.Abs(s.gIn - s.gOut) * 100f / 3f), 0, 5)]++;
                Line($"launch spots by grade break over 5 m: <3% {hist[0]}, 3-6% {hist[1]}, 6-9% {hist[2]}, 9-12% {hist[3]}, 12-15% {hist[4]}, 15%+ {hist[5]}");

                Line("");
                Line("worst 60 (city-wide):");
                for (int i = 0; i < Mathf.Min(60, city.Count); i++) Line("  " + Describe(map, city[i], i + 1));
                Line("profiles of the worst 12 (every metre from 6 before the break to 10 past: mesh y, data y; F = in the fan):");
                for (int i = 0; i < Mathf.Min(12, city.Count); i++) Line("  #" + (i + 1) + " " + Profile(map, trims, city[i]));
                for (int r = 0; r < map.routes.Length; r++)
                {
                    Line($"route {map.routes[r].id}, every LAUNCH spot:");
                    int shown = 0;
                    foreach (var s in routeSpots[r]) if (s.sep >= LaunchSepM && shown++ < 40) { Line("  " + Describe(map, s, shown)); if (shown <= 3) Line("     " + Profile(map, trims, s)); }
                }
                Line("");
                Line(PlanKinks(map, trims, routeEdge));

                // every launch spot, for the record
                var csv = new StringBuilder("sep_m,liftoff_kmh,judged_kmh,x,z,y,lat,lon,kind,cause,route,edge,edge_name,cls,node,g_in,g_out,mesh_minus_data\n");
                foreach (var s in city)
                {
                    if (s.sep < LaunchSepM) continue;
                    var e = map.edges[s.edge];
                    csv.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0},{2:0},{3:0.0},{4:0.0},{5:0.00},{6},{7},{8},{9},{10},{11},\"{12}\",{13},{14},{15:0.000},{16:0.000},{17:0.00}\n",
                        s.sep, s.liftKmh, s.v * 3.6f, s.x, s.z, s.y, CityAudit.LatLon(s.x, s.z).Replace(",", " "), "", KindName(s.kind), s.cause, s.routeMask, s.edge, e.name, e.cls, s.node, s.gIn, s.gOut, s.meshOff));
                }
                File.WriteAllText(Path.Combine(root, "city_launch.csv"), csv.ToString());

                // the worst spots' paths, for the drive (routes first, then the city)
                var top = new List<Spot>();
                for (int r = 0; r < map.routes.Length; r++) foreach (var s in routeSpots[r]) if (s.sep >= LaunchSepM && top.Count < TopForDrive / 2 && !top.Exists(t => Near(t, s))) top.Add(s);
                foreach (var s in city) if (top.Count < TopForDrive && !top.Exists(t => Near(t, s))) top.Add(s);
                var drive = new StringBuilder();
                foreach (var s in top) drive.AppendLine(DrivePath(map, s));
                File.WriteAllText(Path.Combine(root, $"launch_top_{label}.txt"), drive.ToString());
                Line($"the drive's spots: launch_top_{label}.txt ({top.Count})");
                status = 0;
            }
            catch (Exception ex) { Line("THREW " + ex); Debug.LogException(ex); }
            finally { X = Z = YE = YL = Y = null; OnRoad = null; paths.Clear(); }
            Line($"done in {clock.Elapsed.TotalSeconds:0} s");
            File.WriteAllText(Path.Combine(root, "city_launch.txt"), log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(status);
        }

        /// <summary>Debug: PSX_LAUNCH_PROBE="x,z;x,z": the tile under each point
        /// built alone, its road and ground meshes' bounds, this audit's surface
        /// query and a physics ray on the attached tile, side by side.</summary>
        public static void Probe()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            foreach (var pt in (Environment.GetEnvironmentVariable("PSX_LAUNCH_PROBE") ?? "").Split(';'))
            {
                var c = pt.Split(',');
                if (c.Length < 2) continue;
                float x = float.Parse(c[0], CultureInfo.InvariantCulture), z = float.Parse(c[1], CultureInfo.InvariantCulture);
                int tx = Mathf.FloorToInt(x / CityMeshes.TileSize), tz = Mathf.FloorToInt(z / CityMeshes.TileSize);
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                sb.AppendLine($"probe ({x},{z}) tile {tx},{tz} origin {tm.origin}: roads {(tm.roads != null ? tm.roads.vertexCount + " v, sub " + tm.roads.subMeshCount + ", bounds " + tm.roads.bounds : "null")}; ground {(tm.ground != null ? tm.ground.vertexCount + " v, bounds " + tm.ground.bounds : "null")}");
                var sf = SurfOf(tm);
                float best = float.NegativeInfinity; bool road = false;
                if (sf != null) { Query(sf, x, z, 1e6f, ref best, ref road); sb.AppendLine($"   surf x0 {sf.x0:0.0} z0 {sf.z0:0.0} n {sf.nx}x{sf.nz}, tris {sf.tri.Length / 3}: query {best:0.00} {(road ? "road" : "land")}"); }
                var go = new GameObject("~probe"); go.transform.position = tm.origin;
                CityWorld.Attach(go, tm, null);
                Physics.SyncTransforms();
                foreach (var h in Physics.RaycastAll(new Vector3(x, 1000f, z), Vector3.down, 2000f))
                    sb.AppendLine($"   ray: {h.point.y:0.00} on {h.collider.name} (normal y {h.normal.y:0.00})");
                // the raw triangles of the roads mesh under the point, either winding
                int up = 0, down = 0;
                if (tm.roads != null)
                {
                    var v = tm.roads.vertices; var t = tm.roads.triangles;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = v[t[i]] + tm.origin, b = v[t[i + 1]] + tm.origin, cc = v[t[i + 2]] + tm.origin;
                        float d = (b.z - cc.z) * (a.x - cc.x) + (cc.x - b.x) * (a.z - cc.z);
                        if (Mathf.Abs(d) < 1e-9f) continue;
                        float w1 = ((b.z - cc.z) * (x - cc.x) + (cc.x - b.x) * (z - cc.z)) / d;
                        float w2 = ((cc.z - a.z) * (x - cc.x) + (a.x - cc.x) * (z - cc.z)) / d;
                        if (w1 < 0 || w2 < 0 || w1 + w2 > 1) continue;
                        float ny = (b.z - a.z) * (cc.x - a.x) - (b.x - a.x) * (cc.z - a.z);
                        if (ny > 0) up++; else down++;
                        if (up + down <= 4) sb.AppendLine($"   road tri at y {w1 * a.y + w2 * b.y + (1 - w1 - w2) * cc.y:0.00} ny {ny:0.000}");
                    }
                }
                sb.AppendLine($"   road triangles over the point: {up} up, {down} down");
                var near = new HashSet<int>();
                map.EdgeSegsInRect(new Vector2(x - 15f, z - 15f), new Vector2(x + 15f, z + 15f), near);
                var seen = new HashSet<int>();
                foreach (var packed in near)
                {
                    int oi = packed >> 12;
                    if (!seen.Add(oi)) continue;
                    var o = map.edges[oi];
                    CityElevation.ProjectOn(o, new Vector2(x, z), out float so);
                    float dist = Vector2.Distance(o.PointAt(so), new Vector2(x, z));
                    if (dist > 15f) continue;
                    sb.AppendLine($"   edge e{oi} '{o.name}'{(o.link ? " L" : "")} cls{o.cls} w {o.width:0.0} oneway {o.oneway} at {dist:0.0} m s={so:0}/{o.length:0} y {o.YAt(so):0.00}{(o.ElevatedAt(so) ? " deck" : "")} hw(trim) {trims.HalfWidthAt(o, so):0.0}{CityMeshes.DescribeClip(map, trims, o, so)}");
                }
                UnityEngine.Object.DestroyImmediate(go);
                DestroyMeshes(tm);
            }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_launch_probe.txt"), sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static readonly HashSet<int> whoScratch = new HashSet<int>();
        static string Whose(CityMap map, float x, float z)
        {
            var p = new Vector2(x, z);
            whoScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 20f, p + Vector2.one * 20f, whoScratch);
            float bd = float.MaxValue; int bi = -1; float bs = 0f;
            foreach (var packed in whoScratch)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[oi];
                Vector2 q0 = o.pts[si], dq = o.pts[si + 1] - q0;
                float L2 = dq.sqrMagnitude;
                float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, dq) / L2) : 0f;
                float dd = Vector2.Distance(p, q0 + dq * tt);
                if (dd < bd) { bd = dd; bi = oi; bs = o.s[si] + Mathf.Sqrt(L2) * tt; }
            }
            if (bi < 0) return "no edge within 20 m";
            var e = map.edges[bi];
            return $"nearest e{bi} '{e.name}'{(e.link ? " L" : "")} cls{e.cls} w {e.width:0.0} at {bd:0.0} m, s={bs:0}/{e.length:0}, its y {e.YAt(bs):0.00}{(e.ElevatedAt(bs) ? " deck" : "")}";
        }

        static long MoveKey(int node, int ein, int eout) => ((long)node << 40) ^ ((long)ein << 20) ^ eout;

        static string KindName(int k) => k == 0 ? "edge" : k == 1 ? "through" : "turn";

        // ---- the flight ---------------------------------------------------
        static float Separation(int start, int count, int k, float v, out int atX)
        {
            atX = 0;
            int b = Mathf.RoundToInt(PitchBaseM / StepM);
            int i = start + k;
            if (k < b || float.IsNaN(Y[i]) || float.IsNaN(Y[i - b])) return 0f;
            for (int q = i - b + 1; q <= i; q++) if (float.IsNaN(Y[q]) || Mathf.Abs(Y[q] - Y[q - 1]) > 0.25f * StepM + 0.05f) return 0f;
            float g0 = (Y[i] - Y[i - b]) / (b * StepM);
            float kq = G / (2f * v * v);
            int maxX = Mathf.Min(Mathf.RoundToInt(FlyM / StepM), count - 1 - k);
            float best = 0f;
            for (int x = 1; x <= maxX; x++)
            {
                float yi = Y[i + x];
                if (float.IsNaN(yi)) break;
                if (Mathf.Abs(yi - Y[i + x - 1]) > 0.25f * StepM + 0.05f) break;   // a height STEP (the drive audit's), not an angle
                float xb = x * StepM;
                float yb = Y[i] + g0 * xb - kq * xb * xb;
                float d = yb - yi;
                if (d > best) { best = d; atX = x; }
                if (d < -1.5f) break;
            }
            return best;
        }

        sealed class Spot
        {
            public float sep, liftKmh, v, x, z, y, gIn, gOut, meshOff;
            public int kind, edge, node, routeMask, pathIdx, k;
            public float s; public bool inFan, onRoute;
            public string cause; public int cls;
            /// <summary>The break sample hit land, not road: the movement's drawn line left the pavement (a turn cut across a kerb corner). Listed, not counted.</summary>
            public bool offRoad;
        }

        static Spot MakeSpot(CityMap map, CityMeshes.Trims trims, PathRec pr, int pathIdx, int k, float sep)
        {
            var sp = new Spot { sep = sep, v = pr.v, kind = pr.kind, node = pr.node, routeMask = pr.routeMask, onRoute = pr.routeMask != 0, pathIdx = pathIdx, k = k };
            // the break itself: the sharpest bend down near the departure
            int i0 = pr.start + k, bk = k;
            float worst = 0f;
            for (int q = Mathf.Max(2, k - 2); q <= Mathf.Min(pr.count - 3, k + 10); q++)
            {
                int i = pr.start + q;
                if (float.IsNaN(Y[i - 2]) || float.IsNaN(Y[i]) || float.IsNaN(Y[i + 2])) continue;
                float c = (Y[i + 2] - 2f * Y[i] + Y[i - 2]);
                if (c < worst) { worst = c; bk = q; }
            }
            int ib = pr.start + bk;
            sp.x = X[ib]; sp.z = Z[ib]; sp.y = float.IsNaN(Y[ib]) ? YE[ib] : Y[ib];
            // the mesh against the data around the break (outside a fan, where
            // the data has no one height): past 0.1 m it is the tile builder's
            // geometry the car meets there (a clip, a stitch, a neighbour's
            // ribbon), not the solved profile
            sp.meshOff = 0f;
            for (int q = Mathf.Max(0, bk - 6); q <= Mathf.Min(pr.count - 1, bk + 6); q++)
            {
                if (q >= pr.nA && q < pr.nA + pr.nF) continue;
                int i = pr.start + q;
                if (!float.IsNaN(Y[i]) && Mathf.Abs(Y[i] - YE[i]) > Mathf.Abs(sp.meshOff)) sp.meshOff = Y[i] - YE[i];
            }
            sp.offRoad = !OnRoad[ib] || (bk + 1 < pr.count && !OnRoad[ib + 1]);
            int a5 = Mathf.Max(pr.start, ib - 5), b5 = Mathf.Min(pr.start + pr.count - 1, ib + 5);
            sp.gIn = ib > a5 && !float.IsNaN(Y[a5]) ? (sp.y - Y[a5]) / ((ib - a5) * StepM) : 0f;
            sp.gOut = b5 > ib && !float.IsNaN(Y[b5]) ? (Y[b5] - sp.y) / ((b5 - ib) * StepM) : 0f;
            // lift-off speed: the slowest speed that separates LaunchSepM here
            float lo = 3f, hi = 120f;
            if (Separation(pr.start, pr.count, k, hi, out _) < LaunchSepM) sp.liftKmh = 999f;
            else
            {
                for (int it = 0; it < 22; it++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Separation(pr.start, pr.count, k, mid, out _) >= LaunchSepM) hi = mid; else lo = mid;
                }
                sp.liftKmh = hi * 3.6f;
            }
            // where on the graph
            if (pr.kind == 0) { sp.edge = pr.eA; sp.s = pr.sA0 + pr.dirA * bk * StepM; }
            else if (bk < pr.nA) { sp.edge = pr.eA; var A = map.edges[pr.eA]; float from = pr.dirA > 0 ? A.length - pr.sA0 : pr.sA0; float dist = Mathf.Max(trims.TrimAt(A, pr.node), from - bk * StepM); sp.s = A.b == pr.node ? A.length - dist : dist; }
            else if (bk < pr.nA + pr.nF) { sp.edge = pr.eA; sp.inFan = true; sp.s = pr.dirA > 0 ? map.edges[pr.eA].length : 0f; }
            else { sp.edge = pr.eB; var B = map.edges[pr.eB]; float dist = Mathf.Min(B.length, trims.TrimAt(B, pr.node) + (bk - pr.nA - pr.nF) * StepM); sp.s = B.a == pr.node ? dist : B.length - dist; }
            sp.cls = map.edges[sp.edge].cls;
            sp.cause = Cause(map, trims, sp);
            return sp;
        }

        static List<Spot> Dedupe(List<Spot> sorted, int routeBit)
        {
            var o = new List<Spot>();
            var seen = new HashSet<long>();
            foreach (var s in sorted)
            {
                if (routeBit != 0 && (s.routeMask & routeBit) == 0) continue;
                if (s.offRoad && s.kind == 2) continue;
                int cx = Mathf.FloorToInt(s.x / 6f), cz = Mathf.FloorToInt(s.z / 6f);
                bool dup = false;
                for (int dz = -1; dz <= 1 && !dup; dz++) for (int dx = -1; dx <= 1 && !dup; dx++)
                        if (seen.Contains(((long)(cx + dx) << 32) ^ (uint)(cz + dz))) dup = true;
                if (dup) continue;
                seen.Add(((long)cx << 32) ^ (uint)cz);
                o.Add(s);
            }
            return o;
        }

        static bool Near(Spot a, Spot b) => (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z) < 60f * 60f;

        // ---- why ------------------------------------------------------------
        static Dictionary<int, List<float>> humpsOn, trenchesOn, spansOn;

        static string Cause(CityMap map, CityMeshes.Trims trims, Spot sp)
        {
            if (humpsOn == null)
            {
                humpsOn = new Dictionary<int, List<float>>(); trenchesOn = new Dictionary<int, List<float>>(); spansOn = new Dictionary<int, List<float>>();
                var on = CityElevation.EnforcedCrossings; var tr = CityElevation.TrenchedCrossings;
                for (int i = 0; i < map.crossings.Length; i++)
                {
                    if (on != null && i < on.Length && !on[i]) continue;
                    var c = map.crossings[i];
                    bool trench = tr != null && i < tr.Length && tr[i];
                    var dict = trench ? trenchesOn : humpsOn;
                    int ei = trench ? c.under : c.over;
                    CityElevation.ProjectOn(map.edges[ei], c.at, out float sc);
                    if (!dict.TryGetValue(ei, out var l)) dict[ei] = l = new List<float>();
                    l.Add(sc);
                }
                foreach (var ws in map.wspans)
                {
                    if (!spansOn.TryGetValue(ws.edge, out var l)) spansOn[ws.edge] = l = new List<float>();
                    l.Add(ws.s0); l.Add(ws.s1);
                }
            }
            var e = map.edges[sp.edge];
            string seam = Mathf.Abs(Mathf.Repeat(sp.x + 1f, CityMeshes.TileSize) - 1f) < 1.2f || Mathf.Abs(Mathf.Repeat(sp.z + 1f, CityMeshes.TileSize) - 1f) < 1.2f ? " @seam" : "";
            if (Mathf.Abs(sp.meshOff) > 0.1f) seam += " @mesh-off-data";
            if (sp.offRoad) seam += " @on-land";
            if (sp.inFan) return "junction fan" + seam;
            float s = sp.s;
            int n = e.stS.Length;
            bool Near(Func<int, bool> f, float reach)
            {
                for (int i = 1; i < n; i++)
                    if (f(i) != f(i - 1) && Mathf.Abs(e.stS[i] - s) <= reach) return true;
                return false;
            }
            if (e.stSeat != null && Near(i => e.stSeat[i], 20f)) return "ramp seat (climb-out)" + seam;
            bool onBridge = e.bridge;
            if (!onBridge && Near(i => e.stElev[i], 15f)) return "deck/structure end" + seam;
            float trimA = trims.atA[e.index], trimB = trims.atB[e.index];
            bool nearNode = s <= trimA + 8f || s >= e.length - trimB - 8f;
            if (humpsOn.TryGetValue(e.index, out var hl)) foreach (var h in hl) if (Mathf.Abs(h - s) < 130f) return (nearNode ? "crossing hump at a node" : "crossing hump") + seam;
            if (trenchesOn.TryGetValue(e.index, out var tl)) foreach (var h in tl) if (Mathf.Abs(h - s) < 160f) return "trench" + seam;
            if (spansOn.TryGetValue(e.index, out var sl)) foreach (var h in sl) if (Mathf.Abs(h - s) < 80f) return "water span" + seam;
            if (onBridge) return "bridge deck" + seam;
            var prof = CityElevation.TerrainProfiles;
            float raised = 0f;
            if (prof != null && sp.edge < prof.Length && prof[sp.edge] != null)
            {
                var p = prof[sp.edge];
                int lo = 0; while (lo + 1 < n - 1 && e.stS[lo + 1] <= s) lo++;
                float t = (s - e.stS[lo]) / Mathf.Max(1e-3f, e.stS[Mathf.Min(lo + 1, n - 1)] - e.stS[lo]);
                raised = e.YAt(s) - Mathf.Lerp(p[lo], p[Mathf.Min(lo + 1, n - 1)], Mathf.Clamp01(t));
            }
            if (nearNode)
            {
                int node = s < e.length * 0.5f ? e.a : e.b;
                string kind = trims.patch[node] ? "junction node" : trims.mitre[node] ? "mitred joint" : "node";
                return (raised > 0.5f ? kind + " (raised: cone)" : kind) + seam;
            }
            if (raised > 0.5f) return "raised (approach cone)" + seam;
            if (raised < -0.5f) return "lowered (trench/seat cut)" + seam;
            return "terrain profile" + seam;
        }

        static string Describe(CityMap map, Spot s, int rank)
        {
            var e = map.edges[s.edge];
            string rt = "";
            for (int r = 0; r < map.routes.Length; r++) if ((s.routeMask & (1 << r)) != 0) rt += " ROUTE " + map.routes[r].id;
            return string.Format(CultureInfo.InvariantCulture,
                "#{0} sep {1:0.00} m at {2:0} km/h (lifts off from {3:0} km/h) {4} e{5} '{6}'{7} cls{8} s={9:0}/{10:0}{11} at ({12:0},{13:0}) {14}; grade {15:+0.0;-0.0}% -> {16:+0.0;-0.0}%; {17}{18}",
                rank, s.sep, s.v * 3.6f, s.liftKmh, KindName(s.kind), s.edge, e.name, e.link ? " L" : "", e.cls, s.s, e.length,
                s.node >= 0 ? $" node {s.node} deg{map.nodeEdges[s.node].Count}" : "", s.x, s.z, CityAudit.LatLon(s.x, s.z),
                s.gIn * 100f, s.gOut * 100f, s.cause, rt);
        }

        static string Profile(CityMap map, CityMeshes.Trims trims, Spot s)
        {
            var pr = paths[s.pathIdx];
            var sb = new StringBuilder();
            // the break sample: nearest to (x, z)
            int bk = 0; float bd = float.MaxValue;
            for (int q = 0; q < pr.count; q++) { int i = pr.start + q; float d = (X[i] - s.x) * (X[i] - s.x) + (Z[i] - s.z) * (Z[i] - s.z); if (d < bd) { bd = d; bk = q; } }
            for (int q = Mathf.Max(0, bk - 6); q <= Mathf.Min(pr.count - 1, bk + 10); q++)
            {
                int i = pr.start + q;
                bool fan = q >= pr.nA && q < pr.nA + pr.nF;
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}{1:0.00}/{2:0.00} ", fan ? "F" : "", Y[i], YE[i]));
            }
            if (pr.kind != 0 && pr.node >= 0)
            {
                sb.Append($"| node {pr.node} y {map.nodeY[pr.node]:0.00} trims {trims.TrimAt(map.edges[pr.eA], pr.node):0.0}/{trims.TrimAt(map.edges[pr.eB], pr.node):0.0} patch {trims.patch[pr.node]} mitre {trims.mitre[pr.node]} arms:");
                foreach (int ei in map.nodeEdges[pr.node])
                {
                    var e = map.edges[ei];
                    bool atA = e.a == pr.node;
                    int i1 = atA ? 1 : e.stY.Length - 2, i0 = atA ? 0 : e.stY.Length - 1;
                    float h = Mathf.Abs(e.stS[Mathf.Clamp(i1, 0, e.stY.Length - 1)] - e.stS[i0]);
                    var u = atA ? e.TangentAt(0f) : -e.TangentAt(e.length);
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " e{0}{1} end {2:0.00} g {3:+0.0;-0.0}% over {4:0} m dir ({5:0.00},{6:0.00}){7}", ei, e.link ? "L" : "", e.stY[i0], h > 0.1f ? (e.stY[Mathf.Clamp(i1, 0, e.stY.Length - 1)] - e.stY[i0]) / h * 100f : 0f, h, u.x, u.y, e.SeatedAt(i0) ? " seated" : ""));
                }
            }
            else
            {
                var e = map.edges[s.edge];
                int lo = 0; while (lo + 1 < e.stS.Length - 1 && e.stS[lo + 1] <= s.s) lo++;
                sb.Append($"| e{e.index} stations near s={s.s:0}:");
                for (int i = Mathf.Max(0, lo - 2); i <= Mathf.Min(e.stS.Length - 1, lo + 3); i++) sb.Append($" {e.stS[i]:0}:{e.stY[i]:0.00}{(e.stElev[i] ? "d" : "")}{(e.SeatedAt(i) ? "s" : "")}");
                sb.Append(CityMeshes.DescribeClip(map, trims, e, s.s));
            }
            return sb.ToString();
        }

        /// <summary>One line per spot for CityLaunchDrive: id; judged km/h;
        /// the index of the break in the list; then x,y,z every 2 m from 80 m
        /// before the break to 40 m past it (the path's own samples).</summary>
        static string DrivePath(CityMap map, Spot s)
        {
            var pr = paths[s.pathIdx];
            var sb = new StringBuilder();
            int k = s.k;
            int a = Mathf.Max(0, k - 80), b = Mathf.Min(pr.count - 1, k + 40);
            // an edge path shorter than the run-up: prepend nothing, the drive starts where it can
            int brk = (k - a) / 2;
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0};{2:0.0};{3:0.000};{4};", s.x, s.z, s.v * 3.6f, s.sep, brk));
            for (int q = a; q <= b; q += 2)
            {
                int i = pr.start + q;
                float y = float.IsNaN(Y[i]) ? YE[i] : Y[i];
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00} ", X[i], y, Z[i]));
            }
            sb.Append(";" + map.edges[s.edge].name + " " + s.cause);
            return sb.ToString();
        }

        /// <summary>The solve's distance from the survey (the terrain profile
        /// the solve started from, before any structure): RMS and the share of
        /// stations moved more than a metre.</summary>
        static string SurveyFit(CityMap map)
        {
            var prof = CityElevation.TerrainProfiles;
            if (prof == null) return "survey fit: no terrain profile kept";
            double sum = 0; long n = 0, over1 = 0, over3 = 0; float max = 0f;
            foreach (var e in map.edges)
            {
                var p = prof[e.index];
                if (p == null || e.bridge) continue;
                for (int i = 0; i < e.stY.Length; i++)
                {
                    if (e.stElev[i]) continue;
                    float d = e.stY[i] - p[i];
                    sum += d * d; n++;
                    if (Mathf.Abs(d) > 1f) over1++;
                    if (Mathf.Abs(d) > 3f) over3++;
                    max = Mathf.Max(max, Mathf.Abs(d));
                }
            }
            return string.Format(CultureInfo.InvariantCulture, "survey fit (grounded stations vs the smoothed 3DEP profile they started from): RMS {0:0.000} m over {1} stations, {2:0.00}% moved > 1 m, {3:0.000}% > 3 m, max {4:0.00} m",
                Math.Sqrt(sum / Math.Max(1, n)), n, 100.0 * over1 / Math.Max(1, n), 100.0 * over3 / Math.Max(1, n), max);
        }

        /// <summary>Plan kinks: a vertex where the ribbon's heading turns
        /// more than 6 degrees between two segments of 3 m or more (a corner
        /// drawn as a corner, not an arc), outside fans, and the heading step
        /// across every mitred joint; judged at the class speed as a lateral
        /// velocity step v x dtheta.</summary>
        static string PlanKinks(CityMap map, CityMeshes.Trims trims, Dictionary<long, int> routeEdge)
        {
            int kinks = 0, routeKinks = 0, joints = 0, routeJoints = 0;
            var worst = new List<(float dv, string what)>();
            foreach (var e in map.edges)
            {
                float s0 = trims.atA[e.index], s1 = e.length - trims.atB[e.index];
                bool onRoute = routeEdge.ContainsKey(((long)e.index << 1) | 1L) || routeEdge.ContainsKey((long)e.index << 1);
                float v = (onRoute ? Mathf.Max(ClassKmh(e), RouteKmh) : ClassKmh(e)) / 3.6f;
                for (int i = 1; i + 1 < e.pts.Length; i++)
                {
                    if (e.s[i] < s0 || e.s[i] > s1) continue;
                    Vector2 d0 = e.pts[i] - e.pts[i - 1], d1 = e.pts[i + 1] - e.pts[i];
                    if (d0.magnitude < 3f || d1.magnitude < 3f) continue;
                    float ang = Vector2.Angle(d0, d1);
                    if (ang < 6f) continue;
                    kinks++; if (onRoute) routeKinks++;
                    worst.Add((v * ang * Mathf.Deg2Rad, $"vertex e{e.index} '{e.name}' s={e.s[i]:0} {ang:0.0} deg at ({e.pts[i].x:0},{e.pts[i].y:0}){(onRoute ? " ROUTE" : "")}"));
                }
            }
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (!trims.mitre[n] || trims.throughA[n] < 0 || trims.throughB[n] < 0) continue;
                var A = map.edges[trims.throughA[n]]; var B = map.edges[trims.throughB[n]];
                Vector2 ta = A.b == n ? A.TangentAt(A.length) : -A.TangentAt(0f);
                Vector2 tb = B.a == n ? B.TangentAt(0f) : -B.TangentAt(B.length);
                float ang = Vector2.Angle(ta, tb);
                if (ang < 6f) continue;
                bool onRoute = routeEdge.ContainsKey(((long)A.index << 1) | 1L) || routeEdge.ContainsKey((long)A.index << 1);
                joints++; if (onRoute) routeJoints++;
                worst.Add((ClassKmh(A) / 3.6f * ang * Mathf.Deg2Rad, $"joint node {n} e{A.index}->e{B.index} '{A.name}' {ang:0.0} deg at ({map.nodes[n].x:0},{map.nodes[n].y:0}){(onRoute ? " ROUTE" : "")}"));
            }
            worst.Sort((a, b) => b.dv.CompareTo(a.dv));
            var sb = new StringBuilder();
            sb.AppendLine($"PLAN KINKS (a vertex turning > 6 deg between two 3 m+ segments, outside fans): {kinks} ({routeKinks} on a route edge); mitred joints turning > 6 deg: {joints} ({routeJoints} on a route)");
            for (int i = 0; i < Mathf.Min(12, worst.Count); i++) sb.AppendLine($"  {worst[i].dv:0.0} m/s lateral step: {worst[i].what}");
            return sb.ToString().TrimEnd();
        }
    }
}
