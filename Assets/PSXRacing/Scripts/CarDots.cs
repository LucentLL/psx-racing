using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// THE DOTS ON THE PAINT IN SHADE (the cardots round, 2026-11-23). The
    /// owner, several times: "weird shadow dots on car in shade (driving and
    /// menus)"; "Dots in shade...I see them when selecting a car to start the
    /// game. In menus. When parked in house driveway. Its been an issue for a
    /// long time".
    ///
    /// THE CAUSE: URP's screen-space ambient occlusion, as the PC profile runs
    /// it since 2026-11-02 (PlatformProfile.ApplyAO: after the opaques, from
    /// DEPTH alone, every pixel of the frame multiplied by it). It was set up
    /// for the WORLD - the ground darkening where walls, kerbs and trees meet
    /// it - and it ran on the cars' paint as well, in the driver's view, the
    /// replays and the car viewer's showroom (PerfSwitches keeps it there). A
    /// car body is a few thousand flat triangles: the normal the occlusion
    /// rebuilds from depth jumps at every facet, its blur (bilateral, by that
    /// normal) stops at every jump, and the blue-noise samples that should have
    /// been averaged away stay on the panel as single dark pixels. On a lit
    /// panel they are lost in the light; in shade, where the paint is a smooth
    /// dark gradient, they are the speckle the owner sees. The editor never
    /// showed it: ApplyAO runs in the player only, so every tool frame was
    /// drawn without the occlusion.
    ///
    /// THE FIX: the occlusion is for the world; a car carries its own (the
    /// sill-to-roof fall-off, the HD tier's baked AO, the shade under the
    /// body). Every PSX/CarPaint pixel marks one stencil bit (<see cref="Bit"/>,
    /// next to DamageCabin's 64), and around URP's occlusion pass two
    /// full-frame passes keep those pixels as they were: the frame's marked
    /// pixels are copied out just before it (AfterRenderingSkybox's last slot)
    /// and written back just after it (before the transparents, so glass,
    /// smoke and rain still draw over the car). The ground under and beside
    /// the car still darkens - the car is in the depth the occlusion reads.
    /// Only where an opaque drawn AFTER a car covers it does that pixel keep
    /// its pre-occlusion colour too: never a wrong colour, at worst a few
    /// pixels of a pole without occlusion.
    ///
    /// Switch: PSX_CARDOTS=0 = the 28480caf picture (no mark, no passes).
    /// </summary>
    public static class CarDots
    {
        /// <summary>The stencil bit a car's paint writes (DamageCabin uses 64).</summary>
        public const int Bit = 128;

        public static bool On = System.Environment.GetEnvironmentVariable("PSX_CARDOTS") != "0";

        static readonly int RefId = Shader.PropertyToID("_PSXStRef");
        static readonly int WriteId = Shader.PropertyToID("_PSXStWrite");
        static readonly int CompId = Shader.PropertyToID("_PSXStComp");
        static readonly int PassId = Shader.PropertyToID("_PSXStPass");
        static readonly int HoleId = Shader.PropertyToID("_PSXHoleMode");

        /// <summary>
        /// The mark on one PSX/CarPaint material (PSXTexDecode.Stamp: every
        /// car material in the game comes through it). Only a material with
        /// the plain stencil state - always, keep - or this mark is touched;
        /// DamageCabin's hole materials set their own and are left alone. With
        /// the switch off the mark is taken back off, so a material saved
        /// either way draws as the switch says. True when anything changed.
        /// </summary>
        public static bool Mark(Material m)
        {
            if (m == null || m.shader == null || m.shader.name != "PSX/CarPaint" || !m.HasProperty(PassId)) return false;
            if (m.HasProperty(HoleId) && m.GetFloat(HoleId) > 0.5f) return false;
            float comp = m.GetFloat(CompId), pass = m.GetFloat(PassId), rf = m.GetFloat(RefId), wr = m.GetFloat(WriteId);
            bool plain = comp == (float)CompareFunction.Always && pass == (float)StencilOp.Keep;
            bool marked = comp == (float)CompareFunction.Always && pass == (float)StencilOp.Replace && rf == Bit && wr == Bit;
            if (!plain && !marked) return false;
            if (On == marked) return false;
#if UNITY_EDITOR
            bool wasDirty = UnityEditor.EditorUtility.IsPersistent(m) && UnityEditor.EditorUtility.IsDirty(m);
#endif
            if (On)
            {
                m.SetFloat(RefId, Bit);
                m.SetFloat(WriteId, Bit);
                m.SetFloat(PassId, (float)StencilOp.Replace);
            }
            else
            {
                m.SetFloat(RefId, 0f);
                m.SetFloat(WriteId, 255f);
                m.SetFloat(PassId, (float)StencilOp.Keep);
            }
#if UNITY_EDITOR
            // a material ASSET stamped in the editor stays clean (PSXTexDecode's rule)
            if (UnityEditor.EditorUtility.IsPersistent(m) && !wasDirty) UnityEditor.EditorUtility.ClearDirty(m);
#endif
            return true;
        }

        // ------------------------------------------------------------------
        //  Is the occlusion composited over this camera's frame?
        // ------------------------------------------------------------------
        static System.Collections.Generic.List<ScriptableRendererFeature> ao;
        static System.Collections.Generic.List<object> aoSettings;
        static System.Reflection.FieldInfo afterOpaque;
        const System.Reflection.BindingFlags IF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        /// <summary>URP's SSAO is on for the camera about to draw and laid over
        /// its colour after the opaques (the PC profile's setting). PerfSwitches
        /// switches the feature per camera before this is asked.</summary>
        public static bool AOOverFrame()
        {
            if (ao == null)
            {
                ao = new System.Collections.Generic.List<ScriptableRendererFeature>();
                aoSettings = new System.Collections.Generic.List<object>();
                try
                {
                    var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    var list = urp == null ? null : typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", IF)?.GetValue(urp) as ScriptableRendererData[];
                    if (list != null)
                        foreach (var d in list)
                            if (d != null)
                                foreach (var f in d.rendererFeatures)
                                    if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion")
                                    {
                                        ao.Add(f);
                                        aoSettings.Add(f.GetType().GetField("m_Settings", IF)?.GetValue(f));
                                    }
                }
                catch (System.Exception e) { Debug.LogWarning("[CarDots] SSAO not found: " + e.Message); }
            }
            for (int i = 0; i < ao.Count; i++)
            {
                if (ao[i] == null || !ao[i].isActive) continue;
                // (the settings object is re-read: a tool may swap it - CarDotsShots.PlayerAO)
                var s = ao[i].GetType().GetField("m_Settings", IF)?.GetValue(ao[i]);
                if (s == null) continue;
                if (afterOpaque == null) afterOpaque = s.GetType().GetField("AfterOpaque", IF);
                if (afterOpaque != null && afterOpaque.GetValue(s) is bool b && b) return true;
            }
            return false;
        }

        /// <summary>Forget the cached features (a tool that swaps the pipeline asset).</summary>
        public static void Reset() { ao = null; aoSettings = null; }

        // ------------------------------------------------------------------
        //  The two passes (SpeedBlurFeature enqueues them; PSX/SpeedBlur pass 4)
        // ------------------------------------------------------------------
        public const int ShaderPass = 4;
        static KeepPass save, restore;

        /// <summary>For every base camera whose frame gets the occlusion: keep
        /// the marked pixels as they were drawn.</summary>
        public static void Add(ScriptableRenderer renderer, Material material)
        {
            if (!On || material == null || !AOOverFrame()) return;
            save ??= new KeepPass(false) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents - 1, requiresIntermediateTexture = true };
            restore ??= new KeepPass(true) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 1, requiresIntermediateTexture = true };
            save.material = restore.material = material;
            renderer.EnqueuePass(save);
            renderer.EnqueuePass(restore);
            Frames++;
        }

        /// <summary>Frames the passes were enqueued for (the tools' proof).</summary>
        public static int Frames;

        class Kept : ContextItem
        {
            public TextureHandle copy;
            public override void Reset() { copy = TextureHandle.nullHandle; }
        }

        class KeepPass : ScriptableRenderPass
        {
            public Material material;
            readonly bool back;
            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            class Data { public TextureHandle source; public Material material; }

            public KeepPass(bool back) { this.back = back; profilingSampler = new ProfilingSampler(back ? "PSX Car Keep (back)" : "PSX Car Keep (out)"); }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (material == null || resources.isActiveTargetBackBuffer) return;
                var colour = resources.activeColorTexture;
                var depth = resources.activeDepthTexture;
                if (!colour.IsValid() || !depth.IsValid()) return;
                var kept = frameData.GetOrCreate<Kept>();
                TextureHandle src, dst;
                if (!back)
                {
                    var desc = renderGraph.GetTextureDesc(colour);
                    desc.name = "_PSXCarKeep";
                    desc.clearBuffer = false;
                    desc.depthBufferBits = 0;
                    desc.msaaSamples = MSAASamples.None;
                    kept.copy = renderGraph.CreateTexture(desc);
                    src = colour; dst = kept.copy;
                }
                else
                {
                    if (!kept.copy.IsValid()) return;
                    src = kept.copy; dst = colour;
                }
                using (var builder = renderGraph.AddRasterRenderPass<Data>(back ? "PSX Car Keep (back)" : "PSX Car Keep (out)", out var data))
                {
                    data.source = src;
                    data.material = material;
                    builder.UseTexture(src);
                    // (the copy's untouched pixels are never read back: the same stencil gates both)
                    builder.SetRenderAttachment(dst, 0, back ? AccessFlags.ReadWrite : AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                    {
                        Block.Clear();
                        Block.SetTexture(BlitTextureId, d.source);
                        Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, ShaderPass, MeshTopology.Triangles, 3, 1, Block);
                    });
                }
            }
        }
    }
}
