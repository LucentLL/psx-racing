using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// MEASURE THE BODYWORK OVER EVERY WHEEL of every shell, once, off the real
    /// body mesh, and write Resources/ArchCeilings.bytes (see
    /// <see cref="ArchCeilings"/> for what the field is and why).
    ///
    /// Run after any new or changed shell, like the lamp finder and the camera
    /// probe: `-executeMethod PSXRacing.EditorTools.ArchProbe.Bake` (needs the
    /// shell bake in place - it reads Resources/CarModels). <see cref="Report"/>
    /// then writes PSXRacing_archroom.txt: for every catalog car, how far each
    /// axle may rise from its stock ride before the tyre meets the body.
    ///
    /// The instrument is the one CarModelBaker settled on for the cowl: hit the
    /// SURFACE with vertical rays (barycentric tests over the triangles), never
    /// bin vertices - a 4,000-triangle shell has no vertex halfway across a
    /// wing. Rays start at the model's own hub height and take the FIRST
    /// bodywork above it, front face or back (a wing is one sheet of
    /// triangles), skipping anything inside the model's own wheel or inboard
    /// of it within its radius of the axle: a brake disc, a hub, a driveshaft
    /// or a solid axle modelled into the body moves WITH the wheel (the AE86
    /// carries its live rear axle in the body mesh) and is never a ceiling.
    /// </summary>
    public static class ArchProbe
    {
        const string OutPath = "Assets/PSXRacing/Resources/" + ArchCeilings.ResourceName + ".bytes";
        /// <summary>Sub-samples per lane: a cell keeps the lowest of three
        /// across its lane (edges shared with its neighbours), at its row's
        /// exact station along the car.</summary>
        const int Sub = 2;

        [MenuItem("Tools/PSX Racing/Bake Arch Ceilings")]
        public static void Bake()
        {
            var shells = new List<ArchCeilings.Shell>();
            var log = new StringBuilder();
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def == null || def.bodyMesh == null) { log.AppendLine($"SKIP {m.key}: no bake"); continue; }
                shells.Add(Measure(def, log));
            }
            File.WriteAllBytes(OutPath, ArchCeilings.Write(shells));
            AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);
            ArchCeilings.ClearCache();
            log.Insert(0, $"[ArchProbe] {shells.Count} shells -> {OutPath} ({new FileInfo(OutPath).Length / 1024} KB)\n");
            File.WriteAllText("PSXRacing_archprobe.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        // ------------------------------------------------------------------
        //  THE EXACT TABLE (ArchHubs, 2026-10-20)
        // ------------------------------------------------------------------
        const string HubsPath = "Assets/PSXRacing/Resources/" + ArchHubs.ResourceName + ".bytes";
        /// <summary>Plan buckets for the body's triangles (m).</summary>
        const float BucketM = 0.015f;
        /// <summary>The tyre's swept profile: lane width across the tread in
        /// mesh space, and the face sampling pitch that finds a tread face
        /// passing between two vertices (m).</summary>
        const float ProfileLaneM = 0.01f, FaceSampleM = 0.01f;
        /// <summary>Round the tyre's top half (deg); the last few degrees at
        /// each end are its front and back at hub height.</summary>
        const float SweepStepDeg = 3f;

        /// <summary>One shell, ready for exact questions: its triangles above
        /// the model's hub, bucketed in plan; each axle's own hub and tyre (for
        /// the in-wheel skip); its wheel's swept profile.</summary>
        sealed class ShellGeom
        {
            public Vector3[] v;
            public int[] tris;
            public float x0, z0;
            public int nx, nz;
            public int[][] buckets;
            public float hubY;
            public readonly float[] hubX = new float[2], hubZ = new float[2], ownR = new float[2], ownW = new float[2];
            public float laneX0, laneW;
            public float[] laneRho;
        }

        static ShellGeom Geometry(CarModelDef def)
        {
            var g = new ShellGeom { hubY = def.wheelRadius };
            float trackR = def.trackRear > 0.1f ? def.trackRear : def.trackWidth;
            g.hubX[0] = def.trackWidth * 0.5f; g.hubZ[0] = def.wheelbase * 0.5f;
            g.hubX[1] = trackR * 0.5f;         g.hubZ[1] = -def.wheelbase * 0.5f;
            for (int a = 0; a < 2; a++) OwnTyre(def, a, out g.ownR[a], out g.ownW[a]);

            var mv = def.bodyMesh.vertices;
            var all = def.bodyMesh.triangles;
            g.v = new Vector3[mv.Length];
            for (int i = 0; i < mv.Length; i++) g.v[i] = ShellFrame(def, mv[i]);
            var keep = new List<int>();
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int t = 0; t + 2 < all.Length; t += 3)
            {
                Vector3 p0 = g.v[all[t]], p1 = g.v[all[t + 1]], p2 = g.v[all[t + 2]];
                if (Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)) <= g.hubY) continue;
                float det = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
                if (Mathf.Abs(det) < 1e-10f) continue;
                keep.Add(all[t]); keep.Add(all[t + 1]); keep.Add(all[t + 2]);
                minX = Mathf.Min(minX, Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))); maxX = Mathf.Max(maxX, Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)));
                minZ = Mathf.Min(minZ, Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z))); maxZ = Mathf.Max(maxZ, Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z)));
            }
            g.tris = keep.ToArray();
            g.x0 = minX; g.z0 = minZ;
            g.nx = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / BucketM) + 1);
            g.nz = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / BucketM) + 1);
            var lists = new List<int>[g.nx * g.nz];
            for (int t = 0; t + 2 < g.tris.Length; t += 3)
            {
                Vector3 p0 = g.v[g.tris[t]], p1 = g.v[g.tris[t + 1]], p2 = g.v[g.tris[t + 2]];
                int i0 = Mathf.FloorToInt((Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - g.x0) / BucketM);
                int i1 = Mathf.FloorToInt((Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) - g.x0) / BucketM);
                int k0 = Mathf.FloorToInt((Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)) - g.z0) / BucketM);
                int k1 = Mathf.FloorToInt((Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z)) - g.z0) / BucketM);
                for (int i = Mathf.Max(0, i0); i <= Mathf.Min(g.nx - 1, i1); i++)
                    for (int k = Mathf.Max(0, k0); k <= Mathf.Min(g.nz - 1, k1); k++)
                        (lists[i * g.nz + k] ??= new List<int>()).Add(t);
            }
            g.buckets = new int[lists.Length][];
            for (int i = 0; i < lists.Length; i++) g.buckets[i] = lists[i]?.ToArray();

            // The tyre as it can ever stand: the wheel mesh swept about its own
            // axle (the spin turns a low-poly tread), from its vertices and from
            // samples over every face, as the largest radius in each lane.
            var wm = def.wheelMesh;
            var wv = wm.vertices;
            var wt = wm.triangles;
            var b = wm.bounds;
            g.laneX0 = b.min.x; g.laneW = ProfileLaneM;
            int lanes = Mathf.Max(1, Mathf.CeilToInt(b.size.x / ProfileLaneM) + 1);
            g.laneRho = new float[lanes];
            void Add(Vector3 p)
            {
                int l = Mathf.Clamp(Mathf.FloorToInt((p.x - g.laneX0) / g.laneW), 0, lanes - 1);
                float rho = Mathf.Sqrt(p.y * p.y + p.z * p.z);
                if (rho > g.laneRho[l]) g.laneRho[l] = rho;
            }
            foreach (var p in wv) Add(p);
            for (int t = 0; t + 2 < wt.Length; t += 3)
            {
                Vector3 a = wv[wt[t]], c = wv[wt[t + 1]], d = wv[wt[t + 2]];
                float edge = Mathf.Max((a - c).magnitude, Mathf.Max((c - d).magnitude, (d - a).magnitude));
                int n = Mathf.Clamp(Mathf.CeilToInt(edge / FaceSampleM), 1, 64);
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n - i; j++)
                        Add(a + (c - a) * ((float)i / n) + (d - a) * ((float)j / n));
            }
            return g;
        }

        /// <summary>The first bodywork straight above a shell-frame point,
        /// higher than the model's hub, outside its own wheel: the exact
        /// question each field cell answered for a whole lane.</summary>
        static float ExactCeiling(ShellGeom g, int axle, float x, float z)
        {
            int i = Mathf.FloorToInt((x - g.x0) / BucketM), k = Mathf.FloorToInt((z - g.z0) / BucketM);
            if (i < 0 || i >= g.nx || k < 0 || k >= g.nz) return float.PositiveInfinity;
            var list = g.buckets[i * g.nz + k];
            if (list == null) return float.PositiveInfinity;
            float lowest = float.PositiveInfinity;
            foreach (int t in list)
            {
                Vector3 p0 = g.v[g.tris[t]], p1 = g.v[g.tris[t + 1]], p2 = g.v[g.tris[t + 2]];
                float det = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
                float l1 = ((p1.z - p2.z) * (x - p2.x) + (p2.x - p1.x) * (z - p2.z)) / det;
                if (l1 < -1e-5f) continue;
                float l2 = ((p2.z - p0.z) * (x - p2.x) + (p0.x - p2.x) * (z - p2.z)) / det;
                if (l2 < -1e-5f || l1 + l2 > 1f + 1e-5f) continue;
                float y = l1 * p0.y + l2 * p1.y + (1f - l1 - l2) * p2.y;
                if (y <= g.hubY || y >= lowest) continue;
                if (InWheel(Mathf.Abs(x), y, z, g.hubX[axle], g.hubY, g.hubZ[axle], g.ownR[axle], g.ownW[axle])) continue;
                lowest = y;
            }
            return lowest;
        }

        /// <summary>Wheel <paramref name="i"/>'s highest hub in the SHELL frame
        /// (the car frame less the fit's lift) steered <paramref name="steerDeg"/>:
        /// the least gap between the swept tyre and the bodywork straight
        /// above it - the same question ArchCeilings.HubMax asks of the field,
        /// asked of the triangles.</summary>
        static float ExactShellHub(ShellGeom g, CarModelLibrary.ShellFit f, int i, float steerDeg) =>
            ExactShellHub(g, f, i, steerDeg, out _);

        static float ExactShellHub(ShellGeom g, CarModelLibrary.ShellFit f, int i, float steerDeg, out string why)
        {
            why = "";
            int axle = i < 2 ? 0 : 1;
            Vector3 scale = f.WheelScale(i);
            f.Holder(i, out Vector3 holderPos, out Quaternion holderRot);
            Vector3 hub = f.Hub(i);
            Quaternion steer = Quaternion.Euler(0f, steerDeg, 0f);
            float sx = Mathf.Max(f.sx, 1e-3f), sz = Mathf.Max(f.sz, 1e-3f);
            float least = float.PositiveInfinity;
            for (int l = 0; l < g.laneRho.Length; l++)
            {
                if (g.laneRho[l] <= 0f) continue;
                float x = (g.laneX0 + (l + 0.5f) * g.laneW) * scale.x, rho = g.laneRho[l] * scale.y;
                for (float deg = -90f; deg <= 90f + 1e-3f; deg += SweepStepDeg)
                {
                    float a = deg * Mathf.Deg2Rad;
                    var p = new Vector3(x, rho * Mathf.Cos(a), rho * Mathf.Sin(a));
                    Vector3 car = hub + steer * (holderPos + holderRot * p);
                    float ceil = ExactCeiling(g, axle, car.x / sx, car.z / sz);
                    if (float.IsPositiveInfinity(ceil)) continue;
                    float gap = ceil - (car.y - hub.y);
                    if (gap < least)
                    {
                        least = gap;
                        why = $"binds at {deg:+0;-0} deg, {(x - (g.laneX0 + g.laneRho.Length * 0.5f * g.laneW) * scale.x) * 100f:+0.0;-0.0} cm across, " +
                              $"under bodywork {(ceil - g.hubY) * 100f:0.0} cm over the model hub";
                    }
                }
            }
            return least;
        }

        [MenuItem("Tools/PSX Racing/Bake Exact Arches")]
        public static void BakeExact()
        {
            var t0 = System.DateTime.Now;
            var log = new StringBuilder();
            var geoms = new Dictionary<string, ShellGeom>();
            var rows = new List<ArchHubs.Row>();
            var done = new HashSet<string>();
            var jobs = new List<(CarModelDef def, CarModelLibrary.SpecGeometry g)>();
            foreach (var m in CarModelLibrary.Models)
            {
                var d = CarModelLibrary.Load(m.key);
                if (d != null && d.bodyMesh != null && d.wheelMesh != null) jobs.Add((d, default));
            }
            foreach (var spec in CarCatalog.All)
            {
                var d = CarModelLibrary.LoadFor(spec);
                if (d != null && d.bodyMesh != null && d.wheelMesh != null) jobs.Add((d, CarModelLibrary.SpecGeometry.Of(spec)));
            }
            var deltas = new List<float>();
            var perShell = new Dictionary<string, (float lo, float hi, int n)>();
            foreach (var (def, sg) in jobs)
            {
                var f = CarModelLibrary.Fit(def, sg);
                if (f.tableKey == null || !done.Add(f.tableKey)) continue;
                if (!geoms.TryGetValue(def.key, out var g)) geoms[def.key] = g = Geometry(def);
                var row = new ArchHubs.Row { key = f.tableKey, fit = ArchHubs.FitPrint(f) };
                for (int i = 0; i < 4; i++)
                {
                    if (i < 2)
                    {
                        row.hubMm[i] = new ushort[ArchHubs.SteerSteps];
                        for (int k = 0; k < ArchHubs.SteerSteps; k++)
                            row.hubMm[i][k] = ArchHubs.Encode(ExactShellHub(g, f, i, k - ArchHubs.SteerDeg));
                    }
                    else row.hubMm[i] = new[] { ArchHubs.Encode(ExactShellHub(g, f, i, 0f)) };
                    // How much the field undersold it (straight).
                    float exact = ArchHubs.Decode(row.hubMm[i][i < 2 ? ArchHubs.SteerDeg : 0]);
                    float field = ArchCeilings.FieldHubMax(def, f, i) - f.dy;
                    if (!float.IsInfinity(exact) && !float.IsInfinity(field))
                    {
                        float dlt = exact - field;
                        deltas.Add(dlt);
                        perShell.TryGetValue(def.key, out var ps);
                        perShell[def.key] = ps.n == 0 ? (dlt, dlt, 1) : (Mathf.Min(ps.lo, dlt), Mathf.Max(ps.hi, dlt), ps.n + 1);
                    }
                }
                rows.Add(row);
                if ((sg.wheelbaseMm == 0 && sg.widthMm == 0) || def.key == System.Environment.GetEnvironmentVariable("PSX_ARCH_DIAG"))
                    for (int i = 0; i < 4; i += 2)
                    {
                        float h = ExactShellHub(g, f, i, 0f, out string why);
                        log.AppendLine($"  {def.key,-18} {(i == 0 ? "F" : "R")} {(sg.wheelbaseMm == 0 ? "own fit" : f.tableKey)}: hub max {h * 100f:0.0} cm " +
                                       $"(model hub {g.hubY * 100f:0.0}, fitted hub {(f.Hub(i).y - f.dy) * 100f:0.0}, r {f.Radius(i) * 100f:0.0}, " +
                                       $"tyre {f.WheelScale(i).x * def.wheelMesh.bounds.size.x * 100f:0.0} wide, camber {f.Camber(i):0.0}); {why}");
                    }
            }
            File.WriteAllBytes(HubsPath, ArchHubs.Write(rows));
            AssetDatabase.ImportAsset(HubsPath, ImportAssetOptions.ForceUpdate);
            ArchHubs.ClearCache();
            deltas.Sort();
            float Pct(float p) => deltas.Count == 0 ? 0f : deltas[Mathf.Clamp(Mathf.RoundToInt(p * (deltas.Count - 1)), 0, deltas.Count - 1)];
            log.AppendLine($"[ArchProbe] exact: {rows.Count} rows ({geoms.Count} shells) -> {HubsPath} " +
                           $"({new FileInfo(HubsPath).Length / 1024} KB) in {(System.DateTime.Now - t0).TotalSeconds:0} s");
            log.AppendLine($"exact minus field, straight wheels ({deltas.Count}): min {Cm(Pct(0))} p10 {Cm(Pct(0.1f))} median {Cm(Pct(0.5f))} p90 {Cm(Pct(0.9f))} max {Cm(Pct(1f))}");
            foreach (var kv in perShell)
                log.AppendLine($"  {kv.Key,-18} exact - field {Cm(kv.Value.lo)} .. {Cm(kv.Value.hi)} ({kv.Value.n} wheels)");
            File.WriteAllText("PSXRacing_archexact.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>A body vertex in the shell frame CarModelLibrary.ShellFit.P
        /// maps from: the model's yaw and the baked lift and slide.</summary>
        public static Vector3 ShellFrame(CarModelDef def, Vector3 meshV) =>
            Quaternion.Euler(0f, def.bodyYaw, 0f) * meshV + new Vector3(0f, def.bodyYOffset, def.bodyZOffset);

        /// <summary>The model's own tyre at an axle: radius as modelled (not
        /// the 0.93 the rig draws) and section width.</summary>
        static void OwnTyre(CarModelDef def, int axle, out float r, out float w)
        {
            float scale = Mathf.Max(def.wheelMeshScale, 0.01f);
            float rf = def.wheelRadius / scale;
            float wf = def.wheelMesh != null ? def.wheelMesh.bounds.size.x : 0.2f;
            r = axle == 0 ? rf : (def.tyreRadiusRear > 0.05f ? def.tyreRadiusRear : rf);
            w = axle == 0 ? wf : (def.tyreWidthRear > 0.05f ? def.tyreWidthRear : wf);
        }

        static ArchCeilings.Shell Measure(CarModelDef def, StringBuilder log)
        {
            var s = new ArchCeilings.Shell { key = def.key, hubY = def.wheelRadius };
            float trackR = def.trackRear > 0.1f ? def.trackRear : def.trackWidth;
            s.hubX[0] = def.trackWidth * 0.5f; s.hubZ[0] = def.wheelbase * 0.5f;
            s.hubX[1] = trackR * 0.5f;         s.hubZ[1] = -def.wheelbase * 0.5f;

            var mv = def.bodyMesh.vertices;
            var tris = def.bodyMesh.triangles;
            var v = new Vector3[mv.Length];
            for (int i = 0; i < mv.Length; i++) v[i] = ShellFrame(def, mv[i]);

            int nx = (ArchCeilings.Lanes - 1) * Sub + 1, nz = ArchCeilings.Rows;
            float dx = ArchCeilings.LaneStep / Sub, dz = ArchCeilings.RowStep;
            var line = new StringBuilder($"{def.key,-18}");
            for (int a = 0; a < 2; a++)
            {
                OwnTyre(def, a, out float ownR, out float ownW);
                // Sub-sample heights, both sides folded onto |x|.
                var h = new float[nx * nz];
                for (int k = 0; k < h.Length; k++) h[k] = float.PositiveInfinity;
                float x0 = s.hubX[a] - ArchCeilings.HalfX, z0 = s.hubZ[a] - ArchCeilings.HalfZ;
                int skipped = 0;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    Vector3 p0 = v[tris[t]], p1 = v[tris[t + 1]], p2 = v[tris[t + 2]];
                    float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));
                    if (maxY <= s.hubY) continue;
                    float det = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
                    if (Mathf.Abs(det) < 1e-10f) continue;     // edge-on to a vertical ray
                    float minZ = Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)), maxZ = Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z));
                    int k0 = Mathf.Max(0, Mathf.CeilToInt((minZ - z0) / dz)), k1 = Mathf.Min(nz - 1, Mathf.FloorToInt((maxZ - z0) / dz));
                    if (k0 > k1) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) * side, maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) * side;
                        if (side < 0) { float tmp = minX; minX = maxX; maxX = tmp; }
                        int j0 = Mathf.Max(0, Mathf.CeilToInt((minX - x0) / dx)), j1 = Mathf.Min(nx - 1, Mathf.FloorToInt((maxX - x0) / dx));
                        for (int j = j0; j <= j1; j++)
                        {
                            float ax = x0 + j * dx, x = ax * side;
                            for (int k = k0; k <= k1; k++)
                            {
                                float z = z0 + k * dz;
                                float l1 = ((p1.z - p2.z) * (x - p2.x) + (p2.x - p1.x) * (z - p2.z)) / det;
                                float l2 = ((p2.z - p0.z) * (x - p2.x) + (p0.x - p2.x) * (z - p2.z)) / det;
                                float l3 = 1f - l1 - l2;
                                if (l1 < -1e-5f || l2 < -1e-5f || l3 < -1e-5f) continue;
                                float y = l1 * p0.y + l2 * p1.y + l3 * p2.y;
                                if (y <= s.hubY) continue;
                                if (InWheel(ax, y, z, s.hubX[a], s.hubY, s.hubZ[a], ownR, ownW)) { skipped++; continue; }
                                int idx = j * nz + k;
                                if (y < h[idx]) h[idx] = y;
                            }
                        }
                    }
                }

                // Cells: the lowest of the three sub-samples across each lane.
                var cells = new byte[ArchCeilings.Lanes * ArchCeilings.Rows];
                for (int l = 0; l < ArchCeilings.Lanes; l++)
                    for (int r = 0; r < ArchCeilings.Rows; r++)
                    {
                        float lo = float.PositiveInfinity;
                        for (int j = l * Sub - 1; j <= l * Sub + 1; j++)
                            if (j >= 0 && j < nx && h[j * nz + r] < lo) lo = h[j * nz + r];
                        cells[l * ArchCeilings.Rows + r] = float.IsPositiveInfinity(lo)
                            ? ArchCeilings.Open : ArchCeilings.Encode(lo - s.hubY);
                    }
                s.cells[a] = cells;

                // The model's own gap: its own tyre (as the rig draws it, 0.93),
                // straight up from its own hub. A shell with ~0 here clips at rest.
                float gap = float.PositiveInfinity;
                float drawnR = ownR * def.wheelMeshScale;
                for (int lane = -3; lane <= 3; lane++)
                    for (float deg = -84f; deg <= 84f; deg += 3f)
                    {
                        float px = s.hubX[a] + ownW * lane / 6f, pz = s.hubZ[a] + drawnR * Mathf.Sin(deg * Mathf.Deg2Rad);
                        float py = s.hubY + drawnR * Mathf.Cos(deg * Mathf.Deg2Rad);
                        float c = ArchCeilings.Ceiling(s, a, px, pz);
                        if (c - py < gap) gap = c - py;
                    }
                line.Append($" {(a == 0 ? "F" : "R")} own gap {Cm(gap)} (skipped {skipped} in-tyre)");
            }
            log.AppendLine(line.ToString());
            return s;
        }

        /// <summary>Inside the model's own wheel, or inboard of its outer face
        /// within its radius of the axle (axle, hub, disc, driveshaft): parts
        /// that move with the wheel. |x| folded.</summary>
        static bool InWheel(float absX, float y, float z, float hubX, float hubY, float hubZ, float ownR, float ownW)
        {
            float ry = y - hubY, rz = z - hubZ;
            return absX < hubX + ownW * 0.5f - 0.005f && ry * ry + rz * rz < (ownR - 0.01f) * (ownR - 0.01f);
        }

        static string Cm(float m) => float.IsInfinity(m) ? "  open" : (m * 100f).ToString("+0.0;-0.0").PadLeft(6) + "cm";

        /// <summary>The hub's height at stock ride, car frame, axle by axle:
        /// mount - rest length + the preloaded static sag (CarController's
        /// SuspensionAndLoads), with the sheet's rake and any tyre stagger
        /// split between the axles.</summary>
        public static float StockHubRest(CarModelLibrary.ShellFit f, CarSpec spec, int axle)
        {
            float sagSplit = CarController.RideRakeOf(spec) + (f.radiusF - f.radiusR);
            return CarController.DefaultMountHeight - CarController.DefaultRestLength +
                   CarController.StaticSagRef + (axle == 0 ? 0.5f : -0.5f) * sagSplit;
        }

        [MenuItem("Tools/PSX Racing/Report Arch Room")]
        public static void Report()
        {
            ArchCeilings.ClearCache();
            var sb = new StringBuilder();
            sb.AppendLine("=== ARCH ROOM: how far each axle may rise from STOCK ride before the tyre meets the body ===");
            sb.AppendLine("room = least HubMax of the axle's two wheels (as the car draws them: CarBody's fit) - stock rest hub;" +
                          " stage 4 drops " + (CarTune.RideDropAtStage(4) * 100f).ToString("0.0") + " cm more;" +
                          " was = the same before the body sat at its fitted height (hub " +
                          CarController.StockHubRest.ToString("0.000") + " m vs the mean tyre radius)");
            var rooms = new List<float>();
            var rows = new List<(float room, string text)>();
            int noField = 0;
            foreach (var spec in CarCatalog.All)
            {
                var def = CarModelLibrary.LoadFor(spec);
                if (def == null) continue;
                if (ArchCeilings.Get(def.key) == null) { noField++; continue; }
                var f0 = CarModelLibrary.Fit(def, spec);
                var f = CarBody.Lifted(f0);
                float least = float.PositiveInfinity;
                var line = new StringBuilder();
                for (int axle = 0; axle < 2; axle++)
                {
                    float hub = float.PositiveInfinity, was = float.PositiveInfinity;
                    for (int w = axle * 2; w < axle * 2 + 2; w++)
                    {
                        hub = Mathf.Min(hub, ArchCeilings.HubMax(def, f, w));
                        was = Mathf.Min(was, ArchCeilings.HubMax(def, f0, w));
                    }
                    float room = hub - StockHubRest(f, spec, axle);
                    least = Mathf.Min(least, room);
                    rooms.Add(room);
                    line.Append($" {(axle == 0 ? "F" : "R")} {Cm(room)} (was {Cm(was - StockHubRest(f, spec, axle))})");
                }
                rows.Add((least, $"{Cm(least)} | {spec.name,-44} {def.key,-16}{line} lift {Cm(f.dy - f0.dy)} r {f.radiusF:0.000}/{f.radiusR:0.000} cam {f.camberF:0.0}/{f.camberR:0.0}"));
            }
            rows.Sort((a, b) => a.room.CompareTo(b.room));
            rooms.Sort();
            float Pct(float p) => rooms.Count == 0 ? 0f : rooms[Mathf.Clamp(Mathf.RoundToInt(p * (rooms.Count - 1)), 0, rooms.Count - 1)];
            sb.AppendLine($"axles {rooms.Count} (no field: {noField} cars): min {Cm(Pct(0))} p10 {Cm(Pct(0.1f))} median {Cm(Pct(0.5f))} p90 {Cm(Pct(0.9f))}");
            int under0 = 0, under3 = 0, under5 = 0;
            foreach (float r in rooms) { if (r < 0f) under0++; if (r < 0.03f) under3++; if (r < 0.05f) under5++; }
            sb.AppendLine($"axles under 0 cm (tyre in the body at rest): {under0}; under 3 cm: {under3}; under 5 cm: {under5}");
            sb.AppendLine("--- every car, least room first ---");
            foreach (var r in rows) sb.AppendLine(r.text);

            sb.AppendLine("--- shells at their own fit (no sheet: traffic, props, starter) ---");
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def == null || ArchCeilings.Get(def.key) == null) continue;
                var f = CarBody.Lifted(CarModelLibrary.Fit(def, default(CarModelLibrary.SpecGeometry)));
                sb.AppendLine($"{m.key,-18} F {Cm(ArchCeilings.HubMax(def, f, 0) - StockHubRest(f, null, 0))}" +
                              $" R {Cm(ArchCeilings.HubMax(def, f, 2) - StockHubRest(f, null, 1))}");
            }
            File.WriteAllText("PSXRacing_archroom.txt", sb.ToString());
            Debug.Log("[ArchProbe] report -> PSXRacing_archroom.txt");
        }

        // ------------------------------------------------------------------
        //  THE INDEPENDENT CHECK: drawn tyre vertices against the body mesh
        // ------------------------------------------------------------------

        /// <summary>
        /// How far the DRAWN tyres of a live car reach up through its body, as
        /// rendered right now. The tyre is the wheel mesh's vertices AND samples
        /// over its faces (1 cm), through its own spin, holder and hub
        /// transforms; the body goes through its own (lean and seat included);
        /// both are carried into the shell frame. No field, no fit: the
        /// triangles themselves.
        ///
        /// A point clips when some bodywork lies between the model's hub height
        /// and it, straight above it - the field's own definition. So "0 points
        /// over 1 mm" means no tyre shows through.
        ///
        /// Vertices alone were not enough: on a low-poly tread a face can pass
        /// through an arch's lip between two vertices (the old-pack hatch's
        /// rear corner did, and read 0 mm).
        ///
        /// Returns the deepest reach (m, 0 = clear) and counts points over 1 mm.
        /// </summary>
        public static float DrawnClip(CarBody body, out int over1mm, out int worstWheel)
        {
            over1mm = 0; worstWheel = -1;
            var def = body != null ? body.Def : null;
            if (def == null || body.bodyFilter == null || def.bodyMesh == null || def.wheelMesh == null) return 0f;
            if (!clipGeoms.TryGetValue(def, out var g)) clipGeoms[def] = g = Geometry(def);
            var bodyT = body.bodyFilter.transform;
            float worst = 0f;
            for (int w = 0; w < 4; w++)
            {
                var mf = body.wheelFilters[w];
                if (mf == null || mf.sharedMesh == null) continue;
                int axle = w < 2 ? 0 : 1;
                foreach (var local in TyreSamples(mf.sharedMesh))
                {
                    Vector3 pt = ShellFrame(def, bodyT.InverseTransformPoint(mf.transform.TransformPoint(local)));
                    if (pt.y <= g.hubY) continue;
                    float ceil = ExactCeiling(g, axle, pt.x, pt.z);
                    if (ceil >= pt.y) continue;
                    float depth = pt.y - ceil;
                    if (depth > 0.001f) over1mm++;
                    if (depth > worst) { worst = depth; worstWheel = w; }
                }
            }
            return worst;
        }

        static readonly Dictionary<CarModelDef, ShellGeom> clipGeoms = new Dictionary<CarModelDef, ShellGeom>();
        static readonly Dictionary<Mesh, Vector3[]> tyreSamples = new Dictionary<Mesh, Vector3[]>();

        /// <summary>A wheel mesh's vertices plus points over its faces, 1 cm apart.</summary>
        static Vector3[] TyreSamples(Mesh m)
        {
            if (tyreSamples.TryGetValue(m, out var pts)) return pts;
            var list = new List<Vector3>(m.vertices);
            var wv = m.vertices;
            var wt = m.triangles;
            for (int t = 0; t + 2 < wt.Length; t += 3)
            {
                Vector3 a = wv[wt[t]], c = wv[wt[t + 1]], d = wv[wt[t + 2]];
                float edge = Mathf.Max((a - c).magnitude, Mathf.Max((c - d).magnitude, (d - a).magnitude));
                int n = Mathf.Clamp(Mathf.CeilToInt(edge / FaceSampleM), 1, 64);
                if (n < 2) continue;
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n - i; j++)
                        list.Add(a + (c - a) * ((float)i / n) + (d - a) * ((float)j / n));
            }
            return tyreSamples[m] = list.ToArray();
        }
    }
}
