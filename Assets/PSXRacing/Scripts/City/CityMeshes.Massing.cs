using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE MASSING OF A TOWER WITH NO PARTS (Uptown B3, 2026-10-04).
        //  B2 drew every tower whose OSM building:parts carry heights as those
        //  parts; the rest were a prism to the roof and, over 120 m, two
        //  generic boxes on top - every one the same crown. Office towers are
        //  not built that way: a podium of a few storeys over the whole lot,
        //  the shaft set back from it, and something on the roof - the plant
        //  floors in a screened penthouse, a setback top storey or two, or a
        //  plain parapet. Each tower of MassingMinH and more with no parts, no
        //  roof shape and no landmark entry now gets one of each, chosen by a
        //  hash of where it stands (deterministic: the same tower every visit,
        //  the far skyline's copy too), never a copy of any real tower.
        //  The podium stands on the footprint exactly where the prism did, so
        //  nothing the tile collides with moves out; the shaft and the roof
        //  are inside it.
        // ------------------------------------------------------------------
        /// <summary>The shortest tower that gets a podium, a shaft and a roof
        /// treatment instead of a prism.</summary>
        public const float MassingMinH = 60f;
        /// <summary>A footprint narrower than this (its box's short side) is
        /// too slender to step back from: it rises straight from the ground,
        /// with its roof treatment.</summary>
        const float PodiumMinSideM = 22f;
        /// <summary>The shaft above the podium must still be this tall.</summary>
        const float PodiumShaftMinM = 40f;
        static readonly List<Vector2> massPoly = new List<Vector2>(32);
        static readonly List<Vector2> massShaft = new List<Vector2>(32);
        static readonly List<Vector2> massTop = new List<Vector2>(32);

        /// <summary>
        /// A tower as a podium (3-6 storeys over the whole footprint; stone
        /// under about half the glass towers), a shaft inset 2-4 m from its
        /// edge, and one roof treatment: a screened plant PENTHOUSE behind a
        /// parapet; a SETBACK - the top 2-8 storeys stepped in 3-5 m, a small
        /// plant box on it; or a tall PARAPET band. PickFacade has set the
        /// look. False (nothing emitted) when the footprint is degenerate;
        /// the caller then draws the plain prism.
        /// </summary>
        static bool EmitMassing(TileMeshes tm, CityMap.Footprint f, List<Vector2> poly, float y0, float floor, float top)
        {
            // the polygon without its near-duplicate points (a cut leaves some)
            massPoly.Clear();
            foreach (var p in poly)
                if (massPoly.Count == 0 || (p - massPoly[massPoly.Count - 1]).sqrMagnitude > 0.04f) massPoly.Add(p);
            if (massPoly.Count > 3 && (massPoly[0] - massPoly[massPoly.Count - 1]).sqrMagnitude <= 0.04f) massPoly.RemoveAt(massPoly.Count - 1);
            if (massPoly.Count < 3 || PolyArea(massPoly) < 40f) return false;

            uint h = FacadeHash(f.centre + new Vector2(17.3f, -5.1f));   // its own stream, not the look's
            float storey = facFloorH;
            int kind = (int)(h % 3u);                                   // 0 penthouse, 1 setback, 2 parapet
            if (kind == 1 && f.h < 90f) kind = 0;                       // a short tower has no room to step back
            int podStoreys = 3 + (int)((h >> 4) % 4u);                  // 3..6
            float inset = 2f + 2f * (((h >> 8) & 0xFF) / 255f);         // 2..4 m
            float podTop = floor + podStoreys * storey;
            bool podium = 2f * Mathf.Min(f.hu, f.hv) >= PodiumMinSideM && top - podTop >= PodiumShaftMinM
                          && InsetPoly(massPoly, inset, massShaft);
            if (!podium) { massShaft.Clear(); massShaft.AddRange(massPoly); }

            // THE PODIUM: the footprint's own walls to its top storey, in
            // stone under a glass tower half the time
            float shaftY0 = y0;
            if (podium)
            {
                var keepTint = Bucket.Tint; int keepLook = facLook;
                if (facLook <= LookTeal && ((h >> 16) & 1u) == 0u)
                {
                    var pal = LookPalette[LookStone];
                    var k = pal[(int)((h >> 17) % (uint)pal.Length)];
                    facLook = LookStone;
                    Bucket.Tint = new Color32(TintByte(k.x), TintByte(k.y), TintByte(k.z), (byte)(LookStone * 32));
                }
                Walls(tm, massPoly, y0, podTop);
                facLook = keepLook; Bucket.Tint = keepTint;
                EarcutInto(buckets[(int)Slot.RoofFlat], massPoly, podTop, tm.origin, RoofFlatM);
                shaftY0 = podTop;
            }

            // THE SHAFT and THE ROOF
            if (kind == 1)
            {
                int topStoreys = Mathf.Clamp(Mathf.RoundToInt(f.h * 0.12f / storey), 2, 8);
                float setY = top - topStoreys * storey;
                float step = 3f + 2f * (((h >> 20) & 0xFF) / 255f);    // 3..5 m
                if (setY - shaftY0 > 20f && InsetPoly(massShaft, step, massTop))
                {
                    Walls(tm, massShaft, shaftY0, setY);
                    EarcutInto(buckets[(int)Slot.RoofFlat], massShaft, setY, tm.origin, RoofFlatM);
                    Walls(tm, massTop, setY, top);
                    EarcutInto(buckets[(int)Slot.RoofFlat], massTop, top, tm.origin, RoofFlatM);
                    PlantBox(tm, f, massTop, top, 0.30f, 1.0f * storey, h);
                    return true;
                }
                kind = 0;   // no room to step back: a penthouse instead
            }
            Walls(tm, massShaft, shaftY0, top);
            float parapet = kind == 2 ? 2.2f : 1.2f;
            Parapet(tm, massShaft, top, parapet);
            EarcutInto(buckets[(int)Slot.RoofFlat], massShaft, top, tm.origin, RoofFlatM);
            if (kind == 0) PlantBox(tm, f, massShaft, top, 0.42f + 0.13f * (((h >> 24) & 0xFF) / 255f), 1.6f * storey, h);
            return true;
        }

        /// <summary>A ring of facade walls round a CCW polygon, y0 to y1.</summary>
        static void Walls(TileMeshes tm, List<Vector2> poly, float y0, float y1)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var c = poly[(i + 1) % n];
                var d = c - a;
                if (d.sqrMagnitude < 0.04f) continue;
                EmitFacadeQuad(tm, Slot.FacadeGlass, a, c, y0, y1, new Vector2(d.y, -d.x).normalized);
            }
        }

        /// <summary>A parapet: the walls carried <paramref name="rise"/> past
        /// the roof as a metal coping band. Its inner face and cap are left
        /// out - only a camera above the roof could see them, and from there
        /// the roof (at the band's foot) closes the top.</summary>
        static void Parapet(TileMeshes tm, List<Vector2> poly, float roofY, float rise)
        {
            if (skylineBuild) return;   // a metre or two, kilometres off
            FacadeMetal(true);
            Walls(tm, poly, roofY, roofY + rise);
            FacadeMetal(false);
        }

        /// <summary>The plant: a metal-screened box on the roof over the box's
        /// centre (nudged by the hash), <paramref name="share"/> of the roof's
        /// box across, <paramref name="tall"/> high - left off where it would
        /// overhang the roof (an L-shaped tower's box centre can be outside it).</summary>
        static void PlantBox(TileMeshes tm, CityMap.Footprint f, List<Vector2> roof, float y, float share, float tall, uint h)
        {
            // the roof's own box along the footprint's axis
            var v = new Vector2(-f.u.y, f.u.x);
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (var p in roof)
            {
                float a = Vector2.Dot(p - f.centre, f.u), b = Vector2.Dot(p - f.centre, v);
                u0 = Mathf.Min(u0, a); u1 = Mathf.Max(u1, a); v0 = Mathf.Min(v0, b); v1 = Mathf.Max(v1, b);
            }
            float hu = (u1 - u0) * 0.5f, hv = (v1 - v0) * 0.5f;
            if (hu < 3f || hv < 3f) return;
            float nudge = ((((h >> 12) & 0xF) / 15f) - 0.5f) * 0.3f;
            var c = f.centre + f.u * ((u0 + u1) * 0.5f + nudge * hu) + v * ((v0 + v1) * 0.5f);
            for (int tries = 0; tries < 2; tries++, share *= 0.7f)
            {
                float bu = Mathf.Max(2f, hu * share), bv = Mathf.Max(2f, hv * share);
                var c1 = c + f.u * bu + v * bv; var c2 = c - f.u * bu + v * bv;
                var c3 = c - f.u * bu - v * bv; var c4 = c + f.u * bu - v * bv;
                if (!PolyContains(roof, c1) || !PolyContains(roof, c2) || !PolyContains(roof, c3) || !PolyContains(roof, c4)) continue;
                FacadeMetal(true);
                EmitFacadeQuad(tm, Slot.FacadeGlass, c2, c1, y, y + tall, v);
                EmitFacadeQuad(tm, Slot.FacadeGlass, c1, c4, y, y + tall, f.u);
                EmitFacadeQuad(tm, Slot.FacadeGlass, c4, c3, y, y + tall, -v);
                EmitFacadeQuad(tm, Slot.FacadeGlass, c3, c2, y, y + tall, -f.u);
                FacadeMetal(false);
                buckets[(int)Slot.RoofFlat].Up(L(c2, y + tall, tm), L(c3, y + tall, tm), L(c4, y + tall, tm), L(c1, y + tall, tm),
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                return;
            }
        }

        /// <summary>
        /// A CCW polygon moved in by <paramref name="d"/> metres (mitred
        /// corners) into <paramref name="dst"/>. False where that folds it -
        /// a spike too sharp to mitre, an edge that turns back on itself, a
        /// corner that leaves the original, or less than a third of the area
        /// left: the caller then keeps the footprint as it is.
        /// </summary>
        static bool InsetPoly(List<Vector2> src, float d, List<Vector2> dst)
        {
            dst.Clear();
            int n = src.Count;
            if (n < 3) return false;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = src[i], a = src[(i + n - 1) % n], b = src[(i + 1) % n];
                Vector2 e0 = p - a, e1 = b - p;
                if (e0.sqrMagnitude < 1e-4f || e1.sqrMagnitude < 1e-4f) return false;
                Vector2 n0 = new Vector2(-e0.y, e0.x).normalized, n1 = new Vector2(-e1.y, e1.x).normalized;
                float den = 1f + Vector2.Dot(n0, n1);
                if (den < 0.3f) return false;
                dst.Add(p + (n0 + n1) * (d / den));
            }
            for (int i = 0; i < n; i++)
            {
                if (Vector2.Dot(src[(i + 1) % n] - src[i], dst[(i + 1) % n] - dst[i]) <= 0f) return false;
                if (!PolyContains(src, dst[i])) return false;
            }
            return PolyArea(dst) >= 0.33f * PolyArea(src);
        }
    }
}
