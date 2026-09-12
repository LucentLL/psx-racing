// THE LIGHT IN THE AIR IN FRONT OF A HEADLAMP.
//
// PSXHeadlights.cginc lights the SURFACES a beam falls on. This is the beam
// itself: the cone of lit air between the lens and the road that a night
// drive is mostly made of. Drawn the way the era drew it - Silent Hill's
// torch, Driver's headlights - as an additive cone mesh from the lens,
// fading along its length and thinning toward its silhouette, both walls
// adding (Cull Off) so the centre, where a view ray passes through the most
// lit air, is the brightest part and the edge the faintest. CarLights builds
// the cone (a flattened ellipse, wider than it is tall, like the beam) and
// aims it with the lamp.
//
// It is only visible when it is dark. Additive light in the air is invisible
// under a noon sun and there is no point paying for it; `dark` fades the cone
// out against the hour's ambient-plus-sun so that dawn and sunset show the
// lens sprites and the beam on the road but only dusk and night show the
// volume. It fades into the fog band the way PSX/Glow does, for the same
// reason: adding fog colour to an additive pass would make distant lamps
// brighter in daylight haze, which is backwards.
Shader "PSX/Beam"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 0.86, 0.62, 1)
        _Strength ("Strength", Range(0, 2)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Strength;

            fixed4 _PSXAmbient;
            fixed4 _PSXLightColor;
            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wnrm : TEXCOORD1;
                float3 wpos : TEXCOORD2;
                fixed fade : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wnrm = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fade = 1.0 - saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // v runs 0 at the lens to 1 at the far end of the cone.
                float t = saturate(i.uv.y);
                float along = (1.0 - t) * (1.0 - t);
                float3 N = normalize(i.wnrm);
                float3 toCam = _WorldSpaceCameraPos - i.wpos;
                float viewDist = length(toCam);
                float3 V = toCam / max(viewDist, 1e-3);
                // Thick through the middle, nothing at the silhouette. The
                // first cut had a floor under this and a gentler power, and
                // came back as two hard-edged paper wedges: a cone's outline
                // has to fade to zero or it is a shape, not a volume.
                float thick = pow(abs(dot(N, V)), 1.4);
                // A camera INSIDE the cone (the bumper and cockpit views, or
                // a car passing through another's beam) must not see a wall
                // of light across the frame; the part of the cone within a
                // few metres of the eye fades out.
                float near = saturate((viewDist - 1.0) / 4.0);
                float bright = dot(_PSXAmbient.rgb + _PSXLightColor.rgb, float3(0.30, 0.59, 0.11));
                float dark = saturate(1.15 - bright * 1.2);
                float a = along * thick * _Strength * dark * i.fade * near;
                return fixed4(_Color.rgb * a, a);
            }
            ENDCG
        }
    }
}
