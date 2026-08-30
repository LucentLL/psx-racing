using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Headless check on the LifeSim economy. The rollover pipeline is the piece
    /// RG2 had to fix twice (H237, H551) because day hooks raced a frame loop, so
    /// it is worth asserting rather than eyeballing: run 35 simulated days and
    /// confirm the calendar fires exactly the right number of paydays and bills,
    /// then run races until a fault surfaces and confirm it can actually be
    /// repaired.
    ///
    /// Runs off a LifeState built directly by LifeRules — no PlayerPrefs, no
    /// scene, no UI — so it is safe in batchmode and leaves no save behind.
    /// Menu: PSX Racing/Run LifeSim Self-Test.
    /// </summary>
    public static class LifeSimSelfTest
    {
        static StringBuilder log;
        static int failures;

        [MenuItem("PSX Racing/Run LifeSim Self-Test")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;

            TestCatalogLoads();
            TestCalendarPipeline();
            TestRepairEconomy();
            TestFaultGate();
            TestCarCatalog();
            TestEngineVoices();
            TestCarModels();
            TestUpgrades();
            TestMarket();
            TestRaceField();
            TestBlacklist();
            TestTracks();
            TestTimeOfDay();
            TestCameraViews();
            TestToolbox();
            TestInspection();
            TestCarXray();
            TestHousingAndJobs();
            TestShiftRoster();
            TestCalendar();
            TestDiary();
            TestCityProps();
            TestGridStaging();
            TestHomeLot();
            TestMenuNavigation();

            Line(failures == 0 ? "SELF-TEST OK" : "SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_log.txt"),
                log.ToString());
        }

        static void Line(string s) { log.AppendLine(s); }

        static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            Line((ok ? "  ok   " : "  FAIL ") + what + (got != null ? "  (got " + got + ")" : ""));
        }

        // ---------------------------------------------------------------
        //  Housing, delivery job, and the props the world stands up
        // ---------------------------------------------------------------
        static void TestHousingAndJobs()
        {
            Line("housing + jobs:");
            Check(LifeRules.Housing.Length >= 3 && LifeRules.Housing[0].slots == 1,
                  "starting rung is the 1-car-garage house");
            var seeded = LifeRules.SeedNewGame("TEST", 25, 0);
            bool known = false;
            foreach (var h in LifeRules.Housing) if (h.key == seeded.housingType) known = true;
            Check(known, "seeded housingType exists in the ladder", seeded.housingType);
            Check(LifeRules.HousingLabel(seeded.housingType).Contains("1-CAR"),
                  "seed labels as a one-car-garage house",
                  LifeRules.HousingLabel(seeded.housingType));

            bool hasDelivery = false;
            foreach (var j in LifeRules.Jobs) if (j.name == LifeRules.DeliveryJobName) hasDelivery = true;
            Check(hasDelivery, "FOOD DELIVERY is back in the job book");

            var s = LifeRules.SeedNewGame("TEST", 25, LifeRules.Jobs.Length - 1);
            Check(s.playerJob == LifeRules.DeliveryJobName,
                  "last job index seeds the delivery job", s.playerJob);
            s.ateToday = false; s.daysSinceEat = 2;
            LifeRules.WorkOneDay(s);
            Check(s.ateToday && s.daysSinceEat == 0, "a delivery shift feeds the driver");
            Check(s.pendingSalary > 0, "tips accrue into pendingSalary", s.pendingSalary);
        }

        /// <summary>
        /// The 1999 calendar, and the diary written on it.
        ///
        /// The whole date system rests on one coincidence that has to be true
        /// and would be silent if it were not: the game's week has always run
        /// FRI-SAT-SUN-MON..THU from day 1, and 1 January 1999 was a Friday. If
        /// that ever stopped agreeing, every screen would keep rendering
        /// perfectly and every day of the week would be wrong — a bug with no
        /// symptom except a player noticing that payday fell on a Tuesday.
        /// </summary>
        static void TestCalendar()
        {
            Line("calendar:");

            Check(LifeRules.Epoch.Year == 1999 && LifeRules.Epoch.Month == 1 &&
                  LifeRules.Epoch.Day == 1,
                  "a career starts on 1 January 1999", LifeRules.Epoch.ToString("yyyy-MM-dd"));
            Check(LifeRules.Epoch.DayOfWeek == System.DayOfWeek.Friday,
                  "which really was a Friday", LifeRules.Epoch.DayOfWeek);

            // Two years of it, so a leap year and every month length is covered.
            bool dowAgrees = true, roundTrips = true, monthLenOk = true;
            string firstBad = "";
            for (int day = 1; day <= 730; day++)
            {
                var real = LifeRules.DateOf(day);
                // DowNames is FRI-first, so index 0 is Friday and the real
                // DayOfWeek (Sunday-first) has to be shifted by five to compare.
                int realIdx = ((int)real.DayOfWeek - (int)System.DayOfWeek.Friday + 7) % 7;
                if (realIdx != LifeRules.Dow(day) && dowAgrees)
                { dowAgrees = false; firstBad = "day " + day + " " + real.ToString("yyyy-MM-dd"); }
                if (LifeRules.DayNumber(real) != day) roundTrips = false;
                if (LifeRules.DaysInMonth(day) !=
                    System.DateTime.DaysInMonth(real.Year, real.Month)) monthLenOk = false;
            }
            Check(dowAgrees, "the game's own FRI-first week IS the real 1999 week", firstBad);
            Check(roundTrips, "DayNumber and DateOf are inverses");
            Check(monthLenOk, "months are their real length");

            // The flat-30 counter this replaced said February had 30 days, and
            // the BILLS tab counts down to the 1st with that number.
            int feb1 = LifeRules.DayNumber(new System.DateTime(1999, 2, 1));
            Check(LifeRules.DaysInMonth(feb1) == 28, "February 1999 is 28 days",
                  LifeRules.DaysInMonth(feb1));
            Check(LifeRules.DayOfMonth(feb1) == 1 && LifeRules.MonthOf(feb1) == 2,
                  "and starts on its own 1st", LifeRules.DateLabel(feb1));

            // Bills fire on the 1st and nothing else. Counted over a year rather
            // than asserted at one date, because the failure mode of a calendar
            // change is a month that fires twice or not at all.
            int firsts = 0;
            for (int day = 1; day <= 365; day++) if (LifeRules.DayOfMonth(day) == 1) firsts++;
            Check(firsts == 12, "exactly twelve bill days in a year", firsts);

            int paydays = 0;
            for (int day = 1; day <= 364; day++) if (LifeRules.IsPayday(day)) paydays++;
            Check(paydays == 52, "and fifty-two paydays", paydays);

            Check(LifeRules.DateLabel(1).Contains("1999") && LifeRules.DateLabel(1).StartsWith("FRI"),
                  "the header prints the year", LifeRules.DateLabel(1));
            Check(LifeRules.MonthLabel(1) == "JANUARY 1999",
                  "and the calendar prints its month", LifeRules.MonthLabel(1));
        }

        /// <summary>
        /// The diary: booking a race on a future day, and what happens to a
        /// booking nobody keeps.
        ///
        /// A booking is the only thing in the save that points FORWARD, so it is
        /// the only thing a rollover can silently lose or silently keep forever.
        /// Both were worth pinning: a booking that survives its own day sits on
        /// the home screen offering a race that was due last week, and one swept
        /// a day early vanishes the morning the player meant to drive it.
        /// </summary>
        static void TestDiary()
        {
            Line("diary:");

            var s = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            int today = s.day;

            Check(!LifeRules.Book(s, today - 1, 0, false), "you cannot book yesterday");
            Check(LifeRules.Book(s, today + 3, 1, false), "you can book three days out");
            Check(!LifeRules.Book(s, today + 3, 2, false), "but only one race a day");
            Check(LifeRules.BookingOn(s, today + 3) != null &&
                  LifeRules.BookingOn(s, today + 3).trackIndex == 1,
                  "and the booking keeps its OWN venue",
                  LifeRules.BookingOn(s, today + 3)?.trackIndex);
            Check(LifeRules.BookingOn(s, today + 2) == null, "neighbouring days stay empty");

            LifeRules.Unbook(s, today + 3);
            Check(LifeRules.BookingOn(s, today + 3) == null, "cancelling clears it");

            // Live on its own day, gone the morning after. Driven through Sleep
            // so it is the real rollover doing the sweeping.
            LifeRules.Book(s, today + 1, 3, true);
            LifeRules.Sleep(s);
            Check(LifeRules.BookingOn(s, s.day) != null,
                  "a booking is still there on the day it is for");
            Check(LifeRules.BookingOn(s, s.day).practice, "and remembers it was a practice lap");
            int logBefore = s.calendarLog.Count;
            LifeRules.Sleep(s);
            Check(s.bookings.Count == 0, "and is gone the day after", s.bookings.Count);
            Check(s.calendarLog.Count > logBefore, "with a line in the diary saying it was missed");

            // Missing one costs nothing but the slot the player spent elsewhere.
            // Asserted because the absence ladder next door DOES bite, and the
            // two are easy to confuse into one rule.
            var t = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            float repBefore = t.streetRep;
            int moneyBefore = t.money;
            LifeRules.Book(t, t.day + 1, 2, false);
            LifeRules.Sleep(t); LifeRules.Sleep(t);
            Check(Mathf.Approximately(t.streetRep, repBefore) && t.money == moneyBefore,
                  "a missed booking is not a punishment", t.streetRep + " / " + t.money);

            // An old save has no bookings list in its JSON at all.
            var old = new LifeState();
            Check(old.bookings != null && old.bookings.Count == 0,
                  "a save with no diary reads back as an empty one");
        }

        // ---------------------------------------------------------------
        //  The delivery roster
        // ---------------------------------------------------------------
        /// <summary>
        /// The job book is ONE job, and its hours are afternoons and nights,
        /// seven days a week.
        ///
        /// Pinned rather than assumed because every one of these rules is a
        /// number or a boolean that reads fine at the call site and is invisible
        /// on screen until a player loses a day to it. The weekday rule shipped
        /// and printed "WEEKEND — NO WORK" over a pizza shop's two busiest
        /// nights; nothing in the game said otherwise until somebody played it.
        /// </summary>
        static void TestShiftRoster()
        {
            Line("shift roster:");

            Check(LifeRules.Jobs.Length == 1 && LifeRules.Jobs[0].name == LifeRules.DeliveryJobName,
                  "the job book is delivery and nothing else",
                  LifeRules.Jobs.Length + " job(s)");

            var s = LifeRules.SeedNewGame("TEST", 25, LifeRules.DefaultJobIndex);

            // Every slot of a weekday and of a weekend day. Day 2 is a Saturday
            // and day 3 a Sunday (day 1 is Friday), so this covers the exact
            // days the old rule refused.
            bool morningShut = true, afternoonOpen = true, nightOpen = true;
            for (int day = 1; day <= 7; day++)
            {
                s.day = day;
                s.slotIndex = 0; if (LifeRules.ShopOpen(s)) morningShut = false;
                s.slotIndex = 1; if (!LifeRules.ShopOpen(s)) afternoonOpen = false;
                s.slotIndex = 2; if (!LifeRules.ShopOpen(s)) nightOpen = false;
            }
            Check(morningShut, "the shop is shut every morning");
            Check(afternoonOpen, "and open every afternoon, weekends included");
            Check(nightOpen, "and open every night, weekends included");
            Check(LifeRules.IsWeekend(2) && LifeRules.IsWeekend(3),
                  "day 2 and day 3 really are the weekend this proves");

            // The hours the screens print have to be the hours the rule keeps.
            Check(LifeRules.ShiftHours.Contains("AFTERNOON") &&
                  LifeRules.ShiftHours.Contains("NIGHT") &&
                  LifeRules.ShiftHours.ToUpper().Contains("SEVEN"),
                  "the printed roster names both shifts and seven days",
                  LifeRules.ShiftHours);

            // Slot 1 and slot 2 have to LOOK like afternoon and night as well,
            // or the button is open at an hour the sky disagrees with.
            Check(TimeOfDay.At(TimeOfDay.ForSlot(1, 1)).name != "NIGHT",
                  "slot 1 reads as daylight", TimeOfDay.At(TimeOfDay.ForSlot(1, 1)).name);
            Check(TimeOfDay.At(TimeOfDay.ForSlot(2, 1)).lightsOn,
                  "slot 2 is dark enough for headlights");

            // ---- the absence ladder ----
            // Two days off cost nothing; the third starts the ladder and the
            // fifth ends the job. Driven through the real rollover rather than
            // by poking consecutiveAbsences, because the rollover is what a
            // player actually experiences and what the old rule lived in.
            var t = LifeRules.SeedNewGame("TEST", 25, LifeRules.DefaultJobIndex);
            float startRep = t.workRep;
            for (int i = 0; i < LifeRules.FreeDaysOff; i++) LifeRules.Sleep(t);
            Check(t.workRep == startRep && !string.IsNullOrEmpty(t.playerJob),
                  LifeRules.FreeDaysOff + " days off cost nothing", t.workRep);

            LifeRules.Sleep(t);
            Check(t.workRep < startRep, "the day after the allowance costs rep", t.workRep);

            int guard = 0;
            while (!string.IsNullOrEmpty(t.playerJob) && guard++ < 20) LifeRules.Sleep(t);
            Check(string.IsNullOrEmpty(t.playerJob) && t.fired,
                  "a driver who never turns up is fired", guard);

            // And a single shift clears the counter, which is the half that
            // makes the allowance a rolling one rather than a countdown to
            // being sacked.
            var u = LifeRules.SeedNewGame("TEST", 25, LifeRules.DefaultJobIndex);
            LifeRules.Sleep(u); LifeRules.Sleep(u);
            LifeRules.WorkOneDay(u);
            LifeRules.Sleep(u);
            Check(u.consecutiveAbsences == 0, "one shift resets the ladder", u.consecutiveAbsences);

            // ---- the save migration ----
            // An old career is holding a job title the table no longer has. It
            // has to come across, keep its standing, and be paid on the tip
            // roll rather than on a tanker driver's salary.
            var old = LifeRules.SeedNewGame("TEST", 25, LifeRules.DefaultJobIndex);
            old.saveVersion = 7;
            old.playerJob = "FUEL TANKER";
            old.basePay = 231;
            old.workRep = 61f;
            old.consecutiveAbsences = 2;
            LifeSimManager.Migrate(old);
            Check(old.playerJob == LifeRules.DeliveryJobName,
                  "an old career is moved onto the delivery job", old.playerJob);
            Check(old.basePay == LifeRules.DeliveryBasePay,
                  "and paid on the tip roll, not the old salary", old.basePay);
            Check(Mathf.Approximately(old.workRep, 61f),
                  "and keeps the standing it earned", old.workRep);
            Check(old.consecutiveAbsences == 0,
                  "and is not fired by the rule change on its first rollover",
                  old.consecutiveAbsences);
        }

        static void TestCityProps()
        {
            Line("city props:");
            int missing = 0;
            foreach (var kv in PSXRacing.City.CityProps.Defs)
            {
                string path = "Assets/PSXRacing/Resources/" + kv.Value.res + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { missing++; Line("       missing " + path); continue; }
                if (kv.Key == PSXRacing.City.CityProps.Burger ||
                    kv.Key == PSXRacing.City.CityProps.Pizzeria)
                {
                    Check(prefab.GetComponentInChildren<DriveThru>(true) != null,
                          kv.Value.res + " carries its order bay");
                    bool trigger = false;
                    foreach (var c in prefab.GetComponentsInChildren<BoxCollider>(true))
                        if (c.isTrigger) trigger = true;
                    Check(trigger, kv.Value.res + " order bay is a trigger");
                }
                else
                {
                    Check(prefab.GetComponentInChildren<Collider>(true) != null,
                          kv.Value.res + " is solid to a car");
                }

                // The declared footprint IS the placement maths — the corridor
                // test, the occupancy grid and the seating all measure with it.
                // A def that disagrees with the model it names puts buildings
                // through each other and through roads, silently. Measured off
                // the model only: the apron and the foundation skirt are ours,
                // and deliberately bigger than the building.
                var inst = (GameObject)Object.Instantiate(prefab);
                var b = new Bounds();
                bool any = false;
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string n = r.gameObject.name;
                    if (n == "Apron" || n == "Skirt" || n == "Solid" || n == "OrderBay") continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                Object.DestroyImmediate(inst);
                if (any)
                {
                    float wide = Mathf.Max(b.size.x, b.size.z);
                    float deep = Mathf.Min(b.size.x, b.size.z);
                    float declW = Mathf.Max(kv.Value.w, kv.Value.d);
                    float declD = Mathf.Min(kv.Value.w, kv.Value.d);
                    bool fits = Mathf.Abs(wide - declW) < declW * 0.18f + 0.5f &&
                                Mathf.Abs(deep - declD) < declD * 0.18f + 0.5f;
                    Check(fits, kv.Value.res + " is the size its Def claims",
                          wide.ToString("0.0") + " x " + deep.ToString("0.0") +
                          " vs " + declW.ToString("0.0") + " x " + declD.ToString("0.0"));
                }
            }
            Check(missing == 0, "every CityProps row baked a prefab", missing + " missing");

            // The restaurants are the only props the player goes LOOKING for,
            // and the placement gates (four lanes, 1.2-9.5 km from uptown,
            // 1.5 km apart, off a ramp, out of the water) are strict enough
            // that a small change to any of them could quietly leave the city
            // with nowhere to eat. The HUD's signpost would then point at
            // nothing at all.
            var map = PSXRacing.City.CityMap.Get();
            if (map != null)
            {
                var placed = PSXRacing.City.CityBuildings.Precompute(map);
                int burgers = 0, pizzas = 0;
                foreach (var kv in placed)
                    foreach (var b in kv.Value)
                    {
                        if (b.kind == PSXRacing.City.CityProps.Burger) burgers++;
                        else if (b.kind == PSXRacing.City.CityProps.Pizzeria) pizzas++;
                    }
                Check(burgers >= 3, "Charlotte has drive-thrus", burgers);
                Check(pizzas >= 3, "Charlotte has pizzerias", pizzas);
            }

        }

        // ---------------------------------------------------------------
        //  Grid staging, read off the BUILT scenes
        // ---------------------------------------------------------------
        /// <summary>
        /// Every car the builder baked, and the spot the 1v1 restage would put
        /// the player, must be ON THE ROAD of its own venue. This opens the
        /// built scenes — straight after a mirror with no scene build it fails
        /// the same way the build-settings checks do, and means the same thing.
        /// </summary>
        static void TestGridStaging()
        {
            Line("grid staging (built scenes):");
            var scenes = EditorBuildSettings.scenes;
            for (int t = 0; t < TrackCatalog.Count; t++)
            {
                var def = TrackCatalog.At(t);
                if (def.city) continue;
                int sceneIdx = TrackCatalog.SceneIndex(t);
                if (sceneIdx >= scenes.Length || !System.IO.File.Exists(scenes[sceneIdx].path))
                {
                    Check(false, def.id + ": scene not built yet");
                    continue;
                }
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenes[sceneIdx].path);
                var path = Object.FindFirstObjectByType<TrackPath>();
                var applier = Object.FindFirstObjectByType<RaceHandoffApplier>();
                if (path == null || applier == null || applier.playerCar == null)
                {
                    Check(false, def.id + ": scene missing path/applier/player");
                    continue;
                }

                bool allOn = OnRoad(path, applier.playerCar.transform.position);
                foreach (var ai in applier.aiCars)
                    if (ai != null) allOn &= OnRoad(path, ai.transform.position);
                Check(allOn, def.id + ": baked grid is on the road");

                if (applier.aiCars.Count > 0 && applier.aiCars[0] != null)
                {
                    var rival = applier.aiCars[0].transform;
                    bool ok;
                    if (path.drag)
                    {
                        int idx = path.NearestIndex(rival.position);
                        Vector3 centre = path.GetPoint(idx);
                        Vector3 right = path.GetRotation(idx) * Vector3.right;
                        float lane = Mathf.Min(path.roadWidth / 6f, 2.75f);
                        ok = OnRoad(path, centre - right * lane) &&
                             OnRoad(path, centre + right * lane);
                    }
                    else
                    {
                        ok = OnRoad(path, rival.position + rival.right * 5.2f);
                    }
                    Check(ok, def.id + ": 1v1 restage stays on the road");
                }
            }
        }

        /// <summary>
        /// The home lot, in the three ways it has actually been wrong: a house
        /// too big for the person walking round it, a parking bay under its own
        /// garage floor, and a ground plane too coarse to stop the PSX vertex
        /// snap swimming underfoot. All three were reported by eye and none of
        /// them throws, so all three are asserted here.
        /// </summary>
        static void TestHomeLot()
        {
            Line("home lot:");
            if (!System.IO.File.Exists(GarageSceneBuilder.ScenePath))
            {
                Check(false, "garage scene is built");
                return;
            }
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GarageSceneBuilder.ScenePath);

            var house = GameObject.Find("House");
            Check(house != null, "the house is in the scene");
            if (house == null) return;

            // A person is 1.62 m to the eye, so the house has to be a house.
            var doors = new List<float>();
            float garageBaseY = float.MaxValue;
            foreach (var r in house.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name;
                if (n.StartsWith("Garage_Door"))
                    garageBaseY = Mathf.Min(garageBaseY, r.bounds.min.y);
                else if (n == "Door" || (n.StartsWith("Door_0") && !n.Contains("frame")))
                    doors.Add(r.bounds.size.y);
            }
            doors.Sort();
            float med = doors.Count > 0 ? doors[doors.Count / 2] : 0f;
            Check(doors.Count > 0 && Mathf.Abs(med - 2.03f) < 0.25f,
                  "interior doors are door-sized (2.03 m +/- 0.25)", med.ToString("0.00"));

            // The garage floor is the datum the whole lot sits on: at y=0 the
            // lot's own ground slab IS the garage floor, which matters because
            // the collider mesh has no garage floor in it.
            Check(garageBaseY < float.MaxValue && Mathf.Abs(garageBaseY) < 0.12f,
                  "garage floor sits at y=0", garageBaseY.ToString("0.000"));

            var bay0 = GameObject.Find("Bay0");
            Check(bay0 != null && Mathf.Abs(bay0.transform.position.y - garageBaseY) < 0.12f,
                  "bay 0 is ON the garage floor, not under it",
                  bay0 != null ? bay0.transform.position.y.ToString("0.000") : "missing");

            // Eye height above the ground the player is standing on.
            // The player's OWN camera. FindFirstObjectByType returns whichever
            // camera the scene happens to list first, and this scene also holds
            // the PSX output camera parked at the origin — which is how this
            // assertion once reported a 20 cm eye height for a rig that was
            // fine.
            var player = GameObject.Find("Player");
            var cam = player != null ? player.GetComponentInChildren<Camera>() : null;
            if (player != null && cam != null)
            {
                float eye = cam.transform.position.y - player.transform.position.y;
                // SIX FEET, not just "human". The owner asked for a six-foot
                // player and the old range would have passed a five-foot-four
                // one just as happily, which is how it sat at 1.62 unnoticed.
                Check(eye > 1.64f && eye < 1.76f,
                      "the eye is at six-foot eye height (1.70)", eye.ToString("0.00"));
            }
            else Check(false, "player rig is in the scene");

            // Ground panels are subdivided: the PSX snap on a two-triangle lawn
            // is the "ground warps as I walk" report.
            var yard = GameObject.Find("Yard");
            var mf = yard != null ? yard.GetComponent<MeshFilter>() : null;
            int verts = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
            Check(verts >= 400, "the yard is subdivided, not one big quad", verts + " verts");
        }

        static bool OnRoad(TrackPath path, Vector3 pos)
        {
            int idx = path.NearestIndex(pos);
            Vector3 wp = path.waypoints[idx];
            Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(idx)).normalized;
            float lateral = Vector3.Dot(pos - wp, right);
            return Mathf.Abs(lateral) <= path.roadWidth * 0.5f - 0.8f;
        }

        // ---------------------------------------------------------------

        static void TestCatalogLoads()
        {
            Line("catalog:");
            Check(FaultCatalog.Ready, "rg2_faults.json loads with pool entries");
            var e = FaultCatalog.Effect("tire_wear");
            Check(Mathf.Abs(e.gripMult - 0.78f) < 0.001f, "tire_wear grip is 0.78", e.gripMult);
            Check(FaultCatalog.MileageTier(50000f) == "new", "50k mi is 'new'");
            Check(FaultCatalog.MileageTier(90000f) == "mid", "90k mi is 'mid'");
            Check(FaultCatalog.MileageTier(200000f) == "high", "200k mi is 'high'");
            // An unknown id must degrade to identity, not to a null deref: the
            // pools contain at least one fault (timing_chain) with no effect row.
            var unknown = FaultCatalog.Effect("no_such_fault_id");
            Check(unknown.accelMult == 1f && !unknown.hideGauges, "unknown fault id is identity");
        }

        // ---------------------------------------------------------------
        //  Circuits
        // ---------------------------------------------------------------
        /// <summary>
        /// Geometry checks on every circuit, because a bad layout is not a
        /// crash: a corner tighter than a car can turn, or two parts of the loop
        /// close enough that their barriers interpenetrate, both build fine and
        /// both only show up when somebody drives into them.
        ///
        /// The thresholds are the ones the layouts were designed against —
        /// 18 m is a tight hairpin, and two stretches of circuit need road plus
        /// both wall lines between them or the barriers cross.
        /// </summary>
        /// <summary>
        /// The heaviest fuel bill this track can hand out: the thirstiest car
        /// in the catalog, built to stage 4, driven at a racing pace for the
        /// whole race. Fully built rather than stock because that is the car a
        /// player who has been at this a while is actually sitting in.
        /// </summary>
        static float WorstRaceTankPct(TrackCatalog.TrackDef t)
        {
            var maxed = new CarTune.Stages { power = CarTune.MaxStage, weight = CarTune.MaxStage };
            float worst = 0f;
            foreach (var spec in CarCatalog.All)
            {
                float pct = FuelProfile.For(spec, maxed)
                                       .Burn(t.RaceMeters, FuelModel.RacePaceLoad);
                if (pct > worst) worst = pct;
            }
            return worst;
        }

        static void TestTracks()
        {
            Line("tracks:");
            Check(TrackCatalog.Count >= 2, "catalog has more than one circuit", TrackCatalog.Count);

            var ids = new HashSet<string>();
            foreach (var t in TrackCatalog.All)
            {
                Check(ids.Add(t.id), "unique id " + t.id);
                Check(System.IO.File.Exists("Assets/PSXRacing/Scenes/" + t.id + ".unity"),
                      t.id + " scene exists");

                if (t.city)
                {
                    // The city has no centreline, no theme and no lap: its
                    // invariants are graph-shaped and live in CityAudit. What
                    // the CATALOG owes it is a scene (checked above), a map
                    // chip, and the scene-index contract below.
                    var cityThumb = TrackCatalog.Thumbnail(t, 96);
                    Check(cityThumb != null && OpaquePixels(cityThumb) > 200,
                          t.id + " draws a map", cityThumb != null ? OpaquePixels(cityThumb) : 0);
                    continue;
                }
                Check(PSXRacingBuilder.HasTheme(t.id), t.id + " has a builder theme");

                var pts = TrackCatalog.Sample(t, TrackCatalog.Spacing);

                // STAGE FIRST. These two are no longer mutually exclusive: the
                // Bogue Banks bridges are drag events on baked map data, and
                // testing one as a synthetic strip would demand a corner radius
                // over 1000 m from a route whose shutdown area turns off the
                // bridge onto a causeway.
                if (t.stage)
                {
                    // A stage is a real road: it shares the strip's finish-line
                    // contract and the circuit's corner floor, and is exempt
                    // from the 3.3 km economy band — a 7 km mountain run
                    // paying and burning like 7 km is the honest answer.
                    //
                    // The floor is a STUB test, not a length test: a missing
                    // bake becomes a 2-point token road, and the Bogue Banks
                    // bridges are legitimately 400-odd waypoints long.
                    Check(pts.Count > 100, t.id + " bakes to a real stage", pts.Count);
                    Check(t.FinishIndex > 20 && t.FinishIndex < pts.Count - 20,
                          t.id + " has a finish with shutdown beyond it",
                          t.FinishIndex + " of " + pts.Count);
                    Check(!string.IsNullOrEmpty(t.dragLabel), t.id + " is named for the HUD");
                    Check(t.stageStartLineM > 20f,
                          t.id + " has a lead-in for the grid", t.stageStartLineM);
                    float stageMinR = MinCornerRadius(pts);
                    Check(stageMinR >= 18f, t.id + " tightest corner is drivable",
                          stageMinR.ToString("0.0") + " m");
                    // Asked of the builder rather than restated: the stage
                    // barrier line follows the road's width now, and a literal
                    // here would go on testing the parkway's number against
                    // every other stage — which is the bug this pass fixed.
                    float stageNeed = t.roadWidth + 2f * PSXRacingBuilder.WallOffsetFor(t);
                    float stageGap = MinSelfClearance(pts);
                    Check(stageGap >= stageNeed, t.id + " never runs into its own barriers",
                          stageGap.ToString("0.0") + " m vs " + stageNeed.ToString("0.0"));
                    // A drag event has to be RACEABLE from a standing start on
                    // the line: the traps sit past the grid, not behind it.
                    if (t.dragEvent)
                        Check(t.FinishIndex * TrackCatalog.Spacing > t.stageStartLineM + 100f,
                              t.id + " traps sit well past the start line",
                              (t.FinishIndex * TrackCatalog.Spacing).ToString("0") + " m vs line at "
                                  + t.stageStartLineM.ToString("0") + " m");
                }
                else if (t.drag)
                {
                    // A strip is judged on different things entirely: it has no
                    // corners to be too tight and no second stretch to run into.
                    // What it must have is a finish line INSIDE the waypoint
                    // list with shutdown beyond it — a traps index at or past
                    // the end is a race that can never be won.
                    Check(t.FinishIndex > 0 && t.FinishIndex < pts.Count - 20,
                          t.id + " has traps with shutdown beyond them",
                          t.FinishIndex + " of " + pts.Count);
                    Check(Mathf.Abs(t.FinishIndex * TrackCatalog.Spacing - t.dragMeters)
                              < TrackCatalog.Spacing,
                          t.id + " traps land within a waypoint of the real distance",
                          (t.FinishIndex * TrackCatalog.Spacing).ToString("0.0") + " vs " + t.dragMeters);
                    Check(!string.IsNullOrEmpty(t.dragLabel), t.id + " is named for the HUD");
                    // Straight means straight: any curvature at all would have
                    // the AI lifting for a corner that is not there.
                    Check(MinCornerRadius(pts) > 1000f, t.id + " is actually straight",
                          MinCornerRadius(pts).ToString("0"));
                }
                else
                {
                    Check(pts.Count > 120, t.id + " resamples to a real circuit", pts.Count);
                    // Every race should cover roughly the same ground, which is
                    // what keeps one fuel and wear economy honest across four
                    // circuits. A strip is exempt — that is the point of it.
                    float km = t.RaceMeters / 1000f;
                    Check(km > 2.5f && km < 4.5f, t.id + " race is 2.5-4.5 km", km.ToString("0.00"));

                    float minR = MinCornerRadius(pts);
                    Check(minR >= 18f, t.id + " tightest corner is drivable",
                          minR.ToString("0.0") + " m");

                    float need = t.roadWidth + 2f * 10f;
                    float gap = MinSelfClearance(pts);
                    Check(gap >= need, t.id + " never runs into its own barriers",
                          gap.ToString("0.0") + " m vs " + need.ToString("0.0"));
                }

                CheckElevation(t, pts);

                // The tank has to be able to DO the race. A single flat
                // per-metre burn once put every tank in the game at 6.2 km,
                // which quietly made the 7 km parkway stage unfinishable by
                // anything in the catalog — and a race nobody can finish shows
                // up only as a button that will not light.
                float worstPct = WorstRaceTankPct(t);
                Check(worstPct < 85f, t.id + " fits inside a full tank for every car",
                      worstPct.ToString("0") + "% of the thirstiest tank in the game");

                var thumb = TrackCatalog.Thumbnail(t, 96);
                Check(thumb != null && OpaquePixels(thumb) > 200, t.id + " draws a map",
                      thumb != null ? OpaquePixels(thumb) : 0);
            }

            for (int i = 0; i < TrackCatalog.Count; i++)
                Check(TrackCatalog.SceneIndex(i) == i + 1, "scene index " + i + " is " + (i + 1));
            // Build settings ARE the contract SceneIndex assumes. A track added
            // to the catalog and not to the scene list sends the player to the
            // wrong circuit, or to no scene at all.
            var scenes = EditorBuildSettings.scenes;
            Check(scenes.Length == TrackCatalog.Count + 3,
                  "build settings hold home + every circuit + garage + pizzeria", scenes.Length);
            for (int i = 0; i < TrackCatalog.Count && i + 1 < scenes.Length; i++)
                Check(scenes[i + 1].path.EndsWith("/" + TrackCatalog.At(i).id + ".unity"),
                      "build index " + (i + 1) + " is " + TrackCatalog.At(i).id, scenes[i + 1].path);
            // The garage is addressed by a formula off the catalog length, so
            // the one way to get it wrong is to insert a scene before the
            // circuits — which would also silently re-point every race.
            Check(TrackCatalog.GarageSceneIndex == TrackCatalog.Count + 1,
                  "garage scene index sits after every circuit", TrackCatalog.GarageSceneIndex);
            Check(TrackCatalog.GarageSceneIndex < scenes.Length &&
                  scenes[TrackCatalog.GarageSceneIndex].path.EndsWith("/Garage.unity"),
                  "build index " + TrackCatalog.GarageSceneIndex + " is the garage",
                  TrackCatalog.GarageSceneIndex < scenes.Length
                      ? scenes[TrackCatalog.GarageSceneIndex].path : "missing");

            // The pizza shop, on the end. Same formula, same failure mode: it
            // is addressed by position, and DoWork sends the player there by
            // that number alone — a wrong one drops them onto a race track
            // holding nothing, with no way to tell what went wrong.
            Check(TrackCatalog.PizzeriaSceneIndex == TrackCatalog.GarageSceneIndex + 1,
                  "pizzeria scene index sits after the garage", TrackCatalog.PizzeriaSceneIndex);
            Check(TrackCatalog.PizzeriaSceneIndex < scenes.Length &&
                  scenes[TrackCatalog.PizzeriaSceneIndex].path.EndsWith("/Pizzeria.unity"),
                  "build index " + TrackCatalog.PizzeriaSceneIndex + " is the pizzeria",
                  TrackCatalog.PizzeriaSceneIndex < scenes.Length
                      ? scenes[TrackCatalog.PizzeriaSceneIndex].path : "missing");

            // THE PLAYER'S list, not the editor's.
            //
            // Everything above reads EditorBuildSettings, and every assertion
            // passed while the shipped WebGL player was missing the pizza shop
            // entirely — PSXBuildWebGL kept a SECOND hand-written scene list and
            // it had never been told about the new scene. LoadScene(13) on a
            // 13-scene build does nothing at all: no exception, no log the
            // player can see, just a button that appears dead. This is the
            // assertion that would have caught it.
            var shipped = PSXRacingBuilder.SceneOrder();
            Check(shipped.Length == scenes.Length,
                  "the WebGL scene list is the same LENGTH as the build settings",
                  shipped.Length + " vs " + scenes.Length);
            bool sameOrder = shipped.Length == scenes.Length;
            for (int i = 0; sameOrder && i < shipped.Length; i++)
                if (shipped[i] != scenes[i].path) sameOrder = false;
            Check(sameOrder, "...and the same scenes in the same ORDER");

            TestDeliveryJob();
            TestDeliverySlotCost();
            TestPizzaCargo();
            TestVertexSnapOff();
            TestWalkInScenesRender();
        }

        /// <summary>
        /// Every walk-in scene must actually DRAW.
        ///
        /// PSXCameraOutput takes the camera off the screen and renders into a
        /// RenderTexture; something then has to blit that texture back. Add the
        /// component and nothing else and the scene renders no world at all —
        /// and because WebGL does not clear the framebuffer between frames, what
        /// the player sees is the PREVIOUS scene's pixels with the new scene's
        /// overlay UI on top. The pizza shop shipped exactly that way and was
        /// reported as "GO TO WORK does not work, it just puts the walk button
        /// over the menu": the scene had loaded perfectly and was invisible.
        ///
        /// Nothing throws, nothing logs, and a screenshot of the scene taken by
        /// a preview camera looks perfect — the preview makes its own camera and
        /// never goes through this chain. So it is asserted here.
        /// </summary>
        static void TestWalkInScenesRender()
        {
            Line("walk-in scenes render:");
            foreach (var path in new[] { GarageSceneBuilder.ScenePath,
                                         PizzeriaSceneBuilder.ScenePath })
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!System.IO.File.Exists(path))
                {
                    Check(false, name + " scene exists (run the scene build)");
                    continue;
                }
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    path, UnityEditor.SceneManagement.OpenSceneMode.Additive);

                PSXRacing.PSXCameraOutput output = null;
                foreach (var go in scene.GetRootGameObjects())
                {
                    var f = go.GetComponentInChildren<PSXRacing.PSXCameraOutput>(true);
                    if (f != null) { output = f; break; }
                }
                Check(output != null, name + " has a PSXCameraOutput");
                Check(output != null && output.display != null,
                      name + " blits it back to the screen (display wired)");
                Check(output == null || output.height > 0,
                      name + " renders at a real height",
                      output != null ? output.height : 0);

                // PSX/Lit reads GLOBAL uniforms, not Unity's lights, and
                // PSXGlobals is the only thing that pushes them. A scene with a
                // Light and no PSXGlobals leaves ambient, sun and fog colour all
                // at ZERO — which is not a dark room, it is a working room
                // rendered entirely in black, with the interaction prompts
                // readable over a void. The pizza shop shipped exactly that.
                PSXRacing.PSXGlobals globals = null;
                PSXRacing.OnFoot.FirstPersonWalk walker = null;
                PSXRacing.OnFoot.FootScreen foot = null;
                foreach (var go in scene.GetRootGameObjects())
                {
                    globals = globals ?? go.GetComponentInChildren<PSXRacing.PSXGlobals>(true);
                    walker = walker ?? go.GetComponentInChildren<PSXRacing.OnFoot.FirstPersonWalk>(true);
                    foot = foot ?? go.GetComponentInChildren<PSXRacing.OnFoot.FootScreen>(true);
                }
                Check(globals != null, name + " has PSXGlobals (or it renders black)");
                Check(globals != null && globals.sun != null,
                      name + " PSXGlobals has a sun");
                Check(globals != null &&
                      (globals.ambient.r + globals.ambient.g + globals.ambient.b) > 0.05f,
                      name + " has non-black ambient",
                      globals != null ? globals.ambient.ToString() : "none");
                Check(globals != null && globals.fogFar > globals.fogNear && globals.fogFar > 0f,
                      name + " fog band is the right way round",
                      globals != null ? globals.fogNear + ".." + globals.fogFar : "none");
                Check(walker != null, name + " has a player who can walk");
                Check(foot != null, name + " has a FootScreen");

                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// The delivery shift: the job the player actually drives.
        ///
        /// Everything here is silent when it breaks. A delivery that routes to
        /// Charlotte finds no finish line and never pays; a pay roll that can
        /// return zero makes the whole job look like a bug; and the default job
        /// is the one thing a new career cannot change its mind about after the
        /// fact.
        /// </summary>
        static void TestDeliveryJob()
        {
            Line("delivery job:");

            int di = LifeRules.DefaultJobIndex;
            Check(LifeRules.Jobs[di].name == LifeRules.DeliveryJobName,
                  "a new career starts on pizza delivery", LifeRules.Jobs[di].name);

            var s = LifeRules.SeedNewGame("TESTER", 25, di);
            Check(s.playerJob == LifeRules.DeliveryJobName,
                  "and the seeded save says so", s.playerJob);

            // The route. Charlotte has no finish line — it is an open city with
            // no lap and no line to cross — so a delivery sent there would drive
            // forever and never get paid.
            bool everCity = false;
            for (int day = 1; day <= 40; day++)
                for (int slot = 0; slot < 3; slot++)
                {
                    s.day = day; s.slotIndex = slot;
                    int t = LifeRules.DeliveryTrackIndex(s);
                    if (t < 0 || t >= TrackCatalog.Count) { everCity = true; break; }
                    if (TrackCatalog.At(t).city) everCity = true;
                }
            Check(!everCity, "no delivery is ever routed to the open city");

            // The tank. A random venue is only an improvement if it is a venue
            // the car can finish: the parkway stage is 6.9 km with no forecourt
            // on it, and a driver dispatched there on a quarter tank stops on a
            // mountain with no pumps and no way to end the run. Rotating by day
            // made that a rare unlucky Tuesday; rolling at random would make it
            // one shift in eight.
            LifeRules.SeedFallbackCar(s);
            if (s.ActiveCar != null)
            {
                bool everDry = false; string sentTo = "";
                foreach (float tank in new[] { 100f, 45f, 18f, 8f })
                {
                    s.ActiveCar.fuel = tank;
                    for (int i = 0; i < 60; i++)
                    {
                        int t = LifeRules.DeliveryTrackIndex(s);
                        var def = TrackCatalog.At(t);
                        float need = LifeRules.RequiredFuelPct(def, s.ActiveCar);
                        // The last band is deliberately below anything in the
                        // catalog — there the fallback is allowed to send them
                        // to the SHORTEST run, and all that is asserted is that
                        // it picks the cheapest one rather than a random one.
                        if (tank >= 18f && need > tank)
                        { everDry = true; sentTo = def.name + " needs " + need.ToString("0") + "% on " + tank + "%"; }
                    }
                }
                Check(!everDry, "no delivery is routed past the tank", sentTo);

                // The fallback, which only runs when NOTHING in the catalog
                // fits. A DRY tank, not a low one: this first read 4%, and at 4%
                // the bridge sprints and the strips all still qualify, so the
                // first loop answered and the fallback was never reached — the
                // assertion was testing a path it could not enter and failed on
                // a perfectly correct answer.
                s.ActiveCar.fuel = 0f;
                float cheapest = float.MaxValue;
                for (int i = 0; i < TrackCatalog.Count; i++)
                    if (!TrackCatalog.At(i).city)
                        cheapest = Mathf.Min(cheapest,
                                   LifeRules.RequiredFuelPct(TrackCatalog.At(i), s.ActiveCar));
                Check(cheapest > 0f, "an empty tank really does fit nowhere",
                      cheapest.ToString("0.000") + "% needed for the cheapest run");
                var starved = TrackCatalog.At(LifeRules.DeliveryTrackIndex(s));
                Check(Mathf.Approximately(
                          LifeRules.RequiredFuelPct(starved, s.ActiveCar), cheapest),
                      "and gets the shortest run there is", starved.name);
                s.ActiveCar.fuel = 100f;
            }

            // The tip. Never zero, never negative, and it moves.
            s.day = 5; s.slotIndex = 0; s.daysSinceSleep = 0;
            int lo = int.MaxValue, hi = 0;
            for (int i = 0; i < 400; i++)
            {
                int p = LifeRules.RollDeliveryPay(s);
                lo = Mathf.Min(lo, p); hi = Mathf.Max(hi, p);
            }
            Check(lo > 0, "a drop always pays something", lo);
            Check(hi > lo, "and the tip varies", lo + ".." + hi);

            // Tiredness costs money, same as it does at every other job.
            s.daysSinceSleep = 0; int rested = 0;
            s.daysSinceSleep = 4; int wrecked = 0;
            for (int i = 0; i < 400; i++)
            {
                s.daysSinceSleep = 0; rested += LifeRules.RollDeliveryPay(s);
                s.daysSinceSleep = 4; wrecked += LifeRules.RollDeliveryPay(s);
            }
            Check(wrecked < rested, "a driver who has not slept earns less",
                  wrecked + " vs " + rested);

            // ---- the drop is GRADED now: clock, then box ------------------
            //
            // Every one of these is a rule the player is told about at the
            // counter and watches on the HUD, so every one of them is a rule
            // that can silently inverse itself in a refactor and still compile.

            // A par at every venue the roll can actually produce, and a
            // believable one: nothing is a five-second drop and nothing is an
            // afternoon.
            float parLo = float.MaxValue, parHi = 0f;
            string worst = "";
            for (int t = 0; t < TrackCatalog.Count; t++)
            {
                if (TrackCatalog.At(t).city) continue;
                float par = LifeRules.DeliveryParSeconds(t);
                if (par < parLo) { parLo = par; worst = TrackCatalog.At(t).name; }
                parHi = Mathf.Max(parHi, par);
            }
            // Ten seconds, not fifteen. The eighth mile is 201 m and pars at
            // 11.7 s, which LOOKS like a broken number and is the correct one: a
            // real eighth-mile ET is 9-10 s in a quick car and 13 in a slow one,
            // so the clock still separates a good drop from a bad one there. It
            // stays in the pool because the owner asked for a random race track
            // and the player does not get to pick — a twelve-second shift is a
            // lucky Tuesday, not a strategy.
            Check(parLo >= 10f, "the shortest drop is still a drive",
                  worst + " " + parLo.ToString("0") + "s");
            Check(parHi <= 900f, "and the longest is not an afternoon",
                  parHi.ToString("0") + "s");

            // The clock. Same box, same venue, three times: under par pays more
            // than par, and par pays more than crawling in.
            int venue = 0;
            for (int t = 0; t < TrackCatalog.Count; t++)
                if (!TrackCatalog.At(t).city) { venue = t; break; }
            float parV = LifeRules.DeliveryParSeconds(venue);
            var fast = LifeRules.ScoreDelivery(100, venue, parV * 0.7f, 0f, 0);
            var onPar = LifeRules.ScoreDelivery(100, venue, parV, 0f, 0);
            var late = LifeRules.ScoreDelivery(100, venue, parV * 2.4f, 0f, 0);
            Check(fast.tip > onPar.tip && onPar.tip > late.tip,
                  "a quicker drop tips better",
                  fast.tip + " > " + onPar.tip + " > " + late.tip);
            Check(late.tip > 0, "and a late one still pays something", late.tip);

            // The box. Same clock, worse driving.
            var clean = LifeRules.ScoreDelivery(100, venue, parV, 0f, 0);
            var scuffed = LifeRules.ScoreDelivery(100, venue, parV, 14f, 0);
            var crashed = LifeRules.ScoreDelivery(100, venue, parV, 26f, 1);
            Check(clean.tip > scuffed.tip && scuffed.tip > crashed.tip,
                  "hitting things costs the tip",
                  clean.tip + " > " + scuffed.tip + " > " + crashed.tip);
            Check(!clean.refused && Mathf.Approximately(clean.conditionMult, 1f),
                  "a clean run loses nothing to the box", clean.conditionMult);

            // A kerb is not a crash. The free allowance exists so the job is
            // graded on driving rather than on luck.
            var kerbed = LifeRules.ScoreDelivery(100, venue, parV, LifeRules.PizzaFreeDamage, 0);
            Check(kerbed.tip == clean.tip, "a scrape inside the allowance costs nothing",
                  kerbed.tip + " vs " + clean.tip);

            // The cliff. A genuine wreck is refused outright, and refused means
            // no money at all rather than a small one.
            var wreck = LifeRules.ScoreDelivery(100, venue, parV, 70f, 3);
            Check(wreck.refused, "a wrecked car gets the delivery denied",
                  wreck.condition.ToString("0.00"));
            Check(wreck.tip == 0, "and a refused drop pays nothing", wreck.tip);

            // Nothing on any path can hand out more than the quote plus the
            // speed bonus, or less than nothing.
            bool inBand = true; string bad = "";
            for (int i = 0; i < 600; i++)
            {
                var o = LifeRules.ScoreDelivery(100, venue,
                            Random.Range(1f, parV * 4f), Random.Range(0f, 90f),
                            Random.Range(0, 5));
                if (o.tip < 0 || o.tip > 100 * LifeRules.DeliveryFastMult + 1)
                { inBand = false; bad = o.tip.ToString(); break; }
            }
            Check(inBand, "no roll pays outside the quoted band", bad);
        }

        /// <summary>
        /// What a delivery COSTS: exactly one slot, and never an absence on the
        /// day it was worked.
        ///
        /// Both halves shipped wrong and neither was visible. The slot was spent
        /// twice — once when the player drove off and again when the result was
        /// banked — so one drop consumed two thirds of the day, and there is
        /// nothing on any screen that counts slots for a player to notice. And
        /// the attendance latch was set AFTER the spend, so a shift taken in the
        /// last slot rolled the day first, read "did not work today", and booked
        /// an absence for the night the player had just driven.
        ///
        /// A player would have felt both as "this job fires me for no reason"
        /// several in-game weeks later, which is the sort of thing that gets
        /// reported as a balance complaint and is not one.
        ///
        /// Driven through the real handoff rather than the pieces: the bug lived
        /// in the ORDER two correct-looking functions were called in.
        /// </summary>
        static void TestDeliverySlotCost()
        {
            Line("delivery slot cost:");

            // An afternoon run, start to finish. PizzaShift's half is inlined
            // (it is a MonoBehaviour in a scene) but in the same order.
            var s = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            LifeRules.SeedFallbackCar(s);
            s.slotIndex = LifeRules.FirstShiftSlot;
            int dayBefore = s.day;

            LifeRules.ClockOnShift(s);
            LifeRules.SpendActivitySlot(s);
            Check(s.workedToday, "clocking on counts as having worked today");
            Check(s.slotIndex == LifeRules.FirstShiftSlot + 1,
                  "driving off costs exactly one slot", s.slotIndex);

            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.Delivery = true;
            RaceHandoff.Solo = true;
            RaceHandoff.IsPractice = true;
            RaceHandoff.ResultReady = true;
            RaceHandoff.DeliveryPay = 40;
            RaceHandoff.TrackIndex = LifeRules.DeliveryTrackIndex(s);
            RaceHandoff.MetersDriven = 1200f;
            RaceHandoff.RaceTimeSeconds = LifeRules.DeliveryParSeconds(RaceHandoff.TrackIndex);
            LifeRules.ApplyRaceResult(s);

            Check(s.day == dayBefore, "arriving does not roll the day as well", s.day);
            Check(s.slotIndex == LifeRules.FirstShiftSlot + 1,
                  "and does not spend a second slot", s.slotIndex);
            Check(s.workDaysPresent == 1 && s.workDaysTotal == 1,
                  "one run is one day of attendance",
                  s.workDaysPresent + "/" + s.workDaysTotal);
            RaceHandoff.ClearAll();

            // The NIGHT shift, which is the case the ordering bug was invisible
            // in: spending the last slot rolls the day inside the shift itself,
            // so everything the rollover reads has to already be true.
            var n = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            LifeRules.SeedFallbackCar(n);
            n.slotIndex = 2;
            int nightDay = n.day;
            float repBefore = n.workRep;

            LifeRules.ClockOnShift(n);
            LifeRules.SpendActivitySlot(n);

            Check(n.day == nightDay + 1, "a night shift rolls into the next day", n.day);
            Check(n.consecutiveAbsences == 0,
                  "and is NOT recorded as a night the driver skipped", n.consecutiveAbsences);
            Check(n.workRep >= repBefore, "so it costs no work rep", n.workRep);
            Check(n.workDaysPresent == 1, "and is banked as a day worked", n.workDaysPresent);

            // Two runs in one day is still one day of attendance — the shop
            // counts days, and both open slots are workable on purpose.
            var d = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            d.slotIndex = LifeRules.FirstShiftSlot;
            LifeRules.ClockOnShift(d); LifeRules.SpendActivitySlot(d);
            LifeRules.ClockOnShift(d); LifeRules.SpendActivitySlot(d);
            Check(d.workDaysPresent == 1 && d.workDaysTotal == 1,
                  "two runs in a day is one day worked",
                  d.workDaysPresent + "/" + d.workDaysTotal);
        }

        /// <summary>
        /// The cargo: baked parts, the order that fills them, and the grade.
        ///
        /// Everything asserted here fails SILENTLY and looks like something
        /// else. A box baked on its edge still instantiates and still pays a
        /// tip — it is just carried like a briefcase, which is the bug this
        /// whole pass came from. A pizza wider than its box sits proud of it and
        /// reads as a missing lid. An order rolled longer than the carried stack
        /// is boxes the player is charged for and never sees.
        /// </summary>
        static void TestPizzaCargo()
        {
            Line("pizza cargo:");
            const string dir = "Assets/PSXRacing/Resources/PizzaCargo/";

            var box = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "pizza_box.prefab");
            Check(box != null, "the box is baked (run the scene build)");
            if (box == null) return;

            Vector3 bs = PrefabSize(box);
            Check(bs.y <= bs.x && bs.y <= bs.z,
                  "the box is FLAT, not on its edge", bs.ToString("0.000"));
            float w = Mathf.Max(bs.x, bs.z);
            Check(Mathf.Abs(w - PizzaCargoBaker.BoxWidthM) < 0.02f,
                  "and it is a pizza box rather than a coffee table",
                  w.ToString("0.000") + " m");
            Check(bs.y > 0.01f && bs.y < 0.12f, "with a box's thickness", bs.y.ToString("0.000"));

            // Every topping the order can roll has to exist, or a delivery
            // hands the player an empty box and nothing says why.
            int missing = 0; string firstMissing = "";
            for (int i = 0; i < PSXRacing.PizzaCargoBakerNames.ToppingCount; i++)
            {
                var t = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "pizza_top_" + i + ".prefab");
                if (t == null) { missing++; if (firstMissing.Length == 0) firstMissing = "pizza_top_" + i; }
            }
            Check(missing == 0, "every topping an order can roll is baked",
                  missing == 0 ? PSXRacing.PizzaCargoBakerNames.ToppingCount + " toppings" : firstMissing);

            var pizza = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "pizza_top_0.prefab");
            if (pizza != null)
            {
                Vector3 ps = PrefabSize(pizza);
                Check(ps.y <= ps.x && ps.y <= ps.z, "the pizza is flat too", ps.ToString("0.000"));
                Check(Mathf.Max(ps.x, ps.z) < w,
                      "and it FITS IN THE BOX",
                      Mathf.Max(ps.x, ps.z).ToString("0.000") + " in " + w.ToString("0.000"));
            }

            // The order. Never empty, never longer than the stack the pizzeria
            // scene actually builds, and never naming a topping that was not
            // baked.
            int lo = int.MaxValue, hi = 0; bool badIndex = false;
            for (int i = 0; i < 400; i++)
            {
                var o = LifeRules.RollOrderToppings(LifeRules.MaxOrderBoxes);
                lo = Mathf.Min(lo, o.Length); hi = Mathf.Max(hi, o.Length);
                foreach (int t in o)
                    if (t < 0 || t >= PSXRacing.PizzaCargoBakerNames.ToppingCount) badIndex = true;
            }
            Check(lo >= 1, "an order is never empty", lo);
            Check(hi <= LifeRules.MaxOrderBoxes, "and never taller than the carried stack", hi);
            Check(hi > lo, "and the size varies", lo + ".." + hi);
            Check(!badIndex, "every box names a topping that exists");

            // The grade runs off the SIMULATION when there is one, and the
            // override has to actually override — the damage tally passed in
            // here says "spotless" while the cargo says "ruined", and the cargo
            // is the one that must win.
            int venue = 0;
            for (int t = 0; t < TrackCatalog.Count; t++)
                if (!TrackCatalog.At(t).city) { venue = t; break; }
            float par = LifeRules.DeliveryParSeconds(venue);
            var byDamage = LifeRules.ScoreDelivery(100, venue, par, 0f, 0);
            var bySim = LifeRules.ScoreDelivery(100, venue, par, 0f, 0, cargoCondition: 0.1f);
            Check(byDamage.tip > 0 && bySim.refused,
                  "a ruined cargo overrides a clean damage score",
                  byDamage.tip + " vs refused=" + bySim.refused);
            var simFine = LifeRules.ScoreDelivery(100, venue, par, 90f, 4, cargoCondition: 1f);
            Check(!simFine.refused && simFine.tip == byDamage.tip,
                  "and an intact cargo survives a wrecked car", simFine.tip);

            // ---- the simulation itself, driven ---------------------------
            //
            // Three questions in order: does it sit still, does it survive a
            // corner, does a crash actually hurt it. All three fail silently and
            // catastrophically — a stack that explodes at rest refuses every
            // delivery in the game and nothing in the log says why.
            var sim = PizzaCargoSim.Run(shoot: false);
            Check(sim.built && sim.boxes == 3, "a three-box cargo stands up", sim.boxes);
            if (!sim.built) return;
            Check(sim.atRest > 0.995f,
                  "a parked car does not damage its own cargo", sim.atRest.ToString("0.000"));
            Check(sim.afterCorner > 0.98f,
                  "and one hard corner costs nothing", sim.afterCorner.ToString("0.000"));
            // The case that was actually reported: "the top pizza fell off on the
            // first turn even though I didn't wreck". The smooth corner above
            // passed the whole time — the difference is the SIGNAL, so this one
            // is the same corner with a real road's noise on it. A clean lap must
            // arrive with a clean load.
            Check(sim.afterRough > 0.90f,
                  "and a hard corner on a ROUGH road still costs nothing",
                  sim.afterRough.ToString("0.000"));
            Check(sim.afterCrash < sim.afterRough - 0.05f,
                  "but hitting a wall does", sim.afterCrash.ToString("0.000") +
                  " after " + sim.afterRough.ToString("0.000"));
            Line("  .. " + sim.detail);
        }

        static Vector3 PrefabSize(GameObject prefab)
        {
            var probe = (GameObject)Object.Instantiate(prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rs = probe.GetComponentsInChildren<MeshRenderer>(true);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Object.DestroyImmediate(probe);
            return b.size;
        }

        /// <summary>
        /// The renderer the player actually gets.
        ///
        /// PSXGlobals.vertexSnap is a SERIALISED field, so turning the default
        /// off in code changes nothing about a scene that was baked before that
        /// — the old `true` is sitting in the .unity file, and the only thing
        /// that clears it is a rebuild. The failure mode is silent and
        /// venue-shaped: one circuit nobody rebuilt keeps jittering while every
        /// other one is clean, which reads as that TRACK being broken.
        ///
        /// Worth an assertion for a second reason too. Every preview tool in
        /// this project forces _PSXSnap to 0 before it shoots, so no screenshot
        /// pass can ever catch this — the tools were showing a renderer the game
        /// did not have. See PSXGlobals.vertexSnap.
        /// </summary>
        static void TestVertexSnapOff()
        {
            Line("renderer:");
            int found = 0, jittering = 0, missing = 0;
            string names = "";
            foreach (var entry in PSXRacingBuilder.SceneOrder())
            {
                if (!System.IO.File.Exists(entry)) { missing++; continue; }
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    entry, UnityEditor.SceneManagement.OpenSceneMode.Additive);

                // The scene's OWN roots, not FindFirstObjectByType — this opens
                // additively, so a type search would happily answer out of
                // whatever scene the runner already had loaded and certify a
                // scene it never looked at.
                PSXRacing.PSXGlobals globals = null;
                foreach (var go in scene.GetRootGameObjects())
                {
                    var g = go.GetComponentInChildren<PSXRacing.PSXGlobals>(true);
                    if (g != null) { globals = g; break; }
                }
                if (globals != null)
                {
                    found++;
                    if (globals.vertexSnap)
                    {
                        jittering++;
                        names += (names.Length > 0 ? ", " : "") +
                                 System.IO.Path.GetFileNameWithoutExtension(entry);
                    }
                }
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, false);
            }
            Check(missing == 0, "every scene in the order is built", missing + " missing");
            Check(found > 0, "scenes carry a PSXGlobals to check", found);
            Check(jittering == 0, "no scene ships with vertex snapping on",
                  jittering == 0 ? "all " + found + " clean" : names);
        }

        /// <summary>Circumradius of waypoint triples, two apart so a single
        /// resampling wobble does not read as a hairpin.
        ///
        /// FLATTENED, like everything else here that asks a question about the
        /// SHAPE of a circuit rather than about its gradient. Once the tracks
        /// climb, a 10% crest bends the 3D triple by six degrees and reads here
        /// as a 240 m corner that is not there.</summary>
        static float MinCornerRadius(List<Vector3> pts)
        {
            int n = pts.Count;
            float min = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = Flat(pts[(i - 2 + n) % n]), b = Flat(pts[i]), c = Flat(pts[(i + 2) % n]);
                float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c), ca = Vector3.Distance(c, a);
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area < 1e-4f) continue;      // straight
                min = Mathf.Min(min, ab * bc * ca / (4f * area));
            }
            return min == float.MaxValue ? 9999f : min;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// The two numbers an authored height profile can get wrong, plus the
        /// structural checks on the bridges.
        ///
        /// GRADE is the drivable one: past about 15% a car with this game
        /// power-to-weight stops climbing, and the AI reads curvature rather
        /// than gradient, so it would sit at the bottom of the hill with the
        /// throttle pinned and never work out why. VERTICAL RADIUS is the one
        /// nothing else would ever report. The ground mesh is a 9 m grid and the
        /// road ribbon rides 12 cm above it, so a crest tighter than about 90 m
        /// radius puts the linearised ground THROUGH the tarmac between its own
        /// vertices — the road carpeted in triangles of hillside, at a scale
        /// small enough to read as a texture bug rather than as geometry. The
        /// 200 m floor below keeps a 4x margin on that.
        /// </summary>
        static void CheckElevation(TrackCatalog.TrackDef t, List<Vector3> pts)
        {
            // A heights array out of step with the control points is silent:
            // Sample drops it and builds a flat circuit, so the only symptom is
            // a track that used to have hills and quietly does not.
            Check(t.controlHeights == null || t.controlPoints == null
                  || t.controlHeights.Length == t.controlPoints.Length,
                  t.id + " has one height per control point",
                  (t.controlHeights != null ? t.controlHeights.Length : 0) + " vs "
                  + (t.controlPoints != null ? t.controlPoints.Length : 0));

            int n = pts.Count;
            bool hasEnds = t.drag || t.stage;
            float lo = float.MaxValue, hi = float.MinValue, maxGrade = 0f;
            for (int i = 0; i < n; i++)
            {
                lo = Mathf.Min(lo, pts[i].y); hi = Mathf.Max(hi, pts[i].y);
                // Clamped on a route with ends: wrapping measures the fake
                // "grade" between the finish and the start, which on a stage
                // that drops 46 m end to end reads as a cliff.
                int j = hasEnds ? Mathf.Min(i + 1, n - 1) : (i + 1) % n;
                maxGrade = Mathf.Max(maxGrade, Mathf.Abs(pts[j].y - pts[i].y) / TrackCatalog.Spacing);
            }

            if (t.drag)
            {
                // A strip that is not level is a strip where lane choice decides
                // the run before the tree does.
                Check(hi - lo < 0.01f, t.id + " is dead level", (hi - lo).ToString("0.000") + " m");
                Check(t.bridges == null || t.bridges.Length == 0, t.id + " has no bridges on it");
                return;
            }

            float minVertR = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = pts[hasEnds ? Mathf.Max(0, i - 3) : (i - 3 + n) % n],
                        b = pts[i],
                        c = pts[hasEnds ? Mathf.Min(n - 1, i + 3) : (i + 3) % n];
                float g1 = (b.y - a.y) / (3f * TrackCatalog.Spacing);
                float g2 = (c.y - b.y) / (3f * TrackCatalog.Spacing);
                float dg = Mathf.Abs(g2 - g1) / (3f * TrackCatalog.Spacing);
                if (dg > 1e-6f) minVertR = Mathf.Min(minVertR, 1f / dg);
            }

            Check(maxGrade < 0.15f, t.id + " grade is drivable",
                  (maxGrade * 100f).ToString("0.0") + "%");
            Check(minVertR > 200f, t.id + " crests are gentler than the ground grid",
                  minVertR.ToString("0") + " m");
            Line("  ..   " + t.id + " climbs " + (hi - lo).ToString("0.0") + " m, "
                 + (maxGrade * 100f).ToString("0.0") + "% max, vertical radius "
                 + (minVertR > 1e5f ? "flat" : minVertR.ToString("0") + " m"));

            if (t.bridges == null || t.bridges.Length == 0) return;
            float lap = t.LengthM;
            foreach (var span in t.bridges)
            {
                float len = Mathf.Repeat(span.y - span.x, lap);
                // Shorter than two ramps and the gorge never reaches full depth,
                // so the deck ends up spanning a saucer rather than a valley.
                Check(len > 2f * TrackCatalog.BridgeRampM,
                      t.id + " bridge span is longer than its own approaches",
                      len.ToString("0") + " m vs " + (2f * TrackCatalog.BridgeRampM));
                Check(span.x >= 0f && span.x < lap && span.y >= 0f && span.y <= lap * 2f,
                      t.id + " bridge span is on the lap", span.x + ".." + span.y);
            }
            float deepest = 0f;
            for (int i = 0; i < n; i++)
                deepest = Mathf.Max(deepest, TrackCatalog.BridgeBlend(t, i * TrackCatalog.Spacing));
            Check(deepest > 0.98f, t.id + " bridge reaches full depth somewhere",
                  deepest.ToString("0.00"));
        }

        /// <summary>Nearest approach between two stretches of the circuit that
        /// are not neighbours along it, IN PLAN.
        ///
        /// Deliberately blind to height. Two stretches 20 m apart vertically
        /// still have their barriers, their buildings and their trees inside
        /// each other, and the only thing that legitimately passes over itself
        /// is a bridge — which none of these layouts has, because they are polar
        /// loops and cannot self-intersect. Counting vertical separation as
        /// clearance would let one in silently.</summary>
        static float MinSelfClearance(List<Vector3> pts)
        {
            int n = pts.Count;
            float min = float.MaxValue;
            for (int i = 0; i < n; i++)
                for (int j = i + 22; j < n; j++)
                {
                    if (Mathf.Min(j - i, n - (j - i)) < 22) continue;
                    min = Mathf.Min(min, Vector3.Distance(Flat(pts[i]), Flat(pts[j])));
                }
            return min == float.MaxValue ? 9999f : min;
        }

        static int OpaquePixels(Texture2D tex)
        {
            int n = 0;
            foreach (var p in tex.GetPixels32()) if (p.a > 8) n++;
            return n;
        }

        // ---------------------------------------------------------------
        //  Hours
        // ---------------------------------------------------------------
        static void TestTimeOfDay()
        {
            Line("time of day:");
            Check(TimeOfDay.Count >= 5, "more than a morning/afternoon/night", TimeOfDay.Count);
            for (int i = 0; i < TimeOfDay.Count; i++)
            {
                var p = TimeOfDay.At(i);
                Check(!string.IsNullOrEmpty(p.name) && !string.IsNullOrEmpty(p.clock),
                      "hour " + i + " is named");
                // Fog is what an hour mostly IS here, and a near past its far
                // makes the whole world render at full fog colour.
                Check(p.fogNear < p.fogFar, p.name + " fog band is the right way round",
                      p.fogNear + ".." + p.fogFar);
                Check(p.sunIntensity > 0f, p.name + " has a sun", p.sunIntensity);
            }
            // Out-of-range slots must clamp rather than throw: TimeSlot came off
            // a save file and a corrupt one should not take the race scene down.
            Check(TimeOfDay.At(-5).name == TimeOfDay.At(0).name, "index clamps low");
            Check(TimeOfDay.At(999).name == TimeOfDay.At(TimeOfDay.Count - 1).name, "index clamps high");

            // A morning slot must never hand back a night, or the LifeSim's
            // clock and the sky stop agreeing with each other.
            bool bandsHold = true;
            for (int day = 1; day <= 40; day++)
            {
                int m = TimeOfDay.ForSlot(0, day);
                int a = TimeOfDay.ForSlot(1, day);
                int n = TimeOfDay.ForSlot(2, day);
                if (m > TimeOfDay.Noon) bandsHold = false;
                if (a < TimeOfDay.Morning || a > TimeOfDay.Afternoon) bandsHold = false;
                if (n < TimeOfDay.Sunset) bandsHold = false;
            }
            Check(bandsHold, "every slot stays inside its own band over 40 days");
            Check(TimeOfDay.ForSlot(0, 7) == TimeOfDay.ForSlot(0, 7), "the same day picks the same hour");
        }

        static void TestCameraViews()
        {
            Line("cameras:");
            int enumCount = System.Enum.GetValues(typeof(ChaseCamera.View)).Length;
            // The HUD indexes ViewNames by the enum value, so a view added to one
            // and not the other is an IndexOutOfRange the first time it is used.
            Check(ChaseCamera.ViewNames.Length == enumCount,
                  "every camera view has a name", ChaseCamera.ViewNames.Length + " vs " + enumCount);
            // The touch button indexes the short list every frame, so a view
            // added to the enum and not to it is an IndexOutOfRange in Update.
            Check(ChaseCamera.ShortNames.Length == enumCount,
                  "every camera view has a short name",
                  ChaseCamera.ShortNames.Length + " vs " + enumCount);
            Check(enumCount >= 7, "seven views or more", enumCount);
            // TOP DOWN has to stay the highest value in the enum. It is the one
            // conditional view — drag strips only — and the cycle drops it by
            // SHORTENING itself by one, which quietly cycles through the wrong
            // set the moment something is added after it.
            Check((int)ChaseCamera.View.TopDown == enumCount - 1,
                  "top-down is the last view in the cycle", (int)ChaseCamera.View.TopDown);
        }

        // ---------------------------------------------------------------
        //  Tools and inspection
        // ---------------------------------------------------------------
        static void TestToolbox()
        {
            Line("toolbox:");
            var s = LifeRules.SeedNewGame("TOOLS", 25, 3);
            Check(Toolbox.Owned(s, Toolbox.Jack), "the floor jack comes free with the first car");
            Check(!Toolbox.Owned(s, Toolbox.Lift), "the lift does not");

            s.money = 10;
            Check(Toolbox.Buy(s, Toolbox.Lift) != null, "cannot buy a lift with $10");
            s.money = 5000;
            Check(Toolbox.Buy(s, Toolbox.Lift) == null, "can buy one with $5,000");
            Check(Toolbox.Owned(s, Toolbox.Lift), "and then owns it");
            Check(s.money == 5000 - 2200, "and paid for it", s.money);
            Check(Toolbox.Buy(s, Toolbox.Lift) != null, "cannot buy it twice");
        }

        /// <summary>
        /// The invariant that matters most here is coverage: every fault the
        /// pools can roll must be findable somewhere on the inspection map. A
        /// hidden fault with no home is one the player can never diagnose and
        /// never repair, and nothing in the game would ever report that — the
        /// car would simply be permanently, inexplicably slow.
        /// </summary>
        static void TestInspection()
        {
            Line("inspection:");
            Check(Inspection.Order.Length == 8, "eight components", Inspection.Order.Length);

            var homes = new HashSet<string>();
            var subKeys = new HashSet<string>();
            foreach (var c in Inspection.Order)
            {
                var subs = Inspection.SubsOf(c);
                Check(subs.Length > 0, Inspection.Name(c) + " has sub-checks", subs.Length);
                foreach (var sub in subs)
                {
                    Check(subKeys.Add((int)c + ":" + sub.key),
                          "unique sub key " + Inspection.Name(c) + "/" + sub.key);
                    // A sub that can find something must say what finding it
                    // looks like, or the reveal prints an empty line.
                    Check(sub.ids.Length == 0 || !string.IsNullOrEmpty(sub.found),
                          sub.label + " has prose for a find");
                    Check(!string.IsNullOrEmpty(sub.clean), sub.label + " has prose for a clean check");
                    foreach (var id in sub.ids) homes.Add(id);
                }
            }

            var orphans = new List<string>();
            foreach (var p in FaultCatalog.Pools)
                if (!homes.Contains(p.id) && !orphans.Contains(p.id)) orphans.Add(p.id);
            Check(orphans.Count == 0, "every pool fault has somewhere to be found",
                  orphans.Count == 0 ? null : string.Join(", ", orphans.ToArray()));

            // The roll stays inside its band whatever the tools are, including
            // the worst case: an underside check with no lift and no lamp.
            // SeedNewGame(name, age, job) deliberately does NOT hand out a car —
            // the wizard picks one — so the fallback has to be seeded by hand or
            // ActiveCar is null and every car-shaped assertion below is an NRE
            // rather than a failure.
            var s = LifeRules.SeedNewGame("INSPECT", 25, 3);
            LifeRules.SeedFallbackCar(s);
            var bare = Toolbox.AccessFor(s, s.ActiveCar);
            var loaded = new Toolbox.Access { lift = true, impact = true, scope = true, lamp = true,
                                              raise = Toolbox.Raise.Lift };
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var c in Inspection.Order)
                foreach (var sub in Inspection.SubsOf(c))
                    foreach (var acc in new[] { bare, loaded })
                    {
                        s.mechSkill = acc.lift ? 100f : 0f;
                        float p = Inspection.FindChance(s, sub, acc);
                        lo = Mathf.Min(lo, p); hi = Mathf.Max(hi, p);
                    }
            Check(lo >= 0.05f && hi <= 0.95f, "find chance stays in 0.05-0.95",
                  lo.ToString("0.00") + ".." + hi.ToString("0.00"));

            // The raise ladder. Underside checks are the reason the jack exists,
            // so the thing worth asserting is that a car on the floor cannot
            // reach them and a car in the air can — and that the frame rails
            // stay behind the lift whatever else the player owns. Every one of
            // these reads as "the button did nothing" if it silently inverts.
            var oilPan = System.Array.Find(Inspection.SubsOf(Inspection.Comp.Engine),
                                           x => x.key == "oilpan");
            var rails = System.Array.Find(Inspection.SubsOf(Inspection.Comp.Body),
                                          x => x.key == "framerails");
            var plugCheck = System.Array.Find(Inspection.SubsOf(Inspection.Comp.Engine),
                                              x => x.key == "plugs");
            Check(oilPan != null && rails != null && plugCheck != null,
                  "the ladder's three worked examples exist");
            if (oilPan != null && rails != null && plugCheck != null)
            {
                var ground = new Toolbox.Access { raise = Toolbox.Raise.Ground };
                var stands = new Toolbox.Access { raise = Toolbox.Raise.Stands };
                var lifted = new Toolbox.Access { lift = true, raise = Toolbox.Raise.Lift };
                Check(!Inspection.Reachable(oilPan, ground), "no underside check on the ground");
                Check(Inspection.Reachable(oilPan, stands), "stands open the underside");
                Check(Inspection.Reachable(plugCheck, ground),
                      "a check you can make standing up needs no jack");
                Check(!Inspection.Reachable(rails, stands), "frame rails refuse the stands");
                Check(Inspection.Reachable(rails, lifted), "frame rails open on the lift");
                Check(!string.IsNullOrEmpty(Inspection.RefusalFor(oilPan, ground)) &&
                      !string.IsNullOrEmpty(Inspection.RefusalFor(rails, stands)),
                      "every refusal says what would fix it");
                s.mechSkill = 0f;
                Check(Inspection.FindChance(s, oilPan, lifted) >
                      Inspection.FindChance(s, oilPan, stands),
                      "the lift beats the stands underneath");

                // Nobody pulls a wheel with the car sat on it, so an impact
                // wrench on the ground buys nothing and the same wrench with
                // the car on stands buys everything.
                var pads = System.Array.Find(Inspection.SubsOf(Inspection.Comp.Wheels),
                                             x => x.key == "pads");
                var wrenchDown = new Toolbox.Access { impact = true, raise = Toolbox.Raise.Ground };
                var wrenchUp = new Toolbox.Access { impact = true, raise = Toolbox.Raise.Stands };
                Check(pads != null, "the brake pad check exists");
                if (pads != null)
                {
                    Check(Inspection.FindChance(s, pads, wrenchDown) <= 0.15f,
                          "a wrench with the car on the floor is still guesswork",
                          Inspection.FindChance(s, pads, wrenchDown));
                    Check(Inspection.FindChance(s, pads, wrenchUp) > 0.15f,
                          "the same wrench with it on stands takes the wheel off",
                          Inspection.FindChance(s, pads, wrenchUp));
                }
            }

            // Owning a lift is not the same as being under the car, and the two
            // must not drift: putting a car up puts THAT car up.
            var upCar = s.ActiveCar;
            if (upCar == null) { Check(false, "the seeded career has a car to raise"); return; }
            Check(Toolbox.RaiseOf(s, upCar) == Toolbox.Raise.Ground, "a car starts on its wheels");
            Toolbox.ToggleRaise(s, upCar);
            Check(Toolbox.RaiseOf(s, upCar) == Toolbox.Raise.Stands,
                  "with no lift owned, the jack is what you get", upCar.raised);
            s.tools.Add(Toolbox.Lift);
            Toolbox.ToggleRaise(s, upCar);      // down
            Toolbox.ToggleRaise(s, upCar);      // and back up, now onto the lift
            Check(Toolbox.RaiseOf(s, upCar) == Toolbox.Raise.Lift,
                  "a bought lift is what the same button raises onto", upCar.raised);
            s.tools.Remove(Toolbox.Lift);
            Check(Toolbox.RaiseOf(s, upCar) == Toolbox.Raise.Stands,
                  "a save claiming a lift nobody owns is clamped, not trusted");
            Toolbox.SetRaise(s, upCar, Toolbox.Raise.Ground);

            // Entering costs a slot; re-entering the same day does not, so a
            // mis-tap in the garage cannot burn a third of the player's day.
            var car = s.ActiveCar;
            Check(car != null, "the seeded career has a car to inspect");
            if (car == null) return;
            int slots = s.slotsActiveToday;
            Inspection.Enter(s, car);
            Check(s.slotsActiveToday == slots + 1, "opening an inspection costs a slot");
            int after = s.slotsActiveToday;
            Inspection.Enter(s, car);
            Check(s.slotsActiveToday == after, "re-entering the same day is free");

            // A planted hidden fault is findable, and the latch means one tap
            // per sub per day whatever the answer was.
            car.faults.Clear();
            car.faults.Add(new CarFault { id = "spark_plugs", label = "Worn Spark Plugs",
                                          hidden = true, diagnosed = false, stat = "engine" });
            s.mechSkill = 100f;
            var plugs = System.Array.Find(Inspection.SubsOf(Inspection.Comp.Engine),
                                          x => x.key == "plugs");
            Check(plugs != null, "the spark plug check exists");

            int found = 0;
            for (int attempt = 0; attempt < 40 && found == 0; attempt++)
            {
                car.inspectedSubs.Clear();
                var res = Inspection.Check(s, car, Inspection.Comp.Engine, plugs);
                if (res.revealed.Count > 0) found++;
                // Second call on the same day must latch rather than re-roll.
                var again = Inspection.Check(s, car, Inspection.Comp.Engine, plugs);
                Check(attempt > 0 || again.revealed.Count == 0, "a sub only rolls once a day");
                if (found > 0) break;
                car.faults[0].hidden = true; car.faults[0].diagnosed = false;
            }
            Check(found > 0, "a planted hidden fault can be found");
            Check(!car.faults[0].hidden, "and stops being hidden once it is");

            // A hidden fault afflicts the car BEFORE it is found. If it did not,
            // inspecting would only ever cost money.
            var sick = new OwnedCar();
            sick.faults.Add(new CarFault { id = "spark_plugs", hidden = true, label = "x" });
            var agg = FaultCatalog.Aggregate_(sick);
            var eff = FaultCatalog.Effect("spark_plugs");
            Check(Mathf.Abs(agg.accelMult - eff.accelMult) < 0.001f,
                  "a hidden fault still slows the car", agg.accelMult);
        }

        /// <summary>
        /// The X-ray reads the catalog's own engine and drivetrain strings, so
        /// the thing that can silently break is the PARSE: an eType this does
        /// not recognise falls back to an inline four, and a V12 drawn as a
        /// four-pot is wrong in a way no exception reports.
        /// </summary>
        static void TestCarXray()
        {
            Line("x-ray:");
            Check(CarXray.ShapeOf("L4 (DOHC)").kind == "inline", "L4 is an inline");
            Check(CarXray.ShapeOf("L4 (DOHC)").perBank == 4, "L4 has four pots",
                  CarXray.ShapeOf("L4 (DOHC)").perBank);
            Check(CarXray.ShapeOf("V8 (OHV)").kind == "vee", "V8 is a vee");
            Check(CarXray.ShapeOf("V8 (OHV)").perBank == 4, "V8 is four a bank",
                  CarXray.ShapeOf("V8 (OHV)").perBank);
            Check(CarXray.ShapeOf("Boxer4").kind == "flat", "a boxer lies flat");
            Check(CarXray.ShapeOf("Rotor2 (Rotary)").kind == "rotary", "Rotor2 is a rotary");
            // The source data carries one 'Rotar2' typo and special-cases it.
            Check(CarXray.ShapeOf("Rotar2").kind == "rotary", "so is the Rotar2 typo");
            Check(CarXray.ShapeOf(null).kind == "inline", "an unknown engine still draws something");

            if (!CarCatalog.Ready) { Check(false, "catalog loaded for the x-ray sweep"); return; }

            // Sweep the whole catalog: count how many fall through to the
            // default. A handful is expected (blank eType); a hundred means the
            // parser is missing a spelling the data actually uses.
            int unparsed = 0, cars = 0;
            var layouts = new HashSet<string>();
            foreach (var spec in CarCatalog.All)
            {
                cars++;
                if (!string.IsNullOrEmpty(spec.drv)) layouts.Add(spec.drv.ToUpperInvariant());
                if (string.IsNullOrEmpty(spec.eType)) continue;
                var sh = CarXray.ShapeOf(spec.eType);
                bool looksDefault = sh.kind == "inline" && sh.perBank == 4;
                bool reallyL4 = spec.eType.ToUpperInvariant().StartsWith("L4");
                if (looksDefault && !reallyL4) unparsed++;
            }
            Check(unparsed * 20 < cars, "the engine parser covers the catalog",
                  unparsed + " of " + cars + " fell through");

            // Every layout code the catalog uses must have a branch, or those
            // cars draw the FR fallback and quietly claim the wrong layout.
            var known = new HashSet<string> { "FR", "FF", "MR", "RR", "4WD" };
            var missing = new List<string>();
            foreach (var l in layouts) if (!known.Contains(l)) missing.Add(l);
            Check(missing.Count == 0, "every drivetrain code the catalog uses has a layout",
                  missing.Count == 0 ? null : string.Join(", ", missing.ToArray()));

            // Block dimensions have to stay inside the car, or the drawing
            // spills past the body outline.
            bool fits = true;
            foreach (var spec in CarCatalog.All)
            {
                var d = CarXray.EngineDims(CarXray.ShapeOf(spec.eType), 4.1f, 1.72f);
                if (d.x > 4.1f * 0.6f || d.y > 1.72f * 0.85f) fits = false;
            }
            Check(fits, "no engine block is drawn bigger than the car it is in");
        }

        static void TestCalendarPipeline()
        {
            Line("calendar (35 days):");
            var s = LifeRules.SeedNewGame("TESTER", 25, 3);   // OFFICE JOB
            s.money = 100000;                                  // isolate from bill pressure

            int paydays = 0, bills = 0;
            for (int i = 0; i < 35; i++)
            {
                int before = s.calendarLog.Count;
                s.pendingSalary += 100;      // stand in for a worked week
                s.workedToday = true;        // no-show ladder is not under test here
                s.foodStock = 1;
                LifeRules.EatMeal(s, "regular");
                LifeRules.Sleep(s);
                for (int j = before; j < s.calendarLog.Count; j++)
                {
                    if (s.calendarLog[j].Contains("PAYDAY")) paydays++;
                    if (s.calendarLog[j].Contains("BILLS") ||
                        s.calendarLog[j].Contains("bills")) bills++;
                }
            }

            Check(s.day == 36, "35 sleeps land on day 36", s.day);
            // Day 1 is a Friday and months are a flat 30 days, so 35 days covers
            // Fridays 1/8/15/22/29 and exactly one 1st-of-month (day 31).
            Check(paydays == 5, "5 paydays in 35 days", paydays);
            Check(bills == 1, "1 bills fire in 35 days", bills);
            Check(s.health >= 95f, "a fed, rested month keeps health high", s.health);

            // The other direction: the hunger ladder must actually bite, or the
            // three-decisions-a-day tension the whole clock exists for is fake.
            var starve = LifeRules.SeedNewGame("TESTER", 25, 3);
            starve.money = 100000;
            for (int i = 0; i < 20; i++) { starve.workedToday = true; LifeRules.Sleep(starve); }
            Check(starve.health <= 0f, "20 days without food is fatal-grade", starve.health);
            // NOTE: nothing currently CONSUMES health at 0 — faithful to RG2,
            // where health only gates gym level 3 and a recovery bonus. If the
            // starvation ladder should have teeth, that is a design decision to
            // make deliberately, not a port bug to fix quietly.
        }

        static void TestRepairEconomy()
        {
            Line("repair economy:");
            var s = LifeRules.SeedNewGame("TESTER", 25, 3);
            LifeRules.SeedFallbackCar(s);      // the lane picker is UI; take the built-in
            var car = s.ActiveCar;
            Check(car != null, "seeded car exists");
            if (car == null) return;

            // Grind the car down the way racing does, until something breaks.
            int races = 0;
            while (car.faults.Count == 0 && races < 40)
            {
                car.fuel = 100f;
                RaceHandoff.ResultReady = true;
                RaceHandoff.CarId = car.id;
                RaceHandoff.MetersDriven = 3f * 1168f;
                RaceHandoff.FinishPos = 2;
                RaceHandoff.FieldSize = 4;
                RaceHandoff.DriftSeconds = 8f;
                LifeRules.ApplyRaceResult(s);
                races++;
            }
            Check(car.faults.Count > 0, "a fault surfaces within 40 races (took " + races + ")",
                  car.faults.Count);
            if (car.faults.Count == 0) return;

            var f = car.faults[0];
            Check(!string.IsNullOrEmpty(f.label) && f.cost > 0,
                  "the fault is catalogued (named + priced): " + f.label + " $" + f.cost);
            Check(f.stat == "engine" || f.stat == "tires" || f.stat == "hp",
                  "fault sits on a real stat lane", f.stat);

            // Dealer is instant, so it isolates the repair math from the queue.
            var q = FaultCatalog.GetQuote(s, car, f, FaultCatalog.Venue.Dealer);
            var qm = FaultCatalog.GetQuote(s, car, f, FaultCatalog.Venue.Mechanic);
            var qd = FaultCatalog.GetQuote(s, car, f, FaultCatalog.Venue.Diy);
            Check(q.price > qm.price && qm.price > qd.price,
                  "dealer > mechanic > DIY pricing", qd.price + "/" + qm.price + "/" + q.price);
            Check(q.days == 0 && qm.days >= 1, "dealer same-day, mechanic takes days");
            Check(!qd.available, "DIY is out of reach at starting skill 15 (diff " + qd.difficulty + ")");

            float before = StatOf(car, f.stat);
            s.money = 999999;
            string err = LifeRules.OrderRepair(s, car, f, FaultCatalog.Venue.Dealer);
            Check(err == null, "dealer repair is accepted", err);
            Check(StatOf(car, f.stat) > before,
                  "the repaired stat went UP", before + " -> " + StatOf(car, f.stat));
            Check(!car.faults.Exists(x => x.id == f.id), "the fault is gone");

            // Queued repairs must not resolve early, and must resolve on time.
            if (car.faults.Count > 0)
            {
                var f2 = car.faults[0];
                var q2 = FaultCatalog.GetQuote(s, car, f2, FaultCatalog.Venue.Mechanic);
                LifeRules.OrderRepair(s, car, f2, FaultCatalog.Venue.Mechanic);
                Check(s.pendingParts.Count == 1, "the job is queued", s.pendingParts.Count);
                LifeRules.Sleep(s);
                bool stillQueued = s.pendingParts.Count > 0;
                Check(q2.days <= 1 || stillQueued, "a multi-day job does not finish overnight");
                for (int i = 0; i < 8; i++) LifeRules.Sleep(s);
                Check(s.pendingParts.Count == 0, "the job resolves once its day arrives",
                      s.pendingParts.Count);
            }

            // The whole point of L1: wear must be a round trip, not a ratchet.
            var worn = s.ActiveCar;
            worn.engine = 20f;
            int svcCost = LifeRules.ServiceCost(worn, LifeRules.MechanicServices[1].cost);
            s.money = svcCost;
            LifeRules.BuyService(s, worn, 1);   // ENGINE TUNE-UP +35
            Check(worn.engine > 20f, "a service restores condition", worn.engine);
            Check(s.money == 0, "and charges for it", s.money);
        }

        /// <summary>
        /// The pad's map of a page: down goes down, right goes right.
        ///
        /// Worth asserting because nothing else can see it. The navigation
        /// graph is built at runtime over a UI that is also built at runtime,
        /// so a compile proves nothing and a screenshot shows a page that looks
        /// perfectly fine while the cursor walks through it sideways — which is
        /// exactly what happened: every control was chained in CREATION order,
        /// so a repair row's DIY / MECH / DLR buttons, which sit side by side,
        /// were three presses of DOWN. A page with four faults on it took ten.
        ///
        /// Built from bare RectTransforms rather than from a real screen: with
        /// anchorMin == anchorMax a rect resolves from its own sizeDelta and
        /// needs no canvas, so this measures the graph and not the layout.
        /// </summary>
        static void TestMenuNavigation()
        {
            Line("menu navigation:");
            var root = new GameObject("NavTest", typeof(RectTransform));
            var rootRT = (RectTransform)root.transform;

            UnityEngine.UI.Selectable Btn(string name, float x, float y, float w, float h)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(rootRT, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(w, h);
                rt.anchoredPosition = new Vector2(x, y);
                var img = go.AddComponent<UnityEngine.UI.Image>();
                var b = go.AddComponent<UnityEngine.UI.Button>();
                b.targetGraphic = img;
                return b;
            }

            // A page shaped like the garage tab: a full-width caption row, then
            // a fault's three venue quotes side by side, then another one.
            var head = Btn("head", 0f, 0f, 600f, 44f);
            var diy1 = Btn("diy1", -260f, -60f, 250f, 40f);
            var mech1 = Btn("mech1", 0f, -60f, 250f, 40f);
            var dlr1 = Btn("dlr1", 260f, -60f, 250f, 40f);
            var diy2 = Btn("diy2", -260f, -140f, 250f, 40f);
            var all = new List<UnityEngine.UI.Selectable>
                { head, diy1, mech1, dlr1, diy2 };

            var lines = MenuNav.Lines(all);
            Check(lines.Count == 3, "three side-by-side buttons are ONE line, not three",
                  lines.Count + " lines");

            MenuNav.Grid(all);
            Check(diy1.navigation.selectOnRight == mech1, "right off DIY reaches MECH");
            Check(mech1.navigation.selectOnRight == dlr1, "and MECH reaches DLR");
            Check(diy1.navigation.selectOnDown == diy2,
                  "DOWN off DIY skips the rest of its row and lands on the next one");
            Check(dlr1.navigation.selectOnLeft == mech1, "left off DLR walks back");
            Check(dlr1.navigation.selectOnRight == null,
                  "and right off the last button in a row does not wrap across the page");
            Check(diy1.navigation.selectOnUp == head, "UP off the row reaches the caption above it");
            Check(mech1.navigation.selectOnUp == head,
                  "from every button in the row, not just the first");

            // The tab bar joins to the LINE under it, at the column you left.
            var tabA = Btn("tabA", -260f, 120f, 250f, 44f);
            var tabB = Btn("tabB", 260f, 120f, 250f, 44f);
            var tabs = new List<UnityEngine.UI.Selectable> { tabA, tabB };
            MenuNav.JoinLines(tabs, all, tabB);
            Check(head.navigation.selectOnUp == tabB,
                  "UP off the body returns to the tab you are ON, not the first tab");

            Object.DestroyImmediate(root);
        }

        static void TestFaultGate()
        {
            Line("fault gate:");
            var s = LifeRules.SeedNewGame("TESTER", 25, 3);
            LifeRules.SeedFallbackCar(s);
            var car = s.ActiveCar;
            car.faults.Clear();

            // One fault per stat at normal severity; a second only at severe.
            var a = FaultCatalog.RollWearFault(car, "engine", false);
            if (a != null) car.faults.Add(a);
            var b = FaultCatalog.RollWearFault(car, "engine", false);
            Check(a != null, "first engine fault rolls");
            Check(b == null, "a second normal-severity engine fault is refused");

            var c = FaultCatalog.RollWearFault(car, "engine", true);
            if (c != null) car.faults.Add(c);
            Check(c != null, "a severe roll still gets through");
            Check(c == null || c.id != a.id, "and is a different fault");

            var d = FaultCatalog.RollWearFault(car, "engine", true);
            Check(d == null, "but the lane caps at two");

            var t = FaultCatalog.RollWearFault(car, "tires", false);
            Check(t != null, "a different stat lane is unaffected");

            // Nothing diagnoses itself. This is the whole contract of the
            // inspection layer, and it was broken for a whole release by two
            // words in the roller — so it is asserted rather than assumed.
            Check(a == null || a.hidden, "a rolled fault arrives HIDDEN");
            Check(a == null || !a.diagnosed, "and undiagnosed");

            // ...and the two people you can pay to look. The dealer is the
            // deterministic one, so it is the one a test can assert on.
            s.money = 500000;
            car.faults.Clear();
            car.faults.Add(new CarFault { id = "spark_plugs", label = "Worn Plugs",
                                          stat = "engine", hidden = true, diagnosed = false });
            int before = s.slotIndex + s.day * 3;
            Inspection.BookPro(s, car, Inspection.Pro.Dealer);
            Check(!car.faults[0].hidden, "a dealer inspection reveals a hidden fault");
            Check(s.slotIndex + s.day * 3 > before, "and costs a time slot");
            Check(car.proInspectDay == s.day, "and leaves a mark on the CAR");

            TestOldSaveMigration();
        }

        /// <summary>
        /// A career carried over from before the hidden layer landed.
        ///
        /// Every fault rolled under the old rules was written
        /// hidden=false/diagnosed=true, so a save from then lists parts by name
        /// that nobody inspected for — which is how this was reported. The
        /// migration cannot know WHO found what, so it reads the car's own
        /// inspection history, and both halves of that judgement are worth
        /// pinning: the untouched car goes quiet, the one somebody has been
        /// under keeps what it knows.
        ///
        /// The trap under this one is that proInspectDay is an ADDED field.
        /// JsonUtility hands an old save back with it at 0, not the -1 it
        /// initialises to, and 0 is a day before the game starts — so a
        /// migration that reads it before normalising it decides every car has
        /// seen a dealer and re-hides nothing at all.
        /// </summary>
        static void TestOldSaveMigration()
        {
            Line("save migration (v6 -> v7):");

            var s = new LifeState { saveVersion = 6, day = 40 };

            var untouched = new OwnedCar { id = "old1", displayName = "Untouched" };
            untouched.proInspectDay = 0;      // what JsonUtility actually hands back
            untouched.faults.Add(new CarFault { id = "spark_plugs", label = "Worn Plugs",
                                                stat = "engine", hidden = false, diagnosed = true });

            var looked = new OwnedCar { id = "old2", displayName = "Inspected", inspectDay = 12 };
            looked.proInspectDay = 0;
            looked.faults.Add(new CarFault { id = "spark_plugs", label = "Worn Plugs",
                                             stat = "engine", hidden = false, diagnosed = true });

            s.cars.Add(untouched);
            s.cars.Add(looked);
            LifeSimManager.Migrate(s);

            Check(s.saveVersion >= 7, "the save is stamped forward");
            Check(untouched.faults[0].hidden,
                  "a fault on a car nobody inspected goes back to hidden");
            Check(!untouched.faults[0].diagnosed, "and undiagnosed with it");
            Check(!looked.faults[0].hidden,
                  "but a fault on a car the player HAS inspected is left alone");
            Check(untouched.proInspectDay < 0,
                  "and the 0 JsonUtility invents becomes -1, not day zero");

            // Idempotent: Migrate runs on every load, and a second pass must
            // not re-hide what an inspection has since revealed.
            untouched.faults[0].hidden = false;
            untouched.faults[0].diagnosed = true;
            untouched.proInspectDay = 41;
            LifeSimManager.Migrate(s);
            Check(!untouched.faults[0].hidden,
                  "a second load does not re-hide what has since been found");
        }

        /// <summary>
        /// Every car must have a voice, and every voice a folder on disk. The
        /// failure this guards against is silent by construction: a family key
        /// with no imported clips leaves EngineAudio with an empty band ladder,
        /// and an empty ladder is not a crash — it is a car that drives along in
        /// total silence, which is easy to miss in a race with four opponents.
        /// </summary>
        /// <summary>
        /// The body-shell layer. Everything it can get wrong is silent at
        /// runtime: a key with no baked prefab quietly falls back to the FD, a
        /// shell with no livery renders untextured, and a bad measurement gives
        /// a car a wheelbase from another vehicle. None of that throws.
        ///
        /// The FD's own numbers are asserted literally. It is the car every
        /// handling decision in this project was made against, and the baker is
        /// allowed to re-measure fifteen other models precisely because it is
        /// not allowed to move that one.
        /// </summary>
        static void TestCarModels()
        {
            Line("car models:");

            int missing = 0, noSkin = 0, noMesh = 0;
            var absent = new List<string>();
            foreach (var m in CarModelLibrary.Models)
            {
                string prefab = "Assets/PSXRacing/Resources/CarModels/" + m.key + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null)
                {
                    missing++; absent.Add(m.key); continue;
                }
                var def = CarModelLibrary.Load(m.key);
                if (def == null) { missing++; absent.Add(m.key); continue; }
                if (def.bodyMesh == null || def.wheelMesh == null) noMesh++;
                if (def.SkinCount == 0) noSkin++;
            }
            Check(missing == 0, "every library key has a baked prefab",
                  missing > 0 ? string.Join(",", absent) : null);
            Check(noMesh == 0, "every shell has a body and a wheel mesh", noMesh + " without");
            Check(noSkin == 0, "every shell has at least one livery", noSkin + " without");
            if (missing > 0) return;

            // The bonnet camera sits at the measured cowl, and a cowl measured
            // AT ROOF HEIGHT puts the lens on the roof of a car it is supposed
            // to be looking along. That is exactly what the first version of the
            // scan produced on the fastbacks, and it is invisible in a build
            // log — the numbers all look like numbers.
            var badCowl = new List<string>();
            var badRoof = new List<string>();
            var badLens = new List<string>();
            var badEye = new List<string>();
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def == null) continue;
                float halfLen = def.colliderSize.z * 0.5f;
                // Behind the nose, not behind the BOX: the collider is a 0.955
                // fit, and a cab-over's windscreen base is legitimately a few
                // centimetres ahead of its front face.
                bool zOk = def.cowlZ > def.colliderCenter.z - halfLen &&
                           def.cowlZ <= def.noseZ;
                // Well below the roof, not merely below it. Every version of
                // this scan that failed did so by stopping ON the roof, and at
                // a two-centimetre margin all of them passed: a Land Rover, a
                // CX and an A80 all measured a cowl within 6 cm of their own
                // roofline and the check called it fine. The whole pack now
                // clears 0.35 m, so 0.15 is a floor no honest car goes near.
                bool yOk = def.cowlY > 0.45f && def.cowlY < def.roofY - 0.15f;
                // How much of the car is bonnet. Every way this scan has gone
                // wrong lands outside this band and NOTHING ELSE would say so:
                // a wing mistaken for the roof gave a Daytona a bonnet 95% of
                // its own length, and a bumper mistaken for the cowl gave a
                // Charger one 7% long. A cab-over van is the honest low end at
                // about 0.18.
                float frac = (def.noseZ - def.cowlZ) / Mathf.Max(def.colliderSize.z, 0.01f);
                bool fracOk = frac >= -0.01f && frac < 0.62f;
                if (!zOk || !yOk || !fracOk)
                    badCowl.Add(m.key + "(z" + def.cowlZ.ToString("0.00") + " y" +
                                def.cowlY.ToString("0.00") + " " + frac.ToString("0.00") + "L)");
                if (def.roofY < 0.8f || def.roofY > 2.6f) badRoof.Add(m.key);

                // And then the thing the measurement is FOR. A cowl that reads
                // like a cowl can still leave the lens inside the car, which is
                // what shipped: the bonnet camera stood 10 cm off a cowl that
                // was itself 10 cm low, put the lens 3 mm above the windscreen,
                // and the near plane did the rest — the whole nose of the car
                // vanished and the player looked out through their own engine
                // bay. So check the camera, not just the number under it.
                Vector3 lens = ChaseCamera.MountOffset(ChaseCamera.View.Hood,
                                                       def.colliderCenter, def.colliderSize, def);
                bool onMeasurement = Mathf.Abs(lens.z - (def.cowlZ + 0.05f)) < 0.001f;
                bool clearsPanel = lens.y - def.cowlY >= ChaseCamera.MountNearClip * 0.8f;
                if (!onMeasurement || !clearsPanel)
                    badLens.Add(m.key + "(lens " + lens.y.ToString("0.00") + " over " +
                                def.cowlY.ToString("0.00") + (onMeasurement ? "" : " FALLBACK") + ")");

                // The driver's seat, same argument. A cockpit eye is wrong in
                // exactly two silent ways — through the roof, or out in front of
                // the windscreen looking back at nothing — and both of them are
                // a picture rather than an exception. It also has to stay INSIDE
                // the body box lengthways: a cab-forward shell has its cowl near
                // the middle of the car, and half a metre further back from
                // there is a camera in the boot.
                Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit,
                                                      def.colliderCenter, def.colliderSize, def);
                float eyeHalfLen = def.colliderSize.z * 0.5f;
                bool inCabin = eye.y > def.cowlY && eye.y < def.roofY;
                bool inBody = eye.z < def.cowlZ &&
                              eye.z > def.colliderCenter.z - eyeHalfLen &&
                              eye.z < def.colliderCenter.z + eyeHalfLen;
                bool offCentre = eye.x < -0.15f && eye.x > -def.colliderSize.x * 0.5f;
                if (!inCabin || !inBody || !offCentre)
                    badEye.Add(m.key + "(eye " + eye.y.ToString("0.00") + "/" +
                               eye.z.ToString("0.00") + " cowl " + def.cowlY.ToString("0.00") +
                               "/" + def.cowlZ.ToString("0.00") + " roof " + def.roofY.ToString("0.00") + ")");
            }
            Check(badCowl.Count == 0, "every shell's cowl is on the car and below its roof",
                  badCowl.Count == 0 ? null : string.Join(" ", badCowl.ToArray()));
            Check(badRoof.Count == 0, "every shell has a believable roof height",
                  badRoof.Count == 0 ? null : string.Join(" ", badRoof.ToArray()));
            Check(badLens.Count == 0, "every shell's bonnet camera clears its own bonnet",
                  badLens.Count == 0 ? null : string.Join(" ", badLens.ToArray()));
            Check(badEye.Count == 0, "every shell seats its driver in its own cabin",
                  badEye.Count == 0 ? null : string.Join(" ", badEye.ToArray()));
            // The standoff and the near plane are one decision, and it is the
            // near plane that sets it: the bonnet enters frame at clearance /
            // tan(halfFOV + pitch), so a standoff under about 0.77x the near
            // plane puts bodywork closer to the lens than the plane it is
            // clipped against. Tightening one without the other silently
            // reopens the bug for every car at once.
            Check(ChaseCamera.MountClearance >= ChaseCamera.MountNearClip * 0.77f,
                  "mounted-camera standoff clears its own near plane",
                  ChaseCamera.MountClearance + " vs " + ChaseCamera.MountNearClip);

            var fd = CarModelLibrary.Load(CarModelLibrary.Default);
            Check(Mathf.Abs(fd.wheelbase - 2.425f) < 0.005f, "reference FD wheelbase unmoved", fd.wheelbase);
            Check(Mathf.Abs(fd.trackWidth - 1.46f) < 0.005f, "reference FD track unmoved", fd.trackWidth);
            Check(Mathf.Abs(fd.wheelRadius - 0.31f) < 0.002f, "reference FD tyre unmoved", fd.wheelRadius);
            Check(fd.wheelMaterial != null, "reference FD keeps its own wheel material");

            // Geometry sanity across the set. A wheelbase outside this band or a
            // tyre outside that one means a mesh was measured in the wrong axis
            // or the wrong units, which is the failure this whole bake is built
            // to make impossible.
            int odd = 0;
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def.wheelbase < 2.0f || def.wheelbase > 3.3f) odd++;
                else if (def.trackWidth < 1.0f || def.trackWidth > 2.0f) odd++;
                else if (def.wheelRadius < 0.22f || def.wheelRadius > 0.42f) odd++;
                else if (def.colliderSize.z < 2.5f || def.colliderSize.z > 6f) odd++;
            }
            Check(odd == 0, "every shell measures like a road car", odd + " out of band");

            if (!CarCatalog.Ready) { Check(false, "catalog loaded"); return; }

            int unresolved = 0, hand = 0;
            var used = new Dictionary<string, int>();
            foreach (var c in CarCatalog.All)
            {
                string key = CarModelLibrary.KeyFor(c);
                if (CarModelLibrary.Get(key) == null) { unresolved++; continue; }
                used[key] = (used.TryGetValue(key, out int n) ? n : 0) + 1;
                if (CarModelLibrary.HandKey(c) != null) hand++;

                var def = CarModelLibrary.Load(key);
                int skin = def.SkinFor(c.color, 0);
                if (skin < 0 || skin >= def.SkinCount) unresolved++;
            }
            Check(unresolved == 0, "every catalog car resolves to a real shell and livery", unresolved);
            Line($"  ..   {hand}/{CarCatalog.All.Count} hand-mapped, " +
                 $"{used.Count} of {CarModelLibrary.Models.Length} shells raced " +
                 "(the van and the work truck are scenery)");
            // A scorer that collapses onto one shell is worse than no scorer:
            // it would mean the weights are not separating anything.
            Check(used.Count >= 10, "the field is spread across the pack", used.Count);
        }

        static void TestEngineVoices()
        {
            Line("engine voices:");
            if (!CarCatalog.Ready) { Check(false, "catalog loaded"); return; }

            var families = new Dictionary<string, int>();
            int noFamily = 0, noAsp = 0;
            foreach (var c in CarCatalog.All)
            {
                if (string.IsNullOrEmpty(c.engineFamily)) noFamily++;
                else families[c.engineFamily] =
                    (families.TryGetValue(c.engineFamily, out int n) ? n : 0) + 1;
                if (c.asp != "NA" && c.asp != "TURBO" && c.asp != "SuperCharger") noAsp++;
            }
            Check(noFamily == 0, "every car names an engine family", noFamily + " without");
            Check(noAsp == 0, "every car has a known aspiration", noAsp + " odd");
            Line("  ..   " + families.Count + " distinct families across " +
                 CarCatalog.All.Count + " cars");

            int missingDir = 0;
            var missingNames = new List<string>();
            foreach (var fam in families.Keys)
            {
                // Checked on disk rather than through Resources.Load: in
                // batchmode the Resources cache can answer for an asset the
                // importer has not finished with, and "the file is there" is the
                // claim that actually matters for the build.
                string dir = "Assets/PSXRacing/Resources/Engines/" + fam;
                if (!AssetDatabase.IsValidFolder(dir)) { missingDir++; missingNames.Add(fam); }
            }
            Check(missingDir == 0, "every family has an imported clip folder",
                  missingDir > 0 ? string.Join(",", missingNames) : null);

            // The band ladder names the clips it wants; a family missing one of
            // them loses that rung silently.
            int missingClips = 0;
            foreach (var fam in families.Keys)
            {
                foreach (var b in EngineVoiceLibrary.PlayerBands)
                {
                    if (!ClipExists(fam, b.onClip)) missingClips++;
                    if (!string.IsNullOrEmpty(b.offClip) && b.offClip != b.onClip &&
                        !ClipExists(fam, b.offClip)) missingClips++;
                }
                foreach (var extra in new[] { "maxRPM", "intake_on", "intake_off", "startup", "engine_stop" })
                    if (!ClipExists(fam, extra)) missingClips++;
            }
            Check(missingClips == 0, "every family has every clip the ladder plays",
                  missingClips + " missing");

            int turbo = 0, sc = 0;
            foreach (var c in CarCatalog.All)
            {
                if (c.IsTurbo) turbo++;
                if (c.IsSupercharged) sc++;
            }
            Line("  ..   forced induction: " + turbo + " turbo, " + sc + " supercharged, " +
                 (CarCatalog.All.Count - turbo - sc) + " NA");
            Check(turbo > 0 && sc > 0 && turbo + sc < CarCatalog.All.Count,
                  "aspiration actually varies across the catalog");
        }

        static bool ClipExists(string family, string clip) =>
            System.IO.File.Exists(Application.dataPath +
                "/PSXRacing/Resources/Engines/" + family + "/" + clip + ".ogg");

        /// <summary>
        /// The tuning ladder. The two things worth asserting are that it always
        /// moves the numbers the right WAY (a stage must never make a car slower
        /// or heavier) and that its prices stay inside a range a career can
        /// actually pay — an $80k stage-1 on a supercar is how an economy quietly
        /// becomes decorative.
        /// </summary>
        static void TestUpgrades()
        {
            Line("upgrades:");
            if (!CarCatalog.Ready) { Check(false, "catalog loaded"); return; }

            int badHeadroom = 0;
            foreach (var c in CarCatalog.All)
                if (c.builtHp < c.hp || c.minKg > c.kg || c.minKg <= 0) badHeadroom++;
            Check(badHeadroom == 0, "built HP >= stock and min weight <= stock",
                  badHeadroom + " inverted");

            int nonMonotonic = 0;
            foreach (var c in CarCatalog.All)
            {
                for (int st = 1; st <= CarTune.MaxStage; st++)
                {
                    if (CarTune.PowerAtStage(c.hp, c.builtHp, st) <
                        CarTune.PowerAtStage(c.hp, c.builtHp, st - 1)) { nonMonotonic++; break; }
                    if (CarTune.WeightAtStage(c.kg, c.minKg, st) >
                        CarTune.WeightAtStage(c.kg, c.minKg, st - 1)) { nonMonotonic++; break; }
                }
            }
            Check(nonMonotonic == 0, "every stage is an improvement", nonMonotonic + " backwards");

            // Brakes must never out-run the tyres. Stock rubber caps at 1.05 g,
            // and a full brake build on a strong car would otherwise reach 1.3.
            var stock = new CarTune.Stages { brakes = 4, tires = 0 };
            var built = new CarTune.Stages { brakes = 4, tires = 4 };
            Check(CarTune.BrakeDemandG(0.9f, stock) <= CarTune.BrakeGCapStock + 1e-4f,
                  "stage-4 brakes on stock tyres stay under the tyre cap",
                  CarTune.BrakeDemandG(0.9f, stock).ToString("0.000") + " g");
            Check(CarTune.BrakeDemandG(0.9f, built) > CarTune.BrakeDemandG(0.9f, stock),
                  "tyres raise the ceiling the brakes work against");

            // Quote every stage of every category on a cheap car and an exotic.
            var s = LifeRules.SeedNewGame("TUNER", 25, 0);
            s.money = 100000000;
            s.mechSkill = 100f;
            foreach (var probe in new[] { CarCatalog.All[0], CarCatalog.All[CarCatalog.All.Count - 1] })
            {
                var car = new OwnedCar
                {
                    id = "probe_" + probe.id, displayName = probe.name, specId = probe.id,
                    catalogPrice = probe.price, paidPrice = probe.price,
                };
                s.cars.Add(car);
                s.activeCar = car.id;

                int cheapest = int.MaxValue, dearest = 0;
                for (int k = 0; k <= (int)Upgrades.Kind.Tires; k++)
                {
                    var kind = (Upgrades.Kind)k;
                    for (int st = 1; st <= CarTune.MaxStage; st++)
                    {
                        var plan = Upgrades.NextStagePlan(s, car, probe, kind);
                        if (!plan.valid) { Check(false, "plan " + kind + " stage " + st); break; }
                        cheapest = Mathf.Min(cheapest, plan.diyPrice);
                        dearest = Mathf.Max(dearest, plan.shopPrice);
                        Check(plan.shopPrice > plan.diyPrice, "shop costs more than DIY (" + kind + ")");
                        // Order it and land it, so the next iteration quotes the
                        // real next stage rather than the same one five times.
                        string err = Upgrades.Order(s, car, probe, kind, false);
                        Check(err == null, "order " + kind + " stage " + st, err);
                        // Sleep the build off. Going through Sleep rather than
                        // poking s.day is the point: it proves a stage actually
                        // lands through the same rollover the repairs use, which
                        // is where a job with no fault id could have been dropped.
                        for (int d = 0; d < plan.days; d++) LifeRules.Sleep(s);
                    }
                    Check(Upgrades.GetStage(car, kind) == CarTune.MaxStage,
                          "four stages of " + kind + " land", Upgrades.GetStage(car, kind));
                }
                Line("  ..   " + probe.name + " (" + MenuKit.Money(probe.price) + "): stages cost " +
                     MenuKit.Money(cheapest) + " - " + MenuKit.Money(dearest));
                Check(cheapest >= 40 && dearest < 200000,
                      "stage prices stay in a payable range for " + probe.name);

                Check(Upgrades.EffectiveHp(car, probe) == probe.builtHp,
                      "a full power build reaches the engine's ceiling",
                      Upgrades.EffectiveHp(car, probe));
                Check(Upgrades.EffectiveKg(car, probe) == probe.minKg,
                      "a full weight build reaches the minimum weight",
                      Upgrades.EffectiveKg(car, probe));
            }

            // Mods. The one rule with teeth is that a car which already makes
            // boost cannot be sold a supercharger — the offer has to READ the
            // catalog's aspiration, not just assume every car is a candidate.
            CarSpec na = null, turbo = null;
            foreach (var c in CarCatalog.All)
            {
                if (na == null && c.asp == "NA") na = c;
                if (turbo == null && c.IsTurbo) turbo = c;
                if (na != null && turbo != null) break;
            }
            if (na != null && turbo != null)
            {
                var naCar = new OwnedCar { id = "m_na", specId = na.id, displayName = na.name };
                var tCar = new OwnedCar { id = "m_t", specId = turbo.id, displayName = turbo.name };
                s.cars.Add(naCar); s.cars.Add(tCar);
                Check(Upgrades.OfferFor(s, naCar, na, Upgrades.Mod.Supercharger).available,
                      "a blower is offered on an NA car");
                Check(!Upgrades.OfferFor(s, tCar, turbo, Upgrades.Mod.Supercharger).available,
                      "a blower is refused on a turbo car",
                      Upgrades.OfferFor(s, tCar, turbo, Upgrades.Mod.Supercharger).blockedReason);
                Check(Upgrades.OfferFor(s, tCar, turbo, Upgrades.Mod.WeldedDiff).available,
                      "a welded diff is offered on any car");

                Check(Upgrades.OrderMod(s, naCar, na, Upgrades.Mod.Supercharger) == null,
                      "the blower can be bought");
                Check(naCar.supercharged, "and it lands on the car");
                Check(!Upgrades.OfferFor(s, naCar, na, Upgrades.Mod.Supercharger).available,
                      "and cannot be bought twice");
            }
        }

        static void TestCarCatalog()
        {
            Line("car catalog:");
            Check(CarCatalog.Ready, "rg2_cars.json loads", CarCatalog.All.Count);
            if (!CarCatalog.Ready) return;

            int noCurve = 0, badGears = 0, badTop = 0;
            foreach (var c in CarCatalog.All)
            {
                if (c.curveRPM == null || c.curveRPM.Length < 2) noCurve++;
                if (c.gears < 3 || c.gears > 8) badGears++;
                if (c.topSpeedMps < 20f || c.topSpeedMps > 130f) badTop++;
            }
            Check(noCurve == 0, "every car has a torque curve", noCurve + " without");
            Check(badGears == 0, "gear counts are sane (3-8)", badGears + " odd");
            Check(badTop == 0, "top speeds are sane (72-468 km/h)", badTop + " odd");

            // Gear ratios must descend: if the derivation inverts, first gear
            // becomes an overdrive and the car cannot pull away.
            int nonMonotonic = 0;
            foreach (var c in CarCatalog.All)
            {
                var r = c.BuildGearRatios(0.31f, 4.10f);
                for (int i = 1; i < r.Length; i++)
                    if (r[i] >= r[i - 1]) { nonMonotonic++; break; }
            }
            Check(nonMonotonic == 0, "gear ratios descend on every car",
                  nonMonotonic + " inverted");

            var fd = FindSpec("RX-7 Type RS");
            if (fd != null)
            {
                var r = fd.BuildGearRatios(0.31f, 4.10f);
                Line("  ..   FD ratios: " + string.Join(" / ",
                     System.Array.ConvertAll(r, v => v.ToString("0.00"))));
                Check(r[0] > 2f && r[r.Length - 1] < 1.2f,
                      "FD first gear is a real reduction and top is an overdrive");
            }

            // Drivetrain coverage — FF and 4WD only became drivable this pass.
            int ff = 0, awd = 0;
            foreach (var c in CarCatalog.All)
            {
                if (c.drv == "FF") ff++;
                if (c.drv == "4WD") awd++;
            }
            Check(ff > 0 && awd > 0, "FF and 4WD cars exist in the catalog",
                  ff + " FF, " + awd + " 4WD");
            var ffCar = FindDrv("FF");
            Check(ffCar != null && ffCar.FrontDriveShare == 1f, "FF sends all torque forward");
            var awdCar = FindDrv("4WD");
            Check(awdCar != null && awdCar.FrontDriveShare > 0f && awdCar.FrontDriveShare < 1f,
                  "4WD splits torque", awdCar?.FrontDriveShare);
            var frCar = FindDrv("FR");
            Check(frCar != null && frCar.FrontDriveShare == 0f, "FR stays rear-driven");
        }

        static CarSpec FindSpec(string namePart)
        {
            foreach (var c in CarCatalog.All)
                if (c.name.Contains(namePart)) return c;
            return null;
        }

        static CarSpec FindDrv(string drv)
        {
            foreach (var c in CarCatalog.All) if (c.drv == drv) return c;
            return null;
        }

        static void TestMarket()
        {
            Line("used-car market:");
            var s = LifeRules.SeedNewGame("TESTER", 25, 3);
            Check(s.cars.Count == 0, "the character seeds with NO car (lane picker follows)",
                  s.cars.Count);
            Check(s.creditScore >= 350 && s.creditScore <= 850,
                  "starting credit is in range", s.creditScore);
            Check(s.newspaper.Count > 0, "the classifieds have listings", s.newspaper.Count);

            var lanes = CarMarket.RollStartingLanes(s.basePay, s.creditScore);
            Check(lanes.Count >= 2, "starting lanes are offered", lanes.Count);
            foreach (var lane in lanes)
                Check(lane.spec != null && lane.price > 0,
                      "lane '" + lane.label + "' is a real car: " + lane.spec?.name +
                      " " + MenuKit.Money(lane.price));

            // H1287: picking a financed lane must not touch starting cash.
            var financed = lanes.Find(l => l.financed);
            if (financed != null)
            {
                int before = s.money;
                CarMarket.ApplyStartingLane(s, financed);
                Check(s.money == before, "a financed lane does NOT deduct savings (H1287)",
                      before + " -> " + s.money);
                Check(s.carLoans.Count == 1, "but the loan follows you", s.carLoans.Count);
                Check(s.carLoans[0].monthlyPayment > 0, "with a real monthly payment",
                      s.carLoans[0].monthlyPayment);
            }
            Check(s.cars.Count == 1 && !string.IsNullOrEmpty(s.activeCar),
                  "the chosen car is owned and active");
            var owned = s.ActiveCar;
            Check(owned.catalogPrice > 0, "the owned car carries its catalog price",
                  owned.catalogPrice);
            Check(CarCatalog.Get(owned.specId) != null, "and resolves back to a catalog spec");

            // Buying: cash path, garage cap, and the value model.
            s.garageSlots = 2;
            s.money = 500000;
            var listing = s.newspaper[0];
            var cash = CarMarket.FinanceOptions(s, listing)[0];
            string err = CarMarket.Buy(s, listing, cash);
            Check(err == null, "a cash purchase is accepted", err);
            Check(s.cars.Count == 2, "the car lands in the garage", s.cars.Count);
            Check(!s.newspaper.Contains(listing), "and leaves the classifieds");

            var third = s.newspaper.Count > 0 ? s.newspaper[0] : null;
            if (third != null)
            {
                string capped = CarMarket.Buy(s, third, CarMarket.FinanceOptions(s, third)[0]);
                Check(capped != null, "a full garage refuses the next purchase", capped);
            }

            // Selling: the last car is never sellable, and loans are paid off.
            int money0 = s.money;
            var toSell = s.cars[1];
            Check(CarMarket.QuickSell(s, toSell) == null, "quick-sell works with a spare car");
            Check(s.money > money0, "and pays out", s.money - money0);
            Check(CarMarket.QuickSell(s, s.cars[0]) != null, "the only car cannot be sold");

            // Value model: condition and mileage both have to bite.
            var a = s.cars[0];
            a.engine = a.tires = a.carHP = a.paint = 100f; a.odoMiles = 0f;
            int mint = CarMarket.CarValue(a);
            a.engine = a.tires = a.carHP = a.paint = 40f;
            int rough = CarMarket.CarValue(a);
            a.odoMiles = 150000f;
            int roughHighMiles = CarMarket.CarValue(a);
            Check(mint > rough && rough > roughHighMiles,
                  "value falls with condition, then with mileage",
                  mint + " > " + rough + " > " + roughHighMiles);

            // Depreciation model (starting lanes only) must actually depreciate.
            int fresh = CarMarket.DepreciatedPrice(30000, 1998, 1999, 100f, 12000f);
            int old = CarMarket.DepreciatedPrice(30000, 1985, 1999, 60f, 180000f);
            Check(fresh < 30000 && old < fresh, "depreciation compounds with age",
                  fresh + " vs " + old);

            // Loan amortization sanity: paying monthly*months must exceed principal.
            int pay = CarMarket.LoanPayment(10000f, 0.105f, 48);
            Check(pay * 48 > 10000, "a loan costs more than the principal", pay + "/mo");
            Check(CarMarket.LoanPayment(1200f, 0f, 12) == 100, "0% APR splits evenly");
        }

        // ---------------------------------------------------------------
        // P2: the field the player actually races.

        static void TestRaceField()
        {
            Line("race field:");
            var s = LifeRules.SeedNewGame("TESTER", 25, 3);
            LifeRules.SeedFallbackCar(s);
            var car = s.ActiveCar;

            RaceHandoff.ClearAll();
            Check(LifeRules.FillOpponentField(s), "a field is chosen for the seeded car");
            var ids = (RaceHandoff.OpponentSpecIds ?? "").Split(';');
            var skills = (RaceHandoff.OpponentSkills ?? "").Split(';');
            Check(ids.Length == LifeRules.FieldOpponents,
                  "the grid fills to " + LifeRules.FieldOpponents, ids.Length);
            Check(skills.Length == ids.Length, "skills are parallel to ids", skills.Length);

            int unresolved = 0, dupes = 0;
            var seen = new HashSet<string>();
            float cheapest = float.MaxValue, dearest = 0f;
            foreach (var id in ids)
            {
                var spec = CarCatalog.Get(id);
                if (spec == null) { unresolved++; continue; }
                if (!seen.Add(id)) dupes++;
                cheapest = Mathf.Min(cheapest, spec.price);
                dearest = Mathf.Max(dearest, spec.price);
            }
            Check(unresolved == 0, "every opponent id resolves to a catalog car", unresolved);
            Check(dupes == 0, "and no car shows up twice", dupes);
            Check(cheapest > car.catalogPrice * 0.3f && dearest < car.catalogPrice * 4f,
                  "the field is priced near the player's car",
                  MenuKit.Money((int)cheapest) + "-" + MenuKit.Money((int)dearest) +
                  " vs " + MenuKit.Money(car.catalogPrice));

            foreach (var sk in skills)
            {
                float.TryParse(sk, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float v);
                Check(v >= 0.80f && v <= 1.05f, "skill " + sk + " is in AIDriver's range");
            }

            // Reputation should raise the company the player keeps. Sampled
            // rather than compared once: the band overlaps between tiers by
            // design, so a single draw proves nothing either way.
            float lowRep = MeanFieldPrice(s, 0f, 12);
            float highRep = MeanFieldPrice(s, 90f, 12);
            Check(highRep > lowRep, "a high-rep field averages further up the catalog",
                  MenuKit.Money((int)lowRep) + " -> " + MenuKit.Money((int)highRep));

            // At-fault incidents: counted, and capped so one bad night cannot
            // spend the whole L5 allowance.
            RaceHandoff.ClearAll();
            int incidents0 = s.atFaultIncidents;
            car.fuel = 100f;
            RaceHandoff.ResultReady = true;
            RaceHandoff.CarId = car.id;
            RaceHandoff.MetersDriven = 3f * 1168f;
            RaceHandoff.FinishPos = 2;
            RaceHandoff.FieldSize = 4;
            RaceHandoff.HardHits = 9;
            LifeRules.ApplyRaceResult(s);
            Check(s.atFaultIncidents - incidents0 == LifeRules.MaxIncidentsPerRace,
                  "nine hits in one race record " + LifeRules.MaxIncidentsPerRace + " incidents",
                  s.atFaultIncidents - incidents0);
        }

        /// <summary>Average catalog price of a field drawn at a given rep.</summary>
        static float MeanFieldPrice(LifeState s, float rep, int samples)
        {
            float repWas = s.streetRep;
            s.streetRep = rep;
            float total = 0f; int n = 0;
            for (int i = 0; i < samples; i++)
            {
                RaceHandoff.ClearAll();
                if (!LifeRules.FillOpponentField(s)) continue;
                foreach (var id in RaceHandoff.OpponentSpecIds.Split(';'))
                {
                    var spec = CarCatalog.Get(id);
                    if (spec != null) { total += spec.price; n++; }
                }
            }
            s.streetRep = repWas;
            RaceHandoff.ClearAll();
            return n > 0 ? total / n : 0f;
        }

        // ---------------------------------------------------------------
        // L4: the blacklist ladder.

        static void TestBlacklist()
        {
            Line("blacklist:");
            Check(Blacklist.Rivals.Length == 10, "ten rivals", Blacklist.Rivals.Length);

            int unresolved = 0;
            float prevSkill = 0f;
            bool skillAscends = true;
            for (int rank = 10; rank >= 1; rank--)
            {
                var r = Blacklist.ByRank(rank);
                if (r == null) { unresolved++; continue; }
                if (Blacklist.ResolveCar(r) == null) unresolved++;
                if (r.skill < prevSkill) skillAscends = false;
                prevSkill = r.skill;
            }
            Check(unresolved == 0, "every rival resolves a signature car from the catalog",
                  unresolved + " unresolved");
            // Worth printing: the roster is name patterns against a baked
            // catalog, so a re-bake can silently hand a rival a different car.
            for (int rank = 10; rank >= 1; rank--)
            {
                var r = Blacklist.ByRank(rank);
                var rc = Blacklist.ResolveCar(r);
                Line("  ..   #" + rank + " " + r.alias.PadRight(9) + " skill " +
                     r.skill.ToString("0.00") + "  " +
                     (rc != null ? rc.name + " (" + rc.hp + "hp " + rc.drv + ", " +
                                   MenuKit.Money(rc.price) + ")" : "UNRESOLVED"));
            }
            // The cars are chosen for identity and come out non-monotonic, so
            // skill is the thing that has to climb or the ladder gets easier.
            Check(skillAscends, "AI skill climbs from rank 10 to rank 1");

            var s = LifeRules.SeedNewGame("TESTER", 25, 3);
            LifeRules.SeedFallbackCar(s);
            var entry = Blacklist.ByRank(10);
            Check(Blacklist.StatusOf(s, entry) == RivalStatus.Locked,
                  "a fresh driver cannot challenge anyone");
            Check(Blacklist.OpenRival(s) == null, "and nothing is open");

            // Clear the entry gate.
            s.streetRacesWon = entry.gateWins;
            s.streetRep = entry.gateRep;
            Check(Blacklist.StatusOf(s, entry) == RivalStatus.Open, "the gate opens rank 10");
            Check(Blacklist.StatusOf(s, Blacklist.ByRank(9)) == RivalStatus.Locked,
                  "rank 9 stays locked while rank 10 stands");

            // The pager is one-shot.
            Check(Blacklist.TickPager(s) != null, "the call-out fires");
            Check(Blacklist.TickPager(s) == null, "and does not fire twice");
            int mailCount = s.mail.Count;

            // Rep decay re-locks an UNFOUGHT rival, and the latch does not re-page.
            s.streetRep = entry.gateRep - 5;
            Check(Blacklist.StatusOf(s, entry) == RivalStatus.Locked,
                  "rep decay can close a gate again");
            s.streetRep = entry.gateRep;
            Check(Blacklist.TickPager(s) == null, "a reopened gate does not re-page");
            Check(s.mail.Count == mailCount, "and posts no second call-out");

            // Beat them.
            float rep0 = s.streetRep;
            Blacklist.RecordResult(s, 10, true);
            Check(s.blDefeated.Contains(10), "a win records the rank");
            Check(s.streetRep > rep0, "and pays the scalp bonus", s.streetRep - rep0);

            // Defeats are permanent even when rep falls through the floor.
            s.streetRep = 0f;
            Check(Blacklist.StatusOf(s, entry) == RivalStatus.Beaten,
                  "a beaten rival stays beaten through a rep collapse");
            Check(Blacklist.NextRival(s).rank == 9, "the ladder moves to rank 9");

            // A loss changes nothing but the log.
            var nine = Blacklist.ByRank(9);
            s.streetRacesWon = nine.gateWins; s.streetRep = nine.gateRep;
            Check(Blacklist.StatusOf(s, nine) == RivalStatus.Open, "rank 9 opens");
            Blacklist.RecordResult(s, 9, false);
            Check(!s.blDefeated.Contains(9), "a loss does not take the spot");
            Check(Blacklist.StatusOf(s, nine) == RivalStatus.Open, "and leaves them challengeable");

            // The challenge field is 1v1 in the rival's car.
            RaceHandoff.ClearAll();
            Check(LifeRules.FillRivalField(nine), "the challenge field builds");
            Check(!RaceHandoff.OpponentSpecIds.Contains(";"),
                  "a challenge is 1v1", RaceHandoff.OpponentSpecIds);
            Check(CarCatalog.Get(RaceHandoff.OpponentSpecIds) != null,
                  "against a real catalog car: " + Blacklist.CarName(nine));

            // A challenge must not spend the daily purse race, but must still
            // reset the rep-decay clock.
            var car = s.ActiveCar;
            car.fuel = 100f;
            s.lastRaceDay = 0; s.lastAnyRaceDay = 0;
            RaceHandoff.ResultReady = true;
            RaceHandoff.CarId = car.id;
            RaceHandoff.MetersDriven = 3f * 1168f;
            RaceHandoff.FinishPos = 1;
            RaceHandoff.FieldSize = 2;
            RaceHandoff.RivalRank = 9;
            RaceHandoff.RivalAlias = nine.alias;
            RaceHandoff.PurseWin = Blacklist.Purse(9);
            int money0 = s.money;
            LifeRules.ApplyRaceResult(s);
            Check(s.blDefeated.Contains(9), "winning the challenge takes the spot");
            Check(s.lastRaceDay == 0, "a challenge does not burn the daily race", s.lastRaceDay);
            Check(s.lastAnyRaceDay == s.day, "but does reset the rep-decay clock",
                  s.lastAnyRaceDay + " vs day " + s.day);
            Check(s.money > money0, "and pays its purse", s.money - money0);
            RaceHandoff.ClearAll();
        }

        static float StatOf(OwnedCar car, string stat) => stat switch
        {
            "engine" => car.engine,
            "tires" => car.tires,
            "paint" => car.paint,
            _ => car.carHP,
        };
    }
}
