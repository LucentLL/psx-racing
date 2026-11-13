using System.Linq;
using System.Text;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R0c ROOM FOR TWO and the owner's answers 1 and 8 (Fault_System_Design_
    /// 2026-11-11 §8, §10 R0c, OWNER'S ANSWERS): a one-car v24 career buys a
    /// second car with no save change; MOVE HOUSE; the trade-in needs the
    /// visit's appraisal, is 60% of the lane value, holds all day and is not
    /// moved by a hidden fault, and the only car goes in part exchange; JUNK IT
    /// pays scrap and the car leaves; RESTART and the reset are gone from a
    /// non-debug career (kept in debug careers and outside careers); QUIT from
    /// a race and from free roam keeps a planted crash. RunRoom runs these alone
    /// and writes PSXRacing_selftest_room.txt.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        public static void RunRoom()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            try { RunRoomR0cTests(); }
            finally { Edition.Simulate(editionWas); }
            Line(failures == 0 ? "ROOM SELF-TEST OK" : "ROOM SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_room.txt"), log.ToString());
        }

        static void RunRoomR0cTests()
        {
            var randomWas = Random.state;
            bool slots = Room.SlotsOn, move = Room.MoveOn, trade = Room.TradeInOn, junk = Room.JunkOn;
            bool noRestart = CareerDrive.NoRestartOn, noReset = CareerDrive.NoResetOn, keeps = CareerDrive.QuitKeepsOn;
            try
            {
                Room.SlotsOn = Room.MoveOn = Room.TradeInOn = Room.JunkOn = true;
                CareerDrive.NoRestartOn = CareerDrive.NoResetOn = CareerDrive.QuitKeepsOn = true;
                Guard(nameof(TestRoomForTwo), TestRoomForTwo);
                Guard(nameof(TestRoomMoveHouse), TestRoomMoveHouse);
                Guard(nameof(TestRoomTradeIn), TestRoomTradeIn);
                Guard(nameof(TestRoomJunk), TestRoomJunk);
                Guard(nameof(TestCareerDriveRules), TestCareerDriveRules);
                // R1a: this pins the QUIT through R0a's ledger (PSX_FAULT_LOCATED=0);
                // the located path's QUIT is TestDamageBank's.
                bool locatedWas = DamageModel.On;
                DamageModel.On = false;
                try { Guard(nameof(TestQuitKeepsTheLeg), TestQuitKeepsTheLeg); }
                finally { DamageModel.On = locatedWas; }
            }
            finally
            {
                Room.SlotsOn = slots; Room.MoveOn = move; Room.TradeInOn = trade; Room.JunkOn = junk;
                CareerDrive.NoRestartOn = noRestart; CareerDrive.NoResetOn = noReset; CareerDrive.QuitKeepsOn = keeps;
                Room.ForgetQuotes();
                RaceHandoff.ClearAll();
                RoomUseState(null);
                Random.state = randomWas;
            }
        }

        static CarSpec RoomSpec(int skip = 0) =>
            CarCatalog.All.Where(c => c != null && !c.IsRaceCar && !c.IsRallyCar && c.price > 0 && c.kg > 0 &&
                                      c.modelYear >= 1980).Skip(skip).FirstOrDefault();

        static LifeState RoomCareer(string name, int cars, int money)
        {
            var s = LifeRules.SeedNewGame(name, 25, LifeRules.DefaultJobIndex);
            for (int i = 0; i < cars; i++)
            {
                var spec = RoomSpec(i * 7);
                var c = CarMarket.MakeOwnedCar(s, spec, 70, 60000f + i * 1000f, spec.price / 3);
                c.id = spec.id + "#r0c" + name + i;   // unique, whatever MakeOwnedCar's dice said
                c.faults.Clear();
                if (i == 0) s.activeCar = c.id;
            }
            s.money = money;
            return s;
        }

        /// <summary>The career the runtime reads (LifeSimManager.State), on a
        /// scratch key; null puts the player's own key back (reads nothing).</summary>
        static void RoomUseState(LifeState s)
        {
            var field = typeof(LifeSimManager).GetField("state",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (s == null) { LifeSimManager.UseCareerSave(); return; }
            LifeSimManager.UseScratchSave("r0c");
            field.SetValue(null, s);
        }

        static CarMarket.FinanceOption RoomCash(LifeState s, Viewing v) =>
            Viewings.FinanceFor(s, v).FirstOrDefault(o => o.isCash);

        static void TestRoomForTwo()
        {
            Line("R0c room - a one-car v24 career has room for a second car, with no save change:");
            var seeded = RoomCareer("ROOM", 1, 30000);
            var s = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(seeded));
            // R0c adds no save step of its own (the room is derived): a seeded career
            // round-trips at whatever version is current (v25 since R0b merged).
            Check(s.saveVersion == new LifeState().saveVersion,
                  "no save step from R0c: the career round-trips at the current version", s.saveVersion);
            Check(s.garageSlots == 1 && s.housingType == "house1g" && s.cars.Count == 1,
                  "what a v24 one-car career stores: 1 garage slot, the 1-car house, one car",
                  s.garageSlots + " / " + s.housingType + " / " + s.cars.Count);
            Check(Room.GarageSlots(s) == 2 && LifeRules.GarageSlots(s) == 2,
                  "derived: the 1-car garage plus the yard spot = room for 2", Room.GarageSlots(s));

            var spec = RoomSpec(3);
            CarListing Listing(int price) => new CarListing
                { specId = spec.id, displayName = spec.name, price = price, cond = 60, odoMiles = 90000f, expiresDay = s.day + 5 };
            var l2 = Listing(1500);
            s.newspaper.Add(l2);
            string err = CarMarket.Buy(s, l2, CarMarket.FinanceOptions(s, l2).First(o => o.isCash));
            Check(err == null && s.cars.Count == 2, "it BUYS a second car from the paper", err ?? s.cars.Count + " cars");
            var l3 = Listing(1400);
            s.newspaper.Add(l3);
            err = CarMarket.Buy(s, l3, CarMarket.FinanceOptions(s, l3).First(o => o.isCash));
            Check(err == "garage full (2)" && s.cars.Count == 2, "a third is refused: garage full (2)", err);
            CarMarket.RefreshLot(s);
            var v = s.dealerLot.Count > 0 ? Viewings.Open(s, s.dealerLot[0], "lot") : null;
            err = v != null ? Viewings.Adopt(s, v, RoomCash(s, v)) : "no lot";
            Check(err == "garage full (2)", "the lot's door reads the same room", err);

            var d = LifeRules.SeedNewGame(LifeRules.DebugName, 25, LifeRules.DefaultJobIndex);
            Check(Room.GarageSlots(d) == LifeRules.DebugGarageSlots, "a debug career keeps its stored 6", Room.GarageSlots(d));

            Room.SlotsOn = false;
            var off = RoomCareer("ROOMOFF", 1, 30000);
            var lo = Listing(1500);
            off.newspaper.Add(lo);
            err = CarMarket.Buy(off, lo, CarMarket.FinanceOptions(off, lo).First(o => o.isCash));
            Check(Room.GarageSlots(off) == 1 && err == "garage full (1)",
                  "PSX_ROOM_FOR_TWO=0: today's one slot, and the second car is refused", err);
            Room.SlotsOn = true;
        }

        static void TestRoomMoveHouse()
        {
            Line("R0c room - MOVE HOUSE to the bigger rungs:");
            var s = RoomCareer("MOVE", 1, 2000);
            int day0 = s.day, slot0 = s.slotIndex;
            string err = Room.MoveHouse(s, "house2g");
            Check(err == null && s.housingType == "house2g" && s.monthlyHousingCost == 575 &&
                  s.money == 2000 - (575 + Room.TruckRental),
                  "moves to the 2-car house: rent $575 from the next bill, the deposit and the truck paid",
                  err ?? s.housingType + " $" + s.monthlyHousingCost + " left $" + s.money);
            Check(Room.GarageSlots(s) == 3, "room there: the 2-car garage plus the yard = 3", Room.GarageSlots(s));
            Check(s.day != day0 || s.slotIndex != slot0, "moving day takes a block");
            var spec = RoomSpec(2);
            for (int i = 0; i < 2; i++)
            {
                var c = CarMarket.MakeOwnedCar(s, spec, 70, 50000f, 1000);
                c.id = spec.id + "#r0cmove" + i;
            }
            Check(Room.MoveRefusal(s, "house1g") != null && Room.MoveRefusal(s, "house1g").StartsWith("no room"),
                  "3 cars cannot move back to room for 2", Room.MoveRefusal(s, "house1g"));
            s.money = 10;
            Check(Room.MoveRefusal(s, "house3g") == "need " + MenuKit.Money(750 + Room.TruckRental),
                  "the 3-car house needs its deposit and truck", Room.MoveRefusal(s, "house3g"));
            Room.MoveOn = false;
            Check(Room.MoveRefusal(s, "house3g") == "not offered", "PSX_MOVE_HOUSE=0: no move");
            Room.MoveOn = true;
        }

        static void TestRoomTradeIn()
        {
            Line("R0c trade-in - by visit, 60% of the lane value, fixed for the day, blind to hidden faults:");
            Room.ForgetQuotes();
            var s = RoomCareer("TRADE", 1, 3000);
            var car = s.ActiveCar;
            int value = CarMarket.CarValue(car);
            Check(Room.TradeIn(s, car) == "have it appraised first" && Room.QuoteToday(s, car) == 0,
                  "no visit, no quote: the trade-in needs the appraisal");
            int before = Room.AppraisalValue(car);
            car.faults.Add(new CarFault { id = "vacuum_leak", label = "Vacuum leak", stat = "engine", cost = 240, hidden = true });
            Check(Room.AppraisalValue(car) == before,
                  "a planted HIDDEN fault does not move the appraisal", before + " -> " + Room.AppraisalValue(car));
            int q = Room.Appraise(s, car);
            Check(q == Mathf.RoundToInt(value * 0.6f) && q > 0,
                  "the quote is 60% of today's lane-based value", MenuKit.Money(q) + " of " + MenuKit.Money(value));
            car.faults.Add(new CarFault { id = "bumper_crack", label = "Cracked bumper", stat = "hp", cost = 300, hidden = true });
            car.carHP = Mathf.Max(0f, car.carHP - 40f);
            Check(Room.QuoteToday(s, car) == q && Room.Appraise(s, car) == q,
                  "the quote holds all day: a hidden fault and a dent after the visit do not move it", Room.QuoteToday(s, car));
            Check(Room.TradeIn(s, car) != null && s.cars.Contains(car),
                  "the only car is never sold outright (nobody walks home carless)", Room.TradeIn(s, car));

            CarMarket.RefreshLot(s);
            var lot = s.dealerLot.FirstOrDefault(l => !l.isNew) ?? s.dealerLot.FirstOrDefault();
            var v = lot != null ? Viewings.Open(s, lot, "lot") : null;
            Check(v != null, "a car on Crestline's lot to part-exchange against");
            if (v != null)
            {
                var cash = RoomCash(s, v);
                int net = Room.TradeInNet(s, car);
                s.money = Mathf.Max(0, v.offerPrice - net) + 100;
                int money0 = s.money;
                string err = Room.BuyWithTradeIn(s, v, cash, car);
                Check(err == null && s.cars.Count == 1 && !s.cars.Contains(car) && s.activeCar == s.cars[0].id &&
                      s.money == money0 + net - v.offerPrice,
                      "the ONLY car goes in part exchange: it leaves, the lot car is the one driven away, the quote paid toward it",
                      err ?? s.cars.Count + " car(s), $" + s.money + " (want $" + (money0 + net - v.offerPrice) + ")");
            }

            var next = s.ActiveCar;
            Room.Appraise(s, next);
            s.day++;
            Check(!Room.Appraised(s, next) && Room.TradeIn(s, next) == "have it appraised first",
                  "the next day the quote is gone: a new visit");

            var two = RoomCareer("TRADE2", 2, 500);
            var c2 = two.cars[1];
            two.carLoans.Add(new CarLoan { carId = c2.id, principal = 300, monthlyPayment = 100, monthsRemaining = 3, apr = 0.1f });
            int q2 = Room.Appraise(two, c2);
            int m0 = two.money;
            string sold = Room.TradeIn(two, c2);
            Check(sold == null && !two.cars.Contains(c2) && two.money == m0 + q2 - 300 &&
                  !two.carLoans.Exists(l => l.carId == c2.id),
                  "a second car sells outright to Crestline: the quote less the loan, the loan settled",
                  sold ?? "$" + two.money);

            Room.TradeInOn = false;
            var c1 = two.cars[0];
            Check(Room.Appraise(two, c1) == 0 && Room.TradeInRefusal(two, c1, false) == "not offered",
                  "PSX_TRADE_IN=0: no appraisal and no trade-in (the market's QUICK-SELL 50% is back)");
            Room.TradeInOn = true;
        }

        static void TestRoomJunk()
        {
            Line("R0c junk it - the yard pays scrap and the car leaves the career:");
            var one = RoomCareer("JUNK1", 1, 100);
            Check(Room.JunkIt(one, one.cars[0]) == "it is your only car" && one.cars.Count == 1, "not the only car");
            var s = RoomCareer("JUNK", 2, 100);
            var c1 = s.cars[0];
            var c2 = s.cars[1];
            var spec = CarCatalog.Get(c2.specId);
            int scrap = Room.ScrapValue(c2);
            Check(scrap == Mathf.RoundToInt(spec.kg * Room.ScrapPerKg) + (spec.modelYear >= 1975 ? Room.CatalystDollars : 0) &&
                  scrap >= 40 && scrap <= 200,
                  "scrap: $0.04 a kilo plus $40 for the converter", spec.kg + " kg -> " + MenuKit.Money(scrap));
            string err = Room.JunkIt(s, c2);
            Check(err == null && !s.cars.Contains(c2) && s.cars.Count == 1 && s.money == 100 + scrap && s.activeCar == c1.id,
                  "junking pays the scrap and the car is gone", err ?? s.cars.Count + " car(s), $" + s.money);
            var s3 = RoomCareer("JUNK3", 2, 0);
            s3.carLoans.Add(new CarLoan { carId = s3.cars[1].id, principal = 2000, monthlyPayment = 200, monthsRemaining = 10, apr = 0.1f });
            Check(Room.JunkRefusal(s3, s3.cars[1]) != null && Room.JunkRefusal(s3, s3.cars[1]).StartsWith("upside down"),
                  "a car owing more than scrap needs the difference in hand", Room.JunkRefusal(s3, s3.cars[1]));
            Room.JunkOn = false;
            Check(Room.JunkRefusal(s3, s3.cars[0]) == "not offered", "PSX_JUNK_IT=0: no junking");
            Room.JunkOn = true;
        }

        static void TestCareerDriveRules()
        {
            Line("R0c answers 1 and 8 - RESTART and the reset only outside a non-debug career:");
            var s = RoomCareer("RULES", 1, 1000);
            try
            {
                RoomUseState(s);
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                Check(CareerDrive.InCareer && !CareerDrive.RestartOffered && !CareerDrive.ResetAllowed && CareerDrive.Strict,
                      "a career drive: no RESTART, no reset, the tow instead");
                s.debugMode = true;
                Check(CareerDrive.RestartOffered && CareerDrive.ResetAllowed && !CareerDrive.Strict,
                      "a DEBUG career keeps RESTART and the reset");
                s.debugMode = false;
                RaceHandoff.FromLifeSim = false;
                Check(CareerDrive.RestartOffered && CareerDrive.ResetAllowed && !CareerDrive.Strict,
                      "quick race / a standalone race (not from a career) keeps both");
                RaceHandoff.FromLifeSim = true;
                CareerDrive.NoRestartOn = false; CareerDrive.NoResetOn = false;
                Check(CareerDrive.RestartOffered && CareerDrive.ResetAllowed && !CareerDrive.Strict,
                      "PSX_NO_RESTART=0 / PSX_NO_RESET=0: today's menu and resets");
                CareerDrive.NoRestartOn = true; CareerDrive.NoResetOn = true;
            }
            finally { RaceHandoff.ClearAll(); RoomUseState(null); }

            Check(CareerDrive.TowFee(4f) == 55 && CareerDrive.TowFee(10f) == 67 && CareerDrive.TowFee(100f) == 293 &&
                  CareerDrive.TowFee(270f) > 700,
                  "the tow: $55 hook-up with 5 miles, then $2.50 a mile (town $55, Charlotte $67, the mountains $293, the coast $717)",
                  CareerDrive.TowFee(4f) + " / " + CareerDrive.TowFee(10f) + " / " + CareerDrive.TowFee(100f) + " / " + CareerDrive.TowFee(270f));

            // Every way the PLAYER's car is put back on the road reads the rule,
            // and every RESTART row does.
            string root = System.IO.Path.Combine(Application.dataPath, "PSXRacing", "Scripts");
            var respawners = new System.Collections.Generic.List<string>();
            var ungated = new System.Collections.Generic.List<string>();
            var restarts = new System.Collections.Generic.List<string>();
            foreach (var path in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string file = System.IO.Path.GetFileName(path);
                if (file == "DriveSession.cs" || file == "CareerDrive.cs") continue;
                var lines = System.IO.File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i].Trim();
                    if (l.StartsWith("//")) continue;
                    if (l.Contains("DriveSession.Respawn("))
                    {
                        respawners.Add(file + ":" + (i + 1));
                        bool gated = false;
                        for (int j = Mathf.Max(0, i - 25); j <= i; j++)
                            if (lines[j].Contains("CareerDrive.ResetAllowed") || lines[j].Contains("CareerDrive.Strict") ||
                                lines[j].Contains("!strict")) { gated = true; break; }
                        if (!gated) ungated.Add(file + ":" + (i + 1));
                    }
                    if (l.Contains("\"RESTART") && !(i > 0 && lines[i - 1].Contains("CareerDrive.RestartOffered")) &&
                        !l.Contains("CareerDrive.RestartOffered"))
                        restarts.Add(file + ":" + (i + 1));
                }
            }
            Check(respawners.Count >= 6 && ungated.Count == 0,
                  "every player-car respawn reads the career rule (" + string.Join(", ", respawners) + ")",
                  ungated.Count > 0 ? "ungated: " + string.Join(", ", ungated) : null);
            Check(restarts.Count == 0, "every RESTART row is offered only where CareerDrive.RestartOffered says",
                  restarts.Count > 0 ? string.Join(", ", restarts) : null);
        }

        static void TestQuitKeepsTheLeg()
        {
            Line("R0c answer 1 - QUIT from a race and from free roam keeps a planted crash:");
            var spec = CrashSpec("jpn", 1990);
            if (spec == null) { Check(false, "a Japanese road car to crash"); return; }
            bool crashWas = CrashStopGap.On;
            CrashStopGap.On = true;
            var s = LifeRules.SeedNewGame("QUIT", 25, LifeRules.DefaultJobIndex);
            var owned = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
            owned.id = spec.id + "#r0cquit";
            s.activeCar = owned.id;
            owned.faults.Clear();
            owned.carHP = owned.paint = owned.radiator = 80f;
            owned.fuel = 60f;
            var roamer = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
            roamer.id = spec.id + "#r0croam";
            roamer.faults.Clear();
            roamer.carHP = roamer.paint = roamer.radiator = 80f;
            var car = TuneBenchCar(spec);
            var temp = car.gameObject.AddComponent<EngineTemp>();
            temp.car = car;
            var resp = car.gameObject.AddComponent<CollisionResponder>();
            var city = new GameObject("R0cQuitCity") { hideFlags = HideFlags.HideAndDontSave };
            var setInstance = typeof(PSXRacing.City.CityMode).GetProperty("Instance").GetSetMethod(true);
            try
            {
                RoomUseState(s);
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.CarId = owned.id;
                RaceHandoff.CarSpecId = spec.id;
                RaceHandoff.TrackIndex = TrackCatalog.CheckVenue;
                temp.radiatorCond = 90f;
                Check(CrashStopGap.Arm(car) && resp.Ledger != null, "a career race arms the player's crash ledger");
                resp.Ledger.DrawOverride = () => CrashDraws(bumper: 0.1f, radiator: 0.1f, align: 0.1f);
                resp.RecordHit(12f, CrashStopGap.ZoneFront, -1, new Vector3(-0.5f, 0f, 2.1f), "wall", 10f);

                int day0 = s.day, slot0 = s.slotIndex, money0 = s.money, races0 = s.streetRacesTotal;
                float odo0 = owned.odoMiles, hp0 = owned.carHP, fuel0 = owned.fuel;
                bool banked = CareerDrive.BankLeg(car, 5000f, 0f, "GILLESPIE GAP");
                var bump = owned.faults.Find(f => f.id == "bumper_crack");
                Check(banked && bump != null && bump.hidden && owned.carHP < hp0,
                      "a race QUIT banks the planted crash from the seat: the hidden bumper row, the body lost",
                      string.Join(",", owned.faults.Select(f => f.id)) + "  body " + hp0 + " -> " + owned.carHP.ToString("0.0"));
                Check(owned.odoMiles > odo0 + 3f && owned.fuel < fuel0,
                      "and the leg's 5 km of odometer, wear and fuel",
                      (owned.odoMiles - odo0).ToString("0.00") + " mi, fuel " + fuel0 + " -> " + owned.fuel.ToString("0.0"));
                Check(s.money <= money0 && s.streetRacesTotal == races0,
                      "no purse and no race in the totals for a quit", "money " + money0 + " -> " + s.money);
                Check((s.day != day0 || s.slotIndex != slot0) && s.lastRaceDay == day0,
                      "but the quit still uses the block and the day's race (owner 11-12, PSX_QUIT_COSTS)",
                      "day " + day0 + "/" + slot0 + " -> " + s.day + "/" + s.slotIndex + ", lastRaceDay " + s.lastRaceDay);
                Check(!RaceHandoff.ResultReady && s.calendarLog.Skip(System.Math.Max(0, s.calendarLog.Count - 4)).Any(l => l.Contains("quit")),
                      "nothing left for the front end to bank twice, and the diary says it was a quit",
                      s.calendarLog.Count > 0 ? s.calendarLog[s.calendarLog.Count - 1] : "");

                CareerDrive.QuitKeepsOn = false;
                Check(!CareerDrive.BankQuitLeg(car), "PSX_QUIT_KEEPS=0: a quit banks nothing (today's)");
                CareerDrive.QuitKeepsOn = true;

                // FREE ROAM: exactly what PauseMenu.ExitToMenu does, then the
                // front end's apply-back.
                resp.StartOverForNewCar();
                resp.Ledger = null;
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.CarId = roamer.id;
                RaceHandoff.CarSpecId = spec.id;
                Check(CrashStopGap.Arm(car) && resp.Ledger != null, "a free-roam drive arms a fresh ledger");
                resp.Ledger.DrawOverride = () => CrashDraws(bumper: 0.1f, radiator: 0.9f, align: 0.9f);
                resp.RecordHit(12f, CrashStopGap.ZoneFront, 1, new Vector3(0.5f, 0f, 2.1f), "post", 5f);
                var cm = city.AddComponent<PSXRacing.City.CityMode>();
                cm.player = car;
                setInstance.Invoke(null, new object[] { cm });
                Check(!CareerDrive.BankQuitLeg(car), "a city session banks itself: the race quit stands aside");
                PSXRacing.City.CityMode.Instance?.StampExitResult();
                CrashStopGap.StampCityExit();
                LifeRules.ApplyRaceResult(s);
                Check(roamer.faults.Exists(f => f.id == "bumper_crack" && f.hidden) && roamer.carHP < 80f,
                      "END DRIVE from free roam keeps the planted crash on the car",
                      string.Join(",", roamer.faults.Select(f => f.id)) + "  body " + roamer.carHP.ToString("0.0"));
            }
            catch (System.Exception e)
            {
                Check(false, "the quit checks ran", e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                CrashStopGap.On = crashWas;
                try { setInstance.Invoke(null, new object[] { null }); } catch { }
                Object.DestroyImmediate(city);
                TuneDrop(car);
                RaceHandoff.ClearAll();
                RoomUseState(null);
            }
        }
    }
}
