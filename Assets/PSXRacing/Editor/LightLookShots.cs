using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LIGHT-LOOK SHOTS (the lightlook round, 2026-11-23): full-size frames
    /// of the game's own picture - the main camera drawn at the owner's
    /// 3832x2054, its halation built and composited by the display's own
    /// PSX/Blit material - at one pose, once per VARIANT (a list of static
    /// switches set before the render), so a before / after is the same frame
    /// with only the switch changed. Driven by LightPopCheck's city mode
    /// (PSX_LIGHTPOP_SHOTS = game seconds into each route to shoot at).
    ///
    /// PSX_LIGHTLOOK_VARIANTS: "name=Class.Member:v,Class.Member:v;name=..."
    /// (default "base=LightLook.On:0;look=LightLook.On:1"). PSX_LIGHTLOOK_W/H:
    /// the frame (default 3832x2054). Frames: &lt;out&gt;/shots/&lt;seg&gt;_&lt;t&gt;s_&lt;variant&gt;.jpg
    /// </summary>
    public static class LightLookShots
    {
        public static int W = 3832, H = 2054;

        class Op { public System.Reflection.FieldInfo f; public System.Reflection.PropertyInfo p; public object v, was; }
        class Variant { public string name; public List<Op> ops = new List<Op>(); }
        static List<Variant> variants;

        static System.Type FindType(string name)
        {
            foreach (var ns in new[] { "PSXRacing.", "PSXRacing.City.", "" })
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType(ns + name);
                    if (t != null) return t;
                }
            }
            return null;
        }

        static object Parse(System.Type t, string s)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (t == typeof(bool)) return s == "1" || s.ToLowerInvariant() == "true";
            if (t == typeof(int)) return int.Parse(s, ci);
            if (t == typeof(float)) return float.Parse(s, ci);
            return null;
        }

        static void Load()
        {
            if (variants != null) return;
            variants = new List<Variant>();
            if (int.TryParse(System.Environment.GetEnvironmentVariable("PSX_LIGHTLOOK_W") ?? "", out int w) && w >= 256) W = w;
            if (int.TryParse(System.Environment.GetEnvironmentVariable("PSX_LIGHTLOOK_H") ?? "", out int h) && h >= 144) H = h;
            string spec = System.Environment.GetEnvironmentVariable("PSX_LIGHTLOOK_VARIANTS");
            if (string.IsNullOrEmpty(spec)) spec = "base=LightLook.On:0;look=LightLook.On:1";
            foreach (var raw in spec.Split(';'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                var v = new Variant { name = raw.Substring(0, eq).Trim() };
                foreach (var opS in raw.Substring(eq + 1).Split(','))
                {
                    var s = opS.Trim();
                    int colon = s.LastIndexOf(':'), dot = s.LastIndexOf('.', colon > 0 ? colon : s.Length - 1);
                    if (colon <= 0 || dot <= 0) continue;
                    var type = FindType(s.Substring(0, dot));
                    if (type == null) { LightPopCheck.Line("LOOK no type " + s); continue; }
                    string member = s.Substring(dot + 1, colon - dot - 1), val = s.Substring(colon + 1);
                    const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    var op = new Op { f = type.GetField(member, bf), p = type.GetProperty(member, bf) };
                    var mt = op.f != null ? op.f.FieldType : op.p != null ? op.p.PropertyType : null;
                    if (mt == null) { LightPopCheck.Line("LOOK no member " + s); continue; }
                    op.v = Parse(mt, val);
                    v.ops.Add(op);
                }
                variants.Add(v);
            }
        }

        static object Get(Op o) => o.f != null ? o.f.GetValue(null) : o.p.GetValue(null);
        static void Set(Op o, object v) { if (o.f != null) o.f.SetValue(null, v); else o.p.SetValue(null, v); }

        /// <summary>Shoot the main camera once per variant into <paramref name="dir"/>.</summary>
        public static void Take(Camera cam, string dir, string name)
        {
            Load();
            if (cam == null) return;
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1, name = "LightLookFull" };
            var screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1, name = "LightLookScreen" };
            rt.Create(); screen.Create();
            var outp = cam.GetComponent<PSXCameraOutput>();
            Material blit = outp != null && outp.display != null && outp.display.material != null ? new Material(outp.display.material) { hideFlags = HideFlags.DontSave } : null;
            var was = cam.targetTexture;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            foreach (var v in variants)
            {
                foreach (var o in v.ops) { o.was = Get(o); Set(o, o.v); }
                for (int pass = 0; pass < 2; pass++)   // a warm-up render first (the speed blur's history)
                {
                    // the camera's whole stack (the cockpit's interior overlay too), as the light-pop meter does
                    var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                    if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                    else { cam.targetTexture = rt; cam.Render(); }
                }
                RenderTexture glow = null;
                if (blit != null && Halation.Wanted(blit)) glow = Halation.BuildNow(rt);
                if (blit != null) { Halation.Apply(blit, glow); Graphics.Blit(rt, screen, blit); }
                else Graphics.Blit(rt, screen);
                var prev = RenderTexture.active;
                RenderTexture.active = screen;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Halation.Release(ref glow);
                try { File.WriteAllBytes(Path.Combine(dir, name + "_" + v.name + ".jpg"), tex.EncodeToJPG(93)); } catch (System.Exception) { }
                for (int i = v.ops.Count - 1; i >= 0; i--) Set(v.ops[i], v.ops[i].was);
            }
            cam.targetTexture = was;
            Object.Destroy(tex);
            if (blit != null) Object.Destroy(blit);
            rt.Release(); screen.Release();
            Object.Destroy(rt); Object.Destroy(screen);
        }
    }

    /// <summary>Takes a queued look shot in LateUpdate, after every camera rig has
    /// placed its camera for the frame (the cockpit view's camera is not where it
    /// draws from until its own LateUpdate).</summary>
    [DefaultExecutionOrder(32000)]
    public class LightLookShotRunner : MonoBehaviour
    {
        static LightLookShotRunner inst;
        static string pendDir, pendName;
        public static bool Busy => pendName != null;
        public static void Queue(string dir, string name)
        {
            if (inst == null) { var go = new GameObject("LightLookShotRunner"); Object.DontDestroyOnLoad(go); inst = go.AddComponent<LightLookShotRunner>(); }
            pendDir = dir; pendName = name;
        }
        void LateUpdate()
        {
            if (pendName == null) return;
            LightLookShots.Take(Camera.main, pendDir, pendName);
            pendName = null;
        }
    }
}
