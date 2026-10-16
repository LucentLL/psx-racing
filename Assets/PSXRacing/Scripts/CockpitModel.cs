using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace PSXRacing
{
    /// <summary>
    /// THE OWNER'S COCKPIT v08 AS DATA (2026-10-16): the parts, the layout
    /// and the materials, loaded once from Resources/Cockpit and shared by
    /// every car's <see cref="CockpitInterior"/>.
    ///
    /// tools/cockpit/cockpit_v08.py converts his LHD .blend into
    /// v08_geo.bytes (every part quantized: positions and UVs to 16 bits over
    /// the part's box, normals to 8, the vertex colour that carries a flat
    /// material's colour and roughness) and v08_layout.json; the RHD model is
    /// the same parts under a -1 x scale (the two files are exact mirrors).
    /// cockpit_v08_tex.py packs his twelve 1024 textures into four 512
    /// surface tiles and the atlas. 1.25 MB of geometry + ~2.1 MB of GPU
    /// textures - and MAIN's WebGL.data is near GitHub's 100 MB wall, so
    /// none of it is in Resources: the geometry and the textures ship in
    /// StreamingAssets/cockpit.bundle (built by CockpitBundle.cs with the
    /// player, LZ4), fetched the first time the cockpit view is entered
    /// (<see cref="Request"/>) and kept. Until it is <see cref="Ready"/> the
    /// view is what it always was without a cabin: a camera in the seat with
    /// the 2D binnacle. Only the 4 KB layout is in Resources (the fit and the
    /// near plane need it before the bundle arrives). In the editor the
    /// assets load straight from CockpitSrc unless PSX_COCKPIT_BUNDLE=1.
    /// </summary>
    public static class CockpitModel
    {
        public const string Dir = "Cockpit/";
        public const string LayoutPath = Dir + "v08_layout";
        /// <summary>A Resources TextAsset holding the bundle's content hash
        /// (the URL's ?v=), written by CockpitBundle.Build.</summary>
        public const string VersionPath = Dir + "v08_bundle";
        public const string BundleFile = "cockpit.bundle";
        /// <summary>Where the bundle's assets live in the project (outside
        /// Resources, so the main data never carries them).</summary>
        public const string EditorSrc = "Assets/PSXRacing/CockpitSrc/";
        public const string GeoName = "v08_geo", AtlasName = "v08_atlas", SkinName = "hands_skin";
        /// <summary>Every asset the bundle carries (file names in CockpitSrc).</summary>
        public static readonly string[] BundleAssets =
            { "v08_geo.bytes", "v08_vinyl.png", "v08_abs.png", "v08_headliner.png", "v08_leather.png", "v08_atlas.png", "hands_skin.png",
              "arms_geo.bytes" };
        public static readonly string[] Surfaces = { "vinyl", "abs", "headliner", "leather" };

        // ------------------------------------------------------------------
        //  The layout (model frame: the owner's root, Unity axes, LHD)
        // ------------------------------------------------------------------

        [System.Serializable] public class WheelDef { public Vector3 pivot; public Quaternion rot; public float tiltDeg, rimRadius, rimZ, tube; }
        [System.Serializable] public class StackDef { public Vector3 centre, normal; public float squabZ; }
        [System.Serializable] public class RoomDef { public float floorY, firewallZ, doorX, consoleX; }
        [System.Serializable]
        public class GaugeDef
        {
            public string key, part;
            public Vector3 centre, normal;
            public float radius;
            /// <summary>+1 when the canvas's U runs to the driver's right (+x).</summary>
            public int uRight, vUp;
        }
        [System.Serializable]
        public class PaneDef
        {
            public string key, part;
            /// <summary>World up, rearward and +x as UV-space directions.</summary>
            public Vector2 up, rear, right;
            public float metresU, metresV;
            public Vector3 centre;
        }
        [System.Serializable] public class MirrorDef { public string part; public Vector3 centre; public int uRight, vUp; public float width, height; }
        [System.Serializable] public class SurfaceDef { public Color vinyl, abs, headliner, leather; }

        [System.Serializable]
        public class Layout
        {
            public Vector3 eye, seatSurface, cargoSpawn, footwell, sodaSpawn;
            /// <summary>The owner's driver camera: pitch down (deg), vertical
            /// field (deg), near clip (authored m).</summary>
            public float camPitch, camVFov, camNear;
            public float dashFrontZ;
            /// <summary>The dash's top where the windscreen meets it, centre
            /// band, authored metres: the line from the eye over it is the one
            /// the game stands on the shell's cowl (CockpitInterior.SightDrop).</summary>
            public Vector3 screenBase;
            public GaugeDef[] gauges;
            public PaneDef[] glass;
            public MirrorDef mirror;
            public WheelDef wheel;
            public StackDef stack;
            public RoomDef room;
            /// <summary>Nearest visible surface from the eye, authored metres
            /// of camera depth, ahead and at full look either way.</summary>
            public float nearForward, nearLook;
            /// <summary>The same at full look toward the passenger seat
            /// (dipped) and toward the driver's door.</summary>
            public float nearPassenger, nearDoor;
            public SurfaceDef surfaces;

            /// <summary>The cargo's frame for an RHD cabin: the passenger seat
            /// mirrored to +x's other side (the cargo itself is never drawn
            /// mirrored - its motion is the car's).</summary>
            public Layout MirroredCargo()
            {
                var m = (Layout)MemberwiseClone();
                m.stack = new StackDef
                {
                    centre = new Vector3(-stack.centre.x, stack.centre.y, stack.centre.z),
                    normal = new Vector3(-stack.normal.x, stack.normal.y, stack.normal.z),
                    squabZ = stack.squabZ,
                };
                m.room = new RoomDef { floorY = room.floorY, firewallZ = room.firewallZ, doorX = -room.doorX, consoleX = -room.consoleX };
                return m;
            }
        }

        static Layout layout;
        static bool layoutTried;

        /// <summary>The converter's layout, or null if it is missing.</summary>
        public static Layout L
        {
            get
            {
                if (layout == null && !layoutTried)
                {
                    layoutTried = true;
                    var ta = Resources.Load<TextAsset>(LayoutPath);
                    if (ta != null) layout = JsonUtility.FromJson<Layout>(ta.text);
                    if (layout == null || layout.wheel == null || layout.stack == null || layout.room == null
                        || layout.gauges == null || layout.gauges.Length != 4 || layout.glass == null || layout.surfaces == null)
                    {
                        Debug.LogWarning("[Cockpit] Resources/" + LayoutPath + " missing or incomplete");
                        layout = null;
                    }
                }
                return layout;
            }
        }

        // ------------------------------------------------------------------
        //  The parts
        // ------------------------------------------------------------------

        public class Part
        {
            public string name;
            public Mesh mesh;
            /// <summary>The game material of each submesh.</summary>
            public string[] mats;
            /// <summary>A glass part's least-squares plane (model space,
            /// authored metres): its centroid and unit normal, sign
            /// arbitrary. Measured while the vertices are in hand - the
            /// player's meshes are not readable after the upload.</summary>
            public Vector3 planePoint, planeNormal;
            /// <summary>A glass part's vertices (model space), for framing.</summary>
            public Vector3[] points;
        }

        /// <summary>The plane that best fits `pos` (Ernerfeldt's
        /// determinant pick over the covariance; fine for a broad pane).</summary>
        public static void FitPlane(Vector3[] pos, out Vector3 centroid, out Vector3 normal)
        {
            centroid = Vector3.zero;
            normal = Vector3.up;
            if (pos == null || pos.Length < 3) return;
            foreach (var p in pos) centroid += p;
            centroid /= pos.Length;
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pos)
            {
                Vector3 r = p - centroid;
                xx += r.x * r.x; xy += r.x * r.y; xz += r.x * r.z;
                yy += r.y * r.y; yz += r.y * r.z; zz += r.z * r.z;
            }
            double dx = yy * zz - yz * yz, dy = xx * zz - xz * xz, dz = xx * yy - xy * xy;
            double m = System.Math.Max(dx, System.Math.Max(dy, dz));
            if (m <= 0) return;
            Vector3 n = m == dx ? new Vector3((float)dx, (float)(xz * yz - xy * zz), (float)(xy * yz - xz * yy))
                      : m == dy ? new Vector3((float)(xz * yz - xy * zz), (float)dy, (float)(xy * xz - yz * xx))
                      : new Vector3((float)(xy * yz - xz * yy), (float)(xy * xz - yz * xx), (float)dz);
            if (n.sqrMagnitude > 0f) normal = n.normalized;
        }

        // ------------------------------------------------------------------
        //  The on-demand bundle
        // ------------------------------------------------------------------

        enum Fetch { None, Loading, Ready, Failed }
        static Fetch fetch;
        static AssetBundle bundle;
        static bool failLogged;

        /// <summary>The model's assets are here (parts and materials can build).</summary>
        public static bool Ready => fetch == Fetch.Ready;
        public static string State => fetch.ToString();

#if UNITY_EDITOR
        static bool UseBundle => Application.isPlaying && System.Environment.GetEnvironmentVariable("PSX_COCKPIT_BUNDLE") == "1";
#else
        static bool UseBundle => true;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Play mode without a domain reload: an editor fetch state must not
            // outlive its bundle.
            if (bundle == null && fetch != Fetch.Ready) fetch = Fetch.None;
        }

        public static string BundleUrl
        {
            get
            {
                string url = Application.streamingAssetsPath + "/" + BundleFile;
                if (url.Contains("://"))
                {
                    var v = Resources.Load<TextAsset>(VersionPath);
                    if (url.StartsWith("http") && v != null && v.text.Trim().Length > 0) url += "?v=" + v.text.Trim();
                    return url;
                }
                return "file:///" + url.TrimStart('/');
            }
        }

        /// <summary>Start loading the model: the bundle download in a player
        /// (once; the browser caches it under its version), the project files
        /// in the editor (at once). Safe to call every frame.</summary>
        public static void Request()
        {
            if (fetch != Fetch.None) return;
            if (!UseBundle)
            {
#if UNITY_EDITOR
                bool have = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(EditorSrc + GeoName + ".bytes") != null;
                if (have) fetch = Fetch.Ready; else Failed(EditorSrc, "v08_geo.bytes missing");
#endif
                return;
            }
            fetch = Fetch.Loading;
            string url = BundleUrl;
            float t0 = Time.realtimeSinceStartup;
            try
            {
                var req = UnityWebRequestAssetBundle.GetAssetBundle(url);
                req.SendWebRequest().completed += _ => OnBundle(req, url, t0);
                Debug.Log("[Cockpit] fetching the v08 cockpit: " + url);
            }
            catch (System.Exception e) { Failed(url, e.Message); }
        }

        static void OnBundle(UnityWebRequest req, string url, float t0)
        {
            AssetBundle b = null;
            string err = null;
            ulong bytes = req.downloadedBytes;
            try
            {
                if (req.result == UnityWebRequest.Result.Success) b = DownloadHandlerAssetBundle.GetContent(req);
                else err = req.error;
            }
            catch (System.Exception e) { err = e.Message; }
            finally { req.Dispose(); }
            if (b == null) { Failed(url, err ?? "not an AssetBundle"); return; }
            bundle = b;
            fetch = Fetch.Ready;
            Debug.Log($"[Cockpit] v08 cockpit ready: {bytes / 1048576f:F2} MB in {Time.realtimeSinceStartup - t0:F1} s");
        }

        static void Failed(string where, string why)
        {
            fetch = Fetch.Failed;
            if (failLogged) return;
            failLogged = true;
            Debug.LogWarning("[Cockpit] the v08 cockpit is unavailable (" + where + ": " + why + ") - the seat camera without a cabin");
        }

        /// <summary>One of the bundle's assets by name (no extension).</summary>
        static T Asset<T>(string name) where T : Object
        {
            if (bundle != null) return bundle.LoadAsset<T>(name);
#if UNITY_EDITOR
            string ext = typeof(T) == typeof(TextAsset) ? ".bytes" : ".png";
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(EditorSrc + name + ext);
#else
            return null;
#endif
        }

        /// <summary>The driver's arms (CockpitArms reads it), or null while
        /// the model is not <see cref="Ready"/>.</summary>
        public static TextAsset ArmsAsset()
        {
            Request();
            return Ready ? Asset<TextAsset>(CockpitArms.GeoName) : null;
        }

        static Dictionary<string, Part> parts;

        /// <summary>Every part by name, or null while the model is not
        /// <see cref="Ready"/> (this starts the load) or if it is broken.</summary>
        public static Dictionary<string, Part> Parts
        {
            get
            {
                if (parts != null) return parts.Count > 0 ? parts : null;
                Request();
                if (!Ready) return null;
                parts = new Dictionary<string, Part>();
                var ta = Asset<TextAsset>(GeoName);
                if (ta == null) { Debug.LogWarning("[Cockpit] " + GeoName + " missing from the cockpit bundle"); return null; }
                try { Read(ta.bytes, parts); }
                catch (System.Exception e) { Debug.LogWarning("[Cockpit] " + GeoName + " unreadable: " + e.Message); parts.Clear(); }
                return parts.Count > 0 ? parts : null;
            }
        }

        static string Str(BinaryReader r) => new string(r.ReadChars(r.ReadByte()));
        static Vector3 V3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>The format is cockpit_v08.py's (its header says it).</summary>
        static void Read(byte[] bytes, Dictionary<string, Part> into)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes), System.Text.Encoding.ASCII))
            {
                if (new string(r.ReadChars(4)) != "PSXC" || r.ReadInt32() != 1) throw new IOException("not a v1 PSXC file");
                int n = r.ReadInt32();
                for (int p = 0; p < n; p++)
                {
                    string name = Str(r);
                    int nv = r.ReadInt32(), ns = r.ReadInt32();
                    Vector3 pmin = V3(r), pspan = V3(r);
                    Vector2 umin = new Vector2(r.ReadSingle(), r.ReadSingle()), uspan = new Vector2(r.ReadSingle(), r.ReadSingle());
                    var pos = new Vector3[nv];
                    var nrm = new Vector3[nv];
                    var uv = new Vector2[nv];
                    var col = new Color32[nv];
                    const float Q = 1f / 65535f;
                    for (int i = 0; i < nv; i++)
                        pos[i] = new Vector3(pmin.x + r.ReadUInt16() * Q * pspan.x, pmin.y + r.ReadUInt16() * Q * pspan.y, pmin.z + r.ReadUInt16() * Q * pspan.z);
                    for (int i = 0; i < nv; i++)
                        nrm[i] = new Vector3(r.ReadSByte(), r.ReadSByte(), r.ReadSByte()).normalized;
                    for (int i = 0; i < nv; i++)
                        uv[i] = new Vector2(umin.x + r.ReadUInt16() * Q * uspan.x, umin.y + r.ReadUInt16() * Q * uspan.y);
                    for (int i = 0; i < nv; i++)
                        col[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                    var mesh = new Mesh { name = "Cockpit_" + name, hideFlags = HideFlags.DontSave };
                    if (nv > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.vertices = pos;
                    mesh.normals = nrm;
                    mesh.uv = uv;
                    mesh.colors32 = col;
                    mesh.subMeshCount = ns;
                    var mats = new string[ns];
                    for (int s = 0; s < ns; s++)
                    {
                        mats[s] = Str(r);
                        int ni = r.ReadInt32();
                        var idx = new int[ni];
                        if (nv > 65535) for (int i = 0; i < ni; i++) idx[i] = (int)r.ReadUInt32();
                        else for (int i = 0; i < ni; i++) idx[i] = r.ReadUInt16();
                        mesh.SetTriangles(idx, s, false);
                    }
                    mesh.RecalculateBounds();
                    // UV channel 0 is the UV each material's normal map uses.
                    mesh.RecalculateTangents();
                    // Kept readable in the editor, where the shot tool measures it.
                    mesh.UploadMeshData(!Application.isEditor);
                    var part = new Part { name = name, mesh = mesh, mats = mats };
                    if (name.StartsWith("glass_")) { FitPlane(pos, out part.planePoint, out part.planeNormal); part.points = pos; }
                    into[name] = part;
                }
            }
        }

        // ------------------------------------------------------------------
        //  The materials (PSX/Cabin; the gauges and glass are per instance)
        // ------------------------------------------------------------------

        static Dictionary<string, Material> mats;

        /// <summary>The shared material for a game material key, or null
        /// (also while the model is not <see cref="Ready"/>).</summary>
        public static Material Mat(string key)
        {
            if (mats == null)
            {
                Request();
                if (!Ready) return null;
                BuildMats();
            }
            return mats.TryGetValue(key, out var m) ? m : null;
        }

        static void BuildMats()
        {
            mats = new Dictionary<string, Material>();
            var sh = Shader.Find("PSX/Cabin");
            var lay = L;
            if (sh == null || lay == null) { Debug.LogWarning("[Cockpit] PSX/Cabin shader or layout missing"); return; }
            Material Make(string key, float mode)
            {
                var m = new Material(sh) { name = "Cockpit_" + key, hideFlags = HideFlags.HideAndDontSave };
                m.SetFloat("_Mode", mode);
                mats[key] = m;
                return m;
            }
            var s = lay.surfaces;
            var cols = new[] { s.vinyl, s.abs, s.headliner, s.leather };
            for (int i = 0; i < Surfaces.Length; i++)
            {
                var m = Make(Surfaces[i], 0f);
                m.SetTexture("_SurfTex", Asset<Texture2D>("v08_" + Surfaces[i]));
                m.SetColor("_BaseColor", cols[i]);
            }
            var trim = Make("trim", 1f);
            trim.SetTexture("_AtlasTex", Asset<Texture2D>(AtlasName));
            trim.SetFloat("_Rough", 0.83f);
            var skin = Make("skin", 1f);
            skin.SetTexture("_AtlasTex", Asset<Texture2D>(SkinName));
            skin.SetFloat("_Rough", 0.6f);
            Make("solid", 2f);
        }
    }
}
