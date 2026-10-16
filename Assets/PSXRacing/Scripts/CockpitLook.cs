using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// LOOK-OVER in COCKPIT view (owner, 2026-10-14): "Player can look over
    /// (mobile, by sliding, not steering wheel), on controller right stick
    /// left/right". The point is the passenger seat, where the pizzas are.
    ///
    /// The head turns up to <see cref="MaxYaw"/> each way. Turning to the
    /// PASSENGER side (right in an LHD cabin, left in an RHD one) also tips it down,
    /// smoothstepped in to <see cref="DownAtFull"/> at full turn, so the stack
    /// on the seat is framed when the player is all the way round. Let go and
    /// it eases back to the road in about <see cref="ReturnSeconds"/>.
    ///
    ///   Pad      right stick X (its Y shifts gears - PlayerCarInput).
    ///   Touch    a horizontal SLIDE that starts OUTSIDE every control: a
    ///            touch that lands on the wheel, the pedals, the shifter or
    ///            any button is the control's, never a look. Any finger, so it
    ///            works while the other thumb steers.
    ///   Mouse    a left-button drag (desktop only; off while the touch panel
    ///            shows, where a browser also reports touches as a mouse).
    ///
    /// Pure rotation about the eye: the cabin is scaled about the eye
    /// (CockpitInterior), so anything else would depend on the car.
    /// ChaseCamera reads <see cref="Yaw"/> and <see cref="Pitch"/>.
    /// </summary>
    public static class CockpitLook
    {
        // ---- SHARED with tools/cockpit/universal_cockpit.py (near plane) ----
        public const float MaxYaw = 80f;
        public const float DownAtFull = 28.5f;
        // ---------------------------------------------------------------------
        public const float ReturnSeconds = 0.25f;
        /// <summary>Stick dead zone, and how fast the head follows the stick.</summary>
        const float StickDead = 0.15f, StickTau = 0.05f;
        /// <summary>A slide across this fraction of the screen width is a full turn.</summary>
        const float SlideFullFrac = 0.35f;

        /// <summary>Head yaw, degrees, + = right (<see cref="PassengerSign"/> says which way the passenger is).</summary>
        public static float Yaw { get; private set; }
        /// <summary>Extra downward tilt, degrees, at the current yaw.</summary>
        public static float Pitch => DownFor(Yaw);
        /// <summary>0 looking ahead, 1 at full turn either way.</summary>
        public static float Amount => Mathf.Clamp01(Mathf.Abs(Yaw) / MaxYaw);

        public static float DownFor(float yaw) =>
            yaw * PassengerSign <= 0f ? 0f : DownAtFull * ChaseCamera.Smooth01(Mathf.Abs(yaw) / MaxYaw);

        /// <summary>+1 when the passenger seat is to the RIGHT (a left-hand
        /// drive cabin), -1 in a right-hand drive one: the look-over dips
        /// toward the cargo on whichever side it rides. CockpitInterior sets
        /// it with the cabin's variant (DriveSide).</summary>
        public static float PassengerSign = 1f;

        static int tickFrame = -1;
        static int slideTouch = -1;
        static float slideX0, slideYaw0;
        static bool mouseDrag;
        static float mouseX0, mouseYaw0;
        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        /// <summary>Forget everything: out of the cockpit, on foot, a new scene.</summary>
        public static void Reset()
        {
            Yaw = 0f;
            slideTouch = -1;
            mouseDrag = false;
        }

        /// <summary>
        /// One frame. <paramref name="active"/>: the cockpit is the view and the
        /// driver is in the car. <paramref name="readInput"/>: false while a menu
        /// or the focus guard holds the controls (the head stays put).
        /// </summary>
        public static void Tick(float dt, bool active, bool readInput)
        {
            // Once a frame, whoever calls (a second chase camera must not
            // double the easing).
            if (Time.frameCount == tickFrame) return;
            tickFrame = Time.frameCount;
            if (!active) { if (Yaw != 0f || slideTouch >= 0 || mouseDrag) Reset(); return; }
            if (!readInput || dt <= 0f) return;

            bool held = false;
            float target = 0f;

            var pad = Gamepad.current;
            if (pad != null)
            {
                float x = pad.rightStick.x.ReadValue();
                if (Mathf.Abs(x) > StickDead)
                {
                    held = true;
                    target = Mathf.Sign(x) * (Mathf.Abs(x) - StickDead) / (1f - StickDead) * MaxYaw;
                }
            }

            // Touch: one finger owns the look, from the frame it lands until
            // it lifts; the slide moves the head directly (no lag - a finger
            // is direct manipulation), relative to where the head was.
            float perPx = MaxYaw / Mathf.Max(1f, Screen.width * SlideFullFrac);
            var screen = Touchscreen.current;
            bool stillSliding = false;
            if (screen != null)
            {
                var touches = screen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var t = touches[i];
                    var phase = t.phase.ReadValue();
                    bool live = phase == UnityEngine.InputSystem.TouchPhase.Began ||
                                phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                                phase == UnityEngine.InputSystem.TouchPhase.Stationary;
                    if (!live) continue;
                    int id = t.touchId.ReadValue();
                    Vector2 pos = t.position.ReadValue();
                    if (phase == UnityEngine.InputSystem.TouchPhase.Began && slideTouch < 0 && !OnControl(pos))
                    {
                        slideTouch = id;
                        slideX0 = pos.x;
                        slideYaw0 = Yaw;
                    }
                    if (id == slideTouch)
                    {
                        stillSliding = true;
                        held = true;
                        float y = Mathf.Clamp(slideYaw0 + (pos.x - slideX0) * perPx, -MaxYaw, MaxYaw);
                        if (Mathf.Abs(y) >= Mathf.Abs(target)) target = y;
                    }
                }
            }
            if (!stillSliding) slideTouch = -1;

            var mouse = Mouse.current;
            if (mouse != null && !TouchControls.Showing)
            {
                Vector2 mp = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame && !OnControl(mp))
                {
                    mouseDrag = true;
                    mouseX0 = mp.x;
                    mouseYaw0 = Yaw;
                }
                if (!mouse.leftButton.isPressed) mouseDrag = false;
                if (mouseDrag)
                {
                    held = true;
                    float y = Mathf.Clamp(mouseYaw0 + (mp.x - mouseX0) * perPx, -MaxYaw, MaxYaw);
                    if (Mathf.Abs(y) >= Mathf.Abs(target)) target = y;
                }
            }
            else mouseDrag = false;

            if (held)
                Yaw = slideTouch >= 0 || mouseDrag
                    ? target
                    : Mathf.Lerp(Yaw, target, 1f - Mathf.Exp(-dt / StickTau));
            else
                // Back to the road: ~95% of the way in ReturnSeconds.
                Yaw = Mathf.Abs(Yaw) < 0.05f ? 0f : Mathf.Lerp(Yaw, 0f, 1f - Mathf.Exp(-dt * 3f / ReturnSeconds));
        }

        /// <summary>
        /// Would the UI take a press here? The same question the EventSystem
        /// asks: anything under the point that handles a pointer press, click
        /// or drag (the touch wheel, pedals, shifter, every button) owns it.
        /// </summary>
        static bool OnControl(Vector2 pos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            // Only on a press (a finger landing, a click): a fresh probe is fine.
            var probe = new PointerEventData(es) { position = pos };
            hits.Clear();
            es.RaycastAll(probe, hits);
            foreach (var h in hits)
            {
                var go = h.gameObject;
                if (go == null) continue;
                if (ExecuteEvents.GetEventHandler<IPointerDownHandler>(go) != null ||
                    ExecuteEvents.GetEventHandler<IPointerClickHandler>(go) != null ||
                    ExecuteEvents.GetEventHandler<IBeginDragHandler>(go) != null ||
                    ExecuteEvents.GetEventHandler<IDragHandler>(go) != null)
                    return true;
            }
            return false;
        }
    }
}
