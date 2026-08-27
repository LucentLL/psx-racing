using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Updates the UGUI HUD: speed, gear, RPM bar, lap, times, position,
    /// countdown and results.
    ///
    /// Every text field is change-gated. Assigning Text.text rebuilds the mesh
    /// and allocates, and most of these values change once a lap — rebuilding
    /// all seven strings every frame was the single largest managed allocation
    /// in the game.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        public CarController car;
        /// <summary>The analog dials. Speed and gear moved INTO them — a
        /// number in the corner and a number under a needle are the same
        /// number, and printing it twice is how a HUD ends up with a readout
        /// that disagrees with its own instrument.</summary>
        public GaugeCluster cluster;
        public Text lapText;
        public Text timeText;
        public Text lastLapText;
        public Text posText;
        public Text centerText;
        /// <summary>Flashes the name of the camera view for a moment after it
        /// changes. Six views are worth having only if the player can tell
        /// which one they just switched to.</summary>
        public Text camText;

        /// <summary>The player car's stuck watchdog. Its prompt takes over the
        /// centre banner while racing — the banner is empty then anyway, and a
        /// car that cannot move is the only thing worth saying at that
        /// moment.</summary>
        public StuckRecovery stuck;

        /// <summary>The live tank. Optional — a scene built before fuel existed
        /// simply has no gauge.</summary>
        public FuelTank tank;
        /// <summary>Level text beside the bar: "FUEL 62%".</summary>
        public Text fuelText;
        /// <summary>The bar itself. Emptied by shrinking the rect from the left
        /// edge, so it needs no sliced sprite and no fill mode.</summary>
        public RectTransform fuelFill;
        /// <summary>Full width of the fill, in framebuffer pixels. Set by the
        /// builder alongside the rect, because reading sizeDelta back on the
        /// frame the rect was created is the stale-read trap the menus already
        /// learned about the hard way.</summary>
        public float fuelFillWidth = 46f;

        static readonly Color FuelOk = new Color(0.55f, 0.95f, 0.6f);
        static readonly Color FuelLow = new Color(1f, 0.78f, 0.25f);
        static readonly Color FuelOut = new Color(1f, 0.35f, 0.3f);

        int lastFuelPct = int.MinValue;

        /// <summary>Set by RaceHandoffApplier from the car's faults. A dead
        /// cluster blanks speed/gear/tach; a failing tach wanders.</summary>
        public bool hideGauges;
        public bool rpmFlutter;

        int lastLap = int.MinValue;
        int lastPos = int.MinValue;
        int lastTimeCentis = int.MinValue;
        float lastBest = -1f;
        string lastCenter;
        string lastCam;
        /// <summary>Whether the player has changed view yet this race. Until
        /// they do, the flash carries the control that changes it — after that
        /// they plainly know, and repeating it would be nagging.</summary>
        bool camHintUsed;
        ChaseCamera.View lastCamView;

        /// <summary>How long the camera name stays up after a switch. Long
        /// enough to read at a glance, short enough not to become chrome.</summary>
        const float CamFlashSeconds = 2.4f;

        /// <summary>Invariant culture: on a machine with a comma decimal
        /// separator the lap clock would otherwise read 1'23,456.</summary>
        static string FormatTime(float t)
        {
            if (t <= 0f) return "--'--\"---";
            int m = (int)(t / 60f);
            float s = t - m * 60;
            return string.Format(CultureInfo.InvariantCulture, "{0}'{1:00.000}", m, s)
                         .Replace(".", "\"");
        }

        static void Set(Text field, string value)
        {
            if (field != null && field.text != value) field.text = value;
        }

        /// <summary>
        /// The free-roam HUD: the lap slot carries the street name (which is
        /// what Midnight Club printed there, and the single most useful line
        /// in a real city), the clock carries the session, the position slot
        /// says what mode this is, and the centre banner keeps its watchdog
        /// and dry-tank duties. The first seconds also carry the OSM
        /// attribution the road data legally requires.
        /// </summary>
        void UpdateCity(City.CityMode city)
        {
            if (cluster != null)
            {
                cluster.hideGauges = hideGauges;
                cluster.rpmFlutter = rpmFlutter;
            }

            string street = city.CurrentStreet;
            Set(lapText, string.IsNullOrEmpty(street) ? "CHARLOTTE" : street);

            int centis = Mathf.FloorToInt(city.SessionSeconds * 100f);
            if (centis != lastTimeCentis) { lastTimeCentis = centis; Set(timeText, FormatTime(city.SessionSeconds)); }

            Set(posText, "FREE ROAM");
            Set(lastLapText, city.SessionSeconds < 7f && world != null && world.Map != null
                ? "MAP DATA (C) OPENSTREETMAP CONTRIBUTORS" : "");

            UpdateFuel();

            if (lastCamView != ChaseCamera.Current) { lastCamView = ChaseCamera.Current; camHintUsed = true; }
            string cam = "";
            if (Time.unscaledTime - ChaseCamera.ChangedAt < CamFlashSeconds)
            {
                cam = ChaseCamera.ViewNames[(int)ChaseCamera.Current];
                if (!camHintUsed) cam += "   " + CameraHowTo();
            }
            if (cam != lastCam) { lastCam = cam; Set(camText, cam); }

            string center = !city.Live ? "CHARLOTTE"
                : (stuck != null ? stuck.Prompt : null)
                  ?? DryTankPrompt()
                  ?? "";
            if (center != lastCenter) { lastCenter = center; Set(centerText, center); }
        }

        /// <summary>The city world, for the attribution gate above. Wired by
        /// the city scene builder; null on every circuit.</summary>
        public City.CityWorld world;

        /// <summary>
        /// Name the control the player actually HAS. On a phone there is no C
        /// key, and telling someone to press one on a device without a keyboard
        /// reads as the game not knowing what it is running on — the same
        /// mistake the finish banner used to make about RESTART.
        /// </summary>
        static string CameraHowTo()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible)
                return "(CAM, TOP RIGHT)";
            return UnityEngine.InputSystem.Gamepad.current != null
                ? "(TRIANGLE / Y)" : "(PRESS C)";
        }

        void Awake() => HudOnTop.Apply(gameObject);

        void Update()
        {
            var rm = RaceManager.Instance;
            if (car == null) return;
            if (rm == null)
            {
                // No RaceManager means Charlotte: same canvas, no laps to count.
                var city = City.CityMode.Instance;
                if (city != null) UpdateCity(city);
                return;
            }
            var p = rm.GetProgress(car);

            // A dead instrument cluster ($350 to fix) blanks the readouts rather
            // than hiding the widgets: the player should see that the gauges are
            // broken, not that the HUD is missing.
            if (cluster != null)
            {
                cluster.hideGauges = hideGauges;
                cluster.rpmFlutter = rpmFlutter;
            }

            bool drag = rm.path != null && rm.path.drag;
            bool ends = rm.path != null && rm.path.HasEnds;

            if (p != null)
            {
                // A strip has no laps to count, so the slot names the distance
                // instead — "LAP 1/1" on a quarter mile is a readout that tells
                // the player nothing they did not already know. A stage names
                // its run the same way.
                int lap = ends ? 1 : Mathf.Min(p.lap, rm.totalLaps);
                if (lap != lastLap)
                {
                    lastLap = lap;
                    Set(lapText, ends ? rm.path.dragLabel : "LAP " + lap + "/" + rm.totalLaps);
                }

                // The clock only needs redrawing when a hundredth ticks over.
                int centis = Mathf.FloorToInt(p.raceTime * 100f);
                if (centis != lastTimeCentis) { lastTimeCentis = centis; Set(timeText, FormatTime(p.raceTime)); }

                if (!Mathf.Approximately(p.bestLapTime, lastBest))
                {
                    lastBest = p.bestLapTime;
                    Set(lastLapText, p.bestLapTime > 0f ? "BEST " + FormatTime(p.bestLapTime) : "");
                }
            }

            int pos = rm.GetPosition(car);
            if (pos != lastPos) { lastPos = pos; Set(posText, "POS " + pos + "/" + rm.allCars.Count); }

            UpdateFuel();

            // Unscaled: the pause menu freezes time, and a view switched just
            // before pausing should not have its label frozen on screen with it.
            if (lastCamView != ChaseCamera.Current) { lastCamView = ChaseCamera.Current; camHintUsed = true; }
            string cam = "";
            if (Time.unscaledTime - ChaseCamera.ChangedAt < CamFlashSeconds)
            {
                cam = ChaseCamera.ViewNames[(int)ChaseCamera.Current];
                if (!camHintUsed) cam += "   " + CameraHowTo();
            }
            if (cam != lastCam) { lastCam = cam; Set(camText, cam); }

            string center = null;
            switch (rm.State)
            {
                case RaceManager.RaceState.Countdown:
                    float remaining = rm.CountdownRemaining - 1f;
                    center = remaining > 0f ? Mathf.CeilToInt(remaining).ToString() : "GO!";
                    break;
                case RaceManager.RaceState.Racing:
                    // Priority, most urgent first: the lights, then the stuck
                    // watchdog, then the nozzle, then a dry tank.
                    //
                    // The watchdog is above the pump because it now only speaks
                    // when the car is on its ROOF or pinned against a wall — it
                    // stands down for a car merely parked on a forecourt. When
                    // it does speak on a forecourt, it is because the player
                    // rolled it there, and "HOLD F TO FUEL" over the top of the
                    // only instructions for getting out is the game answering a
                    // question nobody asked.
                    center = rm.CountdownRemaining > 0f ? "GO!"
                           : (stuck != null ? stuck.Prompt : null)
                             ?? GasPump.Prompt
                             ?? OnFoot.ForecourtMode.Prompt
                             ?? DryTankPrompt()
                             ?? "";
                    break;
                case RaceManager.RaceState.Finished:
                    // A blacklist challenge is about the name, not the position:
                    // the ladder headline goes first and the timing sheet after.
                    string ladder = RaceHandoff.RivalRank > 0
                        ? "#" + RaceHandoff.RivalRank + " " + RaceHandoff.RivalAlias +
                          (pos == 1 ? " DEFEATED\n" : " KEEPS THE SPOT\n")
                        : "";
                    // Name the control the player actually HAS. On a phone there
                    // is no R key, and telling someone to press one on a device
                    // without a keyboard reads as the game not knowing what it
                    // is running on. The touch RESET button already drives this
                    // same action; it just was not what the text said.
                    bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;
                    string how = touch ? "TAP RESET (TOP RIGHT)"
                               : UnityEngine.InputSystem.Gamepad.current != null ? "PRESS A / CROSS"
                               : "PRESS R";
                    // A drag result is an ET and a trap speed. Reporting a "best
                    // lap" for a single 402 m run would be the circuit's answer
                    // to a question the strip did not ask. A stage result is an
                    // ET too — but a trap speed on a mountain finish line is
                    // drag talk, so the stage sheet is the time alone.
                    string sheet = ends
                        ? "\nET " + FormatTime(p != null ? p.finishTime : 0f) +
                          (drag ? "   TRAP " + Mathf.RoundToInt(p != null ? p.trapSpeedKmh : 0f) + " km/h" : "")
                        : "\nBEST " + FormatTime(p != null ? p.bestLapTime : 0f);
                    center = ladder + "FINISH!  P" + pos + sheet +
                             "\n\n" + how +
                             (RaceHandoff.FromLifeSim ? " TO GO HOME" : " TO RESTART");
                    break;
            }
            if (center != lastCenter) { lastCenter = center; Set(centerText, center); }
        }

        Color lastFuelColor = new Color(-1f, -1f, -1f, -1f);

        /// <summary>
        /// The fuel gauge, and the contextual touch button that goes with it.
        ///
        /// This one readout lives on the 240-line HUD canvas rather than in the
        /// device-resolution cluster, and the split is the project's own: the
        /// CABIN — what you read under a needle and what you hold — is at device
        /// resolution, and the RACE DATA printed over the world stays at 240
        /// lines. How much fuel is left is race data. It is also the only number
        /// on screen that can end the race on its own, which is why it gets a
        /// bar rather than a line of text: a bar is read at a glance mid-corner
        /// and a percentage is not.
        /// </summary>
        void UpdateFuel()
        {
            if (tank != null)
            {
                float pct = Mathf.Clamp(tank.percent, 0f, 100f);

                if (fuelFill != null)
                {
                    float w = fuelFillWidth * pct * 0.01f;
                    var size = fuelFill.sizeDelta;
                    if (!Mathf.Approximately(size.x, w))
                        fuelFill.sizeDelta = new Vector2(w, size.y);

                    Color want = tank.Empty ? FuelOut : tank.Low ? FuelLow : FuelOk;
                    if (want != lastFuelColor)
                    {
                        lastFuelColor = want;
                        var img = fuelFill.GetComponent<Image>();
                        if (img != null) img.color = want;
                        if (fuelText != null) fuelText.color = want;
                    }
                }

                // Ceil, not round: a tank with anything at all in it must not
                // read as 0%, because 0% is the number that means the engine
                // has stopped.
                int shown = tank.Empty ? 0 : Mathf.Max(1, Mathf.CeilToInt(pct));
                if (shown != lastFuelPct)
                {
                    lastFuelPct = shown;
                    Set(fuelText, "FUEL " + shown + "%");
                }
            }

            // The FUEL button only exists while a nozzle is offering itself.
            var touch = TouchControls.Instance;
            if (touch != null) touch.SetAction(GasPump.AtPump && GasPump.Prompt != null, "FUEL");
        }

        /// <summary>
        /// What to say to a player whose engine just stopped.
        ///
        /// It has to name the way OUT, not just the problem. A dry tank is the
        /// one state in the game the car cannot drive itself out of, so the
        /// banner points at the pause menu, where the fuel truck is.
        /// </summary>
        string DryTankPrompt()
        {
            if (tank == null || !tank.Empty) return null;
            string how = TouchControls.Instance != null && TouchControls.Instance.Visible
                ? "TAP MENU (TOP LEFT)"
                : UnityEngine.InputSystem.Gamepad.current != null ? "PRESS START" : "PRESS ESC";
            return "OUT OF FUEL\n" + how + " TO CALL THE FUEL TRUCK";
        }
    }
}
