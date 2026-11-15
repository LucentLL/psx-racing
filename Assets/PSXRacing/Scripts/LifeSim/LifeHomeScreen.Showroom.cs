using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The career's car pages as a GT-style showroom (Showroom, CarViewer's
    /// studio frame; owner, 2026-11-15). Each hook falls back to the page as
    /// it was when PSX_SHOWROOM=0 or when there is no shell to show:
    ///   * the car page: the car big at the top (LEFT / RIGHT step through the
    ///     garage), everything the page had under it;
    ///   * MY CARS: one car at a time over the list (OPEN THIS CAR, GET IN),
    ///     then the toolbox, fuel truck and shelves, then the list as before;
    ///   * the dealer: the lot one car at a time, LOOK IT OVER opens the same
    ///     viewing (test drive, offers, buy) a row did; the viewing and the
    ///     classifieds' BUY page show their car on the stage;
    ///   * NEW GAME's WHAT ARE YOU DRIVING?: the three lanes one at a time,
    ///     DRIVE THIS ONE or the FULL SHEET (the close-up as before).
    /// </summary>
    public partial class LifeHomeScreen
    {
        /// <summary>MY CARS: which car the stage is showing (empty = the one
        /// the player is in).</summary>
        string garageShowId = "";
        /// <summary>The dealer's stage: which car of the lot.</summary>
        int dealerShow;
        /// <summary>NEW GAME: which lane is on the stage.</summary>
        int pickShow;

        float HeroStageH => Showroom.StageHeight(BodyH - MenuKit.ScrollPad);

        /// <summary>The stage with a car the player owns on it: its livery,
        /// its built power and weight, what it is worth.</summary>
        bool OwnedHero(OwnedCar car, ref float y, string tag, Color tagColor, string note, Color noteColor,
                       List<Showroom.Action> actions, int index, int count, System.Action<int> browse)
        {
            var spec = car != null ? CarCatalog.Get(car.specId) : null;
            if (spec == null) return false;
            Viewer.ShowOwned(car, spec);
            var def = Viewer.Shown;
            if (def == null) return false;
            var card = Showroom.From(spec, car.displayName, def, Paint.SkinFor(car, spec, def));
            card.hp = CarSpec.ToHp(Upgrades.EffectiveHp(car, spec));
            card.kg = Upgrades.EffectiveKg(car, spec);
            card.tag = tag;
            card.tagColor = tagColor;
            card.priceLabel = "VALUE";
            card.price = MenuKit.Money(CarMarket.CarValue(car));
            card.note = note;
            card.noteColor = noteColor;
            if (actions != null) card.actions = actions;
            float h = Showroom.Build(body, Viewer, card, ColL, ColW, y, HeroStageH, index, count, browse);
            y -= h + 4f;
            return true;
        }

        /// <summary>The car page's hero: the car, its name and figures; the
        /// page's own buttons follow under it.</summary>
        bool CarMenuHero(OwnedCar car, ref float y)
        {
            int ci = Mathf.Max(0, S.cars.IndexOf(car));
            return OwnedHero(car, ref y, null, MenuKit.Accent, null, MenuKit.Dim, null, ci, S.cars.Count, d =>
            {
                var nx = S.cars[(ci + d + S.cars.Count) % S.cars.Count];
                garageCarId = nx.id;
                if (viewer != null) viewer.ResetStudioAngle();
                Rebuild();
            });
        }

        /// <summary>MY CARS' top: one car at a time, where it is and how it
        /// is, and the two things to do with it from here.</summary>
        bool GarageHero(ref float y)
        {
            var car = string.IsNullOrEmpty(garageShowId) ? null : S.FindCar(garageShowId);
            if (car == null) car = S.ActiveCar ?? S.cars[0];
            int ci = Mathf.Max(0, S.cars.IndexOf(car));
            bool away = CarWhere.Away(S, car);
            var place = CarWhere.PlaceOf(S, car);
            bool driving = car.id == S.activeCar;
            string tag = CarWhere.Label(place) + (away
                ? "  ·  UNAVAILABLE  ·  " + CarWhere.ReadyLabel(S, car)
                : driving ? "  ·  YOU ARE DRIVING THIS ONE"
                : car.noWheels ? "  ·  NO WHEELS ON IT" : "  ·  READY TO DRIVE");
            Color tagColor = away || car.noWheels ? MenuKit.Bad : driving ? MenuKit.Good : PlaceColor(place);
            float worst = Mathf.Min(Mathf.Min(car.engine, car.tires), Mathf.Min(car.carHP, car.paint));
            int known = KnownFaults(car);
            string note = car.odoMiles.ToString("N0") + " mi  ·  FUEL " + Mathf.RoundToInt(car.fuel) + "%  ·  " +
                          (S.debugMode ? "CONDITION " + Mathf.RoundToInt(worst) + "%" : "CONDITION " + LifeRules.ConditionLabel(worst)) +
                          "  ·  " + (known > 0 ? known + " known fault" + (known == 1 ? "" : "s") : "no known faults");
            Color noteColor = known > 0 || worst <= 30f ? MenuKit.Bad : worst > 60f ? MenuKit.Good : MenuKit.Accent;
            var captured = car;
            var acts = new List<Showroom.Action>
            {
                new Showroom.Action("OPEN THIS CAR", () => { garageCarId = captured.id; tab = "carmenu"; Rebuild(); },
                    new Color(0.62f, 0.48f, 0.12f, 1f)),
                away ? new Showroom.Action("NOT HERE", null)
                : driving ? new Showroom.Action("IN THIS CAR", () => Toast("already driving " + Clip(captured.displayName, 30)),
                    new Color(0.42f, 0.34f, 0.10f, 1f))
                : new Showroom.Action("GET IN", () =>
                    {
                        S.activeCar = captured.id;
                        LifeSimManager.Save(); Rebuild();
                        Toast("now driving " + Clip(captured.displayName, 30));
                    }, new Color(0.20f, 0.30f, 0.24f, 1f)),
            };
            if (!OwnedHero(car, ref y, tag, tagColor, note, noteColor, acts, ci, S.cars.Count, d =>
                {
                    garageShowId = S.cars[(ci + d + S.cars.Count) % S.cars.Count].id;
                    if (viewer != null) viewer.ResetStudioAngle();
                    Rebuild();
                }))
                return false;

            // Not about the car on the stage (owner, 2026-11-06): the toolbox,
            // the truck for the car you are in, and the shelves.
            var active = S.ActiveCar ?? S.cars[0];
            float dw = Mathf.Min(320f, (ColW - 10f) * 0.5f);
            MenuKit.Button(body, "TOOLBOX", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, dw), y), new Vector2(dw, 46f),
                () => { garageCarId = S.activeCar; tab = "toolbox"; Rebuild(); }, 17);
            int truck = LifeRules.CallOutRefuelCost(active);
            bool canFill = active.fuel < 99.5f && S.money >= truck;
            MenuKit.Button(body, active.fuel >= 99.5f ? "TANK FULL" : "CALL FUEL TRUCK - " + MenuKit.Money(truck),
                new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL + dw + 10f, dw), y), new Vector2(dw, 46f),
                canFill ? (UnityEngine.Events.UnityAction)(() =>
                {
                    S.money -= truck;
                    active.fuel = 100f;
                    S.calendarLog.Add(LifeRules.LogDate(S.day) + ": fuel truck call-out - " + MenuKit.Money(truck));
                    LifeSimManager.Save(); Rebuild();
                    Toast("TANK FILLED - " + MenuKit.Money(truck));
                }) : null, 17, canFill ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 52f;
            StorageDoors(ColL, ColW, ref y);
            y -= 8f;
            return true;
        }

        /// <summary>The dealer's lot on the stage, new cars first.</summary>
        bool DealerHero(ref float y)
        {
            var lot = new List<CarListing>();
            foreach (var l in S.dealerLot) if (l != null && l.isNew) lot.Add(l);
            foreach (var l in S.dealerLot) if (l != null && !l.isNew) lot.Add(l);
            if (lot.Count == 0) return false;
            dealerShow = (dealerShow % lot.Count + lot.Count) % lot.Count;
            var listing = lot[dealerShow];
            var spec = CarCatalog.Get(listing.specId);
            if (spec == null) return false;
            Viewer.Show(spec);
            var def = Viewer.Shown;
            if (def == null) return false;
            var card = Showroom.From(spec, listing.displayName, def, Paint.FactorySkin(spec, def));
            var seen = Viewings.Find(S, listing);
            card.tag = listing.isNew ? "NEW  ·  IN THE SHOWROOM" : "USED  ·  ON THE LOT";
            card.tagColor = listing.isNew ? MenuKit.Good : MenuKit.Accent;
            card.priceLabel = "PRICE";
            card.price = MenuKit.Money(seen != null ? seen.offerPrice : listing.price);
            card.note = (listing.isNew ? "delivery miles only"
                                       : listing.odoMiles.ToString("N0") + " mi  ·  cond " + listing.cond) +
                        (seen != null ? "  ·  you have looked at this one" : "");
            var captured = listing;
            card.actions.Add(new Showroom.Action("LOOK IT OVER", () =>
            {
                var v = Viewings.Open(S, captured, "lot");
                S.activeViewing = v.key;
                LifeSimManager.Save();
                tab = "viewing"; Rebuild();
            }, new Color(0.20f, 0.30f, 0.24f, 1f)));
            int n = lot.Count;
            float h = Showroom.Build(body, Viewer, card, ColL, ColW, y, HeroStageH, dealerShow, n, d =>
            {
                dealerShow = (dealerShow + d + n) % n;
                if (viewer != null) viewer.ResetStudioAngle();
                Rebuild();
            });
            y -= h + 8f;
            return true;
        }

        /// <summary>A car for sale on the stage (the viewing, the classifieds'
        /// BUY page): the picture and the figures; the page's own words follow.</summary>
        bool ViewHero(CarSpec spec, ref float y)
        {
            if (spec == null) return false;
            Viewer.Show(spec);
            var def = Viewer.Shown;
            if (def == null) return false;
            var card = Showroom.From(spec, spec.name, def, Paint.FactorySkin(spec, def));
            float h = Showroom.Build(body, Viewer, card, ColL, ColW, y, Mathf.Min(HeroStageH, 320f), 0, 1, null);
            y -= h + 4f;
            return true;
        }

        /// <summary>
        /// NEW GAME, WHAT ARE YOU DRIVING?: the three lanes one at a time on
        /// the stage. DRIVE THIS ONE commits exactly as the close-up's button
        /// does; FULL SHEET opens that close-up (its BACK comes back here).
        /// </summary>
        bool BuildCarPickShowroom()
        {
            if (pickLanes == null) pickLanes = CarMarket.RollStartingLanes(S.basePay, S.creditScore);
            var lanes = pickLanes;
            if (lanes.Count == 0) return false;   // the classic page's fallback
            pickShow = (pickShow % lanes.Count + lanes.Count) % lanes.Count;
            var lane = lanes[pickShow];
            var spec = lane.spec;
            if (spec == null) return false;
            Viewer.Show(spec);
            var def = Viewer.Shown;
            if (def == null) return false;

            float W = Mathf.Min(1100f, MenuKit.HalfWidth * 2f - 32f);
            float H = Mathf.Min(MenuKit.DesignHeight - 20f, 700f);
            var root = MenuKit.Rect(canvas.transform, "CarPick",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H), MenuKit.PanelBg);
            MenuKit.Label(root, "WHAT ARE YOU DRIVING?", 26, new Vector2(0.5f, 1f), new Vector2(0f, -12f),
                TextAnchor.MiddleCenter, MenuKit.Accent, W - 60f, height: 34f, bold: true);
            MenuKit.Label(root, "Your savings are yours either way — the payments are what follow you.",
                15, new Vector2(0.5f, 1f), new Vector2(0f, -44f), TextAnchor.MiddleCenter, MenuKit.Dim, W - 60f, height: 26f);

            int age = CarMarket.GameStartYear - spec.modelYear;
            var card = Showroom.From(spec, spec.name, def, Paint.FactorySkin(spec, def));
            card.tag = lane.label;
            card.priceLabel = lane.financed ? "PAYMENT" : "PRICE";
            card.price = lane.financed ? MenuKit.Money(lane.monthly) + "/mo x " + lane.months : "PAID OFF";
            card.priceColor = lane.financed ? MenuKit.Bad : MenuKit.Good;
            card.note = age + " yr old  ·  " + lane.odoMiles.ToString("N0") + " mi  ·  cond " + lane.cond +
                        "  ·  " + DriveSide.Label(spec.id) + "\n" + lane.blurb;
            card.noteColor = lane.financed ? MenuKit.Bad : MenuKit.Good;
            var captured = lane;
            card.actions.Add(new Showroom.Action("DRIVE THIS ONE", () => CommitLane(captured), new Color(0.62f, 0.48f, 0.12f, 1f)));
            card.actions.Add(new Showroom.Action("FULL SHEET", () =>
            {
                if (viewer != null) viewer.SetVisible(false);
                Destroy(root.gameObject);
                BuildCarConfirm(captured);
            }));
            // Room for the stage: what the panel has after the titles, the
            // name block and the figures under it.
            float top = -76f;
            float stageH = Mathf.Clamp(H + top - 108f - 14f - 160f - 10f, 150f, 360f);
            int n = lanes.Count;
            Showroom.Build(root, Viewer, card, -W * 0.5f + 30f, W - 60f, top, stageH, pickShow, n, d =>
            {
                pickShow = (pickShow + d + n) % n;
                pickFocus = d > 0 ? "Btn_>" : "Btn_<";
                if (viewer != null) viewer.ResetStudioAngle();
                Destroy(root.gameObject);
                BuildCarPick();
            });

            WirePanelNavigation(root);
            // A browse rebuilt the panel: the cursor stays on the arrow it used.
            if (!string.IsNullOrEmpty(pickFocus))
            {
                foreach (var s in root.GetComponentsInChildren<Selectable>(true))
                    if (s.gameObject.name == pickFocus) { MenuNav.Select(s); break; }
                pickFocus = null;
            }

            if (PreviewConfirmLane >= 0 && PreviewConfirmLane < lanes.Count)
            {
                var l = lanes[PreviewConfirmLane];
                DestroyImmediate(root.gameObject);
                BuildCarConfirm(l);
            }
            return true;
        }

        string pickFocus;

        /// <summary>DRIVE THIS ONE, as the close-up commits it.</summary>
        void CommitLane(CarMarket.StartingLane lane)
        {
            if (viewer != null) viewer.SetVisible(false);
            pickLanes = null;
            CarMarket.ApplyStartingLane(S, lane);
            LifeSimManager.Save();
            if (wizFirstDrive)
            {
                wizFirstDrive = false;
                pendingFirstDrive = true;
                PendingTab = "charlotte";
            }
            SceneManager.LoadScene(0);   // reload home, now in play mode
        }
    }
}
