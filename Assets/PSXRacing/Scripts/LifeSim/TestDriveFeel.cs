using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R0b: WHAT A TEST DRIVE ACTUALLY DID, measured on the seller's car, so
    /// NAME WHAT YOU FELT can hold a claim against the drive (design §6: a
    /// chip is true only when the car really showed it on that drive - its
    /// condition was met, e.g. the pull above 30 km/h for 5 s). Added to the
    /// player's car by RaceHandoffApplier on a test drive only; it reads the
    /// car, never writes to it. The bits (FaultSymptoms.Cond*) are banked in a
    /// static the moment each is met, so leaving by any door keeps them.
    /// PSX_FAULT_FELT=0: never added.
    /// </summary>
    public class TestDriveFeel : MonoBehaviour
    {
        /// <summary>The visit the drive belongs to (RaceHandoff.TestDriveKey).</summary>
        public static string LastKey;
        /// <summary>FaultSymptoms.Cond* bits met so far on that drive.</summary>
        public static int LastConds;
        static bool lastValid;

        CarController car;
        Rigidbody rb;
        EngineTemp temp;
        Vector3 lastVel;
        bool haveVel;
        float cruiseRun, throttleT, highRpmT, cornerT, slowSteerT, droveT;
        int lastGear, shifts;
        bool braking;

        /// <summary>Start measuring on a test drive's car. Nothing on any other
        /// drive, or with the switch off.</summary>
        public static void Arm(CarController playerCar)
        {
            if (!FaultLeaks.FeltOn || playerCar == null || !RaceHandoff.FromLifeSim || !RaceHandoff.TestDrive)
                return;
            LastKey = RaceHandoff.TestDriveKey;
            LastConds = 0;
            lastValid = true;
            int h = RaceHandoff.TimeOfDayIndex;
            if (h < 6 || h >= 20) LastConds |= FaultSymptoms.CondNight;
            var feel = playerCar.GetComponent<TestDriveFeel>();
            if (feel == null) feel = playerCar.gameObject.AddComponent<TestDriveFeel>();
            feel.car = playerCar;
            feel.rb = playerCar.GetComponent<Rigidbody>();
            feel.temp = playerCar.GetComponent<EngineTemp>();
            feel.lastGear = playerCar.currentGear;
        }

        /// <summary>The conditions met on the drive for this visit, once; 0 when
        /// no drive for it was measured.</summary>
        public static int Take(string key)
        {
            if (!lastValid || string.IsNullOrEmpty(key) || LastKey != key) return 0;
            lastValid = false;
            return LastConds;
        }

        /// <summary>For the self-test: as if a drive for this visit had met
        /// these conditions.</summary>
        public static void Stage(string key, int conds)
        {
            LastKey = key; LastConds = conds; lastValid = true;
        }

        void FixedUpdate()
        {
            if (car == null || !lastValid || LastKey != RaceHandoff.TestDriveKey) return;
            float dt = Time.fixedDeltaTime;
            float kmh = Mathf.Abs(car.speedKmh);
            int c = LastConds;

            cruiseRun = kmh >= 30f ? cruiseRun + dt : 0f;
            if (cruiseRun >= 5f) c |= FaultSymptoms.CondCruise;

            if (kmh >= 20f && car.throttleInput >= 0.8f) throttleT += dt;
            if (throttleT >= 3f) c |= FaultSymptoms.CondThrottle;

            if (car.currentRPM >= 3500f && car.throttleInput >= 0.6f) highRpmT += dt;
            if (highRpmT >= 2f) c |= FaultSymptoms.CondHighRpm;

            if (car.currentGear != lastGear)
            {
                if (kmh >= 5f && car.currentGear > 0 && lastGear > 0) shifts++;
                lastGear = car.currentGear;
            }
            if (shifts >= 2) c |= FaultSymptoms.CondShifts;

            bool brakeNow = car.brakeInput >= 0.5f;
            if (brakeNow && !braking && kmh >= 60f) c |= FaultSymptoms.CondStop;
            braking = brakeNow;

            if (rb != null)
            {
                Vector3 v = rb.linearVelocity;
                if (haveVel && dt > 0f)
                {
                    Vector3 acc = (v - lastVel) / dt;
                    float side = Mathf.Abs(Vector3.Dot(acc, car.transform.right));
                    if (kmh >= 30f && side >= 3f) cornerT += dt;
                }
                lastVel = v; haveVel = true;
            }
            if (cornerT >= 2f) c |= FaultSymptoms.CondCorner;

            if (kmh < 30f && kmh > 1f && Mathf.Abs(car.steerInput) >= 0.5f) slowSteerT += dt;
            if (slowSteerT >= 1f) c |= FaultSymptoms.CondSlowSteer;

            if (kmh >= 5f) droveT += dt;
            if (droveT >= 10f) c |= FaultSymptoms.CondDrove;

            if (temp != null && temp.celsius >= FaultSymptoms.HotMarkC) c |= FaultSymptoms.CondHot;

            LastConds = c;
        }
    }
}
