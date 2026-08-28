using UnityEngine;
using UnityEngine.EventSystems;

namespace PSXRacing
{
    /// <summary>
    /// Analog steering wheel, ported from Racing Game 2's rotary control
    /// (src/input/steerWheel.ts).
    ///
    /// The important property is that it is ABSOLUTE, not relative: the wheel has
    /// a visible centre and a fixed ±165° range, so the player can always see
    /// where neutral is. The drag pad this replaces measured from wherever the
    /// finger happened to land, which meant neutral moved every time you
    /// re-gripped — and a control whose centre you cannot find is a control you
    /// oversteer with, correct, and oversteer again.
    ///
    /// Three details carry the feel, all from the source:
    ///   * ROTARY tracking, not horizontal: the angle around the hub is what
    ///     moves the wheel, so the gesture matches the object.
    ///   * A hub dead zone: atan2 is violently sensitive near the centre, and
    ///     dragging through the middle wraps ±pi, which would snap to full lock.
    ///     Samples inside 15% of the radius are discarded outright.
    ///   * Rotation ACCUMULATES from where the wheel already was, so re-gripping
    ///     continues the turn instead of teleporting the rim to your finger.
    /// </summary>
    public class TouchWheel : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        /// <summary>Hard rotation limit. Full lock = this much wheel rotation.</summary>
        public const float MaxRotationDeg = 165f;
        /// <summary>
        /// Hub dead zone, as a fraction of the wheel's WIDTH — matching the
        /// source's `r.width * HUB_DEADBAND_FRAC`, not of its radius.
        ///
        /// The distinction is a factor of two and it matters: atan2 is violently
        /// sensitive near the centre, and a finger sliding through the middle
        /// wraps +/-pi and would snap the wheel to full lock. Measuring against
        /// the radius made the guarded circle half the size the source protects.
        /// </summary>
        const float HubDeadbandFrac = 0.15f;
        /// <summary>How fast the rim eases back once released, in degrees/sec.
        /// Separate from the steering slew in PlayerCarInput: this one is purely
        /// cosmetic, and the source deliberately runs the two at similar but
        /// independent rates.</summary>
        public float visualReturnDegPerSec = 750f;

        /// <summary>-1..1, or null when nothing is touching the wheel — the null
        /// is what tells the input layer to fall through to its release slew
        /// rather than treating an untouched wheel as a commanded zero.</summary>
        public float? Axis { get; private set; }
        public bool Active { get; private set; }

        RectTransform self, rim;
        int pointerId = int.MinValue;
        float currentRotDeg;      // where the rim is drawn
        float startRotDeg;        // rotation when this drag began
        float prevAngleRad;       // last sampled finger angle
        float cumDeltaDeg;        // accumulated rotation this drag
        bool hasPrevAngle;

        void Awake() => self = GetComponent<RectTransform>();

        public void SetRim(RectTransform r) => rim = r;

        void Update()
        {
            // Same watchdog as the pedals: a wheel is held by a finger, so if
            // there is no finger on the screen it is not held. A stolen touch
            // used to leave Axis latched at whatever lock it was last at, which
            // steers the car into a wall with nothing on the glass.
            if (Active && !TouchPointerWatch.AnyPointerDown()) Release();

            if (!Active)
            {
                // Ease home. The physics axis already went null on release, so
                // this is only the picture catching up with the car.
                currentRotDeg = Mathf.MoveTowards(currentRotDeg, 0f,
                                                  visualReturnDegPerSec * Time.deltaTime);
            }
            if (rim != null) rim.localRotation = Quaternion.Euler(0f, 0f, -currentRotDeg);
        }

        /// <summary>Drive the rim from keyboard or gamepad so the wheel is not a
        /// dead prop for players who never touch it. Ignored mid-drag.</summary>
        public void SetVisualAxis(float axis)
        {
            if (Active) return;
            currentRotDeg = Mathf.Clamp(axis, -1f, 1f) * MaxRotationDeg;
        }

        bool TryAngle(Vector2 screenPos, out float angleRad)
        {
            angleRad = 0f;
            var cam = GetCanvasCamera();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    self, screenPos, cam, out Vector2 local)) return false;

            // Measure about the wheel's HUB, which is the centre of the rect —
            // not about the rect's local origin.
            //
            // ScreenPointToLocalPointInRectangle returns a point relative to the
            // PIVOT, and the hit zone is pivoted at its bottom-left corner
            // (0,0) so it can be anchored into the corner of the screen. Taking
            // atan2 of that raw point measured rotation about a point 150 units
            // down and left of the hub the player is actually turning: the
            // deadband guarded the corner instead of the centre, small movements
            // near the real hub produced huge angle swings, and on the side of
            // the wheel nearer the origin the sign of the delta inverted
            // outright — which is what "the wheel turns the wrong way" was.
            // rect.center is correct for any pivot, so this needs no assumption
            // about how the zone is anchored.
            Vector2 fromHub = local - self.rect.center;

            if (fromHub.magnitude < self.rect.width * HubDeadbandFrac) return false;
            angleRad = Mathf.Atan2(fromHub.y, fromHub.x);
            return true;
        }

        Camera GetCanvasCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            pointerId = e.pointerId;
            Active = true;
            startRotDeg = currentRotDeg;
            cumDeltaDeg = 0f;
            hasPrevAngle = TryAngle(e.position, out prevAngleRad);
            Axis = currentRotDeg / MaxRotationDeg;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            if (!TryAngle(e.position, out float angle))
            {
                // Inside the hub dead zone: drop the sample AND forget the
                // previous one, so re-emerging on the far side does not read as
                // a half-turn of travel that never happened.
                hasPrevAngle = false;
                return;
            }
            if (!hasPrevAngle) { prevAngleRad = angle; hasPrevAngle = true; return; }

            float delta = angle - prevAngleRad;
            // Unwrap across the ±pi seam.
            if (delta > Mathf.PI) delta -= 2f * Mathf.PI;
            else if (delta < -Mathf.PI) delta += 2f * Mathf.PI;
            prevAngleRad = angle;

            // Screen y is up in local rect space but a wheel turned clockwise
            // should steer right, so the accumulated angle is negated.
            cumDeltaDeg += -delta * Mathf.Rad2Deg;
            currentRotDeg = Mathf.Clamp(startRotDeg + cumDeltaDeg,
                                        -MaxRotationDeg, MaxRotationDeg);
            Axis = currentRotDeg / MaxRotationDeg;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            Release();
        }

        void OnDisable() => Release();

        void Release()
        {
            pointerId = int.MinValue;
            Active = false;
            hasPrevAngle = false;
            Axis = null;      // hand control back to the release slew
        }
    }
}
