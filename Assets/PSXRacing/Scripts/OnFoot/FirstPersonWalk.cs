using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Walking around the garage: a capsule, a camera on a neck, and three ways
    /// to drive it — keyboard and mouse, a pad, or two thumbs on a phone.
    ///
    /// Deliberately slow and deliberately unarmed. This is a room with four
    /// cars and a parts rack in it, not a level: the whole floor is crossable in
    /// about seven seconds, and the only verbs are LOOK, WALK and USE. Anything
    /// more — jumping, running, crouching — would be controls to explain in a
    /// place with nothing to use them on.
    ///
    /// Mouse look is gated on pointer lock, and pointer lock is gated on a
    /// click, because a browser will not grant it any other way. That gate is
    /// also what makes the on-screen buttons clickable: with the pointer free,
    /// the mouse belongs to the UI, and the game says so rather than fighting
    /// the cursor for it.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonWalk : MonoBehaviour
    {
        public Transform head;

        /// <summary>Metres per second. A brisk indoor walk — running in a
        /// 22 metre room is one step and a wall.</summary>
        public float walkSpeed = 2.9f;
        /// <summary>Degrees per mouse count. Roughly what a 800 dpi mouse at a
        /// normal Windows pointer speed feels like in any other first-person
        /// game, which is the only reference a player has.</summary>
        public float mouseSensitivity = 0.13f;
        /// <summary>Degrees per second at full stick.</summary>
        public float padLookSpeed = 165f;
        /// <summary>How far you can look up and down. Not 90: at exactly
        /// vertical the yaw axis becomes meaningless and the view rolls as you
        /// turn, which reads as the camera being broken.</summary>
        public float pitchLimit = 78f;

        /// <summary>Walk vector from the on-screen stick, -1..1 per axis.
        /// Written every frame by the touch layer and read here; zero when
        /// there is no touch layer, which is what keeps this component free of
        /// any knowledge of the UI.</summary>
        [HideInInspector] public Vector2 externalMove;
        /// <summary>Look delta in DEGREES from a screen drag, consumed and
        /// cleared each frame. Degrees rather than pixels because the
        /// sensitivity of a drag is a property of the drag, not of this.
        /// </summary>
        [HideInInspector] public Vector2 externalLook;

        /// <summary>False while the pointer is free. The prompt layer reads it
        /// to decide whether to say "CLICK TO LOOK AROUND".</summary>
        public bool MouseCaptured { get; private set; }

        CharacterController body;
        float yaw, pitch;
        float fallSpeed;

        // ---- stuck watchdog ----
        /// <summary>Somewhere the player was standing, moving, and not wedged.
        /// Sampled while they are visibly getting somewhere.</summary>
        Vector3 lastFreeSpot;
        float stuckFor;
        float sinceFreeSample;

        /// <summary>How long to be pushing against nothing before we accept
        /// that the room has hold of us. Long enough that walking into a wall
        /// on purpose does not trigger it — a player leaning on a doorframe for
        /// a second and a half is not stuck, one doing it for four is.</summary>
        const float StuckSeconds = 4f;
        /// <summary>Below this, a metre-per-second walk has not moved.</summary>
        const float StuckSpeed = 0.12f;

        /// <summary>
        /// The walker currently on their feet, or null when the player is in a
        /// car (or in a menu).
        ///
        /// OnEnable/OnDisable rather than Awake, because the forecourt does not
        /// destroy its rig when the player gets back in — it deactivates it,
        /// and a static set in Awake would go on claiming a driver was walking
        /// around for the rest of the session. Anything that wants to know
        /// where the person is rather than where the car is reads this;
        /// <see cref="SwingDoor"/> is the first.
        /// </summary>
        public static FirstPersonWalk Current { get; private set; }

        void Awake()
        {
            body = GetComponent<CharacterController>();
            yaw = transform.eulerAngles.y;
            if (head != null) pitch = 0f;
        }

        void OnEnable() => Current = this;

        /// <summary>
        /// Re-read the heading off the transform.
        ///
        /// Awake caches yaw and Look() then WRITES the rotation from it every
        /// frame, so anything that turns the player after Awake is undone on
        /// the next frame with nothing to say so. That is not hypothetical:
        /// the seller's street picks which driveway the player is standing on
        /// at runtime and turns them to face the car, and script execution
        /// order decides whether Awake ran first.
        /// </summary>
        public void SnapYawToTransform() => yaw = transform.eulerAngles.y;

        void OnDisable()
        {
            if (Current == this) Current = null;
            SetCapture(false);
        }

        void Update()
        {
            HandleCapture();
            HandleInvertKey();
            Look();
            Move();
        }

        /// <summary>
        /// Flip the pitch axis without leaving the room. The setting also lives
        /// in the pause menu and on the home screen, but a player who has just
        /// looked up and gone down needs it HERE, in the two seconds before
        /// they decide the controls are broken.
        /// </summary>
        void HandleInvertKey()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.iKey.wasPressedThisFrame) LookPrefs.Toggle();
        }

        // ------------------------------------------------------------------
        //  pointer lock
        // ------------------------------------------------------------------
        void HandleCapture()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // Escape frees the pointer. It does NOT leave the garage — the door
            // does that, and a key that both releases the mouse and exits the
            // room would make the mouse impossible to release.
            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCapture(false);

            // A click anywhere takes it back, unless the click landed on a
            // button. Nothing in this scene is clickable except the touch
            // layer, which is off on a machine with a mouse.
            if (!MouseCaptured && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                !FootTouchPanel.PointerOverUI)
                SetCapture(true);

            // The browser can drop the lock without telling us (tab switch,
            // Escape handled by the page). Believe Cursor, not the flag.
            if (MouseCaptured && Cursor.lockState != CursorLockMode.Locked)
                MouseCaptured = false;
        }

        void SetCapture(bool on)
        {
            MouseCaptured = on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        // ------------------------------------------------------------------
        //  look
        // ------------------------------------------------------------------
        void Look()
        {
            float dYaw = externalLook.x;
            float dPitch = -externalLook.y;
            externalLook = Vector2.zero;

            var mouse = Mouse.current;
            if (MouseCaptured && mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue();
                dYaw += d.x * mouseSensitivity;
                dPitch -= d.y * mouseSensitivity;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.rightStick.ReadValue();
                if (stick.sqrMagnitude > 0.02f)
                {
                    dYaw += stick.x * padLookSpeed * Time.deltaTime;
                    dPitch -= stick.y * padLookSpeed * Time.deltaTime;
                }
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                float k = padLookSpeed * Time.deltaTime;
                if (kb.leftArrowKey.isPressed) dYaw -= k;
                if (kb.rightArrowKey.isPressed) dYaw += k;
                if (kb.upArrowKey.isPressed) dPitch -= k;
                if (kb.downArrowKey.isPressed) dPitch += k;
            }

            // ONE place, after every source has been summed: mouse, stick,
            // arrow keys and the phone's thumb drag all pitch the same way, so
            // the setting cannot end up honoured on one device and not another.
            dPitch *= LookPrefs.PitchSign;

            yaw += dYaw;
            pitch = Mathf.Clamp(pitch + dPitch, -pitchLimit, pitchLimit);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (head != null) head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // ------------------------------------------------------------------
        //  walk
        // ------------------------------------------------------------------
        void Move()
        {
            Vector2 wish = externalMove;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed) wish.x -= 1f;
                if (kb.dKey.isPressed) wish.x += 1f;
                if (kb.wKey.isPressed) wish.y += 1f;
                if (kb.sKey.isPressed) wish.y -= 1f;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.02f) wish += stick;
            }

            if (wish.sqrMagnitude > 1f) wish.Normalize();

            Vector3 step = (transform.right * wish.x + transform.forward * wish.y) * walkSpeed;

            // Enough gravity to stay on the floor and to walk down the shallow
            // ramp at the door, and not enough to be a physics feature.
            if (body.isGrounded && fallSpeed < 0f) fallSpeed = -2f;
            fallSpeed += Physics.gravity.y * Time.deltaTime;
            step.y = fallSpeed;

            Vector3 before = transform.position;
            body.Move(step * Time.deltaTime);
            Unstick(wish, before);
        }

        /// <summary>
        /// Get the player out of the geometry when the geometry has them.
        ///
        /// A CharacterController can wedge itself into a corner it is able to
        /// enter and not able to leave — a doorframe against a skirting board
        /// does it, and the house pack has a bathroom doorway that managed it.
        /// There is no recovery from inside the game: the player has walk, look
        /// and use, and none of them help. It was reported as a soft-lock, which
        /// is exactly what it is.
        ///
        /// So: remember where they last stood while actually moving, and if they
        /// spend four seconds asking to move and going nowhere, put them back
        /// there. Not a teleport to a fixed respawn — that would drag somebody
        /// across the house for brushing a wall — just a step back to the last
        /// place that was demonstrably not a trap.
        ///
        /// The same idea the cars have had since a Bogue Banks run put one in
        /// the marsh (StuckRecovery); people on foot needed it too.
        /// </summary>
        void Unstick(Vector2 wish, Vector3 before)
        {
            bool wants = wish.sqrMagnitude > 0.04f;
            float moved = (transform.position - before).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);

            if (!wants || moved > StuckSpeed)
            {
                stuckFor = 0f;
                // Only sample somewhere we are STANDING: a spot recorded in
                // mid-air would put them back into the fall they were taking.
                sinceFreeSample += Time.deltaTime;
                if (wants && body.isGrounded && sinceFreeSample > 0.35f)
                {
                    lastFreeSpot = transform.position;
                    sinceFreeSample = 0f;
                }
                return;
            }

            stuckFor += Time.deltaTime;
            if (stuckFor < StuckSeconds) return;
            stuckFor = 0f;
            if (lastFreeSpot == Vector3.zero) return;   // never had a good spot yet

            // Disable while moving the transform: CharacterController caches its
            // own position and writing transform.position under it is ignored on
            // the next Move, which is the classic way a teleport silently
            // does nothing.
            body.enabled = false;
            transform.position = lastFreeSpot + Vector3.up * 0.05f;
            body.enabled = true;
            fallSpeed = 0f;
        }
    }
}
