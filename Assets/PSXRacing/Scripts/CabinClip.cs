using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE PLAYER'S OWN SHELL, CUT AT ITS WINDSCREEN (owner, 2026-10-16:
    /// "Cockpit cam does not give player's car a physical body, the cockpit
    /// shell is the physical barrier").
    ///
    /// A camera inside the car used to hide the whole shell, so nothing of the
    /// car stood between the cabin's glass and the world: a car nosed up to
    /// the bumper sat on the dashboard. Now the shell is DRAWN for that camera
    /// and cut instead (PSXCabinClip.cginc, in PSX/CarPaint and PSX/CarLens):
    /// everything behind the cut - the windscreen's base, the cowl - and inside
    /// the body goes; the bonnet, the wings and the nose stay, in their own
    /// paint, under the same sky.
    ///
    /// Only the PLAYER'S renderers cut (<see cref="Mark"/>, a property block),
    /// so a rival rubbing doors with us keeps its doors. The gate is a global,
    /// opened by whoever renders from inside (<see cref="Open"/>) and closed
    /// when that camera is done (<see cref="Close"/>).
    /// </summary>
    public static class CabinClip
    {
        static readonly int OwnId = Shader.PropertyToID("_PSXCabinOwn");
        static readonly int OnId = Shader.PropertyToID("_PSXCabinOn");
        static readonly int W2LId = Shader.PropertyToID("_PSXCabinW2L");
        static readonly int BoxId = Shader.PropertyToID("_PSXCabinBox");
        static MaterialPropertyBlock block;

        /// <summary>The gate is open (some camera is drawing from inside).</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Every material on the renderer cuts: its shader carries
        /// the _PSXCabinOwn flag (the paint and the lamp covers). Anything else
        /// under the car - a flare, a beam - is still hidden outright from a
        /// camera inside. (Asked of the material, not by shader name: a name
        /// in runtime code is a Shader.Find to the build's shader pre-flight.)</summary>
        public static bool Cuts(Renderer r)
        {
            if (r == null) return false;
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return false;
            foreach (var m in mats)
                if (m == null || !m.HasProperty(OwnId)) return false;
            return true;
        }

        /// <summary>Make the renderer the player's: it cuts while the gate is
        /// open. Keeps whatever else is on its property block (the lamps').</summary>
        public static void Mark(Renderer r)
        {
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            if (block.GetFloat(OwnId) > 0.5f) return;
            block.SetFloat(OwnId, 1f);
            r.SetPropertyBlock(block);
        }

        /// <summary>Open the gate: the marked shell is not drawn behind
        /// <paramref name="cutZ"/> (the car's own frame, metres) inside its
        /// body - <paramref name="halfWidth"/> either side, up to
        /// <paramref name="top"/>, back to <paramref name="tailZ"/>.</summary>
        public static void Open(Transform car, float cutZ, float halfWidth, float top, float tailZ)
        {
            if (car == null) return;
            Shader.SetGlobalMatrix(W2LId, car.worldToLocalMatrix);
            Shader.SetGlobalVector(BoxId, new Vector4(cutZ, halfWidth, top, tailZ));
            Shader.SetGlobalFloat(OnId, 1f);
            IsOpen = true;
        }

        /// <summary>The cut for a body box (centre and size in the car's
        /// frame): generous above and behind, the shell's real width (the box
        /// is a 0.915 fit) plus a hand's breadth either side.</summary>
        public static void Open(Transform car, float cutZ, Vector3 boxCenter, Vector3 boxSize)
        {
            Open(car, cutZ,
                 boxSize.x / 0.915f * 0.5f + 0.08f,
                 boxCenter.y + boxSize.y * 0.5f + 0.8f,
                 boxCenter.z - boxSize.z / 0.955f * 0.5f - 0.3f);
        }

        public static void Close()
        {
            if (!IsOpen) return;
            Shader.SetGlobalFloat(OnId, 0f);
            IsOpen = false;
        }
    }
}
