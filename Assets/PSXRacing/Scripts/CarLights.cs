using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Headlights, tail lights and brake lights.
    ///
    /// Three things, drawn three ways:
    ///   * THE LENSES are additive sprites on the bodywork (PSX/Glow), the
    ///     way a PS1 game drew a lamp. Head lenses run after dark; tail lenses
    ///     run after dark AND whenever the car is braking, day or night,
    ///     because the brake light of the car in front is the one lighting
    ///     cue that tells you what it is about to do.
    ///   * THE BEAM ON THE WORLD is real: this component pushes every lit
    ///     lamp into a global table (position, axis, spread, colour) and
    ///     PSXHeadlights.cginc lights every PSX surface from it per pixel — a
    ///     halogen low beam with a flat top, thirty degrees wide, seventy-odd
    ///     metres long, on the road, the kerb, the wall ahead and the car
    ///     ahead. It replaced a single additive disc laid on the tarmac.
    ///   * THE BEAM IN THE AIR is a cone mesh from each lens (PSX/Beam), the
    ///     lit volume a night drive is mostly made of.
    ///
    /// THE LAMPS ARE MEASURED OFF THE SHELL, NOT THE COLLIDER. They used to
    /// hang three centimetres outside the car's BoxCollider — and the baker
    /// fits every pack car's collider to 95.5% of its mesh length, so on a
    /// 4.5 m car the lens sat seven centimetres INSIDE the bumper, where the
    /// opaque body hid it. That is the whole of "brake lights seem to be
    /// missing": every pack car had its head and tail lamps buried since the
    /// day the pack was wired in, and only the built-in RX-7, whose box is
    /// verbatim, ever showed them. The shell's own mesh bounds, transformed
    /// through the body's yaw and offsets into the car's frame, are where the
    /// nose and the tail actually are.
    ///
    /// Everything hangs under the BODY root when there is one, so the lamps
    /// dive with the nose under braking and roll with it in a corner — and
    /// so the beam does too, which is the one thing about a real headlight
    /// that the follow-the-steering pool got wrong.
    ///
    /// Every lamp is HALOGEN. The game is set in 1999; a white LED beam is
    /// twenty years out of period, and the warm slightly-yellow light is the
    /// single cheapest thing that dates the night scenes correctly.
    /// </summary>
    public class CarLights : MonoBehaviour
    {
        public CarController car;
        public BoxCollider box;

        static readonly List<CarLights> all = new List<CarLights>();
        static bool lightsOn;
        static bool lightsDecided;

        /// <summary>Turn every car's running lights on or off. Called by
        /// <see cref="TimeOfDay.Apply"/>; the hour owns this, not the car.
        /// </summary>
        public static void SetAll(bool on)
        {
            lightsOn = on;
            lightsDecided = true;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i] == null) { all.RemoveAt(i); continue; }
                all[i].Refresh();
            }
            PushGlobals();
        }

        // ------------------------------------------------------------------
        //  The halogen numbers. Colour is shared by the lens, the beam on the
        //  world and the beam in the air, or they read as three lights.
        // ------------------------------------------------------------------
        /// <summary>Tungsten-halogen: warm white, a shade of yellow, nothing
        /// like an LED's blue-white.</summary>
        public static readonly Color Halogen = new Color(1.00f, 0.86f, 0.62f);
        /// <summary>Metres of useful beam. A halogen low beam lights the
        /// road to about this; the night fog closes at 190.</summary>
        public const float BeamRange = 75f;
        /// <summary>Multiplier on the colour as it reaches the world.</summary>
        public const float BeamIntensity = 2.0f;
        /// <summary>Half-spread of the beam, outer edge and full-strength
        /// core, in degrees.</summary>
        public const float BeamOuterDeg = 34f, BeamInnerDeg = 11f;
        /// <summary>The axis is aimed this far below the body's level (a low
        /// beam is), and this far outward on each side (two lobes that merge
        /// a few metres out).</summary>
        public const float BeamDipDeg = 1.5f, BeamToeDeg = 2f;
        /// <summary>The cone in the air: length and its end radii.</summary>
        public const float ConeLength = 13f;
        static readonly Vector2 ConeStart = new Vector2(0.10f, 0.07f);
        static readonly Vector2 ConeEnd = new Vector2(2.4f, 1.3f);

        MeshRenderer[] headLens = new MeshRenderer[2];
        MeshRenderer[] tailLens = new MeshRenderer[2];
        MeshRenderer[] beams = new MeshRenderer[2];
        Transform lampRoot;
        CarBody body;
        Mesh fittedMesh;
        Vector3 fitCenter, fitSize;
        bool braking;

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            Refresh();
        }

        void OnDisable()
        {
            all.Remove(this);
            // The last car out clears the table. A global nothing writes is
            // a stale one, not an empty one.
            if (all.Count == 0) PushGlobals();
        }

        void Start()
        {
            if (car == null) car = GetComponent<CarController>();
            if (box == null) box = GetComponent<BoxCollider>();
            Build();
            // The hour is usually applied before this car exists (the applier
            // runs in RaceManager's Start); pick up whatever it decided. Only
            // when nothing has: SetAll folds the weather in and the preset
            // alone does not.
            if (!lightsDecided) lightsOn = TimeOfDay.At(TimeOfDay.Current).lightsOn;
            Refresh();
        }

        /// <summary>
        /// Re-fit if the shell changed. CarBody can re-fit a car after this
        /// component has already built itself — the LifeSim hands over a grid
        /// during Start, and component Start order is undefined — so the fit
        /// is re-checked rather than trusted once.
        /// </summary>
        void LateUpdate()
        {
            if (ShellChanged()) Fit();

            bool nowBraking = car != null && (car.brakeInput > 0.15f || car.handbrakeInput);
            if (nowBraking != braking)
            {
                braking = nowBraking;
                ApplyBrake();
            }

            // Once per frame for the whole field, by whichever car runs first.
            if (pushedFrame != Time.frameCount)
            {
                pushedFrame = Time.frameCount;
                PushGlobals();
            }
        }

        bool ShellChanged()
        {
            if (body != null && body.bodyFilter != null)
                return body.bodyFilter.sharedMesh != fittedMesh;
            return box != null && (box.center != fitCenter || box.size != fitSize);
        }

        void ApplyBrake()
        {
            var mat = braking ? TailBrightMat : TailDimMat;
            for (int i = 0; i < 2; i++)
            {
                if (tailLens[i] == null) continue;
                tailLens[i].sharedMaterial = mat;
                tailLens[i].enabled = lightsOn || braking;
            }
        }

        /// <summary>
        /// Build the lamps outside play mode and force them on or off — and,
        /// optionally, force the brakes on so a picture can show the brake
        /// lights, which Update would otherwise only ever show for the frames
        /// a pedal is down.
        ///
        /// Only the screenshot tool calls this. Whether a lamp ends up buried
        /// in the bodywork of one of the shells is a purely visual failure
        /// that throws nothing, and the cheapest way to catch it is a picture.
        /// </summary>
        public void PreviewBuild(bool lit, bool brake = false)
        {
            if (car == null) car = GetComponent<CarController>();
            if (box == null) box = GetComponent<BoxCollider>();
            Build();
            lightsOn = lit;
            lightsDecided = true;
            braking = brake;
            ApplyBrake();
            Refresh();
            PushGlobals();
        }

        void Refresh()
        {
            for (int i = 0; i < 2; i++)
            {
                if (headLens[i] != null) headLens[i].enabled = lightsOn;
                if (beams[i] != null) beams[i].enabled = lightsOn;
                if (tailLens[i] != null) tailLens[i].enabled = lightsOn || braking;
            }
        }

        void Build()
        {
            if (headLens[0] != null) return;
            if (body == null) body = GetComponent<CarBody>();
            lampRoot = body != null && body.bodyRoot != null ? body.bodyRoot : transform;
            for (int i = 0; i < 2; i++)
            {
                headLens[i] = MakeQuad("Headlight" + i, lampRoot, HeadMat);
                tailLens[i] = MakeQuad("Taillight" + i, lampRoot, TailDimMat);
                beams[i] = MakeMesh("Beam" + i, lampRoot, ConeMesh, BeamMat);
            }
            Fit();
        }

        /// <summary>
        /// The shell's extents in the CAR's frame: the body mesh's bounds
        /// pushed through the body's yaw and offsets (CarBody.Apply sets
        /// exactly those on the body root, scale one). A 180 yaw swaps nose
        /// and tail, which is why the eight corners go through the matrix
        /// rather than the centre and size.
        /// </summary>
        bool MeasureShell(out Bounds b)
        {
            b = default;
            var def = body != null ? body.Def : null;
            if (def == null || def.bodyMesh == null) return false;
            var m = Matrix4x4.TRS(new Vector3(0f, def.bodyYOffset, def.bodyZOffset),
                                  Quaternion.Euler(0f, def.bodyYaw, 0f), Vector3.one);
            var mb = def.bodyMesh.bounds;
            b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? mb.min.x : mb.max.x,
                                         (c & 2) == 0 ? mb.min.y : mb.max.y,
                                         (c & 4) == 0 ? mb.min.z : mb.max.z);
                b.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return true;
        }

        void Fit()
        {
            Bounds b;
            if (!MeasureShell(out b))
            {
                // No shell to measure: the collider is all there is.
                b = box != null ? new Bounds(box.center, box.size)
                                : new Bounds(new Vector3(0f, 0.72f, 0.05f), new Vector3(1.72f, 1.0f, 4.1f));
            }
            fittedMesh = body != null && body.bodyFilter != null ? body.bodyFilter.sharedMesh : null;
            if (box != null) { fitCenter = box.center; fitSize = box.size; }

            float halfW = b.extents.x;
            float noseZ = b.max.z, tailZ = b.min.z;
            float minY = b.min.y, h = b.size.y;
            float headY = minY + h * 0.40f;
            float tailY = minY + h * 0.52f;
            float lensW = Mathf.Clamp(b.size.x * 0.26f, 0.22f, 0.55f);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Place(headLens[i].transform,
                      new Vector3(side * halfW * 0.62f, headY, noseZ + 0.03f),
                      Quaternion.LookRotation(Vector3.forward, Vector3.up),
                      new Vector3(lensW, lensW * 0.62f, 1f));
                Place(tailLens[i].transform,
                      new Vector3(side * halfW * 0.66f, tailY, tailZ - 0.03f),
                      Quaternion.LookRotation(Vector3.back, Vector3.up),
                      new Vector3(lensW * 0.9f, lensW * 0.5f, 1f));
                // The beam starts a hand inside the lens so the cone's base is
                // hidden in the bodywork, dips a degree and a half and toes
                // out by two: two lobes that merge into one a few metres on.
                Place(beams[i].transform,
                      new Vector3(side * halfW * 0.62f, headY, noseZ - 0.05f),
                      Quaternion.Euler(BeamDipDeg, side * BeamToeDeg, 0f),
                      Vector3.one);
            }
        }

        /// <summary>Put a lamp at a pose given in the CAR's frame, whichever
        /// transform it actually hangs from.</summary>
        void Place(Transform t, Vector3 carPos, Quaternion carRot, Vector3 scale)
        {
            if (lampRoot == transform)
            {
                t.localPosition = carPos;
                t.localRotation = carRot;
            }
            else
            {
                var def = body != null ? body.Def : null;
                var bodyRot = Quaternion.Euler(0f, def != null ? def.bodyYaw : 0f, 0f);
                var bodyPos = def != null ? new Vector3(0f, def.bodyYOffset, def.bodyZOffset) : Vector3.zero;
                t.localPosition = Quaternion.Inverse(bodyRot) * (carPos - bodyPos);
                t.localRotation = Quaternion.Inverse(bodyRot) * carRot;
            }
            t.localScale = scale;
        }

        // ------------------------------------------------------------------
        //  The global table PSXHeadlights.cginc reads.
        // ------------------------------------------------------------------
        public const int MaxLights = 8;
        static readonly Vector4[] gPos = new Vector4[MaxLights];
        static readonly Vector4[] gFwd = new Vector4[MaxLights];
        static readonly Vector4[] gRight = new Vector4[MaxLights];
        static readonly Vector4[] gColor = new Vector4[MaxLights];
        static readonly List<CarLights> sorted = new List<CarLights>();
        static int pushedFrame = -1;
        static readonly int CountId = Shader.PropertyToID("_PSXHeadCount");
        static readonly int PosId = Shader.PropertyToID("_PSXHeadPos");
        static readonly int FwdId = Shader.PropertyToID("_PSXHeadFwd");
        static readonly int RightId = Shader.PropertyToID("_PSXHeadRight");
        static readonly int ColorId = Shader.PropertyToID("_PSXHeadColor");

        /// <summary>How many lamps the table holds right now. For tests.</summary>
        public static int PushedCount { get; private set; }

        /// <summary>
        /// Fill the table from every lit car, nearest the camera first, two
        /// lamps per car, and push it. Eight slots is four cars, which is a
        /// grid; the fifth car back is a pair of lens sprites and no beam,
        /// which nobody has ever seen from the driver's seat.
        /// </summary>
        public static void PushGlobals()
        {
            int n = 0;
            if (lightsOn)
            {
                sorted.Clear();
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var l = all[i];
                    if (l == null) { all.RemoveAt(i); continue; }
                    if (l.isActiveAndEnabled && l.beams[0] != null) sorted.Add(l);
                }
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 eye = cam.transform.position;
                    sorted.Sort((a, b) => (a.transform.position - eye).sqrMagnitude
                                   .CompareTo((b.transform.position - eye).sqrMagnitude));
                }
                float cosOuter = Mathf.Cos(BeamOuterDeg * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(BeamInnerDeg * Mathf.Deg2Rad);
                var col = Halogen * BeamIntensity;
                foreach (var l in sorted)
                {
                    for (int i = 0; i < 2 && n < MaxLights; i++)
                    {
                        var t = l.beams[i].transform;
                        Vector3 p = t.position, f = t.forward, r = t.right;
                        gPos[n] = new Vector4(p.x, p.y, p.z, BeamRange);
                        gFwd[n] = new Vector4(f.x, f.y, f.z, cosOuter);
                        gRight[n] = new Vector4(r.x, r.y, r.z, cosInner);
                        gColor[n] = new Vector4(col.r, col.g, col.b, 1f);
                        n++;
                    }
                }
            }
            for (int i = n; i < MaxLights; i++)
                gPos[i] = gFwd[i] = gRight[i] = gColor[i] = Vector4.zero;
            PushedCount = n;
            Shader.SetGlobalFloat(CountId, n);
            Shader.SetGlobalVectorArray(PosId, gPos);
            Shader.SetGlobalVectorArray(FwdId, gFwd);
            Shader.SetGlobalVectorArray(RightId, gRight);
            Shader.SetGlobalVectorArray(ColorId, gColor);
        }

        static MeshRenderer MakeQuad(string name, Transform parent, Material mat)
            => MakeMesh(name, parent, QuadMesh, mat);

        static MeshRenderer MakeMesh(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        // ------------------------------------------------------------------
        //  Shared assets. Built once per run and reused by every car: two
        //  opponents' headlights should not be two materials.
        // ------------------------------------------------------------------
        static Mesh quadMesh;
        static Mesh QuadMesh
        {
            get
            {
                if (quadMesh != null) return quadMesh;
                quadMesh = new Mesh { name = "GlowQuad" };
                quadMesh.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),   new Vector3(0.5f, -0.5f, 0f),
                };
                quadMesh.uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(1f, 1f), new Vector2(1f, 0f),
                };
                quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quadMesh.RecalculateNormals();
                quadMesh.RecalculateBounds();
                return quadMesh;
            }
        }

        static Mesh coneMesh;
        /// <summary>
        /// The beam in the air: an elliptical cone along +Z from the lens,
        /// wider than it is tall like the beam it stands for. Normals are
        /// analytic (the cross of the around and along tangents) so the seam
        /// vertex carries the same normal as its twin and PSX/Beam's
        /// silhouette term shows no line down the cone.
        /// </summary>
        static Mesh ConeMesh
        {
            get
            {
                if (coneMesh != null) return coneMesh;
                const int seg = 16;
                var verts = new Vector3[(seg + 1) * 2];
                var norms = new Vector3[(seg + 1) * 2];
                var uvs = new Vector2[(seg + 1) * 2];
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    for (int r = 0; r < 2; r++)
                    {
                        Vector2 rad = r == 0 ? ConeStart : ConeEnd;
                        int k = s * 2 + r;
                        verts[k] = new Vector3(rad.x * ca, rad.y * sa, r * ConeLength);
                        var around = new Vector3(-rad.x * sa, rad.y * ca, 0f);
                        var along = new Vector3((ConeEnd.x - ConeStart.x) * ca,
                                                (ConeEnd.y - ConeStart.y) * sa, ConeLength);
                        norms[k] = Vector3.Cross(around, along).normalized;
                        uvs[k] = new Vector2(s / (float)seg, r);
                    }
                }
                var tris = new int[seg * 6];
                for (int s = 0; s < seg; s++)
                {
                    int a0 = s * 2, a1 = s * 2 + 1, b0 = (s + 1) * 2, b1 = (s + 1) * 2 + 1;
                    tris[s * 6 + 0] = a0; tris[s * 6 + 1] = a1; tris[s * 6 + 2] = b0;
                    tris[s * 6 + 3] = a1; tris[s * 6 + 4] = b1; tris[s * 6 + 5] = b0;
                }
                coneMesh = new Mesh { name = "BeamCone" };
                coneMesh.vertices = verts;
                coneMesh.normals = norms;
                coneMesh.uv = uvs;
                coneMesh.triangles = tris;
                coneMesh.RecalculateBounds();
                return coneMesh;
            }
        }

        static Texture2D glowTex;
        /// <summary>Radial falloff, generated rather than imported: one 48x48
        /// blob is every lamp in the game and an asset for it would be one more
        /// thing to keep in sync with the shader.</summary>
        static Texture2D GlowTex
        {
            get
            {
                if (glowTex != null) return glowTex;
                const int n = 48;
                glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - (n - 1) * 0.5f) / (n * 0.5f);
                        float dy = (y - (n - 1) * 0.5f) / (n * 0.5f);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a;                       // tighter core, softer edge
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                glowTex.SetPixels32(px);
                glowTex.Apply();
                return glowTex;
            }
        }

        static Material MakeMat(string shaderName, string name, Color tint, float strength, bool masked)
        {
            var shader = Shader.Find(shaderName);
            // A missing shader would otherwise draw magenta rectangles all over
            // the cars, which reads as damage rather than as a build problem.
            if (shader == null) { Debug.LogWarning("CarLights: " + shaderName + " shader missing"); return null; }
            var m = new Material(shader) { name = name };
            if (masked) m.mainTexture = GlowTex;
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_Strength")) m.SetFloat("_Strength", strength);
            return m;
        }

        static Material headMat, tailDim, tailBright, beamMat;
        /// <summary>The lens, in halogen, a touch hotter than the beam so it
        /// reads as the source.</summary>
        static Material HeadMat => headMat != null ? headMat
            : headMat = MakeMat("PSX/Glow", "Headlight", Halogen, 1.6f, true);
        static Material TailDimMat => tailDim != null ? tailDim
            : tailDim = MakeMat("PSX/Glow", "Taillight", new Color(1.00f, 0.16f, 0.10f), 0.9f, true);
        static Material TailBrightMat => tailBright != null ? tailBright
            : tailBright = MakeMat("PSX/Glow", "Brakelight", new Color(1.00f, 0.12f, 0.06f), 2.6f, true);
        /// <summary>The beam in the air. Same halogen; PSX/Beam fades it out
        /// by daylight on its own.</summary>
        static Material BeamMat => beamMat != null ? beamMat
            : beamMat = MakeMat("PSX/Beam", "HeadlightBeam", Halogen, 0.45f, false);
    }
}
