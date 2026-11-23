using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// SODIUM LAMPS IN THE TUNNELS (the lightlook round, 2026-11-23). The owner's
    /// second reference clip is a sodium-lit tunnel: two rows of lamps along the
    /// ceiling to the vanishing point, the walls and the road washed orange. Our
    /// bores (the stage builder's BuildStageTunnels: Little Switzerland, Blowing
    /// Rock) had no light at all - a black tube with a headlit patch.
    ///
    /// CODE ONLY, no scene data: the catalogue's tunnel table is in metres along
    /// the forward bake (the same table WeatherShelter shelters the bores by), so
    /// the lamps are placed at runtime in the venue's own scene: two rows,
    /// <see cref="RowShare"/> of the bore's half-width either side of the road's
    /// centre line, <see cref="HeadUnderM"/> under the ceiling (the builder's
    /// TunnelH, 5.2 m), every <see cref="PitchM"/> metres from portal to portal.
    /// They are an ordinary NightGlow (one halo draw for every lamp, the lightlook
    /// points included) registered as LIT-STREET lamps, so the lamp field and the
    /// far field wash the walls and the road the whole length of the bore at any
    /// distance (no slot limit), in high-pressure SODIUM (<see cref="Sodium"/> -
    /// a 1999 tunnel's lamp; no LEDs). Lit with the street lamps (the hour's
    /// switch, NightGlow.SetAll), so they come up with the night like every other
    /// lamp, never with a tile. PSX_LIGHT_LOOK_TUNNELS=0 (or the whole look off at
    /// start): none.
    /// </summary>
    public class TunnelLamps : MonoBehaviour
    {
        /// <summary>High-pressure sodium, sRGB (the colour the street lamps were
        /// before the owner's "too orange" of 2026-09-21; a tunnel's is sodium).</summary>
        public static readonly Color Sodium = new Color(1.00f, 0.66f, 0.32f);
        public const float PitchM = 8f, RowShare = 0.55f, HeadUnderM = 0.35f;
        const float BoreH = 5.2f, BoreWallOut = 1.1f;
        /// <summary>A tunnel lamp's light (x the street lamp's): a 150 W head
        /// five metres over the road, two rows, eight metres apart.</summary>
        public const float Gain = 0.15f;

        static UnityEngine.SceneManagement.Scene madeScene;
        /// <summary>Lamps placed in this scene (tests, the shots' log).</summary>
        public static int Count { get; private set; }

        /// <summary>Once per scene load, in the venue's own scene (StreetLights'
        /// driver frame calls it; a cheap no-op after the first).</summary>
        public static void EnsureVenueLamps()
        {
            if (!Application.isPlaying) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene == madeScene) return;
            madeScene = scene;
            Count = 0;
            StreetLights.FieldHueOverride = null;
            if (!(LightLook.On && LightLook.Tunnels)) return;
            var def = RaceHUD.VenueDef();
            if (def == null || !def.stage || def.tunnels == null || def.tunnels.Length == 0) return;
            string venueScene = def.Reversed ? def.reverseOf : def.IsSprintVariant ? def.sprintOf : def.id;
            if (venueScene != scene.name) return;
            TrackCatalog.EnsureStage(def);
            var pts = def.stagePts;
            if (pts == null || pts.Length < 2) return;
            int n = pts.Length;
            float sp = TrackCatalog.Spacing;
            float halfW = def.roadWidth * 0.5f + BoreWallOut;
            var heads = new List<Vector3>();
            float lap = Mathf.Max(def.LengthM, 1f);
            for (float m = 0f; m < (n - 1) * sp; m += PitchM)
            {
                if (!TrackCatalog.InTunnel(def, m) || !TrackCatalog.InTunnel(def, Mathf.Min(m + PitchM * 0.5f, lap))) continue;
                float f = m / sp;
                int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, n - 2);
                float t = f - i;
                Vector3 c = Vector3.Lerp(pts[i], pts[i + 1], t);
                Vector3 fwd = pts[Mathf.Min(i + 2, n - 1)] - pts[Mathf.Max(i - 1, 0)];
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) continue;
                Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized);
                foreach (float side in new[] { -1f, 1f })
                    heads.Add(c + right * (side * halfW * RowShare) + Vector3.up * (BoreH - HeadUnderM));
            }
            if (heads.Count == 0) return;
            var go = new GameObject("TunnelLamps");
            go.AddComponent<TunnelLamps>();
            var glow = go.AddComponent<NightGlow>();
            glow.cityField = true;
            glow.lampColour = Sodium;
            glow.poolGain = Gain;
            StreetLights.FieldHueOverride = Sodium;
            glow.Init(heads);
            Count = heads.Count;
        }
    }
}
