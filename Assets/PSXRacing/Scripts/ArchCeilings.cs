using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE BODYWORK OVER EACH WHEEL, so a tyre can stop short of it.
    ///
    /// Owner, 2026-10-19: "I've noticed tires clipping up through fenders and
    /// hood ... This should never happen. A tire should bottom out when hitting
    /// the body, potentially reducing speed, increasing wear, and causing
    /// damages."
    ///
    /// The suspension draws each wheel where its ray finds the ground, and
    /// nothing stopped a hub rising: the spring force is capped at 5x the
    /// static load (so a kerb cannot launch the car), which means a landing or
    /// a kerb strike drove the wheel on up through the arch, and a lowered car
    /// started nearer the paint than that. The one fact the physics could not
    /// see is where the bodywork IS over each tyre.
    ///
    /// Editor/ArchProbe measures it once per shell, off the real body mesh,
    /// and writes Resources/ArchCeilings.bytes: per axle, a height field over
    /// the model's own wheel - 1 cm across, 5 mm along - each cell the LOWEST
    /// bodywork above the hub across its lane, at its row's exact station,
    /// both sides folded onto one. Exact along the car on purpose: a tyre's
    /// front and back stand 5-10 mm off the arch's near-vertical edges at hub
    /// height, and a field that blurred those edges by half a cell read a
    /// tyre sitting cleanly in its arch as already through it. It is kept in the shell's own frame (the frame
    /// <see cref="CarModelLibrary.ShellFit.P"/> maps from), so one field serves
    /// every car on the shell: the fit's stretch and lift carry a car's tyre
    /// into it, whatever size the sheet made that tyre and however far the fit
    /// cambered it in.
    ///
    /// <see cref="HubMax"/> is the number the car wants: how high its hub may
    /// rise (car frame) before any point of the DRAWN tyre meets the
    /// bodywork. CarBody solves it per wheel when it fits a shell, and
    /// CarController turns it into a bump stop.
    /// </summary>
    public static class ArchCeilings
    {
        /// <summary>Lane pitch across the car and row pitch along it (m).</summary>
        public const float LaneStep = 0.01f, RowStep = 0.005f;
        /// <summary>How far the field reaches either side of the model's hub,
        /// across and along (m). Wide enough for a sheet tyre on a sheet track
        /// (they land up to ~8 cm off the model's own, half a 335 section
        /// beyond that) and for the biggest tyre on the sheet (r 0.38).</summary>
        public const float HalfX = 0.25f, HalfZ = 0.42f;
        public const int Lanes = 51, Rows = 169;
        /// <summary>Height step of a cell byte, and the height byte 0 stands
        /// for, both relative to the model's hub (m). 254 steps reach 1.17 m
        /// over the hub, past any wing.</summary>
        public const float Quantum = 0.005f, Floor = -0.10f;
        /// <summary>Nothing above this cell: open sky, or beside the body.</summary>
        public const byte Open = 255;
        const uint Magic = 0x48435241; // "ARCH"
        public const int Version = 2;
        public const string ResourceName = "ArchCeilings";

        /// <summary>Tyre sampling: lanes across the tread and the arc of its
        /// top half. +/-84 deg is everything a rising tyre can lead with; the
        /// last few degrees either side are its front and back, which move
        /// into the arch's own opening and never meet a ceiling.</summary>
        const int TreadLanes = 7;
        const float ArcHalfDeg = 84f, ArcStepDeg = 3f;

        public sealed class Shell
        {
            public string key;
            /// <summary>The model's own hub height, shell frame (m).</summary>
            public float hubY;
            /// <summary>Per axle (0 front, 1 rear): the model's hub |x| and z,
            /// shell frame (m).</summary>
            public readonly float[] hubX = new float[2], hubZ = new float[2];
            /// <summary>Per axle: <see cref="Lanes"/> x <see cref="Rows"/>
            /// height bytes, lane-major (cell = lane * Rows + row).</summary>
            public readonly byte[][] cells = new byte[2][];
        }

        static Dictionary<string, Shell> table;

        /// <summary>This shell's field, or null when it was never measured.</summary>
        public static Shell Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (table == null) Load();
            return table.TryGetValue(key, out var s) ? s : null;
        }

        /// <summary>Drop the loaded table (the probe rewrites the file).</summary>
        public static void ClearCache() => table = null;

        static void Load()
        {
            table = new Dictionary<string, Shell>();
            var ta = Resources.Load<TextAsset>(ResourceName);
            if (ta == null)
            {
                Debug.LogWarning("[ArchCeilings] Resources/" + ResourceName +
                                 ".bytes missing - no bump stops (run Editor/ArchProbe)");
                return;
            }
            try { Read(ta.bytes, table); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ArchCeilings] unreadable table: " + e.Message);
                table.Clear();
            }
        }

        public static byte Encode(float overHub) =>
            // DOWN, never to nearest: a ceiling may read lower than it is, not higher.
            (byte)Mathf.Clamp(Mathf.FloorToInt((overHub - Floor) / Quantum), 0, Open - 1);

        public static float Decode(byte b) =>
            b == Open ? float.PositiveInfinity : Floor + b * Quantum;

        public static void Read(byte[] bytes, Dictionary<string, Shell> into)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (r.ReadUInt32() != Magic) throw new IOException("not an arch table");
                int version = r.ReadInt32();
                if (version != Version) throw new IOException("version " + version);
                int lanes = r.ReadInt32(), rows = r.ReadInt32(), count = r.ReadInt32();
                if (lanes != Lanes || rows != Rows) throw new IOException("grid " + lanes + "x" + rows);
                for (int n = 0; n < count; n++)
                {
                    var s = new Shell { key = r.ReadString(), hubY = r.ReadSingle() };
                    for (int a = 0; a < 2; a++)
                    {
                        s.hubX[a] = r.ReadSingle();
                        s.hubZ[a] = r.ReadSingle();
                        s.cells[a] = r.ReadBytes(Lanes * Rows);
                    }
                    into[s.key] = s;
                }
            }
        }

        public static byte[] Write(IList<Shell> shells)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Magic); w.Write(Version); w.Write(Lanes); w.Write(Rows); w.Write(shells.Count);
                foreach (var s in shells)
                {
                    w.Write(s.key); w.Write(s.hubY);
                    for (int a = 0; a < 2; a++)
                    {
                        w.Write(s.hubX[a]); w.Write(s.hubZ[a]);
                        w.Write(s.cells[a]);
                    }
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Lane and row of the cell holding a shell-frame point (x
        /// folded to |x|), false when the point is off the field.</summary>
        public static bool CellOf(Shell s, int axle, float x, float z, out int lane, out int row)
        {
            lane = Mathf.RoundToInt((Mathf.Abs(x) - (s.hubX[axle] - HalfX)) / LaneStep);
            row = Mathf.RoundToInt((z - (s.hubZ[axle] - HalfZ)) / RowStep);
            return lane >= 0 && lane < Lanes && row >= 0 && row < Rows;
        }

        /// <summary>The lowest bodywork over a shell-frame point, shell-frame
        /// height (m); +inf where there is none or the point is off the field.
        /// A cell holds the lowest bodywork across its whole lane, so across
        /// the car the answer can only err low.</summary>
        public static float Ceiling(Shell s, int axle, float x, float z)
        {
            if (!CellOf(s, axle, x, z, out int lane, out int row)) return float.PositiveInfinity;
            return s.hubY + Decode(s.cells[axle][lane * Rows + row]);
        }

        /// <summary>
        /// How high wheel <paramref name="i"/>'s hub may rise, car frame (m),
        /// before the tyre as DRAWN - the fit's size, camber and hub, steered
        /// <paramref name="steerDeg"/> - touches the bodywork over it. +inf
        /// when the shell was never measured.
        ///
        /// The field is a height field and the tyre only rises, so this is a
        /// pure difference: the least gap between a point on the tyre's top
        /// half and the ceiling over that point, added to the hub height the
        /// samples were taken at.
        /// </summary>
        public static float HubMax(CarModelDef def, CarModelLibrary.ShellFit f, int i, float steerDeg = 0f)
        {
            if (def == null) return float.PositiveInfinity;
            var s = Get(def.key);
            if (s == null) return float.PositiveInfinity;
            int axle = i < 2 ? 0 : 1;

            Bounds wb = def.wheelMesh != null ? def.wheelMesh.bounds
                : new Bounds(Vector3.zero, new Vector3(0.2f, 0.666f, 0.666f));
            Vector3 scale = f.WheelScale(i);
            Vector3 c = Vector3.Scale(wb.center, scale);
            float hw = wb.size.x * scale.x * 0.5f;
            float r = f.Radius(i);
            f.Holder(i, out Vector3 holderPos, out Quaternion holderRot);
            Vector3 hub = f.Hub(i);
            Quaternion steer = Quaternion.Euler(0f, steerDeg, 0f);
            float sx = Mathf.Max(f.sx, 1e-3f), sz = Mathf.Max(f.sz, 1e-3f);

            float least = float.PositiveInfinity;
            for (int lane = 0; lane < TreadLanes; lane++)
            {
                float x = c.x + hw * (2f * lane / (TreadLanes - 1) - 1f);
                for (float deg = -ArcHalfDeg; deg <= ArcHalfDeg + 1e-3f; deg += ArcStepDeg)
                {
                    float a = deg * Mathf.Deg2Rad;
                    var p = new Vector3(x, c.y + r * Mathf.Cos(a), c.z + r * Mathf.Sin(a));
                    Vector3 car = hub + steer * (holderPos + holderRot * p);
                    float ceil = Ceiling(s, axle, car.x / sx, car.z / sz);
                    if (float.IsPositiveInfinity(ceil)) continue;
                    // The ceiling moves with the body's lift; the tyre point is already in the car frame.
                    float gap = ceil + f.dy - car.y;
                    if (gap < least) least = gap;
                }
            }
            return float.IsPositiveInfinity(least) ? least : hub.y + least;
        }
    }
}
