using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// RUMBLE, WHEEL and FORCE FEEDBACK checks (2026-10-26).
    ///
    /// EDIT MODE (no scene): the rumble mixer against scripted events, the
    /// force feedback model's self-aligning torque curve, the wheel arithmetic
    /// (steer ratio, rotation, deadzones, pedals at either rest), the capture
    /// state machine on scripted pads (RawPads.Override), and the menu rows.
    ///
    /// PLAY MODE: a SIMULATED wheel - an Input System device on the HID
    /// interface with a wheel axis, two pedal axes (one inverted) and three
    /// buttons - plugged in under a real player car. The WHEEL SETUP capture
    /// is run on it exactly as the page runs it, then the car is driven through
    /// PlayerCarInput: road-wheel angle against the ratio at rest and at speed
    /// (with and without SPEED LOCK), pedal travel, a paddle shift, and the
    /// setup surviving a reload of the saved prefs.
    ///
    ///   powershell -ExecutionPolicy Bypass -File tools\wheel-check.ps1
    /// Writes PSXRacing_controls_check.txt beside the project.
    /// </summary>
    public static class ControlsCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        static string ReportPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_controls_check.txt");

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }
        internal static void Note(string s) => log.AppendLine("  .    " + s);

        static string F(float v) => v.ToString("0.000");

        [MenuItem("PSX Racing/Checks/Controls (rumble, wheel, FFB)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            log.AppendLine("CONTROLS CHECK " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            try { EditChecks(); }
            catch (System.Exception e) { Check(false, "edit-mode checks threw: " + e); }

            if (System.Environment.GetEnvironmentVariable("PSX_CONTROLS_EDIT_ONLY") == "1") { Finish(); Quit(); return; }

            // The play half needs a player car: any built venue, opened by FILE
            // (a sandbox's build list can belong to another edition).
            string scene = null;
            string id = null;
            foreach (var v in new[] { "DragQuarter", "HarborPoint", "RidgePass", "CityCircuit" })
            {
                // By ABSOLUTE path: a watched (windowed) editor's working
                // directory is not the project, and a relative File.Exists
                // found nothing on the first run.
                string p = "Assets/PSXRacing/Scenes/" + v + ".unity";
                string abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, p);
                if (File.Exists(abs) && TrackCatalog.TryIndexOf(v, out _)) { scene = p; id = v; break; }
            }
            if (scene == null) { Check(false, "a built venue scene to drive in"); Finish(); Quit(); return; }
            EditorSceneManager.OpenScene(scene);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = TrackCatalog.IndexOf(id);
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.Solo = true;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            Note("play mode in " + scene);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("ControlsCheckRunner").AddComponent<ControlsCheckRunner>();
        }

        /// <summary>Close the editor this check was launched in (batch OR the
        /// watched window - HandlingPlayCheck's way); never from the menu.</summary>
        static void Quit()
        {
            if (Application.isBatchMode || System.Environment.CommandLine.Contains("-executeMethod"))
                EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "CONTROLS CHECK OK" : "CONTROLS CHECK: " + failures + " FAIL");
            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log("[ControlsCheck]\n" + log);
        }

        // ================= edit mode =================

        static void EditChecks()
        {
            log.AppendLine("RUMBLE MIXER");
            var m = new RumbleMixer();
            const float dt = 1f / 60f;
            var rest = new FeelFrame();
            var r = m.Step(rest, dt);
            Check(r.x == 0f && r.y == 0f, "a car at rest with the engine idling: both motors still", F(r.x) + "/" + F(r.y));
            var cruise = new FeelFrame { speed = 25f, rpm01 = 0.5f, throttle = 0.4f };
            r = m.Step(cruise, dt);
            Check(r.x < 0.01f && r.y > 0f && r.y < 0.05f, "cruising on smooth tarmac: a faint high buzz only", F(r.x) + "/" + F(r.y));
            m.Reset();
            var hit = cruise; hit.hit = 0.8f;
            r = m.Step(hit, dt);
            float hit0 = r.x;
            float after = 0f;
            for (int i = 0; i < 24; i++) after = m.Step(cruise, dt).x;   // 0.4 s
            Check(hit0 > 0.6f && after < 0.08f, "a hit: a heavy blow on the low motor, gone in 0.4 s", F(hit0) + " -> " + F(after));
            m.Reset();
            var sh = cruise; sh.shift = true;
            float s0 = m.Step(sh, dt).x, s1 = 0f;
            for (int i = 0; i < 6; i++) s1 = m.Step(cruise, dt).x;      // 0.1 s
            Check(s0 > 0.3f && s1 < 0.02f, "a gear change: one short tick", F(s0) + " -> " + F(s1));
            m.Reset();
            var lim = cruise; lim.limiter = true; lim.throttle = 1f; lim.rpm01 = 1f;
            r = m.Step(lim, dt);
            Check(r.x > 0.15f && r.y > 0.25f, "the rev limiter: both motors", F(r.x) + "/" + F(r.y));
            m.Reset();
            var kerb = new FeelFrame { speed = 30f, texture = 1f };
            r = m.Step(kerb, dt);
            Check(r.x > 0.45f && r.y > 0.3f, "a kerb at speed: a strong texture", F(r.x) + "/" + F(r.y));
            var pile = new FeelFrame { speed = 30f, texture = 1f, slide = 1f, hit = 1f, bottom = 1f, limiter = true, shift = true };
            r = m.Step(pile, dt);
            Check(r.x <= 1f && r.y <= 1f && r.x > 0.9f, "everything at once saturates softly at 1, never past", F(r.x) + "/" + F(r.y));
            m.Reset();
            var bot = new FeelFrame { speed = 20f, bottom = 1f };
            Check(m.Step(bot, dt).x > 0.6f, "a bump stop struck: a heavy thump");

            RumblePrefs.ForgetCache();
            bool onWas = RumblePrefs.On; int levelWas = RumblePrefs.Level;
            PlayerPrefs.DeleteKey("psx.rumble"); PlayerPrefs.DeleteKey("psx.rumbleLevel");
            RumblePrefs.ForgetCache();
            Check(RumblePrefs.On && RumblePrefs.Level == 70 && Mathf.Approximately(RumblePrefs.Gain, 0.7f),
                  "RUMBLE defaults: on, 70%", RumblePrefs.Label + " " + RumblePrefs.Level);
            RumblePrefs.On = false;
            Check(RumblePrefs.Gain == 0f, "RUMBLE off: the gain is zero");
            RumblePrefs.On = onWas; RumblePrefs.Level = levelWas;
            RumbleOut.Stop();
            RumbleOut.Set(0.5f, 0.25f, 0.1f);
            Check(Mathf.Approximately(RumbleOut.Last.x, 0.5f) && Mathf.Approximately(RumbleOut.Last.y, 0.25f),
                  "the output takes (low, high) as mixed", RumbleOut.Last);
            RumbleOut.Stop();
            Check(RumbleOut.Last == Vector2.zero, "and Stop zeroes both");

            log.AppendLine("FORCE FEEDBACK MODEL");
            // A front tyre at 4 kN of grip, along the game's tyre curve.
            const float circle = 4000f;
            float Fy(float slip) => -Mathf.Sign(slip) * circle * Mathf.Min(1f, Mathf.Abs(slip) / 0.17f) *
                                    (Mathf.Abs(slip) > 0.17f ? 1f - 0.65f * Mathf.Min(1f, (Mathf.Abs(slip) - 0.17f) / (Mathf.PI / 2f - 0.17f)) : 1f);
            float peak = 0f, peakAt = 0f;
            for (float a = 0.005f; a < 0.6f; a += 0.005f)
            {
                float t = Mathf.Abs(FfbModel.SatOne(Fy(a), a, circle));
                if (t > peak) { peak = t; peakAt = a; }
            }
            float at30 = Mathf.Abs(FfbModel.SatOne(Fy(0.30f), 0.30f, circle));
            float at05 = Mathf.Abs(FfbModel.SatOne(Fy(0.05f), 0.05f, circle));
            Note("SAT peak " + F(peak) + " at " + F(peakAt) + " rad; 0.05 rad " + F(at05) + "; 0.30 rad " + F(at30));
            Check(peakAt > 0.10f && peakAt < 0.17f, "self-aligning torque peaks BEFORE the tyre's grip peak (0.17 rad)", F(peakAt));
            Check(Mathf.Abs(peak - 1f) < 0.05f, "and is normalised to 1 there", F(peak));
            Check(at30 < 0.45f * peak && at30 > 0f, "past the peak the wheel goes LIGHT, never dead", F(at30));
            // Steering right: the slip is negative, the force to the right, and the rim is pushed back LEFT.
            Check(FfbModel.SatOne(Fy(-0.08f), -0.08f, circle) < 0f, "turning right, the torque pushes the rim back left (centre)");
            var ffb = new FfbModel();
            var o = ffb.Step(new FeelFrame { speed = 20f, satNorm = 3f }, dt, 0.6f);
            Check(Mathf.Abs(o.constant) <= 0.6f + 1e-4f && o.spring <= 0.6f && o.damper <= 0.6f && o.texAmp <= 0.6f,
                  "every force clamped and scaled by FFB STRENGTH", F(o.constant));
            o = ffb.Step(new FeelFrame { speed = 20f, satNorm = 1f }, dt, 0f);
            Check(o.constant == 0f && o.spring == 0f && o.damper == 0f && o.texAmp == 0f, "strength 0: nothing at all");
            ffb.Reset();
            var j = ffb.Step(new FeelFrame { speed = 20f, hit = 1f, hitSide = 1f }, dt, 1f);
            float jl = 0f; for (int i = 0; i < 30; i++) jl = ffb.Step(new FeelFrame { speed = 20f }, dt, 1f).constant;
            Check(j.constant > 0.4f && Mathf.Abs(jl) < 0.05f, "a hit on the right kicks the rim right, gone in half a second", F(j.constant) + " -> " + F(jl));
            float sp0 = ffb.Step(new FeelFrame { speed = 30f }, dt, 1f).spring;
            float sp1 = ffb.Step(new FeelFrame { speed = 30f, frontSlide = 1f }, dt, 1f).spring;
            float dPark = ffb.Step(new FeelFrame { speed = 0f }, dt, 1f).damper, dFast = ffb.Step(new FeelFrame { speed = 30f }, dt, 1f).damper;
            Check(sp1 < sp0 * 0.5f, "the centring softens while the front slides", F(sp0) + " -> " + F(sp1));
            Check(dPark > dFast, "the damper is heaviest parked", F(dPark) + " / " + F(dFast));
            Check(!ForceFeedback.Supported && ForceFeedback.Current == ForceFeedback.State.Unsupported,
                  "outside a browser force feedback reports itself unavailable (no fake CONNECT)");

            log.AppendLine("WHEEL ARITHMETIC");
            var s = new WheelSteerBinding { dev = "x", index = 0, left = -1f, right = 1f, centre = 0f };
            Check(Mathf.Approximately(WheelConfig.SteerNorm(s, -1f, 0), -1f) && Mathf.Approximately(WheelConfig.SteerNorm(s, 1f, 0), 1f) &&
                  WheelConfig.SteerNorm(s, 0f, 0) == 0f, "the steering axis: stops at -1 and 1, centre 0");
            var inv = new WheelSteerBinding { dev = "x", index = 0, left = 0.9f, right = -0.9f, centre = 0.02f };
            Check(WheelConfig.SteerNorm(inv, -0.9f, 0) > 0.99f && WheelConfig.SteerNorm(inv, 0.9f, 0) < -0.99f &&
                  Mathf.Abs(WheelConfig.SteerNorm(inv, 0.02f, 0)) < 1e-4f,
                  "an inverted axis with an off-centre rest maps the same way");
            Check(WheelConfig.SteerNorm(s, 0.04f, 5) == 0f && WheelConfig.SteerNorm(s, 1f, 5) > 0.999f,
                  "the steering deadzone swallows the centre and keeps the stops");
            float lockDeg = 34f;
            float car900 = WheelConfig.RoadDegrees(1f, 900, WheelConfig.RatioCar, 100, lockDeg);
            float car540 = WheelConfig.RoadDegrees(1f, 540, WheelConfig.RatioCar, 100, lockDeg);
            float carHalf = WheelConfig.RoadDegrees(0.5f, 900, WheelConfig.RatioCar, 100, lockDeg);
            Check(Mathf.Abs(car900 - 34f) < 1e-3f && Mathf.Abs(car540 - 270f * 34f / 450f) < 1e-3f && Mathf.Abs(carHalf - 17f) < 1e-3f,
                  "1:1 CAR: lock at 450 deg of wheel; a 540 deg base runs out first; 225 deg is half lock",
                  F(car900) + " / " + F(car540) + " / " + F(carHalf));
            float full540 = WheelConfig.RoadDegrees(1f, 540, WheelConfig.RatioFullLock, 100, lockDeg);
            float lin = WheelConfig.RoadDegrees(0.5f, 540, WheelConfig.RatioFullLock, 100, lockDeg);
            float curve = WheelConfig.RoadDegrees(0.5f, 540, WheelConfig.RatioFullLock, 200, lockDeg);
            Check(Mathf.Abs(full540 - 34f) < 1e-3f && Mathf.Abs(lin - 17f) < 1e-3f && Mathf.Abs(curve - 8.5f) < 1e-3f,
                  "FULL LOCK: any rotation reaches the lock; LINEARITY 200% is finer at the centre",
                  F(full540) + " / " + F(lin) + " / " + F(curve));
            var pedal = new WheelBinding { dev = "x", index = 1, rest = -1f, full = 1f };
            var backwards = new WheelBinding { dev = "x", index = 2, rest = 1f, full = -1f };
            var combinedGas = new WheelBinding { dev = "x", index = 3, rest = 0f, full = 1f };
            var combinedBrake = new WheelBinding { dev = "x", index = 3, rest = 0f, full = -1f };
            Check(WheelConfig.Pedal(pedal, -1f, 3, false) == 0f && WheelConfig.Pedal(pedal, 1f, 3, false) == 1f &&
                  Mathf.Abs(WheelConfig.Pedal(pedal, 0f, 0, false) - 0.5f) < 1e-4f, "a pedal resting at -1: 0 .. 1");
            Check(WheelConfig.Pedal(backwards, 1f, 3, false) == 0f && WheelConfig.Pedal(backwards, -1f, 3, false) == 1f,
                  "a pedal that reads backwards (rest 1, floor -1): the same 0 .. 1");
            Check(WheelConfig.Pedal(combinedGas, -1f, 3, false) == 0f && WheelConfig.Pedal(combinedBrake, -1f, 3, false) == 1f &&
                  WheelConfig.Pedal(combinedGas, 1f, 3, false) == 1f && WheelConfig.Pedal(combinedBrake, 1f, 3, false) == 0f,
                  "one combined axis splits into throttle and brake");
            Check(WheelConfig.Pedal(pedal, -0.96f, 3, false) == 0f && WheelConfig.Pedal(pedal, 0.96f, 3, false) == 1f,
                  "the pedal deadzone works at both ends");
            Check(WheelConfig.Pedal(pedal, 1f, 3, true) == 0f, "INVERT flips a pedal");

            log.AppendLine("CAPTURE (scripted pads)");
            var savedPrefs = PlayerPrefs.GetString("psx.wheel", "");
            WheelConfig.ResetAll();
            var pad = new RawPad { id = "SIM WHEEL (Vendor: 1234 Product: 0001)", axes = new float[] { 0f, -1f, 1f, 0f }, buttons = new float[8] };
            var other = new RawPad { id = "PAD", standard = true, axes = new float[] { 0.02f, 0f, 0f, 0f }, buttons = new float[16] };
            RawPads.Override = new List<RawPad> { other, pad };
            var cap = new WheelCapture();
            void Run(System.Func<int, bool> script, int frames)
            {
                for (int f = 0; f < frames; f++) { if (!script(f)) break; RawPads.Invalidate(); cap.Tick(dt); }
            }
            cap.Begin(WheelCapture.Target.Steer);
            Run(f =>
            {
                float x = f < 20 ? -f / 20f : f < 30 ? -1f : f < 70 ? -1f + 2f * (f - 30) / 40f : f < 80 ? 1f : f < 100 ? 1f - (f - 80) / 20f : 0f;
                pad.axes[0] = x * 0.98f;
                return cap.Busy;
            }, 200);
            var sb = WheelConfig.Data.steer;
            Check(cap.phase == WheelCapture.Phase.Done && sb.dev == pad.id && sb.index == 0 && sb.left < -0.95f && sb.right > 0.95f,
                  "STEER: left, right, centre - the wheel axis, left first", cap.phase + " " + sb.Label + " " + F(sb.left) + ".." + F(sb.right));
            cap.Begin(WheelCapture.Target.Throttle);
            Run(f => { pad.axes[1] = f < 10 ? -1f + 2f * f / 10f : f < 20 ? 1f : f < 30 ? 1f - 2f * (f - 20) / 10f : -1f; return cap.Busy; }, 120);
            var tb = WheelConfig.Data.throttle;
            Check(cap.phase == WheelCapture.Phase.Done && tb.index == 1 && !tb.button && tb.rest < -0.95f && tb.full > 0.95f,
                  "THROTTLE: rest and full captured", tb.Label + " " + F(tb.rest) + " -> " + F(tb.full));
            cap.Begin(WheelCapture.Target.Brake);
            Run(f => { pad.axes[2] = f < 10 ? 1f - 2f * f / 10f : f < 20 ? -1f : f < 30 ? -1f + 2f * (f - 20) / 10f : 1f; return cap.Busy; }, 120);
            var bb = WheelConfig.Data.brake;
            Check(cap.phase == WheelCapture.Phase.Done && bb.index == 2 && bb.rest > 0.95f && bb.full < -0.95f,
                  "BRAKE that reads backwards: captured the right way round", bb.Label + " " + F(bb.rest) + " -> " + F(bb.full));
            cap.Begin(WheelCapture.Target.ShiftUp);
            Run(f => { pad.buttons[5] = f >= 5 && f < 12 ? 1f : 0f; return cap.Busy; }, 60);
            var up = WheelConfig.Data.shiftUp;
            Check(cap.phase == WheelCapture.Phase.Done && up.button && up.index == 5, "SHIFT UP: the button pressed", up.Label);
            cap.Begin(WheelCapture.Target.Handbrake);
            Run(f => { pad.axes[3] = f >= 5 && f < 15 ? 1f : 0f; return cap.Busy; }, 60);
            var hb = WheelConfig.Data.handbrake;
            Check(cap.phase == WheelCapture.Phase.Done && !hb.button && hb.index == 3, "HANDBRAKE: a lever (an axis) works as a button", hb.Label);
            cap.Begin(WheelCapture.Target.ShiftUp);
            Run(f => cap.Busy, Mathf.CeilToInt((WheelCapture.ButtonTimeout + 0.5f) / dt));
            Check(!WheelConfig.Data.shiftUp.Bound && cap.result == "CLEARED", "waiting out a button capture clears it", cap.result);
            cap.Begin(WheelCapture.Target.Throttle);
            Run(f => cap.Busy, Mathf.CeilToInt((WheelCapture.PedalTimeout + 0.5f) / dt));
            Check(cap.phase == WheelCapture.Phase.Failed && WheelConfig.Data.throttle.Bound,
                  "a pedal capture with nothing pressed fails and keeps the old binding", cap.result);
            // the reading through the bindings
            pad.axes[0] = 0.49f; pad.axes[1] = 0f; pad.axes[2] = 1f;
            RawPads.Invalidate(); WheelInput.Invalidate();
            Check(WheelInput.Active && Mathf.Abs(WheelInput.Steer - 0.5f) < 0.02f && Mathf.Abs(WheelInput.Throttle - 0.5f) < 0.02f &&
                  WheelInput.Brake == 0f, "WheelInput reads the bound wheel", F(WheelInput.Steer) + " / " + F(WheelInput.Throttle));
            WheelConfig.Data.enabled = false;
            WheelInput.Invalidate();
            Check(!WheelInput.Active, "WHEEL OFF parks it without forgetting it");
            WheelConfig.Data.enabled = true;
            WheelConfig.Save();
            WheelConfig.ForgetCache();
            Check(WheelConfig.Data.steer.index == 0 && WheelConfig.Data.brake.rest > 0.95f && WheelConfig.Data.handbrake.Bound,
                  "the setup is saved and reloads (psx.wheel)");
            Check(RawPads.ShortName(pad.id) == "SIM WHEEL", "the menu shows the device by a short name", RawPads.ShortName(pad.id));
            RawPads.Override = null;
            PlayerPrefs.SetString("psx.wheel", savedPrefs);
            WheelConfig.ForgetCache();

            log.AppendLine("MENU ROWS");
            var ctl = SettingsCatalog.Items(null, SettingsTab.Controls);
            Check(ctl.Select(i => i.name).SequenceEqual(new[] { "RUMBLE", "RUMBLE LEVEL", "WHEEL", "WHEEL SETUP" }),
                  "CONTROLS: RUMBLE, RUMBLE LEVEL, WHEEL, WHEEL SETUP", string.Join(",", ctl.Select(i => i.name)));
            var lvl = ctl.FirstOrDefault(i => i.name == "RUMBLE LEVEL");
            Check(lvl != null && lvl.IsSlider && lvl.show(lvl.min) == "0%" && lvl.show(lvl.max) == "100%", "RUMBLE LEVEL is a 0-100% slider");
            Check(SettingsCatalog.TabNames.Length == 5 && SettingsCatalog.TabNames[1] == "CONTROLS", "five tabs, CONTROLS second");
            for (int p = 0; p < WheelSetupCatalog.TabNames.Length; p++)
            {
                var rows = WheelSetupCatalog.Items(p);
                Check(rows.Count > 0 && rows.Count <= 7, "WHEEL SETUP " + WheelSetupCatalog.TabNames[p] + ": " + rows.Count + " rows (at most 7 fit a phone)");
                foreach (var row in rows)
                    if (row.IsSlider && row.name.Length > 13) Check(false, "slider name fits its 210-unit cell", row.name);
            }
        }
    }

    /// <summary>The play-mode half: a simulated wheel under a real car.</summary>
    public class ControlsCheckRunner : MonoBehaviour
    {
        const string Layout = @"{
            ""name"": ""PSXTestWheel"",
            ""format"": ""PSXW"",
            ""controls"": [
                { ""name"": ""wheel"",    ""layout"": ""Axis"",   ""format"": ""FLT"", ""offset"": 0 },
                { ""name"": ""gas"",      ""layout"": ""Axis"",   ""format"": ""FLT"", ""offset"": 4 },
                { ""name"": ""stop"",     ""layout"": ""Axis"",   ""format"": ""FLT"", ""offset"": 8 },
                { ""name"": ""paddleR"",  ""layout"": ""Button"", ""format"": ""FLT"", ""offset"": 12 },
                { ""name"": ""paddleL"",  ""layout"": ""Button"", ""format"": ""FLT"", ""offset"": 16 },
                { ""name"": ""hbBtn"",    ""layout"": ""Button"", ""format"": ""FLT"", ""offset"": 20 }
            ]
        }";
        const string Product = "PSX Test Wheel";

        InputDevice dev;
        CarController car;
        PlayerCarInput input;
        InputSettings.BackgroundBehavior bgWas;
        InputSettings.EditorInputBehaviorInPlayMode edWas;

        static void Check(bool ok, string what, object got = null) => ControlsCheck.Check(ok, what, got);
        static string F(float v) => v.ToString("0.000");

        void Set(string control, float v) => InputSystem.QueueDeltaStateEvent(dev[control], v);

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            ControlsCheck.log.AppendLine("PLAY MODE (simulated wheel)");
            for (int i = 0; i < 25; i++) yield return null;
            string savedPrefs = PlayerPrefs.GetString("psx.wheel", "");
            try { Begin(); }
            catch (System.Exception e) { Check(false, "setup threw: " + e.Message); End(savedPrefs); yield break; }
            if (dev == null || car == null || input == null) { End(savedPrefs); yield break; }

            // ---- the device is seen
            Set("wheel", 0f); Set("gas", -1f); Set("stop", 1f);
            yield return Frames(3);
            RawPads.Rescan();
            var rp = RawPads.All.FirstOrDefault(p => p.id == Product);
            Check(rp != null && rp.axes.Length == 3 && rp.buttons.Length == 3,
                  "the simulated wheel is seen as a raw controller: 3 axes, 3 buttons",
                  rp != null ? rp.axes.Length + "/" + rp.buttons.Length : "not seen (" + RawPads.All.Count + " pads)");
            if (rp == null) { End(savedPrefs); yield break; }

            // ---- the WHEEL SETUP capture, as the page runs it
            WheelConfig.ResetAll();
            var cap = WheelSetupCatalog.Capture;
            cap.Begin(WheelCapture.Target.Steer);
            for (int f = 0; f < 240 && cap.Busy; f++)
            {
                float x = f < 20 ? -f / 20f : f < 30 ? -1f : f < 70 ? -1f + 2f * (f - 30) / 40f : f < 80 ? 1f : f < 100 ? 1f - (f - 80) / 20f : 0f;
                Set("wheel", x);
                yield return null;
                cap.Tick(Time.unscaledDeltaTime);
            }
            var s = WheelConfig.Data.steer;
            Check(cap.phase == WheelCapture.Phase.Done && s.dev == Product && s.left < -0.95f && s.right > 0.95f,
                  "capture STEER on the device", cap.phase + " " + s.Label + " " + F(s.left) + ".." + F(s.right));
            yield return Pedal(cap, WheelCapture.Target.Throttle, "gas", -1f, 1f);
            yield return Pedal(cap, WheelCapture.Target.Brake, "stop", 1f, -1f);
            var t = WheelConfig.Data.throttle; var b = WheelConfig.Data.brake;
            Check(t.Bound && t.rest < -0.95f && t.full > 0.95f, "capture THROTTLE", t.Label + " " + F(t.rest) + "->" + F(t.full));
            Check(b.Bound && b.rest > 0.95f && b.full < -0.95f, "capture BRAKE (reads backwards)", b.Label + " " + F(b.rest) + "->" + F(b.full));
            yield return Button(cap, WheelCapture.Target.ShiftUp, "paddleR");
            yield return Button(cap, WheelCapture.Target.ShiftDown, "paddleL");
            yield return Button(cap, WheelCapture.Target.Handbrake, "hbBtn");
            Check(WheelConfig.Data.shiftUp.button && WheelConfig.Data.shiftDown.button && WheelConfig.Data.handbrake.button &&
                  WheelConfig.Data.shiftUp.index != WheelConfig.Data.shiftDown.index,
                  "capture the paddles and the handbrake button",
                  WheelConfig.Data.shiftUp.Label + " / " + WheelConfig.Data.shiftDown.Label + " / " + WheelConfig.Data.handbrake.Label);
            Check(WheelConfig.Active, "the wheel is now set up and on");

            // ---- driving through PlayerCarInput
            WheelConfig.Data.rotationDeg = 900;
            WheelConfig.Data.ratioMode = WheelConfig.RatioCar;
            WheelConfig.Data.speedLock = false;
            WheelConfig.Data.steerDeadPct = 0;
            WheelConfig.Data.pedalDeadPct = 3;
            WheelConfig.Save();
            yield return Place(0f);
            input.enabled = true;
            input.inputEnabled = true;
            float lockLow = car.maxSteerLowSpeedDeg;
            Set("wheel", 0.5f); Set("gas", -1f); Set("stop", 1f);
            for (int f = 0; f < 40; f++) yield return new WaitForFixedUpdate();
            float want = 0.5f * 450f * lockLow / 450f;
            Check(Mathf.Abs(car.SteerAngleDeg - want) < 0.5f && car.WheelSteerLive,
                  "1:1 CAR at rest: 225 deg of wheel is half the car's lock", F(car.SteerAngleDeg) + " deg, want " + F(want));
            // at speed: the wheel's angle, not the pad's speed-reduced lock
            yield return Place(28f);
            input.enabled = true; input.inputEnabled = true;
            Set("wheel", 0.04f);
            for (int f = 0; f < 6; f++) yield return new WaitForFixedUpdate();
            float atSpeed = car.SteerAngleDeg;
            float wantSpeed = 0.04f * 450f * lockLow / 450f;
            Check(Mathf.Abs(atSpeed - wantSpeed) < 0.25f, "at 100 km/h the wheel still sets the angle (no speed lock)",
                  F(atSpeed) + " deg, want " + F(wantSpeed) + " at " + F(Mathf.Abs(car.forwardSpeed)) + " m/s");
            WheelConfig.Data.speedLock = true;
            yield return Place(28f);
            input.enabled = true; input.inputEnabled = true;
            for (int f = 0; f < 6; f++) yield return new WaitForFixedUpdate();
            float locked = car.SteerAngleDeg;
            Check(locked < atSpeed - 0.05f && !car.WheelSteerLive, "SPEED LOCK on: the car's own speed-sensitive lock comes back",
                  F(locked) + " < " + F(atSpeed));
            WheelConfig.Data.speedLock = false;
            WheelConfig.Data.ratioMode = WheelConfig.RatioFullLock;
            yield return Place(0f);
            input.enabled = true; input.inputEnabled = true;
            Set("wheel", 1f);
            for (int f = 0; f < 40; f++) yield return new WaitForFixedUpdate();
            Check(Mathf.Abs(car.SteerAngleDeg - lockLow) < 0.5f, "FULL LOCK: the stop is the car's full lock", F(car.SteerAngleDeg) + " / " + F(lockLow));
            WheelConfig.Data.ratioMode = WheelConfig.RatioCar;
            Set("wheel", 0f);

            // pedals
            Set("gas", 0f); Set("stop", 1f);
            yield return Frames(3);
            float thr = car.throttleInput;
            Set("gas", -1f); Set("stop", -1f);
            yield return Frames(3);
            float brk = car.brakeInput, thr0 = car.throttleInput;
            Check(Mathf.Abs(thr - 0.5f) < 0.03f && thr0 == 0f && brk > 0.99f, "pedals: half throttle, full brake (backwards axis), released throttle",
                  F(thr) + " / " + F(brk) + " / " + F(thr0));
            Set("stop", 1f);
            Set("hbBtn", 1f);
            yield return Frames(3);
            Check(car.handbrakeInput, "the handbrake button pulls the lever");
            Set("hbBtn", 0f);

            // a paddle shift, rolling in a forward gear
            yield return Place(12f);
            input.enabled = true; input.inputEnabled = true;
            Set("gas", -1f); Set("stop", 1f);
            yield return Frames(3);
            car.manualMode = true;
            car.currentGear = 2;
            yield return new WaitForSeconds(0.3f);
            int g0 = car.currentGear;
            Set("paddleR", 1f);
            yield return Frames(3);
            Set("paddleR", 0f);
            yield return new WaitForSeconds(0.5f);
            int g1 = car.currentGear;
            Set("paddleL", 1f);
            yield return Frames(3);
            Set("paddleL", 0f);
            yield return new WaitForSeconds(0.5f);
            Check(g1 == g0 + 1 && car.currentGear == g0, "the right paddle shifts up, the left one down", g0 + " -> " + g1 + " -> " + car.currentGear);

            // the keys still win while pressed? (a wheel at rest must not hold the car straight)
            Check(car.WheelSteerLive, "the wheel steers while no other control is in use");

            // ---- persistence
            WheelConfig.ForgetCache();
            Check(WheelConfig.Data.steer.dev == Product && WheelConfig.Data.throttle.Bound && WheelConfig.Data.shiftUp.Bound,
                  "the whole setup reloads from the saved prefs");

            // ---- rumble and force feedback run with the car, and stop when paused
            Check(PlayerCarInput.Active == input, "the rumble and force feedback follow the live player input");
            input.enabled = false;
            yield return Frames(2);
            Check(!car.WheelSteerLive, "a disabled input leaves no wheel angle on the rack");
            Check(RumbleOut.Last == Vector2.zero && ForceFeedback.LastSent.constant == 0f,
                  "with no driver both are silent");

            End(savedPrefs);
        }

        IEnumerator Pedal(WheelCapture cap, WheelCapture.Target target, string control, float rest, float full)
        {
            cap.Begin(target);
            for (int f = 0; f < 180 && cap.Busy; f++)
            {
                float v = f < 10 ? Mathf.Lerp(rest, full, f / 10f) : f < 20 ? full : f < 30 ? Mathf.Lerp(full, rest, (f - 20) / 10f) : rest;
                Set(control, v);
                yield return null;
                cap.Tick(Time.unscaledDeltaTime);
            }
        }

        IEnumerator Button(WheelCapture cap, WheelCapture.Target target, string control)
        {
            cap.Begin(target);
            for (int f = 0; f < 60 && cap.Busy; f++)
            {
                Set(control, f >= 3 && f < 10 ? 1f : 0f);
                yield return null;
                cap.Tick(Time.unscaledDeltaTime);
            }
            Set(control, 0f);
        }

        IEnumerator Place(float mps)
        {
            input.enabled = false;
            car.TeleportTo(new Vector3(0f, 4001.6f, 0f), Quaternion.identity);
            for (int f = 0; f < 10; f++) yield return new WaitForFixedUpdate();
            car.SetRolling(mps);
        }

        void Begin()
        {
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            Check(car != null, "there is a player car");
            if (car == null) return;
            input = car.GetComponent<PlayerCarInput>();
            Check(input != null, "with its PlayerCarInput");
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
            var strip = new GameObject("ControlsStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            strip.AddComponent<BoxCollider>().size = new Vector3(3000f, 2f, 30000f);

            // A batch editor is not focused: let every device through regardless.
            bgWas = InputSystem.settings.backgroundBehavior;
            edWas = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            InputSystem.RegisterLayout(Layout, "PSXTestWheel", new InputDeviceMatcher().WithInterface("HID").WithProduct(Product));
            dev = InputSystem.AddDevice(new InputDeviceDescription { interfaceName = "HID", product = Product });
            Check(dev != null && dev.layout == "PSXTestWheel" && RawPads.IsRawDevice(dev),
                  "a simulated HID wheel is plugged in", dev != null ? dev.layout + " " + dev.name : "none");
        }

        void End(string savedPrefs)
        {
            try
            {
                if (input != null) input.enabled = false;
                if (dev != null) InputSystem.RemoveDevice(dev);
                InputSystem.RemoveLayout("PSXTestWheel");
                InputSystem.settings.backgroundBehavior = bgWas;
                InputSystem.settings.editorInputBehaviorInPlayMode = edWas;
            }
            catch (System.Exception e) { ControlsCheck.Note("cleanup: " + e.Message); }
            PlayerPrefs.SetString("psx.wheel", savedPrefs);
            PlayerPrefs.Save();
            WheelConfig.ForgetCache();
            ControlsCheck.Finish();
            EditorApplication.ExitPlaymode();
            if (System.Environment.CommandLine.Contains("-executeMethod"))
                EditorApplication.delayCall += () => EditorApplication.Exit(ControlsCheck.failures == 0 ? 0 : 1);
        }
    }
}
