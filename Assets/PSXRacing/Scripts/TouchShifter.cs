using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Gated shift knob, ported from Racing Game 2's shifter (src/input/shifter.ts).
    ///
    /// Slide the knob up to change up, down to change down. Two rules from the
    /// source are what make it feel mechanical rather than like a button:
    ///   * The throw is most of the gate (40 of 53 units). The source tried a
    ///     shorter throw and the player rejected it as "shifts without proper
    ///     sliding" — a control should move the distance of its own gauge.
    ///   * ONE shift per drag. Holding past the threshold does not repeat, and
    ///     there is no tap-to-shift; releasing short simply eases home.
    /// </summary>
    public class TouchShifter : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public const float MaxTravel = 53f;
        public const float ShiftThreshold = 40f;
        public float returnPerSec = 400f;

        /// <summary>+1 upshift, -1 downshift.</summary>
        public event Action<int> Shifted;

        RectTransform self, knob;
        Text gearLabel;
        int pointerId = int.MinValue;
        float startY;
        float offset;
        bool firedThisDrag;

        void Awake() => self = GetComponent<RectTransform>();

        public void SetParts(RectTransform knobRect, Text label)
        {
            knob = knobRect;
            gearLabel = label;
        }

        void Update()
        {
            // A lost pointer-up would leave the shifter believing a drag is
            // still in progress, which blocks every later shift: OnPointerDown
            // refuses to start while pointerId is set, so the gearbox silently
            // stops responding for the rest of the race.
            if (pointerId != int.MinValue && !TouchPointerWatch.AnyPointerDown())
                pointerId = int.MinValue;

            if (pointerId == int.MinValue)
                offset = Mathf.MoveTowards(offset, 0f, returnPerSec * Time.deltaTime);
            if (knob != null)
                knob.anchoredPosition = new Vector2(knob.anchoredPosition.x, offset);
        }

        /// <summary>Show the gear the car is actually in. Reverse reads amber,
        /// the way a real gate marks it.</summary>
        public void SetGear(int gear)
        {
            if (gearLabel == null) return;
            string s = gear == -1 ? "R" : gear == 0 ? "N" : gear.ToString();
            if (gearLabel.text != s) gearLabel.text = s;
            gearLabel.color = gear == -1 ? new Color(1f, 0.55f, 0f) : Color.white;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            pointerId = e.pointerId;
            startY = e.position.y;
            firedThisDrag = false;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            float scale = 1f;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) scale = Mathf.Max(0.01f, canvas.scaleFactor);

            float dy = (e.position.y - startY) / scale;
            offset = Mathf.Clamp(dy, -MaxTravel, MaxTravel);

            if (firedThisDrag) return;
            if (dy >= ShiftThreshold) { firedThisDrag = true; Shifted?.Invoke(1); }
            else if (dy <= -ShiftThreshold) { firedThisDrag = true; Shifted?.Invoke(-1); }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            pointerId = int.MinValue;      // Update() eases the knob home
        }

        void OnDisable() => pointerId = int.MinValue;
    }
}
