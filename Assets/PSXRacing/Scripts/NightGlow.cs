using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Scenery that only lights up after dark: the lamps along the barriers and
    /// the pools they throw on the tarmac.
    ///
    /// One of these per scene, on a parent holding every glow quad, rather than
    /// a component per lamp — a circuit carries thirty of them and thirty
    /// Awake calls to toggle a renderer is thirty too many. The lamp POSTS are
    /// ordinary scenery and stay visible all day; only the light does not.
    /// </summary>
    public class NightGlow : MonoBehaviour
    {
        static readonly List<NightGlow> all = new List<NightGlow>();
        static bool on;

        Renderer[] glows;

        void Awake() => glows = GetComponentsInChildren<Renderer>(true);

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            Apply();
        }

        void OnDisable() => all.Remove(this);

        /// <summary>Called by <see cref="TimeOfDay.Apply"/>; the hour owns
        /// this, the same as it owns the cars' headlights.</summary>
        public static void SetAll(bool lit)
        {
            on = lit;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i] == null) { all.RemoveAt(i); continue; }
                all[i].Apply();
            }
        }

        void Apply()
        {
            if (glows == null) return;
            foreach (var r in glows) if (r != null) r.enabled = on;
        }
    }
}
