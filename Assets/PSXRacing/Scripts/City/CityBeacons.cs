using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE SKYLINE'S LIGHTS (the uptown night pass, 2026-10-16). Two kinds of
    /// light that make a real skyline read at night from kilometres out, and
    /// that UG2's Bayview has on every tower top:
    ///   * RED AVIATION BEACONS on every tower whose top stands
    ///     <see cref="MinTowerM"/> or more over its ground - one at its highest
    ///     point (a spire's tip, a crown's top), flashing slowly (PSX/Halo:
    ///     BEACON_PERIOD), each tower in its own phase;
    ///   * STEADY RED OBSTRUCTION LIGHTS on the corners of the same towers'
    ///     roofs - the owner's 1990s photograph of uptown has a red light on
    ///     each top corner of its towers (the 1990s city, 2026-10-29). The LED
    ///     edge strips that outlined the open-frame tower's crown (2026-10-29
    ///     morning) are gone: "LEDs were not common" - its white frame is
    ///     floodlit instead (CityMeshes.HeroOpenFrame).
    /// Both are halos (PSX/Halo, uv1.y &lt; 0: squeezed onto the far skyline
    /// as PSX/Lit squeezes the tower, hazed by distance rather than faded with
    /// the fog band), ONE mesh and ONE draw for the whole city, built once from
    /// the far skyline's towers (<see cref="CitySkyline"/>), which are uptown's
    /// tallest - every tower of 90 m is among them. Lit with the street lamps
    /// (<see cref="NightGlow.On"/>), off in fog, like the skyline.
    ///
    /// THE LIGHTS ON THEIR FITTINGS (owner, 2026-11-11: "aviation red lights on
    /// buildings are outdated and not actually connected to buildings"). The
    /// lights above were placed from the far skyline's BOUNDING BOX (the top
    /// centre of each element's box) and the shaft's PLAN OUTLINE at its
    /// nominal height - written before the crowns, heroes, rooftop kit, masts
    /// and setbacks were drawn, so on a pyramid, a crown, a mast or a setback
    /// they hung in the air beside the top. Now (<see cref="V2"/>) each light
    /// is placed on the DRAWN mesh: the skyline's own triangles, which are the
    /// tile builder's EmitFootprint (the rooftop kit's mast and parapet are
    /// drawn in the skyline too now, so near and far tops are the same
    /// shape). The top light on the highest drawn vertex (a spire's tip, a
    /// mast's cap, a crown's apex); steady lights on the roof corners as drawn
    /// (the highest drawn vertex at each diagonal extreme of the upper
    /// tower: a parapet's corner, a pyramid's eave, a setback's corner). Each
    /// stands on a FIXTURE - a galvanized stub and a red lens - drawn in one
    /// small mesh near the eye (<see cref="FixtureDrawM"/>). The count follows
    /// the 1990s FAA practice: towers of <see cref="FlashMinM"/> and more a
    /// slow-flashing top beacon and a steady light on each of four roof
    /// corners; 90 m towers a steady top light and two opposite corners.
    /// The glow is drawn OVER the sky (premultiplied, PSX/Halo _DstBlend), so
    /// a red lamp at dusk stays red instead of clamping pink, its size is
    /// clamped in pixels, and the lights run dusk to dawn (<see cref="LitNow"/>).
    /// PSX_CITY_BEACONS2=0: the lights as they were.
    /// </summary>
    public class CityBeacons : MonoBehaviour
    {
        /// <summary>PSX_CITY_BEACONS2=0: the lights as they were (the A/B). Read
        /// when the skyline is built.</summary>
        public static bool V2 => System.Environment.GetEnvironmentVariable("PSX_CITY_BEACONS2") != "0";

        /// <summary>A tower this tall over its ground carries a beacon.</summary>
        public const float MinTowerM = 90f;
        /// <summary>V2: a tower this tall carries a FLASHING top beacon and four
        /// corner lights; a shorter one a steady top light and two corners.</summary>
        public const float FlashMinM = 150f;
        /// <summary>A roof corner's light stands on a shaft at least this
        /// share of its tower's height (a podium's corners carry none).</summary>
        const float CornerFromShare = 0.6f;
        /// <summary>Two corner lights nearer than this are one.</summary>
        const float CornerMinM = 12f;
        /// <summary>Halo half-sizes as multiples of the material's _Size
        /// (<see cref="NightGlow.HaloSize"/>): a beacon's red glow, a corner light.</summary>
        const float BeaconSize = 1.0f, CornerSize = 0.55f;
        /// <summary>V2's: a beacon's glow about a metre each way, a corner light's
        /// 0.7 m (a lamp with a little mist round it, not a disc).</summary>
        const float BeaconSize2 = 0.42f, CornerSize2 = 0.27f;
        static readonly Color BeaconRed = new Color(1.00f, 0.06f, 0.03f);
        /// <summary>V2: aviation red in LINEAR terms (sRGB about 255, 31, 22):
        /// the old (1, 0.06, 0.03) read orange-pink once encoded.</summary>
        static readonly Color AviationRed = new Color(1.00f, 0.014f, 0.008f);

        /// <summary>The fixture: a stub this tall under the lens, the lens's
        /// radius (a beacon's, a corner light's), the stub's half width.</summary>
        /// (Round 2, 2026-11-11: the real hardware's sizes - a steady L-810 is a
        /// globe of about 0.25 m on a short stub, a flashing L-864 about 0.6 m
        /// on a pedestal; dark red glass by day.)
        const float StubCornerM = 0.30f, StubBeaconM = 0.60f, LensBeaconM = 0.30f, LensCornerM = 0.13f,
                    StubHalfCornerM = 0.04f, StubHalfBeaconM = 0.11f;
        /// <summary>Fixtures are drawn for towers this near the eye: past it a
        /// 0.6 m lens is under a pixel and would only sparkle.</summary>
        public const float FixtureDrawM = 700f;
        /// <summary>Round 2, the far density (PSX/Halo; the probe mirrors it):
        /// corner lights fade over the first two real distances; the least
        /// glow size falls to FarMinShare of itself, and a beacon's extra size
        /// to none, over the other two.</summary>
        /// <summary>(Round 3) a top light's least half-size in real framebuffer
        /// pixels (2 px across), a flasher's x1.4 (the tallest read first).</summary>
        public const float TopFloorPx = 1.2f, TopFlashMul = 1.35f;
        public const float CornerFadeFromM = 1500f, CornerFadeToM = 2500f, FarShrinkFromM = 1500f, FarShrinkToM = 6000f, FarMinShare = 0.5f;
        /// <summary>The fixture set is re-picked when the eye has moved this far.</summary>
        const float FixtureMoveM = 25f;

        MeshRenderer mr;
        Mesh mesh;
        Material mat;
        public int BeaconCount { get; private set; }
        public int CornerCount { get; private set; }
        /// <summary>V2: towers that carry lights, and of those the flashing ones.</summary>
        public int TowerCount { get; private set; }
        public int FlashCount { get; private set; }

        /// <summary>Every light (world space), for the probes: where it is,
        /// the drawn point its fixture stands on (V2; the light itself before),
        /// its building (the outline's footprint index), its tower's height.</summary>
        public struct Light
        {
            public Vector3 pos, anchor;
            public int building;
            public bool top, flash;
            public float towerH;
            /// <summary>(Round 3) how far it stands moved onto its near tile now.</summary>
            public Vector3 nearOffset;
        }
        public readonly List<Light> Lights = new List<Light>();
        public bool IsV2 { get; private set; }

        // ---- the fixtures (V2) ----
        MeshRenderer fixR;
        Mesh fixMesh;
        Material fixMat;
        Texture2D fixTex;
        int[] fixAll;
        readonly List<int> fixStart = new List<int>(), fixCount = new List<int>();
        readonly List<Vector3> fixAt = new List<Vector3>();
        readonly List<int> fixIdx = new List<int>(4096);
        bool[] fixOn;
        Vector3 fixEye;
        bool fixValid;
        // ---- (round 3) THE NEAR SNAP: a tower whose near tile stands draws its
        // roof as the tile builds it; where that differs from the far skyline's
        // (a metre or two on a few towers), its lights and fixtures move onto
        // the tile's own drawn points, blended in over the hand-over ----
        FixtureBuild snapB;
        Vector3[] fixBase, fixNow;
        Vector3[] haloBase, haloNow;
        Vector3[][] towerDelta;   // per tower: per light, null = not probed / no tile
        float[] towerW;
        int snapVersion = int.MinValue;
        /// <summary>The blend: the near position whole this far inside the
        /// world's fade start, none at it.</summary>
        const float SnapBlendM = 150f;
        public int SnapTowers { get; private set; }

        public int FixtureTriangles => fixAll != null ? fixAll.Length / 3 : 0;
        public int FixturesShown { get; private set; }

        /// <summary>Build under the skyline's object from its elements (map
        /// footprint indices) and their drawn bounds; null when there is
        /// nothing to light or no PSX/Halo shader.</summary>
        public static CityBeacons Create(Transform parent, CityMap map, List<int> elems, Bounds[] bounds)
            => Create(parent, map, elems, bounds, null, null);

        /// <summary>The same, and with the skyline's drawn mesh
        /// (<paramref name="sm"/>) and a PSX/Lit material for the fixtures
        /// (<paramref name="fixtureSrc"/>, the structure atlas's): V2 places
        /// each light on the drawn tops.</summary>
        public static CityBeacons Create(Transform parent, CityMap map, List<int> elems, Bounds[] bounds,
                                         CityMeshes.SkylineMesh sm, Material fixtureSrc)
        {
            var sh = Shader.Find("PSX/Halo");
            if (sh == null || map == null || elems == null || bounds == null) return null;
            var centres = new List<Vector3>();
            var uv1 = new List<Vector2>();
            var cols = new List<Color32>();
            var lights = new List<Light>();
            bool v2 = V2 && sm != null && sm.mesh != null;
            int beacons, corners, towers = 0, flashes = 0;
            FixtureBuild fb = v2 ? new FixtureBuild() : null;
            if (v2) PlaceOnDrawn(map, elems, sm, centres, uv1, cols, lights, fb, out beacons, out corners, out towers, out flashes);
            else PlaceAsBefore(map, elems, bounds, centres, uv1, cols, lights, out beacons, out corners);
            if (centres.Count == 0) return null;

            var go = new GameObject("CityBeacons") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var cb = go.AddComponent<CityBeacons>();
            cb.IsV2 = v2;
            cb.BeaconCount = beacons;
            cb.CornerCount = corners;
            cb.TowerCount = towers;
            cb.FlashCount = flashes;
            cb.Lights.AddRange(lights);
            cb.mesh = BuildMesh(centres, uv1, cols, map.uptown);
            cb.haloBase = cb.mesh.vertices;
            cb.haloNow = cb.mesh.vertices;
            go.AddComponent<MeshFilter>().sharedMesh = cb.mesh;
            cb.mat = new Material(sh) { name = "SkylineLights", hideFlags = HideFlags.HideAndDontSave };
            cb.mat.SetColor("_Color", Color.white);
            cb.mat.SetFloat("_Size", NightGlow.HaloSize);
            // strength 1: a stronger halo clamps the red pale in the 8-bit framebuffer
            cb.mat.SetFloat("_Strength", 1.0f);
            cb.mat.SetFloat("_MinPixels", 1.6f);
            if (v2)
            {
                // OVER the sky, not added to it (PSX/Halo's tower-light blend):
                // the core replaces what is behind it, so a red lamp against a
                // pale dusk sky is red, not pink. Never larger than a few pixels
                // each way, never dimmer than its own colour.
                cb.mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                cb.mat.SetFloat("_MaxPixels", 4f);
                cb.mat.SetFloat("_MinPixels", 1.2f);
                // (round 2) the far skyline is a handful of points, not a red mass:
                // a steady corner light fades out from 1.5 to 2.5 km (the top light
                // stays) and the least glow size shrinks from 1.5 km to 6 km
                cb.mat.SetFloat("_CornerFadeFrom", CornerFadeFromM);
                cb.mat.SetFloat("_CornerFadeTo", CornerFadeToM);
                cb.mat.SetFloat("_FarShrinkFrom", FarShrinkFromM);
                cb.mat.SetFloat("_FarShrinkTo", FarShrinkToM);
                cb.mat.SetFloat("_FarMinShare", FarMinShare);
                // (round 3) a top light stays a clear point at any distance
                cb.mat.SetFloat("_TopFloorPx", TopFloorPx);
                cb.mat.SetFloat("_TopFlashMul", TopFlashMul);
                cb.mat.SetFloat("_GainFloor", 1f);
            }
            cb.mr = go.AddComponent<MeshRenderer>();
            cb.mr.sharedMaterial = cb.mat;
            cb.mr.enabled = false;
            cb.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cb.mr.receiveShadows = false;
            cb.mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            cb.mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (v2 && fixtureSrc != null && fb.v.Count > 0) cb.BuildFixtures(fb, fixtureSrc);
            Debug.Log(v2
                ? $"[CityBeacons] V2: {towers} towers of {MinTowerM:0} m+ lit on their drawn tops - {flashes} flashing top beacons ({FlashMinM:0} m+), {beacons - flashes} steady top lights, {corners} roof-corner lights; glows 1 draw, fixtures {cb.FixtureTriangles} triangles in 1 draw within {FixtureDrawM:0} m"
                : $"[CityBeacons] {beacons} aviation beacons (towers of {MinTowerM:0} m+), {corners} roof-corner lights, 1 draw");
            return cb;
        }

        /// <summary>The lights as they were (PSX_CITY_BEACONS2=0): the far
        /// element's box top centre, the shaft's plan corners at its height.</summary>
        static void PlaceAsBefore(CityMap map, List<int> elems, Bounds[] bounds, List<Vector3> centres, List<Vector2> uv1, List<Color32> cols,
                                  List<Light> lights, out int beacons, out int corners)
        {
            var fs = map.footprints;
            // each building's highest drawn point, over the ground it stands on
            var topOf = new Dictionary<int, Vector3>();
            for (int e = 0; e < elems.Count && e < bounds.Length; e++)
            {
                var b = bounds[e];
                if (b.size.sqrMagnitude < 1e-4f) continue;   // drew nothing (a part its hero redraws)
                var f = fs[elems[e]];
                int key = f.part && f.outline >= 0 ? f.outline : elems[e];
                var top = new Vector3(b.center.x, b.max.y, b.center.z);
                if (!topOf.TryGetValue(key, out var t) || top.y > t.y) topOf[key] = top;
            }
            beacons = 0;
            foreach (var kv in topOf)
            {
                float ground = GroundOf(map, fs[kv.Key]);
                if (kv.Value.y - ground < MinTowerM) continue;
                uint h = (uint)kv.Key * 2654435761u;
                float phase = (h >> 8) / 16777216f;
                var at = kv.Value + Vector3.up * 0.5f;
                Add(centres, uv1, cols, at, BeaconSize, -(1f + 0.999f * phase), BeaconRed);
                lights.Add(new Light { pos = at, anchor = at, building = kv.Key, top = true, flash = true, towerH = kv.Value.y - ground });
                beacons++;
            }

            // THE ROOF CORNERS (the 1990s city): a steady red light on each
            // corner of the tower's shaft - the tallest of its parts on the
            // ground, or the outline itself - at its roof
            corners = 0;
            foreach (var kv in topOf)
            {
                var of = fs[kv.Key];
                float ground = GroundOf(map, of);
                float towerH = kv.Value.y - ground;
                if (towerH < MinTowerM) continue;
                int si = -1;
                for (int i = 0; i < fs.Length; i++)
                    if (fs[i].part && fs[i].outline == kv.Key && fs[i].minH < 1f && (si < 0 || fs[i].h > fs[si].h)) si = i;
                var shaft = si >= 0 ? fs[si] : of;
                if (shaft.pts == null || shaft.pts.Length < 3 || shaft.h < CornerFromShare * towerH) continue;
                int c0 = centres.Count;
                corners += Corners(centres, uv1, cols, shaft, ground + shaft.h + 0.4f);
                for (int i = c0; i < centres.Count; i++)
                    lights.Add(new Light { pos = centres[i], anchor = centres[i], building = kv.Key, top = false, flash = false, towerH = towerH });
            }
        }

        /// <summary>The fixtures' mesh as it is gathered: per tower a run of
        /// triangles, and where the tower's top is (for the distance pick).</summary>
        sealed class FixtureBuild
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
            public readonly List<int> start = new List<int>(), count = new List<int>();
            public readonly List<Vector3> at = new List<Vector3>();
            /// <summary>(Round 3) per light: its fixture's first vertex and count.</summary>
            public readonly List<int> lv0 = new List<int>(), lvn = new List<int>();
            /// <summary>(Round 3) per tower: its first light, its light count, the tiles its parts stand in.</summary>
            public readonly List<int> tl0 = new List<int>(), tln = new List<int>();
            public readonly List<long[]> tiles = new List<long[]>();
        }

        /// <summary>V2: every light on the skyline's drawn triangles - see the
        /// header. One pass per building over its elements' triangles in every
        /// submesh (facade, flat roofs, crowns).</summary>
        static void PlaceOnDrawn(CityMap map, List<int> elems, CityMeshes.SkylineMesh sm, List<Vector3> centres, List<Vector2> uv1, List<Color32> cols,
                                 List<Light> lights, FixtureBuild fb, out int tops, out int corners, out int towers, out int flashes)
        {
            tops = corners = towers = flashes = 0;
            var fs = map.footprints;
            var verts = sm.mesh.vertices;
            // each building's elements (an outline, or the parts that draw it)
            var byKey = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int e = 0; e < elems.Count; e++)
            {
                var f = fs[elems[e]];
                int key = f.part && f.outline >= 0 ? f.outline : elems[e];
                if (!byKey.TryGetValue(key, out var list)) { list = new List<int>(); byKey[key] = list; order.Add(key); }
                list.Add(e);
            }
            var vs = new List<Vector3>(4096);
            var placed = new List<Vector2>(8);
            foreach (int key in order)
            {
                vs.Clear();
                var list = byKey[key];
                for (int s = 0; s < sm.indices.Length; s++)
                {
                    var idx = sm.indices[s]; var rg = sm.ranges[s];
                    foreach (int e in list)
                    {
                        int a = rg[e * 2], c = rg[e * 2 + 1];
                        // the corners of real triangles only (a degenerate one,
                        // a member's end cap of no area, draws nothing there)
                        for (int k = 0; k + 2 < c; k += 3)
                        {
                            Vector3 p0 = verts[idx[a + k]], p1 = verts[idx[a + k + 1]], p2 = verts[idx[a + k + 2]];
                            if (Vector3.Cross(p1 - p0, p2 - p0).sqrMagnitude < 1e-6f) continue;
                            vs.Add(p0); vs.Add(p1); vs.Add(p2);
                        }
                    }
                }
                if (vs.Count < 3) continue;
                float top = float.MinValue;
                foreach (var p in vs) top = Mathf.Max(top, p.y);
                var of = fs[key];
                float ground = GroundOf(map, of);
                float towerH = top - ground;
                if (towerH < MinTowerM) continue;
                bool flash = towerH >= FlashMinM;
                towers++;
                int fixT0 = fb.t.Count;

                // THE TOP LIGHT: on the highest drawn point - always a drawn
                // vertex: a spire's tip, a mast cap's corner, or on a flat top
                // (a plant box, a slab) the highest vertex nearest its middle
                Vector3 mid = Vector3.zero; int nTop = 0;
                foreach (var p in vs) if (p.y > top - 0.05f) { mid += p; nTop++; }
                mid /= Mathf.Max(1, nTop);
                Vector3 apex = mid;
                {
                    float best = float.MaxValue;
                    foreach (var p in vs)
                        if (p.y > top - 0.05f && ClearAbove(vs, p, LensBeaconM)) { float d = Flat(p - mid).sqrMagnitude; if (d < best) { best = d; apex = p; } }
                    if (best == float.MaxValue)
                        foreach (var p in vs)
                            if (p.y > top - 0.05f) { float d = Flat(p - mid).sqrMagnitude; if (d < best) { best = d; apex = p; } }
                }
                uint h = (uint)key * 2654435761u;
                float phase = (h >> 8) / 16777216f;
                float lensT = LensBeaconM;
                var lightAt = apex + Vector3.up * (StubBeaconM + lensT);
                Add(centres, uv1, cols, lightAt, BeaconSize2, flash ? -(1f + 0.999f * phase) : -0.6f, AviationRed);
                lights.Add(new Light { pos = lightAt, anchor = apex, building = key, top = true, flash = flash, towerH = towerH });
                int towerL0 = lights.Count - 1;
                fb.lv0.Add(fb.v.Count);
                Fixture(fb, apex, lensT, StubBeaconM, StubHalfBeaconM);
                fb.lvn.Add(fb.v.Count - fb.lv0[fb.lv0.Count - 1]);
                tops++;
                if (flash) flashes++;

                // THE ROOF CORNERS, as drawn: along the shaft's own axes, the
                // upper tower's extreme toward each diagonal, and there its
                // highest drawn vertex - a parapet's corner, a pyramid's eave,
                // a setback's corner. Four on a flashing tower, two opposite
                // on a shorter one.
                int si = -1;
                for (int i = 0; i < fs.Length; i++)
                    if (fs[i].part && fs[i].outline == key && fs[i].minH < 1f && (si < 0 || fs[i].h > fs[si].h)) si = i;
                var shaft = si >= 0 ? fs[si] : of;
                var u2 = shaft.u.sqrMagnitude > 0.5f ? shaft.u.normalized : Vector2.right;
                var v2d = new Vector2(-u2.y, u2.x);
                var c0 = shaft.centre;
                float yMin = ground + CornerFromShare * towerH;
                placed.Clear();
                placed.Add(new Vector2(apex.x, apex.z));
                for (int q = 0; q < 4; q++)
                {
                    if (!flash && q != 0 && q != 3) continue;   // two opposite corners
                    var d = (((q & 1) == 0 ? u2 : -u2) + ((q & 2) == 0 ? v2d : -v2d)).normalized;
                    float most = float.MinValue;
                    foreach (var p in vs)
                        if (p.y >= yMin) most = Mathf.Max(most, Vector2.Dot(new Vector2(p.x, p.z) - c0, d));
                    if (most == float.MinValue) continue;
                    // (round 2: ON TOP - the highest such vertex with nothing of
                    // its own building drawn over it, never one at the foot of
                    // a fin or a wall that rises from it; else within 3 m of the extreme)
                    Vector3 at = Vector3.zero; float atY = float.MinValue;
                    foreach (var p in vs)
                        if (p.y >= yMin && p.y > atY && Vector2.Dot(new Vector2(p.x, p.z) - c0, d) > most - 0.75f && ClearAbove(vs, p, LensCornerM)) { atY = p.y; at = p; }
                    if (atY == float.MinValue)
                        foreach (var p in vs)
                            if (p.y >= yMin && p.y > atY && Vector2.Dot(new Vector2(p.x, p.z) - c0, d) > most - 3f && ClearAbove(vs, p, LensCornerM)) { atY = p.y; at = p; }
                    if (atY == float.MinValue) continue;
                    var at2 = new Vector2(at.x, at.z);
                    bool dup = false;
                    foreach (var o in placed) if ((o - at2).sqrMagnitude < CornerMinM * CornerMinM) { dup = true; break; }
                    if (dup) continue;
                    placed.Add(at2);
                    var cAt = at + Vector3.up * (StubCornerM + LensCornerM);
                    Add(centres, uv1, cols, cAt, CornerSize2, -0.5f, AviationRed);
                    lights.Add(new Light { pos = cAt, anchor = at, building = key, top = false, flash = false, towerH = towerH });
                    fb.lv0.Add(fb.v.Count);
                    Fixture(fb, at, LensCornerM, StubCornerM, StubHalfCornerM);
                    fb.lvn.Add(fb.v.Count - fb.lv0[fb.lv0.Count - 1]);
                    corners++;
                }
                fb.start.Add(fixT0); fb.count.Add(fb.t.Count - fixT0);
                fb.at.Add(new Vector3(apex.x, top, apex.z));
                fb.tl0.Add(towerL0); fb.tln.Add(lights.Count - towerL0);
                var tks = new HashSet<long>();
                foreach (int e in list) tks.Add(CityWorld.TileKeyAt(fs[elems[e]].centre));
                var tka = new long[tks.Count]; tks.CopyTo(tka);
                fb.tiles.Add(tka);
            }
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        /// <summary>One fixture: a galvanized square stub from just inside the
        /// drawn point up to the lens, and the lens - an octahedron of red
        /// glass - with the light at its centre. Flat-shaded, its own vertices
        /// per face.</summary>
        /// <summary>True when no triangle of the building (three vertices each)
        /// is drawn over the point within r of it in plan: an upright stub can
        /// stand there. Walls (no plan area) never count.</summary>
        static bool ClearAbove(List<Vector3> tris, Vector3 p, float r)
        {
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector3 a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (a.y < p.y + 0.03f && b.y < p.y + 0.03f && c.y < p.y + 0.03f) continue;
                float minx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - r, maxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + r;
                if (p.x < minx || p.x > maxx) continue;
                float minz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - r, maxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + r;
                if (p.z < minz || p.z > maxz) continue;
                float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
                if (Mathf.Abs(area) < 1e-3f) continue;
                for (int k = 0; k < 5; k++)
                {
                    float x = p.x + (k == 1 ? r : k == 2 ? -r : 0f), z = p.z + (k == 3 ? r : k == 4 ? -r : 0f);
                    float w1 = ((b.x - x) * (c.z - z) - (c.x - x) * (b.z - z)) / area;
                    float w2 = ((c.x - x) * (a.z - z) - (a.x - x) * (c.z - z)) / area;
                    float w3 = 1f - w1 - w2;
                    if (w1 < -1e-4f || w2 < -1e-4f || w3 < -1e-4f) continue;
                    if (w1 * a.y + w2 * b.y + w3 * c.y > p.y + 0.05f) return false;
                }
            }
            return true;
        }

        static void Fixture(FixtureBuild fb, Vector3 foot, float lensR, float stubM, float stubHalf)
        {
            var galv = new Vector2(0.25f, 0.5f);
            var red = new Vector2(0.75f, 0.5f);
            float y0 = foot.y - 0.08f, y1 = foot.y + stubM + lensR * 0.4f;
            float w = stubHalf;
            var c = new[]
            {
                new Vector3(foot.x + w, 0f, foot.z + w), new Vector3(foot.x - w, 0f, foot.z + w),
                new Vector3(foot.x - w, 0f, foot.z - w), new Vector3(foot.x + w, 0f, foot.z - w),
            };
            for (int k = 0; k < 4; k++)
            {
                var a = c[k]; var b = c[(k + 1) % 4];
                Quad(fb, new Vector3(a.x, y0, a.z), new Vector3(b.x, y0, b.z), new Vector3(b.x, y1, b.z), new Vector3(a.x, y1, a.z), galv);
            }
            var L = foot + Vector3.up * (stubM + lensR);
            var up = L + Vector3.up * lensR; var dn = L + Vector3.down * lensR;
            var ring = new[] { L + Vector3.right * lensR, L + Vector3.forward * lensR, L + Vector3.left * lensR, L + Vector3.back * lensR };
            for (int k = 0; k < 4; k++)
            {
                var a = ring[k]; var b = ring[(k + 1) % 4];
                Tri(fb, a, up, b, red);
                Tri(fb, a, b, dn, red);
            }
        }

        static void Quad(FixtureBuild fb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv)
        {
            Tri(fb, a, c, b, uv);
            Tri(fb, a, d, c, uv);
        }

        /// <summary>A triangle facing out (away from its own centre's fixture axis is
        /// not known here: the winding is made to face the side its normal points to).</summary>
        static void Tri(FixtureBuild fb, Vector3 a, Vector3 b, Vector3 c, Vector2 uv)
        {
            var nrm = Vector3.Cross(b - a, c - a);
            if (nrm.sqrMagnitude < 1e-10f) return;
            nrm.Normalize();
            int i = fb.v.Count;
            fb.v.Add(a); fb.v.Add(b); fb.v.Add(c);
            fb.n.Add(nrm); fb.n.Add(nrm); fb.n.Add(nrm);
            fb.uv.Add(uv); fb.uv.Add(uv); fb.uv.Add(uv);
            fb.t.Add(i); fb.t.Add(i + 1); fb.t.Add(i + 2);
        }

        void BuildFixtures(FixtureBuild fb, Material src)
        {
            // which way each face points: out from its fixture (stub sides out
            // of the stub's axis, lens faces out of the lens's centre) - made
            // so by flipping any triangle whose normal faces the nearest light
            for (int t = 0; t < fb.t.Count; t += 3)
            {
                int i0 = fb.t[t], i1 = fb.t[t + 1], i2 = fb.t[t + 2];
                var cen = (fb.v[i0] + fb.v[i1] + fb.v[i2]) / 3f;
                // the fixture's axis point at this height: the nearest light's x/z
                Vector3 axis = cen; float best = float.MaxValue;
                foreach (var l in Lights)
                {
                    float d = Flat(l.pos - cen).sqrMagnitude;
                    if (d < best) { best = d; axis = new Vector3(l.pos.x, Mathf.Abs(cen.y - l.pos.y) < 0.4f ? l.pos.y : cen.y, l.pos.z); }
                }
                var outw = cen - axis;
                if (outw.sqrMagnitude < 1e-8f) continue;
                if (Vector3.Dot(fb.n[i0], outw) < 0f)
                {
                    fb.t[t + 1] = i2; fb.t[t + 2] = i1;
                    fb.n[i0] = fb.n[i1] = fb.n[i2] = -fb.n[i0];
                }
            }
            fixMesh = new Mesh { name = "CityBeaconFixtures" };
            if (fb.v.Count > 65000) fixMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            fixMesh.SetVertices(fb.v);
            fixMesh.SetNormals(fb.n);
            fixMesh.SetUVs(0, fb.uv);
            // no vertex colours: the structure mesh has none either
            fixAll = fb.t.ToArray();
            fixMesh.SetTriangles(new int[0], 0, false);
            fixMesh.RecalculateBounds();
            fixMesh.MarkDynamic();
            fixStart.AddRange(fb.start); fixCount.AddRange(fb.count); fixAt.AddRange(fb.at);
            snapB = fb;
            fixBase = fb.v.ToArray();
            fixNow = fb.v.ToArray();
            towerDelta = new Vector3[fb.tl0.Count][];
            towerW = new float[fb.tl0.Count];
            fixOn = new bool[fixStart.Count];

            // the fixture's two materials in one 4x4 texture: galvanized steel
            // (left half) and the lens's red glass (right half)
            fixTex = new Texture2D(4, 4, TextureFormat.RGBA32, false, false)
            { name = "BeaconFixture", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[16];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    px[y * 4 + x] = x < 2 ? new Color32(150, 152, 156, 255) : new Color32(92, 9, 7, 255);
            fixTex.SetPixels32(px);
            fixTex.Apply(false, true);
            fixMat = new Material(src) { name = "BeaconFixtures", hideFlags = HideFlags.HideAndDontSave };
            fixMat.mainTexture = fixTex;
            if (fixMat.HasProperty("_Color")) fixMat.SetColor("_Color", Color.white);
            fixMat.SetFloat(CityTileFade.ModeId, 0f);

            var fgo = new GameObject("CityBeaconFixtures") { hideFlags = HideFlags.DontSave };
            fgo.transform.SetParent(transform, false);
            fgo.AddComponent<MeshFilter>().sharedMesh = fixMesh;
            fixR = fgo.AddComponent<MeshRenderer>();
            fixR.sharedMaterial = fixMat;
            fixR.enabled = false;
            fixR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fixR.receiveShadows = false;
            fixR.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            fixR.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        public void Show(bool on)
        {
            if (mr != null && mr.enabled != on) mr.enabled = on;
        }

        /// <summary>Whether the obstruction lights run (CitySkyline asks every
        /// frame). V2: a real one's photocell runs it dusk to dawn - on at
        /// DAWN, DUSK and NIGHT (TimeOfDay.NightFor half and more), off in
        /// daylight and at SUNSET while the sun is up. Before: with the street lamps.</summary>
        public bool LitNow() => IsV2 ? TimeOfDay.NightFor(TimeOfDay.Current) >= 0.5f : NightGlow.On;

        /// <summary>V2: the fixtures of the towers near <paramref name="cam"/>
        /// (re-picked as the eye moves), drawn by day and night alike.</summary>
        public void RefreshFixtures(Camera cam)
        {
            if (fixR == null) return;
            if (cam == null) { fixR.enabled = false; return; }
            var eye = cam.transform.position;
            if (fixValid && (eye - fixEye).sqrMagnitude < FixtureMoveM * FixtureMoveM) return;
            fixValid = true; fixEye = eye;
            bool dirty = false; int shown = 0;
            float r2 = FixtureDrawM * FixtureDrawM;
            for (int i = 0; i < fixOn.Length; i++)
            {
                bool on = (fixAt[i] - eye).sqrMagnitude < r2;
                if (on != fixOn[i]) { fixOn[i] = on; dirty = true; }
                if (on) shown++;
            }
            if (dirty)
            {
                fixIdx.Clear();
                for (int i = 0; i < fixOn.Length; i++)
                    if (fixOn[i]) for (int k = 0; k < fixCount[i]; k++) fixIdx.Add(fixAll[fixStart[i] + k]);
                fixMesh.SetTriangles(fixIdx, 0, false);
            }
            FixturesShown = shown;
            fixR.enabled = shown > 0;
        }

        /// <summary>(Round 3) the light of tower t, light k's near-tile offset:
        /// the drawn vertex of the tile's buildings mesh nearest the far anchor
        /// (within 4 m) with nothing of the tile drawn over it - the same rule as
        /// the far pick, on the tile's own geometry. Zero where the tile agrees.</summary>
        Vector3[] ProbeTower(int t, Transform worldT)
        {
            var b = snapB;
            int l0 = b.tl0[t], ln = b.tln[t];
            var res = new Vector3[ln];
            var near = new List<Vector3>(4096);
            bool any = false;
            foreach (long key in b.tiles[t])
            {
                int tx = (int)(key >> 24), tz = (int)((key << 40) >> 40);
                var root = worldT != null ? worldT.Find($"Tile_{tx}_{tz}") : null;
                if (root == null) return null;   // a tile not stood up yet
                var bt = root.Find("Buildings");
                if (bt == null) continue;
                foreach (var mf in bt.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (m == null || !m.isReadable) continue;
                    var vv = m.vertices; var tr = m.triangles; var l2w = mf.transform.localToWorldMatrix;
                    for (int k = 0; k + 2 < tr.Length; k += 3)
                    {
                        Vector3 a = l2w.MultiplyPoint3x4(vv[tr[k]]), c1 = l2w.MultiplyPoint3x4(vv[tr[k + 1]]), c2 = l2w.MultiplyPoint3x4(vv[tr[k + 2]]);
                        bool keep = false;
                        for (int i = 0; i < ln && !keep; i++)
                        {
                            var an = Lights[l0 + i].anchor;
                            if (Mathf.Abs(a.x - an.x) < 8f && Mathf.Abs(a.z - an.z) < 8f) keep = true;
                        }
                        if (!keep) continue;
                        if (Vector3.Cross(c1 - a, c2 - a).sqrMagnitude < 1e-6f) continue;
                        near.Add(a); near.Add(c1); near.Add(c2); any = true;
                    }
                }
            }
            if (!any) return res;
            for (int i = 0; i < ln; i++)
            {
                var an = Lights[l0 + i].anchor;
                float r = Lights[l0 + i].top ? LensBeaconM : LensCornerM;
                // a drawn vertex of the tile with clear sky over it, within 4 m...
                float best = float.MaxValue; Vector3 at = an;
                foreach (var p in near)
                {
                    float d = (p - an).sqrMagnitude;
                    if (d >= best || d > 16f) continue;
                    if (!ClearAbove(near, p, r)) continue;
                    best = d; at = p;
                }
                // ...or, nearer, the tile's top surface straight under (or just
                // over) the far point - a roof the far copy draws a lip or a
                // step above
                float dropY = float.MinValue;
                for (int k = 0; k + 2 < near.Count; k += 3)
                {
                    Vector3 ta = near[k], tb = near[k + 1], tc = near[k + 2];
                    float area = (tb.x - ta.x) * (tc.z - ta.z) - (tc.x - ta.x) * (tb.z - ta.z);
                    if (Mathf.Abs(area) < 1e-3f) continue;
                    float w1 = ((tb.x - an.x) * (tc.z - an.z) - (tc.x - an.x) * (tb.z - an.z)) / area;
                    float w2 = ((tc.x - an.x) * (ta.z - an.z) - (ta.x - an.x) * (tc.z - an.z)) / area;
                    float w3 = 1f - w1 - w2;
                    if (w1 < -1e-4f || w2 < -1e-4f || w3 < -1e-4f) continue;
                    float y = w1 * ta.y + w2 * tb.y + w3 * tc.y;
                    if (y > an.y + 0.5f || y < an.y - 4f) continue;
                    dropY = Mathf.Max(dropY, y);
                }
                if (dropY > float.MinValue && (an.y - dropY) * (an.y - dropY) < best) { best = (an.y - dropY) * (an.y - dropY); at = new Vector3(an.x, dropY, an.z); }
                res[i] = best < 0.0025f || best == float.MaxValue ? Vector3.zero : at - an;
            }
            return res;
        }

        /// <summary>(Round 3) every frame from CitySkyline: probe a tower's near
        /// tile once it stands up (re-read when the live set changes), and move
        /// its lights and fixtures by that offset times the blend - whole once
        /// the tower is <see cref="SnapBlendM"/> inside the world's fade start
        /// (where the tile draws it), none at the fade start (the far skyline).
        /// PSX_CITY_BEACONS2=0 has no snap.</summary>
        public void SnapNear(Camera cam, float fadeStart, int liveVersion)
        {
            if (snapB == null || cam == null || towerDelta == null) return;
            var worldT = transform.parent != null ? transform.parent.parent : null;
            if (liveVersion != snapVersion)
            {
                snapVersion = liveVersion;
                int n = 0;
                for (int t = 0; t < towerDelta.Length; t++)
                {
                    // only towers near enough for the ring to have stood their tiles
                    if ((fixAt[t] - cam.transform.position).sqrMagnitude > 4000f * 4000f) { towerDelta[t] = null; continue; }
                    towerDelta[t] = ProbeTower(t, worldT);
                    if (towerDelta[t] != null) foreach (var d in towerDelta[t]) if (d != Vector3.zero) { n++; break; }
                }
                SnapTowers = n;
            }
            bool haloDirty = false, fixDirty = false;
            var eye = cam.transform.position;
            for (int t = 0; t < towerDelta.Length; t++)
            {
                var dl = towerDelta[t];
                float w = 0f;
                if (dl != null)
                {
                    float d = Vector2.Distance(new Vector2(eye.x, eye.z), new Vector2(fixAt[t].x, fixAt[t].z));
                    w = Mathf.Clamp01((fadeStart - d) / SnapBlendM);
                }
                if (Mathf.Abs(w - towerW[t]) < 0.01f && !(w == 0f && towerW[t] != 0f) && !(w == 1f && towerW[t] != 1f)) continue;
                towerW[t] = w;
                int l0 = snapB.tl0[t], ln = snapB.tln[t];
                for (int i = 0; i < ln; i++)
                {
                    var off = dl != null ? dl[i] * w : Vector3.zero;
                    int li = l0 + i;
                    for (int k = 0; k < 4; k++) haloNow[li * 4 + k] = haloBase[li * 4 + k] + off;
                    for (int k = 0; k < snapB.lvn[li]; k++) fixNow[snapB.lv0[li] + k] = fixBase[snapB.lv0[li] + k] + off;
                    var L = Lights[li]; L.nearOffset = off; Lights[li] = L;
                    haloDirty = true; fixDirty = true;
                }
            }
            // (the halo mesh's bounds are the whole city's, as built: kept)
            if (haloDirty) { var hb = mesh.bounds; mesh.vertices = haloNow; mesh.bounds = hb; }
            if (fixDirty && fixMesh != null) { fixMesh.vertices = fixNow; fixMesh.RecalculateBounds(); }
        }

        /// <summary>The ground a building stands on, as the tiles find it
        /// (CityMeshes.EmitFootprint: the lowest under its outline).</summary>
        static float GroundOf(CityMap map, CityMap.Footprint f)
        {
            float g = float.MaxValue;
            foreach (var p in f.pts) g = Mathf.Min(g, CityElevation.GroundY(map, p.x, p.y));
            return g == float.MaxValue ? 0f : g;
        }

        /// <summary>A roof's corners: its outline's extreme point toward each
        /// of the four diagonals of its own axes (a chamfered corner's
        /// middle), one steady red light on each, none twice.</summary>
        static int Corners(List<Vector3> c, List<Vector2> u, List<Color32> k, CityMap.Footprint s, float y)
        {
            var v = new Vector2(-s.u.y, s.u.x);
            var placed = new List<Vector2>(4);
            for (int q = 0; q < 4; q++)
            {
                var d = ((q & 1) == 0 ? s.u : -s.u) + ((q & 2) == 0 ? v : -v);
                float most = float.MinValue;
                foreach (var p in s.pts) most = Mathf.Max(most, Vector2.Dot(p - s.centre, d));
                Vector2 at = Vector2.zero;
                int n = 0;
                foreach (var p in s.pts) if (Vector2.Dot(p - s.centre, d) > most - 2f) { at += p; n++; }
                at /= n;
                bool dup = false;
                foreach (var o in placed) if ((o - at).sqrMagnitude < CornerMinM * CornerMinM) { dup = true; break; }
                if (dup) continue;
                placed.Add(at);
                Add(c, u, k, new Vector3(at.x, y, at.y), CornerSize, -0.5f, BeaconRed);
            }
            return placed.Count;
        }

        static void Add(List<Vector3> c, List<Vector2> u, List<Color32> k, Vector3 at, float size, float mode, Color col)
        {
            c.Add(at); u.Add(new Vector2(size, mode));
            k.Add(new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(col.r) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(col.g) * 255f),
                              (byte)Mathf.RoundToInt(Mathf.Clamp01(col.b) * 255f), 255));
        }

        /// <summary>The PSX/Halo mesh (NightGlow.BuildHaloMesh's layout: four
        /// vertices per halo at its centre, uv0 the corner, uv1 = size and
        /// mode, the colour per halo). Its bounds cover the whole city: the
        /// lights are squeezed in the shader, so the real positions say
        /// nothing about what is on screen, and one draw is cheap.</summary>
        static Mesh BuildMesh(List<Vector3> centres, List<Vector2> uv1s, List<Color32> cols, Vector2 uptown)
        {
            int n = centres.Count, nv = n * 4;
            var mesh = new Mesh { name = "CityBeacons" };
            if (nv > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new Vector3[nv]; var uv0 = new Vector2[nv]; var uv1 = new Vector2[nv]; var col = new Color32[nv];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int v = i * 4;
                verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = centres[i];
                uv0[v] = new Vector2(0f, 0f); uv0[v + 1] = new Vector2(1f, 0f); uv0[v + 2] = new Vector2(1f, 1f); uv0[v + 3] = new Vector2(0f, 1f);
                uv1[v] = uv1[v + 1] = uv1[v + 2] = uv1[v + 3] = uv1s[i];
                col[v] = col[v + 1] = col[v + 2] = col[v + 3] = cols[i];
                int t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            mesh.vertices = verts; mesh.uv = uv0; mesh.uv2 = uv1; mesh.colors32 = col; mesh.triangles = tris;
            mesh.bounds = new Bounds(new Vector3(uptown.x, 0f, uptown.y), new Vector3(60000f, 4000f, 60000f));
            return mesh;
        }

        void OnDestroy()
        {
            if (mesh != null) Kill(mesh);
            if (mat != null) Kill(mat);
            if (fixMesh != null) Kill(fixMesh);
            if (fixMat != null) Kill(fixMat);
            if (fixTex != null) Kill(fixTex);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
