using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityElevation
    {
        /// <summary>
        /// THE DEM FOR A WORKER THREAD (city perf round 1, 2026-10-30).
        /// <see cref="BaseY"/> decodes the DEM's compressed blocks into ONE
        /// shared rotating cache, which two threads would tear. A reader owns
        /// its own cache over the same immutable blob, so any number of them
        /// (one a thread) sample exactly what BaseY samples - the same blocks,
        /// the same decode, the same bilinear - and never touch BaseY's. Build
        /// one after the DEM has loaded (the main thread's first BaseY did
        /// that); <see cref="Ready"/> says whether it may be used.
        /// </summary>
        public sealed class DemReader
        {
            const int Slots = 32;
            readonly int[] blockSlot;
            readonly ushort[][] slotData = new ushort[Slots][];
            readonly int[] slotBlock = new int[Slots];
            int hand;
            public readonly bool Ready;

            public DemReader()
            {
                Ready = DemLoaded;
                if (demBlob != null)
                {
                    blockSlot = new int[demNBX * demNBZ];
                    for (int k = 0; k < blockSlot.Length; k++) blockSlot[k] = -1;
                    for (int k = 0; k < Slots; k++) slotBlock[k] = -1;
                }
            }

            ushort[] Block(int bx, int bz, out int w)
            {
                w = Mathf.Min(demB, demNX - 1 - bx * demB) + 1;
                int id = bz * demNBX + bx;
                int slot = blockSlot[id];
                if (slot >= 0) return slotData[slot];
                slot = hand;
                hand = (hand + 1) % Slots;
                if (slotBlock[slot] >= 0) blockSlot[slotBlock[slot]] = -1;
                var data = slotData[slot] ??= new ushort[(demB + 1) * (demB + 1)];
                int h = Mathf.Min(demB, demNZ - 1 - bz * demB) + 1;
                int p = demPay + demOff[id];
                var src = demBlob;
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        uint z = 0; int sh = 0; byte c;
                        do { c = src[p++]; z |= (uint)(c & 0x7F) << sh; sh += 7; } while ((c & 0x80) != 0);
                        int res = (z & 1) != 0 ? -(int)((z + 1) >> 1) : (int)(z >> 1);
                        int k = j * w + i;
                        int pred = i == 0 && j == 0 ? 0
                                 : j == 0 ? data[k - 1]
                                 : i == 0 ? data[k - w]
                                 : data[k - 1] + data[k - w] - data[k - w - 1];
                        data[k] = (ushort)(pred + res);
                    }
                slotBlock[slot] = id;
                blockSlot[id] = slot;
                return data;
            }

            /// <summary><see cref="BaseY"/>, on this reader's own cache.</summary>
            public float BaseY(float x, float z)
            {
                if (!Ready) return NoiseY(x, z);
                x /= CityMap.LayoutScale; z /= CityMap.LayoutScale;
                float fx = (x - demX0) / demCell, fz = (z - demZ0) / demCell;
                int ix = Mathf.Clamp(Mathf.FloorToInt(fx), 0, demNX - 2);
                int iz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, demNZ - 2);
                float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
                float a, b, c, d;
                if (dem != null)
                {
                    a = dem[iz * demNX + ix]; b = dem[iz * demNX + ix + 1];
                    c = dem[(iz + 1) * demNX + ix]; d = dem[(iz + 1) * demNX + ix + 1];
                }
                else
                {
                    int bx = Mathf.Min(ix / demB, demNBX - 1), bz = Mathf.Min(iz / demB, demNBZ - 1);
                    var blk = Block(bx, bz, out int w);
                    int k = (iz - bz * demB) * w + ix - bx * demB;
                    a = blk[k]; b = blk[k + 1]; c = blk[k + w]; d = blk[k + w + 1];
                }
                return ((a * (1f - tx) + b * tx) * (1f - tz) + (c * (1f - tx) + d * tx) * tz) * demScale;
            }
        }
    }
}
