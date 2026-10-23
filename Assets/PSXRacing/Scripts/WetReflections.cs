using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing
{
    /// <summary>
    /// THE WORLD IN THE WATER (2026-10-17): houses, trees and cars in a wet
    /// road, each beneath where it really stands. The game half; the drawing
    /// is SpeedBlurFeature's WetPass and PSX/Lit's "PSXWetSSR" pass
    /// (PSXWetSSR.cginc).
    ///
    /// WHY THE SCREEN. The wet road's first pass mirrors the hour's sky, which
    /// is right from every pixel because the sky is infinitely far. From
    /// 2026-10-13 it also read CarReflections' panorama - six views from the
    /// player's roof - and the owner's rain videos showed what that does on a
    /// road: every pixel showed what the CAR sees in that direction, so a
    /// house swept across the road as he drove past it (reverted in bc65f14a).
    /// Something near has to be looked up from where the PIXEL is. The frame
    /// itself is the one picture of the world taken from the camera, and its
    /// depth buffer says how far away each pixel of it is: march the mirrored
    /// ray across the screen against the depth, take the colour where it
    /// meets something. A house's reflection is then under the house in
    /// every frame because it IS the house's own pixels, wherever the camera
    /// stands. What the screen does not hold (the ray leaves it, or reaches
    /// the sky) stays the sky the first pass laid down.
    ///
    /// WHO PAYS. Only a PC (never <see cref="Application.isMobilePlatform"/>),
    /// only with VISUALS > REFLECTIONS on, only when the hour is wet, only the
    /// game camera (the one <see cref="PSXCameraOutput"/> drives - never the
    /// mirror, the pizza cam or CarReflections' probe). Then: one copy of the
    /// frame and one more draw of the WET renderers, whose wet pixels march.
    /// No pipeline asset changes: the copy is the pass's own, and the depth
    /// is read straight out of the camera's depth attachment (the motion
    /// blur's way), so the Mobile asset WebGL ships keeps RequireDepthTexture
    /// and RequireOpaqueTexture off and a phone's frame is untouched.
    ///
    /// WHICH RENDERERS. The ones wearing a PSX/Lit material with _Wet above
    /// 0 (roads, decks, kerbs, pavements, drives), tagged with
    /// <see cref="WetRenderingLayer"/> when their scene loads, or by the code
    /// that builds them at runtime (CityWorld's tiles). A tagged renderer's
    /// dry submesh (a tile's grass beside its pavement) costs its vertices and
    /// nothing else: the pass throws every vertex of a dry material outside
    /// the clip volume.
    /// </summary>
    public static class WetReflections
    {
        /// <summary>The rendering layer the wet renderers carry (bits 20 and
        /// 21 are SpeedBlur's car masks).</summary>
        public const uint WetRenderingLayer = 1u << 22;

        /// <summary>PSX/Lit's second pass's LightMode.</summary>
        public const string PassTag = "PSXWetSSR";

        static readonly int WetId = Shader.PropertyToID("_Wet");
        static readonly int WetnessId = Shader.PropertyToID("_PSXWetness");
        /// <summary>PSXWet.cginc's WET_MIN: below it nothing is wet.</summary>
        const float WetMin = 0.001f;

        /// <summary>Tools only: -1 the game's rule; 0 never; 1 on for
        /// <see cref="PreviewCamera"/> whatever the platform or the setting
        /// (still only when the hour is wet). <see cref="PreviewSet"/>.</summary>
        public static int Preview = -1;
        public static Camera PreviewCamera;
        /// <summary>The mirror's strength (1 in the game); tools may turn it.</summary>
        public static float Strength = 1f;

        public static void PreviewSet(Camera cam, int mode)
        {
            PreviewCamera = cam;
            Preview = mode;
        }

        /// <summary>Does <paramref name="cam"/> draw the world in the water
        /// this frame?</summary>
        public static bool WantedFor(Camera cam)
        {
            if (cam == null || Preview == 0) return false;
            if (Preview == 1)
            {
                if (cam != PreviewCamera) return false;
            }
            else
            {
                if (Application.isMobilePlatform || !ReflectionPrefs.On) return false;
                if (cam.cameraType != CameraType.Game) return false;
                if (!cam.TryGetComponent(out PSXCameraOutput output) || !output.isActiveAndEnabled) return false;
            }
            return Shader.GetGlobalFloat(WetnessId) > WetMin;
        }

        // ------------------------------------------------------------------
        //  Which renderers are wet
        // ------------------------------------------------------------------

        static Shader lit;
        static readonly Dictionary<Material, bool> wetCache = new Dictionary<Material, bool>();
        static readonly List<MeshRenderer> scratch = new List<MeshRenderer>();

        /// <summary>A PSX/Lit material that can be wet.</summary>
        public static bool IsWet(Material m)
        {
            if (m == null) return false;
            if (wetCache.TryGetValue(m, out bool wet)) return wet;
            if (lit == null) lit = Shader.Find("PSX/Lit");
            wet = lit != null && m.shader == lit && m.HasProperty(WetId) && m.GetFloat(WetId) > 0f;
            wetCache[m] = wet;
            return wet;
        }

        /// <summary>Tag <paramref name="r"/> if any of its materials can be
        /// wet. True when it is (or already was) tagged.</summary>
        public static bool Tag(Renderer r)
        {
            if (r == null) return false;
            if ((r.renderingLayerMask & WetRenderingLayer) != 0) return true;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!IsWet(mats[i])) continue;
                r.renderingLayerMask |= WetRenderingLayer;
                return true;
            }
            return false;
        }

        /// <summary>Every mesh renderer in <paramref name="scene"/>, inactive
        /// ones included. Returns how many are wet.</summary>
        public static int TagScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, scratch);
                foreach (var r in scratch) if (Tag(r)) n++;
            }
            scratch.Clear();
            return n;
        }

        /// <summary>Every loaded scene (the edit-mode tools: no sceneLoaded
        /// comes to a scene a tool opened).</summary>
        public static int TagAll()
        {
            wetCache.Clear();
            int n = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) n += TagScene(SceneManager.GetSceneAt(i));
            return n;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // Unsubscribe first: with domain reload off a second play session
            // would otherwise tag twice (harmless, but twice the walk).
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A material can be unloaded with its scene and another reborn
            // at its address: the cache is per load.
            wetCache.Clear();
            int n = TagScene(scene);
            if (n > 0) Debug.Log("[WetReflections] " + scene.name + ": " + n + " wet renderer(s)");
        }
    }
}
