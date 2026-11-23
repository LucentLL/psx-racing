using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// HEADLIGHTS ON IN THE TUNNEL (tunnel2, owner 2026-11-23: the real Parkway bores are unlit and their signs tell
    /// drivers to turn the lights on; by day ours was a blind black hole). By day, when the car the camera follows
    /// comes within <see cref="ZoneM"/> of a mouth of a stage bore, every car's lights come on (the lights are one
    /// switch for the field: the rivals in the bore light up too), and go back to the hour's choice once it is that
    /// far past the other mouth. In the bore the beam is the night beam over the eye's gain (as NEW GAME's tunnel
    /// does), eased in and out over <see cref="EaseS"/> so it never pops; the exposure's own slow open
    /// (ExposureAdapt, capped at x1.6 in a stage bore) is unchanged. The bore is read from the builder's
    /// TunnelLampRow (the row of heads along its wall, built whether or not the lamps are lit).
    /// PSX_TUNNEL3=0: off (the cars keep the hour's lights).
    /// </summary>
    public class TunnelHeadlights : MonoBehaviour
    {
        public static bool On => System.Environment.GetEnvironmentVariable("PSX_TUNNEL3") != "0";
        public const float ZoneM = 50f, EaseS = 0.6f;
        const float BoreLatM = 7f;

        List<Vector3[]> bores = new List<Vector3[]>();
        bool applied;
        float beamK;
        /// <summary>For the tools: is the tunnel holding the lights on now, and is the car in a bore?</summary>
        public static bool Holding { get; private set; }
        public static bool InBore { get; private set; }

        public static void Ensure(UnityEngine.SceneManagement.Scene scene)
        {
            if (!Application.isPlaying) return;
            var rows = Object.FindObjectsByType<TunnelLampRow>(FindObjectsInactive.Include);
            List<Vector3[]> list = null;
            foreach (var r in rows)
                if (r != null && r.gameObject.scene == scene && r.heads != null && r.heads.Length >= 2)
                    (list ??= new List<Vector3[]>()).Add(r.heads);
            if (list == null) return;
            var go = new GameObject("TunnelHeadlights");
            go.AddComponent<TunnelHeadlights>().bores = list;
        }

        /// <summary>Where <paramref name="p"/> is along a bore's row: metres from its first head (negative before
        /// it, past its length after it) and how far off the row's line; false if it is nowhere near.</summary>
        static bool Along(Vector3[] row, Vector3 p, out float along, out float len)
        {
            along = 0f; len = 0f;
            float best = float.MaxValue, acc = 0f;
            for (int i = 0; i + 1 < row.Length; i++)
            {
                Vector3 a = row[i], b = row[i + 1]; a.y = 0f; b.y = 0f;
                Vector3 q = new Vector3(p.x, 0f, p.z);
                Vector3 ab = b - a; float l = ab.magnitude;
                if (l < 1e-3f) continue;
                float t = Vector3.Dot(q - a, ab / l);
                // the ends run on along their own segment (the approach and the run-out)
                float tc = i == 0 ? Mathf.Min(t, l) : i + 2 == row.Length ? Mathf.Max(t, 0f) : Mathf.Clamp(t, 0f, l);
                float lat = Vector3.Distance(q, a + ab / l * tc);
                if (lat < best) { best = lat; along = acc + tc; }
                acc += l;
            }
            len = acc;
            // the row stands on one wall: the far lane is up to a bore's width off it
            return best < BoreLatM + 4f;
        }

        void LateUpdate()
        {
            if (ExposureAdapt.ForceTunnel) return;   // NEW GAME's tunnel drives the lights itself
            var rm = RaceManager.Instance;
            Transform car = rm != null && rm.playerCar != null ? rm.playerCar.transform
                          : ChaseCamera.Active != null ? ChaseCamera.Active.target : null;
            bool zone = false, bore = false;
            if (car != null)
                foreach (var row in bores)
                    if (Along(row, car.position, out float a, out float len))
                    {
                        if (a > -ZoneM - 4f && a < len + ZoneM + 4f) zone = true;
                        if (a > -6f && a < len + 6f) bore = true;
                    }
            InBore = bore;
            if (zone && !applied)
            {
                // only by day: an hour (or weather) that has them on already keeps them as it is
                if (!CarLights.AllOn) { CarLights.SetAll(true); applied = true; }
            }
            else if (applied && !zone)
            {
                applied = false;
                CarLights.SetAll(TimeOfDay.At(TimeOfDay.Current).lightsOn);
            }
            else if (applied && !CarLights.AllOn) CarLights.SetAll(true);   // an hour re-applied in the bore
            Holding = applied;
            // the beam: the night beam in the dark of the bore, eased
            float want = applied && bore ? 1f : 0f;
            beamK = Mathf.MoveTowards(beamK, want, Time.deltaTime / EaseS);
            if (applied || beamK > 0f)
            {
                float inBore = Mathf.Clamp(CarLights.BeamIntensity / Mathf.Max(1f, ExposureAdapt.Current),
                                           CarLights.BeamIntensityDay, CarLights.BeamIntensity);
                CarLights.BeamIntensityOverride = beamK > 0f ? Mathf.Lerp(CarLights.BeamIntensityDay, inBore, beamK) : 0f;
            }
        }

        void OnDestroy()
        {
            if (applied) CarLights.SetAll(TimeOfDay.At(TimeOfDay.Current).lightsOn);
            if (beamK > 0f) CarLights.BeamIntensityOverride = 0f;
            Holding = InBore = false;
        }
    }
}
