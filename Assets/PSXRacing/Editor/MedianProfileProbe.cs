using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ACROSS A DIVIDED STREET'S MEDIAN (2026-10-25, the owner: "I just crashed
    /// into the same West Trade Street median that was supposed to be
    /// removed"). The lane audits sweep the lanes; this slices ACROSS the
    /// median: every PSX_MED_STEP (2) m of edge PSX_MED_EDGE between
    /// PSX_MED_S0 and PSX_MED_S1, from the edge's centre line to its opposing
    /// twin's (same name, one-way, the other way, nearest within 40 m), the
    /// built world's surface every 0.5 m (height over the edge's road, which
    /// collider), and horizontal rays at 0.3 / 0.6 m over the road from the
    /// inner lane toward the twin (the first solid met). The tiles are the
    /// game's (CityWorld.EnsureRing). Writes median_profile.txt at the
    /// project root. Headless: -executeMethod PSXRacing.EditorTools.MedianProfileProbe.Run
    /// </summary>
    public static class MedianProfileProbe
    {
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[MedianProbe] no city data"); return; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int ei = int.Parse(System.Environment.GetEnvironmentVariable("PSX_MED_EDGE") ?? "5781", ci);
            var e = map.edges[ei];
            float s0 = float.Parse(System.Environment.GetEnvironmentVariable("PSX_MED_S0") ?? "0", ci);
            float s1 = Mathf.Min(e.length, float.Parse(System.Environment.GetEnvironmentVariable("PSX_MED_S1") ?? e.length.ToString(ci), ci));
            float step = float.Parse(System.Environment.GetEnvironmentVariable("PSX_MED_STEP") ?? "2", ci);
            var go = new GameObject("~medianProbe");
            var world = go.AddComponent<CityWorld>();
            var sb = new StringBuilder();
            try
            {
                var pm = e.PointAt(0.5f * (s0 + s1));
                world.EnsureRing(new Vector3(pm.x, e.YAt(0.5f * (s0 + s1)), pm.y), 1);
                Physics.SyncTransforms();
                var hits = new RaycastHit[16];
                sb.AppendLine($"MEDIAN PROFILE e{ei} '{e.name}' s {s0:0}..{s1:0} of {e.length:0} m, every {step} m; surface over the road every 0.5 m toward the twin (h@d collider), then rays at 0.3/0.6 m");
                sb.AppendLine($"street halves held to one height city-wide: {CityElevation.StreetHalvesRaised} stations raised, up to {CityElevation.StreetHalvesRaiseMax:0.00} m");
                for (float s = s0; s <= s1 + 1e-3f; s += step)
                {
                    var p = e.PointAt(s);
                    float y = e.YAt(s);
                    var t = e.TangentAt(s).normalized;
                    // the opposing twin's nearest point
                    CityMap.Edge tw = null; float bestD = 40f; Vector2 q = Vector2.zero; float qs = 0f;
                    foreach (var c in map.edges)
                    {
                        if (c == e || !c.oneway || c.name != e.name || c.pts == null) continue;
                        for (int k = 0; k + 1 < c.pts.Length; k++)
                        {
                            var a = c.pts[k]; var ab = c.pts[k + 1] - a;
                            float L2 = ab.sqrMagnitude;
                            if (L2 < 1e-6f || Vector2.Dot(ab.normalized, t) > -0.5f) continue;
                            float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / L2);
                            var f = a + ab * u;
                            float d = Vector2.Distance(p, f);
                            if (d < bestD) { bestD = d; tw = c; q = f; qs = c.s[k] + u * Mathf.Sqrt(L2); }
                        }
                    }
                    if (tw == null) { sb.AppendLine($"s={s:0.0} ({p.x:0.0},{p.y:0.0}) y {y:0.00}: no twin within 40 m"); continue; }
                    float ty = tw.YAt(qs);
                    var dir = (q - p).normalized;
                    var line = new StringBuilder($"s={s:0.0} ({p.x:0.0},{p.y:0.0}) y {y:0.00} | twin e{tw.index} s={qs:0.0} at {bestD:0.0} m, y {ty:0.00} (dy {ty - y:+0.00;-0.00}) | surface:");
                    string lastName = null;
                    for (float d = 0f; d <= bestD + 1e-3f; d += 0.5f)
                    {
                        var w = p + dir * d;
                        int n = Physics.RaycastNonAlloc(new Vector3(w.x, y + 8f, w.y), Vector3.down, hits, 20f, ~0, QueryTriggerInteraction.Ignore);
                        float top = float.NegativeInfinity; string name = "-";
                        for (int i = 0; i < n; i++) if (hits[i].point.y > top) { top = hits[i].point.y; name = hits[i].collider.name; }
                        string nm = name == lastName ? "" : " " + name;
                        lastName = name;
                        line.Append($" {(float.IsNegativeInfinity(top) ? "none" : (top - y).ToString("+0.00;-0.00", ci))}@{d:0.0}{nm}");
                    }
                    // the first solid toward the twin from 1.8 m inside the inner edge
                    LineModel.Extents(e, s, out float em, out float ep);
                    float inner = Mathf.Max(0.5f, Mathf.Max(em, ep) - 1.8f);
                    foreach (float h in new[] { 0.3f, 0.6f })
                    {
                        var o = p + dir * inner;
                        var from = new Vector3(o.x, y + h, o.y);
                        int n = Physics.RaycastNonAlloc(from, new Vector3(dir.x, 0f, dir.y), hits, bestD + 5f, ~0, QueryTriggerInteraction.Ignore);
                        float near = float.MaxValue; int bi = -1;
                        for (int i = 0; i < n; i++) if (hits[i].distance < near) { near = hits[i].distance; bi = i; }
                        line.Append(bi < 0 ? $" | ray {h:0.0}: clear" : $" | ray {h:0.0}: {hits[bi].collider.name} at {inner + near:0.0} m (y {hits[bi].point.y - y:+0.00;-0.00}, n {hits[bi].normal.x:0.00},{hits[bi].normal.y:0.00},{hits[bi].normal.z:0.00})");
                    }
                    sb.AppendLine(line.ToString());
                }
            }
            finally { Object.DestroyImmediate(go); }
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "median_profile.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[MedianProbe] wrote " + path);
        }
    }
}
