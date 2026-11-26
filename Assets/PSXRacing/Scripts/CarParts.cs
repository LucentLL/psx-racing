using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE OPENING PARTS (carparts; owner 2026-11-26, of what he liked in a
    /// reference game: "rigged cars (hood, doors, trunk)" and "engine models
    /// in engine bay"). Hood, trunk or hatch and the doors of a car whose
    /// shell has them (<see cref="CarModelDef.openingParts"/>, vehicle spec
    /// 5A), swung open and shut about their own hinge lines, drawn only.
    ///
    /// CLOSED CAR = TODAY'S CAR. Nothing of this exists on a shut car: the
    /// component is added to the body's object (CarBody, CarShell or the
    /// CarViewer turntable, the one object that is both the scaled root and
    /// the renderer) the first time something asks for a part, and it removes
    /// itself once every part is shut again. While it is up the body's filter
    /// wears <see cref="CarModelDef.bodyOpenMesh"/> (the body with its
    /// apertures, jambs, bay and trunk well) and every panel is drawn as its
    /// own renderer on the body's livery; on the last shut <see cref="CarModelDef.bodyMesh"/>
    /// goes back and the parts are destroyed. Traffic and the drive pay nothing.
    ///
    /// THE HINGE CHAIN (no shear). The body root carries the fit's NON-UNIFORM
    /// scale S (CarModelLibrary.ShellFit.BodyScale); a child rotated under it
    /// would shear as it swings. So each part hangs from
    ///   Hinge (pos = pivot in mesh space, i.e. S*pivot; scale 1/S: a unit frame)
    ///   > Swing (rotation about normalize(S*axis), the hinge line as fitted)
    ///   > Part (pos = -S*pivot, scale S),
    /// and with Swing at identity every vertex is exactly where the closed body
    /// draws it. Re-solved whenever S changes.
    ///
    /// MOTION is one eased hinge angle per part, no clips: doors
    /// <see cref="DoorOpenSeconds"/> (the one door time the whole game uses),
    /// lids <see cref="LidOpenSeconds"/>, smoothstep in and out.
    ///
    /// REFUSED this round: pop-up-lamp shells and a car wearing the lens
    /// variant B (<see cref="Allowed(CarModelDef, MeshFilter)"/>).
    ///
    /// LAB row "carparts" (<see cref="On"/>, PSX_CARPARTS=0 = the old game).
    /// </summary>
    public class CarParts : MonoBehaviour
    {
        // ---- the owner's numbers, in one place -----------------------------
        /// <summary>The one door-opening time (the characters' get-in reads it too).</summary>
        public const float DoorOpenSeconds = 0.6f;
        /// <summary>Hood and trunk / hatch lids.</summary>
        public const float LidOpenSeconds = 0.8f;
        /// <summary>Parts may open below this speed...</summary>
        public const float OpenBelowKmh = 5f;
        /// <summary>...and shut themselves above this one (no reopening until below <see cref="OpenBelowKmh"/>).</summary>
        public const float ShutAboveKmh = 15f;

        // ---- the open mask: one byte a car -------------------------------
        public const int Hood = 1, Trunk = 2, DoorL = 4, DoorR = 8, DoorRL = 16, DoorRR = 32;
        public const int Doors = DoorL | DoorR | DoorRL | DoorRR;
        public const int Lids = Hood | Trunk;
        public const int All = 63;

        /// <summary>LAB "carparts" (OPEN HOOD, TRUNK AND DOORS). Off = today's
        /// game: nothing opens, every open part is shut at once.</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_CARPARTS") != "0";

        /// <summary>The LAB row's apply: switched off, every car shuts now.</summary>
        public static void ApplyLab()
        {
            if (!On) ShutAllNow();
        }

        /// <summary>The mask bit of a 5A outer panel name, 0 for anything else.</summary>
        public static int BitOf(string name)
        {
            switch (name)
            {
                case "Hood": return Hood;
                case "Trunk": case "Hatch": return Trunk;
                case "Door_L": return DoorL;
                case "Door_R": return DoorR;
                case "Door_RL": return DoorRL;
                case "Door_RR": return DoorRR;
                default: return 0;
            }
        }

        static bool IsDoorBit(int bit) => (bit & Doors) != 0;

        // =====================================================================
        //  Static API
        // =====================================================================

        /// <summary>The def that carries <paramref name="def"/>'s parts: the
        /// shell's own (5A art), else the test stand-in when PSX_PARTS_STANDIN=1
        /// asks for it on its shell, else null (a car without parts).</summary>
        public static CarModelDef PartsDef(CarModelDef def)
        {
            if (def == null) return null;
            if (def.HasParts) return def;
            return CarPartsStandIn.For(def);
        }

        /// <summary>May this shell open anything at all? False with the LAB row
        /// off, for a shell without parts and for a pop-up-lamp shell.</summary>
        public static bool Allowed(CarModelDef def)
        {
            if (!On || def == null || def.HasPopUps) return false;
            var p = PartsDef(def);
            return p != null && p.HasParts;
        }

        /// <summary><see cref="Allowed(CarModelDef)"/>, and not while this
        /// body wears the lens variant B.</summary>
        public static bool Allowed(CarModelDef def, MeshFilter body)
        {
            if (!Allowed(def) || body == null) return false;
            return !(def.lensBodyMesh != null && body.sharedMesh == def.lensBodyMesh);
        }

        /// <summary>The parts this shell has, as a mask (0 = none).</summary>
        public static int Available(CarModelDef def)
        {
            var p = PartsDef(def);
            if (p == null || !p.HasParts) return 0;
            int m = 0;
            foreach (var op in p.openingParts) m |= BitOf(op.name);
            return m;
        }

        /// <summary>The parts component on this body, or null (every part shut).</summary>
        public static CarParts Find(MeshFilter body)
        {
            if (body == null) return null;
            // (a torn-down one lingers to the frame's end in play: never it)
            foreach (var c in body.GetComponents<CarParts>()) if (c != null && !c.disposed) return c;
            return null;
        }

        /// <summary>The parts this body has open or opening (0 = all shut).</summary>
        public static int MaskOf(MeshFilter body)
        {
            var cp = Find(body);
            return cp != null ? cp.target : 0;
        }

        /// <summary>
        /// Ask for exactly <paramref name="mask"/> open on this body (0 shuts
        /// everything, eased). Returns the component while anything is open or
        /// moving, null when refused (<see cref="Allowed(CarModelDef, MeshFilter)"/>)
        /// or when nothing was open and nothing is asked. <paramref name="instant"/>:
        /// no easing (tools and tests).
        /// </summary>
        public static CarParts Set(MeshFilter body, CarModelDef def, int mask, bool instant = false)
        {
            var cp = Find(body);
            if (!Allowed(def, body))
            {
                if (cp != null) TearDown(body);
                return null;
            }
            mask &= Available(def);
            if (cp == null)
            {
                if (mask == 0) return null;
                cp = body.gameObject.AddComponent<CarParts>();
                if (!cp.Init(body, def)) { Kill(cp); return null; }
            }
            else if (cp.def != def)
            {
                // a different shell under the same filter: start again, shut
                TearDown(body);
                return Set(body, def, mask, instant);
            }
            cp.target = mask;
            if (instant) cp.Snap();
            return cp != null && !cp.disposed ? cp : null;
        }

        /// <summary>Shut every part on this body NOW and remove the component:
        /// the closed body goes back on the filter. Every re-dress of a body
        /// (CarBody.Apply, CarViewer.Show, the lens variant B) calls this first,
        /// so a re-dress always starts shut.</summary>
        public static void TearDown(MeshFilter body)
        {
            var cp = Find(body);
            if (cp == null) return;
            cp.Dispose();
            Kill(cp);
        }

        /// <summary>Every car, every part, shut now (the LAB row going off).</summary>
        public static void ShutAllNow()
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var cp = live[i];
                if (cp == null) { live.RemoveAt(i); continue; }
                TearDown(cp.filter);
            }
        }

        /// <summary>How many bodies have a part open or moving now (tests, the cap later).</summary>
        public static int LiveCount
        {
            get
            {
                live.RemoveAll(c => c == null);
                return live.Count;
            }
        }

        static readonly List<CarParts> live = new List<CarParts>();

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // =====================================================================
        //  One car
        // =====================================================================

        sealed class Part
        {
            public int bit;
            public CarModelDef.OpeningPart src;
            public Transform hinge, swing, part;
            public MeshRenderer r, cover;
            public float seconds;
            /// <summary>0 shut .. 1 open, linear in time; the angle is eased from it.</summary>
            public float t;
            public Vector3 axisFit;
        }

        CarModelDef def, pdef;
        MeshFilter filter;
        MeshRenderer bodyR;
        Transform root;
        Part[] parts = new Part[0];
        MeshRenderer cabinPanel;
        Vector3 solvedScale = new Vector3(float.NaN, 0f, 0f);
        int target;
        bool wearsOpen, disposed;

        /// <summary>The shell this component dresses.</summary>
        public CarModelDef Def => def;
        /// <summary>The def its parts come from (the shell's own, or the stand-in).</summary>
        public CarModelDef PartsSource => pdef;
        /// <summary>The asked-for open mask.</summary>
        public int Target => target;
        /// <summary>Parts not fully shut now.</summary>
        public int OpenNow
        {
            get
            {
                int m = 0;
                foreach (var p in parts) if (p.t > 0f) m |= p.bit;
                return m;
            }
        }
        /// <summary>Every part where it was asked to be.</summary>
        public bool Settled
        {
            get
            {
                foreach (var p in parts) if (p.t != (((target & p.bit) != 0) ? 1f : 0f)) return false;
                return true;
            }
        }
        /// <summary>The renderers this component drew (parts and the cabin panel).</summary>
        public IEnumerable<Renderer> Renderers
        {
            get
            {
                foreach (var p in parts)
                {
                    if (p.r != null) yield return p.r;
                    if (p.cover != null) yield return p.cover;
                }
                if (cabinPanel != null) yield return cabinPanel;
            }
        }
        /// <summary>The hinge-chain Part transform of a panel (tests), or null.</summary>
        public Transform PartTransform(int bit)
        {
            foreach (var p in parts) if (p.bit == bit) return p.part;
            return null;
        }
        /// <summary>The Swing transform of a panel (tests), or null.</summary>
        public Transform SwingTransform(int bit)
        {
            foreach (var p in parts) if (p.bit == bit) return p.swing;
            return null;
        }
        /// <summary>The eased hinge angle of a panel now, degrees.</summary>
        public float Angle(int bit)
        {
            foreach (var p in parts) if (p.bit == bit) return Ease(p.t) * p.src.openDeg;
            return 0f;
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        bool Init(MeshFilter body, CarModelDef d)
        {
            filter = body;
            bodyR = body.GetComponent<MeshRenderer>();
            def = d;
            pdef = PartsDef(d);
            if (pdef == null || !pdef.HasParts || bodyR == null) return false;

            var go = new GameObject("CarParts");
            go.layer = body.gameObject.layer;
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            root = go.transform;
            root.SetParent(body.transform, false);

            bool standIn = CarPartsStandIn.IsStandIn(pdef);
            var list = new List<Part>();
            foreach (var op in pdef.openingParts)
            {
                int bit = BitOf(op.name);
                if (bit == 0 || op.mesh == null) continue;
                var p = new Part { bit = bit, src = op, seconds = IsDoorBit(bit) ? DoorOpenSeconds : LidOpenSeconds };
                p.hinge = NewNode("Hinge_" + op.name, root);
                p.swing = NewNode("Swing", p.hinge);
                p.part = NewNode(op.name, p.swing);
                p.part.gameObject.AddComponent<MeshFilter>().sharedMesh = op.mesh;
                p.r = p.part.gameObject.AddComponent<MeshRenderer>();
                p.r.sharedMaterial = standIn ? CarPartsStandIn.Material : bodyR.sharedMaterial;
                CopyLook(bodyR, p.r);
                // a clear cover the panel carries (a lid's lamp cover): its own
                // renderer on the moving part, PSX/CarLens like the body's covers
                if (op.coverMesh != null && pdef.coverMaterial != null && CarCovers.On)
                {
                    var cg = NewNode(op.name + "_" + CarCovers.ChildName, p.part);
                    cg.gameObject.AddComponent<MeshFilter>().sharedMesh = op.coverMesh;
                    p.cover = cg.gameObject.AddComponent<MeshRenderer>();
                    p.cover.sharedMaterial = pdef.coverMaterial;
                    p.cover.shadowCastingMode = ShadowCastingMode.Off;
                    p.cover.receiveShadows = false;
                    p.cover.lightProbeUsage = LightProbeUsage.Off;
                    p.cover.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    p.cover.renderingLayerMask = bodyR.renderingLayerMask;
                }
                list.Add(p);
            }
            parts = list.ToArray();

            // THE DARK CABIN PANEL (decision 9): one flat quad per door
            // aperture just inboard of the jamb, so an open door shows a dark,
            // matte, coloured cabin and not the inside of an empty shell.
            var panelMesh = CabinPanelMesh(pdef);
            if (panelMesh != null)
            {
                var pg = NewNode("CabinPanel", root);
                pg.gameObject.AddComponent<MeshFilter>().sharedMesh = panelMesh;
                cabinPanel = pg.gameObject.AddComponent<MeshRenderer>();
                cabinPanel.sharedMaterial = CabinPanelMaterial;
                CopyLook(bodyR, cabinPanel);
                cabinPanel.shadowCastingMode = ShadowCastingMode.Off;
                cabinPanel.enabled = false;
            }
            Solve();
            live.Add(this);
            return true;
        }

        static Transform NewNode(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void CopyLook(MeshRenderer from, MeshRenderer to)
        {
            to.shadowCastingMode = from.shadowCastingMode;
            to.receiveShadows = from.receiveShadows;
            to.lightProbeUsage = from.lightProbeUsage;
            to.reflectionProbeUsage = from.reflectionProbeUsage;
            to.renderingLayerMask = from.renderingLayerMask;
            to.gameObject.layer = from.gameObject.layer;
        }

        /// <summary>The hinge chain for the body's scale now.</summary>
        void Solve()
        {
            var s = filter.transform.localScale;
            solvedScale = s;
            var inv = new Vector3(SafeInv(s.x), SafeInv(s.y), SafeInv(s.z));
            foreach (var p in parts)
            {
                p.hinge.localPosition = p.src.pivot;
                p.hinge.localRotation = Quaternion.identity;
                p.hinge.localScale = inv;
                p.part.localPosition = -Vector3.Scale(s, p.src.pivot);
                p.part.localRotation = Quaternion.identity;
                p.part.localScale = s;
                var a = Vector3.Scale(s, p.src.axis);
                p.axisFit = a.sqrMagnitude > 1e-12f ? a.normalized : Vector3.right;
                Pose(p);
            }
        }

        static float SafeInv(float v) => Mathf.Abs(v) > 1e-6f ? 1f / v : 1f;

        void Pose(Part p)
        {
            p.swing.localRotation = Quaternion.AngleAxis(Ease(p.t) * p.src.openDeg, p.axisFit);
        }

        void Update()
        {
            if (disposed) return;
            if (!On) { TearDown(filter); return; }
            Step(Time.deltaTime);
        }

        /// <summary>Move every part toward the asked mask by <paramref name="dt"/>
        /// seconds; the last shut removes the component.</summary>
        public void Step(float dt)
        {
            if (filter == null) { Kill(this); return; }
            if (filter.transform.localScale != solvedScale) Solve();
            foreach (var p in parts)
            {
                float want = (target & p.bit) != 0 ? 1f : 0f;
                if (p.t != want)
                {
                    p.t = Mathf.MoveTowards(p.t, want, dt / Mathf.Max(0.01f, p.seconds));
                    Pose(p);
                }
            }
            Dress();
        }

        /// <summary>Every part straight to where it was asked (no easing).</summary>
        public void Snap()
        {
            foreach (var p in parts)
            {
                p.t = (target & p.bit) != 0 ? 1f : 0f;
                Pose(p);
            }
            Dress();
        }

        /// <summary>The body and the renderers for the parts' state now; the
        /// last shut tears the component down.</summary>
        void Dress()
        {
            int open = OpenNow;
            if (open == 0 && target == 0)
            {
                TearDown(filter);
                return;
            }
            // The open body while anything is open: the panels are then all
            // drawn as parts (the open body has none). The stand-in has no
            // open body: its boxes sit over the intact car.
            var openMesh = pdef.bodyOpenMesh;
            if (openMesh != null && !wearsOpen) SwapBody(openMesh);
            foreach (var p in parts) if (p.r != null) p.r.enabled = true;
            if (cabinPanel != null) cabinPanel.enabled = (open & Doors) != 0;
        }

        void SwapBody(Mesh m)
        {
            filter.sharedMesh = m;
            wearsOpen = m == pdef.bodyOpenMesh && m != null;
            BodySwapped();
        }

        /// <summary>The readers that follow the body's filter, told of a swap:
        /// the clear covers (over the open body as over the closed one), the
        /// lamps (CarLights.ShellChanged takes the open body for the same
        /// shell) and the cockpit's inside copy (re-copied, so the hood can
        /// open in the cockpit view).</summary>
        void BodySwapped()
        {
            CarCovers.Dress(filter, pdef);   // (the parts def: the same covers, and it knows its open body)
            var car = filter.GetComponentInParent<CarController>();
            var ci = car != null ? car.GetComponentInChildren<CockpitInterior>(true) : null;
            if (ci != null) ci.BodyChanged();
        }

        void Dispose()
        {
            disposed = true;
            live.Remove(this);
            target = 0;
            if (filter != null && wearsOpen)
            {
                filter.sharedMesh = def.bodyMesh;
                wearsOpen = false;
                BodySwapped();
            }
            if (root != null) Kill(root.gameObject);
            root = null;
            parts = new Part[0];
            cabinPanel = null;
        }

        void OnDestroy()
        {
            live.Remove(this);
            if (root != null) Kill(root.gameObject);
        }

        // =====================================================================
        //  The dark cabin panel (shared per shell)
        // =====================================================================

        static readonly Dictionary<CarModelDef, Mesh> panelMeshes = new Dictionary<CarModelDef, Mesh>();
        static Material panelMat;

        /// <summary>Inboard of the jamb: how far inside the door's inner face the panel stands, metres.</summary>
        public const float CabinPanelInboard = 0.06f;
        /// <summary>Dark but coloured (a worn vinyl brown), never the glass class.</summary>
        public static readonly Color CabinPanelColor = new Color(0.17f, 0.12f, 0.09f);

        static Material CabinPanelMaterial
        {
            get
            {
                if (panelMat != null) return panelMat;
                var sh = Shader.Find("PSX/Lit");
                if (sh == null) return null;
                panelMat = new Material(sh) { name = "CarPartsCabinPanel", hideFlags = HideFlags.HideAndDontSave };
                panelMat.SetColor("_Color", CabinPanelColor);
                if (panelMat.HasProperty("_Affine")) panelMat.SetFloat("_Affine", 0f);
                return panelMat;
            }
        }

        /// <summary>One two-sided quad per door: the door's height and length
        /// (its mesh bounds, trimmed 3 cm), standing just inboard of the
        /// door's inner face, body mesh space. Null for a shell with no doors.</summary>
        static Mesh CabinPanelMesh(CarModelDef p)
        {
            if (panelMeshes.TryGetValue(p, out var m) && m != null) return m;
            var v = new List<Vector3>();
            var tri = new List<int>();
            var n = new List<Vector3>();
            float cx = p.bodyMesh != null ? p.bodyMesh.bounds.center.x : 0f;
            foreach (var op in p.openingParts)
            {
                if (!IsDoorBit(BitOf(op.name)) || op.mesh == null) continue;
                var b = op.mesh.bounds;
                // The across axis of the body mesh is X whatever its yaw (0 or 180).
                bool plusSide = b.center.x > cx;
                // (the stand-in's door is a slab OUTSIDE the intact body: its panel goes well inside)
                float inb = CarPartsStandIn.IsStandIn(p) ? 0.30f : CabinPanelInboard;
                float inner = plusSide ? b.min.x - inb : b.max.x + inb;
                float y0 = b.min.y + 0.03f, y1 = b.max.y - 0.03f, z0 = b.min.z + 0.03f, z1 = b.max.z - 0.03f;
                var outN = new Vector3(plusSide ? 1f : -1f, 0f, 0f);
                int i0 = v.Count;
                v.Add(new Vector3(inner, y0, z0)); v.Add(new Vector3(inner, y1, z0));
                v.Add(new Vector3(inner, y1, z1)); v.Add(new Vector3(inner, y0, z1));
                for (int k = 0; k < 4; k++) n.Add(outN);
                int i1 = v.Count;
                for (int k = 0; k < 4; k++) { v.Add(v[i0 + k]); n.Add(-outN); }
                // both windings: seen from outside through the aperture, and from the cabin
                tri.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                tri.AddRange(new[] { i1, i1 + 2, i1 + 1, i1, i1 + 3, i1 + 2 });
            }
            if (v.Count == 0) { panelMeshes[p] = null; return null; }
            m = new Mesh { name = "CarPartsCabinPanel_" + p.key, hideFlags = HideFlags.HideAndDontSave };
            m.SetVertices(v);
            m.SetNormals(n);
            var uv = new List<Vector2>();
            for (int k = 0; k < v.Count; k++) uv.Add(new Vector2(0.5f, 0.5f));
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            panelMeshes[p] = m;
            return m;
        }
    }
}
