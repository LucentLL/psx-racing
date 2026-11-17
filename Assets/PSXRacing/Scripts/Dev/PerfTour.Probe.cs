using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// THE FRAME PROBE (perf round, 2026-11-16; owner: "we need to start
    /// focusing HEAVILY on performance" - 40-49 fps on the mountain stages at
    /// 3832x2054). A tour's frame time says how slow a frame is, not where it
    /// goes; and with other Unity jobs on the machine, two runs of one build
    /// differ by more than most single features cost. So each feature is
    /// measured INSIDE ONE RUN, at one place, by switching it off and on in
    /// short alternating blocks:
    ///
    ///   -perftour "stage=LakeLure&amp;from=2400&amp;probe=park&amp;offscreen=3832x2054&amp;view=chase
    ///              [&amp;pv=name=op,op;name=op...][&amp;pcfg=reps,settle,frames]"
    ///   -perftour "routes=trade&amp;from=1700&amp;probe=park&amp;offscreen=3832x2054"      (the city)
    ///
    /// probe=park: the car stands at <c>from</c>; probe=drive: the race AI
    /// drives while the blocks alternate (speed blur and streaming included).
    /// The main view is drawn into a texture of the asked size and the frame
    /// WAITS for the GPU (PerfTour.Stage's offscreen way), so a frame's time
    /// is main thread + render thread + GPU end to end, and what a switch
    /// saves shows whichever of them it saves it on. Columns: the whole frame,
    /// the main view's render call (main thread), the wait (render thread +
    /// GPU), and Unity's triangle and set-pass counts.
    ///
    /// A variant is a name and one or more ops (comma-separated):
    ///   res:0.5              the main view at that share of the offscreen size
    ///   set:Class.Member:v   a static bool / int / float field or property
    ///   g:_Global:v          Shader.SetGlobalFloat
    ///   layer:8.9 | layer:all   those layers out of the main view
    ///   only:10              ONLY those layers in the main view
    ///   feat:Name            a URP renderer feature off (name contains)
    ///   urp:opaque|depth|hdr the pipeline asset's opaque / depth texture / HDR off
    ///   fs:Feature:Field:v   a field of a renderer feature's settings (fs:ScreenSpaceAmbientOcclusion:Downsample:1)
    ///   rd:Member:v          a field or property of the renderer data (rd:renderingMode:0 = Forward)
    ///   comp:Type            every enabled behaviour of that type off
    ///   go:Name              that GameObject inactive
    ///   rend:token           renderers whose shader name contains token not drawn
    ///   kw:NAME | nkw:NAME   a global shader keyword on | off
    ///   q:aniso:0 q:mip:1 q:lod:0.5   QualitySettings
    ///   add:blit | add:halation   the display blit (PSX/Blit, FXAA) / the halation's glow, drawn too
    /// &amp;onscreen=1 (a windowed run, no offscreen=): the frame as the window draws it - every camera,
    /// the display's blit and the present - by its own time and Unity's GPU time; res: and add: are skipped.
    /// Output: &lt;tag&gt;_&lt;id&gt;_probe.txt (a table) and _probe.csv.
    /// </summary>
    public partial class PerfTour
    {
        string probeArg, pvArg, probeCfg;

        // ---- Unity's own frame timing (needs the player built with Frame Timing Stats) and every camera's cost ----
        float[] cGpu, cRT, cMainFT, cWaitFT;
        readonly FrameTiming[] ftBuf = new FrameTiming[1];

        void TimingAlloc()
        {
            if (cGpu != null) return;
            cGpu = new float[Cap]; cRT = new float[Cap]; cMainFT = new float[Cap]; cWaitFT = new float[Cap];
        }

        void TimingFrame(int i)
        {
            if (cGpu == null) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ftBuf) > 0)
            {
                cGpu[i] = (float)ftBuf[0].gpuFrameTime; cRT[i] = (float)ftBuf[0].cpuRenderThreadFrameTime;
                cMainFT[i] = (float)ftBuf[0].cpuMainThreadFrameTime; cWaitFT[i] = (float)ftBuf[0].cpuMainThreadPresentWaitTime;
            }
            else { cGpu[i] = 0f; cRT[i] = 0f; cMainFT[i] = 0f; cWaitFT[i] = 0f; }
        }

        /// <summary>Two lines for a tour's summary: Unity's GPU / render
        /// thread / main thread times, and each camera's main-thread cost.</summary>
        string ExtraSummary(int frames)
        {
            var sb = new StringBuilder();
            if (cGpu != null && frames > 0)
            {
                var g = new List<float>(frames); double sg = 0, sr = 0, sm = 0, sw = 0; int got = 0;
                for (int i = 0; i < frames; i++)
                {
                    if (cGpu[i] <= 0f && cRT[i] <= 0f && cMainFT[i] <= 0f) continue;
                    got++; g.Add(cGpu[i]); sg += cGpu[i]; sr += cRT[i]; sm += cMainFT[i]; sw += cWaitFT[i];
                }
                if (got > 0)
                {
                    g.Sort();
                    sb.AppendLine($"Unity frame timing ({got} frames): GPU mean {sg / got:0.00} p50 {g[got / 2]:0.00} p95 {g[Mathf.Min(got - 1, got * 95 / 100)]:0.00} ms; render thread {sr / got:0.00}; main thread {sm / got:0.00}; main thread waiting for present {sw / got:0.00}");
                }
                else sb.AppendLine("Unity frame timing: none (this player was built without Frame Timing Stats)");
            }
            if (PerfClock.CamMs.Count > 0 && frames > 0)
            {
                var names = new List<string>(PerfClock.CamMs.Keys);
                names.Sort((a, b) => PerfClock.CamMs[b].CompareTo(PerfClock.CamMs[a]));
                sb.Append("cameras, main-thread ms a frame (draws a frame):");
                foreach (var k in names)
                {
                    PerfClock.CamCalls.TryGetValue(k, out int calls);
                    sb.Append($" {k} {PerfClock.CamMs[k] / frames:0.00} ({calls / (float)frames:0.00});");
                }
                sb.AppendLine();
            }
            double sh = 0;
            for (int i = 0; i < frames; i++) sh += cSlot[(int)PerfClock.Slot.SunShadowMaps][i];
            if (frames > 0) sb.AppendLine($"sun shadow maps recorded: {sh / frames:0.00} ms a frame (inside render submit); casters last frame {SunShadows.DrawnLast}, draw calls {SunShadows.DrawCallsLast}");
            return sb.ToString();
        }

        /// <summary>The main view into a texture, waited for; the texture can
        /// change between frames (res:), and the display's own passes can be
        /// drawn after it (add:).</summary>
        [DefaultExecutionOrder(20000)]
        class ProbeView : MonoBehaviour
        {
            public Camera cam;
            public RenderTexture rt, screen;
            public Material blitMat;
            public bool blit, halation;
            RenderTexture was;
            bool held;
            public void Hold() { if (cam != null && !held) { was = cam.targetTexture; held = true; } }
            void OnDisable() { if (cam != null && held) cam.targetTexture = was; }
            void LateUpdate()
            {
                if (cam == null || rt == null) return;
                Hold();
                if (cam.targetTexture != rt) cam.targetTexture = rt;
                long t0 = PerfClock.Now();
                cam.Render();
                if (halation) Halation.BuildNow(rt);
                Texture last = rt;
                if (blit && blitMat != null && screen != null) { Graphics.Blit(rt, screen, blitMat); last = screen; }
                PerfClock.Add(PerfClock.Slot.OffscreenCpu, t0);
                t0 = PerfClock.Now();
                var req = AsyncGPUReadback.Request(last, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32, null);
                req.WaitForCompletion();
                PerfClock.Add(PerfClock.Slot.OffscreenGpu, t0);
            }

            public byte[] Snap(int maxW, bool png = false)
            {
                int w = Mathf.Min(maxW, rt.width), h = Mathf.Max(16, Mathf.RoundToInt(rt.height * (w / (float)rt.width)));
                var small = RenderTexture.GetTemporary(w, h, 0, rt.format);
                Graphics.Blit(rt, small);
                var prev = RenderTexture.active;
                RenderTexture.active = small;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(small);
                var jpg = png ? tex.EncodeToPNG() : tex.EncodeToJPG(90);
                Destroy(tex);
                return jpg;
            }
        }

        /// <summary>The window's own picture (an on-screen probe's snap), as a PNG.</summary>
        IEnumerator ScreenSnap(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var png = tex.EncodeToPNG();
            Destroy(tex);
            PSXPerfTour_Post(name, png, png.Length);
        }

        class ProbeOp { public System.Action on, off; public string what; }
        class ProbeVariant { public string name; public List<ProbeOp> ops = new List<ProbeOp>(); public bool snap; }

        // what a block's frames record
        const int PM_FRAME = 0, PM_CALL = 1, PM_WAIT = 2, PM_LATE = 3, PM_REFL = 4, PM_TRIS = 5, PM_SETPASS = 6, PM_UPD = 7, PM_FIX = 8, PM_SHADOW = 9,
                  PM_GPU = 10, PM_RT = 11, PM_REND = 12, PM_N = 13;
        static readonly string[] PmNames = { "frame", "viewcall", "gpuwait", "late", "carrefl", "trisM", "setpass", "upd", "fixed", "shadowcpu", "gpuFT", "renderthreadFT", "rendersubmit" };
        readonly FrameTiming[] probeFt = new FrameTiming[1];

        /// <summary>The default variants: every feature of the frame once.</summary>
        const string DefaultProbe =
            "res75=res:0.75;res50=res:0.5;res25=res:0.25;" +
            "nothing=layer:all;" +
            "noDefault=layer:0;noRoad=layer:8;noSolid=layer:9;noFoliage=layer:10;noHorizon=layer:11;noWater=layer:4;noWeather=layer:1;" +
            "noSSAO=feat:ScreenSpaceAmbientOcclusion;noPostFeature=feat:SpeedBlur;" +
            "noOpaqueTex=urp:opaque;noDepthTex=urp:depth;" +
            "noSunShadows=set:SunShadows.ForceOff:1;" +
            "noCarRefl=comp:CarReflections;" +
            "noFarCanopy=g:_PSXFarCanopyOff:1;" +
            "cloudsPaused=set:VolumetricClouds.Paused:1;" +
            "aniso0=q:aniso:0;mipHalf=q:mip:1;" +
            "plusBlit=add:blit;plusHalation=add:halation";

        static System.Type FindType(string name)
        {
            foreach (var ns in new[] { "PSXRacing.", "PSXRacing.City.", "PSXRacing.LifeSim.", "" })
            {
                var t = System.Type.GetType(ns + name);
                if (t != null) return t;
            }
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("PSXRacing." + name) ?? asm.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        static object ParseAs(System.Type t, string v)
        {
            var ci = CultureInfo.InvariantCulture;
            if (t == typeof(bool)) return v != "0" && !string.Equals(v, "false", System.StringComparison.OrdinalIgnoreCase);
            if (t == typeof(int)) return int.Parse(v, ci);
            if (t == typeof(float)) return float.Parse(v, ci);
            if (t == typeof(double)) return double.Parse(v, ci);
            if (t.IsEnum) return System.Enum.ToObject(t, int.Parse(v, ci));
            if (t == typeof(string)) return v;
            return null;
        }

        static int LayerMaskOf(string list)
        {
            if (list == "all") return ~0;
            int m = 0;
            foreach (var s in list.Split('.')) if (int.TryParse(s, out int l) && l >= 0 && l < 32) m |= 1 << l;
            return m;
        }

        ProbeOp MakeOp(string spec, ProbeView view, List<RenderTexture> made)
        {
            var f = spec.Split(':');
            string kind = f[0].Trim();
            string A(int i) => i < f.Length ? f[i].Trim() : "";
            var ci = CultureInfo.InvariantCulture;
            const BindingFlags SF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            switch (kind)
            {
                case "res":
                {
                    if (!float.TryParse(A(1), NumberStyles.Float, ci, out float s) || s <= 0f) return null;
                    var full = view.rt;
                    int w = Mathf.Max(16, Mathf.RoundToInt(full.width * s) & ~1), h = Mathf.Max(16, Mathf.RoundToInt(full.height * s));
                    var small = new RenderTexture(w, h, PlatformProfile.PC ? 32 : 24, RenderTextureFormat.Default) { antiAliasing = 1, name = "PerfProbe" + w };
                    small.Create();
                    made.Add(small);
                    return new ProbeOp { what = "view " + w + "x" + h, on = () => view.rt = small, off = () => view.rt = full };
                }
                case "set":
                {
                    string cm = A(1); int dot = cm.LastIndexOf('.');
                    if (dot <= 0) return null;
                    var type = FindType(cm.Substring(0, dot));
                    string mn = cm.Substring(dot + 1);
                    var fi = type?.GetField(mn, SF);
                    var pi = fi == null ? type?.GetProperty(mn, SF) : null;
                    if (fi == null && (pi == null || !pi.CanWrite || !pi.CanRead)) { Say("probe: no static " + cm); return null; }
                    var mt = fi != null ? fi.FieldType : pi.PropertyType;
                    object val = ParseAs(mt, A(2));
                    if (val == null) { Say("probe: cannot set a " + mt.Name + " (" + cm + ")"); return null; }
                    object was = null;
                    return new ProbeOp
                    {
                        what = cm + "=" + A(2),
                        on = () => { was = fi != null ? fi.GetValue(null) : pi.GetValue(null); if (fi != null) fi.SetValue(null, val); else pi.SetValue(null, val); },
                        off = () => { if (was == null) return; if (fi != null) fi.SetValue(null, was); else pi.SetValue(null, was); },
                    };
                }
                case "g":
                {
                    int id = Shader.PropertyToID(A(1));
                    if (!float.TryParse(A(2), NumberStyles.Float, ci, out float v)) return null;
                    float was = 0f;
                    return new ProbeOp { what = A(1) + "=" + A(2), on = () => { was = Shader.GetGlobalFloat(id); Shader.SetGlobalFloat(id, v); }, off = () => Shader.SetGlobalFloat(id, was) };
                }
                case "layer":
                case "only":
                {
                    int m = LayerMaskOf(A(1));
                    bool only = kind == "only";
                    int was = 0;
                    var cam = view.cam;
                    return new ProbeOp { what = kind + " " + A(1), on = () => { was = cam.cullingMask; cam.cullingMask = only ? (was & m) : (was & ~m); }, off = () => cam.cullingMask = was };
                }
                case "feat":
                {
                    var feats = new List<ScriptableRendererFeature>();
                    var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    var list = urp == null ? null : typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(urp) as ScriptableRendererData[];
                    if (list != null)
                        foreach (var data in list)
                            if (data != null)
                                foreach (var rf in data.rendererFeatures)
                                    if (rf != null && (rf.name.Contains(A(1)) || rf.GetType().Name.Contains(A(1)))) feats.Add(rf);
                    if (feats.Count == 0) { Say("probe: no renderer feature " + A(1)); return null; }
                    var was = new bool[feats.Count];
                    return new ProbeOp
                    {
                        what = "feature " + A(1) + " x" + feats.Count,
                        on = () => { for (int i = 0; i < feats.Count; i++) { was[i] = feats[i].isActive; feats[i].SetActive(false); } },
                        off = () => { for (int i = 0; i < feats.Count; i++) feats[i].SetActive(was[i]); },
                    };
                }
                case "urp":
                {
                    var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    if (urp == null) return null;
                    string which = A(1);
                    bool was = false;
                    return new ProbeOp
                    {
                        what = "urp " + which + " off",
                        on = () =>
                        {
                            if (which == "opaque") { was = urp.supportsCameraOpaqueTexture; urp.supportsCameraOpaqueTexture = false; }
                            else if (which == "hdr") { was = urp.supportsHDR; urp.supportsHDR = false; }
                            else { was = urp.supportsCameraDepthTexture; urp.supportsCameraDepthTexture = false; }
                        },
                        off = () =>
                        {
                            if (which == "opaque") urp.supportsCameraOpaqueTexture = was;
                            else if (which == "hdr") urp.supportsHDR = was;
                            else urp.supportsCameraDepthTexture = was;
                        },
                    };
                }
                case "fs":
                case "rd":
                {
                    // fs:Feature:Field:value - a field of a renderer feature's settings object;
                    // rd:Member:value - a field or property of the renderer data itself (renderingMode, ...)
                    bool isFeat = kind == "fs";
                    string member = isFeat ? A(2) : A(1), value = isFeat ? A(3) : A(2);
                    const BindingFlags IF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    var targets = new List<object>(); var dirty = new List<ScriptableRendererData>();
                    var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    var list = urp == null ? null : typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", IF)?.GetValue(urp) as ScriptableRendererData[];
                    if (list != null)
                        foreach (var data in list)
                        {
                            if (data == null) continue;
                            if (!isFeat) { targets.Add(data); dirty.Add(data); continue; }
                            foreach (var rf in data.rendererFeatures)
                            {
                                if (rf == null || !(rf.name.Contains(A(1)) || rf.GetType().Name.Contains(A(1)))) continue;
                                var st = rf.GetType().GetField("m_Settings", IF)?.GetValue(rf);
                                if (st != null) { targets.Add(st); dirty.Add(data); }
                            }
                        }
                    if (targets.Count == 0) { Say("probe: nothing to set for " + spec); return null; }
                    var tt = targets[0].GetType();
                    FieldInfo tf = null; PropertyInfo tp = null;
                    for (var ty = tt; ty != null && tf == null && tp == null; ty = ty.BaseType) { tf = ty.GetField(member, IF | BindingFlags.DeclaredOnly); if (tf == null) tp = ty.GetProperty(member, IF | BindingFlags.DeclaredOnly); }
                    if (tf == null && (tp == null || !tp.CanWrite)) { Say("probe: no member " + member + " on " + tt.Name); return null; }
                    var mt2 = tf != null ? tf.FieldType : tp.PropertyType;
                    object val2 = ParseAs(mt2, value);
                    if (val2 == null) { Say("probe: cannot set a " + mt2.Name); return null; }
                    var was2 = new object[targets.Count];
                    return new ProbeOp
                    {
                        what = (isFeat ? A(1) + "." : "renderer.") + member + "=" + value,
                        on = () => { for (int i = 0; i < targets.Count; i++) { was2[i] = tf != null ? tf.GetValue(targets[i]) : tp.GetValue(targets[i]); if (tf != null) tf.SetValue(targets[i], val2); else tp.SetValue(targets[i], val2); } foreach (var d in dirty) d.SetDirty(); },
                        off = () => { for (int i = 0; i < targets.Count; i++) { if (tf != null) tf.SetValue(targets[i], was2[i]); else tp.SetValue(targets[i], was2[i]); } foreach (var d in dirty) d.SetDirty(); },
                    };
                }
                case "comp":
                {
                    var type = FindType(A(1));
                    if (type == null) { Say("probe: no type " + A(1)); return null; }
                    var hit = new List<Behaviour>();
                    return new ProbeOp
                    {
                        what = "components " + A(1),
                        on = () =>
                        {
                            hit.Clear();
                            foreach (var o in FindObjectsByType(type, FindObjectsSortMode.None))
                                if (o is Behaviour b && b.enabled) { b.enabled = false; hit.Add(b); }
                        },
                        off = () => { foreach (var b in hit) if (b != null) b.enabled = true; },
                    };
                }
                case "go":
                {
                    var go = GameObject.Find(A(1));
                    if (go == null) { Say("probe: no object " + A(1)); return null; }
                    return new ProbeOp { what = "object " + A(1), on = () => go.SetActive(false), off = () => go.SetActive(true) };
                }
                case "rend":
                {
                    string token = A(1);
                    var hit = new List<Renderer>();
                    var mats = new List<Material>();
                    return new ProbeOp
                    {
                        what = "renderers of " + token,
                        on = () =>
                        {
                            hit.Clear();
                            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            {
                                if (r == null || r.forceRenderingOff || !r.enabled) continue;
                                r.GetSharedMaterials(mats);
                                bool any = false;
                                foreach (var m in mats) if (m != null && m.shader != null && m.shader.name.Contains(token)) { any = true; break; }
                                if (any) { r.forceRenderingOff = true; hit.Add(r); }
                            }
                        },
                        off = () => { foreach (var r in hit) if (r != null) r.forceRenderingOff = false; },
                    };
                }
                case "kw":
                case "nkw":
                {
                    string k = A(1); bool want = kind == "kw"; bool was = false;
                    return new ProbeOp
                    {
                        what = (want ? "keyword " : "no keyword ") + k,
                        on = () => { was = Shader.IsKeywordEnabled(k); if (want) Shader.EnableKeyword(k); else Shader.DisableKeyword(k); },
                        off = () => { if (was) Shader.EnableKeyword(k); else Shader.DisableKeyword(k); },
                    };
                }
                case "q":
                {
                    string what = A(1);
                    if (!float.TryParse(A(2), NumberStyles.Float, ci, out float v)) return null;
                    float was = 0f;
                    if (what == "aniso") return new ProbeOp { what = "aniso " + v, on = () => { was = (int)QualitySettings.anisotropicFiltering; QualitySettings.anisotropicFiltering = (AnisotropicFiltering)(int)v; }, off = () => QualitySettings.anisotropicFiltering = (AnisotropicFiltering)(int)was };
                    if (what == "mip") return new ProbeOp { what = "mip limit " + v, on = () => { was = QualitySettings.globalTextureMipmapLimit; QualitySettings.globalTextureMipmapLimit = (int)v; }, off = () => QualitySettings.globalTextureMipmapLimit = (int)was };
                    if (what == "lod") return new ProbeOp { what = "lod bias " + v, on = () => { was = QualitySettings.lodBias; QualitySettings.lodBias = v; }, off = () => QualitySettings.lodBias = was };
                    return null;
                }
                case "add":
                {
                    if (A(1) == "blit")
                    {
                        if (view.blitMat == null || view.screen == null) { Say("probe: no display material to blit with"); return null; }
                        return new ProbeOp { what = "+ display blit", on = () => view.blit = true, off = () => view.blit = false };
                    }
                    if (A(1) == "halation") return new ProbeOp { what = "+ halation glow", on = () => view.halation = true, off = () => view.halation = false };
                    return null;
                }
            }
            Say("probe: unknown op " + spec);
            return null;
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return 0f;
            v.Sort();
            return (v.Count & 1) == 1 ? v[v.Count / 2] : 0.5f * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        // the block being measured (read by the frame callback)
        List<float>[] probeInto;
        ProfilerRecorderPair probeCounters;

        struct ProfilerRecorderPair
        {
            public Unity.Profiling.ProfilerRecorder tris, setPass;
        }

        void ProbeFrame(double frameMs, double outsideMs)
        {
            var into = probeInto;
            if (into == null) return;
            into[PM_FRAME].Add((float)frameMs);
            into[PM_CALL].Add((float)PerfClock.Slots[(int)PerfClock.Slot.OffscreenCpu]);
            into[PM_WAIT].Add((float)PerfClock.Slots[(int)PerfClock.Slot.OffscreenGpu]);
            into[PM_LATE].Add(Sys(iLate));
            into[PM_REFL].Add((float)PerfClock.Slots[(int)PerfClock.Slot.CarReflect]);
            into[PM_TRIS].Add(probeCounters.tris.Valid ? probeCounters.tris.LastValue / 1e6f : 0f);
            into[PM_SETPASS].Add(probeCounters.setPass.Valid ? probeCounters.setPass.LastValue : 0f);
            into[PM_UPD].Add(Sys(iUpd));
            float fix = 0f; foreach (int k in iFix) fix += Sys(k);
            into[PM_FIX].Add(fix);
            into[PM_SHADOW].Add((float)PerfClock.Slots[(int)PerfClock.Slot.SunShadowMaps]);
            // Unity's own timing (a few frames late; none without Frame Timing Stats in the player)
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, probeFt) > 0 && probeFt[0].gpuFrameTime > 0.0)
            {
                into[PM_GPU].Add((float)probeFt[0].gpuFrameTime);
                into[PM_RT].Add((float)probeFt[0].cpuRenderThreadFrameTime);
            }
            into[PM_REND].Add(Sys(iRend));
        }

        static List<float>[] NewBlock()
        {
            var b = new List<float>[PM_N];
            for (int i = 0; i < PM_N; i++) b[i] = new List<float>(64);
            return b;
        }

        /// <summary>The probe at the place the car is (parked, or driven by
        /// the AI the caller attached). <paramref name="id"/> names the files.</summary>
        IEnumerator Probe(string id)
        {
            var cam = Camera.main;
            if (cam == null) { Say("PROBE " + id + ": no main camera"); yield break; }
            // &onscreen=1: the frame as the window draws it (every camera, the display's blit, the present),
            // measured by its own time and Unity's GPU time - no texture, no wait. A switch then shows on
            // whichever of the main thread and the GPU bounds the frame.
            bool onscreen = (Request() ?? "").Contains("onscreen=1");
            int reps = 4, settle = onscreen ? 10 : 3, frames = onscreen ? 24 : 8;
            if (!string.IsNullOrEmpty(probeCfg))
            {
                var c = probeCfg.Split(',');
                if (c.Length > 0) int.TryParse(c[0], out reps);
                if (c.Length > 1) int.TryParse(c[1], out settle);
                if (c.Length > 2) int.TryParse(c[2], out frames);
                reps = Mathf.Clamp(reps, 1, 40); settle = Mathf.Clamp(settle, 1, 60); frames = Mathf.Clamp(frames, 2, 240);
            }
            // the view: offscreen=WxH, or the owner's screen
            int ow = 3832, oh = 2054;
            if (!string.IsNullOrEmpty(offscreenArg))
            {
                var wh = offscreenArg.ToLowerInvariant().Split('x');
                if (wh.Length == 2) { int.TryParse(wh[0], out ow); int.TryParse(wh[1], out oh); }
            }
            ow = Mathf.Clamp(ow, 64, 8192); oh = Mathf.Clamp(oh, 64, 8192);
            var made = new List<RenderTexture>();
            var full = new RenderTexture(ow, oh, PlatformProfile.PC ? 32 : 24, RenderTextureFormat.Default) { antiAliasing = 1, name = "PerfProbeFull" };
            full.Create(); made.Add(full);
            var screen = new RenderTexture(ow, oh, 0, RenderTextureFormat.Default) { antiAliasing = 1, name = "PerfProbeScreen" };
            screen.Create(); made.Add(screen);
            var holder = new GameObject("PerfTourProbe");
            holder.SetActive(false);
            var view = holder.AddComponent<ProbeView>();
            view.cam = cam; view.rt = full; view.screen = screen;
            var outp = cam.GetComponent<PSXCameraOutput>();
            if (outp != null && outp.display != null) view.blitMat = outp.display.material;
            if (onscreen) { view.enabled = false; ow = Screen.width; oh = Screen.height; full.Release(); screen.Release(); }
            else view.Hold();
            holder.SetActive(true);

            // the variants
            var vars = new List<ProbeVariant>();
            foreach (var raw in (string.IsNullOrEmpty(pvArg) ? DefaultProbe : pvArg.Replace("default", DefaultProbe)).Split(';'))
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                var pv = new ProbeVariant { name = s.Substring(0, eq) };
                foreach (var os in s.Substring(eq + 1).Split(','))
                {
                    if (os.Trim() == "snap") { pv.snap = true; continue; }
                    if (onscreen && (os.Trim().StartsWith("res:") || os.Trim().StartsWith("add:"))) continue;   // (they need the offscreen view)
                    var op = MakeOp(os.Trim(), view, made);
                    if (op != null) pv.ops.Add(op);
                }
                if (pv.ops.Count > 0) vars.Add(pv); else Say("probe: variant " + pv.name + " has no usable op");
            }

            Alloc();
            probeCounters.tris = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
            probeCounters.setPass = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            // warm: the first maps, the clouds' history, the reflection set
            for (int i = 0; i < 90; i++) yield return null;
            // &freeze=1: the clock stopped (wind, water, traffic, the clouds' drift) so that a picture
            // drawn twice is the same picture - the snaps (lossless, full size) then differ only by the switch
            bool freeze = (Request() ?? "").Contains("freeze=1");
            float wasScale = Time.timeScale; bool wasPaused = VolumetricClouds.Paused;
            if (freeze) { Time.timeScale = 0f; VolumetricClouds.Paused = true; for (int i = 0; i < 12; i++) yield return null; }
            PerfClock.FrameDone = ProbeFrame;

            // the baseline alone first: its level and its own noise
            var baseAll = NewBlock();
            probeInto = null;
            for (int i = 0; i < settle; i++) yield return null;
            probeInto = baseAll;
            for (int i = 0; i < frames * 6; i++) yield return null;
            probeInto = null;
            if (!onscreen)
            {
                var basePng = view.Snap(freeze ? 8192 : 1916, freeze);
                PSXPerfTour_Post(tag + "_" + id + "_probe_base." + (freeze ? "png" : "jpg"), basePng, basePng.Length);
                if (freeze)
                {
                    // the same picture again: what two draws of one thing differ by (the noise floor)
                    for (int i = 0; i < 6; i++) yield return null;
                    var again = view.Snap(8192, true);
                    PSXPerfTour_Post(tag + "_" + id + "_probe_base2.png", again, again.Length);
                }
            }
            else if (freeze)
            {
                yield return ScreenSnap(tag + "_" + id + "_probe_base.png");
                for (int i = 0; i < 6; i++) yield return null;
                yield return ScreenSnap(tag + "_" + id + "_probe_base2.png");
            }

            var ci = CultureInfo.InvariantCulture;
            var table = new StringBuilder();
            var csv = new StringBuilder("variant,what,metric,base,variant_value,delta,delta_sd,reps\n");
            var p = car != null ? car.transform.position : cam.transform.position;
            table.AppendLine($"PROBE {id} mode {probeArg}{(onscreen ? " ON SCREEN" : " offscreen")} view {ow}x{oh} {ChaseCamera.Current} at {p.x:0},{p.z:0}; {reps} x ({settle} settle + {frames} frames) each way; hour {(RaceHandoff.TimeOfDayIndex >= 0 && RaceHandoff.TimeOfDayIndex < TimeOfDay.All.Length ? TimeOfDay.All[RaceHandoff.TimeOfDayIndex].name : "?")} weather {Seasons.CurrentWeather}");
            float[] b0 = new float[PM_N];
            for (int m = 0; m < PM_N; m++) b0[m] = Median(baseAll[m]);
            var fm = baseAll[PM_FRAME]; fm.Sort();
            table.AppendLine($"BASE  frame {b0[PM_FRAME]:0.00} ms (p10 {fm[fm.Count / 10]:0.00} p90 {fm[fm.Count * 9 / 10]:0.00}) = view call {b0[PM_CALL]:0.00} + gpu wait {b0[PM_WAIT]:0.00} + rest {b0[PM_FRAME] - b0[PM_CALL] - b0[PM_WAIT]:0.00} (late scripts {b0[PM_LATE]:0.00} incl. car reflections {b0[PM_REFL]:0.00} and the view; update {b0[PM_UPD]:0.00}; fixed {b0[PM_FIX]:0.00}; shadow maps cpu {b0[PM_SHADOW]:0.00}); triangles {b0[PM_TRIS]:0.00} M set-pass {b0[PM_SETPASS]:0}");
            table.AppendLine($"      Unity timing: GPU {b0[PM_GPU]:0.00} ms, render thread {b0[PM_RT]:0.00} ms, render submit (main thread) {b0[PM_REND]:0.00} ms");
            table.AppendLine("variant              | d frame ms (sd) | d view call | d gpu wait (sd) | d late | d GPU(Unity) | d submit | tris M      | set-pass    | what");

            foreach (var pv in vars)
            {
                var dFrame = new List<float>(); var dCall = new List<float>(); var dWait = new List<float>(); var dLate = new List<float>();
                var onAll = NewBlock(); var offAll = NewBlock();
                byte[] snap = null;
                for (int r = 0; r < reps; r++)
                {
                    // variant applied
                    foreach (var op in pv.ops) { try { op.on(); } catch (System.Exception e) { Say("probe: " + pv.name + " on: " + e.Message); } }
                    var onB = NewBlock();
                    probeInto = null;
                    for (int i = 0; i < settle; i++) yield return null;
                    probeInto = onB;
                    for (int i = 0; i < frames; i++) yield return null;
                    probeInto = null;
                    if (pv.snap && r == 0 && !onscreen) snap = view.Snap(freeze ? 8192 : 1916, freeze);
                    if (pv.snap && r == 0 && onscreen) yield return ScreenSnap(tag + "_" + id + "_probe_" + pv.name + ".png");
                    // back to the baseline
                    for (int k = pv.ops.Count - 1; k >= 0; k--) { try { pv.ops[k].off(); } catch (System.Exception e) { Say("probe: " + pv.name + " off: " + e.Message); } }
                    var offB = NewBlock();
                    for (int i = 0; i < settle; i++) yield return null;
                    probeInto = offB;
                    for (int i = 0; i < frames; i++) yield return null;
                    probeInto = null;
                    dFrame.Add(Median(onB[PM_FRAME]) - Median(offB[PM_FRAME]));
                    dCall.Add(Median(onB[PM_CALL]) - Median(offB[PM_CALL]));
                    dWait.Add(Median(onB[PM_WAIT]) - Median(offB[PM_WAIT]));
                    dLate.Add(Median(onB[PM_LATE]) - Median(offB[PM_LATE]));
                    for (int m = 0; m < PM_N; m++) { onAll[m].AddRange(onB[m]); offAll[m].AddRange(offB[m]); }
                }
                if (snap != null) PSXPerfTour_Post(tag + "_" + id + "_probe_" + pv.name + (freeze ? ".png" : ".jpg"), snap, snap.Length);
                float Mean(List<float> v) { float s = 0f; foreach (var x in v) s += x; return v.Count > 0 ? s / v.Count : 0f; }
                float Sd(List<float> v) { if (v.Count < 2) return 0f; float mu = Mean(v), s = 0f; foreach (var x in v) s += (x - mu) * (x - mu); return Mathf.Sqrt(s / (v.Count - 1)); }
                var what = new StringBuilder();
                foreach (var op in pv.ops) { if (what.Length > 0) what.Append(" + "); what.Append(op.what); }
                float tOn = Median(onAll[PM_TRIS]), tOff = Median(offAll[PM_TRIS]), sOn = Median(onAll[PM_SETPASS]), sOff = Median(offAll[PM_SETPASS]);
                float gOn = Median(onAll[PM_GPU]), gOff = Median(offAll[PM_GPU]), rOn = Median(onAll[PM_REND]), rOff = Median(offAll[PM_REND]);
                table.AppendLine($"{pv.name,-20} | {Mean(dFrame),7:0.00} ({Sd(dFrame):0.00}) | {Mean(dCall),11:0.00} | {Mean(dWait),7:0.00} ({Sd(dWait):0.00}) | {Mean(dLate),6:0.00} | {gOn - gOff,12:0.00} | {rOn - rOff,8:0.00} | {tOff:0.00}>{tOn:0.00} | {sOff:0}>{sOn:0} | {what}");
                for (int m = 0; m < PM_N; m++)
                {
                    float on = Median(onAll[m]), off = Median(offAll[m]);
                    float sd = m == PM_FRAME ? Sd(dFrame) : m == PM_CALL ? Sd(dCall) : m == PM_WAIT ? Sd(dWait) : m == PM_LATE ? Sd(dLate) : 0f;
                    csv.Append(pv.name).Append(',').Append(what.ToString().Replace(',', ' ')).Append(',').Append(PmNames[m]).Append(',')
                       .Append(off.ToString("0.000", ci)).Append(',').Append(on.ToString("0.000", ci)).Append(',').Append((on - off).ToString("0.000", ci)).Append(',')
                       .Append(sd.ToString("0.000", ci)).Append(',').Append(reps).Append('\n');
                }
                Say("PROBE " + pv.name + $": frame {Mean(dFrame):0.00} ({Sd(dFrame):0.00}) wait {Mean(dWait):0.00} call {Mean(dCall):0.00}");
            }

            // the baseline again: did the place drift while the probe ran?
            var baseEnd = NewBlock();
            for (int i = 0; i < settle; i++) yield return null;
            probeInto = baseEnd;
            for (int i = 0; i < frames * 6; i++) yield return null;
            probeInto = null;
            table.AppendLine($"BASE again: frame {Median(baseEnd[PM_FRAME]):0.00} view call {Median(baseEnd[PM_CALL]):0.00} gpu wait {Median(baseEnd[PM_WAIT]):0.00} (drift {Median(baseEnd[PM_FRAME]) - b0[PM_FRAME]:0.00} ms)");
            table.AppendLine("d = variant minus baseline, mean of the reps' medians (sd across reps); negative = what switching it off saves. gpu wait = render thread + GPU for everything drawn this frame.");

            PerfClock.FrameDone = null;
            if (freeze) { Time.timeScale = wasScale; VolumetricClouds.Paused = wasPaused; }
            probeCounters.tris.Dispose(); probeCounters.setPass.Dispose();
            Post(id + "_probe.txt", table.ToString());
            Post(id + "_probe.csv", csv.ToString());
            Say("PROBE DONE " + id);
            Destroy(holder);
            yield return null;
            foreach (var t in made) if (t != null) { t.Release(); Destroy(t); }
            FrameRatePrefs.Apply();
        }
    }
}
