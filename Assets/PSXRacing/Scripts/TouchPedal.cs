using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Analog pedal, ported from Racing Game 2's slider pedals
    /// (src/input/sliderPedal.ts). Used for throttle, brake and the handbrake.
    ///
    /// This replaces a binary button, and that swap is the single biggest
    /// handling fix available to this project. First gear makes roughly twice
    /// the force the rear tires can hold, so an on/off throttle drives the
    /// wheelspin ratio straight to its ceiling the instant it is pressed — and
    /// wheelspin is the direct input to the yaw injector that rotates the car.
    /// Binary throttle therefore reads as the car snapping sideways at random.
    /// With travel, the player can ask for 30% and get 30%.
    ///
    /// The input is RELATIVE, which is the source's key idea and worth
    /// preserving: touching the pedal never jumps it to the finger. The value
    /// moves by how far the finger TRAVELS, anchored at whatever the pedal was
    /// already reading. A full bar of travel covers 0 to 1, so a fingertip roll
    /// covers the fine range you actually drive in.
    /// </summary>
    public class TouchPedal : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        /// <summary>
        /// 0..1 pedal travel FROM TOUCH. This is the number the car drives on,
        /// and nothing but a finger may write it.
        ///
        /// It used to double as the display value, which made the visual mirror
        /// for keyboard players a feedback loop: PlayerCarInput forces brake to
        /// 0.3 on the starting grid, ReflectState wrote that into Amount, the
        /// input layer read Amount straight back out as a real brake request,
        /// and wrote it in again next frame. The result was a permanent 30%
        /// brake that no one was pressing and nothing could clear.
        /// </summary>
        public float Amount { get; private set; }
        public bool Active { get; private set; }

        /// <summary>
        /// What the gauge DRAWS — the touch amount, or a value mirrored from
        /// keyboard/gamepad while nothing is touching. Separate from
        /// <see cref="Amount"/> on purpose: display is an output, and an output
        /// must never be readable as an input.
        /// </summary>
        float displayAmount;

        /// <summary>
        /// Top-mounted: the gesture and the gauge both run DOWNWARD, and the
        /// fill hangs from the top rather than rising from the bottom.
        ///
        /// This is the handbrake. RG2 wires it with `ignoreInvert: true`
        /// precisely so it "always reads 'pull bottom to engage' like a real
        /// handbrake" — a lever you pull back, not a pedal you push. Porting all
        /// three controls with the pedals' direction made the e-brake engage the
        /// wrong way round, which is exactly how it was reported.
        /// </summary>
        public bool topMounted;

        /// <summary>How far the pedal pad travels TOWARD ITS MOUNT at full
        /// press, in canvas units. RG2's ARM_TRAVEL_PX, scaled to this
        /// panel.</summary>
        public float faceTravel = 39f;

        /// <summary>What the arm's length scales to at full press. RG2 derives
        /// it as (ARM_REST_PX - ARM_TRAVEL_PX) / ARM_REST_PX = (60-28)/60, and
        /// it has to agree with faceTravel or the pad detaches from the arm
        /// holding it up.</summary>
        public float armMinScale = (60f - 28f) / 60f;

        /// <summary>Handbrake lever angles, in degrees from upright, straight
        /// out of the source's `rotateX(calc(62deg - var(--ebrk-amt) * 75deg))`.
        /// This canvas is orthographic, so the rotation shows up as
        /// foreshortening: the lever is short lying back and reaches full length
        /// as it comes toward you.</summary>
        public float leverRestDeg = 62f, leverSweepDeg = 75f;

        RectTransform self;
        RectTransform fill, thumb, face, arm, lever;
        int pointerId = int.MinValue;
        float anchorY;          // screen y where the finger landed
        float anchorAmount;     // pedal reading at that moment
        float faceRestY;

        void Awake() => self = GetComponent<RectTransform>();

        /// <summary>
        /// Hand over the moving parts.
        ///
        /// <paramref name="armRect"/> and <paramref name="leverRect"/> are
        /// optional because not every variant has both: the source hides the
        /// base/arm/face stack entirely on the handbrake and shows only the
        /// lever, and the shifter has neither.
        /// </summary>
        public void SetParts(RectTransform fillRect, RectTransform thumbRect, RectTransform faceRect,
                             RectTransform armRect = null, RectTransform leverRect = null)
        {
            fill = fillRect; thumb = thumbRect; face = faceRect;
            arm = armRect; lever = leverRect;
            if (face != null) faceRestY = face.anchoredPosition.y;
            Redraw();
        }

        /// <summary>Mirror keyboard/gamepad input onto the pedal so it is not a
        /// dead prop on desktop — and so a later touch anchors from the right
        /// place instead of snapping.</summary>
        public void SetVisualAmount(float amount)
        {
            if (Active) return;
            displayAmount = Mathf.Clamp01(amount);   // NOT Amount — see above
            Redraw();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            pointerId = e.pointerId;
            Active = true;
            anchorY = e.position.y;
            // Anchors at what the gauge is SHOWING, so taking over from a
            // keyboard or gamepad continues from that level. Still deliberately
            // does not jump to the finger.
            anchorAmount = displayAmount;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            // Travel is measured against the bar's own height, so the gesture
            // scales with the control on every screen size.
            float barPixels = Mathf.Max(1f, self.rect.height * CanvasScaleFactor());
            float travel = (e.position.y - anchorY) / barPixels;
            if (topMounted) travel = -travel;      // pull DOWN to engage
            Amount = Mathf.Clamp01(anchorAmount + travel);
            displayAmount = Amount;
            Redraw();
        }

        float CanvasScaleFactor()
        {
            var canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.scaleFactor : 1f;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            Release();
        }

        void OnDisable() => Release();

        /// <summary>
        /// Watchdog for the pointer-up that never came. A pedal is held by a
        /// finger; if there is no finger anywhere on the screen, it is not being
        /// held, whatever the EventSystem last told us. Without this a touch the
        /// browser steals mid-press leaves the throttle pinned with nothing on
        /// the glass — the car drives off on its own and the player cannot stop
        /// it, which is what was reported from the device.
        ///
        /// Runs in Update rather than on an event because the whole problem is
        /// that the event does not arrive.
        /// </summary>
        void Update()
        {
            if (Active && !TouchPointerWatch.AnyPointerDown()) Release();
        }

        void Release()
        {
            pointerId = int.MinValue;
            Active = false;
            // Lifting off is a full lift — a pedal that eased back would keep
            // applying throttle after the player let go.
            Amount = 0f;
            displayAmount = 0f;
            Redraw();
        }

        void Redraw()
        {
            float h = self != null ? self.rect.height : 150f;
            float a = displayAmount;
            if (fill != null) fill.sizeDelta = new Vector2(fill.sizeDelta.x, h * a);
            if (thumb != null)
            {
                // The thumb rides the LEADING edge of the fill, which is the
                // bottom of the bar for a pedal and the top for a lever.
                float y = topMounted ? h * (1f - a) : h * a;
                thumb.anchoredPosition = new Vector2(thumb.anchoredPosition.x, y);
            }
            // The pad rises TOWARD its mount as it is pressed, and the arm
            // holding it shortens by exactly the same distance. Both hang from
            // the top of the bar, so "toward the mount" is +y here. Driving the
            // pad the other way — which is what this did — read as a pedal
            // being pushed off the end of its own linkage.
            if (face != null)
                face.anchoredPosition = new Vector2(face.anchoredPosition.x,
                                                    faceRestY + a * faceTravel);
            if (arm != null)
                arm.localScale = new Vector3(1f, 1f - a * (1f - armMinScale), 1f);
            if (lever != null)
            {
                float deg = leverRestDeg - a * leverSweepDeg;
                lever.localScale = new Vector3(1f, Mathf.Cos(deg * Mathf.Deg2Rad), 1f);
            }
        }
    }
}
