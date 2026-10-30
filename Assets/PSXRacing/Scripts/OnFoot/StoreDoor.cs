using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// A filling station's shop door, for somebody on foot: where
    /// ForecourtMode hangs the store's GO INSIDE prompt (city fuel r1,
    /// 2026-10-30).
    ///
    /// It was an empty object named "StoreDoor" that ForecourtMode found with a
    /// search over every Transform in the scene, nearest the parked car. In
    /// Charlotte the doors stream in and out with the stations' tiles, and a
    /// scene-wide search on every get-out would walk tens of thousands of
    /// city objects; so the marker now adds itself to <see cref="All"/> when it
    /// is enabled and leaves when it goes, and the door nearest the car is a
    /// look down a short list. (A scene built before the marker had this
    /// component still works: ForecourtMode falls back to the name.)
    /// </summary>
    public class StoreDoor : MonoBehaviour
    {
        public const string ObjectName = "StoreDoor";

        static readonly List<StoreDoor> all = new List<StoreDoor>();
        public static IReadOnlyList<StoreDoor> All => all;

        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() => all.Remove(this);

        /// <summary>The door nearest a point, within <paramref name="maxM"/>
        /// (null: none).</summary>
        public static Transform Nearest(Vector3 p, float maxM = float.MaxValue)
        {
            Transform best = null;
            float bestD = maxM * maxM;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d == null) continue;
                float dd = (d.transform.position - p).sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = d.transform; }
            }
            return best;
        }
    }
}
