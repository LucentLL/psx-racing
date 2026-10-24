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
    ///
    /// THE CABIN'S SIZE, NOT THE BODY'S (owner, 2026-10-19: "I should see the
    /// mirrors of cars from cockpit windows ... in rare cases like muscle cars,
    /// see the body extend out"). The cockpit cuts only its own room - out to
    /// its side glass, back to its rearmost part - so the door mirrors, the
    /// door tops, the haunches and the deck show through its windows, and two
    /// KEEP boxes (<see cref="Keep"/>) hold each door mirror whole where a
    /// narrow car's mirror reaches inside that. From the seat every other
    /// panel of a closed shell faces away and is culled anyway.
    /// </summary>
    public static class CabinClip
    {
        static readonly int OwnId = Shader.PropertyToID("_PSXCabinOwn");
        static readonly int OnId = Shader.PropertyToID("_PSXCabinOn");
        static readonly int W2LId = Shader.PropertyToID("_PSXCabinW2L");
        static readonly int BoxId = Shader.PropertyToID("_PSXCabinBox");
        static readonly int[] KeepMinId = { Shader.PropertyToID("_PSXCabinKeepMin0"), Shader.PropertyToID("_PSXCabinKeepMin1") };
        static readonly int[] KeepMaxId = { Shader.PropertyToID("_PSXCabinKeepMax0"), Shader.PropertyToID("_PSXCabinKeepMax1") };
        static MaterialPropertyBlock block;

        /// <summary>The gate is open (some camera is drawing from inside).</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>World to the cut's frame, as last opened (the keep boxes
        /// are measured in it).</summary>
        public static Matrix4x4 W2L { get; private set; } = Matrix4x4.identity;

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
            Open(car.worldToLocalMatrix, cutZ, halfWidth, top, tailZ);
        }

        /// <summary>The cut in a frame of the caller's: the cockpit's is the
        /// car's rest frame riding its leaning body (CockpitInterior.Ride).</summary>
        public static void Open(Matrix4x4 w2l, float cutZ, float halfWidth, float top, float tailZ)
        {
            W2L = w2l;
            Shader.SetGlobalMatrix(W2LId, w2l);
            Shader.SetGlobalVector(BoxId, new Vector4(cutZ, halfWidth, top, tailZ));
            Shader.SetGlobalFloat(OnId, 1f);
            Keep(0, default, false);
            Keep(1, default, false);
            IsOpen = true;
        }

        /// <summary>Keep box `i` (0 or 1, the car's frame): nothing inside it
        /// is cut while the gate is open. Off = an empty box. Set AFTER
        /// <see cref="Open"/>, which empties both.</summary>
        public static void Keep(int i, Bounds b, bool on)
        {
            if (i < 0 || i > 1) return;
            Shader.SetGlobalVector(KeepMinId[i], on ? (Vector4)b.min : new Vector4(1e4f, 1e4f, 1e4f, 0f));
            Shader.SetGlobalVector(KeepMaxId[i], on ? (Vector4)b.max : new Vector4(-1e4f, -1e4f, -1e4f, 0f));
        }

        /// <summary>The cut for a body box (centre and size in the car's
        /// frame): generous above and behind, the shell's real width (the box
        /// is a 0.915 fit) plus a hand's breadth either side.</summary>
        public static void Open(Transform car, float cutZ, Vector3 boxCenter, Vector3 boxSize)
        {
            if (car == null) return;
            Open(car.worldToLocalMatrix, cutZ, boxCenter, boxSize);
        }

        public static void Open(Matrix4x4 w2l, float cutZ, Vector3 boxCenter, Vector3 boxSize)
        {
            Open(w2l, cutZ,
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
