using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// The render-pipeline half of the speed blur: a full-frame pass on the
    /// PSX camera that smears the world radially through PSX/SpeedBlur.
    /// <see cref="SpeedBlur"/> is the game half — it decides HOW MUCH, how
    /// tight the clear tunnel is and where it points, from the car — and this
    /// only ever draws what it is told.
    ///
    /// It lives on the renderer assets (Settings/Mobile_Renderer and
    /// PC_Renderer), which every camera in the project shares, so nearly all
    /// of this file is about NOT running: it enqueues nothing unless the
    /// camera being rendered is the one SpeedBlur named AND the strength is
    /// above nothing. The rear-view mirror, the pizza camera, the garage, the
    /// scene view and every frame driven below 30 mph never see a pass.
    ///
    /// AFTER THE TRANSPARENTS, and that is the reason this is a renderer
    /// feature at all. The project is on URP: there is no GrabPass and no
    /// OnRenderImage, and the only copy of the frame URP offers a shader is
    /// the OPAQUE texture — taken before tyre smoke, glass, lamp glows, blob
    /// shadows and skid marks are drawn. A blur built on that would wipe all
    /// of those out of the edge of the frame at exactly the speed they matter.
    ///
    /// Three steps: the frame copied out with its alpha cleared; the
    /// player's car drawn into that alpha as a MASK (its opaque renderers,
    /// found by <see cref="SpeedBlur.CarRenderingLayer"/>, depth-tested
    /// against the scene); the copy smeared back in — every pixel but the
    /// car's, from every tap but the car's.
    ///
    /// The HUD is kept out of it by SpeedBlur, which moves the HUD canvas onto
    /// an overlay camera stacked on this one: a stacked camera draws after
    /// every pass of its base.
    ///
    /// THE LENS IS A SECOND PASS OF THIS FEATURE (2026-09-21): rain drops on
    /// the glass and dirt bokeh round the lights, through PSX/Lens, one event
    /// later (AfterRenderingTransparents + 1) — so the drops sit in front of
    /// a world that is already smeared, and the stacked HUD camera still
    /// draws after both. Same shape, same rule about not running: it is
    /// enqueued only for the camera <see cref="LensFx"/> names, only when
    /// there is rain or dirt to draw. It lives here rather than in a feature
    /// of its own because a new feature would have to be added, by hand, to
    /// the YAML of BOTH renderer assets (the one the editor renders with is
    /// not the one WebGL ships), and a pass here is on both already.
    ///
    /// SPEED BLUR: MOTION (2026-10-13) is the blur pass's other smear, not a
    /// fourth pass: the same copy and car mask, then PSX/SpeedBlur's pass 3
    /// along each pixel's own screen motion (see <see cref="AddMotion"/> and
    /// <see cref="MotionBlurFx"/>). It reads the DEPTH out of the camera's own
    /// depth attachment, only on the frames it runs. The lens and the glare
    /// follow it exactly as they follow the zoom.
    ///
    /// THE WORLD IN THE WATER (2026-10-17) is a fourth pass, and the only one
    /// BEFORE the transparents (<see cref="WetPass"/>, <see cref="WetReflections"/>):
    /// the wet road's screen-space reflections, on a PC, in the rain.
    /// </summary>
    public class SpeedBlurFeature : ScriptableRendererFeature
    {
        /// <summary>PSX/SpeedBlur. A serialized reference, which is also what
        /// keeps the shader in a player build — nothing else points at it.</summary>
        public Shader shader;

        static readonly int ParamsId = Shader.PropertyToID("_PSXSpeedBlur");
        static readonly int FocusId = Shader.PropertyToID("_PSXSpeedBlurFocus");
        static readonly int LensParamsId = Shader.PropertyToID("_PSXLens");
        static readonly int LensAspectId = Shader.PropertyToID("_PSXLensAspect");
        static readonly int MaskCullId = Shader.PropertyToID("_PSXMaskCull");
        static readonly int MotionReprojId = Shader.PropertyToID("_PSXMotionReproj");
        static readonly int MotionRotId = Shader.PropertyToID("_PSXMotionRot");
        static readonly int MotionProjId = Shader.PropertyToID("_PSXMotionProj");
        static readonly int MotionParamsId = Shader.PropertyToID("_PSXMotionParams");
        static readonly int MotionMiscId = Shader.PropertyToID("_PSXMotionMisc");

        Material material;
        BlurPass pass;

        /// <summary>PSX/Lens, found by name — not a serialized field like
        /// <see cref="shader"/>, because the renderer assets' YAML would have
        /// to be edited to fill one. What keeps it in a player build is its
        /// place in GraphicsSettings' always-included shaders.</summary>
        Shader lensShader;
        Material lensMaterial;
        LensPass lensPass;

        /// <summary>PSX/SunGlare (<see cref="SunGlareFx"/>), found by name and
        /// kept in the build by GraphicsSettings, for the lens's reason.</summary>
        Shader glareShader;
        Material glareMaterial;
        GlarePass glarePass;

        static readonly int GlareSunId = Shader.PropertyToID("_PSXGlareSun");
        static readonly int GlareEyeId = Shader.PropertyToID("_PSXGlareEye");
        static readonly int GlareParamsId = Shader.PropertyToID("_PSXGlareParams");
        static readonly int GlareTintId = Shader.PropertyToID("_PSXGlareTint");

        /// <summary>The glare's eye pixel the last recorded frame wrote, for the
        /// shot tools to read back (GlareShots). Never read in the game.</summary>
        public static RenderTexture GlareEyeForTools;

        /// <summary>The wet road's mirror (<see cref="WetReflections"/>).</summary>
        WetPass wetPass;

        public override void Create()
        {
            pass = new BlurPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents,
                // The pass READS the colour it writes to, which a camera
                // drawing straight into its target cannot offer.
                requiresIntermediateTexture = true,
            };
            lensPass = new LensPass
            {
                // One after the blur: the drops are on the glass, the world
                // smears behind them. Still before the stacked HUD camera,
                // which draws after every pass of this one.
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents + 1,
                requiresIntermediateTexture = true,
            };
            glarePass = new GlarePass
            {
                // After the lens: the glare is light scattered INSIDE the lens
                // and the eye, over whatever is on the glass. Still before the
                // stacked HUD camera.
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents + 2,
                requiresIntermediateTexture = true,
            };
            wetPass = new WetPass
            {
                // After the opaques and the sky, before the transparents: the
                // water mirrors the solid world, and the smoke, the glows, the
                // skid marks and the rain are drawn over it (not in it).
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox,
                // The camera's own depth attachment is READ as a texture, which
                // a camera drawing straight into its target cannot offer.
                requiresIntermediateTexture = true,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cam = renderingData.cameraData.camera;
            if (cam == null || renderingData.cameraData.renderType != CameraRenderType.Base) return;
            AddBlur(renderer, cam, ref renderingData);
            AddLens(renderer, cam);
            AddGlare(renderer, cam);
            if (WetReflections.WantedFor(cam)) renderer.EnqueuePass(wetPass);
        }

        /// <summary>
        /// The sun in the lens, for the camera <see cref="SunGlareFx"/> names.
        /// The eye's two time constants become this frame's blend factors
        /// here, so the shader needs no clock.
        /// </summary>
        void AddGlare(ScriptableRenderer renderer, Camera cam)
        {
            if (!SunGlareFx.Active || cam != SunGlareFx.Camera) return;
            if (glareMaterial == null)
            {
                if (glareShader == null) glareShader = Shader.Find(SunGlareFx.ShaderName);
                // No shader in this build: no glare. Never a pink frame.
                if (glareShader == null || !glareShader.isSupported) return;
                glareMaterial = CoreUtils.CreateEngineMaterial(glareShader);
            }
            if (!SunGlareFx.Aim(cam)) return;
            float dt = SunGlareFx.Dt;
            float seen = 1f - Mathf.Exp(-dt / SunGlareFx.SeenTau);
            float close = 1f - Mathf.Exp(-dt / SunGlareFx.CloseTau);
            float open = 1f - Mathf.Exp(-dt / SunGlareFx.OpenTau);
            glareMaterial.SetVector(GlareSunId, SunGlareFx.Sun);
            glareMaterial.SetVector(GlareEyeId, new Vector4(seen, close, open, SunGlareFx.Reset ? 1f : 0f));
            glareMaterial.SetVector(GlareParamsId, new Vector4(SunGlareFx.Strength, Mathf.Max(cam.aspect, 0.01f), 0f, 0f));
            glareMaterial.SetColor(GlareTintId, SunGlareFx.Tint);
            glarePass.material = glareMaterial;
            renderer.EnqueuePass(glarePass);
        }

        void AddBlur(ScriptableRenderer renderer, Camera cam, ref RenderingData renderingData)
        {
            if (SpeedBlur.ActiveMode == SpeedBlurMode.Motion)
            {
                AddMotion(renderer, cam, ref renderingData);
                return;
            }

            float strength = SpeedBlur.ActiveStrength;
            if (strength < SpeedBlur.MinDrawnStrength) return;
            if (cam != SpeedBlur.ActiveCamera) return;

            if (!EnsureMaterial()) return;
            material.SetVector(ParamsId, new Vector4(
                strength, SpeedBlur.ActiveInner, SpeedBlur.ActiveOuter, cam.aspect));
            material.SetVector(FocusId, SpeedBlur.ActiveFocus);
            // The zoom's mask is drawn as it always was, both faces.
            material.SetFloat(MaskCullId, (float)CullMode.Off);
            pass.material = material;
            pass.motion = false;
            // ...and of the player's car alone, as it always was.
            pass.maskLayers = SpeedBlur.CarRenderingLayer;
            pass.ConfigureInput(ScriptableRenderPassInput.None);
            renderer.EnqueuePass(pass);
        }

        bool EnsureMaterial()
        {
            if (material != null) return true;
            if (shader == null) shader = Shader.Find(SpeedBlur.ShaderName);
            // No shader in this build: no blur. Never a pink frame.
            if (shader == null || !shader.isSupported) return false;
            material = CoreUtils.CreateEngineMaterial(shader);
            return true;
        }

        /// <summary>
        /// SPEED BLUR: MOTION (<see cref="MotionBlurFx"/>): the same three
        /// steps as the zoom — copy out, the car into the alpha, smear back in
        /// — with the smear taken along each pixel's own motion on the screen,
        /// rebuilt from the DEPTH buffer and the lens's previous frame.
        ///
        /// The depth is read by this pass itself, from the camera's own depth
        /// attachment, and only on the frames it runs: the Mobile asset (what
        /// WebGL ships) has no depth texture, and turning it on in the asset
        /// would pay for a depth copy on every frame of the game, on foot and
        /// in the menus too.
        ///
        /// The car mask culls BACK faces in this mode, as the car's own paint
        /// does. Both faces (the zoom's mask) is the same set of pixels from
        /// outside the car — but from the cockpit the lens is INSIDE the
        /// shell, every face round it is a back face, and a two-sided mask
        /// would cover the whole windscreen and leave nothing to smear.
        /// </summary>
        void AddMotion(ScriptableRenderer renderer, Camera cam, ref RenderingData renderingData)
        {
            if (cam != SpeedBlur.ActiveCamera) return;
            // The history is kept on EVERY frame of this camera, smear or no
            // smear, so the first frame past 30 mph is not a cut.
            bool moved = MotionBlurFx.Step(cam, out var reproj, out var rot, out float perFrame);
            float shutter = SpeedBlur.ActiveShutter;
            if (!moved || shutter < SpeedBlur.MinDrawnShutter) return;
            if (!EnsureMaterial()) return;

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            int width = Mathf.Max(desc.width, 1), height = Mathf.Max(desc.height, 1);
            var p = cam.nonJitteredProjectionMatrix;
            int taps = Application.isMobilePlatform || height <= 300 ? MotionBlurFx.PhoneTaps : MotionBlurFx.Taps;
            material.SetMatrix(MotionReprojId, reproj);
            material.SetMatrix(MotionRotId, rot);
            // View space at depth 1 from a pixel: x = (ndc.x + m02) / m00.
            material.SetVector(MotionProjId, new Vector4(p.m00, p.m11, p.m02, p.m12));
            material.SetVector(MotionParamsId, new Vector4(
                shutter * perFrame, MotionBlurFx.MaxSmear, width / (float)height, width));
            material.SetVector(MotionMiscId, new Vector4(taps, MotionBlurFx.DebugVelocity ? 1f : 0f,
                                                         MotionBlurFx.TapSpacingPx, MotionBlurFx.MinTaps));
            material.SetFloat(MaskCullId, (float)CullMode.Back);
            pass.material = material;
            pass.motion = true;
            // EVERY car is masked in this mode (2026-10-16): a camera blur
            // smeared a rival driving alongside as if it were the verge.
            pass.maskLayers = SpeedBlur.MotionMaskLayers;
            // No ConfigureInput(Depth): the pass reads the depth attachment
            // itself (see RecordRenderGraph) — no copy, no prepass, no asset
            // flag, on either renderer.
            pass.ConfigureInput(ScriptableRenderPassInput.None);
            renderer.EnqueuePass(pass);
        }

        /// <summary>
        /// The lens, for the camera LensFx names — the PSX camera in play,
        /// the shot camera for an edit-mode tool that called
        /// <see cref="LensFx.PreviewSet"/> (a render request hands this
        /// feature that camera, so the same test covers both).
        /// </summary>
        void AddLens(ScriptableRenderer renderer, Camera cam)
        {
            if (!LensFx.Active || cam != LensFx.Camera) return;
            if (lensMaterial == null)
            {
                if (lensShader == null) lensShader = Shader.Find(LensFx.ShaderName);
                // No shader in this build: no lens. Never a pink frame.
                if (lensShader == null || !lensShader.isSupported) return;
                lensMaterial = CoreUtils.CreateEngineMaterial(lensShader);
            }
            float aspect = Mathf.Max(cam.aspect, 0.01f);
            lensMaterial.SetVector(LensParamsId, new Vector4(LensFx.Rain, LensFx.Dirt, LensFx.Flow, LensFx.Time));
            lensMaterial.SetVector(LensAspectId, new Vector4(aspect, 1f / aspect, 0f, 0f));
            lensPass.material = lensMaterial;
            lensPass.dirt = LensFx.Dirt > LensFx.MinDrawn;
            renderer.EnqueuePass(lensPass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
            CoreUtils.Destroy(lensMaterial);
            lensMaterial = null;
            CoreUtils.Destroy(glareMaterial);
            glareMaterial = null;
            glarePass?.Release();
        }

        /// <summary>
        /// THE WORLD IN THE WATER (<see cref="WetReflections"/> says why;
        /// PSXWetSSR.cginc says how). Two raster passes: the frame so far
        /// copied out (the water must read the colour it is about to write
        /// over), then the WET renderers drawn again through PSX/Lit's
        /// "PSXWetSSR" pass into the camera's colour, reading that copy and
        /// the camera's own depth attachment. The depth is bound as a texture,
        /// so this pass has no depth attachment at all: each fragment tests
        /// itself against the depth it reads. A fragment that finds nothing to
        /// mirror is discarded, so the colour it would have written is left as
        /// the first pass made it (ReadWrite).
        /// </summary>
        class WetPass : ScriptableRenderPass
        {
            static readonly ShaderTagId PassTag = new ShaderTagId(WetReflections.PassTag);
            static readonly int ColourId = Shader.PropertyToID("_PSXSSRColour");
            static readonly int DepthId = Shader.PropertyToID("_PSXSSRDepth");
            static readonly int ParamsId = Shader.PropertyToID("_PSXSSRParams");

            class WetData
            {
                public TextureHandle copy;
                public TextureHandle depth;
                public RendererListHandle wet;
                public Vector4 prms;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var colour = resources.activeColorTexture;
                var depth = resources.activeDepthTexture;
                if (!colour.IsValid() || !depth.IsValid()) return;
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                var desc = renderGraph.GetTextureDesc(colour);
                desc.name = "_PSXSSRColour";
                desc.clearBuffer = false;
                desc.msaaSamples = MSAASamples.None;
                // bilinear for the mirrored taps; the pixel's own is read at
                // its centre, which bilinear returns exactly
                desc.filterMode = FilterMode.Bilinear;
                desc.wrapMode = TextureWrapMode.Clamp;
                var copy = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(colour, copy, Vector2.one, Vector2.zero,
                    filterMode: RenderGraphUtils.BlitFilterMode.ClampNearest, passName: "PSX Wet SSR (copy)");

                using (var builder = renderGraph.AddRasterRenderPass<WetData>("PSX Wet SSR", out var data))
                {
                    var draw = RenderingUtils.CreateDrawingSettings(PassTag, renderingData, cameraData, lightData,
                                                                    SortingCriteria.CommonOpaque);
                    var filter = new FilteringSettings(RenderQueueRange.opaque, -1, WetReflections.WetRenderingLayer);
                    data.wet = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, draw, filter));
                    data.copy = copy;
                    data.depth = depth;
                    data.prms = new Vector4(WetReflections.Strength, 0f, 0f, 0f);
                    builder.UseRendererList(data.wet);
                    builder.UseTexture(copy);
                    builder.UseTexture(depth);
                    builder.SetRenderAttachment(colour, 0, AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (WetData d, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.SetGlobalTexture(ColourId, d.copy);
                        ctx.cmd.SetGlobalTexture(DepthId, d.depth);
                        ctx.cmd.SetGlobalVector(ParamsId, d.prms);
                        ctx.cmd.DrawRendererList(d.wet);
                    });
                }
            }
        }

        class BlurPass : ScriptableRenderPass
        {
            public Material material;
            /// <summary>SPEED BLUR: MOTION this frame (the smear reads the
            /// depth), else the zoom.</summary>
            public bool motion;
            /// <summary>The rendering layers drawn into the mask: the
            /// player's car for the zoom, every car for MOTION.</summary>
            public uint maskLayers = SpeedBlur.CarRenderingLayer;

            // The PSX shaders carry no LightMode tag, which URP draws as
            // SRPDefaultUnlit; the rest are here so a car dressed in a stock
            // URP material one day is still found.
            static readonly List<ShaderTagId> CarPasses = new List<ShaderTagId>
            {
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
            };
            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly int MotionDepthId = Shader.PropertyToID("_PSXMotionDepth");
            static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

            const int SmearPass = 0, CarMaskPass = 1, CopyPass = 2, MotionPass = 3;

            class FullFrameData
            {
                public TextureHandle source;
                public TextureHandle depth;
                public Material material;
                public int pass;
            }

            class MaskData { public RendererListHandle car; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (material == null || resources.isActiveTargetBackBuffer) return;
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                var colour = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(colour);
                desc.name = "_PSXSpeedBlurSource";
                desc.clearBuffer = false;
                // The copy carries the car mask in its alpha, so it needs one.
                // The PSX camera's buffer has (SpeedBlur turns HDR off on it);
                // an HDR camera's packed float format would not.
                if (!GraphicsFormatUtility.HasAlphaChannel(desc.format))
                    desc.format = GraphicsFormat.R16G16B16A16_SFloat;
                var copy = renderGraph.CreateTexture(desc);

                // 1 — copy out, alpha cleared. Swapping resources.cameraColor
                // to a new texture instead would save the trip back, but this
                // camera is the base of a STACK: the HUD camera that draws next
                // picks up the pipeline's own colour buffer, not whatever this
                // graph was pointing at when it ended, and the blur would be
                // thrown away on exactly the frames that have one. Two full
                // passes over a 480-line buffer are nothing.
                FullFrame(renderGraph, "PSX Speed Blur (copy)", colour, copy, CopyPass, AccessFlags.Write,
                          TextureHandle.nullHandle);

                // 2 — the player's car into that alpha, against the scene's
                // own depth, so whatever stands between it and the lens still
                // cuts it.
                using (var builder = renderGraph.AddRasterRenderPass<MaskData>("PSX Speed Blur (car mask)", out var data))
                {
                    var draw = RenderingUtils.CreateDrawingSettings(CarPasses, renderingData, cameraData, lightData,
                                                                    SortingCriteria.CommonOpaque);
                    draw.overrideMaterial = material;
                    draw.overrideMaterialPassIndex = CarMaskPass;
                    var filter = new FilteringSettings(RenderQueueRange.opaque, -1, maskLayers);
                    data.car = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, draw, filter));
                    builder.UseRendererList(data.car);
                    // ReadWrite: the pass writes ONE channel, and the three the
                    // copy just filled have to still be there afterwards.
                    builder.SetRenderAttachment(copy, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.car));
                }

                // 3 — smear back in. ReadWrite again: it writes RGB and leaves
                // the camera's alpha as it found it.
                if (!motion)
                {
                    FullFrame(renderGraph, "PSX Speed Blur", copy, colour, SmearPass, AccessFlags.ReadWrite,
                              TextureHandle.nullHandle);
                    return;
                }
                // MOTION reads the scene's depth straight out of the camera's
                // own depth ATTACHMENT — the buffer the world was just drawn
                // into, the one the car mask above tests against. NOT URP's
                // _CameraDepthTexture: on the PC renderer that is drawn by a
                // depth PREPASS (SSAO asks for one), and the PSX shaders have
                // no DepthOnly pass, so it came back empty — every pixel "sky",
                // no translation smear at all (SpeedBlurPreview, 2026-10-13).
                // The Mobile asset has no depth texture to begin with; reading
                // the attachment costs it no copy. Declaring the read is what
                // keeps RenderGraph from discarding the buffer before this pass.
                var depth = resources.activeDepthTexture;
                if (!depth.IsValid()) depth = resources.cameraDepthTexture;
                if (!depth.IsValid()) return;
                FullFrame(renderGraph, "PSX Motion Blur", copy, colour, MotionPass, AccessFlags.ReadWrite, depth);
            }

            void FullFrame(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle destination,
                           int shaderPass, AccessFlags destinationAccess, TextureHandle depth)
            {
                using (var builder = renderGraph.AddRasterRenderPass<FullFrameData>(name, out var data))
                {
                    data.source = source;
                    data.depth = depth;
                    data.material = material;
                    data.pass = shaderPass;
                    builder.UseTexture(source);
                    if (depth.IsValid()) builder.UseTexture(depth);
                    builder.SetRenderAttachment(destination, 0, destinationAccess);
                    builder.SetRenderFunc(static (FullFrameData d, RasterGraphContext ctx) =>
                    {
                        Block.Clear();
                        Block.SetTexture(BlitTextureId, d.source);
                        if (d.depth.IsValid()) Block.SetTexture(MotionDepthId, d.depth);
                        Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass,
                                               MeshTopology.Triangles, 3, 1, Block);
                    });
                }
            }
        }

        /// <summary>
        /// Rain on the glass and dirt that catches the lights: the frame
        /// copied out, then drawn back into the camera's own colour through
        /// PSX/Lens. The blur's shape exactly — two full-frame raster passes,
        /// the copy in a graph-owned texture, the result written back into the
        /// ACTIVE colour rather than swapped for it, for the blur's reason:
        /// the stacked HUD camera that draws next picks up the pipeline's own
        /// colour buffer, and a lens drawn anywhere else would be thrown away
        /// on exactly the frames that have a HUD. Two passes over a 480-line
        /// buffer are nothing; the shader's own cost is in its header.
        /// </summary>
        class LensPass : ScriptableRenderPass
        {
            public Material material;
            /// <summary>Dirt on the glass this frame: build what is bright
            /// near each pixel first (passes 2 and 3).</summary>
            public bool dirt;

            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly int DirtSrcId = Shader.PropertyToID("_PSXLensDirtSrc");
            static readonly int DirtBlurId = Shader.PropertyToID("_PSXLensDirtBlur");
            static readonly int BrightId = Shader.PropertyToID("_PSXLensBright");
            static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

            // PSX/Lens's passes.
            const int LensShaderPass = 0, CopyShaderPass = 1, DirtPrefilterPass = 2, DirtBlurPass = 3;

            /// <summary>The old dirt ring's radius, picture heights (PSX/Lens
            /// DIRT_RING), and its share of the weight (twelve taps of
            /// thirteen; the last was the pixel itself): the Gaussian that
            /// replaced it (2026-10-16) is as wide, RMS.</summary>
            const float DirtRing = 0.055f, DirtRingShare = 12f / 13f;

            class FullFrameData
            {
                public TextureHandle source;
                public TextureHandle bright;
                public Material material;
                public int pass;
                public Vector4 src, blur;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (material == null || resources.isActiveTargetBackBuffer) return;

                // Whatever the blur left there (or the camera, with no blur):
                // the lens sits in front of all of it.
                var colour = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(colour);
                desc.name = "_PSXLensSource";
                desc.clearBuffer = false;
                var copy = renderGraph.CreateTexture(desc);

                // 1 — copy out, as it is.
                FullFrame(renderGraph, "PSX Lens (copy)", colour, copy, CopyShaderPass, AccessFlags.Write);

                // With dirt on the glass: what is bright near each pixel, as
                // a real blur at ~60 lines (it used to be a 12-tap ring per
                // pixel, which printed copies of a small bright lamp in every
                // grime disc round it). The excess block-averaged to ~120
                // lines, then a Gaussian across (halving it) and down. Its
                // width is what is left of the old ring's after the block
                // average and the bilinear reads have spread it (variances add).
                TextureHandle bright = TextureHandle.nullHandle;
                if (dirt)
                {
                    int w = Mathf.Max(desc.width, 1), h = Mathf.Max(desc.height, 1);
                    int f = Halation.Factor(h);
                    int aw = (w + f - 1) / f, ah = (h + f - 1) / f;
                    int bw = Mathf.Max(1, (aw + 1) / 2), bh = Mathf.Max(1, (ah + 1) / 2);
                    float sigma = DirtRing * Mathf.Sqrt(DirtRingShare) * h / Mathf.Sqrt(2f);
                    float taps = Mathf.Sqrt(Mathf.Max(sigma * sigma - f * f, 0.25f * f * f)) / (2f * f);
                    var e = renderGraph.CreateTexture(SmallDesc(aw, ah, "_PSXLensDirtE"));
                    var across = renderGraph.CreateTexture(SmallDesc(bw, bh, "_PSXLensDirtT"));
                    bright = renderGraph.CreateTexture(SmallDesc(bw, bh, "_PSXLensBright"));
                    var src = new Vector4(1f / w, 1f / h, w, h);
                    FullFrame(renderGraph, "PSX Lens (dirt prefilter)", copy, e, DirtPrefilterPass, AccessFlags.Write,
                              src, new Vector4(0f, 0f, 0f, f), TextureHandle.nullHandle);
                    FullFrame(renderGraph, "PSX Lens (dirt blur across)", e, across, DirtBlurPass, AccessFlags.Write,
                              src, new Vector4(1f / bw, 0f, taps, f), TextureHandle.nullHandle);
                    FullFrame(renderGraph, "PSX Lens (dirt blur down)", across, bright, DirtBlurPass, AccessFlags.Write,
                              src, new Vector4(0f, 1f / bh, taps, f), TextureHandle.nullHandle);
                }

                // 2 — back in through the lens. ReadWrite: the shader writes
                // RGB and leaves the camera's alpha as it found it.
                FullFrame(renderGraph, "PSX Lens", copy, colour, LensShaderPass, AccessFlags.ReadWrite,
                          Vector4.zero, Vector4.zero, bright);
            }

            static TextureDesc SmallDesc(int w, int h, string name) => new TextureDesc(w, h)
            {
                name = name,
                format = GraphicsFormat.R8G8B8A8_UNorm,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = false,
            };

            void FullFrame(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle destination,
                           int shaderPass, AccessFlags destinationAccess)
                => FullFrame(renderGraph, name, source, destination, shaderPass, destinationAccess,
                             Vector4.zero, Vector4.zero, TextureHandle.nullHandle);

            void FullFrame(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle destination,
                           int shaderPass, AccessFlags destinationAccess, Vector4 src, Vector4 blur,
                           TextureHandle bright)
            {
                using (var builder = renderGraph.AddRasterRenderPass<FullFrameData>(name, out var data))
                {
                    data.source = source;
                    data.bright = bright;
                    data.material = material;
                    data.pass = shaderPass;
                    data.src = src;
                    data.blur = blur;
                    builder.UseTexture(source);
                    if (bright.IsValid()) builder.UseTexture(bright);
                    builder.SetRenderAttachment(destination, 0, destinationAccess);
                    builder.SetRenderFunc(static (FullFrameData d, RasterGraphContext ctx) =>
                    {
                        Block.Clear();
                        Block.SetTexture(BlitTextureId, d.source);
                        Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        Block.SetVector(DirtSrcId, d.src);
                        Block.SetVector(DirtBlurId, d.blur);
                        if (d.bright.IsValid()) Block.SetTexture(BrightId, d.bright);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass,
                                               MeshTopology.Triangles, 3, 1, Block);
                    });
                }
            }
        }

        /// <summary>
        /// The sun in the lens (<see cref="SunGlareFx"/>): ONE pixel that reads
        /// how much of the sun the frame shows and keeps the eye's adaptation,
        /// then the glare screen-blended straight into the camera's colour.
        /// No copy of the frame — the glare reads nothing of the picture under
        /// it, only that pixel. The eye lives in two 1x1 textures written in
        /// turn (last frame's read, this frame's written), owned here and
        /// imported into each frame's graph, so it survives from one frame to
        /// the next.
        /// </summary>
        class GlarePass : ScriptableRenderPass
        {
            public Material material;

            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly int EyePrevId = Shader.PropertyToID("_PSXGlarePrev");
            static readonly int EyeStateId = Shader.PropertyToID("_PSXGlareState");
            static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

            // PSX/SunGlare's passes.
            const int EyeShaderPass = 0, GlareShaderPass = 1;

            readonly RenderTexture[] eyeTex = new RenderTexture[2];
            readonly RTHandle[] eye = new RTHandle[2];
            int current;


            class EyeData { public TextureHandle colour, prev; public Material material; }
            class GlareData { public TextureHandle state; public Material material; }

            public void Release()
            {
                for (int i = 0; i < eye.Length; i++)
                {
                    eye[i]?.Release();
                    eye[i] = null;
                    CoreUtils.Destroy(eyeTex[i]);
                    eyeTex[i] = null;
                }
            }

            // Plain UNORM (never sRGB): the shader packs 16-bit values into
            // pairs of channels, and an sRGB write would bend them.
            RTHandle Eye(int i)
            {
                if (eye[i] == null || eyeTex[i] == null || !eyeTex[i].IsCreated())
                {
                    eye[i]?.Release();
                    CoreUtils.Destroy(eyeTex[i]);
                    eyeTex[i] = new RenderTexture(1, 1, 0, GraphicsFormat.R8G8B8A8_UNorm)
                    {
                        name = "_PSXGlareEye" + i,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    eyeTex[i].Create();
                    eye[i] = RTHandles.Alloc(eyeTex[i]);
                }
                return eye[i];
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (material == null || resources.isActiveTargetBackBuffer) return;
                var colour = resources.activeColorTexture;
                var prev = renderGraph.ImportTexture(Eye(current));
                current ^= 1;
                var next = renderGraph.ImportTexture(Eye(current));
                GlareEyeForTools = eyeTex[current];

                // 1 — the eye: one pixel, reading the frame's alpha round the
                // sun and last frame's eye.
                using (var builder = renderGraph.AddRasterRenderPass<EyeData>("PSX Sun Glare (eye)", out var data))
                {
                    data.colour = colour;
                    data.prev = prev;
                    data.material = material;
                    builder.UseTexture(colour);
                    builder.UseTexture(prev);
                    builder.SetRenderAttachment(next, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (EyeData d, RasterGraphContext ctx) =>
                    {
                        Block.Clear();
                        Block.SetTexture(BlitTextureId, d.colour);
                        Block.SetTexture(EyePrevId, d.prev);
                        Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, EyeShaderPass,
                                               MeshTopology.Triangles, 3, 1, Block);
                    });
                }

                // 2 — the glare, blended into the colour. ReadWrite: it is a
                // blend over what is there, and it leaves the alpha (the
                // emitter mask PSX/Blit's halation reads) as it found it.
                using (var builder = renderGraph.AddRasterRenderPass<GlareData>("PSX Sun Glare", out var data))
                {
                    data.state = next;
                    data.material = material;
                    builder.UseTexture(next);
                    builder.SetRenderAttachment(colour, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (GlareData d, RasterGraphContext ctx) =>
                    {
                        Block.Clear();
                        Block.SetTexture(EyeStateId, d.state);
                        Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, GlareShaderPass,
                                               MeshTopology.Triangles, 3, 1, Block);
                    });
                }
            }
        }
    }
}
