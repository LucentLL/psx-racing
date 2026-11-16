using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE OWNER'S TRAFFIC VEHICLES, BAKED (2026-11-15): "UAA - City Props -
    /// Vehicles" turned into shells the game's traffic can drive
    /// (Resources/TrafficKit.asset, read by <see cref="TrafficModels"/>).
    ///
    /// The pack is store content: gitignored, extracted by
    /// tools/store-assets.ps1 into Assets/StoreAssets/UAAVehicles, and
    /// everything made from it stays under that folder (Gen/). PC content:
    /// the kit is written by a PC pass (PSX_PC_CONTENT=1) with the files
    /// present and DELETED by any other pass, so a web or phone build, or a
    /// checkout without the pack, keeps today's traffic models.
    ///
    /// What the bake does to each of the seventeen vehicles:
    ///   SCALE    to the real car it depicts (Spec.scale), nose +Z, the axle
    ///            midpoint on the ground as the origin: the game's car frame.
    ///   WHEELS   the pack's own wheel mesh, re-centred on its hub, outer face
    ///            +X (CarShell turns it round for the left side); a dual rear
    ///            wheel and the axles past the second go in the kit entry.
    ///   COLLIDER the body's own box (mirrors left out of the width).
    ///   TEXTURE  cleaned (the readable text and the badge shapes painted
    ///            out: Patches), the windows darkened to the car paint
    ///            shader's glass, everything else flagged matte in the alpha
    ///            (a black tyre or grille is not a mirror), and REPAINTED: the
    ///            pack has no colour variety at all, so the body paint is
    ///            found (grown from the door panels) and re-coloured into the
    ///            skins a 1999 street has.
    ///   LAMPS    found on the texture and seated on the skin by the game's
    ///            own CarLampFinder, so CarLights puts its 1999 halogen and
    ///            incandescent lamps where the model has them.
    ///   FAR      body and wheels in one mesh, one draw call (TrafficModels.Dress).
    ///
    ///   -executeMethod PSXRacing.EditorTools.TrafficUaaBaker.Bake   (PSX_PC_CONTENT=1;
    ///    PSX_UAA_PREVIEW=1 also renders every vehicle and skin to Screenshots/uaa_kit)
    /// </summary>
    public static class TrafficUaaBaker
    {
        const string Src = "Assets/StoreAssets/UAAVehicles";
        const string Gen = Src + "/Gen";
        const string KitPath = "Assets/PSXRacing/Resources/" + TrafficKit.ResourcePath + ".asset";
        const string LogFile = "PSXRacing_uaa_bake.txt";
        const string ShotDir = "Screenshots/uaa_kit";

        static bool PCContent => Environment.GetEnvironmentVariable("PSX_PC_CONTENT") == "1";

        class Spec
        {
            public string key, prefab;
            public int kind;
            public float scale = 1f;
            /// <summary>Dark texels higher than this (m above the road) are
            /// windows; anything dark below it is matte.</summary>
            public float glassFrom = 0.86f;
            /// <summary>The door band the paint's own colour is read from (m).</summary>
            public float bandLo = 0.45f, bandHi = 0.80f;
            /// <summary>The colours it is repainted in, after its own.</summary>
            public string[] tints;
            public Spec(string key, string prefab, int kind, float scale, float glassFrom, float bandLo, float bandHi, params string[] tints)
            { this.key = key; this.prefab = prefab; this.kind = kind; this.scale = scale; this.glassFrom = glassFrom; this.bandLo = bandLo; this.bandHi = bandHi; this.tints = tints; }
        }

        const int C = TrafficModels.Car, V = TrafficModels.Van;

        /// <summary>Seventeen of the pack's eighteen vehicles. Left out:
        /// "Taxi" of the first set (the same 1998 cab as "Traffic B - Taxi",
        /// which has twice the detail and rides the shared atlas) and the 22
        /// numbered "Traffic B" prefabs (hub-cap variants of five bodies).</summary>
        static readonly Spec[] Specs =
        {
            new Spec("uaa_sedan_96",    "Prefabs/Car1",                    C, 0.96f, 0.84f, 0.45f, 0.78f, "green", "burgundy", "navy"),
            new Spec("uaa_sedan_box",   "Prefabs/Car2",                    C, 0.98f, 0.90f, 0.45f, 0.80f, "lightblue", "burgundy", "grey"),
            new Spec("uaa_wagon_wood",  "Prefabs/Wagon",                   C, 1.00f, 0.90f, 0.45f, 0.80f),
            new Spec("uaa_suv",         "Prefabs/Suv",                     C, 0.96f, 1.02f, 0.55f, 0.95f, "red", "navy", "green"),
            new Spec("uaa_van_cargo",   "Prefabs/Van",                     V, 1.00f, 1.22f, 0.60f, 1.10f, "silver", "navy"),
            new Spec("uaa_taxi_box",    "Prefabs/Taxi B",                  TrafficModels.Taxi,   0.97f, 0.90f, 0f, 0f),
            new Spec("uaa_police",      "Prefabs/Police Car",              TrafficModels.Police, 0.97f, 1.02f, 0f, 0f),
            new Spec("uaa_bus",         "Prefabs/City Bus",                TrafficModels.Bus,    1.00f, 1.20f, 0f, 0f),
            new Spec("uaa_rig",         "Prefabs/Big Rig",                 TrafficModels.Rig,    1.00f, 99f,   0f, 0f),
            new Spec("uaa_sedan_lux",   "PrefabsB/Traffic B - BigSedan",   C, 0.97f, 0.86f, 0.45f, 0.78f, "burgundy", "navy", "green", "silver"),
            new Spec("uaa_sedan_mid",   "PrefabsB/Traffic B - MidSedan",   C, 0.99f, 0.86f, 0.45f, 0.78f, "navy", "red", "grey", "green"),
            new Spec("uaa_sedan_old",   "PrefabsB/Traffic B - OldSedan",   C, 0.97f, 0.88f, 0.45f, 0.80f, "lightblue", "burgundy", "beige", "grey"),
            new Spec("uaa_wagon_box",   "PrefabsB/Traffic B - OldWagon",   C, 0.97f, 0.88f, 0.45f, 0.80f, "beige", "green", "burgundy", "lightblue"),
            new Spec("uaa_sedan_small", "PrefabsB/Traffic B - Small_Car",  C, 0.95f, 0.82f, 0.42f, 0.74f, "red", "teal", "grey", "navy"),
            new Spec("uaa_taxi",        "PrefabsB/Traffic B - Taxi",       TrafficModels.Taxi,   0.99f, 0.86f, 0f, 0f),
            new Spec("uaa_van_pass",    "PrefabsB/Traffic B - Van",        V, 1.00f, 1.22f, 0.60f, 1.10f, "white", "navy", "burgundy", "teal"),
            new Spec("uaa_truck_box",   "PrefabsB/Traffic B - Truck",      TrafficModels.Truck,  1.00f, 1.50f, 0f, 0f),
        };

        /// <summary>The paint of a 1999 car park (sRGB): no pearl whites, no
        /// matte greys, a lot of dark green, burgundy and teal.</summary>
        static readonly Dictionary<string, Color32> Palette = new Dictionary<string, Color32>
        {
            { "white", new Color32(236, 236, 232, 255) }, { "silver", new Color32(172, 176, 180, 255) },
            { "grey", new Color32(112, 116, 120, 255) }, { "beige", new Color32(190, 172, 140, 255) },
            { "red", new Color32(150, 28, 30, 255) }, { "burgundy", new Color32(92, 24, 36, 255) },
            { "navy", new Color32(30, 42, 84, 255) }, { "lightblue", new Color32(124, 152, 188, 255) },
            { "green", new Color32(34, 72, 52, 255) }, { "teal", new Color32(24, 98, 100, 255) },
        };

        /// <summary>
        /// PAINTED OUT (rects in the source texture's pixels, origin top
        /// left, inclusive; l / t = filled from the column left of / the row
        /// above the rect, v = blended top to bottom, #rrggbb = flat). The one
        /// readable line of real-world text in the pack ("EMERGENCY DIAL 911
        /// FIRE POLICE MEDICAL" on the police car's boot) and the shapes that
        /// read as a badge or as print even though nothing in them is legible.
        /// </summary>
        static readonly (string tex, int x0, int y0, int x1, int y1, string how)[] Patches =
        {
            ("carpolice", 189, 20, 234, 41, "l"),          // the boot lid's text
            ("carpolice", 407, 67, 443, 105, "v"),         // the hub cap's oval
            ("cartaxib", 407, 67, 443, 105, "v"),          // the same hub cap
            ("carbus", 299, 111, 384, 149, "t"),           // the rear panel's plaques
            ("carbus", 329, 150, 384, 168, "t"),
            ("trafficcars", 999, 671, 1023, 696, "l"),     // a dealer plate on the white saloon's nose
            ("trafficcars", 1017, 494, 1023, 508, "l"),    // an oval in the luxury saloon's grille
            ("trafficcars", 160, 98, 205, 117, "#c8c8c4"), // an orange plate on the small saloon's tail
        };

        static readonly List<string> log = new List<string>();
        static bool done;

        // ------------------------------------------------------------------
        //  Entry points
        // ------------------------------------------------------------------
        /// <summary>Batch entry: bake now, exit 0 when the kit is written.</summary>
        public static void Bake()
        {
            int code = 0;
            try
            {
                var kit = Ensure(true);
                if (kit == null) { Debug.LogError("[TrafficUaa] no kit written (PSX_PC_CONTENT=1? store files in " + Src + "?)"); code = 1; }
                else if (Environment.GetEnvironmentVariable("PSX_UAA_PREVIEW") == "1") Preview();
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The kit for this pass: baked when this is a PC content
        /// pass with the store files in the project, removed otherwise.</summary>
        public static TrafficKit Ensure(bool force = false)
        {
            bool have = AssetDatabase.LoadAssetAtPath<GameObject>(Src + "/CarPack.FBX") != null &&
                        AssetDatabase.LoadAssetAtPath<GameObject>(Src + "/TrafficCars.FBX") != null;
            if (!PCContent || !have)
            {
                if (AssetDatabase.LoadAssetAtPath<TrafficKit>(KitPath) != null)
                {
                    AssetDatabase.DeleteAsset(KitPath);
                    Debug.Log("[TrafficUaa] kit removed (not a PC content pass, or no store files): the traffic keeps the models it had");
                    TrafficModels.Reset();
                }
                return null;
            }
            if (done && !force) return AssetDatabase.LoadAssetAtPath<TrafficKit>(KitPath);
            log.Clear();
            foreach (var d in new[] { Gen, Gen + "/tex", Gen + "/mat", Gen + "/mesh", Gen + "/def" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            var shader = Shader.Find("PSX/CarPaint");
            if (shader == null) throw new Exception("PSX/CarPaint not found");

            // every vehicle's geometry first, then the textures they share
            var built = new List<Built>();
            foreach (var s in Specs)
            {
                try { var b = BuildGeometry(s); if (b != null) built.Add(b); }
                catch (Exception e) { log.Add($"{s.key}: FAILED {e.Message}"); Debug.LogException(e); }
            }
            var byTex = new Dictionary<string, List<Built>>();
            foreach (var b in built)
            {
                if (!byTex.TryGetValue(b.texPath, out var l)) byTex[b.texPath] = l = new List<Built>();
                l.Add(b);
            }
            foreach (var kv in byTex) BuildSkins(kv.Key, kv.Value, shader);

            var entries = new List<TrafficKit.Entry>();
            foreach (var b in built)
            {
                if (b.def.skinMaterials == null || b.def.skinMaterials.Length == 0) { log.Add($"{b.spec.key}: no skins, left out"); continue; }
                CarLampFinder.Apply(b.def, log);
                string path = Gen + "/def/" + b.spec.key + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(b.def.gameObject, path);
                UnityEngine.Object.DestroyImmediate(b.def.gameObject);
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                b.entry.def = saved != null ? saved.GetComponent<CarModelDef>() : null;
                if (b.entry.def != null) entries.Add(b.entry);
            }

            var kit = AssetDatabase.LoadAssetAtPath<TrafficKit>(KitPath);
            bool fresh = kit == null;
            if (fresh) kit = ScriptableObject.CreateInstance<TrafficKit>();
            kit.entries = entries.ToArray();
            kit.builtFrom = "UAA - City Props - Vehicles 2.0, baked " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (fresh) AssetDatabase.CreateAsset(kit, KitPath);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            log.Add($"KIT {entries.Count} vehicles -> {KitPath}");
            File.WriteAllLines(LogFile, log);
            Debug.Log("[TrafficUaa] kit: " + entries.Count + " vehicles");
            done = true;
            TrafficModels.Reset();
            return kit;
        }

        // ------------------------------------------------------------------
        //  Geometry
        // ------------------------------------------------------------------
        class Built
        {
            public Spec spec;
            public CarModelDef def;
            public TrafficKit.Entry entry;
            public string texPath;
            /// <summary>The body in the car frame (for the texture maps).</summary>
            public Vector3[] v; public Vector2[] uv; public int[] tris;
        }

        struct Part { public Mesh mesh; public Matrix4x4 mx; public Bounds b; }

        static Built BuildGeometry(Spec s)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Src + "/" + s.prefab + ".prefab");
            if (pf == null) { log.Add($"{s.key}: prefab missing ({s.prefab})"); return null; }
            var go = UnityEngine.Object.Instantiate(pf);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            try
            {
                var bodies = new List<Part>();
                var wheels = new List<Part>();
                string texPath = null;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(false))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var p = new Part { mesh = mf.sharedMesh, mx = r.transform.localToWorldMatrix, b = r.bounds };
                    bool isWheel = p.b.size.y < 1.2f && p.b.size.z < 1.2f && p.b.size.x < 0.7f;
                    if (isWheel) wheels.Add(p);
                    else
                    {
                        bodies.Add(p);
                        var mat = r.sharedMaterial;
                        if (texPath == null && mat != null && mat.mainTexture != null) texPath = AssetDatabase.GetAssetPath(mat.mainTexture);
                    }
                }
                if (bodies.Count == 0 || wheels.Count < 4 || texPath == null) { log.Add($"{s.key}: {bodies.Count} bodies, {wheels.Count} wheels, texture {texPath}: left out"); return null; }

                // the axles: the right-hand wheels, nose first
                var right = wheels.FindAll(w => w.b.center.x > 0f);
                right.Sort((a, b) => b.b.center.z.CompareTo(a.b.center.z));
                if (right.Count < 2) { log.Add($"{s.key}: {right.Count} right wheels: left out"); return null; }
                Part wf = right[0], wr = right[1];
                float k = s.scale;
                float rF = Mathf.Max(wf.b.size.y, wf.b.size.z) * 0.5f * k, rR = Mathf.Max(wr.b.size.y, wr.b.size.z) * 0.5f * k;
                // the car frame: the middle of the first two axles, the tyres' contact at y = 0
                var shift = new Vector3(0f, rF - wf.b.center.y * k, -(wf.b.center.z + wr.b.center.z) * 0.5f * k);

                var bv = new List<Vector3>(); var bn = new List<Vector3>(); var bu = new List<Vector2>(); var bt = new List<int>();
                foreach (var p in bodies) Append(p.mesh, p.mx, k, shift, bv, bn, bu, bt);
                var body = MakeMesh(s.key + "_body", bv, bn, bu, bt);

                Mesh WheelMesh(Part w, string name)
                {
                    var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();
                    Append(w.mesh, w.mx, k, -w.b.center * k, v, n, u, t);
                    return MakeMesh(name, v, n, u, t);
                }
                var wheel = WheelMesh(wf, s.key + "_wheel");
                bool rearOwn = wr.mesh != wf.mesh || Mathf.Abs(wr.b.size.x - wf.b.size.x) > 0.03f;
                var rearWheel = rearOwn ? WheelMesh(wr, s.key + "_wheel_rear") : null;
                float xF = wf.b.center.x * k, xR = wr.b.center.x * k;
                float zF = wf.b.center.z * k + shift.z, zR = wr.b.center.z * k + shift.z;

                var extra = new List<Vector3>();
                for (int i = 2; i < right.Count; i++)
                {
                    var c = right[i].b.center * k + shift;
                    float r = Mathf.Max(right[i].b.size.y, right[i].b.size.z) * 0.5f * k;
                    extra.Add(new Vector3(c.x, r, c.z));
                    extra.Add(new Vector3(-c.x, r, c.z));
                }

                // the far level: the body, and every wheel where it stands
                var fv = new List<Vector3>(bv); var fn = new List<Vector3>(bn); var fu = new List<Vector2>(bu); var ft = new List<int>(bt);
                void Stand(Mesh m, Vector3 hub, bool left)
                {
                    var q = left ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                    Append(m, Matrix4x4.TRS(hub, q, Vector3.one), 1f, Vector3.zero, fv, fn, fu, ft);
                }
                Stand(wheel, new Vector3(xF, rF, zF), false); Stand(wheel, new Vector3(-xF, rF, zF), true);
                Stand(rearWheel ?? wheel, new Vector3(xR, rR, zR), false); Stand(rearWheel ?? wheel, new Vector3(-xR, rR, zR), true);
                foreach (var e in extra) Stand(rearWheel ?? wheel, e, e.x < 0f);
                var far = MakeMesh(s.key + "_far", fv, fn, fu, ft);

                body = SaveAsset(body, Gen + "/mesh/" + s.key + "_body.asset");
                wheel = SaveAsset(wheel, Gen + "/mesh/" + s.key + "_wheel.asset");
                if (rearWheel != null) rearWheel = SaveAsset(rearWheel, Gen + "/mesh/" + s.key + "_wheel_rear.asset");
                far = SaveAsset(far, Gen + "/mesh/" + s.key + "_far.asset");

                var bb = body.bounds;
                var defGO = new GameObject(s.key);
                var d = defGO.AddComponent<CarModelDef>();
                d.key = s.key;
                d.displayName = s.key;
                d.bodyMesh = body;
                d.wheelMesh = wheel;
                d.bodyYaw = 0f; d.bodyYOffset = 0f; d.bodyZOffset = 0f;
                d.wheelbase = zF - zR;
                d.trackWidth = 2f * xF;
                d.wheelRadius = rF;
                d.wheelMeshScale = 1f;
                d.trackRear = 2f * xR;
                d.tyreRadiusRear = rR;
                d.tyreWidthRear = wr.b.size.x * k;
                d.lowerBodyWidth = CarModelLibrary.MeasureLowerWidth(body, 0f, true);
                // THE BOX: the body's own, mirrors left out of the width
                float boxW = Mathf.Min(bb.size.x, d.lowerBodyWidth) * 0.98f;
                d.colliderCenter = new Vector3(0f, (bb.max.y + bb.min.y) * 0.5f, bb.center.z);
                d.colliderSize = new Vector3(boxW, (bb.max.y - bb.min.y) * 0.97f, bb.size.z * 0.985f);
                d.blobSize = new Vector2(d.lowerBodyWidth * 1.22f, bb.size.z * 1.07f);
                d.noseZ = bb.max.z;
                d.roofY = bb.max.y;
                d.cowlZ = bb.center.z + bb.size.z * 0.18f;
                d.cowlY = bb.max.y * 0.72f;

                int wheelTris = Tris(wheel) * 2 + Tris(rearWheel ?? wheel) * (2 + extra.Count);
                var entry = new TrafficKit.Entry
                {
                    key = s.key, kind = s.kind, farMesh = far, rearWheelMesh = rearWheel,
                    rearHalfTrack = Mathf.Abs(xR - xF) > 0.02f ? xR : 0f,
                    extraWheels = extra.ToArray(),
                    length = bb.size.z, width = d.lowerBodyWidth, height = bb.max.y,
                    bodyTris = Tris(body), wheelTris = wheelTris,
                };
                log.Add($"{s.key,-16} {TrafficModels.KindNames[s.kind],-6} L={bb.size.z:0.00} W={d.lowerBodyWidth:0.00} (mirrors {bb.size.x:0.00}) H={bb.max.y:0.00} sill={bb.min.y:0.00} " +
                        $"wb={d.wheelbase:0.00} track={d.trackWidth:0.00}/{d.trackRear:0.00} tyre r={rF:0.000}/{rR:0.000} axles={right.Count} " +
                        $"box={d.colliderSize.x:0.00}x{d.colliderSize.y:0.00}x{d.colliderSize.z:0.00} tris body={entry.bodyTris} wheels={wheelTris} far={Tris(far)} scale={k:0.00}");
                return new Built { spec = s, def = d, entry = entry, texPath = texPath, v = bv.ToArray(), uv = bu.ToArray(), tris = bt.ToArray() };
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static int Tris(Mesh m)
        {
            if (m == null) return 0;
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i);
            return (int)(n / 3);
        }

        /// <summary>A mesh's triangles through a matrix, scaled and shifted;
        /// a mirrored instance (the pack mirrors its left wheels) is wound
        /// back the right way.</summary>
        static void Append(Mesh m, Matrix4x4 mx, float scale, Vector3 shift, List<Vector3> v, List<Vector3> n, List<Vector2> u, List<int> t)
        {
            var sv = m.vertices; var sn = m.normals; var su = m.uv;
            int at = v.Count;
            bool flip = mx.determinant < 0f;
            for (int i = 0; i < sv.Length; i++)
            {
                v.Add(mx.MultiplyPoint3x4(sv[i]) * scale + shift);
                n.Add(sn != null && sn.Length == sv.Length ? mx.MultiplyVector(sn[i]).normalized : Vector3.up);
                u.Add(su != null && su.Length == sv.Length ? su[i] : Vector2.zero);
            }
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                var st = m.GetTriangles(sub);
                for (int i = 0; i + 2 < st.Length; i += 3)
                {
                    t.Add(at + st[i]);
                    t.Add(at + st[flip ? i + 2 : i + 1]);
                    t.Add(at + st[flip ? i + 1 : i + 2]);
                }
            }
        }

        static Mesh MakeMesh(string name, List<Vector3> v, List<Vector3> n, List<Vector2> u, List<int> t)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, u); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Written over the asset that is there (its GUID kept: a
        /// scene's parked car still points at its mesh after a re-bake).</summary>
        static T SaveAsset<T>(T obj, string path) where T : UnityEngine.Object
        {
            var was = AssetDatabase.LoadAssetAtPath<T>(path);
            if (was == null) { AssetDatabase.CreateAsset(obj, path); return obj; }
            string name = obj.name;
            EditorUtility.CopySerialized(obj, was);
            was.name = name;
            EditorUtility.SetDirty(was);
            UnityEngine.Object.DestroyImmediate(obj);
            return was;
        }

        // ------------------------------------------------------------------
        //  Textures and skins
        // ------------------------------------------------------------------
        class TexMap { public int w, h; public float[] hgt; public byte[] face; }

        static void BuildSkins(string texPath, List<Built> cars, Shader shader)
        {
            string texName = Path.GetFileNameWithoutExtension(texPath);
            var imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (imp == null) { log.Add($"texture {texPath}: no importer"); return; }
            if (!imp.isReadable || imp.textureCompression != TextureImporterCompression.Uncompressed || imp.mipmapEnabled || imp.npotScale != TextureImporterNPOTScale.None)
            {
                imp.isReadable = true; imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false; imp.npotScale = TextureImporterNPOTScale.None;
                imp.SaveAndReimport();
            }
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            int w = src.width, h = src.height;
            var px = src.GetPixels32();

            // painted out
            int patched = 0;
            foreach (var p in Patches)
            {
                if (!string.Equals(p.tex, texName, StringComparison.OrdinalIgnoreCase)) continue;
                Patch(px, w, h, p.x0, p.y0, p.x1, p.y1, p.how);
                patched++;
            }

            // glass and matte, per vehicle on the sheet
            var clean = (Color32[])px.Clone();
            for (int i = 0; i < clean.Length; i++) clean[i].a = 128;
            var masks = new List<(Built car, bool[] paint, Color32 own)>();
            int skins = 1;
            foreach (var car in cars)
            {
                var map = new TexMap { w = w, h = h, hgt = new float[w * h], face = new byte[w * h] };
                Raster(map, car.v, car.uv, car.tris);
                int glass = 0;
                for (int i = 0; i < clean.Length; i++)
                {
                    if (map.face[i] == 0 || map.hgt[i] < car.spec.glassFrom) continue;
                    var c = clean[i];
                    float lum = Lum(c);
                    int hi = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), lo = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    float chroma = (hi - lo) / Mathf.Max(hi, 20f);
                    if (lum >= 0.45f || chroma >= 0.5f) continue;
                    // a window: down to the darkness the car paint shader reads as glass
                    float kk = Mathf.Min(1f, 0.07f / Mathf.Max(lum, 0.02f));
                    clean[i] = new Color32((byte)(c.r * kk), (byte)(c.g * kk), (byte)(c.b * kk), 255);
                    glass++;
                }
                bool[] paint = null; Color32 own = new Color32(200, 200, 200, 255);
                if (car.spec.tints != null && car.spec.tints.Length > 0)
                {
                    paint = PaintMask(px, map, car.spec, out own, out int seeds, out int grown);
                    log.Add($"{car.spec.key,-16} paint ({own.r},{own.g},{own.b}) seeds={seeds} mask={grown} px ({100f * grown / Mathf.Max(1, Covered(map)):0}% of its texels) glass={glass} px");
                    if (grown < 400) paint = null;
                    else skins = Mathf.Max(skins, 1 + car.spec.tints.Length);
                }
                else log.Add($"{car.spec.key,-16} own colour only, glass={glass} px");
                masks.Add((car, paint, own));
            }

            // the skins: the pack's own colours, then each repaint
            var mats = new Material[skins];
            var names = new string[skins];
            for (int k = 0; k < skins; k++)
            {
                var outPx = (Color32[])clean.Clone();
                if (k > 0)
                    foreach (var (car, paint, own) in masks)
                    {
                        if (paint == null) continue;
                        var target = Palette[car.spec.tints[(k - 1) % car.spec.tints.Length]];
                        Repaint(outPx, paint, own, target);
                    }
                names[k] = k == 0 ? "own" : "paint" + k;
                string file = Gen + "/tex/uaa_" + texName.ToLowerInvariant() + "_" + k + ".png";
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.SetPixels32(outPx); t.Apply();
                File.WriteAllBytes(file, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
                var ti = AssetImporter.GetAtPath(file) as TextureImporter;
                if (ti != null)
                {
                    ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
                    ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4;
                    ti.wrapMode = TextureWrapMode.Repeat;
                    // one car on a sheet: 512 is a texel a centimetre; the shared atlas keeps its 1024
                    ti.maxTextureSize = cars.Count > 1 ? 1024 : 512;
                    ti.textureCompression = TextureImporterCompression.Compressed;
                    ti.SaveAndReimport();
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(file);
                var mat = new Material(shader) { name = "uaa_" + texName.ToLowerInvariant() + "_" + k };
                mat.mainTexture = tex;
                mat.color = Color.white;
                mat.SetFloat("_Cutoff", 0f);
                mat.SetFloat("_Affine", 0f);
                mat.DisableKeyword("PSX_CAR_HD");
                mats[k] = SaveAsset(mat, Gen + "/mat/" + mat.name + ".mat");
            }
            foreach (var (car, paint, own) in masks)
            {
                // a car with no repaint on a sheet that has some still wears every sheet (it looks the same on all)
                int n = paint != null ? 1 + car.spec.tints.Length : (cars.Count > 1 ? skins : 1);
                car.def.skinMaterials = new Material[n];
                car.def.skinNames = new string[n];
                car.def.skinColors = new Color[n];
                for (int k = 0; k < n; k++)
                {
                    car.def.skinMaterials[k] = mats[k];
                    car.def.skinNames[k] = k == 0 || paint == null ? "own" : car.spec.tints[k - 1];
                    car.def.skinColors[k] = k == 0 || paint == null ? (Color)own : (Color)Palette[car.spec.tints[k - 1]];
                }
            }
            log.Add($"sheet {texName}: {w}x{h}, {cars.Count} vehicle(s), {patched} patch(es), {skins} skin(s)");
        }

        static int Covered(TexMap m) { int n = 0; foreach (var f in m.face) if (f != 0) n++; return n; }

        static float Lum(Color32 c) => (c.r * 0.30f + c.g * 0.59f + c.b * 0.11f) / 255f;

        static void Patch(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, string how)
        {
            // rows are top-down in the patch list, bottom-up in the pixels
            Color32 At(int x, int y) => px[(h - 1 - Mathf.Clamp(y, 0, h - 1)) * w + Mathf.Clamp(x, 0, w - 1)];
            Color32 flat = default;
            if (how.StartsWith("#")) { ColorUtility.TryParseHtmlString(how, out var fc); flat = fc; }
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Color32 c;
                    if (how.StartsWith("#")) c = flat;
                    else if (how == "l") c = At(x0 - 1, y);
                    else if (how == "t") c = At(x, y0 - 1);
                    else
                    {
                        Color32 a = At(x, y0 - 1), b = At(x, y1 + 1);
                        float t = (y - y0 + 1f) / (y1 - y0 + 2f);
                        c = Color32.Lerp(a, b, t);
                    }
                    c.a = 255;
                    px[(h - 1 - y) * w + x] = c;
                }
        }

        /// <summary>Each body triangle drawn into the sheet: how high above
        /// the road the texel is, and which way its panel faces (1 a side, 2
        /// the top, 3 an end or the underside).</summary>
        static void Raster(TexMap m, Vector3[] v, Vector2[] uv, int[] tris)
        {
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                Vector3 fn = Vector3.Cross(v[ib] - v[ia], v[ic] - v[ia]).normalized;
                byte face = Mathf.Abs(fn.x) > 0.6f ? (byte)1 : fn.y > 0.6f ? (byte)2 : (byte)3;
                Vector2 off = new Vector2(Mathf.Floor(Mathf.Min(uv[ia].x, Mathf.Min(uv[ib].x, uv[ic].x)) + 1e-4f),
                                          Mathf.Floor(Mathf.Min(uv[ia].y, Mathf.Min(uv[ib].y, uv[ic].y)) + 1e-4f));
                Vector2 A = Vector2.Scale(uv[ia] - off, new Vector2(m.w, m.h)), B = Vector2.Scale(uv[ib] - off, new Vector2(m.w, m.h)),
                        Cc = Vector2.Scale(uv[ic] - off, new Vector2(m.w, m.h));
                float area = (B.x - A.x) * (Cc.y - A.y) - (B.y - A.y) * (Cc.x - A.x);
                if (Mathf.Abs(area) < 1e-4f) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, Cc.x)) - 1f), 0, m.w - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, Cc.x)) + 1f), 0, m.w - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, Cc.y)) - 1f), 0, m.h - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, Cc.y)) + 1f), 0, m.h - 1);
                float inv = 1f / area;
                // a texel's centre up to a pixel outside the triangle still belongs to it (the filter reads it)
                float lenAB = Mathf.Max((B - A).magnitude, 1e-3f), lenBC = Mathf.Max((Cc - B).magnitude, 1e-3f), lenCA = Mathf.Max((A - Cc).magnitude, 1e-3f);
                float tol = Mathf.Abs(inv) * Mathf.Max(lenAB, Mathf.Max(lenBC, lenCA));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float wA = ((B.x - px) * (Cc.y - py) - (B.y - py) * (Cc.x - px)) * inv;
                        float wB = ((Cc.x - px) * (A.y - py) - (Cc.y - py) * (A.x - px)) * inv;
                        float wC = 1f - wA - wB;
                        if (wA < -tol || wB < -tol || wC < -tol) continue;
                        float hy = wA * v[ia].y + wB * v[ib].y + wC * v[ic].y;
                        int i = y * m.w + x;
                        if (m.face[i] == 0) { m.face[i] = face; m.hgt[i] = hy; }
                        else { if (face < m.face[i]) m.face[i] = face; if (hy > m.hgt[i]) m.hgt[i] = hy; }
                    }
            }
        }

        /// <summary>
        /// THE BODY PAINT: its colour read off the doors (the median of the
        /// side panels' texels in the door band), then grown from there over
        /// the side and top panels while the texture stays smooth and the
        /// same hue - so it stops at a window frame, a lamp, a handle, a
        /// chrome strip or a wood panel. The ends of the car (grille, lamps,
        /// bumpers) are never repainted.
        /// </summary>
        static bool[] PaintMask(Color32[] px, TexMap m, Spec s, out Color32 own, out int seeds, out int grown)
        {
            int w = m.w, h = m.h;
            var rs = new List<byte>(); var gs = new List<byte>(); var bs = new List<byte>();
            for (int i = 0; i < px.Length; i++)
            {
                if (m.face[i] != 1 || m.hgt[i] < s.bandLo || m.hgt[i] > s.bandHi) continue;
                if (Lum(px[i]) < 0.3f) continue;
                rs.Add(px[i].r); gs.Add(px[i].g); bs.Add(px[i].b);
            }
            own = new Color32(200, 200, 200, 255); seeds = 0; grown = 0;
            if (rs.Count < 200) return null;
            rs.Sort(); gs.Sort(); bs.Sort();
            own = new Color32(rs[rs.Count / 2], gs[gs.Count / 2], bs[bs.Count / 2], 255);
            float ownLum = Lum(own);
            float ownMax = Mathf.Max(own.r, Mathf.Max(own.g, own.b));
            Vector3 ownN = new Vector3(own.r, own.g, own.b) / Mathf.Max(ownMax, 1f);

            bool SameHue(Color32 c, float tol)
            {
                float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (mx < 1f) return false;
                return Mathf.Abs(c.r / mx - ownN.x) <= tol && Mathf.Abs(c.g / mx - ownN.y) <= tol && Mathf.Abs(c.b / mx - ownN.z) <= tol;
            }

            var mask = new bool[px.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (m.face[i] == 1)
                {
                    // the doors: close to the paint's own colour
                    if (m.hgt[i] < s.bandLo || m.hgt[i] > s.bandHi) continue;
                    if (Mathf.Abs(c.r - own.r) > 26 || Mathf.Abs(c.g - own.g) > 26 || Mathf.Abs(c.b - own.b) > 26) continue;
                }
                else if (m.face[i] == 2)
                {
                    // bonnet, roof, boot lid: their own island on most sheets, lit
                    // differently from the doors - the same hue, about as bright
                    float l = Lum(c);
                    if (l < ownLum * 0.55f || l > ownLum * 1.6f || !SameHue(c, 0.08f)) continue;
                }
                else continue;
                mask[i] = true; queue.Enqueue(i); seeds++;
            }
            grown = seeds;
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                var c = px[i];
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (mask[j] || m.face[j] == 0 || m.face[j] == 3) continue;
                    var n = px[j];
                    if (Mathf.Abs(n.r - c.r) > 9 || Mathf.Abs(n.g - c.g) > 9 || Mathf.Abs(n.b - c.b) > 9) continue;
                    if (Lum(n) < ownLum * 0.38f || !SameHue(n, 0.10f)) continue;
                    mask[j] = true; queue.Enqueue(j); grown++;
                }
            }
            return mask;
        }

        /// <summary>The paint re-coloured: the new colour times the shading
        /// the photo had there, its highlights kept toward white.</summary>
        static void Repaint(Color32[] px, bool[] paint, Color32 own, Color32 target)
        {
            float ownLum = Mathf.Max(Lum(own), 0.05f);
            for (int i = 0; i < px.Length; i++)
            {
                if (!paint[i]) continue;
                float sh = Lum(px[i]) / ownLum;
                float lit = Mathf.Min(sh, 1.15f);
                float r = target.r * lit, g = target.g * lit, b = target.b * lit;
                if (sh > 1.15f)
                {
                    float t = Mathf.Min(1f, (sh - 1.15f) * 2.5f);
                    r += (255f - r) * t; g += (255f - g) * t; b += (255f - b) * t;
                }
                px[i] = new Color32((byte)Mathf.Clamp(r, 0f, 255f), (byte)Mathf.Clamp(g, 0f, 255f), (byte)Mathf.Clamp(b, 0f, 255f), px[i].a);
            }
        }

        // ------------------------------------------------------------------
        //  Pictures of the kit, as the game assembles it
        // ------------------------------------------------------------------
        /// <summary>Every vehicle through CarShell and TrafficModels.Dress,
        /// under the car shots' sun: nose and tail three-quarters with the
        /// measured lamps marked, and every skin.</summary>
        public static void Preview()
        {
            Directory.CreateDirectory(ShotDir);
            foreach (var f in Directory.GetFiles(ShotDir, "*.png")) File.Delete(f);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.25f; sun.color = new Color(1f, 0.96f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(46f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.56f);
            RenderSettings.fog = false;
            var globals = sun.gameObject.AddComponent<PSXGlobals>();
            globals.sun = sun; globals.ambient = new Color(0.55f, 0.55f, 0.60f); globals.fogNear = 400f; globals.fogFar = 900f;
            globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            var cam = new GameObject("KitCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.30f, 0.33f, 0.38f);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 400f;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var gm = new Material(unlit); gm.SetColor("_BaseColor", new Color(0.22f, 0.23f, 0.25f));
            ground.GetComponent<Renderer>().sharedMaterial = gm;
            Material Dot(Color c) { var mm = new Material(unlit); mm.SetColor("_BaseColor", c); return mm; }
            var headDot = Dot(new Color(1f, 1f, 0.2f)); var tailDot = Dot(new Color(1f, 0.1f, 0.6f));

            TrafficModels.Reset();
            var kit = AssetDatabase.LoadAssetAtPath<TrafficKit>(KitPath);
            if (kit == null) return;
            foreach (var e in kit.entries)
            {
                var def = e.def;
                float len = Mathf.Max(e.length, 4f);
                for (int skin = 0; skin < Mathf.Max(1, def.SkinCount); skin++)
                {
                    var root = new GameObject(e.key);
                    var shell = OnFoot.CarShell.Spawn(root.transform, def, skin, out _, solid: false);
                    TrafficModels.Dress(shell, def);
                    var lod = shell.GetComponent<LODGroup>();
                    if (lod != null) lod.ForceLOD(0);
                    // CarShell centres the shell on the collider: the body's car frame is at -colliderCenter.z
                    float cz = -def.colliderCenter.z;
                    Vector3 look = new Vector3(0f, e.height * 0.45f, 0f);
                    float dist = len * 2.3f + 3f;
                    if (skin == 0)
                    {
                        Shot(cam, e.key + "_a", look + new Vector3(0.7f, 0.36f, 1f).normalized * dist, look);
                        Shot(cam, e.key + "_b", look + new Vector3(-0.7f, 0.36f, -1f).normalized * dist, look);
                        Shot(cam, e.key + "_side", look + new Vector3(1f, 0.12f, 0f) * dist, look);
                        // the lamps, marked
                        var marks = new List<GameObject>();
                        void Mark(Vector3 p, Material mm)
                        {
                            foreach (float sx in new[] { 1f, -1f })
                            {
                                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                                s.transform.position = new Vector3(p.x * sx, p.y, p.z + cz);
                                s.transform.localScale = Vector3.one * 0.12f;
                                s.GetComponent<Renderer>().sharedMaterial = mm;
                                marks.Add(s);
                            }
                        }
                        Mark(def.headLamp, headDot); Mark(def.tailLamp, tailDot);
                        Shot(cam, e.key + "_lampf", look + new Vector3(0.45f, 0.2f, 1f).normalized * dist, look);
                        Shot(cam, e.key + "_lampr", look + new Vector3(-0.45f, 0.2f, -1f).normalized * dist, look);
                        foreach (var s in marks) UnityEngine.Object.DestroyImmediate(s);
                        if (lod != null)
                        {
                            lod.ForceLOD(1);
                            Shot(cam, e.key + "_far", look + new Vector3(0.7f, 0.36f, 1f).normalized * dist, look);
                            lod.ForceLOD(0);
                        }
                    }
                    else Shot(cam, e.key + "_skin" + skin, look + new Vector3(0.7f, 0.36f, 1f).normalized * dist, look);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            Debug.Log("[TrafficUaa] preview -> " + ShotDir);
        }

        static void Shot(Camera cam, string name, Vector3 pos, Vector3 lookAt)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
            var rt = new RenderTexture(480, 300, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
