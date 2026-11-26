using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// CARPARTS STAGE 4: THE ENGINE IN THE BAY (decision 10). A car whose shell
    /// opens (CarParts.Allowed) gets its engine and the shared bay kit LOADED
    /// when it enters a place where its lid can open - its garage bay, the
    /// driveway or a meet lot on foot, the showroom, the inspection page, the
    /// player's car slowing under 15 km/h in free roam (<see cref="Preload"/>)
    /// - so the engine is ready before the lid can move. A lid never rises onto
    /// an empty bay: a press that comes first makes the lid WAIT (CarParts),
    /// and the wait is logged (<see cref="LongestWait"/>). The engine and the
    /// kit draw only while the hood is off its seat (zero pop-in: at 0 degrees
    /// the shut hood covers them); one draw each, assembled into one mesh a car
    /// at load (<see cref="Assemble"/>).
    ///
    /// PLACEMENT. The engine root stands at the car's Engine_Mount (or the
    /// per-car table's) as the fitted body carries it (the position through the
    /// body scale S, like a hinge pivot), with S cancelled (no shear), at one
    /// uniform scale: the table's (default 1.0), brought down to the mount's
    /// clear box where it would not fit, never below 0.92 (a car still too
    /// small is listed, <see cref="FitFails"/>). THE MIRROR: the car's
    /// downpipe side and the model's exhaust side are both in the imported
    /// frame (CarModelDef.hasEngineMount); a one-sided engine whose exhaust is
    /// on the other side is reflected (x -> -x); a V or flat engine never is
    /// for its exhaust, only by the table's hand. The kit: each Bay_ item at
    /// the car's Kit_ empty, else at the kit's default offset from the engine
    /// origin for the car's layout.
    ///
    /// The loader is one function (<see cref="EngineAssets"/>) so the real
    /// engines can move to an on-demand bundle like cockpit.bundle.
    /// </summary>
    public class CarEngineBay : MonoBehaviour
    {
        // =====================================================================
        //  The assets: one loader, a use count each
        // =====================================================================
        public static class EngineAssets
        {
            public const string Dir = "EngineModels/";
            static readonly Dictionary<string, EngineKitDef> loaded = new Dictionary<string, EngineKitDef>();
            static readonly Dictionary<string, ResourceRequest> pending = new Dictionary<string, ResourceRequest>();
            static readonly Dictionary<string, int> pendingFrom = new Dictionary<string, int>();
            /// <summary>Tests: hold a load this many frames past its start (the
            /// editor loads at once; a phone reading from storage does not).</summary>
            public static int TestDelayFrames;
            static readonly Dictionary<string, int> users = new Dictionary<string, int>();
            public static int Loads { get; private set; }

            /// <summary>The asset if it is in, else starts its load (async in play;
            /// at once in the editor's tools) and returns null.</summary>
            public static EngineKitDef Get(string key)
            {
                if (loaded.TryGetValue(key, out var d) && d != null) return d;
                if (!Application.isPlaying)
                {
                    var go = Resources.Load<GameObject>(Dir + key);
                    d = go != null ? go.GetComponent<EngineKitDef>() : null;
                    if (d != null) { loaded[key] = d; Loads++; }
                    return d;
                }
                if (!pending.TryGetValue(key, out var rq))
                {
                    rq = Resources.LoadAsync<GameObject>(Dir + key);
                    pending[key] = rq;
                    pendingFrom[key] = Time.frameCount;
                }
                if (!rq.isDone || Time.frameCount < pendingFrom[key] + TestDelayFrames) return null;
                pending.Remove(key);
                var g = rq.asset as GameObject;
                d = g != null ? g.GetComponent<EngineKitDef>() : null;
                if (d != null) { loaded[key] = d; Loads++; }
                return d;
            }

            public static void Use(string key, int delta)
            {
                users.TryGetValue(key, out int n);
                n = Mathf.Max(0, n + delta);
                users[key] = n;
                // the last user gone: the reference goes (the asset unloads with the next sweep)
                if (n == 0) loaded.Remove(key);
            }

            public static int Users(string key) { users.TryGetValue(key, out int n); return n; }
            public static int Cached => loaded.Count;
        }

        // =====================================================================
        //  Static API
        // =====================================================================

        /// <summary>The catalog row a body wears, where the place knows it
        /// (CarShell spawns, the viewer, CarBody): the engine's shape.</summary>
        static readonly Dictionary<MeshFilter, CarSpec> specs = new Dictionary<MeshFilter, CarSpec>();
        public static void Note(MeshFilter body, CarSpec spec)
        {
            if (body == null || spec == null) return;
            if (specs.Count > 64)
            {
                var gone = new List<MeshFilter>();
                foreach (var k in specs.Keys) if (k == null) gone.Add(k);
                foreach (var k in gone) specs.Remove(k);
            }
            specs[body] = spec;
        }

        /// <summary>The longest a lid waited for its engine, seconds (the hand-back's number).</summary>
        public static float LongestWait;
        public static int Waits;
        /// <summary>Cars whose engine would not fit their clear box even at 0.92.</summary>
        public static readonly List<string> FitFails = new List<string>();
        static readonly List<CarEngineBay> live = new List<CarEngineBay>();
        public static int LiveCount { get { live.RemoveAll(b => b == null); return live.Count; } }

        public static CarEngineBay Of(MeshFilter body) => body != null ? body.GetComponent<CarEngineBay>() : null;

        /// <summary>Load this car's engine and kit now (a place where its lid can
        /// open). Nothing for a car without parts or without a mount.</summary>
        public static CarEngineBay Preload(MeshFilter body, CarModelDef def, CarSpec spec = null)
        {
            if (body == null || !CarParts.Allowed(def, body)) return null;
            var pdef = CarParts.PartsDef(def);
            if (pdef == null || !pdef.hasEngineMount) return null;
            if ((CarParts.Available(def) & CarParts.Hood) == 0) return null;
            if (spec == null) specs.TryGetValue(body, out spec);
            var bay = Of(body);
            if (bay != null && bay.pdef == pdef) return bay;
            if (bay != null) Release(body);
            bay = body.gameObject.AddComponent<CarEngineBay>();
            bay.Init(body, def, pdef, spec);
            return bay;
        }

        /// <summary>Let go of this car's engine (it left the place, or a re-dress).</summary>
        public static void Release(MeshFilter body)
        {
            var bay = Of(body);
            if (bay == null) return;
            bay.Dispose();
            if (Application.isPlaying) Destroy(bay); else DestroyImmediate(bay);
        }

        // =====================================================================
        //  One car
        // =====================================================================

        MeshFilter body;
        CarModelDef def, pdef;
        CarSpec spec;
        public EngineLibrary.Choice Choice { get; private set; }
        public bool Reflected { get; private set; }
        public float Scale { get; private set; } = 1f;
        public bool Ready { get; private set; }
        public string Why { get; private set; } = "";
        Transform node;
        MeshFilter engineF, kitF;
        MeshRenderer engineR, kitR;
        Vector3 solvedS = new Vector3(float.NaN, 0f, 0f);
        EngineKitDef engine, kit;
        bool usedEngine, usedKit, disposed, shown;
        float requestedAt = -1f;
        /// <summary>The pieces in the assembled engine (names, for the fault markers).</summary>
        public readonly List<string> EnginePieces = new List<string>();
        public readonly List<string> KitPieces = new List<string>();
        public Renderer EngineRenderer => engineR;
        public Renderer KitRenderer => kitR;
        public Transform Node => node;
        /// <summary>Tests: ignore the reflection rule (proves the mirror test can fail).</summary>
        public static bool NoReflect;
        /// <summary>Tests and frames: every kit item at its default offset (the second placement path).</summary>
        public static bool IgnoreKitEmpties;

        void Init(MeshFilter b, CarModelDef d, CarModelDef pd, CarSpec s)
        {
            body = b; def = d; pdef = pd; spec = s;
            Choice = EngineLibrary.Choose(spec, pdef);
            live.Add(this);
            TryAssemble();
        }

        void Update()
        {
            if (disposed) return;
            if (!Ready) TryAssemble();
            if (Ready && body != null && body.transform.localScale != solvedS) Place();
        }

        /// <summary>Called by CarParts while a hood waits: ready yet? (and the wait logged)</summary>
        public bool ReadyForLid()
        {
            if (!Ready)
            {
                if (requestedAt < 0f) requestedAt = Time.realtimeSinceStartup;
                TryAssemble();
                if (!Ready) return false;
            }
            if (requestedAt >= 0f)
            {
                // the lid waited from its first ask to now
                float w = Time.realtimeSinceStartup - requestedAt;
                requestedAt = -1f;
                Waits++;
                if (w > LongestWait) LongestWait = w;
                Debug.Log($"[CarEngineBay] a lid waited {w * 1000f:0} ms for its engine ({Choice.key})");
            }
            return true;
        }

        void TryAssemble()
        {
            if (Ready || disposed) return;
            if (engine == null) { engine = EngineAssets.Get(Choice.key); if (engine != null && !usedEngine) { EngineAssets.Use(Choice.key, 1); usedEngine = true; } }
            if (kit == null) { kit = EngineAssets.Get(EngineLibrary.BayKit); if (kit != null && !usedKit) { EngineAssets.Use(EngineLibrary.BayKit, 1); usedKit = true; } }
            if (engine == null || kit == null) return;
            Assemble();
            Ready = true;
        }

        bool Wanted(EngineKitDef.Piece p)
        {
            var c = Choice;
            switch (p.option)
            {
                case null: case "": return true;
                case "gearbox_inline": return c.gearbox == "Eng_Gearbox_Inline";
                case "gearbox_transaxle": return c.gearbox == "Eng_Gearbox_Transaxle";
                case "turbo1": return c.turbos >= 1;
                case "turbo2": return c.turbos >= 2;
                case "sc": return c.supercharger;
                case "fanclutch": return !c.electricFan;
                case "ohc": return !c.pushrod;
                case "pushrod": return c.pushrod;
                case "fan": return c.electricFan;
                case "ic": return c.intercooler;
                case "ictop": return c.intercoolerTop;
                default: return false;   // engineout: the junkyard's
            }
        }

        /// <summary>The car's engine (and kit) as one mesh each, the pieces it
        /// needs, the reflection and the fit decided.</summary>
        void Assemble()
        {
            bool hd = PlatformProfile.PC;
            // ---- the mirror ------------------------------------------------
            EngineLibrary.Table.TryGetValue(spec != null ? spec.id : "", out var row);
            if (row == null) EngineLibrary.Table.TryGetValue(pdef.key, out row);
            bool handReflect = row != null && row.reflect;
            int carSide = pdef.engineDownpipeSide;
            Reflected = !NoReflect && (engine.exhaustSide != 0 && carSide != 0 ? engine.exhaustSide != carSide : false) ^ handReflect;
            // ---- the fit ------------------------------------------------------
            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn;
            var parts = new List<CombineInstance>();
            EnginePieces.Clear();
            foreach (var p in engine.pieces)
            {
                if (!Wanted(p)) continue;
                var m = hd ? p.hd : p.low;
                if (m == null) continue;
                parts.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
                EnginePieces.Add(p.name);
                if (p.inFit) { mn = Vector3.Min(mn, p.min); mx = Vector3.Max(mx, p.max); }
            }
            if (Reflected) { float a = -mx.x, b = -mn.x; mn.x = a; mx.x = b; }
            float s = Choice.scale;
            var cb = pdef.engineClearBox;
            Why = "";
            if (cb != null && cb.Length == 6)
            {
                // front +Y, back -Y, left = the source's -X = imported +X, right = imported -X, up +Z, down -Z
                float[] need = { mx.y, -mn.y, mx.x, -mn.x, mx.z, -mn.z };
                for (int i = 0; i < 6; i++) if (need[i] > 1e-4f && cb[i] > 0f) s = Mathf.Min(s, cb[i] / need[i]);
                if (s < EngineLibrary.ScaleMin)
                {
                    Why = $"needs x{s:0.000} to fit its clear box, held at {EngineLibrary.ScaleMin}";
                    string id = (spec != null ? spec.id : pdef.key) + " (" + Choice.key + ")";
                    if (!FitFails.Contains(id)) FitFails.Add(id);
                    s = EngineLibrary.ScaleMin;
                }
            }
            Scale = Mathf.Clamp(s, EngineLibrary.ScaleMin, EngineLibrary.ScaleMax);
            var em = Combine(parts, "EngineBay_" + Choice.key, Reflected);
            // ---- the node and the renderers -----------------------------------
            var go = new GameObject("EngineBay");
            go.layer = body.gameObject.layer;
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            node = go.transform;
            node.SetParent(body.transform, false);
            engineF = NewRenderer("Engine", engine.material, out engineR);
            engineF.sharedMesh = em;
            kitF = NewRenderer("BayKit", kit.material, out kitR);
            Place();
            Show(shown);
        }

        MeshFilter NewRenderer(string name, Material mat, out MeshRenderer r)
        {
            var g = new GameObject(name);
            g.layer = node.gameObject.layer;
            if (!Application.isPlaying) g.hideFlags = HideFlags.DontSave;
            g.transform.SetParent(node, false);
            var f = g.AddComponent<MeshFilter>();
            r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            // decision 14: no shadows from engines unless the PC tier's
            r.shadowCastingMode = PlatformProfile.PCShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var br = body.GetComponent<MeshRenderer>();
            if (br != null) r.renderingLayerMask = br.renderingLayerMask;
            return f;
        }

        static Mesh Combine(List<CombineInstance> parts, string name, bool reflect)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            if (parts.Count > 0) m.CombineMeshes(parts.ToArray(), true, true);
            if (reflect)
            {
                var v = m.vertices;
                for (int i = 0; i < v.Length; i++) v[i].x = -v[i].x;
                m.vertices = v;
                var t = m.triangles;
                for (int i = 0; i < t.Length; i += 3) { int a = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = a; }
                m.triangles = t;
                m.RecalculateNormals();
            }
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The engine at the mount as the fitted body carries it, S
        /// cancelled; the kit at its seats (rebuilt: its seats go through S).</summary>
        void Place()
        {
            var S = body.transform.localScale;
            solvedS = S;
            node.localPosition = Vector3.zero;
            node.localRotation = Quaternion.identity;
            node.localScale = new Vector3(1f / S.x, 1f / S.y, 1f / S.z);
            var et = engineF.transform;
            et.localPosition = Vector3.Scale(S, pdef.engineMountPos);
            et.localRotation = pdef.engineMountRot;
            et.localScale = Vector3.one * Scale;
            // the kit, in the node's (unscaled, body-aligned) frame
            bool hd = PlatformProfile.PC;
            var carRot = Quaternion.Inverse(Quaternion.Euler(0f, pdef.bodyYaw, 0f));   // car frame -> mesh frame
            var parts = new List<CombineInstance>();
            KitPieces.Clear();
            foreach (var p in kit.pieces)
            {
                if (!Wanted(p)) continue;
                var m = hd ? p.hd : p.low;
                if (m == null) continue;
                string empty = "Kit_" + p.name.Substring(4);   // Bay_Battery -> Kit_Battery
                Vector3 pos; Quaternion rot;
                int at = -1;
                if (pdef.kitMounts != null && p.name != "Bay_Intercooler_Top" && !IgnoreKitEmpties)
                    for (int i = 0; i < pdef.kitMounts.Length; i++) if (pdef.kitMounts[i].name == empty) at = i;
                if (at >= 0) { pos = Vector3.Scale(S, pdef.kitMounts[at].pos); rot = pdef.kitMounts[at].rot * carRot; }
                else
                {
                    var d = Choice.layout == EngineLayout.Transverse ? p.defaultTransverse : p.defaultLongitudinal;
                    pos = Vector3.Scale(S, pdef.engineMountPos + carRot * d);
                    rot = carRot;
                }
                parts.Add(new CombineInstance { mesh = m, transform = Matrix4x4.TRS(pos, rot, Vector3.one) });
                KitPieces.Add(p.name + (at >= 0 ? " @" + empty : " @default"));
            }
            if (kitF.sharedMesh != null) Kill(kitF.sharedMesh);
            kitF.sharedMesh = Combine(parts, "BayKit", false);
        }

        /// <summary>Draw or hide the engine and kit (CarParts: on as the hood
        /// leaves its seat, off when it is back on it).</summary>
        public void Show(bool on)
        {
            shown = on;
            if (engineR != null) engineR.enabled = on;
            if (kitR != null) kitR.enabled = on;
        }

        public bool Shown => shown && Ready;

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void Dispose()
        {
            if (disposed) return;
            disposed = true;
            live.Remove(this);
            if (engineF != null) Kill(engineF.sharedMesh);
            if (kitF != null) Kill(kitF.sharedMesh);
            if (node != null) Kill(node.gameObject);
            if (usedEngine) EngineAssets.Use(Choice.key, -1);
            if (usedKit) EngineAssets.Use(EngineLibrary.BayKit, -1);
            usedEngine = usedKit = false;
            Ready = false;
        }

        void OnDestroy() => Dispose();
    }
}
