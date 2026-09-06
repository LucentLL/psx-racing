using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// PUT A CAR ON A HILL, LET GO, AND MEASURE HOW FAR IT GETS.
    ///
    /// "When I park my car it tends to roll away. It should park in gear and/or
    /// with e-brake." Every part of that is invisible to the tools this project
    /// already has. A screenshot of a parked car and a screenshot of a car that
    /// is about to be somewhere else are the same picture; the whole
    /// CarController block in <see cref="LifeSimSelfTest"/> is algebra over
    /// torque curves and never steps the solver; and the bug it was hiding —
    /// the handbrake doing literally nothing below 0.3 m/s, and the low-speed
    /// brake hold being a viscous damper with a terminal creep speed by
    /// construction — is only ever visible as a car that has MOVED.
    ///
    /// So this drives the physics. Physics.simulationMode goes to Script, the
    /// car's FixedUpdate is called by hand and Physics.Simulate steps behind
    /// it, exactly the way PizzaCargoSim drives the cargo — the one existing
    /// harness in the project that does this, and the pattern rather than a new
    /// idea.
    ///
    /// THE CONTROL IS NOT OPTIONAL. A test that only asserts "the parked car
    /// stayed put" passes just as happily on a slope that is level, a car that
    /// is asleep, and a solver that was never stepped. The fourth case here
    /// takes the brakes off and asserts the car DOES run away; without it the
    /// other three prove nothing at all.
    /// </summary>
    public static class ParkedCarSim
    {
        const float Dt = 0.02f;

        /// <summary>The gradient a car is actually left on in this game. Not a
        /// round number chosen for the test: it is NbDriveGrade, the designed
        /// slope of every driveway on the player's street, and the surface the
        /// report was made about.</summary>
        const float Grade = 0.15f;

        /// <summary>How long the car is left alone, in seconds. Long enough to
        /// be a walk into a shop and back, which is the errand that loses
        /// it.</summary>
        const float Watch = 6f;

        /// <summary>Settling time before the clock starts. A car dropped onto
        /// its springs bounces, and measuring displacement from the instant of
        /// the drop measures the bounce.</summary>
        const float Settle = 1.5f;

        public struct Reading
        {
            public string name;
            public float driftM;        // how far it moved in the plane, in metres
            public float endSpeed;      // how fast it was still going at the end
            /// <summary>Wheels on the ground at the end. Reported because a
            /// drift of exactly zero is ALSO what a car that fell through the
            /// slope, hung in the air, or was never stepped would report, and
            /// those three are the ways a harness like this passes without
            /// having tested anything.</summary>
            public bool grounded;
            public int gear;            // -1 means it selected reverse, which is a result
            /// <summary>Whether the solver's parking hold was actually engaged
            /// at the end. Distinguishes "held" from "happened not to move",
            /// which are the same displacement and different bugs.</summary>
            public bool parked;
        }

        [MenuItem("PSX Racing/Preview Parked Car Sim")]
        public static void Shoot()
        {
            var r = Run();
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== PARKED CAR ON A " + (Grade * 100f).ToString("0") +
                           "% SLOPE, " + Watch.ToString("0") + " s ===");
            foreach (var x in r)
                log.AppendLine(string.Format(
                    "  {0,-22} drifted {1,7:0.000} m,  still doing {2:0.000} m/s,  {3}, gear {4}, {5}",
                    x.name, x.driftM, x.endSpeed,
                    x.grounded ? "on its wheels" : "NOT TOUCHING THE GROUND", x.gear,
                    x.parked ? "HELD" : "free"));
            File.WriteAllText("PSXRacing_parksim.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// Four cars, one slope, one question each.
        ///
        /// Returned rather than asserted here so the self-test owns the
        /// thresholds — the same division PizzaCargoSim uses, and for the same
        /// reason: a harness that grades itself is a harness whose failure mode
        /// is a quiet edit to the threshold.
        /// </summary>
        public static Reading[] Run()
        {
            var prevMode = Physics.simulationMode;
            // CarController reads Time.fixedDeltaTime, not the argument to
            // Physics.Simulate, so the two have to be told the same number or
            // the car integrates forces for one interval while the world moves
            // for another — and the discrepancy looks exactly like a physics
            // bug in whatever is being measured.
            float prevDt = Time.fixedDeltaTime;
            // ITS OWN EMPTY SCENE. Whatever the caller had open would otherwise
            // contribute its colliders to the slope test, and the self-test
            // calls this between passes that open the town and the street.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Time.fixedDeltaTime = Dt;
                BuildSlope();
                return new[]
                {
                    Case("handbrake", handbrake: true,  brake: 0f),
                    // REVERSE OFF for this one, and the first run of this
                    // harness is why. A held brake pedal at a standstill is how
                    // this game SELECTS REVERSE — UpdateGearbox arms it after
                    // 0.4 s — and from then on brakeInput is the reverse
                    // throttle. The case drove itself 39 m backwards up the
                    // hill at 11 m/s, which is correct behaviour and a useless
                    // measurement. With reverse off it asks what it meant to
                    // ask: does the pedal alone hold the car?
                    Case("brake pedal", handbrake: false, brake: 1f, reverse: false),
                    // What the game itself applies when it takes the controls
                    // away — see PlayerCarInput. It is the case the player
                    // actually hits, because getting out of the car disables
                    // input and this is what input-disabled means.
                    Case("game holding it", handbrake: true, brake: 0.3f),
                    // THE CONTROL.
                    Case("nothing (control)", handbrake: false, brake: 0f),
                };
            }
            finally
            {
                Physics.simulationMode = prevMode;
                Time.fixedDeltaTime = prevDt;
            }
        }

        /// <summary>
        /// The hill. A single tilted box, big enough that a car which runs away
        /// stays on it for the whole six seconds — a car that falls off the end
        /// reports the distance to the edge, which looks like a pass the
        /// steeper the test gets.
        /// </summary>
        static void BuildSlope()
        {
            float deg = Mathf.Atan(Grade) * Mathf.Rad2Deg;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Slope";
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.transform.rotation = Quaternion.Euler(deg, 0f, 0f);
            go.transform.localScale = new Vector3(60f, 1f, 400f);
            // Layer 0. CarController's suspension mask is
            // ~((1 << 2) | (1 << solidLayer)) — everything except cars and
            // solid scenery — so the default layer is what its wheels can see.
            go.layer = 0;
        }

        static Reading Case(string name, bool handbrake, float brake, bool reverse = true)
        {
            float deg = Mathf.Atan(Grade) * Mathf.Rad2Deg;
            var go = new GameObject("ParkTestCar") { hideFlags = HideFlags.HideAndDontSave };
            // A BoxCollider before the CarController: Awake derives the inertia
            // tensor from it and falls back to literals without one, and a car
            // with the wrong tensor pitches differently on the same slope.
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1.72f, 1.0f, 4.1f);
            box.center = new Vector3(0f, 0.6f, 0f);
            go.layer = 2;                       // the car layer, as the builder sets
            go.AddComponent<Rigidbody>();

            var car = go.AddComponent<CarController>();
            typeof(CarController).GetMethod("Awake",
                BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(car, null);

            // Nose UP the hill and standing on it, which is how a car is left
            // on a drive: you back out of the garage, so you arrive nose-in.
            // The direction matters — gravity along the car's own forward axis
            // is what the parking hold has to cancel, and a car parked across
            // the slope would test the lateral half instead.
            go.transform.SetPositionAndRotation(
                new Vector3(0f, Mathf.Tan(deg * Mathf.Deg2Rad) * 0f + 0.8f, 0f),
                Quaternion.Euler(deg, 0f, 0f));

            car.allowReverse = reverse;
            car.throttleInput = 0f;
            car.brakeInput = brake;
            car.handbrakeInput = handbrake;

            var fixedUpdate = typeof(CarController).GetMethod("FixedUpdate",
                BindingFlags.NonPublic | BindingFlags.Instance);

            void Steps(float seconds)
            {
                int n = Mathf.RoundToInt(seconds / Dt);
                for (int i = 0; i < n; i++)
                {
                    fixedUpdate?.Invoke(car, null);
                    Physics.Simulate(Dt);
                }
            }

            Steps(Settle);
            Vector3 from = go.transform.position;
            Steps(Watch);
            Vector3 to = go.transform.position;

            // IN THE PLANE. The car settles a centimetre or two into its
            // springs over the run and a 3D distance would report that as
            // rolling away.
            var d = to - from; d.y = 0f;
            var reading = new Reading
            {
                name = name,
                driftM = d.magnitude,
                endSpeed = car.Body != null ? car.Body.linearVelocity.magnitude : 0f,
                grounded = car.anyWheelGrounded,
                gear = car.currentGear,
                parked = car.Parked,
            };
            Object.DestroyImmediate(go);
            return reading;
        }
    }
}
