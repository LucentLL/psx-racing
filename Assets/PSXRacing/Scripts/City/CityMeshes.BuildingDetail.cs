using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE BUILDING PASS: DETAIL ON THE NEAR CITY (2026-10-28). Every
        //  low and mid-rise block was a box with a painted facade - no
        //  cornice, a ground floor like the floors over it, a bare roof (the
        //  owner's NFS frames against ours). Now, on the TILES only (never
        //  the far proxies or the skyline: skylineBuild, which a proxy build
        //  sets too), a building from two storeys to RoofKitMinH gets:
        //    - a CORNICE at its roofline: a band proud of every wall that
        //      faces open ground (never a party wall: a neighbour's footprint
        //      within PartyProbeM of it), mitred round the corners, capped at
        //      a run's end - corbelled brick or cast stone on masonry, a metal
        //      coping on render and glass;
        //    - a BELT COURSE over the ground floor on masonry of three
        //      storeys and more (not on a shopfront: its canopy is there);
        //    - ROOF PLANT: air handlers, vents and a stair bulkhead on its
        //      flat roof, sized and placed by its own hash;
        //  and a shopfront (EmitWallStyled) a CANOPY over its glass and a
        //  plain SIGN BAND over that - generic panels, no lettering (owner
        //  rule: no brands). All of it in the facade atlas's bucket (no new
        //  material, no draw call), drawn in column 7's own pack surfaces
        //  (PSX Textures concrete_2 stone, PSX Textures II metal_panels_pt_4):
        //  PSX/Lit wraps that column inside its stone half, or its panel
        //  quarter on a metal vertex. Everything stands proud of the wall by
        //  12 cm and more - offset, never coplanar.
        //  PSX_BLD_DETAIL=0: none of it (the A/B).
        // ------------------------------------------------------------------
        public static bool BuildingDetailOff = System.Environment.GetEnvironmentVariable("PSX_BLD_DETAIL") == "0";
        /// <summary>A building this tall and more over its floor (two storeys) takes the detail.</summary>
        const float DetailMinH = 5.5f;
        const float CorniceDeep = 0.42f, CorniceTall = 0.70f, CopingDeep = 0.22f, CopingTall = 0.45f;
        const float BeltDeep = 0.12f, BeltTall = 0.30f;
        /// <summary>A wall with another footprint this close outside it is a party wall.</summary>
        const float PartyProbeM = 0.6f;
        const float CanopyDeep = 1.3f, CanopyThick = 0.22f, CanopyY = 3.05f, SignProud = 0.15f;

        static readonly List<int> partyNear = new List<int>(16);
        static readonly List<bool> cornOn = new List<bool>(32);
        static readonly List<Vector2> cornN = new List<Vector2>(32);
        static readonly List<Vector4> gearBoxes = new List<Vector4>(8);

        /// <summary>The awnings' colours (linear multipliers of column 7's
        /// grey): green, maroon, navy, charcoal, terracotta, forest.</summary>
        static readonly Vector3[] AwningCols =
        {
            new Vector3(0.36f, 0.75f, 0.45f), new Vector3(0.90f, 0.36f, 0.36f), new Vector3(0.36f, 0.45f, 0.95f),
            new Vector3(0.40f, 0.40f, 0.42f), new Vector3(1.10f, 0.55f, 0.36f), new Vector3(0.36f, 0.60f, 0.40f),
        };
        static readonly Vector3[] SignCols =
        {
            new Vector3(0.36f, 0.36f, 0.38f), new Vector3(0.95f, 0.93f, 0.88f), new Vector3(0.40f, 0.36f, 0.36f),
            new Vector3(0.36f, 0.40f, 0.48f),
        };

        /// <summary>Does this footprint take the detail? Called by the
        /// footprint emitter with what it already knows.</summary>
        static bool DetailFor(CityMap.Footprint f, Slot wallSlot, bool floating, float height) =>
            !BuildingDetailOff && !skylineBuild && wallSlot == Slot.FacadeGlass && !floating && f.roof == 0 &&
            !f.part && f.landmark == 0 && height >= DetailMinH && height < RoofKitMinH;

        /// <summary>Column 7 as a surface: <paramref name="k"/> the tint
        /// (linear, x the grey), stone or (metal) aluminium panels.</summary>
        static void PartsTint(Vector3 k, bool metal)
        {
            Bucket.Tint = new Color32(TintByte(k.x), TintByte(k.y), TintByte(k.z), (byte)(LookParts * 32 + (metal ? FacadeMetalBit : 0)));
            Bucket.FacOn = false;
        }

        /// <summary>Column 7's V: 8 m a repeat from the building's floor, so
        /// its storeys (PSX/Lit's night silhouette) are the wall's.</summary>
        static float PartV(float y) => (y - facBase) / 8f;

        static bool InPoly(Vector2[] pts, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                if ((pts[i].y > q.y) != (pts[j].y > q.y) &&
                    q.x < (pts[j].x - pts[i].x) * (q.y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x) inside = !inside;
            return inside;
        }

        /// <summary>The footprints whose box comes within a few metres of this one's.</summary>
        static void PartyCandidates(CityMap map, int fi, CityMap.Footprint f)
        {
            partyNear.Clear();
            int tx = Mathf.FloorToInt(f.centre.x / TileSize), tz = Mathf.FloorToInt(f.centre.y / TileSize);
            float reachF = Mathf.Sqrt(f.hu * f.hu + f.hv * f.hv) + 2f;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var list = map.FootprintsInTile(tx + dx, tz + dz);
                    if (list == null) continue;
                    foreach (int oi in list)
                    {
                        if (oi == fi) continue;
                        var o = map.footprints[oi];
                        if (o.pts == null || o.pts.Length < 3) continue;
                        float reach = reachF + Mathf.Sqrt(o.hu * o.hu + o.hv * o.hv);
                        if ((o.centre - f.centre).sqrMagnitude <= reach * reach) partyNear.Add(oi);
                    }
                }
        }

        static bool PartyWall(CityMap map, Vector2 a, Vector2 c, Vector2 outward)
        {
            if (partyNear.Count == 0) return false;
            for (int s = 1; s <= 3; s++)
            {
                Vector2 q = Vector2.Lerp(a, c, s * 0.25f) + outward * PartyProbeM;
                foreach (int oi in partyNear)
                    if (InPoly(map.footprints[oi].pts, q)) return true;
            }
            return false;
        }

        /// <summary>
        /// The cornice (and on masonry of three storeys the belt course) round
        /// <paramref name="poly"/> (CCW, the walls as drawn), the cornice's
        /// top at <paramref name="wallTop"/>. PickFacade has set the look,
        /// tint, storey and floor.
        /// </summary>
        static void EmitCornice(TileMeshes tm, CityMap map, int fi, CityMap.Footprint f, List<Vector2> poly,
                                float wallTop, float floorP, bool retail)
        {
            int n = poly.Count;
            if (n < 3 || n > 48) return;
            var keep = Bucket.Tint; bool keepOn = Bucket.FacOn, keepNc = Bucket.NoCollide;
            Bucket.NoCollide = true;   // drawn, never collided: no cook time, nothing a car meets
            bool masonry = IsPlainLook(facLook) && facLook != LookApartment;
            uint h = FacadeHash(f.centre + new Vector2(3.1f, 7.7f));
            PartyCandidates(map, fi, f);
            cornOn.Clear(); cornN.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], c = poly[(i + 1) % n], d = c - a;
                float len = d.magnitude;
                var o = len > 1e-4f ? new Vector2(d.y, -d.x) / len : Vector2.zero;
                cornN.Add(o);
                cornOn.Add(len >= 1.5f && !PartyWall(map, a, c, o));
            }
            // a corner too sharp to mitre takes no cornice on either side
            for (int i = 0; i < n; i++)
            {
                int ip = (i + n - 1) % n;
                if (cornOn[i] && cornOn[ip] && 1f + Vector2.Dot(cornN[ip], cornN[i]) < 0.35f) { cornOn[i] = false; cornOn[ip] = false; }
            }
            // the band's colour: corbelled brick in the wall's own colour or
            // cast stone on masonry (by the hash); a metal coping on render and glass
            Vector3 kWall = new Vector3(keep.r & 0xFE, keep.g, keep.b) / 127.5f;
            var mean = FacadeLooks[facLook].meanLin;
            bool metal = !masonry;
            Vector3 k = metal ? new Vector3(0.55f, 0.56f, 0.58f)
                      : ((h >> 4) & 1u) == 0u ? Vector3.Scale(kWall, mean) * 2.1f
                      : new Vector3(0.92f, 0.90f, 0.85f);
            PartsTint(k, metal);
            float deep = masonry ? CorniceDeep : CopingDeep, tall = masonry ? CorniceTall : CopingTall;
            Band(tm, poly, wallTop - tall, wallTop, deep, metal);
            float floors = (wallTop - floorP) / Mathf.Max(facFloorH, 2.5f);
            if (masonry && !retail && floors >= 2.6f)
            {
                float yb = floorP + facFloorH;
                Band(tm, poly, yb - BeltTall * 0.5f, yb + BeltTall * 0.5f, BeltDeep, false);
            }
            Bucket.Tint = keep; Bucket.FacOn = keepOn; Bucket.NoCollide = keepNc;
        }

        /// <summary>One band proud of the walls cornOn marks, from y0 to y1,
        /// <paramref name="deep"/> out: front, soffit, top, and an end cap
        /// where a run stops.</summary>
        static void Band(TileMeshes tm, List<Vector2> poly, float y0, float y1, float deep, bool metal)
        {
            int n = poly.Count;
            var bk = buckets[(int)Slot.FacadeGlass];
            float uK = metal ? 1f : 0.5f;
            float v0 = PartV(y0), v1 = PartV(y1), vd = deep / 8f;
            float run = 0f;
            for (int i = 0; i < n; i++)
            {
                if (!cornOn[i]) continue;
                int ip = (i + n - 1) % n, inx = (i + 1) % n;
                Vector2 a = poly[i], c = poly[inx], o = cornN[i];
                Vector2 qa = cornOn[ip] ? a + (cornN[ip] + o) * (deep / (1f + Vector2.Dot(cornN[ip], o))) : a + o * deep;
                Vector2 qc = cornOn[inx] ? c + (o + cornN[inx]) * (deep / (1f + Vector2.Dot(o, cornN[inx]))) : c + o * deep;
                if (Vector2.Dot(qc - qa, c - a) <= 0f || (qc - qa).sqrMagnitude < 0.04f) continue;
                float len = Vector2.Distance(qa, qc);
                float u0 = run * uK, u1 = (run + len) * uK;
                run += len;
                bk.Wall(L(qa, 0f, tm), L(qc, 0f, tm), y0, y1, o, u0, u1, v0, v1);
                bk.Down(L(a, y0, tm), L(c, y0, tm), L(qc, y0, tm), L(qa, y0, tm),
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v0 - vd), new Vector2(u0, v0 - vd));
                bk.Up(L(a, y1, tm), L(c, y1, tm), L(qc, y1, tm), L(qa, y1, tm),
                      new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v1 + vd), new Vector2(u0, v1 + vd));
                Vector2 dir = (c - a).normalized;
                if (!cornOn[ip])
                    bk.Face(L(a, y0, tm), L(a, y1, tm), L(qa, y1, tm), L(qa, y0, tm), new Vector3(-dir.x, 0f, -dir.y),
                            new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(deep * uK, v1), new Vector2(deep * uK, v0));
                if (!cornOn[inx])
                    bk.Face(L(c, y0, tm), L(c, y1, tm), L(qc, y1, tm), L(qc, y0, tm), new Vector3(dir.x, 0f, dir.y),
                            new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(deep * uK, v1), new Vector2(deep * uK, v0));
            }
        }

        /// <summary>A shopfront's canopy over its glass and a plain sign band
        /// over that (EmitWallStyled): <paramref name="yFloor"/> the shop's
        /// floor, <paramref name="split"/> the top of the shopfront strip,
        /// <paramref name="y1"/> the wall's top.</summary>
        static void EmitStorefront(TileMeshes tm, Vector2 a, Vector2 c, float yFloor, float split, float y1, Vector2 outward)
        {
            if (BuildingDetailOff || skylineBuild) return;
            float len = Vector2.Distance(a, c);
            if (len < 4f) return;
            var keep = Bucket.Tint; bool keepOn = Bucket.FacOn, keepNc = Bucket.NoCollide;
            Bucket.NoCollide = true;   // drawn, never collided: no cook time, nothing a car meets
            var bk = buckets[(int)Slot.FacadeGlass];
            Vector2 dir = (c - a) / len;
            uint h = FacadeHash((a + c) * 0.5f + new Vector2(1.3f, -2.9f));
            Vector2 p0 = a + dir * 0.4f, p1 = c - dir * 0.4f;
            float cy0 = yFloor + CanopyY, cy1 = cy0 + CanopyThick;
            if (cy1 > split - 0.1f) { cy1 = split - 0.1f; cy0 = cy1 - CanopyThick; }
            Vector2 q0 = p0 + outward * CanopyDeep, q1 = p1 + outward * CanopyDeep;
            float u1 = len - 0.8f, ud = CanopyDeep;
            PartsTint(AwningCols[h % (uint)AwningCols.Length], true);
            float v0 = PartV(cy0), v1 = PartV(cy1);
            bk.Up(L(p0, cy1, tm), L(p1, cy1, tm), L(q1, cy1, tm), L(q0, cy1, tm),
                  new Vector2(0f, v1), new Vector2(u1, v1), new Vector2(u1, v1 + ud / 8f), new Vector2(0f, v1 + ud / 8f));
            bk.Down(L(p0, cy0, tm), L(p1, cy0, tm), L(q1, cy0, tm), L(q0, cy0, tm),
                    new Vector2(0f, v0), new Vector2(u1, v0), new Vector2(u1, v0 - ud / 8f), new Vector2(0f, v0 - ud / 8f));
            bk.Wall(L(q0, 0f, tm), L(q1, 0f, tm), cy0, cy1, outward, 0f, u1, v0, v1);
            bk.Wall(L(p0, 0f, tm), L(q0, 0f, tm), cy0, cy1, -dir, 0f, ud, v0, v1);
            bk.Wall(L(p1, 0f, tm), L(q1, 0f, tm), cy0, cy1, dir, 0f, ud, v0, v1);
            // the sign band: a plain panel SignProud in front of the wall over the shopfront
            float sy0 = split + 0.06f, sy1 = Mathf.Min(split + 0.70f, y1 - 0.15f);
            if (sy1 > sy0 + 0.3f)
            {
                Vector2 s0 = a + dir * 0.6f, s1 = c - dir * 0.6f;
                Vector2 f0 = s0 + outward * SignProud, f1 = s1 + outward * SignProud;
                float su = Vector2.Distance(s0, s1);
                PartsTint(SignCols[(h >> 8) % (uint)SignCols.Length], true);
                float w0 = PartV(sy0), w1 = PartV(sy1), wd = SignProud / 8f;
                bk.Wall(L(f0, 0f, tm), L(f1, 0f, tm), sy0, sy1, outward, 0f, su, w0, w1);
                bk.Up(L(s0, sy1, tm), L(s1, sy1, tm), L(f1, sy1, tm), L(f0, sy1, tm),
                      new Vector2(0f, w1), new Vector2(su, w1), new Vector2(su, w1 + wd), new Vector2(0f, w1 + wd));
                bk.Down(L(s0, sy0, tm), L(s1, sy0, tm), L(f1, sy0, tm), L(f0, sy0, tm),
                        new Vector2(0f, w0), new Vector2(su, w0), new Vector2(su, w0 - wd), new Vector2(0f, w0 - wd));
                bk.Wall(L(s0, 0f, tm), L(f0, 0f, tm), sy0, sy1, -dir, 0f, SignProud, w0, w1);
                bk.Wall(L(s1, 0f, tm), L(f1, 0f, tm), sy0, sy1, dir, 0f, SignProud, w0, w1);
            }
            Bucket.Tint = keep; Bucket.FacOn = keepOn; Bucket.NoCollide = keepNc;
        }

        /// <summary>
        /// A low or mid-rise flat roof's plant: a stair and lift bulkhead on
        /// a big roof, one to four air handlers and a vent or two, on slots
        /// along the roof's axis (each wholly on the roof, a metre in from its
        /// edge, clear of the others) by the building's hash.
        /// </summary>
        static void EmitRoofGear(TileMeshes tm, CityMap.Footprint f, List<Vector2> roof, float top, float height)
        {
            if (roof.Count < 3) return;
            float area = PolyArea(roof);
            if (area < 80f) return;
            var v = new Vector2(-f.u.y, f.u.x);
            float u0 = float.MaxValue, u1 = float.MinValue, va = float.MaxValue, vb = float.MinValue;
            foreach (var p in roof)
            {
                float a = Vector2.Dot(p - f.centre, f.u), b = Vector2.Dot(p - f.centre, v);
                u0 = Mathf.Min(u0, a); u1 = Mathf.Max(u1, a); va = Mathf.Min(va, b); vb = Mathf.Max(vb, b);
            }
            float hu = (u1 - u0) * 0.5f, hv = (vb - va) * 0.5f;
            if (hu < 3f || hv < 2.5f) return;
            Vector2 c0 = f.centre + f.u * ((u0 + u1) * 0.5f) + v * ((va + vb) * 0.5f);
            uint h = FacadeHash(f.centre + new Vector2(-9.7f, 3.3f));
            var keep = Bucket.Tint; bool keepOn = Bucket.FacOn, keepNc = Bucket.NoCollide;
            Bucket.NoCollide = true;   // drawn, never collided: no cook time, nothing a car meets
            gearBoxes.Clear();
            // the bulkhead, near one end, in the roof's light stone
            if (area >= 250f && height >= 7f)
            {
                float side = ((h >> 3) & 1u) == 0u ? 1f : -1f;
                PartsTint(new Vector3(0.82f, 0.80f, 0.76f), false);
                GearBox(tm, roof, c0 + f.u * (side * hu * 0.5f) + v * (hv * 0.15f * (((h >> 5) & 1u) == 0u ? 1f : -1f)),
                        f.u, 1.6f, 1.3f, top, 2.8f, false);
            }
            // the air handlers: light grey aluminium
            int want = Mathf.Clamp(Mathf.RoundToInt(area / 140f), 1, 4);
            PartsTint(new Vector3(0.95f, 0.95f, 0.97f), true);
            for (int s = 0, got = 0; s < 8 && got < want; s++)
            {
                int slot = (int)((h >> (s * 3)) & 7u);
                float fu = (slot - 3.5f) / 3.5f * 0.7f * hu;
                float fv = ((h >> (s + 7)) & 1u) == 0u ? 0.35f * hv : -0.35f * hv;
                if (GearBox(tm, roof, c0 + f.u * fu + v * fv, f.u, 0.75f, 0.55f, top, 1.0f, true)) got++;
            }
            // a vent or two: small dark boxes
            PartsTint(new Vector3(0.45f, 0.45f, 0.47f), true);
            int vents = 1 + (int)((h >> 27) & 1u);
            for (int s = 0, got = 0; s < 6 && got < vents; s++)
            {
                float fu = (((h >> (s * 4 + 1)) & 15u) / 15f - 0.5f) * 1.2f * hu;
                float fv = (((h >> (s * 4 + 3)) & 7u) / 7f - 0.5f) * 1.0f * hv;
                if (GearBox(tm, roof, c0 + f.u * fu + v * fv, f.u, 0.28f, 0.28f, top, 0.9f, true)) got++;
            }
            Bucket.Tint = keep; Bucket.FacOn = keepOn; Bucket.NoCollide = keepNc;
        }

        /// <summary>One box on the roof at <paramref name="at"/> (half sizes
        /// along u and across), <paramref name="tall"/> high from
        /// <paramref name="y"/>: four walls and a lid, in the current tint.
        /// Not where it would leave the roof (a metre in from its edge) or
        /// touch a box already placed.</summary>
        static bool GearBox(TileMeshes tm, List<Vector2> roof, Vector2 at, Vector2 u, float bu, float bv, float y, float tall, bool metal)
        {
            var v = new Vector2(-u.y, u.x);
            float mu = bu + 1f, mv = bv + 1f;
            if (!PolyContains(roof, at + u * mu + v * mv) || !PolyContains(roof, at - u * mu + v * mv) ||
                !PolyContains(roof, at - u * mu - v * mv) || !PolyContains(roof, at + u * mu - v * mv)) return false;
            foreach (var b in gearBoxes)
                if (Mathf.Abs(Vector2.Dot(at - new Vector2(b.x, b.y), u)) < bu + b.z + 0.4f &&
                    Mathf.Abs(Vector2.Dot(at - new Vector2(b.x, b.y), v)) < bv + b.w + 0.4f) return false;
            gearBoxes.Add(new Vector4(at.x, at.y, bu, bv));
            var bk = buckets[(int)Slot.FacadeGlass];
            Vector2 c1 = at + u * bu + v * bv, c2 = at - u * bu + v * bv, c3 = at - u * bu - v * bv, c4 = at + u * bu - v * bv;
            float k = metal ? 1f : 0.5f, w0 = PartV(y), w1 = PartV(y + tall);
            bk.Wall(L(c2, 0f, tm), L(c1, 0f, tm), y, y + tall, v, 0f, 2f * bu * k, w0, w1);
            bk.Wall(L(c1, 0f, tm), L(c4, 0f, tm), y, y + tall, u, 0f, 2f * bv * k, w0, w1);
            bk.Wall(L(c4, 0f, tm), L(c3, 0f, tm), y, y + tall, -v, 0f, 2f * bu * k, w0, w1);
            bk.Wall(L(c3, 0f, tm), L(c2, 0f, tm), y, y + tall, -u, 0f, 2f * bv * k, w0, w1);
            float yt = y + tall;
            bk.Up(L(c1, yt, tm), L(c2, yt, tm), L(c3, yt, tm), L(c4, yt, tm),
                  new Vector2(2f * bu * k, w1), new Vector2(0f, w1), new Vector2(0f, w1 + 2f * bv / 8f), new Vector2(2f * bu * k, w1 + 2f * bv / 8f));
            return true;
        }
    }
}
