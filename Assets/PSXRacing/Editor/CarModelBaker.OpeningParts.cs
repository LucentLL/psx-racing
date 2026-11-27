using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// SECTION 5A OPENING PARTS (carparts stage 2), beside BakeCovers: a shell
    /// whose export carries <c>key_parts.json</c> (export_models.mjs `opening`)
    /// gets its open body (<c>key_open.obj</c>), one mesh per outer panel
    /// (<c>key_part_Panel.obj</c>, and a lid's own clear cover), the covers on
    /// Body alone (<c>key_covers_open.obj</c>), the hinges, the Engine_Mount and
    /// the Kit_ empties on its def. A shell without the file bakes exactly as
    /// before (every field left empty).
    ///
    /// THE FRAME. The baker keeps the OBJ frame as Unity imports it (bodyYaw
    /// is applied at runtime on the root): Unity's OBJ importer negates x. So a
    /// point (x, y, z) of the JSON is (-x, y, z) here, and a hinge axis a, a
    /// right-hand rotation in the source, is (a.x, -a.y, -a.z) here (mirroring
    /// a rotation reverses its sense: about -M a, the same angle). Checked per
    /// part against its imported bounds; a part that does not land fails the
    /// bake rather than open the wrong way.
    /// </summary>
    public static partial class CarModelBaker
    {
        [Serializable] class PartsJson
        {
            public string key;
            public float scale;
            public int doors, trisClosed, trisOpenOnly;
            public PanelJ[] panels;
            public bool hasMount;
            public MountJ mount;
            public KitJ[] kits;
            public ObjJ[] objects;
        }
        [Serializable] class PanelJ
        {
            public string name, file, coverFile;
            public float[] origin, axis, boundsMin, boundsMax;
            public float openDeg;
        }
        [Serializable] class MountJ
        {
            public float[] origin, x, y, z, clearBox, downpipe;
            public string layout, downpipeSide;
        }
        [Serializable] class KitJ { public string name; public float[] origin; public float yawDeg; }
        [Serializable] class ObjJ { public string name, parent, set, carrier; public int tris; }

        /// <summary>The source frame's point / direction in the imported mesh frame.</summary>
        public static Vector3 ObjPoint(float[] a) => new Vector3(-a[0], a[1], a[2]);
        public static Vector3 ObjAxis(float[] a) => new Vector3(a[0], -a[1], -a[2]);
        static Vector3 ObjDir(float[] a) => new Vector3(-a[0], a[1], a[2]);

        /// <summary>Where the opening-part test shells are baked: NOT Resources,
        /// so nothing of them ships; the tests load them by path.</summary>
        public const string TestDir = "Assets/PSXRacing/Generated/CarPartsTest";

        /// <summary>The synthetic 5A test cars (tools/carmodels/test): baked into
        /// <see cref="TestDir"/>, never into the catalog.</summary>
        public static readonly string[] TestKeys = { "test5a_2door", "test5a_4door" };

        /// <summary>Bake the synthetic test cars (batch).</summary>
        public static void BakeTestCars()
        {
            log.Clear();
            var shader = Shader.Find("PSX/CarPaint");
            if (shader == null) throw new Exception("PSX/CarPaint shader not found");
            Directory.CreateDirectory(TestDir);
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(GenDir);
            foreach (var k in TestKeys)
            {
                if (!File.Exists(ModelDir + "/" + k + "/" + k + ".obj")) { log.Add($"FAIL {k}: not exported (node export_models.mjs {k})"); continue; }
                var model = new CarModelLibrary.Model { key = k, name = "TEST CAR " + k, handOnly = true, widthMm = 1700 };
                try { BakeOne(model, shader, TestDir); }
                catch (Exception e) { log.Add($"FAIL {k}: {e.Message}"); }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>A baked test car by key (null when not baked).</summary>
        public static CarModelDef LoadTestCar(string key)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(TestDir + "/" + key + ".prefab");
            var d = go != null ? go.GetComponent<CarModelDef>() : null;
            if (d != null && d.skinMaterials != null) foreach (var m in d.skinMaterials) PSXTexDecode.Stamp(m);
            return d;
        }

        static void BakeOpeningParts(CarModelDef d, string dir, string key, Mesh bodyMesh)
        {
            d.bodyOpenMesh = null; d.openingParts = null; d.coverOpenMesh = null;
            d.hasEngineMount = false; d.engineMountPos = Vector3.zero; d.engineMountRot = Quaternion.identity;
            d.engineLayout = EngineLayout.Unknown; d.engineClearBox = null; d.engineDownpipe = Vector3.zero;
            d.engineDownpipeSide = 0; d.kitMounts = null;
            string jp = dir + "/" + key + "_parts.json";
            if (!File.Exists(jp)) return;
            // refused this round (carparts decision 13)
            if (d.lampsUpMesh != null || d.lensBodyMesh != null)
            {
                log.Add($"REFUSED {key}: opening parts on a pop-up / lens-variant shell are not baked this round");
                return;
            }
            var j = JsonUtility.FromJson<PartsJson>(File.ReadAllText(jp));
            Mesh Obj(string name)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + name + ".obj");
                return go != null ? go.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            }
            var open = Obj(key + "_open");
            if (open == null) throw new Exception(key + ": " + key + "_open.obj missing beside " + key + "_parts.json");
            var parts = new List<CarModelDef.OpeningPart>();
            float worst = 0f;
            foreach (var p in j.panels)
            {
                var m = Obj(p.file);
                if (m == null) throw new Exception(key + ": " + p.file + ".obj missing");
                // THE FRAME CHECK: the JSON's bounds of this part, mirrored, against the import
                var lo = ObjPoint(p.boundsMin); var hi = ObjPoint(p.boundsMax);
                var want = new Bounds((lo + hi) * 0.5f, new Vector3(Mathf.Abs(hi.x - lo.x), hi.y - lo.y, hi.z - lo.z));
                float off = Mathf.Max((m.bounds.min - want.min).magnitude, (m.bounds.max - want.max).magnitude);
                worst = Mathf.Max(worst, off);
                if (off > 0.001f) throw new Exception($"{key}: {p.name} imported {m.bounds.min}..{m.bounds.max}, the JSON (x negated) says {want.min}..{want.max}: the frame is not the one assumed");
                parts.Add(new CarModelDef.OpeningPart
                {
                    name = p.name, mesh = m, pivot = ObjPoint(p.origin), axis = ObjAxis(p.axis).normalized, openDeg = p.openDeg,
                    coverMesh = string.IsNullOrEmpty(p.coverFile) ? null : Obj(p.coverFile),
                });
            }
            d.bodyOpenMesh = open;
            d.openingParts = parts.ToArray();
            d.coverOpenMesh = Obj(key + "_covers_open");
            if (j.hasMount && j.mount != null && j.mount.origin != null && j.mount.origin.Length == 3)
            {
                d.hasEngineMount = true;
                d.engineMountPos = ObjPoint(j.mount.origin);
                var fy = ObjDir(j.mount.y).normalized; var fz = ObjDir(j.mount.z).normalized;
                d.engineMountRot = Quaternion.LookRotation(fz, fy);
                d.engineLayout = j.mount.layout == "longitudinal" ? EngineLayout.Longitudinal
                               : j.mount.layout == "transverse" ? EngineLayout.Transverse : EngineLayout.Unknown;
                d.engineClearBox = j.mount.clearBox;
                d.engineDownpipe = ObjPoint(j.mount.downpipe);
                // the side, measured in THIS frame: the mirror turns the mount's +X round
                float side = Vector3.Dot(d.engineDownpipe - d.engineMountPos, d.engineMountRot * Vector3.right);
                d.engineDownpipeSide = (sbyte)(side > 0f ? 1 : side < 0f ? -1 : 0);
                log.Add($"{key,-13} Engine_Mount {d.engineLayout}, downpipe {j.mount.downpipeSide} in the source = " +
                        $"{(d.engineDownpipeSide > 0 ? "+X" : d.engineDownpipeSide < 0 ? "-X" : "0")} of the imported mount " +
                        $"(x mirrored: its +X is the source's -X)");
            }
            if (j.kits != null && j.kits.Length > 0)
                d.kitMounts = j.kits.Select(k => new CarModelDef.KitMount
                {
                    name = k.name, pos = ObjPoint(k.origin), rot = Quaternion.Euler(0f, -k.yawDeg, 0f),
                }).ToArray();
            log.Add($"{key,-13} OPENING PARTS: {string.Join(", ", parts.Select(p => $"{p.name} {p.mesh.triangles.Length / 3}t {p.openDeg:0}deg" + (p.coverMesh != null ? " +cover" : "")))}; " +
                    $"open body {open.triangles.Length / 3}t vs closed {bodyMesh.triangles.Length / 3}t; closed set {j.trisClosed}t, open-only {j.trisOpenOnly}t ({j.doors}-door); " +
                    $"kits {(d.kitMounts != null ? string.Join("+", d.kitMounts.Select(k => k.name)) : "none")}; frame check worst {worst * 1000f:0.00} mm");
        }
    }
}
