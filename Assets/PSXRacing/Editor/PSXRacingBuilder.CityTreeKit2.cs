using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S MESH TREES, ROUND 2 (ctrees2, 2026-11-11): what the kit
    /// makes from the owned models beyond the packs' own trees.
    ///
    /// 1. STREET TREES (CityTreeKit.Role.Street). Round 1 had no tree narrow
    ///    enough for a grate 2-4 m from a facade, so 72% of Uptown's trees and
    ///    44% at N Graham were dropped. A real street tree is a pruned tree: a
    ///    clear trunk to 4-5 m (buses, box trucks, awnings), the crown narrow
    ///    (vase or column). Made here from the Meadow poplars and the Forest
    ///    beeches: the lowest branch rings CUT (bark branches and leaves below
    ///    the cut go, the trunk stays), the trunk drawn TALLER (everything
    ///    under the cut stretched, everything over it lifted), the crown's
    ///    width scaled apart from its height. The impostor is cut at the same
    ///    height and moved alike, so the far card still matches the mesh.
    ///
    /// 2. A SANE LOD LADDER. Each mesh level has a triangle budget (near /
    ///    mid / low = <see cref="CTBudget"/>): the Meadow poplars take the
    ///    first of their five FBX levels inside it, and any level still over
    ///    it (Big Oak 37.6k, Big Poplar 16.9k) is THINNED - leaf clusters
    ///    dropped by a hash, the kept ones grown about their own centre so the
    ///    crown keeps its cover; bark untouched.
    ///
    /// Both bake the levels into the tree's own frame (foot at the origin,
    /// pre = impostorPre = identity), so mesh and impostor share an origin.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        /// <summary>Triangle budget of the near, mid and low mesh levels.</summary>
        internal static readonly int[] CTBudget = { 6500, 3000, 1300 };
        internal static readonly int[] CTOakBudget = { 11000, 4500, 2000 };

        /// <summary>A street form: width of the crown (share), and where the
        /// crown starts afterwards (share of the new height).</summary>
        struct CTForm { public string tag; public float w, baseFrac, cutShare; }

        static readonly CTForm[] CTForms =
        {
            // (Uptown's grate trees: 10-14 m, limbed to 4.5-5.5 m over the walk = 0.42-0.47 of the height)
            new CTForm { tag = "vase", w = 0.62f, baseFrac = 0.46f, cutShare = 0.12f },
            new CTForm { tag = "column", w = 0.42f, baseFrac = 0.43f, cutShare = 0.16f },
        };

        /// <summary>The source models of the street trees (kit names).</summary>
        static readonly string[] CTStreetFrom = { "02_Poplar", "03_Poplar_skan", "07_Poplar_skan", "beech_tree_01", "beech_tree_02", "beech_tree_03" };

        /// <summary>The kit's species after the round-2 passes: the ladder on
        /// every tree, then the street forms appended.</summary>
        static void CTRound2(List<MeshForest.Variant> list, List<string> names, List<CityTreeKit.Role> roles)
        {
            if (System.Environment.GetEnvironmentVariable("PSX_CT_R2KIT") == "0") { Log("City trees: round-2 kit passes OFF (PSX_CT_R2KIT=0)"); return; }
            var sb = new System.Text.StringBuilder("City trees round 2 - LOD ladder (near/mid/low tris, budget " + CTBudget[0] + "/" + CTBudget[1] + "/" + CTBudget[2] + "):");
            var done = new Dictionary<MeshForest.Variant, MeshForest.Variant>();
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (roles[i] == CityTreeKit.Role.Shrub || roles[i] == CityTreeKit.Role.Small) continue;
                var keep = new float[3];
                bool any = false;
                // the hero oak (one willow oak in five, parks and yards) keeps more near the car
                var budget = roles[i] == CityTreeKit.Role.Oak ? CTOakBudget : CTBudget;
                for (int l = 0; l < 3; l++)
                {
                    keep[l] = 1f;
                    var m = v.lods[l];
                    if (m == null) continue;
                    int t = (int)Tris(m);
                    if (t <= budget[l]) continue;
                    int leaf = LeafTris(m, v.leafSub);
                    int bark = t - leaf;
                    keep[l] = leaf > 0 ? Mathf.Clamp((budget[l] - bark) / (float)leaf, 0.2f, 1f) : 1f;
                    sb.Append($"\n  {names[i]} L{l}: {t} tris = bark {bark} + leaves {leaf}");
                    if (keep[l] < 0.98f) any = true;
                }
                if (!any) continue;
                // clones (snag, pine stand-in) share their source's meshes: rework the meshes once
                var nv = ReworkVariant(v, names[i], keep, null);
                if (nv == null) continue;
                nv.leafDress = v.leafDress; nv.impostorDress = v.impostorDress;
                list[i] = nv;
                sb.Append($"\n  {names[i]}: {Tris(v.lods[0])}/{Tris(v.lods[1])}/{Tris(v.lods[2])} -> {Tris(nv.lods[0])}/{Tris(nv.lods[1])}/{Tris(nv.lods[2])} (leaves kept {keep[0]:0.00}/{keep[1]:0.00}/{keep[2]:0.00})");
            }
            Log(sb.ToString());

            // ---- the street forms ----
            var sb2 = new System.Text.StringBuilder("City trees round 2 - STREET TREES (cut, lifted, narrowed):");
            foreach (var from in CTStreetFrom)
            {
                int i = names.IndexOf(from);
                if (i < 0) { sb2.Append("\n  (no " + from + ")"); continue; }
                var src = list[i];
                foreach (var f in CTForms)
                {
                    var nv = ReworkVariant(src, from + "_street_" + f.tag, new[] { 1f, 1f, 1f }, f);
                    if (nv == null) continue;
                    nv.leafDress = src.leafDress; nv.impostorDress = src.impostorDress; nv.thin = src.thin; nv.maxDist = src.maxDist;
                    list.Add(nv); names.Add(from + "_street_" + f.tag); roles.Add(CityTreeKit.Role.Street);
                    sb2.Append($"\n  {from}_street_{f.tag}: {src.height:0.0} -> {nv.height:0.0} m, tris {Tris(nv.lods[0])}/{Tris(nv.lods[1])}/{Tris(nv.lods[2])}, impostor {(nv.lods.Length > 3 && nv.lods[3] != null ? "yes" : "no")}");
                }
            }
            Log(sb2.ToString());
        }

        static int LeafTris(Mesh m, bool[] leafSub)
        {
            int n = 0;
            for (int s = 0; s < m.subMeshCount; s++)
                if (leafSub != null && s < leafSub.Length && leafSub[s]) n += (int)(m.GetIndexCount(s) / 3);
            return n;
        }

        /// <summary>The street form's numbers for one model (its LOD0 in the
        /// foot frame): where to cut, how far to lift, the trunk's radius.</summary>
        struct CTCut { public float cutY, lift, trunkCut, h; public CTForm f; }

        static bool StreetCut(MeshForest.Variant src, CTForm f, out CTCut c)
        {
            c = default;
            var m = src.lods[0];
            float h = src.height;
            var leafY = new List<float>();
            var lowR = new List<float>();
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (!TreeKit.Read(m, s, out var verts, out var tris)) continue;
                bool leaf = src.leafSub != null && s < src.leafSub.Length && src.leafSub[s];
                foreach (int ti in tris)
                {
                    var q = src.pre.MultiplyPoint3x4(verts[ti]);
                    if (leaf) leafY.Add(q.y);
                    else if (q.y > 0.3f && q.y < 0.12f * h) lowR.Add(Mathf.Sqrt(q.x * q.x + q.z * q.z));
                }
            }
            if (leafY.Count < 10) return false;
            leafY.Sort();
            // the lowest rings: the cut where this share of the leaves lies under it
            float cutY = Mathf.Max(leafY[Mathf.Min(leafY.Count - 1, (int)(leafY.Count * f.cutShare))], 0.12f * h);
            lowR.Sort();
            float trunkR = lowR.Count > 0 ? lowR[lowR.Count / 2] : 0.2f;
            float lift = Mathf.Max(0f, (f.baseFrac * h - cutY) / (1f - f.baseFrac));
            c = new CTCut { cutY = cutY, lift = lift, trunkCut = Mathf.Max(0.3f, trunkR * 1.8f), h = h, f = f };
            return true;
        }

        static Vector3 StreetMap(in CTCut c, Vector3 q, out Vector2 kn)
        {
            // a tree in a grate: the root flare drawn in to the trunk (it stands a metre off the curb)
            float flare = c.trunkCut * 0.6f;
            if (q.y < 1.2f)
            {
                float rr = Mathf.Sqrt(q.x * q.x + q.z * q.z);
                if (rr > flare) { q.x *= flare / rr; q.z *= flare / rr; }
            }
            float sy = q.y < c.cutY ? (c.cutY + c.lift) / Mathf.Max(0.05f, c.cutY) : 1f;
            float y = q.y < c.cutY ? q.y * sy : q.y + c.lift;
            float b = c.cutY + c.lift;
            float t = Mathf.Clamp01((y - (b - 1.5f)) / 2f);
            t = t * t * (3f - 2f * t);
            float k = Mathf.Lerp(1f, c.f.w, t);
            kn = new Vector2(k, sy);
            return new Vector3(q.x * k, y, q.z * k);
        }

        static readonly Dictionary<string, Mesh> ctReworked = new Dictionary<string, Mesh>();

        /// <summary>A variant's levels baked into its foot frame: leaves
        /// thinned per level (<paramref name="keep"/>), and for a street form
        /// cut, lifted and narrowed (impostor included).</summary>
        static MeshForest.Variant ReworkVariant(MeshForest.Variant src, string name, float[] keep, CTForm? form)
        {
            CTCut cut = default;
            if (form.HasValue && !StreetCut(src, form.Value, out cut)) { Log("WARN: city trees: no street form of " + name); return null; }
            var nv = new MeshForest.Variant
            {
                mats = (Material[])src.mats.Clone(), leafSub = (bool[])src.leafSub.Clone(),
                pre = Matrix4x4.identity, impostorPre = Matrix4x4.identity,
                height = src.height + (form.HasValue ? cut.lift : 0f), shadows = src.shadows,
            };
            nv.lods = new Mesh[Mathf.Max(4, src.lods.Length)];
            for (int l = 0; l < src.lods.Length && l < 4; l++)
            {
                var m = src.lods[l];
                if (m == null) continue;
                // a level that shares its mesh with the one before shares the result too
                if (l > 0 && m == src.lods[l - 1] && (l == 3 || keep[l] == keep[l - 1])) { nv.lods[l] = nv.lods[l - 1]; continue; }
                var P = l == 3 ? src.impostorPre : src.pre;
                float k = l < 3 ? keep[l] : 1f;
                string key = name + "_L" + l + "_" + m.name + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m) + "_" + k.ToString("0.00");
                if (!ctReworked.TryGetValue(key, out var outM) || outM == null)
                {
                    outM = ReworkMesh(m, P, src.leafSub, k, form.HasValue ? cut : (CTCut?)null, l == 3, "CT2_" + name + "_L" + l);
                    if (outM != null) outM = WorldKit.SaveMesh(outM, outM.name);
                    ctReworked[key] = outM;
                }
                nv.lods[l] = outM;
            }
            if (nv.lods[0] == null) return null;
            for (int l = 1; l < 3; l++) if (nv.lods[l] == null) nv.lods[l] = nv.lods[l - 1];
            if (!form.HasValue) nv.height = src.height;
            else nv.height = Mathf.Max(nv.height, nv.lods[0].bounds.max.y);
            return nv;
        }

        struct CTV { public Vector3 p, n; public Vector2 uv; public Color c; }

        static CTV LerpV(CTV a, CTV b, float t) => new CTV
        {
            p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized, uv = Vector2.Lerp(a.uv, b.uv, t), c = Color.Lerp(a.c, b.c, t),
        };

        /// <summary>One level reworked (see ReworkVariant). Output in the foot frame.</summary>
        static Mesh ReworkMesh(Mesh src, Matrix4x4 P, bool[] leafSub, float keep, CTCut? cut, bool impostor, string name)
        {
            var nrm = ReadNormals(src); var uv = ReadUV(src);
            Color[] col = null;
            if (src.isReadable && src.colors != null && src.colors.Length == src.vertexCount) col = src.colors;
            var NP = P.inverse.transpose;
            var outV = new List<CTV>();
            var outT = new List<int>[src.subMeshCount];
            for (int s = 0; s < src.subMeshCount; s++)
            {
                outT[s] = new List<int>();
                if (!TreeKit.Read(src, s, out var verts, out var tris)) continue;
                bool leaf = !impostor && leafSub != null && s < leafSub.Length && leafSub[s];
                CTV V(int i) => new CTV
                {
                    p = P.MultiplyPoint3x4(verts[i]),
                    n = nrm != null && i < nrm.Length ? NP.MultiplyVector(nrm[i]).normalized : Vector3.up,
                    uv = uv != null && i < uv.Length ? uv[i] : Vector2.zero,
                    c = col != null && i < col.Length ? col[i] : Color.white,
                };
                // ---- leaf clusters (thinning): connected leaf triangles, by shared vertex ----
                int[] clusterOf = null; Vector3[] clusterC = null; bool[] clusterKeep = null;
                if (leaf && keep < 0.98f)
                {
                    int nt = tris.Length / 3;
                    var parent = new int[verts.Length];
                    for (int i = 0; i < parent.Length; i++) parent[i] = i;
                    int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                    for (int t = 0; t < nt; t++)
                    {
                        int a = Find(tris[t * 3]), b = Find(tris[t * 3 + 1]);
                        if (a != b) parent[b] = a;
                        a = Find(a); int c2 = Find(tris[t * 3 + 2]);
                        if (a != c2) parent[c2] = a;
                    }
                    var idOf = new Dictionary<int, int>();
                    clusterOf = new int[nt];
                    for (int t = 0; t < nt; t++)
                    {
                        int r = Find(tris[t * 3]);
                        if (!idOf.TryGetValue(r, out int id)) { id = idOf.Count; idOf[r] = id; }
                        clusterOf[t] = id;
                    }
                    // one great web of leaves: pairs of triangles (cards) instead
                    if (idOf.Count < 40) for (int t = 0; t < nt; t++) clusterOf[t] = t / 2;
                    int nc = 0; foreach (int c3 in clusterOf) nc = Mathf.Max(nc, c3 + 1);
                    clusterC = new Vector3[nc]; var cn = new int[nc]; clusterKeep = new bool[nc];
                    for (int t = 0; t < nt; t++)
                        for (int j = 0; j < 3; j++) { clusterC[clusterOf[t]] += P.MultiplyPoint3x4(verts[tris[t * 3 + j]]); cn[clusterOf[t]]++; }
                    for (int c4 = 0; c4 < nc; c4++)
                    {
                        if (cn[c4] > 0) clusterC[c4] /= cn[c4];
                        clusterKeep[c4] = CityTreesHash(c4, 7717) < keep;
                    }
                }
                float grow = keep < 0.98f ? Mathf.Min(1.6f, 1f / Mathf.Sqrt(Mathf.Max(0.2f, keep))) : 1f;
                var tri = new CTV[3];
                for (int t = 0; t < tris.Length / 3; t++)
                {
                    if (clusterOf != null && !clusterKeep[clusterOf[t]]) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        tri[j] = V(tris[t * 3 + j]);
                        if (clusterOf != null) tri[j].p = clusterC[clusterOf[t]] + (tri[j].p - clusterC[clusterOf[t]]) * grow;
                    }
                    if (cut.HasValue && !impostor)
                    {
                        var c = cut.Value;
                        var ctr = (tri[0].p + tri[1].p + tri[2].p) / 3f;
                        if (leaf && ctr.y < c.cutY) continue;
                        float my = Mathf.Max(tri[0].p.y, Mathf.Max(tri[1].p.y, tri[2].p.y));
                        if (!leaf && my < c.cutY && Mathf.Sqrt(ctr.x * ctr.x + ctr.z * ctr.z) > c.trunkCut) continue;
                    }
                    if (cut.HasValue && impostor)
                    {
                        // the card cut at the cut height (so it moves exactly as the mesh does)
                        foreach (var piece in SplitAt(tri, cut.Value.cutY)) Emit(piece, cut, outV, outT[s]);
                        continue;
                    }
                    Emit(tri, cut, outV, outT[s]);
                }
            }
            if (outV.Count == 0) return null;
            var mesh = new Mesh { name = name };
            mesh.indexFormat = outV.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            var pv = new List<Vector3>(outV.Count); var pn = new List<Vector3>(outV.Count); var pu = new List<Vector2>(outV.Count); var pc = new List<Color>(outV.Count);
            foreach (var v in outV) { pv.Add(v.p); pn.Add(v.n); pu.Add(v.uv); pc.Add(v.c); }
            mesh.SetVertices(pv); mesh.SetNormals(pn); mesh.SetUVs(0, pu);
            if (col != null) mesh.SetColors(pc);
            mesh.subMeshCount = outT.Length;
            for (int s = 0; s < outT.Length; s++) mesh.SetTriangles(outT[s], s, false);
            mesh.RecalculateBounds();
            if (impostor) mesh.RecalculateTangents();
            return mesh;
        }

        static void Emit(CTV[] tri, CTCut? cut, List<CTV> outV, List<int> outT)
        {
            for (int j = 0; j < 3; j++)
            {
                var v = tri[j];
                if (cut.HasValue)
                {
                    v.p = StreetMap(cut.Value, v.p, out var kn);
                    v.n = new Vector3(v.n.x / kn.x, v.n.y / kn.y, v.n.z / kn.x).normalized;
                }
                outT.Add(outV.Count);
                outV.Add(v);
            }
        }

        /// <summary>A triangle cut by the plane y = <paramref name="y"/>: the
        /// pieces under and over it (1 to 3 triangles).</summary>
        static IEnumerable<CTV[]> SplitAt(CTV[] t, float y)
        {
            int below = 0;
            for (int j = 0; j < 3; j++) if (t[j].p.y < y) below++;
            if (below == 0 || below == 3) { yield return new[] { t[0], t[1], t[2] }; yield break; }
            // the lone vertex (on its own side), then the other two in winding order
            int lone = -1;
            for (int j = 0; j < 3; j++)
            {
                bool b = t[j].p.y < y;
                if ((below == 1 && b) || (below == 2 && !b)) lone = j;
            }
            var a = t[lone]; var b1 = t[(lone + 1) % 3]; var c1 = t[(lone + 2) % 3];
            float tb = (y - a.p.y) / (b1.p.y - a.p.y), tc = (y - a.p.y) / (c1.p.y - a.p.y);
            var ab = LerpV(a, b1, tb); var ac = LerpV(a, c1, tc);
            ab.p.y = y; ac.p.y = y;
            yield return new[] { a, ab, ac };
            yield return new[] { ab, b1, c1 };
            yield return new[] { ab, c1, ac };
        }

        static float CityTreesHash(int i, int salt) => PSXRacing.City.CityTrees.Hash01(i, i * 7 + 3, salt);
    }
}
