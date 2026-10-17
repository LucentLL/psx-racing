// THE CABIN IN THE WINDSCREEN (2026-10-16), CockpitReflection.cs.
//
// Owner, on an FH6 cockpit clip: "I also noticed the reflection on the
// windshield of hands and steering wheel, nice." A second renderer on the
// windscreen's own mesh, one queue BELOW PSX/CabinGlass (the rain drops are
// drawn over it), reading the cabin-only planar reflection CockpitReflection
// renders: _ReflTex, looked up along the eye's ray by _ReflVP (world -> the
// reflection frame's clip space, GL convention).
//
// SCREEN-BLENDED, src x (1 - dst) + dst: the inside of the glass adds the
// lit cabin's light to whatever is seen through it, so the ghost is plain
// against dark trees and shade and all but gone against a bright sky -
// without a copy of the frame. Strength = min(_Peak, Fresnel x _Gain): a
// raked screen seen at a grazing angle reflects more. Four bilinear taps a
// texel apart soften it (it is a reflection in a pane, not a mirror).
// _Lift (< 1) raises the cabin's darks before that: our cabin sits in its
// car's shade and reads near black, where the sunlit dash and wheel of a
// real one are what the ghost is made of. The destination alpha (the
// emitter mask) is kept.
Shader "PSX/CabinGlassReflect"
{
    Properties
    {
        _ReflTex ("Reflection (CockpitReflection)", 2D) = "black" {}
        _Gain ("Fresnel gain", Float) = 1.6
        _Peak ("Peak strength", Float) = 0.18
        _Lift ("Dark lift (power)", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-1" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend OneMinusDstColor One, Zero One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PSXCameraRel.cginc"

            sampler2D _ReflTex;
            float4 _ReflTex_TexelSize;
            float4x4 _ReflVP;
            float _Gain, _Peak, _Lift;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wnrm : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = PSXObjectToClipRel(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 c = mul(_ReflVP, float4(i.wpos, 1.0));
                float2 uv = c.xy / max(c.w, 1e-5) * 0.5 + 0.5;
                float2 e = smoothstep(0.0, 0.04, uv) * smoothstep(0.0, 0.04, 1.0 - uv);
                float edge = e.x * e.y * step(0.0, c.w);
                float2 o = _ReflTex_TexelSize.xy * 0.9;
                float3 r = tex2D(_ReflTex, uv + float2(-o.x, -o.y)).rgb + tex2D(_ReflTex, uv + float2(o.x, -o.y)).rgb
                         + tex2D(_ReflTex, uv + float2(-o.x, o.y)).rgb + tex2D(_ReflTex, uv + o).rgb;
                r = pow(max(r * 0.25, 0.0), _Lift);
                float3 N = normalize(i.wnrm);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                float ndv = saturate(abs(dot(N, V)));
                float fres = 0.04 + 0.96 * pow(1.0 - ndv, 5.0);
                float k = min(_Peak, fres * _Gain) * edge;
                return fixed4(r * k, 0.0);
            }
            ENDCG
        }
    }
}
