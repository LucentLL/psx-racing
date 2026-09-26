using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>Where a car physically IS. The first three are at home, in the
    /// order a house fills up; the last three are somebody else's premises, and
    /// a car standing on those cannot be driven.</summary>
    public enum CarPlace { Garage = 0, Driveway = 1, Yard = 2, Mechanic = 3, Dealership = 4, PaintShop = 5 }

    /// <summary>
    /// Where every car the player owns is standing, and whether they can have
    /// it.
    ///
    /// The owner's brief (2026-09-18): every car on the MY CARS page shows its
    /// location — "Garage, Driveway, Yard, Mechanic, Dealership, Paint Shop" —
    /// and a car that is away being worked on says it is UNAVAILABLE and when
    /// it will be ready, and that ready time goes on the calendar.
    ///
    /// NOTHING HERE IS STORED. A location is derived from two things the save
    /// already holds — the queue of jobs (<see cref="LifeState.pendingParts"/>,
    /// each of which knows which premises it is being done on) and the order of
    /// the cars — for the reason every derived thing in this project is
    /// derived: a second list that must agree with the first will not. A car
    /// with a job open at the mechanic IS at the mechanic; there is no flag to
    /// forget to clear when the job is done, and the calendar entry for the
    /// pick-up is the job's own ready day, read straight off the queue.
    ///
    /// This is a change of rule as well as of screen. RG2 kept a car in the
    /// shop off the road and the first port of it here deliberately did not
    /// ("with one car in the garage that would strand the player for days").
    /// It strands them now, on purpose: it is what makes the three venues three
    /// different offers — the mechanic is cheap and keeps the car for days, the
    /// dealership is dear and has it back in a block, and doing it yourself
    /// keeps it on your own drive — and it is the first reason the game has
    /// given anybody to own a second car.
    /// </summary>
    public static class CarWhere
    {
        // ---- the premises a job can be done on (PendingPart.venue) ----
        public const int VenueDiy = 0, VenueMechanic = 1, VenueDealer = 2, VenuePaint = 3;

        /// <summary>The names over the three doors in town. The same strings
        /// TownVenue puts on its prompts, so the page that says a car is at
        /// DELMAR AUTO and the building that says DELMAR AUTO agree.</summary>
        public const string MechanicName = "DELMAR AUTO";
        public const string DealerName = "CRESTLINE MOTORS";
        public const string PaintName = "COLOURWORKS";

        /// <summary>How the home lot fills up: one car in the garage (the
        /// starting rung is "SMALL HOUSE — 1-CAR GARAGE") and everything after
        /// that out on the grass. The home street's rooms are laid out in this
        /// order — see GarageWorld.BuildCars — so the page and the lot cannot
        /// disagree about which car is where.
        ///
        /// NO DRIVEWAY BAY since 2026-09-26, when the walk-in moved onto the
        /// home street: there the drive is the garage's only way out, and a
        /// car parked on it would wall the one with the keys in. The second
        /// car stands on the lawn beside it, and the page says YARD.</summary>
        public const int GarageBays = 1, DrivewayBays = 0;

        /// <summary>
        /// The job keeping this car away from home, or null when it is at home.
        ///
        /// The LAST one to finish, when there are several: a car in for a
        /// clutch and a stage-2 exhaust comes back when both are done, not
        /// when the first one is. DIY work (venue 0) never counts — a car on
        /// stands in your own garage is still your car on your own drive.
        /// </summary>
        public static PendingPart AwayJob(LifeState s, OwnedCar car)
        {
            if (s == null || car == null || s.pendingParts == null) return null;
            PendingPart last = null;
            foreach (var p in s.pendingParts)
            {
                if (p == null || p.carId != car.id || p.venue <= VenueDiy) continue;
                if (last == null || After(p, last)) last = p;
            }
            return last;
        }

        static bool After(PendingPart a, PendingPart b) =>
            a.readyDay > b.readyDay || (a.readyDay == b.readyDay && a.readySlot > b.readySlot);

        /// <summary>Is this car somewhere the player cannot drive it from?</summary>
        public static bool Away(LifeState s, OwnedCar car) => AwayJob(s, car) != null;

        /// <summary>The one question every launcher asks.</summary>
        public static bool Available(LifeState s, OwnedCar car) => car != null && !Away(s, car);

        static CarPlace PlaceOfVenue(int venue) =>
            venue == VenueDealer ? CarPlace.Dealership
            : venue == VenuePaint ? CarPlace.PaintShop
            : CarPlace.Mechanic;

        /// <summary>
        /// The cars that are AT HOME, in the order they are parked: the one the
        /// player drives first, then the rest in the order they were bought.
        ///
        /// The driven car gets the garage because it is the one that was put
        /// away last — you come home in it and that is where it goes — and the
        /// alternative, garage-by-purchase-order, leaves the starter car under
        /// cover for ever and the car the player actually cares about out on
        /// the grass. A car that is away is not in this list at all, which is
        /// what empties its bay in the walk-in scene.
        /// </summary>
        public static List<OwnedCar> HomeOrder(LifeState s)
        {
            var list = new List<OwnedCar>();
            if (s == null || s.cars == null) return list;
            var active = s.ActiveCar;
            if (active != null && !Away(s, active)) list.Add(active);
            foreach (var c in s.cars)
                if (c != null && c != active && !Away(s, c)) list.Add(c);
            return list;
        }

        public static CarPlace PlaceOf(LifeState s, OwnedCar car)
        {
            var job = AwayJob(s, car);
            if (job != null) return PlaceOfVenue(job.venue);
            int i = HomeOrder(s).IndexOf(car);
            if (i < 0) return CarPlace.Yard;
            return i < GarageBays ? CarPlace.Garage
                 : i < GarageBays + DrivewayBays ? CarPlace.Driveway
                 : CarPlace.Yard;
        }

        /// <summary>The owner's six words, as the status bar prints them.</summary>
        public static string Label(CarPlace p)
        {
            switch (p)
            {
                case CarPlace.Garage: return "GARAGE";
                case CarPlace.Driveway: return "DRIVEWAY";
                case CarPlace.Yard: return "YARD";
                case CarPlace.Mechanic: return "MECHANIC";
                case CarPlace.Dealership: return "DEALERSHIP";
                default: return "PAINT SHOP";
            }
        }

        /// <summary>The name over the door, for a sentence rather than a chip.
        /// Empty for a place at home.</summary>
        public static string ShopName(CarPlace p) =>
            p == CarPlace.Mechanic ? MechanicName
            : p == CarPlace.Dealership ? DealerName
            : p == CarPlace.PaintShop ? PaintName : "";

        public static string ShopNameOf(int venue) => ShopName(PlaceOfVenue(venue));

        /// <summary>
        /// When a block is, said the way somebody would say it: "TODAY · NIGHT",
        /// "TOMORROW · MORNING", "THU 4 FEB · MORNING". The block is always
        /// named, because "tomorrow" on its own is a third of a day vague and
        /// the whole game is played in thirds of a day.
        /// </summary>
        public static string WhenLabel(LifeState s, int day, int slot)
        {
            slot = Mathf.Clamp(slot, 0, LifeRules.SlotNames.Length - 1);
            string block = LifeRules.SlotNames[slot];
            int d = day - (s != null ? s.day : day);
            if (d <= 0) return "TODAY · " + block;
            if (d == 1) return "TOMORROW · " + block;
            var date = LifeRules.DateOf(day);
            return LifeRules.DowNames[LifeRules.Dow(day)] + " " + date.Day + " " +
                   LifeRules.MonthShort[date.Month - 1] + " · " + block;
        }

        /// <summary>"READY TOMORROW · MORNING", or empty for a car at home.</summary>
        public static string ReadyLabel(LifeState s, OwnedCar car)
        {
            var job = AwayJob(s, car);
            return job == null ? "" : "READY " + WhenLabel(s, job.readyDay, job.readySlot);
        }

        /// <summary>Why this car cannot be driven right now, as a line a
        /// button can wear — or null when it can.</summary>
        public static string BlockedReason(LifeState s, OwnedCar car)
        {
            var job = AwayJob(s, car);
            if (job == null) return null;
            return "AT " + ShopNameOf(job.venue) + " — " + ReadyLabel(s, car);
        }

        /// <summary>
        /// Can more work be booked on this car at this venue?
        ///
        /// A car is in one place. While it is at the mechanic the mechanic can
        /// be given more to do, and nobody else can touch it — the dealership
        /// cannot fit brake pads to a car that is on somebody else's ramp, and
        /// you cannot do a DIY clutch on a car that is not in your garage.
        /// Returns the refusal, or null.
        /// </summary>
        public static string RefuseWork(LifeState s, OwnedCar car, int venue)
        {
            var job = AwayJob(s, car);
            if (job == null || job.venue == venue) return null;
            return "car is at " + ShopNameOf(job.venue).ToLowerInvariant();
        }

        /// <summary>The first block after the one the clock is in — what "same
        /// day" means at the dealership, and the soonest anything can be
        /// promised for.</summary>
        public static void NextBlock(LifeState s, out int day, out int slot)
        {
            day = s.day;
            slot = s.slotIndex + 1;
            if (slot >= LifeRules.SlotNames.Length) { day++; slot = 0; }
        }

        /// <summary>Has the clock reached the block a job was promised for?</summary>
        public static bool Due(LifeState s, PendingPart p) =>
            s.day > p.readyDay ||
            (s.day == p.readyDay && s.slotIndex >= Mathf.Clamp(p.readySlot, 0, LifeRules.SlotNames.Length - 1));

        /// <summary>
        /// The keys, when the car you drive goes in for work.
        ///
        /// Dropping a car off means driving something else home — so if there
        /// IS something else, the player is in it now, and the page's top slot
        /// ("the car you are currently in or last drove") follows. With nothing
        /// else to drive the keys stay where they are: the top of the page is
        /// then a car that says where it is and when it is back, which is the
        /// honest picture of a one-car household with its car in the shop.
        /// Returns the car the keys moved to, or null.
        /// </summary>
        public static OwnedCar HandOver(LifeState s, OwnedCar car)
        {
            if (s == null || car == null || car.id != s.activeCar) return null;
            foreach (var other in s.cars)
            {
                if (other == null || other == car || Away(s, other)) continue;
                s.activeCar = other.id;
                return other;
            }
            return null;
        }

        /// <summary>One car due back in one block, for the calendar.</summary>
        public struct Return
        {
            public OwnedCar car;
            public CarPlace from;
        }

        /// <summary>
        /// Every car that comes home in this block: the calendar's pick-up
        /// entries, read straight off the job queue. One entry per CAR, in the
        /// block its LAST job finishes — a car with two jobs on it is one
        /// pick-up, not two.
        /// </summary>
        public static List<Return> ReturnsAt(LifeState s, int day, int slot)
        {
            var list = new List<Return>();
            if (s == null || s.cars == null) return list;
            foreach (var car in s.cars)
            {
                var job = AwayJob(s, car);
                if (job == null || job.readyDay != day) continue;
                if (Mathf.Clamp(job.readySlot, 0, LifeRules.SlotNames.Length - 1) != slot) continue;
                list.Add(new Return { car = car, from = PlaceOfVenue(job.venue) });
            }
            return list;
        }

        /// <summary>Does ANY job — a shop's or your own — finish on this day?
        /// The month grid's one-character marker.</summary>
        public static bool AnythingReadyOn(LifeState s, int day) =>
            s != null && s.pendingParts != null &&
            s.pendingParts.Exists(p => p != null && p.readyDay == day);
    }
}
