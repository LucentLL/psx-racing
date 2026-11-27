using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Road-surface stage 1 (Docs/ROAD_RULES.md section 2, "one owner per surface"): a junction the measures source
    /// names is drawn as ONE PAVED SURFACE - JunctionSurfaces solves its outline once per load; here its end rows become
    /// the arms' ribbon trims, the old fan and what it lays down are skipped for its nodes, and its own pieces are drawn:
    /// the fill at true height (no lift), a kerb with real thickness along every free edge of the outline (on the fill's
    /// own boundary vertices), each island a closed solid on its hole's own vertices, the land behind a median end
    /// closed against its kerb, a verge from the outer kerbs to the ground.
    /// PSX_CITY_ROADSURFACE=1 (LAB row "JUNCTIONS AS ONE SURFACE", default OLD). Needs PSX_CITY_MEASURES (HAND) today.
    /// NOT DONE in stage 1 (HANDOFF.md): the lane paint and the traffic lines inside the outline still follow the map's
    /// lines, not the measured pavement; the control trim (stop lines, signals, traffic) moves with the ribbon trim.
    /// </summary>
    public static partial class CityMeshes
    {
        public static bool RoadSurfaceOn = System.Environment.GetEnvironmentVariable("PSX_CITY_ROADSURFACE") == "1";
        /// <summary>The kerb: a 6 in (0.15 m) face and a 6 in top (NCDOT standard 2 ft 6 in curb and gutter, std. dwg
        /// 846.01; the gutter pan is pavement here).</summary>
        public const float RsKerbH = 0.15f, RsKerbW = 0.15f;
        public static int RsJunctions, RsTris, RsKerbTris, RsLandTris, RsMaskedTris; public static double RsBuildMs; public static float RsKerbM;

        // ONE OWNER PER SURFACE (Docs/ROAD_RULES.md section 2): while the roads' own emitters run (gores and branches,
        // ribbons and their roadside, junction pieces, centre medians, corner pans, island solids), a quad or triangle
        // whose centre lies inside a solved junction's outline is not laid - the junction's own pieces are (rsOwn).
        // The lattice, the lots, the buildings and the closing pass are not masked.
        static bool RsMaskOn, rsOwn;
        static readonly List<JunctionSurfaces.Built> rsHere = new List<JunctionSurfaces.Built>(4);
        static Vector3 rsOrigin;
        static void RsTileBegin(TileMeshes tm, Vector2 min, Vector2 max)
        {
            rsHere.Clear(); RsMaskOn = false; rsOwn = false; rsOrigin = tm.origin;
            if (!RoadSurfaceOn) return;
            foreach (var b in JunctionSurfaces.All)
            {
                if (!b.Solved) continue;
                var lo = b.min + b.origin; var hi = b.max + b.origin;
                if (hi.x < min.x - 20f || lo.x > max.x + 20f || hi.y < min.y - 20f || lo.y > max.y + 20f) continue;
                rsHere.Add(b);
            }
        }
        static bool RsMasked(Vector3 localCentre)
        {
            if (rsOwn) return false;
            var w = new Vector2(localCentre.x + rsOrigin.x, localCentre.z + rsOrigin.z);
            for (int i = 0; i < rsHere.Count; i++)
            {
                var b = rsHere[i]; var p = w - b.origin;
                if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y) continue;
                if (JunctionOutline.InPoly(b.res.outline, p)) return true;
            }
            return false;
        }

        static Trims surfFrom, surfTrims;
        /// <summary>The tile build's trims: the rules' (control) trims with the rows of every solved junction as its
        /// arms' ribbon trims. The rules' trims themselves when the switch is off or no junction is solved. Cached per
        /// control trims.</summary>
        public static Trims SurfaceTrims(CityMap map, Trims rule)
        {
            if (!RoadSurfaceOn || rule == null) return rule;
            if (rule.control != null) return rule;   // surface trims already
            if (ReferenceEquals(surfFrom, rule) && surfTrims != null) return surfTrims;
            var built = JunctionSurfaces.Ensure(map, (e, node) => e.a == node ? rule.atA[e.index] : rule.atB[e.index]);
            var t = rule;
            bool any = false; foreach (var b in built) any |= b.Solved;
            if (any) t = rule.CloneForSurface();
            foreach (var b in built)
            {
                if (!b.Solved) continue;
                foreach (var kv in b.rowOf)
                {
                    var e = map.edges[kv.Key];
                    if (b.nodes.Contains(e.a)) t.atA[e.index] = kv.Value; else t.atB[e.index] = kv.Value;
                }
                foreach (int ei in b.inside) { var e = map.edges[ei]; t.atA[e.index] = e.length; t.atB[e.index] = 0f; }
            }
            surfFrom = rule; surfTrims = t;
            return t;
        }

        /// <summary>Keep the vertices a straight run needs: a vertex goes when it lies within 2 mm (plan) and 5 mm (height)
        /// of the chord between its kept neighbours and that chord is at most 5 m.</summary>
        static List<int> RsThin(List<Vector2> p, List<float> y)
        {
            var keep = new List<int> { 0 }; int n = p.Count;
            int i = 0;
            while (i < n - 1)
            {
                int j = i + 1;
                for (int c = i + 2; c < n; c++)
                {
                    var ab = p[c] - p[i]; float len = ab.magnitude;
                    if (len > 5f || len < 1e-4f) break;
                    bool ok = true;
                    for (int q = i + 1; q < c && ok; q++)
                    {
                        float tt = Vector2.Dot(p[q] - p[i], ab) / (len * len);
                        ok = (p[i] + ab * tt - p[q]).magnitude <= 0.002f && Mathf.Abs(Mathf.Lerp(y[i], y[c], tt) - y[q]) <= 0.005f;
                    }
                    if (!ok) break;
                    j = c;
                }
                keep.Add(j); i = j;
            }
            return keep;
        }

        static void RsTop(TileMeshes tm, Vector2 org, JunctionOutline.Result r, List<Vector2> ring, float lift, bool paved)
        {
            var f = JunctionOutline.FillOnly(ring, null);
            var bk = GroundBucket(paved); var o = tm.origin;
            for (int i = 0; i + 2 < f.tris.Count; i += 3)
            {
                var a = f.fillVerts[f.tris[i]]; var b = f.fillVerts[f.tris[i + 1]]; var c = f.fillVerts[f.tris[i + 2]];
                Vector3 P(Vector2 p) => new Vector3(p.x + org.x - o.x, r.HeightAt(p) + lift, p.y + org.y - o.z);
                bk.Tri(P(a), P(b), P(c), GroundUV(paved, a.x + org.x, a.y + org.y), GroundUV(paved, b.x + org.x, b.y + org.y), GroundUV(paved, c.x + org.x, c.y + org.y));
                RsLandTris++;
            }
        }

        static void EmitRoadSurface(CityMap map, Trims trims, TileMeshes tm, JunctionSurfaces.Built js)
        {
            rsOwn = true;
            try { EmitRoadSurfaceCore(map, trims, tm, js); }
            finally { rsOwn = false; }
        }

        static void EmitRoadSurfaceCore(CityMap map, Trims trims, TileMeshes tm, JunctionSurfaces.Built js)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = js.res; var o = tm.origin; var org = js.origin;
            Vector3 W(Vector2 p, float y) => new Vector3(p.x + org.x - o.x, y, p.y + org.y - o.z);

            // 1. THE PAVEMENT: the fill, at its own heights (no lift), in its main arm's surface
            var me = map.edges[js.mainEdge];
            var bk = buckets[(int)SlotOf(JunctionProfile, ArmSurface(map, trims, me, js.mainNode))];
            int v0 = bk.v.Count, t0 = bk.t.Count;
            for (int i = 0; i < r.fillVerts.Count; i++)
            {
                var p = r.fillVerts[i];
                bk.v.Add(W(p, r.fillY[i])); bk.uv.Add(new Vector2((p.x + org.x) / 12f, (p.y + org.y) / 12f));
                if (bk.col != null) bk.col.Add(Bucket.Tint);
                if (bk.fac != null) bk.fac.Add(Vector4.zero);
            }
            // anticlockwise in map view is clockwise seen from above: up (as the fan's)
            for (int i = 0; i + 2 < r.tris.Count; i += 3) { bk.t.Add(v0 + r.tris[i]); bk.t.Add(v0 + r.tris[i + 2]); bk.t.Add(v0 + r.tris[i + 1]); }
            ProvHook?.Invoke(bk, bk.t, t0);
            tm.patchCount++; RsJunctions++; RsTris += r.tris.Count / 3;

            // 2. THE FREE EDGES: the outer ring's runs between the end rows, each with its kerb
            var ring = r.ringOuter; int n = ring.Count;
            int RowOf(Vector2 a, Vector2 b)
            {
                for (int i = 0; i < r.rowR.Length; i++)
                {
                    var p = r.rowR[i]; var d = r.rowL[i] - p; float l2 = d.sqrMagnitude; if (l2 < 1e-6f) continue;
                    float ta = Vector2.Dot(a - p, d) / l2, tb = Vector2.Dot(b - p, d) / l2;
                    if (ta < -1e-3f || ta > 1.001f || tb < -1e-3f || tb > 1.001f) continue;
                    if ((p + d * ta - a).magnitude < 0.003f && (p + d * tb - b).magnitude < 0.003f) return i;
                }
                return -1;
            }
            var isRow = new bool[n]; int firstRow = -1;
            for (int i = 0; i < n; i++) { isRow[i] = RowOf(ring[i], ring[(i + 1) % n]) >= 0; if (isRow[i] && firstRow < 0) firstRow = i; }
            if (firstRow >= 0)
            {
                int i = firstRow;
                for (int guard = 0; guard < n; )
                {
                    while (guard < n && isRow[i]) { i = (i + 1) % n; guard++; }
                    if (guard >= n) break;
                    var cp = new List<Vector2>(); var cy = new List<float>();
                    cp.Add(ring[i]);
                    while (guard < n && !isRow[i]) { i = (i + 1) % n; guard++; cp.Add(ring[i]); }
                    foreach (var p in cp) cy.Add(r.HeightAt(p));
                    // which corner the run holds: a median end's land is enclosed (closed against the kerb); else open ground
                    JunctionOutline.Corner corner = null; float bd = 0.05f;
                    foreach (var c in r.corners)
                    {
                        if (c.pts.Count == 0) continue;
                        var m = c.pts[c.pts.Count / 2];
                        foreach (var p in cp) { float d = (p - m).magnitude; if (d < bd) { bd = d; corner = c; } }
                    }
                    bool median = corner != null && (corner.shape == "connector" || corner.shape == "nose");
                    EmitRsRun(map, trims, tm, js, cp, cy, median, corner);
                }
            }

            // 3. THE ISLANDS: each a closed solid on its hole's own vertices (the kerb face up from the pavement, the top)
            for (int h = 0; h < r.ringHoles.Count; h++)
            {
                var hr = r.ringHoles[h]; int m = hr.Count; if (m < 3) continue;
                float kh = h < r.holeKerb.Count ? r.holeKerb[h] : RsKerbH; int kind = h < r.holeKind.Count ? r.holeKind[h] : -1;
                float run = 0f;
                for (int k = 0; k < m; k++)
                {
                    var a = hr[k]; var b = hr[(k + 1) % m]; var d = b - a; float len = d.magnitude; if (len < 1e-4f) continue;
                    float ya = r.HeightAt(a), yb = r.HeightAt(b);
                    // the ring runs clockwise: the pavement is on its left
                    kerbBucket.WallSloped(W(a, 0f), W(b, 0f), ya, ya + kh, yb, yb + kh, new Vector2(-d.y, d.x) / len, run * 0.3f, (run + len) * 0.3f, 0f, 0.05f);
                    run += len; RsKerbTris += 2;
                }
                RsKerbM += run;
                RsTop(tm, org, r, hr, kh, kind != 0);
            }
            RsBuildMs += sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>One free run of the outline (anticlockwise: the land on its right): the kerb's face and top on the run's
        /// own vertices (thinned on straights), then the verge (open ground) or the median's land closed against it.</summary>
        static void EmitRsRun(CityMap map, Trims trims, TileMeshes tm, JunctionSurfaces.Built js, List<Vector2> cp, List<float> cy, bool median, JunctionOutline.Corner corner)
        {
            var r = js.res; var o = tm.origin; var org = js.origin;
            Vector3 W(Vector2 p, float y) => new Vector3(p.x + org.x - o.x, y, p.y + org.y - o.z);
            var keep = RsThin(cp, cy); int m = keep.Count; if (m < 2) return;
            var p2 = new List<Vector2>(m); var y2 = new List<float>(m);
            foreach (int k in keep) { p2.Add(cp[k]); y2.Add(cy[k]); }
            // vertex normals to the land side (the right of the run), mitred so the kerb's back keeps its width
            var nr = new Vector2[m];
            for (int k = 0; k < m; k++)
            {
                Vector2 d0 = k > 0 ? (p2[k] - p2[k - 1]).normalized : (p2[1] - p2[0]).normalized;
                Vector2 d1 = k + 1 < m ? (p2[k + 1] - p2[k]).normalized : d0;
                var n0 = new Vector2(d0.y, -d0.x); var n1 = new Vector2(d1.y, -d1.x);
                var s = (n0 + n1).normalized; float c = Mathf.Max(0.5f, Vector2.Dot(s, n0));
                nr[k] = s / c;
            }
            var back = new List<Vector2>(m);
            float run = 0f;
            for (int k = 0; k < m; k++) back.Add(p2[k] + nr[k] * RsKerbW);
            for (int k = 0; k + 1 < m; k++)
            {
                float len = (p2[k + 1] - p2[k]).magnitude; if (len < 1e-4f) continue;
                var d = (p2[k + 1] - p2[k]) / len; var into = new Vector2(-d.y, d.x);   // the pavement side
                float u0 = run * 0.3f, u1 = (run + len) * 0.3f;
                kerbBucket.WallSloped(W(p2[k], 0f), W(p2[k + 1], 0f), y2[k], y2[k] + RsKerbH, y2[k + 1], y2[k + 1] + RsKerbH, into, u0, u1, 0f, 0.05f);
                kerbBucket.Up(W(p2[k], y2[k] + RsKerbH), W(p2[k + 1], y2[k + 1] + RsKerbH), W(back[k + 1], y2[k + 1] + RsKerbH), W(back[k], y2[k] + RsKerbH),
                              new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.05f), new Vector2(u0, 0.05f));
                run += len; RsKerbTris += 4;
                if (!median)
                {
                    if (groundLog != null) groundTag = $"road-surface verge {js.name}";
                    EmitVergeLine(map, trims, tm, W(back[k], y2[k] + RsKerbH), W(back[k + 1], y2[k + 1] + RsKerbH), nr[k].normalized, nr[k + 1].normalized, 0.3f);
                }
            }
            RsKerbM += run;
            if (median && m >= 3)
            {
                // the land between the two carriageways from the nose out to the rows: the kerb's back line, closed across
                // the median at the rows (where the ribbons' own median takes over)
                float w = (back[0] - back[m - 1]).magnitude;
                int kind = corner != null ? corner.kind : -1;
                bool paved = kind == 1 || (kind < 0 && w < 1.8f);
                var land = new List<Vector2>(back);
                if (JunctionOutline.SelfCross(land) == null) RsTop(tm, org, r, land, RsKerbH, paved);
                else Debug.Log($"[RoadSurface] {js.name}: the land behind corner e{corner.a}/e{corner.b} crosses itself (its kerb's back line): not filled");
            }
        }
    }
}
