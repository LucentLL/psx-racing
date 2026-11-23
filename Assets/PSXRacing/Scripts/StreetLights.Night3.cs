using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 3 (the city-night round, 2026-11-23). The owner, after PC59 in Uptown:
    /// "I still see headlight and streetlight pop-in. Essentially the road is
    /// completely dark, then is illuminated as I drive closer." Measured in the
    /// player (PerfTour &amp;approach=): a road patch under a lamp read dark until
    /// the camera was a few hundred metres off and then rose to its lit value: the
    /// lamp field (StreetLights.Field.cs) faded by the EYE's distance (whole to
    /// 160 m, gone by 200 m), the far field took over at 8 m texels and was gone by
    /// 900 m; every pool's brightness was a function of where the camera stood.
    ///
    /// THE STATIC LAMP FIELD. A lamp's light on the street does not depend on the
    /// eye, so it is not computed for one: every lit city-street lamp the streamed
    /// tiles hold is splatted ONCE (on the GPU, Resources/NightSplat/PSXLampSplat
    /// pass 0) into a world-anchored texture of 2 m texels whose window covers the
    /// whole of what the camera can draw (its far plane, the glow and a step of
    /// slack every way). The window moves in whole texels, so a texel's value is
    /// the same whichever window holds it: moving the window changes nothing on
    /// screen. There is NO fade by the eye's distance anywhere: the light on a
    /// patch of road is the same from the far plane to the bumper. The texture
    /// changes only when a lamp does (a tile streams in or out, at the ring's
    /// edge beyond the far plane; the hour), and is splatted again then.
    /// It replaces, while <see cref="Night3"/> is on, the near field's CPU splat,
    /// its sliced rebuilds, its eye fade, the far field, and both cross-fades
    /// (four textures and their uploads): one RGBAHalf render texture, one fetch.
    /// PSX_NIGHT3=0: c96b5178's night.
    /// </summary>
    public static partial class StreetLights
    {
        /// <summary>The round's one switch (live: the approach tool flips it in-run).</summary>
        public static bool Night3 = System.Environment.GetEnvironmentVariable("PSX_NIGHT3") != "0";

        // ---- the static field's look (live-settable: the approach tool's variants) ----
        /// <summary>Pool radius (m): a lamp's own patch of street. 28 m (the old field) lit the
        /// street evenly from lamp to lamp; his frames show patches with darker road between.</summary>
        public static float N3PoolM = 18f;
        /// <summary>Pool per lamp, x the lamp's intensity.</summary>
        public static float N3PoolGain = 2.4f;
        /// <summary>The scattered glow's radius (m) and gain: the light thrown about by the
        /// street (car sides, the lower storeys); 0.30 lit the pale plazas and pavements whole.</summary>
        public static float N3GlowM = 40f;
        public static float N3GlowGain = 0.20f;
        /// <summary>A lamp more than 15 m over its ground (a high mast) lights from this height over it.</summary>
        public const float N3MastLiftM = 12f;

        const float N3TexelM = 2f;
        const float N3StepM = 64f;
        static RenderTexture n3Tex;
        static int n3Res;
        static Mesh n3Mesh;
        static Material n3Mat;
        static CommandBuffer n3Cmd;
        static int n3Version = 1, n3MeshVersion = -1;
        static Vector2 n3Origin = new Vector2(float.NaN, float.NaN);
        static float n3BaseY = float.NaN;
        static bool n3BuiltOn;
        static Vector4 n3BuiltShape;
        static float n3MeshAt = -99f;
        static int n3Lamps, n3Mercs;
        static bool? n3Ok;
        static readonly List<Vector3> n3V = new List<Vector3>();
        static readonly List<Vector4> n3U0 = new List<Vector4>(), n3U1 = new List<Vector4>();
        static readonly List<int> n3I = new List<int>();
        static readonly int SplatRId = Shader.PropertyToID("_SplatR");

        /// <summary>Lamps in the static field (tests, the approach tool).</summary>
        public static int N3Lamps => n3Lamps;

        /// <summary>Can this device splat (a blended half-float render target)?</summary>
        static bool N3Supported
        {
            get
            {
                if (n3Ok.HasValue) return n3Ok.Value;
                bool ok = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                          && SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);
                var sh = ok ? Resources.Load<Shader>("NightSplat/PSXLampSplat") : null;
                ok = ok && sh != null && sh.isSupported;
                if (ok) n3Mat = new Material(sh) { hideFlags = HideFlags.DontSave };
                else Debug.LogWarning("StreetLights: NIGHT 3's static lamp field is not supported here - the old field draws");
                n3Ok = ok;
                return ok;
            }
        }

        /// <summary>Any field lamp changed (FarTouched's every call site: a mark, a
        /// move or dim, a removal).</summary>
        static void N3Touched() => n3Version++;

        /// <summary>The static field for an eye (PushField's NIGHT 3 branch).</summary>
        static void PushN3(Vector3 eye, Camera cam, bool mayRebuild)
        {
            // the window: everything the camera draws, the glow round it and a step of slack
            float far = cam != null ? cam.farClipPlane : 1000f;
            float reach = Mathf.Max(N3PoolM, N3GlowM);
            int res = Mathf.Clamp(Mathf.CeilToInt(2f * (Mathf.Min(far, 2000f) + reach + N3StepM) / N3TexelM / 64f) * 64, 256, 2048);
            bool remade = false;
            if (n3Tex == null || !n3Tex.IsCreated() || n3Res < res)
            {
                if (n3Tex != null) { n3Tex.Release(); Object.Destroy(n3Tex); }
                n3Res = Mathf.Max(res, n3Res);
                n3Tex = new RenderTexture(n3Res, n3Res, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "PSXLampFieldStatic", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false, autoGenerateMips = false, hideFlags = HideFlags.DontSave,
                };
                n3Tex.Create();
                remade = true;
            }
            float span = n3Res * N3TexelM;
            if (float.IsNaN(n3BaseY) || Mathf.Abs(eye.y - n3BaseY) > 300f) { n3BaseY = Mathf.Round(eye.y / 16f) * 16f; remade = true; }
            // the window steps in whole texels (N3StepM is 32 of them): a texel's value is the window's choice of nothing
            var origin = new Vector2(Mathf.Round(eye.x / N3StepM) * N3StepM - span * 0.5f, Mathf.Round(eye.z / N3StepM) * N3StepM - span * 0.5f);
            // a mirror or the reflection probe draws with the window the game's view built (it holds all they see)
            if (!mayRebuild && !remade && !float.IsNaN(n3Origin.x)) origin = n3Origin;
            var shape = new Vector4(N3PoolM, N3PoolGain, N3GlowM, N3GlowGain);
            // a lamp change (streamed tiles; the ring's edge is past the far plane) is gathered at most twice a second
            bool lampsChanged = n3MeshVersion != n3Version && (Time.unscaledTime - n3MeshAt > 0.5f || n3Mesh == null || !Application.isPlaying);
            if (lampsChanged || shape != n3BuiltShape || n3BuiltOn != StreetOn || remade || origin != n3Origin)
            {
                if (lampsChanged || shape != n3BuiltShape || remade || n3BuiltOn != StreetOn) { N3BuildMesh(); n3MeshAt = Time.unscaledTime; }
                n3BuiltShape = shape; n3BuiltOn = StreetOn; n3Origin = origin;
                N3Splat(origin, span);
            }
            FieldLamps = n3Lamps; FieldMercuryLamps = n3Mercs;
            FieldActive = n3Lamps > 0;
            if (!FieldActive) { FieldOff(); return; }
            Shader.SetGlobalTexture(FieldTexId, n3Tex);
            // w = 3: the static field (PSXLamps.cginc: no eye fade, no far field, no cross-fade)
            Shader.SetGlobalVector(FieldSTId, new Vector4(origin.x, origin.y, 1f / span, 3f));
            Shader.SetGlobalVector(FieldPrevSTId, Vector4.zero);
            FarOff();
            Shader.SetGlobalVector(FieldYId, new Vector4(n3BaseY, UptownCentre.x, UptownCentre.y, 1f / Mathf.Max(1f, UptownOuterM - UptownInnerM)));
            var w = FieldMercury; var k = FieldHueOverride.HasValue ? FieldHueOverride.Value.linear : FieldWarm.linear;
            Shader.SetGlobalVector(FieldWhiteId, new Vector4(w.r, w.g, w.b, UptownOuterM));
            Shader.SetGlobalVector(FieldWarmId, new Vector4(k.r, k.g, k.b, UptownGain));
            Shader.SetGlobalVector(BounceId, new Vector4(N3Bounce.x, N3Bounce.y, N3Bounce.z, 1f));
        }

        /// <summary>The city's bounce on a tower face under NIGHT 3 (linear): his frames'
        /// faces above the lamps read Ycode 7-21 against our beige 18-48 (CityBounce 0.075).</summary>
        public static Vector3 N3Bounce = new Vector3(0.020f, 0.019f, 0.017f);

        /// <summary>One quad per lit field lamp (all of them: the GPU drops what falls off the window).</summary>
        static void N3BuildMesh()
        {
            n3MeshVersion = n3Version;
            n3V.Clear(); n3U0.Clear(); n3U1.Clear(); n3I.Clear();
            int lamps = 0, mercs = 0;
            float reach = Mathf.Max(N3PoolM, N3GlowM) + N3TexelM;
            if (StreetOn)
                for (int i = 0; i < high; i++)
                {
                    ref Entry e = ref entries[i];
                    if (!e.used || !e.field || !e.on || e.kind != Kind.Street || e.intensity <= 0f) continue;
                    if (e.owned && e.owner == null) continue;
                    lamps++;
                    if (e.mercury) mercs++;
                    float pool = e.intensity * N3PoolGain, glow = e.intensity * N3GlowGain;
                    var u0 = new Vector4(e.pos.x, e.pos.z, 0f, 0f);
                    // a high mast (an interchange's ring of floods, 20-30 m up) lights the carriageway under
                    // it: its light is counted from N3MastLiftM over its own ground (the field's vertical reach
                    // is 16 m under a head - so a road on a deck's underside 5+ m below still takes none)
                    float hy = e.pos.y;
                    if (Application.isPlaying)
                    {
                        if (!e.groundKnown)
                        {
                            e.groundKnown = true;
                            e.ground = Physics.Raycast(e.pos + Vector3.down * 0.3f, Vector3.down, out var gh, 60f, ~(1 << 2), QueryTriggerInteraction.Ignore) ? gh.point.y : e.pos.y - 9f;
                        }
                        if (e.pos.y - e.ground > 15f) hy = e.ground + N3MastLiftM;
                    }
                    var u1 = new Vector4(pool, glow, hy - n3BaseY, e.mercury ? 1f : 0f);
                    int v = n3V.Count;
                    n3V.Add(new Vector3(e.pos.x - reach, e.pos.z - reach, 0f));
                    n3V.Add(new Vector3(e.pos.x + reach, e.pos.z - reach, 0f));
                    n3V.Add(new Vector3(e.pos.x + reach, e.pos.z + reach, 0f));
                    n3V.Add(new Vector3(e.pos.x - reach, e.pos.z + reach, 0f));
                    for (int c = 0; c < 4; c++) { n3U0.Add(u0); n3U1.Add(u1); }
                    n3I.Add(v); n3I.Add(v + 1); n3I.Add(v + 2); n3I.Add(v); n3I.Add(v + 2); n3I.Add(v + 3);
                }
            if (n3Mesh == null) n3Mesh = new Mesh { name = "PSXLampSplat", hideFlags = HideFlags.DontSave };
            n3Mesh.Clear();
            n3Mesh.indexFormat = n3V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            n3Mesh.SetVertices(n3V);
            n3Mesh.SetUVs(0, n3U0);
            n3Mesh.SetUVs(1, n3U1);
            n3Mesh.SetTriangles(n3I, 0, false);
            n3Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            n3Lamps = lamps; n3Mercs = mercs;
        }

        static void N3Splat(Vector2 origin, float span)
        {
            if (n3Cmd == null) n3Cmd = new CommandBuffer { name = "PSX static lamp field" };
            n3Cmd.Clear();
            n3Cmd.SetRenderTarget(n3Tex);
            n3Cmd.ClearRenderTarget(false, true, Color.clear);
            if (n3Lamps > 0)
            {
                n3Cmd.SetViewProjectionMatrices(Matrix4x4.identity, SplatProj(span));
                n3Mat.SetVector(SplatRId, new Vector4(1f / (N3PoolM * N3PoolM), 1f / (N3GlowM * N3GlowM), 0f, 0f));
                n3Cmd.DrawMesh(n3Mesh, Matrix4x4.Translate(new Vector3(-origin.x, -origin.y, 0f)), n3Mat, 0, 0);
            }
            Graphics.ExecuteCommandBuffer(n3Cmd);
            if (n3Lamps > 0 && !splatChecked) N3CheckOrientation(origin, span);
        }

        // ---- which way up a splat lands (once a process) ----
        // A splat is drawn with an orthographic view of the window; whether the graphics API (and the
        // command buffer's own handling of a render-texture target) puts world +z at the texture's
        // +v is checked ONCE, by reading back the texel under a lamp and the texel mirrored from it.
        static bool splatChecked, splatFlip;
        internal static Matrix4x4 SplatProj(float span) => splatFlip ? Matrix4x4.Ortho(0f, span, span, 0f, -1f, 1f) : Matrix4x4.Ortho(0f, span, 0f, span, -1f, 1f);
        static void N3CheckOrientation(Vector2 origin, float span)
        {
            // a lamp well inside the window, off its middle row (a mirror of the middle row is itself)
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || !e.on || e.kind != Kind.Street || e.intensity <= 0f) continue;
                float u = (e.pos.x - origin.x) / span, v = (e.pos.z - origin.y) / span;
                if (u < 0.1f || u > 0.9f || v < 0.1f || v > 0.9f || Mathf.Abs(v - 0.5f) < 0.08f) continue;
                float d = Mathf.Abs(u - 0.5f) + Mathf.Abs(Mathf.Abs(v - 0.5f) - 0.2f);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            var p = entries[best].pos;
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / N3TexelM), 0, n3Res - 1), z = Mathf.Clamp(Mathf.FloorToInt((p.z - origin.y) / N3TexelM), 0, n3Res - 1);
            float a = ReadR(x, z), b = ReadR(x, n3Res - 1 - z);
            splatChecked = true;
            if (b > a * 1.5f && b > 0.05f)
            {
                splatFlip = !splatFlip;
                Debug.Log("[StreetLights] NIGHT 3: the splat lands mirrored on this device - flipped (" + a.ToString("0.00") + " / " + b.ToString("0.00") + ")");
                N3Splat(origin, span);
            }
            else Debug.Log("[StreetLights] NIGHT 3: splat orientation checked (" + a.ToString("0.00") + " / " + b.ToString("0.00") + ")");
        }
        static float ReadR(int x, int z)
        {
            var req = AsyncGPUReadback.Request(n3Tex, 0, x, 1, z, 1, 0, 1, TextureFormat.RGBAFloat, null);
            req.WaitForCompletion();
            return req.hasError ? 0f : req.GetData<Color>()[0].r;
        }

        static readonly int Night3Id = Shader.PropertyToID("_PSXNight3");
        static readonly int WetnessId = Shader.PropertyToID("_PSXWetness");
        /// <summary>The wetness under which NIGHT 3 gives the lit streets' lamps no table glint (a clear night is 0.40).</summary>
        public static float N3GlintWetMin = 0.6f;
        static int n3AirFrame = -1;
        static readonly int N3SkyId = Shader.PropertyToID("_PSXN3Sky");
        static readonly int NightGlobalId = Shader.PropertyToID("_PSXNight");
        /// <summary>The lit city's night sky over the horizon, x this (his frames: Ycode 7-17, ours 33).</summary>
        public static float N3SkyMul = 0.25f;
        /// <summary>The air's haze under NIGHT 3 (0 = clear: no cones; tools may set it, -1 = the weather's).</summary>
        public static float AirOverride = -1f;
        /// <summary>The haze (x the cone's light) and the extinction length (m) of the air in a weather.</summary>
        public static Vector2 AirFor(Weather w)
        {
            switch (w)
            {
                case Weather.Fog: return new Vector2(LampCones.HazeFor(w), 260f);
                case Weather.Rain: return new Vector2(LampCones.HazeFor(w), 700f);
                case Weather.Snow: return new Vector2(LampCones.HazeFor(w), 450f);
                default: return new Vector2(0f, 1000f);
            }
        }

        /// <summary>_PSXNight3 for every camera (x on, y the air's haze, z its extinction length);
        /// once a frame, the cones in the air follow the weather (NightGlow.Air.cs).</summary>
        static void PushNight3Globals()
        {
            Vector2 air = Night3 ? AirFor(Seasons.CurrentPrecipWeather) : Vector2.zero;
            if (Night3 && AirOverride >= 0f) air.x = AirOverride;
            if (!LampCones.Enabled) air.x = 0f;
            Shader.SetGlobalVector(Night3Id, new Vector4(Night3 ? 1f : 0f, air.x, Mathf.Max(air.y, 1f), 0f));
            // the lit city's night sky (PSXAtmosphere.cginc): darker toward the zenith, by the night
            // (the city only: a stage whose only field lamps are its tunnels' keeps its sky)
            float sky = Night3 && FieldActive && City.CityWorld.Active != null ? Mathf.Lerp(1f, N3SkyMul, Mathf.Clamp01(Shader.GetGlobalFloat(NightGlobalId))) : 0f;
            Shader.SetGlobalFloat(N3SkyId, sky);
            if (Application.isPlaying && Time.frameCount != n3AirFrame)
            {
                n3AirFrame = Time.frameCount;
                NightGlow.TickAir(air.x);
            }
        }

        /// <summary>Tools: the static field's state and its texel at <paramref name="p"/>
        /// ("origin res lamps base | R G B A | lamps within 40 m of p in the registry").</summary>
        public static string N3Debug(Vector3 p)
        {
            int near = 0, nearOn = 0;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || e.kind != Kind.Street) continue;
                float dx = e.pos.x - p.x, dz = e.pos.z - p.z;
                if (dx * dx + dz * dz < 1600f) { near++; if (e.on && e.intensity > 0f) nearOn++; }
            }
            string px = "-";
            if (n3Tex != null && n3Tex.IsCreated() && !float.IsNaN(n3Origin.x))
            {
                int x = Mathf.FloorToInt((p.x - n3Origin.x) / N3TexelM), z = Mathf.FloorToInt((p.z - n3Origin.y) / N3TexelM);
                if (x >= 0 && z >= 0 && x < n3Res && z < n3Res)
                {
                    var req = UnityEngine.Rendering.AsyncGPUReadback.Request(n3Tex, 0, x, 1, z, 1, 0, 1, TextureFormat.RGBAFloat, null);
                    req.WaitForCompletion();
                    if (!req.hasError) { var c = req.GetData<Color>()[0]; px = c.r.ToString("0.00") + " " + c.g.ToString("0.00") + " " + (c.g > 1e-4f ? (n3BaseY + c.b / c.g).ToString("0.0") : "-") + " " + c.a.ToString("0.00"); }
                    else px = "readback error";
                }
                else px = "outside";
            }
            return "o " + n3Origin.x.ToString("0") + "/" + n3Origin.y.ToString("0") + " res " + n3Res + " lamps " + n3Lamps + " base " + n3BaseY.ToString("0") +
                   " | " + px + " | near " + near + " on " + nearOn + " streetOn " + StreetOn + " ver " + n3Version + "/" + n3MeshVersion;
        }

        /// <summary>The lit city-street lamp (a field lamp) nearest <paramref name="p"/>
        /// in plan, within <paramref name="maxM"/> (tools: the approach curves).</summary>
        public static bool NearestFieldLamp(Vector3 p, float maxM, out Vector3 head)
        {
            head = default;
            float best = maxM * maxM; bool found = false;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || e.kind != Kind.Street) continue;
                float dx = e.pos.x - p.x, dz = e.pos.z - p.z, d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; head = e.pos; found = true; }
            }
            return found;
        }
    }
}
