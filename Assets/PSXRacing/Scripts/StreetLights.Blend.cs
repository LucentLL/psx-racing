using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// A LAMP CHANGE IS FADED, NEVER SWITCHED (the lightlook round, 2026-11-23; the pop round's
    /// leftover). The lamp field and the far field are rebuilt whenever a field lamp is added,
    /// dropped or changed inside them - a station's canopy standing up from its queue, a tile's
    /// tree frame bringing its pole heads, a lamp re-registered - and the rebuilt texture
    /// replaced the shown one at once: every pool the change touched jumped (the meter's
    /// "field rebuilds when lamps change inside its window", 40-280 px at 150 m and more; with
    /// the far field's reach, more of them in view). Now a rebuild after a LAMP change keeps
    /// the texture it replaces and the shaders cross-fade from it over <see cref="FieldBlendS"/>
    /// (PSXLamps.cginc: the two read with their own windows and head heights, mixed). A step
    /// of the window alone re-samples the same splat and is not faded (nothing to fade, and the
    /// second fetch is paid only while a fade runs). Each field keeps three textures: the shown
    /// one, the one being faded from, and the one the next build is uploaded into.
    /// PSX_LIGHT_LOOK_FIELDBLEND=0: switched, as before.
    /// </summary>
    public static partial class StreetLights
    {
        public const float FieldBlendS = 1.0f;
        static bool FieldBlendWanted => LightLook.On && LightLook.FieldBlend && NoPop && Application.isPlaying;

        static readonly int FieldPrevId = Shader.PropertyToID("_PSXLampFieldPrev");
        static readonly int FieldPrevSTId = Shader.PropertyToID("_PSXLampFieldPrevST");
        static readonly int FarPrevId = Shader.PropertyToID("_PSXLampFarPrev");
        static readonly int FarPrevSTId = Shader.PropertyToID("_PSXLampFarPrevST");

        static readonly Texture2D[] fieldPool = new Texture2D[3];
        static Texture2D fieldPrev; static Vector2 fieldPrevO; static float fieldPrevY, fieldBlendT0 = -100f;
        static readonly Texture2D[] farPool = new Texture2D[3];
        static Texture2D farPrev; static Vector2 farPrevO; static float farPrevY, farBlendT0 = -100f;

        static float PrevWeight(Texture2D prev, float t0)
        {
            if (prev == null || !FieldBlendWanted) return 0f;
            float k = (Time.unscaledTime - t0) / FieldBlendS;
            if (k >= 1f) return 0f;
            k = Mathf.Clamp01(k);
            return 1f - k * k * (3f - 2f * k);
        }

        /// <summary>A pool texture that is neither shown nor being faded from.</summary>
        static Texture2D FreeOf(Texture2D[] pool, Texture2D shown, Texture2D prev, float prevW, System.Func<Texture2D> make)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == null) pool[i] = make();
                var t = pool[i];
                if (t == shown) continue;
                if (t == prev && prevW > 0f) continue;
                return t;
            }
            return make();   // (cannot happen with three)
        }

        static Texture2D FieldFreeTex() => FreeOf(fieldPool, fieldTex, fieldPrev, PrevWeight(fieldPrev, fieldBlendT0), NewFieldTex);
        static Texture2D FarFreeTex() => FreeOf(farPool, farTex, farPrev, PrevWeight(farPrev, farBlendT0), NewFarTex);

        static void BeginFieldBlend(Texture2D old, Vector2 o, float y)
        {
            if (old == null) return;
            fieldPrev = old; fieldPrevO = o; fieldPrevY = y; fieldBlendT0 = Time.unscaledTime;
            FieldBlends++;
        }

        static void BeginFarBlend(Texture2D old, Vector2 o, float y)
        {
            if (old == null) return;
            farPrev = old; farPrevO = o; farPrevY = y; farBlendT0 = Time.unscaledTime;
            FieldBlends++;
        }

        /// <summary>Lamp changes faded so far (tests, the shots' log).</summary>
        public static int FieldBlends { get; private set; }

        static void PushFieldBlend()
        {
            float w = PrevWeight(fieldPrev, fieldBlendT0);
            if (w <= 0f) { Shader.SetGlobalVector(FieldPrevSTId, Vector4.zero); return; }
            Shader.SetGlobalTexture(FieldPrevId, fieldPrev);
            Shader.SetGlobalVector(FieldPrevSTId, new Vector4(fieldPrevO.x, fieldPrevO.y, fieldPrevY, w));
        }

        static void PushFarBlend()
        {
            float w = PrevWeight(farPrev, farBlendT0);
            if (w <= 0f) { Shader.SetGlobalVector(FarPrevSTId, Vector4.zero); return; }
            Shader.SetGlobalTexture(FarPrevId, farPrev);
            Shader.SetGlobalVector(FarPrevSTId, new Vector4(farPrevO.x, farPrevO.y, farPrevY, w));
        }
    }
}
