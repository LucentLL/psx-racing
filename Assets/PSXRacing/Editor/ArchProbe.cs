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
        /// rendered right now - wheel mesh vertices through their own spin,
        /// holder and hub transforms, the body through its own (lean included),
        /// both carried into the shell frame. No field, no fit: the triangles
        /// themselves. A vertex clips when some bodywork lies between the
        /// model's hub height and it, straight above it - the field's own
        /// definition, so "0 vertices over 1 mm" means no tyre shows through.
        /// Returns the deepest reach (m, 0 = clear) and counts vertices over 1 mm.
        /// </summary>
        public static float DrawnClip(CarBody body, out int over1mm, out int worstWheel)
        {
            over1mm = 0; worstWheel = -1;
            var def = body != null ? body.Def : null;
            if (def == null || body.bodyFilter == null) return 0f;
            var bodyT = body.bodyFilter.transform;
            var mv = def.bodyMesh.vertices;
            var tris = def.bodyMesh.triangles;
            var v = new Vector3[mv.Length];
            for (int i = 0; i < mv.Length; i++) v[i] = ShellFrame(def, mv[i]);
            float hubY = def.wheelRadius;
            float trackR = def.trackRear > 0.1f ? def.trackRear : def.trackWidth;
            float worst = 0f;
            for (int w = 0; w < 4; w++)
            {
                var mf = body.wheelFilters[w];
                if (mf == null || mf.sharedMesh == null) continue;
                int axle = w < 2 ? 0 : 1;
                OwnTyre(def, axle, out float ownR, out float ownW);
                float hx = (axle == 0 ? def.trackWidth : trackR) * 0.5f, hz = (axle == 0 ? 0.5f : -0.5f) * def.wheelbase;
                var wv = mf.sharedMesh.vertices;
                for (int i = 0; i < wv.Length; i++)
                {
                    Vector3 world = mf.transform.TransformPoint(wv[i]);
                    Vector3 p = ShellFrame(def, bodyT.InverseTransformPoint(world));
                    if (p.y <= hubY) continue;
                    float lowest = float.PositiveInfinity;
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        Vector3 p0 = v[tris[t]], p1 = v[tris[t + 1]], p2 = v[tris[t + 2]];
                        if (Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)) >= p.y) continue;
                        if (p.x < Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) || p.x > Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))) continue;
                        if (p.z < Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z)) || p.z > Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z))) continue;
                        float det = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
                        if (Mathf.Abs(det) < 1e-10f) continue;
                        float l1 = ((p1.z - p2.z) * (p.x - p2.x) + (p2.x - p1.x) * (p.z - p2.z)) / det;
                        float l2 = ((p2.z - p0.z) * (p.x - p2.x) + (p0.x - p2.x) * (p.z - p2.z)) / det;
                        float l3 = 1f - l1 - l2;
                        if (l1 < -1e-5f || l2 < -1e-5f || l3 < -1e-5f) continue;
                        float y = l1 * p0.y + l2 * p1.y + l3 * p2.y;
                        if (y <= hubY || y >= p.y) continue;
                        if (InWheel(Mathf.Abs(p.x), y, p.z, hx, hubY, hz, ownR, ownW)) continue;
                        if (y < lowest) lowest = y;
                    }
                    if (float.IsPositiveInfinity(lowest)) continue;
                    float depth = p.y - lowest;
                    if (depth > 0.001f) over1mm++;
                    if (depth > worst) { worst = depth; worstWheel = w; }
                }
            }
            return worst;
        }
    }
}
