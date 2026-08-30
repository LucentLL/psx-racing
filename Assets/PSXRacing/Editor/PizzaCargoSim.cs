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

            public float afterCrash;  // after a full-clamp frontal impact
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
                var cargo = PizzaCargo.Spawn(null, new[] { 0, 3, 6 });
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
                r.bottomSlideRough = cargo.BoxSlide(0);

                if (shoot) Shoot(cargo, dir, "sim_3_rough");
                Debug.Log("[PizzaSim] rough    " + r.afterRough.ToString("0.00") + "  " + cargo.Describe());

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
                cargo.Tick(Vector3.zero, Quaternion.identity, Dt, new Vector3(0f, 0f, -3f));
                Physics.Simulate(Dt);
                Step(cargo, Vector3.zero, Quaternion.identity, 90);
                r.afterKnock = cargo.Condition;
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
