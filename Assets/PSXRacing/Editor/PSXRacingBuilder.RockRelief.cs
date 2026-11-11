using System.Collections.Generic;
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
        /// <summary>OFF (owner 2026-11-08: the relief read as "a dirt cliff that
        /// would be held up by tree roots", then as stacked slabs - not the
        /// Parkway's jagged gneiss). Kept for rock MESHES along the cuts next.</summary>
        const bool RockReliefOn = false;
        const float ReliefAlong = 0.6f, ReliefUp = 0.4f, ReliefProud = 0.15f;
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
            // Strata 0.8-1.5 m high, each in blocks 2-5 m long; every block a
            // plane of its own (set back 0.2-1.4 m, tilted both ways up to
            // half a metre across it) so its face takes the light at its own
            // angle - the fractured, faceted rock of the Parkway's cuts, not a
            // bumpy wall. A few blocks stand proud; joints between blocks.
            float yj = y + (RNoise(a * 0.17f, 0.5f, seed) - 0.5f) * 0.8f;
            float layerH = 0.8f + RHash(Mathf.FloorToInt(yj / 1.15f), 5, seed) * 0.7f;
            int L = Mathf.FloorToInt(yj / layerH);
            float yIn = yj / layerH - L;
            float blockLen = 2.0f + RHash(L, 1, seed) * 3.0f;
            float off = RHash(L, 2, seed) * blockLen;
            float q = (a + off) / blockLen;
            int B = Mathf.FloorToInt(q);
            float xIn = q - B;
            float hb = RHash(L, B, seed + 11);
            float depth = hb > 0.88f ? -ReliefProud : 0.2f + hb * 1.2f;
            depth += (RHash(L, B, seed + 23) - 0.5f) * 1.0f * (xIn - 0.5f);   // tilted along
            depth += (RHash(L, B, seed + 29) * 0.6f + 0.1f) * yIn;            // leans back: a ledge below
            float edge = Mathf.Min(xIn, 1f - xIn) * blockLen;
            if (edge < 0.22f) depth += 0.35f * (1f - edge / 0.22f);           // a joint
            depth += (RNoise(a * 2.1f, y * 2.1f, seed + 3) - 0.5f) * 0.10f;   // grain
            return Mathf.Max(depth, -ReliefProud);
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
                               float[] distAt, int[] chordT0, int[] chordT1, Vector3[] outw, int R, int seed)
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

            for (int k = 1; k < R; k++)
            {
                if (!faced[k - 1] || !faced[k]) continue;
                float lenA = Vector3.Distance(toe[k - 1], plinthTop[k - 1]) + Vector3.Distance(plinthTop[k - 1], crest[k - 1]);
                float lenB = Vector3.Distance(toe[k], plinthTop[k]) + Vector3.Distance(plinthTop[k], crest[k]);
                Vector3 step = toe[k] - toe[k - 1]; step.y = 0f;
                int na = Mathf.Max(1, Mathf.CeilToInt(step.magnitude / ReliefAlong));
                int nu = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(lenA, lenB) / ReliefUp));
                Vector3 inward = outw[k - 1] + outw[k]; inward.y = 0f; inward.Normalize();
                var gp = new List<Vector3>((na + 1) * (nu + 1));
                var guv = new List<Vector2>((na + 1) * (nu + 1));
                for (int ia = 0; ia <= na; ia++)
                {
                    float t = ia / (float)na;
                    float a = Mathf.Lerp(distAt[k - 1], distAt[k], t);
                    float toeY = Mathf.Lerp(toe[k - 1].y, toe[k].y, t);
                    for (int iu = 0; iu <= nu; iu++)
                    {
                        float u = iu / (float)nu;
                        var p = Vector3.Lerp(ReliefProfile(toe[k - 1], plinthTop[k - 1], crest[k - 1], u),
                                             ReliefProfile(toe[k], plinthTop[k], crest[k], u), t);
                        float taper = Mathf.SmoothStep(0f, 1f, u / 0.12f) * Mathf.SmoothStep(0f, 1f, (1f - u) / 0.12f);
                        p += inward * (ReliefDepth(a, p.y, seed) * taper);
                        gp.Add(p);
                        guv.Add(new Vector2(a / 4.5f, (p.y - toeY) / 4.5f));
                    }
                }
                int row = nu + 1;
                // FACETED: every cell its own four corners, so the normals
                // SaveMesh computes are the cell's own - the block edges stay
                // edges instead of being smoothed round.
                for (int ia = 0; ia < na; ia++)
                    for (int iu = 0; iu < nu; iu++)
                    {
                        int g0 = ia * row + iu, g1 = g0 + 1, h0 = g0 + row, h1 = h0 + 1;
                        int a0 = verts.Count;
                        verts.Add(gp[g0]); uvs.Add(guv[g0]);
                        verts.Add(gp[g1]); uvs.Add(guv[g1]);
                        verts.Add(gp[h0]); uvs.Add(guv[h0]);
                        verts.Add(gp[h1]); uvs.Add(guv[h1]);
                        int a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                        QuadFacingSkipFlat(verts, tris, a0, a1, b1, b0, -inward);
                        // a LEDGE: this cell faces up (the top of a stratum's block)
                        var n = Vector3.Cross(verts[a1] - verts[a0], verts[b0] - verts[a0]);
                        if (n.y < 0f) n = -n;
                        n.Normalize();
                        float uc = (iu + 0.5f) / nu;
                        if (n.y > 0.72f && uc > 0.15f && uc < 0.9f
                            && RHash(ia + k * 131, iu, seed + 17) < 0.45f)
                            rockLedges.Add((verts[a0] + verts[a1] + verts[b0] + verts[b1]) * 0.25f);
                    }
            }
            reliefChords += chords;
            reliefTris += (tris.Count - t0) / 3;
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
