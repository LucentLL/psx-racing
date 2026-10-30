using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// FILLING UP IN CHARLOTTE (city fuel r1, PHASE B): FuelPlayCheck's
    /// refuel, at one of the city's own filling stations. Free roam, the car
    /// put down beside a pump of the plainest station nearest uptown (one
    /// canopy module, the shop behind it, not steep, not a misfit) once its
    /// tiles are built whole (EnsureRing: the station stands with them):
    /// the station stood its pumps and its door; parked beside a pump the
    /// FUEL button is offered; out, at the pump, it says FILL UP; pressed, the
    /// tank rises (and, with a career, the money goes); STOP stops it; and,
    /// with a career, the shop's GO INSIDE prompt hangs at its door (the CITY
    /// edition has none: no career, the pumps fill free).
    ///
    ///   tools\city-fuel-play-check.ps1 -> PSXRacing_city_fuel_play_check.txt
    /// </summary>
    public static class CityFuelPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            log.AppendLine("edition " + Edition.Name(Edition.Current));
            int roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int si = roamIdx >= 0 ? TrackCatalog.SceneIndex(roamIdx) : -1;
            if (si < 0 || si >= scenes.Length || !File.Exists(scenes[si].path))
            { Check(false, "Charlotte's free roam is built"); Finish(); EditorApplication.Exit(1); return; }

            if (Edition.HasCareer)
            {
                // the town check's career car, a fifth of a tank, money to pay
                LifeSimManager.StartNewGame("City Fuel Check", 24, 0);
                var S = LifeSimManager.State;
                S.cars.Clear(); S.activeCar = null; S.money = 5000;
                CarMarket.MakeOwnedCar(S, CarCatalog.All[Mathf.Min(5, CarCatalog.All.Count - 1)], 80, 60000f, 4000);
                S.ActiveCar.fuel = 20f;
                LifeSimManager.Save();
                var car = S.ActiveCar;
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.FreeRoam = true;
                RaceHandoff.TrackIndex = roamIdx;
                RaceHandoff.TimeOfDayIndex = TimeOfDay.Afternoon;
                RaceHandoff.CarId = car.id;
                RaceHandoff.CarSpecId = car.specId;
                RaceHandoff.StartFuelPct = car.fuel;
            }
            else
            {
                Check(CityFrontEnd.FillFreeRoam(CityFrontEnd.DefaultCarId(), TimeOfDay.Afternoon, 0, out int scene) && scene == si,
                      "CITY front end: FREE ROAM fills the handoff for the Charlotte scene");
                RaceHandoff.StartFuelPct = 20f;
            }

            EditorSceneManager.OpenScene(scenes[si].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("CityFuelCheckRunner").AddComponent<CityFuelCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "PULLED IN AT A CHARLOTTE STATION, GOT OUT, FILLED UP." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_city_fuel_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class CityFuelCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => CityFuelPlayCheck.Check(ok, what, got);
        const float HangSeconds = 300f;
        float bornAt;
        bool finished;

        void Awake() => bornAt = Time.realtimeSinceStartup;

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true;
            Check(false, "HUNG: no end after " + HangSeconds.ToString("0") + " s of play");
            CityFuelPlayCheck.Finish();
            EditorApplication.Exit(1);
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 30; i++) yield return null;
            var world = CityWorld.Active;
            var fm = ForecourtMode.Instance;
            var car = fm != null ? fm.playerCar : null;
            Check(world != null && world.Map != null, "Charlotte streams its world");
            Check(fm != null && car != null, "Charlotte has a forecourt and a car (its GameSystems' ForecourtMode)");
            if (world == null || world.Map == null || fm == null || car == null) { Done(); yield break; }
            var map = world.Map;
            Check(map.stations.Length > 0, "the filling stations are installed", CityStations.LastReport);
            Check(!map.stationsStale, "their file matches the city's graph");
            if (map.stations.Length == 0) { Done(); yield break; }

            // the plainest station nearest uptown
            CityStations.Station st = null; float best = float.MaxValue;
            foreach (var s in map.stations)
            {
                if (s.cls != CityStations.ClsForecourt || s.layout != CityStations.LayShopBehind || s.modules != 1) continue;
                if ((s.flags & (CityStations.FSteep | CityStations.FMisfit | CityStations.FCorner)) != 0) continue;
                float d = (s.centre - map.uptown).sqrMagnitude;
                if (d < best) { best = d; st = s; }
            }
            Check(st != null, "a plain forecourt to test at");
            if (st == null) { Done(); yield break; }
            CityFuelPlayCheck.log.AppendLine($"  note station {st.index} ({st.OsmName}) lot at ({st.centre.x:0}, {st.centre.y:0}), " +
                                             $"{Mathf.Sqrt(best):0} m from uptown, {st.drives.Length} driveways, {st.replaced.Length} boxes replaced");

            // its tiles built whole: the station stands with them. NO FRAME
            // between this and the car's teleport beside the pump: the car is
            // still where the scene began (1.6 km off), and the streaming's
            // next Update drops every tile outside its ring - the station and
            // its pumps with them (the first run, 2026-10-30: "0 pumps within
            // 20 m"). The new colliders synced for the parking search.
            var lot = new Vector3(st.centre.x, 0f, st.centre.y);
            world.EnsureRing(lot, 1);
            Physics.SyncTransforms();
            var canopyAt = st.World(0f, CityStations.CanV0 + 0.5f * CityStations.CanD);
            GasPump pump = null; float pd = float.MaxValue;
            int near = 0;
            foreach (var p in GasPump.All)
            {
                if (p == null) continue;
                float d = (new Vector2(p.transform.position.x, p.transform.position.z) - canopyAt).magnitude;
                if (d < 20f) near++;
                if (d < pd) { pd = d; pump = p; }
            }
            Check(pump != null && pd < 12f, "the station stood its pumps under its canopy", near + " pumps within 20 m, nearest " + pd.ToString("0.0") + " m");
            Check(StoreDoor.Nearest(lot, 60f) != null, "and its shop door marker");
            if (pump == null || pd >= 12f) { Done(); yield break; }

            // PARK BESIDE IT (FuelPlayCheck's search: a car-sized box touching nothing solid)
            Vector3 at = pump.transform.position; Quaternion park = pump.transform.rotation;
            bool found = false;
            foreach (float d in new[] { 2.6f, 3.0f, 3.4f })
                foreach (var dir in new[] { pump.transform.forward, -pump.transform.forward, pump.transform.right, -pump.transform.right })
                {
                    if (found) break;
                    Vector3 p = pump.transform.position + dir * d;
                    if (!Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var gh, 20f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                    Quaternion q = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
                    if (Physics.CheckBox(gh.point + Vector3.up * 0.9f, new Vector3(1f, 0.6f, 2.3f), q, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                    at = gh.point + Vector3.up * 0.4f; park = q; found = true;
                }
            Check(found, "there is room to park beside the pump (along its island)");
            car.TeleportTo(at, park);
            car.handbrakeInput = true;
            var input = car.GetComponent<PlayerCarInput>();
            float tw = Time.realtimeSinceStartup;
            while (input != null && !input.inputEnabled && Time.realtimeSinceStartup - tw < 15f) yield return null;
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            yield return null;
            Check(GasPump.AtPump, "the car is parked at the pump");
            Check(ForecourtMode.OfferFuel, "and the FUEL button is offered from the driver's seat", ForecourtMode.Prompt ?? "no prompt");

            // OUT, the game's own way, then to the pump that claimed the car
            var tank = car.GetComponent<FuelTank>();
            float pct0 = tank != null ? tank.percent : 0f;
            int money0 = Edition.HasCareer ? LifeSimManager.State.money : 0;
            var getOut = typeof(ForecourtMode).GetMethod("GetOut", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fm.StartCoroutine((IEnumerator)getOut.Invoke(fm, null));
            float tg = Time.realtimeSinceStartup;
            while (!ForecourtMode.OnFoot && Time.realtimeSinceStartup - tg < 5f) yield return null;
            Check(ForecourtMode.OnFoot, "the driver got out");
            var walkerGo = GameObject.Find("Walker");
            Transform claimed = pump.transform; float nb = float.MaxValue;
            if (walkerGo != null)
                foreach (var gp in GasPump.All)
                {
                    if (gp == null) continue;
                    float dd = (gp.transform.position - walkerGo.transform.position).sqrMagnitude;
                    if (dd < nb) { nb = dd; claimed = gp.transform; }
                }
            if (walkerGo != null)
            {
                var wcc = walkerGo.GetComponent<CharacterController>();
                Vector3 nose = car.transform.position + car.transform.forward * 3.4f +
                               (walkerGo.transform.position - car.transform.position).normalized * 0.5f;
                foreach (var goal in new[] { nose, claimed.position })
                    for (int stp = 0; stp < 160; stp++)
                    {
                        Vector3 to = goal - walkerGo.transform.position; to.y = 0f;
                        if (to.magnitude < (goal == nose ? 0.3f : 1.5f)) break;
                        if (wcc != null) wcc.Move(to.normalized * 0.06f + Vector3.down * 0.05f);
                        yield return new WaitForFixedUpdate();
                    }
                Vector3 look = claimed.position - walkerGo.transform.position; look.y = 0f;
                if (look.sqrMagnitude > 1e-4f) walkerGo.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
                walkerGo.GetComponent<FirstPersonWalk>()?.SnapYawToTransform();
            }
            for (int i = 0; i < 20; i++) yield return null;
            var interactor = walkerGo != null ? walkerGo.GetComponent<FootInteractor>() : null;
            var current = interactor != null ? interactor.Current : null;
            Check(current != null && current.name == "PumpTarget" && current.action == "FILL UP",
                  "looking at the pump, it offers FILL UP", current != null ? current.name + " / " + current.action : "nothing");
            Check(GasPump.WalkerAtNozzle, "and the walker is at the nozzle");
            if (current != null && current.onUse != null && tank != null)
            {
                current.onUse();
                yield return new WaitForSecondsRealtime(2.5f);
                bool paid = !Edition.HasCareer || LifeSimManager.State.money < money0;
                Check(tank.percent > pct0 + 3f && paid,
                      Edition.HasCareer ? "FILL UP runs the pump: the tank rises and the money goes" : "FILL UP runs the pump (free: no career)",
                      pct0.ToString("0") + "% -> " + tank.percent.ToString("0") + "%" + (Edition.HasCareer ? ", $" + money0 + " -> $" + LifeSimManager.State.money : ""));
                Check(current.action == "STOP FUELLING", "and the prompt offers STOP", current.action);
                current.onUse();
                yield return new WaitForSecondsRealtime(0.5f);
                float held = tank.percent;
                yield return new WaitForSecondsRealtime(1f);
                Check(Mathf.Abs(tank.percent - held) < 0.01f, "STOP stops it", held.ToString("0.0") + " -> " + tank.percent.ToString("0.0"));
            }
            // the shop's door: GO INSIDE with a career, nothing without one
            FootTarget storeT = null;
            foreach (var ft in Object.FindObjectsByType<FootTarget>(FindObjectsInactive.Include))
                if (ft.name == "StoreTarget") { storeT = ft; break; }
            if (Edition.HasCareer)
                Check(storeT != null && storeT.transform.parent != null && storeT.transform.parent.GetComponent<StoreDoor>() != null &&
                      (storeT.transform.position - lot).magnitude < 60f,
                      "the shop's GO INSIDE prompt hangs at this station's door", storeT != null ? storeT.action : "none");
            else
                Check(storeT == null, "CITY edition: no GO INSIDE (no career)");
            Done();
        }

        void Done()
        {
            if (finished) return;
            finished = true;
            CityFuelPlayCheck.Finish();
            EditorApplication.Exit(CityFuelPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
