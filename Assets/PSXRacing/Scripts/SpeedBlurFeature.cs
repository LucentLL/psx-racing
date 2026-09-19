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
    /// </summary>
    public class SpeedBlurFeature : ScriptableRendererFeature
    {
        /// <summary>PSX/SpeedBlur. A serialized reference, which is also what
        /// keeps the shader in a player build — nothing else points at it.</summary>
        public Shader shader;

        static readonly int ParamsId = Shader.PropertyToID("_PSXSpeedBlur");
        static readonly int FocusId = Shader.PropertyToID("_PSXSpeedBlurFocus");

        Material material;
        BlurPass pass;

        public override void Create()
        {
            pass = new BlurPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents,
                // The pass READS the colour it writes to, which a camera
                // drawing straight into its target cannot offer.
                requiresIntermediateTexture = true,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            float strength = SpeedBlur.ActiveStrength;
            if (strength < SpeedBlur.MinDrawnStrength) return;
            var cam = renderingData.cameraData.camera;
            if (cam == null || cam != SpeedBlur.ActiveCamera) return;
            if (renderingData.cameraData.renderType != CameraRenderType.Base) return;

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

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
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
    }
}
