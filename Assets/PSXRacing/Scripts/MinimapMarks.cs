using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>The live markers over the map: rivals, the rim marks, north,
    /// and the arrow. A few dozen vertices, rebuilt every frame.</summary>
    // CanvasRenderer REQUIRED, as Image and Text declare it: Graphic only
    // adds one itself in a player, and in the editor its GetComponent returns
    // a fake null - the graphic is created, registered and never drawn.
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public class MinimapMarks : MaskableGraphic
    {
        internal HudMinimap owner;

        /// <summary>Vertices in the last mesh, for the tools.</summary>
        public int LastVerts { get; private set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            LastVerts = 0;
            if (owner == null || owner.car == null) return;
            owner.RimMarks(vh);
            owner.Rivals(vh);
            owner.Arrow(vh);
            LastVerts = vh.currentVertCount;
        }
    }
}
