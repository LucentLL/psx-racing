using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Render-only body roll, dive and squat on the car's SHELL.
    ///
    /// Black Box did this as a separate layer from the physics — MW05's ecar
    /// carries BodyRoll / BodyDive / BodySquat as "degrees per g, max g,
    /// degrees per second" triplets scaled by chassis.RENDER_MOTION — and it
    /// is the right split here too. The Rigidbody already rolls on its springs
    /// and anti-roll bars; that motion is small by design (a rigid PS1 shell on
    /// a 240-line screen barely shows two degrees of it) and it is PHYSICS, so
    /// exaggerating it would change where the weight goes. This leans the mesh
    /// child and nothing else: the collider, the wheels and the solver never
    /// see it, and the wheels stay planted while the body moves over them —
    /// which is what makes a drift READ on a phone.
    ///
    /// Composed on top of the model's own yaw and offset from CarBody, so a
    /// pack whose shells face -Z still leans about the CAR'S axes.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CarBodyLean : MonoBehaviour
    {
        public CarController car;
        public CarBody body;
        /// <summary>The shell's transform. Taken from <see cref="CarBody"/>
        /// when not wired, because that is the one component that knows which
        /// child is the body and which are the wheels.</summary>
        public Transform bodyRoot;

        // ---- MW-shaped constants (deg/g, cap in g, rate in deg/s) -----------
        // Modest on purpose: the springs already give real roll, so this is
        // the visible EXCESS. On a 1.72 m wide shell the 1.69 deg maximum here
        // (1.3 deg/g capped at 1.3 g) drops the outside sill about 2.5 cm —
        // enough to read from a chase camera, not enough to plant the sill in
        // the road. It was 1.8 x 1.5 = 2.7 deg, and 2.7 was measured against a
        // lean that ALSO pinned to its clamp for the whole of every slide,
        // because the g it read was v x yawRate rather than an accelerometer.
        // With the measure honest the same number read as too much.
        public const float RollDegPerG = 1.3f;
        public const float RollMaxG = 1.3f;
        public const float RollRateDegPerSec = 25f;
        public const float PitchDegPerG = 1.2f;
        public const float PitchMaxG = 1.0f;
        public const float PitchRateDegPerSec = 20f;
        /// <summary>The RENDER_MOTION multiplier. 1 = the constants as stated.</summary>
        public const float RenderMotion = 1f;
        /// <summary>Longitudinal g is DIFFERENCED off forward speed once a
        /// frame, and physics runs at 60 Hz under a render rate that may not
        /// be: the raw difference is zero on the frames between ticks and
        /// double on the frames after. Smoothed over this many seconds it
        /// averages to the true acceleration.</summary>
        const float LongGTau = 0.15f;
        /// <summary>The same filter on the LATERAL channel, which had none.
        /// Longer, because the lateral read is now a difference of the whole
        /// velocity vector and so carries every kerb and every wall.</summary>
        const float LatGTau = 0.20f;
        /// <summary>Seconds the lean takes to settle onto its target, on top of
        /// the degrees-per-second slew. MoveTowards alone is a linear ramp that
        /// arrives at the cap and stops dead; a first-order settle underneath it
        /// is what makes the shell ease in and ease out.</summary>
        const float RollTau = 0.30f;
        const float PitchTau = 0.25f;
        /// <summary>Above this many g the reading is a teleport or a wall, not
        /// the road — see the drop in LateUpdate. Well clear of the ~1.3 g the
        /// tyres can actually make and of the 1.3 g clamp on the lean itself.
        /// </summary>
        const float DiscontinuityG = 4f;

        /// <summary>Roll for a signed lateral g (positive = turning LEFT).
        /// The body rolls OUT of the turn — a left turn drops the right
        /// sill, which is a negative rotation about the car's forward axis.</summary>
        public static float RollDegFor(float latG) =>
            -Mathf.Clamp(latG, -RollMaxG, RollMaxG) * RollDegPerG * RenderMotion;

        /// <summary>Pitch for a signed longitudinal g (positive = accelerating).
        /// Braking dives the nose (positive rotation about the car's right
        /// axis), throttle squats it.</summary>
        public static float PitchDegFor(float longG) =>
            -Mathf.Clamp(longG, -PitchMaxG, PitchMaxG) * PitchDegPerG * RenderMotion;

        float roll, pitch;
        float longG, latG;
        float prevForward;
        Vector3 prevVel;
        bool havePrev;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (body == null) body = GetComponent<CarBody>();
            if (bodyRoot == null && body != null) bodyRoot = body.bodyRoot;
        }

        void LateUpdate() => Pose(Time.deltaTime);

        /// <summary>Back to the plain seat while paused (a replay drives the
        /// lean itself and must not inherit a frame's ride-up).</summary>
        void OnDisable()
        {
            if (bodyRoot != null && body != null && body.Def != null && CarController.BumpStopsEnabled)
                bodyRoot.localPosition = body.SeatPosition;
        }

        /// <summary>One frame of the shell's pose: the lean, then the seat on
        /// its tyres. Public so a harness stepping the physics by hand can
        /// pose the shell the way a frame would.</summary>
        public void Pose(float dt)
        {
            if (bodyRoot == null || car == null || car.Body == null) return;
            if (dt <= 0f) return;

            // AN ACCELEROMETER, not a kinematic estimate.
            //
            // This was v * yawRate, the centripetal acceleration of a car
            // following its nose. That is right in a steady grip corner and
            // badly wrong in a slide, where the yaw rate is decoupled from the
            // path: 25 m/s at 1.5 rad/s reports 3.8 g while the tyres are
            // making 1.3, so the lean pinned to its clamp and STAYED there for
            // the whole slide. And because yaw rate is mostly what steering
            // produces, that made it a steering-driven lean — the classic
            // arcade tell, and half of "the car roll is too aggressive".
            //
            // Differencing the velocity cannot report more than the tyres
            // actually delivered. Same sign as before in a steady turn, so the
            // shell still leans the way it always did; only the magnitude in a
            // slide changes, and only downward.
            Vector3 vel = car.Body.linearVelocity;
            float fwdV = Vector3.Dot(vel, transform.forward);

            // DIVIDED BY THE INTERVAL THE VELOCITY ACTUALLY CHANGED OVER, not
            // by the render frame. A Rigidbody's velocity only moves on a
            // physics step, so on a display faster than 60 Hz the difference
            // spans at most one step of fixedDeltaTime however short the frame
            // was — dividing by the frame instead reports an ordinary 1.3 g
            // corner as 5 g at 240 Hz, which the discontinuity test below would
            // then throw away as a collision. Slower than 60 Hz the frame IS
            // the interval and dt is right. Max of the two is correct in both.
            float aDt = Mathf.Max(dt, Time.fixedDeltaTime);
            Vector3 accel = havePrev ? (vel - prevVel) / aDt : Vector3.zero;
            prevVel = vel;
            float rawLatG = Vector3.Dot(accel, transform.right) / 9.81f;
            // A DIFFERENCE OF VELOCITIES IS ALSO A TELEPORT DETECTOR, and this
            // is the price of reading a real accelerometer. ResetTo and
            // DriveSession.Respawn zero the body's velocity in one step, and a
            // wall does something close to it: the difference then reports tens
            // of g, which the clamp turns into a full-lean lurch held for the
            // length of the filter. No tyre makes more than about two g, so
            // anything past DiscontinuityG did not come from the road and the
            // sample is dropped rather than smoothed — a lean that is not there
            // beats a lean that is wrong. The longitudinal channel gets the
            // same treatment; it always had this exposure through prevForward
            // and simply never had the check.
            if (havePrev && Mathf.Abs(rawLatG) > DiscontinuityG) rawLatG = latG;
            float rawLongG = havePrev ? (fwdV - prevForward) / aDt / 9.81f : 0f;
            if (havePrev && Mathf.Abs(rawLongG) > DiscontinuityG) rawLongG = longG;
            prevForward = fwdV;
            havePrev = true;
            latG = Mathf.Lerp(latG, rawLatG, 1f - Mathf.Exp(-dt / LatGTau));
            longG = Mathf.Lerp(longG, rawLongG, 1f - Mathf.Exp(-dt / LongGTau));

            // Airborne there is nothing to lean against; let it settle flat.
            bool grounded = car.anyWheelGrounded;
            float wantRoll = grounded ? RollDegFor(latG) : 0f;
            float wantPitch = grounded ? PitchDegFor(longG) : 0f;
            // Settle onto the target, then bound the rate — the slew alone is a
            // ramp that hits the cap and stops.
            float nextRoll = Mathf.Lerp(roll, wantRoll, 1f - Mathf.Exp(-dt / RollTau));
            float nextPitch = Mathf.Lerp(pitch, wantPitch, 1f - Mathf.Exp(-dt / PitchTau));
            roll = Mathf.MoveTowards(roll, nextRoll, RollRateDegPerSec * dt);
            pitch = Mathf.MoveTowards(pitch, nextPitch, PitchRateDegPerSec * dt);

            // Lean in CAR space, then the model's own yaw underneath it, so the
            // shell rolls about the car's nose-to-tail line whichever way the
            // pack happened to export it facing.
            var def = body != null ? body.Def : null;
            float modelYaw = def != null ? def.bodyYaw : 0f;
            var lean = Quaternion.Euler(pitch, 0f, roll);
            var yaw = Quaternion.Euler(0f, modelYaw, 0f);
            if (!Seat(lean, yaw, pitch, roll)) bodyRoot.localRotation = lean * yaw;
        }

        // ---- THE SHELL RIDES ON ITS TYRES (owner, 2026-10-19) ---------------
        // "Tires clipping up through fenders and hood ... This should never
        // happen." The shells leave a tyre 2-4 cm under its arch and ordinary
        // hard driving uses 4-7 cm of travel, so the physics' hard stop sits
        // beyond normal driving (CarController.BumpTravelMin) and the car
        // drives exactly as it did. What that leaves is the PICTURE: a loaded
        // corner, a landing, a lowered car at rest - the tyre reaches its arch
        // before the stop. Then the shell, which is render-only here anyway,
        // rides up on it: lifted and tilted just enough that every arch stays
        // over its own tyre, as if the tyre pushed it. The wheels stay on the
        // road where the physics put them; the body visibly rolls and dives a
        // little less at the corner that is full. Anything past
        // SeatRideMax (a kerb stepping a wheel up 15 cm in one tick) is held
        // by keeping the drawn hub under its arch instead.

        /// <summary>Paint-to-tyre gap the seat keeps (m).</summary>
        const float SeatMargin = 0.002f;
        /// <summary>The most the seat lifts any corner of the shell (m).</summary>
        const float SeatRideMax = 0.12f;

        readonly Vector3[] seatArm = new Vector3[4];
        readonly float[] seatNeed = new float[4], seatArch = new float[4], seatLean = new float[4];
        readonly float[] seatR = new float[4], seatHalfW = new float[4];
        readonly bool[] seatHas = new bool[4];

        /// <summary>Pose the shell (lean, then the seat) and hold each drawn hub
        /// under its arch. False when the stops are off or the shell has no
        /// measured arches: the plain lean, exactly as before.</summary>
        bool Seat(Quaternion lean, Quaternion yaw, float leanPitchDeg, float leanRollDeg)
        {
            if (!CarController.BumpStopsEnabled || body == null || body.Def == null || car.wheelHubs == null) return false;
            Vector3 pivot = body.SeatPosition;
            int n = 0;
            float cx = 0f, cy = 0f, cz = 0f;
            for (int i = 0; i < 4; i++)
            {
                seatHas[i] = false;
                if (i >= car.wheelHubs.Length || car.wheelHubs[i] == null) continue;
                float h = car.HubMax(i);
                if (float.IsPositiveInfinity(h)) continue;
                Vector3 hub = car.wheelHubs[i].localPosition;
                seatArm[i] = new Vector3(hub.x, h - pivot.y, hub.z - pivot.z);
                seatArch[i] = h;
                seatR[i] = car.WheelRadiusOf(i);
                seatHalfW[i] = body.TyreHalfWidth(i);
                seatHas[i] = true;
                cx += seatArm[i].x; cy += seatArm[i].y; cz += seatArm[i].z;
                n++;
            }
            if (n == 0) return false;
            cx /= n; cy /= n; cz /= n;

            // EVERY turn of the shell - the lean and the seat - pivots about the
            // arches' own centroid at hub height, not the body's origin down at
            // the sills. Turned about the sills, 2.5 deg slid each arch's
            // near-vertical front and back walls 1.3 cm along the car, into
            // tyres that stand 5-10 mm off them; and the lean's 1.2 deg of dive
            // put the S13's straight front tyre 8-12 mm through its lip under
            // braking.
            Vector3 q = pivot + new Vector3(cx, cy, cz);
            for (int i = 0; i < 4; i++)
                if (seatHas[i]) seatLean[i] = (q + lean * (pivot + seatArm[i] - q)).y - (pivot.y + seatArm[i].y);

            // Two passes: what is still left of a tilt (the arch's own floor
            // tips across the tyre's length and width as the shell turns, which
            // a lift measured over the hub cannot see) is allowed for at the
            // tilt the first pass chose.
            float lift = 0f, sinRoll = 0f, sinPitch = 0f, tiltP = 0f, tiltR = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                tiltP = Mathf.Abs(Mathf.Sin((leanPitchDeg + Mathf.Asin(sinPitch) * Mathf.Rad2Deg) * Mathf.Deg2Rad));
                tiltR = Mathf.Abs(Mathf.Sin((leanRollDeg + Mathf.Asin(sinRoll) * Mathf.Rad2Deg) * Mathf.Deg2Rad));
                float worst = 0f;
                for (int i = 0; i < 4; i++)
                {
                    if (!seatHas[i]) continue;
                    float allow = seatR[i] * tiltP + seatHalfW[i] * tiltR;
                    seatNeed[i] = car.PhysicsHubY(i) - (seatArch[i] + seatLean[i] - SeatMargin - allow);
                    if (seatNeed[i] > worst) worst = seatNeed[i];
                }
                // Nothing to lift - or, on the second pass, the first pass's seat
                // already clears it at the tilt it actually makes: keep it.
                if (worst <= 0f) break;
                // A plane of lifts over the four arches: least squares through
                // the tyres that need lifting, then raised until every arch -
                // including the ones with room to spare - clears its tyre.
                float mean = 0f;
                for (int i = 0; i < 4; i++)
                    if (seatHas[i]) mean += Mathf.Max(0f, seatNeed[i]);
                mean /= n;
                float sxx = 0f, szz = 0f, sxd = 0f, szd = 0f;
                for (int i = 0; i < 4; i++)
                {
                    if (!seatHas[i]) continue;
                    float dx = seatArm[i].x - cx, dz = seatArm[i].z - cz, d = Mathf.Max(0f, seatNeed[i]);
                    sxx += dx * dx; szz += dz * dz; sxd += dx * d; szd += dz * d;
                }
                float bx = sxx > 1e-6f ? sxd / sxx : 0f, bz = szz > 1e-6f ? szd / szz : 0f;
                float a = mean, top = 0f;
                for (int i = 0; i < 4; i++)
                    if (seatHas[i])
                        a = Mathf.Max(a, seatNeed[i] - bx * (seatArm[i].x - cx) - bz * (seatArm[i].z - cz));
                for (int i = 0; i < 4; i++)
                    if (seatHas[i]) top = Mathf.Max(top, a + bx * (seatArm[i].x - cx) + bz * (seatArm[i].z - cz));
                if (top > SeatRideMax) { float k = SeatRideMax / top; a *= k; bx *= k; bz *= k; }
                // lift = a + (x - cx) sin(roll) - (z - cz) sin(pitch), about the centroid.
                lift = a;
                sinRoll = Mathf.Clamp(bx, -0.5f, 0.5f);
                sinPitch = Mathf.Clamp(-bz, -0.5f, 0.5f);
            }
            var turn = Quaternion.Euler(Mathf.Asin(sinPitch) * Mathf.Rad2Deg, 0f, Mathf.Asin(sinRoll) * Mathf.Rad2Deg) * lean;
            bodyRoot.localPosition = q + turn * (pivot - q) + Vector3.up * lift;
            bodyRoot.localRotation = turn * yaw;

            // Each drawn hub under its own arch, as the shell now stands.
            for (int i = 0; i < 4; i++)
            {
                if (!seatHas[i]) continue;
                Vector3 arch = q + turn * (pivot + seatArm[i] - q) + Vector3.up * lift;
                float allow = seatR[i] * tiltP + seatHalfW[i] * tiltR;
                float limit = seatArch[i] + (arch.y - (pivot.y + seatArm[i].y)) - SeatMargin - allow;
                var t = car.wheelHubs[i];
                Vector3 lp = t.localPosition;
                lp.y = Mathf.Min(car.PhysicsHubY(i), limit);
                t.localPosition = lp;
            }
            return true;
        }
    }
}
