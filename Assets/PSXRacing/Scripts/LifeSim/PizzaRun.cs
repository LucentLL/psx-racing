using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The delivery errand as a JOURNEY, stitched across three scenes: drive
    /// from home to the shop, walk in and take the order, carry it back out to
    /// the car, drive it to the edge of town, and only then start the run.
    ///
    /// The owner's ask, verbatim: "player should drive from home to pizzeria,
    /// then pick up pizza, drive to part of map, a new menu to Random Race to
    /// deliver." Before this, GO TO WORK teleported straight to the counter and
    /// the shop door launched the race directly — the commute and the loaded
    /// drive across town, the two halves that make it a delivery JOB rather
    /// than a menu, never happened.
    ///
    /// A static mailbox like <see cref="RaceHandoff"/> and for the same reason:
    /// every hop here is a scene load, and these fields are the only thread
    /// through them. Dying on an app kill is fine — an errand abandoned mid-way
    /// just never happened, which is also RaceHandoff's contract.
    /// </summary>
    public static class PizzaRun
    {
        // ---- the commute out ----
        /// <summary>GO TO WORK was pressed at home: the town should point the
        /// player at the shop rather than leave them wondering why they are on
        /// their own driveway.</summary>
        public static bool DriveToShop;
        /// <summary>The player clocked on AT the shop's door in town, so the
        /// next DoWork is the shift itself, not another commute. Set by
        /// TownExit.ClockOn, read once by LifeHomeScreen.DoWork.</summary>
        public static bool ArrivedAtShop;

        // ---- the order on the seat ----
        /// <summary>An order is in the car, in town, on its way to the drop.
        /// While true the junction offers MAKE THE DELIVERY and the shop door
        /// takes the order back.</summary>
        public static bool Carrying;
        /// <summary>The town should spawn the car at the shop's kerb — the
        /// player just walked out of the shop, and respawning them at home
        /// would teleport the commute they drove ten minutes ago.</summary>
        public static bool SpawnAtShop;

        /// <summary>The ticket, rolled at the counter. Same fields PizzaShift
        /// used to pour straight into RaceHandoff; they wait here now because
        /// the race no longer starts at the shop door.</summary>
        public static int[] Toppings;
        public static int Pay;
        public static int TrackIndex = -1;
        public static float ParSeconds;
        /// <summary>The hour the shift left the counter, captured BEFORE the
        /// slot was spent — spending the last slot rolls the day, and a run
        /// collected at night must not arrive in tomorrow's morning light.</summary>
        public static int TodIndex;

        /// <summary>What is left of the pizza after the drive across town,
        /// 0-1. Read off the live cargo at the junction; the race grades the
        /// drop against the WORSE of this and its own leg, so a box thrown on
        /// the floor on Main Street stays thrown.</summary>
        public static float CarryCondition = 1f;

        public static void ClearRun()
        {
            Carrying = false;
            SpawnAtShop = false;
            Toppings = null;
            Pay = 0;
            TrackIndex = -1;
            ParSeconds = 0f;
            TodIndex = 0;
            CarryCondition = 1f;
        }

        public static void ClearAll()
        {
            DriveToShop = false;
            ArrivedAtShop = false;
            ClearRun();
        }

        /// <summary>Take the order at the shop counter's door: everything the
        /// run needs to know later, banked before the town leg begins.</summary>
        public static void StartRun(int[] toppings, int pay, int trackIndex,
                                    float parSeconds, int todIndex)
        {
            Toppings = toppings;
            Pay = pay;
            TrackIndex = trackIndex;
            ParSeconds = parSeconds;
            TodIndex = todIndex;
            CarryCondition = 1f;
            Carrying = true;
            SpawnAtShop = true;
            DriveToShop = false;
        }

        /// <summary>
        /// The key actually turning: the race scene, loaded from the junction.
        ///
        /// This is PizzaShift.Drive's old tail, moved wholesale. It must run
        /// AFTER the town leg has been banked (the deliverrun hop through the
        /// front end does that), because ClearAll here wipes the handoff — the
        /// same reason the pizzeria door has always routed via scene 0.
        ///
        /// The slot and the attendance are NOT spent here. Both were banked at
        /// the shop door when the order left the counter: the shift costs the
        /// evening whether or not the box arrives, and charging again at the
        /// junction would make one drop eat two thirds of a day.
        /// </summary>
        public static void LaunchDelivery(LifeState S)
        {
            if (S == null) return;
            var car = S.ActiveCar;
            if (car == null || !Carrying) { ClearRun(); return; }
            if (TrackIndex < 0 || TrackIndex >= TrackCatalog.All.Length)
                TrackIndex = LifeRules.DeliveryTrackIndex(S);

            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.Delivery = true;
            RaceHandoff.Solo = true;
            RaceHandoff.IsPractice = true;   // no purse, no rep, no rival ladder
            RaceHandoff.DeliveryPay = Pay;
            RaceHandoff.OrderToppings = Toppings;
            RaceHandoff.OrderBoxes = Toppings != null ? Toppings.Length : 1;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = car.specId;
            RaceHandoff.TrackIndex = TrackIndex;
            RaceHandoff.TimeOfDayIndex = TodIndex;
            RaceHandoff.StartFuelPct = car.fuel;
            RaceHandoff.CarryCondition = Mathf.Clamp01(CarryCondition);

            LifeHomeScreen.FillCarRequestFor(S);

            int track = TrackIndex;
            ClearRun();
            LifeSimManager.Save();
            SceneManager.LoadScene(TrackCatalog.SceneIndex(track));
        }

        /// <summary>
        /// The order never made it: handed back at the counter, or left to go
        /// cold when the player parked up for the night. The shift was still
        /// worked — the slot and the attendance stay spent, which is what an
        /// evening spent driving a pizza nowhere costs.
        /// </summary>
        public static void AbandonRun(LifeState S, string why)
        {
            if (!Carrying) { ClearRun(); return; }
            ClearRun();
            if (S != null)
                S.calendarLog.Add(LifeRules.LogDate(S.day) + ": " +
                                  (string.IsNullOrEmpty(why) ? "the delivery never went out" : why));
        }
    }
}
