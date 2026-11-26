using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CUT'S SOLID IS THE ROCK THAT IS DRAWN (stage4, 2026-11-26; PSX_BANKSOLID, default on, =0 the old slab).
    ///
    /// The owner, on exe frames PC71 | PC72 of the Little Switzerland cut: the blocky relief of 77f4abf8 "looks better
    /// and is more engaging", the flattened rock of PSX_BANKFIT "looks very flat and unrealistic". So the drawn rock
    /// stays exactly as it is, and the SOLID is made from it instead: in the band a car can touch (the toe, 0.5 m under
    /// the tarmac edge, to BandSolidH over it) every faced stretch of a cut gets ONE closed MeshCollider on the Solid
    /// layer whose road face is the relief's own vertices (<see cref="ReliefChordCols"/>, the very columns ReliefFace
    /// draws) joined by the very same zip of triangles. Not convex pieces per block: a hull per block would stand in
    /// front of every concave block face and every joint, and a few hundred hulls per cut cost more to test than one
    /// mesh; one mesh per stretch also keeps the wall solids' rule (no seam between colliders for a scraping car to
    /// stop on, WallGeom).
    ///
    /// The face is then only ever moved TOWARD the road, never behind the drawn rock:
    ///   * POCKETS NARROWER THAN A CAR are closed: a flat opening along the cut, BandOpenM wide, at every height (a
    ///     joint or a block set back between two proud ones is bridged at the lower rim);
    ///   * no face turns along the cut more steeply than BandSlopeAlong (a car sliding along the cut meets a ramp,
    ///     not the square end of a block);
    ///   * no OVERHANG steeper than BandUndercut over the band (nothing a nose goes under).
    /// THE OUTCROPS AND TALUS STONES PlaceCliffs sets into a cut (drawn by MeshForest, no collider of their own, often
    /// in front of the relief) are each solid as themselves where any part is in reach: a static MeshCollider of the
    /// mesh MeshForest draws near, at its own place ("BankRock"; one cooked mesh per model).
    /// THE OUTCROPS AND TALUS STONES PlaceCliffs sets into a cut (drawn by MeshForest, no collider of their own, often
    /// in front of the relief) are each solid as themselves where any part is in reach: a static MeshCollider of the
    /// mesh MeshForest draws near, at its own place ("BankRock"; one cooked mesh per model, shared).
    /// Behind the face the solid is closed BandThick past its deepest point; its top runs back from the band's top
    /// row falling at BandTopFall (under any rock top, which falls at most TraversableSlope), its caps close the
    /// stretch's two end columns. Over the band the rock is out of reach and only the rock top's collider stands.
    ///
    /// THE CREST LIP at a cut's END (the "4-6.5 m in front of the solid at every cut end"): where a face tapers off,
    /// its crest comes down into reach and the soil lip over it (BuildCrestLip, drawn only) stood up to a metre over
    /// the crest and half a metre in front of it, with nothing solid nearer than the rock top's far slope. With this
    /// on, the lip's quads within LipReachH of the edge are solid as well (its own triangles: a mound of soil a car
    /// rides onto, off the Solid layer).
    ///
    /// PSX_BANKSOLID_AB=1 (measuring only, never in a build that ships): the old slab and the old face collider are
    /// built as well, under an inactive "BankSolidOld" per cut, for BankFitProbe's PSX_BF_SOLIDSET=old.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        public static bool BankSolid = System.Environment.GetEnvironmentVariable("PSX_BANKSOLID") != "0";
        static bool BankSolidAB => System.Environment.GetEnvironmentVariable("PSX_BANKSOLID_AB") == "1";
        /// <summary>The band's top over the tarmac edge (the first relief row at or over it).</summary>
        const float BandSolidH = 2.5f;
        /// <summary>A pocket narrower than this along the cut is closed (a car's corner, a wheel).</summary>
        const float BandOpenM = 1.2f;
        /// <summary>Metres the solid's face may step into the hill per metre along the cut.</summary>
        const float BandSlopeAlong = 1.5f;
        /// <summary>Metres the solid's face may stand out over the road per metre up (an overhang's limit), in the
        /// band a nose or a wheel reaches (to BandUndercutTop over the edge); over it the drawn overhangs stand.</summary>
        const float BandUndercut = 0.5f, BandUndercutTop = 1.1f;
        /// <summary>No face vertex is moved out more than this from the drawn rock: a deeper pocket is narrowed, not
        /// filled (a solid half a metre in front of the rock it stands for is the old fault again).</summary>
        const float BandMaxFill = 0.4f;
        /// <summary>The solid's depth past its deepest face point.</summary>
        const float BandThick = 0.5f;
        /// <summary>The solid's top falls this much per metre back from the band's top row.</summary>
        const float BandTopFall = 0.4f;
        /// <summary>The face this far in front of an outcrop or talus stone it is pulled out to.</summary>
        const float BandRockSeat = 0.02f;
        /// <summary>A crest lip quad lower than this over the edge is solid.</summary>
        const float LipReachH = 2.6f;
        const int BandRayLayer = 31;
        static int bandSolids, bandTris, bandRockPulls, lipCollTris, bandFailed, bandCliffSolids;
        /// <summary>Face vertices 0-1.6 m over the edge, and how many each step moved out more than 0.15 m.</summary>
        static int bandStatN, bandStatRock, bandStatOpen, bandStatShape;
        static double bandMs;

        /// <summary>The band solids of one cut (its faced stretches, as ReliefFace draws them).</summary>
        static void BuildBandSolids(Transform parent, PhysicsMaterial phys, int no, Vector3[] toe, Vector3[] plinthTop,
                                    Vector3[] crest, bool[] faced, float[] distAt, Vector3[] outw, int R, int seed,
                                    int rock0, int rock1)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // the outcrops and talus stones set into this cut, as temporary colliders (removed below)
            var temp = new List<GameObject>();
            var cliffHulls = new List<(Transform at, MeshCollider mc, int v, int q)>();
            var kit = rock1 > rock0 ? LoadMeshTreeKit() : null;
            if (kit != null)
            {
                var cliffV = new HashSet<int>();
                if (kit.cliffs != null) foreach (var ci in kit.cliffs) cliffV.Add(ci.v);
                var stoneV = new HashSet<int>(boulderVars);
                for (int q = rock0; q < rock1 && q < cliffPlaces.Count; q++)
                {
                    var c = cliffPlaces[q];
                    if (!cliffV.Contains(c.v) && !stoneV.Contains(c.v)) continue;   // leaf litter is not rock
                    var vr = kit.variants[c.v];
                    if (vr == null || vr.lods == null || vr.lods.Length == 0 || vr.lods[0] == null) continue;
                    var go = new GameObject("BandRock") { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = c.pos;
                    go.transform.localRotation = Quaternion.Euler(0f, c.yaw, 0f);
                    go.transform.localScale = new Vector3(c.scale, c.scale, c.scale);
                    var hull = new GameObject("BandRock");
                    hull.transform.SetParent(go.transform, false);
                    hull.transform.localPosition = vr.pre.GetColumn(3);
                    hull.transform.localRotation = vr.pre.rotation;
                    hull.transform.localScale = vr.pre.lossyScale;
                    hull.layer = BandRayLayer;
                    var hmc = hull.AddComponent<MeshCollider>();
                    hmc.sharedMesh = vr.lods[0];
                    temp.Add(go);
                    cliffHulls.Add((go.transform, hmc, c.v, q));
                }
                if (temp.Count > 0) Physics.SyncTransforms();
            }
            try
            {
                int sub = 0;
                for (int k = 1; k < R; )
                {
                    if (!faced[k - 1] || !faced[k]) { k++; continue; }
                    int k1 = k;
                    while (k1 + 1 < R && faced[k1 + 1]) k1++;
                    var cols = new List<ReliefCol>();
                    for (int kk = k; kk <= k1; kk++)
                    {
                        var rc = ReliefChordCols(kk, toe, plinthTop, crest, faced, distAt, outw, R, seed, null, out _);
                        for (int ia = kk == k ? 0 : 1; ia < rc.Count; ia++) cols.Add(rc[ia]);
                    }
                    try { BandStretch(cols, parent, phys, "CollBand" + no + "_" + sub++, false); }
                    catch (System.Exception e)
                    {
                        // a stretch that fails is reported, never left without a solid: the old slab's way for it
                        bandFailed++;
                        Log($"BAND SOLID FAILED cut {no} stretch {sub - 1} ({cols.Count} columns): {e}");
                        Debug.LogException(e);
                        var fb = new List<Vector3>(); var ft = new List<Vector3>(); var fo = new List<Vector3>();
                        foreach (var col in cols)
                        {
                            Vector3 b = col.smooth[0];
                            float top = Mathf.Min(col.smooth[col.smooth.Length - 1].y, col.toeY + 0.5f + BandSolidH);
                            fb.Add(b); ft.Add(new Vector3(b.x, top, b.z)); fo.Add(col.inward);
                        }
                        BuildWallSolid("BankColl", parent, fb, ft, fo, 1.2f, false, phys, "CollBandFallback" + no + "_" + (sub - 1));
                    }
                    k = k1 + 1;
                }
                // THE OUTCROPS AND TALUS STONES IN REACH: PlaceCliffs sets Meadow cliffs into the face (and up to
                // 1.3 x a low face over its crest) and stones at its foot, drawn by MeshForest with no collider of
                // their own, often in front of the relief. A 0.7 m solid cannot follow their outlines (pulling its
                // vertices out to them held it off the relief beside them), so each one any part of which is within
                // BandSolidH of the edge is solid as ITSELF: a static MeshCollider of the very mesh MeshForest draws
                // near (LOD0) at its own place - one cooked mesh per model, shared by every instance.
                foreach (var ch in cliffHulls)
                {
                    var b = ch.mc.bounds;
                    int kn = 0; float best = float.MaxValue;
                    for (int k = 0; k < R; k++)
                    {
                        float dx = crest[k].x - b.center.x, dz = crest[k].z - b.center.z;
                        if (dx * dx + dz * dz < best) { best = dx * dx + dz * dz; kn = k; }
                    }
                    float edgeY = toe[kn].y + 0.5f;
                    if (b.min.y > edgeY + BandSolidH) continue;
                    var vr = kit.variants[ch.v];
                    var go = new GameObject("BankRock");
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = ch.at.localPosition;
                    go.transform.localRotation = ch.at.localRotation;
                    go.transform.localScale = ch.at.localScale;
                    var hull = new GameObject("BankRock");
                    hull.transform.SetParent(go.transform, false);
                    hull.transform.localPosition = vr.pre.GetColumn(3);
                    hull.transform.localRotation = vr.pre.rotation;
                    hull.transform.localScale = vr.pre.lossyScale;
                    hull.layer = SolidLayer;
                    var mc = hull.AddComponent<MeshCollider>();
                    mc.sharedMesh = vr.lods[0];
                    mc.convex = false;
                    mc.sharedMaterial = phys;
                    go.isStatic = hull.isStatic = true;
                    bandCliffSolids++;
                    // a stone the forest pass drops (it met a wall or a portal) takes this solid with it
                    var cp = cliffPlaces[ch.q];
                    if (cp.r > 0f && cp.solid == null) { cp.solid = go; cliffPlaces[ch.q] = cp; }
                }
            }
            finally
            {
                foreach (var g in temp) Object.DestroyImmediate(g);
                if (temp.Count > 0) Physics.SyncTransforms();
            }
            bandMs += sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>The ZIP that draws the relief between two columns (ReliefFace), run until both columns have
        /// reached their band top: where it stands then.</summary>
        static void BandZipStop(int nl, int nr, int tl, int tr, out int si, out int sj)
        {
            int i = 0, j = 0;
            while (!(i >= tl && j >= tr) && (i < nl || j < nr))
            {
                bool stepL = j >= nr || (i < nl && (i + 1) / (float)nl <= (j + 1) / (float)nr);
                if (stepL) i++; else j++;
            }
            si = i; sj = j;
        }

        static void BandStretch(List<ReliefCol> C, Transform parent, PhysicsMaterial phys, string meshName, bool rocks)
        {
            int N = C.Count;
            if (N < 2) return;
            // THE BAND: rows to the first at or over BandSolidH; then raised until the drawn zip between every two
            // neighbours passes through both tops (so the solid's face is the drawn triangles, and nothing else)
            var T = new int[N];
            for (int j = 0; j < N; j++)
            {
                var col = C[j];
                int nu = col.p.Length - 1, m = nu;
                float edge = col.toeY + 0.5f;
                for (int i = 0; i <= nu; i++) if (col.p[i].y - edge >= BandSolidH) { m = i; break; }
                T[j] = Mathf.Max(1, m);
            }
            for (int it = 0; it < 200; it++)
            {
                bool changed = false;
                for (int j = 0; j + 1 < N; j++)
                {
                    BandZipStop(C[j].p.Length - 1, C[j + 1].p.Length - 1, T[j], T[j + 1], out int si, out int sj);
                    if (si > T[j]) { T[j] = si; changed = true; }
                    if (sj > T[j + 1]) { T[j + 1] = sj; changed = true; }
                }
                if (!changed) break;
            }
            // depth into the hill per vertex, from the drawn rock's own
            var c = new float[N][];
            for (int j = 0; j < N; j++)
            {
                c[j] = new float[T[j] + 1];
                for (int i = 0; i <= T[j]; i++) c[j][i] = C[j].d[i];
            }
            float Y(int j, int i) => C[j].p[i].y;
            float DAt(float[][] arr, int x, float y)
            {
                var col = C[x]; var cx = arr[x]; int t = T[x];
                if (y <= col.p[0].y) return cx[0];
                for (int i = 0; i < t; i++)
                    if (y <= col.p[i + 1].y)
                        return Mathf.Lerp(cx[i], cx[i + 1], Mathf.InverseLerp(col.p[i].y, col.p[i + 1].y, y));
                return cx[t];
            }

            float[][] Snap() { var z = new float[N][]; for (int j = 0; j < N; j++) z[j] = (float[])c[j].Clone(); return z; }
            int Moved(float[][] before)
            {
                int m = 0;
                for (int j = 0; j < N; j++)
                {
                    float edge = C[j].toeY + 0.5f;
                    for (int i = 0; i <= T[j]; i++)
                    {
                        float hy = Y(j, i) - edge;
                        if (hy < 0f || hy > 1.6f) continue;
                        if (before[j][i] - c[j][i] > 0.15f) m++;
                    }
                }
                return m;
            }
            for (int j = 0; j < N; j++)
            {
                float edge = C[j].toeY + 0.5f;
                for (int i = 0; i <= T[j]; i++) { float hy = Y(j, i) - edge; if (hy >= 0f && hy <= 1.6f) bandStatN++; }
            }
            var s0 = Snap();
            // 1. ROCK IN FRONT: an outcrop or talus stone nearer the road than the relief
            if (rocks)
            {
                int mask = 1 << BandRayLayer;
                for (int j = 0; j < N; j++)
                {
                    var col = C[j];
                    Vector3 side = Vector3.Cross(Vector3.up, col.inward).normalized;
                    for (int i = 0; i <= T[j]; i++)
                    {
                        float best = c[j][i];
                        // the vertex and four points about it (a quarter of the way to its neighbours)
                        for (int r = 0; r < 5; r++)
                            {
                                int ox = r == 1 ? -1 : r == 2 ? 1 : 0, oy = r == 3 ? -1 : r == 4 ? 1 : 0;
                                Vector3 o = col.smooth[i] + side * (ox * 0.18f) + Vector3.up * (oy * 0.13f) - col.inward * 1.5f;
                                if (Physics.Raycast(o, col.inward, out RaycastHit h, 1.5f + best + 0.05f, mask, QueryTriggerInteraction.Ignore))
                                    best = Mathf.Min(best, h.distance - 1.5f - BandRockSeat);
                            }
                        if (best < c[j][i] - 1e-4f) { c[j][i] = best; bandRockPulls++; }
                    }
                }
            }

            bandStatRock += Moved(s0);
            var s1 = Snap();
            // 2. POCKETS NARROWER THAN A CAR: a flat opening along the cut at each vertex's height
            {
                var a = new float[N];
                for (int j = 0; j < N; j++) a[j] = C[j].a;
                float hw = BandOpenM * 0.5f;
                var o = new float[N][];
                for (int j = 0; j < N; j++)
                {
                    o[j] = new float[T[j] + 1];
                    int x0 = j, x1 = j;
                    while (x0 > 0 && a[j] - a[x0 - 1] <= hw) x0--;
                    while (x1 + 1 < N && a[x1 + 1] - a[j] <= hw) x1++;
                    for (int i = 0; i <= T[j]; i++)
                    {
                        float y = Y(j, i), open = float.MinValue;
                        for (int x = x0; x <= x1; x++)
                        {
                            float ero = float.MaxValue;
                            for (int z = x; z >= 0 && a[x] - a[z] <= hw; z--) ero = Mathf.Min(ero, DAt(c, z, y));
                            for (int z = x + 1; z < N && a[z] - a[x] <= hw; z++) ero = Mathf.Min(ero, DAt(c, z, y));
                            open = Mathf.Max(open, ero);
                        }
                        o[j][i] = Mathf.Min(c[j][i], open);
                    }
                }
                c = o;
            }
            bandStatOpen += Moved(s1);
            var s2 = Snap();

            // 3. along the cut no steeper than BandSlopeAlong; 4. no overhang past BandUndercut (twice: each can
            // tighten the other)
            for (int pass = 0; pass < 2; pass++)
            {
                for (int j = 1; j < N; j++)
                {
                    float da = Mathf.Abs(C[j].a - C[j - 1].a) * BandSlopeAlong;
                    for (int i = 0; i <= T[j]; i++) c[j][i] = Mathf.Min(c[j][i], DAt(c, j - 1, Y(j, i)) + da);
                }
                for (int j = N - 2; j >= 0; j--)
                {
                    float da = Mathf.Abs(C[j + 1].a - C[j].a) * BandSlopeAlong;
                    for (int i = 0; i <= T[j]; i++) c[j][i] = Mathf.Min(c[j][i], DAt(c, j + 1, Y(j, i)) + da);
                }
                for (int j = 0; j < N; j++)
                {
                    var col = C[j];
                    float edgeU = col.toeY + 0.5f;
                    for (int i = T[j] - 1; i >= 0; i--)
                    {
                        if (Y(j, i) - edgeU > BandUndercutTop) continue;
                        float esI = Vector3.Dot(col.smooth[i] - col.smooth[0], col.inward);
                        float esU = Vector3.Dot(col.smooth[i + 1] - col.smooth[0], col.inward);
                        float lim = esU + c[j][i + 1] + BandUndercut * (Y(j, i + 1) - Y(j, i)) - esI;
                        c[j][i] = Mathf.Min(c[j][i], lim);
                    }
                }
            }

            bandStatShape += Moved(s2);
            for (int j = 0; j < N; j++)
                for (int i = 0; i <= T[j]; i++) c[j][i] = Mathf.Max(c[j][i], C[j].d[i] - BandMaxFill);

            // THE MESH: per column the face (rows 0..T), the back top and the back foot
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var front = new int[N][];
            var bt = new int[N]; var bb = new int[N];
            var back = new float[N];
            for (int j = 0; j < N; j++)
            {
                var col = C[j];
                Vector3 b0 = col.smooth[0];
                front[j] = new int[T[j] + 1];
                float eMax = float.MinValue;
                var e = new float[T[j] + 1];
                for (int i = 0; i <= T[j]; i++)
                {
                    Vector3 q = col.smooth[i] + col.inward * c[j][i];
                    front[j][i] = verts.Count; verts.Add(q);
                    e[i] = Vector3.Dot(q - b0, col.inward);
                    eMax = Mathf.Max(eMax, e[i]);
                }
                float eBack = eMax + BandThick;
                back[j] = eBack;
                Vector3 qT = verts[front[j][T[j]]], q0 = verts[front[j][0]];
                float yTop = qT.y, yBt = Mathf.Max(q0.y + 0.05f, yTop - (eBack - e[T[j]]) * BandTopFall);
                // the top's slope must stay behind every face point over it (else the section folds): level if not
                for (int i = 0; i < T[j] && yBt < yTop; i++)
                {
                    float yi = verts[front[j][i]].y;
                    if (yi <= yBt) continue;
                    float eLine = e[T[j]] + (eBack - e[T[j]]) * (yTop - yi) / Mathf.Max(1e-4f, yTop - yBt);
                    if (e[i] >= eLine - 0.02f) yBt = yTop;
                }
                Vector3 pb = b0 + col.inward * eBack;
                bt[j] = verts.Count; verts.Add(new Vector3(pb.x, yBt, pb.z));
                bb[j] = verts.Count; verts.Add(new Vector3(pb.x, q0.y, pb.z));
            }
            for (int j = 0; j + 1 < N; j++)
            {
                Vector3 toRoad = -(C[j].inward + C[j + 1].inward);
                int nl = C[j].p.Length - 1, nr = C[j + 1].p.Length - 1, i = 0, k = 0;
                // the drawn zip, to both tops
                while (!(i >= T[j] && k >= T[j + 1]))
                {
                    bool stepL = k >= nr || (i < nl && (i + 1) / (float)nl <= (k + 1) / (float)nr);
                    // (never past a top: the tops above make the zip pass through both, this only guards it)
                    if (stepL && i >= T[j]) stepL = false;
                    else if (!stepL && k >= T[j + 1]) stepL = true;
                    int va = front[j][i], vb = front[j + 1][k], vc;
                    if (stepL) { i++; vc = front[j][i]; } else { k++; vc = front[j + 1][k]; }
                    TriFacing(verts, tris, va, vb, vc, toRoad);
                }
                Vector3 inw = C[j].inward + C[j + 1].inward;
                QuadFacingSkipFlat(verts, tris, front[j][T[j]], bt[j], bt[j + 1], front[j + 1][T[j + 1]], Vector3.up);
                QuadFacingSkipFlat(verts, tris, bt[j], bb[j], bb[j + 1], bt[j + 1], inw);
                QuadFacingSkipFlat(verts, tris, bb[j], front[j][0], front[j + 1][0], bb[j + 1], Vector3.down);
            }
            // the two caps: the end column's section
            for (int end = 0; end < 2; end++)
            {
                int j = end == 0 ? 0 : N - 1;
                Vector3 along = C[end == 0 ? 1 : N - 1].smooth[0] - C[end == 0 ? 0 : N - 2].smooth[0]; along.y = 0f;
                Vector3 want = end == 0 ? -along : along;
                var ring = new List<int>();
                for (int i = 0; i <= T[j]; i++) ring.Add(front[j][i]);
                ring.Add(bt[j]); ring.Add(bb[j]);
                var b0 = C[j].smooth[0];
                var poly = new List<Vector2>(ring.Count);
                foreach (int vi in ring) poly.Add(new Vector2(Vector3.Dot(verts[vi] - b0, C[j].inward), verts[vi].y));
                foreach (var t3 in EarClip(poly)) TriFacing(verts, tris, ring[t3.x], ring[t3.y], ring[t3.z], want);
            }
            if (tris.Count == 0) return;
            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts.ToArray(), triangles = tris.ToArray(),
            };
            SaveMesh(mesh, meshName);
            var go = new GameObject("BankColl");
            go.transform.SetParent(parent, false);
            go.layer = SolidLayer;
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = false;
            mc.sharedMesh = mesh;
            mc.sharedMaterial = phys;
            go.isStatic = true;
            wallSolidCount++;
            for (int j = 0; j + 1 < N; j++) { Vector3 st = C[j + 1].smooth[0] - C[j].smooth[0]; st.y = 0f; wallSolidM += st.magnitude; }
            bandSolids++;
            bandTris += tris.Count / 3;
        }

        /// <summary>One triangle wound to face <paramref name="want"/>; none if it has no area.</summary>
        static void TriFacing(List<Vector3> verts, List<int> tris, int a, int b, int c, Vector3 want)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (n.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(n, want) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        /// <summary>A simple polygon's triangles (ear clipping); a fan from the first corner for what is left if no
        /// ear is found (collinear corners).</summary>
        static List<Vector3Int> EarClip(List<Vector2> poly)
        {
            var res = new List<Vector3Int>();
            int n = poly.Count;
            if (n < 3) return res;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            float area = 0f;
            for (int i = 0; i < n; i++) { var p = poly[i]; var q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            if (area < 0f) idx.Reverse();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 4 * n * n)
            {
                bool clipped = false;
                int cnt = idx.Count;
                for (int k = 0; k < cnt; k++)
                {
                    int ia = idx[(k - 1 + cnt) % cnt], ib = idx[k], ic = idx[(k + 1) % cnt];
                    Vector2 A = poly[ia], B = poly[ib], Cc = poly[ic];
                    if (Cross(A, B, Cc) <= 1e-7f) continue;
                    bool inside = false;
                    for (int m = 0; m < cnt && !inside; m++)
                    {
                        int im = idx[m];
                        if (im == ia || im == ib || im == ic) continue;
                        Vector2 P = poly[im];
                        if (Cross(A, B, P) >= 0f && Cross(B, Cc, P) >= 0f && Cross(Cc, A, P) >= 0f) inside = true;
                    }
                    if (inside) continue;
                    res.Add(new Vector3Int(ia, ib, ic));
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            for (int k = 1; k + 1 < idx.Count; k++) res.Add(new Vector3Int(idx[0], idx[k], idx[k + 1]));
            return res;
        }
    }
}
