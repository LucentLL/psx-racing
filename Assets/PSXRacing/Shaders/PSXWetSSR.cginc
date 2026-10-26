// THE WORLD IN THE WATER (2026-10-17): PSX/Lit's second pass, "PSXWetSSR".
//
// The wet road's first pass mirrors the hour's sky - right from every pixel,
// the sky being infinitely far - and nothing else. Houses, trees and cars are
// near, so what a road pixel mirrors depends on WHERE it is: the one view the
// car paint uses (CarReflections, from the player's roof) slid houses across
// the road (owner, 2026-10-17). The one picture that already holds the world
// seen from the camera is the frame itself, so this pass reads it: screen-
// space reflections, for wet road pixels only.
//
// SpeedBlurFeature's WetPass, after the opaques and the sky and before the
// transparents, copies the frame (_PSXSSRColour) and draws the WET renderers
// again (WetReflections tags them with a rendering layer) through this pass,
// with the camera's own depth attachment bound as _PSXSSRDepth. Per pixel:
//   1 there is no hardware depth test (the depth buffer is being READ), so a
//     fragment goes on only if the depth buffer agrees it is what the camera
//     sees here - a car or a wall in front of the road discards it;
//   2 how wet, how much mirror and how much fog: PSXWet.cginc's numbers, the
//     very ones the first pass used;
//   3 the mirrored ray marched across the screen against the depth buffer;
//   4 where it meets something, the sky the first pass laid down is traded
//     for what it met: colour = frame + (met - sky) x mirror x (1 - fog) x
//     confidence. Where it meets nothing (it left the screen, or it reached
//     the sky), the fragment is discarded and the sky stays.
// So without this pass - a phone, REFLECTIONS off, any other camera - the
// picture is the first pass's alone, exactly as before.
#ifndef PSX_WET_SSR_INCLUDED
#define PSX_WET_SSR_INCLUDED

#include "UnityCG.cginc"
#include "PSXTone.cginc"

float _Wet;
float _Cull;
fixed4 _PSXLightColor;
fixed4 _PSXAmbient;
fixed4 _PSXFogColor;
float _PSXFogNear;
float _PSXFogFar;
float _PSXFogCurve;
float _PSXWetness;
#include "PSXWet.cginc"

sampler2D _PSXSSRColour;   // the frame so far (opaques + sky): what the water can mirror
sampler2D _PSXSSRDepth;    // the camera's own depth attachment
float4 _PSXSSRParams;      // x: strength (1 in the game; tools may turn it), yzw unused

#define SSR_STEPS        24      // march steps, evenly along the ray's line ON THE SCREEN
#define SSR_REFINE       5       // halvings of the step that crossed behind a surface
#define SSR_START_M      0.10    // the march starts this far up the ray: the road's own depth is no hit
#define SSR_MAX_M        250.0   // how far up the ray the water looks
#define SSR_FADE_FROM    0.70    // ...fading out from this share of it
#define SSR_THICK_M      0.40    // a surface the ray passed behind counts as met within this...
#define SSR_THICK_K      0.02    //   ...plus this share of its distance
#define SSR_EDGE         0.08    // at the screen's edges the mirror fades out over this share of the frame
#define SSR_SELF_K       0.004   // this fragment IS what the camera sees when the depth agrees within this share...
#define SSR_SELF_M       0.05    //   ...plus this many metres
#define SSR_MIN_K        0.004   // below this much mirror nothing is worth marching for

struct appdata_ssr
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
};

struct v2f_ssr
{
    float4 pos : SV_POSITION;
    float3 wpos : TEXCOORD0;
    float3 wnrm : TEXCOORD1;
    float fog : TEXCOORD2;
};

v2f_ssr vertSSR(appdata_ssr v)
{
    v2f_ssr o;
    o.pos = UnityObjectToClipPos(v.vertex);
    float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
    // the first pass's normal, guarded and turned to the side seen
    float3 rawN = UnityObjectToWorldNormal(v.normal);
    float nl2 = dot(rawN, rawN);
    float3 n = nl2 > 1e-8 ? rawN * rsqrt(nl2) : float3(0, 1, 0);
    if (_Cull < 0.5 && dot(n, _WorldSpaceCameraPos - wpos) < 0.0) n = -n;
    o.wpos = wpos;
    o.wnrm = n;
    // the first pass's fog, by view distance
    float dist = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
    float fogT = saturate((dist - _PSXFogNear) / max(_PSXFogFar - _PSXFogNear, 1.0));
    o.fog = pow(fogT, max(_PSXFogCurve, 1.0));
    // A dry material this hour (a tagged renderer's grass beside its
    // pavement, or every surface on a dry day): every vertex outside the
    // clip volume, so no triangle reaches the rasterizer.
    if (_Wet * _PSXWetness <= WET_MIN) o.pos = float4(-2.0, -2.0, -2.0, 1.0);
    return o;
}

// eye depth (metres along the view) of what the camera sees at uv
float SSRSeenZ(float2 uv)
{
    return LinearEyeDepth(tex2Dlod(_PSXSSRDepth, float4(uv, 0.0, 0.0)).r);
}

// NDC (as the projection left it) to the frame's uv - ComputeScreenPos's flip
float2 SSRUV(float2 ndc)
{
    return float2(ndc.x, ndc.y * _ProjectionParams.x) * 0.5 + 0.5;
}

