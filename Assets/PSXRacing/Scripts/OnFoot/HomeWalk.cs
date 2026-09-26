using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Walking into your own house: the ONE door, from anywhere.
    ///
    /// Owner, 2026-09-26: the walk-in button "shouldn't take me to a different
    /// house and map than when choosing Drive". It did. DRIVE loads the
    /// neighbourhood - your street, your drive, your house - and the walk-in
    /// loaded Garage.unity, a second copy of the same model on a bare lawn with
    /// a strip of road in front of it. The rooms that matter (the bays, the
    /// rack, the bench, the fridge, the beds) now stand in the neighbourhood's
    /// copy (GarageWorld, embedded), so this loads THAT scene with the player
    /// on foot and the car with the keys parked in the garage - the same house
    /// you drive away from, and the same one you come home to.
    /// </summary>
    public static class HomeWalk
    {
        /// <summary>Stand the player on their own drive. No car to put in the
        /// garage is fine - you can still walk round your own house; the car
        /// with the keys being at a shop or without an engine is exactly when
        /// the player wants to look at the others.</summary>
        public static void Enter()
        {
            var S = LifeSimManager.State;
            int idx = TrackCatalog.NeighborhoodSceneIndex;
            if (S == null || idx <= 0 || idx >= SceneManager.sceneCountInBuildSettings)
            {
                // A build without the street: the old walk-in scene, which is
                // better than a black screen.
                int g = TrackCatalog.GarageSceneIndex;
                if (g > 0 && g < SceneManager.sceneCountInBuildSettings) SceneManager.LoadScene(g);
                return;
            }

            var car = S.ActiveCar;
            bool live = car != null && LifeRules.DriveRefusal(S, car) == null;

            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.ArriveOnFoot = true;
            RaceHandoff.NoCar = !live;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.ForSlot(S.slotIndex, S.day);
            if (live)
            {
                RaceHandoff.CarId = car.id;
                RaceHandoff.CarSpecId = car.specId;
                RaceHandoff.StartFuelPct = car.fuel;
                LifeHomeScreen.FillCarRequestFor(S, car);
            }
            LifeSimManager.Save();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(idx);
        }
    }
}
