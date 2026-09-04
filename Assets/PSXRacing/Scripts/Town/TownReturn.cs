using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;

namespace PSXRacing.Town
{
    /// <summary>
    /// The way BACK, for an errand that had to go through the front end.
    ///
    /// Reported as: "Everything I do warps me back home to the garage. Leave
    /// Pizzeria with pizza, warp back home in driveway. Paint car, warp back
    /// home to driveway. This should not happen."
    ///
    /// It was true and it was structural. The town's shops are menu PAGES —
    /// the respray book, the mechanic's job list, the dealer's stock — and the
    /// only route to a page is scene 0, which is the house. So pulling onto a
    /// body shop's forecourt teleported the player home, and finishing there
    /// left them at home, with a car parked a hundred and fifty metres up a
    /// street they were no longer standing in. Every town errand cost the whole
    /// town.
    ///
    /// This is the other half of the trip. <see cref="Arm"/> remembers WHERE
    /// the car was left before the hop; the pages the hop lands on offer a way
    /// back instead of a way home; and <see cref="TownWorld"/> puts the car
    /// down on the spot it was standing on. The player walks into a shop and
    /// comes out of the same shop.
    ///
    /// A static mailbox for the same reason <see cref="LifeSim.PizzaRun"/> and
    /// <see cref="RaceHandoff"/> are: every hop is a scene load and these
    /// fields are the only thread through one. Dying on an app kill is the
    /// right behaviour — an errand nobody finished just ends at home.
    /// </summary>
    public static class TownReturn
    {
        /// <summary>True while the player is IN TOWN with the car parked
        /// somewhere, reading a page. The front end reads it to offer the way
        /// back rather than leaving them at the house.</summary>
        public static bool Pending;

        /// <summary>What they walked into, for the button. "COLOURWORKS", not
        /// "the town": the way back should name the place it goes.</summary>
        public static string VenueName = "";

        static Vector3 carPos;
        static Quaternion carRot;

        /// <summary>Set on the hop back, consumed once by TownWorld. Separate
        /// from <see cref="Pending"/> because the flag that says "offer the
        /// door" and the flag that says "put the car here" are answered at
        /// opposite ends of a scene load.</summary>
        public static bool SpawnAtVenue;

        /// <summary>Remember where the car is standing, on the way in.</summary>
        public static void Arm(CarController car, string venueName)
        {
            VenueName = string.IsNullOrEmpty(venueName) ? "THE CAR" : venueName;
            if (car != null)
            {
                carPos = car.transform.position;
                carRot = car.transform.rotation;
                Pending = true;
            }
            else Pending = false;
            SpawnAtVenue = false;
        }

        /// <summary>Out of the page and back onto the forecourt.</summary>
        public static void Go()
        {
            if (!Pending) return;
            int idx = TrackCatalog.TownSceneIndex;
            if (idx <= 0 || idx >= SceneManager.sceneCountInBuildSettings) { Clear(); return; }

            // The drive back out is the SAME visit, so it must not be charged
            // as a fresh one: the slot was spent driving into town and the
            // errand is one errand. CommuteLeg is the existing name for
            // "this leg banks metres and fuel but never a slot".
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.CommuteLeg = true;
            var s = LifeSimManager.State;
            var car = s != null ? s.ActiveCar : null;
            RaceHandoff.CarId = s != null ? s.activeCar : null;
            RaceHandoff.CarSpecId = car != null ? car.specId : "";
            RaceHandoff.TimeOfDayIndex = s != null
                ? TimeOfDay.ForSlot(s.slotIndex, s.day) : TimeOfDay.Sunset;
            RaceHandoff.StartFuelPct = car != null ? car.fuel : 100f;
            if (s != null) LifeHomeScreen.FillCarRequestFor(s);

            SpawnAtVenue = true;
            Pending = false;
            LifeSimManager.Save();
            SceneManager.LoadScene(idx);
        }

        /// <summary>Where the car was left. Only meaningful while
        /// <see cref="SpawnAtVenue"/> is set.</summary>
        public static void Spot(out Vector3 pos, out Quaternion rot)
        {
            pos = carPos;
            rot = carRot;
        }

        public static void Clear()
        {
            Pending = false;
            SpawnAtVenue = false;
            VenueName = "";
        }
    }
}
