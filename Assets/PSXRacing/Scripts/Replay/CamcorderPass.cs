using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// THE CAMCORDER's picture (<see cref="Camcorder"/>, PSX/Camcorder): the
    /// frame copied out, the camcorder's EYE (exposure and white balance,
    /// one pixel that eases toward the scene: a ping-pong pair, the sun
    /// glare's way) updated from it, and the copy drawn back into the
    /// camera's own colour as a tape. A pass of SpeedBlurFeature, for the
    /// lens's reason (a new renderer feature would have to be added by hand
    /// to both renderer assets): enqueued only for <see cref="Camcorder.Camera"/>,
    /// after the glare, before the stacked HUD camera.
    /// </summary>
    public class CamcorderPass : ScriptableRenderPass
    {
        public Material material;

        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int EyePrevId = Shader.PropertyToID("_PSXCamEyePrev");
        static readonly int EyeId = Shader.PropertyToID("_PSXCamEye");
        public static readonly int Cam0Id = Shader.PropertyToID("_PSXCam0");
        public static readonly int Cam1Id = Shader.PropertyToID("_PSXCam1");
        public static readonly int Cam2Id = Shader.PropertyToID("_PSXCam2");
        public static readonly int EyeKId = Shader.PropertyToID("_PSXCamEyeK");
        public static readonly int Cam3Id = Shader.PropertyToID("_PSXCam3");
        static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        const int EyeShaderPass = 0, PictureShaderPass = 1, CopyShaderPass = 2;

        readonly RenderTexture[] eyeTex = new RenderTexture[2];
        readonly RTHandle[] eye = new RTHandle[2];
        int current;

        /// <summary>The eye pixel the last frame wrote, for the tools.</summary>
        public static RenderTexture EyeForTools;

        class CopyData { public TextureHandle source; public Material material; }
        class EyeData { public TextureHandle source, prev; public Material material; }
        class PictureData { public TextureHandle source, eye; public Material material; }

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

        RTHandle Eye(int i)
        {
            if (eye[i] == null || eyeTex[i] == null || !eyeTex[i].IsCreated())
            {
                eye[i]?.Release();
                CoreUtils.Destroy(eyeTex[i]);
                eyeTex[i] = new RenderTexture(1, 1, 0, GraphicsFormat.R8G8B8A8_UNorm)
                {
                    name = "_PSXCamEye" + i,
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
            var desc = renderGraph.GetTextureDesc(colour);
            desc.name = "_PSXCamcorderSource";
            desc.clearBuffer = false;
            var copy = renderGraph.CreateTexture(desc);

            var prev = renderGraph.ImportTexture(Eye(current));
            current ^= 1;
            var next = renderGraph.ImportTexture(Eye(current));
            EyeForTools = eyeTex[current];

            // 1 - the frame, copied out as it is.
            using (var builder = renderGraph.AddRasterRenderPass<CopyData>("PSX Camcorder (copy)", out var data))
            {
                data.source = colour;
                data.material = material;
                builder.UseTexture(colour);
                builder.SetRenderAttachment(copy, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CopyData d, RasterGraphContext ctx) =>
                {
                    Block.Clear();
                    Block.SetTexture(BlitTextureId, d.source);
                    Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, CopyShaderPass,
                                           MeshTopology.Triangles, 3, 1, Block);
                });
            }

            // 2 - the eye: one pixel from the copy and last frame's eye.
            using (var builder = renderGraph.AddRasterRenderPass<EyeData>("PSX Camcorder (eye)", out var data))
            {
                data.source = copy;
                data.prev = prev;
                data.material = material;
                builder.UseTexture(copy);
                builder.UseTexture(prev);
                builder.SetRenderAttachment(next, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (EyeData d, RasterGraphContext ctx) =>
                {
                    Block.Clear();
                    Block.SetTexture(BlitTextureId, d.source);
                    Block.SetTexture(EyePrevId, d.prev);
                    Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, EyeShaderPass,
                                           MeshTopology.Triangles, 3, 1, Block);
                });
            }

            // 3 - the tape, written back into the camera's colour. ReadWrite:
            // RGB only, the alpha (the emitter mask) is left as it was.
            using (var builder = renderGraph.AddRasterRenderPass<PictureData>("PSX Camcorder", out var data))
            {
                data.source = copy;
                data.eye = next;
                data.material = material;
                builder.UseTexture(copy);
                builder.UseTexture(next);
                builder.SetRenderAttachment(colour, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PictureData d, RasterGraphContext ctx) =>
                {
                    Block.Clear();
                    Block.SetTexture(BlitTextureId, d.source);
                    Block.SetTexture(EyeId, d.eye);
                    Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, PictureShaderPass,
                                           MeshTopology.Triangles, 3, 1, Block);
                });
            }
        }

        // ------------------------------------------------------------------
        //  The per-frame values, set on the material by the feature
        // ------------------------------------------------------------------
        static float lastFrameTime = -1f;
        static int frameCounter;

        /// <summary>Write this frame's camcorder state onto the material.</summary>
        public static void Publish(Material m, Camera cam)
        {
            float now = Time.unscaledTime;
            float dt = lastFrameTime < 0f ? 1f / 60f : Mathf.Clamp(now - lastFrameTime, 0f, 0.25f);
            if (now != lastFrameTime) frameCounter++;
            lastFrameTime = now;
            int w = cam.pixelWidth, h = cam.pixelHeight;
            if (cam.targetTexture != null) { w = cam.targetTexture.width; h = cam.targetTexture.height; }
            float aspect = h > 0 ? (float)w / h : cam.aspect;
            m.SetVector(Cam0Id, new Vector4(now, Camcorder.Defocus, Camcorder.PanSmear, Camcorder.Tracking));
            m.SetVector(Cam1Id, new Vector4(aspect, 1f, Camcorder.Transport, Camcorder.ScanRate));
            m.SetVector(Cam2Id, new Vector4(w, h, frameCounter % 8192, h >= 480 ? 1f : 0f));
            // The eye's two time constants: exposure ~0.7 s, white balance ~2.5 s.
            float k = 1f - Mathf.Exp(-dt / 0.7f);
            float kc = 1f - Mathf.Exp(-dt / 2.5f);
            m.SetVector(Cam3Id, new Vector4(Mathf.Clamp01(Camcorder.Static), 0f, 0f, 0f));
            m.SetVector(EyeKId, new Vector4(k, kc, Camcorder.EyeReset ? 1f : 0f,
                                            Camcorder.EyeReset ? Mathf.Max(0.05f, Camcorder.EyeKick) : 1f));
            Camcorder.EyeReset = false;
        }
    }
}
