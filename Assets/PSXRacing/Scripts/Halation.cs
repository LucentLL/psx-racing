using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE HALATION'S GLOW, BUILT AS A REAL BLUR (2026-10-16).
    ///
    /// PSX/Blit glows round lights - the framebuffer's excess over a knee,
    /// weighted by the EMITTER MASK in its alpha. It used to gather that glow
    /// itself, for every SCREEN pixel, from sixteen taps on two rings turned
    /// by the Bayer cell. Round a small, very bright emitter close to the lens
    /// (the R32's owner-authored braking lamps, which clip at 1.8x) that kernel
    /// printed faint COPIES of the lamp in a ring round it: sparse taps show
    /// themselves whenever what they blur is smaller than the gaps between
    /// them. The owner: "Brake-light halo: this should be corrected for R32.
    /// All cars will get the similar texture overhaul like R32."
    ///
    /// Now the glow is built once a frame at about 120 lines
    /// (Hidden/PSX/Halation, Shaders/PSXHalation.shader): the excess taken
    /// per source pixel and averaged into f x f blocks, then two separable
    /// Gaussians - one as wide as the old inner ring at that level, one as
    /// wide as the outer ring at half of it - mixed 62.5 / 37.5 as the old
    /// rings' weights were. Normalised, so a lit AREA glows exactly as much as
    /// it did; round a small emitter the glow is a smooth round halo. PSX/Blit
    /// reads it with ONE bilinear tap at the source pixel's centre, so the
    /// glow still steps with the PSX pixels.
    ///
    /// It is also far cheaper: the old rings were sixteen taps for every pixel
    /// of the SCREEN (the display canvas runs PSX/Blit at device resolution,
    /// ~30M taps a frame on a 2000x923 phone); the pyramid is ~1M taps at the
    /// framebuffer's size plus one tap per screen pixel.
    ///
    /// WHO BUILDS IT. In the game, <see cref="PSXCameraOutput"/>: after the
    /// PSX camera (and the HUD camera stacked on it) has drawn the frame and
    /// before the screen camera draws the display canvas. The shot tools,
    /// which blit an arbitrary render through a copy of the display material
    /// (PSXScreenshotTool.Dithered), build it from that render with
    /// <see cref="BuildNow"/>. A display material whose glow was never built
    /// (_GlowOn 0) has no halation at all - never another frame's.
    /// </summary>
    public static class Halation
    {
        public const string ShaderName = "Hidden/PSX/Halation";
        /// <summary>The profiler sample round the passes (the cost check reads it).</summary>
        public const string Sampler = "PSX Halation";
        /// <summary>The old rings' radii, as a fraction of the picture's
        /// height (PSX/Blit's GLOW_R1 / GLOW_R2), and the inner one's share of
        /// the energy (eight taps of weight 1 out of 12.8).</summary>
        public const float InnerRing = 0.011f, OuterRing = 0.026f, InnerShare = 0.625f;
        /// <summary>The glow's own resolution, in lines, near enough: the
        /// framebuffer's height over a whole factor of 1-4.</summary>
        public const float GlowLines = 120f;

        public static readonly int GlowTexId = Shader.PropertyToID("_GlowTex");
        public static readonly int GlowOnId = Shader.PropertyToID("_GlowOn");
        static readonly int GradeId = Shader.PropertyToID("_Grade");
        static readonly int GlowOffId = Shader.PropertyToID("_GlowOff");
        static readonly int StepId = Shader.PropertyToID("_PSXHalStep");
        static readonly int MixId = Shader.PropertyToID("_PSXHalMix");
        static readonly int FactorId = Shader.PropertyToID("_PSXHalFactor");
        static readonly int SrcId = Shader.PropertyToID("_PSXHalSrc");
        static readonly int AuxId = Shader.PropertyToID("_PSXHalAux");
        static readonly int EId = Shader.PropertyToID("_PSXHalE");
        static readonly int TaId = Shader.PropertyToID("_PSXHalTA");
        static readonly int TbId = Shader.PropertyToID("_PSXHalTB");
        static readonly int GbId = Shader.PropertyToID("_PSXHalGB");
        const int PrefilterPass = 0, BlurPass = 1;

        static Shader shader;
        static Material material;
        static UnityEngine.Profiling.CustomSampler sampler;

        /// <summary>The passes' profiler sample, GPU time included (the play
        /// check's cost line reads it).</summary>
        public static UnityEngine.Profiling.CustomSampler Profile =>
            sampler != null ? sampler : sampler = UnityEngine.Profiling.CustomSampler.Create(Sampler, true);

        /// <summary>Source pixels per glow pixel, per axis.</summary>
        public static int Factor(int sourceHeight) =>
            Mathf.Clamp(Mathf.RoundToInt(sourceHeight / GlowLines), 1, 4);

        /// <summary>Does this display material show a halation at all? Only
        /// inside its film grade, and not with the measuring switch on.</summary>
        public static bool Wanted(Material blit) =>
            blit != null && blit.HasProperty(GradeId) && blit.GetFloat(GradeId) > 0.001f &&
            !(blit.HasProperty(GlowOffId) && blit.GetFloat(GlowOffId) > 0.5f);

        /// <summary>Make (or remake) <paramref name="glow"/> for a source of
        /// this size; true when it is a new texture.</summary>
        public static bool Ensure(ref RenderTexture glow, int sourceWidth, int sourceHeight)
        {
            int f = Factor(sourceHeight);
            int w = Mathf.Max(1, (sourceWidth + f - 1) / f), h = Mathf.Max(1, (sourceHeight + f - 1) / f);
            if (glow != null && glow.width == w && glow.height == h && glow.IsCreated()) return false;
            Release(ref glow);
            glow = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "PSXHalation",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                hideFlags = HideFlags.DontSave,
            };
            glow.Create();
            return true;
        }

        public static void Release(ref RenderTexture glow)
        {
            if (glow == null) return;
            glow.Release();
            if (Application.isPlaying) Object.Destroy(glow);
            else Object.DestroyImmediate(glow);
            glow = null;
        }

        /// <summary>Point a display material at a glow (or at none).</summary>
        public static void Apply(Material blit, RenderTexture glow)
        {
            if (blit == null || !blit.HasProperty(GlowOnId)) return;
            blit.SetTexture(GlowTexId, glow);
            blit.SetFloat(GlowOnId, glow != null ? 1f : 0f);
        }

        static bool EnsureMaterial()
        {
            if (material != null) return true;
            if (shader == null) shader = Shader.Find(ShaderName);
            // No shader in this build: no halation. Never a pink frame.
            if (shader == null || !shader.isSupported) return false;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        /// <summary>
        /// Record the passes that turn <paramref name="source"/> (the frame,
        /// <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/>,
        /// emitter mask in its alpha) into <paramref name="glow"/> (made by
        /// <see cref="Ensure"/> for that size). False = nothing recorded.
        /// </summary>
        public static bool Record(CommandBuffer cmd, RenderTargetIdentifier source, int sourceWidth, int sourceHeight,
                                  RenderTexture glow)
        {
            if (cmd == null || glow == null || !EnsureMaterial()) return false;
            int f = Factor(sourceHeight);
            int aw = glow.width, ah = glow.height;
            int bw = Mathf.Max(1, (aw + 1) / 2), bh = Mathf.Max(1, (ah + 1) / 2);

            // The Gaussians, in taps of their own level. Each level's blur is
            // what is left of the ring's width after the block average and the
            // bilinear reads have spread it already (variances add): the
            // inner level is boxed by f and read back by a tent of f; the
            // outer by 2f, read back by a tent of 2f, then of f.
            float H = sourceHeight;
            float s1 = InnerRing * H / Mathf.Sqrt(2f), s2 = OuterRing * H / Mathf.Sqrt(2f);
            float sa = Mathf.Sqrt(Mathf.Max(s1 * s1 - f * f / 4f, 0.09f * f * f)) / f;
            float sb = Mathf.Sqrt(Mathf.Max(s2 * s2 - 7f * f * f / 6f, 0.36f * f * f)) / (2f * f);

            cmd.BeginSample(Profile);
            cmd.GetTemporaryRT(EId, aw, ah, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            cmd.GetTemporaryRT(TaId, aw, ah, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            cmd.GetTemporaryRT(TbId, bw, bh, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            cmd.GetTemporaryRT(GbId, bw, bh, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            // The excess, block-averaged.
            cmd.SetGlobalFloat(FactorId, f);
            cmd.SetGlobalVector(SrcId, new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
            cmd.Blit(source, EId, material, PrefilterPass);
            cmd.SetGlobalVector(MixId, new Vector4(1f, 0f, 0f, 0f));
            // The outer ring's level: across (reading the inner level two
            // texels a step, which also halves it), then down.
            cmd.SetGlobalVector(StepId, new Vector4(1f / bw, 0f, sb, 0f));
            cmd.Blit(EId, TbId, material, BlurPass);
            cmd.SetGlobalVector(StepId, new Vector4(0f, 1f / bh, sb, 0f));
            cmd.Blit(TbId, GbId, material, BlurPass);
            // The inner ring's level: across, then down with the outer added.
            cmd.SetGlobalVector(StepId, new Vector4(1f / aw, 0f, sa, 0f));
            cmd.Blit(EId, TaId, material, BlurPass);
            cmd.SetGlobalTexture(AuxId, GbId);
            cmd.SetGlobalVector(MixId, new Vector4(InnerShare, 1f - InnerShare, 0f, 0f));
            cmd.SetGlobalVector(StepId, new Vector4(0f, 1f / ah, sa, 0f));
            cmd.Blit(TaId, glow, material, BlurPass);

            cmd.ReleaseTemporaryRT(EId);
            cmd.ReleaseTemporaryRT(TaId);
            cmd.ReleaseTemporaryRT(TbId);
            cmd.ReleaseTemporaryRT(GbId);
            cmd.EndSample(Profile);
            return true;
        }

        /// <summary>
        /// For the shot tools: the glow of <paramref name="source"/>, built
        /// now, in a texture the caller releases (<see cref="Release"/>).
        /// Null when the shader is missing.
        /// </summary>
        public static RenderTexture BuildNow(Texture source)
        {
            if (source == null) return null;
            RenderTexture glow = null;
            Ensure(ref glow, source.width, source.height);
            var cmd = new CommandBuffer { name = Sampler };
            bool ok = Record(cmd, source, source.width, source.height, glow);
            if (ok) Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            if (!ok) Release(ref glow);
            return glow;
        }
    }
}
