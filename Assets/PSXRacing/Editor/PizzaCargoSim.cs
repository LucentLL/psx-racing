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

            public bool built;
            public int boxes;
            public string detail;
        }

        const float Dt = 0.02f;

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
                Step(cargo, Vector3.zero, Quaternion.identity, 40);
                r.afterRough = cargo.Condition;
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
                if (shoot) Shoot(cargo, dir, "sim_4_crash");
                Debug.Log("[PizzaSim] crash    " + r.afterCrash.ToString("0.00") + "  " + cargo.Describe());

                r.detail = cargo.Describe();

                Object.DestroyImmediate(cargo.gameObject);

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
                    if (shoot) Shoot(c, dir, "sim_6_onebox_seat" + t);
                    Debug.Log("[PizzaSim] one box, seat " + t + " " + PizzaCargo.Seats[t].name +
                              "  spin " + r.singleSpin[t].ToString("0.00") +
                              " (slid " + r.singleSlide[t].ToString("0.00") + " m)  " +
                              c.Describe());
                    Object.DestroyImmediate(c.gameObject);
                }
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
