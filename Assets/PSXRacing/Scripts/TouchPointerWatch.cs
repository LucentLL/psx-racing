using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Is anybody actually touching the screen right now?
    ///
    /// Every analog touch control here latches a value on pointer-down and only
    /// clears it on pointer-up, which is correct right up until the pointer-up
    /// never arrives. On mobile that happens routinely: the browser claims the
    /// touch for a scroll or a system gesture, the app loses focus, a call comes
    /// in, or the touch count changes in a way that makes the EventSystem
    /// retarget the id. Unity then never delivers OnPointerUp to the control
    /// that is holding the value, and it holds it forever — a throttle pinned at
    /// 85% with no finger on the glass, which is exactly what was reported.
    ///
    /// A per-id check would be tighter, but the EventSystem's pointerId and the
    /// Input System's touchId are not contractually the same number, and getting
    /// that wrong would release a pedal the player IS pressing — a far worse bug
    /// than the one being fixed. "No pointer anywhere" cannot produce a false
    /// release, and it catches the case the player actually hits: fingers off
    /// the screen, control still applied.
    /// </summary>
    public static class TouchPointerWatch
    {
        public static bool AnyPointerDown()
        {
            var touch = Touchscreen.current;
            if (touch != null)
            {
                var touches = touch.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var phase = touches[i].phase.ReadValue();
                    if (phase == UnityEngine.InputSystem.TouchPhase.Began ||
                        phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                        phase == UnityEngine.InputSystem.TouchPhase.Stationary)
                        return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed ||
                                  mouse.rightButton.isPressed ||
                                  mouse.middleButton.isPressed))
                return true;

            // A pen counts as a pointer too, and on a Windows tablet it is the
            // only one the player may be using.
            var pen = Pen.current;
            return pen != null && pen.tip.isPressed;
        }
    }
}
