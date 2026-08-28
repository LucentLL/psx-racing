using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Decides what the player is standing in front of, and fires it when they
    /// press USE.
    ///
    /// Scoring is ANGLE first and distance second. Standing between two cars
    /// and looking at one of them should pick the one being looked at, even if
    /// the other is fractionally nearer — which is the case the naive
    /// nearest-object rule gets wrong every time, and the one that makes a
    /// garage feel like it is arguing with you.
    /// </summary>
    public class FootInteractor : MonoBehaviour
    {
        public Transform eye;

        /// <summary>Widest angle off the view centre that still counts as
        /// looking at something. 55 degrees is generous — the camera's own
        /// field of view is 60 — and generous is right for a room where the
        /// things worth using are metres apart.</summary>
        public float maxAngle = 55f;

        /// <summary>What is currently offered, or null.</summary>
        public FootTarget Current { get; private set; }

        /// <summary>Set on the frame USE fired, for the UI to flash on.</summary>
        public bool UsedThisFrame { get; private set; }

        /// <summary>Written by the on-screen USE button.</summary>
        [HideInInspector] public bool touchUse;
        /// <summary>Written by the on-screen second button, when the thing in
        /// front of the player has a second verb.</summary>
        [HideInInspector] public bool touchUse2;

        void Update()
        {
            UsedThisFrame = false;
            Current = Pick();

            bool pressed = touchUse;
            bool pressed2 = touchUse2;
            touchUse = false;
            touchUse2 = false;

            var kb = Keyboard.current;
            if (kb != null && (kb.fKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                pressed = true;
            // E for the second verb: it is the other hand's index finger on a
            // WASD grip, and it is not F, which is already the first one.
            if (kb != null && kb.eKey.wasPressedThisFrame) pressed2 = true;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonSouth.wasPressedThisFrame) pressed = true;
            // X on an Xbox pad / Square on a DualShock — the face button a
            // console player already reads as "the other action".
            if (pad != null && pad.buttonWest.wasPressedThisFrame) pressed2 = true;

            if (Current == null) return;

            if (pressed2 && !string.IsNullOrEmpty(Current.action2))
            {
                UsedThisFrame = true;
                Current.onUse2?.Invoke();
                return;
            }

            if (!pressed || string.IsNullOrEmpty(Current.action)) return;

            UsedThisFrame = true;
            Current.onUse?.Invoke();
        }

        FootTarget Pick()
        {
            if (eye == null) return null;
            Vector3 from = eye.position;
            Vector3 fwd = eye.forward;

            FootTarget best = null;
            float bestScore = float.MaxValue;

            var list = FootTarget.All;
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it == null) continue;

                Vector3 to = it.FocusPoint - from;
                float dist = to.magnitude;
                if (dist > it.range || dist < 0.001f) continue;

                float angle = Vector3.Angle(fwd, to);
                if (angle > maxAngle) continue;

                // Angle in degrees plus a metre-for-eight-degrees distance
                // tiebreak. The units are arbitrary; what matters is that
                // pointing at something beats being marginally closer to
                // something else.
                float score = angle + dist * 8f;
                if (score < bestScore) { bestScore = score; best = it; }
            }
            return best;
        }
    }
}
