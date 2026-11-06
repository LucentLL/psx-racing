using System.Collections;
using System.Collections.Generic;
using System.Text;
using PSXRacing.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE REFUGE ISLANDS, DRIVEN (medians round 5, 2026-11-03): PLAYS
    /// Charlotte's free roam (no traffic) and has an AIDriver race the
    /// player's own car along an arterial through refuge islands - the inner
    /// through lane, the one beside the islands, at the racer's own pace -
    /// judging every physics step:
    ///   KERB  any of the car's four footprint corners standing over 8 cm above
    ///         the lane it drives (an island, a kerb, a nose);
    ///   HIT   a contact with anything but the ground over 1.5 m/s;
    ///   OFF   the car over 2.5 m off its lane line (cutting across an island
    ///         or a lane);
    ///   STUCK under 0.5 m/s for 8 s; RECOVERED (the AI's own recovery).
    /// PSX_LAP_RUNS="name:x0:z0:x1:z1:road;..." the runs (default: Beatties
    /// Ford by the library both ways, and the bend). Writes
    /// PSXRacing_refuge_lap.txt; exit 1 on any failure.
    /// </summary>
    public static class RefugeLapCheck
    {
        internal static readonly StringBuilder log = new StringBuilder();
        internal static int failures;

        [MenuItem("PSX Racing/Check Refuge Island Laps")]
        public static void Run()
        {
            log.Clear(); failures = 0;
            int roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int s = roamIdx >= 0 ? TrackCatalog.SceneIndex(roamIdx) : -1;
            if (s < 0 || s >= scenes.Length || !System.IO.File.Exists(scenes[s].path)) { Fail("Charlotte scene not built"); Finish(); return; }
            EditorSceneManager.OpenScene(scenes[s].path);
            bool ok = PSXRacing.LifeSim.CityFrontEnd.FillFreeRoam(PSXRacing.LifeSim.CityFrontEnd.DefaultCarId(), TimeOfDay.Noon, 0, out int scene);
            Line("free roam handoff " + (ok ? "filled" : "NOT FILLED") + "; refuge islands " + (LineModel.RefugeOn ? "ON" : "OFF (PSX_CITY_REFUGE=0)"));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("RefugeLapRunner").AddComponent<RefugeLapRunner>();
        }

        internal static void Line(string s) { log.AppendLine(s); Debug.Log("[RefugeLap] " + s); }
        /// <summary>
        /// Round 5 test hook, moved here from TrafficSystem (round 6): a two-way
        /// road's traffic system with no race and no cars, its race lanes
        /// centred on the path (the harness's path is the car's own lane line),
        /// so an AIDriver steers as a racer on a city route does - pure
        /// pursuit, the hairpin lookahead cap, lane discipline - not with the
        /// free-roam law. Set up by reflection, so the game's code carries no
        /// test entry (and the same harness runs on live's code).
        /// </summary>
        internal static TrafficSystem SteerOnlyTraffic()
        {
            if (TrafficSystem.Instance != null) return TrafficSystem.Instance;
            var go = new GameObject("Traffic (steer only)");
            var ts = go.AddComponent<TrafficSystem>();
            const System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(TrafficSystem).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { ts });
            ((List<float>)typeof(TrafficSystem).GetField("lanesFwd", NP).GetValue(ts)).Add(0f);
            ((List<float>)typeof(TrafficSystem).GetField("lanesBack", NP).GetValue(ts)).Add(-RoadProfiles.LaneM);
            return ts;
        }

        internal static void Fail(string s) { failures++; Line("  FAIL " + s); }
        internal static void Finish()
        {
            Line(failures == 0 ? "REFUGE LAP OK" : "REFUGE LAP FAILED: " + failures);
            System.IO.File.WriteAllText("PSXRacing_refuge_lap.txt", log.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    /// <summary>Records the car's contacts (not the ground's).</summary>
    public class RefugeLapHits : MonoBehaviour
    {
        public readonly List<string> hits = new List<string>();
        Rigidbody rb;
        void Awake() { rb = GetComponent<Rigidbody>(); }
        void OnCollisionEnter(Collision c)
        {
            if (c.collider == null || c.collider.gameObject.layer == 8) return;
            float dv = rb != null && rb.mass > 0f ? c.impulse.magnitude / rb.mass : c.relativeVelocity.magnitude;
            if (dv < 1.5f) return;
            hits.Add($"{c.collider.name} (layer {c.collider.gameObject.layer}{(c.collider.attachedRigidbody != null ? ", a body" : "")}) dv {dv:0.0} m/s at {transform.position.x:0.0},{transform.position.z:0.0}");
        }
    }

    public class RefugeLapRunner : MonoBehaviour
    {
        // (launch 7) PSX_LAP_HANG: the whole session's limit in seconds (1800 by default; a long move list needs more)
        static readonly float HangSeconds = float.TryParse(System.Environment.GetEnvironmentVariable("PSX_LAP_HANG"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hs) && hs > 0f ? hs : 1800f;
        const float RunTimeout = 240f, KerbM = 0.08f, OffM = 2.5f;
        float bornAt; bool finished;
        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }
        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true; RefugeLapCheck.Fail("HUNG"); RefugeLapCheck.Finish();
        }

        struct RunDef { public string name, road, opt; public Vector2 a, b; public int[] edges; public float kmh, off, back, on; public char lane; }

        /// <summary>
        /// FIVE POINTS ROUND 2 (2026-11-03): a junction's movements, driven.
        /// PSX_LAP_MOVES="name;kmh;lane;off;e1,e2,...;back;on|...": the AIDriver
        /// drives the edges in order (each the way that reaches the next), in
        /// the travel direction's lane L (inner/leftmost) or R (outer/
        /// rightmost) as the line model draws it, shifted off metres to the
        /// right of travel (negative: left), from back metres before the end
        /// of the first edge to on metres into the last, its target speed held
        /// at kmh. Besides the lap's own checks it records every wheel's time
        /// off the ground, the body's vertical speed over the path's grade,
        /// and afterwards reads each wheel's track on the colliders every
        /// 0.1 m: a STEP is a change of over 3 cm between two readings.
        /// </summary>
        static List<RunDef> Moves(string v)
        {
            var l = new List<RunDef>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var t in v.Split('|'))
            {
                var f = t.Split(';');
                if (f.Length < 7) continue;
                var ids = new List<int>();
                foreach (var x in f[4].Split(',')) if (int.TryParse(x.Trim().TrimStart('e'), out int id)) ids.Add(id);
                l.Add(new RunDef { name = f[0].Trim(), kmh = float.Parse(f[1], ci), lane = f[2].Trim().Length > 0 ? f[2].Trim()[0] : 'L', off = float.Parse(f[3], ci),
                                   edges = ids.ToArray(), back = float.Parse(f[5], ci), on = float.Parse(f[6], ci), road = "(movement)", opt = f.Length > 7 ? f[7].Trim() : "" });
            }
            return l;
        }

        static List<RunDef> Runs()
        {
            var mv = System.Environment.GetEnvironmentVariable("PSX_LAP_MOVES");
            if (!string.IsNullOrEmpty(mv)) return Moves(mv);
            var v = System.Environment.GetEnvironmentVariable("PSX_LAP_RUNS");
            if (string.IsNullOrEmpty(v))
                v = "library_s:-3309:9390:-3384:8605:Beatties Ford Road;library_n:-3384:8605:-3309:9390:Beatties Ford Road;" +
                    "bend_n:-3800:13565:-4100:14025:Beatties Ford Road;bend_s:-4100:14025:-3800:13565:Beatties Ford Road";
            var l = new List<RunDef>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var t in v.Split(';'))
            {
                var f = t.Split(':');
                if (f.Length < 6) continue;
                l.Add(new RunDef { name = f[0], a = new Vector2(float.Parse(f[1], ci), float.Parse(f[2], ci)), b = new Vector2(float.Parse(f[3], ci), float.Parse(f[4], ci)), road = f[5] });
            }
            return l;
        }

        IEnumerator Start()
        {
            yield return null; yield return new WaitForFixedUpdate(); yield return null;
            var mode = CityMode.Instance;
            if (mode == null || mode.player == null || mode.world == null || mode.world.Map == null)
            { RefugeLapCheck.Fail("free roam did not start"); finished = true; RefugeLapCheck.Finish(); yield break; }
            var map = mode.world.Map;
            var carGo = mode.player.gameObject;
            var car = carGo.GetComponent<CarController>();
            var rb = carGo.GetComponent<Rigidbody>();
            foreach (var mb in carGo.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            var hits = carGo.AddComponent<RefugeLapHits>();
            RefugeLapCheck.Line($"refuge pairs drawn: {LineModel.Refuges.Count / 2}");
            foreach (var run in Runs()) yield return Lap(run, map, car, rb, carGo, hits);
            finished = true;
            RefugeLapCheck.Finish();
        }

        /// <summary>The route from a to b along the named road, in travel order,
        /// as the inner through lane's line every 2 m (plan).</summary>
        static List<Vector2> Route(CityMap map, RunDef run, out string edges)
        {
            edges = "";
            var pts = new List<Vector2>();
            var want = (run.b - run.a).normalized;
            // the starting edge: the named one nearest a whose travel heads toward b
            CityMap.Edge cur = null; bool fwd = true; float s0 = 0f, best = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e.name != run.road || e.a == e.b) continue;
                for (float s = 0f; s <= e.length; s += 2f)
                {
                    var p = e.PointAt(s); float d = (p - run.a).sqrMagnitude;
                    if (d >= best || d > 60f * 60f) continue;
                    var t = e.TangentAt(s);
                    bool f = Vector2.Dot(t, want) > 0f;
                    if (e.oneway && !f) continue;
                    best = d; cur = e; fwd = f; s0 = s;
                }
            }
            if (cur == null) return pts;
            float s1 = s0; int guard = 0;
            var lastT = fwd ? cur.TangentAt(s1) : -cur.TangentAt(s1);
            while (guard++ < 64)
            {
                edges += $" e{cur.index}";
                float len = cur.length;
                for (float s = s1; fwd ? s <= len : s >= 0f; s += fwd ? 2f : -2f)
                {
                    var p = LineModel.LanePoint(cur, s);
                    var left = LineModel.SectionLeft(cur, s);
                    var rightT = fwd ? -left : left;                     // right of travel
                    float off;
                    if (cur.oneway) off = -0.5f * RoadProfiles.LaneM * Mathf.Max(0, cur.lanes - 1);   // the inner (leftmost) lane
                    else off = cur.turnLane ? RoadProfiles.LaneM : 0.5f * RoadProfiles.LaneM;      // the inner lane of this direction
                    var q = p + rightT * off;
                    pts.Add(q);
                    if ((q - run.b).sqrMagnitude < 12f * 12f) return pts;
                    lastT = fwd ? cur.TangentAt(s) : -cur.TangentAt(s);
                }
                int node = fwd ? cur.b : cur.a;
                CityMap.Edge nx = null; bool nf = true; float bd = 0.5f;
                foreach (int oi in map.nodeEdges[node])
                {
                    var o = map.edges[oi];
                    if (o == cur || o.name != run.road || o.a == o.b) continue;
                    bool of = o.a == node;
                    if (o.oneway && !of) continue;
                    var ot = of ? o.TangentAt(0f) : -o.TangentAt(o.length);
                    float d = Vector2.Dot(ot, lastT);
                    if (d > bd) { bd = d; nx = o; nf = of; }
                }
                if (nx == null) break;
                cur = nx; fwd = nf; s1 = fwd ? 0f : cur.length;
            }
            return pts;
        }

        /// <summary>A movement's lane line (plan, every 2 m, smoothed through
        /// the junctions by corner cutting).</summary>
        /// <summary>The clipX@A-B option's offset at metre m of the route (0 without it).</summary>
        static float ClipDrift(string opt, int m)
        {
            if (string.IsNullOrEmpty(opt)) return 0f;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var o in opt.Split(','))
            {
                if (!o.StartsWith("clip")) continue;
                var p = o.Substring(4).Split('@');
                if (p.Length != 2) continue;
                // clipX@A-B (out and hold) or clipX@A-B-C-D (out, hold, back between C and D)
                var r = p[1].Split('-');
                if ((r.Length != 2 && r.Length != 4) || !float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out float x)
                    || !float.TryParse(r[0], System.Globalization.NumberStyles.Float, ci, out float a) || !float.TryParse(r[1], System.Globalization.NumberStyles.Float, ci, out float b) || b <= a) continue;
                float k = LineModel.Smooth((m - a) / (b - a));
                if (r.Length == 4 && float.TryParse(r[2], System.Globalization.NumberStyles.Float, ci, out float c) && float.TryParse(r[3], System.Globalization.NumberStyles.Float, ci, out float d) && d > c)
                    k *= 1f - LineModel.Smooth((m - c) / (d - c));
                return x * k;
            }
            return 0f;
        }

        static List<Vector2> MoveRoute(CityMap map, RunDef run, out string edges)
        {
            edges = "";
            var raw = new List<Vector2>();
            var joins = new List<int>(run.edges.Length);
            var caps = new List<float>(run.edges.Length);
            var kerbs = new List<List<Vector2>>(run.edges.Length);
            var swing = new List<float>(run.edges.Length);
            int n = run.edges.Length;
            for (int i = 0; i < n; i++)
            {
                var e = map.edges[run.edges[i]];
                edges += $" e{e.index}";
                if (i > 0 && raw.Count > 0)
                {
                    joins.Add(raw.Count - 1);
                    var pv = map.edges[run.edges[i - 1]];
                    int nd = pv.b == e.a || pv.a == e.a ? e.a : e.b;
                    caps.Add(CityMode.JoinCap(map, CityWorld.TrimsFor(map), pv, e, nd));
                    kerbs.Add(CityMode.JoinKerbs(map, CityWorld.TrimsFor(map), nd));
                    swing.Add(CityMode.JoinSwingRoom(pv, e));
                }
                bool fwd;
                if (i + 1 < n) { var nx = map.edges[run.edges[i + 1]]; fwd = e.b == nx.a || e.b == nx.b; }
                else if (i > 0) { var pv = map.edges[run.edges[i - 1]]; fwd = e.a == pv.a || e.a == pv.b; }
                else fwd = true;
                float x0 = i == 0 ? Mathf.Max(0f, e.length - run.back) : 0f, x1 = i == n - 1 ? Mathf.Min(e.length, run.on) : e.length;
                float L = RoadProfiles.LaneM;
                for (float x = x0; x <= x1 + 1e-3f; x += 1f)
                {
                    float s = fwd ? Mathf.Min(x, e.length) : Mathf.Max(0f, e.length - x);
                    var p = LineModel.LanePoint(e, s);
                    var left = LineModel.SectionLeft(e, s);
                    var rightT = fwd ? -left : left;
                    float off;
                    if (e.oneway) { float k = Mathf.Max(0, e.lanes - 1) * 0.5f * L; off = run.lane == 'R' ? k : -k; }
                    else
                    {
                        int per = Mathf.Max(1, e.lanes / 2);
                        float inner = e.turnLane ? L : 0.5f * L;
                        off = run.lane == 'R' ? inner + (per - 1) * L : inner;
                    }
                    // (curbs 11-05) option clipX@A-B: a deliberate kerb clip - the line
                    // eases a further X m to the right of travel (negative: left) between
                    // A and B metres along the route, and holds it
                    float drift = ClipDrift(run.opt, raw.Count);
                    var q = p + rightT * (off + run.off + drift);
                    if (raw.Count == 0 || (q - raw[raw.Count - 1]).sqrMagnitude > 0.25f) raw.Add(q);
                }
            }
            // round 4: the game's own turn path (CityMode.FilletTurns, the race
            // paths' rule): a turn at a junction on a radius, as the AI drives it
            // (launch-queens2 11-05) option hpR: a driver's hairpin instead - the
            // two lane lines (8 m either side of the single join) joined by one arc
            // of R m tangent to both, in place of the game's turn path (whose kerb
            // and cap rules leave a 140 deg turn an arc under the car's own lock)
            float hpR = 0f;
            foreach (var o in (run.opt ?? "").Split(','))
                if (o.StartsWith("hp") && float.TryParse(o.Substring(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hv)) hpR = hv;
            if (hpR > 0f && joins.Count == 1 && joins[0] >= 9 && joins[0] + 8 < raw.Count)
            {
                int j = joins[0];
                Vector2 a0 = raw[j - 9], a1 = raw[j - 1], b0 = raw[j], b1 = raw[j + 8];
                Vector2 dA = (a1 - a0).normalized, dB = (b1 - b0).normalized;
                float cr = dA.x * dB.y - dA.y * dB.x, th = Mathf.Acos(Mathf.Clamp(Vector2.Dot(dA, dB), -1f, 1f));
                // the lines' crossing: a1 + dA u = b0 + dB v
                float den = dA.x * dB.y - dA.y * dB.x;
                if (Mathf.Abs(den) > 1e-3f)
                {
                    var w0 = b0 - a1; float u = (w0.x * dB.y - w0.y * dB.x) / den;
                    var X = a1 + dA * u; float T = hpR * Mathf.Tan(th * 0.5f);
                    Vector2 tA = X - dA * T, tB = X + dB * T;
                    var nA = cr > 0f ? new Vector2(-dA.y, dA.x) : new Vector2(dA.y, -dA.x);
                    var C = tA + nA * hpR;
                    var outPts = new List<Vector2>();
                    foreach (var p in raw.GetRange(0, j)) if (Vector2.Dot(p - tA, dA) < 0f) outPts.Add(p);
                    float g0 = Mathf.Atan2(tA.y - C.y, tA.x - C.x), sweep = th * (cr > 0f ? 1f : -1f);
                    int na = Mathf.Max(4, Mathf.CeilToInt(hpR * th));
                    for (int k = 0; k <= na; k++) { float g = g0 + sweep * k / na; outPts.Add(C + new Vector2(Mathf.Cos(g), Mathf.Sin(g)) * hpR); }
                    foreach (var p in raw.GetRange(j, raw.Count - j)) if (Vector2.Dot(p - tB, dB) > 0f) outPts.Add(p);
                    RefugeLapCheck.Line($"  turn path: hairpin {th * Mathf.Rad2Deg:0} deg on an arc of {hpR:0.0} m, centre {C.x:0.0},{C.y:0.0}, tangents {tA.x:0.0},{tA.y:0.0} / {tB.x:0.0},{tB.y:0.0}");
                    raw = outPts; joins.Clear(); caps.Clear(); kerbs.Clear(); swing.Clear();
                }
            }
            int fil = joins.Count == 0 ? 0 : CityMode.FilletTurns(raw, null, joins, CityMode.TurnFilletR, CityMode.TurnFilletMinDeg, caps, kerbs, swing);
            if (fil > 0) RefugeLapCheck.Line($"  turn path: {fil} junction corner(s) on a radius");
            foreach (var note in CityMode.LastFilletNotes) RefugeLapCheck.Line("  turn path: " + note);
            if (System.Environment.GetEnvironmentVariable("PSX_LAP_TRACE") == "1")
                for (int k = 0; k < kerbs.Count; k++)
                {
                    if (kerbs[k] == null) continue;
                    var sb = new System.Text.StringBuilder($"  KERBS join {k}:");
                    for (int i = 0; i + 1 < kerbs[k].Count; i += 2) sb.Append($" {kerbs[k][i].x:0.0},{kerbs[k][i].y:0.0}>{kerbs[k][i + 1].x:0.0},{kerbs[k][i + 1].y:0.0}");
                    RefugeLapCheck.Line(sb.ToString());
                }
            // corner cutting: a car's line through each junction, not the lanes' kinks
            for (int it = 0; it < 4 && raw.Count > 2; it++)
            {
                var sm = new List<Vector2>(raw.Count * 2) { raw[0] };
                for (int k = 0; k + 1 < raw.Count; k++)
                {
                    var a = raw[k]; var b = raw[k + 1];
                    sm.Add(Vector2.Lerp(a, b, 0.25f)); sm.Add(Vector2.Lerp(a, b, 0.75f));
                }
                sm.Add(raw[raw.Count - 1]);
                raw = sm;
            }
            // even 2 m
            var pts = new List<Vector2> { raw[0] };
            float acc = 0f;
            for (int k = 1; k < raw.Count; k++)
            {
                acc += Vector2.Distance(raw[k - 1], raw[k]);
                if (acc >= 2f) { pts.Add(raw[k]); acc = 0f; }
            }
            // (launch 7) option mountX@A: a straight-on curb mount - the plan is a
            // line square across the route at metre A, from 6 m left of the lane
            // line to X m right of it, so the car meets the right curb head on
            foreach (var o in (run.opt ?? "").Split(','))
            {
                if (!o.StartsWith("mount")) continue;
                var mp = o.Substring(5).Split('@');
                var ciM = System.Globalization.CultureInfo.InvariantCulture;
                if ((mp.Length != 2 && mp.Length != 3) || !float.TryParse(mp[0], System.Globalization.NumberStyles.Float, ciM, out float mx)
                    || !float.TryParse(mp[1], System.Globalization.NumberStyles.Float, ciM, out float ma) || pts.Count < 3) continue;
                int mi = Mathf.Clamp(Mathf.RoundToInt(ma / 2f), 1, pts.Count - 2);
                var P = pts[mi]; var T = (pts[mi + 1] - pts[mi - 1]).normalized; var R = new Vector2(T.y, -T.x);
                var line = new List<Vector2>();
                float ms = mp.Length == 3 && float.TryParse(mp[2], System.Globalization.NumberStyles.Float, ciM, out float msv) ? -Mathf.Abs(msv) : -6f;   // mountX@A@S: start S m left
                for (float d = ms; d <= mx + 1e-3f; d += 0.5f) line.Add(P + R * d);
                RefugeLapCheck.Line($"  mount: square across the route at {P.x:0.0},{P.y:0.0}, {ms:0.0} .. +{mx:0.0} m right of the lane line");
                return line;
            }
            return pts;
        }

        /// <summary>The top collider surface under a plan point (no body's) no
        /// higher than <paramref name="below"/>, or NaN. (launch 7) Under a deck
        /// the top surface is the deck, not what the wheel stands on: the wheel
        /// tracks pass the wheel's own height + 1.5 m.</summary>
        static float TopAt(Vector2 q, out string what, float below = float.PositiveInfinity)
        {
            what = "";
            float best = float.NaN;
            // the ray starts at the limit: RaycastAll reports one hit per collider,
            // and a tile's one Roads collider holds both the deck and the street under it
            float top = Mathf.Min(400f, below);
            foreach (var h in Physics.RaycastAll(new Vector3(q.x, top, q.y), Vector3.down, top + 400f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.attachedRigidbody != null) continue;
                if (float.IsNaN(best) || h.point.y > best) { best = h.point.y; what = h.collider.name; }
            }
            return best;
        }

        /// <summary>(launch 7) Every route point's road surface by continuity:
        /// from the first point with one road under it, each point takes the
        /// road hit nearest its neighbour's height (a street under a deck keeps
        /// the street). Points on drawn ground keep theirs. The count changed.</summary>
        static int RouteOnItsOwnRoad(List<Vector2> plan, Vector3[] w)
        {
            int n = plan.Count, seed = -1, changed = 0;
            var cand = new List<float>[n];
            for (int i = 0; i < n; i++)
            {
                cand[i] = new List<float>(2);
                // ray after ray down through every road surface (RaycastAll gives one
                // hit per collider, and one tile's Roads collider holds deck and street)
                float from = 400f;
                for (int g = 0; g < 8 && Physics.Raycast(new Vector3(plan[i].x, from, plan[i].y), Vector3.down, out var h, from + 400f, 1 << 8, QueryTriggerInteraction.Ignore); g++)
                {
                    if (h.collider.attachedRigidbody == null) cand[i].Add(h.point.y);
                    from = h.point.y - 0.05f;
                }
                if (seed < 0 && cand[i].Count == 1) seed = i;
            }
            if (seed < 0) return 0;
            void Pick(int i, float near)
            {
                if (cand[i].Count < 2) return;
                float best = cand[i][0];
                foreach (float y in cand[i]) if (Mathf.Abs(y - near) < Mathf.Abs(best - near)) best = y;
                if (Mathf.Abs(best - w[i].y) > 0.01f) { changed++; w[i].y = best; }
            }
            for (int i = seed + 1; i < n; i++) Pick(i, w[i - 1].y);
            for (int i = seed - 1; i >= 0; i--) Pick(i, w[i + 1].y);
            return changed;
        }

        IEnumerator Lap(RunDef run, CityMap map, CarController car, Rigidbody rb, GameObject carGo, RefugeLapHits hits)
        {
            // test options (an 8th move field, comma separated): fil0 = no turn
            // arcs, kerb0 = the tangent cap alone, capX = a fixed tangent cap,
            // clrX = the kerb clearance, minrX = the smallest arc, tmaxX = the
            // longest tangent, stepX = the swing step
            bool filWas = CityMode.TurnFilletOn, kerbWas = CityMode.TurnKerbsOn;
            float capWas = CityMode.TurnFilletCapM, clrWas = CityMode.TurnKerbClearM, minrWas = CityMode.TurnFilletMinR, stepWas = CityMode.TurnSwingStepM, tmaxWas = CityMode.TurnTangentMaxM, pastWas = CityMode.TurnFilletPastTrimM;
            var ciO = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var o in (run.opt ?? "").Split(','))
            {
                if (o == "fil0") CityMode.TurnFilletOn = false;
                else if (o == "kerb0") CityMode.TurnKerbsOn = false;
                else if (o.StartsWith("cap") && float.TryParse(o.Substring(3), System.Globalization.NumberStyles.Float, ciO, out float cv)) CityMode.TurnFilletCapM = cv;
                else if (o.StartsWith("clr") && float.TryParse(o.Substring(3), System.Globalization.NumberStyles.Float, ciO, out float kv)) CityMode.TurnKerbClearM = kv;
                else if (o.StartsWith("tmax") && float.TryParse(o.Substring(4), System.Globalization.NumberStyles.Float, ciO, out float tv)) CityMode.TurnTangentMaxM = tv;
                else if (o.StartsWith("step") && float.TryParse(o.Substring(4), System.Globalization.NumberStyles.Float, ciO, out float sv)) CityMode.TurnSwingStepM = sv;
                else if (o.StartsWith("minr") && float.TryParse(o.Substring(4), System.Globalization.NumberStyles.Float, ciO, out float mv)) CityMode.TurnFilletMinR = mv;
                else if (o.StartsWith("past") && float.TryParse(o.Substring(4), System.Globalization.NumberStyles.Float, ciO, out float pv)) CityMode.TurnFilletPastTrimM = pv;
            }
            if (!string.IsNullOrEmpty(run.opt)) RefugeLapCheck.Line($"  options: {run.opt}");
            var plan = run.edges != null ? MoveRoute(map, run, out string edges) : Route(map, run, out edges);
            CityMode.TurnFilletOn = filWas; CityMode.TurnKerbsOn = kerbWas; CityMode.TurnFilletCapM = capWas; CityMode.TurnKerbClearM = clrWas; CityMode.TurnFilletMinR = minrWas; CityMode.TurnSwingStepM = stepWas; CityMode.TurnTangentMaxM = tmaxWas; CityMode.TurnFilletPastTrimM = pastWas;
            bool trace = System.Environment.GetEnvironmentVariable("PSX_LAP_TRACE") == "1";
            var traceSb = trace ? new System.Text.StringBuilder() : null;
            int traceTick = 0;
            if (plan.Count < 20) { RefugeLapCheck.Fail($"{run.name}: no route along '{run.road}' ({plan.Count} points)"); yield break; }
            // park at the start and wait for the tiles along the route
            var start = new Vector3(plan[0].x, 0f, plan[0].y);
            rb.isKinematic = true;
            var look = new Vector3(plan[2].x, 0f, plan[2].y) - start;
            car.TeleportTo(start + Vector3.up * 200f, Quaternion.LookRotation(look.normalized, Vector3.up));
            float t0 = Time.realtimeSinceStartup;
            var w = new Vector3[plan.Count];
            bool all = false;
            while (Time.realtimeSinceStartup - t0 < 180f && !all)
            {
                yield return null;
                all = true;
                var onRoad = new bool[plan.Count];
                for (int i = 0; i < plan.Count; i++)
                    if (Physics.Raycast(new Vector3(plan[i].x, 400f, plan[i].y), Vector3.down, out var h, 800f, 1 << 8, QueryTriggerInteraction.Ignore)) { w[i] = h.point; onRoad[i] = true; }
                // publish 1: a movement's smoothed line may cross a few metres of
                // drawn ground (Trade NB -> Beatties Ford NB kinks 23 deg at n10966
                // and its lane line passes over the Beatties Ford median nose). The
                // tile has streamed when a road stands within 6 points either side
                // and a tile's Ground is under the point: take that ground (the
                // drive itself judges the steps), and say so in the log.
                int offRoad = 0;
                for (int i = 0; i < plan.Count && all; i++)
                {
                    if (onRoad[i]) continue;
                    bool near = false;
                    int win = (run.opt ?? "").Contains("mount") ? plan.Count : 6;   // a mount's line runs on across the walk and its backing
                    for (int j = Mathf.Max(0, i - win); j <= Mathf.Min(plan.Count - 1, i + win) && !near; j++) near = onRoad[j];
                    if (near && Physics.Raycast(new Vector3(plan[i].x, 400f, plan[i].y), Vector3.down, out var g, 800f, ~0, QueryTriggerInteraction.Ignore) && (g.collider.name == "Ground" || win > 6)) { w[i] = g.point; offRoad++; }
                    else all = false;
                }
                if (all && offRoad > 0) RefugeLapCheck.Line($"  NOTE {run.name}: {offRoad} of {plan.Count} route points stand on drawn Ground, not a road (the line crosses a nose or verge)");
                // the tiles stream round the car: move it along the route while waiting
                if (!all) { int k = Mathf.Min(plan.Count - 1, (int)((Time.realtimeSinceStartup - t0) / 4f) * Mathf.Max(25, plan.Count / 10)); car.TeleportTo(new Vector3(plan[k].x, 200f, plan[k].y), Quaternion.LookRotation(look.normalized, Vector3.up)); }
            }
            if (!all)
            {
                int miss = -1;
                for (int i = 0; i < plan.Count && miss < 0; i++)
                    if (!Physics.Raycast(new Vector3(plan[i].x, 400f, plan[i].y), Vector3.down, out _, 800f, 1 << 8, QueryTriggerInteraction.Ignore)) miss = i;
                string what = "";
                if (miss >= 0 && Physics.Raycast(new Vector3(plan[miss].x, 400f, plan[miss].y), Vector3.down, out var any, 800f, ~0, QueryTriggerInteraction.Ignore))
                    what = $"; any layer there: {any.collider.name} (layer {any.collider.gameObject.layer}) y {any.point.y:0.00}";
                RefugeLapCheck.Fail($"{run.name}: the route's ground never streamed in (point {miss} of {plan.Count} at {(miss >= 0 ? plan[miss].x.ToString("0.0") + "," + plan[miss].y.ToString("0.0") : "?")}{what})");
                yield break;
            }
            // (launch 7) a street under a deck: the route stands on the road it
            // drives, not the top one (the top road over N Tryon at the I-277
            // ramps is the freeway deck 9 m up - the route's grade, the AI's
            // path and the body's vertical speed all read the deck there)
            int kept = RouteOnItsOwnRoad(plan, w);
            if (kept > 0) RefugeLapCheck.Line($"  NOTE {run.name}: {kept} of {plan.Count} route points stand under another road; the route keeps its own (the nearest road surface to its neighbour's)");
            car.TeleportTo(w[0] + Vector3.up * 0.5f, Quaternion.LookRotation(new Vector3(w[2].x - w[0].x, 0f, w[2].z - w[0].z).normalized, Vector3.up));
            for (int i = 0; i < 20; i++) yield return null;
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            // the path: even 4 m, curvature as the race paths read it
            var even = DeckLapCheck.Even(w, 4f);
            int n = even.Length;
            var go = new GameObject("RefugeLapPath");
            var tp = go.AddComponent<TrackPath>();
            var curv = new float[n];
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 a = even[i - 1], b = even[i], c = even[i + 1];
                a.y = b.y = c.y = 0f;
                float len = Mathf.Max(0.5f, 0.5f * (Vector3.Distance(a, b) + Vector3.Distance(b, c)));
                curv[i] = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad / len;
            }
            tp.waypoints = even; tp.curvatures = curv; tp.spacing = 4f; tp.roadWidth = 3.5f; tp.drag = false; tp.pointToPoint = true;
            // round 5: PSX_LAP_RACESTEER=1 - a traffic system present (no cars), so
            // the AI steers by the race law (pure pursuit, hairpin lookahead cap,
            // lane discipline) as a city racer does, not the free-roam law
            TrafficSystem steerTs = System.Environment.GetEnvironmentVariable("PSX_LAP_RACESTEER") == "1" ? RefugeLapCheck.SteerOnlyTraffic() : null;
            var ai = carGo.AddComponent<AIDriver>();
            ai.path = tp; ai.skill = 0.95f;
            if (run.kmh > 0f) ai.speedCapMps = run.kmh / 3.6f;
            var tracks = new List<Vector2>[4]; for (int k = 0; k < 4; k++) tracks[k] = new List<Vector2>(512);
            var tracksY = new List<float>[4]; for (int k = 0; k < 4; k++) tracksY[k] = new List<float>(512);
            // (launch 7) option diag: each wheel-off tick also logs the surface under
            // each corner over the path's height there, and the run ends with both
            // front wheels' surface profiles over the path (where it changes)
            bool diag = System.Array.IndexOf((run.opt ?? "").Split(','), "diag") >= 0;
            float PathY(Vector2 q)
            {
                int bj = 0; float bd = float.MaxValue;
                for (int i = 0; i + 1 < n; i++)
                {
                    Vector2 a = new Vector2(even[i].x, even[i].z), b = new Vector2(even[i + 1].x, even[i + 1].z);
                    var ab2 = b - a; float u = Mathf.Clamp01(Vector2.Dot(q - a, ab2) / Mathf.Max(1e-4f, ab2.sqrMagnitude));
                    float d = (q - (a + ab2 * u)).sqrMagnitude;
                    if (d < bd) { bd = d; bj = i; }
                }
                Vector2 A1 = new Vector2(even[bj].x, even[bj].z), B1 = new Vector2(even[bj + 1].x, even[bj + 1].z);
                float u1 = Mathf.Clamp01(Vector2.Dot(q - A1, B1 - A1) / Mathf.Max(1e-4f, (B1 - A1).sqrMagnitude));
                return Mathf.Lerp(even[bj].y, even[bj + 1].y, u1);
            }
            float anyOff = 0f, allOff = 0f, maxVy = 0f, vTop = 0f; string offWhere = "", vyWhere = "";
            var offLog = new System.Text.StringBuilder();
            hits.hits.Clear();
            float t = 0f, slowFor = 0f, maxOff = 0f, vMax = 0f, worstKerb = 0f; string offAt = "", kerbAt = "";
            int idx = 0, bestI = 0, kerbs = 0, offs = 0, recov = 0;
            string lastWhy = ai.LastRecoveryWhy;
            bool done = false, stuck = false;
            var corners = new[] { new Vector3(-0.8f, 0f, 1.6f), new Vector3(0.8f, 0f, 1.6f), new Vector3(-0.8f, 0f, -1.4f), new Vector3(0.8f, 0f, -1.4f) };
            while (t < RunTimeout)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var p = rb.position;
                if (traceSb != null && (traceTick++ % 10) == 0) traceSb.Append($" {p.x:0.0},{p.z:0.0}");
                vMax = Mathf.Max(vMax, rb.linearVelocity.magnitude);
                idx = tp.NearestIndex(p, idx, 12);
                if (idx > bestI) bestI = idx;
                // lateral off the lane line: to the segment idx..idx+1 (plan)
                int j = Mathf.Min(n - 2, idx);
                Vector2 A = new Vector2(even[j].x, even[j].z), B = new Vector2(even[j + 1].x, even[j + 1].z), P = new Vector2(p.x, p.z);
                if (j > 0) { Vector2 A0 = new Vector2(even[j - 1].x, even[j - 1].z); if ((P - A0).sqrMagnitude < (P - B).sqrMagnitude) { B = A; A = A0; } }
                var ab = B - A; float tt = Mathf.Clamp01(Vector2.Dot(P - A, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                float off = (P - (A + ab * tt)).magnitude;
                if (off > maxOff) { maxOff = off; offAt = $"{p.x:0.0},{p.z:0.0}"; }
                if (off > OffM) offs++;
                // the four corners over the lane
                foreach (var cn in corners)
                {
                    var wpos = carGo.transform.TransformPoint(cn);
                    if (!Physics.Raycast(new Vector3(wpos.x, p.y + 2f, wpos.z), Vector3.down, out var h, 6f, 1 << 8, QueryTriggerInteraction.Ignore)) continue;
                    if (h.collider.attachedRigidbody == rb) continue;
                    // the lane's own height where the corner is (the path's y at the
                    // corner's place along it - a grade is not a kerb)
                    int jc = Mathf.Min(n - 2, tp.NearestIndex(wpos, idx, 6));
                    Vector2 Ac = new Vector2(even[jc].x, even[jc].z), Bc = new Vector2(even[jc + 1].x, even[jc + 1].z), Pc = new Vector2(wpos.x, wpos.z);
                    if (jc > 0) { var A0c = new Vector2(even[jc - 1].x, even[jc - 1].z); if ((Pc - A0c).sqrMagnitude < (Pc - Bc).sqrMagnitude) { jc--; Bc = Ac; Ac = A0c; } }
                    var abc = Bc - Ac; float tc = Mathf.Clamp01(Vector2.Dot(Pc - Ac, abc) / Mathf.Max(1e-4f, abc.sqrMagnitude));
                    float yC = Mathf.Lerp(even[jc].y, even[jc + 1].y, tc);
                    float up = h.point.y - yC;
                    if (up > worstKerb) { worstKerb = up; kerbAt = $"{wpos.x:0.0},{wpos.z:0.0} on {h.collider.name}"; }
                    if (up > KerbM) kerbs++;
                }
                if (run.edges != null)
                {
                    int grounded = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        if (car.wheelContacts[k].grounded) grounded++;
                        var wp = carGo.transform.TransformPoint(corners[k]);
                        tracks[k].Add(new Vector2(wp.x, wp.z)); tracksY[k].Add(wp.y);
                    }
                    if (grounded < 4 && t > 1f)
                    {
                        anyOff += Time.fixedDeltaTime; if (offWhere.Length == 0) offWhere = $"{p.x:0.0},{p.z:0.0}";
                        // round 5: which wheel, how fast, how hard the car was turning, what lock
                        if (offLog.Length < (diag ? 6000 : 1400))
                        {
                            string ws = ""; for (int k = 0; k < 4; k++) if (!car.wheelContacts[k].grounded)
                            {
                                // (launch-queens 11-05) what stands under the lifted wheel, and
                                // the surface under its axle partner
                                var wq = carGo.transform.TransformPoint(corners[k]); var wo = carGo.transform.TransformPoint(corners[k ^ 1]);
                                float yq = TopAt(new Vector2(wq.x, wq.z), out string wn), yo = TopAt(new Vector2(wo.x, wo.z), out _);
                                ws += $"{k}({wn} {yq - yo:+0.000;-0.000})";
                            }
                            var vh = rb.linearVelocity; vh.y = 0f;
                            string under = "";
                            if (diag)
                                for (int k = 0; k < 4; k++)
                                {
                                    var wp = carGo.transform.TransformPoint(corners[k]); var q2 = new Vector2(wp.x, wp.z);
                                    float sy = TopAt(q2, out string wn, wp.y + 1.5f);
                                    under += $" h{k}{(float.IsNaN(sy) ? "none" : (sy - PathY(q2)).ToString("+0.00;-0.00"))}{(wn.Length > 0 ? wn.Substring(0, 1) : "")}";
                                }
                            offLog.Append($" [t{t:0.00} w{ws} {p.x:0.0},{p.z:0.0} {vh.magnitude * 3.6f:0}kmh lat{rb.angularVelocity.y * vh.magnitude / 9.81f:0.00}g steer{car.SteerAngleDeg:0}deg roll{Vector3.SignedAngle(Vector3.up, carGo.transform.up, carGo.transform.forward):0.0} vy{rb.linearVelocity.y:+0.00;-0.00}{under}]");
                        }
                    }
                    if (grounded == 0 && t > 1f) allOff += Time.fixedDeltaTime;
                    // the body's vertical speed over the path's own grade there
                    int jg = Mathf.Min(n - 2, idx);
                    float runH = Vector2.Distance(new Vector2(even[jg].x, even[jg].z), new Vector2(even[jg + 1].x, even[jg + 1].z));
                    float grade = runH > 0.1f ? (even[jg + 1].y - even[jg].y) / runH : 0f;
                    var v3 = rb.linearVelocity;
                    float vy = Mathf.Abs(v3.y - grade * new Vector2(v3.x, v3.z).magnitude);
                    if (t > 1f && vy > maxVy) { maxVy = vy; vyWhere = $"{p.x:0.0},{p.z:0.0}"; }
                    vTop = Mathf.Max(vTop, v3.magnitude);
                }
                slowFor = rb.linearVelocity.magnitude < 0.5f ? slowFor + Time.fixedDeltaTime : 0f;
                if (ai.LastRecoveryWhy != lastWhy) { recov++; lastWhy = ai.LastRecoveryWhy; RefugeLapCheck.Line($"  {run.name}: RECOVERED at wp {idx}: {lastWhy}"); }
                if (slowFor > 8f) { stuck = true; break; }
                if (bestI >= ((run.opt ?? "").Contains("mount") ? n - 1 : n - 3)) { done = true; break; }   // a mount runs its line to the end
            }
            Object.Destroy(ai); Object.Destroy(go);
            if (steerTs != null) Object.DestroyImmediate(steerTs.gameObject);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            float pathM = 0f; for (int i = 1; i < n; i++) pathM += Vector3.Distance(even[i - 1], even[i]);
            RefugeLapCheck.Line($"{run.name} along '{run.road}' ({pathM:0} m,{edges}): {(done ? "COMPLETE" : "INCOMPLETE")} in {t:0.0} s, top {vMax * 3.6f:0} km/h, avg {pathM / Mathf.Max(0.1f, t) * 3.6f:0} km/h; " +
                                $"max off its lane line {maxOff:0.00} m at {offAt} ({offs} steps over {OffM} m); highest corner over the lane {worstKerb:0.000} m at {kerbAt} ({kerbs} corner-steps over {KerbM} m); " +
                                $"hits {hits.hits.Count}{(hits.hits.Count > 0 ? ": " + string.Join("; ", hits.hits) : "")}; recoveries {recov}{(stuck ? "; STUCK" : "")}");
            if (run.edges != null)
            {
                // every wheel's track on the colliders, every 0.1 m
                float worstStep = 0f; string stepAt = ""; int stepsOver = 0;
                for (int k = 0; k < 4; k++)
                {
                    var tr = tracks[k]; var trY = tracksY[k];
                    float prevY = float.NaN, carry = 0f;
                    for (int j = 1; j < tr.Count; j++)
                    {
                        var a = tr[j - 1]; var b = tr[j]; float L = Vector2.Distance(a, b);
                        if (L < 1e-4f) continue;
                        float u = carry;
                        for (; u < L; u += 0.1f)
                        {
                            var q = Vector2.Lerp(a, b, u / L);
                            float y = TopAt(q, out string what, Mathf.Lerp(trY[j - 1], trY[j], u / L) + 1.5f);
                            if (!float.IsNaN(y) && !float.IsNaN(prevY))
                            {
                                float st = Mathf.Abs(y - prevY);
                                if (st > 0.03f) stepsOver++;
                                if (st > worstStep) { worstStep = st; stepAt = $"wheel {k} ({q.x:0.00},{q.y:0.00}) {(y > prevY ? "up" : "down")} onto {what}"; }
                            }
                            prevY = y;
                        }
                        carry = u - L;
                    }
                }
                RefugeLapCheck.Line($"  MOVE {run.name} @{run.kmh:0} km/h lane {run.lane}{(run.off != 0f ? $" {run.off:+0.0;-0.0} m" : "")}: top {vTop * 3.6f:0} km/h; any wheel off {anyOff:0.00} s{(offWhere.Length > 0 ? " (first at " + offWhere + ")" : "")}, all four off {allOff:0.00} s; max vertical speed over the grade {maxVy:0.00} m/s at {vyWhere}; largest step under a wheel {worstStep:0.000} m at {stepAt} ({stepsOver} readings over 3 cm)");
                if (traceSb != null) RefugeLapCheck.Line($"  TRACE {run.name}:{traceSb}");
                if (offLog.Length > 0) RefugeLapCheck.Line($"  WHEEL OFF {run.name}:{offLog}");
                if (diag)
                    for (int k = 0; k < 2; k++)
                    {
                        // metres along the wheel's track: surface over the path (cm) and collider, where it changes 2 cm+
                        var tr = tracks[k]; var trY = tracksY[k];
                        var sb = new System.Text.StringBuilder($"  PROFILE {run.name} wheel {k}:");
                        float along = 0f, nextAt = 0f, last = float.NaN; string lastW = "";
                        for (int j = 1; j < tr.Count && sb.Length < 6000; j++)
                        {
                            along += Vector2.Distance(tr[j - 1], tr[j]);
                            if (along < nextAt) continue;
                            nextAt = along + 0.25f;
                            float sy = TopAt(tr[j], out string wn, trY[j] + 1.5f);
                            float h = float.IsNaN(sy) ? float.NaN : sy - PathY(tr[j]);
                            if (!float.IsNaN(h) && (float.IsNaN(last) || Mathf.Abs(h - last) >= 0.02f || wn != lastW))
                            { sb.Append($" {along:0.0}:{h * 100f:0}{(wn != lastW ? wn.Substring(0, Mathf.Min(1, wn.Length)) : "")}"); last = h; lastW = wn; }
                        }
                        RefugeLapCheck.Line(sb.ToString());
                    }
                if (!done || stuck || hits.hits.Count > 0 || allOff > 0f || anyOff > 0.05f || worstStep > 0.03f) RefugeLapCheck.Fail($"{run.name}: not clean");
            }
            else if (!done || stuck || offs > 0 || kerbs > 0 || hits.hits.Count > 0 || recov > 0) RefugeLapCheck.Fail($"{run.name}: not clean");
        }
    }
}
