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
    /// THAT "AT WORST" WAS WRONG (the replayfx round, 2026-11-26; the owner,
    /// over a night camcorder frame with a utility pole close to the lens:
    /// "During replays I can see an illuminated silhouette of cars through
    /// objects"). The bit marks every paint pixel DRAWN, and nothing clears it
    /// when a pole, a trunk, a fence or a leaf card is drawn over the car
    /// later: the car's whole silhouette on that object was handed back
    /// without its occlusion, a lighter car-shaped patch. In the chase view
    /// little stands between lens and car; a roadside camera stands behind
    /// poles and trees. <see cref="Seen"/>: after every opaque and cutout the
    /// cars' paint is drawn once more, no colour, depth-tested against the
    /// final depth (PSX/CarPaint's "PSXCarSeen" pass), and marks
    /// <see cref="SeenBit"/> only where its paint is the surface on screen;
    /// the keep passes are gated by that bit (PSX/SpeedBlur pass 5).
    ///
    /// Switch: PSX_CARDOTS=0 = the 28480caf picture (no mark, no passes).
    /// PSX_CARSEEN=0 = the cardots picture (keep where the paint was drawn).
    /// </summary>
    public static class CarDots
    {
        /// <summary>The stencil bit a car's paint writes (DamageCabin uses 64).</summary>
        public const int Bit = 128;
        /// <summary>The bit the "PSXCarSeen" pass writes: paint drawn AND nearest at the end of the opaques.</summary>
        public const int SeenBit = 32;

        public static bool On = System.Environment.GetEnvironmentVariable("PSX_CARDOTS") != "0";

        /// <summary>Keep only the paint that is SEEN (LAB "CARS: NO SHAPE THROUGH POLES"); false = the cardots marking.</summary>
        public static bool Seen = System.Environment.GetEnvironmentVariable("PSX_CARSEEN") != "0";

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
        /// <summary>PSX/SpeedBlur's keep gated by <see cref="SeenBit"/> (<see cref="Seen"/>).</summary>
        public const int ShaderPassSeen = 5;
        static KeepPass save, restore;
        static SeenPass seen;

        /// <summary>For every base camera whose frame gets the occlusion: keep
        /// the marked pixels as they were drawn.</summary>
        public static void Add(ScriptableRenderer renderer, Material material)
        {
            if (!On || material == null || !AOOverFrame()) return;
            save ??= new KeepPass(false) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents - 1, requiresIntermediateTexture = true };
            restore ??= new KeepPass(true) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 1, requiresIntermediateTexture = true };
            save.material = restore.material = material;
            int pass = Seen ? ShaderPassSeen : ShaderPass;
            save.pass = restore.pass = pass;
            if (Seen)
            {
                // (same event as the copy-out, enqueued first: URP keeps the order of equal events)
                seen ??= new SeenPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents - 1, requiresIntermediateTexture = true };
                renderer.EnqueuePass(seen);
                SeenFrames++;
            }
            renderer.EnqueuePass(save);
            renderer.EnqueuePass(restore);
            Frames++;
        }

        /// <summary>Frames the passes were enqueued for (the tools' proof).</summary>
        public static int Frames;
        /// <summary>Frames the seen-paint mark was enqueued for (the tools' proof).</summary>
        public static int SeenFrames;

        /// <summary>
        /// The cars' paint once more, after every opaque and cutout: PSX/CarPaint's
        /// "PSXCarSeen" pass (no colour, no depth write, depth-tested against the
        /// final depth) writes <see cref="SeenBit"/> where the paint is on screen.
        /// One draw per visible paint renderer, from the camera's own culling.
        /// </summary>
        class SeenPass : ScriptableRenderPass
        {
            static readonly ShaderTagId Tag = new ShaderTagId("PSXCarSeen");
            class Data { public RendererListHandle list; }

            public SeenPass() { profilingSampler = new ProfilingSampler("PSX Car Seen (mark)"); }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var colour = resources.activeColorTexture;
                var depth = resources.activeDepthTexture;
                if (!colour.IsValid() || !depth.IsValid()) return;
                var rendering = frameData.Get<UniversalRenderingData>();
                var camera = frameData.Get<UniversalCameraData>();
                var lights = frameData.Get<UniversalLightData>();
                var draw = RenderingUtils.CreateDrawingSettings(Tag, rendering, camera, lights, SortingCriteria.CommonOpaque);
                var filter = new FilteringSettings(RenderQueueRange.opaque);
                using (var builder = renderGraph.AddRasterRenderPass<Data>("PSX Car Seen (mark)", out var data))
                {
                    data.list = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, draw, filter));
                    builder.UseRendererList(data.list);
                    // (ColorMask 0: the colour is bound only so the pass shares the opaques' targets)
                    builder.SetRenderAttachment(colour, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
                }
            }
        }

        class Kept : ContextItem
        {
            public TextureHandle copy;
            public override void Reset() { copy = TextureHandle.nullHandle; }
        }

        class KeepPass : ScriptableRenderPass
        {
            public Material material;
            public int pass = ShaderPass;
            readonly bool back;
            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            class Data { public TextureHandle source; public Material material; public int pass; }

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
                    data.pass = pass;
                    builder.UseTexture(src);
                    // (the copy's untouched pixels are never read back: the same stencil gates both)
                    builder.SetRenderAttachment(dst, 0, back ? AccessFlags.ReadWrite : AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                    {
                        Block.Clear();
                        Block.SetTexture(BlitTextureId, d.source);
                        Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, Block);
                    });
                }
            }
        }
    }
}
