using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R0c ROOM FOR TWO (Fault_System_Design_2026-11-11 §8 "Room" and "Selling",
    /// §10 R0c; the owner's answer 4: two car spots and MOVE HOUSE).
    ///
    /// ROOM. A career's garage slots are DERIVED, not stored: the house rung's
    /// garage (<see cref="LifeRules.Housing"/>, read from housingType) plus the
    /// one yard spot the home street already draws (the lawn bay beside the
    /// drive - CarWhere's YARD), and never less than the stored garageSlots,
    /// which debug careers raise. So an existing one-car career (v24, stored
    /// garageSlots 1, "house1g") has room for a second car with no save change.
    /// Every purchase gate reads <see cref="GarageSlots"/>.
    ///
    /// MOVE HOUSE. Nothing wrote housingType before. A move costs the new
    /// place's first month as the deposit plus a day's truck, takes a block,
    /// and the rent changes from the next bill. The walk-in still draws the
    /// one house the neighbourhood has (a bigger rung's extra cars stand on
    /// the lawn bays it already lays out).
    ///
    /// TRADE-IN at Crestline. Needs a visit (the dealer page only opens from
    /// the dealership's own door in town) and the 15-minute appraisal of the
    /// car the player came in. The quote is 60% of today's lane-based
    /// <see cref="CarMarket.CarValue"/> - it reads no faults, so it leaks none
    /// and a hidden fault cannot move it - and it holds for that car until the
    /// day ends (kept in memory: quitting the game drops it, and a new visit
    /// re-appraises). Sold outright when it is not the only car; the only car
    /// goes in PART EXCHANGE against a car off the lot, so nobody walks home
    /// carless (a career with no car re-opens the starter-car picker).
    ///
    /// JUNK IT at the yard: scrap by weight plus the catalytic converter; the
    /// yard tows it away and the car leaves the career. Not the only car.
    ///
    /// Switches, each 0 = today: PSX_ROOM_FOR_TWO (slots = the stored value),
    /// PSX_MOVE_HOUSE, PSX_TRADE_IN (the market's QUICK-SELL 50% back, no
    /// trade-in), PSX_JUNK_IT.
    /// </summary>
    public static class Room
    {
        public static bool SlotsOn = Env("PSX_ROOM_FOR_TWO") != "0";
        public static bool MoveOn = Env("PSX_MOVE_HOUSE") != "0";
        public static bool TradeInOn = Env("PSX_TRADE_IN") != "0";
        public static bool JunkOn = Env("PSX_JUNK_IT") != "0";

        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);

        // ================= room =================

        /// <summary>The yard spot beside the drive (GarageWorld's lawn bay 2).</summary>
        public const int YardSpots = 1;

        /// <summary>The garage that comes with a housing rung (1 for an unknown key).</summary>
        public static int RungSlots(string housingType)
        {
            foreach (var h in LifeRules.Housing) if (h.key == housingType) return h.slots;
            return 1;
        }

        public static int RentOf(string housingType)
        {
            foreach (var h in LifeRules.Housing) if (h.key == housingType) return h.rent;
            return 0;
        }

        /// <summary>How many cars this career has room for.</summary>
        public static int GarageSlots(LifeState s)
        {
            if (s == null) return 1;
            if (!SlotsOn) return s.garageSlots;
            return Mathf.Max(s.garageSlots, RungSlots(s.housingType) + YardSpots);
        }

        /// <summary>Room for the rung <paramref name="housingType"/> (same rule).</summary>
        public static int SlotsAt(LifeState s, string housingType) =>
            s == null ? 1 : Mathf.Max(s.garageSlots, RungSlots(housingType) + YardSpots);

        // ================= move house =================

        /// <summary>A day's local moving truck, 1999 (about $20-40 plus miles).</summary>
        public const int TruckRental = 45;

        /// <summary>The deposit (the new place's first month) plus the truck.</summary>
        public static int MoveCost(string housingType) => RentOf(housingType) + TruckRental;

        public static string MoveRefusal(LifeState s, string housingType)
        {
            if (s == null) return "no career";
            if (!MoveOn) return "not offered";
            if (RentOf(housingType) <= 0) return "no such house";
            if (s.housingType == housingType) return "you live here";
            int room = SlotsAt(s, housingType);
            if (s.cars.Count > room) return "no room there for " + s.cars.Count + " cars";
            int cost = MoveCost(housingType);
            if (s.money < cost) return "need " + MenuKit.Money(cost);
            return null;
        }

        /// <summary>Move. Null on success, or the reason it was refused.</summary>
        public static string MoveHouse(LifeState s, string housingType)
        {
            string why = MoveRefusal(s, housingType);
            if (why != null) return why;
            int cost = MoveCost(housingType);
            s.money -= cost;
            s.housingType = housingType;
            s.monthlyHousingCost = RentOf(housingType);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": moved to a " +
                              LifeRules.HousingLabel(housingType).ToLowerInvariant() + " — " +
                              MenuKit.Money(cost) + " deposit and truck");
            // Moving day is a block of the day, like any other errand.
            LifeRules.SpendActivitySlot(s);
            return null;
        }

        // ================= trade-in at Crestline =================

        public const float TradeInShare = 0.60f;

        /// <summary>carId -> (day, quote). In memory on purpose: no save field
        /// (R0c ships no save version).</summary>
        static readonly Dictionary<string, KeyValuePair<int, int>> quotes =
            new Dictionary<string, KeyValuePair<int, int>>();

        /// <summary>What the appraiser would offer for this car now: 60% of the
        /// lane-based value. Reads no fault, hidden or found.</summary>
        public static int AppraisalValue(OwnedCar car) =>
            car == null ? 0 : Mathf.Max(0, Mathf.RoundToInt(CarMarket.CarValue(car) * TradeInShare));

        public static bool Appraised(LifeState s, OwnedCar car) =>
            s != null && car != null && quotes.TryGetValue(car.id, out var q) && q.Key == s.day;

        /// <summary>Today's quote for this car, or 0 when it has not been appraised today.</summary>
        public static int QuoteToday(LifeState s, OwnedCar car) =>
            Appraised(s, car) ? quotes[car.id].Value : 0;

        /// <summary>The 15-minute appraisal, at the dealership. Re-asking the
        /// same day returns the same number.</summary>
        public static int Appraise(LifeState s, OwnedCar car)
        {
            if (s == null || car == null || !TradeInOn) return 0;
            if (Appraised(s, car)) return QuoteToday(s, car);
            int q = AppraisalValue(car);
            quotes[car.id] = new KeyValuePair<int, int>(s.day, q);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + CarWhere.DealerName.ToLowerInvariant() +
                              " offered " + MenuKit.Money(q) + " for the " + car.displayName);
            return q;
        }

        /// <summary>Tests and a new career: forget every quote.</summary>
        public static void ForgetQuotes() => quotes.Clear();

        /// <summary>What the player is handed: the quote less what is still owed on it.</summary>
        public static int TradeInNet(LifeState s, OwnedCar car) =>
            QuoteToday(s, car) - CarMarket.LoanPayoff(s, car != null ? car.id : "");

        public static string TradeInRefusal(LifeState s, OwnedCar car, bool partExchange)
        {
            if (!TradeInOn) return "not offered";
            if (s == null || car == null || !s.cars.Contains(car)) return "no car";
            if (!Appraised(s, car)) return "have it appraised first";
            if (LifeRules.CarInShop(s, car)) return "car has work booked on it";
            if (!partExchange && s.cars.Count <= 1)
                return "it is your only car — trade it against one on the lot";
            int net = TradeInNet(s, car);
            if (!partExchange && net < 0 && s.money < -net)
                return "upside down by " + MenuKit.Money(-net);
            return null;
        }

        /// <summary>Sell it to Crestline at today's quote. Not the only car.</summary>
        public static string TradeIn(LifeState s, OwnedCar car)
        {
            string why = TradeInRefusal(s, car, false);
            if (why != null) return why;
            int net = TradeInNet(s, car);
            s.money += net;
            LeaveCareer(s, car, "traded the " + car.displayName + " in at " +
                                CarWhere.DealerName.ToLowerInvariant() + " — " + MenuKit.Money(net));
            return null;
        }

        /// <summary>The down payment a finance option asks for this viewing,
        /// exactly as <see cref="Viewings.Adopt"/> will take it.</summary>
        public static int DownFor(Viewing v, CarMarket.FinanceOption opt) =>
            v == null ? 0 : opt.isCash ? v.offerPrice : Mathf.Min(opt.downPayment, v.offerPrice);

        public static string PartExchangeRefusal(LifeState s, Viewing v, CarMarket.FinanceOption opt, OwnedCar trade)
        {
            if (v == null || v.car == null) return "no car";
            string why = TradeInRefusal(s, trade, true);
            if (why != null) return why;
            if (s.cars.Count - 1 >= GarageSlots(s)) return "garage full (" + GarageSlots(s) + ")";
            int need = DownFor(v, opt) - TradeInNet(s, trade);
            if (s.money < need) return "need " + MenuKit.Money(need - s.money) + " more";
            return null;
        }

        /// <summary>
        /// Buy a car off the lot with <paramref name="trade"/> in part
        /// exchange: the quote (less its loan) goes toward the down payment,
        /// the old car leaves, the new one is the car the player drives away.
        /// Works on the only car and with a full garage.
        /// </summary>
        public static string BuyWithTradeIn(LifeState s, Viewing v, CarMarket.FinanceOption opt, OwnedCar trade)
        {
            string why = PartExchangeRefusal(s, v, opt, trade);
            if (why != null) return why;
            int net = TradeInNet(s, trade);
            string oldName = trade.displayName;
            s.money += net;
            LeaveCareer(s, trade, null);
            string err = Viewings.Adopt(s, v, opt);
            if (err != null) return err;     // the checks above are Adopt's own
            var bought = s.cars[s.cars.Count - 1];
            s.activeCar = bought.id;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": part-exchanged the " + oldName + " (" +
                              MenuKit.Money(net) + ") for the " + bought.displayName);
            return null;
        }

        // ================= junk it =================

        /// <summary>1999 scrap: about $0.03-0.05 a kilo for a shell, plus the
        /// converter. A Civic fetches about $80, a Viper about $105.</summary>
        public const float ScrapPerKg = 0.04f;
        public const int CatalystDollars = 40;
        /// <summary>US catalytic converters from the 1975 model year.</summary>
        public const int CatalystFromYear = 1975;

        public static int ScrapValue(OwnedCar car)
        {
            if (car == null) return 0;
            var spec = CarCatalog.Get(car.specId);
            int kg = spec != null && spec.kg > 0 ? spec.kg : 1200;
            int year = spec != null ? spec.modelYear : 1990;
            return Mathf.RoundToInt(kg * ScrapPerKg) + (year >= CatalystFromYear ? CatalystDollars : 0);
        }

        public static string JunkRefusal(LifeState s, OwnedCar car)
        {
            if (!JunkOn) return "not offered";
            if (s == null || car == null || !s.cars.Contains(car)) return "no car";
            if (s.cars.Count <= 1) return "it is your only car";
            if (CarWhere.Away(s, car)) return "car is at " + CarWhere.ShopName(CarWhere.PlaceOf(s, car)).ToLowerInvariant();
            if (LifeRules.CarInShop(s, car)) return "car has work booked on it";
            int net = ScrapValue(car) - CarMarket.LoanPayoff(s, car.id);
            if (net < 0 && s.money < -net) return "upside down by " + MenuKit.Money(-net);
            return null;
        }

        /// <summary>The yard tows it away and pays scrap (less any loan).</summary>
        public static string JunkIt(LifeState s, OwnedCar car)
        {
            string why = JunkRefusal(s, car);
            if (why != null) return why;
            int net = ScrapValue(car) - CarMarket.LoanPayoff(s, car.id);
            s.money += net;
            LeaveCareer(s, car, "junked the " + car.displayName + " — " + MenuKit.Money(net) + " scrap");
            return null;
        }

        // ================= a car leaves the career =================

        /// <summary>QuickSell's own cleanup: loans settled, any spare going on
        /// back on the shelf, jobs and ads dropped, the keys moved on.</summary>
        internal static void LeaveCareer(LifeState s, OwnedCar car, string logLine)
        {
            s.carLoans.RemoveAll(l => l.carId == car.id);
            Spares.ReturnJobsToShelf(s, car);
            s.pendingParts.RemoveAll(p => p.carId == car.id);
            s.carAds.RemoveAll(a => a.carId == car.id);
            s.cars.Remove(car);
            quotes.Remove(car.id);
            if (s.activeCar == car.id) s.activeCar = s.cars.Count > 0 ? s.cars[0].id : "";
            if (logLine != null) s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + logLine);
        }
    }
}
