using UnityEngine;
using UnityEngine.InputSystem;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// One pump on the forecourt: a trigger volume you park in, a prompt, and
    /// a hold-to-fill transaction that bills the LifeSim's wallet as the
    /// numbers roll.
    ///
    /// Placed by the scene builder against the <c>Fuel_pump</c> objects inside
    /// the station model, so the volume is where the art says the nozzle is
    /// rather than where a hand-typed offset guessed.
    ///
    /// The money comes straight out of <see cref="LifeState"/> and the tank
    /// level goes straight back into the owned car. Deliberately NOT deferred
    /// to the apply-back: a player who buys fuel and then closes the tab should
    /// still own the fuel they paid for, and making the purchase land
    /// immediately is the only way that stays true. The apply-back later writes
    /// the tank's FINAL level over the top, which is the same number for a race
    /// that runs to the flag and the honest one for a race that does not.
    ///
    /// A VISIT is the unit, not a volume and not a squeeze of the trigger. The
    /// station model carries several named pump objects whose volumes overlap,
    /// so a parked car is routinely inside two at once and a nudge forward
    /// hands it from one to the other. All the transaction state below is
    /// therefore STATIC and outlives any individual pump; a visit closes when
    /// no volume has claimed the car for a few frames, which is what "drove
    /// away" actually looks like.
    ///
    /// Everything degrades cleanly when the race scene is played standalone in
    /// the editor: no LifeState is touched and the fuel is free.
    /// </summary>
    public class GasPump : MonoBehaviour
    {
        /// <summary>Tank percent per second on the nozzle. A full fill takes
        /// about five and a half seconds — long enough that stopping for fuel
        /// costs a position, short enough that it is not a loading screen.
        /// </summary>
        public float pctPerSecond = 18f;

        // ---- shared state, read by the HUD ----
        /// <summary>What the HUD should print, or null. One line, already
        /// naming the control the player actually has.</summary>
        public static string Prompt { get; private set; }

        /// <summary>True while fuel is flowing. <see cref="PlayerCarInput"/>
        /// holds the car still on these frames.</summary>
        public static bool Fuelling { get; private set; }

        /// <summary>True while the player car is parked in ANY pump zone —
        /// including while they sit there not pumping. The stuck watchdog reads
        /// this, because a car deliberately stationary on a forecourt is
        /// exactly what "beached" looks like from the outside.</summary>
        public static bool AtPump { get; private set; }

        /// <summary>
        /// Set by <see cref="OnFoot.ForecourtMode"/> while the player is out of
        /// the car and stood at this forecourt's nozzle.
        ///
        /// The pump serves a PERSON, not a vehicle. Filling from the driver's
        /// seat was the first version and it read as a menu attached to a
        /// trigger volume; you now shut the engine off and get out, and this is
        /// the flag that says you did. The transaction stays here — one
        /// implementation of buying fuel, wherever the hands are.
        /// </summary>
        public static bool WalkerAtNozzle;

        /// <summary>Frames a visit survives with nothing claiming the car.
        /// Long enough to cover a hand-off between two overlapping volumes —
        /// which happens on a physics tick, not a frame — and short enough
        /// that driving away settles the bill immediately.</summary>
        const int HandoverGrace = 6;

        static GasPump active;
        static FuelTank tank;
        static CarController car;

        // ---- the visit ----
        /// <summary>Sub-dollar remainder carried between frames AND between
        /// volumes. Fuel is billed per percent at a fractional rate; rounding
        /// it up every time the trigger is released charges a tapped fill up to
        /// a dollar per tap, which turned a $12 tank into $22.</summary>
        static float owed;
        static int visitSpent;
        static float visitPct;
        static bool visitOpen;
        static int lastClaimFrame;

        void Awake()
        {
            // A fresh scene starts with an empty forecourt. These are statics,
            // so without this the prompt from the last race survives the load.
            //
            // RaceHandoff.FuelSpent is deliberately NOT reset here. It belongs
            // to the RACE, is cleared by RaceHandoff.ClearAll before every one,
            // and has to survive a mid-race restart — resetting it per scene
            // load also meant a strip, which has no pumps and therefore no
            // Awake, kept whatever the last circuit put there and printed a
            // receipt for fuel nobody bought.
            Prompt = null;
            Fuelling = false;
            AtPump = false;
            WalkerAtNozzle = false;
            active = null;
            tank = null;
            car = null;
            owed = 0f;
            visitSpent = 0;
            visitPct = 0f;
            visitOpen = false;
        }

        void OnDisable()
        {
            if (visitOpen) CloseVisit();
        }

        void OnTriggerEnter(Collider other) => TryClaim(other);

        /// <summary>
        /// Stay, not just Enter.
        ///
        /// The volumes overlap, so a car can be inside two at once. With Enter
        /// alone, driving out of the volume that happened to claim it while
        /// still standing in its neighbour left the player parked at a pump
        /// with no prompt and no way to get one back without driving away and
        /// coming round again.
        /// </summary>
        void OnTriggerStay(Collider other)
        {
            if (active == null) TryClaim(other);
        }

        void TryClaim(Collider other)
        {
            if (active != null && active != this) return;
            var t = other.GetComponentInParent<FuelTank>();
            if (t == null) return;
            tank = t;
            car = t.car != null ? t.car : t.GetComponent<CarController>();
            active = this;
            AtPump = true;
            visitOpen = true;
            lastClaimFrame = Time.frameCount;
        }

        void OnTriggerExit(Collider other)
        {
            if (active != this) return;
            var t = other.GetComponentInParent<FuelTank>();
            if (t == null || t != tank) return;
            // Let go of the volume, NOT of the visit. The neighbour claims on
            // its next physics tick; the watchdog closes the visit only if
            // nobody does.
            Fuelling = false;
            active = null;
        }

        void Update()
        {
            // The watchdog runs on every pump, because the one holding the
            // visit is precisely the one the car may have just left.
            if (visitOpen && active == null &&
                Time.frameCount - lastClaimFrame > HandoverGrace)
                CloseVisit();

            if (active != this) return;
            if (tank == null || car == null) { active = null; return; }

            lastClaimFrame = Time.frameCount;
            AtPump = true;

            // The nozzle only works for somebody holding it. Everything the
            // player sees from inside the car — "stop here", "press F to get
            // out" — belongs to ForecourtMode, which owns the door and the
            // ignition; this stays quiet until they are stood at the pump.
            if (PauseMenu.IsOpen || !WalkerAtNozzle)
            {
                StopFlow();
                Prompt = null;
                return;
            }

            var s = Life;

            if (tank.percent >= 99.95f)
            {
                StopFlow();
                Prompt = visitSpent > 0 ? "TANK FULL — " + MenuKit.Money(visitSpent)
                                        : "TANK FULL";
                return;
            }

            int wallet = s != null ? s.money : int.MaxValue;
            if (wallet <= 0)
            {
                StopFlow();
                Prompt = "NO MONEY FOR FUEL";
                return;
            }

            if (!HoldPressed())
            {
                StopFlow();
                Prompt = HoldControlName() + " TO FUEL   ·   TANK " +
                         Mathf.FloorToInt(tank.percent) + "%   ·   FILL " +
                         MenuKit.Money(tank.Profile.CostToFill(tank.percent));
                return;
            }

            // ---- the nozzle is running ----
            // Priced off THIS car's tank: a percent of a 28-gallon Group C car
            // is nearly three times a percent of a kei car, because it is
            // nearly three times the fuel.
            float perPct = Mathf.Max(0.0001f, tank.Profile.CostPerPct);
            float want = pctPerSecond * Time.deltaTime;
            if (s != null)
            {
                // Never spend past the wallet. The remainder already owed is
                // money the player has committed but not yet been charged, so
                // it comes off the budget before the affordable percentage is
                // worked out.
                float budget = Mathf.Max(0f, wallet - owed);
                want = Mathf.Min(want, budget / perPct);
            }

            float got = tank.Add(want);
            if (got <= 0f) { StopFlow(); return; }

            Fuelling = true;
            visitPct += got;
            owed += got * perPct;
            if (s != null)
            {
                // WHOLE dollars only. The fraction stays owed and is settled
                // once, when the visit closes.
                int whole = Mathf.FloorToInt(owed);
                if (whole > 0)
                {
                    whole = Mathf.Min(whole, s.money);
                    s.money -= whole;
                    owed -= whole;
                    visitSpent += whole;
                    RaceHandoff.FuelSpent += whole;
                }
                var owned = OwnedCar(s);
                if (owned != null) owned.fuel = tank.percent;
            }

            Prompt = "FUELLING   " + Mathf.FloorToInt(tank.percent) + "%   ·   " +
                     MenuKit.Money(visitSpent) +
                     (s != null ? "   ·   CASH " + MenuKit.Money(s.money) : "");
        }

        /// <summary>Let go of the trigger. The running total stays on screen —
        /// "TANK FULL — $9" is the last thing the player should read.</summary>
        void StopFlow()
        {
            if (!Fuelling) return;
            Fuelling = false;
            Persist();
        }

        /// <summary>
        /// Write the tank onto the owned car and save. NO money moves here.
        ///
        /// Called every time the nozzle stops rather than only when the car
        /// drives away, because a player who fills up and then closes the tab
        /// is a normal thing on the web and a purchase that only reaches the
        /// save file on the way out is one they would lose.
        /// </summary>
        void Persist()
        {
            var s = Life;
            if (s == null) return;
            var owned = OwnedCar(s);
            if (owned != null && tank != null) owned.fuel = tank.percent;
            LifeSimManager.Save();
        }

        /// <summary>
        /// The car has left the forecourt. Settle the part-dollar the running
        /// deduction could not, write ONE line in the log for the whole visit,
        /// and hand everything back.
        /// </summary>
        static void CloseVisit()
        {
            var s = Life;
            if (s != null)
            {
                // The rounding-up happens ONCE per visit. Doing it per release
                // charged a dollar for every tap of the trigger.
                int last = Mathf.CeilToInt(owed - 0.0001f);
                if (last > 0)
                {
                    last = Mathf.Min(last, s.money);
                    s.money -= last;
                    visitSpent += last;
                    RaceHandoff.FuelSpent += last;
                }

                var owned = OwnedCar(s);
                if (owned != null && tank != null) owned.fuel = tank.percent;
                if (visitPct > 0.01f)
                    s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + Mathf.RoundToInt(visitPct) +
                                      "% of fuel at the pumps — " + MenuKit.Money(visitSpent));
                LifeSimManager.Save();
            }

            owed = 0f;
            visitPct = 0f;
            visitSpent = 0;
            visitOpen = false;
            Fuelling = false;
            AtPump = false;
            WalkerAtNozzle = false;
            Prompt = null;
            tank = null;
            car = null;
            active = null;
        }

        static LifeState Life =>
            RaceHandoff.FromLifeSim ? LifeSimManager.State : null;

        static OwnedCar OwnedCar(LifeState s) =>
            s == null ? null : (s.FindCar(RaceHandoff.CarId) ?? s.ActiveCar);

        /// <summary>
        /// Hold, not tap. Filling a tank is a thing you stand there doing, and a
        /// hold is also the input that cannot be fired by accident while
        /// fighting the car onto the forecourt.
        ///
        /// F rather than E: E is the upshift and always has been. South face
        /// (A / cross) rather than any other pad button: east is the handbrake,
        /// west is the respawn, north cycles the camera and start pauses —
        /// south is the only one free while the car is moving.
        /// </summary>
        static bool HoldPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.isPressed) return true;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonSouth.isPressed) return true;
            var touch = TouchControls.Instance;
            return touch != null && touch.Visible && touch.ActionHeld;
        }

        static string HoldControlName()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible)
                return "HOLD FUEL (TOP RIGHT)";
            return Gamepad.current != null ? "HOLD X / A" : "HOLD F";
        }
    }
}
