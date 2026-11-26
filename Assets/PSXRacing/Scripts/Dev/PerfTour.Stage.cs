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
        float popStep, popLen = 600f, popLag = 6f;
        System.Collections.Generic.HashSet<int> popKeep;   // &popkeep=j,j: pairs kept whatever they show (an after run at the before run's worst)

        // ------------------------------------------------------------------
        // THE POP MEASURE (&popwalk=STEP&popm=M; 2026-11-11, owner: "pop-in is
        // still constantly happening on low-high forest settings"). A chase
        // view is stepped down the road STEP metres a frame - the same views
        // every run, nothing timed - and each frame's picture is compared with
        // the next. Motion changes a picture a little every frame; a POP
        // changes one spot in one frame and not in the frames either side.
        // Per frame the change is kept as 8x8-pixel blocks of a 480x270
        // luminance picture (<tag>_<id>_popblocks.bin: int32 frames, w, h,
        // then frames x h x w float32, mean |dL| 0..1 of the pair k, k+1);
        // the pairs whose change stands out from both neighbours are kept
        // as JPGs at 960x540 for the sheet. Analysis: scratchpad popscore.py.
        // ------------------------------------------------------------------
        [DefaultExecutionOrder(1400)]   // after the chase (0) and cockpit (1000) cameras, before MeshForest's cull (1500)
        class PopRig : MonoBehaviour
        {
            public Camera cam; public Vector3 pos; public Quaternion rot; public bool on;
            void LateUpdate() { if (on && cam != null) cam.transform.SetPositionAndRotation(pos, rot); }
        }

        static Vector3 PathAt(TrackPath p, float m)
        {
            float f = m / Mathf.Max(0.5f, p.spacing);
            int i = Mathf.FloorToInt(f); float t = f - i;
            Vector3 a = p.GetPoint(i - 1), b = p.GetPoint(i), c = p.GetPoint(i + 1), d = p.GetPoint(i + 2);
            return 0.5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * (t * t) + (-a + 3f * b - 3f * c + d) * (t * t * t));
        }

        IEnumerator PopWalk(string id, TrackPath path, float m0, OffscreenView off)
        {
            const int QW = 480, QH = 270, HW = 960, HH = 540, BS = 8;
            int bw = QW / BS, bh = QH / BS;
            var cam = off.cam;
            var rb = car.GetComponent<Rigidbody>();
            bool wasKin = rb != null && rb.isKinematic;
            var wasInterp = rb != null ? rb.interpolation : RigidbodyInterpolation.None;
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            var hidden = new System.Collections.Generic.List<Renderer>();
            foreach (var r in car.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); }
            var rigGo = new GameObject("PopRig");
            var rig = rigGo.AddComponent<PopRig>();
            rig.cam = cam;
            float endM = Mathf.Min(m0 + popLen, path.TotalLength - 60f);
            int frames = Mathf.Max(4, Mathf.FloorToInt((endM - m0) / popStep));
            void Pose(float m)
            {
                Vector3 eye = PathAt(path, m - 6.5f) + Vector3.up * 2.3f;
                Vector3 at = PathAt(path, m + 14f) + Vector3.up * 1.0f;
                rig.pos = eye; rig.rot = Quaternion.LookRotation(at - eye, Vector3.up); rig.on = true;
                // the car (hidden, but some of it is drawn by its own scripts) 30 m behind the eye: out of the view
                Vector3 cp = PathAt(path, m - 36f), cf = PathAt(path, m - 34f) - cp; cf.y = 0f;
                if (cf.sqrMagnitude > 1e-4f)
                {
                    var cr = Quaternion.LookRotation(cf, Vector3.up);
                    car.transform.SetPositionAndRotation(cp + Vector3.up * 0.4f, cr);
                    if (rb != null) { rb.position = cp + Vector3.up * 0.4f; rb.rotation = cr; }
                }
            }
            // settle: the dress, the far lists, the shadows' first maps
            Pose(m0);
            for (int s = 0; s < 40; s++) yield return null;

            var half = RenderTexture.GetTemporary(HW, HH, 0, off.rt.format);
            var quart = RenderTexture.GetTemporary(QW, QH, 0, off.rt.format);
            var texH = new Texture2D(HW, HH, TextureFormat.RGB24, false);
            var texQ = new Texture2D(QW, QH, TextureFormat.RGB24, false);
            var img = new Color32[3][]; var lum = new float[3][];
            var blk = new float[frames][];
            int saved = 0;
            var csv = new StringBuilder("k,m,mean,top,popblocks,popmass\n");
            float[] Bk(int k) => k >= 0 && k < frames ? blk[k] : null;
            // THE LAG PAIR (&poplag=M, default 6 m = a quarter second at 85 km/h): the
            // same view drawn a second time with the forest's levels and rings worked
            // out from where the eye stood M metres back. The two differ only by
            // what changes level or ring over those metres - motion taken out - so
            // a level hand-over that reads as a pop at speed (a short band, an
            // impostor unlike its tree) shows however wide its dither is.
            // The clock is stopped for the walk (wind, clouds), so a picture drawn
            // twice is the same picture: every 10th step measures that (noise).
            var lagBlk = new float[frames + 1][];
            var noise = new System.Collections.Generic.List<float>();
            Color32[] lagImg = null; var lagLum = new float[QW * QH];
            int lagSaved = 0;
            var lagCsv = new StringBuilder("k,m,lagmean,lagtop\n");
            float wasScale = Time.timeScale;
            Time.timeScale = 0f;
            void Grab(float[] into, out Color32[] full)
            {
                Graphics.Blit(off.rt, half);
                Graphics.Blit(half, quart);
                var prev = RenderTexture.active;
                RenderTexture.active = half; texH.ReadPixels(new Rect(0, 0, HW, HH), 0, 0, false);
                RenderTexture.active = quart; texQ.ReadPixels(new Rect(0, 0, QW, QH), 0, 0, false);
                RenderTexture.active = prev;
                full = texH.GetPixels32();
                var qp = texQ.GetPixels32();
                for (int p = 0; p < into.Length; p++) into[p] = (0.2126f * qp[p].r + 0.7152f * qp[p].g + 0.0722f * qp[p].b) / 255f;
            }
            float[] Blocks(float[] l1, float[] l0)
            {
                var B = new float[bw * bh];
                for (int y = 0; y < bh * BS; y++)
                    for (int x = 0; x < bw * BS; x++)
                    {
                        int p = y * QW + x;
                        B[(y / BS) * bw + x / BS] += Mathf.Abs(l1[p] - l0[p]);
                    }
                for (int b = 0; b < B.Length; b++) B[b] /= BS * BS;
                return B;
            }
            void SaveJpg(Color32[] px, string name)
            {
                var tex = new Texture2D(HW, HH, TextureFormat.RGB24, false);
                tex.SetPixels32(px); tex.Apply(false);
                var jpg = tex.EncodeToJPG(88);
                Destroy(tex);
                PSXPerfTour_Post(tag + "_" + id + "_" + name + ".jpg", jpg, jpg.Length);
            }
            float t0 = Time.realtimeSinceStartup;
            for (int k = 0; k <= frames; k++)
            {
                float mk = m0 + k * popStep;
                Pose(mk);
                MeshForest.LodEyeOn = false;
                yield return null;      // drawn in that frame's LateUpdate (OffscreenView), read here in the next
                int slot = k % 3;
                var L = lum[slot] ?? (lum[slot] = new float[QW * QH]);
                Grab(L, out img[slot]);
                if (popLag > 0f)
                {
                    bool noiseStep = k % 10 == 5;
                    MeshForest.LodEye = noiseStep ? rig.pos : PathAt(path, mk - popLag - 6.5f) + Vector3.up * 2.3f;
                    MeshForest.LodEyeOn = true;
                    yield return null;
                    MeshForest.LodEyeOn = false;
                    Grab(lagLum, out lagImg);
                    var Lb = Blocks(lagLum, L);
                    float lm = 0f, lt = 0f;
                    for (int b = 0; b < Lb.Length; b++) { lm += Lb[b]; if (Lb[b] > lt) lt = Lb[b]; }
                    lm /= Lb.Length;
                    if (noiseStep) noise.Add(lm);
                    else
                    {
                        lagBlk[k] = Lb;
                        lagCsv.Append(k).Append(',').Append(mk.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                              .Append(lm.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',').Append(lt.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
                        bool askedL = popKeep != null && popKeep.Contains(10000 + k);
                        if ((k % 40 == 0 && lagSaved < 40) || askedL)
                        {
                            if (!askedL) lagSaved++;
                            SaveJpg(img[slot], "lag" + k.ToString("0000") + "a");
                            SaveJpg(lagImg, "lag" + k.ToString("0000") + "b");
                        }
                    }
                }
                if (k == 0) continue;
                // the change from k-1 to k, by block
                blk[k - 1] = Blocks(L, lum[(k - 1) % 3]);
                // pair j = k-2 (k-2 -> k-1) now has both neighbours
                int j = k - 2;
                if (j < 1) continue;
                var bj = Bk(j); var bp = Bk(j - 1); var bn = Bk(j + 1);
                int pops = 0; float mass = 0f, mean = 0f, top = 0f;
                for (int b = 0; b < bj.Length; b++)
                {
                    mean += bj[b]; if (bj[b] > top) top = bj[b];
                    float nb = Mathf.Max(bp[b], bn[b]);
                    if (bj[b] > 0.02f && bj[b] > 2.5f * nb + 0.006f) { pops++; mass += bj[b] - nb; }
                }
                mean /= bj.Length;
                csv.Append(j).Append(',').Append((m0 + j * popStep).ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                   .Append(mean.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',').Append(top.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                   .Append(pops).Append(',').Append(mass.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
                bool asked = popKeep != null && popKeep.Contains(j);
                if ((pops > 0 && mass > 0.12f && saved < 120) || asked)
                {
                    if (!asked) saved++;
                    SaveJpg(img[j % 3], "pop" + j.ToString("0000") + "a");
                    SaveJpg(img[(j + 1) % 3], "pop" + j.ToString("0000") + "b");
                }
            }
            Time.timeScale = wasScale;
            MeshForest.LodEyeOn = false;
            float secs = Time.realtimeSinceStartup - t0;
            if (popLag > 0f)
            {
                var lbin = new byte[12 + (long)(frames + 1) * bw * bh * 4];
                System.Buffer.BlockCopy(new[] { frames + 1, bw, bh }, 0, lbin, 0, 12);
                for (int k = 0; k <= frames; k++)
                    if (lagBlk[k] != null) System.Buffer.BlockCopy(lagBlk[k], 0, lbin, 12 + k * bw * bh * 4, bw * bh * 4);
                    else for (int b = 0; b < bw * bh; b++) System.Buffer.BlockCopy(new[] { -1f }, 0, lbin, 12 + (k * bw * bh + b) * 4, 4);
                PSXPerfTour_Post(tag + "_" + id + "_lagblocks.bin", lbin, lbin.Length);
                float nmax = 0f, nsum = 0f;
                foreach (var x in noise) { nsum += x; nmax = Mathf.Max(nmax, x); }
                Post(id + "_lag.csv", lagCsv.ToString() + $"# noise (one place drawn twice): {noise.Count} steps, mean {(noise.Count > 0 ? nsum / noise.Count : 0f):0.00000} max {nmax:0.00000}\n");
            }
            RenderTexture.ReleaseTemporary(half); RenderTexture.ReleaseTemporary(quart);
            Destroy(texH); Destroy(texQ);
            // the block dump
            int nf = frames;
            var bin = new byte[12 + (long)nf * bw * bh * 4];
            System.Buffer.BlockCopy(new[] { nf, bw, bh }, 0, bin, 0, 12);
            for (int k = 0; k < nf; k++)
                if (blk[k] != null) System.Buffer.BlockCopy(blk[k], 0, bin, 12 + k * bw * bh * 4, bw * bh * 4);
            PSXPerfTour_Post(tag + "_" + id + "_popblocks.bin", bin, bin.Length);
            Post(id + "_pop.csv", csv.ToString());
            Say($"POPWALK {id} {m0:0}-{endM:0} m step {popStep} m: {frames} frames in {secs:0} s, {saved} pairs kept, forest {ForestDetailPrefs.Label}, fov {cam.fieldOfView:0.0}");
            Destroy(rigGo);
            foreach (var r in hidden) if (r != null) r.enabled = true;
            if (rb != null) { rb.isKinematic = wasKin; rb.interpolation = wasInterp; }
            yield return null;
        }

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

        /// <summary>(P0 11-23: "Charlotte levels aren't loading, falling through ground") After
        /// <paramref name="after"/> s of racing: every car within 2 m of its road's height and with ground
        /// under it. Writes &lt;id&gt;_onroad.txt (a line per car, then ONROAD OK / ONROAD FAIL).</summary>
        float gridLowest = float.MaxValue; string gridLowestWho = "-";
        /// <summary>(night5) The field run's on-road file is written (the stage loop waits for it before it moves
        /// on or the app quits: on a LOOP stage the grid stands behind the line and the drive ended at once).</summary>
        bool onRoadDone;

        IEnumerator OnRoadAfter(string id, RaceManager rm, TrackPath path, float after)
        {
            onRoadDone = false;
            float t0 = Time.time;
            while (Time.time - t0 < after) yield return null;
            if (rm == null || path == null)
            {
                Post(id + "_onroad.txt", "ONROAD FAIL the race or its path was gone " + after.ToString("0", CultureInfo.InvariantCulture) + " s after the green");
                onRoadDone = true;
                yield break;
            }
            var sb = new StringBuilder();
            int bad = 0, n = 0;
            foreach (var c in rm.allCars)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                n++;
                var p = c.transform.position;
                float py = path.GetPoint(path.NearestIndex(p, -1, path.Count)).y;
                bool ground = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var g, 8f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                bool on = p.y - py > -2f && ground;
                if (!on) bad++;
                sb.AppendLine(c.name + " " + (p.y - py).ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " m against the path, ground " + (ground ? g.collider.name : "NONE") + (on ? " ok" : " OFF THE ROAD"));
            }
            sb.AppendLine("grid: the lowest car from the spawn to the green " + gridLowestWho + " " + gridLowest.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " m against the path");
            if (gridLowest < -2f) bad++;
            sb.AppendLine((bad == 0 && n > 0 ? "ONROAD OK " : "ONROAD FAIL ") + bad + " of " + n + " cars off their road (spawn, countdown, " + after.ToString("0", CultureInfo.InvariantCulture) + " s after the green)");
            gridLowest = float.MaxValue; gridLowestWho = "-";
            Post(id + "_onroad.txt", sb.ToString());
            Say(id + " " + sb.ToString().Replace('\n', '|'));
            onRoadDone = true;
        }

        /// <summary>(night5) A field run waits here for its on-road file (game time, 30 s at most).</summary>
        IEnumerator WaitOnRoad(string id, bool fieldRun)
        {
            if (!fieldRun) yield break;
            float t0 = Time.realtimeSinceStartup;
            while (!onRoadDone && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            if (!onRoadDone) { Post(id + "_onroad.txt", "ONROAD FAIL no check within 30 s of the drive's end"); Say(id + " ONROAD FAIL (timeout)"); }
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
                // &field=1 (P0 11-23, a Charlotte race start with no ground): the arcade's field of rivals
                // and &traffic=0..4 its traffic, as an event race lines up; else the time trial, alone
                bool fieldRun = ShotLink.Query(Request() ?? "", "field") == "1";
                RaceHandoff.Solo = !fieldRun;
                if (fieldRun)
                {
                    int rivalsN = ArcadeMode.RivalsAt(TrackCatalog.At(venue));
                    if (int.TryParse(ShotLink.Query(Request() ?? "", "rivals") ?? "", out int rq) && rq > 0) rivalsN = Mathf.Min(rq, RaceField.MaxFor(TrackCatalog.At(venue)) - 1);
                    ArcadeMode.FillField(spec, rivalsN);
                    if (int.TryParse(ShotLink.Query(Request() ?? "", "traffic") ?? "", out int tl)) RaceHandoff.TrafficLevel = tl;
                }
                float l0 = Time.realtimeSinceStartup;
                SceneGate.Load(scene);
                car = null;
                TrackPath path = null;
                bool seeded = false;
                while (Time.realtimeSinceStartup - l0 < 180f)
                {
                    yield return null;
                    if (SceneManager.GetActiveScene().buildIndex != scene) continue;
                    var rm = RaceManager.Instance;
                    if (rm != null && !seeded) { seeded = true; FieldSeed(); }   // (cityai) &seed=N
                    var pin = FindFirstObjectByType<PlayerCarInput>();
                    path = FindFirstObjectByType<TrackPath>();
                    // (P0) every car on its road from the spawn through the countdown: the lowest seen
                    if (rm != null && path != null && rm.allCars != null)
                        foreach (var gc in rm.allCars)
                        {
                            if (gc == null || !gc.gameObject.activeInHierarchy) continue;
                            float dyG = gc.transform.position.y - path.GetPoint(path.NearestIndex(gc.transform.position, -1, path.Count)).y;
                            if (dyG < gridLowest) { gridLowest = dyG; gridLowestWho = gc.name + (rm.State == RaceManager.RaceState.Countdown ? " (countdown)" : ""); }
                        }
                    if (rm == null || pin == null || path == null || rm.State != RaceManager.RaceState.Racing) continue;
                    car = pin.GetComponent<CarController>();
                    if (car != null) break;
                }
                if (car == null || path == null) { Say("FAIL " + id + ": the stage never came up"); Post(id + "_summary.txt", "FAIL the stage never came up"); continue; }
                float loadS = Time.realtimeSinceStartup - l0;
                if (!string.IsNullOrEmpty(viewArg) && System.Enum.TryParse(viewArg, true, out ChaseCamera.View v)) ChaseCamera.PreviewView(v);
                // (P0) every car of the grid on its road 10 s after the green: <id>_onroad.txt
                StartCoroutine(OnRoadAfter(id, RaceManager.Instance, path, 10f));

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
                    env.AppendLine("save " + saveCost);
                    Post("env.txt", env.ToString());
                    Say("stage up: " + env.ToString().Replace("\n", " | "));
                }

                // NIGHT 4: &leak= on a stage - the rock-face leak probe and the stage's night frames (PerfTour.Night4.cs)
                if (!string.IsNullOrEmpty(ShotLink.Query(Request() ?? "", "leak"))) { yield return StageNight4(id, path); continue; }

                // offscreen=WxH: the main view into a texture of that size, waited for (see the header)
                OffscreenView off = null;
                if (!string.IsNullOrEmpty(offscreenArg) && Camera.main != null && string.IsNullOrEmpty(probeArg))
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

                // &popwalk=STEP: the pop measure first (the drive's timings follow it)
                if (popStep > 0f && off != null)
                    yield return PopWalk(id, path, Mathf.Clamp(Mathf.RoundToInt(fromM / Mathf.Max(0.5f, path.spacing)), 2, path.Count - 40) * path.spacing, off);

                // the race AI at the wheel, from the asked metre
                var input = car.GetComponent<PlayerCarInput>();
                if (input != null) { input.inputEnabled = false; input.enabled = false; }
                int i0 = Mathf.Clamp(Mathf.RoundToInt(fromM / Mathf.Max(0.5f, path.spacing)), 2, path.Count - 40);
                if (fromM > 1f)
                {
                    car.ResetTo(path.GetPoint(i0) + Vector3.up * 0.45f, Quaternion.LookRotation(Flat(path.GetTangent(i0)), Vector3.up), seated: true);
                    yield return new WaitForSecondsRealtime(2f);
                }
                if (LabRequested)
                {
                    // (the LAB check, standing here: PerfTour.Lab.cs)
                    for (float w0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w0 < 4f;) { car.brakeInput = 1f; car.throttleInput = 0f; yield return null; }
                    yield return LabCheck(id);
                    continue;
                }
                if (probeArg == "park")
                {
                    // (the probe, standing here: PerfTour.Probe.cs)
                    for (float w0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - w0 < 4f;) { car.brakeInput = 1f; car.throttleInput = 0f; yield return null; }
                    yield return Probe(id);
                    continue;
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
                if (probeArg == "drive")
                {
                    batches.Dispose(); setPass.Dispose(); tris.Dispose();
                    MeshForest.StatsOn = false;
                    yield return Probe(id);
                    Destroy(ai);
                    continue;
                }
                PerfClock.CamReset();
                recT0 = Time.realtimeSinceStartup;
                PerfClock.FrameDone = OnFrame;
                recording = true;
                FieldBegin(id, path);   // (cityai) &trace=1: every car, 10 Hz
                // (cityai round 2) the plan cost counted from the green: the plans laid while the countdown is held are load
                AIRoutePlanner.BuildMsLog.Clear(); RacingTurns.ResetStats(); TrafficSystem.StepMs.Clear(); CheckpointRace.SliceMs.Clear(); TrafficSystem.FitMs.Clear(); TrafficSystem.FitLooksMax = 0; AIDriver.FileInLooks = 0; AIDriver.FileInBand = 0; AIDriver.FileInMs = 0;
                Say("STAGE " + id + " from " + (i0 * path.spacing).ToString("0") + " m, up in " + loadS.ToString("0.0") + " s");

                float dist = 0f; var last = car.transform.position;
                bool onLap = false;
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
                    FieldSample();
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
                    // (night5) on a LOOP the grid stands behind the line, at the path's END: the road end counts
                    // only once the car has been on the lap itself (it fired on the first frame)
                    if (ai.PathIndex < path.Count - 14) onLap = true;
                    if (onLap && ai.PathIndex >= path.Count - 14) { end = "road end"; break; }
                    // (cityai round 5) in batch mode the run ends on its seconds: an editor that ran unthrottled (1030 fps, h6fix_2)
                    // hit the cap at 70 s of a 150 s tour
                    if (n >= Cap && !Application.isBatchMode) { end = "frame cap"; break; }
                }
                recording = false;
                PerfClock.FrameDone = null;
                MeshForest.StatsOn = false;
                bool wasOff = off != null;
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
                sum1.Append(ExtraSummary(n));
                // (PC71) the rivals' route plans of this run, measured in the exe: what a plan costs, and how many junction
                // turns were laid on an arc (RacingTurns). Read and cleared here so each stage of a tour gets its own.
                {
                    var bl = new System.Collections.Generic.List<float>(AIRoutePlanner.BuildMsLog); bl.Sort();
                    float BQ(float q) => bl.Count > 0 ? bl[Mathf.Clamp(Mathf.RoundToInt(q * (bl.Count - 1)), 0, bl.Count - 1)] : 0f;
                    if (bl.Count > 0)
                        sum1.AppendLine($"rival plans {bl.Count}: build ms p50 {BQ(0.5f):0.00} p99 {BQ(0.99f):0.00} worst {BQ(1f):0.00}; racing turns {(RacingTurns.On ? "on" : "off")}: laid {RacingTurns.Laid} of {RacingTurns.Tried} tried, no fit {RacingTurns.NoFit}" +
                                        (RacingTurns.Laid > 0 ? $", tightest {RacingTurns.MinLaidR:0.0} m" : "") + $"; arcs read {RacingTurns.ArcReads}, searched {RacingTurns.ArcSearches}");
                    // how far the rivals got in this run: the same seed with AI RACING TURNS off and on is the exe's own A/B
                    var rvRace = CheckpointRace.Active;
                    if (rvRace != null)
                    {
                        int rvN = 0, rvGates = 0; float rvMin = float.PositiveInfinity, rvMax = 0f, rvSum = 0f;
                        foreach (var cp in rvRace.All)
                        {
                            if (cp.isPlayer) continue;
                            rvN++; rvGates += cp.passedTotal; rvSum += cp.odometer;
                            rvMin = Mathf.Min(rvMin, cp.odometer); rvMax = Mathf.Max(rvMax, cp.odometer);
                        }
                        if (rvN > 0) sum1.AppendLine($"rivals {rvN}: driven m min {rvMin:0} mean {rvSum / rvN:0} max {rvMax:0}; gates passed {rvGates} in all" + FieldMarks());
                    }
                    {
                        var sl = CheckpointRace.SliceMs; float sw2 = 0f; foreach (var x in sl) sw2 = Mathf.Max(sw2, x);
                        sum1.AppendLine($"racing-turn searches after the green: {RacingTurns.Deferred} turns deferred, {sl.Count} slices, worst slice {sw2:0.00} ms (in-plan searches {RacingTurns.ArcSearches})");
                        sl.Clear();
                    }
                    // (cityai round 3) the race traffic's own update, ms a physics step
                    var tl = new System.Collections.Generic.List<float>(TrafficSystem.StepMs); tl.Sort();
                    if (tl.Count > 0)
                    {
                        double tsum = 0; foreach (var x in tl) tsum += x;
                        sum1.AppendLine($"traffic step ms ({tl.Count} steps): mean {tsum / tl.Count:0.000} p50 {tl[tl.Count / 2]:0.000} p99 {tl[Mathf.Min(tl.Count - 1, (int)(tl.Count * 0.99f))]:0.000} max {tl[tl.Count - 1]:0.000}; " +
                                        $"soft knocks {(TrafficSystem.Instance != null ? TrafficSystem.Instance.SoftKnockLog.Count : 0)}, wrecks {(TrafficSystem.Instance != null ? TrafficSystem.Instance.WreckLog.Count : 0)}");
                    }
                    {
                        // (cityai round 5) the lane fit's own share of the step, and the rivals' file-in
                        var fl = new System.Collections.Generic.List<float>(TrafficSystem.FitMs); fl.Sort();
                        if (fl.Count > 0) { double fs = 0; foreach (var x in fl) fs += x;
                            sum1.AppendLine($"lane fit ms a step ({fl.Count} steps): mean {fs / fl.Count:0.0000} p99 {fl[Mathf.Min(fl.Count - 1, (int)(fl.Count * 0.99f))]:0.0000} max {fl[fl.Count - 1]:0.0000}; most cars looking in one step {TrafficSystem.FitLooksMax}"); }
                        else sum1.AppendLine("lane fit ms a step: off (no pavement map)");
                        long lk = AIDriver.FileInLooks;
                        sum1.AppendLine($"rivals file-in: {lk} rival ticks looked, {AIDriver.FileInBand} inside the band (the allCars loop ran), {AIDriver.FileInMs:0.00} ms in all, {(lk > 0 ? AIDriver.FileInMs * 1000.0 / lk : 0):0.00} us a look");
                        TrafficSystem.FitMs.Clear(); TrafficSystem.FitLooksMax = 0; AIDriver.FileInLooks = 0; AIDriver.FileInBand = 0; AIDriver.FileInMs = 0;
                    }
                    TrafficSystem.StepMs.Clear();
                    AIRoutePlanner.BuildMsLog.Clear(); RacingTurns.ResetStats();
                }
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
                sum1.AppendLine($"mesh forest on the GPU: triangles avg {MeshForest.SumTris / sf / 1e6:0.00} M max {MeshForest.MaxTris / 1e6:0.00} M, plants avg {MeshForest.SumPlants / sf:0}, draw calls avg {MeshForest.SumDraws / sf:0} (with a plant in them {MeshForest.SumLiveDraws / sf:0}; {MeshForest.StatFrames} samples; in lists no drawn chunk reached: {MeshForest.UnreachedTris} triangles)");
                sum1.AppendLine("mesh forest triangles " + MeshForest.StatsBreakdown());
                sum1.AppendLine($"main view between the forest's cull and its draw: turned at most {MeshForest.ViewTurnedDeg:0.0000} deg, moved {MeshForest.ViewMovedM:0.0000} m, lens changed {MeshForest.ViewLensChanged:0.00000} (0 = the cull saw the view as drawn; only measured when the main view is drawn)");
                if (wasOff)
                {
                    System.Array.Sort(oG);
                    float OP(float q) => n > 0 ? oG[Mathf.Clamp(Mathf.RoundToInt(q * (n - 1)), 0, n - 1)] : 0f;
                    sum1.AppendLine($"offscreen view {offscreenArg}: mean ms render call {oCpu / dn:0.00} + GPU wait {oGpu / dn:0.00}; together p50 {OP(0.5f):0.00} p95 {OP(0.95f):0.00} p99 {OP(0.99f):0.00}");
                }
                FieldEnd(id);
                Post(id + "_frames.csv", csv.ToString());
                Post(id + "_spikes.txt", spikes.ToString());
                Post(id + "_summary.txt", sum1.ToString());
                Say("STAGE " + sum1.ToString().Replace("\n", " | "));
                Destroy(ai);
                yield return WaitOnRoad(id, fieldRun);
                yield return new WaitForSecondsRealtime(1f);
            }
        }
    }
}
