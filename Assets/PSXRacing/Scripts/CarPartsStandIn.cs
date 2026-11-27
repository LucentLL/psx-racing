using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE STAND-IN (carparts, test only): no car has 5A opening parts yet, so
    /// the game side is proven on a def made in code for the pilot's game shell
    /// <see cref="Key"/> (a fixed-lamp notchback with a plain bonnet: no
    /// pop-ups, no lens variant) from ITS OWN measured numbers (cowlZ, cowlY,
    /// noseZ, roofY, lowerBodyWidth, wheelbase, the body's bounds). Thin boxes
    /// over the bonnet, the trunk lid and both doors, hinged where the real
    /// car's would be (bonnet at the cowl, lid at the rear glass base, doors at
    /// the A-pillar); an Engine_Mount over the front axle (longitudinal); two
    /// Kit_ empties (Kit_Battery at its own seat, Kit_Radiator) so the bay kit
    /// can test both placement paths. No open body: its boxes sit over the
    /// intact car, so the body swap is skipped for it.
    ///
    /// Drawn in a flat checker with STAND-IN written on it, and ONLY behind the
    /// LAB row "OPENING: STAND-IN PREVIEW" (default OFF; env
    /// PSX_PARTS_STANDIN=1) or a test's <see cref="Override"/>: it never appears
    /// in the owner's game by default.
    /// </summary>
    public static class CarPartsStandIn
    {
        /// <summary>The pilot's game shell (Silvia_S13_HD_v07 -> coupe_88).</summary>
        public const string Key = "coupe_88";

        /// <summary>-1 = the environment (PSX_PARTS_STANDIN=1), 0 off, 1 on (tests).</summary>
        public static int Override = -1;

        public static bool On => Override >= 0 ? Override == 1 : Preview;

        /// <summary>LAB "carpartspreview" (OPENING: STAND-IN PREVIEW), default
        /// OFF; env PSX_PARTS_STANDIN=1. The only way to see the feature in an exe
        /// before a pilot car exists (coordinator, 2026-11-26: the owner cannot set
        /// environment variables).</summary>
        public static bool Preview = System.Environment.GetEnvironmentVariable("PSX_PARTS_STANDIN") == "1";

        /// <summary>The LAB row's apply: switched off, the stand-in's parts shut.</summary>
        public static void ApplyLab()
        {
            if (!Preview) CarParts.ShutAllNow();
        }

        /// <summary>Tests only: the shell the stand-in is built on instead of
        /// <see cref="Key"/> (null = Key), and a copy of the closed body as a
        /// pretend open body, so the body swap and its readers can be checked
        /// before any 5A car exists.</summary>
        public static string KeyOverride;
        public static bool TestOpenBody;

        static string ActiveKey => string.IsNullOrEmpty(KeyOverride) ? Key : KeyOverride;

        static CarModelDef standIn;
        static CarModelDef standInFrom;
        static readonly List<string> report = new List<string>();

        /// <summary>How the numbers were taken (the hand-back).</summary>
        public static IReadOnlyList<string> Report => report;

        /// <summary>The stand-in for this shell, or null (not its shell, or off).</summary>
        public static CarModelDef For(CarModelDef real)
        {
            if (!On || real == null || real.key != ActiveKey || real.HasPopUps || real.bodyMesh == null) return null;
            if (standIn != null && standInFrom == real && (standIn.bodyOpenMesh != null) == TestOpenBody) return standIn;
            Forget();
            standIn = Build(real);
            standInFrom = real;
            return standIn;
        }

        public static bool IsStandIn(CarModelDef d) => d != null && d == standIn;

        /// <summary>Drop the built stand-in (its meshes too); the next ask builds it again.</summary>
        public static void Forget()
        {
            if (standIn == null) return;
            foreach (var op in standIn.openingParts) if (op.mesh != null) Object.DestroyImmediate(op.mesh);
            if (standIn.bodyOpenMesh != null) Object.DestroyImmediate(standIn.bodyOpenMesh);
            Object.DestroyImmediate(standIn.gameObject);
            standIn = null;
            standInFrom = null;
        }

        // =====================================================================

        const float Skin = 0.025f;    // box thickness
        const float Proud = 0.012f;   // how far a box stands off the surface it covers

        static CarModelDef Build(CarModelDef d)
        {
            report.Clear();
            var go = new GameObject("STAND-IN parts " + d.key) { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            var s = go.AddComponent<CarModelDef>();
            s.key = d.key;
            s.displayName = "STAND-IN " + d.key;
            s.bodyMesh = d.bodyMesh;
            s.bodyYaw = d.bodyYaw;
            s.bodyYOffset = d.bodyYOffset;
            s.bodyZOffset = d.bodyZOffset;
            s.wheelbase = d.wheelbase;
            s.trackWidth = d.trackWidth;
            s.wheelRadius = d.wheelRadius;
            s.lowerBodyWidth = d.lowerBodyWidth;
            s.cowlZ = d.cowlZ; s.cowlY = d.cowlY; s.noseZ = d.noseZ; s.roofY = d.roofY;
            s.colliderCenter = d.colliderCenter; s.colliderSize = d.colliderSize;
            s.coverMesh = d.coverMesh; s.coverUpMesh = d.coverUpMesh; s.coverMaterial = d.coverMaterial;
            if (TestOpenBody)
            {
                // (tests) a copy of the closed body standing in for Body + jambs + bay
                s.bodyOpenMesh = Object.Instantiate(d.bodyMesh);
                s.bodyOpenMesh.name = d.bodyMesh.name + "_TESTOPEN";
                s.bodyOpenMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            // ---- the car's frame (+Z out of the nose, +X right, y 0 on the
            // contact patch: the frame cowlZ etc. are measured in) -----------
            var yaw = Quaternion.Euler(0f, d.bodyYaw, 0f);
            var off = new Vector3(0f, d.bodyYOffset, d.bodyZOffset);
            var mb = d.bodyMesh.bounds;
            // the body's box in the car frame (yaw 0 or 180: an axis swap)
            var c = yaw * mb.center + off;
            var e = mb.extents;
            float tailZ = c.z - e.z, noseZ = c.z + e.z, len = 2f * e.z;
            float half = (d.lowerBodyWidth > 0f ? d.lowerBodyWidth : 2f * e.x) * 0.5f;
            float frontAxle = d.wheelbase * 0.5f;

            var probe = Probe.Make(d.bodyMesh, yaw, off);
            report.Add($"STAND-IN on {d.key}: body {2f * e.x:0.000} x {2f * e.y:0.000} x {len:0.000} m (car frame z {tailZ:0.000}..{noseZ:0.000}), " +
                       $"cowl z {d.cowlZ:0.000} y {d.cowlY:0.000}, nose {d.noseZ:0.000}, roof {d.roofY:0.000}, lower width {d.lowerBodyWidth:0.000}, " +
                       $"wheelbase {d.wheelbase:0.000}; mesh {(probe != null ? "readable (heights probed)" : "NOT readable (bounds only)")}");

            var parts = new List<CarModelDef.OpeningPart>();

            // ---- HOOD: hinged across the car at the cowl, front edge rises --
            {
                float z1 = d.cowlZ + 0.02f, z0 = Mathf.Min(d.noseZ, noseZ) - 0.20f;
                float w = half - 0.12f;
                float yBack = probe != null ? probe.TopAt(z1 + 0.06f, w * 0.6f) : d.cowlY;
                float yFront = probe != null ? probe.TopAt(z0 + 0.06f, w * 0.6f) : d.cowlY - 0.12f;
                var a = new Vector3(-w, yBack + Proud, z1);
                var b = new Vector3(w, yFront + Proud, z0);
                var mesh = Slab("STANDIN_Hood", yaw, off, a, b, Axis.Y, true);
                var pivot = new Vector3(0f, yBack + Proud, z1);
                parts.Add(Part("Hood", mesh, yaw, off, pivot, Vector3.right, 55f, new Vector3(0f, yFront, z0), Vector3.up));
                report.Add($"  Hood: z {z0:0.000}..{z1:0.000}, top {yFront:0.000}..{yBack:0.000}, hinge across at z {z1:0.000} (the cowl), opens 55 deg");
            }

            // ---- TRUNK: no rear-glass measurement exists. The hinge line is
            // taken from the body's BOUNDS: a notchback's deck is about the
            // rear 24% of its length (TrunkDeckFraction), so the lid's front
            // edge (the rear glass base) is at tailZ + 0.24 * length; the
            // lid ends 12 cm short of the tail (the bumper). ---------------
            {
                float z1 = tailZ + TrunkDeckFraction * len, z0 = tailZ + 0.12f;
                float w = half - 0.14f;
                float yFront = probe != null ? probe.TopAt(z1 - 0.06f, w * 0.6f) : d.cowlY + 0.04f;
                float yBack = probe != null ? probe.TopAt(z0 + 0.06f, w * 0.6f) : d.cowlY;
                var a = new Vector3(-w, yFront + Proud, z1);
                var b = new Vector3(w, yBack + Proud, z0);
                var mesh = Slab("STANDIN_Trunk", yaw, off, a, b, Axis.Y, false, false);
                var pivot = new Vector3(0f, yFront + Proud, z1);
                parts.Add(Part("Trunk", mesh, yaw, off, pivot, Vector3.right, 60f, new Vector3(0f, yBack, z0), Vector3.up));
                string glass = probe != null ? $"; the mesh's own rear-glass base (first rise > 8 cm above the deck going forward) is at z {probe.GlassBase(z0, yBack, w * 0.6f):0.000}" : "";
                report.Add($"  Trunk: hinge across at z {z1:0.000} = tail {tailZ:0.000} + {TrunkDeckFraction:0.00} x length {len:0.000} (from the body's bounds){glass}; " +
                           $"lid to z {z0:0.000}, top {yBack:0.000}..{yFront:0.000}, opens 60 deg");
            }

            // ---- DOORS: hinged near-vertical at the A-pillar (just behind the
            // cowl), rear edge swings out. A two-door's door is ~47% of the
            // wheelbase long; sill to the belt (cowl height + 5 cm). ---------
            foreach (int side in new[] { -1, 1 })
            {
                float z1 = d.cowlZ - 0.08f, z0 = z1 - DoorFraction * d.wheelbase;
                float y0 = 0.30f, y1 = d.cowlY + 0.05f;
                float x = probe != null ? probe.SideAt(side, (z0 + z1) * 0.5f, y0 + 0.15f, y1 - 0.05f) : half;
                float xo = side * (x + Proud);
                var a = new Vector3(xo, y0, z0);
                var b = new Vector3(xo + side * Skin, y1, z1);
                var mesh = Slab(side < 0 ? "STANDIN_Door_L" : "STANDIN_Door_R", yaw, off, a, b, Axis.X, side > 0);
                var pivot = new Vector3(xo, (y0 + y1) * 0.5f, z1);
                parts.Add(Part(side < 0 ? "Door_L" : "Door_R", mesh, yaw, off, pivot, Vector3.up, 62f,
                               new Vector3(xo, (y0 + y1) * 0.5f, z0), new Vector3(side, 0f, 0f)));
                report.Add($"  {(side < 0 ? "Door_L" : "Door_R")}: z {z0:0.000}..{z1:0.000} ({DoorFraction:0.00} x wheelbase), y {y0:0.00}..{y1:0.000}, " +
                           $"skin x {xo:0.000}, hinge vertical at the A-pillar z {z1:0.000}, opens 62 deg");
            }
            s.openingParts = parts.ToArray();

            // ---- ENGINE_MOUNT over the front axle, longitudinal: the flywheel
            // face 45 cm behind the axle, the crank 42 cm up, +Y forward (to
            // the timing end), +Z up the bore. -----------------------------
            var mountCar = new Vector3(0f, 0.42f, frontAxle - 0.45f);
            var rotCar = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            s.hasEngineMount = true;
            s.engineMountPos = ToMesh(mountCar, yaw, off);
            s.engineMountRot = Quaternion.Inverse(yaw) * rotCar;
            s.engineLayout = EngineLayout.Longitudinal;
            s.engineClearBox = new[] { 0.80f, 0.20f, half - 0.20f, half - 0.20f, d.cowlY - 0.46f, 0.26f };
            s.engineDownpipe = ToMesh(new Vector3(0.28f, 0.22f, mountCar.z - 0.05f), yaw, off);
            s.engineDownpipeSide = 1;   // the downpipe on the mount's +X side
            report.Add($"  Engine_Mount (car frame) {mountCar} longitudinal, clear box {string.Join(", ", System.Array.ConvertAll(s.engineClearBox, f => f.ToString("0.00")))}");

            // ---- the bay kit's two seats: the battery at its own empty, the
            // radiator's bottom-edge centre behind the grille. ---------------
            var bat = new Vector3(-(half - 0.28f), 0.56f, frontAxle + 0.22f);
            var rad = new Vector3(0f, 0.30f, Mathf.Min(d.noseZ, noseZ) - 0.34f);
            s.kitMounts = new[]
            {
                new CarModelDef.KitMount { name = "Kit_Battery", pos = ToMesh(bat, yaw, off), rot = Quaternion.identity },
                new CarModelDef.KitMount { name = "Kit_Radiator", pos = ToMesh(rad, yaw, off), rot = Quaternion.identity },
            };
            report.Add($"  Kit_Battery (car frame) {bat}, Kit_Radiator {rad}");
            return s;
        }

        /// <summary>A notchback's trunk deck as a share of the body's length (the stand-in's hinge line).</summary>
        public const float TrunkDeckFraction = 0.24f;
        /// <summary>A two-door's door length as a share of the wheelbase.</summary>
        public const float DoorFraction = 0.47f;

        static Vector3 ToMesh(Vector3 car, Quaternion yaw, Vector3 off) => Quaternion.Inverse(yaw) * (car - off);

        /// <summary>A part in mesh space, its axis signed so a positive angle
        /// moves <paramref name="farCar"/> (the edge away from the hinge)
        /// toward <paramref name="opensToward"/> (car frame).</summary>
        static CarModelDef.OpeningPart Part(string name, Mesh mesh, Quaternion yaw, Vector3 off, Vector3 pivotCar,
                                            Vector3 axisCar, float deg, Vector3 farCar, Vector3 opensToward)
        {
            var r = farCar - pivotCar;
            var moved = Quaternion.AngleAxis(20f, axisCar) * r - r;
            if (Vector3.Dot(moved, opensToward) < 0f) axisCar = -axisCar;
            var inv = Quaternion.Inverse(yaw);
            return new CarModelDef.OpeningPart
            {
                name = name, mesh = mesh, pivot = ToMesh(pivotCar, yaw, off),
                axis = (inv * axisCar).normalized, openDeg = deg,
            };
        }

        enum Axis { X, Y }

        /// <summary>A thin box between two car-frame corners (the thin axis is
        /// <paramref name="thin"/>: Y = a lid, sloped from corner a's height
        /// to b's; X = a door), in mesh space, with each big face mapped 0..1
        /// so the STAND-IN label spans it.</summary>
        static Mesh Slab(string name, Quaternion yaw, Vector3 off, Vector3 a, Vector3 b, Axis thin, bool flipU, bool flipV = false)
        {
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                bool hx = (i & 1) != 0, hy = (i & 2) != 0, hz = (i & 4) != 0;
                Vector3 p;
                if (thin == Axis.Y)
                {
                    // x across, z along; y = the top sloping a -> b along z, Skin thick
                    float x = hx ? b.x : a.x, z = hz ? b.z : a.z;
                    float top = hz ? b.y : a.y;
                    p = new Vector3(x, hy ? top : top - Skin, z);
                }
                else
                {
                    p = new Vector3(hx ? b.x : a.x, hy ? b.y : a.y, hz ? b.z : a.z);
                }
                corners[i] = ToMesh(p, yaw, off);
            }
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            // six faces, each its own four corners (flat normals)
            int[][] faces =
            {
                new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 },   // -x, +x
                new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 },   // -y, +y
                new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 },   // -z, +z
            };
            var centre = Vector3.zero;
            foreach (var q in corners) centre += q / 8f;
            foreach (var f in faces)
            {
                int i0 = v.Count;
                for (int k = 0; k < 4; k++) v.Add(corners[f[k]]);
                var uvs = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                if (flipU) for (int k = 0; k < 4; k++) uvs[k].x = 1f - uvs[k].x;
                if (flipV) for (int k = 0; k < 4; k++) uvs[k].y = 1f - uvs[k].y;
                uv.AddRange(uvs);
                // wind outward whatever the corner order and the yaw did
                var n = Vector3.Cross(v[i0 + 1] - v[i0], v[i0 + 2] - v[i0]);
                var fc = (v[i0] + v[i0 + 1] + v[i0 + 2] + v[i0 + 3]) * 0.25f;
                // cross(b - a, c - a) is the side Unity draws (clockwise from the viewer)
                bool outward = Vector3.Dot(n, fc - centre) > 0f;
                if (outward) tri.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                else tri.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            }
            var m = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // =====================================================================
        //  The checker with STAND-IN on it
        // =====================================================================

        static Material mat;
        static Texture2D tex;

        /// <summary>Flat checker, STAND-IN written across: never the livery.</summary>
        public static Material Material
        {
            get
            {
                if (mat != null) return mat;
                var sh = Shader.Find("PSX/Lit");
                if (sh == null) sh = Shader.Find("Sprites/Default");   // a shader the player always carries (the build refuses a runtime name it would strip)
                mat = new Material(sh) { name = "CarPartsStandIn", hideFlags = HideFlags.HideAndDontSave };
                mat.mainTexture = Texture;
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0f);
                return mat;
            }
        }

        public static Texture2D Texture
        {
            get
            {
                if (tex != null) return tex;
                const int W = 256, H = 128;
                tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "CarPartsStandIn", hideFlags = HideFlags.HideAndDontSave,
                                                                          filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[W * H];
                var a = new Color32(232, 170, 40, 255);   // amber
                var b = new Color32(46, 46, 52, 255);     // charcoal
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        px[y * W + x] = ((x / 32) + (y / 32)) % 2 == 0 ? a : b;
                // the label: a white band with black letters across the middle
                const string text = "STAND-IN";
                int scale = 4, gw = 5 * scale, gap = scale * 2;
                int tw = text.Length * gw + (text.Length - 1) * gap;
                int x0 = (W - tw) / 2, y0 = (H - 7 * scale) / 2;
                for (int y = y0 - 6; y < y0 + 7 * scale + 6; y++)
                    for (int x = x0 - 8; x < x0 + tw + 8; x++)
                        if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = new Color32(245, 245, 240, 255);
                for (int i = 0; i < text.Length; i++)
                {
                    var g = Glyph(text[i]);
                    for (int gy = 0; gy < 7; gy++)
                        for (int gx = 0; gx < 5; gx++)
                        {
                            if (g[gy][gx] != '#') continue;
                            for (int sy = 0; sy < scale; sy++)
                                for (int sx = 0; sx < scale; sx++)
                                {
                                    int x = x0 + i * (gw + gap) + gx * scale + sx;
                                    int y = y0 + (6 - gy) * scale + sy;
                                    px[y * W + x] = new Color32(10, 10, 12, 255);
                                }
                        }
                }
                tex.SetPixels32(px);
                tex.Apply(false, false);
                return tex;
            }
        }

        static string[] Glyph(char ch)
        {
            switch (ch)
            {
                case 'S': return new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." };
                case 'T': return new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." };
                case 'A': return new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" };
                case 'N': return new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" };
                case 'D': return new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." };
                case 'I': return new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" };
                case '-': return new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." };
                default: return new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." };
            }
        }

        // =====================================================================
        //  Heights off the body mesh, where it can be read (the editor)
        // =====================================================================

        sealed class Probe
        {
            Vector3[] v;   // car frame

            public static Probe Make(Mesh m, Quaternion yaw, Vector3 off)
            {
                if (m == null) return null;
                if (!m.isReadable && !Application.isEditor) return null;
                Vector3[] raw;
                try { raw = m.vertices; } catch { return null; }
                if (raw == null || raw.Length == 0) return null;
                var p = new Probe { v = new Vector3[raw.Length] };
                for (int i = 0; i < raw.Length; i++) p.v[i] = yaw * raw[i] + off;
                return p;
            }

            /// <summary>The highest vertex within 5 cm of z and |x| &lt; w.</summary>
            public float TopAt(float z, float w)
            {
                float best = float.MinValue;
                foreach (var q in v)
                    if (Mathf.Abs(q.z - z) < 0.05f && Mathf.Abs(q.x) < w && q.y > best) best = q.y;
                return best > float.MinValue ? best : 0.9f;
            }

            /// <summary>The outermost vertex on one side within 10 cm of z, between two heights.</summary>
            public float SideAt(int side, float z, float y0, float y1)
            {
                float best = 0f;
                foreach (var q in v)
                    if (Mathf.Abs(q.z - z) < 0.10f && q.y > y0 && q.y < y1 && q.x * side > best) best = q.x * side;
                return best > 0f ? best : 0.85f;
            }

            /// <summary>Going forward from z0, where the top first rises 8 cm over the deck.</summary>
            public float GlassBase(float z0, float deck, float w)
            {
                for (float z = z0; z < z0 + 2.5f; z += 0.02f)
                    if (TopAt(z, w) > deck + 0.08f) return z;
                return float.NaN;
            }
        }
    }
}
