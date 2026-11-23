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
    /// THE SERVICE DRIVE (service ways, 2026-11-15; play mode, the Charlotte
    /// free-roam scene; CityLotDrive's method). The player's car is driven
    /// from the street through a service way's mouth and along its line to
    /// its far end (220 m at most), steered at the line (pure pursuit), the
    /// throttle holding 15 and then 30 km/h (slowed for a bend the line
    /// itself makes: 20 km/h past 20 degrees, 12 past 45, 8 past 80).
    /// Measured from a metre before the front wheels reach the pavement's
    /// edge: did it reach the end, the longest any ONE wheel was off the
    /// ground, all four off, the hardest vertical kick (g), the furthest its
    /// middle strayed from the line, what solid it touched.
    /// PASS: reached the end, no wheel off over 0.05 s, nothing hit.
    ///
    /// Sites: PSX_SVCDRIVE_SITES, '|' separated "label;piece" (an index into
    /// CityMeshes.ServiceWays), else 20 picked by rule (through service
    /// roads, narrow ways between buildings, ways onto a lot, private
    /// streets, a drive-through, loose aisles outside the core), spread by
    /// distance from Uptown. Writes PSXRacing_service_drive.txt
    /// (PSX_SVCDRIVE_OUT) in the project root.
    /// </summary>
    public static class CityServiceDrive
    {
        internal static StringBuilder log;

        [MenuItem("PSX Racing/Drive The Service Ways (play mode)")]
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
            new GameObject("CityServiceDriveRunner").AddComponent<CityServiceDriveRunner>();
        }

        internal static void Finish(int code)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string name = System.Environment.GetEnvironmentVariable("PSX_SVCDRIVE_OUT");
            File.WriteAllText(Path.Combine(root, string.IsNullOrEmpty(name) ? "PSXRacing_service_drive.txt" : name), log.ToString());
            Debug.Log("[ServiceDrive]\n" + log);
            EditorApplication.Exit(code);
        }
    }

    public class CityServiceDriveRunner : MonoBehaviour
    {
        float bornAt;
        bool finished;
        void Awake() { bornAt = Time.realtimeSinceStartup; }
        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < 2400f) return;
            finished = true;
            Time.timeScale = 1f;
            CityServiceDrive.log.AppendLine("FAIL HUNG after 2400 s");
            CityServiceDrive.Finish(1);
        }

        static StringBuilder L => CityServiceDrive.log;
        static readonly CultureInfo I = CultureInfo.InvariantCulture;
        static int mask, solidMask;
        const float MaxRunM = 220f;

        static float GroundY(float x, float z, float y0) => GroundY(x, z, y0, out _);
        static float GroundY(float x, float z, float y0, out Collider c)
        {
            c = null;
            float top = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(x, y0 + 2.5f, z), Vector3.down, 7f, mask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.attachedRigidbody != null) continue;
                if (float.IsNaN(top) || h.point.y > top) { top = h.point.y; c = h.collider; }
            }
            return top;
        }
        static string Under(Vector3 p)
        {
            GroundY(p.x, p.z, p.y, out var c);
            return c != null ? (c.transform.parent != null ? c.transform.parent.name + "/" : "") + c.name : "nothing";
        }
        static string wheelOver = "";

        struct Site { public string label; public int way; }

        static void Pick(CityMap map, List<Site> sites)
        {
            var W = CityMeshes.ServiceWays;
            var used = new HashSet<int>();
            void Take(string label, System.Predicate<CityMeshes.ServiceWay> want, int count)
            {
                var c = new List<int>();
                for (int i = 0; i < W.Length; i++) if ((W[i].mouthA >= 0 || W[i].mouthB >= 0) && !used.Contains(i) && want(W[i])) c.Add(i);
                c.Sort((a, b) => ((W[a].pts[0] - map.uptown).sqrMagnitude).CompareTo((W[b].pts[0] - map.uptown).sqrMagnitude));
                for (int k = 0; k < count && c.Count > 0; k++)
                {
                    int i = c[Mathf.Min(c.Count - 1, (int)((k + 0.5f) * c.Count / count))];
                    if (!used.Add(i)) continue;
                    sites.Add(new Site { label = $"{label} {k + 1}", way = i });
                }
            }
            float MinHalf(CityMeshes.ServiceWay w) { float h = float.MaxValue; foreach (float v in w.half) h = Mathf.Min(h, v); return h; }
            bool EndsOnLot(CityMeshes.ServiceWay w) => w.mouthA >= 0 ? w.onLot[w.onLot.Length - 1] : w.onLot[0];
            Take("service road, a street at both ends", w => w.kind == 0 && w.mouthA >= 0 && w.mouthB >= 0 && w.length > 80f && w.length < 400f, 4);
            Take("narrow way between buildings", w => w.kind == 0 && MinHalf(w) <= 2.4f && w.length > 50f && w.length < 300f, 3);
            Take("service way onto a lot", w => w.kind == 0 && (w.mouthA < 0 || w.mouthB < 0) && EndsOnLot(w) && w.length > 40f && w.length < 300f, 4);
            Take("private street", w => w.kind == 5 && w.length > 80f, 2);
            Take("drive-through", w => w.kind == 3, 1);
            Take("aisle outside the core", w => w.kind == 4 && w.LooseAisle && w.length > 60f && w.length < 400f, 6);
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
                finished = true; CityServiceDrive.Finish(1); yield break;
            }
            var player = mode.player; var world = mode.world; var map = world.Map;
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            mask = ~((1 << 2) | (1 << CityWorld.SolidLayer) | (1 << CityTrees.FoliageLayer));
            solidMask = (1 << CityWorld.SolidLayer) | (1 << CityTrees.FoliageLayer);
            var sites = new List<Site>();
            string env = System.Environment.GetEnvironmentVariable("PSX_SVCDRIVE_SITES");
            if (!string.IsNullOrEmpty(env))
                foreach (var spec in env.Split('|'))
                {
                    var f = spec.Split(';');
                    if (f.Length < 2) continue;
                    int wi = -1;
                    if (f[1].StartsWith("way:"))
                    {
                        // by OSM way id (a piece's index moves when the data is made again): its first piece with a mouth
                        uint id = uint.Parse(f[1].Substring(4), I);
                        for (int k = 0; k < CityMeshes.ServiceWays.Length && wi < 0; k++)
                            if (CityMeshes.ServiceWays[k].osmId == id && (CityMeshes.ServiceWays[k].mouthA >= 0 || CityMeshes.ServiceWays[k].mouthB >= 0)) wi = k;
                    }
                    else wi = int.Parse(f[1], I);
                    if (wi >= 0 && wi < CityMeshes.ServiceWays.Length) sites.Add(new Site { label = f[0], way = wi });
                    else L.AppendLine($"no piece for site '{spec}'");
                }
            else Pick(map, sites);
            L.AppendLine($"SERVICE DRIVE: car {player.name}; {CityMeshes.ServiceStats.Line()}");
            L.AppendLine($"{sites.Count} sites x 15 and 30 km/h, from the street through the mouth along the way's line to its far end ({MaxRunM:0} m at most); PASS = reached the end, no wheel off the ground over 0.05 s, nothing hit");
            var specs = new StringBuilder();
            foreach (var st in sites) specs.Append(specs.Length > 0 ? "|" : "").Append(st.label.Replace(';', ',').Replace('|', '/')).Append(';').Append(st.way);
            L.AppendLine("SITES " + specs);
            int pass = 0, runs = 0;
            foreach (var st in sites)
            {
                var w = CityMeshes.ServiceWays[st.way];
                bool fromA = w.mouthA >= 0;
                int mi = fromA ? w.mouthA : w.mouthB;
                if (mi < 0 || CityMeshes.ServiceMouths[mi].off) { L.AppendLine($"SITE {st.label}: piece {st.way} has no open mouth"); continue; }
                var path = new List<Vector2>(w.pts);
                var halves = new List<float>(w.half);
                if (!fromA) { path.Reverse(); halves.Reverse(); }
                var m = CityMeshes.ServiceMouths[mi];
                var e = map.edges[m.edge];
                CityMeshes.ServiceMouthFrame(map, mi, out var mouth, out var n, out _);
                float y0 = e.YAt(Mathf.Clamp(m.s, 0f, e.length));
                var at = new Vector3(mouth.x, y0, mouth.y);
                player.TeleportTo(at + Vector3.up * 3f, Quaternion.identity);
                world.EnsureRing(at, 1);
                for (int k = 0; k < 10; k++) { player.TeleportTo(at + Vector3.up * 3f, Quaternion.identity); yield return null; }
                Physics.SyncTransforms();
                L.AppendLine(string.Format(I, "SITE {0}: piece {1} (way {2}, {3}{4}{5}, {6:0} m, half width {7:0.0}-{8:0.0} m), mouth on e{9} '{10}' s {11:0} side {12} at ({13:0.0}, {14:0.0}) = {15:0.00000},{16:0.00000}; far end {17}",
                    st.label, st.way, w.osmId, CityMeshes.ServiceKinds[w.kind], w.OneWay ? " one-way" : "", w.Private ? " private" : "", w.length, Min(halves), Max(halves), e.index, e.name, m.s, m.side, mouth.x, mouth.y,
                    35.18456015184093 + mouth.y / 111132.0, -80.81770185962013 + mouth.x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)),
                    (fromA ? w.mouthB : w.mouthA) >= 0 && fromA ? "a street" : halves.Count > 0 && (fromA ? w.onLot[w.onLot.Length - 1] : w.onLot[0]) ? "on a lot" : "open"));
                {
                    // the surface under the two wheel tracks, every 5 cm along the line from the mouth (the colliders the car meets)
                    float maxStep = 0f, stepAt = 0f, maxRise = 0f, riseAt = 0f; string stepOn = "";
                    var line = new List<Vector2> { mouth };
                    for (int k = 0; k < path.Count; k++) if (Vector2.Dot(path[k] - mouth, n) > 0.5f || k >= 2) line.Add(path[k]);
                    foreach (float lat in new[] { -0.75f, 0.75f })
                    {
                        var ys = new List<float>(); var at2 = new List<float>(); var where = new List<Vector2>();
                        float acc = 0f; float yPrev = y0;
                        for (int k = 0; k + 1 < line.Count && acc < MaxRunM; k++)
                        {
                            var d = line[k + 1] - line[k]; float len = d.magnitude;
                            if (len < 0.05f) continue;
                            d /= len; var lf = new Vector2(-d.y, d.x);
                            for (float s = 0f; s < len; s += 0.05f)
                            {
                                var q = line[k] + d * s + lf * lat;
                                if ((Mathf.RoundToInt((acc + s) / 0.05f) % 400) == 0) world.EnsureRing(new Vector3(q.x, yPrev, q.y), 1);
                                float gy = GroundY(q.x, q.y, yPrev);
                                if (!float.IsNaN(gy)) yPrev = gy;
                                ys.Add(gy); at2.Add(acc + s); where.Add(q);
                            }
                            acc += len;
                        }
                        for (int k = 1; k < ys.Count; k++)
                        {
                            if (float.IsNaN(ys[k]) || float.IsNaN(ys[k - 1]) || at2[k] - at2[k - 1] > 0.2f) continue;
                            float st1 = Mathf.Abs(ys[k] - ys[k - 1]);
                            if (st1 > maxStep) { maxStep = st1; stepAt = at2[k]; stepOn = Under(new Vector3(where[k].x, ys[k], where[k].y)); }
                            if (k >= 8 && !float.IsNaN(ys[k - 8]) && at2[k] - at2[k - 8] < 0.6f && Mathf.Abs(ys[k] - ys[k - 8]) > maxRise) { maxRise = Mathf.Abs(ys[k] - ys[k - 8]); riseAt = at2[k]; }
                        }
                    }
                    L.AppendLine(string.Format(I, "    wheel tracks: largest step between 5 cm samples {0:0.0} cm at {1:0.0} m (on {4}), largest change over 0.4 m {2:0.0} cm at {3:0.0} m", maxStep * 100f, stepAt, maxRise * 100f, riseAt, stepOn));
                }
                foreach (float kmh in new[] { 15f, 30f })
                {
                    var res = new float[10];
                    string hit = "";
                    yield return DriveAlong(player, world, map, e, y0, mouth, n, path, halves, (fromA ? w.mouthB : -1) >= 0, kmh, res, h => hit = h);
                    bool reached = res[0] > 0.5f, ok = reached && res[1] <= 0.05f && hit.Length == 0;
                    if (res[1] > 0.05f) hit += (hit.Length > 0 ? "; " : ", ") + "the wheel left over " + wheelOver;
                    runs++; if (ok) pass++;
                    L.AppendLine(string.Format(I, "    {0,2:0} km/h: {1}; {2:0} of {3:0} m driven in {4:0.0} s; one wheel off {5:0.00} s (at {6:0} m), all four off {7:0.00} s, vertical kick {8:0.0} g (at {9:0} m), strayed {10:0.0} m from the line, mean {11:0} km/h{12}: {13}",
                        kmh, reached ? "REACHED THE END" : "did NOT reach the end", res[2], res[3], res[4], res[1], res[5], res[6], res[7], res[8], res[9], res[4] > 0f ? res[2] / res[4] * 3.6f : 0f, hit.Length > 0 ? (hit.StartsWith(",") ? hit : ", HIT " + hit) : "", ok ? "PASS" : "FAIL"));
                }
            }
            L.AppendLine($"SERVICE DRIVE DONE: {pass} of {runs} runs pass");
            finished = true;
            Time.timeScale = 1f;
            CityServiceDrive.Finish(0);
        }

        static float Min(List<float> l) { float v = float.MaxValue; foreach (float x in l) v = Mathf.Min(v, x); return v; }
        static float Max(List<float> l) { float v = float.MinValue; foreach (float x in l) v = Mathf.Max(v, x); return v; }

        /// <summary>res: 0 reached the end, 1 longest one-wheel-off s, 2 driven m, 3 the run's length m,
        /// 4 seconds, 5 where the wheel left m, 6 all-off s, 7 kick g, 8 where m, 9 strayed m.</summary>
        IEnumerator DriveAlong(CarController player, CityWorld world, CityMap map, CityMap.Edge e, float y0, Vector2 mouth, Vector2 n, List<Vector2> path, List<float> halves, bool farIsStreet, float kmh, float[] res, System.Action<string> hitOut)
        {
            float v = kmh / 3.6f;
            // the line: from the mouth (the pavement's edge) on; its arc lengths
            var P = new List<Vector2>(path.Count + 1) { mouth };
            for (int i = 0; i < path.Count; i++) if (Vector2.Dot(path[i] - mouth, n) > 0.5f || i >= 2) P.Add(path[i]);
            var S = new float[P.Count];
            for (int i = 1; i < P.Count; i++) S[i] = S[i - 1] + (P[i] - P[i - 1]).magnitude;
            float total = S[P.Count - 1];
            float goal = Mathf.Min(MaxRunM, total - (farIsStreet ? 9f : 4f));
            Vector2 At(float s)
            {
                s = Mathf.Clamp(s, 0f, total);
                int i = 1; while (i < P.Count - 1 && S[i] < s) i++;
                float u = S[i] > S[i - 1] ? (s - S[i - 1]) / (S[i] - S[i - 1]) : 0f;
                return Vector2.Lerp(P[i - 1], P[i], u);
            }
            float Project(Vector2 q, float sFrom)
            {
                float best = sFrom, bd = float.MaxValue;
                for (int i = 1; i < P.Count; i++)
                {
                    if (S[i] < sFrom - 3f || S[i - 1] > sFrom + 25f) continue;
                    var ab = P[i] - P[i - 1]; float l2 = ab.sqrMagnitude;
                    float u = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - P[i - 1], ab) / l2) : 0f;
                    float d = (q - (P[i - 1] + ab * u)).sqrMagnitude;
                    if (d < bd) { bd = d; best = S[i - 1] + u * (S[i] - S[i - 1]); }
                }
                return best;
            }
            var dir = new Vector3(n.x, 0f, n.y);
            float back = Mathf.Clamp(e.width - 2.6f, 3.2f, 9f);
            var p0 = mouth - n * back;
            var start = new Vector3(p0.x, y0, p0.y);
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            Time.timeScale = 1f;
            player.TeleportTo(start + Vector3.up * 3f, rot);
            world.EnsureRing(start, 1);
            for (int k = 0; k < 3; k++) { player.TeleportTo(start + Vector3.up * 3f, rot); yield return null; }
            Physics.SyncTransforms();
            float gy = GroundY(start.x, start.z, start.y);
            if (!float.IsNaN(gy)) start.y = gy;
            player.TeleportTo(start + Vector3.up * 0.3f, rot);
            player.throttleInput = 0f; player.brakeInput = 1f; player.steerInput = 0f; player.handbrakeInput = false;
            for (int k = 0; k < 50; k++)
            {
                if (k < 40) { var lv0 = player.Body.linearVelocity; player.Body.linearVelocity = new Vector3(0f, lv0.y, 0f); player.Body.angularVelocity = Vector3.zero; }
                yield return new WaitForFixedUpdate();
            }
            player.brakeInput = 0f;
            float vIn = Mathf.Min(v, 15f / 3.6f);
            player.SetRolling(vIn);
            player.Body.linearVelocity = dir * vIn;
            // held square to the street until the front wheels are 1 m from the pavement's edge
            for (int guard = 0; guard < 1500; guard++)
            {
                float lead = float.MinValue;
                for (int wq = 0; wq < 2; wq++)
                {
                    var wp = player.transform.TransformPoint(new Vector3(wq == 0 ? -0.75f : 0.75f, 0f, 1.3f));
                    lead = Mathf.Max(lead, Vector2.Dot(new Vector2(wp.x, wp.z) - mouth, n));
                }
                if (lead > -1.0f) break;
                var eu = player.transform.eulerAngles;
                player.Body.MoveRotation(Quaternion.Euler(eu.x, rot.eulerAngles.y, eu.z));
                var lv = player.Body.linearVelocity;
                player.Body.linearVelocity = dir * vIn + Vector3.up * lv.y;
                player.steerInput = 0f; player.throttleInput = 0.4f; player.brakeInput = 0f;
                yield return new WaitForFixedUpdate();
            }
            Time.timeScale = 3f;
            float t = 0f, allOff = 0f, maxAll = 0f, maxOne = 0f, maxKick = 0f, kickAt = 0f, oneAt = 0f, strayed = 0f, sCar = 0f, driven = 0f;
            var off = new float[4]; var offFrom = new float[4];
            float lastVy = player.Body.linearVelocity.y;
            string hit = "";
            bool reached = false;
            float limit = 12f + goal / Mathf.Max(1.2f, v * 0.35f);
            while (t < limit)
            {
                var pc = new Vector2(player.Body.position.x, player.Body.position.z);
                sCar = Project(pc, sCar);
                driven = Mathf.Max(driven, sCar);
                if (sCar >= goal) { reached = true; break; }
                if (sCar > 2f) strayed = Mathf.Max(strayed, (pc - At(sCar)).magnitude);
                var vel = player.Body.linearVelocity;
                float sp = new Vector2(vel.x, vel.z).magnitude;
                // the bend ahead: the largest turn of the line within 12 m
                float turn = 0f;
                var d0 = (At(sCar + 1.5f) - At(sCar)).normalized;
                for (float a = 3f; a <= 12f; a += 1.5f)
                {
                    var d1 = (At(Mathf.Min(total, sCar + a + 1.5f)) - At(Mathf.Min(total, sCar + a))).normalized;
                    if (d1.sqrMagnitude > 0.5f) turn = Mathf.Max(turn, Vector2.Angle(d0, d1));
                }
                float vWant = turn > 80f ? Mathf.Min(v, 8f / 3.6f) : turn > 45f ? Mathf.Min(v, 12f / 3.6f) : turn > 20f ? Mathf.Min(v, 20f / 3.6f) : v;
                // pure pursuit at a point ahead on the line
                float ld = Mathf.Clamp(2.5f + 0.5f * sp, 3.5f, 8f);
                var target = At(sCar + ld);
                var fwd = new Vector2(player.transform.forward.x, player.transform.forward.z).normalized;
                var to = target - pc;
                float alpha = Vector2.SignedAngle(fwd, to) * Mathf.Deg2Rad;   // + = to the left (seen from above)
                float delta = Mathf.Atan2(2f * 2.6f * Mathf.Sin(alpha), Mathf.Max(1f, to.magnitude)) * Mathf.Rad2Deg;
                player.steerInput = Mathf.Clamp(-delta / 30f, -1f, 1f);
                player.throttleInput = sp < vWant - 0.4f ? 0.7f : sp < vWant ? 0.25f : 0f;
                player.brakeInput = sp > vWant + 1.2f ? 0.5f : 0f;
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime; t += dt;
                vel = player.Body.linearVelocity;
                float kick = Mathf.Abs(vel.y - lastVy) / dt / 9.81f;
                if (kick > maxKick) { maxKick = kick; kickAt = sCar; }
                lastVy = vel.y;
                int g = 0;
                for (int wq = 0; wq < 4; wq++)
                {
                    if (player.wheelContacts[wq].grounded) { g++; off[wq] = 0f; }
                    else
                    {
                        if (off[wq] <= 0f) offFrom[wq] = sCar;
                        off[wq] += dt;
                        if (off[wq] > maxOne)
                        {
                            maxOne = off[wq]; oneAt = offFrom[wq];
                            if (off[wq] > 0.05f && off[wq] - dt <= 0.05f)
                            {
                                // which wheel, and what lies under it
                                var wp = player.transform.TransformPoint(new Vector3((wq & 1) == 0 ? -0.75f : 0.75f, 0f, wq < 2 ? 1.3f : -1.3f));
                                wheelOver = (wq == 0 ? "front left" : wq == 1 ? "front right" : wq == 2 ? "rear left" : "rear right") + " on " + Under(wp) + string.Format(I, " at ({0:0.0}, {1:0.0})", wp.x, wp.z);
                            }
                        }
                    }
                }
                allOff = g == 0 ? allOff + dt : 0f;
                maxAll = Mathf.Max(maxAll, allOff);
                if (hit.Length == 0)
                {
                    var cols = Physics.OverlapBox(player.Body.position + Vector3.up * 0.5f, new Vector3(0.85f, 0.35f, 2.15f), player.transform.rotation, solidMask, QueryTriggerInteraction.Ignore);
                    foreach (var c in cols) if (c.attachedRigidbody != player.Body) { hit = c.name + string.Format(I, " at {0:0} m ({1:0.0}, {2:0.0})", sCar, pc.x, pc.y); break; }
                }
                if (hit.Length == 0 && sp < 0.25f && t > 4f) { hit = string.Format(I, "something that stopped it at {0:0} m ({1:0.0}, {2:0.0})", sCar, pc.x, pc.y); break; }
            }
            Time.timeScale = 1f;
            res[0] = reached ? 1f : 0f; res[1] = maxOne; res[2] = driven; res[3] = goal; res[4] = t; res[5] = oneAt; res[6] = maxAll; res[7] = maxKick; res[8] = kickAt; res[9] = strayed;
            hitOut(hit);
            player.throttleInput = 0f; player.brakeInput = 1f; player.steerInput = 0f;
            for (int k = 0; k < 20; k++) yield return new WaitForFixedUpdate();
        }
    }
}
