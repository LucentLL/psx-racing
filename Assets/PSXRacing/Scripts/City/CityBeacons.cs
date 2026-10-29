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
    /// </summary>
    public class CityBeacons : MonoBehaviour
    {
        /// <summary>A tower this tall over its ground carries a beacon.</summary>
        public const float MinTowerM = 90f;
        /// <summary>A roof corner's light stands on a shaft at least this
        /// share of its tower's height (a podium's corners carry none).</summary>
        const float CornerFromShare = 0.6f;
        /// <summary>Two corner lights nearer than this are one.</summary>
        const float CornerMinM = 12f;
        /// <summary>Halo half-sizes as multiples of the material's _Size
        /// (<see cref="NightGlow.HaloSize"/>): a beacon's red glow, a corner light.</summary>
        const float BeaconSize = 1.0f, CornerSize = 0.55f;
        static readonly Color BeaconRed = new Color(1.00f, 0.06f, 0.03f);

        MeshRenderer mr;
        Mesh mesh;
        Material mat;
        public int BeaconCount { get; private set; }
        public int CornerCount { get; private set; }

        /// <summary>Build under the skyline's object from its elements (map
        /// footprint indices) and their drawn bounds; null when there is
        /// nothing to light or no PSX/Halo shader.</summary>
        public static CityBeacons Create(Transform parent, CityMap map, List<int> elems, Bounds[] bounds)
        {
            var sh = Shader.Find("PSX/Halo");
            if (sh == null || map == null || elems == null || bounds == null) return null;
            var fs = map.footprints;
            var centres = new List<Vector3>();
            var uv1 = new List<Vector2>();
            var cols = new List<Color32>();

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
            int beacons = 0;
            foreach (var kv in topOf)
            {
                float ground = GroundOf(map, fs[kv.Key]);
                if (kv.Value.y - ground < MinTowerM) continue;
                uint h = (uint)kv.Key * 2654435761u;
                float phase = (h >> 8) / 16777216f;
                Add(centres, uv1, cols, kv.Value + Vector3.up * 0.5f, BeaconSize, -(1f + 0.999f * phase), BeaconRed);
                beacons++;
            }

            // THE ROOF CORNERS (the 1990s city): a steady red light on each
            // corner of the tower's shaft - the tallest of its parts on the
            // ground, or the outline itself - at its roof
            int corners = 0;
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
                corners += Corners(centres, uv1, cols, shaft, ground + shaft.h + 0.4f);
            }
            if (centres.Count == 0) return null;

            var go = new GameObject("CityBeacons") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var cb = go.AddComponent<CityBeacons>();
            cb.BeaconCount = beacons;
            cb.CornerCount = corners;
            cb.mesh = BuildMesh(centres, uv1, cols, map.uptown);
            go.AddComponent<MeshFilter>().sharedMesh = cb.mesh;
            cb.mat = new Material(sh) { name = "SkylineLights", hideFlags = HideFlags.HideAndDontSave };
            cb.mat.SetColor("_Color", Color.white);
            cb.mat.SetFloat("_Size", NightGlow.HaloSize);
            // strength 1: a stronger halo clamps the red pale in the 8-bit framebuffer
            cb.mat.SetFloat("_Strength", 1.0f);
            cb.mat.SetFloat("_MinPixels", 1.6f);
            cb.mr = go.AddComponent<MeshRenderer>();
            cb.mr.sharedMaterial = cb.mat;
            cb.mr.enabled = false;
            cb.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cb.mr.receiveShadows = false;
            cb.mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            cb.mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            Debug.Log($"[CityBeacons] {beacons} aviation beacons (towers of {MinTowerM:0} m+), {corners} roof-corner lights, 1 draw");
            return cb;
        }

        public void Show(bool on)
        {
            if (mr != null && mr.enabled != on) mr.enabled = on;
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
            if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            if (mat != null) { if (Application.isPlaying) Destroy(mat); else DestroyImmediate(mat); }
        }
    }
}
