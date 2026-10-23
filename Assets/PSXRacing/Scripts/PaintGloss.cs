using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// THE FINISH (owner, 2026-10-23: "Car paint should have reflection when
    /// car is clean/washed (potentially dirty cars or cars with poor paint
    /// (from age or damage) do not reflect) and in rain"). How much of its
    /// lacquer's mirror a car's paint keeps - 1 a clean car in good paint,
    /// toward 0 a filthy one or a dead finish - on PSX/CarPaint's _PSXGloss.
    /// The rain's water film (_PSXCarWet, CarLights) goes over any of them in
    /// the shader, so every car shines in the wet.
    /// </summary>
    public static class PaintGloss
    {
        public static readonly int GlossId = Shader.PropertyToID("_PSXGloss");
        public static readonly int CarWetId = Shader.PropertyToID("_PSXCarWet");
        const string PaintShader = "PSX/CarPaint";
        static readonly string[] PaintFaults = { "paint_fade", "paint_bubble", "minor_rust", "panel_rust" };
        static MaterialPropertyBlock block;

        /// <summary>An owned car's shine: its paint (dead below 25, full from
        /// 90), each paint fault on it, and the road dirt since its last wash.</summary>
        public static float Of(OwnedCar car)
        {
            if (car == null) return 1f;
            float g = Mathf.InverseLerp(25f, 90f, car.paint);
            if (car.faults != null)
                foreach (var f in car.faults)
                    if (f != null && System.Array.IndexOf(PaintFaults, f.id) >= 0) g *= 0.6f;
            g *= 1f - 0.85f * Mathf.Clamp01(car.dirt);
            return Mathf.Clamp01(g);
        }

        /// <summary>The shine of a car in a scene: the player's off the save,
        /// a rival's well kept (0.8..1), traffic anything from tired to new.</summary>
        public static float For(GameObject go)
        {
            var cc = go.GetComponent<CarController>();
            if (cc != null && go.GetComponent<AIDriver>() == null)
            {
                if (!Edition.HasCareer || !LifeSimManager.HasSave) return 1f;
                var s = LifeSimManager.State;
                return s == null ? 1f : Of(s.FindCar(RaceHandoff.CarId) ?? s.ActiveCar);
            }
            float h = Mathf.Repeat(Mathf.Sin(go.GetHashCode() * 12.9898f) * 43758.5453f, 1f);
            return cc != null ? Mathf.Lerp(0.8f, 1f, h) : Mathf.Lerp(0.35f, 1f, h);
        }

        /// <summary>The shine onto every PSX/CarPaint renderer under a car,
        /// keeping what the other writers (the lamp spill) put in its block.</summary>
        public static void WriteAll(Transform root, float gloss)
        {
            if (root == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != PaintShader) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(GlossId, gloss);
                r.SetPropertyBlock(block);
            }
        }
    }
}
