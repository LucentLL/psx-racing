// THE HOUR'S SKY, AS A SURFACE SEES IT IN A MIRROR.
//
// This is the car paint's sky lookup (it was SkyIn() inside PSXCarPaint.shader
// from the third cut on), moved here VERBATIM on 2026-09-21 so that a second
// surface can reflect the same sky: the WET ROAD in PSX/Lit. The owner's ask
// for the night pass was Need for Speed (2015) - "how much the skybox effects
// the color and mood of the world and cars" - and in those frames the road is
// the biggest mirror on screen: a blue dusk sky lies on the tarmac, and in the
// city the sodium skyglow does. One function for both means the paint and the
// puddle it drives through can never disagree about what the sky looks like.
//
// VERBATIM means it. Not one operation of the body below changed in the move:
// the car paint's pixels are the owner's signed-off look (four cuts of it),
// and PSXCarPaint.shader now calls this through a one-line SkyIn() wrapper, so
// the compiled paint is the same arithmetic in the same order.
//
// What it does, in order: the hour's gradient (horizon to top, sharpened like
// PSXSky's), the equirect panorama through the same rotation and tint the sky
// material uses (TimeOfDay.ApplySky pushes the _PSXSky* globals), or - when a
// scene has no panorama - a hemisphere made from the ambient and sun that ARE
// set; then the haze toward the horizon; then THE LAND band (hillside, trees,
// walls: dark, fog-coloured, a crisp top at LAND_TOP); then the ground below
// the horizon.
//
// THE LAND BAND IS THE INCLUDER'S TO TUNE, and it has to be. The paint wants
// land up to ten degrees (LAND_TOP 0.17): a flank's grazing reflection is the
// dark roadside, which is what cured the white rim-light glow. A ROAD seen from
// a chase camera reflects at R.y of 0.03 to 0.15 - entirely inside that band -
// so with the paint's numbers every wet road in the game would mirror fog x 0.4
// instead of the sky, which is the opposite of the reference. Hence the four
// constants below are #ifndef-guarded: PSXCarPaint.shader defines its own (and
// must keep them in ITS file - LifeSimSelfTest reads them there with a regex),
// PSX/Lit defines a band that stops at the horizon. An includer that defines
// nothing gets the paint's values.
//
// UNIFORM RULES (a uniform declared twice is a compile error, and a global
// that appears in a Properties block reads the material's 0 instead):
//   * The _PSXSky* globals are declared HERE and nowhere else. An includer
//     must not redeclare them.
//   * _PSXFogColor, _PSXAmbient and _PSXLightColor are NOT declared here: every
//     PSX surface shader already declares them in its own uniform block, so
//     this file must be included AFTER that block (Lit and LitTransparent:
//     after the _PSXSnap line; CarPaint: where its _PSXSky* block used to be).
//   * UnityCG.cginc must come first (UNITY_PI).
//
// The panorama is read with tex2Dlod at the caller's blur: no derivatives, so
// it is safe inside any branch, on any GLES driver, and the equirect seam that
// PSXSky.shader has to fight with tex2Dgrad cannot happen here.
#ifndef PSX_SKY_REFLECT_INCLUDED
#define PSX_SKY_REFLECT_INCLUDED

// The car paint's world-in-the-lacquer band: sky above the land, land up to
// LAND_TOP (the sine of ten degrees), a dark road below the horizon. Defaults
// only - see the header for who overrides them and why.
#ifndef LAND_TOP
#define LAND_TOP           0.17
#endif
#ifndef LAND_SOFT
#define LAND_SOFT          0.045
#endif
#ifndef LAND_REFLECT
#define LAND_REFLECT       0.40   // how bright the land is in the reflection (x fog colour)
#endif
#ifndef GROUND_REFLECT
#define GROUND_REFLECT     0.25   // how bright the ground is in the reflection (x fog colour)
#endif

// The hour's sky, as TimeOfDay.ApplySky hands it to the sky
// material - see PSXSky.shader for what each one means there.
sampler2D _PSXSkyTex;
float _PSXSkyAmount;    // 0 = no panorama this scene
float _PSXSkyRotation;  // degrees
float _PSXSkyTint;      // 0..1, how hard the photo wears the hour
float _PSXSkyExposure;
fixed4 _PSXSkyTop;
fixed4 _PSXSkyHorizon;
float _PSXSkySharpness;

/// What the sky looks like in direction R - the same lookup and
/// the same hour tint PSXSky.shader applies, at a blur picked by
/// the caller (paint is a soft reflection, glass a sharper one).
float3 PSXSkyIn(float3 R, float lod)
{
    float y = R.y;
    float above = pow(saturate(y), 1.0 / max(_PSXSkySharpness, 0.5) * 4.0);
    float3 grad = lerp(_PSXSkyHorizon.rgb, _PSXSkyTop.rgb, above);
    float3 col;
    if (_PSXSkyAmount > 0.001)
    {
        float u = atan2(R.z, R.x) * (0.5 / UNITY_PI) + 0.5 + _PSXSkyRotation / 360.0;
        float v = 0.5 + asin(clamp(y, -1.0, 1.0)) / UNITY_PI;
        // Explicit LOD: no derivatives, so the equirect seam that
        // PSXSky has to fight with tex2Dgrad cannot happen here.
        float3 pano = tex2Dlod(_PSXSkyTex, float4(u, v, 0, lod)).rgb * _PSXSkyExposure;
        float3 tinted = pano * grad * 2.0;
        col = lerp(pano, tinted, _PSXSkyTint);
    }
    else
    {
        // No panorama: a hemisphere from the lighting that IS set.
        col = lerp(_PSXAmbient.rgb * 1.2, saturate(_PSXAmbient.rgb + _PSXLightColor.rgb * 0.5),
                   saturate(y * 1.5 + 0.3));
    }
    // Haze toward the horizon, the way the sky's own horizon
    // fades into the fog colour...
    float hz = saturate(1.0 - abs(y) * 6.0);
    col = lerp(col, _PSXFogColor.rgb, hz * hz * 0.6);
    // ...then THE LAND. A road is not an ocean: from the horizon
    // up to about ten degrees, what a panel mirrors is hillside,
    // trees and walls, not sky - dark, in the hour's haze colour,
    // with a crisp top. This is what keeps a flank's grazing
    // reflection from being a white rim, and what draws the
    // bright line along the shoulder of the body above it.
    float3 land = _PSXFogColor.rgb * LAND_REFLECT;
    col = lerp(land, col, smoothstep(LAND_TOP - LAND_SOFT, LAND_TOP + LAND_SOFT, y));
    // ...and below the horizon the reflection is the road: DARK,
    // the way the reference games' environment maps were.
    col = lerp(col, _PSXFogColor.rgb * GROUND_REFLECT, saturate(-y * 4.0));
    return col;
}
#endif
