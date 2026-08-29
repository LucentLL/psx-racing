using UnityEngine;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// A flat quad that turns to face the camera and, as it turns, swaps which
    /// cell of a sprite sheet it is showing — the trick every PS1 game used for
    /// the objects it could not afford as geometry.
    ///
    /// This exists to answer a question, not to be a system: whether an engine
    /// hanging in the garage has to be a modelled LS1 or whether a photograph
    /// of one is enough. Which is why it is deliberately small, and why the
    /// answer it gives is honest — the sheet carries FOUR horizontal views, so
    /// walking round it you get front, side, rear, side, and you see exactly
    /// where the illusion holds and where it snaps.
    ///
    /// Yaw only. Pitching the quad toward the camera as well would let a player
    /// crouch and look up at a flat picture rotating to meet them, which is the
    /// one angle that gives the whole thing away; a sprite that stays upright
    /// just reads as an object you are not quite level with.
    /// </summary>
    [ExecuteAlways]
    public class AtlasBillboard : MonoBehaviour
    {
        [Tooltip("Cell size in UV — e.g. (1/3, 1/2) for a 3x2 sheet.")]
        public Vector2 cellSize = new Vector2(1f / 3f, 0.5f);

        /// <summary>
        /// UV offsets of the horizontal views, in the order the viewer meets
        /// them turning CLOCKWISE (seen from above) starting from dead ahead:
        /// front, then the side on your right, then rear, then the other side.
        /// Any count works — three views, six, eight — the arc each one owns is
        /// just 360/n. Four is what this sheet has.
        /// </summary>
        public Vector2[] viewOffsets = new Vector2[0];

        /// <summary>Which way the object is "facing" — the direction a viewer
        /// stands in to see <c>viewOffsets[0]</c>. Local to this transform's
        /// parent, so the hoist can be turned round without re-authoring the
        /// table.</summary>
        public Vector3 facing = Vector3.forward;

        MeshRenderer mr;
        MaterialPropertyBlock mpb;
        int stId;
        int shown = -1;

        void OnEnable()
        {
            mr = GetComponent<MeshRenderer>();
            stId = Shader.PropertyToID("_MainTex_ST");
            shown = -1;                       // force a write on the first frame
        }

        void LateUpdate()
        {
            if (mr == null || viewOffsets == null || viewOffsets.Length == 0) return;
            var cam = Camera.main;
#if UNITY_EDITOR
            if (cam == null) cam = Camera.current;
#endif
            if (cam == null) return;

            // Where the viewer is standing, flattened. Using the camera
            // POSITION rather than its forward vector is what makes a sprite at
            // the edge of the screen show the right side of itself: two objects
            // ten metres apart are being looked at from measurably different
            // directions even when the camera is pointed straight between them.
            Vector3 toCam = cam.transform.position - transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 1e-6f) return;
            toCam.Normalize();

            transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);

            Vector3 fwd = transform.parent != null
                ? transform.parent.TransformDirection(facing)
                : facing;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            // Signed angle from "the viewer is dead ahead" round to where they
            // actually are, 0..360 clockwise from above.
            float ang = Vector3.SignedAngle(fwd, toCam, Vector3.up);
            int n = viewOffsets.Length;
            // +0.5 of a step before the floor, so each view owns the arc
            // CENTRED on it rather than the arc starting at it — otherwise
            // every sprite is half a step stale and the front view is showing
            // while you are already past the corner.
            int i = Mathf.FloorToInt(Mathf.Repeat(ang / 360f * n + 0.5f, n));
            if (i == shown) return;
            shown = i;

            mpb ??= new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            var o = viewOffsets[i];
            mpb.SetVector(stId, new Vector4(cellSize.x, cellSize.y, o.x, o.y));
            mr.SetPropertyBlock(mpb);
        }
    }
}
