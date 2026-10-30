using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.City;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// THE FREE-ROAM PERF TOUR (city perf round 1, 2026-10-30; owner, Firefox
    /// on this PC: "in the city FPS is about 59 and dropped as low as 3.
    /// Outside of uptown FPS was better and dropped to 16"). The editor's
    /// probes time the editor; this times THE PLAYER the owner runs, in the
    /// browser he runs it in: a career seeded, FREE ROAM entered through the
    /// front end's own hop, and then the player's car driven by the race AI
    /// down named city routes (the race routes, Trade Street, I-77 south),
    /// every frame timed by <see cref="PerfClock"/> - the player loop's
    /// systems, the time outside the loop, the city's own slices, collections,
    /// heap, where the car was. Each route's frames go to the serving tool as a
    /// CSV, its spikes and a summary (tools\perf-tour.mjs writes them).
    ///
    ///   ?perftour=1[&amp;routes=uptown,tryon,trade,independence,i77s][&amp;secs=150]
    ///             [&amp;hour=Noon][&amp;tag=name][&amp;traffic=1][&amp;view=chase]
    ///             [&amp;shots=x,z,yawDeg,camHeight;...][&amp;ab=switch:0,...]
    ///
    /// The car is the player's own, driven by an AIDriver on a TrackPath built
    /// the way a city race builds one (CityMode.BuildPath); it passes through
    /// the free-roam traffic (no contacts with their layer) so a wreck never
    /// ends a route - the traffic still drives, spawns and costs what it costs.
    /// The focus guard stands aside (a test window loses focus to whatever
    /// else the PC is doing). INERT anywhere but a player served from this
    /// machine (127.0.0.1 / localhost) whose URL asks for it.
    /// </summary>
    public class PerfTour : MonoBehaviour
    {
        public static bool Active { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PSXPerfTour_Post(string name, byte[] data, int length);
#else
        /// <summary>A native player (the Windows standalone): the file is
        /// written to the -perfout folder instead of POSTed.</summary>
        static void PSXPerfTour_Post(string name, byte[] data, int length)
        {
            if (string.IsNullOrEmpty(outDir)) return;
            try
            {
                System.IO.Directory.CreateDirectory(outDir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, name), data);
                Debug.Log("[PerfTour] WROTE " + name + " " + length + " bytes");
            }
            catch (System.Exception e) { Debug.Log("[PerfTour] WRITE FAILED " + name + " " + e.Message); }
        }
#endif
        static string outDir;

        string routesArg, hourArg, tag, viewArg, shotsArg, abArg;
        float maxSecs = 150f, snapEvery;
        bool traffic = true;

        /// <summary>The tour's request: the page URL in a browser (?perftour=1&amp;...),
        /// or, in a native player, <c>-perftour "routes=...&amp;secs=..."</c> (and
        /// <c>-perfout &lt;folder&gt;</c> for the files) on the command line.</summary>
        static string Request()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string url = Application.absoluteURL;
            return ShotLink.LocalHost(url) && ShotLink.Query(url, "perftour") != null ? url : null;
#elif UNITY_EDITOR
            // the editor (Editor/PerfTourEditor.cs): the request in PSX_PERFTOUR, the files to PSX_PERFOUT
            string eq = System.Environment.GetEnvironmentVariable("PSX_PERFTOUR");
            if (eq == null) return null;
            outDir = System.Environment.GetEnvironmentVariable("PSX_PERFOUT");
            if (string.IsNullOrEmpty(outDir)) outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "perftour");
            return "http://127.0.0.1/index.html?perftour=1" + (eq.Length > 0 ? "&" + eq : "");
