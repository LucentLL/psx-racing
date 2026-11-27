using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE FIELD TRACE (cityai round 2; &amp;trace=1 on a stage's field run): what every car did, from the running game
    /// (a built exe or the editor's tour), for the junction-turn plans off | on (tools\city\cityai_field_plan.py).
    /// At 10 Hz of game time: every race car (rival / player) and every race-traffic car within 80 m of a junction turn
    /// of the course line: t, id, kind, x, z, heading (deg, 0 = +z, clockwise), speed (m/s), and for race cars the
    /// controls (steer, throttle, brake), backing up, and the AI's give-way reason. Files: &lt;tag&gt;_&lt;stage&gt;_field.csv,
    /// _turns.csv (the course line's turns of 30 degrees or more), _edges.csv / _kerbs.csv (the map round each turn).
    /// &amp;seed=N seeds the race's draws (traffic, the rivals' mistakes) as the editor's play check does. Measures only.
    /// </summary>
    public partial class PerfTour
    {
        StringBuilder ftb;
        float ftNext, ftT0;
        readonly List<Vector3> ftTurns = new List<Vector3>();
        readonly List<float> ftDeg = new List<float>();
        readonly Dictionary<Rigidbody, int> ftIds = new Dictionary<Rigidbody, int>();
        readonly HashSet<Rigidbody> ftRace = new HashSet<Rigidbody>();
        int ftWrecks; readonly StringBuilder ftWreckNotes = new StringBuilder();
        static readonly CultureInfo FtCI = CultureInfo.InvariantCulture;

        static readonly float[] FtMarks = { 60f, 90f, 120f };
        readonly float[] ftMarkM = new float[3];
        int ftMarkI; float ftRaceT;
        /// <summary>For the stage summary's rivals line: the race time the run reached and the rivals' mean distance at 60 / 90 / 120 s.</summary>
        string FieldMarks() => ftb == null && ftRaceT <= 0f ? "" :
            $"; race time {ftRaceT:0.0} s; rivals mean m at 60/90/120 s of race time: {(ftMarkI > 0 ? ftMarkM[0].ToString("0") : "-")}/{(ftMarkI > 1 ? ftMarkM[1].ToString("0") : "-")}/{(ftMarkI > 2 ? ftMarkM[2].ToString("0") : "-")}";
        bool FieldTraceOn => ShotLink.Query(Request() ?? "", "trace") == "1";

        // (cityai round 6) &ab6=N: the four round 6 rules (the rivals' two, the traffic's two) off | on in alternating
        // blocks of N seconds in ONE process: per arm the traffic step (ms a physics step), the rivals' avoidance (ms a
        // rival tick) and the route plans (ms a plan). Starts off; the summary's 'ab6' line and <id>_ab6.txt.
        float ab6, ab6T0; bool ab6On; int ab6Step0, ab6Plan0; double ab6Avoid0; long ab6Ticks0; int ab6Blocks;
        readonly double[] ab6StepSum = new double[2], ab6PlanSum = new double[2], ab6AvoidSum = new double[2];
        readonly long[] ab6StepN = new long[2], ab6PlanN = new long[2], ab6AvoidN = new long[2];
        readonly float[] ab6StepMax = new float[2], ab6PlanMax = new float[2];
        void Ab6Begin()
        {
            ab6 = 0f;
            string q = ShotLink.Query(Request() ?? "", "ab6");
            if (string.IsNullOrEmpty(q) || !float.TryParse(q, System.Globalization.NumberStyles.Float, FtCI, out ab6) || ab6 <= 0f) { ab6 = 0f; return; }
            for (int k = 0; k < 2; k++) { ab6StepSum[k] = ab6PlanSum[k] = ab6AvoidSum[k] = 0; ab6StepN[k] = ab6PlanN[k] = ab6AvoidN[k] = 0; ab6StepMax[k] = ab6PlanMax[k] = 0f; }
            ab6On = false; ab6Blocks = 0; Ab6Set(false);
            AIDriver.CostOn = true; AIDriver.AvoidMs = 0; AIDriver.AvoidTicks = 0; AIDriver.RoundJudgeMs = 0; AIDriver.RoundJudges = 0;
            ab6Step0 = 0; ab6Plan0 = 0; ab6Avoid0 = 0; ab6Ticks0 = 0; ab6T0 = -1f;
        }
        static void Ab6Set(bool on) { AIDriver.RoundStandingOn = on; AIDriver.NoTailPushOn = on; TrafficSystem.LaneEndNoseOn = on; TrafficSystem.KnockByTouchOn = on; }
        void Ab6Close()
        {
            int k = ab6On ? 1 : 0;
            var sl = TrafficSystem.StepMs; if (ab6Step0 > sl.Count) ab6Step0 = 0;
            for (int i = ab6Step0; i < sl.Count; i++) { ab6StepSum[k] += sl[i]; ab6StepN[k]++; if (sl[i] > ab6StepMax[k]) ab6StepMax[k] = sl[i]; }
            ab6Step0 = sl.Count;
            var pl = AIRoutePlanner.BuildMsLog; if (ab6Plan0 > pl.Count) ab6Plan0 = 0;
            for (int i = ab6Plan0; i < pl.Count; i++) { ab6PlanSum[k] += pl[i]; ab6PlanN[k]++; if (pl[i] > ab6PlanMax[k]) ab6PlanMax[k] = pl[i]; }
            ab6Plan0 = pl.Count;
            ab6AvoidSum[k] += AIDriver.AvoidMs - ab6Avoid0; ab6AvoidN[k] += AIDriver.AvoidTicks - ab6Ticks0;
            ab6Avoid0 = AIDriver.AvoidMs; ab6Ticks0 = AIDriver.AvoidTicks; ab6Blocks++;
        }
        void Ab6Step()
        {
            if (ab6 <= 0f) return;
            if (ab6T0 < 0f) { ab6T0 = Time.time; ab6Step0 = TrafficSystem.StepMs.Count; ab6Plan0 = AIRoutePlanner.BuildMsLog.Count; ab6Avoid0 = AIDriver.AvoidMs; ab6Ticks0 = AIDriver.AvoidTicks; }
            bool on = (((int)((Time.time - ab6T0) / ab6)) & 1) == 1;
            if (on == ab6On) return;
            Ab6Close();
            ab6On = on; Ab6Set(on);
        }
        /// <summary>Closes the last block, puts the switches back on, and gives the summary's line (empty without &amp;ab6).</summary>
        string Ab6Finish()
        {
            if (ab6 <= 0f) return "";
            Ab6Close();
            AIDriver.RoundStandingOn = System.Environment.GetEnvironmentVariable("PSX_AI_ROUND_STANDING") != "0";
            AIDriver.NoTailPushOn = System.Environment.GetEnvironmentVariable("PSX_AI_NO_TAIL_PUSH") != "0";
            TrafficSystem.LaneEndNoseOn = System.Environment.GetEnvironmentVariable("PSX_TRAFFIC_LANE_END_NOSE") != "0";
            TrafficSystem.KnockByTouchOn = System.Environment.GetEnvironmentVariable("PSX_TRAFFIC_KNOCK_BY_TOUCH") != "0";
            AIDriver.CostOn = false;
            string M(double s, long n) => n > 0 ? (s / n).ToString("0.0000", FtCI) : "-";
            string line = $"ab6 round 6 rules off | on in alternating {ab6:0} s blocks ({ab6Blocks} blocks, one process): traffic step ms mean {M(ab6StepSum[0], ab6StepN[0])} | {M(ab6StepSum[1], ab6StepN[1])} (steps {ab6StepN[0]} | {ab6StepN[1]}, worst {ab6StepMax[0]:0.00} | {ab6StepMax[1]:0.00}); " +
                          $"rival avoidance ms a tick {M(ab6AvoidSum[0], ab6AvoidN[0])} | {M(ab6AvoidSum[1], ab6AvoidN[1])} (ticks {ab6AvoidN[0]} | {ab6AvoidN[1]}); " +
                          $"rival plan ms mean {M(ab6PlanSum[0], ab6PlanN[0])} | {M(ab6PlanSum[1], ab6PlanN[1])} (plans {ab6PlanN[0]} | {ab6PlanN[1]}, worst {ab6PlanMax[0]:0.00} | {ab6PlanMax[1]:0.00}); " +
                          $"crawl judgements {AIDriver.RoundJudges}, {(AIDriver.RoundJudges > 0 ? AIDriver.RoundJudgeMs / AIDriver.RoundJudges * 1000.0 : 0):0.0} us each";
            ab6 = 0f;
            return line;
        }

        void FieldBegin(string id, TrackPath path)
        {
            Ab6Begin();
            AIDriver.RoundsBegun = 0; AIDriver.RoundRefused.Clear(); AIDriver.RoundHeldRecoveries = 0;
            TrafficSystem.KnocksRenamed = 0; TrafficSystem.PushedOn = 0; TrafficSystem.NoseIns = 0; TrafficSystem.NoseCreeps = 0;
            ftb = null;
            if (!FieldTraceOn || path == null) return;
            ftb = new StringBuilder(1 << 20);
            ftb.Append("t,id,kind,x,z,heading,speed,steer,thr,brk,rev,why\n");
            TrafficSystem.WreckTrace = true; if (TrafficSystem.Instance != null) TrafficSystem.Instance.WreckSpots.Clear();
            // (cityai round 6) the traffic's rules, the racers' blocks, the ground over the road
            AIDriver.TraceOn = true; ftPath = path; ftHints.Clear(); gnRows = 0; gnOver = 0; gnHoles = 0; gnSteps = 0; gnWorst = 0f; gnWorstAt = ""; gnBox = 0; gnBoxBad = 0; gnSamples = 0; gnBoxWorst = 9f; gnBoxWorstAt = "";
            ftTraf = new StringBuilder(1 << 20); ftTraf.Append("t,id,x,y,z,heading,s,dir,lane,latNow,speed,vWant,gap,leaderV,halfW,wrecked,temper,why\n");
            ftAi = new StringBuilder(1 << 20); ftAi.Append("t,id,s,lat,v,thr,brk,why,contact,block,state\n");
            gnSb = new StringBuilder("kind,s,lat,x,z,roadY,groundY,over,collider\n");
            // &wreck=S[:lat]: a staged wreck in a lane (the same obstacle for every arm and seed)
            ftWreckS = -1f; ftWreckLat = 1.83f; ftLeadS = 0f;
            string wq = ShotLink.Query(Request() ?? "", "wreck");
            if (!string.IsNullOrEmpty(wq))
            {
                var wp = wq.Split(':');
                if (float.TryParse(wp[0], System.Globalization.NumberStyles.Float, FtCI, out float ws)) ftWreckS = ws;
                if (wp.Length > 1 && float.TryParse(wp[1], System.Globalization.NumberStyles.Float, FtCI, out float wl)) ftWreckLat = wl;
            }
            ftNext = 0f; ftT0 = Time.time; ftIds.Clear(); ftMarkI = 0; ftRaceT = 0f; ftWreckNotes.Length = 0; ftWrecks = TrafficSystem.Instance != null ? TrafficSystem.Instance.WreckLog.Count : 0;
            TurnMapDump.Turns(path, ftTurns, ftDeg);
            Post(id + "_turns.csv", TurnMapDump.TurnsCsv(ftTurns, ftDeg));
            TurnMapDump.Map(ftTurns, out string edges, out string kerbs);
            Post(id + "_edges.csv", edges);
            Post(id + "_kerbs.csv", kerbs);
            IslandCheck(id, path);
            CorridorBegin(path);
            Say("FIELD TRACE " + id + ": " + ftTurns.Count + " junction turns of 30 deg or more on the course line");
        }

        /// <summary>
        /// THE ISLANDS CHECK (cityai round 3, &amp;islands=1 on a field run): the racing-turn arcs of THIS course (laid at
        /// race load, within 40 m of its line) sampled every metre; each sample is checked once the tour's car is within
        /// 90 m of it (the city's colliders stand only round the cars): at the sample and at 8 points 0.3 m round it, a
        /// ray down onto what is there. BAD = any of the nine rays lands on something not on the road layer (an island's
        /// kerb or top, a median, grass), or on nothing. Cars are skipped. Written at the end of the stage:
        /// &lt;tag&gt;_&lt;stage&gt;_islands.txt (samples checked, bad, and every bad sample with what it hit).
        /// </summary>
        readonly List<Vector2> isPts = new List<Vector2>(); readonly List<long> isKeys = new List<long>(); readonly List<float> isY = new List<float>();
        bool[] isDone; bool isOn; int isBad, isChecked; readonly HashSet<long> isBadArcs = new HashSet<long>(), isArcs = new HashSet<long>();
        readonly StringBuilder isSb = new StringBuilder();
        readonly RaycastHit[] isHits = new RaycastHit[8];

        void IslandCheck(string id, TrackPath path)
        {
            isOn = ShotLink.Query(Request() ?? "", "islands") == "1" && path != null;
            isPts.Clear(); isKeys.Clear(); isY.Clear(); isBadArcs.Clear(); isArcs.Clear(); isSb.Length = 0; isBad = isChecked = 0;
            if (!isOn) return;
            var pts = new List<Vector2>(); var keys = new List<long>();
            RacingTurns.CachedArcPoints(pts, keys, 1f);
            int hint = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                var q = pts[i];
                hint = path.NearestIndex(new Vector3(q.x, 0f, q.y), hint);
                var w = path.GetPoint(hint);
                if ((new Vector2(w.x, w.z) - q).sqrMagnitude > 40f * 40f) { hint = -1; continue; }
                isPts.Add(q); isKeys.Add(keys[i]); isY.Add(w.y); isArcs.Add(keys[i]);
            }
            isDone = new bool[isPts.Count];
        }

        // (cityai round 5) THE DRIVABLE CORRIDOR: every 4 m of the strip, once the tour car is within 70 m (colliders stream
        // in near the cars), a ray left and right at bumper height (0.45 m) to the first solid that is not a car, out to 12 m.
        // Written as _strip.csv with the strip's half width, the mapped pavement and the nearest city road.
        TrackPath coPath; bool[] coDone; StringBuilder coSb; readonly RaycastHit[] coHits = new RaycastHit[8];
        void CorridorBegin(TrackPath path)
        {
            coPath = path; coSb = new StringBuilder("s,x,z,half,paveR,paveL,solidR,nameR,solidL,nameL,road,way,cls,lanes,width,link,bridge,oneway\n");
            coDone = path != null ? new bool[path.Count] : null;
        }
        float CoRay(Vector3 from, Vector3 dir, out string name)
        {
            name = "";
            int n = Physics.RaycastNonAlloc(from, dir, coHits, 12f, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            for (int h = 0; h < n; h++)
            {
                var k = coHits[h].collider;
                if (k == null || (k.attachedRigidbody != null && !k.attachedRigidbody.isKinematic)) continue;
                if (coHits[h].distance < bd) { bd = coHits[h].distance; var t = k.transform; name = t.name; if (t.parent != null) name = t.parent.name + "/" + name; }
            }
            return bd == float.MaxValue ? float.NaN : bd;
        }
        // (cityai round 6) THE GROUND OVER THE ROAD: per strip waypoint (as the corridor: once the tour car is within 70 m),
        // every 0.5 m along to the next waypoint and at 9 offsets across the two inner lanes, a ray down on the road layer
        // and one on the Default layer: 'over' = a collider named Ground more than 5 cm above the road surface there (a
        // traffic car's box rides 22 cm over the road), 'hole' = no road-layer surface under the lane, 'step' = the road
        // surface changes by more than 10 cm within 0.5 m along. Written as _ground.csv.
        StringBuilder gnSb; int gnRows, gnOver, gnHoles, gnSteps, gnBox, gnBoxBad, gnSamples; float gnWorst, gnBoxWorst = 9f; string gnWorstAt = "", gnBoxWorstAt = "";
        static readonly float[] GnLats = { -3.3f, -2.8f, -1.8f, -0.8f, 0f, 0.8f, 1.8f, 2.8f, 3.3f };
        readonly RaycastHit[] gnHits = new RaycastHit[8];
        readonly float[] gnPrevY = new float[9];
        void GroundProbe(int i, TrafficSystem ts)
        {
            if (gnSb == null || coPath == null || i + 1 >= coPath.Count) return;
            Vector3 a = coPath.GetPoint(i), b = coPath.GetPoint(i + 1);
            float seg = Vector3.Distance(a, b); if (seg < 0.1f) return;
            Vector3 tg = (b - a) / seg; Vector3 flat = tg; flat.y = 0f; flat.Normalize();
            Vector3 rt = Vector3.Cross(Vector3.up, flat);
            float sArc = i * coPath.spacing;
            float pr = ts != null ? ts.PaveAt(sArc, true) : float.NaN, pl = ts != null ? ts.PaveAt(sArc, false) : float.NaN;
            // the traffic's own driving box along each lane that fits here, both directions
            if (ts != null)
                for (float d = 0f; d < seg; d += 0.5f)
                    for (int li = 0; li < ts.LanesFwd.Count + ts.LanesBack.Count; li++)
                    {
                        bool fw = li < ts.LanesFwd.Count; float lat = fw ? ts.LanesFwd[li] : ts.LanesBack[li - ts.LanesFwd.Count];
                        if (!float.IsNaN(pr) && (lat + 1.37f > pr || -lat + 1.37f > pl)) continue;   // the lane fit's own test (half width 1.02 + 0.35)
                        float cl = ts.DebugBoxClear(sArc + d, fw ? 1 : -1, lat, 1.02f, 2.4f, out Vector3 bat, out string touch);
                        gnBox++;
                        if ((cl < 0.08f || touch.Length > 0) && gnRows < 20000)
                        {
                            gnRows++; gnBoxBad++;
                            if (cl < gnBoxWorst) { gnBoxWorst = cl; gnBoxWorstAt = $"s {sArc + d:0.0} lane {lat:+0.0;-0.0} ({bat.x:0.0},{bat.z:0.0})"; }
                            gnSb.Append($"box,{sArc + d:0.0},{lat:0.0},{bat.x:0.00},{bat.z:0.00},{bat.y:0.00},,{cl:0.00},{touch}\n");
                        }
                    }
            for (int q = 0; q < GnLats.Length; q++) gnPrevY[q] = float.NaN;
            for (float d = 0f; d < seg; d += 0.5f)
                for (int q = 0; q < GnLats.Length; q++)
                {
                    float lat = GnLats[q];
                    if (!float.IsNaN(pr) && (lat > pr - 0.3f || -lat > pl - 0.3f)) continue;   // inside the mapped pavement only
                    Vector3 p = a + tg * d + rt * lat + Vector3.up * 3f;
                    float roadY = float.NaN; gnSamples++;
                    if (Physics.Raycast(p, Vector3.down, out RaycastHit rh, 8f, 1 << 8, QueryTriggerInteraction.Ignore)) roadY = rh.point.y;
                    float gY = float.NaN; string gName = "";
                    int n = Physics.RaycastNonAlloc(p, Vector3.down, gnHits, 8f, 1 << 0, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < n; h++)
                    {
                        var k = gnHits[h].collider;
                        if (k == null || k.attachedRigidbody != null || k.name != "Ground") continue;
                        if (float.IsNaN(gY) || gnHits[h].point.y > gY) { gY = gnHits[h].point.y; gName = (k.transform.parent != null ? k.transform.parent.name + "/" : "") + k.name; }
                    }
                    string kind = null; float over = float.NaN;
                    if (float.IsNaN(roadY)) { kind = "hole"; gnHoles++; }
                    else
                    {
                        if (!float.IsNaN(gY) && gY - roadY > 0.05f) { kind = "over"; over = gY - roadY; gnOver++; if (over > gnWorst) { gnWorst = over; gnWorstAt = $"s {sArc + d:0.0} lat {lat:+0.0;-0.0} ({p.x:0.0},{p.z:0.0}) {gName}"; } }
                        if (!float.IsNaN(gnPrevY[q]) && Mathf.Abs(roadY - gnPrevY[q] - tg.y * 0.5f) > 0.10f) { kind = kind == null ? "step" : kind + "+step"; gnSteps++; if (float.IsNaN(over)) over = roadY - gnPrevY[q]; }
                        gnPrevY[q] = roadY;
                    }
                    if (kind != null && gnRows < 20000)
                    { gnRows++; gnSb.Append($"{kind},{sArc + d:0.0},{lat:0.0},{p.x:0.00},{p.z:0.00},{roadY:0.00},{gY:0.00},{over:0.00},{gName}\n"); }
                }
        }

        void CorridorStep()
        {
            if (coDone == null || car == null || coPath == null) return;
            var cp = car.transform.position; var ts = TrafficSystem.Instance;
            int step = Mathf.Max(1, Mathf.RoundToInt(4f / Mathf.Max(0.5f, coPath.spacing)));
            for (int i = 0; i < coPath.Count; i += step)
            {
                if (coDone[i]) continue;
                var o = coPath.GetPoint(i);
                if ((o - cp).sqrMagnitude > 70f * 70f) continue;
                coDone[i] = true;
                for (int gi = i; gi < i + step && gi < coPath.Count; gi++) GroundProbe(gi, ts);   // (cityai round 6)
                var tg = coPath.GetTangent(i); tg.y = 0f; tg.Normalize();
                var rt = Vector3.Cross(Vector3.up, tg);
                var from = o + Vector3.up * 0.45f;
                float r = CoRay(from, rt, out string nr), l = CoRay(from, -rt, out string nl);
                float sArc = i * coPath.spacing;
                string road = ",,,,,,,";
                var map = PSXRacing.City.CityMap.Get();
                if (map != null && map.NearestRoadPoint(new Vector2(o.x, o.z), 30f, false, out int ei, out _, out _) && ei >= 0)
                { var e = map.edges[ei]; road = $"{(e.name ?? "").Replace(',', ' ')},{e.wayId},{e.cls},{e.lanes},{e.width:0.0},{(e.link ? 1 : 0)},{(e.bridge ? 1 : 0)},{(e.oneway ? 1 : 0)}"; }
                coSb.Append($"{sArc:0},{o.x:0.0},{o.z:0.0},{(ts != null ? ts.RoadHalf : 0f):0.0},{(ts != null ? ts.PaveAt(sArc, true) : float.NaN):0.0},{(ts != null ? ts.PaveAt(sArc, false) : float.NaN):0.0},{r:0.0},{nr.Replace(',', ' ')},{l:0.0},{nl.Replace(',', ' ')},{road}\n");
            }
        }

        void IslandStep()
        {
            if (!isOn || isDone == null || car == null) return;
            var cp = car.transform.position;
            const int roadLayer = 8;
            for (int i = 0; i < isPts.Count; i++)
            {
                if (isDone[i]) continue;
                var q = isPts[i];
                if ((new Vector2(cp.x, cp.z) - q).sqrMagnitude > 90f * 90f) continue;
                isDone[i] = true; isChecked++;
                string why = null;
                for (int k = 0; k < 9 && why == null; k++)
                {
                    Vector2 o = k == 0 ? Vector2.zero : new Vector2(Mathf.Cos(k * Mathf.PI / 4f), Mathf.Sin(k * Mathf.PI / 4f)) * 0.3f;
                    var from = new Vector3(q.x + o.x, isY[i] + 4f, q.y + o.y);
                    int n = Physics.RaycastNonAlloc(from, Vector3.down, isHits, 10f, ~0, QueryTriggerInteraction.Ignore);
                    RaycastHit best = default; float bd = float.MaxValue;
                    for (int h = 0; h < n; h++)
                    {
                        if (isHits[h].collider == null || isHits[h].collider.attachedRigidbody != null) continue;   // cars
                        if (isHits[h].distance < bd) { bd = isHits[h].distance; best = isHits[h]; }
                    }
                    if (bd == float.MaxValue) why = (k == 0 ? "centre" : "0.3 m out") + ": nothing under it";
                    else if (best.collider.gameObject.layer != roadLayer)
                        why = (k == 0 ? "centre" : "0.3 m out") + $": {best.collider.name} (layer {best.collider.gameObject.layer}) {best.point.y - isY[i]:+0.00;-0.00} m";
                }
                if (why != null)
                {
                    isBad++; isBadArcs.Add(isKeys[i]);
                    if (isBad <= 200) isSb.AppendLine($"  BAD ({q.x:0.0},{q.y:0.0}) arc {isKeys[i]}: {why}");
                }
            }
        }

        void IslandEnd(string id)
        {
            if (!isOn) return;
            int left = 0; if (isDone != null) foreach (bool d in isDone) if (!d) left++;
            string head = $"islands check {id}: {isArcs.Count} racing-turn arcs on this course, {isPts.Count} samples (1 m; each +8 rays at 0.3 m), {isChecked} checked with the car near ({left} never near), {isBad} over or within 0.3 m of a non-road surface on {isBadArcs.Count} arcs";
            Post(id + "_islands.txt", head + "\n" + isSb);
            Say(head);
            isOn = false;
        }

        /// <summary>(cityai round 5) the reason column: the AI's own give-way reason, else, for a car on the throttle that is
        /// not moving (under 2 m/s), what it is pressed against ("blocked by X") or "no way, no reason" when nothing touches it.</summary>
        static string WhyNow(CarController c, AIDriver ai)
        {
            string w = ai != null ? ai.DebugGiveWhy : "";
            if (!string.IsNullOrEmpty(w) || ai == null) return w ?? "";
            if (c.throttleInput > 0.5f && Mathf.Abs(c.forwardSpeed) < 2f)
            {
                var cr = c.GetComponent<CollisionResponder>();
                if (cr != null && Time.time - cr.LastContactTime < 0.5f) return "blocked by " + cr.LastContactName;
                return "no way, no reason";
            }
            return "";
        }

        void FieldSample()
        {
            Ab6Step();
            if (ftb == null) return;
            IslandStep();
            CorridorStep();
            float t = Time.time - ftT0;
            // (round 4, PC76) the rivals' distance at fixed RACE-time marks (a loaded machine loses race time in a run)
            ftRaceT = t;
            while (ftMarkI < FtMarks.Length && t >= FtMarks[ftMarkI])
            {
                var cr = CheckpointRace.Active; float sum = 0f; int n = 0;
                if (cr != null) foreach (var cp in cr.All) { if (cp.isPlayer) continue; sum += cp.odometer; n++; }
                ftMarkM[ftMarkI] = n > 0 ? sum / n : -1f;
                ftMarkI++;
            }
            if (t < ftNext) return;
            ftNext = t + 0.1f;
            var rm = RaceManager.Instance;
            var raceBodies = ftRace; raceBodies.Clear();
            if (rm != null && rm.allCars != null)
                foreach (var c in rm.allCars)
                {
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    if (c.Body != null) raceBodies.Add(c.Body);
                    var ai = c.GetComponent<AIDriver>();
                    string kind = c == rm.playerCar ? "player" : "rival";
                    Row(t, c.name, kind, c.transform.position, c.transform.forward, Mathf.Abs(c.forwardSpeed),
                        c.steerInput, c.throttleInput, c.brakeInput, ai != null && ai.DebugBackingUp, WhyNow(c, ai));
                    if (ftAi != null && ftPath != null && ai != null)
                    {
                        ftHints.TryGetValue(c, out int hh); if (hh == 0) hh = -1;
                        hh = ftPath.NearestIndex(c.transform.position, hh); ftHints[c] = hh == 0 ? 1 : hh;
                        Vector3 o = ftPath.GetPoint(hh), tg = ftPath.GetTangent(hh); Vector3 rt = Vector3.Cross(Vector3.up, tg).normalized;
                        float sA = hh * ftPath.spacing + Vector3.Dot(c.transform.position - o, tg), lA = Vector3.Dot(c.transform.position - o, rt);
                        if (sA > ftLeadS) ftLeadS = sA;
                        var cr = c.GetComponent<CollisionResponder>();
                        string con = cr != null && Time.time - cr.LastContactTime < 0.5f ? cr.LastContactName : "";
                        ftAi.Append(t.ToString("0.00", FtCI)).Append(',').Append(c.name).Append(',').Append(sA.ToString("0.0", FtCI)).Append(',').Append(lA.ToString("0.00", FtCI)).Append(',')
                            .Append(c.forwardSpeed.ToString("0.00", FtCI)).Append(',').Append(c.throttleInput.ToString("0.00", FtCI)).Append(',').Append(c.brakeInput.ToString("0.00", FtCI)).Append(',')
                            .Append((ai.DebugGiveWhy ?? "").Replace(',', ';')).Append(',').Append(con.Replace(',', ';')).Append(',')
                            .Append((ai.DebugBlock ?? "").Replace(',', ';')).Append(',').Append(ai.DebugState.Replace(',', ';')).Append('\n');
                    }
                }
            var ts = TrafficSystem.Instance;
            if (ts != null && ts.WreckLog.Count > ftWrecks) { for (int w = ftWrecks; w < ts.WreckLog.Count; w++) ftWreckNotes.Append(t.ToString("0.0", FtCI)).Append(" s: ").Append(ts.WreckLog[w]).Append('\n'); ftWrecks = ts.WreckLog.Count; }
            if (ftWreckS > 0f && ts != null && ftLeadS > ftWreckS - 150f)
            {
                bool ok = ts.StageWreck(ftWreckS, ftWreckLat);
                Say($"STAGED WRECK at s {ftWreckS:0} lat {ftWreckLat:0.0}: {(ok ? "placed" : "NO FREE POOL CAR")} at t {t:0.0} s, leader at s {ftLeadS:0}");
                if (ok) ftWreckS = -1f;
            }
            // (cityai round 6) every traffic car within 160 m of a race car, with the rule that set its speed
            if (ts != null && ftTraf != null && rm != null)
            {
                ts.DebugCars(ftCars);
                foreach (var d in ftCars)
                {
                    if (d.rb == null) continue;
                    var p = d.rb.position; bool near = false;
                    foreach (var c in rm.allCars) { if (c == null) continue; var q = c.transform.position; float dx = p.x - q.x, dz = p.z - q.z; if (dx * dx + dz * dz < 160f * 160f) { near = true; break; } }
                    if (!near) continue;
                    var fw = d.rb.transform.forward;
                    if (!ftTrafIds.TryGetValue(d.rb, out int tn)) { tn = ftTrafIds.Count + 1; ftTrafIds[d.rb] = tn; }
                    ftTraf.Append(t.ToString("0.00", FtCI)).Append(',').Append(d.rb.name).Append('#').Append(tn).Append(',')
                          .Append(p.x.ToString("0.00", FtCI)).Append(',').Append(p.y.ToString("0.00", FtCI)).Append(',').Append(p.z.ToString("0.00", FtCI)).Append(',')
                          .Append((Mathf.Atan2(fw.x, fw.z) * Mathf.Rad2Deg).ToString("0.0", FtCI)).Append(',').Append(d.s.ToString("0.0", FtCI)).Append(',').Append(d.dir).Append(',')
                          .Append(d.lat.ToString("0.00", FtCI)).Append(',').Append(d.latNow.ToString("0.00", FtCI)).Append(',').Append(d.speed.ToString("0.00", FtCI)).Append(',')
                          .Append(d.vWant.ToString("0.00", FtCI)).Append(',').Append(Mathf.Min(d.gap, 999f).ToString("0.0", FtCI)).Append(',').Append(d.leaderV.ToString("0.0", FtCI)).Append(',')
                          .Append(d.halfW.ToString("0.00", FtCI)).Append(',').Append(d.wrecked ? 1 : 0).Append(',').Append(d.temper).Append(',').Append(d.why).Append('\n');
                }
            }
            if (ts != null)
                for (int i = 0; i < ts.Obstacles.Count; i++)
                {
                    var rb = ts.Obstacles[i];
                    if (rb == null || raceBodies.Contains(rb)) continue;
                    var p = rb.position;
                    bool near = false;
                    foreach (var c in ftTurns) { float dx = p.x - c.x, dz = p.z - c.z; if (dx * dx + dz * dz < 80f * 80f) { near = true; break; } }
                    if (!near) continue;
                    if (!ftIds.TryGetValue(rb, out int n)) { n = ftIds.Count + 1; ftIds[rb] = n; }
                    var v = rb.linearVelocity; v.y = 0f;
                    Row(t, "T" + n + ":" + rb.name, "traffic", p, rb.transform.forward, v.magnitude, 0f, 0f, 0f, false, "");
                }
        }

        StringBuilder ftTraf, ftAi; TrackPath ftPath; float ftWreckS = -1f, ftWreckLat = 1.83f, ftLeadS;
        readonly Dictionary<CarController, int> ftHints = new Dictionary<CarController, int>();
        readonly List<TrafficSystem.DebugCar> ftCars = new List<TrafficSystem.DebugCar>();
        readonly Dictionary<Rigidbody, int> ftTrafIds = new Dictionary<Rigidbody, int>();

        void Row(float t, string id, string kind, Vector3 p, Vector3 fwd, float v, float st, float th, float br, bool rev, string why)
        {
            float hd = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            ftb.Append(t.ToString("0.00", FtCI)).Append(',').Append(id.Replace(',', ';')).Append(',').Append(kind).Append(',')
               .Append(p.x.ToString("0.00", FtCI)).Append(',').Append(p.z.ToString("0.00", FtCI)).Append(',').Append(hd.ToString("0.0", FtCI)).Append(',')
               .Append(v.ToString("0.00", FtCI)).Append(',').Append(st.ToString("0.00", FtCI)).Append(',').Append(th.ToString("0.00", FtCI)).Append(',')
               .Append(br.ToString("0.00", FtCI)).Append(',').Append(rev ? 1 : 0).Append(',').Append((why ?? "").Replace(',', ';').Replace(' ', '_')).Append('\n');
        }

        void FieldEnd(string id)
        {
            if (ftb == null) return;
            IslandEnd(id);
            if (coSb != null) Post(id + "_strip.csv", coSb.ToString()); coDone = null;
            Post(id + "_field.csv", ftb.ToString());
            if (ftTraf != null) { Post(id + "_traffic.csv", ftTraf.ToString()); ftTraf = null; }
            if (ftAi != null) { Post(id + "_ai.csv", ftAi.ToString()); ftAi = null; }
            if (gnSb != null)
            {
                Post(id + "_ground.csv", gnSb.ToString()); gnSb = null;
                Say($"GROUND PROBE {id}: {gnSamples} lane samples: Ground over the road by more than 5 cm at {gnOver} (worst {gnWorst:0.00} m at {gnWorstAt}), {gnHoles} with no road surface, {gnSteps} steps over 10 cm in 0.5 m; traffic box poses {gnBox}: {gnBoxBad} with the floor under 8 cm over the road or touching a static collider (least {gnBoxWorst:0.00} m at {gnBoxWorstAt})");
            }
            AIDriver.TraceOn = false;
            var tsw = TrafficSystem.Instance;
            if (tsw != null && tsw.WreckSpots.Count > 0) Post(id + "_spots.txt", string.Join("", tsw.WreckSpots));
            if (tsw != null) Post(id + "_wrecks.txt", ftWreckNotes.ToString() + "soft knocks (round 3: stopped, then drove on): " + tsw.SoftKnockLog.Count + (tsw.SoftKnockLog.Count > 0 ? "\n  " + string.Join("\n  ", tsw.SoftKnockLog) : ""));
            Say("FIELD TRACE " + id + ": " + (ftb.Length / 1024) + " KB");
            ftb = null;
        }

        /// <summary>&amp;seed=N: the race's draws seeded (RacePlayCheck's recipe), once the race manager is up.</summary>
        void FieldSeed()
        {
            string s = ShotLink.Query(Request() ?? "", "seed");
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, out int seed)) Random.InitState(9173 + seed * 101);
        }
    }
}
