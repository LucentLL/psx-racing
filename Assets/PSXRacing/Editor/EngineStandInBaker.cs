using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARPARTS STAGE 4: THE STAND-IN ENGINES AND BAY KIT, built in code and
    /// baked into Resources/EngineModels/&lt;KEY&gt;.prefab (no engine art yet:
    /// the engine brief is with the owner). Plainly labelled: flat colour
    /// blocks with STAND-IN on the block. One named piece per object of the
    /// fault table (Eng_Block, Eng_Head, Eng_ExhaustManifold ... Bay_Battery
    /// ...), boxes sized by CarXray.EngineDims at a reference 4.4 x 1.7 m car,
    /// subdivided so the cost means something: about 12,000 triangles HD and
    /// 4,000 low for an engine with every option, 3,000 / 1,200 for the kit.
    /// An inline or rotary engine's exhaust manifold is on +X only (rust red,
    /// the intake grey on -X), so a wrong reflection shows.
    ///   Batch: -executeMethod PSXRacing.EditorTools.EngineStandInBaker.Bake
    /// </summary>
    public static class EngineStandInBaker
    {
        public const string OutDir = "Assets/PSXRacing/Resources/EngineModels";
        const string GenDir = "Assets/PSXRacing/Generated/EngineModels";
        const float RefL = 4.4f, RefW = 1.7f;
        const int SubHD = 6, SubLow = 3, KitSubHD = 5, KitSubLow = 3;

        // the atlas swatches (u0, v0, u1, v1) and their colours
        static readonly Dictionary<string, (Rect r, Color32 c)> Sw = new Dictionary<string, (Rect, Color32)>
        {
            { "block", (new Rect(0.00f, 0.50f, 0.12f, 0.50f), new Color32(120, 124, 130, 255)) },
            { "head", (new Rect(0.12f, 0.50f, 0.12f, 0.50f), new Color32(150, 150, 156, 255)) },
            { "cover", (new Rect(0.24f, 0.50f, 0.12f, 0.50f), new Color32(170, 40, 34, 255)) },
            { "exhaust", (new Rect(0.36f, 0.50f, 0.12f, 0.50f), new Color32(150, 78, 40, 255)) },
            { "intake", (new Rect(0.00f, 0.00f, 0.12f, 0.50f), new Color32(186, 190, 196, 255)) },
            { "black", (new Rect(0.12f, 0.00f, 0.12f, 0.50f), new Color32(34, 34, 36, 255)) },
            { "rubber", (new Rect(0.24f, 0.00f, 0.12f, 0.50f), new Color32(52, 50, 48, 255)) },
            { "amber", (new Rect(0.36f, 0.00f, 0.12f, 0.50f), new Color32(220, 150, 40, 255)) },
            { "label", (new Rect(0.50f, 0.00f, 0.50f, 1.00f), new Color32(255, 255, 255, 255)) },
        };

        [MenuItem("PSX Racing/Cars/Bake Stand-in Engines")]
        public static void Bake()
        {
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(GenDir);
            var mat = MakeMaterial();
            var log = new List<string>();
            foreach (var key in EngineLibrary.Keys) log.Add(BakeOne(key, false, mat));
            log.Add(BakeOne(EngineLibrary.BayKit, true, mat));
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_engine_standins.txt"), string.Join("\n", log));
            Debug.Log("[EngineStandInBaker]\n" + string.Join("\n", log));
        }

        static Material MakeMaterial()
        {
            string tp = GenDir + "/EngineStandInAtlas.asset", mp = GenDir + "/EngineStandIn.mat";
            var tex = new Texture2D(256, 128, TextureFormat.RGBA32, false) { name = "EngineStandInAtlas", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[256 * 128];
            foreach (var kv in Sw)
            {
                if (kv.Key == "label") continue;
                var r = kv.Value.r;
                for (int y = (int)(r.yMin * 128); y < (int)(r.yMax * 128); y++)
                    for (int x = (int)(r.xMin * 256); x < (int)(r.xMax * 256); x++) px[y * 256 + x] = kv.Value.c;
            }
            // the label half: the stand-in checker with STAND-IN, in grey
            var st = CarPartsStandIn.Texture;
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    var c = st.GetPixel(x * st.width / 128, y * st.height / 128);
                    float g = c.grayscale;
                    px[y * 256 + 128 + x] = g > 0.8f ? new Color32(236, 236, 232, 255) : g < 0.1f ? new Color32(14, 14, 16, 255)
                                          : (Color32)Color.Lerp(new Color(0.35f, 0.36f, 0.38f), new Color(0.55f, 0.56f, 0.58f), g);
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(tp) != null) AssetDatabase.DeleteAsset(tp);
            AssetDatabase.CreateAsset(tex, tp);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (mat == null) { mat = new Material(Shader.Find("PSX/Lit")); AssetDatabase.CreateAsset(mat, mp); }
            mat.shader = Shader.Find("PSX/Lit");
            mat.mainTexture = tex;
            mat.color = Color.white;
            if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        struct Box { public Vector3 a, b; public string sw; public bool label; }
        class P { public string name, option; public bool fit; public List<Box> boxes = new List<Box>(); public Vector3 dT, dL; }

        static P Add(List<P> l, string name, string sw, Vector3 a, Vector3 b, bool fit = false, string option = null, bool label = false)
        {
            var p = new P { name = name, option = option, fit = fit };
            p.boxes.Add(new Box { a = Vector3.Min(a, b), b = Vector3.Max(a, b), sw = sw, label = label });
            l.Add(p);
            return p;
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>The stand-in's pieces for one key, in the engine frame (x across, y crank, z up).</summary>
        static List<P> EnginePieces(string key, out int exhaustSide)
        {
            var l = new List<P>();
            LifeSim.CarXray.EngineShape s;
            switch (key)
            {
                case EngineLibrary.I6: s = new LifeSim.CarXray.EngineShape { kind = "inline", perBank = 6, banks = 1 }; break;
                case EngineLibrary.V6: s = new LifeSim.CarXray.EngineShape { kind = "vee", perBank = 3, banks = 2 }; break;
                case EngineLibrary.V8: s = new LifeSim.CarXray.EngineShape { kind = "vee", perBank = 4, banks = 2 }; break;
                case EngineLibrary.F4: s = new LifeSim.CarXray.EngineShape { kind = "flat", perBank = 2, banks = 2 }; break;
                case EngineLibrary.F6: s = new LifeSim.CarXray.EngineShape { kind = "flat", perBank = 3, banks = 2 }; break;
                case EngineLibrary.R2: s = new LifeSim.CarXray.EngineShape { kind = "rotary", perBank = 2, banks = 1 }; break;
                default: s = new LifeSim.CarXray.EngineShape { kind = "inline", perBank = 4, banks = 1 }; break;
            }
            var dims = LifeSim.CarXray.EngineDims(s, RefL, RefW);
            float L = dims.x, hw = dims.y * 0.5f;
            bool vee = s.kind == "vee", flat = s.kind == "flat", rot = s.kind == "rotary";
            exhaustSide = vee || flat ? 0 : 1;
            float top = flat ? 0.24f : rot ? 0.24f : 0.25f;      // top of the block
            // ---- the block, sump, front ------------------------------------
            Add(l, "Eng_Block", "block", V(-hw * (vee ? 0.55f : flat ? 0.45f : 1f), 0.02f, flat ? -0.06f : -0.05f), V(hw * (vee ? 0.55f : flat ? 0.45f : 1f), L, top), true, label: true);
            Add(l, "Eng_OilPan", "black", V(-hw * 0.8f, 0.06f, -0.18f), V(hw * 0.8f, L - 0.05f, flat ? -0.06f : -0.05f), true);
            Add(l, "Eng_TimingCover", "black", V(-hw * 0.85f, L, 0f), V(hw * 0.85f, L + 0.04f, top + 0.12f), true);
            Add(l, "Eng_WaterPump", "intake", V(-0.06f, L + 0.04f, 0.10f), V(0.06f, L + 0.09f, 0.20f), true);
            Add(l, "Eng_DriveBelt", "rubber", V(-hw * 0.75f, L + 0.09f, -0.05f), V(hw * 0.75f, L + 0.11f, top), true);
            Add(l, "Eng_Mounts", "rubber", V(-hw - 0.08f, L * 0.5f - 0.04f, -0.02f), V(-hw - 0.02f, L * 0.5f + 0.04f, 0.06f), true)
                .boxes.Add(new Box { a = V(hw + 0.02f, L * 0.5f - 0.04f, -0.02f), b = V(hw + 0.08f, L * 0.5f + 0.04f, 0.06f), sw = "rubber" });
            Add(l, "Eng_Dipstick", "amber", V(hw * 0.6f, L * 0.4f, 0.1f), V(hw * 0.6f + 0.02f, L * 0.4f + 0.02f, top + 0.2f), true);
            Add(l, "Eng_Thermostat", "intake", V(-0.05f, L - 0.04f, top + 0.12f), V(0.05f, L + 0.02f, top + 0.18f), true);
            Add(l, "Eng_Hoses", "rubber", V(-0.04f, L + 0.02f, top + 0.05f), V(0.04f, L + 0.2f, top + 0.11f), true);
            Add(l, "Eng_Alternator", "intake", V(-hw - 0.10f, L - 0.13f, 0.05f), V(-hw, L - 0.01f, 0.17f), true);
            Add(l, "Eng_PSPump", "intake", V(hw, L - 0.12f, -0.02f), V(hw + 0.09f, L - 0.02f, 0.08f), true);
            Add(l, "Eng_Harness", "black", V(-hw * 0.9f, 0.05f, top + 0.10f), V(-hw * 0.9f + 0.04f, L - 0.05f, top + 0.13f), true);
            Add(l, "Eng_O2Sensor", "amber", V(hw + 0.10f, 0.10f, 0.04f), V(hw + 0.14f, 0.14f, 0.08f), true);
            // ---- the top end ------------------------------------------------
            if (vee)
            {
                float hx0 = hw * 0.45f, hx1 = hw;
                Add(l, "Eng_Head_L", "head", V(-hx1, 0.04f, top - 0.02f), V(-hx0, L - 0.02f, top + 0.12f), true);
                Add(l, "Eng_Head_R", "head", V(hx0, 0.04f, top - 0.02f), V(hx1, L - 0.02f, top + 0.12f), true);
                // a V8's two top ends: tall narrow pushrod covers, or wide overhead-cam ones
                string opt = key == EngineLibrary.V8 ? "ohc" : null;
                Add(l, "Eng_ValveCover_L", "cover", V(-hx1 + 0.02f, 0.06f, top + 0.12f), V(-hx0 - 0.02f, L - 0.06f, top + 0.19f), true, opt);
                Add(l, "Eng_ValveCover_R", "cover", V(hx0 + 0.02f, 0.06f, top + 0.12f), V(hx1 - 0.02f, L - 0.06f, top + 0.19f), true, opt);
                if (key == EngineLibrary.V8)
                {
                    Add(l, "Eng_ValveCover_L", "black", V(-hx1 + 0.05f, 0.06f, top + 0.12f), V(-hx0 - 0.03f, L - 0.06f, top + 0.24f), true, "pushrod");
                    Add(l, "Eng_ValveCover_R", "black", V(hx0 + 0.03f, 0.06f, top + 0.12f), V(hx1 - 0.05f, L - 0.06f, top + 0.24f), true, "pushrod");
                }
                Add(l, "Eng_Intake", "intake", V(-hx0, 0.08f, top), V(hx0, L - 0.08f, top + 0.18f), true);
                Add(l, "Eng_ExhaustManifold_L", "exhaust", V(-hw - 0.10f, 0.08f, 0.02f), V(-hw, L - 0.08f, top - 0.02f), true);
                Add(l, "Eng_ExhaustManifold_R", "exhaust", V(hw, 0.08f, 0.02f), V(hw + 0.10f, L - 0.08f, top - 0.02f), true);
                Add(l, "Eng_PlugLeads", "rubber", V(-hx1, 0.10f, top + 0.19f), V(hx1, 0.14f, top + 0.21f), true);
                Add(l, "Eng_CamSensor", "amber", V(hx0, L - 0.04f, top + 0.10f), V(hx0 + 0.04f, L, top + 0.15f), true);
            }
            else if (flat)
            {
                Add(l, "Eng_Head_L", "head", V(-hw, 0.04f, -0.05f), V(-hw * 0.45f, L - 0.02f, 0.16f), true);
                Add(l, "Eng_Head_R", "head", V(hw * 0.45f, 0.04f, -0.05f), V(hw, L - 0.02f, 0.16f), true);
                Add(l, "Eng_ValveCover_L", "cover", V(-hw - 0.06f, 0.06f, -0.03f), V(-hw, L - 0.06f, 0.14f), true);
                Add(l, "Eng_ValveCover_R", "cover", V(hw, 0.06f, -0.03f), V(hw + 0.06f, L - 0.06f, 0.14f), true);
                Add(l, "Eng_Intake", "intake", V(-hw * 0.4f, 0.08f, top), V(hw * 0.4f, L - 0.08f, top + 0.12f), true);
                Add(l, "Eng_ExhaustManifold_L", "exhaust", V(-hw * 0.95f, 0.08f, -0.22f), V(-hw * 0.5f, L - 0.08f, -0.08f), true);
                Add(l, "Eng_ExhaustManifold_R", "exhaust", V(hw * 0.5f, 0.08f, -0.22f), V(hw * 0.95f, L - 0.08f, -0.08f), true);
                Add(l, "Eng_PlugLeads", "rubber", V(-hw - 0.08f, 0.10f, 0.12f), V(hw + 0.08f, 0.14f, 0.14f), true);
                Add(l, "Eng_CamSensor", "amber", V(hw * 0.5f, L - 0.04f, 0.14f), V(hw * 0.5f + 0.04f, L, 0.19f), true);
            }
            else
            {
                if (!rot)
                {
                    Add(l, "Eng_Head", "head", V(-hw * 0.9f, 0.04f, top), V(hw * 0.9f, L - 0.02f, top + 0.12f), true);
                    Add(l, "Eng_ValveCover", "cover", V(-hw * 0.72f, 0.06f, top + 0.12f), V(hw * 0.72f, L - 0.06f, top + 0.19f), true);
                    Add(l, "Eng_CamSensor", "amber", V(hw * 0.55f, L - 0.04f, top + 0.08f), V(hw * 0.55f + 0.04f, L, top + 0.14f), true);
                    Add(l, "Eng_PlugLeads", "rubber", V(-0.06f, 0.10f, top + 0.19f), V(0.06f, L - 0.10f, top + 0.21f), true);
                }
                else
                    Add(l, "Eng_PlugLeads", "rubber", V(-hw - 0.04f, 0.10f, 0.10f), V(-hw, L - 0.10f, 0.14f), true);
                // ONE-SIDED: the intake on -X (grey), the exhaust on +X (rust) - the mirror test's tell
                Add(l, "Eng_Intake", "intake", V(-hw - 0.12f, 0.08f, 0.12f), V(-hw, L - 0.08f, top + 0.09f), true);
                Add(l, "Eng_ExhaustManifold", "exhaust", V(hw, 0.08f, 0.06f), V(hw + 0.10f, L - 0.08f, top + 0.05f), true);
            }
            Add(l, "Eng_OilFiller", "amber", V(-0.03f, L * 0.6f, top + (vee || flat ? 0.18f : rot ? 0f : 0.19f)), V(0.03f, L * 0.6f + 0.06f, top + (vee || flat ? 0.22f : rot ? 0.04f : 0.23f)), true);
            Add(l, "Eng_Ignition", "black", V(hw * 0.4f, 0.02f, top + 0.10f), V(hw * 0.4f + 0.06f, 0.08f, top + 0.18f), true);
            Add(l, "Eng_ThrottleBody", "intake", V(-hw * 0.3f, -0.02f, top + 0.06f), V(hw * 0.05f, 0.08f, top + 0.14f), true);
            Add(l, "Eng_AirBox", "black", V(-hw - 0.30f, L * 0.2f, top + 0.02f), V(-hw - 0.12f, L * 0.2f + 0.26f, top + 0.18f), false);
            // ---- the gearbox, by layout -------------------------------------
            Add(l, "Eng_Gearbox_Inline", "block", V(-0.15f, -0.65f, -0.12f), V(0.15f, 0f, 0.20f), false, "gearbox_inline");
            Add(l, "Eng_Gearbox_Transaxle", "block", V(-0.20f, -0.38f, -0.22f), V(0.26f, 0f, 0.22f), false, "gearbox_transaxle");
            // ---- the dress ---------------------------------------------------
            Add(l, "Eng_Turbo", "exhaust", V(hw + 0.10f, L * 0.35f, 0.10f), V(hw + 0.24f, L * 0.35f + 0.14f, 0.26f), false, "turbo1");
            Add(l, "Eng_Turbo_2", "exhaust", V(-hw - 0.24f, L * 0.35f, 0.10f), V(-hw - 0.10f, L * 0.35f + 0.14f, 0.26f), false, "turbo2");
            Add(l, "Eng_TurboPipes", "intake", V(-hw * 0.5f, L + 0.12f, top - 0.05f), V(hw + 0.2f, L + 0.18f, top + 0.02f), false, "turbo1");
            Add(l, "Eng_Supercharger", "intake", V(-hw * 0.45f, 0.15f, top + 0.22f), V(hw * 0.45f, L - 0.15f, top + 0.34f), false, "sc");
            Add(l, "Bay_Fan_Clutch", "black", V(-hw * 0.9f, L + 0.12f, 0.0f), V(hw * 0.9f, L + 0.16f, top + 0.12f), false, "fanclutch");
            return l;
        }

        /// <summary>The kit's pieces, each about its own origin (a box-like item's
        /// base centre; the radiator's, fan's and intercooler's bottom-edge centre),
        /// with its default offset from the engine origin in CAR terms (x right,
        /// y up, z forward) for a transverse and a longitudinal inline-4.</summary>
        static List<P> KitPieces()
        {
            var l = new List<P>();
            P K(string n, string sw, Vector3 size, Vector3 dT, Vector3 dL, string opt = null)
            {
                var p = Add(l, n, sw, V(-size.x * 0.5f, 0f, -size.z * 0.5f), V(size.x * 0.5f, size.y, size.z * 0.5f), false, opt);
                p.dT = dT; p.dL = dL;
                return p;
            }
            K("Bay_Battery", "black", V(0.18f, 0.18f, 0.26f), V(-0.45f, -0.05f, 0.05f), V(-0.42f, 0.02f, 0.55f));
            { var rad = K("Bay_Radiator", "block", V(1.00f, 0.42f, 0.05f), V(0f, -0.28f, 0.48f), V(0f, -0.22f, 0.95f)); var bx = rad.boxes[0]; bx.label = true; rad.boxes[0] = bx; }
            K("Bay_Fan", "black", V(0.36f, 0.36f, 0.06f), V(0.15f, -0.24f, 0.42f), V(0f, -0.18f, 0.88f), "fan");
            K("Bay_Overflow", "intake", V(0.10f, 0.16f, 0.10f), V(0.42f, 0.0f, 0.38f), V(0.40f, 0.05f, 0.85f));
            K("Bay_PSReservoir", "intake", V(0.08f, 0.10f, 0.08f), V(0.30f, 0.05f, -0.05f), V(0.32f, 0.10f, 0.60f));
            K("Bay_Intercooler", "intake", V(0.60f, 0.16f, 0.06f), V(0f, -0.42f, 0.56f), V(0f, -0.38f, 1.02f), "ic");
            K("Bay_Intercooler_Top", "intake", V(0.40f, 0.06f, 0.30f), V(0f, 0.32f, 0.10f), V(0f, 0.30f, 0.30f), "ictop");
            K("Bay_BrakeMaster", "intake", V(0.10f, 0.10f, 0.20f), V(-0.40f, 0.08f, -0.30f), V(-0.40f, 0.12f, -0.05f));
            K("Bay_WasherBottle", "intake", V(0.14f, 0.20f, 0.14f), V(0.50f, -0.05f, 0.30f), V(0.50f, -0.02f, 0.75f));
            K("Bay_FuseBox", "black", V(0.14f, 0.08f, 0.20f), V(-0.30f, 0.10f, 0.25f), V(-0.30f, 0.12f, 0.35f));
            K("Bay_EngineOut", "rubber", V(0.50f, 0.04f, 0.50f), Vector3.zero, Vector3.zero, "engineout");
            return l;
        }

        static string BakeOne(string key, bool kit, Material mat)
        {
            int exhaust = 0;
            var pieces = kit ? KitPieces() : EnginePieces(key, out exhaust);
            string meshPath = GenDir + "/" + key + "_meshes.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
            var go = new GameObject(key);
            var d = go.AddComponent<EngineKitDef>();
            d.key = key; d.standIn = true; d.isKit = kit; d.material = mat; d.exhaustSide = exhaust;
            var outPieces = new List<EngineKitDef.Piece>();
            Mesh container = null;
            int trisHD = 0, trisLow = 0;
            foreach (var p in pieces)
            {
                var hd = Build(p, kit ? KitSubHD : SubHD, key + "_" + p.name + (p.option != null ? "_" + p.option : "") + "_hd");
                var low = Build(p, kit ? KitSubLow : SubLow, key + "_" + p.name + (p.option != null ? "_" + p.option : "") + "_low");
                if (container == null) { AssetDatabase.CreateAsset(hd, meshPath); container = hd; }
                else AssetDatabase.AddObjectToAsset(hd, meshPath);
                AssetDatabase.AddObjectToAsset(low, meshPath);
                var mn = p.boxes[0].a; var mx = p.boxes[0].b;
                foreach (var b in p.boxes) { mn = Vector3.Min(mn, b.a); mx = Vector3.Max(mx, b.b); }
                outPieces.Add(new EngineKitDef.Piece { name = p.name, option = p.option, hd = hd, low = low, min = mn, max = mx, inFit = p.fit,
                                                       defaultTransverse = p.dT, defaultLongitudinal = p.dL });
                trisHD += (int)hd.GetIndexCount(0) / 3; trisLow += (int)low.GetIndexCount(0) / 3;
            }
            d.pieces = outPieces.ToArray();
            string prefab = OutDir + "/" + key + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefab);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            long bytes = new FileInfo(meshPath).Length + new FileInfo(prefab).Length;
            return $"{key,-8} STAND-IN {(kit ? "kit" : "engine")}: {pieces.Count} pieces, all options {trisHD} tris HD / {trisLow} low, exhaust side {(exhaust > 0 ? "+X" : exhaust < 0 ? "-X" : "both")}, {bytes / 1024} KB on disk";
        }

        /// <summary>A piece's boxes, each face an n x n grid, UVs on its swatch
        /// (the label faces show STAND-IN).</summary>
        static Mesh Build(P p, int n, string name)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var b in p.boxes)
            {
                var (r, _) = Sw[b.sw];
                var lab = Sw["label"].r;
                // six faces: origin corner, two edge vectors (outward by the right-hand order)
                var a = b.a; var s = b.b - b.a;
                var faces = new (Vector3 o, Vector3 u, Vector3 w)[]
                {
                    (a, V(0, s.y, 0), V(0, 0, s.z)),                                       // -x
                    (a + V(s.x, 0, 0), V(0, 0, s.z), V(0, s.y, 0)),                       // +x
                    (a, V(0, 0, s.z), V(s.x, 0, 0)),                                       // -y
                    (a + V(0, s.y, 0), V(s.x, 0, 0), V(0, 0, s.z)),                       // +y
                    (a, V(s.x, 0, 0), V(0, s.y, 0)),                                       // -z
                    (a + V(0, 0, s.z), V(0, s.y, 0), V(s.x, 0, 0)),                       // +z
                };
                int fi = 0;
                foreach (var (o, u, w) in faces)
                {
                    var rr = b.label && (fi == 0 || fi == 1) ? lab : r;
                    int i0 = v.Count;
                    for (int j = 0; j <= n; j++)
                        for (int i = 0; i <= n; i++)
                        {
                            float fu = i / (float)n, fw = j / (float)n;
                            v.Add(o + u * fu + w * fw);
                            uv.Add(new Vector2(rr.xMin + rr.width * (b.label && fi < 2 ? fw : 0.5f), rr.yMin + rr.height * (b.label && fi < 2 ? fu : 0.5f)));
                        }
                    for (int j = 0; j < n; j++)
                        for (int i = 0; i < n; i++)
                        {
                            int q = i0 + j * (n + 1) + i;
                            tri.AddRange(new[] { q, q + n + 1, q + 1, q + 1, q + n + 1, q + n + 2 });
                        }
                    fi++;
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            // wind every face outward from its box (the order above is checked, not trusted)
            FixWinding(m, p);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static void FixWinding(Mesh m, P p)
        {
            var v = m.vertices; var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                var centre = (a + b + c) / 3f;
                // the box this triangle is on: the nearest centre
                Vector3 bc = Vector3.zero; float best = float.MaxValue;
                foreach (var bx in p.boxes)
                {
                    var cc = (bx.a + bx.b) * 0.5f;
                    var ext = (bx.b - bx.a) * 0.5f + Vector3.one * 1e-4f;
                    var dd = centre - cc;
                    bool on = Mathf.Abs(dd.x) <= ext.x && Mathf.Abs(dd.y) <= ext.y && Mathf.Abs(dd.z) <= ext.z;
                    float d2 = on ? 0f : dd.sqrMagnitude;
                    if (d2 < best) { best = d2; bc = cc; }
                }
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), centre - bc) < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
            m.triangles = t;
        }

    }
}
