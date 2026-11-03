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
        const float HangSeconds = 1800f, RunTimeout = 240f, KerbM = 0.08f, OffM = 2.5f;
        float bornAt; bool finished;
        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }
        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true; RefugeLapCheck.Fail("HUNG"); RefugeLapCheck.Finish();
        }

        struct RunDef { public string name, road; public Vector2 a, b; }

        static List<RunDef> Runs()
        {
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

        IEnumerator Lap(RunDef run, CityMap map, CarController car, Rigidbody rb, GameObject carGo, RefugeLapHits hits)
        {
            var plan = Route(map, run, out string edges);
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
                for (int i = 0; i < plan.Count; i++)
                {
                    if (Physics.Raycast(new Vector3(plan[i].x, 400f, plan[i].y), Vector3.down, out var h, 800f, 1 << 8, QueryTriggerInteraction.Ignore)) w[i] = h.point;
                    else { all = false; break; }
                }
                // the tiles stream round the car: move it along the route while waiting
                if (!all) { int k = Mathf.Min(plan.Count - 1, (int)((Time.realtimeSinceStartup - t0) / 4f) * 25); car.TeleportTo(new Vector3(plan[k].x, 200f, plan[k].y), Quaternion.LookRotation(look.normalized, Vector3.up)); }
            }
            if (!all) { RefugeLapCheck.Fail($"{run.name}: the route's ground never streamed in"); yield break; }
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
            var ai = carGo.AddComponent<AIDriver>();
            ai.path = tp; ai.skill = 0.95f;
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
                slowFor = rb.linearVelocity.magnitude < 0.5f ? slowFor + Time.fixedDeltaTime : 0f;
                if (ai.LastRecoveryWhy != lastWhy) { recov++; lastWhy = ai.LastRecoveryWhy; RefugeLapCheck.Line($"  {run.name}: RECOVERED at wp {idx}: {lastWhy}"); }
                if (slowFor > 8f) { stuck = true; break; }
                if (bestI >= n - 3) { done = true; break; }
            }
            Object.Destroy(ai); Object.Destroy(go);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            float pathM = 0f; for (int i = 1; i < n; i++) pathM += Vector3.Distance(even[i - 1], even[i]);
            RefugeLapCheck.Line($"{run.name} along '{run.road}' ({pathM:0} m,{edges}): {(done ? "COMPLETE" : "INCOMPLETE")} in {t:0.0} s, top {vMax * 3.6f:0} km/h, avg {pathM / Mathf.Max(0.1f, t) * 3.6f:0} km/h; " +
                                $"max off its lane line {maxOff:0.00} m at {offAt} ({offs} steps over {OffM} m); highest corner over the lane {worstKerb:0.000} m at {kerbAt} ({kerbs} corner-steps over {KerbM} m); " +
                                $"hits {hits.hits.Count}{(hits.hits.Count > 0 ? ": " + string.Join("; ", hits.hits) : "")}; recoveries {recov}{(stuck ? "; STUCK" : "")}");
            if (!done || stuck || offs > 0 || kerbs > 0 || hits.hits.Count > 0 || recov > 0) RefugeLapCheck.Fail($"{run.name}: not clean");
        }
    }
}
