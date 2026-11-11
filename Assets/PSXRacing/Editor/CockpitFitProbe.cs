using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE COCKPIT AGAINST THE CAR'S OWN BODY, MEASURED (owner, 2026-11-11:
    /// "all cockpits should fit inside each car model, with mirrors properly
    /// mounted just outside the cockpit window"; "I can see the road ahead
    /// under my hood").
    ///
    /// Every shell in Resources/CarModels as a MeshCollider (back faces hit),
    /// his cabin placed on it as the game places it (CockpitInterior.FitFor
    /// then Fit3; PSX_COCKPIT_FIT3 / _WIDTHFIT read as in play), rays only:
    ///   PROBE (Measure): the windscreen's top on the centreline (rays down
    ///     from over the roof, back from the cowl: the first z whose top is
    ///     within 3 cm of the roof), the belt and the side glass at the seat
    ///     (rays in from the side) -> Scripts/CockpitFit.Table.cs when
    ///     PSX_COCKPITFIT_EMIT=1.
    ///   GATE (a) OUTSIDE: his cabin's vertices beyond the shell (no shell
    ///     above it, or none outboard of it), more than 1 cm.
    ///   GATE (b) INSIDE: from the eye over a grid of directions (+-80 deg
    ///     across, -35..+25 up), any part of the shell the driver sees (a
    ///     front face, not cut by CabinClip) nearer than his cabin's surface
    ///     or glass on that ray: shell standing in the cabin.
    ///   GATE (c) BONNET: of the rays through his windscreen in the ahead
    ///     view (+-40 deg, -25..+10), the share that lands on the car (its
    ///     bonnet, wings, cowl) - a sliver under 3%, a wall over 45%.
    ///   NOSE: windscreen rays whose first shell hit is a BACK face - the
    ///     driver sees into the hollow nose (world through it without the dark
    ///     inside; dark with it when the face is ahead of the cut). Run at
    ///     rest and in four forced poses, the cabin riding the body and the
    ///     eye following it a quarter (EyeFollow): nose up 3 deg about the
    ///     rear axle (the front 12-13 cm up), nose down 3, a brake dive (2 deg
    ///     down, 3 cm down), a crest (8 cm up).
    /// Writes Screenshots/DoorMirrors/fit.txt; FitBatch exits 1 on a gate
    /// failure.
    /// </summary>
    public static class CockpitFitProbe
    {
        const int ShellLayer = 30, CabinLayer = 31;
        const float Tol = 0.01f;

        public struct Probe { public float headerZ, headerY, sideX, beltY; public bool ok; }

        [MenuItem("PSX Racing/Cockpit Fit Probe")]
        public static void Menu() => Run(false);

        public static void EmitBatch()
        {
            System.Environment.SetEnvironmentVariable("PSX_COCKPITFIT_EMIT", "1");
            int fails = Run(true);
            EditorApplication.Exit(0);
        }

        /// <summary>Round 2's first job: the fit report as it was (no width
        /// fit) and as round 1 left it, then both tables re-probed.</summary>
        public static void R2Probe()
        {
            try
            {
                CockpitFit.On = false;
                System.Environment.SetEnvironmentVariable("PSX_COCKPIT_WIDTHFIT", "0");
                System.Environment.SetEnvironmentVariable("PSX_COCKPITFIT_TAG", "base");
                Run(true);
                System.Environment.SetEnvironmentVariable("PSX_COCKPIT_WIDTHFIT", "1");
                System.Environment.SetEnvironmentVariable("PSX_COCKPITFIT_TAG", "r1");
                System.Environment.SetEnvironmentVariable("PSX_COCKPITFIT_EMIT", "1");
                Run(true);
                CockpitFit.On = true;
            }
            catch (System.Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(0);
        }

        public static void FitBatch()
        {
            int fails = Run(true);
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static List<CarModelDef> Defs()
        {
            var defs = new List<CarModelDef>();
            foreach (var go in Resources.LoadAll<GameObject>("CarModels"))
            {
                var d = go != null ? go.GetComponent<CarModelDef>() : null;
                if (d != null && d.bodyMesh != null) defs.Add(d);
            }
            defs.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            return defs;
        }

        public static int Run(bool batch)
        {
            if (!CockpitModel.Ready) CockpitModel.Request();
            var parts = CockpitModel.Parts;
            var L = CockpitModel.L;
            var log = new StringBuilder();
            if (parts == null || L == null) { Debug.LogError("[CockpitFit] no cockpit model"); return 1; }
            if (batch) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool keepBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;

            // His cabin as colliders (cabin, glass panes), in the model frame under a root we pose per car.
            var cabRoot = new GameObject("FitCabin").transform;
            var glassParts = new HashSet<string>();
            foreach (var g in L.glass) glassParts.Add(g.part);
            var cabMeshes = new List<(Mesh, bool)>();
            foreach (var kv in parts)
            {
                if (kv.Value.mesh == null) continue;
                bool glass = glassParts.Contains(kv.Key);
                bool body = kv.Key == "cabin" || glass;
                if (!body) continue;
                var go = new GameObject(kv.Key) { layer = CabinLayer };
                go.transform.SetParent(cabRoot, false);
                go.AddComponent<MeshCollider>().sharedMesh = kv.Value.mesh;
                cabMeshes.Add((kv.Value.mesh, glass));
            }
            var shellGo = new GameObject("FitShell") { layer = ShellLayer };
            var shellCol = shellGo.AddComponent<MeshCollider>();
            Mesh shellMesh = null;

            var rows = new StringBuilder();
            var rawZ = new List<float>(); var rawY = new List<float>();
            int fails = 0;
            var worst = new Dictionary<string, (float, string)>();
            void Worst(string what, float v, string key) { if (!worst.TryGetValue(what, out var w) || v > w.Item1) worst[what] = (v, key); }
            log.Append("shell                fit3 sx    sy    sz   | eye z/y vs cowl     | hdr dz/dy   belt dy  side dx | (a) out  worst cm | (b) in  worst cm | (c) bonnet% | nose% rest/up/down/dive/crest (open after dark inside)\n");
            foreach (var d in Defs())
            {
                shellGo.transform.SetPositionAndRotation(new Vector3(0f, d.bodyYOffset, d.bodyZOffset), Quaternion.Euler(0f, d.bodyYaw, 0f));
                shellCol.sharedMesh = null;
                if (shellMesh != null) Object.DestroyImmediate(shellMesh);
                shellMesh = DoubleSided(d.bodyMesh, out shellTris);
                shellCol.sharedMesh = shellMesh;
                Physics.SyncTransforms();
                var pr = Measure(d);
                {
                    var f00 = CockpitInterior.FitFor(d.colliderCenter, d.colliderSize, d);
                    if (pr.ok && f00.measured && CockpitFit.Pane(out var qb, out var qt))
                    {
                        rawZ.Add((d.cowlZ - pr.headerZ) / Mathf.Max(0.05f, qb.z - qt.z));
                        rawY.Add((pr.headerY - CockpitFit.Inset - d.cowlY) / Mathf.Max(0.05f, qt.y - qb.y));
                    }
                }
                if (pr.ok)
                    rows.Append("            new Row(\"").Append(d.key).Append("\", ")
                        .Append(F(pr.headerZ)).Append(", ").Append(F(pr.headerY)).Append(", ").Append(F(pr.sideX)).Append(", ").Append(F(pr.beltY)).Append("),\n");

                // The fit as play makes it (catalog frame: no fit scale, no lift, LHD).
                var f = CockpitInterior.FitFor(d.colliderCenter, d.colliderSize, d);
                f = CockpitInterior.Fit3(f, d, 1f, 0f, 1f);
                float sx = f.scale * f.widthFit, sy = f.ScaleY, sz = f.scale;
                var ls = new Vector3(sx, sy, sz);
                // model -> car: eye + Scale(ls, m - L.eye)
                Matrix4x4 M = Matrix4x4.TRS(f.eye, Quaternion.identity, Vector3.one) * Matrix4x4.Scale(ls) * Matrix4x4.Translate(-L.eye);
                cabRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                cabRoot.localScale = Vector3.one;
                // colliders cannot take a non-uniform parent scale reliably: bake per car
                foreach (Transform c in cabRoot)
                {
                    var mc = c.GetComponent<MeshCollider>();
                    var src = parts[c.name].mesh;
                    var v = src.vertices;
                    for (int i = 0; i < v.Length; i++) v[i] = M.MultiplyPoint3x4(v[i]);
                    var m = mc.sharedMesh != null && mc.sharedMesh.name == "fit_" + c.name ? mc.sharedMesh : new Mesh { name = "fit_" + c.name };
                    m.Clear();
                    m.indexFormat = src.indexFormat;
                    m.vertices = v;
                    var st = src.triangles; var dt = new int[st.Length * 2];
                    for (int i = 0; i < st.Length; i += 3) { dt[i] = st[i]; dt[i + 1] = st[i + 1]; dt[i + 2] = st[i + 2]; dt[st.Length + i] = st[i]; dt[st.Length + i + 1] = st[i + 2]; dt[st.Length + i + 2] = st[i + 1]; }
                    m.triangles = dt;
                    mc.sharedMesh = null;
                    mc.sharedMesh = m;
                }
                Physics.SyncTransforms();

                // (a) cabin vertices outside the shell
                int outN = 0, tot = 0, beyond = 0; float outW = 0f;
                foreach (Transform c in cabRoot)
                {
                    var vs = c.GetComponent<MeshCollider>().sharedMesh.vertices;
                    for (int i = 0; i < vs.Length; i += 2)
                    {
                        var p = vs[i];
                        if (p.y < d.cowlY - 0.35f) continue;   // the floor and footwells (the shell's floor is open)
                        if (p.z < f.eye.z - 0.3f) continue;    // behind the seat: out of the driver's view
                        tot++;
                        float o = Outside(p);
                        if (o >= 0.99f) { beyond++; continue; }   // no shell over or beside it at all
                        if (o > Tol) { outN++; outW = Mathf.Max(outW, o); }
                    }
                }

                // rays from the eye
                var cut = Cut(f, d);
                Vector3 eye = f.eye;
                Quaternion look = Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
                if (d.key == "rx7_fd_93" || d.key == "flatsix_coupe_87")
                {
                    // the probe's own trace: one ray ahead and one at the bonnet
                    foreach (float pch in new[] { 0f, -6f })
                    {
                        var dir = look * Quaternion.Euler(-pch, 0f, 0f) * Vector3.forward;
                        var sb = new StringBuilder($"  trace {d.key} pitch {pch}: cabin");
                        int nn = Physics.RaycastNonAlloc(eye, dir, buf, 6f, 1 << CabinLayer, QueryTriggerInteraction.Ignore);
                        for (int i = 0; i < nn; i++) sb.Append($" {buf[i].collider.name}@{buf[i].distance:0.00}");
                        sb.Append(" | shell");
                        Vector3 o = eye; float gone = 0f;
                        for (int st = 0; st < 12 && Physics.Raycast(o, dir, out var hh, 20f - gone, 1 << ShellLayer, QueryTriggerInteraction.Ignore); st++)
                        {
                            gone += hh.distance + 0.001f; o = hh.point + dir * 0.001f;
                            sb.Append($" {(hh.triangleIndex < shellTris ? "F" : "B")}@{gone:0.00}{(InCut(hh.point, cut) ? "c" : "")}");
                        }
                        var rr = Classify(eye, dir, cut);
                        sb.Append($" => ws {rr.windscreen} front {rr.front} back {rr.back} intrude {rr.intrude:0.00}\n");
                        log.Append(sb);
                    }
                }
                int inN = 0; float inW = 0f; string inWhere = "";
                for (float yaw = -80f; yaw <= 80f; yaw += 2f)
                    for (float pitch = -35f; pitch <= 25f; pitch += 2f)
                    {
                        var dir = look * Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                        var r = Classify(eye, dir, cut);
                        if (r.intrude > 0.005f) { inN++; if (r.intrude > inW) { inW = r.intrude; inWhere = $"yaw {yaw:0} pitch {pitch:0} at ({r.at.x:0.00},{r.at.y:0.00},{r.at.z:0.00})"; } }
                    }
                // (c) + nose at rest and forced
                var poses = new (string, float, float)[] { ("rest", 0f, 0f), ("up", -3f, 0f), ("down", 3f, 0f), ("dive", 2f, -0.03f), ("crest", 0f, 0.08f) };
                float rearZ = -d.wheelbase * 0.5f;
                string nose = ""; float bonnet = 0f; int noseOpenAll = 0;
                foreach (var (name, pitchDeg, lift) in poses)
                {
                    // the body (and the cabin on it) moves by B; the eye by a quarter of it.
                    Matrix4x4 B = Pose(pitchDeg, lift, rearZ), B4 = Pose(pitchDeg * CockpitInterior.EyeFollow, lift * CockpitInterior.EyeFollow, rearZ);
                    Matrix4x4 rel = B.inverse * B4;
                    Vector3 e2 = rel.MultiplyPoint3x4(eye);
                    int ws = 0, bon = 0, into = 0, open = 0;
                    for (float yaw = -40f; yaw <= 40f; yaw += 1.5f)
                        for (float pitch = -25f; pitch <= 10f; pitch += 1f)
                        {
                            var dir = rel.MultiplyVector(look * Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward).normalized;
                            var r = Classify(e2, dir, cut);
                            if (!r.windscreen) continue;
                            if (r.back) { into++; if (r.at.z < cut.cutZ && r.at.y < d.cowlY + 0.02f) open++; }   // open: below the bonnet line and behind the cut (no dark inside there)
                            if (dir.y >= 0f) continue;   // the bonnet share: the windscreen's view below the horizon
                            ws++;
                            if (r.front || r.back) bon++;
                        }
                    float pc = ws > 0 ? 100f * bon / ws : 0f;
                    if (name == "rest") bonnet = pc;
                    nose += $"{into}({open}) ";
                    noseOpenAll += open;
                }
                // per-axis offsets: eye vs the old fit, header and belt
                var f0 = CockpitInterior.FitFor(d.colliderCenter, d.colliderSize, d);
                CockpitFit.Pane(out var pb, out var pt);
                Vector3 hdr = M.MultiplyPoint3x4(pt);
                float hdz = pr.ok ? hdr.z - pr.headerZ : 0f, hdy = pr.ok ? hdr.y - (pr.headerY - CockpitFit.Inset) : 0f;
                float beltDy = 0f, sideDx = 0f;
                if (pr.ok)
                {
                    float sill = float.MaxValue, gx = 0f;
                    foreach (var g in L.glass)
                        if (g.key.StartsWith("door") && parts.TryGetValue(g.part, out var gp))
                        { sill = Mathf.Min(sill, gp.mesh.bounds.min.y); gx = Mathf.Max(gx, Mathf.Max(-gp.mesh.bounds.min.x, gp.mesh.bounds.max.x)); }
                    beltDy = (f.eye.y + sy * (sill - L.eye.y)) - pr.beltY;
                    sideDx = gx * sx - pr.sideX;
                }
                // THE GATE: no shell standing in the cabin (b), no view out through the nose
                // in any pose (open = 0). (a) and (c) are measured and printed, not gated:
                // his cabin is one model and the catalogue spans a van to a 911.
                // (d) THE WHEEL (2026-11-11 afternoon): round as drawn (rim radius
                // max/min - 1 under 0.5%) and both hands on its rim (grip tube
                // centre within 5 mm of the drawn rim's centre line) at rest and
                // turned 60 deg each way.
                WheelCheck(f, out float oval, out float gripGap);
                bool wheelBad = oval > WheelRoundTol || gripGap > GripTol;
                Worst("(d) wheel oval, %", oval * 100f, d.key);
                Worst("(d) grip off the drawn rim, mm", gripGap * 1000f, d.key);
                bool bad = inN > 0 || noseOpenAll > 0 || wheelBad;
                if (bad) fails++;
                Worst("(a) cabin outside the shell, cm", outW * 100f, d.key);
                Worst("(b) shell inside the cabin, cm", inW * 100f, d.key);
                Worst("header dz, cm", Mathf.Abs(hdz) * 100f, d.key);
                Worst("header dy, cm", Mathf.Abs(hdy) * 100f, d.key);
                Worst("belt dy, cm", Mathf.Abs(beltDy) * 100f, d.key);
                Worst("side glass dx (cabin - shell), cm", sideDx * 100f, d.key);
                log.Append($"{(bad ? "FAIL " : "ok   ")}{d.key,-17} {(f.fit3 ? "yes" : "no ")} {sx:0.00} {sy:0.00} {sz:0.00} | eye {f.eye.z - f.cutZ:+0.00;-0.00}/{f.eye.y - d.cowlY:+0.00;-0.00} (old {f0.eye.z - f0.cutZ:+0.00;-0.00}/{f0.eye.y - d.cowlY:+0.00;-0.00}) | " +
                           $"{hdz * 100f:+0;-0}/{hdy * 100f:+0;-0} cm {beltDy * 100f:+0;-0} cm {sideDx * 100f:+0;-0} cm | {outN}/{tot} {outW * 100f:0.0} (+{beyond} past the shell's silhouette) | {inN} {inW * 100f:0.0} {inWhere} | {bonnet:0.0} | {nose}" +
                           $"| wheel oval {oval * 100f:0.00}% grip {gripGap * 1000f:0.0} mm{(wheelBad ? " WHEEL" : "")}\n");
            }
            if (wrArms != null) wrArms.Destroy();
            if (wheelRig != null) Object.DestroyImmediate(wheelRig);
            wheelRig = null; wrArms = null;
            var head = new StringBuilder($"[CockpitFit] {Defs().Count} shells, {fails} FAIL (fit3 {(CockpitFit.On ? "on" : "off")}, width fit {(CockpitInterior.WidthFitOn ? "on" : "off")}); GATES: (b) no shell in the cabin, nose: rays into the hollow nose (open = behind the cut, where the dark inside is not drawn) = 0 open in any pose; MEASURED: (a) cabin vertices ahead of the seat beyond the shell, (c) of the windscreen view below the horizon the share on the car; old nose text: rays into the hollow nose (open = behind the cut, where the dark inside is not drawn) = 0 open in any pose\n");
            foreach (var kv in worst) head.Append($"  worst {kv.Key}: {kv.Value.Item1:0.0} ({kv.Value.Item2})\n");
            log.Insert(0, head.ToString());
            string root = Path.GetDirectoryName(Application.dataPath);
            string outDir = Path.Combine(root, "Screenshots", "DoorMirrors");
            Directory.CreateDirectory(outDir);
            string tag = System.Environment.GetEnvironmentVariable("PSX_COCKPITFIT_TAG");
            File.WriteAllText(Path.Combine(outDir, "fit" + (string.IsNullOrEmpty(tag) ? "" : "_" + tag) + ".txt"), log.ToString());
            if (System.Environment.GetEnvironmentVariable("PSX_COCKPITFIT_EMIT") == "1")
            {
                string path = Path.Combine(Application.dataPath, "PSXRacing", "Scripts", "CockpitFit.Table.cs");
                rawZ.Sort(); rawY.Sort();
                float cz = rawZ.Count > 0 ? rawZ[rawZ.Count / 2] : 1f, cy = rawY.Count > 0 ? rawY[rawY.Count / 2] : 1f;
                string calib = $"        /// <summary>The catalogue's median windscreen against his pane ({rawZ.Count} shells): his cabin's own\n" +
                               "        /// proportion, so the fit moves each car from the middle of the catalogue, not from the probe's header line.</summary>\n" +
                               $"        public const float CalibZ = {F(cz)}, CalibY = {F(cy)};\n";
                File.WriteAllText(path, Header + rows + "        };\n" + calib + "    }\n}\n");
                Debug.Log("[CockpitFit] table written to " + path);
            }
            Debug.Log(log.ToString());
            Physics.queriesHitBackfaces = keepBack;
            Object.DestroyImmediate(cabRoot.gameObject);
            Object.DestroyImmediate(shellGo);
            if (shellMesh != null) Object.DestroyImmediate(shellMesh);
            return fails;
        }

        public const float WheelRoundTol = 0.005f, GripTol = 0.005f;
        static GameObject wheelRig;
        static Transform wrModel, wrRoot, wrWheel;
        static CockpitArms wrArms;

        /// <summary>The wheel and arms as CockpitInterior.Apply places them
        /// (LHD, cabin frame = the eye), measured: the drawn rim's radius
        /// spread (max/min - 1) and the worse gripping hand's tube centre off
        /// the drawn rim's centre line, metres (-1: no arms data).</summary>
        public static void WheelCheck(CockpitInterior.Fit f, out float oval, out float gap)
        {
            var L = CockpitModel.L;
            oval = 0f; gap = -1f;
            if (L == null || L.wheel == null) return;
            if (wheelRig == null)
            {
                wheelRig = new GameObject("FitWheel");
                wrModel = new GameObject("Model").transform; wrModel.SetParent(wheelRig.transform, false);
                wrRoot = new GameObject("WheelRoot").transform; wrRoot.SetParent(wheelRig.transform, false);
                var piv = new GameObject("WheelPivot").transform; piv.SetParent(wrRoot, false); piv.localRotation = L.wheel.rot;
                wrWheel = new GameObject("SteeringWheel").transform; wrWheel.SetParent(piv, false);
                wrArms = CockpitArms.Create(wrModel, L, 0);
            }
            float sx = f.scale * f.widthFit, sy = f.ScaleY, sz = f.scale;
            wrModel.localScale = new Vector3(sx, sy, sz);
            wrModel.localPosition = -Vector3.Scale(wrModel.localScale, L.eye);
            CockpitInterior.WheelFrame(f, out var pos, out var sc, out var warp);
            wrRoot.localPosition = pos; wrRoot.localScale = sc;
            if (wrArms == null) return;
            wrArms.WheelWarp = warp;
            gap = 0f;
            foreach (float deg in new[] { 0f, 60f, -60f })
            {
                wrWheel.localRotation = Quaternion.Euler(0f, 0f, deg);
                if (deg == 0f)
                {
                    Vector3 c = wrWheel.TransformPoint(new Vector3(0f, 0f, wrArms.RimLocal(0f).z));
                    float rmin = float.MaxValue, rmax = 0f;
                    for (int a = 0; a < 360; a += 3) { float r = (wrWheel.TransformPoint(wrArms.RimLocal(a)) - c).magnitude; rmin = Mathf.Min(rmin, r); rmax = Mathf.Max(rmax, r); }
                    oval = rmax / Mathf.Max(1e-5f, rmin) - 1f;
                }
                wrArms.Plan.Reset(deg);
                wrArms.Drive(0f, deg, false, false);
                foreach (bool left in new[] { true, false })
                {
                    if ((left ? wrArms.Plan.L : wrArms.Plan.R).mode != CockpitArms.Mode.Grip) continue;
                    Vector3 g = wrModel.TransformPoint(wrArms.GripTube(left));
                    float best = float.MaxValue;
                    for (int a = 0; a < 360; a++) best = Mathf.Min(best, (wrWheel.TransformPoint(wrArms.RimLocal(a)) - g).magnitude);
                    gap = Mathf.Max(gap, best);
                }
            }
        }

        static Matrix4x4 Pose(float pitchDeg, float lift, float pivotZ)
        {
            var piv = new Vector3(0f, 0f, pivotZ);
            return Matrix4x4.Translate(Vector3.up * lift) * Matrix4x4.Translate(piv) * Matrix4x4.Rotate(Quaternion.Euler(pitchDeg, 0f, 0f)) * Matrix4x4.Translate(-piv);
        }

        static string F(float x) => x.ToString("0.0000", CultureInfo.InvariantCulture) + "f";

        const string Header =
"// <auto-generated>\n" +
"// Written by PSX Racing/Cockpit Fit Probe (Editor/CockpitFitProbe.cs) from the\n" +
"// body meshes. Do not edit by hand: re-run the probe after a shell is\n" +
"// re-baked or added. See CockpitFit.cs.\n" +
"// </auto-generated>\n" +
"namespace PSXRacing\n" +
"{\n" +
"    public static partial class CockpitFit\n" +
"    {\n" +
"        static readonly Row[] Table =\n" +
"        {\n";
        const string Footer =
"        };\n" +
"    }\n" +
"}\n";

        static bool Hit(Vector3 o, Vector3 dir, float len, out RaycastHit h) =>
            Physics.Raycast(o, dir, out h, len, 1 << ShellLayer, QueryTriggerInteraction.Ignore);

        /// <summary>The windscreen's top, the belt and the side glass, body frame.</summary>
        public static Probe Measure(CarModelDef d)
        {
            var p = new Probe();
            float top(float z, float x) => Hit(new Vector3(x, 4f, z), Vector3.down, 6f, out var h) ? h.point.y : float.NaN;
            float roof = float.MinValue;
            for (float z = d.cowlZ; z > d.cowlZ - 2.2f; z -= 0.01f) { float y = top(z, 0f); if (!float.IsNaN(y)) roof = Mathf.Max(roof, y); }
            if (roof == float.MinValue) return p;
            for (float z = d.cowlZ; z > d.cowlZ - 2.2f; z -= 0.005f)
            {
                float y = top(z, 0f);
                if (!float.IsNaN(y) && y >= roof - 0.03f) { p.headerZ = z; p.headerY = y; break; }
            }
            if (p.headerZ == 0f) return p;
            // at the seat (a metre behind the cowl, or mid-glass when the screen is short)
            float zs = Mathf.Min(d.cowlZ - 1.0f, (d.cowlZ + p.headerZ) * 0.5f - 0.2f);
            float half(float y)
            {
                float best = float.NaN;
                for (float dz = -0.1f; dz <= 0.1f; dz += 0.05f)
                    if (Hit(new Vector3(-3f, y, zs + dz), Vector3.right, 3f, out var h)) best = float.IsNaN(best) ? -h.point.x : Mathf.Max(best, -h.point.x);
                return best;
            }
            float maxBelow = 0f;
            p.beltY = float.NaN;
            for (float y = d.cowlY - 0.25f; y < roof; y += 0.01f)
            {
                float w = half(y);
                if (float.IsNaN(w)) continue;
                if (w > maxBelow - 0.001f && float.IsNaN(p.beltY)) { maxBelow = Mathf.Max(maxBelow, w); continue; }
                if (float.IsNaN(p.beltY) && w < maxBelow - 0.03f) p.beltY = y;
            }
            if (float.IsNaN(p.beltY)) p.beltY = d.cowlY;
            p.sideX = half(p.beltY + 0.12f);
            if (float.IsNaN(p.sideX)) p.sideX = maxBelow;
            p.ok = true;
            return p;
        }

        /// <summary>How far a point is outside the shell: no shell above it,
        /// or none outboard of it (from the point, back faces hit).</summary>
        static float Outside(Vector3 p)
        {
            float o = 0f;
            float side = p.x < 0f ? -1f : 1f;
            // up
            if (!Hit(p - Vector3.up * Tol, Vector3.up, 4f, out _))
            {
                // how far above the shell's top here
                if (Hit(p + Vector3.up * 4f, Vector3.down, 8f, out var h)) o = Mathf.Max(o, p.y - h.point.y);
                else o = Mathf.Max(o, 1f);
            }
            if (!Hit(p - Vector3.right * side * Tol, Vector3.right * side, 4f, out _))
            {
                if (Hit(p + Vector3.right * side * 4f, -Vector3.right * side, 8f, out var h)) o = Mathf.Max(o, Mathf.Abs(p.x) - Mathf.Abs(h.point.x));
                else o = Mathf.Max(o, 1f);
            }
            return o;
        }

        public struct CutBox { public float cutZ, halfW, top, tailZ; }

        static CutBox Cut(CockpitInterior.Fit f, CarModelDef d)
        {
            // CabinRoom as Apply makes it, from the model's own glass and cabin bounds.
            var parts = CockpitModel.Parts; var L = CockpitModel.L;
            float glassX = CockpitInterior.AuthoredGlassX;
            float tail = f.eye.z - 1.5f;
            if (parts.TryGetValue("cabin", out var cp)) tail = f.eye.z + f.scale * (cp.mesh.bounds.min.z - L.eye.z);
            return new CutBox
            {
                cutZ = f.cutZ, halfW = glassX * f.scale * f.widthFit + CockpitInterior.CabinCutMargin,
                top = d.colliderCenter.y + d.colliderSize.y * 0.5f + 0.8f, tailZ = tail,
            };
        }

        static bool InCut(Vector3 p, CutBox c) => p.z < c.cutZ && p.z > c.tailZ && Mathf.Abs(p.x) < c.halfW && p.y < c.top;

        public struct Ray { public float intrude; public bool windscreen, front, back; public Vector3 at; }

        static readonly RaycastHit[] buf = new RaycastHit[64];

        static int shellTris;

        /// <summary>The mesh with every triangle twice, the second reversed:
        /// rays from inside hit it (MeshCollider skips back faces); a hit
        /// index at or over `orig` is a back face of the original.</summary>
        static Mesh DoubleSided(Mesh src, out int orig)
        {
            var st = src.triangles; orig = st.Length / 3;
            var dt = new int[st.Length * 2];
            for (int i = 0; i < st.Length; i += 3) { dt[i] = st[i]; dt[i + 1] = st[i + 1]; dt[i + 2] = st[i + 2]; dt[st.Length + i] = st[i]; dt[st.Length + i + 1] = st[i + 2]; dt[st.Length + i + 2] = st[i + 1]; }
            var m = new Mesh { name = "fit_shell", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = src.vertices;
            m.triangles = dt;
            return m;
        }

        static Ray Classify(Vector3 eye, Vector3 dir, CutBox cut)
        {
            var r = new Ray();
            // his cabin: nearest hit, glass or not
            float dc = float.MaxValue; bool glass = false; string what = null;
            int n = Physics.RaycastNonAlloc(eye, dir, buf, 6f, 1 << CabinLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (buf[i].distance < dc) { dc = buf[i].distance; what = buf[i].collider.name; }
            var L = CockpitModel.L;
            if (what != null) foreach (var g in L.glass) if (g.part == what) { glass = true; r.windscreen = g.key == "windshield"; }
            if (what == null) { glass = true; r.windscreen = true; }   // no cabin on this ray: an opening (the slot over the dash), not a wall
            // the shell: every face along the ray in turn (a cast reports one
            // hit per collider, so march on past each), the nearest not cut away
            Vector3 o = eye; float gone = 0f;
            for (int step = 0; step < 24; step++)
            {
                if (!Physics.Raycast(o, dir, out var h0, 20f - gone, 1 << ShellLayer, QueryTriggerInteraction.Ignore)) break;
                gone += h0.distance + 0.001f;
                o = h0.point + dir * 0.001f;
                var h = h0; h.distance = gone;
                if (InCut(h.point, cut)) continue;
                bool front = h.triangleIndex < shellTris;   // the duplicate (reversed) half is the original's back face
                if (h.distance < dc - 0.005f && front && what != null) { r.intrude = dc - h.distance; r.at = h.point; return r; }
                if (h.distance < dc - 0.005f && !front) continue;   // a back face inside the cabin is not drawn (no inside there)
                if (!glass) return r;                               // behind his opaque cabin
                r.front = front; r.back = !front; r.at = h.point;
                return r;
            }
            return r;
        }
    }
}
