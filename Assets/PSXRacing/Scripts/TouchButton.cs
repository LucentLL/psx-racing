using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// A held on-screen button. Tracks its own pointer id so multi-touch
    /// (gas + steer + handbrake at once) works correctly.
    /// </summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool Pressed { get; private set; }
        public bool PressedThisFrame { get; private set; }

        int pointerId = int.MinValue;
        Image image;
        Color idleColor;
        Color activeColor;
        bool consumedPressFrame;

        void Awake()
        {
            image = GetComponent<Image>();
            if (image != null)
            {
                idleColor = image.color;
                activeColor = new Color(idleColor.r, idleColor.g, idleColor.b,
                                        Mathf.Min(1f, idleColor.a * 2.2f));
            }
        }

        void LateUpdate()
        {
            // PressedThisFrame is set on the down event and cleared the frame after
            if (consumedPressFrame) { PressedThisFrame = false; consumedPressFrame = false; }
            else if (PressedThisFrame) consumedPressFrame = true;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointerId != int.MinValue) return;
            pointerId = e.pointerId;
            Pressed = true;
            PressedThisFrame = true;
            consumedPressFrame = false;
            if (image != null) image.color = activeColor;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointerId) return;
            pointerId = int.MinValue;
            Pressed = false;
            if (image != null) image.color = idleColor;
        }

        void OnDisable()
        {
            Pressed = false;
            pointerId = int.MinValue;
            if (image != null) image.color = idleColor;
        }
    }
}
