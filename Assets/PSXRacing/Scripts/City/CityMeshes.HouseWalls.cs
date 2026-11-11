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
            int storeys = bays == 0 ? 0 : Mathf.Max(0, Mathf.FloorToInt(avail / HouseMinStoreyM + 0.0001f));
            float pitch = storeys > 0 ? Mathf.Min(avail / storeys, 3.3f) : 0f;
            float winH = Mathf.Clamp(pitch * 0.5f, 1.0f, 1.55f);
            float sill = Mathf.Min((pitch - winH - HouseHeadClearM) * 0.6f + 0.15f, 0.95f);
            // the drawn ground along the wall, highest (a sill under it is buried)
            float groundHi = float.NegativeInfinity;
            if (hwMap != null && storeys > 0)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(W / 2f));
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector2.Lerp(a, c, k / (float)n);
                    groundHi = Mathf.Max(groundHi, LatticeY(hwMap, p.x, p.y));
                }
            }
            float y = y0;
            for (int s = 0; s < storeys; s++)
            {
                float ws = floor + s * pitch + sill, we = ws + winH;
                if (we > y1 - HouseHeadClearM + 0.001f) break;
                if (ws < groundHi + 0.1f) { HouseWinGroundDropped += bays; continue; }   // the ground reaches it: plain
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
