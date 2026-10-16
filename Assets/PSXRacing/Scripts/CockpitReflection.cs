using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE CABIN IN THE WINDSCREEN (2026-10-16). The owner, on a Forza
    /// Horizon 6 cockpit clip: "I also noticed the reflection on the
    /// windshield of hands and steering wheel, nice." Glass reflects the
    /// lit cabin back at the driver: a faint ghost of the dash top, the
    /// wheel and the arms, plain against dark trees and shade, lost against
    /// a bright sky. Subtle and realistic, not a mirror.
    ///
    /// A PLANAR REFLECTION OF THE INTERIOR ONLY. The windscreen's least-
    /// squares plane (CockpitModel.Part.planeNormal, measured as the mesh
    /// loads) mirrors the view: a virtual camera at the driver's eye framing
    /// the windscreen, its view matrix times the reflection, an oblique near
    /// plane ON the glass (nothing outside the glass can appear in it), the
    /// culling inverted. No Camera and no URP camera: one command buffer
    /// draws the cabin's own renderers into a small texture (256 lines, the
    /// windscreen's own shape), no shadows, no post, no world, every
    /// <see cref="Every"/> frames, after every LateUpdate (the wheel's turn
    /// and any arm pose are this frame's). The lighting is the cabin's own
    /// (PSX/Cabin's globals as the last frame left them).
    ///
    /// WHAT IS IN IT: every renderer under the cockpit (cabin, gauges,
    /// mirror, wheel, hands, the cargo's copies) except the glass, plus
    /// anything handed to <see cref="Register"/> - the hook for the
    /// articulated arms and any later cabin part that does not live under
    /// the cockpit's transform. Parts under it are picked up by themselves.
    ///
    /// THE GLASS (PSX/CabinGlassReflect, a second renderer on the
    /// windscreen's mesh, one queue below PSX/CabinGlass so the rain drops
    /// stay on top) screen-blends it: src x (1 - dst) + dst. Light added to
    /// what is seen through the glass, so it shows where the view is dark
    /// and all but vanishes where it is bright - with no copy of the frame.
    /// Weighted by Fresnel (grazing glass reflects more), capped near 18%,
    /// four soft taps, the darks lifted (<see cref="Lift"/>). At night the
    /// night grade darkens it with the rest of the frame: near zero. The
    /// gauges' images fall at the very foot of the glass and, not being
    /// emitters in the frame's alpha, do not glint (left so).
    ///
    /// RHD: the cabin is the LHD file under a -1 x scale; the plane is
    /// measured in the cabin's frame after that scale, so the mirrored
    /// cabin gets its own (identical) plane and the mirror image is drawn
    /// from the real, mirrored meshes.
    ///
    /// COST: 14 draws, 63k tris, 768x256 - 0.2 ms a render measured in the
    /// editor (D3D12, RTX 3070: record, submit and GPU), every other frame.
    /// Off on phones (<see cref="WantedFor"/>) and with VISUALS >
    /// REFLECTIONS off: then the overlay renderer is disabled, the texture
    /// released (a phone never makes it) and the glass is exactly what it
    /// was.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class CockpitReflection : MonoBehaviour
    {
        /// <summary>The texture's height in pixels; the width follows the
        /// windscreen's own aspect, up to <see cref="MaxWidth"/>.</summary>
        public const int Height = 256, MaxWidth = 768;
        /// <summary>Re-rendered every Nth frame (the matrix the glass reads
        /// it by is set every frame, in the car's frame, so a skipped frame
        /// only lags the wheel).</summary>
        public const int Every = 2;
        /// <summary>Strength = min(Peak, Fresnel x Gain).</summary>
        public const float Gain = 1.6f, Peak = 0.18f;
        /// <summary>The cabin's darks raised (a power, in linear) before
        /// that: ours sits in its car's shade and reads near black.</summary>
        public const float Lift = 0.6f;
        /// <summary>The clip plane sits this far OUTSIDE the fitted plane
        /// (authored metres): the real glass is curved, the plane is flat.</summary>
        public const float ClipSlack = 0.02f;
        /// <summary>
        /// THE ONE LIBERTY, degrees: the mirror plane turned this much more
        /// upright about the driver's eye. The v08 screen is raked 27 deg
        /// from the horizontal, and a true mirror of it holds only the dash
        /// top and the gauges: the wheel's and hands' images land 13-24 deg
        /// above the eye, over the screen's top edge (about 8 deg). The
        /// owner liked exactly those (FH6's screen stands more upright).
        /// Turned 10 deg (the reflection a 37 deg screen gives - a common
        /// rake), the rim and the hands come down into the top of the
        /// screen, the dash top fills its foot. The angle cannot be seen;
        /// the wheel can. 0 = the physical mirror.
        /// </summary>
        public const float ArtTiltDeg = 10f;

        /// <summary>For the shot tool: force on / off; null = the rule.</summary>
        public static bool? Override;

        /// <summary>The rule: desktop with REFLECTIONS on. Phones never pay
        /// for it. Pure, for the self-test.</summary>
        public static bool WantedFor(bool mobile, bool reflectionsOn) => reflectionsOn && !mobile;

        public static bool Wanted => Override ?? WantedFor(Application.isMobilePlatform, ReflectionPrefs.On);

        static readonly List<Renderer> registered = new List<Renderer>();

        /// <summary>THE HOOK: draw this renderer in the windscreen's
        /// reflection too (e.g. articulated arms that do not live under the
        /// cockpit's transform). Idempotent; dead renderers drop out by
        /// themselves.</summary>
        public static void Register(Renderer r)
        {
            if (r != null && !registered.Contains(r)) registered.Add(r);
        }

        public static void Unregister(Renderer r) => registered.Remove(r);

        static readonly int ReflTexId = Shader.PropertyToID("_ReflTex");
        static readonly int ReflVPId = Shader.PropertyToID("_ReflVP");
        static readonly int ViewId = Shader.PropertyToID("unity_MatrixV");
        static readonly int ProjId = Shader.PropertyToID("glstate_matrix_projection");
        static readonly int VPId = Shader.PropertyToID("unity_MatrixVP");
        static readonly int CamPosId = Shader.PropertyToID("_WorldSpaceCameraPos");

        CockpitInterior owner;
        Renderer overlay;
        Material mat;
        readonly HashSet<Renderer> skip = new HashSet<Renderer>();
        readonly List<Renderer> own = new List<Renderer>();
        readonly Dictionary<Renderer, Mesh> meshOf = new Dictionary<Renderer, Mesh>();
        readonly List<Renderer> drawnList = new List<Renderer>();
        bool[] forced = new bool[32];
        Vector3 planeP, planeN;      // in this transform's frame: the eye is the origin
        Quaternion aim = Quaternion.identity;
        float fl, fr, fb, ft, near, far;
        bool fitted, drawn;
        RenderTexture rt;
        CommandBuffer cmd;
        int frame;

        public bool Active { get; private set; }
        public RenderTexture Texture => rt;
        /// <summary>Draws and triangles in the last render (the report).</summary>
        public int LastDraws { get; private set; }
        public int LastTris { get; private set; }
        /// <summary>The fitted plane's rake from the car's horizontal, degrees.</summary>
        public float RakeDeg { get; private set; }

        internal void Init(CockpitInterior ci, Renderer overlayRenderer, Material overlayMat, IEnumerable<Renderer> panes)
        {
            owner = ci;
            overlay = overlayRenderer;
            mat = overlayMat;
            skip.Clear();
            if (panes != null) foreach (var p in panes) if (p != null) skip.Add(p);
            if (overlay != null) { skip.Add(overlay); overlay.enabled = false; }
        }

        /// <summary>The cabin's renderers to draw (all but the glass).</summary>
        internal void Collect(Renderer[] rends)
        {
            own.Clear();
            if (rends == null) return;
            foreach (var r in rends) if (r != null && !skip.Contains(r)) own.Add(r);
        }

        /// <summary>Measure the plane and the frame in this transform's
        /// space (the eye at the origin) from the pane's model-space plane
        /// and points, under the model's fitted local transform.</summary>
        internal void Fit(Transform model, CockpitModel.Part pane)
        {
            fitted = false;
            if (model == null || pane == null || pane.mesh == null) return;
            var m = Matrix4x4.TRS(model.localPosition, model.localRotation, model.localScale);
            planeP = m.MultiplyPoint3x4(pane.planePoint);
            planeN = m.inverse.transpose.MultiplyVector(pane.planeNormal).normalized;
            // Toward the eye: the inside of the glass.
            if (Vector3.Dot(planeN, -planeP) < 0f) planeN = -planeN;
            RakeDeg = Vector3.Angle(planeN, Vector3.down);
            // The liberty: about the eye (the origin), more upright.
            var tilt = Quaternion.Euler(ArtTiltDeg, 0f, 0f);
            planeN = tilt * planeN;
            planeP = tilt * planeP;
            float scale = Mathf.Abs(model.localScale.y);

            // The frame: from the eye at the pane's middle, just wide enough
            // for every vertex of it (the bounds' corners if there are none).
            var b = pane.mesh.bounds;
            aim = Quaternion.LookRotation(m.MultiplyPoint3x4(b.center), Vector3.up);
            var inv = Quaternion.Inverse(aim);
            fl = fb = float.MaxValue; fr = ft = float.MinValue;
            var pts = pane.points;
            int count = pts != null && pts.Length > 0 ? pts.Length : 8;
            for (int i = 0; i < count; i++)
            {
                var c = pts != null && pts.Length > 0 ? pts[i]
                      : new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var v = inv * m.MultiplyPoint3x4(c);
                float z = Mathf.Max(0.02f * scale, v.z);
                fl = Mathf.Min(fl, v.x / z); fr = Mathf.Max(fr, v.x / z);
                fb = Mathf.Min(fb, v.y / z); ft = Mathf.Max(ft, v.y / z);
            }
            float padX = (fr - fl) * 0.02f, padY = (ft - fb) * 0.03f;
            fl -= padX; fr += padX; fb -= padY; ft += padY;
            near = 0.02f * scale;
            far = 5f * scale;
            int w = Mathf.Clamp(Mathf.RoundToInt(Height * (fr - fl) / Mathf.Max(0.01f, ft - fb)), 64, MaxWidth);
            if (rt != null && rt.width != w) { rt.Release(); DestroyImmediate(rt); rt = null; }
            if (rt == null)
                rt = new RenderTexture(w, Height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "CockpitWindscreenReflection",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1,
                    useMipMap = false,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            if (mat != null) mat.SetTexture(ReflTexId, rt);
            fitted = true;
            drawn = false;
        }

        void LateUpdate()
        {
            if (owner == null || !owner.Shown) return;
            Tick(false);
        }

        /// <summary>Set the glass's matrix and, when due (or `force`d),
        /// draw the reflection. Public for the shot tool (edit mode has no
        /// frames).</summary>
        public void Tick(bool force)
        {
            bool on = fitted && mat != null && overlay != null && Wanted;
            if (overlay != null && overlay.enabled != on) overlay.enabled = on;
            Active = on;
            if (!on)
            {
                // Off (a phone never gets here with a texture made): the
                // glass is PSX/CabinGlass alone, and the memory goes back.
                if (drawn && rt != null) rt.Release();
                drawn = false;
                return;
            }

            var t = transform;
            Vector3 eye = t.position;
            Quaternion look = t.rotation * aim;
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye, look, Vector3.one).inverse;
            Matrix4x4 proj = Matrix4x4.Frustum(fl * near, fr * near, fb * near, ft * near, near, far);
            // The glass looks its fragment up along the eye's ray: no
            // reflection in this one, and plain GL convention (a texture a
            // pipeline rendered samples the same way on every API).
            mat.SetMatrix(ReflVPId, proj * view);

            if (!force && drawn && (++frame % Every) != 0) return;
            Render(view, proj, eye, t);
            drawn = true;
        }

        void Render(Matrix4x4 view, Matrix4x4 proj, Vector3 eye, Transform t)
        {
            if (rt == null) return;
            if (!rt.IsCreated()) rt.Create();
            if (cmd == null) cmd = new CommandBuffer { name = "Cockpit windscreen reflection" };

            Vector3 n = t.TransformDirection(planeN).normalized;
            Vector3 p = t.TransformPoint(planeP);
            Matrix4x4 refl = Reflection(n, p);
            Matrix4x4 rview = view * refl;
            // The near plane ON the glass (a little outside it): only what is
            // on the cabin's side of it can show.
            float slack = ClipSlack * Mathf.Abs(t.lossyScale.y) * (owner != null ? owner.Current.scale : 1f);
            Vector3 cp = rview.MultiplyPoint3x4(p - n * slack);
            Vector3 cn = rview.MultiplyVector(n).normalized;
            Matrix4x4 oblique = Oblique(proj, new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn)));
            Matrix4x4 gpu = GL.GetGPUProjectionMatrix(oblique, true);

            cmd.Clear();
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.SetViewProjectionMatrices(rview, oblique);
            cmd.SetGlobalMatrix(ViewId, rview);
            cmd.SetGlobalMatrix(ProjId, gpu);
            cmd.SetGlobalMatrix(VPId, gpu * rview);
            // The cabin's sheen looks at the eye's mirror image.
            Vector3 eyeM = refl.MultiplyPoint3x4(eye);
            cmd.SetGlobalVector(CamPosId, new Vector4(eyeM.x, eyeM.y, eyeM.z, 1f));
            cmd.SetInvertCulling(true);
            int draws = 0, tris = 0;
            drawnList.Clear();
            for (int i = 0; i < own.Count; i++) Draw(own[i], ref draws, ref tris);
            for (int i = registered.Count - 1; i >= 0; i--)
            {
                var r = registered[i];
                if (r == null) { registered.RemoveAt(i); continue; }
                if (!own.Contains(r) && !skip.Contains(r)) Draw(r, ref draws, ref tris);
            }
            cmd.SetInvertCulling(false);
            // The cabin hides itself from every camera but the driver's
            // (forceRenderingOff, set per camera); these draws are the
            // driver's too.
            for (int i = 0; i < drawnList.Count; i++) { forced[i] = drawnList[i].forceRenderingOff; drawnList[i].forceRenderingOff = false; }
            Graphics.ExecuteCommandBuffer(cmd);
            for (int i = 0; i < drawnList.Count; i++) if (drawnList[i] != null) drawnList[i].forceRenderingOff = forced[i];
            drawnList.Clear();
            LastDraws = draws;
            LastTris = tris;
        }

        void Draw(Renderer r, ref int draws, ref int tris)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return;
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return;
            if (!meshOf.TryGetValue(r, out var mesh) || mesh == null)
            {
                var mf = r.GetComponent<MeshFilter>();
                mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                meshOf[r] = mesh;
            }
            if (mesh == null) return;
            drawnList.Add(r);
            if (drawnList.Count > forced.Length) System.Array.Resize(ref forced, drawnList.Count * 2);
            // As the renderer itself, not its mesh by matrix: its transform's
            // handedness (DrawMesh drew the RHD cabin's -1 x inside out), its
            // property block, a skinned mesh's pose.
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var m = mats[Mathf.Min(s, mats.Length - 1)];
                if (m == null) continue;
                cmd.DrawRenderer(r, m, s, 0);
                draws++; tris += (int)mesh.GetIndexCount(s) / 3;
            }
        }

        /// <summary>The reflection across the plane through `p` with unit
        /// normal `n`.</summary>
        public static Matrix4x4 Reflection(Vector3 n, Vector3 p)
        {
            float d = -Vector3.Dot(n, p);
            var m = Matrix4x4.identity;
            m.m00 = 1f - 2f * n.x * n.x; m.m01 = -2f * n.x * n.y; m.m02 = -2f * n.x * n.z; m.m03 = -2f * d * n.x;
            m.m10 = -2f * n.y * n.x; m.m11 = 1f - 2f * n.y * n.y; m.m12 = -2f * n.y * n.z; m.m13 = -2f * d * n.y;
            m.m20 = -2f * n.z * n.x; m.m21 = -2f * n.z * n.y; m.m22 = 1f - 2f * n.z * n.z; m.m23 = -2f * d * n.z;
            return m;
        }

        /// <summary>Lengyel's oblique near plane: `plane` in view space
        /// (GL convention), the camera on its negative side.</summary>
        public static Matrix4x4 Oblique(Matrix4x4 proj, Vector4 plane)
        {
            Vector4 q = proj.inverse * new Vector4(Mathf.Sign(plane.x), Mathf.Sign(plane.y), 1f, 1f);
            Vector4 c = plane * (2f / Vector4.Dot(plane, q));
            proj[2, 0] = c.x - proj[3, 0];
            proj[2, 1] = c.y - proj[3, 1];
            proj[2, 2] = c.z - proj[3, 2];
            proj[2, 3] = c.w - proj[3, 3];
            return proj;
        }

        /// <summary>The self-test's line: the rule (phones never, desktop
        /// with REFLECTIONS on), and the mirror maths (a point and its image
        /// equidistant either side of a raked plane).</summary>
        public static bool SelfCheck(out string detail)
        {
            bool rule = !WantedFor(true, true) && !WantedFor(true, false) && WantedFor(false, true) && !WantedFor(false, false);
            var n = new Vector3(0f, -0.9f, -0.42f).normalized;
            var p = new Vector3(0.1f, 0.9f, 0.6f);
            var eye = new Vector3(-0.4f, 0.94f, -0.3f);
            var img = Reflection(n, p).MultiplyPoint3x4(eye);
            float de = Vector3.Dot(eye - p, n), di = Vector3.Dot(img - p, n);
            bool maths = de > 0f && Mathf.Abs(de + di) < 1e-4f && Vector3.Cross((img - eye).normalized, n).magnitude < 1e-3f;
            detail = $"rule {rule} (phone off, desktop on with REFLECTIONS), eye {de:0.000} m inside, image {di:0.000}";
            return rule && maths;
        }

        void OnDestroy()
        {
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
            if (cmd != null) cmd.Release();
            if (mat != null) DestroyImmediate(mat);
        }
    }
}
