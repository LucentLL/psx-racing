using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Pause menu and live physics readout. Built at runtime on its own overlay
    /// canvas at device resolution, so the buttons stay finger-sized even though
    /// the game renders at 320x240.
    ///
    /// ESC or the MENU button opens it. "RESET CAR" clears the transient
    /// physics state: drift latch, e-brake window, wheelspin, gear, and body
    /// velocities. (It predates the fault system and has nothing to do with
    /// it — a car's FAULTS live on the owned car and are changed, in a debug
    /// career, from the bench this menu opens: see <see cref="DebugCarPanel"/>.)
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public CarController playerCar;

        /// <summary>True while the modal panel is up. Driving input reads this:
        /// the menu runs at timeScale 0 but Update still ticks, so without it
        /// the same pad press that confirms a menu item also feeds the car.</summary>
        public static bool IsOpen { get; private set; }

        Canvas canvas;
        GameObject panel;
        Text debugText;
        bool open;
        bool debugOn;
        readonly StringBuilder sb = new StringBuilder(512);
        float debugTimer;
        readonly System.Collections.Generic.List<Selectable> menuItems =
            new System.Collections.Generic.List<Selectable>();

        void Start()
        {
            if (playerCar == null && RaceManager.Instance != null)
                playerCar = RaceManager.Instance.playerCar;
            BuildUI();
            SetOpen(false);
        }

        void OnDisable() => IsOpen = false;

        RectTransform menuBtnRT;
        bool menuBtnTouch;

        /// <summary>
        /// Top-left on a PC; on a PHONE, MIDDLE-LEFT, standing just above the
        /// steering wheel's box — the owner, 2026-09-25: "race map should be
        /// top of screen, menu button can be lowered to middle left". Same
        /// canvas reference as the touch panel, so its WheelTop is in these
        /// units. Re-placed when the touch panel comes or goes.
        /// </summary>
        void PlaceMenuButton()
        {
            if (menuBtnRT == null) return;
            menuBtnTouch = TouchControls.Showing;
            Vector2 pos = MenuButtonPos(menuBtnTouch, out Vector2 a);
            menuBtnRT.anchorMin = menuBtnRT.anchorMax = a;
            menuBtnRT.pivot = a;
            menuBtnRT.anchoredPosition = pos;
        }

        /// <summary>The MENU button's anchor (also its pivot) and position —
        /// public so the HUD preview measures the same rectangle.</summary>
        public static Vector2 MenuButtonPos(bool touch, out Vector2 anchor)
        {
            anchor = touch ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
            return touch ? new Vector2(24f, TouchControls.WheelTop + MenuOverWheelGap)
                         : new Vector2(24f, -24f);
        }

        public static readonly Vector2 MenuButtonSize = new Vector2(120f, 62f);

        /// <summary>Between the wheel's box and the MENU button: enough that a
        /// thumb reaching for the top of the rim does not pause the race.</summary>
        const float MenuOverWheelGap = 16f;

        void Update()
        {
            if (menuBtnRT != null && TouchControls.Showing != menuBtnTouch) PlaceMenuButton();

            // THE BENCH OWNS THE KEYS WHILE IT IS UP — and for the rest of the
            // frame it closed on. Escape, START and B all mean "close" to both
            // pages, and two Updates have no defined order between them: read
            // here as well, the press that backs out of the bench would also
            // drop the pause menu and resume the race, on whichever frame the
            // bench's Update happened to run first.
            if (bench != null && (bench.IsOpen || bench.ClosedFrame == Time.frameCount)) return;
            // The settings menu likewise (it has CREDITS over it in turn, and
            // stands back for that page itself).
            if (settings != null && (settings.IsOpen || settings.ClosedFrame == Time.frameCount)) return;

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool toggle = (kb != null && kb.escapeKey.wasPressedThisFrame) ||
                          (pad != null && pad.startButton.wasPressedThisFrame);
            // B / Circle closes, matching every console menu and the LifeSim's
            // own back key. It must not OPEN the menu — buttonEast is the
            // handbrake while driving.
            if (!toggle && open && pad != null && pad.buttonEast.wasPressedThisFrame) toggle = true;
            if (toggle) SetOpen(!open);

            if (debugOn && debugText != null)
            {
                // Unscaled: the readout must keep updating while paused, and it
                // does not need to run at full frame rate.
                debugTimer += Time.unscaledDeltaTime;
                if (debugTimer > 0.1f) { debugTimer = 0f; RefreshDebug(); }
            }
        }

        void SetOpen(bool v)
        {
            // The replay has its own exit (and its own use for START/ESC);
            // RESTART and EXIT from under it would abandon a finished race.
            if (v && RaceReplay.Playing) return;
            open = v;
            IsOpen = v;
            if (!v && settings != null && settings.IsOpen) settings.Close();
            if (panel != null) panel.SetActive(v);
            Time.timeScale = v ? 0f : 1f;
            AudioListener.pause = v;

            // Re-read the fuel row on the way in: its price is a function of
            // how empty the tank is right now.
            if (v && fuelLabel != null) fuelLabel.text = FuelLabel();

            // Put the cursor on RESUME when the panel opens, and take it off
            // when it closes. A UGUI navigation event goes to whatever is
            // selected and nowhere otherwise, so a pause menu with nothing
            // selected is a pad-proof trap: it opens on Start and there is no
            // way to move through it.
            if (EventSystem.current == null) return;
            if (v) MenuNav.Select(menuItems.Count > 0 ? menuItems[0] : null);
            else EventSystem.current.SetSelectedGameObject(null);
        }

        // ---- actions -------------------------------------------------------
        void Resume() => SetOpen(false);

        void RestartRace()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            IsOpen = false;
            // Same reasoning as ExitToMenu: a restart abandons whatever result
            // was stamped, and carrying it home would bank a purse and burn a
            // day slot for a race that was thrown away.
            RaceHandoff.ResultReady = false;

            // The TANK does not rewind, though.
            //
            // A reloaded scene re-seeds the tank from RaceHandoff.StartFuelPct,
            // which is written once, before the lights, from the car's level at
            // the time. Fuel bought mid-race has already left the wallet and
            // been saved — so restarting after a fill handed the player back
            // the empty tank they had paid to fill, and made them buy it again.
            // Carry the level forward and the restart costs a lap time, which
            // is what a restart is supposed to cost.
            var tank = Tank;
            if (tank != null) RaceHandoff.StartFuelPct = tank.percent;

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// Put the car back on the racing line and clear every piece of latched
        /// physics state. This is the "clear faults" equivalent — there are no
        /// faults to clear, but a stuck drift state or a car beached off-track
        /// produces the same "something is broken" feeling.
        /// </summary>
        void ResetCar()
        {
            if (playerCar != null) DriveSession.Respawn(playerCar);
            SetOpen(false);
        }

        // ---- the debug bench ----------------------------------------------
        DebugCarPanel bench;

        /// <summary>
        /// Open the debug bench over the pause menu: any fault, any stage, any
        /// part, onto the car being driven. See <see cref="LifeSim.DebugCarOps"/>.
        ///
        /// The pause menu STAYS OPEN underneath, deliberately. The bench needs
        /// exactly what this menu already holds — the clock stopped, the audio
        /// paused, the driving input gated by <see cref="IsOpen"/>. It is a row
        /// of the settings menu's SETTINGS page now, which also stays open
        /// under it, and closing the bench lands the tester back on that row.
        /// </summary>
        void OpenBench()
        {
            if (!open || !LifeSim.DebugCarOps.BenchAvailable) return;
            if (bench == null)
            {
                bench = gameObject.AddComponent<DebugCarPanel>();
                bench.onClosed = () =>
                {
                    // Give the cursor back to the row that opened the page, or
                    // the pad comes home to a menu with nothing selected —
                    // which is a dead pad. Guarded: this also fires from the
                    // bench's OnDisable as a scene is torn down.
                    if (this != null && open && settings != null) settings.Refocus();
                };
            }
            bench.Open();
        }

        /// <summary>The preview tool's car-less pause screen with the fuel
        /// row in it anyway, so the screen is checked at its tallest.</summary>
        public static bool PreviewFuelRow;

        // ---- the settings menu ----------------------------------------------
        SettingsPanel settings;
        readonly System.Collections.Generic.List<Button> pageBtns =
            new System.Collections.Generic.List<Button>();

        /// <summary>
        /// Open the settings menu (owner, 2026-10-04: "a proper menu with tabs
        /// for Gameplay, Visuals, Audio, Settings") at one of its four pages.
        /// Everything that used to stand in this menu's columns - the camera,
        /// the picture switches, the debug readout, CREDITS - lives there now,
        /// with the new STEERING slider and the volumes; this screen keeps the
        /// things that act on the drive.
        ///
        /// The pause panel hides while the page is up (a 94% backdrop over it
        /// was a ghost of eleven rows behind every page) and comes back on the
        /// button that opened it. Public for the preview tool.
        /// </summary>
        public void OpenSettings(SettingsTab at)
        {
            if (settings == null)
            {
                settings = gameObject.AddComponent<SettingsPanel>();
                settings.host = new SettingsHost
                {
                    debugInfo = () => debugOn,
                    toggleDebugInfo = ToggleDebug,
                    openBench = LifeSim.DebugCarOps.BenchAvailable ? OpenBench : (System.Action)null,
                };
                // The bench opens OVER the settings page and owns the keys
                // while it is up, and for the frame it closed on.
                settings.blocked = () => bench != null && (bench.IsOpen || bench.ClosedFrame == Time.frameCount);
                settings.onClosed = () =>
                {
                    if (this == null || !open) return;
                    if (panel != null) panel.SetActive(true);
                    int i = (int)settings.Tab;
                    MenuNav.Select(i < pageBtns.Count ? pageBtns[i] : (menuItems.Count > 0 ? menuItems[0] : null));
                };
            }
            if (panel != null) panel.SetActive(false);
            settings.Open(at);
        }
        Text fuelLabel;

        FuelTank Tank => playerCar != null ? playerCar.GetComponent<FuelTank>() : null;

        /// <summary>
        /// The escape hatch for a car that ran dry between pumps.
        ///
        /// Fuel is a live resource now, and a live resource you can only buy in
        /// one place is a way to strand a player half a lap from the forecourt
        /// with no way to reach it. So there is a truck, and it costs — a
        /// call-out fee on top of the fuel itself, which is exactly the
        /// relationship a tow has to a gas station in real life and exactly the
        /// reason to plan a stop instead.
        /// </summary>
        string FuelLabel()
        {
            var tank = Tank;
            if (tank == null) return "FUEL TRUCK: N/A";
            if (tank.percent >= 99.5f) return "FUEL TRUCK: TANK FULL";
            // Free where there is no wallet: a standalone editor race, and the
            // CITY edition, which has no career to bill.
            if (!RaceHandoff.FromLifeSim || !Edition.HasCareer) return "FUEL TRUCK: FILL (FREE)";
            int cost = LifeSim.LifeRules.CallOutRefuelCost(tank.percent, tank.Profile);
            var s = LifeSim.LifeSimManager.State;
            return s.money < cost
                ? "FUEL TRUCK: NEED " + LifeSim.MenuKit.Money(cost)
                : "CALL FUEL TRUCK — " + LifeSim.MenuKit.Money(cost);
        }

        void CallFuelTruck()
        {
            var tank = Tank;
            if (tank == null || tank.percent >= 99.5f) return;

            if (RaceHandoff.FromLifeSim && Edition.HasCareer)
            {
                var s = LifeSim.LifeSimManager.State;
                int cost = LifeSim.LifeRules.CallOutRefuelCost(tank.percent, tank.Profile);
                if (s.money < cost) { if (fuelLabel != null) fuelLabel.text = FuelLabel(); return; }
                s.money -= cost;
                tank.percent = 100f;
                var owned = s.FindCar(RaceHandoff.CarId) ?? s.ActiveCar;
                if (owned != null) owned.fuel = 100f;
                // On the race's receipt as well as in the log. The truck is
                // money spent on fuel during this race, and the line the player
                // reads on the way home should say so.
                RaceHandoff.FuelSpent += cost;
                // And the restart must not hand this tank back, for the same
                // reason it must not hand back a tank bought at the pumps.
                RaceHandoff.StartFuelPct = 100f;
                s.calendarLog.Add(LifeSim.LifeRules.LogDate(s.day) + ": fuel truck call-out — " +
                                  LifeSim.MenuKit.Money(cost));
                LifeSim.LifeSimManager.Save();
            }
            else tank.percent = 100f;

            if (fuelLabel != null) fuelLabel.text = FuelLabel();
        }

        void ToggleDebug()
        {
            debugOn = !debugOn;
            if (debugText != null) debugText.gameObject.SetActive(debugOn);
            if (debugOn) RefreshDebug();
        }

        /// <summary>
        /// Abandon the race and go back to the front end.
        ///
        /// This replaces a QUIT button that did nothing. Application.Quit() is a
        /// no-op in a browser — the tab is not ours to close — so the only exit
        /// the race scene offered was one that visibly did nothing when pressed.
        /// A player who wanted to stop driving and go buy a different car had
        /// nowhere to go from here, which is the other half of "there is no
        /// option to restart game or buy another car".
        ///
        /// The pending result is cleared on the way out: the race was abandoned,
        /// not finished, and banking a half-race would pay a purse and burn a
        /// day slot for a race nobody completed.
        /// </summary>
        void ExitToMenu()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            IsOpen = false;
            RaceHandoff.ResultReady = false;
            // QUITTING IS WHERE A TRIP ENDS, so it is never a commute leg. The
            // flag is a static that the legs of a longer trip set on their way
            // through — a hop back out of a shop page, the line between your
            // street and the town — and a drive abandoned from this menu after
            // one of those inherited it: the whole trip into town then cost no
            // block of the day at all. The trip is charged once, at its end,
            // and this is an end.
            RaceHandoff.CommuteLeg = false;
            // Free roam has no finish line, so leaving IS the finish: the city
            // session banks its metres, fuel and damage on the way out, where a
            // race would bank nothing because abandoning one voids the result.
            City.CityMode.Instance?.StampExitResult();
            SceneManager.LoadScene(0);
        }

        void RefreshDebug()
        {
            var car = playerCar;
            if (car == null) { debugText.text = "no player car"; return; }

            sb.Clear();
            // Read off the CAR, not off the request: these are the numbers the
            // physics is multiplying by this tick, so a fault thrown on the
            // bench shows here the moment it lands — or visibly does not.
            sb.Append("faults    : power x").Append(car.faultAccelMult.ToString("0.00"))
              .Append("  grip x").Append(car.faultGripMult.ToString("0.00"))
              .Append("  brake x").Append(car.faultBrakeMult.ToString("0.00"))
              .Append("  shift x").Append(car.faultShiftMult.ToString("0.0"))
              .Append("  pull ").Append(car.faultSteerPull.ToString("+0.00;-0.00;0")).Append('\n');
            sb.Append("build     : ").Append(Mathf.RoundToInt(car.massKg)).Append(" kg  stages P")
              .Append(car.activeTune.power).Append(" W").Append(car.activeTune.weight)
              .Append(" B").Append(car.activeTune.brakes).Append(" S").Append(car.activeTune.suspension)
              .Append(" T").Append(car.activeTune.tires)
              .Append(car.supercharged ? "  +blower" : "").Append(car.weldedDiff ? "  +weld" : "")
              .Append('\n');
            sb.Append("surface   : ").Append(car.onRoad ? "ROAD" : "OFF-ROAD (low grip)").Append('\n');
            sb.Append("grounded  : ").Append(car.anyWheelGrounded ? "yes" : "NO (airborne)").Append('\n');
            sb.Append("speed     : ").Append(Mathf.RoundToInt(SpeedUnits.FromKmh(car.speedKmh)))
              .Append(SpeedUnits.Suffix).Append("  gear ")
              .Append(car.currentGear).Append("  rpm ").Append(Mathf.RoundToInt(car.currentRPM)).Append('\n');
            // The two top speeds, side by side: what the stock car's sheet
            // says, and what THIS build on THIS gearing can reach on the level
            // (the figure the speedometer is scaled from). "Is 280 accurate?"
            // is answered by reading these against the line above.
            sb.Append("top speed : sheet ")
              .Append(Mathf.RoundToInt(SpeedUnits.FromKmh(car.topSpeedMps * 3.6f)))
              .Append("  as built ")
              .Append(Mathf.RoundToInt(SpeedUnits.FromKmh(car.ReachableTopSpeedMps * 3.6f)))
              .Append(SpeedUnits.Suffix).Append('\n');
            sb.Append("drifting  : ").Append(car.Drifting ? "YES" : "no")
              .Append("   ebrakeTimer ").Append(car.EbrakeTimer.ToString("0.00")).Append('\n');
            sb.Append("slip F/R  : ").Append((car.frontSlipAngle * Mathf.Rad2Deg).ToString("0.0"))
              .Append("deg / ").Append((car.rearSlipAngle * Mathf.Rad2Deg).ToString("0.0")).Append("deg\n");
            sb.Append("body slip : ").Append((car.chassisSlipAngle * Mathf.Rad2Deg).ToString("0.0")).Append("deg\n");
            sb.Append("wheelspin : ").Append(car.wheelspinRatio.ToString("0.00")).Append('\n');
            sb.Append("grip mult : ").Append(car.gripBonus.ToString("0.00"))
              .Append("   road mu ").Append(car.roadGrip.ToString("0.00"))
              .Append("  off mu ").Append(car.offroadGrip.ToString("0.00"));
            debugText.text = sb.ToString();
        }

        // ---- UI ------------------------------------------------------------
        void BuildUI()
        {
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            var canvasGO = new GameObject("MenuCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;            // above the touch controls
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Always-visible MENU button. Explicitly unreachable by pad: it
            // lives outside the modal panel and stays active while driving, so
            // leaving it on Automatic navigation would let a stray stick flick
            // land on it and a Submit press pause the race.
            var menuBtn = MakeButton(canvasGO.transform, "MENU", font, new Vector2(0f, 1f),
                       new Vector2(24f, -24f), MenuButtonSize, 20, () => SetOpen(true));
            menuBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            DarkenMenuButton(menuBtn);
            menuBtnRT = (RectTransform)menuBtn.transform;
            PlaceMenuButton();

            // THE PAUSE SCREEN (2026-10-04). The things that act on the drive
            // - RESUME, RESET CAR, the FUEL TRUCK, RESTART, EXIT - and under
            // them the four pages of the settings menu, one press away. It
            // used to be eleven rows and three columns of switches, which
            // filled a phone's height and had nowhere to put a twelfth; the
            // switches are in the tabbed menu now (SettingsPanel).
            //
            // On a MenuKit canvas of its own: matched to HEIGHT (the owner's
            // rule; the MENU button's canvas keeps the touch panel's 50/50
            // reference so it still stands over the wheel), GT2 charcoal with
            // the blueprint grid, and laid out in fixed steps from the top of
            // a 500-unit block - inside a phone's 560-unit column - centred in
            // a taller one. Nothing measures a rect.
            var pauseCanvas = LifeSim.MenuKit.Canvas(transform, "PauseCanvas", 200);
            panel = new GameObject("Panel");
            panel.transform.SetParent(pauseCanvas.transform, false);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.10f, 0.10f, 0.90f);
            var bgRT = bg.rectTransform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            LifeSim.MenuKit.GridBackdrop(panel.transform);

            var top = new Vector2(0.5f, 1f);
            const float RowW = 420f, RowH = 44f, RowStep = 52f, BlockH = 500f;
            float off = Mathf.Max(0f, (LifeSim.MenuKit.DesignHeight - BlockH) * 0.5f);
            var title = LifeSim.MenuKit.Label(panel.transform, "PAUSED", 34, top, new Vector2(0f, -14f - off),
                                              TextAnchor.MiddleCenter, LifeSim.MenuKit.Accent, 420f, 44f, bold: true);
            title.name = "Title";

            float y = -72f - off;
            menuItems.Clear();
            pageBtns.Clear();
            // Order matters twice over: it is the reading order AND the pad's
            // navigation order until the geometric graph takes over.
            Button Row(string label, UnityEngine.Events.UnityAction act)
            {
                var b = LifeSim.MenuKit.Button(panel.transform, label, top, new Vector2(0f, y),
                                               new Vector2(RowW, RowH), act, LifeSim.MenuKit.Small);
                menuItems.Add(b);
                y -= RowStep;
                return b;
            }
            Row("RESUME", Resume);
            Row("RESET CAR (UNSTICK)", ResetCar);
            // Where it applies: a car with a tank. Above RESTART, because a
            // player opening this menu with a dead engine is here for one of
            // these two rows, and the cheap one should be reached first.
            if (Tank != null || PreviewFuelRow)
            {
                var fuelBtn = Row(FuelLabel(), CallFuelTruck);
                fuelLabel = fuelBtn.GetComponentInChildren<Text>();
            }
            Row("RESTART RACE", RestartRace);
            Row("EXIT TO MENU", ExitToMenu);

            // The four pages, as one strip under the column: a press opens the
            // settings menu at that page (where LB / RB walk the others).
            y -= 6f;
            var head = LifeSim.MenuKit.Label(panel.transform, "OPTIONS", LifeSim.MenuKit.MinLabelSize, top,
                                             new Vector2(0f, y), TextAnchor.MiddleCenter, LifeSim.MenuKit.Dim,
                                             300f, 26f, bold: true);
            head.name = "OptionsHead";
            y -= 30f;
            int pages = SettingsCatalog.TabNames.Length;
            float stripW = Mathf.Min(760f, LifeSim.MenuKit.HalfWidth * 2f - 80f);
            const float Gap = 10f;
            float cell = (stripW - (pages - 1) * Gap) / pages;
            for (int i = 0; i < pages; i++)
            {
                var page = (SettingsTab)i;
                var pb = LifeSim.MenuKit.Button(panel.transform, SettingsCatalog.TabNames[i], top,
                                                new Vector2(-stripW * 0.5f + cell * 0.5f + i * (cell + Gap), y),
                                                new Vector2(cell, RowH), () => OpenSettings(page),
                                                LifeSim.MenuKit.Small);
                pb.name = "Btn_page_" + SettingsCatalog.TabNames[i];
                pageBtns.Add(pb);
                menuItems.Add(pb);
            }
            y -= RowH + 18f;

            var foot = LifeSim.MenuKit.Label(panel.transform, "START / ESC CLOSES  ·  B / CIRCLE BACKS OUT",
                                             LifeSim.MenuKit.MinLabelSize, top, new Vector2(0f, y),
                                             TextAnchor.MiddleCenter, LifeSim.MenuKit.Dim, stripW, 26f);
            foot.name = "Footer";

            MenuNav.Column(menuItems);
            var navWatch = MenuNav.Watch(gameObject, menuItems[0]);
            MenuNav.Defer(navWatch, null, menuItems, null);
            // Debug readout lives outside the panel so it stays up while driving
            var dbgGO = new GameObject("DebugText");
            dbgGO.transform.SetParent(canvasGO.transform, false);
            debugText = dbgGO.AddComponent<SafeText>();
            debugText.font = font;
            debugText.fontSize = 17;
            debugText.color = new Color(0.6f, 1f, 0.7f);
            debugText.alignment = TextAnchor.UpperLeft;
            debugText.horizontalOverflow = HorizontalWrapMode.Overflow;
            debugText.verticalOverflow = VerticalWrapMode.Overflow;
            debugText.raycastTarget = false;
            var dsh = dbgGO.AddComponent<Shadow>();
            dsh.effectColor = new Color(0f, 0f, 0f, 0.95f);
            dsh.effectDistance = new Vector2(1f, -1f);
            var dRT = debugText.rectTransform;
            dRT.anchorMin = new Vector2(0f, 1f); dRT.anchorMax = new Vector2(0f, 1f);
            dRT.pivot = new Vector2(0f, 1f);
            dRT.anchoredPosition = new Vector2(24f, -100f);
            dRT.sizeDelta = new Vector2(560f, 240f);
            dbgGO.SetActive(false);
        }

        static Text MakeText(Transform parent, string s, Font font, int size,
                             Vector2 anchor, Vector2 pos)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<SafeText>();
            t.font = font; t.fontSize = size; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = s;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(420f, 50f);
            return t;
        }

        /// <summary>
        /// THE ALWAYS-VISIBLE MENU BUTTON, LEGIBLE AT NOON (the colour pass,
        /// C9 review, 2026-09-29). It was MakeButton's pause-panel style - a
        /// 16% WHITE box with white text and no edge - which is right over the
        /// panel's charcoal and wrong over the game: at a clear noon it sits on
        /// the sky (about code 190), white on white-ish, under 2:1, the most
        /// washed-out thing in the owner's noon frame. It lives on its own
        /// overlay canvas at device resolution, which HudOnTop never sees, so
        /// the HUD's edge never reached it.
        ///
        /// Now a smoked charcoal box (72%: the canvas blends in linear light,
        /// so over a 0.52 sky the box is about 0.16 and the white label 5:1
        /// before its edge) and the HUD's black text edge (HudTextEdge), with
        /// the tint states re-based on the dark box: hover a lighter smoke,
        /// press the pause panel's gold. At night the box is all but the dark
        /// it stands on.
        /// </summary>
        static void DarkenMenuButton(Button btn)
        {
            if (btn == null) return;
            if (btn.targetGraphic is Image img) img.color = Color.white;   // the tint states carry the colour
            var c = btn.colors;
            c.normalColor = new Color(0.07f, 0.07f, 0.07f, 0.72f);
            c.highlightedColor = new Color(0.22f, 0.22f, 0.22f, 0.80f);
            c.pressedColor = new Color(1f, 0.85f, 0.35f, 0.75f);
            c.selectedColor = c.normalColor;
            c.disabledColor = c.normalColor;
            btn.colors = c;
            var t = btn.GetComponentInChildren<Text>(true);
            if (t != null && t.GetComponent<HudTextEdge>() == null)
            {
                var edge = HudOnTop.AddOutline(t.gameObject, 0.9f, 1);
                edge.effectDistance = new Vector2(1.5f, 1.5f);
            }
        }

        static Button MakeButton(Transform parent, string label, Font font, Vector2 anchor,
                                 Vector2 pos, Vector2 size, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            var rt = img.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.32f);
            colors.pressedColor = new Color(1f, 0.85f, 0.35f, 0.55f);
            // Selection has to be legible from across a room on a pad: gold and
            // considerably brighter than the mouse hover. UGUI's default
            // selectedColor is all but identical to normal, which is a cursor
            // the player cannot find.
            colors.selectedColor = new Color(1f, 0.85f, 0.35f, 0.50f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var t = MakeText(go.transform, label, font, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero);
            t.fontStyle = FontStyle.Bold;
            t.rectTransform.sizeDelta = size;
            return btn;
        }
    }
}
