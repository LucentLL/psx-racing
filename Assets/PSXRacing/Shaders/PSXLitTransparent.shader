// PSX/Lit, alpha-blended. The one surface in this game that has to be SEEN
// THROUGH: shop windows.
//
// A sibling rather than a keyword variant of PSX/Lit, for the reason every
// other shader in this folder is one: PSX/Lit is Blend-off with ZWrite on and
// a clip(), and blending is a per-PASS state that cannot be branched at
// runtime. The vertex snap, the affine premultiply, the guarded per-vertex
// lambert and the manual fog are therefore copied here byte for byte — which
// is the house style in this folder (PSX/Decal keeps its own copy of the snap
// and the fog), not new debt.
//
// Three differences from PSX/Lit and no others:
//   * Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off, Queue Transparent.
//   * no clip() — a blended pass has nothing to cut out, and _Cutoff at 0
//     would cut nothing anyway. The property stays declared so the material
//     factory's unconditional SetFloat("_Cutoff", ...) is not a warning.
//   * Cull Back is KEPT. The pack's panes are single quads; double-siding
//     them doubles the tint wherever the far pane shows through the near one.
//
// The opacity knob is _Color.a and needs no new property: PSX/Lit already
// multiplies _Color into the sample and already returns tex.a, so the alpha
// was there all along with nothing reading it.
//
// Fog is applied BEFORE the alpha, deliberately — fogged glass has to become
// fog colour like every other surface, or a distant window reads as a hole
// punched through the haze.
Shader "PSX/LitTransparent"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0
        _Emission ("Emission", Range(0,1)) = 0
        _Affine ("Affine Warping", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;
            float _Emission;
            float _Affine;

            float4 _PSXLightDir;    // xyz = direction TO light (world)
            fixed4 _PSXLightColor;
            fixed4 _PSXAmbient;
            fixed4 _PSXFogColor;
            float _PSXFogNear;
            float _PSXFogFar;
            float _PSXSnap;         // 1 = vertex snapping on

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uvw : TEXCOORD0;
                fixed4 light : COLOR0;
                fixed fog : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 clipPos = UnityObjectToClipPos(v.vertex);

                if (_PSXSnap > 0.5 && clipPos.w > 0.0)
                {
                    float2 grid = _ScreenParams.xy * 0.5;
                    float2 ndc = clipPos.xy / clipPos.w;
                    ndc = floor(ndc * grid + 0.5) / grid;
                    clipPos.xy = ndc * clipPos.w;
                }
                o.pos = clipPos;
                float k = lerp(1.0, max(clipPos.w, 1e-4), _Affine);
                o.uvw = float3(TRANSFORM_TEX(v.uv, _MainTex) * k, k);

                float3 rawN = UnityObjectToWorldNormal(v.normal);
                float nl2 = dot(rawN, rawN);
                float3 n = nl2 > 1e-8 ? rawN * rsqrt(nl2) : float3(0, 1, 0);
                float ndl = saturate(dot(n, normalize(_PSXLightDir.xyz)));
                fixed3 lighting = _PSXAmbient.rgb + _PSXLightColor.rgb * ndl;
                o.light = fixed4(saturate(lighting), 1);

                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fog = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uvw.xy / i.uvw.z) * _Color;
                fixed3 lit = tex.rgb * lerp(i.light.rgb, fixed3(1,1,1), _Emission);
                fixed3 col = lerp(lit, _PSXFogColor.rgb, i.fog);
                // Fog also closes the glass: at full fog a window is as opaque
                // as the wall beside it, because both are simply haze by then.
                return fixed4(col, lerp(tex.a, 1.0, i.fog));
            }
            ENDCG
        }
    }
}
