using UnityEngine;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.Town
{
    /// <summary>
    /// Puts the cars in the town: the dealership's stock on its bays, and the
    /// dead ones in the yard — and, since the delivery loop became a real
    /// journey, everything the town has to DO at runtime: spawn the player at
    /// the shop kerb mid-errand, put the order on the passenger seat, offer
    /// the walk-up doors, and keep the HUD's errand signpost fresh.
    ///
    /// Same split as every other place in this game that has cars standing in
    /// it — the LOT is baked, because it is the same lot for everybody, and
    /// what is parked on it comes out of the save. It matters more here than it
    /// looks: the cars on the dealership's bays ARE
    /// <see cref="LifeState.dealerLot"/>, so walking the lot and reading the
    /// page are two views of one list, and a car you bought this morning is
    /// gone from the forecourt this afternoon.
    ///
    /// The yard's wrecks are NOT save state and deliberately so. Nothing in
    /// the salvage model is a whole car — the yard sells PARTS
    /// (<see cref="Junkyard"/>, three shelves on three clocks) — so what is
    /// standing in the dirt is scenery, seeded off the day so it turns over
    /// with the shelves and never off Random, which would reshuffle every time
    /// the player drove past. What the scenery now agrees with is the STOCK:
    /// wheels on the shelves are wheels off these cars, so cars stand stripped
    /// on cinder blocks in proportion to what the yard is selling.
    /// </summary>
    public class TownWorld : MonoBehaviour
    {
        [Header("Wired by the scene builder")]
        public Transform[] dealerSpots = new Transform[0];
        public Transform[] yardSpots = new Transform[0];
        /// <summary>The player's car, for the mid-errand teleport and the cue
        /// arrow. CityMode holds it too, but Awake/Start order between the two
        /// is nobody's promise.</summary>
        public CarController player;
        /// <summary>Where the car stands when the player walks OUT of the shop
        /// carrying an order — the kerb by the pizzeria, facing the street.</summary>
        public Transform pizzaKerb;
        /// <summary>
        /// Everywhere a walker can be offered a shift at Tony's: the frontage
        /// the car parks at, the pack's actual doorway, and a step inside it.
        ///
        /// A LIST rather than one anchor, because one was demonstrably not
        /// enough. The old hook hung off the centre of the model's bounding
        /// box — 21 m of frontage, so eight metres from anything — and the
        /// shop's only door is round the east end while the apron is to the
        /// north. You could stop at the shop, get out, walk round it, walk in,
        /// and never be offered a thing: "I drove to work but was unable to
        /// find a pizza inside to deliver."
        /// </summary>
        public Transform[] pizzaHooks = new Transform[0];
        public Transform dealerDoor;
        public Transform yardGate;
        public Transform homeDoor;
        public Transform mechanicDoor;
        public Transform paintDoor;
        /// <summary>PSX-lit grey for the cinder blocks under stripped wrecks —
        /// materials are bake-time things and this class runs at runtime.</summary>
        public Material blockMaterial;

        /// <summary>
        /// The errand signpost: one line the HUD draws in the slot Charlotte
        /// uses for its food cue. Null when there is no errand — which is most
        /// sessions — so the slot stays empty rather than chatty.
        /// </summary>
        public static string Cue { get; private set; }

        LifeState S => LifeSimManager.State;

        bool built;
        float cueNext;

        void Awake() { Cue = null; }

        /// <summary>
        /// THE ARRIVAL COMES FIRST, and that ordering is the fix for "leave
        /// Pizzeria with pizza, warp back home in driveway."
        ///
        /// PreviewBuild fills the dealership and the yard out of the save —
        /// sixteen car shells, a runtime model load each. It used to run at the
        /// top of this method, ahead of the two things that decide WHERE THE
        /// PLAYER IS. Anything that threw in there (a model the library cannot
        /// resolve, an empty catalog on a cold boot) aborted Start before the
        /// car was moved and before the pizza was put on the seat — leaving the
        /// player on the scene's baked spawn, which is their own driveway,
        /// holding nothing. A crash in the scenery is not allowed to relocate
        /// the player.
        /// </summary>
        void Start()
        {
            // ---- where the car is ----
            // Two ways to arrive somewhere other than the baked spawn, and they
            // are mutually exclusive: walking out of the shop with an order,
            // and coming back out of a shop PAGE onto the forecourt you drove
            // onto. Both are one-shot flags cleared here whether or not they
            // fire, because a stale one would teleport the next session.
            if (PizzaRun.SpawnAtShop && player != null && pizzaKerb != null)
                player.ResetTo(pizzaKerb.position + Vector3.up * 0.3f, pizzaKerb.rotation);
            PizzaRun.SpawnAtShop = false;

            if (TownReturn.SpawnAtVenue && player != null)
            {
                TownReturn.Spot(out Vector3 back, out Quaternion facing);
                player.ResetTo(back + Vector3.up * 0.3f, facing);
            }
            TownReturn.SpawnAtVenue = false;

            // The order rides the seat for real. Same rig the race scenes
            // spawn, so the drive across town is played by the same rules that
            // grade the run — a box thrown into the footwell on Main Street
            // arrives thrown.
            if (PizzaRun.Carrying && player != null && PizzaRun.Toppings != null)
            {
                var cargo = PizzaCargo.Spawn(player, PizzaRun.Toppings);
                if (cargo != null) PizzaCam.Spawn(cargo);
            }

            BuildFootDoors();

            // ---- and only then the scenery ----
            PreviewBuild();
        }

        void OnDestroy() { Cue = null; }

        void Update()
        {
            if (Time.unscaledTime < cueNext) return;
            cueNext = Time.unscaledTime + 0.4f;
            Cue = BuildCue();

            // The shop door's offer depends on whether an order is in the car,
            // and that can change mid-session (handing one back at the
            // counter). A label written once at load would tell the player the
            // order is in the car while they stand there holding nothing.
            if (lastDoorCarrying != PizzaRun.Carrying) RefreshPizzaDoor();

            // THE BOXES LAND WHEN THE DRIVER DOES. The order is now taken
            // INSIDE the shop, on foot, so there is no scene load to spawn the
            // cargo on any more — and putting it on the passenger seat while
            // the player is still standing at the counter would be three boxes
            // arriving in an empty car. Gated on being back in the car rather
            // than on the collect, so the walk out is a walk out.
            if (PizzaRun.Carrying && !ForecourtMode.OnFoot && player != null &&
                PizzaRun.Toppings != null && PizzaCargo.Instance == null)
            {
                var cargo = PizzaCargo.Spawn(player, PizzaRun.Toppings);
                if (cargo != null) PizzaCam.Spawn(cargo);
            }
        }

        /// <summary>
        /// Where the errand wants you, as an eight-point arrow relative to the
        /// car's own heading — the same instrument as Charlotte's food cue,
        /// and for the same reason: a driver can act on "over your left
        /// shoulder" and cannot act on a compass.
        /// </summary>
        string BuildCue()
        {
            Transform anchor = null;
            string label = null;
            // What the cue says once the arrow stops being useful. Null means
            // keep the same words.
            string near = null;

            if (PizzaRun.Carrying)
            {
                // OUT OF TOWN, not back to the junction. The run starts where
                // the road runs out — see TownEdge — and the arrow points at
                // whichever end is nearer, because the shop is in the middle of
                // the street and both ends are out.
                anchor = NearestEdge();
                string where = " — DRIVE OUT OF TOWN";
                label = "DELIVERY" + where;
                if (PizzaCargo.Instance != null && PizzaCargo.Instance.BoxCount > 0)
                {
                    PizzaRun.CarryCondition = Mathf.Min(PizzaRun.CarryCondition,
                                                        PizzaCargo.Instance.Condition);
                    if (PizzaRun.CarryCondition < LifeRules.PizzaPerfectCondition)
                        label = "DELIVERY (" +
                                LifeRules.PizzaConditionLabel(PizzaRun.CarryCondition) +
                                ")" + where;
                }
            }
            else if (PizzaRun.DriveToShop)
            {
                anchor = FindVenue(TownVenue.Kind.Pizzeria);
                // The shift is taken at the counter, on foot, so the arrow has
                // to say that the last twenty metres are walked. Told at the
                // door rather than from across town, where "walk in" is not yet
                // an instruction anybody can follow.
                label = "GO TO WORK — TONY'S";
                near = "TONY'S — PARK UP AND WALK IN";
            }

            if (anchor == null || label == null || player == null) return null;

            Vector3 to = anchor.position - player.transform.position;
            to.y = 0f;
            if (to.magnitude < 18f) return near ?? label;   // you are basically there
            float rel = Vector3.SignedAngle(
                new Vector3(player.transform.forward.x, 0f, player.transform.forward.z),
                to, Vector3.up);
            int oct = Mathf.RoundToInt(Mathf.Repeat(rel, 360f) / 45f) % 8;
            string range = to.magnitude >= 1000f
                ? (to.magnitude / 1000f).ToString("0.0") + " km"
                : Mathf.RoundToInt(to.magnitude / 10f) * 10 + " m";
            return CueArrows[oct] + "  " + label + "  " + range;
        }

        static readonly string[] CueArrows =
            { "^", "/^", ">", "\v", "v", "v/", "<", "^\\" };


        /// <summary>Whichever end of the main street is nearer. Both ends
        /// launch a delivery, so pointing at the far one would send a driver
        /// the length of the town for no reason.</summary>
        Transform NearestEdge()
        {
            Transform best = null;
            float bestSq = float.MaxValue;
            Vector3 from = player != null ? player.transform.position : Vector3.zero;
            foreach (var e in FindObjectsByType<TownEdge>(FindObjectsSortMode.None))
            {
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = e.transform; }
            }
            return best;
        }
        Transform FindVenue(TownVenue.Kind kind)
        {
            foreach (var v in FindObjectsByType<TownVenue>(FindObjectsSortMode.None))
                if (v.kind == kind) return v.transform;
            return null;
        }

        // ------------------------------------------------------------------
        //  walk-up doors
        // ------------------------------------------------------------------
        /// <summary>
        /// The doors a player ON FOOT can use. The venues answer to a stopped
        /// CAR; these answer to somebody who pressed E and walked over — which
        /// is the fix for "I can't get out of the car at the Pizzeria": now
        /// you can, and the door does what the drive-up prompt did.
        /// </summary>
        void BuildFootDoors()
        {
            // The ranges are wide on purpose. The pizzeria is 21 m of frontage
            // with its door round the side and a hollow room behind it; a
            // 3.6 m hook on the middle of the bounding box was reachable from
            // almost nowhere a player actually stands.
            if (pizzaHooks != null)
                foreach (var at in pizzaHooks)
                    if (at != null) pizzaDoorTargets.Add(MakeDoor(at, at.name + "Target", 6.5f));
            RefreshPizzaDoor();

            if (mechanicDoor != null)
            {
                var t = MakeDoor(mechanicDoor, "MechanicDoorTarget", 5f);
                t.title = "DELMAR AUTO";
                t.detail = "Servicing, repairs, and somebody who will tell you what is wrong.";
                t.action = "BOOK IT IN";
                t.onUse = () => TownExit.GoToShop(player, "service", t.title);
            }

            if (paintDoor != null)
            {
                var t = MakeDoor(paintDoor, "PaintDoorTarget", 5f);
                t.title = "COLOURWORKS — PAINT + BODY";
                t.detail = "Respray, panel work, and a book of colours.";
                t.action = "TALK PAINT";
                t.onUse = () => TownExit.GoToShop(player, "paint", t.title);
            }

            if (dealerDoor != null)
            {
                var t = MakeDoor(dealerDoor, "DealerDoorTarget", 4.2f);
                t.title = "CRESTLINE MOTORS";
                t.detail = "New and used. The stock is standing right here.";
                t.action = "TALK TO SALES";
                t.onUse = () => TownExit.GoToShop(player, "dealer", t.title);
            }

            if (yardGate != null)
            {
                // THE GATE IS A SIGN, NOT A SHOP. It used to be "WALK THE
                // SHELVES", which threw the player back to the front end and
                // opened the classifieds' yard advert — reported as: "I don't
                // like that when I drive to the Junkyard it gives me an option
                // to Walk the Shelves which shows me the News tab and shows me
                // all items available. The point of a Junkyard is for the
                // customers to inspect cars and pull parts they want."
                //
                // Exactly so. The parts are ON THE CARS now (WreckScreen), and
                // the racked shelves stay where a racked shelf belongs: in the
                // advert, read from home. Standing at the gate should tell you
                // where you are and get out of the way.
                var t = MakeDoor(yardGate, "YardGateTarget", 4.2f);
                t.title = Junkyard.YardName;
                t.detail = "Pull your own. Tools for hire at the hut, " +
                           MenuKit.Money(Junkyard.ToolRentalFee) + " unless you brought your own.";
                t.action = "";
                t.onUse = null;
            }

            if (homeDoor != null)
            {
                var t = MakeDoor(homeDoor, "HomeDoorTarget", 3.4f);
                t.title = "HOME";
                t.detail = "Park it up, put the kettle on.";
                t.action = "GO IN — CALL IT A DRIVE";
                t.onUse = () => TownExit.GoHome(player, "garage");
            }
        }

        readonly System.Collections.Generic.List<FootTarget> pizzaDoorTargets =
            new System.Collections.Generic.List<FootTarget>();
        bool lastDoorCarrying;

        /// <summary>What the shop offers a walk-up, from the state as it
        /// stands: hand nothing to a player mid-run, a shift when the clock is
        /// punching, the counter otherwise. Written onto EVERY hook the shop
        /// has — the frontage, the doorway and the counter behind it — because
        /// all three are one shop, and a player who walked past one should not
        /// find the next one saying something different.</summary>
        void RefreshPizzaDoor()
        {
            if (pizzaDoorTargets.Count == 0) return;
            lastDoorCarrying = PizzaRun.Carrying;
            bool canClockOn = S != null && !string.IsNullOrEmpty(S.playerJob) &&
                              LifeRules.ShopOpen(S);
            foreach (var t in pizzaDoorTargets)
            {
                if (t == null) continue;
                t.title = "TONY'S — SLICE HOUSE";
                t.action2 = "";
                t.onUse2 = null;
                if (PizzaRun.Carrying)
                {
                    t.detail = "Boxes are yours. Out to the car and out of town.";
                    t.action = "";
                    t.onUse = null;
                }
                else if (canClockOn)
                {
                    t.detail = "The counter is up. Take the run and drive it.";
                    t.action = "CLOCK ON — TAKE A RUN";
                    t.onUse = CollectOrder;
                    t.action2 = "BUY AT THE COUNTER";
                    t.onUse2 = OpenPizzaCounter;
                }
                else
                {
                    t.detail = LifeRules.ShiftHoursShort;
                    t.action = "BUY AT THE COUNTER";
                    t.onUse = OpenPizzaCounter;
                }
            }
        }

        /// <summary>
        /// TAKE THE RUN, STANDING WHERE YOU ARE.
        ///
        /// This used to be <c>TownExit.ClockOn</c>: a scene load to the front
        /// end, a second scene load into Pizzeria.unity, a walk to a counter in
        /// a shop whose street was a different city, and a third scene load
        /// back out. Reported twice, the second time plainly: "you replaced the
        /// fake car with the player's car, but it still warps the player
        /// instead of just walking in and out. And when I leave the Pizzeria it
        /// warps me back home."
        ///
        /// Right on both counts, and the second one was never a bug in the
        /// return trip — it was the round trip existing at all. The town's
        /// pizzeria is a modelled shop with a counter, booths and a door that
        /// opens; there was never a reason for the shift to happen somewhere
        /// else. So the whole of PizzaShift.Collect + PizzaShift.Drive's
        /// bookkeeping happens here, in the room the player is standing in, and
        /// the next loading screen they see is the race.
        ///
        /// The ticket is rolled ONCE and banked in PizzaRun immediately: the
        /// player is told the address and the money before they set off, which
        /// is the only reason to take the job seriously rather than treat it as
        /// a lap.
        /// </summary>
        void CollectOrder()
        {
            var s = S;
            var screen = FindFirstObjectByType<FootScreen>();
            if (s == null) return;

            var car = s.ActiveCar;
            if (car == null) { screen?.Toast("NO CAR TO DELIVER IN"); return; }
            if (car.fuel <= 5f) { screen?.Toast("TANK IS DRY — FILL UP FIRST"); return; }

            var toppings = LifeRules.RollOrderToppings(LifeRules.MaxOrderBoxes);
            int pay = LifeRules.RollDeliveryPay(s) * toppings.Length;
            int trackIndex = LifeRules.DeliveryTrackIndex(s);
            float par = LifeRules.DeliveryParSeconds(trackIndex);
            // The hour the run leaves the counter, read BEFORE the slot spend
            // rolls the clock: a night pickup must not arrive in tomorrow's
            // morning light.
            int tod = TimeOfDay.ForSlot(s.slotIndex, s.day);

            // The shift costs the evening whether or not the box arrives, and
            // clocking on must precede the spend — spending the last slot of
            // the day rolls it, and the rollover decides whether the player
            // skived by reading the very latch this sets.
            LifeRules.ClockOnShift(s);
            LifeRules.SpendActivitySlot(s);
            PizzaRun.StartRun(toppings, pay, trackIndex, par, tod);
            // Already standing at the shop. SpawnAtShop is for a scene load
            // that is no longer happening, and leaving it set would teleport
            // the car onto the kerb the next time the town loaded.
            PizzaRun.SpawnAtShop = false;
            LifeSimManager.Save();

            string venue = trackIndex >= 0 && trackIndex < TrackCatalog.All.Length
                         ? TrackCatalog.All[trackIndex].name : "the drop";
            screen?.Toast("ORDER UP — " + (toppings.Length == 1 ? "ONE BOX" : toppings.Length + " BOXES") +
                          " TO " + venue.ToUpperInvariant() + ", $" + pay +
                          ". BEAT " + LifeRules.DeliveryClock(par) +
                          " FOR MORE. OUT TO THE CAR, THEN OUT OF TOWN.");
            RefreshPizzaDoor();
            screen?.Invalidate();
        }

        void OpenPizzaCounter()
        {
            var forecourt = FindFirstObjectByType<ForecourtMode>();
            if (forecourt == null) return;
            forecourt.OpenStoreWith("TONY'S — SLICE HOUSE", "COUNTER SERVICE",
                "the pizza shop", TownVenue.PizzaCounter);
        }

        static FootTarget MakeDoor(Transform at, string name, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(at, false);
            var t = go.AddComponent<FootTarget>();
            t.range = range;
            return t;
        }

        // ------------------------------------------------------------------
        //  the lot and the yard
        // ------------------------------------------------------------------

        /// <summary>Fill the lot and the yard. Public and idempotent for the
        /// same reason GarageWorld's is: AddComponent runs no Start outside
        /// play mode, so a reference shot of this scene would otherwise be a
        /// photograph of an empty forecourt.</summary>
        public void PreviewBuild()
        {
            if (built) return;
            built = true;
            if (!CarCatalog.Ready) return;

            FillDealer();
            FillYard();
        }

        void FillDealer()
        {
            var stock = S != null ? S.dealerLot : null;
            if (stock == null) return;
            // An empty lot on the first visit would be a shop that looks shut.
            if (stock.Count == 0) { CarMarket.RefreshLot(S); stock = S.dealerLot; }

            for (int i = 0; i < dealerSpots.Length && i < stock.Count; i++)
            {
                var spot = dealerSpots[i];
                if (spot == null) continue;
                var listing = stock[i];
                var spec = CarCatalog.Get(listing.specId);
                var def = CarShell.DefFor(spec);
                if (def == null) continue;
                // Seeded off the LISTING, not the slot, so the blue one stays
                // the blue one when the lot reshuffles around it.
                int skin = CarShell.SkinFor(def, spec, Viewings.KeyOf(listing).GetHashCode());
                CarShell.Spawn(spot, def, skin, out Vector3 roof);
                MakeDealerCarTarget(spot, listing, roof);
            }
        }

        /// <summary>
        /// The sticker in the windscreen, for a walk-up. Each car on the lot
        /// names itself, quotes its price and condition, and hands the player
        /// to sales — which is the answer to "I can't inspect or talk to
        /// dealer about their cars": the car IS the way in now, not just the
        /// showroom door forty metres away.
        /// </summary>
        void MakeDealerCarTarget(Transform spot, CarListing listing, Vector3 roof)
        {
            var go = new GameObject("DealerCarTarget");
            go.transform.SetParent(spot, false);
            // Aim at the roof line, not the axle midpoint — the same rule every
            // car hook in the game follows, because a hook aimed at road height
            // is one the tarmac stands in front of.
            var focus = new GameObject("Focus");
            focus.transform.SetParent(spot, false);
            focus.transform.localPosition = roof;

            var t = go.AddComponent<OnFoot.FootTarget>();
            t.range = 4.6f;
            t.focus = focus.transform;
            t.ignoreRoot = spot;
            t.title = (string.IsNullOrEmpty(listing.displayName)
                          ? "A CAR FOR SALE" : listing.displayName).ToUpperInvariant();
            t.detail = MenuKit.Money(listing.price) + "   ·   " +
                       LifeRules.ConditionLabel(listing.cond) + "   ·   " +
                       listing.odoMiles.ToString("N0") + " mi" +
                       (string.IsNullOrEmpty(listing.problem)
                           ? "" : "   ·   " + listing.problem);
            t.action = "TALK TO SALES ABOUT IT";
            t.onUse = () => TownExit.GoHome(player, "dealer");
        }

        void FillYard()
        {
            if (yardSpots.Length == 0) return;
            // Off the WEEK, not off Random: the yard's own shelves turn over on
            // day-, week- and month-length clocks, and scenery that reshuffled
            // every time the player drove past would be the one thing in town
            // that never held still.
            var rng = new System.Random((S != null ? S.day / 7 : 0) * 7919 + 13);
            var all = CarCatalog.All;

            // THE SHELVES DECIDE THE STRIPPING. Wheels and tyres in the stock
            // are wheels and tyres off these cars: two stripped corners per
            // tyre-lane part the yard is selling, floored at three so the
            // compound never reads as a car park even on a lean week.
            int tyreParts = 0;
            if (S != null && S.junkyard != null)
                foreach (var p in S.junkyard)
                {
                    if (p == null) continue;
                    if (p.stat == "tires" || p.upgradeKind == "tires") tyreParts++;
                }
            int stripBudget = Mathf.Max(3, tyreParts * 2);

            for (int i = 0; i < yardSpots.Length; i++)
            {
                var spot = yardSpots[i];
                if (spot == null) continue;
                var spec = all[rng.Next(all.Count)];
                var def = CarShell.DefFor(spec);
                if (def == null) continue;

                // Which corners this one has lost. SPREAD across the yard
                // rather than gutting the first car: each shell rolls whether
                // it has been picked at all (about two in three have), then
                // loses one or two corners while the budget lasts — up to
                // three on a bad week, so even a picked-over wreck keeps one
                // wheel to sit crooked on.
                int mask = 0;
                if (stripBudget > 0 && rng.Next(3) != 0)
                {
                    int want = Mathf.Min(stripBudget, 1 + (rng.Next(4) == 0 ? 2 : rng.Next(2)));
                    for (int k = 0; k < 6 && want > 0; k++)
                    {
                        int wheel = rng.Next(4);
                        if ((mask & (1 << wheel)) != 0) continue;
                        mask |= 1 << wheel;
                        stripBudget--;
                        want--;
                    }
                }

                // SOLID now. "A box collider round it would be something a
                // player on foot catches on" held right up until the player
                // could walk the yard — at which point ghosting through a
                // dead Charger reads as the yard not being there at all
                // ("I am also walk through the cars"). A wreck you can lean
                // on is a wreck; a hologram is a bug.
                CarShell.Spawn(spot, def, rng.Next(8), out Vector3 roof, solid: true,
                               missingWheels: mask, blockMat: blockMaterial);
                MakeWreckTarget(spot, i, spec, roof);
            }
        }

        /// <summary>
        /// The walk-up offer on one wreck: get under it, and see what is left.
        ///
        /// The prompt is deliberately a DOOR rather than a shop. It used to be
        /// the whole transaction — one part, named, priced and pulled off the
        /// two lines of a walk-up hook — which is what "looking at the car only
        /// gave me one part to pull, it didn't require an inspection" was. A car
        /// has more than one part on it and a yard is somewhere you SEARCH, so
        /// the hook opens <see cref="OnFoot.WreckScreen"/> and the searching
        /// happens there.
        /// </summary>
        void MakeWreckTarget(Transform spot, int wreckIndex, CarSpec donor, Vector3 roof)
        {
            var go = new GameObject("WreckTarget");
            go.transform.SetParent(spot, false);
            var focus = new GameObject("Focus");
            focus.transform.SetParent(spot, false);
            focus.transform.localPosition = roof;

            var t = go.AddComponent<OnFoot.FootTarget>();
            t.range = 4.4f;
            t.focus = focus.transform;
            t.ignoreRoot = spot;
            RefreshWreck(t, wreckIndex, donor);
        }

        /// <summary>Rewrite one wreck's prompt from the save as it stands.
        /// Called at spawn and again every time the screen closes, because
        /// pulling something changes what the car has left on it.</summary>
        void RefreshWreck(OnFoot.FootTarget t, int wreckIndex, CarSpec donor)
        {
            if (t == null) return;
            string donorName = donor != null && !string.IsNullOrEmpty(donor.name)
                ? donor.name.ToUpperInvariant() : "SOMETHING";
            t.title = "WRECKED " + donorName;

            var s = S;
            if (s == null)
            {
                t.detail = "Dead, and going nowhere.";
                t.action = "";
                t.onUse = null;
                return;
            }

            if (!Junkyard.WreckLookedOver(s, wreckIndex))
            {
                // Nothing is named before the walk-round. The yard does not
                // hand you an inventory through the windscreen.
                t.detail = "Nobody has been over this one. Bonnet down, wheels on.";
                t.action = "GET UNDER IT";
            }
            else
            {
                int left = Junkyard.WreckPartsLeft(s, wreckIndex);
                t.detail = left > 0
                    ? left + (left == 1 ? " part" : " parts") +
                      " still on it, yours for the pulling."
                    : "Picked clean. The crusher gets it next week.";
                t.action = left > 0 ? "PULL SOMETHING OFF IT" : "LOOK AGAIN";
            }

            t.onUse = () => OpenWreck(t, wreckIndex, donor);
        }

        /// <summary>
        /// Crouch down at one shell.
        ///
        /// Freezes the walker while the page is up — the same contract the
        /// forecourt's shop keeps — and rewrites the hook on the way out,
        /// because what is left on the car is exactly what the prompt is about.
        /// The component is added and destroyed per visit rather than kept:
        /// eight shells each holding a dead screen is eight canvases waiting to
        /// be rebuilt out of a save that has moved on.
        /// </summary>
        void OpenWreck(OnFoot.FootTarget t, int wreckIndex, CarSpec donor)
        {
            var screen = gameObject.AddComponent<OnFoot.WreckScreen>();
            screen.wreck = wreckIndex;
            screen.donorName = t.title;
            var walk = OnFoot.FirstPersonWalk.Current;
            if (walk != null) walk.enabled = false;
            screen.onClosed = () =>
            {
                if (walk != null) walk.enabled = true;
                RefreshWreck(t, wreckIndex, donor);
                var foot = FindFirstObjectByType<OnFoot.FootScreen>();
                if (foot != null) foot.Invalidate();
                Destroy(screen);
            };
            screen.Open();
        }
    }
}
