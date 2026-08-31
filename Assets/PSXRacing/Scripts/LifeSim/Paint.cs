using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// What colour a car is, and how to change it.
    ///
    /// The owner's ask: "currently there is no option to change the paint
    /// color of cars even though they have multiple options." Both halves were
    /// true. Every shell the vehicle pack ships is baked with a handful of
    /// liveries — <see cref="CarModelDef.skinMaterials"/>, with a name and a
    /// mean colour for each — and the only thing that had ever chosen between
    /// them was <see cref="CarModelDef.SkinFor"/>, matching the catalog's
    /// nominal colour once at spawn. The player had no say at all.
    ///
    /// So there is now ONE answer to "what colour is this car", and this is
    /// it. That matters more than it looks: before this, an owned car was
    /// coloured by three different salts in three different places — CarBody
    /// salted off the SPEC id for the grid, GarageWorld off the OWNED CAR id
    /// for the bay it stood in, and the turntable off the spec again — so a
    /// car could be silver on the menu, blue in the garage and silver again on
    /// track, and nothing in the code claimed otherwise. Everything that draws
    /// a car the player OWNS goes through <see cref="SkinFor"/> now.
    ///
    /// The override is stored as a NAME rather than an index. Indices are
    /// positions in a baked array, and the array is rebuilt from the pack
    /// every time CarModelBaker runs: a save holding "3" would silently become
    /// a different colour the day a livery was added, whereas a save holding
    /// "midnight_purple" either finds it or falls back to the factory answer.
    /// </summary>
    public static class Paint
    {
        /// <summary>What a full respray costs before the per-car multiplier.
        /// Six times PAINT TOUCH-UP, because it is the whole car rather than
        /// the corner somebody scuffed, and because a colour change should be
        /// a decision rather than something you do while you wait.</summary>
        public const int RespraySpend = 380;

        /// <summary>The shell this car wears, or null when the model library
        /// has nothing for it.</summary>
        public static CarModelDef DefFor(CarSpec spec) =>
            spec != null ? CarModelLibrary.LoadFor(spec)
                         : CarModelLibrary.Load(CarModelLibrary.Default);

        /// <summary>
        /// The livery a car left the factory in: the catalog colour matched
        /// against the shell's baked liveries.
        ///
        /// The salt is the SPEC id, which is CarBody's — a car's default
        /// colour is a property of what it is, not of which copy of it the
        /// player happens to own.
        /// </summary>
        public static int FactorySkin(CarSpec spec, CarModelDef def)
        {
            if (def == null || def.SkinCount == 0) return 0;
            return def.SkinFor(spec != null ? spec.color : null,
                               spec != null && spec.id != null
                                   ? Mathf.Abs(spec.id.GetHashCode()) % 97 : 0);
        }

        /// <summary>The livery this car is wearing RIGHT NOW: the respray if
        /// there has been one and the shell still has that colour, the factory
        /// answer otherwise.</summary>
        public static int SkinFor(OwnedCar car, CarSpec spec, CarModelDef def)
        {
            if (def == null || def.SkinCount == 0) return 0;
            if (car != null && !string.IsNullOrEmpty(car.paintSkin))
            {
                int i = IndexOf(def, car.paintSkin);
                if (i >= 0) return i;
            }
            return FactorySkin(spec, def);
        }

        /// <summary>The same question when the caller has only the car.</summary>
        public static int SkinFor(OwnedCar car)
        {
            var spec = car != null ? CarCatalog.Get(car.specId) : null;
            return SkinFor(car, spec, DefFor(spec));
        }

        public static int IndexOf(CarModelDef def, string skinName)
        {
            if (def == null || def.skinNames == null || string.IsNullOrEmpty(skinName)) return -1;
            for (int i = 0; i < def.skinNames.Length; i++)
                if (def.skinNames[i] == skinName) return i;
            return -1;
        }

        /// <summary>The livery's baked name, tidied for a menu row: the pack
        /// writes them as file stems ("midnight_purple"), and a colour chart
        /// with underscores in it reads as a debug dump.</summary>
        public static string LabelOf(CarModelDef def, int skin)
        {
            if (def == null || def.skinNames == null ||
                skin < 0 || skin >= def.skinNames.Length) return "FACTORY";
            string raw = def.skinNames[skin];
            if (string.IsNullOrEmpty(raw)) return "FACTORY";
            return raw.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
        }

        /// <summary>The swatch. Baked per livery by CarModelBaker as the mean
        /// colour of the sheet, which is the only honest answer for a texture
        /// that carries a whole car on it.</summary>
        public static Color ColorOf(CarModelDef def, int skin)
        {
            if (def == null || def.skinColors == null ||
                skin < 0 || skin >= def.skinColors.Length)
                return new Color(0.5f, 0.5f, 0.5f);
            var c = def.skinColors[skin];
            c.a = 1f;
            return c;
        }

        /// <summary>What the body shop wants for it. The same
        /// clamp(sqrt(price/15000), 0.6, 3.5) curve every other service on a
        /// car uses, so a respray on an exotic costs more than one on a beater
        /// without becoming a different order of magnitude.</summary>
        public static int Cost(OwnedCar car) =>
            car == null ? RespraySpend : LifeRules.ServiceCost(car, RespraySpend);

        /// <summary>
        /// Shoot it.
        ///
        /// A respray is a REFINISH as well as a colour change — the car comes
        /// back with fresh paint on it — so it clears the paint stat's wear
        /// and any fault filed against paint, exactly as PAINT TOUCH-UP does.
        /// Charging for a colour change and leaving the panels looking rough
        /// would be selling half a job.
        ///
        /// Returns null on success, or the reason it did not happen.
        /// </summary>
        public static string Respray(LifeState s, OwnedCar car, int skin)
        {
            if (s == null || car == null) return "no car";
            var spec = CarCatalog.Get(car.specId);
            var def = DefFor(spec);
            if (def == null || def.SkinCount == 0) return "no colours for this shell";
            if (skin < 0 || skin >= def.SkinCount) return "no such colour";

            int price = Cost(car);
            if (s.money < price) return "need " + MenuKit.Money(price);

            s.money -= price;
            car.paintSkin = def.skinNames != null && skin < def.skinNames.Length
                ? def.skinNames[skin] : "";
            car.paint = 100f;
            car.faults.RemoveAll(f => f.stat == "paint");
            // Same sweep BuyService does on the paint lane, and with the same
            // two exemptions: an upgrade build and a salvage part are things
            // the player has already paid for, not appointments to cancel.
            s.pendingParts.RemoveAll(p => p.carId == car.id && p.stat == "paint" &&
                                          !p.IsUpgrade && !p.IsYardPart);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": resprayed " +
                              LabelOf(def, skin).ToLowerInvariant() + " — " +
                              MenuKit.Money(price));
            return null;
        }
    }
}
