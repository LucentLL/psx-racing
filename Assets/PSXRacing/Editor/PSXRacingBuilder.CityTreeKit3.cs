using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S MESH TREES, ROUND 3 (trees2, 2026-11-15). Owner: "Trees
    /// don't seem accurate for Charlotte and are often too tall" - West Trade
    /// Street lined with tall thin columns standing over three-storey blocks.
    /// Round 2's street trees were poplars and beeches NARROWED to 0.42-0.62
    /// of their width and stretched up a long stem (the only way a crown
    /// passed a wall 2-4 m off), then scaled up to 1.6 until the crown
    /// cleared the lane: 13-20 m poles.
    ///
    /// Charlotte's trees (the city's own street tree inventory, 85,146 trees,
    /// USDA FS 2005: willow oak 16.7%, crape myrtle 14.1%, pears 8.7%, red
    /// maple 6.7%, dogwood 6.5%; 42% large species, 17% medium, 41% small):
    /// broad willow oaks over the old streets, round-headed young street
    /// trees Uptown, small ornamentals, loblolly and shortleaf pine with a
    /// long bare trunk. The forms made here from the owned models:
    ///
    ///   G3Street  a pruned street tree: the lower crown CUT at a share of the
    ///             leaves, the trunk stood plumb and drawn to the height that
    ///             puts the crown's base at the lane's clearance, the crown
    ///             kept WIDE and pressed lower (round, broad, small-high, and
    ///             two narrow ones for a walk under a wall) - the clearance
    ///             comes from the form, not from scaling the tree up
    ///   G3Avenue  the old neighbourhoods' willow oak: the broad Meadow
    ///             poplars and Big Oak limbed up to about a third, widened
    ///   G3Pine    the pine stand-ins with the crown in the top two fifths
    ///   G3Myrtle  the grey willows drawn up into a vase (crape myrtle)
    /// Appended after round 2's species (indices and roles of those unchanged:
    /// PSX_CT_ROUND3=0 draws exactly round 2's trees from this kit).
    /// PSX_CT_R3KIT=0: none of them.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        struct CT3Form
        {
            public string tag; public byte group; public CityTreeKit.Role role;
            /// <summary>Crown width and height factors, the crown's base as a share of the new height,
            /// the share of the leaves under the cut.</summary>
            public float w, cy, baseFrac, cutShare;
            public bool vase;
            public string[] from;
        }

        // (a base at k/16 + a hair: the fit reads the crown in sixteenths of the height)
        static readonly CT3Form[] CT3Forms =
        {
            new CT3Form { tag = "lr", group = CityTreeKit.G3Street, role = CityTreeKit.Role.Street, w = 1.0f, cy = 0.80f, baseFrac = 0.512f, cutShare = 0.30f,
                          from = new[] { "02_Poplar", "03_Poplar_skan", "beech_tree_02", "beech_tree_03" } },
            new CT3Form { tag = "lb", group = CityTreeKit.G3Street, role = CityTreeKit.Role.Street, w = 1.0f, cy = 0.85f, baseFrac = 0.45f, cutShare = 0.25f,
                          from = new[] { "03_Poplar_skan", "04_Poplar_skan", "beech_tree_03", "02_Poplar" } },
            new CT3Form { tag = "ls", group = CityTreeKit.G3Street, role = CityTreeKit.Role.Street, w = 1.0f, cy = 0.75f, baseFrac = 0.637f, cutShare = 0.35f,
                          from = new[] { "02_Poplar", "beech_tree_02", "01_Poplar", "beech_tree_01" } },
            new CT3Form { tag = "ln", group = CityTreeKit.G3Street, role = CityTreeKit.Role.Street, w = 0.6f, cy = 0.9f, baseFrac = 0.575f, cutShare = 0.30f,
                          from = new[] { "02_Poplar", "beech_tree_02" } },
            new CT3Form { tag = "lc", group = CityTreeKit.G3Street, role = CityTreeKit.Role.Street, w = 0.42f, cy = 1.0f, baseFrac = 0.575f, cutShare = 0.30f,
                          from = new[] { "beech_tree_01" } },
            new CT3Form { tag = "av", group = CityTreeKit.G3Avenue, role = CityTreeKit.Role.Street, w = 1.3f, cy = 1.0f, baseFrac = 0.325f, cutShare = 0.12f,
                          from = new[] { "beech_tree_06", "beech_tree_07", "beech_tree_08", "04_Poplar_skan", "05_Poplar_skan" } },
            new CT3Form { tag = "pine", group = CityTreeKit.G3Pine, role = CityTreeKit.Role.PineStandIn, w = 0.62f, cy = 0.8f, baseFrac = 0.575f, cutShare = 0.40f,
                          from = new[] { "03_Poplar_skan_pine", "05_Poplar_skan_pine" } },
            new CT3Form { tag = "vase", group = CityTreeKit.G3Myrtle, role = CityTreeKit.Role.Shrub, w = 0.95f, cy = 1.6f, vase = true,
                          from = new[] { "grey_willow_02", "grey_willow_03", "grey_willow_04" } },
        };

        /// <summary>Round 3's forms appended; <paramref name="groups"/> comes
        /// back one entry a species (0 for rounds 1-2).</summary>
        static void CTRound3(List<MeshForest.Variant> list, List<string> names, List<CityTreeKit.Role> roles, List<byte> groups)
        {
            groups.Clear();
            for (int i = 0; i < list.Count; i++) groups.Add(0);
            if (System.Environment.GetEnvironmentVariable("PSX_CT_R3KIT") == "0") { Log("City trees: round-3 forms OFF (PSX_CT_R3KIT=0)"); return; }
            // Big Oak stood on its own trunk - measured on the level that is DRAWN (round 2's ladder draws the
            // pack's coarsest level; the foot was taken from the first)
            for (int i = 0; i < list.Count; i++)
                if (roles[i] == CityTreeKit.Role.Oak) CT3CentreOnTrunk(list[i], names[i]);
            var sb = new System.Text.StringBuilder("City trees round 3 - CHARLOTTE FORMS (cut, stood plumb, crown kept wide):");
            foreach (var f in CT3Forms)
                foreach (var from in f.from)
                {
                    int i = names.IndexOf(from);
                    if (i < 0) { sb.Append("\n  (no " + from + ")"); continue; }
                    var src = list[i];
                    string nm = from + "_r3_" + f.tag;
                    var nv = Rework3(src, nm, f, out string how);
                    if (nv == null) { sb.Append("\n  (no form " + nm + ": " + how + ")"); continue; }
                    list.Add(nv); names.Add(nm); roles.Add(f.role); groups.Add(f.group);
                    sb.Append($"\n  {nm}: {src.height:0.0} -> {nv.height:0.0} m, tris {Tris(nv.lods[0])}/{Tris(nv.lods[1])}/{Tris(nv.lods[2])}, impostor {(nv.lods.Length > 3 && nv.lods[3] != null ? "yes" : "no")}; {how}");
                }
            Log(sb.ToString());
        }

        struct CT3Cut
        {
            public CT3Form f;
            public float cutY, B, h, trunkCut, flare, impK, stemK, limbKeep, limbZone;
            public Vector2[] axis;   // the trunk's line, a point every AxisStep up to the cut
            public Vector2 top, crownC;   // the trunk's line at the cut; the middle of the crown above it
        }
        const float CT3AxisStep = 0.5f;

        static Vector2 Axis3(in CT3Cut c, float y)
        {
            if (c.axis == null || c.axis.Length == 0) return Vector2.zero;
            float t = Mathf.Clamp(y / CT3AxisStep - 0.5f, 0f, c.axis.Length - 1);
            int i = Mathf.Min(c.axis.Length - 1, (int)t);
            int j = Mathf.Min(c.axis.Length - 1, i + 1);
            return Vector2.Lerp(c.axis[i], c.axis[j], t - i);
        }

        static bool Cut3(MeshForest.Variant src, CT3Form f, out CT3Cut c, out string how)
        {
            c = new CT3Cut { f = f, h = src.height };
            how = "";
            var m = src.lods[0];
            float h = src.height;
            if (f.vase) { c.cutY = 0f; c.B = 0f; how = "vase"; return true; }
            var leafY = new List<float>();
            var leaves = new List<Vector3>();
            var bark = new List<Vector3>();
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (!TreeKit.Read(m, s, out var verts, out var tris)) continue;
                bool leaf = src.leafSub != null && s < src.leafSub.Length && src.leafSub[s];
                foreach (int ti in tris)
                {
                    var q = src.pre.MultiplyPoint3x4(verts[ti]);
                    if (leaf) { leafY.Add(q.y); leaves.Add(q); } else bark.Add(q);
                }
            }
            if (leafY.Count < 10) { how = "no leaves"; return false; }
            leafY.Sort();
            float cutY = Mathf.Max(leafY[Mathf.Min(leafY.Count - 1, (int)(leafY.Count * f.cutShare))], 0.10f * h);
            // the trunk: the densest clump of bark at chest height (a model's low limbs and roots apart)
            if (!TrunkAtChest(bark, out var chest, out float trunkR))
            {
                var lowR = new List<float>();
                foreach (var q in bark) if (q.y > 0.3f && q.y < 0.12f * h) lowR.Add(Mathf.Sqrt(q.x * q.x + q.z * q.z));
                lowR.Sort();
                trunkR = lowR.Count > 0 ? lowR[lowR.Count / 2] : 0.2f;
                chest = Vector2.zero;
            }
            if (trunkR > 1.6f) { how = $"no single trunk (bark at chest height {trunkR:0.0} m wide)"; return false; }
            // a young street tree's stem is a hand and a half thick at least
            c.stemK = Mathf.Clamp(0.15f / Mathf.Max(0.02f, trunkR), 1f, 1.7f);
            c.trunkCut = Mathf.Max(0.3f, trunkR * 1.8f);
            c.flare = c.trunkCut * 0.6f;
            // THE TRUNK'S LINE: slice by slice from the foot, the bark within reach of the slice below
            int ns = Mathf.Max(1, Mathf.CeilToInt((cutY + 0.5f) / CT3AxisStep));
            var bySlice = new List<Vector3>[ns];
            foreach (var q in bark)
            {
                int i = Mathf.FloorToInt(q.y / CT3AxisStep);
                if (i < 0 || i >= ns) continue;
                (bySlice[i] ??= new List<Vector3>()).Add(q);
            }
            c.axis = new Vector2[ns];
            var prev = chest;
            float reach2 = c.trunkCut * c.trunkCut * 1.7f;
            for (int i = 0; i < ns; i++)
            {
                var acc = Vector2.zero; int n = 0;
                if (bySlice[i] != null)
                    foreach (var q in bySlice[i])
                    {
                        var d = new Vector2(q.x, q.z) - prev;
                        if (d.sqrMagnitude < reach2) { acc += new Vector2(q.x, q.z); n++; }
                    }
                c.axis[i] = n >= 3 ? acc / n : prev;
                prev = c.axis[i];
            }
            float crown = (h - cutY) * f.cy;
            float B = f.baseFrac / (1f - f.baseFrac) * crown;
            // (the stem no shorter than half what the model has under the cut, no longer than three times)
            B = Mathf.Clamp(B, 0.5f * cutY, 3f * cutY);
            c.cutY = cutY; c.B = B;
            c.top = prev;
            // the crown's middle: its leaves over the cut
            var cc = Vector2.zero; int cn = 0;
            foreach (var q in leaves) if (q.y >= cutY) { cc += new Vector2(q.x, q.z); cn++; }
            c.crownC = cn > 0 ? cc / cn : prev;
            // how far out a limb under the cut is kept (raised to the crown's base): half the crown's reach over the cut
            var lr = new List<float>();
            foreach (var q in leaves) if (q.y >= cutY) lr.Add(Vector2.Distance(new Vector2(q.x, q.z), c.crownC));
            lr.Sort();
            c.limbKeep = Mathf.Max(c.trunkCut * 2.5f, lr.Count > 0 ? 0.5f * lr[Mathf.Min(lr.Count - 1, (int)(lr.Count * 0.9f))] : 1.5f);
            // ... and only the limbs that leave the trunk within this far under the cut: one from lower down, raised,
            // is a sliver metres long up the stem (a pine form's whole lower crown: a trunk wrapped in shards)
            c.limbZone = Mathf.Clamp(0.2f * (h - cutY), 1.0f, 2.5f);
            how = $"cut at {cutY:0.0} m of {h:0.0} (stem {B:0.0} m, crown {crown:0.0} m), trunk r {trunkR:0.00} at ({chest.x:0.0}, {chest.y:0.0}) x{c.stemK:0.00}, lean at the cut {prev.magnitude:0.00} m, crown's middle {c.crownC.magnitude:0.00} m off the foot";
            return true;
        }

        /// <summary>The trunk at chest height: where the most BARK AREA stands
        /// between 0.8 and 1.8 m (the trunk's own skin is square metres; a low
        /// limb's twigs, however many their vertices, are not), its middle and
        /// its radius (the reach half that area lies within).
        /// <paramref name="bark"/>: triangle corners, three a triangle.</summary>
        static bool TrunkAtChest(List<Vector3> bark, out Vector2 centre, out float radius)
        {
            centre = Vector2.zero; radius = 0.2f;
            var cells = new Dictionary<long, float>();
            var cs = new List<Vector3>();   // (x, z, area) of each triangle at chest height
            long best = 0; float bestA = 0f;
            for (int i = 0; i + 2 < bark.Count; i += 3)
            {
                Vector3 p0 = bark[i], p1 = bark[i + 1], p2 = bark[i + 2];
                var c = (p0 + p1 + p2) / 3f;
                if (c.y < 0.8f || c.y > 1.8f) continue;
                float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                if (area <= 0f) continue;
                cs.Add(new Vector3(c.x, c.z, area));
                // (each triangle counted in the four half-metre cells round it: a trunk astride a cell line is one clump)
                for (int dz = 0; dz <= 1; dz++)
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        long k = ((long)(Mathf.FloorToInt(c.x / 0.5f - 0.5f) + dx) << 32) ^ (uint)(Mathf.FloorToInt(c.z / 0.5f - 0.5f) + dz);
                        cells.TryGetValue(k, out float n); n += area; cells[k] = n;
                        if (n > bestA) { bestA = n; best = k; }
                    }
            }
            if (cs.Count < 3 || bestA <= 0f) return false;
            var c0 = new Vector2(((int)(best >> 32) + 0.5f) * 0.5f, ((int)(uint)(best & 0xFFFFFFFFL) + 0.5f) * 0.5f);
            var near = new List<Vector2>();   // (distance, area)
            for (int it = 0; it < 3; it++)
            {
                var acc = Vector2.zero; float w = 0f;
                foreach (var q in cs)
                {
                    var p = new Vector2(q.x, q.y);
                    if ((p - c0).sqrMagnitude > 1.6f * 1.6f) continue;
                    acc += p * q.z; w += q.z;
                }
                if (w <= 0f) return false;
                c0 = acc / w;
            }
            float total = 0f;
            foreach (var q in cs)
            {
                float d = Vector2.Distance(new Vector2(q.x, q.y), c0);
                if (d > 1.6f) continue;
                near.Add(new Vector2(d, q.z)); total += q.z;
            }
            near.Sort((x, y) => x.x.CompareTo(y.x));
            float run = 0f; radius = 0.03f;
            foreach (var q in near) { run += q.y; if (run >= total * 0.5f) { radius = Mathf.Max(0.03f, q.x); break; } }
            centre = c0;
            return true;
        }

        /// <summary>BIG OAK STOOD ON ITS OWN TRUNK. The foot LoadVariant finds
        /// is the mean of the lowest bark of the first level it loads; the
        /// pack's oak sweeps limbs to the ground, and round 2's ladder draws
        /// its coarsest level - whose trunk (measured by bark area at chest
        /// height: 0.66 m radius) stands 1.4 m from that foot at scale 1,
        /// 0.65-0.95 m as drawn: beside its trunk collider, not on it. The
        /// drawn levels are moved so the trunk is on the foot (round 3's kit;
        /// PSX_CT_R3KIT=0: as it was).</summary>
        static void CT3CentreOnTrunk(MeshForest.Variant v, string name)
        {
            if (v == null || v.lods[0] == null || System.Environment.GetEnvironmentVariable("PSX_CT_R3KIT") == "0") return;
            var bark = new List<Vector3>();
            var m = v.lods[0];
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (v.leafSub != null && s < v.leafSub.Length && v.leafSub[s]) continue;
                if (!TreeKit.Read(m, s, out var verts, out var tris)) continue;
                foreach (int ti in tris) bark.Add(v.pre.MultiplyPoint3x4(verts[ti]));
            }
            if (!TrunkAtChest(bark, out var c, out float r)) { Log("City trees: " + name + ": no trunk found at chest height"); return; }
            if (c.magnitude < 0.35f) { Log($"City trees: {name} stands on its trunk ({c.magnitude:0.00} m off, r {r:0.00} m)"); return; }
            // (a trunk, not a limb: a hand's width for every two metres of height at least)
            if (r < 0.011f * v.height) { Log($"City trees: {name}: the bark found at chest height ({c.x:0.0}, {c.y:0.0}) is {r:0.00} m thick - not a {v.height:0.0} m tree's trunk; left where it stands"); return; }
            var tr = Matrix4x4.Translate(new Vector3(-c.x, 0f, -c.y));
            v.pre = tr * v.pre;   // (the far card stays on the foot, where the trunk now is)
            Log($"City trees: {name}'s foot moved {c.magnitude:0.00} m onto its trunk (r {r:0.00} m at chest height; it stood at ({c.x:0.0}, {c.y:0.0}) from the foot)");
        }

        /// <summary>Where a point of the model goes (foot frame in, foot frame out).</summary>
        static Vector3 Map3(in CT3Cut c, Vector3 q, bool impostorStem, out Vector2 kn)
        {
            if (c.f.vase)
            {
                float t = Mathf.Clamp01((q.y - 0.08f * c.h) / (0.57f * c.h));
                t = t * t * (3f - 2f * t);
                float k = Mathf.Lerp(0.42f, c.f.w, t);
                kn = new Vector2(k, c.f.cy);
                return new Vector3(q.x * k, q.y * c.f.cy, q.z * k);
            }
            // the crown's middle over the foot, the stem drawn from the foot to where it meets the crown
            // (an impostor's card is centred already)
            float x = q.x, z = q.z;
            if (c.impK > 0f) { }
            else if (q.y < c.cutY)
            {
                var a = Axis3(c, q.y);
                float dx = q.x - a.x, dz = q.z - a.y;
                float rr = Mathf.Sqrt(dx * dx + dz * dz);
                float up = 0f;
                if (q.y < 1.2f)
                {
                    if (rr > c.flare) { dx *= c.flare / rr; dz *= c.flare / rr; }
                }
                else
                {
                    // A LIMB UNDER THE CUT IS NOT SAWN OFF (a tube cut open shows its inside as shards from the
                    // road): what is off the trunk is RAISED to the crown's base, still joined to the trunk where
                    // it left it - the limbs sweep up the stem into the crown, as a street tree's are trained
                    up = Mathf.Clamp01((rr - 0.8f * c.trunkCut) / (1.2f * c.trunkCut));
                    up = up * up * (3f - 2f * up);
                    // (only within the zone under the cut: lower, what is kept is the trunk and its pruned stubs)
                    up *= Mathf.Clamp01((q.y - (c.cutY - c.limbZone - 0.5f)) / 0.5f);
                }
                float ySrc = Mathf.Lerp(q.y, c.cutY * (1f - 0.015f * (1f - q.y / Mathf.Max(0.05f, c.cutY))), up);
                if (up > 0f)
                {
                    // (measured from the trunk's line at the cut, so the limb meets its own run above the cut)
                    var a2 = Vector2.Lerp(a, c.top, up);
                    dx = q.x - a2.x; dz = q.z - a2.y;
                }
                float tk = Mathf.Clamp01((ySrc - (c.cutY - 1.5f)) / 1.5f);
                float ks = Mathf.Lerp(Mathf.Lerp(c.stemK, 1f, up), 1f, tk * tk * (3f - 2f * tk));
                var tl = (c.top - c.crownC) * (ySrc / Mathf.Max(0.05f, c.cutY));
                x = tl.x + dx * ks; z = tl.y + dz * ks;
                q.y = ySrc;
            }
            else { x = q.x - c.crownC.x; z = q.z - c.crownC.y; }
            float y, sy;
            if (q.y < c.cutY || impostorStem) { sy = c.B / Mathf.Max(0.05f, c.cutY); y = Mathf.Min(q.y, c.cutY) * sy; }
            else { sy = c.f.cy; y = c.B + (q.y - c.cutY) * c.f.cy; }
            float kk;
            if (impostorStem) kk = c.impK;
            else
            {
                float t = Mathf.Clamp01((y - (c.B - 1.5f)) / 2f);
                t = t * t * (3f - 2f * t);
                kk = Mathf.Lerp(1f, c.f.w, t);
            }
            kn = new Vector2(kk, sy);
            return new Vector3(x * kk, y, z * kk);
        }

        static MeshForest.Variant Rework3(MeshForest.Variant src, string name, CT3Form form, out string how)
        {
            if (!Cut3(src, form, out var cut, out how)) return null;
            var nv = new MeshForest.Variant
            {
                mats = (Material[])src.mats.Clone(), leafSub = (bool[])src.leafSub.Clone(),
                pre = Matrix4x4.identity, impostorPre = Matrix4x4.identity, shadows = src.shadows,
                leafDress = src.leafDress, impostorDress = src.impostorDress, thin = src.thin, maxDist = src.maxDist,
            };
            nv.lods = new Mesh[Mathf.Max(4, src.lods.Length)];
            for (int l = 0; l < src.lods.Length && l < 4; l++)
            {
                var m = src.lods[l];
                if (m == null) continue;
                if (l > 0 && l < 3 && m == src.lods[l - 1]) { nv.lods[l] = nv.lods[l - 1]; continue; }
                var c = cut;
                if (l == 3)
                {
                    // the card's lower part (the crown that was cut away) drawn in to a stem's width
                    float half = 0.01f;
                    for (int s = 0; s < m.subMeshCount; s++)
                        if (TreeKit.Read(m, s, out var iv, out _))
                            foreach (var q0 in iv) { var q = src.impostorPre.MultiplyPoint3x4(q0); half = Mathf.Max(half, Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.z))); }
                    c.impK = form.vase ? 0f : Mathf.Clamp(cut.trunkCut * 1.6f / half, 0.05f, 0.35f);
                }
                var outM = Rework3Mesh(m, l == 3 ? src.impostorPre : src.pre, src.leafSub, c, l == 3, "CT3_" + name + "_L" + l);
                if (outM != null) outM = WorldKit.SaveMesh(outM, outM.name);
                nv.lods[l] = outM;
            }
            if (nv.lods[0] == null) { how = "empty mesh"; return null; }
            for (int l = 1; l < 3; l++) if (nv.lods[l] == null) nv.lods[l] = nv.lods[l - 1];
            nv.height = Mathf.Max(0.3f, nv.lods[0].bounds.max.y);
            return nv;
        }

        static Mesh Rework3Mesh(Mesh src, Matrix4x4 P, bool[] leafSub, CT3Cut c, bool impostor, string name)
        {
            var nrm = ReadNormals(src); var uv = ReadUV(src);
            Color[] col = null;
            if (src.isReadable && src.colors != null && src.colors.Length == src.vertexCount) col = src.colors;
            var NP = P.inverse.transpose;
            var outV = new List<CTV>();
            var outT = new List<int>[src.subMeshCount];
            var tri = new CTV[3];
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
                for (int t = 0; t < tris.Length / 3; t++)
                {
                    for (int j = 0; j < 3; j++) tri[j] = V(tris[t * 3 + j]);
                    if (c.f.vase) { Emit3(tri, c, false, outV, outT[s]); continue; }
                    if (impostor)
                    {
                        foreach (var piece in SplitAt(tri, c.cutY))
                        {
                            float top = Mathf.Max(piece[0].p.y, Mathf.Max(piece[1].p.y, piece[2].p.y));
                            Emit3(piece, c, top <= c.cutY + 1e-4f, outV, outT[s]);
                        }
                        continue;
                    }
                    float lowY = Mathf.Min(tri[0].p.y, Mathf.Min(tri[1].p.y, tri[2].p.y));
                    if (lowY < c.cutY)
                    {
                        // under the cut: no leaf; the trunk, and each limb as far out as limbKeep (Map3 raises it
                        // to the crown's base) - its thin outer run, whose leaves are gone, goes
                        if (leaf) continue;
                        var ctr = (tri[0].p + tri[1].p + tri[2].p) / 3f;
                        var ax = Axis3(c, Mathf.Min(ctr.y, c.cutY));
                        float dx = ctr.x - ax.x, dz = ctr.z - ax.y;
                        float r2 = dx * dx + dz * dz;
                        if (r2 > c.limbKeep * c.limbKeep) continue;
                        // (lower than the zone: the trunk alone, its limbs pruned at the stem)
                        if (ctr.y < c.cutY - c.limbZone && r2 > c.trunkCut * c.trunkCut) continue;
                    }
                    Emit3(tri, c, false, outV, outT[s]);
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

        static void Emit3(CTV[] tri, in CT3Cut c, bool impostorStem, List<CTV> outV, List<int> outT)
        {
            for (int j = 0; j < 3; j++)
            {
                var v = tri[j];
                v.p = Map3(c, v.p, impostorStem, out var kn);
                v.n = new Vector3(v.n.x / Mathf.Max(0.05f, kn.x), v.n.y / Mathf.Max(0.05f, kn.y), v.n.z / Mathf.Max(0.05f, kn.x)).normalized;
                outT.Add(outV.Count);
                outV.Add(v);
            }
        }
    }
}
