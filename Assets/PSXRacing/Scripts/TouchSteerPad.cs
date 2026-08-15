using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Analog steering pad: touch anywhere in the zone, then drag left/right.
    /// Steer is relative to where the finger landed, so the player never has to
    /// look at the screen to find a centre. Auto-centres on release.
    /// </summary>
    public class TouchSteerPad : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        /// <summary>Screen-pixel travel for full lock.</summary>
        public float fullLockPixels = 150f;
        /// <summary>Steer units per second the pad recentres when released.</summary>
        public float returnRate = 6f;

        public float Steer { get; private set; }
        public bool Active { get; private set; }

        int pointerId = int.MinValue;
        float originX;
        RectTransform knob;
        RectTransform self;

        void Awake()
        {
            self = GetComponent<RectTransform>();
        }

        public void SetKnob(RectTransform k) => knob = k;

        void Update()
        {
            if (!Active)
                Steer = Mathf.MoveTowards(Steer, 0f, returnRate * Time.deltaTime);
            if (knob != null)
                knob.anchoredPosition = new Vector2(Steer * 60f, knob.anchoredPosition.y);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            pointerId = e.pointerId;
            Active = true;
            originX = e.position.x;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            float scale = Mathf.Max(Screen.height / 720f, 0.5f);
            Steer = Mathf.Clamp((e.position.x - originX) / (fullLockPixels * scale), -1f, 1f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            pointerId = int.MinValue;
            Active = false;
        }

        void OnDisable()
        {
            Active = false;
            pointerId = int.MinValue;
            Steer = 0f;
        }
    }
}
