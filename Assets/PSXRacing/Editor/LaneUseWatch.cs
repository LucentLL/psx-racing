using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LANE-USE WATCH (cpfix 11-17), RacePlayCheck's: the owner, 2026-11-17, verbatim: "I don't like racers being
    /// forced to stay in a particular lane. They should prefer staying in the 'right' lane, but passing around cars in
    /// the oncoming lane is an iconic part of street racing". Every race the play check runs, every rival:
    /// - PASSES from AIDriver's own counters: begun, of them into the oncoming lane and on a rival it was held behind,
    ///   done (that car behind it) and given up;
    /// - its time over the centreline of a two-way road (the oncoming lane), each stay in it, and the longest one
    ///   (a racer that does not come back to the right is a FAIL past 20 s);
    /// - NEAR MISSES: an oncoming traffic car met with under 0.6 m of air between the sides at 10 m/s or more closing
    ///   (sampled every physics step; one per pair per 3 s);
    /// - the better line: seconds its open line had it in the other lane.
    /// Head-ons are RacePlayCheck's own count (a traffic car coming at the racer, hit). PSX_AI_PASS_ONCOMING=0 runs
    /// the same race on the lane discipline as before.
    /// </summary>
    public static class LaneUseWatch
    {
        static RaceManager rm;
        static float t0;
        static readonly Dictionary<CarController, float> overS = new Dictionary<CarController, float>();
        static readonly Dictionary<CarController, float> stayS = new Dictionary<CarController, float>();
        static readonly Dictionary<CarController, float> longest = new Dictionary<CarController, float>();
        static readonly Dictionary<CarController, int> stays = new Dictionary<CarController, int>();
        static readonly Dictionary<(Object, Object), float> missAt = new Dictionary<(Object, Object), float>();
        static int nearMisses;
        static readonly List<string> missLines = new List<string>();
        // aipass 11-17: oncoming-stream traffic met on a road the rival's own map calls ONE-WAY (the race strip lays
        // oncoming lanes along the whole route), every head-on's context, and the last respawn of each car
        static int owMeets; static float owSeconds;
        static readonly Dictionary<(Object, Object), float> owAt = new Dictionary<(Object, Object), float>();
        static readonly List<string> owLines = new List<string>();
        static readonly Dictionary<CarController, float> respawnAt = new Dictionary<CarController, float>();
        static readonly List<string> headLines = new List<string>();
        static int headOnsRival, headOnsPlayer, headOnsSlow, headOnsAfterRespawn, headOnsOneWay, headOnsPassing;
        public static bool Active => rm != null;

        public static void Begin(RaceManager race)
        {
            rm = race; t0 = Time.time;
            overS.Clear(); stayS.Clear(); longest.Clear(); stays.Clear(); missAt.Clear(); missLines.Clear();
            nearMisses = 0;
            owMeets = 0; owSeconds = 0f; owAt.Clear(); owLines.Clear(); respawnAt.Clear(); headLines.Clear();
            headOnsRival = headOnsPlayer = headOnsSlow = headOnsAfterRespawn = headOnsOneWay = headOnsPassing = 0;
            RaceManager.Respawned -= OnRespawned;
            if (rm == null) return;
            RaceManager.Respawned += OnRespawned;
            RacePlayCheck.Note($"LANE USE: PSX_AI_PASS_ONCOMING {(AIDriver.PassOncoming ? "ON (pass in the oncoming lane, the better line where clear)" : "OFF (the lane discipline as before)")}");
        }

        static void OnRespawned(CarController c, int step, float lat) { if (c != null) respawnAt[c] = Time.time; }

        /// <summary>RacePlayCheck's head-on (a traffic car coming at the car, hit): what the car's AI was doing.</summary>
        public static void HeadOn(CarController who, Rigidbody other, float speed)
        {
            if (rm == null || who == null) return;
            bool player = who == rm.playerCar;
            if (player) headOnsPlayer++; else headOnsRival++;
            var ai = who.GetComponent<AIDriver>();
            float since = respawnAt.TryGetValue(who, out float ra) ? Time.time - ra : 1e9f;
            bool afterRespawn = since < 8f;
            bool oneWay = ai != null && ai.path != null && ai.path.Routed && ai.path.oneWayAt[ai.path.Wrap(ai.PathIndex)];
            bool passing = ai != null && ai.DebugPassSide != 0;
            if (Mathf.Abs(who.forwardSpeed) < 5f) headOnsSlow++;
            if (afterRespawn) headOnsAfterRespawn++;
            if (oneWay) headOnsOneWay++;
            if (passing) headOnsPassing++;
            string o = "";
            if (other != null)
            {
                Vector3 lo = who.transform.InverseTransformPoint(other.position);
                o = $"traffic {lo.z:+0.0;-0.0} m ahead {lo.x:+0.0;-0.0} right at {Vector3.Dot(other.linearVelocity, who.transform.forward) * 3.6f:+0;-0} km/h along";
            }
            headLines.Add($"HEAD-ON DETAIL {who.name}{(player ? " (player autopilot)" : "")} at {Time.time - t0:0.0}s, {speed:0.0} m/s: car {Mathf.Abs(who.forwardSpeed) * 3.6f:0} km/h" +
                          (ai != null ? $", road {(oneWay ? "ONE-WAY (own map)" : ai.DebugTwoWayHere ? "two-way" : "one-way")}, lat {ai.DebugRoadLat:+0.0;-0.0} line {ai.DebugLine:+0.0;-0.0} bias {ai.DebugBias:+0.0;-0.0}" +
                                        (float.IsNegativeInfinity(ai.DebugLeftLimit) ? ", no oncoming guard" : $", guard LIM {ai.DebugLeftLimit:+0.0;-0.0}") +
                                        $", pass side {ai.DebugPassSide}, give {ai.DebugGiveWhy ?? "-"}" : "") +
                          $", {(since < 1e8f ? "respawned " + since.ToString("0.0") + " s before" : "no respawn")}; {o}");
        }

        /// <summary>Every physics step while racing.</summary>
        public static void Step(float dt)
        {
            if (rm == null || rm.State != RaceManager.RaceState.Racing) return;
            var ts = TrafficSystem.Instance;
            foreach (var c in rm.allCars)
            {
                if (c == null || c == rm.playerCar) continue;
                var p = rm.GetProgress(c);
                if (p == null || p.finished || p.retired) continue;
                var ai = c.GetComponent<AIDriver>();
                if (ai == null || ai.path == null || ai.path.Count < 2) continue;
                // over the centreline of a two-way road: the AI's own lateral (its line is the road's centre on a
                // fixed line, the lanes' centre on a routed road; right of it is + )
                bool twoWay = ai.DebugTwoWayHere;
                float lat = ai.DebugRoadLat;
                bool over = twoWay && lat < -0.3f;
                if (over)
                {
                    overS.TryGetValue(c, out float o); overS[c] = o + dt;
                    stayS.TryGetValue(c, out float st);
                    if (st <= 0f) { stays.TryGetValue(c, out int n); stays[c] = n + 1; }
                    stayS[c] = st + dt;
                    longest.TryGetValue(c, out float lg);
                    if (st + dt > lg) longest[c] = st + dt;
                }
                else stayS[c] = 0f;
                // near misses with oncoming traffic
                if (ts == null) continue;
                Vector3 fwd = c.transform.forward;
                // ONCOMING TRAFFIC ON A ROAD THIS RIVAL'S MAP CALLS ONE-WAY: an oncoming-stream car within 60 m ahead in
                // its corridor (the strip lays oncoming lanes along the whole route; the routed AI holds no guard there)
                if (ai.path.Routed && ai.path.oneWayAt[ai.path.Wrap(ai.PathIndex)] && ts.ObstacleDir.Count == ts.Obstacles.Count)
                {
                    bool any = false;
                    for (int i = 0; i < ts.Obstacles.Count; i++)
                    {
                        var rb = ts.Obstacles[i];
                        if (rb == null || ts.ObstacleDir[i] >= 0) continue;
                        Vector3 lo = c.transform.InverseTransformPoint(rb.position);
                        if (lo.z < 0f || lo.z > 60f || Mathf.Abs(lo.x) > 3.5f || Mathf.Abs(lo.y) > 4f) continue;
                        any = true;
                        var key = ((Object)c, (Object)rb);
                        if (owAt.TryGetValue(key, out float at0) && Time.time - at0 < 5f) continue;
                        owAt[key] = Time.time;
                        owMeets++;
                        if (owLines.Count < 6)
                            owLines.Add($"ONE-WAY MEET {c.name} at {Time.time - t0:0.0}s: oncoming-stream {rb.name} {lo.z:0} m ahead {lo.x:+0.0;-0.0} right, racer {Mathf.Abs(c.forwardSpeed) * 3.6f:0} km/h, wp {ai.PathIndex}");
                    }
                    if (any) owSeconds += dt;
                }
                foreach (var rb in ts.Obstacles)
                {
                    if (rb == null || rb.useGravity) continue;
                    Vector3 d = rb.position - c.transform.position;
                    if (d.sqrMagnitude > 12f * 12f) continue;
                    float along = Vector3.Dot(rb.linearVelocity, fwd);
                    if (along > -3f) continue;                                   // not coming at us
                    float closing = c.forwardSpeed - along;
                    if (closing < 10f) continue;
                    Vector3 lo = c.transform.InverseTransformPoint(rb.position);
                    if (Mathf.Abs(lo.z) > 4.5f) continue;                       // not level with us
                    float air = Mathf.Abs(lo.x) - 0.95f - 0.95f;
                    if (air > 0.6f) continue;
                    var key = ((Object)c, (Object)rb);
                    if (missAt.TryGetValue(key, out float at) && Time.time - at < 3f) continue;
                    missAt[key] = Time.time;
                    nearMisses++;
                    if (missLines.Count < 8)
                        missLines.Add($"NEAR MISS {c.name} at {Time.time - t0:0.0}s: oncoming {rb.name} {air:0.00} m of air, {closing:0} m/s closing, racer {lat:+0.0;-0.0} m off its line");
                }
            }
        }

        public static void End()
        {
            if (rm == null) return;
            int begun = 0, oncoming = 0, held = 0, done = 0, aborted = 0;
            float over = 0f, lg = 0f, open = 0f; int stayN = 0; string lgWho = "";
            var per = new List<string>();
            foreach (var c in rm.allCars)
            {
                if (c == null || c == rm.playerCar) continue;
                var ai = c.GetComponent<AIDriver>();
                if (ai == null) continue;
                begun += ai.PassesBegun; oncoming += ai.PassesOncoming; held += ai.PassesHeld; done += ai.PassesDone; aborted += ai.PassesAborted;
                open += ai.OpenLineSeconds;
                overS.TryGetValue(c, out float o); over += o;
                stays.TryGetValue(c, out int n); stayN += n;
                longest.TryGetValue(c, out float l); if (l > lg) { lg = l; lgWho = c.name; }
                per.Add($"{c.name}: passes {ai.PassesBegun} (oncoming lane {ai.PassesOncoming}, held {ai.PassesHeld}) done {ai.PassesDone} aborted {ai.PassesAborted}, over the line {o:0.0} s in {n} stays (longest {l:0.0} s), better line {ai.OpenLineSeconds:0.0} s");
            }
            RacePlayCheck.Note($"LANE USE: passes begun {begun} (into the oncoming lane {oncoming}, on a rival it was held behind {held}), done {done}, aborted {aborted}; " +
                               $"near misses {nearMisses}; over the centreline {over:0} s in {stayN} stays, longest {lg:0.0} s ({lgWho}); better-line seconds {open:0}");
            foreach (var s in per) RacePlayCheck.Note("  " + s);
            foreach (var s in missLines) RacePlayCheck.Note("  " + s);
            RacePlayCheck.Note($"ONE-WAY MEETS (oncoming-stream traffic within 60 m ahead on a road the rival's map calls one-way): {owMeets}, {owSeconds:0} s");
            foreach (var s in owLines) RacePlayCheck.Note("  " + s);
            foreach (var s in headLines) RacePlayCheck.Note("  " + s);
            RacePlayCheck.Summary($"LANE USE {(AIDriver.PassOncoming ? "ON" : "OFF")} | passes {begun} oncoming-lane {oncoming} held {held} done {done} aborted {aborted} | near misses {nearMisses} | over-line {over:0}s longest {lg:0.0}s | better-line {open:0}s" +
                                  $" | head-ons rival {headOnsRival} player {headOnsPlayer} (slow {headOnsSlow}, after respawn {headOnsAfterRespawn}, one-way {headOnsOneWay}, passing {headOnsPassing}) | one-way meets {owMeets} {owSeconds:0}s");
            RacePlayCheck.Check(lg <= 20f, "every rival comes back to the right lane (no stay over the centreline past 20 s)", $"{lg:0.0} s ({lgWho})");
            RaceManager.Respawned -= OnRespawned;
            rm = null;
        }
    }
}
