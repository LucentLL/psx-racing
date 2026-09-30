using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LAUNCH AUDIT'S SPOTS, DRIVEN (play mode, the Charlotte free-roam
    /// scene). Each spot from launch_top_{label}.txt (CityLaunchAudit) is a
    /// path: 80 m of run-up, the break, 40 m on. The player's car is put on
    /// the run-up at the spot's judged speed, held on the path (its yaw and
    /// speed kept on the line until 20 m before the break, then only the
    /// wheel and the pedals) and measured from 20 m before the break to the
    /// end: the longest time with ALL FOUR wheels off (a launch past
    /// <see cref="AirLaunchS"/>), the longest with any wheel off, the most air
    /// under the body, the worst yaw-rate step (heading jerk) and how far off
    /// the line it ended. Writes PSXRacing_launch_drive.txt. Menu: PSX Racing/
    /// Drive The Launch Spots (play mode). Spots file: PSX_LAUNCH_SPOTS, else
    /// launch_top_before.txt in the project root.
    ///
    /// Or PSX_LAUNCH_WALK: spots walked on the graph the game is playing,
    /// "label;kmh;x,z;hx,hz[;ox,oz]" separated by '|': the road through
    /// (x,z) heading (hx,hz) that may be driven that way, 90 m back and
    /// 130 m on, each node taking the straightest arm that may be driven
    /// (the first one ahead the arm nearest heading (ox,oz), for a turn or
    /// a slip road). The break is (x,z). Either way every point's height is
    /// read off the colliders once its tiles are built (a file written by an
    /// older solve would put the car under a road that has since risen).
    /// </summary>
    public static class CityLaunchDrive
    {
        public const float AirLaunchS = 0.10f;
        internal static StringBuilder log;
        internal static string spotsPath;
        internal static string walkSpec;

        [MenuItem("PSX Racing/Drive The Launch Spots (play mode)")]
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            string root = Directory.GetParent(Application.dataPath).FullName;
            walkSpec = System.Environment.GetEnvironmentVariable("PSX_LAUNCH_WALK");
            spotsPath = System.Environment.GetEnvironmentVariable("PSX_LAUNCH_SPOTS");
            if (string.IsNullOrEmpty(spotsPath)) spotsPath = Path.Combine(root, "launch_top_before.txt");
            CityPlayCheck.log = new StringBuilder();
            CityPlayCheck.roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int s = CityPlayCheck.roamIdx >= 0 ? TrackCatalog.SceneIndex(CityPlayCheck.roamIdx) : -1;
            if ((string.IsNullOrEmpty(walkSpec) && !File.Exists(spotsPath)) || s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                log.AppendLine("FAIL no spots file (" + spotsPath + ") or no Charlotte scene");
                Finish(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            CityPlayCheck.PrimeRoam();
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("CityLaunchDriveRunner").AddComponent<CityLaunchDriveRunner>();
        }

        static float F(string t) => float.Parse(t.Trim(), CultureInfo.InvariantCulture);
        static Vector2 V2(string t) { var c = t.Split(','); return new Vector2(F(c[0]), F(c[1])); }

        /// <summary>PSX_LAUNCH_WALK's spots, as spots-file lines (see the
        /// class summary).</summary>
        internal static List<string> WalkSpots(CityMap map, string spec)
        {
            var lines = new List<string>();
            var cand = new HashSet<int>();
            foreach (var one in spec.Split('|'))
            {
                var f = one.Split(';');
                if (f.Length < 4) continue;
                string label = f[0].Trim();
                float kmh = F(f[1]);
                Vector2 j = V2(f[2]), h = V2(f[3]).normalized;
                Vector2? turn = f.Length > 4 && f[4].Trim().Length > 0 ? V2(f[4]).normalized : (Vector2?)null;
                // the road through J along h that may be driven that way (of a
                // divided road's two carriageways, the one going h's way)
                cand.Clear();
                map.EdgeSegsInRect(j - Vector2.one * 25f, j + Vector2.one * 25f, cand);
                int e0 = -1, d0 = 1; float s0 = 0f, bd = 25f;
                foreach (int packed in cand)
                {
                    var e = map.edges[packed >> 12];
                    if (e.a == e.b || e.length < 1f) continue;
                    CityElevation.ProjectOn(e, j, out float s);
                    float dot = Vector2.Dot(e.TangentAt(s), h);
                    int dir = dot >= 0f ? 1 : -1;
                    if (Mathf.Abs(dot) < 0.7f || (e.oneway && dir < 0)) continue;
                    float d = Vector2.Distance(e.PointAt(s), j);
                    if (d < bd) { bd = d; e0 = e.index; d0 = dir; s0 = s; }
                }
                if (e0 < 0) { lines.Add($"{label};{kmh.ToString(CultureInfo.InvariantCulture)};0;0;;NO ROAD within 25 m of ({j.x:0},{j.y:0}) heading ({h.x:0.00},{h.y:0.00})"); continue; }
                var names = new List<string> { $"e{e0} '{map.edges[e0].name}'" };
                var back = Walk(map, e0, s0, -d0, 90f, null, true, null);
                var on = Walk(map, e0, s0, d0, 130f, turn, false, names);
                back.Reverse();
                if (back.Count > 0) back.RemoveAt(back.Count - 1);   // J is the forward walk's first point
                int brk = back.Count;
                back.AddRange(on);
                var sb = new StringBuilder();
                sb.Append(label).Append(';').Append(kmh.ToString("0", CultureInfo.InvariantCulture)).Append(";0;").Append(brk).Append(';');
                foreach (var p in back) sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00} ", p.x, p.y, p.z));
                sb.Append(';').Append(string.Join(" > ", names));
                lines.Add(sb.ToString());
            }
            return lines;
        }

        /// <summary>Points every 2 m from (ei, s) along dir for len metres,
        /// on at each node by the straightest arm that may be driven (walking
        /// BACK: driven toward the node); the first node's by the arm nearest
        /// <paramref name="turn"/> when given. A two-way road's point sits in
        /// the right-hand lane of travel.</summary>
        static List<Vector3> Walk(CityMap map, int ei, float s, int dir, float len, Vector2? turn, bool backward, List<string> names)
        {
            var pts = new List<Vector3>();
            float gone = 0f;
            var heading = map.edges[ei].TangentAt(s) * dir;
            for (int guard = 0; guard < 200 && gone < len; guard++)
            {
                var e = map.edges[ei];
                while (gone < len && s >= -1e-3f && s <= e.length + 1e-3f)
                {
                    var t = e.TangentAt(s) * dir;
                    // the lane of TRAVEL: walking back, travel is -t
                    var tr = backward ? -t : t;
                    var p = e.PointAt(s) + new Vector2(tr.y, -tr.x) * (e.oneway ? 0f : e.width * 0.25f);
                    pts.Add(new Vector3(p.x, e.YAt(Mathf.Clamp(s, 0f, e.length)), p.y));
                    heading = t;
                    s += dir * 2f; gone += 2f;
                }
                if (gone >= len) break;
                float over = dir > 0 ? s - e.length : -s;
                int node = dir > 0 ? e.b : e.a;
                int nb = -1, nd = 1; float best = -2f;
                foreach (int oi in map.nodeEdges[node])
                {
                    if (oi == ei) continue;
                    var o = map.edges[oi];
                    if (o.a == o.b || o.length < 1f) continue;
                    int od = o.a == node ? 1 : -1;
                    if (o.oneway && (backward ? od > 0 : od < 0)) continue;
                    var u = od > 0 ? o.TangentAt(0f) : -o.TangentAt(o.length);
                    float dt = Vector2.Dot(u, turn ?? heading);
                    if (dt > best) { best = dt; nb = oi; nd = od; }
                }
                if (nb < 0 || best < 0.2f) break;   // a dead end, or only a hairpin on
                turn = null;
                ei = nb; dir = nd;
                s = dir > 0 ? over : map.edges[nb].length - over;
                names?.Add($"e{nb} '{map.edges[nb].name}'{(map.edges[nb].link ? " L" : "")}");
            }
            return pts;
        }

        internal static void Finish(int code)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(root, "PSXRacing_launch_drive.txt"), log.ToString());
            Debug.Log("[LaunchDrive]\n" + log);
            EditorApplication.Exit(code);
        }
    }

    public class CityLaunchDriveRunner : MonoBehaviour
    {
        float bornAt;
        bool finished;
        void Awake() { bornAt = Time.realtimeSinceStartup; }
        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < 900f) return;
            finished = true;
            CityLaunchDrive.log.AppendLine("FAIL HUNG after 900 s");
            CityLaunchDrive.Finish(1);
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return null;
            var mode = CityMode.Instance;
            if (mode == null || mode.player == null || mode.world == null)
            {
                CityLaunchDrive.log.AppendLine("FAIL no free-roam session");
                finished = true; CityLaunchDrive.Finish(1); yield break;
            }
            var player = mode.player; var world = mode.world;
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            // the file's spots (when there is no walk, or PSX_LAUNCH_SPOTS names
            // one beside it), then the walk's
            bool walking = !string.IsNullOrEmpty(CityLaunchDrive.walkSpec);
            bool fromFile = File.Exists(CityLaunchDrive.spotsPath) && (!walking || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PSX_LAUNCH_SPOTS")));
            var all = new List<string>();
            if (fromFile) all.AddRange(File.ReadAllLines(CityLaunchDrive.spotsPath));
            if (walking) all.AddRange(CityLaunchDrive.WalkSpots(world.Map, CityLaunchDrive.walkSpec));
            var lines = all.ToArray();
            CityLaunchDrive.log.AppendLine($"LAUNCH DRIVE: {lines.Length} spots from {(fromFile ? Path.GetFileName(CityLaunchDrive.spotsPath) : "")}{(fromFile && walking ? " + " : "")}{(walking ? "PSX_LAUNCH_WALK" : "")}, car {player.name}; launch = all four wheels off for {CityLaunchDrive.AirLaunchS:0.00} s or more; heights read off the colliders");
            int launched = 0, driven = 0, crashed = 0;
            int mask = ~((1 << 2) | (1 << CityWorld.SolidLayer));
            for (int li = 0; li < lines.Length; li++)
            {
                var f = lines[li].Split(';');
                if (f.Length < 6) continue;
                float kmh = float.Parse(f[1], CultureInfo.InvariantCulture);
                float sepAudit = float.Parse(f[2], CultureInfo.InvariantCulture);
                int brk = int.Parse(f[3], CultureInfo.InvariantCulture);
                var pts = new List<Vector3>();
                foreach (var tok in f[4].Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var c = tok.Split(',');
                    pts.Add(new Vector3(float.Parse(c[0], CultureInfo.InvariantCulture), float.Parse(c[1], CultureInfo.InvariantCulture), float.Parse(c[2], CultureInfo.InvariantCulture)));
                }
                // a spot near the end of its path: the run-up (60 m) and the run-out
                // (30 m) carried on straight, at the path's end grade
                if (pts.Count >= 2)
                {
                    while (brk < 30)
                    {
                        var d = pts[0] - pts[1];
                        pts.Insert(0, pts[0] + d);
                        brk++;
                    }
                    while (pts.Count - 1 - brk < 15)
                    {
                        var d = pts[pts.Count - 1] - pts[pts.Count - 2];
                        pts.Add(pts[pts.Count - 1] + d);
                    }
                }
                if (pts.Count < 10 || brk < 3 || brk >= pts.Count - 3) { CityLaunchDrive.log.AppendLine($"  --  #{li + 1} {f[0]}: path too short{(f.Length > 5 ? " (" + f[5] + ")" : "")}"); continue; }
                float v = kmh / 3.6f;
                // The car goes there FIRST, held in the air: the world streams
                // round the player and drops what is far from it, so rings
                // built round a spot the car was not at were gone again by the
                // time the heights were read (0 of N seated), and the car was
                // then put down on tiles still streaming in.
                var d00 = pts[1] - pts[0];
                var hold = Quaternion.LookRotation(new Vector3(d00.x, 0f, d00.z).normalized, Vector3.up);
                player.TeleportTo(pts[0] + Vector3.up * 3f, hold);
                world.EnsureRing(pts[0], 1);
                world.EnsureRing(pts[brk], 1);
                world.EnsureRing(pts[pts.Count - 1], 1);
                for (int k = 0; k < 6; k++) { player.TeleportTo(pts[0] + Vector3.up * 3f, hold); yield return null; }
                Physics.SyncTransforms();
                // every point onto the road as built now (the highest static
                // surface within 2.5 m above and 5.5 m below the given height)
                int seated = 0;
                for (int q = 0; q < pts.Count; q++)
                {
                    var p = pts[q];
                    float top = float.NegativeInfinity;
                    foreach (var gh0 in Physics.RaycastAll(new Vector3(p.x, p.y + 2.5f, p.z), Vector3.down, 8f, mask, QueryTriggerInteraction.Ignore))
                        if (gh0.collider.attachedRigidbody == null && gh0.point.y > top) top = gh0.point.y;
                    if (!float.IsNegativeInfinity(top)) { pts[q] = new Vector3(p.x, top, p.z); seated++; }
                }
                var d0 = pts[1] - pts[0]; var fwd = new Vector3(d0.x, 0f, d0.z).normalized;
                player.TeleportTo(pts[0] + Vector3.up * 0.55f, Quaternion.LookRotation(fwd, Vector3.up));
                player.throttleInput = 0f; player.brakeInput = 0f; player.steerInput = 0f;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                // the drivetrain told the speed too (gear and rpm for it): a gear
                // left over from the last spot's 150 km/h lugged the car to a
                // stop on a 60-70 km/h one, throttle wide open
                player.SetRolling(v);
                player.Body.linearVelocity = (pts[2] - pts[0]).normalized * v;
                int prog = 0, winFrom = Mathf.Max(0, brk - 10);
                float allOff = 0f, anyOff = 0f, maxAll = 0f, maxAny = 0f, maxAir = 0f, lastYaw = 0f, maxJerk = 0f, maxLat = 0f, vAtBrk = 0f, t = 0f, baseAir = float.NaN;
                bool inWin = false, reached = false;
                string hit = "";
                // a crash is a speed loss no brake gives (past 2 g in one step);
                // a step or a kerb is a vertical kick
                float lastSp = -1f, lastVy = 0f, maxDecel = 0f, maxVAcc = 0f;
                string hitWhat = "";
                while (t < 20f)
                {
                    var pos = player.Body.position;
                    // progress: the nearest path point ahead of the last
                    float bd = float.MaxValue;
                    for (int q = prog; q < Mathf.Min(pts.Count, prog + 12); q++)
                    {
                        float dd = (new Vector2(pts[q].x - pos.x, pts[q].z - pos.z)).sqrMagnitude;
                        if (dd < bd) { bd = dd; prog = q; }
                    }
                    if (prog >= pts.Count - 2) { reached = true; break; }
                    int la = Mathf.Min(pts.Count - 1, prog + 3 + Mathf.RoundToInt(v * 0.2f / 2f));
                    var to = pts[la] - pos; to.y = 0f;
                    var along = pts[Mathf.Min(pts.Count - 1, prog + 1)] - pts[prog]; along.y = 0f; along.Normalize();
                    float err = Vector3.SignedAngle(player.transform.forward, to, Vector3.up);
                    if (prog < winFrom)
                    {
                        // on the line and at speed until the window: yaw and speed only
                        var e = player.transform.eulerAngles;
                        player.Body.MoveRotation(Quaternion.Euler(e.x, Quaternion.LookRotation(along).eulerAngles.y, e.z));
                        var lv = player.Body.linearVelocity;
                        var hv = new Vector3(lv.x, 0f, lv.z);
                        float grade = (pts[Mathf.Min(pts.Count - 1, prog + 1)].y - pts[prog].y) / 2f;
                        player.Body.linearVelocity = along * v + Vector3.up * (lv.y * 0.5f + grade * v * 0.5f);
                        // (the handbrake too: the free-roam start leaves it ON, and
                        // it stopped every run under ~100 km/h on a smooth road)
                        player.steerInput = 0f; player.throttleInput = 0.5f; player.brakeInput = 0f; player.handbrakeInput = false;
                    }
                    else
                    {
                        inWin = true;
                        float sp = player.Body.linearVelocity.magnitude;
                        player.steerInput = Mathf.Clamp(err / 6f, -1f, 1f);
                        player.throttleInput = sp < v - 0.5f ? 1f : 0.2f;
                        player.brakeInput = sp > v + 2f ? 0.3f : 0f;
                        player.handbrakeInput = false;
                    }
                    yield return new WaitForFixedUpdate();
                    float dt = Time.fixedDeltaTime; t += dt;
                    if (!inWin) continue;
                    if (prog >= brk && vAtBrk == 0f) vAtBrk = player.Body.linearVelocity.magnitude;
                    var vel = player.Body.linearVelocity;
                    float hsp = new Vector2(vel.x, vel.z).magnitude;
                    if (lastSp >= 0f)
                    {
                        float dec = (lastSp - hsp) / dt;
                        if (dec > maxDecel && dec > 2f * 9.81f)
                        {
                            // what it met: the first thing ahead of the bumper
                            // at wheel and at bonnet height, over 3 m of what it
                            // was doing (its last step's velocity)
                            var fwdH = new Vector3(player.transform.forward.x, 0f, player.transform.forward.z).normalized;
                            hitWhat = "";
                            foreach (float hy in new[] { 0.25f, 0.7f })
                                if (Physics.Raycast(player.Body.position + Vector3.up * hy - fwdH * 1f, fwdH, out var fh, 5f, ~(1 << 2), QueryTriggerInteraction.Ignore) && fh.collider.attachedRigidbody != player.Body)
                                    hitWhat += string.Format(CultureInfo.InvariantCulture, " [{0:0.00} m up: {1} (layer {2}, normal y {3:0.00}) at ({4:0.0},{5:0.00},{6:0.0})]", hy, fh.collider.name, fh.collider.gameObject.layer, fh.normal.y, fh.point.x, fh.point.y, fh.point.z);
                            if (hitWhat.Length == 0) hitWhat = " [nothing ahead within 4 m]";
                        }
                        maxDecel = Mathf.Max(maxDecel, dec); maxVAcc = Mathf.Max(maxVAcc, Mathf.Abs(vel.y - lastVy) / dt);
                    }
                    lastSp = hsp; lastVy = vel.y;
                    int grounded = 0;
                    for (int w = 0; w < 4; w++) if (player.wheelContacts[w].grounded) grounded++;
                    allOff = grounded == 0 ? allOff + dt : 0f;
                    anyOff = grounded < 4 ? anyOff + dt : 0f;
                    maxAll = Mathf.Max(maxAll, allOff); maxAny = Mathf.Max(maxAny, anyOff);
                    if (Physics.Raycast(player.Body.position + Vector3.up * 0.2f, Vector3.down, out var gh, 30f, mask, QueryTriggerInteraction.Ignore) && gh.collider.attachedRigidbody != player.Body)
                    {
                        float air = player.Body.position.y - gh.point.y;
                        if (float.IsNaN(baseAir)) baseAir = air;
                        maxAir = Mathf.Max(maxAir, air - baseAir);
                    }
                    float yaw = player.Body.angularVelocity.y * Mathf.Rad2Deg;
                    if (t > dt * 2f) maxJerk = Mathf.Max(maxJerk, Mathf.Abs(yaw - lastYaw) / dt);
                    lastYaw = yaw;
                    var near = pts[prog]; var side = Vector3.Cross(Vector3.up, along);
                    maxLat = Mathf.Max(maxLat, Mathf.Abs(Vector3.Dot(player.Body.position - near, side)));
                    if (player.Body.linearVelocity.magnitude < 3f && prog > brk)
                    {
                        // why: where, the drivetrain, the engine's temperature
                        var et = player.GetComponent<EngineTemp>();
                        hit = string.Format(CultureInfo.InvariantCulture, " STOPPED {0:0} m past the break at ({1:0.0},{2:0.0}), gear {3} {4:0} rpm, throttle {5:0.0} brake {6:0.0} handbrake {7}, engine {8}",
                            (prog - brk) * 2f, pos.x, pos.z, player.currentGear, player.currentRPM, player.throttleInput, player.brakeInput, player.handbrakeInput,
                            et == null ? "-" : $"{et.celsius:0} C x{et.PowerMult:0.00}{(et.Seized ? " SEIZED" : "")}");
                        break;
                    }
                }
                driven++;
                bool launch = maxAll >= CityLaunchDrive.AirLaunchS;
                bool crash = maxDecel > 2f * 9.81f;
                if (launch) launched++;
                if (crash) crashed++;
                CityLaunchDrive.log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0} #{1} {2} at {3:0} km/h (audit sep {4:0.00} m, {5}): all wheels off {6:0.00} s, any wheel off {7:0.00} s, body {8:0.00} m higher over the ground than at the window's start, yaw jerk {9:0} deg/s2, off line {10:0.0} m, {11:0} km/h at the break, hardest speed loss {14:0.0} g, hardest vertical kick {15:0.0} g, {16}/{17} points seated on the colliders{12}{13}",
                    launch ? "LAUNCH" : crash ? "CRASH " : "ok    ", li + 1, f[0], kmh, sepAudit, f.Length > 5 ? f[5] : "", maxAll, maxAny, maxAir, maxJerk, maxLat, vAtBrk * 3.6f, reached ? "" : " (did not reach the end)", hit,
                    maxDecel / 9.81f, maxVAcc / 9.81f, seated, pts.Count));
                if (crash)
                {
                    // the road as seated, every 2 m from 10 m before the break to 16 m past
                    var prof = new StringBuilder();
                    for (int q = Mathf.Max(0, brk - 5); q <= Mathf.Min(pts.Count - 1, brk + 8); q++) prof.Append(pts[q].y.ToString("0.00", CultureInfo.InvariantCulture)).Append(q == brk ? "* " : " ");
                    CityLaunchDrive.log.AppendLine("         hit:" + hitWhat + "; road y (every 2 m, * = the break): " + prof);
                }
            }
            CityLaunchDrive.log.AppendLine($"LAUNCH DRIVE: {launched} of {driven} spots launched the car, {crashed} crashed it (a speed loss past 2 g)");
            finished = true;
            CityLaunchDrive.Finish(0);
        }
    }
}
