using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LEFT CAR (owner 2026-11-11: "I parked my Honda at a car meet and it
    /// rolled away, lights on, front suspension lifted like a monster truck").
    ///
    /// In the running Town on a SAT NIGHT (the meet is on, the roster parked),
    /// for each car: swap it in (RaceHandoffApplier.SwapCar), stand it in a free
    /// stall of the Eastside Lot - and on a 15% slope well away from the town -
    /// let it settle with the driver in and no input (the DRIVING-AT-REST
    /// reference), put the box in R (the owner's HUD said "AT R": he had
    /// backed into the bay), get out the real way (ForecourtMode.GetOut), and
    /// record every second for PSX_PARK_SECONDS (30): drift in the plane,
    /// speed, gear, lever, the solver's hold, the lamps (CarLights.LitNow), and
    /// each tyre's room under its arch as drawn (mm) against the at-rest
    /// reference. Then back in, and the largest speed in the next 3 s (a jump).
    ///
    /// Runs the whole list twice: CarController.LeftCarRules off (the car as
    /// it was) and on. Only the ON rows grade; the OFF rows are the before.
    ///   PSX_PARK_CARS   body keys, comma list (default: the six updated + six older)
    ///   PSX_PARK_SHOTS  body keys to photograph side-on at 0 s and at the end
    ///   PSX_PARK_MODES  before,after (default both)
    ///   tools\parked-play-check.ps1 -> PSXRacing_parked_play_check.txt,
    ///   Screenshots\Parked\*.png
    /// </summary>
    public static class ParkedPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        public static readonly string[] DefaultCars =
        {
            // the 2026-11-11 batch
            "civic_eg", "roadster_99", "rx7_fd_93", "viper_gts", "flatsix_coupe", "flatsix_coupe_87", "rally_sedan_99",
            // older shells
            "skyline_r32", "supra_a80", "euro_hatch", "charger_69", "nissan_180sx", "hatch_83_popup",
        };

        internal static string[] Env(string name, string[] fallback)
        {
            var v = System.Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(v)) return fallback;
            return v.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            int ti = TrackCatalog.TownSceneIndex;
            if (ti <= 0 || ti >= scenes.Length || !File.Exists(scenes[ti].path))
            { Check(false, "the Town scene is built"); Finish(); EditorApplication.Exit(1); return; }

            LifeSimManager.StartNewGame("Parked Check", 24, LifeRules.DefaultJobIndex);
            var S = LifeSimManager.State;
            S.money = 50000; S.health = 100f; S.debugMode = true;
            S.day = 2; S.slotIndex = LifeRules.NightSlot;       // SAT NIGHT: a meet
            S.cars.Clear(); S.activeCar = null;
            var first = SpecFor(Env("PSX_PARK_CARS", DefaultCars)[0]) ?? CarCatalog.All[0];
            var owned = CarMarket.MakeOwnedCar(S, first, 90, 3000f, first.price);
            owned.fuel = 90f;
            S.activeCar = owned.id;
            LifeSimManager.Save();

            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = S.ActiveCar.specId;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.ForSlot(S.slotIndex, S.day);
            RaceHandoff.StartFuelPct = S.ActiveCar.fuel;
            LifeHomeScreen.FillCarRequestFor(S);
            LifeSimManager.Save();

            EditorSceneManager.OpenScene(scenes[ti].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// EVERY CATALOGUE ROW, no play mode: where the drawn tyre meets its
        /// arch (ArchCeilings.HubMax: the exact ArchHubs row for this shell on
        /// this sheet, else the blunter field) against where the physics holds
        /// the hub at rest (StockHubRest +- half the sheet's rake and tyre
        /// split). A hub at rest above its arch is a shell CarBodyLean seats
        /// high - the nose-up the owner saw - so every row must have room.
        /// Writes PSXRacing_arch_sweep.txt.
        /// </summary>
        public static void ArchSweep()
        {
            var sb = new StringBuilder();
            int bad = 0, field = 0, rows = 0;
            sb.AppendLine("row | shell | source | hubMax F / R mm | hub at rest F / R mm | room F / R mm");
            foreach (var spec in CarCatalog.All)
            {
                var def = CarShell.DefFor(spec);
                if (def == null) continue;
                rows++;
                var f = CarModelLibrary.Fit(def, spec);
                bool exact = ArchHubs.TryShellHubMax(f, 0, 0f, out _);
                float hF = ArchCeilings.HubMax(def, f, 0), hR = ArchCeilings.HubMax(def, f, 2);
                float split = CarController.RideRakeOf(spec) + (f.radiusF - f.radiusR);
                float restF = CarController.StockHubRest + 0.5f * split, restR = CarController.StockHubRest - 0.5f * split;
                // the physics' hub is the same height whatever the tyre: the mounts
                // stand on rest + r, so the hub rides at StockHubRest less its sag share
                float roomF = hF - restF, roomR = hR - restR;
                bool low = roomF < 0f || roomR < 0f;
                if (!exact) field++;
                if (low) bad++;
                if (low || !exact)
                    sb.AppendLine(string.Format("{0} | {1} | {2} | {3:0} / {4:0} | {5:0} / {6:0} | {7:0} / {8:0}{9}",
                        spec.id, def.key, exact ? "exact" : "FIELD", hF * 1000f, hR * 1000f, restF * 1000f, restR * 1000f,
                        roomF * 1000f, roomR * 1000f, low ? "   <- SEATED HIGH AT REST" : ""));
            }
            sb.Insert(0, string.Format("{0} rows; {1} on the field (no exact ArchHubs row for their sheet); {2} with a tyre over its arch at rest\n", rows, field, bad));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_arch_sweep.txt"), sb.ToString());
            Debug.Log(sb.ToString());
        }

        /// <summary>The first road car in the catalogue that wears shell
        /// <paramref name="key"/>.</summary>
        internal static CarSpec SpecFor(string key)
        {
            // The rows the owner drives / the sweep's worst for each new shell
            // (PSXRacing_arch_sweep.txt), else the first road car wearing it.
            var pick = new Dictionary<string, string>
            {
                { "civic_eg", "honda_civic_sir_ii__eg___91" }, { "rx7_fd_93", "mazda_rx_7_type_rz__fd__j___92" },
                { "flatsix_coupe", "ruf_btr__86" }, { "flatsix_coupe_87", "ruf_ctr__yellow_bird___87" },
                { "viper_gts", "dodge_viper_gts__99" }, { "euro_hatch", "honda_cr_x_sir__90" },
            };
            foreach (var kv in Env("PSX_PARK_SPECS", new string[0]))
            {
                int eq = kv.IndexOf('=');
                if (eq > 0) pick[kv.Substring(0, eq)] = kv.Substring(eq + 1);
            }
            if (pick.TryGetValue(key, out var id) && CarCatalog.Get(id) != null) return CarCatalog.Get(id);
            CarSpec any = null;
            foreach (var c in CarCatalog.All)
            {
                var d = CarShell.DefFor(c);
                if (d == null || d.key != key) continue;
                if (!c.IsRaceCar && !c.IsRallyCar) return c;
                if (any == null) any = c;
            }
            return any;
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("ParkedCheckRunner").AddComponent<ParkedCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE LEFT CAR STAYS WHERE IT WAS LEFT, DARK, ON ITS OWN RIDE HEIGHT."
                                         : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_parked_play_check.txt"),
                              log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class ParkedCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => ParkedPlayCheck.Check(ok, what, got);
        static StringBuilder Log => ParkedPlayCheck.log;
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const float SpeedUp = 4f;
        float bornAt;
        bool finished;

        ForecourtMode fm;
        CarController car;
        CarBody body;
        CarLights lamps;
        RaceHandoffApplier applier;
        Transform stall;
        GameObject slope;
        float slopeDeg;

        static string OutDir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "Parked");

        void Awake() => bornAt = Time.realtimeSinceStartup;

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < 3600f) return;
            finished = true;
            Check(false, "HUNG: no end after an hour");
            Done();
        }

        void Done()
        {
            finished = true;
            Time.timeScale = 1f;
            CarController.LeftCarRules = true;
            ParkedPlayCheck.Finish();
            EditorApplication.Exit(ParkedPlayCheck.failures == 0 ? 0 : 1);
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 30; i++) yield return null;
            yield return new WaitForSeconds(2f);
            fm = ForecourtMode.Instance;
            car = fm != null ? fm.playerCar : null;
            applier = Object.FindFirstObjectByType<RaceHandoffApplier>();
            var world = Object.FindFirstObjectByType<PSXRacing.Town.TownWorld>();
            Check(fm != null && car != null && applier != null && world != null, "the town is up with a forecourt, a car and the handoff",
                  SceneManager.GetActiveScene().name);
            if (fm == null || car == null || applier == null || world == null) { Done(); yield break; }
            body = car.GetComponent<CarBody>();
            lamps = car.GetComponent<CarLights>();
            int meetCars = Object.FindObjectsByType<FootTarget>(FindObjectsSortMode.None).Count(t => t.gameObject.name == "MeetCarTarget");
            Log.AppendLine("meet tonight: " + CarMeets.OnNow(LifeSimManager.State) + ", " + meetCars + " meet cars in the lot; time of day " +
                           RaceHandoff.TimeOfDayIndex + ", lights on " + TimeOfDay.At(TimeOfDay.Current).lightsOn);

            stall = PickStall(world);
            Check(stall != null, "a free stall in the Eastside Lot", stall != null ? stall.name : "none");
            if (stall == null) { Done(); yield break; }
            BuildSlope();

            var keys = ParkedPlayCheck.Env("PSX_PARK_CARS", ParkedPlayCheck.DefaultCars);
            var shots = new HashSet<string>(ParkedPlayCheck.Env("PSX_PARK_SHOTS", new[] { "civic_eg", "roadster_99", "rx7_fd_93", "viper_gts", "flatsix_coupe", "flatsix_coupe_87", "rally_sedan_99", "skyline_r32" }));
            var modes = ParkedPlayCheck.Env("PSX_PARK_MODES", new[] { "before", "after" });
            float watch = 30f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_PARK_SECONDS") ?? "", out watch);
            if (watch <= 0f) watch = 30f;

            Time.timeScale = SpeedUp;
            Log.AppendLine("mode   car                surface  drift_m  end_mps  gear  lever  held  lamps  gapF_rest gapF_end dF_mm  gapR_rest gapR_end dR_mm  liftF_mm  back_in_mps  teleports");
            foreach (var mode in modes)
            {
                bool after = mode == "after";
                CarController.LeftCarRules = after;
                foreach (var key in keys)
                {
                    var spec = ParkedPlayCheck.SpecFor(key);
                    if (spec == null) { Check(false, key + ": no catalogue car wears it"); continue; }
                    foreach (var surface in new[] { "lot", "slope" })
                    {
                        bool shoot = surface == "lot" && shots.Contains(key);
                        yield return Case(mode, key, spec, surface, watch, shoot, after);
                    }
                }
            }
            Time.timeScale = 1f;
            Done();
        }

        /// <summary>A free stall (not a meet seat) whose back-to-back partner
        /// across the spine IS a meet seat, so a car that rolls out backwards
        /// meets a parked one, as in the owner's picture.</summary>
        static Transform PickStall(PSXRacing.Town.TownWorld world)
        {
            var seats = new HashSet<Transform>(world.meetSpots.Where(t => t != null));
            var stalls = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                               .Where(t => t.name.StartsWith("Stall") && t.name.Length > 6 && char.IsDigit(t.name[6])).ToList();
            Transform best = null;
            foreach (var s in stalls.OrderBy(t => t.name))
            {
                if (seats.Contains(s)) continue;
                char row = s.name[5];
                string idx = s.name.Substring(6);
                string partner = row == 'C' ? "StallB" + idx : row == 'B' ? "StallC" + idx : null;
                var p = partner != null ? stalls.FirstOrDefault(t => t.name == partner) : null;
                if (p != null && seats.Contains(p)) return s;
                if (best == null) best = s;
            }
            return best;
        }

        void BuildSlope()
        {
            slopeDeg = Mathf.Atan(0.15f) * Mathf.Rad2Deg;
            slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slope.name = "ParkCheckSlope";
            slope.transform.position = new Vector3(-400f, 300f, -400f);
            slope.transform.rotation = Quaternion.Euler(-slopeDeg, 0f, 0f);    // +z end UP the hill
            slope.transform.localScale = new Vector3(60f, 1f, 80f);
            slope.layer = 0;
        }

        IEnumerator Wait(float seconds) { yield return new WaitForSeconds(seconds); }

        Phase PhaseNow => (Phase)(int)typeof(ForecourtMode).GetField("phase", Any).GetValue(fm);
        enum Phase { InCar, GettingOut, Afoot, GettingIn }

        IEnumerator GetIn()
        {
            if (PhaseNow != Phase.Afoot) yield break;
            fm.StartCoroutine((IEnumerator)typeof(ForecourtMode).GetMethod("GetIn", Any).Invoke(fm, null));
            float t0 = Time.realtimeSinceStartup;
            while (PhaseNow != Phase.InCar && Time.realtimeSinceStartup - t0 < 20f) yield return null;
        }

        IEnumerator GetOut()
        {
            if (PhaseNow != Phase.InCar) yield break;
            fm.StartCoroutine((IEnumerator)typeof(ForecourtMode).GetMethod("GetOut", Any).Invoke(fm, null));
            float t0 = Time.realtimeSinceStartup;
            while (PhaseNow != Phase.Afoot && Time.realtimeSinceStartup - t0 < 20f) yield return null;
        }

        /// <summary>Tyre <paramref name="i"/>'s room under its arch as DRAWN
        /// (m): the arch where the shell stands now, over the drawn hub. At the
        /// plain seat this is HubMax - hub.</summary>
        float Gap(int i)
        {
            if (body == null || body.Def == null || body.bodyRoot == null) return float.NaN;
            float hm = car.HubMax(i);
            if (float.IsInfinity(hm) || car.wheelHubs[i] == null) return float.NaN;
            Vector3 hub = car.wheelHubs[i].localPosition;
            var yaw = Quaternion.Euler(0f, body.Def.bodyYaw, 0f);
            var now = Matrix4x4.TRS(body.bodyRoot.localPosition, body.bodyRoot.localRotation * Quaternion.Inverse(yaw), Vector3.one);
            var plain = Matrix4x4.TRS(body.SeatPosition, Quaternion.identity, Vector3.one);
            Vector3 arch = (now * plain.inverse).MultiplyPoint3x4(new Vector3(hub.x, hm, hub.z));
            return arch.y - hub.y;
        }

        float GapAxle(bool front) => front ? 0.5f * (Gap(0) + Gap(1)) : 0.5f * (Gap(2) + Gap(3));
        float ShellLiftFront()
        {
            if (body == null || body.bodyRoot == null) return float.NaN;
            // the shell's own height over the front axle vs the plain seat
            var yaw = Quaternion.Euler(0f, body.Def != null ? body.Def.bodyYaw : 0f, 0f);
            var now = Matrix4x4.TRS(body.bodyRoot.localPosition, body.bodyRoot.localRotation * Quaternion.Inverse(yaw), Vector3.one);
            var plain = Matrix4x4.TRS(body.SeatPosition, Quaternion.identity, Vector3.one);
            Vector3 p = car.wheelHubs[0] != null ? car.wheelHubs[0].localPosition : Vector3.zero;
            p.x = 0f; p.y = body.SeatPosition.y;
            return (now * plain.inverse).MultiplyPoint3x4(p).y - p.y;
        }

        /// <summary>The shell's pitch against its plain seat, degrees, nose up +.</summary>
        float ShellPitchDeg()
        {
            if (body == null || body.bodyRoot == null) return float.NaN;
            var yaw = Quaternion.Euler(0f, body.Def != null ? body.Def.bodyYaw : 0f, 0f);
            var q = body.bodyRoot.localRotation * Quaternion.Inverse(yaw);
            Vector3 f = q * Vector3.forward;
            return Mathf.Atan2(f.y, f.z) * Mathf.Rad2Deg;
        }

        IEnumerator Case(string mode, string key, CarSpec spec, string surface, float watch, bool shoot, bool grade)
        {
            yield return GetIn();
            RaceHandoff.CarSpecId = spec.id;
            applier.SwapCar();
            body = car.GetComponent<CarBody>();
            lamps = car.GetComponent<CarLights>();

            Vector3 at; Quaternion rot;
            if (surface == "lot") { at = stall.position; rot = stall.rotation; }
            else
            {
                rot = Quaternion.Euler(-slopeDeg, 0f, 0f);
                at = slope.transform.position + slope.transform.up * 0.5f;
            }
            car.ResetTo(at + Vector3.up * 0.3f, rot);
            int tele0 = car.TeleportCount;
            yield return Wait(3f);

            // DRIVING AT REST: driver in, no input, stopped.
            float gF0 = 0f, gR0 = 0f; int n0 = 0;
            for (int k = 0; k < 10; k++)
            {
                gF0 += GapAxle(true); gR0 += GapAxle(false); n0++;
                yield return new WaitForFixedUpdate();
            }
            gF0 /= n0; gR0 /= n0;
            float lift0 = ShellLiftFront(), shellPitch0 = ShellPitchDeg();
            float chassisPitch0 = surface == "lot" ? -Mathf.DeltaAngle(0f, car.transform.eulerAngles.x) : float.NaN;
            Log.AppendLine(string.Format("  {0}_{1}_{2} AT REST, driver in: gapF {3:0.0} mm gapR {4:0.0} mm, shell lift over front axle {5:0.0} mm, shell pitch {6:+0.00;-0.00} deg (nose up +), chassis pitch {7:+0.00;-0.00} deg, hubY F {8:0.0} R {9:0.0} mm, HubMax F {10:0.0} R {11:0.0} mm",
                mode, key, surface, gF0 * 1000f, gR0 * 1000f, lift0 * 1000f, shellPitch0, chassisPitch0,
                0.5f * (car.PhysicsHubY(0) + car.PhysicsHubY(1)) * 1000f, 0.5f * (car.PhysicsHubY(2) + car.PhysicsHubY(3)) * 1000f,
                0.5f * (car.HubMax(0) + car.HubMax(1)) * 1000f, 0.5f * (car.HubMax(2) + car.HubMax(3)) * 1000f));
            if (grade && surface == "lot")
            {
                Check(Mathf.Abs(shellPitch0) < 0.5f && Mathf.Abs(lift0) < 0.010f,
                      key + ": at rest the shell sits on its plain seat (no nose-up)", string.Format("lift {0:0.0} mm, pitch {1:+0.00;-0.00} deg", lift0 * 1000f, shellPitch0));
                Check(Mathf.Abs(chassisPitch0) < 1.0f, key + ": at rest the chassis is level within 1 deg", chassisPitch0.ToString("+0.00;-0.00"));
            }

            // In R, as the owner left it, then out the real way.
            car.manualMode = false;
            car.currentGear = -1;
            yield return new WaitForFixedUpdate();
            yield return GetOut();
            Vector3 from = car.transform.position;
            string tag = mode + "_" + key + "_" + surface;
            Log.AppendLine("  " + tag + " per second: t  drift_m  mps  gear  lever  held  lamps  gapF_mm  gapR_mm  liftF_mm  hubYF_mm hubYR_mm");
            if (shoot) yield return Shot(tag + "_00s");
            float t0 = Time.time;
            int sec = 0;
            while (Time.time - t0 < watch)
            {
                yield return null;
                if (Time.time - t0 >= sec)
                {
                    var d = car.transform.position - from; d.y = 0f;
                    Log.AppendLine(string.Format("    {0,3}  {1,7:0.000}  {2,5:0.00}  {3,3}  {4,-5}  {5,-5}  {6,-5}  {7,7:0.0}  {8,7:0.0}  {9,7:0.0}  {10,7:0.0}  {11,7:0.0}",
                        sec, d.magnitude, car.Body.linearVelocity.magnitude, car.currentGear, car.handbrakeInput, car.Parked,
                        lamps != null && lamps.LitNow, GapAxle(true) * 1000f, GapAxle(false) * 1000f, ShellLiftFront() * 1000f,
                        0.5f * (car.PhysicsHubY(0) + car.PhysicsHubY(1)) * 1000f, 0.5f * (car.PhysicsHubY(2) + car.PhysicsHubY(3)) * 1000f));
                    sec++;
                }
            }
            var dEnd = car.transform.position - from; dEnd.y = 0f;
            float drift = dEnd.magnitude, endV = car.Body.linearVelocity.magnitude;
            float gF1 = GapAxle(true), gR1 = GapAxle(false), lift = ShellLiftFront();
            bool lit = lamps != null && lamps.LitNow;
            int gear = car.currentGear; bool lever = car.handbrakeInput, held = car.Parked;
            if (shoot) yield return Shot(tag + "_" + Mathf.RoundToInt(watch).ToString("00") + "s");

            // Back in: no jump.
            Vector3 before = car.transform.position;
            yield return GetIn();
            float maxV = 0f; float tb = Time.time;
            while (Time.time - tb < 3f) { maxV = Mathf.Max(maxV, car.Body.linearVelocity.magnitude); yield return new WaitForFixedUpdate(); }
            int teleports = car.TeleportCount - tele0;

            Log.AppendLine(string.Format("{0,-6} {1,-18} {2,-7} {3,8:0.000} {4,8:0.00} {5,5} {6,-6} {7,-5} {8,-5} {9,9:0.0} {10,8:0.0} {11,6:0.0} {12,9:0.0} {13,8:0.0} {14,6:0.0} {15,9:0.0} {16,12:0.00} {17,9}",
                mode, key, surface, drift, endV, gear, lever, held, lit, gF0 * 1000f, gF1 * 1000f, (gF1 - gF0) * 1000f,
                gR0 * 1000f, gR1 * 1000f, (gR1 - gR0) * 1000f, lift * 1000f, maxV, teleports));
            if (grade)
            {
                bool night = TimeOfDay.At(TimeOfDay.Current).lightsOn;
                Check(drift < 0.05f && endV < 0.05f, key + " " + surface + ": stays where it was left", drift.ToString("0.000") + " m");
                Check(!lit, key + " " + surface + ": no lamps on an empty car" + (night ? " (night)" : " (day: trivially)"));
                Check(float.IsNaN(gF0) || Mathf.Abs(gF1 - gF0) < 0.010f && Mathf.Abs(gR1 - gR0) < 0.010f,
                      key + " " + surface + ": sits at its at-rest ride height (arch gaps within 10 mm)",
                      string.Format("F {0:0.0} -> {1:0.0} mm, R {2:0.0} -> {3:0.0} mm", gF0 * 1000f, gF1 * 1000f, gR0 * 1000f, gR1 * 1000f));
                Check(maxV < 0.5f, key + " " + surface + ": back in without a jump", maxV.ToString("0.00") + " m/s");
                Check(teleports == 0, key + " " + surface + ": nothing teleported it", teleports);
            }
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var cam = fm.raceCamera != null ? fm.raceCamera : Camera.main;
            if (cam == null) yield break;
            var keepPos = cam.transform.position; var keepRot = cam.transform.rotation;
            float keepFov = cam.fieldOfView;
            // Side-on from the aisle, fixed to the STALL, so 0 s and 30 s
            // frame the same ground and any roll shows as the car moving.
            // From over the next stall's roof (the lot is packed 3 m apart),
            // so the wheels and arches of the car itself are in view.
            Vector3 look = stall.position + Vector3.up * 0.45f;
            Vector3 eye = stall.position + stall.right * 7.5f + Vector3.up * 4.2f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = 42f;
            var target = cam.targetTexture;
            int w = target != null ? target.width : 960, h = target != null ? target.height : 540;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                var shown = PSXScreenshotTool.Dithered(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev == rt || prev == shown ? null : prev;
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                Directory.CreateDirectory(OutDir);
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            else Log.AppendLine("  (no render request for " + name + ")");
            cam.targetTexture = target;
            cam.transform.SetPositionAndRotation(keepPos, keepRot);
            cam.fieldOfView = keepFov;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
