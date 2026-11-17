using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// DAMAGE ROUND 2 (owner on PC48, 2026-11-16: "I'd also like to see
    /// scuffing for wheels (hitting curbs, etc)"). CURB RASH, per wheel, from
    /// real contacts of the career car - never rolled:
    ///   THE WHEEL'S SIDE ON A CURB. Every physics step, one short ray per
    ///     grounded wheel out of the tyre's side at the rim's lip (7 cm over
    ///     the road): a STEEP face (steeper than 53 degrees) that stands
    ///     within 60 degrees of square to the axle and has reached the
    ///     sidewall is a strike. What it is NOT: a kerb the tread rides - a
    ///     ramped or low kerb (no steep face at lip height: the racing kerbs),
    ///     and any kerb met nose-on (the face square to the wheel's travel:
    ///     the tread climbs it, an ordinary kerb mount).
    ///   THE CAR'S SIDE ON A WALL, A BARRIER, A ROCK OR ANOTHER CAR at a
    ///     wheel (a sideways contact of the body within a tyre's radius of an
    ///     axle, at wheel height): the arch and the sill share that one, so it
    ///     takes more of it.
    /// Its stage from how hard the wheel was pressed into the face (the
    /// closing speed square to it) and how far it then rubbed along it:
    ///   1 light rash (a short arc of the lip scraped bright),
    ///   2 heavy rash (a third of the rim and the sidewall scuffed),
    ///   3 gouged all round, the sidewall cut (a new wheel).
    /// One row per wheel (wheel_rash_fl / _fr / _rl / _rr, raised, never
    /// doubled), banked like every hit row (DamageLedger.Wheel, DamageBank),
    /// hidden until looked at, with its cause and place ("front-right rim:
    /// curb, West Trade Street"). It takes a little off the paint lane and the
    /// value, shows on the wheel everywhere the car is shown
    /// (PSXCarGlass.cginc _PSXWheelRash) and is repaired like any row: a
    /// refinish (stages 1-2) or a wheel (stage 3) at the shop.
    /// PSX_DMG_WHEELS=0: none of it (no strike is read, no row shows).
    /// </summary>
    [DefaultExecutionOrder(850)]
    public sealed class WheelScuff : MonoBehaviour
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_DMG_WHEELS") != "0";

        public const string Id = "wheel_rash";
        public static readonly string[] Ids = { "wheel_rash_fl", "wheel_rash_fr", "wheel_rash_rl", "wheel_rash_rr" };
        public static bool Is(string id) => id != null && id.StartsWith(Id, System.StringComparison.Ordinal);
        /// <summary>Which wheel a row id is (0 FL, 1 FR, 2 RL, 3 RR), -1 not a wheel row.</summary>
        public static int CornerOf(string id) => System.Array.IndexOf(Ids, id);
        public static string IdFor(int corner) => Ids[Mathf.Clamp(corner, 0, 3)];

        // ---------------- the rules (pure) ----------------
        /// <summary>The lip's height over the road: a face lower than this is a kerb the tyre rides.</summary>
        public const float ProbeUp = 0.07f;
        /// <summary>A face whose normal stands up more than this is a ramp (under 53 degrees): a kerb mount.</summary>
        public const float RampNormalY = 0.6f;
        /// <summary>The face stands within 60 degrees of square to the axle, or the tread meets it.</summary>
        public const float SideCos = 0.5f;
        /// <summary>Pressed into the face at (m/s): a kiss, a hard strike, a gouging one.</summary>
        public const float KissMps = 0.10f, HardMps = 0.9f, GougeMps = 2.2f;
        /// <summary>Rubbed along it for (m).</summary>
        public const float RubLight = 0.3f, RubHeavy = 2.5f, RubGouge = 6f;
        /// <summary>The same through the body's side (the arch and the sill share it).</summary>
        public const float BodyLight = 1.0f, BodyHeavy = 3f, BodyGouge = 6f, BodyRubLight = 1.5f, BodyRubHeavy = 8f;

        /// <summary>The rash's stage (0 none) from the closing speed square to
        /// the face (m/s) and the rub along it (m).</summary>
        public static int StageFor(float vn, float rub, bool viaBody)
        {
            if (viaBody)
                return vn >= BodyGouge ? 3 : vn >= BodyHeavy || rub >= BodyRubHeavy ? 2 : vn >= BodyLight || rub >= BodyRubLight ? 1 : 0;
            if (vn >= GougeMps || (vn >= HardMps && rub >= RubGouge)) return 3;
            if (vn >= HardMps || rub >= RubHeavy) return 2;
            return vn >= KissMps || rub >= RubLight ? 1 : 0;
        }

        /// <summary>A strike this is not: a ramped kerb, or a face met nose-on.
        /// <paramref name="normalUp"/> the face's normal along the car's up,
        /// <paramref name="squareToAxle"/> the cosine between the face and the axle.</summary>
        public static bool IsStrike(float normalUp, float squareToAxle) =>
            Mathf.Abs(normalUp) < RampNormalY && squareToAxle >= SideCos;

        /// <summary>By stage: the value a buyer takes off (% per wheel), the paint lane's loss.</summary>
        public static readonly float[] ValuePct = { 0f, 0.4f, 1.0f, 2.0f };
        public static readonly float[] PaintLoss = { 0f, 0.5f, 1.5f, 3f };
        /// <summary>The shop's price by stage against the catalog's flat $110: a touch-up $70,
        /// a refinish $110, a wheel $240 (1999; scaled by the family's own flat price).</summary>
        static readonly int[] Price = { 110, 70, 110, 240 };
        public static int Cost(int cost, float oldStage, float newStage)
        {
            int o = Mathf.Clamp(Mathf.RoundToInt(oldStage), 0, 3), n = Mathf.Clamp(Mathf.RoundToInt(newStage), 0, 3);
            float flat = cost * 110f / Price[o];
            return Mathf.RoundToInt(flat * Price[n] / 110f);
        }

        static readonly string[] CornerWords = { "front-left", "front-right", "rear-left", "rear-right" };
        public static string Corner(int corner) => CornerWords[Mathf.Clamp(corner, 0, 3)];
        public static string Label(int corner, float stage) =>
            (stage >= 3f ? "Wheel Gouged (" : stage >= 2f ? "Wheel Curb Rash (" : "Wheel Scuffed (") + Corner(corner) + ")";
        /// <summary>"front-right rim: curb, West Trade Street".</summary>
        public static string CauseNote(int corner, string what) =>
            Corner(corner) + " rim: " + (string.IsNullOrEmpty(what) ? "struck" : what);

        /// <summary>Where on the wheel the rash starts (rad), off its incident.</summary>
        public static float Angle(float seed) => (seed - 0.5f) * Mathf.PI * 2f;

        static readonly int RashId = Shader.PropertyToID("_PSXWheelRash");
        static MaterialPropertyBlock block;

        /// <summary>One wheel's rash into its renderer's block (0 clears it),
        /// keeping what the other writers put there.</summary>
        public static void Write(Renderer wheel, float amount, float seed, float radius)
        {
            if (wheel == null) return;
            if (amount <= 0f && !wheel.HasPropertyBlock()) return;
            if (block == null) block = new MaterialPropertyBlock();
            wheel.GetPropertyBlock(block);
            block.SetVector(RashId, new Vector4(On ? amount : 0f, Angle(seed), Mathf.Max(0.1f, radius), seed));
            wheel.SetPropertyBlock(block);
        }

        // ---------------- the contacts ----------------
        const float ProbeBack = 0.05f, Touch = 0.012f, LostAfter = 0.25f;

        CarController car;
        DamageLedger ledger;
        Rigidbody rb;
        CarBody body;
        readonly bool[] touching = new bool[4], viaBody = new bool[4];
        readonly float[] vnMax = new float[4], rub = new float[4], lastSeen = new float[4], lastRubT = new float[4];
        readonly int[] told = new int[4];
        readonly string[] what = new string[4];

        /// <summary>Strikes read so far (the play check).</summary>
        public int Strikes { get; private set; }
        public float RubOf(int i) => rub[i];
        public float PressOf(int i) => vnMax[i];

        /// <summary>The career car's wheels, armed with its ledger (DamageLedger.Arm).</summary>
        public static void Attach(CarController car, DamageLedger L)
        {
            if (!On || car == null || L == null || !L.Banks) return;
            var w = car.GetComponent<WheelScuff>();
            if (w == null) w = car.gameObject.AddComponent<WheelScuff>();
            w.car = car;
            w.ledger = L;
            w.rb = car.Body != null ? car.Body : car.GetComponent<Rigidbody>();
            w.body = car.GetComponent<CarBody>();
        }

        void FixedUpdate()
        {
            if (!On || car == null || ledger == null || rb == null) return;
            float now = Time.time, dt = Time.fixedDeltaTime;
            Vector3 up = car.transform.up;
            for (int i = 0; i < 4; i++)
            {
                var wc = car.wheelContacts[i];
                if (wc.grounded)
                {
                    Vector3 fwd = wc.forward.sqrMagnitude > 0.5f ? wc.forward : car.transform.forward;
                    Vector3 outw = Vector3.Cross(up, fwd).normalized * (i % 2 == 0 ? -1f : 1f);
                    float halfW = body != null ? body.TyreHalfWidth(i) : 0.1f;
                    Vector3 from = wc.point + up * ProbeUp - outw * ProbeBack;
                    float reach = ProbeBack + halfW + Touch;
                    if (Physics.Raycast(from, outw, out RaycastHit h, reach, ~0, QueryTriggerInteraction.Ignore) && h.rigidbody != rb)
                    {
                        Vector3 nH = Vector3.ProjectOnPlane(h.normal, up);
                        if (nH.sqrMagnitude > 1e-4f && IsStrike(Vector3.Dot(h.normal, up), Vector3.Dot(-nH.normalized, outw)))
                        {
                            nH.Normalize();
                            Vector3 v = rb.GetPointVelocity(wc.point);
                            if (h.rigidbody != null) v -= h.rigidbody.GetPointVelocity(h.point);
                            float vn = Vector3.Dot(v, -nH);
                            if (vn > -0.05f)
                            {
                                float vt = (v - nH * Vector3.Dot(v, nH)).magnitude;
                                if (!touching[i]) what[i] = Describe(h.collider, h.rigidbody, Tall(from, outw, up, reach));
                                Feed(i, Mathf.Max(0f, vn), vt * dt, false, now);
                            }
                        }
                    }
                }
                if (touching[i] && now - lastSeen[i] > LostAfter) touching[i] = false;
            }
        }

        /// <summary>Something stands 30 cm over the road there too: a wall, not a curb.</summary>
        static bool Tall(Vector3 from, Vector3 outw, Vector3 up, float reach) =>
            Physics.Raycast(from + up * 0.25f, outw, out RaycastHit h, reach + 0.12f, ~0, QueryTriggerInteraction.Ignore) &&
            Mathf.Abs(Vector3.Dot(h.normal, up)) < RampNormalY;

        void OnCollisionEnter(Collision c) => BodySide(c, true);
        void OnCollisionStay(Collision c) => BodySide(c, false);

        /// <summary>The body's side on something at a wheel.</summary>
        void BodySide(Collision c, bool enter)
        {
            if (!On || car == null || ledger == null || rb == null || c == null) return;
            int n = c.contactCount;
            if (n == 0) return;
            Transform t = car.transform;
            Vector3 right = t.right, rel = c.relativeVelocity;
            float now = Time.time;
            for (int k = 0; k < n; k++)
            {
                var cp = c.GetContact(k);
                float lat = Vector3.Dot(cp.normal, right);      // the push on the car: from its left when positive
                if (Mathf.Abs(lat) < 0.75f) continue;
                Vector3 local = t.InverseTransformPoint(cp.point);
                for (int i = lat > 0f ? 0 : 1; i < 4; i += 2)
                {
                    if (car.wheelHubs == null || i >= car.wheelHubs.Length || car.wheelHubs[i] == null) continue;
                    Vector3 hub = t.InverseTransformPoint(car.wheelHubs[i].position);
                    float r = car.WheelRadiusOf(i);
                    if (Mathf.Abs(local.z - hub.z) > r + 0.12f || local.y > hub.y + 0.6f * r) continue;
                    float along = Vector3.Dot(rel, cp.normal);
                    float vn = enter ? Mathf.Abs(along) : 0f;
                    float slide = 0f;
                    if (!enter && lastRubT[i] != Time.fixedTime)
                    {
                        lastRubT[i] = Time.fixedTime;
                        slide = (rel - cp.normal * along).magnitude * Time.fixedDeltaTime;
                    }
                    if (!touching[i]) what[i] = Describe(c.collider, c.rigidbody, true);
                    Feed(i, vn, slide, true, now);
                    break;
                }
            }
        }

        void Feed(int i, float vn, float rubAdd, bool body, float now)
        {
            if (!touching[i])
            {
                touching[i] = true;
                viaBody[i] = body;
                vnMax[i] = 0f;
                rub[i] = 0f;
                told[i] = 0;
                Strikes++;
            }
            lastSeen[i] = now;
            if (vn > vnMax[i]) vnMax[i] = vn;
            rub[i] += rubAdd;
            int st = StageFor(vnMax[i], rub[i], viaBody[i]);
            if (st <= told[i]) return;
            told[i] = st;
            ledger.Wheel(i, st, vnMax[i], what[i], now);
        }

        /// <summary>"curb, West Trade Street": what the wheel met and where.</summary>
        string Describe(Collider col, Rigidbody other, bool tall)
        {
            string kind;
            string name = col != null ? col.name.ToLowerInvariant() : "";
            if (other != null && !other.isKinematic) kind = "another car";
            else if (!tall) kind = "curb";
            else if (name.Contains("rail") || name.Contains("barrier") || name.Contains("guard") || name.Contains("jersey")) kind = "barrier";
            else if (name.Contains("rock") || name.Contains("cliff") || name.Contains("boulder")) kind = "rock";
            else kind = "wall";
            string place = Place(car != null ? car.transform.position : Vector3.zero);
            return string.IsNullOrEmpty(place) ? kind : kind + ", " + place;
        }

        /// <summary>The street (the city) or the venue (a stage) a position is on.</summary>
        public static string Place(Vector3 pos)
        {
            var city = City.CityWorld.Active;
            if (city != null)
            {
                string street = city.StreetNameAt(pos);
                if (!string.IsNullOrEmpty(street)) return street == "RAMP" ? "a ramp" : street;
            }
            if (TrackCatalog.All != null && TrackCatalog.All.Length > 0)
            {
                var def = TrackCatalog.At(RaceHandoff.TrackIndex);
                if (def != null && !string.IsNullOrEmpty(def.name)) return def.name;
            }
            return "";
        }
    }
}
