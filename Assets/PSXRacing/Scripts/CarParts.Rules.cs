using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// CARPARTS STAGE 3: who may open what, where and when.
    ///
    ///  - WHERE (decision 5): parked or at walking pace, in free roam, on the
    ///    driveway, at a garage bay, at a meet (the player's own car), in the
    ///    showroom and on the inspection page. Never in a race (the grid, the
    ///    countdown, the run, the results), a city race, a replay or a
    ///    delivery (<see cref="PlaceAllows"/>).
    ///  - SPEED: a part may open below <see cref="OpenBelowKmh"/> and every
    ///    open part shuts by itself above <see cref="ShutAboveKmh"/>; between
    ///    the two an open part stays open and a shut one stays shut (the
    ///    hysteresis). Closing is always allowed.
    ///  - COCKPIT (decision 8): in the driver's own cockpit view the doors stay
    ///    shut; the hood and trunk may still open.
    ///  - THE BONNET CAP (decision 10): at most <see cref="BonnetCap"/> open
    ///    bonnets at once (4 PC, 2 phone). The player's car always gets one: the
    ///    farthest other car's bonnet shuts, eased. Another car beyond the cap
    ///    is refused its bonnet.
    ///  - THE CYCLE (decision 6): one control steps SHUT, HOOD, TRUNK, DOORS,
    ///    ALL, SHUT; steps for parts the car lacks are skipped; held
    ///    <see cref="HoldToShutSeconds"/> it shuts everything.
    ///  - SOUND: the player's own car latches and hinges through the game's
    ///    audio (an AudioSource on the body, PlayOneShot at the EFFECTS level,
    ///    under MasterOut); every other car opens silently.
    ///  - NOTHING IS SAVED: a car loads shut. The mask per car lives in memory
    ///    (<see cref="MaskOf"/>, one byte; a later online sync can send it).
    /// The control idea (one button cycling the panels, auto-close with speed)
    /// is credited in the hand-back notes, never in the game.
    /// </summary>
    public partial class CarParts
    {
        /// <summary>Hold the control this long and every part shuts.</summary>
        public const float HoldToShutSeconds = 0.6f;

        /// <summary>Tests: a cap other than the profile's (0 = the profile's).</summary>
        public static int CapOverride;
        /// <summary>At most this many bonnets open at once.</summary>
        public static int BonnetCap => CapOverride > 0 ? CapOverride : PlatformProfile.PC ? 4 : 2;

        /// <summary>The cycle's steps, in order.</summary>
        public static readonly int[] Steps = { 0, Hood, Trunk, Doors, All };
        public static readonly string[] StepNames = { "SHUT", "HOOD", "TRUNK", "DOORS", "ALL" };

        /// <summary>
        /// The step after <paramref name="current"/> for a car with
        /// <paramref name="available"/> parts. A step that opens nothing this car
        /// has, or nothing more than the step before it, is skipped; with the
        /// doors barred (the cockpit view) the DOORS step is skipped and ALL
        /// means the lids. A state that is not on the cycle (one door opened on
        /// foot) steps to SHUT.
        /// </summary>
        public static int NextStep(int current, int available, bool doorsAllowed)
        {
            int allowed = available & (doorsAllowed ? All : Lids);
            if (allowed == 0) return 0;
            current &= allowed;
            // the effective masks, duplicates dropped
            var eff = new List<int> { 0 };
            for (int i = 1; i < Steps.Length; i++)
            {
                int m = Steps[i] & allowed;
                if (m != 0 && !eff.Contains(m)) eff.Add(m);
            }
            int at = eff.IndexOf(current);
            if (at < 0) return 0;
            return eff[(at + 1) % eff.Count];
        }

        /// <summary>The name of the step a mask is (for the prompt), "OPEN" otherwise.</summary>
        public static string StepName(int mask, int available)
        {
            for (int i = 0; i < Steps.Length; i++)
                if ((Steps[i] & available) == mask && (i == 0 || mask != 0)) return StepNames[i];
            return "OPEN";
        }

        /// <summary>A driven car may open parts in this world now: free roam,
        /// the town, the home street, the driveway. Never a race scene (grid,
        /// countdown, run, results), a city race, a replay or a delivery.</summary>
        public static bool PlaceAllows()
        {
            if (RaceManager.Instance != null) return false;
            var city = City.CityMode.Instance;
            if (city != null && city.IsRace) return false;
            if (RaceHandoff.Delivery && city != null) return false;
            if (RaceReplay.Playing || TapePlayback.Active) return false;
            return true;
        }

        /// <summary>A part may open at this speed (km/h, either way).</summary>
        public static bool SpeedOpens(float kmh) => Mathf.Abs(kmh) < OpenBelowKmh;
        /// <summary>Every open part shuts by itself at this speed.</summary>
        public static bool SpeedShuts(float kmh) => Mathf.Abs(kmh) > ShutAboveKmh;

        /// <summary>The car under a body (null for a shell or the turntable).</summary>
        static CarController CarOf(MeshFilter body) => body != null ? body.GetComponentInParent<CarController>() : null;

        /// <summary>
        /// THE API for every other system (the driving seat, the walker, the
        /// showroom, later the meet figures): ask for exactly
        /// <paramref name="mask"/> open on this car. Obeys the LAB row, the
        /// refusals (no parts, pop-ups, lens B), the place and speed rules for a
        /// driven car, the cockpit's doors and the bonnet cap. Returns whether
        /// all of it was granted (closing always is). <paramref name="player"/>:
        /// the player's own car (it always gets a bonnet slot, and it is the only
        /// one that makes a sound).
        /// </summary>
        public static bool Request(MeshFilter body, CarModelDef def, int mask, bool player = false)
        {
            var car = CarOf(body);
            return RequestAt(body, def, mask, player, car != null ? Mathf.Abs(car.speedKmh) : 0f,
                             car != null && !PlaceAllows(),
                             car != null && player && ChaseCamera.Current == ChaseCamera.View.Cockpit);
        }

        /// <summary>The player's car (its CarBody), the common case.</summary>
        public static bool Request(CarBody b, int mask, bool player = true) =>
            b != null && Request(b.bodyFilter, b.Def, mask, player);

        /// <summary>The rules with the car's state given (tests, and <see cref="Request(MeshFilter, CarModelDef, int, bool)"/>).</summary>
        public static bool RequestAt(MeshFilter body, CarModelDef def, int mask, bool player, float speedKmh,
                                     bool placeBarred, bool cockpit)
        {
            if (body == null || !Allowed(def, body)) return mask == 0;
            int avail = Available(def);
            mask &= avail;
            var cp = Find(body);
            int now = cp != null ? cp.target : 0;
            int asked = mask;
            // what would newly open (closing is always allowed)
            int opening = mask & ~now;
            if (opening != 0 && (placeBarred || !SpeedOpens(speedKmh))) mask &= now;
            if (cockpit) mask &= ~Doors;
            // the bonnet cap
            if ((mask & Hood) != 0 && (now & Hood) == 0 && !BonnetSlot(body, player)) mask &= ~Hood;
            if (cp == null && mask == 0) return asked == 0;
            var got = Set(body, def, mask);
            if (got != null)
            {
                got.audible |= player;
                got.Sound(now, mask);
            }
            else if (cp != null && player) cp.Sound(now, 0);
            return mask == asked;
        }

        /// <summary>Is there a bonnet slot for this car? For the player, the
        /// farthest other car's bonnet shuts to make one.</summary>
        static bool BonnetSlot(MeshFilter body, bool player)
        {
            int open = 0;
            CarParts far = null;
            float farD = -1f;
            live.RemoveAll(c => c == null);
            foreach (var c in live)
            {
                if (c.disposed || c.filter == body || (c.target & Hood) == 0) continue;
                open++;
                float d = (c.filter.transform.position - body.transform.position).sqrMagnitude;
                if (d > farD) { farD = d; far = c; }
            }
            if (open < BonnetCap) return true;
            if (!player || far == null) return false;
            far.target &= ~Hood;   // eased shut on its own Step
            return true;
        }

        /// <summary>Step the player's car one place round the cycle.</summary>
        public static void Cycle(MeshFilter body, CarModelDef def, bool player, bool doorsAllowed)
        {
            var cp = Find(body);
            int next = NextStep(cp != null ? cp.target : 0, Available(def), doorsAllowed);
            Request(body, def, next, player);
        }

        /// <summary>Open or shut one part (the walker's targets).</summary>
        public static bool Toggle(MeshFilter body, CarModelDef def, int bit, bool player)
        {
            int now = MaskOf(body);
            return Request(body, def, (now & bit) != 0 ? now & ~bit : now | bit, player);
        }

        // ---- the per-car rules a running component keeps -------------------

        bool audible;
        AudioSource audioSrc;
        CarController carCache;
        bool carLooked;

        CarController Car
        {
            get
            {
                if (!carLooked) { carCache = CarOf(filter); carLooked = true; }
                return carCache;
            }
        }

        /// <summary>Called by Step: the speed and cockpit rules on a driven car.</summary>
        void Rules()
        {
            var car = Car;
            if (car == null) return;
            if (SpeedShuts(car.speedKmh) && target != 0) { int was = target; target = 0; Sound(was, 0); }
            if ((target & Doors) != 0 && audible && ChaseCamera.Current == ChaseCamera.View.Cockpit &&
                PlayerCarInput.Active != null && PlayerCarInput.Active.Car == car)
            {
                int was = target; target &= ~Doors; Sound(was, target);
            }
        }

        /// <summary>The latch and hinge of each part that starts to move, on the
        /// player's car only.</summary>
        void Sound(int was, int now)
        {
            if (!audible || !Application.isPlaying) return;
            int opened = now & ~was, shut = was & ~now;
            if (opened == 0 && shut == 0) return;
            if (audioSrc == null)
            {
                audioSrc = gameObject.GetComponent<AudioSource>();
                if (audioSrc == null)
                {
                    audioSrc = gameObject.AddComponent<AudioSource>();
                    audioSrc.playOnAwake = false;
                    audioSrc.spatialBlend = 0.85f;
                    audioSrc.minDistance = 2f;
                    audioSrc.maxDistance = 30f;
                }
            }
            float vol = AudioPrefs.Effects;
            // one sound a part: a door's latch, a lid's lighter catch
            for (int b = 1; b <= DoorRR; b <<= 1)
            {
                if ((opened & b) != 0)
                {
                    audioSrc.pitch = (b & Lids) != 0 ? 1.18f : 1f;
                    audioSrc.PlayOneShot(OnFoot.FootAudio.DoorOpen, vol * ((b & Lids) != 0 ? 0.6f : 0.85f));
                }
                if ((shut & b) != 0)
                {
                    audioSrc.pitch = (b & Lids) != 0 ? 1.12f : 1f;
                    audioSrc.PlayOneShot(OnFoot.FootAudio.DoorClose, vol * ((b & Lids) != 0 ? 0.75f : 0.95f));
                }
            }
        }

        // ---- the LAB row: off shuts every car, on gives them back ---------

        static readonly List<(MeshFilter body, CarModelDef def, int mask, bool audible)> remembered =
            new List<(MeshFilter, CarModelDef, int, bool)>();

        /// <summary>Every car's open mask before the row went off.</summary>
        static void Remember()
        {
            remembered.Clear();
            live.RemoveAll(c => c == null);
            foreach (var c in live)
                if (!c.disposed && c.target != 0) remembered.Add((c.filter, c.def, c.target, c.audible));
        }

        static void GiveBack()
        {
            foreach (var r in remembered)
                if (r.body != null) { var cp = Set(r.body, r.def, r.mask, instant: true); if (cp != null) cp.audible = r.audible; }
            remembered.Clear();
        }
    }
}
