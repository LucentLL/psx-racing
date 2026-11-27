using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE CLEAR LAMP COVERS (owner, 2026-11-11, over the blue Viper's nose at
    /// dusk: "Why don't I see the headlight lens on cars? I specifically made
    /// these layers to cover the headlight textures to make them look more
    /// realistic"). The import had left every clear cover off since the first
    /// delivery that had one - the OBJ path would have painted it opaque - so
    /// the lamp art lay bare in its pocket.
    ///
    /// Now every shell whose delivery carries covers has them
    /// (<see cref="CarModelDef.coverMesh"/>, export_models.mjs `_covers.obj`),
    /// drawn over the ordinary body as ONE extra renderer, a child of the
    /// body's MeshFilter: PSX/CarLens (premultiplied, no depth write, Queue
    /// Transparent, its own fresnel coverage, the sky / world panorama and
    /// the sun's and lamps' glints, like the car's glass). The lamp art and the
    /// lamps' glow, drawn by the body under it, show through it. A pop-up shell
    /// swaps covers with its bodies (the lamps-up body's own covers). Every
    /// place a body is assembled calls <see cref="Dress"/>: CarBody.Apply (the
    /// player, rivals, AI, replay, the inspection), CarShell.Spawn (garage,
    /// seller, dealer, junkyard, meets, traffic), the car viewer, and
    /// CarLights.PopUps after each lamp swap.
    ///
    /// On by default on every profile; -psx-no-lamp-covers (player) or
    /// PSX_CAR_COVERS=0 (editor and player) is the picture as it was.
    /// </summary>
    public static class CarCovers
    {
        public const string ChildName = "LampCovers";
        static int on = -1;

        /// <summary>TOOLS: -1 = the switch as above, 0 off, 1 on.</summary>
        public static int Override = -1;

        public static bool On
        {
            get
            {
                if (Override >= 0) return Override == 1;
                if (on < 0)
                {
                    on = System.Environment.GetEnvironmentVariable("PSX_CAR_COVERS") == "0" ? 0 : 1;
                    foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-psx-no-lamp-covers") on = 0;
                }
                return on == 1;
            }
        }

        /// <summary>Put the right covers over whatever body this filter wears
        /// now (or put them away). Safe to call again and again.</summary>
        public static void Dress(MeshFilter bodyFilter, CarModelDef def)
        {
            if (bodyFilter == null) return;
            var existing = bodyFilter.transform.Find(ChildName);
            Mesh mesh = null;
            if (def != null && def.coverMaterial != null && On)
            {
                var b = bodyFilter.sharedMesh;
                if (b != null && b == def.bodyMesh) mesh = def.coverMesh;
                // CarParts' open body: the covers on Body only (a cover a lid
                // carries moves with the lid, on the part); a shell without the
                // split keeps every cover
                else if (b != null && b == def.bodyOpenMesh) mesh = def.coverOpenMesh != null ? def.coverOpenMesh : def.coverMesh;
                else if (b != null && b == def.lampsUpMesh) mesh = def.coverUpMesh;
                // (the lens variant B wears its own covers: none here)
            }
            if (mesh == null)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }
            GameObject go;
            if (existing == null)
            {
                go = new GameObject(ChildName);
                go.transform.SetParent(bodyFilter.transform, false);
                go.AddComponent<MeshFilter>();
                var nr = go.AddComponent<MeshRenderer>();
                // A clear cover throws no shadow and takes none (as the lens variant's).
                nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                nr.receiveShadows = false;
                nr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                nr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            else go = existing.gameObject;
            go.layer = bodyFilter.gameObject.layer;
            var r = go.GetComponent<MeshRenderer>();
            var br = bodyFilter.GetComponent<Renderer>();
            if (br != null) r.renderingLayerMask = br.renderingLayerMask;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            PSXTexDecode.Stamp(def.coverMaterial);
            r.sharedMaterial = def.coverMaterial;
            go.SetActive(true);
            r.enabled = br == null || br.enabled;
        }

        /// <summary>Triangles of a shell's covers (the cost line).</summary>
        public static int Tris(CarModelDef def) => def == null || def.coverMesh == null ? 0 : (int)def.coverMesh.GetIndexCount(0) / 3;
    }
}
