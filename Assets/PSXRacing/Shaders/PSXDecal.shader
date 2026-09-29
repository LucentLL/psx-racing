// Unlit, vertex-coloured, alpha-blended geometry: the tyre marks laid on the
// road and the smoke that comes off the tyres laying them.
//
// One shader for both, because they are the same drawing problem — a mesh
// built in world space every frame, tinted per vertex, with no lighting of
// its own (the smoke and snow take the scene's, _Lit below) and no depth
// write. What separates them is set on the MATERIAL, not here:
//
//   marks  queue Transparent-200 (under the blob shadow, which is -100),
//          _Tint near-black, and the polygon offset below keeps them out of a
//          z-fight with the road surface they are one millimetre above.
//   smoke  queue Transparent, _Tint white, offset harmless on a billboard.
//
// PSX/Lit is opaque-or-cutout and PSX/Glow is additive, so neither could draw
// a soft grey puff or darken tarmac without lighting it up.
//
// Fog is a TINT toward the fog colour and not a fade to nothing, which is the
// same treatment PSX/Lit gives every surface: at full fog the road has become
// fog colour, and a mark on it must become fog colour too or it stays as a
// black line drawn across a wall of haze.
//
// LIT PARTICLES (_Lit 1; the colour pass, C7, 2026-09-29). The smoke and the
// falling snow were UNLIT: a #E8E8EE puff and a white flake at midnight were
// the brightest things on a dark road - "glowing" - and the tail lamps and the
// beams never touched them. With _Lit 1 a vertex takes the light PSX/Rain
// gives a drop - the hour's ambient, a share of the sky's colour and of the
// sun, every street and tail lamp and every low beam, no N.L (a puff is lit
// from all round) - times the hour's exposure. So smoke is white by day,
// grey-blue in the dark, red where a tail lamp is, halogen in a beam; a flake
// is a dot of whatever light it falls through. Per vertex, like the rain: a
// puff is a few pixels across. The tyre MARKS keep _Lit 0 (a dark mark on the
// road it darkens), and with no hour applied (an interior) nothing is lit.
Shader "PSX/Decal"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _MainTexRaw ("16-bit texel decode (set at runtime by PSXTexDecode.cs)", Float) = 0
        _Tint ("Tint", Color) = (1,1,1,1)
        // 1 = lit like a raindrop (the smoke and the snow); 0 = the colour as given (the marks).
        _Lit ("Lit by the scene's lights (0/1)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            // The colour blends; the framebuffer's ALPHA is kept (Zero One):
            // it is the emitter mask the surface under the mark or the smoke
            // wrote (PSXTone.cginc), and neither is a light source.
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            ZWrite Off
            Cull Off
            // Toward the camera in depth only. A decal sitting a millimetre
            // above the tarmac still z-fights it at 300 m, where a millimetre
            // is well under one step of the depth buffer.
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // Colour texels of the 16-bit set arrive undecoded: PSXMainTex decodes them.
            #include "PSXTexDecode.cginc"
            // The one tone curve and the adaptation (the colour pass).
            #include "PSXTone.cginc"
            // The lamp and beam tables, read per vertex for a lit particle.
            #include "PSXHeadlights.cginc"
            #include "PSXLamps.cginc"

            // THE LIT PARTICLE'S LIGHT: PSX/Rain's scene term (RAIN_SKY,
            // RAIN_SUN) with the lamps and beams at 1 - a puff is matt, not
            // the retro-reflector a drop is.
            #define DECAL_SKY  0.5
            #define DECAL_SUN  0.35

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            float _Lit;
            // Declared float4 (not fixed4): the sun passes 1.0 at noon and
            // lowp would clip it on a GLES device.
            float4 _PSXLightColor;
            float4 _PSXAmbient;
            float4 _PSXSkyAmbient;

            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            // Bends the band so it closes late instead of evenly;
            // see PSXGlobals.fogCurve. Floored at 1 in the maths
            // below, so an unset global (0) is the old straight ramp.
            float _PSXFogCurve;
            float _PSXSnap;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR0;
                fixed fog : TEXCOORD1;
                // The lit particle's light (1 unlit). A TEXCOORD, not in
                // COLOR0: inside a beam or under a lamp it passes 1.0.
                float3 light : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 clipPos = UnityObjectToClipPos(v.vertex);

                // The same NDC quantisation every other surface in the scene
                // gets. Without it a mark slides smoothly over a road that is
                // stepping, which reads as the mark floating above it.
                if (_PSXSnap > 0.5 && clipPos.w > 0.0)
                {
                    float2 grid = _ScreenParams.xy * 0.5;
                    float2 ndc = clipPos.xy / clipPos.w;
                    ndc = floor(ndc * grid + 0.5) / grid;
                    clipPos.xy = ndc * clipPos.w;
                }
                o.pos = clipPos;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Tint;

                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                float fogT = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                o.fog = pow(fogT, max(_PSXFogCurve, 1.0));

                // A uniform branch: only the smoke and the snow take it, and
                // only once an hour is applied (outdoors).
                float3 light = float3(1.0, 1.0, 1.0);
                if (_Lit > 0.5 && _PSXToneOn > 0.5)
                {
                    float3 wpos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                    float3 scene = _PSXAmbient.rgb + _PSXSkyAmbient.rgb * DECAL_SKY + _PSXLightColor.rgb * DECAL_SUN;
                    light = (scene + PSXLampsAt(wpos) + PSXHeadlightsAt(wpos)) * PSXExposureGain();
                }
                o.light = light;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = PSXMainTex(_MainTex, i.uv);
                float4 c = tex * i.color;
                c.rgb *= i.light;
                // (The haze is the sky's colour: adapted, never exposed nor
                // curved, C3; the one curve goes on the mark or the smoke.)
                c.rgb = lerp(PSXTone(c.rgb), PSXFogTone(_PSXFogColor.rgb), i.fog);
                return c;
            }
            ENDCG
        }
    }
}
