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

        void FieldBegin(string id, TrackPath path)
        {
            ftb = null;
            if (!FieldTraceOn || path == null) return;
            ftb = new StringBuilder(1 << 20);
            ftb.Append("t,id,kind,x,z,heading,speed,steer,thr,brk,rev,why\n");
            TrafficSystem.WreckTrace = true; if (TrafficSystem.Instance != null) TrafficSystem.Instance.WreckSpots.Clear();
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

        void FieldSample()
        {
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
                        c.steerInput, c.throttleInput, c.brakeInput, ai != null && ai.DebugBackingUp, ai != null ? ai.DebugGiveWhy : "");
                }
            var ts = TrafficSystem.Instance;
            if (ts != null && ts.WreckLog.Count > ftWrecks) { for (int w = ftWrecks; w < ts.WreckLog.Count; w++) ftWreckNotes.Append(t.ToString("0.0", FtCI)).Append(" s: ").Append(ts.WreckLog[w]).Append('\n'); ftWrecks = ts.WreckLog.Count; }
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
