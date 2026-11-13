using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R0c ROOM FOR TWO on the pages (see <see cref="Room"/>): the TRADE-IN at
    /// Crestline's own page (the visit), PART EXCHANGE on a car off its lot,
    /// JUNK IT on the yard's page, MOVE HOUSE under the bills. Each block draws
    /// nothing with its switch at 0. Nothing here names a part or reads a
    /// fault: the quote is the lane-based value.
    /// </summary>
    public partial class LifeHomeScreen
    {
        /// <summary>The one destructive press waiting for its second press
        /// ("junk:&lt;car id&gt;", "move:&lt;house&gt;", "sell:&lt;car id&gt;").</summary>
        string armKey;

        static readonly Vector2 RoomTop = new Vector2(0.5f, 1f);

        void RoomHeading(string text, ref float y)
        {
            MenuKit.Label(body, text, 18, RoomTop, new Vector2(ColL, y), TextAnchor.MiddleLeft,
                MenuKit.Accent, ColW, height: 26f, bold: true).raycastTarget = false;
            y -= 30f;
        }

        void RoomLine(string text, Color color, ref float y)
        {
            MenuKit.Label(body, text, 15, RoomTop, new Vector2(ColL, y), TextAnchor.MiddleLeft,
                color, ColW, height: 24f).raycastTarget = false;
            y -= 28f;
        }

        /// <summary>A button that asks once more before it acts.</summary>
        void ArmedButton(string key, string label, string refusal, System.Action act, float w, ref float y)
        {
            bool armed = armKey == key && refusal == null;
            string text = refusal != null ? label + "  ·  " + refusal : armed ? "SURE?  " + label : label;
            MenuKit.Button(body, text, RoomTop, new Vector2(MenuKit.ColLeft(ColL, w), y), new Vector2(w, 44f),
                refusal != null ? null : (UnityEngine.Events.UnityAction)(() =>
                {
                    if (armKey != key) { armKey = key; Rebuild(); return; }
                    armKey = null;
                    act();
                }), 15, refusal != null ? MenuKit.BtnBgDisabled : armed ? new Color(0.45f, 0.18f, 0.12f, 1f) : (Color?)null);
            y -= 52f;
        }

        // ---- Crestline: the appraisal and the trade-in (the dealer page is the visit) ----
        void TradeInBlock(ref float y)
        {
            if (!Room.TradeInOn) return;
            var car = S.ActiveCar;
            if (car == null || !CarWhere.Available(S, car)) return;
            RoomHeading("YOUR " + Clip(car.displayName.ToUpperInvariant(), 30) + " — TRADE-IN", ref y);
            float w = Mathf.Min(660f, ColW);
            if (!Room.Appraised(S, car))
            {
                RoomLine("The appraiser walks round it and takes it round the block. A quarter of an hour.",
                         MenuKit.Dim, ref y);
                var c = car;
                MenuKit.Button(body, "HAVE IT APPRAISED  (15 MIN)", RoomTop,
                    new Vector2(MenuKit.ColLeft(ColL, w), y), new Vector2(w, 44f), () =>
                    {
                        int q = Room.Appraise(S, c);
                        LifeSimManager.Save(); Rebuild();
                        Toast("Crestline offers " + MenuKit.Money(q));
                    }, 15);
                y -= 52f;
            }
            else
            {
                int q = Room.QuoteToday(S, car);
                int owed = CarMarket.LoanPayoff(S, car.id);
                RoomLine("CRESTLINE OFFERS " + MenuKit.Money(q) + (owed > 0 ? "  ·  YOU OWE " + MenuKit.Money(owed) : "") +
                         "  ·  GOOD UNTIL CLOSING", MenuKit.Good, ref y);
                if (S.cars.Count > 1)
                {
                    var c = car;
                    ArmedButton("sell:" + car.id, "SELL IT TO CRESTLINE — " + MenuKit.Money(Room.TradeInNet(S, car)),
                        Room.TradeInRefusal(S, car, false), () =>
                        {
                            string name = c.displayName;
                            string err = Room.TradeIn(S, c);
                            // The car on the forecourt is Crestline's now: the
                            // way out of this page is home, not back to it.
                            if (err == null) Town.TownReturn.Clear();
                            LifeSimManager.Save(); Rebuild();
                            Toast(err ?? ("sold the " + name + " to Crestline"));
                        }, w, ref y);
                }
                else RoomLine("It is your only car: pick one off the lot and it goes in part exchange.",
                              MenuKit.Dim, ref y);
            }
            y -= 8f;
        }

        // ---- a car off Crestline's lot, with the appraised car in part exchange ----
        void PartExchangeRows(Viewing v, float bx, float bw, ref float y)
        {
            if (!Room.TradeInOn || v == null || v.car == null || v.source != "lot") return;
            var trade = S.ActiveCar;
            if (trade == null || !Room.Appraised(S, trade)) return;
            int net = Room.TradeInNet(S, trade);
            MenuKit.Label(body, "OR WITH YOUR " + Clip(trade.displayName.ToUpperInvariant(), 24) +
                    " IN PART EXCHANGE (" + MenuKit.Money(net) + ")", 15, RoomTop, new Vector2(bx - bw * 0.5f, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, bw, height: 24f).raycastTarget = false;
            y -= 28f;
            foreach (var opt in Viewings.FinanceFor(S, v))
            {
                var captured = opt;
                string why = Room.PartExchangeRefusal(S, v, opt, trade);
                int pay = Mathf.Max(0, Room.DownFor(v, opt) - net);
                string label = opt.isCash
                    ? "BUY IT — " + MenuKit.Money(pay) + " CASH + YOUR CAR"
                    : opt.label + " — " + MenuKit.Money(pay) + " down + your car, " +
                      MenuKit.Money(opt.monthlyPayment) + "/mo";
                MenuKit.Button(body, label, RoomTop, new Vector2(bx, y), new Vector2(bw, 46f),
                    why == null ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        string err = Room.BuyWithTradeIn(S, v, captured, trade);
                        LifeSimManager.Save();
                        if (err == null) { S.activeViewing = ""; tab = "garage"; }
                        Rebuild();
                        Toast(err ?? ("bought the " + v.car.displayName + " — yours went in part exchange"));
                    }) : null, 16, why == null ? (Color?)null : MenuKit.BtnBgDisabled);
                y -= 52f;
            }
        }

        // ---- the yard: junk it ----
        void JunkBlock(ref float y)
        {
            if (!Room.JunkOn || S.cars.Count == 0) return;
            RoomHeading("WE BUY CARS FOR SCRAP — WE TOW IT AWAY", ref y);
            float w = Mathf.Min(760f, ColW);
            foreach (var car in S.cars)
            {
                var c = car;
                ArmedButton("junk:" + car.id, "JUNK THE " + Clip(car.displayName.ToUpperInvariant(), 26) + " — " +
                            MenuKit.Money(Room.ScrapValue(car)), Room.JunkRefusal(S, car), () =>
                    {
                        string name = c.displayName;
                        string err = Room.JunkIt(S, c);
                        LifeSimManager.Save(); Rebuild();
                        Toast(err ?? ("the yard took the " + name));
                    }, w, ref y);
            }
            y -= 8f;
        }

        // ---- the bills: move house ----
        void MoveHouseBlock(ref float y)
        {
            if (!Room.MoveOn) return;
            y -= 24f;
            RoomHeading("MOVE HOUSE", ref y);
            RoomLine("Room for " + Room.GarageSlots(S) + " cars here: " + Room.RungSlots(S.housingType) +
                     " in the garage, " + Room.YardSpots + " in the yard.", MenuKit.Dim, ref y);
            float w = Mathf.Min(900f, ColW);
            foreach (var h in LifeRules.Housing)
            {
                if (h.key == S.housingType) continue;
                string key = h.key;
                string why = Room.MoveRefusal(S, key);
                // Short enough for a 4:3 screen with the refusal on the end
                // (the refusal names the money when that is the reason).
                ArmedButton("move:" + key, h.label + "  ·  " + MenuKit.Money(h.rent) + "/MO  ·  ROOM FOR " +
                            Room.SlotsAt(S, key) + (why == null ? "  ·  MOVING " + MenuKit.Money(Room.MoveCost(key)) : ""),
                    why, () =>
                    {
                        string err = Room.MoveHouse(S, key);
                        LifeSimManager.Save(); Rebuild();
                        Toast(err ?? "moved — the rent is " + MenuKit.Money(Room.RentOf(key)) + " from the 1st");
                    }, w, ref y);
            }
        }
    }
}
