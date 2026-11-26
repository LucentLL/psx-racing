using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// One engine model (ENG_I4 ...) or the shared bay kit (Bay_Kit), as baked
    /// into Resources/EngineModels/&lt;KEY&gt;.prefab (EngineStandInBaker today:
    /// STAND-INs; the real engines' converter later). Every named object the
    /// fault table speaks of is its own piece, with a mesh per quality tier
    /// (HD for the PC, Low for a phone), in the ENGINE frame:
    ///
    ///   the Engine_Mount frame as the game imports it - origin on the crank
    ///   at the flywheel face, +Y along the crank to the timing end, +Z up the
    ///   bore (the banks' bisector), +X across. THE MIRROR (CarModelDef
    ///   .hasEngineMount): an OBJ import negates x, so a model brought in that
    ///   way states <see cref="exhaustSide"/> in ITS imported frame, the frame
    ///   the car's engineDownpipeSide is measured in. The stand-ins are built
    ///   in this frame directly.
    ///
    /// The game assembles the pieces a car needs (the block, the gearbox for
    /// its layout, its dress) into one mesh a car (CarEngineBay): one draw for
    /// the engine, one for the kit.
    /// </summary>
    public class EngineKitDef : MonoBehaviour
    {
        public string key;
        public bool standIn;
        /// <summary>The kit, not an engine.</summary>
        public bool isKit;
        public Material material;

        /// <summary>+1: the exhaust manifold is on +X as modelled; -1 on -X;
        /// 0: both sides (a V or a flat engine: never reflected for the exhaust).</summary>
        public int exhaustSide;

        [System.Serializable]
        public struct Piece
        {
            /// <summary>Eng_Block, Eng_Head, Eng_ExhaustManifold, Eng_Gearbox_Inline,
            /// Bay_Battery ... (the fault table's names).</summary>
            public string name;
            public Mesh hd, low;
            /// <summary>Its box in the engine frame (the kit: about its own origin).</summary>
            public Vector3 min, max;
            /// <summary>Part of the block's fit box (the clear-box check): not the
            /// gearbox, not the dress that hangs off it.</summary>
            public bool inFit;
            /// <summary>Optional: built in only when the car asks (a gearbox
            /// kind, a turbo, a supercharger, a top end, a fan, an intercooler).</summary>
            public string option;
            /// <summary>The kit: its default offset from the engine origin in the
            /// mount frame, for a transverse and a longitudinal car.</summary>
            public Vector3 defaultTransverse, defaultLongitudinal;
        }

        public Piece[] pieces;

        public int Tris(bool hd)
        {
            int n = 0;
            if (pieces != null) foreach (var p in pieces) { var m = hd ? p.hd : p.low; if (m != null) n += (int)m.GetIndexCount(0) / 3; }
            return n;
        }
    }
}
