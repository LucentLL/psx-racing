using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// A building you can walk into, as far as the weather is concerned: a
    /// box, in world space, inside which nothing falls.
    ///
    /// Owner, 2026-09-26: "Buildings should not let weather through the roof."
    /// WeatherFx first asked the sky straight overhead for a collider, which is
    /// right under a bridge deck and wrong in the house: house_hero's collider
    /// shell is walls and floors - the roof and the ceilings were never given
    /// colliders, because nothing ever stood on them - so a ray up from the
    /// bedroom met nothing and the snow fell on the bed. A building's own
    /// footprint, taken off its renderers at build time and saved with the
    /// scene, is the honest answer for a thing with a roof on it.
    /// </summary>
    public class WeatherShelter : MonoBehaviour
    {
        /// <summary>World-space centre and size of the sheltered volume.</summary>
        public Vector3 center, size;

        static readonly List<WeatherShelter> all = new List<WeatherShelter>();

        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() => all.Remove(this);

        /// <summary>Is any shelter's box within <paramref name="reach"/> of
        /// <paramref name="p"/>?</summary>
        public static bool AnyNear(Vector3 p, float reach)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null) continue;
                Vector3 d = p - s.center, h = s.size * 0.5f;
                float dx = Mathf.Max(0f, Mathf.Abs(d.x) - h.x);
                float dy = Mathf.Max(0f, Mathf.Abs(d.y) - h.y);
                float dz = Mathf.Max(0f, Mathf.Abs(d.z) - h.z);
                if (dx * dx + dy * dy + dz * dz <= reach * reach) return true;
            }
            return false;
        }

        /// <summary>Is <paramref name="p"/> inside any building's shelter?</summary>
        public static bool Contains(Vector3 p)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null) continue;
                Vector3 d = p - s.center, h = s.size * 0.5f;
                if (Mathf.Abs(d.x) <= h.x && Mathf.Abs(d.y) <= h.y && Mathf.Abs(d.z) <= h.z) return true;
            }
            return false;
        }

        /// <summary>
        /// Shelter a building: the union of its colliders' bounds, pulled in a
        /// little at the sides (eaves and porch posts overhang the rooms, and a
        /// box that reaches the tips of them takes the weather off the path to
        /// the door) and down to the ground. Called by the scene builders on the
        /// buildings a player can walk into.
        /// </summary>
        public static WeatherShelter AddTo(GameObject building, float inset = 0.6f,
                                           GameObject roof = null)
        {
            if (building == null) return null;
            // A collider made this build has no bounds until physics hears of it.
            Physics.SyncTransforms();
            // COLLIDERS FIRST: a pack building's renderers bring its garden -
            // house_hero ships an apron and planting beds out front - and a
            // box round those would keep the rain off the drive. Its collider
            // shell is the rooms. Renderers only for a building with none.
            bool any = false;
            Bounds b = default;
            foreach (var c in building.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any)
                foreach (var r in building.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            if (!any) return null;
            var s = building.GetComponent<WeatherShelter>();
            if (s == null) s = building.AddComponent<WeatherShelter>();
            Vector3 size = b.size;
            size.x = Mathf.Max(1f, size.x - 2f * inset);
            size.z = Mathf.Max(1f, size.z - 2f * inset);
            // Up to the ROOF: a collider shell stops at the top of the upstairs
            // walls, and a flake that vanished there would be seen falling
            // through the last two metres of roof from the street. The drawn
            // model's top is the ridge (its garden is all lower than that).
            float top = b.max.y;
            if (roof != null)
                foreach (var r in roof.GetComponentsInChildren<Renderer>(true))
                    top = Mathf.Max(top, r.bounds.max.y);
            top = Mathf.Max(top, b.min.y + 3f);
            s.center = new Vector3(b.center.x, (b.min.y + top) * 0.5f, b.center.z);
            size.y = top - b.min.y;
            s.size = size;
            return s;
        }
    }
}
