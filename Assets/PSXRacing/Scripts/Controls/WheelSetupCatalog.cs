using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE WHEEL SETUP page (CONTROLS > WHEEL SETUP), four tabs over the same
    /// <see cref="SettingsPanel"/> the settings menu uses, so it looks, reads
    /// and navigates like the rest of OPTIONS:
    ///   STEER    which axis, the base's rotation, the ratio, linearity, the
    ///            speed-lock assist and a centre deadzone
    ///   PEDALS   throttle, brake, clutch (captured), deadzone, invert, clear
    ///   BUTTONS  shift up / down, handbrake, look back, reset, camera, pause
    ///   FORCE    force feedback on/off, CONNECT (WebHID needs the click),
    ///            strength, invert, a test wiggle, any-device connect
    /// Every capture row shows the live reading, so the player sees the wheel
    /// and the pedals move the numbers before driving. Saved as it changes.
    /// </summary>
    public static class WheelSetupCatalog
    {
        public static readonly string[] TabNames = { "STEER", "PEDALS", "BUTTONS", "FORCE" };
        public static readonly string[] TabBlurbs =
        {
            "Which axis steers, how far the base turns, and how that reaches the road wheels.",
            "Throttle, brake and clutch: press each one to teach it.",
            "Paddles and buttons for the gears, the handbrake, the camera and the rest.",
            "Force feedback through the browser, on a PC.",
        };

        public const int StrengthStep = 10;
        static readonly WheelCapture cap = new WheelCapture();
        static float resultUntil;

        /// <summary>The capture the page drives (the self-test reads it).</summary>
        public static WheelCapture Capture => cap;

        public static readonly SettingsPages Pages = new SettingsPages
        {
            title = "WHEEL SETUP",
            tabNames = TabNames,
            tabBlurbs = TabBlurbs,
            items = (h, t, c) => Items(t),
            sortOrder = SettingsPanel.SortOrder + 2,
            liveEvery = 0.05f,
            hint = "Q / E  ·  LB / RB  PAGES     ESC  BACK",
            onTick = Tick,
            onClose = () => cap.Cancel(),
            helpOverride = () => cap.Busy || (cap.phase == WheelCapture.Phase.Failed && Time.unscaledTime < resultUntil) ? cap.Help : null,
        };

        static void Tick(SettingsPanel panel)
        {
            if (!cap.Busy) return;
            cap.Tick(Time.unscaledDeltaTime);
            if (!cap.Busy)
            {
                resultUntil = Time.unscaledTime + 2.5f;
                panel.RefreshValues();
            }
        }

        /// <summary>CONTROLS > WHEEL SETUP: over whichever settings page is open.</summary>
        public static void OpenFromSettings()
        {
            foreach (var p in Object.FindObjectsByType<SettingsPanel>(FindObjectsInactive.Exclude))
                if (p != null && p.IsOpen && p.pages == null) { p.OpenSub(Pages, 0); return; }
        }

        public static string WheelStateLabel()
        {
            if (!WheelConfig.Configured) return "NOT SET UP";
            return WheelConfig.Data.enabled ? "ON" : "OFF";
        }

        public static void ToggleWheel()
        {
            if (!WheelConfig.Configured) return;
            WheelConfig.Data.enabled = !WheelConfig.Data.enabled;
            WheelConfig.Save();
        }

        static void Begin(WheelCapture.Target t)
        {
            if (cap.Busy && cap.target == t) { cap.Cancel(); resultUntil = Time.unscaledTime + 1.5f; return; }
            cap.Begin(t);
        }

        /// <summary>The row's value: the capture's prompt while it runs (or
        /// its result for a moment after), else the live reading.</summary>
        static System.Func<string> CapValue(WheelCapture.Target t, System.Func<string> normal) => () =>
        {
            if (cap.target == t && (cap.Busy || Time.unscaledTime < resultUntil)) return cap.Prompt;
            return normal();
        };

        static string SteerLive()
        {
            var s = WheelConfig.Data.steer;
            if (!s.Bound) return "NOT SET";
            var n = WheelConfig.SteerNow();
            if (!n.HasValue) return "UNPLUGGED";
            int deg = Mathf.RoundToInt(WheelConfig.WheelDegrees(n.Value, WheelConfig.Data.rotationDeg));
            return s.Label + "  " + (deg > 0 ? "+" : "") + deg + "°";
        }

        static string PedalLive(WheelBinding b, int invBit)
        {
            if (!b.Bound) return "NOT SET";
            if (RawPads.Find(b.dev, b.ord) == null) return "UNPLUGGED";
            var d = WheelConfig.Data;
            int pct = Mathf.RoundToInt(100f * WheelConfig.Read(b, d.pedalDeadPct, WheelConfig.Inverted(invBit)));
            return b.Label + "  " + pct + "%";
        }

        static string ButtonLive(WheelBinding b)
        {
            if (!b.Bound) return "NOT SET";
            if (RawPads.Find(b.dev, b.ord) == null) return "UNPLUGGED";
            return b.Label + (WheelConfig.Held(b) ? "  ON" : "");
        }

        static string DeviceLabel()
        {
            var s = WheelConfig.Data.steer;
            if (s.Bound) return RawPads.ShortName(s.dev);
            int n = RawPads.All.Count;
            return n == 0 ? "NONE SEEN" : n + " SEEN";
        }

        public static List<SettingItem> Items(int page)
        {
            var list = new List<SettingItem>();
            var d = WheelConfig.Data;
            void Step(string name, System.Func<string> value, string blurb, System.Action apply) =>
                list.Add(new SettingItem { name = name, value = value, blurb = blurb, apply = apply });
            void Slide(string name, int min, int max, System.Func<int> get, System.Action<int> set,
                       System.Func<int, string> show, string blurb) =>
                list.Add(new SettingItem { name = name, min = min, max = max, get = get, set = set, show = show, blurb = blurb });
            void Capture(string name, WheelCapture.Target t, System.Func<string> live, string blurb) =>
                Step(name, CapValue(t, live), blurb, () => Begin(t));

            switch (page)
            {
                case 0: // STEER
                    Step("DEVICE", DeviceLabel,
                         "The wheel steering is read from. Not seen? Press a button on it - a browser shows a controller only once it is used.",
                         () => RawPads.Rescan());
                    Capture("STEERING AXIS", WheelCapture.Target.Steer, SteerLive,
                            "Press, then turn the wheel fully LEFT, fully RIGHT, and back to the centre.");
                    Slide("ROTATION", WheelConfig.MinRotation / WheelConfig.RotationStep, WheelConfig.MaxRotation / WheelConfig.RotationStep,
                          () => WheelConfig.Data.rotationDeg / WheelConfig.RotationStep,
                          v => { WheelConfig.Data.rotationDeg = v * WheelConfig.RotationStep; WheelConfig.Save(); },
                          v => (v * WheelConfig.RotationStep) + "°",
                          "The base's own rotation, as set in its own software (its steering angle). 900 is standard.");
                    Step("STEER RATIO", () => WheelConfig.RatioLabel,
                         "1:1 CAR: the car's real steering, full lock at 450 degrees each way. FULL LOCK: the whole rotation is the lock.",
                         () => { WheelConfig.Data.ratioMode = 1 - WheelConfig.Data.ratioMode; WheelConfig.Save(); });
                    Slide("LINEARITY", WheelConfig.MinLinearity / WheelConfig.LinearityStep, WheelConfig.MaxLinearity / WheelConfig.LinearityStep,
                          () => WheelConfig.Data.linearityPct / WheelConfig.LinearityStep,
                          v => { WheelConfig.Data.linearityPct = v * WheelConfig.LinearityStep; WheelConfig.Save(); },
                          v => (v * WheelConfig.LinearityStep) + "%",
                          "FULL LOCK only. 100% is straight; more makes the centre finer and the ends quicker.");
                    Step("SPEED LOCK", () => WheelConfig.Data.speedLock ? "ON" : "OFF",
                         "The pad's assist: less lock as the speed rises. OFF for a wheel - your hands do that.",
                         () => { WheelConfig.Data.speedLock = !WheelConfig.Data.speedLock; WheelConfig.Save(); });
                    Slide("DEADZONE", 0, WheelConfig.MaxSteerDead,
                          () => WheelConfig.Data.steerDeadPct,
                          v => { WheelConfig.Data.steerDeadPct = v; WheelConfig.Save(); },
                          v => v + "%",
                          "A dead band at the centre, for a base that never quite rests at zero. 0% for most bases.");
                    break;

                case 1: // PEDALS
                    Capture("THROTTLE", WheelCapture.Target.Throttle, () => PedalLive(WheelConfig.Data.throttle, WheelConfig.InvThrottle),
                            "Press, then push the throttle all the way down and let it go.");
                    Capture("BRAKE", WheelCapture.Target.Brake, () => PedalLive(WheelConfig.Data.brake, WheelConfig.InvBrake),
                            "Press, then push the brake all the way down and let it go. A combined pedal axis works too.");
                    Capture("CLUTCH", WheelCapture.Target.Clutch, () => PedalLive(WheelConfig.Data.clutch, WheelConfig.InvClutch),
                            "Saved and shown here; the cars have no clutch pedal yet, so a drive does not use it.");
                    Slide("DEADZONE", 0, WheelConfig.MaxPedalDead,
                          () => WheelConfig.Data.pedalDeadPct,
                          v => { WheelConfig.Data.pedalDeadPct = v; WheelConfig.Save(); },
                          v => v + "%",
                          "Ignored travel at BOTH ends of every pedal, so a pedal at rest is 0 and at the floor is 100.");
                    Step("INVERT", () => WheelConfig.InvertLabel,
                         "Flips a pedal that reads backwards. Teaching it with a press usually makes this unnecessary.",
                         WheelConfig.CycleInvert);
                    Step("CLEAR PEDALS", () => WheelConfig.Data.throttle.Bound || WheelConfig.Data.brake.Bound || WheelConfig.Data.clutch.Bound ? "CLEAR" : "NONE SET",
                         "Forget all three pedals.",
                         () => { var x = WheelConfig.Data; x.throttle.Clear(); x.brake.Clear(); x.clutch.Clear(); WheelConfig.Save(); });
                    break;

                case 2: // BUTTONS
                    const string wait = " Press it again and wait to clear it.";
                    Capture("SHIFT UP", WheelCapture.Target.ShiftUp, () => ButtonLive(WheelConfig.Data.shiftUp), "Usually the right paddle." + wait);
                    Capture("SHIFT DOWN", WheelCapture.Target.ShiftDown, () => ButtonLive(WheelConfig.Data.shiftDown), "Usually the left paddle." + wait);
                    Capture("HANDBRAKE", WheelCapture.Target.Handbrake, () => ButtonLive(WheelConfig.Data.handbrake), "A button, or pull a handbrake lever." + wait);
                    Capture("LOOK BACK", WheelCapture.Target.LookBack, () => ButtonLive(WheelConfig.Data.lookBack), "Hold it to look behind the car." + wait);
                    Capture("RESET CAR", WheelCapture.Target.Reset, () => ButtonLive(WheelConfig.Data.reset), "Puts the car back on the road." + wait);
                    Capture("CAMERA", WheelCapture.Target.Camera, () => ButtonLive(WheelConfig.Data.camera), "Changes the view." + wait);
                    Capture("PAUSE", WheelCapture.Target.Pause, () => ButtonLive(WheelConfig.Data.pause), "Opens and closes the pause menu." + wait);
                    break;

                case 3: // FORCE
                    Step("FORCE FEEDBACK", () => WheelConfig.Data.ffbOn ? "ON" : "OFF",
                         "The wheel pushes back: the tyres' grip, the kerbs, the hits. Off leaves the base to itself.",
                         () => { WheelConfig.Data.ffbOn = !WheelConfig.Data.ffbOn; WheelConfig.Save(); if (!WheelConfig.Data.ffbOn) ForceFeedback.Stop(); });
                    Step("CONNECT WHEEL", () => ForceFeedback.StatusLabel,
                         ForceFeedback.Supported
                            ? "Click, then pick the wheel base in the browser's list. Once allowed it reconnects by itself."
                            : "Needs a desktop browser with WebHID. Not available here.",
                         () => ForceFeedback.Connect(false));
                    Slide("FFB STRENGTH", 0, 100 / StrengthStep,
                          () => WheelConfig.Data.ffbStrengthPct / StrengthStep,
                          v => { WheelConfig.Data.ffbStrengthPct = v * StrengthStep; WheelConfig.Save(); },
                          v => (v * StrengthStep) + "%",
                          "How hard the wheel pushes. Start low on a strong base; 60% is the standard.");
                    Step("FFB INVERT", () => WheelConfig.Data.ffbInvert ? "ON" : "OFF",
                         "If the test pushes the wrong way, or a corner pulls the wheel in rather than out, turn this on.",
                         () => { WheelConfig.Data.ffbInvert = !WheelConfig.Data.ffbInvert; WheelConfig.Save(); });
                    Step("TEST", () => "PLAY",
                         "A short push right, then left, at the strength above. Hold the wheel lightly.",
                         FeelDriver.StartTest);
                    Step("OTHER DEVICE", () => "CONNECT",
                         "The base not in the list? This shows every device the browser can see.",
                         () => ForceFeedback.Connect(true));
                    break;
            }
            return list;
        }
    }
}
