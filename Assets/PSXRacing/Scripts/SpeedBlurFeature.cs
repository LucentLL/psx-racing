using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cam = renderingData.cameraData.camera;
            if (cam == null || renderingData.cameraData.renderType != CameraRenderType.Base) return;
            AddBlur(renderer, cam);
            AddLens(renderer, cam);
            AddGlare(renderer, cam);
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

        void AddBlur(ScriptableRenderer renderer, Camera cam)
        {
            float strength = SpeedBlur.ActiveStrength;
            if (strength < SpeedBlur.MinDrawnStrength) return;
            if (cam != SpeedBlur.ActiveCamera) return;

            if (material == null)
            {
                if (shader == null) shader = Shader.Find(SpeedBlur.ShaderName);
                // No shader in this build: no blur. Never a pink frame.
                if (shader == null || !shader.isSupported) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            material.SetVector(ParamsId, new Vector4(
                strength, SpeedBlur.ActiveInner, SpeedBlur.ActiveOuter, cam.aspect));
            material.SetVector(FocusId, SpeedBlur.ActiveFocus);
            pass.material = material;
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

        class BlurPass : ScriptableRenderPass
        {
            public Material material;

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
            static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

            const int SmearPass = 0, CarMaskPass = 1, CopyPass = 2;

            class FullFrameData
            {
                public TextureHandle source;
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
                FullFrame(renderGraph, "PSX Speed Blur (copy)", colour, copy, CopyPass, AccessFlags.Write);

                // 2 — the player's car into that alpha, against the scene's
                // own depth, so whatever stands between it and the lens still
                // cuts it.
                using (var builder = renderGraph.AddRasterRenderPass<MaskData>("PSX Speed Blur (car mask)", out var data))
                {
                    var draw = RenderingUtils.CreateDrawingSettings(CarPasses, renderingData, cameraData, lightData,
                                                                    SortingCriteria.CommonOpaque);
                    draw.overrideMaterial = material;
                    draw.overrideMaterialPassIndex = CarMaskPass;
                    var filter = new FilteringSettings(RenderQueueRange.opaque, -1, SpeedBlur.CarRenderingLayer);
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
                FullFrame(renderGraph, "PSX Speed Blur", copy, colour, SmearPass, AccessFlags.ReadWrite);
            }

            void FullFrame(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle destination,
                           int shaderPass, AccessFlags destinationAccess)
            {
                using (var builder = renderGraph.AddRasterRenderPass<FullFrameData>(name, out var data))
                {
                    data.source = source;
                    data.material = material;
                    data.pass = shaderPass;
                    builder.UseTexture(source);
                    builder.SetRenderAttachment(destination, 0, destinationAccess);
                    builder.SetRenderFunc(static (FullFrameData d, RasterGraphContext ctx) =>
                    {
                        Block.Clear();
                        Block.SetTexture(BlitTextureId, d.source);
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

            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

            // PSX/Lens's passes.
            const int LensShaderPass = 0, CopyShaderPass = 1;

            class FullFrameData
            {
                public TextureHandle source;
                public Material material;
                public int pass;
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
                // 2 — back in through the lens. ReadWrite: the shader writes
                // RGB and leaves the camera's alpha as it found it.
                FullFrame(renderGraph, "PSX Lens", copy, colour, LensShaderPass, AccessFlags.ReadWrite);
            }

            void FullFrame(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle destination,
                           int shaderPass, AccessFlags destinationAccess)
            {
                using (var builder = renderGraph.AddRasterRenderPass<FullFrameData>(name, out var data))
                {
                    data.source = source;
                    data.material = material;
                    data.pass = shaderPass;
                    builder.UseTexture(source);
                    builder.SetRenderAttachment(destination, 0, destinationAccess);
                    builder.SetRenderFunc(static (FullFrameData d, RasterGraphContext ctx) =>
                    {
                        Block.Clear();
                        Block.SetTexture(BlitTextureId, d.source);
                        Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
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
