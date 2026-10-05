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
    /// order kept - and hazed toward the hour's horizon colour by its REAL
    /// distance (a fifth at 5 km by clear day, never the white wall the owner
    /// banned: the drawn world still ends in its own short edge fade). At
    /// night its lit windows are the facade's own.
    ///
    /// A building is left out while its tile is live AND it is nearer than
    /// the world's fade start: there the tile draws it, clear; further out
    /// the tile's copy is fading into the sky and the skyline's (squeezed in
    /// front of it) carries on. In fog weather the skyline is off.
    /// </summary>
    public class CitySkyline : MonoBehaviour
    {
        /// <summary>How many towers: the tallest of the core.</summary>
        public const int Towers = 60;
        /// <summary>The core the towers are taken from, metres round uptown.</summary>
        public const float CoreRadiusM = 2500f;
        /// <summary>Nothing shorter is a skyline tower.</summary>
        const float MinHeightM = 45f;
        /// <summary>A part lower than this is left to its tile: kilometres
        /// out it is under the world's own edge, and a stadium's low rings
        /// are a quarter of the skyline's triangles.</summary>
        const float MinPartM = 20f;
        /// <summary>The haze's e-folding distance by clear weather, and in
        /// rain or snow (metres): 1 - exp(-d / this) of the horizon colour.</summary>
        public const float HazeClearM = 24000f, HazeWetM = 9000f;
        /// <summary>The world's edge fade runs to this share of the far plane
        /// (PSXLit.shader's SKYLINE_FAR, the same number)...</summary>
        const float FarShare = 0.997f;
        /// <summary>...and the squeezed skyline ends this far into it, where
        /// the fade is about half (SKYLINE_BAND): the world's last, mostly
        /// sky-coloured metres are drawn behind the towers, not over them.</summary>
        const float BandShare = 0.70f;

        CityMeshes.SkylineMesh sm;
        long[] tile;
        Vector2[] centre;
        float[] reach;
        bool[] hidden;
        int shown;
        readonly List<int> idx = new List<int>(16384);
        MeshRenderer mr;
        Material[] mats;
        float hazeSet = -1f;

        public int TowerCount { get; private set; }
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
            var elems = Pick(map, out int towers, out int pack);
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
            sk.PackTowersLeftOut = pack;
            go.AddComponent<MeshFilter>().sharedMesh = sm.mesh;
            sk.mr = go.AddComponent<MeshRenderer>();
            // its own copies: the switch is a material value, and the tiles'
            // materials must never see it
            sk.mats = new[] { new Material(mf) { name = mf.name + " (skyline)", hideFlags = HideFlags.DontSave },
                              new Material(mr0) { name = mr0.name + " (skyline)", hideFlags = HideFlags.DontSave } };
            // squeezed and hazed from the first frame, and drawn only once a
            // Refresh has left out what the tiles draw
            foreach (var m in sk.mats) m.SetFloat("_Skyline", HazeClearM);
            sk.hazeSet = HazeClearM;
            sk.mr.sharedMaterials = sk.mats;
            sk.mr.enabled = false;
            sk.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sk.mr.receiveShadows = false;
            sk.mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            sk.mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            SunShadows.Exclude(go);

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
            Debug.Log($"[CitySkyline] {towers} towers ({n} buildings and parts), {sm.triangles} triangles, 2 draws; {pack} pack towers among the tallest left to their tiles");
            return sk;
        }

        /// <summary>The tallest <see cref="Towers"/> buildings within
        /// <see cref="CoreRadiusM"/> of uptown (an outline's height is its
        /// tallest part's), each as the footprints the tiles draw: the
        /// outline unless its parts draw it, and every part of it. A lot
        /// whose tower is a pack model is left out (the model is not this
        /// mesh's to draw); it is counted.</summary>
        static List<int> Pick(CityMap map, out int towers, out int pack)
        {
            var cand = new List<int>();
            float r2 = CoreRadiusM * CoreRadiusM;
            var fs = map.footprints;
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                if (f.part || f.gable || f.style == 3 || f.h < MinHeightM) continue;
                if ((f.centre - map.uptown).sqrMagnitude > r2) continue;
                cand.Add(i);
            }
            cand.Sort((a, b) => fs[b].h.CompareTo(fs[a].h));
            var chosen = new HashSet<int>();
            pack = 0;
            foreach (int i in cand)
            {
                if (chosen.Count >= Towers) break;
                if (fs[i].propKind != 0) { pack++; continue; }
                chosen.Add(i);
            }
            towers = chosen.Count;
            var elems = new List<int>();
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                if (f.part ? (chosen.Contains(f.outline) && f.h >= MinPartM) : (chosen.Contains(i) && !f.hidden)) elems.Add(i);
            }
            return elems;
        }

        /// <summary>
        /// Leave out what the live tiles draw, re-index if that changed, set
        /// the renderer's bounds round the SQUEEZED towers (Unity culls by
        /// bounds, and the real ones are past the far plane) and the haze.
        /// <paramref name="isLive"/> answers for a tile key.
        /// </summary>
        public void Refresh(Camera cam, System.Func<long, bool> isLive)
        {
            if (sm == null || mr == null) return;
            if (cam == null) { mr.enabled = false; return; }
            var weather = Seasons.CurrentWeather;
            if (weather == Weather.Fog) { mr.enabled = false; return; }
            float far = cam.farClipPlane;
            float fadeEnd = far * FarShare;
            float fadeStart = Mathf.Min(Shader.GetGlobalFloat("_PSXFogNear"), fadeEnd - 30f);
            if (fadeStart < 50f) fadeStart = Mathf.Min(50f, fadeEnd * 0.5f);
            float bandEnd = fadeStart + (fadeEnd - fadeStart) * BandShare;
            Vector3 eye = cam.transform.position;
            var eye2 = new Vector2(eye.x, eye.z);

            bool dirty = false;
            for (int e = 0; e < hidden.Length; e++)
            {
                bool hide = isLive(tile[e]) && Vector2.Distance(eye2, centre[e]) - reach[e] < fadeStart;
                if (hide != hidden[e]) { hidden[e] = hide; dirty = true; }
            }
            if (dirty) Reindex();

            // bounds: the squeezed corners of what is shown
            bool any = false;
            var b = new Bounds();
            for (int e = 0; e < hidden.Length; e++)
            {
                if (hidden[e]) continue;
                var eb = sm.bounds[e];
                for (int k = 0; k < 8; k++)
                {
                    var p = new Vector3((k & 1) == 0 ? eb.min.x : eb.max.x, (k & 2) == 0 ? eb.min.y : eb.max.y, (k & 4) == 0 ? eb.min.z : eb.max.z);
                    p = Squeeze(eye, p, fadeStart, bandEnd);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            mr.enabled = any;
            if (!any) return;
            b.Expand(120f);
            mr.bounds = b;

            float haze = weather == Weather.Clear ? HazeClearM : HazeWetM;
            if (haze != hazeSet)
            {
                hazeSet = haze;
                foreach (var m in mats) m.SetFloat("_Skyline", haze);
            }
        }

        /// <summary>The shader's squeeze (PSXLit.shader, the skyline switch):
        /// a point past <paramref name="a"/> metres moved in along its line
        /// of sight to a + (b - a)(1 - e^-(d - a)/(b - a)).</summary>
        public static Vector3 Squeeze(Vector3 eye, Vector3 p, float a, float b)
        {
            var rel = p - eye;
            float d = rel.magnitude;
            if (d <= a || d < 1e-3f) return p;
            float span = Mathf.Max(b - a, 1f);
            float dc = a + span * (1f - Mathf.Exp(-(d - a) / span));
            return eye + rel * (dc / d);
        }

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
