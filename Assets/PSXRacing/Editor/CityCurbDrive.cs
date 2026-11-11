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
    /// THE CURB DRIVE (curbs2, 2026-11-11; play mode, the Charlotte free-roam
    /// scene). The player's car is driven at a street's curb - square on and
    /// at 45, 30 and 15 degrees to it, at 10-120 km/h, steering held straight,
    /// the throttle holding the speed - and measured from the moment it is let
    /// go (a metre before its front wheel reaches the curb) for 1.5 s: the
    /// longest time with all four and with any wheel off, the hardest vertical
    /// kick and speed loss (g), the body's climb and fall rates, the yaw it
    /// was turned by, the jounce-stop strikes, and where it ended (up on the
    /// walk, or turned back onto the road). Also driven OFF the walk at a
    /// glancing angle, and across a driveway apron and a walk's ramp end
    /// (where the car crosses legitimately: no step allowed there).
    ///
    /// The same spots for each MODE in one run (the tiles dropped and built
    /// again between): PSX_CURB_MODES, comma separated, of
    ///   old    PSX_CITY_CURBS2=0's curb (the 45-degree rolled face)
    ///   v      the vertical curb as drawn (the collider is the drawn mesh)
    ///   cNN    the vertical curb with its face top NN cm out (a chamfered
    ///          collider, to find the least one that drives)
    /// Sites: PSX_CURB_SITES, '|' separated "label;name:Street Name" (the walk
    /// edge of that name nearest the Square), "label;rolled" (the nearest
    /// rolled-curb street) or "label;x,z" (the walk edge nearest a point).
    /// Writes PSXRacing_curb_drive.txt in the project root.
    /// </summary>
    public static class CityCurbDrive
    {
        internal static StringBuilder log;

        [MenuItem("PSX Racing/Drive At The Curbs (play mode)")]
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            CityPlayCheck.log = new StringBuilder();
            CityPlayCheck.roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int s = CityPlayCheck.roamIdx >= 0 ? TrackCatalog.SceneIndex(CityPlayCheck.roamIdx) : -1;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                log.AppendLine("FAIL no Charlotte scene");
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
            new GameObject("CityCurbDriveRunner").AddComponent<CityCurbDriveRunner>();
        }

        internal static void Finish(int code)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(root, "PSXRacing_curb_drive.txt"), log.ToString());
            Debug.Log("[CurbDrive]\n" + log);
            EditorApplication.Exit(code);
        }
    }

    public class CityCurbDriveRunner : MonoBehaviour
    {
        float bornAt;
        bool finished;
        void Awake() { bornAt = Time.realtimeSinceStartup; }
        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < 2400f) return;
            finished = true;
            CityCurbDrive.log.AppendLine("FAIL HUNG after 2400 s");
            CityCurbDrive.Finish(1);
        }

        static StringBuilder L => CityCurbDrive.log;
        static string F2(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static int mask;

        /// <summary>The ground or road surface at plan (x, z): the highest static
        /// hit (not foliage, not a solid) within 1.5 m above and 4 m under y0.</summary>
        static float GroundY(float x, float z, float y0, out Collider c)
        {
            c = null;
            float top = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(x, y0 + 1.5f, z), Vector3.down, 5.5f, mask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.attachedRigidbody != null) continue;
                if (float.IsNaN(top) || h.point.y > top) { top = h.point.y; c = h.collider; }
            }
            return top;
        }

        struct Site
        {
            public string label;
            public Vector3 c;        // the curb line's foot at the station (world)
            public Vector2 t, n;     // along the curb, out of the road (onto the walk)
            public float roadY, topY, room;
            public string what;
        }

        /// <summary>The walk edge a site names, and a station on it.</summary>
        static bool PickEdge(CityMap map, string spec, out CityMap.Edge edge, out float s)
        {
            edge = null; s = 0f;
            float best = float.MaxValue;
            Vector2 at = map.uptown;
            bool byName = spec.StartsWith("name:"), rolled = spec == "rolled";
            string nm = byName ? spec.Substring(5) : null;
            if (!byName && !rolled)
            {
                var c = spec.Split(',');
                at = new Vector2(float.Parse(c[0], CultureInfo.InvariantCulture), float.Parse(c[1], CultureInfo.InvariantCulture));
            }
            foreach (var e in map.edges)
            {
                if (e.a == e.b || e.length < 50f || !CityMeshes.WalkEdge(map, e)) continue;
                if (byName && e.name != nm) continue;
                if (rolled && (!CityMeshes.RolledEdge(map, e) || e.oneway || e.length < 70f)) continue;
                float d;
                if (!byName && !rolled)
                {
                    CityElevation.ProjectOn(e, at, out float sp);
                    d = Vector2.Distance(e.PointAt(sp), at);
                    if (d < best) { best = d; edge = e; s = Mathf.Clamp(sp, 25f, e.length - 25f); }
                    continue;
                }
                d = Vector2.Distance(e.PointAt(e.length * 0.5f), map.uptown);
                if (d < best) { best = d; edge = e; s = e.length * 0.5f; }
            }
            return edge != null;
        }

        /// <summary>The curb at arc s of edge e on the side whose curb is nearer:
        /// scanned every 2 cm out from the map line on the colliders.</summary>
        static bool FindCurb(CityMap.Edge e, float s, string label, out Site site)
        {
            site = default;
            var p = e.PointAt(s); var t = e.TangentAt(s).normalized;
            float y0 = e.YAt(s);
            float bestD = float.MaxValue;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                var n = new Vector2(-t.y, t.x) * sgn;
                float road = GroundY(p.x, p.y, y0, out _);
                if (float.IsNaN(road)) continue;
                float prev = road, foot = -1f, top = float.NaN;
                for (float d = 0.5f; d < 16f; d += 0.02f)
                {
                    var q = p + n * d;
                    float y = GroundY(q.x, q.y, y0, out _);
                    if (float.IsNaN(y)) break;
                    if (foot < 0f && y - road > 0.012f) foot = d - 0.02f;
                    if (foot >= 0f && d - foot > 0.6f) { top = y; break; }
                    if (foot < 0f) road = Mathf.Lerp(road, y, 0.02f);
                    prev = y;
                }
                if (foot < 0f || float.IsNaN(top) || top - road < 0.07f) continue;
                if (foot < bestD)
                {
                    bestD = foot;
                    // the room behind the car on the road: back across to the other side's rise
                    float room = 0f, py = road;
                    for (float d = foot - 0.1f; d > -16f; d -= 0.1f)
                    {
                        var q = p + n * d;
                        float y = GroundY(q.x, q.y, y0, out _);
                        if (float.IsNaN(y) || Mathf.Abs(y - py) > 0.03f) break;
                        py = y; room = foot - d;
                    }
                    var c = p + n * foot;
                    site = new Site { label = label, c = new Vector3(c.x, road, c.y), t = t, n = n, roadY = road, topY = top, room = room,
                                      what = $"e{e.index} '{e.name}' s {s:0} side {(sgn < 0 ? "right" : "left")} of the line, curb {F2(top - road)} m high at {foot:0.00} m out, {room:0.0} m of road behind" };
                }
            }
            return bestD < float.MaxValue;
        }

        /// <summary>The curb's profile at the site, every 2 cm from 0.3 m before
        /// the foot to 0.8 m past it (what the wheel rays read).</summary>
        static string Profile(Site st)
        {
            var sb = new StringBuilder();
            for (float d = -0.3f; d <= 0.8f; d += 0.02f)
            {
                var q = new Vector2(st.c.x, st.c.z) + st.n * d;
                float y = GroundY(q.x, q.y, st.roadY, out _);
                sb.Append(float.IsNaN(y) ? "nan" : ((y - st.roadY) * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append(' ');
            }
            return sb.ToString();
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
                L.AppendLine("FAIL no free-roam session");
                finished = true; CityCurbDrive.Finish(1); yield break;
            }
            var player = mode.player; var world = mode.world;
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            mask = ~((1 << 2) | (1 << CityWorld.SolidLayer) | (1 << CityTrees.FoliageLayer));
            string modesEnv = System.Environment.GetEnvironmentVariable("PSX_CURB_MODES");
            string sitesEnv = System.Environment.GetEnvironmentVariable("PSX_CURB_SITES");
            var modes = (string.IsNullOrEmpty(modesEnv) ? "old,v" : modesEnv).Split(',');
            var sites = (string.IsNullOrEmpty(sitesEnv) ? "graham;name:North Graham Street|rolled;rolled" : sitesEnv).Split('|');
            float[] speeds = { 10f, 30f, 50f, 80f, 120f };
            float[] angles = { 90f, 45f, 30f, 15f };
            float strike = 0f; int strikes = 0;
            player.Bottomed += (v, w) => { strikes++; strike = Mathf.Max(strike, v); };
            L.AppendLine($"CURB DRIVE: car {player.name}, modes {string.Join(",", modes)}; each run released 1 m before the front wheel meets the curb, measured 1.5 s; launch = all four wheels off >= 0.10 s");

            foreach (var md in modes)
            {
                string m = md.Trim();
                CityMeshes.Curbs2On = m != "old";
                CityMeshes.Cs2TestFaceOut = m.StartsWith("c") ? int.Parse(m.Substring(1), CultureInfo.InvariantCulture) / 100f : -1f;
                world.DropAll();
                yield return null;
                L.AppendLine($"== MODE {m}: curbs2 {(CityMeshes.Curbs2On ? "on" : "off")}, test face {CityMeshes.Cs2TestFaceOut:0.00}");
                foreach (var spec in sites)
                {
                    var f = spec.Split(';');
                    if (f.Length < 2) continue;
                    if (!PickEdge(world.Map, f[1].Trim(), out var edge, out float s0)) { L.AppendLine($"  site {f[0]}: no edge for '{f[1]}'"); continue; }
                    var pw = edge.PointAt(s0);
                    var at = new Vector3(pw.x, edge.YAt(s0), pw.y);
                    player.TeleportTo(at + Vector3.up * 3f, Quaternion.identity);
                    world.EnsureRing(at, 1);
                    for (int k = 0; k < 6; k++) { player.TeleportTo(at + Vector3.up * 3f, Quaternion.identity); yield return null; }
                    Physics.SyncTransforms();
                    if (!FindCurb(edge, s0, f[0], out var st)) { L.AppendLine($"  site {f[0]}: no curb found at e{edge.index} '{edge.name}' s {s0:0}"); continue; }
                    L.AppendLine($"  SITE {st.label}: {st.what}, rolled class {CityMeshes.RolledEdge(world.Map, edge)}");
                    L.AppendLine($"    profile (cm over the road, every 2 cm from 0.30 m before the foot): {Profile(st)}");
                    var runs = new List<(float ang, float kmh, int kind)>();
                    foreach (var a in angles) foreach (var v in speeds) runs.Add((a, v, 0));
                    foreach (var a in new[] { 15f, 30f }) foreach (var v in new[] { 30f, 50f, 80f }) runs.Add((a, v, 1));   // off the walk
                    foreach (var r in runs)
                    {
                        strikes = 0; strike = 0f;
                        var res = new float[16];
                        yield return DriveOne(player, world, st, r.ang, r.kmh, r.kind == 1, res);
                        L.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "    {0} {1,3:0} deg {2,3:0} km/h: all off {3:0.00} s, any off {4:0.00} s, vertical kick {5:0.0} g, climb {6:0.00} m/s, body up {7:0.00} m, speed loss {8:0.0} g ({9:0} -> {10:0} km/h), yaw {11:+0.0;-0.0} deg, ended {12:+0.00;-0.00} m {13}, stops {14} (strike {15:0.0} m/s), wheel step {16:0.000} m{17}",
                            r.kind == 1 ? "OFF" : "ON ", r.ang, r.kmh, res[0], res[1], res[2], res[3], res[4], res[5], res[6], res[7], res[8], res[9], res[9] > 0.4f ? "on the walk" : "on the road", strikes, strike, res[10],
                            res[0] >= 0.10f ? "  LAUNCH" : res[5] > 2f ? "  WALL" : ""));
                    }
                    // a driveway apron and a ramp end along this curb, crossed at 45 deg 30 km/h
                    yield return CrossSpecial(player, world, st, edge, s0);
                }
            }
            L.AppendLine("CURB DRIVE DONE");
            finished = true;
            CityCurbDrive.Finish(0);
        }

        IEnumerator CrossSpecial(CarController player, CityWorld world, Site st, CityMap.Edge edge, float s0)
        {
            // an apron: along the curb line both ways up to 60 m
            for (int which = 0; which < 2; which++)
            {
                bool found = false; Site sp = st;
                for (float d = 2f; d < 60f && !found; d += 1f)
                    for (int sg = -1; sg <= 1 && !found; sg += 2)
                    {
                        var q = new Vector2(st.c.x, st.c.z) + st.t * (d * sg);
                        if (which == 0)
                        {
                            var w = q + st.n * 0.8f;
                            if (!CityMeshes.ApronAtForTools(world.Map, w.x, w.y)) continue;
                        }
                        else
                        {
                            // a ramp end: the curb's rise 0.5 m out under 3 cm, with full curb 6 m back toward the site
                            var w = q + st.n * 0.5f;
                            float y = GroundY(w.x, w.y, st.roadY, out _), yr = GroundY(q.x - st.n.x * 1f, q.y - st.n.y * 1f, st.roadY, out _);
                            if (float.IsNaN(y) || float.IsNaN(yr) || y - yr > 0.03f) continue;
                            var bk = q - st.t * (6f * sg) + st.n * 0.5f;
                            float yb = GroundY(bk.x, bk.y, st.roadY, out _);
                            if (float.IsNaN(yb) || yb - yr < 0.08f) continue;
                        }
                        sp.c = new Vector3(q.x, st.roadY, q.y);
                        float ry = GroundY(q.x - st.n.x * 1f, q.y - st.n.y * 1f, st.roadY, out _);
                        if (!float.IsNaN(ry)) { sp.roadY = ry; sp.c.y = ry; }
                        sp.label = (which == 0 ? "apron" : "ramp end") + $" {d * sg:+0;-0} m along";
                        found = true;
                    }
                if (!found) { L.AppendLine($"    {(which == 0 ? "apron" : "ramp end")}: none within 60 m"); continue; }
                foreach (var (a, v) in new[] { (45f, 30f), (90f, 20f), (20f, 50f) })
                {
                    var res = new float[16];
                    yield return DriveOne(player, world, sp, a, v, false, res);
                    L.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0}: {1:0} deg {2:0} km/h: all off {3:0.00} s, any off {4:0.00} s, vertical kick {5:0.0} g, speed loss {6:0.0} g, yaw {7:+0.0;-0.0} deg, wheel step {8:0.000} m{9}",
                        sp.label, a, v, res[0], res[1], res[2], res[5], res[8], res[10], res[10] > 0.05f ? "  STEP" : ""));
                }
            }
        }

        /// <summary>One run at the curb. res: 0 all-off s, 1 any-off s, 2 vertical
        /// kick g, 3 climb m/s, 4 body up m, 5 speed loss g, 6 km/h at release,
        /// 7 km/h at the end, 8 yaw deg, 9 ended lateral m (+ onto the walk),
        /// 10 largest step under a wheel between ticks (m).</summary>
        IEnumerator DriveOne(CarController player, CityWorld world, Site st, float angDeg, float kmh, bool off, float[] res)
        {
            float v = kmh / 3.6f, a = angDeg * Mathf.Deg2Rad;
            // travel: along +t, into the curb by the angle (off the walk: out of it)
            var dir2 = (st.t * Mathf.Cos(a) + st.n * Mathf.Sin(a) * (off ? -1f : 1f)).normalized;
            var dir = new Vector3(dir2.x, 0f, dir2.y);
            Vector2 c2 = new Vector2(st.c.x, st.c.z);
            Vector3 start;
            if (!off)
            {
                float lat = Mathf.Clamp(Mathf.Min(st.room - 1.2f, 12f * Mathf.Sin(a)), 1.4f, 12f);
                float back = lat / Mathf.Max(Mathf.Sin(a), 0.05f);
                var p = c2 - dir2 * back;
                start = new Vector3(p.x, st.roadY, p.y);
            }
            else
            {
                // on the walk, its centre 1.9 m out of the curb
                float back = 1.9f / Mathf.Max(Mathf.Sin(a), 0.05f);
                var p = c2 - dir2 * back;
                float y = GroundY(p.x, p.y, st.roadY + 0.2f, out _);
                start = new Vector3(p.x, float.IsNaN(y) ? st.topY : y, p.y);
            }
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            player.TeleportTo(start + Vector3.up * 3f, rot);
            world.EnsureRing(start, 1);
            for (int k = 0; k < 3; k++) { player.TeleportTo(start + Vector3.up * 3f, rot); yield return null; }
            Physics.SyncTransforms();
            float gy = GroundY(start.x, start.z, start.y, out _);
            if (!float.IsNaN(gy)) start.y = gy;
            player.TeleportTo(start + Vector3.up * 0.55f, rot);
            player.throttleInput = 0f; player.brakeInput = 0f; player.steerInput = 0f; player.handbrakeInput = false;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            player.SetRolling(v);
            player.Body.linearVelocity = dir * v;
            // held on the line at speed until the leading front wheel is 1 m from the curb (or 0.4 s for an off run)
            float held = 0f;
            for (int guard = 0; guard < 3000; guard++)
            {
                float lead = float.MinValue, leadOff = float.MaxValue;
                for (int w = 0; w < 2; w++)
                {
                    var wp = player.wheelContacts[w].point;
                    if (!player.wheelContacts[w].grounded) wp = player.transform.TransformPoint(new Vector3(w == 0 ? -0.75f : 0.75f, 0f, 1.3f));
                    float lw = Vector2.Dot(new Vector2(wp.x, wp.z) - c2, st.n);
                    lead = Mathf.Max(lead, lw); leadOff = Mathf.Min(leadOff, lw);
                }
                if (!off && lead > -1.0f) break;
                if (off && leadOff < 0.6f && held > 0.1f) break;
                var e = player.transform.eulerAngles;
                player.Body.MoveRotation(Quaternion.Euler(e.x, Quaternion.LookRotation(dir).eulerAngles.y, e.z));
                var lv = player.Body.linearVelocity;
                player.Body.linearVelocity = dir * v + Vector3.up * lv.y;
                player.steerInput = 0f; player.throttleInput = 0.4f; player.brakeInput = 0f;
                yield return new WaitForFixedUpdate();
                held += Time.fixedDeltaTime;
            }
            // released: measure 1.5 s
            float t = 0f, allOff = 0f, anyOff = 0f, maxAll = 0f, maxAny = 0f, maxKick = 0f, maxClimb = 0f, maxUp = 0f, maxDecel = 0f, maxStep = 0f;
            float lastVy = player.Body.linearVelocity.y, lastSp = new Vector2(player.Body.linearVelocity.x, player.Body.linearVelocity.z).magnitude;
            float yaw0 = player.transform.eulerAngles.y, v0 = lastSp, baseAir = float.NaN;
            res[11] = held;
            var lastW = new float[4]; var hasW = new bool[4];
            while (t < 1.5f)
            {
                float sp = new Vector2(player.Body.linearVelocity.x, player.Body.linearVelocity.z).magnitude;
                player.steerInput = 0f;
                player.throttleInput = sp < v - 0.5f ? 1f : 0.2f;
                player.brakeInput = 0f;
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime; t += dt;
                var vel = player.Body.linearVelocity;
                float hsp = new Vector2(vel.x, vel.z).magnitude;
                maxKick = Mathf.Max(maxKick, Mathf.Abs(vel.y - lastVy) / dt / 9.81f);
                maxClimb = Mathf.Max(maxClimb, vel.y);
                float dec = (lastSp - hsp) / dt / 9.81f;
                maxDecel = Mathf.Max(maxDecel, dec);
                lastVy = vel.y; lastSp = hsp;
                int g = 0;
                for (int w = 0; w < 4; w++)
                {
                    var wc = player.wheelContacts[w];
                    if (wc.grounded)
                    {
                        g++;
                        if (hasW[w]) maxStep = Mathf.Max(maxStep, Mathf.Abs(wc.point.y - lastW[w]) - Mathf.Abs(hsp * dt * 0.2f));
                        lastW[w] = wc.point.y; hasW[w] = true;
                    }
                }
                allOff = g == 0 ? allOff + dt : 0f; anyOff = g < 4 ? anyOff + dt : 0f;
                maxAll = Mathf.Max(maxAll, allOff); maxAny = Mathf.Max(maxAny, anyOff);
                float gyb = GroundY(player.Body.position.x, player.Body.position.z, player.Body.position.y - 0.3f, out _);
                if (!float.IsNaN(gyb))
                {
                    float air = player.Body.position.y - gyb;
                    if (float.IsNaN(baseAir)) baseAir = air;
                    maxUp = Mathf.Max(maxUp, air - baseAir);
                }
                // stop at a wall past the curb (a building), so it does not read as the curb's
                var pc = new Vector2(player.Body.position.x, player.Body.position.z);
                if (dec > 2f && Vector2.Dot(pc - c2, st.n) > 2.0f) break;
            }
            var end = new Vector2(player.Body.position.x, player.Body.position.z);
            res[0] = maxAll; res[1] = maxAny; res[2] = maxKick; res[3] = maxClimb; res[4] = maxUp; res[5] = maxDecel;
            res[6] = v0 * 3.6f; res[7] = lastSp * 3.6f; res[8] = Mathf.DeltaAngle(yaw0, player.transform.eulerAngles.y);
            res[9] = Vector2.Dot(end - c2, st.n); res[10] = Mathf.Max(0f, maxStep);
        }
    }
}
