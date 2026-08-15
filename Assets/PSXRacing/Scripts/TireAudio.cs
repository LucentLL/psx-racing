using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Tire voice, ported from Racing Game 2's tireGrain.ts + sim/tireLoad.ts.
    ///
    /// The important part is <see cref="GripUse"/>: a smoothed 0..1.6 measure of
    /// how much of the available grip the car is actually using, taken as the
    /// larger of lateral-acceleration utilisation and slip-angle utilisation.
    /// The tires start complaining at 0.72 of the envelope — audibly BEFORE the
    /// slide — which is what makes a slow hairpin and a fast sweeper sound
    /// different. Binding the sound to slip angle alone leaves both silent right
    /// up until the car lets go.
    ///
    /// Branch priority: drift screech > pre-limit scrub > brake lockup > launch chirp.
    /// </summary>
    public class TireAudio : MonoBehaviour
    {
        public CarController car;
        public AudioClip skidClip;
        [Range(0f, 1f)] public float masterVolume = 1f;
        public bool spatial;

        [Header("Grip envelope")]
        /// <summary>Lateral acceleration treated as "at the limit" (m/s^2).
        /// The source used 16.5 against a chassis that pulled 22.5 — i.e. ~73%.
        /// This chassis peaks near mu*g = 1.25*9.81 = 12.3, so 73% is 9.0.
        /// Setting it too high is what makes tires stay silent mid-corner.</summary>
        public float latLimit = 9.0f;
        public float slipRef = 0.14f;          // rad, ~8 deg
        public float smoothTau = 0.09f;
        public float minSpeed = 4.14f;         // m/s, parking manoeuvres stay silent

        // ---- constants from tireGrain.ts ----
        const float ScrubStart = 0.72f;
        const float ScrubMaxVol = 0.30f;
        const float DriftSlipGate = 0.15f;     // rad
        const float DriftMaxVol = 0.50f;
        const float GripUseCap = 1.6f;
        const float GainTau = 0.015f;          // fast, so the screech tracks a flick
        const float LockThreshRoad = 0.80f;
        const float LockThreshOff = 0.40f;
        const float FootLockSpeed = 5.6f;      // m/s
        const float EbrakeLockSpeed = 2.4f;    // m/s
        const float WheelspinGate = 0.15f;
        const float BurnoutGasThresh = 0.7f;

        public float GripUse { get; private set; }

        AudioSource src;
        Rigidbody body;
        float gain;
        float pitch = 1f;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            // Take the Rigidbody straight off the GameObject: CarController.Body is
            // assigned in its own Awake, and component Awake order is not defined.
            body = GetComponent<Rigidbody>();

            if (skidClip != null)
            {
                var go = new GameObject("snd_skid");
                go.transform.SetParent(transform, false);
                src = go.AddComponent<AudioSource>();
                src.clip = skidClip;
                src.loop = true;
                src.volume = 0f;
                src.playOnAwake = false;
                src.spatialBlend = spatial ? 1f : 0f;
                if (spatial)
                {
                    src.rolloffMode = AudioRolloffMode.Linear;
                    src.maxDistance = 70f;
                    src.minDistance = 5f;
                }
                src.Play();
            }
        }

        void Update()
        {
            if (car == null || body == null || src == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float speed = body.linearVelocity.magnitude;
            UpdateGripUse(dt, speed);

            float target = 0f;
            float pitchTarget = 1f;
            float slip = Mathf.Max(Mathf.Abs(car.rearSlipAngle), Mathf.Abs(car.frontSlipAngle));
            bool grounded = car.anyWheelGrounded;
            float lockThresh = car.onRoad ? LockThreshRoad : LockThreshOff;

            if (!grounded)
            {
                target = 0f;
            }
            else if (slip > DriftSlipGate && speed > minSpeed)
            {
                // Full slide: scales past the scrub ceiling so a real drift is a
                // step up in urgency, not just more of the same. The speed gate
                // matches the scrub branch — at walking pace a big steering angle
                // produces a big slip angle, and the tires must not shriek for it.
                float d = Mathf.Clamp01((slip - DriftSlipGate) / 0.45f);
                target = Mathf.Lerp(ScrubMaxVol, DriftMaxVol, d);
                pitchTarget = 0.92f + 0.28f * d;
            }
            else if (GripUse > ScrubStart && speed > minSpeed)
            {
                // Leaning on it, but still stuck: the pre-limit complaint.
                float u = Mathf.Clamp01((GripUse - ScrubStart) / (1f - ScrubStart));
                target = ScrubMaxVol * u;
                pitchTarget = 0.88f + 0.16f * u;
            }
            else if (car.brakeInput > lockThresh && speed > FootLockSpeed && car.forwardSpeed > 0f)
            {
                target = ScrubMaxVol * 1.2f;
                pitchTarget = 1.0f;
            }
            else if (car.handbrakeInput && speed > EbrakeLockSpeed)
            {
                target = ScrubMaxVol * 1.3f;
                pitchTarget = 1.05f;
            }

            // Wheelspin rides on top of whatever branch won: a lit-up rear axle
            // is audible even in a straight line.
            if (grounded && car.wheelSpin > WheelspinGate && car.throttleInput > BurnoutGasThresh)
            {
                float w = Mathf.Clamp01((car.wheelSpin - WheelspinGate) / (1f - WheelspinGate));
                target = Mathf.Max(target, ScrubMaxVol + 0.2f * w);
                pitchTarget = Mathf.Max(pitchTarget, 1.05f + 0.2f * w);
            }

            gain = Smooth(gain, target * masterVolume, GainTau, dt);
            pitch = Smooth(pitch, pitchTarget, 0.05f, dt);
            src.volume = gain;
            src.pitch = pitch;
        }

        /// <summary>
        /// Grip utilisation: the larger of lateral-accel use and slip-angle use,
        /// EMA-smoothed. In a steady turn lateral accel is speed * yaw rate, so
        /// the same steering angle at twice the speed reads as four times the
        /// load — which is exactly the distinction that was missing.
        /// </summary>
        void UpdateGripUse(float dt, float speed)
        {
            float k = Mathf.Min(1f, dt / Mathf.Max(smoothTau, 1e-4f));
            if (speed < minSpeed)
            {
                GripUse += (0f - GripUse) * k;   // decay, do not snap
                return;
            }

            float yawRate = Mathf.Abs(body.angularVelocity.y);
            float aLat = speed * yawRate;
            float ceiling = Mathf.Max(1f, latLimit * Mathf.Max(0.2f, car.gripBonus));
            float latUse = aLat / ceiling;
            float slipUse = Mathf.Max(Mathf.Abs(car.rearSlipAngle), Mathf.Abs(car.frontSlipAngle)) / slipRef;

            float raw = Mathf.Min(GripUseCap, Mathf.Max(latUse, slipUse));
            GripUse += (raw - GripUse) * k;
        }

        static float Smooth(float current, float target, float tc, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / Mathf.Max(tc, 1e-4f)));
    }
}
