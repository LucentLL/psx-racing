using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 3 (the city-night round, 2026-11-23): the lamp's light in the AIR.
    /// The owner: "I see their triangles of light appear as I drive closer" - the
    /// old cones were the 32 lamps nearest the eye (StreetLights.Cones.cs), so a
    /// cone came on at a lamp whose halo had shown for half a mile. His clear-night
    /// frames have no cone at all. So: in clear air no cone anywhere (the old cone
    /// set is off under NIGHT 3); in rain, fog and snow a cone under EVERY lamp
    /// that shows a halo - one merged mesh per lamp set (a tile), built the first
    /// time the air turns thick and then kept - thinned only by the air between it
    /// and the eye (PSX/LampConeAir). The renderers are switched with the weather;
    /// a clear night costs nothing.
    /// </summary>
    public partial class NightGlow
    {
        const string ConeAirName = "LampConesAir";
        GameObject coneAirGo;
        MeshRenderer coneAirMr;
        static Material coneAirMat;
        static bool coneAirWarned;
        static bool airOn;
        static int airBuildCursor;

        /// <summary>The air's haze now (StreetLights.PushNight3Globals, once a frame): renderers
        /// on while it is above nothing and the lamps are lit; up to a few sets built a frame.</summary>
        internal static void TickAir(float haze)
        {
            bool want = haze > 0.002f && on && Application.isPlaying;
            if (want != airOn)
            {
                airOn = want;
                for (int i = all.Count - 1; i >= 0; i--)
                    if (all[i] != null && all[i].coneAirMr != null) all[i].coneAirMr.enabled = want;
            }
            if (!want || all.Count == 0) return;
            // build the sets that have no cones yet, a few a frame (a whole ring at the first rain)
            int built = 0;
            for (int n = 0; n < all.Count && built < 6; n++)
            {
                airBuildCursor = (airBuildCursor + 1) % all.Count;
                var g = all[airBuildCursor];
                if (g == null || g.coneAirGo != null || g.heads.Count == 0) continue;
                g.BuildConesAir();
                built++;
            }
        }

        void BuildConesAir()
        {
            if (coneAirMat == null)
            {
                var sh = Resources.Load<Shader>("NightSplat/PSXLampConeAir");
                if (sh == null || !sh.isSupported)
                {
                    if (!coneAirWarned) { coneAirWarned = true; Debug.LogWarning("NightGlow: PSX/LampConeAir missing - no cones in the rain"); }
                    coneAirGo = new GameObject(ConeAirName);   // (marks the set done)
                    coneAirGo.transform.SetParent(transform, false);
                    return;
                }
                coneAirMat = new Material(sh) { name = "LampConesAir", hideFlags = HideFlags.DontSave };
            }
            int n = Mathf.Min(heads.Count, lampHeads);
            coneAirGo = new GameObject(ConeAirName);
            coneAirGo.layer = gameObject.layer;
            coneAirGo.transform.SetParent(transform, false);
            if (n <= 0) return;
            const int Sides = 12;
            int perV = Sides * 2 + 2, perT = Sides * 4;
            var verts = new List<Vector3>(n * perV);
            var ua = new List<Vector4>(n * perV);
            var ub = new List<Vector4>(n * perV);
            var tris = new List<int>(n * perT * 3);
            float r = 1f / Mathf.Cos(Mathf.PI / Sides);
            var unit = new Vector3[perV];
            for (int s = 0; s < Sides; s++)
            {
                float a = s * 2f * Mathf.PI / Sides;
                var xz = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                unit[s] = xz; unit[Sides + s] = xz + Vector3.down;
            }
            unit[Sides * 2] = Vector3.zero; unit[Sides * 2 + 1] = Vector3.down;
            Color bulb = StreetLights.Bulb.linear, merc = StreetLights.MercuryLamp.linear;
            float strength = LampCones.Strength;
            var wb = new Bounds(heads[0], Vector3.zero);
            for (int i = 0; i < n; i++)
            {
                Vector3 h = heads[i];
                float H = StreetLights.ConeDefaultH;
                if (Physics.Raycast(h + Vector3.down * 0.3f, Vector3.down, out var hit, 40f, ~(1 << 2), QueryTriggerInteraction.Ignore)) H = Mathf.Clamp(h.y - hit.point.y, 2f, 30f);
                Color c = (lampColour.HasValue ? lampColour.Value.linear : (Merc(i) ? merc : bulb)) * (strength * (i < gains.Count ? gains[i] : 1f));
                var A = new Vector4(h.x, h.y, h.z, H);
                var B = new Vector4(c.r, c.g, c.b, LampCones.TopRadiusM);
                int v0 = verts.Count;
                for (int k = 0; k < perV; k++) { verts.Add(unit[k]); ua.Add(A); ub.Add(B); }
                int top = v0 + Sides * 2, bot = top + 1;
                for (int s = 0; s < Sides; s++)
                {
                    int s1 = (s + 1) % Sides;
                    int a0 = v0 + s, a1 = v0 + s1, b0 = v0 + Sides + s, b1 = v0 + Sides + s1;
                    // outward-facing (PSX/LampConeAir culls the front: the far side is drawn)
                    tris.Add(a0); tris.Add(b1); tris.Add(b0);
                    tris.Add(a0); tris.Add(a1); tris.Add(b1);
                    tris.Add(top); tris.Add(a1); tris.Add(a0);
                    tris.Add(bot); tris.Add(b0); tris.Add(b1);
                }
                float rr = LampCones.TopRadiusM + 0.8f * H + 0.5f;
                wb.Encapsulate(h + new Vector3(rr, 0.5f, rr));
                wb.Encapsulate(h - new Vector3(rr, H + 0.5f, rr));
            }
            var mesh = new Mesh { name = "PSXLampConesAir", hideFlags = HideFlags.DontSave };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, ua);
            mesh.SetUVs(1, ub);
            mesh.SetTriangles(tris, 0, false);
            // the shader places every vertex in the world: the bounds are the world box, in this object's space
            var t = coneAirGo.transform;
            var lb = new Bounds(t.InverseTransformPoint(wb.center), Vector3.zero);
            for (int k = 0; k < 8; k++)
            {
                var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((k & 1) != 0 ? 1 : -1, (k & 2) != 0 ? 1 : -1, (k & 4) != 0 ? 1 : -1));
                lb.Encapsulate(t.InverseTransformPoint(corner));
            }
            mesh.bounds = lb;
            coneAirGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            coneAirMr = coneAirGo.AddComponent<MeshRenderer>();
            coneAirMr.sharedMaterial = coneAirMat;
            coneAirMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coneAirMr.receiveShadows = false;
            coneAirMr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            coneAirMr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            coneAirMr.enabled = airOn;
        }

        void DropConesAir()
        {
            if (coneAirGo == null) return;
            var mf = coneAirGo.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) { if (Application.isPlaying) Destroy(mf.sharedMesh); else DestroyImmediate(mf.sharedMesh); }
        }
    }
}
