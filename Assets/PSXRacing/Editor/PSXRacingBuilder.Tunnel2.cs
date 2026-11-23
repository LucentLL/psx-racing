using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE PARKWAY TUNNEL (owner 2026-11-23, after PC59: "The tunnel lights,
    /// shape of the tunnel and texture of blue ridge parkway tunnels (inside or
    /// outside) still do not match", beside his picture of a Parkway portal in
    /// fall rain). PSX_TUNNEL2=0 = the tunnels of c96b5178 (a square tube under
    /// a 36 x 16 m rock face, an elliptical dressed-stone ring).
    ///
    /// The bore is a SEMICIRCULAR arch of the full section: radius = the
    /// tube's half-width (the tarmac's half plus TunnelWallOut, 4.3 m on the
    /// Parkway's 6.4 m road), springing <see cref="T2SpringY"/> over the road,
    /// so the crown stands 5.9 m (19.4 ft) over the centre line and 4.5 m
    /// (14.7 ft) over the tarmac edge - the Parkway's posted tunnel clearances
    /// are of that order. A concrete footing 0.9 m high runs the whole length;
    /// the first <see cref="T2LinedM"/> from each portal is lined in smooth
    /// cast concrete with faint board marks (his picture); the rest is the
    /// rough, unlined, blasted rock of his 11-02 drive, pushed OUT from the
    /// lining's line by a world-space noise (never into the road side of the
    /// wall solid, which is unchanged).
    ///
    /// The portals are coursed rough-cut grey stone as geometry: a ring of
    /// long radiating voussoirs carried down the jambs to the road, a face of
    /// mixed-size blocks in courses with recessed dark joints, each block its
    /// own tint (the vertex colour, PSX/Lit _HouseTint) under the photoscanned
    /// rock (PSX_ROCKFACE) for grain, moss on what faces up, and a level
    /// coping; wing walls step down either side. Over each bore the hill is
    /// closed by a cover of the ground's own material laid on the ground mesh's
    /// surface (raycast) or, over the hole the tube cuts in it, the DEM -
    /// never lower than the masonry's top near the portal.
    ///
    /// One row of amber wall lamps along the left wall (forward), 2.7 m up,
    /// every <see cref="T2LampPitchM"/>: fixtures with a lit lens here, and
    /// their light made at runtime from <see cref="TunnelLampRow"/> by
    /// TunnelLamps, at every hour.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        static bool Tunnel2 => System.Environment.GetEnvironmentVariable("PSX_TUNNEL2") != "0";

        /// <summary>The arch's springing over the road's centre line.</summary>
        const float T2SpringY = 1.6f;
        /// <summary>Top of the concrete footing over the road.</summary>
        const float T2FootTopY = 0.9f;
        /// <summary>Metres of smooth cast lining from each portal.</summary>
        const float T2LinedM = 14f;
        /// <summary>The voussoir ring's depth (radial) and how proud of the
        /// face it stands.</summary>
        const float T2RingW = 0.82f, T2RingProud = 0.10f;
        /// <summary>Headwall: how far past the ring each side, and how high
        /// over the ring's crown to the coping's top.</summary>
        const float T2WallPastRing = 4.4f, T2WallOverRing = 1.55f;
        /// <summary>Wing walls past the headwall each side, in steps.</summary>
        const float T2WingM = 4.5f;
        const float T2LampPitchM = 9f, T2LampY = 2.7f;
        /// <summary>How dark the bore's ambient gets deep inside (its share of
        /// the portal's), and over how many metres from a portal.</summary>
        const float T2DeepAmb = 0.10f, T2DarkenM = 26f;

        static int t2Tris, t2Stones;

        static float T2Radius(float halfW) => halfW;
        static float T2CrownY(float halfW) => T2SpringY + halfW;
        static float T2WallTopY(float halfW) => T2CrownY(halfW) + T2RingW + T2WallPastRingTop;
        const float T2WallPastRingTop = T2WallOverRing;
        static float T2WallX(float halfW) => halfW + T2RingW + T2WallPastRing;

        // ------------------------------------------------------------------
        //  Noise
        // ------------------------------------------------------------------
        static float T2Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffffff) / 16777215f;
            }
        }

        static float T2Value(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float L(int a, int b, int c) => T2Hash(x + a, y + b, z + c);
            float x00 = Mathf.Lerp(L(0, 0, 0), L(1, 0, 0), fx), x10 = Mathf.Lerp(L(0, 1, 0), L(1, 1, 0), fx);
            float x01 = Mathf.Lerp(L(0, 0, 1), L(1, 0, 1), fx), x11 = Mathf.Lerp(L(0, 1, 1), L(1, 1, 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        /// <summary>Blasted rock: 0..1, blocky (a ridged second octave).</summary>
        static float T2Rock(Vector3 p)
        {
            float a = T2Value(p / 1.7f);
            float b = 1f - Mathf.Abs(T2Value(p / 0.62f + new Vector3(17.3f, 5.1f, 9.7f)) * 2f - 1f);
            float c = T2Value(p / 0.27f + new Vector3(3.3f, 41.7f, 2.9f));
            return Mathf.Clamp01(0.55f * a + 0.33f * b + 0.12f * c);
        }

        // ------------------------------------------------------------------
        //  Materials
        // ------------------------------------------------------------------
        static Material T2Mat(string name, string tex, Color tint, bool rock, Vector4 rockParams, bool vertexTint = true)
        {
            var m = MakeMat(MeshPrefix + name, tex, affine: 0f, tint: tint);
            if (rock && MountainStage) T2RockFace(m, rockParams);
            if (m.HasProperty("_HouseTint")) m.SetFloat("_HouseTint", vertexTint ? 1f : 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The cut's photoscanned rock (SetRockFace) at a repeat of
        /// our own: x 1 / fine (m), y 1 / coarse (m), z normal, w moss.</summary>
        static void T2RockFace(Material m, Vector4 p)
        {
            SetRockFace(m);
            if (m.IsKeywordEnabled("PSX_ROCKFACE")) m.SetVector("_RockParams", p);
        }

        // ------------------------------------------------------------------
        //  The bore
        // ------------------------------------------------------------------
        /// <summary>The arch tube from portal to portal: the footing and the
        /// lined ends in concrete, the middle in rock; each its own mesh.
        /// Faces turned in. Vertex colour = the ambient's share x 0.5 (PSX/Lit
        /// _HouseTint doubles it): 1 at a portal, <see cref="T2DeepAmb"/> deep in.</summary>
        static void BuildBore2(List<Vector3> pts, int from, int stations, Transform parent, Material rockMat, Material liningMat, int no, float halfW)
        {
            int n = pts.Count;
            const int Sub = 4;                       // 1 m rings
            // (tunnel2) a short lined mouth, then the rock
            float LinedM = Tunnel3 ? 5f : T2LinedM;
            float R = T2Radius(halfW);
            int rings = (stations - 1) * Sub + 1;
            float total = (stations - 1) * Spacing;
            const int ArchN = 26;

            var cV = new List<Vector3>(); var cUV = new List<Vector2>(); var cC = new List<Color>(); var cT = new List<int>();
            var rV = new List<Vector3>(); var rUV = new List<Vector2>(); var rC = new List<Color>(); var rT = new List<int>();

            // ring frames
            var cen = new Vector3[rings]; var rgt = new Vector3[rings]; var bankL = new float[rings]; var bankR = new float[rings];
            for (int k = 0; k < rings; k++)
            {
                int s = Mathf.Min(k / Sub, stations - 2); float t = (k - s * Sub) / (float)Sub;
                int i0 = WrapIdx(from + s, n), i1 = WrapIdx(from + s + 1, n);
                cen[k] = Vector3.Lerp(pts[i0], pts[i1], t);
                rgt[k] = Vector3.Lerp(RightAt(pts, i0), RightAt(pts, i1), t).normalized;
                bankL[k] = Mathf.Min(0f, BankEdgeAt(i0, i1, t, -1f));
                bankR[k] = Mathf.Min(0f, BankEdgeAt(i0, i1, t, 1f));
            }
            float Amb(float dist)
            {
                float sp = Mathf.Min(dist, total - dist);
                return Mathf.Lerp(1f, T2DeepAmb, Mathf.SmoothStep(0f, 1f, sp / T2DarkenM));
            }

            // ---- the footing: both sides, the whole length (concrete) ----
            foreach (float side in new[] { -1f, 1f })
            {
                int prev = -1;
                for (int k = 0; k < rings; k++)
                {
                    float dist = k * Spacing / Sub;
                    Vector3 c = cen[k], r = rgt[k];
                    float foot = c.y - 0.45f + (side < 0f ? bankL[k] : bankR[k]);
                    Vector3 w = c + r * (side * R);
                    int b = cV.Count;
                    float a = Amb(dist) * 0.5f * 0.9f;
                    cV.Add(new Vector3(w.x, foot, w.z)); cV.Add(new Vector3(w.x, c.y + T2FootTopY, w.z));
                    cUV.Add(new Vector2(dist / 2.4f, 0f)); cUV.Add(new Vector2(dist / 2.4f, (T2FootTopY + 0.45f) / 2.4f));
                    cC.Add(new Color(a * 0.82f, a * 0.82f, a * 0.80f, 1f)); cC.Add(new Color(a, a, a * 0.98f, 1f));
                    if (prev >= 0) QuadFacing(cV, cT, prev, prev + 1, b + 1, b, -r * side);
                    prev = b;
                }
            }

            // ---- the arch profile from the footing's top, left to right ----
            // (x across, y over the road's centre, and the inward normal)
            var prof = new List<Vector2>();
            prof.Add(new Vector2(-R, T2FootTopY));
            prof.Add(new Vector2(-R, (T2FootTopY + T2SpringY) * 0.5f));
            for (int j = 0; j <= ArchN; j++)
            {
                float th = Mathf.PI * (1f - j / (float)ArchN);
                prof.Add(new Vector2(R * Mathf.Cos(th), T2SpringY + R * Mathf.Sin(th)));
            }
            prof.Add(new Vector2(R, (T2FootTopY + T2SpringY) * 0.5f));
            prof.Add(new Vector2(R, T2FootTopY));
            int P = prof.Count;
            var arc = new float[P];
            for (int j = 1; j < P; j++) arc[j] = arc[j - 1] + (prof[j] - prof[j - 1]).magnitude;
            Vector2 Inward(Vector2 q) => q.y <= T2SpringY + 1e-3f ? new Vector2(-Mathf.Sign(q.x), 0f) : (new Vector2(0f, T2SpringY) - q).normalized;

            // LINED: the first and last T2LinedM, in 1.2 m boards along the
            // bore (each board its own vertices: its own shade, the form line
            // between two boards a change of shade).
            float boardM = 1.2f;
            foreach (bool far in new[] { false, true })
            {
                float d0 = far ? total - LinedM : 0f, d1 = far ? total : LinedM;
                for (float bd = d0; bd < d1 - 1e-3f; bd += boardM)
                {
                    float be = Mathf.Min(bd + boardM, d1);
                    float shade = 0.92f + 0.12f * T2Hash(no, Mathf.RoundToInt(bd * 10f), far ? 7 : 3);
                    // the rings inside this board: its ends and every 1 m ring between
                    var ds = new List<float> { bd };
                    for (int k = 0; k < rings; k++) { float dk = k * Spacing / Sub; if (dk > bd + 0.05f && dk < be - 0.05f) ds.Add(dk); }
                    ds.Add(be);
                    int prevRow = -1;
                    foreach (float dd in ds)
                    {
                        float fk = dd / (Spacing / Sub);
                        int k0 = Mathf.Clamp(Mathf.FloorToInt(fk), 0, rings - 2); float tk = Mathf.Clamp01(fk - k0);
                        Vector3 c = Vector3.Lerp(cen[k0], cen[k0 + 1], tk), r = Vector3.Lerp(rgt[k0], rgt[k0 + 1], tk).normalized;
                        float a = Amb(dd) * 0.5f;
                        int row = cV.Count;
                        for (int j = 0; j < P; j++)
                        {
                            Vector2 q = prof[j];
                            cV.Add(c + r * q.x + Vector3.up * q.y);
                            cUV.Add(new Vector2(dd / 2.4f, arc[j] / 2.4f));
                            // damp darker low on the walls and in the crown's seep line
                            float h = q.y / T2CrownY(halfW);
                            float stain = 1f - 0.10f * Mathf.Clamp01(1f - (q.y - T2FootTopY) / 1.6f) - 0.06f * Mathf.Clamp01((h - 0.93f) / 0.07f);
                            float s2 = a * shade * stain;
                            cC.Add(new Color(s2, s2, s2 * 0.98f, 1f));
                        }
                        if (prevRow >= 0)
                            for (int j = 0; j < P - 1; j++)
                            {
                                Vector2 m = (prof[j] + prof[j + 1]) * 0.5f, inw = Inward(m);
                                Vector3 want = r * inw.x + Vector3.up * inw.y;
                                QuadFacing(cV, cT, prevRow + j, prevRow + j + 1, row + j + 1, row + j, want);
                            }
                        prevRow = row;
                    }
                }
            }

            // ROCK: between the linings, the profile pushed out by the blasted
            // rock field (base 0.14 m, up to 0.40 m in the arch, 0.12 m on the
            // walls below the springing); eased from the lining's line over
            // its first 2.5 m. Rings every metre, profile points shared.
            {
                int kA = Mathf.FloorToInt(LinedM / (Spacing / Sub)), kB = Mathf.CeilToInt((total - LinedM) / (Spacing / Sub));
                int prevRow = -1;
                for (int k = kA; k <= kB; k++)
                {
                    float dist = Mathf.Clamp(k * Spacing / Sub, LinedM, total - LinedM);
                    float fk = dist / (Spacing / Sub);
                    int k0 = Mathf.Clamp(Mathf.FloorToInt(fk), 0, rings - 2); float tk = Mathf.Clamp01(fk - k0);
                    Vector3 c = Vector3.Lerp(cen[k0], cen[k0 + 1], tk), r = Vector3.Lerp(rgt[k0], rgt[k0 + 1], tk).normalized;
                    float sp = Mathf.Min(dist, total - dist);
                    float ease = Mathf.SmoothStep(0f, 1f, (sp - LinedM) / 2.5f);
                    float a = Amb(dist) * 0.5f;
                    int row = rV.Count;
                    for (int j = 0; j < P; j++)
                    {
                        Vector2 q = prof[j];
                        Vector3 p0 = c + r * q.x + Vector3.up * q.y;
                        Vector2 inw = Inward(q);
                        float arch = Mathf.Clamp01((q.y - T2SpringY) / 1.2f);
                        float nz = T2Rock(p0);
                        // (tunnel2) rougher: the blasted bore of his 11-02 drive, big facets
                        float off = (j == 0 || j == P - 1) ? 0f : Tunnel3 ? ease * (0.18f + Mathf.Lerp(0.22f, 0.62f, arch) * nz)
                                                                          : ease * (0.14f + Mathf.Lerp(0.12f, 0.40f, arch) * nz);
                        Vector3 outDir = -(r * inw.x + Vector3.up * inw.y);
                        rV.Add(p0 + outDir * off);
                        rUV.Add(new Vector2(dist / 6f, arc[j] / 6f));
                        float tone = a * (0.86f + 0.26f * T2Value(p0 / 2.3f + new Vector3(5f, 9f, 1f))) * Mathf.Lerp(1.05f, 0.92f, nz);
                        rC.Add(new Color(tone, tone, tone * 0.97f, 1f));
                    }
                    if (prevRow >= 0)
                        for (int j = 0; j < P - 1; j++)
                        {
                            Vector2 m = (prof[j] + prof[j + 1]) * 0.5f, inw = Inward(m);
                            QuadFacing(rV, rT, prevRow + j, prevRow + j + 1, row + j + 1, row + j, r * inw.x + Vector3.up * inw.y);
                        }
                    prevRow = row;
                }
            }

            T2Mesh("TunnelLining" + no, cV, cUV, cC, cT, liningMat, parent, null);
            T2Mesh("TunnelRock" + no, rV, rUV, rC, rT, rockMat, parent, null);
        }

        static GameObject T2Mesh(string name, List<Vector3> v, List<Vector2> uv, List<Color> col, List<int> t, Material mat, Transform parent, List<Vector3> normals)
        {
            if (t.Count == 0) return null;
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetUVs(0, uv);
            if (col != null) mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            if (normals != null) mesh.SetNormals(normals); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            // the small parts cast no sun shadow (a pass each, nothing to see)
            if (name.StartsWith("TunnelLamp") || name.StartsWith("TunnelSign"))
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.isStatic = true;
            t2Tris += t.Count / 3;
            return go;
        }

        // ------------------------------------------------------------------
        //  The wall lamps
        // ------------------------------------------------------------------
        /// <summary>The fixtures (a dark bulkhead housing and an amber lens) on
        /// the LEFT wall of the forward bore, 2.7 m up, every
        /// <see cref="T2LampPitchM"/> from half a pitch in, and the
        /// <see cref="TunnelLampRow"/> the runtime lights them from.</summary>
        static void BuildLampRow2(List<Vector3> pts, int from, int stations, Transform parent, int no, float halfW)
        {
            int n = pts.Count;
            float R = T2Radius(halfW), total = (stations - 1) * Spacing;
            // the arch's surface at the lamp's height
            float wallX = Mathf.Sqrt(Mathf.Max(0f, R * R - (T2LampY - T2SpringY) * (T2LampY - T2SpringY)));
            var hv = new List<Vector3>(); var huv = new List<Vector2>(); var ht = new List<int>();
            var lv = new List<Vector3>(); var luv = new List<Vector2>(); var lt = new List<int>();
            var heads = new List<Vector3>();
            int count = Mathf.Max(1, Mathf.FloorToInt((total - 4f) / T2LampPitchM) + 1);
            float start = (total - (count - 1) * T2LampPitchM) * 0.5f;
            for (int q = 0; q < count; q++)
            {
                float dist = start + q * T2LampPitchM;
                float fs = dist / Spacing;
                int s = Mathf.Clamp(Mathf.FloorToInt(fs), 0, stations - 2); float t = fs - s;
                int i0 = WrapIdx(from + s, n), i1 = WrapIdx(from + s + 1, n);
                Vector3 c = Vector3.Lerp(pts[i0], pts[i1], t);
                Vector3 r = Vector3.Lerp(RightAt(pts, i0), RightAt(pts, i1), t).normalized;
                Vector3 fwd = Vector3.Cross(r, Vector3.up).normalized;
                Vector3 inn = r;                               // into the bore from the left wall
                Vector3 baseP = c - r * wallX + Vector3.up * T2LampY;
                // housing: 0.46 along x 0.30 tall, from 0.6 m inside the rock to 0.20 m proud
                Vector3 o = baseP - fwd * 0.23f - Vector3.up * 0.15f - inn * 0.6f;
                if (Tunnel3) o -= inn * 0.5f;
                Block(hv, huv, ht, o, fwd * 0.46f, Vector3.up * 0.30f, inn * (Tunnel3 ? 1.30f : 0.80f), c, r, fwd);
                // the lens: the housing's face, a little proud and smaller
                Vector3 lo = baseP - fwd * 0.18f - Vector3.up * 0.10f + inn * 0.20f;
                Block(lv, luv, lt, lo, fwd * 0.36f, Vector3.up * 0.20f, inn * 0.03f, c, r, fwd);
                heads.Add(baseP + inn * 0.45f - Vector3.up * 0.05f);
            }
            var housing = T2Mat("TunnelLampHousing", StageRockTexPath, new Color(0.16f, 0.16f, 0.15f), false, Vector4.zero, false);
            var lens = T2Mat("TunnelLampLens", null, new Color(1.0f, 0.62f, 0.26f), false, Vector4.zero, false);
            lens.mainTexture = null;
            lens.SetFloat("_Emission", 1f);
            EditorUtility.SetDirty(lens);
            T2Mesh("TunnelLampHousing" + no, hv, huv, null, ht, housing, parent, null);
            T2Mesh("TunnelLampLens" + no, lv, luv, null, lt, lens, parent, null);
            var rowGo = new GameObject("TunnelLampRow" + no);
            rowGo.transform.SetParent(parent, false);
            rowGo.AddComponent<TunnelLampRow>().heads = heads.ToArray();
            Log($"Tunnel {no}: {heads.Count} wall lamps, every {T2LampPitchM:0} m on the left wall at {T2LampY:0.0} m.");
        }

        // ------------------------------------------------------------------
        //  The portal in coursed stone
        // ------------------------------------------------------------------
        /// <summary>One stone: a front outline (four points on the face plane,
        /// in the portal's x/y), set <paramref name="proud"/> in front of the
        /// mortar plane with a <paramref name="bevel"/> chamfer round its face
        /// and sides back to the mortar; its own tint (vertex colour), moss
        /// on its top bevel.</summary>
        static void T2Stone(List<Vector3> v, List<Vector2> uv, List<Color> col, List<int> t, System.Func<float, float, float, Vector3> P,
                            Vector2[] q, float proud, float bevel, float tint, float moss, Vector3 faceDir, float tilt)
        {
            Vector2 mid = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
            var fr = new Vector3[4]; var bk = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                Vector2 d = mid - q[k];
                float len = d.magnitude;
                Vector2 qi = q[k] + (len > 1e-4f ? d / len * Mathf.Min(bevel, len * 0.4f) : Vector2.zero);
                // rough-cut: each front corner its own depth
                float dp = proud + tilt * ((k * 37 + Mathf.RoundToInt(q[k].x * 13f + q[k].y * 7f)) % 5 - 2) * 0.5f;
                fr[k] = P(qi.x, qi.y, dp);
                bk[k] = P(q[k].x, q[k].y, -0.02f);
            }
            Color cFace = new Color(tint, tint, tint * 0.985f, 1f);
            Color cTop = new Color(tint * (1f - 0.25f * moss), tint * (1f - 0.08f * moss), tint * (1f - 0.35f * moss), 1f);
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color cc, Vector3 want)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) col.Add(cc);
                // world-planar uv (the base texture; the rock is world-projected)
                foreach (var p in new[] { a, b, c, d }) uv.Add(new Vector2(p.x + p.z, p.y) / 1.9f);
                QuadFacing(v, t, i, i + 1, i + 2, i + 3, want);
            }
            Quad(fr[0], fr[1], fr[2], fr[3], cFace, faceDir);
            for (int k = 0; k < 4; k++)
            {
                int k1 = (k + 1) % 4;
                Vector3 em = (fr[k] + fr[k1]) * 0.5f - (fr[0] + fr[1] + fr[2] + fr[3]) * 0.25f;
                bool top = em.y > 0.01f && Mathf.Abs(em.y) > 0.5f * em.magnitude;
                Quad(fr[k], fr[k1], bk[k1], bk[k], top ? cTop : cFace * 0.93f, em.normalized + faceDir * 0.3f);
            }
            t2Stones++;
        }

        /// <summary>
        /// The headwall at one mouth, in the station's plane, facing
        /// <paramref name="dir"/> along the road (out of the mountain): the
        /// voussoir ring and its jambs, the coursed face round it, the coping,
        /// the wing walls, and the mortar plane behind every joint.
        /// </summary>
        static void BuildPortal2(List<Vector3> pts, int i, float dir, float halfW, Transform parent, int no, Material stoneMat, Material mortarMat)
        {
            Vector3 right = RightAt(pts, i);
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Vector3 face = fwd * dir;
            Vector3 c = pts[i];
            // x measured along `right` as seen FROM OUTSIDE is mirrored at the
            // entry, which does not matter: the wall is symmetric.
            Vector3 P(float x, float y, float d) => c + right * x + Vector3.up * y + face * d;
            var rnd = new System.Random(9173 + no * 31);
            float Rnd(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            float R = T2Radius(halfW), RE = R + T2RingW;
            float wallX = T2WallX(halfW), top = T2WallTopY(halfW), bottom = -2.6f;
            var sv = new List<Vector3>(); var suv = new List<Vector2>(); var sc = new List<Color>(); var st = new List<int>();
            float Tint() => Rnd(0.48f, 0.74f) * (rnd.NextDouble() < 0.15 ? 0.78f : 1f);

            // ---- the ring: voussoirs over the arch, radiating ----
            const int VN = 25;
            float joint = 0.022f;
            for (int q = 0; q < VN; q++)
            {
                float a0 = Mathf.PI * q / VN + joint / R, a1 = Mathf.PI * (q + 1) / VN - joint / R;
                float re = RE + (q == VN / 2 ? 0.16f : (q % 2 == 0 ? 0.0f : -0.06f));
                Vector2 Pt(float a, float rad) => new Vector2(rad * Mathf.Cos(a), T2SpringY + rad * Mathf.Sin(a));
                var outline = new[] { Pt(a0, R), Pt(a0, re), Pt(a1, re), Pt(a1, R) };
                T2Stone(sv, suv, sc, st, P, outline, T2RingProud + Rnd(0f, 0.03f), 0.035f, Tint() * 1.04f, 0.9f, face, 0.012f);
                // the ring's soffit: the voussoir carried 0.45 m into the bore
                // on the arch line (its underside, seen looking up at the mouth)
                {
                    int b = sv.Count;
                    Vector2 i0 = Pt(a0, R), i1 = Pt(a1, R);
                    sv.Add(P(i0.x, i0.y, -0.02f)); sv.Add(P(i1.x, i1.y, -0.02f)); sv.Add(P(i1.x, i1.y, -0.47f)); sv.Add(P(i0.x, i0.y, -0.47f));
                    float tn = Tint();
                    for (int k = 0; k < 4; k++) { sc.Add(new Color(tn * 0.8f, tn * 0.8f, tn * 0.78f, 1f)); }
                    suv.Add(new Vector2(0f, 0f)); suv.Add(new Vector2(0.3f, 0f)); suv.Add(new Vector2(0.3f, 0.25f)); suv.Add(new Vector2(0f, 0.25f));
                    Vector2 mid = (i0 + i1) * 0.5f;
                    QuadFacing(sv, st, b, b + 1, b + 2, b + 3, -(right * mid.x + Vector3.up * (mid.y - T2SpringY)));
                }
            }
            // ---- the jambs: the ring's width carried down to the road in courses ----
            {
                float y = bottom;
                int course = 0;
                while (y < T2SpringY - 0.05f)
                {
                    float h = Mathf.Min(course == 0 ? (-0.3f - bottom) : Rnd(0.42f, 0.62f), T2SpringY - y);
                    if (T2SpringY - (y + h) < 0.18f) h = T2SpringY - y;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        float x0 = s * R, x1 = s * RE;
                        var outline = new[] { new Vector2(x0, y + joint * 0.5f), new Vector2(x0, y + h - joint * 0.5f), new Vector2(x1, y + h - joint * 0.5f), new Vector2(x1, y + joint * 0.5f) };
                        T2Stone(sv, suv, sc, st, P, outline, T2RingProud, 0.035f, Tint() * 1.04f, 0.5f, face, 0.012f);
                        // the jamb's return into the bore (the stone's inner face)
                        int b = sv.Count;
                        sv.Add(P(x0, y, -0.02f)); sv.Add(P(x0, y + h, -0.02f)); sv.Add(P(x0, y + h, -0.47f)); sv.Add(P(x0, y, -0.47f));
                        float tn = Tint();
                        for (int k = 0; k < 4; k++) sc.Add(new Color(tn * 0.8f, tn * 0.8f, tn * 0.78f, 1f));
                        suv.Add(new Vector2(0f, 0f)); suv.Add(new Vector2(0f, 0.3f)); suv.Add(new Vector2(0.25f, 0.3f)); suv.Add(new Vector2(0.25f, 0f));
                        QuadFacing(sv, st, b, b + 1, b + 2, b + 3, -right * s);
                    }
                    y += h; course++;
                }
            }

            // ---- the face: courses of mixed blocks round the ring ----
            // The ring's outer edge at height y: the extrados circle over the
            // springing, the jambs' vertical edge under it.
            float ExtX(float y)
            {
                if (y <= T2SpringY) return RE;
                float dy = y - T2SpringY, reMax = RE + 0.16f;
                return dy >= reMax ? 0f : Mathf.Sqrt(reMax * reMax - dy * dy);
            }
            float coping = 0.32f;
            {
                float y = bottom;
                int course = 0;
                float[] heights = { 0.30f, 0.36f, 0.42f, 0.50f, 0.58f };
                while (y < top - coping - 0.05f)
                {
                    float h = heights[rnd.Next(heights.Length)];
                    if (top - coping - (y + h) < 0.22f) h = top - coping - y;
                    float y0 = y + joint * 0.5f, y1 = y + h - joint * 0.5f;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        // from the ring's edge (that course's) out to the wall's end
                        float e0 = ExtX(y0), e1 = ExtX(y1);
                        bool overRing = e0 <= 0.01f && e1 <= 0.01f;
                        float xIn = overRing ? 0f : Mathf.Max(e0, e1) + joint;
                        float x = xIn;
                        bool first = true;
                        while (x < wallX - 0.05f)
                        {
                            float len = Rnd(0.45f, 1.25f) * (h > 0.45f ? 1.15f : 1f);
                            if (wallX - (x + len) < 0.3f) len = wallX - x;
                            float xa = x + joint * 0.5f, xb = x + len - joint * 0.5f;
                            Vector2[] outline;
                            if (first && !overRing)
                                // the closer: its inner edge follows the ring's curve
                                outline = new[] { new Vector2(s * (e0 + joint), y0), new Vector2(s * (e1 + joint), y1), new Vector2(s * xb, y1), new Vector2(s * xb, y0) };
                            else outline = new[] { new Vector2(s * xa, y0), new Vector2(s * xa, y1), new Vector2(s * xb, y1), new Vector2(s * xb, y0) };
                            float moss = y > top - 2.2f ? 1f : (y < 0.6f ? 0.8f : 0.35f);
                            T2Stone(sv, suv, sc, st, P, outline, Rnd(0.03f, 0.085f), Rnd(0.02f, 0.045f), Tint(), moss, face, 0.016f);
                            x += len; first = false;
                        }
                        if (overRing && s > 0f) { } // (both halves meet at x = 0)
                    }
                    y += h; course++;
                }
            }
            // ---- the coping: a level course along the top, a little proud ----
            {
                float x = -wallX - 0.12f;
                while (x < wallX + 0.12f - 0.05f)
                {
                    float len = Mathf.Min(Rnd(0.8f, 1.4f), wallX + 0.12f - x);
                    if (wallX + 0.12f - (x + len) < 0.35f) len = wallX + 0.12f - x;
                    var outline = new[] { new Vector2(x + joint * 0.5f, top - coping + joint * 0.5f), new Vector2(x + joint * 0.5f, top), new Vector2(x + len - joint * 0.5f, top), new Vector2(x + len - joint * 0.5f, top - coping + joint * 0.5f) };
                    T2Stone(sv, suv, sc, st, P, outline, 0.16f, 0.03f, Tint() * 1.05f, 1f, face, 0.01f);
                    // its top, back into the hill (the cover meets it)
                    int b = sv.Count;
                    sv.Add(P(x, top, 0.16f)); sv.Add(P(x + len, top, 0.16f)); sv.Add(P(x + len, top, -0.9f)); sv.Add(P(x, top, -0.9f));
                    float tn = Tint() * 0.9f;
                    for (int k = 0; k < 4; k++) sc.Add(new Color(tn * 0.75f, tn * 0.92f, tn * 0.66f, 1f));
                    suv.Add(new Vector2(0f, 0f)); suv.Add(new Vector2(len / 1.9f, 0f)); suv.Add(new Vector2(len / 1.9f, 0.5f)); suv.Add(new Vector2(0f, 0.5f));
                    QuadFacing(sv, st, b, b + 1, b + 2, b + 3, Vector3.up);
                    x += len;
                }
            }
            // ---- the wing walls: the face carried on, stepping down ----
            float[] wingTops = { top - 2.2f, top - 4.0f, top - 5.6f };
            float wingStep = T2WingM / wingTops.Length;
            foreach (float s in new[] { -1f, 1f })
                for (int w = 0; w < wingTops.Length; w++)
                {
                    float xa0 = wallX + w * wingStep, xb0 = xa0 + wingStep;
                    float wt = Mathf.Max(wingTops[w], 1.2f);
                    float y = bottom;
                    while (y < wt - 0.3f - 0.05f)
                    {
                        float h = Mathf.Min(Rnd(0.32f, 0.55f), wt - 0.3f - y);
                        if (wt - 0.3f - (y + h) < 0.2f) h = wt - 0.3f - y;
                        float x = xa0;
                        while (x < xb0 - 0.05f)
                        {
                            float len = Mathf.Min(Rnd(0.5f, 1.2f), xb0 - x);
                            if (xb0 - (x + len) < 0.3f) len = xb0 - x;
                            var outline = new[] { new Vector2(s * (x + joint * 0.5f), y + joint * 0.5f), new Vector2(s * (x + joint * 0.5f), y + h - joint * 0.5f), new Vector2(s * (x + len - joint * 0.5f), y + h - joint * 0.5f), new Vector2(s * (x + len - joint * 0.5f), y + joint * 0.5f) };
                            T2Stone(sv, suv, sc, st, P, outline, Rnd(0.03f, 0.08f), 0.035f, Tint(), 0.6f, face, 0.016f);
                            x += len;
                        }
                        y += h;
                    }
                    // the step's own coping
                    var cop = new[] { new Vector2(s * xa0, wt - 0.3f), new Vector2(s * xa0, wt), new Vector2(s * xb0, wt), new Vector2(s * xb0, wt - 0.3f) };
                    T2Stone(sv, suv, sc, st, P, cop, 0.14f, 0.03f, Tint() * 1.05f, 1f, face, 0.01f);
                    // the wall's end (the outermost step) and each step's top, back into the slope
                    {
                        int b = sv.Count;
                        sv.Add(P(s * xa0, wt, 0.14f)); sv.Add(P(s * xb0, wt, 0.14f)); sv.Add(P(s * xb0, wt, -0.9f)); sv.Add(P(s * xa0, wt, -0.9f));
                        float tn = Tint() * 0.9f;
                        for (int k = 0; k < 4; k++) sc.Add(new Color(tn * 0.75f, tn * 0.92f, tn * 0.66f, 1f));
                        suv.Add(new Vector2(0f, 0f)); suv.Add(new Vector2(0.8f, 0f)); suv.Add(new Vector2(0.8f, 0.5f)); suv.Add(new Vector2(0f, 0.5f));
                        QuadFacing(sv, st, b, b + 1, b + 2, b + 3, Vector3.up);
                    }
                    if (w + 1 < wingTops.Length)
                    {
                        // the riser between this step and the next, facing out along the wall
                        float wn = Mathf.Max(wingTops[w + 1], 1.2f);
                        int b = sv.Count;
                        sv.Add(P(s * xb0, wn, 0.14f)); sv.Add(P(s * xb0, wt, 0.14f)); sv.Add(P(s * xb0, wt, -0.9f)); sv.Add(P(s * xb0, wn, -0.9f));
                        float tn = Tint();
                        for (int k = 0; k < 4; k++) sc.Add(new Color(tn, tn, tn, 1f));
                        suv.Add(new Vector2(0f, 0f)); suv.Add(new Vector2(0f, 0.5f)); suv.Add(new Vector2(0.5f, 0.5f)); suv.Add(new Vector2(0.5f, 0f));
                        QuadFacing(sv, st, b, b + 1, b + 2, b + 3, right * s);
                    }
                    else
                    {
                        // the wall's last end face
                        int b = sv.Count;
                        sv.Add(P(s * xb0, bottom, 0.1f)); sv.Add(P(s * xb0, wt, 0.14f)); sv.Add(P(s * xb0, wt, -0.9f)); sv.Add(P(s * xb0, bottom, -0.9f));
                        float tn = Tint();
                        for (int k = 0; k < 4; k++) sc.Add(new Color(tn, tn, tn, 1f));
                        suv.Add(new Vector2(0f, 0f)); suv.Add(new Vector2(0f, 0.5f)); suv.Add(new Vector2(0.5f, 0.5f)); suv.Add(new Vector2(0.5f, 0f));
                        QuadFacing(sv, st, b, b + 1, b + 2, b + 3, right * s);
                    }
                }

            // ---- the mortar plane behind every joint (one quad strip set) ----
            var mv = new List<Vector3>(); var muv = new List<Vector2>(); var mc = new List<Color>(); var mt = new List<int>();
            void MQuad(Vector2 a, Vector2 b2, Vector2 c2, Vector2 d2, float depth)
            {
                int b = mv.Count;
                foreach (var q in new[] { a, b2, c2, d2 }) { mv.Add(P(q.x, q.y, depth)); muv.Add(q / 1.9f); mc.Add(new Color(0.5f, 0.5f, 0.5f, 1f)); }
                QuadFacing(mv, mt, b, b + 1, b + 2, b + 3, face);
            }
            // round the opening: a fan from the extrados to the wall's edges
            const int FanN = 32;
            float topM = top - 0.01f;
            for (int q = 0; q < FanN; q++)
            {
                float a0 = Mathf.PI * q / FanN, a1 = Mathf.PI * (q + 1) / FanN;
                Vector2 i0 = new Vector2(R * Mathf.Cos(a0), T2SpringY + R * Mathf.Sin(a0)), i1 = new Vector2(R * Mathf.Cos(a1), T2SpringY + R * Mathf.Sin(a1));
                // up to the top (over the ring) as a strip
                MQuad(i0, new Vector2(i0.x, topM), new Vector2(i1.x, topM), i1, -0.025f);
            }
            MQuad(new Vector2(-wallX, bottom), new Vector2(-wallX, topM), new Vector2(-R, topM), new Vector2(-R, bottom), -0.025f);
            MQuad(new Vector2(R, bottom), new Vector2(R, topM), new Vector2(wallX, topM), new Vector2(wallX, bottom), -0.025f);
            foreach (float s in new[] { -1f, 1f })
                for (int w = 0; w < wingTops.Length; w++)
                {
                    float xa0 = wallX + w * wingStep, xb0 = xa0 + wingStep, wt = Mathf.Max(wingTops[w], 1.2f) - 0.01f;
                    if (s < 0f) MQuad(new Vector2(-xb0, bottom), new Vector2(-xb0, wt), new Vector2(-xa0, wt), new Vector2(-xa0, bottom), -0.025f);
                    else MQuad(new Vector2(xa0, bottom), new Vector2(xa0, wt), new Vector2(xb0, wt), new Vector2(xb0, bottom), -0.025f);
                }

            T2Mesh("TunnelPortalStone" + no, sv, suv, sc, st, stoneMat, parent, null);
            T2Mesh("TunnelPortalMortar" + no, mv, muv, mc, mt, mortarMat, parent, null);
        }

        /// <summary>The portal's solids, as drawn: the headwall from the tube's
        /// wall to its end, as tall as the old side panels; the wing walls to
        /// their lowest step's height. Faces on the drawn plane, depth grown
        /// into the hill (the guard wall's seating rule).</summary>
        static void PortalColliders2(List<Vector3> pts, int i, float dir, float halfW, Transform parent, PhysicsMaterial phys)
        {
            Vector3 right = RightAt(pts, i);
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Vector3 into = -fwd * dir;
            portalSpots.Add(pts[i]);
            float wallX = T2WallX(halfW);
            foreach (float side in new[] { -1f, 1f })
                foreach (var (x0, x1, h) in new[] { (halfW, wallX, T2WallTopY(halfW)), (wallX, wallX + T2WingM, 1.2f) })
                {
                    float width = x1 - x0;
                    float y0 = pts[i].y - 1.5f, y1 = pts[i].y + h;
                    Vector3 p = pts[i] + right * (side * (x0 + width * 0.5f)) + into * (StageWallCollThick * 0.5f);
                    // (the wing's box by its own name: the self-test counts four
                    // WallPortal boxes a tube, one either side of each mouth)
                    var seg = new GameObject(x0 < wallX ? "WallPortal" : "WallPortalWing");
                    seg.transform.SetParent(parent, false);
                    seg.transform.position = new Vector3(p.x, (y0 + y1) * 0.5f, p.z);
                    seg.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                    var box = seg.AddComponent<BoxCollider>();
                    box.size = new Vector3(width, y1 - y0, StageWallCollThick);
                    box.sharedMaterial = phys;
                    seg.layer = SolidLayer;
                    seg.isStatic = true;
                }
        }

        // ------------------------------------------------------------------
        //  The hill over the bore
        // ------------------------------------------------------------------
        /// <summary>
        /// The ground mesh has a hole over every tunnel station (no vertex
        /// within the tube, a 12 m lattice quad dropped round it), which the
        /// old rock face hid from the approach. This lays the hill back over
        /// it: a grid in each tunnel's frame, 1.5 m across, a ring a station,
        /// on the ground mesh's own surface 0.3 m under it where there is one
        /// (raycast to its colliders) and the DEM where the hole is; never
        /// lower than the masonry's top over its width near a portal, nor
        /// than 1.2 m over the arch's crown anywhere over the bore. Ground
        /// material; no collider (no wheel reaches it).
        /// </summary>
        static void BuildBoreCover(List<Vector3> pts, int from, int stations, Transform parent, int no, float halfW, Material groundMat)
        {
            int n = pts.Count;
            Physics.SyncTransforms();
            float reach = halfW + 1f + NearCell * 1.6f, step = 1.5f;
            int cols = Mathf.CeilToInt(2f * reach / step) + 1;
            float wallX = T2WallX(halfW), wallTop = T2WallTopY(halfW), crownMin = T2CrownY(halfW) + 1.2f;
            float total = (stations - 1) * Spacing;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float GroundAt(Vector3 p, out bool mesh)
            {
                mesh = false;
                var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 600f, p.z), Vector3.down, 1400f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.NegativeInfinity;
                foreach (var h in hits)
                    if (h.collider != null && h.collider.name.Contains("GroundN") && h.point.y > best) { best = h.point.y; mesh = true; }
                return mesh ? best : StageDemY(p.x, p.z);
            }
            // the masonry's top at x (over the road at a portal): headwall, wing steps
            float WingTop(float ax)
            {
                if (ax <= wallX) return wallTop - 0.05f;
                float[] tops = { wallTop - 2.2f, wallTop - 4.0f, wallTop - 5.6f };
                int w = Mathf.Clamp(Mathf.FloorToInt((ax - wallX) / (T2WingM / tops.Length)), 0, tops.Length - 1);
                return Mathf.Max(tops[w], 1.2f) - 0.05f;
            }
            Vector3 Frame(float dist, out Vector3 r, out Vector3 fwd)
            {
                float fs = Mathf.Clamp(dist / Spacing, 0f, stations - 1 - 1e-4f);
                int s = Mathf.Min(Mathf.FloorToInt(fs), stations - 2); float tt = fs - s;
                int i0 = WrapIdx(from + s, n), i1 = WrapIdx(from + s + 1, n);
                r = Vector3.Lerp(RightAt(pts, i0), RightAt(pts, i1), tt).normalized;
                fwd = Vector3.Cross(r, Vector3.up).normalized;
                return Vector3.Lerp(pts[i0], pts[i1], tt);
            }
            // per portal: the base height across its plane - the masonry's top
            // over its width, the approach's ground beyond it
            float[] baseA = new float[cols], baseB = new float[cols];
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 pc = Frame(pass == 0 ? 0f : total, out Vector3 pr, out Vector3 pf);
                Vector3 face = pf * (pass == 0 ? -1f : 1f);
                for (int j = 0; j < cols; j++)
                {
                    float x = -reach + j * step, ax = Mathf.Abs(x);
                    // the approach's ground just in front of the plane (the cut
                    // slope): a wing step dies into it where it stands higher
                    Vector3 q = pc + pr * x + face * 1.5f;
                    float gq = GroundAt(q, out bool om);
                    float front = om ? gq : StageGroundHeightAt(q.x, q.z);
                    float b = ax <= wallX ? pc.y + WingTop(ax)
                            : ax <= wallX + T2WingM ? Mathf.Max(pc.y + WingTop(ax), front) : front;
                    if (pass == 0) baseA[j] = b; else baseB[j] = b;
                }
            }
            // ---- over the bore: rings every 2 m from 0.6 m behind each headwall,
            // rising from the base at 1:0.9 until under the hill ----
            var dists = new List<float>();
            for (float d = 0.6f; d < total - 0.6f; d += 2f) dists.Add(d);
            dists.Add(total - 0.6f);
            for (int k = 0; k < dists.Count; k++)
            {
                float dist = dists[k];
                Vector3 c = Frame(dist, out Vector3 r, out Vector3 fwd);
                bool nearA = dist < total * 0.5f;
                float sP = nearA ? dist : total - dist;
                for (int j = 0; j < cols; j++)
                {
                    float x = -reach + j * step, ax = Mathf.Abs(x);
                    Vector3 p = c + r * x;
                    float g = GroundAt(p, out bool onMesh);
                    float slope = (nearA ? baseA[j] : baseB[j]) + Mathf.Max(0f, sP - 0.6f) * 1.1f;
                    float y = Mathf.Min(onMesh ? g - 0.3f : g, slope);
                    if (ax < halfW + 1.5f) y = Mathf.Max(y, c.y + crownMin);
                    if ((j == 0 || j == cols - 1) && onMesh) y = g - 0.6f;
                    v.Add(new Vector3(p.x, y, p.z));
                    uv.Add(new Vector2(p.x, p.z) / 8f);
                    if (k > 0 && j > 0)
                    {
                        int a = (k - 1) * cols + j - 1, b = (k - 1) * cols + j, d = k * cols + j, e = k * cols + j - 1;
                        QuadFacing(v, t, a, b, d, e, Vector3.up);
                    }
                }
            }
            // ---- in front of each portal, beside the road: the approach's
            // ground 0.4 m under it (hidden where the ground mesh is; filling
            // the hole its dropped lattice quads leave at the mouth), stitched
            // to the cover's first ring ----
            float x0 = RoadWidth * 0.5f + 2.5f;
            int sideCols = Mathf.CeilToInt((reach - x0) / step) + 1;
            for (int pass = 0; pass < 2; pass++)
                foreach (float side in new[] { -1f, 1f })
                {
                    int start = v.Count, rowsN = 0;
                    for (float s = 0.6f; s >= -16f; s -= 2f, rowsN++)
                    {
                        float dist = pass == 0 ? s : total - s;
                        Vector3 c = Frame(Mathf.Clamp(dist, 0f, total), out Vector3 r, out Vector3 fwd);
                        float beyond = pass == 0 ? Mathf.Min(0f, dist) : Mathf.Max(0f, dist - total);
                        c += fwd * beyond;
                        for (int j = 0; j < sideCols; j++)
                        {
                            float x = side * Mathf.Min(x0 + j * step, reach);
                            Vector3 p = c + r * x;
                            float y;
                            int jj = Mathf.Clamp(Mathf.RoundToInt((x + reach) / step), 0, cols - 1);
                            if (s > 0f) y = v[(pass == 0 ? 0 : dists.Count - 1) * cols + jj].y;
                            else
                            {
                                float g = GroundAt(p, out bool om);
                                y = (om ? g : StageGroundHeightAt(p.x, p.z)) - 0.4f;
                                // never over the headwall's top in front of it
                                if (Mathf.Abs(x) <= wallX) y = Mathf.Min(y, pass == 0 ? baseA[jj] : baseB[jj]);
                            }
                            v.Add(new Vector3(p.x, y, p.z));
                            uv.Add(new Vector2(p.x, p.z) / 8f);
                            if (rowsN > 0 && j > 0)
                            {
                                int a = start + (rowsN - 1) * sideCols + j - 1, b = start + (rowsN - 1) * sideCols + j, d = start + rowsN * sideCols + j, e = start + rowsN * sideCols + j - 1;
                                QuadFacing(v, t, a, b, d, e, Vector3.up);
                            }
                        }
                    }
                }
            T2Mesh("TunnelCover" + no, v, uv, null, t, groundMat, parent, null);
        }

        // ------------------------------------------------------------------
        //  The name sign
        // ------------------------------------------------------------------
        static string T2SignTexPath(string id) => Root + "/Art/Tunnel/tunnel_sign_" + id.ToLowerInvariant() + ".png";

        /// <summary>A brown Parkway sign with the tunnel's name in white serif
        /// letters, on one brown post, on the right of the road
        /// <paramref name="before"/> metres before the mouth (as the traffic
        /// coming to it sees it), its near edge 1.8 m past the tarmac.</summary>
        static void BuildTunnelSign(List<Vector3> pts, int portalIdx, int awayDir, Transform parent, int no, string texPath, PhysicsMaterial phys)
        {
            int n = pts.Count;
            if (!System.IO.File.Exists(texPath)) { Log("Tunnel sign: no texture " + texPath); return; }
            float before = 6f;
            int steps = Mathf.RoundToInt(before / Spacing);
            int i = WrapIdx(portalIdx + awayDir * steps, n);
            Vector3 c = pts[i];
            Vector3 right = RightAt(pts, i);
            // the traffic's forward at the sign: toward the portal
            Vector3 travel = Vector3.Cross(right, Vector3.up).normalized * (-awayDir);
            Vector3 trafficRight = Vector3.Cross(Vector3.up, travel).normalized;
            float panelW = 1.5f, panelH = 1.2f, postH = 2.5f, setOff = 1.8f + panelW * 0.5f;
            Vector3 foot = c + trafficRight * (RoadWidth * 0.5f + setOff);
            foot.y = StageGroundHeightAt(foot.x, foot.z);
            // panel faces the oncoming traffic
            Vector3 faceN = -travel;
            Vector3 across = Vector3.Cross(faceN, Vector3.up).normalized;   // the panel's +x as the driver reads it (his right)
            var post = T2Mat("TunnelSignPost", StageRockTexPath, new Color(0.24f, 0.16f, 0.10f), false, Vector4.zero, false);
            var panel = MakeMat(MeshPrefix + "TunnelSign", texPath, affine: 0f);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            // the post: a 0.1 m square timber, the panel's bottom at postH - panelH + 0.1
            Block(v, uv, t, foot - across * 0.05f - faceN * 0.12f - Vector3.up * 0.3f, across * 0.1f, Vector3.up * (postH + 0.3f), faceN * 0.1f, foot, across, faceN);
            T2Mesh("TunnelSignPost" + no, v, uv, null, t, post, parent, null);
            // the panel: a thin board, its face textured
            var pv = new List<Vector3>(); var puv = new List<Vector2>(); var pt = new List<int>();
            Vector3 o = foot + Vector3.up * (postH - panelH);
            Vector3 bl = o - across * (panelW * 0.5f), tl = bl + Vector3.up * panelH, tr = tl + across * panelW, br = bl + across * panelW;
            int b0 = pv.Count;
            pv.Add(bl); pv.Add(tl); pv.Add(tr); pv.Add(br);
            puv.Add(new Vector2(0f, 0f)); puv.Add(new Vector2(0f, 1f)); puv.Add(new Vector2(1f, 1f)); puv.Add(new Vector2(1f, 0f));
            QuadFacing(pv, pt, b0, b0 + 1, b0 + 2, b0 + 3, faceN);
            // the back and edges in the board's brown (the texture's corner)
            Vector3 back = -faceN * 0.04f;
            int b1 = pv.Count;
            pv.Add(bl + back); pv.Add(tl + back); pv.Add(tr + back); pv.Add(br + back);
            for (int k = 0; k < 4; k++) puv.Add(new Vector2(0.01f, 0.01f));
            QuadFacing(pv, pt, b1, b1 + 1, b1 + 2, b1 + 3, -faceN);
            T2Mesh("TunnelSignPanel" + no, pv, puv, null, pt, panel, parent, null);
            // the post is solid
            var col = new GameObject("SignPostColl");
            col.transform.SetParent(parent, false);
            col.transform.position = foot + Vector3.up * (postH * 0.5f) - faceN * 0.07f;
            col.transform.rotation = Quaternion.LookRotation(faceN, Vector3.up);
            var bx = col.AddComponent<BoxCollider>(); bx.size = new Vector3(0.12f, postH, 0.12f); bx.sharedMaterial = phys;
            col.layer = SolidLayer; col.isStatic = true;
        }

        /// <summary>
        /// The Parkway tunnel at one run of stations (PSX_TUNNEL2): bore,
        /// lamps, portals, the hill over it, the signs. The wall solids are
        /// the c96b5178 tube's (BuildOneTunnel builds them either way).
        /// </summary>
        static void BuildTunnel2Parts(List<Vector3> pts, int from, int stations, Transform parent, Material rockMat, PhysicsMaterial phys, int no, float halfW)
        {
            int n = pts.Count;
            // every part under one "Tunnel<no>" (the self-test's tube marker)
            var tube = new GameObject("Tunnel" + no);
            tube.transform.SetParent(parent, false);
            tube.isStatic = true;
            parent = tube.transform;
            var lining = T2Mat("TunnelLining", Root + "/Art/Buildings/Textures/ConcreteBare.png", new Color(0.80f, 0.79f, 0.76f), false, Vector4.zero);
            // (tunnel2) PSX_TUNNEL3: round 2's real portal, hillside and sign (PSXRacingBuilder.Tunnel3.cs)
            if (Tunnel3) { BuildTunnel3Parts(pts, from, stations, parent, rockMat, phys, no, halfW, lining); return; }
            var stone = T2Mat("TunnelPortalStone2", StageRockTexPath, new Color(0.88f, 0.90f, 0.94f), true, new Vector4(1f / 2.6f, 1f / 11f, 1.0f, 1.6f));
            var mortar = T2Mat("TunnelPortalMortar", StageRockTexPath, new Color(0.22f, 0.21f, 0.20f), false, Vector4.zero, false);
            BuildBore2(pts, from, stations, parent, rockMat, lining, no, halfW);
            BuildLampRow2(pts, from, stations, parent, no, halfW);
            int iA = WrapIdx(from, n), iB = WrapIdx(from + stations - 1, n);
            BuildPortal2(pts, iA, -1f, halfW, parent, no * 2, stone, mortar);
            BuildPortal2(pts, iB, 1f, halfW, parent, no * 2 + 1, stone, mortar);
            PortalColliders2(pts, iA, -1f, halfW, parent, phys);
            PortalColliders2(pts, iB, 1f, halfW, parent, phys);
            var ground = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + MeshPrefix + "Ground.mat");
            if (ground != null) BuildBoreCover(pts, from, stations, parent, no, halfW, ground);
            else Log("Tunnel cover: no ground material " + MeshPrefix + "Ground");
            string sign = T2SignTexPath(track.id);
            BuildTunnelSign(pts, iA, -1, parent, no * 2, sign, phys);
            BuildTunnelSign(pts, iB, 1, parent, no * 2 + 1, sign, phys);
        }
    }
}
