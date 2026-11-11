using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// HOUSE WALLS + HOUSE CLASS (the owner, 2026-11-11, his top priority:
    /// "replacing these very unrealistic generic textures with windows going
    /// through roofs"). Two causes, both fixed here by rule:
    ///
    /// 1. The siding (<c>city_facade_house</c>: one window per 6 m x 3.1 m
    ///    repeat, window at u 0.3125-0.6875, v 0.25-0.75 - the HD set keeps that
    ///    layout) was TILED over each wall from the wall's foot (under the
    ///    ground, under the graded pad) to the eave, rounded to whole repeats
    ///    and stretched, and over the gable triangles too: a window landed
    ///    wherever the repeat put it - sliced by the gable's slopes, half in
    ///    the ground on a sloping lot, two storeys of windows on a one-storey
    ///    wall. Now each wall is laid out in GEOMETRY: whole bays wall to wall
    ///    (a window centred in each, never nearer a corner than 0.3 of a bay),
    ///    one window band per storey from the house's floor (sill and head
    ///    per storey; no band whose head would reach the eave, none whose sill
    ///    the drawn ground reaches), and plain cladding everywhere else - the
    ///    foundation, between the bands, under the eave and the whole gable
    ///    triangle - mapped to the texture's window-free rows (v 0.75 to 1.25,
    ///    1.55 m, anchored at the window edges so the laps run on). Because it
    ///    is UVs and geometry, it holds for Classic, Enhanced and HD alike.
    ///    PSX_CITY_HOUSEWALLS=0 = the old tiling.
    /// 2. The exporter (tools/city/export_osm.mjs) calls a footprint a HOUSE
    ///    when OSM says building=house/detached/terrace/RESIDENTIAL/garages, or
    ///    an untagged building under 260 m2 - so a 1,100 m2 three-level
    ///    apartment block on N Graham St (building=residential, height 11.76)
    ///    wore house siding. <see cref="HouseBlock"/>: a house footprint of
    ///    <see cref="BlockMinAreaM2"/> or more, or <see cref="BlockMinH"/>
    ///    tall or more, is drawn as the residential brick block it is (the
    ///    facade atlas, home storeys, flat roof with its cornice and roof
    ///    gear). Its pad, lot and seat are unchanged (only the drawing).
    ///    PSX_CITY_HOUSECLASS=0 = siding on everything as before.
    /// 3. PSX_CITY_HOUSEEAVES (on): a gabled house's roof overhangs its walls
    ///    0.35 m at the eaves and 0.25 m at the rakes, the overhang drawn on
    ///    both faces (its underside the soffit).
    /// </summary>
    public static partial class CityMeshes
    {
        public static bool HouseWallsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEWALLS") != "0";
        public static bool HouseClassOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSECLASS") != "0";
        public static bool HouseEavesOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEEAVES") != "0";

        /// <summary>A "house" footprint this big (plan m2) is a block of flats,
        /// townhouses in a row or a garage court: no house in Charlotte has
        /// that footprint (the census: 26,079 house footprints, 99 % under 350).</summary>
        public const float BlockMinAreaM2 = 450f;
        /// <summary>...or this tall (m): four storeys and more (the exporter
        /// makes 3 levels 9.3 m; N Graham's blocks are tagged 11.76 m).</summary>
        public const float BlockMinH = 10.2f;

        /// <summary>Is house footprint <paramref name="f"/> drawn as a
        /// residential block (not a house)?</summary>
        public static bool HouseBlock(CityMap.Footprint f)
        {
            if (!HouseClassOn || f == null || f.style != 3 || f.pts == null || f.pts.Length < 3) return false;
            if (f.h >= BlockMinH) return true;
            float a = 0f;
            for (int i = 0, n = f.pts.Length; i < n; i++)
            {
                var p = f.pts[i]; var q = f.pts[(i + 1) % n];
                a += p.x * q.y - q.x * p.y;
            }
            return Mathf.Abs(a) * 0.5f >= BlockMinAreaM2;
        }

        /// <summary>A block drawn as Charlotte's 1980s-90s condo / townhouse
        /// row (the N Graham St pair: lap siding over a brick ground storey,
        /// a pitched roof): four storeys at most, under 4,000 m2, and an oblong
        /// (its plan fills 85 % of its oriented box, so the gable roof on the
        /// box covers it). Others stay the flat-roofed brick block.</summary>
        public static bool HouseBlockSiding(CityMap.Footprint f)
        {
            if (f == null || f.h > 14f || f.hu <= 0f || f.hv <= 0f) return false;
            float a = 0f;
            for (int i = 0, n = f.pts.Length; i < n; i++) { var p = f.pts[i]; var q = f.pts[(i + 1) % n]; a += p.x * q.y - q.x * p.y; }
            a = Mathf.Abs(a) * 0.5f;
            return a < 4000f && a >= 0.85f * 4f * f.hu * f.hv && f.hv >= 4f;
        }

        // ---- the siding texture's layout (city_facade_house, every tier) ----
        const float HouseRepU = 6f, HouseRepV = 3.1f;
        public const float HouseWinU0 = 0.3125f, HouseWinU1 = 0.6875f, HouseWinV0 = 0.25f, HouseWinV1 = 0.75f;
        /// <summary>The window-free rows: v 0.75 up to 1.25 (= 0.25 of the next repeat).</summary>
        const float HousePlainV0 = 0.75f, HousePlainSpanV = 0.5f;
        /// <summary>One bay (window + the siding either side) this wide, about.</summary>
        const float HouseBayM = 4.0f;
        /// <summary>A wall narrower than this has no window (a porch return, a sliver).</summary>
        const float HouseMinWinWallM = 2.2f;
        /// <summary>A storey pitch below this is no storey (floor to floor).</summary>
        const float HouseMinStoreyM = 2.35f;
        /// <summary>Clear siding kept under the eave (fascia, frieze board) and over the sill.</summary>
        const float HouseHeadClearM = 0.3f;

        /// <summary>The storeys' floor of the house whose walls are being
        /// emitted (its pad's top); NaN: the walls' foot + BuildingSink.</summary>
        static float houseFloorY = float.NaN;
        /// <summary>The map the house walls read the drawn ground from (set by
        /// the house emitters; null: no ground test).</summary>
        static CityMap hwMap;

        /// <summary>Audit counters (Editor/CityAudit.Windows.cs): windows laid,
        /// storey bands dropped because the drawn ground reached their sill.</summary>
        public static int HouseWinLaid, HouseWinGroundDropped;

        /// <summary>One siding wall, a to c, from <paramref name="y0"/> (its
        /// foot) to <paramref name="y1"/> (its top: the eave), laid out in
        /// whole bays and storeys (see the class).</summary>
        static void HouseWall(TileMeshes tm, Vector2 a, Vector2 c, float y0, float y1, Vector2 outward)
        {
            float W = Vector2.Distance(a, c);
            if (W < 0.05f || y1 <= y0 + 0.02f) return;
            float floor = float.IsNaN(houseFloorY) ? y0 + BuildingSink : Mathf.Clamp(houseFloorY, y0, y1);
            int bays = W < HouseMinWinWallM ? 0 : Mathf.Max(1, Mathf.RoundToInt(W / HouseBayM));
            float uPlain = bays > 0 ? bays : Mathf.Max(1f, Mathf.Round(W / HouseRepU));
            // storeys: whole floor-to-floor pitches between the floor and the eave
            float avail = y1 - floor;
            int storeys = bays == 0 ? 0 : Mathf.Max(0, Mathf.FloorToInt(avail / houseStoreyM + 0.0001f));
            float pitch = storeys > 0 ? Mathf.Min(avail / storeys, houseStoreyM > HouseMinStoreyM ? 3.4f : 3.3f) : 0f;
            float winH = Mathf.Clamp(pitch * 0.5f, 1.0f, 1.55f);
            float sill = Mathf.Min((pitch - winH - HouseHeadClearM) * 0.6f + 0.15f, 0.95f);
            // the drawn ground along the wall, highest (a sill under it is buried)
            float groundHi = float.NegativeInfinity;
            if (hwMap != null && storeys > 0)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(W / 0.5f));
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector2.Lerp(a, c, k / (float)n);
                    groundHi = Mathf.Max(groundHi, LatticeY(hwMap, p.x, p.y));
                }
            }
            float y = y0;
            int s0 = 0;
            if (houseBrickLook >= 0 && avail > 2.2f && (!houseBrickBase || storeys > 1))
            {
                // BRICK: the atlas's brick column, one whole floor row a storey
                // (whole repeats along, so its windows are whole), the courses
                // under the floor in concrete block; all storeys, or (a block's
                // brick base) the ground storey only
                int stB = Mathf.Max(1, Mathf.FloorToInt(avail / HouseMinStoreyM + 0.0001f));
                int nb = houseBrickBase ? 1 : stB;
                float pb = houseBrickBase ? pitch : avail / stB;
                if (floor > y0 + 0.01f) HouseFoundation(tm, a, c, y0, floor, outward);
                var keep = Bucket.Tint;
                PickFacade(houseBrickAt, 2, 2, 2, default, floor, pb);
                HouseBrickLookSet(houseBrickAt);
                for (int k = 0; k < nb; k++) EmitFacadeQuad(tm, Slot.FacadeGlass, a, c, floor + k * pb, floor + (k + 1) * pb, outward);
                Bucket.Tint = keep; Bucket.FacOn = false;
                y = floor + nb * pb; s0 = nb;
                if (!houseBrickBase) { if (y1 > y + 0.01f) HousePlain(tm, a, c, y, y1, outward, uPlain, true); return; }
            }
            for (int s = s0; s < storeys; s++)
            {
                float ws = floor + s * pitch + sill, we = ws + winH;
                if (we > y1 - HouseHeadClearM + 0.001f) break;
                if (ws < groundHi + 0.15f) { HouseWinGroundDropped += bays; continue; }   // the ground reaches it: plain
                if (ws > y + 0.01f) HousePlain(tm, a, c, y, ws, outward, uPlain, false);
                EmitPanels(tm, Slot.FacadeHouse, a, c, ws, we, outward, bays, HouseWinV1 - HouseWinV0, HouseWinV0);
                HouseWinLaid += bays;
                y = we;
            }
            if (y1 > y + 0.01f) HousePlain(tm, a, c, y, y1, outward, uPlain, true);
        }

        /// <summary>Plain siding from ya to yb: the window-free rows, in pieces
        /// of 1.55 m at most. <paramref name="up"/>: anchored at its foot (it
        /// starts over a window's head, or is the top of the wall), else at its
        /// top (it ends under a sill), so the laps run on at the window.</summary>
        static void HousePlain(TileMeshes tm, Vector2 a, Vector2 c, float ya, float yb, Vector2 outward, float uReps, bool up)
        {
            float span = HousePlainSpanV * HouseRepV;
            if (up)
                for (float y = ya; y < yb - 0.005f; y += span)
                {
                    float t = Mathf.Min(yb, y + span);
                    EmitPanels(tm, Slot.FacadeHouse, a, c, y, t, outward, uReps, (t - y) / HouseRepV, HousePlainV0);
                }
            else
                for (float y = yb; y > ya + 0.005f; y -= span)
                {
                    float b = Mathf.Max(ya, y - span);
                    float h = (y - b) / HouseRepV;
                    EmitPanels(tm, Slot.FacadeHouse, a, c, b, y, outward, uReps, h, HousePlainV0 + HousePlainSpanV - h);
                }
        }

        /// <summary>A gable triangle (a, apex, c; a and c at the eave) in plain
        /// siding: horizontal bands of 1.55 m at most from the eave up, each
        /// band a trapezoid (the last a triangle) on the window-free rows.</summary>
        static void HouseGableTri(Bucket bk, TileMeshes tm, Vector2 a, Vector2 apex, Vector2 c, float eave, float ridge)
        {
            float rise = ridge - eave;
            if (rise < 0.01f) return;
            float span = HousePlainSpanV * HouseRepV;
            float W = Vector2.Distance(a, c);
            float uW = Mathf.Max(1f, Mathf.Round(W / HouseRepU));
            for (float y = eave; y < ridge - 0.005f; y += span)
            {
                float t = Mathf.Min(ridge, y + span);
                float f0 = (y - eave) / rise, f1 = (t - eave) / rise;
                // the band's corners: along each slope from the eave corner to the apex
                Vector2 la = Vector2.Lerp(a, apex, f0), ra = Vector2.Lerp(c, apex, f0);
                Vector2 lb = Vector2.Lerp(a, apex, f1), rb = Vector2.Lerp(c, apex, f1);
                float v0 = HousePlainV0, v1 = HousePlainV0 + (t - y) / HouseRepV;
                // u: across the wall, a to c = 0..uW
                float uLa = uW * 0.5f * f0, uRa = uW - uW * 0.5f * f0, uLb = uW * 0.5f * f1, uRb = uW - uW * 0.5f * f1;
                if (f1 >= 0.9999f)
                    bk.Tri(L(la, y, tm), L(apex, ridge, tm), L(ra, y, tm), new Vector2(uLa, v0), new Vector2(uW * 0.5f, v1), new Vector2(uRa, v0));
                else
                {
                    // two triangles, the same winding as EmitGableTri's (a, apex, c)
                    bk.Tri(L(la, y, tm), L(lb, t, tm), L(ra, y, tm), new Vector2(uLa, v0), new Vector2(uLb, v1), new Vector2(uRa, v0));
                    bk.Tri(L(lb, t, tm), L(rb, t, tm), L(ra, y, tm), new Vector2(uLb, v1), new Vector2(uRb, v1), new Vector2(uRa, v0));
                }
            }
        }

        // ---- BRICK HOUSES (PSX_CITY_HOUSEBRICK) ----
        public static bool HouseBrickOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEBRICK") != "0";
        /// <summary>The atlas brick column the house being emitted wears (-1: siding).</summary>
        static int houseBrickLook = -1;
        /// <summary>...on its ground storey only (a siding block's brick base).</summary>
        static bool houseBrickBase;
        static Vector2 houseBrickAt;
        /// <summary>The least floor-to-floor pitch of the walls being emitted (a block's storeys are taller).</summary>
        static float houseStoreyM = HouseMinStoreyM;
        /// <summary>A block small enough to be a low condo / townhouse building (under 4,000 m2).</summary>
        public static bool HouseBlockLowArea(CityMap.Footprint f)
        {
            float a = 0f;
            for (int i = 0, n = f.pts.Length; i < n; i++) { var p = f.pts[i]; var q = f.pts[(i + 1) % n]; a += p.x * q.y - q.x * p.y; }
            return Mathf.Abs(a) * 0.5f < 4000f;
        }
        /// <summary>The house brick columns: the old brick, red-brown sash, mottled, tan brick.</summary>
        static readonly int[] HouseBrickLooks = { LookBrick, LookRedSash, LookMottled, LookTanBrick };

        /// <summary>Is the house at <paramref name="c"/> brick? A neighbourhood
        /// (a 350 m cell) is mostly brick, mixed or mostly siding (the brick
        /// ranch streets of the 1950s-60s beside the siding bungalows of the
        /// streetcar suburbs); a one-storey house is a little likelier brick.</summary>
        static int HouseBrickChoose(Vector2 c, bool oneStorey)
        {
            if (!HouseBrickOn) return -1;
            int cx = Mathf.FloorToInt(c.x / 350f), cz = Mathf.FloorToInt(c.y / 350f);
            uint nb = HouseHash(cx, cz, 23u);
            float share = (nb & 3u) == 0u ? 0.15f : (nb & 3u) == 1u ? 0.45f : 0.75f;
            if (oneStorey) share += 0.1f;
            uint hh = HouseHash(Mathf.RoundToInt(c.x * 2f), Mathf.RoundToInt(c.y * 2f), 29u);
            if ((hh & 0xFFFF) / 65536f >= share) return -1;
            // one or two brick colours a neighbourhood
            int i = (int)(((nb >> 4) + ((hh >> 16) & 1u)) % (uint)HouseBrickLooks.Length);
            return HouseBrickLooks[i];
        }

        /// <summary>After PickFacade: the chosen brick column, its palette tint, a home's lights.</summary>
        static void HouseBrickLookSet(Vector2 c)
        {
            int look = houseBrickLook;
            uint h = FacadeHash(c);
            facLook = look;
            FacadeStructure(look, 0, h, facFloorH);
            var pal = LookPalette[look];
            var k = pal[(int)((h >> 8) % (uint)pal.Length)];
            Bucket.Tint = new Color32(TintR(k.x, look), TintByte(k.y), TintByte(k.z), (byte)((look & 7) * 32 + FacadeHomeBit));
        }

        /// <summary>The courses under a brick house's floor: concrete block (the flat-roof concrete).</summary>
        static void HouseFoundation(TileMeshes tm, Vector2 a, Vector2 c, float y0, float y1, Vector2 outward)
        {
            var st = buckets[(int)Slot.RoofFlat];
            float W = Vector2.Distance(a, c);
            st.Wall(L(a, 0f, tm), L(c, 0f, tm), y0, y1, outward, 0f, W / RoofFlatM, 0f, (y1 - y0) / RoofFlatM);
        }

        // ---- HOUSE COLOURS + FRONT DOOR (PSX_CITY_HOUSETINT, PSX_CITY_HOUSEDOOR) ----
        public static bool HouseTintOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSETINT") != "0";
        public static bool HouseDoorOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEDOOR") != "0";

        /// <summary>Siding colours as multipliers of the painted off-white
        /// (linear-ish, x the texel; PSX/Lit x2 the vertex colour): white, cream,
        /// pale yellow, sage, grey-blue, light grey, tan, pale blue, clay, slate green.</summary>
        static readonly Vector3[] SidingCols =
        {
            new Vector3(1.10f, 1.10f, 1.10f), new Vector3(1.06f, 1.02f, 0.90f), new Vector3(1.08f, 1.00f, 0.72f),
            new Vector3(0.84f, 0.94f, 0.80f), new Vector3(0.80f, 0.88f, 1.00f), new Vector3(0.86f, 0.86f, 0.86f),
            new Vector3(1.00f, 0.88f, 0.72f), new Vector3(0.88f, 0.98f, 1.08f), new Vector3(0.86f, 0.72f, 0.60f),
            new Vector3(0.70f, 0.80f, 0.74f),
        };

        static uint HouseHash(int x, int z, uint salt)
        {
            uint h = (uint)x * 0x9E3779B1u ^ (uint)z * 0x85EBCA6Bu ^ salt * 0xC2B2AE35u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }

        /// <summary>A house's siding colour: a neighbourhood (a 350 m cell)
        /// draws from four of the palette's colours - an older street mostly
        /// white and cream, another pastel - and each house picks one of them.</summary>
        static Color32 HouseTintOf(Vector2 c)
        {
            if (!HouseTintOn) return new Color32(128, 128, 128, 0);
            int cx = Mathf.FloorToInt(c.x / 350f), cz = Mathf.FloorToInt(c.y / 350f);
            uint nb = HouseHash(cx, cz, 7u);
            uint hh = HouseHash(Mathf.RoundToInt(c.x * 2f), Mathf.RoundToInt(c.y * 2f), 11u);
            // white is common everywhere (a third of houses); the rest from the cell's four
            int pick = (hh & 7u) < 3u ? 0 : (int)((nb >> (int)(((hh >> 3) & 3u) * 4u)) % (uint)SidingCols.Length);
            var k = SidingCols[pick];
            return new Color32(TintByte(k.x), TintByte(k.y), TintByte(k.z), 0);
        }

        /// <summary>The front door and its stoop on a gabled house's long wall
        /// that faces the nearest street: a glazed door (the siding's own
        /// window cell, whole, 0.95 x 2.05 m, 3 cm proud of the wall) on the bay
        /// line nearest the wall's middle - between two windows, never over
        /// one - and a concrete step under it from the floor to the ground.</summary>
        static void HouseDoor(TileMeshes tm, Vector2 centre, Vector2 u, Vector2 v, float hu, float hv, float y0, float floor)
        {
            if (!HouseDoorOn || hwMap == null || float.IsNaN(floor)) return;
            if (!hwMap.NearestRoadPoint(centre, 60f, true, out int ei, out float s, out _)) return;
            var q = hwMap.edges[ei].PointAt(s);
            float side = Vector2.Dot(q - centre, v) >= 0f ? 1f : -1f;
            float W = 2f * hu;
            int bays = W < HouseMinWinWallM ? 0 : Mathf.Max(1, Mathf.RoundToInt(W / HouseBayM));
            if (bays < 2) return;
            float along = -hu + (bays / 2) * (W / bays);
            Vector2 n = v * side;
            Vector2 mid = centre + u * along + n * (hv + 0.03f);
            Vector2 a = mid - u * 0.475f, c = mid + u * 0.475f;
            var bk = buckets[(int)Slot.FacadeHouse];
            bk.Wall(L(a, 0f, tm), L(c, 0f, tm), floor, floor + 2.05f, n, HouseWinU0, HouseWinU1, HouseWinV0, HouseWinV1);
            // the stoop: floor - 0.02 down to the walls' foot, 1.5 m wide, 1.0 m out
            float top = floor - 0.02f;
            if (top <= y0 + 0.05f) return;
            var st = buckets[(int)Slot.RoofFlat];
            Vector2 s0 = centre + u * (along - 0.75f) + n * hv, s1 = centre + u * (along + 0.75f) + n * hv;
            Vector2 o0 = s0 + n * 1.0f, o1 = s1 + n * 1.0f;
            st.Up(L(s0, top, tm), L(s1, top, tm), L(o1, top, tm), L(o0, top, tm),
                  new Vector2(0f, 0f), new Vector2(0.19f, 0f), new Vector2(0.19f, 0.13f), new Vector2(0f, 0.13f));
            float hgt = (top - y0) / RoofFlatM;
            st.Wall(L(o0, 0f, tm), L(o1, 0f, tm), y0, top, n, 0f, 0.19f, 0f, hgt);
            st.Wall(L(s0, 0f, tm), L(o0, 0f, tm), y0, top, -u, 0f, 0.13f, 0f, hgt);
            st.Wall(L(o1, 0f, tm), L(s1, 0f, tm), y0, top, u, 0f, 0.13f, 0f, hgt);
        }

        const float EaveOverM = 0.35f, RakeOverM = 0.25f;

        /// <summary>The gable roof with its overhangs: the two planes reach
        /// <see cref="EaveOverM"/> past the long walls (down the slope) and
        /// <see cref="RakeOverM"/> past the gable ends, and the overhanging
        /// strips are drawn again facing down (the soffit).</summary>
        static void HouseRoofEaves(TileMeshes tm, Vector2 centre, Vector2 u, Vector2 v, float hu, float hv, float eave, float ridge)
        {
            var roof = buckets[(int)Slot.RoofTiles];
            float slope = (ridge - eave) / Mathf.Max(hv, 0.5f);
            float ex = Mathf.Min(EaveOverM, 0.6f / Mathf.Max(slope, 0.3f));   // never more than 0.6 m drop
            float hU = hu + RakeOverM, hV = hv + ex, yE = eave - ex * slope;
            var rN = centre + u * hU; var rS = centre - u * hU;
            float uRep = Mathf.Max(1f, Mathf.Round(hU * 2f / RoofTileM));
            float run = Mathf.Sqrt(hV * hV + (ridge - yE) * (ridge - yE));
            float vRep = Mathf.Max(1f, Mathf.Round(run / RoofTileM));
            for (int side = -1; side <= 1; side += 2)
            {
                var eN = centre + u * hU + v * (hV * side); var eS = centre - u * hU + v * (hV * side);
                roof.Up(L(rS, ridge, tm), L(rN, ridge, tm), L(eN, yE, tm), L(eS, yE, tm),
                        new Vector2(0f, 0f), new Vector2(uRep, 0f), new Vector2(uRep, vRep), new Vector2(0f, vRep));
                // the soffit: the strip past the wall line, and the rake strips over the gable ends
                var wN = centre + u * hu + v * (hv * side); var wS = centre - u * hu + v * (hv * side);
                float yW = eave;
                float vW = vRep * (1f - ex / hV);
                roof.Down(L(eS, yE, tm), L(eN, yE, tm), L(centre + u * hU + v * (hv * side), yW, tm), L(centre - u * hU + v * (hv * side), yW, tm),
                          new Vector2(0f, vRep), new Vector2(uRep, vRep), new Vector2(uRep, vW), new Vector2(0f, vW));
                for (int end = -1; end <= 1; end += 2)
                {
                    // the rake: from the wall's end line out to the roof's edge, ridge to the wall line
                    var o0 = centre + u * (hu * end) + v * (hv * side); var o1 = centre + u * (hU * end) + v * (hv * side);
                    var r0 = centre + u * (hu * end); var r1 = centre + u * (hU * end);
                    roof.Down(L(r0, ridge, tm), L(r1, ridge, tm), L(o1, yW, tm), L(o0, yW, tm),
                              new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.1f, vW), new Vector2(0f, vW));
                }
            }
        }
    }
}
