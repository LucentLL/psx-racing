using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ROCK RELIEF on the mountain stages' road cuts (owner 2026-11-08, beside
    /// four Blue Ridge Parkway frames: "The rock faces seem very flat in game,
    /// when most on the actual Blue Ridge have texture and a steeply sloped").
    ///
    /// The cut's drawn face was two quads tall per station: a 1.4 m plinth
    /// and one battered strip to the crest. On the PC scene pass (PCContent,
    /// the web data file stays as it was) every chord between two FACED
    /// stations is drawn instead as a fine grid (ReliefAlong x ReliefUp)
    /// pushed into the hill by a fractured-rock field: strata about a metre
    /// high, each broken into blocks 1.4-3.6 m long at its own offset, each
    /// block set back by its own amount (a few standing proud), joints cut
    /// between blocks, the top of each stratum leaning back so the one below
    /// shows a ledge. Tapered to nothing at the toe (the ditch meets it) and
    /// at the crest (the rock top meets it).
    ///
    /// The COLLIDER is untouched (BuildWallSolid's smooth hull, as before):
    /// the relief only ever goes INTO the hill, except blocks standing at
    /// most <see cref="ReliefProud"/> proud of it.
    ///
    /// Ledges - cells of the relief that face up - are kept in
    /// <see cref="rockLedges"/> for the forest pass, which grows grass,
    /// cherry and young beech on them (the real cuts' shelves are green).
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        // Was OFF (owner 2026-11-08: the relief read as "a dirt cliff that
        // would be held up by tree roots", then as stacked slabs - not the
        // Parkway's jagged gneiss).
        // ON again (owner 2026-11-11: "a lot of 'rockface' on mountain track
        /// are just flat rock textures. Very bland, repetitive and not
        /// realistic", beside ten Parkway photographs): the field is no longer
        /// level strata in blocks but the blasted gneiss of the photographs -
        /// beds DIPPING across the cut at the cut's own angle, broken into
        /// angular blocks (a jittered cell field), each block a tilted plane
        /// of its own, joints between, and broad bays along the cut so the
        /// face never runs as one straight ribbon. With PSX_ROCKFACE on the
        /// material (world-space photoscan, stain, seep, moss on the ledges).
        const bool RockReliefOn = true;
        const float ReliefAlong = 0.7f, ReliefUp = 0.5f, ReliefProud = 0.15f;
        /// <summary>Up to this height over the road the face is the band a car
        /// can touch: the relief stays within <see cref="ReliefContactM"/> of
        /// the collider there (no invisible wall in front of recessed rock).</summary>
        const float ReliefContactH = 1.3f, ReliefContactM = 0.12f;
        /// <summary>In that band the rock may stand back of the smooth face by
        /// this much at most (cracks, undercuts, set-back blocks): a car on the
        /// solid sees at most a hand's breadth or so of air.</summary>
        const float ReliefBandIn = 0.35f;
        static int reliefLedges, lipTris, lipPlants, talusN;
        /// <summary>Up-facing relief cells this stage (stage-parent frame).</summary>
        static readonly List<Vector3> rockLedges = new List<Vector3>();
        /// <summary>Per ledge: 0 any, 1 over a crest (bush, young tree), 2 out of a face (fern, grass), 3 a cut's foot.</summary>
        static readonly List<int> rockLedgeKind = new List<int>();
        static int reliefChords, reliefTris;

        static float RHash(int a, int b, int c)
        {
            uint h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)c * 83492791u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFFu) / 65535f;
        }

        static float RNoise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = RHash(xi, yi, seed), b = RHash(xi + 1, yi, seed);
            float c = RHash(xi, yi + 1, seed), d = RHash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>How far into the hill (m) the rock stands back at distance
        /// <paramref name="a"/> along the cut and height <paramref name="y"/>.</summary>
        static float ReliefDepth(float a, float y, int seed)
        {
            // THE BEDS DIP: the cut's own foliation angle, 10-40 degrees off
            // level, one way or the other (the photographs' layered gneiss
            // never runs level along a cut). u along the beds, v across them.
            float dip = (0.17f + RHash(seed, 3, 7) * 0.5f) * (RHash(seed, 5, 7) < 0.5f ? -1f : 1f);
            float cs = Mathf.Cos(dip), sn = Mathf.Sin(dip);
            float u = a * cs + y * sn, v = -a * sn + y * cs;
            // ANGULAR BLOCKS: a jittered cell field, cells 3.2 m along the beds
            // and 1.5 m across them; the nearest cell's block, and how near the
            // next one is (a joint between them).
            const float CU = 3.2f, CV = 1.5f;
            float su = u / CU, sv = v / CV;
            int iu = Mathf.FloorToInt(su), iv = Mathf.FloorToInt(sv);
            float d1 = float.MaxValue, d2 = float.MaxValue;
            int bu = 0, bv = 0; float cu = 0f, cv = 0f;
            for (int du = -1; du <= 1; du++)
                for (int dv = -1; dv <= 1; dv++)
                {
                    int gu = iu + du, gv = iv + dv;
                    float pu = gu + 0.15f + 0.7f * RHash(gu, gv, seed + 41);
                    float pv = gv + 0.15f + 0.7f * RHash(gv, gu, seed + 43);
                    float ex = (su - pu) * CU, ey = (sv - pv) * CV;
                    float dd = ex * ex + ey * ey;
                    if (dd < d1) { d2 = d1; d1 = dd; bu = gu; bv = gv; cu = pu * CU; cv = pv * CV; }
                    else if (dd < d2) d2 = dd;
                }
            // the block: set back 0.15-1.25 m (one in nine standing proud),
            // its face tilted both ways - along the beds up to 0.35 m a metre,
            // across them leaning back 0.1-0.55 m a metre, so the top of each
            // block is a ledge under the one above
            float hb = RHash(bu, bv, seed + 11);
            float depth = hb > 0.89f ? -ReliefProud : 0.15f + hb * 1.1f;
            depth += (RHash(bu, bv, seed + 23) - 0.5f) * 0.7f * (u - cu);
            depth += (0.1f + RHash(bu, bv, seed + 29) * 0.45f) * (v - cv);
            // the joint between two blocks: a crack set back
            float gap = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
            if (gap < 0.25f) depth += 0.3f * (1f - gap / 0.25f);
            // BAYS along the cut, 0-0.9 m, ~15 m apart: no straight ribbon
            depth += RNoise(a / 15f, 0.37f, seed + 5) * 0.9f;
            // and a little grain
            depth += (RNoise(a * 1.7f, y * 1.7f, seed + 3) - 0.5f) * 0.12f;
            return Mathf.Clamp(depth, -ReliefProud, 2.4f);
        }

        static Vector3 ReliefProfile(Vector3 toe, Vector3 plinth, Vector3 crest, float u)
        {
            float l1 = Vector3.Distance(toe, plinth), l2 = Vector3.Distance(plinth, crest);
            float s = u * (l1 + l2);
            return s <= l1 && l1 > 1e-4f ? Vector3.Lerp(toe, plinth, s / l1)
                                         : Vector3.Lerp(plinth, crest, (s - l1) / Mathf.Max(l2, 1e-4f));
        }

        /// <summary>Replaces the faced chords' flat quads in a bank's drawn face
        /// with the relief grid (PC scene pass only).</summary>
        static void ReliefFace(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                               Vector3[] toe, Vector3[] plinthTop, Vector3[] crest, bool[] faced,
                               float[] distAt, int[] chordT0, int[] chordT1, Vector3[] outw, int R, int seed,
                               float[] colD = null)
        {
            var kill = new bool[tris.Count];
            int chords = 0;
            for (int k = 1; k < R; k++)
            {
                if (!faced[k - 1] || !faced[k]) continue;
                for (int q = chordT0[k]; q < chordT1[k]; q++) kill[q] = true;
                chords++;
            }
            if (chords == 0) return;
            var kept = new List<int>(tris.Count);
            for (int q = 0; q < tris.Count; q++) if (!kill[q]) kept.Add(tris[q]);
            tris.Clear(); tris.AddRange(kept);
            int t0 = tris.Count;
            // Each COLUMN its own row count (its face's length / ReliefUp) and
            // its own push into the hill (the stations' interpolated): the
            // column on a station is then the very same vertices for the two
            // chords that meet there - a count or a direction per chord left
            // slits between them on a bend. Neighbouring columns of different
            // counts are zipped with triangles.
            for (int k = 1; k < R; k++)
            {
                if (!faced[k - 1] || !faced[k]) continue;
                float lenA = Vector3.Distance(toe[k - 1], plinthTop[k - 1]) + Vector3.Distance(plinthTop[k - 1], crest[k - 1]);
                float lenB = Vector3.Distance(toe[k], plinthTop[k]) + Vector3.Distance(plinthTop[k], crest[k]);
                Vector3 step = toe[k] - toe[k - 1]; step.y = 0f;
                int na = Mathf.Max(1, Mathf.CeilToInt(step.magnitude / ReliefAlong));
                var cols = new List<Vector3[]>(na + 1);
                var cuvs = new List<Vector2[]>(na + 1);
                var cdps = new List<float[]>(na + 1);
                float toeMid = (toe[k - 1].y + toe[k].y) * 0.5f;
                Vector3 inward = Vector3.zero, facing = Vector3.zero;
                // a station where the relief ENDS (a flat chord beyond it) is
                // met at depth 0, so the two meet edge to edge
                float wA = k - 2 >= 0 && faced[k - 2] ? 1f : 0f;
                float wB = k + 1 < R && faced[k + 1] ? 1f : 0f;
                for (int ia = 0; ia <= na; ia++)
                {
                    float t = ia / (float)na;
                    float endW = Mathf.SmoothStep(0f, 1f, Mathf.Lerp(wA, wB, t));
                    inward = Vector3.Lerp(outw[k - 1], outw[k], t); inward.y = 0f; inward.Normalize();
                    facing -= inward;
                    float a = Mathf.Lerp(distAt[k - 1], distAt[k], t);
                    float toeY = Mathf.Lerp(toe[k - 1].y, toe[k].y, t);
                    float lenAt = Mathf.Lerp(lenA, lenB, t);
                    int nu = Mathf.Clamp(Mathf.CeilToInt(lenAt / ReliefUp), 2, 48);
                    var col = new Vector3[nu + 1];
                    var cuv = new Vector2[nu + 1];
                    var cdp = new float[nu + 1];
                    for (int iu = 0; iu <= nu; iu++)
                    {
                        float u = iu / (float)nu;
                        var p = Vector3.Lerp(ReliefProfile(toe[k - 1], plinthTop[k - 1], crest[k - 1], u),
                                             ReliefProfile(toe[k], plinthTop[k], crest[k], u), t);
                        // meets the rock top at the crest over its last 0.7 m
                        float taper = Mathf.SmoothStep(0f, 1f, (1f - u) * lenAt / 0.7f);
                        float dpt = ReliefDepth(a, p.y, seed) * taper * endW;
                        // THE CONTACT BAND (a car's height over the road, toe at
                        // road - 0.5): the drawn rock within a hand's breadth of
                        // the solid, and never in front of it
                        float hy = p.y - (toeY + 0.5f);
                        // (where the full relief starts wanders 0.4-2.5 m along
                        // the cut: one level shelf the length of it read as a
                        // course of masonry). Inside the band the SAME field,
                        // scaled down - the same blocks, tilts and cracks, so no
                        // seam - recessed at most ReliefBandIn behind the
                        // smooth face and proud of it at most ReliefContactM.
                        float bandH = 0.4f + RNoise(a / 4.5f, 0.71f, seed + 9) * 2.1f;
                        float inBand = Mathf.Clamp(dpt * 0.32f, -ReliefContactM, ReliefBandIn);
                        if (colD != null)
                        {
                            // (stage3, BankFit) THE BAND IS WHERE THE SOLID IS: up to BankFitBandH the
                            // rock stands on the cut's solid - its bays (the solid steps back with
                            // them, BankFitDepth) and at most BankFitRelief of block relief behind
                            // that, never in front; the full relief starts over it.
                            bandH = BankFitBandH + RNoise(a / 4.5f, 0.71f, seed + 9) * 0.9f;
                            float raw = ReliefDepth(a, p.y, seed);
                            float bay = RNoise(a / 15f, 0.37f, seed + 5) * 0.9f;
                            float small = Mathf.Clamp((raw - bay) * 0.25f, 0f, BankFitRelief) * endW;
                            inBand = (Mathf.Lerp(colD[k - 1], colD[k], t) + small) * taper;
                        }
                        float band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(bandH, bandH + 0.4f + RNoise(a / 3f, 0.3f, seed + 13) * 0.8f, hy));
                        dpt = Mathf.Lerp(inBand, dpt, band);
                        p += inward * dpt;
                        col[iu] = p;
                        cdp[iu] = dpt;
                        cuv[iu] = new Vector2(a / 4.5f, (p.y - toeY) / 4.5f);
                    }
                    cols.Add(col); cuvs.Add(cuv); cdps.Add(cdp);
                }
                facing.Normalize();
                // FACETED: every triangle its own corners, so the normals
                // SaveMesh computes are the facet's own - the block edges stay
                // edges instead of being smoothed round. A zip up each pair of
                // columns: always step the side whose next row is lower.
                // LEDGE PLANTS: only on a ledge (a facet facing up) whose rock
                // stands no deeper than the smooth face - a block standing
                // out of it. One deeper is behind the face's solid, and the
                // plant fit would move its plant out of the solid into the air
                // in front of it. Each goes through PlantClear (AddLedgePlants).
                for (int ia = 0; ia < na; ia++)
                {
                    Vector3[] L = cols[ia], Rr = cols[ia + 1];
                    Vector2[] Lu = cuvs[ia], Ru = cuvs[ia + 1];
                    float[] Ld = cdps[ia], Rd = cdps[ia + 1];
                    int nl = L.Length - 1, nr = Rr.Length - 1, i = 0, j = 0;
                    while (i < nl || j < nr)
                    {
                        bool stepL = j >= nr || (i < nl && (i + 1) / (float)nl <= (j + 1) / (float)nr);
                        int b = verts.Count;
                        float dMax = Mathf.Max(Ld[i], Rd[j]);
                        verts.Add(L[i]); uvs.Add(Lu[i]);
                        verts.Add(Rr[j]); uvs.Add(Ru[j]);
                        if (stepL) { verts.Add(L[i + 1]); uvs.Add(Lu[i + 1]); dMax = Mathf.Max(dMax, Ld[i + 1]); i++; }
                        else { verts.Add(Rr[j + 1]); uvs.Add(Ru[j + 1]); dMax = Mathf.Max(dMax, Rd[j + 1]); j++; }
                        var nrm = Vector3.Cross(verts[b + 1] - verts[b], verts[b + 2] - verts[b]);
                        if (nrm.sqrMagnitude < 1e-10f) { verts.RemoveRange(b, 3); uvs.RemoveRange(b, 3); continue; }
                        if (Vector3.Dot(nrm, facing) >= 0f) tris.AddRange(new[] { b, b + 1, b + 2 });
                        else tris.AddRange(new[] { b, b + 2, b + 1 });
                        var c = (verts[b] + verts[b + 1] + verts[b + 2]) / 3f;
                        if (dMax <= 0.05f && Mathf.Abs(nrm.normalized.y) > 0.7f && c.y > toeMid + 1.3f
                            && RHash(ia + k * 131, i + j * 17, seed + 17) < 0.5f)
                        { AddLedge(c, 2); reliefLedges++; }
                    }
                }
            }
            reliefChords += chords;
            reliefTris += (tris.Count - t0) / 3;
        }

        /// <summary>THE CREST LIP (owner's photographs: soil, leaf litter and
        /// plants spilling over the top of every cut, never a clean edge): a
        /// ribbon of the hillside's own ground over each faced stretch's crest -
        /// hanging a little down the face, standing 0.15-1.1 m over the crest in
        /// lumps of its own along the cut, and tucked back into the rock top
        /// 1.2 m in. Drawn only (it is above anything a car can reach; the rock
        /// top's collider is unchanged). Plant spots on its top and nose go
        /// through PlantClear like every ledge.</summary>
        static void BuildCrestLip(Transform parent, Material topMat, int no, Vector3[] crest, Vector3[] outw,
                                  bool[] faced, float[] distAt, int R, int seed)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            float g = Mathf.Max(theme.groundTile, 0.01f);
            const int P = 5;
            for (int k = 1; k < R; k++)
            {
                if (!faced[k - 1] || !faced[k]) continue;
                float wA = k - 2 >= 0 && faced[k - 2] ? 1f : 0f;
                float wB = k + 1 < R && faced[k + 1] ? 1f : 0f;
                Vector3 step = crest[k] - crest[k - 1]; step.y = 0f;
                int na = Mathf.Max(1, Mathf.CeilToInt(step.magnitude / ReliefAlong));
                int first = v.Count;
                for (int ia = 0; ia <= na; ia++)
                {
                    float t = ia / (float)na;
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.Lerp(wA, wB, t));
                    Vector3 I = Vector3.Lerp(outw[k - 1], outw[k], t); I.y = 0f; I.Normalize();
                    Vector3 C = Vector3.Lerp(crest[k - 1], crest[k], t);
                    float a = Mathf.Lerp(distAt[k - 1], distAt[k], t);
                    // lumps: a few metres each, and a finer break-up
                    float n1 = RNoise(a / 3.2f, 0.17f, seed + 61) * 0.7f + RNoise(a / 0.9f, 0.53f, seed + 67) * 0.3f;
                    float n2 = RNoise(a / 2.3f, 0.83f, seed + 71);
                    // GAPS: stretches with no lip at all, where the rock's own
                    // edge is the skyline (one unbroken lip read as a band)
                    float gate = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.6f, RNoise(a / 7f, 0.91f, seed + 87)));
                    w *= gate;
                    float rise = (0.15f + n1 * 0.95f) * w;
                    float over = (0.1f + n2 * 0.4f) * w;
                    var col = new[]
                    {
                        C - I * (over * 0.5f) - Vector3.up * (0.02f + (0.2f + 0.35f * n2) * w) + I * 0.05f,   // hanging down the face
                        C - I * over + Vector3.up * (rise * 0.55f),                                    // the nose
                        C + I * 0.25f + Vector3.up * (rise - 0.15f * (1f - w)),                         // the top
                        C + I * 0.7f + Vector3.up * (rise * 0.6f - 0.1f - 0.15f * (1f - w)),
                        C + I * 1.2f - Vector3.up * 0.45f,                                              // tucked into the rock top
                    };
                    // uv: along the cut, and down the lip's own profile (a plan
                    // projection streaked the hanging front like thatch)
                    float sp = 0f;
                    for (int q = 0; q < P; q++)
                    {
                        if (q > 0) sp += Vector3.Distance(col[q - 1], col[q]);
                        v.Add(col[q]); uv.Add(new Vector2(a / g, sp / g));
                    }
                    // plants on it: on the top now and then, in the nose's grass
                    if (w > 0.5f && RHash(ia + k * 37, 3, seed + 73) < 0.32f) { AddLedge(col[2], RHash(ia, k, seed + 79) < 0.35f ? 1 : 2); lipPlants++; }
                    if (w > 0.5f && RHash(ia + k * 37, 5, seed + 83) < 0.18f) { AddLedge(col[1] + I * 0.15f, 2); lipPlants++; }
                }
                for (int ia = 0; ia < na; ia++)
                    for (int q = 0; q + 1 < P; q++)
                    {
                        int a0 = first + ia * P + q, a1 = a0 + 1, b0 = a0 + P, b1 = b0 + 1;
                        Vector3 I = Vector3.Lerp(outw[k - 1], outw[k], (ia + 0.5f) / na); I.y = 0f;
                        int t0 = tr.Count;
                        QuadFacingSkipFlat(v, tr, a0, a1, b1, b0, Vector3.up * 0.6f - I.normalized);
                        lipTris += (tr.Count - t0) / 3;
                    }
            }
            if (tr.Count == 0) return;
            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = v.ToArray(), uv = uv.ToArray(), triangles = tr.ToArray(),
            };
            SaveMesh(mesh, "StageBankLip" + no);
            var go = new GameObject("BankLip" + no);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = topMat;
            go.isStatic = true;
        }

        // ------------------------------------------------------------------
        //  Tunnel portals in dressed stone
        // ------------------------------------------------------------------
        /// <summary>The ring springs this far under the tube's ceiling: the
        /// opening reads as an elliptical arch, the tube's square top corners
        /// behind its stones. At the tarmac edge the intrados stands ~4.8 m
        /// over the road, over the shoulder's outer edge 4.0 m.</summary>
        const float PortalSpringDrop = 1.2f, PortalRingW = 0.65f, PortalStoneOut = 0.12f, PortalStoneIn = 0.55f;
        const float PortalStoneTexM = 2.2f;

        /// <summary>THE PORTAL IN STONE (owner's Parkway photographs: a dressed
        /// arch ring round the bore, a masonry headwall): over the rock portal
        /// face, at one mouth - a headwall of the guard walls' dry stone
        /// (theme.wall) 0.06 m proud of the face, round the bore and to 2.6 m
        /// past it each side and 2.4 m over it; quoined jambs and an elliptical
        /// ring of 15 voussoirs, lighter dressed stone, standing 0.12 m proud
        /// and 0.55 m into the tube. Drawn only: the portal's and the tube's
        /// colliders are unchanged (the stones sit beside and over the bore,
        /// none in front of the solid by more than 0.12 m, none within reach
        /// of a car under 4 m over the road).</summary>
        static void BuildPortalStone(List<Vector3> pts, int i, float dir, float halfW, Transform parent, int no)
        {
            Vector3 right = RightAt(pts, i);
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Vector3 face = fwd * dir;                 // out of the mountain
            Vector3 c = pts[i];
            var wv = new List<Vector3>(); var wuv = new List<Vector2>(); var wt = new List<int>();
            var rv = new List<Vector3>(); var ruv = new List<Vector2>(); var rt = new List<int>();
            Vector3 P(float x, float y, float d) => c + right * x + Vector3.up * y + face * d;

            // the headwall, around the bore (left, right, over)
            float wallX = halfW + 2.6f, wallTop = TunnelH + 2.4f, d0 = 0.06f;
            void Panel(float x0, float x1, float y0, float y1)
            {
                int b = wv.Count;
                wv.Add(P(x0, y0, d0)); wv.Add(P(x0, y1, d0)); wv.Add(P(x1, y1, d0)); wv.Add(P(x1, y0, d0));
                wuv.Add(new Vector2(x0, y0) / PortalStoneTexM); wuv.Add(new Vector2(x0, y1) / PortalStoneTexM);
                wuv.Add(new Vector2(x1, y1) / PortalStoneTexM); wuv.Add(new Vector2(x1, y0) / PortalStoneTexM);
                QuadFacing(wv, wt, b, b + 1, b + 2, b + 3, face);
            }
            // beside the jambs to the springing, beside the ring over it, and
            // over the ring down to its extrados (no corner of the square bore
            // left showing between the ring and the wall)
            float yS0 = TunnelH - PortalSpringDrop, aE0 = halfW + PortalRingW, bE0 = PortalSpringDrop + PortalRingW;
            Panel(-wallX, -halfW, -1.0f, yS0);
            Panel(halfW, wallX, -1.0f, yS0);
            Panel(-wallX, -aE0, yS0, wallTop);
            Panel(aE0, wallX, yS0, wallTop);
            const int FanN = 24;
            for (int q = 0; q < FanN; q++)
            {
                float t0 = Mathf.PI * q / FanN, t1 = Mathf.PI * (q + 1) / FanN;
                float x0 = aE0 * Mathf.Cos(t0), x1 = aE0 * Mathf.Cos(t1);
                float y0 = yS0 + bE0 * Mathf.Sin(t0), y1 = yS0 + bE0 * Mathf.Sin(t1);
                int b = wv.Count;
                wv.Add(P(x0, y0, d0)); wv.Add(P(x0, wallTop, d0)); wv.Add(P(x1, wallTop, d0)); wv.Add(P(x1, y1, d0));
                wuv.Add(new Vector2(x0, y0) / PortalStoneTexM); wuv.Add(new Vector2(x0, wallTop) / PortalStoneTexM);
                wuv.Add(new Vector2(x1, wallTop) / PortalStoneTexM); wuv.Add(new Vector2(x1, y1) / PortalStoneTexM);
                QuadFacing(wv, wt, b, b + 1, b + 2, b + 3, face);
            }
            // its coping: a projecting course along the top
            Block(wv, wuv, wt, P(-wallX - 0.15f, wallTop - 0.05f, -0.4f), right * (2f * wallX + 0.3f), Vector3.up * 0.35f, face * 0.62f, c, right, face);

            // the jambs: quoins in courses, long and short by turns
            float yS = TunnelH - PortalSpringDrop;
            for (int s = -1; s <= 1; s += 2)
            {
                int course = 0;
                for (float y = -0.4f; y < yS - 0.05f; y += 0.6f, course++)
                {
                    float y1 = Mathf.Min(y + 0.6f, yS);
                    float w = (course & 1) == 0 ? PortalRingW + 0.25f : PortalRingW;
                    float x0 = s * halfW, x1 = s * (halfW + w);
                    Vector3 o = P(Mathf.Min(x0, x1), y, -PortalStoneIn);
                    Block(rv, ruv, rt, o, right * Mathf.Abs(x1 - x0) * 0.985f, Vector3.up * (y1 - y) * 0.97f,
                          face * (PortalStoneIn + PortalStoneOut), c, right, face);
                }
            }
            // the ring: 15 voussoirs on an elliptical arch from the springing
            // (x = +-halfW at yS) to the crown at the ceiling
            const int N = 15;
            float bI = PortalSpringDrop, aE = halfW + PortalRingW, bE = PortalSpringDrop + PortalRingW;
            for (int q = 0; q < N; q++)
            {
                float t0 = Mathf.PI * q / N, t1 = Mathf.PI * (q + 1) / N;
                // (no gap between voussoirs: one would be a slit into the
                // bore's dark corner behind the ring; the faces' own shading
                // marks the joints)
                Vector3 i0 = P(halfW * Mathf.Cos(t0), yS + bI * Mathf.Sin(t0), 0f), i1 = P(halfW * Mathf.Cos(t1), yS + bI * Mathf.Sin(t1), 0f);
                float kE = q == N / 2 ? 0.18f : 0f;   // the keystone stands a little taller
                Vector3 e0 = P(aE * Mathf.Cos(t0), yS + (bE + kE) * Mathf.Sin(t0), 0f), e1 = P(aE * Mathf.Cos(t1), yS + (bE + kE) * Mathf.Sin(t1), 0f);
                Prism(rv, ruv, rt, new[] { i0, i1, e1, e0 }, face, PortalStoneOut, PortalStoneIn, c, right, face);
            }

            var wallMat = MakeMat(MeshPrefix + "PortalStone", theme.wall, affine: 0f, tint: new Color(0.9f, 0.88f, 0.85f));
            var ringMat = MakeMat(MeshPrefix + "PortalRing", theme.wall, affine: 0f, tint: new Color(1.18f, 1.15f, 1.08f));
            foreach (var (vv, uu, tt, mat, nm) in new[] { (wv, wuv, wt, wallMat, "PortalStone"), (rv, ruv, rt, ringMat, "PortalRing") })
            {
                var mesh = new Mesh { vertices = vv.ToArray(), uv = uu.ToArray(), triangles = tt.ToArray() };
                SaveMesh(mesh, "Tunnel" + nm + no);
                var go = new GameObject(nm + no);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.isStatic = true;
                portalStoneTris += tt.Count / 3;
            }
        }

        static int portalStoneTris;

        /// <summary>A box from corner o along three edges, every face its own
        /// (faceted), turned out from its centre; uv planar in the portal's
        /// frame (c, right, face).</summary>
        static void Block(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 o, Vector3 ex, Vector3 ey, Vector3 ez,
                          Vector3 c, Vector3 right, Vector3 face)
        {
            var corners = new Vector3[8];
            for (int k = 0; k < 8; k++) corners[k] = o + ((k & 1) != 0 ? ex : Vector3.zero) + ((k & 2) != 0 ? ey : Vector3.zero) + ((k & 4) != 0 ? ez : Vector3.zero);
            BoxFaces(v, uv, t, corners, c, right, face);
        }

        /// <summary>A quad outline in the portal plane (offset 0) pushed out
        /// <paramref name="outM"/> and back <paramref name="inM"/> along
        /// <paramref name="n"/>: a voussoir.</summary>
        static void Prism(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3[] q, Vector3 n, float outM, float inM,
                          Vector3 c, Vector3 right, Vector3 face)
        {
            // corner order as Block's: bit0 along the outline's first edge,
            // bit1 across to the far edge, bit2 front/back
            var corners = new Vector3[8];
            Vector3[] quad = { q[0], q[1], q[3], q[2] };
            for (int k = 0; k < 4; k++)
            {
                corners[k] = quad[k] - n * inM;
                corners[k + 4] = quad[k] + n * outM;
            }
            BoxFaces(v, uv, t, corners, c, right, face);
        }

        static void BoxFaces(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3[] k8, Vector3 c, Vector3 right, Vector3 face)
        {
            Vector3 mid = Vector3.zero;
            foreach (var p in k8) mid += p;
            mid /= 8f;
            int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 } };
            foreach (var f in faces)
            {
                Vector3 fc = (k8[f[0]] + k8[f[1]] + k8[f[2]] + k8[f[3]]) * 0.25f;
                Vector3 n = Vector3.Cross(k8[f[1]] - k8[f[0]], k8[f[3]] - k8[f[0]]);
                if (n.sqrMagnitude < 1e-10f) continue;
                n.Normalize();
                int b = v.Count;
                foreach (int idx in f)
                {
                    Vector3 p = k8[idx], l = p - c;
                    float lx = Vector3.Dot(l, right), ly = l.y, ld = Vector3.Dot(l, face);
                    Vector2 u = Mathf.Abs(Vector3.Dot(n, face)) > 0.7f ? new Vector2(lx, ly)
                              : Mathf.Abs(n.y) > 0.7f ? new Vector2(lx, ld) : new Vector2(ld + lx, ly);
                    v.Add(p); uv.Add(u / PortalStoneTexM);
                }
                QuadFacing(v, t, b, b + 1, b + 2, b + 3, fc - mid);
            }
        }

        const string RockFaceBC = MeadowCliffDir + "/T_Photoscanned_Cliff_01_BC.tga";
        const string RockFaceN = MeadowCliffDir + "/T_Photoscanned_Cliff_01_Normal.tga";

        /// <summary>The cut face's material on PC with the Meadow pack: the
        /// photoscanned cliff in world space (PSX/Lit PSX_ROCKFACE) - fine
        /// repeat 7.5 m, coarse 23 m on a turned frame - stained, seeped and
        /// mossed in the shader. Off (the old texture, as the web draws it)
        /// without the pack or off the PC pass.</summary>
        static void SetRockFace(Material m)
        {
            if (m == null) return;
            var bc = PCContent ? AssetDatabase.LoadAssetAtPath<Texture2D>(RockFaceBC) : null;
            var nm = bc != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(RockFaceN) : null;
            if (bc == null || nm == null) { m.DisableKeyword("PSX_ROCKFACE"); return; }
            m.SetTexture("_RockTex", bc);
            m.SetTexture("_RockNrm", nm);
            m.SetVector("_RockParams", new Vector4(1f / 7.5f, 1f / 23f, 0.85f, 1f));
            m.SetVector("_LeafVivid", new Vector4(1f, 1f, 0f, 0f));
            m.color = Color.white;
            m.EnableKeyword("PSX_ROCKFACE");
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------
        //  Boulders on the steep forest slopes
        // ------------------------------------------------------------------
        /// <summary>The boulder's centre this far past the tarmac edge at
        /// least, and its far side no farther than <see cref="BoulderFarE"/>:
        /// clear of the lanes, shoulder and guard wall, and inside the reach
        /// within which every plant is fitted against what is solid (PlantClear's
        /// PlantReachM), so no tree or fern is planted through one.</summary>
        const float BoulderNearE = 3.2f, BoulderFarE = 8.6f;
        static int bouldersPlaced;

        /// <summary>BOULDERS AND OUTCROPS (owner's Linn Cove photographs: grey
        /// rounded boulders through the forest wherever the mountainside is
        /// steep): the NatureManufacture forest stones at boulder size, sunk a
        /// third into the slope, beside the road where the ground beyond the
        /// shoulder is steeper than 1 in 2.5 - open or walled roadside only
        /// (a cut has its own rock), never on a deck or in a tunnel. Each is
        /// SOLID (a convex hull of its own mesh, named "Boulder" - the plant
        /// fit and PlantSolidAudit take it as a solid nothing may cross), and
        /// drawn by MeshForest with the cliffs, to the near forest's whole range.</summary>
        static void PlaceBoulders(List<Vector3> pts, Transform parent)
        {
            bouldersPlaced = 0;
            boulderSpots.Clear();
            rockSynced = false;
            rockDropped = 0;
            Physics.SyncTransforms();
            var kit = LoadMeshTreeKit();
            if (kit == null || boulderVars.Count == 0 || rsKind == null) return;
            var root = new GameObject("Boulders");
            root.transform.SetParent(parent, false);
            int n = pts.Count;
            float half = RoadWidth * 0.5f;
            for (int i = 2; i < n - 2; i += 2)
            {
                if (hasTunnels && tunnelIn[i]) continue;
                if (DeckCoversStation(i)) continue;
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    var kind = rsKind[s][i];
                    if (kind != Roadside.Open && kind != Roadside.Walled) continue;
                    float r1 = RHash(i, s, 301), r2 = RHash(s, i, 303), r3 = RHash(i + 11, s, 307);
                    if (r1 > 0.42f) continue;
                    // only the stones that LIE (longest axis level, so within
                    // ~25 degrees of a slope's surface): an upright one stood
                    // at the road edge like a monolith (SwissNC226A 8.4 km)
                    var pool = boulderLying.Count > 0 ? boulderLying : boulderVars;
                    int v = pool[(int)(r2 * 9973f) % pool.Count];
                    var vr = kit.variants[v];
                    float rad0 = BoulderRadius(vr);
                    // 0.9-2.4 m tall, no wider than the band allows
                    float sc = Mathf.Lerp(0.9f, 2.4f, r3 * r3) / Mathf.Max(vr.height, 0.1f);
                    float maxR = (BoulderFarE - BoulderNearE) * 0.5f;
                    if (rad0 * sc > maxR) sc = maxR / rad0;
                    float rad = rad0 * sc;
                    float e = Mathf.Lerp(BoulderNearE + rad, BoulderFarE - rad, r2);
                    // within 4 m of the edge nothing over 1.2 m tall
                    if (e - rad < 4f && vr.height * sc > 1.2f)
                    {
                        sc = 1.2f / Mathf.Max(vr.height, 0.1f);
                        rad = rad0 * sc;
                        e = Mathf.Lerp(BoulderNearE + rad, BoulderFarE - rad, r2);
                    }
                    Vector3 right = RightAt(pts, i);
                    Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
                    Vector3 p = pts[i] + right * (side * (half + e)) + fwd * ((r1 - 0.21f) * 6f);
                    // the nearest road there must be this one, this far away
                    if (!StageCorridor(p.x, p.z, half + BoulderFarE + 4f, out RoadFoot f)) continue;
                    if (StageKeepOut(p.x, p.z)) continue;
                    if (Mathf.Abs(f.station - i) > 6f || f.d - half < BoulderNearE + rad - 0.3f) continue;
                    if (OtherRoadNear(p.x, p.z, half + BoulderFarE + rad + 2f, i, out RoadFoot _)) continue;
                    // STEEP: the slope across 4 m
                    float gx = StageGroundHeightAt(p.x + 2f, p.z) - StageGroundHeightAt(p.x - 2f, p.z);
                    float gz = StageGroundHeightAt(p.x, p.z + 2f) - StageGroundHeightAt(p.x, p.z - 2f);
                    float slope = Mathf.Sqrt(gx * gx + gz * gz) / 4f;
                    if (slope < 0.4f) continue;
                    // seated on the LOWEST ground under it, a third sunk
                    float y = float.MaxValue;
                    for (int q = 0; q < 5; q++)
                    {
                        float ang = q * 1.2566f;
                        float qx = p.x + (q == 4 ? 0f : Mathf.Cos(ang) * rad * 0.8f), qz = p.z + (q == 4 ? 0f : Mathf.Sin(ang) * rad * 0.8f);
                        y = Mathf.Min(y, Mathf.Min(StageGroundHeightAt(qx, qz), StageLatticeY(qx, qz)));
                    }
                    y -= vr.height * sc * (0.15f + 0.15f * r1);   // sunk 15-30%
                    p.y = y;
                    // clear of a wall, a rail, a rock face and the last boulders
                    if (!RockClearOfSolids(p + Vector3.up * (vr.height * sc * 0.5f), rad + 0.3f)) continue;
                    bool crowded = false;
                    foreach (var q in boulderSpots) if ((q - p).sqrMagnitude < 9f) crowded = true;
                    if (crowded) continue;
                    boulderSpots.Add(p);
                    float yaw = r3 * 360f;
                    // THE SOLID: the stone's own convex hull, as MeshForest draws it
                    var go = new GameObject("Boulder");
                    cliffPlaces.Add(new CliffPlace { pos = p, yaw = yaw, scale = sc, v = v, r = rad, solid = go,
                                                     lift = vr.height * sc * 0.5f });
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = p;
                    go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    go.transform.localScale = new Vector3(sc, sc, sc);
                    var hull = new GameObject("Boulder");
                    hull.transform.SetParent(go.transform, false);
                    hull.transform.localPosition = vr.pre.GetColumn(3);
                    hull.transform.localRotation = vr.pre.rotation;
                    hull.transform.localScale = vr.pre.lossyScale;
                    var mc = hull.AddComponent<MeshCollider>();
                    mc.sharedMesh = vr.lods[0];
                    mc.convex = true;
                    mc.sharedMaterial = GetOrCreatePhysMat("WallPhys", 0.05f, 0.05f);
                    hull.layer = SolidLayer;
                    go.isStatic = hull.isStatic = true;
                    bouldersPlaced++;
                }
            }
            Physics.SyncTransforms();
            Log($"Boulders: {bouldersPlaced} on the steep slopes beside the road (solid, {boulderVars.Count} stone meshes); " +
                $"rock cuts: {talusN} talus stones and litter at the foot, crest lip {lipTris} tris with {lipPlants} plant spots, {reliefLedges} relief ledge spots.");
            reliefLedges = lipTris = lipPlants = talusN = 0;
        }

        static readonly List<Vector3> boulderSpots = new List<Vector3>();
        /// <summary>The boulder stones that lie: their model no taller than
        /// 0.8 x their widest level extent.</summary>
        static readonly List<int> boulderLying = new List<int>();

        /// <summary>Nothing built solid (a wall, rail, rock face, deck or
        /// pier) within <paramref name="r"/> of <paramref name="c"/>.</summary>
        static bool RockClearOfSolids(Vector3 c, float r, bool rockOk = false)
        {
            foreach (var col in Physics.OverlapSphere(c, r, ~0, QueryTriggerInteraction.Ignore))
            {
                string n = col.gameObject.name;
                if (n.StartsWith("Wall") || (!rockOk && n.StartsWith("BankColl")) || n.StartsWith("BridgeDeck") || n == "Pier") return false;
            }
            return true;
        }

        static float BoulderRadius(MeshForest.Variant v)
        {
            var b = v.lods[0].bounds;
            float r = 0f;
            for (int c = 0; c < 8; c++)
            {
                var q = v.pre.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z));
                r = Mathf.Max(r, new Vector2(q.x, q.z).magnitude);
            }
            return Mathf.Max(r, 0.1f);
        }

        /// <summary>The ledges' plants over one near chunk: grass and ferns,
        /// black cherry, now and then a young beech.</summary>
        /// <summary>A ledge plant's moves: toward the road first (out of the
        /// rock it was set into), then away.</summary>
        static readonly float[] LedgeSteps = { -0.4f, -0.8f, -1.2f, 1f };

        static void AddLedgePlants(MeshTreeKit kit, float ox, float oz, float size, List<Vector4> inst,
                                   List<Vector2> scaleVar, ref int added, ref float ySum)
        {
            if (rockLedges.Count == 0) return;
            for (int li = 0; li < rockLedges.Count; li++)
            {
                var p = rockLedges[li];
                if (p.x < ox || p.x >= ox + size || p.z < oz || p.z >= oz + size) continue;
                int kind = li < rockLedgeKind.Count ? rockLedgeKind[li] : 0;
                float r = RHash(Mathf.FloorToInt(p.x * 10f), Mathf.FloorToInt(p.z * 10f), 91);
                if (kind == 1) r = 0.55f + r * 0.45f;          // bushes and young trees
                else if (kind >= 2) r = r * 0.62f;            // mostly fern and grass, a bush now and then
                int[] pool; float s0, s1;
                if (r < 0.55f && kit.cover.Length > 0) { pool = kit.cover; s0 = 1.4f; s1 = 2.4f; }
                else if (r < 0.88f && kit.bushes.Length > 0) { pool = kit.bushes; s0 = 1.0f; s1 = 1.8f; }
                else if (kit.smallBeech.Length > 0) { pool = kit.smallBeech; s0 = 0.5f; s1 = 0.9f; }
                else continue;
                float r2 = RHash(Mathf.FloorToInt(p.z * 10f), Mathf.FloorToInt(p.x * 10f), 93);
                // NOT OUT THROUGH THE ROCK (PlantClear): a plant set into the face
                // comes forward onto it, then shrinks, then is a fern instead
                float lx = p.x, lz = p.z, ly = p.y - 0.05f, ls = Mathf.Lerp(s0, s1, r2);
                int lv = pool[(int)(r2 * 9973f) % pool.Length];
                float keepY = ly;
                if (FitPlant(PlantClass.Ledge, kit, ref lx, ref lz, ref ly, ref ls, ref lv, (px, pz, ps) => keepY, 0.4f,
                             new[] { new PlantSwap { pool = kit.cover, s0 = 0.8f, s1 = 1.6f } }, r2 * 360f,
                             steps: LedgeSteps) == PlantFit.Dropped) continue;
                inst.Add(new Vector4(lx, ly, lz, r2 * 360f));
                scaleVar.Add(new Vector2(ls, lv));
                ySum += p.y;
                added++;
            }
        }
    }
}
