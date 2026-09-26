using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Finds where a shell's head and tail lamps actually are, and seats them
    /// on its outer surface.
    ///
    /// Owner, 2026-09-26: "Headlights and taillights are not correctly seated
    /// on models... Sometimes they are off of the car, in the car, in front of
    /// or behind the cars." CarLights put every lens at the same fractions of
    /// the body's bounding box - 40% of its height, 62% of its half-width,
    /// 3 cm past its furthest point - and a box knows nothing of a car. The
    /// furthest point is a bumper lip or a spoiler, so the lens hung in the
    /// air; a raked nose left it in front of the panel; a traffic car (no
    /// CarBody) fell back to its SHRUNK, raised collider and buried it.
    ///
    /// THE SHEET SAYS WHERE THE GLASS IS. Every pack model paints its lamps on
    /// the texture: pale glass at the nose, red at the tail. The finder
    /// samples the shell's forward- and rearward-facing surface on a 3 cm grid
    /// through its UVs and keeps the samples that read as lamp. Two things
    /// keep paint out: a lamp texel is the SAME on every livery of a model (a
    /// livery repaints the body, not the glass), and a lamp is a patch a hand
    /// or two wide, not a panel. Each side's patch is trimmed to its core and
    /// the two sides averaged into one mirrored pair.
    ///
    /// THEN SEATED: a ray from outside the car at the patch's height and
    /// offset finds the OUTER surface there, so the lens sits on the skin -
    /// not at the mean of a curved patch, which is inside it.
    ///
    /// No patch (pop-up lamps are shut on the bonnet top, a hidden-lamp grille
    /// is black): the same ray at the usual lamp line, so the lens is at least
    /// on the car.
    /// </summary>
    public static class CarLampFinder
    {
        public struct Lamp
        {
            public Vector3 pos, normal;
            public Vector2 size;
            public string how;
        }

        /// <summary>Sample spacing over the surface, metres.</summary>
        const float Grid = 0.03f;
        /// <summary>Reach of the lamp region from each end, as a fraction of
        /// the car's length.</summary>
        const float EndFrac = 0.2f;
        /// <summary>A side's lamp patch smaller than this (m^2) is a stray
        /// texel; larger is a panel.</summary>
        const float MinArea = 0.0015f, MaxArea = 0.2f;

        /// <summary>Debug: every zone sample's UV, which end, and whether it
        /// read as lamp - CarLampPreview paints them onto the atlas.</summary>
        public static List<(Vector2 uv, int dir, bool lamp)> DiagMarks;

        struct Tri { public Vector3 a, b, c, n; public Vector2 ua, ub, uc; public float area; }

        class Sheet { public Color32[] px; public int w, h; }


        public static void Find(CarModelDef d, out Lamp head, out Lamp tail, List<string> log = null)
        {
            var tris = RigTris(d);
            var sheets = LoadSheets(d);
            Bounds b = new Bounds(tris.Count > 0 ? tris[0].a : Vector3.zero, Vector3.zero);
            foreach (var t in tris) { b.Encapsulate(t.a); b.Encapsulate(t.b); b.Encapsulate(t.c); }

            paint = sheets.Count == 1 ? PaintOf(tris, sheets[0]) : (Color?)null;
            head = FindEnd(tris, sheets, b, d, +1, out string headWhy);
            tail = FindEnd(tris, sheets, b, d, -1, out string tailWhy);
            log?.Add($"{d.key,-15} head {head.how,-7} ({head.pos.x:0.00}, {head.pos.y:0.00}, {head.pos.z:0.00}) " +
                     $"{head.size.x:0.00}x{head.size.y:0.00} {headWhy} | tail {tail.how,-7} " +
                     $"({tail.pos.x:0.00}, {tail.pos.y:0.00}, {tail.pos.z:0.00}) {tail.size.x:0.00}x{tail.size.y:0.00} {tailWhy}");
        }

        /// <summary>Write the lamps into the def.</summary>
        public static void Apply(CarModelDef d, List<string> log = null)
        {
            Find(d, out var head, out var tail, log);
            d.headLamp = head.pos; d.headLampNormal = head.normal; d.headLampSize = head.size;
            d.tailLamp = tail.pos; d.tailLampNormal = tail.normal; d.tailLampSize = tail.size;
        }

        // ------------------------------------------------------------------
        static Lamp FindEnd(List<Tri> tris, List<Sheet> sheets, Bounds b, CarModelDef d, int dir, out string why)
        {
            float len = b.size.z;
            float endZ = dir > 0 ? b.max.z : b.min.z;
            float yTop = dir > 0 ? Mathf.Max(d.cowlY + 0.08f, b.min.y + b.size.y * 0.55f) : d.roofY - 0.08f;
            // Head glass is above the bumper (a third of the body's height up
            // at least; the '69 Charger's white bumper lamps read as heads).
            float yLow = b.min.y + b.size.y * (dir > 0 ? 0.3f : 0.15f);
            // Off the centreline by a third of the half-width: a Viper's
            // stripes and an E30's kidneys are pale and constant too.
            float xMin = Mathf.Max(0.12f, b.extents.x * 0.35f);

            // HEAD glass is tested on every livery at once; TAIL glass on
            // each livery ALONE, keeping the one that shows a lamp-sized red
            // patch on both sides. A pack that makes its liveries by
            // hue-shifting the whole sheet turns the tail lamps blue on a blue
            // car - red lamps exist on one livery only - and a red car's
            // livery shows a whole red panel, which the size cap throws out.
            List<(Vector3 c, Vector2 size, float area)> found = null;
            why = "";
            if (dir > 0) found = Detect(tris, sheets, -1, dir, endZ, len, yLow, yTop, xMin, out why);
            else
                for (int k = 0; k < sheets.Count; k++)
                {
                    var f = Detect(tris, sheets, k, dir, endZ, len, yLow, yTop, xMin, out string w);
                    float area = 0f; foreach (var e in f) area += e.area;
                    float best = 0f; if (found != null) foreach (var e in found) best += e.area;
                    if (found == null || f.Count > found.Count || (f.Count == found.Count && f.Count > 0 && area < best))
                    { found = f; why = w + (sheets.Count > 1 ? " livery " + k : ""); }
                }
            if (found == null) found = new List<(Vector3 c, Vector2 size, float area)>();

            Lamp lamp = default;
            float x, y; Vector2 lensSize;
            if (found.Count > 0)
            {
                x = 0f; y = 0f; lensSize = Vector2.zero;
                foreach (var f in found) { x += Mathf.Abs(f.c.x); y += f.c.y; lensSize += f.size; }
                x /= found.Count; y /= found.Count; lensSize /= found.Count;
                lamp.how = "sheet";
            }
            else
            {
                // The usual lamp line, as CarLights has always guessed it.
                x = b.extents.x * (dir > 0 ? 0.62f : 0.66f);
                y = b.min.y + b.size.y * (dir > 0 ? 0.40f : 0.52f);
                float lw = Mathf.Clamp(b.size.x * 0.26f, 0.22f, 0.55f);
                lensSize = dir > 0 ? new Vector2(lw, lw * 0.62f) : new Vector2(lw * 0.9f, lw * 0.5f);
                lamp.how = "surface";
            }
            lensSize = new Vector2(Mathf.Clamp(lensSize.x, 0.12f, 0.5f), Mathf.Clamp(lensSize.y, 0.07f, 0.28f));

            // SEAT IT: the outer skin at (x, y), from outside the end of the car.
            Vector3 from = new Vector3(x, y, endZ + dir * 1f);
            Vector3 ray = new Vector3(0f, 0f, -dir);
            if (Raycast(tris, from, ray, out Vector3 hit, out Vector3 n))
            {
                if (Vector3.Dot(n, ray) > 0f) n = -n;
                lamp.pos = hit;
                lamp.normal = n;
            }
            else
            {
                lamp.pos = new Vector3(x, y, endZ);
                lamp.normal = new Vector3(0f, 0f, dir);
                lamp.how += "?";
            }
            lamp.size = lensSize;
            return lamp;
        }

        static List<(Vector3 c, Vector2 size, float area)> Detect(List<Tri> tris, List<Sheet> sheets, int only, int dir,
            float endZ, float len, float yLow, float yTop, float xMin, out string why)
        {
            var pts = new List<Vector3>[2] { new List<Vector3>(), new List<Vector3>() };
            var wts = new List<float>[2] { new List<float>(), new List<float>() };
            foreach (var t in tris)
            {
                // Facing out of the end, and more out of it than up: a raked
                // bonnet or boot lid faces forward too, and it is paint.
                if (t.n.z * dir < 0.25f || t.n.z * dir < 0.55f * t.n.y) continue;
                Vector3 cen = (t.a + t.b + t.c) / 3f;
                if ((endZ - cen.z) * dir > len * EndFrac + 0.1f) continue;
                int s = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(t.area * 2f) / Grid), 1, 60);
                float subArea = t.area / (s * s);
                for (int i = 0; i < s; i++)
                    for (int j = 0; j < s - i; j++)
                        for (int up = 0; up < 2; up++)
                        {
                            if (up == 1 && i + j >= s - 1) continue;
                            float u = (i + (up == 0 ? 1f / 3f : 2f / 3f)) / s;
                            float v = (j + (up == 0 ? 1f / 3f : 2f / 3f)) / s;
                            float w = 1f - u - v;
                            Vector3 p = t.a * w + t.b * u + t.c * v;
                            if ((endZ - p.z) * dir > len * EndFrac) continue;
                            if (p.y < yLow || p.y > yTop || Mathf.Abs(p.x) < xMin) continue;
                            Vector2 uv = t.ua * w + t.ub * u + t.uc * v;
                            bool lampHere = IsLamp(sheets, uv, dir, only);
                            if (DiagMarks != null) DiagMarks.Add((uv, dir, lampHere));
                            if (!lampHere) continue;
                            int side = p.x > 0f ? 1 : 0;
                            pts[side].Add(p);
                            wts[side].Add(subArea);
                        }
            }

            var found = new List<(Vector3 c, Vector2 size, float area)>();
            var sideWhy = new string[2];
            for (int side = 0; side < 2; side++)
            {
                if (Core(pts[side], wts[side], out var c, out var size, out float area))
                {
                    if (area < MinArea) sideWhy[side] = $"tiny {area * 1e4f:0}cm2";
                    else if (area > MaxArea) sideWhy[side] = $"panel {area:0.00}m2";
                    else { found.Add((c, size, area)); sideWhy[side] = $"{area * 1e4f:0}cm2"; }
                }
                else sideWhy[side] = "none";
            }
            why = "[L " + sideWhy[0] + ", R " + sideWhy[1] + "]";
            return found;
        }

        /// <summary>Weighted core of a patch: the mean, trimmed twice to the
        /// samples within 30 cm of it (a white badge on the other end of the
        /// bumper is not the lamp), and the 10-90% spread as the lens size.</summary>
        static bool Core(List<Vector3> p, List<float> w, out Vector3 c, out Vector2 size, out float area)
        {
            c = Vector3.zero; size = Vector2.zero; area = 0f;
            if (p.Count == 0) return false;
            var keep = new List<int>();
            for (int i = 0; i < p.Count; i++) keep.Add(i);
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 sum = Vector3.zero; float ws = 0f;
                foreach (int i in keep) { sum += p[i] * w[i]; ws += w[i]; }
                if (ws <= 0f) return false;
                c = sum / ws;
                if (pass == 2) break;
                var next = new List<int>();
                foreach (int i in keep) if ((p[i] - c).magnitude < 0.3f) next.Add(i);
                if (next.Count == 0) break;
                keep = next;
            }
            foreach (int i in keep) area += w[i];
            size = new Vector2(Spread(p, w, keep, 0), Spread(p, w, keep, 1));
            return true;
        }

        static float Spread(List<Vector3> p, List<float> w, List<int> keep, int axis)
        {
            var s = new List<(float v, float w)>();
            float total = 0f;
            foreach (int i in keep) { s.Add((p[i][axis], w[i])); total += w[i]; }
            s.Sort((a, b) => a.v.CompareTo(b.v));
            float lo = s[0].v, hi = s[s.Count - 1].v, acc = 0f;
            bool loSet = false;
            foreach (var e in s)
            {
                acc += e.w;
                if (!loSet && acc >= total * 0.08f) { lo = e.v; loSet = true; }
                if (acc >= total * 0.92f) { hi = e.v; break; }
            }
            return (hi - lo) + Grid;
        }

        /// <summary>Lamp glass: pale and nearly colourless at the nose, red at
        /// the tail - and the same on every livery, which paint is not.</summary>
        /// <summary>Lamp glass at this UV: head glass pale on every livery (a
        /// livery repaints the body; a hue-shifted sheet turns grey glass
        /// grey), tail glass red on livery <paramref name="only"/>.</summary>
        static bool IsLamp(List<Sheet> sheets, Vector2 uv, int dir, int only)
        {
            if (sheets.Count == 0) return false;
            if (only >= 0) return LampColour(Texel(sheets[only], uv), dir, sheets.Count == 1);
            for (int k = 0; k < sheets.Count; k++)
                if (!LampColour(Texel(sheets[k], uv), dir, sheets.Count == 1)) return false;
            return true;
        }

        /// <summary>A single-livery model's paint, read off its doors: the one
        /// colour the glass cannot be, when there is no second livery to tell
        /// the two apart.</summary>
        static Color? paint;

        static Color? PaintOf(List<Tri> tris, Sheet sheet)
        {
            var bins = new Dictionary<int, (Color sum, float w)>();
            foreach (var t in tris)
            {
                if (Mathf.Abs(t.n.x) < 0.7f) continue;
                Vector3 cen = (t.a + t.b + t.c) / 3f;
                if (cen.y < 0.35f || cen.y > 0.95f) continue;
                Color c = Texel(sheet, (t.ua + t.ub + t.uc) / 3f);
                int key = Mathf.RoundToInt(c.r * 8) * 81 + Mathf.RoundToInt(c.g * 8) * 9 + Mathf.RoundToInt(c.b * 8);
                bins.TryGetValue(key, out var e);
                bins[key] = (e.sum + c * t.area, e.w + t.area);
            }
            float best = 0f; Color? col = null;
            foreach (var e in bins.Values) if (e.w > best) { best = e.w; col = e.sum / e.w; }
            return col;
        }

        static bool LampColour(Color c, int dir, bool onlySheet)
        {
            if (onlySheet && paint.HasValue)
            {
                Color pc = paint.Value;
                if (Mathf.Max(Mathf.Abs(c.r - pc.r), Mathf.Max(Mathf.Abs(c.g - pc.g), Mathf.Abs(c.b - pc.b))) < 0.16f)
                    return false;
            }
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (dir > 0)
            {
                float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                float sat = max > 0f ? (max - min) / max : 0f;
                // One sheet has no second livery to rule a pale body out; the
                // paint test above does that, so the glass may be a mid grey
                // (the Civic's is).
                return lum > (onlySheet ? 0.5f : 0.58f) && sat < 0.3f;
            }
            return c.r > 0.3f && c.r > 1.7f * c.g && c.r > 1.7f * c.b;
        }

        static Color Texel(Sheet s, Vector2 uv)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * s.w), 0, s.w - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * s.h), 0, s.h - 1);
            return s.px[y * s.w + x];
        }

        static List<Sheet> LoadSheets(CarModelDef d)
        {
            var list = new List<Sheet>();
            if (d.skinMaterials == null) return list;
            foreach (var m in d.skinMaterials)
            {
                var tex = m != null ? m.mainTexture : null;
                string path = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                // From the file, not the asset: the imported texture is not
                // readable, and a 256 cap may have been applied to it.
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(t); continue; }
                list.Add(new Sheet { px = t.GetPixels32(), w = t.width, h = t.height });
                Object.DestroyImmediate(t);
            }
            return list;
        }

        /// <summary>The body's triangles in the car's frame (+Z out of the
        /// nose, tyre contact at y 0), exactly as CarBody poses the mesh.</summary>
        static List<Tri> RigTris(CarModelDef d)
        {
            var list = new List<Tri>();
            var mesh = d.bodyMesh;
            if (mesh == null) return list;
            var m = Matrix4x4.TRS(new Vector3(0f, d.bodyYOffset, d.bodyZOffset),
                                  Quaternion.Euler(0f, d.bodyYaw, 0f), Vector3.one);
            var v = mesh.vertices;
            var nrm = mesh.normals;
            var uv = mesh.uv;
            bool hasN = nrm != null && nrm.Length == v.Length;
            bool hasUV = uv != null && uv.Length == v.Length;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var t = mesh.GetTriangles(sub);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    float area = cross.magnitude * 0.5f;
                    if (area < 1e-7f) continue;
                    Vector3 n = cross.normalized;
                    // The importer's normals say which way is OUT; the winding
                    // of a converted rip may not.
                    if (hasN)
                    {
                        Vector3 vn = m.MultiplyVector(nrm[t[i]] + nrm[t[i + 1]] + nrm[t[i + 2]]);
                        if (Vector3.Dot(vn, n) < 0f) n = -n;
                    }
                    list.Add(new Tri
                    {
                        a = a, b = b, c = c, n = n, area = area,
                        ua = hasUV ? uv[t[i]] : Vector2.zero,
                        ub = hasUV ? uv[t[i + 1]] : Vector2.zero,
                        uc = hasUV ? uv[t[i + 2]] : Vector2.zero,
                    });
                }
            }
            return list;
        }

        static bool Raycast(List<Tri> tris, Vector3 o, Vector3 dir, out Vector3 hit, out Vector3 n)
        {
            hit = n = Vector3.zero;
            float best = float.MaxValue;
            foreach (var t in tris)
            {
                Vector3 e1 = t.b - t.a, e2 = t.c - t.a;
                Vector3 p = Vector3.Cross(dir, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1f / det;
                Vector3 s = o - t.a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(dir, q) * inv;
                if (v < 0f || u + v > 1f) continue;
                float d = Vector3.Dot(e2, q) * inv;
                if (d > 0f && d < best) { best = d; hit = o + dir * d; n = t.n; }
            }
            return best < float.MaxValue;
        }
    }
}
