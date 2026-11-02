using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE LANDMARK CROWN MODELS (the owner, 2026-11-01: "still many
        //  textures to overhaul and models to improve, especially the crowns
        //  of these buildings"). His Codex models of C1 (the spired crown) and
        //  C3 (the silver crown), Environment_Overhaul2_Uptown/Landmark_Crowns,
        //  converted by tools/city/crowns/glb_to_crown.py into
        //  Resources/CityCrowns/*.bytes (CRWN v1: game-frame triangles, origin
        //  the footprint's area centroid at the mount height). A crowned hero
        //  draws the model into Slot.Crown (its own atlas, Art/City/Crowns)
        //  in place of its coded crown - the tile's and the far skyline's copy
        //  alike, since both are this emit - and C1's mapped tiers stop at the
        //  model's deck. PSX_CROWN_MODELS=0 is the coded crowns (the A/B).
        // ------------------------------------------------------------------

        public static bool CrownModelsOn = System.Environment.GetEnvironmentVariable("PSX_CROWN_MODELS") != "0";

        sealed class CrownModel
        {
            public string res;
            /// <summary>The footprint area centroid the model was built on
            /// (map metres, placement_overhaul1.json).</summary>
            public Vector2 centroid;
            /// <summary>Its origin's height over the tower's floor (m).</summary>
            public float mountH;
            public Vector3[] v;
            public Vector2[] uv;
            public int[] t;
            public bool tried, warned;
        }

        /// <summary>By hero index (<see cref="Heroes"/>): C1 and C3.</summary>
        static readonly CrownModel[] crownModels =
        {
            new CrownModel { res = "CityCrowns/C1_SpiredCrown", centroid = new Vector2(-2231.2237f, 4751.1098f), mountH = 215f },
            null,
            new CrownModel { res = "CityCrowns/C3_SilverCrown", centroid = new Vector2(-2080.9535f, 4794.9095f), mountH = 177f },
            null,
            null,
        };

        /// <summary>The hero's crown model, loaded once; null when it has none,
        /// its file is missing or unreadable, or the models are switched off.</summary>
        static CrownModel CrownModelOf(int hero)
        {
            if (!CrownModelsOn || hero < 0 || hero >= crownModels.Length) return null;
            var m = crownModels[hero];
            if (m == null) return null;
            if (!m.tried)
            {
                m.tried = true;
                var ta = Resources.Load<TextAsset>(m.res);
                if (ta == null) Debug.LogWarning("[CityCrowns] Resources/" + m.res + ".bytes missing - the coded crown stands");
                else
                {
                    try { Read(m, ta.bytes); }
                    catch (System.Exception e) { m.v = null; Debug.LogWarning("[CityCrowns] " + m.res + " unreadable (" + e.Message + ") - the coded crown stands"); }
                }
            }
            return m.v != null ? m : null;
        }

        static void Read(CrownModel m, byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (new string(r.ReadChars(4)) != "CRWN" || r.ReadInt32() != 1) throw new IOException("not CRWN v1");
                int nv = r.ReadInt32(), nt = r.ReadInt32();
                var v = new Vector3[nv]; var uv = new Vector2[nv]; var t = new int[nt * 3];
                for (int i = 0; i < nv; i++)
                {
                    v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                }
                for (int i = 0; i < t.Length; i++)
                {
                    t[i] = r.ReadInt32();
                    if ((uint)t[i] >= (uint)nv) throw new IOException("index out of range");
                }
                m.v = v; m.uv = uv; m.t = t;
            }
        }

        /// <summary>The index in <see cref="Heroes"/> of the hero of this shape (-1 none).</summary>
        static int HeroIndexOf(HeroShape shape)
        {
            for (int h = 0; h < Heroes.Length; h++) if (Heroes[h].shape == shape) return h;
            return -1;
        }

        /// <summary>The height over the tower's floor where a crowned hero's
        /// mapped tiers stop (its model's deck); +inf for every other building.</summary>
        static float CrownClipH(int hero)
        {
            var m = CrownModelOf(hero);
            return m != null ? m.mountH : float.PositiveInfinity;
        }

        /// <summary>The hero's crown model onto the tile, its origin at the
        /// footprint centroid and <paramref name="floor"/> + its mount height:
        /// drawn, never collided (a crown starts far above HeroCollideTopM).
        /// False when the hero has no model (the caller draws the coded crown).</summary>
        static bool EmitCrownModel(TileMeshes tm, int hero, float floor, float drawnMountH = float.NaN)
        {
            var m = CrownModelOf(hero);
            if (m == null) return false;
            // C3 stands on the deck its coded tower actually drew (0.885 of the
            // outline's height): the model was measured on the same map, so the
            // two agree; a map that moved them is said once, and the model
            // follows the drawn deck so it never floats or sinks
            float mount = m.mountH;
            if (!float.IsNaN(drawnMountH))
            {
                if (Mathf.Abs(drawnMountH - m.mountH) > 0.5f && !m.warned)
                {
                    m.warned = true;
                    Debug.LogWarning($"[CityCrowns] {m.res}: the tower's deck is at {drawnMountH:0.0} m, the model was built for {m.mountH:0.0} m - it follows the deck");
                }
                mount = drawnMountH;
            }
            var bk = buckets[(int)Slot.Crown];
            var o = new Vector3(m.centroid.x, floor + mount, m.centroid.y);
            HeroNoCollide(true);
            try
            {
                for (int i = 0; i < m.t.Length; i += 3)
                {
                    int a = m.t[i], b = m.t[i + 1], c = m.t[i + 2];
                    // glb_to_crown.py writes each triangle in its glTF order;
                    // Tri(a, b, c) draws (a, c, b), the front face in Unity
                    bk.Tri(Lw(tm, o + m.v[a]), Lw(tm, o + m.v[b]), Lw(tm, o + m.v[c]), m.uv[a], m.uv[b], m.uv[c]);
                }
            }
            finally { HeroNoCollide(false); }
            return true;
        }
    }
}
