using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// BAKED SKY LIGHT (the GT4 study, gap 3): how much of the sky each vertex
    /// of a stage sees, baked into its vertex colour, and used by PSX/Lit to
    /// scale the AMBIENT only (keyword PSX_SKYVIS). The sun keeps its own
    /// shadow maps (SunShadows: the sun map, its world-fixed dapple under
    /// leaves, the top-down sky map); what they cannot do is the hemisphere -
    /// a verge under a wall of forest, the foot of a rock cut, the floor of
    /// the wood, the land under a viaduct all saw as much sky as an open
    /// field ("looks like a golf course").
    ///
    /// What is measured, per vertex, over 16 bearings x 6 elevation bands
    /// (cosine-weighted about straight up, so a band's share is
    /// cos^2(lo) - cos^2(hi)):
    ///  * the TERRAIN and the STRUCTURES as a height field: every surface
    ///    mesh (road, kerbs, shoulders, near ground, fill tails, pads) sampled
    ///    at 1 m cell centres, every structure (guard walls, rock cuts and
    ///    their tops, decks) splatted as its MAX into the cell, and past the
    ///    built band the DEM (GroundHeightAt) on an 8 m grid out to 220 m. A
    ///    bearing's horizon takes the bands below it.
    ///  * the FOREST: every tree of the table as a crown sphere (its card's
    ///    width and height), each taking LeafK of the cells it covers. Leaves
    ///    are not stone, and the per-cell product saturates where crowns
    ///    stand behind one another (a sum of caps would not).
    /// vis = the cosine-weighted share left, floored at Floor, stored in
    /// colour.r (g, b, a = 255). An upright face (a wall's side) counts only
    /// the bearings in front of it and starts half a metre out of itself.
    ///
    /// PSX/Lit takes min(vis, the sky map's PSXSkyOpen) - the two estimate
    /// the same quantity, and min keeps the tunnel's tuned 0.22 under a roof
    /// instead of multiplying it into black. Per pixel that is one min; the
    /// value rides in the ambient's spare fourth component (no interpolator
    /// is added: PSX/Lit sits at target 2.5's eight).
    ///
    /// Gated per theme (Theme.bakedLight); PSX_BAKEDLIGHT=0/1 overrides for
    /// A/B builds. The keyword goes on a material only when EVERY renderer in
    /// the scene that wears it was baked (and on its season dresses);
    /// MakeMat clears it on every build, so a stage built without the bake
    /// never keeps a keyword over meshes with no colour.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        internal const string SkyVisKeyword = "PSX_SKYVIS";
        const float SkyCell = 1f;           // fine height-field cell (m)
        const int SkyTileN = 32;            // cells a tile side
        const float SkyCoarse = 8f;         // DEM grid past the built band (m)
        const float SkyReach = 220f;        // how far a bearing is marched (m)
        const int SkyAz = 16, SkyEl = 6;
        // what a crown takes of the cells it covers. 0.70 (first cut) left the
        // canopy floor at 0.36 and moved a noon frame ~8%: the dapple lets a
        // third of the SUN through under leaves, so the ambient is a quarter
        // of the light there and has to go nearly all the way to read.
        const float SkyLeafK = 0.85f;
        const float SkyFloor = 0.15f;       // never below: the ambient is the shade's only light
        const float SkyTreeReach = 36f;     // crowns further than this are left to the horizon
        const float SkyBias = 0.12f;        // eye height over the surface
        // Tree cards: softer, and never as dark as the floor they stand on -
        // the forest is the signed-off forest, only its depth is new.
        const float SkyCardStrength = 0.70f, SkyCardFloor = 0.40f;

        struct SkyCrown { public float x, z, cy, r, w; }
        static readonly List<SkyCrown> skyCrowns = new List<SkyCrown>();

        /// <summary>A forest tree's crown, noted where the table takes it.</summary>
        static void NoteSkyCrown(float x, float ground, float z, float w, float h, bool conifer)
        {
            skyCrowns.Add(new SkyCrown
            {
                x = x, z = z, w = w,
                r = (conifer ? 0.40f : 0.38f) * w,
                cy = ground + h * (conifer ? 0.55f : 0.62f),
            });
        }

        internal static bool BakedLightOn
        {
            get
            {
                string e = System.Environment.GetEnvironmentVariable("PSX_BAKEDLIGHT");
                if (e == "0") return false;
                if (e == "1") return true;
                return theme != null && theme.bakedLight;
            }
        }

        enum SkyRole { None, Surface, Structure, Card }

        /// <summary>Which baked role a generated mesh plays, by its name
        /// (after the scene's prefix). Surface = sampled at cell centres and
        /// baked; Structure = splatted as max and baked; Card = a forest
        /// chunk (baked, occludes only as crowns). Far ground, posts, sea and
        /// collider-only meshes are none.</summary>
        static SkyRole SkyRoleOf(string meshName)
        {
            string n = meshName.StartsWith(MeshPrefix) ? meshName.Substring(MeshPrefix.Length) : meshName;
            if (n.StartsWith("Coll") || n.Contains("Coll") || n.StartsWith("StageGroundF") || n.StartsWith("StageSea")
                || n.Contains("Post") || n.StartsWith("Tunnel")) return SkyRole.None;
            if (n.StartsWith("StageForest")) return SkyRole.Card;
            // A circuit's trees share ONE card mesh between every tree: a
            // colour on it would be one tree's, worn by all.
            if (n.StartsWith("TreeMesh")) return SkyRole.None;
            if (n.StartsWith("GroundMesh") || n.StartsWith("ApronMesh")) return SkyRole.Surface;
            if (n.StartsWith("WallMesh")) return SkyRole.Structure;
            if (n.StartsWith("RoadMesh") || n.StartsWith("KerbMesh") || n.StartsWith("RoadEdge")
                || n.StartsWith("StageGroundN") || n.StartsWith("FillTail") || n.StartsWith("EndPad")
                || n.StartsWith("ApexPad")) return SkyRole.Surface;
            if (n.StartsWith("StageWall") || n.StartsWith("StageBank") || n.StartsWith("BankTop")
                || n.StartsWith("BridgeDeck") || n.StartsWith("BridgeJoint") || n.StartsWith("RetainWall")
                || n.StartsWith("Guardrail")) return SkyRole.Structure;
            return SkyRole.None;
        }

        // ---- the height field --------------------------------------------
        static float skyX0, skyZ0, skyCX0, skyCZ0;
        static int skyTilesX, skyTilesZ, skyCellsX, skyCellsZ, skyCoarseX, skyCoarseZ;
        static float[][] skySurf, skyStruct;
        static float[] skyCoarseH;

        static float[] SkyTile(float[][] tiles, int cx, int cz, bool make, float fill)
        {
            if (cx < 0 || cz < 0 || cx >= skyCellsX || cz >= skyCellsZ) return null;
            int t = (cz / SkyTileN) * skyTilesX + cx / SkyTileN;
            var a = tiles[t];
            if (a == null && make)
            {
                a = new float[SkyTileN * SkyTileN];
                for (int i = 0; i < a.Length; i++) a[i] = fill;
                tiles[t] = a;
            }
            return a;
        }

        static float SkyGet(float[][] tiles, int cx, int cz, float none)
        {
            if (cx < 0 || cz < 0 || cx >= skyCellsX || cz >= skyCellsZ) return none;
            var a = tiles[(cz / SkyTileN) * skyTilesX + cx / SkyTileN];
            return a == null ? none : a[(cz % SkyTileN) * SkyTileN + cx % SkyTileN];
        }

        static void SkyMax(float[][] tiles, int cx, int cz, float h, float fill)
        {
            var a = SkyTile(tiles, cx, cz, true, fill);
            if (a == null) return;
            int i = (cz % SkyTileN) * SkyTileN + cx % SkyTileN;
            if (float.IsNaN(a[i]) || h > a[i]) a[i] = h;
        }

        static float SkyCoarseAt(float x, float z)
        {
            float fx = Mathf.Clamp((x - skyCX0) / SkyCoarse, 0f, skyCoarseX - 1.001f);
            float fz = Mathf.Clamp((z - skyCZ0) / SkyCoarse, 0f, skyCoarseZ - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz;
            int i = iz * skyCoarseX + ix;
            float a = skyCoarseH[i] + (skyCoarseH[i + 1] - skyCoarseH[i]) * tx;
            float b = skyCoarseH[i + skyCoarseX] + (skyCoarseH[i + skyCoarseX + 1] - skyCoarseH[i + skyCoarseX]) * tx;
            return a + (b - a) * tz;
        }

        /// <summary>The built surface at (x, z): bilinear over cell centres,
        /// NaN where no surface mesh was rasterised.</summary>
        static float SkySurfAt(float x, float z)
        {
            float fx = (x - skyX0) / SkyCell - 0.5f, fz = (z - skyZ0) / SkyCell - 0.5f;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            float s00 = SkyGet(skySurf, ix, iz, float.NaN), s10 = SkyGet(skySurf, ix + 1, iz, float.NaN);
            float s01 = SkyGet(skySurf, ix, iz + 1, float.NaN), s11 = SkyGet(skySurf, ix + 1, iz + 1, float.NaN);
            float w00 = (1 - tx) * (1 - tz), w10 = tx * (1 - tz), w01 = (1 - tx) * tz, w11 = tx * tz;
            float sum = 0f, ws = 0f;
            if (!float.IsNaN(s00)) { sum += s00 * w00; ws += w00; }
            if (!float.IsNaN(s10)) { sum += s10 * w10; ws += w10; }
            if (!float.IsNaN(s01)) { sum += s01 * w01; ws += w01; }
            if (!float.IsNaN(s11)) { sum += s11 * w11; ws += w11; }
            return ws > 1e-4f ? sum / ws : float.NaN;
        }

        /// <summary>The highest thing at (x, z): the built surface (or the
        /// DEM past it) and any structure standing in that cell.</summary>
        static float SkyHeightAt(float x, float z)
        {
            float s = SkySurfAt(x, z);
            if (float.IsNaN(s)) s = SkyCoarseAt(x, z);
            int cx = Mathf.FloorToInt((x - skyX0) / SkyCell), cz = Mathf.FloorToInt((z - skyZ0) / SkyCell);
            float st = SkyGet(skyStruct, cx, cz, float.NegativeInfinity);
            return st > s ? st : s;
        }

        static void SkyRasterSurface(Vector3 a, Vector3 b, Vector3 c)
        {
            float denom = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denom) < 1e-5f) return;   // edge-on in plan: a structure's job
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
            int ix0 = Mathf.CeilToInt((minX - skyX0) / SkyCell - 0.5f), ix1 = Mathf.FloorToInt((maxX - skyX0) / SkyCell - 0.5f);
            int iz0 = Mathf.CeilToInt((minZ - skyZ0) / SkyCell - 0.5f), iz1 = Mathf.FloorToInt((maxZ - skyZ0) / SkyCell - 0.5f);
            for (int iz = iz0; iz <= iz1; iz++)
            {
                float pz = skyZ0 + (iz + 0.5f) * SkyCell;
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    float px = skyX0 + (ix + 0.5f) * SkyCell;
                    float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / denom;
                    float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / denom;
                    float l3 = 1f - l1 - l2;
                    if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                    SkyMax(skySurf, ix, iz, l1 * a.y + l2 * b.y + l3 * c.y, float.NaN);
                }
            }
        }

        static void SkyRasterStructure(Vector3 a, Vector3 b, Vector3 c)
        {
            float e = Mathf.Max((b - a).magnitude, Mathf.Max((c - a).magnitude, (c - b).magnitude));
            int n = Mathf.Clamp(Mathf.CeilToInt(e / (SkyCell * 0.4f)), 1, 400);
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n - i; j++)
                {
                    Vector3 p = a + (b - a) * ((float)i / n) + (c - a) * ((float)j / n);
                    SkyMax(skyStruct, Mathf.FloorToInt((p.x - skyX0) / SkyCell), Mathf.FloorToInt((p.z - skyZ0) / SkyCell),
                           p.y, float.NegativeInfinity);
                }
        }

        // ---- the forest as crowns ----------------------------------------
        const float SkyCrownCell = 12f;
        static int[] skyCrownStart, skyCrownIdx;
        static int skyCrownGX, skyCrownGZ;
        static SkyCrown[] skyCrownArr;

        static void SkyIndexCrowns()
        {
            skyCrownArr = skyCrowns.ToArray();
            skyCrownGX = Mathf.Max(1, Mathf.CeilToInt(skyCellsX * SkyCell / SkyCrownCell));
            skyCrownGZ = Mathf.Max(1, Mathf.CeilToInt(skyCellsZ * SkyCell / SkyCrownCell));
            var count = new int[skyCrownGX * skyCrownGZ + 1];
            var cellOf = new int[skyCrownArr.Length];
            for (int i = 0; i < skyCrownArr.Length; i++)
            {
                int gx = Mathf.Clamp(Mathf.FloorToInt((skyCrownArr[i].x - skyX0) / SkyCrownCell), 0, skyCrownGX - 1);
                int gz = Mathf.Clamp(Mathf.FloorToInt((skyCrownArr[i].z - skyZ0) / SkyCrownCell), 0, skyCrownGZ - 1);
                cellOf[i] = gz * skyCrownGX + gx;
                count[cellOf[i] + 1]++;
            }
            for (int i = 1; i < count.Length; i++) count[i] += count[i - 1];
            skyCrownStart = (int[])count.Clone();
            skyCrownIdx = new int[skyCrownArr.Length];
            var fillAt = (int[])count.Clone();
            for (int i = 0; i < skyCrownArr.Length; i++) skyCrownIdx[fillAt[cellOf[i]]++] = i;
        }

        static readonly float[] SkyBandW = BandWeights();
        static float[] BandWeights()
        {
            var w = new float[SkyEl];
            for (int e = 0; e < SkyEl; e++)
            {
                float lo = e * (90f / SkyEl) * Mathf.Deg2Rad, hi = (e + 1) * (90f / SkyEl) * Mathf.Deg2Rad;
                w[e] = Mathf.Cos(lo) * Mathf.Cos(lo) - Mathf.Cos(hi) * Mathf.Cos(hi);
            }
            return w;
        }

        static readonly float[] SkySteps = MarchSteps();
        static float[] MarchSteps()
        {
            var l = new List<float>();
            for (float r = 0.6f; r <= SkyReach; r *= 1.3f) l.Add(r);
            return l.ToArray();
        }

        /// <summary>The cosine-weighted share of the sky a point sees.
        /// <paramref name="T"/> is SkyAz*SkyEl scratch.</summary>
        static float SkyVisAt(Vector3 p, Vector3 n, SkyRole role, float[] T)
        {
            float nh = Mathf.Sqrt(n.x * n.x + n.z * n.z);
            bool upright = role != SkyRole.Card && nh > 0.8f;
            float dnx = nh > 1e-4f ? n.x / nh : 0f, dnz = nh > 1e-4f ? n.z / nh : 0f;
            // out of its own 1 m cells (a diagonal and a bit): a cut face's
            // column holds the rock above it, a wall's the coping
            float sx = p.x + (upright ? dnx * 1.5f : 0f), sz = p.z + (upright ? dnz * 1.5f : 0f);
            float y0 = p.y;
            if (!upright)
            {
                // A vertex dug under the road (the slab) or a tree's sunk
                // foot answers for the surface over it; a deck far overhead
                // does not lift the land under the viaduct onto it.
                float s = SkySurfAt(p.x, p.z);
                if (!float.IsNaN(s) && s > y0 && s - y0 < 1.5f) y0 = s;
            }
            y0 += SkyBias;
            const float bandRad = Mathf.PI * 0.5f / SkyEl;
            for (int i = 0; i < T.Length; i++) T[i] = 1f;

            // THE HORIZON, bearing by bearing
            for (int a = 0; a < SkyAz; a++)
            {
                float phi = (a + 0.5f) * (2f * Mathf.PI / SkyAz);
                float dx = Mathf.Cos(phi), dz = Mathf.Sin(phi);
                if (upright && dx * dnx + dz * dnz < 0.05f) { for (int e = 0; e < SkyEl; e++) T[a * SkyEl + e] = -1f; continue; }
                float maxT = 0f;
                for (int k = 0; k < SkySteps.Length; k++)
                {
                    float r = SkySteps[k];
                    float h = SkyHeightAt(sx + dx * r, sz + dz * r);
                    float t = (h - y0) / r;
                    if (t > maxT) maxT = t;
                }
                if (maxT <= 0f) continue;
                float hor = Mathf.Atan(maxT);
                float c2h = 1f / (1f + maxT * maxT);   // cos^2 of the horizon
                for (int e = 0; e < SkyEl; e++)
                {
                    float lo = e * bandRad, hi = lo + bandRad;
                    if (hor >= hi) T[a * SkyEl + e] = 0f;
                    else if (hor > lo)
                    {
                        float cl = Mathf.Cos(lo), ch = Mathf.Cos(hi);
                        T[a * SkyEl + e] = (c2h - ch * ch) / Mathf.Max(cl * cl - ch * ch, 1e-6f);
                    }
                }
            }

            // THE CROWNS
            if (skyCrownArr != null && skyCrownArr.Length > 0)
            {
                int g0x = Mathf.FloorToInt((p.x - SkyTreeReach - skyX0) / SkyCrownCell), g1x = Mathf.FloorToInt((p.x + SkyTreeReach - skyX0) / SkyCrownCell);
                int g0z = Mathf.FloorToInt((p.z - SkyTreeReach - skyZ0) / SkyCrownCell), g1z = Mathf.FloorToInt((p.z + SkyTreeReach - skyZ0) / SkyCrownCell);
                g0x = Mathf.Max(g0x, 0); g0z = Mathf.Max(g0z, 0); g1x = Mathf.Min(g1x, skyCrownGX - 1); g1z = Mathf.Min(g1z, skyCrownGZ - 1);
                const float azRad = 2f * Mathf.PI / SkyAz;
                for (int gz = g0z; gz <= g1z; gz++)
                    for (int gx = g0x; gx <= g1x; gx++)
                    {
                        int cell = gz * skyCrownGX + gx;
                        for (int q = skyCrownStart[cell]; q < skyCrownStart[cell + 1]; q++)
                        {
                            var c = skyCrownArr[skyCrownIdx[q]];
                            float dx = c.x - p.x, dz = c.z - p.z, dy = c.cy - y0;
                            float hd2 = dx * dx + dz * dz;
                            if (hd2 > SkyTreeReach * SkyTreeReach) continue;
                            // a card does not shade itself
                            if (role == SkyRole.Card && hd2 < 0.30f * c.w * c.w) continue;
                            float hd = Mathf.Sqrt(hd2);
                            if (upright && dx * dnx + dz * dnz < -0.5f * c.r) continue;
                            float D = Mathf.Sqrt(hd2 + dy * dy);
                            float elc = Mathf.Atan2(dy, hd);
                            float alpha = D <= c.r ? Mathf.PI * 0.5f : Mathf.Asin(c.r / D);
                            if (elc + alpha <= 0f) continue;
                            float phc = Mathf.Atan2(dz, dx);
                            if (phc < 0f) phc += 2f * Mathf.PI;
                            // the crown's box in (bearing, elevation); its
                            // bearing spread widens toward the zenith
                            float spreadAz = Mathf.Min(Mathf.PI, alpha / Mathf.Max(Mathf.Cos(Mathf.Clamp(elc, -1.5f, 1.5f)), 0.15f));
                            float el0 = Mathf.Max(0f, elc - alpha), el1 = Mathf.Min(Mathf.PI * 0.5f, elc + alpha);
                            if (el1 <= el0) continue;
                            float az0 = phc - spreadAz, az1 = phc + spreadAz;
                            int e0 = Mathf.Clamp((int)(el0 / bandRad), 0, SkyEl - 1), e1 = Mathf.Clamp((int)(el1 / bandRad), 0, SkyEl - 1);
                            int a0 = Mathf.FloorToInt(az0 / azRad - 0.5f), a1 = Mathf.CeilToInt(az1 / azRad - 0.5f);
                            // a disc fills pi/4 of its box
                            const float disc = 0.785f;
                            for (int a = a0; a <= a1; a++)
                            {
                                float ac = (a + 0.5f) * azRad;
                                float ov = Mathf.Min(ac + azRad * 0.5f, az1) - Mathf.Max(ac - azRad * 0.5f, az0);
                                if (ov <= 0f) continue;
                                float fa = Mathf.Min(ov / azRad, 1f);
                                int aw = ((a % SkyAz) + SkyAz) % SkyAz;
                                for (int e = e0; e <= e1; e++)
                                {
                                    float lo = e * bandRad, hi = lo + bandRad;
                                    float oe = Mathf.Min(hi, el1) - Mathf.Max(lo, el0);
                                    if (oe <= 0f) continue;
                                    float f = fa * Mathf.Min(oe / bandRad, 1f) * disc;
                                    int ti = aw * SkyEl + e;
                                    if (T[ti] > 0f) T[ti] *= 1f - SkyLeafK * f;
                                }
                            }
                        }
                    }
            }

            float vis = 0f, wsum = 0f;
            for (int a = 0; a < SkyAz; a++)
                for (int e = 0; e < SkyEl; e++)
                {
                    float t = T[a * SkyEl + e];
                    if (t < 0f) continue;          // behind an upright face
                    vis += t * SkyBandW[e]; wsum += SkyBandW[e];
                }
            return wsum > 0f ? vis / wsum : 1f;
        }

        // ---- the pass -------------------------------------------------------
        class SkyJob { public MeshFilter mf; public Mesh mesh; public SkyRole role; public float[] vis; public Vector3[] wv, wn; }

        static readonly StringBuilder skyReport = new StringBuilder();

        static void SkyCheck(bool ok, string what, string got)
        {
            string line = (ok ? "  ok   " : "  FAIL ") + what + (got != null ? "  (got " + got + ")" : "");
            skyReport.AppendLine(line);
            Log("BAKED LIGHT" + line);
        }

        /// <summary>Bake every stage mesh's sky visibility into its colour
        /// and key the materials. After the forest and the homes (it reads
        /// both), before the season dress is attached.</summary>
        static void BakeStageSkyLight(Transform trackRoot)
        {
            try { BakeSkyLightInner(); }
            catch (System.Exception e)
            {
                // A failed bake must not fail the venue, and must not leave a
                // keyword over meshes that never got their colour.
                Log("WARN: baked sky light failed, venue built without it: " + e);
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                    foreach (var mat in r.sharedMaterials)
                        if (mat != null && mat.IsKeywordEnabled(SkyVisKeyword)) { mat.DisableKeyword(SkyVisKeyword); EditorUtility.SetDirty(mat); }
                foreach (var entry in seasonEntries)
                    if (entry.variants != null)
                        foreach (var v in entry.variants)
                            if (v != null && v.IsKeywordEnabled(SkyVisKeyword)) { v.DisableKeyword(SkyVisKeyword); EditorUtility.SetDirty(v); }
                skySurf = skyStruct = null; skyCoarseH = null; skyCrownArr = null;
            }
        }

        static void BakeSkyLightInner()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            skyReport.Length = 0;
            skyReport.AppendLine("baked sky light: " + (track != null ? track.id : "?") + ", " + skyCrowns.Count + " crowns");

            // WHO: every generated mesh in the scene with a role
            var jobs = new List<SkyJob>();
            var all = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include);
            // a mesh two filters share has no one place to be baked for
            var wearers = new Dictionary<Mesh, int>();
            foreach (var mf in all) if (mf.sharedMesh != null) wearers[mf.sharedMesh] = (wearers.TryGetValue(mf.sharedMesh, out int c) ? c : 0) + 1;
            var b = new Bounds(); bool any = false;
            foreach (var mf in all)
            {
                var m = mf.sharedMesh;
                if (m == null || wearers[m] > 1) continue;
                var role = SkyRoleOf(m.name);
                if (role == SkyRole.None) continue;
                var j = new SkyJob { mf = mf, mesh = m, role = role };
                var v = m.vertices; var nn = m.normals;
                var M = mf.transform.localToWorldMatrix;
                j.wv = new Vector3[v.Length]; j.wn = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    j.wv[i] = M.MultiplyPoint3x4(v[i]);
                    j.wn[i] = nn != null && nn.Length == v.Length ? M.MultiplyVector(nn[i]).normalized : Vector3.up;
                    if (!any) { b = new Bounds(j.wv[i], Vector3.zero); any = true; } else b.Encapsulate(j.wv[i]);
                }
                jobs.Add(j);
            }
            if (!any) { SkyCheck(false, "something to bake", "no stage meshes"); return; }

            // THE FIELD: fine tiles over the built band, the DEM past it
            skyX0 = Mathf.Floor(b.min.x) - 4f; skyZ0 = Mathf.Floor(b.min.z) - 4f;
            skyCellsX = Mathf.CeilToInt((b.size.x + 8f) / SkyCell); skyCellsZ = Mathf.CeilToInt((b.size.z + 8f) / SkyCell);
            skyCellsX = (skyCellsX + SkyTileN - 1) / SkyTileN * SkyTileN; skyCellsZ = (skyCellsZ + SkyTileN - 1) / SkyTileN * SkyTileN;
            skyTilesX = skyCellsX / SkyTileN; skyTilesZ = skyCellsZ / SkyTileN;
            skySurf = new float[skyTilesX * skyTilesZ][]; skyStruct = new float[skyTilesX * skyTilesZ][];
            skyCX0 = skyX0 - SkyReach; skyCZ0 = skyZ0 - SkyReach;
            skyCoarseX = Mathf.CeilToInt((skyCellsX * SkyCell + 2f * SkyReach) / SkyCoarse) + 2;
            skyCoarseZ = Mathf.CeilToInt((skyCellsZ * SkyCell + 2f * SkyReach) / SkyCoarse) + 2;
            skyCoarseH = new float[skyCoarseX * skyCoarseZ];
            for (int iz = 0; iz < skyCoarseZ; iz++)
                for (int ix = 0; ix < skyCoarseX; ix++)
                    skyCoarseH[iz * skyCoarseX + ix] = stageDemLoaded ? StageDemY(skyCX0 + ix * SkyCoarse, skyCZ0 + iz * SkyCoarse)
                                                                       : GroundHeightAt(skyCX0 + ix * SkyCoarse, skyCZ0 + iz * SkyCoarse);
            long tris = 0;
            foreach (var j in jobs)
            {
                if (j.role == SkyRole.Card) continue;
                for (int s = 0; s < j.mesh.subMeshCount; s++)
                {
                    var idx = j.mesh.GetTriangles(s);
                    for (int k = 0; k + 2 < idx.Length; k += 3)
                    {
                        Vector3 p0 = j.wv[idx[k]], p1 = j.wv[idx[k + 1]], p2 = j.wv[idx[k + 2]];
                        // A structure's TOPS (a rock cut's top, a deck, a
                        // coping) are surfaces too: sampled at cell centres,
                        // not splatted as the cell's max - a max over a 1 m
                        // cell of a sloping top stands above the top's own
                        // vertices and shades them from nothing. Its steep
                        // faces (a wall's side, a cut) are what the max is for.
                        Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
                        bool flat = fn.sqrMagnitude > 1e-10f && Mathf.Abs(fn.normalized.y) > 0.6f;
                        if (j.role == SkyRole.Surface || flat) SkyRasterSurface(p0, p1, p2);
                        else SkyRasterStructure(p0, p1, p2);
                    }
                    tris += idx.Length / 3;
                }
            }
            SkyIndexCrowns();
            long fieldMs = clock.ElapsedMilliseconds;

            // THE BAKE
            long verts = 0;
            foreach (var j in jobs)
            {
                var jj = j;
                jj.vis = new float[jj.wv.Length];
                Parallel.For(0, jj.wv.Length, () => new float[SkyAz * SkyEl], (i, st, T) =>
                {
                    float v = SkyVisAt(jj.wv[i], jj.wn[i], jj.role, T);
                    jj.vis[i] = jj.role == SkyRole.Card
                        ? Mathf.Max(1f - SkyCardStrength * (1f - v), SkyCardFloor)
                        : Mathf.Max(v, SkyFloor);
                    return T;
                }, _ => { });
                verts += jj.wv.Length;
            }
            long bakeMs = clock.ElapsedMilliseconds - fieldMs;

            // THE KEYWORD: on a material only if every renderer wearing it
            // was baked
            var baked = new HashSet<Mesh>();
            foreach (var j in jobs) baked.Add(j.mesh);
            var users = new Dictionary<Material, bool>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mf = r.GetComponent<MeshFilter>();
                bool isBaked = mf != null && mf.sharedMesh != null && baked.Contains(mf.sharedMesh);
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    users[mat] = (users.TryGetValue(mat, out bool was) ? was : true) && isBaked;
                }
            }
            var keyed = new List<Material>();
            foreach (var kv in users)
            {
                var mat = kv.Key;
                if (!kv.Value || mat.shader == null || mat.shader.name != "PSX/Lit" || !mat.name.StartsWith(MeshPrefix)) continue;
                if (mat.IsKeywordEnabled("PSX_ATLAS_RECT") || mat.IsKeywordEnabled("PSX_FURNITURE") || mat.IsKeywordEnabled("PSX_FACADE")) continue;
                keyed.Add(mat);
            }
            var keyedSet = new HashSet<Material>(keyed);
            int variantsKeyed = 0;
            foreach (var mat in keyed) { mat.EnableKeyword(SkyVisKeyword); EditorUtility.SetDirty(mat); }
            foreach (var entry in seasonEntries)
            {
                if (entry.baseMat == null || !keyedSet.Contains(entry.baseMat) || entry.variants == null) continue;
                foreach (var v in entry.variants)
                    if (v != null && v != entry.baseMat) { v.EnableKeyword(SkyVisKeyword); EditorUtility.SetDirty(v); variantsKeyed++; }
            }

            // THE COLOURS: only on meshes whose every material took the key
            // (a colour nothing reads is download for nothing)
            long bytes = 0; int meshesColoured = 0, meshesSkipped = 0;
            // the colour stream as it ships (4 B a vertex), for a compressed-
            // size estimate outside Unity (the build is Brotli)
            var dump = new MemoryStream();
            foreach (var j in jobs)
            {
                var r = j.mf.GetComponent<Renderer>();
                bool allKeyed = r != null && r.sharedMaterials.Length > 0;
                if (r != null) foreach (var mat in r.sharedMaterials) if (mat == null || !keyedSet.Contains(mat)) allKeyed = false;
                if (!allKeyed) { meshesSkipped++; j.vis = null; continue; }
                var cols = new Color32[j.vis.Length];
                for (int i = 0; i < cols.Length; i++)
                    cols[i] = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(j.vis[i]) * 255f), 255, 255, 255);
                j.mesh.colors32 = cols;
                foreach (var c in cols) { dump.WriteByte(c.r); dump.WriteByte(c.g); dump.WriteByte(c.b); dump.WriteByte(c.a); }
                EditorUtility.SetDirty(j.mesh);
                bytes += cols.Length * 4L;
                meshesColoured++;
            }
            AssetDatabase.SaveAssets();
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_bakedlight_colours.bin"), dump.ToArray());

            // THE REPORT and its checks
            skyReport.AppendLine($"  field {skyCellsX}x{skyCellsZ} m, {tris} triangles, {fieldMs} ms; bake {verts} vertices in {bakeMs} ms");
            skyReport.AppendLine($"  keyed {keyed.Count} materials (+{variantsKeyed} season dresses); coloured {meshesColoured} meshes, {meshesSkipped} left plain; +{bytes / 1024} KB of vertex colour (4 B a vertex, before compression)");
            foreach (var mat in keyed) skyReport.AppendLine("    " + mat.name);
            SkyStats(jobs);
            // every renderer wearing a keyed material carries a colour a vertex
            int bad = 0, wearing = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                bool wears = false;
                foreach (var mat in r.sharedMaterials) if (mat != null && mat.IsKeywordEnabled(SkyVisKeyword)) wears = true;
                if (!wears) continue;
                wearing++;
                var mf = r.GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                if (m == null || m.colors32 == null || m.colors32.Length != m.vertexCount) bad++;
            }
            SkyCheck(bad == 0 && wearing > 0, "every renderer wearing a sky-baked material carries a colour a vertex", $"{bad} of {wearing} without");
            skyReport.AppendLine($"  total {clock.ElapsedMilliseconds} ms");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_bakedlight.txt"), skyReport.ToString());
            skySurf = skyStruct = null; skyCoarseH = null; skyCrownArr = null;
        }

        /// <summary>Distributions per role, and the three checks the look
        /// rests on: open ground keeps its sky, the canopy floor loses it,
        /// the wall's and the cut's foot is darker than the open verge.</summary>
        static void SkyStats(List<SkyJob> jobs)
        {
            var groups = new Dictionary<string, List<float>>();
            void Add(string k, float v) { if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<float>(); l.Add(v); }
            var open = new List<float>(); var under = new List<float>(); var foot = new List<float>(); var verge = new List<float>();
            var cardLow = new List<float>(); var cardHigh = new List<float>();
            foreach (var j in jobs)
            {
                if (j.vis == null) continue;
                string n = j.mesh.name.Substring(Mathf.Min(MeshPrefix.Length, j.mesh.name.Length));
                string fam = n.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_', 'n', '-');
                if (n.StartsWith("StageGround")) fam = n.Substring(0, Mathf.Min(12, n.Length));
                for (int i = 0; i < j.vis.Length; i++)
                {
                    Add(fam, j.vis[i]);
                    Vector3 p = j.wv[i];
                    if (n.StartsWith("StageGroundN") && j.wn[i].y > 0.95f)
                    {
                        SkyNearestCrown(p, out float nearest, out bool overhead);
                        float st = SkyGetNear(p);
                        if (overhead) under.Add(j.vis[i]);
                        else if (nearest > 25f * 25f && st < p.y + 0.5f) open.Add(j.vis[i]);
                    }
                    if (n.StartsWith("RoadEdge") && j.wn[i].y > 0.7f)
                    {
                        // the foot of a wall or a cut: a structure cell 1.5 m off rising 0.5 m over it
                        bool atFoot = false;
                        for (int a = 0; a < 8 && !atFoot; a++)
                        {
                            float ph = a * Mathf.PI / 4f;
                            float h = SkyGet(skyStruct, Mathf.FloorToInt((p.x + Mathf.Cos(ph) * 1.2f - skyX0) / SkyCell),
                                             Mathf.FloorToInt((p.z + Mathf.Sin(ph) * 1.2f - skyZ0) / SkyCell), float.NegativeInfinity);
                            if (h > p.y + 0.5f) atFoot = true;
                        }
                        (atFoot ? foot : verge).Add(j.vis[i]);
                    }
                    if (j.role == SkyRole.Card)
                    {
                        float gy = SkySurfAt(p.x, p.z);
                        if (float.IsNaN(gy)) gy = SkyCoarseAt(p.x, p.z);
                        if (p.y < gy + 1f) cardLow.Add(j.vis[i]); else if (p.y > gy + 9f) cardHigh.Add(j.vis[i]);
                    }
                }
            }
            foreach (var kv in groups)
            {
                var l = kv.Value; l.Sort();
                skyReport.AppendLine($"  {kv.Key,-14} n {l.Count,7}  mean {Mean(l):0.00}  p10 {l[l.Count / 10]:0.00}  p50 {l[l.Count / 2]:0.00}  p90 {l[l.Count * 9 / 10]:0.00}");
            }
            open.Sort(); under.Sort();
            float openMed = open.Count > 0 ? open[open.Count / 2] : -1f;
            SkyCheck(open.Count > 20 && openMed >= 0.88f, "open ground keeps its sky (flat ground vertex, no crown within 25 m, no structure): median >= 0.88",
                     $"median {openMed:0.00} over {open.Count}");
            float underMean = Mean(under);
            SkyCheck(under.Count > 20 && underMean <= 0.60f, "the canopy floor loses it (a crown overhead): mean <= 0.60",
                     $"mean {underMean:0.00} over {under.Count}");
            float footMean = Mean(foot), vergeMean = Mean(verge);
            SkyCheck(foot.Count > 20 && footMean <= vergeMean - 0.08f, "the foot of a wall or a cut is darker than the open verge by >= 0.08",
                     $"foot {footMean:0.00} ({foot.Count}) vs verge {vergeMean:0.00} ({verge.Count})");
            if (cardLow.Count > 0)
                SkyCheck(Mean(cardLow) < Mean(cardHigh) - 0.05f, "a tree card's foot is darker than its crown (depth into the wood)",
                         $"foot {Mean(cardLow):0.00} vs crown {Mean(cardHigh):0.00}");
        }

        /// <summary>The squared plan distance to the nearest crown within
        /// 25 m (MaxValue past it), and whether one is overhead.</summary>
        static void SkyNearestCrown(Vector3 p, out float nearest2, out bool overhead)
        {
            nearest2 = float.MaxValue; overhead = false;
            if (skyCrownArr == null || skyCrownArr.Length == 0) return;
            int g0x = Mathf.Max(0, Mathf.FloorToInt((p.x - 25f - skyX0) / SkyCrownCell)), g1x = Mathf.Min(skyCrownGX - 1, Mathf.FloorToInt((p.x + 25f - skyX0) / SkyCrownCell));
            int g0z = Mathf.Max(0, Mathf.FloorToInt((p.z - 25f - skyZ0) / SkyCrownCell)), g1z = Mathf.Min(skyCrownGZ - 1, Mathf.FloorToInt((p.z + 25f - skyZ0) / SkyCrownCell));
            for (int gz = g0z; gz <= g1z; gz++)
                for (int gx = g0x; gx <= g1x; gx++)
                {
                    int cell = gz * skyCrownGX + gx;
                    for (int q = skyCrownStart[cell]; q < skyCrownStart[cell + 1]; q++)
                    {
                        var c = skyCrownArr[skyCrownIdx[q]];
                        float dx = c.x - p.x, dz = c.z - p.z, d2 = dx * dx + dz * dz;
                        if (d2 < nearest2) nearest2 = d2;
                        if (d2 < 0.64f * c.r * c.r && c.cy > p.y) overhead = true;
                    }
                }
        }

        static float SkyGetNear(Vector3 p) =>
            SkyGet(skyStruct, Mathf.FloorToInt((p.x - skyX0) / SkyCell), Mathf.FloorToInt((p.z - skyZ0) / SkyCell), float.NegativeInfinity);

        static float Mean(List<float> l)
        {
            if (l.Count == 0) return float.NaN;
            double s = 0; foreach (var v in l) s += v; return (float)(s / l.Count);
        }
    }
}
