using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TEMPERATURE GAUGE, IN THE RUNNING GAME.
    ///
    /// The model itself is pinned in edit mode by LifeSimSelfTest — where it
    /// settles, what it costs, what destroys it. None of that says the feature
    /// WORKS, because every interesting part of it is a wire between two
    /// components that compile perfectly whether or not they are joined:
    ///
    ///   * is there an EngineTemp on the player car in a BUILT scene at all,
    ///   * did the applier start it COLD, at the calendar's ambient, rather
    ///     than at the 18 C the component is constructed with,
    ///   * does the needle in the tach actually read it,
    ///   * does the heat's power cut reach the wheels (it is a SECOND
    ///     multiplier beside the fault one, added precisely because folding it
    ///     into the fault one would have it wiped by a bench press),
    ///   * does a seizure take the throttle off a real pad,
    ///   * and does the exit STAMP it, so the garage ever hears about any of it.
    ///
    /// Every one of those is invisible in a screenshot and silent in a compile.
    ///
    ///   tools\temp-play-check.ps1 -> PSXRacing_temp_play_check.txt
    /// </summary>
    public static class TempPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Engine Temperature (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;

            string id = System.Environment.GetEnvironmentVariable("PSX_TEMP_VENUE");
            if (string.IsNullOrEmpty(id)) id = "DragEighth";
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) index = i;

            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }

            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            // A JULY AFTERNOON, and the day is what makes it one. If the applier
            // does not read the calendar this engine starts at 18 and the first
            // assertion below says so.
            RaceHandoff.CalendarDay = LifeRules.DayNumber(new System.DateTime(1999, 7, 15));
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Afternoon;
            var cars = CarCatalog.All;
            if (cars.Count > 2)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id;
                RaceHandoff.OpponentSkills = "0.9";
            }
            // A car with a FINISHED radiator and hoses on the way out. Set on the
            // request, which is the only door the race scene has: if the applier
            // drops these the car out there has a new cooling system and nothing
            // below can be made to happen at all.
            RaceHandoff.RadiatorCond = 8f;
            RaceHandoff.FanCond = 100f;
            RaceHandoff.HoseCond = 10f;
            RaceHandoff.CoolantPct = 100f;
            RaceHandoff.EngineCond = 70f;

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TempPlayCheckRunner").AddComponent<TempPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE GAUGE IS WIRED TO THE ENGINE."
                                         : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath),
                             "PSXRacing_temp_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class TempPlayCheckRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();

            var rm = RaceManager.Instance;
            var car = rm != null ? rm.playerCar : null;
            var input = car != null ? car.GetComponent<PlayerCarInput>() : null;
            var temp = car != null ? car.GetComponent<EngineTemp>() : null;
            TempPlayCheck.Check(temp != null, "the player car carries an EngineTemp");
            if (rm == null || car == null || input == null || temp == null) { Done(); yield break; }

            // ---- the cold start, at the calendar's air -------------------
            float ambient = CoolingModel.AmbientC(RaceHandoff.CalendarDay, TimeOfDay.Current);
            TempPlayCheck.Check(ambient > 28f,
                                "a July afternoon is a hot day", Mathf.RoundToInt(ambient));
            TempPlayCheck.Check(Mathf.Abs(temp.ambientC - ambient) < 0.5f,
                                "and the engine is running in it, not in a constant 18",
                                Mathf.RoundToInt(temp.ambientC));
            TempPlayCheck.Check(temp.celsius < ambient + 25f,
                                "the engine starts cold", Mathf.RoundToInt(temp.celsius));
            TempPlayCheck.Check(temp.Gauge < 0.35f,
                                "so the needle is down at C", temp.Gauge.ToString("0.00"));

            // ---- the parts crossed with it -------------------------------
            TempPlayCheck.Check(temp.radiatorCond < 20f && temp.hoseCond < 20f,
                                "the car brought its knackered cooling system with it",
                                Mathf.RoundToInt(temp.radiatorCond) + "/" +
                                Mathf.RoundToInt(temp.hoseCond));

            // ---- the needle in the tach ----------------------------------
            // The cluster is built in Start and reads the model every frame.
            // The question is whether it is reading THIS model: park the
            // temperature somewhere unambiguous and ask the dial where its
            // needle went.
            var cluster = Object.FindFirstObjectByType<GaugeCluster>();
            if (cluster == null) TempPlayCheck.Note("no cluster in this scene");
            else
            {
                temp.celsius = EngineTemp.Normal;
                yield return Frames(3);
                float atNormal = cluster.SubNeedleFraction;
                temp.celsius = EngineTemp.RedMark + 10f;
                yield return Frames(3);
                float atHot = cluster.SubNeedleFraction;
                TempPlayCheck.Check(atNormal > 0.38f && atNormal < 0.48f,
                                    "at 90 C the coolant needle sits just below the middle",
                                    atNormal.ToString("0.000"));
                TempPlayCheck.Check(atHot > EngineTemp.RedFrac,
                                    "and past the damage line when the engine is in trouble",
                                    atHot.ToString("0.000"));
            }

            // ---- the power cut reaches the wheels ------------------------
            temp.celsius = EngineTemp.Normal;
            yield return Frames(3);
            TempPlayCheck.Check(car.heatAccelMult > 0.999f,
                                "a healthy engine is not handicapped",
                                car.heatAccelMult.ToString("0.00"));
            temp.celsius = EngineTemp.SeizeFromC - 1f;
            yield return Frames(3);
            TempPlayCheck.Check(car.heatAccelMult < 0.7f,
                                "and a cooking one is visibly down on power",
                                car.heatAccelMult.ToString("0.00"));
            TempPlayCheck.Check(Mathf.Abs(car.faultAccelMult - 1f) < 0.001f,
                                "without touching the fault handicap it multiplies with",
                                car.faultAccelMult.ToString("0.00"));

            // ---- the warning lamp ----------------------------------------
            // A needle is something you have to LOOK at. The lamp is the half
            // of a real cluster that finds the driver instead, so it has to
            // appear when the engine is in trouble, stay away when it is not,
            // and not land on top of the fuel bar it sits under.
            var hudFor = Object.FindFirstObjectByType<RaceHUD>();
            if (hudFor == null) TempPlayCheck.Note("no HUD in this scene");
            else
            {
                temp.celsius = EngineTemp.Normal;
                yield return Frames(3);
                var lampOff = hudFor.transform.Find("Temp");
                TempPlayCheck.Check(lampOff == null || !lampOff.gameObject.activeSelf,
                                    "at a normal temperature there is no warning at all");

                temp.celsius = EngineTemp.RedMark + 6f;
                yield return Frames(3);
                var lamp = hudFor.transform.Find("Temp");
                TempPlayCheck.Check(lamp != null && lamp.gameObject.activeSelf,
                                    "and a lamp the moment it is in the red");
                if (lamp != null && hudFor.fuelText != null)
                {
                    var lrt = (RectTransform)lamp;
                    var frt = hudFor.fuelText.rectTransform;
                    var lampText = lamp.GetComponent<Text>();
                    float lampTop = lrt.anchoredPosition.y +
                                    (lampText != null ? lampText.preferredHeight : 12f) * 0.5f;
                    // THE FUEL BAR IS UP ONLY WHEN NO DIAL CARRIES FUEL: the
                    // speedometer's needle and a bar at the top of the screen
                    // were the same reading twice (the owner, 2026-09-21).
                    var barBg = hudFor.fuelFill != null ? hudFor.fuelFill.parent : null;
                    bool barUp = barBg != null && barBg.gameObject.activeInHierarchy;
                    if (cluster != null && cluster.CarriesFuel)
                    {
                        TempPlayCheck.Check(!barUp && string.IsNullOrEmpty(hudFor.fuelText.text),
                                            "the speedometer carries fuel, so the top-of-screen fuel bar is gone",
                                            (barUp ? "bar up" : "bar down") + ", text '" + hudFor.fuelText.text + "'");
                        // And the lamp takes the fuel line's slot rather than
                        // hanging under the gap the bar left.
                        TempPlayCheck.Check(Mathf.Abs(lrt.anchoredPosition.y - frt.anchoredPosition.y) < 0.01f,
                                            "and the lamp moves up into the fuel line's slot",
                                            lrt.anchoredPosition.y.ToString("0.0"));
                    }
                    else
                    {
                        TempPlayCheck.Check(barUp, "no dial carries fuel here, so the fuel bar is up");
                        // Top of the lamp against the bottom of the fuel BAR,
                        // which is the thing between them: the bar sits 6 units
                        // tall at -42, so its lower edge is -45.
                        TempPlayCheck.Check(lampTop < -45f,
                                            "and it sits clear of the fuel bar above it",
                                            lampTop.ToString("0.0"));
                    }
                    TempPlayCheck.Check(Mathf.Abs(lrt.anchoredPosition.x - frt.anchoredPosition.x) < 0.01f,
                                        "in the same right-hand column as the fuel");
                    TempPlayCheck.Check(lampText != null && lampText.text.Contains("TEMP"),
                                        "saying TEMP and how hot",
                                        lampText != null ? lampText.text : "no text");
                }
            }

            // ---- and the coolant really is going out of it ---------------
            float had = temp.coolantPct;
            temp.celsius = CoolingModel.BoilAboveC + 20f;
            yield return Frames(30);
            TempPlayCheck.Check(temp.coolantPct < had,
                                "boiling it pushes coolant out",
                                had.ToString("0.0") + " -> " + temp.coolantPct.ToString("0.0"));
            TempPlayCheck.Check(temp.EngineDamage > 0f,
                                "and every second of it costs the engine",
                                temp.EngineDamage.ToString("0.000"));

            // ---- a seizure takes the car ---------------------------------
            // LET THE LIGHTS GO OUT FIRST. Everything above is read off the
            // model and the dial, which run on the grid; everything below is
            // about the CONTROLS, and on the grid the controls are the game's —
            // inputEnabled is false and the countdown owns the banner. Asserted
            // there, "the pad does nothing" and "the engine is dead" are the
            // same reading.
            float until = Time.realtimeSinceStartup + 15f;
            while (rm.State == RaceManager.RaceState.Countdown &&
                   Time.realtimeSinceStartup < until) yield return null;
            TempPlayCheck.Check(rm.State == RaceManager.RaceState.Racing,
                                "the race goes live", rm.State);
            TempPlayCheck.Check(input.inputEnabled, "and hands the player the car");

            Gamepad pad = null;
            try { pad = InputSystem.AddDevice<Gamepad>(); }
            catch (System.Exception e) { TempPlayCheck.Note("no virtual pad here: " + e.Message); }

            if (pad != null)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1f });
                InputSystem.Update();
                yield return Frames(4);
                TempPlayCheck.Check(car.throttleInput > 0.5f,
                                    "a pad opens the throttle on a running engine",
                                    car.throttleInput.ToString("0.00"));
                temp.Seize();
                yield return Frames(4);
                TempPlayCheck.Check(car.throttleInput < 0.01f,
                                    "and gets nothing at all out of a seized one",
                                    car.throttleInput.ToString("0.00"));
                // The wheel is NOT taken away. A car that dies at speed still
                // has to be steered off the road.
                InputSystem.QueueStateEvent(pad, new GamepadState
                {
                    rightTrigger = 1f,
                    leftStick = new Vector2(0.8f, 0f),
                });
                InputSystem.Update();
                yield return Frames(4);
                TempPlayCheck.Check(Mathf.Abs(car.steerInput) > 0.2f,
                                    "but the driver keeps the steering",
                                    car.steerInput.ToString("0.00"));
                InputSystem.QueueStateEvent(pad, new GamepadState { buttons = 0 });
                InputSystem.Update();
                InputSystem.RemoveDevice(pad);
            }
            else temp.Seize();

            // ---- and the banner says so ----------------------------------
            var hud = Object.FindFirstObjectByType<RaceHUD>();
            if (hud == null || hud.centerText == null) TempPlayCheck.Note("no HUD banner here");
            else
            {
                // The GO! flash holds the banner for its own second or two
                // after the lights, and it outranks every prompt by design.
                // Waiting it out is the honest way to read what comes next.
                until = Time.realtimeSinceStartup + 8f;
                while ((hud.centerText.text ?? "").StartsWith("GO") &&
                       Time.realtimeSinceStartup < until) yield return null;
                string banner = hud.centerText.text ?? "";
                TempPlayCheck.Check(banner.Contains("ENGINE"),
                                    "the banner tells the player what just happened",
                                    banner.Replace("\n", " / "));
            }

            // ---- the exit banks it ---------------------------------------
            // The one wire between a drive and a career. Without it the engine
            // is destroyed for as long as the scene is loaded and perfectly
            // healthy the moment the player gets home.
            RaceHandoff.ClearResult();
            EngineTemp.StampResult(car);
            TempPlayCheck.Check(RaceHandoff.HeatReported, "the exit stamps the heat");
            TempPlayCheck.Check(RaceHandoff.EngineSeized, "including that the engine let go");
            TempPlayCheck.Check(RaceHandoff.PeakCelsius > EngineTemp.RedMark,
                                "and how hot it got", Mathf.RoundToInt(RaceHandoff.PeakCelsius));
            TempPlayCheck.Check(RaceHandoff.HeatEngineDamage > 0f,
                                "and what that cost the engine",
                                RaceHandoff.HeatEngineDamage.ToString("0.00"));
            TempPlayCheck.Check(RaceHandoff.EndCoolantPct < 99f,
                                "and that the coolant is on the road",
                                Mathf.RoundToInt(RaceHandoff.EndCoolantPct));

            Done();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        void Done()
        {
            TempPlayCheck.Finish();
            EditorApplication.Exit(TempPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
