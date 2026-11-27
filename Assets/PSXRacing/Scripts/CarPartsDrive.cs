using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// THE DRIVING SEAT'S OPEN CONTROL (carparts decision 6): one control steps
    /// the player's car SHUT, HOOD, TRUNK, DOORS, ALL, SHUT; held
    /// <see cref="CarParts.HoldToShutSeconds"/> it shuts everything.
    ///   keyboard O · pad D-pad down · wheel OPEN (WHEEL SETUP > BUTTONS) ·
    ///   touch OPEN (shown only while the car has parts and may open them)
    /// A tap steps on RELEASE (so a hold never steps first); a hold shuts at
    /// 0.6 s. Read from PlayerCarInput.Update, under its pause / focus gates,
    /// only while the player has the car (inputEnabled). A car without parts,
    /// or the LAB row off: nothing is read, no button shows.
    /// </summary>
    public static class CarPartsDrive
    {
        /// <summary>The engine is let go above this (the hysteresis over the 15 km/h load).</summary>
        public const float ReleaseAboveKmh = 30f;
        static float downAt = -1f;
        static CarController lastCar;
        static CarBody lastBody;
        static bool shutFired;

        /// <summary>Tests: whether the control is held now (null = the devices).</summary>
        public static bool? ScriptedHeld;

        public static void Tick(PlayerCarInput input, CarController car, bool inputEnabled)
        {
            var touch = TouchControls.Instance;
            if (car != lastCar) { lastCar = car; lastBody = car != null ? car.GetComponent<CarBody>() : null; }
            var body = lastBody;
            var def = body != null ? body.Def : null;
            if (!CarParts.On || body == null || !CarParts.Allowed(def, body.bodyFilter) || !inputEnabled)
            {
                if (touch != null) touch.SetOpen(false);
                downAt = -1f;
                return;
            }
            // the engine: loaded as the car slows where its lid may open, let go once it is away again
            float kmh = Mathf.Abs(car.speedKmh);
            if (kmh < CarParts.ShutAboveKmh && CarParts.PlaceAllows()) CarEngineBay.Preload(body.bodyFilter, def);
            else if (kmh > ReleaseAboveKmh && CarParts.MaskOf(body.bodyFilter) == 0) CarEngineBay.Release(body.bodyFilter);
            bool cockpit = ChaseCamera.Current == ChaseCamera.View.Cockpit;
            int avail = CarParts.Available(def);
            int now = CarParts.MaskOf(body.bodyFilter);
            bool canOpen = CarParts.PlaceAllows() && Mathf.Abs(car.speedKmh) < CarParts.OpenBelowKmh;
            int next = CarParts.NextStep(now, avail, !cockpit);
            if (touch != null)
                touch.SetOpen(canOpen && touch.Visible, next == 0 ? "SHUT ALL" : "OPEN " + CarParts.StepName(next, avail & (cockpit ? CarParts.Lids : CarParts.All)));

            bool held;
            if (ScriptedHeld.HasValue) held = ScriptedHeld.Value;
            else
            {
                var kb = Keyboard.current;
                var pad = Gamepad.current;
                held = (kb != null && kb.oKey.isPressed) ||
                       (pad != null && pad.dpad.down.isPressed) ||
                       (WheelInput.Active && WheelInput.OpenHeld) ||
                       (touch != null && touch.OpenHeld);
            }
            float t = Time.unscaledTime;
            if (held)
            {
                if (downAt < 0f) { downAt = t; shutFired = false; }
                else if (!shutFired && t - downAt >= CarParts.HoldToShutSeconds)
                {
                    shutFired = true;
                    CarParts.Request(body.bodyFilter, def, 0, player: true);
                }
            }
            else if (downAt >= 0f)
            {
                if (!shutFired) CarParts.Cycle(body.bodyFilter, def, player: true, doorsAllowed: !cockpit);
                downAt = -1f;
            }
        }
    }
}
