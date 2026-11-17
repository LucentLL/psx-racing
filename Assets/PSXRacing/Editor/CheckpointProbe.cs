using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CHECKPOINT RACE PROBE (written by the independent check cpcheck1, kept by cpfix as the -cpDetour test). Inert unless PSX_CPPROBE=dir.
    /// - every AI plan (and the autopilot's): each waypoint's (edge, dir) legal on one-ways, each change of state a
    ///   legal successor of the one before (no illegal turn, no U-turn on a carriageway);
    /// - the minimap GPS polyline at every rebuild while a deviation is live, else every 3 s;
    /// - a 1 Hz car log; the nearby road graph for plotting;
    /// - PSX_CPPROBE_DETOUR=1: once, after a gate pass at 25-45 %, the autopilot is put on an off-route surface street
    ///   200-450 m away (a forced deviation; the respawn event is raised so the watch knows the cause) and re-plans;
    /// - world-camera frames (render request, no HUD) when a graphics device exists.
    /// </summary>
    [InitializeOnLoad]
    public static class CheckpointProbe
    {
        static CheckpointProbe() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            string dir = System.Environment.GetEnvironmentVariable("PSX_CPPROBE");
            if (string.IsNullOrEmpty(dir)) return;
            if (CheckpointProbeTick.Live != null) return;
            var rm = RaceManager.Instance;
            if (rm == null || rm.Checkpoints == null) return;
            var go = new GameObject("CheckpointProbe");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<CheckpointProbeTick>().dir = dir;
        }
    }

    public class CheckpointProbeTick : MonoBehaviour
    {
        public static CheckpointProbeTick Live;
        public string dir;
        RaceManager rm; CheckpointRace cp; CityMap map; CityRouter R;
        StreamWriter log, gps;
        bool wroteGraph;
        float nextSample, nextGps, gpsLiveUntil = -1f, t0 = -1f;
        int lastGpsVer = -1;
        readonly Dictionary<CarController, int> plansSeen = new Dictionary<CarController, int>();
        readonly Dictionary<CarController, int> passedSeen = new Dictionary<CarController, int>();
        int plansChecked, illegalOneWay, illegalTurn, gapJoins;
        readonly StringBuilder illegal = new StringBuilder();
        bool detourDone; float detourAt = -1f; string detourWhat = "";
        readonly HashSet<string> shots = new HashSet<string>();
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        void Awake() { Live = this; }
        void OnDestroy() { Summary(); if (Live == this) Live = null; }

        void Begin()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "probe_log.txt"), false) { AutoFlush = true };
            gps = new StreamWriter(Path.Combine(dir, "probe_gps.jsonl"), false) { AutoFlush = true };
            log.WriteLine("probe start " + System.DateTime.Now.ToString("HH:mm:ss") + " device " + SystemInfo.graphicsDeviceType +
                          " markers " + System.Environment.GetEnvironmentVariable("PSX_CP_MARKERS"));
        }

        void Update()
        {
            if (rm == null) { rm = RaceManager.Instance; if (rm == null) return; }
            if (cp == null) { cp = rm.Checkpoints; if (cp == null) return; }
            if (!cp.Ready) return;
            if (log == null) Begin();
            map = CityMap.Get(); R = cp.Router;
            if (!wroteGraph) { wroteGraph = true; WriteGraph(); }
            if (RaceReplay.Playing) return;
            if (rm.State == RaceManager.RaceState.Countdown) return;
            if (t0 < 0f) t0 = Time.time;
            float now = Time.time - t0;
            CheckPlans(now);
            var player = rm.playerCar;
            var pc = cp.Of(player);
            var pp = rm.GetProgress(player);
            // a gate pass by the player (for the detour trigger)
            bool justPassed = false;
            if (pc != null)
            {
                passedSeen.TryGetValue(player, out int was);
                if (pc.passedTotal != was) { justPassed = true; passedSeen[player] = pc.passedTotal; }
            }
            if (pc != null && pp != null && !pp.finished)
            {
                if (!detourDone && justPassed && System.Environment.GetEnvironmentVariable("PSX_CPPROBE_DETOUR") == "1")
                {
                    float f = cp.RaceFraction(player);
                    if (f > 0.25f && f < 0.45f && pc.remainingGate > 350f) Detour(player, pc, now);
                }
                // GPS dumps
                if (cp.GpsVersion != lastGpsVer && (now < gpsLiveUntil || now >= nextGps || CheckpointRaceWatch.WrongWayTestActive))
                {
                    lastGpsVer = cp.GpsVersion; nextGps = now + 3f;
                    DumpGps(now, player, pc);
                }
                Frames(now, player, pc, pp);
            }
            if (now >= nextSample)
            {
                nextSample = now + 1f;
                foreach (var c in rm.allCars)
                {
                    if (c == null) continue;
                    var p = rm.GetProgress(c); var cc = cp.Of(c);
                    if (p == null || cc == null) continue;
                    var q = c.transform.position;
                    string edgeName = cc.loc.valid && cc.loc.edge >= 0 && cc.loc.edge < map.edges.Length ? map.edges[cc.loc.edge].name : "";
                    log.WriteLine(string.Format(IC, "S {0:0.0} {1} P{2} next {3} rem {4:0} remGate {5:0} v {6:0} pos {7:0.0} {8:0.0} {9:0.0} edge {10} '{11}' dir {12} onPath {13} {14}{15}{16}",
                        now, c.name.Replace(' ', '_'), rm.GetPosition(c), cc.next, cp.RemainingToFinish(c), cc.remainingGate, Mathf.Abs(c.forwardSpeed) * 3.6f,
                        q.x, q.y, q.z, cc.loc.edge, edgeName, cc.loc.dir, cc.onPath, p.finished ? "FIN" : "", p.retired ? "RET" : "", cc.missed ? " MISSED" : ""));
                }
                if (pc != null && cp.PlayerWrongWay) log.WriteLine(string.Format(IC, "WW {0:0.0} player WRONG WAY shown", now));
            }
        }

        void WriteGraph()
        {
            // the route (every 4th waypoint), the gates, and every edge within 900 m of the route
            var sb = new StringBuilder();
            sb.Append("{\"route\":[");
            var path = rm.path;
            var keep = new List<Vector2>();
            for (int i = 0; i < path.Count; i += 4) { var w = path.GetPoint(i); sb.Append(i == 0 ? "" : ",").Append(string.Format(IC, "[{0:0.0},{1:0.0}]", w.x, w.z)); }
            for (int i = 0; i < path.Count; i += 20) { var w = path.GetPoint(i); keep.Add(new Vector2(w.x, w.z)); }
            sb.Append("],\"gates\":[");
            for (int k = 0; k < cp.GateCount; k++)
            {
                var g = cp.gates[k];
                sb.Append(k == 0 ? "" : ",").Append(string.Format(IC, "{{\"k\":{0},\"kind\":\"{1}\",\"x\":{2:0.0},\"z\":{3:0.0},\"hx\":{4:0.000},\"hz\":{5:0.000},\"w\":{6:0.0}}}",
                    k, g.kind, g.centre.x, g.centre.z, g.h.x, g.h.y, g.halfW));
            }
            sb.Append("],\"edges\":[");
            bool first = true;
            for (int e = 0; e < map.edges.Length; e++)
            {
                var ed = map.edges[e];
                if (ed.pts == null || ed.pts.Length < 2) continue;
                var mid = ed.PointAt(ed.length * 0.5f);
                bool near = false;
                foreach (var k in keep) if ((k - mid).sqrMagnitude < 900f * 900f) { near = true; break; }
                if (!near) continue;
                sb.Append(first ? "" : ",");
                first = false;
                sb.Append(string.Format(IC, "{{\"e\":{0},\"ow\":{1},\"cls\":{2},\"link\":{3},\"p\":[", e, ed.oneway ? 1 : 0, ed.cls, ed.link ? 1 : 0));
                for (int i = 0; i < ed.pts.Length; i++) sb.Append(i == 0 ? "" : ",").Append(string.Format(IC, "[{0:0.0},{1:0.0}]", ed.pts[i].x, ed.pts[i].y));
                sb.Append("]}");
            }
            sb.Append("]}");
            File.WriteAllText(Path.Combine(dir, "probe_graph.json"), sb.ToString());
        }

        // ---- every new plan: one-way legality per waypoint, successor legality per change of state
        void CheckPlans(float now)
        {
            foreach (var c in rm.allCars)
            {
                var cc = c != null ? cp.Of(c) : null;
                var pl = cc != null ? cc.planner : null;
                if (pl == null || pl.Path == null || !pl.Path.Routed) continue;
                plansSeen.TryGetValue(c, out int seen);
                if (pl.Rebuilds == seen) continue;
                plansSeen[c] = pl.Rebuilds;
                plansChecked++;
                var P = pl.Path;
                int prevSt = -1, bad1 = 0, bad2 = 0;
                var seq = new StringBuilder();
                int shown = 0;
                for (int i = 0; i < P.Count; i++)
                {
                    int e = P.edgeAt[i];
                    int d = P.dirAt != null ? P.dirAt[i] : 1;
                    if (e < 0 || e >= map.edges.Length) continue;
                    int st = e * 2 + (d == 1 ? 0 : 1);
                    if (map.edges[e].oneway && d != 1) bad1++;
                    if (st != prevSt)
                    {
                        if (prevSt >= 0)
                        {
                            bool ok = false;
                            if (R.eNode[prevSt] == R.sNode[st])
                                for (int k = R.succStart[prevSt]; k < R.succStart[prevSt + 1]; k++) if (R.succ[k] == st) { ok = true; break; }
                            if (!ok)
                            {
                                // the same edge's other direction is a U-turn; a non-adjacent state is a gap
                                if ((prevSt >> 1) == (st >> 1)) bad2++;
                                else if (R.eNode[prevSt] != R.sNode[st]) gapJoins++;
                                else bad2++;
                                if (illegal.Length < 6000)
                                    illegal.AppendLine(string.Format(IC, "  {0:0.0} {1} plan {2}: {3} -> {4} ({5} -> {6}) at wp {7}", now, c.name, pl.Rebuilds,
                                        prevSt, st, map.edges[prevSt >> 1].name, map.edges[e].name, i));
                            }
                        }
                        if (shown++ < 60) seq.Append(e).Append(d == 1 ? "+" : "-").Append(map.edges[e].oneway ? "o" : "").Append(map.edges[e].link ? "L" : "").Append(' ');
                        prevSt = st;
                    }
                }
                illegalOneWay += bad1; illegalTurn += bad2;
                log.WriteLine(string.Format(IC, "PLAN {0:0.0} {1} #{2} {3} wps, one-way against {4}, illegal transitions {5}: {6}", now, c.name, pl.Rebuilds, P.Count, bad1, bad2, seq));
            }
        }

        void DumpGps(float now, CarController player, CheckpointRace.CarCP pc)
        {
            var pts = cp.GpsPts;
            var sb = new StringBuilder();
            var q = player.transform.position;
            var g = cp.gates[pc.next];
            sb.Append(string.Format(IC, "{{\"t\":{0:0.0},\"ver\":{1},\"car\":[{2:0.0},{3:0.0}],\"fwd\":[{4:0.00},{5:0.00}],\"next\":{6},\"gate\":[{7:0.0},{8:0.0}],\"remGate\":{9:0},\"ww\":{10},\"detour\":{11},\"pts\":[",
                now, cp.GpsVersion, q.x, q.z, player.transform.forward.x, player.transform.forward.z, pc.next, g.centre.x, g.centre.z, pc.remainingGate,
                cp.PlayerWrongWay ? 1 : 0, now < gpsLiveUntil ? 1 : 0));
            int step = Mathf.Max(1, pts.Count / 600);
            for (int i = 0; i < pts.Count; i += step) sb.Append(i == 0 ? "" : ",").Append(string.Format(IC, "[{0:0.0},{1:0.0}]", pts[i].x, pts[i].y));
            sb.Append("]}");
            gps.WriteLine(sb.ToString());
        }

        void Detour(CarController car, CheckpointRace.CarCP pc, float now)
        {
            detourDone = true;
            var pos = car.transform.position;
            var routePts = new List<Vector2>();
            for (int i = 0; i < rm.path.Count; i += 5) { var w = rm.path.GetPoint(i); routePts.Add(new Vector2(w.x, w.z)); }
            float bestScore = float.PositiveInfinity; int bestE = -1; int bestDir = 1;
            var F = cp.Cost(pc.next);
            for (int e = 0; e < map.edges.Length; e++)
            {
                var ed = map.edges[e];
                if (ed.cls >= 4 || ed.link || ed.bridge || ed.tunnel || ed.length < 80f || ed.pts == null || ed.pts.Length < 2) continue;
                var mid = ed.PointAt(ed.length * 0.5f);
                float dc = (mid - new Vector2(pos.x, pos.z)).magnitude;
                if (dc < 200f || dc > 450f) continue;
                if (Mathf.Abs(ed.YAt(ed.length * 0.5f) - pos.y) > 12f) continue;
                bool nearRoute = false;
                foreach (var r in routePts) if ((r - mid).sqrMagnitude < 120f * 120f) { nearRoute = true; break; }
                if (nearRoute) continue;
                bool nearGate = false;
                for (int k = 0; k < cp.GateCount; k++) if ((new Vector2(cp.gates[k].centre.x, cp.gates[k].centre.z) - mid).sqrMagnitude < 150f * 150f) { nearGate = true; break; }
                if (nearGate) continue;
                // two-way: the way the GPS points from there (a player turns round to follow it; the autopilot cannot
                // U-turn on a street, so it is set down facing that way)
                int d = 1;
                if (!ed.oneway) R.ValueAt(F, e, ed.length * 0.5f, false, out d);
                int st = e * 2 + (d == 1 ? 0 : 1);
                if (!R.legal[st]) { st ^= 1; d = -d; }
                float t = R.len[st] * 0.5f;
                float v = R.CostFrom(F, st, t);
                if (float.IsInfinity(v) || float.IsNaN(v)) continue;
                // prefer a street whose way back is a real choice of streets: the largest cost back within reason
                float score = Mathf.Abs(dc - 320f) - (ed.oneway ? 60f : 0f);
                if (score < bestScore) { bestScore = score; bestE = e; bestDir = d; }
            }
            if (bestE < 0) { log.WriteLine(string.Format(IC, "DETOUR {0:0.0}: no off-route street found", now)); return; }
            var be = map.edges[bestE];
            float s = be.length * 0.5f;
            var lp = LineModel.LanePoint(be, s);
            var at = new Vector3(lp.x, be.YAt(s), lp.y);
            var tan = be.TangentAt(s) * bestDir;
            var rot = Quaternion.LookRotation(new Vector3(tan.x, 0f, tan.y), Vector3.up);
            float before = cp.RemainingToFinish(car);
            bool placed = DriveSession.TryPlace(car, at, rot);
            if (!placed) { car.ResetTo(at, rot, seated: true); }
            Physics.SyncTransforms();
            // the watch's cause for the progress fall (RaceManager.RaiseRespawned is internal)
            var mi = typeof(RaceManager).GetMethod("RaiseRespawned", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (mi != null) mi.Invoke(rm, new object[] { car, 0, 0f });
            pc.sweptFrom = car.transform.position; pc.loc.valid = false; pc.pathHint = -1; pc.nextLocate = 0f;
            pc.closestRem = float.PositiveInfinity; pc.closestD3 = float.PositiveInfinity;
            if (pc.planner != null) pc.planner.OnRespawned();
            detourAt = now; gpsLiveUntil = now + 40f;
            detourWhat = string.Format(IC, "edge {0} '{1}' cls {2} oneway {3} dir {4}, {5:0} m from the car, {6} placed; remaining {7:0} -> (next sample)",
                bestE, be.name, be.cls, be.oneway, bestDir, (new Vector2(at.x - pos.x, at.z - pos.z)).magnitude, placed ? "TryPlace" : "ResetTo", before);
            log.WriteLine(string.Format(IC, "DETOUR {0:0.0}: gate next {1}; {2}", now, pc.next, detourWhat));
        }

        // ---- world frames (no HUD): the game camera through a render request
        void Frames(float now, CarController player, CheckpointRace.CarCP pc, RaceManager.CarProgress pp)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var g = cp.gates[pc.next];
            Vector3 to = g.centre - player.transform.position; to.y = 0f;
            bool ahead = to.sqrMagnitude > 1f && Vector3.Dot(player.transform.forward, to.normalized) > 0.85f;
            if (!shots.Contains("start") && now > 1f) Shot("start");
            if (pc.passedTotal >= 1 && ahead && pc.remainingGate > 560f && pc.remainingGate < 660f && Time.unscaledTime > pc.flashUntil) Shot("beam_600m");
            if (pc.passedTotal >= 1 && ahead && pc.remainingGate > 260f && pc.remainingGate < 330f) Shot("beam_300m");
            if (pc.passedTotal >= 3 && ahead && pc.remainingGate > 60f && pc.remainingGate < 110f) Shot("gate_80m");
            if (pc.passedTotal >= 3 && ahead && pc.remainingGate > 22f && pc.remainingGate < 45f) Shot("band_35m");
            if (detourAt > 0f && now - detourAt > 2.5f) Shot("detour_street");
            if (detourAt > 0f && now - detourAt > 12f) Shot("detour_12s");
            if (CheckpointRaceWatch.WrongWayTestActive && cp.PlayerWrongWay) Shot("wrongway");
        }

        void Shot(string name)
        {
            if (shots.Contains(name)) return;
            shots.Add(name);
            var cam = Camera.main;
            if (cam == null) { log.WriteLine("shot " + name + ": no main camera"); return; }
            var target = cam.targetTexture;
            int w = target != null ? target.width : 640, h = target != null ? target.height : 360;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            string file = Path.Combine(dir, "probe_" + name + ".png");
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                var shown = PSXScreenshotTool.Dithered(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev == rt || prev == shown ? null : prev;
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                log.WriteLine(string.Format(IC, "shot {0} {1}x{2} cam {3} far {4:0}", name, w, h, cam.name, cam.farClipPlane));
            }
            else log.WriteLine("shot " + name + ": no render request");
            cam.targetTexture = target;
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        void Summary()
        {
            if (log == null) return;
            log.WriteLine(string.Format(IC, "SUMMARY plans checked {0}, one-way waypoints against {1}, illegal transitions {2}, gap joins {3}; detour: {4}",
                plansChecked, illegalOneWay, illegalTurn, gapJoins, detourDone ? detourWhat : "none"));
            if (illegal.Length > 0) log.WriteLine("ILLEGAL:\n" + illegal);
            log.Flush(); log.Close(); gps.Flush(); gps.Close();
            log = null;
        }
    }
}
