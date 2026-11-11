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
        const float StubM = 0.55f, LensBeaconM = 0.38f, LensCornerM = 0.27f, StubHalfM = 0.08f;
        /// <summary>Fixtures are drawn for towers this near the eye: past it a
        /// 0.6 m lens is under a pixel and would only sparkle.</summary>
        public const float FixtureDrawM = 700f;
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
                        if (p.y > top - 0.05f) { float d = Flat(p - mid).sqrMagnitude; if (d < best) { best = d; apex = p; } }
                }
                uint h = (uint)key * 2654435761u;
                float phase = (h >> 8) / 16777216f;
                float lensT = LensBeaconM;
                var lightAt = apex + Vector3.up * (StubM + lensT);
                Add(centres, uv1, cols, lightAt, BeaconSize2, flash ? -(1f + 0.999f * phase) : -0.5f, AviationRed);
                lights.Add(new Light { pos = lightAt, anchor = apex, building = key, top = true, flash = flash, towerH = towerH });
                Fixture(fb, apex, lensT);
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
                    Vector3 at = Vector3.zero; float atY = float.MinValue;
                    foreach (var p in vs)
                        if (p.y >= yMin && p.y > atY && Vector2.Dot(new Vector2(p.x, p.z) - c0, d) > most - 0.75f) { atY = p.y; at = p; }
                    if (atY == float.MinValue) continue;
                    var at2 = new Vector2(at.x, at.z);
                    bool dup = false;
                    foreach (var o in placed) if ((o - at2).sqrMagnitude < CornerMinM * CornerMinM) { dup = true; break; }
                    if (dup) continue;
                    placed.Add(at2);
                    var cAt = at + Vector3.up * (StubM + LensCornerM);
                    Add(centres, uv1, cols, cAt, CornerSize2, -0.5f, AviationRed);
                    lights.Add(new Light { pos = cAt, anchor = at, building = key, top = false, flash = false, towerH = towerH });
                    Fixture(fb, at, LensCornerM);
                    corners++;
                }
                fb.start.Add(fixT0); fb.count.Add(fb.t.Count - fixT0);
                fb.at.Add(new Vector3(apex.x, top, apex.z));
            }
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        /// <summary>One fixture: a galvanized square stub from just inside the
        /// drawn point up to the lens, and the lens - an octahedron of red
        /// glass - with the light at its centre. Flat-shaded, its own vertices
        /// per face.</summary>
        static void Fixture(FixtureBuild fb, Vector3 foot, float lensR)
        {
            var galv = new Vector2(0.25f, 0.5f);
            var red = new Vector2(0.75f, 0.5f);
            float y0 = foot.y - 0.08f, y1 = foot.y + StubM + lensR * 0.4f;
            float w = StubHalfM;
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
            var L = foot + Vector3.up * (StubM + lensR);
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
            fixOn = new bool[fixStart.Count];

            // the fixture's two materials in one 4x4 texture: galvanized steel
            // (left half) and the lens's red glass (right half)
            fixTex = new Texture2D(4, 4, TextureFormat.RGBA32, false, false)
            { name = "BeaconFixture", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[16];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    px[y * 4 + x] = x < 2 ? new Color32(150, 152, 156, 255) : new Color32(150, 18, 14, 255);
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
