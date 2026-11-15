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
    /// THE LOT DRIVE (lot entrances, 2026-11-15; play mode, the Charlotte
    /// free-roam scene; CityCurbDrive's method). The player's car is driven
    /// from the street into parking lots through their entrances: square off
    /// the nearest lane, steering held straight, the throttle holding the
    /// speed, at 15 and 40 km/h. Measured from a metre before its front
    /// wheels reach the pavement's edge until its middle is 4 m onto the
    /// lot's asphalt: did it arrive on the lot, the longest any ONE wheel was
    /// off the ground, all four off, the hardest vertical kick and speed loss
    /// (g), the largest step under a wheel between ticks, what solid it
    /// touched. PASS: on the lot, no wheel off over 0.05 s, no hit.
    ///
    /// Sites: PSX_LOTDRIVE_SITES, '|' separated "label;edge;s;side" (a street
    /// place: the same place with PSX_CITY_LOTENT=0 is the BEFORE), else
    /// picked by rule from the map's entrances: the owner's lot on Beatties
    /// Ford Road first, then Uptown surface lots, lots on walk streets and on
    /// verge streets further out, lots standing back from their street, the
    /// biggest lots, the data's own entrances and filling stations (22 or
    /// more). The picked sites are written out in the PSX_LOTDRIVE_SITES form.
    /// Writes PSXRacing_lot_drive.txt (PSX_LOTDRIVE_OUT) in the project root.
    /// </summary>
    public static class CityLotDrive
    {
        internal static StringBuilder log;

        [MenuItem("PSX Racing/Drive Into The Lots (play mode)")]
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
            new GameObject("CityLotDriveRunner").AddComponent<CityLotDriveRunner>();
        }

        internal static void Finish(int code)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string name = System.Environment.GetEnvironmentVariable("PSX_LOTDRIVE_OUT");
            File.WriteAllText(Path.Combine(root, string.IsNullOrEmpty(name) ? "PSXRacing_lot_drive.txt" : name), log.ToString());
            Debug.Log("[LotDrive]\n" + log);
            EditorApplication.Exit(code);
        }
    }

    public class CityLotDriveRunner : MonoBehaviour
    {
        float bornAt;
        bool finished;
        void Awake() { bornAt = Time.realtimeSinceStartup; }
        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < 2400f) return;
            finished = true;
            CityLotDrive.log.AppendLine("FAIL HUNG after 2400 s");
            CityLotDrive.Finish(1);
        }

        static StringBuilder L => CityLotDrive.log;
        static readonly CultureInfo I = CultureInfo.InvariantCulture;
        static int mask, solidMask;
        static readonly string[] Cls = { "local", "tertiary", "secondary", "primary", "trunk", "motorway" };

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

        struct Site { public string label, kind; public int edge, side; public float s; }

        static void Pick(CityMap map, List<Site> sites)
        {
            int n = map.lotEntrances.Length, from = CityMeshes.LotEntEntrancesFrom;
            var spot = new Vector2((float)((-80.8560 + 80.81770185962013) * 111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)), (float)((35.2437 - 35.18456015184093) * 111132.0));
            var rows = new List<(int en, int li, float gap, float area, bool derived, bool walk, bool station, float dUp, float dSpot)>();
            for (int i = 0; i < n; i++)
            {
                var E = map.lotEntrances[i]; var e = map.edges[E.edge];
                int li = CityMeshes.LotOfEntrance(map, i, out float gap);
                if (li < 0 || gap > 30f) continue;
                CityMeshes.LotEntranceFrame(map, i, out var m, out _, out _);
                bool station = map.stationLotsFrom >= 0 && li >= map.stationLotsFrom && (CityMeshes.LotEntLotsFrom < 0 || li < CityMeshes.LotEntLotsFrom);
                rows.Add((i, li, gap, CityMeshes.LotRingAreaForTools(map.lots[li].ring), from >= 0 && i >= from, CityMeshes.WalkSide(map, e, E.side), station, (m - map.uptown).magnitude, (m - spot).magnitude));
            }
            var used = new HashSet<int>(); var usedLots = new HashSet<int>();
            void Take(string kind, System.Predicate<(int en, int li, float gap, float area, bool derived, bool walk, bool station, float dUp, float dSpot)> want,
                      System.Comparison<(int en, int li, float gap, float area, bool derived, bool walk, bool station, float dUp, float dSpot)> order, int count)
            {
                var c = rows.FindAll(want);
                c.Sort(order);
                int got = 0;
                foreach (var r in c)
                {
                    if (got >= count) break;
                    if (used.Contains(r.en) || usedLots.Contains(r.li)) continue;
                    used.Add(r.en); usedLots.Add(r.li); got++;
                    var E = map.lotEntrances[r.en];
                    sites.Add(new Site { label = $"{kind} {got}", kind = kind, edge = E.edge, s = E.s, side = E.side });
                }
            }
            Take("owner's lot (Beatties Ford Rd)", r => r.dSpot < 150f, (a, b) => a.dSpot.CompareTo(b.dSpot), 2);
            Take("Uptown surface lot", r => r.derived && r.walk && r.dUp < 1500f && r.area > 800f, (a, b) => a.dUp.CompareTo(b.dUp), 4);
            Take("walk street, further out", r => r.derived && r.walk && r.dUp > 2500f && r.area > 1500f && r.area < 12000f, (a, b) => a.dUp.CompareTo(b.dUp), 3);
            Take("verge street (no curb)", r => r.derived && !r.walk && r.area > 1000f, (a, b) => a.dUp.CompareTo(b.dUp), 3);
            Take("lot standing back", r => r.derived && r.gap > 7f, (a, b) => a.dUp.CompareTo(b.dUp), 3);
            Take("big lot", r => r.derived && r.area > 15000f, (a, b) => b.area.CompareTo(a.area), 3);
            Take("the data's entrance, lot standing back", r => !r.derived && !r.station && r.gap > 3f, (a, b) => a.dUp.CompareTo(b.dUp), 2);
            Take("the data's entrance", r => !r.derived && !r.station && r.gap <= 3f, (a, b) => a.dUp.CompareTo(b.dUp), 2);
            Take("filling station", r => r.station, (a, b) => a.dUp.CompareTo(b.dUp), 3);
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
                finished = true; CityLotDrive.Finish(1); yield break;
            }
            var player = mode.player; var world = mode.world; var map = world.Map;
            foreach (var mb in player.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;
            mask = ~((1 << 2) | (1 << CityWorld.SolidLayer) | (1 << CityTrees.FoliageLayer));
            solidMask = (1 << CityWorld.SolidLayer) | (1 << CityTrees.FoliageLayer);
            var sites = new List<Site>();
            string env = System.Environment.GetEnvironmentVariable("PSX_LOTDRIVE_SITES");
            if (!string.IsNullOrEmpty(env))
                foreach (var spec in env.Split('|'))
                {
                    var f = spec.Split(';');
                    if (f.Length < 4) continue;
                    sites.Add(new Site { label = f[0], kind = f[0], edge = int.Parse(f[1], I), s = float.Parse(f[2], I), side = int.Parse(f[3], I) });
                }
            else Pick(map, sites);
            // by place: PSX_LOTDRIVE_NEAR "label;x;z|..." - the entrance whose mouth is nearest (driven first)
            {
                string nearEnv = System.Environment.GetEnvironmentVariable("PSX_LOTDRIVE_NEAR");
                int at0 = 0;
                if (!string.IsNullOrEmpty(nearEnv))
                    foreach (var spec in nearEnv.Split('|'))
                    {
                        var f = spec.Split(';');
                        if (f.Length < 3) continue;
                        int en = CityMeshes.LotEntranceNear(map, new Vector2(float.Parse(f[1], I), float.Parse(f[2], I)), out float dn);
                        if (en < 0 || dn > 120f) { L.AppendLine($"no entrance within 120 m of '{f[0]}' ({f[1]}, {f[2]})"); continue; }
                        var E = map.lotEntrances[en];
                        sites.Insert(at0++, new Site { label = f[0], kind = f[0], edge = E.edge, s = E.s, side = E.side });
                    }
            }
            L.AppendLine($"LOT DRIVE: car {player.name}; lot entrances {(CityMeshes.LotEntOn ? "ON" : "OFF (PSX_CITY_LOTENT=0)")}; {sites.Count} sites x 15 and 40 km/h; square off the lane, released 1 m before the pavement's edge; PASS = on the lot, no wheel off over 0.05 s, no hit");
            var specs = new StringBuilder();
            foreach (var st in sites) specs.Append(specs.Length > 0 ? "|" : "").Append(st.label.Replace(';', ',').Replace('|', '/')).Append(';').Append(st.edge).Append(';').Append(st.s.ToString("0.0", I)).Append(';').Append(st.side);
            L.AppendLine("SITES " + specs);
            int pass = 0, runs = 0;
            foreach (var st in sites)
            {
                var e = map.edges[st.edge];
                float s = Mathf.Clamp(st.s, 0f, e.length);
                var p = e.PointAt(s); var t = e.TangentAt(s);
                var left = LineModel.SectionLeft(e, s);
                LineModel.Extents(e, s, out float eMinus, out float ePlus);
                var n = left * st.side;
                var mouth = p + n * (st.side > 0 ? ePlus : eMinus);
                // the lot's asphalt along the line
                float gap = -1f; int lot = -1;
                for (float d = 0f; d <= 62f && lot < 0; d += 0.25f)
                {
                    int li = CityMeshes.LotPavedForTools(map, mouth + n * d);
                    if (li >= 0 && !map.lots[li].aisleOnly) { lot = li; gap = d; }
                }
                var at = new Vector3(mouth.x, e.YAt(s), mouth.y);
                player.TeleportTo(at + Vector3.up * 3f, Quaternion.identity);
                world.EnsureRing(at, 1);
                for (int k = 0; k < 10; k++) { player.TeleportTo(at + Vector3.up * 3f, Quaternion.identity); yield return null; }
                Physics.SyncTransforms();
                L.AppendLine(string.Format(I, "SITE {0}: e{1} '{2}' [{3}{4}] s {5:0} side {6}, walk {7}; mouth ({8:0.0}, {9:0.0}); lot {10}, its asphalt {11:0.0} m from the pavement",
                    st.label, e.index, e.name, Cls[Mathf.Clamp(e.cls, 0, 5)], e.oneway ? " one-way" : "", s, st.side, CityMeshes.WalkSide(map, e, st.side), mouth.x, mouth.y, lot, gap));
                if (lot < 0) gap = 6f;
                {
                    // the surface under the two wheel tracks, every 5 cm from the lane to 4 m onto the lot
                    float maxStep = 0f, stepAt = 0f, maxRise = 0f, riseAt = 0f;
                    var tng = new Vector2(-n.y, n.x);
                    foreach (float lat in new[] { -0.75f, 0.75f })
                    {
                        var ys = new List<float>();
                        for (float d = -1.5f; d <= gap + 4f; d += 0.05f)
                        {
                            var q = mouth + n * d + tng * lat;
                            ys.Add(GroundY(q.x, q.y, at.y, out _));
                        }
                        for (int k = 1; k < ys.Count; k++)
                        {
                            if (float.IsNaN(ys[k]) || float.IsNaN(ys[k - 1])) continue;
                            float st1 = Mathf.Abs(ys[k] - ys[k - 1]);
                            if (st1 > maxStep) { maxStep = st1; stepAt = -1.5f + k * 0.05f; }
                            if (k >= 8 && !float.IsNaN(ys[k - 8]) && Mathf.Abs(ys[k] - ys[k - 8]) > maxRise) { maxRise = Mathf.Abs(ys[k] - ys[k - 8]); riseAt = -1.5f + k * 0.05f; }
                        }
                    }
                    L.AppendLine(string.Format(I, "    wheel tracks: largest step between 5 cm samples {0:0.0} cm at {1:0.0} m, largest change over 0.4 m {2:0.0} cm at {3:0.0} m", maxStep * 100f, stepAt, maxRise * 100f, riseAt));
                }
                foreach (float kmh in new[] { 15f, 40f })
                {
                    var res = new float[12];
                    string hit = "";
                    yield return DriveIn(player, world, map, e, at.y, mouth, n, gap, kmh, res, h => hit = h);
                    bool onLot = res[0] > 0.5f, ok = onLot && res[1] <= 0.05f && hit.Length == 0;
                    runs++; if (ok) pass++;
                    L.AppendLine(string.Format(I, "    {0,2:0} km/h: {1}; reached {2:0.0} m past the pavement, ended on '{3}'; one wheel off {4:0.00} s (from {14:0.0} m), all four off {5:0.00} s, vertical kick {6:0.0} g (at {15:0.0} m), speed loss {7:0.0} g ({8:0} -> {9:0} km/h), wheel step {10:0.000} m, off the line {11:0.00} m{12}  {13}",
                        kmh, onLot ? "ON THE LOT" : "NOT on the lot", res[2], endName, res[1], res[3], res[4], res[5], res[6], res[7], res[8], res[9], hit.Length > 0 ? ", HIT " + hit : "", ok ? "PASS" : "FAIL", res[10], res[11]));
                }
            }
            L.AppendLine($"LOT DRIVE DONE: {pass} of {runs} runs pass");
            finished = true;
            CityLotDrive.Finish(0);
        }

        static string endName = "";

        /// <summary>res: 0 arrived on the lot, 1 longest one-wheel-off s, 2 reached m,
        /// 3 all-off s, 4 kick g, 5 speed loss g, 6 km/h at release, 7 at the end,
        /// 8 wheel step m, 9 off the line m.</summary>
        IEnumerator DriveIn(CarController player, CityWorld world, CityMap map, CityMap.Edge e, float y0, Vector2 mouth, Vector2 n, float gap, float kmh, float[] res, System.Action<string> hitOut)
        {
            float v = kmh / 3.6f;
            var dir = new Vector3(n.x, 0f, n.y);
            // on the road: the car's middle as far back as the pavement lets it (its tail on the pavement)
            float back = Mathf.Clamp(e.width - 2.6f, 3.2f, 9f);
            var p0 = mouth - n * back;
            var start = new Vector3(p0.x, y0, p0.y);
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            player.TeleportTo(start + Vector3.up * 3f, rot);
            world.EnsureRing(start, 1);
            for (int k = 0; k < 3; k++) { player.TeleportTo(start + Vector3.up * 3f, rot); yield return null; }
            Physics.SyncTransforms();
            float gy = GroundY(start.x, start.z, start.y, out _);
            if (!float.IsNaN(gy)) start.y = gy;
            player.TeleportTo(start + Vector3.up * 0.3f, rot);
            player.throttleInput = 0f; player.brakeInput = 1f; player.steerInput = 0f; player.handbrakeInput = false;
            // settled on its wheels before it moves (a narrow carriageway leaves no run to settle on)
            for (int k = 0; k < 50; k++)
            {
                if (k < 40) { var lv0 = player.Body.linearVelocity; player.Body.linearVelocity = new Vector3(0f, lv0.y, 0f); player.Body.angularVelocity = Vector3.zero; }
                yield return new WaitForFixedUpdate();
            }
            player.brakeInput = 0f;
            player.SetRolling(v);
            player.Body.linearVelocity = dir * v;
            // held on the line at speed until the front wheels are 1 m from the pavement's edge
            for (int guard = 0; guard < 1500; guard++)
            {
                float lead = float.MinValue;
                for (int w = 0; w < 2; w++)
                {
                    var wp = player.transform.TransformPoint(new Vector3(w == 0 ? -0.75f : 0.75f, 0f, 1.3f));
                    lead = Mathf.Max(lead, Vector2.Dot(new Vector2(wp.x, wp.z) - mouth, n));
                }
                if (lead > -1.0f) break;
                var eu = player.transform.eulerAngles;
                player.Body.MoveRotation(Quaternion.Euler(eu.x, rot.eulerAngles.y, eu.z));
                var lv = player.Body.linearVelocity;
                player.Body.linearVelocity = dir * v + Vector3.up * lv.y;
                player.steerInput = 0f; player.throttleInput = 0.4f; player.brakeInput = 0f;
                yield return new WaitForFixedUpdate();
            }
            float t = 0f, allOff = 0f, maxAll = 0f, maxOne = 0f, maxKick = 0f, maxDecel = 0f, maxStep = 0f, reached = float.MinValue, maxLat = 0f, kickAt = 0f, oneAt = 0f;
            var offFrom = new float[4];
            var off = new float[4]; var lastW = new float[4]; var hasW = new bool[4];
            float lastVy = player.Body.linearVelocity.y, lastSp = new Vector2(player.Body.linearVelocity.x, player.Body.linearVelocity.z).magnitude, v0 = lastSp;
            string hit = "";
            var tng = new Vector2(-n.y, n.x);
            float target = gap + 4f;
            while (t < 5f + (gap + 14f) / Mathf.Max(v, 1f))
            {
                var pc = new Vector2(player.Body.position.x, player.Body.position.z);
                float d = Vector2.Dot(pc - mouth, n);
                reached = Mathf.Max(reached, d);
                maxLat = Mathf.Max(maxLat, Mathf.Abs(Vector2.Dot(pc - mouth, tng)));
                if (d >= target) break;
                float sp = new Vector2(player.Body.linearVelocity.x, player.Body.linearVelocity.z).magnitude;
                player.steerInput = 0f;
                player.throttleInput = sp < v - 0.5f ? 1f : 0.2f;
                player.brakeInput = 0f;
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime; t += dt;
                var vel = player.Body.linearVelocity;
                float hsp = new Vector2(vel.x, vel.z).magnitude;
                float kick = Mathf.Abs(vel.y - lastVy) / dt / 9.81f;
                if (kick > maxKick) { maxKick = kick; kickAt = d; }
                maxDecel = Mathf.Max(maxDecel, (lastSp - hsp) / dt / 9.81f);
                lastVy = vel.y; lastSp = hsp;
                int g = 0;
                for (int w = 0; w < 4; w++)
                {
                    var wc = player.wheelContacts[w];
                    if (wc.grounded)
                    {
                        g++; off[w] = 0f;
                        if (hasW[w]) maxStep = Mathf.Max(maxStep, Mathf.Abs(wc.point.y - lastW[w]) - Mathf.Abs(hsp * dt * 0.2f));
                        lastW[w] = wc.point.y; hasW[w] = true;
                    }
                    else
                    {
                        if (off[w] <= 0f) offFrom[w] = d;
                        off[w] += dt;
                        // (a wheel already off before the front wheels are at the pavement's edge is the run-up's, not the entrance's)
                        if (offFrom[w] >= -1.4f && off[w] > maxOne) { maxOne = off[w]; oneAt = offFrom[w]; }
                    }
                }
                allOff = g == 0 ? allOff + dt : 0f;
                maxAll = Mathf.Max(maxAll, allOff);
                // a solid (a pole, a sign, a hydrant, a tree) against the body
                if (hit.Length == 0)
                {
                    var cols = Physics.OverlapBox(player.Body.position + Vector3.up * 0.5f, new Vector3(0.85f, 0.35f, 2.15f), player.transform.rotation, solidMask, QueryTriggerInteraction.Ignore);
                    foreach (var c in cols) if (c.attachedRigidbody != player.Body) { hit = c.name; break; }
                }
                if (hit.Length == 0 && hsp < v0 * 0.5f && t > 0.3f) hit = "something that stopped it (" + maxDecel.ToString("0.0", I) + " g)";
                if (hsp < 0.3f && t > 1.5f) break;   // stopped
            }
            var end = new Vector2(player.Body.position.x, player.Body.position.z);
            GroundY(end.x, end.y, player.Body.position.y - 0.3f, out var under);
            endName = under != null ? under.name : "nothing";
            int lotAt = CityMeshes.LotPavedForTools(map, end);
            res[0] = reached >= gap + 2.5f && lotAt >= 0 ? 1f : 0f;
            res[1] = maxOne; res[2] = reached; res[3] = maxAll; res[4] = maxKick; res[5] = maxDecel;
            res[6] = v0 * 3.6f; res[7] = lastSp * 3.6f; res[8] = Mathf.Max(0f, maxStep); res[9] = maxLat; res[10] = oneAt; res[11] = kickAt;
            hitOut(hit);
            player.throttleInput = 0f; player.brakeInput = 1f;
        }
    }
}
