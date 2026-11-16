using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TAPES, played for real (2026-11-16, <see cref="Tapes"/>,
    /// <see cref="TapePlayback"/>). Run: a career race with the AI driving is
    /// recorded for a few seconds, SAVE TO TAPE cuts a tape (twice, an evening
    /// and a night race), the tape goes in the VCR (the venue loaded through
    /// TapePlayback.Play, the race taken over before its countdown), the deck
    /// plays, scans forward and back, pauses, stops at the end and EJECTS home
    /// with nothing banked. Frames of the deck are written as it goes. Pages:
    /// the shelf and the VIDEO STORE photographed at three screen shapes from
    /// the tapes Run cut, before and after a sale.
    ///
    /// Tapes go to &lt;project&gt;/TapeCheck/Tapes, never the player's folder, and
    /// the career is a scratch save. Report: PSXRacing_tape_check.txt.
    /// </summary>
    public static class TapeCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static int venue;

        internal static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        internal static string TapeDir => Path.Combine(ProjectDir, "TapeCheck", "Tapes");
        internal static string ShotDir => Path.Combine(ProjectDir, "Screenshots", "tapes");
        internal static string ReportPath => Path.Combine(ProjectDir, "PSXRacing_tape_check.txt");

        [MenuItem("PSX Racing/Check Tapes (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            // A mountain stage with a field (PSX_TAPE_VENUE to change it).
            string want = System.Environment.GetEnvironmentVariable("PSX_TAPE_VENUE");
            if (string.IsNullOrEmpty(want)) want = "GillespieGap";
            if (!TrackCatalog.TryIndexOf(want, out venue)) venue = TrackCatalog.IndexOf("GillespieGap");
            if (File.Exists(ReportPath)) File.Delete(ReportPath);
            if (Directory.Exists(TapeDir)) Directory.Delete(TapeDir, true);
            Directory.CreateDirectory(TapeDir);
            Directory.CreateDirectory(ShotDir);
            Tapes.DirOverride = TapeDir;

            // A scratch career: eject lands on the home screen, and the check
            // reads its clock and its money there.
            LifeSimManager.UseScratchSave("tapecheck");
            LifeSimManager.StartNewGame("TAPES", 25, LifeRules.DefaultJobIndex);
            LifeRules.SeedFallbackCar(LifeSimManager.State);
            LifeSimManager.Save();

            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(venue);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            { Check(false, "scene not built"); Finish(); EditorApplication.Exit(1); return; }
            log.AppendLine("venue " + TrackCatalog.At(venue).id + " scene " + s + " " + scenes[s].path);

            EditorSceneManager.OpenScene(scenes[s].path);
            Prime(TimeOfDay.Sunset);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>A career race at the check's venue: four cars, the AI driving.</summary>
        internal static void Prime(int hour)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = venue;
            RaceHandoff.CalendarDay = 320;   // NOV 16 1999
            RaceHandoff.TimeOfDayIndex = hour;
            RaceHandoff.TrafficLevel = 0;
            var cars = CarCatalog.All;
            if (cars.Count > 4)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id + ";" + cars[3].id;
                RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            }
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TapeCheckRunner").AddComponent<TapeCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            string line = "  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : "");
            log.AppendLine(line);
            Debug.Log("[TapeCheck]" + line);
        }

        internal static void Line(string s) { log.AppendLine(s); Debug.Log("[TapeCheck] " + s); }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE TAPES PLAY." : failures + " FAILURE(S).");
            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log(log.ToString());
            Tapes.DirOverride = null;
            LifeSimManager.UseCareerSave();
        }

        // ------------------------------------------------------------------
        //  The pages, in edit mode, from the tapes Run cut
        // ------------------------------------------------------------------
        [MenuItem("PSX Racing/Preview Tape Shelf + Video Store")]
        public static void Pages()
        {
            log = new StringBuilder();
            failures = 0;
            string report = Path.Combine(ProjectDir, "PSXRacing_tape_pages.txt");
            if (File.Exists(report)) File.Delete(report);
            Directory.CreateDirectory(ShotDir);
            Tapes.DirOverride = TapeDir;
            try
            {
                LifeSimManager.UseScratchSave("tapepages");
                LifeSimManager.StartNewGame("TAPES", 25, LifeRules.DefaultJobIndex);
                LifeRules.SeedFallbackCar(LifeSimManager.State);
                var st = LifeSimManager.State;
                st.money = 1250;
                LifeSimManager.Save();

                var shelf = Tapes.Shelf();
                Check(shelf.Count >= 1, "the check's tapes are on the shelf", shelf.Count);
                Line("shelf: " + shelf.Count + " tape(s) in " + TapeDir);
                foreach (var h in shelf)
                    Line("  " + h.Spine + "  " + h.Length + "  $" + Tapes.Price(h) + "  " + new FileInfo(h.path).Length + " bytes");

                var sizes = new (string name, int w, int h)[]
                {
                    ("phone_wide", 1998, 891), ("landscape_16x9", 1280, 720), ("tablet_4x3", 1024, 768), ("owner_2000x1077", 2000, 1077),
                };
                string pick = shelf.Count > 0 ? shelf[0].id : "";
                var fields = new (string name, object value)[] { ("tapeSel", pick) };
                // The montage door open for the pictures (PSX_MONTAGE=1 in a game).
                Tapes.ForceMontage(true);
                LifeHomePreview.Shoot(ShotDir, "garage_tapedoor", "garage", scrollTo: 0f, sizes: sizes);
                LifeHomePreview.Shoot(ShotDir, "tape_shelf", "tapes", sizes: sizes, fields: fields);
                LifeHomePreview.Shoot(ShotDir, "video_store", "videostore", sizes: sizes, fields: fields);
                // Armed: the second press is the sale.
                var armed = new (string name, object value)[] { ("tapeSel", pick), ("armKey", "selltape:" + pick) };
                LifeHomePreview.Shoot(ShotDir, "video_store_armed", "videostore", sizes: sizes, fields: armed);

                // THE MONTAGE page: IN / OUT marked on the picked tape, one clip
                // already added.
                if (shelf.Count > 0)
                {
                    var one = new List<(string id, float tIn, float tOut)> { (pick, 1f, Mathf.Min(shelf[0].duration, 6f)) };
                    var mfields = new (string name, object value)[]
                    {
                        ("tapeSel", pick), ("montTape", pick), ("montIn", Mathf.Min(shelf[0].duration - 2f, 7f)),
                        ("montOut", Mathf.Min(shelf[0].duration, 11f)), ("montClips", one),
                    };
                    LifeHomePreview.Shoot(ShotDir, "montage", "montage", sizes: sizes, fields: mfields);
                }

                // THE SALE, as the second press makes it, then the store again.
                if (shelf.Count > 0)
                {
                    int before = st.money;
                    int offer = Tapes.Price(shelf[0]);
                    int log0 = st.calendarLog.Count;
                    var host = new GameObject("SellHost");
                    var screen = host.AddComponent<LifeHomeScreen>();
                    screen.GetType().GetField("tapeSel", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(screen, pick);
                    var sell = typeof(LifeHomeScreen).GetMethod("SellTape", BindingFlags.NonPublic | BindingFlags.Instance);
                    // SellTape rebuilds the page; with no page built that is a
                    // no-op, and the money, the diary and the file are the sale.
                    try { sell.Invoke(screen, new object[] { shelf[0] }); }
                    catch (System.Exception e) { Line("  (rebuild after the sale: " + (e.InnerException ?? e).GetType().Name + ")"); }
                    Object.DestroyImmediate(host);
                    st = LifeSimManager.State;
                    Check(st.money == before + offer, "the sale pays the offer into the wallet", before + " + " + offer + " = " + st.money);
                    Check(st.calendarLog.Count == log0 + 1 && st.calendarLog[st.calendarLog.Count - 1].Contains("video store"),
                          "one diary line", st.calendarLog.Count > 0 ? st.calendarLog[st.calendarLog.Count - 1] : "");
                    Check(!File.Exists(shelf[0].path) && Tapes.Shelf().Count == shelf.Count - 1, "and the tape has left the shelf");
                    var left = Tapes.Shelf();
                    var after = new (string name, object value)[] { ("tapeSel", left.Count > 0 ? left[0].id : "") };
                    LifeHomePreview.Shoot(ShotDir, "video_store_sold", "videostore", sizes: sizes, fields: after);
                    LifeHomePreview.Shoot(ShotDir, "main_after_sale", "main", sizes: new[] { sizes[1] });
                }
            }
            catch (System.Exception e) { Check(false, "pages threw", e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
            finally
            {
                Tapes.DirOverride = null;
                Tapes.ForceMontage(false);
                LifeSimManager.UseCareerSave();
                log.AppendLine(failures == 0 ? "PAGES OK." : failures + " FAILURE(S).");
                File.WriteAllText(report, log.ToString());
                Debug.Log(log.ToString());
            }
        }
    }

    public class TapeCheckRunner : MonoBehaviour
    {
        /// <summary>The countdown (4 s) and twelve seconds of racing.</summary>
        const float RecordSeconds = 16f;
        static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        static void Check(bool ok, string what, object got = null) => TapeCheck.Check(ok, what, got);

        /// <summary>Real seconds the whole check may take before it calls
        /// itself HUNG and exits: a hidden job is never killed by the tools,
        /// so a wait that never comes must end itself.</summary>
        const float HangSeconds = 900f;
        float bornAt;
        bool exited;
        void Awake() { bornAt = Time.realtimeSinceStartup; }
        void Update()
        {
            if (exited || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            exited = true;
            TapeCheck.Check(false, "the check HUNG (" + HangSeconds + " s) - exiting");
            TapeCheck.Finish();
            EditorApplication.Exit(2);
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            Tapes.Header first = null, second = null;
            yield return Race("evening", h => first = h);
            if (first != null)
            {
                // A second tape, at night: the scene again under the night.
                TapeCheck.Prime(TimeOfDay.Night);
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                yield return null;
                yield return Race("night", h => second = h);
            }
            if (first != null) yield return Vcr(first);
            if (second != null) yield return MontagePlay(second);
            Done();
        }

        /// <summary>Wait for the race, let the AI drive, cut the tape.</summary>
        IEnumerator Race(string label, System.Action<Tapes.Header> got)
        {
            TapeCheck.Line(label + " race:");
            float t0 = Time.realtimeSinceStartup;
            while ((RaceManager.Instance == null || RaceReplay.Instance == null) && Time.realtimeSinceStartup - t0 < 60f)
                yield return null;
            var rm = RaceManager.Instance;
            var rp = RaceReplay.Instance;
            Check(rm != null && rp != null, "the race and its recorder are up");
            if (rm == null || rp == null) yield break;
            yield return null;
            var offer = TapeOffer.Instance;
            Check(offer != null, "a career race gets the SAVE TO TAPE offer");
            Check(!TapePlayback.Active, "and is not a tape");
            if (offer == null) yield break;

            // The player's car on autopilot (RacePlayCheck's way), so the tape
            // has the player's own car racing on it.
            var car = rm.playerCar;
            if (car != null)
            {
                var input = car.GetComponent<PlayerCarInput>();
                if (input != null) { input.inputEnabled = false; input.enabled = false; }
                var auto = car.gameObject.AddComponent<AIDriver>();
                auto.path = rm.path;
                auto.skill = 0.95f;
                car.allowReverse = false;
            }

            t0 = Time.time;
            while (Time.time - t0 < RecordSeconds) yield return null;
            // The flag, for the check: the recorder stops here (ReplayCheck's way).
            typeof(RaceReplay).GetField("recording", Priv).SetValue(rp, false);
            Check(rp.RecordingDone, "the recording is done", rp.FrameCount + " samples");
            bool staged = label == "evening";
            if (staged)
            {
                // STAGED: the results screen, with the race ended by the check
                // (nobody drove to the flag), to show the SAVE TO TAPE line and
                // the touch / mouse button where a player sees them.
                typeof(RaceManager).GetProperty("State").SetValue(rm, RaceManager.RaceState.Finished);
                TapeOffer.ShowButtonForTools = true;
                yield return null;
                yield return null;
                PutButtonInFrame(offer);
                yield return Shot("results_staged");
                Check(TapeOffer.OfferLine(false).Contains("SAVE TO TAPE"), "the results offer SAVE TO TAPE", TapeOffer.OfferLine(false).Trim());
            }
            offer.PressForTest();
            yield return null;
            yield return null;
            if (staged)
            {
                yield return null;
                PutButtonInFrame(offer);
                yield return Shot("results_saved_staged");
                Check(TapeOffer.OfferLine(false).Contains("ON THE SHELF"), "and then say where it went", TapeOffer.OfferLine(false).Trim());
                TapeOffer.ShowButtonForTools = false;
            }
            var h = offer.SavedTape;
            Check(h != null && File.Exists(h.path), "SAVE TO TAPE cut a tape", h != null ? h.path : offer.Refusal);
            if (h == null) yield break;
            long bytes = new FileInfo(h.path).Length;
            Check(bytes > 100 && bytes < 1024 * 1024, "the tape is small", bytes + " bytes for " + h.duration.ToString("0.0") + " s, " + h.cars.Count + " cars");
            TapeCheck.Line("  label: " + h.Spine + "  facts: top " + h.topKmh.ToString("0") + " km/h, crashes " + h.crashes +
                           ", near misses " + h.nearMisses + ", margin " + h.margin.ToString("0.00") + "  price $" + Tapes.Price(h));
            Check(h.cars.Count == rp.TrackCount && h.cars[h.playerIndex].player, "the field is on the label, the player's car marked",
                  h.cars.Count + " cars, player #" + h.playerIndex);
            Check(Camcorder.DateStamp(h.day) == "NOV 16 1999", "the career's date is on it", Camcorder.DateStamp(h.day));

            // The tape is the recording, at 15 Hz.
            var back = Tapes.Load(h.path, out var tt, out var ff, out string why);
            Check(back != null, "it loads back", why);
            if (back != null)
            {
                float worst = 0f;
                for (int c = 0; c < rp.TrackCount; c++)
                {
                    var src = rp.TrackFrames(c);
                    for (int k = 0; k < tt.Count; k += 5)
                    {
                        int j = Mathf.Min(k * 2, src.Count - 1);
                        worst = Mathf.Max(worst, (src[j].pos - ff[c][k].pos).magnitude);
                    }
                }
                Check(worst < 0.01f, "every car where the recording had it", worst.ToString("0.0000") + " m");
                Check(Mathf.Abs(tt.Count - rp.FrameCount / 2f) <= 2f, "every other sample (15 Hz)", tt.Count + " of " + rp.FrameCount);
            }
            got(h);
        }

        IEnumerator Shot(string name)
        {
            // Frames, never WaitForEndOfFrame: batch mode has no game view, so
            // it never comes (it hung the first run of this check for good).
            yield return null;
            yield return null;
            yield return null;
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            { TapeCheck.Line("  shot " + name + " not taken (no camera / no graphics)"); yield break; }
            var fb = cam.targetTexture;
            if (fb == null) { TapeCheck.Line("  shot " + name + " not taken (no framebuffer)"); yield break; }
            var prev = RenderTexture.active;
            RenderTexture.active = fb;
            var tex = new Texture2D(fb.width, fb.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, fb.width, fb.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(TapeCheck.ShotDir, "vcr_" + name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            TapeCheck.Line("  shot vcr_" + name + " (" + fb.width + "x" + fb.height + ")");
        }

        /// <summary>The SAVE TO TAPE button lives on an overlay canvas, which a
        /// framebuffer read never sees: for the picture it is drawn into the
        /// PSX camera's frame at the same relative place (its scaler is
        /// resolution-relative), over everything.</summary>
        static void PutButtonInFrame(TapeOffer offer)
        {
            var go = offer != null ? offer.ButtonCanvas : null;
            var cam = Camera.main;
            if (go == null || cam == null) { TapeCheck.Line("  (no button canvas to draw)"); return; }
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = Mathf.Max(cam.nearClipPlane * 1.5f, 0.4f);
            var hud = HudOnTop.Material;
            if (hud != null) foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.material = hud;
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>Wait this much GAME time (the replay runs on it; a busy
        /// sandbox draws a few frames a second), never more than a minute.</summary>
        IEnumerator Real(float seconds)
        {
            float t = Time.time, r = Time.realtimeSinceStartup;
            while (Time.time - t < seconds && Time.realtimeSinceStartup - r < 60f) yield return null;
        }

        IEnumerator Vcr(Tapes.Header h)
        {
            TapeCheck.Line("the VCR:");
            var S = LifeSimManager.State;
            int money0 = S.money, day0 = S.day, slot0 = S.slotIndex;

            Check(TapePlayback.Play(h, out string why), "PLAY ON THE VCR loads the venue", why);
            float t0 = Time.realtimeSinceStartup;
            while (!(TapePlayback.Active && RaceReplay.Playing) && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            var rp = RaceReplay.Instance;
            var rm = RaceManager.Instance;
            Check(TapePlayback.Active && RaceReplay.Playing, "the tape is playing", (Time.realtimeSinceStartup - t0).ToString("0.0") + " s after PLAY");
            if (rp == null || rm == null || !RaceReplay.Playing) yield break;
            Check(rp.VcrControls && Camcorder.Vcr, "on the deck's transport, with the deck's OSD");
            Check(TapeOffer.Instance == null, "a tape offers no SAVE TO TAPE of itself");
            Check(rp.TrackCount == h.cars.Count, "the whole field is on the screen", rp.TrackCount + " of " + h.cars.Count);
            Check(Camcorder.StampDay == h.day, "the burned-in date is the tape's", Camcorder.DateStamp(Camcorder.StampDay));
            yield return Real(0.5f);
            Check(rm.State == RaceManager.RaceState.Countdown && rm.CountdownRemaining >= 3.99f,
                  "the race never started under it", rm.State + " " + rm.CountdownRemaining.ToString("0.00"));
            // The deck follows the player's own car (it drove on autopilot).
            yield return Real(2.5f);

            // Every car where the tape has it.
            var tape = Tapes.Load(h.path, out var tt, out var ff, out _);
            if (tape != null)
            {
                float rt = rp.ReplayTime, worst = 0f;
                int k = 0;
                while (k < tt.Count - 2 && tt[k + 1] <= rt) k++;
                float u = tt[k + 1] > tt[k] ? Mathf.Clamp01((rt - tt[k]) / (tt[k + 1] - tt[k])) : 0f;
                for (int c = 0; c < rp.TrackCount; c++)
                {
                    var car = rp.TrackCar(c);
                    if (car == null) continue;
                    var want = Vector3.Lerp(ff[c][k].pos, ff[c][k + 1].pos, u);
                    worst = Mathf.Max(worst, (car.transform.position - want).magnitude);
                }
                Check(worst < 2.5f, "every car on its taped pose", worst.ToString("0.00") + " m at " + rt.ToString("0.0") + " s");
            }
            yield return Shot("play");

            var rate = typeof(RaceReplay).GetProperty("Rate");
            var paused = typeof(RaceReplay).GetField("paused", Priv);
            float vol = AudioListener.volume;

            // FF
            float a = rp.ReplayTime;
            rate.SetValue(rp, RaceReplay.ScanRate);
            yield return Real(0.6f);
            Check(Camcorder.Transport == 2 && Camcorder.ScanRate > 1f, "FF scans", "transport " + Camcorder.Transport + " x" + Camcorder.ScanRate);
            Check(AudioListener.volume == 0f, "with the sound off");
            yield return Shot("ff");
            float b = rp.ReplayTime;
            Check(b - a > 1.5f, "the picture runs fast forward", (b - a).ToString("0.0") + " s of tape in 0.6 s");

            // REW
            rate.SetValue(rp, -RaceReplay.ScanRate);
            yield return Real(0.5f);
            float c2 = rp.ReplayTime;
            Check(c2 < b - 1f && Camcorder.ScanRate < -1f, "REW scans back", (b - c2).ToString("0.0") + " s back");
            yield return Shot("rew");

            // PLAY again: the sound comes back.
            rate.SetValue(rp, 1f);
            yield return Real(0.4f);
            Check(Mathf.Approximately(AudioListener.volume, vol) && Camcorder.Transport == 0, "PLAY brings the sound back", AudioListener.volume);

            // PAUSE holds the frame.
            paused.SetValue(rp, true);
            yield return Real(0.2f);
            float p0 = rp.ReplayTime;
            yield return Real(0.8f);
            Check(Mathf.Abs(rp.ReplayTime - p0) < 0.01f && Camcorder.Transport == 1, "PAUSE holds the frame", rp.ReplayTime - p0);
            yield return Shot("pause");
            paused.SetValue(rp, false);

            // The end of the tape stops the deck instead of looping.
            rp.Seek(rp.Duration - 0.4f, hard: true);
            yield return Real(1.2f);
            Check(rp.Paused && rp.ReplayTime >= rp.Duration - 0.01f, "the end of the tape stops the deck", rp.ReplayTime.ToString("0.00") + " / " + rp.Duration.ToString("0.00"));

            // EJECT: home, nothing banked.
            bool took = RaceReplay.ExitHook != null && RaceReplay.ExitHook();
            Check(took, "EJECT takes the exit press");
            t0 = Time.realtimeSinceStartup;
            LifeHomeScreen home = null;
            while (home == null && Time.realtimeSinceStartup - t0 < 30f)
            {
                yield return null;
                if (SceneManager.GetActiveScene().buildIndex == 0) home = Object.FindFirstObjectByType<LifeHomeScreen>();
            }
            yield return null;
            yield return null;
            Check(SceneManager.GetActiveScene().buildIndex == 0 && home != null, "EJECT lands at home");
            if (home != null)
            {
                var tab = typeof(LifeHomeScreen).GetField("tab", Priv)?.GetValue(home) as string;
                Check(tab == "tapes", "on the tape shelf", tab);
            }
            S = LifeSimManager.State;
            Check(!RaceHandoff.ResultReady && !Camcorder.Vcr && !TapePlayback.Active, "no result, no deck left running");
            Check(S.money == money0 && S.day == day0 && S.slotIndex == slot0, "no money, no block of the day spent",
                  "money " + money0 + "->" + S.money + ", day " + day0 + "->" + S.day + ", block " + slot0 + "->" + S.slotIndex);
        }

        /// <summary>THE MONTAGE: two clips of the night tape joined, played on
        /// the deck across the cut, then ejected.</summary>
        IEnumerator MontagePlay(Tapes.Header src)
        {
            TapeCheck.Line("the montage:");
            float d = src.duration;
            var clips = new List<Tapes.Clip>
            {
                new Tapes.Clip { tape = src, tIn = 1f, tOut = Mathf.Max(3f, d * 0.4f) },
                new Tapes.Clip { tape = src, tIn = d * 0.6f, tOut = d - 0.5f },
            };
            var m = Tapes.Montage(clips, out string why);
            Check(m != null && m.montage && m.cuts.Count == 1, "two clips of the night tape join onto a montage", m != null ? m.Spine + " cut at " + m.cuts[0].ToString("0.00") : why);
            if (m == null) yield break;
            Check(!File.Exists(src.path), "the night tape is taped over");
            Check(TapePlayback.Play(m, out why), "the montage goes in the VCR", why);
            float t0 = Time.realtimeSinceStartup;
            while (!(TapePlayback.Active && RaceReplay.Playing) && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            var rp = RaceReplay.Instance;
            Check(RaceReplay.Playing && rp != null, "and plays");
            if (rp == null || !RaceReplay.Playing) yield break;
            var tape = Tapes.Load(m.path, out var tt, out var ff, out _);
            float cut = m.cuts[0];
            rp.Seek(Mathf.Max(0f, cut - 0.6f), hard: true);
            yield return Real(0.25f);
            yield return Shot("montage_before_cut");
            // Across the cut at play speed, then look at where the cars stand.
            float waitUntil = Time.realtimeSinceStartup + 60f;
            while (rp.ReplayTime < cut + 0.25f && Time.realtimeSinceStartup < waitUntil) yield return null;
            yield return null;
            if (tape != null)
            {
                float rt = rp.ReplayTime, worst = 0f;
                int k = 0;
                while (k < tt.Count - 2 && tt[k + 1] <= rt) k++;
                float u = tt[k + 1] > tt[k] ? Mathf.Clamp01((rt - tt[k]) / (tt[k + 1] - tt[k])) : 0f;
                for (int c = 0; c < rp.TrackCount; c++)
                {
                    var car = rp.TrackCar(c);
                    if (car == null) continue;
                    worst = Mathf.Max(worst, (car.transform.position - Vector3.Lerp(ff[c][k].pos, ff[c][k + 1].pos, u)).magnitude);
                }
                Check(rt > cut && worst < 2.5f, "past the cut every car stands in the second clip", worst.ToString("0.00") + " m at " + rt.ToString("0.00") + " s (cut " + cut.ToString("0.00") + ")");
            }
            yield return Shot("montage_after_cut");
            bool took = RaceReplay.ExitHook != null && RaceReplay.ExitHook();
            Check(took, "EJECT");
            t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().buildIndex != 0 && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            yield return null;
            Check(SceneManager.GetActiveScene().buildIndex == 0 && !TapePlayback.Active, "home again");
        }

        void Done()
        {
            if (exited) return;
            exited = true;
            TapeCheck.Finish();
            EditorApplication.Exit(TapeCheck.failures == 0 ? 0 : 1);
        }
    }
}
