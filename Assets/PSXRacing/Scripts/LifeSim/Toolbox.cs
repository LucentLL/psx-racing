using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// What the player owns to work on a car with.
    ///
    /// Tools exist for one reason: they decide what an INSPECTION can reach.
    /// A jack lets you glance underneath and comes free with the first car; a
    /// two-post lift lets you actually LOOK, and is deliberately the most
    /// expensive thing in the game outside the cars themselves — it is the
    /// purchase that turns inspection from a guess into a survey.
    ///
    /// Ported from RG2's toolbox (src/sim/toolbox.ts, toolShop.ts) down to the
    /// items inspection actually consults. The wider tool economy — hoists,
    /// engine stands, welding kits — belongs with the DIY repair ladder, which
    /// this port does not have yet.
    /// </summary>
    public static class Toolbox
    {
        public class Tool
        {
            public string id;
            public string name;
            public string blurb;
            public int price;
            /// <summary>Comes with the first car. RG2 ships the floor jack in
            /// the starter kit, and the whole access model is built on the jack
            /// being the BASELINE rather than a purchase — without one there is
            /// no underside check at all, which would read as the feature being
            /// broken rather than as the player being under-equipped.</summary>
            public bool starter;
        }

        public const string Jack = "jack";
        public const string Lamp = "lamp";
        public const string Impact = "impact";
        public const string Scope = "borescope";
        public const string Lift = "lift";

        public static readonly Tool[] All =
        {
            new Tool { id = Jack, name = "FLOOR JACK + STANDS", price = 0, starter = true,
                       blurb = "Gets the car off the ground. Enough to look, not enough to see." },
            new Tool { id = Lamp, name = "LED SHOP LAMP", price = 35,
                       blurb = "+5% on every check. Cheapest advantage in the garage." },
            new Tool { id = Impact, name = "IMPACT WRENCH", price = 180,
                       blurb = "Wheels off in seconds — pads and rotors become checkable." },
            new Tool { id = Scope, name = "BORESCOPE CAMERA", price = 260,
                       blurb = "+15% inside the engine. Sees what the outside of a block hides." },
            new Tool { id = Lift, name = "TWO-POST LIFT", price = 2200,
                       blurb = "+15% underneath, and the only way to reach the frame rails." },
        };

        public static Tool Get(string id)
        {
            foreach (var t in All) if (t.id == id) return t;
            return null;
        }

        public static bool Owned(LifeState s, string id)
        {
            var t = Get(id);
            if (t != null && t.starter) return true;
            return s != null && s.tools != null && s.tools.Contains(id);
        }

        /// <summary>Buy a tool. Returns null on success, a reason otherwise —
        /// same contract as every other purchase in the LifeSim.</summary>
        public static string Buy(LifeState s, string id)
        {
            var t = Get(id);
            if (t == null) return "no such tool";
            if (Owned(s, id)) return "already owned";
            if (s.money < t.price) return "need " + MenuKit.Money(t.price);
            s.money -= t.price;
            s.tools.Add(id);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": bought " + t.name);
            return null;
        }

        /// <summary>
        /// How the car is standing. Owning a jack is not the same as being
        /// under the car: the tools say what the player COULD do, this says
        /// what they have actually done, and every underside check reads this
        /// one rather than the toolbox.
        ///
        /// It is the whole reason the ladder is legible. A car on its wheels
        /// has an underside nobody can see, stands get you a look, and the lift
        /// is the two thousand dollars that turns looking into seeing.
        /// </summary>
        public enum Raise { Ground = 0, Stands = 1, Lift = 2 }

        /// <summary>The best the player owns the gear for. There is no reason
        /// to choose stands over a lift you already paid for, so the garage
        /// offers ONE raise control and this is what it raises to.</summary>
        public static Raise BestRaise(LifeState s) => Owned(s, Lift) ? Raise.Lift : Raise.Stands;

        public static Raise RaiseOf(LifeState s, OwnedCar car)
        {
            if (car == null) return Raise.Ground;
            var r = (Raise)Mathf.Clamp(car.raised, 0, 2);
            // A save that says LIFT while the toolbox says otherwise is not a
            // car in the air, it is a save that has been edited. Clamp rather
            // than trust it — this value gates the frame rails.
            if (r == Raise.Lift && !Owned(s, Lift)) r = Raise.Stands;
            return r;
        }

        /// <summary>Put the car up or set it down. Returns where it ended up,
        /// so the caller can say so without asking again.</summary>
        public static Raise SetRaise(LifeState s, OwnedCar car, Raise want)
        {
            if (car == null) return Raise.Ground;
            if (want == Raise.Lift && !Owned(s, Lift)) want = Raise.Stands;
            car.raised = (int)want;
            return want;
        }

        /// <summary>Down if it is up, up to the best gear owned if it is
        /// down.</summary>
        public static Raise ToggleRaise(LifeState s, OwnedCar car) =>
            SetRaise(s, car, RaiseOf(s, car) == Raise.Ground ? BestRaise(s) : Raise.Ground);

        public static string RaiseName(Raise r) =>
            r == Raise.Lift ? "ON THE LIFT" : r == Raise.Stands ? "ON STANDS" : "ON ITS WHEELS";

        /// <summary>The tool facts an inspection roll needs plus where the car
        /// is standing, resolved once so the roll does not go shopping per
        /// sub-component.</summary>
        public struct Access
        {
            public bool lift, impact, scope, lamp;
            /// <summary>Whether the car is actually off the ground. Distinct
            /// from <see cref="lift"/>, which only says the player owns one.
            /// </summary>
            public Raise raise;
        }

        /// <summary>
        /// Access for a particular car. The car is not optional: an inspection
        /// of a car sitting on its wheels reaches different things from one of
        /// the same car in the air, and a signature that let a caller forget to
        /// say which car it meant would quietly answer for a car on the ground.
        /// </summary>
        public static Access AccessFor(LifeState s, OwnedCar car) => new Access
        {
            lift = Owned(s, Lift),
            impact = Owned(s, Impact),
            scope = Owned(s, Scope),
            lamp = Owned(s, Lamp),
            raise = RaiseOf(s, car),
        };

        public static List<Tool> Missing(LifeState s)
        {
            var outList = new List<Tool>();
            foreach (var t in All) if (!Owned(s, t.id)) outList.Add(t);
            return outList;
        }
    }
}
