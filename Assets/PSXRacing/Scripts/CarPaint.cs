using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The numbers behind PSX/CarPaint, in one place: what the baker writes
    /// into every livery material, and the per-renderer override that keeps
    /// the WHEELS from shining like the doors.
    ///
    /// The wheels need it because of how the pack is drawn. Every model paints
    /// its wheels on a neutral patch of the SAME 128 px sheet the body uses,
    /// so body and wheels are one material, and the shader's glass mask reads
    /// darkness as glass: a black tyre would come out mirror-bright. A
    /// MaterialPropertyBlock on the wheel renderer turns the reflection down
    /// without a second material per skin (a hundred-odd more assets) and
    /// without touching the shared material, which the body is still wearing.
    /// </summary>
    public static class CarPaint
    {
        /// <summary>Sky in the paint at a grazing angle (a quarter of it
        /// face-on). 0.34 reads as gloss paint at 240 lines; higher and the
        /// car turns to chrome.</summary>
        public const float Reflect = 0.34f;
        /// <summary>Sheet luminance under which a pixel counts as glass or
        /// black trim. The pack's windows sit around 0.05-0.15; its darkest
        /// paint (graphite, midnight blue) around 0.18-0.25 and is allowed to
        /// be mirror-like, which black paint is.</summary>
        public const float GlassLum = 0.22f;
        /// <summary>Extra reflection on the glass, as a multiple of Reflect.</summary>
        public const float GlassBoost = 1.8f;
        /// <summary>Blinn-Phong exponent of the sun's highlight on the paint.</summary>
        public const float Gloss = 28f;
        public const float SpecStrength = 0.55f;

        /// <summary>What a tyre and a rim get instead: a faint sheen, no glass
        /// boost, a duller highlight.</summary>
        public const float WheelReflect = 0.06f;
        public const float WheelSpec = 0.15f;

        static readonly int ReflectId = Shader.PropertyToID("_Reflect");
        static readonly int GlassBoostId = Shader.PropertyToID("_GlassBoost");
        static readonly int SpecId = Shader.PropertyToID("_SpecStrength");
        static MaterialPropertyBlock wheelBlock;

        /// <summary>Take the shine off a wheel renderer. Safe on a renderer
        /// wearing PSX/Lit (the block's properties simply go unread) and on
        /// null.</summary>
        public static void DullWheels(Renderer r)
        {
            if (r == null) return;
            if (wheelBlock == null)
            {
                wheelBlock = new MaterialPropertyBlock();
                wheelBlock.SetFloat(ReflectId, WheelReflect);
                wheelBlock.SetFloat(GlassBoostId, 0f);
                wheelBlock.SetFloat(SpecId, WheelSpec);
            }
            r.SetPropertyBlock(wheelBlock);
        }
    }
}
