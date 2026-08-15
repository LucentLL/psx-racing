using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Waypoint-following opponent: steers toward a speed-scaled lookahead point,
    /// sets corner speed from baked curvature, brakes ahead of slow corners.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class AIDriver : MonoBehaviour
    {
        public TrackPath path;
        [Range(0.8f, 1.05f)] public float skill = 0.95f;
        public float lateralOffset = 0f;   // keeps AI cars off each other's line

        CarController car;
        int nearestIdx;
        float stuckTimer;
        public bool driving = true;

        void Awake() => car = GetComponent<CarController>();

        void Start()
        {
            if (path != null) nearestIdx = path.NearestIndex(transform.position);
        }

        void Update()
        {
            if (path == null || path.Count == 0) return;
            nearestIdx = path.NearestIndex(transform.position, nearestIdx);

            if (!driving)
            {
                car.steerInput = 0f; car.throttleInput = 0f; car.brakeInput = 0.4f;
                return;
            }

            float speed = Mathf.Abs(car.forwardSpeed);

            // ---- steering: chase a lookahead point ----
            float lookDist = 7f + speed * 0.45f;
            int lookIdx = nearestIdx + Mathf.Max(2, Mathf.RoundToInt(lookDist / path.spacing));
            Vector3 target = path.GetPoint(lookIdx);
            Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(lookIdx));
            target += right * lateralOffset;

            Vector3 local = transform.InverseTransformPoint(target);
            float steer = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(local.z, 0.5f)) * 1.4f, -1f, 1f);

            // ---- target speed from curvature ahead ----
            float mu = 1.0f * skill;
            float curvNow = Mathf.Max(path.MaxCurvatureAhead(nearestIdx, 6), 0.0005f);
            float cornerSpeed = Mathf.Sqrt(mu * 9.81f / curvNow) * 0.92f;
            float targetSpeed = Mathf.Min(cornerSpeed, 68f * skill);

            // Brake early for upcoming slow corners
            float brakeScan = speed * speed / (2f * 6.5f) + 10f;
            // Cap the scan: it grows with speed squared, and letting it run long
            // enough to wrap the whole track pins the AI to the tightest corner
            // anywhere on the circuit.
            int scanCount = Mathf.Min(Mathf.CeilToInt(brakeScan / path.spacing),
                                      Mathf.Min(60, path.Count / 3));
            float curvAhead = Mathf.Max(path.MaxCurvatureAhead(nearestIdx, scanCount), 0.0005f);
            float aheadSpeed = Mathf.Sqrt(mu * 9.81f / curvAhead) * 0.92f;
            targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(aheadSpeed, 9f) + 3f);

            float throttle = 0f, brake = 0f;
            if (speed < targetSpeed - 1f) throttle = 1f;
            else if (speed > targetSpeed + 2f) brake = Mathf.Clamp01((speed - targetSpeed) * 0.25f);
            else throttle = 0.35f;

            // Ease off throttle while sliding
            if (Mathf.Abs(car.rearSlipAngle) > 0.25f) throttle *= 0.4f;

            car.steerInput = steer;
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.handbrakeInput = false;

            // ---- stuck recovery ----
            if (speed < 1f && driving) stuckTimer += Time.deltaTime;
            else stuckTimer = 0f;
            if (stuckTimer > 4f)
            {
                stuckTimer = 0f;
                RaceManager.Instance?.RespawnCar(car);
            }
        }
    }
}
