using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Drive the cargo simulation headlessly and photograph what it does.
    ///
    /// Everything the Pizza Cam exists to show is motion, and motion is exactly
    /// what a screenshot pass cannot see. The failure modes are all severe and
    /// all silent: a stack pitched wrong explodes on frame one, a pizza spawned
    /// inside its own lid is launched out of the car before the lights go out, a
    /// condition that decays while the car is parked refuses every delivery in
    /// the game. None of them throw and all of them ship.
    ///
    /// So the simulation is DRIVEN — Physics.simulationMode is switched to
    /// Script and this steps it by hand with accelerations it chose: at rest,
    /// through a hard left, and into a wall. Three questions, in order: does it
    /// sit still, does it slide, does it break.
    ///
    /// Writes to Screenshots/PizzaCargo and returns the readings so
    /// LifeSimSelfTest can assert on them.
    /// </summary>
    public static class PizzaCargoSim
    {
        /// <summary>Run the whole suite and PHOTOGRAPH every case. The self-test
        /// runs the same six steps with the shutter closed and asserts on the
        /// numbers; this is for when a number has changed and the question is
        /// what it looks like. Six pictures beat six floats for "is the load
        /// where I would expect it".</summary>
        [MenuItem("PSX Racing/Preview Pizza Cargo Sim")]
        public static void Shoot() => Run(shoot: true);

        /// <summary>
        /// THE CARRY, from the eye that carries it.
        ///
        /// Its own pass rather than a case inside Run, because it is not a
        /// simulation — nothing is stepped and there is nothing to grade. What
        /// can be wrong with it is entirely framing: a stack too low is invisible
        /// behind the HUD, too high blocks the door you are walking through, and
        /// too close is clipped in half by the near plane. All three are
        /// questions only a picture answers, and none of them is answerable by
        /// playing the game once and squinting.
        /// </summary>
        [MenuItem("PSX Racing/Preview Pizza Carry")]
        public static void ShootCarry()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      "Screenshots", "PizzaCargo");
            Directory.CreateDirectory(dir);
            Lighting();

            var head = new GameObject("Head").transform;
            head.position = new Vector3(0f, 1.62f, 0f);

            var camGO = new GameObject("CarryCam");
            camGO.transform.SetParent(head, false);
            var cam = camGO.AddComponent<Camera>();
            // The walker's own lens: 52 vertical, and a near plane close enough
            // that the stack is not sliced through. Both come from the game —
            // shooting this at a preview-friendly FOV would certify a framing
            // the player never gets.
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f, 1f);

            foreach (var boxes in new[] { 1, 3 })
            {
                for (int bottles = 0; bottles <= 2; bottles += 2)
                {
                    PSXRacing.OnFoot.PizzaCarry.Clear();
                    var order = new int[boxes];
                    for (int i = 0; i < boxes; i++) order[i] = i * 3;
                    var carry = PSXRacing.OnFoot.PizzaCarry.SpawnOn(head, order, bottles);
                    if (carry == null)
                    {
                        Debug.LogError("[PizzaCarry] rig did not build — no baked prefabs?");
                        return;
                    }
                    string tag = "carry_" + boxes + "box_" + bottles + "bottle";
                    ShootCamera(cam, dir, tag);
                    Debug.Log("[PizzaCarry] " + tag);
                }
            }
            PSXRacing.OnFoot.PizzaCarry.Clear();
        }

        /// <summary>Render one frame of an arbitrary camera to a PNG. The sim's
        /// own Shoot builds a camera per call around the cargo island; this one
        /// is handed a camera that is already where it needs to be.</summary>
        static void ShootCamera(Camera cam, string dir, string name)
        {
            var rt = new RenderTexture(854, 480, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }


        public struct Reading
        {
            public float atRest;      // condition after two seconds of nothing
            public float afterCorner; // after a sustained 0.9 g left-hander
            public float afterRough;  // the same corner on a real road surface
            /// <summary>Condition after a heavy stop. This case exists because
            /// the seat has NOTHING across its front — friction is the only
            /// thing holding the load in, by design — so "does an ordinary
            /// emergency stop dump the order in the footwell" is a question the
            /// geometry can no longer answer on its own.</summary>
            public float afterBraking;
            /// <summary>Condition after a LIGHT knock — 3 m/s of closing speed,
            /// a kerb or a brushed barrier. The jolt channel is proportional, so
            /// this is the case that proves it is proportional: a scrape has to
            /// cost something and it must not cost the order.</summary>
            public float afterKnock;
            /// <summary>What the knock alone cost, as a delta, for the same
            /// reason the firm stop is a delta.</summary>
            public float knockCost;

            public float afterCrash;  // after a full-clamp frontal impact

            /// <summary>Condition after THE MANOEUVRE THAT WAS REPORTED: a
            /// handbrake 180 at 80 mph. The centre of mass scrubs off speed at
            /// about 0.8 g while the car spins at three-and-a-half radians a
            /// second, and the passenger seat — half a metre off the axis — is
            /// flung outward on top of that. "The boxes barely move" was true,
            /// and it was true because the seat's own motion was not modelled;
            /// this case is what proves it is now.</summary>
            public float afterSpin;
            /// <summary>How far the bottom box went in that spin. The number
            /// the complaint was actually about.</summary>
            public float bottomSlideSpin;
            /// <summary>Condition after a FIRM stop — 0.7 g, the hardest brake
            /// most driving ever needs. This has to be free on every seat, or
            /// the delivery job is a game about never braking.</summary>
            public float afterFirmStop;
            /// <summary>What the firm stop ALONE cost: condition before it
            /// minus condition after. The absolute number above carries every
            /// case before it, and a stop that cost nothing was failing on a
            /// corner's wear.</summary>
            public float firmStopCost;

            /// <summary>The same corner, spin and panic stop, once per rung of
            /// the seat ladder, indexed by stage. The shop sells five seats on
            /// the promise that each holds the load better than the last, and a
            /// promise like that is an assertion or it is marketing.</summary>
            public float[] tierCorner, tierSpin, tierBraking, tierSlideSpin;

            /// <summary>
            /// ONE BOX, per seat, through the handbrake 180 — the case the
            /// owner named: "especially when just one pizza box is in the
            /// seat". A single box has nothing sitting on it, so it is the
            /// most mobile load the game carries and the cleanest signal a
            /// seat can be judged on: in a stack the bottom box is pinned by
            /// the ones above it and the top box's slide is damped by the
            /// friction it drags across, and the ladder's differences are
            /// buried in that. Condition after the spin, and how far the box
            /// went, indexed by seat stage.
            /// </summary>
            public float[] singleSpin, singleSlide;
            /// <summary>How far the BOTTOM box slid on the rough corner and in
            /// the crash, in metres. Condition alone cannot see the bug this
            /// exists for: a box wedged between two bolsters 3.5 cm off its own
            /// edges reads a perfect 1.00 through everything, which is exactly
            /// what "I drove into a wall full speed and the bottom pizza barely
            /// moved, even side to side" looked like from in here.</summary>
            public float bottomSlideRough, bottomSlideCrash;
            /// <summary>Displacement after a SIDE impact: the least-moved box,
            /// the most-moved box, and the bottom one. There was no lateral
            /// displacement reading of any kind — the one side-jolt case in the
            /// harness only checked that a shut box kept its pizza — so "I
            /// slammed into a wall with the passenger side and the bottom box
            /// did not move" was a report the suite could not have caught, and
            /// the only crash-displacement floor it does own is fed a pure
            /// forward jolt where the seat is open by design.</summary>
            public float sideSlideMin, sideSlideMax, sideSlideBottom;

            // ---- 2026-09-07: the two reports ------------------------------
            //
            // "The pizza clips through the box, eventually breaking through
            // and sitting on top of the box" and "I crashed head first into a
            // wall and the pizza flew off but the box still sat there".
            // Neither was a case; the suite passed with both true.

            /// <summary>S7. Shut boxes found with their pizza escaped, off its
            /// home, or above the box, summed over every reading of the
            /// integrity run. Zero, or a shut box is not a box.</summary>
            public int shutViolations;
            /// <summary>S7. The largest distance any SHUT box's pizza was
            /// found from where it was packed, in metres.</summary>
            public float shutMaxHomeError;
            /// <summary>S8. How far forward a lone box on a stock seat went
            /// in a head-on that cost the car 15 m/s (54 km/h), without and
            /// with a bottle parked in front of it; whether it left the seat
            /// for the footwell; whether its lid came off.</summary>
            public float oneBoxCrashZ, oneBoxCrashBottleZ;
            public bool oneBoxCrashLeftSeat, oneBoxCrashBottleLeftSeat;
            public bool oneBoxCrashOpened, oneBoxCrashBottleOpened;
            /// <summary>S8b. The same lone box through a 6 g stop delivered
            /// on the ACCELERATION channel only — the hit the responder never
            /// classifies as a wall. It still has to move the box.</summary>
            public float oneBoxSixGZ;
            public bool oneBoxSixGLeftSeat;
            /// <summary>S9. THE TUMBLE — a lone box turned over BY HAND
            /// (PizzaCargo.TurnBoxOver) past LidOpenTiltDeg and dropped: did
            /// the lid open, did the pizza leave (escaped, or how far from
            /// home in metres); and the rule's control, a box turned to ten
            /// degrees short of it and read once: did it stay shut.</summary>
            public bool tumbleOpened, tumbleEscaped, tumblePizzaLoose;
            public float tumbleHomeError;
            /// <summary>How far the pizza dropped off the floor of the
            /// upturned box, in metres — PizzaCargo.PizzaOffFloor, the
            /// reading that can see a pizza lying under its own box.</summary>
            public float tumbleOffFloor;
            public bool tumbleControlOpened;
            /// <summary>S9. The seat rolled to 45 and held: did the box stay
            /// shut with the pizza where it was packed.</summary>
            public bool leanOpened;
            public float leanHomeError;
            /// <summary>S9c. The seat rolled to 70 — a car on two wheels —
            /// and brought back over the same fifteen frames: the box must
            /// still be shut, ON the pan (its y offset: -0.01 at rest, -0.14
            /// for a box the pan has swept over) and packed. This is the
            /// case that used to read "tumbled": a seventy degree roll never
            /// turns a box on a bench, and what it had measured was the
            /// harness levelling the seat in one frame and the pan passing
            /// through the box.</summary>
            public bool rollOpened, rollGrounded;
            public float rollY, rollHomeError;
            /// <summary>S9b. The friction calibration, as a pair of leans on
            /// a fresh lone box: how far it slid across the stock bench held
            /// at 30 degrees (cardboard on cloth, 0.7, holds to 35) and at 40
            /// (it must go). The solver's patch-friction model doubles a
            /// material's coefficient and PizzaCargo halves it back; this
            /// is the reading that says the two still cancel.</summary>
            public float holdLeanSlideM, slipLeanSlideM;
            /// <summary>S10. The screenshot, tallied at EVERY reading in the
            /// whole suite: shut boxes with a pizza off home or on the lid.
            /// </summary>
            public int pizzaOnShutBox;
            /// <summary>Cases 7-9 ran to the end.</summary>
            public bool extended;

            public bool built;
            public int boxes;
            public string detail;
        }

        const float Dt = 0.02f;

        /// <summary>The tumble's attitude: a box on its lid. Not "past
        /// LidOpenTiltDeg" — see case 9b for why a box turned to 110 keeps
        /// its pizza, and PizzaCargo.PizzaOffFloor for the reading.</summary>
        const float OnItsLidDeg = 180f;

        /// <summary>Fraction of the last shot that was fully transparent. The
        /// owner asked for no black and no void around the cargo, and "the
        /// background is clear" is a property of the PIXELS, not of a colour
        /// setting that might quietly not apply.</summary>
        public static float LastClearFraction { get; private set; }

        public static Reading Run(bool shoot)
        {
            var r = new Reading();
            var prevMode = Physics.simulationMode;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      "Screenshots", "PizzaCargo");
            if (shoot) Directory.CreateDirectory(dir);
            Lighting();

            try
            {
                Physics.simulationMode = SimulationMode.Script;
                // Three boxes: the full order, and the case the owner asked
                // about — stacked, moving independently, top one most exposed.
                // Two bottles: one lying, one standing, so the harness covers
                // both behaviours in every case it shoots.
                var cargo = PizzaCargo.Spawn(null, new[] { 0, 3, 6 }, 2);
                if (cargo == null || cargo.BoxCount == 0)
                {
                    Debug.LogError("[PizzaSim] the cargo did not build — no baked prefabs?");
                    return r;
                }
                r.built = true;
                r.boxes = cargo.BoxCount;

                // 1. AT REST. A parked car must not damage its own cargo, and if
                //    the stack is going to explode from bad geometry it does it
                //    here, in the first ten frames.
                Step(cargo, Vector3.zero, Quaternion.identity, 100);
                r.atRest = cargo.Condition;
                // S10, tallied at every reading: a pizza on a shut box.
                r.pizzaOnShutBox += cargo.ShutBoxesWithPizzaOut();
                if (shoot) Shoot(cargo, dir, "sim_1_rest");
                Debug.Log("[PizzaSim] at rest  " + r.atRest.ToString("0.00") + "  " + cargo.Describe());

                // 1s. THE HANDBRAKE 180, FIRST — from a settled, centred load.
                //
                // It was run after the corners, and it passed for the wrong
                // reason: BoxSlide is the offset from where the box was PUT, and
                // the rough corner had already pushed it 11 cm into the bolster,
                // so a spin that moved nothing still reported 11 cm. Measured
                // here as the DELTA across the spin alone, on a box that is
                // still in the middle of the seat with somewhere to go. See
                // Spin for what the seat feels.
                float beforeSpin = cargo.BoxSlideMax();
                Spin(cargo, 60);
                Step(cargo, Vector3.zero, Quaternion.identity, 60);
                r.afterSpin = cargo.Condition;
                r.pizzaOnShutBox += cargo.ShutBoxesWithPizzaOut();
                // The MOST-MOVED box, not the bottom one — see BoxSlideMax.
                r.bottomSlideSpin = Mathf.Max(0f, cargo.BoxSlideMax() - beforeSpin);
                if (shoot) Shoot(cargo, dir, "sim_1s_spin");
                Debug.Log("[PizzaSim] spin     " + r.afterSpin.ToString("0.00") +
                          " (bottom box moved " + r.bottomSlideSpin.ToString("0.00") + " m)  " +
                          cargo.Describe());

                // 2. A HARD LEFT. 0.9 g of lateral for two and a half seconds,
                //    with the eight degrees of roll a car actually takes. The
                //    boxes should walk across the seat and lean on the bolster.
                Step(cargo, new Vector3(8.8f, 0f, 0f), Quaternion.Euler(0f, 0f, -8f), 125);
                r.afterCorner = cargo.Condition;
                r.pizzaOnShutBox += cargo.ShutBoxesWithPizzaOut();
                if (shoot) Shoot(cargo, dir, "sim_2_corner");
                Debug.Log("[PizzaSim] corner   " + r.afterCorner.ToString("0.00") + "  " + cargo.Describe());

                // 3. THE SAME CORNER ON A REAL ROAD.
                //
                // This case exists because the smooth one above PASSED and the
                // game still threw the top box off the stack on the first corner
                // of a clean lap on the parkway. The difference is the signal: a
                // real car's per-frame velocity difference carries the suspension
                // working, every kerb and the solver's own contact impulses, and
                // feeding a clean step is testing a car that does not exist.
                //
                // So: the same 0.9 g, plus several g of per-frame noise and a
                // couple of degrees of body shake. Seeded, so a regression here
                // is reproducible rather than a bad afternoon.
                //
                // The noise is a MODEL, not a number picked to make a point. Road
                // input is not white — a car's body has mass and springs, so it
                // moves in tenths of a second, not in single frames. So: a
                // band-limited wander of about 1.2 g on each axis (a new target
                // every three frames, interpolated), plus six discrete kerb
                // strikes of 4 g. Seeded, so a regression here is reproducible
                // rather than a bad afternoon.
                //
                // The first version of this was 3 g of WHITE noise on all three
                // axes at fifty hertz, which is a car being shaken like a paint
                // tin, and it is worth saying why that was wrong: white noise at
                // frame rate is the one thing the filter is specifically there to
                // remove, so the test was measuring the filter rather than the
                // cargo.
                // The road itself is Rough(), so the shut-box run (case 7)
                // drives the same surface with the same seed.
                Rough(cargo);
                Step(cargo, Vector3.zero, Quaternion.identity, 40);
                r.afterRough = cargo.Condition;
                r.pizzaOnShutBox += cargo.ShutBoxesWithPizzaOut();
                // The most-moved box: in a stack it is never the bottom one.
                r.bottomSlideRough = cargo.BoxSlideMax();

                if (shoot) Shoot(cargo, dir, "sim_3_rough");
                Debug.Log("[PizzaSim] rough    " + r.afterRough.ToString("0.00") + "  " + cargo.Describe());

                // 3a. A FIRM STOP. 0.7 g for a second — the hardest brake most
                //     driving ever asks for, and the one that must cost nothing
                //     on any seat. The pan's tilt is what makes this free while
                //     a hard corner is not: sin(12 deg) of a g holds the box
                //     back against the squab, and that margin only exists
                //     forward.
                float beforeFirm = cargo.Condition;
                Step(cargo, new Vector3(0f, 0f, -0.7f * 9.81f), Quaternion.Euler(-3f, 0f, 0f), 50);
                Step(cargo, Vector3.zero, Quaternion.identity, 40);
                r.afterFirmStop = cargo.Condition;
                r.firmStopCost = beforeFirm - r.afterFirmStop;
                r.pizzaOnShutBox += cargo.ShutBoxesWithPizzaOut();
                Debug.Log("[PizzaSim] firm     " + r.afterFirmStop.ToString("0.00") + "  " + cargo.Describe());

                // 3b. A HEAVY STOP. 1.0 g on the brakes for a second and a half,
                //     with the nose-down attitude that comes with it. This is
                //     the hardest thing a driver can do to the load without
                //     hitting anything, and with no lip across the front of the
                //     seat it is friction alone that decides. If an emergency
                //     stop puts the order in the footwell, the job is unplayable.
                Step(cargo, new Vector3(0f, 0f, -9.81f), Quaternion.Euler(-4f, 0f, 0f), 75);
                Step(cargo, Vector3.zero, Quaternion.identity, 60);
                r.afterBraking = cargo.Condition;
                r.pizzaOnShutBox += cargo.ShutBoxesWithPizzaOut();
                if (shoot) Shoot(cargo, dir, "sim_3b_braking");

                Debug.Log("[PizzaSim] braking  " + r.afterBraking.ToString("0.00") + "  " + cargo.Describe());

                // 3c. A LIGHT KNOCK. 3 m/s of closing speed — a kerb, or a
                //     barrier brushed on the way past. The jolt channel is
                //     proportional to what the car actually lost, so this is
                //     where that is checked: with nothing across the front of
                //     the seat, a scrape must jostle the load and must not tip
                //     it into the footwell.
                float beforeKnock = cargo.Condition;
                cargo.Tick(Vector3.zero, Quaternion.identity, Dt, new Vector3(0f, 0f, -3f));
                Physics.Simulate(Dt);
                Step(cargo, Vector3.zero, Quaternion.identity, 90);
                r.afterKnock = cargo.Condition;
                r.knockCost = beforeKnock - r.afterKnock;
                r.pizzaOnShutBox += cargo.ShutBoxesWithPizzaOut();
                if (shoot) Shoot(cargo, dir, "sim_3c_knock");

                Debug.Log("[PizzaSim] knock    " + r.afterKnock.ToString("0.00") + "  " + cargo.Describe());

                // 4. INTO A WALL, AT SPEED.

                //
                // Driven through the JOLT channel, not the acceleration one, and
                // that is the whole point of this case. It used to feed
                // (0, 6, -160) as an acceleration for eight frames — which the
                // clamp turns into four and a half g of shove, because the clamp
                // is there specifically to stop a kerb reading as a crash. So
                // the harness was asking "what does a very firm push do" and
                // getting the answer "not much", while the player asking "what
                // does a wall at full speed do" got the same answer and reported
                // it as a bug.
                //
                // 22 m/s (80 km/h) into something that does not move: the car
                // loses the lot in one step and the load keeps it.
                // 22 m/s (80 km/h) into something that does not move: the car
                // LOSES that much forward speed in one step — hence the negative
                // Z — and the load keeps it, so relative to the seat the load
                // goes forward. Getting that sign backwards shoves the stack
                // into the backrest, which is 38 cm tall and holds everything,
                // and the harness reports a crash that did nothing. It did that
                // on the first run of this case.
                cargo.Tick(Vector3.zero, Quaternion.Euler(-14f, 0f, 6f), Dt,
                           new Vector3(0f, 0f, -22f));

                Physics.Simulate(Dt);
                Step(cargo, Vector3.zero, Quaternion.identity, 150);
                r.afterCrash = cargo.Condition;
                r.bottomSlideCrash = cargo.BoxSlide(0);
                r.pizzaOnShutBox += cargo.ShutBoxesWithPizzaOut();
                if (shoot) Shoot(cargo, dir, "sim_4_crash");
                Debug.Log("[PizzaSim] crash    " + r.afterCrash.ToString("0.00") + "  " + cargo.Describe());

                r.detail = cargo.Describe();

                Object.DestroyImmediate(cargo.gameObject);

                // 4b. THE SAME CRASH, SIDEWAYS.
                //
                // The passenger side into a wall at 80 km/h, on the stock
                // bench. Sideways the seat is walled on both sides on purpose,
                // so this is not asking for the load to leave it — only for
                // every box to actually GO somewhere, including the bottom
                // one, which the stock bolster used to catch at half its own
                // height while the two above rode over the ridge.
                {
                    var c = PizzaCargo.Spawn(null, new[] { 0, 3, 6 }, 0, seatStage: 0);
                    if (c != null)
                    {
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        // A wall on the car's RIGHT throws the car to its
                        // LEFT, so the velocity the car GAINS is -X and the
                        // load — which gains nothing — lurches to +X, into the
                        // door card. Same convention as the head-on above.
                        // The roll has to describe the same side: Rz(-10) tips
                        // the tray's up-vector toward +X, which is the body
                        // roll of a passenger-side hit. Getting the two out of
                        // step measures one side's slide under the other
                        // side's gravity, which is a case that never happens.
                        var roll = Quaternion.Euler(0f, 0f, -10f);
                        c.Tick(Vector3.zero, roll, Dt, new Vector3(-22f, 0f, 0f));
                        Physics.Simulate(Dt);
                        Step(c, Vector3.zero, roll, 40);
                        Step(c, Vector3.zero, Quaternion.identity, 110);
                        r.sideSlideBottom = c.BoxSlide(0);
                        r.sideSlideMax = c.BoxSlideMax();
                        r.sideSlideMin = c.BoxSlideMin();
                        Debug.Log("[PizzaSim] side     bottom " + r.sideSlideBottom.ToString("0.000") +
                                  "  min " + r.sideSlideMin.ToString("0.000") +
                                  "  max " + r.sideSlideMax.ToString("0.000"));
                        Object.DestroyImmediate(c.gameObject);
                    }
                }

                // 5. THE SEAT LADDER, one rung at a time.
                //
                // A fresh three-box cargo on each seat, put through the three
                // things a seat is sold to survive: the hard corner, the
                // handbrake 180, and the panic stop. The self-test asserts the
                // ladder is MONOTONE — every rung at least as good as the one
                // below it on every case — because that is the whole promise
                // of the parts page, and a promise about five prices is an
                // assertion or it is nothing.
                int tiers = PizzaCargo.Seats.Length;
                r.tierCorner = new float[tiers];
                r.tierSpin = new float[tiers];
                r.tierBraking = new float[tiers];
                r.tierSlideSpin = new float[tiers];
                for (int t = 0; t < tiers; t++)
                {
                    var c = PizzaCargo.Spawn(null, new[] { 0, 3, 6 }, 0, seatStage: t);
                    if (c == null) break;
                    Step(c, Vector3.zero, Quaternion.identity, 60);
                    // The spin FIRST, from a centred load, and as a delta — the
                    // same false positive the main run had: a box the corner has
                    // already parked against a bolster has nowhere to be thrown.
                    Vector3 before = c.BoxOffset(0);
                    Spin(c, 60);
                    Step(c, Vector3.zero, Quaternion.identity, 60);
                    r.tierSpin[t] = c.Condition;
                    r.tierSlideSpin[t] = (c.BoxOffset(0) - before).magnitude;
                    Step(c, new Vector3(8.8f, 0f, 0f), Quaternion.Euler(0f, 0f, -8f), 125);
                    Step(c, Vector3.zero, Quaternion.identity, 40);
                    r.tierCorner[t] = c.Condition;
                    Step(c, new Vector3(0f, 0f, -9.81f), Quaternion.Euler(-4f, 0f, 0f), 75);
                    Step(c, Vector3.zero, Quaternion.identity, 60);
                    r.tierBraking[t] = c.Condition;
                    r.pizzaOnShutBox += c.ShutBoxesWithPizzaOut();
                    if (shoot) Shoot(c, dir, "sim_5_seat" + t);
                    Debug.Log("[PizzaSim] seat " + t + " " + PizzaCargo.Seats[t].name +
                              "  corner " + r.tierCorner[t].ToString("0.00") +
                              "  spin " + r.tierSpin[t].ToString("0.00") +
                              " (slid " + r.tierSlideSpin[t].ToString("0.00") + " m)" +
                              "  brake " + r.tierBraking[t].ToString("0.00") +
                              "  " + c.Describe());
                    Object.DestroyImmediate(c.gameObject);
                }

                // 6. ONE BOX, per seat, through the spin. The owner's own case
                //    and the ladder's cleanest signal — see Reading.singleSpin.
                r.singleSpin = new float[tiers];
                r.singleSlide = new float[tiers];
                for (int t = 0; t < tiers; t++)
                {
                    var c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: t);
                    if (c == null) break;
                    Step(c, Vector3.zero, Quaternion.identity, 60);
                    Spin(c, 60);
                    Step(c, Vector3.zero, Quaternion.identity, 60);
                    r.singleSpin[t] = c.Condition;
                    r.singleSlide[t] = c.BoxSlide(0);
                    r.pizzaOnShutBox += c.ShutBoxesWithPizzaOut();
                    if (shoot) Shoot(c, dir, "sim_6_onebox_seat" + t);
                    Debug.Log("[PizzaSim] one box, seat " + t + " " + PizzaCargo.Seats[t].name +
                              "  spin " + r.singleSpin[t].ToString("0.00") +
                              " (slid " + r.singleSlide[t].ToString("0.00") + " m)  " +
                              c.Describe());
                    Object.DestroyImmediate(c.gameObject);
                }

                // ---------------------------------------------------------
                // THE OWNER'S TWO REPORTS, 2026-09-07 — see Reading. Every
                // case below is on the STOCK seat, because that is the seat
                // the reports came from.
                // ---------------------------------------------------------

                // 7. SHUT-BOX INTEGRITY. A box whose lid is on keeps its pizza
                //    exactly where it was packed, through everything the seat
                //    can be given: a side-swipe, the acceleration channel at
                //    its ceiling, a kerb, a crest, the rough road, a wall.
                //    Read on every box that is still SHUT after each — a box
                //    that opened is case 9's question, not this one. One box
                //    and three, because a stack's middle box is the one the
                //    solver chain squeezed. The pizza is a child of its box
                //    now, so these numbers are zero by construction; this run
                //    exists to keep them that way the day someone makes it a
                //    body again.
                r.shutViolations = 0;
                r.shutMaxHomeError = 0f;
                foreach (int boxes in new[] { 1, 3 })
                {
                    var order = new int[boxes];
                    for (int i = 0; i < boxes; i++) order[i] = i * 3;
                    var c = PizzaCargo.Spawn(null, order, 0, seatStage: 0);
                    if (c == null) break;
                    Step(c, Vector3.zero, Quaternion.identity, 60);
                    ReadShut(c, ref r, "rest");
                    // A side-swipe into the door card: the jolt at its
                    // ceiling, sideways, with the roll of a car being hit.
                    var roll = Quaternion.Euler(0f, 0f, -8f);
                    c.Tick(Vector3.zero, roll, Dt, new Vector3(7f, 0f, 0f));
                    Physics.Simulate(Dt);
                    Step(c, Vector3.zero, roll, 40);
                    Step(c, Vector3.zero, Quaternion.identity, 40);
                    ReadShut(c, ref r, "side jolt");
                    // The acceleration channel at ITS ceiling, sideways, for
                    // half a second — a push no corner produces.
                    Step(c, new Vector3(45f, 0f, 0f), roll, 25);
                    Step(c, Vector3.zero, Quaternion.identity, 40);
                    ReadShut(c, ref r, "4.5 g push");
                    // A kerb: the car thrown UP, the load pressed into the
                    // seat (the rough road's spike, on its own).
                    Step(c, new Vector3(0f, 40f, 0f), Quaternion.identity, 3);
                    Step(c, Vector3.zero, Quaternion.identity, 30);
                    ReadShut(c, ref r, "kerb");
                    // A crest: the car DROPS, the load thrown up into the lid
                    // — the direction the old 8.8 mm ceiling was thinnest in.
                    Step(c, new Vector3(0f, -40f, 0f), Quaternion.identity, 3);
                    Step(c, Vector3.zero, Quaternion.identity, 40);
                    ReadShut(c, ref r, "crest");
                    // The rough road, same model and seed as case 3.
                    Rough(c);
                    Step(c, Vector3.zero, Quaternion.identity, 40);
                    ReadShut(c, ref r, "rough");
                    // A wall: the jolt at its ceiling, forward, with the
                    // attitude case 4 uses.
                    c.Tick(Vector3.zero, Quaternion.Euler(-14f, 0f, 6f), Dt,
                           new Vector3(0f, 0f, -7f));
                    Physics.Simulate(Dt);
                    Step(c, Vector3.zero, Quaternion.identity, 150);
                    ReadShut(c, ref r, "wall");
                    if (shoot) Shoot(c, dir, "sim_7_shut_" + boxes + "box");
                    Object.DestroyImmediate(c.gameObject);
                }
                Debug.Log("[PizzaSim] shut-box integrity: violations " + r.shutViolations +
                          ", max home error " + r.shutMaxHomeError.ToString("0.0000") + " m");

                // 8. ONE BOX, HEAD-ON — the owner's exact configuration, which
                //    the suite had never run: the one-box cases were spin-only
                //    and the crash case was a three-box stack that bulldozed
                //    through everything. Modelled on the game's signal rather
                //    than a clean forward step: the car loses 15 m/s (54 km/h)
                //    AND is thrown 3 m/s upward off the bank, nose and roll
                //    for a few frames, then it settles. Once without a bottle
                //    and once with one parked in front of the box — the 2 kg
                //    wall that most orders carry and Tick used to leave
                //    undriven.
                for (int bottles = 0; bottles <= 1; bottles++)
                {
                    var c = PizzaCargo.Spawn(null, new[] { 4 }, bottles, seatStage: 0);
                    if (c == null) break;
                    Step(c, Vector3.zero, Quaternion.identity, 60);
                    HeadOn(c);
                    float z = c.BoxOffset(0).z;
                    if (bottles == 0)
                    {
                        r.oneBoxCrashZ = z;
                        r.oneBoxCrashLeftSeat = c.IsGrounded(0);
                        r.oneBoxCrashOpened = c.IsOpen(0);
                    }
                    else
                    {
                        r.oneBoxCrashBottleZ = z;
                        r.oneBoxCrashBottleLeftSeat = c.IsGrounded(0);
                        r.oneBoxCrashBottleOpened = c.IsOpen(0);
                    }
                    r.pizzaOnShutBox += c.ShutBoxesWithPizzaOut();
                    if (shoot) Shoot(c, dir, "sim_8_headon_" + bottles + "bottle");
                    Debug.Log("[PizzaSim] head-on, one box, " + bottles + " bottle: box went " +
                              z.ToString("0.00") + " m forward" +
                              (c.IsGrounded(0) ? ", FLOOR" : ", still on the seat") +
                              "  " + c.Describe());
                    Object.DestroyImmediate(c.gameObject);
                }

                // 8b. THE SAME STOP WITHOUT THE RESPONDER. A hit whose contact
                //     normal is within 45 degrees of vertical, or a glancing
                //     one whose speed is scrubbed off over several steps, never
                //     arms the jolt: the load gets only the filtered push,
                //     clamped at 4.5 g. That still has to move a lone box —
                //     six g on the brakes for seven frames, nose down.
                {
                    var c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: 0);
                    if (c != null)
                    {
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        Step(c, new Vector3(0f, 0f, -6f * 9.81f), Quaternion.Euler(-6f, 0f, 0f), 7);
                        Step(c, Vector3.zero, Quaternion.identity, 100);
                        r.oneBoxSixGZ = c.BoxOffset(0).z;
                        r.oneBoxSixGLeftSeat = c.IsGrounded(0);
                        r.pizzaOnShutBox += c.ShutBoxesWithPizzaOut();
                        if (shoot) Shoot(c, dir, "sim_8b_sixg");
                        Debug.Log("[PizzaSim] 6 g stop, one box: box went " +
                                  r.oneBoxSixGZ.ToString("0.00") + " m forward" +
                                  (c.IsGrounded(0) ? ", FLOOR" : ", still on the seat") +
                                  "  " + c.Describe());
                        Object.DestroyImmediate(c.gameObject);
                    }
                }

                // 9. THE TUMBLE, and the leans that are not one. The lid
                //    opens on tilt past LidOpenTiltDeg (60) and only then
                //    does the pizza become a body that can leave.
                //
                //    NOT BY ROLLING THE SEAT. This case rolled the tray to 70
                //    and asserted the box opened, and it did — but the trace
                //    showed the box flat against the bolster through the whole
                //    roll (up 0.98 to the tray: a 3 cm ridge above a
                //    floor-level centre of mass is a stop, and the door card
                //    rolls with the seat), and it "opened" only when the
                //    harness snapped the tray level in one step and the pan
                //    swept up through it, leaving it underneath, grounded, the
                //    lid five metres off. So: the box is turned over by hand
                //    (9b), the seat's roll becomes a control that must come
                //    home (9c), and the model bounds its own slew rate now
                //    (PizzaCargo.MaxTiltRateDeg).
                //
                //    9a. The friction itself, as two leans: held at 30 the box
                //    must stay put, at 40 it must slide — the bench's 0.7 lets
                //    go at 35. This is the check that caught nothing until it
                //    existed: the solver was holding a 0.7 material to 50
                //    degrees (see PizzaCargo.PatchFrictionScale).
                {
                    foreach (float roll in new[] { 30f, 40f })
                    {
                        var c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: 0);
                        if (c == null) break;
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        Lean(c, roll, 15, 40);
                        float slid = Mathf.Abs(c.BoxOffset(0).x);
                        if (roll < 35f) r.holdLeanSlideM = slid; else r.slipLeanSlideM = slid;
                        r.pizzaOnShutBox += c.ShutBoxesWithPizzaOut();
                        Debug.Log("[PizzaSim] lean to " + roll + ": slid " + slid.ToString("0.000") +
                                  " m across the seat  " + c.Describe());
                        Object.DestroyImmediate(c.gameObject);
                    }
                }
                // 9b. THE TUMBLE BY HAND. First the rule from below: turned
                //     to ten degrees short of LidOpenTiltDeg and read after
                //     ONE step — before the drop back onto the pan can slam
                //     it — the box must be shut. Then ON ITS LID, the way a
                //     box thrown off a stack lands: it must open, and the
                //     pizza must drop out of it onto the seat. On its lid and
                //     not merely past the rule: turned to 110 the pizza
                //     stayed home, held against the side wall by friction
                //     (see PizzaCargo.PizzaOffFloor), which is right and is
                //     not the question.
                {
                    float control = PizzaCargo.LidOpenTiltDeg - 10f;
                    float over = OnItsLidDeg;
                    var c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: 0);
                    if (c != null)
                    {
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        c.TurnBoxOver(0, control);
                        Step(c, Vector3.zero, Quaternion.identity, 1);
                        r.tumbleControlOpened = c.IsOpen(0);
                        Debug.Log("[PizzaSim] turned to " + control + " and read once: " +
                                  (c.IsOpen(0) ? "OPENED" : "stayed shut") +
                                  " up " + c.BoxUpness(0).ToString("0.00") + "  " + c.Describe());
                        Object.DestroyImmediate(c.gameObject);
                    }
                    c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: 0);
                    if (c != null)
                    {
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        c.TurnBoxOver(0, over);
                        // Sixty frames: the drop, and the pizza's fall out of
                        // the open side.
                        for (int f = 0; f < 60; f++)
                        {
                            c.Tick(Vector3.zero, Quaternion.identity, Dt);
                            Physics.Simulate(Dt);
                            if (shoot && f % 5 == 0) TraceLine(c, "tumble", f, pizza: true);
                        }
                        r.tumbleOpened = c.IsOpen(0);
                        r.tumbleEscaped = c.PizzaEscaped(0);
                        r.tumblePizzaLoose = c.PizzaLoose(0);
                        r.tumbleHomeError = c.PizzaHomeError(0);
                        r.tumbleOffFloor = c.PizzaOffFloor(0);
                        r.pizzaOnShutBox += c.ShutBoxesWithPizzaOut();
                        if (shoot) Shoot(c, dir, "sim_9_tumble");
                        Debug.Log("[PizzaSim] tumble to " + over + ": " + (c.IsOpen(0) ? "OPENED" : "stayed shut") +
                                  (c.PizzaEscaped(0) ? ", pizza ESCAPED" : "") +
                                  ", pizza " + r.tumbleHomeError.ToString("0.000") + " m from home" +
                                  ", " + r.tumbleOffFloor.ToString("0.000") + " m off the floor  " +
                                  c.Describe() +
                                  " pizza at " + c.PizzaLocal(0).ToString("F2") +
                                  " lid at " + c.LidLocal(0).ToString("F2") +
                                  " up " + c.BoxUpness(0).ToString("0.00"));
                        Object.DestroyImmediate(c.gameObject);
                    }
                }
                // 9c. THE ROLL, as the control it always was: to 70 over 15
                //     frames, held, and BACK over 15 — a car coming down off
                //     two wheels, not a seat teleported level. The box slides
                //     to the ridge, lies flat, rides the pan home: shut, on
                //     the pan, packed. Traced when shooting, because this is
                //     the case whose ending the numbers could not explain.
                {
                    var c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: 0);
                    if (c != null)
                    {
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        Lean(c, 70f, 15, 40, returnFrames: 15, trace: shoot ? "roll" : null);
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        r.rollOpened = c.IsOpen(0);
                        r.rollGrounded = c.IsGrounded(0);
                        r.rollY = c.BoxOffset(0).y;
                        r.rollHomeError = c.PizzaHomeError(0);
                        r.pizzaOnShutBox += c.ShutBoxesWithPizzaOut();
                        if (shoot) Shoot(c, dir, "sim_9_roll");
                        Debug.Log("[PizzaSim] roll to 70 and back: " + (c.IsOpen(0) ? "OPENED" : "stayed shut") +
                                  (c.IsGrounded(0) ? ", FLOOR" : ", on the seat") +
                                  ", y " + r.rollY.ToString("0.000") + " m" +
                                  ", pizza " + r.rollHomeError.ToString("0.0000") + " m from home  " +
                                  c.Describe() + " up " + c.BoxUpness(0).ToString("0.00"));
                        Object.DestroyImmediate(c.gameObject);
                    }
                    // 9d. Rolled to 45 and held: past the 35 degrees friction
                    //     holds on, so the box slides into the bolster, but
                    //     well short of the lid — it must stay shut with the
                    //     pizza exactly where it was packed.
                    c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: 0);
                    if (c != null)
                    {
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        Lean(c, 45f, 15, 40);
                        r.leanOpened = c.IsOpen(0);
                        r.leanHomeError = c.PizzaHomeError(0);
                        r.pizzaOnShutBox += c.ShutBoxesWithPizzaOut();
                        if (shoot) Shoot(c, dir, "sim_9_lean");
                        Debug.Log("[PizzaSim] lean to 45: " + (c.IsOpen(0) ? "OPENED" : "stayed shut") +
                                  ", pizza " + r.leanHomeError.ToString("0.0000") + " m from home  " +
                                  c.Describe());
                        Object.DestroyImmediate(c.gameObject);
                    }
                }
                r.extended = true;
                Debug.Log("[PizzaSim] pizza on a shut box, anywhere in the suite: " + r.pizzaOnShutBox);
            }
            finally
            {
                Physics.simulationMode = prevMode;
            }
            return r;
        }

        static void Step(PizzaCargo cargo, Vector3 accel, Quaternion tilt, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                cargo.Tick(accel, tilt, Dt);
                Physics.Simulate(Dt);
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// THE TRACE. A reading is one number at the end of a case, and when
        /// the number is wrong it cannot say which step went wrong. This
        /// steps the lone-box cases by hand and logs the box every step —
        /// where it is, how fast, how fast it is turning, how upright — for
        /// each variant of the kick, plus a ladder of leans for the angle
        /// friction actually lets go at, and the 6 g stop. Numbers only, no
        /// pictures: run with -nographics.
        /// </summary>
        public static void Diagnose()
        {
            var prevMode = Physics.simulationMode;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                var hit = Quaternion.Euler(-14f, 0f, 6f);
                var settle = Quaternion.Euler(-4f, 0f, 0f);
                TraceKick("A headon(0,3,-15) hit5 settle10", new Vector3(0f, 3f, -15f), hit, 5, settle, 10, 0);
                TraceKick("B jolt(0,0,-15) hit5 settle10", new Vector3(0f, 0f, -15f), hit, 5, settle, 10, 0);
                TraceKick("C headon(0,3,-15) level", new Vector3(0f, 3f, -15f), Quaternion.identity, 0, Quaternion.identity, 0, 0);
                TraceKick("D jolt(0,0,-7) hit1 (S7 wall)", new Vector3(0f, 0f, -7f), hit, 1, Quaternion.identity, 0, 0);
                TraceKick("E jolt(0,0,-7) level", new Vector3(0f, 0f, -7f), Quaternion.identity, 0, Quaternion.identity, 0, 0);
                TraceKick("F jolt(0,-3,-15) level", new Vector3(0f, -3f, -15f), Quaternion.identity, 0, Quaternion.identity, 0, 0);
                TraceKick("G headon(0,3,-15) hit5 settle10, 1 bottle", new Vector3(0f, 3f, -15f), hit, 5, settle, 10, 1);

                foreach (float roll in new[] { 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f })
                {
                    var c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: 0);
                    if (c == null) break;
                    Step(c, Vector3.zero, Quaternion.identity, 60);
                    Lean(c, roll, 15, 60);
                    Debug.Log("[PizzaDiag] lean " + roll + ": off " + c.BoxOffset(0).ToString("F3") +
                              " up " + c.BoxUpness(0).ToString("0.00") +
                              (c.IsOpen(0) ? " OPEN" : " shut") + (c.IsGrounded(0) ? " FLOOR" : ""));
                    Object.DestroyImmediate(c.gameObject);
                }

                {
                    var c = PizzaCargo.Spawn(null, new[] { 4 }, 0, seatStage: 0);
                    if (c != null)
                    {
                        Step(c, Vector3.zero, Quaternion.identity, 60);
                        var nose = Quaternion.Euler(-6f, 0f, 0f);
                        for (int f = 0; f < 40; f++)
                        {
                            c.Tick(f < 7 ? new Vector3(0f, 0f, -6f * 9.81f) : Vector3.zero,
                                   f < 7 ? nose : Quaternion.identity, Dt);
                            Physics.Simulate(Dt);
                            TraceLine(c, "H sixg", f);
                        }
                        Step(c, Vector3.zero, Quaternion.identity, 67);
                        Debug.Log("[PizzaDiag] H sixg FINAL " + c.Describe() + (c.IsGrounded(0) ? " FLOOR" : " seat"));
                        Object.DestroyImmediate(c.gameObject);
                    }
                }
            }
            finally
            {
                Physics.simulationMode = prevMode;
            }
        }

        /// <summary>
        /// THE FRICTION PROBE — PhysX alone, no seat. The trace above found a
        /// lone box holding on a 50 degree lean and letting go at 55 with a
        /// material of 0.7 (which is 35 degrees), and a 2.5 m/s slam into
        /// the pan costing it 5.7 m/s of forward speed where Coulomb allows
        /// about 1.8. This asks the solver the same question with plain
        /// cubes on a plain slope: one collider; five colliders sharing the
        /// bottom face as the real box's floor and walls do; five with the
        /// walls lifted off the base. Each is leaned in five-degree steps
        /// and slammed once on the level.
        /// </summary>
        public static void Probe()
        {
            var prevMode = Physics.simulationMode;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            var grip = new PhysicsMaterial("ProbeGrip")
            {
                staticFriction = 0.7f, dynamicFriction = 0.6f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                foreach (string variant in new[] { "single", "five-overlap", "five-raised",
                                                   "single-ccd", "five-overlap-ccd", "five-raised-ccd" })
                {
                    foreach (float roll in new[] { 30f, 35f, 40f, 45f, 50f, 55f, 60f })
                    {
                        var rot = Quaternion.Euler(0f, 0f, roll);
                        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        ground.transform.SetPositionAndRotation(Vector3.zero, rot);
                        ground.transform.localScale = new Vector3(4f, 0.1f, 4f);
                        ground.GetComponent<Collider>().sharedMaterial = grip;
                        var body = ProbeBody(variant, grip);
                        body.transform.SetPositionAndRotation(rot * new Vector3(0f, 0.05f + 0.005f, 0f), rot);
                        for (int i = 0; i < 60; i++) Physics.Simulate(Dt);
                        var local = Quaternion.Inverse(rot) * body.transform.position;
                        Debug.Log("[PizzaProbe] " + variant + " lean " + roll + ": slid " +
                                  (-local.x).ToString("0.000") + " m down the slope");
                        Object.DestroyImmediate(body);
                        Object.DestroyImmediate(ground);
                    }
                    {
                        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        ground.transform.localScale = new Vector3(4f, 0.1f, 4f);
                        ground.GetComponent<Collider>().sharedMaterial = grip;
                        var body = ProbeBody(variant, grip);
                        body.transform.position = new Vector3(0f, 0.05f + 0.005f, 0f);
                        for (int i = 0; i < 30; i++) Physics.Simulate(Dt);
                        var rb = body.GetComponent<Rigidbody>();
                        rb.AddForce(new Vector3(0f, -2.5f, 7f), ForceMode.VelocityChange);
                        Physics.Simulate(Dt);
                        var v1 = rb.linearVelocity;
                        Physics.Simulate(Dt);
                        var v2 = rb.linearVelocity;
                        Debug.Log("[PizzaProbe] " + variant + " slam (0,-2.5,7): after 1 step " +
                                  v1.ToString("F2") + ", after 2 " + v2.ToString("F2"));
                        Object.DestroyImmediate(body);
                        Object.DestroyImmediate(ground);
                    }
                }
            }
            finally
            {
                Physics.simulationMode = prevMode;
                Object.DestroyImmediate(grip);
            }
        }

        /// <summary>A 41 x 5.5 x 41 cm body of 1.2 kg with the real box's
        /// damping and centre of mass, built one of three ways.</summary>
        static GameObject ProbeBody(string variant, PhysicsMaterial grip)
        {
            const float W = 0.41f, H = 0.055f, Wall = H * 0.16f, Side = W * 0.09f;
            var go = new GameObject("Probe " + variant);
            void Box(string name, Vector3 centre, Vector3 size)
            {
                var c = new GameObject(name);
                c.transform.SetParent(go.transform, false);
                var bc = c.AddComponent<BoxCollider>();
                bc.center = centre; bc.size = size; bc.sharedMaterial = grip;
            }
            if (variant.StartsWith("single"))
                Box("Block", new Vector3(0f, H * 0.5f, 0f), new Vector3(W, H, W));
            else
            {
                Box("Floor", new Vector3(0f, Wall * 0.5f, 0f), new Vector3(W, Wall, W));
                Box("Ceiling", new Vector3(0f, H - Wall * 0.5f, 0f), new Vector3(W, Wall, W));
                bool raised = variant.StartsWith("five-raised");
                float wy0 = raised ? Wall : 0f, wy1 = raised ? H - Wall : H;
                float wh = wy1 - wy0, wc = (wy0 + wy1) * 0.5f;
                Box("WallXn", new Vector3(-W * 0.5f + Side * 0.5f, wc, 0f), new Vector3(Side, wh, W));
                Box("WallXp", new Vector3(W * 0.5f - Side * 0.5f, wc, 0f), new Vector3(Side, wh, W));
                Box("WallZn", new Vector3(0f, wc, -W * 0.5f + Side * 0.5f), new Vector3(W, wh, Side));
                Box("WallZp", new Vector3(0f, wc, W * 0.5f - Side * 0.5f), new Vector3(W, wh, Side));
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            rb.linearDamping = 0.35f;
            rb.angularDamping = 3.0f;
            rb.sleepThreshold = 0f;
            rb.centerOfMass = new Vector3(0f, H * 0.16f, 0f);
            rb.collisionDetectionMode = variant.EndsWith("-ccd")
                ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            return go;
        }

        static void TraceKick(string tag, Vector3 jolt, Quaternion hit, int hitFrames,
                              Quaternion settle, int settleFrames, int bottles)
        {
            var c = PizzaCargo.Spawn(null, new[] { 4 }, bottles, seatStage: 0);
            if (c == null) return;
            Step(c, Vector3.zero, Quaternion.identity, 60);
            c.Tick(Vector3.zero, hitFrames > 0 ? hit : Quaternion.identity, Dt, jolt);
            Physics.Simulate(Dt);
            TraceLine(c, tag, 0);
            const int Trace = 30;
            for (int f = 1; f <= Trace; f++)
            {
                var t = f < hitFrames ? hit : (f < hitFrames + settleFrames ? settle : Quaternion.identity);
                c.Tick(Vector3.zero, t, Dt);
                Physics.Simulate(Dt);
                TraceLine(c, tag, f);
            }
            Step(c, Vector3.zero, Quaternion.identity, 165 - Trace);
            Debug.Log("[PizzaDiag] " + tag + " FINAL " + c.Describe() + (c.IsGrounded(0) ? " FLOOR" : " seat"));
            Object.DestroyImmediate(c.gameObject);
        }

        static void TraceLine(PizzaCargo c, string tag, int f, bool pizza = false)
        {
            Debug.Log("[PizzaDiag] " + tag + " f=" + f.ToString("00") +
                      " off " + c.BoxOffset(0).ToString("F3") +
                      " vel " + c.BoxVelocityLocal(0).ToString("F2") +
                      " spin " + c.BoxSpinLocal(0).ToString("F1") +
                      " up " + c.BoxUpness(0).ToString("0.00") +
                      (c.IsOpen(0) ? " OPEN" : "") + (c.IsGrounded(0) ? " FLOOR" : "") +
                      (pizza ? " pizza " + c.PizzaLocal(0).ToString("F3") +
                               " home " + c.PizzaHomeError(0).ToString("0.000") +
                               " lid " + c.LidLocal(0).ToString("F2") : ""));
        }

        /// <summary>
        /// THE ROUGH ROAD — case 3's surface, in one place so case 7 drives
        /// the same one. The 0.9 g corner plus a band-limited wander of about
        /// 1.2 g on each axis (a new target every three frames, interpolated)
        /// and a 4 g kerb strike every half second, with a couple of degrees
        /// of body shake. Seeded, so a regression is reproducible rather than
        /// a bad afternoon. See case 3 for why it is not white noise.
        /// </summary>
        static void Rough(PizzaCargo cargo)
        {
            Random.InitState(20260830);
            Vector3 from = Vector3.zero, to = Vector3.zero;
            for (int i = 0; i < 150; i++)
            {
                if (i % 3 == 0)
                {
                    from = to;
                    to = new Vector3(Random.Range(-12f, 12f),
                                     Random.Range(-12f, 12f),
                                     Random.Range(-12f, 12f));
                }
                var wander = Vector3.Lerp(from, to, (i % 3) / 3f);
                // Kerbs: hard, vertical, and brief.
                if (i % 25 == 0) wander += new Vector3(0f, 40f, 0f);
                var shake = Quaternion.Euler(Random.Range(-2.5f, 2.5f), 0f,
                                             -8f + Random.Range(-2.5f, 2.5f));
                cargo.Tick(new Vector3(8.8f, 0f, 0f) + wander, shake, Dt);
                Physics.Simulate(Dt);
            }
        }

        /// <summary>
        /// A HEAD-ON, as the game delivers one. The car loses 15 m/s forward
        /// in one step and is thrown 3 m/s UP off the bank — the vertical
        /// component a real hit always carries and the old magnitude clamp
        /// let steal the forward budget (see PizzaCargo.MaxAccelVert). Nose
        /// and roll for five frames, a lesser pitch for ten as the suspension
        /// settles, then level for three seconds so the load comes to rest
        /// wherever it is going. The tilt values are case 4's, kept for
        /// comparability.
        /// </summary>
        static void HeadOn(PizzaCargo cargo)
        {
            var hit = Quaternion.Euler(-14f, 0f, 6f);
            cargo.Tick(Vector3.zero, hit, Dt, new Vector3(0f, 3f, -15f));
            Physics.Simulate(Dt);
            Step(cargo, Vector3.zero, hit, 4);
            Step(cargo, Vector3.zero, Quaternion.Euler(-4f, 0f, 0f), 10);
            Step(cargo, Vector3.zero, Quaternion.identity, 150);
        }

        /// <summary>Roll the seat to <paramref name="rollDeg"/> over
        /// <paramref name="rampFrames"/>, hold it there, and — if
        /// <paramref name="returnFrames"/> is given — bring it back to level
        /// over that many. Positive roll lifts the tunnel side, so the load
        /// slides toward the door card. No acceleration: this is tilt alone,
        /// which is the thing case 9 asks about. A caller that leaves
        /// returnFrames at zero leaves the seat rolled; it must not follow
        /// that with a level Step and call it a return — that one-frame snap
        /// is what put a box under the pan (PizzaCargo.MaxTiltRateDeg).
        /// </summary>
        static void Lean(PizzaCargo cargo, float rollDeg, int rampFrames, int holdFrames,
                         int returnFrames = 0, string trace = null)
        {
            for (int i = 1; i <= rampFrames; i++)
            {
                cargo.Tick(Vector3.zero, Quaternion.Euler(0f, 0f, rollDeg * i / rampFrames), Dt);
                Physics.Simulate(Dt);
                if (trace != null) TraceLine(cargo, trace + " ramp", i);
            }
            var held = Quaternion.Euler(0f, 0f, rollDeg);
            for (int i = 0; i < holdFrames; i++)
            {
                cargo.Tick(Vector3.zero, held, Dt);
                Physics.Simulate(Dt);
                if (trace != null && i % 2 == 0) TraceLine(cargo, trace + " hold", i);
            }
            for (int i = returnFrames - 1; i >= 0; i--)
            {
                cargo.Tick(Vector3.zero, Quaternion.Euler(0f, 0f, rollDeg * i / returnFrames), Dt);
                Physics.Simulate(Dt);
                if (trace != null) TraceLine(cargo, trace + " return", i);
            }
        }

        /// <summary>Case 7's reading: for every box still SHUT, the pizza's
        /// distance from home and whether it is out; plus the S10 tally.</summary>
        static void ReadShut(PizzaCargo c, ref Reading r, string after)
        {
            for (int i = 0; i < c.BoxCount; i++)
            {
                if (c.IsOpen(i)) continue;
                r.shutMaxHomeError = Mathf.Max(r.shutMaxHomeError, c.PizzaHomeError(i));
                if (c.PizzaEscaped(i)) r.shutViolations++;
            }
            int outOfShut = c.ShutBoxesWithPizzaOut();
            r.shutViolations += outOfShut;
            r.pizzaOnShutBox += outOfShut;
            Debug.Log("[PizzaSim] shut " + c.BoxCount + " box after " + after + ":  " + c.Describe());
        }

        /// <summary>
        /// A HANDBRAKE 180, as the seat feels it.
        ///
        /// The harness hands Tick an acceleration in the car's own axes, which
        /// in the game is assembled by FixedUpdate from two things: what the
        /// centre of mass is doing, and what the SEAT is doing on top of that
        /// because it is half a metre off the yaw axis. Both are modelled here
        /// so the case is the game's arithmetic and not a guess at it.
        ///
        /// The centre of mass: the tyres are sliding, so the car scrubs speed
        /// at about 0.8 g along its ORIGINAL heading. In the car's axes that
        /// direction rotates as the car does — it starts as braking, is pure
        /// sideways at ninety degrees, and is a shove from behind at the end.
        ///
        /// The seat: a point r from the axis of a body spinning at omega is
        /// accelerated toward the axis at omega-squared-r, and the pseudo-force
        /// is the negative — flung OUTWARD. At 3.5 rad/s and 0.43 m that is
        /// 5.3 m/s^2, more than half a g, for the whole duration. The snap in
        /// and out adds alpha-cross-r for a few frames at each end.
        ///
        /// A 180 takes about a second at that rate. The roll is the car
        /// leaning out of the spin.
        ///
        /// SPUN TOWARD THE PASSENGER, and this is not a detail. The two terms
        /// point the same way only when the seat is on the OUTSIDE of the
        /// spin: the scrub throws everything in the car outward, and the
        /// seat's own centrifugal term throws it away from the axis — which
        /// for a seat on the right is rightward whichever way the car turns.
        /// Spin right and the passenger is on the inside, the two oppose, and
        /// the seat feels 0.3 g; spin left and they add to 1.3 g. The first
        /// version of this case spun right and reported that a handbrake 180
        /// does nothing, which is true of exactly half of them. The violent
        /// half is the one a seat is sold to survive. In Unity's frame a
        /// positive yaw rate is clockwise from above — a right turn — so the
        /// rate here is negative.
        /// </summary>
        static void Spin(PizzaCargo cargo, int frames)
        {
            const float ScrubG = 0.8f, Omega = -3.5f;
            // How long the yaw takes to build, and to die. A THIRD OF A
            // SECOND, which is what a road car's tyres take to let go and to
            // bite again. The first version used a tenth, and a tenth is 35
            // rad/s^2 — alpha-cross-r on the seat was a g and a half, forward,
            // at the snap-out, and it threw two boxes off the whole island.
            // That was the harness inventing an impact, not a seat failing.
            const int RampFrames = 15;
            var r = new Vector3(0.40f, 0f, 0.15f);
            float heading = 0f;
            for (int i = 0; i < frames; i++)
            {
                float ramp = Mathf.Clamp01(Mathf.Min(i, frames - 1 - i) / (float)RampFrames);
                float w = Omega * ramp;
                float alpha = i < RampFrames ? Omega / (RampFrames * Dt)
                            : (i >= frames - RampFrames ? -Omega / (RampFrames * Dt) : 0f);

                // Scrub along the original heading, expressed in the rotating
                // car frame. World -Z is "where the car was going"; the car has
                // yawed by `heading` since.
                var scrubWorld = new Vector3(0f, 0f, -ScrubG * 9.81f);
                var scrubCar = Quaternion.Euler(0f, -heading * Mathf.Rad2Deg, 0f) * scrubWorld;

                var wv = new Vector3(0f, w, 0f);
                var av = new Vector3(0f, alpha, 0f);
                var seat = Vector3.Cross(av, r) + Vector3.Cross(wv, Vector3.Cross(wv, r));

                var tilt = Quaternion.Euler(0f, 0f, -7f * ramp);
                cargo.Tick(scrubCar + seat, tilt, Dt);
                Physics.Simulate(Dt);
                heading += w * Dt;
            }
        }

        static void Lighting()
        {
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.35f, 0.85f, -0.4f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(1f, 0.95f, 0.86f));
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.60f));
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.1f, 0.1f, 0.12f));
            Shader.SetGlobalFloat("_PSXFogNear", 60f);
            Shader.SetGlobalFloat("_PSXFogFar", 240f);
            Shader.SetGlobalFloat("_PSXSnap", 0f);
        }

        /// <summary>The Pizza Cam's own framing, at four times its resolution so
        /// the result is legible in a report.</summary>
        static void Shoot(PizzaCargo cargo, string dir, string name)
        {
            const int W = 480, H = 324;
            Vector3 origin = cargo.transform.position;
            var go = new GameObject("~simCam");
            var cam = go.AddComponent<Camera>();
            // The PLAYER'S framing, asked for rather than copied — these shots
            // are only evidence if they are the picture the player gets.
            PizzaCam.Framing(origin, out Vector3 eye, out Vector3 look, out float fov);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            // ARGB and RGBA32: the shots have to carry the alpha, or the one
            // thing being checked — that everything around the seat is CLEAR —
            // is thrown away on the way to disk. RGB24 would have written a
            // black frame and it would have looked exactly like a bug.
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            int clear = 0;
            foreach (var px in tex.GetPixels32()) if (px.a < 8) clear++;
            LastClearFraction = clear / (float)(W * H);
            Debug.Log("[PizzaSim] " + name + " is " +
                      (LastClearFraction * 100f).ToString("0") + "% transparent");
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }
}

namespace PSXRacing.EditorTools
{
    /// <summary>Entry point for the verification pass: run the simulation AND
    /// write the pictures. The self-test calls PizzaCargoSim.Run(shoot: false)
    /// instead — it wants the numbers, not three PNGs per build.</summary>
    public static class PizzaCargoSimShots
    {
        public static void Run() => PizzaCargoSim.Run(shoot: true);
    }
}
