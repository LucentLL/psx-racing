using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.City
{
    /// <summary>
    /// THE CITY'S SHADER VARIANTS, COMPILED AT LOAD (city perf round 1,
    /// 2026-10-30). A browser compiles a shader program the first time it is
    /// drawn (ANGLE turns it into HLSL and compiles that: tens to hundreds of
    /// milliseconds in ONE frame, the first time a facade, a far layer, a
    /// sign, the night's halos come into view). Behind the loading screen, every
    /// variant the city's materials use - the kit's slots and its other
    /// materials (each dress of trees), the far layers' and proxies' own
    /// copies, the traffic's car materials, the lamps' halos - is gathered into
    /// one ShaderVariantCollection and warmed. The pictures are the same; only
    /// when the compile happens moves. PSX_CITY_WARMSHADERS=0 (or
    /// ?ab=CityShaderWarm.Enabled:0): as before.
    /// </summary>
    public static class CityShaderWarm
    {
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_CITY_WARMSHADERS") != "0";
        static bool done;

        /// <summary>Warm every variant the given roots' renderers and the city
        /// kit use (once a process). Returns the variants warmed.</summary>
        public static int Warm(IEnumerable<GameObject> roots)
        {
            if (!Enabled || done || !Application.isPlaying) return 0;
            done = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var mats = new HashSet<Material>();
            var kit = CityKit.Get();
            if (kit != null)
            {
                if (kit.slots != null) foreach (var m in kit.slots) Add(mats, m);
                if (kit.trees != null) foreach (var m in kit.trees) Add(mats, m);
                Add(mats, kit.lampPost); Add(mats, kit.bank); Add(mats, kit.furniture); Add(mats, kit.paint);
                Add(mats, kit.signs); Add(mats, kit.guide); Add(mats, kit.signals);
            }
            if (roots != null)
                foreach (var go in roots)
                    if (go != null)
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                            foreach (var m in r.sharedMaterials) Add(mats, m);
            // the traffic's cars (CarModelLibrary has loaded them: CityWorld's warm)
            foreach (var key in TrafficSystem.Keys)
            {
                var def = CarModelLibrary.Load(key);
                if (def == null) continue;
                foreach (var r in def.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) Add(mats, m);
                if (def.skinMaterials != null) foreach (var m in def.skinMaterials) Add(mats, m);
                Add(mats, def.wheelMaterial); Add(mats, def.lensHeadMaterial); Add(mats, def.lensTailMaterial);
            }
            // every loaded material of the game's own shaders (the player's car, the
            // HUD's), and every PSX shader's plain variant - the materials scripts make
            // at runtime from a shader name (CarLights' lenses and beams, the tyres'
            // spray, the far layers' copies) use those. The browser before-runs stalled
            // 0.6-1.1 s in ONE render at the same two places on every run, Chrome and
            // Firefox alike, with no city work in the frame: a first draw.
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.shader != null && m.shader.name.StartsWith("PSX")) Add(mats, m);
            var plains = new List<Material>();
            foreach (var sh in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (sh == null || !sh.name.StartsWith("PSX") || !sh.isSupported) continue;
                var plain = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                plain.shaderKeywords = System.Array.Empty<string>();
                plains.Add(plain);
                mats.Add(plain);
            }
            var svc = new ShaderVariantCollection();
            int variants = 0, refused = 0;
            var seen = new HashSet<string>();
            foreach (var m in mats)
            {
                var sh = m.shader;
                if (sh == null) continue;
                string[] kw = m.shaderKeywords ?? System.Array.Empty<string>();
                string key = sh.name + "|" + string.Join(" ", kw);
                if (!seen.Add(key)) continue;
                bool any = false;
                foreach (var pt in Passes)
                {
                    try
                    {
                        if (svc.Add(new ShaderVariantCollection.ShaderVariant(sh, pt, kw))) { variants++; any = true; }
                    }
                    catch (System.ArgumentException) { }
                }
                if (!any) refused++;
            }
            double gather = clock.Elapsed.TotalMilliseconds;
            svc.WarmUp();
            foreach (var p in plains) Object.Destroy(p);
            Debug.Log($"[CityShaderWarm] {variants} variants of {seen.Count} shader+keyword sets from {mats.Count} materials ({plains.Count} PSX shaders' plain variants) warmed in {clock.Elapsed.TotalMilliseconds - gather:0} ms (gathered in {gather:0} ms; {refused} sets no pass type took)");
            return variants;
        }

        static readonly PassType[] Passes = { PassType.ScriptableRenderPipelineDefaultUnlit, PassType.ScriptableRenderPipeline, PassType.Normal, PassType.ShadowCaster };

        static void Add(HashSet<Material> set, Material m) { if (m != null) set.Add(m); }
    }
}
