using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// THE WALKER'S PARTS (carparts decision 7): one target per part on the
    /// player's OWN car - the hood at the nose, the trunk at the tail, each
    /// door at its side (four on a four-door). USE opens or shuts that part
    /// (FootInteractor: F / Enter / pad South / the touch USE). The hood's
    /// second verb is the car's INSPECT IT when the place has one (the garage,
    /// the home driveway); the car's own hook keeps GET IN / TAKE KEYS and
    /// angle-first picking chooses between them. Never on someone else's car.
    ///
    /// Built only for a car whose shell has parts (CarParts.Allowed): a car
    /// without them gets nothing at all. The targets hang off the car's root,
    /// not the body, so the parts component coming and going leaves them be.
    /// </summary>
    public class CarPartsFoot : MonoBehaviour
    {
        MeshFilter body;
        CarModelDef def;
        readonly List<(FootTarget t, int bit, string name)> targets = new List<(FootTarget, int, string)>();
        int shown = -1;

        /// <summary>The hood's target (its second verb is set by the place).</summary>
        public FootTarget HoodTarget { get; private set; }
        public IReadOnlyList<(FootTarget t, int bit, string name)> Targets => targets;

        /// <summary>Put the targets on <paramref name="root"/> for the body
        /// <paramref name="bodyFilter"/> wearing <paramref name="d"/>; null (and
        /// nothing made, an old set removed) when the car has no parts.</summary>
        public static CarPartsFoot Attach(Transform root, MeshFilter bodyFilter, CarModelDef d)
        {
            if (root == null) return null;
            var have = root.GetComponent<CarPartsFoot>();
            if (!CarParts.Allowed(d, bodyFilter))
            {
                if (have != null) Remove(have);
                return null;
            }
            if (have != null && have.body == bodyFilter && have.def == d) return have;
            if (have != null) Remove(have);
            var f = root.gameObject.AddComponent<CarPartsFoot>();
            f.body = bodyFilter;
            f.def = d;
            f.Build(root);
            return f;
        }

        public static void Detach(Transform root)
        {
            var have = root != null ? root.GetComponent<CarPartsFoot>() : null;
            if (have != null) Remove(have);
        }

        static void Remove(CarPartsFoot f)
        {
            foreach (var (t, _, _) in f.targets) if (t != null) Kill(t.gameObject);
            Kill(f);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        static string Word(string part)
        {
            switch (part)
            {
                case "Hood": return "HOOD";
                case "Trunk": return "TRUNK";
                case "Hatch": return "HATCH";
                case "Door_L": return "LEFT DOOR";
                case "Door_R": return "RIGHT DOOR";
                case "Door_RL": return "LEFT REAR DOOR";
                case "Door_RR": return "RIGHT REAR DOOR";
            }
            return part.ToUpperInvariant();
        }

        void Build(Transform root)
        {
            var pdef = CarParts.PartsDef(def);
            if (pdef == null) return;
            var bt = body.transform;
            var centre = bt.TransformPoint(def.bodyMesh.bounds.center);
            foreach (var op in pdef.openingParts)
            {
                int bit = CarParts.BitOf(op.name);
                if (bit == 0 || op.mesh == null) continue;
                var b = op.mesh.bounds;
                // the part's middle, pushed out to its own face of the car
                // (the nose for the hood, the tail for the trunk, the side for
                // a door) and raised to a hand's height on it
                var mid = bt.TransformPoint(b.center);
                var outward = mid - centre; outward.y = 0f;
                bool lid = (bit & CarParts.Lids) != 0;
                var at = mid + (outward.sqrMagnitude > 1e-4f ? outward.normalized : bt.forward) * (lid ? 0.25f : 0.15f);
                at.y = Mathf.Max(bt.TransformPoint(b.max).y, bt.TransformPoint(b.min).y) - (lid ? 0f : 0.25f);
                var go = new GameObject("Part_" + op.name);
                go.transform.SetParent(root, true);
                go.transform.position = at;
                var t = go.AddComponent<FootTarget>();
                t.range = lid ? 2.6f : 2.2f;
                t.ignoreRoot = root;
                targets.Add((t, bit, op.name));
                if (bit == CarParts.Hood) HoodTarget = t;
            }
            Relabel();
        }

        /// <summary>Each target says what its part will do now.</summary>
        public void Relabel()
        {
            int mask = CarParts.MaskOf(body);
            shown = mask;
            foreach (var (t, bit, name) in targets)
            {
                if (t == null) continue;
                bool open = (mask & bit) != 0;
                t.title = Word(name);
                t.detail = open ? "Open." : "Shut.";
                t.action = (open ? "SHUT THE " : "OPEN THE ") + Word(name);
                t.verb = open ? "SHUT" : "OPEN";
                int b = bit;
                t.onUse = () =>
                {
                    CarParts.Toggle(body, def, b, player: true);
                    Relabel();
                };
            }
        }

        void Update()
        {
            // a part shut by another way (the LAB row, a re-dress): the words follow
            if (body == null) { Remove(this); return; }
            // the LAB rows off (or lens B on the bench): no target offers anything
            bool allowed = CarParts.Allowed(def, body);
            foreach (var (t, _, _) in targets)
                if (t != null && t.gameObject.activeSelf != allowed) t.gameObject.SetActive(allowed);
            if (!allowed) return;
            int mask = CarParts.MaskOf(body);
            if (mask != shown) Relabel();
        }
    }
}
