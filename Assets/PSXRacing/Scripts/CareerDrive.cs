using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE OWNER'S ANSWERS 1 AND 8 (Fault_System_Design_2026-11-11, 2026-11-12),
    /// for a drive that banks into a career:
    ///
    ///   1. "Restart should be a debug option only. Quit means you stop
    ///      participating, but you are not immune to what happened." RESTART
    ///      is offered only in debug careers (and outside careers: quick race,
    ///      the CITY edition, a standalone editor race). QUIT banks the leg's
    ///      crash outcome, wear, fuel and heat from the seat before the result
    ///      is thrown away - the way LifeRules.BankSeizureNow banks a seizure -
    ///      but no purse and no rep (a quit is not a finish). Owner 2026-11-12,
    ///      asked whether a quit should still use the time and the day's race:
    ///      "Yes" - the block is spent and the day's purse race is used up, the
    ///      way a finish would use them (PSX_QUIT_COSTS).
    ///   8. "No respawning or resetting cars ... If stuck, player can call tow
    ///      truck." In a non-debug career the player's car is never put back on
    ///      the road: no R / pad X / wheel reset, no RESET CAR row, no
    ///      auto-reset when stuck, rolled, in the water or fallen off the road.
    ///      It stays where it is and the pause menu's leave row becomes CALL
    ///      TOW TRUCK: a 1999 Charlotte wrecker's fee by the distance home,
    ///      the drive ends, the car goes home and the drive's damage is banked.
    ///      The one recovery kept is the backstop for a car that fell out of
    ///      the world (under the map's floor), which is a hole in the world,
    ///      not something the driver did. AI cars keep their own recovery
    ///      (AIDriver), and quick race, the CITY edition and debug careers keep
    ///      the old behaviour.
    ///
    /// Switches, each 0 = today: PSX_NO_RESTART, PSX_NO_RESET (also removes the
    /// tow), PSX_QUIT_KEEPS, PSX_QUIT_COSTS (0 = a quit spends no block and
    /// leaves the day's race open, as R0c shipped it in PC32).
    /// </summary>
    public static class CareerDrive
    {
        public static bool NoRestartOn = Env("PSX_NO_RESTART") != "0";
        public static bool NoResetOn = Env("PSX_NO_RESET") != "0";
        public static bool QuitKeepsOn = Env("PSX_QUIT_KEEPS") != "0";
        public static bool QuitCostsOn = Env("PSX_QUIT_COSTS") != "0";

        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);

        /// <summary>This drive banks into a career that is not a debug career
        /// (a test drive of a seller's car included).</summary>
        public static bool InCareer
        {
            get
            {
                if (!RaceHandoff.FromLifeSim || !Edition.HasCareer) return false;
                var s = LifeSim.LifeSimManager.State;
                return s != null && !s.debugMode;
            }
        }

        /// <summary>Whether the pause menu offers RESTART.</summary>
        public static bool RestartOffered => !(NoRestartOn && InCareer);

        /// <summary>Whether anything may put the PLAYER's car back on the road
        /// (R, pad X / Select, the wheel's reset, RESET CAR, the stuck watchdog).</summary>
        public static bool ResetAllowed => !(NoResetOn && InCareer);

        /// <summary>A career drive under answer 8: the tow instead of the reset.</summary>
        public static bool Strict => NoResetOn && InCareer;

        // ================= the tow truck =================

        /// <summary>A light-duty wrecker's hook-up in Charlotte, 1999, with the
        /// first five miles in it, then so much a mile.</summary>
        public const int TowHookUp = 55;
        public const float TowIncludedMiles = 5f;
        public const float TowPerMile = 2.5f;

        public static int TowFee(float miles) =>
            TowHookUp + Mathf.RoundToInt(Mathf.Max(0f, miles - TowIncludedMiles) * TowPerMile);

        /// <summary>
        /// Road miles to home from where this drive is: the town is local,
        /// Charlotte is across town, the coast and the mountains are a long
        /// haul (Charlotte to Emerald Isle about 270 miles, to the Blue Ridge
        /// stages about 100).
        /// </summary>
        public static float TowMiles()
        {
            var city = City.CityMode.Instance;
            if (city != null && !city.IsRace) return city.world == null ? 4f : 10f;
            var t = TrackCatalog.At(RaceHandoff.TrackIndex);
            if (t == null) return 10f;
            if (t.city || t.stageInCity) return 10f;
            string data = t.stageData ?? "";
            if (data.StartsWith("bogue")) return 270f;
            if (t.stage) return 100f;
            return 25f;
        }

        public static int TowFeeHere() => TowFee(TowMiles());

        /// <summary>
        /// The car cannot get itself home: the engine has let go, or the stuck
        /// watchdog has it on its roof, wedged, in the water or off the road.
        /// </summary>
        public static bool NeedsTow(CarController car)
        {
            if (!Strict || car == null) return false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null && temp.Seized) return true;
            var stuck = car.GetComponent<StuckRecovery>();
            return stuck != null && stuck.Stranded;
        }

        /// <summary>
        /// Take the fee for the tow, from the seat (the fuel truck's precedent).
        /// A driver who cannot cover it pays what is in the wallet; the truck
        /// still comes. Returns the fee, or 0 outside a career.
        /// </summary>
        public static int ChargeTow()
        {
            if (!InCareer) return 0;
            var s = LifeSim.LifeSimManager.State;
            if (s == null) return 0;
            int fee = TowFeeHere();
            int paid = Mathf.Clamp(s.money, 0, fee);
            s.money -= paid;
            s.calendarLog.Add(LifeSim.LifeRules.LogDate(s.day) + ": tow truck home — " +
                              LifeSim.MenuKit.Money(fee) +
                              (paid < fee ? " (paid " + LifeSim.MenuKit.Money(paid) + ", all you had)" : ""));
            LifeSim.LifeSimManager.Save();
            return fee;
        }

        // ================= QUIT keeps what happened =================

        /// <summary>
        /// The distance this car has covered in the race so far, the way the
        /// finish line reckons a whole race: metres along a point-to-point
        /// road, laps plus the station on a loop (nothing before the first
        /// crossing of the line from the grid).
        /// </summary>
        public static float RaceMetersSoFar(RaceManager rm, CarController car)
        {
            if (rm == null || car == null || rm.path == null) return 0f;
            var p = rm.GetProgress(car);
            if (p == null) return 0f;
            if (rm.path.HasEnds) return Mathf.Max(0f, p.progress);
            if (!p.crossedStartOnce) return 0f;
            return Mathf.Max(0f, (p.lap - 1) * rm.path.TotalLength + p.nearestIdx * rm.path.spacing);
        }

        /// <summary>
        /// QUIT (or the tow) from a RACE: bank what the leg did to the car
        /// before the pause menu throws the result away. The city's exits bank
        /// their own session (CityMode.StampExitResult), so this is only for a
        /// race with no city session under it; a finished race keeps its stamped
        /// result instead. Same guards as BankSeizureNow: a career's own car,
        /// never a seller's on a test drive. True when something was banked.
        /// </summary>
        public static bool BankQuitLeg(CarController player)
        {
            if (!QuitKeepsOn || player == null) return false;
            if (!RaceHandoff.FromLifeSim || RaceHandoff.TestDrive || !Edition.HasCareer) return false;
            if (City.CityMode.Instance != null) return false;
            var rm = RaceManager.Instance;
            if (rm == null) return false;
            if (rm.State == RaceManager.RaceState.Finished && RaceHandoff.ResultReady) return false;
            return BankLeg(player, RaceMetersSoFar(rm, player), rm.PlayerDriftSeconds,
                           TrackCatalog.At(RaceHandoff.TrackIndex) != null
                               ? TrackCatalog.At(RaceHandoff.TrackIndex).name : "the race");
        }

        /// <summary>
        /// Stamp the leg the way the finish line stamps one (distance, drift,
        /// the located crash and what was rolled, the tank, the gauge) and bank
        /// its car half through the ordinary apply-back - odometer, fuel, wear,
        /// heat, the crash outcome, the bump stops, the distance wear rolls -
        /// as a leg that pays nothing and spends no block. Public for the
        /// self-test, which drives it with a bench car.
        /// </summary>
        public static bool BankLeg(CarController player, float meters, float driftSeconds, string venue)
        {
            var s = LifeSim.LifeSimManager.State;
            if (s == null || player == null) return false;
            var car = s.FindCar(RaceHandoff.CarId) ?? s.ActiveCar;
            if (car == null) return false;

            bool roamWas = RaceHandoff.FreeRoam;
            string placeWas = RaceHandoff.FreeRoamPlace;
            // What kind of race this was, read before the apply-back clears it:
            // a meet race's block is paid by the drive home, a delivery is the job's,
            // and a rival, meet or club race never burns the day's race (LifeRules).
            bool meetRace = RaceHandoff.MeetRace, delivery = RaceHandoff.Delivery, clubRace = RaceHandoff.ClubRace;
            bool rivalRace = !string.IsNullOrEmpty(RaceHandoff.RivalAlias);
            RaceHandoff.ResultReady = true;
            RaceHandoff.FreeRoam = true;       // no purse, no rep, no race counted
            RaceHandoff.CommuteLeg = true;     // no block
            RaceHandoff.FreeRoamPlace = venue;
            RaceHandoff.MetersDriven = meters;
            RaceHandoff.DriftSeconds = driftSeconds;
            CrashStopGap.StampHandoff(player.GetComponent<CollisionResponder>());
            var tank = player.GetComponent<FuelTank>();
            if (tank != null)
            {
                RaceHandoff.EndFuelPct = tank.percent;
                RaceHandoff.FuelReported = true;
            }
            EngineTemp.StampResult(player);
            RaceHandoff.FuelSpent = 0;         // the pumps took their money at the pump
            LifeSim.LifeRules.ApplyRaceResult(s);   // ends with ClearResult
            RaceHandoff.FreeRoam = roamWas;
            RaceHandoff.FreeRoamPlace = placeWas;
            // The apply-back wrote "race on the road - x km in ..."; say what it was.
            if (s.calendarLog.Count > 0)
                s.calendarLog[s.calendarLog.Count - 1] = LifeSim.LifeRules.LogDate(s.day) + ": quit " +
                    (string.IsNullOrEmpty(venue) ? "the race" : venue) + " after " +
                    (meters / 1000f).ToString("0.0") + " km — the car keeps what happened";
            RaceHandoff.CommuteLeg = false;
            // Owner 2026-11-12: a quit still uses the time and the day's race (no
            // purse, no rep). The day is stamped first: the block may roll it over.
            if (QuitCostsOn)
            {
                if (!rivalRace && !meetRace && !clubRace) s.lastRaceDay = s.day;
                if (!delivery && !meetRace) LifeSim.LifeRules.SpendActivitySlot(s, LifeSim.LifeRules.ActRace);
            }
            LifeSim.LifeSimManager.Save();
            return true;
        }
    }
}
