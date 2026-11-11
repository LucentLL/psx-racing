using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  THE UPTOWN STREET FURNITURE and THE DRESS CORRECTIONS (2026-11-10,
    //  the second round of the street dress; the frames are
    //  Editor/CityRefSpots.Uptown.cs's spots).
    //
    //  FURNITURE (PSX_CITY_STREETFURN=0: none of it). What Tryon and Trade
    //  Streets carry between their trees that the first round left out:
    //    the lamps    TWIN-GLOBE ornamental posts, not the side streets'
    //                 single acorn: a black post, a crossarm along the kerb,
    //                 a globe at each end (Lamp kind LampTwin: placed, lit and
    //                 collided exactly as the acorn post it replaces; CityPoles
    //                 draws it).
    //    the walk     in the tree line (a metre behind the kerb), a quarter
    //                 of a gap from each tree: a BENCH and a litter BIN every
    //                 fourth gap, a square PLANTER every fourth, on each side -
    //                 only where the walk is whole there (no building, drive,
    //                 apron, other road, junction fan, deck or skybridge), as
    //                 the trees are. Solid as drawn (the buildings mesh), and
    //                 drawn only in a race route's run-off, as its posts are.
    //    the Square   Trade and Tryon: a bronze figure on a granite pedestal
    //                 at each of the four corners, three black bollards before
    //                 it (a table of one place).
    //  All in the facade atlas's bucket, with the ground floors' surfaces
    //  (CityMeshes.GroundFloors.cs): no material, no draw call, no object.
    //
    //  CORRECTIONS to the first round (PSX_CITY_DRESSFIX=0: as it was):
    //    the trees    a side street's pitch is its own (SidePitchOf: what its
    //                 frames show - East 3rd Street has a tree every 25 m,
    //                 not every 12), not one number for every side street;
    //    the bridges  a skybridge is its own walkway (SkyLooks): West 4th
    //                 Street's climbs from one building's floor to the
    //                 other's under a glass roof, East Trade's is banded and
    //                 mostly opaque, South College's a white box with a
    //                 window ribbon, East 5th's tan with punched bands.
    //  (The paver's colour is the texture's own: tools/city/paver_walk.py.)
    // ======================================================================
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_STREETFURN=0: no twin posts, benches, bins, planters or statues (the before).</summary>
        public static bool StreetFurnitureOn = System.Environment.GetEnvironmentVariable("PSX_CITY_STREETFURN") != "0";
        /// <summary>PSX_CITY_DRESSFIX=0: one tree pitch for every side street, every skybridge one glazed box (the first round).</summary>
        public static bool DressFixOn = System.Environment.GetEnvironmentVariable("PSX_CITY_DRESSFIX") != "0";

        /// <summary>Tryon and Trade Streets' twin-globe post: an acorn post's rules, two globes on a crossarm.</summary>
        public const byte LampTwin = 5;
        /// <summary>An ornamental pedestrian post (the acorn or the twin globe): no arm, the acorn's pitch, light and collider.</summary>
        public static bool IsAcornKind(byte kind) => kind == LampAcorn || kind == LampTwin;

        public static class FurnitureStats
        {
            public static int benches, bins, planters, statues, bollards, refused, drawnOnly;
            public static void Reset() { benches = bins = planters = statues = bollards = refused = drawnOnly = 0; }
            public static string Line() =>
                $"street furniture: benches {benches}, bins {bins}, planters {planters}, statues {statues}, bollards {bollards} ({drawnOnly} drawn only, in race run-off; {refused} stations refused: no whole walk there)";
        }

        // ---- the trees' pitch on a side street (the frames) ----
        static readonly (string name, float pitch)[] SidePitches =
        {
            ("3rd Street", 25f), ("4th Street", 20f), ("Church Street", 20f), ("Martin Luther King", 18f),
            ("6th Street", 15f), ("College Street", 13f), ("Brooklyn Village", 14f), ("7th Street", 14f),
            ("5th Street", 12f), ("Brevard Street", 12f),
        };
        /// <summary>A side street's tree pitch: its own where its frames were read, 14 m otherwise (the first round: 12 m everywhere).</summary>
        public static float SidePitchOf(CityMap.Edge e)
        {
            if (!DressFixOn) return TreePitchSideM;
            if (e.name != null)
                foreach (var (name, pitch) in SidePitches)
                    if (e.name.Contains(name)) return pitch;
            return 14f;
        }

        // ---- the skybridges' own character ----
        public struct SkyLook
        {
            public string id; public float band, rise; public Vector3 bandK; public bool glassRoof; public int glass; public Vector3 glassK;
            public SkyLook(string id, float band, float rise, Vector3 bandK, bool glassRoof, int glass, Vector3 glassK)
            { this.id = id; this.band = band; this.rise = rise; this.bandK = bandK; this.glassRoof = glassRoof; this.glass = glass; this.glassK = glassK; }
        }
        /// <summary>id, the opaque band's height top and bottom (m), the rise from its first end to its second (m), the band's stone, a glass roof, the glazing.</summary>
        static readonly SkyLook[] SkyLooks =
        {
            new SkyLook("w4th_tryon_church", 0.45f, 2.6f, new Vector3(0.55f, 0.58f, 0.60f), true, LookTeal, new Vector3(1.20f, 1.50f, 1.40f)),
            new SkyLook("etrade_tryon_college", 1.45f, 0f, new Vector3(0.62f, 0.56f, 0.50f), false, LookBlue, new Vector3(1.10f, 1.10f, 1.10f)),
            new SkyLook("scollege_3rd_4th", 1.25f, 0f, new Vector3(1.55f, 1.53f, 1.48f), false, LookBlue, new Vector3(1.00f, 1.05f, 1.10f)),
            new SkyLook("e5th_tryon_college", 1.30f, 0f, new Vector3(1.20f, 0.98f, 0.80f), false, LookBlue, new Vector3(1.20f, 1.25f, 1.30f)),
            new SkyLook("emlk_tryon_college", 0.50f, 0f, new Vector3(0.90f, 0.92f, 0.95f), false, LookTeal, new Vector3(1.40f, 1.60f, 1.60f)),
            new SkyLook("e4th_tryon_college", 0.70f, 0f, new Vector3(1.30f, 1.28f, 1.22f), false, LookSilver, new Vector3(0.90f, 0.95f, 1.00f)),
        };

        /// <summary>A skybridge drawn as itself (the header): false, the first round's glazed box stands.</summary>
        static bool EmitSkybridgeLook(TileMeshes tm, in Skybridge b, float y0, float y1)
        {
            if (!DressFixOn) return false;
            int li = -1;
            for (int q = 0; q < SkyLooks.Length; q++) if (SkyLooks[q].id == b.id) { li = q; break; }
            if (li < 0) return false;
            var lk = SkyLooks[li];
            var bk = buckets[(int)Slot.FacadeGlass];
            var keep = Bucket.Tint; bool keepOn = Bucket.FacOn;
            float hu = b.len * 0.5f + SkybridgeIntoM, hv = b.wide * 0.5f;
            var v = new Vector2(-b.u.y, b.u.x);
            // its two ends' floors: level, or climbing from the first to the second
            float fa = y0 - lk.rise * 0.5f + (lk.rise > 0f ? lk.rise * 0.5f : 0f), fb = fa + lk.rise;
            float tall = y1 - y0, band = Mathf.Min(lk.band, tall * 0.4f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 pa = b.c - b.u * hu + v * (side * hv), pb = b.c + b.u * hu + v * (side * hv);
                Vector3 la = L(pa, 0f, tm), lb = L(pb, 0f, tm);
                var n = v * side;
                float uLen = 2f * hu;
                PartsTint(lk.bandK, false);
                bk.WallSloped(la, lb, fa, fa + band, fb, fb + band, n, 0f, uLen * 0.5f, 0f, band / 8f);
                bk.WallSloped(la, lb, fa + tall - band, fa + tall, fb + tall - band, fb + tall, n, 0f, uLen * 0.5f, 0.3f, 0.3f + band / 8f);
                var gl = FacadeLooks[lk.glass];
                Bucket.Tint = new Color32(TintR(lk.glassK.x, lk.glass), TintByte(lk.glassK.y), TintByte(lk.glassK.z), (byte)((lk.glass & 7) * 32));
                Bucket.FacOn = false;
                float bays = Mathf.Max(1f, Mathf.Round(uLen / 1.9f)) / 8f;
                bk.WallSloped(la, lb, fa + band, fa + tall - band, fb + band, fb + tall - band, n, 0f, bays, 1f / gl.floors, 2f / gl.floors);
            }
            // the roof (glass over West 4th's) and the soffit
            Vector3 r0 = L(b.c - b.u * hu - v * hv, fa + tall, tm), r1 = L(b.c + b.u * hu - v * hv, fb + tall, tm),
                    r2 = L(b.c + b.u * hu + v * hv, fb + tall, tm), r3 = L(b.c - b.u * hu + v * hv, fa + tall, tm);
            if (lk.glassRoof)
                Bucket.Tint = new Color32(TintR(lk.glassK.x, lk.glass), TintByte(lk.glassK.y), TintByte(lk.glassK.z), (byte)((lk.glass & 7) * 32));
            else PartsTint(lk.bandK * 0.9f, false);
            var u0 = new Vector2(0f, 0.15f);
            bk.Up(r0, r1, r2, r3, u0, u0 + new Vector2(1f, 0f), u0 + new Vector2(1f, 0.1f), u0 + new Vector2(0f, 0.1f));
            PartsTint(lk.bandK * 0.8f, false);
            var d = Vector3.up * tall;
            bk.Down(r0 - d, r1 - d, r2 - d, r3 - d, u0, u0 + new Vector2(1f, 0f), u0 + new Vector2(1f, 0.1f), u0 + new Vector2(0f, 0.1f));
            Bucket.Tint = keep; Bucket.FacOn = keepOn;
            return true;
        }

        // ------------------------------------------------------------------
        //  THE WALK'S FURNITURE
        // ------------------------------------------------------------------
        static readonly HashSet<int> furnEdges = new HashSet<int>();

        /// <summary>The benches, bins and planters of the main streets' walks on this tile, and the Square's statues: into the tile's building mesh.</summary>
        static void EmitStreetFurniture(CityMap map, Trims trims, TileMeshes tm, int tx, int tz)
        {
            if (!StreetFurnitureOn || !StreetDressOn || skylineBuild || proxyBuild) return;
            var o = tm.origin;
            furnEdges.Clear();
            dressSegs.Clear();
            map.EdgeSegsInRect(new Vector2(o.x - 8f, o.z - 8f), new Vector2(o.x + TileSize + 8f, o.z + TileSize + 8f), dressSegs);
            foreach (int packed in dressSegs) furnEdges.Add(packed >> 12);
            var keep = Bucket.Tint; bool keepOn = Bucket.FacOn, keepNc = Bucket.NoCollide;
            gfMap = map; gfTrims = trims; gfTm = tm;
            foreach (int ei in furnEdges)
            {
                var e = map.edges[ei];
                if (StreetDressOf(map, e) != DressMain) continue;
                if (!StreetTreePlan(map, trims, e, out float lo, out float step, out int n) || n < 2) continue;
                for (int k = 0; k < n; k++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int what = (k + (side > 0 ? 2 : 0)) & 3;       // 1 a bench and bin, 3 a planter; the sides staggered
                        if (what != 1 && what != 3) continue;
                        float s = lo + (k + 0.75f) * step;
                        if (s > e.length - 2f) continue;
                        RoadsideOccupancy.RoadEdgeAt(e, trims, s, out var c, out var right, out float hl, out float hr);
                        var outw = right * side;
                        var p = c + outw * ((side > 0 ? hr : hl) + TreeOutM + 0.15f);
                        if (p.x < o.x || p.x >= o.x + TileSize || p.y < o.z || p.y >= o.z + TileSize) continue;
                        var along = e.TangentAt(s);
                        if (!FurnitureFits(map, trims, e, s, p, along, 1.3f)) { FurnitureStats.refused++; continue; }
                        float y = e.YAt(s) + CurbH + 0.02f;
                        bool runOff = RaceRunOff.Inside(map, trims, p);
                        Bucket.NoCollide = runOff;
                        if (runOff) FurnitureStats.drawnOnly++;
                        gfA = p; gfD = along; gfN = outw; gfLen = 1f;
                        if (what == 1) { FurnBench(y); FurnBin(y, 1.55f); }
                        else FurnPlanter(y);
                    }
            }
            EmitTheSquare(map, trims, tm);
            Bucket.Tint = keep; Bucket.FacOn = keepOn; Bucket.NoCollide = keepNc; gfDarkUv = false;
        }

        /// <summary>Is the walk whole round p (the trees' own tests): no building, drive, apron, other road, fan, deck or skybridge within <paramref name="half"/> along the kerb?</summary>
        static bool FurnitureFits(CityMap map, Trims trims, CityMap.Edge e, float s, Vector2 p, Vector2 along, float half)
        {
            if (OffOwnRoad(e, trims, s, p) < 0.7f) return false;
            if (UnderSkybridge(map, p, 1.5f) || NearDeck(map, trims, p, 1.5f) || InFanDisc(map, trims, p, 1.0f)) return false;
            float y = e.YAt(s) + CurbH;
            for (int q = -1; q <= 1; q++)
            {
                var c = p + along * (q * half);
                if (!map.FootprintClear(c, 0.9f) || LampInLot(c) || OnDriveway(c, 1.0f) || ApronAt(map, c.x, c.y)) return false;
                if (OnPavement(map, trims, c, y, e.index, e.index)) return false;
            }
            return true;
        }

        /// <summary>A bench along the kerb: two black cast ends, a wooden seat and back (it faces the buildings).</summary>
        static void FurnBench(float y)
        {
            const float hl = 0.85f;
            gfDarkUv = true; GfDark(DarkBlack);
            GfBox(-hl, -hl + 0.07f, -0.28f, 0.28f, y - 0.3f, y + 0.82f, 1 | 2 | 4 | 8 | 16);
            GfBox(hl - 0.07f, hl, -0.28f, 0.28f, y - 0.3f, y + 0.82f, 1 | 2 | 4 | 8 | 16);
            gfDarkUv = false; GfStone(new Vector3(0.78f, 0.55f, 0.38f));   // the slats: a warm brown
            GfBox(-hl + 0.07f, hl - 0.07f, -0.24f, 0.24f, y + 0.40f, y + 0.46f, 1 | 2 | 16 | 32);
            GfBox(-hl + 0.07f, hl - 0.07f, -0.28f, -0.22f, y + 0.46f, y + 0.82f, 1 | 2 | 16);
            gfDarkUv = false;
            FurnitureStats.benches++;
        }

        /// <summary>A litter bin, <paramref name="at"/> along the kerb from the bench: a black drum under a lid.</summary>
        static void FurnBin(float y, float at)
        {
            gfDarkUv = true; GfDark(DarkBlack);
            GfBox(at - 0.27f, at + 0.27f, -0.27f, 0.27f, y - 0.3f, y + 0.90f, 1 | 2 | 4 | 8 | 16);
            GfBox(at - 0.31f, at + 0.31f, -0.31f, 0.31f, y + 0.90f, y + 0.98f, 1 | 2 | 4 | 8 | 16 | 32);
            gfDarkUv = false;
            FurnitureStats.bins++;
        }

        /// <summary>A square stone planter, planted.</summary>
        static void FurnPlanter(float y)
        {
            gfDarkUv = false;
            GfStone(new Vector3(0.98f, 0.94f, 0.86f));
            GfBox(-0.62f, 0.62f, -0.62f, 0.62f, y - 0.3f, y + 0.55f, 1 | 2 | 4 | 8 | 16);
            gfDarkUv = true; GfDark(DarkLeaf);
            GfBox(-0.50f, 0.50f, -0.50f, 0.50f, y + 0.55f, y + 1.00f, 1 | 2 | 4 | 8 | 16);
            gfDarkUv = false;
            FurnitureStats.planters++;
        }

        // ---- the Square: Trade and Tryon ----
        static readonly Vector2 SquareAt = new Vector2((float)((-80.84309 + 80.81770185962013) * 111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)),
                                                       (float)((35.22721 - 35.18456015184093) * 111132.0));

        /// <summary>The four corner statues of the Square and the bollards before them, where the corners are clear.</summary>
        static void EmitTheSquare(CityMap map, Trims trims, TileMeshes tm)
        {
            var o = tm.origin;
            if (Mathf.Abs(o.x + TileSize * 0.5f - SquareAt.x) > TileSize || Mathf.Abs(o.z + TileSize * 0.5f - SquareAt.y) > TileSize) return;
            // the junction: the node nearest the Square's point where a Tryon and a Trade edge meet
            int node = -1; float best = 40f;
            dressSegs.Clear();
            map.EdgeSegsInRect(SquareAt - Vector2.one * 40f, SquareAt + Vector2.one * 40f, dressSegs);
            foreach (int packed in dressSegs)
            {
                var e = map.edges[packed >> 12];
                for (int end = 0; end < 2; end++)
                {
                    int nd = end == 0 ? e.a : e.b;
                    float d = Vector2.Distance(map.nodes[nd], SquareAt);
                    if (d >= best) continue;
                    bool tryon = false, trade = false;
                    foreach (int oi in map.nodeEdges[nd])
                    {
                        string nm = map.edges[oi].name ?? "";
                        tryon |= nm.EndsWith("Tryon Street"); trade |= nm.EndsWith("Trade Street");
                    }
                    if (tryon && trade) { best = d; node = nd; }
                }
            }
            if (node < 0) return;
            var c = map.nodes[node];
            // its arms' directions: Tryon's and Trade's
            Vector2 t = Vector2.zero, r = Vector2.zero; float reach = 0f;
            foreach (int oi in map.nodeEdges[node])
            {
                var e = map.edges[oi];
                var dir = e.a == node ? e.TangentAt(0f) : -e.TangentAt(e.length);
                string nm = e.name ?? "";
                if (nm.EndsWith("Tryon Street") && t == Vector2.zero) t = dir;
                if (nm.EndsWith("Trade Street") && r == Vector2.zero) r = dir;
                reach = Mathf.Max(reach, e.width * 0.5f);
            }
            if (t == Vector2.zero || r == Vector2.zero) return;
            float y0 = map.nodeY[node];
            for (int q = 0; q < 4; q++)
            {
                var diag = ((q & 1) == 0 ? t : -t) + ((q & 2) == 0 ? r : -r);
                diag.Normalize();
                // out along the corner's diagonal until the walk is whole and clear
                Vector2 p = c; bool ok = false;
                for (float d = reach * 1.4142f + 3.0f; d < reach * 1.4142f + 18f; d += 0.5f)
                {
                    p = c + diag * d;
                    if (!map.FootprintClear(p, 1.3f)) break;   // the corner's building: no room
                    bool clear = true;
                    for (int k = 0; k < 4 && clear; k++)
                        clear = !OnPavement(map, trims, p + new Vector2((k & 1) == 0 ? 2.4f : -2.4f, (k & 2) == 0 ? 2.4f : -2.4f) * 0.7071f, y0 + CurbH, -1, -1);
                    if (clear && !OnPavement(map, trims, p - diag * 2.6f, y0 + CurbH, -1, -1)) { ok = true; break; }
                }
                if (!ok) { FurnitureStats.refused++; continue; }
                if (p.x < o.x || p.x >= o.x + TileSize || p.y < o.z || p.y >= o.z + TileSize) continue;
                float y = Mathf.Max(CityElevation.GroundY(map, p.x, p.y), y0) + CurbH;
                bool runOff = RaceRunOff.Inside(map, trims, p);
                Bucket.NoCollide = runOff;
                gfA = p; gfN = -diag; gfD = new Vector2(-gfN.y, gfN.x); gfLen = 1f;
                // the pedestal: granite, stepped
                gfDarkUv = false;
                GfStone(new Vector3(0.78f, 0.74f, 0.72f));
                GfBox(-1.0f, 1.0f, -1.0f, 1.0f, y - 0.4f, y + 0.45f, 1 | 2 | 4 | 8 | 16);
                GfBox(-0.75f, 0.75f, -0.75f, 0.75f, y + 0.45f, y + 2.4f, 1 | 2 | 4 | 8 | 16);
                // the figure: dark bronze, a body, shoulders and a head
                gfDarkUv = true; GfDark(DarkBronze);
                GfBox(-0.42f, 0.42f, -0.34f, 0.34f, y + 2.4f, y + 4.3f, 1 | 2 | 4 | 8 | 16);
                GfBox(-0.62f, 0.62f, -0.30f, 0.30f, y + 4.3f, y + 5.0f, 1 | 2 | 4 | 8 | 16 | 32);
                GfBox(-0.22f, 0.22f, -0.22f, 0.22f, y + 5.0f, y + 5.6f, 1 | 2 | 4 | 8 | 16);
                FurnitureStats.statues++;
                // three black bollards between it and the corner
                GfDark(DarkBlack);
                for (int k = -1; k <= 1; k++)
                {
                    var bp = p - diag * 2.0f + gfD * (k * 1.5f);
                    if (OnPavement(map, trims, bp, y0 + CurbH, -1, -1)) continue;
                    GfBox(k * 1.5f - 0.10f, k * 1.5f + 0.10f, 1.9f, 2.1f, y - 0.4f, y + 0.95f, 1 | 2 | 4 | 8 | 16);
                    FurnitureStats.bollards++;
                }
                gfDarkUv = false;
            }
            Bucket.NoCollide = false;
        }
    }
}
