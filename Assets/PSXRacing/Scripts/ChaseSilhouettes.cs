using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// What each body shell looks like FROM BEHIND, for the chase camera's
    /// framing fit (2026-09-28, the NFS U / MW pass).
    ///
    /// "The camera position can make them appear wider if necessary" (owner,
    /// 2026-09-27) — and it has to be told how wide a car LOOKS, which is not
    /// how wide it is. Seen from a lens three metres back, an RX-7 FD reads a
    /// fifth narrower per metre of real width than a '69 Charger: the FD's
    /// tail tapers, so its widest points are the rear arches 0.6-1.0 m up
    /// the car, where a Charger or a Crown Vic is full width at the bumper.
    /// A chase rig at one fixed distance therefore shows the FD at 21% of the
    /// frame and the Crown Vic at 30%, and no single distance puts both at the
    /// W-share NFS uses for a car of their width.
    ///
    /// So each shell carries a handful of points that set its rear silhouette
    /// — per height band of the body, the vertex that is widest seen from
    /// behind, plus the tail corner — and the lowest point of the tail, which
    /// is what leaves the bottom of the frame first. The chase rig projects
    /// these through its own lens and solves the distance at which the car
    /// fills its share. Measured off the MESHES, which a WebGL build cannot
    /// read (the shells import with Read/Write off), hence a table.
    ///
    /// x is the NATIVE half-width (before CarBody's across-scale, which the
    /// rig multiplies back in), y is height above the ground, and z is metres
    /// forward of the rearmost bodywork. <see cref="Entry.tailZ"/> is that
    /// rearmost point in car-local metres. The table half lives in
    /// ChaseSilhouettes.Table.cs and is WRITTEN by the Camera Framing Probe
    /// (PSX_CAMFRAME_EMIT=1, tools\camframe-probe.ps1 -Emit); a shell with no
    /// row falls back to a box-shaped estimate off its collider.
    /// </summary>
    public static partial class ChaseSilhouettes
    {
        public readonly struct Entry
        {
            public readonly string key;
            public readonly float tailZ, lowY, lowZ;
            /// <summary>x (native half-width), y, z-forward-of-tail triples.</summary>
            public readonly float[] pts;

            public Entry(string key, float tailZ, float lowY, float lowZ, float[] pts)
            {
                this.key = key; this.tailZ = tailZ; this.lowY = lowY; this.lowZ = lowZ; this.pts = pts;
            }

            public int Count => pts != null ? pts.Length / 3 : 0;
            public Vector3 Point(int i) => new Vector3(pts[i * 3], pts[i * 3 + 1], pts[i * 3 + 2]);
        }

        public static int Rows => Table.Length;
        public static Entry Row(int i) => Table[i];

        /// <summary>The row for a shell, or false when the table has none.</summary>
        public static bool TryGet(string key, out Entry e)
        {
            if (!string.IsNullOrEmpty(key))
                foreach (var t in Table)
                    if (t.key == key) { e = t; return true; }
            e = default;
            return false;
        }
    }
}
