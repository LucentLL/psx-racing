using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A CAR ON A BANK, STEPPED BY HAND (owner 2026-11-17: "I noticed that cars
    /// slide on banked roads. Even when resting in place at the start of a
    /// race. This means tires don't actually grip the roads.").
    ///
    /// The ParkedCarSim pattern (Physics.simulationMode = Script, the car's
    /// FixedUpdate called by hand, Physics.Simulate behind it) on a plane tilted
    /// ACROSS the car - a road's crossfall or superelevation - and along it - a
    /// grade - for each way a car stands still in this game: the pre-race hold
    /// as the player and as an AI, no pedals at all, the brake pedal, the lever.
    /// Each reading is how far the car moved sideways and fore-aft in the
    /// plane over <see cref="Watch"/> seconds after a settle, plus what the
    /// tyres were doing at the half-way mark.
    ///
    /// Then THE HAND-OVER: from rest on the bank, a launch on a light throttle
    /// and a crawl, traced tick by tick - sideways speed, yaw rate and the
    /// tick-to-tick change in sideways acceleration (a twitch shows there) -
    /// so static grip handing over to the slip model can be seen to do it
    /// smoothly.
    ///
    /// PSX Racing/Grip Sim (Bank Sweep), or GripSim.Shoot from -executeMethod.
    /// Writes PSXRacing_gripsim.txt in the project folder. Reads no switch
    /// itself: run it in two fresh processes with PSX_STATIC_GRIP=0 and 1.
    /// </summary>
    public static class GripSim
    {
        const float Dt = 1f / 60f;   // the game's own step (PSXBootstrap.fixedTimestep)
        const float Settle = 1.5f;
        const float Watch = 20f;

        static readonly float[] Banks = { 0.02f, 0.05f, 0.08f, 0.10f, 0.12f };
        static readonly float[] Grades = { 0f, 0.08f, 0.15f };

        struct Case
        {
            public string name;
            public float throttle, brake;
            public bool lever, reverse;
            public int gear;
        }

        static readonly Case[] Cases =
        {
            // PlayerCarInput with inputEnabled false: what the countdown holds
            // the player's car on (brake 0.3, the lever below 1 m/s).
            new Case { name = "hold (player)", brake = 0.3f, lever = true, reverse = true, gear = 1 },
            // AIDriver with driving false: brake 0.4, never reverse.
            new Case { name = "hold (AI)", brake = 0.4f, reverse = false, gear = 1 },
            // No pedal, no lever: the closest this model has to neutral (it has
            // no neutral gear; engine braking is gated off below 0.5 m/s).
            new Case { name = "no pedals", reverse = true, gear = 1 },
            new Case { name = "brake held", brake = 1f, reverse = false, gear = 1 },
            new Case { name = "parking brake", lever = true, reverse = true, gear = 1 },
        };

        public struct Reading
        {
            public string name;
            public float bank, grade;
            public float sideM, foreM;      // drift in the plane over Watch s
            public float settleM;           // drift during the settle
            public float peakSpeed;         // planar, during the watch
            public float parkedFrac;        // ticks the solver called it parked
            public float yawDeg;            // heading change over the watch
            public string wheels;           // tyre numbers at mid-watch
        }

        [MenuItem("PSX Racing/Grip Sim (Bank Sweep)")]
        public static void Shoot()
        {
            var sb = new StringBuilder();
            string sw = System.Environment.GetEnvironmentVariable("PSX_STATIC_GRIP");
            sb.AppendLine("=== GRIP SIM: a car at rest on a bank, " + Watch.ToString("0") +
                          " s after a " + Settle.ToString("0.0") + " s settle; PSX_STATIC_GRIP=" +
                          (string.IsNullOrEmpty(sw) ? "(unset)" : sw) + " ===");
            sb.AppendLine("drift in mm: SIDE = across the car, FORE = along it; peak = planar m/s in the watch; held = ticks parked");
            var prevMode = Physics.simulationMode;
            float prevDt = Time.fixedDeltaTime;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Time.fixedDeltaTime = Dt;
                foreach (float g in Grades)
                    foreach (float b in Banks)
                    {
                        sb.AppendLine(string.Format("-- bank {0:0}% grade {1:0}%", b * 100f, g * 100f));
                        foreach (var c in Cases)
                        {
                            var r = RunCase(c, b, g, wantWheels: Mathf.Approximately(b, 0.10f) && g == 0f);
                            sb.AppendLine(string.Format(
                                "  {0,-15} SIDE {1,8:0.0}  FORE {2,8:0.0}  YAW {6,6:+0.00;-0.00}  settle {3,6:0.0}  peak {4:0.000}  held {5,4:0}%",
                                r.name, r.sideM * 1000f, r.foreM * 1000f, r.settleM * 1000f, r.peakSpeed,
                                r.parkedFrac * 100f, r.yawDeg));
                            if (!string.IsNullOrEmpty(r.wheels)) sb.Append(r.wheels);
                        }
                    }
                sb.AppendLine();
                sb.AppendLine("=== HAND-OVER: from rest (lever, 3 s) on a 10% bank, then the inputs below ===");
                foreach (var l in new[] { 0.15f, 0.35f, 1.0f })
                    sb.Append(Launch(0.10f, 0f, l, 0f));
                sb.Append(Launch(0.10f, 0f, 0.35f, 0.5f));
                sb.Append(Launch(0.12f, 0.08f, 0.35f, 0f));
                sb.Append(Crawl(0.10f));
                sb.AppendLine();
                sb.AppendLine("=== IN MOTION: 20 m/s straight, no steering, across a crossfall (the pull) ===");
                foreach (var c in new[] { 0f, 0.025f, 0.08f }) sb.Append(Straight(c));
                sb.AppendLine("=== IN MOTION: a 40 m ring, speed raised until the car runs wide (the bank's help) ===");
                foreach (var b in new[] { 0f, 0.08f }) sb.Append(Ring(b));
            }
            finally
            {
                foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (go != null && (go.name == "GripSimPlane" || go.name == "GripSimCar")) Object.DestroyImmediate(go);
                Physics.simulationMode = prevMode;
                Time.fixedDeltaTime = prevDt;
            }
            string tag = System.Environment.GetEnvironmentVariable("PSX_GRIP_TAG");
            string file = "PSXRacing_gripsim" + (string.IsNullOrEmpty(tag) ? "" : "_" + tag) + ".txt";
            File.WriteAllText(file, sb.ToString());
            Debug.Log(sb.ToString());
        }

        /// <summary>The plane: crossfall <paramref name="bank"/> (the car's
        /// RIGHT side up, so gravity pulls it left) and grade
        /// <paramref name="grade"/> (nose up). Returned with its rotation so
        /// the car can stand in it.</summary>
        static Quaternion Plane(float bank, float grade, out GameObject go)
        {
            var rot = Quaternion.Euler(-Mathf.Atan(grade) * Mathf.Rad2Deg, 0f, Mathf.Atan(bank) * Mathf.Rad2Deg);
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GripSimPlane";
            go.transform.rotation = rot;
            go.transform.position = rot * new Vector3(0f, -0.5f, 0f);
            go.transform.localScale = new Vector3(400f, 1f, 400f);
            go.layer = 0;
            return rot;
        }

        static readonly MethodInfo Fixed = typeof(CarController).GetMethod("FixedUpdate",
            BindingFlags.NonPublic | BindingFlags.Instance);

        static CarController MakeCar(Quaternion rot)
        {
            var go = new GameObject("GripSimCar") { hideFlags = HideFlags.HideAndDontSave };
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1.72f, 1.0f, 4.1f);
            box.center = new Vector3(0f, 0.6f, 0f);
            go.layer = 2;
            go.AddComponent<Rigidbody>();
            var car = go.AddComponent<CarController>();
            typeof(CarController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(car, null);
            // FRONT-HEAVY, like the game's cars (the RX-7 on the Lake Lure grid
            // stood 65:35 on its springs). A 50:50 test car hides anything that
            // turns a car about its centre: the even split of a load over four
            // tyres is only couple-free when the centre is mid-wheelbase.
            car.weightDistFront = 0.6f;
            if (car.Body != null) car.Body.centerOfMass = car.CenterOfMassLocal;
            go.transform.SetPositionAndRotation(rot * new Vector3(0f, 0.8f, 0f), rot);
            return car;
        }

        static void Step(CarController car)
        {
            Fixed?.Invoke(car, null);
            Physics.Simulate(Dt);
        }

        static void Apply(CarController car, in Case c)
        {
            car.throttleInput = c.throttle;
            car.brakeInput = c.brake;
            car.handbrakeInput = c.lever;
            car.allowReverse = c.reverse;
            if (car.currentGear != -1) car.currentGear = Mathf.Max(1, c.gear);
        }

        static Reading RunCase(Case c, float bank, float grade, bool wantWheels)
        {
            var rot = Plane(bank, grade, out var plane);
            var car = MakeCar(rot);
            var r = new Reading { name = c.name, bank = bank, grade = grade };
            try
            {
                Vector3 p0 = car.transform.position;
                bool touched = false;
                int n = Mathf.RoundToInt(Settle / Dt);
                for (int i = 0; i < n; i++)
                {
                    Apply(car, c); Step(car);
                    // The settle counts from the first tick all four tyres
                    // touch: before that the car is falling, and a straight
                    // drop onto a slope moves it along the slope's plane.
                    if (!touched)
                    {
                        bool all = true;
                        for (int w = 0; w < 4; w++) all &= car.wheelContacts[w].grounded;
                        if (all) { touched = true; p0 = car.transform.position; }
                    }
                }
                Vector3 p1 = car.transform.position;
                r.settleM = Vector3.ProjectOnPlane(p1 - p0, rot * Vector3.up).magnitude;
                Vector3 right = rot * Vector3.right, fwd = rot * Vector3.forward, up = rot * Vector3.up;
                n = Mathf.RoundToInt(Watch / Dt);
                int parked = 0;
                for (int i = 0; i < n; i++)
                {
                    Apply(car, c);
                    Step(car);
                    if (car.Parked) parked++;
                    float sp = Vector3.ProjectOnPlane(car.Body.linearVelocity, up).magnitude;
                    if (sp > r.peakSpeed) r.peakSpeed = sp;
                    if (wantWheels && i == n / 2) r.wheels = Wheels(car, up);
                }
                Vector3 d = car.transform.position - p1;
                r.yawDeg = Vector3.SignedAngle(fwd, Vector3.ProjectOnPlane(car.transform.forward, up), up);
                r.sideM = Vector3.Dot(d, right);
                r.foreM = Vector3.Dot(d, fwd);
                r.parkedFrac = parked / (float)n;
            }
            finally
            {
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(plane);
            }
            return r;
        }

        /// <summary>What each tyre was doing: its load, its grip circle, the
        /// lateral force it gave and at what slip angle and contact speed; and
        /// the sum against what gravity asks of the four across the car.</summary>
        static string Wheels(CarController car, Vector3 up)
        {
            var sb = new StringBuilder();
            float need = -Vector3.Dot(Physics.gravity, car.transform.right) * car.massKg;
            float got = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = car.wheelContacts[i];
                if (!w.grounded) { sb.AppendLine("      wheel " + i + " in the air"); continue; }
                float cv = car.Body.GetPointVelocity(w.point).magnitude;
                got += w.latForce;
                sb.AppendLine(string.Format(
                    "      wheel {0}: Fz {1,6:0} N  circle {2,6:0} N  lat {3,7:0.0} N  slip {4,7:0.000} deg  contact {5:0.0000} m/s",
                    i, w.load, w.gripCircle, w.latForce, w.slipAngle * Mathf.Rad2Deg, cv));
            }
            sb.AppendLine(string.Format("      across the car: gravity asks {0:0} N, the tyres give {1:0} N ({2:0}%)",
                need, -got, need != 0f ? -got / need * 100f : 0f));
            return sb.ToString();
        }

        /// <summary>From rest on the bank (lever, then released) to a launch:
        /// sideways speed, yaw rate and the tick-to-tick change in sideways
        /// acceleration over the first seconds.</summary>
        static string Launch(float bank, float grade, float throttle, float steer)
        {
            var rot = Plane(bank, grade, out var plane);
            var car = MakeCar(rot);
            var sb = new StringBuilder();
            try
            {
                var hold = new Case { name = "lever", lever = true, reverse = true, gear = 1 };
                for (int i = 0; i < Mathf.RoundToInt(3f / Dt); i++) { Apply(car, hold); Step(car); }
                Vector3 up = rot * Vector3.up;
                float prevLatV = Vector3.Dot(car.Body.linearVelocity, car.transform.right), prevA = 0f;
                float maxLatV = 0f, maxYaw = 0f, maxJerk = 0f, sideAt2 = 0f;
                int flips = 0; float lastSign = 0f;
                Vector3 start = car.transform.position;
                var trace = new StringBuilder();
                int n = Mathf.RoundToInt(4f / Dt);
                for (int i = 0; i < n; i++)
                {
                    car.throttleInput = throttle; car.brakeInput = 0f; car.handbrakeInput = false; car.steerInput = steer;
                    Step(car);
                    float latV = Vector3.Dot(car.Body.linearVelocity, car.transform.right);
                    float a = (latV - prevLatV) / Dt;
                    float jerk = i > 0 ? Mathf.Abs(a - prevA) / Dt : 0f;
                    float yaw = Vector3.Dot(car.Body.angularVelocity, car.transform.up);
                    if (steer == 0f)
                    {
                        maxLatV = Mathf.Max(maxLatV, Mathf.Abs(latV));
                        maxYaw = Mathf.Max(maxYaw, Mathf.Abs(yaw));
                    }
                    if (i > 0) maxJerk = Mathf.Max(maxJerk, jerk);
                    if (Mathf.Abs(latV) > 0.002f)
                    {
                        float s = Mathf.Sign(latV);
                        if (lastSign != 0f && s != lastSign) flips++;
                        lastSign = s;
                    }
                    if (i == Mathf.RoundToInt(2f / Dt) - 1)
                        sideAt2 = Vector3.Dot(car.transform.position - start, rot * Vector3.right);
                    if (i % 5 == 4 && i < 100)
                        trace.Append(string.Format(" {0:0.0}s:{1:0.0}/{2:+0.000;-0.000}/{3:+0.00;-0.00}",
                            (i + 1) * Dt, car.forwardSpeed, latV, yaw));
                    prevLatV = latV; prevA = a;
                }
                sb.AppendLine(string.Format(
                    "  bank {0:0}% grade {1:0}% throttle {2:0.00} steer {3:0.0}: side drift by 2 s {4:+0.000;-0.000} m, " +
                    "max |vLat| {5:0.000} m/s, max |yaw| {6:0.000} rad/s, max lateral jerk {7:0} m/s^3, vLat sign flips {8}",
                    bank * 100f, grade * 100f, throttle, steer, sideAt2, maxLatV, maxYaw, maxJerk, flips));
                sb.AppendLine("     trace t:fwd/vLat/yaw" + trace);
            }
            finally
            {
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(plane);
            }
            return sb.ToString();
        }

        /// <summary>20 m/s along a road with a crossfall (right side up), the
        /// wheel dead straight, the throttle holding the speed: how far the car
        /// wanders across in 6 s and how far its heading turns.</summary>
        static string Straight(float cross)
        {
            var rot = Plane(cross, 0f, out var plane);
            var car = MakeCar(rot);
            try
            {
                for (int i = 0; i < Mathf.RoundToInt(1f / Dt); i++)
                { car.throttleInput = 0f; car.brakeInput = 0f; car.handbrakeInput = true; Step(car); }
                car.handbrakeInput = false;
                car.SetRolling(20f);
                Vector3 start = car.transform.position;
                Vector3 right0 = car.transform.right, fwd0 = car.transform.forward;
                for (int i = 0; i < Mathf.RoundToInt(6f / Dt); i++)
                {
                    float v = car.forwardSpeed;
                    car.steerInput = 0f;
                    car.throttleInput = Mathf.Clamp01((20f - v) * 0.4f + 0.15f);
                    car.brakeInput = 0f;
                    Step(car);
                }
                Vector3 d = car.transform.position - start;
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(fwd0, rot * Vector3.up),
                                                Vector3.ProjectOnPlane(car.transform.forward, rot * Vector3.up), rot * Vector3.up);
                return string.Format("  crossfall {0:0.0}%: went {1:0.0} m, across {2:+0.000;-0.000} m (+ = uphill), heading {3:+0.00;-0.00} deg, body slip {4:+0.00;-0.00} deg\n",
                    cross * 100f, Vector3.Dot(d, fwd0), Vector3.Dot(d, right0), yaw, car.chassisSlipAngle * Mathf.Rad2Deg);
            }
            finally
            {
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(plane);
            }
        }

        /// <summary>A ring of radius 40 m banked <paramref name="bank"/> toward
        /// its centre (72 boxes). The car goes round it anticlockwise on a
        /// steering controller holding the radius, the speed raised 0.25 m/s
        /// a second from 12 m/s, until it runs 2 m wide: that speed, and the
        /// lateral acceleration it implies, measure the grip the bank adds.</summary>
        static string Ring(float bank)
        {
            const float R = 40f;
            var parts = new List<GameObject>();
            var sb = new StringBuilder();
            CarController car = null;
            try
            {
                for (int k = 0; k < 72; k++)
                {
                    float th = k * Mathf.PI * 2f / 72f;
                    var radial = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                    var tan = new Vector3(-Mathf.Sin(th), 0f, Mathf.Cos(th));
                    var n = (Vector3.up - radial * bank).normalized;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "GripSimPlane";
                    go.transform.rotation = Quaternion.LookRotation(tan, n);
                    go.transform.position = radial * R - n * 0.5f;
                    go.transform.localScale = new Vector3(14f, 1f, 3.7f);
                    go.layer = 0;
                    parts.Add(go);
                }
                var n0 = (Vector3.up - Vector3.right * bank).normalized;
                var rot0 = Quaternion.LookRotation(Vector3.forward, n0);
                car = MakeCar(rot0);
                car.transform.position = new Vector3(R, 0f, 0f) + n0 * 0.8f;
                for (int i = 0; i < Mathf.RoundToInt(1f / Dt); i++)
                { car.handbrakeInput = true; Step(car); }
                car.handbrakeInput = false;
                car.SetRolling(12f);
                float target = 12f, prevR = R, wideAt = -1f, maxLatG = 0f, latG = 0f;
                Vector3 prevV = car.Body.linearVelocity;
                for (int i = 0; i < Mathf.RoundToInt(70f / Dt); i++)
                {
                    Vector3 p = car.transform.position; p.y = 0f;
                    float r = p.magnitude;
                    float rdot = (r - prevR) / Dt; prevR = r;
                    float v = car.forwardSpeed;
                    car.steerInput = Mathf.Clamp(-0.12f - 0.35f * (r - R) - 0.6f * rdot, -1f, 1f);
                    car.throttleInput = Mathf.Clamp01((target - v) * 0.6f + 0.2f);
                    car.brakeInput = v > target + 1f ? 0.3f : 0f;
                    Step(car);
                    Vector3 vv = car.Body.linearVelocity;
                    float a = Vector3.Dot((vv - prevV) / Dt, -p.normalized);
                    prevV = vv;
                    latG = Mathf.Lerp(latG, a / 9.81f, Dt / 0.3f);
                    if (i * Dt > 5f && Mathf.Abs(r - R) < 1.0f) maxLatG = Mathf.Max(maxLatG, latG);
                    target += 0.25f * Dt;
                    if (i * Dt > 5f && r - R > 2f) { wideAt = v; break; }
                }
                sb.AppendLine(string.Format("  bank {0:0}%: ran 2 m wide at {1:0.00} m/s ({2:0.00} g by v^2/R), peak lateral {3:0.00} g held on line",
                    bank * 100f, wideAt, wideAt > 0f ? wideAt * wideAt / R / 9.81f : 0f, maxLatG));
            }
            finally
            {
                if (car != null) Object.DestroyImmediate(car.gameObject);
                foreach (var go in parts) Object.DestroyImmediate(go);
            }
            return sb.ToString();
        }

        /// <summary>A CRAWL: the brake let off on a grade (no throttle) so the
        /// car rolls very slowly across the static/slip boundary - the speed
        /// band a hand-over lives in. 10% bank and an 8% grade nose-DOWN.</summary>
        static string Crawl(float bank)
        {
            var rot = Plane(bank, -0.08f, out var plane);
            var car = MakeCar(rot);
            var sb = new StringBuilder();
            try
            {
                var hold = new Case { name = "lever", lever = true, reverse = true, gear = 1 };
                for (int i = 0; i < Mathf.RoundToInt(3f / Dt); i++) { Apply(car, hold); Step(car); }
                Vector3 start = car.transform.position;
                float maxJerk = 0f, prevLatV = 0f, prevA = 0f;
                var trace = new StringBuilder();
                // A light brake drag that lets it roll: 0.08 of pedal, no lever.
                int n = Mathf.RoundToInt(6f / Dt);
                for (int i = 0; i < n; i++)
                {
                    car.throttleInput = 0.03f; car.brakeInput = 0f; car.handbrakeInput = false; car.steerInput = 0f;
                    car.allowReverse = false;
                    Step(car);
                    float latV = Vector3.Dot(car.Body.linearVelocity, car.transform.right);
                    float a = (latV - prevLatV) / Dt;
                    if (i > 1) maxJerk = Mathf.Max(maxJerk, Mathf.Abs(a - prevA) / Dt);
                    prevLatV = latV; prevA = a;
                    if (i % 25 == 24)
                        trace.Append(string.Format(" {0:0.0}s:{1:0.000}/{2:+0.0000;-0.0000}", (i + 1) * Dt, car.forwardSpeed, latV));
                }
                Vector3 d = car.transform.position - start;
                sb.AppendLine(string.Format(
                    "  CRAWL bank {0:0}% grade -8%: rolled {1:0.00} m along, {2:+0.000;-0.000} m across in 6 s, max lateral jerk {3:0} m/s^3",
                    bank * 100f, Vector3.Dot(d, rot * Vector3.forward), Vector3.Dot(d, rot * Vector3.right), maxJerk));
                sb.AppendLine("     trace t:fwd/vLat" + trace);
            }
            finally
            {
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(plane);
            }
            return sb.ToString();
        }
    }
}
