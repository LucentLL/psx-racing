using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// WALKING INTO YOUR OWN HOUSE, in the running game (2026-09-26).
    ///
    /// Owner: the walk-in "shouldn't take me to a different house and map than
    /// when choosing Drive", and "buildings should not let weather through the
    /// roof". The walk-in rooms now stand round the neighbourhood's copy of the
    /// house (GarageWorld, embedded) and a walk-in is an ON-FOOT arrival there
    /// (HomeWalk -> HomeArrival). This enters play mode exactly as HomeWalk
    /// hands over - a save with three cars and the lift, the handoff flags,
    /// the Neighborhood scene - and checks:
    ///
    ///   * the player is on foot on the drive, the car with the keys is in the
    ///     garage bay, the other two stand on the lawn and nothing on the drive;
    ///   * every bed offers SLEEP;
    ///   * snow stops being drawn indoors and comes back outside;
    ///   * the lift takes the REAL car up (and GET IN is refused while it is
    ///     up), and sets it back down where it was;
    ///   * GET IN puts the player back in the driving seat.
    ///
    ///   tools\homewalk-play-check.ps1 -> PSXRacing_homewalk_play_check.txt
    /// </summary>
    public static class HomeWalkPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            int idx = TrackCatalog.NeighborhoodSceneIndex;
            if (idx <= 0 || idx >= scenes.Length || !File.Exists(scenes[idx].path))
            {
                Check(false, "the neighbourhood is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }

            // A save: three cars at home, the keys on the first, the lift paid for.
            LifeSimManager.StartNewGame("Walk Check", 24, 0);
            var S = LifeSimManager.State;
            S.cars.Clear();
            S.activeCar = null;
            S.garageSlots = 6;
            S.money = 100000;
            var all = CarCatalog.All;
            CarMarket.MakeOwnedCar(S, all[Mathf.Min(5, all.Count - 1)], 80, 60000f, 4000);
            CarMarket.MakeOwnedCar(S, all[Mathf.Min(40, all.Count - 1)], 70, 90000f, 3000);
            CarMarket.MakeOwnedCar(S, all[Mathf.Min(90, all.Count - 1)], 60, 120000f, 2000);
            S.ActiveCar.fuel = 80f;
            Toolbox.Buy(S, Toolbox.Lift);
            LifeSimManager.Save();

            // What HomeWalk.Enter hands over, minus the LoadScene.
            var car = S.ActiveCar;
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.ArriveOnFoot = true;
            RaceHandoff.NoCar = false;
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
            new GameObject("HomeWalkCheckRunner").AddComponent<HomeWalkCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "ONE HOUSE: WALK IN, LOOK ROUND, DRIVE OUT." : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_homewalk_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class HomeWalkCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => HomeWalkPlayCheck.Check(ok, what, got);

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            // Frames, never WaitForEndOfFrame: batch mode has no game view.
            float t0 = Time.realtimeSinceStartup;
            while (!HomeArrival.Settled && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            for (int i = 0; i < 20; i++) yield return null;

            var S = LifeSimManager.State;
            var home = Object.FindFirstObjectByType<GarageWorld>();
            var fm = ForecourtMode.Instance;
            var arrival = Object.FindFirstObjectByType<HomeArrival>();
            Check(HomeArrival.Settled, "the arrival settled");
            Check(home != null && home.embedded && fm != null && arrival != null, "the street carries the walk-in rooms");
            if (home == null || fm == null || arrival == null) { Done(); yield break; }
            var car = home.liveCar;

            // ---- on foot, car in the garage ----
            Check(ForecourtMode.OnFoot, "the player arrives ON FOOT");
            var walker = GameObject.Find("Walker");
            Check(walker != null && walker.activeInHierarchy &&
                  Vector3.Distance(walker.transform.position, arrival.walkerStart.position) < 1.5f,
                  "standing on the drive, looking at the house",
                  walker != null ? Vector3.Distance(walker.transform.position, arrival.walkerStart.position).ToString("0.00") + " m" : "no walker");
            Vector3 d = car.transform.position - home.bays[0].position; d.y = 0f;
            Check(car.gameObject.activeInHierarchy && d.magnitude < 0.5f &&
                  Vector3.Angle(car.transform.forward, home.bays[0].forward) < 5f,
                  "the car with the keys is parked in the garage, nose out", d.magnitude.ToString("0.00") + " m");
            float carY = car.transform.position.y;
            Snap("1_arrival");
            Check(Mathf.Abs(car.speedKmh) < 1f, "and it is standing still", car.speedKmh.ToString("0.0") + " km/h");

            // ---- the others on the lawn, none on the drive ----
            int shells = 0, onDrive = 0;
            for (int i = 0; i < home.bays.Length; i++)
            {
                var bay = home.bays[i];
                if (bay == null) continue;
                bool has = bay.Find("Shell") != null;
                if (has) shells++;
                if (has && i <= 1) onDrive++;
            }
            Check(shells == 2 && onDrive == 0, "the other two cars stand on the lawn, the drive left clear",
                  shells + " parked, " + onDrive + " in the garage or on the drive");

            // ---- beds ----
            var targets = new List<FootTarget>(Object.FindObjectsByType<FootTarget>(FindObjectsSortMode.None));
            int sleep = 0;
            foreach (var t in targets) if (t.action == "SLEEP" && t.verb == "SLEEP") sleep++;
            Check(sleep == home.beds.Length && sleep > 0, "every bed offers SLEEP", sleep + "/" + home.beds.Length);

            // ---- snow stays outside ----
            WeatherFx.Set(Weather.Snow);
            for (int i = 0; i < 10; i++) yield return null;
            var cc = walker.GetComponent<CharacterController>();
            var bed = home.beds[0];
            Vector3 outside = walker.transform.position;
            // Inside the house on the ground floor: the garage, beside the car.
            // Just inside the open door, in front of the car's nose, looking
            // out - so the picture has both: dry in here, snowing out there.
            Place(walker, cc, home.bays[0].position + home.bays[0].forward * 2.3f + Vector3.up * 0.1f);
            walker.transform.rotation = Quaternion.LookRotation(home.bays[0].forward, Vector3.up);
            walker.GetComponent<FirstPersonWalk>()?.SnapYawToTransform();
            yield return new WaitForSecondsRealtime(1.5f);
            Snap("2_garage_in_snow");
            var camNow = Camera.main;
            string why = "no main camera";
            if (camNow != null)
            {
                Vector3 e = camNow.transform.position;
                why = "eye " + e.ToString("0.0") + ", walker " + walker.transform.position.ToString("0.0") +
                      ", bed " + bed.position.ToString("0.0");
                if (Physics.Raycast(e + Vector3.up * 0.05f, Vector3.up, out var roofHit, 25f, ~(1 << 2),
                                    QueryTriggerInteraction.Ignore))
                    why += ", up hits " + roofHit.collider.name + " at " + roofHit.distance.ToString("0.0") + " m";
                else why += ", nothing overhead";
                why += ", walk " + (FirstPersonWalk.Current != null ? FirstPersonWalk.Current.isActiveAndEnabled.ToString() : "none");
            }
            Check(WeatherFx.Covered, "indoors, the lens is under the roof", why);
            // AND NO FLAKE IS INSIDE THE HOUSE, while it goes on snowing
            // outside the door.
            int inside = 0, outsideN = 0;
            var fx = GameObject.Find("WeatherFx");
            var ps = fx != null ? fx.GetComponent<ParticleSystem>() : null;
            if (ps != null)
            {
                var parts = new ParticleSystem.Particle[ps.main.maxParticles];
                int n = ps.GetParticles(parts);
                for (int i = 0; i < n; i++)
                {
                    if (parts[i].remainingLifetime <= 0f) continue;
                    if (WeatherShelter.Contains(parts[i].position)) inside++; else outsideN++;
                }
            }
            Check(ps != null && inside == 0 && outsideN > 50,
                  "no snow falls inside the house, and it keeps falling outside",
                  inside + " flakes indoors, " + outsideN + " outside");
            Place(walker, cc, outside);
            walker.transform.rotation = Quaternion.LookRotation(-home.bays[0].forward, Vector3.up);
            walker.GetComponent<FirstPersonWalk>()?.SnapYawToTransform();
            yield return new WaitForSecondsRealtime(1.5f);
            Snap("3_drive_in_snow");
            Check(!WeatherFx.Covered, "back outside, it is snowing again");
            WeatherFx.Set(Weather.Clear);

            // ---- the lift takes the real car up ----
            FootTarget rig = null;
            foreach (var t in home.bays[0].GetComponentsInChildren<FootTarget>(true))
                if (t.name == "RaiseHook") rig = t;
            Check(rig != null && rig.gameObject.activeInHierarchy && rig.action == "RAISE IT ON THE LIFT",
                  "the lift is offered for the car in the garage", rig != null ? rig.action : "no hook");
            if (rig != null && rig.onUse != null)
            {
                rig.onUse();
                yield return new WaitForSecondsRealtime(3.2f);
                var rb = car.GetComponent<Rigidbody>();
                float up = car.transform.position.y - carY;
                Check(rb.isKinematic && up > 1.4f, "the REAL car goes up on the lift", up.ToString("0.00") + " m");
                string no = ForecourtMode.GetInRefusal != null ? ForecourtMode.GetInRefusal() : null;
                Check(no != null, "and nobody drives it off the lift", no ?? "get in allowed");
                rig.onUse();
                yield return new WaitForSecondsRealtime(3.2f);
                for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
                float back = car.transform.position.y - carY;
                Vector3 drift = car.transform.position - home.bays[0].position; drift.y = 0f;
                Check(!rb.isKinematic && Mathf.Abs(back) < 0.06f && drift.magnitude < 0.5f,
                      "and sets it back down where it stood", back.ToString("0.000") + " m high, " +
                      drift.magnitude.ToString("0.00") + " m off the bay");
            }

            // ---- and drive ----
            FootTarget carTarget = null;
            foreach (var t in car.GetComponentsInChildren<FootTarget>(true)) carTarget = t;
            Check(carTarget != null && carTarget.action == "GET IN AND DRIVE" &&
                  carTarget.title.Contains("YOURS") && !string.IsNullOrEmpty(carTarget.action2),
                  "your car says whose it is, GET IN, and INSPECT", carTarget != null ? carTarget.title : "no target");
            if (carTarget != null && carTarget.onUse != null)
            {
                carTarget.onUse();
                yield return new WaitForSecondsRealtime(2.5f);
                var input = car.GetComponent<PlayerCarInput>();
                Check(!ForecourtMode.OnFoot && input != null && input.inputEnabled,
                      "GET IN puts you in the driving seat");
            }
            Done();
        }

        /// <summary>What the player's camera sees, to Screenshots/homewalk_*.png.</summary>
        static void Snap(string name)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
            {
                req.destination = rt;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "homewalk_" + name + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            rt.Release();
            Object.Destroy(rt);
        }

        static void Place(GameObject walker, CharacterController cc, Vector3 at)
        {
            if (cc != null) cc.enabled = false;
            walker.transform.position = at;
            if (cc != null) cc.enabled = true;
        }

        static void Done()
        {
            HomeWalkPlayCheck.Finish();
            EditorApplication.Exit(HomeWalkPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
