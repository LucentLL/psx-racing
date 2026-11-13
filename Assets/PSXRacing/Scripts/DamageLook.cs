using System.Collections.Generic;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// R1a PART 2a "THE CAR SHOWS THE HIT" (2026-11-13). What the career car's
    /// damage rows look like, read every fifth of a second from the rows the car
    /// came out with (OwnedCar.faults, hidden or found) and the rows this drive's
    /// incidents wrote so far (DamageLedger.incidents) - nothing new is saved:
    ///   LAMPS  a lamp_out row puts that lamp out: the headlamp (front) or tail
    ///          lamp (rear) on the row's side gives no light, no beam and no lens
    ///          sprite (CarLights.SetLampOut), and on a lamp-map body its lens goes
    ///          dark and its glow out (PSX/CarPaint _PSXLampOut). No label.
    ///   BODY   panel_damage per zone and side (stage 1 a scrape / cracked cover,
    ///          2 a dent, 3 crumpled), bumper_bar (hidden: it only deepens a zone
    ///          whose cover already shows), roof_glass (1 cracked glass, 2 a dented
    ///          roof, 3 crushed): per renderer on PSX/CarPaint (PSXCarDamage.cginc),
    ///          MaterialPropertyBlock, the PaintGloss pattern - never on AI or traffic.
    ///   STEAM  a radiator_leak row with a hot engine: wisps from under the bonnet
    ///          (a weep), a hard plume (a holed core). One small ParticleSystem on
    ///          this car, made the first time it applies and emitting only while it does.
    ///   SRS    a srs_deployed row (SrsBag below): a limp bag on the steering wheel
    ///          and, on a dual-bag car, one over the passenger's dash, in the cockpit.
    /// Switches: PSX_FAULT_LOOK=0 = none of it (the base); PSX_FAULT_LAMPS=0,
    /// PSX_FAULT_BODY=0, PSX_FAULT_STEAM=0 one look each; PSX_FAULT_SRS=0 = no bag
    /// fires (no row, no look); PSX_FAULT_CRUSH=0 = no crush-order rows (SrsBag.CrushOrder).
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class DamageLook : MonoBehaviour
    {
        static bool Env(string k) => System.Environment.GetEnvironmentVariable(k) != "0";
        public static bool On = Env("PSX_FAULT_LOOK");
        public static bool LampsOn = Env("PSX_FAULT_LAMPS");
        public static bool BodyOn = Env("PSX_FAULT_BODY");
        public static bool SteamOn = Env("PSX_FAULT_STEAM");
        public static bool SrsOn = Env("PSX_FAULT_SRS");

        /// <summary>Steam: a weeping core from this hot, a holed one from this.</summary>
        public const float WeepSteamC = 98f, HoleSteamC = 85f;

        CarController car;
        CarBody body;
        CarLights lights;
        EngineTemp temp;
        DamageLedger ledger;
        string carId = "";
        readonly List<CarFault> carried = new List<CarFault>();

        /// <summary>What the car shows now (absolute, rebuilt each read).</summary>
        public struct Look
        {
            /// <summary>Front, rear, left, right: 0 clean .. 1 crumpled.</summary>
            public Vector4 zones;
            public float roof, biasF, biasR;
            /// <summary>Bits: 1 head L, 2 head R, 4 tail L, 8 tail R.</summary>
            public int lampOut;
            public int bags, radStage;
            public bool Any => zones != Vector4.zero || roof > 0f || lampOut != 0;
        }
        public Look Now;
        Look pushed;
        bool pushedOnce;
        float nextRead, nextPush;
        readonly List<Renderer> rends = new List<Renderer>();
        static MaterialPropertyBlock block;

        /// <summary>The career car's look, armed with its ledger (DamageLedger.Arm).</summary>
        public static void Attach(CarController car, DamageLedger L)
        {
            if (!On || car == null) return;
            var look = car.GetComponent<DamageLook>();
            if (look == null) look = car.gameObject.AddComponent<DamageLook>();
            look.Bind(car, L);
        }

        void Bind(CarController c, DamageLedger L)
        {
            car = c;
            ledger = L;
            body = c.GetComponent<CarBody>();
            lights = c.GetComponentInChildren<CarLights>(true);
            temp = c.GetComponent<EngineTemp>();
            carId = L != null ? L.CarId : "";
            carried.Clear();
            var s = LifeSimManager.State;
            var owned = s != null && !string.IsNullOrEmpty(carId) ? s.FindCar(carId) : null;
            if (owned != null && owned.faults != null)
                foreach (var f in owned.faults) if (f != null) carried.Add(f);
            nextRead = 0f;
            pushedOnce = false;
        }

        // ================= the rows -> the look =================
        static void Fold(ref Look k, string id, int stage, int zone, int side, int corner, float mag)
        {
            float a = Mathf.Clamp01(stage / 3f);
            if (id == DamageModel.PanelId)
            {
                int z = zone;
                if (zone == DamageModel.ZCorner && corner >= 0) { z = corner < 2 ? DamageModel.ZFront : DamageModel.ZRear; side = corner % 2 == 0 ? -1 : 1; }
                if (z == DamageModel.ZFront) { if (a >= k.zones.x) k.biasF = side; k.zones.x = Mathf.Max(k.zones.x, a); }
                else if (z == DamageModel.ZRear) { if (a >= k.zones.y) k.biasR = side; k.zones.y = Mathf.Max(k.zones.y, a); }
                else if (z == DamageModel.ZSide) { if (side <= 0) k.zones.z = Mathf.Max(k.zones.z, a); if (side >= 0) k.zones.w = Mathf.Max(k.zones.w, a); }
                else if (z == DamageModel.ZRoof) k.roof = Mathf.Max(k.roof, a);
            }
            else if (id == DamageModel.GlassId) k.roof = Mathf.Max(k.roof, a);
            else if (id == DamageModel.LampId)
            {
                int bit = (zone == DamageModel.ZRear ? 4 : 1) << (side > 0 ? 1 : 0);
                if (zone == DamageModel.ZFront || zone == DamageModel.ZRear) k.lampOut |= bit;
            }
            else if (id == DamageModel.RadiatorId) k.radStage = Mathf.Max(k.radStage, stage);
            else if (id == SrsBag.Id) k.bags = Mathf.Max(k.bags, Mathf.Max(1, Mathf.RoundToInt(mag)));
        }

        /// <summary>The look of a set of rows: the car's own, then this drive's.</summary>
        public static Look Read(List<CarFault> rows, DamageLedger L)
        {
            var k = new Look();
            bool barF = false, barR = false;
            if (rows != null)
                foreach (var f in rows)
                {
                    if (f == null) continue;
                    if (f.id == DamageModel.BarId) { barF |= f.zone1 == DamageModel.ZFront; barR |= f.zone1 == DamageModel.ZRear; continue; }
                    Fold(ref k, f.id, Mathf.RoundToInt(f.stage), f.zone1, f.hitSide, f.corner1 - 1, f.mag);
                }
            if (L != null)
                foreach (var inc in L.incidents)
                    foreach (var r in inc.outcome.rows)
                    {
                        if (r.id == DamageModel.BarId) { barF |= r.zone == DamageModel.ZFront; barR |= r.zone == DamageModel.ZRear; continue; }
                        Fold(ref k, r.id, r.stage, r.zone, r.side, r.corner, r.mag);
                    }
            // a crushed bar is hidden: it only lets a cover that already shows sit deeper
            if (barF && k.zones.x > 0f) k.zones.x = Mathf.Min(1f, k.zones.x + 0.12f);
            if (barR && k.zones.y > 0f) k.zones.y = Mathf.Min(1f, k.zones.y + 0.12f);
            return k;
        }

        void Update()
        {
            if (car == null) return;
            float now = Time.time;
            if (now >= nextRead)
            {
                nextRead = now + 0.2f;
                Now = Read(carried, ledger);
                if (!LampsOn) Now.lampOut = 0;
                if (!BodyOn) { Now.zones = Vector4.zero; Now.roof = 0f; }
                if (!SrsOn) Now.bags = 0;
                bool changed = !pushedOnce || !Same(Now, pushed);
                if (changed || now >= nextPush) { Push(); nextPush = now + 2f; }
                if (Now.bags > 0 && bagsPlaced == 0) PlaceBags();
                if (BodyOn && Now.roof > 0f && crack == null) PlaceCrack();
            }
            Steam();
        }

        static bool Same(Look a, Look b) =>
            a.zones == b.zones && a.roof == b.roof && a.biasF == b.biasF && a.biasR == b.biasR && a.lampOut == b.lampOut;

        // ================= the body and the lenses (PSX/CarPaint) =================
        public static readonly int OnId = Shader.PropertyToID("_PSXDmgOn"), AId = Shader.PropertyToID("_PSXDmgA"),
            BId = Shader.PropertyToID("_PSXDmgB"), FId = Shader.PropertyToID("_PSXDmgF"), RId = Shader.PropertyToID("_PSXDmgR"),
            UId = Shader.PropertyToID("_PSXDmgU"), CId = Shader.PropertyToID("_PSXDmgC"), LampOutId = Shader.PropertyToID("_PSXLampOut");

        void Push()
        {
            pushed = Now;
            pushedOnce = true;
            if (lights != null) lights.SetLampOut(Now.lampOut);
            PaintGloss.Collect(car.transform, rends);
            if (rends.Count == 0) return;
            var bx = body != null && body.box != null ? body.box : car.GetComponent<BoxCollider>();
            Transform bt = bx != null ? bx.transform : car.transform;
            Vector3 cW = bx != null ? bt.TransformPoint(bx.center) : bt.position;
            Vector3 ls = bt.lossyScale;
            Vector3 half = bx != null ? Vector3.Scale(bx.size * 0.5f, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)))
                                      : new Vector3(0.9f, 0.65f, 2.2f);
            half = Vector3.Max(half, Vector3.one * 0.1f);
            float seed = Mathf.Abs((carId ?? "").GetHashCode() % 1000) * 0.0137f;
            bool any = Now.Any;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var r in rends)
            {
                if (r == null) continue;
                bool wheel = false;
                if (body != null && body.wheelRenderers != null)
                    foreach (var w in body.wheelRenderers) wheel |= w == r;
                var m = r.sharedMaterial;
                if (m != null && m.HasProperty("_Dull") && m.GetFloat("_Dull") > 0.5f) wheel = true;
                var t = r.transform;
                Matrix4x4 lw = t.localToWorldMatrix;
                var axis = new Vector3(((Vector3)lw.GetColumn(0)).magnitude, ((Vector3)lw.GetColumn(1)).magnitude, ((Vector3)lw.GetColumn(2)).magnitude);
                Vector3 cB = Vector3.Scale(t.InverseTransformPoint(cW), axis);
                Vector3 f = t.InverseTransformDirection(bt.forward), rt = t.InverseTransformDirection(bt.right), up = t.InverseTransformDirection(bt.up);
                r.GetPropertyBlock(block);
                block.SetFloat(OnId, any && !wheel ? 1f : 0f);
                block.SetVector(AId, Now.zones);
                block.SetVector(BId, new Vector4(Now.roof, Now.biasF, Now.biasR, seed));
                block.SetVector(FId, new Vector4(f.x, f.y, f.z, 1f / half.z));
                block.SetVector(RId, new Vector4(rt.x, rt.y, rt.z, 1f / half.x));
                block.SetVector(UId, new Vector4(up.x, up.y, up.z, 1f / half.y));
                block.SetVector(CId, new Vector4(cB.x, cB.y, cB.z, 0f));
                int lo = Now.lampOut;
                block.SetVector(LampOutId, new Vector4(lo & 1, (lo >> 1) & 1, (lo >> 2) & 1, (lo >> 3) & 1));
                r.SetPropertyBlock(block);
            }
        }

        // ================= steam =================
        ParticleSystem steam;
        bool steamFailed;

        void Steam()
        {
            float rate = 0f;
            if (SteamOn && Now.radStage > 0 && temp != null && temp.enabled && !temp.Seized)
            {
                float c = temp.celsius;
                bool holed = Now.radStage >= 2;
                float from = holed ? HoleSteamC : WeepSteamC;
                if (c >= from)
                {
                    float heat = Mathf.Clamp01((c - from) / 20f);
                    rate = holed ? Mathf.Lerp(14f, 30f, heat) : Mathf.Lerp(3f, 8f, heat);
                    if (temp.coolantPct < 5f) rate *= 0.3f;   // little left to boil
                }
            }
            if (rate <= 0f && steam == null) return;
            if (steam == null && !MakeSteam()) return;
            var em = steam.emission;
            em.rateOverTime = rate;
            var main = steam.main;
            bool hard = Now.radStage >= 2;
            main.startSize = new ParticleSystem.MinMaxCurve(hard ? 0.35f : 0.22f, hard ? 0.7f : 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(hard ? 1.2f : 0.5f, hard ? 2.6f : 1.0f);
            main.startColor = new Color(0.93f, 0.95f, 0.97f, hard ? 0.42f : 0.24f);
        }

        bool MakeSteam()
        {
            if (steamFailed) return false;
            var smoke = car.GetComponent<TireSmoke>();
            var mat = smoke != null ? smoke.material : null;
            if (mat == null) { steamFailed = true; return false; }
            var bx = body != null && body.box != null ? body.box : car.GetComponent<BoxCollider>();
            Vector3 c = bx != null ? bx.center : new Vector3(0f, 0.65f, 0f);
            Vector3 h = bx != null ? bx.size * 0.5f : new Vector3(0.9f, 0.65f, 2.2f);
            var go = new GameObject("RadiatorSteam") { layer = CarReflections.NoReflectLayer };
            go.transform.SetParent(bx != null ? bx.transform : car.transform, false);
            // the front of the bonnet, over the grille: where a core lets go
            go.transform.localPosition = c + new Vector3(0f, h.y * 0.35f, h.z * 0.78f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // the box shape emits along +Z: up
            steam = go.AddComponent<ParticleSystem>();
            var main = steam.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 96;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.7f);
            main.gravityModifier = -0.12f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = steam.emission;
            em.rateOverTime = 0f;
            var sh = steam.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(h.x * 1.1f, 0.45f, 0.05f);
            sh.randomDirectionAmount = 0.25f;
            var col = steam.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = steam.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            pr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            steam.Play();
            return true;
        }

        // ================= the bags (cockpit) =================
        int bagsPlaced;
        float nextBagTry;
        static Mesh bagMesh;
        static Material bagMat;

        void PlaceBags()
        {
            if (Time.time < nextBagTry) return;
            nextBagTry = Time.time + 0.5f;
            var ci = car.GetComponentInChildren<CockpitInterior>(true);
            if (ci == null || ci.WheelTransform == null) return;
            var vinyl = CockpitModel.Mat("vinyl");
            if (vinyl == null) return;
            if (bagMat == null)
            {
                bagMat = new Material(vinyl) { name = "SRS_Bag", hideFlags = HideFlags.HideAndDontSave };
                bagMat.SetFloat("_Mode", 2f);    // SOLID: vertex rgb the colour, alpha the roughness
            }
            var ct = car.transform;
            var wheel = ci.WheelTransform;
            Quaternion facing = Quaternion.LookRotation(-ct.forward, ct.up);
            // the driver's: limp off the wheel's hub, turning with it
            Bag("SRS_Driver", wheel, wheel.position - ct.forward * 0.08f - ct.up * 0.03f, facing, 0.42f, ci.gameObject.layer);
            if (Now.bags >= 2)
            {
                // the passenger's: out of the top of the dash, draped over it
                Vector3 hub = ct.InverseTransformPoint(wheel.position);
                Vector3 at = ct.TransformPoint(new Vector3(-hub.x, hub.y + 0.10f, hub.z + 0.26f));
                Transform frame = ci.transform;   // (the cabin model is scaled per axis: a bag under it would skew)
                Bag("SRS_Passenger", frame, at, facing * Quaternion.Euler(18f, 0f, 0f), 0.58f, ci.gameObject.layer);
            }
            bagsPlaced = Now.bags;
            ci.CollectRenderers();
        }

        // ================= the cracked windscreen (cockpit) =================
        GameObject crack;
        float nextCrackTry;
        static Texture2D crackTex;

        /// <summary>A roof_glass row: a star of cracks on the windscreen, seen from
        /// the seat (a quad on the pane's own plane, PSX/Decal like the tyre smoke).</summary>
        void PlaceCrack()
        {
            if (Time.time < nextCrackTry) return;
            nextCrackTry = Time.time + 0.5f;
            var ci = car.GetComponentInChildren<CockpitInterior>(true);
            var smoke = car.GetComponent<TireSmoke>();
            if (ci == null || smoke == null || smoke.material == null) return;
            Renderer screen = null;
            foreach (var r in ci.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null && r.sharedMaterial.name == "Cockpit_Glass_windshield") { screen = r; break; }
            var mf = screen != null ? screen.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return;
            var ct = car.transform;
            var vs = mf.sharedMesh.vertices;
            var ts = mf.sharedMesh.triangles;
            Vector3 nSum = Vector3.zero, cSum = Vector3.zero;
            float aSum = 0f;
            for (int i = 0; i + 2 < ts.Length; i += 3)
            {
                Vector3 a = screen.transform.TransformPoint(vs[ts[i]]), b = screen.transform.TransformPoint(vs[ts[i + 1]]),
                        c = screen.transform.TransformPoint(vs[ts[i + 2]]);
                Vector3 x = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(x, ct.forward) > 0f) x = -x;           // every face toward the cabin
                float area = x.magnitude * 0.5f;
                nSum += x;
                cSum += (a + b + c) / 3f * area;
                aSum += area;
            }
            if (aSum < 1e-4f) return;
            Vector3 n = nSum.normalized, centre = cSum / aSum;
            Vector3 up = Vector3.ProjectOnPlane(ct.up, n).normalized;
            Vector3 side = Vector3.Cross(up, n).normalized;
            float hub = ct.InverseTransformPoint(ci.WheelTransform != null ? ci.WheelTransform.position : centre).x;
            // the star on the passenger's half, low on the pane
            Vector3 at = centre + n * 0.012f - up * 0.06f + side * (Vector3.Dot(side, ct.right) * (hub > 0f ? -0.22f : 0.22f));
            crack = new GameObject("GlassCrack") { layer = ci.gameObject.layer };
            crack.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-n, up));
            crack.transform.SetParent(ci.transform, true);
            var m = new Mesh { name = "GlassCrack" };
            const float H = 0.34f;
            m.vertices = new[] { new Vector3(-H, -H, 0f), new Vector3(-H, H, 0f), new Vector3(H, H, 0f), new Vector3(H, -H, 0f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            crack.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = crack.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(smoke.material) { name = "GlassCrack", mainTexture = CrackTex(), hideFlags = HideFlags.HideAndDontSave };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            ci.CollectRenderers();
        }

        /// <summary>A star of cracks: jagged rays from a strike and broken rings round it.</summary>
        static Texture2D CrackTex()
        {
            if (crackTex != null) return crackTex;
            const int S = 256;
            var px = new Color32[S * S];
            var rng = new System.Random(1999);
            Vector2 c0 = new Vector2(S * 0.5f, S * 0.5f);
            void Dot(Vector2 p, byte a)
            {
                int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                for (int dy = 0; dy <= 1; dy++)
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= S || yy >= S) continue;
                        int k = yy * S + xx;
                        if (px[k].a < a) px[k] = new Color32(235, 238, 240, a);
                    }
            }
            void Line(Vector2 a, Vector2 b, byte alpha)
            {
                int n = Mathf.CeilToInt((b - a).magnitude * 1.5f);
                for (int i = 0; i <= n; i++) Dot(Vector2.Lerp(a, b, i / (float)Mathf.Max(1, n)), alpha);
            }
            int rays = 11;
            for (int r = 0; r < rays; r++)
            {
                float ang = (r + (float)rng.NextDouble() * 0.6f) / rays * Mathf.PI * 2f;
                Vector2 p = c0;
                float len = S * (0.30f + 0.18f * (float)rng.NextDouble());
                for (float d = 0f; d < len; d += 9f)
                {
                    ang += ((float)rng.NextDouble() - 0.5f) * 0.35f;
                    Vector2 q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 9f;
                    Line(p, q, (byte)Mathf.Lerp(220f, 90f, d / len));
                    p = q;
                }
            }
            foreach (float rad in new[] { 10f, 24f, 44f })
                for (float a = 0f; a < Mathf.PI * 2f; a += 0.22f)
                    if (rng.NextDouble() < 0.7)
                    {
                        float r1 = rad * (0.9f + 0.2f * (float)rng.NextDouble());
                        Line(c0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r1,
                             c0 + new Vector2(Mathf.Cos(a + 0.22f), Mathf.Sin(a + 0.22f)) * r1 * (0.9f + 0.2f * (float)rng.NextDouble()), 170);
                    }
            crackTex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "GlassCrack", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            crackTex.SetPixels32(px);
            crackTex.Apply(true);
            return crackTex;
        }

        static void Bag(string name, Transform parent, Vector3 at, Quaternion rot, float width, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetPositionAndRotation(at, rot);
            go.transform.SetParent(parent, true);
            float s = parent.lossyScale.x;
            go.transform.localScale = Vector3.one * (width / Mathf.Max(1e-3f, Mathf.Abs(s)));
            go.AddComponent<MeshFilter>().sharedMesh = BagMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = bagMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>A spent bag, one unit across: a flattened sphere with limp
        /// folds, its lower half sagging toward the lap (+Z faces the occupant).</summary>
        static Mesh BagMesh()
        {
            if (bagMesh != null) return bagMesh;
            const int nu = 18, nv = 10;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var col = new List<Color>();
            var tri = new List<int>();
            for (int j = 0; j <= nv; j++)
            {
                float th = Mathf.PI * j / nv;
                for (int i = 0; i <= nu; i++)
                {
                    float ph = 2f * Mathf.PI * i / nu;
                    var s = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float fold = 1f + 0.10f * Mathf.Sin(ph * 5f + th * 3f) * Mathf.Sin(th * 2f);
                    var p = new Vector3(s.x * 0.5f, s.y * 0.44f, s.z * 0.15f) * fold;
                    float sag = (1f - s.y) * 0.5f;
                    p.y -= 0.10f * sag;
                    p.z += 0.06f * sag;
                    v.Add(p);
                    n.Add(new Vector3(s.x / 0.5f, s.y / 0.44f, s.z / 0.15f).normalized);
                    float shade = 0.80f + 0.06f * Mathf.Sin(ph * 7f + th * 5f);
                    col.Add(new Color(shade + 0.04f, shade + 0.03f, shade - 0.02f, 0.85f));
                }
            }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = j * (nu + 1) + i, b = a + nu + 1;
                    // both windings: a limp bag is seen from any side
                    tri.Add(a); tri.Add(b); tri.Add(a + 1);
                    tri.Add(a + 1); tri.Add(b); tri.Add(b + 1);
                    tri.Add(a); tri.Add(a + 1); tri.Add(b);
                    tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
                }
            bagMesh = new Mesh { name = "SRS_Bag" };
            bagMesh.SetVertices(v);
            bagMesh.SetNormals(n);
            bagMesh.SetColors(col);
            bagMesh.SetTriangles(tri, 0);
            bagMesh.RecalculateBounds();
            bagMesh.RecalculateTangents();
            return bagMesh;
        }
    }

    /// <summary>
    /// SRS (design §3.1): on a car fitted with bags (car_facts.json airbags), a
    /// FRONTAL hit - the push on the car within 30 degrees of straight back, the
    /// nose or a front corner - fires them with a chance that ramps from 0 at a
    /// 3.5 m/s delta-V (along the car) to 1 at 6 m/s. The row srs_deployed
    /// (hidden, R0b's cause stamped by DamageBank like every hit row; mag = the
    /// bags that fired) costs the bag set (FaultCatalog). Read against the
    /// incident's own dice, so as the pulse grows it only ever fires, once.
    /// PSX_FAULT_SRS=0 = no bag fires. The SRS lamp is R2a's.
    /// </summary>
    public static class SrsBag
    {
        /// <summary>
        /// One call after every DamageModel.Resolve in a ledger: the crush order
        /// (every ledger) and the bags (the career car's). Rows are only added.
        /// </summary>
        public static void AfterResolve(Incident inc, bool banks)
        {
            CrushOrder(inc);
            Roll(inc, banks);
        }

        /// <summary>PSX_FAULT_CRUSH=0 = the zone table's dice alone at the top bands.</summary>
        public static bool CrushOn = System.Environment.GetEnvironmentVariable("PSX_FAULT_CRUSH") != "0";

        /// <summary>
        /// THE CRUSH ORDER at the top bands (design §3.1: cover, bar, lamps, hood,
        /// condenser, radiator, core support, rails): a FRONT or REAR hit that
        /// crushes past a part takes it whatever its die said. Band 4 (8-10 m/s):
        /// the cover crushed (panel stage 3) and the struck side's lamp (both on a
        /// square hit); band 5 (10+, the core support ~100%): both lamps and, in
        /// front, the radiator crushed (stage 3, its leak live at once). An 80 mph
        /// tree reads as a wreck, not as one cracked lamp.
        /// </summary>
        public static void CrushOrder(Incident inc)
        {
            if (!CrushOn || inc == null || inc.kind != DamageModel.KHit || inc.band < 4) return;
            int z = inc.zone;
            if (z != DamageModel.ZFront && z != DamageModel.ZRear) return;
            var o = inc.outcome;
            Add(o, DamageModel.PanelId, 3, 8f, z, inc.side);
            if (inc.band >= 5 || inc.side == 0) { Add(o, DamageModel.LampId, 1, 0f, z, -1); Add(o, DamageModel.LampId, 1, 0f, z, 1); }
            else Add(o, DamageModel.LampId, 1, 0f, z, inc.side);
            if (z == DamageModel.ZFront && inc.band >= 5)
            {
                Add(o, DamageModel.RadiatorId, 3, DamageModel.LeakPctMin[3], z, 0);
                o.leakPctMin = Mathf.Max(o.leakPctMin, DamageModel.LeakPctMin[3]);
                o.radiatorLoss = Mathf.Max(o.radiatorLoss, 52f);
            }
        }

        /// <summary>As DamageModel's AddRow: one row per id, zone and side
        /// (the radiator one per car), raised, never doubled.</summary>
        static void Add(Outcome o, string id, int stage, float mag, int zone, int side)
        {
            bool one = id == DamageModel.RadiatorId;
            for (int i = 0; i < o.rows.Count; i++)
            {
                var r = o.rows[i];
                if (r.id != id || (!one && (r.zone != zone || r.side != side))) continue;
                if (stage > r.stage) o.rows[i] = new Row { id = id, stage = stage, mag = Mathf.Max(mag, r.mag), zone = r.zone, side = r.side, corner = -1 };
                return;
            }
            o.rows.Add(new Row { id = id, stage = stage, mag = mag, zone = zone, side = side, corner = -1 });
        }

        public const string Id = "srs_deployed";
        public const float FromMps = 3.5f, SureMps = 6f;
        public static readonly float FrontCosMin = Mathf.Cos(30f * Mathf.Deg2Rad);
        /// <summary>DamageModel's water die: a hit never reads it.</summary>
        const int Die = 11;

        /// <summary>How square on from the front a push on the car is (1 dead
        /// ahead, below 0.87 outside the 30-degree cone), in the box frame.</summary>
        public static float FrontCos(Vector3 pushLocal)
        {
            float h = Mathf.Sqrt(pushLocal.x * pushLocal.x + pushLocal.z * pushLocal.z);
            return h < 1e-4f ? 0f : -pushLocal.z / h;
        }

        /// <summary>The bags this drive's car has: 0 none, 1 driver, 2 dual.</summary>
        public static int Fitted()
        {
            var f = CarFacts.Get(RaceHandoff.CarSpecId);
            return f == null || !f.HasAirbag ? 0 : f.HasPassengerAirbag ? 2 : 1;
        }

        /// <summary>After DamageModel.Resolve (the career car's ledger only).</summary>
        public static void Roll(Incident inc, bool banks)
        {
            if (!DamageLook.SrsOn || !banks || inc == null || inc.kind != DamageModel.KHit) return;
            bool frontal = inc.zone == DamageModel.ZFront || (inc.zone == DamageModel.ZCorner && inc.corner >= 0 && inc.corner < 2);
            if (!frontal || inc.frontCos < FrontCosMin) return;
            float p = Mathf.InverseLerp(FromMps, SureMps, inc.dv * inc.frontCos);
            float die = inc.draws != null && inc.draws.Length > Die ? inc.draws[Die] : 0.5f;
            if (p <= 0f || die >= p) return;
            int bags = Fitted();
            if (bags == 0) return;
            inc.outcome.rows.Add(new Row { id = Id, stage = 1, mag = bags, zone = DamageModel.ZFront, side = inc.side, corner = -1 });
        }
    }
}
