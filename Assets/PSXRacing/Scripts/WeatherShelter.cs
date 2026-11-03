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

        /// <summary>A ROOM (every building: lit by its own lamps at night, and
        /// a car parked in it sheds its snow), or a tunnel BORE
        /// (<see cref="EnsureVenueBores"/>): no weather falls in it either, but
        /// it has no lamps of its own and a car driving through keeps the
        /// snow on its roof.</summary>
        public bool room = true;

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

        /// <summary>Is <paramref name="p"/> inside any shelter, a building's
        /// or a tunnel bore's?</summary>
        public static bool Contains(Vector3 p) => Inside(p, 0);

        /// <summary>Is <paramref name="p"/> inside a building's room (not a
        /// tunnel bore)?</summary>
        public static bool ContainsRoom(Vector3 p) => Inside(p, 1);

        /// <summary>Is <paramref name="p"/> inside one of the venue's tunnel
        /// bores?</summary>
        public static bool InBore(Vector3 p) => Inside(p, 2);

        /// <summary><paramref name="want"/>: 0 any shelter, 1 rooms only, 2 bores only.</summary>
        static bool Inside(Vector3 p, int want)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || (want == 1 && !s.room) || (want == 2 && s.room)) continue;
                Vector3 d = p - s.center, h = s.size * 0.5f;
                if (Mathf.Abs(d.x) <= h.x && Mathf.Abs(d.y) <= h.y && Mathf.Abs(d.z) <= h.z) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  tunnel bores
        // ------------------------------------------------------------------
        /// <summary>The stage builder's bore (PSXRacingBuilder.Stage.cs): the
        /// tube's walls stand TunnelWallOut past the road's edge and its roof
        /// TunnelH over the centreline.</summary>
        const float BoreWallOut = 1.1f, BoreH = 5.2f;
        /// <summary>Stations per bore box: 8 m, short enough that a box round
        /// a piece of a curving bore stays in the rock beside it.</summary>
        const int BoreStep = 2;

        static UnityEngine.SceneManagement.Scene boresScene;

        /// <summary>
        /// THE VENUE'S TUNNEL BORES, sheltered (2026-11-03). The PC build's
        /// acoustics run drove the Little Switzerland Tunnel and heard a
        /// CANYON: the drawn tube has no roof collider (nothing drives on a
        /// ceiling) and the mountain over it is cut away for the bore, so a ray
        /// up from the road met nothing - and the rain fell through the roof
        /// the same way. The catalogue's tunnel table is in metres along the
        /// FORWARD bake (<see cref="TrackCatalog.TrackDef.stagePts"/>), which
        /// a reverse twin and a sprint race in unchanged, so the boxes are
        /// world space whichever way the road is driven: one per
        /// <see cref="BoreStep"/> stations of each bore, the tube's width and
        /// height. Made once per scene load, in the venue's own scene only (a
        /// handoff that still names a stage in free roam must not drop its
        /// tunnel into Charlotte). Read by WeatherFx and AcousticSpace.
        /// </summary>
        public static void EnsureVenueBores()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene == boresScene) return;
            boresScene = scene;
            var def = RaceHUD.VenueDef();
            if (def == null || !def.stage) return;
            string venueScene = def.Reversed ? def.reverseOf : def.IsSprintVariant ? def.sprintOf : def.id;
            if (venueScene != scene.name) return;
            TrackCatalog.EnsureStage(def);
            var pts = def.stagePts;
            if (pts == null || pts.Length < 2 || def.tunnels == null || def.tunnels.Length == 0) return;
            int n = pts.Length, made = 0;
            float halfW = def.roadWidth * 0.5f + BoreWallOut;
            var root = new GameObject("TunnelBores");
            for (int i = 0; i + 1 < n; i++)
            {
                if (!TrackCatalog.InTunnel(def, i * TrackCatalog.Spacing)) continue;
                int j = i;
                while (j + 1 < n && j - i < BoreStep && TrackCatalog.InTunnel(def, (j + 1) * TrackCatalog.Spacing)) j++;
                if (j == i) continue;
                var b = new Bounds(pts[i], Vector3.zero);
                for (int k = i; k <= j; k++)
                {
                    Vector3 fwd = pts[Mathf.Min(k + 1, n - 1)] - pts[Mathf.Max(k - 1, 0)];
                    fwd.y = 0f;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized);
                    foreach (float side in new[] { -halfW, halfW })
                    {
                        Vector3 w = pts[k] + right * side;
                        b.Encapsulate(new Vector3(w.x, pts[k].y - 0.5f, w.z));
                        b.Encapsulate(new Vector3(w.x, pts[k].y + BoreH, w.z));
                    }
                }
                var go = new GameObject("Bore");
                go.transform.SetParent(root.transform, false);
                var s = go.AddComponent<WeatherShelter>();
                s.room = false;
                s.center = b.center;
                s.size = b.size;
                made++;
                i = j - 1;
            }
            if (made > 0) return;
            if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
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
