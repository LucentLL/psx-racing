using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using PSXRacing.City;
using PSXRacing.LifeSim;
using PSXRacing.People;

namespace PSXRacing
{
    /// <summary>
    /// THE PEOPLE COST TEST (people, Stage 1 of Characters_Unity_Plan, 2026-11-26): the town's meet lot on a
    /// meet night, the player's car parked in the aisle, and the test mannequin crowd (People/PeopleCrowd)
    /// shown by each technique at each count in alternating blocks INSIDE ONE RUN, each block beside a block
    /// with no figures (the baseline). A variant "off" that shows nothing is measured the same way: its delta
    /// is the run's own noise (the A/A column).
    ///
    ///   PSX_PERFTOUR="place=town&amp;stand=meet&amp;people=10,20,40&amp;peopletech=A,B,D&amp;hour=Night&amp;weather=Clear
    ///                 [&amp;offscreen=1920x1080][&amp;pcfg=reps,blockSecs,settleSecs][&amp;peopleshot=1]&amp;tag=..."
    ///
    /// The main view is drawn into a texture of the asked size and the frame waits for the GPU (the frame
    /// probe's ProbeView), so a frame's time carries main thread + render thread + GPU. Output:
    /// &lt;tag&gt;_town_people.txt (the table), _town_people.csv (every variant's numbers), a picture of each
    /// technique at the largest count, and with peopleshot=1 the night frame under a lamp beside a meet car.
    /// </summary>
    public partial class PerfTour
    {
        string placeArg = "", standArg = "", peopleArg = "", peopleTechArg = "", peopleShotArg = "";

        const int PP_FRAME = 0, PP_CALL = 1, PP_WAIT = 2, PP_MAIN = 3, PP_RT = 4, PP_GPU = 5, PP_DRAWS = 6, PP_BATCH = 7,
                  PP_SETPASS = 8, PP_TRIS = 9, PP_SKIN = 10, PP_ANIM = 11, PP_N = 12;
        static readonly string[] PpNames = { "frame_ms", "view_call_ms", "gpu_wait_ms", "main_thread_ms(FTM)", "render_thread_ms(FTM)", "gpu_ms(FTM)",
                                             "draw_calls", "batches", "setpass", "triangles_M", "skinning_markers_ms", "animation_markers_ms" };

        List<float>[] ppInto;
        ProfilerRecorder ppDraws, ppBatch, ppSetPass, ppTris;
        readonly List<(string name, ProfilerRecorder rec, bool skin)> ppMarkers = new List<(string, ProfilerRecorder, bool)>();
        readonly FrameTiming[] ppFt = new FrameTiming[1];
        CarController ppCar;

        static List<float>[] PpBlock() { var b = new List<float>[PP_N]; for (int i = 0; i < PP_N; i++) b[i] = new List<float>(256); return b; }

