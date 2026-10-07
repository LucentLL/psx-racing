using System.Collections.Generic;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// HOW HARD THE AI DRIVES (AI at the limit, 2026-10-07), measured on every
    /// rival every physics step of a race-play-check, against what its car can
    /// do (<see cref="AIGrip"/>, calibrated on a skidpad):
    ///   corners  - peak lateral g in each limiting bend over the car's limit
    ///              at that speed, and the slowest speed through it over the
    ///              limit speed for its tightest point;
    ///   braking  - where the brake goes on against the ideal point for a full
    ///              stop to the bend ahead (1.0 = on the limit, 2 = twice as
    ///              far out), and the peak pedal;
    ///   pedals   - share of the race at full throttle, and on the brake;
    ///   slides   - rear slip over 0.2 rad under power, handbrake pulls, spins;
    ///   and recoveries. PSX_RACE_PROBE="a-b" adds each car's slowest speed
    ///   through waypoints a..b, every pass (a hairpin's speed).
    /// Reads only what the baseline AIDriver also has, so the same harness
    /// measures the car before and after.
    /// A plain class the runner ticks (RacePlayCheckRunner.FixedUpdate):
    /// Unity will not AddComponent a MonoBehaviour from an Editor folder whose
    /// file is named after it ("because it is an editor script").
    /// </summary>
    public class AIRaceStats
    {
        class S
        {
            public float tRace, tFull, tBrake;
            public Vector3 lastVel; public bool haveLast;
            public float aLat, aLong;
            // corner
            public bool inCorner, cornerClean;
            public float cPeakRatio, cPeakG, cKmax, cVmin, cLimitAtPeak;
            public int corners; public float sumRatio, sumPeakG, sumSpeedFrac;
            // braking
            public bool braking; public float bPeak, bEarly, bDist; public bool bValid;
            public int brakeEvents, brakeNoReason; public float sumEarly, sumPeakBrake, sumBrakeDist, sumDecelG; public float bPeakDecel;
            // slides
            public float slideT; public bool sliding; public int slides; public bool prevHb; public int hbPulls;
            public float spinCool; public int spins; public Queue<string> hist = new Queue<string>(); public int histTick;
            public int recoveries;
            // probe
            public bool inProbe; public float probeMin = 999f; public List<float> probeMins = new List<float>(); public string probeAt = ""; public List<string> probeAts = new List<string>(); public List<string> probeTrail = new List<string>(); public int probeTick;
        }
        readonly Dictionary<CarController, S> stats = new Dictionary<CarController, S>();
        RaceManager rm;
        CarController player;
        int probeA = -1, probeB = -1;

        public void Begin(RaceManager r, CarController p, MonoBehaviour host)
        {
            rm = r; player = p;
            var pr = System.Environment.GetEnvironmentVariable("PSX_RACE_PROBE");
            if (pr == "auto" && rm.path != null && rm.path.curvatures != null && rm.path.curvatures.Length == rm.path.Count)
            {
                // The tightest bend on the course, +-6 waypoints.
                int at = 0;
                for (int i = 0; i < rm.path.Count; i++) if (rm.path.curvatures[i] > rm.path.curvatures[at]) at = i;
                probeA = Mathf.Max(0, at - 6); probeB = Mathf.Min(rm.path.Count - 1, at + 6);
                float grade = (rm.path.GetPoint(at + 2).y - rm.path.GetPoint(at - 2).y) / (4f * rm.path.spacing);
                RacePlayCheck.Note($"PROBE the tightest bend: wp {at} ({at * rm.path.spacing:0} m of {rm.path.TotalLength:0}), " +
                                   $"R {1f / Mathf.Max(rm.path.curvatures[at], 1e-4f):0.0} m, grade {grade * 100f:0.0}%, road {rm.path.roadWidth:0.0} m");
                var sb = new System.Text.StringBuilder("PROBE radius by waypoint:");
                for (int i = Mathf.Max(0, at - 12); i <= Mathf.Min(rm.path.Count - 1, at + 12); i++)
                {
                    float turn = Vector3.SignedAngle(rm.path.GetTangent(i - 1), rm.path.GetTangent(i + 1), Vector3.up);
                    sb.Append($" {i}:{1f / Mathf.Max(rm.path.curvatures[i], 1e-4f):0.0}{(turn >= 0f ? "R" : "L")}");
                }
                RacePlayCheck.Note(sb.ToString());
            }
            else if (!string.IsNullOrEmpty(pr))
            {
                var parts = pr.Split('-');
                if (parts.Length == 2) { int.TryParse(parts[0], out probeA); int.TryParse(parts[1], out probeB); }
            }
            RaceManager.Respawned += OnRespawn;
            // PSX_AIRACE_SHOTS=dir (a watched run): the chase camera rides a
            // RIVAL - a drifter first - and saves a frame when it is sideways.
            shotDir = System.Environment.GetEnvironmentVariable("PSX_AIRACE_SHOTS");
            if (!string.IsNullOrEmpty(shotDir) && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                System.IO.Directory.CreateDirectory(shotDir);
                host.StartCoroutine(Spectate());
            }
            else shotDir = null;
        }

        string shotDir;
        int shots;
        static readonly System.Reflection.PropertyInfo drifterProp = typeof(AIDriver).GetProperty("Drifter");
        static readonly System.Reflection.PropertyInfo targetProp = typeof(AIDriver).GetProperty("DebugTarget");

        System.Collections.IEnumerator Spectate()
        {
            var cam = Object.FindAnyObjectByType<ChaseCamera>();
            CarController watched = null;
            float lastShot = -10f;
            while (cam != null && rm != null)
            {
                yield return new WaitForSeconds(0.1f);
                var wp = watched != null ? rm.GetProgress(watched) : null;
                if (watched == null || wp == null || wp.finished || wp.retired)
                {
                    CarController pick = null;
                    foreach (var c in rm.allCars)
                    {
                        if (c == null || c == player) continue;
                        var p = rm.GetProgress(c);
                        if (p == null || p.finished || p.retired) continue;
                        var ai = c.GetComponent<AIDriver>();
                        bool drifts = ai != null && drifterProp != null && (bool)drifterProp.GetValue(ai);
                        if (pick == null || drifts) { pick = c; if (drifts) break; }
                    }
                    if (pick == null) yield break;
                    watched = pick;
                    cam.target = watched.transform;
                    cam.targetCar = watched;
                    cam.ForgetAim();
                    RacePlayCheck.Note($"SPECTATE {watched.name}");
                }
                if (shots >= 8 || Time.time - lastShot < 3f) continue;
                float slip = Mathf.Abs(watched.chassisSlipAngle);
                if ((slip > 0.22f || watched.handbrakeInput) && Mathf.Abs(watched.forwardSpeed) > 7f)
                {
                    lastShot = Time.time;
                    yield return new WaitForSeconds(0.15f);
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    string file = System.IO.Path.Combine(shotDir, $"slide_{++shots}_{watched.name}_{slip * Mathf.Rad2Deg:0}deg.jpg");
                    System.IO.File.WriteAllBytes(file, tex.EncodeToJPG(88));
                    Object.Destroy(tex);
                    RacePlayCheck.Note($"shot {file}");
                }
            }
        }

        public void End() { RaceManager.Respawned -= OnRespawn; }

        void OnRespawn(CarController c, int step, float lat)
        {
            if (c == null) return;
            if (!stats.TryGetValue(c, out var s)) stats[c] = s = new S();
            s.recoveries++;
            s.haveLast = false; s.inCorner = false; s.braking = false;
        }

        const float CornerK = 1f / 150f;

        public void Tick()
        {
            if (rm == null || rm.path == null || rm.State != RaceManager.RaceState.Racing && !AnyRunning()) return;
            float dt = Time.fixedDeltaTime;
            var path = rm.path;
            foreach (var c in rm.allCars)
            {
                if (c == null || c.Body == null) continue;
                var ai = c.GetComponent<AIDriver>();
                var p = rm.GetProgress(c);
                if (ai == null || p == null || p.finished || p.retired || !ai.driving) continue;
                if (!stats.TryGetValue(c, out var s)) stats[c] = s = new S();
                Vector3 vel = c.Body.linearVelocity; vel.y = 0f;
                float v = vel.magnitude;
                if (s.haveLast && v > 3f)
                {
                    Vector3 acc = (vel - s.lastVel) / dt;
                    Vector3 dir = vel / v;
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    float k = Mathf.Clamp01(dt / 0.2f);
                    s.aLat = Mathf.Lerp(s.aLat, Mathf.Clamp(Vector3.Dot(acc, right), -30f, 30f), k);
                    s.aLong = Mathf.Lerp(s.aLong, Mathf.Clamp(Vector3.Dot(acc, dir), -30f, 30f), k);
                }
                s.lastVel = vel; s.haveLast = true;
                int idx = ai.PathIndex;
                bool traffic = ai.DebugLift > 0.05f || ai.DebugTrafficBrake > 0f;

                s.tRace += dt;
                if (c.throttleInput >= 0.95f) s.tFull += dt;
                if (c.brakeInput > 0.1f) s.tBrake += dt;

                // ---- corners ----
                float kHere = path.curvatures != null && path.curvatures.Length > 0 ? path.curvatures[path.Wrap(idx)] : 0f;
                if (kHere > CornerK)
                {
                    if (!s.inCorner)
                    {
                        s.inCorner = true; s.cornerClean = true;
                        s.cPeakRatio = 0f; s.cPeakG = 0f; s.cKmax = 0f; s.cVmin = 999f; s.cLimitAtPeak = 1f;
                    }
                    if (traffic) s.cornerClean = false;
                    s.cKmax = Mathf.Max(s.cKmax, kHere);
                    s.cVmin = Mathf.Min(s.cVmin, v);
                    float g = Mathf.Abs(s.aLat) / 9.81f;
                    float lim = AIGrip.LateralGMax(c, v);
                    if (g / lim > s.cPeakRatio) { s.cPeakRatio = g / lim; s.cPeakG = g; s.cLimitAtPeak = lim; }
                }
                else if (s.inCorner)
                {
                    s.inCorner = false;
                    float vLim = AIGrip.CornerSpeed(c, s.cKmax, 1f);
                    float playerVmax = rm.playerCar != null ? rm.playerCar.BuildTopSpeedMps : 0f;
                    float cap = AIDriver.TargetSpeedCap(ai.skill, playerVmax);
                    if (s.cornerClean && s.cVmin > 5f && vLim < cap * 0.9f && s.cPeakRatio < 2.5f)
                    {
                        s.corners++;
                        s.sumRatio += s.cPeakRatio;
                        s.sumPeakG += s.cPeakG;
                        s.sumSpeedFrac += s.cVmin / vLim;
                    }
                }

                // ---- braking ----
                bool bOn = c.brakeInput > 0.3f;
                if (bOn && !s.braking)
                {
                    s.braking = true; s.bPeak = 0f; s.bPeakDecel = 0f;
                    s.bValid = !traffic && v > 12f;
                    if (s.bValid)
                    {
                        // The bend this brake is for: the lowest limit speed in
                        // the next 300 m that is under the speed now.
                        int n = Mathf.Min(Mathf.CeilToInt(300f / path.spacing), path.Count - 1);
                        float worst = float.MaxValue; int at = -1;
                        for (int j = 1; j <= n; j++)
                        {
                            int wi = path.Wrap(idx + j);
                            if (path.HasEnds && idx + j >= path.Count) break;
                            float vl = AIGrip.CornerSpeed(c, path.curvatures[wi], 1f);
                            if (vl < v && vl < worst) { worst = vl; at = j; }
                        }
                        if (at < 0) { s.brakeNoReason++; s.bValid = false; }
                        else
                        {
                            float d = at * path.spacing;
                            float ideal = (v * v - worst * worst) / (2f * AIGrip.BrakeDecel(c, v));
                            s.bEarly = d / Mathf.Max(ideal, 5f);
                            s.bDist = d;
                        }
                    }
                }
                if (s.braking)
                {
                    s.bPeak = Mathf.Max(s.bPeak, c.brakeInput);
                    s.bPeakDecel = Mathf.Max(s.bPeakDecel, -s.aLong / 9.81f);
                    if (c.brakeInput < 0.1f)
                    {
                        s.braking = false;
                        if (s.bValid && !traffic)
                        {
                            s.brakeEvents++;
                            s.sumEarly += s.bEarly; s.sumPeakBrake += s.bPeak; s.sumBrakeDist += s.bDist; s.sumDecelG += s.bPeakDecel;
                        }
                    }
                }

                // ---- slides, handbrake, spins ----
                bool slideNow = Mathf.Abs(c.rearSlipAngle) > 0.2f && c.throttleInput > 0.5f && v > 8f;
                if (slideNow) { s.slideT += dt; if (s.slideT > 0.25f && !s.sliding) { s.sliding = true; s.slides++; } }
                else { s.slideT = 0f; s.sliding = false; }
                if (c.handbrakeInput && !s.prevHb) s.hbPulls++;
                s.prevHb = c.handbrakeInput;
                s.spinCool -= dt;
                if ((s.histTick++ % 12) == 0)
                {
                    Vector3 rr = Vector3.Cross(Vector3.up, path.GetTangent(idx)).normalized;
                    float lat = Vector3.Dot(c.transform.position - path.GetPoint(idx), rr);
                    float turn = Vector3.SignedAngle(path.GetTangent(idx), path.GetTangent(idx + 5), Vector3.up);
                    s.hist.Enqueue($"wp{idx}({turn:+0;-0}) {v * 3.6f:0}kmh lat{lat:+0.0;-0.0} st{c.steerInput:+0.00;-0.00} th{c.throttleInput:0.0} br{c.brakeInput:0.0} hb{(c.handbrakeInput ? 1 : 0)} rs{c.rearSlipAngle:+0.00;-0.00} fs{c.frontSlipAngle:+0.00;-0.00} cs{c.chassisSlipAngle:+0.00;-0.00} up{c.transform.up.y:0.00}");
                    while (s.hist.Count > 14) s.hist.Dequeue();
                }
                if (Mathf.Abs(c.chassisSlipAngle) > 1.2f && v > 5f && s.spinCool <= 0f)
                {
                    s.spins++; s.spinCool = 3f;
                    RacePlayCheck.Note($"SPIN {c.name} at wp {idx}: " + string.Join(" | ", s.hist));
                }

                // ---- probe ----
                if (probeA >= 0)
                {
                    bool inP = idx >= probeA && idx <= probeB;
                    if (inP && s.probeMins.Count == 0 && (s.probeTick++ % 12) == 0 && s.probeTrail.Count < 40)
                    {
                        Vector3 rr = Vector3.Cross(Vector3.up, path.GetTangent(idx)).normalized;
                        float lat = Vector3.Dot(c.transform.position - path.GetPoint(idx), rr);
                        s.probeTrail.Add($"{idx}:{v * 3.6f:0}kmh lat{lat:+0.0;-0.0} st{c.steerInput:+0.00;-0.00} th{c.throttleInput:0.0} br{c.brakeInput:0.0} fs{c.frontSlipAngle:+0.00;-0.00} cs{c.chassisSlipAngle:+0.00;-0.00} tgt{(targetProp != null ? (float)targetProp.GetValue(ai) * 3.6f : -1f):0} give{ai.DebugLift:0.0}/{ai.DebugTrafficBrake:0.0}");
                    }
                    if (inP && v < s.probeMin)
                    {
                        s.probeMin = v;
                        s.probeAt = $"wp{idx} th{c.throttleInput:0.00} br{c.brakeInput:0.00} st{c.steerInput:+0.00;-0.00} rs{c.rearSlipAngle:+0.00;-0.00} g{c.currentGear} give{ai.DebugLift:0.0}/{ai.DebugTrafficBrake:0.0}";
                    }
                    else if (!inP && s.inProbe) { s.probeMins.Add(s.probeMin); s.probeAts.Add(s.probeAt); s.probeMin = 999f; }
                    s.inProbe = inP;
                }
            }
        }

        bool AnyRunning()
        {
            foreach (var c in rm.allCars)
            {
                var p = c != null ? rm.GetProgress(c) : null;
                if (p != null && !p.finished && !p.retired) return true;
            }
            return false;
        }

        /// <summary>Per-car lines, then one AI summary line for the matrix.</summary>
        public void Report(Dictionary<CarController, float> finishedAt, Dictionary<CarController, float> retiredAt)
        {
            End();
            float tR = 0f, tF = 0f, tB = 0f, sRatio = 0f, sG = 0f, sFrac = 0f, sEarly = 0f, sPB = 0f, sDec = 0f;
            int nC = 0, nB = 0, slides = 0, hb = 0, spins = 0, rec = 0, hard = 0, noReason = 0;
            var probeAll = new List<float>();
            foreach (var c in rm.allCars)
            {
                if (c == null) continue;
                if (!stats.TryGetValue(c, out var s)) continue;
                if (s.inProbe) { s.probeMins.Add(s.probeMin); s.probeAts.Add(s.probeAt); s.inProbe = false; }
                if (s.probeAts.Count > 0) RacePlayCheck.Note($"  probe {c.name}: " + string.Join(" | ", s.probeAts));
                if (s.probeTrail.Count > 0) RacePlayCheck.Note($"  probe trail {c.name}: " + string.Join(" ", s.probeTrail));
                var ai = c.GetComponent<AIDriver>();
                var r = c.GetComponent<CollisionResponder>();
                bool isPlayer = c == player;
                string probe = probeA >= 0 ? " | probe wp" + probeA + "-" + probeB + " min " +
                               (s.probeMins.Count > 0 ? string.Join(",", s.probeMins.ConvertAll(x => (x * 3.6f).ToString("0"))) : "-") + " km/h" : "";
                RacePlayCheck.Note($"AI {c.name}{(isPlayer ? "*" : "")} skill {(ai != null ? ai.skill : 0f):0.00} F{c.frontDriveShare:0.00}: " +
                                   $"corners {s.corners} g/limit {(s.corners > 0 ? s.sumRatio / s.corners : 0f):0.00} ({(s.corners > 0 ? s.sumPeakG / s.corners : 0f):0.00} g) " +
                                   $"vmin/vlim {(s.corners > 0 ? s.sumSpeedFrac / s.corners : 0f):0.00} | brakes {s.brakeEvents} early x{(s.brakeEvents > 0 ? s.sumEarly / s.brakeEvents : 0f):0.00} " +
                                   $"at {(s.brakeEvents > 0 ? s.sumBrakeDist / s.brakeEvents : 0f):0} m peak {(s.brakeEvents > 0 ? s.sumPeakBrake / s.brakeEvents : 0f):0.00} " +
                                   $"({(s.brakeEvents > 0 ? s.sumDecelG / s.brakeEvents : 0f):0.00} g) no-reason {s.brakeNoReason} | full gas {(s.tRace > 0 ? s.tFull / s.tRace * 100f : 0f):0}% " +
                                   $"brake {(s.tRace > 0 ? s.tBrake / s.tRace * 100f : 0f):0}% | slides {s.slides} hb {s.hbPulls} spins {s.spins} rec {s.recoveries} " +
                                   $"hard {(r != null ? r.HardHits : 0)}" +
                                   (finishedAt.TryGetValue(c, out float ft) ? $" | home {ft:0.0}s" : retiredAt.ContainsKey(c) ? " | RETIRED" : " | not home") + probe);
                if (isPlayer) continue;
                tR += s.tRace; tF += s.tFull; tB += s.tBrake;
                sRatio += s.sumRatio; sG += s.sumPeakG; sFrac += s.sumSpeedFrac; nC += s.corners;
                sEarly += s.sumEarly; sPB += s.sumPeakBrake; sDec += s.sumDecelG; nB += s.brakeEvents; noReason += s.brakeNoReason;
                slides += s.slides; hb += s.hbPulls; spins += s.spins; rec += s.recoveries;
                hard += r != null ? r.HardHits : 0;
                probeAll.AddRange(s.probeMins);
            }
            float fin = 0f, best = float.MaxValue; int nf = 0;
            foreach (var kv in finishedAt) if (kv.Key != player) { fin += kv.Value; nf++; best = Mathf.Min(best, kv.Value); }
            float pMin = float.MaxValue, pSum = 0f;
            foreach (var x in probeAll) { pMin = Mathf.Min(pMin, x); pSum += x; }
            RacePlayCheck.Summary($"AI {RacePlayCheck.current.venue,-18} {(string.IsNullOrEmpty(RacePlayCheck.current.traffic) ? "HOUR" : RacePlayCheck.current.traffic.ToUpperInvariant()),-9} seed {RacePlayCheck.current.seed} | " +
                                  $"g/limit {(nC > 0 ? sRatio / nC : 0f):0.00} ({(nC > 0 ? sG / nC : 0f):0.00} g, {nC} bends) vmin/vlim {(nC > 0 ? sFrac / nC : 0f):0.00} | " +
                                  $"brake early x{(nB > 0 ? sEarly / nB : 0f):0.00} peak {(nB > 0 ? sPB / nB : 0f):0.00} ({(nB > 0 ? sDec / nB : 0f):0.00} g) n {nB} no-reason {noReason} | " +
                                  $"full gas {(tR > 0 ? tF / tR * 100f : 0f):0}% brake {(tR > 0 ? tB / tR * 100f : 0f):0}% | slides {slides} hb {hb} spins {spins} | " +
                                  $"recoveries {rec} hard hits {hard} retired {retiredAt.Count} | rivals home {nf} mean {(nf > 0 ? fin / nf : 0f):0.0}s best {(nf > 0 ? best : 0f):0.0}s" +
                                  (probeA >= 0 ? $" | probe min {(probeAll.Count > 0 ? pMin * 3.6f : 0f):0} mean {(probeAll.Count > 0 ? pSum / probeAll.Count * 3.6f : 0f):0} km/h ({probeAll.Count} passes)" : ""));
        }
    }
}
