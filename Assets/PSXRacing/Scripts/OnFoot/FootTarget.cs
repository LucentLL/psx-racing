using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Something in the garage worth walking up to: a car in a bay, the parts
    /// rack, the tool board, the bench, the door out.
    ///
    /// Targets are found by walking a REGISTERED LIST rather than by raycasting
    /// a physics layer. Two reasons, and the second is the one that decided it:
    /// half of what is worth looking at here is spawned at runtime from the
    /// save (the cars, the crates on the rack) so it has no authored collider
    /// to hit; and a car is a four-metre object whose transform sits at its
    /// axle midpoint, so a ray that misses the bodywork by ten centimetres
    /// would find nothing at all while the player is plainly standing in front
    /// of it. A list of a dozen entries scored by angle and distance is both
    /// cheaper and far more forgiving than a cast.
    /// </summary>
    public class FootTarget : MonoBehaviour
    {
        /// <summary>Headline: what this thing IS.</summary>
        public string title = "";
        /// <summary>Second line: what the player wants to know about it
        /// without pressing anything. Condition, contents, price.</summary>
        public string detail = "";
        /// <summary>What pressing USE does, in words. Empty means this is a
        /// label rather than a control, and the prompt shows no key.</summary>
        public string action = "";
        /// <summary>How close you have to be. Generous by design — the point is
        /// standing in front of a thing, not aiming at it.</summary>
        public float range = 3.6f;

        /// <summary>What the player is actually looking AT, when that is not
        /// this object's own origin. A car's origin is between its axles, at
        /// road height; the aim point is the middle of its roof line.</summary>
        public Transform focus;

        /// <summary>Wired at spawn time by <see cref="GarageWorld"/>. A
        /// delegate rather than a serialized UnityEvent because everything in
        /// this room is built at runtime out of the save file, so there is no
        /// authored object for an inspector reference to point at.</summary>
        public System.Action onUse;

        /// <summary>A SECOND thing this object can do, on its own button.
        ///
        /// Only the cars have one, and it is there because a car is the one
        /// thing in the room you want two different verbs for: getting in it
        /// and getting under it. Everything else in here is a fixture with a
        /// single obvious purpose, and giving those a second prompt would be
        /// three lines of chrome for nothing to press.
        /// </summary>
        public string action2 = "";
        public System.Action onUse2;

        /// <summary>
        /// What the LINE-OF-SIGHT test is allowed to see through on its way to
        /// this target. Defaults to the object the hook hangs off.
        ///
        /// Every hook in the game is a child of the thing it describes, and most
        /// of those things are solid: a car carries a box collider you walk
        /// around, the pizza on the counter is a primitive with its own, a
        /// petrol pump is a prop with a mesh. A ray cast at a car's roof from
        /// beside the car hits the car — so without this the sight test would
        /// answer "no" for precisely the objects the player is standing in
        /// front of, which is the opposite of the bug it exists to fix.
        /// </summary>
        public Transform ignoreRoot;
        public Transform IgnoreRoot =>
            ignoreRoot != null ? ignoreRoot :
            transform.parent != null ? transform.parent : transform;

        /// <summary>Clear this for something meant to be reachable through
        /// geometry — a hatch you reach through, a prompt behind glass. Nothing
        /// sets it today; it is here so the next such case does not have to
        /// switch the whole test off to get itself working.</summary>
        public bool requireLineOfSight = true;

        public static readonly List<FootTarget> All = new List<FootTarget>();

        public Vector3 FocusPoint => focus != null ? focus.position : transform.position;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);
    }
}
