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
        /// <summary>Walk-up anchors, one per door a player on foot can use.</summary>
        public Transform pizzaDoor;
        public Transform dealerDoor;
        public Transform yardGate;
        public Transform homeDoor;
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

        void Start()
        {
            PreviewBuild();

            // ---- mid-errand arrival ----
            // The player just walked out of the shop with the boxes: the car
            // is at the kerb outside it, not back on the home drive where the
            // scene's baked spawn puts it.
            if (PizzaRun.SpawnAtShop && player != null && pizzaKerb != null)
            {
                PizzaRun.SpawnAtShop = false;
                player.ResetTo(pizzaKerb.position + Vector3.up * 0.3f, pizzaKerb.rotation);
            }
            else PizzaRun.SpawnAtShop = false;

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
            if (pizzaDoorTarget != null && lastDoorCarrying != PizzaRun.Carrying)
                RefreshPizzaDoor();
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

            if (PizzaRun.Carrying)
            {
                // The junction launches the run, so the junction is the way.
                anchor = FindVenue(TownVenue.Kind.Depart);
                label = "DELIVERY — TO THE JUNCTION";
                if (PizzaCargo.Instance != null && PizzaCargo.Instance.BoxCount > 0)
                {
                    PizzaRun.CarryCondition = Mathf.Min(PizzaRun.CarryCondition,
                                                        PizzaCargo.Instance.Condition);
                    if (PizzaRun.CarryCondition < LifeRules.PizzaPerfectCondition)
                        label = "DELIVERY (" +
                                LifeRules.PizzaConditionLabel(PizzaRun.CarryCondition) +
                                ") — TO THE JUNCTION";
                }
            }
            else if (PizzaRun.DriveToShop)
            {
                anchor = FindVenue(TownVenue.Kind.Pizzeria);
                label = "ON THE CLOCK — TONY'S";
            }

            if (anchor == null || label == null || player == null) return null;

            Vector3 to = anchor.position - player.transform.position;
            to.y = 0f;
            if (to.magnitude < 18f) return label;   // you are basically there
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
            if (pizzaDoor != null)
            {
                pizzaDoorTarget = MakeDoor(pizzaDoor, "PizzaDoorTarget", 3.6f);
                RefreshPizzaDoor();
            }

            if (dealerDoor != null)
            {
                var t = MakeDoor(dealerDoor, "DealerDoorTarget", 4.2f);
                t.title = "CRESTLINE MOTORS";
                t.detail = "New and used. The stock is standing right here.";
                t.action = "TALK TO SALES";
                t.onUse = () => TownExit.GoHome(player, "dealer");
            }

            if (yardGate != null)
            {
                var t = MakeDoor(yardGate, "YardGateTarget", 4.2f);
                t.title = Junkyard.YardName;
                t.detail = "Three shelves, three clocks.";
                t.action = "WALK THE SHELVES";
                t.onUse = () => TownExit.GoHome(player, "junkyard");
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

        FootTarget pizzaDoorTarget;
        bool lastDoorCarrying;

        /// <summary>What the shop door offers a walk-up, from the state as it
        /// stands: hand nothing to a player mid-run, a shift when the clock is
        /// punching, the counter otherwise.</summary>
        void RefreshPizzaDoor()
        {
            var t = pizzaDoorTarget;
            if (t == null) return;
            lastDoorCarrying = PizzaRun.Carrying;
            bool canClockOn = S != null && !string.IsNullOrEmpty(S.playerJob) &&
                              LifeRules.ShopOpen(S);
            t.title = "TONY'S — SLICE HOUSE";
            t.action2 = "";
            t.onUse2 = null;
            if (PizzaRun.Carrying)
            {
                t.detail = "The order is already in the car.";
                t.action = "";
                t.onUse = null;
            }
            else if (canClockOn)
            {
                t.detail = "The counter is up. A shift is a drive.";
                t.action = "CLOCK ON — TAKE A RUN";
                t.onUse = () => TownExit.ClockOn(player);
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
        /// The walk-up offer on one wreck: look it over, and pull what it
        /// still carries. The LOOK is the prompt itself — grade, part, effect,
        /// price and the skill it wants are all on the two lines — and the one
        /// button does the pull, gated by <see cref="Junkyard.GetPull"/> on
        /// skill, money, and whether this shell has already been stripped
        /// this week.
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
        /// Called at spawn and again after a pull, because the pull changes
        /// every line of it.</summary>
        void RefreshWreck(OnFoot.FootTarget t, int wreckIndex, CarSpec donor)
        {
            if (t == null) return;
            string donorName = donor != null && !string.IsNullOrEmpty(donor.name)
                ? donor.name.ToUpperInvariant() : "SOMETHING";
            t.title = "WRECKED " + donorName;

            var s = S;
            var car = s != null ? s.ActiveCar : null;
            var carSpec = car != null ? CarCatalog.Get(car.specId) : null;
            if (s == null || car == null)
            {
                t.detail = "Stripped to the shell, one good part left in it.";
                t.action = "";
                t.onUse = null;
                return;
            }

            var offer = Junkyard.GetPull(s, car, carSpec, wreckIndex);
            string what = Junkyard.GradeWord(offer.part.grade) + " " + offer.part.label;

            if (offer.pulled)
            {
                t.detail = "Picked clean. The crusher gets it next week.";
                t.action = "";
                t.onUse = null;
                return;
            }

            string effect = offer.quote.effect;
            t.detail = what +
                       (string.IsNullOrEmpty(effect) ? "" : "   ·   " + effect) +
                       "   ·   " + MenuKit.Money(offer.price) +
                       (offer.rental > 0 ? " incl. tool rental" : ", own tools") +
                       (offer.skillReq > 0 ? "   ·   skill " + offer.skillReq : "");

            if (!offer.can)
            {
                // The part stays named — a blocked pull should still be a
                // thing you learned by walking over — but the reason takes
                // the action line, so nothing is pressable that will refuse.
                t.detail = what + "   ·   " + offer.blocked;
                t.action = "";
                t.onUse = null;
                return;
            }

            t.action = "PULL IT — " + MenuKit.Money(offer.price);
            t.onUse = () =>
            {
                string err = Junkyard.PullFromWreck(s, car, carSpec, wreckIndex);
                var screen = FindFirstObjectByType<OnFoot.FootScreen>();
                if (err == null)
                {
                    LifeSimManager.Save();
                    if (screen != null)
                        screen.Toast("PULLED — " + offer.part.label +
                                     ", " + offer.quote.days + " DAY" +
                                     (offer.quote.days == 1 ? "" : "S") + " TO FIT");
                }
                else if (screen != null) screen.Toast(err.ToUpperInvariant());
                RefreshWreck(t, wreckIndex, donor);
                if (screen != null) screen.Invalidate();
            };
        }
    }
}
