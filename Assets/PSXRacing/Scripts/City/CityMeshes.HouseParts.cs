using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// HOUSES ROUND 2 (2026-11-11, the owner's top priority: "replacing these
    /// very unrealistic generic textures with windows going through roofs").
    /// Round 1 (CityMeshes.HouseWalls.cs) laid the windows out; this gives
    /// every house a pitched roof and the residential blocks their units.
    ///
    /// 1. PSX_CITY_HOUSEROOFS (on): NO FLAT-ROOFED HOUSE. A house polygon the
    ///    exporter did not gable (over 10 corners: 1,074 of them) was drawn
    ///    with a flat roof (EarcutInto); a low residential block of any plan
    ///    too. Now the plan is split into rectangles on its own two axes (its
    ///    corners' coordinates are the grid, so a rectangle's sides are its
    ///    walls), the biggest rectangles first, overlapping where the arms of an
    ///    L or a T cross; each rectangle takes a roof along its long axis -
    ///    a gable or a hip at an outside end, and at an end inside the plan its
    ///    ridge runs on into the other roof (a cross gable). A flat ceiling at
    ///    the eave under it closes what the rectangles miss. The gabled boxes
    ///    take a hip by era and hash (the brick ranches of the 1950s-60s about
    ///    half, the streetcar suburbs a quarter).
    /// 2. PSX_CITY_HOUSEUNITS (on): a residential block drawn in siding (the
    ///    owner's N Graham St condos, OSM ways 589068963-969, and every block
    ///    like them) is a row of UNITS: each wall of 9 m or more divided into
    ///    units of about 6.5 m, a window pair a storey each; on the walls that
    ///    face the street every second unit steps forward 0.5 m (side returns
    ///    in siding) under its own front gable rising over the main eave (an
    ///    attic window in it), and every unit has its door and stoop. Half the
    ///    blocks (by hash) keep the brick ground storey, its windows the atlas
    ///    column's at their own size (two bays a unit), the band 2.9 m at most.
    /// Everything rides the existing buckets (FacadeHouse, FacadeGlass,
    /// RoofTiles, RoofFlat): no new slot, material or draw call.
    /// </summary>
    public static partial class CityMeshes
    {
        public static bool HouseRoofsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEROOFS") != "0";
        public static bool HouseUnitsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEUNITS") != "0";

        /// <summary>Counters for the audit / the cost line (reset by the caller).</summary>
        public static int HouseFlatLeft, HousePolyRoofs, HousePolyRects, HousePolyFallback, HouseHipRoofs, HouseUnitsLaid, HouseCrossGables;

        // ---- the era of the house being emitted (distance from Uptown) ----
        const int EraPrewar = 0, EraRanch = 1, EraModern = 2;
        static int houseEra = EraPrewar;

        /// <summary>Charlotte's house eras by distance from Uptown: the
        /// streetcar suburbs (Dilworth, Plaza Midwood, Elizabeth: under 3.8 km),
        /// the post-war brick ranches (Shamrock, Eastway, Sedgefield: to 8 km),
        /// the subdivisions after 1980 beyond.</summary>
        static int HouseEraOf(Vector2 c)
        {
            if (hwMap == null) return EraModern;
            float d = Vector2.Distance(c, hwMap.uptown);
            return d < 3800f ? EraPrewar : d < 8000f ? EraRanch : EraModern;
        }

        /// <summary>Gable or hip for a house roof: hips on about a quarter of
        /// the old houses (foursquares), half the ranches, a third of the new.</summary>
        static bool HouseHipOf(Vector2 c, int era)
        {
            uint h = HouseHash(Mathf.RoundToInt(c.x * 2f), Mathf.RoundToInt(c.y * 2f), 41u);
            float share = era == EraPrewar ? 0.25f : era == EraRanch ? 0.55f : 0.33f;
            return (h & 0xFFFF) / 65536f < share;
        }

        // =====================================================================
        //  ONE ROOF ON A RECTANGLE
        // =====================================================================
        const int EndGable = 0, EndHip = 1, EndBuried = 2;

        /// <summary>A pitched roof over the rectangle centred <paramref name="c"/>,
        /// ridge along <paramref name="u"/> (half length <paramref name="hu"/>),
        /// half width <paramref name="hv"/>, rising from <paramref name="eave"/>
        /// at <paramref name="slope"/> (rise over run), overhanging the long
        /// walls <paramref name="over"/>. Each end (-u, +u): a GABLE (rake
        /// overhang, the gable triangle in plain siding on the wall line), a
        /// HIP, or BURIED (the ridge runs on <paramref name="extN"/> /
        /// <paramref name="extP"/> into another roof, no triangle).</summary>
        static void HouseRoofRect(TileMeshes tm, Vector2 c, Vector2 u, float hu, float hv, float eave, float slope, float over,
                                  int endN, int endP, float extN, float extP)
        {
            var roof = buckets[(int)Slot.RoofTiles];
            Vector2 v = new Vector2(-u.y, u.x);
            float R = eave + slope * hv, yE = eave - over * slope;
            float kS = Mathf.Sqrt(1f + slope * slope);
            // ridge ends (rN, rP: distance along u from c) and eave-line ends (aN, aP)
            float rN = RidgeEnd(endN, hu, hv, extN), rP = RidgeEnd(endP, hu, hv, extP);
            float aN = EaveEnd(endN, hu, over, extN), aP = EaveEnd(endP, hu, over, extP);
            float vE = (hv + over) * kS / RoofTileM;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 q0 = c - u * rN, q1 = c + u * rP;
                Vector2 e1 = c + u * aP + v * ((hv + over) * side), e0 = c - u * aN + v * ((hv + over) * side);
                roof.Up(L(q0, R, tm), L(q1, R, tm), L(e1, yE, tm), L(e0, yE, tm),
                        new Vector2(-rN / RoofTileM, 0f), new Vector2(rP / RoofTileM, 0f), new Vector2(aP / RoofTileM, vE), new Vector2(-aN / RoofTileM, vE));
                // the soffit under the overhang along the long wall
                if (over > 0.01f)
                {
                    Vector2 w0 = c - u * aN + v * (hv * side), w1 = c + u * aP + v * (hv * side);
                    roof.Down(L(w0, eave, tm), L(w1, eave, tm), L(e1, yE, tm), L(e0, yE, tm),
                              new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.1f, 0.1f), new Vector2(0f, 0.1f));
                }
            }
            for (int end = -1; end <= 1; end += 2)
            {
                int kind = end < 0 ? endN : endP;
                float rE = end < 0 ? rN : rP, aE = end < 0 ? aN : aP;
                Vector2 apex = c + u * (rE * end);
                if (kind == EndHip)
                {
                    // the hip's end: a triangle from the ridge's end down to the end's eave line
                    Vector2 eL = c + u * (aE * end) + v * (hv + over), eR = c + u * (aE * end) - v * (hv + over);
                    float run = Mathf.Max(aE - rE, 0.01f);
                    float vH = Mathf.Sqrt(run * run + (R - yE) * (R - yE)) / RoofTileM;
                    TriFace(roof, L(apex, R, tm), L(eL, yE, tm), L(eR, yE, tm),
                            new Vector2(0f, 0f), new Vector2((hv + over) / RoofTileM, vH), new Vector2(-(hv + over) / RoofTileM, vH), true);
                    if (over > 0.01f)
                    {
                        Vector2 w0 = c + u * (hu * end) + v * hv, w1 = c + u * (hu * end) - v * hv;
                        Vector2 o0 = c + u * ((hu + over) * end) + v * hv, o1 = c + u * ((hu + over) * end) - v * hv;
                        roof.Down(L(w0, eave, tm), L(w1, eave, tm), L(o1, yE, tm), L(o0, yE, tm),
                                  new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.1f, 0.1f), new Vector2(0f, 0.1f));
                    }
                }
                else if (kind == EndGable)
                {
                    // the rake soffits, then the gable triangle on the wall line
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 r0 = c + u * (hu * end), r1 = c + u * (aE * end);
                        Vector2 o0 = r0 + v * (hv * side), o1 = r1 + v * (hv * side);
                        roof.Down(L(r0, R, tm), L(r1, R, tm), L(o1, eave, tm), L(o0, eave, tm),
                                  new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.1f, 0.1f), new Vector2(0f, 0.1f));
                    }
                    Vector2 g0 = c + u * (hu * end) + v * hv, g1 = c + u * (hu * end) - v * hv;
                    EmitGableTri(buckets[(int)Slot.FacadeHouse], tm, g0, c + u * (hu * end), g1, eave, R, u * end);
                }
            }
        }

        static float RidgeEnd(int kind, float hu, float hv, float ext) =>
            kind == EndHip ? Mathf.Max(0f, hu - hv) : kind == EndBuried ? hu + ext : hu + RakeOverM;
        static float EaveEnd(int kind, float hu, float over, float ext) =>
            kind == EndHip ? hu + over : kind == EndBuried ? hu + ext : hu + RakeOverM;

        /// <summary>A triangle facing up (<paramref name="up"/>) or down, whatever its corners' order.</summary>
        static void TriFace(Bucket bk, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, bool up)
        {
            float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
            // Bucket.Tri(a, b, c) emits (a, c, b): it faces up when (a, b, c) has a positive area (as Up)
            if ((area >= 0f) == up) bk.Tri(a, b, c, ua, ub, uc);
            else bk.Tri(a, c, b, ua, uc, ub);
        }

        // =====================================================================
        //  THE GABLED BOX (real house footprints, frontage gables, the fill)
        // =====================================================================
        /// <summary>EmitGableHouse's roof when PSX_CITY_HOUSEROOFS is on: a gable
        /// or a hip (by era and hash; a block of units always a gable) with the
        /// eaves round it.</summary>
        static void HouseBoxRoof(TileMeshes tm, Vector2 centre, Vector2 u, float hu, float hv, float eave, float ridge, float floor)
        {
            houseEra = HouseEraOf(centre);
            float slope = (ridge - eave) / Mathf.Max(hv, 0.5f);
            float ex = Mathf.Min(EaveOverM, 0.6f / Mathf.Max(slope, 0.3f));   // never more than 0.6 m drop (round 1)
            bool hip = houseUnitRow ? hv > 8f : HouseHipOf(centre, houseEra);
            int k = hip ? EndHip : EndGable;
            if (hip) HouseHipRoofs++;
            HouseRoofRect(tm, centre, u, hu, hv, eave, slope, ex, k, k, 0f, 0f);
            if (HousePartsOn && !houseUnitRow && !proxyBuild && hwMap != null && !float.IsNaN(floor))
                HouseBoxParts(tm, centre, u, hu, hv, eave, ridge, floor, hip, slope);
        }

        // =====================================================================
        //  HOUSE PARTS BY ERA (PSX_CITY_HOUSEPARTS)
        // =====================================================================
        public static bool HousePartsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_HOUSEPARTS") != "0";
        public static int HousePorches, HouseChimneys;

        /// <summary>A gabled box's parts: a brick chimney through the roof (most
        /// old houses and ranches, a third of the new), and on the streetcar
        /// suburbs' houses a front porch - a deck on the floor, white posts, a
        /// shed roof under the eave, a step - on the wall that faces the street.
        /// A porch is entered in the tile's house boxes, so the trees keep
        /// their distance from it as from the house.</summary>
        static void HouseBoxParts(TileMeshes tm, Vector2 centre, Vector2 u, float hu, float hv, float eave, float ridge, float floor, bool hip, float slope)
        {
            uint h = HouseHash(Mathf.RoundToInt(centre.x * 2f), Mathf.RoundToInt(centre.y * 2f), 53u);
            Vector2 v = new Vector2(-u.y, u.x);
            // ---- the chimney ----
            float chShare = houseEra == EraModern ? 0.33f : 0.7f;
            if ((h & 0xFF) / 256f < chShare && hu > 2.5f && hv > 2f)
            {
                float endU = (((h >> 8) & 1u) == 0u ? 1f : -1f) * Mathf.Max(0f, hu - 1.3f);
                float sideV = (((h >> 9) & 1u) == 0u ? 1f : -1f) * hv * 0.3f;
                float cw = houseEra == EraRanch ? 1.1f : 0.75f, cd = 0.6f;
                // the roof's height under it (its lowest corner), the top over the ridge
                float lo = float.MaxValue;
                for (int a = -1; a <= 1; a += 2)
                    for (int b = -1; b <= 1; b += 2)
                    {
                        float uu = Mathf.Abs(endU + a * cw * 0.5f), vv = Mathf.Abs(sideV + b * cd * 0.5f);
                        float d = Mathf.Max(0f, hv - vv);
                        if (hip) d = Mathf.Min(d, Mathf.Max(0f, hu - uu));
                        lo = Mathf.Min(lo, eave + slope * d);
                    }
                Vector2 cc = centre + u * endU + v * sideV;
                HouseChimney(tm, cc, u, v, cw * 0.5f, cd * 0.5f, lo - 0.3f, ridge + 0.6f);
                HouseChimneys++;
            }
            // ---- the porch (the streetcar suburbs' houses) ----
            if (houseEra != EraPrewar || ((h >> 12) & 3u) == 0u) return;   // three in four of the old houses
            if (!hwMap.NearestRoadPoint(centre, 60f, true, out int ei, out float s, out _)) return;
            Vector2 toRoad = (hwMap.edges[ei].PointAt(s) - centre).normalized;
            float dU = Vector2.Dot(toRoad, u), dV = Vector2.Dot(toRoad, v);
            Vector2 n, along; float W, off;
            if (Mathf.Abs(dV) >= Mathf.Abs(dU)) { n = v * Mathf.Sign(dV); along = u; W = 2f * hu; off = hv; }
            else { n = u * Mathf.Sign(dU); along = v; W = 2f * hv; off = hu; }
            HousePorch(tm, centre + n * off, n, along, W, eave, floor, Mathf.Abs(dV) >= Mathf.Abs(dU));   // (round 1's door is on the long wall)
        }

        /// <summary>A brick chimney: the atlas's brick column (a pier between its
        /// windows, so no window shows), a concrete cap.</summary>
        static void HouseChimney(TileMeshes tm, Vector2 c, Vector2 u, Vector2 v, float hu, float hv, float y0, float y1)
        {
            var keepTint = Bucket.Tint; int keepLook = houseBrickLook;
            houseBrickLook = HouseBrickLooks[(int)(FacadeHash(c) % (uint)HouseBrickLooks.Length)];
            PickFacade(c, 2, 2, 2, default, y0, 3f);
            HouseBrickLookSet(c);
            Bucket.FacOn = false;
            var lk = FacadeLooks[houseBrickLook];
            int bays = HouseLookBays(houseBrickLook);
            float pu = 1f / bays, du = 0.02f, dv = (y1 - y0) / (3.0f * lk.floors);   // one floor row (1/floors) is a 3 m storey on a house
            HouseBrickBox(tm, c, u, v, hu, hv, y0, y1, pu - du, pu + du, 0.02f, 0.02f + dv);
            Bucket.Tint = keepTint; houseBrickLook = keepLook;
            var st = buckets[(int)Slot.RoofFlat];
            Vector2 p0 = c + u * (hu + 0.05f) + v * (hv + 0.05f), p1 = c - u * (hu + 0.05f) + v * (hv + 0.05f);
            Vector2 p2 = c - u * (hu + 0.05f) - v * (hv + 0.05f), p3 = c + u * (hu + 0.05f) - v * (hv + 0.05f);
            st.Up(L(p0, y1 + 0.08f, tm), L(p1, y1 + 0.08f, tm), L(p2, y1 + 0.08f, tm), L(p3, y1 + 0.08f, tm),
                  new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.1f, 0.1f), new Vector2(0f, 0.1f));
            st.Wall(L(p0, 0f, tm), L(p1, 0f, tm), y1, y1 + 0.08f, v, 0f, 0.1f, 0f, 0.01f);
            st.Wall(L(p2, 0f, tm), L(p3, 0f, tm), y1, y1 + 0.08f, -v, 0f, 0.1f, 0f, 0.01f);
            st.Wall(L(p1, 0f, tm), L(p2, 0f, tm), y1, y1 + 0.08f, -u, 0f, 0.1f, 0f, 0.01f);
            st.Wall(L(p3, 0f, tm), L(p0, 0f, tm), y1, y1 + 0.08f, u, 0f, 0.1f, 0f, 0.01f);
        }

        /// <summary>A box's four walls on the facade atlas's bucket (the tint and
        /// column already set), u0..u1 and v0..v1 of the column on every face.</summary>
        static void HouseBrickBox(TileMeshes tm, Vector2 c, Vector2 u, Vector2 v, float hu, float hv, float y0, float y1, float u0, float u1, float v0, float v1)
        {
            var bk = buckets[(int)Slot.FacadeGlass];
            Vector2 p0 = c + u * hu + v * hv, p1 = c - u * hu + v * hv, p2 = c - u * hu - v * hv, p3 = c + u * hu - v * hv;
            bk.Wall(L(p1, 0f, tm), L(p0, 0f, tm), y0, y1, v, u0, u1, v0, v1);
            bk.Wall(L(p3, 0f, tm), L(p2, 0f, tm), y0, y1, -v, u0, u1, v0, v1);
            bk.Wall(L(p2, 0f, tm), L(p1, 0f, tm), y0, y1, -u, u0, u1, v0, v1);
            bk.Wall(L(p0, 0f, tm), L(p3, 0f, tm), y0, y1, u, u0, u1, v0, v1);
        }

        /// <summary>The front porch on the wall through <paramref name="m"/>
        /// (facing <paramref name="n"/>, <paramref name="W"/> long): a deck on
        /// the floor 2.2 m deep, white posts, a shed roof meeting the wall
        /// between the ground storey's window heads and the next storey's sills,
        /// a step at its front. None where that band is too tight or the floor
        /// stands more than 1.4 m over the ground.</summary>
        static void HousePorch(TileMeshes tm, Vector2 m, Vector2 n, Vector2 along, float W, float eave, float floor, bool hasDoor)
        {
            const float depth = 2.2f, pSlope = 0.25f, post = 0.09f;
            float pw = Mathf.Min(W - 0.6f, 9f);
            if (pw < 3f) return;
            // the ground under it
            float gLo = float.MaxValue;
            for (int i = 0; i <= 2; i++)
                for (int j = 0; j <= 1; j++)
                {
                    var p = m + along * (pw * (i * 0.5f - 0.5f)) + n * (depth * j + 0.1f);
                    gLo = Mathf.Min(gLo, LatticeY(hwMap, p.x, p.y));
                }
            if (floor - gLo > 1.4f || floor < gLo - 0.3f) return;
            // the band its roof meets the wall in (HouseWall's storeys, as laid)
            float avail = eave - floor;
            int storeys = Mathf.Max(1, Mathf.FloorToInt(avail / houseStoreyM + 0.0001f));
            float pitch = Mathf.Min(avail / storeys, houseStoreyM > HouseMinStoreyM ? 3.4f : 3.3f);
            float winH = Mathf.Clamp(pitch * 0.5f, 1.0f, 1.55f);
            float sill = Mathf.Min((pitch - winH - HouseHeadClearM) * 0.6f + 0.15f, 0.95f);
            float bandLo = floor + sill + winH + 0.12f, bandHi = storeys > 1 ? floor + pitch + sill - 0.1f : eave - 0.1f;
            float hW = Mathf.Min(bandHi, floor + 3.1f);
            float hF = hW - pSlope * (depth + 0.25f);
            if (hW < bandLo || hF < floor + 2.25f) return;
            var st = buckets[(int)Slot.RoofFlat];
            Vector2 a0 = m - along * (pw * 0.5f), a1 = m + along * (pw * 0.5f);
            Vector2 f0 = a0 + n * depth, f1 = a1 + n * depth;
            float yDeck = floor + 0.01f, yFoot = gLo - 0.15f;   // (over the door's own stoop, which it hides)
            // the deck and its skirt (concrete), on the floor
            st.Up(L(a0, yDeck, tm), L(a1, yDeck, tm), L(f1, yDeck, tm), L(f0, yDeck, tm),
                  new Vector2(0f, 0f), new Vector2(pw / RoofFlatM, 0f), new Vector2(pw / RoofFlatM, depth / RoofFlatM), new Vector2(0f, depth / RoofFlatM));
            float hS = (yDeck - yFoot) / RoofFlatM;
            st.Wall(L(f0, 0f, tm), L(f1, 0f, tm), yFoot, yDeck, n, 0f, pw / RoofFlatM, 0f, hS);
            st.Wall(L(a0, 0f, tm), L(f0, 0f, tm), yFoot, yDeck, -along, 0f, depth / RoofFlatM, 0f, hS);
            st.Wall(L(f1, 0f, tm), L(a1, 0f, tm), yFoot, yDeck, along, 0f, depth / RoofFlatM, 0f, hS);
            // the posts: white, the siding's plain rows
            var keep = Bucket.Tint;
            Bucket.Tint = new Color32(TintByte(1.12f), TintByte(1.12f), TintByte(1.1f), 0);
            var sb = buckets[(int)Slot.FacadeHouse];
            int np = pw > 6f ? 4 : 3;
            for (int k = 0; k < np; k++)
            {
                Vector2 pc = Vector2.Lerp(a0 + along * 0.2f, a1 - along * 0.2f, k / (float)(np - 1)) + n * (depth - 0.2f);
                float top = hW - pSlope * (depth - 0.2f);
                Vector2 q0 = pc + along * post + n * post, q1 = pc - along * post + n * post, q2 = pc - along * post - n * post, q3 = pc + along * post - n * post;
                sb.Wall(L(q1, 0f, tm), L(q0, 0f, tm), yDeck, top, n, 0f, 0.03f, HousePlainV0, HousePlainV0 + 0.4f);
                sb.Wall(L(q3, 0f, tm), L(q2, 0f, tm), yDeck, top, -n, 0f, 0.03f, HousePlainV0, HousePlainV0 + 0.4f);
                sb.Wall(L(q2, 0f, tm), L(q1, 0f, tm), yDeck, top, -along, 0f, 0.03f, HousePlainV0, HousePlainV0 + 0.4f);
                sb.Wall(L(q0, 0f, tm), L(q3, 0f, tm), yDeck, top, along, 0f, 0.03f, HousePlainV0, HousePlainV0 + 0.4f);
            }
            // a beam over the posts (the same white), under the roof's edge
            {
                float yb = hW - pSlope * (depth - 0.2f);
                Vector2 b0 = a0 + n * (depth - 0.2f + post + 0.01f), b1 = a1 + n * (depth - 0.2f + post + 0.01f);
                sb.Wall(L(b0, 0f, tm), L(b1, 0f, tm), yb - 0.25f, yb, n, 0f, pw / HouseRepU, HousePlainV0, HousePlainV0 + 0.08f);
            }
            Bucket.Tint = keep;
            // the shed roof (and its underside), 0.25 m past the posts and the deck's ends
            var roof = buckets[(int)Slot.RoofTiles];
            Vector2 r0 = a0 - along * 0.25f, r1 = a1 + along * 0.25f;
            Vector2 e0 = r0 + n * (depth + 0.25f), e1 = r1 + n * (depth + 0.25f);
            float vr = (depth + 0.25f) * 1.03f / RoofTileM, ur = (pw + 0.5f) / RoofTileM;
            roof.Up(L(r0, hW, tm), L(r1, hW, tm), L(e1, hF, tm), L(e0, hF, tm),
                    new Vector2(0f, 0f), new Vector2(ur, 0f), new Vector2(ur, vr), new Vector2(0f, vr));
            roof.Down(L(r0, hW - 0.01f, tm), L(r1, hW - 0.01f, tm), L(e1, hF - 0.01f, tm), L(e0, hF - 0.01f, tm),
                      new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.1f, 0.1f), new Vector2(0f, 0.1f));
            // its step at the middle of the front, and (on a gable end) its door
            HouseStoop(tm, m + n * depth, n, along, yDeck, yFoot);
            if (!hasDoor)
            {
                int bays = W < HouseMinWinWallM ? 0 : Mathf.Max(1, Mathf.RoundToInt(W / HouseBayM));
                if (bays >= 2) HouseDoorAt(tm, m + along * (-W * 0.5f + (bays / 2) * (W / bays)), n, along, yFoot, floor, eave);
            }
            tm.houseBoxes.Add((m + n * (depth * 0.5f), along, pw * 0.5f + 0.25f, depth * 0.5f + 0.1f));
            HousePorches++;
        }

        /// <summary>A concrete step 1.5 m wide, 1.0 m out from <paramref name="mid"/>, its top at <paramref name="top"/>.</summary>
        static void HouseStoop(TileMeshes tm, Vector2 mid, Vector2 n, Vector2 along, float top, float y0)
        {
            if (top <= y0 + 0.05f) return;
            var st = buckets[(int)Slot.RoofFlat];
            Vector2 s0 = mid - along * 0.75f, s1 = mid + along * 0.75f;
            Vector2 o0 = s0 + n * 1.0f, o1 = s1 + n * 1.0f;
            st.Up(L(s0, top, tm), L(s1, top, tm), L(o1, top, tm), L(o0, top, tm),
                  new Vector2(0f, 0f), new Vector2(0.19f, 0f), new Vector2(0.19f, 0.13f), new Vector2(0f, 0.13f));
            float hgt = (top - y0) / RoofFlatM;
            st.Wall(L(o0, 0f, tm), L(o1, 0f, tm), y0, top, n, 0f, 0.19f, 0f, hgt);
            st.Wall(L(s0, 0f, tm), L(o0, 0f, tm), y0, top, -along, 0f, 0.13f, 0f, hgt);
            st.Wall(L(o1, 0f, tm), L(s1, 0f, tm), y0, top, along, 0f, 0.13f, 0f, hgt);
        }

        // =====================================================================
        //  THE HOUSE POLYGON (over 10 corners) AND THE LOW BLOCK OF ANY PLAN
        // =====================================================================
        /// <summary>Does this footprint take a pitched roof on the polygon path
        /// (a house, or a block drawn in siding)?</summary>
        static bool HousePolyPitched(CityMap.Footprint f, bool block, bool polySiding) =>
            HouseRoofsOn && !skylineBuild && f.style == 3 && f.roof == 0 && (!block || polySiding);

        /// <summary>The eave of a pitched house polygon (the gabled box's
        /// proportion: 0.68 of its height, a block's 0.76), and the roof's
        /// slope for its era, the rise kept under 3.5 m (a block's 4.5).</summary>
        static float HousePolyEave(CityMap.Footprint f, float floor, bool block)
        {
            houseEra = HouseEraOf(f.centre);
            // the plan's rectangles first (fitPoly: the plan as cut), so the slope
            // and a block's depth come from its widest arm, not its box
            HpDecompose(f, fitPoly);
            float hvMax = 1f;
            foreach (var r in hpRects) hvMax = Mathf.Max(hvMax, Mathf.Min(r.y - r.x, r.w - r.z) * 0.5f);
            if (hpRects.Count == 0) hvMax = Mathf.Max(f.hv, 1f);
            float s = block ? 0.5f : houseEra == EraRanch ? 0.38f : houseEra == EraPrewar ? 0.55f : 0.5f;
            houseMainSlope = Mathf.Min(s, (block ? 4.5f : 3.5f) / hvMax);
            hpHalfDepth = hvMax;
            return floor + Mathf.Max(2.4f, f.h * (block ? 0.76f : 0.68f));
        }

        /// <summary>The rectangles (s0, s1, t0, t1 on the footprint's axes from its centre) of the plan being emitted.</summary>
        static readonly List<Vector4> hpRects = new List<Vector4>(6);
        static float hpHalfDepth = 6f;

        /// <summary>Split <paramref name="poly"/> into rectangles (see the class) into <see cref="hpRects"/>.</summary>
        static void HpDecompose(CityMap.Footprint f, List<Vector2> poly)
        {
            hpRects.Clear();
            Vector2 o = f.centre, u = f.u.sqrMagnitude > 0.5f ? f.u.normalized : Vector2.right, v = new Vector2(-u.y, u.x);
            int n = poly.Count;
            if (n < 3) return;
            if (hpPts.Length < n) hpPts = new Vector2[n * 2];
            for (int k = 0; k < n; k++) { var d = poly[k] - o; hpPts[k] = new Vector2(Vector2.Dot(d, u), Vector2.Dot(d, v)); }
            float[] tols = { 0.5f, 1.0f, 1.6f, 2.5f };
            foreach (float tol in tols)
            {
                HpLines(n, true, tol, hpS); HpLines(n, false, tol, hpT);
                if (hpS.Count <= HpMaxLines && hpT.Count <= HpMaxLines) break;
            }
            int nI = hpS.Count - 1, nJ = hpT.Count - 1;
            if (nI < 1 || nJ < 1 || nI > HpMaxLines || nJ > HpMaxLines) return;
            float total = 0f;
            for (int i = 0; i < nI; i++)
                for (int j = 0; j < nJ; j++)
                {
                    var m = new Vector2((hpS[i] + hpS[i + 1]) * 0.5f, (hpT[j] + hpT[j + 1]) * 0.5f);
                    bool inside = HpInside(n, m);
                    hpIn[i * nJ + j] = inside; hpCov[i * nJ + j] = false;
                    if (inside) total += (hpS[i + 1] - hpS[i]) * (hpT[j + 1] - hpT[j]);
                }
            for (int pick = 0; pick < 5; pick++)
            {
                HpPrefix(nI, nJ);
                float best = 0f; int b0 = 0, b1 = 0, c0 = 0, c1 = 0;
                for (int i0 = 0; i0 < nI; i0++)
                    for (int i1 = i0 + 1; i1 <= nI; i1++)
                    {
                        float ds = hpS[i1] - hpS[i0];
                        if (ds < HpMinRectM) continue;
                        for (int j0 = 0; j0 < nJ; j0++)
                            for (int j1 = j0 + 1; j1 <= nJ; j1++)
                            {
                                if (HpSumIn(i0, i1, j0, j1, nJ) != (i1 - i0) * (j1 - j0)) break;   // a wider j1 only adds outside cells
                                float dt = hpT[j1] - hpT[j0];
                                if (dt < HpMinRectM) continue;
                                float sc = HpSumUnc(i0, i1, j0, j1, nJ) + 0.01f * ds * dt;
                                if (sc > best) { best = sc; b0 = i0; b1 = i1; c0 = j0; c1 = j1; }
                            }
                    }
                if (best <= 0f || (best < Mathf.Max(3f, 0.05f * total) && hpRects.Count > 0)) break;
                for (int i = b0; i < b1; i++) for (int j = c0; j < c1; j++) hpCov[i * nJ + j] = true;
                hpRects.Add(new Vector4(hpS[b0], hpS[b1], hpT[c0], hpT[c1]));
            }
        }

        static readonly List<float> hpS = new List<float>(24), hpT = new List<float>(24), hpVals = new List<float>(64);
        static readonly List<Vector2> hpPoly = new List<Vector2>(64);
        static Vector2[] hpPts = new Vector2[64];
        static bool[] hpIn = new bool[256], hpCov = new bool[256];
        static float[] hpPre = new float[289];
        static int[] hpPreIn = new int[289];
        const int HpMaxLines = 15;
        const float HpMinRectM = 2.0f;

        /// <summary>The pitched roof over a house polygon (see the class).</summary>
        static void HousePolyRoof(TileMeshes tm, CityMap.Footprint f, List<Vector2> poly, float eave, bool block)
        {
            HousePolyRoofs++;
            Vector2 o = f.centre, u = f.u.sqrMagnitude > 0.5f ? f.u.normalized : Vector2.right, v = new Vector2(-u.y, u.x);
            float slope = houseMainSlope;
            float over = Mathf.Min(EaveOverM, 0.6f / Mathf.Max(slope, 0.3f));
            // the flat ceiling at the eave: closes what the roofs leave (an angled wall's corner)
            hpPoly.Clear(); hpPoly.AddRange(poly);
            EarcutInto(buckets[(int)Slot.RoofFlat], hpPoly, eave - 0.02f, tm.origin, RoofFlatM);
            var tintKeep = Bucket.Tint;
            Bucket.Tint = HouseTintOf(f.centre);   // the gable triangles' siding
            bool hip = !block && HouseHipOf(f.centre, houseEra);
            int mainKind = hip ? EndHip : EndGable;
            if (hip) HouseHipRoofs++;
            int rects = 0;
            foreach (var r in hpRects)   // (HousePolyEave split the plan)
            {
                HouseRectRoof(tm, poly, o, u, v, r.x, r.y, r.z, r.w, eave, slope, over, mainKind, rects == 0);
                rects++;
            }
            if (rects == 0)
            {
                // no rectangle 2 m wide fits: a hip over the plan's box (a tiny or a ragged plan)
                HousePolyFallback++;
                HouseRoofRect(tm, o, u, Mathf.Max(f.hu, 0.5f), Mathf.Max(f.hv, 0.5f), eave, slope, over, EndHip, EndHip, 0f, 0f);
            }
            HousePolyRects += rects;
            Bucket.Tint = tintKeep;
        }

        /// <summary>One rectangle (s0..s1 along u, t0..t1 along v) of a house
        /// polygon: its roof along its long axis; an end outside the plan is the
        /// house's gable or hip, an end inside runs on into the neighbour's roof
        /// by its half width (a cross gable) where the plan holds it, else a hip.</summary>
        static void HouseRectRoof(TileMeshes tm, List<Vector2> poly, Vector2 o, Vector2 u, Vector2 v,
                                  float s0, float s1, float t0, float t1, float eave, float slope, float over, int mainKind, bool main)
        {
            float ds = s1 - s0, dt = t1 - t0;
            Vector2 c = o + u * ((s0 + s1) * 0.5f) + v * ((t0 + t1) * 0.5f);
            Vector2 ru = ds >= dt ? u : v, rv = new Vector2(-ru.y, ru.x);
            float hu = Mathf.Max(ds, dt) * 0.5f, hv = Mathf.Min(ds, dt) * 0.5f;
            int kN = mainKind, kP = mainKind; float xN = 0f, xP = 0f;
            for (int end = -1; end <= 1; end += 2)
            {
                Vector2 p = c + ru * (hu * end);
                bool outside = true;
                for (int k = -1; k <= 1 && outside; k++)
                    outside = !PolyContains(poly, p + ru * (0.4f * end) + rv * (hv * 0.6f * k));
                if (outside)
                {
                    // an outside end another rectangle's roof covers (the corner of an L): a hip, so the
                    // two roofs meet in a hip and a valley, never a gable standing in the other roof
                    Vector2 q = p - ru * (0.3f * end) - o;
                    float qs = Vector2.Dot(q, u), qt = Vector2.Dot(q, v);
                    foreach (var r in hpRects)
                        if (qs > r.x + 0.05f && qs < r.y - 0.05f && qt > r.z + 0.05f && qt < r.w - 0.05f &&
                            !(Mathf.Abs(r.x - s0) < 0.01f && Mathf.Abs(r.y - s1) < 0.01f && Mathf.Abs(r.z - t0) < 0.01f && Mathf.Abs(r.w - t1) < 0.01f))
                        { if (end < 0) kN = EndHip; else kP = EndHip; break; }
                    continue;
                }
                // inside the plan: a cross gable buried in the neighbour (where it is under a roof), else a hip
                bool held = PolyContains(poly, p + ru * (hv * end));
                if (end < 0) { kN = held ? EndBuried : EndHip; xN = held ? hv : 0f; }
                else { kP = held ? EndBuried : EndHip; xP = held ? hv : 0f; }
            }
            HouseRoofRect(tm, c, ru, hu, hv, eave, slope, over, kN, kP, xN, xP);
        }

        static bool HpInside(int n, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
                if ((hpPts[i].y > q.y) != (hpPts[j].y > q.y) &&
                    q.x < (hpPts[j].x - hpPts[i].x) * (q.y - hpPts[i].y) / (hpPts[j].y - hpPts[i].y) + hpPts[i].x) inside = !inside;
            return inside;
        }

        /// <summary>The grid lines on one axis: the corners' coordinates,
        /// sorted, each run within <paramref name="tol"/> merged to its mean
        /// (the outermost kept exact).</summary>
        static void HpLines(int n, bool sAxis, float tol, List<float> outL)
        {
            hpVals.Clear();
            for (int k = 0; k < n; k++) hpVals.Add(sAxis ? hpPts[k].x : hpPts[k].y);
            hpVals.Sort();
            outL.Clear();
            int i = 0;
            while (i < hpVals.Count)
            {
                int j = i; float sum = 0f;
                while (j < hpVals.Count && hpVals[j] - hpVals[i] <= tol) { sum += hpVals[j]; j++; }
                outL.Add(sum / (j - i));
                i = j;
            }
            if (outL.Count > 0) { outL[0] = hpVals[0]; outL[outL.Count - 1] = hpVals[hpVals.Count - 1]; }
        }

        static void HpPrefix(int nI, int nJ)
        {
            int W = nJ + 1;
            for (int i = 0; i <= nI; i++) { hpPre[i * W] = 0f; hpPreIn[i * W] = 0; }
            for (int j = 0; j <= nJ; j++) { hpPre[j] = 0f; hpPreIn[j] = 0; }
            for (int i = 1; i <= nI; i++)
                for (int j = 1; j <= nJ; j++)
                {
                    int cI = (i - 1) * nJ + (j - 1);
                    float a = hpIn[cI] && !hpCov[cI] ? (hpS[i] - hpS[i - 1]) * (hpT[j] - hpT[j - 1]) : 0f;
                    hpPre[i * W + j] = a + hpPre[(i - 1) * W + j] + hpPre[i * W + j - 1] - hpPre[(i - 1) * W + j - 1];
                    hpPreIn[i * W + j] = (hpIn[cI] ? 1 : 0) + hpPreIn[(i - 1) * W + j] + hpPreIn[i * W + j - 1] - hpPreIn[(i - 1) * W + j - 1];
                }
        }
        static float HpSumUnc(int i0, int i1, int j0, int j1, int nJ)
        {
            int W = nJ + 1;
            return hpPre[i1 * W + j1] - hpPre[i0 * W + j1] - hpPre[i1 * W + j0] + hpPre[i0 * W + j0];
        }
        static int HpSumIn(int i0, int i1, int j0, int j1, int nJ)
        {
            int W = nJ + 1;
            return hpPreIn[i1 * W + j1] - hpPreIn[i0 * W + j1] - hpPreIn[i1 * W + j0] + hpPreIn[i0 * W + j0];
        }

        // =====================================================================
        //  UNIT ROWS (PSX_CITY_HOUSEUNITS)
        // =====================================================================
        /// <summary>The block being emitted is a row of units; the unit's own
        /// walls are being laid (no second split).</summary>
        static bool houseUnitRow, houseUnitInner;
        /// <summary>The nearest street's point (houseStreetOk), the main roof's
        /// slope, the block's half depth.</summary>
        static Vector2 houseStreetPt;
        static bool houseStreetOk;
        static float houseMainSlope = 0.45f, houseBlockHalfDepth = 6f;
        const float UnitM = 6.5f, UnitStepM = 0.5f, UnitMinWallM = 9f;

        /// <summary>A residential block drawn in siding begins: its brick base
        /// (half the blocks by hash; every one with the switch off, as round 1),
        /// its storeys, its street, its units.</summary>
        static void HouseBlockBegin(CityMap.Footprint f, Vector2 centre, float slope, float halfDepth = -1f)
        {
            bool brick = !HouseUnitsOn || ((FacadeHash(centre) >> 3) & 1u) == 0u;
            houseBrickLook = brick ? HouseBrickLooks[(int)(FacadeHash(centre) % (uint)HouseBrickLooks.Length)] : -1;
            houseBrickBase = brick; houseStoreyM = 2.8f; houseBrickAt = centre;
            houseUnitRow = HouseUnitsOn && !skylineBuild && !proxyBuild;
            houseBlockHalfDepth = Mathf.Max(2f, halfDepth > 0f ? halfDepth : f.hv);
            houseMainSlope = slope;
            houseStreetOk = false;
            if (houseUnitRow && hwMap != null && hwMap.NearestRoadPoint(centre, 90f, true, out int ei, out float s, out _))
            {
                houseStreetPt = hwMap.edges[ei].PointAt(s);
                houseStreetOk = true;
            }
        }

        static void HouseBlockEnd() { houseUnitRow = false; houseStreetOk = false; houseUnitInner = false; }

        /// <summary>A block's wall laid as units (see the class); false: not a
        /// unit wall (too short, not a block), the caller lays it whole.</summary>
        static bool HouseUnitWall(TileMeshes tm, Vector2 a, Vector2 c, float y0, float y1, Vector2 outward)
        {
            if (!houseUnitRow || houseUnitInner) return false;
            float W = Vector2.Distance(a, c);
            if (W < UnitMinWallM || y1 - y0 < 4f) return false;
            Vector2 dir = (c - a) / W, mid = (a + c) * 0.5f;
            // a street wall: a street within 35 m in front of it (each wall its own: a corner block has two)
            bool street;
            if (hwMap != null && hwMap.NearestRoadPoint(mid + outward * 2f, 35f, true, out int sei, out float ss, out _))
                street = Vector2.Dot(outward, (hwMap.edges[sei].PointAt(ss) - mid).normalized) > 0.5f;
            else street = houseStreetOk && Vector2.Dot(outward, (houseStreetPt - mid).normalized) > 0.35f;
            int n = Mathf.Max(2, Mathf.RoundToInt(W / UnitM));
            float wu = W / n;
            houseUnitInner = true;
            try
            {
                for (int k = 0; k < n; k++)
                {
                    Vector2 ak = a + dir * (wu * k), ck = a + dir * (wu * (k + 1));
                    bool step = street && (k & 1) == 1;
                    Vector2 off = step ? outward * UnitStepM : Vector2.zero;
                    HouseWall(tm, ak + off, ck + off, y0, y1, outward);
                    HouseUnitsLaid++;
                    if (step)
                    {
                        float ur = UnitStepM / HouseRepU;
                        HousePlain(tm, ak, ak + off, y0, y1, -dir, ur, true);
                        HousePlain(tm, ck, ck + off, y0, y1, dir, ur, true);
                        HouseCrossGable(tm, ak + off, ck + off, outward, y1);
                    }
                    if (street) HouseDoorAt(tm, (ak + ck) * 0.5f + off, outward, dir, y0, houseFloorY, y1);
                }
            }
            finally { houseUnitInner = false; }
            return true;
        }

        /// <summary>A unit's front gable: its triangle on the unit's (stepped)
        /// front from the main eave, its two roof planes running back until the
        /// main roof swallows the ridge, an attic window in the triangle.</summary>
        static void HouseCrossGable(TileMeshes tm, Vector2 a, Vector2 c, Vector2 outward, float eave)
        {
            float wu = Vector2.Distance(a, c), hv = wu * 0.5f;
            float sMain = Mathf.Max(houseMainSlope, 0.15f);
            float sU = Mathf.Min(0.7f, 0.9f * sMain * houseBlockHalfDepth / hv);
            if (sU < 0.25f) return;
            float rise = sU * hv;
            // back from the stepped front: through the step, to where the main roof stands over the ridge
            float back = Mathf.Min(rise / sMain + UnitStepM + 0.3f, houseBlockHalfDepth + UnitStepM);
            Vector2 mid = (a + c) * 0.5f;
            Vector2 cen = mid - outward * (back * 0.5f);
            HouseRoofRect(tm, cen, outward, back * 0.5f, hv, eave, sU, 0.3f, EndBuried, EndGable, 0f, 0f);
            HouseCrossGables++;
            // the attic window (the siding's window cell, whole) in the triangle
            if (rise > 1.5f)
            {
                var bk = buckets[(int)Slot.FacadeHouse];
                Vector2 dir = (c - a) / wu;
                float wy0 = eave + 0.3f, wy1 = Mathf.Min(eave + rise * 0.62f, wy0 + 0.9f);
                float half = Mathf.Min(0.45f, (rise - (wy1 - eave)) / sU * 0.9f);   // inside the slopes at its head
                if (half > 0.25f && wy1 > wy0 + 0.4f)
                {
                    Vector2 p = mid + outward * 0.03f;
                    bk.Wall(L(p - dir * half, 0f, tm), L(p + dir * half, 0f, tm), wy0, wy1, outward, HouseWinU0, HouseWinU1, HouseWinV0, HouseWinV1);
                    HouseWinLaid++;
                }
            }
        }

        /// <summary>The bays a house brick column shows in one repeat (its windows
        /// centred in each): 4 of the old brick, 3 of the red-brown sash, 4 of the
        /// mottled and the tan.</summary>
        static int HouseLookBays(int look) => look == LookRedSash ? 3 : 4;

        /// <summary>A front door (the siding's window cell, whole, 0.95 x 2.05 m,
        /// 3 cm proud) centred at <paramref name="mid"/> on a wall facing
        /// <paramref name="n"/>, its foot on the floor or the drawn ground, and a
        /// concrete stoop 1.5 m wide and 1.0 m out from the floor to the walls' foot.</summary>
        static void HouseDoorAt(TileMeshes tm, Vector2 mid, Vector2 n, Vector2 along, float y0, float floor, float eave)
        {
            if (!HouseDoorOn || hwMap == null || float.IsNaN(floor)) return;
            Vector2 m = mid + n * 0.03f;
            Vector2 a = m - along * 0.475f, c = m + along * 0.475f;
            var bk = buckets[(int)Slot.FacadeHouse];
            float foot = floor;
            for (int k = 0; k <= 4; k++) { var p = Vector2.Lerp(a, c, k / 4f); foot = Mathf.Max(foot, LatticeY(hwMap, p.x, p.y) + 0.04f); }
            if (foot + 2.05f > eave - 0.2f) return;
            bk.Wall(L(a, 0f, tm), L(c, 0f, tm), foot, foot + 2.05f, n, HouseWinU0, HouseWinU1, HouseWinV0, HouseWinV1);
            float top = floor - 0.02f;
            if (top <= y0 + 0.05f) return;
            var st = buckets[(int)Slot.RoofFlat];
            Vector2 s0 = mid - along * 0.75f, s1 = mid + along * 0.75f;
            Vector2 o0 = s0 + n * 1.0f, o1 = s1 + n * 1.0f;
            st.Up(L(s0, top, tm), L(s1, top, tm), L(o1, top, tm), L(o0, top, tm),
                  new Vector2(0f, 0f), new Vector2(0.19f, 0f), new Vector2(0.19f, 0.13f), new Vector2(0f, 0.13f));
            float hgt = (top - y0) / RoofFlatM;
            st.Wall(L(o0, 0f, tm), L(o1, 0f, tm), y0, top, n, 0f, 0.19f, 0f, hgt);
            st.Wall(L(s0, 0f, tm), L(o0, 0f, tm), y0, top, -along, 0f, 0.13f, 0f, hgt);
            st.Wall(L(o1, 0f, tm), L(s1, 0f, tm), y0, top, along, 0f, 0.13f, 0f, hgt);
        }
    }
}
