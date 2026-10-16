using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE DRIVER'S ARMS, tested and photographed (tools/cockpit/cockpit-arms.ps1,
    /// ONE Unity job on an already-built sandbox):
    ///   1. the self-test lines (<see cref="SelfTest"/>, also in the LifeSim
    ///      self-test): the arm loads with its poses, the left mirrors the
    ///      right, small inputs ride, a big turn crosses, a fast unwind opens
    ///      the hands, a manual shift and the handbrake take the inboard hand
    ///      away and bring it back, RHD mirrors the reach;
    ///   2. eight frames (Screenshots/cockpitarms_*.png): rest grip LHD + RHD,
    ///      three frames of hand over hand (crossing, open palm, regrip), a
    ///      shift (the hand gone), the handbrake (RHD), a look-over. The car
    ///      is the venue's player car; the steering is a SCRIPTED SWEEP fed
    ///      through CockpitInterior.SetSteer/Tick at 60 Hz (edit mode, no
    ///      frames), and the shift and handbrake go through the real inputs
    ///      (CarController.ShiftTo in manual mode, handbrakeInput);
    ///   3. the cockpit bundle rebuilt for WebGL (CockpitBundle.Build), its
    ///      size before and after.
    /// Writes PSXRacing_cockpitarms.txt beside the project.
    /// </summary>
    public static class CockpitArmsShots
    {
        const float Dt = 1f / 60f;
        static StringBuilder log;
        static int fails;

        public static void Run()
        {
            log = new StringBuilder();
            fails = 0;
            try
            {
                SelfTest((ok, what, got) =>
                {
                    if (!ok) fails++;
                    log.AppendLine((ok ? "  ok   " : "  FAIL ") + what + (got != null ? "  (got " + got + ")" : ""));
                });
                log.AppendLine(fails == 0 ? "ARMS SELF-TEST OK" : "ARMS SELF-TEST FAILED (" + fails + ")");
                try { Shots(); }
                catch (System.Exception e) { log.AppendLine("SHOTS THREW " + e); }
                string bundle = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", CockpitModel.BundleFile);
                long before = System.IO.File.Exists(bundle) ? new System.IO.FileInfo(bundle).Length : -1;
                string err = CockpitBundle.Build(BuildTarget.WebGL, out long after);
                log.AppendLine(err == null ? $"[Arms] cockpit.bundle {before} -> {after} bytes ({(after - before) / 1024f:+0.0;-0.0} KB)"
                                           : "[Arms] cockpit.bundle FAILED: " + err);
            }
            catch (System.Exception e) { log.AppendLine("RUN THREW " + e); }
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../PSXRacing_cockpitarms.txt"), log.ToString());
            Debug.Log("[Arms]\n" + log);
        }

        // ------------------------------------------------------------------
        //  The self-test
        // ------------------------------------------------------------------

        static void Sim(CockpitArms.Planner p, float seconds, System.Func<float, float> theta, System.Action<float> each = null,
                        System.Func<float, bool> shift = null, System.Func<float, bool> brake = null)
        {
            int n = Mathf.RoundToInt(seconds / Dt);
            for (int i = 1; i <= n; i++)
            {
                float t = i * Dt;
                p.Step(Dt, theta(t), shift != null && shift(t), brake != null && brake(t));
                each?.Invoke(t);
            }
        }

        public static void SelfTest(System.Action<bool, string, object> Check)
        {
            var rig = CockpitArms.Data;
            bool mirrored = rig != null;
            if (rig != null)
            {
                var r = rig.right.pos; var l = rig.left.pos;
                for (int i = 0; i < r.Length && mirrored; i++)
                    mirrored &= Mathf.Abs(l[i].x + r[i].x) < 1e-5f && l[i].y == r[i].y && l[i].z == r[i].z;
            }
            Check(rig != null && rig.right.shapeNames.Length == 2 && rig.right.shapeNames[0] == "open"
                  && rig.right.shapeNames[1] == "relaxed" && rig.tris >= 400 && rig.tris <= 1500 && rig.upperLen > 0.2f,
                  "cockpit arms: the HandsPack arm (hand to shoulder) loads with its OPEN and RELAXED poses over the grip",
                  rig != null ? rig.tris + " tris/arm, " + rig.verts + " verts, upper " + rig.upperLen.ToString("0.00") + " m" : "no arms_geo");
            Check(mirrored, "cockpit arms: the left arm is the right one mirrored (x -> -x)", null);

            // Small corrections ride the rim: no crossing, no letting go.
            var p = new CockpitArms.Planner();
            bool held = true;
            Sim(p, 4f, t => 30f * Mathf.Sin(t * Mathf.PI), t =>
                held &= p.L.mode == CockpitArms.Mode.Grip && p.R.mode == CockpitArms.Mode.Grip);
            Check(held && p.Crossings == 0 && p.L.open < 0.05f,
                  "cockpit arms: +/-30 degree corrections - both hands stay gripped and ride the rim", "crossings " + p.Crossings);

            // A big right turn (1.25 turns at 300 deg/s): hand over hand.
            p.Reset(0f);
            bool crossedOver = false; float openMax = 0f; int returns = 0; bool wasReturn = false;
            Sim(p, 2.0f, t => -Mathf.Min(450f, 300f * t), t =>
            {
                crossedOver |= p.R.crossed && p.R.mode == CockpitArms.Mode.Move;
                bool ret = p.L.mode == CockpitArms.Mode.Move && p.L.kind == CockpitArms.Kind.Return;
                if (ret) openMax = Mathf.Max(openMax, p.L.open);
                if (ret && !wasReturn) returns++;
                wasReturn = ret;
            });
            int crossings = p.Crossings;
            Check(crossings >= 2 && crossedOver && returns >= 2 && openMax > 0.8f,
                  "cockpit arms: a 1.25-turn right turn goes hand over hand (the right hand crosses over, the left lets go OPEN)",
                  crossings + " crossings, " + returns + " open returns, open " + openMax.ToString("0.00"));

            // A fast unwind (back to centre in 0.6 s): open hands, no crossing, re-grip.
            bool unwound = false; float openBoth = 0f;
            Sim(p, 1.6f, t => Mathf.Min(0f, -450f + 750f * t), t =>
            {
                unwound |= p.Unwinding;
                if (p.Unwinding) openBoth = Mathf.Max(openBoth, Mathf.Min(p.L.open, p.R.open));
            });
            float offL = Mathf.Abs(Mathf.DeltaAngle(p.L.alpha, CockpitArms.Planner.RestAlpha(true)));
            float offR = Mathf.Abs(Mathf.DeltaAngle(p.R.alpha, CockpitArms.Planner.RestAlpha(false)));
            Check(unwound && openBoth > 0.8f && p.Crossings == crossings && p.L.mode == CockpitArms.Mode.Grip
                  && p.R.mode == CockpitArms.Mode.Grip && offL <= CockpitArms.Planner.HomeSlack + 1f && offR <= CockpitArms.Planner.HomeSlack + 1f,
                  "cockpit arms: a fast unwind spins through OPEN hands (no crossing), which re-grip at rest",
                  $"open {openBoth:0.00}, crossings {p.Crossings - crossings}, rest off L {offL:0} R {offR:0} deg");

            // A manual shift: the inboard hand away ~1 s, the other holds.
            p.Reset(0f);
            Sim(p, 0.2f, t => 0f);
            float away = 0f; bool otherHeld = true;
            Sim(p, 2f, t => 0f, t =>
            {
                if (p.R.mode == CockpitArms.Mode.Away) away += Dt;
                otherHeld &= p.L.mode == CockpitArms.Mode.Grip;
            }, t => t < Dt * 0.5f + Dt);
            Check(p.Reaches == 1 && away >= 0.85f && away <= 1.25f && otherHeld && p.R.mode == CockpitArms.Mode.Grip,
                  "cockpit arms: a manual shift takes the INBOARD hand off the rim for ~1 s; the other hand holds",
                  $"away {away:0.00} s, reaches {p.Reaches}");

            // The handbrake: away while held, back after.
            p.Reset(0f);
            float awayB = 0f; bool backAfter = false;
            Sim(p, 2.5f, t => 0f, t =>
            {
                if (p.R.mode == CockpitArms.Mode.Away) awayB += Dt;
                if (t > 2.0f) backAfter |= p.R.mode == CockpitArms.Mode.Grip;
            }, null, t => t < 1.5f);
            Check(awayB >= 1.6f && awayB <= 2.0f && backAfter,
                  "cockpit arms: the handbrake keeps the inboard hand on the lever while held, then back on the rim",
                  $"away {awayB:0.00} s of a 1.5 s pull");

            // RHD: the same arms under the mirrored model - the reach hand is
            // on the driver's LEFT (the console side).
            var lay = CockpitInterior.L;
            if (lay == null || rig == null) { Check(false, "cockpit arms: RHD mirror (no layout or arms)", null); return; }
            string got = "";
            bool ok = true;
            foreach (bool rhd in new[] { false, true })
            {
                var model = new GameObject("ArmsTestModel") { hideFlags = HideFlags.HideAndDontSave };
                model.transform.localScale = new Vector3(rhd ? -1f : 1f, 1f, 1f);
                var arms = CockpitArms.Create(model.transform, lay, 0);
                if (arms == null) { ok = false; got += " no arms"; Object.DestroyImmediate(model); continue; }
                arms.Drive(0f, 0f, false, false);
                Vector3 eye = model.transform.TransformPoint(lay.eye);
                arms.Probe(true, out var gl, out _, out var fl);
                arms.Probe(false, out var gr, out _, out var fr);
                float lx = fl.position.x - eye.x, rx = fr.position.x - eye.x;
                for (int i = 0; i < 40; i++) arms.Drive(Dt, 0f, i == 0, false);
                arms.Probe(false, out _, out _, out var fa);
                float awayX = fa.position.x - eye.x;
                bool inboardLeft = rhd ? awayX < -0.15f : awayX > 0.15f;
                ok &= rhd ? (lx > 0.08f && rx < -0.08f && inboardLeft) : (lx < -0.08f && rx > 0.08f && inboardLeft);
                got += $"{(rhd ? " RHD" : "LHD")}: model-left hand x {lx:+0.00;-0.00}, model-right {rx:+0.00;-0.00}, shift hand {awayX:+0.00;-0.00};";
                arms.Destroy();
                Object.DestroyImmediate(model);
            }
            Check(ok, "cockpit arms: RHD mirrors them - the shift hand is the console-side hand on both drive sides", got);

            // THE HANDS NEVER INSIDE THE WHEEL (owner, 2026-10-16): every
            // sequence, every frame, the skinned vertices against the measured
            // wheel. Before (the first cut) and after (Contact).
            bool keep = CockpitArms.Contact;
            try
            {
                var before = Battery(lay, false);
                var after = Battery(lay, true);
                bool clean = true;
                foreach (var kv in after) clean &= kv.Value.x <= 0.001f;
                string rows = "";
                foreach (var kv in after)
                {
                    var b0 = before[kv.Key];
                    rows += $"\n         {kv.Key,-22} fingers {b0.x * 1000f,5:0.0} -> {kv.Value.x * 1000f,4:0.0} mm, " +
                            $"hand {b0.y * 1000f,5:0.0} -> {kv.Value.y * 1000f,4:0.0} mm, forearm {b0.z * 1000f,5:0.0} -> {kv.Value.z * 1000f,4:0.0} mm" +
                            (Worst.TryGetValue(kv.Key, out var wy) ? "  [worst: " + wy + "]" : "");
                }
                Check(clean, "cockpit arms: no finger vertex deeper than 1 mm in the wheel (rim, spokes, pad), any frame of any sequence (max depth before -> after)", rows);
            }
            finally { CockpitArms.Contact = keep; }
        }

        /// <summary>Max depth in the wheel (x fingers, y hand, z forearm),
        /// metres, per sequence, over every frame at 60 Hz.</summary>
        static readonly Dictionary<string, string> Worst = new Dictionary<string, string>();

        static Dictionary<string, Vector3> Battery(CockpitModel.Layout lay, bool contact)
        {
            CockpitArms.Contact = contact;
            var res = new Dictionary<string, Vector3>();
            var model = new GameObject("ArmsDepthModel") { hideFlags = HideFlags.HideAndDontSave };
            var arms = CockpitArms.Create(model.transform, lay, 0);
            try
            {
                if (arms == null) return res;
                Vector3 worst = Vector3.zero;
                string why = "";
                void Frame(float theta, bool shift, bool brake)
                {
                    arms.Drive(Dt, theta, shift, brake);
                    arms.Depths(out float f, out float h, out float fa);
                    if (f > worst.x + 1e-5f)
                        why = $"L {arms.Plan.L.mode}/{arms.Plan.L.kind}{arms.Plan.L.phase} R {arms.Plan.R.mode}/{arms.Plan.R.kind}{arms.Plan.R.phase} " +
                              $"u {(arms.Plan.R.mode == CockpitArms.Mode.Move ? arms.Plan.R.t / Mathf.Max(1e-3f, arms.Plan.R.dur) : arms.Plan.L.t / Mathf.Max(1e-3f, arms.Plan.L.dur)):0.00} wheel {theta:0}";
                    worst = Vector3.Max(worst, new Vector3(f, h, fa));
                }
                void Run(string name, float seconds, System.Func<float, float> theta, System.Func<float, bool> shift = null,
                         System.Func<float, bool> brake = null, bool fresh = true)
                {
                    if (fresh) { arms.Plan.Reset(theta(0f)); arms.Drive(0f, theta(0f), false, false); }
                    worst = Vector3.zero;
                    arms.Depths(out float f0, out float h0, out float a0);
                    worst = new Vector3(f0, h0, a0);
                    int n = Mathf.RoundToInt(seconds / Dt);
                    for (int i = 1; i <= n; i++)
                    {
                        float t = i * Dt;
                        Frame(theta(t), shift != null && shift(t), brake != null && brake(t));
                    }
                    res[name] = worst;
                    if (contact && worst.x > 0.001f) Worst[name] = why;
                }
                Run("rest", 0.5f, t => 0f);
                Run("small corrections", 4f, t => 30f * Mathf.Sin(t * Mathf.PI));
                Run("hand over hand (right)", 2.2f, t => -Mathf.Min(450f, 300f * t));
                Run("unwind", 1.6f, t => Mathf.Min(0f, -450f + 750f * t), fresh: false);
                Run("hand over hand (left)", 3.0f, t => Mathf.Min(450f, 180f * t));
                Run("unwind (left, open)", 1.6f, t => Mathf.Max(0f, 450f - 750f * t), fresh: false);
                Run("shift reach", 1.5f, t => 0f, t => t < 1.5f * Dt);
                Run("handbrake reach", 2.2f, t => 90f, null, t => t < 1.4f);
                // Every 5 degrees (finer, 3 a frame): 0 -> +450 -> -450 -> 0.
                Run("sweep +/-450", 10f, t => t < 2.5f ? 180f * t : t < 7.5f ? 450f - 180f * (t - 2.5f) : -450f + 180f * (t - 7.5f));
                // And held still at each 5 degrees, settled (hands rest there).
                Vector3 still = Vector3.zero;
                for (float th = -450f; th <= 450f; th += 5f)
                {
                    arms.Plan.Reset(th);
                    arms.Drive(0f, th, false, false);
                    for (int i = 0; i < 6; i++) Frame(th, false, false);
                    arms.Depths(out float f, out float h, out float fa);
                    still = Vector3.Max(still, new Vector3(f, h, fa));
                }
                res["still every 5 deg"] = still;
            }
            finally
            {
                if (arms != null) arms.Destroy();
                Object.DestroyImmediate(model);
            }
            return res;
        }

        // ------------------------------------------------------------------
        //  The shots
        // ------------------------------------------------------------------

        static void Shots()
        {
            var def = TrackCatalog.At(0);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (!string.IsNullOrEmpty(only))
                foreach (var d in TrackCatalog.All) if (d.id == only) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { log.AppendLine("[Arms] venue did not open"); return; }
            var t = player.transform;
            var car = player.GetComponent<CarController>();
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
            Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
            var keepView = ChaseCamera.Current;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            var cv = Object.FindFirstObjectByType<CockpitView>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null && globals.sun != null) { TimeOfDay.Apply(TimeOfDay.Noon, globals.sun); globals.Apply(); }
            float lockDeg = Mathf.Max(car.maxSteerLowSpeedDeg, car.maxSteerDriftDeg);
            CockpitInterior ci = null;

            CockpitInterior Ready(bool rhd)
            {
                DriveSide.Override = rhd;
                PSXScreenshotTool.AspectOverride = 16f / 9f;
                car.currentRPM = 3000f; car.speedKmh = 30f;
                if (cv != null) cv.PreviewShow(true);
                if (ci == null) ci = player.GetComponentInChildren<CockpitInterior>(true);
                if (ci == null) return null;
                ci.Refit();
                ci.Show(true, true);
                ci.HideShell(true);
                // The 2D cluster stands down in this view (GaugesShowing).
                foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                Canvas.ForceUpdateCanvases();
                return ci;
            }
            void Steer(float road, float dt) { ci.SetSteer(road, lockDeg); ci.Tick(dt, null); }
            void Shoot(string name, bool rhd, float yaw = 0f)
            {
                Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, body != null ? body.Def : null,
                                                      body != null ? body.LengthScale : 1f, body != null ? body.BodyLift : 0f, rhd);
                var rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(yaw), yaw, 0f);
                cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, yaw * CockpitLook.PassengerSign / CockpitLook.MaxYaw);
                PSXScreenshotTool.ShotAs(cam, "cockpitarms_" + name, t.TransformPoint(eye), rot);
                var p = ci.Arms.Plan;
                ci.Arms.Depths(out float sf, out float sh, out _);
                log.AppendLine($"[Arms] shot {name} (depth fingers {sf * 1000f:0.0} mm, hand {sh * 1000f:0.0}): {(rhd ? "RHD" : "LHD")} wheel {ci.WheelDeg:0} deg; L {p.L.mode}/{p.L.kind} a {p.L.alpha:0} open {p.L.open:0.00} relax {p.L.relax:0.00}" +
                               $"{(p.L.crossed ? " crossed" : "")}; R {p.R.mode}/{p.R.kind} a {p.R.alpha:0} open {p.R.open:0.00} relax {p.R.relax:0.00}{(p.R.crossed ? " crossed" : "")}");
            }
            // A close-up of one hand: from the eye, and from beside the rim.
            void Close(string name, bool left)
            {
                var arms = ci.Arms;
                arms.GripFrame(left, out var at, out var tan, out var rout);
                var p = left ? arms.Plan.L : arms.Plan.R;
                // The hand, wherever it is (not only gripping): its rim point
                // follows alpha; aim between it and the hand's own bone.
                arms.Probe(left, out var hp, out _, out _);
                Vector3 aim = Vector3.Lerp(at, hp, 0.35f);
                var M = ci.Model;
                Vector3 eyeW = M.TransformPoint(CockpitInterior.L.eye);
                Vector3 aimW = M.TransformPoint(aim);
                float keepN = cam.nearClipPlane, keepF = cam.fieldOfView;
                cam.nearClipPlane = 0.004f;
                cam.fieldOfView = 24f;
                PSXScreenshotTool.ShotAs(cam, "cockpitcontact_" + name + "_eye", eyeW, Quaternion.LookRotation(aimW - eyeW, t.up));
                Vector3 face = CockpitInterior.L.wheel.rot * Vector3.back;
                Vector3 side = at + tan * 0.15f + rout * 0.05f + face * 0.03f;
                Vector3 sideW = M.TransformPoint(side);
                cam.fieldOfView = 40f;
                PSXScreenshotTool.ShotAs(cam, "cockpitcontact_" + name + "_side", sideW,
                                         Quaternion.LookRotation(aimW - sideW, M.TransformDirection(rout)));
                cam.nearClipPlane = keepN; cam.fieldOfView = keepF;
                arms.Depths(out float df, out float dh, out float da);
                log.AppendLine($"[Arms] contact shot {name} ({(CockpitArms.Contact ? "after" : "before")}): {(left ? "L" : "R")} {p.mode}/{p.kind} " +
                               $"alpha {p.alpha:0} open {p.open:0.00}; depth fingers {df * 1000f:0.0} mm, hand {dh * 1000f:0.0}, forearm {da * 1000f:0.0}; " +
                               "curls " + string.Join(" ", System.Array.ConvertAll(arms.Curls(left), c => c.ToString("0.00"))) +
                               " (stopped by: " + arms.Stoppers(left) + ")");
            }
            // Steps the scripted sweep until `until` holds (or the time runs out).
            bool Sweep(System.Func<float, float> road, ref float clock, float maxSec, System.Func<bool> until)
            {
                int n = Mathf.RoundToInt(maxSec / Dt);
                for (int i = 0; i < n; i++)
                {
                    clock += Dt;
                    Steer(road(clock), Dt);
                    if (until()) return true;
                }
                return false;
            }

            try
            {
                foreach (bool rhd in new[] { false, true })
                {
                    if (Ready(rhd) == null || ci.Arms == null) { log.AppendLine("[Arms] no cabin / no arms on the player"); return; }
                Canvas.ForceUpdateCanvases();
                    Steer(0f, 0f);
                    float clk = 0f;
                    Sweep(_ => 0f, ref clk, 0.5f, () => false);
                    Shoot(rhd ? "1_rest_rhd" : "1_rest_lhd", rhd);
                }

                // Hand over hand: a right turn ramped at the clip's pace
                // (1.25 turns in 2.5 s), LHD.
                Ready(false);
                Steer(0f, 0f);
                var plan = ci.Arms.Plan;
                float clock = 0f;
                System.Func<float, float> right = tt => Mathf.Min(lockDeg, lockDeg * tt / 2.5f);
                bool a = Sweep(right, ref clock, 3f, () => plan.R.mode == CockpitArms.Mode.Move && plan.R.kind == CockpitArms.Kind.Cross
                                                       && plan.R.t / plan.R.dur > 0.8f);
                Shoot("2_hoh_crossing", false);
                bool b = Sweep(right, ref clock, 3f, () => plan.L.mode == CockpitArms.Mode.Move && plan.L.kind == CockpitArms.Kind.Return
                                                       && plan.L.open > 0.8f);
                Shoot("3_hoh_open_palm", false);
                bool d = Sweep(right, ref clock, 3f, () => plan.L.mode == CockpitArms.Mode.Grip && plan.R.mode == CockpitArms.Mode.Grip
                                                       && plan.L.open < 0.15f);
                Shoot("4_hoh_regrip", false);
                log.AppendLine($"[Arms] hand-over-hand frames found: crossing {a}, open {b}, regrip {d}; crossings {plan.Crossings}");

                // Back to centre, then a manual upshift: the inboard hand goes.
                Sweep(_ => 0f, ref clock, 2.5f, () => false);
                car.manualMode = true;
                int shifts = car.ManualShifts;
                car.ShiftTo(Mathf.Min(car.currentGear + 1, 3));
                if (car.ManualShifts == shifts) { car.ShiftTo(1); }
                bool reach = Sweep(_ => 0f, ref clock, 1.2f, () => plan.R.mode == CockpitArms.Mode.Away && plan.R.phase == 2);
                log.AppendLine($"[Arms] manual shift: CarController.ManualShifts {shifts} -> {car.ManualShifts}, inboard hand away {reach}");
                Shoot("5_shift_reach", false);
                Sweep(_ => 0f, ref clock, 1.5f, () => false);

                // The handbrake, RHD (the inboard hand is the LEFT one there).
                Ready(true);
                Steer(0f, 0f);
                plan = ci.Arms.Plan;
                car.handbrakeInput = true;
                bool pulled = Sweep(_ => 0f, ref clock, 1.2f, () => plan.R.mode == CockpitArms.Mode.Away && plan.R.phase == 2);
                Sweep(_ => 0f, ref clock, 0.3f, () => false);
                Shoot("6_handbrake_rhd", true);
                car.handbrakeInput = false;
                Sweep(_ => 0f, ref clock, 1.0f, () => false);
                log.AppendLine($"[Arms] handbrake: inboard hand on the lever {pulled}, back {plan.R.mode == CockpitArms.Mode.Grip}");

                // The look-over toward the passenger seat, LHD: no stub.
                Ready(false);
                Steer(0f, 0f);
                Sweep(_ => 0f, ref clock, 0.5f, () => false);
                Shoot("7_look_over_lhd", false, CockpitLook.MaxYaw);

                // CONTACT close-ups, before (the first cut) and after: the
                // left hand at rest, the right hand crossed over the top, the
                // left hand open on the rim - from the driver's eye and from
                // beside the rim (its cross-section), LHD.
                bool keepContact = CockpitArms.Contact;
                try
                {
                    foreach (bool contact in new[] { false, true })
                    {
                        CockpitArms.Contact = contact;
                        string tag = contact ? "after" : "before";
                        Ready(false);
                        ci.Arms.Plan.Reset(0f);
                        Steer(0f, 0f);
                        Sweep(_ => 0f, ref clock, 0.5f, () => false);
                        Close(tag + "_1_rest", true);
                        plan = ci.Arms.Plan;
                        float c0 = clock;
                        System.Func<float, float> turn = tt => Mathf.Min(lockDeg, lockDeg * (tt - c0) / 2.5f);
                        Sweep(turn, ref clock, 3f, () => plan.R.mode == CockpitArms.Mode.Grip && plan.R.crossed);
                        Close(tag + "_2_top", false);
                        Sweep(turn, ref clock, 3f, () => plan.L.mode == CockpitArms.Mode.Move && plan.L.kind == CockpitArms.Kind.Return
                                                          && plan.L.open > 0.8f);
                        Close(tag + "_3_open", true);
                        Sweep(_ => 0f, ref clock, 2.5f, () => false);
                    }
                }
                finally { CockpitArms.Contact = keepContact; }

                // Where the arm pieces end up (model frame), for the report.
                ci.Arms.Probe(true, out var g, out var e, out _);
                log.AppendLine($"[Arms] rest LHD left hand: grip {g:F3} elbow {e:F3}; eye {CockpitInterior.L.eye:F3}");
            }
            finally
            {
                DriveSide.Override = null;
                car.handbrakeInput = false;
                PSXScreenshotTool.AspectOverride = 0f;
                if (ci != null) ci.HideShell(false);
                if (cv != null) cv.PreviewShow(false);
                ChaseCamera.PreviewView(keepView);
                cam.fieldOfView = keepFov;
                cam.nearClipPlane = keepNear;
            }
        }
    }
}
