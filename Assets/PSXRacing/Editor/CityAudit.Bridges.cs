using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE BRIDGES AUDIT (2026-11-15, the owner: "review all roads for
    /// accuracy (including parking lots, bridges, neigborhoods, driveways). I
    /// plan to start adding more racing tracks in Charlotte today"). Every
    /// stretch of road ON STRUCTURE in the city (a RUN: an edge's stations the
    /// solve stood on a deck) and 30 m of each approach, measured on the tiles
    /// the game itself builds (CityMeshes.Build + CityWorld.Attach: the Roads
    /// mesh is the drawn surface AND the collider, one mesh; the render-only
    /// Structure / Kerbs / Banks / Water meshes get probe colliders here so a
    /// thing drawn and not solid is seen too). Per run:
    ///   SURFACE   a ray down every 0.5 m along every lane line (and the OSM
    ///             line): HOLE (nothing to drive on), STEP (over 2 cm between
    ///             two samples), KINK (the grade over 2 m before and after
    ///             differs by over 2 %), each apart AT A JOINT (within 6 m of
    ///             a structure end) - joints throw cars;
    ///   IN LANE   land / a drawn thing / an object standing over a lane's
    ///             surface or in the 4.5 m over it, a face square to the
    ///             travel on a lane line (a barrier across a lane or a mouth);
    ///   RAILS     a ray out from the outer lane every metre, both sides: on
    ///             structure a rail, or pavement carrying on (a union, a gore,
    ///             a mouth), else RAIL-DROP / RAIL-FLUSH; on the approaches the
    ///             rail's reach past the deck's end (APPROACH-RAIL under 10 m
    ///             with no junction or mouth to explain it); BLUNT-END: a
    ///             rail's or wall's face square to a car along the edge lines;
    ///   BODY      NO-SOFFIT (looking up from under the deck: nothing drawn),
    ///             NO-FASCIA (the deck's edge from the side), OPEN-ABUTMENT
    ///             (from under the deck toward its end: daylight under the
    ///             approach);
    ///   BELOW     every road passing under the run, 15 m either side: its own
    ///             surface (UNDER-HOLE / -STEP / -KINK, UNDER-BURIED: land over
    ///             its lanes), UNDER-WALL-IN-LANE (a pier, a cap, an abutment
    ///             on a lane line), and the MEASURED clearance, a ray up from
    ///             every lane every metre: LOW-CLEAR under 4.3 m (4.9 m over a
    ///             freeway's main line) - girders and pier caps included,
    ///             stacked levels included (a run under a run is a road below);
    ///   PIERS     every column the tiles stood (AuditView's records): in a
    ///             road's pavement below (PIER-PAVE), within 0.6 m of it
    ///             (PIER-SHY), in its sidewalk (PIER-WALK).
    /// And from the data alone, city-wide: every crossing's design clearance
    /// (the slab less the girders the bridges pass hangs under it).
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.RunBridges
    /// PSX_BRAUD_TAG names the report (city_bridges_audit_&lt;tag&gt;.txt + .tsv +
    /// _runs.tsv), PSX_BRAUD_MINUTES caps the mesh-level part (default 45; the
    /// runs are taken from Uptown outward), PSX_BRAUD_EDGES / PSX_BRAUD_NEAR
    /// ("x,z,r") restrict it.
    /// </summary>
    public static partial class CityAudit
    {
        sealed class BrRun
        {
            public CityMap.Edge e; public float s0, s1; public bool endA, endB;
            public CityMap.Edge pA, pB; public bool water; public int unders;
            public float uA, uB; public bool under; public BrRun over; public int finds;
        }
        struct BrPt { public CityMap.Edge e; public float s, u; public bool ok, fwd, elev; public Vector2 p, tan, right; public float extM, extP, y; }
        struct BrFind { public string kind; public float w; public int edge; public float s; public Vector2 at; public string note; }
        struct BrSurf { public bool road, other; public float y, topY; public string topName; }

        const float BrStep = 0.5f;
        static readonly RaycastHit[] brBuf = new RaycastHit[64];
        static readonly Dictionary<string, BrFind> brFinds = new Dictionary<string, BrFind>();
        static readonly Dictionary<long, (GameObject go, Mesh[] meshes)> brTiles = new Dictionary<long, (GameObject, Mesh[])>();
        static int brTilesBuilt; static double brBuildMs;
        static readonly List<BrPt> brPts = new List<BrPt>(1024);
        static float[] brY = new float[0]; static byte[] brSide = new byte[0];
        static readonly HashSet<int> brSegs = new HashSet<int>();
        static readonly HashSet<long> brUnderDone = new HashSet<long>();
        static readonly Dictionary<long, (Vector3 c, float r, float h)> brColumns = new Dictionary<long, (Vector3, float, float)>();
        static BrRun brCur;
        static readonly float[] BrEdgeLats = { -0.15f, 0.15f, 0.75f, 1.2f };

        static long BrKey(int tx, int tz) => ((long)tx << 24) | (uint)(tz & 0xFFFFFF);

        static void BrAdd(string kind, float w, CityMap.Edge e, float s, Vector2 at, string note)
        {
            string key = kind + "|" + e.index + "|" + Mathf.FloorToInt(s / 8f);
            if (brFinds.TryGetValue(key, out var f) && f.w >= w) return;
            if (!brFinds.ContainsKey(key) && brCur != null) brCur.finds++;
            brFinds[key] = new BrFind { kind = kind, w = w, edge = e.index, s = s, at = at, note = note };
        }

        static void BrDropTiles()
        {
            foreach (var kv in brTiles)
            {
                if (kv.Value.go != null) UnityEngine.Object.DestroyImmediate(kv.Value.go);
                if (kv.Value.meshes != null) foreach (var m in kv.Value.meshes) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            }
            brTiles.Clear();
        }

        static void BrEnsure(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> bld, Vector2 mn, Vector2 mx)
        {
            float T = CityMeshes.TileSize;
            int tx0 = Mathf.FloorToInt(mn.x / T), tx1 = Mathf.FloorToInt(mx.x / T), tz0 = Mathf.FloorToInt(mn.y / T), tz1 = Mathf.FloorToInt(mx.y / T);
            int need = 0;
            for (int tz = tz0; tz <= tz1; tz++) for (int tx = tx0; tx <= tx1; tx++) if (!brTiles.ContainsKey(BrKey(tx, tz))) need++;
            if (need == 0) return;
            if (brTiles.Count + need > 40) BrDropTiles();
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    long key = BrKey(tx, tz);
                    if (brTiles.ContainsKey(key)) continue;
                    var c = System.Diagnostics.Stopwatch.StartNew();
                    var tm = CityMeshes.Build(map, trims, bld, tx, tz);
                    brBuildMs += c.Elapsed.TotalMilliseconds; brTilesBuilt++;
                    GameObject go = null; Mesh[] ms = null;
                    if (tm != null)
                    {
                        foreach (var box in tm.solids)
                        {
                            if (!box.round || box.radius < 0.5f) continue;   // (a high mast's shaft is thinner)
                            var wc = tm.origin + box.center;
                            brColumns[((long)Mathf.RoundToInt(wc.x * 4f) << 32) ^ (uint)Mathf.RoundToInt(wc.z * 4f)] = (wc, box.radius, box.size.y);
                        }
                        go = new GameObject($"~br_{tx}_{tz}");
                        go.transform.position = tm.origin;
                        ms = CityWorld.Attach(go, tm, null);
                        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                            string nm = mf.gameObject.name;
                            if (nm != "Kerbs" && nm != "Banks" && nm != "Structure" && nm != "Water") continue;
                            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                        }
                    }
                    brTiles[key] = (go, ms);
                }
            Physics.SyncTransforms();
        }

        static float BrArcNear(CityMap.Edge e, Vector2 p, out float dist)
        {
            float best = float.MaxValue, at = 0f;
            for (int i = 0; i + 1 < e.pts.Length; i++)
            {
                Vector2 a = e.pts[i], d = e.pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float dd = Vector2.Distance(p, a + d * t);
                if (dd < best) { best = dd; at = e.s[i] + Mathf.Sqrt(L2) * t; }
            }
            dist = best;
            return at;
        }

        static bool BrPoint(BrRun r, float u, out BrPt pt)
        {
            pt = default; pt.u = u;
            var e = r.e; float s = u; bool fwd = true;
            if (u < 0f)
            {
                var o = r.pA; if (o == null) return false;
                float d = -u; if (d > o.length - 0.5f) return false;
                if (o.a == r.e.a) { s = d; fwd = false; } else { s = o.length - d; fwd = true; }
                e = o;
            }
            else if (u > e.length)
            {
                var o = r.pB; if (o == null) return false;
                float d = u - r.e.length; if (d > o.length - 0.5f) return false;
                if (o.a == r.e.b) { s = d; fwd = true; } else { s = o.length - d; fwd = false; }
                e = o;
            }
            pt.e = e; pt.s = s; pt.fwd = fwd; pt.ok = true;
            pt.p = e.PointAt(s);
            var t = e.TangentAt(s);
            LineModel.Extents(e, s, out float eM, out float eP);
            if (!fwd) { t = -t; float q = eM; eM = eP; eP = q; }
            pt.tan = t; pt.right = new Vector2(-t.y, t.x); pt.extM = eM; pt.extP = eP;
            pt.y = e.YAt(s); pt.elev = e.ElevatedAt(s);
            return true;
        }

        /// <summary>Line j of n lane lines across the station (j == n: the OSM line itself).</summary>
        static Vector2 BrLane(in BrPt pt, int j, int n)
        {
            if (j >= n) return pt.p;
            float W = pt.extM + pt.extP;
            return pt.p + pt.right * (-pt.extM + (j + 0.5f) * W / n);
        }

        static BrSurf BrDown(Vector2 p, float yRef, float above, float below)
        {
            var s = new BrSurf { y = float.NaN, topY = float.NegativeInfinity };
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, yRef + above, p.y), Vector3.down, brBuf, above + below, ~0, QueryTriggerInteraction.Ignore);
            float best = 1.2f;
            for (int i = 0; i < n; i++)
            {
                var c = brBuf[i].collider; if (IsLampPost(c) || c.name != "Roads") continue;
                float d = Mathf.Abs(brBuf[i].point.y - yRef);
                if (d < best) { best = d; s.y = brBuf[i].point.y; s.road = true; }
            }
            for (int i = 0; i < n; i++)
            {
                var c = brBuf[i].collider; if (IsLampPost(c)) continue;
                float hy = brBuf[i].point.y;
                if (s.road)
                {
                    if (hy > s.y + 0.05f && hy > s.topY && c.name != "Roads") { s.topY = hy; s.topName = c.name; }
                }
                else if (Mathf.Abs(hy - yRef) < 0.8f) { s.other = true; s.topName = c.name; }
            }
            return s;
        }

        static bool BrFace(Vector3 A, Vector3 B, out RaycastHit hit)
        {
            hit = default;
            var d = B - A; float L = d.magnitude;
            if (L < 0.2f) return false;
            d /= L;
            if (!RaycastPastLamps(A, d, out hit, L + 0.05f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return hit.normal.y <= 0.6f && Vector3.Dot(hit.normal, -d) >= 0.5f;
        }

        /// <summary>Is this point under a barrier's or a guardrail's top (inside its body)?</summary>
        static bool BrInsideBarrier(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 2.5f, Vector3.down, brBuf, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) { string nm = brBuf[i].collider.name; if (nm == "Barriers" || nm == "Guardrail") return true; }
            return false;
        }

        static string BrWho(CityMap.Edge e) => $"e{e.index} '{e.name}' cls {e.cls}{(e.link ? " link" : "")}{(e.oneway ? " one-way" : "")}";

        static void BrScan(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> bld, BrRun r)
        {
            brCur = r;
            bool under = r.under;
            brPts.Clear();
            for (float u = r.uA; u <= r.uB + 1e-3f; u += BrStep) { BrPoint(r, u, out var pt0); brPts.Add(pt0); }
            int K = brPts.Count;
            if (K < 6) return;
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            int kMid = -1;
            for (int k = 0; k < K; k++)
            {
                if (!brPts[k].ok) continue;
                mn = Vector2.Min(mn, brPts[k].p); mx = Vector2.Max(mx, brPts[k].p);
                if (brPts[k].e == r.e && (kMid < 0 || Mathf.Abs(brPts[k].u - 0.5f * (r.s0 + r.s1)) < Mathf.Abs(brPts[kMid].u - 0.5f * (r.s0 + r.s1)))) kMid = k;
            }
            if (kMid < 0) return;
            float pad = 18f + brPts[kMid].extM + brPts[kMid].extP;
            BrEnsure(map, trims, bld, mn - Vector2.one * pad, mx + Vector2.one * pad);
            int n = Mathf.Clamp(Mathf.RoundToInt((brPts[kMid].extM + brPts[kMid].extP) / 3.6f), 1, 8);
            int n1 = n + 1;
            if (brY.Length < K * n1) brY = new float[K * n1 + 1024];
            if (brSide.Length < K * 2) brSide = new byte[K * 2 + 1024];
            for (int i = 0; i < K * 2; i++) brSide[i] = 0;
            string pre = under ? "UNDER-" : "";
            bool Joint(float u) => !under && (Mathf.Abs(u - r.s0) < 6f || Mathf.Abs(u - r.s1) < 6f);

            // ---- SURFACE: the lane lines and the OSM line, every 0.5 m ----
            for (int j = 0; j < n1; j++)
            {
                int holeFrom = -1;
                for (int k = 0; k <= K; k++)
                {
                    bool hole = false;
                    if (k < K)
                    {
                        var pt = brPts[k];
                        brY[k * n1 + j] = float.NaN;
                        if (pt.ok)
                        {
                            var P = BrLane(pt, j, n);
                            var sf = BrDown(P, pt.y, 4.5f, 2.5f);
                            if (sf.road)
                            {
                                brY[k * n1 + j] = sf.y;
                                if (sf.topName != null && j < n)
                                {
                                    float h = sf.topY - sf.y;
                                    bool centre = !pt.e.oneway && (n % 2 == 1) && j == n / 2;
                                    string nm = sf.topName;
                                    string where = $"{h:0.00} m over the lane surface, lane line {j + 1}/{n}{(pt.elev ? ", on structure" : "")}";
                                    if (nm == "Ground") { if (!centre) BrAdd(under ? "UNDER-BURIED" : "LAND-OVER-LANE", h, pt.e, pt.s, P, "land " + where); }
                                    else if (nm == "Kerbs" || nm == "Banks" || nm == "Structure" || nm == "Water") { if (!centre) BrAdd(pre + "DRAWN-OVER-LANE", h, pt.e, pt.s, P, nm + " (drawn, not solid) " + where); }
                                    else if (nm == "Barriers" || nm == "Guardrail") { if (!centre) BrAdd(pre + "RAIL-ON-LANE", h, pt.e, pt.s, P, nm + " " + where); }
                                    else BrAdd(pre + "OBJECT-OVER-LANE", h, pt.e, pt.s, P, nm + " " + where);
                                }
                            }
                            else if (!sf.other && j < n) hole = true;
                        }
                    }
                    if (hole) { if (holeFrom < 0) holeFrom = k; }
                    else if (holeFrom >= 0)
                    {
                        var pt = brPts[holeFrom];
                        float len = (k - holeFrom) * BrStep;
                        BrAdd(under ? "UNDER-HOLE" : pt.elev ? "DECK-HOLE" : "ROAD-HOLE", len, pt.e, pt.s, BrLane(pt, j, n),
                              $"nothing to drive on for {len:0.0} m along lane line {j + 1}/{n}{(j == 0 || j == n - 1 ? " (an outer line)" : "")}");
                        holeFrom = -1;
                    }
                }
            }
            // ---- STEPS and KINKS along each line ----
            for (int j = 0; j < n1; j++)
            {
                bool Cont(int k)   // the line does not jump sideways between k and k+1
                {
                    var a = brPts[k]; var b = brPts[k + 1];
                    if (!a.ok || !b.ok) return false;
                    return Mathf.Abs(Vector2.Dot(BrLane(b, j, n) - BrLane(a, j, n), a.right)) < 0.12f;
                }
                for (int k = 1; k + 2 < K; k++)
                {
                    float a = brY[(k - 1) * n1 + j], b = brY[k * n1 + j], c = brY[(k + 1) * n1 + j], d = brY[(k + 2) * n1 + j];
                    if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c) || float.IsNaN(d)) continue;
                    if (!Cont(k - 1) || !Cont(k) || !Cont(k + 1)) continue;
                    float jump = (c - b) - 0.5f * ((b - a) + (d - c));
                    if (Mathf.Abs(jump) <= 0.02f) continue;
                    var pt = brPts[k];
                    BrAdd(pre + (Joint(pt.u) ? "STEP-JOINT" : "STEP"), Mathf.Abs(jump), pt.e, pt.s, BrLane(pt, j, n),
                          $"{jump * 100f:+0.0;-0.0} cm between two samples 0.5 m apart, {(j < n ? $"lane line {j + 1}/{n}" : "the OSM line")}{(pt.elev ? ", on structure" : "")}");
                }
                const int B4 = 4;
                for (int k = B4; k + B4 < K; k++)
                {
                    float a = brY[(k - B4) * n1 + j], b = brY[k * n1 + j], c = brY[(k + B4) * n1 + j];
                    if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c)) continue;
                    bool cont = true;
                    for (int q = k - B4; q < k + B4 && cont; q++) cont = Cont(q) && !float.IsNaN(brY[q * n1 + j]);
                    if (!cont) continue;
                    float gB = (b - a) / (B4 * BrStep), gA = (c - b) / (B4 * BrStep), dg = gA - gB;
                    if (Mathf.Abs(dg) <= 0.02f) continue;
                    var pt = brPts[k];
                    BrAdd(pre + (Joint(pt.u) ? "KINK-JOINT" : "KINK"), Mathf.Abs(dg) * 100f, pt.e, pt.s, BrLane(pt, j, n),
                          $"grade {gB * 100f:+0.0;-0.0} % -> {gA * 100f:+0.0;-0.0} % over 2 m either side, {(j < n ? $"lane line {j + 1}/{n}" : "the OSM line")}{(pt.elev ? ", on structure" : "")}");
                }
            }
            // ---- RAILS: out from the outer lanes, every metre ----
            float[] dropMax = { 0f, 0f };
            if (!under)
            {
                for (int k = 0; k < K; k += 2)
                {
                    var pt = brPts[k]; if (!pt.ok) continue;
                    float W = pt.extM + pt.extP, half = 0.5f * W / n;
                    for (int side = 0; side < 2; side++)
                    {
                        int j = side == 0 ? 0 : n - 1;
                        float yo = brY[k * n1 + j]; if (float.IsNaN(yo)) continue;
                        var P = BrLane(pt, j, n); var outv = side == 0 ? -pt.right : pt.right;
                        int nh = Physics.RaycastNonAlloc(new Vector3(P.x, yo + 0.45f, P.y), new Vector3(outv.x, 0f, outv.y), brBuf, half + 4.5f, ~0, QueryTriggerInteraction.Ignore);
                        bool rail = false;
                        for (int i = 0; i < nh; i++) { string nm = brBuf[i].collider.name; if (nm == "Barriers" || nm == "Guardrail") { rail = true; break; } }
                        if (rail) { brSide[k * 2 + side] = 1; continue; }
                        var E = P + outv * half;
                        bool pave = true, flush = true; float drop = 0f;
                        foreach (float off in new[] { 0.8f, 2.2f })
                        {
                            var q = E + outv * off;
                            int m = Physics.RaycastNonAlloc(new Vector3(q.x, yo + 1.5f, q.y), Vector3.down, brBuf, 41.5f, ~0, QueryTriggerInteraction.Ignore);
                            float top = float.NegativeInfinity; string tn = null;
                            for (int i = 0; i < m; i++) { if (IsLampPost(brBuf[i].collider)) continue; if (brBuf[i].point.y > top) { top = brBuf[i].point.y; tn = brBuf[i].collider.name; } }
                            pave &= tn == "Roads" && Mathf.Abs(top - yo) < 0.35f;
                            bool fl = top > yo - 0.6f;
                            flush &= fl;
                            if (!fl) drop = Mathf.Max(drop, float.IsNegativeInfinity(top) ? 40f : yo - top);
                        }
                        brSide[k * 2 + side] = (byte)(pave ? 2 : flush ? 3 : 4);
                        if (!pave && !flush) dropMax[side] = Mathf.Max(dropMax[side], drop);
                    }
                }
                // on structure: no rail beside a drop / beside flush land
                for (int side = 0; side < 2; side++)
                    foreach (byte what in new byte[] { 4, 3 })
                    {
                        int from = -1;
                        for (int k = 0; k <= K + 1; k += 2)
                        {
                            bool bad = k < K && brPts[k].ok && brPts[k].elev && brSide[k * 2 + side] == what;
                            if (bad) { if (from < 0) from = k; continue; }
                            if (from < 0) continue;
                            float len = (k - from) * BrStep;
                            var pt = brPts[from];
                            if (len >= 2f)
                                BrAdd(what == 4 ? "RAIL-DROP" : "RAIL-FLUSH", len, pt.e, pt.s, BrLane(pt, side == 0 ? 0 : n - 1, n),
                                      $"no rail for {len:0} m on structure, {(side == 0 ? "right" : "left")} side of the walk{(what == 4 ? $", a drop of up to {dropMax[side]:0.0} m beside" : ", land flush beside")}");
                            from = -1;
                        }
                    }
                // the approaches: how far the rail is carried past a real structure end
                for (int end = 0; end < 2; end++)
                {
                    if (end == 0 ? !r.endA : !r.endB) continue;
                    float uEnd = end == 0 ? r.s0 : r.s1; int dir = end == 0 ? -1 : 1;
                    int kEnd = 2 * Mathf.RoundToInt((uEnd - r.uA) / (2f * BrStep));
                    for (int side = 0; side < 2; side++)
                    {
                        float reach = 0f; int why = 0;
                        for (int m = 1; m <= 20; m++)
                        {
                            int k = kEnd + dir * m * 2;
                            if (k < 0 || k >= K || !brPts[k].ok) { why = 9; break; }
                            int st = brSide[k * 2 + side];
                            if (st == 1) { reach = m; continue; }
                            why = st; break;
                        }
                        if (reach >= 10f || why == 9 || why == 2 || why == 0) continue;
                        if (end == 0 && r.s0 - reach - 8f <= 0f && map.nodeEdges[r.e.a].Count >= 3) continue;
                        if (end == 1 && r.e.length - r.s1 - reach - 8f <= 0f && map.nodeEdges[r.e.b].Count >= 3) continue;
                        int kk = Mathf.Clamp(kEnd, 0, K - 1);
                        BrAdd("APPROACH-RAIL", 20f - reach, r.e, uEnd, brPts[kk].ok ? brPts[kk].p : r.e.PointAt(Mathf.Clamp(uEnd, 0f, r.e.length)),
                              $"the rail is carried {reach:0} m past the deck's {(end == 0 ? "start" : "end")} on the {(side == 0 ? "right" : "left")} side, then {(why == 3 ? "flush land" : "a drop")} with no rail");
                    }
                }
            }
            // ---- FACES SQUARE TO THE TRAVEL: on the lane lines, along the edge lines ----
            for (int k = 0; k + 2 < K; k += 2)
            {
                var A = brPts[k]; var B = brPts[k + 2];
                if (!A.ok || !B.ok) continue;
                bool both = !A.e.oneway;
                for (int j = 0; j < n; j++)
                {
                    float ya = brY[k * n1 + j], yb = brY[(k + 2) * n1 + j];
                    if (float.IsNaN(ya) || float.IsNaN(yb)) continue;
                    var pa = BrLane(A, j, n); var pb = BrLane(B, j, n);
                    var a3 = new Vector3(pa.x, ya + 0.45f, pa.y); var b3 = new Vector3(pb.x, yb + 0.45f, pb.y);
                    RaycastHit hit;
                    if (BrFace(a3, b3, out hit) || (both && BrFace(b3, a3, out hit)))
                        BrAdd(pre + "WALL-IN-LANE", 1f + (A.elev ? 1f : 0f), A.e, A.s, pa, $"a face of {hit.collider.name} square to the travel at bumper height on lane line {j + 1}/{n}{(A.elev ? ", on structure" : "")}");
                }
                for (int side = 0; side < 2; side++)
                {
                    int j = side == 0 ? 0 : n - 1;
                    float ya = brY[k * n1 + j], yb = brY[(k + 2) * n1 + j];
                    if (float.IsNaN(ya) || float.IsNaN(yb)) continue;
                    foreach (float off in BrEdgeLats)
                    {
                        var pa = A.p + A.right * (side == 0 ? -(A.extM + off) : A.extP + off);
                        var pb = B.p + B.right * (side == 0 ? -(B.extM + off) : B.extP + off);
                        var a3 = new Vector3(pa.x, ya + 0.45f, pa.y); var b3 = new Vector3(pb.x, yb + 0.45f, pb.y);
                        // a line inside a rail's own body sees the caps two pieces share inside it: not ends
                        if (BrInsideBarrier(a3) || BrInsideBarrier(b3)) continue;
                        RaycastHit hit;
                        if (!(BrFace(a3, b3, out hit) || (both && BrFace(b3, a3, out hit)))) continue;
                        string nm = hit.collider.name;
                        if (nm != "Barriers" && nm != "Guardrail") continue;   // (a Structure face here is a W-beam post or an object marker: drawn on a solid beam)
                        BrAdd(pre + "BLUNT-END", 1f + (off < 0.2f ? 1f : 0f), A.e, A.s, pa, $"a face of {nm} square to a car along the {(side == 0 ? "right" : "left")} edge line ({off:+0.00;-0.00} m past the pavement edge){(A.elev ? ", on structure" : "")}");
                        break;
                    }
                }
            }
            // ---- THE DECK'S BODY: soffit, fascia, abutments ----
            if (!under)
            {
                for (int k = 0; k < K; k += 8)
                {
                    var pt = brPts[k]; if (!pt.ok || !pt.elev) continue;
                    float W = pt.extM + pt.extP;
                    int open = 0, seen = 0;
                    foreach (float f in new[] { 0.15f, 0.5f, 0.85f })
                    {
                        var P = pt.p + pt.right * (-pt.extM + f * W);
                        var sf = BrDown(P, pt.y, 2.5f, 2.5f);
                        if (!sf.road) continue;
                        float air = 60f;
                        if (RaycastPastLamps(new Vector3(P.x, sf.y - 0.6f, P.y), Vector3.down, out var g, 60f, ~0, QueryTriggerInteraction.Ignore)) air = g.distance;
                        if (air < 1.5f) continue;
                        seen++;
                        float y0 = sf.y - Mathf.Min(3.0f, 0.6f + air - 0.1f);
                        if (!RaycastPastLamps(new Vector3(P.x, y0, P.y), Vector3.up, out _, sf.y - 0.02f - y0, ~0, QueryTriggerInteraction.Ignore)) open++;
                    }
                    if (open > 0) BrAdd("NO-SOFFIT", open, pt.e, pt.s, pt.p, $"looking up from under the deck nothing is drawn at {open} of {seen} points across it");
                    for (int side = 0; side < 2; side++)
                    {
                        int k2 = k - (k % 2);
                        if (brSide[k2 * 2 + side] == 2 || brSide[k2 * 2 + side] == 3) continue;
                        int j = side == 0 ? 0 : n - 1;
                        float yo = brY[k * n1 + j]; if (float.IsNaN(yo)) continue;
                        var outv = side == 0 ? -pt.right : pt.right;
                        var E = pt.p + outv * (side == 0 ? pt.extM : pt.extP);
                        var from = E + outv * 1.5f;
                        // an edge with a surface carrying on beside it at its own level is not an open edge
                        var nb = E + outv * 0.6f;
                        if (RaycastPastLamps(new Vector3(nb.x, yo + 1.5f, nb.y), Vector3.down, out var nbh, 2.1f, ~0, QueryTriggerInteraction.Ignore)
                            && nbh.collider.name != "Barriers" && nbh.collider.name != "Guardrail") continue;
                        if (!RaycastPastLamps(new Vector3(from.x, yo - 0.28f, from.y), new Vector3(-outv.x, 0f, -outv.y), out _, 2.3f, ~0, QueryTriggerInteraction.Ignore))
                            BrAdd("NO-FASCIA", 1f, pt.e, pt.s, E, $"the deck's {(side == 0 ? "right" : "left")} edge shows no face from the side (0.28 m under the surface)");
                    }
                }
                for (int end = 0; end < 2; end++)
                {
                    if (end == 0 ? !r.endA : !r.endB) continue;
                    float uEnd = end == 0 ? r.s0 : r.s1; int dirIn = end == 0 ? 1 : -1;
                    float gd = CityMeshes.GirderDepthOf(map, r.e);
                    foreach (float inset in new[] { 3f, 6f, 10f })
                    {
                        float u = uEnd + dirIn * inset;
                        if (u < r.s0 - 0.01f || u > r.s1 + 0.01f) break;
                        if (!BrPoint(r, u, out var pt)) break;
                        float W = pt.extM + pt.extP;
                        int open = 0, seen = 0; string through = "";
                        foreach (float f in new[] { 0.2f, 0.5f, 0.8f })
                        {
                            var P = pt.p + pt.right * (-pt.extM + f * W);
                            var sf = BrDown(P, pt.y, 2.5f, 2.5f);
                            if (!sf.road) continue;
                            float yRay = sf.y - CityElevation.DeckThick - gd - 0.35f;
                            if (RaycastPastLamps(new Vector3(P.x, sf.y - 0.6f, P.y), Vector3.down, out var g, 80f, ~0, QueryTriggerInteraction.Ignore) && yRay - g.point.y < 0.3f) continue;
                            seen++;
                            var d3 = new Vector3(pt.tan.x, 0f, pt.tan.y) * -dirIn;
                            if (!RaycastPastLamps(new Vector3(P.x, yRay, P.y), d3, out _, inset + 12f, ~0, QueryTriggerInteraction.Ignore)) { open++; through += $" {f:0.0}"; }
                        }
                        if (seen == 0) continue;
                        if (open > 0) BrAdd("OPEN-ABUTMENT", open, r.e, uEnd, r.e.PointAt(Mathf.Clamp(uEnd, 0f, r.e.length)),
                                            $"from under the deck {inset:0} m in, looking at its {(end == 0 ? "start" : "end")}: nothing closes the view for 12 m past it at {open} of {seen} lines across (fractions{through})");
                        break;
                    }
                }
            }
            // ---- MEASURED CLEARANCE over a road below ----
            if (under)
            {
                var ue = r.e;
                float thr = ue.cls >= 5 && !ue.link ? 4.9f : 4.3f;
                for (int k = 0; k < K; k += 2)
                {
                    var pt = brPts[k]; if (!pt.ok) continue;
                    for (int j = 0; j < n; j++)
                    {
                        float y = brY[k * n1 + j]; if (float.IsNaN(y)) continue;
                        var P = BrLane(pt, j, n);
                        if (!RaycastPastLamps(new Vector3(P.x, y + 0.1f, P.y), Vector3.up, out var h, 7f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        float clr = h.distance + 0.1f;
                        if (clr >= thr) continue;
                        BrAdd("LOW-CLEAR", thr - clr, ue, pt.s, P, $"{clr:0.00} m clear (wanted {thr:0.0}) under {h.collider.name} of {(r.over != null ? BrWho(r.over.e) : "?")}, lane line {j + 1}/{n}");
                    }
                }
                for (float s = Mathf.Floor(r.uA); s <= r.uB; s += 1f) brUnderDone.Add((long)ue.index * 100000L + (long)s);
                return;
            }
            // ---- THE ROADS BELOW ----
            var unders = new Dictionary<int, Vector2>();
            for (int k = 0; k < K; k += 8)
            {
                var pt = brPts[k]; if (!pt.ok || !pt.elev) continue;
                float reach = Mathf.Max(pt.extM, pt.extP) + 30f;
                brSegs.Clear();
                map.EdgeSegsInRect(pt.p - Vector2.one * reach, pt.p + Vector2.one * reach, brSegs);
                foreach (int packed in brSegs)
                {
                    int oi = packed >> 12, si = packed & 0xFFF;
                    var o = map.edges[oi];
                    if (o == pt.e || o == r.e || si + 1 >= o.pts.Length || o.tunnel) continue;
                    Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                    float L2 = d.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(pt.p - a, d) / L2) : 0f;
                    if (Vector2.Distance(pt.p, a + d * t) > Mathf.Max(pt.extM, pt.extP) + o.HalfMax + 1f) continue;
                    float at = o.s[si] + Mathf.Sqrt(L2) * t;
                    if (o.YAt(at) > pt.y - 2.0f) continue;
                    if (unders.TryGetValue(oi, out var rg)) unders[oi] = new Vector2(Mathf.Min(rg.x, at), Mathf.Max(rg.y, at));
                    else unders[oi] = new Vector2(at, at);
                }
            }
            r.unders = unders.Count;
            foreach (var kv in unders)
            {
                var o = map.edges[kv.Key];
                float mid = 0.5f * (kv.Value.x + kv.Value.y);
                if (brUnderDone.Contains((long)o.index * 100000L + (long)Mathf.Floor(mid))) continue;
                var ur = new BrRun { e = o, s0 = kv.Value.x, s1 = kv.Value.y, under = true, over = r };
                ur.uA = Mathf.Max(0.5f, kv.Value.x - 15f); ur.uB = Mathf.Min(o.length - 0.5f, kv.Value.y + 15f);
                if (ur.uB - ur.uA < 3f) continue;
                try { BrScan(map, trims, bld, ur); } catch (Exception ex) { Debug.LogWarning($"[BridgesAudit] under-run e{o.index} threw {ex.Message}"); }
                r.finds += ur.finds;
                brCur = r;
            }
        }

        static readonly (string kind, string what)[] BrKinds =
        {
            ("DECK-HOLE", "nothing to drive on, on structure (a lane line with no surface and nothing within 0.8 m of its level)"),
            ("ROAD-HOLE", "the same on an approach"),
            ("STEP-JOINT", "a step over 2 cm in the driven surface within 6 m of a structure end"),
            ("STEP", "a step over 2 cm elsewhere on the run or its approaches"),
            ("KINK-JOINT", "a grade break over 2 % (2 m bases) within 6 m of a structure end"),
            ("KINK", "a grade break over 2 % elsewhere"),
            ("WALL-IN-LANE", "a face square to the travel on a lane line at bumper height"),
            ("RAIL-ON-LANE", "a barrier or guardrail standing on a lane line"),
            ("LAND-OVER-LANE", "land standing over a lane's surface (5 cm or more)"),
            ("DRAWN-OVER-LANE", "a render-only face (structure, kerb, bank, water) over a lane's surface"),
            ("OBJECT-OVER-LANE", "a solid (pole, sign, building) in the 4.5 m over a lane"),
            ("RAIL-DROP", "no rail on structure beside a drop (2 m or longer)"),
            ("RAIL-FLUSH", "no rail on structure, land flush beside (2 m or longer)"),
            ("APPROACH-RAIL", "the rail is carried under 10 m past a deck end with no mouth or junction there"),
            ("BLUNT-END", "a rail's or wall's face square to a car along an edge line"),
            ("NO-SOFFIT", "the deck shows nothing from below"),
            ("NO-FASCIA", "the deck's edge shows no face from the side"),
            ("OPEN-ABUTMENT", "daylight under the approach, seen from under the deck"),
            ("LOW-CLEAR", "measured clearance over a road below under 4.3 m (4.9 m over a freeway main line)"),
            ("UNDER-HOLE", "road below: nothing to drive on"),
            ("UNDER-STEP", "road below: a step over 2 cm"),
            ("UNDER-KINK", "road below: a grade break over 2 %"),
            ("UNDER-BURIED", "road below: land over its lanes"),
            ("UNDER-WALL-IN-LANE", "road below: a face (pier, cap, abutment, barrier) square to the travel on a lane line"),
            ("UNDER-RAIL-ON-LANE", "road below: a barrier on a lane line"),
            ("UNDER-DRAWN-OVER-LANE", "road below: a render-only face over a lane"),
            ("UNDER-OBJECT-OVER-LANE", "road below: a solid in the 4.5 m over a lane"),
            ("UNDER-BLUNT-END", "road below: a rail's or wall's face square to a car along an edge line"),
            ("PIER-PAVE", "a pier column (its solid) inside the pavement of a road passing it below its top"),
            ("PIER-SHY", "a pier column within 0.6 m of the pavement of a road below"),
            ("PIER-WALK", "a pier column in the sidewalk band (2.7 m) of a road below that has walks"),
            ("DESIGN-CLEAR", "data: slab less girders leaves under 4.3 m (4.9 m over a freeway main line) at a crossing"),
        };

        /// <summary>Headless: -executeMethod PSXRacing.EditorTools.CityAudit.RunBridges</summary>
        public static void RunBridges()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string tag = Environment.GetEnvironmentVariable("PSX_BRAUD_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "run";
            string file = Path.Combine(root, $"city_bridges_audit_{tag}.txt");
            foreach (var f in new[] { file, Path.ChangeExtension(file, ".tsv"), Path.Combine(root, $"city_bridges_audit_{tag}_runs.tsv") }) if (File.Exists(f)) File.Delete(f);
            var sb = new StringBuilder();
            int code = 0;
            bool backWas = Physics.queriesHitBackfaces;
            var runs = new List<BrRun>();
            try
            {
                Physics.queriesHitBackfaces = false;
                brFinds.Clear(); brUnderDone.Clear(); brColumns.Clear(); brTilesBuilt = 0; brBuildMs = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var map = CityMap.Get();
                if (map == null) throw new Exception("no city data");
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                float capMin = 45f;
                if (float.TryParse(Environment.GetEnvironmentVariable("PSX_BRAUD_MINUTES"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cm)) capMin = cm;
                sb.AppendLine($"CITY BRIDGES AUDIT ({tag}) {DateTime.Now:s}; graph hash {map.graphHash:x8} (GHSH); BRIDGE2 {(CityMeshes.Bridge2On ? "ON" : "OFF")}; solved in {clock.Elapsed.TotalSeconds:0} s");

                // ---- the runs ----
                var waterEdges = new HashSet<int>();
                if (map.wspans != null) foreach (var ws in map.wspans) waterEdges.Add(ws.edge);
                foreach (var e in map.edges)
                {
                    if (e.stS == null || e.stS.Length < 2 || e.stElev == null || e.tunnel) continue;
                    bool any = false; foreach (bool b in e.stElev) if (b) { any = true; break; }
                    if (!any) continue;
                    float from = -1f, last = 0f;
                    for (float s = 0f; ; s += 0.5f)
                    {
                        float ss = Mathf.Min(s, e.length);
                        bool el = e.ElevatedAt(ss);
                        if (el) { if (from < 0f) from = ss; last = ss; }
                        if ((!el || ss >= e.length) && from >= 0f)
                        {
                            if (last - from >= 1.5f)
                            {
                                var r = new BrRun { e = e, s0 = from, s1 = last, water = waterEdges.Contains(e.index) };
                                r.endA = true; r.endB = true;
                                for (int end = 0; end < 2; end++)
                                {
                                    bool atNode = end == 0 ? from <= 0.6f : last >= e.length - 0.6f;
                                    if (!atNode) continue;
                                    int node = end == 0 ? e.a : e.b;
                                    Vector2 dirE = end == 0 ? e.TangentAt(0f) : -e.TangentAt(e.length);
                                    CityMap.Edge best = null; float bestDot = -0.5f; bool cont = false;
                                    foreach (int oi in map.nodeEdges[node])
                                    {
                                        if (oi == e.index) continue;
                                        var o = map.edges[oi];
                                        if (o.stS != null && o.stS.Length >= 2 && o.ElevatedAt(o.a == node ? 0f : o.length)) cont = true;
                                        Vector2 dirO = o.a == node ? o.TangentAt(0f) : -o.TangentAt(o.length);
                                        float dot = Vector2.Dot(dirE, dirO);
                                        if (dot < bestDot) { bestDot = dot; best = o; }
                                    }
                                    if (end == 0) { r.pA = best; r.endA = !cont; } else { r.pB = best; r.endB = !cont; }
                                }
                                r.uA = r.s0 - 30f; r.uB = r.s1 + 30f;
                                if (r.pA == null) r.uA = Mathf.Max(r.uA, 0.25f);
                                if (r.pB == null) r.uB = Mathf.Min(r.uB, e.length - 0.25f);
                                runs.Add(r);
                            }
                            from = -1f;
                        }
                        if (ss >= e.length) break;
                    }
                }
                string only = Environment.GetEnvironmentVariable("PSX_BRAUD_EDGES");
                if (!string.IsNullOrWhiteSpace(only))
                {
                    var keep = new HashSet<int>();
                    foreach (var t in only.Split(',')) if (int.TryParse(t.Trim(), out int v)) keep.Add(v);
                    runs.RemoveAll(r => !keep.Contains(r.e.index));
                }
                string near = Environment.GetEnvironmentVariable("PSX_BRAUD_NEAR");
                if (!string.IsNullOrWhiteSpace(near))
                {
                    var t = near.Split(',');
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var c = new Vector2(float.Parse(t[0], inv), float.Parse(t[1], inv)); float rad = float.Parse(t[2], inv);
                    runs.RemoveAll(r => Vector2.Distance(r.e.PointAt(0.5f * (r.s0 + r.s1)), c) > rad);
                }
                float T = CityMeshes.TileSize;
                long OrderKey(BrRun r)
                {
                    var p = r.e.PointAt(0.5f * (r.s0 + r.s1));
                    int tx = Mathf.FloorToInt(p.x / T), tz = Mathf.FloorToInt(p.y / T);
                    int ring = Mathf.FloorToInt(Vector2.Distance(new Vector2((tx + 0.5f) * T, (tz + 0.5f) * T), map.uptown) / 1024f);
                    return ((long)ring << 40) | ((long)(tx + 4096) << 20) | (long)(tz + 4096);
                }
                runs.Sort((a, b) => OrderKey(a).CompareTo(OrderKey(b)));
                float totalM = 0f; int nWater = 0, nLink = 0, nFwy = 0, realEnds = 0;
                var runTiles = new HashSet<long>();
                foreach (var r in runs)
                {
                    totalM += r.s1 - r.s0; if (r.water) nWater++; if (r.e.link) nLink++; if (r.e.cls >= 5 && !r.e.link) nFwy++;
                    if (r.endA) realEnds++; if (r.endB) realEnds++;
                    var p = r.e.PointAt(0.5f * (r.s0 + r.s1));
                    runTiles.Add(BrKey(Mathf.FloorToInt(p.x / T), Mathf.FloorToInt(p.y / T)));
                }
                sb.AppendLine($"RUNS ON STRUCTURE: {runs.Count} on {runTiles.Count} tiles, {totalM / 1000f:0.0} km; over water {nWater}, freeway main line {nFwy}, ramps/links {nLink}; structure ends at fill (abutments) {realEnds}");

                // ---- the data-level design clearance, every crossing ----
                int nCross = 0, nLowDesign = 0;
                if (map.crossings != null)
                    foreach (var c in map.crossings)
                    {
                        if (c.over < 0 || c.under < 0 || c.over >= map.edges.Length || c.under >= map.edges.Length) continue;
                        var over = map.edges[c.over]; var und = map.edges[c.under];
                        if (und.tunnel) continue;
                        float sO = BrArcNear(over, c.at, out _), sU = BrArcNear(und, c.at, out _);
                        if (!over.ElevatedAt(sO)) continue;
                        nCross++;
                        float clr = over.YAt(sO) - CityElevation.DeckThick - CityMeshes.GirderDepthOf(map, over) - und.YAt(sU);
                        float thr = und.cls >= 5 && !und.link ? 4.9f : 4.3f;
                        if (clr >= thr) continue;
                        nLowDesign++;
                        brCur = null;
                        BrAdd("DESIGN-CLEAR", thr - clr, und, sU, c.at, $"{clr:0.00} m (wanted {thr:0.0}) under {BrWho(over)}: slab {over.YAt(sO) - CityElevation.DeckThick - und.YAt(sU):0.00} m less girders {CityMeshes.GirderDepthOf(map, over):0.00} m");
                    }
                sb.AppendLine($"CROSSINGS (data): {nCross} decks over a road; design clearance under the wanted figure at {nLowDesign}");

                // ---- the mesh-level scan ----
                CityMeshes.AuditView.BeginRecord();
                int done = 0, threw = 0;
                foreach (var r in runs)
                {
                    if (clock.Elapsed.TotalMinutes > capMin) break;
                    try { BrScan(map, trims, buildings, r); }
                    catch (Exception ex) { threw++; Debug.LogWarning($"[BridgesAudit] run e{r.e.index} threw {ex}"); }
                    done++;
                    if (done % 100 == 0) Debug.Log($"[BridgesAudit] {done}/{runs.Count} runs, {brTilesBuilt} tiles built, {brFinds.Count} findings, {clock.Elapsed.TotalMinutes:0.0} min");
                }
                brCur = null;
                // ---- the piers: every round column the tiles stood (their solids) ----
                int nPiers = 0;
                foreach (var col in brColumns.Values)
                {
                    nPiers++;
                    var c = new Vector2(col.c.x, col.c.z);
                    float footY = col.c.y - 0.5f * col.h, topY = col.c.y + 0.5f * col.h;
                    brSegs.Clear();
                    map.EdgeSegsInRect(c - Vector2.one * 40f, c + Vector2.one * 40f, brSegs);
                    var seenEdge = new HashSet<int>();
                    foreach (int packed in brSegs)
                    {
                        int oi = packed >> 12, si = packed & 0xFFF;
                        var o = map.edges[oi];
                        if (si + 1 >= o.pts.Length || o.tunnel) continue;
                        Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(c - a, d) / L2) : 0f;
                        var q = a + d * t;
                        float at = o.s[si] + Mathf.Sqrt(L2) * t;
                        float oy = o.YAt(at);
                        // a road passing the column between its foot and its top: under the deck it carries
                        if (oy > topY - 1.5f || oy < footY - 1.0f) continue;
                        LineModel.Extents(o, at, out float eM, out float eP);
                        var tn = L2 > 1e-8f ? d / Mathf.Sqrt(L2) : Vector2.up;
                        var right = new Vector2(-tn.y, tn.x);
                        float ext = Vector2.Dot(c - q, right) >= 0f ? eP : eM;
                        float gap = Vector2.Distance(c, q) - ext - col.r;
                        if (!seenEdge.Add(oi * 4 + (gap < 0f ? 0 : gap < 0.6f ? 1 : 2))) continue;
                        string who = $"a pier column (radius {col.r:0.00} m, {col.h:0.0} m tall) {(gap < 0f ? $"{-gap:0.00} m inside" : $"{gap:0.00} m from")} the pavement{(o.ElevatedAt(at) ? " (that road on structure here)" : "")}";
                        if (gap < 0f) BrAdd("PIER-PAVE", -gap, o, at, c, who);
                        else if (gap < 0.6f) BrAdd("PIER-SHY", 0.6f - gap, o, at, c, who);
                        else if (gap < 2.7f && CityMeshes.WalkEdge(map, o)) BrAdd("PIER-WALK", 2.7f - gap, o, at, c, who + " of a road with walks");
                    }
                }
                CityMeshes.AuditView.EndRecord();
                sb.AppendLine($"SCANNED: {done} of {runs.Count} runs{(done < runs.Count ? " (TIME CAP: the rest, furthest from Uptown, NOT scanned)" : "")}, {threw} threw; {brTilesBuilt} tile builds, mean {(brTilesBuilt > 0 ? brBuildMs / brTilesBuilt : 0):0} ms; {nPiers} piers; {clock.Elapsed.TotalMinutes:0.0} min");
                sb.AppendLine();

                // ---- the count table ----
                var byKind = new Dictionary<string, List<BrFind>>();
                foreach (var f in brFinds.Values) { if (!byKind.TryGetValue(f.kind, out var l)) byKind[f.kind] = l = new List<BrFind>(); l.Add(f); }
                sb.AppendLine("COUNTS (places = distinct 8 m stretches of one edge; edges = roads touched)");
                sb.AppendLine("kind                     places  edges  what");
                var known = new HashSet<string>();
                void Row(string kind, string what)
                {
                    known.Add(kind);
                    int places = 0, edges = 0;
                    if (byKind.TryGetValue(kind, out var l)) { places = l.Count; var es = new HashSet<int>(); foreach (var f in l) es.Add(f.edge); edges = es.Count; }
                    sb.AppendLine($"{kind,-24} {places,6} {edges,6}  {what}");
                }
                foreach (var (kind, what) in BrKinds) Row(kind, what);
                foreach (var k in byKind.Keys) if (!known.Contains(k)) Row(k, "");
                sb.AppendLine();
                foreach (var kv in byKind)
                {
                    kv.Value.Sort((a, b) => b.w.CompareTo(a.w));
                    sb.AppendLine($"== {kv.Key}: the {Mathf.Min(20, kv.Value.Count)} worst of {kv.Value.Count}");
                    for (int i = 0; i < kv.Value.Count && i < 20; i++)
                    {
                        var f = kv.Value[i]; var e = map.edges[f.edge];
                        sb.AppendLine($"  {f.w,6:0.00}  {BrWho(e)} s={f.s:0}/{e.length:0} ({f.at.x:0},{f.at.y:0}) {LatLon(f.at.x, f.at.y)}  {f.note}");
                    }
                }
                var tsv = new StringBuilder("kind\tweight\tedge\tname\tcls\tlink\ts\tx\tz\tlatlon\tnote\n");
                foreach (var f in brFinds.Values)
                {
                    var e = map.edges[f.edge];
                    tsv.Append($"{f.kind}\t{f.w:0.000}\t{f.edge}\t{e.name}\t{e.cls}\t{(e.link ? 1 : 0)}\t{f.s:0.0}\t{f.at.x:0.0}\t{f.at.y:0.0}\t{LatLon(f.at.x, f.at.y)}\t{f.note}\n");
                }
                File.WriteAllText(Path.ChangeExtension(file, ".tsv"), tsv.ToString());
                var rt = new StringBuilder("edge\tname\tcls\tlink\toneway\tlanes\twidth\ts0\ts1\tlen\tendA\tendB\twater\tunders\tx\tz\ty\tlatlon\tfinds\n");
                foreach (var r in runs)
                {
                    var p = r.e.PointAt(0.5f * (r.s0 + r.s1));
                    rt.Append($"{r.e.index}\t{r.e.name}\t{r.e.cls}\t{(r.e.link ? 1 : 0)}\t{(r.e.oneway ? 1 : 0)}\t{r.e.lanes}\t{r.e.width:0.0}\t{r.s0:0.0}\t{r.s1:0.0}\t{r.s1 - r.s0:0.0}\t{(r.endA ? 1 : 0)}\t{(r.endB ? 1 : 0)}\t{(r.water ? 1 : 0)}\t{r.unders}\t{p.x:0.0}\t{p.y:0.0}\t{r.e.YAt(0.5f * (r.s0 + r.s1)):0.0}\t{LatLon(p.x, p.y)}\t{r.finds}\n");
                }
                File.WriteAllText(Path.Combine(root, $"city_bridges_audit_{tag}_runs.tsv"), rt.ToString());
                sb.AppendLine("CITY BRIDGES AUDIT DONE");
            }
            catch (Exception ex) { sb.AppendLine("CITY BRIDGES AUDIT FAILED: " + ex); Debug.LogException(ex); code = 1; }
            finally
            {
                Physics.queriesHitBackfaces = backWas;
                BrDropTiles();
                File.WriteAllText(file, sb.ToString());
                Debug.Log("[BridgesAudit] report " + file);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
