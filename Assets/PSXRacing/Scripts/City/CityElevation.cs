using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Solves road heights and answers ground heights for the city.
    ///
    /// The circuits' rule carries over whole: the land is graded TO the road,
    /// never the other way round. Every edge starts life following the real
    /// ground (the 60 m road grid, see <see cref="RoadBaseY"/>), smoothed and
    /// grade-limited (the USGS 3DEP bare earth averaged over each 60 m cell,
    /// rebuilt from the 30 m land grid since WP-13); then OpenStreetMap's FACTS
    /// turn into structure:
    ///
    ///   bridge=yes on an edge     -> the whole edge is a deck: it holds a
    ///                                straight line between its ends and never
    ///                                pins the land.
    ///   crossing (no shared node) -> the OVER edge takes a hump that clears
    ///                                the under road by <see cref="ClearanceM"/>,
    ///                                approach-graded, merging into a viaduct
    ///                                where humps overlap (I-77 through uptown).
    ///   ...unless the under road is a FREEWAY MAINLINE and the over road a
    ///   surface street, in which case the freeway is dug into a TRENCH under
    ///   it instead. Charlotte's inner freeways (the Belk, Brookshire) run in
    ///   cuts under streets that stay at grade; raising every cross street
    ///   onto an embankment would hump the whole uptown grid.
    ///   water span                -> the road HOLDS its line across the span
    ///                                while the ground is carved to the creek's
    ///                                bed (sampled from 3DEP, WP-04b).
    ///
    /// Stations marked elevated get a deck and piers and NO ground pin; the
    /// ground query pins only to grounded stations, with the circuits' shelf /
    /// sink / blend shape — looked up through the tile-local spatial hash
    /// instead of a whole-track Gaussian, because an O(track) walk is a
    /// non-starter against 25,000 edges.
    /// </summary>
    public static class CityElevation
    {
        public const float StationStep = 10f;
        public const float ClearanceM = 5.0f;   // under-side of deck over road below
        public const float DeckThick = 0.55f;
        /// <summary>How far below the tarmac the coarse 8 m lattice sits
        /// inside a road corridor. NOT a kerb any more: it was 0.20 m and the
        /// road's side was a 0.32 m vertical collider face, which made a
        /// 20 cm wall the length of every street in Charlotte (the owner:
        /// "roads sitting cm above the ground do not need rails/walls, they
        /// should meet the ground properly by DOT standards"). The lattice is
        /// now only HIDDEN under the exact verge surface CityMeshes lays
        /// beside every grounded edge (RoadsideRules.CityHideMarginM), and it
        /// is the verge, flush with the tarmac, that the car meets. Half the
        /// old sink is safe only because the allowance under it is now
        /// measured where the lattice actually overshoots: at a SAG
        /// (<see cref="MeasureSags"/>).</summary>
        public const float CorridorSink = RoadsideRules.CityHideMarginM;
        public const float CorridorBlend = 26f;
        /// <summary>Air between a deck's soffit and the land under it. A
        /// bridge is OVER something, and the 60 m DEM sees neither the creek
        /// nor the railway cut it was built to cross: 800 decks tagged
        /// bridge=yes stood on land the DEM called level, and the grass grew
        /// up through the concrete. The land under structure is capped this
        /// far below the soffit, and never raised.</summary>
        public const float UnderDeckAir = 1.2f;
        /// <summary>How far past a grounded corridor its "never above the
        /// tarmac" cap still holds: the diagonal of an 8 m lattice cell, so
        /// no vertex that can touch the pavement's cells sits above it.</summary>
        public const float CapReach = 5f;
        /// <summary>How far outside a DECK's own half width the land is dug
        /// to <see cref="UnderDeckAir"/> under its soffit. It was the whole
        /// corridor plus half the blend (hw + 19.5 m), measured from CLAMPED
        /// segment projections, so every elevated polyline vertex was a disc
        /// that dug 1.75 m pits 15-25 m along the grounded approach beyond
        /// the rails (1,427 approach ends city-wide). The dig is the deck's
        /// footprint now, from true perpendicular feet (and the outer wedge
        /// of a bend INSIDE the structure, which no true foot reaches); past it a deck's
        /// pavement is protected like any other (never under land, the same
        /// <see cref="CapReach"/> band), which is all the at-grade decks
        /// needed.</summary>
        public const float CapPadM = 2f;
        // (FloorFlatM, the 11.5 m every grounded road held its land flat for
        // before it fell at 1V:3H, went with WP-14: a road's FLOOR is now its
        // section's fill - flat across RoadsideRules.CityBenchM, then
        // RoadsideRules.CityFillSlope - see Ground.)
        /// <summary>The longest run, along a road, between two corners of
        /// one 8 m lattice triangle: the cell diagonal. The sag allowance is
        /// measured over chords this long (it bounds the 8 m chords too).</summary>
        public const float SagChordM = 8f * 1.41421356f;
        /// <summary>Above the base ground by this much = on structure, as a
        /// LAST RESORT. It was 1.4 m and it decided most of the decks in the
        /// city: the SRTM grid was min-filtered (6.6 m low on average, 20 m
        /// low beside every valley), so a road on any hillside stood proud
        /// of the "ground" and became a floating slab with rails. Decks now
        /// come from the facts (<see cref="MarkStructure"/>); a road merely
        /// above the terrain is an EMBANKMENT and the ground is graded up to
        /// it. This margin only catches an untagged viaduct.</summary>
        public const float ElevMarginM = 3.5f;
        /// <summary>Stations the <see cref="ElevMarginM"/> last resort made
        /// structure in the last solve (the audit's TerrainFidelity reports
        /// it: real hills put more roads above the 60 m grid).</summary>
        public static int MarginStructureStations { get; private set; }
        /// <summary>Of those, on a twin deck's edge or one meeting it at a
        /// node (plan B1: what the twin holds can have lifted).</summary>
        public static int MarginStationsNearTwins { get; private set; }

        // ------------------------------------------------------------------
        //  Water (WP-04b). The exporter samples each creek's BED from USGS
        //  3DEP 1/3" every 20 m along the county's (or 3DHP's) surveyed line
        //  and makes it fall downstream (section WBED). The ground is carved
        //  to it: a flat floor under the water, then banks at 1V:2H until
        //  they meet the land. It replaced a fixed 3.6 m carve along RG2's
        //  traced lines, which sat 3-8 m high in valleys the old grid had
        //  filled, and would have dug a second trench into the valleys the
        //  real ground now shows. The numbers below are the whole shape.
        // ------------------------------------------------------------------
        /// <summary>The water surface stands this far under the sampled bed
        /// (the 10 m pixels straddle the channel, so the bed reads high).</summary>
        public const float WaterBelowBed = 0.1f;
        /// <summary>A creek's carved floor, under its water surface: deep
        /// enough that the 8 m lattice, which only meets the floor at its
        /// vertices, still shows water along the whole line.</summary>
        public const float CarveBelowWater = 0.8f;
        /// <summary>A creek's flat floor reaches this far each side of its
        /// line: the lattice's half diagonal at least, so the vertex nearest
        /// any point of the line is on the floor.</summary>
        public const float CreekFlatMin = 6f, CreekFlatPad = 3f;
        /// <summary>The bank, from the floor's edge up to the land.</summary>
        public const float BankSlope = 0.5f;
        /// <summary>A ravine (a small stream: no water, no span) is carved
        /// this far under its bed, with a floor this wide each side.</summary>
        public const float RavineBelowBed = 0.4f, RavineFlat = 3f;
        /// <summary>No carve past this distance from a line (the query box
        /// of <see cref="Ground"/>).</summary>
        public const float CarveReachM = 36f;
        /// <summary>A lake's surface under its level in the hydro-flattened
        /// 3DEP (WBED's one value for a lake).</summary>
        public const float LakeBelowLevel = 0.3f;

        /// <summary>A water span's soffit stands at least this far over the
        /// water under it (WP-25's figure). The span is lifted to it, with
        /// 4.5% approaches, like a crossing's hump: the water is at its real
        /// level since WP-04b, and a road the 60 m grid averages down into a
        /// valley (I-485 over Reedy Creek's tributary, on a fill the grid
        /// cannot see) would otherwise have the creek standing over its deck.</summary>
        public const float WaterDeckClearM = 1.0f;
        /// <summary>The water surface under each water span (NaN where no
        /// water is found), measured once per solve.</summary>
        static float[] spanWaterY;
        static bool[] spanLifted;
        public static float SpanWaterY(int span) => spanWaterY != null && span < spanWaterY.Length ? spanWaterY[span] : float.NaN;
        /// <summary>Water spans the solve lifted to clear their water, and
        /// the most any was lifted, for the audit.</summary>
        public static int SpansLiftedForWater { get; private set; }
        public static float SpanWaterLiftMax { get; private set; }

        public static float CreekFlatHalf(CityMap.Water w) => Mathf.Max(CreekFlatMin, w.width * 0.5f + CreekFlatPad);

        /// <summary>The bed, metres above the datum, at arc length
        /// <paramref name="s"/> along the water's line; NaN without WBED.
        /// Samples stand every bedStep metres from the first point, and the
        /// last one at the line's end.</summary>
        public static float BedYAt(CityMap.Water w, float s)
        {
            var b = w.bedY;
            if (b == null || b.Length == 0) return float.NaN;
            if (b.Length == 1 || !(w.bedStep > 0f)) return b[0];
            int last = b.Length - 1;
            float sLast = (last - 1) * w.bedStep;
            if (s >= sLast)
                return Mathf.Lerp(b[last - 1], b[last], Mathf.Clamp01((s - sLast) / Mathf.Max(1e-3f, w.length - sLast)));
            float f = Mathf.Max(0f, s) / w.bedStep;
            int k = Mathf.Min(last - 1, (int)f);
            return Mathf.Lerp(b[k], b[k + 1], f - k);
        }

        /// <summary>A creek's water surface at arc length s (above the datum),
        /// or the old fixed rule where the data has no bed.</summary>
        public static float CreekSurfaceY(CityMap.Water w, float s, Vector2 p)
        {
            float bed = BedYAt(w, s);
            return float.IsNaN(bed) ? RiverSurfaceY(p.x, p.y) : bed - WaterBelowBed;
        }

        /// <summary>Distance from p to segment si of a water line, and the
        /// arc length of the foot.</summary>
        public static float WaterFoot(CityMap.Water w, int si, Vector2 p, out float s)
        {
            if (si + 1 >= w.pts.Length) si = Mathf.Max(0, w.pts.Length - 2);
            Vector2 a = w.pts[si], d = w.pts[si + 1] - a;
            float L2 = d.sqrMagnitude;
            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
            s = w.s[si] + Mathf.Sqrt(L2) * t;
            return Vector2.Distance(p, a + d * t);
        }

        const float ApproachGrade = 0.045f;

        /// <summary>
        /// How far an approach has fallen below its apex at distance d, on a
        /// road whose crests need radius r (<see cref="CrestRadius"/>). It was
        /// d x ApproachGrade, a TENT: a crossing's hump, a raised node's cone
        /// and a water span's plateau met their 4.5% approaches at an apex, a
        /// 9% crest a car at speed leaves the road over (the launch audit's
        /// commonest cause). Now the apex is a vertical curve: a parabola from
        /// level to the approach grade over ApproachGrade x r, then the same
        /// 4.5%. Never lower than the tent (d^2/2r &lt;= d g for d &lt;= g r, and
        /// the line beyond runs g^2 r/2 above it), so every clearance the tent
        /// held still holds.
        /// </summary>
        static float ApproachDrop(float d, float r)
        {
            if (!VerticalCurvesOn) return Mathf.Max(0f, d) * ApproachGrade;   // the tent, as it was (PSX_CITY_VCURVES=0)
            if (d <= 0f) return 0f;
            float d1 = ApproachGrade * r;
            return d <= d1 ? d * d / (2f * r) : ApproachGrade * d - 0.5f * ApproachGrade * d1;
        }

        /// <summary>The race routes' edges (their crests are judged at race
        /// speed), for the solve that is running.</summary>
        static bool[] routeEdge;
        static float CrestR(CityMap.Edge e) => CrestRadius(e, routeEdge != null && e.index < routeEdge.Length && routeEdge[e.index]);
        /// <summary>A crossing closer than this to the end of the freeway
        /// edge is AT the junction, not near it; the hump rule keeps it.</summary>
        const float TrenchEndM = 20f;

        static float MaxGrade(CityMap.Edge e) =>
            e.cls >= 5 && !e.link ? 0.04f : e.link ? 0.08f : e.cls == 4 ? 0.05f : e.cls == 0 ? 0.08f : 0.065f;

        // ------------------------------------------------------------------
        //  Base terrain: the real one. A 30 m grid baked by the exporter from
        //  USGS 3DEP 1/3" bare earth, each node the mean of its 30 m cell
        //  (WP-13; 60 m from WP-04, and before that the AWS skadi tiles,
        //  opened, closed and blurred flat), bilinear per query. Heights are
        //  metres above the DATUM, pinned at 97.0 m ASL (WP-02; it used to be
        //  the grid's lowest point less two metres, and would have moved with
        //  the ground), so world y = 0 is 97 m ASL.
        //
        //  PDEM v3 (WP-13): u32 'PDEM' | i32 3 | u32 nx, nz | f32 x0, z0,
        //  cell, base, scale | u32 B, nbx, nbz | u32 off[nbx*nbz + 1] |
        //  payload. The grid is cut into blocks of B x B cells (B + 1 nodes a
        //  side, one node shared with the next block, so a cell never
        //  straddles two), each node a zigzag varint residual against a
        //  planar prediction from the nodes before it (tools/city/lib/
        //  pdem3.mjs writes it). The compressed blob is all that stays
        //  resident: a block is decoded when a query first lands in it, into
        //  a cache of BlockCacheSlots blocks (a tile build touches one to
        //  four), so the whole u16 grid (5.9 MB at 30 m) is never held.
        //
        //  PDEM v2 / v1 (a plain nx*nz u16 grid after the header; v1 without
        //  the scale, 0.1 hard-coded) are still read: the grid is then one
        //  resident array, as it was.
        // ------------------------------------------------------------------
        const uint MagicDem = 0x4D454450;   // "PDEM"
        static bool demTried;
        static int demNX, demNZ, demVersion;
        static float demX0, demZ0, demCell, demBase, demScale = 0.1f;
        /// <summary>v1/v2: the whole grid.</summary>
        static ushort[] dem;
        /// <summary>v3: the blocks' bytes, where their payload starts, and
        /// each block's offset into it.</summary>
        static byte[] demBlob;
        static int demPay, demB, demNBX, demNBZ;
        static int[] demOff;
        /// <summary>v3: how many decoded blocks are held. 128 blocks of
        /// 33 x 33 nodes are 279 KB; the road grid's one pass over the city
        /// (BuildRoadDem) walks a row of 51 blocks at a time.</summary>
        public const int BlockCacheSlots = 128;
        static ushort[][] slotData;
        static int[] slotBlock, blockSlot;
        static int slotHand;
        /// <summary>Blocks decoded since the grid loaded (the budget probe's
        /// cache census).</summary>
        public static long BlockDecodes { get; private set; }

        static bool DemLoaded => dem != null || demBlob != null;

        static void EnsureDem()
        {
            if (demTried) return;
            demTried = true;
            var ta = Resources.Load<TextAsset>("charlotte_dem");
            if (ta == null) { Debug.LogWarning("[City] charlotte_dem.bytes missing — using value-noise terrain"); return; }
            // Take the bytes and let the asset go BEFORE the grid is built,
            // so the TextAsset's copy and the grid are never both held.
            byte[] bytes = ta.bytes;
            Resources.UnloadAsset(ta);
            LoadDem(bytes);
        }

        /// <summary>
        /// Install a height grid from PDEM bytes (v1, v2 or v3), wherever they
        /// came from: Resources today (<see cref="EnsureDem"/>), a separately
        /// downloaded Charlotte data file later (plan WP-29, critic C46). v3
        /// keeps the bytes themselves (the blocks decode from them on demand);
        /// v1/v2's u16 payload is block-copied, not read one value at a time.
        /// Returns false (and keeps the previous grid) on a bad header.
        /// </summary>
        public static bool LoadDem(byte[] bytes)
        {
            demTried = true;
            if (bytes == null || bytes.Length < 32) { Debug.LogError("[City] charlotte_dem.bytes: too short"); return false; }
            int version, nx, nz, headLen;
            float x0, z0, cell, bas, scale;
            int B = 0, nbx = 0, nbz = 0;
            int[] off = null;
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (r.ReadUInt32() != MagicDem) { Debug.LogError("[City] charlotte_dem.bytes: bad magic"); return false; }
                version = r.ReadInt32();
                if (version < 1 || version > 3) { Debug.LogError("[City] charlotte_dem.bytes: version " + version + " (reader knows 1, 2 and 3)"); return false; }
                nx = r.ReadInt32(); nz = r.ReadInt32();
                x0 = r.ReadSingle(); z0 = r.ReadSingle(); cell = r.ReadSingle(); bas = r.ReadSingle();
                scale = version >= 2 ? r.ReadSingle() : 0.1f;
                if (version >= 3)
                {
                    B = r.ReadInt32(); nbx = r.ReadInt32(); nbz = r.ReadInt32();
                    if (B < 1 || nbx != (nx - 2) / Mathf.Max(1, B) + 1 || nbz != (nz - 2) / Mathf.Max(1, B) + 1 || (long)12 * 4 + 4L * (nbx * nbz + 1) > bytes.Length)
                    { Debug.LogError($"[City] charlotte_dem.bytes: v3 block table {nbx} x {nbz} of {B} does not fit a {nx} x {nz} grid"); return false; }
                    off = new int[nbx * nbz + 1];
                    for (int k = 0; k < off.Length; k++) off[k] = (int)r.ReadUInt32();
                }
                headLen = (int)r.BaseStream.Position;
            }
            if (nx < 2 || nz < 2 || !(cell > 0f) || !(scale > 0f))
            {
                Debug.LogError($"[City] charlotte_dem.bytes: header says {nx} x {nz} (cell {cell}, scale {scale})");
                return false;
            }
            if (version >= 3)
            {
                if (headLen + (long)off[off.Length - 1] != bytes.Length)
                {
                    Debug.LogError($"[City] charlotte_dem.bytes: v3 index says {off[off.Length - 1]} bytes of blocks, the file holds {bytes.Length - headLen}");
                    return false;
                }
                demBlob = bytes; demPay = headLen; demOff = off; demB = B; demNBX = nbx; demNBZ = nbz;
                blockSlot = new int[nbx * nbz];
                for (int k = 0; k < blockSlot.Length; k++) blockSlot[k] = -1;
                slotData = new ushort[BlockCacheSlots][];
                slotBlock = new int[BlockCacheSlots];
                for (int k = 0; k < BlockCacheSlots; k++) slotBlock[k] = -1;
                slotHand = 0;
                BlockDecodes = 0;
                dem = null;
            }
            else
            {
                long want = (long)nx * nz * 2;
                if (headLen + want != bytes.Length)
                {
                    Debug.LogError($"[City] charlotte_dem.bytes: header says {nx} x {nz} (cell {cell}, scale {scale}) but holds {bytes.Length - headLen} bytes of grid");
                    return false;
                }
                var grid = new ushort[nx * nz];
                if (System.BitConverter.IsLittleEndian) System.Buffer.BlockCopy(bytes, headLen, grid, 0, (int)want);
                else for (int i = 0; i < grid.Length; i++) grid[i] = (ushort)(bytes[headLen + 2 * i] | bytes[headLen + 2 * i + 1] << 8);
                dem = grid;
                demBlob = null; demOff = null; blockSlot = null; slotData = null; slotBlock = null;
            }
            demVersion = version;
            demNX = nx; demNZ = nz; demX0 = x0; demZ0 = z0; demCell = cell; demBase = bas; demScale = scale;
            return true;
        }

        /// <summary>The decoded block (bx, bz), <paramref name="w"/> nodes a
        /// row: from the cache, or decoded into the next slot round.</summary>
        static ushort[] Block(int bx, int bz, out int w)
        {
            w = Mathf.Min(demB, demNX - 1 - bx * demB) + 1;
            int id = bz * demNBX + bx;
            int slot = blockSlot[id];
            if (slot >= 0) return slotData[slot];
            slot = slotHand;
            slotHand = (slotHand + 1) % BlockCacheSlots;
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
            BlockDecodes++;
            return data;
        }

        /// <summary>One grid node's stored step (v1/v2 array or v3 block).</summary>
        static int Node(int ix, int iz)
        {
            if (dem != null) return dem[iz * demNX + ix];
            int bx = Mathf.Min(ix / demB, demNBX - 1), bz = Mathf.Min(iz / demB, demNBZ - 1);
            var blk = Block(bx, bz, out int w);
            return blk[(iz - bz * demB) * w + ix - bx * demB];
        }

        /// <summary>Metres per stored height step (the PDEM header's scale).</summary>
        public static float DemScale { get { EnsureDem(); return DemLoaded ? demScale : 0f; } }
        /// <summary>Metres between the grid's nodes (30 since WP-13).</summary>
        public static float DemCellM { get { EnsureDem(); return DemLoaded ? demCell : 0f; } }
        public static int DemVersion { get { EnsureDem(); return DemLoaded ? demVersion : 0; } }
        /// <summary>What the grid holds resident: v3's compressed blob and the
        /// decoded block cache, or v1/v2's whole array.</summary>
        public static long DemResidentBytes
        {
            get
            {
                EnsureDem();
                if (dem != null) return dem.LongLength * 2;
                if (demBlob == null) return 0;
                long n = demBlob.LongLength + (demOff.LongLength + blockSlot.LongLength + slotBlock.LongLength) * 4;
                foreach (var s in slotData) if (s != null) n += s.LongLength * 2;
                return n;
            }
        }

        /// <summary>The DEM's datum in metres above sea level: add it to a
        /// world y to get an altitude. 0 without a DEM.</summary>
        public static float DatumASL { get { EnsureDem(); return DemLoaded ? demBase : 0f; } }
        public static bool HasDem { get { EnsureDem(); return DemLoaded; } }

        public static float BaseY(float x, float z)
        {
            EnsureDem();
            if (!DemLoaded) return NoiseY(x, z);
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
                // ix <= nx - 2, so the cell's four nodes are in this one block
                int bx = Mathf.Min(ix / demB, demNBX - 1), bz = Mathf.Min(iz / demB, demNBZ - 1);
                var blk = Block(bx, bz, out int w);
                int k = (iz - bz * demB) * w + ix - bx * demB;
                a = blk[k]; b = blk[k + 1]; c = blk[k + w]; d = blk[k + w + 1];
            }
            return ((a * (1f - tx) + b * tx) * (1f - tz) + (c * (1f - tx) + d * tx) * tz) * demScale;
        }

        // ------------------------------------------------------------------
        //  THE ROADS' GROUND (WP-04). The solve reads the grid through a
        //  Gaussian of RoadDemSigmaCells cells of a 60 m grid; the land
        //  (Ground) reads the 30 m grid raw. On the real 3DEP ground two
        //  carriageways 20-40 m apart, or a ramp and the road it runs inside,
        //  sampled the grid's local slope at their own centrelines and came
        //  out at different heights (1.85 m between I-277's squeezed
        //  carriageways, a 2.5 m step where a slip lane changes host), which
        //  the filtered grid had hidden. Smoothing what the ROADS read, not
        //  the land, puts every road that shares a hillside on the same hill;
        //  the roadside grading still meets the real land beside them. WP-06
        //  replaces it with measured road profiles (plan R2).
        //
        //  WP-13: the roads keep reading a 60 m grid. It is rebuilt from the
        //  30 m nodes with the [1/4, 1/2, 1/4] weights each way, which is the
        //  60 m cell's area mean again (a 60 m cell is its own 30 m cell and
        //  half of each neighbour's), so the solved roads stay where WP-04 put
        //  them and only the land beside them gains the finer ground. It also
        //  keeps the one transient array at 811 x 916 floats (3 MB) instead of
        //  the 30 m grid's 12 MB.
        // ------------------------------------------------------------------
        public static float RoadDemSigmaCells = ReadRoadSigma();
        static float ReadRoadSigma()
        {
            // a measuring override (nothing sets it in a build)
            var v = System.Environment.GetEnvironmentVariable("PSX_CITY_ROADSIGMA");
            return v != null && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : RoadDemSigmaDefault;
        }
        public const float RoadDemSigmaDefault = 0.8f;
        /// <summary>The cell of the grid the roads read (see above).</summary>
        public const float RoadGridCellM = 60f;
        static float[] roadDem;
        static int roadNX, roadNZ;
        static float roadCell;

        /// <summary>The grid the solve reads (see RoadDemSigmaCells), built at
        /// the start of a solve and dropped at its end.</summary>
        static void BuildRoadDem()
        {
            roadDem = null;
            if (!DemLoaded || !(RoadDemSigmaCells > 0.05f)) return;
            // 30 m nodes -> the 60 m grid (every second node, 1-2-1 each way)
            int f = Mathf.Max(1, Mathf.RoundToInt(RoadGridCellM / demCell));
            if (f != 1 && f != 2) f = 1;
            int nx = (demNX - 1) / f + 1, nz = (demNZ - 1) / f + 1;
            // ONE grid-sized array (3 MB for 811 x 916): the coarse grid is
            // built into it, its rows are blurred through a row buffer, then
            // each column in place through a column buffer. A second full
            // array would be 3 MB more of a phone's WebGL heap at load, which
            // never shrinks.
            var outp = new float[nx * nz];
            if (f == 1)
            {
                for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++) outp[z * nx + x] = Node(x, z);
            }
            else
            {
                // row by row of 30 m nodes, so the block cache walks one row of
                // blocks at a time; each node adds its weight to the coarse
                // nodes it belongs to, and the edges divide by what they got
                for (int iz = 0; iz < demNZ; iz++)
                {
                    int z0 = iz / 2; bool zOdd = (iz & 1) != 0;
                    for (int ix = 0; ix < demNX; ix++)
                    {
                        float v = Node(ix, iz);
                        int x0 = ix / 2; bool xOdd = (ix & 1) != 0;
                        if (!zOdd)
                        {
                            if (!xOdd) outp[z0 * nx + x0] += 0.25f * v;
                            else { outp[z0 * nx + x0] += 0.125f * v; if (x0 + 1 < nx) outp[z0 * nx + x0 + 1] += 0.125f * v; }
                        }
                        else
                        {
                            for (int zz = z0; zz <= z0 + 1 && zz < nz; zz++)
                            {
                                if (!xOdd) outp[zz * nx + x0] += 0.125f * v;
                                else { outp[zz * nx + x0] += 0.0625f * v; if (x0 + 1 < nx) outp[zz * nx + x0 + 1] += 0.0625f * v; }
                            }
                        }
                    }
                }
                // an edge node lost the half of its kernel outside the grid
                for (int z = 0; z < nz; z++)
                {
                    float wz = z == 0 || z == nz - 1 ? 0.75f : 1f;
                    for (int x = 0; x < nx; x++)
                        outp[z * nx + x] /= wz * (x == 0 || x == nx - 1 ? 0.75f : 1f);
                }
            }
            float sg = RoadDemSigmaCells;
            int r = Mathf.CeilToInt(sg * 3f);
            var k = new float[2 * r + 1];
            float ks = 0f;
            for (int i = -r; i <= r; i++) { k[i + r] = Mathf.Exp(-0.5f * i * i / (sg * sg)); ks += k[i + r]; }
            for (int i = 0; i < k.Length; i++) k[i] /= ks;
            var row = new float[nx];
            for (int z = 0; z < nz; z++)
            {
                System.Array.Copy(outp, z * nx, row, 0, nx);
                for (int x = 0; x < nx; x++)
                {
                    float acc = 0f;
                    for (int i = -r; i <= r; i++) acc += k[i + r] * row[Mathf.Clamp(x + i, 0, nx - 1)];
                    outp[z * nx + x] = acc;
                }
            }
            var col = new float[nz];
            for (int x = 0; x < nx; x++)
            {
                for (int z = 0; z < nz; z++) col[z] = outp[z * nx + x];
                for (int z = 0; z < nz; z++)
                {
                    float acc = 0f;
                    for (int i = -r; i <= r; i++) acc += k[i + r] * col[Mathf.Clamp(z + i, 0, nz - 1)];
                    outp[z * nx + x] = acc * demScale;
                }
            }
            roadNX = nx; roadNZ = nz; roadCell = demCell * f;
            roadDem = outp;
        }

        /// <summary>The ground as the road solve reads it: <see cref="BaseY"/>
        /// through the roads' Gaussian while a solve runs.</summary>
        public static float RoadBaseY(float x, float z)
        {
            if (roadDem == null) return BaseY(x, z);
            x /= CityMap.LayoutScale; z /= CityMap.LayoutScale;
            float fx = (x - demX0) / roadCell, fz = (z - demZ0) / roadCell;
            int ix = Mathf.Clamp(Mathf.FloorToInt(fx), 0, roadNX - 2);
            int iz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, roadNZ - 2);
            float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
            float a = roadDem[iz * roadNX + ix], b = roadDem[iz * roadNX + ix + 1];
            float c = roadDem[(iz + 1) * roadNX + ix], d = roadDem[(iz + 1) * roadNX + ix + 1];
            return (a * (1f - tx) + b * tx) * (1f - tz) + (c * (1f - tx) + d * tx) * tz;
        }

        /// <summary>How far apart two carriageways of one divided road may be
        /// and still read one terrain line (see <see cref="PairedRoadBaseY"/>).</summary>
        public const float PairReachM = 45f;
        /// <summary>Stations and nodes that read the ground at a divided
        /// road's midline in the last solve.</summary>
        public static int PairedStations { get; private set; }
        /// <summary>Per edge, the edges that may be its opposite carriageway
        /// (see <see cref="PairedRoadBaseY"/>): one-way, not a ramp, the same
        /// name, their boxes within <see cref="PairReachM"/>. Found once per
        /// solve by name, so a station tests a handful of edges instead of
        /// querying the segment hash (that query cost the solve 0.4 s).</summary>
        static int[][] pairCands;
        /// <summary>Per candidate edge, the box of every block of
        /// <see cref="PairBlock"/> segments, so a station skips the blocks
        /// that cannot hold its foot (I-485's edges run to hundreds of
        /// points).</summary>
        static Rect[][] pairBlocks;
        const int PairBlock = 16;

        static bool PairCarriageway(CityMap.Edge e) => e.oneway && !e.link && !string.IsNullOrEmpty(e.name);

        static void BuildPairCandidates(CityMap map)
        {
            pairCands = new int[map.edges.Length][];
            pairBlocks = new Rect[map.edges.Length][];
            var byName = new Dictionary<string, List<int>>();
            foreach (var o in map.edges)
            {
                if (!PairCarriageway(o) || o.pts.Length < 2) continue;
                if (!byName.TryGetValue(o.name, out var l)) byName[o.name] = l = new List<int>();
                l.Add(o.index);
                int segs = o.pts.Length - 1, nb = (segs + PairBlock - 1) / PairBlock;
                var blocks = new Rect[nb];
                for (int b = 0; b < nb; b++)
                {
                    int s0 = b * PairBlock, s1 = Mathf.Min(segs, s0 + PairBlock);
                    Vector2 mn = o.pts[s0], mx = o.pts[s0];
                    for (int k = s0 + 1; k <= s1; k++) { mn = Vector2.Min(mn, o.pts[k]); mx = Vector2.Max(mx, o.pts[k]); }
                    blocks[b] = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
                }
                pairBlocks[o.index] = blocks;
            }
            var box = new Rect[map.edges.Length];
            foreach (var l in byName.Values)
                foreach (int i in l)
                {
                    var o = map.edges[i];
                    Vector2 mn = o.pts[0], mx = o.pts[0];
                    foreach (var q in o.pts) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                    box[i] = Rect.MinMaxRect(mn.x - PairReachM, mn.y - PairReachM, mx.x + PairReachM, mx.y + PairReachM);
                }
            var tmp = new List<int>();
            foreach (var e in map.edges)
            {
                if (!PairCarriageway(e) || e.cls < 2 || !byName.TryGetValue(e.name, out var l)) continue;
                tmp.Clear();
                foreach (int i in l)
                    if (i != e.index && box[i].Overlaps(box[e.index])) tmp.Add(i);
                if (tmp.Count > 0) pairCands[e.index] = tmp.ToArray();
            }
        }

        /// <summary>
        /// The roads' ground under a carriageway of a DIVIDED road, read at
        /// the midline between it and its opposite carriageway: the nearest
        /// one-way, non-ramp edge of the same name within
        /// <see cref="PairReachM"/>, running the other way. Both carriageways
        /// then climb the same hill. On the real ground (WP-04) the two sides
        /// of I-77, I-85 or I-277 read the grid's slope 20-40 m apart and
        /// solved up to a metre apart, which squeezed carriageways show as an
        /// open edge between them. Anything else reads
        /// <see cref="RoadBaseY"/> at its own point.
        /// </summary>
        static float PairedRoadBaseY(CityMap map, CityMap.Edge e, Vector2 p, Vector2 dir)
        {
            var cands = pairCands != null && e.index < pairCands.Length ? pairCands[e.index] : null;
            if (cands == null) return RoadBaseY(p.x, p.y);
            float best = PairReachM; Vector2 q = p; bool found = false;
            foreach (int oi in cands)
            {
                var o = map.edges[oi];
                var blocks = pairBlocks[oi];
                for (int si = 0; si + 1 < o.pts.Length; si++)
                {
                    if (si % PairBlock == 0)
                    {
                        var bb = blocks[si / PairBlock];
                        if (p.x < bb.xMin - best || p.x > bb.xMax + best || p.y < bb.yMin - best || p.y > bb.yMax + best)
                        { si += PairBlock - 1; continue; }
                    }
                    Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                    // a cheap reject before the foot: both ends past the reach on one side
                    if ((a.x < p.x - best && a.x + d.x < p.x - best) || (a.x > p.x + best && a.x + d.x > p.x + best) ||
                        (a.y < p.y - best && a.y + d.y < p.y - best) || (a.y > p.y + best && a.y + d.y > p.y + best)) continue;
                    float L2 = d.sqrMagnitude;
                    if (L2 < 1e-6f) continue;
                    if (Vector2.Dot(d / Mathf.Sqrt(L2), dir) > -0.85f) continue;   // not running the other way
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / L2);
                    var f = a + d * t;
                    float dist = Vector2.Distance(p, f);
                    if (dist < best) { best = dist; q = f; found = true; }
                }
            }
            if (!found) return RoadBaseY(p.x, p.y);
            PairedStations++;
            var m = (p + q) * 0.5f;
            return RoadBaseY(m.x, m.y);
        }

        static float NoiseY(float x, float z)
        {
            return ValueNoise(x, z, 1701f) * 8.0f
                 + ValueNoise(x + 9173f, z - 4711f, 613f) * 4.2f
                 + ValueNoise(x - 3137f, z + 8291f, 211f) * 1.5f;
        }

        static float ValueNoise(float x, float z, float wavelength)
        {
            float fx = x / wavelength, fz = z / wavelength;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = Hash01(ix, iz), b = Hash01(ix + 1, iz);
            float c = Hash01(ix, iz + 1), d = Hash01(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz) * 2f - 1f;
        }

        static float Hash01(int x, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + z * 668265263) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // ------------------------------------------------------------------
        //  The solve, at load.
        // ------------------------------------------------------------------
        /// <summary>Which crossings the solver actually enforced after the
        /// mutual-conflict prune. The audit reads this: judging a pruned
        /// crossing by the clearance rule it was excused from is a false FAIL.</summary>
        public static bool[] EnforcedCrossings { get; private set; }
        /// <summary>Crossings solved as a trench under the over road rather
        /// than a hump over the under road. For the audit and the preview.</summary>
        public static bool[] TrenchedCrossings { get; private set; }
        public static int TrenchCount { get; private set; }
        /// <summary>Per crossing (plan B2, the PROFILE report): why a
        /// crossing whose under road is a freeway mainline was NOT dug into a
        /// trench; null where it was, and where the rule never applied (the
        /// under road is no freeway mainline).</summary>
        public static string[] TrenchWhy { get; private set; }
        /// <summary>One line on the last solve's trench decisions (plan B2),
        /// for the PROFILE report; null before B2's group rule.</summary>
        public static string TrenchDecisionReport { get; private set; }

        /// <summary>Where the last solve spent its time (ms per phase), for
        /// the budget probe.</summary>
        public static string LastSolvePhases { get; private set; } = "";

        /// <summary>For the launch audit (Editor/CityLaunchAudit): keep each
        /// edge's station profile as the terrain gave it (step 1, before any
        /// trench, seat, raise or cone), so a crest can be traced to the pass
        /// that made it and the solve's distance from the survey measured.
        /// Off in the game.</summary>
        public static bool KeepTerrainProfiles;
        public static float[][] TerrainProfiles { get; private set; }

        public static void Solve(CityMap map)
        {
            var phaseClock = System.Diagnostics.Stopwatch.StartNew();
            var phases = new System.Text.StringBuilder();
            void Phase(string name) { phases.Append(name).Append(' ').Append(phaseClock.ElapsedMilliseconds).Append(", "); phaseClock.Restart(); }
            crossingOn = null;
            crossingTarget = null;
            routeEdge = new bool[map.edges.Length];
            if (map.routes != null) foreach (var rt in map.routes) foreach (int ei in rt.edges) routeEdge[ei] = true;
            EnsureDem();
            BuildRoadDem();
            Phase("road grid");
            BuildPairCandidates(map);
            PrepareWater(map);
            Phase("pairs+water");
            map.nodeY = new float[map.nodes.Length];
            PairedStations = 0;
            for (int i = 0; i < map.nodes.Length; i++)
            {
                // a node of a divided road reads the midline too, through its
                // first carriageway arm (its ends must agree with the stations)
                map.nodeY[i] = RoadBaseY(map.nodes[i].x, map.nodes[i].y);
                foreach (int ei in map.nodeEdges[i])
                {
                    var ne = map.edges[ei];
                    if (!ne.oneway || ne.link || ne.cls < 2 || ne.length < 1f) continue;
                    var dir = ne.a == i ? ne.TangentAt(0f) : ne.TangentAt(ne.length);
                    map.nodeY[i] = PairedRoadBaseY(map, ne, map.nodes[i], dir);
                    break;
                }
            }

            // 1. per-edge profile from the terrain
            foreach (var e in map.edges)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(e.length / StationStep) + 1);
                e.stS = new float[n];
                e.stY = new float[n];
                e.stElev = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    float at = i == n - 1 ? e.length : i * e.length / (n - 1);
                    e.stS[i] = at;
                    var p = e.PointAt(at);
                    e.stY[i] = PairedRoadBaseY(map, e, p, e.TangentAt(at));
                }
                Smooth(e.stY, 2.5f);
                ClampGrade(e, MaxGrade(e));
                BlendEndsToNodes(map, e);
                // OSM's bridges are decks end to end, whatever the terrain
                // under them does.
                if (e.bridge) for (int i = 0; i < n; i++) e.stElev[i] = true;
            }
            TerrainProfiles = null;
            if (KeepTerrainProfiles)
            {
                TerrainProfiles = new float[map.edges.Length][];
                foreach (var e in map.edges) TerrainProfiles[e.index] = (float[])e.stY.Clone();
            }
            Phase("profiles");

            // 2. trenches, before anything lifts anything: they only ever
            // LOWER a freeway, and every raise below reads the lowered height.
            SinkTrenches(map);
            Phase("trenches");

            // 2b. every ramp beside its mainline IS the mainline there. Seated
            // now, so no junction below reads a ramp end that disagrees with
            // the road it joins; locked, so no raise lifts it off again; and
            // re-seated after every pass that moves a host.
            var seatClock = System.Diagnostics.Stopwatch.StartNew();
            PrepareSeats(map);
            SeatPrepMs = seatClock.ElapsedMilliseconds;
            SeatBranches(map);
            Phase("seats");

            // 3-7. structure and junction agreement, to a fixed point.
            //
            // Two forces both only push roads UP: a crossing lifts its OVER
            // edge clear of the road below, and a junction node takes the
            // highest incident end so ramps meet the mainline they climb to.
            // Each can invalidate the other, so neither order of two passes
            // settles it. Monotone + bounded means iterating CONVERGES; run to
            // quiescence and finish on a raise, so the clearance rule is the
            // one that holds exactly.
            for (int it = 0; it < 4; it++)
            {
                RaiseAllCrossings(map);
                HoldWaterSpans(map);
                HoldCulverts(map);
                HoldBridges(map);
                float moved = ReconcileNodes(map);
                SeatBranches(map);
                if (moved < 0.05f) break;
            }

            // 8. a gentle pass over the interiors: reconciliation can leave a
            // cliff INSIDE a mid-length edge (its ends are fixed by nodes that
            // genuinely disagree). Ends stay put; interiors ease. BEFORE the
            // final raise, so nothing can lower a deck after its clearance is
            // guaranteed.
            foreach (var e in map.edges)
            {
                // NEVER STEEPER THAN THE EDGE'S OWN ENDS. The forward sweep
                // pins to the start and the backward sweep to the end, so
                // when the ends disagree by more than the clamp can span the
                // two sweeps meet in a cliff at the first station — 2.4 m in
                // 10 m on a 124 m piece of I-277 whose nodes sat 9.7 m apart.
                // An edge that has to climb 7.8% end to end climbs 7.8% all
                // the way, which is what the road honestly does.
                int last = e.stY.Length - 1;
                float ends = Mathf.Abs(e.stY[last] - e.stY[0]) / Mathf.Max(e.length, 1f);
                float g = Mathf.Max(MaxGrade(e) * 1.6f, ends * 1.05f);
                for (int pass = 0; pass < 2; pass++)
                {
                    // A seated station is a fixed point the sweep eases
                    // TOWARD, never one it moves.
                    for (int i = 1; i < e.stY.Length - 1; i++)
                    {
                        if (e.SeatedAt(i)) continue;
                        float ds = e.stS[i] - e.stS[i - 1];
                        e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i - 1] - g * ds, e.stY[i - 1] + g * ds);
                    }
                    for (int i = e.stY.Length - 2; i >= 1; i--)
                    {
                        if (e.SeatedAt(i)) continue;
                        float ds = e.stS[i + 1] - e.stS[i];
                        e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i + 1] - g * ds, e.stY[i + 1] + g * ds);
                    }
                }
            }

            // Holds before the final raise; nothing below this line may move a
            // road except the raise itself. Three fresh passes: within one, an
            // under-road that is itself OVER something later in the same
            // layer group can rise after being measured.
            HoldWaterSpans(map);
            HoldCulverts(map);
            HoldBridges(map);
            RaiseAllCrossings(map, fresh: true);
            RaiseAllCrossings(map, fresh: true);
            RaiseAllCrossings(map, fresh: true);
            SeatBranches(map);

            // THE ENDS MUST MEET, AND THE APPROACH MUST HAVE ROOM. The fresh
            // raises lift some edge ends; the snap takes the highest at each
            // node; but the other edges' ends stayed where they were, and at
            // a degree-2 node that is a step the size of the lift (4.6 m in
            // 1 m on the 277 belt, in the first race path built through the
            // graph). Blending each edge's own ends up to its nodes was the
            // first answer and it fails on short edges: a 36 m street
            // approach to a bridge lifted 6.6 m is a 37% ramp however it is
            // blended within itself. A real approach embankment runs back
            // through the next junctions, and so does this: every node whose
            // incident ends fall short of it seeds an APPROACH CONE at 4.5%
            // through the graph (RaiseCone), which carries on through any
            // node it still stands above. Raises only; iterated with the
            // crossing raise until nothing moves, ending on the cones so the
            // ends are exact (the audit's clearance margin covers the few
            // centimetres a final cone can take from a deck's underside).
            // A seat can move a ramp's FAR end (see ClimbOut), and that end's
            // node and neighbours are this loop's business: iterate until
            // neither the cones nor the seats move anything.
            //
            // TWIN DECKS (plan B1, critic C12): the two decks of one structure
            // (DeckPairs: West 5th Street's two carriageways over I-77) take
            // ONE height - raises only, inside this loop, so the cones carry
            // each hold into the approaches and the vertical curves below
            // round every crest it makes. The loop ends only when the holds
            // move nothing either.
            TwinHoldMoves = 0; TwinHoldSkipped = 0; TwinHoldRounds = 0;
            for (int k = 0; k < 12; k++)
            {
                RaiseAllCrossings(map, fresh: true);
                SnapNodesToEnds(map);
                int seatMoves = SeatBranches(map);
                int twinMoves = HoldTwinDecks(map);
                TwinHoldMoves += twinMoves;
                // a street the loop still lifted over its cut: the cut gives
                // the excess back (plan B2)
                int relaxMoves = RelaxTrenches(map);
                if (RaiseConesFromNodes(map) == 0 && seatMoves == 0 && twinMoves == 0 && relaxMoves == 0) break;
            }
            SnapNodesToEnds(map);
            SeatBranches(map);
            Phase("raises");

            // 8b. VERTICAL CURVES: every crest the passes above left sharp
            // (a hump's apex, a cone's tent, a seat's climb-out, two arms
            // meeting at a node) is rounded to what a car at the road's speed
            // can take without leaving it. See VerticalCurves.
            if (VerticalCurvesOn)
            {
                for (int round = 0; round < 2; round++)
                {
                    VerticalCurves(map, round);
                    RaiseAllCrossings(map, fresh: true);
                    SnapNodesToEnds(map);
                    SeatBranches(map);
                    // a crest the curves rounded by lifting a street over its
                    // cut: the cut gives that back too (plan B2), before the
                    // last round, which rounds what this moves (critic C12)
                    if (RelaxTrenches(map) > 0)
                    {
                        RaiseAllCrossings(map, fresh: true);
                        SnapNodesToEnds(map);
                        RaiseConesFromNodes(map);
                        SnapNodesToEnds(map);
                        SeatBranches(map);
                    }
                }
                // and once more with nothing after it: the snap and the seats
                // leave a node above an arm's end (the fan's centre stands at
                // the node, the arm's corner at its own end: a ramp inside the
                // fan). The pass starts by lifting every end to its node, and
                // it can neither raise a road under a crossing nor lower the
                // deck over it, so the clearances the raise just set hold.
                VerticalCurves(map, 2);
            }
            // THE TWINS AFTER THE CURVES (critic C12). The curves round each
            // carriageway on its own chain - its own junction planes, pins and
            // crossing windows - so twins held to one height come out of them
            // up to ~0.2 m apart. A RaiseHump now would put back unrounded
            // humps, and re-running the curves broke the twins again (two
            // rounds left 0.198 m) while lifting the stations round every
            // crest they could not settle (3.5 m margin decks +8.8%: the
            // 2026-10-02 run). So the last word is a POINTWISE hold: each
            // interior deck station beside its twin rises to the twin's height
            // there, nothing else moves. The higher of two crest-limited
            // profiles is crest-limited (the larger of two functions only bends
            // UP where they cross), so this adds no crest the curves did not
            // allow; it may add a gentle sag where the two cross, which is
            // measured (TwinFinalSagAdded) for the TWIN report and package B4.
            TwinDyBeforeFinal = TwinDy(map, out _);
            FinalTwinHold(map);
            TwinDyAfterCurves = TwinDy(map, out TwinDyWorstPair);
            Phase("vcurves");

            // 9. mark structure LAST, from the facts: decks over crossings,
            // embankments everywhere else.
            MarkStructure(map);
            InheritSeatStructure(map);
            AlignTwinStructure(map);
            // the twin-deck table on what is on structure NOW (critic C1):
            // decks only the solve made, side by side, are pairs too
            DeckPairs.Complete(map);
            MeasureTwinDy(map);
            MeasureSags(map);

            // (10. the water was prepared first: PrepareWater)
            Phase("structure");
            LastSolvePhases = phases.ToString().TrimEnd(' ', ',') + " ms";
            TwinHoldReport = !TwinHoldsOn ? "twin holds OFF (PSX_CITY_TWINHOLD=0)"
                : $"twin holds: {TwinHoldMoves} raises in the cone loop ({TwinHoldSkipped} station checks left apart, more than {TwinHoldMaxDyM} m off their partner); after the curves {TwinDyBeforeFinal:0.000} m apart at worst, the pointwise hold raised {TwinFinalRaised} stations (up to {TwinFinalRaiseMax:0.000} m; {TwinFinalClamped} taken back by the crest guard; sag added up to {TwinFinalSagAdded * 100f:0.00}%, crest added up to {TwinFinalCrestAdded * 100f:0.000}%), worst station dy now {TwinDyAfterCurves:0.000} m; TWIN (e) p50 {TwinDyP50:0.000} / p95 {TwinDyP95:0.000} / max {TwinDyMax:0.000} m over {TwinDySamples} samples" +
                  (TwinDyWorstPair >= 0 ? $" (e{map.deckPairs[TwinDyWorstPair].a}/e{map.deckPairs[TwinDyWorstPair].b})" : "") +
                  $"; deck ends aligned on {TwinAlignedPairs} pairs (+{TwinAlignedStations} stations on structure); 3.5 m margin stations {MarginStructureStations} ({MarginStationsNearTwins} on or beside a twin deck's edge)";
            roadDem = null;
            pairCands = null;
            pairBlocks = null;
        }

        /// <summary>
        /// The water, before any road is solved (it depends on no road): each
        /// bed into the world frame (WBED is metres ASL); each lake one flat
        /// surface, its level in the hydro-flattened 3DEP where the data has
        /// it, else the lowest of its shore; and the water surface under each
        /// water span, which <see cref="HoldWaterSpans"/> keeps the deck
        /// clear of.
        /// </summary>
        static void PrepareWater(CityMap map)
        {
            foreach (var w in map.waters)
            {
                w.bedY = null;
                if (w.bedASL != null && HasDem)
                {
                    w.bedY = new float[w.bedASL.Length];
                    for (int k = 0; k < w.bedY.Length; k++) w.bedY[k] = w.bedASL[k] - DatumASL;
                }
                if (!w.lake) { w.surfaceY = 0f; continue; }
                if (w.bedY != null) { w.surfaceY = w.bedY[0] - LakeBelowLevel; continue; }
                float min = float.MaxValue;
                foreach (var p in w.pts) min = Mathf.Min(min, BaseY(p.x, p.y));
                w.surfaceY = min - 0.6f;
            }
            spanWaterY = new float[map.wspans.Length];
            spanLifted = new bool[map.wspans.Length];
            SpansLiftedForWater = 0; SpanWaterLiftMax = 0f;
            CulvertCrossingsHeld = 0; CulvertHoldMaxM = 0f; culvertHeld.Clear();
            var near = new HashSet<int>();
            for (int k = 0; k < map.wspans.Length; k++)
            {
                spanWaterY[k] = float.NaN;
                var ws = map.wspans[k];
                var e = map.edges[ws.edge];
                var p = e.PointAt((Mathf.Clamp(ws.s0, 0f, e.length) + Mathf.Clamp(ws.s1, 0f, e.length)) * 0.5f);
                near.Clear();
                map.WaterSegsInRect(p - Vector2.one * 40f, p + Vector2.one * 40f, near);
                float best = 40f;
                foreach (int packed in near)
                {
                    var w = map.waters[packed >> 12];
                    if (w.lake)
                    {
                        if (CityMap.LakeContains(w, p)) { spanWaterY[k] = w.surfaceY; best = 0f; }
                        continue;
                    }
                    if (w.bedY == null) continue;   // no bed, no level: the old rules stand
                    float d = WaterFoot(w, packed & 0xFFF, p, out float sw);
                    if (d < best) { best = d; spanWaterY[k] = CreekSurfaceY(w, sw, p); }
                }
            }
        }

        /// <summary>
        /// Dig a freeway under the surface streets that cross it.
        ///
        /// Only where the OVER road is a street (or an expressway) and the
        /// UNDER road a freeway mainline. Everywhere else — freeway over
        /// freeway, ramp over anything — the hump rule stands. The street
        /// keeps its ground level and its OSM bridge tag makes it a deck;
        /// when the tag is a bare layer=1 with no bridge=yes the stations over
        /// the cut are marked structure here, or the street would pin the
        /// ground up under itself and float over the trench.
        ///
        /// THE CUT RUNS THROUGH THE INTERCHANGES. The first cut only trenched
        /// a crossing 150 m clear of the freeway's next junction, so the
        /// trough could climb back out before a node whose height is the
        /// highest of its incident ends. Uptown's freeways have a ramp every
        /// two hundred metres; most crossings failed the test, the street
        /// was humped 6.6 m onto a 58 m bridge, and its 36 m approach edges
        /// could not climb that (26% steps on North Caldwell Street). Real
        /// Charlotte keeps the street at grade and the Belk in one continuous
        /// cut, with the ramps climbing out of it. So: the trough is carried
        /// through the freeway's own nodes into the next mainline edge, those
        /// nodes are PINNED to the cut (<see cref="pinnedNodeY"/> — the one
        /// place the max-of-ends rule yields), and every ramp meeting them
        /// blends DOWN to the cut, once, before the raise loop begins.
        /// Afterwards the cut is an ordinary set of low heights the monotone
        /// solver only ever raises where something real demands it.
        /// </summary>
        static float[] pinnedNodeY;

        /// <summary>PSX_CITY_TRENCHRULE=0: the trench decisions as they were
        /// before plan B2 - per crossing, a whole edge exempt for a water span
        /// anywhere on it, <see cref="TrenchEndM"/> to the EDGE's end, ramps
        /// never dug (a measuring override; nothing sets it in a build).</summary>
        public static bool TrenchGroupsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TRENCHRULE") != "0";
        /// <summary>Crossings of one street over one freeway within this of
        /// each other are ONE decision (plan B2): West 5th Street's two
        /// carriageways over I-77's two are four crossings and one cut.</summary>
        public const float TrenchGroupReachM = 60f;
        /// <summary>A ramp under the street within this of a dug mainline's
        /// crossing, running within acos(<see cref="RampEnvelopeCos"/>) of it,
        /// is in the same cut (plan B2 rule (d)).</summary>
        public const float RampEnvelopeM = 60f, RampEnvelopeCos = 0.9f;
        /// <summary>A ramp in the cut climbs back out at its own grade.</summary>
        const float RampCutGrade = 0.08f;
        /// <summary>A trench's cut stays this far clear of a water span.</summary>
        public const float TrenchWaterPadM = 10f;
        /// <summary>The crossings the B2 rule decided differently from the
        /// old one, one line each, for the PROFILE report.</summary>
        public static List<string> TrenchRedecided { get; private set; } = new List<string>();

        /// <summary>
        /// THE TRENCH DECISIONS (plan B2, 2026-10-02). Before, each crossing
        /// decided alone: a water span ANYWHERE on the freeway's edge exempted
        /// the whole edge (I-77 southbound under West 5th St, for a creek
        /// 240 m away), a crossing within 20 m of the EDGE's end was skipped
        /// (an OSM way split is no junction: Rea Rd over I-485 at 15 m and
        /// 2 m), and ramps were never dug - so one carriageway sat in a cut
        /// and the street humped over the other, 10.8 m over the dug one
        /// where the real separation is 4.9-5.5 m, the two carriageways 5.0 m
        /// apart in height (real 0.4). Now:
        ///   * a crossing's cut is blocked by water only where the cut reaches
        ///     it: depth / 4.5% + 10 m along the freeway, through its nodes;
        ///   * <see cref="TrenchEndM"/> is the distance along the freeway to
        ///     its nearest REAL junction (a node of three arms or more),
        ///     through way splits;
        ///   * crossings of one street over one freeway within
        ///     <see cref="TrenchGroupReachM"/> are ONE decision: all dug, to
        ///     one bottom (the lowest any of them needs), flat on each
        ///     carriageway across every member's section - or, where any
        ///     member's cut would reach water, or every member stands at a
        ///     junction, none;
        ///   * a ramp passing under the same street beside a dug mainline
        ///     (<see cref="RampEnvelopeM"/>, parallel) is dug with it, as deep
        ///     as its own street needs, climbing out at a ramp's grade through
        ///     the nodes where only ramps meet, so it no longer humps the
        ///     street;
        ///   * where the solve still lifts a street over its cut, the cut
        ///     gives the excess back (<see cref="RelaxTrenches"/>).
        /// The DECISION layer only (critic D1): the cut's shape is still the
        /// 4.5% V the solver always made; plan B4 rounds it, B7 replaces it.
        /// </summary>
        static void SinkTrenches(CityMap map)
        {
            int NC = map.crossings.Length;
            TrenchedCrossings = new bool[NC];
            TrenchCount = 0;
            TrenchWhy = new string[NC];
            TrenchRedecided = new List<string>();
            TrenchDecisionReport = null;
            trenchGroups = null; TrenchRelaxRaised = 0; TrenchRelaxGroups = 0;
            trenchUnits = new List<(int group, List<int> crossings)>(); trenchPost = null; trenchGiven = null;
            trenchPre = new Dictionary<long, float>(); trenchWant = new Dictionary<long, List<(int g, float want)>>();
            pinnedNodeY = new float[map.nodes.Length];
            for (int i = 0; i < pinnedNodeY.Length; i++) pinnedNodeY[i] = float.NaN;

            // which edges carry water, and where
            var wet = new bool[map.edges.Length];
            var spansOf = new List<Vector2>[map.edges.Length];
            foreach (var ws in map.wspans) { wet[ws.edge] = true; (spansOf[ws.edge] ??= new List<Vector2>(2)).Add(new Vector2(ws.s0, ws.s1)); }

            // (edge, flat bottom from sLo to sHi, target, climb grade, carried
            // through nodes: 1 into the freeway mainline, 2 a ramp's into the
            // ramps it continues as, 0 not)
            var pending = new List<(int edge, float sLo, float sHi, float target, float grade, int carry, int group)>();
            void Dig(int ci, CityMap.Edge over, float sO, CityMap.Edge under)
            {
                float half = under.width * 0.5f + 7f;
                for (int i = 0; i < over.stS.Length; i++)
                    if (Mathf.Abs(over.stS[i] - sO) <= half) over.stElev[i] = true;
                // (plan B2) a crossing near the street's node: its deck runs on
                // into the street beyond, or that street's first metres stand
                // grounded over the cut (East 7th Street's e9180 put a
                // retaining face beside the lane of the ramp dug under it)
                if (TrenchGroupsOn)
                {
                    if (sO - half < 0f) DeckThrough(map, over, over.a, half - sO, 0);
                    if (sO + half > over.length) DeckThrough(map, over, over.b, sO + half - over.length, 0);
                }
                TrenchedCrossings[ci] = true;
                TrenchCount++;
            }

            // the old rule, per crossing (also the B2 report's "before")
            var oldWhy = new string[NC];
            var sUs = new float[NC]; var sOs = new float[NC];
            var cand = new List<int>();
            for (int ci = 0; ci < NC; ci++)
            {
                var c = map.crossings[ci];
                var over = map.edges[c.over];
                var under = map.edges[c.under];
                bool mainline = under.cls >= 5 && !under.link;
                if (!mainline) continue;
                ProjectOn(under, c.at, out sUs[ci]);
                ProjectOn(over, c.at, out sOs[ci]);
                if (!c.forced) { TrenchWhy[ci] = oldWhy[ci] = "not forced (layers guessed)"; continue; }
                if (under.tunnel) { TrenchWhy[ci] = oldWhy[ci] = "under road a tunnel"; continue; }
                if (over.cls >= 5 || over.link) { TrenchWhy[ci] = oldWhy[ci] = "over road a freeway or ramp"; continue; }
                if (wet[c.under]) oldWhy[ci] = "a water span somewhere on the under edge (whole edge)";
                else if (sUs[ci] < TrenchEndM || under.length - sUs[ci] < TrenchEndM) oldWhy[ci] = $"within {TrenchEndM:0} m of the under edge's end";
                cand.Add(ci);
            }

            if (!TrenchGroupsOn)
            {
                foreach (int ci in cand)
                {
                    var c = map.crossings[ci];
                    if (oldWhy[ci] != null) { TrenchWhy[ci] = oldWhy[ci]; continue; }
                    var over = map.edges[c.over];
                    pending.Add((c.under, sUs[ci], sUs[ci], over.YAt(sOs[ci]) - ClearanceM - DeckThick, ApproachGrade, 1, -1));
                    Dig(ci, over, sOs[ci], map.edges[c.under]);
                }
                TrenchDecisionReport = "the trench rule before plan B2 (PSX_CITY_TRENCHRULE=0)";
            }
            else
            {
                // ---- one decision per group of crossings
                var parent = new Dictionary<int, int>(cand.Count);
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                foreach (int ci in cand) parent[ci] = ci;
                for (int p = 0; p < cand.Count; p++)
                    for (int q = p + 1; q < cand.Count; q++)
                    {
                        var cp = map.crossings[cand[p]]; var cq = map.crossings[cand[q]];
                        if ((cp.at - cq.at).sqrMagnitude > TrenchGroupReachM * TrenchGroupReachM) continue;
                        if (RoadKey(map.edges[cp.over]) != RoadKey(map.edges[cq.over])) continue;
                        if (RoadKey(map.edges[cp.under]) != RoadKey(map.edges[cq.under])) continue;
                        parent[Find(cand[p])] = Find(cand[q]);
                    }
                var groups = new SortedDictionary<int, List<int>>();
                foreach (int ci in cand) { int r = Find(ci); if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>(); l.Add(ci); }

                int dugGroups = 0, dugX = 0, humpWater = 0, humpJunction = 0, rampX = 0, nowDug = 0, nowHumped = 0;
                var groupOfDug = new List<(List<int> members, float target)>();
                trenchGroups = new List<List<int>>();
                foreach (var kv in groups)
                {
                    var members = kv.Value;
                    string hard = null; int soft = 0;
                    float target = float.MaxValue;
                    foreach (int ci in members)
                    {
                        var c = map.crossings[ci];
                        var over = map.edges[c.over]; var under = map.edges[c.under];
                        float t = over.YAt(sOs[ci]) - ClearanceM - DeckThick;
                        target = Mathf.Min(target, t);
                        string w = CutReachesWater(map, spansOf, c.under, sUs[ci], under.YAt(sUs[ci]) - t, ApproachGrade, true);
                        if (w != null && hard == null) hard = $"x{ci}'s cut would reach {w}";
                        if (JunctionDistance(map, c.under, sUs[ci]) < TrenchEndM) soft++;
                    }
                    if (hard != null || soft == members.Count)
                    {
                        string why = hard != null ? "a water span within the cut's reach (" + hard + ")"
                                                  : $"every crossing of the group within {TrenchEndM:0} m of a junction";
                        if (hard != null) humpWater++; else humpJunction++;
                        foreach (int ci in members)
                        {
                            TrenchWhy[ci] = (members.Count > 1 ? "group: " : "") + why;
                            if (oldWhy[ci] == null) { nowHumped++; TrenchRedecided.Add($"x{ci} '{map.edges[map.crossings[ci].over].name}' over '{map.edges[map.crossings[ci].under].name}' e{map.crossings[ci].under}: dug before, now at grade - {TrenchWhy[ci]}"); }
                        }
                        continue;
                    }
                    dugGroups++;
                    groupOfDug.Add((members, target));
                    int gid = trenchGroups.Count;
                    trenchGroups.Add(new List<int>(members));
                    var unitOf = new Dictionary<int, int>();
                    // one flat bottom per carriageway, between the group's crossings on it
                    var span = new Dictionary<int, Vector2>();
                    foreach (int ci in members)
                    {
                        var c = map.crossings[ci];
                        span[c.under] = span.TryGetValue(c.under, out var v) ? new Vector2(Mathf.Min(v.x, sUs[ci]), Mathf.Max(v.y, sUs[ci])) : new Vector2(sUs[ci], sUs[ci]);
                        if (!unitOf.TryGetValue(c.under, out int u)) { unitOf[c.under] = u = trenchUnits.Count; trenchUnits.Add((gid, new List<int>())); }
                        trenchUnits[u].crossings.Add(ci);
                        Dig(ci, map.edges[c.over], sOs[ci], map.edges[c.under]);
                        dugX++;
                        if (oldWhy[ci] != null) { nowDug++; TrenchRedecided.Add($"x{ci} '{map.edges[c.over].name}' over '{map.edges[c.under].name}' e{c.under}: at grade before ({oldWhy[ci]}), now dug with its group of {members.Count}"); }
                    }
                    // ...across the whole group: every member's crossing point
                    // projected onto each carriageway, so the two carriageways'
                    // bottoms span the same sections (a skewed street crosses
                    // them metres apart along the freeway, and two V's centred
                    // there stood 1-2 m apart across the median)
                    var keys = new List<int>(span.Keys);
                    foreach (int ue in keys)
                    {
                        var v = span[ue];
                        foreach (int ci in members)
                        {
                            ProjectOn(map.edges[ue], map.crossings[ci].at, out float sp);
                            v = new Vector2(Mathf.Min(v.x, sp), Mathf.Max(v.y, sp));
                        }
                        span[ue] = v;
                    }
                    foreach (var sv in span) pending.Add((sv.Key, sv.Value.x, sv.Value.y, target, ApproachGrade, 1, unitOf[sv.Key]));
                }

                // ---- (d) ramps beside a dug mainline under the same street
                for (int ri = 0; ri < NC; ri++)
                {
                    var rc = map.crossings[ri];
                    var ramp = map.edges[rc.under];
                    if (!rc.forced || !ramp.link || ramp.tunnel || TrenchedCrossings[ri]) continue;
                    var rover = map.edges[rc.over];
                    if (rover.cls >= 5 || rover.link) continue;
                    string rkey = RoadKey(rover);
                    ProjectOn(ramp, rc.at, out float sR);
                    ProjectOn(rover, rc.at, out float sRo);
                    for (int gi = 0; gi < groupOfDug.Count; gi++)
                    {
                        var (members, target) = groupOfDug[gi];
                        bool inside = false;
                        foreach (int ci in members)
                        {
                            var mc = map.crossings[ci];
                            if (RoadKey(map.edges[mc.over]) != rkey) break;
                            if ((mc.at - rc.at).sqrMagnitude > RampEnvelopeM * RampEnvelopeM) continue;
                            if (Mathf.Abs(Vector2.Dot(ramp.TangentAt(sR), map.edges[mc.under].TangentAt(sUs[ci]))) < RampEnvelopeCos) continue;
                            inside = true; break;
                        }
                        if (!inside) continue;
                        // dug for its own street (no deeper: the street over it
                        // can stand higher than over the mainline)
                        float rTarget = rover.YAt(sRo) - ClearanceM - DeckThick;
                        string w = CutReachesWater(map, spansOf, rc.under, sR, ramp.YAt(sR) - rTarget, RampCutGrade, false);
                        if (w != null) { TrenchWhy[ri] = "ramp beside a dug mainline, but its cut would reach " + w; break; }
                        pending.Add((rc.under, sR, sR, rTarget, RampCutGrade, 2, trenchUnits.Count));
                        trenchUnits.Add((gi, new List<int> { ri }));
                        trenchGroups[gi].Add(ri);
                        Dig(ri, rover, sRo, ramp);
                        rampX++;
                        TrenchRedecided.Add($"x{ri} '{rover.name}' over ramp e{rc.under}: humped the street before, now in the cut beside its mainline");
                        break;
                    }
                }
                TrenchDecisionReport = $"{cand.Count} street-over-freeway crossings in {groups.Count} groups: {dugGroups} groups dug ({dugX} crossings, one bottom each) and {rampX} ramps beside them in the cut; " +
                                       $"{groups.Count - dugGroups} groups at grade ({humpWater} for a water span within the cut's reach, {humpJunction} at a junction); " +
                                       $"redecided: {nowDug} dug that were at grade, {nowHumped} at grade that were dug, {rampX} ramps";
            }

            // Sink, and carry the trough through the mainline's nodes into
            // its neighbours until it has climbed back to their own profiles.
            // A ramp in the cut climbs out the same way along its own ramps,
            // through nodes where only ramps meet (a ramp's way split, its
            // own forks): West 5th Street's ramp e338 meets e8463 40 m past
            // the street, and stopped at that node it climbed 6 m in 40 m.
            var queue = new Queue<(int edge, float sLo, float sHi, float target, float grade, int carry, int group)>(pending);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 200000)
            {
                var (ei, sLo, sHi, target, grade, carry, group) = queue.Dequeue();
                var e = map.edges[ei];
                bool moved = false;
                for (int i = 0; i < e.stS.Length; i++)
                {
                    float want = target + Mathf.Max(0f, Mathf.Max(sLo - e.stS[i], e.stS[i] - sHi)) * grade;
                    long key = ((long)ei << 20) | (uint)i;
                    // what each group's cut wants here, against the profile
                    // before any cut (RelaxTrenches gives it back)
                    if (group >= 0 && trenchPre != null)
                    {
                        if (!trenchPre.TryGetValue(key, out float pre)) pre = e.stY[i];
                        if (want < pre - 0.01f)
                        {
                            trenchPre[key] = pre;
                            if (!trenchWant.TryGetValue(key, out var wl)) trenchWant[key] = wl = new List<(int, float)>(1);
                            wl.Add((group, want));
                        }
                    }
                    if (e.stY[i] > want + 0.01f) { e.stY[i] = want; moved = true; }
                }
                if (!moved || carry == 0) continue;
                // the ends: pin the node and continue into every other mainline
                // edge there (a ramp's cut: every other ramp)
                foreach (var (node, endS) in new[] { (e.a, 0f), (e.b, e.length) })
                {
                    float wantEnd = target + Mathf.Max(0f, Mathf.Max(sLo - endS, endS - sHi)) * grade;
                    float endY = e.a == node ? e.stY[0] : e.stY[e.stY.Length - 1];
                    if (endY > wantEnd + 0.01f) continue;      // the trough faded before this end
                    if (carry == 2)
                    {
                        // a ramp's: only where nothing but ramps meet
                        bool rampsOnly = true;
                        foreach (var oi in map.nodeEdges[node]) { var o = map.edges[oi]; if (!o.link || o.tunnel) { rampsOnly = false; break; } }
                        if (!rampsOnly) continue;
                    }
                    if (!float.IsNaN(pinnedNodeY[node]) && pinnedNodeY[node] <= endY + 0.01f) continue;
                    pinnedNodeY[node] = endY;
                    foreach (var oi in map.nodeEdges[node])
                    {
                        if (oi == ei) continue;
                        var o = map.edges[oi];
                        if (carry == 1 ? (o.cls < 5 || o.link || o.tunnel) : (!o.link || o.tunnel || o.a == o.b)) continue;
                        float oAt = o.a == node ? 0f : o.length;
                        float g = carry == 1 ? ApproachGrade : RampCutGrade;
                        if (!TrenchGroupsOn) { if (wet[oi]) continue; }
                        else if (spansOf[oi] != null)
                        {
                            // not into water: the cut on o reaches as far as
                            // o stands above the node's pinned height allows
                            float reach = Mathf.Max(0f, o.YAt(oAt) - endY) / g + TrenchWaterPadM;
                            bool hit = false;
                            foreach (var sp in spansOf[oi])
                            {
                                float from = oAt == 0f ? sp.x : o.length - sp.y;
                                if (from - TrenchWaterPadM < reach) { hit = true; break; }
                            }
                            if (hit) continue;
                        }
                        queue.Enqueue((oi, oAt, oAt, endY, g, carry, group));
                    }
                }
            }

            // Every edge meeting a pinned node takes the cut's height at that
            // end, now, once — the only lowering in the solve. A RAMP is cut
            // down along its own cone (8%, a ramp's grade) so a short one is
            // steep rather than stepped; a surface street that shares a node
            // with the cut blends over its usual length.
            foreach (var e in map.edges)
            {
                bool pa = !float.IsNaN(pinnedNodeY[e.a]), pb = !float.IsNaN(pinnedNodeY[e.b]);
                if (!pa && !pb) continue;
                if (pa) map.nodeY[e.a] = pinnedNodeY[e.a];
                if (pb) map.nodeY[e.b] = pinnedNodeY[e.b];
                if (e.link)
                {
                    foreach (var (node, fromA) in new[] { (e.a, true), (e.b, false) })
                    {
                        if (float.IsNaN(pinnedNodeY[node])) continue;
                        float cut = pinnedNodeY[node];
                        for (int i = 0; i < e.stS.Length; i++)
                        {
                            float dist = fromA ? e.stS[i] : e.length - e.stS[i];
                            float want = cut + dist * 0.08f;
                            if (e.stY[i] > want) e.stY[i] = want;
                        }
                    }
                }
                else BlendEndsToNodes(map, e);
            }
            // the cut as SinkTrenches leaves it, where a group's cut wants it
            // (RelaxTrenches gives back from here)
            if (trenchWant != null)
            {
                trenchPost = new Dictionary<long, float>(trenchWant.Count);
                foreach (var key in trenchWant.Keys) trenchPost[key] = map.edges[(int)(key >> 20)].stY[(int)(key & 0xFFFFF)];
                trenchGiven = new float[trenchUnits.Count];
            }
        }

        /// <summary>The dug groups (their crossings, ramps in the cut
        /// included), each lowered station's height before any cut, and what
        /// each group's cut wants there (plan B2; <see cref="RelaxTrenches"/>).</summary>
        static List<List<int>> trenchGroups;
        /// <summary>The cut's units: one per carriageway of a dug group, one
        /// per ramp in its cut (the group, the unit's crossings).</summary>
        static List<(int group, List<int> crossings)> trenchUnits;
        static Dictionary<long, float> trenchPre, trenchPost;
        static Dictionary<long, List<(int g, float want)>> trenchWant;
        /// <summary>What each unit has given back so far this solve.</summary>
        static float[] trenchGiven;
        /// <summary>A dug group whose street stands more than this over the
        /// clearance its cut was dug for gives the excess back.</summary>
        public const float TrenchRelaxMinM = 0.1f;
        /// <summary>How much more one carriageway of a group may give back
        /// than the one with the least spare (PROFILE judges the two at
        /// 0.5 m).</summary>
        public const float TrenchPairSlackM = 0.4f;
        /// <summary>Stations the last solve raised giving cuts back, and the
        /// groups that gave.</summary>
        public static int TrenchRelaxRaised { get; private set; }
        public static int TrenchRelaxGroups { get; private set; }

        /// <summary>
        /// ONE MOVE, NOT TWO (plan B2). A cut is dug for its street's height
        /// when the solve begins; the raise loop can still lift that street
        /// (a cone from a junction that met a raised ramp, a twin's hold) -
        /// West 5th Street rose 3 m on its ramp to I-77's southbound exit,
        /// and stood 7.6 m over a freeway dug 5.55 m under where it began.
        /// Then the freeway gives the excess back: every station a dug group
        /// lowered rises from where the cut left it by the spare clearance over
        /// the crossings of its own carriageway - at most
        /// <see cref="TrenchPairSlackM"/> more than the group's other
        /// carriageway gives, so the two stay together; a ramp in the cut by
        /// no more than its carriageways - never above where it was before the
        /// cut, and by no more than the least any other cut there has given.
        /// The cones carry the rise along the freeway. Raises only, so the
        /// loop it runs in still settles. Returns the stations raised.
        /// </summary>
        static int RelaxTrenches(CityMap map)
        {
            if (!TrenchGroupsOn || trenchGroups == null || trenchWant == null || trenchWant.Count == 0 || trenchPost == null) return 0;
            // each unit's spare clearance over its own crossings
            int U = trenchUnits.Count;
            var spare = new float[U];
            for (int u = 0; u < U; u++)
            {
                float E = float.MaxValue;
                foreach (int ci in trenchUnits[u].crossings)
                {
                    var c = map.crossings[ci];
                    var over = map.edges[c.over]; var under = map.edges[c.under];
                    ProjectOn(over, c.at, out float sO);
                    ProjectOn(under, c.at, out float sU);
                    E = Mathf.Min(E, over.YAt(sO) - DeckThick - ClearanceM - under.YAt(sU));
                }
                spare[u] = E;
            }
            // what each unit has given in all, after this round: what it has
            // given plus its spare now, but a carriageway no more than
            // TrenchPairSlackM over its group's least such total (PROFILE
            // judges the two carriageways at 0.5 m; bounding each round
            // instead let the slack add up round on round: Lakeview Road's
            // two carriageways of I-77 1.38 m apart in the B2 audit run), and a
            // ramp in the cut no more than its group's carriageways have (it
            // meets them at their gores: more, and its stub to the gore
            // climbs - 20% on I-485's at Shopton Road, emulated)
            var total = new float[U];
            var cwMin = new float[trenchGroups.Count];
            for (int g = 0; g < cwMin.Length; g++) cwMin[g] = float.MaxValue;
            bool IsRamp(int u) => map.edges[map.crossings[trenchUnits[u].crossings[0]].under].link;
            for (int u = 0; u < U; u++)
            {
                total[u] = trenchGiven[u] + (spare[u] < float.MaxValue ? Mathf.Max(0f, spare[u]) : 0f);
                if (!IsRamp(u)) cwMin[trenchUnits[u].group] = Mathf.Min(cwMin[trenchUnits[u].group], total[u]);
            }
            bool any = false;
            for (int u = 0; u < U; u++)
            {
                float cap = cwMin[trenchUnits[u].group];
                if (cap == float.MaxValue) cap = 0f;
                float want = IsRamp(u) ? Mathf.Min(total[u], cap) : Mathf.Min(total[u], cap + TrenchPairSlackM);
                if (want > trenchGiven[u] + TrenchRelaxMinM) { trenchGiven[u] = want; any = true; }
            }
            if (!any) return 0;
            int raised = 0;
            var gave = new HashSet<int>();
            foreach (var kv in trenchWant)
            {
                int ei = (int)(kv.Key >> 20), i = (int)(kv.Key & 0xFFFFF);
                var e = map.edges[ei];
                if (i >= e.stY.Length || e.SeatedAt(i)) continue;
                // the cut rises by the least any unit cutting here has given
                float G = float.MaxValue; int by = -1;
                foreach (var (u, _) in kv.Value) if (trenchGiven[u] < G) { G = trenchGiven[u]; by = u; }
                float floor = Mathf.Min(trenchPre[kv.Key], trenchPost[kv.Key] + G);
                if (e.stY[i] >= floor - 0.02f) continue;
                e.stY[i] = floor;
                raised++;
                if (by >= 0) gave.Add(trenchUnits[by].group);
            }
            TrenchRelaxRaised += raised;
            TrenchRelaxGroups = Mathf.Max(TrenchRelaxGroups, gave.Count);
            return raised;
        }

        /// <summary>A deck's reach past the end of its edge (a crossing near
        /// the over road's node): the stations of the road's continuation
        /// there (its best-aligned arm, within 60 degrees) within
        /// <paramref name="left"/> of the node are structure too.</summary>
        static void DeckThrough(CityMap map, CityMap.Edge from, int node, float left, int hops)
        {
            if (left <= 0f || hops > 3) return;
            Vector2 dirIn = from.b == node ? from.TangentAt(from.length) : -from.TangentAt(0f);
            CityMap.Edge best = null; float bestDot = 0.5f;
            foreach (int oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == from || o.a == o.b || o.stS == null) continue;
                Vector2 d = o.a == node ? o.TangentAt(0f) : -o.TangentAt(o.length);
                float dot = Vector2.Dot(d, dirIn) + (RoadKey(o) == RoadKey(from) ? 0.5f : 0f);
                if (dot > bestDot) { bestDot = dot; best = o; }
            }
            if (best == null) return;
            bool fromA = best.a == node;
            for (int i = 0; i < best.stS.Length; i++)
                if ((fromA ? best.stS[i] : best.length - best.stS[i]) <= left) best.stElev[i] = true;
            if (left > best.length) DeckThrough(map, best, fromA ? best.b : best.a, left - best.length, hops + 1);
        }

        /// <summary>A road's identity for the trench groups: its name, or the
        /// edge itself when it has none.</summary>
        static string RoadKey(CityMap.Edge e) => string.IsNullOrEmpty(e.name) ? "#" + e.index : e.name;

        /// <summary>Would a cut <paramref name="depth"/> deep at arc
        /// <paramref name="sAt"/> of edge <paramref name="ei"/>, climbing out at
        /// <paramref name="grade"/>, reach a water span (plus
        /// <see cref="TrenchWaterPadM"/>)? Along the edge, and with
        /// <paramref name="through"/> on into the freeway mainline beyond its
        /// nodes, as the trough is carried. The span found, or null.</summary>
        static string CutReachesWater(CityMap map, List<Vector2>[] spansOf, int ei, float sAt, float depth, float grade, bool through)
        {
            float reach = Mathf.Max(0f, depth) / grade + TrenchWaterPadM;
            var e = map.edges[ei];
            string hit = SpanIn(spansOf, ei, sAt - reach, sAt + reach);
            if (hit != null || !through) return hit;
            hit = WaterBeyond(map, spansOf, ei, e.b, reach - (e.length - sAt), 0);
            return hit ?? WaterBeyond(map, spansOf, ei, e.a, reach - sAt, 0);
        }

        static string SpanIn(List<Vector2>[] spansOf, int ei, float lo, float hi)
        {
            var l = spansOf[ei];
            if (l == null) return null;
            foreach (var sp in l)
                if (sp.y + TrenchWaterPadM >= lo && sp.x - TrenchWaterPadM <= hi)
                    return $"the water span on e{ei} at s {sp.x:0}-{sp.y:0}";
            return null;
        }

        static string WaterBeyond(CityMap map, List<Vector2>[] spansOf, int from, int node, float left, int hops)
        {
            if (left <= 0f || hops > 8) return null;
            foreach (int oi in map.nodeEdges[node])
            {
                if (oi == from) continue;
                var o = map.edges[oi];
                if (o.cls < 5 || o.link || o.tunnel || o.a == o.b) continue;
                bool fromA = o.a == node;
                string hit = fromA ? SpanIn(spansOf, oi, 0f, left) : SpanIn(spansOf, oi, o.length - left, o.length);
                if (hit != null) return hit;
                hit = WaterBeyond(map, spansOf, oi, fromA ? o.b : o.a, left - o.length, hops + 1);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>How far along the road from arc <paramref name="s"/> of
        /// edge <paramref name="ei"/> its nearest JUNCTION is: a node of three
        /// arms or more, or a dead end; a node of two (an OSM way split) is
        /// walked through. Searched to <see cref="TrenchEndM"/> (beyond that
        /// the exact distance does not matter).</summary>
        static float JunctionDistance(CityMap map, int ei, float s)
        {
            var e = map.edges[ei];
            return Mathf.Min(JunctionAlong(map, ei, e.b, e.length - s, 0), JunctionAlong(map, ei, e.a, s, 0));
        }

        static float JunctionAlong(CityMap map, int from, int node, float d, int hops)
        {
            if (d >= TrenchEndM || hops > 8) return d;
            var arms = map.nodeEdges[node];
            if (arms.Count != 2) return d;
            int oi = arms[0] == from ? arms[1] : arms[0];
            var o = map.edges[oi];
            if (o.a == o.b) return d;
            return JunctionAlong(map, oi, o.a == node ? o.b : o.a, d + o.length, hops + 1);
        }

        /// <summary>
        /// Which stations are a DECK (no ground under them) and which are an
        /// EMBANKMENT (the ground graded up to them). A tagged bridge and a
        /// water span were marked as they were solved; here every enforced
        /// crossing marks its OVER road as structure across the under road's
        /// whole corridor and blend, both sides (<see cref="DeckReach"/>), so
        /// the under road's verge is never pinned up into a bank by the road
        /// crossing it. Beyond that reach the approach is a fill. Anything
        /// still standing more than <see cref="ElevMarginM"/> above the
        /// terrain is taken for an untagged viaduct.
        /// </summary>
        static void MarkStructure(CityMap map)
        {
            for (int ci = 0; ci < map.crossings.Length; ci++)
            {
                if (crossingOn != null && ci < crossingOn.Length && !crossingOn[ci]) continue;
                var c = map.crossings[ci];
                var over = map.edges[c.over];
                var under = map.edges[c.under];
                ProjectOn(over, c.at, out float sO);
                ProjectOn(under, c.at, out float sU);
                float reach = DeckReach(over, under, sO, sU);
                for (int i = 0; i < over.stS.Length; i++)
                    if (Mathf.Abs(over.stS[i] - sO) <= reach) over.stElev[i] = true;
            }
            int margin = 0, marginTwin = 0;
            // (plan B1) which edges are a twin deck's or meet one at a node:
            // the margin stations there are counted apart, for the TWIN report
            bool[] nearTwin = null;
            if (map.deckUnionsOf != null)
            {
                nearTwin = new bool[map.edges.Length];
                var nodeTwin = new bool[map.nodes.Length];
                foreach (var e in map.edges)
                    if (map.deckUnionsOf[e.index] != null) { nodeTwin[e.a] = true; nodeTwin[e.b] = true; }
                foreach (var e in map.edges) nearTwin[e.index] = nodeTwin[e.a] || nodeTwin[e.b];
            }
            // a road over a creek pipe is an EMBANKMENT whatever it stands
            // over the grid (plan B2, owner Q8): never a deck by the margin -
            // nor is any road beside the pipe (a divided road's other
            // carriageway: its margin deck's cap dug 3.3 m under Highland
            // Creek Parkway's culvert, the first B2 run)
            var culvertWin = new Dictionary<int, List<Vector2>>();
            void Exempt(int ei, float a, float b)
            {
                if (!culvertWin.TryGetValue(ei, out var l)) culvertWin[ei] = l = new List<Vector2>(1);
                l.Add(new Vector2(a, b));
            }
            if (map.creekCulverts != null)
            {
                var near = new HashSet<int>();
                foreach (var x in map.creekCulverts)
                {
                    if (x.edge < 0 || x.edge >= map.edges.Length) continue;
                    Exempt(x.edge, x.s - x.halfAlong - StationStep, x.s + x.halfAlong + StationStep);
                    var at = map.edges[x.edge].PointAt(x.s);
                    near.Clear();
                    map.EdgeSegsInRect(at - Vector2.one * CulvertBesideM, at + Vector2.one * CulvertBesideM, near);
                    foreach (int packed in near)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        if (ei == x.edge) continue;
                        var o = map.edges[ei];
                        if (si + 1 >= o.pts.Length) continue;
                        ProjectOn(o, at, out float so);
                        if (Vector2.Distance(o.PointAt(so), at) > CulvertBesideM) continue;
                        Exempt(ei, so - x.halfAlong - StationStep, so + x.halfAlong + StationStep);
                    }
                }
            }
            foreach (var e in map.edges)
            {
                culvertWin.TryGetValue(e.index, out var win);
                for (int i = 0; i < e.stS.Length; i++)
                {
                    if (e.stElev[i]) continue;
                    if (win != null)
                    {
                        bool inWin = false;
                        foreach (var w in win) if (e.stS[i] >= w.x && e.stS[i] <= w.y) { inWin = true; break; }
                        if (inWin) continue;
                    }
                    var p = e.PointAt(e.stS[i]);
                    if (e.stY[i] > RoadBaseY(p.x, p.y) + ElevMarginM)
                    {
                        e.stElev[i] = true; margin++;
                        if (nearTwin != null && nearTwin[e.index]) marginTwin++;
                    }
                }
            }
            MarginStructureStations = margin;
            MarginStationsNearTwins = marginTwin;
        }

        /// <summary>
        /// The SAG at every station: how much the grade INCREASES there
        /// (grade out minus grade in, never negative), kept on the edge.
        /// <see cref="CityMap.Edge.SagAt"/> turns it into the extra sink the
        /// corridor pin needs at any arc position.
        ///
        /// The ground under a road is an 8 m lattice pinned from the road's
        /// own height, and a lattice is straight between its corners while
        /// the road bends at its stations. On a straight road the corners and
        /// the road points share one affine projection, so by Jensen the
        /// straight lattice runs ABOVE the bent road only where the profile is
        /// convex from below — a dip, the foot of an approach cone, a trench
        /// floor — and BELOW it at a crest. This measured the other way round
        /// until 2026-09-13: humps were sunk (up to 0.66 m of 20 cm kerb over
        /// a 6.5%/6.5% crest) and dips got nothing, and a replica of the
        /// lattice put grass 15 cm through the lanes of a 6.5% dip even with
        /// the old 0.20 m sink. With this allowance it holds exactly 0.10 m
        /// under the tarmac on every orientation and phase the replica tried
        /// (dip, cone foot, smooth sag, dip-then-crest, two dips).
        ///
        /// A node is a break too: the chord from one arm through the node to
        /// another sees the rise of both, so an edge END carries the worst
        /// pair it forms with any other arm there (a flat side street meeting
        /// a hill road is a sag for the path onto the hill; a straight grade
        /// through a node is not).
        /// </summary>
        static void MeasureSags(CityMap map)
        {
            foreach (var e in map.edges)
            {
                int n = e.stY.Length;
                e.stSag = new float[n];
                for (int i = 1; i < n - 1; i++)
                {
                    float d1 = e.stS[i] - e.stS[i - 1], d2 = e.stS[i + 1] - e.stS[i];
                    if (d1 < 0.5f || d2 < 0.5f) continue;
                    float gIn = (e.stY[i] - e.stY[i - 1]) / d1;
                    float gOut = (e.stY[i + 1] - e.stY[i]) / d2;
                    e.stSag[i] = Mathf.Max(0f, gOut - gIn);
                }
            }
            var rises = new List<(CityMap.Edge e, bool atA, float rise)>(8);
            for (int n = 0; n < map.nodes.Length; n++)
            {
                rises.Clear();
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    int last = e.stS.Length - 1;
                    if (last < 1 || e.a == e.b) continue;
                    bool fromA = e.a == n;
                    float ds = fromA ? e.stS[1] - e.stS[0] : e.stS[last] - e.stS[last - 1];
                    if (ds < 0.5f) continue;
                    float rise = fromA ? (e.stY[1] - e.stY[0]) / ds : (e.stY[last - 1] - e.stY[last]) / ds;
                    rises.Add((e, fromA, rise));
                }
                for (int i = 0; i < rises.Count; i++)
                {
                    float worst = 0f;
                    for (int j = 0; j < rises.Count; j++)
                        if (j != i) worst = Mathf.Max(worst, rises[i].rise + rises[j].rise);
                    var (e, atA, _) = rises[i];
                    int st = atA ? 0 : e.stSag.Length - 1;
                    e.stSag[st] = Mathf.Max(e.stSag[st], worst);
                }
            }
        }

        // ------------------------------------------------------------------
        //  Ramps beside their mainlines
        // ------------------------------------------------------------------
        /// <summary>
        /// THE RAMP IS THE MAINLINE UNTIL IT LEAVES IT.
        ///
        /// Reported as "entrance ramps going up through the centre of a road
        /// like a staircase in the centre of a house". OSM joins a ramp to its
        /// carriageway at the END of the taper, so for its last hundred metres
        /// or so a ramp's ribbon lies inside the mainline's; CityMeshes clips
        /// it against the host there (see EmitBranch) — but only while the two
        /// are within 0.6 m in height, and nothing here ever made them so.
        /// Every edge was solved on its own profile and they met at the NODE,
        /// so a ramp still climbing toward its overpass, or a mainline dug
        /// into a trench under a suburban bridge, left the ramp standing up to
        /// five metres out of the lanes it was drawn inside: the clip let go,
        /// and the ramp's sliver climbed through the air like a stair
        /// stringer. The census counted 5.7 km of it city-wide, 2.3 km of it
        /// more than a metre off.
        ///
        /// So a branch station inside its host's gore (CityMeshes.BranchSeats,
        /// the tile's own walk in plan) is SEATED: it takes the host's height
        /// under it, every pass that raises roads leaves it alone, and it is
        /// put back on the host after every pass that moves hosts. Beyond the
        /// last seated station the branch CLIMBS OUT at its class's grade (or
        /// the grade its far end demands, if steeper), which is where a real
        /// ramp starts to separate vertically: after it has separated in plan.
        /// </summary>
        static readonly List<(int edge, int st, int host, float hostS)> seated = new List<(int, int, int, float)>(4096);
        /// <summary>Stations of a street BRANCH (not a ramp) inside its host's
        /// gore zone: the vertical curves pull them onto the host's height
        /// (see VerticalCurves), softly - never the host onto them.</summary>
        static readonly List<(int edge, int st, int host, float hostS)> streetSeats = new List<(int, int, int, float)>(2048);
        /// <summary>Per street seat: the ribbon that station shapes runs into
        /// its host's lanes there (CityMeshes.Seat.overlaps) - the seats the
        /// vertical curves PIN, not just pull.</summary>
        static readonly List<bool> streetSeatInto = new List<bool>(2048);
        /// <summary>Each seated run's last station and which way leads away
        /// from the host (+1 toward the edge's b end).</summary>
        static readonly List<(int edge, int boundary, int dir)> climbs = new List<(int, int, int)>(1024);

        /// <summary>Seated stations, per solve: pure plan geometry.</summary>
        public static int SeatedStationCount => seated.Count;
        /// <summary>What finding the seats cost this solve, for the load log.</summary>
        public static long SeatPrepMs { get; private set; }

        /// <summary>For the audit: which stations of an edge are seated and on
        /// what, and where it climbs out.</summary>
        public static string DescribeSeats(int edge)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (ei, st, h, hs) in seated)
                if (ei == edge && h >= 0) sb.Append($" st{st}->e{h}@{hs:0}");
            foreach (var (ei, b, d) in climbs)
                if (ei == edge) sb.Append($" climb from st{b} dir{d}");
            return sb.Length == 0 ? " (no seats)" : sb.ToString();
        }

        /// <summary>The steepest a lane seated on two hosts may climb between
        /// its two seated runs (see PrepareSeats; the grade audit's ceiling is
        /// 16%, and the raises after the seats can still move a host).</summary>
        const float SeatStepGrade = 0.10f;
        /// <summary>The step between two such runs that starts the split: a
        /// grade this steep is near the audit's ceiling; anything gentler was
        /// always left as it was.</summary>
        const float SeatStepTrigger = 0.13f;
        /// <summary>Stations given up by runs meeting on two hosts, last solve.</summary>
        public static int SeatStepsSplit { get; private set; }

        static void PrepareSeats(CityMap map)
        {
            seated.Clear();
            SeatStationsFreed = 0;
            climbs.Clear();
            seatRuns.Clear();
            seatPairs.Clear();
            SeatStepsSplit = 0;
            foreach (var e in map.edges) e.stSeat = null;
            var seatTrims = CityMeshes.ComputeTrims(map);
            var seats = CityMeshes.BranchSeats(map, seatTrims);
            // the other branches' zones, for the vertical curves' SOFT seats
            streetSeats.Clear();
            streetSeatInto.Clear();
            if (VerticalCurvesOn)
                foreach (var seat in CityMeshes.BranchSeats(map, seatTrims, streetsOnly: true))
                    foreach (var (ei, s0, s1, dir) in seat.pieces)
                    {
                        var e = map.edges[ei];
                        for (int i = 1; i < e.stS.Length - 1; i++)
                        {
                            if (e.stS[i] < s0 - 0.01f || e.stS[i] > s1 + 0.01f) continue;
                            int h = seat.HostAt(e.PointAt(e.stS[i]), out float hs);
                            if (h < 0 || h == ei) continue;
                            streetSeats.Add((ei, i, h, hs));
                            // does the ribbon this station shapes (to its
                            // neighbours either side) run INTO the host's lanes?
                            bool into = false;
                            foreach (var (oe, o0, o1) in seat.overlaps)
                                if (oe == ei && o1 >= e.stS[i - 1] && o0 <= e.stS[i + 1]) { into = true; break; }
                            streetSeatInto.Add(into);
                        }
                    }
            // Each run's stations inside the gore, first, per edge, so the
            // runs of one edge can be judged against each other.
            var runs = new List<(CityMeshes.Seat seat, int ei, int first, int last, int dir)>();
            var runsOf = new Dictionary<int, List<int>>();
            foreach (var seat in seats)
                foreach (var (ei, s0, s1, dir) in seat.pieces)
                {
                    var e = map.edges[ei];
                    int n = e.stS.Length;
                    int first = -1, last = -1;
                    for (int i = 0; i < n; i++)
                        if (e.stS[i] >= s0 - 0.01f && e.stS[i] <= s1 + 0.01f) { if (first < 0) first = i; last = i; }
                    if (first < 0) continue;
                    if (!runsOf.TryGetValue(ei, out var l)) runsOf[ei] = l = new List<int>(2);
                    l.Add(runs.Count);
                    runs.Add((seat, ei, first, last, dir));
                }
            // Each run's range as it will be seated: ONE station past its far
            // end (the height between the last seated station and the next
            // is a lerp, and a climb starting inside the run lifts its last
            // few metres off the host before the tile stops clipping them).
            int R = runs.Count;
            var rFirst = new int[R];
            var rLast = new int[R];
            for (int r = 0; r < R; r++)
            {
                var (seat, ei, first, last, dir) = runs[r];
                int n = map.edges[ei].stS.Length;
                if (dir > 0 && last < n - 1) last++;
                if (dir < 0 && first > 0) first--;
                rFirst[r] = first; rLast[r] = last;
            }
            // Two runs of one edge walked in from its opposite ends: where
            // they overlap, the run seated first keeps the shared stations
            // (the seating below skips a station already seated), so the
            // other's range starts, or ends, where it really does. Each such
            // pair is watched by SeatBranches (see SplitTwoHosts).
            var pairsOf = new List<(int ei, int a, int b)>();
            foreach (var kv in runsOf)
            {
                var list = kv.Value;
                if (list.Count < 2) continue;
                foreach (int a in list)
                    foreach (int b in list)
                    {
                        if (runs[a].dir <= 0 || runs[b].dir >= 0 || runs[a].seat == runs[b].seat) continue;
                        if (rFirst[a] > rLast[a] || rFirst[b] > rLast[b]) continue;
                        if (rFirst[b] <= rLast[a])
                        {
                            if (a < b) rFirst[b] = rLast[a] + 1; else rLast[a] = rFirst[b] - 1;
                            if (rFirst[a] > rLast[a] || rFirst[b] > rLast[b]) continue;
                        }
                        pairsOf.Add((kv.Key, a, b));
                    }
            }
            var runSlot = new int[R];
            for (int r = 0; r < R; r++)
            {
                runSlot[r] = -1;
                var seat = runs[r].seat;
                int ei = runs[r].ei, first = rFirst[r], last = rLast[r], dir = runs[r].dir;
                if (first > last) continue;
                var e = map.edges[ei];
                int n = e.stS.Length;
                var mine = new List<int>();
                for (int i = first; i <= last; i++)
                {
                    int h = seat.HostAt(e.PointAt(e.stS[i]), out float hs);
                    if (h < 0 || h == ei) continue;
                    e.stSeat ??= new bool[n];
                    if (e.stSeat[i]) continue;      // already seated from its other end
                    e.stSeat[i] = true;
                    mine.Add(seated.Count);
                    seated.Add((ei, i, h, hs));
                }
                if (mine.Count == 0) continue;
                int boundary = dir > 0 ? last : first;
                int climb = -1;
                if ((dir > 0 && boundary < n - 1) || (dir < 0 && boundary > 0))
                {
                    climb = climbs.Count;
                    climbs.Add((ei, boundary, dir));
                }
                runSlot[r] = seatRuns.Count;
                seatRuns.Add((mine, climb, dir));
            }
            foreach (var (ei, a, b) in pairsOf)
                if (runSlot[a] >= 0 && runSlot[b] >= 0) seatPairs.Add((ei, runSlot[a], runSlot[b]));
        }

        /// <summary>The seated runs that meet another from their edge's other
        /// end: each run's indices into <see cref="seated"/> (in station
        /// order), its entry in <see cref="climbs"/> (or -1) and its
        /// direction; and the pairs, as (edge, run from the a end, run from
        /// the b end).</summary>
        static readonly List<(List<int> seats, int climb, int dir)> seatRuns = new List<(List<int>, int, int)>(1024);
        static readonly List<(int edge, int a, int b)> seatPairs = new List<(int, int, int)>(64);

        /// <summary>
        /// TWO HOSTS AT ONCE (WP-04). A short slip lane beside one road at its
        /// a end and another at its b end is seated on both, and where the two
        /// runs meet it steps from one host's height to the other's between
        /// two stations. On the old filtered ground the roads of a corner
        /// agreed; on the real 3DEP ground they stand 1.3-1.8 m apart, and the
        /// step was a 16-19% grade in 7-9 m (e271 between a ramp and Little
        /// Rock Road; e9017, W. T. Harris Boulevard's ramp). So each time the
        /// hosts have moved, a step steeper than <see cref="SeatStepTrigger"/>
        /// is split: the longer run gives up its station at the meeting point,
        /// alternately, until the lane can climb from one host to the other at
        /// <see cref="SeatStepGrade"/> over the stations freed (its climbs run
        /// from the new ends). Stations are only ever freed, never re-seated,
        /// so the solve's passes still converge.
        /// </summary>
        static void SplitTwoHosts(CityMap map)
        {
            foreach (var (ei, a, b) in seatPairs)
            {
                var e = map.edges[ei];
                var A = seatRuns[a]; var B = seatRuns[b];
                bool split = false;
                for (int guard = 0; guard < 64 && A.seats.Count > 0 && B.seats.Count > 0; guard++)
                {
                    int ia = seated[A.seats[A.seats.Count - 1]].st, ib = seated[B.seats[0]].st;
                    if (ib <= ia) break;
                    float dy = Mathf.Abs(e.stY[ia] - e.stY[ib]);
                    float span = Mathf.Max(0.5f, e.stS[ib] - e.stS[ia]);
                    if (dy <= (split ? SeatStepGrade : SeatStepTrigger) * span) break;
                    split = true;
                    bool fromA = A.seats.Count >= B.seats.Count;
                    var run = fromA ? A : B;
                    int k = fromA ? run.seats.Count - 1 : 0;
                    var t = seated[run.seats[k]];
                    seated[run.seats[k]] = (t.edge, t.st, -1, 0f);
                    e.stSeat[t.st] = false;
                    run.seats.RemoveAt(k);
                    if (run.climb >= 0)
                        climbs[run.climb] = run.seats.Count == 0 ? (ei, -1, run.dir)
                            : (ei, seated[run.seats[fromA ? run.seats.Count - 1 : 0]].st, run.dir);
                    SeatStepsSplit++;
                }
            }
        }

        /// <summary>Put every seated station back on its host, meet the
        /// nodes the seated runs end at, and grade each climb out. Returns
        /// how many far ends the climbs had to move, for the loop that
        /// reconciles nodes after it.</summary>
        static int SeatBranches(CityMap map)
        {
            if (seated.Count == 0) return 0;
            foreach (var (ei, st, h, hs) in seated)
                if (h >= 0) map.edges[ei].stY[st] = map.edges[h].YAt(hs);
            SplitTwoHosts(map);
            foreach (var (ei, st, h, hs) in seated)
            {
                if (h < 0) continue;   // freed (SplitTwoHosts)
                var e = map.edges[ei];
                if (st == 0) SeatNode(map, e.a, e.stY[0]);
                else if (st == e.stY.Length - 1) SeatNode(map, e.b, e.stY[st]);
            }
            int moves = 0;
            foreach (var (ei, boundary, dir) in climbs)
                if (boundary >= 0) moves += ClimbOut(map, map.edges[ei], boundary, dir, farEnds: true);
            return moves;
        }

        /// <summary>A node a seated run ends at takes the run's height, and so
        /// does every other RAMP end there (with its own climb out). A through
        /// road's end is never moved from here — at a merge node those ends
        /// are the host the run was seated on — and a higher one keeps the
        /// node up with it.</summary>
        static void SeatNode(CityMap map, int node, float y)
        {
            float ny = y;
            foreach (var oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o.a == o.b) continue;
                bool atA = o.a == node;
                int st = atA ? 0 : o.stY.Length - 1;
                if (o.SeatedAt(st)) continue;
                if (!o.link) { ny = Mathf.Max(ny, o.stY[st]); continue; }
                if (Mathf.Abs(o.stY[st] - y) < 0.01f) continue;
                o.stY[st] = y;
                ClimbOut(map, o, st, atA ? 1 : -1, farEnds: false);
            }
            map.nodeY[node] = ny;
        }

        /// <summary>
        /// From a fixed station, away along the edge: no steeper than the
        /// class allows, unless the next fixed height (a seated station or the
        /// far end) needs steeper, and then exactly as steep as that.
        ///
        /// A long ramp beside its mainline is seated to within a station of its
        /// far end, and that end's node was solved without it: a collector
        /// road on the I-277 bridge for 70 of its 81 m had 10 m left to drop
        /// 4.6 m to a node at ground level (50%), and one lying in the 277 cut
        /// for all 261 m had a junction 2.7 m above the cut at its far end. So
        /// with <paramref name="farEnds"/>, a far END out of reach of
        /// <see cref="ClimbReach"/> is moved into it: RAISED when it is too low
        /// (the node and its other arms follow through the solver's own snap
        /// and cones, which is why the caller counts the move), and LOWERED
        /// when it is too high but only where nothing but ground put it there
        /// (<see cref="LowerFarNode"/>). Anything else keeps the steep climb,
        /// which is what the geometry honestly is.
        /// </summary>
        static int ClimbOut(CityMap map, CityMap.Edge e, int from, int dir, bool farEnds)
        {
            int n = e.stY.Length;
            int far = from + dir;
            while (far > 0 && far < n - 1 && !e.SeatedAt(far)) far += dir;
            far = Mathf.Clamp(far, 0, n - 1);
            if (far == from) return 0;
            float yZ = e.stY[from];
            float span = Mathf.Abs(e.stS[far] - e.stS[from]);
            if (span < 0.5f) return 0;
            int moves = 0;
            if (farEnds && (far == 0 || far == n - 1) && !e.SeatedAt(far) && e.link)
            {
                float reach = ClimbReach(e) * span;
                int farNode = far == 0 ? e.a : e.b;
                if (e.stY[far] < yZ - reach - 0.02f)
                {
                    e.stY[far] = yZ - reach;
                    moves++;
                }
                else if (e.stY[far] > yZ + reach + 0.02f && LowerFarNode(map, farNode, e, yZ + reach))
                    moves++;
            }
            float g = Mathf.Max(MaxGrade(e), Mathf.Abs(e.stY[far] - yZ) / span * 1.05f);
            for (int i = from + dir; i != far; i += dir)
            {
                float d = Mathf.Abs(e.stS[i] - e.stS[from]);
                e.stY[i] = Mathf.Clamp(e.stY[i], yZ - g * d, yZ + g * d);
            }
            return moves;
        }

        /// <summary>The host a seated station sits on, or -1. A scan: only the
        /// rare refused-or-not lowering asks.</summary>
        static int SeatHostOf(int edge, int station)
        {
            foreach (var (ei, st, h, _) in seated)
                if (ei == edge && st == station) return h;
            return -1;
        }

        /// <summary>The steepest a ramp may leave its host at before its far
        /// end is moved instead: one and a half times its class's grade (12%
        /// on a link) — the relax pass's own ceiling.</summary>
        static float ClimbReach(CityMap.Edge e) => MaxGrade(e) * 1.5f;

        /// <summary>
        /// Lower a ramp junction to <paramref name="y"/>, with every arm, IF
        /// nothing but the ground put it where it is: at its own terrain (not
        /// raised for a crossing or an approach), not pinned by a trench, every
        /// arm a ramp (a street would be dragged into a dip), no arm on
        /// structure or seated at that end. Every other arm eases down to it at
        /// its own grade. False when refused.
        /// </summary>
        static bool LowerFarNode(CityMap map, int node, CityMap.Edge from, float y)
        {
            if (map.nodeY[node] > RoadBaseY(map.nodes[node].x, map.nodes[node].y) + 0.5f) return false;
            if (pinnedNodeY != null && !float.IsNaN(pinnedNodeY[node])) return false;
            foreach (var oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (!o.link || o.a == o.b) return false;
                int st = o.a == node ? 0 : o.stY.Length - 1;
                if (o.stElev[st]) return false;
                // Seated on some other road, that end is not ours to move; seated
                // on the very ramp being lowered, it follows it anyway.
                if (o.SeatedAt(st) && SeatHostOf(oi, st) != from.index) return false;
            }
            map.nodeY[node] = y;
            foreach (var oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                bool atA = o.a == node;
                int st = atA ? 0 : o.stY.Length - 1;
                o.stY[st] = y;
                if (o != from) ClimbOut(map, o, st, atA ? 1 : -1, farEnds: false);
            }
            return true;
        }

        /// <summary>A seated station stands on whatever its host stands on: a
        /// ramp inside a bridge deck's pavement is on the deck.</summary>
        static void InheritSeatStructure(CityMap map)
        {
            foreach (var (ei, st, h, hs) in seated)
                if (h >= 0 && map.edges[h].ElevatedAt(hs)) map.edges[ei].stElev[st] = true;
        }

        /// <summary>How far along the over road, either side of the crossing
        /// point, its deck must run to clear the under road's graded
        /// corridor: the under road's corridor and most of its blend plus
        /// the over road's own corridor, stretched for an oblique crossing.</summary>
        public static float DeckReach(CityMap.Edge over, CityMap.Edge under, float sO, float sU)
        {
            var tO = over.TangentAt(sO);
            var tU = under.TangentAt(sU);
            float cos = Vector2.Dot(tO, tU);
            float sin = Mathf.Max(0.4f, Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos)));
            float across = under.CorridorHalf + CorridorBlend * 0.85f + over.CorridorHalf;
            return across / sin;
        }

        /// <summary>A tagged bridge holds at least the straight line between
        /// its two ends: a deck does not sag into the valley it was built to
        /// cross. Raises only, like everything else here.</summary>
        static void HoldBridges(CityMap map)
        {
            foreach (var e in map.edges)
            {
                if (!e.bridge || e.stY.Length < 3) continue;
                float y0 = e.stY[0], y1 = e.stY[e.stY.Length - 1];
                for (int i = 1; i < e.stY.Length - 1; i++)
                {
                    if (e.SeatedAt(i)) continue;
                    float hold = Mathf.Lerp(y0, y1, e.stS[i] / e.length);
                    if (e.stY[i] < hold) e.stY[i] = hold;
                }
            }
        }

        /// <summary>Every grade separation lifts its OVER edge clear of the
        /// under road's CURRENT height. Lowest stack first, so an edge that is
        /// over one road and under a third reads the raised height when the
        /// higher deck solves. Idempotent and monotonic: safe to run again.</summary>
        static bool[] crossingOn;
        static float[] crossingTarget;

        static void RaiseAllCrossings(CityMap map, bool fresh = false)
        {
            crossingOn ??= PruneMutualCrossings(map);
            EnforcedCrossings = crossingOn;
            if (crossingTarget == null || crossingTarget.Length != map.crossings.Length)
            {
                crossingTarget = new float[map.crossings.Length];
                for (int i = 0; i < crossingTarget.Length; i++) crossingTarget[i] = float.NaN;
            }
            // A SEATED station is its host's (RaiseHump skips it), so a ramp
            // seated beside its mainline takes the mainline's height where it
            // crosses something. On a skewed pair crossing the same road the
            // mainline's hump peaks at its own crossing, metres along from the
            // ramp's: the I-77 connector over I-85 cleared it by 4.35 m (the
            // WP-10 review's audit, once PARA had moved it 4 m clear of I-77).
            // Where a seated branch would clear what it crosses by less than
            // the audit's margin, the stations its hump needs higher than the
            // host are FREED and take the hump: the branch rises off its host
            // there (a separate deck), and nowhere else changes. (Raising the
            // host instead stood I-77 0.8 m above its own other carriageway.)
            Dictionary<int, List<int>> seatsOf = null;
            if (seated.Count > 0)
            {
                seatsOf = new Dictionary<int, List<int>>();
                for (int k = 0; k < seated.Count; k++)
                {
                    if (seated[k].host < 0) continue;
                    if (!seatsOf.TryGetValue(seated[k].edge, out var l)) seatsOf[seated[k].edge] = l = new List<int>();
                    l.Add(k);
                }
            }
            var order = new List<int>();
            for (int i = 0; i < map.crossings.Length; i++) if (crossingOn[i]) order.Add(i);
            order.Sort((p, q) => map.edges[map.crossings[p].over].layer
                .CompareTo(map.edges[map.crossings[q].over].layer));
            foreach (var ci in order)
            {
                var c = map.crossings[ci];
                var over = map.edges[c.over];
                var under = map.edges[c.under];
                ProjectOn(over, c.at, out float sOver);
                // The target LATCHES on first computation. A ramp that both
                // MEETS a street at a node and passes under it further along
                // otherwise feeds back: crossing raises street, node lifts
                // ramp tip, next pass reads the lifted ramp and raises the
                // street again — one such cluster once ratcheted 24 m into
                // the sky. Stacks still solve: within the first pass, lower
                // decks are raised before higher ones read them.
                if (fresh || float.IsNaN(crossingTarget[ci]))
                {
                    ProjectOn(under, c.at, out float sUnder);
                    float t = under.YAt(sUnder) + ClearanceM + DeckThick;
                    crossingTarget[ci] = fresh && !float.IsNaN(crossingTarget[ci])
                        ? Mathf.Max(crossingTarget[ci], t) : t;
                }
                RaiseHump(over, sOver, crossingTarget[ci]);
                // (in the FRESH passes only: by then every host has taken its
                // own humps, so a branch is freed only where it really falls short)
                if (fresh && seatsOf != null && seatsOf.TryGetValue(c.over, out var seats))
                {
                    ProjectOn(under, c.at, out float sU);
                    if (over.YAt(sOver) - DeckThick - under.YAt(sU) < ClearanceM - SeatFreeMarginM)
                    {
                        int freed = 0;
                        foreach (var k in seats)
                        {
                            var (ei, st, h, hs) = seated[k];
                            if (h < 0) continue;
                            float want = crossingTarget[ci] - ApproachDrop(Mathf.Abs(over.stS[st] - sOver), CrestR(over));
                            if (map.edges[h].YAt(hs) >= want) continue;
                            seated[k] = (ei, st, -1, hs);
                            over.stSeat[st] = false;
                            freed++;
                        }
                        if (freed > 0) { SeatStationsFreed += freed; RaiseHump(over, sOver, crossingTarget[ci]); }
                    }
                }
            }
        }

        /// <summary>A seated branch clearing what it crosses by less than
        /// ClearanceM minus this is freed there (see RaiseAllCrossings): just
        /// inside the audit's 0.6 m margin, so only a branch that would fail
        /// it moves. (At 0.3 m it freed branches clearing 4.4-4.7 m, which
        /// passed, and opened two deck gaps on the Uptown Loop's tiles.)</summary>
        const float SeatFreeMarginM = 0.55f;
        /// <summary>Seated stations freed to take a crossing's hump, last solve.</summary>
        public static int SeatStationsFreed { get; private set; }

        /// <summary>
        /// Two crossings of the SAME two edges in OPPOSITE directions, close
        /// enough that their approach humps overlap, cannot both hold at these
        /// grades — each iteration raised one past the other and the pair
        /// ratcheted 20 m into the sky (braided interchange ramps are where
        /// this lives). Keep the one whose over-edge carries the higher stack
        /// (layer, then class, then length).
        /// </summary>
        static bool[] PruneMutualCrossings(CityMap map)
        {
            var on = new bool[map.crossings.Length];
            for (int i = 0; i < on.Length; i++) on[i] = true;
            var byPair = new Dictionary<long, List<int>>();
            for (int i = 0; i < map.crossings.Length; i++)
            {
                var c = map.crossings[i];
                long a = Mathf.Min(c.over, c.under), b = Mathf.Max(c.over, c.under);
                long key = (a << 20) | b;
                if (!byPair.TryGetValue(key, out var list)) byPair[key] = list = new List<int>(2);
                list.Add(i);
            }
            int pruned = 0;
            foreach (var list in byPair.Values)
            {
                if (list.Count < 2) continue;
                for (int m = 0; m < list.Count; m++)
                    for (int n = m + 1; n < list.Count; n++)
                    {
                        var cm = map.crossings[list[m]];
                        var cn = map.crossings[list[n]];
                        if (cm.over == cn.over) continue;             // same direction: a real double-cross
                        if (!on[list[m]] || !on[list[n]]) continue;
                        if (Vector2.Distance(cm.at, cn.at) > 500f) continue;
                        var em = map.edges[cm.over];
                        var en = map.edges[cn.over];
                        bool mWins = em.layer != en.layer ? em.layer > en.layer
                                   : em.cls != en.cls ? em.cls > en.cls
                                   : em.length >= en.length;
                        on[mWins ? list[n] : list[m]] = false;
                        pruned++;
                    }
            }
            if (pruned > 0) Debug.Log($"[City] pruned {pruned} mutually-conflicting grade separations");
            return on;
        }

        /// <summary>Water spans hold a straight line between their approach
        /// heights and are always structure.</summary>
        static void HoldWaterSpans(CityMap map)
        {
            for (int k = 0; k < map.wspans.Length; k++)
            {
                var ws = map.wspans[k];
                var e = map.edges[ws.edge];
                float s0 = Mathf.Clamp(ws.s0, 0f, e.length);
                float s1 = Mathf.Clamp(ws.s1, 0f, e.length);
                if (s1 - s0 < 2f) continue;
                // THE DECK CLEARS ITS WATER (WP-04b): lifted, never lowered,
                // with its approaches at the crossings' grade
                float wy = SpanWaterY(k);
                if (!float.IsNaN(wy))
                {
                    float want = wy + WaterDeckClearM + DeckThick;
                    float low = float.MaxValue;
                    for (int i = 0; i < e.stS.Length; i++)
                        if (e.stS[i] >= s0 && e.stS[i] <= s1 && !e.SeatedAt(i)) low = Mathf.Min(low, e.stY[i]);
                    if (low < want)
                    {
                        RaiseSpan(e, s0, s1, want);
                        if (!spanLifted[k]) { spanLifted[k] = true; SpansLiftedForWater++; }
                        SpanWaterLiftMax = Mathf.Max(SpanWaterLiftMax, want - low);
                    }
                }
                float y0 = e.YAt(s0), y1 = e.YAt(s1);
                bool any = false;
                for (int i = 0; i < e.stS.Length; i++)
                {
                    if (e.stS[i] < s0 || e.stS[i] > s1 || e.SeatedAt(i)) continue;
                    float t = (e.stS[i] - s0) / (s1 - s0);
                    float hold = Mathf.Lerp(y0, y1, t);
                    if (e.stY[i] < hold) e.stY[i] = hold;
                    e.stElev[i] = true;
                    any = true;
                }
                // A span narrower than the station step can fall between two
                // stations (the surveyed creeks are narrower than RG2's
                // traced ones): the two stations round its middle carry it.
                if (!any)
                {
                    float mid = (s0 + s1) * 0.5f;
                    int lo = 0;
                    while (lo + 2 < e.stS.Length && e.stS[lo + 1] <= mid) lo++;
                    for (int i = lo; i <= lo + 1 && i < e.stS.Length; i++)
                        if (!e.SeatedAt(i)) e.stElev[i] = true;
                }
            }
        }

        /// <summary>Road crossings over a creek pipe the last solve raised to
        /// the pipe's cover, and the most any rose (plan B2, owner Q8).</summary>
        /// <summary>Two carriageways of one road over one creek pipe, their
        /// crossings within <see cref="CulvertTwinM"/>, stand within
        /// <see cref="CulvertTwinDyM"/> of each other there (the lower is
        /// raised: HoldCulverts).</summary>
        public const float CulvertTwinM = 60f, CulvertTwinDyM = 1f;
        /// <summary>Roads within this of a creek pipe's crossing (a divided
        /// road's other carriageway) are embankments there too.</summary>
        public const float CulvertBesideM = 30f;
        public static int CulvertCrossingsHeld { get; private set; }
        public static float CulvertHoldMaxM { get; private set; }
        static readonly HashSet<int> culvertHeld = new HashSet<int>();

        /// <summary>
        /// A ROAD OVER A CREEK OSM PIPES UNDER IT (plan B2, owner Q8;
        /// <see cref="CityMap.creekCulverts"/>) keeps the pipe's cover: at
        /// least <see cref="CityCulverts.MinFillM"/> over the creek's carved
        /// floor across the creek's flat floor (the crossing's halfAlong),
        /// falling away like a crossing's hump. Raises only, and the stations
        /// stay on the GROUND: an embankment over a pipe, not a deck. It
        /// replaces the water span's hold (the deck's soffit a metre over the
        /// water, 0.75 m higher than this).
        /// </summary>
        static void HoldCulverts(CityMap map)
        {
            if (map.creekCulverts == null || !HasDem) return;
            for (int ci = 0; ci < map.creekCulverts.Length; ci++)
            {
                var x = map.creekCulverts[ci];
                if (x.edge < 0 || x.edge >= map.edges.Length) continue;
                var e = map.edges[x.edge];
                float want = CulvertFloorY(x) + CityCulverts.MinFillM;
                // ONE FILL: the other carriageway of the same road over the
                // same pipe stands within a metre of this one (the land between
                // is graded to the lower; three metres apart, Highland Creek
                // Parkway's higher carriageway stood on nothing - the B2 run).
                // Only below secondary: a secondary or bigger road's two
                // carriageways already read one ground line (PairedRoadBaseY);
                // five pairs city-wide, West 5th Street's over Irwin Creek one
                var at = e.PointAt(x.s);
                for (int cj = 0; cj < map.creekCulverts.Length && e.cls < 2; cj++)
                {
                    var o = map.creekCulverts[cj];
                    if (cj == ci || o.water != x.water || o.edge == x.edge || o.edge < 0 || o.edge >= map.edges.Length) continue;
                    var oe = map.edges[o.edge];
                    if (string.IsNullOrEmpty(e.name) || oe.name != e.name) continue;
                    if ((oe.PointAt(o.s) - at).sqrMagnitude > CulvertTwinM * CulvertTwinM) continue;
                    want = Mathf.Max(want, oe.YAt(o.s) - CulvertTwinDyM);
                }
                float s0 = Mathf.Clamp(x.s - x.halfAlong, 0f, e.length), s1 = Mathf.Clamp(x.s + x.halfAlong, 0f, e.length);
                float low = Mathf.Min(e.YAt(s0), e.YAt(s1), e.YAt(x.s));
                for (int i = 0; i < e.stS.Length; i++)
                    if (e.stS[i] >= s0 && e.stS[i] <= s1 && !e.SeatedAt(i)) low = Mathf.Min(low, e.stY[i]);
                if (low >= want - 1e-3f) continue;
                RaiseSpan(e, s0, s1, want);
                CulvertHoldMaxM = Mathf.Max(CulvertHoldMaxM, want - low);
                if (culvertHeld.Add(ci)) CulvertCrossingsHeld++;
            }
        }

        /// <summary>A creek pipe's invert under a road crossing: the creek's
        /// carved floor there (its bed less the water's and the carve's
        /// depth, as <see cref="Ground"/> carves an open creek).</summary>
        public static float CulvertFloorY(CityMap.CulvertCrossing x) => x.bedASL - DatumASL - WaterBelowBed - CarveBelowWater;

        /// <summary>A flat-topped hump: at least <paramref name="targetY"/>
        /// from s0 to s1, falling away at the approach grade either side
        /// (seated stations are their hosts').</summary>
        static void RaiseSpan(CityMap.Edge e, float s0, float s1, float targetY)
        {
            float r = CrestR(e);
            for (int i = 0; i < e.stS.Length; i++)
            {
                if (e.SeatedAt(i)) continue;
                float off = Mathf.Max(0f, Mathf.Max(s0 - e.stS[i], e.stS[i] - s1));
                float want = targetY - ApproachDrop(off, r);
                if (e.stY[i] < want) e.stY[i] = want;
            }
        }

        /// <summary>
        /// Junctions meet exactly: a node takes the HIGHEST incident end (a
        /// raised mainline lifts its ramps' tips, never the reverse) and every
        /// edge re-blends. A stub too short to ramp levels its two nodes — but
        /// ONLY over small disagreements: a big disagreement across a stub
        /// stays a steep little ramp, which is what the geometry honestly is.
        /// Returns how far any node moved, for convergence.
        /// </summary>
        const float RigidStubM = 25f;
        const float RigidStubMaxDelta = 2.5f;

        static float ReconcileNodes(CityMap map)
        {
            float moved = 0f;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                float best = float.MinValue;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float endY = e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1];
                    if (endY > best) best = endY;
                }
                if (best > float.MinValue)
                {
                    moved = Mathf.Max(moved, Mathf.Abs(best - map.nodeY[n]));
                    map.nodeY[n] = best;
                }
            }
            foreach (var e in map.edges)
            {
                if (e.a == e.b) continue;
                // A stub or a fragment with one end in a cut is a ramp out
                // of the cut, however short; levelling it would lift the cut.
                if (pinnedNodeY != null && (!float.IsNaN(pinnedNodeY[e.a]) || !float.IsNaN(pinnedNodeY[e.b]))) continue;
                // ...and one seated on a host has the host's heights, whatever
                // they disagree by; levelling it lifts a node off the host.
                if (e.stSeat != null) continue;
                if (e.length < RigidStubM)
                {
                    float d = Mathf.Abs(map.nodeY[e.a] - map.nodeY[e.b]);
                    if (d > 0.01f && d < RigidStubMaxDelta)
                    {
                        float m = Mathf.Max(map.nodeY[e.a], map.nodeY[e.b]);
                        map.nodeY[e.a] = m;
                        map.nodeY[e.b] = m;
                        moved = Mathf.Max(moved, d);
                    }
                }
                else if (e.length < 90f)
                {
                    // A short viaduct fragment whose nodes disagree by more
                    // than it can climb gets its LOW node raised to what the
                    // climb can reach. Raising only, so clearances hold; the
                    // lift cascades outward through later rounds.
                    float feasible = e.length * MaxGrade(e) * 1.6f;
                    float hi = Mathf.Max(map.nodeY[e.a], map.nodeY[e.b]);
                    float lo = Mathf.Min(map.nodeY[e.a], map.nodeY[e.b]);
                    if (hi - lo > feasible)
                    {
                        float lift = hi - feasible - lo;
                        if (map.nodeY[e.a] < map.nodeY[e.b]) map.nodeY[e.a] += lift;
                        else map.nodeY[e.b] += lift;
                        moved = Mathf.Max(moved, lift);
                    }
                }
            }
            foreach (var e in map.edges) BlendEndsToNodes(map, e);
            return moved;
        }

        /// <summary>
        /// Lift the network around a node with an approach cone: every edge
        /// out of the node rises to at least (y - distance x ApproachGrade),
        /// and where the cone still stands above the far node it carries on
        /// through it — the same 4.5% RaiseHump uses within one edge, taken
        /// across junctions. Raises only. A one-metre sliver between a raised
        /// bridge and a low junction is simply passed through.
        /// </summary>
        /// (Each queued node carries the cone's apex and how far along the
        /// graph it already is from it, so the rounded top (ApproachDrop)
        /// runs on through a node instead of starting a new crest there.)
        static readonly List<(int node, float apex, float d0, float y)> coneQueue = new List<(int, float, float, float)>(64);
        static bool RaiseCone(CityMap map, int node, float y)
        {
            coneQueue.Clear();
            coneQueue.Add((node, y, 0f, y));
            int head = 0;
            bool any = false;
            while (head < coneQueue.Count && head < 4000)
            {
                var (n, apex, d0, ny) = coneQueue[head++];
                if (map.nodeY[n] < ny) map.nodeY[n] = ny;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    bool fromA = e.a == n;
                    bool moved = false, seatedOn = false;
                    int count = e.stS.Length;
                    float r = CrestR(e);
                    // Walked AWAY from the node, and stopped by the first
                    // seated station: an embankment does not run through a
                    // ramp that is lying on its mainline, and it must not
                    // come out of the far side and lift the mainline's node.
                    for (int k = 0; k < count; k++)
                    {
                        int i = fromA ? k : count - 1 - k;
                        if (e.SeatedAt(i)) { seatedOn = true; break; }
                        float dist = fromA ? e.stS[i] : e.length - e.stS[i];
                        float want = apex - ApproachDrop(d0 + dist, r);
                        if (e.stY[i] < want - 0.02f) { e.stY[i] = want; moved = true; }
                    }
                    if (moved) any = true;
                    if (!moved || seatedOn) continue;
                    int far = fromA ? e.b : e.a;
                    float farWant = apex - ApproachDrop(d0 + e.length, r);
                    if (farWant > map.nodeY[far] + 0.02f) coneQueue.Add((far, apex, d0 + e.length, farWant));
                }
            }
            return any;
        }

        /// <summary>
        /// Seed a cone from every node that stands ABOVE THE GROUND — every
        /// node a raise or a snap has lifted — and from any node an incident
        /// end falls short of. Not from every node: a road descending a real
        /// 6% hill away from a junction is not an embankment, and a cone from
        /// there would flatten every downhill in the county to 4.5%. Returns
        /// how many cones actually moved something, for the convergence loop.
        /// </summary>
        static int RaiseConesFromNodes(CityMap map)
        {
            int seeded = 0;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                float y = map.nodeY[n];
                bool seed = y > RoadBaseY(map.nodes[n].x, map.nodes[n].y) + 0.5f;
                if (!seed)
                    foreach (var ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        float endY = e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1];
                        if (endY < y - 0.02f) { seed = true; break; }
                    }
                if (!seed) continue;
                if (RaiseCone(map, n, y)) seeded++;
            }
            return seeded;
        }

        /// <summary>After the final raise, patches need node heights that match
        /// the raised arm ends — max of ends, no re-blend, so the clearance
        /// the raise just guaranteed is not disturbed.</summary>
        static void SnapNodesToEnds(CityMap map)
        {
            for (int n = 0; n < map.nodes.Length; n++)
            {
                float best = float.MinValue;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float endY = e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1];
                    if (endY > best) best = endY;
                }
                if (best > float.MinValue) map.nodeY[n] = best;
            }
        }

        /// <summary>Gaussian along the stations, sigma in stations. The SRTM
        /// grid is 60 m and a road is sampled every 10, so the raw profile
        /// carries the grid's facets; 25 m of smoothing takes them out
        /// without flattening a real hill.</summary>
        static readonly List<float> smoothScratch = new List<float>(256);
        static void Smooth(float[] y, float sigma)
        {
            int n = y.Length;
            if (n < 4) return;
            int r = Mathf.CeilToInt(sigma * 2.5f);
            smoothScratch.Clear();
            for (int i = 0; i < n; i++)
            {
                float sum = 0f, wsum = 0f;
                for (int k = -r; k <= r; k++)
                {
                    int j = Mathf.Clamp(i + k, 0, n - 1);
                    float w = Mathf.Exp(-k * k / (2f * sigma * sigma));
                    sum += y[j] * w; wsum += w;
                }
                smoothScratch.Add(sum / wsum);
            }
            for (int i = 0; i < n; i++) y[i] = smoothScratch[i];
        }

        static void ClampGrade(CityMap.Edge e, float g)
        {
            for (int i = 1; i < e.stY.Length; i++)
            {
                float ds = e.stS[i] - e.stS[i - 1];
                e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i - 1] - g * ds, e.stY[i - 1] + g * ds);
            }
            for (int i = e.stY.Length - 2; i >= 0; i--)
            {
                float ds = e.stS[i + 1] - e.stS[i];
                e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i + 1] - g * ds, e.stY[i + 1] + g * ds);
            }
        }

        /// <summary>Returns how far either end moved, for the final
        /// convergence loop.</summary>
        static float BlendEndsToNodes(CityMap map, CityMap.Edge e)
        {
            float L = Mathf.Min(e.length * 0.5f, 90f);
            if (L < 1f)
            {
                // a stub too short to blend just takes its nodes' line
                float ya = map.nodeY[e.a], yb = map.nodeY[e.b];
                float was = Mathf.Max(Mathf.Abs(ya - e.stY[0]), Mathf.Abs(yb - e.stY[e.stY.Length - 1]));
                for (int i = 0; i < e.stY.Length; i++)
                    e.stY[i] = Mathf.Lerp(ya, yb, e.length > 0f ? e.stS[i] / e.length : 0f);
                return was;
            }
            float dA = map.nodeY[e.a] - e.stY[0];
            float dB = map.nodeY[e.b] - e.stY[e.stY.Length - 1];
            for (int i = 0; i < e.stY.Length; i++)
            {
                float fromA = e.stS[i], fromB = e.length - e.stS[i];
                if (fromA < L) e.stY[i] += dA * (1f - fromA / L);
                if (fromB < L) e.stY[i] += dB * (1f - fromB / L);
            }
            return Mathf.Max(Mathf.Abs(dA), Mathf.Abs(dB));
        }

        // ------------------------------------------------------------------
        //  Vertical curves (2026-09-30)
        // ------------------------------------------------------------------
        /// <summary>The speed a road is judged at for its crests (km/h):
        /// what a driver in a hurry carries there, not the posted limit. The
        /// launch audit (Editor/CityLaunchAudit) judges by the same table.</summary>
        public static float JudgedKmh(CityMap.Edge e) =>
            e.link ? (e.cls >= 5 ? 90f : e.cls >= 3 ? 80f : 70f)
                   : e.cls >= 5 ? 150f : e.cls == 4 ? 130f : e.cls == 3 ? 110f : e.cls == 2 ? 100f : e.cls == 1 ? 90f : 70f;
        /// <summary>A race route's roads are judged at least this fast.</summary>
        public const float RouteKmh = 140f;
        /// <summary>The vertical-velocity kick (m/s) one station's grade break
        /// may give a car at the road's judged speed: dg &lt;= kick / v. A break
        /// of dg separates a free-flying car from the road by (v dg)^2 / 2G, so
        /// 1 m/s is 5 cm - the springs' static compression, the wheels just
        /// keep their load.</summary>
        public const float VcurveKickMs = 1.0f;
        /// <summary>PSX_CITY_VCURVES=0 switches the pass off (a measuring
        /// override: the launch audit's BEFORE; nothing sets it in a build).</summary>
        public static bool VerticalCurvesOn = System.Environment.GetEnvironmentVariable("PSX_CITY_VCURVES") != "0";
        /// <summary>What the last solve's vertical curves did, for the audits.</summary>
        public static string VcurveReport { get; private set; } = "";
        static string debugUnsettled = "";
        /// <summary>The most the pass may raise or lower any station: a crest
        /// held between pins is left as it is (and counted), not chased.</summary>
        public const float VcurveMaxRaiseM = 1.5f, VcurveMaxLowerM = 0.8f;
        /// <summary>How far a soft-seated street branch may stand off its
        /// host's height (see VerticalCurves).</summary>
        public const float SoftSeatTol = 0.03f;
        /// <summary>PSX_CITY_VPLANES=0: junctions keep their arms' own grades
        /// (a measuring override).</summary>
        static readonly bool FanPlanesOn = System.Environment.GetEnvironmentVariable("PSX_CITY_VPLANES") != "0";
        /// <summary>PSX_CITY_SEATPIN=0: street branches only soft-seated, as
        /// before the pins (a measuring override; see VerticalCurves).</summary>
        static readonly bool SeatPinsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SEATPIN") != "0";
        /// <summary>A street branch station is pinned on its host only within
        /// this of it: the tile builder's attach range (CityMeshes.AttachDy);
        /// further apart the two are drawn as different levels.</summary>
        public const float SeatPinMaxDy = 0.6f;

        /// <summary>The crest radius a road needs at its judged speed: the
        /// larger of 1.5 v^2/G (the curve takes two thirds of the wheels'
        /// load) and StationStep v / kick (each station's break within
        /// <see cref="VcurveKickMs"/>) - the second binds at every speed.</summary>
        public static float CrestRadius(CityMap.Edge e, bool route)
        {
            float v = Mathf.Max(JudgedKmh(e), route ? RouteKmh : 0f) / 3.6f;
            return Mathf.Max(1.5f * v * v / 9.81f, StationStep * v / VcurveKickMs);
        }

        /// <summary>
        /// VERTICAL CURVES. The owner, 2026-09-30: "many sections of road that
        /// meet at sharp angles that result in launching the car at high
        /// speed. All roads should meet at smooth junctions and transitions."
        /// Every pass above shapes a road with straight lines - a crossing's
        /// hump is a 4.5% TENT with its apex over the road below, a cone
        /// another tent at every raised node, a seat's climb-out an 8% line
        /// off the host, a node the meeting of two arms solved apart - and a
        /// car at speed leaves the road at every such apex (the launch audit
        /// measured it: Editor/CityLaunchAudit).
        ///
        /// So the profile is given a CREST LIMIT: along every edge, and
        /// through every node from each arm to the arm it continues into
        /// (the node's through pairs; a two-arm node always), no grade may
        /// fall faster than the spacing over <see cref="CrestRadius"/>. Each
        /// violated break is projected out with the smallest change (the
        /// middle station down, its neighbours up, in proportion to what the
        /// break owes each), Gauss-Seidel over the stations still moving, so
        /// a sharp apex becomes a curve spread over as many stations as its
        /// angle needs. Sags are left alone: a sag presses the car into the
        /// road.
        ///
        /// What may not move, and which way: a SEATED station is its host's
        /// (fixed; SeatBranches re-seats after); an over road within the deck
        /// reach of its crossing, a tagged bridge and a water span may only
        /// rise (their clearance is a floor, and a hump's cone would come
        /// straight back if lowered); an under road within its crossing's
        /// footprint, and a trench's pinned node, may only fall. A crest held
        /// on both sides by such pins is rounded by raising what can rise.
        /// The crossings are re-raised fresh after each round.
        /// </summary>
        static void VerticalCurves(CityMap map, int round)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int E = map.edges.Length, N = map.nodes.Length;
            debugUnsettled = "";
            var R = new float[E];
            for (int i = 0; i < E; i++) R[i] = CrestR(map.edges[i]);

            // mobility: bit 1 may fall, bit 2 may rise
            const byte Down = 1, Up = 2, Free = 3;
            var mob = new byte[E][];
            var nodeMob = new byte[N];
            for (int n = 0; n < N; n++) nodeMob[n] = Free;
            foreach (var e in map.edges)
            {
                int n = e.stY.Length;
                var m = mob[e.index] = new byte[n];
                for (int i = 0; i < n; i++) m[i] = e.SeatedAt(i) ? (byte)0 : Free;
                // the ends ARE the node (raised to it first: the ribbon's end
                // and the fan's centre must agree before anything is judged)
                if (e.a != e.b)
                {
                    if (e.stY[0] < map.nodeY[e.a]) e.stY[0] = map.nodeY[e.a];
                    if (e.stY[n - 1] < map.nodeY[e.b]) e.stY[n - 1] = map.nodeY[e.b];
                }
            }
            foreach (var ws in map.wspans)
            {
                var e = map.edges[ws.edge];
                for (int i = 0; i < e.stS.Length; i++)
                    if (e.stS[i] >= ws.s0 - StationStep && e.stS[i] <= ws.s1 + StationStep) mob[e.index][i] &= Up;
            }
            // a road over a creek pipe keeps the pipe's cover (HoldCulverts),
            // as a water span's deck did - except on a road that also carries
            // a trench's cut (plan B2) and stands well over the cover (three
            // rounds lower at most 2.4 m): there an Up-only window beside the
            // cut's lip made the curves round the lip by lifting the window
            // 4.2 m (I-277's ramp e329 under East 7th Street, 17.6% grade,
            // the B2 run)
            HashSet<int> cutEdges = null;
            if (trenchWant != null && trenchWant.Count > 0)
            {
                cutEdges = new HashSet<int>();
                foreach (var key in trenchWant.Keys) cutEdges.Add((int)(key >> 20));
            }
            if (map.creekCulverts != null && HasDem)
                foreach (var x in map.creekCulverts)
                {
                    if (x.edge < 0 || x.edge >= E) continue;
                    var e = map.edges[x.edge];
                    if (cutEdges != null && cutEdges.Contains(x.edge) &&
                        e.YAt(x.s) > CulvertFloorY(x) + CityCulverts.MinFillM + 3f * VcurveMaxLowerM + 0.1f) continue;
                    for (int i = 0; i < e.stS.Length; i++)
                        if (e.stS[i] >= x.s - x.halfAlong - StationStep && e.stS[i] <= x.s + x.halfAlong + StationStep) mob[e.index][i] &= Up;
                }
            var on = crossingOn;
            for (int ci = 0; ci < map.crossings.Length; ci++)
            {
                if (on != null && ci < on.Length && !on[ci]) continue;
                var c = map.crossings[ci];
                var over = map.edges[c.over]; var under = map.edges[c.under];
                ProjectOn(over, c.at, out float sO);
                ProjectOn(under, c.at, out float sU);
                float cos = Vector2.Dot(over.TangentAt(sO), under.TangentAt(sU));
                float sin = Mathf.Max(0.4f, Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos)));
                // the deck over the road below may only rise (the hump itself
                // is already a vertical curve, ApproachDrop; lowering its
                // approach would only be lifted back by the fresh raise)
                float over1 = (under.width * 0.5f + 3f) / sin + StationStep;
                for (int i = 0; i < over.stS.Length; i++)
                    if (Mathf.Abs(over.stS[i] - sO) <= over1) mob[over.index][i] &= Up;
                float foot = (over.width * 0.5f + 6f) / sin + StationStep;
                for (int i = 0; i < under.stS.Length; i++)
                    if (Mathf.Abs(under.stS[i] - sU) <= foot) mob[under.index][i] &= Down;
            }
            if (pinnedNodeY != null)
                for (int n = 0; n < N; n++) if (!float.IsNaN(pinnedNodeY[n])) nodeMob[n] &= Down;
            // a node moves only as every arm's end may
            foreach (var e in map.edges)
            {
                if (e.a == e.b) continue;
                nodeMob[e.a] &= mob[e.index][0];
                nodeMob[e.b] &= mob[e.index][e.stY.Length - 1];
            }

            // the through pairs: (arm i's first station, node, arm j's first station)
            var pairs = new List<(int n, int ei, int ej)>(N * 2);
            var armDir = new List<(int ei, Vector2 d)>(8);
            for (int n = 0; n < N; n++)
            {
                armDir.Clear();
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || e.length < 0.5f) continue;
                    armDir.Add((ei, e.a == n ? e.TangentAt(0f) : -e.TangentAt(e.length)));
                }
                for (int p = 0; p < armDir.Count; p++)
                    for (int q = p + 1; q < armDir.Count; q++)
                        if (armDir.Count == 2 ? Vector2.Dot(armDir[p].d, armDir[q].d) < 0.3f : Vector2.Dot(armDir[p].d, armDir[q].d) < -0.7f)
                            pairs.Add((n, armDir[p].ei, armDir[q].ei));
            }
            var nodePairs = new List<int>[N];
            for (int k = 0; k < pairs.Count; k++)
            {
                int n = pairs[k].n;
                (nodePairs[n] ??= new List<int>(2)).Add(k);
            }

            // THE MINOR ROAD MEETS THE MAJOR ROAD'S CROSS-SECTION. A junction
            // of three or more arms is drawn as one fan of triangles from the
            // node to each arm's two corners, and the ribbons are level across,
            // so where two graded roads cross the corners cannot lie on one
            // plane: the fan folds, and the lanes that pass beside the centre
            // cross the folds (the launch audit's junction-fan spots, lift-off
            // from 40 km/h). As a road designer does it: the node's MAJOR
            // through pair keeps its grade, level across; every other arm
            // leaves the node on that plane over its first station, held there
            // (with the node) while the crest limit curves it back to its own
            // grade further out.
            int planeHeld = 0;
            float planeMove = 0f;
            for (int n = 0; n < N && FanPlanesOn; n++)
            {
                if (map.nodeEdges[n].Count < 3 || nodePairs[n] == null || (nodeMob[n] & Free) != Free) continue;
                int best = -1; float bestKey = float.MinValue;
                foreach (int k in nodePairs[n])
                {
                    var P = map.edges[pairs[k].ei]; var Q = map.edges[pairs[k].ej];
                    float key = Mathf.Min(P.link ? P.cls - 0.5f : P.cls, Q.link ? Q.cls - 0.5f : Q.cls) * 100f + P.width + Q.width;
                    if (key > bestKey) { bestKey = key; best = k; }
                }
                var MP = map.edges[pairs[best].ei]; var MQ = map.edges[pairs[best].ej];
                Vector2 uP = MP.a == n ? MP.TangentAt(0f) : -MP.TangentAt(MP.length);
                Vector2 uQ = MQ.a == n ? MQ.TangentAt(0f) : -MQ.TangentAt(MQ.length);
                int p1 = MP.a == n ? 1 : MP.stY.Length - 2, q1 = MQ.a == n ? 1 : MQ.stY.Length - 2;
                float hP = Mathf.Abs(MP.stS[p1] - MP.stS[MP.a == n ? 0 : MP.stY.Length - 1]);
                float hQ = Mathf.Abs(MQ.stS[q1] - MQ.stS[MQ.a == n ? 0 : MQ.stY.Length - 1]);
                if (hP < 0.5f || hQ < 0.5f) continue;
                var axis = (uQ - uP).normalized;
                float gAlong = ((Get(MQ, q1) - map.nodeY[n]) / hQ - (Get(MP, p1) - map.nodeY[n]) / hP) * 0.5f;
                bool held = false;
                foreach (int ei in map.nodeEdges[n])
                {
                    if (ei == MP.index || ei == MQ.index) continue;
                    var e = map.edges[ei];
                    if (e.a == e.b || e.stY.Length < 3) continue;   // its first station is the far node
                    int i1 = e.a == n ? 1 : e.stY.Length - 2, i0 = e.a == n ? 0 : e.stY.Length - 1;
                    float h = Mathf.Abs(e.stS[i1] - e.stS[i0]);
                    if (h < 0.5f || mob[ei][i1] != Free) continue;
                    var u = e.a == n ? e.TangentAt(0f) : -e.TangentAt(e.length);
                    float want = map.nodeY[n] + gAlong * Vector2.Dot(axis, u) * h;
                    float y1 = e.stY[i1];
                    float to = Mathf.Clamp(want, y1 - VcurveMaxLowerM, y1 + VcurveMaxRaiseM);
                    planeMove = Mathf.Max(planeMove, Mathf.Abs(to - y1));
                    e.stY[i1] = to;
                    mob[ei][i1] = 0;
                    held = true; planeHeld++;
                }
                if (held) nodeMob[n] = 0;
            }

            // STREET BRANCHES ONTO THEIR HOSTS, PINNED. The soft seats (in the
            // sweeps below) pulled each station to within SoftSeatTol of its
            // host, but where the crest limit disagreed the two cycled and
            // froze, and the branch stayed off its host where the lanes merge:
            // North Caldwell Street 0.30 m under East 12th Street where it is
            // cut against it, a 0.28 m rise in 0.6 m of lane that stopped a
            // car dead at 70 km/h. A station whose ribbon runs INTO the host's
            // lanes (streetSeatInto; beside them it stays a soft seat) and that
            // may move that way is set on its host and HELD, as a ramp's seat
            // is, and the crest limit rounds the road around it instead (within
            // the pass's raise and lower caps); it follows its host again after
            // the sweeps. Only within SeatPinMaxDy: past it the tile builder
            // draws the two as different levels.
            int pinnedSeats = 0;
            var pinnedAt = new List<int>(256);
            if (SeatPinsOn)
                for (int k = 0; k < streetSeats.Count; k++)
                {
                    if (!streetSeatInto[k]) continue;
                    var (ei, i, h, hs) = streetSeats[k];
                    var e = map.edges[ei];
                    if (i <= 0 || i >= e.stY.Length - 1) continue;
                    byte m = mob[ei][i];
                    if (m == 0) continue;
                    float target = map.edges[h].YAt(hs), off = target - e.stY[i];
                    if (!(Mathf.Abs(off) <= SeatPinMaxDy)) continue;
                    if ((off > 0f && (m & Up) == 0) || (off < 0f && (m & Down) == 0)) continue;
                    e.stY[i] = target;
                    mob[ei][i] = 0;
                    pinnedSeats++;
                    pinnedAt.Add(k);
                }

            // the variables: interior stations in e.stY, nodes in map.nodeY
            float Get(CityMap.Edge e, int i) => e.a != e.b && i == 0 ? map.nodeY[e.a] : e.a != e.b && i == e.stY.Length - 1 ? map.nodeY[e.b] : e.stY[i];
            byte Mob(CityMap.Edge e, int i) => e.a != e.b && i == 0 ? nodeMob[e.a] : e.a != e.b && i == e.stY.Length - 1 ? nodeMob[e.b] : mob[e.index][i];
            var activeE = new bool[E]; var nextE = new bool[E];
            var activeN = new bool[N]; var nextN = new bool[N];
            for (int i = 0; i < E; i++) activeE[i] = true;
            for (int n = 0; n < N; n++) activeN[n] = nodePairs[n] != null;
            float maxRaise = 0f, maxLower = 0f;
            var before = new float[E][];
            foreach (var e in map.edges) before[e.index] = (float[])e.stY.Clone();
            var nodeBefore = (float[])map.nodeY.Clone();
            // no station moves further than this from where the pass found it:
            // a crest held between pins is left, not chased up the hill
            float Lo(CityMap.Edge e, int i) => (e.a != e.b && i == 0 ? nodeBefore[e.a] : e.a != e.b && i == e.stY.Length - 1 ? nodeBefore[e.b] : before[e.index][i]) - VcurveMaxLowerM;
            float Hi(CityMap.Edge e, int i) => (e.a != e.b && i == 0 ? nodeBefore[e.a] : e.a != e.b && i == e.stY.Length - 1 ? nodeBefore[e.b] : before[e.index][i]) + VcurveMaxRaiseM;

            void Touch(CityMap.Edge e, int i)
            {
                nextE[e.index] = true;
                if (e.a == e.b) return;
                int last = e.stY.Length - 1;
                if (i <= 1) { nextN[e.a] = true; if (last <= 1) nextN[e.b] = true; }
                if (i >= last - 1) { nextN[e.b] = true; if (last <= 1) nextN[e.a] = true; }
                if (i == 0) foreach (int oi in map.nodeEdges[e.a]) nextE[oi] = true;
                if (i == last) foreach (int oi in map.nodeEdges[e.b]) nextE[oi] = true;
            }
            void Put(CityMap.Edge e, int i, float y)
            {
                if (e.a != e.b && i == 0) map.nodeY[e.a] = y;
                else if (e.a != e.b && i == e.stY.Length - 1) map.nodeY[e.b] = y;
                else e.stY[i] = y;
                Touch(e, i);
            }
            // one break (a, b, c): h1 = b - a, h2 = c - b along the path
            // the break raises the grade by g2 - g1; the crest limit is
            // g2 - g1 >= -(h1 + h2) / 2R. Returns true when it moved anything.
            int stuck = 0;
            // A crest held between pins with no feasible curve cycles its
            // neighbours; a constraint projected this often is left as it is.
            const int FreezeAfter = 40;
            var hits = new byte[E][];
            var pairHits = new byte[pairs.Count];
            var seatHits = new byte[streetSeats.Count];
            bool Project(CityMap.Edge ea, int ia, CityMap.Edge eb, int ib, CityMap.Edge ec, int ic, float h1, float h2, float r)
            {
                if (h1 < 0.5f || h2 < 0.5f) return false;
                float ya = Get(ea, ia), yb = Get(eb, ib), yc = Get(ec, ic);
                float brk = (yc - yb) / h2 - (yb - ya) / h1;
                float excess = -(h1 + h2) / (2f * r) - brk;
                if (!(excess >= 2e-4f)) return false;   // (and never on a NaN)
                float ca = 1f / h1, cb = -(1f / h1 + 1f / h2), cc = 1f / h2;
                bool ma = (Mob(ea, ia) & Up) != 0 && ya < Hi(ea, ia) - 1e-4f;
                bool mb = (Mob(eb, ib) & Down) != 0 && yb > Lo(eb, ib) + 1e-4f;
                bool mc = (Mob(ec, ic) & Up) != 0 && yc < Hi(ec, ic) - 1e-4f;
                float norm = (ma ? ca * ca : 0f) + (mb ? cb * cb : 0f) + (mc ? cc * cc : 0f);
                if (norm < 1e-9f) { stuck++; return false; }
                float lam = excess / norm;
                if (ma) Put(ea, ia, Mathf.Min(Hi(ea, ia), ya + lam * ca));
                if (mb) Put(eb, ib, Mathf.Max(Lo(eb, ib), yb + lam * cb));
                if (mc) Put(ec, ic, Mathf.Min(Hi(ec, ic), yc + lam * cc));
                return true;
            }

            // every crest past its limit (0.2% of grade slack), without moving anything
            int Residual()
            {
                int bad = 0;
                float Brk(CityMap.Edge ea, int ia, CityMap.Edge eb, int ib, CityMap.Edge ec, int ic, float h1, float h2, float r)
                {
                    if (h1 < 0.5f || h2 < 0.5f) return 0f;
                    float ya = Get(ea, ia), yb = Get(eb, ib), yc = Get(ec, ic);
                    return -(h1 + h2) / (2f * r) - ((yc - yb) / h2 - (yb - ya) / h1);
                }
                foreach (var e in map.edges)
                    for (int i = 1; i < e.stY.Length - 1; i++)
                        if (Brk(e, i - 1, e, i, e, i + 1, e.stS[i] - e.stS[i - 1], e.stS[i + 1] - e.stS[i], R[e.index]) > 0.002f) bad++;
                foreach (var (nd, pi, pj) in pairs)
                {
                    var A = map.edges[pi]; var B = map.edges[pj];
                    int ia = A.a == nd ? 1 : A.stY.Length - 2, ja = A.a == nd ? 0 : A.stY.Length - 1;
                    int ib = B.a == nd ? 1 : B.stY.Length - 2, jb = B.a == nd ? 0 : B.stY.Length - 1;
                    if (Brk(A, ia, A, ja, B, ib, Mathf.Abs(A.stS[ia] - A.stS[ja]), Mathf.Abs(B.stS[ib] - B.stS[jb]), Mathf.Max(R[pi], R[pj])) > 0.002f) bad++;
                }
                return bad;
            }
            int violBefore = Residual();

            int sweeps = 0, moves = 0;
            var late = new int[E];   // projections per edge in the last 50 sweeps (a debug trail for a pass that does not settle)
            var lateNode = new int[N];
            for (; sweeps < 800; sweeps++)
            {
                bool any = false;
                stuck = 0;
                System.Array.Clear(nextE, 0, E); System.Array.Clear(nextN, 0, N);
                for (int ei = 0; ei < E; ei++)
                {
                    if (!activeE[ei]) continue;
                    var e = map.edges[ei];
                    int n = e.stY.Length;
                    var hc = hits[ei] ??= new byte[n];
                    for (int k = 1; k < n - 1; k++)
                    {
                        int i = (sweeps & 1) == 0 ? k : n - 1 - k;
                        if (hc[i] >= FreezeAfter) continue;
                        if (Project(e, i - 1, e, i, e, i + 1, e.stS[i] - e.stS[i - 1], e.stS[i + 1] - e.stS[i], R[ei])) { any = true; moves++; hc[i]++; if (sweeps >= 750) late[ei]++; }
                    }
                }
                for (int nd = 0; nd < N; nd++)
                {
                    if (!activeN[nd] || nodePairs[nd] == null) continue;
                    foreach (int k in nodePairs[nd])
                    {
                        if (pairHits[k] >= FreezeAfter) continue;
                        var (_, pi, pj) = pairs[k];
                        var A = map.edges[pi]; var B = map.edges[pj];
                        int ia = A.a == nd ? 1 : A.stY.Length - 2, ja = A.a == nd ? 0 : A.stY.Length - 1;
                        int ib = B.a == nd ? 1 : B.stY.Length - 2, jb = B.a == nd ? 0 : B.stY.Length - 1;
                        float hA = Mathf.Abs(A.stS[ia] - A.stS[ja]), hB = Mathf.Abs(B.stS[ib] - B.stS[jb]);
                        if (Project(A, ia, A, ja, B, ib, hA, hB, Mathf.Max(R[pi], R[pj]))) { any = true; moves++; pairHits[k]++; if (sweeps >= 750) lateNode[nd]++; }
                    }
                }
                // SOFT SEATS: a street branch inside its host's gore zone is
                // drawn clipped to the host's edge, its inner edge at the host's
                // height; standing apart from the host there (up to the clip's
                // 0.6 m), the lane beside the seam tilted and dropped back to
                // the branch's own height where the clip let go - a 0.24 m step
                // in a metre on the Independence route. The branch comes onto
                // the host (never the host onto it), within SoftSeatTol.
                for (int k = 0; k < streetSeats.Count; k++)
                {
                    var (ei, i, h, hs) = streetSeats[k];
                    if (!activeE[ei] || seatHits[k] >= FreezeAfter) continue;
                    var e = map.edges[ei];
                    float target = map.edges[h].YAt(hs), y = Get(e, i), off = y - target;
                    if (!(Mathf.Abs(off) > SoftSeatTol)) continue;
                    byte m = Mob(e, i);
                    float to = off > 0f ? target + SoftSeatTol : target - SoftSeatTol;
                    if (off > 0f && (m & Down) != 0) to = Mathf.Max(to, Lo(e, i));
                    else if (off < 0f && (m & Up) != 0) to = Mathf.Min(to, Hi(e, i));
                    else continue;
                    if (Mathf.Abs(to - y) < 1e-3f) continue;
                    Put(e, i, to);
                    seatHits[k]++; any = true; moves++;
                }
                if (!any) break;
                var t1 = activeE; activeE = nextE; nextE = t1;
                var t2 = activeN; activeN = nextN; nextN = t2;
            }
            // the pinned seats follow their hosts, which the sweeps may have moved
            foreach (int k in pinnedAt)
            {
                var (ei, i, h, hs) = streetSeats[k];
                float target = map.edges[h].YAt(hs);
                if (Mathf.Abs(target - map.edges[ei].stY[i]) <= SeatPinMaxDy) map.edges[ei].stY[i] = target;
            }
            // the ends take their nodes again
            int moved = 0;
            foreach (var e in map.edges)
            {
                int n = e.stY.Length;
                if (e.a != e.b) { e.stY[0] = map.nodeY[e.a]; e.stY[n - 1] = map.nodeY[e.b]; }
                var b0 = before[e.index];
                for (int i = 0; i < n; i++)
                {
                    float d = e.stY[i] - b0[i];
                    if (Mathf.Abs(d) > 0.01f) moved++;
                    if (d > maxRaise) maxRaise = d;
                    if (-d > maxLower) maxLower = -d;
                }
            }
            int violAfter = Residual();
            int frozen = 0;
            foreach (var hc in hits) if (hc != null) foreach (var c in hc) if (c >= FreezeAfter) frozen++;
            foreach (var c in pairHits) if (c >= FreezeAfter) frozen++;
            int softOff = 0;
            foreach (var (ei, i, h, hs) in streetSeats) if (Mathf.Abs(map.edges[ei].stY[i] - map.edges[h].YAt(hs)) > SoftSeatTol + 0.02f) softOff++;
            if (sweeps >= 800)
            {
                var worstE = new List<int>(); for (int i = 0; i < E; i++) if (late[i] > 0) worstE.Add(i);
                worstE.Sort((x, y) => late[y].CompareTo(late[x]));
                var sbd = new System.Text.StringBuilder(" UNSETTLED:");
                for (int k = 0; k < Mathf.Min(6, worstE.Count); k++)
                {
                    var e = map.edges[worstE[k]];
                    sbd.Append($" e{e.index} '{e.name}'{(e.link ? " L" : "")} cls{e.cls} len{e.length:0} n{e.stY.Length} x{late[e.index]} mob[");
                    for (int i = 0; i < e.stY.Length && i < 14; i++) sbd.Append(Mob(e, i));
                    sbd.Append("] y[");
                    for (int i = 0; i < e.stY.Length && i < 14; i++) sbd.Append(Get(e, i).ToString("0.00")).Append(' ');
                    sbd.Append($"] a{e.a}(deg{map.nodeEdges[e.a].Count}) b{e.b}(deg{map.nodeEdges[e.b].Count});");
                }
                for (int nd = 0; nd < N; nd++) if (lateNode[nd] > 20) { sbd.Append($" node{nd} x{lateNode[nd]} mob{nodeMob[nd]} pairs{nodePairs[nd]?.Count};"); if (sbd.Length > 3000) break; }
                debugUnsettled = sbd.ToString();
            }
            string r0 = round == 0 ? "" : VcurveReport + "; ";
            VcurveReport = r0 + debugUnsettled + $" round {round}: {violBefore} crests past their limit -> {violAfter} ({frozen} frozen cycling, {stuck} held by pins), {planeHeld} junction arms set on their major road's plane (up to {planeMove:0.00} m), {streetSeats.Count} street-branch stations seated ({pinnedSeats} pinned, {softOff} still off their host), {sweeps} sweeps, {moves} projections, {moved} stations moved > 1 cm (up to +{maxRaise:0.00} / -{maxLower:0.00} m), {clock.ElapsedMilliseconds} ms";
        }

        // ------------------------------------------------------------------
        //  TWIN DECKS (plan B1; DeckPairs): one structure, one height, one
        //  abutment line.
        // ------------------------------------------------------------------

        /// <summary>PSX_CITY_TWINHOLD=0 turns the twin-deck holds, the deck-end
        /// alignment and the land under a union's gap off (the TWIN report's
        /// before-numbers; never in a build).</summary>
        public static bool TwinHoldsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TWINHOLD") != "0";
        /// <summary>A twin station further than this off its partner is left
        /// alone: two decks that far apart in height are a split level or a
        /// ramp climbing away, not one deck's two halves (a union ends there,
        /// plan A2). The humps of a skewed crossing put one carriageway's
        /// peak up to ~0.8 m over the other's (I-277 e2026/e2029).</summary>
        public const float TwinHoldMaxDyM = 1.0f;
        /// <summary>What the holds leave between twins after the curves (C12).</summary>
        public const float TwinHoldTolM = 0.02f;
        public static int TwinHoldMoves { get; private set; }
        public static int TwinHoldSkipped { get; private set; }
        public static int TwinHoldRounds { get; private set; }
        public static float TwinDyBeforeFinal { get; private set; }
        public static int TwinFinalRaised { get; private set; }
        public static float TwinFinalRaiseMax { get; private set; }
        /// <summary>The largest grade break the pointwise hold after the
        /// curves added at any station it touched or beside one: as a sag
        /// (grade out minus grade in, up) and as a crest (down; the higher of
        /// two crest-limited profiles adds none, bar the resampling of the
        /// twin at this edge's stations).</summary>
        public static float TwinFinalSagAdded { get; private set; }
        public static float TwinFinalCrestAdded { get; private set; }
        /// <summary>Raises the crest guard took back (some or all of).</summary>
        public static int TwinFinalClamped { get; private set; }
        /// <summary>The TWIN report's (e), measured at the end of the solve:
        /// the union twins' height difference every 2 m where both are decks
        /// by the facts and on structure (up to <see cref="TwinHoldMaxDyM"/>).</summary>
        public static float TwinDyP50 { get; private set; }
        public static float TwinDyP95 { get; private set; }
        public static float TwinDyMax { get; private set; }
        public static int TwinDySamples { get; private set; }

        /// <summary>The (e) numbers over the table as it stands (the TWIN
        /// report computes its own; this is the solve's copy, so a launch or
        /// budget log carries them too).</summary>
        static void MeasureTwinDy(CityMap map)
        {
            TwinDyP50 = TwinDyP95 = TwinDyMax = 0f; TwinDySamples = 0;
            if (map.deckPairs == null) return;
            var d = new List<float>(4096);
            foreach (var pr in map.deckPairs)
            {
                if (!pr.union || pr.solved) continue;
                var A = map.edges[pr.a]; var B = map.edges[pr.b];
                for (float s = pr.a0; s <= pr.a1 + 0.01f; s += 2f)
                {
                    if (!A.ElevatedAt(s)) continue;
                    float at = pr.ArcOnOther(pr.a, s);
                    if (at < 0f || !B.ElevatedAt(at) || !DeckPairs.DeckAt(pr.a, s) || !DeckPairs.DeckAt(pr.b, at)) continue;
                    float dy = Mathf.Abs(A.YAt(s) - B.YAt(at));
                    if (dy <= TwinHoldMaxDyM) d.Add(dy);
                }
            }
            if (d.Count == 0) return;
            d.Sort();
            float P(float q) => d[Mathf.Clamp(Mathf.CeilToInt(q * d.Count) - 1, 0, d.Count - 1)];
            TwinDyP50 = P(0.5f); TwinDyP95 = P(0.95f); TwinDyMax = d[d.Count - 1]; TwinDySamples = d.Count;
        }
        public static float TwinDyAfterCurves { get; private set; }
        static int TwinDyWorstPair = -1;
        public static int TwinAlignedPairs { get; private set; }
        public static int TwinAlignedStations { get; private set; }
        public static string TwinHoldReport { get; private set; } = "";

        /// <summary>
        /// Every union pair (DeckPairs) to one height: each station of one
        /// deck beside the other's deck rises to the other's height there,
        /// with RaiseHump's flat top and cones. Raises only, so it converges
        /// with every other raise; a seated station is its host's; a station
        /// more than <see cref="TwinHoldMaxDyM"/> off is left (a real split
        /// level). The number of stations raised more than a centimetre.
        /// </summary>
        static int HoldTwinDecks(CityMap map)
        {
            if (!TwinHoldsOn || map.deckPairs == null) return 0;
            int moves = 0;
            foreach (var pr in map.deckPairs)
            {
                if (!pr.union || pr.solved) continue;
                moves += HoldTwin(map, pr, pr.a, true) + HoldTwin(map, pr, pr.b, true);
            }
            return moves;
        }

        /// <summary>
        /// The twin's arc beside station arc <paramref name="s"/> of edge
        /// <paramref name="ei"/>: the pair's map inside its range, a
        /// projection a station beyond it (the range is where the census's 2 m
        /// samples saw both decks, from 1 m in; the station at the edge's end
        /// governs its first 10 m), -1 when the twin is not beside it.
        /// </summary>
        static float TwinArc(CityMap map, DeckPairs.Pair pr, int ei, float s)
        {
            if (s < pr.From(ei) - StationStep - 0.01f || s > pr.To(ei) + StationStep + 0.01f) return -1f;
            float at = pr.ArcOnOther(ei, s);
            if (at >= 0f) return at;
            var e = map.edges[ei]; var o = map.edges[pr.Other(ei)];
            var p = e.PointAt(s);
            ProjectOn(o, p, out at);
            return Vector2.Distance(o.PointAt(at), p) <= e.HalfMax + o.HalfMax + Mathf.Max(0f, pr.gap) + 6f ? at : -1f;
        }

        /// <summary>One deck's stations beside its twin, raised to the twin.
        /// In the cone loop (<paramref name="cones"/>): the stations inside
        /// the pair's range, with RaiseHump's cones. (A station past the range
        /// - the edge's end, a node - was tried on 2026-10-02: its cones ran
        /// through the node into the approaches and lifted 145 more stations
        /// over the 3.5 m margin.) After the curves, pointwise: the interior
        /// stations a step either side of the range and of the deck too (the
        /// station that ends a deck shapes its last 10 m), no cones, then the
        /// crest guard (FinalTwinHold).</summary>
        static int HoldTwin(CityMap map, DeckPairs.Pair pr, int ei, bool cones)
        {
            var e = map.edges[ei]; var o = map.edges[pr.Other(ei)];
            int moves = 0, n = e.stS.Length;
            for (int i = 0; i < n; i++)
            {
                float s = e.stS[i];
                if (e.SeatedAt(i)) continue;
                float at;
                if (cones)
                {
                    if (s < pr.From(ei) - 0.01f || s > pr.To(ei) + 0.01f || !DeckPairs.DeckAt(ei, s)) continue;
                    at = pr.ArcOnOther(ei, s);
                }
                else
                {
                    if (i == 0 || i == n - 1) continue;
                    if (!DeckPairs.DeckAt(ei, s) && !DeckPairs.DeckAt(ei, s - StationStep) && !DeckPairs.DeckAt(ei, s + StationStep)) continue;
                    at = TwinArc(map, pr, ei, s);
                }
                if (at < 0f || !DeckPairs.DeckAt(o.index, at)) continue;
                float target = o.YAt(at);
                if (e.stY[i] >= target - (cones ? 0.01f : 0.001f)) continue;
                if (target - e.stY[i] > TwinHoldMaxDyM) { if (cones) TwinHoldSkipped++; continue; }
                if (cones) RaiseHump(e, s, target);
                else
                {
                    TwinFinalRaiseMax = Mathf.Max(TwinFinalRaiseMax, target - e.stY[i]);
                    e.stY[i] = target;
                    TwinFinalRaised++;
                }
                moves++;
            }
            return moves;
        }

        /// <summary>The pointwise hold after the curves (see Solve), both ways
        /// round, until it settles (three passes at most), with the grade
        /// breaks it added measured.</summary>
        static void FinalTwinHold(CityMap map)
        {
            TwinFinalRaised = 0; TwinFinalRaiseMax = 0f; TwinFinalSagAdded = 0f; TwinFinalCrestAdded = 0f;
            if (!TwinHoldsOn || map.deckPairs == null) return;
            var before = new Dictionary<int, float[]>();
            foreach (var pr in map.deckPairs)
            {
                if (!pr.union || pr.solved) continue;
                foreach (int ei in new[] { pr.a, pr.b })
                    if (!before.ContainsKey(ei)) before[ei] = (float[])map.edges[ei].stY.Clone();
            }
            for (int pass = 0; pass < 4; pass++)
            {
                int moved = 0;
                foreach (var pr in map.deckPairs)
                {
                    if (!pr.union || pr.solved) continue;
                    moved += HoldTwin(map, pr, pr.a, false) + HoldTwin(map, pr, pr.b, false);
                }
                if (moved == 0) break;
            }
            // THE CREST GUARD. Raising a station can only sharpen the crest AT
            // that station (its neighbours' breaks turn toward a sag), and the
            // higher of two crest-limited profiles adds none - but where the
            // raise stops short of a crossing (an edge's end, a seated station,
            // the deck's end) it does: a plateau has corners. Every raised
            // station whose break is now past the curves' own limit (and worse
            // than before) comes back down to the limit, never below where the
            // curves left it, until none is: the curves' crest rule holds over
            // every twin, and what the guard keeps apart is the TWIN report's
            // residual.
            TwinFinalClamped = 0;
            for (int it = 0; it < 12; it++)
            {
                bool any = false;
                foreach (var kv in before)
                {
                    var e = map.edges[kv.Key]; var y0 = kv.Value; var y = e.stY;
                    float r = CrestR(e);
                    for (int i = 1; i + 1 < y.Length; i++)
                    {
                        if (y[i] <= y0[i] + 1e-5f) continue;
                        float h0 = Mathf.Max(0.01f, e.stS[i] - e.stS[i - 1]), h1 = Mathf.Max(0.01f, e.stS[i + 1] - e.stS[i]);
                        float lim = -0.5f * (h0 + h1) / r;
                        float b = (y[i + 1] - y[i]) / h1 - (y[i] - y[i - 1]) / h0;
                        float b0 = (y0[i + 1] - y0[i]) / h1 - (y0[i] - y0[i - 1]) / h0;
                        float want = Mathf.Min(lim, b0);
                        if (b >= want - 1e-5f) continue;
                        float yi = (y[i + 1] / h1 + y[i - 1] / h0 - want) / (1f / h1 + 1f / h0);
                        float ny = Mathf.Max(y0[i], Mathf.Min(y[i], yi));
                        if (ny < y[i] - 1e-6f) { y[i] = ny; any = true; TwinFinalClamped++; }
                    }
                }
                if (!any) break;
            }
            foreach (var kv in before)
            {
                var e = map.edges[kv.Key]; var y0 = kv.Value; var y = e.stY;
                for (int i = 1; i + 1 < y.Length; i++)
                {
                    if (y[i - 1] == y0[i - 1] && y[i] == y0[i] && y[i + 1] == y0[i + 1]) continue;
                    float h0 = Mathf.Max(0.01f, e.stS[i] - e.stS[i - 1]), h1 = Mathf.Max(0.01f, e.stS[i + 1] - e.stS[i]);
                    float b = (y[i + 1] - y[i]) / h1 - (y[i] - y[i - 1]) / h0;
                    float b0 = (y0[i + 1] - y0[i]) / h1 - (y0[i] - y0[i - 1]) / h0;
                    TwinFinalSagAdded = Mathf.Max(TwinFinalSagAdded, b - b0);
                    TwinFinalCrestAdded = Mathf.Max(TwinFinalCrestAdded, b0 - b);
                }
            }
        }

        /// <summary>The largest height difference left between union twins
        /// (station against the partner beside it, within
        /// <see cref="TwinHoldMaxDyM"/>), and the pair it is on.</summary>
        static float TwinDy(CityMap map, out int worstPair)
        {
            worstPair = -1;
            if (!TwinHoldsOn || map.deckPairs == null) return 0f;
            float worst = 0f;
            for (int pi = 0; pi < map.deckPairs.Length; pi++)
            {
                var pr = map.deckPairs[pi];
                if (!pr.union || pr.solved) continue;
                foreach (int ei in new[] { pr.a, pr.b })
                {
                    var e = map.edges[ei]; var o = map.edges[pr.Other(ei)];
                    for (int i = 0; i < e.stS.Length; i++)
                    {
                        float s = e.stS[i];
                        if (e.SeatedAt(i) || !DeckPairs.DeckAt(ei, s)) continue;
                        float at = TwinArc(map, pr, ei, s);
                        if (at < 0f || !DeckPairs.DeckAt(o.index, at)) continue;
                        float dy = Mathf.Abs(o.YAt(at) - e.stY[i]);
                        if (dy > TwinHoldMaxDyM) continue;
                        if (dy > worst) { worst = dy; worstPair = pi; }
                    }
                }
            }
            return worst;
        }

        /// <summary>The longest a deck is carried on to meet its twin's end.</summary>
        public const float TwinAlignMaxM = 15f;

        /// <summary>
        /// ONE ABUTMENT LINE (plan B1): where a union deck's end stops at most
        /// <see cref="TwinAlignMaxM"/> short of its twin's (projected), the
        /// shorter deck is carried on to it, so both end on one line and the
        /// mesh can close them with one face (plan A2). After
        /// <see cref="MarkStructure"/>; structure only, no height moves.
        /// </summary>
        static void AlignTwinStructure(CityMap map)
        {
            TwinAlignedPairs = 0; TwinAlignedStations = 0;
            if (!TwinHoldsOn || map.deckPairs == null) return;
            var add = new List<(CityMap.Edge e, int i)>();
            foreach (var pr in map.deckPairs)
            {
                if (!pr.union) continue;
                int before = add.Count;
                foreach (int ei in new[] { pr.a, pr.b })
                {
                    var e = map.edges[ei]; var o = map.edges[pr.Other(ei)];
                    float s0 = pr.From(ei), s1 = pr.To(ei);
                    for (int i = 0; i < e.stS.Length; i++)
                    {
                        if (e.stElev[i]) continue;
                        float s = e.stS[i];
                        if (s < s0 - TwinAlignMaxM || s > s1 + TwinAlignMaxM) continue;
                        // within reach of THIS pair's deck run on e
                        float nearest = float.MaxValue;
                        for (int j = 0; j < e.stS.Length; j++)
                            if (e.stElev[j] && e.stS[j] >= s0 - 0.01f && e.stS[j] <= s1 + 0.01f)
                                nearest = Mathf.Min(nearest, Mathf.Abs(e.stS[j] - s));
                        if (nearest > TwinAlignMaxM) continue;
                        var p = e.PointAt(s);
                        ProjectOn(o, p, out float at);
                        if (Vector2.Distance(o.PointAt(at), p) > e.HalfMax + o.HalfMax + Mathf.Max(0f, pr.gap) + 2f) continue;
                        if (!o.ElevatedAt(at)) continue;
                        add.Add((e, i));
                    }
                }
                if (add.Count > before) TwinAlignedPairs++;
            }
            foreach (var (e, i) in add) if (!e.stElev[i]) { e.stElev[i] = true; TwinAlignedStations++; }
        }

        static void RaiseHump(CityMap.Edge e, float sAt, float targetY)
        {
            // No reach cap, deliberately. A capped hump under a four-level
            // stack ended in a fifteen-metre CLIFF at the cap — which the
            // grade-relax pass then "fixed" by hauling the whole deck down
            // through the road it was built to clear. The cone fades below
            // terrain on its own; distant stations are a comparison and a no-op.
            // The top is a vertical curve (ApproachDrop), not a tent's apex.
            float r = CrestR(e);
            for (int i = 0; i < e.stS.Length; i++)
            {
                // A seated station is its host's; the host takes its own hump.
                if (e.SeatedAt(i)) continue;
                float want = targetY - ApproachDrop(Mathf.Abs(e.stS[i] - sAt), r);
                if (e.stY[i] < want) e.stY[i] = want;
            }
        }

        public static void ProjectOn(CityMap.Edge e, Vector2 p, out float arcS)
        {
            float best = float.MaxValue; arcS = 0f;
            for (int i = 0; i + 1 < e.pts.Length; i++)
            {
                Vector2 a = e.pts[i], d = e.pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                Vector2 q = a + d * t;
                float dd = (p - q).sqrMagnitude;
                if (dd < best) { best = dd; arcS = e.s[i] + Mathf.Sqrt(L2) * t; }
            }
        }

        // ------------------------------------------------------------------
        //  Ground height, per query. Tile-local by construction.
        // ------------------------------------------------------------------
        static HashSet<int> segScratch;
        static HashSet<int> waterScratch;

        public const float MaxCorridorHalf = 24f;

        /// <summary>Which rule set a ground height, for the audit's pit
        /// census: the DEM (after water), the corridor blend, the highest
        /// road FLOOR, the lowest PROTECTED pavement, and the two deck caps.
        /// Values are NaN and edges -1 where the rule did not apply.</summary>
        public struct GroundTerms
        {
            public float dem, blended, result;
            /// <summary>How far the water carved the DEM here (WP-04b).</summary>
            public float carve;
            public float floor; public int floorEdge;
            public float protect; public int protectEdge;
            public float deckProtect, deckCap; public int deckEdge;
            /// <summary>The highest floor among GROUNDED roads whose pavement
            /// is within <see cref="PitReachM"/>: the design a pit is judged
            /// against.</summary>
            public float nearFloor; public int nearEdge; public float nearDist;
            /// <summary>WP-25: the creek channel's level where it took the
            /// land from a bridge approach's spill (<see cref="Ground"/>,
            /// THE CHANNEL AT A BRIDGE); NaN where it did not.</summary>
            public float channel;
        }
        /// <summary>How far from a grounded ribbon's pavement the pit census
        /// looks.</summary>
        public const float PitReachM = 25f;

        public static float GroundY(CityMap map, float x, float z) => Ground(map, x, z, out _);

        // ------------------------------------------------------------------
        //  THE SECTION (WP-14). One definition, read by Ground, the tile's
        //  cut-wall test (CityMeshes.InCut) and the probes.
        // ------------------------------------------------------------------
        /// <summary>How far past its pavement edge (<paramref name="edgeM"/>
        /// from the centreline) a road's section reaches: Ground sees every
        /// segment within <see cref="MaxCorridorHalf"/> +
        /// <see cref="CorridorBlend"/> of the point, so that less the edge and
        /// a metre, and at most <see cref="RoadsideRules.CityReachMaxM"/>.</summary>
        public static float SectionReachM(float edgeM) =>
            Mathf.Min(RoadsideRules.CityReachMaxM, MaxCorridorHalf + CorridorBlend - edgeM - 1f);

        /// <summary>The natural land (<see cref="BaseY"/>) at the section's
        /// reach, straight out from the foot <paramref name="foot"/> through
        /// <paramref name="p"/>: what a bank too deep for its slope meets.
        /// <paramref name="land"/> (the land at p) where p is on the line.</summary>
        public static float LandAtReach(Vector2 foot, Vector2 p, float dist, float outM, float land)
        {
            if (dist < 1e-3f) return land;
            var r = foot + (p - foot) * (outM / dist);
            return BaseY(r.x, r.y);
        }

        /// <summary>A FILL at <paramref name="past"/> metres past the
        /// pavement: the pin across the bench, then
        /// <see cref="RoadsideRules.CityFillSlope"/> (1V:4H) down, and never
        /// above the <see cref="RoadsideRules.CityBankSlope"/> (1V:2H) bank
        /// that reaches the land by <paramref name="reach"/> - the land there
        /// (<paramref name="landAtReach"/>) or here, whichever is lower, so it
        /// ends ON the land where the ground query stops seeing the road.
        /// Meaningful where it stands above the land.</summary>
        public static float SectionFill(float pin, float bench, float past, float reach, float land, float landAtReach)
        {
            float f = pin - Mathf.Max(0f, past - bench) * RoadsideRules.CityFillSlope;
            float bank = Mathf.Min(land, landAtReach) + Mathf.Max(0f, reach - past) * RoadsideRules.CityBankSlope;
            return Mathf.Min(f, bank);
        }

        /// <summary>A CUT at <paramref name="past"/> metres past the pavement:
        /// the pin across the band to <paramref name="start"/>, then
        /// <see cref="RoadsideRules.BackSlope"/> (1V:3H) up, and never below
        /// the 1V:2H bank that comes down from the land by
        /// <paramref name="reach"/> (the higher of the land there and here).
        /// Meaningful where it stands below the land.</summary>
        public static float SectionCut(float pin, float start, float past, float reach, float land, float landAtReach)
        {
            float c = pin + Mathf.Max(0f, past - start) * RoadsideRules.BackSlope;
            float bank = Mathf.Max(land, landAtReach) - Mathf.Max(0f, reach - past) * RoadsideRules.CityBankSlope;
            return Mathf.Max(c, bank);
        }

        /// <summary>
        /// The ground height at a point, and why. In order:
        ///
        ///   1. the DEM, carved by creeks and sunk by lakes;
        ///   2. graded to every grounded road beside it by that road's DOT
        ///      SECTION (WP-14, <see cref="RoadsideRules"/>), each side on its
        ///      own: the land lies at the road's pin (tarmac -
        ///      <see cref="CorridorSink"/> - the sag allowance) across the
        ///      pavement and its BENCH, then
        ///        a FILL falls from the bench at 1V:4H down to the land
        ///        (raised to the highest such floor of all the roads, so a
        ///        road's verge is never graded down onto a neighbour's ledge);
        ///        a CUT stays at the pin across the lattice band
        ///        (<see cref="RoadsideRules.CityCutBandM"/>: no 8 m lattice
        ///        triangle that touches the pavement may rise above it) and
        ///        climbs from there at the 1V:3H back slope up to the land
        ///        (lowered to the lowest such cap, so grass never comes
        ///        through a lower road's lanes; where a floor and a cap
        ///        disagree the two roads cannot be graded apart, the upper
        ///        edge stands over a drop, and the tile puts a warranted rail
        ///        on it);
        ///      a section too deep for its slope to meet the land by its reach
        ///      (<see cref="SectionReachM"/>, where this query stops seeing the
        ///      road) steepens to a 1V:2H bank for its last stretch
        ///      (<see cref="SectionFill"/>, <see cref="SectionCut"/>) and meets
        ///      the land exactly there, never a steeper face nor a cliff; a
        ///      smoothstep fade over 20-32 m before stood deep sections at up
        ///      to 1V:1.2H. Measured from the pavement edge the ribbon is
        ///      drawn to (CityMap.Edge.PaveEdgeM). It replaced a fixed
        ///      corridor, flat to 11.5 m past the pavement and blended to the
        ///      DEM over 26 m more, which read on every hill as a road on a
        ///      flat strip;
        ///   3. THE CHANNEL AT A BRIDGE (WP-25): by a creek, the disc round a
        ///      bridge approach's vertex (<see cref="SpillsAtStructure"/>)
        ///      spills at the 1V:2H bank and no further down than the creek's
        ///      carved channel, and under a deck's footprint no disc holds
        ///      the land over the channel at all - the approaches' 1V:4H
        ///      spill buried the water for 20-50 m beside every bridge the
        ///      owner named;
        ///   5. under structure: no higher than any deck's pavement minus the
        ///      sink within the same band, and dug to UnderDeckAir under the
        ///      soffit within <see cref="CapPadM"/> of the deck — both from
        ///      TRUE perpendicular feet on an elevated stretch, or from the
        ///      vertex of a bend inside one (<see cref="InStructureWedge"/>).
        /// </summary>
        public static float Ground(CityMap map, float x, float z, out GroundTerms terms)
        {
            terms = new GroundTerms
            {
                floor = float.NaN, protect = float.NaN, deckProtect = float.NaN, deckCap = float.NaN, nearFloor = float.NaN,
                floorEdge = -1, protectEdge = -1, deckEdge = -1, nearEdge = -1, channel = float.NaN,
            };
            float baseY = BaseY(x, z);

            // creeks and ravines carve, lakes sink
            waterScratch ??= new HashSet<int>();
            waterScratch.Clear();
            float reachW = CarveReachM;
            map.WaterSegsInRect(new Vector2(x - reachW, z - reachW), new Vector2(x + reachW, z + reachW), waterScratch);
            map.RavineSegsInRect(new Vector2(x - reachW, z - reachW), new Vector2(x + reachW, z + reachW), waterScratch);
            var p2 = new Vector2(x, z);
            float demY = baseY;
            // WP-25: the lowest creek channel here (its carved profile, flat
            // floor and banks, whether or not it cut the DEM); MaxValue away
            // from every creek, and with WP-25 off (the A/B instruments'
            // "before"). A ravine is not a channel: a road over one keeps its
            // embankment (a culvert).
            float chanV = float.MaxValue;
            bool channels = !CityMeshes.HydroOff;
            foreach (var packed in waterScratch)
            {
                int wi = packed >> 12, si = packed & 0xFFF;
                var w = map.waters[wi];
                if (w.bedY != null && !w.lake)
                {
                    // CARVED TO THE BED: a flat floor, then banks at
                    // BankSlope until they meet the land (a min, so a valley
                    // the grid already shows is not dug twice)
                    float d = WaterFoot(w, si, p2, out float sAt);
                    if (d >= CarveReachM) continue;
                    float bed = BedYAt(w, sAt);
                    float floorY, flat;
                    if (w.ravine) { floorY = bed - RavineBelowBed; flat = RavineFlat; }
                    else { floorY = bed - WaterBelowBed - CarveBelowWater; flat = CreekFlatHalf(w); }
                    float cut = floorY + Mathf.Max(0f, d - flat) * BankSlope;
                    if (cut < baseY) baseY = cut;
                    if (channels && !w.ravine && cut < chanV) chanV = cut;
                    continue;
                }
                if (w.ravine) continue;
                if (w.lake)
                {
                    // inside: pinned under the surface (below, for every lake
                    // the point is in); near shore: blended down
                    if (!CityMap.LakeContains(w, p2))
                    {
                        float dsh = DistToSeg(w.pts, si, p2);
                        if (dsh < 14f)
                            baseY = Mathf.Min(baseY, Mathf.Lerp(w.surfaceY + 0.4f, baseY, dsh / 14f));
                    }
                }
                else
                {
                    float reach = w.width * 0.5f + 16f;
                    float d = DistToSeg(w.pts, si, p2);
                    if (d < reach)
                    {
                        float t = 1f - d / reach;
                        t = t * t * (3f - 2f * t);
                        baseY -= 3.6f * t;
                    }
                }
            }

            // THE INSIDE OF A LAKE, whatever its shore is doing (WP-04b: the
            // hash holds only a lake's shore now)
            foreach (int li in map.LakesAt(p2))
            {
                var w = map.waters[li];
                if (CityMap.LakeContains(w, p2)) baseY = Mathf.Min(baseY, w.surfaceY - 2.2f);
            }
            terms.carve = demY - baseY;

            // every grounded road beside the point grades it by its section
            // (grounded stations only; WP-14)
            segScratch ??= new HashSet<int>();
            segScratch.Clear();
            float reachR = MaxCorridorHalf + CorridorBlend;
            // only segments within the reach can pin: every other one fails the corridor test below
            map.EdgeSegsNear(new Vector2(x - reachR, z - reachR), new Vector2(x + reachR, z + reachR), segScratch);

            float land = baseY;
            float floorMax = float.MinValue, cutMin = float.MaxValue, capMin = float.MaxValue;
            float deckProtect = float.MaxValue, deckCap = float.MaxValue;
            // WP-25, THE CHANNEL AT A BRIDGE: the floors from clamped feet
            // (the discs round a grounded road's vertices) kept apart from
            // the true feet's, the highest design floor before the channel
            // took any, and whether the point is under a deck's footprint
            float floorClampMax = float.MinValue, floorDesignMax = float.MinValue;
            int floorClampEdge = -1, floorTrueEdge = -1;
            bool underDeck = false;
            terms.dem = baseY;
            foreach (var packed in segScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float tRaw = L2 > 1e-8f ? Vector2.Dot(p2 - a, d) / L2 : 0f;
                float t = Mathf.Clamp01(tRaw);
                Vector2 q = a + d * t;
                float dist = Vector2.Distance(p2, q);
                float at = e.s[si] + Mathf.Sqrt(L2) * t;
                // metres past the pavement's edge on this side (negative on it)
                float hw = e.PaveEdgeM(at, e.SideOf(si, p2));
                float past = dist - hw;
                float reachS = SectionReachM(hw);
                if (past > reachS) continue;
                if (e.ElevatedAt(at))
                {
                    // Structure does not pin the land — but the land may not
                    // reach it either. Only BESIDE the deck, at a true
                    // perpendicular foot: a clamped projection turned every
                    // polyline vertex and node of a structure run into a disc
                    // that dug the grounded approach beyond the rails. The one
                    // clamped foot kept is the OUTER WEDGE of a bend inside the
                    // structure, which no segment's true foot covers: without
                    // it a lattice corner there took the DEM, and a replica of
                    // an at-grade deck bending 8 degrees at a vertex put the
                    // land 0.30 m over its surface wherever the DEM stood that
                    // far above the road (see InStructureWedge).
                    if ((tRaw <= 0f || tRaw >= 1f) && !InStructureWedge(map, e, si, tRaw <= 0f, p2)) continue;
                    float deckY = e.YAt(at);
                    // A deck is over SOMETHING; where the DEM shows nothing to
                    // cross, its footprint is dug to leave UnderDeckAir under
                    // the soffit. A cap, never a raise: under a real viaduct
                    // the valley is already deeper. Beside a twin it is one
                    // structure with (plan B1/A2: DeckPairs), the cap runs on
                    // to the middle of the gap the median slab spans, where
                    // the twin's own cap takes over.
                    float capR = hw + CapPadM;
                    if (TwinHoldsOn && map.deckUnionsOf != null && map.deckUnionsOf[ei] != null)
                        capR = Mathf.Max(capR, DeckPairs.UnionCapReach(map, e, at, -e.SideOf(si, p2), hw));
                    if (dist <= capR) underDeck = true;
                    if (dist <= capR && deckY - DeckThick - UnderDeckAir < deckCap)
                    { deckCap = deckY - DeckThick - UnderDeckAir; terms.deckEdge = ei; }
                    // ...and its pavement is protected like any road's: the
                    // lattice cells it touches never stand above it.
                    float ch = Mathf.Min(e.CorridorHalf, MaxCorridorHalf);
                    if (dist <= ch + CapReach && deckY - CorridorSink < deckProtect)
                    { deckProtect = deckY - CorridorSink; if (terms.deckEdge < 0) terms.deckEdge = ei; }
                    continue;
                }
                // the sag allowance: extra sink where the profile is convex
                // from below, so the straight lattice never rises above the
                // bent road (see MeasureSags)
                float pin = e.YAt(at) - CorridorSink - e.SagAt(at);
                float bench = RoadsideRules.CityBenchM(e.cls, e.link);
                // the land at the section's reach straight out from the road,
                // sampled only where the section binds (SectionCut/SectionFill)
                float atReach = float.NaN;
                // THE CUT (the land above the road): the pin across the
                // lattice band, then the back slope up to the land, steepened
                // to the 1V:2H bank only where it cannot meet it by the reach.
                float band = RoadsideRules.CityCutStartM(e.cls, e.link);
                float cut = pin + Mathf.Max(0f, past - band) * RoadsideRules.BackSlope;
                if (cut < baseY)
                {
                    atReach = LandAtReach(q, p2, dist, hw + reachS, baseY);
                    cut = SectionCut(pin, band, past, reachS, baseY, atReach);
                }
                if (cut < cutMin) cutMin = cut;
                // THE CAP (every road's protection, whatever grades the land
                // round it): no vertex of an 8 m lattice triangle that can
                // touch this pavement stands above it. Across the band that
                // is the pin; from there to the lattice's reach the back
                // slope, whose rise a triangle reaching back under the
                // pavement carries over the edge by less than the sink
                // (RoadsideRules.CityCutBandM); past the reach, nothing.
                if (past <= RoadsideRules.CityLatticeReachM)
                {
                    float cap = pin + Mathf.Max(0f, past - RoadsideRules.CityCutBandM) * RoadsideRules.BackSlope;
                    if (cap < capMin) { capMin = cap; terms.protectEdge = ei; }
                }
                // THE FLOOR (a fill's side): the pin across the bench, then
                // 1V:4H down, steepened to the 1V:2H bank only where it cannot
                // meet the land by the reach - but only where the land is
                // BELOW it. Where the land stands above the road a floor is no
                // fill, and pulled toward that land it raised what the cuts had
                // graded down straight back up (I-277's floor stood the land
                // 5 m over a ramp beside it).
                float floor = pin - Mathf.Max(0f, past - bench) * RoadsideRules.CityFillSlope;
                if (baseY < floor)
                {
                    if (float.IsNaN(atReach)) atReach = LandAtReach(q, p2, dist, hw + reachS, baseY);
                    floor = SectionFill(pin, bench, past, reachS, baseY, atReach);
                }
                if (past <= PitReachM && (float.IsNaN(terms.nearFloor) || floor > terms.nearFloor))
                { terms.nearFloor = floor; terms.nearEdge = ei; terms.nearDist = past; }
                if (floor > floorDesignMax) floorDesignMax = floor;
                if (tRaw <= 0f || tRaw >= 1f)
                {
                    // THE CHANNEL AT A BRIDGE (WP-25): the disc round an
                    // approach's vertex spilled its 1V:4H fill down the creek
                    // beside the deck and buried the water for 20-50 m below
                    // W Trade St, State St and Archdale Dr. By a creek, that
                    // spill stands at the steepest graded bank (1V:2H, a
                    // bridge's spill-through slope) and the channel takes the
                    // rest. Beside the road (a true foot) the section is as it
                    // was: its verge grades onto a 1V:4H fill.
                    if (chanV < floor && SpillsAtStructure(map, e, tRaw <= 0f ? si : si + 1))
                        floor = Mathf.Min(floor, Mathf.Max(chanV, pin - Mathf.Max(0f, past - bench) * RoadsideRules.CityBankSlope));
                    if (floor > floorClampMax) { floorClampMax = floor; floorClampEdge = ei; }
                }
                else if (floor > floorMax) { floorMax = floor; floorTrueEdge = ei; }
            }
            // UNDER A DECK over a creek no disc holds the land at all: the
            // pavement there is the deck, and the channel runs under it
            // (the land was held 4 m over Irwin Creek's water under W Trade
            // St, dug only to the soffit's air). A true foot still holds it:
            // another road's own pavement and verge.
            if (underDeck && chanV < float.MaxValue) floorClampMax = Mathf.Min(floorClampMax, chanV);
            terms.floorEdge = floorClampMax > floorMax ? floorClampEdge : floorTrueEdge;
            floorMax = Mathf.Max(floorMax, floorClampMax);
            if (floorMax < floorDesignMax - 1e-4f && floorDesignMax > baseY) terms.channel = Mathf.Max(floorMax, baseY);
            if (floorMax > float.MinValue || cutMin < float.MaxValue)
            {
                // the land cut down to every road's back slope...
                if (cutMin < float.MaxValue) land = Mathf.Min(land, cutMin);
                // ...held up by every road's fill (the upper road's verge is
                // never cut down onto a neighbour's ledge)...
                if (floorMax > float.MinValue) { land = Mathf.Max(land, floorMax); terms.floor = floorMax; }
                terms.blended = land;
                // ...and never above the LOWEST cap this point is beside:
                // grass through a lower road's lanes is worse than a ledge
                if (capMin < float.MaxValue) { land = Mathf.Min(land, capMin); terms.protect = capMin; }
                baseY = land;
            }
            else terms.blended = baseY;
            if (deckProtect < float.MaxValue) { baseY = Mathf.Min(baseY, deckProtect); terms.deckProtect = deckProtect; }
            if (deckCap < float.MaxValue) { baseY = Mathf.Min(baseY, deckCap); terms.deckCap = deckCap; }
            terms.result = baseY;
            return baseY;
        }

        /// <summary>
        /// Is <paramref name="p"/>, whose projection on segment
        /// <paramref name="si"/> clamps to one of its ends, in the outer wedge
        /// of a bend that is structure on BOTH sides? The wedge outside a
        /// polyline vertex is exactly the set of points with no true
        /// perpendicular foot on either segment meeting there. At an interior
        /// vertex the other segment is this edge's own; at a node every other
        /// arm must be on structure at its end (a viaduct split into OSM ways,
        /// a fan on a deck) and give no true foot either. A structure END — a
        /// node with a grounded arm, the edge's run stopping at the vertex — is
        /// not a wedge: that is the disc that dug the approaches.
        ///
        /// The other segment's projection must clamp to the SHARED vertex, not
        /// to its far end: a point beyond the far end has no true foot on it
        /// either, but it is round that far vertex, not in this one's wedge.
        /// WP-11's densify put a vertex 2.4 m into Chestnut Lane's 9 m bridge
        /// over West Fork Twelvemile Creek, and a point on the grounded
        /// approach 2.9 m short of the deck (its first segment's far side)
        /// read as the wedge of that vertex: the deck's cap dug the land under
        /// the approach 1.8 m below the road (the WP-25 culvert audit's hole).
        /// </summary>
        static bool InStructureWedge(CityMap map, CityMap.Edge e, int si, bool atStart, Vector2 p)
        {
            int vi = atStart ? si : si + 1;
            int last = e.pts.Length - 1;
            if (vi > 0 && vi < last)
            {
                int oj = atStart ? si - 1 : si + 1;
                // atStart: the other segment ENDS at the vertex; else it starts there
                return ClampsAt(e.pts[oj], e.pts[oj + 1], p, atStart) && e.ElevatedAt(e.s[vi] + (atStart ? -0.5f : 0.5f));
            }
            int node = vi == 0 ? e.a : e.b;
            bool any = false;
            foreach (var oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == e || o.a == o.b || o.pts.Length < 2) continue;
                bool fromA = o.a == node;
                if (!o.ElevatedAt(fromA ? 0f : o.length)) return false;
                int n = o.pts.Length;
                if (!(fromA ? ClampsAt(o.pts[0], o.pts[1], p, false) : ClampsAt(o.pts[n - 2], o.pts[n - 1], p, true))) return false;
                any = true;
            }
            return any;
        }

        /// <summary>Does <paramref name="p"/>'s projection on segment a-b
        /// clamp to <paramref name="b"/> (<paramref name="atB"/>) or to
        /// <paramref name="a"/>? A degenerate segment clamps to either.</summary>
        static bool ClampsAt(Vector2 a, Vector2 b, Vector2 p, bool atB)
        {
            var d = b - a;
            float L2 = d.sqrMagnitude;
            if (L2 < 1e-8f) return true;
            float t = Vector2.Dot(p - a, d) / L2;
            return atB ? t >= 1f : t <= 0f;
        }

        /// <summary>How far along a grounded road from one of its vertices a
        /// deck may begin for that vertex's disc to be an APPROACH's spill
        /// (<see cref="SpillsAtStructure"/>).</summary>
        public const float SpillReachM = 40f;

        /// <summary>
        /// Is vertex <paramref name="vi"/> of grounded edge
        /// <paramref name="e"/> a bridge approach's: a station of the edge on
        /// structure within <see cref="SpillReachM"/> of it along the edge
        /// (a deck in the middle of the way, W Trade St and State St), or, at
        /// a node, another arm on structure within that of the node (a deck
        /// that is its own way, Archdale Dr)? WP-25's channel takes the land
        /// from such a vertex's disc by a creek (<see cref="Ground"/>).
        /// </summary>
        static bool SpillsAtStructure(CityMap map, CityMap.Edge e, int vi)
        {
            if (StructureNear(e, e.s[vi], SpillReachM)) return true;
            if (vi != 0 && vi != e.pts.Length - 1) return false;
            int node = vi == 0 ? e.a : e.b;
            foreach (var oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == e || o.stS == null) continue;
                if (StructureNear(o, o.a == node ? 0f : o.length, SpillReachM)) return true;
            }
            return false;
        }

        /// <summary>A station of <paramref name="e"/> on structure within
        /// <paramref name="r"/> of arc position <paramref name="at"/>?</summary>
        static bool StructureNear(CityMap.Edge e, float at, float r)
        {
            var st = e.stS;
            if (st == null || st.Length == 0 || e.stElev == null) return false;
            int lo = 0, hi = st.Length - 1;
            float from = at - r;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (st[mid] < from) lo = mid + 1; else hi = mid;
            }
            // one station back: ElevatedAt reads a span on structure when
            // either end of it is
            for (int i = Mathf.Max(0, lo - 1); i < st.Length && st[i] <= at + r; i++)
                if (e.stElev[i]) return true;
            return false;
        }

        static float DistToSeg(Vector2[] pts, int si, Vector2 p)
        {
            if (si + 1 >= pts.Length) si = Mathf.Max(0, pts.Length - 2);
            Vector2 a = pts[si], d = pts[si + 1] - a;
            float L2 = d.sqrMagnitude;
            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
            return Vector2.Distance(p, a + d * t);
        }

        /// <summary>Water surface height for a river point. The bed is carved
        /// 3.6 m; the surface sits 1.4 m up it, because the ground lattice is
        /// 8 m and a surface half a metre above the centreline's dip was
        /// under the grass everywhere but the one vertex that hit the
        /// centreline — creeks were invisible from every bridge. Lakes use
        /// their flat surfaceY instead.</summary>
        public static float RiverSurfaceY(float x, float z) => BaseY(x, z) - 3.6f + 1.4f;
    }
}
