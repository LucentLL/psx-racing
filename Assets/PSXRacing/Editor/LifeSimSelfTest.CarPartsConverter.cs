using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARPARTS STAGE 2: the converter (export_models.mjs `opening`) and the
    /// baker (CarModelBaker.BakeOpeningParts) on the synthetic 5A TEST CARS
    /// (tools/carmodels/test/make_test_car.py, two-door and four-door): what
    /// was authored lands where it was authored, the closed body equals the
    /// open body plus the parts minus the open-only objects triangle for
    /// triangle, every panel opens the way it was authored, the chain stays
    /// exact at the fit's extremes, and the caps hold.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>A triangle as its three corners, rounded to 0.1 mm and sorted.</summary>
        static string TriKey(Vector3 a, Vector3 b, Vector3 c)
        {
            string K(Vector3 v) => $"{Mathf.RoundToInt(v.x * 1e4f)},{Mathf.RoundToInt(v.y * 1e4f)},{Mathf.RoundToInt(v.z * 1e4f)}";
            var k = new[] { K(a), K(b), K(c) };
            System.Array.Sort(k, System.StringComparer.Ordinal);
            return k[0] + "|" + k[1] + "|" + k[2];
        }

        static void AddTris(Dictionary<string, int> bag, Mesh m, int sign)
        {
            var v = m.vertices; var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var k = TriKey(v[t[i]], v[t[i + 1]], v[t[i + 2]]);
                bag.TryGetValue(k, out int n);
                bag[k] = n + sign;
            }
        }

        static void TestCarPartsConverter()
        {
            Line("CARPARTS STAGE 2: the converter and the baker, on the synthetic 5A TEST CARS");
            var anchor = new GameObject("CP_Test2");
            try
            {
                foreach (var key in CarModelBaker.TestKeys)
                {
                    string dir = "Assets/PSXRacing/Art/Car/Models/" + key + "/";
                    if (!System.IO.File.Exists(dir + key + "_parts.json")) { Check(false, key + ": exported (node tools/carmodels/export_models.mjs " + key + ")"); continue; }
                    var def = CarModelBaker.LoadTestCar(key);
                    if (def == null) { CarModelBaker.BakeTestCars(); def = CarModelBaker.LoadTestCar(key); }
                    if (def == null) { Check(false, key + ": baked"); continue; }
                    bool four = key.Contains("4door");
                    int nPanels = def.openingParts != null ? def.openingParts.Length : 0;
                    Check(def.HasParts && nPanels == (four ? 6 : 4) && def.bodyOpenMesh != null && def.coverOpenMesh != null,
                          $"{key}: baked with {(four ? 6 : 4)} panels, an open body and the covers on Body", nPanels);
                    if (!def.HasParts) continue;
                    var trunk = def.openingParts.FirstOrDefault(p => p.name == "Trunk");
                    Check(trunk.coverMesh != null, key + ": the trunk lid carries its own clear cover (it moves with the lid)");
                    Check(def.hasEngineMount && def.engineLayout == (four ? EngineLayout.Transverse : EngineLayout.Longitudinal) &&
                          def.engineClearBox != null && def.engineClearBox.Length == 6 && def.engineDownpipeSide != 0,
                          $"{key}: Engine_Mount {(four ? "transverse" : "longitudinal")}, a clear box of 6, the downpipe side known", def.engineLayout);
                    Check(def.kitMounts != null && def.kitMounts.Length == 2, key + ": Kit_Battery and Kit_Radiator on Bay");

                    // THE FRAME: authored (Blender, Z up, nose +Y) -> GLB / OBJ (x, z, -y) -> imported (x negated)
                    var hood = def.openingParts.First(p => p.name == "Hood");
                    var bat = def.kitMounts.First(k => k.name == "Kit_Battery");
                    Vector3 hoodWant = new Vector3(0f, 0.765f, -0.96f), batWant = new Vector3(0.52f, 0.39f, -1.70f);
                    Vector3 mountWant = four ? new Vector3(0.30f, 0.48f, -1.55f) : new Vector3(0f, 0.52f, -1.05f);
                    Check((hood.pivot - hoodWant).magnitude < 0.001f && (bat.pos - batWant).magnitude < 0.001f && (def.engineMountPos - mountWant).magnitude < 0.001f,
                          key + ": the hood hinge, Kit_Battery and Engine_Mount where they were authored (1 mm)",
                          $"hood {hood.pivot.ToString("F3")} battery {bat.pos.ToString("F3")} mount {def.engineMountPos.ToString("F3")}");
                    float yawErr = Quaternion.Angle(bat.rot, Quaternion.Euler(0f, -12f, 0f));
                    Check(yawErr < 0.1f, key + ": Kit_Battery's yaw (12 deg authored, mirrored by the import)", yawErr.ToString("0.000") + " deg off");
                    // the mount's crank (+Y) and bore (+Z) as authored
                    var crank = def.engineMountRot * Vector3.up; var bore = def.engineMountRot * Vector3.forward;
                    var crankWant = four ? new Vector3(-0.9848f, 0f, 0.1736f) : new Vector3(0f, 0f, -1f);
                    Check(Vector3.Angle(crank, four ? new Vector3(-1f, 0f, 0f) : crankWant) < (four ? 0.1f : 0.1f) && bore.y > 0.9f,
                          key + ": the mount's crank along the authored line, the bore up", $"crank {crank.ToString("F3")} bore {bore.ToString("F3")}");

                    // THE CLOSED MATCH: closed == open + every part - the open-only objects, triangle for triangle
                    var openOnlyGo = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(dir + key + "_openonly.obj");
                    var openOnly = openOnlyGo != null ? openOnlyGo.GetComponentInChildren<MeshFilter>().sharedMesh : null;
                    var bag = new Dictionary<string, int>();
                    AddTris(bag, def.bodyMesh, 1);
                    AddTris(bag, def.bodyOpenMesh, -1);
                    foreach (var p in def.openingParts) AddTris(bag, p.mesh, -1);
                    if (openOnly != null) AddTris(bag, openOnly, 1);
                    int extra = bag.Values.Where(n => n > 0).Sum(), missing = -bag.Values.Where(n => n < 0).Sum();
                    Check(openOnly != null && extra == 0 && missing == 0,
                          $"{key}: the closed body equals the open body + the {nPanels} parts - the open-only objects, triangle for triangle (0.1 mm)",
                          $"closed {def.bodyMesh.triangles.Length / 3}t; only in the closed body {extra}, only in open + parts {missing}");

                    // every panel opens the way it was authored; the chain is exact
                    var shell = OnFoot.CarShell.Spawn(anchor.transform, def, 0, out _, solid: false);
                    var mf = shell.Find("Body").GetComponent<MeshFilter>();
                    var cp = CarParts.Set(mf, def, CarParts.All, true);
                    Check(cp != null && mf.sharedMesh == def.bodyOpenMesh, key + ": all open, the body wears the open body");
                    if (cp == null) { Object.DestroyImmediate(shell.gameObject); continue; }
                    float cx = def.bodyMesh.bounds.center.x;
                    foreach (var p in def.openingParts)
                    {
                        int bit = CarParts.BitOf(p.name);
                        var part = cp.PartTransform(bit);
                        var lo = new Vector3(1e9f, 1e9f, 1e9f); var hi = -lo;
                        foreach (var v in p.mesh.vertices)
                        {
                            var q = mf.transform.InverseTransformPoint(part.TransformPoint(v));
                            lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
                        }
                        var b = p.mesh.bounds;
                        bool lid = bit == CarParts.Hood || bit == CarParts.Trunk;
                        bool ok; string got;
                        if (lid) { ok = hi.y > b.max.y + 0.3f; got = $"top {b.max.y:0.00} -> {hi.y:0.00} m"; }
                        else
                        {
                            float wasOut = Mathf.Max(Mathf.Abs(b.min.x - cx), Mathf.Abs(b.max.x - cx));
                            float nowOut = Mathf.Max(Mathf.Abs(lo.x - cx), Mathf.Abs(hi.x - cx));
                            ok = nowOut > wasOut + 0.3f; got = $"out {wasOut:0.00} -> {nowOut:0.00} m";
                        }
                        Check(ok, $"{key}: {p.name} opens the way it was authored ({(lid ? "its free edge rises" : "its rear edge swings out")})", got);
                    }
                    foreach (var sc in new[] { new Vector3(0.7f, 1f, 1.2f), new Vector3(1.2f, 1f, 0.7f) })
                    {
                        mf.transform.localScale = sc;
                        cp.Step(0f);
                        float err = CarPartsShots.ClosedError(cp, mf), shear = CarPartsShots.MaxEdgeChange(cp);
                        Check(err < 1e-5f && shear < 1e-5f, $"{key}: fit {sc.x:0.0} x {sc.z:0.0}: the parts at the closed body (0 mm) and no shear",
                              $"{err * 1000f:0.0000} / {shear * 1000f:0.0000} mm");
                    }
                    CarParts.TearDown(mf);
                    Check(mf.sharedMesh == def.bodyMesh, key + ": the last shut puts the closed body back");
                    Object.DestroyImmediate(shell.gameObject);

                    // the caps, as the converter counted them
                    var json = System.IO.File.ReadAllText(dir + key + "_parts.json");
                    int closedT = int.Parse(System.Text.RegularExpressions.Regex.Match(json, "\"trisClosed\":\\s*(\\d+)").Groups[1].Value);
                    int openT = int.Parse(System.Text.RegularExpressions.Regex.Match(json, "\"trisOpenOnly\":\\s*(\\d+)").Groups[1].Value);
                    Check(closedT <= 10000 && openT <= (four ? 3500 : 3000), $"{key}: closed set {closedT} <= 10000, open-only {openT} <= {(four ? 3500 : 3000)} tris");
                }
            }
            finally
            {
                CarParts.ShutAllNow();
                Object.DestroyImmediate(anchor);
            }
        }
    }
}
