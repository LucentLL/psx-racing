using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// PSX-style chase camera with a bumper cam toggle (C key / gamepad north).
    /// </summary>
    public class ChaseCamera : MonoBehaviour
    {
        public Transform target;
        public CarController targetCar;

        public float distance = 5.4f;
        public float height = 1.8f;
        public float lookHeight = 0.9f;
        public float positionLag = 5f;
        public float rotationLag = 7f;
        public float baseFOV = 58f;
        public float speedFOV = 8f;

        int mode; // 0 chase, 1 bumper
        Vector3 smoothPos;
        Camera cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (target != null) smoothPos = target.position;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) mode = (mode + 1) % 2;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonNorth.wasPressedThisFrame) mode = (mode + 1) % 2;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float speed = targetCar != null ? Mathf.Abs(targetCar.forwardSpeed) : 0f;

            if (mode == 0)
            {
                // Flatten forward so the camera doesn't dive with body pitch
                Vector3 fwd = target.forward; fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;

                Vector3 wanted = target.position - fwd * distance + Vector3.up * height;
                float lag = positionLag + speed * 0.08f;
                smoothPos = Vector3.Lerp(smoothPos, wanted, 1f - Mathf.Exp(-lag * Time.deltaTime));
                transform.position = smoothPos;

                Vector3 lookAt = target.position + Vector3.up * lookHeight + fwd * 1.5f;
                Quaternion wantedRot = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, wantedRot,
                    1f - Mathf.Exp(-rotationLag * Time.deltaTime));
            }
            else
            {
                transform.position = target.TransformPoint(0f, 0.95f, 1.9f);
                transform.rotation = Quaternion.LookRotation(target.forward, Vector3.up);
                smoothPos = transform.position;
            }

            if (cam != null)
                cam.fieldOfView = baseFOV + speedFOV * Mathf.Clamp01(speed / 60f);
        }
    }
}
