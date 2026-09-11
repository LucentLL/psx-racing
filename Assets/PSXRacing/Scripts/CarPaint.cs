using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The one thing a renderer can tell PSX/CarPaint: turn the shine down.
    /// The look itself (how much sky the paint takes, the sun's sheen and
    /// highlight, the glass threshold) lives in the shader as constants, so
    /// changing it never means rebaking three hundred livery materials.
    ///
    /// The wheels need this because of how the pack is drawn. Every model
    /// paints its wheels on a neutral patch of the SAME 128 px sheet the body
    /// uses, so body and wheels are one material, and the shader's glass mask
    /// reads darkness as glass: a black tyre would come out mirror-bright. A
    /// MaterialPropertyBlock on the wheel renderer sets _Dull without a
    /// second material per skin and without touching the shared material,
    /// which the body is still wearing.
    /// </summary>
    public static class CarPaint
    {
        static readonly int DullId = Shader.PropertyToID("_Dull");
        static MaterialPropertyBlock wheelBlock;

        /// <summary>Take the shine off a wheel renderer. Safe on a renderer
        /// wearing PSX/Lit (the block's property simply goes unread) and on
        /// null.</summary>
        public static void DullWheels(Renderer r)
        {
            if (r == null) return;
            if (wheelBlock == null)
            {
                wheelBlock = new MaterialPropertyBlock();
                wheelBlock.SetFloat(DullId, 1f);
            }
            r.SetPropertyBlock(wheelBlock);
        }
    }
}
