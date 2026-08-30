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
    /// ESC or the MENU button opens it. There is no damage or fault system in
    /// this game, so "RESET CAR" clears the transient physics state instead:
    /// drift latch, e-brake window, wheelspin, gear, and body velocities.
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

        void Update()
        {
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
            open = v;
            IsOpen = v;
            if (panel != null) panel.SetActive(v);
            Time.timeScale = v ? 0f : 1f;
            AudioListener.pause = v;

            // Re-read the camera row on the way in. The view is restored from
            // PlayerPrefs in ChaseCamera.Start, and two Starts have no defined
            // order between them — build the label once and a player who last
            // drove in bumper cam opens the menu to a row claiming CHASE.
            if (v && camLabel != null) camLabel.text = CameraLabel();
            // Same story for the fuel row, whose price is a function of how
            // empty the tank is right now.
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

        Text camLabel;

        static string CameraLabel() =>
            "CAMERA: " + ChaseCamera.ViewNames[(int)ChaseCamera.Current];

        /// <summary>
        /// Step to the next view without closing the menu, so the player can
        /// read the names as they go past. The race is paused, so nothing moves
        /// behind it until they resume — the label is the feedback here, not the
        /// picture.
        /// </summary>
        void CycleCamera()
        {
            ChaseCamera.CycleView(1);
            if (camLabel != null) camLabel.text = CameraLabel();
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
            if (!RaceHandoff.FromLifeSim) return "FUEL TRUCK: FILL (FREE)";
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

            if (RaceHandoff.FromLifeSim)
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

        Text bulbLabel;

        static string BulbLabel() => "CLUSTER BULB: " + ClusterBulbs.Name;

        /// <summary>
        /// Step the instrument backlight. Green, amber, orange — three real
        /// cluster bulbs rather than three arbitrary hues, and the choice is
        /// remembered, so this row exists mainly so a player finds out there is
        /// a choice at all.
        ///
        /// The cluster itself picks the change up on its next frame by
        /// comparing a counter; nothing here has to reach into it, which
        /// matters because the pause menu outlives any one race scene.
        /// </summary>
        void CycleBulb()
        {
            ClusterBulbs.Cycle(1);
            if (bulbLabel != null) bulbLabel.text = BulbLabel();
        }

        Text pixelLabel;

        static string PixelLabel() => "PICTURE: " + PSXQuality.Name;

        /// <summary>
        /// Step the framebuffer resolution and how hard the dither is applied.
        /// The one setting a player is likely to want and could never guess at
        /// otherwise: the game is rendered into a few hundred lines and blown up
        /// to whatever the display is, and how coarse that is is taste rather
        /// than truth. PSXCameraOutput picks the change up on its next frame.
        /// </summary>
        void CyclePixels()
        {
            PSXQuality.Cycle(1);
            if (pixelLabel != null) pixelLabel.text = PixelLabel();
        }

        Text unitLabel;

        static string UnitLabel() => "SPEED: " + SpeedUnits.Label;

        /// <summary>
        /// Swap the speedometer between miles and kilometres.
        ///
        /// Here as well as on the front end's OPTIONS page because this is the
        /// one setting a player discovers while looking at the wrong number —
        /// mid-race, at the dial. The cluster rebuilds itself on the next frame
        /// off the same kind of counter the bulb uses, so the scale, the ticks,
        /// the numerals and the legend all change together rather than leaving
        /// an MPH needle on a km/h face.
        /// </summary>
        void CycleUnits()
        {
            SpeedUnits.Toggle();
            if (unitLabel != null) unitLabel.text = UnitLabel();
        }

        Text lookLabel;

        static string LookLabel() => "LOOK Y: " + LookPrefs.Label;

        /// <summary>
        /// Flip the on-foot pitch axis. Reachable from here because the
        /// forecourt is a walking place reached from a race, and a player who
        /// needs this needs it the moment they first look up and go down.
        /// </summary>
        void ToggleLook()
        {
            LookPrefs.Toggle();
            if (lookLabel != null) lookLabel.text = LookLabel();
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
            sb.Append("FAULT SYSTEM: none installed\n");
            sb.Append("surface   : ").Append(car.onRoad ? "ROAD" : "OFF-ROAD (low grip)").Append('\n');
            sb.Append("grounded  : ").Append(car.anyWheelGrounded ? "yes" : "NO (airborne)").Append('\n');
            sb.Append("speed     : ").Append(Mathf.RoundToInt(SpeedUnits.FromKmh(car.speedKmh)))
              .Append(SpeedUnits.Suffix).Append("  gear ")
              .Append(car.currentGear).Append("  rpm ").Append(Mathf.RoundToInt(car.currentRPM)).Append('\n');
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
                       new Vector2(24f, -24f), new Vector2(120f, 62f), 20, () => SetOpen(true));
            menuBtn.navigation = new Navigation { mode = Navigation.Mode.None };

            // Dimmed modal panel
            panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGO.transform, false);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.03f, 0.08f, 0.85f);
            var bgRT = bg.rectTransform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

            var title = MakeText(panel.transform, "PAUSED", font, 34,
                                 new Vector2(0.5f, 1f), new Vector2(0f, -70f));
            title.fontStyle = FontStyle.Bold;

            // Eleven rows in the height ten used to take. The panel already
            // reached the bottom of a 16:9 canvas at ten, and on a 20:9 phone
            // the scaler leaves under 650 units of height to put them in — so a
            // new row has to come out of the pitch rather than out of the
            // screen. 44 in a 49 step still reads as separate buttons and is
            // still a comfortable thumb target.
            const float RowH = 44f, RowStep = 49f;
            var rowSize = new Vector2(360f, RowH);
            float y = -108f;
            menuItems.Clear();
            // Order matters twice over: it is the reading order AND the pad's
            // navigation order, because the graph below is built from it.
            menuItems.Add(MakeButton(panel.transform, "RESUME", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 22, Resume)); y -= RowStep;
            // The camera cycle lives here as well as on C / triangle / the CAM
            // pad button, because a menu row is the only one of the four that
            // says out loud that there are six views.
            var camBtn = MakeButton(panel.transform, CameraLabel(), font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 20, CycleCamera);
            camLabel = camBtn.GetComponentInChildren<Text>();
            menuItems.Add(camBtn); y -= RowStep;
            var pixelBtn = MakeButton(panel.transform, PixelLabel(), font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 20, CyclePixels);
            pixelLabel = pixelBtn.GetComponentInChildren<Text>();
            menuItems.Add(pixelBtn); y -= RowStep;
            var bulbBtn = MakeButton(panel.transform, BulbLabel(), font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 20, CycleBulb);
            bulbLabel = bulbBtn.GetComponentInChildren<Text>();
            menuItems.Add(bulbBtn); y -= RowStep;
            // Next to the other two things about how the picture reads, and
            // above LOOK Y, which is about walking rather than driving.
            var unitBtn = MakeButton(panel.transform, UnitLabel(), font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 20, CycleUnits);
            unitLabel = unitBtn.GetComponentInChildren<Text>();
            menuItems.Add(unitBtn); y -= RowStep;
            var lookBtn = MakeButton(panel.transform, LookLabel(), font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 20, ToggleLook);
            lookLabel = lookBtn.GetComponentInChildren<Text>();
            menuItems.Add(lookBtn); y -= RowStep;
            menuItems.Add(MakeButton(panel.transform, "RESET CAR (UNSTICK)", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 20, ResetCar)); y -= RowStep;
            // Above RESTART rather than below it: a player opening this menu
            // with a dead engine is here for one of these two rows, and the
            // cheap one should be the one they reach first.
            var fuelBtn = MakeButton(panel.transform, FuelLabel(), font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 20, CallFuelTruck);
            fuelLabel = fuelBtn.GetComponentInChildren<Text>();
            menuItems.Add(fuelBtn); y -= RowStep;
            menuItems.Add(MakeButton(panel.transform, "RESTART RACE", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 22, RestartRace)); y -= RowStep;
            menuItems.Add(MakeButton(panel.transform, "EXIT TO MENU", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 22, ExitToMenu)); y -= RowStep;
            menuItems.Add(MakeButton(panel.transform, "TOGGLE DEBUG INFO", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), rowSize, 20, ToggleDebug)); y -= 54f;

            MakeText(panel.transform, "START / ESC CLOSES  ·  B / CIRCLE BACKS OUT", font, 15,
                     new Vector2(0.5f, 1f), new Vector2(0f, y)).color = new Color(0.72f, 0.74f, 0.85f);

            MenuNav.Column(menuItems);
            var navWatch = MenuNav.Watch(gameObject, menuItems[0]);
            MenuNav.Defer(navWatch, null, menuItems, null);

            // Debug readout lives outside the panel so it stays up while driving
            var dbgGO = new GameObject("DebugText");
            dbgGO.transform.SetParent(canvasGO.transform, false);
            debugText = dbgGO.AddComponent<Text>();
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
            var t = go.AddComponent<Text>();
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
