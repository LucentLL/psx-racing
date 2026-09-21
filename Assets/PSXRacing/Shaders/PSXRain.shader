// FALLING RAIN AND TYRE SPRAY, LIT BY WHAT IS AROUND IT.
//
// Until 2026-09-21 the rain was drawn with PSX/Decal, the skid-mark shader:
// unlit vertex colour, alpha-blended, tinted toward the fog. That made every
// drop the same pale blue at noon and at midnight - a grey screen door hung in
// front of a night city, brightest exactly where the scene was darkest - and a
// street lamp or a pair of headlights did nothing to the streaks under them.
// The owner's reference is Need for Speed (2015): "I like the particle effects
// on screen for rain and light." In those frames rain is INVISIBLE against the
// black and only exists where something lights it: an orange column under each
// sodium lamp, a white cone of streaks in front of every car, a red haze of
// spray behind the tail lamps. That is what water in the air actually does -
// a drop is a tiny lens that throws the light it is standing in back at you,
// and in the dark there is nothing to throw.
//
// So this shader is vertex-lit from the same tables the surfaces use:
//
//   light = RAIN_BASE * (ambient + sky ambient * RAIN_SKY + sun * RAIN_SUN)
//         + PSXLampsAt(wpos)      * RAIN_LAMP    street lamps + tail lamps
//         + PSXHeadlightsAt(wpos) * RAIN_HEAD    every car's low beam
//
// The *At variants are the lamp maths without the N.L term: a streak has no
// normal worth the name (it is a stretched billboard turned toward the eye),
// and a drop hanging in a beam is lit whichever way it faces. The base term is
// what keeps rain VISIBLE BY DAY (the owner signed off a rainy noon that has
// rain in it): the hour's ambient, half the sky's colour and a third of the sun
// scattered off the water. At night every one of those is near zero - the NFS
// pass made the night presets dark - so what is left is lamps and beams.
//
// WHY PER VERTEX. A streak is 3 cm wide and a few pixels long on a 240-line
// buffer; nothing inside one quad varies enough to earn a per-pixel loop, and
// 2200 particles x 4 corners x 20 lamp slots is cheaper in the vertex stage
// than the same loops over every covered pixel of 2200 overlapping sprites.
//
// BLENDING: One One, with the fragment already multiplied by the texture's
// alpha and the particle's alpha (premultiplied - alpha applied ONCE). The old
// SrcAlpha One shape the contract first sketched would square the falloff, the
// same trap PSX/Glow documents. Additive is the physics: light scattered off a
// drop ADDS to what is behind it, and a drop in the dark adds nothing, so there
// is no "rain colour" to pick and nothing that can go grey. The alpha channel
// is written as ZERO: additive light does not change coverage, and the mirror's
// render texture is shown through a RawImage that blends by that alpha.
//
// FOG is a FADE, not a tint (PSX/Glow's reasoning): adding fog colour to an
// additive pass would make distant rain BRIGHTER inside a daylight fog bank,
// which is backwards. The fade uses the same curved band as every surface
// (_PSXFogCurve), so a streak thins out exactly as the wall behind it does.
//
// Snow stays on PSX/Decal: a flake is white by DAY and reads as a white dot
// against a dark verge, which additive light cannot draw.
//
// Always-included (ProjectSettings/GraphicsSettings.asset): nothing on disk
// references this shader - WeatherFx and TireSpray build their materials with
// Shader.Find at runtime - and a shader reached only through Shader.Find is
// stripped from a WebGL build and comes back null.
Shader "PSX/Rain"
{
    Properties
    {
        _MainTex ("Texture (alpha = streak)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            // The two per-pixel light tables, read here per vertex. Both are
            // include-guarded, so a future shader that pulls in either one
            // first is still fine.
            #include "PSXHeadlights.cginc"
            #include "PSXLamps.cginc"

            // THE LOOK OF THE RAIN. Linear-light multipliers on each source.
            //
            // RAIN_BASE scales the whole scene-light term. At 1.0 a rainy noon
            // adds about 0.18 linear to the pixel under a full streak - about
            // what the old alpha-blended PSX/Decal drops did to a dark road -
            // so daytime rain looks as it did; at night the same term is a
            // hundredth of that and the streaks vanish unless something lights
            // them. Lower it to make daytime rain quieter without touching
            // the lamp columns.
            #define RAIN_BASE 1.0
            // How much of the sky's colour and the sun reach a drop. The sky
            // term is the sky ambient (the hour's own hue, see PSXGlobals
            // skyAmbient); the sun scatters off water in every direction, so a
            // third of it with no N.L.
            #define RAIN_SKY  0.5
            #define RAIN_SUN  0.35
            // Street lamps and tail lamps. Above 1 on purpose: a drop is a
            // retro-reflector compared to the matt tarmac the lamp tables were
            // tuned on, and the NFS frames show the column under a sodium head
            // brighter than the pool it throws on the road.
            #define RAIN_LAMP 1.4
            // Headlights. The beam in the rain is THE night-rain image - a cone
            // of white streaks ahead of every car - so it is lifted too, a
            // little less than the lamps because the beam's own intensity is
            // already higher at close range.
            #define RAIN_HEAD 1.2
            // THE DROP AT THE LENS. A streak is 2.8 cm wide and up to 1.5 m
            // long, drawn at whatever size the perspective makes it - so one
            // drop falling 40 cm in front of the camera became a single bright
            // COLUMN from the top of the frame to the bottom, several pixels
            // wide and as bright as the lamp it hung in (the night shots
            // caught one). No eye or lens sees that: a drop that close is
            // far out of focus, a smear too faint to register, and the drops
            // that ARE on the glass are PSX/Lens's job. So a streak fades in
            // over RAIN_NEAR_RAMP_M metres from RAIN_NEAR_M out: gone inside
            // 1.2 m, whole from 3.2 m, per vertex (a streak crossing the band
            // fades along its length). Beyond 3.2 m nothing changes. The same
            // shader draws the tyre SPRAY, whose 1.8 m puffs are left behind
            // by the car and can drift through a chase camera 5.4 m back -
            // the same fade stops one filling the screen as it passes the
            // lens, and leaves the cloud at the rear wheels (about 4 m from
            // that camera) whole.
            #define RAIN_NEAR_M      1.2
            #define RAIN_NEAR_RAMP_M 2.0

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Tint;

            // Scene globals pushed every frame by PSXGlobals. Declared float4,
            // not fixed4: the sun's linear colour x intensity passes 1.0 at
            // noon and lowp would clip it on a GLES device.
            float4 _PSXLightColor;
            float4 _PSXAmbient;
            // The ambient for faces that look UP, in the hour's sky hue.
            // Equal to _PSXAmbient in any scene that never applied an hour.
            float4 _PSXSkyAmbient;
            float _PSXFogNear;
            float _PSXFogFar;
            // Bends the band so it closes late instead of evenly; see
            // PSXGlobals.fogCurve. Floored at 1 below, so an unset global (0)
            // is the old straight ramp.
            float _PSXFogCurve;
            float _PSXSnap;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                // The particle's colour: rgb = the water's tint, a = opacity
                // (WeatherFx's start colour, TireSpray's colour over life).
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                // Light x tint x opacity x fog fade, already multiplied: the
                // fragment only applies the texture. A TEXCOORD, not COLOR0:
                // next to a lamp this passes 1.0, and COLOR0 is lowp on GLES.
                float3 col : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                // A ParticleSystem hands over world-space vertices with an
                // identity object matrix; going through it anyway keeps the
                // shader right for any mesh drawn with it.
                float3 wpos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));

                // The same NDC quantisation every other surface gets (off by
                // default, see the renderer-look memory).
                if (_PSXSnap > 0.5 && clipPos.w > 0.0)
                {
                    float2 grid = _ScreenParams.xy * 0.5;
                    float2 ndc = clipPos.xy / clipPos.w;
                    ndc = floor(ndc * grid + 0.5) / grid;
                    clipPos.xy = ndc * clipPos.w;
                }
                o.pos = clipPos;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float3 scene = _PSXAmbient.rgb
                             + _PSXSkyAmbient.rgb * RAIN_SKY
                             + _PSXLightColor.rgb * RAIN_SUN;
                float3 light = scene * RAIN_BASE
                             + PSXLampsAt(wpos) * RAIN_LAMP
                             + PSXHeadlightsAt(wpos) * RAIN_HEAD;

                // Fade into the fog band by view distance, on the same curve
                // the surfaces use.
                float dist = length(mul(UNITY_MATRIX_V, float4(wpos, 1.0)).xyz);
                float fogT = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                float fade = 1.0 - pow(fogT, max(_PSXFogCurve, 1.0));
                // ...and out of the way right at the lens (RAIN_NEAR_M).
                fade *= saturate((dist - RAIN_NEAR_M) / RAIN_NEAR_RAMP_M);

                o.col = light * v.color.rgb * _Tint.rgb * (v.color.a * _Tint.a * fade);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Premultiplied: the texture's alpha is the streak's shape and
                // is applied here, once. Alpha out is zero (see the header).
                float a = tex2D(_MainTex, i.uv).a;
                return fixed4(i.col * a, 0.0);
            }
            ENDCG
        }
    }
}
