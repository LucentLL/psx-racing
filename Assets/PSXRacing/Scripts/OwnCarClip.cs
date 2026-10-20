using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// A car's own tyre spray and smoke are not drawn inside the car
    /// (Shaders/PSXOwnCarClip.cginc; owner, 2026-10-19: "rain splash goes
    /// through car, cockpit, rear bumper, etc. Smoke does, too"). The emitter
    /// calls <see cref="Write"/> on its renderer every frame it draws: the
    /// car's frame and its BODY box - the collider widened back to the
    /// shell's real width and length (the collider is a 0.915 x 0.955 fit,
    /// as CabinClip reads it) and as tall as the collider, so a puff rolling
    /// out from under the sills and anything above the roof still show.
    /// </summary>
    public static class OwnCarClip
    {
        static readonly int W2LId = Shader.PropertyToID("_PSXOwnW2L");
        static readonly int CentreId = Shader.PropertyToID("_PSXOwnBoxC");
        static readonly int HalfId = Shader.PropertyToID("_PSXOwnBoxH");
        static MaterialPropertyBlock block;

        /// <summary>Hand <paramref name="r"/> the box of <paramref name="car"/>'s
        /// body (its <paramref name="box"/> collider), keeping whatever else is
        /// on the renderer's property block. A null car or box turns it off.</summary>
        public static void Write(Renderer r, Transform car, BoxCollider box)
        {
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            if (car != null && box != null)
            {
                var c = box.center; var s = box.size;
                block.SetMatrix(W2LId, car.worldToLocalMatrix);
                block.SetVector(CentreId, new Vector4(c.x, c.y, c.z, 1f));
                block.SetVector(HalfId, new Vector4(s.x / 0.915f * 0.5f, s.y * 0.5f, s.z / 0.955f * 0.5f, 0f));
            }
            else block.SetVector(CentreId, Vector4.zero);
            r.SetPropertyBlock(block);
        }
    }
}
