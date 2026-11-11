using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A PLANT AS A VOLUME (owner 2026-11-10, on the Linn Cove Viaduct in the
    /// PC26 build: "trees/foliage/etc should not protrude through bridge, walls,
    /// rails, or cars (especially in cockpit mode)"). What a MeshForest variant
    /// occupies, read off its own LOD0 vertices after its node transform (base
    /// at y = 0, trunk on the axis): its height and, in eight height bands, how
    /// far from the axis it reaches. The stage builder plants against the bands
    /// (PSXRacingBuilder.PlantClear); the audit (PlantSolidAudit) tests the
    /// vertices themselves.
    /// </summary>
    public sealed class PlantShape
    {
        public const int Bands = 8;
        /// <summary>Height at scale 1 (m), from the base.</summary>
        public float height;
        /// <summary>Lowest point under the base (roots; usually ~0).</summary>
        public float bottom;
        /// <summary>Per band (bottom .. height in eight), the farthest any vertex
        /// in it stands from the axis, in plan (m at scale 1).</summary>
        public float[] r = new float[Bands];
        public float rMax;

        /// <summary>A billboard tree's shape: a trunk below the foliage, the
        /// card's half width from <paramref name="foliageFrom"/> up.</summary>
        public static PlantShape Card(float h, float halfWidth, float foliageFrom)
        {
            var s = new PlantShape { height = Mathf.Max(0.1f, h) };
            for (int b = 0; b < Bands; b++)
            {
                float y1 = (b + 1) * s.height / Bands;
                s.r[b] = y1 > foliageFrom ? halfWidth : Mathf.Min(0.35f, halfWidth);
                s.rMax = Mathf.Max(s.rMax, s.r[b]);
            }
            return s;
        }

        /// <summary>The shape at ANY tilt (a log or a litter patch laid on its
        /// slope): every band as wide as the plant's whole reach, and as tall.</summary>
        public PlantShape Tumbled()
        {
            float reach = Mathf.Sqrt(rMax * rMax + height * height);
            var t = new PlantShape { height = reach, bottom = -reach, rMax = reach };
            for (int b = 0; b < Bands; b++) t.r[b] = reach;
            return t;
        }

        static readonly Dictionary<Mesh, PlantShape> cache = new Dictionary<Mesh, PlantShape>();
        static readonly Dictionary<Mesh, Vector3[]> pointCache = new Dictionary<Mesh, Vector3[]>();

        public static void ClearCache() { cache.Clear(); pointCache.Clear(); }

        /// <summary>Every vertex of LOD0 (all submeshes), through the variant's
        /// node transform.</summary>
        static Vector3[] AllPoints(MeshForest.Variant v)
        {
            var m = v.lods[0];
            if (m == null || !TreeKit.Read(m, 0, out var verts, out _)) return new Vector3[0];
            var pts = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) pts[i] = v.pre.MultiplyPoint3x4(verts[i]);
            return pts;
        }

        public static PlantShape Of(MeshForest.Variant v)
        {
            if (v == null || v.lods == null || v.lods[0] == null) return null;
            if (cache.TryGetValue(v.lods[0], out var s)) return s;
            var pts = AllPoints(v);
            s = new PlantShape();
            float top = 0f, bot = 0f;
            foreach (var p in pts) { top = Mathf.Max(top, p.y); bot = Mathf.Min(bot, p.y); }
            s.height = Mathf.Max(0.05f, top);
            s.bottom = bot;
            foreach (var p in pts)
            {
                int b = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, p.y) / s.height * Bands), 0, Bands - 1);
                float rr = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                if (rr > s.r[b]) s.r[b] = rr;
            }
            for (int b = 0; b < Bands; b++) s.rMax = Mathf.Max(s.rMax, s.r[b]);
            cache[v.lods[0]] = s;
            return s;
        }

        /// <summary>Up to <paramref name="max"/> of LOD0's vertices, spread
        /// evenly through the list, PLUS the extremes (the highest vertex and
        /// the farthest from the axis in each band) so a sparse sample never
        /// misses the tip that pokes through.</summary>
        public static Vector3[] Points(MeshForest.Variant v, int max)
        {
            if (v == null || v.lods == null || v.lods[0] == null) return new Vector3[0];
            if (pointCache.TryGetValue(v.lods[0], out var got)) return got;
            var all = AllPoints(v);
            var shape = Of(v);
            var outp = new List<Vector3>();
            int step = Mathf.Max(1, all.Length / Mathf.Max(1, max));
            for (int i = 0; i < all.Length; i += step) outp.Add(all[i]);
            if (all.Length > 0)
            {
                // per band and per eighth of a turn round the axis: the plant
                // is yawed at random, so the tip toward the road may be any of them
                int hi = 0;
                var far = new int[Bands * 8];
                var farR = new float[Bands * 8];
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].y > all[hi].y) hi = i;
                    int b = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, all[i].y) / shape.height * Bands), 0, Bands - 1);
                    int o = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(all[i].z, all[i].x) + Mathf.PI) / (2f * Mathf.PI) * 8f), 0, 7);
                    float rr = all[i].x * all[i].x + all[i].z * all[i].z;
                    if (rr > farR[b * 8 + o]) { farR[b * 8 + o] = rr; far[b * 8 + o] = i; }
                }
                outp.Add(all[hi]);
                for (int b = 0; b < Bands * 8; b++) if (farR[b] > 0f) outp.Add(all[far[b]]);
            }
            got = outp.ToArray();
            pointCache[v.lods[0]] = got;
            return got;
        }
    }
}
