using System.IO;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// How much of the ground is under tree canopy, everywhere in Charlotte
    /// (plan WP-08): the USDA Forest Service's NLCD Tree Canopy Cover for 2024
    /// (public domain), averaged onto the DEM's own 60 m lattice by
    /// tools/city/canopy.mjs into Resources/charlotte_canopy.bytes. The owner
    /// chose PRESENT DAY for everything but the cars, so it is the latest full
    /// year, not a 1999 band.
    ///
    /// Its own file rather than a section of charlotte_city.bytes: the road
    /// export is rewritten by the lines release, and this is not derived from
    /// the roads. PCAN v1: u32 'PCAN' | i32 1 | u32 nx, nz | f32 x0, z0, cell
    /// | u16 year | u16 steps | nx*nz u8 (row z0 first); a value v is canopy
    /// v / steps. A cell is centred on its lattice node, as a DEM cell is.
    /// </summary>
    public static class CityCanopy
    {
        const uint Magic = 0x4E414350;   // "PCAN"
        static bool tried;
        static int nx, nz, steps = 100;
        static float x0, z0, cell = 60f;
        static byte[] grid;

        /// <summary>The survey year the grid holds (0 without one).</summary>
        public static int Year { get; private set; }
        public static bool Loaded { get { Ensure(); return grid != null; } }
        public static float CellM { get { Ensure(); return cell; } }

        static void Ensure()
        {
            if (tried) return;
            tried = true;
            var ta = Resources.Load<TextAsset>("charlotte_canopy");
            if (ta == null) { Debug.LogWarning("[City] charlotte_canopy.bytes missing - no canopy trees (tools/city/canopy.mjs --out Assets/PSXRacing/Resources)"); return; }
            byte[] bytes = ta.bytes;
            Resources.UnloadAsset(ta);
            Load(bytes);
        }

        /// <summary>Install a grid from PCAN bytes (Resources today, a
        /// separately downloaded Charlotte file later). False on a bad file.</summary>
        public static bool Load(byte[] bytes)
        {
            tried = true;
            if (bytes == null || bytes.Length < 32) { Debug.LogError("[City] charlotte_canopy.bytes: too short"); return false; }
            int w, h, st, yr;
            float ox, oz, c;
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (r.ReadUInt32() != Magic) { Debug.LogError("[City] charlotte_canopy.bytes: bad magic"); return false; }
                if (r.ReadInt32() != 1) { Debug.LogError("[City] charlotte_canopy.bytes: not version 1"); return false; }
                w = r.ReadInt32(); h = r.ReadInt32();
                ox = r.ReadSingle(); oz = r.ReadSingle(); c = r.ReadSingle();
                yr = r.ReadUInt16(); st = r.ReadUInt16();
            }
            if (w < 2 || h < 2 || !(c > 0f) || st <= 0 || 32 + (long)w * h != bytes.Length)
            {
                Debug.LogError($"[City] charlotte_canopy.bytes: header says {w} x {h} but holds {bytes.Length - 32} bytes");
                return false;
            }
            var g = new byte[w * h];
            System.Buffer.BlockCopy(bytes, 32, g, 0, g.Length);
            nx = w; nz = h; x0 = ox; z0 = oz; cell = c; steps = st; Year = yr;
            grid = g;
            return true;
        }

        /// <summary>The canopy fraction (0..1) of the 60 m cell a world point
        /// is in: the nearest lattice node's value, so every point of one cell
        /// reads the same number (what the tree pass plants against). 0 off
        /// the grid or without it.</summary>
        public static float CellFraction(float x, float z)
        {
            Ensure();
            if (grid == null) return 0f;
            x /= CityMap.LayoutScale; z /= CityMap.LayoutScale;
            int ix = Mathf.RoundToInt((x - x0) / cell), iz = Mathf.RoundToInt((z - z0) / cell);
            if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) return 0f;
            return grid[iz * nx + ix] / (float)steps;
        }

        /// <summary>The canopy fraction at a point, bilinear between the
        /// lattice nodes (for the audit's truth along a road).</summary>
        public static float Fraction(float x, float z)
        {
            Ensure();
            if (grid == null) return 0f;
            x /= CityMap.LayoutScale; z /= CityMap.LayoutScale;
            float fx = (x - x0) / cell, fz = (z - z0) / cell;
            int ix = Mathf.Clamp(Mathf.FloorToInt(fx), 0, nx - 2), iz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, nz - 2);
            float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
            float a = grid[iz * nx + ix], b = grid[iz * nx + ix + 1], c = grid[(iz + 1) * nx + ix], d = grid[(iz + 1) * nx + ix + 1];
            return ((a * (1f - tx) + b * tx) * (1f - tz) + (c * (1f - tx) + d * tx) * tz) / steps;
        }
    }
}
