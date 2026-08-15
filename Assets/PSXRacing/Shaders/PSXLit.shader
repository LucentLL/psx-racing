// PSX-era surface shader: per-vertex lighting, vertex snapping to the
// low-res grid, affine texture mapping, and manual linear fog.
// Renders via the SRPDefaultUnlit pass so it works under URP.
//
// Global uniforms driven by PSXGlobals.cs:
//   _PSXLightDir, _PSXLightColor, _PSXAmbient,
//   _PSXFogColor, _PSXFogNear, _PSXFogFar, _PSXSnap
Shader "PSX/Lit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0
        _Emission ("Emission", Range(0,1)) = 0
        // 1 = full PS1 affine texture warping, 0 = perspective correct.
        // Warping scales with triangle size, so huge surfaces like the ground
        // plane need to opt out or the texture visibly swims near the camera.
        _Affine ("Affine Warping", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
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
                // Affine mapping is emulated rather than declared: interpolation
                // qualifiers are compile-time, so `noperspective` could not be
                // varied per material. Premultiplying by w and dividing it back
                // out in the fragment gives the same warp, continuously dialled.
                float3 uvw : TEXCOORD0;
                fixed4 light : COLOR0;
                fixed fog : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 clipPos = UnityObjectToClipPos(v.vertex);

                // Vertex snapping: quantize NDC xy to the render target grid.
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

                // Per-vertex diffuse (half-lambert-ish so shaded sides stay readable).
                // Guarded: a zero-length normal makes normalize() produce NaN, and
                // GLSL ES leaves clamp(NaN) undefined, so a WebGL driver that
                // propagates it renders garbage rather than merely flat shading.
                float3 rawN = UnityObjectToWorldNormal(v.normal);
                float nl2 = dot(rawN, rawN);
                float3 n = nl2 > 1e-8 ? rawN * rsqrt(nl2) : float3(0, 1, 0);
                float ndl = saturate(dot(n, normalize(_PSXLightDir.xyz)));
                fixed3 lighting = _PSXAmbient.rgb + _PSXLightColor.rgb * ndl;
                o.light = fixed4(saturate(lighting), 1);

                // Manual linear fog by view distance
                float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
                o.fog = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uvw.xy / i.uvw.z) * _Color;
                clip(tex.a - _Cutoff);
                fixed3 lit = tex.rgb * lerp(i.light.rgb, fixed3(1,1,1), _Emission);
                fixed3 col = lerp(lit, _PSXFogColor.rgb, i.fog);
                return fixed4(col, tex.a);
            }
            ENDCG
        }
    }
}
