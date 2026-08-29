using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Reads the LifeSim's half of <see cref="RaceHandoff"/> when the race scene
    /// boots and applies it: the player's fault handicaps onto the car, the
    /// opponents' catalog specs onto the AI field, the time-of-day onto the sun,
    /// and the gauge flags onto the HUD.
    ///
    /// Everything here no-ops cleanly when <c>FromLifeSim</c> is false, so
    /// pressing Play on CityCircuit in the editor still gives the standalone
    /// demo race it always did. That property is worth protecting: it is the
    /// fastest way to test a physics change without going through the menus.
    ///
    /// RaceManager calls <see cref="Apply"/> at the top of its own Start rather
    /// than letting this run on its own Start, because the field application
    /// RETIRES cars and RaceManager builds its progress table from that list.
    /// Two Starts on the same object have no defined order between them, and the
    /// half of the time it lost that race the manager tracked a car that was not
    /// in the race.
    /// </summary>
    public class RaceHandoffApplier : MonoBehaviour
    {
        public CarController playerCar;
        public RaceHUD hud;
        public Light sun;
        /// <summary>The AI cars in grid order, set by the builder. Order is the
        /// contract with OpponentSpecIds: entry 0 gets the first opponent.</summary>
        public List<CarController> aiCars = new List<CarController>();

        bool applied;

        // Belt and braces: RaceManager normally calls Apply first. If this Start
        // wins the coin flip instead, it must still hand over the manager's list
        // — retiring a car without removing it from that list leaves the timing
        // sheet tracking a deactivated object that never crosses the line.
        // Instance is safe to read here: every Awake runs before every Start.
        void Start() => Apply(RaceManager.Instance != null ? RaceManager.Instance.allCars : null);

        /// <summary>
        /// Apply the whole request. <paramref name="raceField"/> is RaceManager's
        /// own car list; when supplied, retired opponents are removed from it so
        /// the manager never tracks a car that is not racing. Safe to call twice —
        /// RaceManager calls it first, then this component's Start finds the work
        /// already done.
        /// </summary>
        public void Apply(List<CarController> raceField)
        {
            if (applied) return;
            applied = true;
            // Time of day is applied EVEN on a standalone editor race, unlike
            // everything else here: the scene is baked at one hour, and the
            // hour is now the cheapest thing to vary while testing. Pressing
            // Play still gives the sunset the scene was built with, because
            // that is the default the handoff clears to.
            TimeOfDay.Apply(RaceHandoff.TimeOfDayIndex, sun);

            if (!RaceHandoff.FromLifeSim) return;

            if (playerCar != null)
            {
                var spec = CarCatalog.Get(RaceHandoff.CarSpecId);
                if (spec != null)
                {
                    // Shell BEFORE spec: fitting a body writes wheel radius, and
                    // ApplySpec builds the gearbox off it.
                    ApplyShell(playerCar, spec);
                    // Spec first: ApplySpec rewrites mass, torque curve, gearing
                    // and drag from the catalog entry AND the parts bolted to
                    // this particular car; the fault handicaps below multiply on
                    // top of that result.
                    playerCar.ApplySpec(spec, new CarTune.Stages
                    {
                        power = RaceHandoff.UpPower,
                        weight = RaceHandoff.UpWeight,
                        brakes = RaceHandoff.UpBrakes,
                        suspension = RaceHandoff.UpSuspension,
                        tires = RaceHandoff.UpTires,
                    });
                    // Mods go on BEFORE the voice: a blower changes what the car
                    // sounds like as well as what it makes.
                    playerCar.weldedDiff = RaceHandoff.Welded;
                    playerCar.supercharged = RaceHandoff.Supercharged;
                    ApplyVoice(playerCar, spec, RaceHandoff.Supercharged, isPlayer: true);
                }

                // The tank arrives with whatever is in it. A car sent out on a
                // third of a tank is a car whose driver has to plan a stop at
                // the forecourt — which is only a decision if the game hands
                // over the real number instead of a full one.
                var tank = playerCar.GetComponent<FuelTank>();
                if (tank != null)
                {
                    tank.percent = Mathf.Clamp(RaceHandoff.StartFuelPct, 0f, 100f);
                    // A rich mixture or a weeping tank is a fault effect, and it
                    // used to be applied once at the end against the whole
                    // race. Applied per metre it is the same total and a
                    // visibly faster-falling needle.
                    tank.burnMult = RaceHandoff.FuelMult;
                }

                playerCar.faultAccelMult = RaceHandoff.AccelMult;
                playerCar.faultGripMult = RaceHandoff.GripMult;
                playerCar.faultBrakeMult = RaceHandoff.BrakeMult;
                playerCar.faultShiftMult = RaceHandoff.ShiftMult;
                playerCar.faultSteerPull = RaceHandoff.SteerPull;
            }

            ApplyField(raceField);

            if (hud != null)
            {
                hud.hideGauges = RaceHandoff.HideGauges;
                hud.rpmFlutter = RaceHandoff.RpmFlutter;
            }
        }

        /// <summary>
        /// Spec the AI field from the catalog. Until this existed the player
        /// could buy any of 317 cars and still line up against four identical
        /// RX-7s — a 90 hp hatchback and a 600 hp supercar raced the same
        /// opponents, so the field said nothing about whether the car was any
        /// good. A shorter list than the grid retires the spare cars, which is
        /// how a blacklist challenge gets its 1v1.
        /// </summary>
        void ApplyField(List<CarController> raceField)
        {
            // Solo first, and BEFORE the empty-list early-out. An empty
            // OpponentSpecIds means "the track's own grid stands", so a
            // delivery run that simply named no opponents would line up against
            // four street racers on the way to drop off a pizza.
            if (RaceHandoff.Solo)
            {
                foreach (var ai in aiCars)
                {
                    if (ai == null) continue;
                    raceField?.Remove(ai);
                    ai.gameObject.SetActive(false);
                }
                return;
            }

            var ids = Split(RaceHandoff.OpponentSpecIds);
            if (ids.Length == 0) return;

            var skills = Split(RaceHandoff.OpponentSkills);

            for (int i = 0; i < aiCars.Count; i++)
            {
                var ai = aiCars[i];
                if (ai == null) continue;

                if (i >= ids.Length)
                {
                    // Retired. Removed from the manager's list first — a
                    // deactivated object still answers GetComponent, so leaving
                    // it in would keep it on the timing sheet as a car sitting
                    // on the grid forever.
                    raceField?.Remove(ai);
                    ai.gameObject.SetActive(false);
                    continue;
                }

                var spec = CarCatalog.Get(ids[i]);
                if (spec != null) { ApplyShell(ai, spec); ai.ApplySpec(spec); ApplyVoice(ai, spec); }

                var driver = ai.GetComponent<AIDriver>();
                if (driver != null && i < skills.Length &&
                    float.TryParse(skills[i], System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out float sk))
                    driver.skill = Mathf.Clamp(sk, 0.8f, 1.05f);
            }

            // A 1v1 lines up SIDE BY SIDE. The grid staggers four cars over four
            // rows with the player at the back, so retiring two of them leaves
            // the challenger starting twenty metres up the road — which reads as
            // the game cheating rather than as a grudge race. Set the transform
            // directly instead of going through ResetTo: nothing has stepped
            // yet, and ResetTo's ride-height nudge would stack on the grid's own.
            if (ids.Length == 1 && playerCar != null && aiCars.Count > 0 && aiCars[0] != null)
            {
                var rival = aiCars[0].transform;
                var path = RaceManager.Instance != null ? RaceManager.Instance.path : null;

                if (path != null && path.drag)
                {
                    // A drag venue already stages ABREAST, but in the four-car
                    // lanes — the surviving pair sit lopsided on the left half
                    // of the strip, and "rival.right * 5.2" measured for the
                    // 2x2 circuit grid can push the player to the wall on an
                    // 11 m stage road. Restage both cars in the two CENTRE
                    // lanes off the path itself: a proper heads-up run, on any
                    // road width, on every venue that calls itself a drag.
                    int idx = path.NearestIndex(rival.position);
                    Vector3 centre = path.GetPoint(idx);
                    var rot = path.GetRotation(idx);
                    Vector3 right = rot * Vector3.right;
                    float lane = Mathf.Min(path.roadWidth / 6f, 2.75f);
                    Vector3 lift = Vector3.up * (rival.position.y - centre.y);
                    rival.SetPositionAndRotation(centre - right * lane + lift, rot);
                    playerCar.transform.SetPositionAndRotation(centre + right * lane + lift, rot);
                }
                else
                {
                    playerCar.transform.SetPositionAndRotation(
                        rival.position + rival.right * RivalGridGapM, rival.rotation);
                }
            }
        }

        /// <summary>
        /// Give a car the body it actually has. Until the vehicle pack landed
        /// the grid was four RX-7s in four colours whatever the player bought,
        /// so a Charger and a Civic were the same silhouette with different
        /// numbers; CarModelLibrary now picks the closest shell the pack ships.
        ///
        /// Silently does nothing on a car the builder did not give a CarBody —
        /// which is what keeps an older saved scene loading.
        /// </summary>
        static void ApplyShell(CarController car, CarSpec spec)
        {
            var shell = car.GetComponent<CarBody>();
            if (shell != null) shell.ApplySpec(spec);
        }

        /// <summary>
        /// Give a car the engine it actually has. Shape gets it only halfway —
        /// sixteen shells cover 317 cars — so the voice still does most of the
        /// work of telling a 660 cc kei car apart from a race V8, which makes
        /// this the single highest-value thing the spec carries after physics.
        ///
        /// Both halves are gated on data, not on assumptions: the family key
        /// comes from RG2's resolver (baked per car), and the forced-induction
        /// layer comes from the GT4 aspiration field. Before this, every car in
        /// the game idled like a 13B and blew off like a sequential twin-turbo.
        /// </summary>
        static void ApplyVoice(CarController car, CarSpec spec, bool blowerFitted = false, bool isPlayer = false)
        {
            var engine = car.GetComponent<EngineAudio>();
            if (engine != null)
            {
                // Order matters: SetVoice before SetFamily, because SetFamily
                // builds the ladder and a band's home RPM is derived from the
                // car's own rev range. Both are cheap and idempotent.
                engine.SetVoice(spec);
                engine.SetFamily(spec);
            }

            // The formant and the exhaust shelf need a parametric EQ, and the
            // only one in the project is the master chain on the listener — so
            // the car being DRIVEN sets it. Opponents keep the pitch and level
            // half of their voice and share the player's tone, which is what
            // you would hear from outside their car anyway.
            if (isPlayer)
            {
                var tone = UnityEngine.Object.FindFirstObjectByType<AudioToneChain>();
                if (tone != null) tone.SetVoiceFormant(spec);
            }

            var boost = car.GetComponent<TurboAudio>();
            if (boost != null)
                boost.SetAspiration(
                    spec.IsTurbo ? TurboAudio.Aspiration.Turbo :
                    (spec.IsSupercharged || blowerFitted) ? TurboAudio.Aspiration.Supercharger :
                    TurboAudio.Aspiration.NaturallyAspirated);
        }

        /// <summary>Lateral gap for a 1v1 start. The AI car sits 2.6 m left of
        /// the centreline, so this puts the player 2.6 m right of it — clear of
        /// contact, and inside the 12 m road.</summary>
        const float RivalGridGapM = 5.2f;

        static readonly string[] Empty = new string[0];
        static string[] Split(string joined) =>
            string.IsNullOrEmpty(joined) ? Empty : joined.Split(';');

    }
}
