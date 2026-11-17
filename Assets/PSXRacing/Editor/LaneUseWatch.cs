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
            AIDriver.ResetPassStats();
            if (rm == null) return;
            RaceManager.Respawned += OnRespawned;
            // PSX_PASS_SHOTS=dir (graphics only): frame strips of rivals' passes in the oncoming lane (LaneUseShots)
            string shots = System.Environment.GetEnvironmentVariable("PSX_PASS_SHOTS");
            if (!string.IsNullOrEmpty(shots) && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                var go = new GameObject("LaneUseShots");
                var ls = go.AddComponent<LaneUseShots>();
                ls.Init(rm, shots, System.Environment.GetEnvironmentVariable("PSX_RACE_SHOTTAG") ?? "pass");
            }
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
            RacePlayCheck.Note($"ONE-WAY MEETS (oncoming-stream traffic within 60 m ahead on a road the rival's map calls one-way): {owMeets}, {owSeconds:0} s; the race path's one-way waypoints (no oncoming stream there, PSX_TRAFFIC_ONEWAY): {(TrafficSystem.Instance != null ? TrafficSystem.Instance.OneWayWaypoints : 0)}");
            {
                var refused = new List<string>();
                foreach (var kv in AIDriver.PassRefused) refused.Add(kv.Key + " " + kv.Value);
                RacePlayCheck.Note($"PASS WINDOW {(AIDriver.PassWindowOn ? "ON" : "OFF")}: {AIDriver.PassJudgeCount} judgements, {AIDriver.PassJudgeMs:0.0} ms in all " +
                                   $"({(AIDriver.PassJudgeCount > 0 ? AIDriver.PassJudgeMs * 1000.0 / AIDriver.PassJudgeCount : 0):0} us each, worst {AIDriver.PassJudgeWorstMs * 1000.0:0} us); " +
                                   $"refused (judgements) by: {(refused.Count > 0 ? string.Join(", ", refused) : "none")}");
            }
            foreach (var s in owLines) RacePlayCheck.Note("  " + s);
            foreach (var s in headLines) RacePlayCheck.Note("  " + s);
            RacePlayCheck.Summary($"LANE USE {(AIDriver.PassOncoming ? "ON" : "OFF")} | passes {begun} oncoming-lane {oncoming} held {held} done {done} aborted {aborted} | near misses {nearMisses} | over-line {over:0}s longest {lg:0.0}s | better-line {open:0}s" +
                                  $" | head-ons rival {headOnsRival} player {headOnsPlayer} (slow {headOnsSlow}, after respawn {headOnsAfterRespawn}, one-way {headOnsOneWay}, passing {headOnsPassing}) | one-way meets {owMeets} {owSeconds:0}s");
            RacePlayCheck.Check(lg <= 20f, "every rival comes back to the right lane (no stay over the centreline past 20 s)", $"{lg:0.0} s ({lgWho})");
            RaceManager.Respawned -= OnRespawned;
            rm = null;
        }
    }

    /// <summary>PSX_PASS_SHOTS=dir (aipass 11-17, graphics only): FRAME STRIPS of rivals passing in the oncoming lane.
    /// When a rival on a two-way road chooses the left side round a car and swings out, a camera 15 m behind and 6.5 m
    /// over it takes eight frames 0.5 s apart; the strip is kept only if that became a pass into the oncoming lane
    /// (AIDriver.PassesOncoming), with each frame's label (time, speed, where across the road, the nearest oncoming
    /// car) and the outcome in a .txt beside it. A harness camera (not a game view): the frames say so.</summary>
    public class LaneUseShots : MonoBehaviour
    {
        const int MaxStrips = 4, Frames = 8;
        const float FrameEveryS = 0.5f, StripCoolS = 6f;
        RaceManager rm;
        string dir, tag;
        Camera cam;
        AIDriver who;
        int strips, frame, oncoming0, done0, aborted0;
        float nextAt, coolUntil;
        readonly List<byte[]> jpgs = new List<byte[]>();
        readonly List<string> labels = new List<string>();

        public void Init(RaceManager race, string folder, string shotTag)
        {
            rm = race; dir = folder; tag = shotTag;
            System.IO.Directory.CreateDirectory(dir);
        }

        void LateUpdate()
        {
            if (rm == null) { Destroy(gameObject); return; }
            if (rm.State == RaceManager.RaceState.Countdown) return;
            if (who == null)
            {
                if (strips >= MaxStrips || Time.time < coolUntil) return;
                foreach (var c in rm.allCars)
                {
                    if (c == null || c == rm.playerCar) continue;
                    var ai = c.GetComponent<AIDriver>();
                    var p = rm.GetProgress(c);
                    if (ai == null || p == null || p.finished || p.retired) continue;
                    if (ai.DebugPassSide < 0 && ai.DebugTwoWayHere && ai.DebugBias < -0.3f)
                    {
                        who = ai; frame = 0; nextAt = Time.time; jpgs.Clear(); labels.Clear();
                        oncoming0 = ai.PassesOncoming; done0 = ai.PassesDone; aborted0 = ai.PassesAborted;
                        break;
                    }
                }
                if (who == null) return;
            }
            if (Time.time < nextAt) return;
            nextAt = Time.time + FrameEveryS;
            var t = who.transform;
            if (cam == null)
            {
                var go = new GameObject("PassStripCam");
                cam = go.AddComponent<Camera>();
                if (Camera.main != null) cam.CopyFrom(Camera.main);
                cam.fieldOfView = 55f; cam.nearClipPlane = 0.3f; cam.farClipPlane = Mathf.Max(cam.farClipPlane, 900f);
                cam.enabled = false;
            }
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            cam.transform.position = t.position - fwd * 15f + Vector3.up * 6.5f - right * 1.0f;
            cam.transform.rotation = Quaternion.LookRotation(t.position + fwd * 22f + Vector3.up * 0.5f - cam.transform.position, Vector3.up);
            const int w = 960, h = 540;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                var shown = PSXScreenshotTool.Dithered(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev == rt || prev == shown ? null : prev;
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                jpgs.Add(tex.EncodeToJPG(88));
                Object.DestroyImmediate(tex);
            }
            rt.Release(); Object.DestroyImmediate(rt);
            // the nearest oncoming traffic car up the road from the rival
            float near = float.PositiveInfinity;
            var ts = TrafficSystem.Instance;
            if (ts != null)
                for (int i = 0; i < ts.Obstacles.Count; i++)
                {
                    var rb = ts.Obstacles[i];
                    if (rb == null || rb.useGravity || Vector3.Dot(rb.linearVelocity, t.forward) > -1f) continue;
                    Vector3 lo = t.InverseTransformPoint(rb.position);
                    if (lo.z > -3f && lo.z < near && Mathf.Abs(lo.x) < 12f) near = lo.z;
                }
            var car = who.GetComponent<CarController>();
            labels.Add($"{who.name} +{frame * FrameEveryS:0.0}s {Mathf.Abs(car != null ? car.forwardSpeed : 0f) * 3.6f:0} km/h lat {who.DebugRoadLat:+0.0;-0.0} m" +
                       $" | oncoming {(float.IsPositiveInfinity(near) ? "none" : near.ToString("0") + " m")} | {(who.DebugGiveWhy ?? "-")}" +
                       (float.IsNaN(who.LastPassMargin) ? "" : $" | window {who.LastPassMargin:0.0} s ({who.LastPassWhy})"));
            frame++;
            if (frame < Frames) return;
            // the strip is a pass into the oncoming lane: keep it
            bool pass = who.PassesOncoming > oncoming0;
            string outcome = who.PassesDone > done0 ? "pass done" : who.PassesAborted > aborted0 ? "pass given up" : "pass under way";
            if (pass)
            {
                for (int k = 0; k < jpgs.Count; k++)
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{tag}_strip{strips}_{k}.jpg"), jpgs[k]);
                labels.Add("OUTCOME " + outcome + " | harness camera 15 m behind, 6.5 m up (not a game view)");
                System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, $"{tag}_strip{strips}.txt"), labels);
                RacePlayCheck.Note($"PASS STRIP {strips}: {who.name}, {jpgs.Count} frames, {outcome}");
                strips++;
            }
            who = null; coolUntil = Time.time + StripCoolS;
        }

        void OnDestroy() { if (cam != null) Destroy(cam.gameObject); }
    }
}
