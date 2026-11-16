using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// WHICH MODEL A TRAFFIC CAR WEARS, AND ON WHICH ROAD (2026-11-15).
    ///
    /// The owner bought "UAA - City Props - Vehicles" on 11-07 and asked for
    /// its cars "to REPLACE the placeholder Sketchfab traffic models (vans
    /// etc.), not the generated ones". The placeholders were two keys of
    /// <see cref="TrafficSystem.Keys"/>: camry_2001 (a ripped PS1 saloon) and
    /// ford_transit (a Sketchfab van), each dealt twice in every nineteen
    /// cars. With the kit (<see cref="TrafficKit"/>) present they are gone
    /// from every traffic list, and the pack's seventeen vehicles are dealt
    /// with the owner's own models and the catalogue shells:
    ///
    ///   RaceMix    the race tracks, the mountain stages, the title screen
    ///              (TrafficSystem): cars, wagons, the SUV, vans - nothing
    ///              longer than a van on a two-lane road.
    ///   CityMix    Charlotte (CityTraffic), forty-eight pool slots: the same
    ///              cars plus the box truck, the bus, the rig, the taxis and
    ///              the police car, each held to the roads it belongs on
    ///              (<see cref="Fits"/>).
    ///   ParkedMix  the parked cars the builder places (race-track streets,
    ///              the neighbourhood).
    ///
    /// No kit (a checkout without the store files, a web or phone pass), or
    /// PSX_TRAFFIC_UAA=0: every list is today's, model for model, and Fits
    /// answers yes to everything.
    /// </summary>
    public static class TrafficModels
    {
        public const int Car = 0, Van = 1, Taxi = 2, Police = 3, Truck = 4, Bus = 5, Rig = 6;
        public static readonly string[] KindNames = { "car", "van", "taxi", "police", "truck", "bus", "rig" };

        /// <summary>PSX_TRAFFIC_UAA=0: the traffic as it was.</summary>
        public static bool SwitchOn = System.Environment.GetEnvironmentVariable("PSX_TRAFFIC_UAA") != "0";

        static TrafficKit kit;
        static bool looked;
        static readonly Dictionary<string, TrafficKit.Entry> byKey = new Dictionary<string, TrafficKit.Entry>();
        static string[] raceKeys, cityKeys, parkedKeys;

        /// <summary>Forget what was loaded (a tool that re-bakes the kit or
        /// flips the switch inside one editor session; every play start).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            looked = false; kit = null; byKey.Clear();
            raceKeys = cityKeys = parkedKeys = null;
            heldCap = -1;
        }

        static void Look()
        {
            if (looked) return;
            looked = true;
            byKey.Clear();
            kit = SwitchOn ? Resources.Load<TrafficKit>(TrafficKit.ResourcePath) : null;
            if (kit == null || kit.entries == null) { kit = null; return; }
            foreach (var e in kit.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.key) || e.def == null || e.def.bodyMesh == null || e.def.wheelMesh == null) continue;
                byKey[e.key] = e;
                // What it wears, stamped before anything draws it (PSXTexDecode).
                if (e.def.skinMaterials != null) foreach (var m in e.def.skinMaterials) PSXTexDecode.Stamp(m);
                PSXTexDecode.Stamp(e.def.wheelMaterial);
            }
            if (byKey.Count == 0) kit = null;
        }

        /// <summary>Is the pack in this build, and switched on?</summary>
        public static bool On { get { Look(); return kit != null; } }
        public static int KitCount { get { Look(); return byKey.Count; } }

        public static TrafficKit.Entry Entry(string key)
        {
            Look();
            return key != null && byKey.TryGetValue(key, out var e) ? e : null;
        }

        public static bool IsKitKey(string key) => key != null && key.StartsWith("uaa_");

        // ---- the lists ------------------------------------------------------
        // uaa_* = the pack; crown_victoria, sedan_98, fullsize_* = the owner's
        // own models; the rest = the catalogue's everyday shells.
        static readonly string[] RaceMix =
        {
            "uaa_sedan_mid", "crown_victoria", "uaa_van_cargo", "sedan_98",
            "uaa_wagon_box", "uaa_suv", "uaa_sedan_old", "jdm_pickup",
            "uaa_sedan_lux", "uaa_van_pass", "volvo_estate", "uaa_sedan_small",
            "fullsize_taxi", "uaa_wagon_wood", "bmw_e30", "uaa_sedan_96",
            "uaa_sedan_box", "fullsize_police", "audi_saloon", "uaa_sedan_mid",
            "euro_hatch", "fullsize_trooper", "landrover", "citroen_cx", "classic_van",
        };

        /// <summary>Forty-eight pool slots (the desktop cap once, the PC's 96
        /// twice, a phone's 24 the first half): 26 of the pack's everyday
        /// cars, 7 of its special vehicles (2 box trucks, a bus, a rig, 2
        /// taxis, a police car), 7 of the owner's own and 8 catalogue shells.</summary>
        static readonly string[] CityMix =
        {
            "uaa_sedan_mid", "crown_victoria", "uaa_van_cargo", "uaa_sedan_old", "sedan_98", "uaa_suv", "uaa_wagon_box", "uaa_truck_box",
            "uaa_sedan_lux", "fullsize_taxi", "uaa_sedan_small", "uaa_van_pass", "uaa_bus", "uaa_sedan_96", "volvo_estate", "uaa_wagon_wood",
            "fullsize_police", "uaa_sedan_box", "jdm_pickup", "uaa_sedan_mid", "uaa_rig", "uaa_van_cargo", "bmw_e30", "uaa_sedan_old",
            "uaa_taxi", "uaa_suv", "crown_victoria", "uaa_sedan_small", "audi_saloon", "uaa_wagon_box", "uaa_truck_box", "uaa_sedan_lux",
            "sedan_98", "uaa_van_pass", "uaa_police", "uaa_sedan_96", "landrover", "uaa_sedan_box", "uaa_taxi_box", "uaa_wagon_wood",
            "euro_hatch", "uaa_van_cargo", "fullsize_trooper", "uaa_sedan_mid", "citroen_cx", "uaa_suv", "classic_van", "uaa_sedan_old",
        };

        static readonly string[] ParkedMix =
        {
            "crown_victoria", "uaa_sedan_old", "uaa_van_cargo", "jdm_pickup", "uaa_sedan_mid", "uaa_suv",
            "classic_van", "uaa_wagon_wood", "volvo_estate", "uaa_sedan_small", "uaa_sedan_96", "landrover",
            "uaa_sedan_box", "bmw_e30", "uaa_van_pass", "euro_hatch", "uaa_wagon_box", "citroen_cx",
            "uaa_sedan_lux", "audi_saloon", "uaa_police",
        };

        /// <summary>A list with the kit keys this kit does not carry dropped
        /// (a partial bake still deals what it has).</summary>
        static string[] Present(string[] mix, string[] without)
        {
            Look();
            if (kit == null) return without;
            var l = new List<string>(mix.Length);
            foreach (var k in mix) if (!IsKitKey(k) || byKey.ContainsKey(k)) l.Add(k);
            return l.Count > 0 ? l.ToArray() : without;
        }

        static string At(string[] a, int i) => a[((i % a.Length) + a.Length) % a.Length];

        /// <summary>The i-th car of a race's pool (TrafficSystem).</summary>
        public static string RaceKey(int i) => At(raceKeys ?? (raceKeys = Present(RaceMix, TrafficSystem.Keys)), i);
        /// <summary>The i-th car of the city's pool (CityTraffic).</summary>
        public static string CityKey(int i) => At(cityKeys ?? (cityKeys = Present(CityMix, TrafficSystem.Keys)), i);
        /// <summary>PSX_TRAFFIC_UAA_PARKED=1 (read when a scene is BUILT): the
        /// builders' parked cars dealt from the pack too. Default OFF
        /// (2026-11-16): no venue's theme places PlaceParkedCars' cars today,
        /// the neighbourhood's list has no placeholder in it, and no rebuilt
        /// scene with the pack's cars parked has been looked at yet.</summary>
        public static bool ParkedOn => System.Environment.GetEnvironmentVariable("PSX_TRAFFIC_UAA_PARKED") == "1";

        /// <summary>The parked cars a builder deals, in order; <paramref name="was"/>
        /// = its own list, kept when there is no kit or PSX_TRAFFIC_UAA_PARKED is not 1.</summary>
        public static string[] ParkedKeys(string[] was) => !ParkedOn ? was : parkedKeys ?? (parkedKeys = Present(ParkedMix, was));
        public static int CityMixLength => (cityKeys ?? (cityKeys = Present(CityMix, TrafficSystem.Keys))).Length;

        /// <summary>The shell for a key: the kit's, else the car library's.</summary>
        public static CarModelDef Load(string key)
        {
            var e = Entry(key);
            if (e != null) return e.def;
            if (IsKitKey(key)) key = "crown_victoria";   // a kit key with no kit: the owner's own saloon
            return CarModelLibrary.Load(key) ?? CarModelLibrary.Load(CarModelLibrary.Default);
        }

        public static int KindOf(string key)
        {
            var e = Entry(key);
            if (e != null) return e.kind;
            switch (key)
            {
                case "fullsize_taxi": return Taxi;
                case "fullsize_police": case "fullsize_trooper": return Police;
                case "ford_transit": case "classic_van": return Van;
                default: return Car;
            }
        }

        /// <summary>A long body (the box truck, the bus, the rig): it keeps
        /// straight on where the road lets it (CityTraffic.Extend).</summary>
        public static bool Heavy(int kind) => kind >= Truck;

        static int heldCap = -1, heldN;
        /// <summary>How many of the city pool's first <paramref name="cap"/>
        /// slots <see cref="Fits"/> holds to roads of their own (the rig, the
        /// bus, the box trucks, the cabs). CityTraffic lets its pool grow past
        /// the cap by this many - idle, they are not drawn - so the cars that
        /// may go anywhere can still fill the cap in a suburb. 0 without the pack.</summary>
        public static int HeldSlots(int cap)
        {
            if (!On) return 0;
            if (cap != heldCap)
            {
                heldCap = cap; heldN = 0;
                for (int i = 0; i < cap; i++) { int k = KindOf(CityKey(i)); if (Heavy(k) || k == Taxi) heldN++; }
            }
            return heldN;
        }

        // Trade and Tryon, the middle of Uptown, in the city's metres
        // (x = (lon + 80.8177) * 111320 * cos 35.18456, z = (lat - 35.18456) * 111132).
        const float UptownX = -2311f, UptownZ = 4739f;
        /// <summary>How far from Trade and Tryon a cab still cruises: Uptown,
        /// South End, the inner wards. Past it a cab is on a freeway (the
        /// airport run) or is not there.</summary>
        public const float TaxiRadiusM = 4500f;

        /// <summary>
        /// THE RIGHT VEHICLE ON THE RIGHT ROAD: may a car of this kind be born
        /// on a road of this class (CityMap.Edge.cls: 0 local street .. 5
        /// motorway; link = a ramp or slip road) at this spot? Rigs on the
        /// freeways and the trunks' main lines; buses on the arterials; box
        /// trucks on anything but a residential street; cabs in and near
        /// Uptown and on the freeways; cars, vans and the police anywhere.
        /// </summary>
        public static bool Fits(int kind, int cls, bool link, float x, float z)
        {
            if (!On) return true;
            switch (kind)
            {
                case Rig: return cls >= 4 && !link;
                case Bus: return (cls == 2 || cls == 3) && !link;
                case Truck: return cls >= 1;
                case Taxi:
                {
                    if (cls >= 4) return true;
                    float dx = x - UptownX, dz = z - UptownZ;
                    return dx * dx + dz * dz < TaxiRadiusM * TaxiRadiusM;
                }
                default: return true;
            }
        }

        // ---- the shell ------------------------------------------------------
        /// <summary>LOD0 (the body and its turning wheels) gives way to the
        /// one-mesh far level under this share of the screen's height: about
        /// 95 m for a five-metre car at a 60 degree view.</summary>
        const float FarAt = 0.045f;

        /// <summary>
        /// What a kit vehicle has that CarShell's four-wheel shell does not:
        /// its dual rear wheels on their own track, the axles past the second
        /// (they turn with the rear pair), dulled wheels, and the far level.
        /// Called right after CarShell.Spawn by both traffic systems; a shell
        /// that is not the kit's is left exactly as it was.
        /// </summary>
        public static void Dress(Transform shell, CarModelDef def)
        {
            var e = def != null ? Entry(def.key) : null;
            if (e == null || shell == null) return;
            var body = shell.Find("Body");
            Transform wl = shell.Find("Wheel2"), wr = shell.Find("Wheel3");
            var near = new List<Renderer>();
            if (body != null) near.AddRange(body.GetComponentsInChildren<Renderer>(true));
            Material wheelMat = null;
            for (int i = 0; i < 4; i++)
            {
                var w = shell.Find("Wheel" + i);
                if (w == null) continue;
                var r = w.GetComponent<MeshRenderer>();
                if (r == null) continue;
                if (wheelMat == null) wheelMat = r.sharedMaterial;
                if (i >= 2)
                {
                    if (e.rearWheelMesh != null) w.GetComponent<MeshFilter>().sharedMesh = e.rearWheelMesh;
                    if (e.rearHalfTrack > 0.1f)
                    {
                        var p = w.localPosition;
                        p.x = (i % 2 == 0 ? -1f : 1f) * e.rearHalfTrack;
                        w.localPosition = p;
                    }
                }
                CarPaint.DullWheels(r);
                near.Add(r);
            }
            if (e.extraWheels != null && e.extraWheels.Length > 0 && wl != null && wr != null)
            {
                // CarShell slid the shell so its box is centred on the root:
                // the same slide for the extra axles, read off a wheel it placed.
                float slideZ = wl.localPosition.z + def.wheelbase * 0.5f;
                var extra = new Transform[e.extraWheels.Length];
                var left = new bool[extra.Length];
                for (int i = 0; i < extra.Length; i++)
                {
                    var p = e.extraWheels[i];
                    left[i] = p.x < 0f;
                    var from = left[i] ? wl : wr;
                    var g = new GameObject("WheelX" + i);
                    g.layer = from.gameObject.layer;
                    g.transform.SetParent(shell, false);
                    g.transform.localPosition = new Vector3(p.x, p.y, p.z + slideZ);
                    g.transform.localRotation = from.localRotation;
                    g.transform.localScale = from.localScale;
                    g.AddComponent<MeshFilter>().sharedMesh = e.rearWheelMesh != null ? e.rearWheelMesh : def.wheelMesh;
                    var r = g.AddComponent<MeshRenderer>();
                    r.sharedMaterial = wheelMat;
                    CarPaint.DullWheels(r);
                    near.Add(r);
                    extra[i] = g.transform;
                }
                var follow = shell.gameObject.AddComponent<TrafficExtraWheels>();
                follow.left = wl; follow.right = wr; follow.wheels = extra; follow.isLeft = left;
            }
            if (e.farMesh != null && body != null && near.Count > 0)
            {
                var far = new GameObject("Far");
                far.layer = body.gameObject.layer;
                far.transform.SetParent(shell, false);
                far.transform.localPosition = body.localPosition;
                far.transform.localRotation = body.localRotation;
                far.transform.localScale = body.localScale;
                far.AddComponent<MeshFilter>().sharedMesh = e.farMesh;
                var fr = far.AddComponent<MeshRenderer>();
                var br = body.GetComponent<MeshRenderer>();
                fr.sharedMaterial = br != null ? br.sharedMaterial : wheelMat;
                var lod = shell.gameObject.AddComponent<LODGroup>();
                lod.fadeMode = LODFadeMode.None;
                lod.SetLODs(new[] { new LOD(FarAt, near.ToArray()), new LOD(0.0004f, new Renderer[] { fr }) });
                lod.RecalculateBounds();
            }
            // The renderers added here are a car's too (SPEED BLUR's mask).
            SpeedBlur.MarkOtherCar(shell);
        }
    }

    /// <summary>The axles past a kit vehicle's second turn with its rear
    /// wheels: whichever traffic system drives the car turns Wheel2 and
    /// Wheel3, and these copy them.</summary>
    public class TrafficExtraWheels : MonoBehaviour
    {
        public Transform left, right;
        public Transform[] wheels;
        public bool[] isLeft;

        void LateUpdate()
        {
            if (wheels == null || left == null || right == null) return;
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null) wheels[i].localRotation = (isLeft[i] ? left : right).localRotation;
        }
    }
}