#else
            var a = System.Environment.GetCommandLineArgs();
            string q = null;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == "-perftour") q = i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : "";
                else if (a[i] == "-perfout" && i + 1 < a.Length) outDir = a[i + 1];
            }
            if (q == null) return null;
            if (string.IsNullOrEmpty(outDir)) outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "perftour");
            return "http://127.0.0.1/index.html?perftour=1" + (q.Length > 0 ? "&" + q : "");
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Active) return;
            string url = Request();
            if (url == null) return;
            Active = true;
            var go = new GameObject("PerfTour");
            DontDestroyOnLoad(go);
            var t = go.AddComponent<PerfTour>();
            t.routesArg = ShotLink.Query(url, "routes") ?? "uptown,tryon,trade,independence,i77s";
            t.hourArg = ShotLink.Query(url, "hour") ?? "";
            t.tag = ShotLink.Query(url, "tag") ?? "run";
            t.viewArg = ShotLink.Query(url, "view") ?? "";
            t.shotsArg = ShotLink.Query(url, "shots") ?? "";
            t.abArg = ShotLink.Query(url, "ab") ?? "";
            string secs = ShotLink.Query(url, "secs");
            if (!string.IsNullOrEmpty(secs)) float.TryParse(secs, NumberStyles.Float, CultureInfo.InvariantCulture, out t.maxSecs);
            // &snap=N: a picture of the drive every N seconds (a LOOK run: the
            // capture is itself a long frame, marked "SNAP" in the frames)
            string snap = ShotLink.Query(url, "snap");
            if (!string.IsNullOrEmpty(snap)) float.TryParse(snap, NumberStyles.Float, CultureInfo.InvariantCulture, out t.snapEvery);
            t.traffic = ShotLink.Query(url, "traffic") != "0";
            PerfClock.Install();
            Debug.Log("[PerfTour] BEGIN routes=" + t.routesArg + " secs=" + t.maxSecs + " hour=" + t.hourArg + " tag=" + t.tag);
        }

        static void Say(string s) => Debug.Log("[PerfTour] " + s);

        void Post(string name, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            PSXPerfTour_Post(tag + "_" + name, bytes, bytes.Length);
        }

        // ------------------------------------------------------------------
        //  the walk to free roam
        // ------------------------------------------------------------------
        static LifeState S => LifeSimManager.State;

        static void SeedCareer()
        {
            if (!LifeSimManager.HasSave || string.IsNullOrEmpty(S.playerName))
                LifeSimManager.StartNewGame("PERF TOUR", 30, LifeRules.DefaultJobIndex);
            if (S.cars.Count == 0)
            {
                var lanes = CarMarket.RollStartingLanes(S.basePay, S.creditScore);
                if (lanes.Count > 0) CarMarket.ApplyStartingLane(S, lanes[0]);
                else LifeRules.SeedFallbackCar(S);
            }
            if (S.ActiveCar != null) S.ActiveCar.fuel = 100f;
            LifeSimManager.Save();
        }

        int HourIndex()
        {
            if (string.IsNullOrEmpty(hourArg)) return -1;
            if (int.TryParse(hourArg, out int i) && i >= 0 && i < TimeOfDay.All.Length) return i;
            for (int k = 0; k < TimeOfDay.All.Length; k++)
                if (string.Equals(TimeOfDay.All[k].name, hourArg, System.StringComparison.OrdinalIgnoreCase)) return k;
            return -1;
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 30; i++) yield return null;
            ApplyAB();
            try { SeedCareer(); }
            catch (System.Exception e) { Say("FAIL the career would not seed: " + e.Message); }
            int hour = HourIndex();
            LifeHomeScreen.PendingTab = "charlotte";
            LifeSimManager.Save();
            SceneManager.LoadScene(0);
            float t0 = Time.realtimeSinceStartup;
            CityMode mode = null;
            while (Time.realtimeSinceStartup - t0 < 180f)
            {
                if (hour >= 0 && RaceHandoff.FreeRoam) RaceHandoff.TimeOfDayIndex = hour;
                mode = CityMode.Instance;
                if (mode != null && mode.player != null && mode.world != null && mode.world.Map != null && mode.Live) break;
                yield return null;
            }
            if (mode == null || mode.player == null || mode.world == null || !mode.Live)
            {
                Say("FAIL free roam never came up (scene " + SceneManager.GetActiveScene().name + ")");
                Post("done.txt", "FAIL free roam never came up");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(1);
#elif !UNITY_WEBGL
                Application.Quit();
#endif
                yield break;
            }
            float loadS = Time.realtimeSinceStartup - t0;
            int h = RaceHandoff.TimeOfDayIndex;
            string hourName = h >= 0 && h < TimeOfDay.All.Length ? TimeOfDay.All[h].name : "?";
            var env = new StringBuilder();
            env.AppendLine("tag " + tag);
            env.AppendLine("os " + SystemInfo.operatingSystem);
            env.AppendLine("gpu " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceVersion + " | " + SystemInfo.graphicsDeviceVendor);
            env.AppendLine("screen " + Screen.width + "x" + Screen.height + " dpi " + Screen.dpi);
            env.AppendLine("hour " + hourName + " (" + h + ") weather " + Seasons.CurrentWeather + " traffic " + (traffic ? "on" : "off") + " view " + ChaseCamera.Current);
            env.AppendLine("free roam up in " + loadS.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            env.AppendLine("heap used MB " + (System.GC.GetTotalMemory(false) / 1048576.0).ToString("0", CultureInfo.InvariantCulture) +
                           " reserved MB " + (UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576.0).ToString("0", CultureInfo.InvariantCulture) +
                           " gc count " + System.GC.CollectionCount(0));
            env.AppendLine("ab " + abArg);
            env.AppendLine("systems " + string.Join(" ", PerfClock.SystemNames));
            Post("env.txt", env.ToString());
            Say("free roam up: " + env.ToString().Replace("\n", " | "));

            if (!traffic && CityTraffic.Instance != null) CityTraffic.Instance.gameObject.SetActive(false);
            if (!string.IsNullOrEmpty(viewArg) && System.Enum.TryParse(viewArg, true, out ChaseCamera.View v)) ChaseCamera.PreviewView(v);

            if (!string.IsNullOrEmpty(shotsArg)) yield return Shots(mode);
            else
                foreach (var id in routesArg.Split(','))
                {
                    if (string.IsNullOrEmpty(id.Trim())) continue;
                    yield return Drive(mode, id.Trim());
                }
            Post("done.txt", "DONE " + tag);
            Say("DONE " + tag);
#if UNITY_EDITOR
            yield return new WaitForSecondsRealtime(1f);
            UnityEditor.EditorApplication.Exit(0);
#elif !UNITY_WEBGL
            // a native player's tour is its whole run
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
#endif
        }

        /// <summary>?ab=Name:0,Name:1 - a static bool switch on a city class set
        /// before the world is built (the A/B in one player).</summary>
        void ApplyAB()
        {
            if (string.IsNullOrEmpty(abArg)) return;
            foreach (var part in abArg.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length != 2) continue;
                bool val = kv[1].Trim() != "0";
                int dot = kv[0].LastIndexOf('.');
                if (dot <= 0) { Say("ab: '" + kv[0] + "' is not Class.Field"); continue; }
                var type = System.Type.GetType("PSXRacing.City." + kv[0].Substring(0, dot)) ?? System.Type.GetType("PSXRacing." + kv[0].Substring(0, dot));
                var f = type?.GetField(kv[0].Substring(dot + 1), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (f == null || f.FieldType != typeof(bool)) { Say("ab: no static bool " + kv[0]); continue; }
                f.SetValue(null, val);
                Say("ab: " + kv[0] + " = " + val);
            }
        }

        // ------------------------------------------------------------------
        //  the routes
        // ------------------------------------------------------------------
        static Vector2 LL(double lat, double lon)
        {
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale;
        }

        /// <summary>A road followed node to node from the nearest point of a
        /// named road heading toward a target, the straightest way on that keeps
        /// the name (HitchProbe's FollowRoad, as a route).</summary>
        static CityMap.Route FollowRoad(CityMap map, string id, Vector2 from, Vector2 toward, string nameHas, float maxM)
        {
            CityMap.Edge e = null; float bestD = float.MaxValue; bool fwd = true;
            foreach (var c in map.edges)
            {
                if (c.name == null || !c.name.Contains(nameHas) || c.pts == null || c.pts.Length < 2 || c.link) continue;
                for (int i = 0; i + 1 < c.pts.Length; i++)
                {
                    float d = (c.pts[i] - from).sqrMagnitude;
                    if (d >= bestD) continue;
                    var tc = c.pts[i + 1] - c.pts[i];
                    bool f = Vector2.Dot(tc, toward - from) >= 0f;
                    if (c.oneway && !f) continue;
                    bestD = d; e = c; fwd = f;
                }
            }
            if (e == null) return null;
            var edges = new List<int>(); var dirs = new List<sbyte>();
            float run = 0f;
            var seen = new HashSet<int>();
            for (int guard = 0; guard < 4000 && run < maxM && e != null; guard++)
            {
                seen.Add(e.index);
                edges.Add(e.index); dirs.Add((sbyte)(fwd ? 1 : -1));
                run += e.length;
                int n = fwd ? e.b : e.a;
                var dirIn = fwd ? e.pts[e.pts.Length - 1] - e.pts[e.pts.Length - 2] : e.pts[0] - e.pts[1];
                dirIn.Normalize();
                CityMap.Edge best = null; bool bestFwd = true; float bestScore = float.MinValue;
                foreach (int oi in map.nodeEdges[n])
                {
                    var o = map.edges[oi];
                    if (o == e || seen.Contains(oi) || o.pts.Length < 2) continue;
                    bool of = o.a == n;
                    if (o.oneway && !of) continue;
                    var dOut = of ? o.pts[1] - o.pts[0] : o.pts[o.pts.Length - 2] - o.pts[o.pts.Length - 1];
                    dOut.Normalize();
                    float score = Vector2.Dot(dirIn, dOut) + (o.name != null && o.name.Contains(nameHas) ? 2f : 0f) - (o.link ? 1f : 0f);
                    if (score > bestScore) { bestScore = score; best = o; bestFwd = of; }
                }
                if (best == null || bestScore < 1.5f) break;
                e = best; fwd = bestFwd;
            }
            if (edges.Count == 0) return null;
            var first = map.edges[edges[0]];
            return new CityMap.Route
            {
                id = id, name = id, loop = false, oneway = true, roadWidth = Mathf.Max(8f, first.width),
                speedKmh = first.speedKmh, lengthM = run, startM = 0f, finishM = run,
                edges = edges.ToArray(), dirs = dirs.ToArray(),
            };
        }

        static CityMap.Route RouteFor(CityMap map, string id)
        {
            var r = map.RouteById(id);
            if (r != null) return r;
            switch (id)
            {
                // West Trade from I-77 through the Square, then East Trade to the 277
                case "trade": return FollowRoad(map, id, LL(35.2372, -80.8566), LL(35.2190, -80.8320), "Trade Street", 4000f);
                // I-77 south out of uptown, past the 277 to the south side
                case "i77s": return FollowRoad(map, id, LL(35.2290, -80.8600), LL(35.1000, -80.8900), "I-77", 14000f);
                // I-77 north from uptown toward the north side
                case "i77n": return FollowRoad(map, id, LL(35.2290, -80.8580), LL(35.3200, -80.8400), "I-77", 14000f);
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  one route, driven and timed
        // ------------------------------------------------------------------
        const int Cap = 72000;
        // per-frame columns
        float[] cT, cMs, cOut, cFix, cPhys, cFixS, cUpd, cLate, cRend, cUi, cUsed, cX, cZ, cV;
        byte[] cSteps, cGc, cTiles;
        float[][] cSlot;
        string[] cEv;
        int n;
        bool recording;
        float recT0;
        int gcWas;
        double trafWas = -1.0;
        string evNow;
        CarController car;
        int iPhys = -1, iFixS = -1, iUpd = -1, iLate = -1, iRend = -1;
        readonly List<int> iUi = new List<int>(), iFix = new List<int>();
        readonly StringBuilder spikes = new StringBuilder();
        int spikeLines;

        void Alloc()
        {
            if (cT != null) return;
            cT = new float[Cap]; cMs = new float[Cap]; cOut = new float[Cap]; cFix = new float[Cap]; cPhys = new float[Cap];
            cFixS = new float[Cap]; cUpd = new float[Cap]; cLate = new float[Cap]; cRend = new float[Cap]; cUi = new float[Cap];
            cUsed = new float[Cap]; cX = new float[Cap]; cZ = new float[Cap]; cV = new float[Cap];
            cSteps = new byte[Cap]; cGc = new byte[Cap]; cTiles = new byte[Cap]; cEv = new string[Cap];
            cSlot = new float[(int)PerfClock.Slot.COUNT][];
            for (int s = 0; s < cSlot.Length; s++) cSlot[s] = new float[Cap];
            var names = PerfClock.SystemNames;
            for (int i = 0; i < names.Length; i++)
            {
                string nm = names[i];
                if (nm == "FixedUpdate.PhysicsFixedUpdate") iPhys = i;
                else if (nm == "FixedUpdate.ScriptRunBehaviourFixedUpdate") iFixS = i;
                else if (nm == "Update.ScriptRunBehaviourUpdate") iUpd = i;
                else if (nm == "PreLateUpdate.ScriptRunBehaviourLateUpdate") iLate = i;
                else if (nm == "PostLateUpdate.FinishFrameRendering") iRend = i;
                if (nm.StartsWith("FixedUpdate.")) iFix.Add(i);
                if (nm.Contains("Canvas") || nm.Contains("UpdateRectTransform") || nm.Contains("UpdateAllRenderers")) iUi.Add(i);
            }
            CityWorld.TileBuilt += t => { if (recording) evNow = t.tx + ":" + t.tz + (t.treeFrame ? "T" : t.slices > 0 ? "S" : "W"); };
        }

        static float Sys(int i) => i >= 0 && i < PerfClock.SystemMs.Length ? (float)PerfClock.SystemMs[i] : 0f;

        void OnFrame(double frameMs, double outsideMs)
        {
            if (!recording || n >= Cap) { evNow = null; return; }
            int i = n++;
            cT[i] = Time.realtimeSinceStartup - recT0;
            cMs[i] = (float)frameMs; cOut[i] = (float)outsideMs;
            float fix = 0f; foreach (int k in iFix) fix += Sys(k);
            float ui = 0f; foreach (int k in iUi) ui += Sys(k);
            cFix[i] = fix; cPhys[i] = Sys(iPhys); cFixS[i] = Sys(iFixS); cUpd[i] = Sys(iUpd); cLate[i] = Sys(iLate); cRend[i] = Sys(iRend); cUi[i] = ui;
            cSteps[i] = (byte)Mathf.Min(255, PerfClock.FixedSteps);
            int gc = System.GC.CollectionCount(0);
            cGc[i] = (byte)Mathf.Clamp(gc - gcWas, 0, 255);
            gcWas = gc;
            cUsed[i] = (float)(System.GC.GetTotalMemory(false) / 1048576.0);
            if (car != null)
            {
                var p = car.transform.position;
                cX[i] = p.x; cZ[i] = p.z; cV[i] = Mathf.Abs(car.forwardSpeed);
            }
            var w = CityWorld.Active;
            cTiles[i] = (byte)(w != null ? Mathf.Min(255, w.LiveTiles) : 0);
            // the traffic's physics steps time themselves (CityTraffic.StepMsSum)
            var ct = CityTraffic.Instance;
            if (ct != null)
            {
                double st = ct.StepMsSum;
                if (trafWas >= 0.0 && st >= trafWas) PerfClock.Slots[(int)PerfClock.Slot.TrafficFixed] = st - trafWas;
                trafWas = st;
            }
            for (int s = 0; s < cSlot.Length; s++) cSlot[s][i] = (float)PerfClock.Slots[s];
            cEv[i] = evNow;
            evNow = null;
            if (frameMs > 33.4 && spikeLines < 1500)
            {
                spikeLines++;
                spikes.Append(i).Append(' ').Append(cT[i].ToString("0.000", CultureInfo.InvariantCulture)).Append(' ')
                      .Append(frameMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(" out ").Append(outsideMs.ToString("0.0", CultureInfo.InvariantCulture));
                if (cGc[i] > 0) spikes.Append(" GC");
                spikes.Append(" |");
                var sm = PerfClock.SystemMs;
                for (int k = 0; k < sm.Length; k++)
                    if (sm[k] >= 1.0) spikes.Append(' ').Append(PerfClock.SystemNames[k]).Append('=').Append(sm[k].ToString("0.0", CultureInfo.InvariantCulture));
                spikes.Append(" |");
                for (int s = 0; s < cSlot.Length; s++)
                    if (PerfClock.Slots[s] >= 0.5) spikes.Append(' ').Append(PerfClock.SlotNames[s].Replace(' ', '_')).Append('=').Append(PerfClock.Slots[s].ToString("0.0", CultureInfo.InvariantCulture));
                if (cEv[i] != null) spikes.Append(" | ev ").Append(cEv[i]);
                spikes.Append(" | at ").Append(cX[i].ToString("0", CultureInfo.InvariantCulture)).Append(',').Append(cZ[i].ToString("0", CultureInfo.InvariantCulture))
                      .Append(" v ").Append(cV[i].ToString("0.0", CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        IEnumerator Drive(CityMode mode, string id)
        {
            var world = mode.world;
            var map = world.Map;
            car = mode.player;
            var route = RouteFor(map, id);
            if (route == null || route.edges == null || route.edges.Length == 0) { Say("SKIP " + id + ": no such route"); Post(id + "_summary.txt", "SKIP no such route"); yield break; }
            var pgo = new GameObject("PerfTourPath_" + id);
            var path = pgo.AddComponent<TrackPath>();
            CityMode.BuildPath(map, route, path);
            if (path.Count < 10) { Say("SKIP " + id + ": path too short"); Destroy(pgo); yield break; }
            float pathM = path.Count * path.spacing;

            // to the start, the ground under it first, then the ring streamed in
            int i0 = route.loop ? 0 : 2;
            var pos = path.GetPoint(i0) + Vector3.up * 0.45f;
            var rot = Quaternion.LookRotation(Flat(path.GetTangent(i0)), Vector3.up);
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var old = car.GetComponent<AIDriver>();
            if (old != null) Destroy(old);
            yield return null;
            world.EnsureRing(pos, 1);
            car.ResetTo(pos, rot, seated: true);
            float settle0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - settle0 < 60f)
            {
                if (world.LiveTiles >= 25 && world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - settle0 > 6f) break;
                car.ResetTo(pos, rot, seated: true);
                yield return null;
            }
            float settleS = Time.realtimeSinceStartup - settle0;
            yield return new WaitForSecondsRealtime(1.5f);

            // the race AI at the wheel, through the traffic
            foreach (var col in car.GetComponentsInChildren<Collider>(true))
                if (col.gameObject.layer != 2) Physics.IgnoreLayerCollision(col.gameObject.layer, 2, true);
            car.allowReverse = false;
            var ai = car.gameObject.AddComponent<AIDriver>();
            ai.path = path;
            ai.skill = 0.95f;
            ai.ReseedPath();
            var fuel = car.GetComponent<FuelTank>();
            if (fuel != null) fuel.percent = 100f;

            Alloc();
            n = 0; spikes.Length = 0; spikeLines = 0;
            gcWas = System.GC.CollectionCount(0);
            int gc0 = gcWas;
            long heap0 = System.GC.GetTotalMemory(false);
            long res0 = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
            // rolling: the clock starts when the car is moving
            float wait0 = Time.realtimeSinceStartup;
            while (Mathf.Abs(car.forwardSpeed) < 3f && Time.realtimeSinceStartup - wait0 < 15f) yield return null;
            recT0 = Time.realtimeSinceStartup;
            trafWas = CityTraffic.Instance != null ? CityTraffic.Instance.StepMsSum : -1.0;
            PerfClock.FrameDone = OnFrame;
            recording = true;
            Say("DRIVE " + id + " " + pathM.ToString("0") + " m path, settled in " + settleS.ToString("0.0") + " s");

            float dist = 0f; var last = car.transform.position;
            float slowFor = 0f;
            string end = "time";
            float nextSnap = snapEvery > 0f ? 2f : float.MaxValue;
            int snapN = 0;
            while (Time.realtimeSinceStartup - recT0 < maxSecs)
            {
                if (Time.realtimeSinceStartup - recT0 >= nextSnap)
                {
                    nextSnap += snapEvery;
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    var png = tex.EncodeToPNG();
                    Destroy(tex);
                    var cp = car.transform.position;
                    PSXPerfTour_Post(tag + "_" + id + "_snap" + (++snapN).ToString("00") + "_" + cp.x.ToString("0") + "_" + cp.z.ToString("0") + ".png", png, png.Length);
                    evNow = "SNAP";
                }
                yield return null;
                var p = car.transform.position;
                float step = Vector3.Distance(p, last);
                if (step < 60f) dist += step;
                last = p;
                if (Mathf.Abs(car.forwardSpeed) < 2f) slowFor += Time.unscaledDeltaTime; else slowFor = 0f;
                if (slowFor > 12f) { end = "stuck at " + p.x.ToString("0") + "," + p.z.ToString("0"); break; }
                if (!route.loop && ai.PathIndex >= path.Count - 12) { end = "route end"; break; }
                if (route.loop && dist >= pathM) { end = "lap"; break; }
                if (n >= Cap) { end = "frame cap"; break; }
            }
            recording = false;
            PerfClock.FrameDone = null;
            float secs = Time.realtimeSinceStartup - recT0;
            int gcN = System.GC.CollectionCount(0) - gc0;
            long heap1 = System.GC.GetTotalMemory(false);
            long res1 = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
            Destroy(ai);
            car.brakeInput = 1f; car.throttleInput = 0f;
            Destroy(pgo);

            // ---- the report ----
            var csv = new StringBuilder(n * 160 + 1024);
            csv.Append("i,t,ms,out,fix,phys,fixs,upd,late,rend,ui,steps,gc,usedMB,x,z,v,tiles,ev");
            for (int s = 0; s < cSlot.Length; s++) csv.Append(',').Append(PerfClock.SlotNames[s].Replace(' ', '_'));
            csv.Append('\n');
            var ci = CultureInfo.InvariantCulture;
            for (int i = 0; i < n; i++)
            {
                csv.Append(i).Append(',').Append(cT[i].ToString("0.000", ci)).Append(',').Append(cMs[i].ToString("0.00", ci)).Append(',')
                   .Append(cOut[i].ToString("0.00", ci)).Append(',').Append(cFix[i].ToString("0.00", ci)).Append(',').Append(cPhys[i].ToString("0.00", ci)).Append(',')
                   .Append(cFixS[i].ToString("0.00", ci)).Append(',').Append(cUpd[i].ToString("0.00", ci)).Append(',').Append(cLate[i].ToString("0.00", ci)).Append(',')
                   .Append(cRend[i].ToString("0.00", ci)).Append(',').Append(cUi[i].ToString("0.00", ci)).Append(',').Append(cSteps[i]).Append(',').Append(cGc[i]).Append(',')
                   .Append(cUsed[i].ToString("0.0", ci)).Append(',').Append(cX[i].ToString("0.0", ci)).Append(',').Append(cZ[i].ToString("0.0", ci)).Append(',')
                   .Append(cV[i].ToString("0.0", ci)).Append(',').Append(cTiles[i]).Append(',').Append(cEv[i] ?? "");
                for (int s = 0; s < cSlot.Length; s++) csv.Append(',').Append(cSlot[s][i].ToString("0.00", ci));
                csv.Append('\n');
            }
            var ms = new float[n];
            System.Array.Copy(cMs, ms, n);
            System.Array.Sort(ms);
            float P(float q) => n > 0 ? ms[Mathf.Clamp(Mathf.RoundToInt(q * (n - 1)), 0, n - 1)] : 0f;
            int over33 = 0, over50 = 0, over100 = 0; double sum = 0;
            for (int i = 0; i < n; i++) { sum += ms[i]; if (ms[i] > 33.4f) over33++; if (ms[i] > 50f) over50++; if (ms[i] > 100f) over100++; }
            var sum1 = new StringBuilder();
            sum1.AppendLine($"route {id} ({route.name}) {pathM:0} m path; ended: {end}; {secs:0.0} s, {dist:0} m driven, avg {(secs > 0 ? dist / secs * 3.6f : 0):0} km/h");
            sum1.AppendLine($"frames {n}  avg fps {(sum > 0 ? n * 1000.0 / sum : 0):0.0}  p50 {P(0.5f):0.0}  p95 {P(0.95f):0.0}  p99 {P(0.99f):0.0}  worst {P(1f):0.0} ms");
            sum1.AppendLine($"over 33 ms {over33}  over 50 ms {over50}  over 100 ms {over100}");
            sum1.AppendLine($"gc collections {gcN}  heap used MB {heap0 / 1048576.0:0} -> {heap1 / 1048576.0:0}  reserved MB {res0 / 1048576.0:0} -> {res1 / 1048576.0:0}");
            sum1.AppendLine($"settle {settleS:0.0} s; tiles abandoned so far {CityWorld.JobsAbandoned}");
            Post(id + "_frames.csv", csv.ToString());
            Post(id + "_spikes.txt", spikes.ToString());
            Post(id + "_summary.txt", sum1.ToString());
            Say("ROUTE " + sum1.ToString().Replace("\n", " | "));
            yield return new WaitForSecondsRealtime(1f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        // ------------------------------------------------------------------
        //  shots (phase 2: the same spot before and after, from the road)
        // ------------------------------------------------------------------
        IEnumerator Shots(CityMode mode)
        {
            var world = mode.world;
            var map = world.Map;
            car = mode.player;
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            int k = 0;
            foreach (var spec in shotsArg.Split(';'))
            {
                var f = spec.Split(',');
                if (f.Length < 3) continue;
                float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
                float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
                float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw);
                string name = f.Length > 3 && f[3].Length > 0 ? f[3] : "shot" + (++k);
                if (!map.NearestRoadPoint(new Vector2(x, z), 200f, skipLinks: false, out int ei, out float s, out _)) { Say("SHOT " + name + ": no road near"); continue; }
                var e = map.edges[ei];
                var q = LineModel.LanePoint(e, s);
                var pos = new Vector3(q.x, e.YAt(s) + 0.45f, q.y);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                world.EnsureRing(pos, 1);
                car.ResetTo(pos, rot, seated: true);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 90f)
                {
                    car.ResetTo(pos, rot, seated: true);
                    if (world.LiveTiles >= 25 && world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - t0 > 8f) break;
                    yield return null;
                }
                // the far layers settle (chunks, proxies, a swap a second)
                for (int i = 0; i < 240; i++) { car.ResetTo(pos, rot, seated: true); yield return null; }
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var png = tex.EncodeToPNG();
                Destroy(tex);
                PSXPerfTour_Post(tag + "_" + name + ".png", png, png.Length);
                Say("SHOT " + name + " at " + pos.x.ToString("0") + "," + pos.z.ToString("0") + " yaw " + yaw.ToString("0"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }
    }
}
