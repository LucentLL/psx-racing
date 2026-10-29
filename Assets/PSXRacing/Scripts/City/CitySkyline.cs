using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE FAR SKYLINE (Uptown B4, 2026-10-04). The city streams two tiles
    /// round the player and the camera's far plane stops at 500 m, so from
    /// I-77 or the stadium uptown's towers did not exist until you were
    /// nearly under them. Now the tallest <see cref="Towers"/> towers of the
    /// core - parts, roof shapes and B3 massing exactly as the tiles build
    /// them (CityMeshes.BuildSkyline) - stand as ONE merged mesh with two
    /// submeshes (the facade atlas, the flat roofs): two draw calls, no
    /// collider, no shadow.
    ///
    /// It is drawn beyond the far plane by PSX/Lit's skyline switch
    /// (<c>_Skyline</c>, the haze distance in metres): past the world's fade
    /// start each vertex is pulled in along its own line of sight into the
    /// last metres before the far plane - the same pixels, depth squeezed,
    /// order kept. Its haze toward the horizon colour is off since
    /// 2026-10-17 (<see cref="HazeClearM"/>; the drawn world still ends in
    /// its own short edge fade). At night its lit windows are the facade's own.
    ///
    /// A building is left out while its tile is live AND it is nearer than
    /// the world's fade start: there the tile draws it, clear; further out
    /// the tile's copy is fading into the sky and the skyline's (squeezed in
    /// front of it) carries on. In fog weather the skyline is off.
    ///
    /// THE OWN DEPTH (2026-10-17): the squeeze keeps towers in order but
    /// not the faces of ONE tower (a crown on its roof, a fin on its band),
    /// which tied in the band's few depth steps and flickered. So the mesh
    /// is drawn three times, before the world (<see cref="Draws"/>; PSX/Lit's
    /// _SkylinePass): in colour with depth by the real distance on its own
    /// range, then the depth set to the far plane and the squeezed depth
    /// written over it, for the world and the beacons to meet.
    /// </summary>
    public class CitySkyline : MonoBehaviour
    {
        /// <summary>How many towers: the tallest of the core.</summary>
        public const int Towers = 60;
        /// <summary>The core the towers are taken from, metres round uptown.</summary>
        public const float CoreRadiusM = 2500f;
        /// <summary>Nothing shorter is a skyline tower: towers only - a low,
        /// wide block kilometres out barely clears the treeline, and was
        /// the "floating" slab over the horizon the owner saw.</summary>
        const float MinHeightM = 60f;
        /// <summary>A part lower than this is left to its tile: a podium or
        /// a stadium ring kilometres out is under the world's own edge.</summary>
        const float MinPartM = 45f;
        /// <summary>The haze's e-folding distance by clear weather, and in
        /// rain or snow (metres): 1 - exp(-d / this) of the horizon colour -
        /// and the shader's skyline switch, so never 0. OFF in every weather
        /// since 2026-10-17 (owner: the far towers still "faded/hazy"; his
        /// rule of 2026-10-03 is no distance fog unless the weather is Fog,
        /// and in fog the skyline is not drawn at all). At dusk it lifted the
        /// finned crown's darkest tenth from 48 to 60 of 255 at 1.3 km. It was
        /// 24 km clear, 9 km wet.</summary>
        public const float HazeClearM = 1e9f, HazeWetM = 1e9f;
        /// <summary>The world's edge fade runs to this share of the far plane
        /// (PSXLit.shader's SKYLINE_FAR, the same number)...</summary>
        const float FarShare = 0.997f;
        /// <summary>...and the squeezed skyline ends this far into it, where
        /// the fade is about half (SKYLINE_BAND): the world's last, mostly
        /// sky-coloured metres are drawn behind the towers, not over them.</summary>
        const float BandShare = 0.70f;

        /// <summary>The three draws (PSX/Lit _SkylinePass 1..3), each a
        /// renderer on the one mesh: its queue, ZWrite, ZTest, ColorMask.
        /// Before the world's Geometry (2000), so the world covers them. The
        /// far land (CityFar) takes the same three; the far lights draw
        /// between the first and the second (<see cref="FarLightsQueue"/>),
        /// in the depth the first wrote.</summary>
        static readonly (string name, int queue, float zwrite, float ztest, float mask)[] Draws =
        {
            ("CitySkyline", 1985, 1f, (float)UnityEngine.Rendering.CompareFunction.LessEqual, 15f),
            ("CitySkyline depth reset", 1987, 1f, (float)UnityEngine.Rendering.CompareFunction.Always, 0f),
            ("CitySkyline band depth", 1988, 1f, (float)UnityEngine.Rendering.CompareFunction.LessEqual, 0f),
        };
        public const int DrawCount = 3;
        /// <summary>The far city's lights (CityFar): after every colour draw
        /// of the skyline and the far land, before their depth reset.</summary>
        public const int FarLightsQueue = 1986;

        /// <summary>Make <paramref name="m"/> (a PSX/Lit copy) draw
        /// <paramref name="draw"/> (0..2) of the skyline's three, squeezed,
        /// with no haze.</summary>
        public static void SetDraw(Material m, int draw)
        {
            m.SetFloat("_Skyline", HazeClearM);
            m.SetFloat("_SkylinePass", draw + 1);
            m.SetFloat("_SkyZWrite", Draws[draw].zwrite);
            m.SetFloat("_SkyZTest", Draws[draw].ztest);
            m.SetFloat("_SkyColorMask", Draws[draw].mask);
            m.renderQueue = Draws[draw].queue;
        }

        /// <summary>THE MID-RISE TIER (the far city, 2026-10-24): every
        /// building this tall or more within <see cref="MidCoreM"/> of uptown
        /// stands on the far land too - uptown's edges, midtown, South End,
        /// the hospitals - drawn while it is within <see cref="MidDrawM"/> of
        /// the eye. Their walls and lit floors are the tiles' own.</summary>
        public const float MidMinM = 20f, MidCoreM = 7000f;
        public static float MidDrawM => Application.isMobilePlatform ? 4500f : 7000f;
        /// <summary>The outlines the skyline draws (its towers and its mid-rise
        /// tier): the far lights leave their windows to it.</summary>
        public readonly HashSet<int> Outlines = new HashSet<int>();
        bool[] mid;

        CityMeshes.SkylineMesh sm;
        long[] tile;
        Vector2[] centre;
        float[] reach;
        bool[] hidden;
        int shown;
        readonly List<int> idx = new List<int>(16384);
        MeshRenderer mr;
        /// <summary>The three draws' renderers (mr first).</summary>
        MeshRenderer[] mrs;
        Material[] mats;
        float hazeSet = -1f;
        CityBeacons beacons;

        // ---- the per-frame refresh, cached (the perf probe, 2026-10-25:
        // 2.3-2.8 ms of every editor frame in a Charlotte race, the bounds'
        // eight squeezed corners of every tower shown, and a tile lookup per
        // tower) ----
        /// <summary>The perf probe's A/B: false runs every pass every frame,
        /// as before.</summary>
        public static bool CacheRefresh = true;
        /// <summary>Bumped by CityWorld whenever a tile stands up or drops:
        /// the towers' "its tile is live" flags are re-read only then.</summary>
        public int LiveVersion;
        /// <summary>The bounds are re-measured when the eye has moved this far
        /// (and whenever what is shown changed); between, the last ones are
        /// grown by <see cref="BoundsDrift"/> x the distance moved, which a
        /// squeezed corner can never outrun (one in the band moves at most
        /// about twice the eye's own move, an unsqueezed one not at all).</summary>
        const float BoundsMoveM = 12f, BoundsDrift = 3.5f;
        bool[] liveOf;
        int liveOfVersion = int.MinValue;
        bool boundsValid, boundsAny;
        Bounds boundsAt;
        Vector3 boundsEye;
        float boundsFade, boundsBand;
        static readonly int FogNearId = Shader.PropertyToID("_PSXFogNear");
        /// <summary>The towers' beacons and crown strips (null with none).</summary>
        public CityBeacons Beacons => beacons;

        public int TowerCount { get; private set; }
        public int MidCount { get; private set; }
        public int ElementCount => hidden != null ? hidden.Length : 0;
        public int Triangles => sm != null ? sm.triangles : 0;
        /// <summary>Buildings drawn now (not left to their tiles).</summary>
        public int Shown => shown;
        public int PackTowersLeftOut { get; private set; }

        /// <summary>Build the skyline under <paramref name="parent"/>; null
        /// when the map has no tall core or no facade material.</summary>
        public static CitySkyline Create(Transform parent, CityMap map, CityMeshes.Trims trims,
                                         System.Func<CityMeshes.Slot, Material> matFor)
        {
            var outlines = new HashSet<int>();
            var midOf = new List<bool>();
            var pickClock = System.Diagnostics.Stopwatch.StartNew();
            var elems = Pick(map, out int towers, out int pack, out int mids, outlines, midOf);
            if (elems.Count == 0) return null;
            var sm = CityMeshes.BuildSkyline(map, trims, elems);
            if (sm == null) return null;
            var mf = matFor(CityMeshes.Slot.FacadeGlass);
            var mr0 = matFor(CityMeshes.Slot.RoofFlat);
            if (mf == null || mr0 == null) return null;

            var go = new GameObject("CitySkyline") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var sk = go.AddComponent<CitySkyline>();
            sk.sm = sm;
            sk.TowerCount = towers;
            sk.MidCount = mids;
            sk.PackTowersLeftOut = pack;
            sk.Outlines.UnionWith(outlines);
            sk.mid = midOf.ToArray();
            // its own copies, a pair per draw: the switch is a material value,
            // and the tiles' materials must never see it. Squeezed and hazed
            // from the first frame, and drawn only once a Refresh has left out
            // what the tiles draw.
            sk.mrs = new MeshRenderer[Draws.Length];
            sk.mats = new Material[Draws.Length * 2];
            for (int d = 0; d < Draws.Length; d++)
            {
                var dgo = d == 0 ? go : new GameObject(Draws[d].name) { hideFlags = HideFlags.DontSave };
                if (d > 0) dgo.transform.SetParent(go.transform, false);
                dgo.AddComponent<MeshFilter>().sharedMesh = sm.mesh;
                var r = dgo.AddComponent<MeshRenderer>();
                var pair = new Material[2];
                for (int k = 0; k < 2; k++)
                {
                    var src = k == 0 ? mf : mr0;
                    var m = new Material(src) { name = src.name + " (" + Draws[d].name + ")", hideFlags = HideFlags.DontSave };
                    SetDraw(m, d);
                    // city-popin: hands its pixels to a live tile's own copy (PSX/Lit)
                    m.SetFloat(CityTileFade.ModeId, CityTileFade.ModeSkyline);
                    pair[k] = m;
                    sk.mats[d * 2 + k] = m;
                }
                r.sharedMaterials = pair;
                r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                SunShadows.Exclude(dgo);
                sk.mrs[d] = r;
            }
            sk.mr = sk.mrs[0];
            sk.hazeSet = HazeClearM;

            int n = elems.Count;
            sk.tile = new long[n]; sk.centre = new Vector2[n]; sk.reach = new float[n]; sk.hidden = new bool[n];
            for (int e = 0; e < n; e++)
            {
                var f = map.footprints[elems[e]];
                sk.centre[e] = f.centre;
                float r2 = 0f;
                foreach (var p in f.pts) r2 = Mathf.Max(r2, (p - f.centre).sqrMagnitude);
                sk.reach[e] = Mathf.Sqrt(r2);
                sk.tile[e] = CityWorld.TileKeyAt(f.centre);
            }
            sk.shown = n;
            sk.hidden = new bool[n];
            // the uptown night pass: the towers' aviation beacons and crown strips
            sk.beacons = CityBeacons.Create(go.transform, map, elems, sm.bounds);
            Debug.Log($"[CitySkyline] {towers} towers + {mids} mid-rise ({n} buildings and parts), {sm.triangles} triangles, 2 materials x 3 draws, built in {pickClock.ElapsedMilliseconds} ms; {pack} pack towers among the tallest left to their tiles");
            return sk;
        }

        /// <summary>The tallest <see cref="Towers"/> buildings within
        /// <see cref="CoreRadiusM"/> of uptown (an outline's height is its
        /// tallest part's), each as the footprints the tiles draw: the
        /// outline unless its parts draw it, and every part of it. A lot
        /// whose tower is a pack model is left out (the model is not this
        /// mesh's to draw); it is counted.</summary>
        static List<int> Pick(CityMap map, out int towers, out int pack, out int mids, HashSet<int> outlines, List<bool> midOf)
        {
            var cand = new List<int>();
            float r2 = CoreRadiusM * CoreRadiusM;
            var fs = map.footprints;
            // skyline tops (2026-10-16): a partly mapped outline (a measured
            // tower's podium, its tiers standing on it) is shorter than what
            // stands on it - it is as tall as its tallest part
            var partTop = new Dictionary<int, float>();
            foreach (var p in fs)
                if (p.part && p.outline >= 0 && (!partTop.TryGetValue(p.outline, out float t) || p.h > t)) partTop[p.outline] = p.h;
            float TowerH(int i) => partTop.TryGetValue(i, out float t) ? Mathf.Max(fs[i].h, t) : fs[i].h;
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                if (f.part || f.gable || f.style == 3 || TowerH(i) < MinHeightM) continue;
                if ((f.centre - map.uptown).sqrMagnitude > r2) continue;
                cand.Add(i);
            }
            cand.Sort((a, b) => TowerH(b).CompareTo(TowerH(a)));
            var chosen = new HashSet<int>();
            pack = 0;
            foreach (int i in cand)
            {
                if (chosen.Count >= Towers) break;
                if (fs[i].propKind != 0) { pack++; continue; }
                chosen.Add(i);
            }
            towers = chosen.Count;
            // THE MID-RISE TIER: the 20 m+ buildings round uptown, the same
            // rules (no house, no gable, no pack model)
            var midSet = new HashSet<int>();
            float m2 = MidCoreM * MidCoreM;
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                if (f.part || f.gable || f.style == 3 || f.propKind != 0 || chosen.Contains(i) || TowerH(i) < MidMinM) continue;
                if ((f.centre - map.uptown).sqrMagnitude > m2) continue;
                midSet.Add(i);
            }
            mids = midSet.Count;
            outlines.UnionWith(chosen);
            outlines.UnionWith(midSet);
            var elems = new List<int>();
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                bool tower = f.part ? (chosen.Contains(f.outline) && f.h >= MinPartM) : (chosen.Contains(i) && !f.hidden);
                bool midE = !tower && (f.part ? (midSet.Contains(f.outline) && f.h >= MidMinM) : (midSet.Contains(i) && !f.hidden));
                if (tower || midE) { elems.Add(i); midOf.Add(midE); }
            }
            return elems;
        }

        /// <summary>
        /// Leave out what the live tiles draw, re-index if that changed, set
        /// the renderer's bounds round the SQUEEZED towers (Unity culls by
        /// bounds, and the real ones are past the far plane) and the haze.
        /// <paramref name="isLive"/> answers for a tile key.
        /// </summary>
        static readonly Unity.Profiling.ProfilerMarker ReindexMarker = new Unity.Profiling.ProfilerMarker("CitySkyline.Reindex");

        public void Refresh(Camera cam, System.Func<long, bool> isLive)
        {
            if (sm == null || mr == null) return;
            // the beacons and strips: lit with the street lamps, off in fog
            if (beacons != null) beacons.Show(cam != null && NightGlow.On && Seasons.CurrentWeather != Weather.Fog);
            if (cam == null) { Show(false); return; }
            var weather = Seasons.CurrentWeather;
            if (weather == Weather.Fog) { Show(false); return; }
            float far = cam.farClipPlane;
            float fadeEnd = far * FarShare;
            float fadeStart = Mathf.Min(Shader.GetGlobalFloat(FogNearId), fadeEnd - 30f);
            if (fadeStart < 50f) fadeStart = Mathf.Min(50f, fadeEnd * 0.5f);
            float bandEnd = fadeStart + (fadeEnd - fadeStart) * BandShare;
            Vector3 eye = cam.transform.position;
            var eye2 = new Vector2(eye.x, eye.z);

            // each tower's "its tile is live", re-read when the live set changed
            if (liveOf == null || liveOf.Length != hidden.Length) { liveOf = new bool[hidden.Length]; liveOfVersion = int.MinValue; }
            if (!CacheRefresh || liveOfVersion != LiveVersion)
            {
                for (int e = 0; e < hidden.Length; e++) liveOf[e] = isLive(tile[e]);
                liveOfVersion = LiveVersion;
            }
            bool dirty = false;
            float midDraw = MidDrawM;
            for (int e = 0; e < hidden.Length; e++)
            {
                // de < fadeStart and de > midDraw, de = |eye - centre| - reach,
                // on the squared distance (reach is never negative)
                float d2 = (eye2 - centre[e]).sqrMagnitude;
                // city-popin: left out only when ALL of it is nearer than the fade
                // start (it was: any of it) - PSX/Lit cuts a straddling tower
                // per pixel, its near part the tile's and its far part this
                // copy's, so a tower crossing the far plane is never cut there
                float near = fadeStart - reach[e], farM = midDraw + reach[e];
                bool hide = (liveOf[e] && (near > 0f && d2 < near * near)) || (mid != null && mid[e] && d2 > farM * farM);
                if (hide != hidden[e]) { hidden[e] = hide; dirty = true; }
            }
            if (dirty) using (ReindexMarker.Auto()) Reindex();

            // bounds: the squeezed corners of what is shown
            float moved = boundsValid ? Vector3.Distance(eye, boundsEye) : 0f;
            if (!CacheRefresh || dirty || !boundsValid || moved > BoundsMoveM || fadeStart != boundsFade || bandEnd != boundsBand)
            {
                bool any = false;
                var bb = new Bounds();
                for (int e = 0; e < hidden.Length; e++)
                {
                    if (hidden[e]) continue;
                    var eb = sm.bounds[e];
                    for (int k = 0; k < 8; k++)
                    {
                        var p = new Vector3((k & 1) == 0 ? eb.min.x : eb.max.x, (k & 2) == 0 ? eb.min.y : eb.max.y, (k & 4) == 0 ? eb.min.z : eb.max.z);
                        p = Squeeze(eye, p, fadeStart, bandEnd);
                        if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p);
                    }
                }
                boundsValid = true; boundsAny = any; boundsAt = bb;
                boundsEye = eye; boundsFade = fadeStart; boundsBand = bandEnd;
                moved = 0f;
            }
            Show(boundsAny);
            if (!boundsAny) return;
            var b = boundsAt;
            b.Expand(120f + 2f * BoundsDrift * moved);
            foreach (var r in mrs) r.bounds = b;

            float haze = weather == Weather.Clear ? HazeClearM : HazeWetM;
            if (haze != hazeSet)
            {
                hazeSet = haze;
                foreach (var m in mats) m.SetFloat("_Skyline", haze);
            }
        }

        void Show(bool on)
        {
            foreach (var r in mrs) r.enabled = on;
        }

        /// <summary>The shader's squeeze (PSXLit.shader, the skyline switch):
        /// a point past <paramref name="a"/> metres moved in along its line
        /// of sight to <see cref="SqueezeDist"/>.</summary>
        public static Vector3 Squeeze(Vector3 eye, Vector3 p, float a, float b)
        {
            var rel = p - eye;
            float d = rel.magnitude;
            if (d <= a || d < 1e-3f) return p;
            return eye + rel * (SqueezeDist(d, a, b) / d);
        }

        /// <summary>The squeezed distance (PSXLit.shader and PSXHalo.shader,
        /// the same numbers, 2026-10-16): a + s(1 - e^-(d - a)/s) over 70% of
        /// the band (s = 0.7 (b - a)), plus a log TAIL over the last 30% from
        /// six of those spans past a to <see cref="TailToM"/> - so towers
        /// kilometres out keep their order in depth instead of all landing on
        /// the band's end, which was the flicker the owner saw. Never deeper
        /// than <paramref name="d"/>.</summary>
        public static float SqueezeDist(float d, float a, float b)
        {
            if (d <= a) return d;
            float span = Mathf.Max(b - a, 1f);
            float spanE = span * (1f - TailShare);
            float x = d - a;
            float t0 = TailFromSpans * spanE;
            float tail = Mathf.Clamp01(Mathf.Log(1f + Mathf.Max(x - t0, 0f) / TailKneeM)
                                       / Mathf.Log(1f + Mathf.Max(TailToM - a - t0, 1f) / TailKneeM));
            return a + spanE * (1f - Mathf.Exp(-x / spanE)) + span * TailShare * tail;
        }

        /// <summary>The far tail (PSXLit's SKYLINE_TAIL*): its share of the
        /// band, where it starts (in squeeze spans past a), its log knee and
        /// the distance at which it reaches the band's end.</summary>
        public const float TailShare = 0.30f, TailFromSpans = 6f, TailKneeM = 400f, TailToM = 9000f;

        void Reindex()
        {
            shown = 0;
            for (int e = 0; e < hidden.Length; e++) if (!hidden[e]) shown++;
            for (int s = 0; s < 2; s++)
            {
                idx.Clear();
                var all = sm.indices[s]; var rg = sm.ranges[s];
                for (int e = 0; e < hidden.Length; e++)
                {
                    if (hidden[e]) continue;
                    int a = rg[e * 2], c = rg[e * 2 + 1];
                    for (int k = 0; k < c; k++) idx.Add(all[a + k]);
                }
                sm.mesh.SetTriangles(idx, s, false);
            }
        }

        void OnDestroy()
        {
            if (sm != null && sm.mesh != null) Kill(sm.mesh);
            if (mats != null) foreach (var m in mats) if (m != null) Kill(m);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
