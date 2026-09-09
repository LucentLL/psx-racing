using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The scene's seasonal wardrobe: every material the builder baked five
    /// ways, and the swap that puts the right one on when the scene loads.
    ///
    /// One component per scene, written by the builder. Each entry is a
    /// material as baked (the FALL one, which is what the meshes reference)
    /// and its five variants in <see cref="Seasons"/> order — winter, spring,
    /// summer, fall, snow. On Awake the component reads the calendar off
    /// <see cref="RaceHandoff.CalendarDay"/>, walks every renderer in the
    /// scene, and replaces any material it knows with that day's variant.
    ///
    /// SWAPPED, not tinted at runtime. Tinting a shared material in play
    /// mode edits the asset in the editor, and the materials here are shared
    /// across a thousand terrain chunks; five baked materials cost five
    /// assets and change nothing anywhere else. A swap is also the only way
    /// to change a TEXTURE — and the forest is a texture, not a colour: an
    /// orange maple tinted green is a brown maple.
    ///
    /// The city builds its tiles at runtime, after this has run, so it asks
    /// <see cref="Substitute"/> for each material as it goes rather than
    /// being swept.
    /// </summary>
    public class SeasonDress : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            /// <summary>What the meshes were baked with.</summary>
            public Material baseMat;
            /// <summary>Winter, Spring, Summer, Fall, Snow. Fall is normally
            /// the base itself. A null leaves the base on for that dress.</summary>
            public Material[] variants;
            /// <summary>What kind of thing this is, for the self-test and the
            /// log: "forest", "ground", "far", "grass", "edge".</summary>
            public string role;
        }

        public Entry[] entries;
        /// <summary>Whether this scene wants rain and snow drawn. Off for
        /// interiors, on for anywhere with a sky.</summary>
        public bool weatherFx = true;

        /// <summary>The dress the most recent Awake put on, or -1.</summary>
        public static int AppliedDress { get; private set; } = -1;

        // Every material this scene knows about, base and variants alike,
        // to the entry that owns it — so a renderer already wearing a
        // variant (a second Apply) is recognised and re-dressed.
        static readonly Dictionary<Material, Entry> known = new Dictionary<Material, Entry>();

        void Awake()
        {
            Register();
            Apply(Seasons.CurrentDress);
            if (weatherFx) WeatherFx.Ensure(Seasons.CurrentWeather);
        }

        void Register()
        {
            // One scene at a time: the previous scene's materials are gone
            // with it and must not be answered for.
            known.Clear();
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null || e.baseMat == null) continue;
                known[e.baseMat] = e;
                if (e.variants == null) continue;
                foreach (var v in e.variants)
                    if (v != null) known[v] = e;
            }
        }

        /// <summary>Put dress <paramref name="dress"/> on every renderer in
        /// the scene. Safe to call again with another index.</summary>
        public void Apply(int dress)
        {
            AppliedDress = Mathf.Clamp(dress, 0, Seasons.DressCount - 1);
            if (entries == null || entries.Length == 0) return;
            var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int swapped = 0;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !known.TryGetValue(m, out var e)) continue;
                    var want = VariantOf(e, AppliedDress);
                    if (want != null && want != m) { mats[i] = want; changed = true; }
                }
                if (changed) { r.sharedMaterials = mats; swapped++; }
            }
            if (swapped > 0)
                Debug.Log("[SeasonDress] " + Seasons.DressNames[AppliedDress] + " on " + swapped + " renderers.");
        }

        static Material VariantOf(Entry e, int dress)
        {
            if (e.variants != null && dress < e.variants.Length && e.variants[dress] != null)
                return e.variants[dress];
            return e.baseMat;
        }

        /// <summary>The material to use in place of <paramref name="m"/> for
        /// the dress currently applied — <paramref name="m"/> itself when it
        /// is not seasonal, or when no dress has been applied yet.</summary>
        public static Material Substitute(Material m)
        {
            if (m == null || AppliedDress < 0 || !known.TryGetValue(m, out var e)) return m;
            return VariantOf(e, AppliedDress) ?? m;
        }
    }
}
