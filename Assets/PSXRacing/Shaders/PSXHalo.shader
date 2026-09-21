// THE GLOW OF A LAMP HEAD, SEEN FROM ANYWHERE.
//
// PSXLamps.cginc lights the road UNDER a street lamp. This is the lamp itself
// as the eye sees it at night: a hot core where the bulb is and a soft halo
// of lit mist around it - the string of orange beads running up a boulevard
// that half of any Need for Speed night frame is made of, and the thing the
// owner meant by "light" in "the particle effects on screen for rain and
// light". It replaced a 2.2 m additive quad laid FLAT under each head: seen
// from the driver's seat, a flat quad eight metres up is a sliver edge-on,
// and a lamp fifty metres away was not there at all.
//
// HOW IT IS DRAWN. NightGlow merges every lamp head of a scene (a circuit's
// street lamps, a city tile's) into ONE mesh: four vertices per halo, all
// four AT the head's centre, told apart by uv0 (the corner) - so a hundred
// lamps are one draw call, and the quad is built here, in view space, where
// it can face the camera exactly:
//   * the centre is pulled 0.6 m toward the eye, so the lamp's own head box
//     (drawn with depth) does not eat the half of the halo behind its face;
//   * the corner goes out by max(_Size x uv1.x, the size that keeps the
//     quad's half-width _MinPixels framebuffer pixels at that depth) - a lamp
//     four hundred metres off is still a spark, never a sub-pixel that
//     twinkles in and out as the camera moves;
//   * the size grows with the global wetness (x 1.5 at 1): rain and night
//     damp put mist round every lamp, and a lamp in the rain is bigger.
//
// It FADES SLOWER THAN THE FOG. Glow/Beam fade out across the fog band; a
// halo stays whole to the fog's far end and is gone by 2.2 times it (never
// cut by the far plane: 92% of it at most). A lamp punches through night
// haze - that is the whole reason a distant street reads as a street at
// night - and a halo that faded with the fog would put the horizon out.
//
// Additive (One One) with the fragment already multiplied by its own
// profile, like PSX/Glow: SrcAlpha One would apply the profile twice and
// square the falloff into a hard dot. The halo is WINDOWED to exactly zero
// at the quad's edge: the framebuffer is 8-bit and the colour space linear,
// so a stray 0.004 of linear light at a corner is 13/255 once encoded, and
// every lamp would wear a faint square.
//
// ZWrite Off with the depth TEST on: a halo behind a building is hidden by
// it, and one in front of a car never punches a hole in the car.
//
// The globals (_PSXWetness, _PSXFogFar) are uniforms ONLY, never Properties:
// a property of the same name would shadow the global and read 0.
//
// PSX/Halo is created at runtime only (NightGlow: Shader.Find), never by a
// saved .mat, so it is in ProjectSettings' Always Included Shaders or a
// WebGL build would not have it.
Shader "PSX/Halo"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 0.72, 0.40, 1)
        _Strength ("Strength", Range(0, 4)) = 1
        _Size ("Halo half-size (m)", Float) = 2.6
        _HaloAmt ("Soft halo amount", Range(0, 1)) = 0.35
        _MinPixels ("Minimum size (framebuffer px)", Float) = 1.5
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

            // THE SHAPE. A tight core (the bulb and its refractor) and a wide
            // soft halo (the lit mist), both gaussians in the quad's own
            // radius r (0 at the centre, 1 at the edge midpoints).
            #define HALO_CORE_K   40.0   // core: exp(-r^2 * 40), e^-1 at r = 0.16
            #define HALO_SOFT_K   4.5    // halo: exp(-r^2 * 4.5), e^-1 at r = 0.47
            // How far the centre comes toward the eye, metres.
            #define HALO_PULL     0.6
            // Mist: size multiplier per unit of global wetness.
            #define HALO_WET_GROW 0.5
            // Distance fade: whole to min(fogFar, HALO_FULL x end), gone by
            // end = min(fogFar x HALO_FAR, far plane x HALO_CLIP).
            #define HALO_FAR      2.2
            #define HALO_FULL     0.6
            #define HALO_CLIP     0.92

            float4 _Color;
            float _Strength;
            float _Size;
            float _HaloAmt;
            float _MinPixels;

            float _PSXWetness;
            float _PSXFogFar;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;     // the corner: (0,0) (1,0) (1,1) (0,1)
                float2 uv1 : TEXCOORD1;    // x = this halo's size multiplier
                float4 color : COLOR;      // white; a per-halo tint if ever wanted
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 q : TEXCOORD0;      // -1..1 across the quad
                float4 col : TEXCOORD1;    // rgb = tint x strength x fade
            };

            v2f vert (appdata v)
            {
                v2f o;
                // All four corners share the centre; the quad is made here.
                float4 wc = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
                float3 vc = mul(UNITY_MATRIX_V, wc).xyz;
                float dist = length(vc);

                // Toward the eye, but never through it.
                float pull = min(HALO_PULL, dist * 0.5);
                vc -= vc * (pull / max(dist, 1e-3));

                // The smallest half-size that still reaches _MinPixels of the
                // framebuffer out from the centre at this depth (a quad twice
                // that across): a half-size h at depth z spans
                // h * |P11| * H / (2z) pixels. |P11| because it is negative
                // when rendering into a texture on D3D. An orthographic camera
                // has no depth in that sum.
                float depth = max(-vc.z, 1e-3);
                float zScale = lerp(depth, 1.0, unity_OrthoParams.w);
                float minM = _MinPixels * 2.0 * zScale
                           / max(_ScreenParams.y * abs(UNITY_MATRIX_P[1][1]), 1e-3);
                float size = max(_Size * v.uv1.x * (1.0 + HALO_WET_GROW * saturate(_PSXWetness)), minM);

                float2 q = v.uv * 2.0 - 1.0;
                vc.xy += q * size;
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1.0));
                o.q = q;

                // Slower than the fog, never clipped by the far plane.
                float end = min(_PSXFogFar * HALO_FAR, _ProjectionParams.z * HALO_CLIP);
                float start = min(_PSXFogFar, HALO_FULL * end);
                float fade = 1.0 - saturate((dist - start) / max(end - start, 1.0));

                o.col = float4(_Color.rgb * v.color.rgb * (_Strength * fade), fade);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float r2 = dot(i.q, i.q);
                // Windowed to exactly zero at r = 1 (see the header: an 8-bit
                // linear framebuffer shows a 0.004 corner as a square).
                float win = saturate(1.0 - r2);
                win *= win;
                float a = exp(-r2 * HALO_CORE_K) + exp(-r2 * HALO_SOFT_K) * _HaloAmt * win;
                return fixed4(i.col.rgb * a, saturate(a * i.col.a));
            }
            ENDCG
        }
    }
}
