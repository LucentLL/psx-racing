using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// FILLING UP, in the running game (2026-09-26). Owner: "I'm unable to fill
    /// up gas on mobile or PC. I should be prompted when looking at the gas
    /// pump when car is parked near it." Two faults: from the driver's seat a
    /// phone was told "TAP FUEL (TOP RIGHT)" with no button there (the HUD
    /// showed FUEL only when the pump had a prompt, and it has none until
    /// someone stands at it), and on foot the pump's prompt anchor sat inside
    /// the pump's own solid, which the sight test did not forgive - no prompt
    /// from anywhere, so the nozzle never counted as held.
    ///
    /// In the town: park at a pump, the FUEL button is offered; stand at the
    /// pump looking at it, it says FILL UP; press it, the tank rises and the
    /// money goes; walk away and it stops.
    ///
    ///   tools\fuel-play-check.ps1 -> PSXRacing_fuel_play_check.txt
    /// </summary>
    public static class FuelPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            int idx = TrackCatalog.TownSceneIndex;
            if (idx <= 0 || idx >= scenes.Length || !File.Exists(scenes[idx].path))
            { Check(false, "the town is built"); Finish(); EditorApplication.Exit(1); return; }

            LifeSimManager.StartNewGame("Fuel Check", 24, 0);
            var S = LifeSimManager.State;
            S.cars.Clear(); S.activeCar = null; S.money = 5000;
            CarMarket.MakeOwnedCar(S, CarCatalog.All[Mathf.Min(5, CarCatalog.All.Count - 1)], 80, 60000f, 4000);
            S.ActiveCar.fuel = 20f;
            LifeSimManager.Save();
            var car = S.ActiveCar;
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Afternoon;
            RaceHandoff.CarId = car.id;
            RaceHandoff.CarSpecId = car.specId;
            RaceHandoff.StartFuelPct = car.fuel;
            LifeHomeScreen.FillCarRequestFor(S, car);

            EditorSceneManager.OpenScene(scenes[idx].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("FuelCheckRunner").AddComponent<FuelCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "PULL IN, GET OUT, FILL UP." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_fuel_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class FuelCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => FuelPlayCheck.Check(ok, what, got);

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 30; i++) yield return null;
            var fm = ForecourtMode.Instance;
            var car = fm != null ? fm.playerCar : null;
            Check(fm != null && car != null, "the town has a forecourt and a car");
            if (fm == null || car == null) { Done(); yield break; }

            // The pump nearest the town's start, its fuelling volume.
            GasPump pump = null; float best = float.MaxValue;
            foreach (var p in Object.FindObjectsByType<GasPump>(FindObjectsSortMode.None))
            {
                float d = (p.transform.position - car.transform.position).sqrMagnitude;
                if (d < best) { best = d; pump = p; }
            }
            Check(pump != null, "there is a pump in town");
            if (pump == null) { Done(); yield break; }

            // PARK BESIDE IT: the first clear spot 2.5-3.5 m from the pump, on
            // either side, either axis, where a car-sized box touches nothing
            // solid - the way a player pulls up to an island.
            var box = pump.GetComponent<BoxCollider>();
            Vector3 at = pump.transform.position; Quaternion park = pump.transform.rotation;
            bool found = false;
            foreach (float d in new[] { 2.6f, 3.0f, 3.4f })
                foreach (var dir in new[] { pump.transform.right, -pump.transform.right, pump.transform.forward, -pump.transform.forward })
                {
                    if (found) break;
                    Vector3 p = pump.transform.position + dir * d;
                    if (!Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var gh, 20f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                    Quaternion q = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
                    if (Physics.CheckBox(gh.point + Vector3.up * 0.9f, new Vector3(1f, 0.6f, 2.3f), q, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                    at = gh.point + Vector3.up * 0.4f; park = q; found = true;
                }
            Check(found, "there is room to park beside the pump");
            car.TeleportTo(at, park);
            car.handbrakeInput = true;
            // The player has the car once the town hands it over.
            var input = car.GetComponent<PlayerCarInput>();
            float tw = Time.realtimeSinceStartup;
            while (input != null && !input.inputEnabled && Time.realtimeSinceStartup - tw < 15f) yield return null;
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            yield return null;
            Check(GasPump.AtPump, "the car is parked at the pump");
            Check(ForecourtMode.OfferFuel, "and the FUEL button is offered from the driver's seat",
                  ForecourtMode.Prompt ?? "no prompt");
            {
                var ci = car.GetComponent<PlayerCarInput>();
                FuelPlayCheck.log.AppendLine("  note input " + (ci != null ? ci.inputEnabled.ToString() : "none") +
                    ", speed " + car.speedKmh.ToString("0.0") + ", pause " + PauseMenu.IsOpen + ", tank " +
                    car.GetComponent<FuelTank>()?.percent.ToString("0") + ", anywhere " + fm.anywhereInTown +
                    ", onfoot " + ForecourtMode.OnFoot + ", fm enabled " + fm.isActiveAndEnabled);
            }
            var touch = TouchControls.Instance;
            if (touch != null)
            {
                var btnField = typeof(TouchControls).GetField("actionBtn", BindingFlags.NonPublic | BindingFlags.Instance);
                var btn = btnField != null ? btnField.GetValue(touch) as Component : null;
                var label = btn != null ? btn.GetComponentInChildren<UnityEngine.UI.Text>(true) : null;
                Check(btn != null && btn.gameObject.activeSelf && label != null && label.text == "FUEL",
                      "the touch button is there, and says FUEL", label != null ? label.text : "none");
            }

            // OUT, standing at the pump and looking at it.
            var tank = car.GetComponent<FuelTank>();
            float pct0 = tank.percent;
            int money0 = LifeSimManager.State.money;
            // OUT, the game's own way (engine off, door, walker beside it), then
            // turned to face the pump the car claimed.
            var getOut = typeof(ForecourtMode).GetMethod("GetOut", BindingFlags.NonPublic | BindingFlags.Instance);
            fm.StartCoroutine((IEnumerator)getOut.Invoke(fm, null));
            float tg = Time.realtimeSinceStartup;
            while (!ForecourtMode.OnFoot && Time.realtimeSinceStartup - tg < 5f) yield return null;
            var walkerGo = GameObject.Find("Walker");
            // Facing the pump nearest where the driver got out - whichever
            // island volume claimed the car.
            Transform claimed = pump.transform; float nb = float.MaxValue;
            if (walkerGo != null)
                foreach (var gp in Object.FindObjectsByType<GasPump>(FindObjectsSortMode.None))
                {
                    float dd = (gp.transform.position - walkerGo.transform.position).sqrMagnitude;
                    if (dd < nb) { nb = dd; claimed = gp.transform; }
                }
            if (walkerGo != null)
            {
                // Walk to it, as a player would: straight at it until a stride
                // and a half off, or something stops you.
                var wcc = walkerGo.GetComponent<CharacterController>();
                // Round the car: past its nose first, then to the pump.
                Vector3 nose = car.transform.position + car.transform.forward * 3.4f +
                               (walkerGo.transform.position - car.transform.position).normalized * 0.5f;
                foreach (var goal in new[] { nose, claimed.position })
                    for (int st = 0; st < 160; st++)
                    {
                        Vector3 to = goal - walkerGo.transform.position; to.y = 0f;
                        if (to.magnitude < (goal == nose ? 0.3f : 1.5f)) break;
                        if (wcc != null) wcc.Move(to.normalized * 0.06f + Vector3.down * 0.05f);
                        yield return new WaitForFixedUpdate();
                    }
                Vector3 look = claimed.position - walkerGo.transform.position; look.y = 0f;
                walkerGo.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
            var walker = GameObject.Find("Walker");
            walker?.GetComponent<FirstPersonWalk>()?.SnapYawToTransform();
            for (int i = 0; i < 20; i++) yield return null;
            var interactor = walker != null ? walker.GetComponent<FootInteractor>() : null;
            foreach (var ft in Object.FindObjectsByType<FootTarget>(FindObjectsSortMode.None))
            {
                if (ft.name != "PumpTarget" && ft.name != "CarTarget") continue;
                var eye = Camera.main != null ? Camera.main.transform : walker.transform;
                Vector3 to = ft.FocusPoint - eye.position;
                FuelPlayCheck.log.AppendLine("  note " + ft.name + " under " + (ft.transform.parent ? ft.transform.parent.name : "-") +
                    " at " + ft.FocusPoint.ToString("0.0") + ", dist " + to.magnitude.ToString("0.0") + " (range " + ft.range +
                    "), angle " + Vector3.Angle(eye.forward, to).ToString("0") + ", active " + ft.isActiveAndEnabled +
                    ", los " + ft.requireLineOfSight + ", action '" + ft.action + "'");
            }
            FuelPlayCheck.log.AppendLine("  note pump volume at " + pump.transform.position.ToString("0.0") + ", walker at " + walker.transform.position.ToString("0.0"));
            var current = interactor != null ? interactor.Current : null;
            Check(current != null && current.name == "PumpTarget" && current.action == "FILL UP",
                  "looking at the pump, it offers FILL UP", current != null ? current.name + " / " + current.action : "nothing");
            Check(GasPump.WalkerAtNozzle, "and the walker is at the nozzle");

            if (current != null && current.onUse != null)
            {
                current.onUse();
                yield return new WaitForSecondsRealtime(2.5f);
                Check(tank.percent > pct0 + 3f && LifeSimManager.State.money < money0,
                      "FILL UP runs the pump: the tank rises and the money goes",
                      pct0.ToString("0") + "% -> " + tank.percent.ToString("0") + "%, $" + money0 + " -> $" + LifeSimManager.State.money);
                Check(current.action == "STOP FUELLING", "and the prompt offers STOP", current.action);
                current.onUse();
                yield return new WaitForSecondsRealtime(0.5f);
                float held = tank.percent;
                yield return new WaitForSecondsRealtime(1f);
                Check(Mathf.Abs(tank.percent - held) < 0.01f, "STOP stops it", held.ToString("0.0") + " -> " + tank.percent.ToString("0.0"));
            }
            Done();
        }

        static void Done()
        {
            FuelPlayCheck.Finish();
            EditorApplication.Exit(FuelPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
