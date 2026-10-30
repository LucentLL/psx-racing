using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE FAR BUILDINGS' MESH (city-popin, 2026-10-28; CityProxies.cs).
        //  Every footprint the far skyline does not draw, emitted by the
        //  tile's own EmitFootprint the way the skyline is (skylineBuild: one
        //  panel a wall, no cut off the pavement, no shopfront, no rooftop
        //  kit) - so the copy past the far plane wears the tile building's
        //  atlas column, tint, storeys, structure and night windows, and the
        //  hand-over at the squeeze's start changes nothing but detail - with
        //  three differences of its own (proxyBuild): floored parts float on
        //  their podiums as the tile stands them (the skyline stands every
        //  tier on the ground, since it leaves the low podiums out), a house
        //  is not fitted (half a pixel out there), and the walls run on only
        //  ProxySkirtM below the ground (the far land stands under them).
        //
        //  It may run BETWEEN the steps of an open tile job (a job holds the
        //  builder's buckets and facade state across frames): what it emits is
        //  appended to the buckets, copied out and cut off again, and every
        //  piece of facade state a footprint touches is saved and put back.
        // ------------------------------------------------------------------

        /// <summary>True while a far-building proxy is being emitted (with
        /// <see cref="skylineBuild"/>).</summary>
        static bool proxyBuild;
        /// <summary>City perf round 1: the pack towers' lots have far proxies
        /// (their OSM massing) too. PSX_CITY_TOWERPROXY=0 (or
        /// ?ab=CityMeshes.TowerProxies:0, set before the city loads): none, as before.</summary>
        public static bool TowerProxies = System.Environment.GetEnvironmentVariable("PSX_CITY_TOWERPROXY") != "0";
        /// <summary>How far a proxy's walls run on below its ground (m).</summary>
        const float ProxySkirtM = 12f;

        /// <summary>One proxy vertex, as the mesh stores it (44 bytes):
        /// position, normal (SNorm8), the facade colour (tint + column),
        /// UV0, the wall (UV1, the facade structure) and the building's tile
        /// (UV2, PSX/Lit's CITY POP-IN).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ProxyVert
        {
            public Vector3 pos;
            public sbyte nx, ny, nz, nw;
            public Color32 col;
            public Vector2 uv;
            public Vector4 fac;
            public Vector2 tile;
        }

        public static readonly VertexAttributeDescriptor[] ProxyLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
        };

        /// <summary>What proxies emit into: the vertices and, per slot, the
        /// triangles (indices into <see cref="v"/>).</summary>
        public sealed class ProxyOut
        {
            public readonly List<ProxyVert> v = new List<ProxyVert>(4096);
            public readonly List<int>[] t = new List<int>[(int)Slot.COUNT];
            public void Clear() { v.Clear(); foreach (var l in t) l?.Clear(); }
        }

        static readonly TileMeshes proxyTm = new TileMeshes { origin = Vector3.zero };
        static readonly int[] pxV = new int[(int)Slot.COUNT], pxT = new int[(int)Slot.COUNT], pxTn = new int[(int)Slot.COUNT];
        static readonly List<Vector2> pxFit = new List<Vector2>(32), pxRoof = new List<Vector2>(32);
        static readonly List<Vector3> pxNrm = new List<Vector3>(256);

        /// <summary>
        /// Emit footprint <paramref name="fi"/> as a far-building proxy into
        /// <paramref name="o"/>, every vertex tagged with <paramref name="tile"/>
        /// (UV2). Returns the vertices added (0: nothing drawn - a model's lot,
        /// a part its hero draws...).
        /// </summary>
        public static int EmitProxy(CityMap map, Trims trims, int fi, ProxyOut o, Vector2 tile) =>
            EmitProxyCore(map, trims, fi, default, false, o, tile);

        // ---- city perf round 1 (2026-10-30; the owner: "a lot of pop in of distant objects") ----
        /// <summary>Outside the 8 x 8 km core OpenStreetMap maps buildings for,
        /// the city's buildings are CityBuildings' lots - the frontage boxes and
        /// gables, the prefab houses, trailers and restaurants - and none of them
        /// had a far copy: past the core every building appeared at the 500 m far
        /// plane. A lot now has a far proxy as a footprint does: its tile's own
        /// box (the same facade pick, walls and flat roof) or gable, a prefab's
        /// lot as the box or gable of its size. PSX_CITY_LOTPROXY=0 (or
        /// ?ab=CityMeshes.LotProxies:0, set before the city loads): none, as before.
        /// The interior fill houses (CityHouses) have none (their placement is
        /// worked out a tile at a time, too dear for 2 km round the eye): LEFT.</summary>
        public static bool LotProxies = System.Environment.GetEnvironmentVariable("PSX_CITY_LOTPROXY") != "0";

        /// <summary>Emit CityBuildings lot <paramref name="b"/> as a far proxy (see
        /// <see cref="LotProxies"/>). Returns the vertices added.</summary>
        public static int EmitProxyLot(CityMap map, Trims trims, in CityBuildings.B b, ProxyOut o, Vector2 tile) =>
            EmitProxyCore(map, trims, -1, b, true, o, tile);

        /// <summary>A lot's far shape, as BuildBuildings stands its box or
        /// gable (on the lowest drawn ground at its corners, without a graded
        /// house pad - a metre or so, a pixel out there); a prefab's lot is the
        /// gable (a home) or the box of its own size.</summary>
        static void EmitLotShape(CityMap map, TileMeshes tm, in CityBuildings.B b)
        {
            if (b.w < 0.5f || b.d < 0.5f || b.h < 0.5f) return;
            float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
            Vector2 fwd = new Vector2(sy, cy);           // faces the road
            Vector2 rgt = new Vector2(cy, -sy);
            Vector2 hw = rgt * (b.w * 0.5f);
            Vector2 hd = fwd * (b.d * 0.5f);
            var c1 = b.pos + hw + hd; var c2 = b.pos - hw + hd;
            var c3 = b.pos - hw - hd; var c4 = b.pos + hw - hd;
            float g = Mathf.Min(
                Mathf.Min(CityElevation.GroundY(map, c1.x, c1.y), CityElevation.GroundY(map, c2.x, c2.y)),
                Mathf.Min(CityElevation.GroundY(map, c3.x, c3.y), CityElevation.GroundY(map, c4.x, c4.y)));
            // (the tile's own heights: the far land stands sunk under them)
            float y0 = g - BuildingSink;
            float y1 = y0 + b.h + BuildingSink;
            bool gable = b.kind == 0 ? b.gable : CityProps.IsHome(b.kind);
            if (gable)
            {
                EmitGableHouse(tm, b.pos, rgt, b.w * 0.5f, b.d * 0.5f, y0, g - BuildingSink + b.h * 0.7f, y1, Slot.FacadeHouse);
                return;
            }
            PickFacade(b.pos, (byte)(b.style == 0 ? (b.h > 40f ? 0 : 1) : b.style == 1 ? 1 : 2), 0, 0, default, g, b.h);
            EmitWallStyled(tm, c2, c1, y0, y1, fwd, b.style == 3, false, Slot.FacadeGlass);
            EmitWallStyled(tm, c1, c4, y0, y1, rgt, b.style == 3, false, Slot.FacadeGlass);
            EmitWallStyled(tm, c4, c3, y0, y1, -fwd, b.style == 3, false, Slot.FacadeGlass);
            EmitWallStyled(tm, c3, c2, y0, y1, -rgt, b.style == 3, false, Slot.FacadeGlass);
            EndFacade();
            var roof = buckets[(int)Slot.RoofFlat];
            roof.Up(L(c2, y1, tm), L(c3, y1, tm), L(c4, y1, tm), L(c1, y1, tm),
                new Vector2(c2.x / RoofFlatM, c2.y / RoofFlatM), new Vector2(c3.x / RoofFlatM, c3.y / RoofFlatM),
                new Vector2(c4.x / RoofFlatM, c4.y / RoofFlatM), new Vector2(c1.x / RoofFlatM, c1.y / RoofFlatM));
        }

        static int EmitProxyCore(CityMap map, Trims trims, int fi, CityBuildings.B lot, bool isLot, ProxyOut o, Vector2 tile)
        {
            // what an open tile job may be holding between its steps
            var tint = Bucket.Tint; bool facOn = Bucket.FacOn; var facA = Bucket.FacA; var facDir = Bucket.FacDir;
            float facLen = Bucket.FacLen, facZ = Bucket.FacZ, facW = Bucket.FacW;
            bool memOn = Bucket.MemberOn; var memA = Bucket.MemberA; float memDir = Bucket.MemberDir, memW = Bucket.MemberW;
            bool noCol = Bucket.NoCollide;
            int sLook = facLook, sScheme = facScheme, sBaseN = facBaseN, sBelt = facBelt, sCA = facCornerA, sCB = facCornerB;
            float sFloorH = facFloorH, sBase = facBase, sCrownY = facCrownY, sWallTop = facWallTop;
            byte sLed = facLed;
            bool sHero = heroOnePanel, sSky = skylineBuild, sProxy = proxyBuild;
            pxFit.Clear(); pxFit.AddRange(fitPoly);
            pxRoof.Clear(); pxRoof.AddRange(roofScratch);
            for (int s = 0; s < buckets.Length; s++)
            {
                var b = buckets[s];
                pxV[s] = b.v.Count; pxT[s] = b.t.Count; pxTn[s] = b.tn != null ? b.tn.Count : 0;
            }
            int added = 0;
            skylineBuild = true; proxyBuild = true;
            // city perf round 1 (the owner: "pop in of distant objects"): a pack
            // tower's lot stands its MODEL near (CityWorld), which had no far copy
            // at all - an 8-12 storey building appearing at the far plane. Its
            // proxy is the lot's own OSM massing, as an unswapped lot's is
            // (TowerProxies; the model hands over to it past the squeeze's start).
            var fpx = isLot ? null : map.footprints[fi];
            byte keptKind = fpx != null ? fpx.propKind : (byte)0;
            if (fpx != null && TowerProxies && CityProps.IsTower(keptKind)) fpx.propKind = 0;
            try
            {
                if (isLot) EmitLotShape(map, proxyTm, lot);
                else EmitFootprint(map, trims, proxyTm, fi);
                EndFacade();
            }
            finally
            {
                if (fpx != null) fpx.propKind = keptKind;
                skylineBuild = sSky; proxyBuild = sProxy;
                // copy out what was appended, then cut it off again
                for (int s = 0; s < buckets.Length; s++)
                {
                    var b = buckets[s];
                    int v0 = pxV[s], nv = b.v.Count - v0;
                    int nt = b.t.Count - pxT[s], ntn = (b.tn != null ? b.tn.Count : 0) - pxTn[s];
                    if (nv <= 0) continue;
                    int baseOut = o.v.Count;
                    if (nt + ntn > 0)
                    {
                        var tl = o.t[s] ??= new List<int>(1024);
                        int tStart = tl.Count;
                        for (int k = 0; k < nt; k++) tl.Add(b.t[pxT[s] + k] - v0 + baseOut);
                        for (int k = 0; k < ntn; k++) tl.Add(b.tn[pxTn[s] + k] - v0 + baseOut);
                        // the normals, as RecalculateNormals makes them (area-weighted)
                        pxNrm.Clear();
                        for (int k = 0; k < nv; k++) pxNrm.Add(Vector3.zero);
                        for (int k = tStart; k + 2 < tl.Count; k += 3)
                        {
                            int a = tl[k] - baseOut, c1 = tl[k + 1] - baseOut, c2 = tl[k + 2] - baseOut;
                            Vector3 pa = b.v[v0 + a], pb = b.v[v0 + c1], pc = b.v[v0 + c2];
                            var n = Vector3.Cross(pb - pa, pc - pa);
                            pxNrm[a] += n; pxNrm[c1] += n; pxNrm[c2] += n;
                        }
                        for (int k = 0; k < nv; k++)
                        {
                            var n = pxNrm[k].sqrMagnitude > 1e-12f ? pxNrm[k].normalized : Vector3.up;
                            o.v.Add(new ProxyVert
                            {
                                pos = b.v[v0 + k],
                                nx = (sbyte)Mathf.RoundToInt(n.x * 127f), ny = (sbyte)Mathf.RoundToInt(n.y * 127f),
                                nz = (sbyte)Mathf.RoundToInt(n.z * 127f), nw = 0,
                                col = b.col != null && b.col.Count > v0 + k ? b.col[v0 + k] : new Color32(128, 128, 128, 0),
                                uv = b.uv[v0 + k],
                                fac = b.fac != null && b.fac.Count > v0 + k ? b.fac[v0 + k] : Vector4.zero,
                                tile = tile,
                            });
                        }
                        added += nv;
                    }
                    // cut the bucket back to what the job had
                    b.v.RemoveRange(v0, nv);
                    b.uv.RemoveRange(v0, b.uv.Count - v0);
                    if (b.col != null && b.col.Count > v0) b.col.RemoveRange(v0, b.col.Count - v0);
                    if (b.fac != null && b.fac.Count > v0) b.fac.RemoveRange(v0, b.fac.Count - v0);
                    if (nt > 0) b.t.RemoveRange(pxT[s], nt);
                    if (ntn > 0) b.tn.RemoveRange(pxTn[s], ntn);
                }
                Bucket.Tint = tint; Bucket.FacOn = facOn; Bucket.FacA = facA; Bucket.FacDir = facDir;
                Bucket.FacLen = facLen; Bucket.FacZ = facZ; Bucket.FacW = facW;
                Bucket.MemberOn = memOn; Bucket.MemberA = memA; Bucket.MemberDir = memDir; Bucket.MemberW = memW;
                Bucket.NoCollide = noCol;
                facLook = sLook; facScheme = sScheme; facBaseN = sBaseN; facBelt = sBelt; facCornerA = sCA; facCornerB = sCB;
                facFloorH = sFloorH; facBase = sBase; facCrownY = sCrownY; facWallTop = sWallTop; facLed = sLed;
                heroOnePanel = sHero;
                fitPoly.Clear(); fitPoly.AddRange(pxFit);
                roofScratch.Clear(); roofScratch.AddRange(pxRoof);
                proxyTm.houseSeats.Clear(); proxyTm.mastTips.Clear(); proxyTm.solids?.Clear();
            }
            return added;
        }
    }
}