// March the ray from wpos along R across the screen. Returns the confidence
// of a hit (0 = none: off the screen, into the sky, past SSR_MAX_M) and its uv.
float SSRMarch(float3 wpos, float3 R, float2 fragXY, out float2 hitUV)
{
    hitUV = float2(0.0, 0.0);
    // view space: the camera looks down -z, and a perspective clip w is the eye depth
    float3 p0 = mul(UNITY_MATRIX_V, float4(wpos + R * SSR_START_M, 1.0)).xyz;
    float3 d = mul((float3x3)UNITY_MATRIX_V, R);
    float len = SSR_MAX_M;
    // a ray toward the lens stops short of the near plane
    float nearZ = -_ProjectionParams.y * 1.5;
    if (d.z > 1e-4) len = min(len, (nearZ - p0.z) / d.z);
    if (len <= 0.0) return 0.0;
    float3 p1 = p0 + d * len;
    float4 h0 = mul(UNITY_MATRIX_P, float4(p0, 1.0));
    float4 h1 = mul(UNITY_MATRIX_P, float4(p1, 1.0));
    float k0 = 1.0 / h0.w, k1 = 1.0 / h1.w;
    float2 s0 = h0.xy * k0, s1 = h1.xy * k1;
    // Only the part of the line ON the screen is marched. Steps are even on
    // the screen; the depth along them is 1 / lerp(1/w), perspective-correct.
    float2 ds = s1 - s0;
    float tEnd = 1.0;
    if (ds.x > 1e-6) tEnd = min(tEnd, (1.0 - s0.x) / ds.x);
    else if (ds.x < -1e-6) tEnd = min(tEnd, (-1.0 - s0.x) / ds.x);
    if (ds.y > 1e-6) tEnd = min(tEnd, (1.0 - s0.y) / ds.y);
    else if (ds.y < -1e-6) tEnd = min(tEnd, (-1.0 - s0.y) / ds.y);
    if (tEnd <= 0.0) return 0.0;
    // interleaved gradient noise: each pixel starts its steps a little
    // further along, so the steps do not band
    float jitter = frac(52.9829189 * frac(dot(fragXY, float2(0.06711056, 0.00583715))));
    float dt = tEnd / SSR_STEPS;
    float farZ = _ProjectionParams.z * 0.999;
    float tPrev = 0.0, zPrev = 1.0 / k0, tHit = -1.0;
    [loop]
    for (int s = 0; s < SSR_STEPS; s++)
    {
        float t = dt * (s + jitter);
        float rayZ = 1.0 / lerp(k0, k1, t);
        float seenZ = SSRSeenZ(SSRUV(lerp(s0, s1, t)));
        // MET: the ray is now behind what the camera sees there, and that
        // surface lies within the depths the ray swept on this step (plus its
        // thickness). A ray that slipped BEHIND a nearer thing - a car, a
        // trunk - swept depths far past it and is not stopped by it.
        if (rayZ >= seenZ && seenZ < farZ
            && seenZ >= min(zPrev, rayZ) - (SSR_THICK_M + SSR_THICK_K * seenZ))
        {
            tHit = t;
            break;
        }
        tPrev = t;
        zPrev = rayZ;
    }
    if (tHit < 0.0) return 0.0;
    // the crossing, by halving the step that found it
    float lo = tPrev, hi = tHit;
    [unroll]
    for (int r = 0; r < SSR_REFINE; r++)
    {
        float m = 0.5 * (lo + hi);
        if (1.0 / lerp(k0, k1, m) >= SSRSeenZ(SSRUV(lerp(s0, s1, m)))) hi = m; else lo = m;
    }
    hitUV = SSRUV(lerp(s0, s1, hi));
    // fade at the screen's edges, where the next pixel out is unknown...
    float2 e = min(hitUV, 1.0 - hitUV);
    float conf = saturate(min(e.x, e.y) / SSR_EDGE);
    // ...and toward the far end of the ray (world distance along it)
    float u = hi * k1 / lerp(k0, k1, hi);
    conf *= 1.0 - smoothstep(SSR_FADE_FROM, 1.0, u * len / SSR_MAX_M);
    return conf;
}

fixed4 fragSSR(v2f_ssr i) : SV_Target
{
    // this pixel in the frame's copies
    float2 pix = i.pos.xy / _ScreenParams.xy;
    // 1 - the depth test the hardware is not doing
    float ownZ = -mul(UNITY_MATRIX_V, float4(i.wpos, 1.0)).z;
    if (ownZ > SSRSeenZ(pix) * (1.0 + SSR_SELF_K) + SSR_SELF_M) discard;

    // 2 - how much of this pixel is mirror
    float nl2 = dot(i.wnrm, i.wnrm);
    float3 N = nl2 > 1e-8 ? i.wnrm * rsqrt(nl2) : float3(0, 1, 0);
    float3 toEye = _WorldSpaceCameraPos - i.wpos;
    float eyeDist = length(toEye);
    float3 V = toEye / max(eyeDist, 1e-4);
    float w = PSXWetAmount(_Wet * _PSXWetness, N, i.wpos, eyeDist) * PSXWetGloss(i.wpos);
    float k =PSXWetFresnel(N, V) * w * (1.0 - i.fog) * _PSXSSRParams.x;
    if (k < SSR_MIN_K) discard;

    // 3 - what the mirrored ray meets
    float3 R = reflect(-V, N);
    float2 hitUV;
    float conf = SSRMarch(i.wpos, R, i.pos.xy, hitUV);
    if (conf <= 0.0) discard;

    // 4 - the world for the sky, as far as the march is sure of it. The
    // frame's alpha is the emitter mask (PSXTone.cginc): a lit window met in
    // the water is a light in the water.
    fixed4 base = tex2Dlod(_PSXSSRColour, float4(pix, 0.0, 0.0));
    fixed4 met = tex2Dlod(_PSXSSRColour, float4(hitUV, 0.0, 0.0));
    float3 sky = PSXWetSky(R, PSXAdaptGain());
    float kk = k * conf;
    float3 col = max(base.rgb + (met.rgb - sky) * kk, 0.0);
    return fixed4(col, saturate(base.a + met.a * kk));
}

#endif
