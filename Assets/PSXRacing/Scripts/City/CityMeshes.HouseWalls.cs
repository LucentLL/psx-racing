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
            if (HouseUnitWall(tm, a, c, y0, y1, outward)) return;   // ROUND 2 (CityMeshes.HouseParts.cs): a block's wall as units
            float floor = float.IsNaN(houseFloorY) ? y0 + BuildingSink : Mathf.Clamp(houseFloorY, y0, y1);
            int bays = W < HouseMinWinWallM ? 0 : houseUnitInner ? 2 : Mathf.Max(1, Mathf.RoundToInt(W / HouseBayM));
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
            bool foundDone = false;
            // HOUSE MIX: this wall in the house's brick, painted brick or stone; a brick front's
            // return round the corner; a siding wall's brick or stone foundation, its stone water table
            if (hmActive && !skylineBuild && !houseUnitRow && !houseUnitInner)
            {
                HmFront(a, c);
                bool front = hmFrontOk && Vector2.Dot(outward, hmFrontN) > 0.7f;
                bool frontCls = hm.cls == HmBrickFront || hm.cls == HmStoneFront;
                if (hm.cls == HmBrick || hm.cls == HmPainted || (frontCls && front))
                {
                    HmWall(tm, hm.mat, hm.tint, a, c, y0, y1, outward, floor, bays, storeys, pitch, winH, sill, groundHi);
                    return;
                }
                if (frontCls && hmFrontOk && !hmInReturn && W > HmReturnM + 1.0f)
                {
                    Vector2 dir = (c - a) / W;
                    float along = Vector2.Dot(dir, hmFrontN);
                    if (Mathf.Abs(along) > 0.7f)
                    {
                        // a side wall: the front's brick returns round its front corner (an outside
                        // corner only: on a polygon, not where the plan stands in front of it)
                        Vector2 e = along > 0f ? c : a;
                        bool poly = !float.IsNegativeInfinity(facWallTop);
                        if (!poly || !PolyContains(fitPoly, e + hmFrontN * 0.3f - outward * 0.3f))
                        {
                            Vector2 m = e - dir * (Mathf.Sign(along) * HmReturnM);
                            var keepT = Bucket.Tint; Bucket.Tint = hm.tint;
                            if (along > 0f) HmPlain(tm, hm.mat, m, c, y0, y1, outward, floor, 0f);
                            else HmPlain(tm, hm.mat, a, m, y0, y1, outward, floor, 0f);
                            Bucket.Tint = keepT;
                            HouseMixReturns++;
                            hmInReturn = true;
                            try { if (along > 0f) HouseWall(tm, a, m, y0, y1, outward); else HouseWall(tm, m, c, y0, y1, outward); }
                            finally { hmInReturn = false; }
                            return;
                        }
                    }
                }
                float fTop = floor + 0.2f, tTop = floor + sill;
                if (hm.stoneTable && front && storeys > 0 && tTop > y0 + 0.3f && tTop < y1 - 1.5f && tTop > groundHi + 0.15f)
                {
                    // the stone water table: stone from the ground to the ground storey's sills, a ledge on it
                    var keepT = Bucket.Tint; Bucket.Tint = hm.foundTint;
                    HmPlain(tm, HmStone, a, c, y0, tTop, outward, floor, 0f);
                    Bucket.Tint = keepT;
                    HmLedge(tm, a, c, tTop, outward);
                    y = tTop; foundDone = true;
                }
                else if (hm.found >= 0 && HousePartsOn && fTop > y0 + 0.05f && fTop < y1 - 1.5f)
                {
                    var keepT = Bucket.Tint; Bucket.Tint = hm.foundTint;
                    HmPlain(tm, hm.found, a, c, y0, fTop, outward, floor, 0f);
                    Bucket.Tint = keepT;
                    y = fTop; foundDone = true;
                }
            }
            if (houseBrickLook >= 0 && avail > 2.2f && (!houseBrickBase || storeys > 1))
            {
                // BRICK: the atlas's brick column, one whole floor row a storey
                // (whole repeats along, so its windows are whole), the courses
                // under the floor in concrete block; all storeys, or (a block's
                // brick base) the ground storey only
                int stB = Mathf.Max(1, Mathf.FloorToInt(avail / HouseMinStoreyM + 0.0001f));
                int nb = houseBrickBase ? 1 : stB;
                float pb = houseBrickBase ? (HouseUnitsOn ? Mathf.Min(pitch, 2.9f) : pitch) : Mathf.Min(avail / stB, 3.0f);
                if (floor > y0 + 0.01f) HouseFoundation(tm, a, c, y0, floor, outward);
                var keep = Bucket.Tint;
                PickFacade(houseBrickAt, 2, 2, 2, default, floor, pb);
                HouseBrickLookSet(houseBrickAt);
                var lk = FacadeLooks[houseBrickLook];
                // a house's windows: the column's repeat at 0.55 of its width (the atlas's are a block's)
                // (ROUND 2: a unit's two bays, a pier at its middle for its door; a block's base at 0.8)
                float ur = houseUnitInner ? 2f / HouseLookBays(houseBrickLook)
                         : Mathf.Max(1f, Mathf.Round(W / (lk.uM * (houseBrickBase && HouseUnitsOn ? 0.8f : 0.55f))));
                for (int k = 0; k < nb; k++) EmitPanels(tm, Slot.FacadeGlass, a, c, floor + k * pb, floor + (k + 1) * pb, outward, ur, 1f / lk.floors, 0f);
                Bucket.Tint = keep; Bucket.FacOn = false;
                y = floor + nb * pb; s0 = nb;
                if (!houseBrickBase) { if (y1 > y + 0.01f) HousePlain(tm, a, c, y, y1, outward, uPlain, true); return; }   // the frieze over the brick: siding
            }
            // ROUND 2 (PSX_CITY_HOUSEPARTS): a siding house's foundation, concrete to 0.2 m over its floor
            if (HousePartsOn && s0 == 0 && !foundDone && floor + 0.2f > y0 + 0.05f && floor + 0.2f < y1 - 1.5f)
            {
                HouseFoundation(tm, a, c, y0, floor + 0.2f, outward);
                y = floor + 0.2f;
            }
            for (int s = s0; s < storeys; s++)
            {
                float ws = floor + s * pitch + sill, we = ws + winH;
                if (we > y1 - HouseHeadClearM + 0.001f) break;
                if (ws < groundHi + 0.15f) { HouseWinGroundDropped += bays; continue; }   // the ground reaches it: plain
                if (ws > y + 0.01f) HousePlain(tm, a, c, y, ws, outward, uPlain, false);
                EmitPanels(tm, Slot.FacadeHouse, a, c, ws, we, outward, bays, HouseWinV1 - HouseWinV0, HouseWinV0);
                HouseWinLaid += bays;
                HouseShutters(tm, a, c, ws, we, outward, bays);   // ROUND 3 (CityMeshes.HouseParts.cs)
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
            // HOUSE MIX: the house's wall class (its brick is the house brick's bucket, not an atlas column)
            hmActive = false;
            if (HouseMixOn && !houseUnitRow && HmReady())
            {
                hm = HouseMixOf(c); hmAt = c; hmActive = true; hmFrontTried = false; hmFrontOk = false;
                HouseMixCount[hm.cls]++; HouseMixEra[hm.era]++;
                if (hm.stoneTable) HouseMixStone++;
                if (hm.infill) HouseMixInfill++;
                return -1;
            }
            if (!HouseBrickOn || houseUnitRow) return -1;   // (a block of units: its base was decided by HouseBlockBegin)
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
            // ROUND 2 (item 6): stronger, from street views - the round 1 set read as one beige at
            // driving distance; and three darker paints (navy, charcoal, forest) a street has a few of
            // (ROUND 3: stronger again - the A/B frames moved the siding only ~6 levels a channel)
            new Vector3(1.18f, 1.18f, 1.18f), new Vector3(1.12f, 1.04f, 0.78f), new Vector3(1.18f, 1.02f, 0.50f),
            new Vector3(0.72f, 0.92f, 0.62f), new Vector3(0.62f, 0.78f, 1.10f), new Vector3(0.80f, 0.82f, 0.86f),
            new Vector3(1.02f, 0.80f, 0.56f), new Vector3(0.78f, 0.98f, 1.20f), new Vector3(0.90f, 0.50f, 0.40f),
            new Vector3(0.52f, 0.70f, 0.56f), new Vector3(0.42f, 0.50f, 0.74f), new Vector3(0.50f, 0.52f, 0.55f),
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
            // HOUSE MIX: the class's siding / trim colour; past the far plane (the proxies: one
            // siding box a house) the colour its walls read as from there
            if (HouseMixOn && !houseUnitRow && !houseBrickBase && HmReady())
            {
                var m = HouseMixOf(c);
                return skylineBuild ? m.far : m.siding;
            }
            return HousePickTint(c);
        }

        /// <summary>The siding colour by position alone (rounds 1-3).</summary>
        static Color32 HousePickTint(Vector2 c)
        {
            int cx = Mathf.FloorToInt(c.x / 350f), cz = Mathf.FloorToInt(c.y / 350f);
            uint nb = HouseHash(cx, cz, 7u);
            uint hh = HouseHash(Mathf.RoundToInt(c.x * 2f), Mathf.RoundToInt(c.y * 2f), 11u);
            // white is common everywhere (a third of houses); the rest from the cell's four
            // (ROUND 3: every house its own of the twelve - a row of ten reads as ten houses; white one in eight)
            int pick = (hh & 7u) == 0u ? 0 : (int)(((hh >> 3) ^ (nb & 0xFFu)) % (uint)SidingCols.Length);
            var k = SidingCols[pick];
            return new Color32(TintByte(k.x), TintByte(k.y), TintByte(k.z), 0);
        }

        /// <summary>The front door and its stoop on a gabled house's long wall
        /// that faces the nearest street: a glazed door (the siding's own
        /// window cell, whole, 0.95 x 2.05 m, 3 cm proud of the wall) on the bay
        /// line nearest the wall's middle - between two windows, never over
        /// one - and a concrete step under it from the floor to the ground.</summary>
        static void HouseDoor(TileMeshes tm, Vector2 centre, Vector2 u, Vector2 v, float hu, float hv, float y0, float floor, float eave)
        {
            if (!HouseDoorOn || hwMap == null || float.IsNaN(floor) || houseUnitRow) return;   // (a block of units: a door a unit)
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
            // its foot on the floor, or on the drawn ground where a sunk pad's ground stands over the floor
            float foot = floor;
            for (int k = 0; k <= 4; k++) { var p = Vector2.Lerp(a, c, k / 4f); foot = Mathf.Max(foot, LatticeY(hwMap, p.x, p.y) + 0.04f); }
            if (foot + 2.05f > eave - 0.2f) return;
            var dk = Bucket.Tint; if (HousePartsOn) Bucket.Tint = HouseDoorTint(mid);   // ROUND 3: its paint
            bk.Wall(L(a, 0f, tm), L(c, 0f, tm), foot, foot + 2.05f, n, HouseWinU0, HouseWinU1, HouseWinV0, HouseWinV1);
            Bucket.Tint = dk;
            // the stoop: floor - 0.02 down to the walls' foot, 1.5 m wide, 1.0 m out
            // (ROUND 3: none that would reach a street's pavement - the drive audit's court lanes)
            if (!HouseStepOffRoad(mid, n, u, 1.0f, 0.75f)) return;
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

        // =====================================================================
        //  HOUSE MIX (PSX_CITY_HOUSEMIX; the owner, 2026-11-15: "You can also use
        //  brick and stone textures for houses (front face or full) as necessary
        //  for variety", and 11-11 / 11-15: his PBR Bricks pack on houses).
        //
        //  Every house takes a WALL CLASS by rule, steady from visit to visit (a
        //  hash of its centre) and coherent by neighbourhood (an era a 600 m
        //  cell, a brick and two of its colours a 350 m cell):
        //    siding (as before, in its colours), FULL BRICK, BRICK FRONT with
        //    siding on the other faces (the brick returns round the front corners),
        //    PAINTED brick, a STONE FRONT, and stone accents (a stone water table
        //    on the front of a newer siding house, a stone foundation on an old one).
        //  The shares come from the era and district of the spot (distance and
        //  bearing from the Square: the table HmShare and its sources are in
        //  HANDOFF.md, "houses" round of 2026-11-15).
        //
        //  A masonry wall is laid out by the SAME rule as a siding wall (whole
        //  bays, one window band a storey from the floor, none whose head reaches
        //  the eave or whose sill the ground reaches): the window is the siding
        //  texture's own window cell, whole (so the night mask lights it as
        //  before and the windows audit reads it), set between brick piers with a
        //  rowlock course under it (the sill) and over it (the lintel): the brick
        //  texture turned a quarter, half a brick tall. Brick is mapped in world
        //  metres from the wall's start and the house's floor, so its courses run
        //  on round the house.
        //
        //  Buckets: the kit's three facade slots nothing has emitted into since
        //  the facade atlas (FacadeBrick = the house's red brick, FacadeMid = its
        //  old brick, FacadeTower = its stone) and one new slot at the end
        //  (HousePaint = painted brick); their materials take the vertex colour
        //  (_HouseTint) like the siding. PSX_CITY_HOUSEMIX=0: every house as
        //  before (siding, or round 1's atlas brick).
        // =====================================================================
        public static bool HouseMixOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEMIX") != "0";

        public const int HmSiding = 0, HmBrick = 1, HmBrickFront = 2, HmPainted = 3, HmStoneFront = 4, HmClasses = 5;
        public const int HmPrewar = 0, HmMill = 1, HmRanch = 2, Hm1970 = 3, Hm1990 = 4, Hm2010 = 5, HmEras = 6;
        public static readonly string[] HmClassNames = { "siding", "full brick", "brick front", "painted brick", "stone front" };
        public static readonly string[] HmEraNames = { "pre-1940", "mill village", "1945-69", "1970-89", "1990-2009", "2010-" };
        /// <summary>The masonry kinds, their kit slots, the wall one repeat covers (m) and
        /// the stretch of the texture's U a rowlock course shows (no head joint in it).</summary>
        public const int HmNew = 0, HmOld = 1, HmPaint = 2, HmStone = 3;
        public static readonly Slot[] HmSlot = { Slot.FacadeBrick, Slot.FacadeMid, Slot.HousePaint, Slot.FacadeTower };
        static readonly Vector2[] HmRep = { new Vector2(0.76f, 0.88f), new Vector2(1.25f, 1.25f), new Vector2(1.25f, 1.25f), new Vector2(2.0f, 2.0f) };
        static readonly Vector2[] HmRowU = { new Vector2(0.085f, 0.245f), new Vector2(0.02f, 0.12f), new Vector2(0.02f, 0.12f), Vector2.zero };
        /// <summary>A brick front's return round the corner (m): a brick's length and a joint.</summary>
        const float HmReturnM = 0.3f;

        /// <summary>Audit counters (CityAudit.Windows.cs): houses by class and era, stone
        /// water tables, in-town infill houses, brick returns, fronts no street was found for.</summary>
        public static readonly int[] HouseMixCount = new int[HmClasses], HouseMixEra = new int[HmEras];
        public static int HouseMixStone, HouseMixInfill, HouseMixReturns, HouseMixNoFront, HouseMixWalls;

        public struct HouseMix
        {
            public int era, cls;
            /// <summary>The masonry of its walls (its front), -1 siding; its foundation's (-1 concrete); its chimney's.</summary>
            public int mat, found, chim;
            public bool stoneTable, infill;
            public Color32 tint, foundTint, chimTint, siding, far;
        }

        /// <summary>Share of siding, full brick, brick front, painted brick; and of stone
        /// accents (an overlay). Rows: the district / era of HmDistrict.</summary>
        static readonly float[,] HmShare =
        {
            { 0.62f, 0.26f, 0.00f, 0.12f, 0.03f },   // 0 pre-1940 streetcar suburbs (Dilworth, Elizabeth, Plaza Midwood, Wilmore)
            { 0.35f, 0.55f, 0.00f, 0.10f, 0.02f },   // 1 Wesley Heights (most houses brick veneer)
            { 0.25f, 0.50f, 0.00f, 0.25f, 0.05f },   // 2 Myers Park / Eastover 1911-50
            { 0.95f, 0.03f, 0.00f, 0.02f, 0.00f },   // 3 the North Charlotte mill village
            { 0.25f, 0.65f, 0.02f, 0.08f, 0.03f },   // 4 ranch houses 1945-69
            { 0.07f, 0.88f, 0.00f, 0.05f, 0.03f },   // 5 the Beatties Ford belt (McCrorey Heights, Oaklawn Park)
            { 0.45f, 0.45f, 0.07f, 0.03f, 0.03f },   // 6 1970-89
            { 0.37f, 0.33f, 0.27f, 0.03f, 0.08f },   // 7 1990-2009
            { 0.55f, 0.15f, 0.20f, 0.10f, 0.20f },   // 8 2010-
        };

        /// <summary>The brick colours (x the texture): the new red brick's, the old brick's;
        /// the paints of painted brick (white first: more than half of them); the stone's.</summary>
        static readonly Vector3[] HmNewTints = { new Vector3(1f, 1f, 1f), new Vector3(0.80f, 0.74f, 0.74f), new Vector3(0.92f, 0.82f, 0.76f), new Vector3(1.12f, 1.05f, 0.94f) };
        static readonly Vector3[] HmOldTints = { new Vector3(1f, 1f, 1f), new Vector3(0.84f, 0.79f, 0.79f), new Vector3(1.08f, 0.97f, 0.90f), new Vector3(0.92f, 0.86f, 0.82f) };
        static readonly Vector3[] HmPaints =
        {
            new Vector3(1.04f, 1.04f, 1.02f), new Vector3(1.02f, 0.98f, 0.86f), new Vector3(0.86f, 0.87f, 0.88f), new Vector3(0.90f, 0.86f, 0.78f),
            new Vector3(0.80f, 0.86f, 0.76f), new Vector3(0.80f, 0.86f, 0.92f), new Vector3(0.42f, 0.43f, 0.45f),
        };
        static readonly Vector3[] HmStoneTints = { new Vector3(1f, 1f, 1f), new Vector3(1.08f, 1.0f, 0.90f), new Vector3(0.90f, 0.92f, 0.95f) };
        /// <summary>A brick house's trim (its gables' siding): white, cream, light grey, tan, sage.</summary>
        static readonly Vector3[] HmTrims = { new Vector3(1.18f, 1.18f, 1.18f), new Vector3(1.12f, 1.04f, 0.78f), new Vector3(0.86f, 0.87f, 0.88f), new Vector3(1.02f, 0.80f, 0.56f), new Vector3(0.72f, 0.92f, 0.62f) };

        static HouseMix hm, hmCache;
        static Vector2 hmAt, hmCacheAt = new Vector2(float.NaN, 0f), hmUptown = new Vector2(float.NaN, 0f), hmFrontN;
        static bool hmActive, hmFrontTried, hmFrontOk, hmInReturn;

        /// <summary>The Square is known (kept from the tile builds' map: a far proxy has none).</summary>
        static bool HmReady()
        {
            if (hwMap != null) hmUptown = hwMap.uptown;
            return !float.IsNaN(hmUptown.x);
        }

        /// <summary>The Square from the map before any tile is built (the audit's census, the
        /// far proxies' build: hwMap is null there), so a house's class never depends on what
        /// the process built first.</summary>
        public static void HouseMixPrime(CityMap map) { if (map != null) hmUptown = map.uptown; }

        static Color32 HmTint(Vector3 k) => new Color32(TintByte(k.x), TintByte(k.y), TintByte(k.z), 0);
        static Color32 HmTint(Vector3 k, Vector3 m) => new Color32(TintByte(k.x * m.x), TintByte(k.y * m.y), TintByte(k.z * m.z), 0);

        /// <summary>The district / era row (HmShare) of the spot <paramref name="c"/>, by its
        /// distance (km) and bearing (0 north, 90 east) from the Square, and the era of its
        /// subdivision (a 600 m cell).</summary>
        static int HmDistrict(Vector2 c, out int era)
        {
            Vector2 d = c - hmUptown;
            float km = d.magnitude / 1000f;
            float brg = (Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + 360f) % 360f;
            float r = (HouseHash(Mathf.FloorToInt(c.x / 600f), Mathf.FloorToInt(c.y / 600f), 101u) & 0xFFFF) / 65536f;
            bool myers = brg >= 140f && brg < 188f && km >= 3.0f && km < 5.9f;
            if (km < 4.6f || myers)
            {
                era = HmPrewar;
                if (myers) return 2;
                if (brg >= 36f && brg < 72f && km >= 3.3f) { era = HmMill; return 3; }
                if ((brg >= 318f || brg < 4f) && km >= 2.5f) { era = HmRanch; return 5; }
                if (brg >= 268f && brg < 302f && km >= 1.4f && km < 2.7f) return 1;
                return 0;
            }
            era = km < 7.5f ? (r < 0.85f ? HmRanch : r < 0.95f ? Hm1970 : Hm2010)
                : km < 10f ? (r < 0.45f ? HmRanch : r < 0.85f ? Hm1970 : r < 0.95f ? Hm1990 : Hm2010)
                : km < 13f ? (r < 0.10f ? HmRanch : r < 0.55f ? Hm1970 : r < 0.90f ? Hm1990 : Hm2010)
                : km < 17f ? (r < 0.20f ? Hm1970 : r < 0.70f ? Hm1990 : Hm2010)
                : (r < 0.10f ? Hm1970 : r < 0.55f ? Hm1990 : Hm2010);
            return era == HmRanch ? 4 : era == Hm1970 ? 6 : era == Hm1990 ? 7 : 8;
        }

        /// <summary>The wall class of the house centred at <paramref name="c"/> (see the
        /// block above): by position alone, so a tile, its neighbour and its far proxy agree.</summary>
        public static HouseMix HouseMixOf(Vector2 c)
        {
            if (c == hmCacheAt) return hmCache;
            var m = new HouseMix { mat = -1, found = -1 };
            int row = HmDistrict(c, out m.era);
            uint h = HouseHash(Mathf.RoundToInt(c.x * 2f), Mathf.RoundToInt(c.y * 2f), 131u);
            uint h2 = HouseHash(Mathf.RoundToInt(c.x * 2f), Mathf.RoundToInt(c.y * 2f), 139u);
            // in-town infill: one house in twelve of the old districts is a house of today
            if (m.era <= HmRanch && row != 4 && (h2 & 0xFF) < (row == 3 || row == 5 ? 10u : 21u)) { m.era = Hm2010; row = 8; m.infill = true; }
            float r = (h & 0xFFFF) / 65536f, rStone = ((h >> 16) & 0xFF) / 256f, rf = ((h >> 24) & 0xFF) / 256f;
            float sS = HmShare[row, 0], sB = HmShare[row, 1], sF = HmShare[row, 2];
            m.cls = r < sS ? HmSiding : r < sS + sB ? HmBrick : r < sS + sB + sF ? HmBrickFront : HmPainted;
            bool stone = rStone < HmShare[row, 4];
            // the neighbourhood's brick (a 350 m cell): which brick, and two of its colours
            uint nb = HouseHash(Mathf.FloorToInt(c.x / 350f), Mathf.FloorToInt(c.y / 350f), 137u);
            int brick = m.era <= HmMill ? HmOld : m.era == HmRanch ? ((nb & 0xFF) < 102u ? HmOld : HmNew) : m.era == Hm1970 ? ((nb & 0xFF) < 64u ? HmOld : HmNew) : HmNew;
            var pal = brick == HmOld ? HmOldTints : HmNewTints;
            Vector3 bt = pal[(int)(((nb >> 8) + ((h2 >> 8) & 1u)) % (uint)pal.Length)];
            Vector3 st = HmStoneTints[(int)((nb >> 12) % (uint)HmStoneTints.Length)];
            Color32 pick = HousePickTint(c);
            m.siding = pick;
            m.chim = brick; m.chimTint = HmTint(bt);
            // the siding box a far proxy draws, in the colour the walls read as from there
            // (x the siding's off-white: the new brick's red, the old brick's wine-grey)
            Vector3 farBrick = brick == HmOld ? new Vector3(0.74f, 0.58f, 0.55f) : new Vector3(0.80f, 0.42f, 0.34f);
            m.far = pick;
            switch (m.cls)
            {
                case HmSiding:
                    // its foundation: brick on most old houses, on half the later ones; stone where
                    // the house has stone; a newer house's stone is a water table on its front
                    if (stone) { m.found = HmStone; m.foundTint = HmTint(st); m.stoneTable = m.era >= Hm1990; m.chim = HmStone; m.chimTint = m.foundTint; }
                    else if (rf < (m.era <= HmMill ? 0.7f : 0.5f)) { m.found = brick; m.foundTint = HmTint(bt); }
                    break;
                case HmBrick:
                    m.mat = brick; m.tint = HmTint(bt);
                    m.siding = HmTint(HmTrims[(int)((h2 >> 12) % (uint)HmTrims.Length)]);
                    m.far = HmTint(farBrick, bt);
                    if (stone) { m.chim = HmStone; m.chimTint = HmTint(st); }
                    break;
                case HmBrickFront:
                    m.mat = brick; m.tint = HmTint(bt);
                    m.found = brick; m.foundTint = m.tint;
                    // a stone front instead of brick on some of the houses that have stone
                    if (stone && m.era >= Hm1990) { m.cls = HmStoneFront; m.mat = HmStone; m.tint = HmTint(st); m.found = HmStone; m.foundTint = m.tint; m.chim = HmStone; m.chimTint = m.tint; }
                    break;
                default:
                {
                    // painted: white more than half, then cream, greys, sage, a few dark
                    uint pi = (h2 >> 16) & 0xFF;
                    Vector3 pt = HmPaints[pi < 140u ? 0 : pi < 178u ? 1 : pi < 204u ? 2 : pi < 224u ? 3 : pi < 234u ? 4 : pi < 242u ? 5 : 6];
                    m.mat = HmPaint; m.tint = HmTint(pt);
                    m.siding = HmTint(pt, new Vector3(1.1f, 1.1f, 1.12f));
                    m.far = m.siding;
                    m.chim = HmPaint; m.chimTint = m.tint;
                    break;
                }
            }
            hmCache = m; hmCacheAt = c;
            return m;
        }

        /// <summary>The house's front: the direction of its walls (the first wall's line and
        /// its normal) that faces the nearest street within 60 m. None: no front (a brick
        /// front's house stays in siding).</summary>
        static void HmFront(Vector2 a, Vector2 c)
        {
            if (hmFrontTried) return;
            hmFrontTried = true; hmFrontOk = false;
            if (hwMap == null || !hwMap.NearestRoadPoint(hmAt, 60f, true, out int ei, out float s, out _))
            {
                if (hm.cls == HmBrickFront || hm.cls == HmStoneFront) HouseMixNoFront++;
                return;
            }
            Vector2 to = hwMap.edges[ei].PointAt(s) - hmAt;
            if (to.sqrMagnitude < 1f) return;
            to.Normalize();
            Vector2 dir = (c - a).normalized, perp = new Vector2(-dir.y, dir.x);
            float du = Vector2.Dot(to, dir), dv = Vector2.Dot(to, perp);
            hmFrontN = Mathf.Abs(du) > Mathf.Abs(dv) ? dir * Mathf.Sign(du) : perp * Mathf.Sign(dv);
            hmFrontOk = true;
        }

        /// <summary>Plain masonry on the wall p to q from ya to yb: the texture in world
        /// metres, u from p (plus <paramref name="uAt"/>), v from the house's floor.</summary>
        static void HmPlain(TileMeshes tm, int kind, Vector2 p, Vector2 q, float ya, float yb, Vector2 outward, float floor, float uAt)
        {
            float w = Vector2.Distance(p, q);
            if (w < 0.004f || yb <= ya + 0.002f) return;
            Vector2 rep = HmRep[kind];
            buckets[(int)HmSlot[kind]].Wall(L(p, 0f, tm), L(q, 0f, tm), ya, yb, outward,
                uAt / rep.x, (uAt + w) / rep.x, (ya - floor) / rep.y, (yb - floor) / rep.y);
        }

        /// <summary>A rowlock course on the wall p to q from ya to yb: the brick turned a
        /// quarter (the wall's length runs up the texture's courses, its height across a
        /// stretch of one brick with no head joint).</summary>
        static void HmRowlock(TileMeshes tm, int kind, Vector2 p, Vector2 q, float ya, float yb, Vector2 outward, float uAt)
        {
            float w = Vector2.Distance(p, q);
            if (w < 0.004f || yb <= ya + 0.002f) return;
            Vector3 P = L(p, 0f, tm), Q = L(q, 0f, tm);
            float vp = uAt / HmRep[kind].y, vq = (uAt + w) / HmRep[kind].y;
            Vector2 d = new Vector2(Q.x - P.x, Q.z - P.z);
            if (Vector2.Dot(new Vector2(-d.y, d.x), outward) < 0f) { var t = P; P = Q; Q = t; float tv = vp; vp = vq; vq = tv; }
            Vector2 ru = HmRowU[kind];
            buckets[(int)HmSlot[kind]].Quad(new Vector3(P.x, ya, P.z), new Vector3(P.x, yb, P.z), new Vector3(Q.x, yb, Q.z), new Vector3(Q.x, ya, Q.z),
                new Vector2(ru.x, vp), new Vector2(ru.y, vp), new Vector2(ru.y, vq), new Vector2(ru.x, vq));
        }

        /// <summary>A masonry wall, a to c, foot <paramref name="y0"/> to top
        /// <paramref name="y1"/>, laid out as the siding wall is (the caller's bays, storeys,
        /// pitch, window height and sill): plain masonry, and in each storey's band the
        /// siding's window cell, whole, between piers, a rowlock sill under it and a rowlock
        /// lintel over it (a house a street comes within 45 m of; none in stone). The pieces
        /// of one band run 3 mm into the plain masonry over and under them (the same
        /// texels), so no seam opens where their corners meet its edge.</summary>
        static void HmWall(TileMeshes tm, int kind, Color32 tint, Vector2 a, Vector2 c, float y0, float y1, Vector2 outward,
                           float floor, int bays, int storeys, float pitch, float winH, float sill, float groundHi)
        {
            float W = Vector2.Distance(a, c);
            Vector2 dir = (c - a) / W;
            var keep = Bucket.Tint;
            Vector2 ru = HmRowU[kind];
            float hr = (ru.y - ru.x) * HmRep[kind].x;
            bool dress = hr > 0.01f && HouseNearStreet(hmAt);
            const float ov = 0.003f;
            var win = buckets[(int)Slot.FacadeHouse];
            float y = y0;
            HouseMixWalls++;
            for (int s = 0; s < storeys && bays > 0; s++)
            {
                float ws = floor + s * pitch + sill, we = ws + winH;
                if (we > y1 - HouseHeadClearM + 0.001f) break;
                if (ws < groundHi + 0.15f) { HouseWinGroundDropped += bays; continue; }
                bool sillOn = dress && ws - hr > y + 0.02f && ws - hr > groundHi + 0.05f;
                bool lintOn = dress && we + hr < y1 - 0.05f;
                float r0 = sillOn ? ws - hr : ws, r1 = lintOn ? we + hr : we;
                Bucket.Tint = tint;
                bool below = r0 > y + 0.005f, above = y1 > r1 + 0.005f;
                if (below) HmPlain(tm, kind, a, c, y, r0, outward, floor, 0f);
                float lo = below ? r0 - ov : r0, hi = above ? r1 + ov : r1;
                float bw = W / bays, half = bw * (HouseWinU1 - HouseWinU0) * 0.5f, x = 0f;
                for (int i = 0; i < bays; i++)
                {
                    float xm = bw * (i + 0.5f), x0 = xm - half, x1 = xm + half;
                    Vector2 p0 = a + dir * x0, p1 = a + dir * x1;
                    Bucket.Tint = tint;
                    HmPlain(tm, kind, a + dir * x, p0, lo, hi, outward, floor, x);
                    if (sillOn) HmRowlock(tm, kind, p0, p1, lo, ws, outward, x0);
                    else if (ws > lo + 0.002f) HmPlain(tm, kind, p0, p1, lo, ws, outward, floor, x0);
                    if (lintOn) HmRowlock(tm, kind, p0, p1, we, hi, outward, x0);
                    else if (hi > we + 0.002f) HmPlain(tm, kind, p0, p1, we, hi, outward, floor, x0);
                    // the window: the siding's cell, whole, as painted
                    Bucket.Tint = new Color32(128, 128, 128, 0);
                    win.Wall(L(p0, 0f, tm), L(p1, 0f, tm), ws, we, outward, HouseWinU0, HouseWinU1, HouseWinV0, HouseWinV1);
                    x = x1;
                }
                Bucket.Tint = tint;
                HmPlain(tm, kind, a + dir * x, c, lo, hi, outward, floor, x);
                HouseWinLaid += bays;
                y = r1;
            }
            Bucket.Tint = tint;
            if (y1 > y + 0.005f) HmPlain(tm, kind, a, c, y, y1, outward, floor, 0f);
            Bucket.Tint = keep;
        }

        /// <summary>The ledge on a stone water table: a concrete cap 5 cm proud of the wall.</summary>
        static void HmLedge(TileMeshes tm, Vector2 a, Vector2 c, float top, Vector2 outward)
        {
            var st = buckets[(int)Slot.RoofFlat];
            float W = Vector2.Distance(a, c);
            Vector2 o = outward * 0.05f;
            st.Wall(L(a + o, 0f, tm), L(c + o, 0f, tm), top - 0.07f, top, outward, 0f, W / RoofFlatM, 0f, 0.01f);
            st.Up(L(a, top, tm), L(c, top, tm), L(c + o, top, tm), L(a + o, top, tm),
                  new Vector2(0f, 0f), new Vector2(W / RoofFlatM, 0f), new Vector2(W / RoofFlatM, 0.01f), new Vector2(0f, 0.01f));
        }

        /// <summary>The house's chimney in its brick or stone: four faces (see HouseChimney).</summary>
        static void HmChimney(TileMeshes tm, Vector2 c, Vector2 u, Vector2 v, float hu, float hv, float y0, float y1)
        {
            var keep = Bucket.Tint; Bucket.Tint = hm.chimTint;
            Vector2 p0 = c + u * hu + v * hv, p1 = c - u * hu + v * hv, p2 = c - u * hu - v * hv, p3 = c + u * hu - v * hv;
            HmPlain(tm, hm.chim, p1, p0, y0, y1, v, y0, 0f);
            HmPlain(tm, hm.chim, p3, p2, y0, y1, -v, y0, 0f);
            HmPlain(tm, hm.chim, p2, p1, y0, y1, -u, y0, 2f * hu);
            HmPlain(tm, hm.chim, p0, p3, y0, y1, u, y0, 2f * hu);
            Bucket.Tint = keep;
        }
    }
}
