using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// A city restaurant's ROOM: its tables, fryers, shelves and cans, drawn
    /// only while somebody is near enough to see them (Charlotte WP-07, plan
    /// critic C1).
    ///
    /// The drive-thru and the pizzeria are the two props with real
    /// interiors, and those interiors were 300-odd renderers each - more draw
    /// calls than the whole street they stand on, paid from half a kilometre
    /// away through opaque walls. The city variant (CityPropBaker) merges the
    /// shell into a handful of draws and leaves the room here, switched off
    /// until the viewer is within <see cref="OnM"/>. Nothing else changes:
    /// the order bay, the hinged doors and every piece's collider stay where
    /// they were, so ORDER and walking in work exactly as before.
    ///
    /// Baked with the room OFF, which is also what the budget probe (edit
    /// mode, no Update) measures - the far state.
    /// </summary>
    public class CityPropInterior : MonoBehaviour
    {
        /// <summary>Metres from the lot's centre at which the room appears,
        /// and the farther one at which it goes again (no flicker on the line).</summary>
        public const float OnM = 40f, OffM = 46f;
        const float CheckEvery = 0.25f;

        public Renderer[] interior;

        bool shown;
        float nextCheck;

        void OnEnable()
        {
            // a staggered start, so ten restaurants do not all look at once
            nextCheck = Time.unscaledTime + Random.value * CheckEvery;
            Show(false);
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + CheckEvery;
            float d2 = NearestViewerSq(transform.position);
            if (d2 < 0f) return;
            bool want = shown ? d2 < OffM * OffM : d2 < OnM * OnM;
            if (want != shown) Show(want);
        }

        /// <summary>Squared distance to the nearer of the two things the
        /// picture is about: the player's car (the camera trails it, and
        /// catches up a beat after a reset or a teleport) and the camera
        /// (which rides the walker's head on foot, when the car may be parked
        /// further off). -1 when there is neither.</summary>
        static float NearestViewerSq(Vector3 p)
        {
            float best = -1f;
            var w = CityWorld.Active;
            if (w != null && w.player != null) best = (w.player.position - p).sqrMagnitude;
            var cam = Camera.main;
            if (cam != null)
            {
                float d = (cam.transform.position - p).sqrMagnitude;
                if (best < 0f || d < best) best = d;
            }
            return best;
        }

        /// <summary>The room on or off (the play check calls it too).</summary>
        public bool Shown => shown;

        void Show(bool on)
        {
            shown = on;
            if (interior == null) return;
            foreach (var r in interior) if (r != null) r.enabled = on;
        }
    }
}
