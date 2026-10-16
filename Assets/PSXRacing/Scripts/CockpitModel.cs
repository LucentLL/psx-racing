using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
    /// textures, against the ~2 MB the v07 cabin it replaces cost - no
    /// on-demand bundle needed.
    /// </summary>
    public static class CockpitModel
    {
        public const string Dir = "Cockpit/";
        public const string GeoPath = Dir + "v08_geo";
        public const string LayoutPath = Dir + "v08_layout";
        public const string AtlasPath = Dir + "v08_atlas";
        public const string SkinPath = Dir + "hands_skin";
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
        }

        static Dictionary<string, Part> parts;

        /// <summary>Every part by name, or null if the geometry is missing.</summary>
        public static Dictionary<string, Part> Parts
        {
            get
            {
                if (parts != null) return parts.Count > 0 ? parts : null;
                parts = new Dictionary<string, Part>();
                var ta = Resources.Load<TextAsset>(GeoPath);
                if (ta == null) { Debug.LogWarning("[Cockpit] Resources/" + GeoPath + " missing"); return null; }
                try { Read(ta.bytes, parts); }
                catch (System.Exception e) { Debug.LogWarning("[Cockpit] " + GeoPath + " unreadable: " + e.Message); parts.Clear(); }
                Resources.UnloadAsset(ta);
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
                    into[name] = new Part { name = name, mesh = mesh, mats = mats };
                }
            }
        }

        // ------------------------------------------------------------------
        //  The materials (PSX/Cabin; the gauges and glass are per instance)
        // ------------------------------------------------------------------

        static Dictionary<string, Material> mats;

        /// <summary>The shared material for a game material key, or null.</summary>
        public static Material Mat(string key)
        {
            if (mats == null) BuildMats();
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
                m.SetTexture("_SurfTex", Resources.Load<Texture2D>(Dir + "v08_" + Surfaces[i]));
                m.SetColor("_BaseColor", cols[i]);
            }
            var trim = Make("trim", 1f);
            trim.SetTexture("_AtlasTex", Resources.Load<Texture2D>(AtlasPath));
            trim.SetFloat("_Rough", 0.83f);
            var skin = Make("skin", 1f);
            skin.SetTexture("_AtlasTex", Resources.Load<Texture2D>(SkinPath));
            skin.SetFloat("_Rough", 0.6f);
            Make("solid", 2f);
        }
    }
}
