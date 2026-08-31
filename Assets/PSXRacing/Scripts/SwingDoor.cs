using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// A door leaf on a hinge that opens as somebody walks up to it and swings
    /// shut behind them.
    ///
    /// This replaces the way every shop front in the game used to handle its
    /// doors, which was to DELETE THEM. WorldKit.OpenDoors disabled the pack's
    /// Door meshes so the opening behind them was an opening — it fixed "I am
    /// unable to go inside Pizzeria" and it created the bug the owner reported
    /// next, in as many words: "the doors are missing to Pizzeria and
    /// Convenience store. They should swing open as player moves through."
    /// A doorway with no leaf in it is a hole in a wall, and the two shops in
    /// this game with real interiors were both wearing one.
    ///
    /// So the leaf stays, keeps its collider, and MOVES. Three things make that
    /// work rather than becoming a door you get shut in:
    ///
    ///   1. IT OPENS EARLY. The trigger radius is more than three metres, which
    ///      at a walk is a second and a half before you arrive — the leaf is
    ///      already out of the way by the time the doorway matters. There is no
    ///      state in which you are stopped by a door that is about to open.
    ///   2. IT OPENS AWAY FROM YOU. The side you are standing on is measured
    ///      every frame against the door's own plane and the leaf swings to the
    ///      other one, so it never sweeps through the person opening it. The
    ///      sign is LATCHED while the door is off its stop: recomputing it as
    ///      you cross the threshold would have the leaf change its mind and
    ///      close through you halfway.
    ///   3. IT WATCHES THE PLAYER, not a trigger volume. On foot that is the
    ///      walker (FirstPersonWalk.Current); in the car it is the car; and
    ///      failing both it is the camera, which is where the player is looking
    ///      from whatever else is true. A trigger volume would need a collider
    ///      on the walker AND on the car AND to survive the forecourt swapping
    ///      one for the other mid-scene.
    ///
    /// Baked by <see cref="EditorTools.WorldKit.HingeDoors"/>, which measures
    /// the hinge edge off the leaf's own mesh — nothing here is authored per
    /// shop, because the two packs that have doors do not agree about which way
    /// round they are modelled and neither of them is going to be re-exported.
    /// </summary>
    public class SwingDoor : MonoBehaviour
    {
        [Header("Baked by WorldKit.HingeDoors")]
        /// <summary>
        /// Direction from the hinge to the leaf's free edge with the door
        /// SHUT, in the BUILDING's frame — this pivot's parent.
        ///
        /// Parent-local rather than world, and that is not tidiness: the city
        /// props are baked to prefabs and then instantiated by CityProps at
        /// whatever yaw the street runs at, so a world vector would describe
        /// the door of the building as it stood on the bake turntable. It
        /// cannot be pivot-local either, because the pivot is the thing that
        /// turns.
        /// </summary>
        public Vector3 hingeToFree = Vector3.right;
        /// <summary>Normal of the shut door's plane — "through the doorway",
        /// either way along it — in the same frame and for the same reason.
        /// Which side of this the player is standing on is the whole of the
        /// open-away rule.</summary>
        public Vector3 throughNormal = Vector3.forward;

        [Header("Feel")]
        public float openAngle = 86f;
        /// <summary>Start opening at this range. Generous on purpose: see rule
        /// 1 above.</summary>
        public float openRadius = 3.4f;
        /// <summary>And do not close again until this far, so standing in the
        /// doorway does not make the leaf flutter.</summary>
        public float closeRadius = 4.6f;
        /// <summary>Degrees per second. A shop door on a closer, not a saloon
        /// door: quick to open, unhurried on the way back.</summary>
        public float openSpeed = 260f;
        public float closeSpeed = 130f;

        /// <summary>0 shut, 1 fully open.</summary>
        float t;
        /// <summary>Which way this leaf is currently swinging. Held while the
        /// door is off its stop — see rule 2.</summary>
        float sign = 1f;

        Transform watcher;
        float rewatchAt;

        /// <summary>
        /// The pivot's rotation with the door SHUT, in its parent's frame.
        ///
        /// Captured rather than assumed to be identity, and applied as a
        /// rotation ON TOP of rather than instead of: these packs arrive with
        /// the exporter's axis conversion baked into a node somewhere up the
        /// hierarchy, so writing a plain Euler onto localRotation would snap
        /// the leaf to the parent's frame the instant the door first moved —
        /// a door that jumps ninety degrees before it starts opening.
        ///
        /// LOCAL rather than world, because a prefab instantiated and THEN
        /// positioned runs Awake before it is placed, and a world rotation
        /// captured at that moment is the rotation of a building that is not
        /// standing where it will stand.
        /// </summary>
        Quaternion restLocal = Quaternion.identity;

        void Awake() => restLocal = transform.localRotation;

        /// <summary>A baked direction in world space. Flattened and normalised
        /// on the way out rather than trusted from the bake: a leaf measured
        /// off an axis-aligned box carries a little Y, and a hinge two degrees
        /// out of plumb reads as a door sinking into the floor as it opens.
        /// </summary>
        Vector3 WorldDir(Vector3 local, Vector3 fallback)
        {
            var p = transform.parent;
            Vector3 v = p != null ? p.TransformDirection(local) : local;
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : fallback;
        }

        void Update()
        {
            // Unscaled: a paused game should still finish shutting a door
            // rather than freezing it half open — and more to the point, the
            // pause menu is the one place where a door caught mid-swing would
            // sit in frame for as long as the player left it there.
            float dt = Time.unscaledDeltaTime;
            var who = Watcher();

            bool want = false;
            if (who != null)
            {
                Vector3 d = who.position - transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                // Hysteresis: open inside openRadius, stay open out to
                // closeRadius. One radius makes a player standing on the line
                // the operator of a flapping door.
                want = t > 0.001f ? dist <= closeRadius : dist <= openRadius;
                // The side, and therefore the way it swings — but only while
                // the door is shut. Latched after that (rule 2).
                if (want && t <= 0.001f)
                {
                    Vector3 n = WorldDir(throughNormal, Vector3.forward);
                    Vector3 leaf = WorldDir(hingeToFree, Vector3.right);
                    float side = Vector3.Dot(d, n) >= 0f ? 1f : -1f;
                    float k = Vector3.Dot(Vector3.Cross(Vector3.up, leaf), n);
                    sign = -side * (k >= 0f ? 1f : -1f);
                }
            }

            float target = want ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;
            float rate = (want ? openSpeed : closeSpeed) / Mathf.Max(1f, openAngle);
            t = Mathf.MoveTowards(t, target, rate * dt);

            // About WORLD UP, expressed in the parent's frame. A door hangs
            // plumb whatever the model's own axes are doing, and half of these
            // packs carry the exporter's conversion on a node above the leaf.
            var p = transform.parent;
            Vector3 axis = p != null ? p.InverseTransformDirection(Vector3.up) : Vector3.up;
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
            transform.localRotation =
                Quaternion.AngleAxis(sign * openAngle * t, axis.normalized) * restLocal;
        }

        /// <summary>
        /// Where the player is. Re-resolved a few times a second rather than
        /// cached once: the forecourt destroys and rebuilds the walker every
        /// time somebody gets out of the car, and a door holding a dead
        /// Transform is a door that never opens again.
        /// </summary>
        Transform Watcher()
        {
            if (watcher != null && Time.unscaledTime < rewatchAt) return watcher;
            rewatchAt = Time.unscaledTime + 0.25f;

            var walker = OnFoot.FirstPersonWalk.Current;
            if (walker != null && walker.isActiveAndEnabled)
                return watcher = walker.transform;

            var input = FindFirstObjectByType<PlayerCarInput>();
            if (input != null) return watcher = input.transform;

            var cam = Camera.main;
            return watcher = cam != null ? cam.transform : null;
        }
    }
}
