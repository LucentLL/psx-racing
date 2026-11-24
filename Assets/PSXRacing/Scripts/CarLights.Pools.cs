using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 3 (the city-night round, 2026-11-23): THE ROAD IN FRONT OF EVERY CAR.
    /// The owner: "I still see headlight ... pop-in. Essentially the road is
    /// completely dark, then is illuminated as I drive closer." Only the cars in
    /// the per-pixel table (four on PC, nearest the eye) lit the road; every other
    /// car was two lens points over a dark road until it ranked in, 140-240 m off.
    ///
    /// Now every lit car's low beam is on the road from the moment the car is
    /// drawn: each headlamp's beam, evaluated on the ground under the car (the
    /// same arithmetic as PSXHeadlights.cginc with the surface facing up),
    /// splatted every frame on the GPU (Resources/NightSplat/PSXLampSplat pass 1)
    /// into a world-anchored 2 m texture round the eye, read by every PSX surface
    /// with one fetch (PSXHeadlights.cginc, PSXCarPools). No slot, no rank. A car
    /// in the table gives its pool only the share its per-pixel beam does not
    /// (1 - its weight), so the hand-over from the pool to the full beam on a
    /// flat road changes nothing. Under PSX_NIGHT3=0: off.
    /// </summary>
    public partial class CarLights
    {
        const int PoolRes = 1024;
        const float PoolTexelM = 2f;
        static RenderTexture poolTex;
        static Mesh poolMesh;
        static CommandBuffer poolCmd;
        static readonly List<Vector3> pV = new List<Vector3>();
        static readonly List<Vector4> pU0 = new List<Vector4>(), pU1 = new List<Vector4>(), pU2 = new List<Vector4>(), pU3 = new List<Vector4>();
        static readonly List<int> pI = new List<int>();
        static readonly int PoolTexId = Shader.PropertyToID("_PSXCarPool");
        static readonly int PoolSTId = Shader.PropertyToID("_PSXCarPoolST");
        static readonly int PoolColId = Shader.PropertyToID("_PSXCarPoolCol");
        static readonly int PoolBaseId = Shader.PropertyToID("_SplatBaseY");
        static readonly int SplatCosId = Shader.PropertyToID("_SplatCos");
        static Material poolMat;
        static bool? poolOk;
        /// <summary>Lamps splatted at the last push (tests, the approach tool).</summary>
        public static int PoolLamps { get; private set; }

        // this car's weight in the per-pixel table at the last push, and its ground
        [System.NonSerialized] float tableW;
        [System.NonSerialized] float poolGroundY = float.NaN, poolGroundAt = -9f;
        // (P1 11-23, "lights are flickering on ground and trees while driving") the lamps' height over
        // the ground, measured now and then; the ground itself follows the car every frame
        [System.NonSerialized] float poolLampH = float.NaN;

        static bool PoolsSupported
        {
            get
            {
                if (poolOk.HasValue) return poolOk.Value;
                bool ok = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                          && SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);
                var sh = ok ? Resources.Load<Shader>("NightSplat/PSXLampSplat") : null;
                ok = ok && sh != null && sh.isSupported;
                if (ok) poolMat = new Material(sh) { hideFlags = HideFlags.DontSave };
                poolOk = ok;
                return ok;
            }
        }

        static void PoolsOff() => Shader.SetGlobalVector(PoolSTId, Vector4.zero);

        /// <summary>Every lit car's pool (PushGlobals, after the table: <see cref="tableW"/> set).</summary>
        static void PushPools(Camera cam, float cosOuter, float cosInner, Color col)
        {
            if (!StreetLights.Night3 || !lightsOn || cam == null || sorted.Count == 0 || !PoolsSupported) { PoolLamps = 0; PoolsOff(); return; }
            Vector3 eye = cam.transform.position;
            float span = PoolRes * PoolTexelM;
            var origin = new Vector2(Mathf.Round(eye.x / PoolTexelM) * PoolTexelM - span * 0.5f, Mathf.Round(eye.z / PoolTexelM) * PoolTexelM - span * 0.5f);
            float baseY = Mathf.Round(eye.y / 16f) * 16f;
            pV.Clear(); pU0.Clear(); pU1.Clear(); pU2.Clear(); pU3.Clear(); pI.Clear();
            float tanO = Mathf.Tan(BeamOuterDeg * Mathf.Deg2Rad);
            float now = Time.unscaledTime;
            float far2 = (span * 0.5f) * (span * 0.5f);
            int lamps = 0;
            for (int c = 0; c < sorted.Count; c++)
            {
                var l = sorted[c];
                float w = 1f - Mathf.Clamp01(l.tableW);
                if (w <= 0.001f) continue;
                Vector3 cp = l.transform.position;
                if ((cp - eye).sqrMagnitude > far2) continue;
                // the ground under the car (a ray now and then: layer 2 is the cars)
                var top = l.beams[0] != null ? l.beams[0].transform.position : cp + Vector3.up;
                if (float.IsNaN(l.poolGroundY) || now - l.poolGroundAt > 0.3f || !Application.isPlaying)
                {
                    l.poolGroundAt = now;
                    l.poolGroundY = Physics.Raycast(top + Vector3.up * 0.2f, Vector3.down, out var hit, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                        ? hit.point.y : top.y - 0.65f;
                    // P1: the lamp's height over its ground, eased (a kerb or a pothole under the ray is no step)
                    float hNow = Mathf.Clamp(top.y - l.poolGroundY, 0.3f, 1.5f);
                    l.poolLampH = float.IsNaN(l.poolLampH) || !Application.isPlaying ? hNow : Mathf.Lerp(l.poolLampH, hNow, 0.35f);
                }
                // P1: the ground under the lamps THIS frame (it stood still for 0.3 s while the car climbed: the
                // pool's height gate cut the light on a grade in steps - the flicker on the ground and the trees)
                float groundNow = StreetLights.Night4 && !float.IsNaN(l.poolLampH) ? top.y - l.poolLampH : l.poolGroundY;
                // and the ground's slope under the car (its own floor): a pool on a grade lies on the grade
                var upC = l.transform.up;
                float gx = 0f, gz = 0f;
                if (StreetLights.Night4 && upC.y > 0.5f) { gx = -upC.x / upC.y; gz = -upC.z / upC.y; }
                for (int i = 0; i < 2; i++)
                {
                    if (l.beams[i] == null || l.HeadOut(i)) continue;
                    var t = l.beams[i].transform;
                    Vector3 p = t.position, f = t.forward, r = t.right;
                    Vector3 fh = new Vector3(f.x, 0f, f.z); if (fh.sqrMagnitude < 1e-6f) continue; fh.Normalize();
                    Vector3 rh = new Vector3(-fh.z, 0f, fh.x);
                    float back = 4f, ahead = BeamRange + 2f, side = BeamRange * tanO + 4f;

                    Vector2 F2 = new Vector2(fh.x, fh.z), R2 = new Vector2(rh.x, rh.z);
                    Vector2 P2 = new Vector2(p.x, p.z);
                    int v = pV.Count;
                    Vector2 c0 = P2 - F2 * back - R2 * side, c1 = P2 - F2 * back + R2 * side, c2 = P2 + F2 * ahead + R2 * side, c3 = P2 + F2 * ahead - R2 * side;
                    pV.Add(new Vector3(c0.x, c0.y, 0f)); pV.Add(new Vector3(c1.x, c1.y, 0f)); pV.Add(new Vector3(c2.x, c2.y, 0f)); pV.Add(new Vector3(c3.x, c3.y, 0f));
                    var oxz = l.OccOriginXZ();
                    var u0 = new Vector4(p.x, p.y, p.z, oxz.x);   // w: the fan's origin x (4b; the range is _SplatCos.z)
                    // w: the ground plane's slopes (P1; the beam's two cosines are the material's _SplatCos)
                    var u1 = new Vector4(f.x, f.y, f.z, gx);
                    var u2 = new Vector4(r.x, r.y, r.z, gz);
                    // y: the ground under the lamp, z: NIGHT 4's row (-1 none)
                    var u3 = new Vector4(w, groundNow, l.OccV, oxz.y);   // w: the fan's origin z
                    for (int k = 0; k < 4; k++) { pU0.Add(u0); pU1.Add(u1); pU2.Add(u2); pU3.Add(u3); }
                    pI.Add(v); pI.Add(v + 1); pI.Add(v + 2); pI.Add(v); pI.Add(v + 2); pI.Add(v + 3);
                    lamps++;
                }
            }
            PoolLamps = lamps;
            if (lamps == 0) { PoolsOff(); return; }
            if (poolTex == null || !poolTex.IsCreated())
            {
                if (poolTex != null) Object.Destroy(poolTex);
                poolTex = new RenderTexture(PoolRes, PoolRes, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "PSXCarPools", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false, autoGenerateMips = false, hideFlags = HideFlags.DontSave,
                };
                poolTex.Create();
            }
            if (poolMesh == null) poolMesh = new Mesh { name = "PSXCarPoolSplat", hideFlags = HideFlags.DontSave };
            poolMesh.Clear();
            poolMesh.SetVertices(pV);
            poolMesh.SetUVs(0, pU0); poolMesh.SetUVs(1, pU1); poolMesh.SetUVs(2, pU2); poolMesh.SetUVs(3, pU3);
            poolMesh.SetTriangles(pI, 0, false);
            poolMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            if (poolCmd == null) poolCmd = new CommandBuffer { name = "PSX car pools" };
            poolCmd.Clear();
            poolCmd.SetRenderTarget(poolTex);
            poolCmd.ClearRenderTarget(false, true, Color.clear);
            poolCmd.SetViewProjectionMatrices(Matrix4x4.identity, StreetLights.SplatProj(span));
            poolMat.SetFloat(PoolBaseId, baseY);
            poolMat.SetVector(SplatCosId, new Vector4(cosOuter, cosInner, BeamRange, 0f));
            poolCmd.DrawMesh(poolMesh, Matrix4x4.Translate(new Vector3(-origin.x, -origin.y, 0f)), poolMat, 0, 1);
            Graphics.ExecuteCommandBuffer(poolCmd);
            Shader.SetGlobalTexture(PoolTexId, poolTex);
            Shader.SetGlobalVector(PoolSTId, new Vector4(origin.x, origin.y, 1f / span, 1f));
            PoolWindowWas(origin, span);
            Shader.SetGlobalVector(PoolColId, new Vector4(col.r, col.g, col.b, baseY));
        }
    }
}
