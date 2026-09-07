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
        /// <summary>
        /// The WORD on the button — what the thumb presses — as distinct from
        /// <see cref="action"/>, the sentence about what happens. Empty means
        /// USE.
        ///
        /// PURELY PRESENTATIONAL. <see cref="action"/> stays the one thing that
        /// decides whether there is a button at all (FootInteractor refuses to
        /// fire on an empty action; the thumb panel and the prompt line hide on
        /// it), and nothing may gate on this instead: the yard gate, the pump
        /// and the shop's carrying-state hooks all have an empty action ON
        /// PURPOSE and would change behaviour the day a verb became a second
        /// gate. Set it BESIDE the action, in the same Refresh, because the
        /// verb flips with the sentence (PICK UP becomes PUT BACK) and a verb
        /// written once at spawn is a button that lies after the first press.
        /// Reported as "picking up pizza for delivery should say Pick Up, not
        /// Use": the button read the literal USE over every counter in the game.
        ///
        /// Keep it to two words and about nine characters — TAKE KEYS, CLOCK
        /// OFF, PUT BACK are the longest in use. The thumb button is 240 canvas
        /// units at 26pt bold and its label sets no overflow mode, so a longer
        /// verb wraps inside a 96-unit-tall button. Not passed through
        /// <see cref="FirstWords"/>: a verb is already button-sized.
        /// </summary>
        public string verb = "";
        public bool HasVerb => !string.IsNullOrEmpty(verb);
        /// <summary>The button word with its fallback applied. USE is what the
        /// button said before any target could say otherwise, and it is what a
        /// target still says for the frame before its first Refresh.</summary>
        public string Verb => HasVerb ? verb : "USE";
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
        /// <summary>The word on the SECOND button. Same contract as
        /// <see cref="verb"/>, with a different fallback: the second button
        /// has always been labelled from the first two words of its sentence
        /// (INSPECT IT, CARRY ON, GET UNDER), and those read as verbs. This
        /// exists for the one that does not — "BUY AT THE COUNTER" made a
        /// button that said BUY AT. Set it only where the fallback is wrong.
        /// </summary>
        public string verb2 = "";
        public bool HasVerb2 => !string.IsNullOrEmpty(verb2);
        public string Verb2 => HasVerb2 ? verb2 : FirstWords(action2);

        /// <summary>The first two words of a prompt, upper-cased — "INSPECT
        /// THIS CAR" becomes "INSPECT THIS". Long enough to be a verb with an
        /// object, short enough to fit a thumb button at 22pt. The fallback
        /// for <see cref="Verb2"/> only: the first button's fallback is the
        /// literal USE, because its sentences ("CLOCK ON — TAKE A RUN") do not
        /// start with the word a player would press for.</summary>
        public static string FirstWords(string action)
        {
            if (string.IsNullOrEmpty(action)) return "";
            var parts = action.Split(' ');
            string s = parts.Length > 1 ? parts[0] + " " + parts[1] : parts[0];
            return s.ToUpperInvariant();
        }

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
