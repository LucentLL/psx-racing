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
    ///   * LED EDGE STRIPS on the crowns the real towers outline in light
    ///     (CityMeshes.NightCrowns' strips flag): beads of the tower's LED
    ///     colour every <see cref="BeadM"/> along the top edges of its crown's
    ///     members and up its shaft's corners, which merge into lines at a
    ///     distance (the crown itself is not floodlit).
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
        /// <summary>Metres between a strip's beads.</summary>
        const float BeadM = 1.6f;
        /// <summary>A tower's non-glass part carries its LED lines when it
        /// rises to this share of the tower's height (the open frame's
        /// members: the sloped pieces from its 180 m roof, the bridge, the
        /// ridge beam), and its shaft's corners carry them when the shaft
        /// does.</summary>
        const float FrameFromShare = 0.7f;
        /// <summary>A member's edge facing in across its tower by more than
        /// this (its outward side looking at the shaft's middle this far off)
        /// is the inside of a face and carries no line.</summary>
        const float InnerM = 5f;
        /// <summary>Halo half-sizes as multiples of the material's _Size
        /// (<see cref="NightGlow.HaloSize"/>): a beacon's red glow, a bead.</summary>
        const float BeaconSize = 1.0f, BeadSize = 0.3f;
        static readonly Color BeaconRed = new Color(1.00f, 0.06f, 0.03f);

        MeshRenderer mr;
        Mesh mesh;
        Material mat;
        public int BeaconCount { get; private set; }
        public int BeadCount { get; private set; }

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

            // the crown strips
            int beads = 0;
            var stripTowers = new HashSet<int>();
            foreach (var kv in topOf) if (fs[kv.Key].landmark != 0 && CityMeshes.NightStripsOf(fs[kv.Key].landmark)) stripTowers.Add(kv.Key);
            foreach (int oi in stripTowers)
            {
                var of = fs[oi];
                var col = CityMeshes.LedColour(CityMeshes.NightLedOf(of.landmark));
                float ground = GroundOf(map, of);
                float towerH = topOf[oi].y - ground;
                // the shaft: the tallest of the tower's parts on the ground
                int si = -1;
                for (int i = 0; i < fs.Length; i++)
                    if (fs[i].part && fs[i].outline == oi && fs[i].minH < 1f && (si < 0 || fs[i].h > fs[si].h)) si = i;
                var core = si >= 0 ? fs[si].centre : of.centre;
                int pieces = 0;
                for (int i = 0; i < fs.Length; i++)
                {
                    var f = fs[i];
                    // the frame the lines run on (2026-10-29, the owner's night
                    // photograph of the open-frame tower: thin lines up its
                    // corners and along its crown's members, the glass dark):
                    // the tower's own pieces that are not glass and rise into
                    // its top - the frame's members, along their top edges
                    if (!f.part || f.outline != oi || f.mat == 1 || f.h < FrameFromShare * towerH) continue;
                    beads += StripPiece(centres, uv1, cols, f, ground, col, core);
                    pieces++;
                }
                // ...and up its vertical edges: OSM's posts on the shaft's
                // corners fall under the exporter's 2 m2 floor, so the lines
                // run up the shaft's own corners, to the frame's foot on each
                if (pieces > 0 && si >= 0 && fs[si].h >= FrameFromShare * towerH)
                    beads += StripCorners(centres, uv1, cols, fs, oi, si, ground, towerH, col);
                // no mapped crown pieces: the outline's top ring
                if (pieces == 0 && !of.hidden) beads += StripRing(centres, uv1, cols, of.pts, ground + of.h, col);
            }
            if (centres.Count == 0) return null;

            var go = new GameObject("CityBeacons") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var cb = go.AddComponent<CityBeacons>();
            cb.BeaconCount = beacons;
            cb.BeadCount = beads;
            cb.mesh = BuildMesh(centres, uv1, cols, map.uptown);
            go.AddComponent<MeshFilter>().sharedMesh = cb.mesh;
            cb.mat = new Material(sh) { name = "SkylineLights", hideFlags = HideFlags.HideAndDontSave };
            cb.mat.SetColor("_Color", Color.white);
            cb.mat.SetFloat("_Size", NightGlow.HaloSize);
            // strength 1: a stronger halo clamps the LED colour pale in the
            // 8-bit framebuffer (the first cut's blue read as white)
            cb.mat.SetFloat("_Strength", 1.0f);
            cb.mat.SetFloat("_MinPixels", 1.6f);
            cb.mr = go.AddComponent<MeshRenderer>();
            cb.mr.sharedMaterial = cb.mat;
            cb.mr.enabled = false;
            cb.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cb.mr.receiveShadows = false;
            cb.mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            cb.mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            Debug.Log($"[CityBeacons] {beacons} aviation beacons (towers of {MinTowerM:0} m+), {beads} crown strip beads on {stripTowers.Count} towers, 1 draw");
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

        /// <summary>The tower's four vertical edges: a line up each corner of
        /// its shaft (the tallest of its parts on the ground; a chamfered
        /// corner's middle) from the ground to the highest frame member
        /// standing on the shaft's roof over that corner.</summary>
        static int StripCorners(List<Vector3> c, List<Vector2> u, List<Color32> k, CityMap.Footprint[] fs, int oi, int si, float ground, float towerH, Color col)
        {
            var s = fs[si];
            var v = new Vector2(-s.u.y, s.u.x);
            int beads = 0;
            for (int q = 0; q < 4; q++)
            {
                var d = ((q & 1) == 0 ? s.u : -s.u) + ((q & 2) == 0 ? v : -v);
                float most = float.MinValue;
                foreach (var p in s.pts) most = Mathf.Max(most, Vector2.Dot(p - s.centre, d));
                Vector2 at = Vector2.zero;
                int n = 0;
                foreach (var p in s.pts) if (Vector2.Dot(p - s.centre, d) > most - 2f) { at += p; n++; }
                at /= n;
                float top = ground + s.h;
                foreach (var f in fs)
                {
                    if (!f.part || f.outline != oi || f.mat == 1 || f.h < FrameFromShare * towerH || Mathf.Abs(f.minH - s.h) > 0.5f) continue;
                    bool near = false;
                    foreach (var p in f.pts) if ((p - at).sqrMagnitude < 25f) { near = true; break; }
                    if (near) top = Mathf.Max(top, TopAt(f, ground, at));
                }
                byte face = FaceCode(d, true);
                for (float y = ground + BeadM * 0.5f; y < top; y += BeadM) { Add(c, u, k, new Vector3(at.x, y, at.y), BeadSize, -0.5f, col, face); beads++; }
            }
            return beads;
        }

        /// <summary>PSX/Halo's one-way code for a tower light (its colour
        /// alpha): the compass yaw <paramref name="outward"/> points along, in
        /// 127 steps - a face's top edge 0..126, a corner 127..253 (lit three
        /// quarters round). 255 lights all round.</summary>
        static byte FaceCode(Vector2 outward, bool corner)
        {
            float yaw01 = Mathf.Repeat(Mathf.Atan2(outward.x, outward.y) / (2f * Mathf.PI), 1f);
            int step = Mathf.RoundToInt(yaw01 * 127f) % 127;
            return (byte)(step + (corner ? 127 : 0));
        }

        /// <summary>A part's top over a plan point (a skillion's sloped as
        /// CityMeshes.EmitRoofShape slopes it), held within the part.</summary>
        static float TopAt(CityMap.Footprint f, float ground, Vector2 at)
        {
            float top = ground + f.h;
            float rise = f.roof == 4 ? Mathf.Min(f.roofH, f.h - f.minH) : 0f;
            if (rise <= 0.05f) return top;
            var dir = f.roofDir >= 0 ? new Vector2(Mathf.Sin(f.roofDir * Mathf.Deg2Rad), Mathf.Cos(f.roofDir * Mathf.Deg2Rad))
                                     : new Vector2(f.u.y, -f.u.x);
            float s0 = float.MaxValue, s1 = float.MinValue;
            foreach (var p in f.pts) { float sp = Vector2.Dot(p, dir); s0 = Mathf.Min(s0, sp); s1 = Mathf.Max(s1, sp); }
            float y = top - rise * (Vector2.Dot(at, dir) - s0) / Mathf.Max(0.5f, s1 - s0);
            return Mathf.Clamp(y, ground + f.minH, top);
        }

        /// <summary>One frame member outlined: its top edges (a skillion's
        /// sloped as CityMeshes.EmitRoofShape slopes it), each seen only from
        /// the side it faces; an edge that faces in across the tower (more
        /// than <see cref="InnerM"/> short of its middle, <paramref name="core"/>)
        /// is the far face's back and carries none. Not its corners: a line
        /// down every corner of a sloped member crossed its tower's glass
        /// with lines the real one does not carry.</summary>
        static int StripPiece(List<Vector3> c, List<Vector2> u, List<Color32> k, CityMap.Footprint f, float ground, Color col, Vector2 core)
        {
            var pts = f.pts;
            int n = pts.Length;
            if (n < 3) return 0;
            float top = ground + f.h;
            float rise = f.roof == 4 ? Mathf.Min(f.roofH, f.h - f.minH) : 0f;
            Vector2 dir = Vector2.up; float s0 = 0f, span = 1f;
            if (rise > 0.05f)
            {
                dir = f.roofDir >= 0 ? new Vector2(Mathf.Sin(f.roofDir * Mathf.Deg2Rad), Mathf.Cos(f.roofDir * Mathf.Deg2Rad))
                                     : new Vector2(f.u.y, -f.u.x);
                float s1 = float.MinValue; s0 = float.MaxValue;
                foreach (var p in pts) { float sp = Vector2.Dot(p, dir); s0 = Mathf.Min(s0, sp); s1 = Mathf.Max(s1, sp); }
                span = Mathf.Max(0.5f, s1 - s0);
            }
            float YAt(Vector2 p) => rise > 0.05f ? top - rise * (Vector2.Dot(p, dir) - s0) / span : top;
            int beads = 0;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                float len = Vector2.Distance(a, b);
                if (len < 0.5f) continue;
                var outward = new Vector2(b.y - a.y, a.x - b.x) / len;   // CCW outline
                if (Vector2.Dot(outward, core - (a + b) * 0.5f) > InnerM) continue;
                byte face = FaceCode(outward, false);
                int steps = Mathf.Max(1, Mathf.RoundToInt(len / BeadM));
                for (int s = 0; s < steps; s++)
                {
                    var p = Vector2.Lerp(a, b, s / (float)steps);
                    Add(c, u, k, new Vector3(p.x, YAt(p), p.y), BeadSize, -0.5f, col, face); beads++;
                }
            }
            return beads;
        }

        static int StripRing(List<Vector3> c, List<Vector2> u, List<Color32> k, Vector2[] pts, float y, Color col)
        {
            int beads = 0, n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                int steps = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(a, b) / BeadM));
                for (int s = 0; s < steps; s++) { var p = Vector2.Lerp(a, b, s / (float)steps); Add(c, u, k, new Vector3(p.x, y, p.y), BeadSize, -0.5f, col); beads++; }
            }
            return beads;
        }

        static void Add(List<Vector3> c, List<Vector2> u, List<Color32> k, Vector3 at, float size, float mode, Color col, byte face = 255)
        {
            c.Add(at); u.Add(new Vector2(size, mode));
            k.Add(new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(col.r) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(col.g) * 255f),
                              (byte)Mathf.RoundToInt(Mathf.Clamp01(col.b) * 255f), face));
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
