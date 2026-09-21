// THE SHADOW MAPS, the caster side (the DAY PASS, 2026-09-21).
//
// SunShadows.cs draws every opaque thing near the camera once more - from the
// sun, and now and then from straight overhead - through this shader, into a
// square texture. What it writes is not a picture: each texel is HOW FAR ALONG
// THE RAY the first surface is, as a 16-bit number spread over two 8-bit
// channels. PSXSunShadow.cginc reads it back, and a pixel further along the
// ray than that is in something's shadow.
//
// Why a colour texture holding a packed number and not a depth buffer: this
// game ships as WebGL to phones, and an 8-bit RGBA target is the one format
// every one of them renders to and samples from. A float target needs an
// extension some do not have, and a sampled depth buffer comes with each
// platform's own idea of which way z runs. The number written here is OURS -
// the ray depth from SunShadows' own matrix, 0 at the box's near face and 1 at
// its far one on every platform - so reader and writer cannot disagree.
//
// TWO NUMBERS A TEXEL, because a wall and a tree are not the same kind of
// shade. A wall stops the sun. A tree's crown lets a third of it through in
// coins of light, and 17 cm texels under four overlapping cards cannot draw
// those coins - so the receiver is told "there are LEAVES between you and the
// sun" and draws the dapple itself. The first cut kept ONE depth and a flag
// saying which kind the first surface was, and its first picture had sunlight
// dappling the road INSIDE A TUNNEL: the first thing the sun met over that
// tunnel was the wood on the hill above it, so the map said "leaves" and never
// learned about the rock underneath. So:
//
//   pass 0, SOLID    the depth of the first solid surface   -> r, g
//   pass 1, FOLIAGE  the depth of the first cutout surface  -> b, a
//
// SunShadows draws every solid first and every cutout after, into the one
// target with its one depth buffer: a leaf behind a wall is discarded by the
// depth test (the wall's shadow has the last word anyway), a leaf in front of
// one is kept, and neither pass can touch the other's channels (ColorMask).
//
// The cutout pass is drawn with the SOURCE material's texture and cutoff
// (SunShadows copies them over), so a tree card casts the shape of its leaves
// rather than of the rectangle they are painted on. Cull Off, always: a
// caster's back face shadows as well as its front, the crossed tree cards
// have no back, and it spares the map caring which way the platform flipped
// it.
//
// 16 bits over the sun's 600 m column is 9 mm a step; the bias is 60.
Shader "PSX/ShadowCaster"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    fixed4 _Color;
    float _Cutoff;
    // World -> the map being drawn: xy the texel (0..1), z the ray depth
    // (0..1). SunShadows sets it per map (the sun's, then the sky's) on the
    // command buffer, ahead of that map's draws.
    float4x4 _PSXCasterMatrix;

    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float depth : TEXCOORD1;
    };

    v2f vert (appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        float4 w = mul(unity_ObjectToWorld, v.vertex);
        o.depth = mul(_PSXCasterMatrix, float4(w.xyz, 1.0)).z;
        return o;
    }

    // 16 bits over two 8-bit channels. Clamped just short of 1: frac() of
    // exactly 1 is 0, and the far face must not read as the near one.
    float2 PSXPackDepth(float depth)
    {
        float d = clamp(depth, 0.0, 0.99998);
        float2 enc = frac(d * float2(1.0, 255.0));
        enc.x -= enc.y / 255.0;
        return enc;
    }

    float4 fragSolid (v2f i) : SV_Target
    {
        return float4(PSXPackDepth(i.depth), 0.0, 0.0);
    }

    float4 fragFoliage (v2f i) : SV_Target
    {
        float a = tex2D(_MainTex, i.uv).a * _Color.a;
        clip(a - _Cutoff);
        return float4(0.0, 0.0, PSXPackDepth(i.depth));
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "SOLID"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off
            ColorMask RG
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragSolid
            ENDCG
        }
        Pass
        {
            Name "FOLIAGE"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off
            ColorMask BA
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragFoliage
            ENDCG
        }
    }
}
