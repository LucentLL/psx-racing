using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.City;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE GUIDED FIRST DRIVE, driven (City.FirstDrive): Charlotte with
    /// RaceHandoff.FirstDrive set, a virtual pad on the car, every step satisfied
    /// the way a player would (a teleport only brings the car up to a place; the
    /// pad drives the last stretch), and no step's state ever written. Each step
    /// change is logged with the time and the car's position, the dead ends tried
    /// after the run. RunMenus previews the license pages, walks GO BACK and the
    /// wizard in play, and the strip BACK from a forecourt page.
    ///
    ///   -executeMethod PSXRacing.EditorTools.FirstDrivePlayCheck.Run       (PSX_FD_SCEN=main|all)
    ///   -executeMethod PSXRacing.EditorTools.FirstDrivePlayCheck.RunMenus
    ///
    /// PSX_FD_OUT = folder for the report and PNGs (default: the project root).
    /// </summary>
    public static class FirstDrivePlayCheck
    {
        internal static readonly StringBuilder log = new StringBuilder();
        internal static int failures;
        internal static string report;
        internal const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        internal static string OutDir
        {
            get
            {
                var d = Environment.GetEnvironmentVariable("PSX_FD_OUT");
                if (string.IsNullOrEmpty(d)) d = Path.GetDirectoryName(Application.dataPath);
                Directory.CreateDirectory(d);
                return d;
            }
        }

        internal static void Line(string s)
        {
            log.AppendLine(s);
            try { File.WriteAllText(Path.Combine(OutDir, report), log.ToString()); } catch { }
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            Line("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            Line(failures == 0 ? "NO FAILURES." : failures + " FAILURE(S).");
            Debug.Log(log.ToString());
        }

        public static void Run()
        {
            report = "firstdrive_check_" + Edition.Name(Edition.Current) + ".txt";
            EditionParking.RecoverIfNeeded();
            Line("edition " + Edition.Name(Edition.Current) + ", career " + Edition.HasCareer);
            int roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int si = roamIdx >= 0 ? TrackCatalog.SceneIndex(roamIdx) : -1;
            if (si < 0 || si >= scenes.Length || !File.Exists(scenes[si].path))
            { Check(false, "Charlotte is built"); Finish(); EditorApplication.Exit(1); return; }

            if (Edition.HasCareer)
            {
                LifeSimManager.StartNewGame("FIRST", 24, 0);
                var S = LifeSimManager.State;
                S.cars.Clear(); S.activeCar = null; S.money = 3000;
                CarMarket.MakeOwnedCar(S, CarCatalog.All[Mathf.Min(5, CarCatalog.All.Count - 1)], 80, 60000f, 4000);
                S.ActiveCar.fuel = 100f;
                LifeSimManager.Save();
                var car = S.ActiveCar;
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.FreeRoam = true;
                RaceHandoff.TrackIndex = roamIdx;
                RaceHandoff.TimeOfDayIndex = TimeOfDay.Afternoon;
                RaceHandoff.CarId = car.id;
                RaceHandoff.CarSpecId = car.specId;
            }
            else
            {
                Check(CityFrontEnd.FillFreeRoam(CityFrontEnd.DefaultCarId(), TimeOfDay.Afternoon, 0, out int scene) && scene == si,
                      "CITY front end fills the handoff");
            }
            RaceHandoff.FirstDrive = true;
            // As StartFreeRoam does for a first drive.
            RaceHandoff.StartFuelPct = 60f;

            EditorSceneManager.OpenScene(scenes[si].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnPlay;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlay(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlay;
            new GameObject("FirstDriveRunner").AddComponent<FirstDriveRunner>();
        }

        // ------------------------------------------------------------------
        //  the wizard's pages
        // ------------------------------------------------------------------
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("phone_wide", 1998, 891),
            ("landscape_16x9", 1280, 720),
            ("tablet_4x3", 1024, 768),
        };

        public static void RunMenus()
        {
            report = "firstdrive_menus.txt";
            EditionParking.RecoverIfNeeded();
            Line("edition " + Edition.Name(Edition.Current));
            LifeSimManager.DeleteSave();
            foreach (var size in Sizes) ShootPages(size);
            LifeSimManager.DeleteSave();
            MenuKit.ScreenSizeOverride = Vector2.zero;

            var path = EditorBuildSettings.scenes[0].path;
            EditorSceneManager.OpenScene(path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnPlayMenus;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMenus(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMenus;
            new GameObject("FirstDriveMenuRunner").AddComponent<FirstDriveMenuRunner>();
        }

        internal static string Reach(Transform root)
        {
            var all = new List<Selectable>(root.GetComponentsInChildren<Selectable>());
            if (all.Count == 0) return "no controls";
            var seen = new HashSet<Selectable> { all[0] };
            var q = new Queue<Selectable>(); q.Enqueue(all[0]);
            while (q.Count > 0)
            {
                var nav = q.Dequeue().navigation;
                foreach (var n in new[] { nav.selectOnUp, nav.selectOnDown, nav.selectOnLeft, nav.selectOnRight })
                    if (n != null && seen.Add(n)) q.Enqueue(n);
            }
            var names = new List<string>();
            foreach (var s in all)
            {
                var t = s.GetComponentInChildren<Text>();
                names.Add((t != null ? t.text : s.name) + (seen.Contains(s) ? "" : " UNREACHABLE"));
            }
            return seen.Count + "/" + all.Count + " reachable: " + string.Join(" | ", names);
        }

        static void ShootPages((string name, int w, int h) size)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGO = new GameObject("PreviewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.04f, 0.09f);
            cam.orthographic = true;
            var rt = new RenderTexture(size.w, size.h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            MenuKit.ScreenSizeOverride = new Vector2(size.w, size.h);
            var host = new GameObject("LifeHome");
            var screen = host.AddComponent<LifeHomeScreen>();
            typeof(LifeHomeScreen).GetMethod("Start", Any).Invoke(screen, null);
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 10f;
                var cs = c.GetComponent<CanvasScaler>();
                if (cs != null) { cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = size.h / MenuKit.DesignHeight; }
            }
            Canvas.ForceUpdateCanvases();
            RectTransform root = null;
            foreach (var r in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
                if (r.name == "Wizard") root = r;
            Check(root != null, "the wizard's panel is up at " + size.name);
            if (root == null) return;
            typeof(LifeHomeScreen).GetMethod("AskLicense", Any).Invoke(screen, new object[] { root, "TEST" });
            Canvas.ForceUpdateCanvases();
            var ask = root.Find("License");
            Page(cam, rt, size, "license", ask);
            Button yes = null;
            foreach (var b in ask.GetComponentsInChildren<Button>())
                if (b.GetComponentInChildren<Text>().text == "YES") yes = b;
            yes.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            Page(cam, rt, size, "license_confirm", ask);
            cam.targetTexture = null;
            rt.Release();
        }

        static void Page(Camera cam, RenderTexture rt, (string name, int w, int h) size, string label, Transform page)
        {
            LifeHomePreview.ReportClippedText(cam, label + "/" + size.name, size.w, size.h);
            var rows = MenuNav.Collect(page);
            if (MenuNav.RectsResolved(rows)) MenuNav.Grid(rows); else Line("  rects unresolved at " + size.name);
            Line("  " + label + "/" + size.name + " nav " + Reach(page));
            string Cap(Selectable x) { if (x == null) return "-"; var t = x.GetComponentInChildren<Text>(); return t != null ? t.text : x.name; }
            foreach (var r in rows)
                Line("    " + Cap(r) + ": up " + Cap(r.navigation.selectOnUp) + ", down " + Cap(r.navigation.selectOnDown) + ", left " + Cap(r.navigation.selectOnLeft) + ", right " + Cap(r.navigation.selectOnRight));
            foreach (var t in page.GetComponentsInChildren<Text>())
            {
                var gs = t.GetGenerationSettings(t.rectTransform.rect.size);
                var g = new TextGenerator();
                g.Populate(t.text, gs);
                Line("    text \"" + t.text.Replace("\n", " / ") + "\" rect " + t.rectTransform.rect.size +
                     " lines " + g.lineCount + " prefW " + t.preferredWidth.ToString("0") + " prefH " + t.preferredHeight.ToString("0"));
            }
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size.w, size.h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size.w, size.h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, label + "_" + size.name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }

    // ======================================================================
    //  The wizard in play, GO BACK, and the strip's BACK from a forecourt.
    // ======================================================================
    public class FirstDriveMenuRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => FirstDrivePlayCheck.Check(ok, what, got);
        static void Note(string s) => FirstDrivePlayCheck.Line(s);
        const BindingFlags Any = FirstDrivePlayCheck.Any;
        float bornAt; bool finished;
        Gamepad pad;

        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < 600f) return;
            Check(false, "HUNG: 600 s");
            Done();
        }

        void Done()
        {
            if (finished) return;
            finished = true;
            FirstDrivePlayCheck.Finish();
            EditorApplication.Exit(FirstDrivePlayCheck.failures == 0 ? 0 : 1);
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static Button Named(Transform under, string label)
        {
            foreach (var b in under.GetComponentsInChildren<Button>())
            {
                var t = b.GetComponentInChildren<Text>();
                if (t != null && t.text == label) return b;
            }
            return null;
        }

        static string Sel()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return "nothing selected";
            var t = go.GetComponentInChildren<Text>();
            return go.name + " \"" + (t != null ? t.text : "") + "\"" + (go.activeInHierarchy ? "" : " INACTIVE");
        }

        IEnumerator Press(GamepadButton b)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(b));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
        }

        IEnumerator Start()
        {
            yield return Frames(30);
            var screen = FindFirstObjectByType<LifeHomeScreen>();
            Check(screen != null, "the home screen is up");
            if (screen == null) { Done(); yield break; }
            try { pad = InputSystem.AddDevice<Gamepad>(); }
            catch (Exception e) { Note("no virtual pad: " + e.Message); }

            yield return Wizard(screen);
            yield return StripBack();
            Done();
        }

        IEnumerator Wizard(LifeHomeScreen screen)
        {
            Note("WIZARD (play)");
            var wiz = GameObject.Find("Wizard");
            Check(wiz != null, "no save: the wizard is up");
            if (wiz == null) yield break;
            var field = wiz.GetComponentInChildren<InputField>();
            if (field != null) field.text = "tester";
            var next = Named(wiz.transform, "NEXT: PICK A CAR");
            Check(next != null, "NEXT is there");
            next.onClick.Invoke();
            yield return Frames(3);
            var ask = wiz.transform.Find("License");
            Check(ask != null, "NEXT opens the license question");
            Check(!LifeSimManager.HasSave || string.IsNullOrEmpty(LifeSimManager.State.playerName),
                  "and no career is made yet");

            for (int round = 0; round < 3; round++)
            {
                var yes = Named(ask, "YES"); var no = Named(ask, "NO");
                Check(yes != null && no != null && yes.interactable && no.interactable && yes.isActiveAndEnabled,
                      "round " + round + ": YES and NO alive", FirstDrivePlayCheck.Reach(ask));
                Check(Sel().Contains("YES") || Sel().Contains("NO"), "  and a pad has a selection on the page", Sel());
                Check(yes.navigation.selectOnRight == no && no.navigation.selectOnLeft == yes, "  left/right joins them");
                int kids = ask.childCount;
                if (pad != null && round == 1)
                {
                    // a real pad: right to NO, left to YES, south presses it
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(yes.gameObject);
                    yield return Press(GamepadButton.South);
                    Note("  pad south on YES -> page has " + (Named(ask, "GO BACK") != null ? "GO BACK" : "no GO BACK") + ", sel " + Sel());
                    if (Named(ask, "GO BACK") == null) yes.onClick.Invoke();
                }
                else yes.onClick.Invoke();
                yield return Frames(3);
                var skip = Named(ask, "YES, SKIP IT"); var back = Named(ask, "GO BACK");
                Check(skip != null && back != null && skip.isActiveAndEnabled && back.interactable,
                      "  YES -> the confirm page's buttons alive", FirstDrivePlayCheck.Reach(ask));
                Check(Named(ask, "YES") == null && Named(ask, "NO") == null, "  and the first page's are gone", "children " + kids + " -> " + ask.childCount);
                Check(Sel().Contains("YES, SKIP") || Sel().Contains("GO BACK"), "  selection on the confirm page", Sel());
                if (pad != null && round == 1)
                {
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(back.gameObject);
                    yield return Press(GamepadButton.South);
                    Note("  pad south on GO BACK -> " + (Named(ask, "YES") != null ? "first page" : "still confirm") + ", sel " + Sel());
                    if (Named(ask, "YES") == null) back.onClick.Invoke();
                }
                else back.onClick.Invoke();
                yield return Frames(3);
                Check(Named(ask, "YES") != null && Named(ask, "GO BACK") == null, "  GO BACK -> the first page again", Sel());
            }
            ask = null;
            ask = wiz.transform.Find("License");
            Keyboard kbd = null;
            try { kbd = InputSystem.AddDevice<Keyboard>(); } catch (Exception e) { Note("no virtual keyboard: " + e.Message); }
            for (int round = 0; round < 4; round++)
            {
                Note("  BACK round " + round);
                var yes = Named(ask, "YES"); var no = Named(ask, "NO"); var bk = Named(ask, "<  BACK");
                Check(yes != null && no != null && bk != null && bk.isActiveAndEnabled && bk.interactable, "  YES, NO and BACK alive", FirstDrivePlayCheck.Reach(ask));
                if (bk == null) yield break;
                if (round != 3)
                {
                    Check(yes.navigation.selectOnDown == bk && no.navigation.selectOnDown == bk, "  down from YES and NO reaches BACK");
                    Check(bk.navigation.selectOnUp == yes || bk.navigation.selectOnUp == no, "  up from BACK reaches YES or NO");
                }
                if (pad != null && round == 1)
                {
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(bk.gameObject);
                    yield return Press(GamepadButton.South);
                    Note("  pad south on BACK -> License " + (wiz.transform.Find("License") != null));
                    if (wiz.transform.Find("License") != null) bk.onClick.Invoke();
                }
                else bk.onClick.Invoke();
                yield return Frames(3);
                Check(wiz != null && wiz.transform.Find("License") == null, "  BACK returns to the name page");
                var f2 = wiz.GetComponentInChildren<InputField>();
                var nx = Named(wiz.transform, "NEXT: PICK A CAR");
                Check(f2 != null && f2.text == "tester", "  the typed name is kept", f2 != null ? f2.text : "no field");
                Check(nx != null && nx.isActiveAndEnabled && nx.interactable, "  NEXT is alive", Sel());
                Check(!Sel().Contains("nothing") && !Sel().Contains("INACTIVE"), "  a pad has a selection on the name page", Sel());
                Note("  name page: " + FirstDrivePlayCheck.Reach(wiz.transform));
                Check(!LifeSimManager.HasSave, "  and still no career");
                if (pad != null && round == 2)
                {
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(nx.gameObject);
                    yield return Press(GamepadButton.South);
                    Note("  pad south on NEXT -> License " + (wiz.transform.Find("License") != null));
                    if (wiz.transform.Find("License") == null) nx.onClick.Invoke();
                }
                else nx.onClick.Invoke();
                // round 3: BACK in the frame the page was built, before its graph has settled
                if (round != 2) yield return Frames(3);
                ask = wiz.transform.Find("License");
                Check(ask != null && Named(ask, "YES") != null && Named(ask, "<  BACK") != null, "  NEXT again: the license page with live buttons", Sel());
                if (ask == null) yield break;
            }
            yield return Frames(4);
            Check(Sel().Contains("YES") || Sel().Contains("NO") || Sel().Contains("BACK"), "  after the rounds the selection is on the page", Sel());
            // BACK from the confirm page's flow
            Named(ask, "YES").onClick.Invoke();
            yield return Frames(3);
            Check(Named(ask, "<  BACK") == null && Named(ask, "GO BACK") != null, "the confirm page has GO BACK, no BACK");
            string conf = FirstDrivePlayCheck.Reach(ask);
            if (pad != null)
            {
                yield return Press(GamepadButton.East);
                Check(Named(ask, "GO BACK") != null, "B on the confirm page does nothing", conf + ", sel " + Sel());
            }
            if (kbd != null)
            {
                InputSystem.QueueStateEvent(kbd, new KeyboardState(Key.Escape)); yield return Frames(3);
                InputSystem.QueueStateEvent(kbd, new KeyboardState()); yield return Frames(3);
                Check(Named(ask, "GO BACK") != null, "Esc on the confirm page does nothing");
            }
            Named(ask, "GO BACK").onClick.Invoke();
            yield return Frames(3);
            Check(Named(ask, "YES") != null && Named(ask, "<  BACK") != null, "GO BACK: the first page, BACK included", Sel());
            Named(ask, "<  BACK").onClick.Invoke();
            yield return Frames(3);
            Check(wiz.transform.Find("License") == null, "and its BACK reaches the name page");
            Named(wiz.transform, "NEXT: PICK A CAR").onClick.Invoke();
            yield return Frames(3);
            ask = wiz.transform.Find("License");
            Check(ask != null, "and NEXT the question");
            if (kbd != null)
            {
                InputSystem.QueueStateEvent(kbd, new KeyboardState(Key.Escape)); yield return Frames(3);
                InputSystem.QueueStateEvent(kbd, new KeyboardState()); yield return Frames(3);
                Check(wiz != null && wiz.transform.Find("License") != null, "Esc on the first page does nothing");
                InputSystem.RemoveDevice(kbd);
            }
            if (pad != null)
            {
                // B on the question. It has no cancel of its own: where does it land?
                yield return Press(GamepadButton.East);
                Note("  pad B on the first page: wizard " + (GameObject.Find("Wizard") != null) + ", License " + (wiz != null && wiz.transform.Find("License") != null) + ", sel " + Sel());
            }

            // NO: the career is made, the car pick opens
            Named(ask, "NO").onClick.Invoke();
            yield return Frames(3);
            bool wizFd = (bool)typeof(LifeHomeScreen).GetField("wizFirstDrive", Any).GetValue(null);
            Check(wizFd, "NO arms the first drive");
            Check(LifeSimManager.HasSave && LifeSimManager.State.playerName == "TESTER", "and makes the career", LifeSimManager.State.playerName);
            Check(wiz == null, "the wizard's panel is gone");
            var pick = GameObject.Find("CarPick") ?? GameObject.Find("Wizard");
            Note("  panel after NO: " + (pick != null ? pick.name : "none") + ", sel " + Sel());

            // Pick the first lane through its own confirm, as far as the hop.
            // pendingFirstDrive and PendingTab are what the reload reads.
            var lanes = typeof(LifeHomeScreen).GetField("pickLanes", Any).GetValue(screen) as System.Collections.IList;
            Check(lanes != null && lanes.Count > 0, "the car pick rolled lanes", lanes != null ? lanes.Count : 0);
            if (lanes != null && lanes.Count > 0)
            {
                typeof(LifeHomeScreen).GetMethod("BuildCarConfirm", Any).Invoke(screen, new object[] { lanes[0] });
                yield return Frames(2);
                Button drive = null;
                foreach (var b in FindObjectsByType<Button>(FindObjectsSortMode.None))
                {
                    var t = b.GetComponentInChildren<Text>();
                    if (t != null && t.text == "DRIVE THIS ONE") drive = b;
                }
                Check(drive != null, "DRIVE THIS ONE is there");
                if (drive != null)
                {
                    drive.onClick.Invoke();
                    Check((bool)typeof(LifeHomeScreen).GetField("pendingFirstDrive", Any).GetValue(null) || RaceHandoff.FirstDrive,
                          "the car chosen: the first drive is pending");
                    // Follow the reload: "charlotte" hops straight to free roam.
                    float t0 = Time.realtimeSinceStartup;
                    int roam = TrackCatalog.SceneIndex(TrackCatalog.IndexOf("Charlotte"));
                    while (SceneManager.GetActiveScene().buildIndex != roam && Time.realtimeSinceStartup - t0 < 120f) yield return null;
                    Check(SceneManager.GetActiveScene().buildIndex == roam, "the reload hops into Charlotte",
                          SceneManager.GetActiveScene().name + " after " + (Time.realtimeSinceStartup - t0).ToString("0") + " s");
                    Check(RaceHandoff.FirstDrive && RaceHandoff.StartFuelPct <= 60f, "with the first drive on and a tank a pump will serve", RaceHandoff.StartFuelPct);
                    float t1 = Time.realtimeSinceStartup;
                    while (!FirstDrive.Active && Time.realtimeSinceStartup - t1 < 60f) yield return null;
                    Check(FirstDrive.Active, "FirstDrive runs in the real flow", "tank " + (FindFirstObjectByType<FuelTank>()?.percent.ToString("0") ?? "?"));
                    yield return Frames(120);
                    Note("  prompt: " + FirstDrive.Prompt);
                    // back to the menu for the strip test
                    RaceHandoff.ClearAll();
                    LifeHomeScreen.PendingTab = "main";
                    SceneManager.LoadScene(0);
                    yield return Frames(60);
                }
            }
        }

        // ---- the strip's BACK and B from the forecourt pages ----
        IEnumerator StripBack()
        {
            Note("STRIP BACK");
            int townIdx = TrackCatalog.TownSceneIndex;
            Note("  town scene index " + townIdx);
            foreach (var pageName in new[] { "paint", "service" })
            {
                yield return OneBack(pageName, armed: true, carAway: false, useB: false, wantHop: true);
                yield return OneBack(pageName, armed: false, carAway: false, useB: false, wantHop: false);
                yield return OneBack(pageName, armed: true, carAway: true, useB: false, wantHop: false);
                yield return OneBack(pageName, armed: true, carAway: false, useB: true, wantHop: true);
                yield return OneBack(pageName, armed: false, carAway: false, useB: true, wantHop: false);
            }
            yield return OneBack("market", armed: true, carAway: false, useB: false, wantHop: false);
            yield return OneBack("market", armed: true, carAway: false, useB: true, wantHop: false);
        }

        IEnumerator OneBack(string page, bool armed, bool carAway, bool useB, bool wantHop)
        {
            string what = page + " armed=" + armed + " away=" + carAway + (useB ? " B" : " strip BACK");
            if (SceneManager.GetActiveScene().buildIndex != 0)
            {
                RaceHandoff.ClearAll();
                Town.TownReturn.Clear();
                LifeHomeScreen.PendingTab = "main";
                SceneManager.LoadScene(0);
                yield return Frames(30);
            }
            var screen = FindFirstObjectByType<LifeHomeScreen>();
            var S = LifeSimManager.State;
            if (screen == null || S == null) { Check(false, what + ": no home screen"); yield break; }
            var tabF = typeof(LifeHomeScreen).GetField("tab", Any);
            string saved = S.activeCar;
            if (carAway) S.activeCar = null;   // CarWhere.Available's no-car arm of the same guard
            Town.TownReturn.Pending = armed;
            Town.TownReturn.VenueName = "CHECK";
            Town.TownReturn.SpawnAtVenue = false;
            tabF.SetValue(screen, page);
            typeof(LifeHomeScreen).GetMethod("Rebuild", Any).Invoke(screen, null);
            yield return Frames(3);
            var back = typeof(LifeHomeScreen).GetField("tabBack", Any).GetValue(screen) as Button;
            int sceneWas = SceneManager.GetActiveScene().buildIndex;
            if (useB)
            {
                if (pad == null) { Note("  " + what + ": no pad, skipped"); yield break; }
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East));
                InputSystem.Update();
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(pad, new GamepadState());
            }
            else back.onClick.Invoke();
            bool spawn = Town.TownReturn.SpawnAtVenue;
            string tabNow = (string)tabF.GetValue(screen);
            float t0 = Time.realtimeSinceStartup;
            if (wantHop)
                while (SceneManager.GetActiveScene().buildIndex == sceneWas && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            else yield return Frames(3);
            string landed = SceneManager.GetActiveScene().name;
            bool hopped = SceneManager.GetActiveScene().buildIndex != sceneWas;
            if (carAway) S.activeCar = saved;
            Check(hopped == wantHop && (useB || spawn == wantHop) && (wantHop || tabNow != page),
                  what + " -> " + (hopped ? "scene " + landed + ", SpawnAtVenue " + spawn : "page " + tabNow),
                  "Pending " + Town.TownReturn.Pending);
            Town.TownReturn.Pending = false;
            if (!hopped) { tabF.SetValue(screen, "main"); typeof(LifeHomeScreen).GetMethod("Rebuild", Any).Invoke(screen, null); }
            yield return Frames(2);
        }
    }

    // ======================================================================
    //  The drive.
    // ======================================================================
    public class FirstDriveRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => FirstDrivePlayCheck.Check(ok, what, got);
        static void Note(string s) => FirstDrivePlayCheck.Line(s);
        const BindingFlags Any = FirstDrivePlayCheck.Any;
        const float HangSeconds = 2700f;
        const int Mask = ~(1 << 2);

        float bornAt; bool finished;
        CityMode mode; CityWorld world; CityMap map; CarController car;
        RaceHUD hud; HudMinimap mini; ForecourtMode fm; FuelTank tank; Gamepad pad;
        FirstDrive fd;
        IReadOnlyList<CitySignals.Junction> js;
        string lastStep = "", lastCenter = "?";
        static readonly FieldInfo StepF = typeof(FirstDrive).GetField("step", Any);

        string StepNow => fd != null && StepF != null ? StepF.GetValue(fd).ToString() : "-";
        Vector2 Plan => new Vector2(car.transform.position.x, car.transform.position.z);
        float Kmh => Mathf.Abs(car.forwardSpeed) * 3.6f;
        Vector2 Fwd { get { var f = car.transform.forward; return new Vector2(f.x, f.z).normalized; } }
        float T => Time.realtimeSinceStartup - bornAt;

        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }

        void Update()
        {
            if (!finished && Time.realtimeSinceStartup - bornAt > HangSeconds)
            {
                Check(false, "HUNG: " + HangSeconds + " s");
                Done();
                return;
            }
            if (car == null || hud == null) return;
            string st = StepNow;
            if (st != lastStep)
            {
                Note($"[{T,6:0.0}s] STEP {lastStep} -> {st}  pos ({car.transform.position.x:0.0}, {car.transform.position.z:0.0}) {Kmh:0.0} km/h fuel {(tank != null ? tank.percent : -1):0.0} onfoot {ForecourtMode.OnFoot}");
                lastStep = st;
            }
            string c = hud.centerText != null ? hud.centerText.text : "<none>";
            if (c != lastCenter)
            {
                lastCenter = c;
                Note($"[{T,6:0.0}s]   hud \"{c.Replace("\n", " / ")}\"  FirstDrive.Prompt=\"{(FirstDrive.Prompt ?? "null").Replace("\n", " / ")}\"");
            }
        }

        void Done()
        {
            if (finished) return;
            finished = true;
            FirstDrivePlayCheck.Finish();
            EditorApplication.Exit(FirstDrivePlayCheck.failures == 0 ? 0 : 1);
        }

        IEnumerator Bind()
        {
            float t0 = Time.realtimeSinceStartup;
            while (CityMode.Instance == null && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            for (int i = 0; i < 10; i++) yield return null;
            mode = CityMode.Instance;
            world = mode != null ? mode.world : null;
            map = world != null ? world.Map : null;
            car = mode != null ? mode.player : null;
            hud = FindFirstObjectByType<RaceHUD>();
            mini = FindFirstObjectByType<HudMinimap>();
            fm = ForecourtMode.Instance;
            tank = car != null ? car.GetComponent<FuelTank>() : null;
            fd = mode != null ? mode.GetComponent<FirstDrive>() : null;
        }

        IEnumerator Start()
        {
            yield return Bind();
            Check(fd != null, "CityMode added FirstDrive for RaceHandoff.FirstDrive");
            Check(map != null && car != null && hud != null && fm != null, "world, car, HUD, forecourt");
            if (fd == null || map == null || car == null || hud == null) { Done(); yield break; }
            try { pad = InputSystem.AddDevice<Gamepad>(); }
            catch (Exception e) { Note("no virtual pad: " + e.Message); }
            Check(pad != null, "virtual pad");
            if (pad == null) { Done(); yield break; }
            js = CitySignals.All(map, CityWorld.TrimsFor(map));

            Stats();
            HudLayout();
            if (Environment.GetEnvironmentVariable("PSX_FD_SCEN") == "sample")
            {
                try { GpsArrivalSample(); } catch (Exception e) { Note("  gps sampling threw: " + e.Message); }
                Done();
                yield break;
            }
            yield return MainRun();
            if ((Environment.GetEnvironmentVariable("PSX_FD_SCEN") ?? "all") == "all")
                yield return DeadEnds();
            Done();
        }

        // ---- what the city holds -----------------------------------------
        void Stats()
        {
            var p = Plan;
            int sig = 0, stop = 0, stopAppr = 0, allAppr = 0, allStop = 0;
            float nearSig = 1e9f, nearStop = 1e9f;
            foreach (var j in js)
            {
                if (j.signal) { sig++; nearSig = Mathf.Min(nearSig, (j.centre - p).magnitude); continue; }
                int k = 0; foreach (var a in j.approaches) if (a.ctl == 2) k++;
                if (k == 0) continue;
                stop++; stopAppr += k; allAppr += j.approaches.Count; if (k == j.approaches.Count) allStop++;
                nearStop = Mathf.Min(nearStop, (j.centre - p).magnitude);
            }
            Note($"CITY  start ({p.x:0}, {p.y:0}), uptown ({map.uptown.x:0}, {map.uptown.y:0})");
            Note($"  signals {sig} (nearest {nearSig:0} m); stop junctions {stop} (nearest {nearStop:0} m), {stopAppr}/{allAppr} of their approaches carry the STOP, {allStop} all-way");
            int rows = 0, bays = 0, lots = 0; float nearRow = 1e9f, nearUp = 1e9f;
            foreach (var lot in map.lots)
            {
                if (lot.runFoot == null) continue;
                bool any = false;
                for (int r = 0; r < lot.runFoot.Length; r++)
                {
                    if (lot.runN[r] == 0) continue;
                    any = true; rows++; bays += lot.runN[r];
                    nearRow = Mathf.Min(nearRow, (lot.runFoot[r] - p).magnitude);
                    nearUp = Mathf.Min(nearUp, (lot.runFoot[r] - map.uptown).magnitude);
                }
                if (any) lots++;
            }
            Note($"  lots {map.lots.Length}, with stalls {lots}, rows {rows}, bays {bays}; nearest row {nearRow:0} m from start, {nearUp:0} m from uptown; StallW {CityMeshes.StallW} StallD {CityMeshes.StallD}");
            float nearSt = 1e9f; foreach (var s in map.stations) nearSt = Mathf.Min(nearSt, (s.origin - p).magnitude);
            Note($"  stations {map.stations.Length}, stale {map.stationsStale}, nearest {nearSt:0} m; anywhereInTown {fm.anywhereInTown}; DriveThru.Serves {DriveThru.Serves}");
            // worst case: how far can a road be from each kind of target?
            float worstSig = 0, worstStop = 0, worstRow = 0, worstSt = 0;
            for (int i = 0; i < map.edges.Length; i += Mathf.Max(1, map.edges.Length / 400))
            {
                var q = LineModel.LanePoint(map.edges[i], map.edges[i].length * 0.5f);
                float a = 1e9f, b = 1e9f, c = 1e9f, d = 1e9f;
                foreach (var j in js)
                {
                    float dd = (j.centre - q).sqrMagnitude;
                    if (j.signal) a = Mathf.Min(a, dd);
                    else { bool st = false; foreach (var ap in j.approaches) if (ap.ctl == 2) st = true; if (st) b = Mathf.Min(b, dd); }
                }
                foreach (var lot in map.lots) if (lot.runFoot != null) for (int r = 0; r < lot.runFoot.Length; r++) if (lot.runN[r] > 0) c = Mathf.Min(c, (lot.runFoot[r] - q).sqrMagnitude);
                foreach (var s in map.stations) d = Mathf.Min(d, (s.origin - q).sqrMagnitude);
                worstSig = Mathf.Max(worstSig, a); worstStop = Mathf.Max(worstStop, b); worstRow = Mathf.Max(worstRow, c); worstSt = Mathf.Max(worstSt, d);
            }
            Note($"  worst nearest over sampled roads (crow): signal {Mathf.Sqrt(worstSig):0} m, stop {Mathf.Sqrt(worstStop):0} m, bay row {Mathf.Sqrt(worstRow):0} m, station {Mathf.Sqrt(worstSt):0} m");
        }

        void HudLayout()
        {
            var c = hud.centerText;
            Note($"HUD  centerText anchor {c.rectTransform.anchorMin} pos {c.rectTransform.anchoredPosition} size {c.rectTransform.rect.size} font {c.fontSize} align {c.alignment} overflow {c.horizontalOverflow}/{c.verticalOverflow}");
            foreach (var kv in new (string, Text)[] { ("lastLap", hud.lastLapText), ("pos", hud.posText), ("lap", hud.lapText), ("time", hud.timeText) })
                if (kv.Item2 != null)
                    Note($"  {kv.Item1} anchor {kv.Item2.rectTransform.anchorMin} pos {kv.Item2.rectTransform.anchoredPosition} size {kv.Item2.rectTransform.rect.size} font {kv.Item2.fontSize}");
            string[] lines =
            {
                "YOUR FIRST DRIVE\nRIGHT TRIGGER TO GO, LEFT TRIGGER TO SLOW AND STOP",
                "STOP AT THE STOP SIGN\nCOME TO A FULL STOP BEHIND THE LINE",
                "FOLLOW THE LINE ON THE MAP\nTO QUIT THE LESSON, OPEN THE MENU AND PICK END DRIVE",
                "YOU DID NOT PUMP ANY GAS\nGET BACK OUT AT THE PUMP AND FILL IT",
                "WATCH THE TEMP GAUGE. IF TEMP BLINKS RED THE ENGINE IS OVERHEATING\nEASE OFF, OR IT WILL LET GO",
                "A COOLANT LEAK MAKES AN ENGINE OVERHEAT\nCHECK COOLANT UNDER MY CARS BEFORE A DRIVE",
                "PARKED. BUMPS AND HITS WEAR THE CAR, AND WORN PARTS FAIL\nDELMAR AUTO FIXES THEM",
                "DRIVE TO THE GAS STATION AND STOP AT A PUMP\nTHEN GET OUT AND PUMP GAS",
                "OUT OF FUEL, THE CAR STOPS\nTHE PAUSE MENU CAN CALL A FUEL TRUCK, BUT IT COSTS MORE",
                "THAT IS THE BASICS. THE ROAD IS YOURS\nTO GO HOME, OPEN THE MENU AND PICK END DRIVE",
            };
            foreach (var s in lines)
            {
                var g = new TextGenerator();
                var gs = c.GetGenerationSettings(Vector2.zero);
                float w = g.GetPreferredWidth(s, gs) / c.pixelsPerUnit;
                float h = g.GetPreferredHeight(s, gs) / c.pixelsPerUnit;
                bool fits = w <= 640f && h <= 100f;
                Check(fits, $"fits the banner: {w:0} of 640 wide, {h:0} of 100 high, font {c.fontSize}, \"{s.Replace("\n", " / ")}\"");
            }
        }

        // ---- the target, measured --------------------------------------
        void TargetReport(string when)
        {
            if (!FirstDrive.HasTarget) { Note($"  target @{when}: NONE"); return; }
            var t = FirstDrive.Target; var p = Plan;
            float d = (t - p).magnitude;
            float bearing = Vector2.SignedAngle(t - p, Fwd);
            float road = map.NearestRoadPoint(t, 400f, false, out int ei, out float s, out float dr) ? dr : -1f;
            string gps = "no route";
            if (mini != null && mini.GpsRemaining(out float m))
            {
                gps = "route " + m.ToString("0") + " m";
                var g = typeof(HudMinimap).GetField("gps", Any)?.GetValue(mini);
                var pts = g?.GetType().GetField("pts", Any)?.GetValue(g) as List<Vector2>;
                if (pts != null && pts.Count > 1)
                    gps += ", starts " + (pts[0] - p).magnitude.ToString("0") + " m from the car, ends " + (pts[pts.Count - 1] - t).magnitude.ToString("0") + " m from the target (" + pts.Count + " pts)";
            }
            string slot = hud.lastLapText != null ? hud.lastLapText.text : "";
            Note($"  target @{when}: {FirstDrive.TargetLabel} ({t.x:0.0}, {t.y:0.0}), {d:0} m crow, bearing {bearing:0} deg off the nose, road {(road < 0 ? "none in 400" : road.ToString("0.0") + " m")} ({(ei >= 0 && ei < map.edges.Length ? map.edges[ei].name : "?")}), {gps}, HUD slot \"{slot}\"");
        }

        // ---- the pad -----------------------------------------------------
        void Pad(float throttle, float brake, float steer, GamepadButton? b = null)
        {
            var st = new GamepadState { rightTrigger = throttle, leftTrigger = brake, leftStick = new Vector2(steer, 0f) };
            if (b.HasValue) st = st.WithButton(b.Value);
            InputSystem.QueueStateEvent(pad, st);
        }

        float SteerAt(Vector2 aim)
        {
            var to = aim - Plan;
            float a = Vector3.SignedAngle(new Vector3(Fwd.x, 0, Fwd.y), new Vector3(to.x, 0, to.y), Vector3.up);
            return Mathf.Clamp(a / 22f, -1f, 1f);
        }

        IEnumerator Press(GamepadButton b)
        {
            Pad(0, 0, 0, b); yield return null; yield return null;
            Pad(0, 0, 0); yield return null; yield return null;
        }

        static IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

        IEnumerator Settle(int n = 40)
        {
            for (int i = 0; i < n; i++) { Idle(); yield return new WaitForFixedUpdate(); }
            Pad(0, 0, 0);
        }

        bool Ground(Vector2 xz, float hintY, out RaycastHit hit)
        {
            return Physics.Raycast(new Vector3(xz.x, hintY + 6f, xz.y), Vector3.down, out hit, 25f, Mask, QueryTriggerInteraction.Ignore);
        }

        bool Put(Vector2 xz, float hintY, Vector2 dir)
        {
            var p = new Vector3(xz.x, hintY, xz.y);
            world.EnsureRing(p, 1);
            Physics.SyncTransforms();
            if (!Ground(xz, hintY, out var h)) return false;
            car.TeleportTo(h.point + Vector3.up * 0.5f, Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up));
            return true;
        }

        // Drive along the line through `at` in direction `u`; stop `stopBefore` short of it,
        // or (stop false) hold vKmh through it. out via lastDrive.
        string lastDrive = "";
        bool driveOk;
        IEnumerator Drive(Vector2 at, Vector2 u, float vKmh, float stopBefore, bool stop, float timeout = 30f, Func<bool> quit = null)
        {
            driveOk = false;
            float t0 = Time.time, stillFor = 0f, stuckFor = 0f;
            while (Time.time - t0 < timeout)
            {
                if (quit != null && quit()) { driveOk = true; lastDrive = "quit: " + StepNow; Pad(0, 0, 0); yield break; }
                var rel = at - Plan;
                float along = Vector2.Dot(rel, u);
                float d = along - stopBefore;
                float vt = stop ? Mathf.Min(vKmh, 3.6f * Mathf.Sqrt(2f * 2.2f * Mathf.Max(d - 0.8f, 0f))) : vKmh;
                float k = Kmh;
                float th = k < vt - 1f ? 0.65f : 0f;
                float br = k > vt + 1.5f ? Mathf.Clamp01((k - vt) / 8f) : 0f;
                if (stop && d < 0.8f) { th = 0f; br = brakeHeld ? 1f : k > 0.5f ? 0.7f : 0f; }
                float look = Mathf.Max(8f, k * 0.5f);
                var aim = at - u * (along - look);
                Pad(th, br, SteerAt(aim));
                if (stop && d < 2f && k < 1f) { stillFor += Time.deltaTime; if (stillFor > 0.4f) break; } else stillFor = 0f;
                stuckFor = (th > 0f && k < 1.5f) ? stuckFor + Time.deltaTime : 0f;
                if (stuckFor > 4f) { lastDrive = $"BLOCKED {d:0.0} m short at ({Plan.x:0.0},{Plan.y:0.0})"; Pad(0, 0, 0); yield break; }
                if (!stop && along < -14f) { driveOk = true; lastDrive = "through"; Pad(0, 0, 0); yield break; }
                yield return null;
            }
            Pad(0, 0, 0);
            float dd = Vector2.Dot(at - Plan, u);
            driveOk = stop ? (dd < stopBefore + 2.5f && Kmh < 1.5f) : false;
            lastDrive = (driveOk ? "stopped " : "TIMEOUT ") + dd.ToString("0.0") + " m short of the line, " + Kmh.ToString("0.0") + " km/h, off line " + Mathf.Abs(Vector2.Dot(at - Plan, new Vector2(u.y, -u.x))).ToString("0.0") + " m";
        }

        // ---- approaches to use -------------------------------------------
        static Vector2 LaneMid(CitySignals.Approach a) => a.at + a.Right * ((a.inL + a.inR) * 0.5f);

        bool ClearRun(CitySignals.Approach a, float runUp)
        {
            var lane = LaneMid(a);
            var p = new Vector3(lane.x, a.roadY, lane.y);
            world.EnsureRing(p, 1);
            Physics.SyncTransforms();
            for (float b = 0f; b <= runUp; b += 6f)
            {
                var q = lane - a.u * b;
                if (!Ground(q, a.roadY, out var h) || Mathf.Abs(h.point.y - a.roadY) > 1.2f) return false;
                if (Physics.CheckBox(h.point + Vector3.up * 1.0f, new Vector3(1.1f, 0.6f, 2.4f),
                        Quaternion.LookRotation(new Vector3(a.u.x, 0, a.u.y)), Mask, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        List<(CitySignals.Junction j, CitySignals.Approach a)> Near(bool signal, Vector2 from, int want, float runUp, int skip = 0)
        {
            var all = new List<(CitySignals.Junction, CitySignals.Approach, float)>();
            foreach (var j in js)
            {
                if (j.signal != signal) continue;
                foreach (var a in j.approaches)
                {
                    if (!signal && a.ctl != 2) continue;
                    all.Add((j, a, (a.at - from).sqrMagnitude));
                }
            }
            all.Sort((x, y) => x.Item3.CompareTo(y.Item3));
            var res = new List<(CitySignals.Junction, CitySignals.Approach)>();
            int tries = 0;
            foreach (var x in all)
            {
                if (tries++ > 40 || res.Count >= want + skip) break;
                if (ClearRun(x.Item2, runUp)) res.Add((x.Item1, x.Item2));
            }
            if (skip > 0 && res.Count > skip) res.RemoveRange(0, skip);
            return res;
        }

        void FrameReport(string what, CitySignals.Approach a)
        {
            float d = map.NearestRoadPoint(a.at, 100f, false, out int ei, out float s, out float dr) ? dr : -1f;
            var q = LineModel.LanePoint(map.edges[a.edge], a.sStop);
            var edgePt = map.edges[a.edge];
            bool gr = Ground(a.at, a.roadY, out var h);
            Note($"  frame {what}: a.at ({a.at.x:0.0}, {a.at.y:0.0}) u ({a.u.x:0.00}, {a.u.y:0.00}) edge {a.edge} \"{edgePt.name}\" sStop {a.sStop:0.0} of {edgePt.length:0}, lanes {a.inL:0.0}..{a.inR:0.0}; LanePoint(edge,sStop) is {(q - a.at).magnitude:0.0} m from a.at; road {d:0.0} m; ground {(gr ? h.collider.name + " y " + h.point.y.ToString("0.00") + " vs roadY " + a.roadY.ToString("0.00") : "NONE")}");
        }

        IEnumerator WaitStep(string step, float seconds)
        {
            float t0 = Time.time;
            while (StepNow != step && Time.time - t0 < seconds) yield return null;
        }

        // ---- the run -----------------------------------------------------
        IEnumerator MainRun()
        {
            Note("MAIN RUN");
            yield return WaitStep("Go", 40f);
            Check(StepNow == "Go", "Wait -> Go when the session is Live", "live " + mode.Live);
            yield return Fixed(10);
            yield return null;
            Note($"  on Go, hud \"{hud.centerText.text.Replace("\n", " / ")}\" canvas on {hud.centerText.isActiveAndEnabled}, input {car.GetComponent<PlayerCarInput>().inputEnabled}");
            Shot("hud_go", 1280, 720, false);

            yield return Pedals();
            Check(StepNow == "StopSign", "Go -> StopSign on the pedal", Kmh.ToString("0") + " km/h");

            yield return StopSignStep();
            Check(StepNow == "Light", "StopSign -> Light on a real stop");
            if (StepNow != "Light") yield break;

            yield return LightStep();
            Check(StepNow == "Park", "Light -> LightWait -> Park on a real red");
            if (StepNow != "Park") yield break;

            yield return ParkStep();
            Check(StepNow == "Pump", "Park -> Pump in a real bay");
            if (StepNow != "Pump") yield break;

            yield return PumpStep(fill: true);
            Check(StepNow == "Done", "Pump -> Done on foot at a pump and back");
            if (StepNow != "Done") yield break;

            // Done clears everything
            Note($"  on Done: hud \"{hud.centerText.text.Replace("\n", " / ")}\" Active {FirstDrive.Active} HasTarget {FirstDrive.HasTarget}");
            float t0 = Time.time;
            while (FirstDrive.Active && Time.time - t0 < 20f) yield return null;
            yield return Fixed(30);
            yield return null;
            Check(!FirstDrive.Active && FirstDrive.Prompt == null && !FirstDrive.HasTarget, "Done clears the statics", (Time.time - t0).ToString("0.0") + " s");
            Check(hud.centerText.text != "" ? !hud.centerText.text.Contains("BASICS") : true, "the banner has gone", hud.centerText.text);
            Note($"  after Done: slot \"{hud.lastLapText.text}\" RaceHandoff.FirstDrive still {RaceHandoff.FirstDrive}, component enabled {fd.enabled}");
            bool gps = mini != null && mini.GpsRemaining(out float m0);
            Note($"  GPS after Done: {gps}, Serves {DriveThru.Serves}");
            float v0 = Kmh;
            float t1 = Time.time;
            while (Kmh < 15f && Time.time - t1 < 12f) { Pad(0.6f, 0, 0); yield return null; }
            Pad(0, 0, 0);
            Check(Kmh >= 15f || Time.time - t1 >= 12f && Kmh > 5f, "free drive goes on: the pad drives the car", v0.ToString("0") + " -> " + Kmh.ToString("0") + " km/h");
            yield return Settle();
        }

        IEnumerator Pedals()
        {
            float t0 = Time.time;
            while (StepNow == "Go" && Time.time - t0 < 30f)
            {
                float steer = 0f;
                if (map.NearestRoadPoint(Plan, 60f, false, out int ei, out float s, out float _))
                {
                    var e = map.edges[ei];
                    var tan = e.TangentAt(s);
                    float sign = Vector2.Dot(tan, Fwd) >= 0f ? 1f : -1f;
                    steer = SteerAt(LineModel.LanePoint(e, Mathf.Clamp(s + sign * 14f, 0f, e.length)));
                }
                Pad(0.8f, 0, steer);
                yield return null;
            }
            Pad(0, 0, 0);
            Note($"  pedals: Go left after {Time.time - t0:0.0} s at {Kmh:0} km/h");
            yield return null;
            yield return Fixed(5);
            TargetReport("StopSign, just entered");
            Note($"  lesson card on screen: \"{hud.centerText.text.Replace("\n", " / ")}\"");
            Shot("hud_stop_lesson_phone_wide", 1998, 891, false);
            Shot("hud_stop_lesson_16x9", 1280, 720, false);
            Shot("hud_stop_lesson_4x3", 1024, 768, false);
        }

        IEnumerator StopSignStep()
        {
            yield return Settle(20);
            float tw = Time.time;
            while (!FirstDrive.HasTarget && Time.time - tw < 5f) yield return null;
            yield return new WaitForSeconds(2.5f);
            TargetReport("StopSign, 2.5 s on");
            GpsArrival();
            var list = Near(false, Plan, 3, 50f);
            Note($"  stop approaches with a clear 50 m run-up near here: {list.Count}");
            if (list.Count == 0) yield break;
            var (j, a) = list[0];
            FrameReport("stop sign", a);
            Note($"  junction centre ({j.centre.x:0.0}, {j.centre.y:0.0}), {(j.centre - a.at).magnitude:0.0} m from the line, approaches {j.approaches.Count}");
            // roll through it
            var lane = LaneMid(a);
            Put(lane - a.u * 50f, a.roadY, a.u);
            yield return Settle(15);
            yield return Drive(lane, a.u, 40f, 0f, false);
            Note($"  rolled through: {lastDrive}; step {StepNow}, praise \"{(FirstDrive.Prompt ?? "").Replace("\n", " / ")}\"");
            Check(StepNow == "StopSign", "rolling through does not pass the step");
            // now stop at it
            Put(lane - a.u * 50f, a.roadY, a.u);
            yield return Settle(15);
            yield return Drive(lane, a.u, 35f, 3f, true, 30f, () => StepNow != "StopSign");
            Note($"  stopped: {lastDrive}, d to line {Vector2.Dot(a.at - Plan, a.u):0.0} m; hud \"{hud.centerText.text.Replace("\n", " / ")}\"");
            yield return Hold(1.5f, () => StepNow != "StopSign");
            Note($"  held: step {StepNow}");
        }

        IEnumerator Hold(float seconds, Func<bool> until = null)
        {
            float t0 = Time.time;
            while (Time.time - t0 < seconds && (until == null || !until()))
            {
                Idle();
                yield return null;
            }
        }

        // brakeHeld: the driver keeps the pedal down at a stop; otherwise off it once stopped.
        bool brakeHeld;
        void Idle() { Pad(0, brakeHeld ? 1f : Kmh > 0.5f ? 0.6f : 0f, 0); }

        void GpsArrival()
        {
            var g = mini != null ? typeof(HudMinimap).GetField("gps", Any)?.GetValue(mini) : null;
            var pts = g?.GetType().GetField("pts", Any)?.GetValue(g) as List<Vector2>;
            if (pts == null || pts.Count < 2 || !FirstDrive.HasTarget) { Note("  gps arrival: no route"); return; }
            var last = (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized;
            CitySignals.Junction tj = null;
            foreach (var j in js) if ((j.centre - FirstDrive.Target).sqrMagnitude < 1f) tj = j;
            if (tj == null) { Note("  gps arrival: target is no junction centre"); return; }
            string how = "no approach along the route's last leg";
            foreach (var a in tj.approaches)
                if (Vector2.Dot(a.u, last) > 0.7f) how = "arrives on \"" + map.edges[a.edge].name + "\" ctl " + a.ctl + " (2 = STOP)";
            Note($"  gps arrival at the target junction: {how}; junction has {tj.approaches.Count} approaches");
        }

        IEnumerator LightStep()
        {
            yield return WaitStep("Light", 5f);
            yield return new WaitForSeconds(0.5f);
            Shot("hud_light_lesson", 1280, 720, false);
            TargetReport("Light");
            var list = Near(true, Plan, 3, 50f);
            Note($"  signal approaches with a clear 50 m run-up near here: {list.Count}");
            if (list.Count == 0) yield break;
            var (j, a) = list[0];
            FrameReport("signal", a);
            yield return RedStop(a);
            Note($"  stopped at the red: {lastDrive}, aspect {CitySignals.AspectNow(a)}, step {StepNow}; hud \"{hud.centerText.text.Replace("\n", " / ")}\"");
            yield return WaitStep("LightWait", 4f);
            Check(StepNow == "LightWait", "stopped on red -> LightWait", CitySignals.AspectNow(a));
            float t0 = Time.time;
            while (CitySignals.AspectNow(a) != CitySignals.Aspect.Green && Time.time - t0 < 180f) { Idle(); yield return null; }
            Note($"  green after {Time.time - t0:0.0} s; pulling away");
            var lane = LaneMid(a);
            yield return Drive(lane, a.u, 30f, 0f, false, 20f, () => StepNow != "LightWait");
            Note($"  pulled away: step {StepNow}, {Kmh:0} km/h");
            yield return Settle(20);
        }

        IEnumerator RedStop(CitySignals.Approach a)
        {
            var lane = LaneMid(a);
            Put(lane - a.u * 40f, a.roadY, a.u);
            yield return Settle(15);
            // wait where a stop does not count, until it has just gone red
            float t0 = Time.time;
            var prev = CitySignals.AspectNow(a);
            while (Time.time - t0 < 180f)
            {
                var now = CitySignals.AspectNow(a);
                if (now == CitySignals.Aspect.Red && prev != CitySignals.Aspect.Red) break;
                prev = now;
                Idle();
                yield return null;
            }
            Note($"  waited {Time.time - t0:0.0} s 40 m back for red");
            yield return Drive(lane, a.u, 30f, 3f, true, 25f, () => StepNow != "Light");
            yield return Hold(1.5f, () => StepNow != "Light");
        }

        // ---- the bay -----------------------------------------------------
        CityMap.Lot targetLot; int targetRow;

        bool RowAt(Vector2 t)
        {
            float best = 4f;
            bool ok = false;
            foreach (var lot in map.lots)
            {
                if (lot.runFoot == null) continue;
                for (int r = 0; r < lot.runFoot.Length; r++)
                {
                    if (lot.runN[r] == 0) continue;
                    float d = (lot.runFoot[r] - t).sqrMagnitude;
                    if (d < best) { best = d; targetLot = lot; targetRow = r; ok = true; }
                }
            }
            return ok;
        }

        IEnumerator ParkStep()
        {
            yield return WaitStep("Park", 3f);
            yield return new WaitForSeconds(2.2f);
            TargetReport("Park");
            if (!RowAt(FirstDrive.Target)) { Check(false, "the target is a row's foot"); yield break; }
            yield return ParkIn(targetLot, targetRow, report: true);
        }

        /// <summary>A free bay in this row, centre and way in.</summary>
        bool FreeBay(CityMap.Lot lot, int r, float y, out Vector2 c, out Vector2 inward, out string about)
        {
            var u = lot.runU != null ? lot.runU[r] : lot.u;
            var v = new Vector2(-u.y, u.x);
            c = default; inward = default; about = "";
            int free = 0, blocked = 0; string firstHit = null;
            bool got = false;
            for (int k = 0; k < lot.runN[r]; k++)
            {
                var b = lot.runFoot[r] + u * ((k + 0.5f) * CityMeshes.StallW) + v * (CityMeshes.StallD * 0.5f);
                if (!Ground(b, y, out var h)) { blocked++; continue; }
                var q = Quaternion.LookRotation(new Vector3(v.x, 0, v.y));
                if (Physics.CheckBox(h.point + Vector3.up * 1.0f, new Vector3(0.95f, 0.5f, 2.2f), q, Mask, QueryTriggerInteraction.Ignore))
                {
                    blocked++;
                    if (firstHit == null)
                    {
                        var cs = Physics.OverlapBox(h.point + Vector3.up * 1.0f, new Vector3(0.95f, 0.5f, 2.2f), q, Mask, QueryTriggerInteraction.Ignore);
                        firstHit = cs.Length > 0 ? cs[0].name : "?";
                    }
                    continue;
                }
                free++;
                if (got) continue;
                // the way in: whichever end has clear ground for a run-up
                foreach (float s in new[] { -1f, 1f })
                {
                    var mouth = b + v * (s * (CityMeshes.StallD * 0.5f + 5f));
                    if (!Ground(mouth, y, out var mh) || Mathf.Abs(mh.point.y - h.point.y) > 0.6f) continue;
                    if (Physics.CheckBox(mh.point + Vector3.up * 1.0f, new Vector3(1.0f, 0.5f, 2.3f), q, Mask, QueryTriggerInteraction.Ignore)) continue;
                    c = b; inward = -v * s; got = true; break;
                }
            }
            about = $"row {r}: {lot.runN[r]} bays, {free} free, {blocked} blocked{(firstHit != null ? " (first by " + firstHit + ")" : "")}, row dir ({u.x:0.00},{u.y:0.00})";
            return got;
        }

        IEnumerator ParkIn(CityMap.Lot lot, int r, bool report)
        {
            var foot = lot.runFoot[r];
            float roadD = map.NearestRoadPoint(foot, 300f, false, out int ei, out float s, out float dr) ? dr : -1f;
            float y0 = 0f;
            Vector2 roadPt = foot;
            if (roadD >= 0f) { y0 = map.edges[ei].YAt(s); roadPt = LineModel.LanePoint(map.edges[ei], s); }
            Put(foot, y0, Vector2.up);          // stands the lot's tile
            Ground(foot, y0, out var gh);
            float lotY = gh.collider != null ? gh.point.y : y0;
            bool got = FreeBay(lot, r, lotY, out var c, out var inward, out var about);
            if (report)
            {
                Note($"  bay: {about}; road {roadD:0.0} m from the row's foot ({(roadD >= 0 ? map.edges[ei].name : "?")}), ground at foot {(gh.collider != null ? gh.collider.name : "NONE")} y {lotY:0.00} vs road y {y0:0.00}");
                Note($"  lot: entries? ring {lot.ring?.Length} pts, runs {lot.runFoot.Length}");
            }
            if (!got) { Check(false, "a free bay with a way in in the target row", about); yield break; }
            // 1) from the road, straight at the bay, as a player would
            if (report && roadD >= 0f)
            {
                var from = roadPt;
                var dir = (c - from).normalized;
                Put(from, y0, dir);
                yield return Settle(15);
                yield return Drive(c, dir, 18f, 0f, true, 35f, () => StepNow != "Park");
                Note($"  from the road ({(c - from).magnitude:0} m, straight): {lastDrive}; at ({Plan.x:0.0},{Plan.y:0.0}), step {StepNow}");
                yield return Hold(2f, () => StepNow != "Park");
                if (StepNow != "Park") { Note("  parked from the road"); Top("bay_top", c, 22f); yield break; }
            }
            // 2) lined up at the bay's mouth, driven in
            Put(c - inward * 12f, lotY, inward);
            yield return Settle(15);
            yield return Drive(c, inward, 12f, 0f, true, 25f, () => StepNow != "Park");
            Note($"  from the mouth: {lastDrive}; off bay centre {(Plan - c).magnitude:0.0} m, step {StepNow}");
            yield return Hold(2.5f, () => StepNow != "Park");
            if (StepNow == "Park")
            {
                // 3) set down in it: does the test pass a parked car at all?
                Put(c, lotY, inward);
                yield return Settle(25);
                yield return Hold(2.5f, () => StepNow != "Park");
                Note($"  set down in the bay: off centre {(Plan - c).magnitude:0.0} m, InBay {typeof(FirstDrive).GetMethod("InBay", Any).Invoke(fd, null)}, step {StepNow}");
            }
            if (report) Top("bay_top", c, 22f);
        }

        // ---- the pump ----------------------------------------------------
        bool ParkAtPump(Vector2 near, out GasPump pump, out CityStations.Station st, bool report)
        {
            pump = null; st = null;
            float best = 1e12f;
            foreach (var s in map.stations) { float d = (s.origin - near).sqrMagnitude; if (d < best) { best = d; st = s; } }
            if (st == null) return false;
            var lot = new Vector3(st.centre.x, 0f, st.centre.y);
            world.EnsureRing(lot, 1);
            Physics.SyncTransforms();
            var canopy = st.World(0f, CityStations.CanV0 + 0.5f * CityStations.CanD);
            float pd = 1e9f; int nearN = 0;
            foreach (var p in GasPump.All)
            {
                if (p == null) continue;
                float d = (new Vector2(p.transform.position.x, p.transform.position.z) - canopy).magnitude;
                if (d < 20f) nearN++;
                if (d < pd) { pd = d; pump = p; }
            }
            if (report)
                Note($"  station {st.index} \"{st.OsmName}\" origin ({st.origin.x:0.0},{st.origin.y:0.0}) centre ({st.centre.x:0.0},{st.centre.y:0.0}) cls {st.cls} layout {st.layout} modules {st.modules} flags {st.flags}; pumps within 20 m of the canopy {nearN}, nearest {pd:0.0} m; origin to nearest pump {(pump != null ? (new Vector2(pump.transform.position.x, pump.transform.position.z) - st.origin).magnitude.ToString("0.0") : "?")} m");
            if (pump == null || pd > 14f) return false;
            foreach (float d in new[] { 2.6f, 3.0f, 3.4f })
                foreach (var dir in new[] { pump.transform.forward, -pump.transform.forward, pump.transform.right, -pump.transform.right })
                {
                    Vector3 p = pump.transform.position + dir * d;
                    if (!Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var gh, 20f, Mask, QueryTriggerInteraction.Ignore)) continue;
                    Quaternion q = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
                    if (Physics.CheckBox(gh.point + Vector3.up * 0.9f, new Vector3(1f, 0.6f, 2.3f), q, Mask, QueryTriggerInteraction.Ignore)) continue;
                    car.TeleportTo(gh.point + Vector3.up * 0.4f, q);
                    return true;
                }
            return false;
        }

        IEnumerator PumpStep(bool fill)
        {
            yield return WaitStep("Pump", 3f);
            yield return new WaitForSeconds(4.5f);
            TargetReport("Pump");
            var aim = FirstDrive.Target;
            bool ok = ParkAtPump(aim, out var pump, out var st, true);
            Check(ok, "beside a pump of the station the line points at");
            if (!ok) yield break;
            yield return Settle(50);
            yield return Fixed(10);
            yield return null;
            Note($"  at the pump: AtPump {GasPump.AtPump} OfferFuel {ForecourtMode.OfferFuel} forecourt \"{ForecourtMode.Prompt}\" gas \"{GasPump.Prompt}\" hud \"{hud.centerText.text.Replace("\n", " / ")}\" tank {tank.percent:0.0}");
            Check(ForecourtMode.OfferFuel, "the FUEL button is offered");
            // nothing done yet: the step must wait
            Check(StepNow == "Pump", "stopped at the pump alone does not end the step");
            yield return Press(GamepadButton.South);
            float t0 = Time.time;
            while (!ForecourtMode.OnFoot && Time.time - t0 < 6f) yield return null;
            Check(ForecourtMode.OnFoot, "pad south gets out");
            yield return Fixed(5);
            yield return null;
            Note($"  on foot: hud \"{hud.centerText.text.Replace("\n", " / ")}\" ({(hud.centerText.isActiveAndEnabled ? "drawn" : "hidden")}), slot \"{hud.lastLapText.text}\", step {StepNow}, HasTarget {FirstDrive.HasTarget}");
            Shot("hud_onfoot", 1280, 720, false);
            var walker = GameObject.Find("Walker");
            if (walker == null) { Check(false, "a walker"); yield break; }
            if (fill)
            {
                Transform claimed = pump.transform; float nb = 1e9f;
                foreach (var gp in GasPump.All)
                {
                    if (gp == null) continue;
                    float dd = (gp.transform.position - walker.transform.position).sqrMagnitude;
                    if (dd < nb) { nb = dd; claimed = gp.transform; }
                }
                yield return WalkTo(walker, claimed.position, 1.5f);
                var it = walker.GetComponent<FootInteractor>();
                yield return Fixed(10);
                yield return null;
                var cur = it != null ? it.Current : null;
                Check(cur != null && cur.action == "FILL UP", "at the pump it says FILL UP", cur != null ? cur.name + "/" + cur.action : "nothing");
                float pct0 = tank.percent;
                if (cur != null && cur.onUse != null)
                {
                    cur.onUse();
                    yield return new WaitForSecondsRealtime(2.5f);
                    Check(tank.percent > pct0, "FILL UP fills", pct0.ToString("0.0") + " -> " + tank.percent.ToString("0.0"));
                    cur.onUse();
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                Check(StepNow == "Pump", "pumping gas alone does not end the step (it ends on getting back in)");
            }
            // back into the car
            yield return WalkTo(walker, car.transform.position, 2.4f);
            var inter = walker.GetComponent<FootInteractor>();
            yield return Fixed(10);
            yield return null;
            var c = inter != null ? inter.Current : null;
            Note($"  by the car: current {(c != null ? c.name + "/" + c.action : "nothing")}");
            if (c != null && c.onUse != null && c.name == "CarTarget") c.onUse();
            else
            {
                var m = typeof(ForecourtMode).GetMethod("GetIn", Any);
                fm.StartCoroutine((IEnumerator)m.Invoke(fm, null));
                Note("  (GetIn called: the walker did not face the car)");
            }
            t0 = Time.time;
            while (ForecourtMode.OnFoot && Time.time - t0 < 8f) yield return null;
            Check(!ForecourtMode.OnFoot, "back in the car");
            yield return WaitStep("Done", 3f);
        }

        IEnumerator WalkTo(GameObject walker, Vector3 goal, float near)
        {
            var wcc = walker.GetComponent<CharacterController>();
            for (int i = 0; i < 200; i++)
            {
                Vector3 to = goal - walker.transform.position; to.y = 0f;
                if (to.magnitude < near) break;
                wcc?.Move(to.normalized * 0.06f + Vector3.down * 0.05f);
                yield return new WaitForFixedUpdate();
            }
            Vector3 look = goal - walker.transform.position; look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) walker.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            walker.GetComponent<FirstPersonWalk>()?.SnapYawToTransform();
        }

        // ---- dead ends ---------------------------------------------------
        IEnumerator Fresh()
        {
            if (fd != null) Destroy(fd);
            yield return null;
            fd = mode.gameObject.AddComponent<FirstDrive>();
            lastStep = "";
            yield return WaitStep("Go", 5f);
            float t0 = Time.time;
            while (StepNow == "Go" && Time.time - t0 < 25f) { Pad(0.8f, 0, 0); yield return null; }
            Pad(0, 0, 0);
            yield return Settle(20);
        }

        IEnumerator PassStop()
        {
            var list = Near(false, Plan, 1, 30f);
            if (list.Count == 0) { Check(false, "a stop line to use"); yield break; }
            var a = list[0].a;
            var lane = LaneMid(a);
            Put(lane - a.u * 30f, a.roadY, a.u);
            yield return Settle(15);
            yield return Drive(lane, a.u, 25f, 3f, true, 20f, () => StepNow != "StopSign");
            yield return Hold(1.5f, () => StepNow != "StopSign");
        }

        IEnumerator DeadEnds()
        {
            Note("DEAD ENDS");
            // ---- 0. the pedal kept down ----
            Note(" 0. the driver keeps the brake down at each stop");
            yield return BrakeHeld();
            try { GpsArrivalSample(); } catch (Exception e) { Note("  gps sampling threw: " + e.Message); }
            // ---- 1. the driver goes the other way ----
            Note(" 1. driving away from the target");
            yield return Fresh();
            yield return new WaitForSeconds(2.5f);
            TargetReport("before");
            var before = FirstDrive.Target;
            var edge = map.edges[map.edges.Length / 3];
            var farPt = LineModel.LanePoint(edge, edge.length * 0.5f);
            Put(farPt, edge.YAt(edge.length * 0.5f), edge.TangentAt(edge.length * 0.5f));
            yield return Settle(15);
            yield return new WaitForSeconds(2.5f);
            TargetReport("after a jump to " + edge.name);
            Check(FirstDrive.HasTarget && FirstDrive.Target != before, "the target follows the car");

            // ---- 2. reset car, mid-approach ----
            Note(" 2. RESET CAR while a stop line is in hand");
            var nearStops = Near(false, Plan, 1, 30f);
            if (nearStops.Count > 0)
            {
                var a = nearStops[0].a;
                var lane = LaneMid(a);
                Put(lane - a.u * 22f, a.roadY, a.u);
                yield return Settle(15);
                var pm = FindFirstObjectByType<PauseMenu>();
                Vector2 was = Plan;
                if (pm != null) typeof(PauseMenu).GetMethod("ResetCar", Any).Invoke(pm, null);
                else DriveSession.Respawn(car);
                yield return Fixed(40);
                Note($"  reset moved the car {(Plan - was).magnitude:0.0} m; step {StepNow}, haveLine {typeof(FirstDrive).GetField("haveLine", Any).GetValue(fd)}, timeScale {Time.timeScale}, pad drives? hud \"{hud.centerText.text.Replace("\n", " / ")}\"");
                yield return PassStop();
                Check(StepNow == "Light", "after a reset the stop still counts", StepNow);
            }
            // ---- 3. only greens ----
            Note(" 3. three greens in a row");
            yield return ThreeGreens();
            // ---- 4. a dry tank ----
            Note(" 4. a dry tank mid-tutorial");
            yield return Fresh();
            var money0 = Edition.HasCareer ? LifeSimManager.State.money : 0;
            tank.percent = 0f;
            yield return Fixed(10);
            yield return null;
            Note($"  tank 0: hud \"{hud.centerText.text.Replace("\n", " / ")}\" (FirstDrive \"{(FirstDrive.Prompt ?? "").Replace("\n", " / ")}\")");
            Check(hud.centerText.text.Contains("OUT OF FUEL"), "a dry tank says so, and how to get out of it", hud.centerText.text);
            float vDry = 0f; float td = Time.time;
            while (Time.time - td < 2f) { Pad(1f, 0, 0); vDry = Mathf.Max(vDry, Kmh); yield return null; }
            Pad(0, 0, 0);
            Note($"  throttle dry: {vDry:0.0} km/h");
            var pm2 = FindFirstObjectByType<PauseMenu>();
            string label = pm2 != null ? (string)typeof(PauseMenu).GetMethod("FuelLabel", Any).Invoke(pm2, null) : "no menu";
            Note($"  pause menu row: \"{label}\", money {money0}");
            if (pm2 != null) typeof(PauseMenu).GetMethod("CallFuelTruck", Any).Invoke(pm2, null);
            yield return Fixed(5);
            Note($"  after the truck: tank {tank.percent:0}, step {StepNow}");
            yield return PumpCases();
            // ---- 7. RESTART RACE ----
            Note(" 7. RESTART RACE row, then END DRIVE, then Charlotte again");
            if (StepNow == "Done")
            {
                float t0 = Time.time;
                while (FirstDrive.Active && Time.time - t0 < 16f) yield return null;
            }
            Note($"  before restart: Active {FirstDrive.Active}, handoff.FirstDrive {RaceHandoff.FirstDrive}");
            var pm3 = FindFirstObjectByType<PauseMenu>();
            if (pm3 != null)
            {
                typeof(PauseMenu).GetMethod("RestartRace", Any).Invoke(pm3, null);
                float tr = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(3f);
                while ((CityMode.Instance == null || CityMode.Instance.world == null) && Time.realtimeSinceStartup - tr < 120f) yield return null;
                yield return Bind();
                Time.timeScale = 1f;
                yield return new WaitForSecondsRealtime(4f);
                Note($"  after RESTART: FirstDrive.Active {FirstDrive.Active}, step {StepNow}, tank {(tank != null ? tank.percent : -1):0}, prompt \"{(FirstDrive.Prompt ?? "null").Replace("\n", " / ")}\"");
            }
            // ---- 8. END DRIVE and back ----
            if (Edition.HasCareer)
            {
                var pm4 = FindFirstObjectByType<PauseMenu>();
                if (pm4 != null) typeof(PauseMenu).GetMethod("ExitToMenu", Any).Invoke(pm4, null);
                float te = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().buildIndex != 0 && Time.realtimeSinceStartup - te < 60f) yield return null;
                Time.timeScale = 1f;
                yield return new WaitForSecondsRealtime(3f);
                Note($"  after END DRIVE: scene {SceneManager.GetActiveScene().name}, Active {FirstDrive.Active}, Prompt null {FirstDrive.Prompt == null}, handoff.FirstDrive {RaceHandoff.FirstDrive}");
                car = null; hud = null;
                var home = FindFirstObjectByType<LifeHomeScreen>();
                if (home != null)
                {
                    bool went = (bool)typeof(LifeHomeScreen).GetMethod("StartFreeRoam", Any).Invoke(home, null);
                    Note($"  Charlotte again: StartFreeRoam {went}, handoff.FirstDrive {RaceHandoff.FirstDrive}");
                    Check(!RaceHandoff.FirstDrive, "a quit tutorial does not come back");
                }
            }
        }

        IEnumerator Trace(string what, float seconds, string step)
        {
            float t0 = Time.time; float maxBack = 0f; var at = Plan;
            int gear0 = car.currentGear;
            while (Time.time - t0 < seconds && StepNow == step)
            {
                Pad(0, 1f, 0);
                maxBack = Mathf.Max(maxBack, (Plan - at).magnitude);
                yield return null;
            }
            Note($"  {what}: step {StepNow} after {Time.time - t0:0.00} s of the pedal down, moved {maxBack:0.0} m, {Kmh:0.0} km/h, gear {gear0} -> {car.currentGear}");
            Pad(0, 0, 0);
            yield return Fixed(50);
            Note($"  and released: step {StepNow}, {Kmh:0.0} km/h");
        }

        IEnumerator BrakeHeld()
        {
            yield return Fresh();
            brakeHeld = true;
            var ss = Near(false, Plan, 1, 40f);
            if (ss.Count > 0)
            {
                var a = ss[0].a; var lane = LaneMid(a);
                Put(lane - a.u * 40f, a.roadY, a.u);
                brakeHeld = false; yield return Settle(15); brakeHeld = true;
                yield return Drive(lane, a.u, 30f, 4f, true, 20f, () => StepNow != "StopSign" || Vector2.Dot(a.at - Plan, a.u) < 5f && Kmh < 3f);
                yield return Trace("STOP line", 5f, "StopSign");
                if (StepNow == "StopSign") { brakeHeld = false; yield return Hold(2f, () => StepNow != "StopSign"); }
            }
            brakeHeld = false;
            if (StepNow == "Light")
            {
                var sl = Near(true, Plan, 1, 40f);
                if (sl.Count > 0)
                {
                    var a = sl[0].a; var lane = LaneMid(a);
                    Put(lane - a.u * 40f, a.roadY, a.u);
                    yield return Settle(15);
                    float t0 = Time.time; var prev = CitySignals.AspectNow(a);
                    while (Time.time - t0 < 180f)
                    {
                        var now = CitySignals.AspectNow(a);
                        if (now == CitySignals.Aspect.Red && prev != CitySignals.Aspect.Red) break;
                        prev = now; Idle(); yield return null;
                    }
                    brakeHeld = true;
                    yield return Drive(lane, a.u, 30f, 4f, true, 20f, () => StepNow != "Light" || Vector2.Dot(a.at - Plan, a.u) < 5f && Kmh < 3f);
                    yield return Trace("red line", 5f, "Light");
                    // waiting out the red with the pedal still down
                    yield return Trace("waiting at the red", 12f, "LightWait");
                    Note($"  aspect {CitySignals.AspectNow(a)}, {Vector2.Dot(a.at - Plan, a.u):0.0} m from the line");
                    brakeHeld = false;
                    t0 = Time.time;
                    while (CitySignals.AspectNow(a) != CitySignals.Aspect.Green && Time.time - t0 < 120f) { Idle(); yield return null; }
                    yield return Drive(lane, a.u, 30f, 0f, false, 20f, () => StepNow != "LightWait");
                    yield return Settle(20);
                }
            }
            if (StepNow == "Park")
            {
                yield return new WaitForSeconds(2.2f);
                if (RowAt(FirstDrive.Target))
                {
                    var lot = targetLot; int r = targetRow;
                    float y0 = map.NearestRoadPoint(lot.runFoot[r], 300f, false, out int ei, out float s, out float _) ? map.edges[ei].YAt(s) : 0f;
                    Put(lot.runFoot[r], y0, Vector2.up);
                    Ground(lot.runFoot[r], y0, out var gh);
                    float lotY = gh.collider != null ? gh.point.y : y0;
                    if (FreeBay(lot, r, lotY, out var c, out var inward, out _))
                    {
                        Put(c - inward * 12f, lotY, inward);
                        yield return Settle(15);
                        brakeHeld = true;
                        yield return Drive(c, inward, 12f, 0f, true, 20f, () => StepNow != "Park" || Vector2.Dot(c - Plan, inward) < 1f && Kmh < 3f);
                        yield return Trace("in the bay", 5f, "Park");
                        brakeHeld = false;
                    }
                }
            }
            Note($"  ended on step {StepNow}");
        }

        void GpsArrivalSample()
        {
            var g = mini != null ? typeof(HudMinimap).GetField("gps", Any)?.GetValue(mini) : null;
            var solve = g?.GetType().GetMethod("Solve", Any);
            var ptsF = g?.GetType().GetField("pts", Any);
            var pick = typeof(FirstDrive).GetMethod("PickStop", Any);
            if (solve == null || pick == null) { Note("  gps sampling: no solver"); return; }
            int centreStop = 0, centreNot = 0, along = 0, side = 0, against = 0, none = 0, n = 0;
            float routeSum = 0f;
            for (int i = 0; i < map.edges.Length && n < 60; i += 37)
            {
                var e = map.edges[i];
                var q = LineModel.LanePoint(e, e.length * 0.5f);
                if ((q - map.uptown).magnitude > 4000f) continue;
                var fw = e.TangentAt(e.length * 0.5f);
                var args = new object[] { q, fw, null, null };
                if (!(bool)pick.Invoke(fd, args)) { none++; n++; continue; }
                var at = (Vector2)args[2]; var a = (CitySignals.Approach)args[3];
                solve.Invoke(g, new object[] { map, q, at });
                var pts = ptsF.GetValue(g) as List<Vector2>;
                n++;
                if (pts == null || pts.Count < 2) { none++; continue; }
                for (int k = 1; k < pts.Count; k++) routeSum += (pts[k] - pts[k - 1]).magnitude;
                var last = (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized;
                float dot = Vector2.Dot(last, a.u);
                if (dot > 0.5f) along++; else if (dot < -0.5f) against++; else side++;
                // and the old aim, the junction's centre, for comparison
                CitySignals.Junction tj = null; float best = 1e12f;
                foreach (var j in js)
                {
                    if (j.signal) continue;
                    bool st = false; foreach (var ap in j.approaches) if (ap.ctl == 2) st = true;
                    if (!st) continue;
                    float d = (j.centre - q).magnitude;
                    if (d < 40f || d >= best) continue;
                    best = d; tj = j;
                }
                if (tj == null) continue;
                solve.Invoke(g, new object[] { map, q, tj.centre });
                pts = ptsF.GetValue(g) as List<Vector2>;
                if (pts == null || pts.Count < 2) continue;
                last = (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized;
                bool hit = false;
                foreach (var ap in tj.approaches) if (ap.ctl == 2 && Vector2.Dot(ap.u, last) > 0.7f) hit = true;
                if (hit) centreStop++; else centreNot++;
            }
            typeof(HudMinimap).GetField("solvedDest", Any)?.SetValue(mini, new Vector2(float.NaN, float.NaN));
            Note($"  gps sample, {n} road starts within 4 km of uptown: aim up the STOP's arm: route ends heading down it {along}, joins it from the side {side}, heading the wrong way {against}, no pick or route {none}, mean route {routeSum / Mathf.Max(1, along + side + against):0} m; old aim (junction centre): last leg carries the STOP {centreStop}, does not {centreNot}");
        }

        // ---- the pump step's rule ----
        IEnumerator ToPump()
        {
            yield return Fresh();
            yield return PassStop();
            yield return PassLight();
            if (StepNow != "Park") { Check(false, "reached Park", StepNow); yield break; }
            yield return new WaitForSeconds(2.2f);
            if (RowAt(FirstDrive.Target)) yield return ParkIn(targetLot, targetRow, report: false);
            yield return WaitStep("Pump", 4f);
            yield return new WaitForSeconds(2.2f);
            Check(StepNow == "Pump", "reached Pump", StepNow);
        }

        bool lastAtPump;
        IEnumerator StandAtPump(float pct)
        {
            lastAtPump = ParkAtPump(FirstDrive.Target, out _, out _, false);
            Check(lastAtPump, "beside a pump");
            if (pct >= 0f) tank.percent = pct;
            yield return Settle(50);
            yield return Fixed(10);
            yield return null;
            Note($"  at the pump, tank {tank.percent:0.0}: AtPump {GasPump.AtPump} OfferFuel {ForecourtMode.OfferFuel}, step {StepNow}, hud \"{hud.centerText.text.Replace("\n", " / ")}\"");
        }

        IEnumerator OutOfCar()
        {
            yield return Press(GamepadButton.South);
            float t0 = Time.time;
            while (!ForecourtMode.OnFoot && Time.time - t0 < 6f) yield return null;
            Check(ForecourtMode.OnFoot, "pad south gets out", "tank " + tank.percent.ToString("0.0"));
            yield return Fixed(5);
            yield return null;
        }

        IEnumerator BackInCar()
        {
            var walker = GameObject.Find("Walker");
            if (walker != null) yield return WalkTo(walker, car.transform.position, 2.4f);
            yield return Fixed(10);
            yield return null;
            var it = walker != null ? walker.GetComponent<FootInteractor>() : null;
            var c = it != null ? it.Current : null;
            if (c != null && c.name == "CarTarget" && c.onUse != null) c.onUse();
            else
            {
                Note("  (GetIn called: the walker did not face the car)");
                fm.StartCoroutine((IEnumerator)typeof(ForecourtMode).GetMethod("GetIn", Any).Invoke(fm, null));
            }
            float t0 = Time.time;
            while (ForecourtMode.OnFoot && Time.time - t0 < 8f) yield return null;
            Check(!ForecourtMode.OnFoot, "back in the car");
        }

        // Walk to the nearest pump and press FILL UP; press it again after a while.
        IEnumerator Pump(float seconds)
        {
            var walker = GameObject.Find("Walker");
            if (walker == null) { Check(false, "a walker"); yield break; }
            Transform claimed = null; float nb = 1e9f;
            foreach (var gp in GasPump.All)
            {
                if (gp == null) continue;
                float dd = (gp.transform.position - walker.transform.position).sqrMagnitude;
                if (dd < nb) { nb = dd; claimed = gp.transform; }
            }
            if (claimed != null) yield return WalkTo(walker, claimed.position, 1.5f);
            yield return Fixed(10);
            yield return null;
            var it = walker.GetComponent<FootInteractor>();
            var cur = it != null ? it.Current : null;
            float pct0 = tank.percent;
            int money0 = Edition.HasCareer ? LifeSimManager.State.money : 0;
            Note($"  at the nozzle: current {(cur != null ? cur.name + " action \"" + cur.action + "\" detail \"" + cur.detail + "\"" : "nothing")}");
            if (cur == null || cur.onUse == null) yield break;
            cur.onUse();
            float t0 = Time.time; bool saw = false;
            while (Time.time - t0 < seconds) { saw |= GasPump.Fuelling; yield return null; }
            Note($"  pumped {seconds:0.0} s: Fuelling seen {saw}, tank {pct0:0.00} -> {tank.percent:0.00}, money {money0} -> {(Edition.HasCareer ? LifeSimManager.State.money : 0)}, gas prompt \"{GasPump.Prompt}\", FirstDrive.pumped {typeof(FirstDrive).GetField("pumped", Any).GetValue(fd)}");
            if (GasPump.FillLatched) { cur.onUse(); yield return new WaitForSecondsRealtime(0.3f); }
        }

        IEnumerator SampleAfterIn(string what)
        {
            float t0 = Time.time;
            foreach (float at in new[] { 0.1f, 0.5f, 1.5f, 3f, 5f })
            {
                while (Time.time - t0 < at) yield return null;
                Note($"  {what} +{at:0.0} s: step {StepNow}, hud \"{hud.centerText.text.Replace("\n", " / ")}\", FirstDrive.Prompt \"{(FirstDrive.Prompt ?? "null").Replace("\n", " / ")}\", OfferFuel {ForecourtMode.OfferFuel}");
            }
        }

        IEnumerator PumpCases()
        {
            var pm = FindFirstObjectByType<PauseMenu>();
            var truck = typeof(PauseMenu).GetMethod("CallFuelTruck", Any);

            Note(" 5a. out and back in without pumping, twice; then a real FILL UP");
            yield return ToPump();
            if (StepNow == "Pump")
            {
                yield return StandAtPump(50f);
                for (int i = 0; i < 2; i++)
                {
                    yield return OutOfCar();
                    yield return new WaitForSeconds(1f);
                    yield return BackInCar();
                    yield return SampleAfterIn("no gas, round " + i);
                    Check(StepNow == "Pump", "out and back in with no gas stays on Pump", StepNow);
                    Check(ForecourtMode.OfferFuel, "and the pump still offers the way out");
                }
                yield return OutOfCar();
                yield return Pump(2.5f);
                yield return BackInCar();
                yield return WaitStep("Done", 2f);
                Check(StepNow == "Done", "a real FILL UP then reaches Done", "tank " + tank.percent.ToString("0.0"));
            }

            Note(" 5b. the truck fills it on the way, then the pump");
            yield return ToPump();
            if (StepNow == "Pump" && pm != null)
            {
                tank.percent = 60f;
                truck.Invoke(pm, null);
                Note($"  truck: tank {tank.percent:0.0}");
                float t0 = Time.time;
                yield return StandAtPump(-1f);
                yield return WaitStep("Done", 3f);
                Check(StepNow == "Done", "a full tank stopped at a pump ends the step", $"{Time.time - t0:0.0} s, step {StepNow}, OfferFuel {ForecourtMode.OfferFuel} OfferGetOut {ForecourtMode.OfferGetOut}");
            }

            if (Edition.HasCareer)
            {
                var S = LifeSimManager.State;
                int keep = S.money;
                Note(" 5c. no money at the pump");
                yield return ToPump();
                if (StepNow == "Pump")
                {
                    S.money = 0;
                    yield return StandAtPump(50f);
                    yield return OutOfCar();
                    yield return Pump(1.5f);
                    Check(Mathf.Abs(tank.percent - 50f) < 0.01f, "no money, no fuel", tank.percent.ToString("0.00"));
                    yield return BackInCar();
                    yield return WaitStep("Done", 3f);
                    Check(StepNow == "Done", "no money at the pump ends the step", StepNow);
                }
                S.money = keep;

                Note(" 5d. one dollar at the pump");
                yield return ToPump();
                if (StepNow == "Pump")
                {
                    S.money = 1;
                    yield return StandAtPump(50f);
                    yield return OutOfCar();
                    yield return Pump(3f);
                    yield return BackInCar();
                    yield return WaitStep("Done", 3f);
                    Check(StepNow == "Done", "a dollar's worth pumped ends the step", $"step {StepNow}, tank {tank.percent:0.00}, money {S.money}");
                }
                S.money = keep;
            }

            Note(" 5e. just under what a pump will serve (the truck, then a short drive)");
            yield return ToPump();
            if (StepNow == "Pump")
            {
                yield return StandAtPump(99.4f);
                yield return OutOfCar();
                yield return Pump(1.5f);
                yield return BackInCar();
                yield return WaitStep("Done", 3f);
                Check(StepNow == "Done", "a sip at 99.4% ends the step", $"step {StepNow}, tank {tank.percent:0.00}");
            }

            Note(" 5f. the truck called while standing at the pump");
            yield return ToPump();
            if (StepNow == "Pump" && pm != null)
            {
                yield return StandAtPump(60f);
                yield return OutOfCar();
                truck.Invoke(pm, null);
                yield return null;
                Note($"  truck on foot: tank {tank.percent:0.0}");
                yield return new WaitForSeconds(0.5f);
                yield return BackInCar();
                yield return SampleAfterIn("truck on foot");
                Check(StepNow == "Done", "nothing left to pump: the step still ends", StepNow);
            }
        }

        IEnumerator PassLight()
        {
            var list = Near(true, Plan, 1, 40f);
            if (list.Count == 0) { Check(false, "a signal to use"); yield break; }
            var a = list[0].a;
            yield return RedStop(a);
            yield return WaitStep("LightWait", 3f);
            float t0 = Time.time;
            while (CitySignals.AspectNow(a) != CitySignals.Aspect.Green && Time.time - t0 < 180f) { Idle(); yield return null; }
            yield return Drive(LaneMid(a), a.u, 30f, 0f, false, 20f, () => StepNow != "LightWait");
            yield return Settle(15);
        }

        IEnumerator ThreeGreens()
        {
            yield return Fresh();
            yield return PassStop();
            if (StepNow != "Light") { Check(false, "reached Light", StepNow); yield break; }
            int crossed = 0;
            var list = Near(true, Plan, 3, 40f);
            if (list.Count == 0) { Check(false, "a signal to use"); yield break; }
            var a = list[0].a;
            var lane = LaneMid(a);
            for (int i = 0; i < 4 && StepNow == "Light"; i++)
            {
                Put(lane - a.u * 28f, a.roadY, a.u);
                yield return Settle(10);
                float t0 = Time.time;
                var prev = CitySignals.AspectNow(a);
                while (Time.time - t0 < 180f)
                {
                    var now = CitySignals.AspectNow(a);
                    if (now == CitySignals.Aspect.Green && prev != CitySignals.Aspect.Green) break;
                    prev = now;
                    Idle();
                    yield return null;
                }
                Note($"  green at {Time.time - t0:0.0} s wait");
                yield return Drive(lane, a.u, 40f, 0f, false, 14f, () => StepNow != "Light");
                crossed++;
                Note($"  crossed {crossed}: {lastDrive}; step {StepNow} greens {typeof(FirstDrive).GetField("greens", Any).GetValue(fd)}, hud \"{hud.centerText.text.Replace("\n", " / ")}\"");
            }
            Check(StepNow == "Park", "three greens pass the step", crossed + " crossings");
        }

        // ---- pictures ----------------------------------------------------
        void Shot(string name, int w, int h, bool worldToo)
        {
            try
            {
                var canvas = hud.centerText != null ? hud.centerText.canvas.rootCanvas : null;
                if (canvas == null) return;
                var go = new GameObject("FdShot");
                var cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
                cam.cullingMask = 1 << canvas.gameObject.layer;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var mode0 = canvas.renderMode;
                var cs = canvas.GetComponent<CanvasScaler>();
                var sm = cs != null ? cs.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize;
                float sf = cs != null ? cs.scaleFactor : 1f;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 10f;
                if (cs != null) { cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = h / 720f; }
                Canvas.ForceUpdateCanvases();
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(FirstDrivePlayCheck.OutDir, name + ".png"), tex.EncodeToPNG());
                DestroyImmediate(tex);
                canvas.renderMode = mode0;
                canvas.worldCamera = null;
                if (cs != null) { cs.uiScaleMode = sm; cs.scaleFactor = sf; }
                cam.targetTexture = null;
                rt.Release();
                Destroy(go);
            }
            catch (Exception e) { Note("  shot " + name + " failed: " + e.Message); }
        }

        void Top(string name, Vector2 at, float size)
        {
            try
            {
                var go = new GameObject("FdTop");
                var cam = go.AddComponent<Camera>();
                var main = FindFirstObjectByType<ChaseCamera>();
                cam.orthographic = true;
                cam.orthographicSize = size;
                cam.farClipPlane = 200f;
                go.transform.SetPositionAndRotation(new Vector3(at.x, car.transform.position.y + 60f, at.y), Quaternion.Euler(90f, 0f, 0f));
                var rt = new RenderTexture(900, 900, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(FirstDrivePlayCheck.OutDir, name + ".png"), tex.EncodeToPNG());
                DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Destroy(go);
            }
            catch (Exception e) { Note("  top " + name + " failed: " + e.Message); }
        }
    }
}
