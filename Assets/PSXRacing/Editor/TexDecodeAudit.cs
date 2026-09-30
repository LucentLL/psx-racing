using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE 16-BIT DECODE AUDIT (the colour pass, C1b, 2026-09-29).
    ///
    /// The 16-bit set (ReleaseBudget.Psx16Label) is imported as linear data on
    /// every platform and decoded in the PSX shaders, switched per material by
    /// PSXTexDecode.cs. A material the switch misses draws its texels RAW -
    /// the x1.5-9 picture of 2026-09-27 to 09-29, on that one surface. This
    /// proves nothing is missed:
    ///
    ///   EDIT MODE (also the self-test's TestTexDecode)
    ///   * the imports: every texture of the set carries the label and a
    ///     linear import, nothing else does, and NO WebGL RGB565 override
    ///     sits on an sRGB-flagged texture (the 09-27 bug itself);
    ///   * every PSX material whose colour texture is linear is the set's
    ///     (a real mask on a colour slot would be decoded);
    ///   * no .mat on disk stores _MainTexRaw/_DeepTexRaw 1 (the runtime owns
    ///     the flag);
    ///   * every Resources.Load of a GameObject in Scripts goes through
    ///     PSXTexDecode.LoadPrefab; the six colour shaders decode through
    ///     PSXMainTex and compile;
    ///   * THE GREY CARD (needs a GPU): a 565 linear card decoded by PSX/Lit
    ///     against an RGBA32 sRGB card of the nearest code, decoded by the
    ///     hardware - within 1 code, at six greys from black to white.
    ///
    ///   PLAY MODE (tools\colour\texdecode-audit.ps1)
    ///   * every scene in the build, loaded as the player loads it and run
    ///     for a couple of seconds (grids, traffic, the city's tiles): every
    ///     material on every renderer has the flag its texture asks for;
    ///   * every prefab under Resources (car shells, city props, pizza
    ///     cargo) through the loader's own door, and every car shell's
    ///     liveries; no UI graphic shows a texture of the set.
    ///
    /// Report: PSXRacing_texdecode_audit.txt, ending TEXDECODE AUDIT OK.
    /// </summary>
    public static class TexDecodeAudit
    {
        public struct Row { public bool ok; public string what; public string got; }

        internal static StringBuilder log;
        internal static int failures;
        static string ReportPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_texdecode_audit.txt");

        static readonly string[] ColourShaders =
        {
            "PSXLit.shader", "PSXLitTransparent.shader", "PSXCarPaint.shader",
            "PSXWater.shader", "PSXDecal.shader", "PSXZoneLine.shader",
        };

        // ------------------------------------------------------------------
        //  Edit mode
        // ------------------------------------------------------------------

        public static List<Row> EditChecks(bool gpu)
        {
            var rows = new List<Row>();
            void Add(bool ok, string what, object got = null) =>
                rows.Add(new Row { ok = ok, what = what, got = got != null ? got.ToString() : null });

            // THE IMPORTS.
            int labelled = 0, linearLabelled = 0, set565 = 0;
            var srgb565 = new List<string>();
            var labelledSrgb = new List<string>();
            var notApplied = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/PSXRacing" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                bool lab = ReleaseBudget.HasPsx16Label(guid);
                var web = imp.GetPlatformTextureSettings("WebGL");
                bool is565 = web.overridden && web.format == TextureImporterFormat.RGB16;
                if (lab) { labelled++; if (!imp.sRGBTexture) linearLabelled++; else labelledSrgb.Add(path); }
                if (is565) { if (imp.sRGBTexture) srgb565.Add(path); else set565++; }
                if (ReleaseBudget.InSet(path, imp, lab) != lab) notApplied.Add(path);
            }
            Add(labelled > 0, "the 16-bit set is labelled '" + ReleaseBudget.Psx16Label + "'", labelled + " textures");
            Add(labelledSrgb.Count == 0, "every texture of the set is imported as linear data (decoded in the shader)",
                labelledSrgb.Count == 0 ? linearLabelled + " linear" : labelledSrgb.Count + " still sRGB, e.g. " + labelledSrgb[0]);
            Add(srgb565.Count == 0, "NO RGB565 WebGL override on an sRGB-flagged texture (WebGL2 has no sRGB 565: the 09-27 bug)",
                srgb565.Count == 0 ? set565 + " RGB565, all linear" : srgb565.Count + ", e.g. " + srgb565[0]);
            Add(notApplied.Count == 0, "the release budget's set and the labels agree (ReleaseBudget.Apply has run)",
                notApplied.Count == 0 ? null : notApplied.Count + ", e.g. " + notApplied[0]);

            // THE MATERIALS ON DISK.
            int psxMats = 0, onSet = 0;
            var strayLinear = new List<string>();
            var stored = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/PSXRacing" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !m.HasProperty(PSXTexDecode.MainTexRawId)) continue;
                psxMats++;
                foreach (var prop in new[] { "_MainTex", "_DeepTex" })
                {
                    if (!m.HasProperty(prop)) continue;
                    var t = m.GetTexture(prop) as Texture2D;
                    if (t == null) continue;
                    string tp = AssetDatabase.GetAssetPath(t);
                    if (!(AssetImporter.GetAtPath(tp) is TextureImporter ti)) continue;
                    bool lab = ReleaseBudget.HasPsx16Label(AssetDatabase.AssetPathToGUID(tp));
                    if (lab) onSet++;
                    else if (!ti.sRGBTexture) strayLinear.Add(path + " " + prop + " = " + tp);
                }
                string text = File.ReadAllText(path);
                if (text.Contains("_MainTexRaw: 1") || text.Contains("_DeepTexRaw: 1")) stored.Add(path);
            }
            Add(strayLinear.Count == 0, "no PSX colour slot holds a linear texture outside the set (it would be decoded)",
                strayLinear.Count == 0 ? psxMats + " PSX materials, " + onSet + " slots on the set" : strayLinear.Count + ", e.g. " + strayLinear[0]);
            Add(stored.Count == 0, "no .mat stores the decode flag (PSXTexDecode.cs sets it at runtime)",
                stored.Count == 0 ? null : stored.Count + ", e.g. " + stored[0]);

            // THE ONE DOOR OUT OF Resources.
            var doors = new List<string>();
            string scripts = Path.Combine(Application.dataPath, "PSXRacing", "Scripts");
            foreach (var f in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(f) == "PSXTexDecode.cs") continue;
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Contains("Resources.Load<GameObject>") && !lines[i].TrimStart().StartsWith("//"))
                        doors.Add(Path.GetFileName(f) + ":" + (i + 1));
            }
            Add(doors.Count == 0, "every prefab comes out of Resources through PSXTexDecode.LoadPrefab",
                doors.Count == 0 ? null : string.Join(", ", doors));

            // THE SHADERS.
            string shaders = Path.Combine(Application.dataPath, "PSXRacing", "Shaders");
            var undecoded = new List<string>();
            foreach (var f in ColourShaders)
            {
                string s = File.ReadAllText(Path.Combine(shaders, f));
                if (!s.Contains("#include \"PSXTexDecode.cginc\"") || s.Contains("tex2D(_MainTex,") || !s.Contains("PSXMainTex("))
                    undecoded.Add(f);
            }
            Add(undecoded.Count == 0, "the six colour shaders sample _MainTex through PSXMainTex",
                undecoded.Count == 0 ? null : string.Join(", ", undecoded));
            foreach (var name in new[] { "PSX/Lit", "PSX/LitTransparent", "PSX/CarPaint", "PSX/Water", "PSX/Decal", "PSX/ZoneLine" })
            {
                var sh = Shader.Find(name);
                Add(sh != null && !ShaderUtil.ShaderHasError(sh), name + " compiles with the decode");
            }

            if (gpu && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                GreyCard(rows);
            return rows;
        }

        /// <summary>A 565 linear card decoded by PSX/Lit against an RGBA32
        /// sRGB card of the nearest 8-bit code, decoded by the GPU. Both drawn
        /// emissive (lit = texel) with the fog pushed out of reach.</summary>
        static void GreyCard(List<Row> rows)
        {
            var sh = Shader.Find("PSX/Lit");
            if (sh == null) { rows.Add(new Row { ok = false, what = "grey card: PSX/Lit found" }); return; }
            float keepNear = Shader.GetGlobalFloat("_PSXFogNear"), keepFar = Shader.GetGlobalFloat("_PSXFogFar");
            Shader.SetGlobalFloat("_PSXFogNear", 1e5f);
            Shader.SetGlobalFloat("_PSXFogFar", 2e5f);
            var made = new List<Object>();
            // 64x32 at aspect 2 and ortho size .5: the view is x -1..1, y -.5..5,
            // so the two unit quads at x -.5 and +.5 fill the left and right halves.
            var rt = new RenderTexture(64, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Point };
            rt.Create();
            float worst = 0f;
            string worstAt = "";
            var cardLog = new StringBuilder();
            bool flagsOk = true;
            try
            {
                var root = new Vector3(0f, 25000f, 0f);
                var camGO = new GameObject("TexDecodeAudit.Cam") { hideFlags = HideFlags.HideAndDontSave };
                made.Add(camGO);
                var cam = camGO.AddComponent<Camera>();
                cam.orthographic = true; cam.orthographicSize = 0.5f;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
                cam.transform.SetPositionAndRotation(root + new Vector3(0f, 0f, -5f), Quaternion.identity);
                cam.targetTexture = rt;
                cam.ResetAspect();
                cam.enabled = false;

                // Six greys on the 5/6-bit ladder, black to white.
                int[] r5s = { 0, 2, 5, 11, 20, 31 };
                foreach (int r5 in r5s)
                {
                    int g6 = Mathf.RoundToInt(r5 * 63f / 31f);
                    ushort px = (ushort)((r5 << 11) | (g6 << 5) | r5);
                    var t565 = new Texture2D(1, 1, TextureFormat.RGB565, false, true) { filterMode = FilterMode.Point };
                    t565.LoadRawTextureData(new[] { (byte)(px & 0xff), (byte)(px >> 8) });
                    t565.Apply(false, false);
                    var ref8 = new Texture2D(1, 1, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point };
                    // The code the 565 level stands for: Unity truncates, so
                    // level L is the bin 8L..8L+7 (4G..4G+3) and its centre
                    // 8L+3.5 (4G+1.5), here 8L+4 (4G+2) - half a code off.
                    var c8 = new Color32((byte)(8 * r5 + 4), (byte)(4 * g6 + 2), (byte)(8 * r5 + 4), 255);
                    ref8.SetPixels32(new[] { c8 });
                    ref8.Apply(false, false);
                    made.Add(t565); made.Add(ref8);

                    var cards = new[] { t565, ref8 };
                    var mats = new Material[2];
                    for (int k = 0; k < 2; k++)
                    {
                        var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                        m.mainTexture = cards[k];
                        m.SetFloat("_Emission", 1f);
                        m.SetColor("_Color", Color.white);
                        m.SetFloat("_Cull", 0f);
                        PSXTexDecode.Stamp(m);
                        mats[k] = m; made.Add(m);
                        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                        q.hideFlags = HideFlags.HideAndDontSave;
                        Object.DestroyImmediate(q.GetComponent<Collider>());
                        q.transform.position = root + new Vector3(k == 0 ? -0.5f : 0.5f, 0f, 0f);
                        q.transform.localScale = new Vector3(1f, 1f, 1f);
                        q.GetComponent<MeshRenderer>().sharedMaterial = m;
                        made.Add(q);
                    }
                    if (mats[0].GetFloat(PSXTexDecode.MainTexRawId) != 2f || mats[1].GetFloat(PSXTexDecode.MainTexRawId) != 0f) flagsOk = false;

                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
                    read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    read.Apply();
                    RenderTexture.active = prev;
                    var a = read.GetPixel(rt.width / 4, rt.height / 2);        // the 565 card
                    var b = read.GetPixel(3 * rt.width / 4, rt.height / 2);    // the RGBA32 card
                    Object.DestroyImmediate(read);
                    float d = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)) * 255f;
                    if (d > worst) { worst = d; worstAt = $"r5 {r5}: 565 ({a.r * 255f:0},{a.g * 255f:0},{a.b * 255f:0}) vs 8-bit ({b.r * 255f:0},{b.g * 255f:0},{b.b * 255f:0})"; }
                    cardLog.Append($" {r5}/31:({a.r * 255f:0},{a.g * 255f:0},{a.b * 255f:0})v({b.r * 255f:0},{b.g * 255f:0},{b.b * 255f:0})");
                    foreach (var o in made.ToArray()) if (o is GameObject g && g != camGO) { Object.DestroyImmediate(g); made.Remove(o); }
                }
            }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                rt.Release(); Object.DestroyImmediate(rt);
                Shader.SetGlobalFloat("_PSXFogNear", keepNear);
                Shader.SetGlobalFloat("_PSXFogFar", keepFar);
            }
            rows.Add(new Row { ok = flagsOk, what = "grey card: the 565 linear card is flagged 2 (decode from the bin centre), the sRGB card 0" });
            rows.Add(new Row { ok = worst <= 1.01f, what = "grey card: PSX/Lit's decode of a 565 texel matches the GPU's sRGB decode of its bin's centre code within 1 code (6 greys)",
                               got = $"worst {worst:0.00} codes ({worstAt});" + cardLog });
        }

        // ------------------------------------------------------------------
        //  The audit: edit mode, then every built scene in play mode
        // ------------------------------------------------------------------

        [MenuItem("PSX Racing/Audit 16-bit Texture Decode (play mode)")]
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            Line("TEXDECODE AUDIT " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  target " + EditorUserBuildSettings.activeBuildTarget);
            Line("edit mode:");
            foreach (var r in EditChecks(gpu: true)) Check(r.ok, r.what, r.got);

            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || !File.Exists(scenes[0].path))
            {
                Check(false, "a built scene list (run the scene build)");
                Finish(); EditorApplication.Exit(1); return;
            }
            EditorSceneManager.OpenScene(scenes[0].path);
            RaceHandoff.ClearAll();
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TexDecodeAuditRunner").AddComponent<TexDecodeAuditRunner>();
        }

        internal static void Line(string s) => log.AppendLine(s);

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            Line(failures == 0 ? "TEXDECODE AUDIT OK" : "TEXDECODE AUDIT FAILED (" + failures + ")");
            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log("[TexDecodeAudit]\n" + log);
        }

        /// <summary>Every material on these renderers against the rule;
        /// returns the misses (first few named in the log).</summary>
        internal static int Walk(IList<Renderer> rs, string where, Dictionary<string, bool> labelCache,
                                 out int mats, out int decoding, out int onSet)
        {
            mats = 0; decoding = 0; onSet = 0;
            int miss = 0;
            var seen = new HashSet<Material>();
            foreach (var r in rs)
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m)) continue;
                    foreach (int id in new[] { PSXTexDecode.MainTexRawId, PSXTexDecode.DeepTexRawId })
                    {
                        int want = PSXTexDecode.Want(m, id);
                        if (want < 0) continue;
                        if (id == PSXTexDecode.MainTexRawId) mats++;
                        float got = m.GetFloat(id);
                        if (got > 0.5f) decoding++;
                        var tex = m.GetTexture(id == PSXTexDecode.MainTexRawId ? "_MainTex" : "_DeepTex");
                        bool set = IsSet(tex, labelCache);
                        if (set) onSet++;
                        // A texture of the set must arrive linear (want 1 or 2),
                        // and the flag must be what the texture asks for.
                        if (Mathf.RoundToInt(got) != want || (set && want < 1))
                        {
                            miss++;
                            if (miss <= 6)
                                Line("    MISSED in " + where + ": " + Name(r) + " / " + m.name + " / " + (tex != null ? tex.name : "no texture") +
                                     " flag " + got + " want " + want + (set ? " (set texture)" : ""));
                        }
                    }
                }
            }
            return miss;
        }

        internal static bool IsSet(Texture t, Dictionary<string, bool> cache)
        {
            if (t == null) return false;
            string p = AssetDatabase.GetAssetPath(t);
            if (string.IsNullOrEmpty(p)) return false;
            if (!cache.TryGetValue(p, out bool on))
                cache[p] = on = ReleaseBudget.HasPsx16Label(AssetDatabase.AssetPathToGUID(p));
            return on;
        }

        static string Name(Component c)
        {
            var t = c.transform;
            string s = t.name;
            for (int i = 0; i < 2 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }

    public class TexDecodeAuditRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            var cache = new Dictionary<string, bool>();
            var scenes = EditorBuildSettings.scenes;
            int totalMiss = 0, totalMats = 0, totalDecoding = 0, totalScenes = 0;
            TexDecodeAudit.Line("play mode, every scene in the build:");
            for (int si = 0; si < scenes.Length; si++)
            {
                if (!scenes[si].enabled || !File.Exists(scenes[si].path)) continue;
                string name = Path.GetFileNameWithoutExtension(scenes[si].path);
                bool loaded = true;
                try { SceneManager.LoadScene(scenes[si].path, LoadSceneMode.Single); }
                catch (System.Exception e) { loaded = false; TexDecodeAudit.Line("  " + name + ": did not load (" + e.GetType().Name + ")"); }
                if (!loaded) continue;
                // Long enough for the grid, the traffic and the city's first
                // ring of tiles; the real time covers a slow load.
                float t0 = Time.realtimeSinceStartup;
                for (int f = 0; f < 90 || Time.realtimeSinceStartup - t0 < 3f; f++) { yield return null; if (f > 2000) break; }
                var rs = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
                int miss = TexDecodeAudit.Walk(rs, name, cache, out int mats, out int dec, out int onSet);
                totalScenes++; totalMiss += miss; totalMats += mats; totalDecoding += dec;
                TexDecodeAudit.Check(miss == 0, name + ": every PSX material decodes exactly when its texture asks",
                                     rs.Length + " renderers, " + mats + " PSX materials, " + dec + " decoding, " + onSet + " slots on the set" +
                                     (miss > 0 ? ", " + miss + " MISSED" : ""));
                // No UI graphic may show a texture of the set: UI/Default has
                // no decode and would draw it raw.
                int ui = 0;
                foreach (var g in Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsInactive.Include))
                    if (TexDecodeAudit.IsSet(g.mainTexture, cache)) { ui++; if (ui <= 3) TexDecodeAudit.Line("    UI shows a set texture: " + g.name + " / " + g.mainTexture.name); }
                if (ui > 0) TexDecodeAudit.Check(false, name + ": no UI graphic shows a 16-bit set texture", ui);
            }
            TexDecodeAudit.Check(totalScenes > 0, "scenes walked", totalScenes + " scenes, " + totalMats + " PSX materials, " + totalDecoding + " decoding, " + totalMiss + " missed");

            // THE ROSTER AND EVERY PREFAB IN Resources, through the loader's door.
            TexDecodeAudit.Line("the car roster and Resources prefabs:");
            int shells = 0, liveries = 0, missL = 0;
            int prefabs = 0, missP = 0;
            const string res = "Assets/PSXRacing/Resources/";
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/PSXRacing/Resources" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith(res)) continue;
                string rp = path.Substring(res.Length);
                rp = rp.Substring(0, rp.Length - Path.GetExtension(rp).Length);
                GameObject go;
                if (rp.StartsWith(CarModelLibrary.ResourceDir))
                {
                    var def = CarModelLibrary.Load(rp.Substring(CarModelLibrary.ResourceDir.Length));
                    if (def == null) continue;
                    shells++;
                    var lm = new List<Material>();
                    if (def.skinMaterials != null) lm.AddRange(def.skinMaterials);
                    if (def.wheelMaterial != null) lm.Add(def.wheelMaterial);
                    foreach (var m in lm)
                    {
                        if (m == null) continue;
                        int want = PSXTexDecode.Want(m, PSXTexDecode.MainTexRawId);
                        if (want < 0) continue;
                        liveries++;
                        bool set = TexDecodeAudit.IsSet(m.mainTexture, cache);
                        if (Mathf.RoundToInt(m.GetFloat(PSXTexDecode.MainTexRawId)) != want || (set && want < 1))
                        { missL++; if (missL <= 6) TexDecodeAudit.Line("    livery MISSED: " + def.key + " / " + m.name); }
                    }
                    go = def.gameObject;
                }
                else go = PSXTexDecode.LoadPrefab(rp);
                if (go == null) continue;
                prefabs++;
                missP += TexDecodeAudit.Walk(go.GetComponentsInChildren<Renderer>(true), rp, cache, out _, out _, out _);
            }
            TexDecodeAudit.Check(shells > 0 && missL == 0, "every car shell's liveries decode (CarModelLibrary.Load)",
                                 shells + " shells, " + liveries + " liveries" + (missL > 0 ? ", " + missL + " MISSED" : ""));
            TexDecodeAudit.Check(prefabs > 0 && missP == 0, "every Resources prefab decodes through PSXTexDecode.LoadPrefab",
                                 prefabs + " prefabs" + (missP > 0 ? ", " + missP + " MISSED" : ""));

            Time.timeScale = 1f;
            RaceHandoff.ClearAll();
            TexDecodeAudit.Finish();
            EditorApplication.Exit(TexDecodeAudit.failures == 0 ? 0 : 1);
        }
    }
}
