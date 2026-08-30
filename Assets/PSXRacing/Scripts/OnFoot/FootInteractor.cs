using System.Collections.Generic;
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
    ///
    /// Then it has to be SEEN. Angle and distance alone do not know what a wall
    /// is, and the house is two storeys with a garage under the bedrooms: a
    /// player upstairs was inside the 4.6 m range of the car below, inside the
    /// 55-degree cone when they looked at the carpet, and was offered the car —
    /// so you could jack up a car and start work on it from bed. Reported
    /// exactly that way: "I can work on my car in the garage while in the
    /// upstairs bedroom." A prompt for something you cannot see is a prompt for
    /// something that is not there.
    /// </summary>
    public class FootInteractor : MonoBehaviour
    {
        public Transform eye;

        /// <summary>Widest angle off the view centre that still counts as
        /// looking at something. 55 degrees is generous — the camera's own
        /// field of view is 60 — and generous is right for a room where the
        /// things worth using are metres apart.</summary>
        public float maxAngle = 55f;

        /// <summary>
        /// How far SHORT of the target the sight ray stops, in metres.
        ///
        /// The ray has to miss the thing it is aiming at, and not by a little.
        /// A hook hangs off the object it describes and is aimed at a point
        /// somewhere inside it — the middle of a car's roof line, the box on the
        /// counter, the anchor sunk into the work-cart the pack modelled. Its
        /// own body is skipped by <see cref="FootTarget.IgnoreRoot"/>, but the
        /// FURNITURE AROUND IT is not, and a bench hook a hand's width inside
        /// the model's cabinets would fail a sight test that insisted on
        /// reaching the exact aim point.
        ///
        /// Half a metre and change is comfortably more than any of that and
        /// comfortably less than the gap between the player and a wall they are
        /// trying to reach through: the car sits 1.5 m off the garage's side
        /// walls and a storey of house is 2.6 m thick, so nothing this is meant
        /// to catch gets through it.
        /// </summary>
        public float sightClearance = 0.6f;

        /// <summary>What counts as something to see through. Default layers, so
        /// anything deliberately parked on Ignore Raycast stays ignored.</summary>
        public LayerMask sightBlockers = Physics.DefaultRaycastLayers;

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

        FootTarget Pick() =>
            eye == null ? null : PickFrom(FootTarget.All, eye.position, eye.forward);

        /// <summary>
        /// The offer rule, over an explicit candidate list.
        ///
        /// Split out so it can be asked a question from outside play mode.
        /// FootTarget.All is filled by OnEnable, which the editor never calls
        /// for a plain MonoBehaviour — so a tool that wants to know what a
        /// player standing HERE would be offered has to hand in its own list.
        /// It matters that it is the same code: a probe that reimplemented the
        /// angle, the range and the sight test could pass on all three while
        /// the game failed, which is the only kind of test worth nothing.
        /// </summary>
        public FootTarget PickFrom(IList<FootTarget> candidates, Vector3 from, Vector3 fwd)
        {
            if (candidates == null) return null;
            FillIgnoreRoots(candidates);

            FootTarget best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                var it = candidates[i];
                if (it == null || !it.isActiveAndEnabled) continue;

                Vector3 aim = it.FocusPoint;
                Vector3 to = aim - from;
                float dist = to.magnitude;
                if (dist > it.range || dist < 0.001f) continue;

                float angle = Vector3.Angle(fwd, to);
                if (angle > maxAngle) continue;

                // Cheapest test last on purpose: this is the only one that
                // touches the physics scene, and by here the candidates are
                // down to the handful that are close and in front of you.
                if (it.requireLineOfSight && BlockedCore(from, to / dist, dist)) continue;

                // Angle in degrees plus a metre-for-eight-degrees distance
                // tiebreak. The units are arbitrary; what matters is that
                // pointing at something beats being marginally closer to
                // something else.
                float score = angle + dist * 8f;
                if (score < bestScore) { bestScore = score; best = it; }
            }
            return best;
        }

        /// <summary>
        /// Everything the sight test is allowed to see through: the body of
        /// EVERY target in the room, not just the one being asked about.
        ///
        /// The rule this encodes is that sight blocks on the ROOM and not on
        /// what is standing in it. It has to, in a garage this size. The bays
        /// are 3.1 m across with a 1.8 m car down the middle, so the parts rack,
        /// the bench and the fridge are all on the far side of a car from
        /// somewhere a player can legitimately stand — and a box collider drawn
        /// around a low sports car is a crude enough shape that "is the bench
        /// behind the car" comes out differently for two positions a step apart.
        /// That is the room arguing with you, which is the exact failure the
        /// registered-target list exists to avoid; walls and floors are the only
        /// things here that should ever take a prompt away.
        /// </summary>
        readonly List<Transform> ignoreRoots = new List<Transform>();

        void FillIgnoreRoots(IList<FootTarget> candidates)
        {
            ignoreRoots.Clear();
            if (candidates == null) return;
            for (int i = 0; i < candidates.Count; i++)
            {
                var t = candidates[i];
                if (t == null) continue;
                var r = t.IgnoreRoot;
                if (r != null && !ignoreRoots.Contains(r)) ignoreRoots.Add(r);
            }
        }
        /// <summary>Scratch buffer for the sight cast. Sixteen is far past what
        /// a room this size puts on one line; a seventeenth hit that would have
        /// blocked is lost, which errs toward offering the prompt — the same
        /// direction every other tolerance here errs in.</summary>
        static readonly RaycastHit[] sightHits = new RaycastHit[16];

        /// <summary>
        /// Is there anything solid between the eye and the thing?
        ///
        /// RaycastNonAlloc rather than a plain Raycast because the FIRST thing
        /// hit is very often something we have to forgive — the player's own
        /// capsule if the eye ever leaves it, the car a hook belongs to — and a
        /// single-hit cast that returns one of those cannot tell us whether
        /// there was a wall behind it.
        ///
        /// Takes the candidate list so a caller outside the pick loop (a probe,
        /// a test) gets the same answer the loop would: what counts as
        /// see-through depends on what else is in the room.
        /// </summary>
        public bool Blocked(FootTarget it, Vector3 from, Vector3 dir, float dist,
                            IList<FootTarget> candidates)
        {
            FillIgnoreRoots(candidates);
            if (it != null && it.IgnoreRoot != null && !ignoreRoots.Contains(it.IgnoreRoot))
                ignoreRoots.Add(it.IgnoreRoot);
            return BlockedCore(from, dir, dist);
        }

        bool BlockedCore(Vector3 from, Vector3 dir, float dist)
        {
            float len = dist - sightClearance;
            if (len <= 0.05f) return false;   // close enough to be touching it

            int n = Physics.RaycastNonAlloc(from, dir, sightHits, len,
                                            sightBlockers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = sightHits[i].collider;
                if (col == null || col == SelfBody) continue;
                bool forgiven = false;
                for (int j = 0; j < ignoreRoots.Count && !forgiven; j++)
                    forgiven = col.transform.IsChildOf(ignoreRoots[j]);
                if (!forgiven) return true;
            }
            return false;
        }

        /// <summary>The player's own capsule. A ray starting inside a convex
        /// collider does not report it, so this is belt and braces — but the eye
        /// is on a neck that leans, and a walk mode that ever moves it out of the
        /// capsule would otherwise blind the player to the whole room.
        ///
        /// Resolved on first use rather than in Awake. AddComponent does not
        /// call Awake outside play mode, and the tool that checks this rule runs
        /// there — an instrument that answers a different question from the game
        /// is worse than no instrument, and this is a two-line difference.</summary>
        Collider selfBody;
        bool selfBodyResolved;
        Collider SelfBody
        {
            get
            {
                if (!selfBodyResolved)
                {
                    selfBody = GetComponentInParent<CharacterController>();
                    selfBodyResolved = true;
                }
                return selfBody;
            }
        }
    }
}
