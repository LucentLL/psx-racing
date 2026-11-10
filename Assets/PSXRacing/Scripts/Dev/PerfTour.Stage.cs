using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// THE PERF TOUR ON A MOUNTAIN STAGE (2026-11-09; the owner's targets for
    /// the PC build: "60FPS minimum, 144FPS target", draw distance and pop-in
    /// "equivalent for FH6"). The city tour measured Charlotte only; the
    /// stages - where the mesh forest and now the horizon are - had never
    /// been measured in a player at all.
    ///
    ///   -perftour "stage=SwissNC226A[,BlueRidge][&amp;from=7832][&amp;secs=90][&amp;hour=Dusk]
    ///              [&amp;weather=Clear][&amp;day=50][&amp;view=chase][&amp;cap=0][&amp;snap=10][&amp;tag=name]
    ///              [&amp;offscreen=3832x2054]"
    ///
    /// The stage loads the way ARCADE's time trial loads it (a loaner, alone
    /// on the road), the race AI drives the player's car from <c>from</c>
    /// metres down the road, and every frame is recorded as the city tour
    /// records them, plus what the GPU was asked to draw (batches, set-pass
    /// calls, triangles). The frame cap is lifted for the run (cap=0; cap=60
    /// to measure under the owner's own cap): a capped run can only say
    /// "60", not how much room there is over it.
    ///
    /// <c>offscreen=WxH</c> (2026-11-09, for a run with NO WINDOW: -batchmode
    /// draws no main view at all): the main camera is drawn into a texture of
    /// that size every frame from a late LateUpdate, and the frame then WAITS
    /// for the GPU to finish it (a one-pixel read-back). The frame's time is
    /// then the main thread plus the GPU, one after the other - slower than a
    /// windowed run, where the two overlap, but it puts a number on the GPU's
    /// share of a view at the owner's resolution without opening anything on
    /// his screen, and an A/B in one player compares like with like.
    /// </summary>
    public partial class PerfTour
    {
        string stageArg, fromArg, dayArg, capArg, offscreenArg;

        /// <summary>The main view drawn into a texture and waited for (offscreen=WxH).</summary>
        [DefaultExecutionOrder(20000)]
        class OffscreenView : MonoBehaviour
        {
            public Camera cam;
            public RenderTexture rt;
            RenderTexture was;
            void OnEnable() { if (cam != null) { was = cam.targetTexture; cam.targetTexture = rt; } }
            void OnDisable() { if (cam != null) cam.targetTexture = was; }
            void LateUpdate()
            {
                if (cam == null || rt == null) return;
                if (cam.targetTexture != rt) cam.targetTexture = rt;
                long t0 = PerfClock.Now();
                cam.Render();
                PerfClock.Add(PerfClock.Slot.OffscreenCpu, t0);
                t0 = PerfClock.Now();
                var req = UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32, null);
                req.WaitForCompletion();
                PerfClock.Add(PerfClock.Slot.OffscreenGpu, t0);
            }

            /// <summary>The texture as last drawn, at half size, as a PNG (snap=N in a run with no window).</summary>
            public byte[] Snap()
            {
                int w = Mathf.Max(16, rt.width / 2), h = Mathf.Max(16, rt.height / 2);
                var small = RenderTexture.GetTemporary(w, h, 0, rt.format);
                Graphics.Blit(rt, small);
                var prev = RenderTexture.active;
                RenderTexture.active = small;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(small);
                var png = tex.EncodeToPNG();
                Destroy(tex);
                return png;
            }
        }

        IEnumerator StageTour(int hour, int weather)
        {
            if (hour < 0) hour = TimeOfDay.Noon;
            if (weather < 0) weather = (int)Weather.Clear;
            int.TryParse(dayArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int day);
            float.TryParse(fromArg, NumberStyles.Float, CultureInfo.InvariantCulture, out float fromM);
            int cap = 0;
            if (!string.IsNullOrEmpty(capArg)) int.TryParse(capArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out cap);
            float t0 = Time.realtimeSinceStartup;
            while (!CarCatalog.Ready && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            bool envDone = false;
            foreach (var raw in stageArg.Split(','))
            {
                string id = raw.Trim();
                if (id.Length == 0) continue;
                if (!TrackCatalog.TryIndexOfShipped(id, out int venue)) { Say("SKIP " + id + ": no such course in this build"); Post(id + "_summary.txt", "SKIP no such course"); continue; }
                int scene = TrackCatalog.SceneIndex(venue);
                var spec = CarCatalog.Get(CityFrontEnd.DefaultCarId());
                if (spec == null || !TrackCatalog.SceneShipped(scene)) { Say("SKIP " + id + ": scene or car missing"); continue; }

                // ARCADE's time trial, as its front end fills it (ArcadeFrontEnd.Launch)
                ArcadeMode.Simulate(true);
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.CarId = "";
                RaceHandoff.CarSpecId = spec.id;
                RaceHandoff.StartFuelPct = 100f;
                RaceHandoff.TimeOfDayIndex = hour;
                RaceHandoff.WeatherOverride = weather;
                RaceHandoff.CalendarDay = day;
                RaceHandoff.TrackIndex = venue;
                RaceHandoff.TrafficLevel = TrafficLevels.None;
                RaceHandoff.Solo = true;
                float l0 = Time.realtimeSinceStartup;
                SceneGate.Load(scene);
                car = null;
                TrackPath path = null;
                while (Time.realtimeSinceStartup - l0 < 180f)
                {
                    yield return null;
                    if (SceneManager.GetActiveScene().buildIndex != scene) continue;
                    var rm = RaceManager.Instance;
                    var pin = FindFirstObjectByType<PlayerCarInput>();
                    path = FindFirstObjectByType<TrackPath>();
                    if (rm == null || pin == null || path == null || rm.State != RaceManager.RaceState.Racing) continue;
                    car = pin.GetComponent<CarController>();
                    if (car != null) break;
                }
                if (car == null || path == null) { Say("FAIL " + id + ": the stage never came up"); Post(id + "_summary.txt", "FAIL the stage never came up"); continue; }
                float loadS = Time.realtimeSinceStartup - l0;
                if (!string.IsNullOrEmpty(viewArg) && System.Enum.TryParse(viewArg, true, out ChaseCamera.View v)) ChaseCamera.PreviewView(v);

                if (!envDone)
                {
                    envDone = true;
                    var cam = Camera.main;
                    var env = new StringBuilder();
                    env.AppendLine("tag " + tag);
                    env.AppendLine("os " + SystemInfo.operatingSystem);
                    env.AppendLine("gpu " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceVersion);
                    env.AppendLine("screen " + Screen.width + "x" + Screen.height + " dpi " + Screen.dpi + " refresh " + Screen.currentResolution.refreshRateRatio.value.ToString("0.0", CultureInfo.InvariantCulture));
                    env.AppendLine("hour " + TimeOfDay.All[hour].name + " weather " + Seasons.CurrentWeather + " day " + day + " dress " + Seasons.DressNames[Mathf.Clamp(SeasonDress.AppliedDress, 0, Seasons.DressCount - 1)] +
                                   " view " + ChaseCamera.Current + " picture " + PSXQuality.Current + " cap " + (cap <= 0 ? "none" : cap.ToString()));
                    env.AppendLine("camera far " + (cam != null ? cam.farClipPlane.ToString("0") : "?") + " m, detail to " + TimeOfDay.FarEdgeM.ToString("0") + " m, horizon " + TimeOfDay.HorizonM.ToString("0") +
                                   " m, target " + (cam != null && cam.targetTexture != null ? cam.targetTexture.width + "x" + cam.targetTexture.height + " depth " + cam.targetTexture.depthStencilFormat : "screen"));
                    env.AppendLine(PlatformProfile.Describe());
                    Post("env.txt", env.ToString());
                    Say("stage up: " + env.ToString().Replace("\n", " | "));
                }

                // offscreen=WxH: the main view into a texture of that size, waited for (see the header)
                OffscreenView off = null;
                if (!string.IsNullOrEmpty(offscreenArg) && Camera.main != null)
                {
                    var wh = offscreenArg.ToLowerInvariant().Split('x');
                    if (wh.Length == 2 && int.TryParse(wh[0], out int ow) && int.TryParse(wh[1], out int oh) && ow >= 16 && oh >= 16)
                    {
                        var ort = new RenderTexture(ow, oh, PlatformProfile.PC ? 32 : 24, RenderTextureFormat.Default) { antiAliasing = 1, name = "PerfTourOffscreen" };
                        ort.Create();
                        var holder = new GameObject("PerfTourOffscreen");
                        holder.SetActive(false);
                        off = holder.AddComponent<OffscreenView>();
                        off.cam = Camera.main; off.rt = ort;
                        holder.SetActive(true);
                        Say("offscreen view " + ow + "x" + oh + " (depth " + ort.depthStencilFormat + "), drawn and waited for every frame");
                    }
                }

                // the race AI at the wheel, from the asked metre
                var input = car.GetComponent<PlayerCarInput>();
                if (input != null) { input.inputEnabled = false; input.enabled = false; }
                int i0 = Mathf.Clamp(Mathf.RoundToInt(fromM / Mathf.Max(0.5f, path.spacing)), 2, path.Count - 40);
                if (fromM > 1f)
                {
                    car.ResetTo(path.GetPoint(i0) + Vector3.up * 0.45f, Quaternion.LookRotation(Flat(path.GetTangent(i0)), Vector3.up), seated: true);
                    yield return new WaitForSecondsRealtime(2f);
                }
                car.allowReverse = false;
                var old = car.GetComponent<AIDriver>();
                if (old != null) Destroy(old);
                yield return null;
                var ai = car.gameObject.AddComponent<AIDriver>();
                ai.path = path;
                ai.skill = 0.9f;
                ai.ReseedPath();

                Alloc();
                MeshForest.StatsReset();
                MeshForest.StatsOn = true;
                n = 0; spikes.Length = 0; spikeLines = 0;
                gcWas = System.GC.CollectionCount(0);
                int gc0 = gcWas;
                allocWas = 0;
                var batches = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
                var setPass = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
                var tris = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
                float wait0 = Time.realtimeSinceStartup;
                while (Mathf.Abs(car.forwardSpeed) < 3f && Time.realtimeSinceStartup - wait0 < 15f) yield return null;
                recT0 = Time.realtimeSinceStartup;
                PerfClock.FrameDone = OnFrame;
                recording = true;
                Say("STAGE " + id + " from " + (i0 * path.spacing).ToString("0") + " m, up in " + loadS.ToString("0.0") + " s");

                float dist = 0f; var last = car.transform.position;
                float slowFor = 0f; int nudges = 0; string end = "time";
                float nextSnap = snapEvery > 0f ? 2f : float.MaxValue; int snapN = 0;
                double bSum = 0, sSum = 0, tSum = 0; long bMax = 0, tMax = 0; int rn = 0;
                while (Time.realtimeSinceStartup - recT0 < maxSecs)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = cap <= 0 ? -1 : cap;
                    if (Time.realtimeSinceStartup - recT0 >= nextSnap)
                    {
                        nextSnap += snapEvery;
                        byte[] png;
                        if (off != null) png = off.Snap();
                        else
                        {
                            yield return new WaitForEndOfFrame();
                            var tex = ScreenCapture.CaptureScreenshotAsTexture();
                            png = tex.EncodeToPNG();
                            Destroy(tex);
                        }
                        PSXPerfTour_Post(tag + "_" + id + "_snap" + (++snapN).ToString("00") + "_" + (ai.PathIndex * path.spacing).ToString("0") + "m.png", png, png.Length);
                        evNow = "SNAP";
                    }
                    yield return null;
                    long b = batches.LastValue, tr = tris.LastValue;
                    bSum += b; sSum += setPass.LastValue; tSum += tr; rn++;
                    if (b > bMax) bMax = b;
                    if (tr > tMax) tMax = tr;
                    var p = car.transform.position;
                    float step = Vector3.Distance(p, last);
                    if (step < 60f) dist += step;
                    last = p;
                    if (Mathf.Abs(car.forwardSpeed) < 2f) slowFor += Time.unscaledDeltaTime; else slowFor = 0f;
                    if (slowFor > 6f && nudges < 6)
                    {
                        int to = Mathf.Min(path.Count - 13, ai.PathIndex + Mathf.CeilToInt(50f / Mathf.Max(0.5f, path.spacing)));
                        car.ResetTo(path.GetPoint(to) + Vector3.up * 0.45f, Quaternion.LookRotation(Flat(path.GetTangent(to)), Vector3.up), seated: true);
                        ai.ReseedPath();
                        nudges++; slowFor = 0f; last = car.transform.position; evNow = "NUDGE";
                        continue;
                    }
                    if (slowFor > 10f) { end = "stuck at " + p.x.ToString("0") + "," + p.z.ToString("0"); break; }
                    if (ai.PathIndex >= path.Count - 14) { end = "road end"; break; }
                    if (n >= Cap) { end = "frame cap"; break; }
                }
                recording = false;
                PerfClock.FrameDone = null;
                MeshForest.StatsOn = false;
                if (off != null) { var ort = off.rt; Destroy(off.gameObject); yield return null; if (ort != null) { ort.Release(); Destroy(ort); } }
                float secs = Time.realtimeSinceStartup - recT0;
                int gcN = System.GC.CollectionCount(0) - gc0;
                batches.Dispose(); setPass.Dispose(); tris.Dispose();
                FrameRatePrefs.Apply();

                var ci = CultureInfo.InvariantCulture;
                var csv = new StringBuilder(n * 120 + 512);
                csv.Append("i,t,ms,out,fix,phys,fixs,upd,late,rend,ui,steps,gc,x,z,v,ev,allocKB");
                for (int s = 0; s < cSlot.Length; s++) csv.Append(',').Append(PerfClock.SlotNames[s].Replace(' ', '_'));
                csv.Append('\n');
                for (int i = 0; i < n; i++)
                {
                    csv.Append(i).Append(',').Append(cT[i].ToString("0.000", ci)).Append(',').Append(cMs[i].ToString("0.00", ci)).Append(',')
                       .Append(cOut[i].ToString("0.00", ci)).Append(',').Append(cFix[i].ToString("0.00", ci)).Append(',').Append(cPhys[i].ToString("0.00", ci)).Append(',')
                       .Append(cFixS[i].ToString("0.00", ci)).Append(',').Append(cUpd[i].ToString("0.00", ci)).Append(',').Append(cLate[i].ToString("0.00", ci)).Append(',')
                       .Append(cRend[i].ToString("0.00", ci)).Append(',').Append(cUi[i].ToString("0.00", ci)).Append(',').Append(cSteps[i]).Append(',').Append(cGc[i]).Append(',')
                       .Append(cX[i].ToString("0.0", ci)).Append(',').Append(cZ[i].ToString("0.0", ci)).Append(',').Append(cV[i].ToString("0.0", ci)).Append(',')
                       .Append(cEv[i] ?? "").Append(',').Append(cAlloc[i].ToString("0.0", ci));
                    for (int s = 0; s < cSlot.Length; s++) csv.Append(',').Append(cSlot[s][i].ToString("0.000", ci));
                    csv.Append('\n');
                }
                var ms = new float[n];
                System.Array.Copy(cMs, ms, n);
                System.Array.Sort(ms);
                float P(float q) => n > 0 ? ms[Mathf.Clamp(Mathf.RoundToInt(q * (n - 1)), 0, n - 1)] : 0f;
                int over16 = 0, over33 = 0, over7 = 0; double sum = 0;
                double mOut = 0, mUpd = 0, mLate = 0, mRend = 0, mFix = 0, mUi = 0;
                for (int i = 0; i < n; i++)
                {
                    sum += ms[i];
                    if (ms[i] > 6.95f) over7++;
                    if (ms[i] > 16.7f) over16++;
                    if (ms[i] > 33.4f) over33++;
                    mOut += cOut[i]; mUpd += cUpd[i]; mLate += cLate[i]; mRend += cRend[i]; mFix += cFix[i]; mUi += cUi[i];
                }
                double dn = Mathf.Max(1, n);
                var sum1 = new StringBuilder();
                sum1.AppendLine($"stage {id} from {i0 * path.spacing:0} m; ended: {end}; {secs:0.0} s, {dist:0} m driven, avg {(secs > 0 ? dist / secs * 3.6f : 0):0} km/h" + (nudges > 0 ? $"; set on 50 m {nudges}x" : ""));
                sum1.AppendLine($"frames {n}  avg fps {(sum > 0 ? n * 1000.0 / sum : 0):0.0}  p50 {P(0.5f):0.0}  p95 {P(0.95f):0.0}  p99 {P(0.99f):0.0}  worst {P(1f):0.0} ms");
                sum1.AppendLine($"1% low fps {(P(0.99f) > 0 ? 1000f / P(0.99f) : 0):0.0}; frames over 6.9 ms (144) {over7 * 100.0 / dn:0.0}%  over 16.7 ms (60) {over16 * 100.0 / dn:0.0}%  over 33 ms {over33}");
                sum1.AppendLine($"mean ms: scripts update {mUpd / dn:0.00} late {mLate / dn:0.00} fixed {mFix / dn:0.00} ui {mUi / dn:0.00}; render submit {mRend / dn:0.00}; outside the loop (present, GPU wait) {mOut / dn:0.00}");
                sum1.AppendLine($"drawn: batches avg {(rn > 0 ? bSum / rn : 0):0} max {bMax}; set-pass avg {(rn > 0 ? sSum / rn : 0):0}; triangles avg {(rn > 0 ? tSum / rn / 1e6 : 0):0.00} M max {tMax / 1e6:0.00} M");
                sum1.AppendLine($"gc collections {gcN}");
                // the mesh forest's own share (PerfClock slots) and what it put on the GPU
                // (its own count: Unity's triangle counter does not see an indirect draw's instances)
                double fCull = 0, fDraw = 0, fOther = 0, fRefl = 0, oCpu = 0, oGpu = 0;
                var oG = new float[n];
                for (int i = 0; i < n; i++)
                {
                    fCull += cSlot[(int)PerfClock.Slot.ForestCull][i]; fDraw += cSlot[(int)PerfClock.Slot.ForestDraw][i];
                    fOther += cSlot[(int)PerfClock.Slot.ForestOther][i]; fRefl += cSlot[(int)PerfClock.Slot.CarReflect][i];
                    oCpu += cSlot[(int)PerfClock.Slot.OffscreenCpu][i]; oGpu += cSlot[(int)PerfClock.Slot.OffscreenGpu][i];
                    oG[i] = cSlot[(int)PerfClock.Slot.OffscreenCpu][i] + cSlot[(int)PerfClock.Slot.OffscreenGpu][i];
                }
                var mfo = FindFirstObjectByType<MeshForest>();
                int sf = Mathf.Max(1, MeshForest.StatFrames);
                sum1.AppendLine($"mesh forest ({(mfo == null ? "none" : !MeshForest.MeshTreesOn ? "cards" : MeshForest.GpuDriven ? "GPU lists" : "CPU lists")}): mean ms cull {fCull / dn:0.000} draw {fDraw / dn:0.000} other cameras {fOther / dn:0.000} (inside car reflections {fRefl / dn:0.000})");
                sum1.AppendLine($"mesh forest on the GPU: triangles avg {MeshForest.SumTris / sf / 1e6:0.00} M max {MeshForest.MaxTris / 1e6:0.00} M, plants avg {MeshForest.SumPlants / sf:0}, draw calls avg {MeshForest.SumDraws / sf:0} ({MeshForest.StatFrames} samples; in lists no drawn chunk reached: {MeshForest.UnreachedTris} triangles)");
                sum1.AppendLine("mesh forest triangles " + MeshForest.StatsBreakdown());
                sum1.AppendLine($"main view between the forest's cull and its draw: turned at most {MeshForest.ViewTurnedDeg:0.0000} deg, moved {MeshForest.ViewMovedM:0.0000} m, lens changed {MeshForest.ViewLensChanged:0.00000} (0 = the cull saw the view as drawn; only measured when the main view is drawn)");
                if (off != null)
                {
                    System.Array.Sort(oG);
                    float OP(float q) => n > 0 ? oG[Mathf.Clamp(Mathf.RoundToInt(q * (n - 1)), 0, n - 1)] : 0f;
                    sum1.AppendLine($"offscreen view {offscreenArg}: mean ms render call {oCpu / dn:0.00} + GPU wait {oGpu / dn:0.00}; together p50 {OP(0.5f):0.00} p95 {OP(0.95f):0.00} p99 {OP(0.99f):0.00}");
                }
                Post(id + "_frames.csv", csv.ToString());
                Post(id + "_spikes.txt", spikes.ToString());
                Post(id + "_summary.txt", sum1.ToString());
                Say("STAGE " + sum1.ToString().Replace("\n", " | "));
                Destroy(ai);
                yield return new WaitForSecondsRealtime(1f);
            }
        }
    }
}
