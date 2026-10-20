using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE DOOR MIRRORS, MEASURED (owner, 2026-10-19: "I should see the
    /// mirrors of cars from cockpit windows", "door mirrors should be
    /// functional like rear-view mirror"). Every shell's mirrors are small
    /// separate pieces of its body mesh - a housing, often an arm, sometimes
    /// the glass as its own quad - just behind the windscreen's base, out at
    /// the sides near the belt line. Per shell:
    ///   the islands of the welded mesh (1 mm) small enough to be a mirror
    ///   (under 45 cm every way), out at the side (|x| over 0.45 m), between
    ///   the cowl's height less 40 cm and the roof, and from a metre behind
    ///   the cowl to 20 cm ahead of it;
    ///   per side the biggest of them, with every other within 8 cm of it
    ///   (the arm, the glass), out at the car's side (its outer edge at 85%
    ///   of the body's half-width or more: a police car's light bar is not a
    ///   mirror);
    ///   the GLASS: the rearward faces (normal z under -0.4) within 50
    ///   degrees of their own mean normal, the biggest patch of them that
    ///   hangs together (<see cref="GlassPatches"/>) - the shell's OWN
    ///   triangles, so the picture lies on a flat glass, an angled one and
    ///   the '69 Charger's domed bullet alike - each vertex stood 5 mm out
    ///   along their normal;
    ///   its frame (centre on the rearmost point, half-vectors across and up
    ///   the plane) maps the picture onto them.
    /// Logged to Screenshots/DoorMirrors/probe.txt in the car's frame (z from
    /// the cowl); with PSX_DOORMIRROR_EMIT=1 written, in the MESH's frame, to
    /// Scripts/DoorMirrors.Table.cs (a build cannot read the meshes).
    /// </summary>
    public static class DoorMirrorProbe
    {
        const float MaxPiece = 0.45f, MinOut = 0.45f, Merge = 0.08f, OutAtSide = 0.85f;
        const float RearNz = -0.4f, GlassCone = 50f, Proud = 0.005f;

        struct Island { public List<int> tris; public Vector3 min, max; public float area; }

        public struct Found
        {
            public Vector3 centre, across, up, normal, min, max;   // mesh frame
            public Vector3[] tris;                                   // mesh frame, stood out
            public Vector3 rigCentre, rigMin, rigMax;                // car frame
            public float w, h;
            public int pieces, faceTris;
        }

        [MenuItem("PSX Racing/Door Mirror Probe")]
        public static void Run()
        {
            var log = new StringBuilder();
            var rows = new StringBuilder();
            var defs = new List<CarModelDef>();
            foreach (var go in Resources.LoadAll<GameObject>("CarModels"))
            {
                var d = go != null ? go.GetComponent<CarModelDef>() : null;
                if (d != null && d.bodyMesh != null) defs.Add(d);
            }
            defs.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            int two = 0, one = 0, none = 0;
            foreach (var d in defs)
            {
                var found = Measure(d, log);
                if (found.Count == 2) two++; else if (found.Count == 1) one++; else none++;
                if (found.Count == 0) continue;
                rows.Append("            new Entry(\"").Append(d.key).Append('"');
                foreach (var f in found)
                {
                    Floats(rows, new[] { f.centre, f.across, f.up, f.normal, f.min, f.max });
                    Floats(rows, f.tris);
                }
                rows.Append("),\n");
            }
            log.Insert(0, $"[DoorMirror] {defs.Count} shells: {two} with two mirrors, {one} with one, {none} with none\n");
            string root = Path.GetDirectoryName(Application.dataPath);
            string dir = Path.Combine(root, "Screenshots", "DoorMirrors");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "probe.txt"), log.ToString());
            Debug.Log(log.ToString());
            if (System.Environment.GetEnvironmentVariable("PSX_DOORMIRROR_EMIT") == "1")
            {
                string path = Path.Combine(Application.dataPath, "PSXRacing", "Scripts", "DoorMirrors.Table.cs");
                File.WriteAllText(path, Header + rows + Footer);
                Debug.Log("[DoorMirror] table written to " + path);
            }
        }

        static void Floats(StringBuilder sb, Vector3[] vs)
        {
            sb.Append(",\n                new[] { ");
            for (int i = 0; i < vs.Length; i++)
                foreach (float x in new[] { vs[i].x, vs[i].y, vs[i].z })
                    sb.Append(x.ToString("0.0000", CultureInfo.InvariantCulture)).Append("f, ");
            sb.Length -= 2;
            sb.Append(" }");
        }

        const string Header =
