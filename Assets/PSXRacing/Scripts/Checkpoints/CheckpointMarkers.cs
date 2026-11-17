using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE CHECKPOINT IN THE WORLD (DESIGN_v2 3.3), 1999-plausible light, nothing in a lane:
    /// - a SEARCHLIGHT beam (the period's grand-opening "sky tracker") rising 260 m from the next gate, warm xenon
    ///   white, slowly sweeping +-6 degrees at 0.12 Hz so it reads as a searchlight, not a pillar; the gate after it a
    ///   dimmer amber; every other gate dark;
    /// - a glow BAND lying across each span of the gate, breathing at 1 Hz, and short glow posts on the kerb lines.
    /// Additive PSX/Glow quads (the lamps' and the free-roam start gates' shader). No LEDs, no numbers or text in the
    /// world (the number is on the HUD), no cones, barrels or barricades. Two gates lit at a time, about 8 quads each.
    /// PSX_CP_MARKERS=0: none (HUD and minimap only).
    /// </summary>
    public class CheckpointMarkers : MonoBehaviour
    {
        CheckpointRace race;
        RaceManager rm;
        class Lit
        {
            public Transform root, beam, core;
            public Transform[] bands, posts;
            public Material beamMat, coreMat, bandMat, postMat;
            public int gate = -1;
        }
        Lit next, after;
        static Texture2D beamTex, bandTex, postTex;
        static Mesh beamMesh;
        public static readonly Color Xenon = new Color(1f, 0.902f, 0.69f);    // #FFE6B0
        public static readonly Color Amber = new Color(1f, 0.706f, 0.353f);   // #FFB45A
        public const float BeamM = 260f;
        bool on;

        void Start()
        {
            on = CheckpointFile.On("PSX_CP_MARKERS");
            race = GetComponent<CheckpointRace>();
            rm = GetComponent<RaceManager>();
            if (!on || race == null) { enabled = false; return; }
            next = Make("CPMarkNext");
            after = Make("CPMarkAfter");
        }

        Lit Make(string name)
        {
            EnsureAssets();
            var l = new Lit();
            l.root = new GameObject(name).transform;
            l.root.SetParent(transform, false);
            l.beamMat = Mat(beamTex, Xenon, 1f);
            l.coreMat = Mat(beamTex, Xenon, 1f);
            l.bandMat = Mat(bandTex, Xenon, 1f);
            l.postMat = Mat(postTex, Xenon, 1f);
            l.beam = MeshObj(l.root, "Beam", beamMesh, l.beamMat);
            l.core = MeshObj(l.root, "BeamCore", beamMesh, l.coreMat);
            l.core.localScale = new Vector3(0.45f, 1f, 1f);
            l.bands = new Transform[4]; l.posts = new Transform[8];
            for (int i = 0; i < l.bands.Length; i++) l.bands[i] = Quad(l.root, "Band", l.bandMat);
            for (int i = 0; i < l.posts.Length; i++) l.posts[i] = Quad(l.root, "Post", l.postMat);
            l.root.gameObject.SetActive(false);
            return l;
        }

        void LateUpdate()
        {
            if (!on || race == null || !race.Ready || rm == null || rm.playerCar == null) return;
            var c = race.Of(rm.playerCar);
            var p = rm.GetProgress(rm.playerCar);
            bool done = c == null || (p != null && p.finished);
            int k = done ? -1 : c.next;
            int k2 = k >= 0 ? race.NextAfter(k) : -1;
            if (k >= 0 && race.IsFinishGate(c, k)) k2 = -1;   // nothing after the flag
            Place(next, k, true);
            Place(after, k2, false);
            var cam = Camera.main;
            if (cam == null) return;
            float night = TimeOfDay.At(TimeOfDay.Current).lightsOn ? 1f : 0f;
            float rain = WeatherFx.Raining ? 1f : 0f;
            Animate(next, cam, night, rain, 1f, Xenon);
            Animate(after, cam, night, rain, 0.4f, Amber);
        }

        void Place(Lit l, int k, bool isNext)
        {
            if (l.gate == k) return;
            l.gate = k;
            if (k < 0) { l.root.gameObject.SetActive(false); return; }
            var g = race.gates[k];
            l.root.gameObject.SetActive(true);
            l.root.position = g.centre;
            l.root.rotation = Quaternion.LookRotation(new Vector3(g.h.x, 0f, g.h.y), Vector3.up);
            // the bands and posts, span by span, in the root's frame
            int bi = 0, pi = 0;
            foreach (var sp in g.spans)
            {
                if (bi >= l.bands.Length) break;
                Vector3 c0 = new Vector3(sp.p.x, sp.y, sp.p.y);
                float mid = (sp.l0 + sp.l1) * 0.5f, width = (sp.l1 - sp.l0) - 6f;     // kerb to kerb (the +-3 m is the capture)
                Vector3 r3 = new Vector3(sp.r.x, 0f, sp.r.y), h3 = new Vector3(sp.h.x, 0f, sp.h.y);
                var band = l.bands[bi++];
                band.gameObject.SetActive(true);
                band.position = c0 + r3 * mid + Vector3.up * 0.09f;
                band.rotation = Quaternion.LookRotation(Vector3.down, h3);     // lying flat, across the road
                band.localScale = new Vector3(Mathf.Max(3f, width), 1.2f, 1f);
                for (int side = -1; side <= 1; side += 2)
                {
                    float lat = side < 0 ? sp.l0 + 3f : sp.l1 - 3f;
                    for (int x = 0; x < 2 && pi < l.posts.Length; x++)
                    {
                        var post = l.posts[pi++];
                        post.gameObject.SetActive(true);
                        post.position = c0 + r3 * (lat + side * 0.35f) + Vector3.up * 0.55f;
                        post.rotation = Quaternion.LookRotation(x == 0 ? h3 : r3, Vector3.up);
                        post.localScale = new Vector3(0.35f, 1.1f, 1f);
                    }
                }
            }
            for (; bi < l.bands.Length; bi++) l.bands[bi].gameObject.SetActive(false);
            for (; pi < l.posts.Length; pi++) l.posts[pi].gameObject.SetActive(false);
        }

        void Animate(Lit l, Camera cam, float night, float rain, float level, Color col)
        {
            if (l.gate < 0) return;
            Vector3 basePos = l.root.position;
            // the beam faces the camera about Y, and sweeps slowly about the axis it faces along
            Vector3 to = cam.transform.position - basePos; to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) to = Vector3.forward;
            float sweep = 6f * Mathf.Sin(Time.time * 2f * Mathf.PI * 0.12f + l.gate * 1.7f);
            var face = Quaternion.LookRotation(-to.normalized, Vector3.up);
            var rot = face * Quaternion.AngleAxis(sweep, Vector3.forward);
            l.beam.SetPositionAndRotation(basePos, rot);
            l.core.SetPositionAndRotation(basePos, rot);
            // brightness: the floor by day, near full at night and in rain or fog where a real beam shows best;
            // faded inside 40 m of the camera (fill rate) and beyond 3 km
            float dist = to.magnitude;
            float a = Mathf.Lerp(0.25f, 0.9f, night) * (1f + 0.3f * rain) * level;
            float near = Mathf.InverseLerp(15f, 60f, dist), far = 1f - Mathf.InverseLerp(2500f, 3000f, dist);
            l.beamMat.SetFloat("_Strength", 0.55f * a * near * far);
            l.coreMat.SetFloat("_Strength", 0.9f * a * near * far);
            float breathe = 0.8f + 0.2f * Mathf.Sin(Time.time * 2f * Mathf.PI);
            l.bandMat.SetFloat("_Strength", Mathf.Lerp(0.55f, 1.1f, night) * level * breathe);
            l.postMat.SetFloat("_Strength", Mathf.Lerp(0.8f, 1.4f, night) * level);
            l.beamMat.SetColor("_Color", col); l.coreMat.SetColor("_Color", Color.Lerp(col, Color.white, 0.35f));
            l.bandMat.SetColor("_Color", col); l.postMat.SetColor("_Color", col);
        }

        // ------------------------------------------------------------------ assets (made once, never saved)
        static void EnsureAssets()
        {
            if (beamTex != null) return;
            const int W = 32, H = 128;
            beamTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "cp_beam", hideFlags = HideFlags.DontSave };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H;
                    float across = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u)), 1.6f);
                    float up = Mathf.Pow(1f - v, 1.4f) * Mathf.Clamp01(v * 40f);
                    beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, across * up));
                }
            beamTex.Apply();
            bandTex = new Texture2D(64, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "cp_band", hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 64; x++)
                {
                    float u = (x + 0.5f) / 64f * 2f - 1f, v = (y + 0.5f) / 16f * 2f - 1f;
                    float a = Mathf.Clamp01((1f - Mathf.Abs(u)) * 8f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v)), 1.3f);
                    bandTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            bandTex.Apply();
            postTex = new Texture2D(16, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "cp_post", hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 16; x++)
                {
                    float u = (x + 0.5f) / 16f * 2f - 1f, v = (y + 0.5f) / 64f;
                    float a = Mathf.Clamp01(1f - Mathf.Abs(u) * 1.2f) * Mathf.Lerp(1f, 0.15f, v);
                    postTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            postTex.Apply();
            // the beam: a trapezoid, 3 m wide at the road, 9 m at the top, BeamM tall, its base at the origin
            beamMesh = new Mesh { name = "cp_beam", hideFlags = HideFlags.DontSave };
            beamMesh.vertices = new[] { new Vector3(-1.5f, 0f, 0f), new Vector3(1.5f, 0f, 0f), new Vector3(-4.5f, BeamM, 0f), new Vector3(4.5f, BeamM, 0f) };
            beamMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            beamMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            beamMesh.bounds = new Bounds(new Vector3(0f, BeamM * 0.5f, 0f), new Vector3(12f, BeamM, 12f));
        }

        static Material Mat(Texture tex, Color col, float strength)
        {
            var sh = Shader.Find("PSX/Glow");
            if (sh == null) { Debug.LogWarning("[Checkpoints] PSX/Glow shader missing: no markers"); return null; }
            var m = new Material(sh) { name = "CheckpointGlow", hideFlags = HideFlags.DontSave };
            m.mainTexture = tex;
            m.SetColor("_Color", col);
            m.SetFloat("_Strength", strength);
            return m;
        }

        static Transform MeshObj(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        static Mesh quadMesh;
        static Transform Quad(Transform parent, string name, Material mat)
        {
            if (quadMesh == null)
            {
                quadMesh = new Mesh { name = "cp_quad", hideFlags = HideFlags.DontSave };
                quadMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                quadMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                quadMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                quadMesh.RecalculateBounds();
            }
            return MeshObj(parent, name, quadMesh, mat);
        }
    }
}
