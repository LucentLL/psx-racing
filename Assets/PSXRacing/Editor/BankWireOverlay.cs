using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// (stage4) THE CUT SOLIDS AS A WIRE over a look shot (PSX_LIGHTPOP_WIRE=1, LightPopCheck's tunnel/metre shots):
    /// every edge of the rock cuts' colliders near the car - the band solids or the old slabs (BankColl), the rock
    /// top's face collider and the crest lip's (not the rock top itself, which is its own drawn mesh) - yellow where
    /// nothing drawn hides it, dark red where the drawn rock stands in front of it. Editor frames only; the overlay is
    /// destroyed after the shot. PSX_BF_SOLIDSET=old on a PSX_BANKSOLID_AB=1 scene shows the old slab instead.
    /// </summary>
    public static class BankWireOverlay
    {
        public static bool Wanted => System.Environment.GetEnvironmentVariable("PSX_LIGHTPOP_WIRE") == "1";

        public static GameObject Build(Vector3 centre, float radius)
        {
            var root = new GameObject("BankWireOverlay");
            var shader = Shader.Find("Hidden/Internal-Colored");
            Material Mat(Color c, UnityEngine.Rendering.CompareFunction zt)
            {
                var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                m.SetColor("_Color", c);
                m.SetInt("_ZTest", (int)zt);
                m.SetInt("_ZWrite", 0);
                m.SetInt("_Cull", 0);
                m.renderQueue = 4000;
                return m;
            }
            var front = Mat(new Color(1f, 0.92f, 0.1f, 1f), UnityEngine.Rendering.CompareFunction.LessEqual);
            var hidden = Mat(new Color(0.75f, 0.05f, 0.05f, 1f), UnityEngine.Rendering.CompareFunction.Greater);
            var pts = new List<Vector3>();
            var seen = new HashSet<long>();
            float r2 = radius * radius;
            int colls = 0;
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            {
                if (mc == null || !mc.enabled || !mc.gameObject.activeInHierarchy || mc.sharedMesh == null) continue;
                if (!mc.name.StartsWith("Bank")) continue;
                string mn = mc.sharedMesh.name;
                if (mn.Contains("BankTopMesh")) continue;
                if (!mc.bounds.Intersects(new Bounds(centre, Vector3.one * radius * 2f))) continue;
                var mesh = mc.sharedMesh;
                var v = mesh.vertices; var t = mesh.triangles;
                var xf = mc.transform;
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) w[i] = xf.TransformPoint(v[i]);
                seen.Clear();
                colls++;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 cc = (w[t[i]] + w[t[i + 1]] + w[t[i + 2]]) / 3f;
                    if ((cc - centre).sqrMagnitude > r2) continue;
                    for (int e = 0; e < 3; e++)
                    {
                        int a = t[i + e], b = t[i + (e + 1) % 3];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (!seen.Add(key)) continue;
                        pts.Add(w[a]); pts.Add(w[b]);
                    }
                }
            }
            for (int s = 0; s < pts.Count; s += 60000)
            {
                int n = Mathf.Min(60000, pts.Count - s);
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(pts.GetRange(s, n));
                var idx = new int[n];
                for (int i = 0; i < n; i++) idx[i] = i;
                m.SetIndices(idx, MeshTopology.Lines, 0);
                m.bounds = new Bounds(centre, Vector3.one * radius * 4f);
                foreach (var mat in new[] { hidden, front })
                {
                    var go = new GameObject("wire");
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
            Debug.Log($"[BankWire] {colls} cut colliders, {pts.Count / 2} edges within {radius:0} m");
            return root;
        }
    }
}
