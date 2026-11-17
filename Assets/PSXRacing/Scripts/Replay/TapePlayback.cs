using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// THE VCR AT HOME (2026-11-16, see <see cref="Tapes"/>). A tape is a
    /// replay without its race: <see cref="Play"/> writes the tape's venue,
    /// hour, weather and field into the handoff and loads the venue, and
    /// <see cref="Attach"/> (from RaceManager.Start, beside the title and the
    /// intro) takes the race over before its countdown ever runs: the
    /// recorder is handed the tape's samples, the replay starts at once with
    /// the deck's transport (PLAY, PAUSE, REW / FF scanning with the picture
    /// moving), and EJECT goes back to the house with nothing banked - no
    /// result, no block of the day, no wear.
    ///
    /// The picture is the camcorder's (the tape was shot on one), the OSD the
    /// deck's: <see cref="Camcorder.Vcr"/>, <see cref="Camcorder.Transport"/>
    /// and <see cref="Camcorder.ScanRate"/> are published here every frame.
    /// </summary>
    public class TapePlayback : MonoBehaviour
    {
        /// <summary>The tape <see cref="Play"/> loaded the venue for; consumed by Attach.</summary>
        public static string PendingPath;
        static TapePlayback instance;
        /// <summary>A tape is on the deck in this scene.</summary>
        public static bool Active => instance != null;
        /// <summary>The tape on the deck.</summary>
        public static Tapes.Header Current => instance != null ? instance.header : null;

        RaceManager rm;
        Tapes.Header header;
        List<float> times;
        List<List<RaceReplay.Frame>> frames;
        bool begun;
        float savedVolume = 1f;
        bool muted;
        bool labelled;

        // ------------------------------------------------------------------
        //  From the house
        // ------------------------------------------------------------------
        /// <summary>
        /// Put the tape in the deck: the venue it was shot at, under its own
        /// hour and weather, with its own field on the grid. False and a
        /// reason when this build cannot play it (a venue it does not carry).
        /// </summary>
        public static bool Play(Tapes.Header h, out string why)
        {
            if (!Prime(h, out int scene, out why)) return false;
            SceneGate.Load(scene);
            return true;
        }

        /// <summary>The handoff half of <see cref=Play/>: everything the
        /// venue needs to stage the tape, and the scene to load, without
        /// loading it (the shot tools open the scene themselves).</summary>
        public static bool Prime(Tapes.Header h, out int scene, out string why)
        {
            why = null;
            scene = -1;
            if (h == null || string.IsNullOrEmpty(h.path)) { why = "NO TAPE"; return false; }
            if (!TrackCatalog.TryIndexOfShipped(h.venueId, out int track))
            { why = "THIS BUILD HAS NO " + h.venueName.ToUpperInvariant(); return false; }
            scene = TrackCatalog.SceneIndex(track);
            if (!TrackCatalog.SceneShipped(scene)) { why = h.venueName.ToUpperInvariant() + " IS NOT IN THIS BUILD"; return false; }

            RaceHandoff.ClearAll();
            RaceHandoff.ClearResult();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = track;
            var opp = new List<string>();
            for (int i = 0; i < h.cars.Count; i++)
            {
                var c = h.cars[i];
                if (i == h.playerIndex)
                {
                    if (CarCatalog.Get(c.specId) != null) RaceHandoff.CarSpecId = c.specId;
                    RaceHandoff.CarPaintSkin = string.IsNullOrEmpty(c.paint) ? null : c.paint;
                }
                else opp.Add(c.specId ?? "");
            }
            if (opp.Count == 0) RaceHandoff.Solo = true;
            else RaceHandoff.OpponentSpecIds = string.Join(";", opp);
            RaceHandoff.TimeOfDayIndex = Mathf.Clamp(h.hour, 0, TimeOfDay.Night);
            RaceHandoff.WeatherOverride = h.weather;
            if (h.day > 0) RaceHandoff.CalendarDay = h.day;
            RaceHandoff.TrafficLevel = 0;
            PendingPath = h.path;
            return true;
        }

        // ------------------------------------------------------------------
        //  In the venue
        // ------------------------------------------------------------------
        /// <summary>RaceManager.Start, after the field has settled: a tape
        /// takes the race over; a career race gets the SAVE TO TAPE offer.</summary>
        public static void Attach(RaceManager race)
        {
            if (race == null) return;
            if (string.IsNullOrEmpty(PendingPath))
            {
                if (TapeOffer.Wanted()) race.gameObject.AddComponent<TapeOffer>().rm = race;
                return;
            }
            string path = PendingPath;
            PendingPath = null;
            var h = Tapes.Load(path, out var t, out var f, out string why);
            if (h == null)
            {
                Debug.LogWarning("[TapePlayback] " + path + ": " + why);
                Eject();
                return;
            }
            var tp = race.gameObject.AddComponent<TapePlayback>();
            tp.rm = race;
            tp.header = h;
            tp.times = t;
            tp.frames = f;
            instance = tp;
        }

        IEnumerator Start()
        {
            // The field settles (the applier, the grid) and a city course
            // streams in round the cars before anything is put in motion.
            yield return null;
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (RaceManager.HoldCountdown != null && RaceManager.HoldCountdown() &&
                   Time.realtimeSinceStartup - t0 < 90f)
                yield return null;
            Begin();
        }

        /// <summary>The tape's cars onto the scene's: the player's onto the
        /// player's car, the rest onto the AI field in order (the handoff
        /// specced the AI field in the tape's order).</summary>
        public static List<CarController> MapField(Tapes.Header h, RaceManager race)
        {
            var map = new List<CarController>();
            var ai = new List<CarController>();
            foreach (var c in race.allCars) if (c != null && c != race.playerCar) ai.Add(c);
            int a = 0;
            for (int i = 0; i < h.cars.Count; i++)
            {
                if (i == h.playerIndex) map.Add(race.playerCar);
                else map.Add(a < ai.Count ? ai[a++] : null);
            }
            return map;
        }

        void Begin()
        {
            var rp = RaceReplay.Instance;
            if (rp == null || rm == null) { Eject(); return; }
            var cars = MapField(header, rm);
            var names = new List<string>();
            foreach (var c in header.cars) names.Add(c.name);
            if (!rp.LoadTape(cars, times, frames, names)) { Debug.LogWarning("[TapePlayback] the tape would not load"); Eject(); return; }
            rp.VcrControls = true;
            RaceReplay.ExitHook = OnExit;
            Camcorder.Vcr = true;
            Camcorder.StampDay = header.day;
            Camcorder.StampClock = header.clock;
            // The points the tape was shot at (v2): its cameras stand there
            // whatever this build's own feed would plant. A v1 tape has none
            // and is filmed at the build's points.
            Camcorder.TapeCourse = header.course != null && header.course.points.Count >= 2 ? header.course : null;
            rp.Begin();
            if (!RaceReplay.Playing) { Debug.LogWarning("[TapePlayback] the replay refused the tape"); Eject(); return; }
            // The player's own car first, as the camcorder followed it.
            int me = 0;
            for (int i = 0; i < rp.TrackCount; i++) if (rp.TrackCar(i) == rm.playerCar) { me = i; break; }
            rp.Watch(me);
            begun = true;
        }

        void Update()
        {
            if (!begun) return;
            var rp = RaceReplay.Instance;
            if (rp == null) return;
            Camcorder.Vcr = true;
            Camcorder.StampDay = header.day;
            Camcorder.StampClock = header.clock;
            bool scanning = Mathf.Abs(rp.Rate) > 1.01f && !rp.Paused;
            Camcorder.Transport = rp.Paused ? 1 : scanning ? 2 : 0;
            Camcorder.ScanRate = scanning ? rp.Rate : 0f;
            // A deck scanning is a deck with the sound off.
            // Through the fader: it owns AudioListener.volume every frame and
            // pulled a bare write straight back up.
            if (scanning && !muted) { AudioFader.Mute(true); muted = true; }
            else if (!scanning && muted) { AudioFader.Mute(false); muted = false; }
            if (!labelled) labelled = RelabelTouchBar();

            // A MONTAGE's joins are cuts: crossing one (either way, at any
            // speed) re-seats every car hard and the camcorder picks a fresh
            // operator, instead of the cars sliding across the map to the
            // next clip's poses.
            float now = rp.ReplayTime;
            if (header.cuts != null && header.cuts.Count > 0 && lastTime >= 0f)
                foreach (float cut in header.cuts)
                    if ((lastTime < cut && now >= cut) || (lastTime >= cut && now < cut))
                    {
                        rp.Seek(now, hard: true);
                        break;
                    }
            lastTime = now;
        }
        float lastTime = -1f;

        /// <summary>The phone's replay bar says SKIP 10 s on the two arrows;
        /// on the deck they are REW and FF.</summary>
        static bool RelabelTouchBar()
        {
            var touch = TouchControls.Instance;
            if (touch == null) return false;
            bool any = false;
            foreach (var t in touch.GetComponentsInChildren<Text>(true))
            {
                var p = t.transform.parent;
                if (p == null) continue;
                if (p.name == "Replay0") { t.text = "<< REW"; any = true; }
                else if (p.name == "Replay2") { t.text = "FF >>"; any = true; }
            }
            return any;
        }

        static bool OnExit()
        {
            if (instance == null) return false;
            Eject();
            return true;
        }

        /// <summary>EJECT: back to the shelf at home, nothing banked.</summary>
        public static void Eject()
        {
            if (instance != null && instance.muted) { AudioFader.Mute(false); instance.muted = false; }
            instance = null;
            RaceReplay.ExitHook = null;
            Camcorder.TapeCourse = null;
            Camcorder.Vcr = false;
            Camcorder.Transport = 0;
            Camcorder.ScanRate = 0f;
            RaceHandoff.ClearAll();
            RaceHandoff.ClearResult();
            LifeSim.LifeHomeScreen.PendingTab = "tapes";
            Time.timeScale = 1f;
            SceneManager.LoadScene(0);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (muted) { AudioFader.Mute(false); muted = false; }
            if (RaceReplay.ExitHook == (System.Func<bool>)OnExit) RaceReplay.ExitHook = null;
            Camcorder.TapeCourse = null;
            Camcorder.Vcr = false;
            Camcorder.Transport = 0;
            Camcorder.ScanRate = 0f;
        }
    }

    /// <summary>
    /// SAVE TO TAPE, on a career race's results screen and in its replay:
    /// T on a keyboard, SELECT (View / Share) on a pad, and a button for touch
    /// and the mouse. Pad Y is the camera there; SELECT is respawn while
    /// driving and stands down past the flag, so it is free on both screens.
    /// Once per race. The recorder rolls four seconds past the flag; a press
    /// before that is held and the tape cut when the tail is in.
    /// </summary>
    public class TapeOffer : MonoBehaviour
    {
        public RaceManager rm;
        public static TapeOffer Instance { get; private set; }

        bool want, saved;
        string why;
        Tapes.Header tape;
        float mouseSeenAt = -99f;
        GameObject canvasGo;
        Button button;
        Text buttonText;

        /// <summary>A career race worth a tape: not a delivery, a free roam,
        /// a test drive or the arcade.</summary>
        public static bool Wanted() =>
            Tapes.Enabled && RaceHandoff.FromLifeSim && Edition.HasCareer &&
            !RaceHandoff.Delivery && !RaceHandoff.FreeRoam && !RaceHandoff.TestDrive &&
            !RaceHandoff.TitleScreen;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; if (canvasGo != null) Destroy(canvasGo); }

        bool Offered()
        {
            if (rm == null || saved) return false;
            var rp = RaceReplay.Instance;
            if (rp == null || rp.TrackCount == 0) return false;
            return rm.State == RaceManager.RaceState.Finished || RaceReplay.Playing;
        }

        /// <summary>The line under the results (RaceHUD.ReplayOffer): what to
        /// press, the tape being cut, or where it went.</summary>
        public static string OfferLine(bool touch)
        {
            var o = Instance;
            if (o == null) return "";
            if (o.saved && o.tape != null) return "\nON THE SHELF: " + o.tape.venueName.ToUpperInvariant() + " " + Camcorder.DateStamp(o.tape.day);
            if (!string.IsNullOrEmpty(o.why)) return "\nNO TAPE: " + o.why;
            if (!o.Offered()) return "";
            if (o.want) return "\nSAVING TO TAPE...";
            if (touch) return "\nTAP SAVE TO TAPE TO KEEP IT";
            return Gamepad.current != null ? "\nSELECT TO SAVE TO TAPE" : "\nT TO SAVE TO TAPE";
        }

        void Update()
        {
            var rp = RaceReplay.Instance;
            bool offered = Offered();
            if (offered && !want)
            {
                var kb = Keyboard.current;
                var pad = Gamepad.current;
                bool press = (kb != null && kb.tKey.wasPressedThisFrame) ||
                             (pad != null && pad.selectButton.wasPressedThisFrame);
                if (press && !PauseMenu.IsOpen) Press();
            }
            if (want && !saved && rp != null && rp.RecordingDone)
            {
                want = false;
                tape = Tapes.SaveFromReplay(rp, rm, out why);
                saved = tape != null;
                if (saved) why = null;
            }
            UpdateButton(offered);
        }

        void Press()
        {
            if (saved || want) return;
            want = true;
            why = null;
        }

        // ---- the button (touch and mouse) ----
        void UpdateButton(bool offered)
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0.5f || mouse.leftButton.wasPressedThisFrame))
                mouseSeenAt = Time.unscaledTime;
            var touch = TouchControls.Instance;
            bool pointer = ShowButtonForTools || (touch != null && touch.Visible) || Time.unscaledTime - mouseSeenAt < 4f;
            if (saved && savedAt < 0f) savedAt = Time.unscaledTime;
            bool show = pointer && (offered || want || saved) && (!saved || Time.unscaledTime - savedAt < 4f);
            if (!show) { if (canvasGo != null && canvasGo.activeSelf) canvasGo.SetActive(false); return; }
            if (canvasGo == null) Build();
            if (!canvasGo.activeSelf) canvasGo.SetActive(true);
            string label = saved ? "ON THE SHELF" : want ? "SAVING..." : "SAVE TO TAPE";
            if (buttonText.text != label) buttonText.text = label;
            button.interactable = !saved && !want;
        }
        float savedAt = -1f;

        /// <summary>Right edge, a little above the middle: clear of the touch
        /// panel's REPLAY / FUEL corner, its CONTINUE and replay bar along the
        /// bottom, the wheel and the pedals, and the results in the centre.</summary>
        void Build()
        {
            canvasGo = new GameObject("TapeOfferCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 101;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            LifeSim.MenuKit.EnsureEventSystem();

            var go = new GameObject("SaveToTape");
            go.transform.SetParent(canvasGo.transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.40f, 0.10f, 0.10f, 0.88f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.62f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-24f, 0f);
            rt.sizeDelta = new Vector2(230f, 60f);
            button = go.AddComponent<Button>();
            button.targetGraphic = img;
            // Never a pad stop: the pad has SELECT, and a stray stick on a
            // results screen must not land here.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(Press);

            var tgo = new GameObject("Label");
            tgo.transform.SetParent(go.transform, false);
            buttonText = tgo.AddComponent<SafeText>();
            buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonText.fontSize = 22;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.color = Color.white;
            buttonText.raycastTarget = false;
            buttonText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var trt = buttonText.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
        }

        /// <summary>For the check: press SAVE TO TAPE as a player would.</summary>
        public void PressForTest() => Press();
        /// <summary>For the shot tools: the button as a touch player sees it.</summary>
        public static bool ShowButtonForTools;
        public GameObject ButtonCanvas => canvasGo;
        public Tapes.Header SavedTape => tape;
        public string Refusal => why;
    }
}
