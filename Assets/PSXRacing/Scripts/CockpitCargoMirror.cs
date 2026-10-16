using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE PIZZAS ON THE PASSENGER SEAT, in cockpit view (owner, 2026-10-14:
    /// "I want the pizzas to be stacked in the passenger seat with no pizza
    /// cam"). He chose to MIRROR the live cargo for now: the boxes, lids,
    /// pizzas and bottles are still simulated on PizzaCargo's island at
    /// y = -4000 on his PSX seat, exactly as before - physics, Condition and
    /// grading untouched - and every frame (LateUpdate, after physics) each of
    /// their renderers is copied into the cockpit:
    ///
    ///   pose in cockpit = cockpit seat frame * inverse(island seat frame) * island pose
    ///
    /// The copies are stripped (a MeshFilter and a MeshRenderer sharing the
    /// island's mesh and materials; no collider, no rigidbody) and live under
    /// the cabin's scaled model, so they scale with it.
    ///
    /// THE TWO SEAT FRAMES. The island's: where the bottom of the stack
    /// stands on the pan (PizzaCargo.StackSeatLocal), with the pan's slope.
    /// The cockpit's: the plane a 0.41 m box rests on at the owner's
    /// Passenger_Seat_Surface marker, measured by the converter on his seat
    /// (Layout.stack) - so the stack at rest sits ON his cushion. Both frames
    /// are one definition each, so when the owner puts this seat on the island
    /// ("I intend to replace the seats in the car with the same seat model
    /// from cockpit view after basic testing") only those two change.
    ///
    /// THE FLOOR. The island's footwell is 0.34 m under the pan and two metres
    /// long; his is 0.19 m under the cushion and ends at the firewall. So a
    /// part is mapped by a blend of the seat frame and a FLOOR frame (island
    /// footwell top onto his footwell floor), weighted by how far it has fallen
    /// from the seat towards the floor, and then held inside his passenger
    /// space (door card, console, firewall, floor). One offset per rigid part,
    /// so a lid never leaves its box.
    /// </summary>
    public class CockpitCargoMirror
    {
        readonly Transform root;
        readonly CockpitModel.Layout lay;
        PizzaCargo source;

        class Group
        {
            public Transform anchor;
            public float halfW, halfH;
            public readonly List<int> members = new List<int>();
            public Vector3 offset;
            public float t;
        }

        struct Pair
        {
            public MeshRenderer src;
            public Transform srcT;
            public MeshRenderer dst;
            public Transform dstT;
            public int group;
        }

        readonly List<Pair> pairs = new List<Pair>();
        readonly List<Group> groups = new List<Group>();

        public CockpitCargoMirror(Transform root, CockpitModel.Layout lay)
        {
            this.root = root;
            this.lay = lay;
        }

        public int Count => pairs.Count;

        public void Clear()
        {
            foreach (var p in pairs)
                if (p.dst != null)
                {
                    if (Application.isPlaying) Object.Destroy(p.dst.gameObject);
                    else Object.DestroyImmediate(p.dst.gameObject);
                }
            pairs.Clear();
            groups.Clear();
            source = null;
        }

        /// <summary>Follow <paramref name="cargo"/>; true when copies were
        /// (re)built, so the cabin re-collects its renderers.</summary>
        public bool Sync(PizzaCargo cargo)
        {
            bool rebuilt = false;
            if (cargo != source)
            {
                Clear();
                source = cargo;
                if (cargo != null) { Build(cargo); rebuilt = true; }
            }
            if (source == null || source.Tray == null || lay == null) return rebuilt;
            Map();
            return rebuilt;
        }

        // Nothing on the island is created after BuildIsland (PizzaCargo.
        // ReplayParts says so), so the set of renderers is fixed here.
        void Build(PizzaCargo cargo)
        {
            var parts = cargo.ReplayParts();
            var tray = cargo.Tray;
            var anchors = new HashSet<Transform>();
            foreach (var t in parts) if (t != null && t != tray) anchors.Add(t);
            var seen = new HashSet<MeshRenderer>();
            foreach (var part in parts)
            {
                if (part == null || part == tray) continue;
                // The HIGHEST part above it is the rigid body it moves with:
                // a lid and a packed pizza ride their box.
                if (AnchorOf(part, anchors) != part) continue;
                var g = new Group { anchor = part };
                var b = new Bounds();
                bool any = false;
                foreach (var r in part.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!seen.Add(r)) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var go = new GameObject(r.name + "Mirror");
                    go.layer = root.gameObject.layer;
                    go.hideFlags = root.gameObject.hideFlags;
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var dst = go.AddComponent<MeshRenderer>();
                    dst.sharedMaterials = r.sharedMaterials;
                    g.members.Add(pairs.Count);
                    pairs.Add(new Pair { src = r, srcT = r.transform, dst = dst, dstT = go.transform, group = groups.Count });
                    if (r.enabled) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                }
                if (g.members.Count == 0) continue;
                // Measured as built: the stack lies flat on a level tray.
                var size = tray.InverseTransformVector(b.size);
                g.halfW = 0.5f * Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.z));
                g.halfH = 0.5f * Mathf.Abs(size.y);
                groups.Add(g);
            }
        }

        static Transform AnchorOf(Transform t, HashSet<Transform> anchors)
        {
            Transform best = null;
            for (var p = t; p != null; p = p.parent)
                if (anchors.Contains(p)) best = p;
            return best;
        }

        // ---- The frames ------------------------------------------------------

        Vector3 seatP, floorP;
        Quaternion seatQi, floorQi, cockSeatQ;
        Vector3 cockSeatP, cockFloorP;
        float seatY, floorY;

        void Frames()
        {
            var tray = source.Tray;
            var s = source.StackSeatLocal;
            seatP = tray.TransformPoint(s);
            seatQi = Quaternion.Inverse(tray.rotation * source.StackSeatRotLocal);
            floorP = tray.TransformPoint(new Vector3(s.x, PizzaCargo.FootwellTopLocal, s.z));
            floorQi = Quaternion.Inverse(tray.rotation);
            seatY = s.y;
            floorY = PizzaCargo.FootwellTopLocal;
            cockSeatP = lay.stack.centre;
            cockSeatQ = Quaternion.FromToRotation(Vector3.up, lay.stack.normal.normalized);
            cockFloorP = new Vector3(lay.stack.centre.x, lay.room.floorY, lay.stack.centre.z);
        }

        /// <summary>An island point by the SEAT frame alone, into the cabin's
        /// model frame - what a part resting on the seat gets.</summary>
        public Vector3 MapSeat(Vector3 islandWorld)
        {
            if (source == null) return islandWorld;
            Frames();
            return cockSeatP + cockSeatQ * (seatQi * (islandWorld - seatP));
        }

        Vector3 MapP(Vector3 w, float t) =>
            Vector3.Lerp(cockSeatP + cockSeatQ * (seatQi * (w - seatP)),
                         cockFloorP + floorQi * (w - floorP), t);

        Quaternion MapQ(Quaternion q, float t) =>
            Quaternion.Slerp(cockSeatQ * seatQi * q, floorQi * q, t);

        void Map()
        {
            Frames();
            var tray = source.Tray;
            var room = lay.room;
            foreach (var g in groups)
            {
                // Where the part's middle is, and how far it has fallen off
                // the seat towards the floor (0 on the seat, 1 on the floor).
                var b = new Bounds();
                bool any = false;
                foreach (int i in g.members)
                {
                    var r = pairs[i].src;
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any) { g.offset = Vector3.zero; g.t = 0f; continue; }
                var c = tray.InverseTransformPoint(b.center);
                float bottom = c.y - g.halfH;
                g.t = Mathf.Clamp01((seatY - bottom) / Mathf.Max(0.05f, seatY - floorY));
                var p = MapP(b.center, g.t);
                // Inside his passenger space.
                var q = p;
                // Between the console and the door, whichever side of the
                // cabin the passenger is on (an RHD cabin's layout is mirrored).
                float lo = Mathf.Min(room.consoleX, room.doorX) + g.halfW, hi = Mathf.Max(room.consoleX, room.doorX) - g.halfW;
                q.x = lo <= hi ? Mathf.Clamp(q.x, lo, hi) : (room.consoleX + room.doorX) * 0.5f;
                q.z = Mathf.Min(q.z, room.firewallZ - g.halfW);
                q.y = Mathf.Max(q.y, room.floorY + g.halfH);
                g.offset = q - p;
            }
            foreach (var pr in pairs)
            {
                if (pr.dst == null) continue;
                bool on = pr.src != null && pr.src.enabled && pr.src.gameObject.activeInHierarchy;
                if (pr.dst.enabled != on) pr.dst.enabled = on;
                if (!on) continue;
                var g = groups[pr.group];
                pr.dstT.localPosition = MapP(pr.srcT.position, g.t) + g.offset;
                pr.dstT.localRotation = MapQ(pr.srcT.rotation, g.t);
                pr.dstT.localScale = pr.srcT.lossyScale;
            }
        }
    }
}
