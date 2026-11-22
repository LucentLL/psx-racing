using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE LIGHT-POP METER (the lightpop round, 2026-11-22; owner after PC52:
    /// "now I constantly see light pop-in. Streetlights, headlights").
    ///
    /// Every frame it renders the game's own camera TWICE at the same pose:
    ///   A - the frame as the game draws it;
    ///   B - the same frame with every light hand-over put back to how the
    ///       frame before had it: the lamp table at last frame's weights (today's
    ///       positions, intensities and distance fades - so a car braking or a
    ///       lamp fading by its own distance is NOT a change), last frame's lamp
    ///       field, the headlight table at last frame's weights, the lamp cones
    ///       at last frame's arrays, and every halo / lens / beam renderer as it
    ///       was (one that came this frame - a tile's halos, a spawned car's
    ///       lenses - switched off).
    /// A - B is then exactly the light that SWITCHED this frame, and nothing
    /// that moved. A frame where it reaches <see cref="MinPx"/> pixels that
    /// changed by at least <see cref="MinDelta"/> levels AND by
    /// <see cref="MinRel"/> of themselves is a POP frame; it is rendered again
    /// once per component (only that component put back) to say which one did
    /// it. Consecutive pop frames of one cause are one event. A camera cut (the
    /// eye jumps or turns as no driving could) is a new shot, not counted; the
    /// frames after it are.
    ///
    /// A distance fade over 30 m changes a lamp by ~3% a frame at 100 km/h and
    /// 30 fps; a quarter-second timer by 13%; a switch by 100%: MinRel 10%
    /// keeps the first out and the other two in.
    ///
    /// Not seen: a car or tile that LEAVES (its renderers cannot be put back).
    /// Reports and frame pairs go to <see cref="OutDir"/>.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class LightPopMeter : MonoBehaviour
    {
        public static LightPopMeter Instance { get; private set; }
        public static string OutDir;
        public int W = 512, H = 288;
        public const int MinDelta = 6;           // 8-bit levels
        public const float MinRel = 0.10f;       // of the brighter of the two
        public const int MinPx = 40;             // at 512x288 (0.027% of the frame)
        public const int PairsPerCause = 3;
        public const float CutJumpM = 20f, CutTurnDeg = 45f;
        public const int AfterCutFrames = 12;

        public enum Cause { Street = 0, Point, Field, Head, Halo, Lens, Beam, Cones, Mixed, Noise, Count }
        static readonly string[] CauseName = { "street lamp table", "tail lamp table", "lamp field", "headlight table",
                                               "lamp halos", "car lenses", "car beam cones", "lamp cones", "mixed", "NOISE (no light changed)" };
        /// <summary>A lamp's or car's weight moved less than this: no evidence.</summary>
        const float MinWeightStep = 0.02f;

        // ---- the segment being measured ----
        string seg;
        bool measuring;
        float segDistM, segSeconds;
        int segFrames, segPopFrames, segCuts;
        Vector3 lastEye, lastFwd, lastCarPos;
        bool haveEye;
        int framesSinceCut = 9999;
        int lastShot = -1;
        Transform carT;
        readonly int[] evCount = new int[(int)Cause.Count];
        readonly int[] evAfterCut = new int[(int)Cause.Count];
        readonly double[] evLight = new double[(int)Cause.Count];
        readonly int[] lastPopFrame = new int[(int)Cause.Count];
        readonly List<(float score, string file)>[] pairs = new List<(float, string)>[(int)Cause.Count];
        readonly StringBuilder csv = new StringBuilder();
        readonly StringBuilder summary = new StringBuilder();
        int stripEvery, stripN;
        int afterCutSave;
        int cutIndex;
        long noiseFrames, noiseZero;
        int selfFrames, selfNoisy, selfWorst;

        // ---- the frame before ----
        bool havePrev;
        readonly Dictionary<int, float> prevStreet = new Dictionary<int, float>(), curStreet = new Dictionary<int, float>();
        readonly Dictionary<int, float> prevHead = new Dictionary<int, float>(), curHead = new Dictionary<int, float>();
        Texture prevFieldTex, curFieldTex;
        Vector4 prevFieldST, curFieldST, prevFieldY, curFieldY, saveFieldY;
        string prevFieldState = "", curFieldState = "";
        readonly Vector4[] prevConeA = new Vector4[StreetLights.ConeMax], prevConeB = new Vector4[StreetLights.ConeMax];
        readonly Vector4[] curConeA = new Vector4[StreetLights.ConeMax], curConeB = new Vector4[StreetLights.ConeMax];
        bool prevConeOn, curConeOn;
        HashSet<Renderer> prevOn = new HashSet<Renderer>(), curOn = new HashSet<Renderer>();
        readonly Dictionary<Renderer, Cause> rendCause = new Dictionary<Renderer, Cause>();
        readonly List<Renderer> lightRends = new List<Renderer>();
        readonly List<(Renderer r, bool was)> undo = new List<(Renderer, bool)>();

        RenderTexture rt, rtShown;
        Material shownMat;
        Texture2D readA, readB;
        byte[] pixA, pixB, pixB0;
        static readonly int FieldTexId = Shader.PropertyToID("_PSXLampField");
        static readonly int FieldSTId = Shader.PropertyToID("_PSXLampFieldST");
        static readonly int FieldYId = Shader.PropertyToID("_PSXLampFieldY");
        static readonly int ConeAId = Shader.PropertyToID("_PSXConeA");
        static readonly int ConeBId = Shader.PropertyToID("_PSXConeB");

        public static LightPopMeter Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("LightPopMeter");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LightPopMeter>();
            return Instance;
        }

        void Awake()
        {
            for (int i = 0; i < pairs.Length; i++) pairs[i] = new List<(float, string)>();
            csv.AppendLine("segment,frame,t,cause,px,light,cx,cy,dist_m,after_cut,detail");
        }

        void OnDestroy()
        {
            StreetLights.MeterOn = false; CarLights.MeterOn = false;
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (rtShown != null) { rtShown.Release(); Destroy(rtShown); }
            if (shownMat != null) Destroy(shownMat);
        }

        /// <summary>Start a segment (a route in one view, a replay).</summary>
        public void Begin(string name, Transform car, int stripEveryFrames = 0)
        {
            seg = name; carT = car; measuring = true;
            segDistM = 0f; segSeconds = 0f; segFrames = 0; segPopFrames = 0; segCuts = 0;
            haveEye = false; havePrev = false; framesSinceCut = 9999;
            for (int i = 0; i < evCount.Length; i++) { evCount[i] = 0; evAfterCut[i] = 0; evLight[i] = 0; lastPopFrame[i] = -100; pairs[i].Clear(); }
            stripEvery = stripEveryFrames; stripN = 0; afterCutSave = 0; cutIndex = 0;
            noiseFrames = noiseZero = 0; selfFrames = selfNoisy = selfWorst = 0;
            StreetLights.MeterOn = true; CarLights.MeterOn = true;
            if (carT != null) lastCarPos = carT.position;
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(OutDir, seg));
        }

        /// <summary>End the segment; its summary line (also kept for <see cref="Report"/>).</summary>
        public string End()
        {
            measuring = false;
            float km = segDistM / 1000f;
            var sb = new StringBuilder();
            sb.Append(seg).Append(": ").Append((segDistM / 1000f).ToString("0.00", CultureInfo.InvariantCulture)).Append(" km, ")
              .Append(segSeconds.ToString("0", CultureInfo.InvariantCulture)).Append(" s, ").Append(segFrames).Append(" frames, ")
              .Append(segCuts).Append(" cuts; quiet frames ").Append(noiseFrames > 0 ? (100.0 * noiseZero / noiseFrames).ToString("0", CultureInfo.InvariantCulture) : "-").Append("%; meter self-noise ").Append(selfNoisy).Append("/").Append(selfFrames)
              .Append(" frames, worst ").Append(selfWorst).Append(" px; tile halo births faded so far ").Append(NightGlow.BirthsFaded).Append("\n");
            int total = 0;
            for (int i = 0; i < (int)Cause.Count; i++) if (i != (int)Cause.Noise) total += evCount[i];
            sb.Append("    POPS ").Append(total).Append(" (").Append(km > 0.01f ? (total / km).ToString("0.0", CultureInfo.InvariantCulture) + "/km, " : "")
              .Append((total * 60f / Mathf.Max(1f, segSeconds)).ToString("0.0", CultureInfo.InvariantCulture)).Append("/min)");
            for (int i = 0; i < (int)Cause.Count; i++)
                if (evCount[i] > 0)
                    sb.Append("\n      ").Append(CauseName[i]).Append(": ").Append(evCount[i])
                      .Append(km > 0.01f ? " (" + (evCount[i] / km).ToString("0.0", CultureInfo.InvariantCulture) + "/km)" : "")
                      .Append(evAfterCut[i] > 0 ? ", " + evAfterCut[i] + " within " + AfterCutFrames + " frames of a cut" : "")
                      .Append(", light ").Append(evLight[i].ToString("0", CultureInfo.InvariantCulture));
            string line = sb.ToString();
            summary.AppendLine(line);
            Write("pops.csv", csv.ToString());
            Write("summary.txt", summary.ToString());
            StreetLights.MeterOn = false; CarLights.MeterOn = false;
            return line;
        }

        public string Report => summary.ToString();

        static void Write(string name, string text)
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(OutDir, name), text); } catch (System.Exception) { }
        }

        // ------------------------------------------------------------------
        void LateUpdate()
        {
            if (!measuring) return;
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            EnsureTargets();
            var t = cam.transform;
            Vector3 eye = t.position, fwd = t.forward;
            bool cut = haveEye && ((eye - lastEye).sqrMagnitude > CutJumpM * CutJumpM ||
                                   Vector3.Dot(fwd, lastFwd) < Mathf.Cos(CutTurnDeg * Mathf.Deg2Rad));
            // a camcorder tape's own cut (a new clip or the other bystander), however near
            var cc = cam.GetComponent<CamcorderCam>();
            int shot = cc != null && cc.enabled ? cc.CurrentClip * 16 + cc.CurrentLens : -1;
            if (haveEye && shot != lastShot) cut = true;
            lastShot = shot;
            if (haveEye && !cut && carT != null) segDistM += (carT.position - lastCarPos).magnitude;
            else if (haveEye && !cut) segDistM += (eye - lastEye).magnitude;
            if (carT != null) lastCarPos = carT.position;
            lastEye = eye; lastFwd = fwd; haveEye = true;
            segSeconds += Time.deltaTime;
            segFrames++;
            if (cut) { segCuts++; framesSinceCut = 0; cutIndex++; afterCutSave = 3; }
            else framesSinceCut++;

            CollectRenderers();

            bool diffable = havePrev && !cut;
            // WARM-UP: the frame's first render advances the light hand-overs
            // (the hook) and carries the speed blur of the camera's move since
            // the last frame; every later render at this pose has none. So the
            // first is thrown away, and A and B are both later renders: they
            // differ by the lights put back and by nothing else.
            Render(cam, readA, ref pixA);
            // A: the frame as the game draws it
            Render(cam, readA, ref pixA);
            CaptureCur();
            // the meter's own noise: A again, every tenth frame
            if (segFrames % 10 == 0)
            {
                Render(cam, readB, ref pixB);
                Diff(pixA, pixB, out int selfPx, out _, out _, out _);
                selfFrames++;
                if (selfPx > 0) selfNoisy++;
                selfWorst = Mathf.Max(selfWorst, selfPx);
            }
            // B: the frame before's lights, at this pose
            if (diffable)
            {
                ApplyPrev(true, true, true, true, true);
                Render(cam, readB, ref pixB);
                RestoreAll();
            }

            if (diffable)
            {
                Diff(pixA, pixB, out int px, out double light, out float cx, out float cy);
                noiseFrames++;
                if (px == 0) noiseZero++;
                if (px >= MinPx)
                {
                    segPopFrames++;
                    // the frame before, whole, for the pair (the cause renders reuse pixB)
                    if (pixB0 == null || pixB0.Length != pixB.Length) pixB0 = new byte[pixB.Length];
                    System.Buffer.BlockCopy(pixB, 0, pixB0, 0, pixB.Length);
                    // which component: each put back alone
                    // A POP NEEDS A CAUSE: only a component whose state really
                    // moved this frame may be blamed (a lamp's or car's weight
                    // by MinWeightStep, the field's window or texture, the
                    // cones' arrays, a light renderer that came or went); a
                    // frame with none is the renderer's own noise, kept apart
                    bool eS = StreetMoved(), eF = prevFieldTex != curFieldTex || prevFieldST != curFieldST,
                         eH = HeadMoved(), eR = !curOn.SetEquals(prevOn), eC = ConesMoved();
                    Cause best = (eS || eF || eH || eR || eC) ? Cause.Mixed : Cause.Noise; int bestPx = 0;
                    if (eS) TryCause(cam, Cause.Street, ref best, ref bestPx);
                    if (eF) TryCause(cam, Cause.Field, ref best, ref bestPx);
                    if (eH) TryCause(cam, Cause.Head, ref best, ref bestPx);
                    if (eR) TryCause(cam, Cause.Halo, ref best, ref bestPx);
                    if (eC) TryCause(cam, Cause.Cones, ref best, ref bestPx);
                    if (best == Cause.Street && PointDominates()) best = Cause.Point;
                    if (best == Cause.Halo) best = RendererCause();
                    Note(best, px, light, cx, cy, eye);
                }
            }
            if (stripEvery > 0 && segFrames % stripEvery == 0) SaveFrame(pixA, "strip_" + (stripN++).ToString("000"));
            if (afterCutSave > 0) { SaveFrame(pixA, "cut" + cutIndex.ToString("00") + "_f" + (3 - afterCutSave)); afterCutSave--; }

            // this frame becomes the frame before
            Swap();
            havePrev = true;
        }

        void TryCause(Camera cam, Cause c, ref Cause best, ref int bestPx)
        {
            ApplyPrev(c == Cause.Street, c == Cause.Field, c == Cause.Head, c == Cause.Halo, c == Cause.Cones);
            Render(cam, readB, ref pixB);
            RestoreAll();
            Diff(pixA, pixB, out int px, out _, out _, out _);
            if (px > bestPx) { bestPx = px; best = c; }
        }

        bool StreetMoved()
        {
            foreach (var kv in curStreet)
                if (Mathf.Abs(kv.Value - (prevStreet.TryGetValue(kv.Key, out float p) ? p : 0f)) >= MinWeightStep) return true;
            foreach (var kv in prevStreet)
                if (!curStreet.ContainsKey(kv.Key) && kv.Value >= MinWeightStep) return true;
            return false;
        }

        bool HeadMoved()
        {
            foreach (var kv in curHead)
                if (Mathf.Abs(kv.Value - (prevHead.TryGetValue(kv.Key, out float p) ? p : 0f)) >= MinWeightStep) return true;
            foreach (var kv in prevHead)
                if (!curHead.ContainsKey(kv.Key) && kv.Value >= MinWeightStep) return true;
            return false;
        }

        bool ConesMoved()
        {
            if (curConeOn != prevConeOn) return true;
            for (int i = 0; i < curConeA.Length; i++)
                if ((curConeA[i] - prevConeA[i]).sqrMagnitude > 1e-8f || (curConeB[i] - prevConeB[i]).sqrMagnitude > 1e-10f) return true;
            return false;
        }

        bool PointDominates()
        {
            float ds = 0f, dp = 0f;
            foreach (var kv in curStreet) Acc(kv.Key, kv.Value, prevStreet.TryGetValue(kv.Key, out float p) ? p : 0f, ref ds, ref dp);
            foreach (var kv in prevStreet) if (!curStreet.ContainsKey(kv.Key)) Acc(kv.Key, 0f, kv.Value, ref ds, ref dp);
            return dp > ds;
        }

        static void Acc(int h, float now, float was, ref float ds, ref float dp)
        {
            if (!StreetLights.MeterLamp(h, out _, out var kind, out _)) return;
            if (kind == StreetLights.Kind.Point) dp += Mathf.Abs(now - was); else ds += Mathf.Abs(now - was);
        }

        Cause RendererCause()
        {
            foreach (var r in curOn) if (!prevOn.Contains(r) && rendCause.TryGetValue(r, out var c)) return c;
            foreach (var r in prevOn) if (!curOn.Contains(r) && rendCause.TryGetValue(r, out var c)) return c;
            return Cause.Halo;
        }

        void Note(Cause c, int px, double light, float cx, float cy, Vector3 eye)
        {
            int ci = (int)c;
            bool newEvent = segFrames - lastPopFrame[ci] > 2;
            lastPopFrame[ci] = segFrames;
            float dist = CulpritDistance(c, eye, out string detail);
            bool afterCut = framesSinceCut <= AfterCutFrames;
            csv.Append(seg).Append(',').Append(segFrames).Append(',').Append(segSeconds.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
               .Append(CauseName[ci]).Append(',').Append(px).Append(',').Append(light.ToString("0", CultureInfo.InvariantCulture)).Append(',')
               .Append(cx.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(cy.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
               .Append(dist.ToString("0", CultureInfo.InvariantCulture)).Append(',').Append(afterCut ? 1 : 0).Append(',').Append(detail).Append('\n');
            if (newEvent)
            {
                evCount[ci]++;
                if (afterCut) evAfterCut[ci]++;
            }
            evLight[ci] += light;
            // the worst frame pairs of each cause
            var list = pairs[ci];
            float score = (float)light;
            if (list.Count < PairsPerCause || score > list[list.Count - 1].score)
            {
                string file = "pair_" + c.ToString().ToLowerInvariant() + "_" + segFrames.ToString("00000");
                SavePair(file, c, px, dist);
                list.Add((score, file));
                list.Sort((a, b) => b.score.CompareTo(a.score));
                if (list.Count > PairsPerCause)
                {
                    try { System.IO.File.Delete(System.IO.Path.Combine(OutDir, seg, list[list.Count - 1].file + ".png")); } catch (System.Exception) { }
                    try { System.IO.File.Delete(System.IO.Path.Combine(OutDir, seg, list[list.Count - 1].file + ".txt")); } catch (System.Exception) { }
                    list.RemoveAt(list.Count - 1);
                }
            }
        }

        float CulpritDistance(Cause c, Vector3 eye, out string detail)
        {
            detail = "";
            if (c == Cause.Street || c == Cause.Point)
            {
                float best = 0f, bd = -1f; int bh = 0; float was = 0f, now = 0f;
                foreach (var kv in curStreet) Pick(kv.Key, kv.Value, prevStreet.TryGetValue(kv.Key, out float p) ? p : 0f, eye, ref best, ref bd, ref bh, ref was, ref now);
                foreach (var kv in prevStreet) if (!curStreet.ContainsKey(kv.Key)) Pick(kv.Key, 0f, kv.Value, eye, ref best, ref bd, ref bh, ref was, ref now);
                detail = "lamp " + bh + " weight " + was.ToString("0.00", CultureInfo.InvariantCulture) + "->" + now.ToString("0.00", CultureInfo.InvariantCulture);
                return bd;
            }
            if (c == Cause.Head)
            {
                float best = 0f, bd = -1f; float was = 0f, now = 0f;
                foreach (var l in CarLights.MeterAll)
                {
                    if (l == null) continue;
                    int id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(l);
                    float a = prevHead.TryGetValue(id, out float p) ? p : 0f, b = curHead.TryGetValue(id, out float q) ? q : 0f;
                    if (Mathf.Abs(b - a) > best) { best = Mathf.Abs(b - a); bd = (l.transform.position - eye).magnitude; was = a; now = b; }
                }
                detail = "car weight " + was.ToString("0.00", CultureInfo.InvariantCulture) + "->" + now.ToString("0.00", CultureInfo.InvariantCulture);
                return bd;
            }
            if (c == Cause.Field) { detail = "field " + prevFieldState + " -> " + curFieldState; return -1f; }
            if (c == Cause.Cones) { detail = "cone arrays"; return -1f; }
            // a renderer that came or went
            foreach (var r in curOn)
                if (!prevOn.Contains(r) && r != null) { detail = "came: " + r.name; return Vector3.Distance(r.bounds.center, eye); }
            foreach (var r in prevOn)
                if (!curOn.Contains(r) && r != null) { detail = "went: " + r.name; return Vector3.Distance(r.bounds.center, eye); }
            return -1f;
        }

        static void Pick(int h, float now, float was, Vector3 eye, ref float best, ref float bd, ref int bh, ref float bwas, ref float bnow)
        {
            float d = Mathf.Abs(now - was);
            if (d <= best || !StreetLights.MeterLamp(h, out var pos, out _, out _)) return;
            best = d; bd = (pos - eye).magnitude; bh = h & 0xFFFF; bwas = was; bnow = now;
        }

        // ------------------------------------------------------------------
        void CollectRenderers()
        {
            lightRends.Clear();
            foreach (var g in NightGlow.MeterAll)
            {
                if (g == null) continue;
                var r = g.MeterHalo;
                if (r != null) { lightRends.Add(r); rendCause[r] = Cause.Halo; }
            }
            foreach (var l in CarLights.MeterAll)
            {
                if (l == null) continue;
                for (int i = 0; i < 2; i++)
                {
                    var a = l.MeterLens(i, true); if (a != null) { lightRends.Add(a); rendCause[a] = Cause.Lens; }
                    var b = l.MeterLens(i, false); if (b != null) { lightRends.Add(b); rendCause[b] = Cause.Lens; }
                    var c = l.MeterBeam(i); if (c != null) { lightRends.Add(c); rendCause[c] = Cause.Beam; }
                }
            }
            if (rendCause.Count > 20000) rendCause.Clear();
        }

        static bool On(Renderer r) => r != null && r.enabled && r.gameObject.activeInHierarchy;

        void CaptureCur()
        {
            curStreet.Clear();
            foreach (var kv in StreetLights.MeterRank) curStreet[kv.Key] = kv.Value;
            curHead.Clear();
            foreach (var kv in CarLights.MeterRank) curHead[kv.Key] = kv.Value;
            curFieldTex = Shader.GetGlobalTexture(FieldTexId);
            curFieldST = Shader.GetGlobalVector(FieldSTId);
            curFieldState = StreetLights.MeterFieldState;
            curFieldY = Shader.GetGlobalVector(FieldYId);
            var m = LampCones.MeterMat;
            var mr = LampCones.MeterRenderer;
            curConeOn = mr != null && mr.enabled;
            if (m != null)
            {
                var a = m.GetVectorArray(ConeAId); var b = m.GetVectorArray(ConeBId);
                for (int i = 0; i < curConeA.Length; i++)
                {
                    curConeA[i] = a != null && i < a.Length ? a[i] : Vector4.zero;
                    curConeB[i] = b != null && i < b.Length ? b[i] : Vector4.zero;
                }
            }
            curOn.Clear();
            foreach (var r in lightRends) if (On(r)) curOn.Add(r);
        }

        void Swap()
        {
            prevStreet.Clear(); foreach (var kv in curStreet) prevStreet[kv.Key] = kv.Value;
            prevHead.Clear(); foreach (var kv in curHead) prevHead[kv.Key] = kv.Value;
            prevFieldTex = curFieldTex; prevFieldST = curFieldST; prevFieldState = curFieldState; prevFieldY = curFieldY;
            System.Array.Copy(curConeA, prevConeA, curConeA.Length);
            System.Array.Copy(curConeB, prevConeB, curConeB.Length);
            prevConeOn = curConeOn;
            var tmp = prevOn; prevOn = curOn; curOn = tmp;
        }

        // the state put back for a B render
        bool heldStreet, heldHead, heldCones;
        Texture saveFieldTex; Vector4 saveFieldST;
        Vector4[] saveConeA, saveConeB; bool saveConeOn;

        void ApplyPrev(bool street, bool field, bool head, bool rends, bool cones)
        {
            var eye = Camera.main.transform.position;
            // the lamp table: re-emitted at last frame's weights (or today's,
            // when only another component is put back), held against the hook
            StreetLights.MeterEmit(street ? prevStreet : StreetLights.MeterRank, eye);
            StreetLights.MeterHold = true; heldStreet = true;
            if (field)
            {
                saveFieldTex = Shader.GetGlobalTexture(FieldTexId); saveFieldST = Shader.GetGlobalVector(FieldSTId);
                saveFieldY = Shader.GetGlobalVector(FieldYId);
                if (prevFieldTex != null) Shader.SetGlobalTexture(FieldTexId, prevFieldTex);
                Shader.SetGlobalVector(FieldSTId, prevFieldST);
                Shader.SetGlobalVector(FieldYId, prevFieldY);
            }
            else saveFieldTex = null;
            if (head) { CarLights.MeterPush(prevHead); heldHead = true; }
            if (cones)
            {
                var m = LampCones.MeterMat; var mr = LampCones.MeterRenderer;
                if (m != null && mr != null)
                {
                    saveConeA = (Vector4[])curConeA.Clone(); saveConeB = (Vector4[])curConeB.Clone(); saveConeOn = mr.enabled;
                    m.SetVectorArray(ConeAId, prevConeA); m.SetVectorArray(ConeBId, prevConeB);
                    mr.enabled = prevConeOn;
                    heldCones = true;
                }
            }
            undo.Clear();
            if (rends)
                foreach (var r in lightRends)
                {
                    if (r == null || !r.gameObject.activeInHierarchy) continue;
                    bool want = prevOn.Contains(r);
                    if (r.enabled != want) { undo.Add((r, r.enabled)); r.enabled = want; }
                }
        }

        void RestoreAll()
        {
            if (heldStreet) { StreetLights.MeterHold = false; heldStreet = false; StreetLights.MeterEmit(StreetLights.MeterRank, Camera.main.transform.position); }
            if (saveFieldTex != null) { Shader.SetGlobalTexture(FieldTexId, saveFieldTex); Shader.SetGlobalVector(FieldSTId, saveFieldST); Shader.SetGlobalVector(FieldYId, saveFieldY); saveFieldTex = null; }
            if (heldHead) { CarLights.MeterPush(null); heldHead = false; }
            if (heldCones)
            {
                var m = LampCones.MeterMat; var mr = LampCones.MeterRenderer;
                if (m != null) { m.SetVectorArray(ConeAId, saveConeA); m.SetVectorArray(ConeBId, saveConeB); }
                if (mr != null) mr.enabled = saveConeOn;
                heldCones = false;
            }
            foreach (var (r, was) in undo) if (r != null) r.enabled = was;
            undo.Clear();
        }

        // ------------------------------------------------------------------
        void EnsureTargets()
        {
            if (rt != null) return;
            rt = new RenderTexture(W, H, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            rtShown = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rtShown.Create();
            // THE PICTURE THE PLAYER GETS: the camera's buffer through the
            // scene's own PSX/Blit (exposure, grade, halation, dither), as the
            // screenshot tools show it - a raw render is not what the eye sees
            var output = FindFirstObjectByType<PSXCameraOutput>();
            var m = output != null && output.display != null ? output.display.material : null;
            if (m != null && m.HasProperty("_ColorDepth"))
            {
                shownMat = new Material(m) { hideFlags = HideFlags.HideAndDontSave };
                shownMat.SetFloat("_ColorDepth", PSXQuality.ColorDepth);
                shownMat.SetFloat("_DitherStrength", PSXQuality.Dither);
                if (shownMat.HasProperty("_Grade")) shownMat.SetFloat("_Grade", 1f);
            }
            readA = new Texture2D(W, H, TextureFormat.RGBA32, false);
            readB = new Texture2D(W, H, TextureFormat.RGBA32, false);
        }

        void Render(Camera cam, Texture2D into, ref byte[] pix)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, request)) return;
            RenderPipeline.SubmitRenderRequest(cam, request);
            var src = rt;
            if (shownMat != null)
            {
                var glow = Halation.Wanted(shownMat) ? Halation.BuildNow(rt) : null;
                Halation.Apply(shownMat, glow);
                Graphics.Blit(rt, rtShown, shownMat);
                Halation.Release(ref glow);
                src = rtShown;
            }
            var prev = RenderTexture.active;
            RenderTexture.active = src;
            into.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            RenderTexture.active = prev;
            var raw = into.GetRawTextureData<byte>();
            if (pix == null || pix.Length != raw.Length) pix = new byte[raw.Length];
            raw.CopyTo(pix);
        }

        void Diff(byte[] a, byte[] b, out int px, out double light, out float cx, out float cy)
        {
            px = 0; light = 0; cx = cy = 0f;
            if (a == null || b == null || a.Length != b.Length) return;
            double sx = 0, sy = 0;
            for (int i = 0, p = 0; i < a.Length; i += 4, p++)
            {
                int dr = a[i] - b[i], dg = a[i + 1] - b[i + 1], db = a[i + 2] - b[i + 2];
                int d = Mathf.Max(Mathf.Abs(dr), Mathf.Max(Mathf.Abs(dg), Mathf.Abs(db)));
                if (d < MinDelta) continue;
                int top = Mathf.Max(Mathf.Max(a[i], a[i + 1]), Mathf.Max(a[i + 2], Mathf.Max(b[i], Mathf.Max(b[i + 1], b[i + 2]))));
                if (d < MinRel * top) continue;
                px++; light += d;
                sx += (p % W) * d; sy += (p / W) * d;
            }
            if (light > 0) { cx = (float)(sx / light / W); cy = 1f - (float)(sy / light / H); }
        }

        void SaveFrame(byte[] pix, string name)
        {
            if (pix == null) return;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(pix); tex.Apply(false);
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(OutDir, seg, name + ".png"), tex.EncodeToPNG()); } catch (System.Exception) { }
            Destroy(tex);
        }

        /// <summary>B | A | the change x4, side by side, with a label strip.</summary>
        void SavePair(string file, Cause c, int px, float dist)
        {
            var tex = new Texture2D(W * 3, H, TextureFormat.RGBA32, false);
            var o = new Color32[W * 3 * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = (y * W + x) * 4;
                    var b = new Color32(pixB0[i], pixB0[i + 1], pixB0[i + 2], 255);
                    var a = new Color32(pixA[i], pixA[i + 1], pixA[i + 2], 255);
                    int d = Mathf.Min(255, 4 * Mathf.Max(Mathf.Abs(pixA[i] - pixB0[i]), Mathf.Max(Mathf.Abs(pixA[i + 1] - pixB0[i + 1]), Mathf.Abs(pixA[i + 2] - pixB0[i + 2]))));
                    o[y * W * 3 + x] = b;
                    o[y * W * 3 + W + x] = a;
                    o[y * W * 3 + 2 * W + x] = new Color32((byte)d, (byte)d, (byte)d, 255);
                }
            tex.SetPixels32(o); tex.Apply(false);
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(OutDir, seg, file + ".png"), tex.EncodeToPNG()); } catch (System.Exception) { }
            Destroy(tex);
            // the pair's facts beside it
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(OutDir, seg, file + ".txt"),
                    seg + " frame " + segFrames + " t " + segSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s: " + CauseName[(int)c] +
                    ", " + px + " px changed, culprit " + dist.ToString("0", CultureInfo.InvariantCulture) + " m" +
                    (framesSinceCut <= AfterCutFrames ? ", " + framesSinceCut + " frames after a cut" : "") +
                    "\nleft: the frame before's lights at this pose; middle: this frame; right: the change x4\n");
            }
            catch (System.Exception) { }
        }
    }
}