        void PeopleFrame(double frameMs, double outsideMs)
        {
            if (Application.targetFrameRate != -1 || QualitySettings.vSyncCount != 0) { Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0; }
            if (ppCar != null) { ppCar.throttleInput = 0f; ppCar.brakeInput = 1f; }
            var into = ppInto;
            if (into == null) return;
            into[PP_FRAME].Add((float)frameMs);
            into[PP_CALL].Add((float)PerfClock.Slots[(int)PerfClock.Slot.OffscreenCpu]);
            into[PP_WAIT].Add((float)PerfClock.Slots[(int)PerfClock.Slot.OffscreenGpu]);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ppFt) > 0)
            {
                into[PP_MAIN].Add((float)ppFt[0].cpuMainThreadFrameTime);
                into[PP_RT].Add((float)ppFt[0].cpuRenderThreadFrameTime);
                into[PP_GPU].Add((float)ppFt[0].gpuFrameTime);
            }
            into[PP_DRAWS].Add(ppDraws.Valid ? ppDraws.LastValue : 0f);
            into[PP_BATCH].Add(ppBatch.Valid ? ppBatch.LastValue : 0f);
            into[PP_SETPASS].Add(ppSetPass.Valid ? ppSetPass.LastValue : 0f);
            into[PP_TRIS].Add(ppTris.Valid ? ppTris.LastValue / 1e6f : 0f);
            double skin = 0, anim = 0;
            foreach (var m in ppMarkers) { double v = m.rec.Valid ? m.rec.LastValue * 1e-6 : 0; if (m.skin) skin += v; else anim += v; }
            into[PP_SKIN].Add((float)skin);
            into[PP_ANIM].Add((float)anim);
        }

        static float Pct(List<float> v, float p)
        {
            if (v == null || v.Count == 0) return 0f;
            var c = new List<float>(v); c.Sort();
            return c[Mathf.Clamp(Mathf.CeilToInt(p * c.Count) - 1, 0, c.Count - 1)];
        }

        IEnumerator TownPeople(int hour, int weather)
        {
            var ci = CultureInfo.InvariantCulture;
            // ---- a meet night: day 1 of a career is a Friday; the night block
            var s = LifeSimManager.State;
            if (s.day < 1) s.day = 1;
            while (!CarMeets.MeetOn(s.day)) s.day++;
            s.slotIndex = LifeRules.NightSlot;
            LifeSimManager.Save();
            int idx = TrackCatalog.TownSceneIndex;
            if (idx <= 0) { Say("FAIL no Town scene in the build settings"); yield break; }
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.CarId = s.activeCar;
            RaceHandoff.CarSpecId = s.ActiveCar != null ? s.ActiveCar.specId : "";
            RaceHandoff.TimeOfDayIndex = hour >= 0 ? hour : TimeOfDay.Night;
            RaceHandoff.WeatherOverride = weather;
            RaceHandoff.StartFuelPct = 100f;
            SceneGate.Load(idx);
            float t0 = Time.realtimeSinceStartup;
            CityMode mode = null; PSXRacing.Town.TownWorld tw = null;
            while (Time.realtimeSinceStartup - t0 < 180f)
            {
                mode = CityMode.Instance;
                tw = FindAnyObjectByType<PSXRacing.Town.TownWorld>();
                if (mode != null && mode.player != null && mode.Live && tw != null && tw.meetSpots != null && tw.meetSpots.Length > 0) break;
                yield return null;
            }
            if (mode == null || mode.player == null || tw == null || tw.meetSpots == null || tw.meetSpots.Length == 0)
            { Say("FAIL the town's meet lot never came up"); yield break; }
            float loadS = Time.realtimeSinceStartup - t0;
            ppCar = mode.player;
            // ---- stand: the car in the near aisle, nose down it (east), the lot's rows either side
            float y = tw.meetSpots[0].position.y;
            var stand = new Vector3(99f, y + 0.4f, 21.8f);
            ppCar.TeleportTo(stand, Quaternion.Euler(0f, 90f, 0f));
            PerfClock.FrameDone = PeopleFrame;
            for (float w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w < 8f;) yield return null;
            var cam = Camera.main;
            if (cam == null) { Say("FAIL no main camera in the town"); PerfClock.FrameDone = null; yield break; }
            int cars = 0;
            foreach (var sp in tw.meetSpots) if (sp != null && sp.GetComponentInChildren<MeshRenderer>() != null) cars++;

            // ---- the crowd, every technique built for the largest count
            var counts = new List<int>(); var techs = new List<PeopleCrowd.Tech>();
            foreach (var c in (string.IsNullOrEmpty(peopleArg) ? "10,20,40" : peopleArg).Split(',', '|'))
                if (int.TryParse(c, out int n) && n > 0) counts.Add(n);
            foreach (var c in (string.IsNullOrEmpty(peopleTechArg) ? "A,B,D" : peopleTechArg).Split(',', '|'))
                if (System.Enum.TryParse(c.Trim(), true, out PeopleCrowd.Tech t)) techs.Add(t);
            int most = 0; foreach (var n in counts) most = Mathf.Max(most, n);
            var memRec = new[] { ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory"), ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Mesh Memory"),
                                 ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory") };
            yield return null; yield return null;
            string memBefore = $"texture {memRec[0].LastValue / 1048576f:0.0} MB, mesh {memRec[1].LastValue / 1048576f:0.0} MB, total used {memRec[2].LastValue / 1048576f:0} MB";
            var crowd = PeopleCrowd.Build(PeopleCrowd.MeetSpots(tw.meetSpots, cam, most), PlatformProfile.PC);
            crowd.Show(PeopleCrowd.Tech.B, 0);
            yield return null; yield return null;
            string memAfter = $"texture {memRec[0].LastValue / 1048576f:0.0} MB, mesh {memRec[1].LastValue / 1048576f:0.0} MB, total used {memRec[2].LastValue / 1048576f:0} MB";
            foreach (var r in memRec) r.Dispose();

            // ---- the offscreen view (the frame probe's way)
            int ow = 1920, oh = 1080;
            if (!string.IsNullOrEmpty(offscreenArg))
            {
                var wh = offscreenArg.ToLowerInvariant().Split('x');
                if (wh.Length == 2) { int.TryParse(wh[0], out ow); int.TryParse(wh[1], out oh); }
            }
            var full = new RenderTexture(ow, oh, PlatformProfile.PC ? 32 : 24, RenderTextureFormat.Default) { antiAliasing = 1, name = "PeopleProbeFull" };
            full.Create();
            var screenRt = new RenderTexture(ow, oh, 0, RenderTextureFormat.Default) { antiAliasing = 1, name = "PeopleProbeScreen" };
            screenRt.Create();
            var holder = new GameObject("PeopleProbe");
            holder.SetActive(false);
            var view = holder.AddComponent<ProbeView>();
            view.cam = cam; view.rt = full; view.screen = screenRt;
            view.Hold();
            holder.SetActive(true);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;

            // ---- warm every technique (shader variants, skin buffers), then find the profiler's skinning and
            //      animation markers (they exist once something has used them)
            string aCheck = "";
            foreach (var t in techs)
            {
                crowd.Show(t, most);
                for (float w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w < 3f;) yield return null;
                if (t == PeopleCrowd.Tech.A)
                {
                    // is the Animator really posing the bones? two looks at a walker's thigh 0.5 s apart
                    string a1 = crowd.APose();
                    for (float w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w < 0.5f;) yield return null;
                    aCheck = "A animates: thigh " + a1 + " then " + crowd.APose();
                    Say(aCheck);
                }
            }
            crowd.Show(PeopleCrowd.Tech.B, 0);
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var found = new StringBuilder();
            var seen = new StringBuilder();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                string n = d.Name;
                if (n.Contains("Animat") || n.Contains("Director") || n.Contains("Playable")) seen.Append(n).Append(" [").Append(d.UnitType).Append("]; ");
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                // the top-level ones only (summing nested markers counts the same time twice)
                bool skin = n == "MeshSkinning.Update";
                bool anim = n == "Animators.Update" || n == "Director.ProcessFrame" || n == "Director.PrepareFrame" ||
                            n == "PreLateUpdate.DirectorUpdateAnimationBegin" || n == "PreLateUpdate.DirectorUpdateAnimationEnd" || n == "Animation.Update";
                if (!skin && !anim) continue;
                if (ppMarkers.Count >= 24) break;
                ppMarkers.Add((n, ProfilerRecorder.StartNew(d.Category, n), skin));
                found.Append(n).Append(skin ? " (skin); " : " (anim); ");
            }
            if (aCheck.Length > 0) found.Append(" | ").Append(aCheck);
            found.Append(" | animation-like markers registered: ").Append(seen);
            ppDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            ppBatch = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            ppSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            ppTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

            int reps = 6; float blockS = 10f, settleS = 1f;
            if (!string.IsNullOrEmpty(probeCfg))
            {
                var c = probeCfg.Split(',');
                if (c.Length > 0) int.TryParse(c[0], out reps);
                if (c.Length > 1) float.TryParse(c[1], NumberStyles.Float, ci, out blockS);
                if (c.Length > 2) float.TryParse(c[2], NumberStyles.Float, ci, out settleS);
                reps = Mathf.Clamp(reps, 1, 30); blockS = Mathf.Clamp(blockS, 1f, 60f); settleS = Mathf.Clamp(settleS, 0.2f, 10f);
            }
            var vars = new List<(string name, PeopleCrowd.Tech t, int n)> { ("off(A/A)", PeopleCrowd.Tech.B, 0) };
            foreach (var t in techs) foreach (var n in counts) vars.Add((t + n.ToString(), t, n));
            var onAll = new List<float>[vars.Count][]; var offAll = new List<float>[vars.Count][];
            var dRep = new List<float>[vars.Count, PP_N];
            for (int v = 0; v < vars.Count; v++) { onAll[v] = PpBlock(); offAll[v] = PpBlock(); for (int m = 0; m < PP_N; m++) dRep[v, m] = new List<float>(); }
            var snaps = new Dictionary<string, byte[]>();
            Say($"PEOPLE begin: {vars.Count} variants x {reps} reps x ({settleS:0.#} + {blockS:0.#} s) each way, view {ow}x{oh}, profile {(PlatformProfile.PC ? "PC" : "non-PC")}");
            for (int r = 0; r < reps; r++)
            {
                for (int k = 0; k < vars.Count; k++)
                {
                    int v = (k + r) % vars.Count;   // each rep starts one variant later: no variant always follows the same one
                    crowd.Show(vars[v].t, vars[v].n);
                    for (float w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w < settleS;) yield return null;
                    var on = PpBlock(); ppInto = on;
                    for (float w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w < blockS;) yield return null;
                    ppInto = null;
                    if (r == 0 && vars[v].n == most && !snaps.ContainsKey(vars[v].name)) snaps[vars[v].name] = view.Snap(1920);
                    crowd.Show(PeopleCrowd.Tech.B, 0);
                    for (float w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w < settleS;) yield return null;
                    var off = PpBlock(); ppInto = off;
                    for (float w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w < blockS;) yield return null;
                    ppInto = null;
                    for (int m = 0; m < PP_N; m++)
                    {
                        dRep[v, m].Add(Median(on[m]) - Median(off[m]));
                        onAll[v][m].AddRange(on[m]); offAll[v][m].AddRange(off[m]);
                    }
                }
                Say($"PEOPLE rep {r + 1}/{reps} done");
            }
            PerfClock.FrameDone = null;
            ppDraws.Dispose(); ppBatch.Dispose(); ppSetPass.Dispose(); ppTris.Dispose();
            foreach (var m in ppMarkers) m.rec.Dispose();

            // ---- the table
            float Mean(List<float> l) { float a = 0f; foreach (var x in l) a += x; return l.Count > 0 ? a / l.Count : 0f; }
            float Sd(List<float> l) { if (l.Count < 2) return 0f; float mu = Mean(l), a = 0f; foreach (var x in l) a += (x - mu) * (x - mu); return Mathf.Sqrt(a / (l.Count - 1)); }
            string Thirds(List<float> l)
            {
                // the three "runs": the reps in three groups (1-2, 3-4, 5-6 at six reps), each group's mean
                var sb2 = new StringBuilder();
                for (int g = 0; g < 3; g++)
                {
                    int a = l.Count * g / 3, b = l.Count * (g + 1) / 3; float sum = 0f;
                    for (int i = a; i < b; i++) sum += l[i];
                    sb2.Append(g > 0 ? "/" : "").Append(b > a ? (sum / (b - a)).ToString("0.00", ci) : "-");
                }
                return sb2.ToString();
            }
            int hIdx = RaceHandoff.TimeOfDayIndex;
            var table = new StringBuilder();
            table.AppendLine($"PEOPLE COST TEST (EDITOR) tag {tag}, stand {standArg}: the town's meet lot, {cars} meet cars parked, the player's car in the aisle at {stand.x:0},{stand.z:0} facing east, chase view");
            table.AppendLine($"profile {(PlatformProfile.PC ? "PC" : "non-PC (PSX_PROFILE=" + System.Environment.GetEnvironmentVariable("PSX_PROFILE") + ")")}; hour {(hIdx >= 0 && hIdx < TimeOfDay.All.Length ? TimeOfDay.All[hIdx].name : "?")} weather {Seasons.CurrentWeather}; view {ow}x{oh} offscreen, waited for the GPU; town up in {loadS:0.0} s");
            table.AppendLine($"gpu {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceVersion}; cpu {SystemInfo.processorType} x{SystemInfo.processorCount}");
            table.AppendLine($"mannequin: {crowd.TrisPerFigure} triangles, {crowd.VertsPerFigure} vertices, {Mannequin.BoneCount} bones a figure, {(crowd.PC ? 4 : 2)} weights a vertex; half idle at the cars, half walk the aisles");
            table.AppendLine("memory: " + crowd.MemoryReport());
            table.AppendLine($"memory counters before the crowd: {memBefore}; after (all three techniques built, {most} each): {memAfter}");
            table.AppendLine($"each variant: {reps} reps x {blockS:0.#} s, each beside a {blockS:0.#} s block with no figures; delta = mean over reps of (median with - median without); runs = the reps in three groups");
            table.AppendLine("markers: " + (found.Length > 0 ? found.ToString() : "none found"));
            table.AppendLine();
            table.AppendLine("variant   | frame med  p99  | d frame (sd)  runs            | d view call | d gpu wait | d main(FTM) d RT(FTM) d GPU(FTM) | draws  d     | setpass d | tris M   | d skin mk | d anim mk");
            var csv = new StringBuilder("variant,tech,count,metric,with_median,without_median,delta_mean,delta_sd,with_p99,reps\n");
            for (int v = 0; v < vars.Count; v++)
            {
                var on = onAll[v]; var off = offAll[v];
                table.AppendLine(string.Format(ci,
                    "{0,-9} | {1,6:0.00} {2,6:0.00} | {3,6:0.00} ({4:0.00}) {5,-15} | {6,11:0.00} | {7,10:0.00} | {8,11:0.00} {9,9:0.00} {10,10:0.00} | {11,5:0} {12,5:+0;-0;0} | {13,5:0} {14,3:+0;-0;0} | {15,5:0.00}>{16:0.00} | {17,9:0.000} | {18,9:0.000}",
                    vars[v].name, Median(on[PP_FRAME]), Pct(on[PP_FRAME], 0.99f), Mean(dRep[v, PP_FRAME]), Sd(dRep[v, PP_FRAME]), Thirds(dRep[v, PP_FRAME]),
                    Mean(dRep[v, PP_CALL]), Mean(dRep[v, PP_WAIT]), Mean(dRep[v, PP_MAIN]), Mean(dRep[v, PP_RT]), Mean(dRep[v, PP_GPU]),
                    Median(on[PP_DRAWS]), Mean(dRep[v, PP_DRAWS]), Median(on[PP_SETPASS]), Mean(dRep[v, PP_SETPASS]),
                    Median(off[PP_TRIS]), Median(on[PP_TRIS]), Mean(dRep[v, PP_SKIN]), Mean(dRep[v, PP_ANIM])));
                for (int m = 0; m < PP_N; m++)
                    csv.Append(vars[v].name).Append(',').Append(vars[v].t).Append(',').Append(vars[v].n).Append(',').Append(PpNames[m]).Append(',')
                       .Append(Median(on[m]).ToString("0.000", ci)).Append(',').Append(Median(off[m]).ToString("0.000", ci)).Append(',')
                       .Append(Mean(dRep[v, m]).ToString("0.000", ci)).Append(',').Append(Sd(dRep[v, m]).ToString("0.000", ci)).Append(',')
                       .Append(Pct(on[m], 0.99f).ToString("0.000", ci)).Append(',').Append(reps).Append('\n');
            }
            table.AppendLine();
            table.AppendLine("FTM = Unity's FrameTimingManager (0.00 everywhere = not reported by this editor). d = with minus without; the off(A/A) row's d is the noise.");
            Post("town_people.txt", table.ToString());
            Post("town_people.csv", csv.ToString());
            foreach (var kv in snaps) PSXPerfTour_Post(tag + "_town_people_" + kv.Key + ".jpg", kv.Value, kv.Value.Length);
            Say("PEOPLE table written");

            // ---- the night frame: a figure under a lamp beside a meet car (peopleshot=1)
            if (peopleShotArg == "1")
            {
                crowd.Show(PeopleCrowd.Tech.B, 0);
                // the lot's lamps stand on the spine between rows B and C (PSXRacingBuilder.Town BuildTownMeetLot)
                float bestX = 105.5f; int bestCars = -1;
                foreach (float lx in new[] { 96.5f, 105.5f, 114.5f, 123.5f })
                {
                    int n = 0;
                    foreach (var sp in tw.meetSpots)
                        if (sp != null && Mathf.Abs(sp.position.z - 28f) < 1f && Mathf.Abs(sp.position.x - lx) < 2f && sp.GetComponentInChildren<MeshRenderer>() != null) n++;
                    if (n > bestCars) { bestCars = n; bestX = lx; }
                }
                var techShot = techs.Contains(PeopleCrowd.Tech.B) ? PeopleCrowd.Tech.B : techs[0];
                var one = PeopleCrowd.Build(new[]
                {
                    new PeopleCrowd.Spot { pos = new Vector3(bestX, y, 25.4f), rot = Quaternion.Euler(0f, 200f, 0f) },
                    new PeopleCrowd.Spot { walk = true, a = new Vector3(bestX - 4f, y, 22.6f), b = new Vector3(bestX + 3f, y, 22.6f), pos = new Vector3(bestX - 4f, y, 22.6f) },
                }, PlatformProfile.PC);
                one.Show(techShot, 2);
                var chase = FindAnyObjectByType<ChaseCamera>();
                if (chase != null) chase.enabled = false;
                var eye = new Vector3(bestX + 2.6f, y + 1.65f, 20.2f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(bestX, y + 1.2f, 25.6f) - eye));
                for (float w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w < 2.5f;)
                {
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(bestX, y + 1.2f, 25.6f) - eye));
                    yield return null;
                }
                var png = view.Snap(ow, true);
                PSXPerfTour_Post(tag + "_town_people_lamp.png", png, png.Length);
                Say($"PEOPLE lamp frame: lamp at x {bestX:0.0} (z 29.3 / 32.3, 7.1 m up), {bestCars} meet car(s) in the two stalls beside it, technique {techShot}");
                if (chase != null) chase.enabled = true;
                Destroy(one.gameObject);
            }
            Destroy(holder);
            yield return null;
            full.Release(); screenRt.Release();
            Destroy(crowd.gameObject);
        }
    }
}
