using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Feeds keyboard/gamepad input into a CarController using the Input System.
    /// WASD/arrows drive, Space handbrake, Q/E manual shift (enables manual mode),
    /// M back to automatic, R respawn, C camera toggle.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class PlayerCarInput : MonoBehaviour
    {
        CarController car;
        float steerSmoothed;
        public bool inputEnabled = true;

        void Awake() => car = GetComponent<CarController>();

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;

            float steer = 0f, throttle = 0f, brake = 0f;
            bool handbrake = false;

            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle = 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) brake = 1f;
                handbrake = kb.spaceKey.isPressed;

                if (kb.eKey.wasPressedThisFrame) { car.manualMode = true; car.ShiftTo(car.currentGear + 1); }
                if (kb.qKey.wasPressedThisFrame) { car.manualMode = true; car.ShiftTo(car.currentGear - 1); }
                if (kb.mKey.wasPressedThisFrame) car.manualMode = false;
                if (kb.rKey.wasPressedThisFrame) RaceManager.Instance?.RespawnCar(car);
            }

            if (pad != null)
            {
                float padSteer = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(padSteer) > 0.12f) steer = padSteer;
                throttle = Mathf.Max(throttle, pad.rightTrigger.ReadValue());
                brake = Mathf.Max(brake, pad.leftTrigger.ReadValue());
                handbrake |= pad.buttonEast.isPressed;
                if (pad.rightShoulder.wasPressedThisFrame) { car.manualMode = true; car.ShiftTo(car.currentGear + 1); }
                if (pad.leftShoulder.wasPressedThisFrame) { car.manualMode = true; car.ShiftTo(car.currentGear - 1); }
                if (pad.selectButton.wasPressedThisFrame) RaceManager.Instance?.RespawnCar(car);
            }

            // On-screen controls (phones, tablets, touch laptops)
            bool touchDriving = false;
            var touch = TouchControls.Instance;
            if (touch != null && touch.Visible)
            {
                if (Mathf.Abs(touch.Steer) > 0.001f) { steer = touch.Steer; touchDriving = true; }
                throttle = Mathf.Max(throttle, touch.Throttle);
                brake = Mathf.Max(brake, touch.Brake);
                handbrake |= touch.Handbrake;
                if (touch.RestartPressed) RaceManager.Instance?.RespawnCar(car);
            }

            if (!inputEnabled) { steer = 0f; throttle = 0f; brake = 0.3f; handbrake = false; }

            // Keyboard steering is rate-limited so taps ramp in; the touch pad and
            // gamepad stick are already analog, so they pass through nearly direct.
            float rate = touchDriving ? 14f : (Mathf.Abs(steer) > 0.01f ? 5.5f : 8f);
            steerSmoothed = Mathf.MoveTowards(steerSmoothed, steer, rate * Time.deltaTime);

            car.steerInput = steerSmoothed;
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.handbrakeInput = handbrake;
        }
    }
}
