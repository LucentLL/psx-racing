using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// A city restaurant's ROOM: its tables, fryers, shelves and cans, drawn
    /// only when they can be SEEN (Charlotte WP-07, plan critic C1).
    ///
    /// The drive-thru and the pizzeria are the two props with real
    /// interiors, and those interiors were 300-odd renderers each - more draw
    /// calls than the whole street they stand on, paid through opaque walls.
    /// The city variant (CityPropBaker) merges the shell into a handful of
    /// draws and leaves the room here, switched off. What is room was decided
    /// by looking (no ray from outside reaches it), and the city props wear
    /// opaque windows - the pickup window too - so from outside the room is
    /// never in the picture (CityPropBaker.CompareShots renders it both ways
    /// from the street, the bay's chase camera and the driver's seat, and
    /// counts the pixels that change). It can be seen only from INSIDE the
    /// building, or through a door standing open, so those are the two
    /// things that switch it on:
    ///
    ///   - the camera or the player (walker, else the city's car) inside the
    ///     room's hull or within <see cref="InM"/> of it (a camera pushed
    ///     against a wall, somebody in a doorway); off again past
    ///     <see cref="OutM"/>;
    ///   - any of the building's hinged doors off its stop (a SwingDoor opens
    ///     for whoever comes within 3.4 m of it), checked every frame so the
    ///     room is there the frame the leaf starts to move.
    ///
    /// Distance alone switches nothing: the first cut used a 40 m radius,
    /// which turned the room on for a car driving past on the restaurant's
    /// own road, where it cost 270-370 draws nobody could see (WP-07 review).
    /// The order bay, the doors and every piece's collider stay where they
    /// were, so ORDER and walking in work exactly as before.
    ///
    /// Baked with the room OFF. The budget probe (edit mode, no Update)
    /// applies the same rule at its eye and car through <see cref="Apply"/>.
    /// </summary>
    public class CityPropInterior : MonoBehaviour
    {
        /// <summary>Metres outside the room's hull within which the room is
        /// drawn, and the farther one at which it goes again (no flicker on
        /// the line).</summary>
        public const float InM = 1.0f, OutM = 2.0f;
        const float CheckEvery = 0.25f;

        public Renderer[] interior;
        /// <summary>The room's box in the prop root's frame (unscaled), baked
        /// by CityPropBaker from the room pieces' own meshes.</summary>
        public Bounds hull;

        SwingDoor[] doors;
        bool shown;
        float nextCheck;

        void Awake() => doors = GetComponentsInChildren<SwingDoor>(true);

        void OnEnable()
        {
            // a staggered start, so ten restaurants do not all look at once
            nextCheck = Time.unscaledTime + Random.value * CheckEvery;
            Show(false);
        }

        void Update()
        {
            // a door on the move is a few bools: every frame
            if (AnyDoorOpen()) { if (!shown) Show(true); return; }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + CheckEvery;
            bool want = ViewerInside(shown ? OutM : InM);
            if (want != shown) Show(want);
        }

        /// <summary>Is any of this building's door leaves off its stop?</summary>
        public bool AnyDoorOpen()
        {
            if (doors == null) return false;
            foreach (var d in doors) if (d != null && d.IsOpen) return true;
            return false;
        }

        /// <summary>Is the camera or the player within <paramref name="margin"/>
        /// metres of the room's hull? The player is the walker on foot, else
        /// the city's car; the camera is whatever is drawing.</summary>
        bool ViewerInside(float margin)
        {
            var walker = OnFoot.FirstPersonWalk.Current;
            if (walker != null && Inside(walker.transform.position, margin)) return true;
            var w = CityWorld.Active;
            if (w != null && w.player != null && Inside(w.player.position, margin)) return true;
            var cam = Camera.main;
            return cam != null && Inside(cam.transform.position, margin);
        }

        /// <summary>The offset of a world point from the hull's centre and the
        /// hull's half size, both in metres along the prop's own axes (the
        /// prop may stand turned and scaled).</summary>
        void Frame(Vector3 world, out Vector3 d, out Vector3 e)
        {
            var s = transform.lossyScale;
            var local = Quaternion.Inverse(transform.rotation) * (world - transform.position);
            d = local - Vector3.Scale(hull.center, s);
            e = Vector3.Scale(hull.extents, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        /// <summary>A world point within <paramref name="margin"/> metres of
        /// the room's hull.</summary>
        public bool Inside(Vector3 world, float margin)
        {
            Frame(world, out var d, out var e);
            return Mathf.Abs(d.x) <= e.x + margin && Mathf.Abs(d.y) <= e.y + margin && Mathf.Abs(d.z) <= e.z + margin;
        }

        /// <summary>Metres from a world point to the room's hull (0 inside).
        /// For the probe and the play check.</summary>
        public float DistanceTo(Vector3 world)
        {
            Frame(world, out var d, out var e);
            return new Vector3(Mathf.Max(0f, Mathf.Abs(d.x) - e.x), Mathf.Max(0f, Mathf.Abs(d.y) - e.y),
                               Mathf.Max(0f, Mathf.Abs(d.z) - e.z)).magnitude;
        }

        /// <summary>The nearest point of the room's hull to a world point (the
        /// point itself when inside).</summary>
        public Vector3 ClosestPoint(Vector3 world)
        {
            Frame(world, out var d, out var e);
            var s = transform.lossyScale;
            var c = Vector3.Scale(hull.center, s);
            var q = c + new Vector3(Mathf.Clamp(d.x, -e.x, e.x), Mathf.Clamp(d.y, -e.y, e.y), Mathf.Clamp(d.z, -e.z, e.z));
            return transform.position + transform.rotation * q;
        }

        /// <summary>The same rule without Update, for edit mode (the budget
        /// probe, the room check): the camera at <paramref name="eye"/>, the
        /// player's car at <paramref name="car"/>, and a door counted open
        /// when the car is within its opening radius (the door watches the
        /// car). Sets the room and says whether it is drawn.</summary>
        public bool Apply(Vector3 eye, Vector3 car)
        {
            bool on = Inside(eye, InM) || Inside(car, InM) || DoorOpensFor(car);
            Show(on);
            return on;
        }

        /// <summary>Would a door of this building swing open for a car here?</summary>
        public bool DoorOpensFor(Vector3 car)
        {
            var ds = doors ?? GetComponentsInChildren<SwingDoor>(true);
            foreach (var d in ds)
            {
                if (d == null) continue;
                var o = car - d.transform.position; o.y = 0f;
                if (o.magnitude <= d.openRadius) return true;
            }
            return false;
        }

        /// <summary>The room on or off (the play check reads it).</summary>
        public bool Shown => shown;

        void Show(bool on)
        {
            shown = on;
            if (interior == null) return;
            foreach (var r in interior) if (r != null) r.enabled = on;
        }
    }
}
