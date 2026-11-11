using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE LOW-RISE CLASSES (charlotte-buildings-1111 round 2 "types",
        //  2026-11-11; the owner: "replacing these very unrealistic generic
        //  textures ... needs to be a top priority"). Outside the Uptown loop
        //  a non-house building was a box in an office or glass facade at
        //  whatever height OSM said - Atando Ave's 11,015 m2 shed tagged
        //  height=26 (the 2015 Charlotte import's feet, read as metres) stood
        //  as a six-storey precast block, NoDa's loft apartments in a glass
        //  curtain wall. What OSM says each one IS rides in
        //  CityLowRise.Data.cs (tools/city/lowrise.py: a point inside the
        //  outline, its kind, whether a height / levels was mapped), matched
        //  to the footprints once at load (LowRiseApply, from
        //  CityBuildings.Precompute), and:
        //    HEIGHT (PSX_CITY_LRHEIGHT): a works shed, a shop or a big plain
        //      building (>= 2,000 m2, no levels mapped, 9.5-45 m) is ONE tall
        //      storey of 6.5-9 m; a school with no height its levels' (or 1-2
        //      by size) 4.2 m storeys; a small church a nave under a gable.
        //    LOOK (PSX_CITY_LRLOOK): a SHED is a metal-panel or block-and-brick
        //      box behind a parapet - a few high windows, roll-up dock doors
        //      on a long side, an office corner with a storefront and a door;
        //      a SHOP anywhere outside the loop is the strip centre (plain bays,
        //      canopy, blank sign band, parapet: covShop); APARTMENTS never
        //      wear a curtain wall; a MILL tall brick storeys and big
        //      multi-pane windows; a SCHOOL low brick; a CHURCH a brick nave
        //      with white gables under a gable roof and, the smaller ones, a
        //      steeple at the street end; a big church a brick hall.
        //  Everything rides the facade atlas's bucket and the roof buckets:
        //  no new material, slot or draw call.
        // ------------------------------------------------------------------
        public static bool LowRiseHeightOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LRHEIGHT") != "0";
        public static bool LowRiseLookOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LRLOOK") != "0";

        // the data's kinds (tools/city/lowrise.py)
        const byte LrWork = 1, LrShop = 2, LrWorship = 3, LrSchool = 4, LrHomes = 5, LrMill = 6, LrPlain = 7;
        const byte LrFHeight = 1, LrFLevels = 2, LrFBrick = 4, LrFMetal = 8, LrFOfficeZone = 16, LrFHeightInt = 32;
        /// <summary>Which kind wins a footprint two rows land in.</summary>
        static int LrRank(byte kind) => kind == LrMill ? 6 : kind == LrWorship ? 5 : kind == LrSchool ? 4 : kind == LrWork || kind == LrShop ? 3 : kind == LrPlain ? 2 : 1;
        // the classes the rules give
        const byte LcNone = 0, LcShed = 1, LcStrip = 2, LcChurch = 3, LcHall = 4, LcSchool = 5, LcApart = 6, LcMill = 7;
        static readonly string[] LcNames = { "none", "shed", "strip", "church", "hall", "school", "apartments", "mill" };

        struct LrInfo { public byte kind, flags, levels, cls; public float h0, area, wallH, rise; }
        static CityMap lrMap;
        static Dictionary<int, LrInfo> lrOf;
        /// <summary>The last LowRiseApply's counts and lists (the hand-back; Editor/CityRefSpots.Types.cs writes it out).</summary>
        public static string LowRiseReportText = "";

        static float LrArea(Vector2[] p)
        {
            float s = 0f;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) s += p[j].x * p[i].y - p[i].x * p[j].y;
            return Mathf.Abs(s) * 0.5f;
        }

        static uint LrHash(Vector2 c, uint salt)
        {
            uint h = FacadeHash(c) ^ (salt * 0x9E3779B1u);
            h ^= h >> 15; h *= 0x85EBCA6Bu; h ^= h >> 13;
            return h;
        }

        /// <summary>Match the data's rows to the footprints and apply the height and class rules, once a map
        /// (CityBuildings.Precompute calls it before anything reads the heights: the skyline, the far city, the
        /// tower swap, the tiles).</summary>
        internal static void LowRiseApply(CityMap map)
        {
            if (lrMap == map && lrOf != null) return;
            lrMap = map;
            lrOf = new Dictionary<int, LrInfo>();
            var sb = new System.Text.StringBuilder();
            if (!LowRiseHeightOn && !LowRiseLookOn) { LowRiseReportText = "low-rise classes: off (PSX_CITY_LRHEIGHT=0, PSX_CITY_LRLOOK=0)\n"; return; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var blob = System.Convert.FromBase64String(string.Concat(LrRows));
            int rows = blob.Length / 11, matched = 0, uptown = 0;
            var counts = new int[8];
            int lowered = 0, schools = 0, churches = 0, halls = 0, mills = 0;
            var lowList = new List<(float area, int fi, float h0, float h1, byte kind)>();
            var fs = map.footprints;
            // pass 1: each row to the footprint holding its point; a footprint two rows land in keeps the
            // stronger kind (a mill over the apartments mapped inside it, a church over a shop)
            var pick = new Dictionary<int, int>();
            for (int r = 0; r < rows; r++)
            {
                int o = r * 11;
                var p = new Vector2(System.BitConverter.ToInt32(blob, o) * 0.1f, System.BitConverter.ToInt32(blob, o + 4) * 0.1f);
                int tx = Mathf.FloorToInt(p.x / CityMap.FootCell), tz = Mathf.FloorToInt(p.y / CityMap.FootCell);
                int best = -1; float bestA = float.MaxValue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var list = map.FootprintsInTile(tx + dx, tz + dz);
                        if (list == null) continue;
                        foreach (int fi in list)
                        {
                            var f = fs[fi];
                            // (a building drawn by its OSM parts is matched on its hidden outline; its parts take the look)
                            if (f.pts == null || f.pts.Length < 3 || f.part || f.replaced || f.gable || f.style == 3 || f.landmark != 0) continue;
                            float reach = f.hu + f.hv;
                            if ((f.centre - p).sqrMagnitude > reach * reach) continue;
                            if (!InPoly(f.pts, p)) continue;
                            float a = 4f * f.hu * f.hv;
                            if (a < bestA) { bestA = a; best = fi; }
                        }
                    }
                if (best < 0) continue;
                if (pick.TryGetValue(best, out int prev) && LrRank(blob[prev * 11 + 8]) >= LrRank(blob[o + 8])) continue;
                pick[best] = r;
            }
            int feet = 0, partsLook = 0;
            // a hidden outline's parts, for the look
            var partsOf = new Dictionary<int, List<int>>();
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                if (!f.part || f.outline < 0 || !pick.ContainsKey(f.outline)) continue;
                if (!partsOf.TryGetValue(f.outline, out var l)) partsOf[f.outline] = l = new List<int>(4);
                l.Add(i);
            }
            foreach (var kv in pick)
            {
                int best = kv.Key, o = kv.Value * 11;
                byte kind = blob[o + 8], flags = blob[o + 9], levels = blob[o + 10];
                matched++;
                var fb = fs[best];
                var info = new LrInfo { kind = kind, flags = flags, levels = levels, h0 = fb.h, area = LrArea(fb.pts) };
                if (CityPoles.Uptown(map, fb.centre)) { uptown++; lrOf[best] = info; continue; }   // the ground-floor kit's
                if (fb.hidden)
                {
                    // drawn by its parts (a church's nave and steeple, a mill's wings): their shapes and heights are
                    // OSM's own; they take the class's LOOK only (a church or school its brick, a mill its loft brick)
                    byte pc = kind == LrMill ? LcMill : kind == LrWorship ? LcHall : kind == LrSchool ? LcSchool : kind == LrHomes ? LcApart : LcNone;
                    if (pc != LcNone && partsOf.TryGetValue(best, out var pl))
                        foreach (int pi in pl) { lrOf[pi] = new LrInfo { kind = kind, flags = flags, levels = levels, h0 = fs[pi].h, area = LrArea(fs[pi].pts), cls = pc }; partsLook++; }
                    continue;
                }
                bool noLevels = (flags & LrFLevels) == 0, tagH = (flags & LrFHeight) != 0;
                // THE CHARLOTTE IMPORT'S FEET: an integer height with no levels on a works / shop / plain building,
                // over 13 (a metre reading would be four storeys and more of a shed): the number is feet
                bool feetH = tagH && noLevels && (flags & LrFHeightInt) != 0 && fb.h > 13f && fb.h <= 120f;
                uint hh = LrHash(fb.centre, 7);
                float j = ((hh >> 8) & 0xFF) / 255f;
                switch (kind)
                {
                    case LrWork: case LrShop: case LrPlain:
                    {
                        bool strip = kind == LrShop || (kind == LrPlain && fb.style == 4);
                        bool tall = noLevels && info.area >= 2000f && fb.h > 10.5f && fb.h <= 45f;
                        if (LowRiseHeightOn && !fb.noSwap && fb.roof == 0 && (feetH || tall))
                        {
                            float h1 = feetH ? Mathf.Max(4.2f, fb.h * 0.3048f)
                                             : (info.area < 3000f ? 6.5f : info.area < 8000f ? 7.5f : 8.5f) + 0.5f * j;
                            lowList.Add((info.area, best, fb.h, h1, kind));
                            fb.h = h1;
                            if (fb.style <= 2 || fb.style == 4) fb.style = (byte)(strip ? 4 : 2);
                            lowered++;
                            if (feetH) feet++;
                        }
                        // a plain building: a shop by the exporter's own call (a big box by a lot), a shed only when big
                        // and outside an office zone (an office park's two-storey block keeps its atlas walls)
                        bool plainLook = kind != LrPlain || strip || ((flags & LrFOfficeZone) == 0 && info.area >= 5000f);
                        if (fb.h <= 12.5f && fb.roof == 0 && plainLook)
                        {
                            info.cls = strip ? LcStrip : LcShed;
                            if (strip && LowRiseLookOn) fb.style = 4;   // the strip centre's front, wherever it is
                            // (a strip centre is a shop storey and its parapet's band: the shopfront needs ShopFloorH + 1.5 m)
                            if (strip && LowRiseHeightOn && fb.h < 6.2f) fb.h = 6.2f + 0.6f * j;
                        }
                        break;
                    }
                    case LrWorship:
                    {
                        float fill = info.area / Mathf.Max(1f, 4f * fb.hu * fb.hv);
                        if (fb.style == 4 && LowRiseLookOn) fb.style = 2;   // (amenity=place_of_worship made it a shop)
                        if (info.area < 1600f && fill >= 0.60f && fb.hv >= 3.5f && fb.roof == 0)
                        {
                            info.cls = LcChurch;
                            info.wallH = Mathf.Clamp(5.5f + info.area / 400f, 6f, 8.5f);
                            info.rise = Mathf.Clamp(fb.hv * 0.80f, 2.5f, 8f);
                            if (LowRiseHeightOn && noLevels) fb.h = info.wallH + info.rise;
                            churches++;
                        }
                        else if (fb.roof == 0)
                        {
                            info.cls = LcHall;
                            if (LowRiseHeightOn && noLevels && fb.h > 10f) fb.h = (flags & LrFHeightInt) != 0 && fb.h > 13f ? Mathf.Max(6f, fb.h * 0.3048f) : 8.5f + j;
                            halls++;
                        }
                        break;
                    }
                    case LrSchool:
                        if (fb.roof == 0)
                        {
                            info.cls = LcSchool;
                            if (fb.style == 4 && LowRiseLookOn) fb.style = 2;
                            if (LowRiseHeightOn && noLevels && (!tagH || feetH || fb.h > 13f))
                            {
                                int lv = levels > 0 ? Mathf.Min((int)levels, 4) : info.area < 1200f ? 1 : 2;
                                fb.h = lv * 4.2f + 0.9f;
                            }
                            schools++;
                        }
                        break;
                    case LrHomes: info.cls = LcApart; break;
                    case LrMill:
                        info.cls = LcMill;
                        if (LowRiseHeightOn && !tagH && levels > 0) fb.h = levels * 4.3f + 1.0f;
                        mills++;
                        break;
                }
                counts[info.cls]++;
                lrOf[best] = info;
            }
            sb.Append($"low-rise classes: {rows} rows, {matched} footprints matched ({uptown} inside the Uptown loop: left to the ground-floor kit), {clock.ElapsedMilliseconds} ms\n");
            sb.Append("  classes outside the loop:");
            for (int c = 1; c < counts.Length; c++) sb.Append($" {LcNames[c]} {counts[c]}");
            sb.Append($"\n  HEIGHT rule: {lowered} sheds/shops/plain buildings lowered ({feet} of them an import's feet read as metres, the rest big low boxes with a guessed height made one tall storey); {schools} schools, {churches} churches (gabled nave), {halls} big church halls, {mills} mills; {partsLook} OSM building:parts take their building's class look\n");
            lowList.Sort((a, b) => b.area.CompareTo(a.area));
            sb.Append("  the 20 largest lowered (footprint, area m2, kind, h before -> after, x, z, lat, lon):\n");
            for (int i = 0; i < Mathf.Min(20, lowList.Count); i++)
            {
                var l = lowList[i]; var c = fs[l.fi].centre;
                double lat = 35.18456015184093 + c.y / 111132.0, lon = -80.81770185962013 + c.x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0));
                sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "    {0}\t{1:0}\t{2}\t{3:0.0} -> {4:0.0}\t({5:0}, {6:0})\t{7:0.00000},{8:0.00000}\n",
                    l.fi, l.area, l.kind == LrWork ? "work" : l.kind == LrShop ? "shop" : "plain", l.h0, l.h1, c.x, c.y, lat, lon));
            }
            LowRiseReportText = sb.ToString();
            Debug.Log("[City] " + LowRiseReportText);
        }

        /// <summary>The footprints within <paramref name="r"/> m of p: index, style, height, use, mat, roof and the class (the shot tool's log).</summary>
        public static string LowRiseDescribe(CityMap map, Vector2 p, float r)
        {
            var sb = new System.Text.StringBuilder();
            int tx = Mathf.FloorToInt(p.x / CityMap.FootCell), tz = Mathf.FloorToInt(p.y / CityMap.FootCell);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var list = map.FootprintsInTile(tx + dx, tz + dz);
                    if (list == null) continue;
                    foreach (int fi in list)
                    {
                        var f = map.footprints[fi];
                        if (f.pts == null || (f.centre - p).magnitude > r + f.hu) continue;
                        LrInfo info = default;
                        bool has = lrOf != null && lrOf.TryGetValue(fi, out info);
                        sb.Append($"    fp {fi} d {(f.centre - p).magnitude:0} m style {f.style} gable {f.gable} h {f.h:0.0} use {f.use} mat {f.mat} roof {f.roof} part {f.part} hidden {f.hidden} landmark {f.landmark} prop {f.propKind} area {LrArea(f.pts):0} in {InPoly(f.pts, p)} class {(has ? LcNames[info.cls] + " kind " + info.kind + " flags " + info.flags : "-")}\n");
                    }
                }
            return sb.ToString();
        }

        static bool LrIs(int fi, byte cls) => LowRiseLookOn && LrInfoOf(fi, out var i) && i.cls == cls;

        static bool LrInfoOf(int fi, out LrInfo info)
        {
            info = default;
            return lrOf != null && lrOf.TryGetValue(fi, out info) && info.cls != LcNone;
        }

        /// <summary>A look by hand: the column, its palette tint, storeys of <paramref name="storeyM"/> from the floor.</summary>
        static void LrSetLook(Vector2 centre, int look, float floor, float height, float storeyM, bool home)
        {
            uint h = FacadeHash(centre);
            facLook = look;
            float floors = Mathf.Max(1f, Mathf.Round(height / storeyM));
            facFloorH = height > 0.5f ? height / floors : storeyM;
            facBase = floor;
            FacadeStructure(look, 0, h, height);
            var pal = LookPalette[look];
            var k = pal[(int)((h >> 8) % (uint)pal.Length)];
            Bucket.Tint = new Color32(TintR(k.x, look), TintByte(k.y), TintByte(k.z), (byte)((look & 7) * 32 + (home ? FacadeHomeBit : 0)));
        }

        static readonly int[] LrSchoolLooks = { LookBrick, LookMottled, LookRedSash, LookTanBrick, LookDecoBrick };
        static readonly int[] LrChurchLooks = { LookBrick, LookRedSash, LookMottled, LookDecoBrick };

        /// <summary>After PickFacade (EmitFootprint): the class's own look - apartments in brick, never a curtain
        /// wall; a mill's tall brick storeys and big windows; a school's and a church hall's brick; a shed's
        /// stone (what its far proxy wears: the tile draws its own walls).</summary>
        static void LowRiseLook(CityMap map, int fi, CityMap.Footprint f, Vector2 centre, float floor, float height, byte mat)
        {
            if (!LowRiseLookOn || skylineBuild && !proxyBuild) return;
            bool has = LrInfoOf(fi, out var info);
            if (!has && f.use != 2) return;
            if (!has && CityPoles.Uptown(map, f.centre)) return;
            uint h = FacadeHash(centre);
            int p = LookDraw(h);
            byte cls = has ? info.cls : LcApart;
            switch (cls)
            {
                case LcApart:
                    if (facLook <= LookTeal && mat != 1)
                    {
                        int look = (info.flags & LrFBrick) != 0 || p < 70 ? BrickLook(h, height, 2) : LookApartment;
                        if (look <= LookTeal) look = LookBrick;
                        LrSetLook(centre, look, floor, height, HomeFloorM, true);
                    }
                    break;
                case LcMill:
                    LrSetLook(centre, p < 55 ? LookLoft : LookRedSash, floor, height, 4.3f, true);
                    break;
                case LcSchool:
                    LrSetLook(centre, LrSchoolLooks[p % LrSchoolLooks.Length], floor, height, 4.2f, false);
                    break;
                case LcHall:
                    LrSetLook(centre, LrChurchLooks[p % LrChurchLooks.Length], floor, height, 4.5f, false);
                    break;
                case LcShed:
                    if (facLook <= LookTeal || facLook == LookGranite || facLook == LookPrecast) LrSetLook(centre, LookStone, floor, height, Mathf.Max(4f, height), false);
                    break;
            }
        }

        // ------------------------------------------------------------------
        //  THE SHED: its walls drawn here in place of the atlas (EmitFootprint
        //  calls LowRiseBegin once, LowRiseWall a wall; EndFacade ends it)
        // ------------------------------------------------------------------
        static bool lrActive, lrMetal;
        static int lrWall;
        static float lrFloor;
        static int lrFront = -1, lrDock = -1, lrOfficeEnd;
        static uint lrHash;
        static Vector3 lrBlockK, lrPanelK, lrTrimK;
        static Color32 lrBrickTint;
        static int lrBrickLook;
        /// <summary>The panel colours (column 7's metal, linear multipliers): off-white, beige, light grey, pale blue-grey, tan.</summary>
        static readonly Vector3[] LrPanelCols =
        {
            new Vector3(1.10f, 1.08f, 1.02f), new Vector3(1.08f, 0.98f, 0.82f), new Vector3(0.92f, 0.93f, 0.94f),
            new Vector3(0.78f, 0.86f, 0.96f), new Vector3(1.00f, 0.88f, 0.70f),
        };
        static readonly Vector3[] LrBlockCols =
        {
            new Vector3(1.30f, 1.26f, 1.18f), new Vector3(1.12f, 1.06f, 0.96f), new Vector3(0.98f, 0.98f, 0.98f), new Vector3(1.40f, 1.32f, 1.16f),
        };

        static bool LowRiseBegin(CityMap map, TileMeshes tm, int fi, CityMap.Footprint f, List<Vector2> poly, float floor, bool floating, int hero)
        {
            lrActive = false;
            if (!LowRiseLookOn || skylineBuild || proxyBuild || floating || hero >= 0 || f.roof != 0 || poly.Count < 3) return false;
            if (!LrInfoOf(fi, out var info) || info.cls != LcShed) return false;
            lrHash = LrHash(f.centre, 31);
            lrFloor = floor;
            // the wall: brick (block to the clerestory, a row of the brick column's windows) 45 %, painted
            // tilt-up concrete 35 %, ribbed metal 20 % (OSM's material first)
            int wp = (int)((lrHash >> 4) % 100u);
            lrWall = (info.flags & LrFMetal) != 0 ? 0 : (info.flags & LrFBrick) != 0 ? 2 : wp < 20 ? 0 : wp < 55 ? 1 : 2;
            lrMetal = lrWall == 0;
            lrPanelK = LrPanelCols[(lrHash >> 9) % (uint)LrPanelCols.Length];
            lrBlockK = LrBlockCols[(lrHash >> 13) % (uint)LrBlockCols.Length];
            lrTrimK = ((lrHash >> 17) & 1u) == 0u ? new Vector3(0.55f, 0.58f, 0.62f) : new Vector3(0.95f, 0.45f, 0.40f);
            lrBrickLook = LrChurchLooks[(lrHash >> 19) % (uint)LrChurchLooks.Length];
            {
                var pal = LookPalette[lrBrickLook];
                var k = pal[(int)((lrHash >> 22) % (uint)pal.Length)];
                lrBrickTint = new Color32(TintR(k.x, lrBrickLook), TintByte(k.y), TintByte(k.z), (byte)((lrBrickLook & 7) * 32));
            }
            // the front: the wall facing the nearest street (or the lot), its office at one end; the docks on the
            // longest other wall that is not a party wall
            lrFront = -1; lrDock = -1;
            int n = poly.Count;
            Vector2 toward = Vector2.zero;
            if (map.NearestRoadPoint(f.centre, 160f, true, out int ei, out float arc, out _)) toward = (map.edges[ei].PointAt(arc) - f.centre).normalized;
            float bestDot = -2f, bestLen = 0f;
            for (int i = 0; i < n; i++)
            {
                var d = poly[(i + 1) % n] - poly[i];
                float len = d.magnitude;
                if (len < 6f) continue;
                var nn = new Vector2(d.y, -d.x) / len;
                float dt = toward == Vector2.zero ? len : Vector2.Dot(nn, toward) + len * 0.002f;
                if (dt > bestDot) { bestDot = dt; lrFront = i; }
            }
            for (int i = 0; i < n; i++)
            {
                if (i == lrFront) continue;
                var a = poly[i]; var c = poly[(i + 1) % n];
                float len = Vector2.Distance(a, c);
                if (len < 14f || len <= bestLen) continue;
                var nn = new Vector2(c.y - a.y, a.x - c.x) / len;
                if (!map.FootprintClear((a + c) * 0.5f + nn * 4f, 1.5f)) continue;
                bestLen = len; lrDock = i;
            }
            lrOfficeEnd = (int)((lrHash >> 25) & 1u);
            lrActive = true;
            return true;
        }

        /// <summary>One wall of the shed (EmitFootprint's loop): the whole wall from its foot to the parapet's
        /// top; returns that top (the atlas draws nothing over it).</summary>
        static float LowRiseWall(TileMeshes tm, CityMap map, Vector2 a, Vector2 c, float y0, float y1, Vector2 n, int wi)
        {
            if (!lrActive) return y0;
            float len = Vector2.Distance(a, c);
            if (len < 0.05f || y1 - y0 < 0.3f) return y0;
            gfTm = tm; gfMap = map; gfA = a; gfD = (c - a) / len; gfN = n; gfLen = len; gfDarkUv = false;
            var keep = Bucket.Tint; bool keepOn = Bucket.FacOn, keepNc = Bucket.NoCollide; int keepLook = facLook;
            try
            {
                float F = lrFloor, T = y1;
                float baseTop = Mathf.Min(F + 1.0f, T - 1f);
                float coping = Mathf.Max(baseTop + 0.5f, T - 0.35f);
                // the body
                if (lrWall == 1)
                {
                    // tilt-up concrete: the panels' stone, a painted accent band under the coping
                    GfStone(lrBlockK);
                    GfFront(0f, len, 0f, y0, coping - 1.1f);
                    GfStone(lrTrimK * 1.25f);
                    GfFront(0f, len, 0f, coping - 1.1f, coping - 0.5f);
                    GfStone(lrBlockK);
                    GfFront(0f, len, 0f, coping - 0.5f, coping);
                }
                else if (lrMetal)
                {
                    GfStone(lrBlockK * 0.92f);
                    GfFront(0f, len, 0f, y0, baseTop);
                    GfMetal(lrPanelK);
                    GfFront(0f, len, 0f, baseTop, coping);
                }
                else
                {
                    // painted block to the clerestory, one row of the brick column's windows up there
                    float brick0 = Mathf.Clamp(F + (T - F) * 0.52f, baseTop + 1.5f, coping - 1.6f);
                    GfStone(lrBlockK);
                    GfFront(0f, len, 0f, y0, brick0);
                    var lk = FacadeLooks[lrBrickLook];
                    Bucket.Tint = lrBrickTint; Bucket.FacOn = false;
                    float ur = Mathf.Max(1f, Mathf.Round(len / lk.uM));
                    float fl = 1f / lk.floors;
                    GfBk.Wall(GP(0f, 0f, 0f), GP(len, 0f, 0f), brick0, coping, gfN, 0f, ur, fl, fl * 2f);
                }
                // the coping: a metal cap 8 cm proud along the top
                Bucket.NoCollide = true;
                GfMetal(lrTrimK);
                GfBox(0f, len, 0f, 0.08f, coping, T + 0.06f, 1 | 16 | 32);
                // a few high windows on a metal wall (every ~11 m, dark)
                if (lrMetal && wi != lrDock && T - F > 5.5f && len >= 12f)
                {
                    int nw = Mathf.FloorToInt(len / 11f);
                    float wy = Mathf.Min(coping - 1.4f, F + (T - F) * 0.66f);
                    gfDarkUv = true; GfDark(DarkGrey);
                    for (int k = 0; k < nw; k++)
                    {
                        float s = (k + 0.5f) * len / nw;
                        if (wi == lrFront && OfficeSpan(len, out float o0, out float o1) && s > o0 - 1f && s < o1 + 1f) continue;
                        GfBox(s - 0.9f, s + 0.9f, 0f, 0.05f, wy, wy + 0.9f, 1 | 4 | 8 | 16);
                    }
                    gfDarkUv = false;
                }
                Bucket.NoCollide = keepNc;
                // the office corner on the front
                if (wi == lrFront) LrOffice(len, F);
                // the docks
                if (wi == lrDock) LrDocks(len, F, T, coping);
                else if (wi == lrFront && lrDock < 0 && len >= 14f) LrDriveIn(len * (lrOfficeEnd == 0 ? 0.72f : 0.28f), F, coping);
            }
            finally
            {
                Bucket.Tint = keep; Bucket.FacOn = keepOn; Bucket.NoCollide = keepNc; facLook = keepLook; gfDarkUv = false;
            }
            return y1;
        }

        static bool OfficeSpan(float len, out float s0, out float s1)
        {
            float w = Mathf.Clamp(len * 0.3f, 6f, 14f);
            if (len < 12f) { s0 = s1 = 0f; return false; }
            if (lrOfficeEnd == 0) { s0 = 1.2f; s1 = 1.2f + w; } else { s1 = len - 1.2f; s0 = s1 - w; }
            return true;
        }

        /// <summary>The office corner: a storefront of glazing over a low sill, a glazed door in it, a flat canopy
        /// over the door and its light (the glazing lit as an office floor after dark).</summary>
        static void LrOffice(float len, float F)
        {
            if (!OfficeSpan(len, out float s0, out float s1)) return;
            Bucket.NoCollide = true;
            // the aluminium frame round the glazing
            GfMetal(new Vector3(0.55f, 0.57f, 0.60f));
            GfBox(s0 - 0.12f, s1 + 0.12f, 0f, 0.10f, F + 0.45f, F + 3.25f, 1 | 4 | 8 | 16 | 32);
            bool bronze = ((lrHash >> 27) & 1u) == 0u;
            GfGlass(s0, s1, 0.11f, F + 0.55f, F + 3.15f, bronze ? LookTeal : LookBlue, bronze ? new Vector3(1.50f, 1.25f, 0.95f) : new Vector3(1.20f, 1.32f, 1.50f), 1f, 1.5f);
            // the door: a darker pane with its frame, at the office's outer third
            float dm = lrOfficeEnd == 0 ? s0 + (s1 - s0) * 0.30f : s1 - (s1 - s0) * 0.30f;
            gfDarkUv = true; GfDark(DarkDoor);
            GfBox(dm - 0.95f, dm + 0.95f, 0.12f, 0.17f, F + 0.02f, F + 2.25f, 1 | 4 | 8 | 16);
            gfDarkUv = false;
            // the canopy over it and the light under that
            GfMetal(lrTrimK);
            float cy = F + 2.95f;
            GfBox(dm - 1.8f, dm + 1.8f, 0.10f, 1.6f, cy, cy + 0.25f, 1 | 4 | 8 | 16 | 32);
            var lampAt = GP(dm, 0.8f, cy - 0.05f);
            gfTm.lamps.Add(new Lamp { foot = new Vector3(lampAt.x, F, lampAt.z), head = lampAt, height = cy - 0.05f - F, kind = LampDeck, breakaway = true });
            // a concrete step and walk in front of the door (drawn only: flush with the ground)
            GfStone(new Vector3(1.10f, 1.08f, 1.02f));
            GfBox(dm - 1.4f, dm + 1.4f, 0.17f, 1.5f, F - 0.30f, F + 0.02f, 1 | 4 | 8 | 16);
            Bucket.NoCollide = false;
        }

        /// <summary>The loading docks on a long wall: dock-high roll-up doors (sill 1.2 m over the floor) in a
        /// row with rubber bumpers and a seal, a light over each, a drive-in door at grade at one end.</summary>
        static void LrDocks(float len, float F, float T, float coping)
        {
            const float doorW = 2.9f, pitch = 4.4f;
            float usable = len - 6f;
            int nd = Mathf.Clamp(Mathf.FloorToInt(usable / pitch), 1, 14);
            // docks along part of a very long wall (a cross-dock warehouse has them the whole length)
            if (len > 90f && ((lrHash >> 5) & 1u) == 0u) nd = Mathf.Min(nd, 6 + (int)((lrHash >> 6) % 5u));
            float run = nd * pitch;
            float s0 = lrOfficeEnd == 0 ? len - 3f - run : 3f;
            float doorTop = Mathf.Min(F + 1.2f + 3.0f, coping - 0.4f);
            if (doorTop - (F + 1.2f) < 2.2f) return;
            Bucket.NoCollide = true;
            for (int k = 0; k < nd; k++)
            {
                float dm = s0 + (k + 0.5f) * pitch;
                // the door: dark ribbed slats in a steel frame
                GfMetal(lrTrimK * 0.9f);
                GfBox(dm - doorW * 0.5f - 0.15f, dm + doorW * 0.5f + 0.15f, 0f, 0.07f, F + 1.05f, doorTop + 0.15f, 1 | 4 | 8 | 16 | 32);
                GfMetal(new Vector3(0.80f, 0.82f, 0.86f));
                GfFront(dm - doorW * 0.5f, dm + doorW * 0.5f, 0.08f, F + 1.2f, doorTop);
                // the dock seal round it (dark) and the two bumpers under the sill
                gfDarkUv = true; GfDark(DarkBlack);
                GfBox(dm - doorW * 0.5f - 0.45f, dm - doorW * 0.5f - 0.15f, 0f, 0.35f, F + 1.0f, doorTop + 0.25f, 1 | 4 | 8 | 16);
                GfBox(dm + doorW * 0.5f + 0.15f, dm + doorW * 0.5f + 0.45f, 0f, 0.35f, F + 1.0f, doorTop + 0.25f, 1 | 4 | 8 | 16);
                GfBox(dm - doorW * 0.5f - 0.45f, dm + doorW * 0.5f + 0.45f, 0f, 0.35f, doorTop + 0.25f, doorTop + 0.60f, 1 | 4 | 8 | 16 | 32);
                GfBox(dm - 1.0f, dm - 0.75f, 0f, 0.18f, F + 0.75f, F + 1.15f, 1 | 4 | 8 | 16);
                GfBox(dm + 0.75f, dm + 1.0f, 0f, 0.18f, F + 0.75f, F + 1.15f, 1 | 4 | 8 | 16);
                gfDarkUv = false;
                // a dock light over every other door
                if ((k & 1) == 0)
                {
                    var lampAt = GP(dm, 0.5f, doorTop + 0.9f);
                    gfTm.lamps.Add(new Lamp { foot = new Vector3(lampAt.x, F, lampAt.z), head = lampAt, height = doorTop + 0.9f - F, kind = LampDeck, breakaway = true });
                }
            }
            Bucket.NoCollide = false;
            float di = lrOfficeEnd == 0 ? s0 - 3.2f : s0 + run + 3.2f;
            if (di > 2.6f && di < len - 2.6f) LrDriveIn(di, F, coping);
        }

        /// <summary>A drive-in door at grade: a tall roll-up door in a frame, with a pair of yellow bollards.</summary>
        static void LrDriveIn(float dm, float F, float coping)
        {
            const float w = 3.7f;
            float top = Mathf.Min(F + 4.3f, coping - 0.4f);
            if (top - F < 3f || dm - w * 0.5f < 0.5f || dm + w * 0.5f > gfLen - 0.5f) return;
            Bucket.NoCollide = true;
            GfMetal(lrTrimK * 0.9f);
            GfBox(dm - w * 0.5f - 0.15f, dm + w * 0.5f + 0.15f, 0f, 0.07f, F, top + 0.15f, 1 | 4 | 8 | 16);
            GfMetal(new Vector3(0.80f, 0.82f, 0.86f));
            GfFront(dm - w * 0.5f, dm + w * 0.5f, 0.08f, F, top);
            GfMetal(new Vector3(1.60f, 1.35f, 0.40f));
            GfBox(dm - w * 0.5f - 0.55f, dm - w * 0.5f - 0.25f, 0.3f, 0.6f, F - 0.2f, F + 1.1f, 1 | 2 | 4 | 8 | 16);
            GfBox(dm + w * 0.5f + 0.25f, dm + w * 0.5f + 0.55f, 0.3f, 0.6f, F - 0.2f, F + 1.1f, 1 | 2 | 4 | 8 | 16);
            Bucket.NoCollide = false;
        }

        // ------------------------------------------------------------------
        //  THE CHURCH: a brick nave on its oriented box under a gable roof,
        //  white gables, a door at the street end and (most of the smaller
        //  ones) a steeple there - a square brick tower, a white belfry with
        //  louvres, a spire
        // ------------------------------------------------------------------
        static bool LowRiseChurch(CityMap map, Trims trims, TileMeshes tm, int fi, CityMap.Footprint f, float y0, float top)
        {
            if (!LowRiseLookOn || skylineBuild && !proxyBuild || !LrInfoOf(fi, out var info) || info.cls != LcChurch) return false;
            var centre = f.centre; var u = f.u; float hu = f.hu, hv = f.hv;
            int fit = proxyBuild ? 0 : FitHouse(map, trims, ref centre, ref u, ref hu, ref hv, y0, top);
            if (fit < 0) { tm.footprintsLeftOut++; return true; }
            if (fit > 0) tm.footprintsCut++;
            if (hu < 3f || hv < 2.5f) { tm.footprintsLeftOut++; return true; }
            float floor = y0 + BuildingSink;
            float wallH = info.wallH > 1f ? info.wallH : Mathf.Max(5f, f.h * 0.6f);
            float rise = Mathf.Clamp(hv * 0.80f, 2.0f, 8f);
            float eave = floor + wallH, ridge = eave + rise;
            uint h = LrHash(centre, 53);
            // the street end
            int fe = 1;
            if (map.NearestRoadPoint(centre, 120f, true, out int ei, out float arc, out _))
                fe = Vector2.Dot(map.edges[ei].PointAt(arc) - centre, u) >= 0f ? 1 : -1;
            var v = new Vector2(-u.y, u.x);
            var c1 = centre + u * hu + v * hv; var c2 = centre - u * hu + v * hv;
            var c3 = centre - u * hu - v * hv; var c4 = centre + u * hu - v * hv;
            int look = LrChurchLooks[LookDraw(h) % LrChurchLooks.Length];
            // one tall storey: the brick column's window stretched to a church's tall window
            LrSetLook(centre, look, floor, wallH, wallH, false);
            var wallTint = Bucket.Tint;
            EmitFacadeQuad(tm, Slot.FacadeGlass, c2, c1, y0, eave, v);
            EmitFacadeQuad(tm, Slot.FacadeGlass, c1, c4, y0, eave, u);
            EmitFacadeQuad(tm, Slot.FacadeGlass, c4, c3, y0, eave, -v);
            EmitFacadeQuad(tm, Slot.FacadeGlass, c3, c2, y0, eave, -u);
            var bk = buckets[(int)Slot.FacadeGlass];
            // the white gables (a pediment's board), their raking trim
            var white = ((h >> 3) & 3u) == 0u ? new Vector3(1.35f, 1.30f, 1.18f) : new Vector3(1.70f, 1.68f, 1.62f);
            PartsTint(white, false);
            var rN = centre + u * hu; var rS = centre - u * hu;
            LrTri(bk, tm, c1, rN, c4, eave, ridge, u);
            LrTri(bk, tm, c3, rS, c2, eave, ridge, -u);
            // the roof: two planes past the walls (0.4 m at the eaves, 0.3 m at the gables), both faces
            {
                var roof = buckets[(int)Slot.RoofTiles];
                float ox = 0.4f, oz = 0.3f, drop = ox * rise / hv;
                Vector2 e1 = centre + u * (hu + oz) + v * (hv + ox), e2 = centre - u * (hu + oz) + v * (hv + ox);
                Vector2 e3 = centre - u * (hu + oz) - v * (hv + ox), e4 = centre + u * (hu + oz) - v * (hv + ox);
                Vector2 r1 = centre + u * (hu + oz), r2 = centre - u * (hu + oz);
                float uRep = Mathf.Max(1f, Mathf.Round((hu + oz) * 2f / RoofTileM));
                float vRep = Mathf.Max(1f, Mathf.Round(Mathf.Sqrt((hv + ox) * (hv + ox) + (rise + drop) * (rise + drop)) / RoofTileM));
                float ye = eave - drop;
                var keepT = Bucket.Tint;
                roof.Up(L(r2, ridge, tm), L(r1, ridge, tm), L(e1, ye, tm), L(e2, ye, tm), new Vector2(0f, 0f), new Vector2(uRep, 0f), new Vector2(uRep, vRep), new Vector2(0f, vRep));
                roof.Up(L(e3, ye, tm), L(e4, ye, tm), L(r1, ridge, tm), L(r2, ridge, tm), new Vector2(0f, vRep), new Vector2(uRep, vRep), new Vector2(uRep, 0f), new Vector2(0f, 0f));
                bool nc = Bucket.NoCollide; Bucket.NoCollide = true;
                roof.Down(L(r2, ridge - 0.05f, tm), L(r1, ridge - 0.05f, tm), L(e1, ye - 0.05f, tm), L(e2, ye - 0.05f, tm), new Vector2(0f, 0f), new Vector2(uRep, 0f), new Vector2(uRep, vRep), new Vector2(0f, vRep));
                roof.Down(L(e3, ye - 0.05f, tm), L(e4, ye - 0.05f, tm), L(r1, ridge - 0.05f, tm), L(r2, ridge - 0.05f, tm), new Vector2(0f, vRep), new Vector2(uRep, vRep), new Vector2(uRep, 0f), new Vector2(0f, 0f));
                Bucket.NoCollide = nc;
                Bucket.Tint = keepT;
            }
            // the street end: a steeple (most of the smaller ones), else the door in the gable wall
            bool steeple = !proxyBuild && info.area < 1400f && ((h >> 7) % 100u) < 70u && hv >= 4f;
            Vector2 endA = fe > 0 ? c4 : c2, endC = fe > 0 ? c1 : c3;   // the street gable, left to right seen from outside
            Vector2 endN = u * fe;
            if (steeple)
            {
                float w = Mathf.Clamp(hv * 0.85f, 3.2f, 5.5f) * 0.5f;
                var tc = centre + u * fe * (hu - w + 0.3f);
                float towerTop = ridge + 2.2f;
                Bucket.Tint = wallTint;
                LrSetLook(tc, look, floor, towerTop - floor, wallH, false);
                LrBoxAtlas(tm, tc, u, w, w, y0, towerTop);
                // the belfry: a white box with dark louvres, a cornice, the spire
                float bw = w * 0.82f, b0 = towerTop, b1 = towerTop + 3.4f;
                PartsTint(white, false);
                LrBoxParts(tm, tc, u, w + 0.15f, w + 0.15f, towerTop - 0.30f, towerTop + 0.05f, true);
                LrBoxParts(tm, tc, u, bw, bw, b0, b1, false);
                LrBoxParts(tm, tc, u, bw + 0.2f, bw + 0.2f, b1, b1 + 0.35f, true);
                bool nc = Bucket.NoCollide; Bucket.NoCollide = true;
                Bucket.Tint = new Color32(TintR(0.36f, LookBrick), TintByte(0.62f), TintByte(0.88f), (byte)(LookBrick * 32));
                foreach (var dir in new[] { u, v, -u, -v })
                {
                    var side = new Vector2(-dir.y, dir.x);
                    var pa = tc + dir * (bw + 0.03f) - side * (bw * 0.45f); var pc = tc + dir * (bw + 0.03f) + side * (bw * 0.45f);
                    bk.Wall(L(pa, 0f, tm), L(pc, 0f, tm), b0 + 0.7f, b1 - 0.6f, dir, GfDarkU0, GfDarkU1, GfDarkV0, GfDarkV1);
                }
                // the spire: four triangles to a point, metal
                PartsTint(((h >> 11) & 1u) == 0u ? new Vector3(0.45f, 0.47f, 0.50f) : white, true);
                float sy0 = b1 + 0.35f, sy1 = sy0 + bw * 2f * 2.6f;
                var apex = L(tc, sy1, tm);
                Vector2[] sq = { tc + u * bw + v * bw, tc - u * bw + v * bw, tc - u * bw - v * bw, tc + u * bw - v * bw };
                for (int k = 0; k < 4; k++)
                {
                    var pa = sq[k]; var pb = sq[(k + 1) % 4];
                    var mid = (pa + pb) * 0.5f - tc;
                    var A = L(pa, sy0, tm); var B = L(pb, sy0, tm);
                    var facing = new Vector3(mid.x, 0.3f, mid.y);
                    if (Vector3.Dot(Vector3.Cross(apex - A, B - A), facing) >= 0f) bk.Tri(A, B, apex, new Vector2(0f, PartV(sy0)), new Vector2(bw, PartV(sy0)), new Vector2(bw * 0.5f, PartV(sy1)));
                    else bk.Tri(A, apex, B, new Vector2(0f, PartV(sy0)), new Vector2(bw * 0.5f, PartV(sy1)), new Vector2(bw, PartV(sy0)));
                }
                Bucket.NoCollide = nc;
                // the door in the tower's front
                endA = tc + u * fe * w - v * fe * w; endC = tc + u * fe * w + v * fe * w;
                endN = u * fe;
            }
            // the double door: white surround, dark leaves, a step
            if (!proxyBuild)
            {
                gfTm = tm; gfMap = map;
                float len = Vector2.Distance(endA, endC);
                gfA = endA; gfD = (endC - endA) / Mathf.Max(0.01f, len); gfN = endN; gfLen = len;
                if (Vector2.Dot(new Vector2(gfD.y, -gfD.x), gfN) < 0f) { gfA = endC; gfD = -gfD; }
                float dm = len * 0.5f;
                bool nc = Bucket.NoCollide; Bucket.NoCollide = true;
                PartsTint(white, false);
                GfBox(dm - 1.55f, dm + 1.55f, 0f, 0.12f, floor - 0.1f, floor + 3.6f, 1 | 4 | 8 | 16);
                gfDarkUv = true; GfDark(DarkRed * 0.7f);
                GfBox(dm - 1.15f, dm + 1.15f, 0.12f, 0.16f, floor, floor + 3.0f, 1 | 4 | 8 | 16);
                gfDarkUv = false;
                GfStone(new Vector3(1.10f, 1.08f, 1.02f));
                GfBox(dm - 1.8f, dm + 1.8f, 0.12f, 1.4f, floor - 0.35f, floor + 0.05f, 1 | 4 | 8 | 16);
                Bucket.NoCollide = nc;
            }
            EndFacade();
            tm.footprintCount++;
            return true;
        }

        static void LrTri(Bucket bk, TileMeshes tm, Vector2 a, Vector2 apex, Vector2 c, float eave, float ridge, Vector2 outward)
        {
            Vector2 d = c - a;
            if (Vector2.Dot(new Vector2(-d.y, d.x), outward) < 0f) { var t = a; a = c; c = t; }
            float w = Vector2.Distance(a, c);
            bk.Tri(L(a, eave, tm), L(apex, ridge, tm), L(c, eave, tm),
                   new Vector2(0f, PartV(eave)), new Vector2(w * 0.25f, PartV(ridge)), new Vector2(w * 0.5f, PartV(eave)));
        }

        /// <summary>A box on the atlas (the current look) round centre, its walls only.</summary>
        static void LrBoxAtlas(TileMeshes tm, Vector2 c, Vector2 u, float hu, float hv, float y0, float y1)
        {
            var v = new Vector2(-u.y, u.x);
            var c1 = c + u * hu + v * hv; var c2 = c - u * hu + v * hv; var c3 = c - u * hu - v * hv; var c4 = c + u * hu - v * hv;
            EmitFacadeQuad(tm, Slot.FacadeGlass, c2, c1, y0, y1, v);
            EmitFacadeQuad(tm, Slot.FacadeGlass, c1, c4, y0, y1, u);
            EmitFacadeQuad(tm, Slot.FacadeGlass, c4, c3, y0, y1, -v);
            EmitFacadeQuad(tm, Slot.FacadeGlass, c3, c2, y0, y1, -u);
            var roof = buckets[(int)Slot.RoofFlat];
            roof.Up(L(c1, y1, tm), L(c2, y1, tm), L(c3, y1, tm), L(c4, y1, tm), c1 / RoofFlatM, c2 / RoofFlatM, c3 / RoofFlatM, c4 / RoofFlatM);
        }

        /// <summary>A box in column 7 (the current parts tint): four walls, a top and (under) a bottom.</summary>
        static void LrBoxParts(TileMeshes tm, Vector2 c, Vector2 u, float hu, float hv, float y0, float y1, bool under)
        {
            var bk = buckets[(int)Slot.FacadeGlass];
            var v = new Vector2(-u.y, u.x);
            var c1 = c + u * hu + v * hv; var c2 = c - u * hu + v * hv; var c3 = c - u * hu - v * hv; var c4 = c + u * hu - v * hv;
            float w0 = PartV(y0), w1 = PartV(y1);
            bk.Wall(L(c2, 0f, tm), L(c1, 0f, tm), y0, y1, v, 0f, hu, w0, w1);
            bk.Wall(L(c1, 0f, tm), L(c4, 0f, tm), y0, y1, u, 0f, hv, w0, w1);
            bk.Wall(L(c4, 0f, tm), L(c3, 0f, tm), y0, y1, -v, 0f, hu, w0, w1);
            bk.Wall(L(c3, 0f, tm), L(c2, 0f, tm), y0, y1, -u, 0f, hv, w0, w1);
            bk.Up(L(c1, y1, tm), L(c2, y1, tm), L(c3, y1, tm), L(c4, y1, tm), new Vector2(0f, w1), new Vector2(hu, w1), new Vector2(hu, w1 + hv / 4f), new Vector2(0f, w1 + hv / 4f));
            if (under) bk.Down(L(c1, y0, tm), L(c2, y0, tm), L(c3, y0, tm), L(c4, y0, tm), new Vector2(0f, w0), new Vector2(hu, w0), new Vector2(hu, w0 + hv / 4f), new Vector2(0f, w0 + hv / 4f));
        }
    }
}
