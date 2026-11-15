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
    /// THE CONCRETE INVENTORY (2026-11-15, the owner: "can you see this massacre
    /// of unnecessary concrete median pieces? this is still an issue all across
    /// the city after weeks of attempts to correct them"). Every earlier audit
    /// looked along lane lines and counted one rule's class. This one asks the
    /// owner's question of EVERY piece of standing concrete the tiles build,
    /// whatever rule asked for it: "why is this concrete here?"
    ///
    /// It reads the concrete gate's own records (CityMeshes.concreteLog: every
    /// piece that entered the barriers mesh, as asked and as let through), from
    /// the tiles the game itself builds, then from the WHOLE map (so tiles
    /// agree at their seams) joins the pieces into RUNS (ends within 0.75 m),
    /// finds what each piece stands on and its REASON:
    ///   R1 divides opposing traffic of a divided road, R2 guards a fall (a
    ///   structure's edge, a drop beside it) or holds land, R4 a kerb.
    /// and classes the strays, in the owner's words:
    ///   a ON-PAVEMENT   pavement past both its faces at its own level
    ///   b NO-REASON     none of R1-R4 where it stands
    ///   c ORPHAN        a run under 30 m (MinMedianRunM, now for every kind)
    ///   d DOUBLE/BOX    two walls side by side, no lane and no fall between
    ///   e STARTS-STOPS  a gap between two run ends in line, nothing real opening in it
    ///   f BAD-END       a free end standing square (over 0.3 m) within reach of pavement
    ///   g UP-AND-DOWN   a step or a dip in a run's top at a joint
    ///   h OVERSIZE      taller than its kind; and TALL: top over 1.35 m above the lane beside it
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.RunConcrete
    /// PSX_CONC_TAG names the report (city_concrete_&lt;tag&gt;.txt, _pieces.tsv,
    /// _tiles.tsv, _clusters.tsv); PSX_CONC_NEAR "x,z,r" restricts the tiles;
    /// PSX_CONC_MINUTES caps the tile building (default 70; from the owner's
    /// deck outward). With the gate on, one pass gives BEFORE (as asked) and
    /// AFTER (as built).
    /// </summary>
    public static partial class CityAudit
    {
        const float CnTie = 0.75f, CnTieY = 0.8f, CnOrphanM = 30f, CnCell = 4f;
        const int CnPave = 1, CnNoReason = 2, CnOrphan = 4, CnDouble = 8, CnGap = 16, CnBadEnd = 32, CnUpDown = 64, CnOversize = 128, CnTall = 256;
        static readonly string[] CnNames = { "a ON-PAVEMENT", "b NO-REASON", "c ORPHAN", "d DOUBLE/BOX", "e STARTS-AND-STOPS", "f BAD-END", "g UP-AND-DOWN", "h OVERSIZE", "h2 TALL-OVER-LANE" };
        static readonly Vector2 CnDeck = new Vector2(-7151f, 7028f);   // the owner's deck: I-85 at Freedom Drive

        sealed class CnResult
        {
            public List<CityMeshes.ConcretePiece> p;
            public int[] run, cls; public byte[] reason; public float[] runLen; public bool[] freeA, freeB;
            public int runs;
        }

        static CityMap cnMap; static CityMeshes.Trims cnTrims;
        static Vector2 CnXZ(Vector3 v) => new Vector2(v.x, v.z);
        static long CnKey(float x, float z, float cell) => ((long)Mathf.FloorToInt(x / cell) << 32) ^ (uint)Mathf.FloorToInt(z / cell);
        static bool CnLine(CityMeshes.ConKind k) => k <= CityMeshes.ConKind.InsetJersey || k == CityMeshes.ConKind.WBeam;
        static bool CnConcrete(CityMeshes.ConKind k) => k <= CityMeshes.ConKind.InsetJersey;
        static bool CnProbe(Vector2 q, float y, bool loose, out int who, out Vector2 travel, out float yLane) =>
            CityMeshes.ConcretePaveProbe(cnMap, cnTrims, q, y, loose, out who, out travel, out yLane);

        static int CnFind(int[] par, int i) { while (par[i] != i) { par[i] = par[par[i]]; i = par[i]; } return i; }

        /// <summary>Plan distance from q to the segment a-b and the parameter of the nearest point.</summary>
        static float CnSegDist(Vector2 q, Vector2 a, Vector2 b, out float t)
        {
            var d = b - a; float L2 = d.sqrMagnitude;
            t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - a, d) / L2) : 0f;
            return Vector2.Distance(q, a + d * t);
        }

        static byte CnReason(in CityMeshes.ConcretePiece p)
        {
            float h = Mathf.Max(p.hA, p.hB);
            if (h <= 0.2f) return 4;
            if (p.elev) return 2;
            Vector2 a2 = CnXZ(p.a), b2 = CnXZ(p.b), m = (a2 + b2) * 0.5f;
            float y = 0.5f * (p.a.y + p.b.y);
            var d = b2 - a2; float len = d.magnitude;
            if (len < 0.05f) return 0;
            d /= len;
            var perp = new Vector2(d.y, -d.x);
            for (int sgn = -1; sgn <= 1; sgn += 2)
                for (int k = 0; k < 3; k++)
                {
                    float o = k == 0 ? 1.5f : k == 1 ? 3f : 5f;
                    float g = CityElevation.GroundY(cnMap, m.x + perp.x * sgn * o, m.y + perp.y * sgn * o);
                    if (g < y - 0.6f) return 2;
                    if (k < 2 && g > y + 0.3f) return 3;
                }
            bool h0 = false, h1 = false; int w0 = 0, w1 = 0; Vector2 t0 = default, t1 = default;
            for (float o = p.half + 0.3f; o <= 14f && !h0; o += 0.75f) h0 = CnProbe(m + perp * o, y, true, out w0, out t0, out _);
            for (float o = p.half + 0.3f; o <= 14f && !h1; o += 0.75f) h1 = CnProbe(m - perp * o, y, true, out w1, out t1, out _);
            if (h0 && h1 && w0 >= 0 && w1 >= 0 && Vector2.Dot(t0, t1) < -0.5f && Mathf.Abs(Vector2.Dot(t0, d)) > 0.7f) return 1;
            return 0;
        }

        static CnResult CnClassify(List<CityMeshes.ConcretePiece> P, HashSet<long> built)
        {
            int n = P.Count;
            var R = new CnResult { p = P, run = new int[n], cls = new int[n], reason = new byte[n], runLen = new float[n], freeA = new bool[n], freeB = new bool[n] };
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                R.run[i] = i;
                if (!CnLine(P[i].kind)) continue;
                Vector2 a = CnXZ(P[i].a), b = CnXZ(P[i].b);
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - CnTie) / CnCell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + CnTie) / CnCell);
                int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - CnTie) / CnCell), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + CnTie) / CnCell);
                if ((x1 - x0 + 1) * (z1 - z0 + 1) > 400) continue;
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        long k = ((long)x << 32) ^ (uint)z;
                        if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(4);
                        l.Add(i);
                    }
            }
            // ---- runs: ends tied to another piece (end to end, or a T onto its body)
            var joints = new List<(int i, int ei, int j, int ej)>();   // end-to-end ties (ei, ej: 0 = a, 1 = b)
            float T = CityMeshes.TileSize;
            for (int i = 0; i < n; i++)
            {
                if (!CnLine(P[i].kind)) continue;
                for (int end = 0; end < 2; end++)
                {
                    var e3 = end == 0 ? P[i].a : P[i].b; var e2 = CnXZ(e3);
                    bool tied = false;
                    if (grid.TryGetValue(CnKey(e2.x, e2.y, CnCell), out var l))
                        foreach (int j in l)
                        {
                            if (j == i) continue;
                            Vector2 a = CnXZ(P[j].a), b = CnXZ(P[j].b);
                            float dist = CnSegDist(e2, a, b, out float t);
                            if (dist > CnTie || Mathf.Abs(Mathf.Lerp(P[j].a.y, P[j].b.y, t) - e3.y) > CnTieY) continue;
                            tied = true;
                            int ri = CnFind(R.run, i), rj = CnFind(R.run, j);
                            if (ri != rj) R.run[ri] = rj;
                            float lj = Vector2.Distance(a, b);
                            if (i < j && (t * lj < CnTie || (1f - t) * lj < CnTie)) joints.Add((i, end, j, t * lj < CnTie ? 0 : 1));
                        }
                    if (!tied && built != null)
                    {
                        // an end on the border of a tile that was not built: its run may carry on there
                        float fx = e2.x - Mathf.Floor(e2.x / T) * T, fz = e2.y - Mathf.Floor(e2.y / T) * T;
                        int tx = Mathf.FloorToInt(e2.x / T), tz = Mathf.FloorToInt(e2.y / T);
                        if ((fx < 1f && !built.Contains(BrKey(tx - 1, tz))) || (fx > T - 1f && !built.Contains(BrKey(tx + 1, tz))) ||
                            (fz < 1f && !built.Contains(BrKey(tx, tz - 1))) || (fz > T - 1f && !built.Contains(BrKey(tx, tz + 1)))) tied = true;
                    }
                    if (end == 0) R.freeA[i] = !tied; else R.freeB[i] = !tied;
                }
            }
            var lenOf = new Dictionary<int, float>();
            for (int i = 0; i < n; i++)
            {
                if (!CnLine(P[i].kind)) continue;
                int r = CnFind(R.run, i);
                lenOf.TryGetValue(r, out float L);
                lenOf[r] = L + Vector2.Distance(CnXZ(P[i].a), CnXZ(P[i].b));
            }
            R.runs = lenOf.Count;
            // ---- per piece: on pavement, reason, orphan, oversize
            for (int i = 0; i < n; i++)
            {
                var p = P[i];
                if (!CnConcrete(p.kind)) continue;
                int r = CnFind(R.run, i);
                R.runLen[i] = lenOf[r];
                float len = Vector2.Distance(CnXZ(p.a), CnXZ(p.b));
                if (p.paveM >= 0.25f) R.cls[i] |= CnPave;
                R.reason[i] = CnReason(p);
                if (R.reason[i] == 0 && (R.cls[i] & CnPave) == 0) R.cls[i] |= CnNoReason;
                if (R.runLen[i] < CnOrphanM && Mathf.Max(p.hA, p.hB) > 0.2f) R.cls[i] |= CnOrphan;
                float cap = p.kind == CityMeshes.ConKind.Rail ? CityMeshes.RailH : CityMeshes.BarrierH;
                if (Mathf.Max(p.hA, p.hB) > cap + 0.03f || p.half * 2f > 0.75f) R.cls[i] |= CnOversize;
                if (len >= 0.05f)
                {
                    Vector2 a2 = CnXZ(p.a), b2 = CnXZ(p.b), m = (a2 + b2) * 0.5f, d = (b2 - a2) / len, perp = new Vector2(d.y, -d.x);
                    float y = 0.5f * (p.a.y + p.b.y), top = y + 0.5f * (p.hA + p.hB);
                    for (int q = 0; q < 4; q++)
                    {
                        float o = (q < 2 ? p.half + 0.6f : p.half + 1.8f) * ((q & 1) == 0 ? 1f : -1f);
                        if (CnProbe(m + perp * o, y, true, out _, out _, out float yl) && top - yl > 1.35f && top - yl < 3.5f) { R.cls[i] |= CnTall; break; }
                    }
                }
            }
            // ---- double / box
            var seen = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                var p = P[i];
                if (!CnConcrete(p.kind) || Mathf.Max(p.hA, p.hB) <= 0.3f || (R.cls[i] & CnPave) != 0) continue;
                Vector2 a2 = CnXZ(p.a), b2 = CnXZ(p.b); float len = Vector2.Distance(a2, b2);
                if (len < 0.5f) continue;
                Vector2 m = (a2 + b2) * 0.5f, d = (b2 - a2) / len, perp = new Vector2(d.y, -d.x);
                float y = 0.5f * (p.a.y + p.b.y);
                seen.Clear();
                bool dbl = false;
                for (float o = -9.5f; o <= 9.5f && !dbl; o += 1.9f)
                {
                    var q = m + perp * o;
                    if (!grid.TryGetValue(CnKey(q.x, q.y, CnCell), out var l)) continue;
                    foreach (int j in l)
                    {
                        if (j == i || !seen.Add(j)) continue;
                        var pj = P[j];
                        if (Mathf.Max(pj.hA, pj.hB) <= 0.3f) continue;
                        Vector2 aj = CnXZ(pj.a), bj = CnXZ(pj.b); float lj = Vector2.Distance(aj, bj);
                        if (lj < 0.5f) continue;
                        var dj = (bj - aj) / lj;
                        if (Mathf.Abs(Vector2.Dot(d, dj)) < 0.94f) continue;
                        float u = Vector2.Dot(m - aj, dj);
                        if (u < 0f || u > lj) continue;
                        var foot = aj + dj * u;
                        float lat = Vector2.Distance(m, foot);
                        if (lat < 0.6f || lat > 9.5f) continue;
                        float yj = Mathf.Lerp(pj.a.y, pj.b.y, u / lj);
                        if (Mathf.Abs(yj - y) > 1.5f) continue;
                        bool between = false;
                        for (int k = 1; k <= 3 && !between; k++)
                        {
                            var bq = Vector2.Lerp(m, foot, k / 4f);
                            if (CnProbe(bq, Mathf.Lerp(y, yj, k / 4f), true, out _, out _, out _)) between = true;
                            else if (k == 2 && CityElevation.GroundY(cnMap, bq.x, bq.y) < Mathf.Min(y, yj) - 1.5f) between = true;   // a real gap between two decks
                        }
                        if (between) continue;
                        dbl = true;
                        if (CnConcrete(pj.kind)) R.cls[j] |= CnDouble;
                        break;
                    }
                }
                if (dbl) R.cls[i] |= CnDouble;
            }
            // ---- free ends: bad ends, and gaps between two ends in line
            var ends = new List<(int i, Vector3 p, Vector2 outw, float h)>();
            var endGrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var p = P[i];
                if (!CnConcrete(p.kind)) continue;
                Vector2 a2 = CnXZ(p.a), b2 = CnXZ(p.b); float len = Vector2.Distance(a2, b2);
                if (len < 0.05f) continue;
                var d = (b2 - a2) / len;
                for (int end = 0; end < 2; end++)
                {
                    if (end == 0 ? !R.freeA[i] : !R.freeB[i]) continue;
                    var e3 = end == 0 ? p.a : p.b; float h = end == 0 ? p.hA : p.hB;
                    var e2 = CnXZ(e3);
                    if (h > 0.3f && (R.cls[i] & CnPave) == 0)
                    {
                        bool reach = CnProbe(e2 + (end == 0 ? -d : d) * 1.2f, e3.y, false, out _, out _, out _);
                        for (int k = 0; k < 8 && !reach; k++)
                        {
                            float ang = k * Mathf.PI / 4f;
                            reach = CnProbe(e2 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 1.5f, e3.y, false, out _, out _, out _);
                        }
                        if (reach) R.cls[i] |= CnBadEnd;
                    }
                    long k2 = CnKey(e2.x, e2.y, 40f);
                    if (!endGrid.TryGetValue(k2, out var l)) endGrid[k2] = l = new List<int>(4);
                    l.Add(ends.Count);
                    ends.Add((i, e3, end == 0 ? -d : d, h));
                }
            }
            for (int a = 0; a < ends.Count; a++)
            {
                var ea = ends[a]; var pa = CnXZ(ea.p);
                int cx = Mathf.FloorToInt(pa.x / 40f), cz = Mathf.FloorToInt(pa.y / 40f);
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                    {
                        if (!endGrid.TryGetValue(((long)x << 32) ^ (uint)z, out var l)) continue;
                        foreach (int b in l)
                        {
                            if (b <= a) continue;
                            var eb = ends[b];
                            if (CnFind(R.run, ea.i) == CnFind(R.run, eb.i)) continue;
                            var v = CnXZ(eb.p) - pa; float dist = v.magnitude;
                            if (dist < CnTie || dist > 40f || Mathf.Abs(eb.p.y - ea.p.y) > 1.5f) continue;
                            v /= dist;
                            if (Vector2.Dot(v, ea.outw) < 0.94f || Vector2.Dot(-v, eb.outw) < 0.94f) continue;
                            // does something real open in the gap: pavement passing through it
                            var perp = new Vector2(v.y, -v.x);
                            bool opens = false;
                            for (float s = 0.5f; s < dist && !opens; s += 1f)
                            {
                                var q = pa + v * s; float y = Mathf.Lerp(ea.p.y, eb.p.y, s / dist);
                                opens = CnProbe(q + perp * 0.9f, y, false, out _, out _, out _) && CnProbe(q - perp * 0.9f, y, false, out _, out _, out _);
                            }
                            if (opens) continue;
                            R.cls[ea.i] |= CnGap; R.cls[eb.i] |= CnGap;
                        }
                    }
            }
            // ---- up and down: a step or a dip at an end-to-end joint in line
            foreach (var (i, ei, j, ej) in joints)
            {
                var pi = P[i]; var pj = P[j];
                if (!CnConcrete(pi.kind) || !CnConcrete(pj.kind)) continue;
                Vector2 di = (CnXZ(pi.b) - CnXZ(pi.a)).normalized, dj = (CnXZ(pj.b) - CnXZ(pj.a)).normalized;
                if (Mathf.Abs(Vector2.Dot(di, dj)) < 0.87f) continue;
                float hi = ei == 0 ? pi.hA : pi.hB, hj = ej == 0 ? pj.hA : pj.hB;
                float ti = (ei == 0 ? pi.a.y : pi.b.y) + hi, tj = (ej == 0 ? pj.a.y : pj.b.y) + hj;
                bool step = Mathf.Abs(ti - tj) > 0.15f;
                bool dip = hi < 0.45f && hj < 0.45f && Mathf.Max(pi.hA, pi.hB) > 0.6f && Mathf.Max(pj.hA, pj.hB) > 0.6f;
                if (step || dip) { R.cls[i] |= CnUpDown; R.cls[j] |= CnUpDown; }
            }
            for (int i = 0; i < n; i++) R.run[i] = CnFind(R.run, i);
            return R;
        }

        static void CnDestroy(CityMeshes.TileMeshes tm)
        {
            if (tm == null) return;
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings, tm.buildingCollider, tm.structure, tm.medianPlants })
                if (m != null) UnityEngine.Object.DestroyImmediate(m);
        }

        static string CnLatLon(Vector2 p) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.00000},{1:0.00000}", p.y / 111132.0 + 35.18456015184093, p.x / (111320.0 * Math.Cos(35.18456 * Math.PI / 180.0)) - 80.81770185962013);

        /// <summary>The driver's seat u metres along e (before its start: on the road it comes from).</summary>
        static Vector3 CnSeat(CityMap map, CityMap.Edge e, float u)
        {
            var ee = e; float s = Mathf.Clamp(u, 0.5f, Mathf.Max(0.5f, e.length - 0.5f)); bool f = true;
            if (u < 0.5f)
            {
                Vector2 dirE = e.TangentAt(0f);
                CityMap.Edge best = null; float bestDot = -0.3f;
                foreach (int oi in map.nodeEdges[e.a])
                {
                    if (oi == e.index) continue;
                    var o = map.edges[oi];
                    Vector2 dirO = o.a == e.a ? o.TangentAt(0f) : -o.TangentAt(o.length);
                    float dot = Vector2.Dot(dirE, dirO);
                    if (dot < bestDot) { bestDot = dot; best = o; }
                }
                if (best != null)
                {
                    float dd = Mathf.Min(0.5f - u, best.length - 0.5f);
                    ee = best;
                    if (best.a == e.a) { s = dd; f = false; } else { s = best.length - dd; f = true; }
                }
            }
            var p = ee.PointAt(s); var t = ee.TangentAt(s);
            LineModel.Extents(ee, s, out float eM, out float eP);
            if (!f) { t = -t; float q = eM; eM = eP; eP = q; }
            var right = new Vector2(-t.y, t.x);
            float lat = ee.oneway ? -eM + 0.5f * (eM + eP) : -eM * 0.5f;
            var w = p + right * lat;
            return new Vector3(w.x, ee.YAt(s), w.y);
        }

        public static void RunConcrete()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string tag = Environment.GetEnvironmentVariable("PSX_CONC_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "run";
            string file = Path.Combine(root, $"city_concrete_{tag}.txt");
            foreach (var f in new[] { file, Path.Combine(root, $"city_concrete_{tag}_pieces.tsv"), Path.Combine(root, $"city_concrete_{tag}_tiles.tsv"), Path.Combine(root, $"city_concrete_{tag}_clusters.tsv") })
                if (File.Exists(f)) File.Delete(f);
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            int code = 0;
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var map = CityMap.Get();
                if (map == null) throw new Exception("no city data");
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                cnMap = map; cnTrims = trims;
                float capMin = 70f;
                if (float.TryParse(Environment.GetEnvironmentVariable("PSX_CONC_MINUTES"), NumberStyles.Float, inv, out float cm)) capMin = cm;
                sb.AppendLine($"CITY CONCRETE INVENTORY ({tag}) {DateTime.Now:s}; graph hash {map.graphHash:x8} (GHSH); gate {(CityMeshes.ConcreteOn ? "ON" : "OFF")} (pave {(CityMeshes.ConcretePaveOn ? "on" : "off")}); BRIDGE2 {(CityMeshes.Bridge2On ? "ON" : "OFF")}; solved in {clock.Elapsed.TotalSeconds:0} s");

                // ---- the population: every tile with a freeway, trunk, ramp, structure or divided (one-way) road
                float T = CityMeshes.TileSize;
                var tiles = new HashSet<long>();
                int capable = 0;
                foreach (var e in map.edges)
                {
                    if (e.tunnel || e.pts == null || e.pts.Length < 2) continue;
                    bool elev = false;
                    if (e.stElev != null) foreach (bool b in e.stElev) if (b) { elev = true; break; }
                    if (!(e.cls >= 4 || e.link || e.oneway || elev)) continue;
                    capable++;
                    float pad = e.HalfMax + 3f;
                    for (int i = 0; i + 1 < e.pts.Length; i++)
                    {
                        Vector2 a = e.pts[i], b = e.pts[i + 1];
                        int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - pad) / T), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + pad) / T);
                        int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - pad) / T), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + pad) / T);
                        for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) tiles.Add(BrKey(x, z));
                    }
                }
                int popAll = tiles.Count;
                string near = Environment.GetEnvironmentVariable("PSX_CONC_NEAR");
                if (!string.IsNullOrWhiteSpace(near))
                {
                    var v = near.Split(',');
                    var c = new Vector2(float.Parse(v[0], inv), float.Parse(v[1], inv)); float r = float.Parse(v[2], inv);
                    tiles.RemoveWhere(k => { int tx = (int)(k >> 24), tz = (int)(k & 0xFFFFFF); if (tz >= 0x800000) tz -= 0x1000000; var tc = new Vector2((tx + 0.5f) * T, (tz + 0.5f) * T); return Vector2.Distance(tc, c) > r + T * 0.71f; });
                }
                var order = new List<long>(tiles);
                Vector2 TileC(long k) { int tx = (int)(k >> 24), tz = (int)(k & 0xFFFFFF); if (tz >= 0x800000) tz -= 0x1000000; return new Vector2((tx + 0.5f) * T, (tz + 0.5f) * T); }
                order.Sort((x, y) => Vector2.SqrMagnitude(TileC(x) - CnDeck).CompareTo(Vector2.SqrMagnitude(TileC(y) - CnDeck)));
                sb.AppendLine($"population: {capable} roads (class 4+, ramps, one-way carriageways, anything on structure) of {map.edges.Length}; {popAll} tiles hold one; {order.Count} taken{(string.IsNullOrWhiteSpace(near) ? "" : " (PSX_CONC_NEAR " + near + ")")}");

                // ---- the tiles
                var log = new List<CityMeshes.ConcretePiece>(1 << 16);
                CityMeshes.concreteLog = log;
                var built = new HashSet<long>();
                var tileRows = new StringBuilder("tx\tz\tms\tground_tris\tground_hash\troads_tris\troads_hash\tkerbs_tris\tkerbs_hash\tbarriers_tris\tbarriers_hash\tstructure_tris\tstructure_hash\tguardrails_tris\tguardrails_hash\tasked\tchanged\tpave_cut_m\tunknown_verts\n");
                double buildMs = 0; int unknownTiles = 0; long barrierTris = 0;
                foreach (long k in order)
                {
                    if (clock.Elapsed.TotalMinutes > capMin) break;
                    int tx = (int)(k >> 24), tz = (int)(k & 0xFFFFFF); if (tz >= 0x800000) tz -= 0x1000000;
                    var c = System.Diagnostics.Stopwatch.StartNew();
                    var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                    double ms = c.Elapsed.TotalMilliseconds;
                    buildMs += ms;
                    built.Add(k);
                    if (tm != null)
                    {
                        string Cell(Mesh m) => m == null ? "0\t0" : $"{m.triangles.Length / 3}\t{BrMeshHash(m):x16}";
                        tileRows.Append($"{tx}\t{tz}\t{ms:0}\t{Cell(tm.ground)}\t{Cell(tm.roads)}\t{Cell(tm.kerbs)}\t{Cell(tm.barriers)}\t{Cell(CityMeshes.StructureMeshOf(tm))}\t{Cell(tm.guardrails)}\t{CityMeshes.conAsked}\t{CityMeshes.conChanged}\t{CityMeshes.conPaveCutM.ToString("0.0", inv)}\t{CityMeshes.concreteUnknownVerts}\n");
                        if (CityMeshes.concreteUnknownVerts != 0) unknownTiles++;
                        if (tm.barriers != null) barrierTris += tm.barriers.triangles.Length / 3;
                        CnDestroy(tm);
                    }
                    if (built.Count % 500 == 0) { Debug.Log($"[Concrete] {built.Count}/{order.Count} tiles, {log.Count} pieces, {clock.Elapsed.TotalMinutes:0.0} min"); Resources.UnloadUnusedAssets(); }
                }
                CityMeshes.concreteLog = null;
                File.WriteAllText(Path.Combine(root, $"city_concrete_{tag}_tiles.tsv"), tileRows.ToString());
                sb.AppendLine($"tiles built: {built.Count} of {order.Count} in {buildMs / 1000:0} s ({buildMs / Mathf.Max(1, built.Count):0} ms a tile); barriers mesh {barrierTris} triangles; tiles with barrier vertices no gate call accounts for: {unknownTiles}");

                // ---- before (as asked) and after (as built)
                var before = new List<CityMeshes.ConcretePiece>(log.Count);
                var after = new List<CityMeshes.ConcretePiece>(log.Count);
                foreach (var p in log)
                {
                    if (p.stage != 1) before.Add(p);
                    if (p.stage != 0) after.Add(p);
                }
                bool two = before.Count != after.Count || log.Exists(p => p.stage != 2);
                var RB = CnClassify(before, built.Count < popAll ? built : null);
                var RA = two ? CnClassify(after, built.Count < popAll ? built : null) : RB;
                sb.AppendLine($"classified in {clock.Elapsed.TotalMinutes:0.0} min total");

                void Table(string title, CnResult R)
                {
                    int pieces = 0; float metres = 0f;
                    var cnt = new int[CnNames.Length]; var met = new float[CnNames.Length];
                    var byRule = new SortedDictionary<string, (int n, float m, int stray, float strayM, int[] c)>(StringComparer.Ordinal);
                    var reasons = new float[5];
                    int strayPieces = 0; float strayM = 0f;
                    for (int i = 0; i < R.p.Count; i++)
                    {
                        var p = R.p[i];
                        if (!CnConcrete(p.kind)) continue;
                        float len = Vector2.Distance(CnXZ(p.a), CnXZ(p.b));
                        pieces++; metres += len;
                        reasons[R.reason[i]] += len;
                        if (!byRule.TryGetValue(p.rule, out var br)) br = (0, 0f, 0, 0f, new int[CnNames.Length]);
                        br.n++; br.m += len;
                        if (R.cls[i] != 0) { strayPieces++; strayM += len; br.stray++; br.strayM += len; }
                        for (int b = 0; b < CnNames.Length; b++) if ((R.cls[i] & (1 << b)) != 0) { cnt[b]++; met[b] += len; br.c[b]++; }
                        byRule[p.rule] = br;
                    }
                    sb.AppendLine();
                    sb.AppendLine($"==== {title}: {pieces} pieces of standing concrete, {metres / 1000f:0.0} km, in {R.runs} runs; {strayPieces} pieces ({strayM / 1000f:0.00} km) in a stray class");
                    sb.AppendLine($"reason (km): R1 divides opposing traffic {reasons[1] / 1000f:0.0}; R2 guards a fall / on structure {reasons[2] / 1000f:0.0}; R2 holds land {reasons[3] / 1000f:0.0}; R4 kerb height {reasons[4] / 1000f:0.0}; none {reasons[0] / 1000f:0.0}");
                    sb.AppendLine("class\tpieces\tmetres");
                    for (int b = 0; b < CnNames.Length; b++) sb.AppendLine($"{CnNames[b]}\t{cnt[b]}\t{met[b]:0}");
                    sb.AppendLine("by rule:\tpieces\tmetres\tstray\tstray_m\ta\tb\tc\td\te\tf\tg\th\th2");
                    foreach (var kv in byRule) sb.AppendLine($"{kv.Key}\t{kv.Value.n}\t{kv.Value.m:0}\t{kv.Value.stray}\t{kv.Value.strayM:0}\t{string.Join("\t", kv.Value.c)}");
                }
                Table(two ? "BEFORE (as the rules asked)" : "INVENTORY", RB);
                if (two) Table("AFTER (as the gate built)", RA);

                // ---- clusters (200 m cells) of the BEFORE strays; the pieces file
                var cells = new Dictionary<long, (float w, int[] c, Vector2 sum, float sumW, int best, float bestW)>();
                var pieces = new StringBuilder("set\tclass_bits\trule\tkind\tedge\tside\tnode\tx\ty\tz\tlen\thA\thB\thalf\tpaveM\treason\trun\trunLen\tfreeA\tfreeB\ttx\ttz\n");
                void Dump(string set, CnResult R, bool cluster)
                {
                    for (int i = 0; i < R.p.Count; i++)
                    {
                        var p = R.p[i];
                        if (R.cls[i] == 0 || !CnConcrete(p.kind)) continue;
                        var m3 = (p.a + p.b) * 0.5f; float len = Vector2.Distance(CnXZ(p.a), CnXZ(p.b));
                        pieces.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7:0.0}\t{8:0.00}\t{9:0.0}\t{10:0.0}\t{11:0.00}\t{12:0.00}\t{13:0.00}\t{14:0.0}\tR{15}\t{16}\t{17:0}\t{18}\t{19}\t{20}\t{21}\n",
                            set, R.cls[i], p.rule, p.kind, p.edge, p.side, p.node, m3.x, m3.y, m3.z, len, p.hA, p.hB, p.half, p.paveM, R.reason[i], R.run[i], R.runLen[i], R.freeA[i] ? 1 : 0, R.freeB[i] ? 1 : 0, p.tx, p.tz));
                        if (!cluster) continue;
                        float w = Mathf.Max(1f, len) * ((R.cls[i] & CnPave) != 0 ? 3f : (R.cls[i] & (CnBadEnd | CnDouble)) != 0 ? 2f : 1f);
                        long k = CnKey(m3.x, m3.z, 200f);
                        if (!cells.TryGetValue(k, out var c)) c = (0f, new int[CnNames.Length], Vector2.zero, 0f, -1, 0f);
                        c.w += w; c.sum += CnXZ(m3) * w; c.sumW += w;
                        for (int b = 0; b < CnNames.Length; b++) if ((R.cls[i] & (1 << b)) != 0) c.c[b]++;
                        float pw = ((R.cls[i] & CnPave) != 0 ? 100f : 0f) + len;
                        if (pw > c.bestW) { c.bestW = pw; c.best = i; }
                        cells[k] = c;
                    }
                }
                Dump("before", RB, true);
                if (two) Dump("after", RA, false);
                File.WriteAllText(Path.Combine(root, $"city_concrete_{tag}_pieces.tsv"), pieces.ToString());

                var ranked = new List<long>(cells.Keys);
                ranked.Sort((x, y) => cells[y].w.CompareTo(cells[x].w));
                // the owner's deck first
                long deckKey = CnKey(CnDeck.x, CnDeck.y, 200f);
                float bestD = float.MaxValue;
                foreach (long k in cells.Keys) { float dd = Vector2.Distance(cells[k].sum / cells[k].sumW, CnDeck); if (dd < bestD) { bestD = dd; deckKey = k; } }
                if (bestD < 220f) { ranked.Remove(deckKey); ranked.Insert(0, deckKey); }
                var cl = new StringBuilder("rank\tid\tname\tlat_lon\tx\tz\tweight\ta\tb\tc\td\te\tf\tg\th\th2\teye_x\teye_y\teye_z\tlook_x\tlook_y\tlook_z\ttop_x\ttop_y\ttop_z\n");
                sb.AppendLine();
                sb.AppendLine($"==== the {Mathf.Min(40, ranked.Count)} worst clusters (200 m cells of the BEFORE strays; {ranked.Count} cells hold one)");
                sb.AppendLine("rank\tname\tlat,lon\tweight\ta\tb\tc\td\te\tf\tg\th\th2");
                var segs = new HashSet<int>();
                for (int r = 0; r < ranked.Count && r < 40; r++)
                {
                    var c = cells[ranked[r]];
                    var ctr = c.sum / c.sumW;
                    // its name: the two highest roads near its middle
                    segs.Clear();
                    map.EdgeSegsInRect(ctr - Vector2.one * 110f, ctr + Vector2.one * 110f, segs);
                    var names = new Dictionary<string, int>();
                    foreach (int packed in segs)
                    {
                        var o = map.edges[packed >> 12];
                        if (string.IsNullOrEmpty(o.name)) continue;
                        names.TryGetValue(o.name, out int sc);
                        names[o.name] = Mathf.Max(sc, o.cls * 10 + (o.link ? 0 : 5));
                    }
                    var nl = new List<string>(names.Keys);
                    nl.Sort((x, y) => names[y].CompareTo(names[x]));
                    string name = nl.Count == 0 ? "unnamed" : nl.Count == 1 ? nl[0] : nl[0] + " at " + nl[1];
                    // the driver's eye: on the worst piece's road, 26 m before it
                    var bp = RB.p[c.best];
                    var tgt = (bp.a + bp.b) * 0.5f;
                    CityMap.Edge pe = bp.edge >= 0 ? map.edges[bp.edge] : null;
                    if (pe == null)
                    {
                        float bd = float.MaxValue;
                        segs.Clear();
                        map.EdgeSegsInRect(CnXZ(tgt) - Vector2.one * 30f, CnXZ(tgt) + Vector2.one * 30f, segs);
                        foreach (int packed in segs)
                        {
                            var o = map.edges[packed >> 12];
                            float at = BrArcNear(o, CnXZ(tgt), out float dd);
                            if (Mathf.Abs(o.YAt(at) - tgt.y) > 1.2f || dd >= bd) continue;
                            bd = dd; pe = o;
                        }
                    }
                    Vector3 eye = tgt + new Vector3(-20f, 1.15f, 0f);
                    if (pe != null)
                    {
                        float at = BrArcNear(pe, CnXZ(tgt), out _);
                        eye = CnSeat(map, pe, at - 26f) + Vector3.up * 1.15f;
                    }
                    var look = tgt + Vector3.up * 0.5f;
                    string id = (r == 0 && bestD < 220f) ? "c00_deck" : $"c{r:00}";
                    sb.AppendLine(string.Format(inv, "{0}\t{1}\t{2}\t{3:0}\t{4}", r + 1, name, CnLatLon(ctr), c.w, string.Join("\t", c.c)));
                    cl.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3}\t{4:0.0}\t{5:0.0}\t{6:0}\t{7}\t{8:0.00}\t{9:0.00}\t{10:0.00}\t{11:0.00}\t{12:0.00}\t{13:0.00}\t{14:0.0}\t{15:0.0}\t{16:0.0}\n",
                        r + 1, id, name, CnLatLon(ctr), ctr.x, ctr.y, c.w, string.Join("\t", c.c), eye.x, eye.y, eye.z, look.x, look.y, look.z, ctr.x, tgt.y, ctr.y));
                }
                File.WriteAllText(Path.Combine(root, $"city_concrete_{tag}_clusters.tsv"), cl.ToString());
                sb.AppendLine();
                sb.AppendLine($"CITY CONCRETE INVENTORY OK ({clock.Elapsed.TotalMinutes:0.0} min)");
            }
            catch (Exception ex) { sb.AppendLine("CITY CONCRETE INVENTORY FAILED: " + ex); Debug.LogException(ex); code = 1; }
            finally
            {
                CityMeshes.concreteLog = null;
                File.WriteAllText(file, sb.ToString());
                Debug.Log("[Concrete]\n" + sb);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