"// <auto-generated>\n" +
"// Written by PSX Racing/Door Mirror Probe (PSX_DOORMIRROR_EMIT=1) from the\n" +
"// body meshes. Do not edit by hand: re-run the probe after a shell is\n" +
"// re-baked or added. See DoorMirrors.cs.\n" +
"// </auto-generated>\n" +
"namespace PSXRacing\n" +
"{\n" +
"    public static partial class DoorMirrors\n" +
"    {\n" +
"        // key; per mirror two arrays in the body mesh's frame: the glass's\n" +
"        // frame (centre, half-vector across, half-vector up, normal toward\n" +
"        // the driver, then the min and max corners of every piece of the\n" +
"        // mirror), and the glass's triangles (9 floats each, stood out).\n" +
"        static readonly Entry[] Table =\n" +
"        {\n";
        const string Footer =
"        };\n" +
"    }\n" +
"}\n";

        /// <summary>The shell's mirrors, left (car -x) first.</summary>
        public static List<Found> Measure(CarModelDef d, StringBuilder log)
        {
            var result = new List<Found>();
            var mesh = d.bodyMesh;
            var mv = mesh.vertices;
            var tris = mesh.triangles;
            var rot = Quaternion.Euler(0f, d.bodyYaw, 0f);
            var off = new Vector3(0f, d.bodyYOffset, d.bodyZOffset);
            var inv = Quaternion.Inverse(rot);
            var P = new Vector3[mv.Length];
            for (int i = 0; i < mv.Length; i++) P[i] = rot * mv[i] + off;

            // Weld (1 mm) and join by triangles.
            var weld = new int[P.Length];
            var key = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < P.Length; i++)
            {
                var k = Vector3Int.RoundToInt(P[i] * 1000f);
                if (!key.TryGetValue(k, out int id)) { id = key.Count; key[k] = id; }
                weld[i] = id;
            }
            var parent = new int[key.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = Find(weld[tris[t]]), b = Find(weld[tris[t + 1]]), c = Find(weld[tris[t + 2]]);
                parent[b] = a; parent[Find(c)] = a;
            }
            var islands = new Dictionary<int, Island>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int r = Find(weld[tris[t]]);
                if (!islands.TryGetValue(r, out var isl))
                    isl = new Island { tris = new List<int>(), min = Vector3.positiveInfinity, max = Vector3.negativeInfinity };
                isl.tris.Add(t);
                for (int k = 0; k < 3; k++) { isl.min = Vector3.Min(isl.min, P[tris[t + k]]); isl.max = Vector3.Max(isl.max, P[tris[t + k]]); }
                isl.area += Vector3.Cross(P[tris[t + 1]] - P[tris[t]], P[tris[t + 2]] - P[tris[t]]).magnitude * 0.5f;
                islands[r] = isl;
            }

            var cand = new List<Island>();
            foreach (var isl in islands.Values)
            {
                Vector3 size = isl.max - isl.min, ctr = (isl.max + isl.min) * 0.5f;
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) > MaxPiece) continue;
                if (Mathf.Abs(ctr.x) < MinOut) continue;
                if (ctr.y < d.cowlY - 0.40f || ctr.y > d.roofY - 0.05f) continue;
                if (ctr.z < d.cowlZ - 1.0f || ctr.z > d.cowlZ + 0.2f) continue;
                cand.Add(isl);
            }

            float halfW = 0f;
            foreach (var p in P) halfW = Mathf.Max(halfW, Mathf.Abs(p.x));
            log.Append($"{d.key,-18} cowl z {d.cowlZ:0.00} y {d.cowlY:0.00} roof {d.roofY:0.00} half-width {halfW:0.00} islands {islands.Count} candidates {cand.Count}\n");
            foreach (float side in new[] { -1f, 1f })
            {
                Island main = default; bool have = false;
                foreach (var isl in cand)
                    if (Mathf.Sign((isl.min.x + isl.max.x) * 0.5f) == side && (!have || isl.area > main.area)) { main = isl; have = true; }
                if (!have) { log.Append($"   {(side < 0 ? "L" : "R")}: none\n"); continue; }
                var parts = new List<Island> { main };
                foreach (var isl in cand)
                {
                    if (ReferenceEquals(isl.tris, main.tris) || Mathf.Sign((isl.min.x + isl.max.x) * 0.5f) != side) continue;
                    Vector3 gap = Vector3.Max(Vector3.Max(main.min - isl.max, isl.min - main.max), Vector3.zero);
                    if (gap.x <= Merge && gap.y <= Merge && gap.z <= Merge) parts.Add(isl);
                }
                Vector3 amin = Vector3.positiveInfinity, amax = Vector3.negativeInfinity;
                foreach (var isl in parts) { amin = Vector3.Min(amin, isl.min); amax = Vector3.Max(amax, isl.max); }
                float outer = Mathf.Max(Mathf.Abs(amin.x), Mathf.Abs(amax.x));
                if (outer < OutAtSide * halfW) { log.Append($"   {(side < 0 ? "L" : "R")}: not out at the side ({outer:0.00} of {halfW:0.00})\n"); continue; }
                var rear = new List<int>();
                foreach (var isl in parts)
                {
                    foreach (int t in isl.tris)
                    {
                        var n = Vector3.Cross(P[tris[t + 1]] - P[tris[t]], P[tris[t + 2]] - P[tris[t]]);
                        if (n.sqrMagnitude < 1e-12f) continue;
                        if (n.normalized.z < RearNz) rear.Add(t);
                    }
                }
                if (rear.Count == 0) { log.Append($"   {(side < 0 ? "L" : "R")}: no rearward face ({parts.Count} pieces)\n"); continue; }

                Vector3 N = Normal(rear, P, tris, out _);
                float cone = Mathf.Cos(GlassCone * Mathf.Deg2Rad);
                var near = new List<int>();
                foreach (int t in rear)
                {
                    var n = Vector3.Cross(P[tris[t + 1]] - P[tris[t]], P[tris[t + 2]] - P[tris[t]]).normalized;
                    if (Vector3.Dot(n, N) >= cone) near.Add(t);
                }
                var face = GlassPatches(near, weld, P, tris, N);
                N = Normal(face, P, tris, out Vector3 C);
                Vector3 A = Vector3.Cross(Vector3.up, N);
                if (A.sqrMagnitude < 1e-6f) { log.Append($"   {(side < 0 ? "L" : "R")}: glass faces up/down\n"); continue; }
                A.Normalize();
                Vector3 B = Vector3.Cross(N, A).normalized;
                if (B.y < 0f) B = -B;
                float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue, n1 = float.MinValue;
                foreach (int t in face)
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 r = P[tris[t + k]] - C;
                        float a = Vector3.Dot(r, A), b = Vector3.Dot(r, B);
                        a0 = Mathf.Min(a0, a); a1 = Mathf.Max(a1, a); b0 = Mathf.Min(b0, b); b1 = Mathf.Max(b1, b);
                        n1 = Mathf.Max(n1, Vector3.Dot(r, N));
                    }
                float ha = (a1 - a0) * 0.5f, hb = (b1 - b0) * 0.5f;
                if (ha < 0.025f || hb < 0.015f) { log.Append($"   {(side < 0 ? "L" : "R")}: glass too small {ha * 200f:0.0}x{hb * 200f:0.0} cm\n"); continue; }
                // The frame's centre on the rearmost point's plane; the
                // picture maps across and up it, whatever the glass's depth.
                Vector3 F = C + A * ((a0 + a1) * 0.5f) + B * ((b0 + b1) * 0.5f) + N * (n1 + Proud);
                var glass = new Vector3[face.Count * 3];
                for (int i = 0; i < face.Count; i++)
                    for (int k = 0; k < 3; k++)
                        glass[i * 3 + k] = inv * (P[tris[face[i] + k]] + N * Proud - off);

                var f = new Found
                {
                    rigCentre = F, rigMin = amin, rigMax = amax, w = ha * 2f, h = hb * 2f,
                    pieces = parts.Count, faceTris = face.Count,
                    centre = inv * (F - off), across = inv * (A * ha), up = inv * (B * hb), normal = inv * N,
                    tris = glass,
                };
                Vector3 m0 = inv * (amin - off), m1 = inv * (amax - off);
                f.min = Vector3.Min(m0, m1); f.max = Vector3.Max(m0, m1);
                result.Add(f);
                log.Append($"   {(side < 0 ? "L" : "R")}: glass dz {F.z - d.cowlZ:+0.00;-0.00} x {F.x:+0.000;-0.000} y {F.y:0.000} " +
                           $"{f.w * 100f:0.0}x{f.h * 100f:0.0} cm n ({N.x:+0.00;-0.00},{N.y:+0.00;-0.00},{N.z:+0.00;-0.00}) " +
                           $"pieces {parts.Count} faceTris {face.Count} box x {amin.x:+0.00;-0.00}..{amax.x:+0.00;-0.00} " +
                           $"y {amin.y:0.00}..{amax.y:0.00} dz {amin.z - d.cowlZ:+0.00;-0.00}..{amax.z - d.cowlZ:+0.00;-0.00}\n");
            }
            return result;
        }

        /// <summary>The glass among the mirror's rearward faces: they join
        /// into patches (sharing a welded corner); the biggest patch is the
        /// glass, with any other of 20 cm2 or more standing level with it or
        /// proud of it (a separate glass quad on its housing's back). A
        /// stalk's tapered faces 12 cm further forward (the Civic's) and a
        /// mount's square centimetre further back (the Charger's) are not.</summary>
        static List<int> GlassPatches(List<int> near, int[] weld, Vector3[] P, int[] tris, Vector3 N)
        {
            var comp = new int[near.Count];
            for (int i = 0; i < comp.Length; i++) comp[i] = -1;
            int nc = 0;
            for (int s = 0; s < near.Count; s++)
            {
                if (comp[s] >= 0) continue;
                comp[s] = nc;
                var open = new Stack<int>();
                open.Push(s);
                while (open.Count > 0)
                {
                    int i = open.Pop(), ti = near[i];
                    for (int j = 0; j < near.Count; j++)
                    {
                        if (comp[j] >= 0) continue;
                        int tj = near[j];
                        bool shares = false;
                        for (int a = 0; a < 3 && !shares; a++)
                            for (int b = 0; b < 3; b++)
                                if (weld[tris[ti + a]] == weld[tris[tj + b]]) { shares = true; break; }
                        if (shares) { comp[j] = nc; open.Push(j); }
                    }
                }
                nc++;
            }
            var area = new float[nc];
            var rearmost = new float[nc];
            for (int c = 0; c < nc; c++) rearmost[c] = float.MinValue;
            for (int i = 0; i < near.Count; i++)
            {
                int t = near[i];
                area[comp[i]] += Vector3.Cross(P[tris[t + 1]] - P[tris[t]], P[tris[t + 2]] - P[tris[t]]).magnitude * 0.5f;
                for (int k = 0; k < 3; k++) rearmost[comp[i]] = Mathf.Max(rearmost[comp[i]], Vector3.Dot(P[tris[t + k]], N));
            }
            int prim = 0;
            for (int c = 1; c < nc; c++) if (area[c] > area[prim]) prim = c;
            var face = new List<int>();
            for (int i = 0; i < near.Count; i++)
            {
                int c = comp[i];
                if (c == prim || (area[c] >= 0.002f && rearmost[c] >= rearmost[prim] - 0.01f)) face.Add(near[i]);
            }
            return face;
        }

        static Vector3 Normal(List<int> sel, Vector3[] P, int[] tris, out Vector3 centroid)
        {
            Vector3 n = Vector3.zero, c = Vector3.zero;
            float area = 0f;
            foreach (int t in sel)
            {
                var cr = Vector3.Cross(P[tris[t + 1]] - P[tris[t]], P[tris[t + 2]] - P[tris[t]]);
                float a = cr.magnitude * 0.5f;
                n += cr * 0.5f;
                c += (P[tris[t]] + P[tris[t + 1]] + P[tris[t + 2]]) / 3f * a;
                area += a;
            }
            centroid = area > 0f ? c / area : Vector3.zero;
            return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.back;
        }
    }
}
