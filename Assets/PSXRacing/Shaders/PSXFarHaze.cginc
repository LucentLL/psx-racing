// THE FAR HAZE v2 (owner, 2026-11-23, PC60, Bat Cave under a rain sky: "I'm
// not opposed to distant foggy objects...but they shouldn't be glowing white
// like a white dwarf star").
//
// What made it white: the fog colour is a TABLE colour (TimeOfDay: in rain the
// hour's skyHorizon, greyed but at clear-sky brightness; in Fog the hour's
// fogColor), while the sky drawn behind it is the panorama (or the dynamic
// sky's clouds) through the weather's exposure (Seasons.SkyMul: rain x 0.60).
// A far ridge past the detail's edge took the table colour whole: (221,231,237)
// against a (117,120,131) sky in his frame. Nothing tied the two but the
// author's eye at clear noon.
//
// THE RULE NOW, for everything that fades with distance: its fog colour is
// never brighter (luminance) than _PSXFarHaze.y x the sky directly behind it -
// the dome as PSX/Sky draws it at that bearing, sampled a few degrees up (w),
// photograph or dynamic map, exposure, tint and the unlit night's keep
// included. The hue stays the fog's (the horizon ring's); only too much light
// is taken out. Where the fog is already darker than its sky (a clear day, the
// city's murk) nothing changes. PSX/Sky's horizon band takes the same cap
// against the same sky row, so land, band and dome are one paint.
//
// _PSXFarHaze (TimeOfDay.FarHazeVector, every hour applied): x 1 = on
// (PSX_FARHAZE2=0: 0, the old picture exactly), y the cap (1 in overcast, rain,
// fog, snow, dawn, dusk and night; a little over 1 for clear-air daylight),
// z the sky map's LOD (a ~256-texel-wide mip of either map), w the sine of the
// lowest elevation the sky is read at (under it a panorama is ground or its
// photographed horizon). Zero in any scene that never applied an hour.
//
// Needs, before the include: _PSXFogColor, and (for PSXFarHazeRing) PSXSunShadow's
// PSXFogTowardSun and PSXTone's PSXNightSkyKeep. PSX/Lit and PSX/Water bring
// PSXSkyReflect.cginc's sky globals; elsewhere they are declared here, all but
// _PSXSkyRotation, which the includer declares (PSX/LitTransparent has its own).
#ifndef PSX_FARHAZE_INCLUDED
#define PSX_FARHAZE_INCLUDED

float4 _PSXFarHaze;

#ifndef PSX_SKY_REFLECT_INCLUDED
sampler2D _PSXSkyTex;
float _PSXSkyAmount;
float _PSXSkyTint;
float _PSXSkyExposure;
fixed4 _PSXSkyTop;
fixed4 _PSXSkyHorizon;
float _PSXSkySharpness;
#endif

#define PSX_FARHAZE_LUM float3(0.2126, 0.7152, 0.0722)

/// The dome PSX/Sky draws at this bearing, read no lower than _PSXFarHaze.w:
/// PSXSkyIn's lookup without its land and ground bands.
float3 PSXSkyBehind(float3 dir)
{
    float2 flat = dir.xz;
    float fl = length(flat);
    flat = fl > 1e-5 ? flat / fl : float2(1.0, 0.0);
    float y = clamp(dir.y / max(length(dir), 1e-5), _PSXFarHaze.w, 0.6);
    float3 R = float3(flat.x * sqrt(1.0 - y * y), y, flat.y * sqrt(1.0 - y * y));
    float above = pow(saturate(y), 1.0 / max(_PSXSkySharpness, 0.5) * 4.0);
    float3 grad = lerp(_PSXSkyHorizon.rgb, _PSXSkyTop.rgb, above);
    float3 col = grad;
    if (_PSXSkyAmount > 0.001)
    {
        float u = atan2(R.z, R.x) * (0.5 / UNITY_PI) + 0.5 + _PSXSkyRotation / 360.0;
        float v = 0.5 + asin(y) / UNITY_PI;
        float3 pano = tex2Dlod(_PSXSkyTex, float4(u, v, 0, _PSXFarHaze.z)).rgb * _PSXSkyExposure;
        col = lerp(pano, pano * grad * 2.0, _PSXSkyTint);
    }
    return col * PSXNightSkyKeep();
}

/// How much of a colour may stay so its luminance is no more than the cap
/// times the sky's (1 = all of it). Both sides carry the same added glow.
float PSXFarHazeKeep(float lumFog, float lumSky, float lumGlow)
{
    float room = _PSXFarHaze.y * (lumSky + lumGlow) - lumGlow;
    return saturate(max(room, 0.0) / max(lumFog, 1e-4));
}

/// The share of a fog colour that may stay (no glow term): for the shaders
/// that fade to _PSXFogColor alone (PSX/Decal's marks and smoke).
float PSXFarHazeScale(float3 fogCol, float3 eyeToPoint, float amount)
{
    if (_PSXFarHaze.x < 0.5 || amount <= 0.001) return 1.0;
    return PSXFarHazeKeep(dot(fogCol, PSX_FARHAZE_LUM), dot(PSXSkyBehind(eyeToPoint), PSX_FARHAZE_LUM), 0.0);
}

#ifdef PSX_FARHAZE_FOG
/// A fogged vertex's ring ratio, scaled down so _PSXFogColor x ring (plus the
/// haze's glow toward the sun) is never brighter than the sky behind it.
/// eyeToPoint = world position - camera; amount = how fogged it is (0 skips).
/// alsoCol = a second colour the same ring tints (the blue distance), or 0.
half3 PSXFarHazeRing(half3 ring, float3 eyeToPoint, float amount, float3 alsoCol)
{
    if (_PSXFarHaze.x < 0.5 || amount <= 0.001) return ring;
    float3 V = -normalize(eyeToPoint);
    float lg = dot(PSXFogTowardSun(float3(0, 0, 0), V), PSX_FARHAZE_LUM);
    float lf = max(dot(_PSXFogColor.rgb * ring, PSX_FARHAZE_LUM), dot(alsoCol * ring, PSX_FARHAZE_LUM));
    float ls = dot(PSXSkyBehind(eyeToPoint), PSX_FARHAZE_LUM);
    return ring * (half)PSXFarHazeKeep(lf, ls, lg);
}
#endif
#endif
