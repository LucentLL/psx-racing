using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The one place that knows whether "driving" is being managed by a
    /// RaceManager (circuits, strips) or a CityMode (Charlotte free roam).
    ///
    /// Everything that used to reach for RaceManager.Instance to respawn a
    /// car or ask "is the session live" goes through here instead, so the
    /// city did not have to grow a fake RaceManager and the circuits did not
    /// have to learn about the city.
    /// </summary>
    public static class DriveSession
    {
        /// <summary>Car responds to input and watchdogs may act.</summary>
        public static bool Live
        {
            get
            {
                var rm = RaceManager.Instance;
                if (rm != null) return rm.State == RaceManager.RaceState.Racing;
                var city = City.CityMode.Instance;
                return city != null && city.Live;
            }
        }

        /// <summary>Put a car back on the road, whichever world this is.</summary>
        public static void Respawn(CarController car)
        {
            if (car == null) return;
            var city = City.CityMode.Instance;
            if (city != null) { city.Respawn(car); return; }
            RaceManager.Instance?.RespawnCar(car);
        }
    }
}
