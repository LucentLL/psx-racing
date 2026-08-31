using UnityEngine;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.Town
{
    /// <summary>
    /// Puts the cars in the town: the dealership's stock on its bays, and the
    /// dead ones in the yard.
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
    /// lying in the dirt is scenery, seeded off the day so it turns over with
    /// the shelves and never off Random, which would reshuffle every time the
    /// player drove past.
    /// </summary>
    public class TownWorld : MonoBehaviour
    {
        [Header("Wired by the scene builder")]
        public Transform[] dealerSpots = new Transform[0];
        public Transform[] yardSpots = new Transform[0];

        LifeState S => LifeSimManager.State;

        bool built;

        void Start() => PreviewBuild();

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
                CarShell.Spawn(spot, def, skin, out _);
            }
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
            for (int i = 0; i < yardSpots.Length; i++)
            {
                var spot = yardSpots[i];
                if (spot == null) continue;
                var spec = all[rng.Next(all.Count)];
                var def = CarShell.DefFor(spec);
                if (def == null) continue;
                // A wreck is not solid. Its shell is leaned over and half
                // sunk, so a box collider round it would be a box collider at
                // an angle in the dirt for a player on foot to catch on — and
                // nobody drives into the yard.
                CarShell.Spawn(spot, def, rng.Next(8), out _, solid: false);
            }
        }
    }
}
