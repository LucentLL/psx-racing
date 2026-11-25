using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// RIVALS' JUNCTION TURNS, TRACED (cityai 11-25; the owner: "AI can't even turn on this track, they make 90
    /// degree turns"). Started by RacePlayCheck in a checkpoint race when PSX_TT_OUT names a folder. Writes:
    /// - trace.csv: every car at 20 Hz (x, y, z, forward speed, yaw rate, steer / throttle / brake / handbrake, the
    ///   point its steering chased, its path index, the routed path's junction flag and curvature there, the speed
    ///   it planned, per-wheel on-road bits and the spread of the four contact heights, its remaining distance);
    /// - hits.csv: every collision a car reported (speed, hard, the collider, its layer);
    /// - turns.csv: the course line's turns of 30 degrees or more (centre, deflection), and course.csv (the line);
    /// - plan_&lt;k&gt;.png (PSX_TT_PLAN=1, needs a graphics device): a top-down orthographic frame of each turn, 100 m
    ///   square, north up, with its mapping in turns.csv;
    /// - chase_&lt;k&gt;_&lt;phase&gt;.png (PSX_TT_CHASE=k): three frames of the first rival through turn k on lap 2 or
    ///   later, from a camera 14 m behind it (entry 30 m out, nearest the centre, 25 m past).
    /// Measures only: it changes nothing a car does.
    /// </summary>
    public static class CheckpointTurnTrace
    {
        static string outDir;
        static RaceManager rm;
        static StringBuilder trace, hits;
        static float t0, nextSample;
        static readonly List<Vector3> turnC = new List<Vector3>();
        static readonly List<float> turnDeg = new List<float>();
        static int chaseTurn = -1, chasePhase;
        static bool[] planDone; static bool planOn;
        static float chaseMin;
        static CarController chaseCar;
        static float chaseLast;
        static Camera chaseCam;
        static RenderTexture chaseRT;
        public static bool On => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PSX_TT_OUT"));
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public static void Begin(RaceManager race)
        {
            outDir = System.Environment.GetEnvironmentVariable("PSX_TT_OUT");
            if (string.IsNullOrEmpty(outDir) || race == null || race.path == null) { outDir = null; return; }
            outDir = Path.Combine(outDir, $"{RacePlayCheck.VenueNow}_s{RacePlayCheck.SeedNow}_{(string.IsNullOrEmpty(RacePlayCheck.current.traffic) ? "hour" : RacePlayCheck.current.traffic)}");
            Directory.CreateDirectory(outDir);
            rm = race; t0 = Time.time; nextSample = 0f;
            trace = new StringBuilder(1 << 20);
            trace.AppendLine("t,car,x,y,z,v,yaw,steer,thr,brk,hb,aimx,aimz,idx,junc,curv,tgt,onroad,dyw,rem,retired,routed");
            hits = new StringBuilder();
            hits.AppendLine("t,car,x,z,v,hit,hard,what,layer");
            CollisionResponder.HitReportedOn += OnHit;
            // the course line and its turns
            var p = rm.path;
            var sb = new StringBuilder("i,x,y,z\n");
            for (int i = 0; i < p.Count; i++) { var w = p.GetPoint(i); sb.Append(i).Append(',').Append(F(w.x)).Append(',').Append(F(w.y)).Append(',').Append(F(w.z)).Append('\n'); }
            File.WriteAllText(Path.Combine(outDir, "course.csv"), sb.ToString());
            turnC.Clear(); turnDeg.Clear();
            int n = p.Count, win = Mathf.Max(2, Mathf.RoundToInt(24f / p.spacing));
            var defl = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = p.GetPoint(i) - p.GetPoint(i - win), b = p.GetPoint(i + win) - p.GetPoint(i);
                a.y = 0f; b.y = 0f;
                defl[i] = Vector3.Angle(a, b);
            }
            for (int i = 0; i < n; i++)
            {
                if (defl[i] < 30f) continue;
                bool peak = true;
                for (int k = -win; k <= win && peak; k++) { int j = (i + k + n) % n; if (j != i && (defl[j] > defl[i] || (defl[j] == defl[i] && j < i))) peak = false; }
                if (!peak) continue;
                turnC.Add(p.GetPoint(i)); turnDeg.Add(defl[i]);
            }
            var tsb = new StringBuilder("k,x,y,z,deg,planx0,planz0,planm\n");
            const float PlanM = 100f;
            for (int k = 0; k < turnC.Count; k++)
                tsb.Append(k).Append(',').Append(F(turnC[k].x)).Append(',').Append(F(turnC[k].y)).Append(',').Append(F(turnC[k].z)).Append(',')
                   .Append(F(turnDeg[k])).Append(',').Append(F(turnC[k].x - PlanM / 2)).Append(',').Append(F(turnC[k].z - PlanM / 2)).Append(',').Append(F(PlanM)).Append('\n');
            File.WriteAllText(Path.Combine(outDir, "turns.csv"), tsb.ToString());
            RacePlayCheck.Note($"TURN TRACE: {turnC.Count} turns of 30 deg or more on the course line, out {outDir}");
            planOn = false; planDone = new bool[turnC.Count];
            bool gfx = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            if (gfx && System.Environment.GetEnvironmentVariable("PSX_TT_PLAN") == "1")
                planOn = true;   // each turn shot when the autopilot comes within 70 m of it (its tiles are live then)
            string ch = System.Environment.GetEnvironmentVariable("PSX_TT_CHASE") ?? "-1";
            if (ch == "auto") { chaseTurn = -1; float best = 0f; for (int k = 0; k < turnDeg.Count; k++) if (turnDeg[k] > best) { best = turnDeg[k]; chaseTurn = k; } }
            else if (!int.TryParse(ch, out chaseTurn)) chaseTurn = -1;
            if (!gfx || chaseTurn >= turnC.Count) chaseTurn = -1;
            chasePhase = 0; chaseCar = null;
        }

        static string F(float v) => v.ToString("0.###", CI);

        static void PlanShot(int k, Vector3 c, float m)
        {
            var go = new GameObject("TT_PlanCam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = m * 0.5f;
            cam.transform.position = c + Vector3.up * 200f;
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // looking down, +z (north) up the frame
            cam.nearClipPlane = 1f; cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            Save(cam, 1024, 1024, Path.Combine(outDir, $"plan_{k}.png"));
            Object.DestroyImmediate(go);
        }

        static void Save(Camera cam, int w, int h, string file)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        }

        static void OnHit(CollisionResponder who, float speed, bool hard, Collider c)
        {
            if (hits == null || who == null) return;
            var car = who.GetComponentInParent<CarController>();
            var p = who.transform.position;
            hits.Append(F(Time.time - t0)).Append(',').Append(car != null ? car.name : who.name).Append(',').Append(F(p.x)).Append(',').Append(F(p.z)).Append(',')
                .Append(F(car != null ? Mathf.Abs(car.forwardSpeed) : 0f)).Append(',').Append(F(speed)).Append(',').Append(hard ? 1 : 0).Append(',')
                .Append(c != null ? c.name.Replace(',', ';') : "-").Append(',').Append(c != null ? c.gameObject.layer : -1).Append('\n');
        }

        /// <summary>Every physics step (RacePlayCheck's FixedUpdate): samples at 20 Hz.</summary>
        public static void Step()
        {
            if (outDir == null || rm == null) return;
            float t = Time.time - t0;
            if (chaseTurn >= 0) ChaseStep(t);
            if (planOn && rm.playerCar != null)
                for (int k = 0; k < turnC.Count; k++)
                {
                    if (planDone[k]) continue;
                    Vector3 d = rm.playerCar.transform.position - turnC[k]; d.y = 0f;
                    if (d.magnitude < 70f) { planDone[k] = true; PlanShot(k, turnC[k], 100f); }
                }
            if (t < nextSample) return;
            nextSample = t + 0.05f;
            var cp = rm.Checkpoints;
            foreach (var c in rm.allCars)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var ai = c.GetComponent<AIDriver>();
                var rb = c.Body;
                var pos = c.transform.position;
                var path = ai != null ? ai.path : null;
                int idx = ai != null ? ai.PathIndex : -1;
                bool junc = path != null && path.Routed && idx >= 0 && path.junction[path.Wrap(idx)];
                float curv = path != null && path.curvatures != null && path.curvatures.Length == path.Count && idx >= 0 ? path.curvatures[path.Wrap(idx)] : 0f;
                int onroad = 0; float yMin = float.MaxValue, yMax = float.MinValue;
                for (int w = 0; w < 4; w++)
                {
                    var wc = c.wheelContacts[w];
                    if (!wc.grounded) continue;
                    if (wc.onRoad) onroad |= 1 << w;
                    yMin = Mathf.Min(yMin, wc.point.y); yMax = Mathf.Max(yMax, wc.point.y);
                }
                var ccp = cp != null ? cp.Of(c) : null;
                var prog = rm.GetProgress(c);
                Vector3 aim = ai != null ? ai.DebugAim : Vector3.zero;
                trace.Append(F(t)).Append(',').Append(c.name).Append(',').Append(F(pos.x)).Append(',').Append(F(pos.y)).Append(',').Append(F(pos.z)).Append(',')
                     .Append(F(c.forwardSpeed)).Append(',').Append(F(rb != null ? rb.angularVelocity.y : 0f)).Append(',')
                     .Append(F(c.steerInput)).Append(',').Append(F(c.throttleInput)).Append(',').Append(F(c.brakeInput)).Append(',').Append(c.handbrakeInput ? 1 : 0).Append(',')
                     .Append(F(aim.x)).Append(',').Append(F(aim.z)).Append(',').Append(idx).Append(',').Append(junc ? 1 : 0).Append(',').Append(curv.ToString("0.#####", CI)).Append(',')
                     .Append(F(ai != null ? ai.DebugTarget : 0f)).Append(',').Append(onroad).Append(',').Append(F(yMax > yMin ? yMax - yMin : 0f)).Append(',')
                     .Append(F(ccp != null ? ccp.remaining : 0f)).Append(',').Append(prog != null && prog.retired ? 1 : 0).Append(',').Append(path != null && path.Routed ? 1 : 0).Append('\n');
            }
        }

        static void ChaseStep(float t)
        {
            if (chasePhase >= 3) return;
            Vector3 c = turnC[chaseTurn];
            if (chaseCar == null)
            {
                if (t < 60f) return;   // lap 2 or so: the field has spread
                foreach (var car in rm.allCars)
                {
                    if (car == null || car == rm.playerCar || !car.gameObject.activeInHierarchy) continue;
                    var p = rm.GetProgress(car);
                    if (p != null && p.retired) continue;
                    Vector3 d = car.transform.position - c; d.y = 0f;
                    if (d.magnitude < 60f && d.magnitude > 35f && Vector3.Dot(car.transform.forward, -d) > 0f) { chaseCar = car; chaseLast = d.magnitude; break; }
                }
                return;
            }
            Vector3 dd = chaseCar.transform.position - c; dd.y = 0f;
            float dist = dd.magnitude;
            bool approaching = Vector3.Dot(chaseCar.transform.forward, -dd) > 0f;
            if (chasePhase == 0 && approaching && dist <= 30f) { ChaseShot("entry"); chasePhase = 1; }
            else if (chasePhase == 1 && dist > chaseMin + 1f) { ChaseShot("apex"); chasePhase = 2; }
            else if (chasePhase == 2 && !approaching && dist >= 25f) { ChaseShot("exit"); chasePhase = 3; }
            if (chasePhase == 0) chaseMin = dist;
            chaseMin = Mathf.Min(chaseMin, dist);
            chaseLast = dist;
        }

        static void ChaseShot(string phase)
        {
            if (chaseCam == null)
            {
                var go = new GameObject("TT_ChaseCam");
                chaseCam = go.AddComponent<Camera>();
                chaseCam.fieldOfView = 60f; chaseCam.nearClipPlane = 0.3f; chaseCam.farClipPlane = 1500f;
                chaseCam.enabled = false;
            }
            var tr = chaseCar.transform;
            Vector3 fwd = tr.forward; fwd.y = 0f; fwd.Normalize();
            chaseCam.transform.position = tr.position - fwd * 14f + Vector3.up * 4.5f;
            chaseCam.transform.LookAt(tr.position + fwd * 8f + Vector3.up * 0.5f);
            Save(chaseCam, 1280, 720, Path.Combine(outDir, $"chase_{chaseTurn}_{chasePhase}_{phase}.png"));
            RacePlayCheck.Note($"TURN TRACE: chase frame {phase} of {chaseCar.name} at turn {chaseTurn}, t {Time.time - t0:0.0}");
        }

        public static void End()
        {
            if (outDir == null) return;
            CollisionResponder.HitReportedOn -= OnHit;
            File.WriteAllText(Path.Combine(outDir, "trace.csv"), trace.ToString());
            File.WriteAllText(Path.Combine(outDir, "hits.csv"), hits.ToString());
            if (chaseCam != null) Object.DestroyImmediate(chaseCam.gameObject);
            RacePlayCheck.Note($"TURN TRACE: {trace.Length / 1024} KB of trace written");
            outDir = null; rm = null; trace = null; hits = null;
        }
    }
}
