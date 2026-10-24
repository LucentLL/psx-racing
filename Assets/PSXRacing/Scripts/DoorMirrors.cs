using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// THE CAR'S OWN DOOR MIRRORS, SEEN FROM THE SEAT AND WORKING (owner,
    /// 2026-10-19: "I should see the mirrors of cars from cockpit windows",
    /// then "door mirrors should be functional like rear-view mirror").
    ///
    /// The mirrors are the shell's own: on 41 of the 46 shells a small
    /// separate piece just behind the windscreen's base, one each side (the
    /// '70 Charger has only the driver's). The cut that used to hide them
    /// (and every panel beside the cabin) is now the cabin's own size, and
    /// keeps each mirror whole (<see cref="CockpitInterior"/>,
    /// <see cref="CabinClip"/>). This puts a live picture on each mirror's
    /// glass: a small camera at the glass looking back along its side of the
    /// car, its render on a quad laid a few millimetres proud of the glass.
    ///
    /// The glass is MEASURED off the meshes by the Door Mirror Probe
    /// (Editor/DoorMirrorProbe.cs) into DoorMirrors.Table.cs: the mirror's
    /// own rear-facing triangles, stood 5 mm out, and a frame to map the
    /// picture onto them (flat, angled or domed, they all take it). A WebGL
    /// build cannot read the shells (Read/Write off), hence a table, as for
    /// <see cref="ChaseSilhouettes"/>. It is in MESH space, so the glass
    /// rides the body's fit and lean exactly as the housing does. The glass
    /// is drawn camera-relative (PSX/CabinGauge): 5 km from the origin a
    /// world-space vertex rounds to 0.49 mm, a tenth of the stand-off. A
    /// shell with no row keeps its mirrors on view, painted, with no picture
    /// in them.
    /// </summary>
    public static partial class DoorMirrors
    {
        /// <summary>One mirror: its glass's frame (centre on its rearmost
        /// point, the half-vectors across and up it, the normal out of it
        /// toward the driver), the box of every piece of it (housing, arm,
        /// glass) and the glass's triangles, all in the body MESH's frame.</summary>
        public readonly struct Glass
        {
            public readonly Vector3 centre, across, up, normal, min, max;
            public readonly Vector3[] tris;
            public Glass(Vector3 centre, Vector3 across, Vector3 up, Vector3 normal, Vector3 min, Vector3 max, Vector3[] tris)
            { this.centre = centre; this.across = across; this.up = up; this.normal = normal; this.min = min; this.max = max; this.tris = tris; }
        }

        public readonly struct Entry
        {
            public readonly string key;
            public readonly Glass[] glass;
            /// <summary>Two arrays a mirror: its frame (18 floats: centre,
            /// across, up, normal, min, max) and its triangles (9 floats each).</summary>
            public Entry(string key, params float[][] parts)
            {
                this.key = key;
                int n = parts != null ? parts.Length / 2 : 0;
                glass = new Glass[n];
                for (int i = 0; i < n; i++)
                {
                    float[] f = parts[i * 2], t = parts[i * 2 + 1];
                    Vector3 V(float[] a, int k) => new Vector3(a[k], a[k + 1], a[k + 2]);
                    var tris = new Vector3[t.Length / 3];
                    for (int k = 0; k < tris.Length; k++) tris[k] = V(t, k * 3);
                    glass[i] = new Glass(V(f, 0), V(f, 3), V(f, 6), V(f, 9), V(f, 12), V(f, 15), tris);
                }
            }
        }

        static System.Collections.Generic.Dictionary<string, Entry> byKey;

        public static bool TryGet(string key, out Entry e)
        {
            if (byKey == null)
            {
                byKey = new System.Collections.Generic.Dictionary<string, Entry>();
                foreach (var t in Table) byKey[t.key] = t;
            }
            e = default;
            return key != null && byKey.TryGetValue(key, out e) && e.glass != null && e.glass.Length > 0;
        }

        /// <summary>The picture's horizontal field, degrees. A flat door
        /// mirror at arm's length spans ~12; this is the slightly convex one
        /// a game needs (the lane beside and the car behind in one look).</summary>
        public const float HFov = 30f;
        /// <summary>How much of the field the car's own flank keeps along the
        /// inner edge, degrees (a driver sets the mirror to just see it).</summary>
        public const float FlankDeg = 5f;
        /// <summary>The picture dips this far below level, degrees.</summary>
        public const float DipDeg = 3f;
        /// <summary>The camera stands this far outside the car's widest
        /// point and this far over the glass's middle, metres: the flank is a
        /// sliver at the inner edge and the door top under the glass (a '69
        /// Charger's bullet sits 3 cm over it) stays out of the picture.</summary>
        public const float Clear = 0.03f, Lift = 0.06f;
        /// <summary>Pixels across the picture: desktop, phone.</summary>
        public const int WidthDesktop = 192, WidthPhone = 128;
        /// <summary>Phones draw each mirror at most every Nth frame.</summary>
        public const int EveryPhone = 4;
        /// <summary>A mirror's box, padded for the body's lean, is never cut.</summary>
        public const float KeepPad = 0.03f;
    }

    /// <summary>One car's live door mirrors: the quads on the glass and the
    /// cameras behind them. <see cref="CockpitInterior"/> owns it; it lives
    /// under the cabin, so it is drawn for the driver alone and only while
    /// the cockpit view is on.</summary>
    public sealed class DoorMirrorRig
    {
        sealed class Side
        {
            public DoorMirrors.Glass g;
            public float sign;                  // -1 the car's left, +1 its right
            public MeshRenderer rend;
            public Mesh mesh;
            public Material mat;
            public Camera cam;
            public RenderTexture rt;
            public bool seen;
        }

        readonly Transform root, owner, car, body;
        readonly float halfW;
        readonly Side[] sides;
        readonly Plane[] planes = new Plane[6];
        bool active = true;

        public int Count => sides.Length;
        /// <summary>Mirrors drawn this frame (for the shot tool's log).</summary>
        public int Drawing { get; private set; }

        DoorMirrorRig(Transform owner, Transform car, Transform body, Side[] sides, Transform root, float halfW)
        { this.owner = owner; this.car = car; this.body = body; this.sides = sides; this.root = root; this.halfW = halfW; }

        /// <summary>The rig for this shell, or null (no row, no body).
        /// `halfWidth`: the car's widest point off its centreline, metres.</summary>
        public static DoorMirrorRig Build(Transform owner, Transform car, CarBody carBody, CarModelDef def, int layer, float halfWidth)
        {
            if (owner == null || car == null || carBody == null || carBody.bodyFilter == null || def == null) return null;
            if (!DoorMirrors.TryGet(def.key, out var e)) return null;
            var shader = Shader.Find("PSX/CabinGauge");
            if (shader == null) return null;
            var body = carBody.bodyFilter.transform;
            var root = new GameObject("DoorMirrors").transform;
            root.gameObject.layer = layer;
            root.SetParent(owner, false);
            // Mesh x to the car's x: the shell is turned 180 when its nose
            // was at -Z in the file.
            float xSign = Mathf.Abs(Mathf.DeltaAngle(def.bodyYaw, 180f)) < 90f ? -1f : 1f;
            var list = new System.Collections.Generic.List<Side>();
            foreach (var g in e.glass)
            {
                var s = new Side { g = g, sign = Mathf.Sign(g.centre.x * xSign) };
                var go = new GameObject(s.sign < 0f ? "Glass_L" : "Glass_R");
                go.layer = layer;
                go.transform.SetParent(root, false);
                s.mesh = GlassMesh(g, xSign);
                go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
                s.rend = go.AddComponent<MeshRenderer>();
                s.mat = new Material(shader) { name = "DoorMirror_" + go.name, hideFlags = HideFlags.HideAndDontSave };
                s.mat.SetFloat("_Mode", 1f);
                s.mat.SetFloat("_FlipU", 0f);
                s.rend.sharedMaterial = s.mat;
                list.Add(s);
            }
            var rig = new DoorMirrorRig(owner, car, body, list.ToArray(), root, halfWidth);
            rig.Pose();
            return rig;
        }

        /// <summary>The glass in mesh space: the shell's own triangles (their
        /// winding faces the driver), the picture projected across and up its
        /// frame. U runs from the car's right (0) to its left (1): the camera
        /// looking back has the car's left on ITS right, and a mirror shows
        /// the scene the other way round.</summary>
        static Mesh GlassMesh(DoorMirrors.Glass g, float xSign)
        {
            Vector3 a = g.across, u = g.up;
            if (u.y < 0f) u = -u;
            if (a.x * xSign < 0f) a = -a;
            float aa = Mathf.Max(1e-8f, a.sqrMagnitude), uu = Mathf.Max(1e-8f, u.sqrMagnitude);
            var v = g.tris;
            var uv = new Vector2[v.Length];
            var tri = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 r = v[i] - g.centre;
                uv[i] = new Vector2(Mathf.Clamp01(0.5f - 0.5f * Vector3.Dot(r, a) / aa), Mathf.Clamp01(0.5f + 0.5f * Vector3.Dot(r, u) / uu));
                tri[i] = i;
            }
            var m = new Mesh { name = "DoorMirrorGlass", hideFlags = HideFlags.DontSave };
            m.vertices = v;
            m.uv = uv;
            m.triangles = tri;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The quads onto the body as it stands now (its fit and its
        /// lean): the root copies the body's transform.</summary>
        public void Pose()
        {
            if (root == null || body == null) return;
            root.SetPositionAndRotation(body.position, body.rotation);
            Vector3 ps = owner.lossyScale, bs = body.lossyScale;
            root.localScale = new Vector3(bs.x / Mathf.Max(1e-4f, ps.x), bs.y / Mathf.Max(1e-4f, ps.y), bs.z / Mathf.Max(1e-4f, ps.z));
        }

        /// <summary>Each mirror's box in the CUT's frame as the body stands
        /// now (CabinClip.W2L: the cut rides the body with the cabin),
        /// padded: the cut keeps it whole.</summary>
        public void Keep()
        {
            for (int i = 0; i < 2; i++)
            {
                if (i >= sides.Length || body == null || car == null) { CabinClip.Keep(i, default, false); continue; }
                var g = sides[i].g;
                Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                for (int k = 0; k < 8; k++)
                {
                    var p = new Vector3((k & 1) == 0 ? g.min.x : g.max.x, (k & 2) == 0 ? g.min.y : g.max.y, (k & 4) == 0 ? g.min.z : g.max.z);
                    p = CabinClip.W2L.MultiplyPoint3x4(body.TransformPoint(p));
                    mn = Vector3.Min(mn, p);
                    mx = Vector3.Max(mx, p);
                }
                var b = new Bounds();
                b.SetMinMax(mn - Vector3.one * DoorMirrors.KeepPad, mx + Vector3.one * DoorMirrors.KeepPad);
                CabinClip.Keep(i, b, true);
            }
        }

        public bool Owns(Camera cam)
        {
            if (cam == null) return false;
            foreach (var s in sides) if (s.cam == cam) return true;
            return false;
        }

        /// <summary>Cameras off with the cabin (the quads go with it).</summary>
        public void SetActive(bool on)
        {
            active = on;
            foreach (var s in sides)
                if (s.cam != null && s.cam.gameObject.activeSelf != on) s.cam.gameObject.SetActive(on);
        }

        /// <summary>The quads on or off (the shot tool's BEFORE frame).</summary>
        public void ShowGlass(bool on)
        {
            foreach (var s in sides) if (s.rend != null) s.rend.enabled = on;
        }

        /// <summary>Which mirror draws this frame. Only a mirror the driver
        /// can see draws at all: in a left-hand drive car the left one sits
        /// at the edge of the view ahead, the right one shows only when he
        /// looks over. Never on the rear-view's frames (it draws on even
        /// ones); two in view take turns, and so does every mirror on a phone
        /// (each at most every <see cref="DoorMirrors.EveryPhone"/>th frame).</summary>
        public void Tick(Camera driver)
        {
            Drawing = 0;
            if (!active || driver == null) { foreach (var s in sides) if (s.cam != null) s.cam.enabled = false; return; }
            Pose();
            GeometryUtility.CalculateFrustumPlanes(driver, planes);
            int inView = 0;
            foreach (var s in sides)
            {
                s.seen = s.rend != null && GeometryUtility.TestPlanesAABB(planes, s.rend.bounds);
                if (s.seen) inView++;
            }
            bool phone = Application.isMobilePlatform;
            int f = Time.frameCount;
            bool odd = (f & 1) == 1;
            bool turns = phone || inView > 1;
            foreach (var s in sides)
            {
                bool draw = s.seen && odd && (!turns || ((f >> 1) & 1) == (s.sign < 0f ? 0 : 1));
                if (draw && s.cam == null) Ensure(s, driver);
                if (s.cam == null) continue;
                if (draw) Aim(s);
                s.cam.enabled = draw;
                if (draw) Drawing++;
            }
        }

        /// <summary>For the shot tool (edit mode): every mirror rendered once
        /// from `src`'s world. The caller hides the cabin around it.</summary>
        public void Preview(Camera src)
        {
            Pose();
            foreach (var s in sides)
            {
                if (s.cam == null) Ensure(s, src);
                if (s.cam == null) continue;
                Aim(s);
                s.cam.gameObject.SetActive(true);
                s.cam.enabled = false;
                s.cam.Render();
            }
        }

        /// <summary>For the shot tool: one mirror rendered `n` times, ms each
        /// (the CPU's share; the caller syncs the GPU around it).</summary>
        public Camera CameraOf(int i) => i >= 0 && i < sides.Length ? sides[i].cam : null;
        public float SignOf(int i) => i >= 0 && i < sides.Length ? sides[i].sign : 0f;
        public Bounds GlassBounds(int i) => i >= 0 && i < sides.Length && sides[i].rend != null ? sides[i].rend.bounds : default;

        /// <summary>The camera, as the rear-view's: at the glass, looking
        /// back along the car - stood just outside the car's widest point and
        /// a little over the glass (<see cref="DoorMirrors.Clear"/>,
        /// <see cref="DoorMirrors.Lift"/>), turned out so the flank keeps
        /// <see cref="DoorMirrors.FlankDeg"/> of the inner edge, dipped
        /// <see cref="DoorMirrors.DipDeg"/>: a mirror set as a driver sets it,
        /// a sliver of the car and the lane beside. (The driver's eye
        /// reflected in the glass stands INSIDE the car's width, and a wide
        /// car's own door then filled half of the picture.)</summary>
        void Aim(Side s)
        {
            if (s.cam == null || s.rend == null) return;
            Vector3 m = car.InverseTransformPoint(s.rend.transform.TransformPoint(s.g.centre));
            var p = new Vector3(s.sign * Mathf.Max(Mathf.Abs(m.x), halfW + DoorMirrors.Clear), m.y + DoorMirrors.Lift, m.z);
            float yaw = (DoorMirrors.HFov * 0.5f - DoorMirrors.FlankDeg) * Mathf.Deg2Rad;
            float dip = DoorMirrors.DipDeg * Mathf.Deg2Rad;
            Vector3 dir = -car.forward * Mathf.Cos(yaw) + car.right * (s.sign * Mathf.Sin(yaw));
            dir = dir * Mathf.Cos(dip) - car.up * Mathf.Sin(dip);
            s.cam.transform.SetPositionAndRotation(car.TransformPoint(p), Quaternion.LookRotation(dir, car.up));
        }

        /// <summary>A small camera into the glass's texture, set up as the
        /// rear-view's is (CockpitInterior.EnsureMirrorCamera): the driver's
        /// world and far plane, no post, no shadows.</summary>
        void Ensure(Side s, Camera src)
        {
            if (src == null || s.mat == null) return;
            float across = (s.g.across.magnitude) * 2f, up = (s.g.up.magnitude) * 2f;
            float aspect = Mathf.Clamp(across / Mathf.Max(0.01f, up), 0.8f, 3f);
            int w = Application.isMobilePlatform ? DoorMirrors.WidthPhone : DoorMirrors.WidthDesktop;
            int h = Mathf.Clamp(Mathf.RoundToInt(w / aspect), 48, 192);
            s.rt = new RenderTexture(w, h, 24) { name = "DoorMirror", filterMode = FilterMode.Bilinear, antiAliasing = 1 };
            var go = new GameObject(s.sign < 0f ? "DoorMirrorCamera_L" : "DoorMirrorCamera_R");
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(owner, false);
            s.cam = go.AddComponent<Camera>();
            s.cam.CopyFrom(src);
            go.tag = "Untagged";
            s.cam.targetTexture = s.rt;
            s.cam.aspect = w / (float)h;
            s.cam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(DoorMirrors.HFov * 0.5f * Mathf.Deg2Rad) / s.cam.aspect) * Mathf.Rad2Deg;
            s.cam.nearClipPlane = 0.05f;
            // The driver's far plane, as the rear-view's (the skyline is
            // squeezed for it and culled by its bounds).
            s.cam.farClipPlane = src.farClipPlane;
            s.cam.depth = src.depth - 1f;
            s.cam.useOcclusionCulling = false;
            s.cam.allowHDR = false;
            s.cam.allowMSAA = false;
            var data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            s.mat.SetTexture("_FaceTex", s.rt);
            s.cam.enabled = false;
            go.SetActive(active);
        }

        public void Destroy()
        {
            foreach (var s in sides)
            {
                if (s.cam != null) Object.DestroyImmediate(s.cam.gameObject);
                if (s.rt != null) { s.rt.Release(); Object.DestroyImmediate(s.rt); }
                if (s.mat != null) Object.DestroyImmediate(s.mat);
                if (s.mesh != null) Object.DestroyImmediate(s.mesh);
            }
            if (root != null) Object.DestroyImmediate(root.gameObject);
            for (int i = 0; i < 2; i++) CabinClip.Keep(i, default, false);
        }
    }
}
