namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Sets of wheels as things the player owns (owner, 2026-11-06): any car's
    /// wheels come off, go on the rack, and go on any other car, resized to it.
    /// No lug patterns and no "wrong size". A set is named by the shell it came
    /// from, because a shell carries one wheel mesh and its sheet.
    /// </summary>
    public static class WheelSets
    {
        /// <summary>The shell whose wheels this car wears.</summary>
        public static string Current(OwnedCar car, CarSpec spec) =>
            !string.IsNullOrEmpty(car.wheelKey) ? car.wheelKey : CarModelLibrary.KeyFor(spec);

        /// <summary>What a set is called: the shell's own generic name.</summary>
        public static string Label(string key)
        {
            var def = CarModelLibrary.Load(key);
            return (def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : key).ToUpperInvariant();
        }

        /// <summary>Whether a set can go on now. A car already in the air with
        /// nothing on it does not wait on tools: the wrench is what gets a wheel
        /// OFF, and a car must never be stuck with no wheels.</summary>
        public static bool CanFit(LifeState s, OwnedCar car) =>
            CarWhere.Away(s, car) ? false :
            car.noWheels ? Toolbox.RaiseOf(s, car) != Toolbox.Raise.Ground
                         : Inspection.WheelsComeOff(Toolbox.AccessFor(s, car));

        public static void Store(LifeState s, OwnedCar car, CarSpec spec)
        {
            if (car.noWheels) return;
            s.wheelRack.Add(Current(car, spec));
            car.noWheels = true;
        }

        /// <summary>Off the rack and onto the car; whatever it wore takes the
        /// rack's place.</summary>
        public static void Fit(LifeState s, OwnedCar car, CarSpec spec, string key)
        {
            if (!s.wheelRack.Remove(key)) return;
            if (!car.noWheels) s.wheelRack.Add(Current(car, spec));
            car.wheelKey = key == CarModelLibrary.KeyFor(spec) ? "" : key;
            car.noWheels = false;
        }
    }
}
