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
        public static bool Active => rm != null;

        public static void Begin(RaceManager race)
        {
            rm = race; t0 = Time.time;
            overS.Clear(); stayS.Clear(); longest.Clear(); stays.Clear(); missAt.Clear(); missLines.Clear();
            nearMisses = 0;
            if (rm == null) return;
            RacePlayCheck.Note($"LANE USE: PSX_AI_PASS_ONCOMING {(AIDriver.PassOncoming ? "ON (pass in the oncoming lane, the better line where clear)" : "OFF (the lane discipline as before)")}");
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
            RacePlayCheck.Summary($"LANE USE {(AIDriver.PassOncoming ? "ON" : "OFF")} | passes {begun} oncoming-lane {oncoming} held {held} done {done} aborted {aborted} | near misses {nearMisses} | over-line {over:0}s longest {lg:0.0}s | better-line {open:0}s");
            RacePlayCheck.Check(lg <= 20f, "every rival comes back to the right lane (no stay over the centreline past 20 s)", $"{lg:0.0} s ({lgWho})");
            rm = null;
        }
    }
}
