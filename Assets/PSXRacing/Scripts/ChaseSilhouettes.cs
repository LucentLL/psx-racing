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
    /// So each shell carries the points that set its rear silhouette — per
    /// 10 cm of height and per SIDE, every vertex that is the widest seen from
    /// behind for some chase lens, plus the tail corners (a phone's dials are
    /// circles, so the lane between them needs the OUTLINE, not six widest
    /// points) — and two short lists off its
    /// side profile: the ROOF LINE (the upper-hull vertices that are the
    /// highest thing in the picture for some chase lens: the roof's rear edge,
    /// a windscreen header, a Daytona's wing) and the TAIL'S LOWEST EDGE (the
    /// lower-hull vertices of the last 1.5 m that are the lowest thing in the
    /// picture for some lens). The chase rig projects these through its own
    /// lens to solve the distance at which the car fills its share, how high
    /// the lens must stand for the road to show over the roof, and how far it
    /// may pitch before the tail leaves the frame. Measured off the MESHES,
    /// which a WebGL build cannot read (the shells import with Read/Write off),
    /// hence a table.
    ///
    /// Silhouette x is the NATIVE x (before CarBody's across-scale, which the
    /// rig multiplies back in), SIGNED: the Viper GTS body is 5 cm wider on
    /// its left than its right at the rear arches, and a mirrored |x| read it
    /// 3% wider than it is (2026-09-28 review). y is height above the ground,
    /// z is metres forward of the rearmost bodywork. <see cref="Entry.tailZ"/>
    /// is that rearmost point in car-local metres. The table half lives in
    /// ChaseSilhouettes.Table.cs and is WRITTEN by the Camera Framing Probe
    /// (PSX_CAMFRAME_EMIT=1, tools\camframe-probe.ps1 -Emit); a shell with no
    /// row falls back to a box-shaped estimate off its collider.
    /// </summary>
    public static partial class ChaseSilhouettes
    {
        public readonly struct Entry
        {
            public readonly string key;
            public readonly float tailZ;
            /// <summary>x (native, signed), y, z-forward-of-tail triples.</summary>
            public readonly float[] pts;
            /// <summary>The roof line: y, z-forward-of-tail pairs.</summary>
            public readonly float[] roof;
            /// <summary>The tail's lowest edge: y, z-forward-of-tail pairs.</summary>
            public readonly float[] low;

            public Entry(string key, float tailZ, float[] pts, float[] roof, float[] low)
            {
                this.key = key; this.tailZ = tailZ; this.pts = pts; this.roof = roof; this.low = low;
            }

            public int Count => pts != null ? pts.Length / 3 : 0;
            public Vector3 Point(int i) => new Vector3(pts[i * 3], pts[i * 3 + 1], pts[i * 3 + 2]);
            public int RoofCount => roof != null ? roof.Length / 2 : 0;
            /// <summary>(y, z-forward-of-tail).</summary>
            public Vector2 Roof(int i) => new Vector2(roof[i * 2], roof[i * 2 + 1]);
            public int LowCount => low != null ? low.Length / 2 : 0;
            /// <summary>(y, z-forward-of-tail).</summary>
            public Vector2 Low(int i) => new Vector2(low[i * 2], low[i * 2 + 1]);
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
